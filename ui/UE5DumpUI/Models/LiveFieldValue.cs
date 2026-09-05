using CommunityToolkit.Mvvm.ComponentModel;
using System;
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
/// <remarks>
/// ⚠ <b>Row objects SURVIVE a refresh.</b> [LWREFRESH-2026-08-21]
///
/// <para>Live Walker's refresh used to assign <c>Fields[i] = newFields[i]</c> under a comment
/// claiming it "preserves scroll position". Measured on DumperTest, it does the opposite: the
/// <c>DataGridCollectionView</c> splits each indexer assignment into Remove+Add and the grid ends
/// up <b>exactly one row higher</b> per Refresh, cumulatively — which is the reported "the match
/// sits one row below the viewport". Disabling the assignment entirely made the drift vanish;
/// replacing the loop with a single Clear+Add was worse still (it jumps to the top).</para>
///
/// <para>So the refresh now copies values onto the EXISTING row via
/// <see cref="CopyLiveValuesFrom"/>, and the members that can differ between two walks of the same
/// object are observable so the cells still repaint. The structural ones — <c>Name</c>,
/// <c>Offset</c>, <c>TypeName</c>, the navigability flags — stay <c>init</c> on purpose: they are
/// exactly what the same-layout branch checks before deciding it may reuse the rows at all.</para>
/// </remarks>
public sealed partial class LiveFieldValue : ObservableObject
{
    public string Name { get; init; } = "";
    public string TypeName { get; init; } = "";
    public int Offset { get; init; }
    public int Size { get; init; }

    /// <summary>
    /// True when this row is NOT reachable from the current object by a byte offset —
    /// it was derived from a back-reference, not read out of a reflected field. The
    /// GWorld actor list is the only producer today: <c>ULevel::Actors</c> carries no
    /// UPROPERTY (audit #5 F8/F9), so the actors under "Start from GWorld" are
    /// reconstructed from each actor's <c>Outer</c> and there is no offset — and no
    /// element index — that walks UWorld to them.
    ///
    /// <para><b>Offset stays 0 on such a row and MUST NOT be treated as one.</b> A
    /// pointer chain built through it emits <c>[UWorld + 0]</c>, which is the world's
    /// vtable pointer — that is the whole defect this flag exists to stop. Navigation
    /// stamps <c>BreadcrumbItem.FieldOffset = -1</c> for these (the marker
    /// <see cref="ViewModels.LiveWalkerViewModel.PathStepToBreadcrumbs"/> already uses
    /// for the same hop), and every CE export re-roots the spine there.</para>
    /// </summary>
    public bool HasNoParentOffset { get; init; }

    /// <summary>
    /// Offset column text: hex, or an em dash when the row has no parent offset at all
    /// (<see cref="HasNoParentOffset"/>). Displaying those as "0x0" is what made a
    /// derived actor look like a field at offset zero. Sorting still uses
    /// <see cref="Offset"/>.
    /// </summary>
    public string OffsetDisplay => HasNoParentOffset ? "—" : $"0x{Offset:X}";

    /// <summary>True if this field was heuristically guessed (not from UE reflection).</summary>
    public bool IsGuessed { get; init; }

    /// <summary>Raw hex value (always populated for readable fields).</summary>
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    [NotifyPropertyChangedFor(nameof(ValueTooltip))]
    [NotifyPropertyChangedFor(nameof(EditableValue))]
    [ObservableProperty] private string _hexValue = "";

    /// <summary>Human-readable typed value (for Float, Int, Bool, etc.).</summary>
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    [NotifyPropertyChangedFor(nameof(ValueTooltip))]
    [NotifyPropertyChangedFor(nameof(EditableValue))]
    [ObservableProperty] private string _typedValue = "";

    /// <summary>For ObjectProperty: address of the referenced object.</summary>
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    [NotifyPropertyChangedFor(nameof(ValueTooltip))]
    [NotifyPropertyChangedFor(nameof(EditableValue))]
    [ObservableProperty] private string _ptrAddress = "";

    /// <summary>For ObjectProperty: name of the pointed-to object.</summary>
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    [NotifyPropertyChangedFor(nameof(ValueTooltip))]
    [NotifyPropertyChangedFor(nameof(EditableValue))]
    [ObservableProperty] private string _ptrName = "";

