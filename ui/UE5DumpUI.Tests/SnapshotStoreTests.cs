using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

public class SnapshotStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public SnapshotStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UE5DumpSnapTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(new MockPlatformService(_tempDir));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* WAL files may linger briefly; best effort */ }
    }

    private static SnapshotCapturedObject MakeObject(int index, params (string name, string type, string hex)[] fields)
    {
        var o = new SnapshotCapturedObject
        {
            Index = index,
            Addr = $"0x{index:X}",
            Name = $"BP_Player_C_{index}",
            ClassName = "BP_Player_C",
            OuterClassName = "World",
            Path = $"/Game/Map.Map:PersistentLevel.BP_Player_C_{index}",
        };
        foreach (var (name, type, hex) in fields)
            o.Fields.Add(new SnapshotCapturedField { Name = name, Type = type, Hex = hex, Offset = 0 });
        return o;
    }

    // An object whose ONLY rows are struct-array elements (no scalar fields) — so it lands
    // in the is_array=1 pivot bucket, not is_array=0.
    private static SnapshotCapturedObject EnemyWithArray(int index)
    {
        var o = new SnapshotCapturedObject
        {
            Index = index, Addr = $"0x{index:X}", Name = $"BP_Enemy_C_{index}",
            ClassName = "BP_Enemy_C", OuterClassName = "World",
            Path = $"/Game/Map.Map:PersistentLevel.BP_Enemy_C_{index}",
        };
        var arr = new SnapshotCapturedArray { Field = "Tunes" };
        arr.Elements.Add(new SnapshotCapturedArrayElement
        {
            Index = 0,
            Fields = { new SnapshotCapturedField { Name = "V", Type = "IntProperty", Hex = "01000000" } },
        });
        o.Arrays.Add(arr);
        return o;
    }

    [Fact]
    public async Task CaptureSession_IncrementalPivotCounts_MatchGroupByBuild()
    {
        var ct = TestContext.Current.CancellationToken;
        SnapshotCapturedObject[] Chunk1() => new[]
        {
            MakeObject(1, ("HP", "IntProperty", "0A000000")),
            MakeObject(2, ("HP", "IntProperty", "14000000")),
            EnemyWithArray(3),
        };
        SnapshotCapturedObject[] Chunk2() => new[]
        {
            MakeObject(4, ("HP", "IntProperty", "1E000000")),
            EnemyWithArray(5),
        };

        // (A) Capture via the bulk session → incremental pivot counts (no GROUP BY).
        long idA = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "A", Scope = "NumericNoByte" }, ct);
        int objA = 0, fieldA = 0;
        await using (var s = await _store.BeginCaptureSessionAsync(ct))
        {
            var c1 = Chunk1(); var c2 = Chunk2();
            fieldA += s.WriteChunk(idA, c1, ct); objA += c1.Length;
            fieldA += s.WriteChunk(idA, c2, ct); objA += c2.Length;
            await s.CompleteSnapshotAsync(idA, objA, fieldA, ct: ct);
        }

        // (B) Identical data via the one-shot path + the GROUP-BY finalize (the oracle).
        long idB = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "B", Scope = "NumericNoByte" }, ct);
        var all = Chunk1().Concat(Chunk2()).ToArray();
        int fieldB = await _store.WriteChunkAsync(idB, all, ct);
        await _store.FinalizeSnapshotAsync(idB, all.Length, fieldB, ct);

        Assert.Equal(fieldB, fieldA);   // same rows written
        static string Render(IReadOnlyList<PivotClassInfo> xs) =>
            string.Join("|", xs.Select(x => $"{x.ClassName}={x.InstanceCount}").OrderBy(t => t));
        // Scalar (is_array=0) + struct-array (is_array=1) counts must match the GROUP BY.
        Assert.Equal(Render(await _store.ListPivotClassesAsync(idB, ct)),
                     Render(await _store.ListPivotClassesAsync(idA, ct)));
        Assert.Equal(Render(await _store.ListPivotArrayClassesAsync(idB, ct)),
                     Render(await _store.ListPivotArrayClassesAsync(idA, ct)));
    }

    [Fact]
    public async Task CaptureSession_CurrentSizeBytes_ReflectsUncommittedRows()
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "sz", Scope = "NumericNoByte" }, ct);
        await using var s = await _store.BeginCaptureSessionAsync(ct);

        long before = s.CurrentSizeBytes();
        // A single chunk written into the OPEN transaction (no commit yet, < CommitEveryChunks).
        s.WriteChunk(id, new[]
        {
            MakeObject(1, ("HP", "IntProperty", "0A000000"), ("MP", "FloatProperty", "0000803F")),
            MakeObject(2, ("HP", "IntProperty", "14000000")),
        }, ct);

        // The gauge must rise immediately — the cap can't wait for the every-16 commit to
        // flush the page cache to the -wal, or it would overshoot by up to one commit batch.
        Assert.True(s.CurrentSizeBytes() > before,
            $"CurrentSizeBytes should grow with uncommitted rows (before={before}, after={s.CurrentSizeBytes()})");
    }

    [Fact]
    public async Task IsUsableFlag_RoundTrips_AndDeleteUnusableRemovesOnlyFlagged()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.SetActiveGame("USABLE");

        // A clean (usable) snapshot via the streaming finalize.
        long good = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "good", Scope = "NumericNoByte" }, ct);
        await using (var s = await _store.BeginCaptureSessionAsync(ct))
        {
            int n = s.WriteChunk(good, new[] { MakeObject(1, ("HP", "IntProperty", "0A000000")) }, ct);
            await s.CompleteSnapshotAsync(good, 1, n, isUsable: true, ct: ct);
        }

        // A drift-flagged (unusable) snapshot.
        long bad = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "bad", Scope = "NumericNoByte" }, ct);
        await using (var s = await _store.BeginCaptureSessionAsync(ct))
        {
            int n = s.WriteChunk(bad, new[] { MakeObject(2, ("HP", "IntProperty", "14000000")) }, ct);
            await s.CompleteSnapshotAsync(bad, 1, n, isUsable: false, ct: ct);
        }

        // The flag round-trips through the additive is_usable column.
        var list = await _store.ListSnapshotsAsync(ct);
        Assert.True(list.Single(m => m.Id == good).IsUsable);
        Assert.False(list.Single(m => m.Id == bad).IsUsable);

        // Auto-clean removes ONLY the flagged one; the good snapshot survives.
        int removed = await _store.DeleteUnusableSnapshotsAsync(ct);
        Assert.Equal(1, removed);
        var after = await _store.ListSnapshotsAsync(ct);
        Assert.Single(after);
        Assert.Equal(good, after[0].Id);

        // A second call is a no-op (nothing left flagged).
        Assert.Equal(0, await _store.DeleteUnusableSnapshotsAsync(ct));
    }

    // [W1-PARTIAL-MARK] A cap / low-disk partial is KEPT usable by design, so its marker is a column of its
    // own. The refuted "obvious" repair (is_usable=0) would have handed it to the auto-clean below.
    [Fact]
    public async Task PartialReason_RoundTrips_AndAPartialSurvivesTheUnusableAutoClean()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.SetActiveGame("PARTIAL");

        long capped = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "capped", Scope = "NumericNoByte" }, ct);
        await using (var s = await _store.BeginCaptureSessionAsync(ct))
        {
            int n = s.WriteChunk(capped, new[] { MakeObject(1, ("HP", "IntProperty", "0A000000")) }, ct);
            await s.CompleteSnapshotAsync(capped, 1, n, isUsable: true, partialReason: Constants.SnapshotPartialCap, ct: ct);
        }
        long whole = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "whole", Scope = "NumericNoByte" }, ct);
        await using (var s = await _store.BeginCaptureSessionAsync(ct))
        {
            int n = s.WriteChunk(whole, new[] { MakeObject(2, ("HP", "IntProperty", "14000000")) }, ct);
            await s.CompleteSnapshotAsync(whole, 1, n, ct: ct);
        }

        var list = await _store.ListSnapshotsAsync(ct);
        var c = list.Single(m => m.Id == capped);
        Assert.Equal(Constants.SnapshotPartialCap, c.PartialReason);
        Assert.True(c.IsPartial);
        Assert.True(c.IsUsable);
        Assert.Equal("", list.Single(m => m.Id == whole).PartialReason);

        // The whole point of a separate marker: the auto-clean leaves the partial alone.
        Assert.Equal(0, await _store.DeleteUnusableSnapshotsAsync(ct));
        Assert.Equal(2, (await _store.ListSnapshotsAsync(ct)).Count);
    }

    [Fact]
    public void PartialMarker_ShowsInTheGridLabel_AndEveryPickerLine()
    {
        const string at = "2026-09-12T10:00:00Z";
        var capped  = new SnapshotMeta { Label = "run", CapturedAt = at, PartialReason = Constants.SnapshotPartialCap };
        var lowDisk = new SnapshotMeta { Label = "run", CapturedAt = at, PartialReason = Constants.SnapshotPartialDiskLow };
        var whole   = new SnapshotMeta { Label = "run", CapturedAt = at };

        Assert.Contains("partial: stopped at the size cap", capped.LabelDisplay);
        Assert.Contains("partial: stopped at the size cap", capped.PickerDisplay);
        Assert.Contains("partial: stopped on low disk", lowDisk.LabelDisplay);
        Assert.Contains("partial: stopped on low disk", lowDisk.PickerDisplay);
        Assert.Equal("run", whole.LabelDisplay);
        Assert.DoesNotContain("partial", whole.PickerDisplay);

        // An unusable partial keeps BOTH marks.
        var both = new SnapshotMeta { Label = "run", IsUsable = false, PartialReason = Constants.SnapshotPartialCap };
        Assert.StartsWith("⚠ ", both.LabelDisplay);
        Assert.Contains("partial", both.LabelDisplay);
    }

    [Fact]
    public async Task DeleteSnapshot_Reclaim_ShrinksDbFileOnDisk()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.SetActiveGame("RECLAIM");
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "big", Scope = "NumericNoByte" }, ct);

        // Write enough rows that the DB file grows well past the empty-schema baseline.
        var objs = new List<SnapshotCapturedObject>();
        for (int i = 0; i < 3000; i++)
            objs.Add(MakeObject(i,
                ("HP", "IntProperty", "0A000000"),
                ("MP", "FloatProperty", "0000803F"),
                ("XP", "Int64Property", "0100000000000000")));
        await _store.WriteChunkAsync(id, objs, ct);
        await _store.FinalizeSnapshotAsync(id, objs.Count, objs.Count * 3, ct);

        // Fold the WAL into the main .db so its file size reflects the written data.
        await _store.GetUsageAsync(ct);   // does wal_checkpoint(TRUNCATE)
        long beforeBytes = new FileInfo(_store.DatabasePath).Length;
        Assert.True(beforeBytes > 100_000, $"sanity: data should have grown the file (was {beforeBytes})");

        // reclaim:true must VACUUM so the freed space is returned to the OS (the
        // capture-cancel contract) — DELETE alone would leave beforeBytes unchanged.
        await _store.DeleteSnapshotAsync(id, reclaim: true, ct);

        Assert.Empty(await _store.ListSnapshotsAsync(ct));
        long afterBytes = new FileInfo(_store.DatabasePath).Length;
        Assert.True(afterBytes < beforeBytes,
            $"reclaim should shrink the file on disk (before={beforeBytes}, after={afterBytes})");
    }

    [Fact]
    public async Task DeleteAll_WithAnotherConnectionOpen_DoesNotThrow_ClearsData_DbStaysUsable()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.SetActiveGame("LOCKED");
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "x", Scope = "NumericNoByte" }, ct);
        await _store.WriteChunkAsync(id, new[] { MakeObject(1, ("HP", "IntProperty", "0A000000")) }, ct);
        await _store.FinalizeSnapshotAsync(id, 1, 1, ct);

        // Hold a SECOND connection open on the same DB — the real-app condition (an idle
        // reader from another panel, or a just-finished capture session) that made the
        // exclusive journal_mode=DELETE in the disk reclaim throw 'database is locked' and
        // bubble out of Delete All (leaving the list stale + the DB wedged).
        await using var other = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _store.DatabasePath }.ToString());
        await other.OpenAsync(ct);
        await using (var probe = other.CreateCommand())
        {
            probe.CommandText = "SELECT COUNT(*) FROM snapshots;";
            await probe.ExecuteScalarAsync(ct);   // actively attach the connection to the DB
        }

        // Must NOT throw (reclaim is best-effort) and must clear the data.
        await _store.DeleteAllSnapshotsAsync(ct);
        Assert.Empty(await _store.ListSnapshotsAsync(ct));

        // The DB must be left usable (WAL restored, no wedged lock) — a follow-up write works.
        long id2 = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "y", Scope = "NumericNoByte" }, ct);
        await _store.WriteChunkAsync(id2, new[] { MakeObject(2, ("HP", "IntProperty", "14000000")) }, ct);
        Assert.Single(await _store.ListSnapshotsAsync(ct));
    }

    [Fact]
    public async Task CreateWriteListDelete_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var meta = new SnapshotMeta
        {
            Label = "first", CapturedAt = "2026-06-02T00:00:00Z",
            PeHash = "ABCD", GameSessionId = "ABCD-1", UeVersion = 504, Scope = "NumericNoByte",
        };
        long id = await _store.CreateSnapshotAsync(meta, ct);
        Assert.True(id > 0);

        int rows = await _store.WriteChunkAsync(id, new[]
        {
            MakeObject(1, ("Health", "FloatProperty", "0000803F"), ("Ammo", "IntProperty", "1E000000")),
            MakeObject(2, ("Health", "FloatProperty", "0000003F")),
        }, ct);
        Assert.Equal(3, rows);

        await _store.FinalizeSnapshotAsync(id, objectCount: 2, fieldCount: rows, ct);

        var list = await _store.ListSnapshotsAsync(ct);
        var saved = Assert.Single(list);
        Assert.Equal(id, saved.Id);
        Assert.Equal("first", saved.Label);
        Assert.Equal("ABCD", saved.PeHash);
        Assert.Equal(504, saved.UeVersion);
        Assert.Equal(2, saved.ObjectCount);
        Assert.Equal(3, saved.FieldCount);

        await _store.DeleteSnapshotAsync(id, ct: ct);
        Assert.Empty(await _store.ListSnapshotsAsync(ct));
    }

    [Fact]
    public async Task Schema_DenormalizedFields_StoresIdentityPerRow()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.SetActiveGame("DENORM");
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "n" }, ct);
        await _store.WriteChunkAsync(id, new[]
        {
            MakeObject(1, ("A", "IntProperty", "01000000"),
                          ("B", "IntProperty", "02000000"),
                          ("C", "IntProperty", "03000000")),
        }, ct);
        await _store.FinalizeSnapshotAsync(id, 1, 3, ct);

        await using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _store.DatabasePath }.ToString());
        await conn.OpenAsync(ct);

        // Denormalised: identity columns live on `fields` (the fast single-table
        // covering-index layout). No `objects` table, no `vfields` view.
        Assert.Equal(3, await ScalarAsync(conn, "SELECT COUNT(*) FROM fields", ct));
        Assert.Equal(1, await ScalarAsync(conn,
            "SELECT COUNT(*) FROM pragma_table_info('fields') WHERE name='norm_path'", ct));
        Assert.Equal(0, await ScalarAsync(conn,
            "SELECT COUNT(*) FROM sqlite_master WHERE name='objects'", ct));
        Assert.Equal(3, await ScalarAsync(conn,
            "SELECT COUNT(*) FROM fields WHERE class_fqn='BP_Player_C' " +
            "AND norm_path='/Game/Map.Map:PersistentLevel.BP_Player_C'", ct));
        Assert.Equal(4, await ScalarAsync(conn, "PRAGMA user_version", ct));
        // v4 dropped the heavy composite indexes — only the lean ix_fields remains.
        Assert.Equal(0, await ScalarAsync(conn,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN ('ix_strict','ix_loose','ix_insession')", ct));
    }

    private static async Task<long> ScalarAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
    }

    [Fact]
    public async Task Diff_InMemoryHashJoin_ChangedAddedRemoved()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.SetActiveGame("DIFF");

        long a = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "a" }, ct);
        await _store.WriteChunkAsync(a, new[]
        {
            MakeObject(1, ("HP",   "IntProperty", "64000000")),   // 100
            MakeObject(2, ("Mana", "IntProperty", "0A000000")),   // 10
            MakeObject(3, ("XP",   "IntProperty", "05000000")),   // 5  (removed in B)
        }, ct);
        await _store.FinalizeSnapshotAsync(a, 3, 3, ct);

        long b = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "b" }, ct);
        await _store.WriteChunkAsync(b, new[]
        {
            MakeObject(1, ("HP",   "IntProperty", "5A000000")),   // 90   (changed, down)
            MakeObject(2, ("Mana", "IntProperty", "0A000000")),   // 10   (unchanged)
            MakeObject(4, ("Gold", "IntProperty", "32000000")),   // 50   (added)
        }, ct);
        await _store.FinalizeSnapshotAsync(b, 3, 3, ct);

        var diff = await _store.DiffSnapshotsAsync(a, b, new SnapshotDiffFilter(), ct);

        var changed = Assert.Single(diff.Changed);
        Assert.Equal("HP", changed.PropName);
        Assert.Equal("100", changed.OldValue);
        Assert.Equal("90", changed.NewValue);
        Assert.Equal(SnapshotDiffDirection.Down, changed.Direction);
        Assert.Equal(1, diff.AddedCount);     // Gold (obj 4) is B-only
        Assert.Equal(1, diff.RemovedCount);   // XP (obj 3) is A-only
    }

    private async Task<long> SeedSnapshotAsync(string label, int idx, CancellationToken ct)
    {
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = label }, ct);
        await _store.WriteChunkAsync(id, new[] { MakeObject(idx, ("X", "IntProperty", "01000000")) }, ct);
        await _store.FinalizeSnapshotAsync(id, 1, 1, ct);
        return id;
    }

    [Fact]
    public async Task EnforceQuota_DropsOldestKeepsNewest()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedSnapshotAsync("a", 1, ct);
        await SeedSnapshotAsync("b", 2, ct);
        await SeedSnapshotAsync("c", 3, ct);

        // A 1-byte quota evicts everything but the newest (always kept).
        int dropped = await _store.EnforceQuotaAsync(1, ct);
        Assert.Equal(2, dropped);
        Assert.Equal("c", Assert.Single(await _store.ListSnapshotsAsync(ct)).Label);
    }

    [Fact]
    public async Task EnforceQuota_UnlimitedIsNoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedSnapshotAsync("a", 1, ct);
        Assert.Equal(0, await _store.EnforceQuotaAsync(0, ct));   // 0 = unlimited
        Assert.Single(await _store.ListSnapshotsAsync(ct));
    }

    // Regression for the post-capture "Loading fields…" Not-Responding freeze: even when the
    // capture stays UNDER quota (no eviction), EnforceQuota must fold the capture-grown WAL
    // back into the .db. A bloated, un-checkpointed WAL left behind made the next reader (the
    // Class Pivot field load) contend for the WAL read-mark lock and spin the -shm lock byte.
    // Pre-fix the unlimited/under-quota path returned WITHOUT checkpointing, leaving -wal > 0.
    [Fact]
    public async Task EnforceQuota_FoldsWalEvenWhenUnderQuota()
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "wal" }, ct);

        // Write enough committed rows to leave un-checkpointed frames in the -wal file
        // (a plain COMMIT does not truncate the WAL — only a TRUNCATE checkpoint does).
        var objs = new List<SnapshotCapturedObject>();
        for (int i = 0; i < 500; i++)
            objs.Add(MakeObject(i, ("X", "IntProperty", "01000000"), ("Y", "IntProperty", "02000000")));
        await _store.WriteChunkAsync(id, objs, ct);
        await _store.FinalizeSnapshotAsync(id, objs.Count, objs.Count * 2, ct);

        string wal = _store.DatabasePath + "-wal";
        long walBefore = File.Exists(wal) ? new FileInfo(wal).Length : 0;
        Assert.True(walBefore > 0, "precondition: the capture should leave a non-empty WAL");

        // Unlimited (under-quota) call: keeps the snapshot, but MUST still fold the WAL.
        Assert.Equal(0, await _store.EnforceQuotaAsync(0, ct));
        Assert.Single(await _store.ListSnapshotsAsync(ct));

        long walAfter = File.Exists(wal) ? new FileInfo(wal).Length : 0;
        Assert.True(walAfter == 0, $"WAL not folded by EnforceQuota: {walAfter} bytes remain (was {walBefore})");
    }

    [Fact]
    public async Task EnforceCount_DropsOldestKeepsNewestN()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedSnapshotAsync("a", 1, ct);
        await SeedSnapshotAsync("b", 2, ct);
        await SeedSnapshotAsync("c", 3, ct);

        // Keep the newest 2 → drop the oldest ("a").
        int dropped = await _store.EnforceCountAsync(2, ct);
        Assert.Equal(1, dropped);
        var kept = await _store.ListSnapshotsAsync(ct);
        Assert.Equal(new[] { "c", "b" }, kept.Select(s => s.Label).ToArray());   // newest first
    }

    [Fact]
    public async Task EnforceCount_KeepOneDropsAllButNewest()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedSnapshotAsync("a", 1, ct);
        await SeedSnapshotAsync("b", 2, ct);
        await SeedSnapshotAsync("c", 3, ct);
        Assert.Equal(2, await _store.EnforceCountAsync(1, ct));
        Assert.Equal("c", Assert.Single(await _store.ListSnapshotsAsync(ct)).Label);
    }

    [Fact]
    public async Task EnforceCount_NoOpWhenWithinLimitOrUnlimited()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedSnapshotAsync("a", 1, ct);
        await SeedSnapshotAsync("b", 2, ct);
        Assert.Equal(0, await _store.EnforceCountAsync(5, ct));    // already within N
        Assert.Equal(0, await _store.EnforceCountAsync(0, ct));    // 0 = unlimited
        Assert.Equal(0, await _store.EnforceCountAsync(-1, ct));   // negative = unlimited
        Assert.Equal(2, (await _store.ListSnapshotsAsync(ct)).Count);
    }

    [Fact]
    public async Task GetUsage_ReportsSizeAndCount()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedSnapshotAsync("a", 1, ct);
        var u = await _store.GetUsageAsync(ct);
        Assert.Equal(1, u.SnapshotCount);
        Assert.True(u.GameDbBytes > 0);
        Assert.True(u.AllGamesBytes >= u.GameDbBytes);

        // List populates a positive size estimate for snapshots with fields.
        Assert.True(Assert.Single(await _store.ListSnapshotsAsync(ct)).EstBytes > 0);
    }

    [Fact]
    public async Task DeleteAllSnapshotDatabases_RemovesEveryGameFileFromDisk()
    {
        var ct = TestContext.Current.CancellationToken;

        // Two distinct games -> two snapshots.<peHash>.db files on disk.
        _store.SetActiveGame("WIPEAAAA");
        await SeedSnapshotAsync("a", 1, ct);
        _store.SetActiveGame("WIPEBBBB");
        await SeedSnapshotAsync("b", 2, ct);

        var dbDir = Path.GetDirectoryName(_store.DatabasePath)!;
        Assert.Equal(2, Directory.GetFiles(dbDir, "snapshots.*.db").Length);

        var r = await _store.DeleteAllSnapshotDatabasesAsync(ct);

        // Both .db files are deleted outright (whole-file wipe, not a row purge), none skipped.
        Assert.Equal(2, r.Deleted);
        Assert.Equal(0, r.Skipped);
        Assert.Empty(Directory.GetFiles(dbDir, "snapshots.*.db"));
        // Sidecars are cleaned up too — no orphaned -wal/-shm left behind.
        Assert.Empty(Directory.GetFiles(dbDir, "snapshots.*.db-wal"));
        Assert.Empty(Directory.GetFiles(dbDir, "snapshots.*.db-shm"));

        // The store is still USABLE for the same game afterwards. OpenAsync memoises
        // "schema already built" per DB path to keep concurrent first-opens from racing,
        // and a whole-FILE wipe (as opposed to a row purge) must invalidate that memo —
        // otherwise this re-open skips CREATE TABLE and dies on "no such table: snapshots".
        _store.SetActiveGame("WIPEAAAA");
        await SeedSnapshotAsync("after-wipe", 1, ct);
        Assert.Single(await _store.ListSnapshotsAsync(ct));
    }

    [Fact]
    public async Task DeleteAllSnapshotDatabases_SkipsFileHeldOpenByActiveCapture()
    {
        var ct = TestContext.Current.CancellationToken;

        // Game B is fully written then idle (its connection returns to the pool).
        _store.SetActiveGame("WIPEFREE");
        await SeedSnapshotAsync("b", 1, ct);

        // Game A has a LIVE capture session holding its .db open — the "DB in use" case.
        _store.SetActiveGame("WIPEBUSY");
        await using (var session = await _store.BeginCaptureSessionAsync(ct))
        {
            var r = await _store.DeleteAllSnapshotDatabasesAsync(ct);

            // The idle game's file is removed; the in-use game's file is skipped, not fatal.
            Assert.Equal(1, r.Deleted);
            Assert.Equal(1, r.Skipped);
        }
    }

    private static SnapshotCapturedArrayElement MakeElem(
        int idx, string keyName, string keyVal, params (string n, string t, string h)[] fields)
    {
        var el = new SnapshotCapturedArrayElement { Index = idx, KeyName = keyName, KeyValue = keyVal };
        foreach (var (n, t, h) in fields)
            el.Fields.Add(new SnapshotCapturedField { Name = n, Type = t, Hex = h });
        return el;
    }

    private static SnapshotCapturedObject ShipWithCargo(string hpHex, string fuelQtyHex)
    {
        var o = new SnapshotCapturedObject
        {
            Index = 1, Addr = "0x1", Name = "Ship_0", ClassName = "BP_Ship_C",
            OuterClassName = "World", Path = "/Game/M.M:L.Ship_0",
        };
        o.Fields.Add(new SnapshotCapturedField { Name = "HP", Type = "IntProperty", Hex = hpHex });
        var cargo = new SnapshotCapturedArray { Field = "Cargo" };
        cargo.Elements.Add(MakeElem(0, "ItemID", "Fuel", ("Quantity", "IntProperty", fuelQtyHex)));
        cargo.Elements.Add(MakeElem(1, "ItemID", "Ore",  ("Quantity", "IntProperty", "0A000000")));
        o.Arrays.Add(cargo);
        return o;
    }

    [Fact]
    public async Task WriteChunk_WritesArrayRows_IncludedInDiff()
    {
        var ct = TestContext.Current.CancellationToken;

        long a = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "a" }, ct);
        int rowsA = await _store.WriteChunkAsync(a, new[] { ShipWithCargo("64000000", "64000000") }, ct); // HP100, Fuel100
        Assert.Equal(3, rowsA);  // 1 scalar HP + 2 array-element Quantity rows
        await _store.FinalizeSnapshotAsync(a, 1, rowsA, ct);

        long b = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "b" }, ct);
        await _store.WriteChunkAsync(b, new[] { ShipWithCargo("5A000000", "50000000") }, ct);  // HP90, Fuel80
        await _store.FinalizeSnapshotAsync(b, 1, 3, ct);

        // build 1203: the diff now INCLUDES struct-array-element rows (keyed by
        // array_field + elem_index so distinct elements don't collide on prop_name).
        // HP (100->90) and Cargo[0].Quantity (Fuel 100->80) both changed; Cargo[1]
        // (Ore, 10->10) is unchanged and must not appear.
        var diff = await _store.DiffSnapshotsAsync(a, b, new SnapshotDiffFilter(), ct);
        Assert.Equal(2, diff.Changed.Count);
        Assert.Contains(diff.Changed, r => r.PropName == "HP");
        Assert.Contains(diff.Changed, r => r.PropName == "Cargo[0].Quantity");
        Assert.DoesNotContain(diff.Changed, r => r.PropName == "Cargo[1].Quantity");
    }

    // build 1204 — deep nested array_field path (the DLL bakes the full path with
    // outer indices into SnapshotArray.field) + a leaf-container element (TArray<int>,
    // empty inner prop). Verifies the diff keys distinct deep paths AND the display
    // renders "path[N]" (no trailing dot) for an empty inner prop.
    private static SnapshotCapturedObject DeepSave(string tunesHex, string gpHex)
    {
        var o = new SnapshotCapturedObject
        {
            Index = 5, Addr = "0x5", Name = "Save_0", ClassName = "BP_Save_C",
            OuterClassName = "World", Path = "/Game/M.M:L.Save_0",
        };
        // Leaf-container element nested deep: element 2 is the int value (no inner prop).
        var tunes = new SnapshotCapturedArray { Field = "SaveSlotList[1].MsTuneData.MsTunes[0].WeaponTuneList[0].Tunes" };
        tunes.Elements.Add(MakeElem(2, "", "", ("", "IntProperty", tunesHex)));
        o.Arrays.Add(tunes);
        // 1-level struct-array element field at a deep-ish path.
        var slots = new SnapshotCapturedArray { Field = "SaveSlotList" };
        slots.Elements.Add(MakeElem(1, "Slot", "1", ("GP", "IntProperty", gpHex)));
        o.Arrays.Add(slots);
        return o;
    }

    [Fact]
    public async Task Diff_DeepNestedPath_AndLeafContainer()
    {
        var ct = TestContext.Current.CancellationToken;

        long a = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "a" }, ct);
        await _store.WriteChunkAsync(a, new[] { DeepSave("14000000", "33B60000") }, ct);  // Tunes[2]=20, GP=46643
        await _store.FinalizeSnapshotAsync(a, 1, 2, ct);

        long b = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "b" }, ct);
        await _store.WriteChunkAsync(b, new[] { DeepSave("15000000", "34B60000") }, ct);  // Tunes[2]=21, GP=46644
        await _store.FinalizeSnapshotAsync(b, 1, 2, ct);

        var diff = await _store.DiffSnapshotsAsync(a, b, new SnapshotDiffFilter(), ct);
        Assert.Equal(2, diff.Changed.Count);
        // Leaf-container element: empty inner prop -> "path[N]" with NO trailing dot.
        Assert.Contains(diff.Changed, r =>
            r.PropName == "SaveSlotList[1].MsTuneData.MsTunes[0].WeaponTuneList[0].Tunes[2]");
        // 1-level struct-array element field -> "SaveSlotList[1].GP".
        Assert.Contains(diff.Changed, r => r.PropName == "SaveSlotList[1].GP");
    }

    [Fact]
    public async Task WriteChunk_EmptyIsNoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "e" }, ct);
        Assert.Equal(0, await _store.WriteChunkAsync(id, Array.Empty<SnapshotCapturedObject>(), ct));
    }

    [Fact]
    public async Task PerGameDb_IsolatesSnapshotsByPeHash()
    {
        var ct = TestContext.Current.CancellationToken;

        _store.SetActiveGame("GAMEA");
        var pathA = _store.DatabasePath;
        await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "a", PeHash = "GAMEA" }, ct);

        _store.SetActiveGame("GAMEB");
        var pathB = _store.DatabasePath;
        await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "b", PeHash = "GAMEB" }, ct);

        Assert.NotEqual(pathA, pathB);
        Assert.Contains("GAMEA", pathA);
        Assert.Contains("GAMEB", pathB);

        // Each game sees only its own snapshot.
        _store.SetActiveGame("GAMEA");
        var listA = await _store.ListSnapshotsAsync(ct);
        Assert.Equal("a", Assert.Single(listA).Label);

        _store.SetActiveGame("GAMEB");
        var listB = await _store.ListSnapshotsAsync(ct);
        Assert.Equal("b", Assert.Single(listB).Label);
    }

    [Fact]
    public void SetActiveGame_SanitizesPeHashIntoFilename()
    {
        _store.SetActiveGame("../../evil\\x00");
        // Only ASCII alphanumerics survive (drops . / \) -> no path traversal.
        // Raw chars kept: e v i l x 0 0  ->  "evilx00".
        Assert.DoesNotContain("..", _store.DatabasePath);
        Assert.EndsWith("snapshots.evilx00.db", _store.DatabasePath);
    }

    // Two snapshots sharing objects 1 & 2; obj 3 only in A, obj 4 only in B.
    // Health 100->90 (down), Mana 5->8 (up), Ammo 30->30 (unchanged).
    private async Task<(long a, long b)> SeedDiffPairAsync(CancellationToken ct)
    {
        long a = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "a" }, ct);
        await _store.WriteChunkAsync(a, new[]
        {
            MakeObject(1, ("Health", "FloatProperty", "0000C842")),                                  // 100.0
            MakeObject(2, ("Ammo", "IntProperty", "1E000000"), ("Mana", "IntProperty", "05000000")), // 30, 5
            MakeObject(3, ("Gold", "IntProperty", "E7030000")),                                       // 999 (A only)
        }, ct);
        await _store.FinalizeSnapshotAsync(a, 3, 4, ct);

        long b = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "b" }, ct);
        await _store.WriteChunkAsync(b, new[]
        {
            MakeObject(1, ("Health", "FloatProperty", "0000B442")),                                  // 90.0 (down)
            MakeObject(2, ("Ammo", "IntProperty", "1E000000"), ("Mana", "IntProperty", "08000000")), // 30 same, 8 up
            MakeObject(4, ("Score", "IntProperty", "01000000")),                                     // B only
        }, ct);
        await _store.FinalizeSnapshotAsync(b, 3, 4, ct);
        return (a, b);
    }

    [Fact]
    public async Task DiffSnapshots_FindsChangedValues_CountsChurn()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await SeedDiffPairAsync(ct);

        var diff = await _store.DiffSnapshotsAsync(a, b, new SnapshotDiffFilter(), ct);

        Assert.Equal(2, diff.Changed.Count);  // Health + Mana (Ammo unchanged)

        var health = Assert.Single(diff.Changed, r => r.PropName == "Health");
        Assert.Equal("100", health.OldValue);
        Assert.Equal("90", health.NewValue);
        Assert.Equal(SnapshotDiffDirection.Down, health.Direction);
        Assert.Equal("BP_Player_C", health.ClassName);

        var mana = Assert.Single(diff.Changed, r => r.PropName == "Mana");
        Assert.Equal("5", mana.OldValue);
        Assert.Equal("8", mana.NewValue);
        Assert.Equal(SnapshotDiffDirection.Up, mana.Direction);

        Assert.Equal(1, diff.RemovedCount);   // Gold (A only)
        Assert.Equal(1, diff.AddedCount);     // Score (B only)
        Assert.False(diff.Truncated);
    }

    [Fact]
    public async Task DiffSnapshots_FiltersByDirection()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await SeedDiffPairAsync(ct);

        var up = await _store.DiffSnapshotsAsync(a, b,
            new SnapshotDiffFilter { Direction = SnapshotDiffDirection.Up }, ct);
        Assert.Equal("Mana", Assert.Single(up.Changed).PropName);

        var down = await _store.DiffSnapshotsAsync(a, b,
            new SnapshotDiffFilter { Direction = SnapshotDiffDirection.Down }, ct);
        Assert.Equal("Health", Assert.Single(down.Changed).PropName);
    }

    [Fact]
    public async Task DiffSnapshots_FiltersByPropName()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await SeedDiffPairAsync(ct);

        var diff = await _store.DiffSnapshotsAsync(a, b,
            new SnapshotDiffFilter { PropContains = "heal", IncludeAddedRemoved = false }, ct);
        Assert.Equal("Health", Assert.Single(diff.Changed).PropName);
        Assert.Equal(0, diff.AddedCount);   // skipped
    }

    [Fact]
    public async Task MultipleSnapshots_ListedNewestFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        long a = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "a" }, ct);
        long b = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "b" }, ct);
        var list = await _store.ListSnapshotsAsync(ct);
        Assert.Equal(2, list.Count);
        Assert.Equal(b, list[0].Id);  // newest (higher id) first
        Assert.Equal(a, list[1].Id);
    }

    // ---------- N1: per-game class denylist (noise picker) ----------

    private static SnapshotCapturedObject MakeObjectAs(string className, int index,
        params (string name, string type, string hex)[] fields)
    {
        var o = new SnapshotCapturedObject
        {
            Index = index, Addr = $"0x{index:X}", Name = $"{className}_{index}",
            ClassName = className, OuterClassName = "World",
            Path = $"/Game/Map.Map:PersistentLevel.{className}_{index}",
        };
        foreach (var (name, type, hex) in fields)
            o.Fields.Add(new SnapshotCapturedField { Name = name, Type = type, Hex = hex, Offset = 0 });
        return o;
    }

    // Two snapshots covering two classes: BP_Player_C (gameplay-relevant) and
    // W_HUD_C (noise). Both classes have changing values between A and B so the
    // denylist + Top-N picker have something to act on.
    private async Task<(long a, long b)> SeedTwoClassPairAsync(CancellationToken ct)
    {
        long a = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "a" }, ct);
        await _store.WriteChunkAsync(a, new[]
        {
            MakeObjectAs("BP_Player_C", 1, ("Health", "IntProperty", "64000000")), // 100
            MakeObjectAs("W_HUD_C",     2, ("Alpha",  "FloatProperty", "00000000")),// 0.0
            MakeObjectAs("W_HUD_C",     3, ("Width",  "FloatProperty", "00007044")),// 960
        }, ct);
        await _store.FinalizeSnapshotAsync(a, 3, 3, ct);

        long b = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "b" }, ct);
        await _store.WriteChunkAsync(b, new[]
        {
            MakeObjectAs("BP_Player_C", 1, ("Health", "IntProperty", "5A000000")), // 90 (down)
            MakeObjectAs("W_HUD_C",     2, ("Alpha",  "FloatProperty", "0000803F")),// 1.0 (up)
            MakeObjectAs("W_HUD_C",     3, ("Width",  "FloatProperty", "0000F043")),// 480 (down)
        }, ct);
        await _store.FinalizeSnapshotAsync(b, 3, 3, ct);
        return (a, b);
    }

    [Fact]
    public async Task DiffSnapshots_ExcludedClasses_FiltersOutNoiseClass()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await SeedTwoClassPairAsync(ct);

        var deny = new HashSet<string>(StringComparer.Ordinal) { "W_HUD_C" };
        var diff = await _store.DiffSnapshotsAsync(a, b,
            new SnapshotDiffFilter { ExcludedClasses = deny }, ct);

        // Only the gameplay class row survives. The two W_HUD_C rows are gone.
        var row = Assert.Single(diff.Changed);
        Assert.Equal("BP_Player_C", row.ClassName);
        Assert.Equal("Health", row.PropName);
    }

    [Fact]
    public async Task DiffSnapshots_TopContributors_RanksByHitCount_ExcludesDenied()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await SeedTwoClassPairAsync(ct);

        // No denylist yet: both classes should appear, W_HUD_C first (2 hits vs 1).
        var diff = await _store.DiffSnapshotsAsync(a, b, new SnapshotDiffFilter(), ct);
        Assert.Equal(2, diff.TopContributors.Count);
        Assert.Equal("W_HUD_C",     diff.TopContributors[0].ClassName);
        Assert.Equal(2,             diff.TopContributors[0].HitCount);
        Assert.Equal("BP_Player_C", diff.TopContributors[1].ClassName);
        Assert.Equal(1,             diff.TopContributors[1].HitCount);

        // Denylist suppresses W_HUD_C from BOTH the rows AND the picker (the user
        // can't re-suggest a class they already hid).
        var deny = new HashSet<string>(StringComparer.Ordinal) { "W_HUD_C" };
        var diff2 = await _store.DiffSnapshotsAsync(a, b,
            new SnapshotDiffFilter { ExcludedClasses = deny }, ct);
        var only = Assert.Single(diff2.TopContributors);
        Assert.Equal("BP_Player_C", only.ClassName);
    }

    [Fact]
    public async Task Diff_AlreadyCancelledToken_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await SeedTwoClassPairAsync(ct);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _store.DiffSnapshotsAsync(a, b, new SnapshotDiffFilter(), cts.Token));
    }

    [Fact]
    public async Task SpcQuery_AlreadyCancelledToken_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await SeedTwoClassPairAsync(ct);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var query = new SpcQuery
        {
            SnapshotIds = { a, b },
            Predicates  = { SpcPredicateKind.Any, SpcPredicateKind.Changed },
            AbsolutePredicates = { new SpcAbsolutePredicate(), new SpcAbsolutePredicate() },
            JoinMode = SpcJoinMode.InSession,
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _store.SpcQueryAsync(query, cts.Token));
    }

    [Fact]
    public async Task SpcQuery_ExcludedClasses_FiltersOutNoiseClass()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await SeedTwoClassPairAsync(ct);

        var deny = new HashSet<string>(StringComparer.Ordinal) { "W_HUD_C" };
        var query = new SpcQuery
        {
            SnapshotIds = { a, b },
            Predicates  = { SpcPredicateKind.Any, SpcPredicateKind.Changed },
            AbsolutePredicates = { new SpcAbsolutePredicate(), new SpcAbsolutePredicate() },
            JoinMode = SpcJoinMode.InSession,
            ExcludedClasses = deny,
        };
        var res = await _store.SpcQueryAsync(query, ct);

        // Only the gameplay class survives the chain — W_HUD_C never enters the
        // candidate dict on the anchor pass.
        var row = Assert.Single(res.Rows);
        Assert.Equal("BP_Player_C", row.ClassName);
        Assert.Equal("Health",      row.PropName);

        // Top-N likewise excludes the denied class.
        var only = Assert.Single(res.TopContributors);
        Assert.Equal("BP_Player_C", only.ClassName);
    }

    [Fact]
    public void ClassDenylist_PersistsAcrossStoreReload_PerGame()
    {
        // Game A: deny two classes (Spc scope).
        _store.SetActiveGame("GAMEAA");
        var picksA = new HashSet<string>(StringComparer.Ordinal) { "W_HUD_C", "BP_Tick_C" };
        _store.SetClassDenylist(DenylistScope.Spc, picksA);

        // Game B: independently deny a different class.
        _store.SetActiveGame("GAMEBB");
        var picksB = new HashSet<string>(StringComparer.Ordinal) { "WBP_Inventory_C" };
        _store.SetClassDenylist(DenylistScope.Spc, picksB);

        // Fresh store instance over the same temp dir — proves the JSON survived.
        var store2 = new SnapshotStore(new MockPlatformService(_tempDir));
        store2.SetActiveGame("GAMEAA");
        Assert.Equal(picksA, store2.GetClassDenylist(DenylistScope.Spc));
        store2.SetActiveGame("GAMEBB");
        Assert.Equal(picksB, store2.GetClassDenylist(DenylistScope.Spc));
        // Unknown game: empty (not the wrong game's list).
        store2.SetActiveGame("UNKNOWN");
        Assert.Empty(store2.GetClassDenylist(DenylistScope.Spc));
    }

    [Fact]
    public void ClassDenylist_ScopesAreIndependent_AndCoexistInOneFile()
    {
        _store.SetActiveGame("SCOPED");
        var diff  = new HashSet<string>(StringComparer.Ordinal) { "DiffOnly_C" };
        var spc   = new HashSet<string>(StringComparer.Ordinal) { "SpcOnly_C", "Shared_C" };
        var pivot = new HashSet<string>(StringComparer.Ordinal) { "PivotOnly_C" };
        // Writing one scope must not clobber the others (read-modify-write).
        _store.SetClassDenylist(DenylistScope.Diff, diff);
        _store.SetClassDenylist(DenylistScope.Spc, spc);
        _store.SetClassDenylist(DenylistScope.Pivot, pivot);

        var store2 = new SnapshotStore(new MockPlatformService(_tempDir));
        store2.SetActiveGame("SCOPED");
        Assert.Equal(diff,  store2.GetClassDenylist(DenylistScope.Diff));
        Assert.Equal(spc,   store2.GetClassDenylist(DenylistScope.Spc));
        Assert.Equal(pivot, store2.GetClassDenylist(DenylistScope.Pivot));
        // A class in Spc is NOT reported under Diff/Pivot (independence).
        Assert.DoesNotContain("Shared_C", store2.GetClassDenylist(DenylistScope.Diff));
        Assert.DoesNotContain("Shared_C", store2.GetClassDenylist(DenylistScope.Pivot));
    }

    [Fact]
    public void ClassDenylist_WithoutActiveGame_DoesNotPersist()
    {
        // No SetActiveGame call — default DB; the store should refuse to save so a
        // noise pick from one game doesn't leak into the default bucket.
        _store.SetClassDenylist(DenylistScope.Spc, new HashSet<string>(StringComparer.Ordinal) { "X" });

        var store2 = new SnapshotStore(new MockPlatformService(_tempDir));
        Assert.Empty(store2.GetClassDenylist(DenylistScope.Spc));
    }

    [Fact]
    public void ClassDenylist_FilenameSanitisedForPeHash()
    {
        _store.SetActiveGame("../../escape");
        _store.SetClassDenylist(DenylistScope.Spc, new HashSet<string>(StringComparer.Ordinal) { "A" });

        // Only ASCII alphanumerics survive in the filename.
        var expected = Path.Combine(_tempDir, Constants.LogFolderName,
                                    Constants.SnapshotSubFolder, "snapshots.escape.denylist.json");
        Assert.True(File.Exists(expected));
    }

    // ---------- Pivot class-index + Delete All ----------

    [Fact]
    public async Task PivotClassIndex_ComputesInstanceCounts_AndIsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "p" }, ct);
        await _store.WriteChunkAsync(id, new[]
        {
            MakeObjectAs("BP_Player_C", 1, ("Health", "IntProperty", "64000000")),
            MakeObjectAs("BP_Player_C", 2, ("Health", "IntProperty", "32000000")),
            MakeObjectAs("W_HUD_C",     3, ("Alpha",  "FloatProperty", "0000803F")),
        }, ct);
        await _store.FinalizeSnapshotAsync(id, 3, 3, ct);   // precomputes the index

        var classes = await _store.ListPivotClassesAsync(id, ct);
        Assert.Equal(2, Assert.Single(classes, c => c.ClassName == "BP_Player_C").InstanceCount);
        Assert.Equal(1, Assert.Single(classes, c => c.ClassName == "W_HUD_C").InstanceCount);

        // Idempotent: a second call serves the same built index (no rebuild).
        var again = await _store.ListPivotClassesAsync(id, ct);
        Assert.Equal(classes.Count, again.Count);
    }

    [Fact]
    public async Task PivotClassIndex_LazyBuild_WhenNotFinalizedWithIndex()
    {
        // Simulate an old snapshot: write rows but the index is built lazily on
        // first ListPivotClasses (FinalizeSnapshot also builds it, but the lazy
        // path must work for pre-feature snapshots / cancelled finalizes).
        var ct = TestContext.Current.CancellationToken;
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "lazy" }, ct);
        await _store.WriteChunkAsync(id, new[]
        {
            MakeObjectAs("BP_Item_C", 1, ("Qty", "IntProperty", "05000000")),
            MakeObjectAs("BP_Item_C", 2, ("Qty", "IntProperty", "07000000")),
        }, ct);
        // NOTE: no FinalizeSnapshotAsync — the index doesn't exist yet.
        var classes = await _store.ListPivotClassesAsync(id, ct);   // builds lazily
        Assert.Equal(2, Assert.Single(classes, c => c.ClassName == "BP_Item_C").InstanceCount);
    }

    [Fact]
    public async Task DeleteAll_TruncatesEverything()
    {
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await SeedTwoClassPairAsync(ct);
        Assert.Equal(2, (await _store.ListSnapshotsAsync(ct)).Count);

        await _store.DeleteAllSnapshotsAsync(ct);

        Assert.Empty(await _store.ListSnapshotsAsync(ct));
        // The pivot index for the deleted snapshots is gone too (no rows linger).
        Assert.Empty(await _store.ListPivotClassesAsync(a, ct));
        Assert.Empty(await _store.ListPivotClassesAsync(b, ct));
    }

    // Regression for the CI-only Capture_StreamsAllChunks flake: concurrent FIRST opens
    // of the same brand-new DB used to race inside OpenAsync. SnapshotViewModel reaches
    // this shape for real — SetEngineState ends with a fire-and-forget `_ = RefreshAsync()`
    // (its own OpenAsync on a background thread) while the user can press Capture at once,
    // whose CreateSnapshotAsync/BeginCaptureSessionAsync open the same file. Three distinct
    // races lived in that window, all of which throw out of the capture BEFORE the first
    // chunk fetch (the observed `LastGameOnly == null` shape):
    //   1. `PRAGMA journal_mode=WAL` ran FIRST in the pragma batch — before busy_timeout
    //      was set — so it hit the default 0 ms timeout and returned SQLITE_BUSY at once.
    //   2. EnsureSchemaAsync read `user_version` then DROPped + re-CREATEd; two openers
    //      both saw 0, so one could DROP the tables the other had just created.
    //   3. AddColumnIfMissingAsync is read-then-ALTER: both see is_usable missing, both
    //      ALTER, and the loser gets "duplicate column name".
    // Fresh dir per iteration — the race only exists on the very first open of a file.
    [Fact]
    public async Task ConcurrentFirstOpens_OnFreshDb_DoNotRace()
    {
        var ct = TestContext.Current.CancellationToken;
        for (int iter = 0; iter < 12; iter++)
        {
            var dir = Path.Combine(_tempDir, $"race{iter}");
            Directory.CreateDirectory(dir);
            var store = new SnapshotStore(new MockPlatformService(dir));
            store.SetActiveGame($"PE{iter:D4}");

            // Every one of these drives an independent OpenAsync on the same new file.
            var ops = new List<Task>
            {
                Task.Run(() => store.ListSnapshotsAsync(ct), ct),
                Task.Run(() => store.ListSnapshotsAsync(ct), ct),
                Task.Run(() => store.CreateSnapshotAsync(
                    new SnapshotMeta { Label = "a", CapturedAt = "t", PeHash = "PE", GameSessionId = "S", UeVersion = 504, Scope = "NumericNoByte" }, ct), ct),
                Task.Run(async () => { await using var s = await store.BeginCaptureSessionAsync(ct); }, ct),
                Task.Run(() => store.GetUsageAsync(ct), ct),
            };

            // Report the real exception: a bare Assert on WhenAll says only "one or more errors".
            try { await Task.WhenAll(ops); }
            catch (Exception)
            {
                var failed = ops.Where(t => t.IsFaulted)
                                .SelectMany(t => t.Exception!.InnerExceptions)
                                .Select(e => $"{e.GetType().Name}: {e.Message}")
                                .ToList();
                Assert.Fail($"iteration {iter}: {failed.Count} concurrent first-open(s) threw:{Environment.NewLine}  " +
                            string.Join(Environment.NewLine + "  ", failed));
            }

            // The schema is actually usable afterwards (not just "no exception").
            Assert.Single(await store.ListSnapshotsAsync(ct));
        }
    }
}
