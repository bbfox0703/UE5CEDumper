using System.Linq;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// VM-level tests for the Live ProcessEvent Call Profiler (Live Funcs panel).
/// Exercises Start/Stop/Refresh/Clear, the "no PE hook" warning, the client-side
/// keyword filter (space = AND), and the cross-tab handoffs. The actual PE
/// counting is DLL-side and verified in-game (Fern has no unit tests).
/// </summary>
public class LiveFuncsViewModelTests
{
    private sealed class FakeDumpService : StubDumpService, IDumpService
    {
        public bool? LastSkipPerFrame { get; private set; }

        Task<PeProfileResult> IDumpService.PeProfileGetAsync(int limit, bool skipPerFrame, CancellationToken ct)
        {
            LastSkipPerFrame = skipPerFrame;
            return PeProfileGetAsync(limit, ct);
        }

        public bool StartHookActive { get; set; } = true;
        public string StartDetail { get; set; } = "";
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int GetCalls { get; private set; }
        public int LastLimit { get; private set; }
        public PeProfileResult NextGet { get; set; } = new();

        public override Task<PeProfileStartResult> PeProfileStartAsync(CancellationToken ct = default)
        {
            StartCalls++;
            return Task.FromResult(new PeProfileStartResult { HookActive = StartHookActive, Detail = StartDetail });
        }
        public override Task PeProfileStopAsync(CancellationToken ct = default)
        {
            StopCalls++;
            return Task.CompletedTask;
        }
        public override Task<PeProfileResult> PeProfileGetAsync(int limit = 200, CancellationToken ct = default)
        {
            GetCalls++;
            LastLimit = limit;
            return Task.FromResult(NextGet);
        }
    }

    private sealed class NoopLogger : ILoggingService
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

    private static (LiveFuncsViewModel vm, FakeDumpService dump) MakeVm()
    {
        var dump = new FakeDumpService();
        var vm = new LiveFuncsViewModel(dump, new NoopLogger());
        return (vm, dump);
    }

    private static PeProfileResult ResultOf(params PeProfileEntry[] entries)
        => new()
        {
            Recording     = false,
            DistinctFuncs = entries.Length,
            TotalCalls    = entries.Sum(e => e.Count),
            Entries       = entries.ToList(),
        };

    /// <summary>A page the DLL CAPPED: <paramref name="distinct"/> functions were recorded,
    /// only <paramref name="entries"/> came back. Deliberately a separate helper —
    /// <see cref="ResultOf"/> sets <c>DistinctFuncs = entries.Length</c>, so every test using
    /// it is structurally incapable of expressing truncation and always asserts the
    /// degenerate not-truncated case. That aliasing is exactly why ~15 existing tests never
    /// caught AF3; reusing it here would have made these tests pass while testing nothing.</summary>
    private static PeProfileResult TruncatedResultOf(int distinct, params PeProfileEntry[] entries)
        => new()
        {
            Recording     = false,
            DistinctFuncs = distinct,
            TotalCalls    = entries.Sum(e => e.Count),
            Entries       = entries.ToList(),
        };

    // ==================================================================
    // Start / Stop
    // ==================================================================

    [Fact]
    public async Task Start_HookActive_SetsRecordingAndReadyStatus()
    {
        var (vm, dump) = MakeVm();
        dump.StartHookActive = true;

        await vm.StartCommand.ExecuteAsync(null);

        Assert.True(vm.IsRecording);
        Assert.Equal(1, dump.StartCalls);
        Assert.Contains("Recording", vm.StatusText);
    }

    // Regression for audit #3 L16: nothing reset IsRecording on pipe disconnect, so a
    // reconnect showed a stuck "recording" that swallowed the next Start.
    [Fact]
    public async Task ResetOnDisconnect_clears_recording_state()
    {
        var (vm, _) = MakeVm();
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsRecording);

        vm.ResetOnDisconnect();

