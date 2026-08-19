using System.Collections.Generic;

namespace UE5DumpUI.Models;

/// <summary>
/// One slot of a Snapshot Group Match query (2–4 per query). Reuses the Value
/// Search enums so the UI can bind the same DataType / ScanType pickers; the
/// store translates these to the pure <c>Services.GroupMatch</c> matcher. Numeric
/// scope only (<see cref="ValueScanDataType.NumericNoByte"/> /
/// <see cref="ValueScanDataType.NumericAll"/>); the 8 numeric predicates
/// (Exact/Bigger/Smaller/Between absolute + Changed/Unchanged/Increased/Decreased
/// relative). See docs/snapshot-group-match-spec.md.
/// </summary>
public sealed class SnapshotGroupSlotInput
{
    public ValueScanDataType DataType { get; set; } = ValueScanDataType.NumericNoByte;
    public ValueScanType     ScanType { get; set; } = ValueScanType.Exact;
    /// <summary>Target value for an absolute predicate (Exact/Bigger/Smaller/Between).
    /// Ignored for relative predicates (Changed/Unchanged/Increased/Decreased).</summary>
    public string Value  { get; set; } = "";
    /// <summary>Between upper bound. Ignored otherwise.</summary>
    public string Value2 { get; set; } = "";
    /// <summary>How a fractional float/double target is reduced to the displayed
    /// integer before compare (Round/Trunc/Ceil). All slots in a query share the
    /// panel mode (set from <see cref="SnapshotGroupQuery.RoundMode"/>).</summary>
    public FloatRoundMode RoundMode { get; set; } = FloatRoundMode.Round;
}

/// <summary>
/// A Snapshot Group Match query: find objects in the captured snapshot(s) that
/// hold ALL of N values at distinct numeric offsets. <see cref="SnapshotIds"/>
/// length 1 = Mode A (single-snapshot, absolute predicates); ≥ 2 = Mode B
/// (cross-snapshot temporal comparison, relative predicates — added in S4).
/// </summary>
public sealed class SnapshotGroupQuery
{
    /// <summary>Oldest → newest. One = Mode A; ≥ 2 = Mode B (S4).</summary>
    public List<long> SnapshotIds { get; set; } = new();
    public List<SnapshotGroupSlotInput> Slots { get; set; } = new();
    /// <summary>Deep (opt-in, default off): also match an object's NESTED container /
    /// struct-array element values (the captured <c>array_field</c> rows, e.g.
    /// <c>SaveSlotList[1]…Tunes[2]</c>), folded into the owning object's block. Off =
    /// the object's direct fields only. Mirrors the live Group Scan's Deep toggle.</summary>
    public bool Deep { get; set; }
    /// <summary>Per-game class denylist (noise picker) — excluded before matching.</summary>
    public HashSet<string>? ExcludedClasses { get; set; }
    public int MaxResults { get; set; } = Constants.DefaultMaxQueryRows;
    /// <summary>Cross-snapshot identity join (Mode B). Ignored for Mode A.</summary>
    public SpcJoinMode JoinMode { get; set; } = SpcJoinMode.InSession;
    /// <summary>Per-panel rounding mode applied to every slot — how a fractional
    /// float/double target is reduced to the displayed integer before compare.</summary>
    public FloatRoundMode RoundMode { get; set; } = FloatRoundMode.Round;
}

/// <summary>
/// Result of a Snapshot Group Match — reuses <see cref="GroupCandidate"/> /
/// <see cref="GroupSlotMatch"/> so the Snapshot panel can bind the same
/// master-detail template the live Value Search group mode uses. Handoff
/// addresses are the captured (same-session) obj_addr, like the Diff / SPC rows.
/// </summary>
public sealed class SnapshotGroupResult
{
    public List<GroupCandidate> Candidates { get; set; } = new();
    /// <summary>Total objects that matched (may exceed <see cref="Candidates"/> when capped).</summary>
    public int  Total          { get; set; }
    public bool Truncated      { get; set; }
    /// <summary>
    /// A slot matched more fields than <see cref="PerSlotCap"/> kept, so a row's
    /// witness list is a page (audit #5 AF13). Distinct from
    /// <see cref="Truncated"/>, which is about ROWS: every candidate here is real and
    /// complete as an object; what is capped is the list of FIELDS that satisfied a
    /// slot. Mirrors the DLL's <c>per_slot_cap_hit</c>, which the live path has
    /// carried since AE13 while this one discarded it.
    /// </summary>
    public bool PerSlotCapHit  { get; set; }
    /// <summary>The per-slot cap actually applied. Reported because the snapshot path
    /// uses the compile-time default while Value Search lets the user change its own
    /// (audit #5 AF12), so "which cap bit me?" has two possible answers.</summary>
    public int  PerSlotCap     { get; set; }
    /// <summary>Objects scanned (the snapshot's object count after denylist).</summary>
    public int  ScannedObjects { get; set; }
    /// <summary>Non-null on a validation failure (bad slot count, unsupported predicate, …).</summary>
    public string? Error       { get; set; }
}