    /// <summary>For ObjectProperty: class name of the pointed-to object.</summary>
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    [NotifyPropertyChangedFor(nameof(ValueTooltip))]
    [NotifyPropertyChangedFor(nameof(EditableValue))]
    [ObservableProperty] private string _ptrClassName = "";

    /// <summary>For ObjectProperty: UClass* address of the pointed-to object (for CSX drilldown).</summary>
    public string PtrClassAddr { get; init; } = "";

    /// <summary>For BoolProperty: bit index (0-7) within the byte; -1 = not a bool.</summary>
    public int BoolBitIndex { get; init; } = -1;

    /// <summary>For BoolProperty: raw FieldMask byte.</summary>
    public int BoolFieldMask { get; init; }

    /// <summary>For BoolProperty: byte offset within the field for bitfield reads/writes.</summary>
    public int BoolByteOffset { get; init; }

    /// <summary>For ArrayProperty: element count (-1 = not an array).</summary>
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    [NotifyPropertyChangedFor(nameof(ValueTooltip))]
    [NotifyPropertyChangedFor(nameof(EditableValue))]
    [ObservableProperty] private int _arrayCount = -1;

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

    /// <summary>For ArrayProperty (Phase G soft arrays): sizeof(FName) in bytes (8 normal, 12 with CasePreservingName). 0 = not a soft array.</summary>
    public int SoftArrayFNameSize { get; init; }

    /// <summary>For ArrayProperty (Phase G soft arrays): true when FSoftObjectPath uses FTopLevelAssetPath (UE >= 5.1) — two FNames at PathOffset / PathOffset+fnameSize. False = single FName AssetPathName at PathOffset (UE4 / UE5.0).</summary>
    public bool SoftArrayIsTopLevelAssetPath { get; init; }

    /// <summary>For ArrayProperty (Phase G soft arrays): byte offset of FSoftObjectPath inside the TSoftObjectPtr element.
    /// 0x10 up to UE 5.2, 0x08 from UE 5.3 — which deleted TPersistentObjectPtr::TagAtLastTest.
    /// Measured DLL-side from the property's ElementSize; 0 = not a soft array (or a pre-fix DLL, where 0x10 is the only safe assumption).
    /// ⚠ Do not bake 0x10: on any UE 5.3+ title that reads AssetName where PackageName should be.</summary>
    public int SoftArrayPathOffset { get; init; }

    /// <summary>For ArrayProperty Phase B: inline scalar element values (up to 64).
    /// Settable so NavigateToArrayContainerAsync can persist on-demand-fetched elements
    /// (struct arrays whose inner struct reflects 0 fields get no inline walk preview)
    /// onto the cached container field, so Back-navigation re-renders the same rows.</summary>
    public List<ArrayElementValue>? ArrayElements { get; set; }

    /// <summary>For ArrayProperty (enum/byte-with-enum): UEnum* address for CE DropDownList sharing.</summary>
    public string ArrayEnumAddr { get; init; } = "";

    /// <summary>For ArrayProperty (enum/byte-with-enum): full UEnum entries for CE DropDownList.</summary>
    public List<EnumEntryValue>? ArrayEnumEntries { get; init; }

    /// <summary>For MapProperty: entry count (-1 = not a map).</summary>
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    [NotifyPropertyChangedFor(nameof(ValueTooltip))]
    [NotifyPropertyChangedFor(nameof(EditableValue))]
    [ObservableProperty] private int _mapCount = -1;

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

    /// <summary>For MapProperty: aligned byte offset of value within TPair (may differ from MapKeySize due to alignment).</summary>
    public int MapValueOffset { get; init; }

    /// <summary>
    /// For MapProperty: the TSparseArray slot stride the DLL actually used to read these elements.
    /// 0 = the DLL did not supply one (older DLL, or element data was not read).
    /// <para>
    /// Never recompute this client-side. The real formula is
    /// <c>Align(Align(pairSize, alignof(TPair)) + 8, alignof(TPair))</c> and the alignments do not
    /// cross the wire, so a client-side copy can only guess — which is exactly how three separate
    /// C# copies silently went stale when the DLL's own formula was corrected (audit #5 V2).
    /// Go through <see cref="ContainerGeometry.MapStrideOf"/>.
    /// </para>
    /// </summary>
    public int MapStride { get; init; }

    /// <summary>For MapProperty: inline element preview.</summary>
    public List<ContainerElementValue>? MapElements { get; init; }

