using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Pure, AOT-safe SPC predicate evaluator. Given one candidate field's value
/// sequence across the selected snapshots (oldest → newest), decides whether it
/// satisfies the directional chain + the per-snapshot absolute predicates. Kept
/// separate from <see cref="SnapshotStore"/> (which does the in-memory intersection
/// load) so the predicate logic is unit-testable without a database. The in-memory
/// approach (port of the `discrete` sister project) replaced the old SQL N-way
/// self-join, which only stayed fast with perfect composite covering indexes.
/// </summary>
public static class SpcEngine
{
    /// <summary>
    /// Evaluate the chain over a value sequence. <paramref name="hex"/>/<paramref
    /// name="num"/> are the raw bytes / numeric value per snapshot (same length,
    /// oldest → newest). <paramref name="dir"/>[i] is snapshot i's directional
    /// predicate (index 0 = baseline, ignored). <paramref name="abs"/>[i] is the
    /// optional absolute predicate for snapshot i (null/short list = none). Unchanged
    /// / Changed compare raw hex (type-exact); Increased / Decreased compare numeric
    /// (per declared width). Absolute predicates compare numeric. All must hold.
    /// </summary>
    public static bool Matches(
        IReadOnlyList<string> hex,
        IReadOnlyList<double?> num,
        IReadOnlyList<SpcPredicateKind> dir,
        IReadOnlyList<SpcAbsolutePredicate>? abs,
        string declaredType = "",
        FloatRoundMode mode = FloatRoundMode.Round)
    {
        int n = hex.Count;
        bool isFloat = SnapshotNumeric.IsFloatType(declaredType);

        // Absolute (value-window) predicates: each snapshot independently.
        if (abs != null)
            for (int i = 0; i < n && i < abs.Count; i++)
                if (!AbsMatches(abs[i], num[i], declaredType, mode)) return false;

        // Directional chain: snapshot i vs i-1. Index 0 is the baseline (Any). The
        // rounding mode compares the DISPLAYED (reduced) values for Float/
        // Double leaves so sub-display jitter is "unchanged"; integer / non-finite leaves
        // keep byte-exact hex for Changed/Unchanged (no >2^53 double-precision regression).
        for (int i = 1; i < n; i++)
        {
            switch (dir[i])
            {
                case SpcPredicateKind.Unchanged:
                case SpcPredicateKind.Changed:
                {
                    bool unchanged = dir[i] == SpcPredicateKind.Unchanged;
                    bool same;
                    if (isFloat && num[i] is double a && num[i - 1] is double b)
                        same = SnapshotNumeric.TemporalMatch(b, a, declaredType, mode, 0); // 0 = Unchanged-eq
                    else
                        same = string.Equals(hex[i], hex[i - 1], StringComparison.Ordinal);
                    if (unchanged ? !same : same) return false;
                    break;
                }
                case SpcPredicateKind.Increased:
                    if (num[i] is not double hi || num[i - 1] is not double loPrev
                        || !SnapshotNumeric.TemporalMatch(loPrev, hi, declaredType, mode, 1)) return false;
                    break;
                case SpcPredicateKind.Decreased:
                    if (num[i] is not double lo || num[i - 1] is not double hiPrev
                        || !SnapshotNumeric.TemporalMatch(hiPrev, lo, declaredType, mode, -1)) return false;
                    break;
                case SpcPredicateKind.Any:
                default:
                    break;
            }
        }
        return true;
    }

    // Evaluate one absolute predicate with the displayed-integer rounding mode (build
    // 1672): every kind reduces a Float/Double value to the integer the game shows
    // before comparing a whole target/bound, and coerces a fractional target/bound on
    // an integer field. Lives here (Services) so the rounding reuses SnapshotNumeric
    // without the Models layer depending on Services.
    private static bool AbsMatches(SpcAbsolutePredicate p, double? value, string declaredType,
                                   FloatRoundMode mode)
    {
        if (p.Kind == SpcAbsoluteKind.None) return true;
        if (value is not double v) return false;
        return p.Kind switch
        {
            SpcAbsoluteKind.Exact   => SnapshotNumeric.ExactMatch(v, p.Low, declaredType, mode),
            SpcAbsoluteKind.Between => SnapshotNumeric.BetweenMatch(v, p.Low, p.High, declaredType, mode),
            SpcAbsoluteKind.AtLeast => SnapshotNumeric.AtLeastMatch(v, p.Low, declaredType, mode),
            SpcAbsoluteKind.AtMost  => SnapshotNumeric.AtMostMatch(v, p.High, declaredType, mode),
            _                       => true,
        };
    }
}
