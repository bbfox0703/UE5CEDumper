using System.Collections.Generic;

namespace UE5DumpUI.Models;

/// <summary>
/// One cell of the SPC group predicate MATRIX — the predicate applied to a single
/// (value-slot, snapshot) pair. Carries a directional predicate (compared against
/// the previous selected snapshot, exactly like single-value SPC) plus an optional
/// absolute value window for that snapshot. A slot's full
/// <see cref="SpcGroupSlot.Chain"/> is one of these per selected snapshot
/// (oldest → newest). See docs/group-value-scan-spec.md §3.1.
/// </summary>
public sealed class SpcGroupCell
{
    public SpcPredicateKind     Predicate { get; set; } = SpcPredicateKind.Any;
    public SpcAbsolutePredicate Absolute  { get; set; } = new();
}

/// <summary>
/// One value-slot of an SPC group query (2–4 per query): a numeric width scope plus
/// a full per-snapshot predicate <see cref="Chain"/> (one <see cref="SpcGroupCell"/>
/// per selected snapshot, same length / order as
/// <see cref="SpcGroupQuery.SnapshotIds"/>). A field "fits" this slot iff its
/// declared width is in <see cref="Scope"/> AND its value sequence satisfies the
/// chain (reusing <see cref="Services.SpcEngine"/>).
/// </summary>
public sealed class SpcGroupSlot
{
    public ValueScanDataType  Scope { get; set; } = ValueScanDataType.NumericNoByte;
    public List<SpcGroupCell> Chain { get; set; } = new();
}

/// <summary>
/// An SPC GROUP query — find OBJECTS in the captured-snapshot corpus that hold N
/// (2–4) fields, each satisfying its OWN per-snapshot predicate chain across the
/// selected snapshots, at DISTINCT offsets, in any order. The N-snapshot,
/// object-aware generalisation of Snapshot Group Match Mode B (which compares only
/// 2 snapshots first-vs-last). The <c>Orden</c>-reuse seam the group-value-scan
/// spec reserved for SPC (§3.1): run the SDR matcher per object after the SPC
/// intersection load. Pure SQL fetch + C# matcher — zero DLL/pipe change.
/// </summary>
public sealed class SpcGroupQuery
{
    /// <summary>Snapshot ids, ordered oldest → newest. Must be ≥ 2.</summary>
    public List<long> SnapshotIds { get; set; } = new();

    /// <summary>The 2–4 value-slots. Each slot's <see cref="SpcGroupSlot.Chain"/>
    /// must have the same length as <see cref="SnapshotIds"/>.</summary>
    public List<SpcGroupSlot> Slots { get; set; } = new();

    public SpcJoinMode JoinMode { get; set; } = SpcJoinMode.Strict;

    /// <summary>Case-insensitive substring on the class name (empty = all).</summary>
    public string ClassContains { get; set; } = "";
    /// <summary>Case-insensitive substring on the property name (empty = all).</summary>
    public string PropContains { get; set; } = "";

    /// <summary>Max matching candidate OBJECTS returned (default 50,000).</summary>
    public int MaxResults { get; set; } = Constants.DefaultMaxQueryRows;

    /// <summary>Per-game class denylist (noise picker) — skipped at the intersection
    /// load, before the cap. Null / empty = no filtering. Ordinal comparison.</summary>
    public HashSet<string>? ExcludedClasses { get; set; }

    /// <summary>How a fractional float/double target is reduced to the displayed
    /// integer before an absolute compare. Threaded to every <c>SpcEngine.Matches</c>
    /// call; the rounding lives in <c>SpcEngine.AbsMatches</c>.</summary>
    public FloatRoundMode RoundMode { get; set; } = FloatRoundMode.Round;
}

/// <summary>
/// Result of an SPC group query — reuses <see cref="GroupCandidate"/> /
/// <see cref="GroupSlotMatch"/> so the SPC panel binds the same master-detail
/// template as the live Value Search group + Snapshot Group. Handoff addresses are
/// the captured (same-session) obj_addr, like the single-value SPC rows.
/// </summary>
public sealed class SpcGroupResult
{
    public List<GroupCandidate> Candidates { get; set; } = new();
    /// <summary>Total objects that matched (may exceed <see cref="Candidates"/> when capped).</summary>
    public int  Total          { get; set; }
    public bool Truncated      { get; set; }
    // No PerSlotCapHit here, deliberately (checked while fixing audit #5 AF13):
    // SpcGroupQueryAsync runs its own inline matcher that adds EVERY satisfying field
    // to a slot with no cap, so there is no witness list to truncate. The flag exists
    // on SnapshotGroupResult because GroupMatch.Run does cap.
    /// <summary>Objects (identity groups) evaluated after the intersection load.</summary>
    public int  ScannedObjects { get; set; }
    /// <summary>Non-null on a validation failure (bad slot count, chain-length mismatch, …).</summary>
    public string? Error       { get; set; }
    /// <summary>Top-N classes by matched-object count — feeds the noise picker
    /// (built post-query from the result set, like SPC / Diff).</summary>
    public List<ClassNoiseRow> TopContributors { get; } = new();
}
