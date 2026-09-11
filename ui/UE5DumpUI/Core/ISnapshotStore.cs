using UE5DumpUI.Models;

namespace UE5DumpUI.Core;

/// <summary>
/// SQLite-backed persistence for experimental snapshots. A snapshot is created
/// once (returning its id), streamed in chunks (so neither the game process nor
/// the UI holds the whole capture), then finalised with its totals. Raw ADO.NET
/// only — no EF Core (reflection-based query breaks trim/AOT).
/// </summary>
/// <summary>
/// A bulk-capture write session (one connection + bulk pragmas + a transaction committed
/// every N chunks). <see cref="WriteChunk"/> runs SYNCHRONOUSLY — call it from a background
/// consumer task, never the UI thread. Disposing commits the tail + restores durability
/// pragmas. See <see cref="ISnapshotStore.BeginCaptureSessionAsync"/>.
/// </summary>
public interface ICaptureSession : IAsyncDisposable
{
    /// <summary>Insert one captured chunk's rows on the session's connection/transaction.
    /// Returns the number of field rows written. Synchronous (SQLite is sync under
    /// Microsoft.Data.Sqlite); the caller runs it off the UI thread.</summary>
    int WriteChunk(long snapshotId, IReadOnlyList<SnapshotCapturedObject> objects,
                   CancellationToken ct = default);

    /// <summary>Approximate footprint of the capture so far (committed db + WAL file
    /// bytes + an estimate of rows written since the last commit), in bytes — the live
    /// gauge the streaming loop polls to enforce the opt-in max-dataset cap.
    /// The on-disk part is monotonic non-decreasing during a capture (passive
    /// autocheckpoints recycle WAL frames in place without shrinking the -wal file; only
    /// the post-capture TRUNCATE checkpoint shrinks it, never overlapping a capture), and
    /// the uncommitted estimate keeps the cap tight (no commit-batch lag). No SQL, no
    /// checkpoint.</summary>
    long CurrentSizeBytes();

    /// <summary>Finalise the captured snapshot on the session connection: commit the row
    /// tail, stamp object/field totals, and write the per-class pivot counts (class_counts)
    /// INCREMENTALLY from per-class distinct GObjects-index sets accumulated while writing —
    /// replacing the ~10 s <c>COUNT(DISTINCT) GROUP BY ×2</c> the lazy build runs. Call once,
    /// after the last <see cref="WriteChunk"/> and before disposing. The lazy GROUP-BY build
    /// (<c>EnsurePivotIndexAsync</c>) remains the fallback for snapshots NOT captured via a
    /// session. <paramref name="isUsable"/> false marks the capture temporally
    /// inconsistent (GObjects drifted mid-capture) so SPC/Pivot skip it and it's
    /// auto-deleted before the next capture. <paramref name="partialReason"/> (a
    /// <c>Constants.SnapshotPartial*</c> token, "" = complete) records why a KEPT capture
    /// stopped early -- its own marker, because a partial stays usable [W1-PARTIAL-MARK].</summary>
    Task CompleteSnapshotAsync(long snapshotId, int objectCount, int fieldCount,
                               bool isUsable = true, string partialReason = "", CancellationToken ct = default);
}

public interface ISnapshotStore
{
    /// <summary>Absolute path of the SQLite database file for the active game.</summary>
    string DatabasePath { get; }

    /// <summary>
    /// Scope all subsequent operations to a per-game database file
    /// (snapshots.&lt;pe_hash&gt;.db). Called when the engine state arrives so
    /// each game's snapshots stay isolated — no cross-game mixing in the list
    /// or in SPC / Pivot joins, isolated growth, and isolated corruption blast
    /// radius. pe_hash is stable across launches of the same build, so all
    /// sessions of a game share one file.
    /// </summary>
    void SetActiveGame(string? peHash);

    /// <summary>Insert a new snapshot row; returns its generated id.</summary>
    Task<long> CreateSnapshotAsync(SnapshotMeta meta, CancellationToken ct = default);

