namespace UE5DumpUI.Models;

/// <summary>
/// Statistical-Process-Control predicate kinds for the experimental SPC Query
/// tab: the type-agnostic DIRECTIONAL set — the user picks how a field's value
/// moved between consecutive snapshots, never a type or a literal value (a literal
/// value window is <see cref="SpcAbsoluteKind"/>). This generalizes CE's "Unknown Initial Value + Increased/Decreased"
/// workflow across N persisted snapshots (and across game restarts). See
/// docs/experimental-snapshot-spc-pivot.md §"Phase B".
/// </summary>
public enum SpcPredicateKind
{
    /// <summary>No constraint. The oldest selected snapshot is always the
    /// baseline (Any) since it has no predecessor to compare against.</summary>
    Any,
    /// <summary>Raw bytes equal to the previous snapshot (type-exact).</summary>
    Unchanged,
    /// <summary>Raw bytes differ from the previous snapshot (type-exact).</summary>
    Changed,
    /// <summary>Numeric value (per the field's declared width) &gt; previous.</summary>
    Increased,
    /// <summary>Numeric value (per the field's declared width) &lt; previous.</summary>
    Decreased,
}

/// <summary>
/// Absolute (value-level) predicate kinds for SPC — applied per snapshot IN ADDITION
/// to the directional chain, BEFORE the result cap. Lets the user pin a real value
/// window (e.g. "now Between 0–500") to cut directional-but-irrelevant noise like UI
/// widget sizes. Mirrors `discrete` + CE's Exact / Between / range scans. Compared on
/// the field's numeric value (per its declared width).
/// </summary>
public enum SpcAbsoluteKind
{
    /// <summary>No value constraint for this snapshot.</summary>
    None,
    /// <summary>numeric == Low.</summary>
    Exact,
    /// <summary>Low ≤ numeric ≤ High.</summary>
    Between,
    /// <summary>numeric ≥ Low.</summary>
    AtLeast,
    /// <summary>numeric ≤ High.</summary>
    AtMost,
}

/// <summary>One snapshot's optional absolute value predicate.</summary>
public sealed class SpcAbsolutePredicate
{
    public SpcAbsoluteKind Kind { get; set; } = SpcAbsoluteKind.None;
    public double Low  { get; set; }
    public double High { get; set; }

    /// <summary>True when this field's numeric value satisfies the predicate.
    /// A non-None predicate on a field with no numeric value (null) fails.</summary>
    public bool Matches(double? value)
    {
        if (Kind == SpcAbsoluteKind.None) return true;
        if (value is not double v) return false;
        return Kind switch
        {
            SpcAbsoluteKind.Exact   => v == Low,
            SpcAbsoluteKind.Between => v >= Low && v <= High,
            SpcAbsoluteKind.AtLeast => v >= Low,
            SpcAbsoluteKind.AtMost  => v <= High,
            _                       => true,
        };
    }
}

/// <summary>
/// Cross-snapshot join mode for SPC. Strict has the lowest false-positive rate
/// but misses runtime-spawned objects across sessions; Loose recovers some of
/// them at the cost of multiple candidates per logical field (the predicate
/// chain filters them down); InSession is exact within one game launch.
/// Identity keys: docs/experimental-snapshot-spc-pivot.md §5.
/// </summary>
public enum SpcJoinMode
{
    /// <summary>(class_fqn, norm_path, prop_name, prop_offset).</summary>
    Strict,
    /// <summary>(class_fqn, outer_chain, prop_name).</summary>
    Loose,
    /// <summary>(class_fqn, gobjects_index, prop_name) — one launch only.</summary>
    InSession,
}

/// <summary>
/// One SPC query: an ordered snapshot sequence (oldest → newest) plus a
/// per-snapshot predicate chain, a join mode and optional name filters. The
/// engine intersects the fields present in ALL listed snapshots under the join
/// key, then keeps those whose value sequence satisfies the predicate chain.
/// </summary>
public sealed class SpcQuery
{
    /// <summary>Snapshot ids, ordered oldest → newest. Must be ≥ 2.</summary>
    public List<long> SnapshotIds { get; set; } = new();

    /// <summary>One predicate per snapshot, same order/length as
    /// <see cref="SnapshotIds"/>. Index 0 is the baseline and is treated as
    /// <see cref="SpcPredicateKind.Any"/> regardless of its value. Predicate
    /// <c>i</c> (i ≥ 1) compares snapshot <c>i</c> against snapshot <c>i-1</c>.</summary>
    public List<SpcPredicateKind> Predicates { get; set; } = new();

