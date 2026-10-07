using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LIVEFUNCS-TIMELINE-2026-10-04] What Live Funcs' Start and Stop do for the call trace: experimental only (T6),
/// the buffer and its estimate (T1), ticks deciding the scope (T3, T5), the once-per-session question when nothing
/// is ticked but there is something to tick (T7), and what a stopped trace leaves for the Call Trace tab.
/// </summary>
public class LiveFuncsTraceTests
{
    private sealed class Gate(bool enabled) : IExperimentalGate
    {
        public bool IsEnabled { get; set; } = enabled;
        public int SnapshotQuotaMb { get; set; }
        public bool IsLocked => false;
        public void Lock() { }
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class FakeDumpService : StubDumpService, IDumpService
    {
        public int StartCalls { get; private set; }
        public TraceStartOptions? LastTrace { get; private set; }
        public bool LastStartHadTraceArgument { get; private set; }
        public TraceInfo? StopTrace { get; set; }
        public PeProfileResult NextGet { get; set; } = new();

        public override Task<PeProfileStartResult> PeProfileStartAsync(CancellationToken ct = default)
        {
            StartCalls++;
            LastTrace = null;
            LastStartHadTraceArgument = false;
            return Task.FromResult(new PeProfileStartResult { HookActive = true });
        }

        Task<PeProfileStartResult> IDumpService.PeProfileStartAsync(TraceStartOptions? trace, CancellationToken ct)
        {
            StartCalls++;
            LastTrace = trace;
            LastStartHadTraceArgument = true;
            return Task.FromResult(new PeProfileStartResult { HookActive = true });
        }

        public override Task PeProfileStopAsync(CancellationToken ct = default) => Task.CompletedTask;

        Task<TraceInfo?> IDumpService.PeProfileStopWithTraceAsync(CancellationToken ct) => Task.FromResult(StopTrace);

        public override Task<PeProfileResult> PeProfileGetAsync(int limit = 200, CancellationToken ct = default)
            => Task.FromResult(NextGet);
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

    private static (LiveFuncsViewModel vm, FakeDumpService dump) MakeVm(bool experimental = true)
    {
        var dump = new FakeDumpService();
        var vm = new LiveFuncsViewModel(dump, new NoopLogger(), experimentalGate: new Gate(experimental));
        return (vm, dump);
    }

    private static PeProfileEntry Row(string cls, string func, string addr, long count = 10)
        => new() { ClassName = cls, FuncName = func, FuncAddr = addr, Count = count };

    private static PeProfileResult ResultOf(long? windowMs, params PeProfileEntry[] entries) => new()
    {
        DistinctFuncs = entries.Length,
        TotalCalls = entries.Sum(e => e.Count),
        WindowMs = windowMs,
        Entries = entries.ToList(),
    };

    private static async Task RecordOnce(LiveFuncsViewModel vm)
    {
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Without_the_experimental_tabs_a_Start_asks_for_no_trace()
    {
        var (vm, dump) = MakeVm(experimental: false);
        vm.TraceEnabled = true;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.False(vm.TraceAvailable);
        Assert.False(dump.LastStartHadTraceArgument);
    }

    [Fact]
    public async Task With_Trace_off_a_Start_asks_for_no_trace()
    {
        var (vm, dump) = MakeVm();
        await vm.StartCommand.ExecuteAsync(null);
        Assert.False(dump.LastStartHadTraceArgument);
    }

    [Fact]
    public async Task The_first_traced_Start_has_nothing_to_tick_and_does_not_ask()
    {
        var (vm, dump) = MakeVm();
        int asked = 0;
        vm.ConfirmTraceAllCalls = () => { asked++; return Task.FromResult(true); };
        vm.TraceEnabled = true;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(0, asked);
        Assert.NotNull(dump.LastTrace);
        Assert.Empty(dump.LastTrace!.Ticked);
        Assert.Equal(64L << 20, dump.LastTrace.Bytes);   // the default, 2^6 MB
    }

    [Fact]
    public async Task With_rows_to_tick_and_none_ticked_it_asks_and_Cancel_starts_nothing_and_asks_again()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("Character", "Jump", "0x100"));
        await RecordOnce(vm);   // the table now has rows
        int asked = 0;
        vm.ConfirmTraceAllCalls = () => { asked++; return Task.FromResult(false); };
        vm.TraceEnabled = true;
        int startsBefore = dump.StartCalls;

        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(1, asked);
        Assert.Equal(startsBefore, dump.StartCalls);
        Assert.False(vm.IsRecording);

        await vm.StartCommand.ExecuteAsync(null);   // Cancel did not count as asked
        Assert.Equal(2, asked);
    }

