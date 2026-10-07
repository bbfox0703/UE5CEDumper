using System.Globalization;

namespace UE5DumpUI.Models;

/// <summary>
/// [LIVEFUNCS-TIMELINE-2026-10-04] A loaded call trace: one entry per CALL, built from the ring's entry and return
/// records by <see cref="Services.CallTraceBuilder"/>. Columns, not row objects: a 512 MB ring holds about 6 million
/// calls, and an object per call would cost more than the ring itself.
/// </summary>
public sealed class CallTrace
{
    public int Count { get; }

    /// <summary>The entry record's sequence number; ascending, so a return finds its call by binary search.</summary>
    public ulong[] Seq { get; }
    public ulong[] StartTicks { get; }
    /// <summary>Valid only where <see cref="Returned"/> is true.</summary>
    public ulong[] EndTicks { get; }
    public bool[] Returned { get; }
    public int[] Parent { get; }
    public int[] Depth { get; }
    public uint[] Tid { get; }
    public ulong[] Func { get; }
    public ulong[] Obj { get; }
    public uint[] Flags { get; }
    /// <summary>Children in call order: the first, then each one's next sibling; -1 ends the list.</summary>
    public int[] FirstChild { get; }
    public int[] NextSibling { get; }
    public int[] ChildCount { get; }
    /// <summary>The calls with no caller in the kept window, in call order.</summary>
    public int[] Roots { get; internal set; } = Array.Empty<int>();

    public ulong OriginTicks { get; init; }
    public ulong QpcFreq { get; init; } = 1;
    public TraceInfo Info { get; init; } = new();
    /// <summary>Returns whose call began before the kept window: the ring overwrote their entry.</summary>
    public int ReturnsBeforeWindow { get; internal set; }

    /// <summary>[TRACE-UNLOADED-NAMES] The trace's distinct functions; those gone when the trace was read (named from their
    /// first call) and those with no name at all, each with its calls. Counted once, after the names are in.</summary>
    public int  DistinctFuncs { get; internal set; }
    public int  UnloadedFuncs { get; internal set; }
    public long UnloadedCalls { get; internal set; }
    public int  UnnamedFuncs  { get; internal set; }
    public long UnnamedCalls  { get; internal set; }

    /// <summary>[LIVEFUNCS-STEP2] The parameter snapshots, when the trace took any; null otherwise.</summary>
    public CallTraceSnapshots? Snapshots { get; internal set; }

    public Dictionary<ulong, TraceFuncName> Funcs { get; } = new();
    public Dictionary<ulong, TraceObjName> Objs { get; } = new();

    public CallTrace(int count)
    {
        Count = count;
        Seq = new ulong[count];
        StartTicks = new ulong[count];
        EndTicks = new ulong[count];
        Returned = new bool[count];
        Parent = new int[count];
        Depth = new int[count];
        Tid = new uint[count];
        Func = new ulong[count];
        Obj = new ulong[count];
        Flags = new uint[count];
        FirstChild = new int[count];
        NextSibling = new int[count];
        ChildCount = new int[count];
    }

    /// <summary>The call whose entry record has this sequence number, or -1.</summary>
    public int FindBySeq(ulong seq, int upTo = -1)
    {
        int lo = 0, hi = (upTo < 0 ? Count : upTo) - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            ulong s = Seq[mid];
            if (s == seq) return mid;
            if (s < seq) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }

    /// <summary>Milliseconds from the first kept record.</summary>
    public double StartMs(int i) => (StartTicks[i] - OriginTicks) * 1000.0 / QpcFreq;

    /// <summary>Microseconds the call took, or null when it never returned in the window.</summary>
    public double? DurationUs(int i) => Returned[i] ? (EndTicks[i] - StartTicks[i]) * 1_000_000.0 / QpcFreq : null;

    public double WindowSeconds => Count == 0 ? 0 : (LastTicks - OriginTicks) / (double)QpcFreq;
    public ulong LastTicks { get; init; }

    // [TRACE-UNLOADED-NAMES] Live, or unloaded and named from its first call: a name either way. Only a function
    // with neither shows its address.
    public string ClassName(int i) => Funcs.TryGetValue(Func[i], out var f) && f.Named ? f.ClassName : "";
    public string FuncName(int i) => Funcs.TryGetValue(Func[i], out var f) && f.Named
        ? f.FuncName : "0x" + Func[i].ToString("X", CultureInfo.InvariantCulture);
    /// <summary>The function was gone when the trace was read (pe_trace_names classifies then, not at Stop): its name
    /// is the one it had at its first call.</summary>
    public bool FuncUnloaded(int i) => Funcs.TryGetValue(Func[i], out var f) && !f.Live && f.Unloaded;
    /// <summary>The address held another function during the recording (review DLL-3): this call may be either one's.</summary>
    public bool FuncReused(int i) => Funcs.TryGetValue(Func[i], out var f) && f.Reused;
    /// <summary>Gone, and another function holds the address now (review UI-6). Its calls are still this one's.</summary>
    public bool FuncRecycled(int i) => Funcs.TryGetValue(Func[i], out var f) && f.Recycled;

    /// <summary>A share of a whole as the status shows it: "0%" only when nothing, "100%" only when all, otherwise up
    /// to two decimals, clamped to "&lt;0.01%" / "&gt;99.99%" so rounding never claims the clean or the total answer
    /// (review UI-3). Invariant, like the trace's other numbers.</summary>
    public static string ShareText(long part, long whole)
    {
        if (part <= 0 || whole <= 0) return "0%";
        if (part >= whole) return "100%";
        double rounded = Math.Round(part * 100.0 / whole, 2);
        if (rounded <= 0) return "<0.01%";
        if (rounded >= 100) return ">99.99%";
        return rounded.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }
    public string ObjName(int i) => Obj[i] == 0 ? ""
        : Objs.TryGetValue(Obj[i], out var o) && o.Live ? o.Name : "0x" + Obj[i].ToString("X", CultureInfo.InvariantCulture);
    public string ObjClass(int i) => Objs.TryGetValue(Obj[i], out var o) && o.Live ? o.ClassName : "";
    /// <summary>The object no longer holds what it held at the call (or was never named): its name is not shown.</summary>
    public bool ObjStale(int i) => Obj[i] != 0 && !(Objs.TryGetValue(Obj[i], out var o) && o.Live);
}
