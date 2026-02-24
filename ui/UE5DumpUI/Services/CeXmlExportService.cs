using System.Text;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Services;

/// <summary>
/// Generates Cheat Engine XML address records using hierarchical nested format.
///
/// CE XML address resolution rules (hierarchical tree model):
/// - Root node: absolute address "Module.exe"+RVA
/// - Each child's Address is relative to its parent's RESOLVED address
/// - Pointer field: &lt;Address&gt;+{offset}&lt;/Address&gt; with &lt;Offsets&gt;&lt;Offset&gt;0&lt;/Offset&gt;&lt;/Offsets&gt;
///   → CE resolves to *(parentAddr + offset), children offset from the dereferenced value
/// - Inline field (scalar/struct): &lt;Address&gt;+{offset}&lt;/Address&gt; (no Offsets, no dereference)
/// - GroupHeader=1 makes an entry a collapsible folder with children
///
/// CE type mapping:
/// - Signed integers (IntProperty, Int8/16/64Property): ShowAsSigned=1
/// - Unsigned integers (UInt32/16/64Property, ByteProperty): ShowAsSigned=0
/// - BoolProperty with bit mask: VariableType=Binary, BitStart/BitLength from UE FieldMask
/// - Pointer fields (ObjectProperty navigable): ShowAsHex=1, GroupHeader placeholder
/// - Struct fields (StructProperty): real field names via DLL resolution, flattened nested structs
///
/// Struct expansion:
/// - StructProperty fields are resolved via WalkInstanceAsync to get real field names/types
/// - Nested StructProperty are recursively flattened (all inner fields at the same level)
/// - Pointer fields inside structs emit as 8 Bytes ShowAsHex placeholder
/// - Max recursion depth: 5 levels
/// </summary>
public static class CeXmlExportService
{
    // NOTE: _nextId is reset at the start of each Generate* method call,
    // so concurrent calls are safe as long as each completes atomically.
    // Using ThreadStatic to eliminate any cross-thread risk.
    [ThreadStatic]
    private static int _nextId;

    /// <summary>Max depth for recursive struct resolution.</summary>
    private const int MaxStructDepth = 5;

    /// <summary>Max entries for a CE DropDownList. Lists exceeding this are omitted.</summary>
    private const int MaxDropDownEntries = 3000;

    /// <summary>
    /// Tracks emitted DropDownList owners by UEnum address → parent group's Description.
    /// Reset per Generate* call. Enables DropDownListLink sharing for same-enum arrays.
    /// </summary>
    [ThreadStatic]
    private static Dictionary<string, string>? _dropDownOwners;

    /// <summary>
    /// Tracks emitted DropDownList parent descriptions to ensure uniqueness.
    /// CE uses Description text as DropDownListLink key, so duplicates cause ambiguity.
    /// If a duplicate is found, ".001", ".002" etc. suffix is appended.
    /// </summary>
    [ThreadStatic]
    private static HashSet<string>? _dropDownDescriptions;

    /// <summary>
    /// When true, pointer/array GroupHeader nodes (those with Offsets=[0]) emit
    /// &lt;Options moHideChildren="1" moDeactivateChildrenAsWell="1"/&gt; to collapse
    /// them by default in Cheat Engine. Root node is excluded (stays expanded).
    /// </summary>
    [ThreadStatic]
    private static bool _collapsePointerNodes;

    /// <summary>CE field metadata for XML generation.</summary>
    private record CeFieldInfo(
        string VariableType,
        bool IsSigned = false,
        bool ShowAsHex = false,
        int BitStart = -1,
        int BitLength = 0);

    // ========================================
    // Struct field resolution (async, requires DLL pipe)
    // ========================================

    /// <summary>
    /// Pre-resolve all StructProperty fields by walking their inner structure via the DLL.
    /// Returns a dictionary keyed by field offset, containing flattened inner fields
    /// with relative offsets from the struct start and dot-prefixed names for nested structs.
    ///
    /// Example: StructA at offset 0x100 with inner StructB at +0x10 containing X at +0x0
    ///   -> resolvedStructs[0x100] = [
    ///        LiveFieldValue { Name="IntField", Offset=0x0 },
    ///        LiveFieldValue { Name="StructB.X", Offset=0x10 },
    ///        LiveFieldValue { Name="StructB.Y", Offset=0x14 },
    ///      ]
    /// </summary>
    public static async Task<Dictionary<int, List<LiveFieldValue>>> ResolveStructFieldsAsync(
        IDumpService dump, IReadOnlyList<LiveFieldValue> fields, int arrayLimit = 64)
    {
        var result = new Dictionary<int, List<LiveFieldValue>>();

        foreach (var field in fields)
        {
            if (field.TypeName != "StructProperty"
                || string.IsNullOrEmpty(field.StructClassAddr)
                || string.IsNullOrEmpty(field.StructDataAddr)
                || field.StructDataAddr == "0x0")
                continue;

            var resolved = new List<LiveFieldValue>();
            try
            {
                await ResolveStructRecursiveAsync(dump, field.StructDataAddr, field.StructClassAddr,
                    "", 0, resolved, 0, arrayLimit);
            }
            catch
            {
                // If resolution fails (pipe error, etc.), leave empty — will fall back to placeholder
            }

            if (resolved.Count > 0)
                result[field.Offset] = resolved;
        }

        return result;
    }

