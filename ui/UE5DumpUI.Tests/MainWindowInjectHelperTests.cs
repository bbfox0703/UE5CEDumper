using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// MainWindowViewModel.InjectCeHelperLuaCommand wiring tests.
///
/// The command is a thin orchestrator: probe AOBMaker availability ->
/// read embedded helper -> hand off to bridge -> set status text. The
/// VM doesn't touch the pipe client / dump service in this path, so
/// the surrounding infrastructure is stubbed out as no-ops. These tests
/// validate the four end-states the user can land in:
///   - bridge missing entirely (DI didn't supply one)
///   - bridge present but CE not running -> graceful "use Export" hint
///   - bridge present, inject succeeds -> success status
///   - bridge present, inject fails -> fallback hint
/// </summary>
public class MainWindowInjectHelperTests
{
    [Fact]
    public async Task InjectCeHelperLua_NoBridge_StatusTextHintsConfigMissing()
    {
        var vm = BuildVm(aobMaker: null);

        await vm.InjectCeHelperLuaCommand.ExecuteAsync(null);

        Assert.Contains("AOBMaker", vm.StatusText);
    }

    [Fact]
    public async Task InjectCeHelperLua_BridgeUnavailable_StatusTextHintsCe()
    {
        var bridge = new RecordingBridge { NextAvailability = false };
        var vm = BuildVm(aobMaker: bridge);

        await vm.InjectCeHelperLuaCommand.ExecuteAsync(null);

        Assert.Equal(1, bridge.CheckCalls);
        Assert.Equal(0, bridge.InjectCalls);
        Assert.Contains("AOBMaker not connected", vm.StatusText);
    }

    [Fact]
    public async Task InjectCeHelperLua_Success_PassesEmbeddedHelperToBridge()
    {
        var bridge = new RecordingBridge { NextAvailability = true, NextInjectResult = true };
        var vm = BuildVm(aobMaker: bridge);

        await vm.InjectCeHelperLuaCommand.ExecuteAsync(null);

        Assert.Equal(1, bridge.InjectCalls);
        Assert.Equal(HelperLuaResource.DefaultFileName, bridge.LastInjectFileName);
        // The embedded helper is a non-trivial Lua module -- a good
        // floor without coupling to its exact byte count.
        Assert.NotNull(bridge.LastInjectContent);
        Assert.True(bridge.LastInjectContent!.Length > 200,
            $"helper content was suspiciously short ({bridge.LastInjectContent.Length} chars)");
        Assert.Contains("Inject helper OK", vm.StatusText);
        Assert.Contains(HelperLuaResource.DefaultFileName, vm.StatusText);
    }

    [Fact]
    public async Task InjectCeHelperLua_BridgeReturnsFalse_StatusOffersExportFallback()
    {
        var bridge = new RecordingBridge { NextAvailability = true, NextInjectResult = false };
        var vm = BuildVm(aobMaker: bridge);

        await vm.InjectCeHelperLuaCommand.ExecuteAsync(null);

        Assert.Equal(1, bridge.InjectCalls);
        Assert.Contains("Export to disk", vm.StatusText);
    }

    [Fact]
    public async Task InjectCeHelperLua_BridgeReturnsError_StatusSurfacesPluginMessage()
    {
        // Plugin returned an explicit failure reason (e.g. real bug from
        // pre-fix builds: "Stream size mismatch: wrote 10008, stream has 0").
        // The user can't tell "wrong CE state" apart from a real plugin
        // bug unless we actually surface the plugin's text.
        var bridge = new RecordingBridge
        {
            NextAvailability = true,
            NextInjectResult = false,
            NextInjectError = "Stream size mismatch: wrote 10008, stream has 0"
        };
        var vm = BuildVm(aobMaker: bridge);

        await vm.InjectCeHelperLuaCommand.ExecuteAsync(null);

        Assert.Equal(1, bridge.InjectCalls);
        Assert.Contains("Stream size mismatch", vm.StatusText);
        Assert.Contains("Export to disk", vm.StatusText);
    }

