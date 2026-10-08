using System.Text.RegularExpressions;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LIVEFUNCS-STEP2] Live Funcs' parameter snapshots: choosing by name (U5), the estimate (U6), the bulk choice (U7)
/// and the view's wiring (U8). docs/live-funcs-step2-items.md. [LIVEFUNCS-STEP3] The native-stack choice beside them,
/// with its view, budget, per-frame question and estimate: the S3-U items of docs/live-funcs-step3-items.md.
/// Clear choices: the one clear that drops the trace's ticks with them. [LF-COMPACT-TOP] The controls above the table,
/// made smaller: what each still says and when.
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

    private static (LiveFuncsViewModel vm, FakeDumpService dump) MakeVm(bool experimental = true,
                                                                         IPlatformService? platform = null)
    {
        var dump = new FakeDumpService();
        var vm = new LiveFuncsViewModel(dump, new NoopLogger(), platform, experimentalGate: new Gate(experimental))
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
        Assert.Equal((16, 25, 50), (st.Depth, st.PerRingPerSec, st.TotalPerSec));
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
        // The request model's own defaults are the DLL's too: a Start built without the view model sends them.
        var defaults = new StackStartOptions();
        Assert.Equal(HeaderConst(linie, "kStackDefaultDepth"), defaults.Depth);
        Assert.Equal(HeaderConst(linie, "kStackDefaultPerRingPerSec"), defaults.PerRingPerSec);
        Assert.Equal(HeaderConst(linie, "kStackDefaultTotalPerSec"), defaults.TotalPerSec);
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
        Assert.Equal((16, 25, 50), (standard.Depth, standard.PerRingPerSec, standard.TotalPerSec));

        vm.StackBudgetLow = true;                        // the Low radio
        Assert.False(vm.StackBudgetStandard);
        var low = await StacksSentByAStart(vm, dump);
        Assert.Equal((16, 12, 25), (low.Depth, low.PerRingPerSec, low.TotalPerSec));   // the depth is not the budget's

        vm.StackBudgetStandard = true;                   // the Standard radio
        Assert.False(vm.StackBudgetLow);
        var again = await StacksSentByAStart(vm, dump);
        Assert.Equal((25, 50), (again.PerRingPerSec, again.TotalPerSec));
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
        // Halved and rounded down: a budget is a whole number of captures.
        Assert.Equal(HeaderConst(linie, "kStackDefaultPerRingPerSec") / 2, HeaderConst(linie, "kStackLowPerRingPerSec"));
        Assert.Equal(HeaderConst(linie, "kStackDefaultTotalPerSec") / 2, HeaderConst(linie, "kStackLowTotalPerSec"));
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
        Assert.Equal((12, 25), (dump.LastTrace!.Snapshots!.Stacks!.PerRingPerSec, dump.LastTrace.Snapshots.Stacks.TotalPerSec));
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
        Assert.Contains("25", vm.StackStandardTip, StringComparison.Ordinal);
        Assert.Contains("50", vm.StackStandardTip, StringComparison.Ordinal);
        Assert.Contains("12", vm.StackLowTip, StringComparison.Ordinal);
        Assert.Contains("25", vm.StackLowTip, StringComparison.Ordinal);
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

        // The warning's place, wrapping and colour moved with the estimate it now follows: S3-U7's view test.
        Assert.True(Line("str.LF.Stack.Warning").Length > 0);
    }

    // ---- [LIVEFUNCS-STEP3] S3-U6: choosing a per-frame function's stack asks once (T9.2) ----

    /// <summary>A view model showing two per-frame functions and a plain one, Trace on, whose question records what it
    /// was asked and answers <paramref name="answer"/>.</summary>
    private static async Task<(LiveFuncsViewModel vm, FakeDumpService dump, List<string> asked)> WithPerFrameRows(bool answer)
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000,
            Row("A", "Tick", "0x1", new NameKey(1, 0, 9, 0), count: 600, perFrame: true),
            Row("A", "Open", "0x2", new NameKey(2, 0, 9, 0)),
            Row("B", "Tick", "0x3", new NameKey(3, 0, 9, 0), count: 600, perFrame: true));
        await Fetch(vm);
        vm.TraceEnabled = true;
        var asked = new List<string>();
        vm.ConfirmStackPerFrame = q => { asked.Add(q); return Task.FromResult(answer); };
        return (vm, dump, asked);
    }

    private static PeProfileEntry Shown(LiveFuncsViewModel vm, string cls, string func)
        => vm.Results.Single(r => r.ClassName == cls && r.FuncName == func);

    /// <summary>Counts the row's IsStackChosen notifications: what a box bound to it reads back.</summary>
    private static Func<int> CountStackChosenRaised(PeProfileEntry row)
    {
        int raised = 0;
        row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PeProfileEntry.IsStackChosen)) raised++; };
        return () => raised;
    }

    [Fact]
    public async Task Choosing_a_per_frame_stack_asks_once_and_a_yes_holds_for_the_connection()
    {
        var (vm, _, asked) = await WithPerFrameRows(answer: true);
        var tick = Shown(vm, "A", "Tick");

        await vm.ToggleStackCommand.ExecuteAsync(tick);
        Assert.Single(asked);
        Assert.Equal(new[] { "A::Tick" }, vm.StackFunctions);
        Assert.True(tick.IsStackChosen);

        await vm.ToggleStackCommand.ExecuteAsync(tick);                     // dropped
        Assert.Empty(vm.StackFunctions);
        await vm.ToggleStackCommand.ExecuteAsync(tick);                     // choosing it again: answered already
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "B", "Tick"));   // another per-frame function: the same
        Assert.Single(asked);
        Assert.Equal(new[] { "A::Tick", "B::Tick" }, vm.StackFunctions);

        // The question names the function clicked and the budget a Start would send.
        Assert.Equal(Line("str.LF.Stack.PerFrame.Message", "A::Tick", LiveFuncsViewModel.StackPerFuncPerSec,
                          LiveFuncsViewModel.StackTotalPerSec), asked[0]);
    }

    [Fact]
    public async Task A_no_leaves_the_per_frame_stack_unchosen_reads_its_box_back_and_asks_again()
    {
        var (vm, _, asked) = await WithPerFrameRows(answer: false);
        var tick = Shown(vm, "A", "Tick");
        var raised = CountStackChosenRaised(tick);

        await vm.ToggleStackCommand.ExecuteAsync(tick);
        Assert.Single(asked);
        Assert.Empty(vm.StackFunctions);
        Assert.False(vm.HasStackChoices);
        Assert.False(tick.IsStackChosen);
        // The view's box flipped on the click, before the question: the unchanged value is raised so it reads it back.
        Assert.Equal(1, raised());

        vm.StackBudgetLow = true;
        await vm.ToggleStackCommand.ExecuteAsync(tick);                     // a no is not an answer to keep
        Assert.Equal(2, asked.Count);
        Assert.Empty(vm.StackFunctions);
        Assert.Equal(2, raised());
        Assert.Equal(Line("str.LF.Stack.PerFrame.Message", "A::Tick", LiveFuncsViewModel.StackLowPerFuncPerSec,
                          LiveFuncsViewModel.StackLowTotalPerSec), asked[1]);

        vm.ConfirmStackPerFrame = null;                                     // no view to ask in: not chosen either
        await vm.ToggleStackCommand.ExecuteAsync(tick);
        Assert.Empty(vm.StackFunctions);
        Assert.Equal(3, raised());
    }

    /// <summary>A guard: no step-3 build has ever asked, so this holds before S3-U6 too.</summary>
    [Fact]
    public async Task A_plain_row_never_asks()
    {
        var (vm, _, asked) = await WithPerFrameRows(answer: false);
        var open = Shown(vm, "A", "Open");

        await vm.ToggleStackCommand.ExecuteAsync(open);
        Assert.Equal(new[] { "A::Open" }, vm.StackFunctions);
        Assert.True(open.IsStackChosen);
        await vm.ToggleStackCommand.ExecuteAsync(open);
        Assert.Empty(vm.StackFunctions);
        Assert.Empty(asked);
    }

    /// <summary>Review S3U6-PERFRAME-BY-ROW: a stack is chosen by name, so a function that reloaded at a new address and
    /// runs every frame there is asked about from either of its rows: the plain row's box chooses the per-frame one too.</summary>
    [Fact]
    public async Task A_plain_row_of_a_function_per_frame_at_another_address_asks_too()
    {
        var (vm, dump) = MakeVm();
        var key = new NameKey(1, 0, 9, 0);
        dump.NextGet = ResultOf(10_000, Row("A", "Tick", "0x1", key),
                                Row("A", "Tick", "0x2", key, count: 600, perFrame: true));
        await Fetch(vm);
        vm.TraceEnabled = true;
        var asked = new List<string>();
        bool answer = false;
        vm.ConfirmStackPerFrame = q => { asked.Add(q); return Task.FromResult(answer); };
        var plain = vm.Results.Single(r => r.FuncAddr == "0x1");
        var perFrame = vm.Results.Single(r => r.FuncAddr == "0x2");
        Assert.False(plain.IsPerFrame);

        await vm.ToggleStackCommand.ExecuteAsync(plain);
        Assert.Single(asked);
        Assert.Empty(vm.StackFunctions);
        Assert.False(plain.IsStackChosen);
        Assert.False(perFrame.IsStackChosen);

        answer = true;
        await vm.ToggleStackCommand.ExecuteAsync(plain);
        Assert.Equal(2, asked.Count);
        Assert.Equal(new[] { "A::Tick" }, vm.StackFunctions);
        Assert.True(plain.IsStackChosen);
        Assert.True(perFrame.IsStackChosen);
    }

    /// <summary>A choice made while its function ran plain is followed by name onto the next fetch's rows, which can mark
    /// it per-frame: dropping it there asks nothing, and no kept yes hides the question it would be.</summary>
    [Fact]
    public async Task Dropping_a_stack_choice_never_asks_though_the_next_fetch_marks_it_per_frame()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "Tick", "0x1", new NameKey(1, 0, 9, 0)));
        await Fetch(vm);
        vm.TraceEnabled = true;
        var asked = new List<string>();
        vm.ConfirmStackPerFrame = q => { asked.Add(q); return Task.FromResult(false); };
        await vm.ToggleStackCommand.ExecuteAsync(vm.Results.Single());      // plain: chosen without a question
        Assert.Equal(new[] { "A::Tick" }, vm.StackFunctions);

        dump.NextGet = ResultOf(10_000, Row("A", "Tick", "0x1", new NameKey(1, 0, 9, 0), count: 600, perFrame: true));
        await vm.RefreshCommand.ExecuteAsync(null);
        var tick = vm.Results.Single();
        Assert.True(tick.IsPerFrame);
        Assert.True(tick.IsStackChosen);

        await vm.ToggleStackCommand.ExecuteAsync(tick);
        Assert.Empty(asked);
        Assert.Empty(vm.StackFunctions);
        Assert.False(tick.IsStackChosen);

        await vm.ToggleStackCommand.ExecuteAsync(tick);                     // choosing it now does ask
        Assert.Single(asked);
        Assert.Empty(vm.StackFunctions);
    }

    [Fact]
    public async Task A_disconnect_forgets_the_yes_so_the_next_connection_asks_again()
    {
        var (vm, _, asked) = await WithPerFrameRows(answer: true);
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "Tick"));
        Assert.Single(asked);

        vm.ResetOnDisconnect();
        await Fetch(vm);                                                    // the next connection's rows
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "Tick"));
        Assert.Equal(2, asked.Count);
        Assert.Equal(new[] { "A::Tick" }, vm.StackFunctions);
    }

    [Fact]
    public async Task A_yes_that_arrives_after_the_connection_dropped_chooses_nothing_and_is_not_kept()
    {
        var (vm, _, asked) = await WithPerFrameRows(answer: true);
        var tick = Shown(vm, "A", "Tick");
        var raised = CountStackChosenRaised(tick);
        vm.ConfirmStackPerFrame = q => { asked.Add(q); vm.ResetOnDisconnect(); return Task.FromResult(true); };

        await vm.ToggleStackCommand.ExecuteAsync(tick);
        Assert.Single(asked);
        Assert.Empty(vm.StackFunctions);                                    // the row is the old process's
        Assert.False(tick.IsStackChosen);
        Assert.Equal(1, raised());

        vm.ConfirmStackPerFrame = q => { asked.Add(q); return Task.FromResult(true); };
        await Fetch(vm);
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "Tick"));
        Assert.Equal(2, asked.Count);                                       // that yes was given for no connection
        Assert.Equal(new[] { "A::Tick" }, vm.StackFunctions);
    }

    [Fact]
    public void The_view_asks_the_per_frame_question_in_a_dialog_and_the_question_says_why()
    {
        var codeBehind = File.ReadAllText(Path.Combine(RepoRoot(), "ui", "UE5DumpUI", "Views", "LiveFuncsPanel.axaml.cs"));
        // The view model's text is the message: it names the function and the budget.
        var wiring = Regex.Match(codeBehind,
            @"_wired\.ConfirmStackPerFrame\s*=\s*(?<q>\w+)\s*=>\s*ConfirmDialog\.ShowAsync\(\s*" +
            @"Core\.Res\.Get\(""str\.LF\.Stack\.PerFrame\.Title""\)\s*,\s*\k<q>\s*,(?<rest>[^;]*);");
        Assert.True(wiring.Success, "LiveFuncsPanel does not ask ConfirmStackPerFrame in a ConfirmDialog");
        Assert.Contains("Core.Res.Get(\"str.LF.Stack.PerFrame.Run\")", wiring.Groups["rest"].Value, StringComparison.Ordinal);
        Assert.Contains("Core.Res.Get(\"str.LF.Stack.PerFrame.Cancel\")", wiring.Groups["rest"].Value, StringComparison.Ordinal);

        // Why it asks: a per-frame function is called every frame, so its stacks are taken every frame up to the budget.
        string question = Line("str.LF.Stack.PerFrame.Message", "A::Tick", 25, 50);
        Assert.Contains("A::Tick", question, StringComparison.Ordinal);
        Assert.Contains("every frame", question, StringComparison.Ordinal);
        Assert.Contains("25", question, StringComparison.Ordinal);
        Assert.Contains("50", question, StringComparison.Ordinal);
        Assert.True(Line("str.LF.Stack.PerFrame.Title").Length > 0);
    }

    // ---- [LIVEFUNCS-STEP3] S3-U7: the stack estimate line (T9.1) ----

    [Fact]
    public void The_stack_estimate_holds_each_function_to_its_budget_and_all_of_them_to_the_total()
    {
        // One busy function and one rare one: the busy one is held to 25 a second, the total of 50 is not reached.
        var perFunc = LiveFuncsViewModel.EstimateStacks(new[] { 100.0, 1.0 }, 25, 50, 10.0);
        Assert.Equal(26.0, perFunc.CapturesPerSec, 9);
        Assert.Equal(0.26, perFunc.MsPerSec, 9);             // 26 x 10 us

        // Three at 30 a second each: 75 within their own budgets, held to 50 in all.
        var total = LiveFuncsViewModel.EstimateStacks(new[] { 30.0, 30.0, 30.0 }, 25, 50, 28.0);
        Assert.Equal(50.0, total.CapturesPerSec, 9);
        Assert.Equal(1.4, total.MsPerSec, 9);                // 50 x 28 us

        // A function not called last time counts nothing, and neither does a choice of nothing.
        var mixed = LiveFuncsViewModel.EstimateStacks(new[] { 0.0, 4.0 }, 25, 50, 10.0);
        Assert.Equal(4.0, mixed.CapturesPerSec, 9);
        var zero = LiveFuncsViewModel.EstimateStacks(new[] { 0.0, 0.0 }, 25, 50, 10.0);
        Assert.Equal(0.0, zero.CapturesPerSec);
        Assert.Equal(0.0, zero.MsPerSec);
        Assert.Equal(0.0, LiveFuncsViewModel.EstimateStacks(Array.Empty<double>(), 25, 50, 10.0).MsPerSec);
    }

    private static readonly NameKey KeyF = new(1, 0, 9, 0), KeyG = new(2, 0, 9, 0), KeyH = new(3, 0, 9, 0);

    /// <summary>A view model whose last fetch saw A::F and A::G called 100 times a second and A::H 5 times, Trace on,
    /// nothing chosen yet.</summary>
    private static async Task<(LiveFuncsViewModel vm, FakeDumpService dump)> WithRatesToEstimate()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x1", KeyF, count: 1_000), Row("A", "G", "0x2", KeyG, count: 1_000),
                                Row("A", "H", "0x3", KeyH, count: 50));
        await Fetch(vm);
        vm.TraceEnabled = true;
        return (vm, dump);
    }

    /// <summary>The stack estimate the view model shows now; an empty one is no line at all, which fails here before
    /// any sentence is compared.</summary>
    private static string StackLine(LiveFuncsViewModel vm)
    {
        string line = vm.StackEstimate;
        Assert.False(string.IsNullOrEmpty(line), "no stack estimate line");
        return line;
    }

    /// <summary>The line en.axaml makes for an estimate, with the cost of a capture measured or assumed.</summary>
    private static string EstimateLine(double captures, double ms, double us, bool measured)
        => Line("str.LF.Stack.Estimate", captures, ms, us,
                Line(measured ? "str.LF.Stack.EstimateMeasured" : "str.LF.Stack.EstimateAssumed"));

    /// <summary>The view model's stack line is the one en.axaml makes for this estimate. The line is read first, so its
    /// absence fails as that.</summary>
    private static void AssertStackLine(LiveFuncsViewModel vm, double captures, double ms, double us, bool measured)
    {
        string shown = StackLine(vm);
        Assert.Equal(EstimateLine(captures, ms, us, measured), shown);
    }

    /// <summary>One traced recording whose Stop reports <paramref name="captures"/> stacks taken in
    /// <paramref name="spentTicks"/> ticks of a 10 MHz clock, a tenth of a microsecond each.</summary>
    private static async Task RecordStacks(LiveFuncsViewModel vm, FakeDumpService dump, ulong captures, ulong spentTicks)
    {
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsRecording);
        Assert.NotNull(dump.LastTrace?.Snapshots?.Stacks);
        dump.StopTrace = new TraceInfo
        {
            Allocated = true, Quiesced = true, Gen = 1, Written = 40, QpcFreq = 10_000_000,
            Stack = new StackInfo { Rings = dump.LastTrace!.Snapshots!.Stacks!.Funcs.Count, Depth = 16, Captures = captures,
                                    SpentTicks = spentTicks, MaxTicks = spentTicks },
        };
        await vm.StopCommand.ExecuteAsync(null);
        Assert.False(vm.IsRecording);
    }

    [Fact]
    public async Task The_stack_line_weighs_the_choices_under_Standard_and_Low_and_follows_the_radio_and_the_choices()
    {
        var (vm, _) = await WithRatesToEstimate();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.Equal("", vm.StackEstimate);                  // nothing chosen, no line

        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "F"));
        string one = StackLine(vm);
        Assert.Contains(nameof(LiveFuncsViewModel.StackEstimate), raised);
        Assert.Contains(nameof(LiveFuncsViewModel.StackEstimateWarn), raised);
        Assert.Equal(EstimateLine(25, 0.25, 10.0, measured: false), one);   // 100 a second, held to Standard's 25
        Assert.Contains("25", one, StringComparison.Ordinal);
        Assert.Contains("0.25", one, StringComparison.Ordinal);
        Assert.Contains("10.0", one, StringComparison.Ordinal);

        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "G"));
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "H"));
        AssertStackLine(vm, 50, 0.5, 10.0, measured: false);   // 25 + 25 + 5, held to 50 in all

        raised.Clear();
        vm.StackBudgetLow = true;                            // the Low radio: 12 + 12 + 5, held to 25 in all
        string low = StackLine(vm);
        Assert.Contains(nameof(LiveFuncsViewModel.StackEstimate), raised);
        Assert.Contains(nameof(LiveFuncsViewModel.StackEstimateWarn), raised);
        Assert.Equal(EstimateLine(25, 0.25, 10.0, measured: false), low);

        raised.Clear();
        vm.StackBudgetStandard = true;
        Assert.Contains(nameof(LiveFuncsViewModel.StackEstimate), raised);
        AssertStackLine(vm, 50, 0.5, 10.0, measured: false);

        raised.Clear();
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "G"));      // dropped: 25 + 5
        Assert.Contains(nameof(LiveFuncsViewModel.StackEstimate), raised);
        AssertStackLine(vm, 30, 0.3, 10.0, measured: false);

        vm.ClearSnapshotsCommand.Execute(null);
        Assert.Equal("", vm.StackEstimate);
        Assert.False(vm.StackEstimateWarn);
    }

    [Fact]
    public async Task A_function_not_called_last_time_counts_nothing_and_the_line_says_so_when_none_was()
    {
        var (vm, dump) = await WithRatesToEstimate();
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "F"));
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "H"));
        AssertStackLine(vm, 30, 0.3, 10.0, measured: false);   // 25 + 5

        // A fetch that never saw A::H: the choice is kept by name, and counts nothing.
        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x1", KeyF, count: 1_000), Row("B", "Other", "0x9", new NameKey(9, 0, 9, 0)));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "A::F", "A::H" }, vm.StackFunctions);
        AssertStackLine(vm, 25, 0.25, 10.0, measured: false);

        // Neither called: no rate to weigh, and the line says so, with the most the budget takes.
        dump.NextGet = ResultOf(10_000, Row("B", "Other", "0x9", new NameKey(9, 0, 9, 0)));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "A::F", "A::H" }, vm.StackFunctions);
        string none = StackLine(vm);
        Assert.Equal(Line("str.LF.Stack.EstimateNone", LiveFuncsViewModel.StackTotalPerSec, LiveFuncsViewModel.StackPerFuncPerSec),
                     none);
        Assert.Contains("50", none, StringComparison.Ordinal);
        Assert.False(vm.StackEstimateWarn);
    }

    /// <summary>Review S3U7-HIDDEN-PERFRAME: Hide per-frame leaves a chosen per-frame function off the page, and the DLL
    /// names its address among the ones it left out. It ran every frame, so it takes its whole budget: counted as not
    /// called, the line said none was and stayed grey for the dearest choice there is.</summary>
    [Fact]
    public async Task A_chosen_per_frame_function_that_Hide_per_frame_left_out_takes_its_whole_budget()
    {
        var (vm, dump, _) = await WithPerFrameRows(answer: true);
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "Tick"));
        AssertStackLine(vm, 25, 0.25, 10.0, measured: false);   // 60 a second, held to 25

        vm.HidePerFrame = true;
        dump.NextGet = new PeProfileResult
        {
            DistinctFuncs = 3, TotalCalls = 1_210, WindowMs = 10_000, PerFrameHidden = 2, PerFrameFuncs = new[] { "0x1", "0x3" },
            Entries = new List<PeProfileEntry> { Row("A", "Open", "0x2", new NameKey(2, 0, 9, 0)) },
        };
        await Fetch(vm);
        Assert.Equal(new[] { "A::Tick" }, vm.StackFunctions);
        Assert.DoesNotContain(vm.Results, r => r.FuncName == "Tick");
        Assert.NotEqual(Line("str.LF.Stack.EstimateNone", LiveFuncsViewModel.StackTotalPerSec,
                             LiveFuncsViewModel.StackPerFuncPerSec), StackLine(vm));
        AssertStackLine(vm, 25, 0.25, 10.0, measured: false);

        // Beside a plain choice it adds its budget, not nothing: 25 + 1.
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "Open"));
        AssertStackLine(vm, 26, 0.26, 10.0, measured: false);

        // At 100 us a capture that is 2.6 ms of the game's time a second: orange, with the warning.
        await RecordStacks(vm, dump, captures: 100, spentTicks: 100_000);
        AssertStackLine(vm, 26, 2.6, 100.0, measured: true);
        Assert.True(vm.StackEstimateWarn);
    }

    /// <summary>Review S3U7-HIDDEN-PERFRAME: a page that did not show every function recorded cannot say a chosen
    /// function missing from it was not called -- the cut keeps the highest counts, and a rare one is what this panel
    /// is for. The line a binding reads when it is told of a change says the same.</summary>
    [Fact]
    public async Task A_page_that_did_not_show_every_function_never_calls_a_chosen_one_missing_from_it_not_called()
    {
        var (vm, dump) = await WithRatesToEstimate();
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "F"));
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "H"));
        var told = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LiveFuncsViewModel.StackEstimate)) told.Add(vm.StackEstimate);
        };
        string notCalled = Line("str.LF.Stack.EstimateNone", LiveFuncsViewModel.StackTotalPerSec,
                                LiveFuncsViewModel.StackPerFuncPerSec);

        // 40 functions recorded and one shown: both choices are below the cut.
        dump.NextGet = new PeProfileResult
        {
            DistinctFuncs = 40, TotalCalls = 9_000, WindowMs = 10_000,
            Entries = new List<PeProfileEntry> { Row("B", "Other", "0x9", new NameKey(9, 0, 9, 0), count: 5_000) },
        };
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "A::F", "A::H" }, vm.StackFunctions);
        string cut = StackLine(vm);
        Assert.NotEqual(notCalled, cut);
        Assert.NotEqual(notCalled, told.Last());
        string notShown = Line("str.LF.Stack.EstimateNotShown", LiveFuncsViewModel.StackTotalPerSec,
                               LiveFuncsViewModel.StackPerFuncPerSec);
        Assert.Equal(notShown, cut);
        Assert.Equal(notShown, told.Last());
        Assert.False(vm.StackEstimateWarn);

        // The same choices against a page that showed every function recorded: not called is then what they were.
        dump.NextGet = ResultOf(10_000, Row("B", "Other", "0x9", new NameKey(9, 0, 9, 0), count: 5_000));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(notCalled, StackLine(vm));
        Assert.Equal(notCalled, told.Last());
    }

    [Fact]
    public async Task A_capture_costs_what_the_last_Stop_measured_when_it_took_stacks_and_10_us_assumed_otherwise()
    {
        var (vm, dump) = await WithRatesToEstimate();
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "F"));
        AssertStackLine(vm, 25, 0.25, 10.0, measured: false);

        // 28,000 ticks over 100 captures at 10 MHz: 28 us a capture; 25 a second of them is 0.7 ms a second.
        await RecordStacks(vm, dump, captures: 100, spentTicks: 28_000);
        AssertStackLine(vm, 25, 0.7, 28.0, measured: true);
        await vm.StartCommand.ExecuteAsync(null);                         // recording: still the last Stop's
        AssertStackLine(vm, 25, 0.7, 28.0, measured: true);
        await vm.StopCommand.ExecuteAsync(null);

        await RecordStacks(vm, dump, captures: 0, spentTicks: 0);          // armed, but none taken: nothing measured
        AssertStackLine(vm, 25, 0.25, 10.0, measured: false);

        await RecordStacks(vm, dump, captures: 100, spentTicks: 28_000);
        vm.TraceEnabled = false;                                           // a plain recording takes no stacks
        await Fetch(vm);
        AssertStackLine(vm, 25, 0.25, 10.0, measured: false);

        vm.TraceEnabled = true;
        await RecordStacks(vm, dump, captures: 100, spentTicks: 28_000);
        AssertStackLine(vm, 25, 0.7, 28.0, measured: true);
        vm.ResetOnDisconnect();                                            // the next connection may be another game
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "F"));
        AssertStackLine(vm, 25, 0.25, 10.0, measured: false);
    }

    [Fact]
    public async Task The_line_turns_orange_only_above_2_ms_of_the_games_time_a_second()
    {
        var (vm, dump) = await WithRatesToEstimate();
        foreach (var f in new[] { "F", "G", "H" }) await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", f));
        AssertStackLine(vm, 50, 0.5, 10.0, measured: false);
        Assert.False(vm.StackEstimateWarn);

        // 40 us a capture: 50 a second is exactly 2.0 ms, which is not above it.
        await RecordStacks(vm, dump, captures: 100, spentTicks: 40_000);
        AssertStackLine(vm, 50, 2.0, 40.0, measured: true);
        Assert.False(vm.StackEstimateWarn);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        await RecordStacks(vm, dump, captures: 100, spentTicks: 50_000);   // 50 us: 2.5 ms
        Assert.Contains(nameof(LiveFuncsViewModel.StackEstimateWarn), raised);
        AssertStackLine(vm, 50, 2.5, 50.0, measured: true);
        Assert.True(vm.StackEstimateWarn);

        vm.StackBudgetLow = true;                                          // 25 a second: 1.25 ms
        Assert.False(vm.StackEstimateWarn);
        vm.StackBudgetStandard = true;
        Assert.True(vm.StackEstimateWarn);
        vm.ClearSnapshotsCommand.Execute(null);
        Assert.False(vm.StackEstimateWarn);
    }

    [Fact]
    public void The_estimate_sits_under_the_stack_budget_and_it_and_the_warning_turn_orange_together()
    {
        var axaml = File.ReadAllText(Path.Combine(RepoRoot(), "ui", "UE5DumpUI", "Views", "LiveFuncsPanel.axaml"));
        var estimate = Regex.Match(axaml, @"<TextBlock Text=""\{Binding SnapshotEstimate\}"" IsVisible=""\{Binding SnapshotEstimateWarn\}""\s+Foreground=""(?<c>[^""]+)""");
        Assert.True(estimate.Success, "the parameter estimate's orange line moved");
        string orange = estimate.Groups["c"].Value;
        var note = Regex.Match(axaml, @"<TextBlock Text=""\{Binding SnapshotBudgetNote\}""[^>]*Foreground=""(?<c>[^""]+)""");
        Assert.True(note.Success, "the panel's grey note moved");
        string grey = note.Groups["c"].Value;

        // Each line twice, one TextBlock a colour, toggled by the estimate's bool: the D3 pattern.
        var blocks = Regex.Matches(axaml, @"<TextBlock\b[^>]*/>").Select(m => m.Value).ToList();
        foreach (var (text, calmColour) in new[] { ("{Binding StackEstimate}", (string?)null),
                                                   ("{StaticResource str.LF.Stack.Warning}", grey) })
        {
            var pair = blocks.Where(b => b.Contains($"Text=\"{text}\"", StringComparison.Ordinal)).ToList();
            Assert.True(pair.Count == 2, $"{text} is not shown by two TextBlocks");
            string hot = Assert.Single(pair, b => b.Contains("IsVisible=\"{Binding StackEstimateWarn}\"", StringComparison.Ordinal));
            string calm = Assert.Single(pair, b => b.Contains("IsVisible=\"{Binding !StackEstimateWarn}\"", StringComparison.Ordinal));
            Assert.Contains($"Foreground=\"{orange}\"", hot, StringComparison.Ordinal);
            Assert.DoesNotContain($"Foreground=\"{orange}\"", calm, StringComparison.Ordinal);
            if (calmColour != null) Assert.Contains($"Foreground=\"{calmColour}\"", calm, StringComparison.Ordinal);
            foreach (var b in pair) Assert.Contains("TextWrapping=\"Wrap\"", b, StringComparison.Ordinal);
        }
        foreach (var b in blocks.Where(b => b.Contains("Text=\"{Binding StackEstimate}\"", StringComparison.Ordinal)))
            Assert.Contains("ToolTip.Tip=\"{Binding StackEstimateTip}\"", b, StringComparison.Ordinal);

        // Under the choices row that holds the stack count and the budget radios, above the parameters' estimate, and
        // shown only with a stack chosen and the trace available.
        int count = axaml.IndexOf("{Binding StackCountText}", StringComparison.Ordinal);
        int rowEnd = axaml.IndexOf("</StackPanel>", count, StringComparison.Ordinal);
        int first = axaml.IndexOf("Text=\"{Binding StackEstimate}\"", StringComparison.Ordinal);
        int warning = axaml.IndexOf("Text=\"{StaticResource str.LF.Stack.Warning}\"", StringComparison.Ordinal);
        int parameters = axaml.IndexOf("Text=\"{Binding SnapshotEstimate}\"", StringComparison.Ordinal);
        Assert.True(count >= 0 && rowEnd > count && first > rowEnd && warning > first && parameters > warning,
                    "the stack estimate is not between the choices row and the parameters' estimate, before the warning");
        string head = axaml[rowEnd..first];
        Assert.Contains("IsVisible=\"{Binding TraceAvailable}\"", head, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding HasStackChoices}\"", head, StringComparison.Ordinal);
        // Not inside the parameters' estimate row, which shows for a parameter choice alone too.
        Assert.DoesNotContain("BoolConverters.Or", head, StringComparison.Ordinal);

        // Its tooltip says the threshold and the assumed cost the view model uses.
        var (vm, _) = MakeVm();
        Assert.Equal(Line("str.Tip.LF.Stack.Estimate", 2.0, 10.0), vm.StackEstimateTip);
        Assert.Contains("2.0", vm.StackEstimateTip, StringComparison.Ordinal);
        Assert.Contains("10", vm.StackEstimateTip, StringComparison.Ordinal);
    }

    // ---- [LF-COMPACT-TOP] the controls above the table, made smaller ----

    /// <summary>(1) The warning shows one line of its essentials and keeps the whole text one click away: the four-line
    /// text was the biggest block above the table. The one line is still the warning, so it keeps the D3 pair (grey,
    /// orange with the estimate) and wraps rather than clip in a narrow window; the whole text keeps every point the
    /// maintainer asked the step-3 warning to make, and shows only while its Details toggle is down.</summary>
    [Fact]
    public void The_stack_warning_is_one_line_of_essentials_and_Details_shows_the_whole_text()
    {
        string shortLine = Line("str.LF.Stack.WarningShort");
        foreach (var essential in new[] { "soft capture", "game's own thread", "frame", "save first", "few functions" })
            Assert.Contains(essential, shortLine, StringComparison.OrdinalIgnoreCase);
        string whole = Line("str.LF.Stack.Warning");
        foreach (var point in new[] { "not a debugger capture like Cheat Engine's", "in software", "added to the game's frame",
                                      "stopping mid-capture", "stall or crash the game", "save first", "few functions" })
            Assert.Contains(point, whole, StringComparison.Ordinal);
        Assert.True(shortLine.Length * 4 < whole.Length, $"the one line is {shortLine.Length} characters of {whole.Length}");
        Assert.True(Line("str.LF.Stack.WarningDetails").Length > 0);
        Assert.True(Line("str.Tip.LF.Stack.WarningDetails").Length > 0);

        var axaml = File.ReadAllText(Path.Combine(RepoRoot(), "ui", "UE5DumpUI", "Views", "LiveFuncsPanel.axaml"));
        var blocks = Regex.Matches(axaml, @"<TextBlock\b[^>]*/>").Select(m => m.Value).ToList();
        var pair = blocks.Where(b => b.Contains("Text=\"{StaticResource str.LF.Stack.WarningShort}\"", StringComparison.Ordinal))
                         .ToList();
        Assert.True(pair.Count == 2, "the one line is not shown by two TextBlocks");
        var full = blocks.Single(b => b.Contains("Text=\"{StaticResource str.LF.Stack.Warning}\"", StringComparison.Ordinal)
                                      && b.Contains("IsVisible=\"{Binding StackEstimateWarn}\"", StringComparison.Ordinal));
        string orange = Regex.Match(full, @"Foreground=""(?<c>[^""]+)""").Groups["c"].Value;
        string hot = Assert.Single(pair, b => b.Contains("IsVisible=\"{Binding StackEstimateWarn}\"", StringComparison.Ordinal));
        string calm = Assert.Single(pair, b => b.Contains("IsVisible=\"{Binding !StackEstimateWarn}\"", StringComparison.Ordinal));
        Assert.Contains($"Foreground=\"{orange}\"", hot, StringComparison.Ordinal);
        Assert.DoesNotContain($"Foreground=\"{orange}\"", calm, StringComparison.Ordinal);
        foreach (var b in pair) Assert.Contains("TextWrapping=\"Wrap\"", b, StringComparison.Ordinal);

        // The toggle, by its name, and the whole text inside the one panel that follows it.
        var toggle = Regex.Matches(axaml, @"<ToggleButton\b[^>]*/>").Select(m => m.Value)
                          .Single(t => t.Contains("Content=\"{StaticResource str.LF.Stack.WarningDetails}\"", StringComparison.Ordinal));
        Assert.Contains("ToolTip.Tip=\"{StaticResource str.Tip.LF.Stack.WarningDetails}\"", toggle, StringComparison.Ordinal);
        string name = Regex.Match(toggle, @"x:Name=""(?<n>\w+)""").Groups["n"].Value;
        Assert.True(name.Length > 0, "the Details toggle has no name to bind to");
        int shortAt = axaml.IndexOf("Text=\"{StaticResource str.LF.Stack.WarningShort}\"", StringComparison.Ordinal);
        int toggleAt = axaml.IndexOf(toggle, StringComparison.Ordinal);
        int wholeAt = axaml.IndexOf("Text=\"{StaticResource str.LF.Stack.Warning}\"", StringComparison.Ordinal);
        Assert.True(shortAt < toggleAt && toggleAt < wholeAt, "the one line, its toggle and the whole text are out of order");
        var opens = Regex.Matches(axaml[..wholeAt], @"<Panel\b[^>]*>");
        Assert.Contains($"IsVisible=\"{{Binding #{name}.IsChecked}}\"", opens[^1].Value, StringComparison.Ordinal);
    }

    /// <summary>(3) The baseline's line speaks only when there is a baseline, or Diff is on and there is none: it said "No
    /// baseline" to every user who never asked for one, a line of the panel spent on a hint. Each way it can change is
    /// raised, the last one when Diff was already off and nothing else would be.</summary>
    [Fact]
    public async Task The_baseline_line_shows_only_with_a_baseline_or_with_Diff_on()
    {
        var (vm, dump) = MakeVm();
        var raised = Raised(vm);
        Assert.False(vm.BaselineStatusVisible);

        vm.DiffMode = true;                                   // Diff without a baseline: the line says there is none
        Assert.True(vm.BaselineStatusVisible);
        Assert.Contains(nameof(LiveFuncsViewModel.BaselineStatusVisible), raised);
        vm.DiffMode = false;
        Assert.False(vm.BaselineStatusVisible);

        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x1", KeyF));
        await Fetch(vm);
        raised.Clear();
        vm.SetBaselineCommand.Execute(null);
        Assert.True(vm.BaselineStatusVisible);
        Assert.Contains(nameof(LiveFuncsViewModel.BaselineStatusVisible), raised);
        vm.DiffMode = false;                                  // a baseline kept with Diff off is still worth its line
        Assert.True(vm.BaselineStatusVisible);

        raised.Clear();
        vm.ClearBaselineCommand.Execute(null);
        Assert.False(vm.BaselineStatusVisible);
        Assert.Contains(nameof(LiveFuncsViewModel.BaselineStatusVisible), raised);
    }

    /// <summary>(3) The hint the line gave moves into Set Baseline's tooltip, and the line binds its visibility.</summary>
    [Fact]
    public void The_no_baseline_hint_is_in_Set_Baselines_tooltip_and_the_line_binds_its_visibility()
    {
        Assert.Contains("record idle, then Set Baseline", Line("str.Tip.LF.SetBaseline"), StringComparison.Ordinal);
        var axaml = File.ReadAllText(Path.Combine(RepoRoot(), "ui", "UE5DumpUI", "Views", "LiveFuncsPanel.axaml"));
        var line = Regex.Matches(axaml, @"<TextBlock\b[^>]*/>").Select(m => m.Value)
                        .Single(b => b.Contains("Text=\"{Binding BaselineStatus}\"", StringComparison.Ordinal));
        Assert.Contains("IsVisible=\"{Binding BaselineStatusVisible}\"", line, StringComparison.Ordinal);
        var button = Regex.Matches(axaml, @"<Button\b[^>]*/>").Select(m => m.Value)
                          .Single(b => b.Contains("Command=\"{Binding SetBaselineCommand}\"", StringComparison.Ordinal));
        Assert.Contains("ToolTip.Tip=\"{StaticResource str.Tip.LF.SetBaseline}\"", button, StringComparison.Ordinal);
    }

    /// <summary>(2) The capture settings start unfolded, so a user who never folds them sees what they always saw, and the
    /// header's button folds and unfolds them, saying which it will do. Folding is a view choice that changes no setting,
    /// so it is not refused while recording.</summary>
    [Fact]
    public async Task The_capture_settings_start_unfolded_and_the_header_button_folds_and_unfolds_them()
    {
        var (vm, _) = MakeVm();
        var raised = Raised(vm);
        Assert.False(vm.CaptureSettingsCollapsed);
        Assert.Equal(Line("str.LF.Settings.Collapse"), vm.CaptureSettingsToggleText);

        vm.ToggleCaptureSettingsCommand.Execute(null);
        Assert.True(vm.CaptureSettingsCollapsed);
        Assert.Equal(Line("str.LF.Settings.Expand"), vm.CaptureSettingsToggleText);
        Assert.Contains(nameof(LiveFuncsViewModel.CaptureSettingsCollapsed), raised);
        Assert.Contains(nameof(LiveFuncsViewModel.CaptureSettingsToggleText), raised);
        Assert.NotEqual(vm.CaptureSettingsToggleText, Line("str.LF.Settings.Collapse"));

        vm.ToggleCaptureSettingsCommand.Execute(null);
        Assert.False(vm.CaptureSettingsCollapsed);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsRecording);
        vm.ToggleCaptureSettingsCommand.Execute(null);
        Assert.True(vm.CaptureSettingsCollapsed);
    }

    private static string Summary(params string[] parts) => string.Join(" · ", parts);

    /// <summary>(2) Folded, the header says what is set: the plain capture's settings always, and with the experimental
    /// trace its switch and buffer, the three choice counts, the stack budget once a stack is chosen, the snapshot buffer
    /// once anything fills it, and the stack warning's one line, which folding never hides.</summary>
    [Fact]
    public async Task The_summary_says_what_is_set()
    {
        var (plain, _) = MakeVm(experimental: false);
        Assert.Equal(Line("str.LF.Summary.Fetch", 512, 1), plain.CaptureSummary);
        plain.FetchLimitExponent = 10;
        plain.MinCallsExponent = 2;
        plain.HidePerFrame = true;
        Assert.Equal(Summary(Line("str.LF.Summary.Fetch", 1024, 4), Line("str.LF.Summary.HidePerFrame")), plain.CaptureSummary);
        plain.TraceEnabled = true;                               // no trace without the experimental tabs: nothing to say
        Assert.Equal(Summary(Line("str.LF.Summary.Fetch", 1024, 4), Line("str.LF.Summary.HidePerFrame")), plain.CaptureSummary);

        var (vm, dump) = MakeVm();
        Assert.Equal(Summary(Line("str.LF.Summary.Fetch", 512, 1), Line("str.LF.Summary.TraceOff"),
                             Line("str.LF.Summary.Choices", 0, 0, 0)), vm.CaptureSummary);
        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x1", KeyF), Row("A", "G", "0x2", KeyG));
        await Fetch(vm);
        vm.TraceEnabled = true;
        vm.TraceBufferExponent = 5;
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "F"));
        Assert.Equal(Summary(Line("str.LF.Summary.Fetch", 512, 1), Line("str.LF.Summary.TraceOn", 32),
                             Line("str.LF.Summary.Choices", 0, 0, 1),
                             Line("str.LF.Summary.StackBudget", Line("str.LF.Stack.Standard")),
                             Line("str.LF.Summary.SnapBuffer", 32), Line("str.LF.Stack.WarningShort")), vm.CaptureSummary);
        Assert.False(vm.CaptureSummaryWarn);

        // A 128 MB snapshot buffer keeps G's one call a second longer than the 32 MB trace keeps the chosen calls, so
        // nothing here warns and the summary is the settings alone.
        vm.StackBudgetLow = true;
        vm.SnapshotBufferExponent = 7;
        vm.ToggleTickCommand.Execute(Shown(vm, "A", "G"));
        vm.ToggleSnapshotCommand.Execute(Shown(vm, "A", "G"));
        vm.TraceEnabled = false;
        Assert.False(vm.SnapshotEstimateWarn);
        Assert.Equal(Summary(Line("str.LF.Summary.Fetch", 512, 1), Line("str.LF.Summary.TraceOff"),
                             Line("str.LF.Summary.Choices", 1, 1, 1),
                             Line("str.LF.Summary.StackBudget", Line("str.LF.Stack.Low")),
                             Line("str.LF.Summary.SnapBuffer", 128), Line("str.LF.Stack.WarningShort")), vm.CaptureSummary);
        Assert.False(vm.CaptureSummaryWarn);
    }

    /// <summary>(2) Folding never hides a warning: each orange line the section can show puts its own short warning in
    /// the summary and turns it orange -- the stack estimate over 2 ms a second, the parameter estimate (a choice that
    /// keeps less time than the trace, or a buffer the DLL refuses), and memory over what is free.</summary>
    [Fact]
    public async Task Every_orange_line_of_the_section_turns_the_summary_orange_with_its_warning()
    {
        var (stacks, stacksDump) = await WithRatesToEstimate();
        await stacks.ToggleStackCommand.ExecuteAsync(Shown(stacks, "A", "F"));
        Assert.False(stacks.CaptureSummaryWarn);
        await RecordStacks(stacks, stacksDump, captures: 100, spentTicks: 100_000);   // 100 us: 25 a second is 2.5 ms
        Assert.True(stacks.StackEstimateWarn);
        Assert.True(stacks.CaptureSummaryWarn);
        Assert.Contains(Line("str.LF.Summary.StackWarn", 2.5), stacks.CaptureSummary, StringComparison.Ordinal);

        var (busy, busyDump) = MakeVm();
        busyDump.NextGet = ResultOf(10_000, Row("A", "Hot", "0x1", KeyF, count: 100_000, size: 2048));
        await Fetch(busy);
        busy.TraceEnabled = true;
        busy.SnapshotBufferExponent = 3;
        busy.ToggleSnapshotCommand.Execute(busy.Results.Single());
        Assert.True(busy.SnapshotEstimateWarn);
        Assert.True(busy.CaptureSummaryWarn);
        Assert.Contains(Line("str.LF.Summary.SnapWarn"), busy.CaptureSummary, StringComparison.Ordinal);
        Assert.DoesNotContain(Line("str.LF.Summary.SnapTooSmall"), busy.CaptureSummary, StringComparison.Ordinal);

        // 510 functions of a 2 KB block in 8 MB: fewer than 8 slots a ring, so the DLL would refuse the Start.
        var (many, manyDump) = MakeVm();
        manyDump.NextGet = ResultOf(10_000, Enumerable.Range(1, 510)
            .Select(i => Row("A", $"F{i}", $"0x{i:X}", new NameKey(i, 0, 9, 0), size: 2048)).ToArray());
        await Fetch(many);
        many.TraceEnabled = true;
        many.SnapshotBufferExponent = 3;
        many.SnapshotShownRowsCommand.Execute(null);
        Assert.Contains(Line("str.LF.Snap.TooSmall", 7).Split(':')[0], many.SnapshotEstimate, StringComparison.Ordinal);
        Assert.True(many.CaptureSummaryWarn);
        Assert.Contains(Line("str.LF.Summary.SnapTooSmall"), many.CaptureSummary, StringComparison.Ordinal);

        var platform = new MockPlatformService(Path.GetTempPath()) { AvailablePhysicalMemory = 1L << 30 };   // 1 GB
        var (memory, _) = MakeVm(platform: platform);
        memory.TraceEnabled = true;
        Assert.False(memory.CaptureSummaryWarn);
        memory.TraceBufferExponent = 9;                          // 512 MB in the game and about 1.2 GB in the UI
        Assert.True(memory.TraceMemoryOverAvailable);
        Assert.True(memory.CaptureSummaryWarn);
        Assert.Contains(Line("str.LF.Summary.MemoryOver", Line("str.LF.Trace.Gb", 1.0)), memory.CaptureSummary,
                        StringComparison.Ordinal);

        // Without the experimental tabs the section shows none of these lines, so the summary has none to carry.
        var (plain, _) = MakeVm(experimental: false, platform: platform);
        plain.TraceBufferExponent = 9;
        Assert.True(plain.TraceMemoryOverAvailable);
        Assert.False(plain.CaptureSummaryWarn);
        Assert.DoesNotContain("⚠", plain.CaptureSummary, StringComparison.Ordinal);
    }

    /// <summary>(2) A summary the screen keeps showing after a setting moved would be wrong while folded, which is the only
    /// time it is read: so every change it names raises it, and its orange flag with it.</summary>
    [Fact]
    public async Task The_summary_is_raised_by_every_change_it_names()
    {
        var (vm, dump) = await WithRatesToEstimate();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        void Raises(string what, Action change)
        {
            raised.Clear();
            change();
            Assert.True(raised.Contains(nameof(LiveFuncsViewModel.CaptureSummary)), $"{what} does not raise the summary");
            Assert.True(raised.Contains(nameof(LiveFuncsViewModel.CaptureSummaryWarn)), $"{what} does not raise its flag");
        }
        Raises("the fetch limit", () => vm.FetchLimitExponent = 12);
        Raises("min calls", () => vm.MinCallsExponent = 3);
        Raises("hide per-frame", () => vm.HidePerFrame = true);
        Raises("the trace's switch", () => vm.TraceEnabled = false);
        Raises("the trace's buffer", () => vm.TraceBufferExponent = 8);
        Raises("a tick", () => vm.ToggleTickCommand.Execute(Shown(vm, "A", "F")));
        vm.TraceEnabled = true;
        Raises("a parameter choice", () => vm.ToggleSnapshotCommand.Execute(Shown(vm, "A", "G")));
        Raises("a stack choice", () => vm.ToggleStackCommand.Execute(Shown(vm, "A", "H")));
        Raises("the stack budget", () => vm.StackBudgetLow = true);
        Raises("the snapshot buffer", () => vm.SnapshotBufferExponent = 6);
        raised.Clear();
        await RecordStacks(vm, dump, captures: 100, spentTicks: 100_000);
        Assert.Contains(nameof(LiveFuncsViewModel.CaptureSummaryWarn), raised);
        Raises("the free memory read again", () => vm.OnEnteringTab());
    }

    /// <summary>(2) The section's fold, its summary and the header's buttons, as the view binds them: compiled bindings
    /// only, the summary the D3 pair (calm, and the panel's orange when it warns), wrapping so a warning is never cut.</summary>
    [Fact]
    public void The_header_binds_the_fold_and_the_summary_and_the_section_folds_under_it()
    {
        var axaml = File.ReadAllText(Path.Combine(RepoRoot(), "ui", "UE5DumpUI", "Views", "LiveFuncsPanel.axaml"));
        var buttons = Regex.Matches(axaml, @"<Button\b[^>]*/>").Select(m => m.Value).ToList();
        string toggle = Assert.Single(buttons, b => b.Contains("Command=\"{Binding ToggleCaptureSettingsCommand}\"", StringComparison.Ordinal));
        Assert.Contains("Content=\"{Binding CaptureSettingsToggleText}\"", toggle, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{StaticResource str.Tip.LF.Settings.Toggle}\"", toggle, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled", toggle, StringComparison.Ordinal);   // folding is not refused while recording
        Assert.True(Line("str.Tip.LF.Settings.Toggle").Length > 0);

        var blocks = Regex.Matches(axaml, @"<TextBlock\b[^>]*/>").Select(m => m.Value).ToList();
        var pair = blocks.Where(b => b.Contains("Text=\"{Binding CaptureSummary}\"", StringComparison.Ordinal)).ToList();
        Assert.True(pair.Count == 2, "the summary is not shown by two TextBlocks");
        string orange = Regex.Match(blocks.Single(b => b.Contains("IsVisible=\"{Binding TraceMemoryOverAvailable}\"",
                                                                  StringComparison.Ordinal)),
                                    @"Foreground=""(?<c>[^""]+)""").Groups["c"].Value;
        string hot = Assert.Single(pair, b => b.Contains("IsVisible=\"{Binding CaptureSummaryWarn}\"", StringComparison.Ordinal));
        string calm = Assert.Single(pair, b => b.Contains("IsVisible=\"{Binding !CaptureSummaryWarn}\"", StringComparison.Ordinal));
        Assert.Contains($"Foreground=\"{orange}\"", hot, StringComparison.Ordinal);
        Assert.DoesNotContain($"Foreground=\"{orange}\"", calm, StringComparison.Ordinal);
        foreach (var b in pair) Assert.Contains("TextWrapping=\"Wrap\"", b, StringComparison.Ordinal);

        // The summary shows folded, the section's own rows unfolded; one binding each way.
        Assert.Contains("IsVisible=\"{Binding CaptureSettingsCollapsed}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding !CaptureSettingsCollapsed}\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fold_is_remembered_through_the_main_window()
    {
        // MainWindowViewModel cannot be built in a unit test; pin its three persistence sites by source, as
        // LiveFuncsViewModelTests.HidePerFrame_PersistsThroughTheMainWindow does.
        var main = File.ReadAllText(Path.Combine(RepoRoot(), "ui", "UE5DumpUI", "ViewModels", "MainWindowViewModel.cs"));
        Assert.Contains("nameof(LiveFuncsViewModel.CaptureSettingsCollapsed)", main, StringComparison.Ordinal);
        Assert.Contains("LiveFuncs.CaptureSettingsCollapsed = o.LiveFuncs.CaptureSettingsCollapsed;", main, StringComparison.Ordinal);
        Assert.Contains("o.LiveFuncs.CaptureSettingsCollapsed = LiveFuncs.CaptureSettingsCollapsed;", main, StringComparison.Ordinal);
        // Unfolded by default on both sides, so a file from before the option shows every setting.
        Assert.False(new UiOptionsSettings().LiveFuncs.CaptureSettingsCollapsed);
    }

    // ---- Clear choices: one clear for the three choice columns ----

    /// <summary>A view model with every kind of choice, Trace on: A::F ticked and chosen for parameters and for a stack,
    /// A::G for parameters, A::H for a stack. F carries all three, so a clear that misses one kind leaves a box ticked
    /// on a row whose other boxes it cleared.</summary>
    private static async Task<(LiveFuncsViewModel vm, FakeDumpService dump)> WithEveryKindOfChoice()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x1", KeyF), Row("A", "G", "0x2", KeyG), Row("A", "H", "0x3", KeyH));
        await Fetch(vm);
        vm.TraceEnabled = true;
        var f = Shown(vm, "A", "F");
        vm.ToggleTickCommand.Execute(f);
        vm.ToggleSnapshotCommand.Execute(f);
        await vm.ToggleStackCommand.ExecuteAsync(f);
        vm.ToggleSnapshotCommand.Execute(Shown(vm, "A", "G"));
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "H"));
        Assert.Equal(new[] { "A::F" }, vm.TickedFunctions);
        Assert.Equal(new[] { "A::F", "A::G" }, vm.SnapshotFunctions.Order());
        Assert.Equal(new[] { "A::F", "A::H" }, vm.StackFunctions.Order());
        return (vm, dump);
    }

    [Fact]
    public async Task Clear_choices_unticks_all_three_columns_and_every_count_and_estimate_follows()
    {
        var (vm, _) = await WithEveryKindOfChoice();
        Assert.True(vm.TraceGameMb > vm.TraceBufferMb, "the choices do not hold the snapshot buffer");

        vm.ClearChoicesCommand.Execute(null);

        Assert.Empty(vm.TickedFunctions);
        Assert.Empty(vm.SnapshotFunctions);
        Assert.Empty(vm.StackFunctions);
        foreach (var r in vm.Results)
        {
            Assert.False(r.IsTicked, $"{r.FuncName} is still ticked for the trace");
            Assert.False(r.IsSnapChosen, $"{r.FuncName} is still chosen for parameters");
            Assert.False(r.IsStackChosen, $"{r.FuncName} is still chosen for a stack");
        }
        Assert.False(vm.HasTickedFunctions);
        Assert.False(vm.HasSnapshotChoices);
        Assert.False(vm.HasStackChoices);
        Assert.Equal(Line("str.LF.Trace.TickedCount", 0), vm.TickedCountText);
        Assert.Equal(Line("str.LF.Snap.Count", 0), vm.SnapshotCountText);
        Assert.Equal(Line("str.LF.Stack.Count", 0), vm.StackCountText);
        Assert.Equal(vm.TraceBufferMb, vm.TraceGameMb);   // nothing left to fill the snapshot buffer
        Assert.Equal("", vm.SnapshotEstimate);
        Assert.Equal("", vm.StackEstimate);
        Assert.False(vm.StackEstimateWarn);
    }

    /// <summary>A count, a "has" flag or an estimate the clear changes but does not raise keeps its old figure on
    /// screen beside an empty column, and the values above cannot show that: so whatever the two clears raise between
    /// them, Clear choices raises too.</summary>
    [Fact]
    public async Task Clear_choices_raises_every_property_the_two_clears_raise()
    {
        var (separate, _) = await WithEveryKindOfChoice();
        var bySeparate = Raised(separate);
        separate.ClearTicksCommand.Execute(null);
        separate.ClearSnapshotsCommand.Execute(null);

        var (together, _) = await WithEveryKindOfChoice();
        var byTogether = Raised(together);
        together.ClearChoicesCommand.Execute(null);

        Assert.Contains(nameof(LiveFuncsViewModel.TickedCountText), bySeparate);
        Assert.Contains(nameof(LiveFuncsViewModel.StackCountText), bySeparate);
        Assert.Empty(bySeparate.Except(byTogether).Order());
    }

    private static HashSet<string> Raised(LiveFuncsViewModel vm)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        vm.PropertyChanged += (_, e) => { if (e.PropertyName != null) names.Add(e.PropertyName); };
        return names;
    }

    [Fact]
    public async Task Clear_choices_holds_still_while_recording()
    {
        var (vm, _) = await WithEveryKindOfChoice();
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsRecording);
        vm.ClearChoicesCommand.Execute(null);      // the button is disabled; a call that arrives anyway is refused
        Assert.Equal(new[] { "A::F" }, vm.TickedFunctions);
        Assert.Equal(2, vm.SnapshotFunctions.Count);
        Assert.Equal(2, vm.StackFunctions.Count);
        Assert.True(Shown(vm, "A", "F") is { IsTicked: true, IsSnapChosen: true, IsStackChosen: true });

        await vm.StopCommand.ExecuteAsync(null);
        vm.ClearChoicesCommand.Execute(null);
        Assert.Empty(vm.TickedFunctions);
        Assert.Empty(vm.SnapshotFunctions);
        Assert.Empty(vm.StackFunctions);
    }

    /// <summary>A guard: the two clears it joins keep their own scopes. Clear ticks drops the ticks alone; the
    /// Parameters row's Clear drops the parameter and the stack choices, which fill the same buffer (D4).</summary>
    [Fact]
    public async Task Clear_ticks_and_the_Parameters_Clear_keep_their_own_scopes()
    {
        var (vm, _) = await WithEveryKindOfChoice();
        vm.ClearTicksCommand.Execute(null);
        Assert.Empty(vm.TickedFunctions);
        Assert.Equal(2, vm.SnapshotFunctions.Count);
        Assert.Equal(2, vm.StackFunctions.Count);

        (vm, _) = await WithEveryKindOfChoice();
        vm.ClearSnapshotsCommand.Execute(null);
        Assert.Equal(new[] { "A::F" }, vm.TickedFunctions);
        Assert.Empty(vm.SnapshotFunctions);
        Assert.Empty(vm.StackFunctions);
    }

    /// <summary>What a layout cannot read: the button runs the command, is disabled while recording like the two clears
    /// it joins, and shows only with the experimental trace, as the three columns do. Its tooltip names the columns,
    /// and the two clears stay in their rows.</summary>
    [Fact]
    public void The_Clear_choices_button_runs_the_command_holds_still_while_recording_and_shows_with_the_columns()
    {
        var axaml = File.ReadAllText(Path.Combine(RepoRoot(), "ui", "UE5DumpUI", "Views", "LiveFuncsPanel.axaml"));
        var buttons = Regex.Matches(axaml, @"<Button\b[^>]*/>").Select(m => m.Value).ToList();
        string clear = Assert.Single(buttons, b => b.Contains("Command=\"{Binding ClearChoicesCommand}\"", StringComparison.Ordinal));
        Assert.Contains("Content=\"{StaticResource str.LF.ClearChoices}\"", clear, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{StaticResource str.Tip.LF.ClearChoices}\"", clear, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding !IsRecording}\"", clear, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding TraceAvailable}\"", clear, StringComparison.Ordinal);
        Assert.True(Line("str.LF.ClearChoices").Length > 0);
        string tip = Line("str.Tip.LF.ClearChoices");
        foreach (var column in new[] { "str.LF.Col.Trace", "str.LF.Col.Snapshot", "str.LF.Col.Stack" })
            Assert.Contains(Line(column), tip, StringComparison.Ordinal);

        Assert.Contains("Command=\"{Binding ClearTicksCommand}\"", EnclosingStackPanel(axaml, "{Binding TickedCountText}"),
                        StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ClearSnapshotsCommand}\"", EnclosingStackPanel(axaml, "{Binding SnapshotCountText}"),
                        StringComparison.Ordinal);
    }

    /// <summary>The user chose, then narrowed the table and switched the trace off: the filter hides A::G (chosen for
    /// parameters) and A::H (chosen for a stack), and Trace off makes the rows unchoosable. Clear choices still clears
    /// every row the table holds, not the rows on screen, and does not wait for the trace to be on: a choice made while
    /// it was on is still a choice, and the next traced Start would take it.</summary>
    [Fact]
    public async Task Clear_choices_reaches_the_rows_the_filter_hides_and_works_with_the_trace_off()
    {
        var (vm, _) = await WithEveryKindOfChoice();
        vm.FilterText = "F";
        Assert.Equal(new[] { "F" }, vm.Results.Select(r => r.FuncName));
        vm.TraceEnabled = false;
        Assert.False(vm.CanSnapshot);

        vm.ClearChoicesCommand.Execute(null);
        vm.FilterText = "";

        Assert.Equal(new[] { "F", "G", "H" }, vm.Results.Select(r => r.FuncName).Order());
        foreach (var r in vm.Results)
        {
            Assert.False(r.IsTicked, $"{r.FuncName} is still ticked for the trace");
            Assert.False(r.IsSnapChosen, $"{r.FuncName} is still chosen for parameters");
            Assert.False(r.IsStackChosen, $"{r.FuncName} is still chosen for a stack");
        }
        Assert.Empty(vm.TickedFunctions);
        Assert.Empty(vm.SnapshotFunctions);
        Assert.Empty(vm.StackFunctions);
    }

    /// <summary>Stacks are the only choice: nothing ticked and no parameters, which a "nothing to clear" shortcut that
    /// asks only the first two would take for an empty table.</summary>
    [Fact]
    public async Task Clear_choices_clears_a_stack_chosen_alone()
    {
        var (vm, dump) = MakeVm();
        dump.NextGet = ResultOf(10_000, Row("A", "F", "0x1", KeyF), Row("A", "G", "0x2", KeyG));
        await Fetch(vm);
        vm.TraceEnabled = true;
        await vm.ToggleStackCommand.ExecuteAsync(Shown(vm, "A", "F"));
        Assert.Empty(vm.TickedFunctions);
        Assert.Empty(vm.SnapshotFunctions);
        Assert.Equal(new[] { "A::F" }, vm.StackFunctions);

        vm.ClearChoicesCommand.Execute(null);

        Assert.Empty(vm.StackFunctions);
        Assert.False(vm.HasStackChoices);
        Assert.False(Shown(vm, "A", "F").IsStackChosen);
        Assert.Equal("", vm.StackEstimate);
    }

    /// <summary>It clears choices and nothing else: the rows and the filter on screen, the trace's switch and the stack
    /// budget are settings the user made, and the next Start uses them as they are. Each is set away from its default
    /// first, so a clear that reset it would show.</summary>
    [Fact]
    public async Task Clear_choices_leaves_the_rows_the_filter_the_trace_switch_and_the_stack_budget_as_they_are()
    {
        var (vm, _) = await WithEveryKindOfChoice();
        vm.StackBudgetLow = true;
        vm.FilterText = "A";
        var rows = vm.Results.ToList();
        Assert.Equal(3, rows.Count);
        Assert.True(vm.TraceEnabled);

        vm.ClearChoicesCommand.Execute(null);

        Assert.Empty(vm.StackFunctions);                 // it did clear
        Assert.Equal("A", vm.FilterText);
        Assert.Equal(rows.Count, vm.Results.Count);
        for (int i = 0; i < rows.Count; i++) Assert.Same(rows[i], vm.Results[i]);
        Assert.True(vm.TraceEnabled);
        Assert.True(vm.StackBudgetLow);
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
