using System.Runtime.CompilerServices;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LIVEFUNCS-TIMELINE-2026-10-04] The call trace's wire records and the call tree built from them. The ring itself
/// (what the DLL writes, and when) is dll_core_test's; this is what the UI makes of the records it is sent.
/// </summary>
public class CallTraceBuilderTests
{
    private const ulong R = TraceRecord.ReturnBit;

    private static TraceRecord Entry(ulong seq, ulong ticks, ulong func, ulong obj = 0, uint tid = 1, uint flags = 0)
        => new(seq, ticks, func, obj, tid, flags);

    private static TraceRecord Return(ulong seq, ulong ticks, ulong entrySeq, uint tid = 1)
        => new(seq | R, ticks, entrySeq, 0, tid, 0);

    private static readonly TraceInfo Info = new() { QpcFreq = 1_000_000 };

    [Fact]
    public void A_record_is_the_DLLs_40_bytes()
    {
        Assert.Equal(40, Unsafe.SizeOf<TraceRecord>());
        Assert.Equal(CallTraceBuilder.RecordSize, Unsafe.SizeOf<TraceRecord>());
    }

    [Fact]
    public void Decode_reads_the_fields_little_endian_at_their_offsets()
    {
        var bytes = new byte[40];
        BitConverter.GetBytes(5UL | R).CopyTo(bytes, 0);
        BitConverter.GetBytes(123456789UL).CopyTo(bytes, 8);
        BitConverter.GetBytes(0x1B2C3D40UL).CopyTo(bytes, 16);
        BitConverter.GetBytes(0x7FF0_0000_1000UL).CopyTo(bytes, 24);
        BitConverter.GetBytes(4242U).CopyTo(bytes, 32);
        BitConverter.GetBytes(1U).CopyTo(bytes, 36);

        var r = Assert.Single(CallTraceBuilder.Decode(bytes));
        Assert.True(r.IsReturn);
        Assert.Equal(5UL, r.Seq);
        Assert.Equal(123456789UL, r.Ticks);
        Assert.Equal(0x1B2C3D40UL, r.A);
        Assert.Equal(0x7FF0_0000_1000UL, r.B);
        Assert.Equal(4242U, r.Tid);
        Assert.Equal(1U, r.Flags);
    }

    [Fact]
    public void Decode_refuses_a_page_that_is_not_whole_records()
    {
        Assert.Throws<InvalidDataException>(() => CallTraceBuilder.Decode(new byte[41]));
        Assert.Empty(CallTraceBuilder.Decode(Array.Empty<byte>()));
    }

    [Fact]
    public void Nested_calls_become_parent_and_child_with_their_durations()
    {
        var t = CallTraceBuilder.Build(new[]
        {
            Entry(0, 1000, 0xF1, 0xB1), Entry(1, 1100, 0xF2, 0xB2), Return(2, 1350, 1), Return(3, 2000, 0),
        }, Info);

        Assert.Equal(2, t.Count);
        Assert.Equal(new[] { 0 }, t.Roots);
        Assert.Equal(-1, t.Parent[0]);
        Assert.Equal(0, t.Parent[1]);
        Assert.Equal(1, t.Depth[1]);
        Assert.Equal(1, t.FirstChild[0]);
        Assert.Equal(1, t.ChildCount[0]);
        Assert.Equal(1000.0, t.DurationUs(0));   // 1000 ticks at 1 MHz
        Assert.Equal(250.0, t.DurationUs(1));
        Assert.Equal(0.1, t.StartMs(1), 6);
        Assert.Equal(0xB2UL, t.Obj[1]);
    }

    [Fact]
    public void A_call_takes_its_caller_from_its_own_thread()
    {
        var t = CallTraceBuilder.Build(new[]
        {
            Entry(0, 10, 0xA, tid: 1), Entry(1, 20, 0xB, tid: 2), Entry(2, 30, 0xC, tid: 1),
            Return(3, 40, 2, tid: 1), Return(4, 50, 1, tid: 2), Return(5, 60, 0, tid: 1),
        }, Info);

        Assert.Equal(0, t.Parent[2]);    // C ran inside A, not inside B on the other thread
        Assert.Equal(-1, t.Parent[1]);
        Assert.Equal(new[] { 0, 1 }, t.Roots);
    }

