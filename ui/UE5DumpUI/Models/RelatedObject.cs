namespace UE5DumpUI.Models;

/// <summary>
/// One object related to a queried UObject — itself, its class/outer, its
/// Controller↔Pawn counterpart, or a sub-object it owns (component, GAS
/// AbilitySystemComponent, AttributeSet). Result of the forward owned-graph scan
/// (<c>get_related_objects</c>). The reverse "who points AT this object" view is
/// <see cref="ReferenceMatch"/>.
/// </summary>
public sealed class RelatedObject
{
    public string Address { get; init; } = "";

    /// <summary>InternalIndex in GObjects; -1 when unreadable / off-array.</summary>
    public int Index { get; init; } = -1;

    public string Name { get; init; } = "";
    public string ClassName { get; init; } = "";

    /// <summary>
    /// Relationship to the queried object: "Self" / "Class" / "Outer" /
    /// "Controller" / "Pawn" / "AbilitySystem (ASC)" / "AttributeSet" /
    /// "Owned Component" / "Owned Object".
    /// </summary>
    public string Relation { get; init; } = "";

    /// <summary>Field/path on <see cref="ParentAddress"/> that points here
    /// (empty for Self/Class/Outer).</summary>
    public string FieldName { get; init; } = "";

    /// <summary>Offset within the parent object (CE / GWorld handoff); -1 when N/A.</summary>
    public int FieldOffset { get; init; } = -1;

    /// <summary>0 = hierarchy/counterpart; 1..2 = owned-BFS depth.</summary>
    public int Depth { get; init; }

    /// <summary>Object that holds the pointer to this one.</summary>
    public string ParentAddress { get; init; } = "";

    /// <summary>Display label: "Name : ClassName" (or whichever is non-empty).</summary>
    public string Display =>
        string.IsNullOrEmpty(Name) ? ClassName
        : string.IsNullOrEmpty(ClassName) ? Name
        : $"{Name} : {ClassName}";

    /// <summary>The field/path on the parent, with offset hint when available.
    /// Empty for Self/Class/Outer rows.</summary>
    public string FieldDisplay =>
        string.IsNullOrEmpty(FieldName) ? ""
        : FieldOffset >= 0 ? $"{FieldName} @ 0x{FieldOffset:X}"
        : FieldName;

    /// <summary>Address as ulong for AOT-safe DataGrid hex sorting (0 on parse failure).</summary>
    public ulong AddressValue =>
        ulong.TryParse(Address.Replace("0x", "", System.StringComparison.OrdinalIgnoreCase),
            System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0UL;
}

/// <summary>Result of a forward related-object scan (<c>get_related_objects</c>).</summary>
public sealed class RelatedObjectsResult
{
    public string QueryAddress { get; init; } = "";
    public List<RelatedObject> Related { get; init; } = new();

    /// <summary>[W4-RELATED-STOPS] Why the list stopped short; null from a DLL that did not say.</summary>
    public RelatedObjectsStops? Stops { get; init; }
}

/// <summary>
/// [W4-RELATED-STOPS] Why a related-object walk stopped short -- one flag per cause, never one "stopped early"
/// bool, because each needs its own advice. A cap flag means an object was actually refused, so more EXIST;
/// the pointer budget, the deadline and a cancel mean more MAY exist.
/// </summary>
public sealed class RelatedObjectsStops
{
    public bool ResultCapHit { get; init; }
    public bool OwnedCapHit  { get; init; }
    public bool VisitCapHit  { get; init; }
    public bool DeadlineHit  { get; init; }
    public bool Cancelled    { get; init; }
    public int  MaxResults   { get; init; }
    public int  MaxOwned     { get; init; }
    public long MaxVisited   { get; init; }
    public long DeadlineMs   { get; init; }
}