    /// <summary>Append one captured chunk (flattened to field rows) under one
    /// transaction. Returns the number of field rows written. One connection +
    /// transaction per call — used by tests / seeding; the streaming capture flow uses
    /// <see cref="BeginCaptureSessionAsync"/> for the bulk path.</summary>
    Task<int> WriteChunkAsync(long snapshotId, IReadOnlyList<SnapshotCapturedObject> objects,
                              CancellationToken ct = default);

    /// <summary>Open a bulk-capture write session: ONE connection with bulk-load pragmas
    /// (`synchronous=OFF` etc., safe because a partial capture is discarded) + a
    /// transaction committed every N chunks, so a multi-million-row capture isn't paying
    /// a fresh connection + `EnsureSchema` + fsync per chunk. The streaming capture loop
    /// writes each chunk via <see cref="ICaptureSession.WriteChunk"/> from a background
    /// consumer task; <see cref="IAsyncDisposable.DisposeAsync"/> commits the tail and
    /// restores durability pragmas.</summary>
    Task<ICaptureSession> BeginCaptureSessionAsync(CancellationToken ct = default);

    /// <summary>Record the final object/field totals on the snapshot row.</summary>
    Task FinalizeSnapshotAsync(long snapshotId, int objectCount, int fieldCount,
                               CancellationToken ct = default);

    /// <summary>All snapshots for the active game, newest first. Each row's
    /// <see cref="SnapshotMeta.EstBytes"/> is set (pro-rated from the DB size).</summary>
    Task<IReadOnlyList<SnapshotMeta>> ListSnapshotsAsync(CancellationToken ct = default);

    /// <summary>Delete a snapshot and all its field rows. When
    /// <paramref name="reclaim"/> is true, ALSO fold the WAL back and VACUUM so the
    /// disk space is returned to the OS immediately (DELETE alone frees the pages
    /// internally but leaves the file size unchanged). Used on capture-cancel, where
    /// the discarded partial can be multi-GB and the user expects the space back.</summary>
    Task DeleteSnapshotAsync(long snapshotId, bool reclaim = false, CancellationToken ct = default);

    /// <summary>Delete EVERY snapshot for the active game (truncate all tables)
    /// and VACUUM the DB file. Irreversible.</summary>
    Task DeleteAllSnapshotsAsync(CancellationToken ct = default);

    /// <summary>Delete every snapshot flagged unusable (<c>is_usable=0</c>) for the
    /// active game — captures that spanned a GObjects drift and are temporally
    /// inconsistent. Called at the START of a new capture so a flagged-but-kept
    /// snapshot is reclaimed once, deferred (the user sees it flagged in the list
    /// until then). Returns how many were removed. Default no-op (returns 0) so
    /// lightweight test doubles need not implement it.</summary>
    Task<int> DeleteUnusableSnapshotsAsync(CancellationToken ct = default)
        => Task.FromResult(0);

    /// <summary>Delete the snapshot database FILES for EVERY game (all
    /// <c>snapshots.*.db</c> plus their <c>-wal</c>/<c>-shm</c> sidecars and the
    /// per-game <c>snapshots.*.denylist.json</c>) from disk — a whole-file wipe, not
    /// a row truncate. Idle pooled connections are released first
    /// (<c>SqliteConnection.ClearAllPools</c>) so a just-finished read/capture doesn't
    /// keep a file locked. Best-effort per file: a file still held open (an in-flight
    /// capture) is skipped, never fatal. Returns how many .db files were deleted vs
    /// skipped (still in use). Irreversible.
    ///
    /// Default no-op (returns 0/0) so lightweight test doubles need not implement it;
    /// the real <see cref="Services.SnapshotStore"/> overrides it.</summary>
    Task<SnapshotWipeResult> DeleteAllSnapshotDatabasesAsync(CancellationToken ct = default)
        => Task.FromResult(new SnapshotWipeResult(0, 0));

    /// <summary>Active game's DB file size + all-games total + snapshot count.</summary>
    Task<SnapshotUsage> GetUsageAsync(CancellationToken ct = default);

