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
            CallLog.Add("release");
            return Task.CompletedTask;
        }

        // [LIVEFUNCS-STEP2] The snapshots: arms with layouts, then each ring's slots, all before the release.
        public List<string> CallLog { get; } = new();
        public List<SnapArm> SnapArms { get; } = new();
        public List<SnapRingInfo> SnapRingList { get; } = new();
        public List<SnapSlot> SnapSlots { get; } = new();
        public bool SnapStale { get; set; }

        Task<SnapLayoutsPage> IDumpService.PeSnapLayoutsAsync(ulong gen, int offset, CancellationToken ct)
        {
            CallLog.Add("layouts");
            return Task.FromResult(new SnapLayoutsPage
            {
                Info = new TraceInfo { Gen = SnapStale ? gen + 1 : gen }, Stale = SnapStale, Total = SnapArms.Count,
                Next = SnapArms.Count, Rings = SnapRingList, Arms = SnapArms,
            });
        }

        Task<SnapPage> IDumpService.PeSnapGetAsync(ulong gen, int ring, ulong from, int max, CancellationToken ct)
        {
            CallLog.Add($"snap{ring}@{from}");
            var items = SnapSlots.Where(s => s.Index >= from).Take(1).ToList();   // one slot a page: paging shows
            return Task.FromResult(new SnapPage
            {
                Info = new TraceInfo { Gen = gen }, Ring = ring, Count = items.Count,
                Next = items.Count > 0 ? items[0].Index + 1 : from, Items = items,
            });
        }

        // [LIVEFUNCS-STEP3] The native stacks. Every reply carries every stack ring's window, and a page holds one
        // index: a slot, or an orphan, whose page comes back empty yet moves on, as the DLL's does (review M1).
        public List<SnapRingInfo> StackRingList { get; } = new();
        public List<(int Ring, StackSlot Slot)> StackSlots { get; } = new();
        public HashSet<(int Ring, ulong Index)> StackOrphans { get; } = new();
        /// <summary>From this stack call on (1 = the first), the DLL answers Stale.</summary>
        public int StackStaleAt { get; set; } = int.MaxValue;
        /// <summary>From this stack call on, the DLL holds another recording: a new Start between the pages.</summary>
        public int StackGenMovesAt { get; set; } = int.MaxValue;
        private int _stackCalls;

        Task<StackPage> IDumpService.PeStackGetAsync(ulong gen, int ring, ulong from, int max, CancellationToken ct)
        {
            CallLog.Add($"stack{ring}@{from}");
            int k = ++_stackCalls;
            if (k >= StackStaleAt)
                return Task.FromResult(new StackPage { Info = new TraceInfo { Gen = gen + 1 }, Stale = true, Ring = ring, Next = from });
            var w = StackRingList.FirstOrDefault(r => r.Ring == ring);
            ulong begin = Math.Max(from, w?.FirstValid ?? 0);   // the DLL starts a from below the window at it
            bool inWindow = w != null && begin < w.Written;
            var items = inWindow
                ? StackSlots.Where(s => s.Ring == ring && s.Slot.Index == begin).Select(s => s.Slot).ToList()
                : new List<StackSlot>();
            return Task.FromResult(new StackPage
            {
                Info = new TraceInfo { Gen = k >= StackGenMovesAt ? gen + 1 : gen }, Ring = ring, Rings = StackRingList,
                Count = items.Count, Orphans = inWindow && StackOrphans.Contains((ring, begin)) ? 1UL : 0,
                Next = inWindow ? begin + 1 : begin, Items = items,
            });
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
                                                                          IPlatformService? platform = null,
                                                                          UE5DumpUI.Helpers.AobMakerStatus? aobMaker = null)
    {
        var lf = new LiveFuncsViewModel(dump, new NoopLogger(), aobMaker: aobMaker);
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

    // ---- [LIVEFUNCS-STEP2] U12: the snapshots, read before the release ----

    private static FakeDumpService DumpWithSnapshots()
    {
        var d = Dump();
        d.Info = new TraceInfo { Allocated = true, Quiesced = true, Gen = 7, Written = 9, FirstValid = 0, QpcFreq = 1_000_000,
                                 Snap = new SnapInfo { Allocated = true, Rings = 1 } };
        var lay0 = new SnapLayout { FuncName = "Jump", Params = new[] { new SnapParam { Name = "X", Type = "IntProperty", Size = 4 } } };
        var lay1 = new SnapLayout { FuncName = "Jump", Params = new[] { new SnapParam { Name = "Y", Type = "IntProperty", Offset = 4, Size = 4 } } };
        d.SnapArms.Add(new SnapArm { Index = 0, Ring = 0, FuncName = "Jump", State = "read", Layout = lay0 });
        d.SnapArms.Add(new SnapArm { Index = 1, Ring = 0, FuncName = "Jump", State = "read", Layout = lay1 });   // a reload
        d.SnapRingList.Add(new SnapRingInfo { Ring = 0, Written = 3, FirstValid = 0 });
        d.SnapSlots.Add(new SnapSlot { Index = 0, EntrySeq = 1, Arm = 0, Values = new[] { new SnapValue { Text = "7" } } });
        d.SnapSlots.Add(new SnapSlot { Index = 1, EntrySeq = 1, Arm = 0, IsAfter = true });
        d.SnapSlots.Add(new SnapSlot { Index = 2, EntrySeq = 5, Arm = 1, Values = new[] { new SnapValue { Text = "9" } } });
        return d;
    }

    [Fact]
    public async Task The_snapshots_are_read_after_the_names_and_before_the_release()
    {
        var dump = DumpWithSnapshots();
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "layouts", "snap0@0", "snap0@1", "snap0@2", "release" }, dump.CallLog);
        Assert.Equal(new ulong[] { 7 }, dump.ReleasedGens);
        var s = vm.Trace!.Snapshots!;
        int jump = vm.Trace.FindBySeq(1), onJumped = vm.Trace.FindBySeq(5);
        Assert.Equal("7", s.EntryOf(jump)!.Values![0].Text);
        Assert.NotNull(s.AfterOf(jump));
        Assert.Equal("X", s.ArmOf(s.EntryOf(jump)!)!.Layout!.Params[0].Name);
        Assert.Equal("Y", s.ArmOf(s.EntryOf(onJumped)!)!.Layout!.Params[0].Name);   // its own arm's layout
        Assert.Equal(2, s.CallsWithParams);
        Assert.False(s.Has(vm.Trace.FindBySeq(0)));
    }

    [Fact]
    public async Task A_stale_snapshot_answer_drops_the_load_without_releasing()
    {
        var dump = DumpWithSnapshots();
        dump.SnapStale = true;
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Null(vm.Trace);
        Assert.Empty(dump.ReleasedGens);
    }

    [Fact]
    public async Task A_trace_without_snapshots_reads_none_and_keeps_none_from_the_last_one()
    {
        var dump = DumpWithSnapshots();
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Trace!.Snapshots);

        dump.Info = new TraceInfo { Allocated = true, Quiesced = true, Gen = 8, Written = 9, FirstValid = 0, QpcFreq = 1_000_000 };
        dump.NamesGen = 8;
        dump.CallLog.Clear();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "release" }, dump.CallLog);
        Assert.Null(vm.Trace!.Snapshots);
    }

    // ---- [LIVEFUNCS-STEP2] U13: the Parameters tab ----

    // A(0) unchosen; B(1) taken, copies at and after the call; C(2) over the budget; D(5) taken, its arm never read;
    // E(8) lone and taken, but its slot overwritten since.
    private static FakeDumpService DumpForParams()
    {
        var d = DumpWithSnapshots();
        d.Ring.Clear();
        d.Ring.AddRange(new[]
        {
            new TraceRecord(0, 1000, 0xA, 0x10, 1, 0), new TraceRecord(1, 1010, 0xB, 0x20, 1, 2),
            new TraceRecord(2, 1020, 0xC, 0, 1, 16), new TraceRecord(3 | R, 1030, 2, 0, 1, 0),
            new TraceRecord(4 | R, 1040, 1, 0, 1, 0), new TraceRecord(5, 1050, 0xD, 0x10, 1, 2),
            new TraceRecord(6 | R, 1060, 5, 0, 1, 0), new TraceRecord(7 | R, 2000, 0, 0, 1, 0),
            new TraceRecord(8, 3000, 0xE, 0, 1, TraceRecord.ScopeRootFlag | 4 | 2),
        });
        var lay = new SnapLayout
        {
            FuncName = "Jump",
            Params = new[]
            {
                new SnapParam { Name = "X", Type = "IntProperty", Offset = 0, Size = 4, Kind = "in" },
                new SnapParam { Name = "Out", Type = "IntProperty", Offset = 4, Size = 4, Kind = "out" },
                new SnapParam { Name = "ReturnValue", Type = "IntProperty", Offset = 8, Size = 4, Kind = "return" },
            },
        };
        d.SnapArms.Clear();
        d.SnapArms.Add(new SnapArm { Index = 0, Ring = 0, FuncName = "Jump", State = "read", Layout = lay });
        d.SnapArms.Add(new SnapArm { Index = 1, Ring = 0, FuncName = "OnJumped", State = "not_read_before_stop" });
        d.SnapSlots.Clear();
        d.SnapSlots.Add(new SnapSlot
        {
            Index = 0, EntrySeq = 1, Arm = 0, Data = new byte[] { 7, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0 },
            Values = new[] { new SnapValue { Text = "7" }, new SnapValue { Text = "1" },
                             new SnapValue { Text = "\u2014", Mark = SnapMark.Missing } },
        });
        d.SnapSlots.Add(new SnapSlot
        {
            Index = 1, EntrySeq = 1, Arm = 0, IsAfter = true, Data = new byte[] { 7, 0, 0, 0, 2, 0, 0, 0, 9, 0, 0, 0 },
            Values = new[] { new SnapValue { Text = "", Mark = SnapMark.Missing }, new SnapValue { Text = "2" },
                             new SnapValue { Text = "9" } },
        });
        d.SnapSlots.Add(new SnapSlot { Index = 2, EntrySeq = 5, Arm = 1, Data = new byte[] { 1, 2 } });
        return d;
    }

    [Fact]
    public async Task The_Parameters_tab_gives_values_and_a_reason_for_every_kind_of_call()
    {
        var dump = DumpForParams();
        var (vm, _) = MakeVm(dump);
        vm.StringLookup = k => k;   // the reasons by their keys
        await vm.LoadCommand.ExecuteAsync(null);
        var t = vm.Trace!;

        var b = vm.Params(t.FindBySeq(1));
        Assert.Equal(new[] { "X", "Out", "ReturnValue" }, b.Rows.Select(r => r.Name));
        Assert.Equal("7", b.Rows[0].AtCall);
        Assert.Equal("", b.Rows[0].After);                      // an In after the call: blank
        Assert.Equal("\u2014", b.Rows[2].AtCall);                // the return value at the call: not yet
        Assert.Equal("9", b.Rows[2].After);
        Assert.False(b.Rows[0].Changed);                         // the same bytes
        Assert.True(b.Rows[1].Changed && b.Rows[2].Changed);     // bytes that differ, nothing else
        Assert.Empty(b.Notes);
        Assert.Contains("str.CT.Param.HexAfter", b.Hex, StringComparison.Ordinal);

        Assert.Equal(new[] { "str.CT.Param.Budget" }, vm.Params(t.FindBySeq(2)).Notes);
        var d = vm.Params(t.FindBySeq(5));
        Assert.Empty(d.Rows);
        Assert.Equal(new[] { "str.CT.Param.RawOnly" }, d.Notes);
        Assert.NotEqual("", d.Hex);
        Assert.Equal(new[] { "str.CT.Param.Lone", "str.CT.Param.Overwritten" }, vm.Params(t.FindBySeq(8)).Notes);
        Assert.Equal(new[] { "str.CT.Param.NotChosen" }, vm.Params(t.FindBySeq(0)).Notes);
    }

    [Fact]
    public async Task Only_calls_with_parameters_works_with_the_filter_box_empty()
    {
        var dump = DumpForParams();
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Rows.Count);   // the tree's two roots

        vm.OnlyWithParams = true;
        Assert.True(vm.IsFiltered);
        Assert.Equal(2, vm.Rows.Count);   // B and D carry copies; E's was overwritten
        var calls = Enumerable.Range(0, vm.Rows.Count).Select(k => ((CallTraceRowList)vm.Rows).CallAt(k)).ToArray();
        Assert.Equal(new[] { vm.Trace!.FindBySeq(1), vm.Trace.FindBySeq(5) }, calls);
        Assert.True(((CallTraceRowList)vm.Rows)[0].HasParams);

        vm.FilterText = "OnJumped";
        Assert.Single(Enumerable.Range(0, vm.Rows.Count));
        vm.FilterText = "";
        vm.OnlyWithParams = false;
        Assert.False(vm.IsFiltered);
    }

    // ---- [LIVEFUNCS-STEP3] S3-U4: the native stacks, read before the release and joined apart from the parameters ----

    private const uint StackTaken = StackInfo.TakenEntryFlag;
    private const uint Lone = 4;

    // A(0) unchosen; B(1) chosen for both, its stack taken (and its parameters, with them); C(2) inside B, chosen for a
    // stack alone; D(5) parameters alone, with them; E(8) chosen for a stack alone and called outside every scope, so
    // recorded on its own. Stack ring 0 lost slot 0 to newer calls and its first kept slot is an orphan; ring 1 has E's.
    // Without parameters it is a stacks-only Start: the buffer allocated with no parameter ring and no arm.
    private static FakeDumpService DumpWithStacks(bool withParams = true, ulong captures = 5)
    {
        var d = withParams ? DumpWithSnapshots() : Dump();
        uint p = withParams ? 2u : 0u;
        d.Ring.Clear();
        d.Ring.AddRange(new[]
        {
            new TraceRecord(0, 1000, 0xA, 0x10, 1, 0), new TraceRecord(1, 1010, 0xB, 0x20, 1, p | StackTaken),
            new TraceRecord(2, 1020, 0xC, 0, 1, StackTaken), new TraceRecord(3 | R, 1030, 2, 0, 1, 0),
            new TraceRecord(4 | R, 1040, 1, 0, 1, 0), new TraceRecord(5, 1050, 0xD, 0x10, 1, p),
            new TraceRecord(6 | R, 1060, 5, 0, 1, 0), new TraceRecord(7 | R, 2000, 0, 0, 1, 0),
            new TraceRecord(8, 3000, 0xE, 0, 1, Lone | StackTaken),
        });
        d.Info = new TraceInfo
        {
            Allocated = true, Quiesced = true, Gen = 7, Written = 9, FirstValid = 0, QpcFreq = 1_000_000,
            Snap = new SnapInfo { Allocated = true, Rings = withParams ? 1 : 0 },
            // 12 ticks a capture at 1 MHz: 12 us.
            Stack = new StackInfo { Rings = 2, Depth = 16, Captures = captures, SkippedBudget = 1, DroppedBudget = 2,
                                    SpentTicks = 12 * captures, MaxTicks = 30 },
        };
        var game = new StackSite { Addr = 0x140001234, Module = "Game.exe", CeModule = "Game.exe", ModuleBase = 0x140000000,
                                   Rva = 0x1234, Unwind = true };
        d.StackRingList.Add(new SnapRingInfo { Ring = 0, Written = 4, FirstValid = 1 });
        d.StackRingList.Add(new SnapRingInfo { Ring = 1, Written = 1, FirstValid = 0 });
        d.StackOrphans.Add((0, 1));
        d.StackSlots.Add((0, new StackSlot { Index = 2, EntrySeq = 1, Frames = new[] { game } }));
        d.StackSlots.Add((0, new StackSlot { Index = 3, EntrySeq = 2, Ticks = 12, Frames = new[] { game, game } }));
        d.StackSlots.Add((1, new StackSlot { Index = 0, EntrySeq = 8, Frames = new[] { game } }));
        return d;
    }

    [Fact]
    public async Task A_stack_is_joined_to_its_call_apart_from_the_parameters_and_marks_its_row()
    {
        var dump = DumpWithStacks();
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);
        var t = vm.Trace!;
        int b = t.FindBySeq(1), c = t.FindBySeq(2), d = t.FindBySeq(5), e = t.FindBySeq(8);

        Assert.NotNull(t.Stacks);
        var st = t.Stacks!;
        // Every ring paged to its end: a page of orphans alone is empty, and the slots after it are still read.
        Assert.Equal(new[] { b, c, e }, st.Calls);
        Assert.Equal(2, st.StackOf(c)!.Frames.Count);
        Assert.Null(st.StackOf(d));
        Assert.Equal(1UL, st.Orphans);
        Assert.Equal(0, st.Unjoined);
        Assert.Same(dump.Info.Stack, st.Info);
        Assert.Equal("release", dump.CallLog[^1]);   // read before the release frees the rings

        // Not parameters: a stack never makes a call one with a copy.
        var s = t.Snapshots!;
        Assert.Equal(2, s.CallsWithParams);
        Assert.False(s.Has(c));
        Assert.False(s.Has(e));

        vm.ExpandAllCommand.Execute(null);
        var rows = vm.Rows.Cast<CallTraceRow>().ToDictionary(r => r.Call);
        Assert.True(rows[b].HasStack && rows[b].HasParams);
        Assert.True(rows[c].HasStack);
        Assert.False(rows[c].HasParams);
        Assert.False(rows[d].HasStack);
        Assert.True(rows[e].HasStack);

        // The row marks it beside (p).
        var marker = RowTemplate(PanelAxaml()).Descendants(Av + "TextBlock")
            .SingleOrDefault(x => (string?)x.Attribute("Text") == "{StaticResource str.CT.Stack.Marker}");
        Assert.NotNull(marker);
        Assert.Equal("{Binding HasStack}", (string?)marker!.Attribute("IsVisible"));
        Assert.Equal("{StaticResource str.Tip.CT.Stack.Marker}", (string?)marker.Attribute("ToolTip.Tip"));
    }

    [Fact]
    public async Task A_stacks_only_trace_reads_its_stacks_and_asks_for_no_parameter_layouts()
    {
        var dump = DumpWithStacks(withParams: false);
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);
        var t = vm.Trace!;
        Assert.DoesNotContain(dump.CallLog, x => x == "layouts" || x.StartsWith("snap", StringComparison.Ordinal));
        Assert.Null(t.Snapshots);
        Assert.False(vm.CanExportParams);
        Assert.NotNull(t.Stacks);
        Assert.Equal(new[] { t.FindBySeq(1), t.FindBySeq(2), t.FindBySeq(8) }, t.Stacks!.Calls);
        Assert.Equal("release", dump.CallLog[^1]);
    }

    [Theory]
    [InlineData(1, int.MaxValue)]   // the first read, which brings the windows
    [InlineData(2, int.MaxValue)]   // a later page
    [InlineData(int.MaxValue, 3)]   // a new Start between the pages
    public async Task A_stale_stack_page_or_a_new_recording_between_the_pages_drops_the_load(int staleAt, int genMovesAt)
    {
        var dump = DumpWithStacks();
        dump.StackStaleAt = staleAt;
        dump.StackGenMovesAt = genMovesAt;
        var (vm, _) = MakeVm(dump);
        vm.StringLookup = k => k;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Null(vm.Trace);
        Assert.False(vm.HasTrace);
        Assert.Empty(dump.ReleasedGens);   // not ours to release
        Assert.Equal("str.CT.Status.Changed", vm.StatusText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_call_chosen_for_its_stack_alone_has_no_parameters_to_show_or_export(bool withParams)
    {
        var dump = DumpWithStacks(withParams);
        var (vm, _) = MakeVm(dump);
        vm.StringLookup = k => k;
        await vm.LoadCommand.ExecuteAsync(null);
        var t = vm.Trace!;
        int c = t.FindBySeq(2), e = t.FindBySeq(8);

        // Lone marks a call chosen for anything: alone it says nothing of parameters, so not "taken, then overwritten".
        Assert.Equal(new[] { "str.CT.Param.NotChosen" }, vm.Params(e).Notes);
        Assert.Equal(new[] { "str.CT.Param.NotChosen" }, vm.Params(c).Notes);
        Assert.Empty(vm.Params(e).Rows);

        var w = new StringWriter();
        UE5DumpUI.Helpers.CallTraceExport.WriteJsonl(t, w, new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc));
        var lines = w.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var calls = lines.Where(l => l.StartsWith("{\"kind\":\"call\"", StringComparison.Ordinal))
                         .Select(l => JsonDocument.Parse(l).RootElement)
                         .ToDictionary(x => x.GetProperty("seq").GetUInt64());
        // No snapshot to report, but still recorded on its own.
        Assert.False(calls[8].TryGetProperty("snapshot", out _));
        Assert.True(calls[8].GetProperty("lone").GetBoolean());
        Assert.False(calls[8].GetProperty("excluded").GetBoolean());
        Assert.False(calls[2].TryGetProperty("snapshot", out _));
        Assert.False(calls[2].TryGetProperty("lone", out _));
        if (withParams)
        {
            Assert.Equal("taken", calls[1].GetProperty("snapshot").GetString());
            Assert.Contains("\"snapshot_skipped_budget\":", lines[0], StringComparison.Ordinal);
        }
        else
        {
            // No parameter ring, so no parameter budget to account for in the header.
            Assert.DoesNotContain("snapshot_skipped_budget", lines[0], StringComparison.Ordinal);
            Assert.DoesNotContain("snapshot_dropped_budget", lines[0], StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_summary_says_how_many_calls_carry_a_stack_what_the_budget_left_out_and_what_a_capture_cost()
    {
        var (vm, _) = MakeVm(DumpWithStacks());
        vm.StringLookup = En;
        await vm.LoadCommand.ExecuteAsync(null);
        string sum = vm.Summary(vm.Trace!);
        // Three calls carry one; the budget skipped one and dropped two; 60 ticks over 5 captures at 1 MHz.
        Assert.Contains(Line("str.CT.Status.Stacks", 3, 3L, 2L), sum);
        Assert.Contains(Line("str.CT.Status.StackCost", 12.0), sum);
        Assert.Contains(Line("str.CT.Status.Snapshots", 2, 2, 0L), sum);   // the parameters' sentence stands too
        Assert.Equal(sum, vm.StatusText);

        static string Lead(string key) => En(key)[..En(key).IndexOf('{')];
        // Nothing taken: nothing to price.
        var (none, _) = MakeVm(DumpWithStacks(withParams: false, captures: 0));
        none.StringLookup = En;
        await none.LoadCommand.ExecuteAsync(null);
        Assert.Contains(Lead("str.CT.Status.Stacks"), none.StatusText);
        Assert.DoesNotContain(Lead("str.CT.Status.StackCost"), none.StatusText);
        // No stacks armed: no sentence.
        var (plain, _) = MakeVm(DumpWithSnapshots());
        plain.StringLookup = En;
        await plain.LoadCommand.ExecuteAsync(null);
        Assert.DoesNotContain(Lead("str.CT.Status.Stacks"), plain.StatusText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_new_load_lets_go_of_the_trace_on_screen_before_it_reads(bool stackShown)
    {
        // [TRACE-UI-LOAD-MEMORY] Live, build 3634: a 512 MB load after a 128 MB one peaked at 3.77 GB, the earlier
        // trace still held beside the new window and columns. The new read starts without it: off the screen, and
        // reachable from nothing the view model keeps -- a stack shown from it too, whose frames were named against
        // that trace's functions (review U5-CODEINDEX-PINS-OLD-TRACE).
        var dump = stackShown ? StackDump() : Dump();
        var (vm, _) = MakeVm(dump);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Trace);
        if (stackShown)
        {
            vm.SelectedIndex = 0;
            Assert.NotEmpty(vm.StackRows);
        }
        var first = Weakly(vm);

        dump.Info = new TraceInfo { Allocated = true, Quiesced = true, Gen = 8, Written = dump.Info.Written, FirstValid = 0,
                                    QpcFreq = 1_000_000 };
        dump.NamesGen = 8;
        bool heldDuringRead = true, wasShown = true, firstAlive = true;
        dump.DuringObjNames = () =>
        {
            heldDuringRead = vm.Trace != null;
            wasShown = vm.HasTrace;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            firstAlive = first.IsAlive;
        };
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(heldDuringRead);   // let go before the read
        Assert.False(wasShown);
        Assert.False(firstAlive);       // and nothing else keeps it while the read runs
        Assert.NotNull(vm.Trace);       // and the new one shown after it
        Assert.True(vm.HasTrace);
    }

    /// <summary>A weak reference to the trace on screen, made in a frame of its own: a local of the async test would
    /// keep the trace alive itself.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference Weakly(CallTraceViewModel vm) => new(vm.Trace);

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
    public async Task The_parameters_csv_is_offered_only_for_a_trace_with_snapshots()
    {
        // [LIVEFUNCS-STEP2] U14: a trace that took no copies has no rows for it.
        var dump = DumpWithSnapshots();
        var (vm, _) = MakeVm(dump);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.False(vm.CanExportParams);

        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.CanExportParams);
        Assert.Contains(nameof(vm.CanExportParams), raised);   // the button's binding hears it

        dump.Info = new TraceInfo { Allocated = true, Quiesced = true, Gen = 8, Written = 9, FirstValid = 0, QpcFreq = 1_000_000 };
        dump.NamesGen = 8;
        raised.Clear();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.CanExport);
        Assert.False(vm.CanExportParams);
        Assert.Contains(nameof(vm.CanExportParams), raised);
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

    // ---- the list's widths ([LIVEFUNCS-STEP2] U10): dragged from the header, shared by every row, remembered ----

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

    // ---- [CT-DETAIL-COVERS-LIST] the remembered widths fitted to the room the panel has ----
    // SplitWidth is the width the list and the detail pane share. The list's floor is 512: a row's item padding (24),
    // Time, Duration and Thread at their default widths (96 + 88 + 64), Object at its floor (80), and 160 of Function.

    private static (double time, double duration, double thread, double obj) ShownColumns(CallTraceViewModel vm)
        => (vm.ShownTimeColWidth, vm.ShownDurationColWidth, vm.ShownThreadColWidth, vm.ShownObjectColWidth);

    [Fact]
    public void The_pane_shows_its_remembered_width_where_the_list_keeps_its_floor_beside_it()
    {
        // Build 3645: a remembered 766 in a narrower panel covered the whole list.
        var (vm, _) = MakeVm(Dump());
        vm.DetailPaneWidth = 766;
        Assert.Equal(766.0, vm.ShownDetailPaneWidth);       // not laid out yet: nothing to fit it to
        vm.SplitWidth = 1000;
        Assert.Equal(1000.0 - 512, vm.ShownDetailPaneWidth);
        Assert.Equal(766.0, vm.DetailPaneWidth);            // still the user's
        vm.SplitWidth = 1600;                               // a wider window gives it back
        Assert.Equal(766.0, vm.ShownDetailPaneWidth);
    }

    [Fact]
    public void A_panel_narrower_than_both_floors_keeps_the_pane_at_its_200_and_gives_the_list_the_rest()
    {
        var (vm, _) = MakeVm(Dump());
        vm.DetailPaneWidth = 4096;
        vm.SplitWidth = 600;
        Assert.Equal(200.0, vm.ShownDetailPaneWidth);
        vm.SplitWidth = 50;
        Assert.Equal(200.0, vm.ShownDetailPaneWidth);
    }

    [Fact]
    public void A_layout_raises_the_shown_widths_and_never_writes_a_remembered_one()
    {
        // A remembered width's change is what saves ui-options.json: a window resize must neither save nor lose it.
        var (vm, _) = MakeVm(Dump());
        vm.DetailPaneWidth = 766;
        vm.TimeColWidth = 300;
        vm.ObjectColWidth = 900;
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.SplitWidth = 700;
        vm.SplitWidth = 2400;
        vm.SplitWidth = 700;
        string[] remembered = { nameof(CallTraceViewModel.TimeColWidth), nameof(CallTraceViewModel.DurationColWidth),
                                nameof(CallTraceViewModel.ThreadColWidth), nameof(CallTraceViewModel.ObjectColWidth),
                                nameof(CallTraceViewModel.DetailPaneWidth) };
        Assert.DoesNotContain(raised, n => remembered.Contains(n));
        Assert.Equal((300.0, 88.0, 64.0, 900.0, 766.0),
                     (vm.TimeColWidth, vm.DurationColWidth, vm.ThreadColWidth, vm.ObjectColWidth, vm.DetailPaneWidth));
        foreach (var shown in new[] { nameof(CallTraceViewModel.ShownDetailPaneWidth), nameof(CallTraceViewModel.ShownTimeColWidth),
                                      nameof(CallTraceViewModel.ShownObjectColWidth) })
            Assert.Contains(shown, raised);
    }

    [Fact]
    public void A_pane_drag_past_the_limit_stops_there_and_the_next_drag_back_moves_at_once()
    {
        var (vm, _) = MakeVm(Dump());
        vm.SplitWidth = 1000;          // the pane may show 488
        vm.DragDetailPane(+400);       // from 380, asks for 780
        Assert.Equal(488.0, vm.ShownDetailPaneWidth);
        Assert.Equal(488.0, vm.DetailPaneWidth);   // what a drag leaves shown is what is remembered
        vm.DragDetailPane(-30);
        Assert.Equal(458.0, vm.ShownDetailPaneWidth);
    }

    [Fact]
    public void A_pane_drag_starts_from_the_width_shown_not_the_one_remembered()
    {
        // The remembered 766 shows as 488: a drag 10 narrower moves the pane at once, with no margin to undo first.
        var (vm, _) = MakeVm(Dump());
        vm.DetailPaneWidth = 766;
        vm.SplitWidth = 1000;
        vm.DragDetailPane(-10);
        Assert.Equal((478.0, 478.0), (vm.ShownDetailPaneWidth, vm.DetailPaneWidth));
        vm.DragDetailPane(-1000);
        Assert.Equal(200.0, vm.ShownDetailPaneWidth);   // its own floor
    }

    [Fact]
    public void Object_gives_way_so_Function_keeps_160_and_comes_back_to_its_remembered_width_with_room()
    {
        var (vm, _) = MakeVm(Dump());
        vm.SplitWidth = 1000;   // the pane 380, the list 620: a row's cells have 596
        Assert.Equal((96.0, 88.0, 64.0, 596.0 - 248 - 160), ShownColumns(vm));
        Assert.Equal(260.0, vm.ObjectColWidth);
        vm.SplitWidth = 1400;   // 996 for the cells: every column as remembered
        Assert.Equal((96.0, 88.0, 64.0, 260.0), ShownColumns(vm));
    }

    [Fact]
    public void The_columns_give_way_from_the_right_each_to_its_floor()
    {
        var (vm, _) = MakeVm(Dump());
        vm.TimeColWidth = 4096;   // hand-edited, or dragged on a wider screen
        vm.SplitWidth = 1000;     // 596 for the cells: Time keeps what Function and the others' floors leave
        Assert.Equal((596.0 - 160 - 40 - 32 - 80, 40.0, 32.0, 80.0), ShownColumns(vm));
        Assert.Equal(4096.0, vm.TimeColWidth);
    }

    [Fact]
    public void A_list_below_its_floor_is_laid_out_as_at_its_floor_and_cut_at_its_edge()
    {
        // The pane keeps its 200 and the list has 400 of its 512. Squeezed to their floors instead, the columns would
        // show less of every one of them, Time included; laid out as at the floor, Time and Duration stay whole.
        var (vm, _) = MakeVm(Dump());
        vm.SplitWidth = 600;
        Assert.Equal((96.0, 88.0, 64.0, 80.0), ShownColumns(vm));
    }

    [Fact]
    public void A_column_drag_starts_from_the_width_shown_stops_where_Function_keeps_160_and_comes_back_at_once()
    {
        var (vm, _) = MakeVm(Dump());
        vm.SplitWidth = 1000;   // 596 for the cells; Object shows 188 of its 260
        vm.DragThread(+1000);   // Thread pushes Object to its floor, then stops: 596 - 160 - 96 - 88 - 80
        Assert.Equal((96.0, 88.0, 172.0, 80.0), ShownColumns(vm));
        Assert.Equal(172.0, vm.ThreadColWidth);
        vm.DragThread(-10);
        Assert.Equal((96.0, 88.0, 162.0, 90.0), ShownColumns(vm));
        Assert.Equal(260.0, vm.ObjectColWidth);   // pushed, not dragged: still the user's

        vm.TimeColWidth = 4096;   // shown as 284, the columns after it at their floors
        Assert.Equal((284.0, 40.0, 32.0, 80.0), ShownColumns(vm));
        vm.DragTime(-10);         // from the 284 shown; the 10 it frees goes to Duration, the next that wants room
        Assert.Equal((274.0, 50.0, 32.0, 80.0), ShownColumns(vm));
        Assert.Equal(274.0, vm.TimeColWidth);

        vm.TimeColWidth = 96;
        vm.SplitWidth = 1400;     // 996 for the cells
        vm.DragObject(+1000);     // Object's step widens it, up to what Function's 160 leaves
        Assert.Equal(996.0 - 160 - 96 - 88 - 162, vm.ShownObjectColWidth);
        vm.DragObject(-5);
        Assert.Equal(996.0 - 160 - 96 - 88 - 162 - 5, vm.ShownObjectColWidth);
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
        // [LIVEFUNCS-STEP2] U13: the pane is a TabControl now (Call | Parameters); the width and its thumb are its.
        var pane = outside.Single(e => (string?)e.Attribute("Width") == "{Binding DetailPaneWidth}");
        Assert.Contains(pane.Descendants(Av + "TextBox"),
                        e => (string?)e.Attribute("Text") == "{Binding DetailText, Mode=OneWay}");
        Assert.Contains(pane.Parent!.Elements(Av + "Thumb"), t => t.Attribute("DragDelta") != null);
    }

    [Fact]
    public void The_panels_thumbs_have_a_template_so_they_can_be_seen_and_dragged()
    {
        // Avalonia.Themes.Fluent 12 themes a Thumb only inside a ScrollBar or a Slider (read from its assembly,
        // 2026-10-08), so a bare Thumb has no template and draws nothing: a handle no one can find. The panel styles
        // its own; a column was dragged live with it on build 3640.
        var doc = PanelAxaml();
        Assert.NotEmpty(doc.Descendants(Av + "Thumb"));
        var style = doc.Descendants(Av + "Style").SingleOrDefault(s => (string?)s.Attribute("Selector") == "Thumb");
        Assert.NotNull(style);
        Assert.Contains(style!.Descendants(Av + "Setter"), s => (string?)s.Attribute("Property") == "Template"
                        && s.Descendants(Av + "ControlTemplate").Any());
    }

    [Fact]
    public void A_list_narrower_than_its_columns_cuts_them_at_its_edge_instead_of_overlapping_them()
    {
        // [CT-COLUMNS-OVERLAP] A DockPanel gives a cell that no longer fits only the width that is left, and Avalonia
        // centres a fixed-width cell in a narrower slot, so the cell reached back over the column on its left
        // ("ThreaObject" in the step-3 walkthrough). Aligned left, a cell keeps where it starts and its dragged width,
        // and runs past the list's edge to be cut there. The layout itself is measured on real controls by the
        // headless project's CallTraceColumnsTests.
        var doc = PanelAxaml();
        var row = RowTemplate(doc);
        var outside = doc.Descendants().Where(e => !e.Ancestors().Contains(row) && e != row).ToList();
        foreach (var width in new[] { "TimeColWidth", "DurationColWidth", "ThreadColWidth", "ObjectColWidth" })
        {
            var header = outside.Single(e => (string?)e.Attribute("Width") == "{Binding " + width + "}");
            var cell = row.Descendants().Single(e => (string?)e.Attribute("Width") == RowWidth + width + "}");
            Assert.True((string?)header.Attribute("HorizontalAlignment") == "Left", $"the header's {width} cell is not aligned left");
            Assert.True((string?)cell.Attribute("HorizontalAlignment") == "Left", $"the row's {width} cell is not aligned left");
        }
        // The header is outside the list and is drawn after the detail pane: unclipped, its overflow covers that pane.
        var headerRow = outside.Single(e => (string?)e.Attribute("Width") == "{Binding TimeColWidth}").Parent!;
        Assert.True((string?)headerRow.Attribute("ClipToBounds") == "True", "the header's row does not clip what overflows it");
        // A row is narrower than the list's viewport by the item's padding: a column the row places at its own edge
        // would show there, over the column cut at that edge (measured with the list narrower than Time, Duration and
        // Thread together).
        var rowPanel = row.Descendants().Single(e => (string?)e.Attribute("Width") == RowWidth + "TimeColWidth}").Parent!;
        Assert.True((string?)rowPanel.Attribute("ClipToBounds") == "True", "a row does not clip what overflows it");
    }

    // ---- the detail pane's addresses and the function's native entry ([LIVEFUNCS-STEP2] U11) ----

    /// <summary>en.axaml's strings, read as the app shows them: Res has no Avalonia application in a unit test, so
    /// the view model is handed these and the tests read the sentences a user reads.</summary>
    private static readonly Lazy<Dictionary<string, string>> EnStrings = new(() =>
    {
        var text = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Resources/Strings/en.axaml"));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     text, "x:Key=\"(str\\.[^\"]+)\">([^<]*)</sys:String>"))
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

    private const uint FuncNative = 0x400;
    /// <summary>Where the game's module is loaded in DetailDump: the first function's code lies 0x123456 into it.</summary>
    private const ulong GameBase = 0x140000000;
    /// <summary>The five functions' UFunction objects, on the heap as a game's are.</summary>
    private static readonly ulong[] DetailFuncs = { 0x1E5F0A000, 0x1E5F0A100, 0x1E5F0A200, 0x1E5F0A300, 0x1E5F0A400 };

    // Five calls in a row, one per kind of native-entry line: 0 native with its entry in the module, 1 native with no
    // entry found, 2 script, 3 flags unread, 4 a native function unloaded before the trace was read. Calls 0 and 1
    // are on a named and a stale object; 2 on none.
    private static FakeDumpService DetailDump()
    {
        var d = new FakeDumpService();
        for (ulong k = 0; k < 5; k++)
        {
            ulong obj = k == 2 ? 0UL : 0x2B4C0010UL + k * 0x100;
            d.Ring.Add(new TraceRecord(2 * k, 1000 + 10 * k, DetailFuncs[k], obj, 1, 0));
            d.Ring.Add(new TraceRecord((2 * k + 1) | R, 1005 + 10 * k, 2 * k, 0, 1, 0));
        }
        d.Info = new TraceInfo { Allocated = true, Quiesced = true, Gen = 7, Written = 10, FirstValid = 0, QpcFreq = 1_000_000 };
        d.Funcs.AddRange(new[]
        {
            new TraceFuncName { Addr = DetailFuncs[0], Live = true, ClassName = "Character", FuncName = "Jump",
                                FunctionFlags = FuncNative, CodeAddr = GameBase + 0x123456 },
            new TraceFuncName { Addr = DetailFuncs[1], Live = true, ClassName = "Character", FuncName = "Crouch",
                                FunctionFlags = FuncNative },
            new TraceFuncName { Addr = DetailFuncs[2], Live = true, ClassName = "WBP_Inventory_C", FuncName = "OnOpen",
                                FunctionFlags = 0x04020000 },   // BlueprintCallable | Public: no FUNC_Native
            new TraceFuncName { Addr = DetailFuncs[3], Live = true, ClassName = "Pawn", FuncName = "Restart" },
            new TraceFuncName { Addr = DetailFuncs[4], Unloaded = true, ClassName = "WBP_Map_C", FuncName = "OnTile",
                                FunctionFlags = FuncNative },
        });
        d.Objs.Add(new TraceObjName { Addr = 0x2B4C0010, Live = true, Name = "BP_Hero_C_0", ClassName = "BP_Hero_C" });
        d.Objs.Add(new TraceObjName { Addr = 0x2B4C0110, Live = false });
        return d;
    }

    private static async Task<CallTraceViewModel> DetailVm(AddressFormat format, string moduleBase = "0x140000000")
    {
        var (vm, _) = MakeVm(DetailDump());
        vm.StringLookup = En;
        vm.SetEngineState(new EngineState { ModuleName = "Game.exe", ModuleBase = moduleBase });
        vm.SelectedAddressFormatIndex = (int)format;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(5, vm.Trace!.Count);
        return vm;
    }

    [Fact]
    public void The_address_lines_say_whose_address_they_are()
    {
        // Maintainer, build 3638: "UFunction: 0x..." reads as a call address, and pasted into CE's disassembler it shows
        // garbage. It is the UFunction object's address (data), and the Object line's is the calling object's.
        Assert.Contains("UFunction object's address", En("str.CT.Detail.FuncAddr"), StringComparison.Ordinal);
        Assert.Contains("calling object", En("str.CT.Detail.Object"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("calling object's address", En("str.CT.Detail.ObjectStale"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Under_hex_without_prefix_the_function_object_and_object_lines_carry_no_0x()
    {
        var vm = await DetailVm(AddressFormat.HexNoPrefix);
        string named = vm.Detail(0), stale = vm.Detail(1);
        Assert.Contains(Line("str.CT.Detail.FuncAddr", "1E5F0A000"), named);
        Assert.Contains(Line("str.CT.Detail.Object", "BP_Hero_C_0", "BP_Hero_C", "2B4C0010"), named);
        Assert.Contains(Line("str.CT.Detail.ObjectStale", "2B4C0110"), stale);
        Assert.DoesNotContain("0x", named, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0x", stale, StringComparison.OrdinalIgnoreCase);

        // The setting changed with a call selected: the pane follows it at once.
        vm.SelectedIndex = 0;
        vm.SelectedAddressFormatIndex = (int)AddressFormat.HexWithPrefix;
        Assert.Contains(Line("str.CT.Detail.FuncAddr", "0x1E5F0A000"), vm.DetailText);
        Assert.Contains(Line("str.CT.Detail.Object", "BP_Hero_C_0", "BP_Hero_C", "0x2B4C0010"), vm.DetailText);
    }

    private static readonly string[] KindKeys =
        { "str.CT.Detail.NativeNotFound", "str.CT.Detail.Script", "str.CT.Detail.KindUnknown", "str.CT.Detail.NativeNotRead" };

    /// <summary>What a shown entry's line starts with, inside the module or outside it.</summary>
    private static IEnumerable<string> EntryPrefixes()
        => new[] { "str.CT.Detail.NativeEntry", "str.CT.Detail.NativeEntryOutside" }
           .Select(k => En(k)).Select(s => s[..s.IndexOf("{0}", StringComparison.Ordinal)]);

    [Fact]
    public async Task A_live_native_functions_entry_shows_as_a_CE_address_inside_the_module()
    {
        var vm = await DetailVm(AddressFormat.HexNoPrefix);
        string d = vm.Detail(0);
        Assert.Contains(Line("str.CT.Detail.NativeEntry", "\"Game.exe\"+123456"), d);
        foreach (var k in KindKeys) Assert.DoesNotContain(Line(k), d);

        // The module is the one the trace was loaded from: a trace outlives its connection, and the next game's base
        // would give an RVA into another image.
        vm.SetEngineState(new EngineState { ModuleName = "Other.exe", ModuleBase = "0x7FF600000000" });
        Assert.Contains(Line("str.CT.Detail.NativeEntry", "\"Game.exe\"+123456"), vm.Detail(0));

        // Below the module's base it is no RVA: the absolute address, written as the Address setting says.
        var other = await DetailVm(AddressFormat.HexWithPrefix, moduleBase: "0x7FF600000000");
        Assert.Contains(Line("str.CT.Detail.NativeEntryOutside", "0x140123456"), other.Detail(0));
    }

    [Theory]
    [InlineData(1, "str.CT.Detail.NativeNotFound", "native entry not found")]
    [InlineData(2, "str.CT.Detail.Script", "script function (runs in the interpreter)")]
    [InlineData(3, "str.CT.Detail.KindUnknown", "kind unknown")]
    [InlineData(4, "str.CT.Detail.NativeNotRead", "native entry not read (unloaded)")]
    public async Task A_function_with_no_entry_to_show_says_why(int call, string key, string says)
    {
        Assert.Contains(says, En(key), StringComparison.OrdinalIgnoreCase);
        var vm = await DetailVm(AddressFormat.HexNoPrefix);
        string d = vm.Detail(call);
        Assert.Contains(Line(key), d);
        // One kind line a call: flags of 0 are not a script function's, an unloaded native is not "not found".
        foreach (var other in KindKeys.Where(k => k != key)) Assert.DoesNotContain(Line(other), d);
        foreach (var prefix in EntryPrefixes()) Assert.DoesNotContain(prefix, d);
    }

    [Fact]
    public async Task The_names_reply_carries_a_live_native_functions_entry()
    {
        var pipe = new MockPipeClient();
        pipe.SetHandler(_ => new System.Text.Json.Nodes.JsonObject
        {
            ["ok"] = true, ["gen"] = 4UL, ["total"] = 3, ["offset"] = 0,
            ["items"] = new System.Text.Json.Nodes.JsonArray
            {
                new System.Text.Json.Nodes.JsonObject { ["addr"] = "0x100", ["live"] = true, ["code_addr"] = "0x7FF6A0123456" },
                new System.Text.Json.Nodes.JsonObject { ["addr"] = "0x200", ["live"] = true, ["code_addr"] = "" },   // script, or not found
                new System.Text.Json.Nodes.JsonObject { ["addr"] = "0x300", ["live"] = false, ["unloaded"] = true },  // not live: none sent
            },
        });
        IDumpService svc = new UE5DumpUI.Services.DumpService(pipe, new MockLoggingService(), IdentityCodePage.Instance);
        var page = await svc.PeTraceFuncNamesAsync(4, 0, 100, TestContext.Current.CancellationToken);
        Assert.Equal(new ulong[] { 0x7FF6A0123456, 0, 0 }, page.Items.Select(f => f.CodeAddr).ToArray());
    }

    // ---- [LIVEFUNCS-STEP3] S3-U5: the Call stack tab (view A), and a frame to Cheat Engine (view D) ----

    private const uint StackBudget = StackInfo.BudgetEntryFlag;
    private const uint Excluded = 8;
    /// <summary>Two traced natives share this code (identical-code folding): a frame there is neither one's alone.</summary>
    private const ulong SharedCode = GameBase + 0x200000;
    private static readonly ulong[] FoldedFuncs = { 0x1E5F0A500, 0x1E5F0A600 };
    /// <summary>A command or a value on the panel's view model, from inside a row template.</summary>
    private const string PanelVm = "{Binding $parent[UserControl].((vm:CallTraceViewModel)DataContext).";

    /// <summary>A return address as the DLL describes it: the RVAs from its own module's base, and CE's name for that
    /// module unless one is given.</summary>
    private static StackSite Site(ulong addr, string module = "", ulong moduleBase = 0, ulong fn = 0, bool unwind = true,
                                  bool own = false, string known = "", string? ceModule = null, string cls = "",
                                  string func = "", int shared = 0, bool script = false)
        => new()
        {
            Addr = addr, Module = module, CeModule = ceModule ?? module, ModuleBase = moduleBase,
            Rva = module.Length == 0 ? 0 : (uint)(addr - moduleBase), Fn = fn,
            FnRva = fn == 0 || module.Length == 0 ? 0 : (uint)(fn - moduleBase), Unwind = unwind, Own = own, Known = known,
            UFunc = func.Length == 0 ? 0 : 0x5000UL, ClassName = cls, FuncName = func, Shared = shared, Script = script,
        };

    // Call 0's stack, nearest first: a frame of each kind the tab names.
    private static readonly StackSite[] JumpStack =
    {
        Site(GameBase + 0x1234, "Game.exe", GameBase, fn: GameBase + 0x1200),             // #0 no traced native starts there
        Site(GameBase + 0x123480, "Game.exe", GameBase, fn: GameBase + 0x123456),         // #1 Character::Jump's native entry
        Site(SharedCode + 0x10, "Game.exe", GameBase, fn: SharedCode),                     // #2 two natives' folded code
        Site(GameBase + 0x300100, "Game.exe", GameBase, fn: GameBase + 0x300000, known: "process_event"),
        Site(0x7FFB10001000, "version.dll", 0x7FFB10000000, fn: 0x7FFB10000F00, own: true), // #4 the hook, in its proxy
        Site(0x7FFC20000050, "ntdll.dll", 0x7FFC20000000, unwind: false),                  // #5 no unwind data
        // #6 a name the UI machine's code page narrows: CE lists Café as Cafe (best fit, measured on ACP 950).
        Site(0x7FFC30002000, "Café.dll", 0x7FFC30000000, fn: 0x7FFC30001F00, ceModule: "Cafe.dll"),
        Site(0x2A0000010, unwind: false),                                                   // #7 outside every module
    };

    // DetailDump's five functions and two more that share their native entry, a call each. Each call shows one thing the
    // tab says: 0 a whole stack (above); 1 lone, and over the stack budget; 2 taken, its slot written over since; 3 not
    // chosen; 4 excluded, the walk partial, its one frame without unwind data; 5 lone, cut at the depth, its nearest
    // frame without unwind data; 6 a slot whose flags a theory sets.
    private static FakeDumpService StackDump(int lastSlotFlags = 0)
    {
        var d = DetailDump();
        uint[] flags = { StackTaken, Lone | StackBudget, StackTaken, 0, Excluded | StackTaken, Lone | StackTaken, StackTaken };
        var funcs = DetailFuncs.Concat(FoldedFuncs).ToArray();
        d.Ring.Clear();
        for (ulong k = 0; k < (ulong)funcs.Length; k++)
        {
            d.Ring.Add(new TraceRecord(2 * k, 1000 + 10 * k, funcs[k], 0, 1, flags[k]));
            d.Ring.Add(new TraceRecord((2 * k + 1) | R, 1005 + 10 * k, 2 * k, 0, 1, 0));
        }
        d.Info = new TraceInfo
        {
            Allocated = true, Quiesced = true, Gen = 7, Written = 2 * (ulong)funcs.Length, FirstValid = 0, QpcFreq = 1_000_000,
            Stack = new StackInfo { Rings = 1, Depth = 16, Captures = 4, SpentTicks = 21, MaxTicks = 12 },
        };
        // Run before Walk: the label takes the first name in order, not the first one read.
        d.Funcs.Add(new TraceFuncName { Addr = FoldedFuncs[0], Live = true, ClassName = "Pawn", FuncName = "Run",
                                        FunctionFlags = FuncNative, CodeAddr = SharedCode });
        d.Funcs.Add(new TraceFuncName { Addr = FoldedFuncs[1], Live = true, ClassName = "Character", FuncName = "Walk",
                                        FunctionFlags = FuncNative, CodeAddr = SharedCode });
        d.StackRingList.Add(new SnapRingInfo { Ring = 0, Written = 4, FirstValid = 0 });
        // At 1 MHz a tick is a microsecond.
        d.StackSlots.Add((0, new StackSlot { Index = 0, EntrySeq = 0, Ticks = 12, Frames = JumpStack }));
        d.StackSlots.Add((0, new StackSlot { Index = 1, EntrySeq = 8, Flags = StackSlot.Partial, Ticks = 3,
                                             Frames = new[] { JumpStack[7] } }));
        d.StackSlots.Add((0, new StackSlot { Index = 2, EntrySeq = 10, Flags = StackSlot.More, Ticks = 5,
                                             Frames = new[] { JumpStack[5], JumpStack[0] } }));
        bool walked = lastSlotFlags is 0 or StackSlot.Partial or StackSlot.More;
        d.StackSlots.Add((0, new StackSlot { Index = 3, EntrySeq = 12, Flags = lastSlotFlags, Ticks = 1,
                                             Frames = walked ? new[] { JumpStack[0] } : Array.Empty<StackSite>() }));
        return d;
    }

    private static async Task<CallTraceViewModel> StackVm(FakeDumpService dump, IPlatformService? platform = null,
                                                          UE5DumpUI.Helpers.AobMakerStatus? aobMaker = null)
    {
        var (vm, _) = MakeVm(dump, platform, aobMaker);
        vm.StringLookup = En;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(7, vm.Trace!.Count);
        Assert.Equal(4, vm.Trace.Stacks!.Calls.Count);
        return vm;
    }

    [Theory]
    [InlineData(AddressFormat.ModuleOffset, "\"Game.exe\"+1234", "\"ntdll.dll\"+50", "\"Cafe.dll\"+2000", "2A0000010")]
    [InlineData(AddressFormat.HexNoPrefix, "140001234", "7FFC20000050", "7FFC30002000", "2A0000010")]
    [InlineData(AddressFormat.HexWithPrefix, "0x140001234", "0x7FFC20000050", "0x7FFC30002000", "0x2A0000010")]
    public async Task A_frames_address_follows_the_Address_setting_in_CEs_name_for_its_own_module(
        AddressFormat format, string game, string ntdll, string cafe, string none)
    {
        // Each frame's own module and base, never the game module the trace was loaded from.
        Assert.Equal(game, CallTraceViewModel.FrameAddress(JumpStack[0], format));
        Assert.Equal(ntdll, CallTraceViewModel.FrameAddress(JumpStack[5], format));
        Assert.Equal(cafe, CallTraceViewModel.FrameAddress(JumpStack[6], format));
        Assert.Equal(none, CallTraceViewModel.FrameAddress(JumpStack[7], format));

        // On the tab: the selected call's rows, drawn again when the setting changes.
        var vm = await StackVm(StackDump());
        vm.SelectedAddressFormatIndex = (int)(format == AddressFormat.HexNoPrefix ? AddressFormat.ModuleOffset
                                                                                  : AddressFormat.HexNoPrefix);
        vm.SelectedIndex = 0;
        vm.SelectedAddressFormatIndex = (int)format;
        Assert.Equal(new[] { game, ntdll, cafe, none }, new[] { 0, 5, 6, 7 }.Select(k => vm.StackRows[k].Address));
    }

    [Fact]
    public async Task A_frame_is_named_by_the_first_rule_that_fits_it()
    {
        var vm = await StackVm(StackDump());
        vm.SelectedIndex = 0;
        Assert.Equal(new[]
        {
            Line("str.CT.Stack.Into", 0x34UL, "\"Game.exe\"+1200"),
            Line("str.CT.Stack.Native", "Character::Jump", 0x2AUL),
            Line("str.CT.Stack.NativeShared", "Character::Walk", 0x10UL, 1),
            Line("str.CT.Stack.ProcessEvent", 0x100UL),
            Line("str.CT.Stack.Hook"),
            Line("str.CT.Stack.NoUnwind"),
            Line("str.CT.Stack.Into", 0x100UL, "\"Cafe.dll\"+1F00"),
            Line("str.CT.Stack.NoModule"),
        }, vm.StackRows.Select(r => r.Where));
        Assert.Equal(Enumerable.Range(0, JumpStack.Length), vm.StackRows.Select(r => r.Index));

        // Where a frame fits more than one rule: the hook, then ProcessEvent, then a traced native's entry, then "into".
        var index = CallTraceViewModel.CodeIndex(vm.Trace!);
        Assert.Equal(new[] { "Character::Walk", "Pawn::Run" }, index[SharedCode]);
        Assert.Equal(Line("str.CT.Stack.Hook"),
                     vm.FrameWhere(Site(SharedCode + 0x10, "Game.exe", GameBase, fn: SharedCode, own: true,
                                        known: "process_event"), index));
        Assert.Equal(Line("str.CT.Stack.ProcessEvent", 0x10UL),
                     vm.FrameWhere(Site(SharedCode + 0x10, "Game.exe", GameBase, fn: SharedCode, known: "process_event"),
                                   index));
        // Code outside every module that has unwind data (a table registered at run time): "into" an absolute start.
        Assert.Equal(Line("str.CT.Stack.Into", 0x8UL, "0x2A0000000"), vm.FrameWhere(Site(0x2A0000008, fn: 0x2A0000000), index));
    }

    [Fact]
    public async Task A_frame_the_trace_never_named_is_named_from_the_DLLs_index()
    {
        // [LIVEFUNCS-STEP3] S3-A1: an exec thunk a Blueprint reached without ProcessEvent is in no traced function's
        // code_addr, but the DLL's one pass over the object array names it.
        var vm = await StackVm(StackDump());
        var index = CallTraceViewModel.CodeIndex(vm.Trace!);
        const ulong thunk = GameBase + 0x9000;
        Assert.False(index.ContainsKey(thunk));
        Assert.Equal(Line("str.CT.Stack.Native", "Weapon::Fire", 0x18UL),
                     vm.FrameWhere(Site(thunk + 0x18, "Game.exe", GameBase, fn: thunk, cls: "Weapon", func: "Fire"), index));
        // Several functions enter there: the frame is none of them in particular, and the line says how many.
        Assert.Equal(Line("str.CT.Stack.NativeIndexShared", "Weapon::Fire", 0x18UL, 3),
                     vm.FrameWhere(Site(thunk + 0x18, "Game.exe", GameBase, fn: thunk, cls: "Weapon", func: "Fire",
                                        shared: 3), index));
        // A traced native's own name comes first: the trace saw that very function called.
        Assert.Equal(Line("str.CT.Stack.Native", "Character::Jump", 0x2AUL),
                     vm.FrameWhere(Site(GameBase + 0x123456 + 0x2A, "Game.exe", GameBase, fn: GameBase + 0x123456, cls: "Other", func: "Name"),
                                   index));
        // A name without its class still reads.
        Assert.Equal(Line("str.CT.Stack.Native", "Fire", 0x18UL),
                     vm.FrameWhere(Site(thunk + 0x18, "Game.exe", GameBase, fn: thunk, func: "Fire"), index));
        // [A1-INTERP-LABEL] The script functions' entry is the Blueprint interpreter: the function the DLL names is only
        // the lowest-addressed of them (live on DQ XI S, a level script's function on the minimap widget's stack), so
        // the line names the interpreter and how many enter it, never that function.
        string interp = vm.FrameWhere(Site(thunk + 0x525, "Game.exe", GameBase, fn: thunk, cls: "x00_Snd_Common_C",
                                           func: "Game - CasinoNpcScheduleEnd", shared: 6678, script: true), index);
        Assert.DoesNotContain("CasinoNpcScheduleEnd", interp, StringComparison.Ordinal);
        Assert.Equal(Line("str.CT.Stack.Interpreter", 0x525UL, 6678), interp);
        // A script entry no other function shares (one Blueprint function loaded) is still the interpreter.
        Assert.Equal(Line("str.CT.Stack.InterpreterOne", 0x525UL),
                     vm.FrameWhere(Site(thunk + 0x525, "Game.exe", GameBase, fn: thunk, cls: "BP_A_C", func: "Tick",
                                        script: true), index));
    }

    private static readonly string[] SlotFlagKeys =
    {
        "str.CT.Stack.Partial", "str.CT.Stack.Fault", "str.CT.Stack.More", "str.CT.Stack.BadSp", "str.CT.Stack.LowStack",
        "str.CT.Stack.NoCapturer",
    };

    [Fact]
    public async Task The_tab_says_why_a_call_has_no_stack_and_how_far_to_trust_the_one_it_has()
    {
        var vm = await StackVm(StackDump());
        // 0: a whole stack. Its first frame without unwind data is #5; #7 has none either, but nothing lies below it.
        Assert.Equal(new[] { Line("str.CT.Stack.MayBeWrong", 5), Line("str.CT.Stack.Cost", 12.0) }, vm.Stack(0).Notes);
        // 1: lone, and the stack budget left it out.
        var budget = vm.Stack(1);
        Assert.Empty(budget.Rows);
        Assert.Equal(new[] { Line("str.CT.Param.Lone"), Line("str.CT.Stack.Budget") }, budget.Notes);
        // 2: taken, then written over.
        Assert.Empty(vm.Stack(2).Rows);
        Assert.Equal(new[] { Line("str.CT.Stack.Overwritten") }, vm.Stack(2).Notes);
        // 3: never chosen.
        Assert.Equal(new[] { Line("str.CT.Stack.NotChosen") }, vm.Stack(3).Notes);
        // 4: excluded, and the walk stopped at the caller: one frame, and nothing below it to doubt.
        var partial = vm.Stack(4);
        Assert.Single(partial.Rows);
        Assert.Equal(new[] { Line("str.CT.Param.Excluded"), Line("str.CT.Stack.Partial"), Line("str.CT.Stack.Cost", 3.0) },
                     partial.Notes);
        // 5: lone, cut at the depth, and its nearest frame without unwind data.
        Assert.Equal(new[] { Line("str.CT.Param.Lone"), Line("str.CT.Stack.More"), Line("str.CT.Stack.MayBeWrong", 0),
                             Line("str.CT.Stack.Cost", 5.0) }, vm.Stack(5).Notes);
    }

    [Theory]
    [InlineData(StackSlot.Partial, "str.CT.Stack.Partial")]
    [InlineData(StackSlot.Fault, "str.CT.Stack.Fault")]
    [InlineData(StackSlot.More, "str.CT.Stack.More")]
    [InlineData(StackSlot.BadSp, "str.CT.Stack.BadSp")]
    [InlineData(StackSlot.LowStack, "str.CT.Stack.LowStack")]
    [InlineData(StackSlot.NoCapturer, "str.CT.Stack.NoCapturer")]
    public async Task Each_slot_flag_has_a_note_of_its_own(int flag, string key)
    {
        var vm = await StackVm(StackDump(lastSlotFlags: flag));
        var notes = vm.Stack(6).Notes;
        Assert.Contains(Line(key), notes);
        foreach (var other in SlotFlagKeys.Where(k => k != key)) Assert.DoesNotContain(Line(other), notes);
    }

    [Fact]
    public async Task Selecting_a_call_fills_the_tab_and_a_new_load_empties_it()
    {
        var dump = StackDump();
        var vm = await StackVm(dump);
        Assert.Empty(vm.StackRows);
        vm.SelectedIndex = 0;
        Assert.Equal(JumpStack.Select(s => s.Addr), vm.StackRows.Select(r => r.Abs));
        Assert.Equal(string.Join(Environment.NewLine, vm.Stack(0).Notes), vm.StackNote);
        vm.SelectedIndex = 3;
        Assert.Empty(vm.StackRows);
        Assert.Equal(Line("str.CT.Stack.NotChosen"), vm.StackNote);

        vm.SelectedIndex = 0;
        dump.Info = new TraceInfo { Allocated = true, Quiesced = true, Gen = 8, Written = 14, FirstValid = 0, QpcFreq = 1_000_000 };
        dump.NamesGen = 8;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Null(vm.Trace!.Stacks);
        Assert.Empty(vm.StackRows);
        Assert.Equal("", vm.StackNote);
    }

    [Fact]
    public async Task Copy_gives_CEs_module_form_whatever_the_Address_setting_says()
    {
        Assert.Equal("\"Game.exe\"+1234", CallTraceViewModel.FrameCopyText(JumpStack[0]));
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = await StackVm(StackDump(), platform);
        vm.SelectedIndex = 0;
        foreach (var format in Enum.GetValues<AddressFormat>())
        {
            vm.SelectedAddressFormatIndex = (int)format;
            await vm.CopyFrameCommand.ExecuteAsync(vm.StackRows[0]);
            Assert.Equal("\"Game.exe\"+1234", platform.LastClipboard);
            Assert.Equal(Line("str.CT.Stack.Copied", "\"Game.exe\"+1234"), vm.StatusText);
            await vm.CopyFrameCommand.ExecuteAsync(vm.StackRows[6]);
            Assert.Equal("\"Cafe.dll\"+2000", platform.LastClipboard);   // CE's name for the module
            await vm.CopyFrameCommand.ExecuteAsync(vm.StackRows[7]);
            Assert.Equal("0x2A0000010", platform.LastClipboard);         // no module: this run's address
        }

        // Nothing reached the clipboard: the status says so, not that it was copied.
        var none = await StackVm(StackDump());
        none.SelectedIndex = 0;
        await none.CopyFrameCommand.ExecuteAsync(none.StackRows[0]);
        Assert.Equal(Line("str.CT.Stack.CopyFailed", "\"Game.exe\"+1234"), none.StatusText);
    }

    /// <summary>Call 0's nearest frame as its row carries it; the rows themselves are pinned by the tests above.</summary>
    private static StackFrameRow GameRow() => new() { Index = 0, Abs = 0x140001234, CopyText = "\"Game.exe\"+1234" };

    [Fact]
    public async Task Asm_moves_CEs_disassembler_to_the_frames_absolute_address()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = await StackVm(StackDump(), aobMaker: new UE5DumpUI.Helpers.AobMakerStatus(bridge));
        vm.SelectedAddressFormatIndex = (int)AddressFormat.ModuleOffset;   // the setting does not change what is sent
        await vm.AsmFrameCommand.ExecuteAsync(GameRow());
        Assert.Equal("140001234", bridge.LastAsm);   // the view sends 0x140001234; AobMakerActions strips the 0x (M2)
        Assert.EndsWith("@ 0x140001234", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Asm_is_refused_for_a_trace_from_an_earlier_connection_and_Copy_still_works()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = await StackVm(StackDump(), platform, new UE5DumpUI.Helpers.AobMakerStatus(bridge));
        vm.ClearOnDisconnect();

        await vm.AsmFrameCommand.ExecuteAsync(GameRow());
        Assert.Null(bridge.LastAsm);
        Assert.Equal(Line("str.CT.Stack.AsmOldTrace"), vm.StatusText);

        // The module form names the same code in any run of the game.
        await vm.CopyFrameCommand.ExecuteAsync(GameRow());
        Assert.Equal("\"Game.exe\"+1234", platform.LastClipboard);
    }

    [Fact]
    public void The_Call_stack_tab_sits_between_Call_and_Parameters_with_a_compiled_unsorted_grid()
    {
        var doc = PanelAxaml();
        Assert.Equal(new[] { "{StaticResource str.CT.Tab.Call}", "{StaticResource str.CT.Tab.Stack}",
                             "{StaticResource str.CT.Tab.Params}" },
                     doc.Descendants(Av + "TabItem").Select(t => (string?)t.Attribute("Header")));
        var tab = doc.Descendants(Av + "TabItem")
                     .Single(t => (string?)t.Attribute("Header") == "{StaticResource str.CT.Tab.Stack}");
        Assert.Contains(tab.Descendants(Av + "TextBlock"), e => (string?)e.Attribute("Text") == "{Binding StackNote}");

        var grid = tab.Descendants(Av + "DataGrid").Single();
        Assert.Equal("{Binding StackRows}", (string?)grid.Attribute("ItemsSource"));
        // The reflection-based column sort is an AOT trap, and a stack's order is what it says.
        Assert.Equal("False", (string?)grid.Attribute("CanUserSortColumns"));
        Assert.Equal("True", (string?)grid.Attribute("IsReadOnly"));
        var columns = grid.Elements(Av + "DataGrid.Columns").Elements().ToList();
        Assert.NotEmpty(columns);
        foreach (var c in columns)
        {
            Assert.Equal(Av + "DataGridTemplateColumn", c.Name);
            Assert.Equal("vm:StackFrameRow", (string?)c.Descendants(Av + "DataTemplate").Single().Attribute(Xaml + "DataType"));
        }
        // [CT-STACK-WHERE-WIDTH] Where is the one star column: it takes what a wide pane leaves, and its 420 floor keeps a
        // narrow pane scrolling sideways instead of squeezing it (CallTraceColumnsTests lays both out).
        var star = Assert.Single(columns, c => ((string?)c.Attribute("Width") ?? "").Contains('*'));
        Assert.Equal("{StaticResource str.CT.Stack.Col.Where}", (string?)star.Attribute("Header"));
        Assert.Equal("420", (string?)star.Attribute("MinWidth"));
        foreach (var path in new[] { "Index", "Address", "Where" })
            Assert.Contains(grid.Descendants(Av + "TextBlock"), e => (string?)e.Attribute("Text") == "{Binding " + path + "}");

        var copy = grid.Descendants(Av + "Button").Single(b => (string?)b.Attribute("Command") == PanelVm + "CopyFrameCommand}");
        Assert.Equal("{Binding}", (string?)copy.Attribute("CommandParameter"));
        var asm = grid.Descendants(Av + "Button").Single(b => (string?)b.Attribute("Command") == PanelVm + "AsmFrameCommand}");
        Assert.Equal("{Binding}", (string?)asm.Attribute("CommandParameter"));
        Assert.Equal(PanelVm + "LiveFuncs.AobMaker.IsAvailable}", (string?)asm.Attribute("IsEnabled"));
    }
}
