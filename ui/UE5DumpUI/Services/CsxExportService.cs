using System.Text;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Generates Cheat Engine Structure Dissect (.CSX) export from Live Walker data.
///
/// CSX is the XML format used by CE's "Define new structure" → "Import from file".
/// It describes structure layouts with offsets, types, and optional nested child structures
/// for pointer dereference.
///
/// Key differences from CE XML (Cheat Table address list):
/// - CSX uses decimal Offset + hex OffsetHex (not address expressions)
/// - CSX uses Vartype (not VariableType) + Bytesize + DisplayMethod
/// - CSX supports nested Structure within Element for pointer targets
/// - Single-layer: StructProperty fields are flattened inline; pointer targets get dummy children
/// </summary>
public static class CsxExportService
{
    // CSX type descriptor
    private record CsxTypeInfo(string Vartype, int Bytesize, string DisplayMethod);

    /// <summary>
    /// Generate CSX XML from the current Live Walker fields.
    /// StructProperty fields are resolved and flattened inline.
    /// </summary>
    public static async Task<string> GenerateCsxAsync(
        IDumpService dump,
        string structName,
        IReadOnlyList<LiveFieldValue> fields,
        int arrayLimit = 64)
    {
        // Resolve StructProperty inner fields via DLL (reuse existing logic)
        var resolvedStructs = await CeXmlExportService.ResolveStructFieldsAsync(
            dump, fields, arrayLimit);

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("<Structures>");
        sb.Append("  <Structure Name=\"").Append(EscapeXml(structName))
          .AppendLine("\" AutoFill=\"0\" AutoCreate=\"1\" DefaultHex=\"0\" AutoDestroy=\"0\" DoNotSaveLocal=\"0\" RLECompression=\"1\" AutoCreateStructsize=\"4096\">");
        sb.AppendLine("    <Elements>");

        foreach (var field in fields)
        {
            if (field.TypeName == "StructProperty")
            {
                // Flatten struct fields inline with "StructType / FieldName" naming
                EmitStructPropertyFlattened(sb, field, resolvedStructs);
            }
            else
            {
                EmitElement(sb, field.Offset, field.Name, field, "      ");
            }
        }

        sb.AppendLine("    </Elements>");
        sb.AppendLine("  </Structure>");
        sb.AppendLine("</Structures>");

        return sb.ToString();
    }

    /// <summary>
    /// Flatten a StructProperty's inner fields inline, with offsets relative to parent base.
    /// </summary>
    private static void EmitStructPropertyFlattened(
        StringBuilder sb,
        LiveFieldValue structField,
        Dictionary<int, List<LiveFieldValue>> resolvedStructs)
    {
        var prefix = !string.IsNullOrEmpty(structField.StructTypeName)
            ? structField.StructTypeName
            : structField.Name;

        if (resolvedStructs.TryGetValue(structField.Offset, out var innerFields) && innerFields.Count > 0)
        {
            foreach (var inner in innerFields)
            {
                var absoluteOffset = structField.Offset + inner.Offset;
                var description = $"{prefix} / {inner.Name}";
                EmitElement(sb, absoluteOffset, description, inner, "      ");
            }
        }
        else
        {
            // Unresolved struct — emit as raw bytes block
            var typeInfo = new CsxTypeInfo("Array of byte",
                structField.Size > 0 ? structField.Size : 8, "hexadecimal");
            EmitElementRaw(sb, structField.Offset, structField.Name, typeInfo, null, "      ");
        }
    }

