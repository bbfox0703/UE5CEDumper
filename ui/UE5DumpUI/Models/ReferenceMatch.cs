namespace UE5DumpUI.Models;

/// <summary>
/// One UObject that holds a pointer to a target UObject. Result of the
/// reverse-reference scan — answers "who logically owns this object?"
/// when UE's OuterPrivate (naming hierarchy) is uninformative.
/// </summary>
public sealed class ReferenceMatch
{
    public string OwnerAddress { get; init; } = "";
    /// <summary>[W4-HEXSORT] <see cref="OwnerAddress"/> as a ulong, for the AOT-safe numeric column sort (0 when
    /// empty or unparseable, so those rows sort first) -- the InstanceResult / RelatedObject.AddressValue idiom.</summary>
    public ulong OwnerAddressValue =>
        ulong.TryParse(OwnerAddress.Replace("0x", "", System.StringComparison.OrdinalIgnoreCase),
            System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0UL;
    public int OwnerIndex { get; init; }
    public string OwnerName { get; init; } = "";
    public string OwnerClassName { get; init; } = "";

    /// <summary>Absolute field offset within the owner UObject.</summary>
    public int FieldOffset { get; init; }

    /// <summary>
    /// Dotted field path (e.g. "Stats.Equipment").
    /// Map matches append ".Key" or ".Value" so the side that held the
    /// pointer is visible in the path (e.g. "ItemTable.Value").
    /// </summary>
    public string FieldName { get; init; } = "";

    /// <summary>
    /// The UE property type name of the field that held the reference, as the DLL's scan reports it: any type
    /// that can carry an object reference, a container or wrapper of one included. Not a closed set here: the
    /// scan decides which types it reads and reports each under the engine's own spelling, so the same kind of
    /// reference arrives under an era's name (UE 4.17's asset pointer is the later soft pointer;
    /// [UE4-ASSETPTR-PROPS]). Match on it as an open string, never against a fixed list.
    /// </summary>
    public string FieldType { get; init; } = "";

    /// <summary>
    /// Element type for Array / Set; "<keyType> → <valueType>" for Map.
    /// Empty for direct (non-container) fields.
    /// </summary>
    public string InnerType { get; init; } = "";

    /// <summary>-1 for direct field; >=0 for array/map/set element index
    /// (sparse-array index for Map/Set).</summary>
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
