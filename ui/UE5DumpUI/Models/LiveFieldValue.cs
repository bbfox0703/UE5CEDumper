using System.Linq;
using UE5DumpUI.Core;

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
    // Pointer resolution for ObjectProperty/ClassProperty sub-fields
    public string PtrAddress { get; init; } = "";
    public string PtrName { get; init; } = "";
    public string PtrClassName { get; init; } = "";
    public string PtrClassAddr { get; init; } = "";
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
    /// <summary>Raw integer value for CE DropDownList (enum value or FName ComparisonIndex).</summary>
    public long RawIntValue { get; init; }
    // Phase D: pointer array fields
    public string PtrAddress { get; init; } = "";
    public string PtrName { get; init; } = "";
    public string PtrClassName { get; init; } = "";
    // Phase F: struct sub-fields
    public List<StructSubFieldValue>? StructFields { get; init; }
}

/// <summary>
/// A single element from a Map or Set container.
/// </summary>
public sealed class ContainerElementValue
{
    public int Index { get; init; }
    /// <summary>Map: formatted key; Set: formatted element.</summary>
    public string Key { get; init; } = "";
    /// <summary>Map: formatted value; Set: unused.</summary>
    public string Value { get; init; } = "";
    public string KeyHex { get; init; } = "";
    public string ValueHex { get; init; } = "";
    /// <summary>For pointer keys: resolved name.</summary>
    public string KeyPtrName { get; init; } = "";
    /// <summary>For pointer keys: UObject* address (hex string).</summary>
    public string KeyPtrAddress { get; init; } = "";
    /// <summary>For pointer keys: class name of the pointed-to object.</summary>
    public string KeyPtrClassName { get; init; } = "";
    /// <summary>For pointer values: resolved name.</summary>
    public string ValuePtrName { get; init; } = "";
    /// <summary>For pointer values: UObject* address (hex string).</summary>
    public string ValuePtrAddress { get; init; } = "";
    /// <summary>For pointer values: class name of the pointed-to object.</summary>
    public string ValuePtrClassName { get; init; } = "";
}

/// <summary>
/// A single enum entry (value + name) for CE DropDownList.
/// </summary>
public sealed class EnumEntryValue
{
    public long Value { get; init; }
    public string Name { get; init; } = "";
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

    /// <summary>True if this field was heuristically guessed (not from UE reflection).</summary>
    public bool IsGuessed { get; init; }

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

    /// <summary>For ObjectProperty: UClass* address of the pointed-to object (for CSX drilldown).</summary>
    public string PtrClassAddr { get; init; } = "";

    /// <summary>For BoolProperty: bit index (0-7) within the byte; -1 = not a bool.</summary>
    public int BoolBitIndex { get; init; } = -1;

    /// <summary>For BoolProperty: raw FieldMask byte.</summary>
    public int BoolFieldMask { get; init; }

    /// <summary>For BoolProperty: byte offset within the field for bitfield reads/writes.</summary>
    public int BoolByteOffset { get; init; }

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

    /// <summary>For ArrayProperty: TArray::Data base address (for computing element addresses in container view).</summary>
    public string ArrayDataAddr { get; init; } = "";

    /// <summary>For ArrayProperty (struct arrays): UScriptStruct* address for struct element navigation.</summary>
    public string ArrayStructClassAddr { get; init; } = "";

    /// <summary>For ArrayProperty Phase B: inline scalar element values (up to 64).</summary>
    public List<ArrayElementValue>? ArrayElements { get; init; }

    /// <summary>For ArrayProperty (enum/byte-with-enum): UEnum* address for CE DropDownList sharing.</summary>
    public string ArrayEnumAddr { get; init; } = "";

    /// <summary>For ArrayProperty (enum/byte-with-enum): full UEnum entries for CE DropDownList.</summary>
    public List<EnumEntryValue>? ArrayEnumEntries { get; init; }

    /// <summary>For MapProperty: entry count (-1 = not a map).</summary>
    public int MapCount { get; init; } = -1;

    /// <summary>For MapProperty: key type name (e.g. "StrProperty").</summary>
    public string MapKeyType { get; init; } = "";

    /// <summary>For MapProperty: value type name (e.g. "IntProperty").</summary>
    public string MapValueType { get; init; } = "";

