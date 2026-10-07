using System.Runtime.InteropServices;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// [LIVEFUNCS-TIMELINE-2026-10-04] Turns the ring's records into a call tree. The DLL writes an entry record when a
/// call enters ProcessEvent and a return record, pointing back at the entry, when it returns; nesting and durations
/// are worked out here, after Stop, not kept on the game's hot path (the plan's TR3).
/// </summary>
public static class CallTraceBuilder
{
    public const int RecordSize = 40;

    /// <summary>The records of one <c>pe_trace_get</c> page. The DLL ships whole 40-byte slots; anything else is a
    /// protocol mismatch, not data to guess at.</summary>
    public static TraceRecord[] Decode(byte[] data)
    {
        if (data.Length % RecordSize != 0)
            throw new InvalidDataException($"A trace page of {data.Length} bytes is not whole {RecordSize}-byte records.");
        return MemoryMarshal.Cast<byte, TraceRecord>(data).ToArray();
    }

    /// <summary>[TRACE-UI-LOAD-MEMORY] One page's base64 -- the pipe's UTF-8 -- decoded straight into
    /// <paramref name="into"/>, the load's window, with no array of its own. Returns the records written. A page that
    /// is not base64, not whole records, or larger than <paramref name="into"/> is a protocol mismatch, as in
    /// <see cref="Decode"/>.</summary>
    public static int DecodeInto(ReadOnlySpan<byte> base64Utf8, Span<TraceRecord> into)
    {
        var st = System.Buffers.Text.Base64.DecodeFromUtf8(base64Utf8, MemoryMarshal.AsBytes(into), out _, out int written);
        if (st != System.Buffers.OperationStatus.Done)
            throw new InvalidDataException(
                $"A trace page of {base64Utf8.Length} base64 bytes does not decode into {into.Length} records ({st}).");
        return WholeRecords(written);
    }

    /// <summary>The same from chars: a reply whose value is a string.</summary>
    public static int DecodeInto(ReadOnlySpan<char> base64, Span<TraceRecord> into)
    {
        if (base64.IsEmpty) return 0;
        if (!Convert.TryFromBase64Chars(base64, MemoryMarshal.AsBytes(into), out int written))
            throw new InvalidDataException(
                $"A trace page of {base64.Length} base64 chars is not base64, or does not fit {into.Length} records.");
        return WholeRecords(written);
    }

    private static int WholeRecords(int bytes) => bytes % RecordSize == 0
        ? bytes / RecordSize
        : throw new InvalidDataException($"A trace page of {bytes} bytes is not whole {RecordSize}-byte records.");

    /// <summary>
    /// Builds the tree. Each thread keeps a stack of its open calls: an entry's caller is the top of its thread's
    /// stack. A return closes its call, and any call still open above it on that thread never returned (an
    /// exception unwound it). A return whose entry is older than the kept window ends a call that began before it,
    /// and every call still open on that thread was inside that call, so they are closed as never returned too.
    /// </summary>
    public static CallTrace Build(ReadOnlySpan<TraceRecord> records, TraceInfo info,
                                  IEnumerable<TraceFuncName>? funcs = null, IEnumerable<TraceObjName>? objs = null)
    {
        TraceRecord[]? sorted = null;
        for (int k = 1; k < records.Length; k++)
        {
            if (records[k].Seq <= records[k - 1].Seq)
            {
                sorted = records.ToArray();
                Array.Sort(sorted, (x, y) => x.Seq.CompareTo(y.Seq));
                break;
            }
        }
        ReadOnlySpan<TraceRecord> recs = sorted ?? records;

        // The time base is the earliest clock reading, not the first record's: the DLL takes a sequence number and then
        // reads the clock, so two threads can take numbers in one order and read the clock in the other, and an
        // unsigned difference from a later origin wraps to about 1.8e15 ms (review F4).
        int calls = 0;
        ulong earliest = ulong.MaxValue, latest = 0;
        foreach (ref readonly var r in recs)
        {
            if (!r.IsReturn) calls++;
            if (r.Ticks < earliest) earliest = r.Ticks;
            if (r.Ticks > latest) latest = r.Ticks;
        }

        var t = new CallTrace(calls)
        {
            OriginTicks = recs.Length > 0 ? earliest : 0,
            LastTicks = recs.Length > 0 ? latest : 0,
            QpcFreq = info.QpcFreq == 0 ? 1 : info.QpcFreq,
            Info = info,
        };
        var stacks = new Dictionary<uint, List<int>>();
        int n = 0, beforeWindow = 0;

        foreach (ref readonly var r in recs)
        {
            if (!stacks.TryGetValue(r.Tid, out var stack)) stacks[r.Tid] = stack = new List<int>();
            if (!r.IsReturn)
            {
                int i = n++;
                t.Seq[i] = r.Seq;
                t.StartTicks[i] = r.Ticks;
                t.Func[i] = r.A;
                t.Obj[i] = r.B;
                t.Tid[i] = r.Tid;
                t.Flags[i] = r.Flags;
                int parent = stack.Count > 0 ? stack[^1] : -1;
                t.Parent[i] = parent;
                t.Depth[i] = parent >= 0 ? t.Depth[parent] + 1 : 0;
                t.FirstChild[i] = -1;
                t.NextSibling[i] = -1;
                stack.Add(i);
                continue;
            }

            int call = t.FindBySeq(r.A, n);
            if (call < 0)
            {
                beforeWindow++;
                stack.Clear();
                continue;
            }
            t.EndTicks[call] = r.Ticks;
            t.Returned[call] = true;
            int at = stack.LastIndexOf(call);
            if (at >= 0) stack.RemoveRange(at, stack.Count - at);
        }

        // Children in call order: walk backwards and push each call onto the front of its parent's list.
        var roots = new List<int>();
        for (int i = calls - 1; i >= 0; i--)
        {
            int p = t.Parent[i];
            if (p < 0) continue;
            t.NextSibling[i] = t.FirstChild[p];
            t.FirstChild[p] = i;
            t.ChildCount[p]++;
        }
        for (int i = 0; i < calls; i++) if (t.Parent[i] < 0) roots.Add(i);

        t.Roots = roots.ToArray();
        t.ReturnsBeforeWindow = beforeWindow;
        if (funcs != null) foreach (var f in funcs) t.Funcs[f.Addr] = f;
        if (objs != null) foreach (var o in objs) t.Objs[o.Addr] = o;
        CountNames(t);
        return t;
    }

    /// <summary>[TRACE-UNLOADED-NAMES] How many of the trace's functions, and of their calls, are named only from their
    /// first call (gone when the trace was read) or not at all. One pass over the calls: a dictionary per distinct function,
    /// thousands of them against millions of calls.</summary>
    private static void CountNames(CallTrace t)
    {
        var calls = new Dictionary<ulong, long>();
        for (int i = 0; i < t.Count; i++)
        {
            ref long c = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(calls, t.Func[i], out _);
            c++;
        }
        t.DistinctFuncs = calls.Count;
        foreach (var (addr, n) in calls)
        {
            if (!t.Funcs.TryGetValue(addr, out var f) || !f.Named)
            {
                t.UnnamedFuncs++;
                t.UnnamedCalls += n;
            }
            else if (!f.Live)
            {
                t.UnloadedFuncs++;
                t.UnloadedCalls += n;
            }
        }
    }
}
