namespace UE5DumpUI.Models;

/// <summary>
/// A single field value read from a live UObject instance.
/// </summary>
public sealed class LiveFieldValue
{
    public string Name { get; init; } = "";
    public string TypeName { get; init; } = "";
    public int Offset { get; init; }
    public int Size { get; init; }

    /// <summary>Raw hex value (always populated for readable fields).</summary>
    public string HexValue { get; init; } = "";

    /// <summary>Human-readable typed value (for Float, Int, Bool, etc.).</summary>
    public string TypedValue { get; init; } = "";

    /// <summary>For ObjectProperty: address of the referenced object.</summary>
    public string PtrAddress { get; init; } = "";

    /// <summary>For ObjectProperty: name of the pointed-to object.</summary>
    public string PtrName { get; init; } = "";

    /// <summary>For ObjectProperty: class name of the pointed-to object.</summary>
    public string PtrClassName { get; init; } = "";

    /// <summary>For ArrayProperty: element count (-1 = not an array).</summary>
    public int ArrayCount { get; init; } = -1;

    /// <summary>Display-friendly value string.</summary>
    public string DisplayValue =>
        !string.IsNullOrEmpty(TypedValue) ? TypedValue :
        !string.IsNullOrEmpty(PtrName) ? $"{PtrName} ({PtrClassName})" :
        ArrayCount >= 0 ? $"[{ArrayCount} elements]" :
        !string.IsNullOrEmpty(HexValue) ? HexValue :
        "";

    /// <summary>Whether this field is a clickable pointer to another object.</summary>
    public bool IsNavigable =>
        !string.IsNullOrEmpty(PtrAddress) && PtrAddress != "0x0";
}
