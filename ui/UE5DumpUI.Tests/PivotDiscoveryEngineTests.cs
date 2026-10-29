using System.Collections.Generic;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Pure change-driven discovery: rolls (instance, field) value-sequences up per
/// (class, prop), keeps only fields that moved, and ranks them so named gameplay
/// state beats noise and a change confined to few instances beats a ubiquitous one.
/// This is the auto front-door to Class Pivot (no class/key guessing).
/// </summary>
public class PivotDiscoveryEngineTests
{
    // One instance-candidate of (class, prop) with a value sequence. norm lets a
    // test pin the deterministic representative; num drives direction.
    private static DiscoveryInput In(
        string cls, string prop, string type, string norm,
        string[] hex, double?[]? num = null, string addr = "0x1000")
        => new()
        {
            ClassName = cls, PropName = prop, DeclaredType = type, NormPath = norm,
            ObjAddr = addr, Hex = hex,
            Num = num ?? hex.Select(_ => (double?)null).ToArray(),
        };

    private static DiscoveryCandidate? Find(DiscoveryResult r, string cls, string prop)
        => r.Rows.FirstOrDefault(x => x.ClassName == cls && x.PropName == prop);

    [Fact]
    public void HasChanged_DetectsMovementAndConstants()
    {
        Assert.True(PivotDiscoveryEngine.HasChanged(new[] { "a", "b" }));
        Assert.True(PivotDiscoveryEngine.HasChanged(new[] { "a", "b", "a" }));   // changed-then-back
        Assert.False(PivotDiscoveryEngine.HasChanged(new[] { "a", "a", "a" }));
        Assert.False(PivotDiscoveryEngine.HasChanged(new[] { "a" }));            // single snapshot
    }

    [Fact]
    public void UnchangedField_IsExcluded()
    {
        var inputs = new[]
        {
            In("PlayerState", "Gold",  "IntProperty", "PS_0", new[] { "64", "64" }, new double?[] { 100, 100 }),
            In("PlayerState", "Level", "IntProperty", "PS_0", new[] { "01", "05" }, new double?[] { 1, 5 }),
        };
        var r = PivotDiscoveryEngine.Rank(inputs, 2);
        Assert.Null(Find(r, "PlayerState", "Gold"));        // constant → dropped
        Assert.NotNull(Find(r, "PlayerState", "Level"));    // moved → kept
        Assert.Equal(1, r.ChangedGroups);
    }

    [Fact]
    public void InterestingName_BeatsNeutralName_SameChangeShape()
    {
        // Two singletons that both moved monotonically once; only the NAME differs.
        var inputs = new[]
        {
            In("BP_Player_C", "Gold",          "IntProperty", "P_0", new[] { "64", "32" }, new double?[] { 100, 50 }),
            In("BP_Player_C", "ZqxInternalPad", "IntProperty", "P_0", new[] { "64", "32" }, new double?[] { 100, 50 }),
        };
        var r = PivotDiscoveryEngine.Rank(inputs, 2);
        Assert.Equal("Gold", r.Rows[0].PropName);
        Assert.True(Find(r, "BP_Player_C", "Gold")!.Score
                  > Find(r, "BP_Player_C", "ZqxInternalPad")!.Score);
    }

    [Fact]
    public void SelectiveChange_BeatsUbiquitousChange_SameName()
    {
        // Same field name (Health), same monotonic move. ClassA: 1 instance changed.
        // ClassB: 200 instances all changed (global poison tick → noise). The
        // specific, low-population one ranks higher.
        var inputs = new List<DiscoveryInput>
        {
            In("BP_Boss_C", "Health", "IntProperty", "Boss_0", new[] { "64", "32" }, new double?[] { 100, 50 }),
        };
        for (int i = 0; i < 200; i++)
            inputs.Add(In("BP_Mob_C", "Health", "IntProperty", $"Mob_{i:D3}",
                          new[] { "64", "32" }, new double?[] { 100, 50 }));

        var r = PivotDiscoveryEngine.Rank(inputs, 2);
        Assert.True(Find(r, "BP_Boss_C", "Health")!.Score
                  > Find(r, "BP_Mob_C", "Health")!.Score);
        Assert.Equal("BP_Boss_C", r.Rows[0].ClassName);
    }

