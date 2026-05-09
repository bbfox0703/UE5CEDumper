namespace UE5DumpUI.Models;

/// <summary>
/// One UObject that holds a pointer to a target UObject. Result of the
/// reverse-reference scan — answers "who logically owns this object?"
/// when UE's OuterPrivate (naming hierarchy) is uninformative.
/// </summary>
public sealed class ReferenceMatch
{
    public string OwnerAddress { get; init; } = "";
    public int OwnerIndex { get; init; }
    public string OwnerName { get; init; } = "";
    public string OwnerClassName { get; init; } = "";

    /// <summary>Absolute field offset within the owner UObject.</summary>
    public int FieldOffset { get; init; }

    /// <summary>Dotted field path (e.g. "Stats.Equipment").</summary>
    public string FieldName { get; init; } = "";

    /// <summary>"ObjectProperty" / "ClassProperty" / "ArrayProperty".</summary>
    public string FieldType { get; init; } = "";

    /// <summary>For arrays: inner element type ("ObjectProperty" / "ClassProperty"). Empty otherwise.</summary>
    public string InnerType { get; init; } = "";

    /// <summary>-1 for direct field; >=0 for array element index.</summary>
    public int ElementIndex { get; init; } = -1;

    /// <summary>Display path: "OwnerName.FieldName" or "OwnerName.FieldName[N]".</summary>
    public string DisplayPath
    {
        get
        {
            var path = $"{OwnerName}.{FieldName}";
            if (ElementIndex >= 0)
                path += $"[{ElementIndex}]";
            return path;
        }
    }
}

/// <summary>
/// Result of a reverse-reference scan.
/// </summary>
public sealed class FindReferencesResult
{
    public string QueryAddress { get; init; } = "";
    public List<ReferenceMatch> References { get; init; } = new();
    public ContainerScanStats? Scan { get; init; }
}
