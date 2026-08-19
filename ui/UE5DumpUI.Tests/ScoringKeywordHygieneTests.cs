using System;
using System.Collections.Generic;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Audit #5 Z13 / Z14 — keyword-table hygiene for BOTH scoring tables.
///
/// <para>
/// <b>The rule these tests draw, and why it is drawn exactly there.</b>
/// <see cref="KeywordScoringTable"/> and <see cref="PropertyScoringTable"/> both score by
/// counting how many table entries match, and both tokenise the entry before matching. So
/// two entries that tokenise to the SAME set are one keyword written twice — it fires on
/// identical input, can express no difference, and double-counts against
/// <c>CountTokenHits</c>' own documented contract ("Each keyword counts once total").
/// <c>"HP"</c> and <c>"Hp"</c> were exactly that in both tables: the tokeniser lowercases,
/// so both are <c>["hp"]</c>. Removed (Z13).
/// </para>
/// <para>
/// A SUBSUMED entry is a different animal and is deliberately kept: <c>"IsDead"</c>
/// tokenises to <c>["is","dead"]</c>, which can only match when <c>"Dead"</c> also
/// matches — but it fires on a strictly narrower set of names, so the extra hit is a
/// specificity weight separating <c>bIsDead</c> from <c>DeadZone</c>. Eight such
/// compounds already live in these tables (CritDamage, HitDamage, CritRate, JumpHeight,
/// JumpZ, InitialLifeSpan, GlobalTimeDilation, TimeDilation) and every one would have to
/// go if subsumption were treated as duplication. The comment beside IsDead/IsAlive DID
/// claim they were needed to MATCH — behaviour the code does not have — and that comment
/// was corrected instead (Z14). This is the same line
/// <c>PropertySearchTests.SeedQueries_ContainNoExactDuplicates</c> already draws for the
/// seed list, for the same reason, and its own doc records that widening it to
/// subsumption would have deleted three working entries.
/// </para>
/// </summary>
public class ScoringKeywordHygieneTests
{
    private static (string Table, string[] Keywords)[] AllTables() => new[]
    {
        ("KeywordScoringTable.Stats",           KeywordScoringTable.StatsKeywords),
        ("KeywordScoringTable.Inventory",       KeywordScoringTable.InventoryKeywords),
        ("KeywordScoringTable.Movement",        KeywordScoringTable.MovementKeywords),
        ("KeywordScoringTable.ExplicitCheats",  KeywordScoringTable.ExplicitMovementCheats),
        ("KeywordScoringTable.Combat",          KeywordScoringTable.CombatKeywords),
        ("KeywordScoringTable.Utility",         KeywordScoringTable.UtilityKeywords),
        ("KeywordScoringTable.GameplayAction",  KeywordScoringTable.GameplayActionKeywords),
        ("PropertyScoringTable.Stats",          PropertyScoringTable.StatsKeywords),
        ("PropertyScoringTable.Combat",         PropertyScoringTable.CombatKeywords),
        ("PropertyScoringTable.Resources",      PropertyScoringTable.ResourcesKeywords),
        ("PropertyScoringTable.Movement",       PropertyScoringTable.MovementKeywords),
        ("PropertyScoringTable.Utility",        PropertyScoringTable.UtilityKeywords),
        ("PropertyScoringTable.Timing",         PropertyScoringTable.TimingKeywords),
    };

    /// <summary>
    /// The invariant. FAILS before Z13 on <c>HP</c>/<c>Hp</c> in two tables at once.
    ///
    /// <para>⚠ Do NOT widen this to reject a keyword whose tokens are a SUBSET of
    /// another's — see the class doc. Subsumption is a working specificity weight in
    /// eight places; identical token sets are dead weight in zero.</para>
    /// </summary>
    [Fact]
    public void No_scoring_table_lists_one_keyword_twice_under_different_casing()
    {
        var offenders = new List<string>();
        foreach (var (table, keywords) in AllTables())
        {
            var byTokens = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var kw in keywords)
            {
                var key = string.Join("|", KeywordTokenizer.Tokenize(kw));
                if (!byTokens.TryGetValue(key, out var list))
                    byTokens[key] = list = new List<string>();
                list.Add(kw);
            }
            offenders.AddRange(byTokens.Values
                .Where(g => g.Count > 1)
                .Select(g => $"{table}: {string.Join(" == ", g)}"));
        }

