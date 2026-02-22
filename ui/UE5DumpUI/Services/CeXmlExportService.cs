using System.Text;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Services;

/// <summary>
/// Generates Cheat Engine XML address records using hierarchical nested format.
///
/// CE XML address resolution rules:
/// - Root node: absolute address "Module.exe"+RVA
/// - Same-layer offset: &lt;Address&gt;+{hex}&lt;/Address&gt; (no dereference)
/// - Pointer dereference: &lt;Address&gt;+0&lt;/Address&gt; with &lt;Offsets&gt;&lt;Offset&gt;{hex}&lt;/Offset&gt;&lt;/Offsets&gt;
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
        IDumpService dump, IReadOnlyList<LiveFieldValue> fields)
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
                    "", 0, resolved, 0);
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
        string namePrefix, int baseOffset, List<LiveFieldValue> output, int depth)
    {
        if (depth >= MaxStructDepth) return;

        var walkResult = await dump.WalkInstanceAsync(dataAddr, classAddr);

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
                    displayName, absOffset, output, depth + 1);
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
                // Scalar field — add with accumulated offset and prefixed name
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
    /// - Each breadcrumb[i] (i>=1): determines Address and Offsets based on parent type
    ///   - If i==1 (direct child of root): Address=+{fieldOffset} (root is already the object)
    ///   - If parent was pointer: Address=+0, Offsets=[fieldOffset] (dereference parent pointer)
    ///   - If parent was struct: Address=+{fieldOffset} (inline offset)
    /// - Leaf fields at current level:
    ///   - Under root only (Count==1): Address=+{field.Offset}
    ///   - Under pointer parent: Address=+0, Offsets=[field.Offset]
    ///   - Under struct parent: Address=+{field.Offset}
    /// </summary>
    public static string GenerateHierarchicalXml(
        string rootAddress,
        string rootName,
        IReadOnlyList<BreadcrumbItem> breadcrumbs,
        IReadOnlyList<LiveFieldValue> currentFields,
        Dictionary<int, List<LiveFieldValue>>? resolvedStructs = null)
    {
        // Clean breadcrumbs: remove navigation cycles (e.g., Child->Parent->Child)
        // before generating XML to avoid deeply nested duplicate pointer chains.
        var cleanedBc = CleanBreadcrumbs(breadcrumbs);

        _nextId = 100;
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
        for (int i = 1; i < cleanedBc.Count; i++)
        {
            var bc = cleanedBc[i];
            var childIndent = indent + new string(' ', i * 2);

            if (i == 1)
            {
                // Direct child of root. Root is the object instance itself.
                // Access field at offset N: just +N (no dereference needed from root)
                EmitGroupOpen(sb, childIndent, bc.FieldName,
                    $"+{bc.FieldOffset:X}", null,
                    showAsHex: bc.IsPointerDeref);
            }
            else
            {
                var prev = cleanedBc[i - 1];
                if (prev.IsPointerDeref)
                {
                    // Parent was a pointer field. Must dereference to reach target object.
                    EmitGroupOpen(sb, childIndent, bc.FieldName,
                        "+0", new[] { bc.FieldOffset });
                }
                else
                {
                    // Parent was inline struct. Just offset from parent.
                    EmitGroupOpen(sb, childIndent, bc.FieldName,
                        $"+{bc.FieldOffset:X}", null);
                }
            }
            openTags++;
        }

        // Leaf fields at the deepest level
        var leafIndent = indent + new string(' ', cleanedBc.Count * 2);
        bool parentIsPointer = cleanedBc.Count == 1 || cleanedBc[^1].IsPointerDeref;

        EmitFields(sb, leafIndent, currentFields, parentIsPointer, cleanedBc.Count > 1, resolvedStructs);

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
        Dictionary<int, List<LiveFieldValue>>? resolvedStructs = null)
    {
        _nextId = 100;
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<CheatTable>");
        sb.AppendLine("  <CheatEntries>");

        var indent = "    ";
        EmitGroupOpen(sb, indent, $"{className}: {rootName}", rootAddress, null,
            showAsHex: true, varType: "8 Bytes");

        var leafIndent = indent + "  ";
        EmitFields(sb, leafIndent, fields, parentIsPointer: false, needDeref: false, resolvedStructs);

        EmitGroupClose(sb, indent);

        sb.AppendLine("  </CheatEntries>");
        sb.AppendLine("</CheatTable>");

        return sb.ToString();
    }

    /// <summary>
    /// Generate a CE-compatible XML with an AutoAssembler script that registers a symbol.
    /// </summary>
    public static string GenerateRegisterSymbolXml(string symbolName, string moduleName, ulong rva)
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
        sb.AppendLine($"define({symbolName},\"{moduleName}\"+{rva:X})");
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
    /// </summary>
    private static void EmitFields(StringBuilder sb, string indent,
        IReadOnlyList<LiveFieldValue> fields, bool parentIsPointer, bool needDeref,
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
                EmitResolvedStruct(sb, indent, field, structChildren, parentIsPointer, needDeref);
                continue;
            }

            var ceField = MapCeField(field);
            bool isScalar = ceField != null;

            if (parentIsPointer && needDeref)
            {
                // Under a pointer parent (not root): need dereference
                if (isScalar)
                {
                    EmitLeaf(sb, indent, field.Name, ceField!,
                        "+0", new[] { field.Offset });
                }
                else if (field.IsNavigable)
                {
                    EmitNavigableField(sb, indent, field,
                        "+0", new[] { field.Offset });
                }
            }
            else
            {
                // Under root directly, or under an inline struct parent: just +offset
                if (isScalar)
                {
                    EmitLeaf(sb, indent, field.Name, ceField!,
                        $"+{field.Offset:X}", null);
                }
                else if (field.IsNavigable)
                {
                    EmitNavigableField(sb, indent, field,
                        $"+{field.Offset:X}", null);
                }
            }
        }
    }

    /// <summary>
    /// Emit a StructProperty with pre-resolved inner fields as a CE group.
    /// Children are flattened (nested structs already expanded with dot-prefixed names).
    /// Each child's Offset is relative to the struct start.
    /// </summary>
    private static void EmitResolvedStruct(StringBuilder sb, string indent,
        LiveFieldValue structField, List<LiveFieldValue> children,
        bool parentIsPointer, bool needDeref)
    {
        // Determine the group header's address (same logic as other fields)
        string address;
        int[]? offsets;

        if (parentIsPointer && needDeref)
        {
            address = "+0";
            offsets = new[] { structField.Offset };
        }
        else
        {
            address = $"+{structField.Offset:X}";
            offsets = null;
        }

        // Struct group header with struct type name in description
        var description = !string.IsNullOrEmpty(structField.StructTypeName)
            ? $"{structField.Name} ({structField.StructTypeName})"
            : structField.Name;

        EmitGroupOpen(sb, indent, description, address, offsets);
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
            // Skip unknown types (arrays, delegates, etc.) — they're not useful in CE
        }

        EmitGroupClose(sb, indent);
    }

    /// <summary>Emit a group header that will contain child entries (opens CheatEntries block).</summary>
    private static void EmitGroupOpen(StringBuilder sb, string indent, string description,
        string address, int[]? offsets, bool showAsHex = false, string? varType = null)
    {
        sb.AppendLine($"{indent}<CheatEntry>");
        sb.AppendLine($"{indent}  <ID>{_nextId++}</ID>");
        sb.AppendLine($"{indent}  <Description>\"{description}\"</Description>");
        if (showAsHex)
            sb.AppendLine($"{indent}  <ShowAsHex>1</ShowAsHex>");
        sb.AppendLine($"{indent}  <ShowAsSigned>0</ShowAsSigned>");
        sb.AppendLine($"{indent}  <GroupHeader>1</GroupHeader>");
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
        sb.AppendLine($"{indent}  <Address>{address}</Address>");
        EmitOffsets(sb, indent, offsets);
        sb.AppendLine($"{indent}</CheatEntry>");
    }

    /// <summary>
    /// Emit a scalar leaf entry with proper CE type, signedness, and bit field support.
    /// </summary>
    private static void EmitLeaf(StringBuilder sb, string indent, string description,
        CeFieldInfo ceField, string address, int[]? offsets)
    {
        sb.AppendLine($"{indent}<CheatEntry>");
        sb.AppendLine($"{indent}  <ID>{_nextId++}</ID>");
        sb.AppendLine($"{indent}  <Description>\"{description}\"</Description>");
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
}
