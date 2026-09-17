namespace UE5DumpUI.Models;

/// <summary>
/// How a Class Pivot groups a class's instances. UE dissolves the
/// "user must guess a business key" pain that <c>discrete</c> hits with anonymous
/// Unity instances — so identity is a first-class mode, offered before any key
/// field. See docs/experimental-snapshot-spc-pivot.md §"Phase C".
/// </summary>
public enum PivotKeyMode
{
    /// <summary>Group by intrinsic object identity (normalised path). Spawn-
    /// counter-normalised siblings (BP_Enemy_C_0/_1/…) collapse into one group,
    /// so the value cells show the spread across all of them. No key field needed.</summary>
    Identity,
    /// <summary>Group by the value of a chosen key field (e.g. ItemID → group the
    /// inventory by item).</summary>
    Field,
}

/// <summary>One live DataTable instance pickable as a zero-config pivot source
/// (Phase C4). A DataTable is already a RowName→struct map, so it pivots without
/// key discovery. See docs/experimental-snapshot-spc-pivot.md §"Phase C — C4".</summary>
public sealed class DataTablePick
{
    public string Name      { get; }
    public string Address   { get; }
    public string ClassName { get; }

    public DataTablePick(string name, string address, string className)
    {
        Name = name;
        Address = address;
        ClassName = className;
    }

    /// <summary>"DT_Items  [DataTable]" for the picker.</summary>
    public string Display => string.IsNullOrEmpty(ClassName) ? Name : $"{Name}  [{ClassName}]";
}

/// <summary>One class present in a snapshot, with its live-instance count.</summary>
public sealed class PivotClassInfo
{
    public string ClassName    { get; set; } = "";
    public int    InstanceCount { get; set; }
    /// <summary>"BP_Item_C  (1,240)" for the class picker.</summary>
    public string Display => $"{ClassName}  ({InstanceCount:N0})";
}

/// <summary>One numeric field of a pivot class, with cardinality stats used to
/// rank key candidates and suggest value fields.</summary>
public sealed class PivotFieldInfo
{
    public string Name          { get; set; } = "";
    public string DeclaredType  { get; set; } = "";
    /// <summary>Distinct values of this field across the class's instances.</summary>
    public int    DistinctCount { get; set; }
    /// <summary>Instances that carry this field (≈ class instance count).</summary>
    public int    InstanceCount { get; set; }
}

/// <summary>One captured struct-array field of a pivot class (Phase C6), with its
/// inner-key field name and total element count. Drives the array-field picker for
/// array-element pivot. See docs/experimental-snapshot-spc-pivot.md §"Phase C — C6".</summary>
public sealed class PivotArrayFieldInfo
{
    public string ArrayField   { get; set; } = "";
    /// <summary>Inner key field captured for elements (e.g. "ItemID"); "" if none.</summary>
    public string InnerKeyName { get; set; } = "";
    /// <summary>Total captured elements across all instances of the class.</summary>
    public int    ElementCount { get; set; }
    public string Display => string.IsNullOrEmpty(InnerKeyName)
        ? $"{ArrayField}  ({ElementCount:N0})"
        : $"{ArrayField}  [key={InnerKeyName}]  ({ElementCount:N0})";
}

/// <summary>An array-element pivot request (Phase C6): group one class's captured
/// struct-array elements by their inner-key value (reorder- and session-immune),
/// projecting the inner numeric props. Reuses <see cref="PivotEngine"/> in Identity
/// mode with the inner-key value as the group key.</summary>
public sealed class ArrayPivotQuery
{
    public long   SnapshotId { get; set; }
    public string ClassName  { get; set; } = "";
    public string ArrayField { get; set; } = "";
    /// <summary>Inner prop names whose values are projected per inner-key group.</summary>
    public List<string> ValueProps { get; set; } = new();
    public int MaxGroups { get; set; } = Constants.DefaultMaxPivotGroups;
}

