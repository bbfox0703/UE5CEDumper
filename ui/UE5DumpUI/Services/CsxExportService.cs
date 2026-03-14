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
/// - Single-layer: StructProperty fields are flattened inline; pointer targets have no child (CE native deref)
///
/// Drilldown (depth ≥ 1): ObjectProperty targets with valid PtrAddress are walked via
/// WalkInstanceAsync, producing real child structures with actual field definitions.
/// Container types (MapProperty, ArrayProperty, SetProperty) expand inline elements into
/// child structures from pre-fetched element data (MapElements/ArrayElements/SetElements).
/// Each depth level recursively resolves nested pointers, enabling multi-level expansion.
/// </summary>
public static class CsxExportService
{
    // CSX type descriptor
    private record CsxTypeInfo(string Vartype, int Bytesize, string DisplayMethod);

    /// <summary>
    /// Generate CSX XML from the current Live Walker fields.
    /// StructProperty fields are resolved and flattened inline.
    /// When drilldownDepth &gt; 0, ObjectProperty targets with valid PtrAddress
    /// are walked via WalkInstanceAsync to produce real child structures with actual fields.
    /// Each level decrements depth, enabling multi-level recursive expansion.
    /// </summary>
    public static async Task<string> GenerateCsxAsync(
        IDumpService dump,
        string structName,
        IReadOnlyList<LiveFieldValue> fields,
        int arrayLimit = 64,
        int drilldownDepth = 0)
    {
        // Resolve StructProperty inner fields via DLL (reuse existing logic)
        var resolvedStructs = await CeXmlExportService.ResolveStructFieldsAsync(
            dump, fields, arrayLimit);

        // Pre-resolve pointer target instances for drilldown (recursive up to drilldownDepth)
        var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>();
        if (drilldownDepth > 0)
        {
            var visited = new HashSet<string>();
            await ResolvePointerInstancesAsync(dump, fields, resolvedInstances, drilldownDepth, arrayLimit, visited);
            // Also resolve pointer instances within flattened struct fields
            foreach (var innerFields in resolvedStructs.Values)
                await ResolvePointerInstancesAsync(dump, innerFields, resolvedInstances, drilldownDepth, arrayLimit, visited);
            // Resolve pointer targets within container elements (Map/Array/Set)
            await ResolveContainerPointerInstancesAsync(dump, fields, resolvedInstances, drilldownDepth, arrayLimit, visited);
            foreach (var innerFields in resolvedStructs.Values)
                await ResolveContainerPointerInstancesAsync(dump, innerFields, resolvedInstances, drilldownDepth, arrayLimit, visited);
        }

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("<Structures>");
        sb.Append("  <Structure Name=\"").Append(EscapeXml(structName))
          .AppendLine("\" AutoFill=\"0\" AutoCreate=\"1\" DefaultHex=\"0\" AutoDestroy=\"0\" DoNotSaveLocal=\"0\" RLECompression=\"1\" AutoCreateStructsize=\"4096\">");
        sb.AppendLine("    <Elements>");

        foreach (var field in fields)
        {
            if (field.IsGuessed) continue;

            if (field.TypeName == "StructProperty")
            {
                // Flatten struct fields inline with "StructType / FieldName" naming
                EmitStructPropertyFlattened(sb, field, resolvedStructs, resolvedInstances, drilldownDepth);
            }
            else
            {
                EmitElement(sb, field.Offset, field.Name, field, "      ", resolvedInstances, drilldownDepth);
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
        Dictionary<int, List<LiveFieldValue>> resolvedStructs,
        Dictionary<string, List<LiveFieldValue>>? resolvedInstances = null,
        int drilldownDepth = 0)
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
                EmitElement(sb, absoluteOffset, description, inner, "      ", resolvedInstances, drilldownDepth);
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
    /// For ObjectProperty with drilldownDepth &gt; 0, uses resolved live instance data
    /// to build real child structures with actual fields, decrementing depth for recursion.
    /// </summary>
    private static void EmitElement(StringBuilder sb, int offset, string description,
        LiveFieldValue field, string indent,
        Dictionary<string, List<LiveFieldValue>>? resolvedInstances = null,
        int drilldownDepth = 0)
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
                // Drilldown: use real child structure if instance was resolved
                if (drilldownDepth > 0
                    && !string.IsNullOrEmpty(field.PtrAddress)
                    && field.PtrAddress != "0x0"
                    && resolvedInstances != null
                    && resolvedInstances.TryGetValue(field.PtrAddress, out var instanceFields))
                {
                    childStructure = BuildLiveChildStructure(
                        field.PtrClassName, instanceFields, resolvedInstances, drilldownDepth - 1);
                }
                // No dummy — CE handles native pointer dereference
                break;

            case "MapProperty":
            case "ArrayProperty":
            case "SetProperty":
            case "DataTableRows":
                // Drilldown: convert container elements to synthetic fields for child structure
                if (drilldownDepth > 0
                    && resolvedInstances != null)
                {
                    var containerFields = ConvertContainerElementsToFields(field);
                    if (containerFields is { Count: > 0 })
                    {
                        childStructure = BuildLiveChildStructure(
                            field.Name, containerFields, resolvedInstances, drilldownDepth - 1);
                    }
                }
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
            "DataTableRows"         => new CsxTypeInfo("Pointer", 8, "unsigned integer"),

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
    /// Build a real child Structure from resolved live instance fields.
    /// Each field becomes a CSX Element with proper type mapping, and nested
    /// ObjectProperty fields can recurse with decremented remainingDepth.
    /// </summary>
    private static string BuildLiveChildStructure(
        string? structName,
        IReadOnlyList<LiveFieldValue> fields,
        Dictionary<string, List<LiveFieldValue>> resolvedInstances,
        int remainingDepth)
    {
        var name = !string.IsNullOrEmpty(structName) ? structName : "Unknown";

        var sb = new StringBuilder();
        sb.Append("        <Structure Name=\"").Append(EscapeXml(name))
          .AppendLine("\" AutoFill=\"0\" AutoCreate=\"1\" DefaultHex=\"0\" AutoDestroy=\"0\" DoNotSaveLocal=\"0\" RLECompression=\"1\" AutoCreateStructsize=\"4096\">");
        sb.AppendLine("          <Elements>");

        if (fields.Count > 0)
        {
            foreach (var field in fields)
                EmitElement(sb, field.Offset, field.Name, field, "            ", resolvedInstances, remainingDepth);
        }
        else
        {
            sb.AppendLine("            <Element Offset=\"0\" Vartype=\"4 Bytes\" Bytesize=\"4\" OffsetHex=\"00000000\" Description=\"empty\" DisplayMethod=\"hexadecimal\"/>");
        }

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
    /// Pre-resolve pointer target instances for ObjectProperty drilldown.
    /// Recursively calls WalkInstanceAsync for each unique PtrAddress found in the fields,
    /// decrementing remainingDepth at each level. Uses visited set for cycle detection.
    /// </summary>
    private static async Task ResolvePointerInstancesAsync(
        IDumpService dump,
        IReadOnlyList<LiveFieldValue> fields,
        Dictionary<string, List<LiveFieldValue>> resolved,
        int remainingDepth,
        int arrayLimit,
        HashSet<string> visited)
    {
        if (remainingDepth <= 0) return;

        foreach (var field in fields)
        {
            if (!IsObjectPropertyType(field.TypeName)) continue;
            if (string.IsNullOrEmpty(field.PtrAddress) || field.PtrAddress == "0x0") continue;
            if (resolved.ContainsKey(field.PtrAddress)) continue;
            if (!visited.Add(field.PtrAddress)) continue; // cycle detection

            try
            {
                var result = await dump.WalkInstanceAsync(field.PtrAddress, field.PtrClassAddr, arrayLimit);
                if (result.Fields.Count > 0)
                {
                    resolved[field.PtrAddress] = result.Fields;
                    // Recurse deeper for nested pointers
                    await ResolvePointerInstancesAsync(
                        dump, result.Fields, resolved, remainingDepth - 1, arrayLimit, visited);
                }
            }
            catch
            {
                // Skip on pipe error — pointer will have no child structure
            }
        }
    }

    /// <summary>
    /// Check if a type name is an object/pointer property that can have a PropertyClass.
    /// </summary>
    private static bool IsObjectPropertyType(string typeName) => typeName is
        "ObjectProperty" or "ClassProperty" or "SoftObjectProperty" or
        "SoftClassProperty" or "LazyObjectProperty" or "InterfaceProperty";

    /// <summary>
    /// Resolve pointer targets within container elements (MapProperty, ArrayProperty, SetProperty).
    /// Container expansion consumes one depth level: map/array elements appear at depth-1,
    /// then their ObjectProperty targets are resolved at depth-2 via ResolvePointerInstancesAsync.
    /// </summary>
    private static async Task ResolveContainerPointerInstancesAsync(
        IDumpService dump,
        IReadOnlyList<LiveFieldValue> fields,
        Dictionary<string, List<LiveFieldValue>> resolved,
        int remainingDepth,
        int arrayLimit,
        HashSet<string> visited)
    {
        if (remainingDepth <= 0) return;

        foreach (var field in fields)
        {
            var containerFields = ConvertContainerElementsToFields(field);
            if (containerFields == null || containerFields.Count == 0) continue;

            // Container expansion uses one depth level; resolve inner pointers at depth-1
            await ResolvePointerInstancesAsync(
                dump, containerFields, resolved, remainingDepth - 1, arrayLimit, visited);
        }
    }

    /// <summary>
    /// Convert container elements (Map/Array/Set) to synthetic LiveFieldValue list
    /// for CSX child structure generation. Returns null if the field has no expandable elements.
    /// Handles all inner types: ObjectProperty (pointers), StructProperty (flattened sub-fields),
    /// and scalar types (Int, Float, Byte, Enum, etc.).
    /// </summary>
    private static List<LiveFieldValue>? ConvertContainerElementsToFields(LiveFieldValue field)
    {
        return field.TypeName switch
        {
            "MapProperty" when field.MapElements is { Count: > 0 }
                => ConvertMapElementsToFields(field),
            "ArrayProperty" when field.ArrayElements is { Count: > 0 }
                => ConvertArrayElementsToFields(field),
            "SetProperty" when field.SetElements is { Count: > 0 }
                => ConvertSetElementsToFields(field),
            "DataTableRows" when field.DataTableRowData is { Count: > 0 }
                => ConvertDataTableRowsToFields(field),
            _ => null,
        };
    }

    /// <summary>
    /// Dispatch ArrayProperty element conversion based on inner type.
    /// </summary>
    private static List<LiveFieldValue> ConvertArrayElementsToFields(LiveFieldValue arrField)
    {
        if (IsObjectPropertyType(arrField.ArrayInnerType))
            return ConvertArrayPointerElementsToFields(arrField);

        // Struct arrays → flatten sub-fields per element (or raw blocks if no Phase F data)
        if (arrField.ArrayInnerType == "StructProperty")
            return ConvertArrayStructElementsToFields(arrField);

        // Scalar/enum/name arrays → one entry per element
        return ConvertArrayScalarElementsToFields(arrField);
    }

    /// <summary>
    /// Dispatch SetProperty element conversion based on element type.
    /// </summary>
    private static List<LiveFieldValue> ConvertSetElementsToFields(LiveFieldValue setField)
    {
        if (IsObjectPropertyType(setField.SetElemType))
            return ConvertSetPointerElementsToFields(setField);

        return ConvertSetScalarElementsToFields(setField);
    }

    /// <summary>
    /// Convert MapProperty elements to synthetic LiveFieldValue list.
    /// Each map entry is represented by its VALUE at offset (sparseIndex * stride + valOffset).
    /// TMap.Data is TSparseArray: stride = AlignUp(valOffset + valueSize, 4) + 8 (hash overhead).
    /// valOffset = aligned byte offset of value within TPair (may differ from keySize).
    /// </summary>
    private static List<LiveFieldValue> ConvertMapElementsToFields(LiveFieldValue mapField)
    {
        // Use aligned value offset if available; fall back to key size
        int valOffset = mapField.MapValueOffset > 0 ? mapField.MapValueOffset : mapField.MapKeySize;
        int pairSize = valOffset + mapField.MapValueSize;
        int stride = ComputeSetElementStride(pairSize);
        var fields = new List<LiveFieldValue>();

        foreach (var elem in mapField.MapElements!)
        {
            var keyDisplay = !string.IsNullOrEmpty(elem.KeyPtrName) ? elem.KeyPtrName : elem.Key;
            var name = $"[{elem.Index}] {keyDisplay}";
            int offset = elem.Index * stride + valOffset;

            fields.Add(new LiveFieldValue
            {
                Name = name,
                TypeName = mapField.MapValueType,
                Offset = offset,
                Size = mapField.MapValueSize,
                PtrAddress = elem.ValuePtrAddress,
                PtrName = elem.ValuePtrName,
                PtrClassName = elem.ValuePtrClassName,
            });
        }

        return fields;
    }

    /// <summary>
    /// Convert ArrayProperty pointer elements to synthetic LiveFieldValue list.
    /// Each element is an ObjectProperty pointer at offset (index * elemSize).
    /// </summary>
    private static List<LiveFieldValue> ConvertArrayPointerElementsToFields(LiveFieldValue arrField)
    {
        int elemSize = arrField.ArrayElemSize > 0 ? arrField.ArrayElemSize : 8;
        var fields = new List<LiveFieldValue>();

        foreach (var elem in arrField.ArrayElements!)
        {
            var name = !string.IsNullOrEmpty(elem.PtrName)
                ? $"[{elem.Index}] {elem.PtrName}"
                : $"[{elem.Index}]";
            int offset = elem.Index * elemSize;

            fields.Add(new LiveFieldValue
            {
                Name = name,
                TypeName = arrField.ArrayInnerType,
                Offset = offset,
                Size = elemSize,
                PtrAddress = elem.PtrAddress,
                PtrName = elem.PtrName,
                PtrClassName = elem.PtrClassName,
            });
        }

        return fields;
    }

    /// <summary>
    /// Convert SetProperty pointer elements to synthetic LiveFieldValue list.
    /// Each element is at offset (sparseIndex * stride) within the TSparseArray data buffer.
    /// </summary>
    private static List<LiveFieldValue> ConvertSetPointerElementsToFields(LiveFieldValue setField)
    {
        int stride = ComputeSetElementStride(setField.SetElemSize);
        var fields = new List<LiveFieldValue>();

        foreach (var elem in setField.SetElements!)
        {
            var name = !string.IsNullOrEmpty(elem.KeyPtrName)
                ? $"[{elem.Index}] {elem.KeyPtrName}"
                : $"[{elem.Index}]";
            int offset = elem.Index * stride;

            fields.Add(new LiveFieldValue
            {
                Name = name,
                TypeName = setField.SetElemType,
                Offset = offset,
                Size = setField.SetElemSize,
                PtrAddress = elem.KeyPtrAddress,
                PtrName = elem.KeyPtrName,
                PtrClassName = elem.KeyPtrClassName,
            });
        }

        return fields;
    }

    /// <summary>
    /// Convert ArrayProperty struct elements to flattened synthetic LiveFieldValue list.
    /// Each element's sub-fields (from Phase F StructSubFieldValue data) are inlined with
    /// "[index] / SubFieldName" naming and absolute offsets within TArray.Data.
    /// </summary>
    private static List<LiveFieldValue> ConvertArrayStructElementsToFields(LiveFieldValue arrField)
    {
        int elemSize = arrField.ArrayElemSize > 0 ? arrField.ArrayElemSize : 1;
        var structType = !string.IsNullOrEmpty(arrField.ArrayStructType)
            ? arrField.ArrayStructType : "Struct";
        var fields = new List<LiveFieldValue>();

        foreach (var elem in arrField.ArrayElements!)
        {
            if (elem.StructFields is not { Count: > 0 })
            {
                // No sub-fields available — emit entire element as raw bytes block
                fields.Add(new LiveFieldValue
                {
                    Name = $"[{elem.Index}] {structType}",
                    TypeName = structType, // Falls through to Array of byte in MapCsxType
                    Offset = elem.Index * elemSize,
                    Size = elemSize,
                });
                continue;
            }

            foreach (var sub in elem.StructFields)
            {
                int absoluteOffset = elem.Index * elemSize + sub.Offset;
                fields.Add(new LiveFieldValue
                {
                    Name = $"[{elem.Index}] / {sub.Name}",
                    TypeName = sub.TypeName,
                    Offset = absoluteOffset,
                    Size = sub.Size,
                    // Propagate pointer info for ObjectProperty sub-fields
                    PtrAddress = sub.PtrAddress,
                    PtrName = sub.PtrName,
                    PtrClassName = sub.PtrClassName,
                    PtrClassAddr = sub.PtrClassAddr,
                });
            }
        }

        return fields;
    }

    /// <summary>
    /// Convert ArrayProperty scalar/enum elements to synthetic LiveFieldValue list.
    /// Each element is a single typed entry at offset (index × elemSize).
    /// </summary>
    private static List<LiveFieldValue> ConvertArrayScalarElementsToFields(LiveFieldValue arrField)
    {
        int elemSize = arrField.ArrayElemSize > 0 ? arrField.ArrayElemSize : 1;
        var fields = new List<LiveFieldValue>();

        foreach (var elem in arrField.ArrayElements!)
        {
            // Build descriptive name: prefer enum name, then short value, then bare index
            var label = !string.IsNullOrEmpty(elem.EnumName) ? elem.EnumName
                : !string.IsNullOrEmpty(elem.Value) && elem.Value.Length <= 20 ? elem.Value
                : null;
            var name = label != null ? $"[{elem.Index}] {label}" : $"[{elem.Index}]";

            fields.Add(new LiveFieldValue
            {
                Name = name,
                TypeName = arrField.ArrayInnerType,
                Offset = elem.Index * elemSize,
                Size = elemSize,
            });
        }

        return fields;
    }

    /// <summary>
    /// Convert SetProperty non-pointer elements to synthetic LiveFieldValue list.
    /// Each element is at offset (sparseIndex × stride) within the TSparseArray data buffer.
    /// </summary>
    private static List<LiveFieldValue> ConvertSetScalarElementsToFields(LiveFieldValue setField)
    {
        int stride = ComputeSetElementStride(setField.SetElemSize);
        var fields = new List<LiveFieldValue>();

        foreach (var elem in setField.SetElements!)
        {
            var name = !string.IsNullOrEmpty(elem.Key)
                ? $"[{elem.Index}] {elem.Key}"
                : $"[{elem.Index}]";

            fields.Add(new LiveFieldValue
            {
                Name = name,
                TypeName = setField.SetElemType,
                Offset = elem.Index * stride,
                Size = setField.SetElemSize,
            });
        }

        return fields;
    }

    /// <summary>
    /// Convert DataTable RowMap rows to synthetic LiveFieldValue list for CSX drilldown.
    /// Each row is represented as a pointer (uint8*) at offset (sparseIndex * stride + fnameSize)
    /// within the TSparseArray data buffer. CSX drilldown walks each row's data buffer as a struct.
    /// </summary>
    private static List<LiveFieldValue> ConvertDataTableRowsToFields(LiveFieldValue field)
    {
        var fields = new List<LiveFieldValue>();

        foreach (var row in field.DataTableRowData!)
        {
            int offset = row.SparseIndex * field.DataTableStride + field.DataTableFNameSize;

            fields.Add(new LiveFieldValue
            {
                Name = $"[{row.SparseIndex}] {row.RowName}",
                TypeName = "ObjectProperty", // treated as pointer for CSX drilldown
                Offset = offset,
                Size = 8,
                PtrAddress = row.DataAddr,
                PtrName = row.RowName,
                PtrClassName = field.DataTableStructName,
                PtrClassAddr = field.DataTableRowStructAddr,
            });
        }

        return fields;
    }

    /// <summary>
    /// Compute TSparseArray element stride: AlignUp(elemSize, 4) + 8 (HashNextId + HashIndex).
    /// Mirrors Mem::ComputeSetElementStride in the DLL.
    /// </summary>
    private static int ComputeSetElementStride(int elemSize)
    {
        int hashStart = (elemSize + 3) & ~3; // align to 4
        return hashStart + 8; // + HashNextId(4) + HashIndex(4)
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
