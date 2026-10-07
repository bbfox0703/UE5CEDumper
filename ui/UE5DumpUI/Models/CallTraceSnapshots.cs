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