    /// <summary>Optional absolute value predicate per snapshot (same order/length as
    /// <see cref="SnapshotIds"/>, or empty for none). Applied in addition to the
    /// directional chain. Index 0 (baseline) may carry one too.</summary>
    public List<SpcAbsolutePredicate> AbsolutePredicates { get; set; } = new();

    public SpcJoinMode JoinMode { get; set; } = SpcJoinMode.Strict;

    /// <summary>Case-insensitive substring on the class name (empty = all).</summary>
    public string ClassContains { get; set; } = "";
    /// <summary>Case-insensitive substring on the property name (empty = all).</summary>
    public string PropContains { get; set; } = "";

    /// <summary>Max matching candidates returned (default 50,000). One extra is
    /// probed to set <see cref="SpcResult.Truncated"/>.</summary>
    public int MaxRows { get; set; } = Constants.DefaultMaxQueryRows;

    /// <summary>Per-game class denylist. Class FQNs in this set are skipped
    /// at the anchor-load step and every subsequent snapshot pass, before the cap
    /// is evaluated. Null / empty = no filtering. Ordinal comparison.</summary>
    public HashSet<string>? ExcludedClasses { get; set; }

    /// <summary>How a fractional float/double target is reduced to the displayed
    /// integer before an absolute Exact/Between/AtLeast/AtMost compare. Threaded to
    /// <c>SpcEngine.Matches</c>; the rounding logic lives in <c>SpcEngine.AbsMatches</c>,
    /// not in <see cref="SpcAbsolutePredicate.Matches"/> (Models must not depend on Services).</summary>
    public FloatRoundMode RoundMode { get; set; } = FloatRoundMode.Round;
}

/// <summary>One field whose value sequence across the selected snapshots matched
/// the predicate chain.</summary>
public sealed class SpcResultRow
{
    public string ClassName    { get; set; } = "";
    public string NormPath     { get; set; } = "";   // object identity (display)
    public string PropName     { get; set; } = "";
    public int    PropOffset   { get; set; }
    public string DeclaredType { get; set; } = "";
    /// <summary>Newest snapshot's live address — the CE-export handoff target.</summary>
    public string ObjAddr      { get; set; } = "";
    /// <summary>Rendered value per snapshot, oldest → newest.</summary>
    public List<string> Values { get; set; } = new();

    /// <summary>The value sequence as one display string ("100  →  80  →  60").
    /// A single rendered column keeps the results grid AOT-safe (no per-snapshot
    /// dynamic <c>Binding("Values[i]")</c>, which trips IL2026/IL3050).</summary>
    public string SequenceDisplay => string.Join("  →  ", Values);
}

/// <summary>Result of an SPC query: matching rows + truncation flag.</summary>
public sealed class SpcResult
{
    public List<SpcResultRow> Rows { get; } = new();
    /// <summary>True when the row cap was hit.</summary>
    public bool Truncated   { get; set; }
    /// <summary>Number of snapshots in the evaluated sequence.</summary>
    public int  SnapshotCount { get; set; }
    /// <summary>Top-N classes by hit count across <see cref="Rows"/> (the noise
    /// picker). Sorted desc by count. Classes already in the query's
    /// <see cref="SpcQuery.ExcludedClasses"/> are NOT included here (they didn't
    /// produce rows anyway). Empty when no rows matched.</summary>
    public List<ClassNoiseRow> TopContributors { get; } = new();
}

/// <summary>One Top-N noise picker row: a class that produced many result rows in
/// the most recent query, with sample prop names for context. Built post-query
/// from the result row set so it reflects what the user is staring at, not the
/// raw capture distribution.</summary>
public sealed class ClassNoiseRow
{
    public string ClassName { get; set; } = "";
    public int    HitCount  { get; set; }
    /// <summary>Up to 3 most-frequent property names this class contributed —
    /// helps the user judge whether the class is widget/anim noise or genuinely
    /// gameplay-relevant before ticking it.</summary>
    public List<string> SampleProps { get; set; } = new();

    /// <summary>Comma-joined sample props for compact column display.</summary>
    public string SamplePropsDisplay => string.Join(", ", SampleProps);
}