    /// <summary>Diff snapshot <paramref name="idA"/> (old) against
    /// <paramref name="idB"/> (new): fields whose value changed, joined by
    /// (class, GObjects index, property). Both must be in the active game's DB
    /// (in-session identity). Returns changed rows (filtered/capped) plus
    /// Added/Removed churn counts.</summary>
    Task<SnapshotDiffResult> DiffSnapshotsAsync(
        long idA, long idB, SnapshotDiffFilter filter, CancellationToken ct = default);

    /// <summary>Run an SPC query over the active game's DB: intersect the fields
    /// present in all of <see cref="SpcQuery.SnapshotIds"/> under the chosen join
    /// mode, then keep those whose value sequence satisfies the directional
    /// predicate chain. Multi-session by design (snapshots may span game
    /// launches). Pure SQL — no DLL/pipe. See
    /// docs/experimental-snapshot-spc-pivot.md §"Phase B".</summary>
    Task<SpcResult> SpcQueryAsync(SpcQuery query, CancellationToken ct = default);

    /// <summary>Run an SPC GROUP query: find OBJECTS in the active game's DB holding N
    /// (2–4) fields, each satisfying its OWN per-snapshot predicate chain across
    /// <see cref="SpcGroupQuery.SnapshotIds"/> (≥ 2), at DISTINCT offsets. The
    /// N-snapshot, object-aware generalisation of Snapshot Group Match Mode B (the
    /// <c>Orden</c>-reuse seam group-value-scan-spec §3.1 reserved for SPC). Reuses the
    /// SPC intersection load + <see cref="Services.SpcEngine"/> for the per-slot chains
    /// + <see cref="Services.GroupMatch"/> for the SDR. Pure SQL fetch — no DLL/pipe.</summary>
    Task<SpcGroupResult> SpcGroupQueryAsync(SpcGroupQuery query, CancellationToken ct = default);

    /// <summary>Snapshot Group Match: find objects in the snapshot(s) that hold ALL
    /// of N values (2–4) at DISTINCT numeric offsets, in any order — the object-aware
    /// "Group Scan" over captured data (the <c>Orden</c> reuse seam, run in C#).
    /// <see cref="SnapshotGroupQuery.SnapshotIds"/> length 1 = Mode A (single-snapshot
    /// absolute); ≥ 2 = Mode B (cross-snapshot temporal — relative predicates incl.
    /// Unchanged). Pure SQL fetch + <see cref="Services.GroupMatch"/>. See
    /// docs/snapshot-group-match-spec.md.</summary>
    Task<SnapshotGroupResult> GroupMatchAsync(SnapshotGroupQuery query, CancellationToken ct = default);

    /// <summary>Change-driven discovery over the active game's DB: find the
    /// (class, property) targets whose value MOVED across
    /// <see cref="DiscoveryQuery.SnapshotIds"/> (≥ 2, oldest → newest), rolled up per
    /// (class, prop) and ranked by "looks like game state" (interest × change ×
    /// selectivity × population). The automatic front-door to Class Pivot — no
    /// class/key guessing. Reuses the SPC intersection load; ranks via the pure
    /// <see cref="Services.PivotDiscoveryEngine"/>. See
    /// docs/experimental-snapshot-spc-pivot.md §"Phase C — C3".</summary>
    Task<DiscoveryResult> DiscoverChangesAsync(DiscoveryQuery query, CancellationToken ct = default);

    /// <summary>Classes present in a snapshot with their live-instance counts,
    /// most-populous first — populates the Class Pivot class picker.</summary>
    Task<IReadOnlyList<PivotClassInfo>> ListPivotClassesAsync(
        long snapshotId, CancellationToken ct = default);

    /// <summary>Numeric fields of one class in a snapshot with cardinality stats
    /// (distinct values / instance count) — drives key ranking + value picks.</summary>
    Task<IReadOnlyList<PivotFieldInfo>> ListPivotFieldsAsync(
        long snapshotId, string className, CancellationToken ct = default);

