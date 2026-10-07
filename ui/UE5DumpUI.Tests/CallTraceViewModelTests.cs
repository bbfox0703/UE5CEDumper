using System.Runtime.InteropServices;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LIVEFUNCS-TIMELINE-2026-10-04] The Call Trace tab: reading the DLL's stopped ring page by page, naming what it
/// saw, releasing the DLL's memory, and the rows the user expands, filters and takes back to the tree.
/// </summary>
public class CallTraceViewModelTests
{
    private const ulong R = TraceRecord.ReturnBit;

    private sealed class FakeDumpService : StubDumpService, IDumpService
    {
        public List<TraceRecord> Ring { get; } = new();
        public TraceInfo Info { get; set; } = new();
        public int PageMax { get; set; } = int.MaxValue;
        public List<(ulong from, int max)> PageCalls { get; } = new();
        public int ReleaseCalls { get; private set; }
        public Func<int, ulong>? GenOnPage { get; set; }
        public bool Stall { get; set; }
        public List<TraceFuncName> Funcs { get; } = new();
        public List<TraceObjName> Objs { get; } = new();
        public int NamesLimitSeen { get; private set; }
        /// <summary>Holds the activation's probe (the first, one-record page) until released.</summary>
        public TaskCompletionSource? ProbeGate { get; set; }
        /// <summary>Runs inside the objects' names call: a user action that lands while the names are read.</summary>
        public Action? DuringObjNames { get; set; }
        /// <summary>After this many pages the ring is gone (another reader released it): pages come back empty.</summary>
        public int FreedAfterPages { get; set; } = int.MaxValue;

        async Task<TracePage> IDumpService.PeTraceGetAsync(ulong from, int max, CancellationToken ct)
        {
            if (max == 1 && ProbeGate != null) { var g = ProbeGate; ProbeGate = null; await g.Task; }
            PageCalls.Add((from, max));
            if (PageCalls.Count(p => p.max != 1) > FreedAfterPages && max != 1)
                return new TracePage { Info = new TraceInfo { Gen = Info.Gen, Allocated = false }, Count = 0, Next = from };
            int k = PageCalls.Count;
            ulong begin = Math.Max(from, Info.FirstValid);
            ulong end = Math.Min(Info.Written, begin + (ulong)Math.Min(max, PageMax));
            var recs = Ring.Where(r => r.Seq >= begin && r.Seq < end).ToArray();
            var info = GenOnPage == null ? Info : new TraceInfo
            {
                Allocated = Info.Allocated, Tracing = Info.Tracing, Quiesced = Info.Quiesced, Gen = GenOnPage(k),
                Written = Info.Written, FirstValid = Info.FirstValid, QpcFreq = Info.QpcFreq,
            };
            return new TracePage
            {
                Info = info,
                Count = recs.Length,
                Next = Stall ? from : (recs.Length > 0 ? end : begin),
                Data = MemoryMarshal.AsBytes(recs.AsSpan()).ToArray(),
            };
        }

        /// <summary>The recording the DLL holds when names are asked; another one answers Stale.</summary>
        public ulong NamesGen { get; set; } = 7;
        public List<ulong> ReleasedGens { get; } = new();

        Task<TraceNamesPage<TraceFuncName>> IDumpService.PeTraceFuncNamesAsync(ulong gen, int offset, int limit, CancellationToken ct)
        {
            NamesLimitSeen = limit;
            if (gen != NamesGen) return Task.FromResult(new TraceNamesPage<TraceFuncName> { Gen = NamesGen, Stale = true });
            return Task.FromResult(new TraceNamesPage<TraceFuncName>
                { Gen = gen, Total = Funcs.Count, Offset = offset, Items = Funcs.Skip(offset).Take(limit).ToList() });
        }

        Task<TraceNamesPage<TraceObjName>> IDumpService.PeTraceObjNamesAsync(ulong gen, int offset, int limit, CancellationToken ct)
        {
            DuringObjNames?.Invoke();
            if (gen != NamesGen) return Task.FromResult(new TraceNamesPage<TraceObjName> { Gen = NamesGen, Stale = true });
            return Task.FromResult(new TraceNamesPage<TraceObjName>
                { Gen = gen, Total = Objs.Count, Offset = offset, Items = Objs.Skip(offset).Take(limit).ToList() });
        }

