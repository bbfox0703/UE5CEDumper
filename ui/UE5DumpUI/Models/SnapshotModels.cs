namespace UE5DumpUI.Models;

/// <summary>
/// Metadata for one captured snapshot (one row in the <c>snapshots</c> SQLite
/// table). A snapshot is a type-agnostic point-in-time capture of every numeric
/// UPROPERTY of every (scoped) UObject, used by the experimental Snapshot /
/// SPC / Pivot tabs. See docs/experimental-snapshot-spc-pivot.md.
/// </summary>
public sealed class SnapshotMeta
{
    public long   Id            { get; set; }
    public string Label         { get; set; } = "";
    /// <summary>ISO-8601 capture timestamp (UI-stamped).</summary>
    public string CapturedAt    { get; set; } = "";
    /// <summary>Game build hash (from get_pointers) — distinguishes games.</summary>
    public string PeHash        { get; set; } = "";
    /// <summary>pe_hash + launch token — distinguishes game restarts (sessions).</summary>
    public string GameSessionId { get; set; } = "";
    public int    UeVersion     { get; set; }
    /// <summary>Objects that contributed at least one numeric field.</summary>
    public int    ObjectCount   { get; set; }
    /// <summary>Total numeric field rows captured.</summary>
    public int    FieldCount    { get; set; }
    /// <summary>Capture scope tag, e.g. "NumericNoByte".</summary>
    public string Scope         { get; set; } = "NumericNoByte";

    /// <summary>False when the capture spanned a GObjects-count drift (likely a
    /// level transition / mass spawn-free) and is therefore temporally
    /// inconsistent. Such snapshots are excluded from SPC Query / Class Pivot
    /// pickers and auto-deleted before the next capture. Defaults true so every
    /// existing row (and any capture proven clean) is usable. See
    /// <see cref="Services.SnapshotConsistency"/>.</summary>
    public bool IsUsable { get; set; } = true;

    /// <summary>"⚠" for an unusable (inconsistent) snapshot, else empty — a compact
    /// status glyph for the saved-snapshots grid.</summary>
    public string UsabilityBadge => IsUsable ? "" : "⚠";

    /// <summary>Label with a leading ⚠ when the snapshot is unusable, so the
    /// saved-snapshots grid flags it without needing a separate column.</summary>
    public string LabelDisplay => IsUsable ? Label : $"⚠ {Label}";

    /// <summary>Estimated on-disk size of this snapshot in bytes (field_count
    /// pro-rated against the DB file size). Computed by the store on list, not
    /// persisted — snapshots share one per-game DB file, so exact per-snapshot
    /// bytes aren't recoverable.</summary>
    public long EstBytes { get; set; }

    /// <summary>Human-readable estimated size, e.g. "42.3 MB".</summary>
    public string EstSizeDisplay => SnapshotFormat.Bytes(EstBytes);

    /// <summary>Label + captured local time — used in the diff / pivot snapshot
    /// ComboBoxes so a custom label never hides WHEN the snapshot was taken
    /// (those pickers show only one line, unlike the saved-snapshots grid which
    /// has a separate Captured column).</summary>
    public string PickerDisplay
    {
        get
        {
            var prefix = IsUsable ? "" : "⚠ ";
            if (System.DateTimeOffset.TryParse(CapturedAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dto))
                return $"{prefix}{Label}  ·  {dto.LocalDateTime:yyyy-MM-dd HH:mm:ss}";
            return $"{prefix}{Label}";
        }
    }
}

/// <summary>Per-game snapshot DB disk usage, for the quota/usage UI.</summary>
public sealed class SnapshotUsage
{
    /// <summary>Active game's DB file size in bytes.</summary>
    public long   GameDbBytes   { get; set; }
    /// <summary>Sum of all snapshots.*.db files in bytes (all games).</summary>
    public long   AllGamesBytes { get; set; }
    /// <summary>Snapshots in the active game's DB.</summary>
    public int    SnapshotCount { get; set; }
}