    /// <summary>
    /// Emit a single &lt;Element&gt; for a field.
    /// </summary>
    private static void EmitElement(StringBuilder sb, int offset, string description,
        LiveFieldValue field, string indent)
    {
        var typeInfo = MapCsxType(field.TypeName, field.Size);
        string? childStructure = null;

        // BoolProperty with bitmask: CSX has no bitmask type,
        // so we append bit info to the description for the user.
        if (field.TypeName == "BoolProperty" && field.BoolBitIndex >= 0)
        {
            description = $"{description} (bit {field.BoolBitIndex}, mask 0x{field.BoolFieldMask:X2})";
        }

        // Determine if this needs a child structure
        switch (field.TypeName)
        {
            case "StrProperty":
                childStructure = BuildStrChildStructure(field.HexValue);
                break;

            case "ObjectProperty":
            case "ClassProperty":
            case "SoftObjectProperty":
            case "SoftClassProperty":
            case "LazyObjectProperty":
            case "InterfaceProperty":
                childStructure = BuildDummyChildStructure(field.PtrAddress ?? field.HexValue);
                break;

            case "ArrayProperty":
                childStructure = BuildDummyChildStructure(field.ArrayDataAddr ?? field.HexValue);
                break;

            case "MapProperty":
                childStructure = BuildDummyChildStructure(field.MapDataAddr ?? field.HexValue);
                break;

            case "SetProperty":
                childStructure = BuildDummyChildStructure(field.SetDataAddr ?? field.HexValue);
                break;
        }

        EmitElementRaw(sb, offset, description, typeInfo, childStructure, indent);
    }

    /// <summary>
    /// Emit raw XML for a single Element.
    /// </summary>
    private static void EmitElementRaw(StringBuilder sb, int offset, string description,
        CsxTypeInfo typeInfo, string? childStructure, string indent)
    {
        sb.Append(indent)
          .Append("<Element Offset=\"").Append(offset).Append('"')
          .Append(" Vartype=\"").Append(typeInfo.Vartype).Append('"')
          .Append(" Bytesize=\"").Append(typeInfo.Bytesize).Append('"')
          .Append(" OffsetHex=\"").Append(offset.ToString("X8")).Append('"')
          .Append(" Description=\"").Append(EscapeXml(description)).Append('"')
          .Append(" DisplayMethod=\"").Append(typeInfo.DisplayMethod).Append('"');

        if (childStructure != null)
        {
            sb.AppendLine(">");
            sb.Append(childStructure);
            sb.Append(indent).AppendLine("</Element>");
        }
        else
        {
            sb.AppendLine("/>");
        }
    }

    /// <summary>
    /// Map UE property type to CSX type descriptor.
    /// </summary>
    private static CsxTypeInfo MapCsxType(string typeName, int fieldSize)
    {
        return typeName switch
        {
            "IntProperty"       => new CsxTypeInfo("4 Bytes", 4, "unsigned integer"),
            "UInt32Property"    => new CsxTypeInfo("4 Bytes", 4, "unsigned integer"),
            "Int16Property"     => new CsxTypeInfo("2 Bytes", 2, "signed integer"),
            "UInt16Property"    => new CsxTypeInfo("2 Bytes", 2, "unsigned integer"),
            "Int8Property"      => new CsxTypeInfo("Byte", 1, "signed integer"),
            "Int64Property"     => new CsxTypeInfo("8 Bytes", 8, "signed integer"),
            "UInt64Property"    => new CsxTypeInfo("8 Bytes", 8, "unsigned integer"),
            "ByteProperty"     => new CsxTypeInfo("Byte", 1, "unsigned integer"),
            "FloatProperty"    => new CsxTypeInfo("Float", 4, "unsigned integer"),
            "DoubleProperty"   => new CsxTypeInfo("Double", 8, "unsigned integer"),
            "BoolProperty"     => new CsxTypeInfo("Byte", 1, "unsigned integer"),
            "EnumProperty"     => new CsxTypeInfo(fieldSize switch
            {
                1 => "Byte",
                2 => "2 Bytes",
                8 => "8 Bytes",
                _ => "4 Bytes",
            }, fieldSize > 0 ? fieldSize : 4, "unsigned integer"),
            "NameProperty"     => new CsxTypeInfo("8 Bytes", 8, "unsigned integer"),

            // Pointer types — Vartype=Pointer so CE can dereference
            "StrProperty"           => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "ObjectProperty"        => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "ClassProperty"         => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "SoftObjectProperty"    => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "SoftClassProperty"     => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "LazyObjectProperty"    => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "InterfaceProperty"     => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "ArrayProperty"         => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "MapProperty"           => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "SetProperty"           => new CsxTypeInfo("Pointer", 8, "unsigned integer"),

            // Opaque types
            "TextProperty"         => new CsxTypeInfo("8 Bytes", 8, "hexadecimal"),
            "WeakObjectProperty"   => new CsxTypeInfo("8 Bytes", 8, "hexadecimal"),
            "DelegateProperty"     => new CsxTypeInfo("8 Bytes", 8, "hexadecimal"),
            "MulticastInlineDelegateProperty" =>
                new CsxTypeInfo("Array of byte", fieldSize > 0 ? fieldSize : 16, "hexadecimal"),
            "MulticastSparseDelegateProperty" =>
                new CsxTypeInfo("Array of byte", fieldSize > 0 ? fieldSize : 16, "hexadecimal"),

            // Fallback — raw bytes
            _ => new CsxTypeInfo("Array of byte",
                fieldSize > 0 ? fieldSize : 8, "hexadecimal"),
        };
    }

