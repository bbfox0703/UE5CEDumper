using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// SQLite snapshot store (raw ADO.NET — no EF Core, for trim/AOT safety). One DB
/// per game at %LOCALAPPDATA%\UE5CEDumper\Snapshots\snapshots.&lt;pe_hash&gt;.db. The
/// <c>fields</c> table is denormalised (identity columns per row) so the SPC / Pivot
/// / Diff self-joins hit a single-table covering index (ix_strict/loose/insession) —
/// the fast path. Schema: docs/experimental-snapshot-spc-pivot.md §6.
///
/// <para>The <c>Snapshots\</c> subfolder, the one-time move of DB sets still at the old
/// flat root (as a GROUP — a <c>.db</c> without its <c>-wal</c> has lost data), and the
/// age-out of games nobody has connected to in <see cref="Constants.DataMaxAgeDays"/>
/// days are all <see cref="AppDataFolderMaintenance"/>'s — running from this
/// constructor so no connection can open before the folder has been migrated.</para>
/// </summary>
public sealed class SnapshotStore : ISnapshotStore
{
    private readonly string _dir;
    // The pre-subfolder location (%LOCALAPPDATA%\UE5CEDumper itself). Kept only so the
    // "Remove All Snapshot Data" wipe can also sweep a DB set that migration had to
    // leave behind — that button promises "for EVERY game", and a file it cannot see is
    // a file it silently keeps.
    private readonly string _legacyDir;
    private readonly ILoggingService? _log;
    // Active game's pe_hash (sanitised for use in the filename). Empty until
    // SetActiveGame is called — falls back to a shared "default" db.
    private string _peHash = "";

    // SQLitePCLRaw provider init is idempotent; do it once before first use so
    // the bundled native e_sqlite3 is registered under Native AOT.
    private static readonly object s_initLock = new();
    private static bool s_initialised;

    // Per-DB-path gate for the ONE-TIME schema init. EnsureSchemaAsync is NOT safe to
    // run concurrently against itself on the same file, and the damage is silent:
    //   - it reads PRAGMA user_version, and on a stale/zero version DROPs snapshots +
    //     objects + fields before re-CREATEing them. Two openers on a brand-new DB both
    //     read 0, so the slower one can DROP the tables — and the rows — the faster one
    //     already committed. That is DATA LOSS, not just a lock error: no exception is
    //     raised, the capture just disappears.
    //   - AddColumnIfMissingAsync is read-then-ALTER, so a tie throws "duplicate column".
    // SnapshotViewModel reaches this shape for real: SetEngineState ends with a
    // fire-and-forget `_ = RefreshAsync()` (its own open, on a thread-pool thread) while
    // the user can hit Capture immediately, whose CreateSnapshotAsync +
    // BeginCaptureSessionAsync open the same file.
    //
    // So: run the schema init at most once per (path, process), under a gate, and let
    // every later open skip it. Only the FIRST open of a file pays the gate — the
    // capture's producer/consumer connections still open concurrently, which the
    // pipelined capture depends on. Single-instance UI (Mutex) + single-process tests
    // mean a process-wide gate covers every writer; the busy_timeout below is what
    // guards the (unsupported) cross-process case.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> s_schemaGates =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> s_schemaReady =
        new(StringComparer.OrdinalIgnoreCase);

    // Defensive input cap for the two pivot fetch paths: a pathologically large
    // class shouldn't pull an unbounded row set into memory. Far above any realistic
    // class fan-out; if it ever fires we log it and flag the result truncated (no
    // silent caps — see the repo's lessons-learned).
    private const int PivotFetchRowCap = 2_000_000;

    /// <summary>Per-game DB path: snapshots.&lt;pe_hash&gt;.db (or
    /// snapshots.default.db before a game is set).</summary>
    public string DatabasePath =>
        Path.Combine(_dir, $"{Constants.SnapshotDbPrefix}.{(_peHash.Length > 0 ? _peHash : "default")}.db");

    public SnapshotStore(IPlatformService platform, ILoggingService? log = null)
    {
        _log = log;
        _legacyDir = Path.Combine(platform.GetAppDataPath(), Constants.LogFolderName);
        _dir = AppDataFolderMaintenance.Prepare(
            _legacyDir,
            Constants.SnapshotSubFolder,
            Constants.SnapshotDbPrefix,
            Constants.DataMaxAgeDays,
            log);
        EnsureProviderInitialised();
    }

    public void SetActiveGame(string? peHash)
    {
        _peHash = SanitizePeHash(peHash);
        // Connecting to a game IS use of its DB. Stamped here rather than at open
        // because the age sweep has to see the game as live even in a session where
        // the user never opens the experimental Snapshot tab.
        AppDataFolderMaintenance.TouchUsed(DatabasePath);
        _log?.Info(Constants.LogCatView, $"SnapshotStore: active DB -> {DatabasePath}");
    }

