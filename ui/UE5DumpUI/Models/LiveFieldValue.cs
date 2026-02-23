using System.Linq;

namespace UE5DumpUI.Models;

/// <summary>
/// A sub-field value within a struct array element (Phase F).
/// </summary>
public sealed class StructSubFieldValue
{
    public string Name { get; init; } = "";
    public string TypeName { get; init; } = "";
    public int Offset { get; init; }
    public int Size { get; init; }
    public string Value { get; init; } = "";
}

/// <summary>
/// A single element value from an array (Phase B scalar / Phase D pointer / Phase F struct).
/// </summary>
public sealed class ArrayElementValue
{
    public int Index { get; init; }
    public string Value { get; init; } = "";
    public string Hex { get; init; } = "";
    public string EnumName { get; init; } = "";
    // Phase D: pointer array fields
    public string PtrAddress { get; init; } = "";
    public string PtrName { get; init; } = "";
    public string PtrClassName { get; init; } = "";
    // Phase F: struct sub-fields
    public List<StructSubFieldValue>? StructFields { get; init; }
}

/// <summary>
/// Result of reading array elements via read_array_elements command.
/// </summary>
public sealed class ArrayElementsResult
{
    public int TotalCount { get; init; }
    public int ReadCount { get; init; }
    public string InnerType { get; init; } = "";
    public int ElemSize { get; init; }
    public List<ArrayElementValue> Elements { get; init; } = new();
}

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

    /// <summary>For ArrayProperty: inner element type name (e.g., "FloatProperty", "StructProperty").</summary>
    public string ArrayInnerType { get; init; } = "";

    /// <summary>For ArrayProperty (struct arrays): UScriptStruct name (e.g., "FVector").</summary>
    public string ArrayStructType { get; init; } = "";

    /// <summary>For ArrayProperty: element size in bytes.</summary>
    public int ArrayElemSize { get; init; }

    /// <summary>For ArrayProperty: Inner FProperty* address (for read_array_elements command).</summary>
    public string ArrayInnerAddr { get; init; } = "";

    /// <summary>For ArrayProperty Phase B: inline scalar element values (up to 64).</summary>
    public List<ArrayElementValue>? ArrayElements { get; init; }

    /// <summary>For StructProperty: absolute address of struct data (instance + offset).</summary>
    public string StructDataAddr { get; init; } = "";

    /// <summary>For StructProperty: UScriptStruct* address for the struct type.</summary>
    public string StructClassAddr { get; init; } = "";

    /// <summary>For StructProperty: struct type name (e.g. "FGameplayAttributeData").</summary>
    public string StructTypeName { get; init; } = "";

    /// <summary>For EnumProperty: resolved enum name (e.g., "ROLE_Authority").</summary>
    public string EnumName { get; init; } = "";

    /// <summary>For EnumProperty: raw enum integer value.</summary>
    public long EnumValue { get; init; }

    /// <summary>For StrProperty: decoded UTF-8 string value.</summary>
    public string StrValue { get; init; } = "";

    /// <summary>Display-friendly value string.</summary>
    public string DisplayValue =>
        !string.IsNullOrEmpty(TypedValue) ? TypedValue :
        !string.IsNullOrEmpty(PtrName) ? $"{PtrName} ({PtrClassName})" :
        !string.IsNullOrEmpty(StructTypeName) ? $"{{{StructTypeName}}}" :
        ArrayCount >= 0 && !string.IsNullOrEmpty(ArrayInnerType)
            ? FormatArrayDisplay()
            : ArrayCount >= 0 ? $"[{ArrayCount} elements]" :
        !string.IsNullOrEmpty(StrValue) ? $"\"{StrValue}\"" :
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

    private string FormatArrayDisplay()
    {
        var typeLabel = !string.IsNullOrEmpty(ArrayStructType) ? ArrayStructType : ArrayInnerType;
        var header = $"[{ArrayCount} x {typeLabel} ({ArrayElemSize}B)]";

        if (ArrayElements == null || ArrayElements.Count == 0)
            return header;

        const int previewCount = 5;
        var preview = ArrayElements
            .Take(previewCount)
            .Select(e =>
                !string.IsNullOrEmpty(e.EnumName) ? e.EnumName :
                !string.IsNullOrEmpty(e.PtrName) ? (
                    !string.IsNullOrEmpty(e.PtrClassName)
                        ? $"{e.PtrName} ({e.PtrClassName})"
                        : e.PtrName
                ) :
                e.Value);
        var joined = string.Join(", ", preview);

        if (ArrayCount > previewCount)
            joined += ", ...";

        return $"{header} = [{joined}]";
    }
}