    /// <summary>For SetProperty: entry count (-1 = not a set).</summary>
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    [NotifyPropertyChangedFor(nameof(ValueTooltip))]
    [NotifyPropertyChangedFor(nameof(EditableValue))]
    [ObservableProperty] private int _setCount = -1;

    /// <summary>For SetProperty: element type name.</summary>
    public string SetElemType { get; init; } = "";

    /// <summary>For SetProperty: element size in bytes.</summary>
    public int SetElemSize { get; init; }

    /// <summary>
    /// For SetProperty: the TSparseArray slot stride the DLL actually used. 0 = not supplied.
    /// Same rule as <see cref="MapStride"/> — go through <see cref="ContainerGeometry.SetStrideOf"/>.
    /// </summary>
    public int SetStride { get; init; }

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
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    [NotifyPropertyChangedFor(nameof(ValueTooltip))]
    [NotifyPropertyChangedFor(nameof(EditableValue))]
    [ObservableProperty] private string _enumName = "";

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

    /// <summary>For DataTable RowMap: the FName's PADDED SLOT inside the TPair (8, or 16 with CasePreservingName).
    /// ⚠ NOT sizeof(FName), which is 12 under CasePreservingName — RowMap is TMap&lt;FName, uint8*&gt; and the
    /// pointer makes the pair alignof 8, so the 12-byte key pads up to 16. Deliberately different from
    /// SoftArrayFNameSize above, which IS a sizeof. Do not "make them consistent".</summary>
    public int DataTableFNameSize { get; init; }

    /// <summary>For DataTable RowMap: TSparseArray element stride (for CE XML offset calculation).</summary>
    public int DataTableStride { get; init; }

    /// <summary>For DataTable RowMap: UScriptStruct* address for row struct definition.</summary>
    public string DataTableRowStructAddr { get; init; } = "";

    /// <summary>For DataTable RowMap: row data for CE XML / CSX export (from WalkDataTableRowsAsync).</summary>
    public List<DataTableRowInfo>? DataTableRowData { get; init; }

    /// <summary>
    /// Take the freshly-walked values from <paramref name="src"/> without replacing this object.
    /// [LWREFRESH-2026-08-21]
    ///
    /// <para>Only the members that can differ between two walks of the SAME object are copied.
    /// The caller has already established that the layout matches (same count, same first name),
    /// so <c>Name</c>/<c>Offset</c>/<c>TypeName</c> are identical by construction and copying them
    /// would be noise — and they are <c>init</c> precisely so that stays true.</para>
    ///
    /// <para>⚠ <c>IsSearchMatch</c> is deliberately NOT copied. The refresh path re-runs
    /// <c>MarkSearchMatches</c> over the NEW rows before this is called, so taking the flag from
    /// <paramref name="src"/> would be right by luck; but callers that copy outside that path would
    /// silently clear a highlight. The marker owns that flag.</para>
    /// </summary>
    public void CopyLiveValuesFrom(LiveFieldValue src)
    {
        HexValue     = src.HexValue;
        TypedValue   = src.TypedValue;
        PtrAddress   = src.PtrAddress;
        PtrName      = src.PtrName;
        PtrClassName = src.PtrClassName;
        ArrayCount   = src.ArrayCount;
        MapCount     = src.MapCount;
        SetCount     = src.SetCount;
        EnumName     = src.EnumName;
        FieldAddress = src.FieldAddress;
        ArrayElements = src.ArrayElements;
    }

    /// <summary>Display-friendly value string.</summary>
    public string DisplayValue =>
        // Intrinsic FDateTime / FTimespan structs carry no readable preview (a single
        // raw int64 Ticks). Format them human-readable here, display-only — TypedValue
        // stays empty so edit (raw ticks via the synthesized child) and CE/CSX export
        // are unaffected.
        DecodeDateTimeStruct(StructTypeName, HexValue) ??
        (!string.IsNullOrEmpty(TypedValue) ? TypedValue :
        !string.IsNullOrEmpty(PtrName) ? $"{PtrName} ({PtrClassName})" :
        !string.IsNullOrEmpty(StructTypeName) ? $"{{{StructTypeName}}}" :
        ArrayCount >= 0 && !string.IsNullOrEmpty(ArrayInnerType)
            ? FormatArrayDisplay()
            : ArrayCount >= 0 ? $"[{ArrayCount} elements]" :
        MapCount >= 0 ? FormatMapDisplay() :
        SetCount >= 0 ? FormatSetDisplay() :
        DataTableRowCount >= 0 ? $"{{DataTable: {DataTableRowCount} rows, {DataTableStructName}}}" :
        !string.IsNullOrEmpty(StrValue) ? $"\"{StrValue}\"" :
        DecodeHexAsNumeric(TypeName, HexValue) ??
        (!string.IsNullOrEmpty(HexValue) ? HexValue :
        ""));