    // ------------------------------------------------------------------
    // [W1-PIPEBUSY-STATUS] a BUSY pipe must not tell the user to open Cheat Engine
    // ------------------------------------------------------------------
    //
    // 62f1596b made the bridge LOG a busy pipe as busy; the toolbar ⟳ still showed
    // "open Cheat Engine with the AOBMaker plugin loaded" for it (live, L64,
    // 2026-09-22). These go through the REAL bridge on its internal seam -- a pipe
    // name nobody serves, a 150 ms connect, and the existence probe answering for
    // "busy" -- so the whole chain from the timeout to the status line is exercised.

    private static AobMakerBridgeService SeamBridge(bool pipeExists)
        => new(new NoopLog(), "UE5DumpUITest_" + Guid.NewGuid().ToString("N"), 150, _ => pipeExists);

    [Fact]
    public async Task RefreshAobMaker_BusyPipe_SaysAnotherClientHoldsIt_NotOpenCheatEngine()
    {
        using var bridge = SeamBridge(pipeExists: true);
        var vm = BuildVm(aobMaker: bridge);

        await vm.RefreshAobMakerCommand.ExecuteAsync(null);

        Assert.False(vm.IsAobMakerAvailable);
        Assert.Contains("busy", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("another program", vm.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("open Cheat Engine", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshAobMaker_ReasonChangesWhileOffline_TheSystemTabLineRepaints()
    {
        // [R7-S7] The System tab's offline line is computed from the shared bridge's LastFailure, but the toolbar ⟳ only
        // SET Pointers.IsAobMakerAvailable: false -> false raises nothing, so the line kept "not connected -- open Cheat
        // Engine" after ⟳ had found the pipe busy. (true -> false raised nothing for it either: no change hook.)
        bool exists = false;
        using var bridge = new AobMakerBridgeService(new NoopLog(), "UE5DumpUITest_" + Guid.NewGuid().ToString("N"), 150,
                                                     _ => exists);
        var vm = BuildVm(aobMaker: bridge);
        await vm.Pointers.CheckAobMakerAsync();                 // the System tab's own probe: absent
        Assert.Contains("not connected", vm.Pointers.AobMakerOfflineText, StringComparison.Ordinal);

        exists = true;                                           // another client now holds the pipe
        var raised = new List<string?>();
        vm.Pointers.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        await vm.RefreshAobMakerCommand.ExecuteAsync(null);

        Assert.False(vm.Pointers.IsAobMakerAvailable);
        Assert.Contains(nameof(PointerPanelViewModel.AobMakerOfflineText), raised);
        Assert.Contains("busy", vm.Pointers.AobMakerOfflineText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshAobMaker_ReasonChangesWhileOffline_LiveWalkerAndTeleportNotesRepaint()
    {
        // [R7-S12] Live Walker's note IS bound (LiveWalkerPanel.axaml) and, since R7-S7, reasoned -- but the toolbar ⟳
        // still set its flag plainly, so false -> false with a new reason repainted nothing there; Teleport's note was
        // never touched by ⟳ at all.
        bool exists = false;
        using var bridge = new AobMakerBridgeService(new NoopLog(), "UE5DumpUITest_" + Guid.NewGuid().ToString("N"), 150,
                                                     _ => exists);
        var vm = BuildVm(aobMaker: bridge);
        await vm.LiveWalker.CheckAobMakerAsync();               // absent
        await vm.Teleport.CheckAobMakerAsync();
        Assert.Contains("not connected", vm.LiveWalker.AobMakerNote, StringComparison.Ordinal);

        exists = true;
        var walker = new List<string?>();
        var teleport = new List<string?>();
        vm.LiveWalker.PropertyChanged += (_, e) => walker.Add(e.PropertyName);
        vm.Teleport.PropertyChanged += (_, e) => teleport.Add(e.PropertyName);
        await vm.RefreshAobMakerCommand.ExecuteAsync(null);

        Assert.Contains(nameof(LiveWalkerViewModel.AobMakerNote), walker);
        Assert.Contains("busy", vm.LiveWalker.AobMakerNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(TeleportViewModel.AobMakerNote), teleport);
    }

    // ------------------------------------------------------------------
    // [PROPSEARCH-FREEZE-STALE-AOBMAKER] Property Search's Freeze follows what the app knows about the plugin
    // ------------------------------------------------------------------
    //
    // Measured on build 3682: AOBMaker connected while the Properties tab was open, the toolbar ⟳ said "AOBMaker
    // plugin connected", and Freeze stayed disabled until a tab switch -- ⟳ repainted four panels by name and
    // Property Search was not one of them. A disabled button cannot reach the probe its click would run.

    [Fact]
    public async Task ToolbarRefresh_AfterThePluginConnects_EnablesPropertySearchFreeze()
    {
        var bridge = new RecordingBridge { NextAvailability = false };
        var vm = BuildVm(aobMaker: bridge);
        await vm.PropertySearch.RefreshAobMakerAvailabilityAsync();   // the panel's own probe: CE not up yet
        Assert.False(vm.PropertySearch.IsAobMakerAvailable);

        bridge.NextAvailability = true;                               // CE + AOBMaker start, the user presses ⟳
        await vm.RefreshAobMakerCommand.ExecuteAsync(null);

        Assert.True(vm.IsAobMakerAvailable);
        Assert.True(vm.PropertySearch.IsAobMakerAvailable);
    }

    [Fact]
    public async Task ToolbarRefresh_AfterThePluginGoes_DisablesPropertySearchFreeze()
    {
        // The other direction: an enabled Freeze over a dead pipe only fails at the click.
        var bridge = new RecordingBridge { NextAvailability = true };
        var vm = BuildVm(aobMaker: bridge);
        await vm.PropertySearch.RefreshAobMakerAvailabilityAsync();
        Assert.True(vm.PropertySearch.IsAobMakerAvailable);

        bridge.NextAvailability = false;
        await vm.RefreshAobMakerCommand.ExecuteAsync(null);

        Assert.False(vm.PropertySearch.IsAobMakerAvailable);
    }

    [Fact]
    public async Task AnotherPanelsProbe_ReachesPropertySearch_AndItsOwnProbeReachesTheToolbar()
    {
        // The shared status reaches Property Search whoever probes it, and Property Search's own probe reaches it.
        var bridge = new RecordingBridge { NextAvailability = true };
        var vm = BuildVm(aobMaker: bridge);
        await vm.PropertySearch.RefreshAobMakerAvailabilityAsync();   // what Property Search learns, everyone learns
        Assert.True(vm.AobMakerShared.IsAvailable);
        Assert.True(vm.IsAobMakerAvailable);

        // A probe started from elsewhere (ProbeAsync, not the cooldown-bound refresh: they share one cooldown now).
        bridge.NextAvailability = false;
        await vm.AobMakerShared.ProbeAsync();
        Assert.False(vm.PropertySearch.IsAobMakerAvailable);
        Assert.Equal(2, bridge.CheckCalls);
    }

    [Fact]
    public async Task PropertySearchAndTheSharedStatus_KeepOneCooldown()
    {
        // [PROPSEARCH-FREEZE-STALE-AOBMAKER] review 1, INFO 2. Two cooldowns cost a second pipe connect -- 2 s of the
        // bridge's lock with CE absent -- when a Search was followed by a switch to a tab that probes the shared status.
        var bridge = new RecordingBridge { NextAvailability = true };
        var vm = BuildVm(aobMaker: bridge);
        await vm.PropertySearch.RefreshAobMakerAvailabilityAsync();
        vm.AobMakerShared.TryProbe();
        await vm.AobMakerShared.RefreshAsync();

        Assert.Equal(1, bridge.CheckCalls);
    }

    [Fact]
    public async Task TeleportsAndInterestingFunctionsProbes_ReachTheToolbarAndPropertySearch()
    {
        // [PROPSEARCH-FREEZE-STALE-AOBMAKER] review 1, F2. These two panels keep a flag of their own and probe on their
        // own tab activation, but only Live Walker's and Pointers' flags were mirrored into the toolbar chip -- so what
        // they learned reached neither the chip nor anything that follows the shared status.
        var bridge = new RecordingBridge { NextAvailability = true };
        var vm = BuildVm(aobMaker: bridge);
        await vm.Teleport.CheckAobMakerAsync();
        Assert.True(vm.IsAobMakerAvailable);
        Assert.True(vm.PropertySearch.IsAobMakerAvailable);

        bridge.NextAvailability = false;
        await vm.InterestingFunctions.CheckAobMakerAsync();   // its flag starts "up", so this is a change
        Assert.False(vm.IsAobMakerAvailable);
        Assert.False(vm.PropertySearch.IsAobMakerAvailable);
    }

    /// <summary>A flag-keeping panel's own probe, and a way to set its flag without one.</summary>
    private static (Func<Task> Probe, Action<bool> SetFlag) FlagKeepingPanel(MainWindowViewModel vm, string panel)
        => panel switch
        {
            "LiveWalker" => (() => vm.LiveWalker.CheckAobMakerAsync(), v => vm.LiveWalker.IsAobMakerAvailable = v),
            "Pointers" => (() => vm.Pointers.CheckAobMakerAsync(), v => vm.Pointers.IsAobMakerAvailable = v),
            "InterestingFunctions" => (() => vm.InterestingFunctions.CheckAobMakerAsync(),
                                       v => vm.InterestingFunctions.IsAobMakerAvailable = v),
            "Teleport" => (() => vm.Teleport.CheckAobMakerAsync(), v => vm.Teleport.IsAobMakerAvailable = v),
            _ => throw new ArgumentOutOfRangeException(nameof(panel), panel, null),
        };

    [Theory]
    [InlineData("LiveWalker")]
    [InlineData("Pointers")]
    [InlineData("InterestingFunctions")]
    [InlineData("Teleport")]
    public async Task APanelsProbe_ThatConfirmsItsOwnFlag_StillReachesTheToolbar(string panel)
    {
        // [PROPSEARCH-FREEZE-STALE-AOBMAKER] review 2, F1. The chip mirrors a panel's flag on a CHANGE, and a probe
        // that confirms what the flag already says raises none: Interesting Functions' flag starts "up", so its first
        // probe after Cheat Engine came up reached nobody, and a "down" confirmed while the chip said "up" left the
        // chip saying "connected" over a dead pipe.
        var bridge = new RecordingBridge { NextAvailability = true };
        var vm = BuildVm(aobMaker: bridge);
        var (probe, setFlag) = FlagKeepingPanel(vm, panel);

        setFlag(true);                    // the panel already says "up"...
        vm.IsAobMakerAvailable = false;   // ...while the chip, and the shared status behind it, say "down"
        await probe();
        Assert.True(vm.IsAobMakerAvailable);
        Assert.True(vm.PropertySearch.IsAobMakerAvailable);

        bridge.NextAvailability = false;
        setFlag(false);
        vm.IsAobMakerAvailable = true;
        await probe();
        Assert.False(vm.IsAobMakerAvailable);
        Assert.False(vm.PropertySearch.IsAobMakerAvailable);
    }

    [Fact]
    public async Task LiveWalkerProbe_ReasonChangesWhileOffline_TheNoteRepaints()
    {
        // [R7-S12] ...and Live Walker's own tab-activation probe had the same false -> false gap.
        bool exists = false;
        using var bridge = new AobMakerBridgeService(new NoopLog(), "UE5DumpUITest_" + Guid.NewGuid().ToString("N"), 150,
                                                     _ => exists);
        var vm = BuildVm(aobMaker: bridge);
        await vm.LiveWalker.CheckAobMakerAsync();               // absent

        exists = true;
        var raised = new List<string?>();
        vm.LiveWalker.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        await vm.LiveWalker.CheckAobMakerAsync();               // still false, now busy

        Assert.Contains(nameof(LiveWalkerViewModel.AobMakerNote), raised);
    }

    [Fact]
    public void PointersAvailabilityFlip_RepaintsTheOfflineLineAndTheButtons()
    {
        // [R7-S7] ...and a plain flip (connected -> not) repaints the line and the CE buttons that read the flag.
        var vm = new PointerPanelViewModel(new MockPlatformService(Path.GetTempPath())) { IsAobMakerAvailable = true };
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsAobMakerAvailable = false;

        Assert.Contains(nameof(PointerPanelViewModel.AobMakerOfflineText), raised);
        Assert.Contains(nameof(PointerPanelViewModel.CanHexGObjects), raised);
    }

    [Fact]
    public async Task RefreshAobMaker_AbsentPipe_StillSaysOpenCheatEngine()
    {
        // The control, green both ways: nothing listening keeps the old remedy.
        using var bridge = SeamBridge(pipeExists: false);
        var vm = BuildVm(aobMaker: bridge);

        await vm.RefreshAobMakerCommand.ExecuteAsync(null);

        Assert.False(vm.IsAobMakerAvailable);
        Assert.Contains("open Cheat Engine", vm.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("busy", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InjectCeHelperLua_BusyPipe_SaysAnotherClientHoldsIt_NotOpenCheatEngine()
    {
        // The same probe feeds the Tools-menu inject; it must not disagree with ⟳.
        using var bridge = SeamBridge(pipeExists: true);
        var vm = BuildVm(aobMaker: bridge);

        await vm.InjectCeHelperLuaCommand.ExecuteAsync(null);

        Assert.StartsWith("Inject helper:", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("busy", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("open Cheat Engine", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // Wiring helpers
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    // [MAINWINDOW-NAVIGATE-EXACT-NAME] a Locate asks find_live_instance, and says so when nothing is live
    // ------------------------------------------------------------------

    private sealed class LiveLookupDump : StubDumpService
    {
        public List<string> Asked { get; } = new();
        /// <summary>What every lookup answers; nothing live by default.</summary>
        public UE5DumpUI.Models.LiveInstanceResult Answer { get; set; } = new();

        public override Task<UE5DumpUI.Models.LiveInstanceResult> FindLiveInstanceAsync(string className,
                                                                                        CancellationToken ct = default)
        {
            Asked.Add(className);
            return Task.FromResult(Answer);
        }
    }

    [Fact]
    public void A_Locate_an_older_DLL_missed_says_no_subclass_was_looked_for()
    {
        // Review 3: the miss line takes the lookup's answer; dropping it at the call site compiled and said "or of a
        // subclass" again.
        var miss = new UE5DumpUI.Models.LiveInstanceResult { Pick = UE5DumpUI.Models.LiveInstanceResult.PickLegacy };
        var vm = BuildVm(aobMaker: null, dump: new LiveLookupDump { Answer = miss });

        vm.InterestingProperties.LocateRowInGWorldCommand.Execute(new UE5DumpUI.Models.ScoredPropertyRow
        {
            Match = new UE5DumpUI.Models.PropertySearchMatch { ClassName = "AMyActor", PropName = "Health" },
            FinalScore = 1, Category = UE5DumpUI.Services.PropertyCategory.Other, KeywordHits = 0, ClassBonus = 0,
            IsUnusualLocation = false,
        });

        Assert.Equal(MainWindowViewModel.NoLiveInstanceToLocateText("AMyActor", "GWorld", miss), vm.LiveWalker.StatusText);
        Assert.DoesNotContain("or of a subclass", vm.LiveWalker.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Locate_with_nothing_live_says_so_in_Live_Walker()
    {
        var dump = new LiveLookupDump();
        var vm = BuildVm(aobMaker: null, dump: dump);

        vm.InterestingProperties.LocateRowInGWorldCommand.Execute(new UE5DumpUI.Models.ScoredPropertyRow
        {
            Match = new UE5DumpUI.Models.PropertySearchMatch { ClassName = "AMyActor", PropName = "Health" },
            FinalScore = 1, Category = UE5DumpUI.Services.PropertyCategory.Other, KeywordHits = 0, ClassBonus = 0,
            IsUnusualLocation = false,
        });

        Assert.Equal(new[] { "AMyActor" }, dump.Asked);
        Assert.Equal((int)MainTabIndex.LiveWalker, vm.SelectedTabIndex);
        Assert.Equal(MainWindowViewModel.NoLiveInstanceToLocateText("AMyActor", "GWorld"), vm.LiveWalker.StatusText);
    }

    private static MainWindowViewModel BuildVm(IAobMakerBridge? aobMaker, IDumpService? dump = null)
        => new MainWindowViewModel(
            pipeClient: new NoopPipeClient(),
            dump: dump ?? new StubDumpService(),
            log: new NoopLog(),
            platform: new NoopPlatform(),
            aobUsage: null,
            aobMaker: aobMaker,
            proxyDeploy: null);

    private sealed class RecordingBridge : IAobMakerBridge
    {
        public bool NextAvailability { get; set; }
        public bool NextInjectResult { get; set; }
        public string? NextInjectError { get; set; }
        public int CheckCalls { get; private set; }
        public int InjectCalls { get; private set; }
        public string? LastInjectFileName { get; private set; }
        public string? LastInjectContent { get; private set; }

        public bool IsAvailable { get; private set; }

        public Task<bool> CheckAvailabilityAsync(CancellationToken ct = default)
        {
            CheckCalls++;
            IsAvailable = NextAvailability;
            return Task.FromResult(NextAvailability);
        }

        public Task<(bool Ok, string? ErrorMessage)> InjectTableFileAsync(string fileName, string content,
            CancellationToken ct = default)
        {
            InjectCalls++;
            LastInjectFileName = fileName;
            LastInjectContent = content;
            return Task.FromResult((NextInjectResult, NextInjectError));
        }

        public Task<bool> NavigateHexViewAsync(string hexAddress, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> NavigateDisassemblerAsync(string hexAddress, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> CreateAAScriptAsync(string description, string script,
            bool autoActivate = true, string? group = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CreateSymbolScriptAsync(string name, string aob, int pos, int aoblen,
            string symbol, string module, bool autoActivate = true, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> CreateMemoryRecordAsync(string description, string address, int valueType,
            bool isSigned = false, bool showAsHex = false, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class NoopPipeClient : IPipeClient
    {
        public bool IsConnected => false;
        public event Action<bool>? ConnectionStateChanged { add { } remove { } }
        public event Action<JsonObject>? EventReceived { add { } remove { } }
        public event Action<UE5DumpUI.Models.PipeLogEntry>? Activity { add { } remove { } }
        public event Action<bool>? GameThreadStalledChanged { add { } remove { } }
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<JsonObject> SendAsync(JsonObject request, CancellationToken ct = default)
            => Task.FromResult(new JsonObject());
        public void Dispose() { }
    }

    private sealed class NoopPlatform : IPlatformService
    {
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => "";
        public string GetLogDirectoryPath() => "";
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "test";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName,
            string filterName, string filterExtension) => Task.FromResult<string?>(null);
    }

    private sealed class NoopLog : ILoggingService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Error(string message, Exception ex) { }
        public void Debug(string message) { }
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message) { }
        public void Error(string category, string message, Exception ex) { }
        public void Debug(string category, string message) { }
        public void StartProcessMirror(string processName) { }
        public void StopProcessMirror() { }
    }

    /// <summary>
    /// Neither helper may reach CE with a CR in it. [FREEZEINJECT-CRLF-2026-08-20]
    ///
    /// ⚠ The FREEZE half is the one that was reported, but the INVOKE half is the one
    /// that matters for a fresh clone: both files are `i/lf` in the index with
    /// core.autocrlf=true, so which of them arrives CRLF is a property of the CHECKOUT.
    /// Today the freeze helper is CRLF here and the invoke helper is not — asserting only
    /// the reported one would pass on this machine and miss the next.
    /// </summary>
    [Fact]
    public async Task InjectCeHelperLua_SendsLfOnly()
    {
        var bridge = new RecordingBridge { NextAvailability = true, NextInjectResult = true };
        var vm = BuildVm(aobMaker: bridge);

        await vm.InjectCeHelperLuaCommand.ExecuteAsync(null);

        Assert.NotNull(bridge.LastInjectContent);
        Assert.DoesNotContain('\r', bridge.LastInjectContent!);
        Assert.Contains('\n', bridge.LastInjectContent!);   // not simply emptied
    }

    [Fact]
    public async Task InjectFreezeHelperLua_SendsLfOnly()
    {
        var bridge = new RecordingBridge { NextAvailability = true, NextInjectResult = true };
        var vm = BuildVm(aobMaker: bridge);

        await vm.InjectFreezeHelperLuaCommand.ExecuteAsync(null);

        Assert.Equal(1, bridge.InjectCalls);
        Assert.NotNull(bridge.LastInjectContent);
        Assert.DoesNotContain('\r', bridge.LastInjectContent!);
        Assert.Contains('\n', bridge.LastInjectContent!);
    }
}