    private static async Task ResolveStructRecursiveAsync(
        IDumpService dump, string dataAddr, string classAddr,
        string namePrefix, int baseOffset, List<LiveFieldValue> output, int depth,
        int arrayLimit = 64)
    {
        if (depth >= MaxStructDepth) return;

        var walkResult = await dump.WalkInstanceAsync(dataAddr, classAddr, arrayLimit: arrayLimit);

        foreach (var f in walkResult.Fields)
        {
            var displayName = string.IsNullOrEmpty(namePrefix) ? f.Name : $"{namePrefix}.{f.Name}";
            var absOffset = baseOffset + f.Offset;

            if (f.TypeName == "StructProperty"
                && !string.IsNullOrEmpty(f.StructClassAddr)
                && !string.IsNullOrEmpty(f.StructDataAddr)
                && f.StructDataAddr != "0x0")
            {
                // Nested struct — recurse and flatten into the same list
                await ResolveStructRecursiveAsync(dump, f.StructDataAddr, f.StructClassAddr,
                    displayName, absOffset, output, depth + 1, arrayLimit);
            }
            else if (f.IsPointerNavigation)
            {
                // Pointer inside struct — emit as pointer placeholder
                output.Add(new LiveFieldValue
                {
                    Name = displayName,
                    TypeName = f.TypeName,
                    Offset = absOffset,
                    Size = f.Size,
                    PtrAddress = f.PtrAddress,
                    PtrName = f.PtrName,
                    PtrClassName = f.PtrClassName,
                });
            }
            else
            {
                // Scalar or array field — add with accumulated offset and prefixed name
                output.Add(new LiveFieldValue
                {
                    Name = displayName,
                    TypeName = f.TypeName,
                    Offset = absOffset,
                    Size = f.Size,
                    HexValue = f.HexValue,
                    TypedValue = f.TypedValue,
                    BoolBitIndex = f.BoolBitIndex,
                    BoolFieldMask = f.BoolFieldMask,
                    ArrayCount = f.ArrayCount,
                    ArrayInnerType = f.ArrayInnerType,
                    ArrayElemSize = f.ArrayElemSize,
                    ArrayStructType = f.ArrayStructType,
                    ArrayElements = f.ArrayElements,
                    ArrayEnumAddr = f.ArrayEnumAddr,
                    ArrayEnumEntries = f.ArrayEnumEntries,
                    EnumName = f.EnumName,
                    EnumValue = f.EnumValue,
                    StrValue = f.StrValue,
                });
            }
        }
    }

    // ========================================
    // XML generation
    // ========================================

    /// <summary>
    /// Generate hierarchical CE XML from the navigation breadcrumb trail and current fields.
    ///
    /// Algorithm:
    /// - Root (breadcrumbs[0]): absolute address, GroupHeader
    /// - Each breadcrumb[i] (i>=1): Address=+{fieldOffset}
    ///   - If the breadcrumb is a pointer (IsPointerDeref): add Offsets=[0] to dereference
    ///   - If inline (struct): no Offsets
    ///   Parent's Offsets=[0] resolves the pointer, so children just add their offset
    /// - Leaf fields: always Address=+{field.Offset}, no Offsets
    ///   (Parent breadcrumb already resolved any pointer dereference via its Offsets=[0])
    /// - StructProperty (inline): Address=+{structOffset}, no Offsets, children at relative offsets
    /// - ArrayProperty (scalar): Address=+{fieldOffset}, Offsets=[0] (deref TArray.Data)
    ///   Element children: Address=+{N*elemSize} (Data pointer already dereferenced by parent)
    /// </summary>
    public static string GenerateHierarchicalXml(
        string rootAddress,
        string rootName,
        IReadOnlyList<BreadcrumbItem> breadcrumbs,
        IReadOnlyList<LiveFieldValue> currentFields,
        Dictionary<int, List<LiveFieldValue>>? resolvedStructs = null,
        bool collapsePointerNodes = false)
    {
        // Clean breadcrumbs: remove navigation cycles (e.g., Child->Parent->Child)
        // before generating XML to avoid deeply nested duplicate pointer chains.
        var cleanedBc = CleanBreadcrumbs(breadcrumbs);

        _nextId = 100;
        _collapsePointerNodes = collapsePointerNodes;
        _dropDownOwners = new Dictionary<string, string>();
        _dropDownDescriptions = new HashSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<CheatTable>");
        sb.AppendLine("  <CheatEntries>");

        // Build the nested structure recursively via indentation tracking
        var indent = "    ";
        var openTags = 0;

        // Root entry (cycle removal preserves breadcrumbs[0], so rootAddress/rootName are still valid)
        EmitGroupOpen(sb, indent, rootName, rootAddress, null, showAsHex: true, varType: "8 Bytes");
        openTags++;

        // Intermediate breadcrumb levels (navigation path)
        // Each breadcrumb: go to field offset from parent's resolved address.
        // If this field is a pointer, add Offsets=[0] to dereference it.
        // Parent's own Offsets=[0] (if pointer) already resolved the dereference,
        // so children just add their field offset.
        for (int i = 1; i < cleanedBc.Count; i++)
        {
            var bc = cleanedBc[i];
            var childIndent = indent + new string(' ', i * 2);

            EmitGroupOpen(sb, childIndent, bc.FieldName,
                $"+{bc.FieldOffset:X}",
                bc.IsPointerDeref ? new[] { 0 } : null,
                showAsHex: bc.IsPointerDeref);
            openTags++;
        }

        // Leaf fields at the deepest level
        // Parent breadcrumb (if any) already handled pointer dereference via Offsets=[0],
        // so all leaf fields simply use Address=+{field.Offset}.
        var leafIndent = indent + new string(' ', cleanedBc.Count * 2);
        EmitFields(sb, leafIndent, currentFields, resolvedStructs);

        // Close all nested levels (innermost first)
        for (int i = openTags - 1; i >= 0; i--)
        {
            var closeIndent = indent + new string(' ', i * 2);
            EmitGroupClose(sb, closeIndent);
        }

        sb.AppendLine("  </CheatEntries>");
        sb.AppendLine("</CheatTable>");

        return sb.ToString();
    }