    /// <summary>
    /// The full <see cref="DisplayValue"/>, for the Value column's hover tooltip — or
    /// <c>null</c> when there is nothing to show.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Value cell is a fixed 200 px <c>TextBlock</c>
    /// (<c>Views/LiveWalkerPanel.axaml</c>), so anything longer is simply invisible —
    /// there is not even a trimming ellipsis to hint that text was cut.
    /// <c>[V8PREVIEWCLIP-2026-08-23]</c> is what that costs: a DataTable's pre-drill
    /// preview reads
    /// <c>{DataTable: 100 rows, DumperTestTableRow}  ⚠ showing 64 of 100</c>, the badge
    /// is appended LAST, and the prefix alone overflows the column — so the one
    /// disclosure that warns you the grid holds 64 of 100 rows was unreachable at the
    /// default width, on every table, at every N. The ViewModel string was correct the
    /// whole time and a test asserted it; only the pixels were wrong. Same shape as
    /// <c>[PARAMSSORT-2026-08-22]</c> in another panel.
    /// </para>
    /// <para>
    /// ⚠ Returns <c>null</c>, not <c>""</c>, and that is the point of having a separate
    /// property instead of binding <c>DisplayValue</c> straight to <c>ToolTip.Tip</c>:
    /// Avalonia shows a tooltip whenever <c>Tip</c> is non-null, so an empty string
    /// would pop an empty box on every blank cell. This matches how the other bound
    /// tooltips in this UI behave (<c>XrefInfo</c>, <c>ScoreTooltip</c>).
    /// </para>
    /// <para>
    /// ⚠ Every <c>[NotifyPropertyChangedFor(nameof(DisplayValue))]</c> in this file
    /// carries a matching one for this property. They must stay in lockstep or a live
    /// field's tooltip goes stale while its visible text updates — which is a worse
    /// failure than the clipping, because a stale tooltip looks authoritative.
    /// <c>LiveFieldValueTooltipTests</c> pins the pairing at source level.
    /// </para>
    /// </remarks>
    public string? ValueTooltip => string.IsNullOrEmpty(DisplayValue) ? null : DisplayValue;

    /// <summary>
    /// The full <see cref="TypeName"/>, for the Type column's hover tooltip — or
    /// <c>null</c> when empty.
    /// </summary>
    /// <remarks>
    /// Sibling of <see cref="ValueTooltip"/>, same defect, one column left. The Type
    /// cell is 115 px and UE type names run long by nature — the case that prompted it
    /// was <c>DataTableRows</c> rendering as <c>DataTableRo</c>, and
    /// <c>SoftObjectProperty</c> / <c>MulticastInlineDelegateProperty</c> are worse. It
    /// was spotted as the negative control for <c>[V8PREVIEWCLIP-2026-08-23]</c>:
    /// hovering this cell to prove the Value tooltip was really the new binding showed
    /// a column that was equally clipped and equally silent.
    ///
    /// <para>⚠ No <c>[NotifyPropertyChangedFor]</c> needed, and that is a fact about
    /// <see cref="TypeName"/>, not an omission: it is <c>init</c>-only and structural —
    /// the same-layout branch of a refresh checks it before reusing rows at all, so it
    /// cannot change under a live row. <see cref="DisplayValue"/> is the opposite and
    /// that is why its twin needs nine notifications.</para>
    /// </remarks>
    public string? TypeTooltip => string.IsNullOrEmpty(TypeName) ? null : TypeName;

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
    [ObservableProperty] private string _fieldAddress = "";

    /// <summary>Whether this field matches the current search query (set by ViewModel).</summary>
    [ObservableProperty] private bool _isSearchMatch ;

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
            // For numeric types, return TypedValue (already a clean number string).
            // Fallback: if DLL didn't send a typed value, try decoding hex bytes.
            if (string.IsNullOrEmpty(TypedValue))
                return DecodeHexAsNumeric(TypeName, HexValue) ?? TypedValue;
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