    /// <summary>For MapProperty: key element size in bytes.</summary>
    public int MapKeySize { get; init; }

    /// <summary>For MapProperty: value element size in bytes.</summary>
    public int MapValueSize { get; init; }

    /// <summary>For MapProperty: TSparseArray::Data base address.</summary>
    public string MapDataAddr { get; init; } = "";

    /// <summary>For MapProperty: UScriptStruct* if key is StructProperty.</summary>
    public string MapKeyStructAddr { get; init; } = "";

    /// <summary>For MapProperty: struct name for key (e.g. "FVector").</summary>
    public string MapKeyStructType { get; init; } = "";

    /// <summary>For MapProperty: UScriptStruct* if value is StructProperty.</summary>
    public string MapValueStructAddr { get; init; } = "";

    /// <summary>For MapProperty: struct name for value.</summary>
    public string MapValueStructType { get; init; } = "";

    /// <summary>For MapProperty: inline element preview.</summary>
    public List<ContainerElementValue>? MapElements { get; init; }

    /// <summary>For SetProperty: entry count (-1 = not a set).</summary>
    public int SetCount { get; init; } = -1;

    /// <summary>For SetProperty: element type name.</summary>
    public string SetElemType { get; init; } = "";

    /// <summary>For SetProperty: element size in bytes.</summary>
    public int SetElemSize { get; init; }

    /// <summary>For SetProperty: TSparseArray::Data base address.</summary>
    public string SetDataAddr { get; init; } = "";

    /// <summary>For SetProperty: UScriptStruct* if element is StructProperty.</summary>
    public string SetElemStructAddr { get; init; } = "";

    /// <summary>For SetProperty: struct name for element.</summary>
    public string SetElemStructType { get; init; } = "";

    /// <summary>For SetProperty: inline element preview.</summary>
    public List<ContainerElementValue>? SetElements { get; init; }

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

    /// <summary>For non-array EnumProperty/ByteProperty: UEnum* address for CE DropDownList sharing.</summary>
    public string EnumAddr { get; init; } = "";

    /// <summary>For non-array EnumProperty/ByteProperty: full UEnum entries for CE DropDownList.</summary>
    public List<EnumEntryValue>? EnumEntries { get; init; }

    /// <summary>For StrProperty: decoded UTF-8 string value.</summary>
    public string StrValue { get; init; } = "";

    /// <summary>For DataTable RowMap: number of rows (-1 = not a DataTable).</summary>
    public int DataTableRowCount { get; init; } = -1;

    /// <summary>For DataTable RowMap: row struct name (e.g., "JackDataTableRecipeBook").</summary>
    public string DataTableStructName { get; init; } = "";

    /// <summary>For DataTable RowMap: FName size in bytes (8 or 16 with CasePreservingName).</summary>
    public int DataTableFNameSize { get; init; }

    /// <summary>For DataTable RowMap: TSparseArray element stride (for CE XML offset calculation).</summary>
    public int DataTableStride { get; init; }

    /// <summary>For DataTable RowMap: UScriptStruct* address for row struct definition.</summary>
    public string DataTableRowStructAddr { get; init; } = "";

    /// <summary>For DataTable RowMap: row data for CE XML / CSX export (from WalkDataTableRowsAsync).</summary>
    public List<DataTableRowInfo>? DataTableRowData { get; init; }

    /// <summary>Display-friendly value string.</summary>
    public string DisplayValue =>
        !string.IsNullOrEmpty(TypedValue) ? TypedValue :
        !string.IsNullOrEmpty(PtrName) ? $"{PtrName} ({PtrClassName})" :
        !string.IsNullOrEmpty(StructTypeName) ? $"{{{StructTypeName}}}" :
        ArrayCount >= 0 && !string.IsNullOrEmpty(ArrayInnerType)
            ? FormatArrayDisplay()
            : ArrayCount >= 0 ? $"[{ArrayCount} elements]" :
        MapCount >= 0 ? FormatMapDisplay() :
        SetCount >= 0 ? FormatSetDisplay() :
        DataTableRowCount >= 0 ? $"{{DataTable: {DataTableRowCount} rows, {DataTableStructName}}}" :
        !string.IsNullOrEmpty(StrValue) ? $"\"{StrValue}\"" :
        !string.IsNullOrEmpty(HexValue) ? HexValue :
        "";

