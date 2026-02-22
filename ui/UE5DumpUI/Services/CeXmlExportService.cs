using System.Text;
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
/// - Struct fields (StructProperty navigable): GroupHeader placeholder (no empty CheatEntries)
///
/// Struct auto-expansion:
/// - Simple StructProperty with 1-2 numeric values (detected via f:[...] hint) are auto-expanded
/// - Children named #1, #2 with Float type, at +8 and +C (8-byte struct preamble skip)
/// - Only triggers when TypedValue shows actual values, NOT when showing {TypeName}
/// </summary>
public static class CeXmlExportService
{
    private static int _nextId;

    /// <summary>CE field metadata for XML generation.</summary>
    private record CeFieldInfo(
        string VariableType,
        bool IsSigned = false,
        bool ShowAsHex = false,
        int BitStart = -1,
        int BitLength = 0);

    /// <summary>Info for auto-expanding a simple StructProperty in CE XML.</summary>
    private record StructExpandInfo(int ValueCount, string CeType, int TypeSize, int Preamble);

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
        IReadOnlyList<LiveFieldValue> currentFields)
    {
        _nextId = 100;
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<CheatTable>");
        sb.AppendLine("  <CheatEntries>");

        // Build the nested structure recursively via indentation tracking
        var indent = "    ";
        var openTags = 0;

        // Root entry
        EmitGroupOpen(sb, indent, rootName, rootAddress, null, showAsHex: true, varType: "8 Bytes");
        openTags++;

        // Intermediate breadcrumb levels (navigation path)
        for (int i = 1; i < breadcrumbs.Count; i++)
        {
            var bc = breadcrumbs[i];
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
                var prev = breadcrumbs[i - 1];
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
        var leafIndent = indent + new string(' ', breadcrumbs.Count * 2);
        bool parentIsPointer = breadcrumbs.Count == 1 || breadcrumbs[^1].IsPointerDeref;

        foreach (var field in currentFields)
        {
            var ceField = MapCeField(field);
            bool isScalar = ceField != null;

            if (parentIsPointer && breadcrumbs.Count > 1)
            {
                // Under a pointer parent (not root): need dereference
                // Address=+0, Offsets=[field.Offset]
                if (isScalar)
                {
                    EmitLeaf(sb, leafIndent, field.Name, ceField!,
                        "+0", new[] { field.Offset });
                }
                else if (field.IsNavigable)
                {
                    // Navigable but non-scalar (struct/object): auto-expand or placeholder
                    EmitNavigableField(sb, leafIndent, field,
                        "+0", new[] { field.Offset });
                }
            }
            else
            {
                // Under root directly, or under an inline struct parent: just +offset
                if (isScalar)
                {
                    EmitLeaf(sb, leafIndent, field.Name, ceField!,
                        $"+{field.Offset:X}", null);
                }
                else if (field.IsNavigable)
                {
                    EmitNavigableField(sb, leafIndent, field,
                        $"+{field.Offset:X}", null);
                }
            }
        }

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
        IReadOnlyList<LiveFieldValue> fields)
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
        foreach (var field in fields)
        {
            var ceField = MapCeField(field);
            if (ceField != null)
            {
                EmitLeaf(sb, leafIndent, field.Name, ceField,
                    $"+{field.Offset:X}", null);
            }
            else if (field.IsNavigable)
            {
                EmitNavigableField(sb, leafIndent, field,
                    $"+{field.Offset:X}", null);
            }
        }

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
    // Private helpers
    // ========================================

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
    /// Emit a group placeholder — a GroupHeader with no children.
    /// Used for navigable struct/pointer fields at leaf level.
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
    /// Try to get struct auto-expansion info for a simple StructProperty.
    /// Returns expansion info if the field has f:[val1] or f:[val1, val2] hint in TypedValue.
    /// Returns null if not expandable (not StructProperty, no values, showing {TypeName}, etc.).
    /// </summary>
    private static StructExpandInfo? TryGetStructExpand(LiveFieldValue field)
    {
        if (field.TypeName != "StructProperty") return null;
        if (string.IsNullOrEmpty(field.TypedValue)) return null;
        // Only expand when TypedValue shows actual values: "f:[300.0000, 300.0000]"
        // NOT when showing "{GameplayAttributeData}" (uninitialized/unknown struct)
        if (!field.TypedValue.StartsWith("f:[") || !field.TypedValue.EndsWith("]")) return null;

        var inner = field.TypedValue[3..^1]; // Extract content between "f:[" and "]"
        var parts = inner.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 1 || parts.Length > 2) return null;

        // Preamble: structs > 8 bytes have an 8-byte preamble (vtable/base class pointer)
        int preamble = (field.Size > 8) ? 8 : 0;
        return new StructExpandInfo(parts.Length, "Float", 4, preamble);
    }

    /// <summary>
    /// Emit a navigable field — either auto-expanded (struct with values) or as a group placeholder.
    /// For expandable structs: emits GroupOpen + #1/#2 Float children + GroupClose.
    /// For non-expandable: emits GroupPlaceholder (pointer gets ShowAsHex=1).
    /// </summary>
    private static void EmitNavigableField(StringBuilder sb, string indent,
        LiveFieldValue field, string address, int[]? offsets)
    {
        var expand = TryGetStructExpand(field);
        if (expand != null)
        {
            // Auto-expand: group with #1, #2 children
            EmitGroupOpen(sb, indent, field.Name, address, offsets);
            var childIndent = indent + "  ";
            int valueOffset = expand.Preamble;
            for (int vi = 0; vi < expand.ValueCount; vi++)
            {
                EmitLeaf(sb, childIndent, $"#{vi + 1}",
                    new CeFieldInfo(expand.CeType),
                    $"+{valueOffset:X}", null);
                valueOffset += expand.TypeSize;
            }
            EmitGroupClose(sb, indent);
        }
        else
        {
            // Non-expandable navigable field: placeholder
            EmitGroupPlaceholder(sb, indent, field.Name, address, offsets,
                showAsHex: field.IsPointerNavigation);
        }
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

            _ => null // Unknown — not a scalar (StructProperty, ArrayProperty, ObjectProperty, etc.)
        };
    }
}
