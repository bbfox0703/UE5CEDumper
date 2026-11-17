using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UE5DumpUI;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the per-game data-folder layout: which file belongs to which game, which games
/// have aged out, that a game's files MOVE AS A GROUP into the subfolder, and that a
/// blocked move leaves the whole group where it was rather than half of it.
///
/// The pure half (<see cref="AppDataRetentionPolicy"/>) is tested without a disk, which
/// is the point of the split — these rules decide what gets DELETED.
/// </summary>
public class AppDataRetentionPolicyTests
{
    private const string Snap = Constants.SnapshotDbPrefix;   // "snapshots"
    private const string Book = Constants.BookmarkFilePrefix; // "bookmarks"

    // ---------- GameKeyOf ----------

    [Theory]
    [InlineData("snapshots.1A2B.db",             "1A2B")]
    [InlineData("snapshots.1A2B.db-wal",         "1A2B")]
    [InlineData("snapshots.1A2B.db-shm",         "1A2B")]
    [InlineData("snapshots.1A2B.denylist.json",  "1A2B")]
    [InlineData("snapshots.default.db",          "default")]
    [InlineData("SNAPSHOTS.1A2B.db",             "1A2B")]   // prefix match is case-insensitive
    public void GameKeyOf_ParsesEveryFileShape(string name, string expected)
        => Assert.Equal(expected, AppDataRetentionPolicy.GameKeyOf(name, Snap));

    [Theory]
    [InlineData("snapshots-backup.db")]   // a file a user parked here by hand
    [InlineData("snapshotsfoo.db")]       // no dot after the prefix
    [InlineData("snapshots.db")]          // ...that one IS ours-shaped; see the note below
    [InlineData("snapshots.")]
    [InlineData("snapshots")]
    [InlineData("ui-options.json")]
    [InlineData("dll-path.txt")]
    [InlineData("")]
    [InlineData(null)]
    public void GameKeyOf_RejectsWhatIsNotOurs(string? name)
    {
        // "snapshots.db" parses to key "db" rather than null — it is still one of ours by
        // shape, and no such file has ever been written. It is listed here only so the
        // reader sees it was considered: the guard that matters is that a name without
        // the literal "snapshots." lead-in is NEVER claimed.
        if (name == "snapshots.db")
        {
            Assert.Equal("db", AppDataRetentionPolicy.GameKeyOf(name, Snap));
            return;
        }
        Assert.Null(AppDataRetentionPolicy.GameKeyOf(name, Snap));
        Assert.False(AppDataRetentionPolicy.BelongsTo(name, Snap));
    }

    [Fact]
    public void GameKeyOf_PrefixesDoNotClaimEachOther()
    {
        Assert.Null(AppDataRetentionPolicy.GameKeyOf("bookmarks.1A2B.json", Snap));
        Assert.Equal("1A2B", AppDataRetentionPolicy.GameKeyOf("bookmarks.1A2B.json", Book));
    }

    // ---------- SelectExpired ----------

    private static readonly DateTime Now = new(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc);

    private static AppDataFileEntry Aged(string name, int daysAgo) =>
        new(name, Now.AddDays(-daysAgo));

    [Fact]
    public void SelectExpired_AgesOutAWholeGameAtOnce()
    {
        var files = new[]
        {
            Aged("snapshots.OLD.db", 40),
            Aged("snapshots.OLD.db-wal", 40),
            Aged("snapshots.OLD.denylist.json", 40),
        };
        var expired = AppDataRetentionPolicy.SelectExpired(files, Snap, Now, 21);
        Assert.Equal(
            new[] { "snapshots.OLD.db", "snapshots.OLD.db-wal", "snapshots.OLD.denylist.json" },
            expired);
    }