        Assert.False(vm.IsRecording);
    }

    [Fact]
    public async Task Start_NoHook_SurfacesDllReason()
    {
        var (vm, dump) = MakeVm();
        dump.StartHookActive = false;
        dump.StartDetail = "PE hook couldn't install — change to another map/scene and Start again.";

        await vm.StartCommand.ExecuteAsync(null);

        Assert.True(vm.IsRecording);
        // The DLL's self-contained reason is shown verbatim (change-map guidance included).
        Assert.Equal(dump.StartDetail, vm.StatusText);
    }

    [Fact]
    public async Task Stop_FetchesAndPopulatesRanked()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "AShopVendor", FuncName = "OpenShop", Count = 3 },
            new PeProfileEntry { ClassName = "APawn",       FuncName = "Tick",     Count = 900 });

        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.False(vm.IsRecording);
        Assert.Equal(1, dump.StopCalls);
        Assert.Equal(1, dump.GetCalls);
        Assert.Equal(2, vm.Results.Count);
        // Ranked by call count desc (non-diff mode): Tick (900) tops OpenShop (3).
        // (The action-specific low-count OpenShop is what Diff mode surfaces instead.)
        Assert.Equal("Tick", vm.Results[0].FuncName);
        Assert.Contains(vm.Results, r => r.FuncName == "OpenShop");
        Assert.Contains("2 distinct", vm.StatusText);
    }

    [Fact]
    public async Task Stop_WhenNotRecording_IsNoOp()
    {
        var (vm, dump) = MakeVm();
        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(0, dump.StopCalls);
        Assert.Equal(0, dump.GetCalls);
    }

    [Fact]
    public async Task Stop_EmptyRecording_ExplainsZero()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(); // nothing fired

        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.Empty(vm.Results);
        Assert.Contains("No UFunctions recorded", vm.StatusText);
    }

    // ==================================================================
    // Filter (space = AND over func + class) + Clear
    // ==================================================================

    [Fact]
    public async Task Filter_SpaceIsAnd_OverFuncAndClass()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "AShopVendor", FuncName = "OpenPanel", Count = 5 }, // open + shop(class)
            new PeProfileEntry { ClassName = "AMenu",       FuncName = "OpenPanel", Count = 4 }); // open only

        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Results.Count);

        vm.FilterText = "open shop";   // AND: needs both terms across func OR class
        Assert.Single(vm.Results);
        Assert.Equal("AShopVendor", vm.Results[0].ClassName);
    }

    [Fact]
    public async Task Clear_EmptiesResultsAndFilter()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "A", FuncName = "F", Count = 1 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.FilterText = "F";

        vm.ClearCommand.Execute(null);

        Assert.Empty(vm.Results);
        Assert.Equal("", vm.FilterText);
    }

    // ==================================================================
    // Baseline diff (isolate the action's function from Tick noise)
    // ==================================================================

    [Fact]
    public async Task Diff_SetBaselineThenAction_SurfacesNewFunctionOnTop()
    {
        var (vm, dump) = MakeVm();
        // Idle baseline: per-frame noise, no shop function.
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "APawn", FuncName = "Tick",   Count = 900 },
            new PeProfileEntry { ClassName = "AHUD",  FuncName = "Update", Count = 300 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        vm.SetBaselineCommand.Execute(null);
        Assert.True(vm.DiffMode);

        // Action: Tick keeps firing (higher), Update unchanged, PLUS a NEW OpenShop.
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "APawn",       FuncName = "Tick",     Count = 1000 },
            new PeProfileEntry { ClassName = "AHUD",        FuncName = "Update",   Count = 300 },
            new PeProfileEntry { ClassName = "AShopVendor", FuncName = "OpenShop", Count = 2 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        // NewChangedOnly defaults on → the unchanged Update (Δ0) is hidden.
        Assert.DoesNotContain(vm.Results, r => r.FuncName == "Update");
        // NEW function ranks first, ahead of the +100 Tick.
        Assert.Equal("OpenShop", vm.Results[0].FuncName);
        Assert.True(vm.Results[0].IsNew);
        Assert.Equal("NEW", vm.Results[0].DeltaLabel);
        var tick = vm.Results.First(r => r.FuncName == "Tick");
        Assert.Equal(100, tick.Delta);
        Assert.Equal("+100", tick.DeltaLabel);
        Assert.Contains("NEW", vm.StatusText);
    }

    [Fact]
    public async Task Diff_NewChangedOnlyOff_ShowsUnchangedRowsToo()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = "AHUD", FuncName = "Update", Count = 300 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);

        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = "AHUD", FuncName = "Update", Count = 300 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.Results, r => r.FuncName == "Update"); // Δ0 hidden by default
        vm.NewChangedOnly = false;
        Assert.Contains(vm.Results, r => r.FuncName == "Update");       // now visible
    }

    [Fact]
    public void SetBaseline_WithNoData_DoesNotEnableDiff()
    {
        var (vm, _) = MakeVm();
        vm.SetBaselineCommand.Execute(null);
        Assert.False(vm.DiffMode);
    }

    [Fact]
    public void ClearBaseline_ResetsDiffMode()
    {
        var (vm, _) = MakeVm();
        vm.DiffMode = true;
        vm.ClearBaselineCommand.Execute(null);
        Assert.False(vm.DiffMode);
    }

    [Theory]
    [InlineData(false, 0L, "")]
    [InlineData(false, 5L, "+5")]
    [InlineData(false, -3L, "-3")]
    [InlineData(true, 7L, "NEW")]
    public void PeProfileEntry_DeltaLabel_Formats(bool isNew, long delta, string expected)
    {
        var e = new PeProfileEntry { IsNew = isNew, Delta = delta };
        Assert.Equal(expected, e.DeltaLabel);
    }

    [Fact]
    public async Task HideWidgets_RemovesTransientWidgetMethods()
    {
        // A shop-open recording: the widget's Construct fires (is_widget) alongside
        // the persistent controller's opener. Hiding widgets leaves the opener.
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "DOLLShopStoreLayout", FuncName = "Construct",
                                 Count = 1, IsWidget = true },
            new PeProfileEntry { ClassName = "AShopController", FuncName = "OpenShop",
                                 Count = 2, IsWidget = false });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Results.Count);

        vm.HideWidgets = true;
        Assert.DoesNotContain(vm.Results, r => r.ClassName == "DOLLShopStoreLayout");
        Assert.Contains(vm.Results, r => r.FuncName == "OpenShop");
    }

    [Fact]
    public async Task PeriodicOnly_KeepsOnlyTimerCadenceRows()
    {
        // An idle-window recording: a steady 0.5 s timer callback alongside per-frame
        // Tick (frame band) and an input-driven one-off. Periodic-only leaves the timer.
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "ABuffMgr", FuncName = "OnDotTick",
                                 Count = 20, MeanPeriodMs = 500, Cv = 0.04, GapSamples = 19 },
            new PeProfileEntry { ClassName = "APawn", FuncName = "Tick",
                                 Count = 900, MeanPeriodMs = 16, Cv = 0.05, GapSamples = 899 },
            new PeProfileEntry { ClassName = "APlayer", FuncName = "OnFirePressed",
                                 Count = 4, MeanPeriodMs = 1200, Cv = 0.9, GapSamples = 3 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Results.Count);

        vm.PeriodicOnly = true;
        Assert.Single(vm.Results);
        Assert.Equal("OnDotTick", vm.Results[0].FuncName);
        Assert.Equal("Timer", vm.Results[0].Kind);   // periodic rows badge as "Timer"
    }

    [Theory]
    [InlineData(true, "UI")]
    [InlineData(false, "")]
    public void PeProfileEntry_Kind_ReflectsIsWidget(bool isWidget, string expected)
    {
        var e = new PeProfileEntry { IsWidget = isWidget };
        Assert.Equal(expected, e.Kind);
    }

    // Cadence classification (Phase E): (meanMs, cv, gapSamples) → isPeriodic.
    [Theory]
    [InlineData(500.0, 0.05, 19, true)]    // steady 0.5 s timer — regular, out of frame band
    [InlineData(16.0,  0.05, 900, false)]  // per-frame Tick — regular but in the frame band
    [InlineData(500.0, 0.05, 2,  false)]   // too few samples (need ≥3 gaps)
    [InlineData(500.0, 0.80, 19, false)]   // high CV — bursty / input-driven, not regular
    [InlineData(60000.0, 0.05, 10, false)] // too slow to be a gameplay timer here
    [InlineData(0.0,   0.0,  0,  false)]   // never fired twice — no cadence
    public void PeProfileEntry_IsPeriodic_Classifies(double meanMs, double cv, long gaps, bool expected)
    {
        var e = new PeProfileEntry { MeanPeriodMs = meanMs, Cv = cv, GapSamples = gaps };
        Assert.Equal(expected, e.IsPeriodic);
    }

    [Fact]
    public void PeProfileEntry_Kind_IsTimer_WhenPeriodic()
    {
        var e = new PeProfileEntry { MeanPeriodMs = 250, Cv = 0.03, GapSamples = 40 };
        Assert.True(e.IsPeriodic);
        Assert.Equal("Timer", e.Kind);   // Timer takes precedence over the UI badge
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "250 ms")]      // sub-second → ms
    [InlineData(40, "250 ms")]
    public void PeProfileEntry_PeriodLabel_FormatsMs(long gaps, string expected)
    {
        var e = new PeProfileEntry { MeanPeriodMs = 250, GapSamples = gaps };
        Assert.Equal(expected, e.PeriodLabel);
    }

    [Fact]
    public void PeProfileEntry_PeriodLabel_FormatsSecondsAboveOneThousandMs()
    {
        var e = new PeProfileEntry { MeanPeriodMs = 1500, GapSamples = 5 };
        Assert.Equal("1.5 s", e.PeriodLabel);
    }

    // FUNC_Event=0x800, FUNC_MulticastDelegate=0x10000, FUNC_BlueprintCallable=0x04000000, FUNC_Native=0x400.
    [Theory]
    [InlineData(0x0000_0800u, true,  "Event")]   // BP event (On*)
    [InlineData(0x0001_0000u, true,  "Deleg")]   // multicast delegate (OnHit etc.)
    [InlineData(0x0400_0000u, false, "Call")]    // imperative BlueprintCallable — the kind we want
    [InlineData(0x0000_0400u, false, "native")]
    public void PeProfileEntry_TypeAndEventLike_FromFlags(uint flags, bool eventLike, string label)
    {
        var e = new PeProfileEntry { FunctionFlags = flags };
        Assert.Equal(eventLike, e.IsEventLike);
        Assert.Equal(label, e.TypeLabel);
    }

    [Fact]
    public async Task EarliestFirst_SortsByFirstFireCausalOrder()
    {
        // The opener (OpenShop) fired at call #40 — BEFORE the reactions it triggered
        // (widget Construct #205, a per-frame Tick #2 with a huge count). Ranked by count
        // Tick tops; by call order OpenShop tops. Unknown FirstSeq (0) sinks last.
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "APawn",    FuncName = "Tick",      Count = 900, FirstSeq = 2 },
            new PeProfileEntry { ClassName = "AShopMgr", FuncName = "OpenShop",  Count = 1,   FirstSeq = 40 },
            new PeProfileEntry { ClassName = "AWidget",  FuncName = "Construct", Count = 3,   FirstSeq = 205 },
            new PeProfileEntry { ClassName = "AMisc",    FuncName = "NoSeq",     Count = 5,   FirstSeq = 0 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        vm.EarliestFirst = true;
        // Ascending by FirstSeq (0 → last): Tick(2), OpenShop(40), Construct(205), NoSeq(0→last).
        Assert.Equal("Tick", vm.Results[0].FuncName);
        Assert.Equal("OpenShop", vm.Results[1].FuncName);
        Assert.Equal("Construct", vm.Results[2].FuncName);
        Assert.Equal("NoSeq", vm.Results[3].FuncName);  // FirstSeq 0 sinks to the bottom
    }

    [Fact]
    public async Task HideEvents_RemovesEventAndDelegateRows()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "AVolume",  FuncName = "OnStartSkit", Count = 7,
                                 FunctionFlags = 0x0000_0800 },   // Event
            new PeProfileEntry { ClassName = "AShopMgr", FuncName = "OpenShop",    Count = 1,
                                 FunctionFlags = 0x0400_0000 });  // BlueprintCallable
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Results.Count);

        vm.HideEvents = true;
        Assert.DoesNotContain(vm.Results, r => r.FuncName == "OnStartSkit");
        Assert.Contains(vm.Results, r => r.FuncName == "OpenShop");
    }

    // ==================================================================
    // Cross-tab handoffs + auto-stop
    // ==================================================================

    [Fact]
    public void OpenInLiveWalker_RaisesNavigateWithClassAndFunc()
    {
        var (vm, _) = MakeVm();
        (string cls, string func)? got = null;
        vm.NavigateToFunction += (c, f) => got = (c, f);

        vm.OpenInLiveWalkerCommand.Execute(
            new PeProfileEntry { ClassName = "AShopVendor", FuncName = "OpenShop" });

        Assert.Equal(("AShopVendor", "OpenShop"), got);
    }

    [Fact]
    public async Task CopyFuncName_RaisesRequestCopyText_AndClaimsOnlyOnSuccess()
    {
        var (vm, _) = MakeVm();
        string? copied = null;
        vm.RequestCopyText += t => { copied = t; return Task.FromResult(true); };

        await vm.CopyFuncNameCommand.ExecuteAsync(
            new PeProfileEntry { ClassName = "A", FuncName = "OpenShop" });

        Assert.Equal("OpenShop", copied);
        Assert.Contains("Copied function name: OpenShop", vm.StatusText);
    }

    /// <summary>
    /// The reason RequestCopyText is Func&lt;string,Task&lt;bool&gt;&gt; and not
    /// Action&lt;string&gt;. As an Action the handler was an async lambda, so Invoke
    /// returned at its first await and the VM set "Copied ..." BEFORE the clipboard was
    /// even touched -- unreachable by any assertion, because the answer arrived after the
    /// claim. Blind-spot sweep round 3, sub-shape (b).
    /// </summary>
    [Fact]
    public async Task CopyFuncName_WhenTheClipboardRefuses_DoesNotClaimSuccess()
    {
        var (vm, _) = MakeVm();
        vm.RequestCopyText += _ => Task.FromResult(false);

        await vm.CopyFuncNameCommand.ExecuteAsync(
            new PeProfileEntry { ClassName = "A", FuncName = "OpenShop" });

        Assert.DoesNotContain("Copied function name", vm.StatusText);
        Assert.Contains("Could not copy", vm.StatusText);
    }

    [Fact]
    public async Task CopyFuncName_WithNoSubscriber_DoesNotClaimSuccess()
    {
        // Nobody wired the event: nothing reached the clipboard, so nothing may be claimed.
        var (vm, _) = MakeVm();

        await vm.CopyFuncNameCommand.ExecuteAsync(
            new PeProfileEntry { ClassName = "A", FuncName = "OpenShop" });

        Assert.DoesNotContain("Copied function name", vm.StatusText);
    }

    [Fact]
    public async Task OnLeavingTab_WhileRecording_AutoStops()
    {
        var (vm, dump) = MakeVm();
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsRecording);

        vm.OnLeavingTab();
        // Auto-stop is fire-and-forget; give the continuation a beat to run.
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.False(vm.IsRecording);
        Assert.True(dump.StopCalls >= 1);
    }

    [Fact]
    public void OnLeavingTab_WhenNotRecording_DoesNotStop()
    {
        var (vm, dump) = MakeVm();
        vm.OnLeavingTab();
        Assert.Equal(0, dump.StopCalls);
    }

    // ==================================================================
    // Model
    // ==================================================================

    [Theory]
    [InlineData(0, 0, "")]
    [InlineData(2, 5, "2 (5B)")]
    [InlineData(1, 8, "1 (8B)")]
    public void PeProfileEntry_ParamsLabel_Formats(byte numParms, ushort parmsSize, string expected)
    {
        var e = new PeProfileEntry { NumParms = numParms, ParmsSize = parmsSize };
        Assert.Equal(expected, e.ParamsLabel);
    }

    // ==================================================================
    // Truncated fetch (AF3) — the DLL sorts count-DESC and emits only the top
    // FetchLimit rows while distinct_funcs stays pre-cap, so the page can be a
    // strict subset. The panel's target is a LOW-count function, i.e. exactly
    // what the cap removes, so silence about it is the defect.
    // ==================================================================

    [Fact]
    public async Task Truncated_NonDiffStatus_SaysHowManyOfHowMany()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = TruncatedResultOf(900,
            new PeProfileEntry { ClassName = "APawn", FuncName = "Tick",   Count = 900 },
            new PeProfileEntry { ClassName = "AHUD",  FuncName = "Update", Count = 300 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.Contains("showing top 2 of 900", vm.StatusText);
    }

    // [TRACE-UNLOADED-NAMES] D1: functions with no name at all are never sent, so they are not "cut by the limit";
    // the unloaded ones are sent, named, and counted.
    [Fact]
    public async Task Unnamed_functions_are_not_reported_as_cut_by_the_fetch_limit()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = new PeProfileResult
        {
            DistinctFuncs = 4, TotalCalls = 100, UnloadedFuncs = 1, UnloadedCalls = 5, UnnamedFuncs = 1, UnnamedCalls = 2,
            Entries = new()
            {
                new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 90 },
                new PeProfileEntry { ClassName = "AHUD", FuncName = "Draw", Count = 3 },
                new PeProfileEntry { ClassName = "WBP_Inventory_C", FuncName = "OnOpen", Count = 5, IsUnloaded = true },
            },
        };
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.DoesNotContain("showing top", vm.StatusText);
        Assert.Equal(1, vm.LastUnloadedFuncs);
        Assert.Equal(1, vm.LastUnnamedFuncs);
    }

    [Fact]
    public async Task NotTruncated_NonDiffStatus_HasNoCapNote()
    {
        // Negative control for the test above: the note must not appear when the
        // whole table came back, or it is noise on every ordinary fetch.
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.DoesNotContain("showing top", vm.StatusText);
    }

    [Fact]
    public async Task Truncated_SetBaseline_MarksBaselinePartial()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = TruncatedResultOf(900,
            new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        vm.SetBaselineCommand.Execute(null);

        Assert.Contains("PARTIAL", vm.BaselineStatus);
        Assert.Contains("900", vm.BaselineStatus);
        Assert.True(vm.DiffMode);   // still usable — refusing would disable Diff on busy games
    }

    [Fact]
    public async Task WholeBaseline_SetBaseline_IsNotMarkedPartial()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        vm.SetBaselineCommand.Execute(null);

        Assert.DoesNotContain("PARTIAL", vm.BaselineStatus);
    }

    [Fact]
    public async Task PartialBaseline_DiffStatus_DropsTheCertaintyClaim()
    {
        // The actively harmful sentence: with a capped baseline a rare idle function
        // that ranked below the cut comes back as NEW and sorts to the very top, so
        // "almost certainly among the NEW rows" points at a fabricated row.
        var (vm, dump) = MakeVm();
        dump.NextGet = TruncatedResultOf(900,
            new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);

        dump.NextGet = TruncatedResultOf(900,
            new PeProfileEntry { ClassName = "APawn",       FuncName = "Tick",     Count = 1000 },
            new PeProfileEntry { ClassName = "AShopVendor", FuncName = "OpenShop", Count = 2 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.DoesNotContain("almost certainly", vm.StatusText);
        Assert.Contains("not in the idle top N", vm.StatusText);
    }

    [Fact]
    public async Task WholeBaseline_DiffStatus_KeepsTheCertaintyClaim()
    {
        // Negative control: when nothing was capped, the original guidance is correct
        // and must survive — otherwise the fix has just removed a working hint.
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);

        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "APawn",       FuncName = "Tick",     Count = 1000 },
            new PeProfileEntry { ClassName = "AShopVendor", FuncName = "OpenShop", Count = 2 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.Contains("almost certainly", vm.StatusText);
        Assert.DoesNotContain("not in the idle top N", vm.StatusText);
    }

    [Fact]
    public async Task DiffStatus_CountsAreReportedAgainstThePageNotTheTable()
    {
        // "1 NEW of 900" invited reading 900 as the population those rows were
        // selected from, when only the 2 shown were ever examined.
        var (vm, dump) = MakeVm();
        dump.NextGet = TruncatedResultOf(900,
            new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);

        dump.NextGet = TruncatedResultOf(900,
            new PeProfileEntry { ClassName = "APawn",       FuncName = "Tick",     Count = 1000 },
            new PeProfileEntry { ClassName = "AShopVendor", FuncName = "OpenShop", Count = 2 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.Contains("of 2 shown; 900 recorded", vm.StatusText);
    }

    [Fact]
    public async Task ClearBaseline_ResetsThePartialFlag()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = TruncatedResultOf(900,
            new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);
        Assert.Contains("PARTIAL", vm.BaselineStatus);

        vm.ClearBaselineCommand.Execute(null);

        // A stale _baselineTruncated would keep warning after a clean re-capture.
        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);

        Assert.DoesNotContain("PARTIAL", vm.BaselineStatus);
    }

    [Fact]
    public async Task SetBaseline_ValueDoesNotDependOnTheEarliestFirstToggle()
    {
        // Not filed in AF3. _allEntries is re-sorted IN PLACE by ApplyDiffAndFilter, and
        // Earliest-first orders it by FirstSeq — so GroupBy(...).First() captured whichever
        // duplicate-key row happened to be on top of the CURRENT VIEW. Max() is what First()
        // was reaching for and is sort-independent.
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900, FirstSeq = 50 },
            new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 5,   FirstSeq = 1 });
        vm.EarliestFirst = true;
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);

        // Action window: the same key at 900. Against a baseline of 900 that is Δ0 and is
        // hidden by NewChangedOnly. Against a baseline of 5 (the FirstSeq-ordered First())
        // it would read as +895 and be presented as caused by the action.
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900, FirstSeq = 50 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.Results, r => r.FuncName == "Tick");
    }

    // ==================================================================
    // [EXTPR-539-540-2026-10-02] L1: the fetch limit is a slider over powers of two,
    // 2^6 = 64 .. 2^15 = 32768, default 2^9 = 512. A recording keeps the value it
    // started with (L4).
    // ==================================================================

    [Fact]
    public async Task FetchLimit_Default_AsksFor512()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 9 });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(9, vm.FetchLimitExponent);
        Assert.Equal(512, vm.FetchLimit);
        Assert.Equal(512, dump.LastLimit);
    }

    [Fact]
    public async Task FetchLimit_FollowsTheExponent_UpTo32768()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 9 });

        vm.FetchLimitExponent = 15;
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(32768, vm.FetchLimit);
        Assert.Equal(32768, dump.LastLimit);
    }

    [Theory]
    [InlineData(5, 6)]
    [InlineData(-3, 6)]
    [InlineData(16, 15)]
    [InlineData(99, 15)]
    [InlineData(12, 12)]
    public void FetchLimitExponent_StaysInTheSliderRange(int set, int expected)
    {
        // A hand-edited ui-options.json reaches the property without the slider's own bounds.
        var (vm, _) = MakeVm();

        vm.FetchLimitExponent = set;

        Assert.Equal(expected, vm.FetchLimitExponent);
        Assert.Equal(1 << expected, vm.FetchLimit);
    }

    [Fact]
    public async Task FetchLimit_ARecordingUsesTheValueItStartedWith()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 9 });
        vm.FetchLimitExponent = 7;                  // 128
        await vm.StartCommand.ExecuteAsync(null);

        vm.FetchLimitExponent = 12;                 // the slider is disabled now; a change reaches the VM anyway
        await vm.RefreshCommand.ExecuteAsync(null); // a peek during the recording
        Assert.Equal(128, dump.LastLimit);
        await vm.StopCommand.ExecuteAsync(null);    // Stop's own fetch belongs to the recording too
        Assert.Equal(128, dump.LastLimit);

        await vm.RefreshCommand.ExecuteAsync(null); // after it, a re-pull uses the current value
        Assert.Equal(4096, dump.LastLimit);
    }

    // ==================================================================
    // [EXTPR-539-540-2026-10-02] When does a higher Fetch limit help? Only when the DLL sent
    // as many rows as it was asked for (the cap is what cut the page) and the slider can still
    // go higher. A page short of BOTH the limit and distinct_funcs lost rows the DLL could not
    // read (stale UFunctions) or an abort cut it; no limit brings those back. Measured on
    // Avowed 2026-10-06: 648 distinct, 543 rows at every limit from 8192 up.
    // ==================================================================

    private static PeProfileEntry[] Rows(int n)
        => Enumerable.Range(0, n).Select(i => new PeProfileEntry
        {
            ClassName = "C" + i, FuncName = "F" + i, Count = n - i, FuncAddr = "0x" + (i + 1).ToString("X"),
        }).ToArray();

    [Fact]
    public async Task CapHit_BelowTheMaximum_RaiseHelps()
    {
        var (vm, dump) = MakeVm();
        vm.FetchLimitExponent = 6;   // 64
        dump.NextGet = TruncatedResultOf(900, Rows(64));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.RaiseFetchLimitHelps);
    }

    [Fact]
    public async Task ShortOfTheLimit_RaiseDoesNotHelp()
    {
        var (vm, dump) = MakeVm();   // 512
        dump.NextGet = TruncatedResultOf(900, Rows(2));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.RaiseFetchLimitHelps);
        Assert.Contains("showing top 2 of 900", vm.StatusText);   // still reported as incomplete
    }

    [Fact]
    public async Task CapHit_AtTheMaximum_RaiseDoesNotHelp()
    {
        var (vm, dump) = MakeVm();
        vm.FetchLimitExponent = 15;   // 32768, the slider's end
        dump.NextGet = TruncatedResultOf(40000, Rows(32768));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.RaiseFetchLimitHelps);
    }

    [Fact]
    public async Task WholePage_RaiseDoesNotHelp()
    {
        var (vm, dump) = MakeVm();
        vm.FetchLimitExponent = 6;
        dump.NextGet = ResultOf(Rows(64));   // exactly the limit, but nothing more was recorded

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.RaiseFetchLimitHelps);
    }

    [Fact]
    public async Task BaselineCutByTheCap_RaiseHelps_EvenWhenTheActionPageIsWhole()
    {
        // The baseline has to be recorded again with a higher limit, so the advice stands.
        var (vm, dump) = MakeVm();
        vm.FetchLimitExponent = 6;
        dump.NextGet = TruncatedResultOf(900, Rows(64));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);

        dump.NextGet = ResultOf(Rows(3));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.True(vm.RaiseFetchLimitHelps);
    }

    [Fact]
    public async Task ClearBaseline_ForgetsTheBaselinesCap()
    {
        var (vm, dump) = MakeVm();
        vm.FetchLimitExponent = 6;
        dump.NextGet = TruncatedResultOf(900, Rows(64));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);
        dump.NextGet = ResultOf(Rows(3));
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.ClearBaselineCommand.Execute(null);

        Assert.False(vm.RaiseFetchLimitHelps);
    }

    // ==================================================================
    // [EXTPR-539-540-2026-10-02] L2: Save .jsonl writes the rows on screen, in the order the
    // game first called them, under one summary line. Disabled while recording (L4).
    // ==================================================================

    /// <summary>A platform whose save dialog answers with a temp path (or with nothing, to cancel).</summary>
    private sealed class SavePlatform : IPlatformService
    {
        public string? Answer { get; set; } = Path.Combine(Path.GetTempPath(), $"lf-save-{Guid.NewGuid():N}.jsonl");
        public int Asked { get; private set; }
        public string? DefaultName { get; private set; }
        public string? Extension { get; private set; }

        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName, string filterExtension)
        {
            Asked++;
            DefaultName = defaultFileName;
            Extension = filterExtension;
            return Task.FromResult(Answer);
        }
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetLogDirectoryPath() => Path.GetTempPath();
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST";
        public void CloseImeForWindow(IntPtr windowHandle) { }
    }

    private static (LiveFuncsViewModel vm, FakeDumpService dump, SavePlatform platform) MakeSavingVm()
    {
        var dump = new FakeDumpService();
        var platform = new SavePlatform();
        return (new LiveFuncsViewModel(dump, new NoopLogger(), platform), dump, platform);
    }

    // ==================================================================
    // [LIVEFUNCS-HIDE-PERFRAME] Hide per-frame: asked of the DLL, fixed at Start, and the cut counted without it.
    // ==================================================================

    private static PeProfileResult PerFramePage(int shown, int distinct, int? perFrameHidden) => new()
    {
        DistinctFuncs = distinct, TotalCalls = 100_000, PerFrameHidden = perFrameHidden,
        Entries = Enumerable.Range(0, shown).Select(i => new PeProfileEntry
            { ClassName = "A", FuncName = "F" + i, Count = 100 - i % 50, FirstSeq = i + 1, FuncAddr = "0x" + i }).ToList(),
    };

    [Fact]
    public async Task HidePerFrame_IsOff_ByDefault_AndAskedOfTheDllWhenOn()
    {
        var dump = new FakeDumpService { NextGet = PerFramePage(3, 3, null) };
        var vm = new LiveFuncsViewModel(dump, new NoopLogger());
        Assert.False(vm.HidePerFrame);
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.False(dump.LastSkipPerFrame);

        vm.HidePerFrame = true;
        dump.NextGet = PerFramePage(3, 3, 5);
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.True(dump.LastSkipPerFrame);
    }

    [Fact]
    public async Task HidePerFrame_IsFixedAtStart_LikeTheFetchLimit()
    {
        var dump = new FakeDumpService { NextGet = PerFramePage(3, 3, 0) };
        var vm = new LiveFuncsViewModel(dump, new NoopLogger()) { HidePerFrame = true };
        await vm.StartCommand.ExecuteAsync(null);
        vm.HidePerFrame = false;                       // moved while recording
        await vm.RefreshCommand.ExecuteAsync(null);    // a peek ranks the recording as it began
        Assert.True(dump.LastSkipPerFrame);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.True(dump.LastSkipPerFrame);

        await vm.RefreshCommand.ExecuteAsync(null);    // after Stop, a re-pull takes the current value
        Assert.False(dump.LastSkipPerFrame);
    }

    [Fact]
    public async Task HidePerFrame_TheRowsLeftOutAreNotRowsTheLimitCut()
    {
        // 1000 distinct, 488 of them per-frame and left out, 512 shown at a 512 limit: nothing else is missing, so
        // no higher limit is offered. Without the count from the DLL the same page reads as cut.
        var dump = new FakeDumpService { NextGet = PerFramePage(512, 1000, 488) };
        var vm = new LiveFuncsViewModel(dump, new NoopLogger()) { HidePerFrame = true };
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(488, vm.LastPerFrameHidden);
        Assert.False(vm.RaiseFetchLimitHelps);

        dump.NextGet = PerFramePage(512, 1000, 400);   // 88 more were cut by the limit
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.True(vm.RaiseFetchLimitHelps);
    }

    [Fact]
    public async Task HidePerFrame_AnOlderDllThatLeftNothingOut_IsSaidSo()
    {
        var dump = new FakeDumpService { NextGet = PerFramePage(3, 3, null) };
        var vm = new LiveFuncsViewModel(dump, new NoopLogger()) { HidePerFrame = true };
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.True(vm.PerFrameUnsupported);
        Assert.Equal(0, vm.LastPerFrameHidden);

        dump.NextGet = PerFramePage(3, 10, 7);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.PerFrameUnsupported);
        Assert.Equal(7, vm.LastPerFrameHidden);
    }

    [Fact]
    public async Task HidePerFrame_ABaselineFetchedTheOtherWay_IsFlagged()
    {
        // Per-frame rows missing from one side only would all come back NEW (or vanish) in the diff.
        var dump = new FakeDumpService { NextGet = PerFramePage(3, 3, null) };
        var vm = new LiveFuncsViewModel(dump, new NoopLogger());
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);
        Assert.False(vm.BaselinePerFrameMismatch);

        vm.HidePerFrame = true;
        dump.NextGet = PerFramePage(3, 10, 7);
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.True(vm.BaselinePerFrameMismatch);

        vm.SetBaselineCommand.Execute(null);           // a baseline fetched the same way
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.False(vm.BaselinePerFrameMismatch);

        // A DLL that ignored the option left nothing out: that page matches a baseline that hid nothing.
        vm.HidePerFrame = false;
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);
        vm.HidePerFrame = true;
        dump.NextGet = PerFramePage(3, 3, null);
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.False(vm.BaselinePerFrameMismatch);
    }

    [Fact]
    public async Task HidePerFrame_AFunctionLeftOutOfTheBaseline_IsNotNew_WhenTheActionFiredItLess()
    {
        // Review: the idle baseline left the HUD's Tick out as per-frame; the action paused the game for a menu, so
        // the Tick fired too little to be per-frame there and came back -- with no baseline row, it read NEW and
        // topped the New/changed-only list the baseline exists to clean.
        var dump = new FakeDumpService
        {
            NextGet = new PeProfileResult
            {
                DistinctFuncs = 2, TotalCalls = 700, PerFrameHidden = 1, PerFrameFuncs = new[] { "0xA" },
                Entries = new() { new PeProfileEntry { ClassName = "AIdle", FuncName = "Wander", Count = 3, FirstSeq = 5, FuncAddr = "0xB" } },
            },
        };
        var vm = new LiveFuncsViewModel(dump, new NoopLogger()) { HidePerFrame = true };
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);

        dump.NextGet = new PeProfileResult
        {
            DistinctFuncs = 3, TotalCalls = 200, PerFrameHidden = 0, PerFrameFuncs = Array.Empty<string>(),
            Entries = new()
            {
                new PeProfileEntry { ClassName = "AHUD", FuncName = "ReceiveTick", Count = 120, FirstSeq = 1, FuncAddr = "0xA" },
                new PeProfileEntry { ClassName = "AShop", FuncName = "OpenShop", Count = 1, FirstSeq = 9, FuncAddr = "0xC" },
            },
        };
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.Results, r => r.FuncName == "ReceiveTick");   // New/changed-only is on by default
        Assert.Contains(vm.Results, r => r.FuncName == "OpenShop" && r.IsNew);
        vm.NewChangedOnly = false;
        var tick = Assert.Single(vm.Results, r => r.FuncName == "ReceiveTick");
        Assert.False(tick.IsNew);
    }

    [Fact]
    public async Task HidePerFrame_APartialBaseline_CountsWithoutTheLeftOutFunctions()
    {
        // 1000 distinct, 600 left out as per-frame, 64 shown at a 64 limit: 64 of 400 were fetched, not of 1,000.
        var dump = new FakeDumpService { NextGet = PerFramePage(64, 1000, 600) };
        var vm = new LiveFuncsViewModel(dump, new NoopLogger()) { HidePerFrame = true, FetchLimitExponent = 6 };
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);
        Assert.Contains("64 of 400", vm.BaselineStatus);
    }

    [Fact]
    public async Task HidePerFrame_AgainstAMismatchedBaseline_TheStatusMakesNoClaimAboutTheNewRows()
    {
        var dump = new FakeDumpService { NextGet = PerFramePage(3, 10, 7) };
        var vm = new LiveFuncsViewModel(dump, new NoopLogger()) { HidePerFrame = true };
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);

        vm.HidePerFrame = false;                       // the action fetched the other way
        dump.NextGet = PerFramePage(10, 10, null);
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.True(vm.BaselinePerFrameMismatch);
        Assert.DoesNotContain("almost certainly", vm.StatusText);
    }

    [Fact]
    public async Task SaveJsonl_InDiffMode_RecordsWhetherTheBaselineLeftThePerFrameFunctionsOut()
    {
        var (vm, dump, platform) = MakeSavingVm();
        vm.HidePerFrame = true;
        dump.NextGet = PerFramePage(3, 10, 7);
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.NewChangedOnly = false;                     // keep rows on screen to save
        await vm.SaveJsonlCommand.ExecuteAsync(null);
        try
        {
            var head = ReadLines(platform.Answer!)[0].RootElement;
            Assert.True(head.GetProperty("diff").GetBoolean());
            Assert.True(head.GetProperty("baseline_hide_per_frame").GetBoolean());
        }
        finally { File.Delete(platform.Answer!); }
    }

    [Fact]
    public async Task SaveJsonl_RecordsHidePerFrame_AndHowManyWereLeftOut()
    {
        var (vm, dump, platform) = MakeSavingVm();
        vm.HidePerFrame = true;
        dump.NextGet = PerFramePage(2, 9, 7);
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        await vm.SaveJsonlCommand.ExecuteAsync(null);
        try
        {
            var head = ReadLines(platform.Answer!)[0].RootElement;
            Assert.True(head.GetProperty("hide_per_frame").GetBoolean());
            Assert.Equal(7, head.GetProperty("per_frame_hidden").GetInt32());
        }
        finally { File.Delete(platform.Answer!); }

        // Off: the key says so, and no count is claimed.
        var (vm2, dump2, platform2) = MakeSavingVm();
        dump2.NextGet = PerFramePage(2, 2, null);
        await vm2.StartCommand.ExecuteAsync(null);
        await vm2.StopCommand.ExecuteAsync(null);
        await vm2.SaveJsonlCommand.ExecuteAsync(null);
        try
        {
            var head = ReadLines(platform2.Answer!)[0].RootElement;
            Assert.False(head.GetProperty("hide_per_frame").GetBoolean());
            Assert.False(head.TryGetProperty("per_frame_hidden", out _));
        }
        finally { File.Delete(platform2.Answer!); }
    }

    [Fact]
    public void HidePerFrame_PersistsThroughTheMainWindow()
    {
        // MainWindowViewModel cannot be built in a unit test; pin its persistence sites by source.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "build.ps1"))) root = root.Parent;
        Assert.NotNull(root);
        var src = File.ReadAllText(Path.Combine(root!.FullName, "ui", "UE5DumpUI", "ViewModels", "MainWindowViewModel.cs"));
        Assert.Contains("nameof(LiveFuncsViewModel.HidePerFrame)", src);
        Assert.Contains("LiveFuncs.HidePerFrame = o.LiveFuncs.HidePerFrame", src);
        Assert.Contains("o.LiveFuncs.HidePerFrame = LiveFuncs.HidePerFrame", src);
    }

    private static List<System.Text.Json.JsonDocument> ReadLines(string path)
        => File.ReadAllLines(path).Where(l => l.Length > 0).Select(l => System.Text.Json.JsonDocument.Parse(l)).ToList();

    [Fact]
    public async Task SaveJsonl_WritesTheRowsOnScreen_InFirstCallOrder_UnderASummaryLine()
    {
        var (vm, dump, platform) = MakeSavingVm();
        dump.NextGet = new PeProfileResult
        {
            DistinctFuncs = 5, TotalCalls = 1234,
            Entries = new()
            {
                new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900, FirstSeq = 5, FuncAddr = "0x10" },
                new PeProfileEntry { ClassName = "AShop", FuncName = "OpenShop", Count = 3, FirstSeq = 2, FuncAddr = "0x20",
                                     NumParms = 2, ParmsSize = 16 },
                new PeProfileEntry { ClassName = "AHUD", FuncName = "Draw", Count = 40, FirstSeq = 0, FuncAddr = "0x30" },
            },
        };
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        await vm.SaveJsonlCommand.ExecuteAsync(null);

        Assert.Equal(1, platform.Asked);
        Assert.Equal(".jsonl", platform.Extension);   // the dialog builds "*" + this as its pattern
        Assert.StartsWith("live-funcs-", platform.DefaultName);
        Assert.EndsWith(".jsonl", platform.DefaultName);
        try
        {
            var lines = ReadLines(platform.Answer!);
            Assert.Equal(4, lines.Count);
            var head = lines[0].RootElement;
            Assert.Equal("live_funcs", head.GetProperty("kind").GetString());
            Assert.Equal(3, head.GetProperty("rows").GetInt32());
            Assert.Equal(3, head.GetProperty("fetched").GetInt32());
            Assert.Equal(5, head.GetProperty("distinct").GetInt32());
            Assert.Equal(1234, head.GetProperty("total_calls").GetInt64());
            Assert.Equal(512, head.GetProperty("fetch_limit").GetInt32());
            Assert.False(head.GetProperty("diff").GetBoolean());
            Assert.Equal("", head.GetProperty("filter").GetString());
            Assert.True(head.TryGetProperty("saved_at", out _));

            // First call first; an unknown order (0) goes last.
            Assert.Equal(new[] { "OpenShop", "Tick", "Draw" },
                         lines.Skip(1).Select(l => l.RootElement.GetProperty("func").GetString()).ToArray());
            var shop = lines[1].RootElement;
            Assert.Equal("func", shop.GetProperty("kind").GetString());
            Assert.Equal("AShop", shop.GetProperty("class").GetString());
            Assert.Equal(2, shop.GetProperty("order").GetInt64());
            Assert.Equal(3, shop.GetProperty("calls").GetInt64());
            Assert.Equal("0x20", shop.GetProperty("addr").GetString());
            Assert.Equal(2, shop.GetProperty("params").GetInt32());
            Assert.Equal(16, shop.GetProperty("params_size").GetInt32());
        }
        finally { File.Delete(platform.Answer!); }
    }

    [Fact]
    public async Task SaveJsonl_MarksTheUnloadedRows()
    {
        // [TRACE-UNLOADED-NAMES] A dead address must not read as a real one in the file.
        var (vm, dump, platform) = MakeSavingVm();
        dump.NextGet = new PeProfileResult
        {
            DistinctFuncs = 2, TotalCalls = 20,
            Entries = new()
            {
                new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 15, FirstSeq = 1, FuncAddr = "0x10" },
                new PeProfileEntry { ClassName = "WBP_Inventory_C", FuncName = "OnOpen", Count = 5, FirstSeq = 2,
                                     FuncAddr = "0x20", IsUnloaded = true },
            },
        };
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        await vm.SaveJsonlCommand.ExecuteAsync(null);
        try
        {
            var lines = ReadLines(platform.Answer!);
            Assert.False(lines[1].RootElement.GetProperty("unloaded").GetBoolean());
            Assert.True(lines[2].RootElement.GetProperty("unloaded").GetBoolean());
        }
        finally
        {
            File.Delete(platform.Answer!);
        }
    }

    [Fact]
    public async Task SaveJsonl_LeavesOutRowsTheFilterHides_AndRecordsTheFilter()
    {
        var (vm, dump, platform) = MakeSavingVm();
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "AShop", FuncName = "OpenShop", Count = 3, FirstSeq = 2 },
            new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900, FirstSeq = 1 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.FilterText = "shop";

        await vm.SaveJsonlCommand.ExecuteAsync(null);

        try
        {
            var lines = ReadLines(platform.Answer!);
            Assert.Equal(2, lines.Count);
            Assert.Equal(1, lines[0].RootElement.GetProperty("rows").GetInt32());
            Assert.Equal(2, lines[0].RootElement.GetProperty("fetched").GetInt32());
            Assert.Equal("shop", lines[0].RootElement.GetProperty("filter").GetString());
            Assert.Equal("OpenShop", lines[1].RootElement.GetProperty("func").GetString());
        }
        finally { File.Delete(platform.Answer!); }
    }

    [Fact]
    public async Task SaveJsonl_NamesThatNeedEscaping_ReadBackUnchanged()
    {
        var (vm, dump, platform) = MakeSavingVm();
        const string cls = "AShop\"Vendor\\Ü 商店";
        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = cls, FuncName = "Open\tShop", Count = 1, FirstSeq = 1 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        await vm.SaveJsonlCommand.ExecuteAsync(null);

        try
        {
            var row = ReadLines(platform.Answer!)[1].RootElement;
            Assert.Equal(cls, row.GetProperty("class").GetString());
            Assert.Equal("Open\tShop", row.GetProperty("func").GetString());
        }
        finally { File.Delete(platform.Answer!); }
    }

    [Fact]
    public async Task SaveJsonl_InDiffMode_CarriesTheDeltaAndTheNewFlag()
    {
        var (vm, dump, platform) = MakeSavingVm();
        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900, FirstSeq = 1 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 950, FirstSeq = 1 },
            new PeProfileEntry { ClassName = "AShop", FuncName = "OpenShop", Count = 2, FirstSeq = 3 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.NewChangedOnly = false;

        await vm.SaveJsonlCommand.ExecuteAsync(null);

        try
        {
            var lines = ReadLines(platform.Answer!);
            Assert.True(lines[0].RootElement.GetProperty("diff").GetBoolean());
            Assert.Equal(1, lines[0].RootElement.GetProperty("baseline_funcs").GetInt32());
            var tick = lines.Skip(1).Single(l => l.RootElement.GetProperty("func").GetString() == "Tick").RootElement;
            var shop = lines.Skip(1).Single(l => l.RootElement.GetProperty("func").GetString() == "OpenShop").RootElement;
            Assert.Equal(50, tick.GetProperty("delta").GetInt64());
            Assert.False(tick.GetProperty("new").GetBoolean());
            Assert.True(shop.GetProperty("new").GetBoolean());
        }
        finally { File.Delete(platform.Answer!); }
    }

    [Fact]
    public async Task SaveJsonl_WhileRecording_DoesNotAsk()
    {
        var (vm, dump, platform) = MakeSavingVm();
        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 9 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.RefreshCommand.ExecuteAsync(null);   // rows on screen, still recording

        await vm.SaveJsonlCommand.ExecuteAsync(null);

        Assert.Equal(0, platform.Asked);
    }

    [Fact]
    public async Task SaveJsonl_EmptyTable_DoesNotAsk()
    {
        var (vm, _, platform) = MakeSavingVm();

        await vm.SaveJsonlCommand.ExecuteAsync(null);

        Assert.Equal(0, platform.Asked);
    }

    [Fact]
    public async Task SaveJsonl_Cancelled_WritesNothing()
    {
        var (vm, dump, platform) = MakeSavingVm();
        string path = platform.Answer!;
        platform.Answer = null;   // the user closed the dialog
        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 9 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        await vm.SaveJsonlCommand.ExecuteAsync(null);

        Assert.Equal(1, platform.Asked);
        Assert.False(File.Exists(path));
    }

    // Review of a20af931: the summary must say why rows are missing and whether NEW can be trusted.

    [Fact]
    public async Task SaveJsonl_RecordsTheCheckBoxesThatHideRows()
    {
        var (vm, dump, platform) = MakeSavingVm();
        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 9, FirstSeq = 1 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.HideWidgets = true;
        vm.HideEvents = true;

        await vm.SaveJsonlCommand.ExecuteAsync(null);

        try
        {
            var head = ReadLines(platform.Answer!)[0].RootElement;
            Assert.True(head.GetProperty("hide_widgets").GetBoolean());
            Assert.True(head.GetProperty("hide_events").GetBoolean());
            Assert.False(head.GetProperty("periodic_only").GetBoolean());
            Assert.False(head.TryGetProperty("new_changed_only", out _));   // only means something in diff mode
        }
        finally { File.Delete(platform.Answer!); }
    }

    [Fact]
    public async Task SaveJsonl_InDiffMode_SaysWhetherTheBaselineWasPartial()
    {
        var (vm, dump, platform) = MakeSavingVm();
        dump.NextGet = TruncatedResultOf(900, new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900, FirstSeq = 1 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);
        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = "AShop", FuncName = "OpenShop", Count = 2, FirstSeq = 1 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        await vm.SaveJsonlCommand.ExecuteAsync(null);

        try
        {
            var head = ReadLines(platform.Answer!)[0].RootElement;
            Assert.True(head.GetProperty("baseline_partial").GetBoolean());
            Assert.Equal(900, head.GetProperty("baseline_distinct").GetInt32());
            Assert.True(head.GetProperty("new_changed_only").GetBoolean());
        }
        finally { File.Delete(platform.Answer!); }
    }

    [Fact]
    public async Task SaveJsonl_RowsFromAPeekDuringTheRecording_SaySo()
    {
        // A Refresh during the recording leaves its rows on screen when the recording ends without a
        // fetch (a disconnect, leaving the tab), and Save is enabled again.
        var (vm, dump, platform) = MakeSavingVm();
        dump.NextGet = new PeProfileResult
        {
            Recording = true, DistinctFuncs = 1, TotalCalls = 9,
            Entries = new() { new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 9, FirstSeq = 1 } },
        };
        await vm.StartCommand.ExecuteAsync(null);
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.ResetOnDisconnect();

        await vm.SaveJsonlCommand.ExecuteAsync(null);

        try
        {
            Assert.True(ReadLines(platform.Answer!)[0].RootElement.GetProperty("recording_at_fetch").GetBoolean());
        }
        finally { File.Delete(platform.Answer!); }
    }

    [Fact]
    public async Task SaveJsonl_CadenceValuesReadBackExactly_AndThePanelsVerdictIsSaved()
    {
        // Just past the Timer thresholds: a rounded cv of 0.25 would turn the panel's "not periodic" into "periodic".
        var entry = new PeProfileEntry
        {
            ClassName = "AHUD", FuncName = "Pulse", Count = 30, FirstSeq = 1,
            MeanPeriodMs = 40.0004, Cv = 0.2504, GapSamples = 9,
        };
        var (vm, dump, platform) = MakeSavingVm();
        dump.NextGet = ResultOf(entry);
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        await vm.SaveJsonlCommand.ExecuteAsync(null);

        try
        {
            var row = ReadLines(platform.Answer!)[1].RootElement;
            Assert.Equal(0.2504, row.GetProperty("cv").GetDouble());
            Assert.Equal(40.0004, row.GetProperty("period_ms").GetDouble());
            Assert.Equal(entry.IsPeriodic, row.GetProperty("periodic").GetBoolean());
            Assert.Equal(entry.Kind, row.GetProperty("badge").GetString());
        }
        finally { File.Delete(platform.Answer!); }
    }

    [Fact]
    public async Task SetBaseline_AgainWhileDiffIsOn_RecomputesTheRowsAgainstTheNewBaseline()
    {
        // DiffMode = true re-applied the diff only when it CHANGED, so a second Set Baseline kept every row's
        // Delta / IsNew against the old baseline while the status (and a saved file) named the new one.
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 900 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);
        dump.NextGet = ResultOf(
            new PeProfileEntry { ClassName = "APawn", FuncName = "Tick", Count = 950 },
            new PeProfileEntry { ClassName = "AShop", FuncName = "OpenShop", Count = 2 });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.NewChangedOnly = false;
        Assert.Contains(vm.Results, r => r.IsNew);   // against the idle baseline

        vm.SetBaselineCommand.Execute(null);          // this table is now the baseline

        Assert.Equal(2, vm.Results.Count);
        Assert.All(vm.Results, r => { Assert.False(r.IsNew); Assert.Equal(0, r.Delta); });
    }

    // ==================================================================
    // [EXTPR-539-540-2026-10-02] L3: Min calls, a slider over 1, 2, 4 .. 32 (exponent 0..5,
    // default 1). It hides rows with FEWER calls (Count < MinCalls, so the default hides
    // nothing; R1: NEW rows get no exemption). It is a VIEW filter on the capture it was set
    // for: read at Start, kept for that capture, and moving it later changes nothing until the
    // next Start. The baseline is built from every fetched row, never from the filtered view.
    // ==================================================================

    private static PeProfileResult CallsResult(params (string func, long count)[] rows)
        => ResultOf(rows.Select((r, i) => new PeProfileEntry
        {
            ClassName = "C", FuncName = r.func, Count = r.count, FirstSeq = i + 1, FuncAddr = "0x" + (i + 1).ToString("X"),
        }).ToArray());

    [Fact]
    public async Task MinCalls_Default_IsOne_AndHidesNothing()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = CallsResult(("Once", 1), ("Often", 50));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.Equal(0, vm.MinCallsExponent);
        Assert.Equal(1, vm.MinCalls);
        Assert.Equal(2, vm.Results.Count);
    }

    [Fact]
    public async Task MinCalls_HidesRowsWithFewerCalls_KeepsTheEqualOne()
    {
        var (vm, dump) = MakeVm();
        vm.MinCallsExponent = 2;   // 4
        dump.NextGet = CallsResult(("Three", 3), ("Four", 4), ("Fifty", 50));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Four", "Fifty" }, vm.Results.Select(r => r.FuncName).OrderBy(n => n).Reverse().ToArray());
    }

    [Fact]
    public async Task MinCalls_MovedAfterStart_ChangesNothingUntilTheNextStart()
    {
        var (vm, dump) = MakeVm();
        vm.MinCallsExponent = 2;   // 4, read at Start
        dump.NextGet = CallsResult(("Three", 3), ("Fifty", 50));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        vm.MinCallsExponent = 0;                      // moved after the capture
        Assert.Single(vm.Results);
        await vm.RefreshCommand.ExecuteAsync(null);   // a re-pull of the same capture
        Assert.Single(vm.Results);

        await vm.StartCommand.ExecuteAsync(null);     // the next capture reads 1
        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Results.Count);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(6, 5)]
    [InlineData(40, 5)]
    [InlineData(3, 3)]
    public void MinCallsExponent_StaysInTheSliderRange(int set, int expected)
    {
        var (vm, _) = MakeVm();

        vm.MinCallsExponent = set;

        Assert.Equal(expected, vm.MinCallsExponent);
        Assert.Equal(1 << expected, vm.MinCalls);
    }

    [Fact]
    public async Task MinCalls_HidesANewRowToo()
    {
        // R1 (maintainer, 2026-10-06): no exemption for NEW rows.
        var (vm, dump) = MakeVm();
        dump.NextGet = CallsResult(("Tick", 900));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);

        vm.MinCallsExponent = 2;   // 4, for the action capture
        dump.NextGet = CallsResult(("Tick", 950), ("OpenShop", 2));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.Results, r => r.FuncName == "OpenShop");
    }

    [Fact]
    public async Task MinCalls_TheBaselineStillHoldsTheRowsItHid()
    {
        // A view filter: if it cut what SetBaseline reads, an idle function with few calls would be
        // missing from the baseline and come back as a false NEW.
        var (vm, dump) = MakeVm();
        vm.MinCallsExponent = 2;   // 4
        dump.NextGet = CallsResult(("Tick", 900), ("Rare", 2));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.DoesNotContain(vm.Results, r => r.FuncName == "Rare");
        vm.SetBaselineCommand.Execute(null);

        dump.NextGet = CallsResult(("Tick", 950), ("Rare", 6));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        var rare = Assert.Single(vm.Results, r => r.FuncName == "Rare");
        Assert.False(rare.IsNew);
        Assert.Equal(4, rare.Delta);
    }

    [Fact]
    public async Task SaveJsonl_RecordsTheMinCallsOfTheCapture()
    {
        var (vm, dump, platform) = MakeSavingVm();
        vm.MinCallsExponent = 3;   // 8
        dump.NextGet = CallsResult(("Fifty", 50));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.MinCallsExponent = 0;   // moved afterwards: the rows still came from 8

        await vm.SaveJsonlCommand.ExecuteAsync(null);

        try
        {
            Assert.Equal(8, ReadLines(platform.Answer!)[0].RootElement.GetProperty("min_calls").GetInt32());
        }
        finally { File.Delete(platform.Answer!); }
    }

    // Review of f45dca96: the minimum belongs to the rows it filtered, not to the latest Start.

    [Fact]
    public async Task MinCalls_ANewStart_DoesNotRefilterTheRowsStillOnScreen()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = CallsResult(("Once", 1), ("Often", 50));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Results.Count);

        vm.MinCallsExponent = 3;                    // 8, for the next capture
        await vm.StartCommand.ExecuteAsync(null);   // the old rows stay until this capture is fetched
        vm.FilterText = "o";                        // any re-filter

        Assert.Equal(2, vm.Results.Count);
    }

    [Fact]
    public async Task SaveJsonl_AfterARecordingThatEndedWithoutAFetch_RecordsTheShownRowsMinimum()
    {
        var (vm, dump, platform) = MakeSavingVm();
        dump.NextGet = CallsResult(("Once", 1), ("Often", 50));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.MinCallsExponent = 3;
        await vm.StartCommand.ExecuteAsync(null);
        vm.ResetOnDisconnect();                     // the recording ends, nothing fetched

        await vm.SaveJsonlCommand.ExecuteAsync(null);

        try
        {
            var lines = ReadLines(platform.Answer!);
            Assert.Equal(1, lines[0].RootElement.GetProperty("min_calls").GetInt32());
            Assert.Equal(3, lines.Count);   // both rows, as shown
        }
        finally { File.Delete(platform.Answer!); }
    }

    [Fact]
    public async Task CapHit_ButEveryCutRowIsBelowMinCalls_RaiseDoesNotHelp()
    {
        // The DLL cuts the lowest counts, so every row a higher limit brings back has at most the page's
        // lowest count; with that below Min calls, all of them would be hidden.
        var (vm, dump) = MakeVm();
        vm.FetchLimitExponent = 6;   // 64
        vm.MinCallsExponent = 3;     // 8
        var rows = Rows(64).Select((e, i) => new PeProfileEntry
        {
            ClassName = e.ClassName, FuncName = e.FuncName, FuncAddr = e.FuncAddr, FirstSeq = i + 1,
            Count = i < 10 ? 100 : 2,
        }).ToArray();
        dump.NextGet = TruncatedResultOf(500, rows);
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.False(vm.RaiseFetchLimitHelps);
    }

    [Fact]
    public async Task CapHit_WithTheLowestCountAtMinCalls_RaiseStillHelps()
    {
        var (vm, dump) = MakeVm();
        vm.FetchLimitExponent = 6;
        vm.MinCallsExponent = 3;     // 8
        var rows = Rows(64).Select((e, i) => new PeProfileEntry
        {
            ClassName = e.ClassName, FuncName = e.FuncName, FuncAddr = e.FuncAddr, FirstSeq = i + 1, Count = 8,
        }).ToArray();
        dump.NextGet = TruncatedResultOf(500, rows);
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.True(vm.RaiseFetchLimitHelps);
    }

    [Fact]
    public async Task DiffStatus_DoesNotClaimTheTargetIsOnScreen_WhenMinCallsHidANewRow()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = CallsResult(("Tick", 900));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);
        vm.MinCallsExponent = 2;   // 4
        dump.NextGet = CallsResult(("Tick", 950), ("OpenShop", 2));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.DoesNotContain("almost certainly", vm.StatusText);
    }

    [Fact]
    public async Task DiffStatus_KeepsItsClaim_WhenNoNewRowIsHidden()
    {
        // Negative control for the test above.
        var (vm, dump) = MakeVm();
        dump.NextGet = CallsResult(("Tick", 900));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.SetBaselineCommand.Execute(null);
        vm.MinCallsExponent = 2;
        dump.NextGet = CallsResult(("Tick", 950), ("OpenShop", 6));
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.Contains("almost certainly", vm.StatusText);
    }
}