    [Fact]
    public void Calls_still_open_above_a_return_never_returned()
    {
        // A > B > C, then A returns: an exception unwound B and C.
        var t = CallTraceBuilder.Build(new[]
        {
            Entry(0, 10, 0xA), Entry(1, 20, 0xB), Entry(2, 30, 0xC), Return(3, 40, 0), Entry(4, 50, 0xD),
        }, Info);

        Assert.True(t.Returned[0]);
        Assert.False(t.Returned[1]);
        Assert.False(t.Returned[2]);
        Assert.Null(t.DurationUs(2));
        Assert.Equal(-1, t.Parent[3]);   // D comes after A, not inside the unwound C
        Assert.Equal(new[] { 0, 3 }, t.Roots);
    }

    [Fact]
    public void A_return_from_before_the_window_closes_what_its_thread_had_open()
    {
        // The ring kept seq 10 on. X entered at 10, then the call around it (entry 5, overwritten) returned at 11:
        // X was inside it and never returned. Y after it is a root.
        var t = CallTraceBuilder.Build(new[]
        {
            Entry(10, 100, 0xA), Return(11, 110, 5), Entry(12, 120, 0xB),
        }, Info);

        Assert.Equal(1, t.ReturnsBeforeWindow);
        Assert.False(t.Returned[0]);
        Assert.Equal(-1, t.Parent[1]);
        Assert.Equal(new[] { 0, 1 }, t.Roots);
    }

    [Fact]
    public void Children_keep_their_call_order()
    {
        var t = CallTraceBuilder.Build(new[]
        {
            Entry(0, 1, 0xA),
            Entry(1, 2, 0x1), Return(2, 3, 1),
            Entry(3, 4, 0x2), Return(4, 5, 3),
            Entry(5, 6, 0x3), Return(6, 7, 5),
            Return(7, 8, 0),
        }, Info);

        var kids = new List<int>();
        for (int c = t.FirstChild[0]; c >= 0; c = t.NextSibling[c]) kids.Add(c);
        Assert.Equal(new[] { 1, 2, 3 }, kids);
        Assert.Equal(3, t.ChildCount[0]);
        Assert.Equal(new[] { 0x1UL, 0x2UL, 0x3UL }, kids.Select(k => t.Func[k]).ToArray());
    }

    [Fact]
    public void Time_counts_from_the_earliest_clock_reading_not_the_first_record()
    {
        // The DLL takes a sequence number, then reads the clock: two threads can take 0 and 1 and read 105 and 100.
        // Counted from record 0, call 1 would start at (100 - 105) as unsigned -- about 1.8e15 ms.
        var t = CallTraceBuilder.Build(new[]
        {
            Entry(0, 105, 0xA, tid: 1), Entry(1, 100, 0xB, tid: 2), Return(2, 300, 1, tid: 2), Return(3, 200, 0, tid: 1),
        }, Info);

        Assert.Equal(100UL, t.OriginTicks);
        Assert.Equal(0.005, t.StartMs(0), 9);
        Assert.Equal(0.0, t.StartMs(1), 9);
        Assert.Equal(200e-6, t.WindowSeconds, 9);   // the earliest reading (100) to the latest (300)
    }

    [Fact]
    public void Records_out_of_order_are_put_back_in_sequence_first()
    {
        var t = CallTraceBuilder.Build(new[]
        {
            Return(3, 40, 0), Entry(1, 20, 0xB), Entry(0, 10, 0xA), Return(2, 30, 1),
        }, Info);

        Assert.Equal(new[] { 0UL, 1UL }, t.Seq);
        Assert.Equal(0, t.Parent[1]);
        Assert.True(t.Returned[0] && t.Returned[1]);
    }