    [Fact]
    public void SelectExpired_NewestFileInTheGameKeepsTheWholeSetAlive()
    {
        // The realistic shape: the denylist was written once, months ago, but the DB was
        // opened yesterday. Per-FILE ageing would delete the denylist out from under a
        // live DB; per-GAME ageing keeps them together.
        var files = new[]
        {
            Aged("snapshots.LIVE.db", 1),
            Aged("snapshots.LIVE.denylist.json", 200),
        };
        Assert.Empty(AppDataRetentionPolicy.SelectExpired(files, Snap, Now, 21));
    }

    [Fact]
    public void SelectExpired_OnlyTheAgedGameGoes()
    {
        var files = new[]
        {
            Aged("snapshots.OLD.db", 22),
            Aged("snapshots.NEW.db", 20),
        };
        Assert.Equal(new[] { "snapshots.OLD.db" },
                     AppDataRetentionPolicy.SelectExpired(files, Snap, Now, 21));
    }

    [Fact]
    public void SelectExpired_ExactlyAtTheWindowIsStillFresh()
    {
        var files = new[] { new AppDataFileEntry("snapshots.EDGE.db", Now.AddDays(-21)) };
        Assert.Empty(AppDataRetentionPolicy.SelectExpired(files, Snap, Now, 21));
    }

    [Fact]
    public void SelectExpired_NeverReturnsFilesThatAreNotOurs()
    {
        var files = new[]
        {
            Aged("ui-options.json", 900),
            Aged("dll-path.txt", 900),
            Aged("bookmarks.OLD.json", 900),   // another family's file, swept by its own pass
            Aged("snapshots.OLD.db", 900),
        };
        Assert.Equal(new[] { "snapshots.OLD.db" },
                     AppDataRetentionPolicy.SelectExpired(files, Snap, Now, 21));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SelectExpired_NonPositiveWindowDeletesNothing(int days)
    {
        var files = new[] { Aged("snapshots.ANCIENT.db", 5000) };
        // A mis-configured window must not read as "everything expired" — that is the one
        // failure mode that would wipe the user's whole corpus.
        Assert.Empty(AppDataRetentionPolicy.SelectExpired(files, Snap, Now, days));
    }

    [Fact]
    public void SelectExpired_FutureTimestampIsFresh()
    {
        // Clock skew / a file copied off another machine. Fresh is the safe direction.
        var files = new[] { new AppDataFileEntry("snapshots.SKEW.db", Now.AddDays(5)) };
        Assert.Empty(AppDataRetentionPolicy.SelectExpired(files, Snap, Now, 21));
    }

    [Fact]
    public void SelectExpired_EmptyInputIsEmpty()
        => Assert.Empty(AppDataRetentionPolicy.SelectExpired(
            Array.Empty<AppDataFileEntry>(), Snap, Now, 21));

    // ---------- MoveOrder ----------

    [Fact]
    public void MoveOrder_PutsTheBareDbFirst()
    {
        var ordered = AppDataRetentionPolicy.MoveOrder(new[]
        {
            "snapshots.X.db-wal", "snapshots.X.denylist.json", "snapshots.X.db", "snapshots.X.db-shm",
        });
        Assert.Equal("snapshots.X.db", ordered[0]);
        Assert.Equal(4, ordered.Count);
    }
}

/// <summary>
/// The IO half: migration out of the old flat root, group atomicity, and the age sweep.
/// </summary>
public class AppDataFolderMaintenanceTests : IDisposable
{
    private const string Snap = Constants.SnapshotDbPrefix;
    private readonly string _root;
    private readonly string _dest;

    public AppDataFolderMaintenanceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"UE5DumpDataFolder_{Guid.NewGuid():N}");
        _dest = Path.Combine(_root, Constants.SnapshotSubFolder);
        Directory.CreateDirectory(_dest);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best effort */ }
    }

    private string WriteAt(string dir, string name, int daysAgo = 0)
    {
        var p = Path.Combine(dir, name);
        File.WriteAllText(p, name);
        if (daysAgo > 0)
        {
            var t = DateTime.UtcNow.AddDays(-daysAgo);
            File.SetLastWriteTimeUtc(p, t);
            File.SetLastAccessTimeUtc(p, t);
        }
        return p;
    }

    private static string[] NamesIn(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir).Select(Path.GetFileName).Select(n => n!).OrderBy(n => n).ToArray()
            : Array.Empty<string>();

    // ---------- Migration ----------

    [Fact]
    public void Migrate_MovesAWholeGameSetIntoTheSubfolder()
    {
        WriteAt(_root, "snapshots.AAA.db");
        WriteAt(_root, "snapshots.AAA.db-wal");
        WriteAt(_root, "snapshots.AAA.denylist.json");

        int moved = AppDataFolderMaintenance.MigrateFromLegacyRoot(_root, _dest, Snap, null);

        Assert.Equal(3, moved);
        Assert.Equal(
            new[] { "snapshots.AAA.db", "snapshots.AAA.db-wal", "snapshots.AAA.denylist.json" },
            NamesIn(_dest));
        Assert.Empty(NamesIn(_root));
    }

    [Fact]
    public void Migrate_LeavesEveryOtherFileAlone()
    {
        WriteAt(_root, "ui-options.json");
        WriteAt(_root, "dll-path.txt");
        WriteAt(_root, "experimental.json");
        WriteAt(_root, "ai-writes.json");
        WriteAt(_root, "teleport-coords.Game.json");
        WriteAt(_root, "snapshots-backup.db");     // ours by eye, not by the prefix rule
        WriteAt(_root, "bookmarks.AAA.json");      // another family — its own pass owns it
        WriteAt(_root, "snapshots.AAA.db");

        AppDataFolderMaintenance.MigrateFromLegacyRoot(_root, _dest, Snap, null);

        Assert.Equal(new[] { "snapshots.AAA.db" }, NamesIn(_dest));
        Assert.Equal(
            new[]
            {
                "ai-writes.json", "bookmarks.AAA.json", "dll-path.txt", "experimental.json",
                "snapshots-backup.db", "teleport-coords.Game.json", "ui-options.json",
            },
            NamesIn(_root));
    }

    [Fact]
    public void Migrate_DestinationCollision_LeavesTheWHOLEGroupAtTheRoot()
    {
        // The scenario: an older build wrote to the root again after a migration. Moving
        // only the -wal would hand the already-migrated .db a foreign WAL, and moving only
        // the .db is impossible (it collides) — so nothing may move.
        WriteAt(_dest, "snapshots.AAA.db");                 // already migrated copy
        WriteAt(_root, "snapshots.AAA.db");                 // collides
        WriteAt(_root, "snapshots.AAA.db-wal");             // would otherwise move alone
        WriteAt(_root, "snapshots.BBB.db");                 // unaffected game still migrates

        int moved = AppDataFolderMaintenance.MigrateFromLegacyRoot(_root, _dest, Snap, null);

        Assert.Equal(1, moved);
        Assert.Equal(new[] { "snapshots.AAA.db", "snapshots.AAA.db-wal" }, NamesIn(_root));
        Assert.Equal(new[] { "snapshots.AAA.db", "snapshots.BBB.db" }, NamesIn(_dest));
    }

    [Fact]
    public void Migrate_LockedDbLeavesItsSidecarsWithIt()
    {
        // A .db held open by another process must not have its -wal migrated out from
        // under it. MoveOrder attempts the .db first precisely so this aborts early.
        var db = WriteAt(_root, "snapshots.LOCK.db");
        WriteAt(_root, "snapshots.LOCK.db-wal");

        using (File.Open(db, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            int moved = AppDataFolderMaintenance.MigrateFromLegacyRoot(_root, _dest, Snap, null);
            Assert.Equal(0, moved);
        }

        Assert.Equal(new[] { "snapshots.LOCK.db", "snapshots.LOCK.db-wal" }, NamesIn(_root));
        Assert.Empty(NamesIn(_dest));
    }

    [Fact]
    public void Migrate_NothingToDoIsCheapAndSilent()
        => Assert.Equal(0, AppDataFolderMaintenance.MigrateFromLegacyRoot(_root, _dest, Snap, null));

    [Fact]
    public void Migrate_SameSourceAndDestinationIsANoOp()
    {
        WriteAt(_root, "snapshots.AAA.db");
        Assert.Equal(0, AppDataFolderMaintenance.MigrateFromLegacyRoot(_root, _root, Snap, null));
        Assert.Equal(new[] { "snapshots.AAA.db" },
                     NamesIn(_root).Where(n => n.StartsWith("snapshots", StringComparison.Ordinal)).ToArray());
    }

    // ---------- Retention ----------

    [Fact]
    public void Prune_DeletesTheAgedGameAndKeepsTheFreshOne()
    {
        WriteAt(_dest, "snapshots.OLD.db", daysAgo: 40);
        WriteAt(_dest, "snapshots.OLD.db-wal", daysAgo: 40);
        WriteAt(_dest, "snapshots.OLD.denylist.json", daysAgo: 40);
        WriteAt(_dest, "snapshots.NEW.db");

        int deleted = AppDataFolderMaintenance.PruneAged(_dest, Snap, Constants.DataMaxAgeDays, null);

        Assert.Equal(3, deleted);
        Assert.Equal(new[] { "snapshots.NEW.db" }, NamesIn(_dest));
    }

    [Fact]
    public void Prune_ARecentlyUsedFileKeepsItsAgedSiblings()
    {
        WriteAt(_dest, "snapshots.MIX.denylist.json", daysAgo: 400);
        var db = WriteAt(_dest, "snapshots.MIX.db", daysAgo: 400);
        AppDataFolderMaintenance.TouchUsed(db);           // "you connected to this game today"

        Assert.Equal(0, AppDataFolderMaintenance.PruneAged(_dest, Snap, Constants.DataMaxAgeDays, null));
        Assert.Equal(2, NamesIn(_dest).Length);
    }

    [Fact]
    public void Prune_NeverTouchesAnotherFamilysFiles()
    {
        WriteAt(_dest, "bookmarks.OLD.json", daysAgo: 400);
        WriteAt(_dest, "ui-options.json", daysAgo: 400);
        WriteAt(_dest, "snapshots.OLD.db", daysAgo: 400);

        Assert.Equal(1, AppDataFolderMaintenance.PruneAged(_dest, Snap, Constants.DataMaxAgeDays, null));
        Assert.Equal(new[] { "bookmarks.OLD.json", "ui-options.json" }, NamesIn(_dest));
    }

    [Fact]
    public void Prune_ZeroWindowIsDisabled()
    {
        WriteAt(_dest, "snapshots.ANCIENT.db", daysAgo: 5000);
        Assert.Equal(0, AppDataFolderMaintenance.PruneAged(_dest, Snap, 0, null));
        Assert.Single(NamesIn(_dest));
    }

    [Fact]
    public void Prune_MissingFolderIsSurvivable()
        => Assert.Equal(0, AppDataFolderMaintenance.PruneAged(
            Path.Combine(_root, "nope"), Snap, Constants.DataMaxAgeDays, null));

    // ---------- TouchUsed ----------

    [Fact]
    public void TouchUsed_OnAMissingFileCreatesNothing()
    {
        var p = Path.Combine(_dest, "snapshots.GHOST.db");
        AppDataFolderMaintenance.TouchUsed(p);            // must not throw
        Assert.False(File.Exists(p));
    }

    [Fact]
    public void TouchUsed_StampsTheWriteTime()
    {
        var p = WriteAt(_dest, "snapshots.T.db", daysAgo: 100);
        var before = DateTime.UtcNow;
        AppDataFolderMaintenance.TouchUsed(p);
        var seen = AppDataFolderMaintenance.LastUsedUtc(new FileInfo(p));
        Assert.True(seen >= before.AddSeconds(-5), $"expected ~now, got {seen:O}");
    }

    [Fact]
    public void LastUsedUtc_IgnoresLastAccessTime()
    {
        // NTFS last-access updates are ENABLED by default on Windows (fsutil
        // DisableLastAccess = 2), so an antivirus scan / backup / indexer read refreshes
        // it on files nobody has opened in months — measured on the maintainer's own
        // folder, where every file read as "accessed today" against write times weeks
        // old. Honouring it would make the sweep a permanent no-op.
        var p = WriteAt(_dest, "snapshots.A.db", daysAgo: 100);
        File.SetLastAccessTimeUtc(p, DateTime.UtcNow);

        var seen = AppDataFolderMaintenance.LastUsedUtc(new FileInfo(p));
        Assert.True(seen < DateTime.UtcNow.AddDays(-90), $"expected ~100 days old, got {seen:O}");
        Assert.Equal(1, AppDataFolderMaintenance.PruneAged(_dest, Snap, Constants.DataMaxAgeDays, null));
    }

    // ---------- Prepare (the store-facing entry point) ----------

    // Every [Fact] gets a fresh instance and therefore a fresh GUID temp folder, so
    // Prepare's once-per-folder gate has never seen these keys — no reset hook needed,
    // and no cross-test interference from one.

    [Fact]
    public void Prepare_MigratesThenPrunesAndReturnsTheSubfolder()
    {
        WriteAt(_root, "snapshots.KEEP.db");
        WriteAt(_root, "snapshots.GONE.db", daysAgo: 400);

        var dir = AppDataFolderMaintenance.Prepare(
            _root, Constants.SnapshotSubFolder, Snap, Constants.DataMaxAgeDays, null);

        Assert.Equal(_dest, dir);
        Assert.Equal(new[] { "snapshots.KEEP.db" }, NamesIn(_dest));
        Assert.Empty(NamesIn(_root));
    }

    [Fact]
    public void Prepare_RunsOncePerProcessPerFolder()
    {
        AppDataFolderMaintenance.Prepare(_root, Constants.SnapshotSubFolder, Snap, Constants.DataMaxAgeDays, null);

        // A file dropped at the old root AFTER the first Prepare is not re-migrated: the
        // gate is what keeps store construction from re-scanning on every instance.
        WriteAt(_root, "snapshots.LATE.db");
        AppDataFolderMaintenance.Prepare(_root, Constants.SnapshotSubFolder, Snap, Constants.DataMaxAgeDays, null);

        Assert.Equal(new[] { "snapshots.LATE.db" }, NamesIn(_root));
    }
}