    [Fact]
    public void RollUp_CountsTotalAndChangedPerGroup()
    {
        // Three instances of one (class, prop); two moved, one stayed constant.
        var inputs = new[]
        {
            In("BP_Enemy_C", "Health", "IntProperty", "E_0", new[] { "64", "32" }, new double?[] { 100, 50 }),
            In("BP_Enemy_C", "Health", "IntProperty", "E_1", new[] { "64", "10" }, new double?[] { 100, 16 }),
            In("BP_Enemy_C", "Health", "IntProperty", "E_2", new[] { "64", "64" }, new double?[] { 100, 100 }),
        };
        var r = PivotDiscoveryEngine.Rank(inputs, 2);
        var c = Find(r, "BP_Enemy_C", "Health")!;
        Assert.Equal(3, c.InstancesTotal);
        Assert.Equal(2, c.InstancesChanged);
        Assert.Equal("2/3", c.ChangedDisplay);   // changed/total
    }

    [Fact]
    public void Direction_MonotonicUpDownAndMixed()
    {
        var up = Find(PivotDiscoveryEngine.Rank(new[]
        {
            In("C", "XP", "IntProperty", "x", new[] { "01", "02", "03" }, new double?[] { 1, 2, 3 }),
        }, 3), "C", "XP")!;
        Assert.Equal("↑", up.Direction);

        var down = Find(PivotDiscoveryEngine.Rank(new[]
        {
            In("C", "HP", "IntProperty", "x", new[] { "64", "32", "0A" }, new double?[] { 100, 50, 10 }),
        }, 3), "C", "HP")!;
        Assert.Equal("↓", down.Direction);

        var mixed = Find(PivotDiscoveryEngine.Rank(new[]
        {
            In("C", "Ammo", "IntProperty", "x", new[] { "0A", "1E", "05" }, new double?[] { 10, 30, 5 }),
        }, 3), "C", "Ammo")!;
        Assert.Equal("↕", mixed.Direction);
    }

    [Fact]
    public void RepresentativeInstance_IsDeterministic_SmallestNormPath()
    {
        // Two changed instances in any input order → the rep (sample/addr) is the
        // one with the smallest norm path, so the display is stable.
        var inputs = new[]
        {
            In("C", "Gold", "IntProperty", "B_zzz", new[] { "64", "10" }, new double?[] { 100, 16 }, addr: "0xZZZ"),
            In("C", "Gold", "IntProperty", "A_aaa", new[] { "64", "32" }, new double?[] { 100, 50 }, addr: "0xAAA"),
        };
        var c = Find(PivotDiscoveryEngine.Rank(inputs, 2), "C", "Gold")!;
        Assert.Equal("0xAAA", c.ObjAddr);
        Assert.Equal("A_aaa", c.NormPath);
    }

    [Fact]
    public void MaxResults_CapsAndFlagsTruncated()
    {
        var inputs = Enumerable.Range(0, 10)
            .Select(i => In("C", "P" + i, "IntProperty", "n", new[] { "00", "01" }, new double?[] { 0, 1 }))
            .ToArray();
        var r = PivotDiscoveryEngine.Rank(inputs, 2, maxResults: 3);
        Assert.Equal(3, r.Rows.Count);
        Assert.True(r.Truncated);
        Assert.Equal(10, r.ChangedGroups);   // counted before the cap
    }

    [Fact]
    public void ChangeIntervals_CountsAdjacentDifferences()
    {
        Assert.Equal(0, PivotDiscoveryEngine.ChangeIntervals(new[] { "a", "a", "a" }));
        Assert.Equal(1, PivotDiscoveryEngine.ChangeIntervals(new[] { "a", "a", "b" }));   // one-time
        Assert.Equal(2, PivotDiscoveryEngine.ChangeIntervals(new[] { "a", "b", "c" }));   // every interval (N=3)
        Assert.Equal(1, PivotDiscoveryEngine.ChangeIntervals(new[] { "a", "b", "b" }));
    }