    // pe_hash is hex, but sanitise defensively so it can never escape the
    // filename (path traversal / invalid chars).
    private static string SanitizePeHash(string? peHash)
    {
        if (string.IsNullOrEmpty(peHash)) return "";
        var sb = new StringBuilder(peHash.Length);
        foreach (var c in peHash)
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    private string ConnectionString =>
        new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();

    private static void EnsureProviderInitialised()
    {
        if (s_initialised) return;
        lock (s_initLock)
        {
            if (s_initialised) return;
            SQLitePCL.Batteries_V2.Init();
            s_initialised = true;
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(ConnectionString);
        var dbPath = DatabasePath;
        try
        {
            await conn.OpenAsync(ct);
            // busy_timeout goes FIRST, before journal_mode. Switching a DB into WAL needs a
            // brief exclusive lock, and with the default 0 ms timeout a concurrent opener
            // makes it fail on the spot with SQLITE_BUSY — the pragma batch was setting the
            // timeout AFTER the pragma that needed it. Ordering it first turns that instant
            // failure into a bounded wait.
            //
            // busy_timeout: WAL allows concurrent readers, but a reader that opens while a
            // writer/checkpoint holds the read-mark lock would otherwise busy-spin the -shm
            // lock byte (LockFile/UnlockFile thousands/sec, low CPU) until Microsoft.Data.Sqlite's
            // 30s default command timeout — the post-capture "Loading fields…" Not-Responding
            // freeze. A bounded native sleep-and-retry turns that contention into at worst a
            // short wait that then succeeds (or a catchable SQLITE_BUSY), instead of a hang.
            //
            // temp_store + cache_size are re-normalized here on EVERY open because they are
            // connection-scoped PRAGMAs that BeginCaptureSessionAsync sets on a POOLED handle
            // (temp_store=MEMORY, cache_size=-262144). Microsoft.Data.Sqlite pools the native
            // sqlite3 handle (no Pooling=false in ConnectionString) and does NOT reset PRAGMAs
            // on return, and CaptureSession.DisposeAsync restores only synchronous — so without
            // this, a later reader draws a poisoned handle and the Discovery/Pivot external-merge
            // sort spills to OUR HEAP instead of a temp FILE, re-creating the OOM the bounded-SQL
            // path exists to prevent. temp_store=DEFAULT restores the compile-time default (FILE);
            // cache_size=-2000 restores the ~2MB default ceiling.
            await ExecAsync(conn, "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=DEFAULT; PRAGMA cache_size=-2000;", ct);
            await EnsureSchemaOnceAsync(conn, dbPath, ct);
            return conn;
        }
        catch
        {
            // OpenAsync succeeded but schema init threw/cancelled (the tab-switch
            // token can land mid-EnsureSchema): dispose so the native handle is
            // released and the pooled connection is returned, not leaked.
            await conn.DisposeAsync();
            throw;
        }
    }

    private static async Task ExecAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Add a column to a table only if it doesn't already exist (SQLite has
    /// no ADD COLUMN IF NOT EXISTS). Used for ADDITIVE schema changes that must NOT
    /// bump <see cref="SchemaVersion"/> (which drops every snapshot) — so an existing
    /// DB gains the column while keeping its data. <paramref name="columnDef"/> is the
    /// SQL after the name, e.g. "INTEGER NOT NULL DEFAULT 1".</summary>
    private static async Task AddColumnIfMissingAsync(
        SqliteConnection conn, string table, string column, string columnDef, CancellationToken ct)
    {
        bool exists = false;
        await using (var info = conn.CreateCommand())
        {
            info.CommandText = $"PRAGMA table_info({table});";
            await using var r = await info.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                // table_info columns: cid, name, type, notnull, dflt_value, pk
                if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                { exists = true; break; }
            }
        }
        if (!exists)
            await ExecAsync(conn, $"ALTER TABLE {table} ADD COLUMN {column} {columnDef};", ct);
    }

    /// <summary>Current on-disk schema version. The <c>fields</c> table is
    /// denormalised (identity columns per row). v4 DROPPED the three heavy composite
    /// covering indexes (ix_strict/loose/insession — ~450 MB on a ~1.8M-row capture):
    /// Diff and SPC now run as in-memory hash-joins (see <see cref="DiffSnapshotsAsync"/>
    /// / <see cref="SpcQueryAsync"/>), which need only a fast <c>WHERE snapshot_id</c>
    /// scan, and Pivot filters by (snapshot_id, class_fqn). So a single lean
    /// <c>ix_fields(snapshot_id, class_fqn)</c> serves every query — roughly halving
    /// the DB. Bump this on any incompatible change: an older DB is dropped +
    /// recreated on open (experimental captures recapture in ~2 min; no migration).</summary>
    private const long SchemaVersion = 4;

    /// <summary>Run <see cref="EnsureSchemaAsync"/> at most once per (DB path, process),
    /// serialised against every other opener of the same file. See the s_schemaGates
    /// comment for why concurrent schema init silently destroys committed rows.</summary>
    private static async Task EnsureSchemaOnceAsync(SqliteConnection conn, string dbPath, CancellationToken ct)
    {
        if (s_schemaReady.ContainsKey(dbPath)) return;
        var gate = s_schemaGates.GetOrAdd(dbPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Re-check under the gate: the opener we queued behind may have just built it.
            if (s_schemaReady.ContainsKey(dbPath)) return;
            await EnsureSchemaAsync(conn, ct);
            // Only mark ready on success — a throw/cancel must leave the next open to retry.
            s_schemaReady[dbPath] = true;
        }
        finally { gate.Release(); }
    }

    /// <summary>Forget the "schema already built" memo for every DB (or one path), so the
    /// next open rebuilds it. MUST be called whenever the .db FILES are deleted rather than
    /// truncated — otherwise the memo outlives the file it describes and the next open skips
    /// schema creation, leaving "no such table: snapshots".</summary>
    private static void InvalidateSchemaMemo(string? dbPath = null)
    {
        if (dbPath == null) s_schemaReady.Clear();
        else s_schemaReady.TryRemove(dbPath, out _);
    }

    private static async Task EnsureSchemaAsync(SqliteConnection conn, CancellationToken ct)
    {
        long ver;
        await using (var vcmd = conn.CreateCommand())
        {
            vcmd.CommandText = "PRAGMA user_version;";
            ver = (long)(await vcmd.ExecuteScalarAsync(ct) ?? 0L);
        }

        // Old (or experimental v2) schema detected -> just drop and recreate. The
        // DROPs are no-ops on a brand-new (empty) DB. Covers v1 (user_version 0) and
        // the reverted v2 normalised layout alike.
        if (ver < SchemaVersion)
        {
            await ExecAsync(conn, """
                DROP VIEW  IF EXISTS vfields;
                DROP TABLE IF EXISTS fields;
                DROP TABLE IF EXISTS objects;
                DROP TABLE IF EXISTS snapshots;
                """, ct);
        }

        await ExecAsync(conn, """
            CREATE TABLE IF NOT EXISTS snapshots(
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                label           TEXT,
                captured_at     TEXT,
                pe_hash         TEXT,
                game_session_id TEXT,
                ue_version      INTEGER,
                object_count    INTEGER,
                field_count     INTEGER,
                scope           TEXT
            );
            CREATE TABLE IF NOT EXISTS fields(
                snapshot_id     INTEGER NOT NULL,
                class_fqn       TEXT,
                norm_path       TEXT,
                outer_chain     TEXT,
                prop_name       TEXT,
                prop_offset     INTEGER,
                declared_type   TEXT,
                gobjects_index  INTEGER,
                obj_addr        TEXT,
                numeric_value   REAL,
                hex             TEXT,
                array_field     TEXT,
                elem_index      INTEGER,
                inner_key_name  TEXT,
                inner_key_value TEXT,
                inner_prop_name TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_fields ON fields(snapshot_id, class_fqn);
            """, ct);

        // Pivot class-index (additive — no SchemaVersion bump, so it doesn't drop
        // existing snapshots). Precomputes the per-class instance count once per
        // snapshot so the Class Pivot picker reads a tiny table instead of running
        // a COUNT(DISTINCT) GROUP BY over ~1.7M rows on every open (the 10s+ scan).
        // pivot_index_built marks (snapshot, scalar/array) as computed so an empty
        // result (a snapshot with no array classes) isn't mistaken for "not built".
        await ExecAsync(conn, """
            CREATE TABLE IF NOT EXISTS class_counts(
                snapshot_id    INTEGER NOT NULL,
                class_fqn      TEXT,
                is_array       INTEGER NOT NULL,
                instance_count INTEGER
            );
            CREATE INDEX IF NOT EXISTS ix_class_counts ON class_counts(snapshot_id, is_array);
            CREATE TABLE IF NOT EXISTS pivot_index_built(
                snapshot_id INTEGER NOT NULL,
                is_array    INTEGER NOT NULL,
                PRIMARY KEY(snapshot_id, is_array)
            );
            """, ct);

        // is_usable (additive — no SchemaVersion bump, so existing snapshots are
        // PRESERVED, not dropped). 0 marks a capture that spanned a GObjects-count
        // drift (likely a level transition) and is temporally inconsistent; such
        // snapshots are hidden from SPC/Pivot and auto-deleted before the next
        // capture. DEFAULT 1 => every existing + future row is usable unless proven
        // otherwise. See Services.SnapshotConsistency.
        await AddColumnIfMissingAsync(conn, "snapshots", "is_usable", "INTEGER NOT NULL DEFAULT 1", ct);

        if (ver < SchemaVersion)
            await ExecAsync(conn, $"PRAGMA user_version={SchemaVersion};", ct);
    }

    public async Task<long> CreateSnapshotAsync(SnapshotMeta meta, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO snapshots(label, captured_at, pe_hash, game_session_id, ue_version, object_count, field_count, scope)
            VALUES ($label, $at, $pe, $sess, $ue, 0, 0, $scope);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$label", meta.Label);
        cmd.Parameters.AddWithValue("$at",    meta.CapturedAt);
        cmd.Parameters.AddWithValue("$pe",    meta.PeHash);
        cmd.Parameters.AddWithValue("$sess",  meta.GameSessionId);
        cmd.Parameters.AddWithValue("$ue",    meta.UeVersion);
        cmd.Parameters.AddWithValue("$scope", meta.Scope);
        var id = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
        meta.Id = id;
        _log?.Info(Constants.LogCatView, $"SnapshotStore: created snapshot #{id} ({meta.Label})");
        return id;
    }

    public async Task<int> WriteChunkAsync(long snapshotId, IReadOnlyList<SnapshotCapturedObject> objects,
                                           CancellationToken ct = default)
    {
        if (objects.Count == 0) return 0;
        await using var conn = await OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
        using var ins = new ChunkInserter(conn, tx);
        int rows = ins.Insert(snapshotId, objects, ct);
        await tx.CommitAsync(ct);
        return rows;
    }

    public async Task<ICaptureSession> BeginCaptureSessionAsync(CancellationToken ct = default)
    {
        var conn = await OpenAsync(ct);   // WAL + synchronous=NORMAL + EnsureSchema
        try
        {
            // Bulk-load pragmas: a snapshot is rebuildable (a crash mid-capture leaves a
            // partial DB the next open/cleanup discards), so trade durability for speed
            // during capture. Restored to NORMAL when the session disposes.
            await ExecAsync(conn, "PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-262144;", ct);
            return new CaptureSession(conn, _log, DatabasePath);
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    // Builds + binds the scalar + struct-array INSERT commands for the `fields` table
    // once, then inserts each captured object's rows. Shared by the one-shot
    // WriteChunkAsync (tests / seeding) and the long-lived CaptureSession so the
    // row-binding logic lives in ONE place. Commands bind to the connection; Rebind()
    // re-points them at a new transaction after the session commits + reopens one.
    private sealed class ChunkInserter : IDisposable
    {
        private readonly SqliteCommand _cmd, _arrCmd;
        private readonly SqliteParameter pSnap, pCls, pNp, pOc, pPn, pOff, pDt, pIdx, pAddr, pNum, pHex;
        private readonly SqliteParameter aSnap, aCls, aNp, aOc, aPn, aOff, aDt, aIdx, aAddr, aNum, aHex, aAf, aEi, aIkn, aIkv, aIpn;

        public ChunkInserter(SqliteConnection conn, SqliteTransaction tx)
        {
            _cmd = conn.CreateCommand();
            _cmd.Transaction = tx;
            _cmd.CommandText = """
                INSERT INTO fields(snapshot_id, class_fqn, norm_path, outer_chain, prop_name, prop_offset,
                                   declared_type, gobjects_index, obj_addr, numeric_value, hex)
                VALUES ($snap, $cls, $np, $oc, $pn, $off, $dt, $idx, $addr, $num, $hex);
                """;
            pSnap = _cmd.Parameters.Add("$snap", SqliteType.Integer);
            pCls  = _cmd.Parameters.Add("$cls",  SqliteType.Text);
            pNp   = _cmd.Parameters.Add("$np",   SqliteType.Text);
            pOc   = _cmd.Parameters.Add("$oc",   SqliteType.Text);
            pPn   = _cmd.Parameters.Add("$pn",   SqliteType.Text);
            pOff  = _cmd.Parameters.Add("$off",  SqliteType.Integer);
            pDt   = _cmd.Parameters.Add("$dt",   SqliteType.Text);
            pIdx  = _cmd.Parameters.Add("$idx",  SqliteType.Integer);
            pAddr = _cmd.Parameters.Add("$addr", SqliteType.Text);
            pNum  = _cmd.Parameters.Add("$num",  SqliteType.Real);
            pHex  = _cmd.Parameters.Add("$hex",  SqliteType.Text);

            // Second command for struct-array element rows (carries the array
            // columns; SPC/Pivot inner-join on array_field + inner_key + inner_prop).
            _arrCmd = conn.CreateCommand();
            _arrCmd.Transaction = tx;
            _arrCmd.CommandText = """
                INSERT INTO fields(snapshot_id, class_fqn, norm_path, outer_chain, prop_name, prop_offset,
                                   declared_type, gobjects_index, obj_addr, numeric_value, hex,
                                   array_field, elem_index, inner_key_name, inner_key_value, inner_prop_name)
                VALUES ($snap, $cls, $np, $oc, $pn, $off, $dt, $idx, $addr, $num, $hex,
                        $af, $ei, $ikn, $ikv, $ipn);
                """;
            aSnap = _arrCmd.Parameters.Add("$snap", SqliteType.Integer);
            aCls  = _arrCmd.Parameters.Add("$cls",  SqliteType.Text);
            aNp   = _arrCmd.Parameters.Add("$np",   SqliteType.Text);
            aOc   = _arrCmd.Parameters.Add("$oc",   SqliteType.Text);
            aPn   = _arrCmd.Parameters.Add("$pn",   SqliteType.Text);
            aOff  = _arrCmd.Parameters.Add("$off",  SqliteType.Integer);
            aDt   = _arrCmd.Parameters.Add("$dt",   SqliteType.Text);
            aIdx  = _arrCmd.Parameters.Add("$idx",  SqliteType.Integer);
            aAddr = _arrCmd.Parameters.Add("$addr", SqliteType.Text);
            aNum  = _arrCmd.Parameters.Add("$num",  SqliteType.Real);
            aHex  = _arrCmd.Parameters.Add("$hex",  SqliteType.Text);
            aAf   = _arrCmd.Parameters.Add("$af",   SqliteType.Text);
            aEi   = _arrCmd.Parameters.Add("$ei",   SqliteType.Integer);
            aIkn  = _arrCmd.Parameters.Add("$ikn",  SqliteType.Text);
            aIkv  = _arrCmd.Parameters.Add("$ikv",  SqliteType.Text);
            aIpn  = _arrCmd.Parameters.Add("$ipn",  SqliteType.Text);
        }

        // Re-point the prepared commands at a fresh transaction (after a session commit).
        public void Rebind(SqliteTransaction tx) { _cmd.Transaction = tx; _arrCmd.Transaction = tx; }

        public int Insert(long snapshotId, IReadOnlyList<SnapshotCapturedObject> objects, CancellationToken ct)
        {
            int rows = 0, objIdx = 0;
            foreach (var obj in objects)
            {
                if ((++objIdx & 0x3FF) == 0) ct.ThrowIfCancellationRequested();
                string normPath = SnapshotIdentity.NormalizePath(obj.Path);
                foreach (var f in obj.Fields)
                {
                    pSnap.Value = snapshotId;
                    pCls.Value  = obj.ClassName;
                    pNp.Value   = normPath;
                    pOc.Value   = obj.OuterClassName;
                    pPn.Value   = f.Name;
                    pOff.Value  = f.Offset;
                    pDt.Value   = f.Type;
                    pIdx.Value  = obj.Index;
                    pAddr.Value = obj.Addr;
                    pNum.Value  = SnapshotNumeric.TryFromHex(f.Type, f.Hex, out var num)
                                    ? num : (object)DBNull.Value;
                    pHex.Value  = f.Hex;
                    _cmd.ExecuteNonQuery();
                    rows++;
                }

                // Struct-array element rows (one per inner numeric field).
                foreach (var arr in obj.Arrays)
                {
                    foreach (var el in arr.Elements)
                    {
                        object keyName  = string.IsNullOrEmpty(el.KeyName) ? DBNull.Value : el.KeyName;
                        object keyValue = string.IsNullOrEmpty(el.KeyName) ? DBNull.Value : el.KeyValue;
                        foreach (var f in el.Fields)
                        {
                            aSnap.Value = snapshotId;
                            aCls.Value  = obj.ClassName;
                            aNp.Value   = normPath;
                            aOc.Value   = obj.OuterClassName;
                            aPn.Value   = f.Name;
                            aOff.Value  = f.Offset;
                            aDt.Value   = f.Type;
                            aIdx.Value  = obj.Index;
                            aAddr.Value = obj.Addr;
                            aNum.Value  = SnapshotNumeric.TryFromHex(f.Type, f.Hex, out var anum)
                                            ? anum : (object)DBNull.Value;
                            aHex.Value  = f.Hex;
                            aAf.Value   = arr.Field;
                            aEi.Value   = el.Index;
                            aIkn.Value  = keyName;
                            aIkv.Value  = keyValue;
                            aIpn.Value  = f.Name;
                            _arrCmd.ExecuteNonQuery();
                            rows++;
                        }
                    }
                }
            }
            return rows;
        }

        public void Dispose() { _cmd.Dispose(); _arrCmd.Dispose(); }
    }

    // Bulk-capture write session (see ICaptureSession): one long-lived connection +
    // bulk-load pragmas + a transaction committed every N chunks, so a multi-million-row
    // capture isn't paying a fresh connection + EnsureSchema + fsync per chunk. Used by a
    // single background consumer task — NOT thread-safe (one writer).
    private sealed class CaptureSession : ICaptureSession
    {
        private const int CommitEveryChunks = 16;
        private readonly ILoggingService? _log;
        private readonly SqliteConnection _conn;
        private readonly string _dbPath;   // for CurrentSizeBytes (db + WAL file size)
        private SqliteTransaction? _tx;
        private readonly ChunkInserter _ins;
        private int _sinceCommit;
        private bool _disposed;
        private bool _completed;
        // Phase-2 D: per-(class, is_array) DISTINCT GObjects-index sets, accumulated while
        // writing so CompleteSnapshotAsync can INSERT class_counts with no GROUP BY scan.
        // is_array 0 = the object wrote ≥1 scalar field; 1 = it wrote ≥1 array-element field.
        // Mirrors EnsurePivotIndexAsync's "array_field IS NULL / IS NOT NULL" split exactly.
        private readonly Dictionary<(string cls, int isArray), HashSet<long>> _pivot = new();
        private readonly Dictionary<string, string> _intern = new(StringComparer.Ordinal);

        public CaptureSession(SqliteConnection conn, ILoggingService? log, string dbPath)
        {
            _conn   = conn;
            _log    = log;
            _dbPath = dbPath;
            _tx     = (SqliteTransaction)_conn.BeginTransaction();
            _ins    = new ChunkInserter(_conn, _tx);
        }

        // Live capture footprint = committed on-disk bytes (db + WAL) + an estimate of the
        // rows written SINCE the last commit. The on-disk files only reflect committed data:
        // rows of the open transaction sit in the page cache (synchronous=OFF, commit-every-16)
        // and don't hit the -wal until the next commit, so a bare file-size read lags by up to
        // one commit batch — letting the cap overshoot. Adding the uncommitted estimate keeps
        // the cap tight (within ~one chunk). The on-disk part is monotonic non-decreasing:
        // passive autocheckpoints DO run during capture but recycle WAL frames in place without
        // shrinking the -wal file — only the post-capture wal_checkpoint(TRUNCATE) truncates it,
        // and that never overlaps a live capture. No SQL, no checkpoint here. Single-consumer
        // only (same thread as WriteChunk), so _uncommittedBytes needs no synchronisation.
        public long CurrentSizeBytes() =>
            FileSizeOf(_dbPath) + FileSizeOf(_dbPath + "-wal") + _uncommittedBytes;

        // Estimated bytes of rows inserted into the OPEN transaction (reset on each commit).
        private long _uncommittedBytes;

        private string Intern(string s) { if (_intern.TryGetValue(s, out var v)) return v; _intern[s] = s; return s; }

        public int WriteChunk(long snapshotId, IReadOnlyList<SnapshotCapturedObject> objects, CancellationToken ct = default)
        {
            if (objects.Count == 0) return 0;

            // Accumulate the per-class distinct-index pivot counts (cheap; one pass).
            foreach (var obj in objects)
            {
                bool hasScalar = obj.Fields.Count > 0;
                bool hasArray = false;
                foreach (var arr in obj.Arrays)
                {
                    foreach (var el in arr.Elements)
                        if (el.Fields.Count > 0) { hasArray = true; break; }
                    if (hasArray) break;
                }
                if (hasScalar) Bucket(obj.ClassName, 0).Add(obj.Index);
                if (hasArray)  Bucket(obj.ClassName, 1).Add(obj.Index);
            }

            int rows = _ins.Insert(snapshotId, objects, ct);
            // Track the open transaction's footprint so CurrentSizeBytes (the max-dataset
            // cap's gauge) reflects rows not yet flushed to the -wal — the same row model
            // the pre-flight "Estimate size" uses, so the cap and the estimate agree.
            _uncommittedBytes += SnapshotSizeEstimate.EstimateChunkBytes(objects);
            // Commit every N chunks so the WAL doesn't grow to the whole capture before
            // the single final commit (bounds disk + lets a later checkpoint reclaim).
            if (++_sinceCommit >= CommitEveryChunks)
            {
                _tx!.Commit();
                _tx.Dispose();
                _tx = (SqliteTransaction)_conn.BeginTransaction();
                _ins.Rebind(_tx);
                _sinceCommit = 0;
                _uncommittedBytes = 0;   // committed rows are now in the on-disk size
            }
            return rows;
        }

        private HashSet<long> Bucket(string cls, int isArray)
        {
            var key = (Intern(cls), isArray);
            if (!_pivot.TryGetValue(key, out var set)) { set = new HashSet<long>(); _pivot[key] = set; }
            return set;
        }

        public async Task CompleteSnapshotAsync(long snapshotId, int objectCount, int fieldCount,
                                                bool isUsable = true, CancellationToken ct = default)
        {
            // Flush the captured rows, then write totals + incremental pivot counts in a
            // fresh transaction (so a reader can't observe a half-written class_counts).
            _tx!.Commit();
            await _tx.DisposeAsync();
            _tx = null;

            await using var tx = (SqliteTransaction)await _conn.BeginTransactionAsync(ct);

            await using (var u = _conn.CreateCommand())
            {
                u.Transaction = tx;
                u.CommandText = "UPDATE snapshots SET object_count=$oc, field_count=$fc, is_usable=$us WHERE id=$id;";
                u.Parameters.AddWithValue("$oc", objectCount);
                u.Parameters.AddWithValue("$fc", fieldCount);
                u.Parameters.AddWithValue("$us", isUsable ? 1 : 0);
                u.Parameters.AddWithValue("$id", snapshotId);
                await u.ExecuteNonQueryAsync(ct);
            }

            // Authoritative pivot counts: clear any partial lazy build for this snapshot,
            // then write the accumulated counts + mark both kinds built (so the lazy
            // EnsurePivotIndexAsync GROUP-BY path becomes a no-op for this snapshot).
            await using (var del = _conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM class_counts WHERE snapshot_id=$s;";
                del.Parameters.AddWithValue("$s", snapshotId);
                await del.ExecuteNonQueryAsync(ct);
            }
            await using (var insCmd = _conn.CreateCommand())
            {
                insCmd.Transaction = tx;
                insCmd.CommandText =
                    "INSERT INTO class_counts(snapshot_id, class_fqn, is_array, instance_count) VALUES($s,$c,$a,$n);";
                var ps = insCmd.Parameters.Add("$s", SqliteType.Integer); ps.Value = snapshotId;
                var pc = insCmd.Parameters.Add("$c", SqliteType.Text);
                var pa = insCmd.Parameters.Add("$a", SqliteType.Integer);
                var pn = insCmd.Parameters.Add("$n", SqliteType.Integer);
                foreach (var kv in _pivot)
                {
                    pc.Value = kv.Key.cls;
                    pa.Value = kv.Key.isArray;
                    pn.Value = kv.Value.Count;
                    await insCmd.ExecuteNonQueryAsync(ct);
                }
            }
            await using (var mark = _conn.CreateCommand())
            {
                mark.Transaction = tx;
                mark.CommandText = "INSERT OR IGNORE INTO pivot_index_built(snapshot_id, is_array) VALUES($s,0),($s,1);";
                mark.Parameters.AddWithValue("$s", snapshotId);
                await mark.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            _completed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            // CompleteSnapshotAsync already committed + nulled _tx; only commit here when
            // disposing WITHOUT completing (a cancelled / failed capture — the partial rows
            // are discarded by the caller's DeleteSnapshotAsync afterwards).
            if (!_completed && _tx != null)
            {
                try { _tx.Commit(); }
                catch (Exception ex) { _log?.Warn(Constants.LogCatView, $"CaptureSession: final commit failed: {ex.Message}"); }
                await _tx.DisposeAsync();
            }
            // Restore durability now that the bulk load is done (best-effort).
            try { await ExecAsync(_conn, "PRAGMA synchronous=NORMAL;", default); } catch { /* closing anyway */ }
            _ins.Dispose();
            await _conn.DisposeAsync();
        }
    }

    public async Task FinalizeSnapshotAsync(long snapshotId, int objectCount, int fieldCount,
                                            CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE snapshots SET object_count=$oc, field_count=$fc WHERE id=$id;";
        cmd.Parameters.AddWithValue("$oc", objectCount);
        cmd.Parameters.AddWithValue("$fc", fieldCount);
        cmd.Parameters.AddWithValue("$id", snapshotId);
        await cmd.ExecuteNonQueryAsync(ct);

        // Precompute the pivot class-index now, while we're still in the capture
        // flow (the user already expects a wait) — so the Class Pivot picker opens
        // instantly later instead of running a 10s+ GROUP BY on first selection.
        // Best-effort: if it throws/cancels, the list methods lazily build it.
        try
        {
            // forceRebuild: a lazy build triggered by browsing this snapshot in the
            // Pivot tab WHILE it was still capturing would have persisted counts +
            // the built-marker over partial rows, and the marker check would then
            // make this finalize call a no-op — permanently wrong counts. Forcing a
            // rebuild here (snapshot now complete) makes the final state authoritative.
            await EnsurePivotIndexAsync(conn, snapshotId, 0, ct, forceRebuild: true);
            await EnsurePivotIndexAsync(conn, snapshotId, 1, ct, forceRebuild: true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log?.Warn(Constants.LogCatView,
                $"SnapshotStore: pivot index precompute failed for #{snapshotId} (will build lazily): {ex.Message}");
        }

        _log?.Info(Constants.LogCatView,
            $"SnapshotStore: finalised snapshot #{snapshotId} ({objectCount} objects, {fieldCount} fields)");
    }

    public async Task<IReadOnlyList<SnapshotMeta>> ListSnapshotsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, label, captured_at, pe_hash, game_session_id, ue_version, object_count, field_count, scope, is_usable
            FROM snapshots ORDER BY id DESC;
            """;
        var list = new List<SnapshotMeta>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new SnapshotMeta
            {
                Id            = reader.GetInt64(0),
                Label         = reader.IsDBNull(1) ? "" : reader.GetString(1),
                CapturedAt    = reader.IsDBNull(2) ? "" : reader.GetString(2),
                PeHash        = reader.IsDBNull(3) ? "" : reader.GetString(3),
                GameSessionId = reader.IsDBNull(4) ? "" : reader.GetString(4),
                UeVersion     = reader.IsDBNull(5) ? 0  : reader.GetInt32(5),
                ObjectCount   = reader.IsDBNull(6) ? 0  : reader.GetInt32(6),
                FieldCount    = reader.IsDBNull(7) ? 0  : reader.GetInt32(7),
                Scope         = reader.IsDBNull(8) ? "" : reader.GetString(8),
                // Defensive: treat NULL/missing as usable (older rows default to 1).
                IsUsable      = reader.IsDBNull(9) || reader.GetInt32(9) != 0,
            });
        }

        // Estimate each snapshot's on-disk size by pro-rating the DB file size
        // across all field rows (snapshots share one per-game DB file).
        long fileBytes = FileSizeOf(DatabasePath);
        long totalFields = 0;
        foreach (var m in list) totalFields += m.FieldCount;
        if (totalFields > 0 && fileBytes > 0)
        {
            double bytesPerField = (double)fileBytes / totalFields;
            foreach (var m in list) m.EstBytes = (long)(m.FieldCount * bytesPerField);
        }
        return list;
    }

    public async Task<SnapshotUsage> GetUsageAsync(CancellationToken ct = default)
    {
        var usage = new SnapshotUsage();
        await using (var conn = await OpenAsync(ct))
        {
            // Fold the WAL back into the .db so the file size reflects all data.
            await ExecAsync(conn, "PRAGMA wal_checkpoint(TRUNCATE);", ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM snapshots;";
            usage.SnapshotCount = (int)(long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
        }
        usage.GameDbBytes   = FileSizeOf(DatabasePath);
        usage.AllGamesBytes = AllGamesBytes();
        return usage;
    }

    public async Task<int> EnforceQuotaAsync(long quotaBytes, CancellationToken ct = default)
    {
        // ALWAYS fold the capture-grown WAL back into the .db first, off the UI thread (the
        // caller wraps this in Task.Run). The previous "estimate db+wal+shm and skip the
        // checkpoint when under quota" optimisation left a large un-checkpointed WAL behind
        // after every under-quota capture; the next reader (e.g. the Class Pivot field load)
        // then opened against that bloated WAL, contended for the WAL read-mark lock, and
        // stalled — the "Loading fields…" Not-Responding freeze (low CPU, -shm lock churn).
        // Folding here is cheap (passive autocheckpoints already moved most frames into the
        // db during capture; this mainly truncates the -wal file) and means subsequent
        // readers always meet a small WAL and never contend.
        await using var conn = await OpenAsync(ct);
        await ExecAsync(conn, "PRAGMA wal_checkpoint(TRUNCATE);", ct);

        if (quotaBytes <= 0) return 0;  // unlimited (WAL already folded above)

        long fileBytes = FileSizeOf(DatabasePath);   // accurate size after folding the WAL
        if (fileBytes <= quotaBytes) return 0;       // under quota → no eviction

        // Read snapshots newest-first with their field counts.
        var rows = new List<(long id, int fields)>();
        long totalFields = 0;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, field_count FROM snapshots ORDER BY id DESC;";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                int fc = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                rows.Add((r.GetInt64(0), fc));
                totalFields += fc;
            }
        }
        if (rows.Count <= 1) return 0;  // always keep at least the newest

        double bytesPerField = totalFields > 0 ? (double)fileBytes / totalFields : 0;
        long kept = 0;
        var dropIds = new List<long>();
        bool keeping = true;
        for (int i = 0; i < rows.Count; i++)
        {
            long est = (long)(rows[i].fields * bytesPerField);
            if (keeping && (i == 0 || kept + est <= quotaBytes))
                kept += est;
            else
            {
                keeping = false;       // once over, every OLDER snapshot drops too
                dropIds.Add(rows[i].id);
            }
        }
        if (dropIds.Count == 0) return 0;

        {
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            await using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText =
                "DELETE FROM fields WHERE snapshot_id=$id; " +
                "DELETE FROM class_counts WHERE snapshot_id=$id; " +
                "DELETE FROM pivot_index_built WHERE snapshot_id=$id; " +
                "DELETE FROM snapshots WHERE id=$id;";
            var p = del.Parameters.Add("$id", SqliteType.Integer);
            foreach (var id in dropIds) { p.Value = id; await del.ExecuteNonQueryAsync(ct); }
            await tx.CommitAsync(ct);
        }
        // Reclaim disk now that rows are gone (DELETE alone doesn't shrink, and VACUUM
        // only truncates the file in a rollback journal mode — see ReclaimDiskAsync).
        await ReclaimDiskAsync(conn, ct);

        _log?.Info(Constants.LogCatView,
            $"SnapshotStore: quota eviction dropped {dropIds.Count} oldest snapshot(s)");
        return dropIds.Count;
    }

    public async Task<int> EnforceCountAsync(int keepNewest, CancellationToken ct = default)
    {
        // Count-based FIFO retention for the auto-snapshot loop's "keep newest N" mode.
        // Sibling of EnforceQuotaAsync (byte-based) — same WAL-fold-first / ORDER BY id
        // DESC / keep-newest / four-table-delete / reclaim shape, but the drop set is
        // decided by COUNT, not size. Runs off the UI thread (caller wraps in Task.Run).
        await using var conn = await OpenAsync(ct);
        await ExecAsync(conn, "PRAGMA wal_checkpoint(TRUNCATE);", ct);   // fold WAL (see EnforceQuotaAsync)

        if (keepNewest <= 0) return 0;   // 0/negative = unlimited (WAL already folded above)

        // Newest-first; the first keepNewest survive, the rest drop.
        var ids = new List<long>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM snapshots ORDER BY id DESC;";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) ids.Add(r.GetInt64(0));
        }
        if (ids.Count <= keepNewest) return 0;   // already within the count (also keeps ≥1)

        var dropIds = ids.GetRange(keepNewest, ids.Count - keepNewest);
        {
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            await using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText =
                "DELETE FROM fields WHERE snapshot_id=$id; " +
                "DELETE FROM class_counts WHERE snapshot_id=$id; " +
                "DELETE FROM pivot_index_built WHERE snapshot_id=$id; " +
                "DELETE FROM snapshots WHERE id=$id;";
            var p = del.Parameters.Add("$id", SqliteType.Integer);
            foreach (var id in dropIds) { p.Value = id; await del.ExecuteNonQueryAsync(ct); }
            await tx.CommitAsync(ct);
        }
        await ReclaimDiskAsync(conn, ct);   // shrink the file (see EnforceQuotaAsync)

        _log?.Info(Constants.LogCatView,
            $"SnapshotStore: count retention dropped {dropIds.Count} oldest snapshot(s), kept newest {keepNewest}");
        return dropIds.Count;
    }

    private static long FileSizeOf(string path)
    {
        try { var fi = new FileInfo(path); return fi.Exists ? fi.Length : 0; }
        catch { return 0; }
    }

    private long AllGamesBytes()
    {
        try
        {
            long sum = 0;
            foreach (var f in Directory.EnumerateFiles(_dir, $"{Constants.SnapshotDbPrefix}.*.db"))
                sum += FileSizeOf(f);
            return sum;
        }
        catch { return 0; }
    }

    public async Task<SnapshotDiffResult> DiffSnapshotsAsync(
        long idA, long idB, SnapshotDiffFilter filter, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();   // bail before opening a connection
        var result = new SnapshotDiffResult();
        int max = filter.MaxRows > 0 ? filter.MaxRows : Constants.DefaultMaxQueryRows;

        string classContains = filter.ClassContains ?? "";
        string propContains  = filter.PropContains ?? "";
        // N1: per-game class denylist. Filter both A-load and B-stream so the
        // Added/Removed churn counts also reflect only non-denylisted classes.
        var deny = filter.ExcludedClasses is { Count: > 0 } ? filter.ExcludedClasses : null;

        await using var conn = await OpenAsync(ct);

        // In-memory hash join (the technique the `discrete` sister project uses):
        // stream both snapshots' scalar fields into a dictionary keyed by the
        // in-session identity, then diff in two O(n) passes with O(1) hash lookups.
        // This is independent of index/schema shape — far faster than a SQL
        // self-join over ~1.8M rows, which only stays quick with a perfect
        // single-table composite covering index. Key = (class_fqn, gobjects_index,
        // prop_name); unique within one snapshot.

        // Intern the high-repetition class/prop strings to cut allocations.
        var intern = new Dictionary<string, string>(StringComparer.Ordinal);
        string Intern(string s) { if (intern.TryGetValue(s, out var v)) return v; intern[s] = s; return s; }

        // Snapshot A → { key : (hex, numeric) }. Only the old value + direction
        // input is kept (display columns come from B, the newer snapshot).
        // The key includes array_field + elem_index so struct-array-element rows
        // (e.g. SaveSlotList[0].GP vs SaveSlotList[1].GP, same class/owner/inner
        // prop name) don't collide. Direct fields use "" / -1. (build 1203)
        var aMap = new Dictionary<(string cls, long idx, string prop, string arr, int elem), (string hex, double? num)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT class_fqn, gobjects_index, prop_name, hex, numeric_value, " +
                              "array_field, elem_index FROM fields WHERE snapshot_id=$id;";
            cmd.Parameters.AddWithValue("$id", idA);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            int rowCount = 0;
            while (await r.ReadAsync(ct))
            {
                // ReadAsync ignores ct under Microsoft.Data.Sqlite — explicit check.
                if ((++rowCount & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                string aCls = r.IsDBNull(0) ? "" : r.GetString(0);
                if (deny != null && deny.Contains(aCls)) continue;
                var key = (Intern(aCls),
                           r.IsDBNull(1) ? -1L : r.GetInt64(1),
                           Intern(r.IsDBNull(2) ? "" : r.GetString(2)),
                           Intern(r.IsDBNull(5) ? "" : r.GetString(5)),
                           r.IsDBNull(6) ? -1 : r.GetInt32(6));
                aMap[key] = (r.IsDBNull(3) ? "" : r.GetString(3),
                             r.IsDBNull(4) ? (double?)null : r.GetDouble(4));
            }
        }

        // Snapshot B: stream rows, hash-look-up A. matched = common keys (changed +
        // unchanged); bTotal = all B rows — together they give the Added/Removed churn.
        int matched = 0, bTotal = 0;
        // N1: Top-N noise picker — accumulate per-class hit count + sample props
        // over the changed-row set (counts only what the user actually sees).
        var noise = new NoiseAccumulator();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT class_fqn, gobjects_index, prop_name, hex, numeric_value, " +
                              "norm_path, obj_addr, prop_offset, declared_type, array_field, elem_index " +
                              "FROM fields WHERE snapshot_id=$id;";
            cmd.Parameters.AddWithValue("$id", idB);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            int rowCount = 0;
            while (await r.ReadAsync(ct))
            {
                if ((++rowCount & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                string cls  = r.IsDBNull(0) ? "" : r.GetString(0);
                if (deny != null && deny.Contains(cls)) continue;  // also skip from bTotal so churn is correct
                bTotal++;
                long   idx  = r.IsDBNull(1) ? -1L : r.GetInt64(1);
                string prop = r.IsDBNull(2) ? "" : r.GetString(2);
                string arr  = r.IsDBNull(9) ? "" : r.GetString(9);
                int    elem = r.IsDBNull(10) ? -1 : r.GetInt32(10);
                if (!aMap.TryGetValue((Intern(cls), idx, Intern(prop), Intern(arr), elem), out var a)) continue;  // B-only (added)
                matched++;

                string bHex = r.IsDBNull(3) ? "" : r.GetString(3);
                if (string.Equals(a.hex, bHex, StringComparison.Ordinal)) continue;  // unchanged

                // Struct-array-element rows display the full path "Array[N].Inner"
                // (the inner prop name alone collides across elements). A leaf-
                // container element (TArray<int> etc.) has no inner prop → "Array[N]".
                string displayProp = arr.Length == 0 ? prop
                                   : prop.Length == 0 ? $"{arr}[{elem}]"
                                   : $"{arr}[{elem}].{prop}";

                // Optional store-side filters (the VM passes an empty filter and
                // filters client-side, but honour these for API completeness).
                if (classContains.Length > 0 && cls.IndexOf(classContains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (propContains.Length  > 0 && displayProp.IndexOf(propContains, StringComparison.OrdinalIgnoreCase) < 0) continue;

                double? bNum = r.IsDBNull(4) ? (double?)null : r.GetDouble(4);
                var dir = (a.num.HasValue && bNum.HasValue)
                    ? (bNum > a.num ? SnapshotDiffDirection.Up
                       : bNum < a.num ? SnapshotDiffDirection.Down
                       : SnapshotDiffDirection.None)
                    : SnapshotDiffDirection.None;
                if (filter.Direction == SnapshotDiffDirection.Up   && dir != SnapshotDiffDirection.Up)   continue;
                if (filter.Direction == SnapshotDiffDirection.Down && dir != SnapshotDiffDirection.Down) continue;

                // Count noise even past the row cap so the picker isn't biased
                // toward whichever class happened to land in the first 50k rows.
                noise.Bump(cls, displayProp);
                if (result.Changed.Count >= max) { result.Truncated = true; continue; }  // keep counting churn
                string type = r.IsDBNull(8) ? "" : r.GetString(8);
                result.Changed.Add(new SnapshotDiffRow
                {
                    ClassName    = cls,
                    NormPath     = r.IsDBNull(5) ? "" : r.GetString(5),
                    ObjectIndex  = (int)idx,
                    PropName     = displayProp,
                    // Element rows carry an owner-relative prop_offset that doesn't
                    // address the heap element (separate allocation); 0 it so a
                    // naive obj_addr+offset doesn't point somewhere wrong. ObjAddr
                    // stays the owner so "Open in Live Walker" reaches it.
                    PropOffset   = arr.Length > 0 ? 0 : (r.IsDBNull(7) ? 0 : r.GetInt32(7)),
                    DeclaredType = type,
                    ObjAddr      = r.IsDBNull(6) ? "" : r.GetString(6),
                    OldValue     = SnapshotNumeric.Render(type, a.hex),
                    NewValue     = SnapshotNumeric.Render(type, bHex),
                    Direction    = dir,
                });
            }
        }

        // Added = B keys with no A match; Removed = A keys with no B match.
        if (filter.IncludeAddedRemoved)
        {
            result.AddedCount   = bTotal - matched;
            result.RemovedCount = aMap.Count - matched;
        }
        noise.WriteTo(result.TopContributors);
        return result;
    }

    // ============================================================
    // Snapshot Group Match. Runs the C# Orden port (Services.GroupMatch) over a
    // snapshot's objects. Mode A (1 snapshot) = absolute predicates over one frozen
    // instant; Mode B (2 snapshots) = cross-snapshot temporal comparison, per-slot
    // relative predicates (Changed/Unchanged/Increased/Decreased, incl. the
    // "Current HP↓ + Max HP unchanged" group) + optional absolute on the newest.
    // See docs/snapshot-group-match-spec.md.
    // ============================================================
    public async Task<SnapshotGroupResult> GroupMatchAsync(
        SnapshotGroupQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = new SnapshotGroupResult();

        // ---- validate ----
        if (query.Slots.Count < 2 || query.Slots.Count > 4)
        {
            result.Error = "A group needs 2-4 values.";
            return result;
        }
        if (query.SnapshotIds.Count < 1)
        {
            result.Error = "Select a snapshot.";
            return result;
        }
        if (query.SnapshotIds.Count > 2)
        {
            // v1 compares exactly two snapshots (first-vs-last); 3+-snapshot predicate
            // chains are a later extension (docs/snapshot-group-match-spec.md §8).
            result.Error = "Group comparison supports 1 snapshot (absolute) or 2 (compare).";
            return result;
        }
        bool modeB = query.SnapshotIds.Count == 2;

        // ---- translate input slots to the pure matcher's slots ----
        var slots = new GroupMatch.Slot[query.Slots.Count];
        for (int i = 0; i < query.Slots.Count; i++)
        {
            var s = query.Slots[i];
            var pred = MapGroupPredicate(s.ScanType);
            if (pred is null)
            {
                result.Error = $"Predicate '{s.ScanType}' is not supported by group match.";
                return result;
            }
            bool isRel = IsRelativeGroupPredicate(pred.Value);
            if (isRel && !modeB)
            {
                // No baseline in a single snapshot — relative predicates need Mode B.
                result.Error = "Changed / Unchanged / Increased / Decreased need 2 snapshots (compare).";
                return result;
            }
            double? target = TryParseValue(s.Value);
            double? target2 = TryParseValue(s.Value2);
            if (!isRel)   // absolute predicates require a target (relative ones don't)
            {
                if (target is null)
                {
                    result.Error = $"Slot {i + 1}: enter a numeric value.";
                    return result;
                }
                if (pred.Value == GroupMatch.Predicate.Between && target2 is null)
                {
                    result.Error = $"Slot {i + 1}: Between needs an upper bound.";
                    return result;
                }
            }
            slots[i] = new GroupMatch.Slot
            {
                Scope = s.DataType == ValueScanDataType.NumericAll
                    ? GroupMatch.Scope.NumericAll : GroupMatch.Scope.NumericNoByte,
                Predicate = pred.Value,
                Target = target,
                Target2 = target2,
                // All slots share the panel rounding mode (the query carries it).
                RoundMode = query.RoundMode,
            };
        }

        int max = query.MaxResults > 0 ? query.MaxResults : Constants.DefaultMaxQueryRows;
        var deny = query.ExcludedClasses is { Count: > 0 } ? query.ExcludedClasses : null;

        await using var conn = await OpenAsync(ct);

        if (modeB)
        {
            // Sort ascending so [0] is the oldest, [last] the newest (snapshot id is
            // monotonic = capture order). The matcher compares first-vs-last.
            var ids = query.SnapshotIds.OrderBy(x => x).ToList();
            await GroupMatchModeBAsync(conn, ids, query.JoinMode, slots, query.Slots, query.Deep, deny, max, result, ct);
            return result;
        }

        long snapshotId = query.SnapshotIds[0];

        // Stream the snapshot's DIRECT numeric fields ordered by object identity
        // (class, GObjects index) so each object's leaves arrive contiguously; match
        // each object as its run completes (O(one object) memory, not the whole
        // snapshot). Struct-array element rows (array_field != '') are NOT part of an
        // object block here — deep blocks are SPC Query's scope.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT class_fqn, gobjects_index, obj_addr, norm_path, prop_name, prop_offset, " +
            "declared_type, hex, numeric_value, array_field, elem_index FROM fields " +
            "WHERE snapshot_id=$id " +
            // Deep: also fold the object's nested container / struct-array element values
            // (array_field rows) into its block; default = direct fields only.
            (query.Deep ? "" : "AND (array_field IS NULL OR array_field='') ") +
            "ORDER BY class_fqn, gobjects_index;";
        cmd.Parameters.AddWithValue("$id", snapshotId);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        // Per-object scratch (reused across objects).
        string? curCls = null;
        long curIdx = long.MinValue;
        string curAddr = "", curName = "";
        var leaves = new List<GroupMatch.Leaf>();
        var fName = new List<string>();
        var fOff  = new List<int>();
        var fType = new List<string>();
        var fVal  = new List<string>();
        int scanned = 0, totalMatched = 0, rowCount = 0;

        void Reset() { leaves.Clear(); fName.Clear(); fOff.Clear(); fType.Clear(); fVal.Clear(); }

        void Flush()
        {
            if (curCls is null || leaves.Count == 0) { Reset(); return; }
            scanned++;
            if (GroupMatch.Run(leaves, slots, out var perSlot, out bool capHit))
            {
                totalMatched++;
                if (result.Candidates.Count < max)
                    result.Candidates.Add(BuildGroupCandidate(
                        curCls, curIdx, curAddr, curName, leaves, slots, perSlot,
                        fName, fOff, fType, fVal, query.Slots));
            }
            // Sticky across objects, and recorded even when the object did NOT match
            // (AF13): a slot truncated at the cap is exactly when a miss may be an
            // artifact rather than an answer.
            if (capHit) result.PerSlotCapHit = true;
            Reset();
        }

        while (await r.ReadAsync(ct))
        {
            // ReadAsync ignores ct under Microsoft.Data.Sqlite — explicit poll.
            if ((++rowCount & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            string cls = r.IsDBNull(0) ? "" : r.GetString(0);
            if (deny != null && deny.Contains(cls)) continue;     // skip denylisted classes
            long idx = r.IsDBNull(1) ? -1L : r.GetInt64(1);
            if (!string.Equals(cls, curCls, StringComparison.Ordinal) || idx != curIdx)
            {
                Flush();
                curCls = cls;
                curIdx = idx;
                curAddr = r.IsDBNull(2) ? "" : r.GetString(2);
                curName = r.IsDBNull(3) ? "" : r.GetString(3);
            }
            string type = r.IsDBNull(6) ? "" : r.GetString(6);
            string hex  = r.IsDBNull(7) ? "" : r.GetString(7);
            double? num = r.IsDBNull(8) ? (double?)null : r.GetDouble(8);
            string prop = r.IsDBNull(4) ? "" : r.GetString(4);
            string arrayField = r.IsDBNull(9) ? "" : r.GetString(9);
            bool isArray = arrayField.Length > 0;
            // Array-element heap address isn't captured (separate allocation) — use
            // offset 0 + the full path as the identifier; direct fields keep their offset.
            int off = isArray ? 0 : (r.IsDBNull(5) ? 0 : r.GetInt32(5));
            string display = isArray
                ? GroupFieldDisplay(prop, arrayField, r.IsDBNull(10) ? -1 : r.GetInt32(10)) : prop;
            leaves.Add(new GroupMatch.Leaf
            {
                Offset = off,
                DeclaredType = type,
                Hex = new[] { hex },
                Num = new double?[] { num },
                Tag = fName.Count,
            });
            fName.Add(display);
            fOff.Add(off);
            fType.Add(type);
            fVal.Add(SnapshotNumeric.Render(type, hex));
        }
        Flush();   // last object

        result.Total = totalMatched;
        result.ScannedObjects = scanned;
        result.Truncated = totalMatched > result.Candidates.Count;
        result.PerSlotCap = Constants.GroupPerSlotCap;   // AF12: name the cap in force
        return result;
    }

    private static double? TryParseValue(string s) =>
        double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture, out var v) ? v : (double?)null;

    private static GroupMatch.Predicate? MapGroupPredicate(ValueScanType st) => st switch
    {
        ValueScanType.Exact     => GroupMatch.Predicate.Exact,
        ValueScanType.Bigger    => GroupMatch.Predicate.Bigger,
        ValueScanType.Smaller   => GroupMatch.Predicate.Smaller,
        ValueScanType.Between   => GroupMatch.Predicate.Between,
        ValueScanType.Changed   => GroupMatch.Predicate.Changed,
        ValueScanType.Unchanged => GroupMatch.Predicate.Unchanged,
        ValueScanType.Increased => GroupMatch.Predicate.Increased,
        ValueScanType.Decreased => GroupMatch.Predicate.Decreased,
        _ => null,   // string predicates (Contains/StartsWith/EndsWith) — unsupported
    };

    private static bool IsRelativeGroupPredicate(GroupMatch.Predicate p) =>
        p is GroupMatch.Predicate.Changed or GroupMatch.Predicate.Unchanged
          or GroupMatch.Predicate.Increased or GroupMatch.Predicate.Decreased;

    private static GroupCandidate BuildGroupCandidate(
        string cls, long idx, string addr, string name,
        List<GroupMatch.Leaf> leaves, GroupMatch.Slot[] slots, List<int>[] perSlot,
        List<string> fName, List<int> fOff, List<string> fType, List<string> fVal,
        List<SnapshotGroupSlotInput> inputs)
    {
        var cand = new GroupCandidate
        {
            InstanceAddr = addr,
            InstanceIndex = (int)idx,
            InstanceName = name,
            ClassName = cls,
            DefiningClassName = cls,
        };
        // Representative = the slot's SDR-ASSIGNED leaf (not perSlot[s][0]) so two slots can't
        // render the same field; lock/offsets count DISTINCT matching fields, not deduped
        // offsets (array elements all report offset 0 — dedup would falsely "lock" them).
        var assign = GroupMatch.Assignment(perSlot, leaves.Count);
        for (int s = 0; s < slots.Length; s++)
        {
            var hits = perSlot[s];
            int repLeaf = assign != null && assign[s] >= 0 ? assign[s] : hits[0];
            int rep = leaves[repLeaf].Tag;
            var offs = new List<int>(hits.Count);
            foreach (int li in hits) offs.Add(leaves[li].Offset);
            cand.Slots.Add(new GroupSlotMatch
            {
                SlotIndex      = s,
                Value          = inputs[s].Value,
                ScanType       = slots[s].Predicate.ToString(),
                Value2         = inputs[s].Value2,
                FieldName      = fName[rep],
                FieldOffset    = fOff[rep],
                FieldType      = fType[rep],
                LeafValue      = fVal[rep],
                Addr           = AddrPlusOffset(addr, fOff[rep]),
                InstanceAddr   = addr,
                ClassName      = cls,
                MatchedOffsets = offs,
                Locked         = hits.Count == 1,
            });
        }
        return cand;
    }

    private static string AddrPlusOffset(string baseAddr, int offset)
    {
        if (string.IsNullOrEmpty(baseAddr) || offset < 0) return baseAddr;
        string h = baseAddr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? baseAddr[2..] : baseAddr;
        return ulong.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
            ? $"0x{b + (ulong)offset:X}" : baseAddr;
    }

    // Display name for a Deep group leaf: "Array[N].Inner" for a struct-array element,
    // "Array[N]" for a leaf-container element (no inner prop, e.g. TArray<int>), else the
    // plain prop name for a direct field. Mirrors SpcDisplayProp.
    private static string GroupFieldDisplay(string propName, string arrayField, int elemIndex)
    {
        if (string.IsNullOrEmpty(arrayField)) return propName;
        return propName.Length == 0 ? $"{arrayField}[{elemIndex}]" : $"{arrayField}[{elemIndex}].{propName}";
    }

    // Mode B — cross-snapshot temporal group match over exactly 2 snapshots
    // (oldest -> newest). Hash-join the OLD snapshot's direct fields, then stream the
    // NEW snapshot grouping rows per object; each field present in BOTH becomes a leaf
    // carrying the [old, new] value sequence, so relative predicates (Changed /
    // Unchanged / Increased / Decreased) compare across time and absolute predicates
    // use the newest value. Object-block only (array_field rows excluded — deep blocks
    // are SPC Query's scope). Identity join = the same SpcKey SPC/Diff use.
    private async Task GroupMatchModeBAsync(
        SqliteConnection conn, IReadOnlyList<long> ids, SpcJoinMode mode,
        GroupMatch.Slot[] slots, List<SnapshotGroupSlotInput> inputs, bool deep,
        HashSet<string>? deny, int max, SnapshotGroupResult result, CancellationToken ct)
    {
        long oldId = ids[0], newId = ids[ids.Count - 1];
        // Deep: include the captured array_field rows so nested container / struct-array
        // element values (e.g. SaveSlotList[1]…Tunes[2]) fold into the owning object's
        // block; the SpcKey join already keys on array_field+elem_index so an element
        // joins to its own counterpart across snapshots. Default = direct fields only.
        string sql =
            "SELECT class_fqn, norm_path, outer_chain, declared_type, prop_name, prop_offset, " +
            "obj_addr, hex, numeric_value, gobjects_index, array_field, elem_index FROM fields " +
            "WHERE snapshot_id=$id" + (deep ? ";" : " AND (array_field IS NULL OR array_field='');");

        var intern = new Dictionary<string, string>(StringComparer.Ordinal);
        string Intern(string s) { if (intern.TryGetValue(s, out var v)) return v; intern[s] = s; return s; }

        // Pass 1: OLD snapshot -> fieldKey -> (hex, num).
        var oldFields = new Dictionary<string, (string hex, double? num)>(StringComparer.Ordinal);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$id", oldId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            int rc = 0;
            while (await r.ReadAsync(ct))
            {
                if ((++rc & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                string cls = r.IsDBNull(0) ? "" : r.GetString(0);
                if (deny != null && deny.Contains(cls)) continue;
                string prop = r.IsDBNull(4) ? "" : r.GetString(4);
                oldFields[Intern(SpcKey(mode, r, cls, prop))] =
                    (r.IsDBNull(7) ? "" : r.GetString(7), r.IsDBNull(8) ? (double?)null : r.GetDouble(8));
            }
        }

        // Pass 2: NEW snapshot -> bucket shared fields per object.
        var objs = new Dictionary<string, GroupObjAcc>(StringComparer.Ordinal);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$id", newId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            int rc = 0;
            while (await r.ReadAsync(ct))
            {
                if ((++rc & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                string cls = r.IsDBNull(0) ? "" : r.GetString(0);
                if (deny != null && deny.Contains(cls)) continue;
                string prop = r.IsDBNull(4) ? "" : r.GetString(4);
                if (!oldFields.TryGetValue(SpcKey(mode, r, cls, prop), out var old)) continue; // not in old -> unstable
                string objKey = SpcObjectKey(mode, r, cls);
                if (!objs.TryGetValue(objKey, out var acc))
                {
                    acc = new GroupObjAcc
                    {
                        ClassName = Intern(cls),
                        NormPath  = r.IsDBNull(1) ? "" : r.GetString(1),
                        ObjAddr   = r.IsDBNull(6) ? "" : r.GetString(6),
                        Index     = r.IsDBNull(9) ? -1 : r.GetInt64(9),
                    };
                    objs[Intern(objKey)] = acc;
                }
                string type = r.IsDBNull(3) ? "" : r.GetString(3);
                string newHex = r.IsDBNull(7) ? "" : r.GetString(7);
                double? newNum = r.IsDBNull(8) ? (double?)null : r.GetDouble(8);
                string arrayField = r.IsDBNull(10) ? "" : r.GetString(10);
                bool isArray = arrayField.Length > 0;
                int off = isArray ? 0 : (r.IsDBNull(5) ? 0 : r.GetInt32(5));
                string display = isArray
                    ? GroupFieldDisplay(prop, arrayField, r.IsDBNull(11) ? -1 : r.GetInt32(11)) : prop;
                acc.Leaves.Add(new GroupMatch.Leaf
                {
                    Offset = off,
                    DeclaredType = type,
                    Hex = new[] { old.hex, newHex },     // [oldest, newest]
                    Num = new double?[] { old.num, newNum },
                    Tag = acc.FName.Count,
                });
                acc.FName.Add(display);
                acc.FOff.Add(off);
                acc.FType.Add(type);
                acc.FVal.Add(SnapshotNumeric.Render(type, newHex));   // show the newest value
            }
        }

        // Match each object that shares >= 1 field across both snapshots.
        int totalMatched = 0, scanned = 0;
        foreach (var acc in objs.Values)
        {
            if ((++scanned & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
            if (acc.Leaves.Count == 0) continue;
            if (GroupMatch.Run(acc.Leaves, slots, out var perSlot, out bool capHit))
            {
                totalMatched++;
                if (result.Candidates.Count < max)
                    result.Candidates.Add(BuildGroupCandidate(
                        acc.ClassName, acc.Index, acc.ObjAddr, acc.NormPath,
                        acc.Leaves, slots, perSlot, acc.FName, acc.FOff, acc.FType, acc.FVal, inputs));
            }
            if (capHit) result.PerSlotCapHit = true;     // AF13
        }
        result.Total = totalMatched;
        result.ScannedObjects = scanned;
        result.Truncated = totalMatched > result.Candidates.Count;
        result.PerSlotCap = Constants.GroupPerSlotCap;   // AF12: name the cap in force
    }

    // The object-identity portion of an SpcKey (no prop/offset/array) — the grouping
    // key for Mode B. Mirrors SpcKeyBase's per-mode identity. Reader columns:
    // 1 norm_path, 2 outer_chain, 9 gobjects_index.
    private static string SpcObjectKey(SpcJoinMode mode, SqliteDataReader r, string cls) => mode switch
    {
        SpcJoinMode.Loose     => cls + (char)1 + (r.IsDBNull(2) ? "" : r.GetString(2)),
        SpcJoinMode.InSession => cls + (char)1 + (r.IsDBNull(9) ? -1L : r.GetInt64(9)),
        _ /* Strict */        => cls + (char)1 + (r.IsDBNull(1) ? "" : r.GetString(1)),
    };

    // Per-object accumulator for Mode B: the object's identity + display fields (from
    // the newest snapshot) + its shared-field leaves carrying [old, new] sequences.
    private sealed class GroupObjAcc
    {
        public string ClassName = "", NormPath = "", ObjAddr = "";
        public long Index;
        public readonly List<GroupMatch.Leaf> Leaves = new();
        public readonly List<string> FName = new();
        public readonly List<int>    FOff  = new();
        public readonly List<string> FType = new();
        public readonly List<string> FVal  = new();
    }

    public async Task<SpcResult> SpcQueryAsync(SpcQuery query, CancellationToken ct = default)
    {
        var result = new SpcResult { SnapshotCount = query.SnapshotIds.Count };
        int n = query.SnapshotIds.Count;
        if (n < 2)
            throw new ArgumentException("SPC needs at least two snapshots.", nameof(query));
        if (query.Predicates.Count != n)
            throw new ArgumentException("Predicate count must equal snapshot count.", nameof(query));
        ct.ThrowIfCancellationRequested();   // bail before opening a connection
        int max = query.MaxRows > 0 ? query.MaxRows : Constants.DefaultMaxQueryRows;

        string classContains = query.ClassContains?.Trim() ?? "";
        string propContains  = query.PropContains?.Trim() ?? "";
        var mode = query.JoinMode;
        var abs  = query.AbsolutePredicates is { Count: > 0 } ? query.AbsolutePredicates : null;
        // N1: per-game class denylist. Null/empty = no filtering. The set is
        // captured once here so a concurrent UI-side mutation can't change the
        // predicate mid-query.
        var deny = query.ExcludedClasses is { Count: > 0 } ? query.ExcludedClasses : null;

        await using var conn = await OpenAsync(ct);

        // Shared in-memory intersection load (also used by change-driven Discovery):
        // candidates present in ALL snapshots, carrying their oldest→newest value
        // sequence + newest-snapshot display fields. See LoadIntersectedCandidatesAsync.
        var cands = await LoadIntersectedCandidatesAsync(
            conn, query.SnapshotIds, mode, classContains, propContains, deny, ct);

        // Evaluate the directional chain + absolute predicates; build result rows.
        // Accumulate per-class hit count + per-(class, prop) sub-count to drive the
        // post-query Top-N noise picker (N1).
        var noise = new NoiseAccumulator();
        int evalCount = 0;
        foreach (var c in cands)
        {
            if ((++evalCount & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            if (c.Hex.Count != n) continue;
            if (!SpcEngine.Matches(c.Hex, c.Num, query.Predicates, abs, c.DeclaredType, query.RoundMode)) continue;
            noise.Bump(c.ClassName, c.PropName);  // counted even when row cap is hit
            if (result.Rows.Count >= max) { result.Truncated = true; continue; }
            var row = new SpcResultRow
            {
                ClassName = c.ClassName, NormPath = c.NormPath, PropName = c.PropName,
                PropOffset = c.PropOffset, DeclaredType = c.DeclaredType, ObjAddr = c.ObjAddr,
            };
            for (int i = 0; i < n; i++) row.Values.Add(SnapshotNumeric.Render(c.DeclaredType, c.Hex[i]));
            result.Rows.Add(row);
        }
        noise.WriteTo(result.TopContributors);
        return result;
    }

    // ============================================================
    // SPC GROUP QUERY — the object-aware, N-snapshot generalisation of Snapshot Group
    // Match Mode B. Reuses the SPC cross-snapshot intersection load
    // (LoadIntersectedCandidatesAsync) to get each field's value SEQUENCE, then groups
    // the surviving fields back into per-object blocks and runs the Orden SDR matcher
    // per object — where each of the N value-slots carries its OWN per-snapshot
    // predicate CHAIN (evaluated by SpcEngine.Matches, the same engine single-value SPC
    // uses). A match = one object holds all N slots' chains at DISTINCT fields/offsets.
    // Deep (struct-array elements) is inherent: the SPC load already includes
    // array_field rows (build 1203), so each element is its own candidate field grouped
    // under its owner. See docs/group-value-scan-spec.md §3.1.
    // ============================================================
    public async Task<SpcGroupResult> SpcGroupQueryAsync(SpcGroupQuery query, CancellationToken ct = default)
    {
        var result = new SpcGroupResult();
        int n = query.SnapshotIds.Count;

        // ---- validate ----
        if (query.Slots.Count < 2 || query.Slots.Count > 4)
        {
            result.Error = "A group needs 2-4 values.";
            return result;
        }
        if (n < 2)
        {
            result.Error = "Select at least two snapshots.";
            return result;
        }
        for (int s = 0; s < query.Slots.Count; s++)
            if (query.Slots[s].Chain.Count != n)
            {
                result.Error = $"Slot {s + 1}: predicate chain length must equal the snapshot count.";
                return result;
            }

        ct.ThrowIfCancellationRequested();
        int max = query.MaxResults > 0 ? query.MaxResults : Constants.DefaultMaxQueryRows;
        var mode = query.JoinMode;
        string classContains = query.ClassContains?.Trim() ?? "";
        string propContains  = query.PropContains?.Trim() ?? "";
        var deny = query.ExcludedClasses is { Count: > 0 } ? query.ExcludedClasses : null;

        await using var conn = await OpenAsync(ct);

        // Reuse the SPC intersection: candidate FIELDS present in ALL snapshots under the
        // join key, each carrying its oldest→newest value sequence + newest display
        // fields + (computeObjKey) the object-identity grouping key.
        var cands = await LoadIntersectedCandidatesAsync(
            conn, query.SnapshotIds, mode, classContains, propContains, deny, ct, computeObjKey: true);

        // Group surviving fields back into per-object blocks (the object-identity key).
        var byObj = new Dictionary<string, List<Cand>>(StringComparer.Ordinal);
        foreach (var c in cands)
        {
            if (!byObj.TryGetValue(c.ObjKey, out var list)) { list = new List<Cand>(); byObj[c.ObjKey] = list; }
            list.Add(c);
        }

        // Precompute per-slot scope + the directional / absolute arrays of each chain.
        int ns = query.Slots.Count;
        var slotScope = new GroupMatch.Scope[ns];
        var slotDir   = new SpcPredicateKind[ns][];
        var slotAbs   = new SpcAbsolutePredicate[ns][];
        for (int s = 0; s < ns; s++)
        {
            slotScope[s] = query.Slots[s].Scope == ValueScanDataType.NumericAll
                ? GroupMatch.Scope.NumericAll : GroupMatch.Scope.NumericNoByte;
            var chain = query.Slots[s].Chain;
            var dir = slotDir[s] = new SpcPredicateKind[n];
            var abs = slotAbs[s] = new SpcAbsolutePredicate[n];
            for (int i = 0; i < n; i++) { dir[i] = chain[i].Predicate; abs[i] = chain[i].Absolute; }
        }

        var noise = new NoiseAccumulator();
        int scanned = 0, totalMatched = 0;
        var perSlot = new List<int>[ns];
        foreach (var grp in byObj.Values)
        {
            if ((++scanned & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();

            // Build each slot's matching-field list over this object's candidate fields.
            bool anyEmpty = false;
            for (int s = 0; s < ns; s++)
            {
                var lst = perSlot[s] = new List<int>();
                for (int i = 0; i < grp.Count; i++)
                {
                    var f = grp[i];
                    if (!GroupMatch.TypeInScope(f.DeclaredType, slotScope[s])) continue;
                    if (SpcEngine.Matches(f.Hex, f.Num, slotDir[s], slotAbs[s], f.DeclaredType, query.RoundMode)) lst.Add(i);
                }
                if (lst.Count == 0) { anyEmpty = true; break; }   // early reject
            }
            if (anyEmpty) continue;

            // Distinct fields per slot (SDR over candidate-field indices — distinct
            // fields ⇒ distinct offsets / distinct array elements).
            if (!GroupMatch.HasDistinctAssignment(perSlot, grp.Count)) continue;

            totalMatched++;
            noise.Bump(grp[0].ClassName, grp[0].PropName);   // counted even past the cap
            if (result.Candidates.Count < max)
                result.Candidates.Add(BuildSpcGroupCandidate(grp, query.Slots, perSlot));
        }

        noise.WriteTo(result.TopContributors);
        result.Total = totalMatched;
        result.ScannedObjects = scanned;
        result.Truncated = totalMatched > result.Candidates.Count;
        return result;
    }

    // Build a GroupCandidate (the shared model) from one matched object's candidate
    // fields + the per-slot assignment. Each slot's representative = its first matching
    // field; ScanType carries the chain glyph string (e.g. "· ↓ ↑") for the detail row,
    // Value stays empty so the master SlotSummary falls back to the newest leaf value.
    private static GroupCandidate BuildSpcGroupCandidate(
        List<Cand> grp, List<SpcGroupSlot> slots, List<int>[] perSlot)
    {
        var first = grp[0];
        var cand = new GroupCandidate
        {
            InstanceAddr      = first.ObjAddr,
            InstanceIndex     = (int)first.Index,
            InstanceName      = first.NormPath,
            ClassName         = first.ClassName,
            DefiningClassName = first.ClassName,
        };
        // Each slot's representative is its SDR-ASSIGNED leaf (not perSlot[s][0]), so two
        // slots never render the same field. (Run already proved an assignment exists.)
        var assign = GroupMatch.Assignment(perSlot, grp.Count);
        for (int s = 0; s < slots.Count; s++)
        {
            var hits = perSlot[s];
            int repIdx = assign != null && assign[s] >= 0 ? assign[s] : hits[0];
            var rep = grp[repIdx];
            // One entry per DISTINCT matching field (each Cand is a distinct field/element).
            // Do NOT dedup by PropOffset: array elements all report offset 0, so dedup would
            // collapse N distinct elements into one and falsely report the slot "locked".
            var offs = new List<int>(hits.Count);
            foreach (int li in hits) offs.Add(grp[li].PropOffset);
            cand.Slots.Add(new GroupSlotMatch
            {
                SlotIndex      = s,
                Value          = "",                                   // SPC slots carry a chain, not a single target
                ScanType       = SpcChainGlyphs(slots[s].Chain),       // "· ↓ ↑" — shown in the SPC detail row
                FieldName      = rep.PropName,
                FieldOffset    = rep.PropOffset,
                FieldType      = rep.DeclaredType,
                LeafValue      = rep.Hex.Count > 0 ? SnapshotNumeric.Render(rep.DeclaredType, rep.Hex[rep.Hex.Count - 1]) : "",
                Addr           = AddrPlusOffset(first.ObjAddr, rep.PropOffset),
                InstanceAddr   = first.ObjAddr,
                ClassName      = first.ClassName,
                MatchedOffsets = offs,
                Locked         = hits.Count == 1,                      // exactly one matching field ⇒ unambiguous
            });
        }
        return cand;
    }

    // Compact glyph string for a slot's directional chain (index 0 = baseline → "·").
    // Absolute windows are an input detail, not shown here.
    private static string SpcChainGlyphs(List<SpcGroupCell> chain)
    {
        var sb = new StringBuilder(chain.Count * 2);
        for (int i = 0; i < chain.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(i == 0 ? "·" : chain[i].Predicate switch
            {
                SpcPredicateKind.Unchanged => "=",
                SpcPredicateKind.Changed   => "≠",
                SpcPredicateKind.Increased => "↑",
                SpcPredicateKind.Decreased => "↓",
                _                          => "·",
            });
        }
        return sb.ToString();
    }

    // 0 class,1 norm,2 outer,3 type,4 prop,5 offset,6 addr,7 hex,8 num,9 gobjects_index,
    // 10 array_field,11 elem_index. Struct-array-element rows are now INCLUDED (build 1203);
    // array_field/elem_index join into the key so distinct elements don't collide.
    private const string SpcRowSql =
        "SELECT class_fqn, norm_path, outer_chain, declared_type, prop_name, prop_offset, " +
        "obj_addr, hex, numeric_value, gobjects_index, array_field, elem_index " +
        "FROM fields WHERE snapshot_id=$id;";

    private static string SpcKeyBase(SpcJoinMode mode, SqliteDataReader r, string cls, string prop) => mode switch
    {
        SpcJoinMode.Loose     => cls + "\u0001" + (r.IsDBNull(2) ? "" : r.GetString(2)) + "\u0001" + prop,
        SpcJoinMode.InSession => cls + "\u0001" + (r.IsDBNull(9) ? -1L : r.GetInt64(9)) + "\u0001" + prop,
        _ /* Strict */        => cls + "\u0001" + (r.IsDBNull(1) ? "" : r.GetString(1)) + "\u0001" + prop +
                                 "\u0001" + (r.IsDBNull(5) ? 0 : r.GetInt32(5)),
    };

    // Append array_field + elem_index so SaveSlotList[0].GP and SaveSlotList[1].GP
    // (same class/owner/inner prop) stay distinct; direct fields contribute ""/-1,
    // leaving their keys byte-for-byte as before. (build 1203)
    private static string SpcKey(SpcJoinMode mode, SqliteDataReader r, string cls, string prop)
        => SpcKeyBase(mode, r, cls, prop)
           + (char)1 + (r.IsDBNull(10) ? "" : r.GetString(10))
           + (char)1 + (r.IsDBNull(11) ? -1 : r.GetInt32(11)).ToString();

    /// <summary>Display prop for a (possibly array-element) row: "Array[N].Inner"
    /// when array_field (col 10) / elem_index (col 11) are set, else the inner prop.</summary>
    private static string SpcDisplayProp(SqliteDataReader r, string prop)
    {
        if (r.IsDBNull(10)) return prop;
        string arr = r.GetString(10);
        if (arr.Length == 0) return prop;
        int elem = r.IsDBNull(11) ? -1 : r.GetInt32(11);
        // Leaf-container element (TArray<int> etc.) has no inner prop -> "Array[N]".
        return prop.Length == 0 ? $"{arr}[{elem}]" : $"{arr}[{elem}].{prop}";
    }

    // One SPC candidate field: identity + its value sequence (+ display from newest).
    private sealed class Cand
    {
        public string ClassName = "", PropName = "", NormPath = "", ObjAddr = "", DeclaredType = "";
        public int  PropOffset;
        public bool Seen;
        public readonly List<string>  Hex = new();
        public readonly List<double?> Num = new();
        // SPC GROUP only (populated when LoadIntersectedCandidatesAsync is called with
        // computeObjKey: true): the object-identity portion of the join key, so the
        // surviving candidate FIELDS can be grouped back into per-object blocks, plus
        // the anchor snapshot's GObjects index for the candidate's display.
        public string ObjKey = "";
        public long   Index;
    }

    // Shared cross-snapshot intersection load for SPC + change-driven Discovery
    // (extracted from SpcQueryAsync so both consume one code path): load the anchor
    // (oldest) snapshot's fields into a candidate dict keyed by the join identity,
    // then stream each later snapshot keeping only candidates that recur, appending
    // their value. One shrinking dict — no SQL self-join, no covering-index
    // dependency. Class/prop filters + the per-game denylist narrow at load.
    // Duplicate keys within a snapshot (e.g. spawn siblings sharing a normalised path
    // under Strict) keep the first — collapses cross-product noise. Returns only the
    // candidates present in ALL snapshots (Hex.Count == snapshotIds.Count), with
    // NormPath / ObjAddr / declared type set from the NEWEST snapshot. The caller
    // evaluates predicates (SPC) or ranks them (Discovery).
    private async Task<List<Cand>> LoadIntersectedCandidatesAsync(
        SqliteConnection conn, IReadOnlyList<long> snapshotIds, SpcJoinMode mode,
        string classContains, string propContains, HashSet<string>? deny, CancellationToken ct,
        bool computeObjKey = false)
    {
        int n = snapshotIds.Count;
        var intern = new Dictionary<string, string>(StringComparer.Ordinal);
        string Intern(string s) { if (intern.TryGetValue(s, out var v)) return v; intern[s] = s; return s; }

        var cands = new Dictionary<string, Cand>();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = SpcRowSql;
            cmd.Parameters.AddWithValue("$id", snapshotIds[0]);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            int rowCount = 0;
            while (await r.ReadAsync(ct))
            {
                // Microsoft.Data.Sqlite's ReadAsync runs synchronously and ignores
                // the token, so the ONLY way to abort a multi-million-row scan is an
                // explicit periodic check. ~64k-row cadence keeps the overhead nil.
                if ((++rowCount & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                string cls  = r.IsDBNull(0) ? "" : r.GetString(0);
                if (deny != null && deny.Contains(cls)) continue;
                string prop = r.IsDBNull(4) ? "" : r.GetString(4);
                // Display path: "Array[N].Inner" for struct-array elements, else the
                // inner prop. The key uses the RAW prop + array_field/elem_index (via
                // SpcKey) so elements stay distinct; the filter + display use the path.
                string displayProp = SpcDisplayProp(r, prop);
                if (classContains.Length > 0 && cls.IndexOf(classContains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (propContains.Length  > 0 && displayProp.IndexOf(propContains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string key = SpcKey(mode, r, cls, prop);
                if (cands.ContainsKey(key)) continue;
                var c = new Cand { ClassName = Intern(cls), PropName = Intern(displayProp) };
                c.Hex.Add(r.IsDBNull(7) ? "" : r.GetString(7));
                c.Num.Add(r.IsDBNull(8) ? (double?)null : r.GetDouble(8));
                if (computeObjKey)   // SPC group: remember which OBJECT this field belongs to
                {
                    c.ObjKey = Intern(SpcObjectKey(mode, r, cls));
                    c.Index  = r.IsDBNull(9) ? -1L : r.GetInt64(9);
                }
                cands[key] = c;
            }
        }

        for (int i = 1; i < n && cands.Count > 0; i++)
        {
            foreach (var c in cands.Values) c.Seen = false;
            bool newest = i == n - 1;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = SpcRowSql;
                cmd.Parameters.AddWithValue("$id", snapshotIds[i]);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                int rowCount = 0;
                while (await r.ReadAsync(ct))
                {
                    if ((++rowCount & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                    string cls  = r.IsDBNull(0) ? "" : r.GetString(0);
                    if (deny != null && deny.Contains(cls)) continue;
                    string prop = r.IsDBNull(4) ? "" : r.GetString(4);
                    string key = SpcKey(mode, r, cls, prop);
                    if (!cands.TryGetValue(key, out var c) || c.Seen) continue;
                    c.Seen = true;
                    c.Hex.Add(r.IsDBNull(7) ? "" : r.GetString(7));
                    c.Num.Add(r.IsDBNull(8) ? (double?)null : r.GetDouble(8));
                    if (newest)
                    {
                        c.NormPath     = r.IsDBNull(1) ? "" : r.GetString(1);
                        c.ObjAddr      = r.IsDBNull(6) ? "" : r.GetString(6);
                        // Struct-array-element rows carry an owner-relative offset
                        // that doesn't address the heap element (separate allocation);
                        // 0 it so a naive obj_addr+offset doesn't point somewhere
                        // wrong. ObjAddr stays the owner for Open in Live Walker.
                        bool isArrayElem = !r.IsDBNull(10) && r.GetString(10).Length > 0;
                        c.PropOffset   = isArrayElem ? 0 : (r.IsDBNull(5) ? 0 : r.GetInt32(5));
                        c.DeclaredType = r.IsDBNull(3) ? "" : r.GetString(3);
                    }
                }
            }
            // Intersection: drop candidates absent from this snapshot.
            ct.ThrowIfCancellationRequested();
            List<string>? drop = null;
            foreach (var kv in cands)
                if (!kv.Value.Seen) (drop ??= new()).Add(kv.Key);
            if (drop != null) foreach (var k in drop) cands.Remove(k);
        }

        var survivors = new List<Cand>(cands.Count);
        foreach (var c in cands.Values)
            if (c.Hex.Count == n) survivors.Add(c);
        return survivors;
    }

    public async Task<DiscoveryResult> DiscoverChangesAsync(DiscoveryQuery query, CancellationToken ct = default)
    {
        // Validate on the DISTINCT count: the SQL path de-dups ids (a degenerate caller
        // passing [5,5] must fail cleanly here, not emit malformed SQL downstream).
        if (query.SnapshotIds.Distinct().Count() < 2)
            throw new ArgumentException("Discovery needs at least two distinct snapshots.", nameof(query));
        ct.ThrowIfCancellationRequested();   // bail before opening a connection

        // BOUNDED path (default): the standard Strict discovery — the only mode the
        // "Suggest Targets" front-door ever uses — reduces the N-snapshot compare
        // SERVER-SIDE and marshals only the CHANGED instances. Process memory is then
        // bounded by the changed-group count (thousands), NOT the snapshot's field
        // count (millions) — which is what ballooned the old in-memory Dictionary<…,Cand>
        // to 11 GB and hung the UI on big games. Loose / InSession fall back to the
        // in-memory intersection: the UI never selects them, but keeping the path
        // preserves the public contract for any other caller. See
        // docs/experimental-snapshot-spc-pivot.md §"Phase C — C3".
        if (query.JoinMode == SpcJoinMode.Strict)
            return await DiscoverChangesSqlAsync(query, ct);
        return await DiscoverChangesInMemoryAsync(query, ct);
    }

    // Bounded server-side change discovery over N (2-4) snapshots. One pass: pivot each
    // Strict identity's per-snapshot values into columns (h0..hN-1 / v0..vN-1), keep
    // only identities present in ALL snapshots whose value is NOT constant, and return
    // just those CHANGED instances. SQLite does the heavy GROUP BY + external-merge sort
    // (spilling to a temp FILE, not our heap); we marshal a tiny result. The pure engine
    // then rolls these up per (class, displayProp) and ranks — change-interval "shape"
    // (one-time event vs per-frame jitter) included once N≥3.
    private async Task<DiscoveryResult> DiscoverChangesSqlAsync(DiscoveryQuery query, CancellationToken ct)
    {
        // Order oldest -> newest (lower id = older) so h0..hN-1 read chronologically
        // regardless of how the caller arranged the picks; the newest is the pivot/CE
        // handoff anchor (its address + value are current).
        var ids = query.SnapshotIds.Distinct().OrderBy(x => x).ToList();
        int n = ids.Count;
        long newestId = ids[n - 1];

        // Per-game Pivot-scope denylist — captured once so a concurrent UI mutation
        // can't change the filter mid-query. Applied server-side (shrinks the sort).
        var deny = query.ExcludedClasses is { Count: > 0 }
            ? query.ExcludedClasses.Where(s => !string.IsNullOrEmpty(s)).ToList() : null;

        await using var conn = await OpenAsync(ct);
        // Generous-but-BOUNDED page cache cuts the sort's spill IO. Do NOT set
        // temp_store=MEMORY — the external-merge sort MUST spill to a temp FILE (not
        // our heap), or we recreate the very OOM this method exists to fix. temp_store=FILE
        // is forced explicitly (belt-and-suspenders): OpenAsync already un-poisons the pooled
        // handle, but this guarantees the OOM-critical path regardless of pool/open changes.
        await ExecAsync(conn, "PRAGMA cache_size=-65536; PRAGMA temp_store=FILE;", ct);   // 64 MB ceiling

        // Ensure the newest snapshot's per-class instance counts exist (Total source).
        await EnsurePivotIndexAsync(conn, newestId, 0, ct);

        // Hard cancel: Microsoft.Data.Sqlite's Read runs synchronously and the FIRST
        // Read can block for the whole sort, so the per-row ct check below can't fire
        // during it. Interrupt the in-flight statement on cancellation (best-effort —
        // if the handle is gone we simply fall back to the per-row check). Registered
        // AFTER the connection so it is unregistered BEFORE the connection disposes.
        using var reg = ct.Register(() =>
        {
            try { var h = conn.Handle; if (h != null) SQLitePCL.raw.sqlite3_interrupt(h); }
            catch { /* connection torn down — nothing to interrupt */ }
        });

        var changed = new List<DiscoveryInput>();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = BuildDiscoverSql(ids, deny);
            for (int i = 0; i < n; i++) cmd.Parameters.AddWithValue($"$s{i}", ids[i]);
            if (deny != null)
                for (int i = 0; i < deny.Count; i++) cmd.Parameters.AddWithValue($"$d{i}", deny[i]);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            // cols: 0 class_fqn, 1 declared_type, 2 prop_name, 3 array_field,
            // 4 elem_index, 5 norm_path, 6 addrNewest, then 7..7+n-1 = h, 7+n..7+2n-1 = v
            int baseH = 7, baseV = 7 + n, rowCount = 0;
            while (await r.ReadAsync(ct))
            {
                if ((++rowCount & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                string cls  = r.IsDBNull(0) ? "" : r.GetString(0);
                string type = r.IsDBNull(1) ? "" : r.GetString(1);
                string prop = r.IsDBNull(2) ? "" : r.GetString(2);
                string arr  = r.IsDBNull(3) ? "" : r.GetString(3);
                int    elem = r.IsDBNull(4) ? -1 : r.GetInt32(4);
                string norm = r.IsDBNull(5) ? "" : r.GetString(5);
                string addr = r.IsDBNull(6) ? "" : r.GetString(6);

                var hex = new string[n];
                var num = new double?[n];
                for (int i = 0; i < n; i++)
                {
                    hex[i] = r.IsDBNull(baseH + i) ? "" : r.GetString(baseH + i);
                    num[i] = r.IsDBNull(baseV + i) ? (double?)null : r.GetDouble(baseV + i);
                }
                changed.Add(new DiscoveryInput
                {
                    ClassName = cls, PropName = DiscoverDisplayProp(prop, arr, elem),
                    DeclaredType = type, NormPath = norm, ObjAddr = addr, Hex = hex, Num = num,
                });
            }
        }
        catch (SqliteException) when (ct.IsCancellationRequested)
        {
            // sqlite3_interrupt surfaces as SQLITE_INTERRUPT — normalise to cancellation.
            throw new OperationCanceledException(ct);
        }

        // Class / prop substring filters: the changed set is tiny, so filter in C# for
        // exact OrdinalIgnoreCase parity with the in-memory path (avoids SQL LIKE
        // wildcard/collation pitfalls). Applied before Rank so ChangedGroups + the cap
        // reflect the filtered set, matching the old load-time filtering.
        string cc = query.ClassContains?.Trim() ?? "";
        string pc = query.PropContains?.Trim()  ?? "";
        IEnumerable<DiscoveryInput> filtered = changed;
        if (cc.Length > 0) filtered = filtered.Where(d => d.ClassName.IndexOf(cc, StringComparison.OrdinalIgnoreCase) >= 0);
        if (pc.Length > 0) filtered = filtered.Where(d => d.PropName.IndexOf(pc, StringComparison.OrdinalIgnoreCase) >= 0);
        var inputs = filtered as List<DiscoveryInput> ?? filtered.ToList();

        // Per-class instance totals for the population sub-score — from the precomputed
        // class_counts (zero extra scan; built just above). The engine reads these
        // instead of counting fed inputs, since we feed only the CHANGED instances.
        var totals = await LoadClassTotalsAsync(conn, newestId, ct);

        int max = query.MaxResults > 0 ? query.MaxResults : 200;
        return PivotDiscoveryEngine.Rank(inputs, n, max, totals);
    }

    // Build the N-snapshot change-discovery SQL. GROUP BY the Strict identity key
    // (leading class_fqn so ix_fields drives the scan), pivot each snapshot's hex +
    // numeric_value into a column, keep identities present in ALL snapshots whose value
    // is NOT constant. COALESCE matches the C# SpcKey NULL handling exactly.
    //
    // Spawn-sibling consistency: NormalizePath strips the trailing _<n> spawn counter,
    // so several physical instances of one class can share a norm_path and thus the
    // Strict identity key. The in-memory path keeps ONE self-consistent sibling
    // (first-wins). Independent per-column MINs would instead let hex/num/addr each come
    // from a DIFFERENT sibling → a representative whose sample, direction arrow, and
    // handoff address disagree. So we first de-dup to ONE physical row per (snapshot,
    // identity) — the smallest gobjects_index, a deterministic stand-in for first-wins —
    // via ROW_NUMBER, then pivot. Every snapshot column then comes from that one sibling.
    private static string BuildDiscoverSql(IReadOnlyList<long> ids, IReadOnlyList<string>? deny)
    {
        int n = ids.Count;
        const string KEY = "class_fqn, norm_path, prop_name, COALESCE(prop_offset,0), " +
                           "COALESCE(array_field,''), COALESCE(elem_index,-1)";
        var sb = new StringBuilder();
        sb.Append("WITH dedup AS (SELECT class_fqn, norm_path, prop_name, prop_offset, array_field, elem_index, ");
        sb.Append("declared_type, snapshot_id, hex, numeric_value, obj_addr, ");
        sb.Append($"ROW_NUMBER() OVER (PARTITION BY snapshot_id, {KEY} ORDER BY gobjects_index) AS rn ");
        sb.Append("FROM fields WHERE snapshot_id IN (");
        for (int i = 0; i < n; i++) { if (i > 0) sb.Append(','); sb.Append($"$s{i}"); }
        sb.Append(')');
        if (deny is { Count: > 0 })
        {
            sb.Append(" AND class_fqn NOT IN (");
            for (int i = 0; i < deny.Count; i++) { if (i > 0) sb.Append(','); sb.Append($"$d{i}"); }
            sb.Append(')');
        }
        sb.Append(") SELECT class_fqn, MIN(declared_type) AS dt, prop_name, array_field, elem_index, norm_path, ");
        sb.Append($"MIN(CASE WHEN snapshot_id=$s{n - 1} THEN obj_addr END) AS addrNewest");
        for (int i = 0; i < n; i++)
            sb.Append($", MIN(CASE WHEN snapshot_id=$s{i} THEN hex END) AS h{i}");
        for (int i = 0; i < n; i++)
            sb.Append($", MIN(CASE WHEN snapshot_id=$s{i} THEN numeric_value END) AS v{i}");
        sb.Append(" FROM dedup WHERE rn=1 GROUP BY ").Append(KEY).Append(' ');
        // present in ALL snapshots (intersection)
        sb.Append("HAVING ");
        for (int i = 0; i < n; i++) { if (i > 0) sb.Append(" AND "); sb.Append($"h{i} IS NOT NULL"); }
        // changed = NOT constant across the sequence
        sb.Append(" AND NOT (");
        for (int i = 1; i < n; i++) { if (i > 1) sb.Append(" AND "); sb.Append($"h{i - 1}=h{i}"); }
        sb.Append(");");
        return sb.ToString();
    }

    // Mirror of SpcDisplayProp (SnapshotStore.cs SpcDisplayProp) over already-read
    // string parts: "Array[N].Inner" for struct-array elements (empty inner -> "Array[N]"),
    // else the plain prop. Keeps the engine rollup key identical to the SPC path's.
    private static string DiscoverDisplayProp(string prop, string arrayField, int elem)
    {
        if (arrayField.Length == 0) return prop;
        return prop.Length == 0 ? $"{arrayField}[{elem}]" : $"{arrayField}[{elem}].{prop}";
    }

    // Per-class instance counts (scalar) for one snapshot, from the precomputed
    // class_counts table — the Total source for the discovery population sub-score.
    private static async Task<Dictionary<string, int>> LoadClassTotalsAsync(
        SqliteConnection conn, long snapshotId, CancellationToken ct)
    {
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT class_fqn, instance_count FROM class_counts WHERE snapshot_id=$s AND is_array=0;";
        cmd.Parameters.AddWithValue("$s", snapshotId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            if (!r.IsDBNull(0)) totals[r.GetString(0)] = r.IsDBNull(1) ? 0 : r.GetInt32(1);
        return totals;
    }

    // In-memory fallback for non-Strict discovery (Loose / InSession). The UI never
    // selects these; kept so the public contract holds for any other caller. NOTE:
    // this is the OLD path that loads a snapshot's full field set into RAM — it carries
    // the same big-game memory exposure the Strict SQL path was built to avoid.
    private async Task<DiscoveryResult> DiscoverChangesInMemoryAsync(DiscoveryQuery query, CancellationToken ct)
    {
        int n = query.SnapshotIds.Count;
        string classContains = query.ClassContains?.Trim() ?? "";
        string propContains  = query.PropContains?.Trim() ?? "";
        var deny = query.ExcludedClasses is { Count: > 0 } ? query.ExcludedClasses : null;

        await using var conn = await OpenAsync(ct);
        var cands = await LoadIntersectedCandidatesAsync(
            conn, query.SnapshotIds, query.JoinMode, classContains, propContains, deny, ct);

        var inputs = new List<DiscoveryInput>(cands.Count);
        foreach (var c in cands)
            inputs.Add(new DiscoveryInput
            {
                ClassName = c.ClassName, PropName = c.PropName, DeclaredType = c.DeclaredType,
                NormPath = c.NormPath, ObjAddr = c.ObjAddr, Hex = c.Hex, Num = c.Num,
            });

        int max = query.MaxResults > 0 ? query.MaxResults : 200;
        return PivotDiscoveryEngine.Rank(inputs, n, max);
    }

    // Compute the per-class instance counts for one snapshot ONCE and persist them
    // into class_counts (the expensive COUNT(DISTINCT gobjects_index) GROUP BY runs
    // server-side via INSERT...SELECT — no row marshalling). Idempotent: guarded by
    // the pivot_index_built marker so it never recomputes. isArray selects scalar
    // (0) vs struct-array (1) classes. Called eagerly at FinalizeSnapshot and lazily
    // from the list methods (covers snapshots captured before this feature).
    internal static async Task EnsurePivotIndexAsync(
        SqliteConnection conn, long snapshotId, int isArray, CancellationToken ct,
        bool forceRebuild = false)
    {
        if (!forceRebuild)
        {
            await using var chk = conn.CreateCommand();
            chk.CommandText = "SELECT 1 FROM pivot_index_built WHERE snapshot_id=$s AND is_array=$a LIMIT 1;";
            chk.Parameters.AddWithValue("$s", snapshotId);
            chk.Parameters.AddWithValue("$a", isArray);
            if (await chk.ExecuteScalarAsync(ct) != null) return;   // already built
        }

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
        // Clear any prior counts for this snapshot+kind first: makes the rebuild
        // authoritative (forceRebuild path overwrites a partial mid-capture build)
        // AND idempotent against the dup-marker race where two concurrent lazy
        // callers both pass the check above and would otherwise double-INSERT.
        await using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM class_counts WHERE snapshot_id=$s AND is_array=$a;";
            del.Parameters.AddWithValue("$s", snapshotId);
            del.Parameters.AddWithValue("$a", isArray);
            await del.ExecuteNonQueryAsync(ct);
        }
        await using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = isArray == 0
                ? """
                  INSERT INTO class_counts(snapshot_id, class_fqn, is_array, instance_count)
                  SELECT snapshot_id, class_fqn, 0, COUNT(DISTINCT gobjects_index)
                  FROM fields WHERE snapshot_id=$s AND array_field IS NULL
                  GROUP BY class_fqn;
                  """
                : """
                  INSERT INTO class_counts(snapshot_id, class_fqn, is_array, instance_count)
                  SELECT snapshot_id, class_fqn, 1, COUNT(DISTINCT gobjects_index)
                  FROM fields WHERE snapshot_id=$s AND array_field IS NOT NULL
                  GROUP BY class_fqn;
                  """;
            ins.Parameters.AddWithValue("$s", snapshotId);
            await ins.ExecuteNonQueryAsync(ct);
        }
        await using (var mark = conn.CreateCommand())
        {
            mark.Transaction = tx;
            mark.CommandText = "INSERT OR IGNORE INTO pivot_index_built(snapshot_id, is_array) VALUES($s,$a);";
            mark.Parameters.AddWithValue("$s", snapshotId);
            mark.Parameters.AddWithValue("$a", isArray);
            await mark.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<PivotClassInfo>> ListPivotClassesAsync(
        long snapshotId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var list = new List<PivotClassInfo>();
        await using var conn = await OpenAsync(ct);
        await EnsurePivotIndexAsync(conn, snapshotId, 0, ct);   // builds once, then no-op
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT class_fqn, instance_count FROM class_counts
            WHERE snapshot_id=$s AND is_array=0
            ORDER BY instance_count DESC, class_fqn;
            """;
        cmd.Parameters.AddWithValue("$s", snapshotId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new PivotClassInfo
            {
                ClassName     = r.IsDBNull(0) ? "" : r.GetString(0),
                InstanceCount = r.IsDBNull(1) ? 0  : r.GetInt32(1),
            });
        return list;
    }

    public async Task<IReadOnlyList<PivotFieldInfo>> ListPivotFieldsAsync(
        long snapshotId, string className, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var list = new List<PivotFieldInfo>();
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT prop_name, declared_type,
                   COUNT(DISTINCT hex) AS distinctVals, COUNT(*) AS instances
            FROM fields WHERE snapshot_id=$s AND class_fqn=$c AND array_field IS NULL
            GROUP BY prop_name, declared_type ORDER BY prop_name;
            """;
        cmd.Parameters.AddWithValue("$s", snapshotId);
        cmd.Parameters.AddWithValue("$c", className);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new PivotFieldInfo
            {
                Name          = r.IsDBNull(0) ? "" : r.GetString(0),
                DeclaredType  = r.IsDBNull(1) ? "" : r.GetString(1),
                DistinctCount = r.IsDBNull(2) ? 0  : r.GetInt32(2),
                InstanceCount = r.IsDBNull(3) ? 0  : r.GetInt32(3),
            });
        return list;
    }

    public async Task<PivotResult> PivotAsync(PivotQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();   // bail before opening a connection
        // Fetch only the rows the engine needs: the key field (Field mode) + the
        // value fields, for this class. Prop names come from our own field list
        // (DB-sourced identifiers) but are still parameterised defensively.
        var props = new List<string>();
        if (query.KeyMode == PivotKeyMode.Field)
            foreach (var kf in query.EffectiveKeyFields)
                if (!string.IsNullOrEmpty(kf) && !props.Contains(kf)) props.Add(kf);
        foreach (var v in query.ValueFields)
            if (!props.Contains(v)) props.Add(v);

        var rows = new List<PivotInputRow>();
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        var sql = new StringBuilder("""
            SELECT gobjects_index, norm_path, obj_addr, prop_name, prop_offset, declared_type, hex
            FROM fields WHERE snapshot_id=$s AND class_fqn=$c AND array_field IS NULL
            """);
        cmd.Parameters.AddWithValue("$s", query.SnapshotId);
        cmd.Parameters.AddWithValue("$c", query.ClassName);
        if (props.Count > 0)
        {
            sql.Append(" AND prop_name IN (");
            for (int i = 0; i < props.Count; i++)
            {
                if (i > 0) sql.Append(',');
                var name = "$p" + i;
                sql.Append(name);
                cmd.Parameters.AddWithValue(name, props[i]);
            }
            sql.Append(')');
        }
        sql.Append(" ORDER BY gobjects_index;");
        cmd.CommandText = sql.ToString();

        bool capped = false;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            int rowCount = 0;
            while (await r.ReadAsync(ct))
            {
                if ((++rowCount & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                if (rows.Count >= PivotFetchRowCap) { capped = true; break; }
                rows.Add(new PivotInputRow
                {
                    ObjectIndex  = r.IsDBNull(0) ? -1 : r.GetInt64(0),
                    NormPath     = r.IsDBNull(1) ? "" : r.GetString(1),
                    ObjAddr      = r.IsDBNull(2) ? "" : r.GetString(2),
                    PropName     = r.IsDBNull(3) ? "" : r.GetString(3),
                    PropOffset   = r.IsDBNull(4) ? 0  : r.GetInt32(4),
                    DeclaredType = r.IsDBNull(5) ? "" : r.GetString(5),
                    Hex          = r.IsDBNull(6) ? "" : r.GetString(6),
                });
            }
        }
        if (capped)
            _log?.Warn(Constants.LogCatView,
                $"Pivot: row fetch hit the {PivotFetchRowCap:N0} cap for class {query.ClassName} — results truncated");

        var result = PivotEngine.Build(rows, query);
        if (capped) result.Truncated = true;
        return result;
    }

    // --- Phase C6: array-element pivot ---------------------------------------

    public async Task<IReadOnlyList<PivotClassInfo>> ListPivotArrayClassesAsync(
        long snapshotId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var list = new List<PivotClassInfo>();
        await using var conn = await OpenAsync(ct);
        await EnsurePivotIndexAsync(conn, snapshotId, 1, ct);   // array classes, built once
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT class_fqn, instance_count FROM class_counts
            WHERE snapshot_id=$s AND is_array=1
            ORDER BY instance_count DESC, class_fqn;
            """;
        cmd.Parameters.AddWithValue("$s", snapshotId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new PivotClassInfo
            {
                ClassName     = r.IsDBNull(0) ? "" : r.GetString(0),
                InstanceCount = r.IsDBNull(1) ? 0  : r.GetInt32(1),
            });
        return list;
    }

    public async Task<IReadOnlyList<PivotArrayFieldInfo>> ListPivotArrayFieldsAsync(
        long snapshotId, string className, CancellationToken ct = default)
    {
        var list = new List<PivotArrayFieldInfo>();
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT array_field, COALESCE(inner_key_name, ''), COUNT(*) AS elems
            FROM fields WHERE snapshot_id=$s AND class_fqn=$c AND array_field IS NOT NULL
            GROUP BY array_field, inner_key_name ORDER BY array_field;
            """;
        cmd.Parameters.AddWithValue("$s", snapshotId);
        cmd.Parameters.AddWithValue("$c", className);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new PivotArrayFieldInfo
            {
                ArrayField   = r.IsDBNull(0) ? "" : r.GetString(0),
                InnerKeyName = r.IsDBNull(1) ? "" : r.GetString(1),
                ElementCount = r.IsDBNull(2) ? 0  : r.GetInt32(2),
            });
        return list;
    }

    public async Task<IReadOnlyList<PivotFieldInfo>> ListPivotArrayPropsAsync(
        long snapshotId, string className, string arrayField, CancellationToken ct = default)
    {
        var list = new List<PivotFieldInfo>();
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT inner_prop_name, declared_type,
                   COUNT(DISTINCT hex) AS distinctVals, COUNT(*) AS elems
            FROM fields WHERE snapshot_id=$s AND class_fqn=$c AND array_field=$af AND array_field IS NOT NULL
            GROUP BY inner_prop_name, declared_type ORDER BY inner_prop_name;
            """;
        cmd.Parameters.AddWithValue("$s", snapshotId);
        cmd.Parameters.AddWithValue("$c", className);
        cmd.Parameters.AddWithValue("$af", arrayField);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new PivotFieldInfo
            {
                Name          = r.IsDBNull(0) ? "" : r.GetString(0),
                DeclaredType  = r.IsDBNull(1) ? "" : r.GetString(1),
                DistinctCount = r.IsDBNull(2) ? 0  : r.GetInt32(2),
                InstanceCount = r.IsDBNull(3) ? 0  : r.GetInt32(3),
            });
        return list;
    }

    public async Task<PivotResult> PivotArrayAsync(ArrayPivotQuery query, CancellationToken ct = default)
    {
        var props = new List<string>();
        foreach (var v in query.ValueProps)
            if (!props.Contains(v)) props.Add(v);

        var rows = new List<PivotInputRow>();
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        var sql = new StringBuilder("""
            SELECT gobjects_index, elem_index, obj_addr, inner_key_value, inner_prop_name, declared_type, hex
            FROM fields WHERE snapshot_id=$s AND class_fqn=$c AND array_field=$af AND array_field IS NOT NULL
            """);
        cmd.Parameters.AddWithValue("$s",  query.SnapshotId);
        cmd.Parameters.AddWithValue("$c",  query.ClassName);
        cmd.Parameters.AddWithValue("$af", query.ArrayField);
        if (props.Count > 0)
        {
            sql.Append(" AND inner_prop_name IN (");
            for (int i = 0; i < props.Count; i++)
            {
                if (i > 0) sql.Append(',');
                var name = "$p" + i;
                sql.Append(name);
                cmd.Parameters.AddWithValue(name, props[i]);
            }
            sql.Append(')');
        }
        sql.Append(" ORDER BY gobjects_index, elem_index;");
        cmd.CommandText = sql.ToString();

        // Each (owner instance, element index) pair becomes one synthetic
        // "instance" so PivotEngine folds an element's inner props together; the
        // inner-key value becomes the Identity group key (reorder-/session-immune).
        var idMap = new Dictionary<(long, int), long>();
        long nextId = 0;
        bool capped = false;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            int rowCount = 0;
            while (await r.ReadAsync(ct))
            {
                if ((++rowCount & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                if (rows.Count >= PivotFetchRowCap) { capped = true; break; }
                long objIdx = r.IsDBNull(0) ? -1 : r.GetInt64(0);
                int  elem   = r.IsDBNull(1) ? -1 : r.GetInt32(1);
                var key = (objIdx, elem);
                if (!idMap.TryGetValue(key, out var sid)) { sid = nextId++; idMap[key] = sid; }
                rows.Add(new PivotInputRow
                {
                    ObjectIndex  = sid,
                    NormPath     = r.IsDBNull(3) ? "(no key)" : r.GetString(3),  // inner-key value = group key
                    ObjAddr      = r.IsDBNull(2) ? "" : r.GetString(2),
                    PropName     = r.IsDBNull(4) ? "" : r.GetString(4),
                    DeclaredType = r.IsDBNull(5) ? "" : r.GetString(5),
                    Hex          = r.IsDBNull(6) ? "" : r.GetString(6),
                });
            }
        }
        if (capped)
            _log?.Warn(Constants.LogCatView,
                $"Array pivot: row fetch hit the {PivotFetchRowCap:N0} cap for {query.ClassName}.{query.ArrayField} — results truncated");

        var pq = new PivotQuery
        {
            KeyMode     = PivotKeyMode.Identity,   // group key = inner-key value (NormPath)
            ValueFields = props,
            MaxGroups   = query.MaxGroups,
        };
        var result = PivotEngine.Build(rows, pq);
        if (capped) result.Truncated = true;
        return result;
    }

    public async Task<int> DeleteUnusableSnapshotsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        int removed;
        await using (var count = conn.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM snapshots WHERE is_usable=0;";
            removed = (int)(long)(await count.ExecuteScalarAsync(ct) ?? 0L);
        }
        if (removed == 0) return 0;

        // Delete the flagged snapshots' rows across all four tables in one statement
        // (the per-row tables key on snapshot_id; the metadata row holds the flag).
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "DELETE FROM fields            WHERE snapshot_id IN (SELECT id FROM snapshots WHERE is_usable=0); " +
                "DELETE FROM class_counts      WHERE snapshot_id IN (SELECT id FROM snapshots WHERE is_usable=0); " +
                "DELETE FROM pivot_index_built WHERE snapshot_id IN (SELECT id FROM snapshots WHERE is_usable=0); " +
                "DELETE FROM snapshots         WHERE is_usable=0;";
            await cmd.ExecuteNonQueryAsync(ct);
        }
        // Reclaim the disk: an unusable capture can still be large, and this runs at
        // the START of a new capture, so freeing it now keeps the file from bloating.
        await ReclaimDiskAsync(conn, ct);
        _log?.Info(Constants.LogCatView, $"SnapshotStore: deleted {removed} unusable snapshot(s) (reclaimed disk)");
        return removed;
    }

    public async Task DeleteSnapshotAsync(long snapshotId, bool reclaim = false, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "DELETE FROM fields WHERE snapshot_id=$id; " +
                "DELETE FROM class_counts WHERE snapshot_id=$id; " +
                "DELETE FROM pivot_index_built WHERE snapshot_id=$id; " +
                "DELETE FROM snapshots WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", snapshotId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        // DELETE frees pages internally but leaves the file size unchanged; a cancelled
        // partial capture can be multi-GB, so reclaim the disk now when asked.
        if (reclaim) await ReclaimDiskAsync(conn, ct);
        _log?.Info(Constants.LogCatView,
            $"SnapshotStore: deleted snapshot #{snapshotId}{(reclaim ? " (reclaimed disk)" : "")}");
    }

    // Return freed pages to the OS by truncating the main .db file. VACUUM only shrinks
    // the file in a ROLLBACK journal mode — in WAL mode the vacuumed (compact) pages land
    // in the -wal and a passive checkpoint never shrinks the main file, so the on-disk
    // size would stay put. Fold the WAL back, switch to DELETE mode (which truncates on
    // VACUUM), VACUUM, then restore WAL.
    //
    // BEST-EFFORT by contract: the caller has ALREADY deleted the rows, so the data is
    // gone regardless. Switching WAL->DELETE needs EXCLUSIVE access, which SQLite refuses
    // ("database is locked") when ANY other connection still holds this DB open — including
    // IDLE POOLED connections left by a just-finished capture session or a list read. So
    // (a) evict idle pooled handles first via ClearPool, and (b) swallow a lock so a
    // reclaim failure never throws out of Delete All / cancel / quota (which would leave the
    // UI list stale + the DB wedged), and ALWAYS restore WAL in finally so the next
    // capture/read isn't stuck in rollback mode. Worst case: rows deleted, file not shrunk
    // this time (recovered by the next reclaim or an app restart, when nothing else is open).
    // Caller runs this off the UI thread; no open transaction may be held (VACUUM forbids one).
    private async Task ReclaimDiskAsync(SqliteConnection conn, CancellationToken ct)
    {
        try
        {
            SqliteConnection.ClearPool(conn);   // close idle pooled handles -> exclusive
            await ExecAsync(conn, "PRAGMA wal_checkpoint(TRUNCATE);", ct);
            await ExecAsync(conn, "PRAGMA journal_mode=DELETE;", ct);
            await ExecAsync(conn, "VACUUM;", ct);
        }
        catch (Exception ex)
        {
            _log?.Warn(Constants.LogCatView,
                $"SnapshotStore: disk reclaim skipped ({ex.Message}) — rows deleted, file not shrunk this time");
        }
        finally
        {
            try { await ExecAsync(conn, "PRAGMA journal_mode=WAL;", ct); } catch { /* leave as-is */ }
        }
    }

    public async Task DeleteAllSnapshotsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Truncate every table for the active game's DB, then reclaim the
        // (potentially multi-GB) file on disk.
        cmd.CommandText =
            "DELETE FROM fields; DELETE FROM class_counts; " +
            "DELETE FROM pivot_index_built; DELETE FROM snapshots;";
        await cmd.ExecuteNonQueryAsync(ct);
        await ReclaimDiskAsync(conn, ct);   // VACUUM in rollback mode so the file actually shrinks
        _log?.Info(Constants.LogCatView, "SnapshotStore: deleted ALL snapshots for the active game");
    }

    // Whole-disk wipe across EVERY game: delete the snapshot DB files outright
    // (the .db, its -wal/-shm sidecars, and the per-game .denylist.json) rather than
    // truncating rows. ClearAllPools first so a just-finished read/capture isn't still
    // holding a pooled handle that locks the file on Windows. Per-file best-effort: a
    // file held open by an in-flight capture throws on Delete -> counted as skipped,
    // never fatal. The blocking work (ClearAllPools + the File.Delete loop, which can
    // stall on a locked file or a network share) runs on a thread-pool thread so the
    // calling [RelayCommand] doesn't freeze the UI. (bookmarks.*.json belong to the
    // Live Walker bookmark feature, not the snapshot DB, so they are left alone.)
    //
    // Sweeps the LEGACY flat root as well as Snapshots\. Migration moves a game's files
    // only as an all-or-nothing group, so a set it had to leave behind (a name collision
    // with an already-migrated copy) is still on disk and still this button's problem:
    // "for EVERY game" cannot quietly mean "for every game in the new folder".
    public Task<SnapshotWipeResult> DeleteAllSnapshotDatabasesAsync(CancellationToken ct = default)
        => Task.Run(() =>
        {
            // Evict ALL idle pooled connections (every connection string), so the per-game
            // .db files aren't locked by a pooled handle left behind by a list read / capture.
            SqliteConnection.ClearAllPools();
            // The files are about to go away, so the "schema already built" memo describes
            // nothing — drop it or the next open skips CREATE TABLE and hits "no such table".
            // Cleared up-front: a file we fail to delete is re-ensured harmlessly (the memo
            // is only ever an optimisation), whereas a missed clear is a broken store.
            InvalidateSchemaMemo();

            int deleted = 0, skipped = 0;
            // Order matters only for tidiness: drop sidecars/denylists alongside each .db.
            // The .db count is what we report; sidecar/denylist removal is silent cleanup.
            var dirs = string.Equals(_dir, _legacyDir, StringComparison.OrdinalIgnoreCase)
                ? new[] { _dir }
                : new[] { _dir, _legacyDir };
            foreach (var dir in dirs)
            foreach (var pattern in new[]
            {
                $"{Constants.SnapshotDbPrefix}.*.db",
                $"{Constants.SnapshotDbPrefix}.*.db-wal",
                $"{Constants.SnapshotDbPrefix}.*.db-shm",
                $"{Constants.SnapshotDbPrefix}.*.denylist.json",
            })
            {
                List<string> files;
                try { files = Directory.EnumerateFiles(dir, pattern).ToList(); }
                catch { continue; }   // directory gone / unreadable — nothing to do for this pattern

                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
                    // Only the bare .db files count toward the deleted/skipped totals — the
                    // "*.db" glob matches exactly the .db extension (sidecars end in
                    // .db-wal/.db-shm, so they fall to their own patterns).
                    bool isDb = f.EndsWith(".db", StringComparison.OrdinalIgnoreCase);
                    try
                    {
                        File.Delete(f);
                        if (isDb) deleted++;
                    }
                    catch (Exception ex)
                    {
                        if (isDb) skipped++;
                        _log?.Warn(Constants.LogCatView,
                            $"SnapshotStore: could not delete '{Path.GetFileName(f)}' ({ex.Message}) — in use?");
                    }
                }
            }

            _log?.Info(Constants.LogCatView,
                $"SnapshotStore: removed {deleted} snapshot DB file(s) (all games), {skipped} skipped (in use)");
            return new SnapshotWipeResult(deleted, skipped);
        }, ct);