        Task IDumpService.PeTraceReleaseAsync(ulong gen, CancellationToken ct)
        {
            ReleaseCalls++;
            ReleasedGens.Add(gen);
            return Task.CompletedTask;
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

    // A(0) > B(1) > C(2); D(3) after A; E(4) on its own -- the same shape as CallTraceTreeTests.
    private static FakeDumpService Dump()
    {
        var d = new FakeDumpService();
        d.Ring.AddRange(new[]
        {
            new TraceRecord(0, 1000, 0xA, 0x10, 1, 0), new TraceRecord(1, 1010, 0xB, 0x20, 1, 0),
            new TraceRecord(2, 1020, 0xC, 0, 1, 0), new TraceRecord(3 | R, 1030, 2, 0, 1, 0),
            new TraceRecord(4 | R, 1040, 1, 0, 1, 0), new TraceRecord(5, 1050, 0xD, 0x10, 1, 0),
            new TraceRecord(6 | R, 1060, 5, 0, 1, 0), new TraceRecord(7 | R, 2000, 0, 0, 1, 0),
            new TraceRecord(8, 3000, 0xE, 0, 1, TraceRecord.ScopeRootFlag),
        });
        d.Info = new TraceInfo { Allocated = true, Quiesced = true, Gen = 7, Written = 9, FirstValid = 0, QpcFreq = 1_000_000 };
        d.Funcs.AddRange(new[]
        {
            new TraceFuncName { Addr = 0xA, Live = true, ClassName = "PlayerController", FuncName = "InputJump" },
            new TraceFuncName { Addr = 0xB, Live = true, ClassName = "Character", FuncName = "Jump" },
            new TraceFuncName { Addr = 0xC, Live = true, ClassName = "AnimInstance", FuncName = "Update" },
            new TraceFuncName { Addr = 0xD, Live = true, ClassName = "Character", FuncName = "OnJumped" },
            new TraceFuncName { Addr = 0xE, Live = true, ClassName = "GameMode", FuncName = "Tick" },
        });
        d.Objs.Add(new TraceObjName { Addr = 0x10, Live = true, Name = "PC_0", ClassName = "PC_C" });
        d.Objs.Add(new TraceObjName { Addr = 0x20, Live = false });
        return d;
    }

    private static (CallTraceViewModel vm, LiveFuncsViewModel lf) MakeVm(FakeDumpService dump)
    {
        var lf = new LiveFuncsViewModel(dump, new NoopLogger());
        return (new CallTraceViewModel(dump, new NoopLogger(), lf), lf);
    }

    [Fact]
    public async Task Load_reads_every_page_names_what_it_saw_and_releases_the_DLLs_memory()
    {
        var dump = Dump();
        dump.PageMax = 4;   // three pages of the nine records
        var (vm, lf) = MakeVm(dump);
        lf.HasTraceToOpen = true;

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasTrace);
        Assert.NotNull(vm.Trace);
        Assert.Equal(5, vm.Trace!.Count);
        Assert.Equal(new ulong[] { 0, 0, 4, 8 }, dump.PageCalls.Select(p => p.from).ToArray());   // probe, then 3 pages
        Assert.Equal(1, dump.ReleaseCalls);
        Assert.False(lf.HasTraceToOpen);
        Assert.Equal("Jump", vm.Trace.FuncName(1));
        Assert.True(vm.Trace.ObjStale(1));
        Assert.Equal(2, vm.Rows.Count);   // collapsed: the two roots
    }

    [Fact]
    public async Task A_long_read_collects_the_pages_garbage_as_it_goes()
    {
        // [TRACE-UI-LOAD-MEMORY] Live, build 3634, Avowed: a full 128 MB load still peaked at 1.27 GB working set. Each
        // page leaves the pipe's line and its parsed document behind (~40 MB for a full page), and nothing collected
        // them during a load of dozens of pages. A collection every CollectEveryPages pages, and one before the build.
        var dump = new FakeDumpService
        {
            Info = new TraceInfo { Allocated = true, Quiesced = true, Gen = 7, Written = 40, FirstValid = 0, QpcFreq = 1_000_000 },
            PageMax = 4,   // ten pages
        };
        for (ulong k = 0; k < 40; k++) dump.Ring.Add(new TraceRecord(k, 1000 + k, 0xA, 0, 1, 0));
        var (vm, _) = MakeVm(dump);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(40, vm.Trace!.Count);   // forty calls, ten pages read
        Assert.True(vm.CollectionsDuringLastLoad >= 10 / CallTraceViewModel.CollectEveryPages,
                    $"{vm.CollectionsDuringLastLoad} collections over ten pages");
    }