    /// <summary>
    /// Generate CE XML for an instance with no navigation history (Instance Finder).
    /// Root = the instance itself. Fields are direct children with +{offset}.
    /// </summary>
    public static string GenerateInstanceXml(
        string rootAddress,
        string rootName,
        string className,
        IReadOnlyList<LiveFieldValue> fields,
        Dictionary<int, List<LiveFieldValue>>? resolvedStructs = null,
        bool collapsePointerNodes = false)
    {
        _nextId = 100;
        _collapsePointerNodes = collapsePointerNodes;
        _dropDownOwners = new Dictionary<string, string>();
        _dropDownDescriptions = new HashSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<CheatTable>");
        sb.AppendLine("  <CheatEntries>");

        var indent = "    ";
        EmitGroupOpen(sb, indent, $"{className}: {rootName}", rootAddress, null,
            showAsHex: true, varType: "8 Bytes");

        var leafIndent = indent + "  ";
        EmitFields(sb, leafIndent, fields, resolvedStructs);

        EmitGroupClose(sb, indent);

        sb.AppendLine("  </CheatEntries>");
        sb.AppendLine("</CheatTable>");

        return sb.ToString();
    }

    /// <summary>
    /// Generate a CE-compatible XML with an AutoAssembler script that registers a symbol.
    /// Accepts a pre-formatted address string (e.g., "module.exe"+RVA or plain hex).
    /// </summary>
    public static string GenerateRegisterSymbolXml(string symbolName, string formattedAddress)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<CheatTable>");
        sb.AppendLine("  <CheatEntries>");
        sb.AppendLine($"    <CheatEntry>");
        sb.AppendLine($"      <ID>0</ID>");
        sb.AppendLine($"      <Description>\"{symbolName}\"</Description>");
        sb.AppendLine($"      <VariableType>Auto Assembler Script</VariableType>");
        sb.AppendLine($"      <AssemblerScript>");

        sb.AppendLine("[ENABLE]");
        sb.AppendLine($"define({symbolName},{formattedAddress})");
        sb.AppendLine($"registersymbol({symbolName})");
        sb.AppendLine();

        sb.AppendLine("[DISABLE]");
        sb.AppendLine($"unregistersymbol({symbolName})");

        sb.AppendLine($"      </AssemblerScript>");
        sb.AppendLine($"    </CheatEntry>");
        sb.AppendLine("  </CheatEntries>");
        sb.AppendLine("</CheatTable>");

