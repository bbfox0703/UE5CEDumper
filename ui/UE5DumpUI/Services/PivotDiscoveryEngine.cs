using System;
using System.Collections.Generic;
using System.Linq;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Pure, AOT-safe change-driven discovery: takes the (instance, field) value
/// sequences that survived the cross-snapshot intersection, rolls them up per
/// (class, property), keeps only those that actually MOVED, and ranks them by how
/// much each looks like a deliberate game-state target. This is the engine behind
/// the "Suggest targets" front-door to Class Pivot — instead of forcing the user
/// to guess a class/key when the target is unknown, it surfaces the handful of
/// (class, prop) pairs that changed between two captures (before/after an in-game
/// action) so the user picks from a short, ranked list.
///
/// The ranking is a transparent weighted sum of four signals, every one of which
/// is exposed on the candidate for a future cross-game calibration pass (the same
/// way <see cref="PropertyScoringTable"/> was tuned over 20 games):
///   • interest    — the calibrated name/class score (Gold/HP/XP rank high). Dominant.
///   • change      — did it move cleanly (monotonic) vs jitter.
///   • selectivity — a change confined to FEW instances is specific (the thing you
///                   touched); a change on thousands is usually global render/anim noise.
///   • population  — penalises ubiquitous fields (huge instance counts) as a noise prior.
/// Kept separate from <see cref="SnapshotStore"/> (which does the SQL load) so the
/// roll-up/ranking is unit-testable without a database — same split as
/// <see cref="PivotEngine"/> / <see cref="SpcEngine"/>. See
/// docs/experimental-snapshot-spc-pivot.md §"Phase C — C3".
/// </summary>
public static class PivotDiscoveryEngine
{
    // Rank weights. Interest is the reliable, calibrated anchor; change &
    // selectivity nudge; population is a gentle noise penalty. Chosen so a named
    // gameplay field always outranks a name-less one of the same change shape, and
    // a change confined to a few instances outranks the same field changing across
    // a huge population. Tunable — sub-scores are exposed on each candidate.
    public const double InterestWeight   = 1.0;
    public const double ChangeWeight      = 2.0;
    public const double SelectivityWeight = 3.0;
    public const double PopulationWeight  = 1.5;
    // Shape (only discriminates with ≥3 snapshots; inert at 2): rewards a clean
    // ONE-TIME change — the discrete in-game action you captured around — and stays
    // neutral on a steady monotonic trend (a draining resource), while demoting a
    // field that JITTERS on every interval (per-frame render/anim/physics noise).
    // Change-interval count alone can't tell a monotonic drain from jitter, so shape
    // combines the interval count WITH monotonicity. Tunable; exposed on the candidate.
    public const double ShapeWeight       = 3.0;

    // One rolled-up (class, prop, type) group: instance counts + a deterministic
    // representative changed instance (smallest norm path) for display / handoff.
    private sealed class Group
    {
        public string ClassName = "", PropName = "", DeclaredType = "";
        public int Total;
        public int Changed;
        public DiscoveryInput? Rep;
    }

    /// <summary>Roll up the candidate value-sequences per (class, prop), drop those
    /// that never changed, score + rank the rest, and return the top
    /// <paramref name="maxResults"/>. <paramref name="snapshotCount"/> is recorded
    /// on the result for display.</summary>
    public static DiscoveryResult Rank(
        IReadOnlyList<DiscoveryInput> inputs, int snapshotCount, int maxResults = 200,
        IReadOnlyDictionary<string, int>? classTotals = null)
    {
        var result = new DiscoveryResult { SnapshotCount = snapshotCount };

        var groups = new Dictionary<(string, string, string), Group>();
        foreach (var inp in inputs)
        {
            var k = (inp.ClassName, inp.PropName, inp.DeclaredType);
            if (!groups.TryGetValue(k, out var g))
            {
                g = new Group { ClassName = inp.ClassName, PropName = inp.PropName, DeclaredType = inp.DeclaredType };
                groups[k] = g;
            }
            g.Total++;
            if (HasChanged(inp.Hex))
            {
                g.Changed++;
                // Deterministic representative: the changed instance with the
                // lexicographically smallest norm path. Stable regardless of the
                // dictionary iteration order the store happens to hand us.
                if (g.Rep == null || string.CompareOrdinal(inp.NormPath, g.Rep.NormPath) < 0)
                    g.Rep = inp;
            }
        }

        var rows = new List<DiscoveryCandidate>();
        foreach (var g in groups.Values)
        {
            if (g.Changed == 0 || g.Rep == null) continue;   // gate: only things that moved
            rows.Add(BuildCandidate(g, snapshotCount, classTotals));
        }
        result.ChangedGroups = rows.Count;

        rows.Sort(CompareCandidates);
        if (maxResults > 0 && rows.Count > maxResults)
        {
            result.Truncated = true;
            rows.RemoveRange(maxResults, rows.Count - maxResults);
        }
        result.Rows.AddRange(rows);
        return result;
    }

