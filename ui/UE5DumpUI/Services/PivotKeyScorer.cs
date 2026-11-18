using System.Linq;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Ranks a pivot class's numeric fields as candidate GROUP keys, and (reusing
/// the calibrated <see cref="PropertyScoringTable"/>) as candidate VALUE fields.
/// This is the UE answer to <c>discrete</c>'s "user must guess the business key"
/// pain: a good key is an enum/int-typed, key-named field whose cardinality
/// actually partitions the instances. Pure / AOT-safe. See
/// docs/experimental-snapshot-spc-pivot.md §"Phase C" (the six-layer key win).
///
/// Note: top-level snapshot capture is all-numeric, so FName keys (e.g. an
/// FName ItemID) aren't seen here — int/enum IDs are. FName inner keys live on
/// array-element rows (the cargo case) and are a separate pivot path.
/// </summary>
public static class PivotKeyScorer
{
    // Key-ish name tokens — fields whose NAME reads like an identifier/category.
    private static readonly HashSet<string> s_keyTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "index", "idx", "type", "kind", "category", "cat", "slot", "row",
        "rowname", "tag", "key", "class", "state", "team", "faction", "rank",
        "tier", "group", "guid", "enum", "mode", "variant",
    };

    /// <summary>Composite key suitability in [0,1]: type prior + name prior +
    /// cardinality (does it actually partition the instances?).</summary>
    public static double KeyScore(PivotFieldInfo f)
        => 0.35 * TypeKeyPrior(f.DeclaredType)
         + 0.35 * NameKeyBoost(f.Name)
         + 0.30 * CardinalityScore(f.DistinctCount, f.InstanceCount);

    /// <summary>The best key candidate (highest <see cref="KeyScore"/> with a
    /// non-zero score), or null when nothing partitions usefully.</summary>
    public static PivotFieldInfo? SuggestKey(IEnumerable<PivotFieldInfo> fields)
    {
        PivotFieldInfo? best = null;
        double bestScore = 0;
        foreach (var f in fields)
        {
            double s = KeyScore(f);
            if (s > bestScore) { bestScore = s; best = f; }
        }
        return best;
    }

    /// <summary>Interesting-to-project score (HP / Gold / Damage rank high) via
    /// the shared <see cref="PropertyScoringTable"/>, for suggesting and sorting
    /// value fields.</summary>
    public static int ValueScore(string className, string propName)
        => PropertyScoringTable.Score(
            new PropertySearchMatch { ClassName = className, PropName = propName }).FinalScore;

    // Enum-like (Byte) and integer types make good keys; floats/doubles are
    // continuous and almost never grouping keys.
    private static double TypeKeyPrior(string declaredType) => declaredType switch
    {
        "ByteProperty"                                                            => 1.0,
        "IntProperty" or "UInt32Property" or "Int16Property" or "UInt16Property"
            or "Int64Property" or "UInt64Property" or "Int8Property"              => 0.8,
        "FloatProperty" or "DoubleProperty"                                       => 0.1,
        _                                                                         => 0.4,
    };

    private static double NameKeyBoost(string name)
    {
        var tokens = KeywordTokenizer.TokenizeAsSet(name);
        int hits = tokens.Count(t => s_keyTokens.Contains(t));
        return Math.Min(1.0, 0.5 * hits);
    }

    // A grouping key wants 1 < distinct < instances (real groups). All-same
    // (distinct<=1) is useless; unique-per-instance (distinct==instances) is
    // degenerate (that's just identity).
    private static double CardinalityScore(int distinct, int instances)
    {
        if (instances <= 1 || distinct <= 1) return 0.0;
        if (distinct >= instances)           return 0.3;
        return 1.0;
    }
}
