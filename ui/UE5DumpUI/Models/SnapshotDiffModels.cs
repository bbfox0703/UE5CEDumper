namespace UE5DumpUI.Models;

/// <summary>Direction of a numeric change between two snapshots.</summary>
public enum SnapshotDiffDirection { None, Up, Down }

/// <summary>Filter for a snapshot diff query.</summary>
public sealed class SnapshotDiffFilter
{
    /// <summary>Case-insensitive substring on the class name (empty = all).</summary>
    public string ClassContains { get; set; } = "";
    /// <summary>Case-insensitive substring on the property name (empty = all).</summary>
    public string PropContains  { get; set; } = "";
    /// <summary>Only changes in this direction (None = any).</summary>
    public SnapshotDiffDirection Direction { get; set; } = SnapshotDiffDirection.None;
    /// <summary>Max changed rows returned (default 50,000). One extra is probed
    /// to set <see cref="SnapshotDiffResult.Truncated"/>.</summary>
    public int MaxRows { get; set; } = Constants.DefaultMaxQueryRows;
    /// <summary>Also count Added / Removed fields (object set churn). Off skips
    /// the two NOT EXISTS passes.</summary>
    public bool IncludeAddedRemoved { get; set; } = true;

    /// <summary>Per-game class denylist. Class FQNs in this set are skipped
    /// during both the A and B load passes. Null / empty = no filtering. Ordinal
    /// comparison. Note: Added/Removed counts also respect the denylist so the
    /// churn numbers reflect the visible result set.</summary>
    public HashSet<string>? ExcludedClasses { get; set; }
}

/// <summary>One field whose value changed between snapshot A (old) and B (new).
/// Joined by (class, GObjects index, property) — in-session identity.</summary>
public sealed class SnapshotDiffRow
{
    public string ClassName    { get; set; } = "";
    public string NormPath     { get; set; } = "";   // object identity (display)
    public int    ObjectIndex  { get; set; } = -1;
    public string PropName     { get; set; } = "";
    public int    PropOffset   { get; set; }
    public string DeclaredType { get; set; } = "";
    public string ObjAddr      { get; set; } = "";   // session-local (snapshot B) for CE export
    public string OldValue     { get; set; } = "";   // rendered
    public string NewValue     { get; set; } = "";   // rendered
    public SnapshotDiffDirection Direction { get; set; }

    public string DirectionGlyph => Direction switch
    {
        SnapshotDiffDirection.Up   => "▲",
        SnapshotDiffDirection.Down => "▼",
        _                          => "",
    };
}

/// <summary>Result of diffing two snapshots: changed rows + churn counts.</summary>
public sealed class SnapshotDiffResult
{
    public List<SnapshotDiffRow> Changed { get; } = new();
    /// <summary>Fields present in B but not A (spawned objects / new fields).</summary>
    public int  AddedCount    { get; set; }
    /// <summary>Fields present in A but not B (despawned objects).</summary>
    public int  RemovedCount  { get; set; }
    /// <summary>True when the changed-row cap was hit.</summary>
    public bool Truncated     { get; set; }
    /// <summary>Top-N classes by hit count across <see cref="Changed"/> (noise
    /// picker). Sorted desc by count. Classes already in
    /// <see cref="SnapshotDiffFilter.ExcludedClasses"/> are NOT included here.</summary>
    public List<ClassNoiseRow> TopContributors { get; } = new();
}
