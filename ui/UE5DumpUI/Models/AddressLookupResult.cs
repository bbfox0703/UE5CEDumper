namespace UE5DumpUI.Models;

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
}
