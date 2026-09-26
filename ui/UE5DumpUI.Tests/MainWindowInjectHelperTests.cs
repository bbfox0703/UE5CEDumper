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

    private static MainWindowViewModel BuildVm(IAobMakerBridge? aobMaker)
        => new MainWindowViewModel(
            pipeClient: new NoopPipeClient(),
            dump: new StubDumpService(),
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
