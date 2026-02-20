using System.Text;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Generates Cheat Engine XML address records from CE pointer chain info.
/// </summary>
public static class CeXmlExportService
{
    /// <summary>
    /// Generate a CE XML entry for a single field of a UObject instance.
    /// </summary>
    public static string GenerateFieldEntry(
        CePointerInfo ceInfo,
        LiveFieldValue field,
        string objectName,
        string className)
    {
        var ceType = MapCeType(field.TypeName, field.Size);
        var description = $"{className}.{field.Name}";

        // Build offsets: replace the innermost (field) offset with this field's offset
        var offsets = (int[])ceInfo.CeOffsets.Clone();
        if (offsets.Length > 0) offsets[0] = field.Offset;

        return GenerateEntry(ceInfo.CeBase, description, ceType, offsets);
    }

    /// <summary>
    /// Generate CE XML for all scalar fields of an instance.
    /// </summary>
    public static string GenerateInstanceXml(
        CePointerInfo ceInfo,
        InstanceWalkResult instance)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<CheatTable>");
        sb.AppendLine("  <CheatEntries>");

        // Group header
        sb.AppendLine($"    <CheatEntry>");
        sb.AppendLine($"      <ID>0</ID>");
        sb.AppendLine($"      <Description>\"{instance.ClassName}: {instance.Name}\"</Description>");
        sb.AppendLine($"      <GroupHeader>1</GroupHeader>");
        sb.AppendLine($"      <CheatEntries>");

        int entryId = 1;
        foreach (var field in instance.Fields)
        {
            // Only export scalar fields with known types
            var ceType = MapCeType(field.TypeName, field.Size);
            if (ceType == "Byte") continue; // Skip unknown/struct types

            var offsets = (int[])ceInfo.CeOffsets.Clone();
            if (offsets.Length > 0) offsets[0] = field.Offset;

            sb.AppendLine($"        <CheatEntry>");
            sb.AppendLine($"          <ID>{entryId++}</ID>");
            sb.AppendLine($"          <Description>\"{field.Name}\"</Description>");
            sb.AppendLine($"          <VariableType>{ceType}</VariableType>");
            sb.AppendLine($"          <Address>{ceInfo.CeBase}</Address>");
            sb.AppendLine($"          <Offsets>");
            foreach (var offset in offsets)
            {
                sb.AppendLine($"            <Offset>{offset:X}</Offset>");
            }
            sb.AppendLine($"          </Offsets>");
            sb.AppendLine($"        </CheatEntry>");
        }

        sb.AppendLine($"      </CheatEntries>");
        sb.AppendLine($"    </CheatEntry>");
        sb.AppendLine("  </CheatEntries>");
        sb.AppendLine("</CheatTable>");

        return sb.ToString();
    }

    /// <summary>
    /// Generate a CE-compatible XML with an AutoAssembler script that registers a symbol
    /// for a static pointer address. Output format:
    /// define(symbolName, "MODULE.EXE"+RVA) / registersymbol(symbolName)
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

        // Enable script
        sb.AppendLine("[ENABLE]");
        sb.AppendLine($"define({symbolName},\"{moduleName}\"+{rva:X})");
        sb.AppendLine($"registersymbol({symbolName})");
        sb.AppendLine();

        // Disable script
        sb.AppendLine("[DISABLE]");
        sb.AppendLine($"unregistersymbol({symbolName})");

        sb.AppendLine($"      </AssemblerScript>");
        sb.AppendLine($"    </CheatEntry>");
        sb.AppendLine("  </CheatEntries>");
        sb.AppendLine("</CheatTable>");

        return sb.ToString();
    }

    private static string GenerateEntry(string ceBase, string description, string ceType, int[] offsets)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<CheatEntry>");
        sb.AppendLine($"  <Description>\"{description}\"</Description>");
        sb.AppendLine($"  <VariableType>{ceType}</VariableType>");
        sb.AppendLine($"  <Address>{ceBase}</Address>");
        sb.AppendLine($"  <Offsets>");
        foreach (var offset in offsets)
        {
            sb.AppendLine($"    <Offset>{offset:X}</Offset>");
        }
        sb.AppendLine($"  </Offsets>");
        sb.AppendLine($"</CheatEntry>");
        return sb.ToString();
    }

    private static string MapCeType(string ueType, int size)
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
            _ => "Byte" // Unknown
        };
    }
}
