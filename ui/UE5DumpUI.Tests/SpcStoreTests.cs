using System.IO;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// End-to-end SPC engine tests against a real (temp-file) SQLite store. Seeds
/// several snapshots, then asserts the directional predicate chain isolates the
/// right field — including the headline energy-bar case (value-unknown,
/// type-unknown, multi-session) and the money case (decreased twice). See
/// docs/experimental-snapshot-spc-pivot.md §"Phase B" / §1.
/// </summary>
public class SpcStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public SpcStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UE5DumpSpcTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(new MockPlatformService(_tempDir));
        _store.SetActiveGame("SPCGAME");
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

    // Seed one snapshot containing the given objects; returns its id.
    private async Task<long> SeedAsync(string label, string sessionId,
        SnapshotCapturedObject[] objs, CancellationToken ct)
    {
        long id = await _store.CreateSnapshotAsync(
            new SnapshotMeta { Label = label, GameSessionId = sessionId }, ct);
        int n = await _store.WriteChunkAsync(id, objs, ct);
        await _store.FinalizeSnapshotAsync(id, objs.Length, n, ct);
        return id;
    }

    private static SpcQuery Chain(SpcJoinMode mode, long[] ids, params SpcPredicateKind[] preds)
    {
        var q = new SpcQuery { JoinMode = mode };
        q.SnapshotIds.AddRange(ids);
        q.Predicates.AddRange(preds);
        return q;
    }

    [Fact]
    public async Task Money_DecreasedTwice_IsolatesGold()
    {
        var ct = TestContext.Current.CancellationToken;
        const string path = "/Game/Map.Map:PersistentLevel.PlayerState_0";
        // Gold drains 10160 -> 9910 -> 9410; Level stays 5; Score rises.
        long s1 = await SeedAsync("t1", "S", new[] { Obj(1, "PlayerState", path,
            ("Gold", "IntProperty", 10160), ("Level", "IntProperty", 5), ("Score", "IntProperty", 100)) }, ct);
        long s2 = await SeedAsync("t2", "S", new[] { Obj(1, "PlayerState", path,
            ("Gold", "IntProperty", 9910), ("Level", "IntProperty", 5), ("Score", "IntProperty", 150)) }, ct);
        long s3 = await SeedAsync("t3", "S", new[] { Obj(1, "PlayerState", path,
            ("Gold", "IntProperty", 9410), ("Level", "IntProperty", 5), ("Score", "IntProperty", 200)) }, ct);

        var res = await _store.SpcQueryAsync(Chain(SpcJoinMode.Strict, new[] { s1, s2, s3 },
            SpcPredicateKind.Any, SpcPredicateKind.Decreased, SpcPredicateKind.Decreased), ct);

        var row = Assert.Single(res.Rows);
        Assert.Equal("Gold", row.PropName);
        Assert.Equal(new[] { "10160", "9910", "9410" }, row.Values.ToArray());
        Assert.Equal(3, res.SnapshotCount);
    }

    [Fact]
    public async Task AbsolutePredicate_Between_CutsDirectionalNoise()
    {
        var ct = TestContext.Current.CancellationToken;
        // Both fields Decrease, but WidthOverride is UI noise (1920 -> 0) and HP is the
        // real gameplay value (300 -> 120).
        long a = await SeedAsync("a", "S", new[]
        {
            Obj(1, "SizeBox",     "/Game/UI.UI:Tree.SizeBox",      ("WidthOverride", "IntProperty", 1920)),
            Obj(2, "PlayerState", "/Game/M.M:L.PlayerState_0",     ("HP",            "IntProperty", 300)),
        }, ct);
        long b = await SeedAsync("b", "S", new[]
        {
            Obj(1, "SizeBox",     "/Game/UI.UI:Tree.SizeBox",      ("WidthOverride", "IntProperty", 0)),
            Obj(2, "PlayerState", "/Game/M.M:L.PlayerState_0",     ("HP",            "IntProperty", 120)),
        }, ct);

        // Directional-only: both decreases match.
        var bare = Chain(SpcJoinMode.Strict, new[] { a, b }, SpcPredicateKind.Any, SpcPredicateKind.Decreased);
        Assert.Equal(2, (await _store.SpcQueryAsync(bare, ct)).Rows.Count);

        // Add a value window (both snapshots Between 0–500): the 1920 baseline fails,
        // so only the real gameplay HP survives.
        var windowed = Chain(SpcJoinMode.Strict, new[] { a, b }, SpcPredicateKind.Any, SpcPredicateKind.Decreased);
        windowed.AbsolutePredicates.Add(new SpcAbsolutePredicate { Kind = SpcAbsoluteKind.Between, Low = 0, High = 500 });
        windowed.AbsolutePredicates.Add(new SpcAbsolutePredicate { Kind = SpcAbsoluteKind.Between, Low = 0, High = 500 });

        var row = Assert.Single((await _store.SpcQueryAsync(windowed, ct)).Rows);
        Assert.Equal("HP", row.PropName);
        Assert.Equal(new[] { "300", "120" }, row.Values.ToArray());
    }

    [Fact]
    public async Task EnergyBar_SameSameDownUp_AcrossSessions_IsolatesStamina()
    {
        var ct = TestContext.Current.CancellationToken;
        // Same object identity (norm_path) but a different FName spawn counter and
        // a different game session each capture — the cross-session driver case.
        // Stamina: full, full, drained, refilled. A distractor Counter only rises.
        long s1 = await SeedAsync("s1", "sess-A", new[] { Obj(7, "BP_Pawn_C",
            "/Game/M.M:PersistentLevel.BP_Pawn_C_0", ("Stamina", "IntProperty", 100), ("Counter", "IntProperty", 1)) }, ct);
        long s2 = await SeedAsync("s2", "sess-A", new[] { Obj(7, "BP_Pawn_C",
            "/Game/M.M:PersistentLevel.BP_Pawn_C_0", ("Stamina", "IntProperty", 100), ("Counter", "IntProperty", 2)) }, ct);
        long s3 = await SeedAsync("s3", "sess-B", new[] { Obj(9, "BP_Pawn_C",
            "/Game/M.M:PersistentLevel.BP_Pawn_C_3", ("Stamina", "IntProperty", 30), ("Counter", "IntProperty", 3)) }, ct);
        long s4 = await SeedAsync("s4", "sess-B", new[] { Obj(9, "BP_Pawn_C",
            "/Game/M.M:PersistentLevel.BP_Pawn_C_3", ("Stamina", "IntProperty", 80), ("Counter", "IntProperty", 4)) }, ct);

        var res = await _store.SpcQueryAsync(Chain(SpcJoinMode.Strict, new[] { s1, s2, s3, s4 },
            SpcPredicateKind.Any, SpcPredicateKind.Unchanged,
            SpcPredicateKind.Decreased, SpcPredicateKind.Increased), ct);

        var row = Assert.Single(res.Rows);
        Assert.Equal("Stamina", row.PropName);
        Assert.Equal(new[] { "100", "100", "30", "80" }, row.Values.ToArray());
        // The newest snapshot's live address is the CE-export handoff target.
        Assert.Equal($"0x{0x7FF600000000 + 9:X}", row.ObjAddr);
    }

    [Fact]
    public async Task NormPathJoin_MergesSpawnCounterAcrossSnapshots()
    {
        var ct = TestContext.Current.CancellationToken;
        // Different leaf spawn counter each snapshot — Strict join must still
        // merge them because NormalizePath strips the trailing _<n>.
        long s1 = await SeedAsync("a", "S", new[] { Obj(1, "BP_Enemy_C",
            "/Game/M.M:PersistentLevel.BP_Enemy_C_12", ("HP", "IntProperty", 50)) }, ct);
        long s2 = await SeedAsync("b", "S", new[] { Obj(1, "BP_Enemy_C",
            "/Game/M.M:PersistentLevel.BP_Enemy_C_47", ("HP", "IntProperty", 40)) }, ct);

        var res = await _store.SpcQueryAsync(Chain(SpcJoinMode.Strict, new[] { s1, s2 },
            SpcPredicateKind.Any, SpcPredicateKind.Decreased), ct);
        var row = Assert.Single(res.Rows);
        Assert.Equal("HP", row.PropName);
        Assert.Equal("/Game/M.M:PersistentLevel.BP_Enemy_C", row.NormPath);
    }

    [Fact]
    public async Task Increased_And_Filters_Work()
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

        // Increased matches both objects' Score...
        var all = await _store.SpcQueryAsync(Chain(SpcJoinMode.Strict, new[] { s1, s2 },
            SpcPredicateKind.Any, SpcPredicateKind.Increased), ct);
        Assert.Equal(2, all.Rows.Count);

        // ...class filter narrows to PlayerState only.
        var q = Chain(SpcJoinMode.Strict, new[] { s1, s2 },
            SpcPredicateKind.Any, SpcPredicateKind.Increased);
        q.ClassContains = "PlayerState";
        var filtered = await _store.SpcQueryAsync(q, ct);
        Assert.Equal("PlayerState", Assert.Single(filtered.Rows).ClassName);
    }

    [Fact]
    public async Task UnchangedChain_KeepsOnlyStableFields()
    {
        var ct = TestContext.Current.CancellationToken;
        const string path = "/Game/M.M:PersistentLevel.PlayerState_0";
        long s1 = await SeedAsync("a", "S", new[] { Obj(1, "PlayerState", path,
            ("MaxHP", "IntProperty", 100), ("HP", "IntProperty", 100)) }, ct);
        long s2 = await SeedAsync("b", "S", new[] { Obj(1, "PlayerState", path,
            ("MaxHP", "IntProperty", 100), ("HP", "IntProperty", 70)) }, ct);

        var res = await _store.SpcQueryAsync(Chain(SpcJoinMode.Strict, new[] { s1, s2 },
            SpcPredicateKind.Any, SpcPredicateKind.Unchanged), ct);
        Assert.Equal("MaxHP", Assert.Single(res.Rows).PropName);
    }

    [Fact]
    public async Task MissingFieldInOneSnapshot_DropsCandidate()
    {
        var ct = TestContext.Current.CancellationToken;
        const string path = "/Game/M.M:PersistentLevel.PlayerState_0";
        // "Transient" exists only in s1, so the intersection join drops it.
        long s1 = await SeedAsync("a", "S", new[] { Obj(1, "PlayerState", path,
            ("HP", "IntProperty", 100), ("Transient", "IntProperty", 5)) }, ct);
        long s2 = await SeedAsync("b", "S", new[] { Obj(1, "PlayerState", path,
            ("HP", "IntProperty", 90)) }, ct);

        var res = await _store.SpcQueryAsync(Chain(SpcJoinMode.Strict, new[] { s1, s2 },
            SpcPredicateKind.Any, SpcPredicateKind.Any), ct);
        Assert.Equal("HP", Assert.Single(res.Rows).PropName);
    }

    // Same as Obj(), but each field carries its OWN offset. Needed because the Strict join
    // key includes prop_offset, and Obj() hardcodes 0x40 for every field -- which silently
    // makes that key term untestable.
    private static SnapshotCapturedObject ObjAt(int idx, string cls, string path,
        params (string name, string type, int val, int off)[] fields)
    {
        var o = new SnapshotCapturedObject
        {
            Index = idx, Addr = $"0x{0x7FF600000000 + idx:X}",
            Name = path[(path.LastIndexOf('.') + 1)..], ClassName = cls,
            OuterClassName = "World", Path = path,
        };
        foreach (var (name, type, val, off) in fields)
            o.Fields.Add(new SnapshotCapturedField { Name = name, Type = type, Hex = IntHex(val), Offset = off });
        return o;
    }

    /// <summary>
    /// Native-C raw holes survive the SPC join and track their values across snapshots.
    ///
    /// This is the SPC-Query arm of the P3 acceptance list
    /// (native-c-value-scan-spec.md: "capture native_c on two snapshots -> SPC query on a
    /// &lt;raw@0x..&gt; field joins by offset"). The 2026-09-06 in-game run proved the Class
    /// Pivot half and recorded, honestly, that it had used the Snapshot panel's *Compare
    /// snapshots* rather than SPC Query -- so this arm was still owed.
    ///
    /// Confirmed against the REAL capture from that run before this test was written
    /// (snapshots.6A9C1C8410F23000.db, snapshots 2 and 3, 77 s apart): all 8,556
    /// &lt;raw@0x..&gt; fields join under the Strict key, none has a zero/absent prop_offset,
    /// and 8 changed -- four of them on DumperTestActor itself, including
    /// &lt;raw@0x918&gt; 4684 -> 5829, whose 5829 is the exact pivot group key that run
    /// recorded. That also answers the run's second open note ("DumperTestActor's own raw
    /// rows did not appear in the changed list ... not chased"): they do.
    /// </summary>
    [Fact]
    public async Task NativeCRawHoles_JoinAcrossSnapshots_AndTrackTheirValues()
    {
        var ct = TestContext.Current.CancellationToken;
        const string path = "/Game/Map.Map:PersistentLevel.DumperTestActor_1";
        // A raw hole that moves, a raw hole that does not, and a reflected control.
        long s1 = await SeedAsync("t1", "S", new[] { ObjAt(1, "DumperTestActor", path,
            ("<raw@0x6D0>", "IntProperty", 702184, 0x6D0),
            ("<raw@0x918>", "IntProperty", 4684,   0x918),
            ("TickCount",   "IntProperty", 312,    0x1C0)) }, ct);
        long s2 = await SeedAsync("t2", "S", new[] { ObjAt(1, "DumperTestActor", path,
            ("<raw@0x6D0>", "IntProperty", 702716, 0x6D0),
            ("<raw@0x918>", "IntProperty", 4684,   0x918),
            ("TickCount",   "IntProperty", 388,    0x1C0)) }, ct);

        var res = await _store.SpcQueryAsync(Chain(SpcJoinMode.Strict, new[] { s1, s2 },
            SpcPredicateKind.Any, SpcPredicateKind.Increased), ct);

        // Both the raw hole and the reflected control rose; the static raw hole did not.
        Assert.Equal(new[] { "<raw@0x6D0>", "TickCount" },
                     res.Rows.Select(r => r.PropName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        var raw = res.Rows.Single(r => r.PropName == "<raw@0x6D0>");
        Assert.Equal(new[] { "702184", "702716" }, raw.Values.ToArray());
    }

    /// <summary>
    /// The Strict join key's prop_offset term is LOAD-BEARING: a field with the same class,
    /// path and name at a DIFFERENT offset must not join to it.
    ///
    /// Without this, "joins by offset" would be satisfied only incidentally for raw holes,
    /// because a &lt;raw@0xNN&gt; name already encodes its own offset -- so a broken offset
    /// term would be invisible on exactly the rows the P3 acceptance is about.
    /// </summary>
    [Fact]
    public async Task SpcStrictKey_OffsetIsPartOfTheJoin_SameNameDifferentOffsetDoesNotJoin()
    {
        var ct = TestContext.Current.CancellationToken;
        const string path = "/Game/Map.Map:PersistentLevel.Actor_1";
        // ⚠ "Moved" alone would be asserted with Assert.Empty, which also passes when the
        // seeding silently fails -- i.e. it cannot tell "the offset term bit" from "nothing
        // was stored". So the object carries a CONTROL field that keeps its offset and MUST
        // join. One row, and it is the control: that is only true if both halves work.
        long s1 = await SeedAsync("t1", "S", new[] { ObjAt(1, "Actor", path,
            ("Value",  "IntProperty", 100, 0x40),
            ("Anchor", "IntProperty", 7,   0xC0)) }, ct);
        long s2 = await SeedAsync("t2", "S", new[] { ObjAt(1, "Actor", path,
            ("Value",  "IntProperty", 200, 0x80),    // same name, MOVED -> must not join
            ("Anchor", "IntProperty", 9,   0xC0)) }, ct);   // same offset  -> must join

        var res = await _store.SpcQueryAsync(Chain(SpcJoinMode.Strict, new[] { s1, s2 },
            SpcPredicateKind.Any, SpcPredicateKind.Any), ct);

        Assert.Equal("Anchor", Assert.Single(res.Rows).PropName);
    }
}