    [Fact]
    public void Shape_OneTimeEvent_BeatsJitter_NeutralNameAndPopulation()
    {
        // 3 snapshots, neutral names + identical changed-count (1) + population (1), so
        // SHAPE is the only differentiator. "Aaa" moves ONCE (stable, stable, moved) =
        // one-time event. "Bbb" jitters every interval, non-monotonically = noise.
        var inputs = new[]
        {
            In("BP_Actor_C", "Aaa", "IntProperty", "p", new[] { "01000000", "01000000", "05000000" }, new double?[] { 1, 1, 5 }),
            In("BP_Actor_C", "Bbb", "IntProperty", "q", new[] { "01000000", "63000000", "05000000" }, new double?[] { 1, 99, 5 }),
        };
        var r = PivotDiscoveryEngine.Rank(inputs, 3);
        var one = Find(r, "BP_Actor_C", "Aaa")!;
        var jit = Find(r, "BP_Actor_C", "Bbb")!;
        Assert.Equal("one-time", one.ShapeLabel);
        Assert.Equal(1.0, one.ShapeScore);
        Assert.Equal(0.0, jit.ShapeScore);            // changed every interval + non-monotonic
        Assert.True(one.Score > jit.Score);
        Assert.Equal("BP_Actor_C", r.Rows[0].ClassName);
        Assert.Equal("Aaa", r.Rows[0].PropName);      // one-time event ranks first
    }

    [Fact]
    public void Shape_MonotonicTrend_IsNeutral_NotDemotedLikeJitter()
    {
        // A steady drain that moves EVERY interval but MONOTONICALLY (a draining
        // resource) must NOT be demoted like jitter: shape 0.5, above jitter's 0.0.
        var trend = Find(PivotDiscoveryEngine.Rank(new[]
        {
            In("C", "Energy", "IntProperty", "x", new[] { "64000000", "32000000", "0A000000" }, new double?[] { 100, 50, 10 }),
        }, 3), "C", "Energy")!;
        Assert.Equal(0.5, trend.ShapeScore);
        Assert.StartsWith("trend", trend.ShapeLabel);
    }

    [Fact]
    public void Shape_NeutralAtTwoSnapshots()
    {
        // One interval → shape can't be inferred → neutral 1.0 / blank label for all.
        var c = Find(PivotDiscoveryEngine.Rank(new[]
        {
            In("C", "Gold", "IntProperty", "x", new[] { "64000000", "32000000" }, new double?[] { 100, 50 }),
        }, 2), "C", "Gold")!;
        Assert.Equal(1.0, c.ShapeScore);
        Assert.Equal("", c.ShapeLabel);
    }

    [Fact]
    public void ClassTotals_OverrideInstanceTotal_WhenOnlyChangedFed()
    {
        // The bounded SQL path feeds only the CHANGED instances + a per-class total.
        // InstancesTotal must come from the dict, not the fed count.
        var inputs = new[]
        {
            In("BP_Mob_C", "Health", "IntProperty", "m0", new[] { "64", "32" }, new double?[] { 100, 50 }),
        };
        var totals = new Dictionary<string, int> { ["BP_Mob_C"] = 500 };
        var c = Find(PivotDiscoveryEngine.Rank(inputs, 2, 200, totals), "BP_Mob_C", "Health")!;
        Assert.Equal(500, c.InstancesTotal);   // from class_counts, not the 1 fed input
        Assert.Equal(1, c.InstancesChanged);
    }

    [Fact]
    public void ClassTotals_NeverBelowChanged()
    {
        // A stale / missing class_counts entry must never yield total < changed.
        var inputs = Enumerable.Range(0, 3)
            .Select(i => In("C", "P", "IntProperty", "n" + i, new[] { "00", "01" }, new double?[] { 0, 1 }))
            .ToArray();
        var totals = new Dictionary<string, int> { ["C"] = 1 };   // understated
        var c = Find(PivotDiscoveryEngine.Rank(inputs, 2, 200, totals), "C", "P")!;
        Assert.Equal(3, c.InstancesChanged);
        Assert.Equal(3, c.InstancesTotal);     // max(1, 3)
    }