    /// <summary>
    /// Decode hex bytes into a numeric display string for known scalar property types.
    /// Returns null if the type is not a known numeric type or if hex is empty/malformed.
    /// Used as a defensive fallback when the DLL doesn't populate TypedValue (e.g., memory read edge cases).
    /// </summary>
    internal static string? DecodeHexAsNumeric(string typeName, string hexValue)
    {
        if (string.IsNullOrEmpty(hexValue) || string.IsNullOrEmpty(typeName))
            return null;

        try
        {
            var bytes = Convert.FromHexString(hexValue.Replace("...", "").TrimEnd());
            return typeName switch
            {
                "FloatProperty" when bytes.Length >= 4 =>
                    FormatFloat(BitConverter.ToSingle(bytes, 0)),
                "DoubleProperty" when bytes.Length >= 8 =>
                    FormatDouble(BitConverter.ToDouble(bytes, 0)),
                "IntProperty" when bytes.Length >= 4 =>
                    BitConverter.ToInt32(bytes, 0).ToString(),
                "UInt32Property" when bytes.Length >= 4 =>
                    BitConverter.ToUInt32(bytes, 0).ToString(),
                "Int64Property" when bytes.Length >= 8 =>
                    BitConverter.ToInt64(bytes, 0).ToString(),
                "UInt64Property" when bytes.Length >= 8 =>
                    BitConverter.ToUInt64(bytes, 0).ToString(),
                "Int16Property" when bytes.Length >= 2 =>
                    BitConverter.ToInt16(bytes, 0).ToString(),
                "UInt16Property" when bytes.Length >= 2 =>
                    BitConverter.ToUInt16(bytes, 0).ToString(),
                "ByteProperty" when bytes.Length >= 1 =>
                    bytes[0].ToString(),
                "Int8Property" when bytes.Length >= 1 =>
                    ((sbyte)bytes[0]).ToString(),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Format an intrinsic FDateTime / FTimespan struct from its raw 8-byte value as a
    /// human-readable date / duration, keeping the raw ticks alongside in parentheses
    /// (e.g. "2026-06-11 14:30:00 (638..)" / "01:17:53 (467..)"). Display-only: the raw
    /// int64 ticks remain the value used for in-place edit (via the synthesized Ticks
    /// child) and for CE XML / CE Field / CSX export.
    ///
    /// Ticks are .NET-compatible (100ns since 0001-01-01). The value is shown verbatim as
    /// wall-clock time — no UTC/local conversion is applied, so what you see matches the
    /// stored bytes. Returns null for any non-matching type or unparseable data so
    /// DisplayValue falls through to its default ("{DateTime}").
    /// </summary>
    internal static string? DecodeDateTimeStruct(string structType, string hexValue)
    {
        if (string.IsNullOrEmpty(hexValue))
            return null;
        if (structType is not ("DateTime" or "Timespan"))
            return null;

        try
        {
            var bytes = Convert.FromHexString(hexValue.Replace("...", "").TrimEnd());
            if (bytes.Length < 8)
                return null;
            long ticks = BitConverter.ToInt64(bytes, 0);

            if (structType == "DateTime")
            {
                // new DateTime(long) rejects ticks outside [0, DateTime.MaxValue.Ticks];
                // guard so uninitialized / garbage values fall back to the raw display.
                if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                    return null;
                var dt = new DateTime(ticks);
                return $"{dt:yyyy-MM-dd HH:mm:ss} ({ticks})";
            }
            else // Timespan
            {
                var ts = new TimeSpan(ticks);
                var mag = ts.Duration();
                string sign = ts < TimeSpan.Zero ? "-" : "";
                string body = mag.Days > 0
                    ? $"{mag.Days}d {mag.Hours:00}:{mag.Minutes:00}:{mag.Seconds:00}"
                    : $"{mag.Hours:00}:{mag.Minutes:00}:{mag.Seconds:00}";
                return $"{sign}{body} ({ticks})";
            }
        }
        catch
        {
            return null;
        }
    }

    private static string FormatFloat(float v)
    {
        // Match DLL format: integer display when fractional part is zero
        if (v == (int)v && !float.IsInfinity(v) && !float.IsNaN(v))
            return ((int)v).ToString();
        return v.ToString("G10");
    }

    private static string FormatDouble(double v)
    {
        if (v == (long)v && !double.IsInfinity(v) && !double.IsNaN(v))
            return ((long)v).ToString();
        return v.ToString("G15");
    }
}