        Assert.True(offenders.Count == 0,
            "These entries tokenise identically, so ONE keyword is counted more than once — "
          + "every row matching it gets a silent multiple of that bucket's per-hit score, in "
          + "the column the panel sorts on, and the tooltip reports the inflated hit count. "
          + "Delete all but one; the tokeniser already lowercases, so case variants are never "
          + "needed (spelling variants like Defense/Defence still are): "
          + string.Join("; ", offenders));
    }

    /// <summary>
    /// The behavioural half of Z13, function side: one "hp" token, one Stats hit.
    /// Before the fix this returned 2 hits and 10 points.
    /// </summary>
    [Theory]
    [InlineData("GetHP")]
    [InlineData("SetHp")]
    [InlineData("Player_HP")]
    public void Function_named_with_HP_scores_the_Stats_keyword_exactly_once(string funcName)
    {
        var r = KeywordScoringTable.Score(new AllFunctionEntry { FuncName = funcName, ClassName = "AThing" });

        Assert.Equal(1, r.KeywordHits);
        Assert.Equal(KeywordScoringTable.StatsKeywordScore, r.FinalScore);
        Assert.Equal(FunctionCategory.Stats, r.Category);
    }

    /// <summary>The property side of the same defect.</summary>
    [Theory]
    [InlineData("HP")]
    [InlineData("Hp")]
    [InlineData("CurrentHP")]
    public void Property_named_with_HP_scores_the_Stats_keyword_exactly_once(string propName)
    {
        var r = PropertyScoringTable.Score(new PropertySearchMatch
        {
            PropName = propName, ClassName = "AThing", PropType = "FloatProperty",
        });

        Assert.Equal(1, r.KeywordHits);
        Assert.Equal(PropertyScoringTable.StatsKeywordScore, r.FinalScore);
    }

    /// <summary>
    /// Negative control for the removal: a genuinely two-keyword name still counts two.
    /// Without this, "score dropped from 10 to 5" could equally mean the whole Stats
    /// bucket had stopped working.
    /// </summary>
    [Fact]
    public void A_name_hitting_two_DISTINCT_stats_keywords_still_counts_two()
    {
        var r = PropertyScoringTable.Score(new PropertySearchMatch
        {
            PropName = "MaxHealth", ClassName = "AThing", PropType = "FloatProperty",
        });

        Assert.Equal(2, r.KeywordHits);   // "Max" + "Health"
        Assert.Equal(2 * PropertyScoringTable.StatsKeywordScore, r.FinalScore);
    }

    /// <summary>
    /// Z14, direction "the comment was wrong": the compound entries stay, so
    /// <c>bIsDead</c> (2 hits: Dead + IsDead) still outranks <c>DeadZone</c> (1 hit:
    /// Dead). Pinning this is what stops a later reader "tidying up" the redundancy the
    /// corrected comment now explains.
    /// </summary>
    [Fact]
    public void IsDead_compound_is_a_specificity_weight_that_outranks_a_bare_Dead_match()
    {
        static int Hits(string name) => PropertyScoringTable.Score(new PropertySearchMatch
        {
            PropName = name, ClassName = "AThing", PropType = "BoolProperty",
        }).KeywordHits;

        Assert.Equal(1, Hits("DeadZone"));    // "Dead" only
        Assert.Equal(2, Hits("bIsDead"));     // "Dead" + "IsDead"
        Assert.Equal(2, Hits("bIsAlive"));    // "Alive" + "IsAlive"
        // ...and the compound genuinely cannot fire alone, which is why it is a WEIGHT
        // and not a matcher: removing "Dead"/"Alive" would not change which names match.
        // ("AliveCount" would be the obvious sibling here and is NOT usable — "Count"
        // is a Resources keyword, so it scores 2 for a reason unrelated to this rule.
        // KeywordHits is the total across every bucket, not the Stats bucket.)
        Assert.Equal(1, Hits("AliveFlag"));   // "Alive" only
    }
}
