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
        /// <summary>A DLL that predates the trace: it ignores the `trace` key and answers no trace object.</summary>
        public bool StartOmitsTrace { get; set; }
        public bool StartThrows { get; set; }
        /// <summary>What the traced Start's reply says the DLL left out of the ticks (review UI-1).</summary>
        public int StartTickedDropped { get; set; }
        public bool StopThrows { get; set; }

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
            if (StartThrows) throw new InvalidOperationException("The game process could not spare the buffer.");
            return Task.FromResult(new PeProfileStartResult
            {
                HookActive = true,
                Trace = trace == null || StartOmitsTrace ? null
                    : new TraceInfo { Allocated = true, Tracing = true, Gen = 1, TickedDropped = StartTickedDropped },
            });
        }

        public override Task PeProfileStopAsync(CancellationToken ct = default) => Task.CompletedTask;

        Task<TraceInfo?> IDumpService.PeProfileStopWithTraceAsync(CancellationToken ct)
            => StopThrows ? throw new InvalidOperationException("pipe closed") : Task.FromResult(StopTrace);

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

    private static (LiveFuncsViewModel vm, FakeDumpService dump) MakeVm(bool experimental = true,
                                                                         IPlatformService? platform = null)
    {
        var dump = new FakeDumpService();
        var vm = new LiveFuncsViewModel(dump, new NoopLogger(), platform, experimentalGate: new Gate(experimental));
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
    public async Task A_DLL_that_armed_no_trace_is_not_reported_as_tracing()
    {
        // An older DLL ignores the `trace` key: the recording is plain, so Stop offers nothing to open.
        var (vm, dump) = MakeVm();
        vm.TraceEnabled = true;
        dump.StartOmitsTrace = true;
        dump.StopTrace = new TraceInfo { Allocated = true, Quiesced = true, Written = 50 };   // a stale one
        await RecordOnce(vm);
        Assert.False(vm.HasTraceToOpen);
        Assert.Null(vm.LastTraceInfo);
    }

    [Fact]
    public async Task A_refused_traced_Start_withdraws_the_offer_of_the_previous_trace()
    {
        // The DLL releases the previous trace before it tries the new buffer (review DLL-4).
        var (vm, dump) = MakeVm();
        vm.TraceEnabled = true;
        dump.StopTrace = new TraceInfo { Allocated = true, Quiesced = true, Written = 50 };
        await RecordOnce(vm);
        Assert.True(vm.HasTraceToOpen);

        dump.StartThrows = true;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.False(vm.IsRecording);
        Assert.False(vm.HasTraceToOpen);
        Assert.Null(vm.LastTraceInfo);
    }

    [Fact]
    public async Task A_stop_that_fails_on_leaving_the_tab_reports_no_earlier_trace()
    {
        var (vm, dump) = MakeVm();
        vm.TraceEnabled = true;
        dump.StopTrace = new TraceInfo { Allocated = true, Quiesced = true, Written = 50 };
        await RecordOnce(vm);
        Assert.NotNull(vm.LastTraceInfo);

        await vm.StartCommand.ExecuteAsync(null);
        dump.StopThrows = true;
        vm.OnLeavingTab();
        await vm.PendingAutoStop;
        Assert.Null(vm.LastTraceInfo);   // the earlier recording's trace is not this one's
        Assert.False(vm.HasTraceToOpen);
    }

    [Fact]
    public async Task Rows_that_share_a_name_tick_together_and_send_every_address()
    {
        // pe_profile_get names a class by its short name, so two Blueprint classes in different folders can give two
        // rows with the same Class::Func: a tick is by name, so both rows show it and both addresses are traced.
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("BP_Door_C", "Open", "0x100"), Row("BP_Door_C", "Open", "0x200"),
                                Row("Pawn", "Tick", "0x300"));
        await RecordOnce(vm);
        var doors = vm.Results.Where(r => r.FuncName == "Open").ToList();
        vm.ToggleTickCommand.Execute(doors[1]);
        Assert.True(doors[0].IsTicked && doors[1].IsTicked);
        Assert.Equal(new[] { "BP_Door_C::Open" }, vm.TickedFunctions);

        vm.TraceEnabled = true;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "0x100", "0x200" }, dump.LastTrace!.Ticked.OrderBy(a => a).ToArray());
        await vm.StopCommand.ExecuteAsync(null);

        vm.ToggleTickCommand.Execute(vm.Results.First(r => r.FuncName == "Open"));
        Assert.All(vm.Results, r => Assert.False(r.IsTicked));
        Assert.Empty(vm.TickedFunctions);
    }

    // [TRACE-UI-LOAD-MEMORY] D3: what a full buffer costs, beside the slider, from the buffer alone -- before any
    // recording, at any call rate: the game holds N MB from Start; the UI holds the window and the trace's columns
    // while it loads (about twice N, plus a page in flight) and the columns after (about N).
    // Calibrated live on build 3636 (Avowed, 2026-10-07): a full 128 MB load peaked 303 MB over its start; a full
    // 512 MB one 1,054 MB over its start. "Up to" holds over the load's start at 2.25 x N + 45 (333 and 1,197); from a
    // freshly started UI that 512 MB run came to 1,206 MB, the 152 MB the UI already held before the load included.
    [Theory]
    [InlineData(5, 32, 117, 32)]
    [InlineData(6, 64, 189, 64)]
    [InlineData(9, 512, 1197, 512)]
    public void The_memory_estimate_comes_from_the_buffer_alone(int exponent, int gameMb, int uiPeakMb, int uiHeldMb)
    {
        var (vm, _) = MakeVm();
        Assert.Equal(0, vm.LastCallsPerSecond);   // no recording yet
        vm.TraceBufferExponent = exponent;
        Assert.Equal(gameMb, vm.TraceGameMb);
        Assert.Equal(uiPeakMb, vm.TraceUiPeakMb);
        Assert.Equal(uiHeldMb, vm.TraceUiHeldMb);
    }

    [Fact]
    public async Task Above_the_available_memory_the_estimate_warns_and_Start_still_starts()
    {
        var platform = new MockPlatformService(Path.GetTempPath()) { AvailablePhysicalMemory = 1L << 30 };   // 1 GB
        var (vm, dump) = MakeVm(platform: platform);
        vm.TraceEnabled = true;
        vm.TraceBufferExponent = 9;   // 512 MB in the game and about 1 GB in the UI: more than 1 GB free
        Assert.True(vm.TraceMemoryOverAvailable);

        int reads = platform.AvailableMemoryReads;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsRecording);                      // a warning, never a refusal (D3)
        Assert.Equal(1, dump.StartCalls);
        Assert.True(platform.AvailableMemoryReads > reads);   // read again at Start: memory moves while the slider waits

        await vm.StopCommand.ExecuteAsync(null);
        vm.TraceBufferExponent = 5;
        Assert.False(vm.TraceMemoryOverAvailable);        // 32 MB + about 110 MB fits in 1 GB
    }

    private sealed class RaisingGate : IExperimentalGate
    {
        public bool IsEnabled { get; set; }
        public int SnapshotQuotaMb { get; set; }
        public bool IsLocked => false;
        public void Lock() { }
        public event EventHandler? Changed;
        public void Turn(bool on) { IsEnabled = on; Changed?.Invoke(this, EventArgs.Empty); }
    }

    [Fact]
    public void The_free_memory_is_read_again_when_the_tab_is_shown_and_when_the_experimental_tabs_come_on()
    {
        // Review INT-4 / UI-7: read once at startup, the line kept a figure from before the game was launched.
        var platform = new MockPlatformService(Path.GetTempPath()) { AvailablePhysicalMemory = 64L << 30 };
        var gate = new RaisingGate();
        var vm = new LiveFuncsViewModel(new FakeDumpService(), new NoopLogger(), platform, experimentalGate: gate);
        vm.TraceBufferExponent = 9;
        Assert.False(vm.TraceMemoryOverAvailable);

        platform.AvailablePhysicalMemory = 1L << 30;   // the game came up and took the rest
        vm.OnEnteringTab();
        Assert.True(vm.TraceMemoryOverAvailable);

        platform.AvailablePhysicalMemory = 64L << 30;
        int reads = platform.AvailableMemoryReads;
        gate.Turn(true);
        Assert.True(platform.AvailableMemoryReads > reads);
        Assert.False(vm.TraceMemoryOverAvailable);
    }

    [Fact]
    public void With_plenty_of_memory_or_none_known_there_is_no_warning()
    {
        var plenty = new MockPlatformService(Path.GetTempPath()) { AvailablePhysicalMemory = 64L << 30 };
        var (vm, _) = MakeVm(platform: plenty);
        vm.TraceBufferExponent = 9;
        Assert.False(vm.TraceMemoryOverAvailable);

        var (vm2, _) = MakeVm();   // no platform service: unknown, so no warning
        vm2.TraceBufferExponent = 9;
        Assert.False(vm2.TraceMemoryOverAvailable);
    }

    // [TRACE-UNLOADED-NAMES] D1: a function the game unloaded since it fired keeps its row, named from its first call,
    // but its address is dead: never ticked, never sent.
    private static PeProfileEntry Unloaded(string cls, string func, string addr, long count = 10)
        => new() { ClassName = cls, FuncName = func, FuncAddr = addr, Count = count, IsUnloaded = true };

    [Fact]
    public async Task An_unloaded_row_cannot_be_ticked_and_its_address_is_never_sent()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Unloaded("WBP_Inventory_C", "OnOpen", "0x100"),
                                Row("WBP_Inventory_C", "OnOpen", "0x180"));
        await RecordOnce(vm);
        var dead = vm.Results.Single(r => r.IsUnloaded);
        var live = vm.Results.Single(r => !r.IsUnloaded);

        vm.ToggleTickCommand.Execute(dead);
        Assert.Empty(vm.TickedFunctions);
        Assert.False(dead.IsTicked);

        // Ticking the live row of the same name ticks the name, but only the live address goes to the DLL.
        vm.ToggleTickCommand.Execute(live);
        Assert.True(live.IsTicked);
        Assert.False(dead.IsTicked);
        vm.TraceEnabled = true;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "0x180" }, dump.LastTrace!.Ticked.ToArray());
    }

    [Fact]
    public async Task A_ticked_function_that_unloaded_sends_no_dead_address_and_is_not_traced_as_every_call()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("WBP_Inventory_C", "OnOpen", "0x100"), Row("Character", "Jump", "0x200"));
        await RecordOnce(vm);
        vm.ToggleTickCommand.Execute(vm.Results.Single(r => r.FuncName == "OnOpen"));
        // The next recording finds it unloaded: the inventory closed before Stop.
        dump.NextGet = ResultOf(10_000, Unloaded("WBP_Inventory_C", "OnOpen", "0x100"), Row("Character", "Jump", "0x200"));
        await RecordOnce(vm);
        Assert.Equal(new[] { "WBP_Inventory_C::OnOpen" }, vm.TickedFunctions);   // the tick stays, by name
        Assert.False(vm.Results.Single(r => r.FuncName == "OnOpen").IsTicked);
        int asked = 0;
        vm.ConfirmTraceAllCalls = () => { asked++; return Task.FromResult(true); };
        vm.TraceEnabled = true;
        int startsBefore = dump.StartCalls;

        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(0, asked);                        // not T7's question: something IS ticked
        Assert.Equal(startsBefore, dump.StartCalls);   // and not a trace of every call either: refused
        Assert.False(vm.IsRecording);

        // Loaded again, at a new address: the tick follows it.
        vm.TraceEnabled = false;
        dump.NextGet = ResultOf(10_000, Row("WBP_Inventory_C", "OnOpen", "0x900"));
        await RecordOnce(vm);
        vm.TraceEnabled = true;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "0x900" }, dump.LastTrace!.Ticked.ToArray());
    }

    [Fact]
    public async Task A_traced_Start_says_how_many_ticks_the_DLL_left_out()
    {
        // Review UI-1: a tick whose function unloaded after the last fetch (its row cut by the fetch limit) is caught by
        // the DLL at Start; the UI says so instead of tracing fewer functions in silence.
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("Character", "Jump", "0x100"), Row("WBP_Inventory_C", "OnOpen", "0x200"));
        await RecordOnce(vm);
        vm.ToggleTickCommand.Execute(vm.Results.Single(r => r.FuncName == "Jump"));
        vm.ToggleTickCommand.Execute(vm.Results.Single(r => r.FuncName == "OnOpen"));
        dump.StartTickedDropped = 1;
        vm.TraceEnabled = true;

        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsRecording);
        Assert.Equal(1, vm.LastTickedDropped);
    }

    [Fact]
    public async Task With_only_unloaded_rows_there_is_nothing_to_tick_and_nothing_is_asked()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Unloaded("WBP_Inventory_C", "OnOpen", "0x100"));
        await RecordOnce(vm);
        int asked = 0;
        vm.ConfirmTraceAllCalls = () => { asked++; return Task.FromResult(false); };
        vm.TraceEnabled = true;

        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(0, asked);
        Assert.True(vm.IsRecording);
        Assert.Empty(dump.LastTrace!.Ticked);
    }

    [Fact]
    public async Task Rows_from_an_earlier_connection_lose_their_ticks_and_cannot_be_ticked()
    {
        // Their addresses belong to a process that is gone: a tick on one would send a dead address.
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("Character", "Jump", "0x100"));
        await RecordOnce(vm);
        var row = vm.Results.Single();
        vm.ToggleTickCommand.Execute(row);
        Assert.True(row.IsTicked);

        vm.ResetOnDisconnect();
        Assert.False(row.IsTicked);
        Assert.False(vm.CanTick);
        vm.ToggleTickCommand.Execute(row);
        Assert.False(row.IsTicked);
        Assert.Empty(vm.TickedFunctions);

        // A fetch in the new connection brings fresh rows, and ticking works again.
        dump.NextGet = ResultOf(10_000, Row("Character", "Jump", "0x180"));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.True(vm.CanTick);
        vm.ToggleTickCommand.Execute(vm.Results.Single());
        Assert.Equal(new[] { "Character::Jump" }, vm.TickedFunctions);
    }

    [Fact]
    public async Task Rows_from_an_earlier_connection_are_nothing_to_tick_for_the_question()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("Character", "Jump", "0x100"));
        await RecordOnce(vm);
        vm.ResetOnDisconnect();
        int asked = 0;
        vm.ConfirmTraceAllCalls = () => { asked++; return Task.FromResult(true); };
        vm.TraceEnabled = true;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(0, asked);
        Assert.NotNull(dump.LastTrace);
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