    /// <summary>
    /// Build a dummy child Structure for pointer elements (ObjectProperty, MapProperty, etc.).
    /// </summary>
    private static string BuildDummyChildStructure(string? addr)
    {
        var name = FormatStructName(addr);
        var sb = new StringBuilder();
        sb.Append("        <Structure Name=\"").Append(EscapeXml(name))
          .AppendLine("\" AutoFill=\"0\" AutoCreate=\"1\" DefaultHex=\"0\" AutoDestroy=\"0\" DoNotSaveLocal=\"0\" RLECompression=\"1\" AutoCreateStructsize=\"4096\">");
        sb.AppendLine("          <Elements>");
        sb.AppendLine("            <Element Offset=\"0\" Vartype=\"4 Bytes\" Bytesize=\"4\" OffsetHex=\"00000000\" Description=\"dummy\" DisplayMethod=\"hexadecimal\"/>");
        sb.AppendLine("          </Elements>");
        sb.AppendLine("        </Structure>");
        return sb.ToString();
    }

    /// <summary>
    /// Build a child Structure for StrProperty (pointer → Unicode String).
    /// </summary>
    private static string BuildStrChildStructure(string? addr)
    {
        var name = FormatStructName(addr);
        var sb = new StringBuilder();
        sb.Append("        <Structure Name=\"").Append(EscapeXml(name))
          .AppendLine("\" AutoFill=\"0\" AutoCreate=\"1\" DefaultHex=\"0\" AutoDestroy=\"0\" DoNotSaveLocal=\"0\" RLECompression=\"1\" AutoCreateStructsize=\"4096\">");
        sb.AppendLine("          <Elements>");
        sb.AppendLine("            <Element Offset=\"0\" Vartype=\"Unicode String\" Bytesize=\"18\" OffsetHex=\"00000000\" DisplayMethod=\"unsigned integer\"/>");
        sb.AppendLine("          </Elements>");
        sb.AppendLine("        </Structure>");
        return sb.ToString();
    }

    /// <summary>
    /// Format a hex address into a CE-style structure name.
    /// </summary>
    private static string FormatStructName(string? addr)
    {
        if (string.IsNullOrEmpty(addr)) return "Autocreated";
        // Strip "0x" prefix if present, uppercase
        var clean = addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? addr[2..] : addr;
        // Remove leading zeros from hex for cleaner display
        clean = clean.TrimStart('0');
        if (clean.Length == 0) clean = "0";
        return $"Autocreated from {clean}";
    }

    /// <summary>
    /// Escape special XML characters.
    /// </summary>
    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