    [Fact]
    public void Names_are_what_the_DLL_resolved_and_a_dead_address_shows_as_an_address()
    {
        var t = CallTraceBuilder.Build(new[] { Entry(0, 1, 0xF1, 0xB1), Entry(1, 2, 0xF2, 0xB2), Entry(2, 3, 0xF3) },
            Info,
            new[]
            {
                new TraceFuncName { Addr = 0xF1, Live = true, ClassName = "Pawn", FuncName = "Jump" },
                new TraceFuncName { Addr = 0xF2, Live = false },
            },
            new[]
            {
                new TraceObjName { Addr = 0xB1, Live = true, Name = "BP_Hero_C_0", ClassName = "BP_Hero_C" },
                new TraceObjName { Addr = 0xB2, Live = false },
            });

        Assert.Equal("Jump", t.FuncName(0));
        Assert.Equal("Pawn", t.ClassName(0));
        Assert.Equal("BP_Hero_C_0", t.ObjName(0));
        Assert.False(t.ObjStale(0));
        Assert.Equal("0xF2", t.FuncName(1));
        Assert.Equal("0xB2", t.ObjName(1));
        Assert.True(t.ObjStale(1));
        Assert.Equal("", t.ObjName(2));      // no object: a static call
        Assert.False(t.ObjStale(2));
    }

    // [TRACE-UI-LOAD-MEMORY] D2: a page goes from its base64 straight into the caller's window -- no byte[] and no
    // TraceRecord[] of its own -- whether the base64 arrives as UTF-8 (the pipe's JSON) or as chars.
    private static TraceRecord[] Three() => new[]
    {
        Entry(10, 100, 0xA1, 0xB1), Return(11, 110, 10), Entry(12, 120, 0xA2, 0, tid: 9, flags: TraceRecord.ScopeRootFlag),
    };

    [Fact]
    public void DecodeInto_writes_a_page_straight_into_the_callers_window()
    {
        var recs = Three();
        string b64 = Convert.ToBase64String(System.Runtime.InteropServices.MemoryMarshal.AsBytes(recs.AsSpan()));
        var window = new TraceRecord[5];

        int fromChars = CallTraceBuilder.DecodeInto(b64.AsSpan(), window.AsSpan(1));
        Assert.Equal(3, fromChars);
        Assert.Equal(recs, window[1..4]);
        Assert.Equal(default, window[0]);
        Assert.Equal(default, window[4]);

        var window2 = new TraceRecord[3];
        int fromUtf8 = CallTraceBuilder.DecodeInto(System.Text.Encoding.ASCII.GetBytes(b64), window2.AsSpan());
        Assert.Equal(3, fromUtf8);
        Assert.Equal(recs, window2);

        Assert.Equal(0, CallTraceBuilder.DecodeInto(ReadOnlySpan<char>.Empty, window.AsSpan()));
    }

    [Fact]
    public void DecodeInto_refuses_a_page_that_is_not_whole_records_or_does_not_fit()
    {
        string partial = Convert.ToBase64String(new byte[41]);
        Assert.Throws<InvalidDataException>(() => CallTraceBuilder.DecodeInto(partial.AsSpan(), new TraceRecord[2]));
        Assert.Throws<InvalidDataException>(() =>
            CallTraceBuilder.DecodeInto(System.Text.Encoding.ASCII.GetBytes(partial), new TraceRecord[2]));
        string three = Convert.ToBase64String(System.Runtime.InteropServices.MemoryMarshal.AsBytes(Three().AsSpan()));
        Assert.Throws<InvalidDataException>(() => CallTraceBuilder.DecodeInto(three.AsSpan(), new TraceRecord[2]));
        Assert.Throws<InvalidDataException>(() =>
            CallTraceBuilder.DecodeInto(System.Text.Encoding.ASCII.GetBytes(three), new TraceRecord[2]));
        Assert.Throws<InvalidDataException>(() => CallTraceBuilder.DecodeInto("not base64!".AsSpan(), new TraceRecord[2]));
        // Six records into room for three: 120 bytes is a whole number of base64 blocks AND of records, so a decoder
        // that stopped there would hand back three records that look whole. Too small is refused, not truncated.
        var six = Three().Concat(Three()).ToArray();
        string sixB64 = Convert.ToBase64String(System.Runtime.InteropServices.MemoryMarshal.AsBytes(six.AsSpan()));
        Assert.Throws<InvalidDataException>(() => CallTraceBuilder.DecodeInto(sixB64.AsSpan(), new TraceRecord[3]));
        Assert.Throws<InvalidDataException>(() =>
            CallTraceBuilder.DecodeInto(System.Text.Encoding.ASCII.GetBytes(sixB64), new TraceRecord[3]));
        Assert.Throws<InvalidDataException>(() =>
            CallTraceBuilder.DecodeInto(System.Text.Encoding.ASCII.GetBytes("not base64!"), new TraceRecord[2]));
    }