    /// <summary>Group a class's instances by identity or a key field and project
    /// the requested value fields per group (collisions rendered ⟨N: …⟩). Pure
    /// SQL fetch + <see cref="Services.PivotEngine"/>. See
    /// docs/experimental-snapshot-spc-pivot.md §"Phase C".</summary>
    Task<PivotResult> PivotAsync(PivotQuery query, CancellationToken ct = default);

    // --- Phase C6: array-element pivot (struct-array inner-key join) ---

    /// <summary>Classes in a snapshot that captured struct-array elements
    /// (array_field rows), with owner-instance counts — the array-pivot class
    /// picker.</summary>
    Task<IReadOnlyList<PivotClassInfo>> ListPivotArrayClassesAsync(
        long snapshotId, CancellationToken ct = default);

    /// <summary>The struct-array fields captured for one class (e.g. "Cargo"),
    /// each with its inner-key name + element count.</summary>
    Task<IReadOnlyList<PivotArrayFieldInfo>> ListPivotArrayFieldsAsync(
        long snapshotId, string className, CancellationToken ct = default);

    /// <summary>The inner numeric props of one captured struct-array (e.g.
    /// "Quantity") with cardinality stats — the value-field picker.</summary>
    Task<IReadOnlyList<PivotFieldInfo>> ListPivotArrayPropsAsync(
        long snapshotId, string className, string arrayField, CancellationToken ct = default);

    /// <summary>Group a class's captured struct-array elements by inner-key value
    /// (e.g. Cargo by ItemID), projecting the inner numeric props per group.
    /// Reorder- and session-immune (keyed by value, not array index). Pure SQL
    /// fetch + <see cref="Services.PivotEngine"/> in Identity mode. See
    /// docs/experimental-snapshot-spc-pivot.md §"Phase C — C6".</summary>
    Task<PivotResult> PivotArrayAsync(ArrayPivotQuery query, CancellationToken ct = default);

    /// <summary>Drop oldest snapshots (FIFO) from the active game's DB until it
    /// fits <paramref name="quotaBytes"/>, then VACUUM to reclaim the space. The
    /// newest snapshot is always kept (even if it alone exceeds the quota).
    /// <paramref name="quotaBytes"/> &lt;= 0 means unlimited (no-op). Returns the
    /// number of snapshots dropped.</summary>
    Task<int> EnforceQuotaAsync(long quotaBytes, CancellationToken ct = default);

    /// <summary>Drop oldest snapshots (FIFO) from the active game's DB until only
    /// <paramref name="keepNewest"/> remain, then VACUUM to reclaim the space — the
    /// count-based sibling of <see cref="EnforceQuotaAsync"/>, used by the
    /// auto-snapshot loop's "keep newest N" retention. <paramref name="keepNewest"/>
    /// &lt;= 0 means unlimited (no-op). Returns the number of snapshots dropped.
    /// Default no-op (returns 0) so lightweight test doubles need not implement it;
    /// the real <see cref="Services.SnapshotStore"/> overrides it.</summary>
    Task<int> EnforceCountAsync(int keepNewest, CancellationToken ct = default)
        => Task.FromResult(0);

    // --- N1: per-game, per-tab class denylists (noise picker) ---

    /// <summary>Read the active game's class denylist for one tab
    /// (<paramref name="scope"/>). Returns an empty set when no game is active or
    /// the denylist file is absent / malformed (defensive — never throws on a
    /// missing/corrupt file). The three scopes are independent. Ordinal comparison.</summary>
    HashSet<string> GetClassDenylist(DenylistScope scope);

    /// <summary>Persist the active game's class denylist for one tab
    /// (<paramref name="scope"/>), preserving the other two tabs' lists (atomic
    /// temp-then-rename write). No-op when no game is active. Pass an empty set to
    /// clear that tab's picks. Ordinal comparison. Set is copied — caller's set is
    /// not retained.</summary>
    void SetClassDenylist(DenylistScope scope, HashSet<string> classes);
}