/// <summary>
/// The two stores' own end of the contract: each lands in its subfolder, each picks up
/// data left at the old flat root — and only the snapshot folder is ever swept.
/// </summary>
public class AppDataStoreLayoutTests : IDisposable
{
    private readonly string _appData;
    private readonly string _root;

    public AppDataStoreLayoutTests()
    {
        _appData = Path.Combine(Path.GetTempPath(), $"UE5DumpLayout_{Guid.NewGuid():N}");
        _root = Path.Combine(_appData, Constants.LogFolderName);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_appData)) Directory.Delete(_appData, recursive: true); }
        catch { /* best effort */ }
    }

    private string Sub(string subFolder) => Path.Combine(_root, subFolder);

    [Fact]
    public void BookmarkStore_MigratesALegacyRootFileAndStillReadsIt()
    {
        var legacy = Path.Combine(_root, "bookmarks.CAFEBABE.json");
        File.WriteAllText(legacy, """{"peHash":"CAFEBABE","slots":[{"slotIndex":3,"label":"kept"}]}""");

        var store = new BookmarkStore(new MockPlatformService(_appData));

        Assert.False(File.Exists(legacy));
        Assert.Equal(Path.Combine(Sub(Constants.BookmarkSubFolder), "bookmarks.CAFEBABE.json"),
                     store.FilePathFor("CAFEBABE"));
        Assert.True(File.Exists(store.FilePathFor("CAFEBABE")));

        var slot = Assert.Single(store.Load("CAFEBABE").Slots);
        Assert.Equal("kept", slot.Label);
    }

    [Fact]
    public void BookmarkStore_NeverPurgesEvenAncientFiles()
    {
        // The explicit product decision: bookmarks are hand-placed and unrecoverable, so
        // they are migrated but NEVER aged out — unlike the snapshot DBs next door. Aged
        // in place (steady state), so this is the sweep's absence and not Move's
        // timestamp behaviour being tested.
        var dir = Sub(Constants.BookmarkSubFolder);
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "bookmarks.ANCIENT.json");
        File.WriteAllText(p, """{"peHash":"ANCIENT","slots":[]}""");
        var old = DateTime.UtcNow.AddDays(-4000);
        File.SetLastWriteTimeUtc(p, old);
        File.SetLastAccessTimeUtc(p, old);

        _ = new BookmarkStore(new MockPlatformService(_appData));

        Assert.True(File.Exists(p));
    }

    [Fact]
    public void SnapshotStore_MigratesALegacyDbSetAndTargetsTheSubfolder()
    {
        foreach (var n in new[] { "snapshots.DEADBEEF.db", "snapshots.DEADBEEF.db-wal",
                                  "snapshots.DEADBEEF.denylist.json" })
            File.WriteAllText(Path.Combine(_root, n), "x");

        var store = new SnapshotStore(new MockPlatformService(_appData));
        store.SetActiveGame("DEADBEEF");

        var dir = Sub(Constants.SnapshotSubFolder);
        Assert.Equal(Path.Combine(dir, "snapshots.DEADBEEF.db"), store.DatabasePath);
        Assert.True(File.Exists(Path.Combine(dir, "snapshots.DEADBEEF.db")));
        Assert.True(File.Exists(Path.Combine(dir, "snapshots.DEADBEEF.db-wal")));
        Assert.True(File.Exists(Path.Combine(dir, "snapshots.DEADBEEF.denylist.json")));
        Assert.False(File.Exists(Path.Combine(_root, "snapshots.DEADBEEF.db")));
    }

    [Fact]
    public void SnapshotStore_PurgesAGameNobodyHasOpenedInTheWindow()
    {
        var dir = Sub(Constants.SnapshotSubFolder);
        Directory.CreateDirectory(dir);
        var stale = Path.Combine(dir, "snapshots.STALE.db");
        var live  = Path.Combine(dir, "snapshots.LIVE.db");
        File.WriteAllText(stale, "x");
        File.WriteAllText(live, "x");
        var old = DateTime.UtcNow.AddDays(-(Constants.DataMaxAgeDays + 10));
        File.SetLastWriteTimeUtc(stale, old);
        File.SetLastAccessTimeUtc(stale, old);

        _ = new SnapshotStore(new MockPlatformService(_appData));

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(live));
    }

    [Fact]
    public async Task SnapshotStore_WipeAlsoSweepsFilesStrandedAtTheLegacyRoot()
    {
        // Stranded = a set migration had to leave behind (name collision). "Remove All
        // Snapshot Data" promises EVERY game, so it must still see those.
        var dir = Sub(Constants.SnapshotSubFolder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "snapshots.AAA.db"), "x");
        File.WriteAllText(Path.Combine(_root, "snapshots.AAA.db"), "x");
        File.WriteAllText(Path.Combine(_root, "snapshots.AAA.db-wal"), "x");

        var store = new SnapshotStore(new MockPlatformService(_appData));
        var result = await store.DeleteAllSnapshotDatabasesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Deleted);          // both .db files; the -wal is silent cleanup
        Assert.Equal(0, result.Skipped);
        Assert.False(File.Exists(Path.Combine(dir, "snapshots.AAA.db")));
        Assert.False(File.Exists(Path.Combine(_root, "snapshots.AAA.db")));
        Assert.False(File.Exists(Path.Combine(_root, "snapshots.AAA.db-wal")));
    }
}