    // [TRACE-UNLOADED-NAMES] D1: a function the game unloaded before Stop keeps the name read at its first call.
    private static CallTrace UnloadedSample() => CallTraceBuilder.Build(new[]
        {
            Entry(0, 1, 0xF1), Entry(1, 2, 0xF1), Entry(2, 3, 0xF1),   // live, 3 calls
            Entry(3, 4, 0xF2),                                          // unloaded, named from its first call
            Entry(4, 5, 0xF3), Entry(5, 6, 0xF3),                       // gone and never read
        },
        Info,
        new[]
        {
            new TraceFuncName { Addr = 0xF1, Live = true, ClassName = "Pawn", FuncName = "Jump" },
            new TraceFuncName { Addr = 0xF2, Live = false, Unloaded = true, ClassName = "WBP_Inventory_C", FuncName = "OnOpen" },
            new TraceFuncName { Addr = 0xF3, Live = false },
        });

    [Fact]
    public void An_unloaded_function_shows_the_name_from_its_first_call_and_is_marked()
    {
        var t = UnloadedSample();
        Assert.Equal("OnOpen", t.FuncName(3));
        Assert.Equal("WBP_Inventory_C", t.ClassName(3));
        Assert.True(t.FuncUnloaded(3));
        Assert.False(t.FuncUnloaded(0));
        Assert.Equal("0xF3", t.FuncName(4));   // never read: an address, as before
        Assert.False(t.FuncUnloaded(4));
    }

    [Fact]
    public void An_address_that_held_two_functions_is_marked_reused()
    {
        // Review DLL-3: another function took the address during the recording; its calls and the first one's share it.
        var t = CallTraceBuilder.Build(new[] { Entry(0, 1, 0xF1), Entry(1, 2, 0xF2) }, Info, new[]
        {
            new TraceFuncName { Addr = 0xF1, Live = true, ClassName = "Pawn", FuncName = "Jump" },
            new TraceFuncName { Addr = 0xF2, Live = true, Reused = true, ClassName = "WBP_Map_C", FuncName = "OnTile" },
        });
        Assert.False(t.FuncReused(0));
        Assert.True(t.FuncReused(1));
        Assert.Equal("OnTile", t.FuncName(1));
    }

    [Fact]
    public void The_trace_counts_its_unloaded_and_its_unnamed_functions_and_their_calls()
    {
        var t = UnloadedSample();
        Assert.Equal(3, t.DistinctFuncs);
        Assert.Equal(1, t.UnloadedFuncs);
        Assert.Equal(1L, t.UnloadedCalls);
        Assert.Equal(1, t.UnnamedFuncs);
        Assert.Equal(2L, t.UnnamedCalls);
    }

    [Theory]
    [InlineData(0L, 6_710_886L, "0%")]
    [InlineData(5_387L, 6_710_886L, "0.08%")]
    [InlineData(1L, 6_710_886L, "<0.01%")]
    [InlineData(1L, 3L, "33.33%")]
    [InlineData(3L, 3L, "100%")]
    [InlineData(0L, 0L, "0%")]
    // Review UI-3: 50 in a million is exactly 0.005%, which banker's rounding takes to 0; 999,960 in a million rounds
    // to 100 while 40 calls are named. Neither may claim the clean (or the total) answer.
    [InlineData(50L, 1_000_000L, "<0.01%")]
    [InlineData(999_960L, 1_000_000L, ">99.99%")]
    public void The_share_with_no_name_reads_as_a_percentage_at_any_size(long part, long whole, string expected)
        => Assert.Equal(expected, CallTrace.ShareText(part, whole));
}