        return sb.ToString();
    }

    // ========================================
    // Breadcrumb cleaning
    // ========================================

    /// <summary>
    /// Remove cycles from the breadcrumb navigation path before XML generation.
    ///
    /// A cycle occurs when the user navigates away from an object and later returns to
    /// the same address (e.g., Child -> Parent -> Child again). The intermediate entries
    /// (the detour) are removed, keeping only the shortest path.
    ///
    /// Example: [A, B, C, A, B] -> A appears at 0 and 3 -> remove [1..3] -> [A, B]
    /// This gives the clean CE pointer chain: Root(A) -> field(B) instead of
    /// Root(A) -> field(B) -> Outer(C) -> field(A) -> field(B).
    /// </summary>
    internal static IReadOnlyList<BreadcrumbItem> CleanBreadcrumbs(IReadOnlyList<BreadcrumbItem> breadcrumbs)
    {
        if (breadcrumbs.Count <= 1) return breadcrumbs;

        var result = new List<BreadcrumbItem>(breadcrumbs);

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < result.Count && !changed; i++)
            {
                for (int j = i + 1; j < result.Count; j++)
                {
                    if (string.Equals(result[i].Address, result[j].Address, StringComparison.OrdinalIgnoreCase))
                    {
                        // Found cycle from i to j -- remove entries (i+1) through j inclusive.
                        // Keeps the first occurrence at i and continues with j+1 onward.
                        result.RemoveRange(i + 1, j - i);
                        changed = true;
                        break;
                    }
                }
            }
        }

        return result;
    }

    // ========================================
    // Private helpers
    // ========================================

    /// <summary>
    /// Emit all leaf fields, handling scalars, resolved structs, and navigable placeholders.
    /// All fields use Address=+{field.Offset} (no Offsets) because parent breadcrumb/group
    /// already resolved any pointer dereference via its own Offsets=[0].
    /// </summary>
    private static void EmitFields(StringBuilder sb, string indent,
        IReadOnlyList<LiveFieldValue> fields,
        Dictionary<int, List<LiveFieldValue>>? resolvedStructs)
    {
        foreach (var field in fields)
        {
            // Check if this StructProperty has pre-resolved children
            if (field.TypeName == "StructProperty"
                && resolvedStructs != null
                && resolvedStructs.TryGetValue(field.Offset, out var structChildren)
                && structChildren.Count > 0)
            {
                EmitResolvedStruct(sb, indent, field, structChildren);
                continue;
            }

            // ArrayProperty: emit as group with element children (Phase C)
            if (field.TypeName == "ArrayProperty" && field.ArrayCount >= 0)
            {
                EmitArrayProperty(sb, indent, field);
                continue;
            }

            var ceField = MapCeField(field);
            if (ceField != null)
            {
                EmitLeaf(sb, indent, field.Name, ceField,
                    $"+{field.Offset:X}", null);
            }
            else if (field.IsNavigable)
            {
                EmitNavigableField(sb, indent, field,
                    $"+{field.Offset:X}", null);
            }
        }
    }

    /// <summary>
    /// Emit a StructProperty with pre-resolved inner fields as a CE group.
    /// Struct is inline (not a pointer), so Address=+{structOffset}, no Offsets.
    /// Children are flattened (nested structs already expanded with dot-prefixed names).
    /// Each child's Offset is relative to the struct start.
    /// </summary>
    private static void EmitResolvedStruct(StringBuilder sb, string indent,
        LiveFieldValue structField, List<LiveFieldValue> children)
    {
        // Struct is inline: just offset from parent, no dereference
        var address = $"+{structField.Offset:X}";

        // Struct group header with struct type name in description
        var description = !string.IsNullOrEmpty(structField.StructTypeName)
            ? $"{structField.Name} ({structField.StructTypeName})"
            : structField.Name;

        EmitGroupOpen(sb, indent, description, address, null);
        var childIndent = indent + "  ";

        foreach (var child in children)
        {
            var ceField = MapCeField(child);
            if (ceField != null)
            {
                // Scalar child: offset relative to struct start
                EmitLeaf(sb, childIndent, child.Name, ceField,
                    $"+{child.Offset:X}", null);
            }
            else if (child.IsPointerNavigation)
            {
                // Pointer inside struct: emit as 8 Bytes hex placeholder
                EmitGroupPlaceholder(sb, childIndent, child.Name,
                    $"+{child.Offset:X}", null, showAsHex: true);
            }
            else if (child.TypeName == "ArrayProperty" && child.ArrayCount >= 0)
            {
                // Array inside struct — full expansion if element data is available
                EmitArrayProperty(sb, childIndent, child);
            }
            // Skip unknown types (delegates, etc.) — they're not useful in CE
        }

        EmitGroupClose(sb, indent);
    }

    /// <summary>
    /// Emit an ArrayProperty as a CE group with per-element children.
    /// Scalar arrays (Float, Int, Bool, Byte, Enum, Name) get individual leaf entries.
    /// Non-scalar arrays (Struct, Object) or empty arrays emit as placeholder only.
    ///
    /// TArray addressing:
    /// - Group header: Address=+{fieldOffset}, Offsets=[0] → dereferences TArray.Data pointer
    /// - Element children: Address=+{N*elemSize} → simple offset from the dereferenced Data pointer
    /// </summary>
    private static void EmitArrayProperty(StringBuilder sb, string indent,
        LiveFieldValue field)
    {
        // Build description: "FieldName [N x Type (SizeB)]"
        var typeLabel = !string.IsNullOrEmpty(field.ArrayStructType)
            ? field.ArrayStructType : field.ArrayInnerType;
        var desc = field.ArrayCount > 0 && !string.IsNullOrEmpty(typeLabel)
            ? $"{field.Name} [{field.ArrayCount} x {typeLabel} ({field.ArrayElemSize}B)]"
            : field.Name;

        // Phase F: struct array with resolved sub-fields → per-element group emission
        if (field.ArrayInnerType == "StructProperty"
            && field.ArrayElements is { Count: > 0 }
            && field.ArrayElements[0].StructFields is { Count: > 0 })
        {
            EmitStructArrayProperty(sb, indent, field, desc);
            return;
        }

        // Map inner type to CE type
        var ceElem = MapInnerTypeToCeField(field.ArrayInnerType);

        // Non-scalar, empty, or no inline elements → placeholder only (no deref needed)
        if (ceElem == null || field.ArrayCount <= 0
            || field.ArrayElements == null || field.ArrayElements.Count == 0)
        {
            EmitGroupPlaceholder(sb, indent, desc, $"+{field.Offset:X}", null);
            return;
        }

        // CE DropDownList: determine if this array should have dropdown support.
        // DropDownList goes on the parent GroupHeader; all children use DropDownListLink.
        _dropDownOwners ??= new Dictionary<string, string>();
        string? dropDownContent = null;
        string? dropDownLinkTarget = null;
        bool isEnumArray = field.ArrayInnerType is "EnumProperty" or "ByteProperty"
            && field.ArrayEnumEntries is { Count: > 0 and <= MaxDropDownEntries };
        bool isNameArray = field.ArrayInnerType == "NameProperty"
            && field.ArrayElements is { Count: > 0 and <= MaxDropDownEntries };
        // Fallback: enum/byte array with per-element enum names but no full UEnum entries list.
        // Build DropDownList from element values (like NameProperty), no sharing.
        bool isEnumFallback = !isEnumArray
            && field.ArrayInnerType is "EnumProperty" or "ByteProperty"
            && field.ArrayElements is { Count: > 0 and <= MaxDropDownEntries }
            && field.ArrayElements.Any(e => !string.IsNullOrEmpty(e.EnumName));

        if (isEnumArray)
        {
            var enumKey = field.ArrayEnumAddr;
            if (!string.IsNullOrEmpty(enumKey) && _dropDownOwners.TryGetValue(enumKey, out var existing))
            {
                // Shared: this parent and all children link to first occurrence's parent
                dropDownLinkTarget = existing;
            }
            else
            {
                // First occurrence: parent gets DropDownList, children link to this parent.
                // Ensure unique description (CE uses Description text as DropDownListLink key).
                dropDownContent = BuildDropDownContent(
                    field.ArrayEnumEntries!.Select(e => (e.Value, e.Name)));
                desc = EnsureUniqueDropDownDesc(desc);
                dropDownLinkTarget = desc;
                if (!string.IsNullOrEmpty(enumKey))
                    _dropDownOwners[enumKey] = desc;
            }
        }
        else if (isEnumFallback)
        {
            // Build from current element enum values (deduplicated)
            var seen = new HashSet<long>();
            var pairs = new List<(long, string)>();
            foreach (var e in field.ArrayElements!)
            {
                if (seen.Add(e.RawIntValue) && !string.IsNullOrEmpty(e.EnumName))
                    pairs.Add((e.RawIntValue, e.EnumName));
            }
            if (pairs.Count > 0)
            {
                dropDownContent = BuildDropDownContent(pairs);
                desc = EnsureUniqueDropDownDesc(desc);
                dropDownLinkTarget = desc;
            }
        }
        else if (isNameArray)
        {
            // Build from current element values (deduplicated)
            var seen = new HashSet<long>();
            var pairs = new List<(long, string)>();
            foreach (var e in field.ArrayElements!)
            {
                if (seen.Add(e.RawIntValue) && !string.IsNullOrEmpty(e.Value))
                    pairs.Add((e.RawIntValue, e.Value));
            }
            if (pairs.Count > 0)
            {
                dropDownContent = BuildDropDownContent(pairs);
                desc = EnsureUniqueDropDownDesc(desc);
                dropDownLinkTarget = desc;
            }
        }

        // Array group: Address=+{fieldOffset}, Offsets=[0] to dereference TArray.Data pointer.
        // TArray layout: { Data* +0x00, Count +0x08, Max +0x0C }
        // Offsets=[0] reads the pointer at TArray+0x00 (the Data pointer).
        // DropDownList/DropDownListLink is emitted on this parent group node.
        if (dropDownContent != null)
        {
            EmitGroupOpen(sb, indent, desc, $"+{field.Offset:X}", new[] { 0 },
                dropDownContent: dropDownContent);
        }
        else if (dropDownLinkTarget != null)
        {
            // Shared enum: parent links to first occurrence's parent
            EmitGroupOpen(sb, indent, desc, $"+{field.Offset:X}", new[] { 0 },
                dropDownListLink: dropDownLinkTarget);
        }
        else
        {
            EmitGroupOpen(sb, indent, desc, $"+{field.Offset:X}", new[] { 0 });
        }
        var childIndent = indent + "  ";

        foreach (var elem in field.ArrayElements)
        {
            // Element description: simplified [N] when dropdown is active, else full names
            string elemDesc;
            if (dropDownLinkTarget != null)
            {
                // DisplayValueAsItem=1 handles showing the resolved name in CE's Value column
                elemDesc = $"[{elem.Index}]";
            }
            else if (!string.IsNullOrEmpty(elem.PtrName))
            {
                elemDesc = !string.IsNullOrEmpty(elem.PtrClassName)
                    ? $"[{elem.Index}] {elem.PtrName} ({elem.PtrClassName})"
                    : $"[{elem.Index}] {elem.PtrName}";
            }
            else if (!string.IsNullOrEmpty(elem.EnumName))
                elemDesc = $"[{elem.Index}] {elem.EnumName}";
            else
                elemDesc = $"[{elem.Index}]";

            // Element: simple offset from the already-dereferenced Data pointer
            int elemByteOffset = elem.Index * field.ArrayElemSize;

            if (dropDownLinkTarget != null)
            {
                // All children link to the parent (or first occurrence's parent) Description
                EmitLeaf(sb, childIndent, elemDesc, ceElem,
                    $"+{elemByteOffset:X}", null,
                    dropDownListLink: dropDownLinkTarget);
            }
            else
            {
                EmitLeaf(sb, childIndent, elemDesc, ceElem,
                    $"+{elemByteOffset:X}", null);
            }
        }

        EmitGroupClose(sb, indent);
    }

    /// <summary>
    /// Phase F: Emit struct array with per-element groups containing field children.
    /// Array group: Offsets=[0] (deref TArray.Data)
    /// Element group: Address=+{N*elemSize}, no Offsets (inline within Data)
    /// Field leaf: Address=+{fieldOffset} (relative to element start)
    /// </summary>
    private static void EmitStructArrayProperty(StringBuilder sb, string indent,
        LiveFieldValue field, string desc)
    {
        EmitGroupOpen(sb, indent, desc, $"+{field.Offset:X}", new[] { 0 });
        var elemIndent = indent + "  ";

        foreach (var elem in field.ArrayElements!)
        {
            int elemByteOffset = elem.Index * field.ArrayElemSize;
            var elemDesc = $"[{elem.Index}]";

            if (elem.StructFields is { Count: > 0 })
            {
                // Element group: inline offset from Data pointer
                EmitGroupOpen(sb, elemIndent, elemDesc, $"+{elemByteOffset:X}", null);
                var fieldIndent = elemIndent + "  ";

                foreach (var sf in elem.StructFields)
                {
                    var ceField = MapInnerTypeToCeField(sf.TypeName);
                    if (ceField != null)
                    {
                        EmitLeaf(sb, fieldIndent, sf.Name, ceField, $"+{sf.Offset:X}", null);
                    }
                    // Skip unmappable types (nested structs, pointers, containers)
                }

                EmitGroupClose(sb, elemIndent);
            }
            else
            {
                EmitGroupPlaceholder(sb, elemIndent, elemDesc, $"+{elemByteOffset:X}", null);
            }
        }

        EmitGroupClose(sb, indent);
    }

    /// <summary>Emit a group header that will contain child entries (opens CheatEntries block).</summary>
    private static void EmitGroupOpen(StringBuilder sb, string indent, string description,
        string address, int[]? offsets, bool showAsHex = false, string? varType = null,
        string? dropDownContent = null, string? dropDownListLink = null)
    {
        sb.AppendLine($"{indent}<CheatEntry>");
        sb.AppendLine($"{indent}  <ID>{_nextId++}</ID>");
        sb.AppendLine($"{indent}  <Description>\"{description}\"</Description>");
        // CE DropDownList: inline list on this group, or link to another group's list
        if (dropDownContent != null)
            sb.AppendLine($"{indent}  <DropDownList DisplayValueAsItem=\"1\">{dropDownContent}</DropDownList>");
        else if (dropDownListLink != null)
            sb.AppendLine($"{indent}  <DropDownListLink>{EscapeXmlContent(dropDownListLink)}</DropDownListLink>");
        if (showAsHex)
            sb.AppendLine($"{indent}  <ShowAsHex>1</ShowAsHex>");
        sb.AppendLine($"{indent}  <ShowAsSigned>0</ShowAsSigned>");
        sb.AppendLine($"{indent}  <GroupHeader>1</GroupHeader>");
        // Collapse pointer/array nodes: emit Options only for non-root nodes with pointer dereference
        if (_collapsePointerNodes && offsets != null && address.StartsWith("+"))
            sb.AppendLine($"{indent}  <Options moHideChildren=\"1\" moDeactivateChildrenAsWell=\"1\"/>");
        if (varType != null)
            sb.AppendLine($"{indent}  <VariableType>{varType}</VariableType>");
        sb.AppendLine($"{indent}  <Address>{address}</Address>");
        EmitOffsets(sb, indent, offsets);
        sb.AppendLine($"{indent}  <CheatEntries>");
    }

    /// <summary>Close a group header's CheatEntries block.</summary>
    private static void EmitGroupClose(StringBuilder sb, string indent)
    {
        sb.AppendLine($"{indent}  </CheatEntries>");
        sb.AppendLine($"{indent}</CheatEntry>");
    }

    /// <summary>
    /// Emit a group placeholder -- a GroupHeader with no children.
    /// Used for navigable struct/pointer fields at leaf level when resolution is unavailable.
    /// Pointer fields get ShowAsHex=1.
    /// </summary>
    private static void EmitGroupPlaceholder(StringBuilder sb, string indent, string description,
        string address, int[]? offsets, bool showAsHex = false)
    {
        sb.AppendLine($"{indent}<CheatEntry>");
        sb.AppendLine($"{indent}  <ID>{_nextId++}</ID>");
        sb.AppendLine($"{indent}  <Description>\"{description}\"</Description>");
        if (showAsHex)
            sb.AppendLine($"{indent}  <ShowAsHex>1</ShowAsHex>");
        sb.AppendLine($"{indent}  <ShowAsSigned>0</ShowAsSigned>");
        sb.AppendLine($"{indent}  <GroupHeader>1</GroupHeader>");
        // Collapse pointer/array nodes: emit Options only for non-root nodes with pointer dereference
        if (_collapsePointerNodes && offsets != null && address.StartsWith("+"))
            sb.AppendLine($"{indent}  <Options moHideChildren=\"1\" moDeactivateChildrenAsWell=\"1\"/>");
        sb.AppendLine($"{indent}  <Address>{address}</Address>");
        EmitOffsets(sb, indent, offsets);
        sb.AppendLine($"{indent}</CheatEntry>");
    }

    /// <summary>
    /// Emit a scalar leaf entry with proper CE type, signedness, and bit field support.
    /// </summary>
    private static void EmitLeaf(StringBuilder sb, string indent, string description,
        CeFieldInfo ceField, string address, int[]? offsets,
        string? dropDownListLink = null)
    {
        sb.AppendLine($"{indent}<CheatEntry>");
        sb.AppendLine($"{indent}  <ID>{_nextId++}</ID>");
        sb.AppendLine($"{indent}  <Description>\"{description}\"</Description>");
        // CE DropDownListLink: element content referencing the parent group's Description
        if (dropDownListLink != null)
            sb.AppendLine($"{indent}  <DropDownListLink>{EscapeXmlContent(dropDownListLink)}</DropDownListLink>");
        if (ceField.ShowAsHex)
            sb.AppendLine($"{indent}  <ShowAsHex>1</ShowAsHex>");
        sb.AppendLine($"{indent}  <ShowAsSigned>{(ceField.IsSigned ? 1 : 0)}</ShowAsSigned>");
        sb.AppendLine($"{indent}  <VariableType>{ceField.VariableType}</VariableType>");
        if (ceField.BitStart >= 0)
        {
            sb.AppendLine($"{indent}  <BitStart>{ceField.BitStart}</BitStart>");
            sb.AppendLine($"{indent}  <BitLength>{ceField.BitLength}</BitLength>");
            sb.AppendLine($"{indent}  <ShowAsBinary>0</ShowAsBinary>");
        }
        sb.AppendLine($"{indent}  <Address>{address}</Address>");
        EmitOffsets(sb, indent, offsets);
        sb.AppendLine($"{indent}</CheatEntry>");
    }

    /// <summary>Emit Offsets block if offsets are provided.</summary>
    private static void EmitOffsets(StringBuilder sb, string indent, int[]? offsets)
    {
        if (offsets != null && offsets.Length > 0)
        {
            sb.AppendLine($"{indent}  <Offsets>");
            foreach (var o in offsets)
                sb.AppendLine($"{indent}    <Offset>{o:X}</Offset>");
            sb.AppendLine($"{indent}  </Offsets>");
        }
    }

    /// <summary>
    /// Build DropDownList content string from value:name pairs.
    /// Format: newline-separated "value:name" entries (decimal values, no leading zeros).
    /// </summary>
    private static string BuildDropDownContent(IEnumerable<(long value, string name)> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine();  // newline after opening tag
        foreach (var (v, n) in entries)
            sb.AppendLine($"{v}:{n}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Escape special characters for XML element text content.</summary>
    private static string EscapeXmlContent(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    /// Ensure a DropDownList parent Description is unique.
    /// CE uses Description text as DropDownListLink key, so duplicates cause ambiguity.
    /// Appends ".001", ".002" etc. suffix if the description was already used.
    /// </summary>
    private static string EnsureUniqueDropDownDesc(string desc)
    {
        _dropDownDescriptions ??= new HashSet<string>(StringComparer.Ordinal);
        if (_dropDownDescriptions.Add(desc))
            return desc;  // first use — unique

        // Collision: append suffix .001, .002, ...
        for (int i = 1; i < 1000; i++)
        {
            var suffixed = $"{desc}.{i:D3}";
            if (_dropDownDescriptions.Add(suffixed))
                return suffixed;
        }
        return desc;  // fallback (should never happen)
    }

    /// <summary>
    /// Emit a navigable field as a group placeholder (no resolved children available).
    /// Pointer fields get ShowAsHex=1.
    /// </summary>
    private static void EmitNavigableField(StringBuilder sb, string indent,
        LiveFieldValue field, string address, int[]? offsets)
    {
        EmitGroupPlaceholder(sb, indent, field.Name, address, offsets,
            showAsHex: field.IsPointerNavigation);
    }

    /// <summary>
    /// Map UE property type + field metadata to CE field info.
    /// Returns null for unsupported/unknown types (struct, array, delegate, etc.).
    ///
    /// Signedness rules:
    /// - Signed: IntProperty (int32), Int8Property, Int16Property, Int64Property
    /// - Unsigned: UInt32Property, UInt16Property, UInt64Property, ByteProperty
    ///
    /// BoolProperty rules:
    /// - If BoolBitIndex >= 0: Binary type with BitStart/BitLength (CE bit field)
    /// - Otherwise: Byte type (fallback for bool without bit info)
    /// </summary>
    private static CeFieldInfo? MapCeField(LiveFieldValue field)
    {
        return field.TypeName switch
        {
            "FloatProperty" => new CeFieldInfo("Float"),
            "DoubleProperty" => new CeFieldInfo("Double"),

            // Signed integers
            "Int8Property" => new CeFieldInfo("Byte", IsSigned: true),
            "Int16Property" => new CeFieldInfo("2 Bytes", IsSigned: true),
            "IntProperty" => new CeFieldInfo("4 Bytes", IsSigned: true),
            "Int64Property" => new CeFieldInfo("8 Bytes", IsSigned: true),

            // Unsigned integers
            "ByteProperty" => new CeFieldInfo("Byte"),
            "UInt16Property" => new CeFieldInfo("2 Bytes"),
            "UInt32Property" => new CeFieldInfo("4 Bytes"),
            "UInt64Property" => new CeFieldInfo("8 Bytes"),

            // Bool with bit field support
            "BoolProperty" when field.BoolBitIndex >= 0 =>
                new CeFieldInfo("Binary", BitStart: field.BoolBitIndex, BitLength: 1),
            "BoolProperty" => new CeFieldInfo("Byte"),

            // FName index
            "NameProperty" => new CeFieldInfo("4 Bytes"),

            // Enum -- underlying value is typically int32 (4 bytes)
            "EnumProperty" => new CeFieldInfo("4 Bytes"),

            // String types -- CE "String" reads pointer-to-string
            "StrProperty" => new CeFieldInfo("String"),
            "TextProperty" => new CeFieldInfo("String"),

            _ => null // Unknown -- not a scalar (StructProperty, ArrayProperty, ObjectProperty, etc.)
        };
    }

    /// <summary>
    /// Map an array inner type name to CE field info.
    /// Similar to MapCeField but takes a type name string (for array element types).
    /// BoolProperty in arrays = full byte (no bitfield).
    /// Returns null for non-scalar types (StructProperty, ObjectProperty, etc.).
    /// </summary>
    private static CeFieldInfo? MapInnerTypeToCeField(string innerTypeName)
    {
        return innerTypeName switch
        {
            "FloatProperty" => new CeFieldInfo("Float"),
            "DoubleProperty" => new CeFieldInfo("Double"),

            // Signed integers
            "Int8Property" => new CeFieldInfo("Byte", IsSigned: true),
            "Int16Property" => new CeFieldInfo("2 Bytes", IsSigned: true),
            "IntProperty" => new CeFieldInfo("4 Bytes", IsSigned: true),
            "Int64Property" => new CeFieldInfo("8 Bytes", IsSigned: true),

            // Unsigned integers
            "ByteProperty" => new CeFieldInfo("Byte"),
            "UInt16Property" => new CeFieldInfo("2 Bytes"),
            "UInt32Property" => new CeFieldInfo("4 Bytes"),
            "UInt64Property" => new CeFieldInfo("8 Bytes"),

            // Bool in arrays: stored as full bytes (no bitfield)
            "BoolProperty" => new CeFieldInfo("Byte"),

            // FName index
            "NameProperty" => new CeFieldInfo("4 Bytes"),

            // Enum -- underlying value is typically int32
            "EnumProperty" => new CeFieldInfo("4 Bytes"),

            // Phase D: pointer types — 8 bytes, shown as hex
            "ObjectProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),
            "ClassProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),

            // Phase E: weak object pointer — 8 bytes (ObjectIndex + SerialNumber)
            "WeakObjectProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),

            _ => null // Non-scalar (StructProperty, SoftObjectProperty, etc.)
        };
    }
}
