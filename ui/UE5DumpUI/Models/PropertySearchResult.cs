using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UE5DumpUI.Models;

/// <summary>
/// A single property match from the search_properties command.
///
/// Build 610+: results are deduped by (definingClass, propName, offset)
/// so a field declared on AActor and inherited by 4823 children only
/// emits one row keyed by the defining class. <see cref="ClassName"/>
/// and <see cref="DefiningClassName"/> will be the same value after
/// dedup; both are exposed to keep wire forward-compat in case the
/// dedup story changes (e.g. a future "Show inheritance expanded"
/// toggle could emit one row per inheriting class).
/// </summary>
public partial class PropertySearchMatch : ObservableObject
{
    public string ClassName { get; set; } = "";
    public string ClassAddr { get; set; } = "";
    public string ClassPath { get; set; } = "";

    /// <summary>
    /// FProperty* address (UProperty* on UE4 &lt;4.25) — the key for
    /// find_property_xrefs ("which methods use this field?"). Emitted by
    /// search_properties / search_properties_batch since build 842.
    /// </summary>
    public string FieldAddr { get; set; } = "";
    public string SuperName { get; set; } = "";
    public string PropName { get; set; } = "";
    public string PropType { get; set; } = "";
    public int PropOffset { get; set; }
    public int PropSize { get; set; }
    public string StructType { get; set; } = "";
    public string InnerType { get; set; } = "";
    public string Preview { get; set; } = "";

    /// <summary>CPF_* reflection flags (SaveGame / BlueprintVisible /
    /// EditorOnly / …). 0 when absent (older DLL or all-default field).
    /// Feeds the auto-detect scorer's PropertyFlags gating.</summary>
    public ulong PropertyFlags { get; set; }

    // === Inheritance-aware fields (build 610+) ===
    public string DefiningClassName { get; set; } = "";
    public string DefiningClassAddr { get; set; } = "";
    public string DefiningClassPath { get; set; } = "";
    /// <summary>Number of OTHER classes (excludes the defining class
    /// itself) that inherit this field at the same offset. 0 means
    /// the property is unique to this class -- often a strong
    /// signal that it's a game-specific addition rather than an
    /// engine inherited field.</summary>
    public int InheritedByCount { get; set; }

    /// <summary>
    /// True when this row is a synthetic dotted-path leaf found by the
    /// opt-in deep descent (build 1222) into nested struct members + struct-
    /// typed container elements. For these <see cref="PropName"/> is a dotted
    /// path (e.g. "SaveSlotList[].MsTuneData.GP"), <see cref="ClassName"/> is
    /// the OWNING class (so Find Instances works), and <see cref="FieldAddr"/>
    /// is the leaf FProperty* (so Find Funcs works). There is no single
    /// class-absolute address, so Copy Offset / Freeze are hidden for these
    /// rows (see <see cref="ShowScalarActions"/>).
    /// </summary>
    public bool IsNested { get; set; }

    /// <summary>
    /// Gates the row's Copy Offset + Freeze buttons. Nested (deep) matches
    /// have a dotted path rather than a class-absolute offset, so those two
    /// actions don't apply — only finder (locate live instances of the
    /// owning class) + Find Funcs (xref the leaf FProperty) make sense.
    /// </summary>
    public bool ShowScalarActions => !IsNested;

    // === Force-field (Solide) per-row action gates ===
    // A direct (non-nested) row can be held by the DLL force-and-hold worker.
    // The kind is decided by the reflected property type.

    /// <summary>Row is a BoolProperty → "Force ON / OFF" applies.</summary>
    public bool CanForceBool => ShowScalarActions && PropType == "BoolProperty";

    /// <summary>Row is a strong ObjectProperty → "Force → null" applies (weak/soft
    /// object ptrs are intentionally excluded — nulling their index hits GObjects[0]).</summary>
    public bool CanForceNull => ShowScalarActions && PropType == "ObjectProperty";

    /// <summary>Row is a DLL-supported numeric type → "Force value…" applies.</summary>
    public bool CanForceNumeric => ShowScalarActions && PropType is
        "FloatProperty" or "DoubleProperty" or "IntProperty" or "Int64Property"
        or "ByteProperty" or "UInt8Property" or "Int8Property";

    /// <summary>Any Force action applies to this row (gates the context submenu).</summary>
    public bool CanForceAny => CanForceBool || CanForceNull || CanForceNumeric;

    /// <summary>
    /// Tooltip for the Property column. Empty for a normal direct field;
    /// for a nested (deep) match it explains the dotted path is a drill
    /// route, not a directly-addressable field, and points at how to reach
    /// a live value.
    /// </summary>
    public string? PropNameTooltip => IsNested
        ? $"Nested field reached via {PropName} on {ClassName}.\n" +
          "This path crosses struct/container members, so it has no single " +
          "class-absolute address. Use finder to list instances of the owning " +
          "class, then Value Search (by value) or Live Walker (drill the path) " +
          "to reach a live value."
        : null;  // null => no tooltip popup on plain direct-field rows