    [Fact]
    public async Task Confirmed_once_a_later_Start_with_nothing_ticked_runs_without_asking()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("Character", "Jump", "0x100"));
        await RecordOnce(vm);
        int asked = 0;
        vm.ConfirmTraceAllCalls = () => { asked++; return Task.FromResult(true); };
        vm.TraceEnabled = true;

        await RecordOnce(vm);
        await RecordOnce(vm);
        Assert.Equal(1, asked);
        Assert.NotNull(dump.LastTrace);
        Assert.Empty(dump.LastTrace!.Ticked);
    }

    [Fact]
    public async Task Without_a_confirm_hook_a_Start_that_needs_the_question_does_not_start()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("Character", "Jump", "0x100"));
        await RecordOnce(vm);
        vm.TraceEnabled = true;
        int startsBefore = dump.StartCalls;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(startsBefore, dump.StartCalls);
    }

    [Fact]
    public async Task Ticked_functions_are_sent_by_address_and_nothing_is_asked()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("Character", "Jump", "0x100"), Row("Pawn", "Tick", "0x200"));
        await RecordOnce(vm);
        int asked = 0;
        vm.ConfirmTraceAllCalls = () => { asked++; return Task.FromResult(true); };
        vm.TraceEnabled = true;
        vm.TraceExcludePerFrame = true;
        vm.TraceBufferExponent = 7;

        vm.ToggleTickCommand.Execute(vm.Results.Single(r => r.FuncName == "Jump"));
        Assert.Equal(new[] { "Character::Jump" }, vm.TickedFunctions);
        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(0, asked);
        Assert.Equal(new[] { "0x100" }, dump.LastTrace!.Ticked);
        Assert.True(dump.LastTrace.ExcludePerFrame);
        Assert.Equal(128L << 20, dump.LastTrace.Bytes);
    }

    [Fact]
    public async Task Ticks_survive_a_fetch_and_follow_the_functions_new_address()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("Character", "Jump", "0x100"));
        await RecordOnce(vm);
        vm.ToggleTickCommand.Execute(vm.Results.Single());
        dump.NextGet = ResultOf(10_000, Row("Character", "Jump", "0x180"), Row("Pawn", "Tick", "0x200"));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.Results.Single(r => r.FuncName == "Jump").IsTicked);
        Assert.False(vm.Results.Single(r => r.FuncName == "Tick").IsTicked);
        vm.TraceEnabled = true;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "0x180" }, dump.LastTrace!.Ticked);
    }

    [Fact]
    public async Task Ticking_is_refused_while_recording_and_ticking_twice_unticks()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("Character", "Jump", "0x100"));
        await RecordOnce(vm);
        var row = vm.Results.Single();
        vm.ToggleTickCommand.Execute(row);
        vm.ToggleTickCommand.Execute(row);
        Assert.Empty(vm.TickedFunctions);
        Assert.False(row.IsTicked);

        await vm.StartCommand.ExecuteAsync(null);
        vm.ToggleTickCommand.Execute(row);
        Assert.Empty(vm.TickedFunctions);
    }

    [Fact]
    public void The_buffer_slider_is_32_to_512_MB()
    {
        var (vm, _) = MakeVm();
        vm.TraceBufferExponent = 2;
        Assert.Equal(LiveFuncsViewModel.TraceBufferMinExponent, vm.TraceBufferExponent);
        Assert.Equal(32, vm.TraceBufferMb);
        vm.TraceBufferExponent = 12;
        Assert.Equal(512, vm.TraceBufferMb);
    }

    [Fact]
    public async Task The_estimate_comes_from_the_last_fetchs_call_rate()
    {
        Assert.Equal(16.777216, LiveFuncsViewModel.EstimateSeconds(64L << 20, 50_000), 6);   // 64 MB / (50k/s * 80 B)
        Assert.Equal(0, LiveFuncsViewModel.EstimateSeconds(64L << 20, 0));

        // The text itself is en.axaml's (Res has no application in a unit test); the number behind it is this.
        var (vm, dump) = MakeVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        dump.NextGet = ResultOf(10_000, Row("Pawn", "Tick", "0x200", count: 500_000));   // 50,000 calls/s
        await RecordOnce(vm);
        Assert.Equal(50_000, vm.LastCallsPerSecond, 3);
        Assert.Contains(nameof(LiveFuncsViewModel.TraceEstimate), raised);

        raised.Clear();
        vm.TraceBufferExponent = 8;
        Assert.Contains(nameof(LiveFuncsViewModel.TraceEstimate), raised);
    }

    [Fact]
    public async Task An_older_DLL_without_window_ms_gives_no_rate()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(null, Row("Pawn", "Tick", "0x200", count: 500_000));
        await RecordOnce(vm);
        Assert.Equal(0, vm.LastCallsPerSecond);
    }

    [Fact]
    public async Task A_stopped_trace_is_offered_to_the_Call_Trace_tab()
    {
        var (vm, dump) = MakeVm();
        vm.TraceEnabled = true;
        dump.StopTrace = new TraceInfo { Allocated = true, Quiesced = true, Written = 1000, FirstValid = 200 };
        bool navigated = false;
        vm.NavigateToCallTrace += () => navigated = true;

        await RecordOnce(vm);
        Assert.True(vm.HasTraceToOpen);
        Assert.Same(dump.StopTrace, vm.LastTraceInfo);
        vm.OpenCallTraceCommand.Execute(null);
        Assert.True(navigated);

        // A Start clears the offer until the next traced Stop.
        await vm.StartCommand.ExecuteAsync(null);
        Assert.False(vm.HasTraceToOpen);
    }

    [Fact]
    public async Task A_stop_that_could_not_quiesce_offers_nothing()
    {
        var (vm, dump) = MakeVm();
        vm.TraceEnabled = true;
        dump.StopTrace = new TraceInfo { Allocated = true, Quiesced = false, Written = 1000 };
        await RecordOnce(vm);
        Assert.False(vm.HasTraceToOpen);
        Assert.False(vm.LastTraceInfo!.Quiesced);
    }

    [Fact]
    public async Task An_untraced_recording_offers_no_trace()
    {
        var (vm, dump) = MakeVm();
        dump.StopTrace = new TraceInfo { Allocated = true, Quiesced = true, Written = 1000 };   // a stale one
        await RecordOnce(vm);
        Assert.False(vm.HasTraceToOpen);
    }

    [Fact]
    public void The_trace_settings_persist_through_the_main_window_and_default_off_at_64_MB()
    {
        // MainWindowViewModel cannot be built in a unit test; pin its persistence sites by source.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "build.ps1"))) root = root.Parent;
        Assert.NotNull(root);
        var src = File.ReadAllText(Path.Combine(root!.FullName, "ui", "UE5DumpUI", "ViewModels", "MainWindowViewModel.cs"));
        foreach (var p in new[] { "TraceEnabled", "TraceBufferExponent", "TraceExcludePerFrame" })
        {
            Assert.Contains($"nameof(LiveFuncsViewModel.{p})", src);
            Assert.Contains($"LiveFuncs.{p} = o.LiveFuncs.{p}", src);
            Assert.Contains($"o.LiveFuncs.{p} = LiveFuncs.{p}", src);
        }
        // The stored defaults are the view model's: a first run is the same with or without ui-options.json.
        var o = new LiveFuncsUiOptions();
        var (vm, _) = MakeVm();
        Assert.Equal(vm.TraceEnabled, o.TraceEnabled);
        Assert.Equal(vm.TraceBufferExponent, o.TraceBufferExponent);
        Assert.Equal(vm.TraceExcludePerFrame, o.TraceExcludePerFrame);
        Assert.False(o.TraceEnabled);
        Assert.Equal(6, o.TraceBufferExponent);
    }

    [Fact]
    public async Task A_disconnect_drops_the_ticks_the_offer_and_the_rate()
    {
        var (vm, dump) = MakeVm();
        vm.TraceEnabled = true;
        dump.NextGet = ResultOf(10_000, Row("Character", "Jump", "0x100"));
        dump.StopTrace = new TraceInfo { Allocated = true, Quiesced = true, Written = 10 };
        await RecordOnce(vm);
        vm.ToggleTickCommand.Execute(vm.Results.Single());

        vm.ResetOnDisconnect();
        Assert.Empty(vm.TickedFunctions);
        Assert.False(vm.HasTraceToOpen);
        Assert.Null(vm.LastTraceInfo);
        Assert.Equal(0, vm.LastCallsPerSecond);
    }
}
