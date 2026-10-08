namespace UE5DumpUI.Models;

/// <summary>
/// [LIVEFUNCS-STEP2] A loaded trace's parameter snapshots: every slot the DLL kept, joined to its call by the entry
/// record's sequence number, with the arms -- the loads of each chosen function -- whose layouts decoded them. Read
/// before the trace is released (the DLL frees the rings with it), stored on the <see cref="CallTrace"/>.
/// </summary>
public sealed class CallTraceSnapshots
{
    private readonly Dictionary<int, SnapSlot> _entry = new();
    private readonly Dictionary<int, SnapSlot> _after = new();
    private readonly Dictionary<int, SnapArm> _arms = new();

    public IReadOnlyList<SnapArm> Arms { get; }
    public IReadOnlyList<SnapRingInfo> Rings { get; }
    /// <summary>Slots whose call's entry record the trace's ring no longer kept: the DLL left them out.</summary>
    public ulong Orphans { get; }
    /// <summary>Slots whose call the loaded trace does not hold: none expected after the DLL's own orphan filter.</summary>
    public int Unjoined { get; private set; }

    private CallTraceSnapshots(IReadOnlyList<SnapArm> arms, IReadOnlyList<SnapRingInfo> rings, ulong orphans)
    {
        Arms = arms;
        Rings = rings;
        Orphans = orphans;
        foreach (var a in arms) _arms[a.Index] = a;
    }

    /// <summary>Calls with a copy of their parameters (at the call, after it, or both).</summary>
    public int CallsWithParams => _entry.Keys.Union(_after.Keys).Count();

    public bool Has(int call) => _entry.ContainsKey(call) || _after.ContainsKey(call);

    private int[]? _calls;
    /// <summary>The calls with a copy, in call order: the list "Only calls with parameters" shows.</summary>
    public IReadOnlyList<int> Calls => _calls ??= _entry.Keys.Union(_after.Keys).OrderBy(c => c).ToArray();
    public SnapSlot? EntryOf(int call) => _entry.TryGetValue(call, out var s) ? s : null;
    public SnapSlot? AfterOf(int call) => _after.TryGetValue(call, out var s) ? s : null;
    /// <summary>The arm that wrote <paramref name="slot"/>: its layout decodes it, whatever another load's says.</summary>
    public SnapArm? ArmOf(SnapSlot slot) => _arms.TryGetValue(slot.Arm, out var a) ? a : null;

    /// <summary>Joins each slot to its call in <paramref name="trace"/>.</summary>
    public static CallTraceSnapshots Join(CallTrace trace, IReadOnlyList<SnapArm> arms, IReadOnlyList<SnapRingInfo> rings,
                                          IEnumerable<SnapSlot> slots, ulong orphans)
    {
        var s = new CallTraceSnapshots(arms, rings, orphans);
        foreach (var slot in slots)
        {
            int call = trace.FindBySeq(slot.EntrySeq);
            if (call < 0) { s.Unjoined++; continue; }
            (slot.IsAfter ? s._after : s._entry)[call] = slot;
        }
        return s;
    }
}

/// <summary>
/// [LIVEFUNCS-STEP3] A loaded trace's native stacks: every stack slot the DLL kept, joined to its call by the entry
/// record's sequence number. Kept apart from <see cref="CallTraceSnapshots"/> (the design's D15): a stack is not a
/// parameter copy, so it never counts where a call is said to have parameters, and a trace that took stacks and no
/// parameters still has these. Read before the trace is released, as the parameters are.
/// </summary>
public sealed class CallTraceStacks
{
    private readonly Dictionary<int, StackSlot> _slots = new();

    /// <summary>The stack rings as the DLL armed them: what the budget left out and what the captures cost.</summary>
    public StackInfo? Info { get; }
    /// <summary>Slots whose call's entry record the trace's ring no longer kept: the DLL left them out.</summary>
    public ulong Orphans { get; }
    /// <summary>Slots whose call the loaded trace does not hold: none expected after the DLL's own orphan filter.</summary>
    public int Unjoined { get; private set; }

    private CallTraceStacks(StackInfo? info, ulong orphans)
    {
        Info = info;
        Orphans = orphans;
    }

    public bool Has(int call) => _slots.ContainsKey(call);
    public StackSlot? StackOf(int call) => _slots.TryGetValue(call, out var s) ? s : null;

    private int[]? _calls;
    /// <summary>The calls with a stack, in call order.</summary>
    public IReadOnlyList<int> Calls => _calls ??= _slots.Keys.OrderBy(c => c).ToArray();

    /// <summary>Joins each slot to its call in <paramref name="trace"/>.</summary>
    public static CallTraceStacks Join(CallTrace trace, IEnumerable<StackSlot> slots, ulong orphans, StackInfo? info)
        => new(info, orphans);
}