    // --- N1: Top-N noise contributor accumulator (used by SPC + Diff) ---
    //
    // Walks each emitted result row, keeps per-class hit counts + per-(class, prop)
    // sub-counts, then writes a Top-50 ranking with up to 3 sample props per class.
    // Pure in-memory; the rows that drive it are the same rows the user is about to
    // see, so the picker reflects the predicate they just ran rather than the raw
    // capture distribution.
    private sealed class NoiseAccumulator
    {
        private const int kTopN          = 50;
        private const int kSamplePropMax = 3;
        private readonly Dictionary<string, ClassBucket> _buckets =
            new(StringComparer.Ordinal);

        private sealed class ClassBucket
        {
            public int HitCount;
            public readonly Dictionary<string, int> PropCounts =
                new(StringComparer.Ordinal);
        }

        public void Bump(string className, string propName)
        {
            // A malformed (NULL→"") class_fqn shouldn't surface as a blank,
            // un-tickable picker row.
            if (string.IsNullOrEmpty(className)) return;
            if (!_buckets.TryGetValue(className, out var b))
            {
                b = new ClassBucket();
                _buckets[className] = b;
            }
            b.HitCount++;
            b.PropCounts.TryGetValue(propName, out var c);
            b.PropCounts[propName] = c + 1;
        }

