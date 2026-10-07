using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
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

    private static (CallTraceViewModel vm, LiveFuncsViewModel lf) MakeVm(FakeDumpService dump,
                                                                          IPlatformService? platform = null)
    {
        var lf = new LiveFuncsViewModel(dump, new NoopLogger());
        return (new CallTraceViewModel(dump, new NoopLogger(), lf, platform), lf);
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
    public async Task A_new_load_lets_go_of_the_trace_on_screen_before_it_reads()
    {
        // [TRACE-UI-LOAD-MEMORY] Live, build 3634: a 512 MB load after a 128 MB one peaked at 3.77 GB, the earlier
        // trace still held beside the new window and columns. The new read starts without it.
        var dump = Dump();
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Trace);

        dump.Info = new TraceInfo { Allocated = true, Quiesced = true, Gen = 8, Written = 9, FirstValid = 0, QpcFreq = 1_000_000 };
        dump.NamesGen = 8;
        CallTrace? heldDuringRead = vm.Trace;
        bool wasShown = true;
        dump.DuringObjNames = () => { heldDuringRead = vm.Trace; wasShown = vm.HasTrace; };
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Null(heldDuringRead);   // let go before the read
        Assert.False(wasShown);
        Assert.NotNull(vm.Trace);      // and the new one shown after it
        Assert.True(vm.HasTrace);
    }

    [Fact]
    public async Task A_long_read_collects_the_pages_garbage_as_it_goes()
    {
        // [TRACE-UI-LOAD-MEMORY] Live, build 3634, Avowed: a full 128 MB load still peaked at 1.27 GB working set. Each
        // page leaves the pipe's line and its parsed document behind (~5 MB for a full page), and nothing collected
        // them during a load of hundreds of pages. A collection at least every CollectEveryPages pages (no platform
        // service: free memory unknown, so exactly that), and one before the build.
        int pages = 4 * CallTraceViewModel.CollectEveryPages;   // four collection periods, whatever the period
        var dump = new FakeDumpService
        {
            Info = new TraceInfo { Allocated = true, Quiesced = true, Gen = 7, Written = (ulong)pages, FirstValid = 0, QpcFreq = 1_000_000 },
            PageMax = 1,   // a record a page
        };
        for (ulong k = 0; k < (ulong)pages; k++) dump.Ring.Add(new TraceRecord(k, 1000 + k, 0xA, 0, 1, 0));
        var (vm, _) = MakeVm(dump);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(pages, vm.Trace!.Count);
        Assert.True(vm.CollectionsDuringLastLoad >= 4, $"{vm.CollectionsDuringLastLoad} collections over {pages} pages");
    }

    // [TRACE-UI-LOAD-MEMORY] The maintainer, 2026-10-07: how often a load collects depends on the memory free now. The
    // pages' garbage between two collections may take 1/32 of it, at about 5 MB a page; never more than
    // CollectEveryPages pages, which the slider's estimate was calibrated on, and never less than one.
    [Theory]
    [InlineData(long.MaxValue, 16)]   // unknown: the calibrated period
    [InlineData(64L << 30, 16)]       // plenty: still the calibrated period, so the estimate stays an upper bound
    [InlineData(2560L << 20, 16)]     // 80 MB of garbage = 16 pages
    [InlineData(1L << 30, 6)]         // 32 MB = 6 pages
    [InlineData(512L << 20, 3)]
    [InlineData(256L << 20, 1)]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    public void The_pages_between_collections_follow_the_free_memory(long available, int pages)
        => Assert.Equal(pages, CallTraceViewModel.PagesBetweenCollections(available));

    [Fact]
    public async Task A_read_collects_more_often_as_the_free_memory_runs_low()
    {
        const int pages = 64;
        FakeDumpService Ring()
        {
            var d = new FakeDumpService
            {
                Info = new TraceInfo { Allocated = true, Quiesced = true, Gen = 7, Written = pages, FirstValid = 0, QpcFreq = 1_000_000 },
                PageMax = 1,   // a record a page
            };
            for (ulong k = 0; k < pages; k++) d.Ring.Add(new TraceRecord(k, 1000 + k, 0xA, 0, 1, 0));
            return d;
        }

        // Plenty free throughout: the calibrated period, four collections and the one before the build.
        var plenty = new MockPlatformService(Path.GetTempPath()) { AvailablePhysicalMemory = 64L << 30 };
        var (vm, _) = MakeVm(Ring(), plenty);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(pages, vm.Trace!.Count);
        Assert.InRange(vm.CollectionsDuringLastLoad, 4, 5);

        // Half way through, the free memory falls to 256 MB (the load itself, or the game, took it): from there a
        // collection after every page. Read again as the load goes, not once at its start.
        var falling = new MockPlatformService(Path.GetTempPath()) { AvailableByRead = r => r < pages / 2 ? 64L << 30 : 256L << 20 };
        var (vm2, _) = MakeVm(Ring(), falling);
        await vm2.LoadCommand.ExecuteAsync(null);
        Assert.Equal(pages, vm2.Trace!.Count);
        Assert.True(falling.AvailableMemoryReads >= pages, $"{falling.AvailableMemoryReads} reads over {pages} pages");
        Assert.True(vm2.CollectionsDuringLastLoad >= pages / 2, $"{vm2.CollectionsDuringLastLoad} collections over {pages} pages");
    }

    [Fact]
    public void A_page_reply_stays_short_enough_for_the_pipe_to_read_it_without_keeping_big_buffers()
    {
        // [TRACE-UI-LOAD-MEMORY] Measured 2026-10-07: StreamReader.ReadLineAsync on 13 lines of 14 million chars -- a
        // page of 262,144 records as base64 -- read from rotating pool threads, as PipeClient's loop is, kept 205 MB of
        // pooled line buffers after a full collection; on 104 lines of 1.75 million chars, nothing. Live, the same
        // retention held a full 128 MB load's working set ~0.3 GB above what the trace keeps. A page is read as one
        // line: its base64 stays near 1.75 million chars.
        long base64Chars = (long)CallTraceViewModel.PageRecords * 40 * 4 / 3;
        Assert.True(base64Chars <= 2_000_000, $"a page is {base64Chars:N0} chars of base64");
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

    // ---- [LIVEFUNCS-STEP2 U10] the list's widths: dragged from the header, shared by every row, remembered ----

    [Fact]
    public void The_widths_start_at_the_old_fixed_layout_and_the_saved_defaults_are_the_same()
    {
        // A file from before the widths were saved hydrates to the options' defaults, which must be what the tab
        // showed before them: Time 96, Duration 88, Thread 64, Object 260, the detail pane 380.
        var (vm, _) = MakeVm(Dump());
        var o = new CallTraceUiOptions();
        Assert.Equal((96.0, 88.0, 64.0, 260.0, 380.0),
                     (vm.TimeColWidth, vm.DurationColWidth, vm.ThreadColWidth, vm.ObjectColWidth, vm.DetailPaneWidth));
        Assert.Equal((96.0, 88.0, 64.0, 260.0, 380.0),
                     (o.TimeColWidth, o.DurationColWidth, o.ThreadColWidth, o.ObjectColWidth, o.DetailPaneWidth));
    }

    [Fact]
    public void A_width_dragged_or_loaded_below_its_minimum_stops_there()
    {
        // A drag past the edge, or a hand-edited ui-options.json, must not make a column vanish: a column of 0 has no
        // header left to drag it back by.
        var (vm, _) = MakeVm(Dump());
        vm.TimeColWidth = 0;
        vm.DurationColWidth = -50;
        vm.ThreadColWidth = 1;
        vm.ObjectColWidth = 10;
        vm.DetailPaneWidth = 0;
        Assert.Equal((40.0, 40.0, 32.0, 80.0, 200.0),
                     (vm.TimeColWidth, vm.DurationColWidth, vm.ThreadColWidth, vm.ObjectColWidth, vm.DetailPaneWidth));

        vm.TimeColWidth = double.NaN;   // Width NaN is "auto" to Avalonia: the column would size to its text
        Assert.Equal(40.0, vm.TimeColWidth);
        vm.ObjectColWidth = 1e9;
        Assert.Equal(CallTraceViewModel.MaxWidth, vm.ObjectColWidth);
        vm.ObjectColWidth = 333.5;      // inside the range: kept as dragged
        Assert.Equal(333.5, vm.ObjectColWidth);
    }

    [Fact]
    public void The_widths_round_trip_through_the_settings_root_with_the_source_generated_context()
    {
        // The JSON context reaches only what the root reaches: a CallTraceUiOptions held anywhere else is never written.
        var o = new UiOptionsSettings();
        o.CallTrace.TimeColWidth = 120;
        o.CallTrace.DurationColWidth = 70;
        o.CallTrace.ThreadColWidth = 50;
        o.CallTrace.ObjectColWidth = 333;
        o.CallTrace.DetailPaneWidth = 512;
        string json = JsonSerializer.Serialize(o, UiOptionsJsonContext.Default.UiOptionsSettings);
        var back = JsonSerializer.Deserialize(json, UiOptionsJsonContext.Default.UiOptionsSettings)!;
        Assert.Equal((120.0, 70.0, 50.0, 333.0, 512.0),
                     (back.CallTrace.TimeColWidth, back.CallTrace.DurationColWidth, back.CallTrace.ThreadColWidth,
                      back.CallTrace.ObjectColWidth, back.CallTrace.DetailPaneWidth));

        // A file from before the widths were saved has no callTrace object: the defaults, not zero widths.
        var older = JsonSerializer.Deserialize("{\"schemaVersion\":1}", UiOptionsJsonContext.Default.UiOptionsSettings)!;
        Assert.Equal(380.0, older.CallTrace.DetailPaneWidth);
        Assert.Equal(260.0, older.CallTrace.ObjectColWidth);
    }

    private static readonly XNamespace Av = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    /// <summary>A row cell's width binding: a row's DataContext is its CallTraceRow, so the width comes from the
    /// panel's view model, by the compiled route the row's ToggleCommand already takes.</summary>
    private const string RowWidth = "{Binding $parent[UserControl].((vm:CallTraceViewModel)DataContext).";

    private static XDocument PanelAxaml()
        => XDocument.Load(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Views/CallTracePanel.axaml"));

    private static XElement RowTemplate(XDocument doc)
        => doc.Descendants(Av + "DataTemplate").Single(e => (string?)e.Attribute(Xaml + "DataType") == "vm:CallTraceRow");

    [Fact]
    public void Every_row_cell_binds_the_view_models_width_and_the_Object_cell_has_a_tooltip()
    {
        var row = RowTemplate(PanelAxaml());
        // A ColumnDefinition is not a Visual, so a $parent binding on it never resolves: fixed columns cannot follow a drag.
        Assert.DoesNotContain(row.DescendantsAndSelf(), e => e.Attribute("ColumnDefinitions") != null);
        foreach (var (width, text) in new[] { ("TimeColWidth", "TimeText"), ("DurationColWidth", "DurationText"),
                                              ("ThreadColWidth", "ThreadText"), ("ObjectColWidth", "ObjectText") })
        {
            var cells = row.Descendants().Where(e => (string?)e.Attribute("Width") == RowWidth + width + "}").ToList();
            Assert.True(cells.Count == 1, $"{cells.Count} row cell(s) bind {width}");
            Assert.Contains(cells[0].DescendantsAndSelf(), e => (string?)e.Attribute("Text") == "{Binding " + text + "}");
        }
        var obj = row.Descendants().Single(e => (string?)e.Attribute("Width") == RowWidth + "ObjectColWidth}");
        Assert.Contains(obj.DescendantsAndSelf(),
                        e => ((string?)e.Attribute("ToolTip.Tip") ?? "").StartsWith("{Binding Object", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_header_cell_has_the_rows_width_and_a_thumb_that_drags_it_and_so_has_the_detail_pane()
    {
        var doc = PanelAxaml();
        var row = RowTemplate(doc);
        var outside = doc.Descendants().Where(e => !e.Ancestors().Contains(row) && e != row).ToList();
        Assert.DoesNotContain(outside, e => e.Attribute("ColumnDefinitions") != null);
        foreach (var width in new[] { "TimeColWidth", "DurationColWidth", "ThreadColWidth", "ObjectColWidth" })
        {
            var cells = outside.Where(e => (string?)e.Attribute("Width") == "{Binding " + width + "}").ToList();
            Assert.True(cells.Count == 1, $"{cells.Count} header cell(s) bind {width}");
            Assert.Contains(cells[0].Descendants(Av + "Thumb"), t => t.Attribute("DragDelta") != null);
        }
        var pane = outside.Single(e => e.Name == Av + "TextBox" && (string?)e.Attribute("Text") == "{Binding DetailText, Mode=OneWay}");
        Assert.Equal("{Binding DetailPaneWidth}", (string?)pane.Attribute("Width"));
        Assert.Contains(pane.Parent!.Elements(Av + "Thumb"), t => t.Attribute("DragDelta") != null);
    }
}