/// <summary>One captured (instance, field) row the store hands to the pure
/// <see cref="Services.PivotEngine"/>. Rows are grouped by instance, then by key.</summary>
public sealed class PivotInputRow
{
    public long   ObjectIndex { get; set; }   // gobjects_index (in-session instance id)
    public string NormPath    { get; set; } = "";
    public string ObjAddr     { get; set; } = "";
    public string PropName    { get; set; } = "";
    public int    PropOffset  { get; set; }
    public string DeclaredType { get; set; } = "";
    public string Hex         { get; set; } = "";
}

/// <summary>A Class Pivot request: one snapshot, one class, a key mode, and the
/// value fields to project per group.</summary>
public sealed class PivotQuery
{
    public long   SnapshotId { get; set; }
    public string ClassName  { get; set; } = "";
    public PivotKeyMode KeyMode { get; set; } = PivotKeyMode.Identity;
    /// <summary>Primary key field name (used only when <see cref="KeyMode"/> is Field).
    /// Kept for back-compat / the single-key handoff flows; <see cref="KeyFields"/> is
    /// the full composite key when grouping by a multi-field tuple.</summary>
    public string KeyField   { get; set; } = "";
    /// <summary>Composite group key (Field mode): the primary key field plus any
    /// additional ticked key fields, grouping by the TUPLE of their rendered values.
    /// When empty, <see cref="KeyField"/> is used as the sole key (back-compat). The
    /// rendered segments join with " · "; a single field renders verbatim, so a
    /// one-element list is byte-identical to the legacy single-key path.</summary>
    public List<string> KeyFields { get; set; } = new();
    /// <summary>Fields whose values are projected per group.</summary>
    public List<string> ValueFields { get; set; } = new();
    /// <summary>Max groups returned (default 5,000). One extra is probed to set
    /// <see cref="PivotResult.Truncated"/>.</summary>
    public int MaxGroups { get; set; } = Constants.DefaultMaxPivotGroups;

    /// <summary>The effective key-field list for Field-mode grouping: <see cref="KeyFields"/>
    /// when non-empty, else the single <see cref="KeyField"/>, else empty. Order is
    /// preserved (the tuple order the user picked).</summary>
    public IReadOnlyList<string> EffectiveKeyFields =>
        KeyFields.Count > 0 ? KeyFields
        : (!string.IsNullOrEmpty(KeyField) ? new List<string> { KeyField }
                                           : (IReadOnlyList<string>)System.Array.Empty<string>());
}

/// <summary>One group in a pivot: a distinct key value and the projected values
/// of the instances that share it.</summary>
public sealed class PivotResultRow
{
    public string KeyValue { get; set; } = "";
    /// <summary>Instances in this group.</summary>
    public int    Count    { get; set; }
    /// <summary>A representative instance's live address (first in the group) —
    /// the Open-in-Live-Walker handoff target.</summary>
    public string ObjAddr  { get; set; } = "";
    /// <summary>Projected values rendered as "field=cell", joined. A cell for a
    /// group with differing values renders as a collision ⟨N: v1,v2,+M⟩.</summary>
    public string ValuesDisplay { get; set; } = "";
}

/// <summary>Result of a Class Pivot: grouped rows + stats.</summary>
public sealed class PivotResult
{
    public List<PivotResultRow> Rows { get; } = new();
    public int  GroupCount    { get; set; }
    public int  InstanceCount { get; set; }
    public bool Truncated     { get; set; }
    /// <summary>[P5-PIVOT-FETCHCAP] The row FETCH cap SnapshotStore stopped at, 0 when it did not. A different fact from
    /// <see cref="Truncated"/> (the GROUP cap over a complete input): the pivot was built over a PREFIX, so every count
    /// is an undercount.</summary>
    public int  FetchCap      { get; set; }
    public bool FetchCapped   => FetchCap > 0;
}