    /// <summary>Display-friendly offset as hex.</summary>
    public string OffsetHex => $"0x{PropOffset:X}";

    /// <summary>Combined type display (e.g. "StructProperty (FVector)" or "ArrayProperty [ObjectProperty]").</summary>
    public string TypeDisplay
    {
        get
        {
            if (!string.IsNullOrEmpty(StructType))
                return $"{PropType} ({StructType})";
            if (!string.IsNullOrEmpty(InnerType))
                return $"{PropType} [{InnerType}]";
            return PropType;
        }
    }

    /// <summary>
    /// Compact inheritance hint shown next to ClassName in the DataGrid.
    /// "(unique)" when only one class has this field; "+N inherited"
    /// for a field shared with N children. Empty when count == 0 and
    /// we want the column to stay clean.
    /// </summary>
    public string InheritanceBadge => InheritedByCount switch
    {
        0   => "",            // unique to this class -- no badge needed
        1   => "+1 inheritor",
        _   => $"+{InheritedByCount} inheritors",
    };

    /// <summary>
    /// Tooltip explaining the inheritance relationship -- shows the
    /// defining class path so the user can see whether it's an engine
    /// (/Script/Engine.*) or game (/Game/* /Script/MyGame.*) field.
    /// </summary>
    public string InheritanceTooltip => InheritedByCount == 0
        ? $"This property is unique to {ClassName} -- likely a " +
          $"game-specific field rather than an engine inheritance.\n" +
          $"Path: {ClassPath}"
        : $"Defined on {DefiningClassName} (at offset {OffsetHex}); " +
          $"inherited by {InheritedByCount} subclass(es). Writing to " +
          $"this offset on any instance of {DefiningClassName} (or any " +
          $"subclass) has identical effect.\n" +
          $"Path: {DefiningClassPath}";

    /// <summary>Batch "Find Funcs" result: which UFunctions reference this
    /// property. Format "N · func1, func2[, …]" / "0" / "—" / "" (not run).</summary>
    [ObservableProperty] private string _xrefInfo = "";
}

/// <summary>
/// Result set from the search_properties command.
/// </summary>
public class PropertySearchResult
{
    public int Total { get; set; }
    public int ScannedClasses { get; set; }
    /// <summary>Objects the DLL actually walked — NOT the pool size. Before build
    /// 2818 this carried the full GObjects count regardless of where the walk
    /// stopped, so a capped search claimed a complete sweep. (audit #5 D5/F4)</summary>
    public int ScannedObjects { get; set; }
    /// <summary>The DLL stopped at the result cap: more matches exist beyond this set.
    /// A client-side filter over these rows is therefore filtering a PAGE.</summary>
    public bool Truncated { get; set; }
    /// <summary>The walk was cancelled (client gone / shutdown) — the set is partial
    /// for a different reason than <see cref="Truncated"/>.</summary>
    public bool Aborted { get; set; }
    public List<PropertySearchMatch> Results { get; set; } = new();
}

/// <summary>
/// Per-query envelope inside a <see cref="PropertySearchBatchResult"/>.
/// Mirrors the DLL-side `per_query[i]` shape.
/// </summary>
public class PropertySearchQueryEnvelope
{
    public string Query { get; set; } = "";
    public int MatchCount { get; set; }
    /// <summary>
    /// This query stopped at the per-query cap: more matches exist for it. Per-query and not
    /// per-batch, because the shared walk only stops once EVERY query is full — one seed keyword
    /// can be capped while another swept the whole pool.
    /// <para>
    /// The DLL has emitted this since the D5/F4 fix; the client parsed it on the single-query path
    /// and not here, so the two discovery panels presented a capped page as the whole pool — the
    /// exact report class F4 was written to end (audit #5 X1).
    /// </para>
    /// </summary>
    public bool Truncated { get; set; }
    public List<PropertySearchMatch> Results { get; set; } = new();
}

/// <summary>
/// Result set from the search_properties_batch command. Walks GObjects
/// + class fields ONCE for N queries — see DLL-side SearchPropertiesBatch
/// for the speedup rationale (~30x on a 36-query / 4400-class game).
/// Order of <see cref="PerQuery"/> matches the input queries[] order;
/// callers can therefore index by position or by matching the
/// envelope's <see cref="PropertySearchQueryEnvelope.Query"/> field.
/// </summary>
public class PropertySearchBatchResult
{
    public int QueryCount { get; set; }
    public int Total { get; set; }
    public int ScannedClasses { get; set; }
    public int ScannedObjects { get; set; }
    /// <summary>The shared walk was cancelled (client gone / shutdown), so every query's set is
    /// partial for a different reason than <see cref="PropertySearchQueryEnvelope.Truncated"/>.</summary>
    public bool Aborted { get; set; }
    public List<PropertySearchQueryEnvelope> PerQuery { get; set; } = new();

    /// <summary>Queries that hit their cap. Empty when the sweep was complete.</summary>
    public List<string> TruncatedQueries =>
        PerQuery.Where(q => q.Truncated).Select(q => q.Query).ToList();
}
