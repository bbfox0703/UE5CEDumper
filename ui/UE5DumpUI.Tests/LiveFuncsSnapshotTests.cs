using System.Text.RegularExpressions;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LIVEFUNCS-STEP2] Live Funcs' parameter snapshots: choosing by name (U5), the estimate (U6), the bulk choice (U7)
/// and the view's wiring (U8). docs/live-funcs-step2-items.md. [LIVEFUNCS-STEP3] The native-stack choice beside them
/// (S3-U2), its view (S3-U3), and its budget and warning (S3-U8): docs/live-funcs-step3-items.md.
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
        /// <summary>[LIVEFUNCS-STEP3] A DLL that predates stacks: no names.stacks and no trace.stack in its reply.</summary>
        public bool StacksUnknown { get; set; }
        /// <summary>[LIVEFUNCS-STEP3] A DLL that refused every stack choice: names.stacks 0, and their names in Refused.</summary>
        public bool StacksRefused { get; set; }
        public List<ulong> Released { get; } = new();
        public int StopCalls { get; private set; }

        public override Task<PeProfileStartResult> PeProfileStartAsync(CancellationToken ct = default)
            => Task.FromResult(new PeProfileStartResult { HookActive = true });

        Task<PeProfileStartResult> IDumpService.PeProfileStartAsync(TraceStartOptions? trace, CancellationToken ct)
        {
            LastTrace = trace;
            // The stack half echoes what was asked, as a DLL that took every stack choice answers (design 3.2).
            var stacks = trace?.Snapshots?.Stacks;
            int? armed = stacks == null || StacksUnknown ? null : StacksRefused ? 0 : stacks.Funcs.Count;
            return Task.FromResult(new PeProfileStartResult
            {
                HookActive = true,
                Trace = trace == null ? null : new TraceInfo
                {
                    Allocated = true, Tracing = true, Gen = 1,
                    Names = new StartNames
                    {
                        Ticks = trace.TickedNames.Count, Chosen = trace.Snapshots?.Funcs.Count ?? 0, Stacks = armed,
                        Refused = StacksRefused && stacks != null
                            ? stacks.Funcs.Select(f => (f.ClassName, f.FuncName, "not_found")).ToList()
                            : Array.Empty<(string, string, string)>(),
                    },
                    Snap = trace.Snapshots == null ? null : new SnapInfo { Allocated = true, Rings = trace.Snapshots.Funcs.Count },
                    Stack = armed is > 0 ? new StackInfo
                    {
                        Rings = armed.Value, Depth = stacks!.Depth,
                        PerRingPerSec = stacks.PerRingPerSec, TotalPerSec = stacks.TotalPerSec,
                    } : null,
                },
            });
        }

        Task IDumpService.PeTraceReleaseAsync(ulong gen, CancellationToken ct)
        {
            Released.Add(gen);
            return Task.CompletedTask;
        }

        public override Task PeProfileStopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public TraceInfo? StopTrace { get; set; }
        Task<TraceInfo?> IDumpService.PeProfileStopWithTraceAsync(CancellationToken ct)
        {
            StopCalls++;
            return Task.FromResult(StopTrace);
        }
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
        var vm = new LiveFuncsViewModel(dump, new NoopLogger(), null, experimentalGate: new Gate(experimental))
        {
            StringLookup = En,
        };
        return (vm, dump);
    }

    /// <summary>en.axaml's strings, read as the app shows them: Res has no Avalonia application in a unit test, so
    /// the view model is handed these and the tests read the sentences a user reads.</summary>
    private static readonly Lazy<Dictionary<string, string>> EnStrings = new(() =>
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "ui", "UE5DumpUI", "Resources", "Strings", "en.axaml"));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, "x:Key=\"(str\\.[^\"]+)\">([^<]*)</sys:String>"))
            map[m.Groups[1].Value] = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value);
        return map;
    });

    private static string En(string key) => EnStrings.Value.TryGetValue(key, out var s) ? s : "";

    /// <summary>The line a key makes; a key missing from en.axaml fails here rather than matching as "".</summary>
    private static string Line(string key, params object[] args)
    {
        string template = En(key);
        Assert.True(template.Length > 0, $"{key} is not in en.axaml");
        return string.Format(template, args);
    }

    /// <summary>A constant's value as a DLL header writes it: decimal, hex, or a bit shift ("1u &lt;&lt; 5").</summary>
    private static long HeaderConst(string header, string name)
    {
        var m = Regex.Match(header, $@"\b{name}\s*=\s*(?:0x([0-9A-Fa-f]+)|(\d+)u?\s*<<\s*(\d+)|(\d+))");
        Assert.True(m.Success, $"{name} is not in the header");
        return m.Groups[1].Success ? Convert.ToInt64(m.Groups[1].Value, 16)
             : m.Groups[2].Success ? long.Parse(m.Groups[2].Value) << int.Parse(m.Groups[3].Value)
             : long.Parse(m.Groups[4].Value);
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
        int Const(string name) => (int)HeaderConst(h, name);
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

    // ---- [LIVEFUNCS-STEP3] S3-U2: the stack choice ----

    private static NamedFunction Named(string cls, string func, int idx = 1)
        => new() { ClassName = cls, FuncName = func, Keys = new[] { new NameKey(idx, 0, 9, 0) } };

    [Fact]
    public async Task A_stack_needs_a_key_but_no_parameters()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000,
            Row("A", "NoParams", "0x3", new NameKey(3, 0, 9, 0), parms: 0, size: 0, flags: 0x400),
            Row("A", "Keyless", "0x2", null));
        await Fetch(vm);
        vm.TraceEnabled = true;
        var none = vm.Results.Single(r => r.FuncName == "NoParams");

        vm.ToggleSnapshotCommand.Execute(none);
        Assert.Empty(vm.SnapshotFunctions);              // no parameters to copy
        vm.ToggleStackCommand.Execute(vm.Results.Single(r => r.FuncName == "Keyless"));
        vm.ToggleStackCommand.Execute(none);
        Assert.Equal(new[] { "A::NoParams" }, vm.StackFunctions);   // every function has a stack; chosen by name
        Assert.True(none.IsStackChosen);
        Assert.False(none.IsSnapChosen);
        Assert.True(vm.HasStackChoices);
        Assert.False(vm.HasSnapshotChoices);
    }

    [Fact]
    public async Task A_stack_choice_is_refused_with_Trace_off_while_recording_keyless_and_from_an_earlier_connection()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x1", new NameKey(1, 0, 9, 0)), Row("A", "Old", "0x2", null));
        await Fetch(vm);
        var row = vm.Results.Single(r => r.FuncName == "F");

        vm.ToggleStackCommand.Execute(row);
        Assert.Empty(vm.StackFunctions);                 // Trace off: stacks ride on the trace
        vm.TraceEnabled = true;
        vm.ToggleStackCommand.Execute(vm.Results.Single(r => r.FuncName == "Old"));
        Assert.Empty(vm.StackFunctions);                 // no name key
        vm.ConfirmTraceAllCalls = () => Task.FromResult(true);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsRecording);
        vm.ToggleStackCommand.Execute(row);
        Assert.Empty(vm.StackFunctions);                 // recording: it takes the choices it started with
        await vm.StopCommand.ExecuteAsync(null);

        vm.ToggleStackCommand.Execute(row);
        Assert.Equal(new[] { "A::F" }, vm.StackFunctions);   // the same row, once nothing refuses it
        vm.ResetOnDisconnect();
        vm.ToggleStackCommand.Execute(row);
        Assert.Empty(vm.StackFunctions);                 // a row from an earlier connection: its key was that process's
    }

    [Fact]
    public async Task A_fetch_carries_the_stack_choice_onto_its_new_rows()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x1", new NameKey(1, 0, 9, 0)));
        await Fetch(vm);
        vm.TraceEnabled = true;
        vm.ToggleStackCommand.Execute(vm.Results.Single());
        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x9", new NameKey(1, 0, 9, 0)));   // the same function, new rows
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("0x9", vm.Results.Single().FuncAddr);
        Assert.True(vm.Results.Single().IsStackChosen);
    }

    [Fact]
    public async Task Stacks_alone_start_a_trace_without_asking_and_send_no_parameter_functions()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "Call", "0x1", new NameKey(5, 0, 9, 0)),
                                Row("A", "Other", "0x2", new NameKey(6, 0, 9, 0)));
        await Fetch(vm);
        vm.TraceEnabled = true;
        vm.ToggleStackCommand.Execute(vm.Results.Single(r => r.FuncName == "Call"));
        int asked = 0;
        vm.ConfirmTraceAllCalls = () => { asked++; return Task.FromResult(false); };

        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(0, asked);       // a stack choice scopes the trace as a parameter choice does (T11): not T7's case
        Assert.True(vm.IsRecording);
        var snaps = dump.LastTrace!.Snapshots;
        Assert.NotNull(snaps);
        Assert.Empty(snaps!.Funcs);
        Assert.Equal(32L << 20, snaps.Bytes);       // stacks share the snapshot buffer (D4)
        var st = snaps.Stacks;
        Assert.NotNull(st);
        var f = Assert.Single(st!.Funcs);
        Assert.Equal(("A", "Call"), (f.ClassName, f.FuncName));
        Assert.Equal(new NameKey(5, 0, 9, 0), Assert.Single(f.Keys));
        Assert.Equal((16, 100, 200), (st.Depth, st.PerRingPerSec, st.TotalPerSec));
    }

    [Fact]
    public async Task A_DLL_without_stacks_is_stopped_and_released_but_one_that_refused_them_keeps_recording()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "Call", "0x1", new NameKey(5, 0, 9, 0)),
                                Row("A", "Busy", "0x2", new NameKey(6, 0, 9, 0)));
        await Fetch(vm);
        vm.TraceEnabled = true;
        vm.ToggleStackCommand.Execute(vm.Results.Single(r => r.FuncName == "Call"));
        vm.ToggleSnapshotCommand.Execute(vm.Results.Single(r => r.FuncName == "Busy"));

        int stops = dump.StopCalls;                      // the fetch's own Stop
        dump.StacksUnknown = true;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.False(vm.IsRecording);
        Assert.Equal(stops + 1, dump.StopCalls);
        Assert.Equal(new ulong[] { 1 }, dump.Released);
        Assert.Equal(Line("str.LF.Stack.NotArmed"), vm.StatusText);

        // Review M3: every stack choice refused is not an old DLL. The rest of the Start stands, and Refused names them.
        dump.StacksUnknown = false;
        dump.StacksRefused = true;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsRecording);
        Assert.Equal(stops + 1, dump.StopCalls);
        Assert.Contains(Line("str.LF.Snap.Refused", 1), vm.StatusText);
        Assert.DoesNotContain(Line("str.LF.Stack.NotArmed"), vm.StatusText);
        Assert.DoesNotContain(Line("str.LF.Stack.Recording", 0, 16), vm.StatusText);
    }

    [Fact]
    public async Task The_status_says_what_the_stacks_record_and_counts_a_function_chosen_for_both_once()
    {
        var both = new SnapshotStartOptions
        {
            Funcs = new[] { Named("A", "P") },
            Stacks = new StackStartOptions { Funcs = new[] { Named("A", "P"), Named("A", "S", 2) } },
        };
        Assert.Equal("str.LF.Trace.RecordingSnapOnly", LiveFuncsViewModel.TraceStartKey(new TraceStartOptions { Snapshots = both }));
        Assert.Equal(2, LiveFuncsViewModel.TraceStartCount(new TraceStartOptions { Snapshots = both }));
        Assert.Equal(1, LiveFuncsViewModel.TraceStartCount(new TraceStartOptions
        {
            Snapshots = new SnapshotStartOptions { Stacks = new StackStartOptions { Funcs = new[] { Named("A", "S") } } },
        }));
        // Ticked: the sentence counts the ticked functions the trace runs inside.
        Assert.Equal(3, LiveFuncsViewModel.TraceStartCount(new TraceStartOptions { Ticked = new[] { "0x1", "0x2", "0x3" }, Snapshots = both }));

        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "P", "0x1", new NameKey(1, 0, 9, 0)), Row("A", "S", "0x2", new NameKey(2, 0, 9, 0)));
        await Fetch(vm);
        vm.TraceEnabled = true;
        var p = vm.Results.Single(r => r.FuncName == "P");
        vm.ToggleSnapshotCommand.Execute(p);
        vm.ToggleStackCommand.Execute(p);
        vm.ToggleStackCommand.Execute(vm.Results.Single(r => r.FuncName == "S"));
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Contains(Line("str.LF.Trace.RecordingSnapOnly", vm.TraceBufferMb, 2), vm.StatusText);
        Assert.Contains(Line("str.LF.Snap.Recording", 1, vm.SnapshotBufferMb), vm.StatusText);
        Assert.Contains(Line("str.LF.Stack.Recording", 2, 16), vm.StatusText);
        await vm.StopCommand.ExecuteAsync(null);

        vm.ToggleSnapshotCommand.Execute(p);             // stacks alone: no parameter sentence
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Contains(Line("str.LF.Trace.RecordingSnapOnly", vm.TraceBufferMb, 2), vm.StatusText);
        Assert.DoesNotContain(Line("str.LF.Snap.Recording", 0, vm.SnapshotBufferMb), vm.StatusText);
        Assert.Contains(Line("str.LF.Stack.Recording", 2, 16), vm.StatusText);
    }

    [Fact]
    public async Task The_Stop_note_gives_the_stacks_and_their_cost_in_microseconds()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x1", new NameKey(1, 0, 9, 0)));
        await Fetch(vm);
        vm.TraceEnabled = true;
        vm.ToggleStackCommand.Execute(vm.Results.Single());
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsRecording);
        // A stacks-only trace: the snapshot buffer is allocated with no parameter ring (design 3.2).
        dump.StopTrace = new TraceInfo
        {
            Allocated = true, Quiesced = true, Gen = 1, Written = 40, QpcFreq = 10_000_000,
            Snap = new SnapInfo { Allocated = true, Rings = 0 },
            Stack = new StackInfo { Rings = 1, Depth = 16, Captures = 100, SkippedBudget = 5, DroppedBudget = 3,
                                    SpentTicks = 1_400, MaxTicks = 51 },
        };
        await vm.StopCommand.ExecuteAsync(null);
        Assert.DoesNotContain(Line("str.LF.Snap.StopNote", 0L, 0L), vm.StatusText);   // no parameter ring, no sentence
        Assert.Contains(Line("str.LF.Stack.StopNote", 100UL, 8UL, 3UL), vm.StatusText);
        // 1,400 ticks over 100 captures at 10 MHz is 1.4 us each; the longest, 51 ticks, 5.1 us.
        Assert.Contains(Line("str.LF.Stack.Cost", 1.4, 5.1), vm.StatusText);
    }

    [Fact]
    public async Task The_game_figure_holds_the_snapshot_buffer_for_stacks_alone()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x1", new NameKey(1, 0, 9, 0)));
        await Fetch(vm);
        vm.TraceEnabled = true;
        Assert.Equal(vm.TraceBufferMb, vm.TraceGameMb);
        vm.ToggleStackCommand.Execute(vm.Results.Single());
        Assert.Equal(vm.TraceBufferMb + vm.SnapshotBufferMb, vm.TraceGameMb);
    }

    [Fact]
    public void The_K_estimate_counts_stack_rings_as_the_DLL_does()
    {
        // K = (bytes - 64 (P + S)) / (sum_P (24 + cap_p) + S (24 + 8 depth)): design 2.2's two examples.
        var one = new[] { new LiveFuncsViewModel.SnapRate("A::P", 0, 64, 0x400) };
        Assert.Equal(139_809, LiveFuncsViewModel.EstimateSnapshots(one, 32L << 20, 1000, 10000, stackRings: 1).SlotsPerRing);
        var five = Enumerable.Range(0, 5).Select(i => new LiveFuncsViewModel.SnapRate($"A::P{i}", 0, 64, 0x400)).ToList();
        Assert.Equal(45_099, LiveFuncsViewModel.EstimateSnapshots(five, 32L << 20, 1000, 10000, stackRings: 2).SlotsPerRing);

        // Stacks alone have a K too, and the DLL refuses one below 8 (review L6).
        var none = Array.Empty<LiveFuncsViewModel.SnapRate>();
        var alone = LiveFuncsViewModel.EstimateSnapshots(none, 32L << 20, 1000, 10000, stackRings: 1);
        Assert.Equal((33_554_432L - 64) / 152, alone.SlotsPerRing);
        Assert.False(alone.TooSmall);
        Assert.True(LiveFuncsViewModel.EstimateSnapshots(none, 8L << 20, 1000, 10000, stackRings: 7000).TooSmall);   // K = 7

        // A stack ring is 64-aligned too: its 64 bytes come off the top, one byte short of 8 slots is 7.
        Assert.Equal(8, LiveFuncsViewModel.EstimateSnapshots(none, 64 + 8 * 152, 1000, 10000, stackRings: 1).SlotsPerRing);
        Assert.Equal(7, LiveFuncsViewModel.EstimateSnapshots(none, 64 + 8 * 152 - 1, 1000, 10000, stackRings: 1).SlotsPerRing);
        Assert.Equal(7, LiveFuncsViewModel.EstimateSnapshots(one, 2 * 64 + 8 * 240 - 1, 1000, 10000, stackRings: 1).SlotsPerRing);
    }

    [Fact]
    public async Task A_stack_choice_scopes_the_trace_the_estimate_compares_with_and_needs_the_trace_too()
    {
        var (vm, dump) = MakeVm();
        // P: 1,000 calls/s of a 2 KB block; 8 MB keeps ~1,900 of its calls, about 2 s. Alone in the scope, the trace keeps
        // 64 MB / (1,000 x 80 B/s) = ~840 s: orange. Hot, chosen for a stack, adds 1,000,000 calls/s to the scope, so
        // the trace keeps under a second and P's 2 s is no longer short of it (review L6).
        dump.NextGet = ResultOf(10_000, Row("A", "P", "0x1", new NameKey(1, 0, 9, 0), count: 10_000, size: 2048),
                                Row("A", "Hot", "0x2", new NameKey(2, 0, 9, 0), count: 10_000_000));
        await Fetch(vm);
        vm.TraceEnabled = true;
        vm.SnapshotBufferExponent = 3;
        var p = vm.Results.Single(r => r.FuncName == "P");
        vm.ToggleSnapshotCommand.Execute(p);
        Assert.True(vm.SnapshotEstimateWarn);
        vm.ToggleStackCommand.Execute(vm.Results.Single(r => r.FuncName == "Hot"));
        Assert.False(vm.SnapshotEstimateWarn);

        vm.ToggleSnapshotCommand.Execute(p);             // stacks alone: no parameter estimate to show
        Assert.True(vm.HasStackChoices);
        Assert.Equal("", vm.SnapshotEstimate);
        vm.TraceEnabled = false;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Contains(Line("str.LF.Stack.NeedsTrace"), vm.StatusText);
        Assert.DoesNotContain(Line("str.LF.Snap.NeedsTrace"), vm.StatusText);
    }

    [Fact]
    public async Task Clearing_and_a_disconnect_drop_the_stack_choices()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x1", new NameKey(1, 0, 9, 0)));
        await Fetch(vm);
        vm.TraceEnabled = true;
        var row = vm.Results.Single();
        vm.ToggleStackCommand.Execute(row);
        Assert.True(row.IsStackChosen);
        vm.ClearSnapshotsCommand.Execute(null);
        Assert.Empty(vm.StackFunctions);
        Assert.False(row.IsStackChosen);

        vm.ToggleStackCommand.Execute(row);
        Assert.True(vm.HasStackChoices);
        vm.ResetOnDisconnect();
        Assert.Empty(vm.StackFunctions);
        Assert.False(vm.HasStackChoices);
        Assert.False(row.IsStackChosen);
        Assert.Equal(vm.TraceBufferMb, vm.TraceGameMb);
    }

    [Fact]
    public void The_stack_choice_pins_the_DLLs_numbers_read_from_Linie_h_and_Macht_h()
    {
        string src = Path.Combine(RepoRoot(), "dll", "src");
        var linie = File.ReadAllText(Path.Combine(src, "Linie.h"));
        var macht = File.ReadAllText(Path.Combine(src, "Macht.h"));
        Assert.Equal((long)LiveFuncsViewModel.StackDepth, HeaderConst(linie, "kStackDefaultDepth"));
        Assert.Equal((long)LiveFuncsViewModel.StackPerFuncPerSec, HeaderConst(linie, "kStackDefaultPerRingPerSec"));
        Assert.Equal((long)LiveFuncsViewModel.StackTotalPerSec, HeaderConst(linie, "kStackDefaultTotalPerSec"));
        Assert.Equal((long)LiveFuncsViewModel.StackLowPerFuncPerSec, HeaderConst(linie, "kStackLowPerRingPerSec"));
        Assert.Equal((long)LiveFuncsViewModel.StackLowTotalPerSec, HeaderConst(linie, "kStackLowTotalPerSec"));
        // Review L8: the entry flags and the slot flags the Call Trace tab decodes.
        Assert.Equal((long)StackInfo.TakenEntryFlag, HeaderConst(linie, "kTraceStackTaken"));
        Assert.Equal((long)StackInfo.BudgetEntryFlag, HeaderConst(linie, "kTraceStackBudget"));
        Assert.Equal((long)StackSlot.NoCapturer, HeaderConst(linie, "kSnapNoCapturer"));
        Assert.Equal((long)StackSlot.Partial, HeaderConst(macht, "kStackPartial"));
        Assert.Equal((long)StackSlot.Fault, HeaderConst(macht, "kStackFault"));
        Assert.Equal((long)StackSlot.More, HeaderConst(macht, "kStackMore"));
        Assert.Equal((long)StackSlot.BadSp, HeaderConst(macht, "kStackBadSp"));
        Assert.Equal((long)StackSlot.LowStack, HeaderConst(macht, "kStackLowStack"));
    }

    // ---- [LIVEFUNCS-STEP3] S3-U3: the view ----

    /// <summary>A DataGrid column is not in the visual tree, so the code-behind reveals the hidden ones by header. A
    /// hidden column that the loop never compares stays hidden for good, with nothing failing: so every header the axaml
    /// hides must be fetched in ApplyTickColumnVisibility AND compared in its loop.</summary>
    [Fact]
    public void Every_hidden_column_the_Stack_column_included_is_revealed_with_Trace()
    {
        string root = RepoRoot();
        var axaml = File.ReadAllText(Path.Combine(root, "ui", "UE5DumpUI", "Views", "LiveFuncsPanel.axaml"));
        var hidden = Regex.Matches(axaml,
                @"<DataGridTemplateColumn Header=""\{StaticResource (?<key>[\w.]+)\}""[^>]*IsVisible=""False""[^>]*>")
            .Select(m => m.Groups["key"].Value).ToList();
        Assert.Contains("str.LF.Col.Stack", hidden);

        var codeBehind = File.ReadAllText(Path.Combine(root, "ui", "UE5DumpUI", "Views", "LiveFuncsPanel.axaml.cs"));
        var method = Regex.Match(codeBehind, @"void ApplyTickColumnVisibility\(\)\s*\{(?<body>.*?)\n    \}",
                                 RegexOptions.Singleline);
        Assert.True(method.Success, "ApplyTickColumnVisibility not found");
        string body = method.Groups["body"].Value;
        foreach (var key in hidden)
        {
            var fetched = Regex.Match(body, @"(?<var>\w+)\s*=\s*Core\.Res\.Get\(""" + Regex.Escape(key) + @"""\)");
            Assert.True(fetched.Success, $"{key} is not fetched in ApplyTickColumnVisibility");
            Assert.True(Regex.IsMatch(body, @"==\s*" + fetched.Groups["var"].Value + @"\b"),
                        $"{key} is fetched but the loop never compares it");
        }
    }

    [Fact]
    public void The_Stack_column_copies_Params_and_the_snapshot_rows_show_for_either_choice()
    {
        var axaml = File.ReadAllText(Path.Combine(RepoRoot(), "ui", "UE5DumpUI", "Views", "LiveFuncsPanel.axaml"));
        var col = Regex.Match(axaml,
            @"<DataGridTemplateColumn Header=""\{StaticResource str\.LF\.Col\.Stack\}""[^>]*>.*?</DataGridTemplateColumn>",
            RegexOptions.Singleline);
        Assert.True(col.Success, "no Stack? column");
        string open = col.Value[..(col.Value.IndexOf('>') + 1)];
        Assert.Contains("CanUserSort=\"False\"", open, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"False\"", open, StringComparison.Ordinal);
        Assert.DoesNotContain("SortMemberPath", col.Value, StringComparison.Ordinal);
        // The compiled-binding routes the Params? column already ships trimmed.
        Assert.Contains("<DataTemplate x:DataType=\"m:PeProfileEntry\">", col.Value, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IsStackChosen, Mode=OneWay}\"", col.Value, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding $parent[UserControl].((vm:LiveFuncsViewModel)DataContext).ToggleStackCommand}\"",
                        col.Value, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"{Binding}\"", col.Value, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding $parent[UserControl].((vm:LiveFuncsViewModel)DataContext).CanSnapshot}\"",
                        col.Value, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding CanChooseStack}\"", col.Value, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{StaticResource str.Tip.LF.Stack.Choose}\"", col.Value, StringComparison.Ordinal);

        // The choices row carries the stack count beside the parameters' count.
        Assert.Contains("{Binding StackCountText}", EnclosingStackPanel(axaml, "{Binding SnapshotCountText}"),
                        StringComparison.Ordinal);
        // The estimate row: for stacks alone it still has the buffer's refusal to show (S3-U2), so it shows for
        // either choice -- an OR, which an AND or a lone HasSnapshotChoices would silently hide.
        string estimateHead = OpeningOfEnclosingStackPanel(axaml, "{Binding SnapshotEstimate}");
        Assert.Contains("BoolConverters.Or", estimateHead, StringComparison.Ordinal);
        Assert.Contains("HasSnapshotChoices", estimateHead, StringComparison.Ordinal);
        Assert.Contains("HasStackChoices", estimateHead, StringComparison.Ordinal);
    }

    // ---- [LIVEFUNCS-STEP3] S3-U8: the stack budget, Standard or Low, and the warning (T20) ----

    /// <summary>A view model with one function chosen for a stack, Trace on.</summary>
    private static async Task<(LiveFuncsViewModel vm, FakeDumpService dump)> WithAStackChosen()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x1", new NameKey(1, 0, 9, 0)));
        await Fetch(vm);
        vm.TraceEnabled = true;
        vm.ToggleStackCommand.Execute(vm.Results.Single());
        Assert.True(vm.HasStackChoices);
        return (vm, dump);
    }

    /// <summary>One recording, started and stopped: what its Start asked of the stacks.</summary>
    private static async Task<StackStartOptions> StacksSentByAStart(LiveFuncsViewModel vm, FakeDumpService dump)
    {
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsRecording);
        var stacks = dump.LastTrace?.Snapshots?.Stacks;
        Assert.NotNull(stacks);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.False(vm.IsRecording);
        return stacks!;
    }

    [Fact]
    public async Task The_stack_budget_is_Standard_by_default_and_Low_sends_half_of_it()
    {
        var (vm, dump) = await WithAStackChosen();
        Assert.False(vm.StackBudgetLow);
        Assert.True(vm.StackBudgetStandard);
        var standard = await StacksSentByAStart(vm, dump);
        Assert.Equal((16, 100, 200), (standard.Depth, standard.PerRingPerSec, standard.TotalPerSec));

        vm.StackBudgetLow = true;                        // the Low radio
        Assert.False(vm.StackBudgetStandard);
        var low = await StacksSentByAStart(vm, dump);
        Assert.Equal((16, 50, 100), (low.Depth, low.PerRingPerSec, low.TotalPerSec));   // the depth is not the budget's

        vm.StackBudgetStandard = true;                   // the Standard radio
        Assert.False(vm.StackBudgetLow);
        var again = await StacksSentByAStart(vm, dump);
        Assert.Equal((100, 200), (again.PerRingPerSec, again.TotalPerSec));
    }

    [Fact]
    public async Task Both_stack_budgets_sent_are_the_ones_Linie_h_declares()
    {
        var linie = File.ReadAllText(Path.Combine(RepoRoot(), "dll", "src", "Linie.h"));
        var (vm, dump) = await WithAStackChosen();
        var standard = await StacksSentByAStart(vm, dump);
        Assert.Equal(HeaderConst(linie, "kStackDefaultPerRingPerSec"), standard.PerRingPerSec);
        Assert.Equal(HeaderConst(linie, "kStackDefaultTotalPerSec"), standard.TotalPerSec);

        vm.StackBudgetLow = true;
        var low = await StacksSentByAStart(vm, dump);
        Assert.Equal(HeaderConst(linie, "kStackLowPerRingPerSec"), low.PerRingPerSec);
        Assert.Equal(HeaderConst(linie, "kStackLowTotalPerSec"), low.TotalPerSec);

        // str.LF.Stack.Warning says Low halves the budget: a re-weigh (D3) that moves one pair alone makes it wrong.
        Assert.Equal(HeaderConst(linie, "kStackDefaultPerRingPerSec"), 2 * HeaderConst(linie, "kStackLowPerRingPerSec"));
        Assert.Equal(HeaderConst(linie, "kStackDefaultTotalPerSec"), 2 * HeaderConst(linie, "kStackLowTotalPerSec"));
    }

    [Fact]
    public async Task The_stack_budget_holds_still_while_recording()
    {
        var (vm, dump) = await WithAStackChosen();
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsRecording);
        vm.StackBudgetLow = true;                        // the radios are disabled; a write that arrives anyway is refused
        Assert.False(vm.StackBudgetLow);
        Assert.True(vm.StackBudgetStandard);
        await vm.StopCommand.ExecuteAsync(null);

        vm.StackBudgetLow = true;
        Assert.True(vm.StackBudgetLow);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsRecording);
        vm.StackBudgetStandard = true;
        Assert.True(vm.StackBudgetLow);
        Assert.Equal((50, 100), (dump.LastTrace!.Snapshots!.Stacks!.PerRingPerSec, dump.LastTrace.Snapshots.Stacks.TotalPerSec));
    }

    [Fact]
    public void Each_budget_radio_says_its_numbers()
    {
        var (vm, _) = MakeVm();
        Assert.Equal(Line("str.Tip.LF.Stack.Standard", LiveFuncsViewModel.StackPerFuncPerSec, LiveFuncsViewModel.StackTotalPerSec),
                     vm.StackStandardTip);
        Assert.Equal(Line("str.Tip.LF.Stack.Low", LiveFuncsViewModel.StackLowPerFuncPerSec, LiveFuncsViewModel.StackLowTotalPerSec),
                     vm.StackLowTip);
        // A template without its placeholders would format to the same text and say no number at all.
        Assert.Contains("100", vm.StackStandardTip, StringComparison.Ordinal);
        Assert.Contains("200", vm.StackStandardTip, StringComparison.Ordinal);
        Assert.Contains("50", vm.StackLowTip, StringComparison.Ordinal);
        Assert.Contains("100", vm.StackLowTip, StringComparison.Ordinal);
    }

    [Fact]
    public void The_budget_radios_and_the_warning_show_with_a_stack_choice_and_the_radios_hold_still_while_recording()
    {
        string views = Path.Combine(RepoRoot(), "ui", "UE5DumpUI", "Views");
        var axaml = File.ReadAllText(Path.Combine(views, "LiveFuncsPanel.axaml"));
        // Compiled bindings (trimmed AOT): the view's own data type, and the radios outside any row template.
        Assert.Contains("x:DataType=\"vm:LiveFuncsViewModel\"", axaml[..axaml.IndexOf('>')], StringComparison.Ordinal);
        string row = EnclosingStackPanel(axaml, "{Binding SnapshotCountText}");

        var radios = Regex.Matches(axaml, @"<RadioButton\b[^>]*/>").Select(m => m.Value).ToList();
        string standard = Assert.Single(radios, r => r.Contains("IsChecked=\"{Binding StackBudgetStandard, Mode=TwoWay}\"",
                                                                StringComparison.Ordinal));
        string low = Assert.Single(radios, r => r.Contains("IsChecked=\"{Binding StackBudgetLow, Mode=TwoWay}\"",
                                                           StringComparison.Ordinal));
        foreach (var r in new[] { standard, low })
        {
            Assert.Contains(r, row, StringComparison.Ordinal);   // beside the stack count, in the choices row
            Assert.Contains("IsVisible=\"{Binding HasStackChoices}\"", r, StringComparison.Ordinal);
            Assert.Contains("IsEnabled=\"{Binding !IsRecording}\"", r, StringComparison.Ordinal);
        }
        Assert.Contains("Content=\"{StaticResource str.LF.Stack.Standard}\"", standard, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding StackStandardTip}\"", standard, StringComparison.Ordinal);
        Assert.Contains("Content=\"{StaticResource str.LF.Stack.Low}\"", low, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding StackLowTip}\"", low, StringComparison.Ordinal);

        // One group, and no other view's: Avalonia groups radios by name across the window every panel shares.
        static string Group(string radio)
        {
            var g = Regex.Match(radio, @"GroupName=""(?<g>[^""]+)""");
            Assert.True(g.Success, "a budget radio has no GroupName");
            return g.Groups["g"].Value;
        }
        string group = Group(standard);
        Assert.Equal(group, Group(low));
        foreach (var file in Directory.GetFiles(views, "*.axaml"))
        {
            int uses = Regex.Matches(File.ReadAllText(file), $@"GroupName=""{Regex.Escape(group)}""").Count;
            Assert.Equal(Path.GetFileName(file) == "LiveFuncsPanel.axaml" ? 2 : 0, uses);
        }

        // The warning: wrapping, on a choice, in the colour the panel's own warnings use (the orange estimate line's).
        var warning = Regex.Match(axaml, @"<TextBlock\b[^>]*Text=""\{StaticResource str\.LF\.Stack\.Warning\}""[^>]*/>");
        Assert.True(warning.Success, "no TextBlock shows str.LF.Stack.Warning");
        Assert.Contains("IsVisible=\"{Binding HasStackChoices}\"", warning.Value, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", warning.Value, StringComparison.Ordinal);
        var orange = Regex.Match(axaml, @"<TextBlock Text=""\{Binding SnapshotEstimate\}"" IsVisible=""\{Binding SnapshotEstimateWarn\}""\s+Foreground=""(?<c>[^""]+)""");
        Assert.True(orange.Success, "the estimate's warning line moved");
        Assert.Contains($"Foreground=\"{orange.Groups["c"].Value}\"", warning.Value, StringComparison.Ordinal);
        Assert.True(Line("str.LF.Stack.Warning").Length > 0);
    }

    /// <summary>Where the last StackPanel opened before <paramref name="marker"/> starts, and where the marker is.</summary>
    private static (int Start, int At) FindEnclosingStackPanel(string axaml, string marker)
    {
        int at = axaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{marker} not found");
        var opens = Regex.Matches(axaml[..at], @"<StackPanel[\s>]");
        Assert.True(opens.Count > 0, $"no StackPanel encloses {marker}");
        return (opens[^1].Index, at);
    }

    /// <summary>From the StackPanel <paramref name="marker"/> sits in to the marker: its attributes and property
    /// elements.</summary>
    private static string OpeningOfEnclosingStackPanel(string axaml, string marker)
    {
        var (start, at) = FindEnclosingStackPanel(axaml, marker);
        return axaml[start..at];
    }

    /// <summary>The StackPanel <paramref name="marker"/> sits in, to its closing tag; a nested StackPanel would end it
    /// early, so the test refuses one.</summary>
    private static string EnclosingStackPanel(string axaml, string marker)
    {
        var (start, at) = FindEnclosingStackPanel(axaml, marker);
        int end = axaml.IndexOf("</StackPanel>", at, StringComparison.Ordinal);
        Assert.True(end > at, $"the StackPanel around {marker} is not closed");
        string panel = axaml[start..end];
        Assert.Single(Regex.Matches(panel, @"<StackPanel[\s>]"));
        return panel;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "build.ps1"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
