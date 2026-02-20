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

    /// <summary>For BoolProperty: bit index (0-7) within the byte; -1 = not a bool.</summary>
    public int BoolBitIndex { get; init; } = -1;

    /// <summary>For BoolProperty: raw FieldMask byte.</summary>
    public int BoolFieldMask { get; init; }

    /// <summary>For ArrayProperty: element count (-1 = not an array).</summary>
    public int ArrayCount { get; init; } = -1;

    /// <summary>For StructProperty: absolute address of struct data (instance + offset).</summary>
    public string StructDataAddr { get; init; } = "";

    /// <summary>For StructProperty: UScriptStruct* address for the struct type.</summary>
    public string StructClassAddr { get; init; } = "";

    /// <summary>For StructProperty: struct type name (e.g. "FGameplayAttributeData").</summary>
    public string StructTypeName { get; init; } = "";

    /// <summary>Display-friendly value string.</summary>
    public string DisplayValue =>
        !string.IsNullOrEmpty(TypedValue) ? TypedValue :
        !string.IsNullOrEmpty(PtrName) ? $"{PtrName} ({PtrClassName})" :
        !string.IsNullOrEmpty(StructTypeName) ? $"{{{StructTypeName}}}" :
        ArrayCount >= 0 ? $"[{ArrayCount} elements]" :
        !string.IsNullOrEmpty(HexValue) ? HexValue :
        "";

    /// <summary>Whether this field is a clickable pointer to another object.</summary>
    public bool IsNavigable =>
        (!string.IsNullOrEmpty(PtrAddress) && PtrAddress != "0x0") ||
        (!string.IsNullOrEmpty(StructDataAddr) && StructDataAddr != "0x0");

    /// <summary>Whether this field is a pointer navigation (true) or struct navigation (false).</summary>
    public bool IsPointerNavigation =>
        !string.IsNullOrEmpty(PtrAddress) && PtrAddress != "0x0";

    /// <summary>Whether this field is a struct-inline navigation.</summary>
    public bool IsStructNavigation =>
        !IsPointerNavigation && !string.IsNullOrEmpty(StructDataAddr) && StructDataAddr != "0x0";

    /// <summary>Absolute memory address of this field (instance base + offset). Set by ViewModel.</summary>
    public string FieldAddress { get; set; } = "";

    /// <summary>Whether this field matches the current search query (set by ViewModel).</summary>
    public bool IsSearchMatch { get; set; }
}
