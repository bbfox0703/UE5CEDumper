namespace UE5DumpUI.Models;

/// <summary>
/// One container-aware match: the address falls inside a UObject's
/// ArrayProperty heap buffer (TArray::Data), pinpointing element index
/// and intra-element offset.
/// </summary>
public sealed class ContainerMatch
{
    public string OwnerAddress { get; init; } = "";
    public int OwnerIndex { get; init; }
    public string OwnerName { get; init; } = "";
    public string OwnerClassName { get; init; } = "";

    /// <summary>Field offset within the owner UObject.</summary>
    public int FieldOffset { get; init; }
    public string FieldName { get; init; } = "";

    /// <summary>"ArrayProperty" (Map/Set future).</summary>
    public string FieldType { get; init; } = "";

    /// <summary>Inner element FProperty type (e.g., "ObjectProperty", "StructProperty").</summary>
    public string InnerType { get; init; } = "";

    public int ElementIndex { get; init; }
    public int ElementSize { get; init; }

    /// <summary>Byte offset within the element (addr - elementStart).</summary>
    public int IntraOffset { get; init; }

    /// <summary>TArray::Data base address (where the element buffer lives).</summary>
    public string DataAddress { get; init; } = "";

    /// <summary>TArray::Count (logical element count).</summary>
    public int Count { get; init; }

    /// <summary>Display path: "OwnerName.FieldName[N]+0xK".</summary>
    public string DisplayPath
    {
        get
        {
            var path = $"{OwnerName}.{FieldName}[{ElementIndex}]";
            if (IntraOffset > 0)
                path += $"+0x{IntraOffset:X}";
            return path;
        }
    }
}

/// <summary>
/// Result of a reverse address lookup — given an arbitrary address,
/// find which UObject (if any) it belongs to.
/// </summary>
public sealed class AddressLookupResult
{
    /// <summary>Whether a matching UObject was found.</summary>
    public bool Found { get; init; }

    /// <summary>"exact" if the address is a UObject itself, "contains" if it falls inside one.</summary>
    public string MatchType { get; init; } = "";

    /// <summary>
    /// More precise confidence kind: "exact" / "contains" / "backward" / "nearest".
    /// "nearest" is a low-confidence fallback (addr is BEYOND PropertiesSize of the
    /// closest UObject) and should be presented as a hint rather than containment.
    /// Empty for older DLLs.
    /// </summary>
    public string MatchKind { get; init; } = "";

    /// <summary>True when the match is a real containment / exact (high confidence).</summary>
    public bool IsHighConfidence => MatchKind is "exact" or "contains";

    /// <summary>The owning UObject address.</summary>
    public string Address { get; init; } = "";

    /// <summary>InternalIndex in GObjects.</summary>
    public int Index { get; init; }

    /// <summary>Object name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Class name of the object.</summary>
    public string ClassName { get; init; } = "";

    /// <summary>Outer object address.</summary>
    public string OuterAddr { get; init; } = "";

    /// <summary>Byte offset from object base (0 for exact match).</summary>
    public int OffsetFromBase { get; init; }

    /// <summary>The original query address.</summary>
    public string QueryAddress { get; init; } = "";

    /// <summary>
    /// Container-aware matches — when the address falls inside a UObject's
    /// ArrayProperty heap buffer rather than within the UObject itself.
    /// Empty list when the standard match was sufficient and container
    /// scan was not requested / produced no hits.
    /// </summary>
    public List<ContainerMatch> ContainerMatches { get; init; } = new();
}
