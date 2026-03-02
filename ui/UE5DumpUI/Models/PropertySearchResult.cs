namespace UE5DumpUI.Models;

/// <summary>
/// A single property match from the search_properties command.
/// </summary>
public class PropertySearchMatch
{
    public string ClassName { get; set; } = "";
    public string ClassAddr { get; set; } = "";
    public string ClassPath { get; set; } = "";
    public string SuperName { get; set; } = "";
    public string PropName { get; set; } = "";
    public string PropType { get; set; } = "";
    public int PropOffset { get; set; }
    public int PropSize { get; set; }
    public string StructType { get; set; } = "";
    public string InnerType { get; set; } = "";
    public string Preview { get; set; } = "";

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
}

/// <summary>
/// Result set from the search_properties command.
/// </summary>
public class PropertySearchResult
{
    public int Total { get; set; }
    public int ScannedClasses { get; set; }
    public int ScannedObjects { get; set; }
    public List<PropertySearchMatch> Results { get; set; } = new();
}
