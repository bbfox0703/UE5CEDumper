namespace UE5DumpUI.Models;

/// <summary>
/// One hop of a computed GWorld → target pointer path: parent --edge--> child.
/// The inverse of <see cref="ReferenceMatch"/> — instead of "who points at this
/// object", it records "the field we followed to get one step closer to the
/// target".
/// </summary>
public sealed class GWorldPathStep
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";

    /// <summary>Byte offset of the field within the parent UObject.</summary>
    public int FieldOffset { get; init; }

    /// <summary>Property name (Map matches append ".Key"/".Value").</summary>
    public string FieldName { get; init; } = "";

    /// <summary>"ObjectProperty" / "ArrayProperty" / "MapProperty" / "SetProperty" / ...</summary>
    public string FieldType { get; init; } = "";

    /// <summary>Container element type ("" for direct fields).</summary>
    public string InnerType { get; init; } = "";

    /// <summary>-1 for a direct field; >=0 for an array/map/set element.</summary>
    public int ElementIndex { get; init; } = -1;

    /// <summary>Element stride in the container's Data buffer (Map pairStride / Set stride /
    /// interface-array 16-byte slot), which splits a container element hop into container +
    /// element CE derefs. 0 = not carried (object/class arrays use the implicit 8-byte pointer
    /// stride, hardcoded by the UI).</summary>
    public int ElemStride { get; init; }

    /// <summary>Within-element offset of the followed pointer — the Map value's offset within
    /// its key/value pair. 0 for arrays, sets, and map keys.</summary>
    public int ElemValueOffset { get; init; }

    /// <summary>Resolved name of the child object.</summary>
    public string ToName { get; init; } = "";

    /// <summary>Resolved class name of the child object.</summary>
    public string ToClass { get; init; } = "";
}

/// <summary>
/// Result of the forward object-graph path search ("Locate in GWorld"): the
/// shortest pointer chain from the live UWorld down to a target object. When
/// <see cref="Found"/> is false, <see cref="Status"/> explains why
/// ("not_reachable" / "deadline" / "cancelled" / "no_gworld" /
/// "invalid_target" / "visited_cap"). When found, <see cref="Status"/> is "ok",
/// or "ok_via_level" when the actor was recovered through the world's level list
/// (a streaming / World-Partition actor whose ULevel isn't forward-reachable —
/// the world→level hop is a back-reference, not a static pointer).
/// </summary>
public sealed class GWorldPathResult
{
    public bool Found { get; init; }
    public string Status { get; init; } = "";

    /// <summary>The resolved UWorld root the path starts from.</summary>
    public string RootAddr { get; init; } = "";
    public string RootName { get; init; } = "";

    /// <summary>The UObject reached (== the owning object for a value target).</summary>
    public string TargetObj { get; init; } = "";
    public string TargetName { get; init; } = "";
    public string TargetClass { get; init; } = "";

    /// <summary>(target value address - TargetObj), >0 when the target was a value
    /// inside the object's inline property block.</summary>
    public int TargetIntraOffset { get; init; }

    public int MaxDepth { get; init; }

    /// <summary>Number of hops in the returned path (== Steps.Count).</summary>
    public int Depth { get; init; }

    /// <summary>Distinct objects discovered during the search.</summary>
    public int Visited { get; init; }
    public long DurationMs { get; init; }

    /// <summary>Edges root → Steps[0].To → … → TargetObj.</summary>
    public List<GWorldPathStep> Steps { get; init; } = new();
}