/// <summary>Outcome of a whole-disk snapshot wipe
/// (<see cref="Core.ISnapshotStore.DeleteAllSnapshotDatabasesAsync"/>):
/// how many .db files were deleted vs skipped because they were still in use
/// (e.g. an active capture holds the file open).</summary>
public readonly record struct SnapshotWipeResult(int Deleted, int Skipped);

/// <summary>One UObject captured in a snapshot chunk (transient — flattened
/// into <c>fields</c> rows by the store, not persisted as-is).</summary>
public sealed class SnapshotCapturedObject
{
    public int    Index          { get; set; } = -1;  // GObjects index (in-session join key)
    public string Addr           { get; set; } = "";  // session-local; for CE export
    public string Name           { get; set; } = "";
    public string ClassName      { get; set; } = "";
    public string OuterClassName { get; set; } = "";  // loose-join component
    public string Path           { get; set; } = "";  // full object path (cross-session id)
    public List<SnapshotCapturedField> Fields { get; set; } = new();
    /// <summary>Struct-array inner-key captures (Phase A1b).</summary>
    public List<SnapshotCapturedArray> Arrays { get; set; } = new();
}

/// <summary>One element of a captured struct-array, with a reorder-immune inner
/// key (e.g. FCargoSlot.ItemID = "Fuel") and its numeric inner fields.</summary>
public sealed class SnapshotCapturedArrayElement
{
    public int    Index    { get; set; }
    public string KeyName  { get; set; } = "";   // inner-key field (e.g. "ItemID"); "" if none
    public string KeyValue { get; set; } = "";   // rendered inner-key value (e.g. "Fuel")
    public List<SnapshotCapturedField> Fields { get; set; } = new();
}

/// <summary>A captured struct-array (e.g. "Cargo") and its elements.</summary>
public sealed class SnapshotCapturedArray
{
    public string Field { get; set; } = "";
    public List<SnapshotCapturedArrayElement> Elements { get; set; } = new();
}

/// <summary>One numeric scalar field captured for an object.</summary>
public sealed class SnapshotCapturedField
{
    public string Name   { get; set; } = "";
    public int    Offset { get; set; }
    public string Type   { get; set; } = "";  // declared property type (e.g. "FloatProperty")
    public string Hex    { get; set; } = "";  // little-endian raw bytes (exact compare)
}

/// <summary>Result of one <c>snapshot_chunk</c> pipe round-trip.</summary>
public sealed class SnapshotChunkResult
{
    public int Total   { get; set; }   // GObjects count
    public int Scanned { get; set; }   // indices iterated (advance offset by this)
    // Phase-0 telemetry (DLL-side, per chunk): the parallel walk+merge and the JSON
    // DOM-build wall-times. Default 0 keeps older DLLs / test stubs back-compatible.
    public long WalkMs      { get; set; }
    public long SerializeMs { get; set; }
    // C#-side split of the old "parse+pipe" bucket, so a capture pinpoints exactly which
    // stage of the JSON round-trip costs the seconds: pipe read of the response line, the
    // "Pipe RX" debug-log of that line, the JsonNode DOM parse, and the model-build loop.
    public long ReadMs      { get; set; }   // _reader.ReadLineAsync (pipe I/O of the response)
    public long RxLogMs     { get; set; }   // the "Pipe RX: {line}" debug log (string build + write)
    public long ParseMs     { get; set; }   // JsonNode.Parse (DOM build from the line)
    public long BuildMs     { get; set; }   // materialising SnapshotCapturedObject[] from the DOM
    // [W1-SNAP-FAULT] A DLL scan worker FAULTED while walking this chunk, so part of its index
    // range was never captured (the DLL still reports the whole window as `Scanned`, so the
    // pager can step past the hole). The capture finalises the snapshot UNUSABLE on it.
    // Absent on an older DLL = false, the pre-fix behaviour.
    public bool WorkerFaulted { get; set; }
    public List<SnapshotCapturedObject> Objects { get; set; } = new();
}
