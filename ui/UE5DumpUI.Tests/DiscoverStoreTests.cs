using System.IO;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// End-to-end change-driven discovery against a real (temp-file) SQLite store:
/// seeds before/after snapshots, then asserts <c>DiscoverChangesAsync</c> surfaces
/// the (class, prop) targets that MOVED, drops the constant ones, ranks a
/// gameplay-named field above a neutral one, and joins across game sessions. The
/// ranking MATH (selectivity / population) is unit-tested in
/// <see cref="PivotDiscoveryEngineTests"/>; this file checks the SQL load + join +
/// engine wiring. Mirrors the <see cref="SpcStoreTests"/> fixture. See
/// docs/experimental-snapshot-spc-pivot.md §"Phase C — C3".
/// </summary>
public class DiscoverStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public DiscoverStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UE5DumpDiscTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(new MockPlatformService(_tempDir));
        _store.SetActiveGame("DISCGAME");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* WAL files may linger briefly; best effort */ }
    }

    private static string IntHex(int v) =>
        string.Concat(BitConverter.GetBytes(v).Select(b => b.ToString("X2")));

    private static SnapshotCapturedObject Obj(int idx, string cls, string path,
        params (string name, string type, int val)[] fields)
    {
        var o = new SnapshotCapturedObject
        {
            Index = idx, Addr = $"0x{0x7FF600000000 + idx:X}",
            Name = path[(path.LastIndexOf('.') + 1)..], ClassName = cls,
            OuterClassName = "World", Path = path,
        };
        foreach (var (name, type, val) in fields)
            o.Fields.Add(new SnapshotCapturedField { Name = name, Type = type, Hex = IntHex(val), Offset = 0x40 });
        return o;
    }

    private async Task<long> SeedAsync(string label, string sessionId,
        SnapshotCapturedObject[] objs, CancellationToken ct)
    {
        long id = await _store.CreateSnapshotAsync(
            new SnapshotMeta { Label = label, GameSessionId = sessionId }, ct);
        int n = await _store.WriteChunkAsync(id, objs, ct);
        await _store.FinalizeSnapshotAsync(id, objs.Length, n, ct);
        return id;
    }

    private static DiscoveryQuery DQ(SpcJoinMode mode, long[] ids, int max = 200)
    {
        var q = new DiscoveryQuery { JoinMode = mode, MaxResults = max };
        q.SnapshotIds.AddRange(ids);
        return q;
    }

    private static DiscoveryCandidate? Find(DiscoveryResult r, string prop)
        => r.Rows.FirstOrDefault(x => x.PropName == prop);

    [Fact]
    public async Task ChangedField_Surfaces_ConstantDropped()
    {
        var ct = TestContext.Current.CancellationToken;
        const string path = "/Game/Map.Map:PersistentLevel.PlayerState_0";
        long s1 = await SeedAsync("t1", "S", new[] { Obj(1, "PlayerState", path,
            ("Gold", "IntProperty", 100), ("Level", "IntProperty", 5)) }, ct);
        long s2 = await SeedAsync("t2", "S", new[] { Obj(1, "PlayerState", path,
            ("Gold", "IntProperty", 50),  ("Level", "IntProperty", 5)) }, ct);

        var res = await _store.DiscoverChangesAsync(DQ(SpcJoinMode.Strict, new[] { s1, s2 }), ct);

        var gold = Find(res, "Gold");
        Assert.NotNull(gold);
        Assert.Null(Find(res, "Level"));            // constant → gated out
        Assert.Equal(1, gold!.InstancesChanged);
        Assert.Equal(1, gold.InstancesTotal);
        Assert.Equal("↓", gold.Direction);
        Assert.Equal("100  →  50", gold.SampleSequence);
        Assert.Equal(2, res.SnapshotCount);
    }

    [Fact]
    public async Task InterestingName_RanksAboveNeutral()
    {
        var ct = TestContext.Current.CancellationToken;
        const string path = "/Game/M.M:PersistentLevel.PlayerState_0";
        // Both fields move identically on one object; only the NAME differs.
        long s1 = await SeedAsync("a", "S", new[] { Obj(1, "PlayerState", path,
            ("Gold", "IntProperty", 100), ("ZqxPad", "IntProperty", 100)) }, ct);
        long s2 = await SeedAsync("b", "S", new[] { Obj(1, "PlayerState", path,
            ("Gold", "IntProperty", 50),  ("ZqxPad", "IntProperty", 50)) }, ct);

        var res = await _store.DiscoverChangesAsync(DQ(SpcJoinMode.Strict, new[] { s1, s2 }), ct);

        Assert.Equal("Gold", res.Rows[0].PropName);
        Assert.True(Find(res, "Gold")!.Score > Find(res, "ZqxPad")!.Score);
    }

    [Fact]
    public async Task CrossSession_Join_FindsStaminaAcrossRestarts()
    {
        var ct = TestContext.Current.CancellationToken;
        // Same identity (norm_path) but a different spawn counter + game session each
        // capture — the cross-session energy-bar case. Strict join merges them.
        long s1 = await SeedAsync("s1", "sess-A", new[] { Obj(7, "BP_Pawn_C",
            "/Game/M.M:PersistentLevel.BP_Pawn_C_0", ("Stamina", "IntProperty", 100)) }, ct);
        long s2 = await SeedAsync("s2", "sess-B", new[] { Obj(9, "BP_Pawn_C",
            "/Game/M.M:PersistentLevel.BP_Pawn_C_3", ("Stamina", "IntProperty", 30)) }, ct);

        var res = await _store.DiscoverChangesAsync(DQ(SpcJoinMode.Strict, new[] { s1, s2 }), ct);

        var stam = Find(res, "Stamina");
        Assert.NotNull(stam);
        Assert.Equal("100  →  30", stam!.SampleSequence);
        // Newest snapshot's live address is the Pivot / CE handoff target.
        Assert.Equal($"0x{0x7FF600000000 + 9:X}", stam.ObjAddr);
    }

    [Fact]
    public async Task ClassFilter_NarrowsCandidates()
    {
        var ct = TestContext.Current.CancellationToken;
        const string p1 = "/Game/M.M:PersistentLevel.PlayerState_0";
        const string p2 = "/Game/M.M:PersistentLevel.Monster_0";
        long s1 = await SeedAsync("a", "S", new[]
        {
            Obj(1, "PlayerState", p1, ("Score", "IntProperty", 10)),
            Obj(2, "Monster",     p2, ("Score", "IntProperty", 10)),
        }, ct);
        long s2 = await SeedAsync("b", "S", new[]
        {
            Obj(1, "PlayerState", p1, ("Score", "IntProperty", 20)),
            Obj(2, "Monster",     p2, ("Score", "IntProperty", 20)),
        }, ct);

        var all = await _store.DiscoverChangesAsync(DQ(SpcJoinMode.Strict, new[] { s1, s2 }), ct);
        Assert.Equal(2, all.Rows.Count);   // both classes' Score changed

        var q = DQ(SpcJoinMode.Strict, new[] { s1, s2 });
        q.ClassContains = "PlayerState";
        var filtered = await _store.DiscoverChangesAsync(q, ct);
        Assert.Equal("PlayerState", Assert.Single(filtered.Rows).ClassName);
    }

    [Fact]
    public async Task MaxResults_CapsAndFlagsTruncated()
    {
        var ct = TestContext.Current.CancellationToken;
        const string path = "/Game/M.M:PersistentLevel.PlayerState_0";
        // Five fields all move → five changed groups; cap at 2.
        long s1 = await SeedAsync("a", "S", new[] { Obj(1, "PlayerState", path,
            ("A", "IntProperty", 1), ("B", "IntProperty", 1), ("C", "IntProperty", 1),
            ("D", "IntProperty", 1), ("E", "IntProperty", 1)) }, ct);
        long s2 = await SeedAsync("b", "S", new[] { Obj(1, "PlayerState", path,
            ("A", "IntProperty", 2), ("B", "IntProperty", 2), ("C", "IntProperty", 2),
            ("D", "IntProperty", 2), ("E", "IntProperty", 2)) }, ct);

        var res = await _store.DiscoverChangesAsync(DQ(SpcJoinMode.Strict, new[] { s1, s2 }, max: 2), ct);
        Assert.Equal(2, res.Rows.Count);
        Assert.True(res.Truncated);
        Assert.Equal(5, res.ChangedGroups);   // counted before the cap
    }

    [Fact]
    public async Task FieldMissingInOneSnapshot_DropsCandidate()
    {
        var ct = TestContext.Current.CancellationToken;
        const string path = "/Game/M.M:PersistentLevel.PlayerState_0";
        // "Transient" exists only in s1, so the intersection drops it even though it
        // would otherwise have no pair to compare. HP survives + is found (it moved).
        long s1 = await SeedAsync("a", "S", new[] { Obj(1, "PlayerState", path,
            ("HP", "IntProperty", 100), ("Transient", "IntProperty", 5)) }, ct);
        long s2 = await SeedAsync("b", "S", new[] { Obj(1, "PlayerState", path,
            ("HP", "IntProperty", 90)) }, ct);

        var res = await _store.DiscoverChangesAsync(DQ(SpcJoinMode.Strict, new[] { s1, s2 }), ct);
        Assert.Equal("HP", Assert.Single(res.Rows).PropName);
    }

    [Fact]
    public async Task TooFewSnapshots_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        long s1 = await SeedAsync("a", "S", new[] { Obj(1, "PlayerState",
            "/Game/M.M:PersistentLevel.PlayerState_0", ("HP", "IntProperty", 100)) }, ct);
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.DiscoverChangesAsync(DQ(SpcJoinMode.Strict, new[] { s1 }), ct));
    }

    [Fact]
    public async Task DuplicateSnapshotIds_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        long s1 = await SeedAsync("a", "S", new[] { Obj(1, "PlayerState",
            "/Game/M.M:PersistentLevel.PlayerState_0", ("HP", "IntProperty", 100)) }, ct);
        // [s1, s1] passes a raw count==2 check but is 1 distinct snapshot — must throw
        // ArgumentException, not emit malformed SQL / surface an opaque failure.
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.DiscoverChangesAsync(DQ(SpcJoinMode.Strict, new[] { s1, s1 }), ct));
    }

    [Fact]
    public async Task SpawnSiblings_RepresentativeIsSelfConsistent_NotMixedAcrossSiblings()
    {
        var ct = TestContext.Current.CancellationToken;
        // Two physical siblings of one class share a normalized path (the _N spawn
        // counter is stripped). They move to DIFFERENT values; independent per-column
        // MINs would mix them (sequence from one sibling, address from another). The
        // fix de-dups to ONE sibling per snapshot (min gobjects_index = idx 1), so the
        // representative is internally consistent. This would FAIL pre-fix: independent
        // MIN picks idx 2's smaller values (100→50, ↓) but idx 1's smaller address.
        long s1 = await SeedAsync("a", "S", new[]
        {
            Obj(1, "BP_Enemy_C", "/G.M:L.BP_Enemy_C_0", ("HP", "IntProperty", 200)),
            Obj(2, "BP_Enemy_C", "/G.M:L.BP_Enemy_C_1", ("HP", "IntProperty", 100)),
        }, ct);
        long s2 = await SeedAsync("b", "S", new[]
        {
            Obj(1, "BP_Enemy_C", "/G.M:L.BP_Enemy_C_0", ("HP", "IntProperty", 210)),
            Obj(2, "BP_Enemy_C", "/G.M:L.BP_Enemy_C_1", ("HP", "IntProperty", 50)),
        }, ct);

        var res = await _store.DiscoverChangesAsync(DQ(SpcJoinMode.Strict, new[] { s1, s2 }), ct);
        var hp = Find(res, "HP");
        Assert.NotNull(hp);
        Assert.Equal("200  →  210", hp!.SampleSequence);          // idx 1's sequence …
        Assert.Equal("↑", hp.Direction);                           // … consistent direction …
        Assert.Equal($"0x{0x7FF600000000 + 1:X}", hp.ObjAddr);     // … and idx 1's address
    }

    [Fact]
    public async Task ThreeSnapshots_StableThenChanged_FoundAsOneTime()
    {
        var ct = TestContext.Current.CancellationToken;
        const string path = "/Game/M.M:PersistentLevel.PlayerState_0";
        // The user's "不變、不變、有變" case: constant across s1→s2, moves at s3.
        long s1 = await SeedAsync("t1", "S", new[] { Obj(1, "PlayerState", path, ("Mana", "IntProperty", 50)) }, ct);
        long s2 = await SeedAsync("t2", "S", new[] { Obj(1, "PlayerState", path, ("Mana", "IntProperty", 50)) }, ct);
        long s3 = await SeedAsync("t3", "S", new[] { Obj(1, "PlayerState", path, ("Mana", "IntProperty", 80)) }, ct);

        var res = await _store.DiscoverChangesAsync(DQ(SpcJoinMode.Strict, new[] { s1, s2, s3 }), ct);

        var mana = Find(res, "Mana");
        Assert.NotNull(mana);
        Assert.Equal(3, res.SnapshotCount);
        Assert.Equal("one-time", mana!.ShapeLabel);              // changed in exactly one interval
        Assert.Equal("50  →  50  →  80", mana.SampleSequence);
        Assert.Equal("↑", mana.Direction);
    }

    [Fact]
    public async Task FourSnapshots_OneTimeEvent_OutranksPerFrameJitter()
    {
        var ct = TestContext.Current.CancellationToken;
        const string path = "/Game/M.M:PersistentLevel.PlayerState_0";
        // Neutral names + same changed-count + same population, so SHAPE decides.
        // "Aaa": stable,stable,stable,moved = one-time event. "Bbb": jitters every step.
        long s1 = await SeedAsync("a", "S", new[] { Obj(1, "PlayerState", path, ("Aaa", "IntProperty", 1), ("Bbb", "IntProperty", 1)) }, ct);
        long s2 = await SeedAsync("b", "S", new[] { Obj(1, "PlayerState", path, ("Aaa", "IntProperty", 1), ("Bbb", "IntProperty", 9)) }, ct);
        long s3 = await SeedAsync("c", "S", new[] { Obj(1, "PlayerState", path, ("Aaa", "IntProperty", 1), ("Bbb", "IntProperty", 2)) }, ct);
        long s4 = await SeedAsync("d", "S", new[] { Obj(1, "PlayerState", path, ("Aaa", "IntProperty", 7), ("Bbb", "IntProperty", 8)) }, ct);

        var res = await _store.DiscoverChangesAsync(DQ(SpcJoinMode.Strict, new[] { s1, s2, s3, s4 }), ct);

        var aaa = Find(res, "Aaa");
        var bbb = Find(res, "Bbb");
        Assert.NotNull(aaa);
        Assert.NotNull(bbb);
        Assert.Equal("one-time", aaa!.ShapeLabel);
        Assert.True(aaa.Score > bbb!.Score);                    // shape promotes the discrete event
        Assert.Equal("Aaa", res.Rows[0].PropName);
    }

    // [DISCOVER-SHADOWED-POOL] review: the Strict path feeds Rank only the rows that CHANGED, so when one copy of a shadowed
    // name stays put Rank saw one offset and gave the moved copy the bare name -- while Class Pivot's picker, built from
    // the whole field list, lists both with offsets.
    [Fact]
    public async Task OneMovedCopyOfAShadowedName_IsLabelledWithItsOffset_LikeThePicker()
    {
        var ct = TestContext.Current.CancellationToken;
        long s1 = await SeedAsync("t1", "S", new[] { PivotStoreTests.ShotSection(1, "/G.M:L.ShotA", 5) }, ct);
        long s2 = await SeedAsync("t2", "S", new[] { PivotStoreTests.ShotSection(1, "/G.M:L.ShotA", 9) }, ct);

        var res = await _store.DiscoverChangesAsync(DQ(SpcJoinMode.Strict, new[] { s1, s2 }), ct);

        var row = Assert.Single(res.Rows, x => x.PropName == "PrerollTime");   // only the +296 copy moved
        Assert.Equal(296, row.PropOffset);
        Assert.Equal("PrerollTime@+0x128", row.PropLabel);
    }
}