        public void WriteTo(List<ClassNoiseRow> dest)
        {
            // Sort classes by hit count desc, tie-break by name for stability.
            var ordered = _buckets
                .Select(kv => (Cls: kv.Key, Count: kv.Value.HitCount, Props: kv.Value.PropCounts))
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Cls, StringComparer.Ordinal)
                .Take(kTopN);

            foreach (var x in ordered)
            {
                var sampleProps = x.Props
                    .OrderByDescending(p => p.Value)
                    .ThenBy(p => p.Key, StringComparer.Ordinal)
                    .Take(kSamplePropMax)
                    .Select(p => p.Key)
                    .ToList();
                dest.Add(new ClassNoiseRow
                {
                    ClassName   = x.Cls,
                    HitCount    = x.Count,
                    SampleProps = sampleProps,
                });
            }
        }
    }

    // --- N1: per-game, PER-TAB class denylists (noise picker) ---
    //
    // Stored next to the per-game DB as snapshots.<pe_hash>.denylist.json so the
    // lists auto-follow the game and survive quota-driven snapshot eviction. The
    // file holds three INDEPENDENT lists (Diff / Spc / Pivot) — a class hidden in
    // one tab never affects the others. Source-gen JSON (AOT-safe), atomic write,
    // swallow-and-log on failure — mirrors ExperimentalGate's persistence pattern.

    private static readonly ClassDenylistJsonContext s_denylistJsonCtx =
        ClassDenylistJsonContext.Default;

    private string DenylistPath =>
        Path.Combine(_dir, $"{Constants.SnapshotDbPrefix}.{(_peHash.Length > 0 ? _peHash : "default")}.denylist.json");

    // Load the whole settings object (all three lists); defensive on missing /
    // corrupt file. Returns a fresh empty object on any failure.
    private ClassDenylistSettings LoadDenylistSettings()
    {
        try
        {
            var path = DenylistPath;
            if (!File.Exists(path)) return new ClassDenylistSettings();
            var json = File.ReadAllText(path);
            return System.Text.Json.JsonSerializer.Deserialize(json, s_denylistJsonCtx.ClassDenylistSettings)
                   ?? new ClassDenylistSettings();
        }
        catch (Exception ex)
        {
            _log?.Warn(Constants.LogCatView,
                $"SnapshotStore: failed to load denylist, using empty: {ex.Message}");
            return new ClassDenylistSettings();
        }
    }

    private static List<string> ScopeList(ClassDenylistSettings s, DenylistScope scope) => scope switch
    {
        DenylistScope.Diff  => s.Diff,
        DenylistScope.Spc   => s.Spc,
        DenylistScope.Pivot => s.Pivot,
        _                   => s.Diff,
    };

    public HashSet<string> GetClassDenylist(DenylistScope scope)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in ScopeList(LoadDenylistSettings(), scope))
            if (!string.IsNullOrEmpty(c)) set.Add(c);
        return set;
    }

    public void SetClassDenylist(DenylistScope scope, HashSet<string> classes)
    {
        // No-op without an active game — the default DB shouldn't accumulate
        // game-specific noise picks. UI gates the save behind SetActiveGame
        // already, so this is defence in depth.
        if (_peHash.Length == 0) return;
        try
        {
            // Read-modify-write: preserve the other two tabs' lists.
            var settings = LoadDenylistSettings();
            var sorted = new List<string>(classes);
            sorted.Sort(StringComparer.Ordinal);  // stable on-disk order for diffability
            switch (scope)
            {
                case DenylistScope.Diff:  settings.Diff  = sorted; break;
                case DenylistScope.Spc:   settings.Spc   = sorted; break;
                case DenylistScope.Pivot: settings.Pivot = sorted; break;
            }
            var json = System.Text.Json.JsonSerializer.Serialize(
                settings, s_denylistJsonCtx.ClassDenylistSettings);
            var path = DenylistPath;
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
            _log?.Info(Constants.LogCatView,
                $"SnapshotStore: saved {scope} denylist ({sorted.Count} classes) -> {path}");
        }
        catch (Exception ex)
        {
            _log?.Error(Constants.LogCatView, "SnapshotStore: failed to save denylist", ex);
        }
    }
}