    /// <summary>Whether this field is a container that can be drilled into (Array/Map/Set/DataTable with data).</summary>
    public bool IsContainerNavigable =>
        !IsGuessed &&
        ((ArrayCount > 0 && !string.IsNullOrEmpty(ArrayInnerType)) ||
        (MapCount > 0 && !string.IsNullOrEmpty(MapKeyType)) ||
        (SetCount > 0 && !string.IsNullOrEmpty(SetElemType)) ||
        DataTableRowCount > 0);

    /// <summary>Whether this field is a clickable pointer to another object.</summary>
    public bool IsNavigable =>
        !IsGuessed &&
        ((!string.IsNullOrEmpty(PtrAddress) && PtrAddress != "0x0") ||
        (!string.IsNullOrEmpty(StructDataAddr) && StructDataAddr != "0x0"));

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

    /// <summary>Whether this field's value can be edited inline (scalar numeric/bool/enum types only).</summary>
    public bool IsEditable =>
        !IsGuessed &&
        !string.IsNullOrEmpty(FieldAddress) &&
        FieldValueConverter.IsEditableType(TypeName);

    /// <summary>Whether this field is a BoolProperty (for dropdown editing vs TextBox).</summary>
    public bool IsBoolProperty => TypeName == "BoolProperty";

    /// <summary>Static options for BoolProperty dropdown.</summary>
    public static string[] BoolOptions { get; } = ["true", "false"];

    /// <summary>Mutable value for DataGrid edit binding. Get returns the editable string form; set stores pending value.</summary>
    public string EditableValue
    {
        get
        {
            if (TypeName == "BoolProperty")
            {
                // Extract just "true" or "false" from TypedValue like "true (bit 2, mask 0x04)"
                if (!string.IsNullOrEmpty(TypedValue))
                    return TypedValue.StartsWith("true", System.StringComparison.OrdinalIgnoreCase) ? "true" : "false";
                return "false";
            }
            if (TypeName == "EnumProperty" && !string.IsNullOrEmpty(EnumName))
                return EnumName;
            // For numeric types, return TypedValue (already a clean number string)
            return TypedValue;
        }
        set => _editableValue = value;
    }
    private string _editableValue = "";

    /// <summary>Get the pending edit value (what the user typed). Falls back to EditableValue getter if not set.</summary>
    internal string GetPendingEditValue() => _editableValue;

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

    private string FormatMapDisplay()
    {
        var keyLabel = !string.IsNullOrEmpty(MapKeyType) ? MapKeyType : "?";
        var valLabel = !string.IsNullOrEmpty(MapValueType) ? MapValueType : "?";
        var header = $"{{Map: {MapCount}, {keyLabel} \u2192 {valLabel}}}";

        if (MapElements == null || MapElements.Count == 0)
            return header;

        const int previewCount = 3;
        var preview = MapElements
            .Take(previewCount)
            .Select(e =>
            {
                var k = !string.IsNullOrEmpty(e.KeyPtrName) ? e.KeyPtrName
                    : !string.IsNullOrEmpty(e.Key) ? e.Key : e.KeyHex;
                var v = !string.IsNullOrEmpty(e.ValuePtrName) ? e.ValuePtrName
                    : !string.IsNullOrEmpty(e.Value) ? e.Value : e.ValueHex;
                return $"{k}: {v}";
            });
        var joined = string.Join(", ", preview);

        if (MapCount > previewCount)
            joined += ", ...";

        return $"{header} = {{{joined}}}";
    }

    private string FormatSetDisplay()
    {
        var elemLabel = !string.IsNullOrEmpty(SetElemType) ? SetElemType : "?";
        var header = $"{{Set: {SetCount}, {elemLabel}}}";

        if (SetElements == null || SetElements.Count == 0)
            return header;

        const int previewCount = 5;
        var preview = SetElements
            .Take(previewCount)
            .Select(e => !string.IsNullOrEmpty(e.KeyPtrName) ? e.KeyPtrName
                : !string.IsNullOrEmpty(e.Key) ? e.Key : e.KeyHex);
        var joined = string.Join(", ", preview);

        if (SetCount > previewCount)
            joined += ", ...";

        return $"{header} = {{{joined}}}";
    }
}