    [Fact]
    public async Task Each_page_asks_no_more_than_the_window_has_left()
    {
        // [TRACE-UI-LOAD-MEMORY] D2: a page is decoded into what is left of the window, so it must never be asked
        // for more: the old read asked 262,144 records of every page and copied what fitted.
        var dump = Dump();
        dump.PageMax = 4;
        var (vm, _) = MakeVm(dump);

        await vm.LoadCommand.ExecuteAsync(null);

        var pages = dump.PageCalls.Skip(1).ToList();   // the first call is the load's one-record probe
        Assert.Equal(new ulong[] { 0, 4, 8 }, pages.Select(p => p.from).ToArray());
        Assert.Equal(new[] { 9, 5, 1 }, pages.Select(p => p.max).ToArray());
        Assert.All(pages, p => Assert.True((ulong)p.max <= dump.Info.Written - p.from, $"page at {p.from} asked {p.max}"));
    }

    [Fact]
    public async Task The_release_names_the_recording_that_was_read()
    {
        var dump = Dump();
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(new[] { 7UL }, dump.ReleasedGens);
    }

    [Fact]
    public async Task Names_for_another_recording_drop_the_load_and_release_nothing()
    {
        var dump = Dump();
        dump.NamesGen = 8;   // a new Start replaced the trace between the paging and the names
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasTrace);
        Assert.Null(vm.Trace);
        Assert.Equal(0, dump.ReleaseCalls);
    }

    [Fact]
    public async Task Load_starts_at_the_first_kept_record()
    {
        var dump = Dump();
        dump.Info = new TraceInfo { Allocated = true, Quiesced = true, Gen = 7, Written = 9, FirstValid = 5, QpcFreq = 1_000_000 };
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(5UL, dump.PageCalls[1].from);
        Assert.Equal(2, vm.Trace!.Count);              // D(5) and E(8)
        Assert.Equal(1, vm.Trace.ReturnsBeforeWindow); // A's return (7); B's (4) is before the window too
    }

    [Fact]
    public async Task Nothing_is_loaded_while_recording_or_without_a_trace()
    {
        var dump = Dump();
        dump.Info = new TraceInfo { Allocated = true, Tracing = true, Quiesced = true, Gen = 7, Written = 9 };
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasTrace);
        Assert.Equal(0, dump.ReleaseCalls);