    [Fact]
    public void SampleSequence_RendersNumericValues()
    {
        // "64"=100, "32"=50 as little-endian int8-in-hex won't decode as IntProperty
        // (needs 4 bytes) so use a 4-byte LE hex for 100 -> 64000000, 50 -> 32000000.
        var c = Find(PivotDiscoveryEngine.Rank(new[]
        {
            In("C", "Gold", "IntProperty", "x", new[] { "64000000", "32000000" },
               new double?[] { 100, 50 }),
        }, 2), "C", "Gold")!;
        Assert.Equal("100  →  50", c.SampleSequence);
        Assert.Equal("↓", c.Direction);
    }

    // [DISCOVER-SHADOWED-POOL] A class can carry two properties of one name and type at different offsets (a shadowed
    // name). Rank grouped by (class, prop, type), so the two copies became ONE candidate: their changed counts pooled,
    // and the sample was whichever copy's instance sorted first. Each copy is its own candidate, labelled with its offset.
    private static DiscoveryInput At(DiscoveryInput d, int offset) { d.PropOffset = offset; return d; }

    [Fact]
    public void TheTwoCopiesOfAShadowedName_AreTwoCandidates_EachLabelledWithItsOffset()
    {
        var r = PivotDiscoveryEngine.Rank(new[]
        {
            At(In("Shot", "PrerollTime", "FloatProperty", "/a", new[] { "00", "01" }), 0xC4),
            At(In("Shot", "PrerollTime", "FloatProperty", "/b", new[] { "00", "02" }), 0xC4),
            At(In("Shot", "PrerollTime", "FloatProperty", "/c", new[] { "10", "20" }), 0x128),
        }, 2);

        var rows = r.Rows.Where(x => x.PropName == "PrerollTime").OrderBy(x => x.PropOffset).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal((0xC4, 2, "PrerollTime@+0xC4"), (rows[0].PropOffset, rows[0].InstancesChanged, rows[0].PropLabel));
        Assert.Equal((0x128, 1, "PrerollTime@+0x128"), (rows[1].PropOffset, rows[1].InstancesChanged, rows[1].PropLabel));
        Assert.Equal("/c", rows[1].NormPath);                                      // the sample is that copy's
    }

    [Fact]
    public void OneMovedCopyOfANameTheSnapshotShadows_IsStillLabelledWithItsOffset()
    {
        // The Strict path feeds only the CHANGED rows: when one copy did not move, Rank sees one offset. The store passes
        // the names the snapshot shows at more than one offset, so the label matches Class Pivot's picker (review).
        var r = PivotDiscoveryEngine.Rank(new[]
        {
            At(In("Shot", "PrerollTime", "FloatProperty", "/c", new[] { "10", "20" }), 0x128),
        }, 2, shadowedNames: new HashSet<(string, string)> { ("Shot", "PrerollTime") });

        Assert.Equal("PrerollTime@+0x128", Assert.Single(r.Rows).PropLabel);
    }

    [Fact]
    public void TwoCopiesTiedOnEveryScore_ComeOutInOffsetOrder()
    {
        // Review: the last tie-break (offset) was unpinned -- the shadowed-name test's copies differ in changed count, so
        // they never reach it, and it re-sorted before asserting. Fed in REVERSE offset order, fully tied copies must
        // still come out +0xC4 first: List.Sort alone keeps no order.
        var r = PivotDiscoveryEngine.Rank(new[]
        {
            At(In("Shot", "PrerollTime", "FloatProperty", "/b", new[] { "00", "01" }), 0x128),
            At(In("Shot", "PrerollTime", "FloatProperty", "/a", new[] { "00", "01" }), 0xC4),
        }, 2);

        Assert.Equal(new[] { 0xC4, 0x128 }, r.Rows.Select(x => x.PropOffset));
    }

    [Fact]
    public void ANameUsedOnce_KeepsItsBareLabel_AndPoolsItsInstances()
    {
        // CONTROL: one copy, several instances -- one candidate, as before.
        var r = PivotDiscoveryEngine.Rank(new[]
        {
            At(In("C", "Gold", "IntProperty", "/a", new[] { "01", "02" }), 0x30),
            At(In("C", "Gold", "IntProperty", "/b", new[] { "01", "03" }), 0x30),
        }, 2);

        var gold = Assert.Single(r.Rows);
        Assert.Equal(2, gold.InstancesChanged);
        Assert.Equal("Gold", gold.PropLabel);
    }
}
