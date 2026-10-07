using System.Text.RegularExpressions;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LIVEFUNCS-STEP2] Live Funcs' parameter snapshots: choosing by name (U5), the estimate (U6), the bulk choice (U7)
/// and the view's wiring (U8). docs/live-funcs-step2-items.md.
/// </summary>
public class LiveFuncsSnapshotTests
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
        public TraceStartOptions? LastTrace { get; private set; }
        public PeProfileResult NextGet { get; set; } = new();

        public override Task<PeProfileStartResult> PeProfileStartAsync(CancellationToken ct = default)
            => Task.FromResult(new PeProfileStartResult { HookActive = true });

        Task<PeProfileStartResult> IDumpService.PeProfileStartAsync(TraceStartOptions? trace, CancellationToken ct)
        {
            LastTrace = trace;
            return Task.FromResult(new PeProfileStartResult
            {
                HookActive = true,
                Trace = trace == null ? null : new TraceInfo
                {
                    Allocated = true, Tracing = true, Gen = 1,
                    Names = new StartNames { Ticks = trace.TickedNames.Count, Chosen = trace.Snapshots?.Funcs.Count ?? 0 },
                    Snap = trace.Snapshots == null ? null : new SnapInfo { Allocated = true, Rings = trace.Snapshots.Funcs.Count },
                },
            });
        }

        public override Task PeProfileStopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public TraceInfo? StopTrace { get; set; }
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
        return (new LiveFuncsViewModel(dump, new NoopLogger(), null, experimentalGate: new Gate(experimental)), dump);
    }

    private static PeProfileEntry Row(string cls, string func, string addr, NameKey? key, long count = 10,
                                      byte parms = 1, ushort size = 16, uint flags = 0x400, bool unloaded = false,
                                      bool perFrame = false)
        => new()
        {
            ClassName = cls, FuncName = func, FuncAddr = addr, FnameKey = key, Count = count, NumParms = parms,
            ParmsSize = size, FunctionFlags = flags, IsUnloaded = unloaded, IsPerFrame = perFrame,
        };

    private static PeProfileResult ResultOf(long windowMs, params PeProfileEntry[] entries) => new()
    {
        DistinctFuncs = entries.Length, TotalCalls = entries.Sum(e => e.Count), WindowMs = windowMs, Entries = entries.ToList(),
    };

    private static async Task Fetch(LiveFuncsViewModel vm)
    {
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
    }

    // ---- U5 ----

    [Fact]
    public async Task A_choice_needs_the_trace_a_key_and_parameters_and_an_unloaded_keyed_row_can_be_chosen()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000,
            Row("A", "Keyed", "0x1", new NameKey(1, 0, 9, 0)),
            Row("A", "Keyless", "0x2", null),
            Row("A", "NoParams", "0x3", new NameKey(3, 0, 9, 0), parms: 0, size: 0, flags: 0x400),
            Row("A", "Gone", "0x4", new NameKey(4, 0, 9, 0), unloaded: true));
        await Fetch(vm);
        var keyed = vm.Results.Single(r => r.FuncName == "Keyed");

        vm.ToggleSnapshotCommand.Execute(keyed);
        Assert.Empty(vm.SnapshotFunctions);            // Trace off: snapshots ride on the trace
        vm.TraceEnabled = true;
        vm.ToggleSnapshotCommand.Execute(vm.Results.Single(r => r.FuncName == "Keyless"));
        vm.ToggleSnapshotCommand.Execute(vm.Results.Single(r => r.FuncName == "NoParams"));
        Assert.Empty(vm.SnapshotFunctions);
        vm.ToggleSnapshotCommand.Execute(vm.Results.Single(r => r.FuncName == "Gone"));
        vm.ToggleSnapshotCommand.Execute(keyed);
        Assert.Equal(new[] { "A::Gone", "A::Keyed" }, vm.SnapshotFunctions);
        Assert.True(keyed.IsSnapChosen);
    }

    [Fact]
    public async Task Rows_from_an_earlier_connection_cannot_be_chosen_and_a_disconnect_clears_the_choices()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x1", new NameKey(1, 0, 9, 0)));
        await Fetch(vm);
        vm.TraceEnabled = true;
        vm.ToggleSnapshotCommand.Execute(vm.Results.Single());
        Assert.Single(vm.SnapshotFunctions);

        vm.ResetOnDisconnect();
        Assert.Empty(vm.SnapshotFunctions);
        Assert.False(vm.Results.Single().IsSnapChosen);
        vm.ToggleSnapshotCommand.Execute(vm.Results.Single());
        Assert.Empty(vm.SnapshotFunctions);
    }

    [Fact]
    public async Task Choices_alone_start_a_snapshots_only_trace_without_asking_and_send_keys_and_sizes()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "Call", "0x1", new NameKey(5, 0, 9, 0), size: 152),
                                Row("A", "Other", "0x2", new NameKey(6, 0, 9, 0)));
        await Fetch(vm);
        vm.TraceEnabled = true;
        vm.ToggleSnapshotCommand.Execute(vm.Results.Single(r => r.FuncName == "Call"));
        int asked = 0;
        vm.ConfirmTraceAllCalls = () => { asked++; return Task.FromResult(false); };

        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(0, asked);                 // T11: nothing ticked and something chosen is not T7's case
        Assert.True(vm.IsRecording);
        var f = Assert.Single(dump.LastTrace!.Snapshots!.Funcs);
        Assert.Equal(new NameKey(5, 0, 9, 0), Assert.Single(f.Keys));
        Assert.Equal(152, f.ParmsSize);
        Assert.Equal(32L << 20, dump.LastTrace.Snapshots.Bytes);   // the default buffer
    }

    // ---- U6 ----

    [Fact]
    public void The_estimate_pins_the_DLLs_numbers_read_from_Linie_h()
    {
        var h = File.ReadAllText(Path.Combine(RepoRoot(), "dll", "src", "Linie.h"));
        int Const(string name) => int.Parse(Regex.Match(h, $@"\b{name}\s*=\s*(\d+)").Groups[1].Value);
        Assert.Equal(LiveFuncsViewModel.SnapMaxCopy, Const("kSnapMaxCopy"));
        Assert.Equal(LiveFuncsViewModel.SnapUnknownCopy, Const("kSnapUnknownCopy"));
        Assert.Equal(LiveFuncsViewModel.SnapMinSlots, Const("kSnapMinSlots"));
        Assert.Equal(LiveFuncsViewModel.SnapHeaderBytes, Const("kSnapHeaderBytes"));
    }

    [Fact]
    public void The_estimate_from_rates_budgets_and_slot_sizes()
    {
        Assert.Equal(256, LiveFuncsViewModel.RingCapFor(0));
        Assert.Equal(2048, LiveFuncsViewModel.RingCapFor(3000));
        Assert.Equal(48, LiveFuncsViewModel.RingCapFor(41));
        Assert.Equal(2, LiveFuncsViewModel.SlotsPerCall(0));
        Assert.Equal(2, LiveFuncsViewModel.SlotsPerCall(0x00400400));
        Assert.Equal(1, LiveFuncsViewModel.SlotsPerCall(0x400));

        var chosen = new[]
        {
            new LiveFuncsViewModel.SnapRate("A::Busy", 2000, 64, 0x400),
            new LiveFuncsViewModel.SnapRate("A::Rare", 10, 0, 0),
        };
        var e = LiveFuncsViewModel.EstimateSnapshots(chosen, 8L << 20, 1000, 10000);
        Assert.Equal(2010, e.CallsPerSec);
        Assert.Equal(1010, e.AdmittedPerSec);            // the busy one held to 1,000/s
        Assert.Equal(1000, e.SkippedPerSec);
        Assert.Equal(22794, e.SlotsPerRing);
        Assert.Equal(11397, e.CallsKept);
        Assert.Equal("A::Busy", e.Busiest);
        Assert.Equal(11.397, e.BusiestSeconds, 3);
        Assert.Equal(181600.0 * 60 / (1 << 20), e.MbPerMinute, 6);   // two slots a call: an upper bound

        var capped = LiveFuncsViewModel.EstimateSnapshots(chosen, 8L << 20, 1000, 505);
        Assert.Equal(505, capped.AdmittedPerSec, 6);     // the total scales every function down

        var many = Enumerable.Range(0, 512).Select(i => new LiveFuncsViewModel.SnapRate($"F{i}", 1, 2048, 0)).ToList();
        Assert.True(LiveFuncsViewModel.EstimateSnapshots(many, 8L << 20, 1000, 10000).TooSmall);   // K = 7: refused
    }

    [Fact]
    public async Task The_game_figure_holds_the_snapshot_buffer_only_while_something_is_chosen()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x1", new NameKey(1, 0, 9, 0)));
        await Fetch(vm);
        vm.TraceEnabled = true;
        Assert.Equal(vm.TraceBufferMb, vm.TraceGameMb);
        vm.ToggleSnapshotCommand.Execute(vm.Results.Single());
        Assert.Equal(vm.TraceBufferMb + vm.SnapshotBufferMb, vm.TraceGameMb);
    }

    [Fact]
    public async Task The_estimate_turns_orange_when_the_busiest_choice_keeps_less_time_than_the_trace()
    {
        var (vm, dump) = MakeVm();
        // 100,000 calls of one function over 10 s: 10,000/s, held to 1,000/s; 8 MB of 2,072-byte slots keeps ~2,000 calls
        // = ~2 s, while the trace (its rows' rate, 10,000/s at 80 B) keeps 64 MB / 800 KB/s = ~84 s.
        dump.NextGet = ResultOf(10_000, Row("A", "Hot", "0x1", new NameKey(1, 0, 9, 0), count: 100_000, size: 2048));
        await Fetch(vm);
        vm.TraceEnabled = true;
        vm.SnapshotBufferExponent = 3;
        vm.ToggleSnapshotCommand.Execute(vm.Results.Single());
        Assert.True(vm.SnapshotEstimateWarn);
    }

    // ---- U7 ----

    [Fact]
    public async Task Choosing_shown_rows_takes_the_filtered_view_only_and_says_what_it_left_out()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000,
            Row("Shop", "Open", "0x1", new NameKey(1, 0, 9, 0)),
            Row("Shop", "Tick", "0x2", new NameKey(2, 0, 9, 0), perFrame: true),
            Row("Shop", "Old", "0x3", null),
            Row("Shop", "Empty", "0x4", new NameKey(4, 0, 9, 0), parms: 0, size: 0, flags: 0x400),
            Row("Other", "Jump", "0x5", new NameKey(5, 0, 8, 0)));
        await Fetch(vm);
        vm.TraceEnabled = true;
        vm.FilterText = "Shop";
        vm.SnapshotShownRowsCommand.Execute(null);
        Assert.Equal(new[] { "Shop::Open", "Shop::Tick" }, vm.SnapshotFunctions);   // per-frame included
        Assert.Equal(2, vm.LastSnapshotBulkSkipped);   // two left out: no key, no parameters

        await vm.StartCommand.ExecuteAsync(null);
        int before = vm.SnapshotFunctions.Count;
        vm.FilterText = "";
        vm.SnapshotShownRowsCommand.Execute(null);   // recording: nothing changes
        Assert.Equal(before, vm.SnapshotFunctions.Count);
    }

    // ---- U9 ----

    [Fact]
    public void The_trace_says_what_it_followed_by_name_snapshots_only_or_every_call()
    {
        Assert.Equal("str.CT.Status.ScopedNames",
                     CallTraceViewModel.ScopeKey(new TraceInfo { Scoped = true, TickedNames = 2, Ticked = 0 }));
        Assert.Equal("str.CT.Status.SnapOnly", CallTraceViewModel.ScopeKey(new TraceInfo { Scoped = true, SnapOnly = true }));
        Assert.Equal("str.CT.Status.Unscoped", CallTraceViewModel.ScopeKey(new TraceInfo { Scoped = false }));
        // A DLL that predates names sends no `scoped`: its ticks by address say it.
        Assert.Equal("str.CT.Status.Scoped", CallTraceViewModel.ScopeKey(new TraceInfo { Ticked = 3 }));
        Assert.Equal("str.CT.Status.Unscoped", CallTraceViewModel.ScopeKey(new TraceInfo()));
        Assert.Null(CallTraceViewModel.ScopeKey(new TraceInfo { Excluded = 4 }));

        Assert.Equal("str.LF.Trace.RecordingTicked", LiveFuncsViewModel.TraceStartKey(new TraceStartOptions
        {
            TickedNames = new[] { new NamedFunction { ClassName = "A", FuncName = "F", Keys = new[] { new NameKey(1, 0, 2, 0) } } },
        }));
        Assert.Equal("str.LF.Trace.RecordingSnapOnly",
                     LiveFuncsViewModel.TraceStartKey(new TraceStartOptions { Snapshots = new SnapshotStartOptions() }));
        Assert.Equal("str.LF.Trace.RecordingAll", LiveFuncsViewModel.TraceStartKey(new TraceStartOptions()));
    }

    [Fact]
    public async Task The_Stop_lists_the_followed_names_never_called_and_a_disconnect_forgets_them()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "Late", "0x1", new NameKey(1, 0, 9, 0)));
        await Fetch(vm);
        vm.TraceEnabled = true;
        vm.ToggleSnapshotCommand.Execute(vm.Results.Single());
        dump.StopTrace = new TraceInfo
        {
            Allocated = false, Quiesced = true, Written = 0,
            Followed = new[]
            {
                new FollowedName { ClassName = "A", FuncName = "Late", Chosen = true, Addresses = 0 },
                new FollowedName { ClassName = "A", FuncName = "Busy", Tick = true, Addresses = 2 },
            },
        };
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "A::Late" }, vm.NotCalledNames);
        vm.ResetOnDisconnect();
        Assert.Empty(vm.NotCalledNames);
    }

    // ---- U8 ----

    [Fact]
    public void The_view_hides_the_Snapshot_column_with_Trace_never_sorts_it_and_keeps_the_buffer_slider()
    {
        string root = RepoRoot();
        var codeBehind = File.ReadAllText(Path.Combine(root, "ui", "UE5DumpUI", "Views", "LiveFuncsPanel.axaml.cs"));
        Assert.Contains("Core.Res.Get(\"str.LF.Col.Snapshot\")", codeBehind, StringComparison.Ordinal);
        var axaml = File.ReadAllText(Path.Combine(root, "ui", "UE5DumpUI", "Views", "LiveFuncsPanel.axaml"));
        var col = Regex.Match(axaml, @"<DataGridTemplateColumn Header=""\{StaticResource str\.LF\.Col\.Snapshot\}""[^>]*>");
        Assert.True(col.Success);
        Assert.Contains("CanUserSort=\"False\"", col.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("SortMemberPath", col.Value, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"False\"", col.Value, StringComparison.Ordinal);
        var main = File.ReadAllText(Path.Combine(root, "ui", "UE5DumpUI", "ViewModels", "MainWindowViewModel.cs"));
        Assert.Contains("nameof(LiveFuncsViewModel.SnapshotBufferExponent)", main, StringComparison.Ordinal);
        Assert.Contains("o.LiveFuncs.SnapshotBufferExponent = LiveFuncs.SnapshotBufferExponent;", main, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "build.ps1"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
