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
/// </summary>
public static class CeXmlExportService
{
    private static int _nextId;

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
            var ceType = MapCeType(field.TypeName, field.Size);
            bool isScalar = ceType != null;

            if (parentIsPointer && breadcrumbs.Count > 1)
            {
                // Under a pointer parent (not root): need dereference
                // Address=+0, Offsets=[field.Offset]
                if (isScalar)
                {
                    EmitLeaf(sb, leafIndent, field.Name, ceType!,
                        "+0", new[] { field.Offset });
                }
                else if (field.IsNavigable)
                {
                    // Navigable but non-scalar (struct/object): emit as group placeholder
                    EmitGroupOpen(sb, leafIndent, field.Name,
                        "+0", new[] { field.Offset });
                    EmitGroupClose(sb, leafIndent);
                }
            }
            else
            {
                // Under root directly, or under an inline struct parent: just +offset
                if (isScalar)
                {
                    EmitLeaf(sb, leafIndent, field.Name, ceType!,
                        $"+{field.Offset:X}", null);
                }
                else if (field.IsNavigable)
                {
                    EmitGroupOpen(sb, leafIndent, field.Name,
                        $"+{field.Offset:X}", null);
                    EmitGroupClose(sb, leafIndent);
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
            var ceType = MapCeType(field.TypeName, field.Size);
            if (ceType != null)
            {
                EmitLeaf(sb, leafIndent, field.Name, ceType,
                    $"+{field.Offset:X}", null);
            }
            else if (field.IsNavigable)
            {
                EmitGroupOpen(sb, leafIndent, field.Name,
                    $"+{field.Offset:X}", null);
                EmitGroupClose(sb, leafIndent);
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
        if (offsets != null && offsets.Length > 0)
        {
            sb.AppendLine($"{indent}  <Offsets>");
            foreach (var o in offsets)
                sb.AppendLine($"{indent}    <Offset>{o:X}</Offset>");
            sb.AppendLine($"{indent}  </Offsets>");
        }
        sb.AppendLine($"{indent}  <CheatEntries>");
    }

    private static void EmitGroupClose(StringBuilder sb, string indent)
    {
        sb.AppendLine($"{indent}  </CheatEntries>");
        sb.AppendLine($"{indent}</CheatEntry>");
    }

    private static void EmitLeaf(StringBuilder sb, string indent, string description,
        string ceType, string address, int[]? offsets)
    {
        sb.AppendLine($"{indent}<CheatEntry>");
        sb.AppendLine($"{indent}  <ID>{_nextId++}</ID>");
        sb.AppendLine($"{indent}  <Description>\"{description}\"</Description>");
        sb.AppendLine($"{indent}  <ShowAsSigned>0</ShowAsSigned>");
        sb.AppendLine($"{indent}  <VariableType>{ceType}</VariableType>");
        sb.AppendLine($"{indent}  <Address>{address}</Address>");
        if (offsets != null && offsets.Length > 0)
        {
            sb.AppendLine($"{indent}  <Offsets>");
            foreach (var o in offsets)
                sb.AppendLine($"{indent}    <Offset>{o:X}</Offset>");
            sb.AppendLine($"{indent}  </Offsets>");
        }
        sb.AppendLine($"{indent}</CheatEntry>");
    }

    /// <summary>
    /// Map UE property type to CE variable type. Returns null for unsupported/unknown types.
    /// </summary>
    private static string? MapCeType(string ueType, int size)
    {
        return ueType switch
        {
            "FloatProperty" => "Float",
            "DoubleProperty" => "Double",
            "IntProperty" => "4 Bytes",
            "UInt32Property" => "4 Bytes",
            "Int64Property" => "8 Bytes",
            "UInt64Property" => "8 Bytes",
            "Int16Property" => "2 Bytes",
            "UInt16Property" => "2 Bytes",
            "ByteProperty" => "Byte",
            "BoolProperty" => "Byte",
            "NameProperty" => "4 Bytes",
            _ => null // Unknown — not a scalar
        };
    }
}
