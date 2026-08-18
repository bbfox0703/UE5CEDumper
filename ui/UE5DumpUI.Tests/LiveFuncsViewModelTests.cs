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
    private sealed class FakeDumpService : StubDumpService
    {
        public bool StartHookActive { get; set; } = true;
        public string StartDetail { get; set; } = "";
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int GetCalls { get; private set; }
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
    public void CopyFuncName_RaisesRequestCopyText()
    {
        var (vm, _) = MakeVm();
        string? copied = null;
        vm.RequestCopyText += t => copied = t;

        vm.CopyFuncNameCommand.Execute(
            new PeProfileEntry { ClassName = "A", FuncName = "OpenShop" });

        Assert.Equal("OpenShop", copied);
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
}