    /// <summary>True when a value sequence is not constant — i.e. the field moved
    /// at least once across the snapshots (changed-then-back still counts as moved).
    /// Public for unit tests.</summary>
    public static bool HasChanged(IReadOnlyList<string> hex)
    {
        if (hex.Count < 2) return false;
        string first = hex[0];
        for (int i = 1; i < hex.Count; i++)
            if (!string.Equals(hex[i], first, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Number of adjacent snapshot intervals whose value differs (0..N-1). A
    /// single discrete event scores 1; per-frame churn scores N-1. Public for tests.</summary>
    public static int ChangeIntervals(IReadOnlyList<string> hex)
    {
        int c = 0;
        for (int i = 1; i < hex.Count; i++)
            if (!string.Equals(hex[i], hex[i - 1], StringComparison.Ordinal)) c++;
        return c;
    }

    private static DiscoveryCandidate BuildCandidate(
        Group g, int snapshotCount, IReadOnlyDictionary<string, int>? classTotals)
    {
        var rep = g.Rep!;
        var score = PropertyScoringTable.Score(
            new PropertySearchMatch { ClassName = g.ClassName, PropName = g.PropName });
        var (direction, monotonic) = Direction(rep.Num);

        // Total: the bounded SQL path feeds only the CHANGED instances, so it supplies
        // the real per-class instance count separately; the in-memory path (and the
        // unit tests) count every fed input. Never let total drop below changed (guards
        // a stale / missing class_counts entry — the display must stay sane).
        int total = classTotals != null && classTotals.TryGetValue(g.ClassName, out var ct)
            ? Math.Max(ct, g.Changed)
            : g.Total;

        // A clean (monotonic) move is a stronger signal than jitter.
        double changeScore = monotonic ? 1.0 : 0.5;
        // Few instances changed → specific (the one thing you touched). Decays with
        // the absolute changed count, so a singleton (1) scores 1.0 and global noise
        // (thousands) scores near 0 — independent of the class's total population.
        double selectivity = 1.0 / (1.0 + Math.Log10(Math.Max(1, g.Changed)));
        // Ubiquitous fields (huge instance counts) are usually render/anim/timer
        // noise. Zero below ~10 instances (singletons & small squads pay nothing).
        double population  = Math.Max(0.0, Math.Log10(Math.Max(1, total)) - 1.0);

        // Shape over the representative's value sequence. With only 2 snapshots (one
        // interval) shape is meaningless → neutral 1.0 for every candidate (a constant
        // that can't reorder). With ≥3: a single discrete change (ci==1) is the
        // cleanest game-action signal; a steady monotonic trend over several intervals
        // (a draining resource) stays neutral-ish; jitter on every interval is noise.
        int intervals = Math.Max(1, snapshotCount - 1);
        int ci = ChangeIntervals(rep.Hex);
        double shapeScore = snapshotCount <= 2 ? 1.0
                          : ci <= 1            ? 1.0
                          : monotonic          ? 0.5
                          :                      0.0;
        string shapeLabel = snapshotCount <= 2 ? ""
                          : ci <= 1            ? "one-time"
                          : monotonic          ? $"trend {ci}/{intervals}"
                          :                      $"jitter {ci}/{intervals}";

        double composite =
              InterestWeight    * Math.Max(0, score.FinalScore)
            + ChangeWeight      * changeScore
            + SelectivityWeight * selectivity
            + ShapeWeight       * shapeScore
            - PopulationWeight  * population;

        return new DiscoveryCandidate
        {
            ClassName        = g.ClassName,
            PropName         = g.PropName,
            DeclaredType     = g.DeclaredType,
            InstancesTotal   = total,
            InstancesChanged = g.Changed,
            Direction        = direction,
            SampleSequence   = string.Join("  →  ",
                rep.Hex.Select(h => SnapshotNumeric.Render(g.DeclaredType, h))),
            ObjAddr          = rep.ObjAddr,
            NormPath         = rep.NormPath,
            ArrayField       = rep.ArrayField,     // [W1-DISCOVER-ARRAY] one array element per group
            InnerProp        = rep.InnerProp,
            InterestScore    = score.FinalScore,
            CategoryName     = PropertyScoringTable.DisplayName(score.Category),
            CategoryColor    = PropertyScoringTable.CategoryColor(score.Category),
            Score            = composite,
            ChangeScore      = changeScore,
            Selectivity      = selectivity,
            PopulationPenalty = population,
            ShapeScore       = shapeScore,
            ChangeIntervals  = ci,
            ShapeLabel       = shapeLabel,
        };
    }

    // Monotonic direction of a numeric sequence. Null pairs are simply skipped; a
    // sequence with no decidable numeric move falls through to "" (non-numeric/flat).
    private static (string dir, bool monotonic) Direction(IReadOnlyList<double?> num)
    {
        bool up = false, down = false;
        for (int i = 1; i < num.Count; i++)
        {
            if (num[i] is not double cur || num[i - 1] is not double prev) continue;
            if (cur > prev) up = true;
            else if (cur < prev) down = true;
        }
        if (up && !down) return ("↑", true);
        if (down && !up) return ("↓", true);
        if (up && down)  return ("↕", false);   // numeric but jittery
        return ("", false);                       // non-numeric, or flat numeric
    }

    // Rank desc by composite; tie-break by interest, then fewer-changed (more
    // specific), then class/prop name for total determinism.
    private static int CompareCandidates(DiscoveryCandidate a, DiscoveryCandidate b)
    {
        int c = b.Score.CompareTo(a.Score);
        if (c != 0) return c;
        c = b.InterestScore.CompareTo(a.InterestScore);
        if (c != 0) return c;
        c = a.InstancesChanged.CompareTo(b.InstancesChanged);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.ClassName, b.ClassName);
        return c != 0 ? c : string.CompareOrdinal(a.PropName, b.PropName);
    }
}