        dump.Info = new TraceInfo { Allocated = false };
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasTrace);

        dump.Info = new TraceInfo { Allocated = true, Quiesced = false, Written = 9 };
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasTrace);
        Assert.Equal(0, dump.ReleaseCalls);
    }

    [Fact]
    public async Task A_trace_replaced_while_it_was_read_is_dropped()
    {
        var dump = Dump();
        dump.PageMax = 4;
        dump.GenOnPage = k => k < 3 ? 7UL : 8UL;   // a new Start between the pages
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasTrace);
        Assert.Equal(0, dump.ReleaseCalls);   // not ours to release
    }

    [Fact]
    public async Task A_page_that_does_not_move_ends_the_read_instead_of_looping()
    {
        var dump = Dump();
        dump.PageMax = 4;
        dump.Stall = true;
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(dump.PageCalls.Count <= 3);
    }

    [Fact]
    public async Task Activation_loads_a_new_trace_once()
    {
        var dump = Dump();
        var (vm, _) = MakeVm(dump);
        await vm.OnActivatedAsync();
        Assert.True(vm.HasTrace);
        int pages = dump.PageCalls.Count;
        await vm.OnActivatedAsync();   // the same generation: only the probe
        Assert.Equal(pages + 1, dump.PageCalls.Count);
        Assert.Equal(1, dump.ReleaseCalls);
    }

    [Fact]
    public async Task After_a_disconnect_the_next_processs_first_trace_is_loaded_though_its_gen_repeats()
    {
        // Every game process numbers its traces from 1, so gen 7 in a new process is not the gen 7 already shown.
        var dump = Dump();
        var (vm, _) = MakeVm(dump);
        await vm.OnActivatedAsync();
        Assert.Equal(1, dump.ReleaseCalls);

        vm.ClearOnDisconnect();
        await vm.OnActivatedAsync();
        Assert.Equal(2, dump.ReleaseCalls);   // read again, not taken for the one already shown
    }

    [Fact]
    public async Task Leaving_the_tab_while_activation_waits_reads_nothing()
    {
        var dump = Dump();
        var gate = new TaskCompletionSource();
        dump.ProbeGate = gate;
        var (vm, _) = MakeVm(dump);
        var activation = vm.OnActivatedAsync();
        vm.OnLeavingTab();   // the user moved on before the probe came back
        gate.SetResult();
        await activation;
        Assert.False(vm.HasTrace);
        Assert.DoesNotContain(dump.PageCalls, p => p.max != 1);
    }

    [Fact]
    public async Task Once_read_the_release_is_sent_even_when_the_load_is_cancelled_while_building()
    {
        var dump = Dump();
        var (vm, _) = MakeVm(dump);
        dump.DuringObjNames = () => vm.CancelLoadCommand.Execute(null);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1, dump.ReleaseCalls);   // the DLL is not left holding the ring
    }

    [Fact]
    public async Task A_ring_freed_mid_read_is_not_built_as_a_whole_trace()
    {
        var dump = Dump();
        dump.PageMax = 4;
        dump.FreedAfterPages = 1;   // another reader released it after the first page
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasTrace);
        Assert.Null(vm.Trace);
        Assert.Equal(0, dump.ReleaseCalls);
    }

    [Fact]
    public async Task While_a_load_runs_neither_export_nor_another_load_is_offered()
    {
        var dump = Dump();
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);   // a trace on screen: export is offered
        Assert.True(vm.CanExport && vm.CanLoad);

        var gate = new TaskCompletionSource();
        dump.ProbeGate = gate;
        dump.Info = new TraceInfo { Allocated = true, Quiesced = true, Gen = 8, Written = 9, QpcFreq = 1_000_000 };
        dump.NamesGen = 8;
        var load = vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsLoading);
        Assert.False(vm.CanExport);
        Assert.False(vm.CanLoad);
        gate.SetResult();
        await load;
        Assert.True(vm.CanExport && vm.CanLoad);
    }

    [Fact]
    public async Task A_newer_recording_that_cannot_be_read_marks_the_trace_on_screen_as_older()
    {
        var dump = Dump();
        var (vm, _) = MakeVm(dump);
        await vm.OnActivatedAsync();
        Assert.False(vm.ShownIsOlder);

        // The next recording kept nothing: the trace on screen is no longer the last recording's.
        dump.Info = new TraceInfo { Allocated = true, Quiesced = true, Gen = 8, Written = 0, QpcFreq = 1_000_000 };
        await vm.OnActivatedAsync();
        Assert.True(vm.HasTrace);
        Assert.True(vm.ShownIsOlder);
    }

    [Fact]
    public async Task Toggle_expands_a_call_and_the_rows_follow()
    {
        var (vm, _) = MakeVm(Dump());
        await vm.LoadCommand.ExecuteAsync(null);
        var rowA = (CallTraceRow)vm.Rows[0]!;
        vm.ToggleCommand.Execute(rowA);
        Assert.Equal(4, vm.Rows.Count);   // A, B, D, E
        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public async Task The_filter_shows_matches_flat_and_Show_in_tree_takes_one_back_opened()
    {
        var (vm, _) = MakeVm(Dump());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "update";
        Assert.True(vm.IsFiltered);
        Assert.Single(vm.Rows.Cast<object>());
        vm.SelectedIndex = 0;
        vm.ShowInTreeCommand.Execute(null);
        Assert.False(vm.IsFiltered);
        Assert.Equal("", vm.FilterText);
        Assert.Equal(5, vm.Rows.Count);   // A and B opened above C
        Assert.Equal(2, ((CallTraceRow)vm.Rows[vm.SelectedIndex]!).Call);
    }

    [Fact]
    public async Task Clearing_the_filter_shows_the_tree_again()
    {
        var (vm, _) = MakeVm(Dump());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "character";
        Assert.Equal(2, vm.Rows.Count);
        vm.FilterText = "";
        Assert.False(vm.IsFiltered);
        Assert.Equal(2, vm.Rows.Count);   // the two roots
    }

    [Fact]
    public async Task The_detail_names_the_call_and_its_callers()
    {
        var (vm, _) = MakeVm(Dump());
        await vm.LoadCommand.ExecuteAsync(null);
        string d = vm.Detail(2);   // C, under A and B
        int a = d.IndexOf("PlayerController::InputJump", StringComparison.Ordinal);
        int b = d.IndexOf("Character::Jump", StringComparison.Ordinal);
        int c = d.LastIndexOf("AnimInstance::Update", StringComparison.Ordinal);
        Assert.True(a >= 0 && b > a && c > b, d);
    }

    [Fact]
    public async Task Rows_are_made_on_demand_and_carry_the_tree()
    {
        var (vm, _) = MakeVm(Dump());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ExpandAllCommand.Execute(null);
        var rows = vm.Rows.Cast<CallTraceRow>().ToList();
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, rows.Select(r => r.Call));
        Assert.Equal("▾", rows[0].Glyph);
        Assert.Equal("", rows[2].Glyph);          // C has no children
        Assert.Equal(28.0, rows[2].Indent);        // depth 2
        Assert.True(rows[1].ObjectStale);
        Assert.True(rows[4].IsScopeRoot);
        Assert.Equal("—", rows[4].DurationText);   // never returned
    }
}
