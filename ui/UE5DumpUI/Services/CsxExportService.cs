using System.Text;
using System.Threading;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// CSX output format version. CE before 7.7 has no bit-switch type, so a bit-field bool is
/// emitted as a whole Byte with the bit index noted in the description (a series of same-address
/// bytes). CE 7.7+ supports Vartype="Binary" (BitStart/BitSize), so a bit-field bool becomes a
/// real single-bit element. Copy CE XML / Copy CE Field already emit Binary; this brings CSX in line.
/// </summary>
public enum CsxFormat
{
    /// <summary>Legacy: bit-field bools -> Byte + "(bit N, mask 0xNN)" appended to the description.</summary>
    PreCe77,
    /// <summary>CE 7.7+: bit-field bools -> Vartype="Binary" with BitStart/BitSize.</summary>
    Ce77Plus,
}

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
        int drilldownDepth = 0,
        CsxFormat format = CsxFormat.PreCe77,
        CancellationToken ct = default)
    {
        // Unified drilldown resolve (docs/ce-export-drilldown-spec.md Phase B): structs
        // (flatten, depth-free) + pointers + CONTAINER ELEMENT VALUES that are structs
        // (Map&lt;…,Struct&gt; / Set&lt;Struct&gt; / struct-array), recursively to drilldownDepth.
        // Shared with Copy CE XML — one resolver, see CeXmlExportService.ResolveDrilldownAsync.
        // resolvedStructs is keyed by StructDataAddr; resolvedInstances by PtrAddress — the
        // same keys the CSX emit phase looks up.
        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        await CeXmlExportService.ResolveDrilldownAsync(
            dump, fields, resolvedStructs, resolvedInstances, drilldownDepth, arrayLimit, ct: ct);

        // CSX additionally drills OBJECT pointers held in object-arrays / DataTable rows /
        // multicast delegates — container shapes the unified resolver doesn't descend (CE XML
        // emits those as flat leaves; CSX builds real child structures). The shared
        // resolvedInstances dict dedupes any address the unified pass already walked.
        if (drilldownDepth > 0)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            await ResolvePointerInstancesAsync(dump, fields, resolvedInstances, drilldownDepth, arrayLimit, visited, ct);
            // Also resolve pointer instances within flattened struct fields
            foreach (var innerFields in resolvedStructs.Values)
                await ResolvePointerInstancesAsync(dump, innerFields, resolvedInstances, drilldownDepth, arrayLimit, visited, ct);
            // Resolve pointer targets within container elements (Map/Array/Set/DataTable)
            await ResolveContainerPointerInstancesAsync(dump, fields, resolvedInstances, drilldownDepth, arrayLimit, visited, ct);
            foreach (var innerFields in resolvedStructs.Values)
                await ResolveContainerPointerInstancesAsync(dump, innerFields, resolvedInstances, drilldownDepth, arrayLimit, visited, ct);
        }

        var sb = new StringBuilder();
        sb.AppendLine();
        // Embed a best-effort note when the game uses the UE5.7+ UNVERIFIED packed layout.
        if (PackedLayoutNotice.IsActive)
            sb.Append(PackedLayoutNotice.XmlComment);
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
                EmitStructPropertyFlattened(sb, field, resolvedStructs, resolvedInstances, drilldownDepth, "      ", format);
            }
            else
            {
                EmitElement(sb, field.Offset, field.Name, field, "      ", resolvedStructs, resolvedInstances, drilldownDepth, format);
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
        Dictionary<string, List<LiveFieldValue>>? resolvedStructs,
        Dictionary<string, List<LiveFieldValue>>? resolvedInstances,
        int drilldownDepth,
        string indent,
        CsxFormat format)
    {
        // Prefix names each flattened sub-field. For a regular StructProperty this is
        // the struct type (e.g. "FVector / X"); for a synthetic container element value
        // (Map&lt;…,Struct&gt; / Set&lt;Struct&gt;) the field Name already encodes the element
        // (e.g. "[0] structure"), so the sub-fields read "[0] structure / SubField".
        var prefix = !string.IsNullOrEmpty(structField.StructTypeName)
            ? structField.StructTypeName
            : structField.Name;

        // Lookup by StructDataAddr (matches the unified resolver's string-keyed result —
        // unique across instances so the same dict serves top-level struct fields,
        // container element struct values, and struct fields inside drilled targets).
        if (!string.IsNullOrEmpty(structField.StructDataAddr)
            && resolvedStructs != null
            && resolvedStructs.TryGetValue(structField.StructDataAddr, out var innerFields)
            && innerFields.Count > 0)
        {
            foreach (var inner in innerFields)
            {
                var absoluteOffset = structField.Offset + inner.Offset;
                var description = $"{prefix} / {inner.Name}";
                EmitElement(sb, absoluteOffset, description, inner, indent, resolvedStructs, resolvedInstances, drilldownDepth, format);
            }
        }
        else
        {
            // Unresolved struct (depth exhausted / walk failed) — emit as raw bytes block.
            var typeInfo = new CsxTypeInfo("Array of byte",
                structField.Size > 0 ? structField.Size : 8, "hexadecimal");
            EmitElementRaw(sb, structField.Offset, structField.Name, typeInfo, null, indent);
        }
    }

    /// <summary>
    /// Emit a single &lt;Element&gt; for a field.
    /// For ObjectProperty with drilldownDepth &gt; 0, uses resolved live instance data
    /// to build real child structures with actual fields, decrementing depth for recursion.
    /// </summary>
    private static void EmitElement(StringBuilder sb, int offset, string description,
        LiveFieldValue field, string indent,
        Dictionary<string, List<LiveFieldValue>>? resolvedStructs = null,
        Dictionary<string, List<LiveFieldValue>>? resolvedInstances = null,
        int drilldownDepth = 0,
        CsxFormat format = CsxFormat.PreCe77)
    {
        // CE 7.7+ : a bit-field BoolProperty becomes a real Binary element (BitStart/BitSize)
        // instead of the Pre-7.7 "Byte + (bit N, mask) in description" workaround. Only bit-packed
        // bools (BoolBitIndex >= 0) qualify; whole-byte bools (BoolBitIndex == -1, mask 0xFF) keep
        // the Byte path below. The byte address uses the `offset` PARAMETER (already absolute for
        // flattened-struct / container-element fields — field.Offset would be struct-relative) plus
        // BoolByteOffset (the byte within the field holding the bit), matching the DLL read/write
        // path (Ubel/Solitar/Wirbel all read/write at base + Offset + ByteOffset).
        if (format == CsxFormat.Ce77Plus
            && field.TypeName == "BoolProperty"
            && field.BoolBitIndex >= 0)
        {
            EmitBinaryBitfieldElement(sb, offset + field.BoolByteOffset, description, field.BoolBitIndex, indent);
            return;
        }

        var typeInfo = MapCsxType(field.TypeName, field.Size);
        string? childStructure = null;

        // Pre-7.7 BoolProperty with bitmask: CSX has no bitmask type,
        // so we append bit info to the description for the user.
        if (field.TypeName == "BoolProperty" && field.BoolBitIndex >= 0)
        {
            description = $"{description} (bit {field.BoolBitIndex}, mask 0x{field.BoolFieldMask:X2})";
        }

        // Determine if this needs a child structure
        switch (field.TypeName)
        {
            case "StrProperty":
            case "Utf8StrProperty":   // UE5.5+ FUtf8String (1-byte UTF-8)
            case "AnsiStrProperty":   // UE5.5+ FAnsiString (1-byte ANSI)
                childStructure = BuildStrChildStructure(field.TypeName, field.HexValue);
                break;

            // Audit #5 W9: Soft / SoftClass / Lazy were here too. A child <Structure>
            // is only reachable in a dissect file by dereferencing the parent element,
            // and those slots hold an FSoftObjectPath / FWeakObjectPtr rather than an
            // address — so the child was laid out under a slot CE cannot follow. The
            // DLL does resolve their embedded target and stamps it on PtrAddress, which
            // is exactly what made the TryGetValue guard below succeed and the defect
            // invisible. Same reasoning, same three types, as W5 in the CE-XML emitter.
            case "ObjectProperty":
            case "ClassProperty":
            case "InterfaceProperty":
                // Drilldown: use real child structure if instance was resolved
                if (drilldownDepth > 0
                    && !string.IsNullOrEmpty(field.PtrAddress)
                    && field.PtrAddress != "0x0"
                    && resolvedInstances != null
                    && resolvedInstances.TryGetValue(field.PtrAddress, out var instanceFields))
                {
                    childStructure = BuildLiveChildStructure(
                        field.PtrClassName, instanceFields, resolvedStructs, resolvedInstances, drilldownDepth - 1, format);
                }
                // No dummy — CE handles native pointer dereference
                break;

            case "MapProperty":
            case "ArrayProperty":
            case "SetProperty":
            case "DataTableRows":
            // Multicast delegates expose an implicit DelegateProperty array
            // via ArrayCount/Inner; treat them like ArrayProperty for drill-down.
            case "MulticastInlineDelegateProperty":
            case "MulticastDelegateProperty":
                // Drilldown: convert container elements to synthetic fields for child structure
                if (drilldownDepth > 0
                    && resolvedInstances != null)
                {
                    var containerFields = ConvertContainerElementsToFields(field);
                    if (containerFields is { Count: > 0 })
                    {
                        childStructure = BuildLiveChildStructure(
                            field.Name, containerFields, resolvedStructs, resolvedInstances, drilldownDepth - 1, format);
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
    /// Emit a CE 7.7+ Binary &lt;Element&gt; for a single bit-field bool. Attribute order matches
    /// CE's Structure Dissect output (Offset, BitSize, Vartype, BitStart, Bytesize, OffsetHex,
    /// Description, DisplayMethod). BitSize is always 1 (a UE FBoolProperty is single-bit by
    /// construction — FieldMask is a validated power-of-2); Bytesize is 1 (the Binary view spans
    /// the one byte at <paramref name="byteOffset"/>). Self-closing — bit-field bools never carry a
    /// child structure. <paramref name="byteOffset"/> is the ABSOLUTE byte address (caller already
    /// added BoolByteOffset), used for both the decimal Offset and the hex OffsetHex.
    /// </summary>
    private static void EmitBinaryBitfieldElement(StringBuilder sb, int byteOffset,
        string description, int bitStart, string indent)
    {
        sb.Append(indent)
          .Append("<Element Offset=\"").Append(byteOffset).Append('"')
          .Append(" BitSize=\"1\"")
          .Append(" Vartype=\"Binary\"")
          .Append(" BitStart=\"").Append(bitStart).Append('"')
          .Append(" Bytesize=\"1\"")
          .Append(" OffsetHex=\"").Append(byteOffset.ToString("X8")).Append('"')
          .Append(" Description=\"").Append(EscapeXml(description)).Append('"')
          .AppendLine(" DisplayMethod=\"unsigned integer\"/>");
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
            "Utf8StrProperty"       => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "AnsiStrProperty"       => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "ObjectProperty"        => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "ClassProperty"         => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            // InterfaceProperty IS a pointer slot: FScriptInterface is
            // { UObject* +0x00; void* +0x08 }, so its first 8 bytes are a real address.
            "InterfaceProperty"     => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "ArrayProperty"         => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "MapProperty"           => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "SetProperty"           => new CsxTypeInfo("Pointer", 8, "unsigned integer"),
            "DataTableRows"         => new CsxTypeInfo("Pointer", 8, "unsigned integer"),

            // Opaque types — 8-byte hex leaves, NOT Vartype=Pointer.
            // Audit #5 W9: Soft / SoftClass / Lazy sat in the pointer block above, so
            // CSX told CE to dereference a slot that holds no address —
            // FSoftObjectPath for the soft pair, FWeakObjectPtr { int32 ObjectIndex;
            // int32 SerialNumber } for Lazy. This is exactly what W5 fixed in the
            // CE-XML emitter (see CeXmlExportService.IsRawObjectPtrSlot and its doc
            // block); that commit did not touch this file. WeakObjectProperty was
            // already correct here, which is why the defect never looked systematic.
            // A watchable 8-byte hex leaf is the honest representation for all four.
            "TextProperty"         => new CsxTypeInfo("8 Bytes", 8, "hexadecimal"),
            "WeakObjectProperty"   => new CsxTypeInfo("8 Bytes", 8, "hexadecimal"),
            "SoftObjectProperty"   => new CsxTypeInfo("8 Bytes", 8, "hexadecimal"),
            "SoftClassProperty"    => new CsxTypeInfo("8 Bytes", 8, "hexadecimal"),
            "LazyObjectProperty"   => new CsxTypeInfo("8 Bytes", 8, "hexadecimal"),
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
    /// Build a child Structure for an FString-family pointer (Data ptr → string buffer).
    /// FString uses the wide "Unicode String"; FUtf8String/FAnsiString are 1-byte "String".
    /// CE's Structure Dissect has only these two string Vartypes (no CodePage option), so a
    /// UTF-8 FUtf8String falls back to "String" — CE renders it byte-wise and cannot decode
    /// multibyte UTF-8 (a CSX format limitation; CE XML's CodePage flag has no CSX equivalent).
    /// </summary>
    private static string BuildStrChildStructure(string typeName, string? addr)
    {
        var vartype = typeName == "StrProperty" ? "Unicode String" : "String";
        var name = FormatStructName(addr);
        var sb = new StringBuilder();
        sb.Append("        <Structure Name=\"").Append(EscapeXml(name))
          .AppendLine("\" AutoFill=\"0\" AutoCreate=\"1\" DefaultHex=\"0\" AutoDestroy=\"0\" DoNotSaveLocal=\"0\" RLECompression=\"1\" AutoCreateStructsize=\"4096\">");
        sb.AppendLine("          <Elements>");
        sb.AppendLine($"            <Element Offset=\"0\" Vartype=\"{vartype}\" Bytesize=\"18\" OffsetHex=\"00000000\" DisplayMethod=\"unsigned integer\"/>");
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
        Dictionary<string, List<LiveFieldValue>>? resolvedStructs,
        Dictionary<string, List<LiveFieldValue>> resolvedInstances,
        int remainingDepth,
        CsxFormat format)
    {
        var name = !string.IsNullOrEmpty(structName) ? structName : "Unknown";

        var sb = new StringBuilder();
        sb.Append("        <Structure Name=\"").Append(EscapeXml(name))
          .AppendLine("\" AutoFill=\"0\" AutoCreate=\"1\" DefaultHex=\"0\" AutoDestroy=\"0\" DoNotSaveLocal=\"0\" RLECompression=\"1\" AutoCreateStructsize=\"4096\">");
        sb.AppendLine("          <Elements>");

        if (fields.Count > 0)
        {
            foreach (var field in fields)
            {
                // A StructProperty here is a container element VALUE that is itself a
                // struct (Map&lt;…,Struct&gt; / Set&lt;Struct&gt;) or a struct member of a drilled
                // target — flatten its resolved sub-fields inline instead of a raw blob.
                if (field.TypeName == "StructProperty")
                    EmitStructPropertyFlattened(sb, field, resolvedStructs, resolvedInstances, remainingDepth, "            ", format);
                else
                    EmitElement(sb, field.Offset, field.Name, field, "            ", resolvedStructs, resolvedInstances, remainingDepth, format);
            }
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
        HashSet<string> visited,
        CancellationToken ct = default)
    {
        if (remainingDepth <= 0) return;

        foreach (var field in fields)
        {
            // Abort promptly between pipe round-trips when the CSX export is cancelled.
            ct.ThrowIfCancellationRequested();

            if (!IsObjectPropertyType(field.TypeName)) continue;
            if (string.IsNullOrEmpty(field.PtrAddress) || field.PtrAddress == "0x0") continue;
            if (resolved.ContainsKey(field.PtrAddress)) continue;
            if (!visited.Add(field.PtrAddress)) continue; // cycle detection

            try
            {
                var result = await dump.WalkInstanceAsync(field.PtrAddress, field.PtrClassAddr, arrayLimit, ct: ct);
                if (result.Fields.Count > 0)
                {
                    resolved[field.PtrAddress] = result.Fields;
                    // Recurse deeper for nested pointers
                    await ResolvePointerInstancesAsync(
                        dump, result.Fields, resolved, remainingDepth - 1, arrayLimit, visited, ct);
                }
            }
            // Let a cancel unwind the export; only genuine pipe/target failures leave the
            // pointer without a child structure. OperationCanceledException covers its
            // TaskCanceledException subclass too.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Skip on pipe error — pointer will have no child structure
            }
        }
    }

    /// <summary>
    /// Check if a type name is an object/pointer property that can have a PropertyClass.
    /// DelegateProperty also exposes a single bound UObject* (the FWeakObjectPtr target),
    /// so it gets the same pointer-style treatment in array element conversion.
    /// </summary>
    private static bool IsObjectPropertyType(string typeName) => typeName is
        "ObjectProperty" or "ClassProperty" or "SoftObjectProperty" or
        "SoftClassProperty" or "LazyObjectProperty" or "InterfaceProperty" or
        "DelegateProperty";

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
        HashSet<string> visited,
        CancellationToken ct = default)
    {
        if (remainingDepth <= 0) return;

        foreach (var field in fields)
        {
            ct.ThrowIfCancellationRequested();

            var containerFields = ConvertContainerElementsToFields(field);
            if (containerFields == null || containerFields.Count == 0) continue;

            // Container expansion uses one depth level; resolve inner pointers at depth-1
            await ResolvePointerInstancesAsync(
                dump, containerFields, resolved, remainingDepth - 1, arrayLimit, visited, ct);
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
            // Multicast delegate fields are exposed as implicit DelegateProperty arrays
            "MulticastInlineDelegateProperty" when field.ArrayElements is { Count: > 0 }
                => ConvertArrayElementsToFields(field),
            "MulticastDelegateProperty" when field.ArrayElements is { Count: > 0 }
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

        // Set&lt;Struct&gt;: stamp each element's absolute struct address so the emit phase
        // flattens its resolved sub-fields inline (mirrors the Map&lt;…,Struct&gt; path).
        if (setField.SetElemType == "StructProperty"
            && !string.IsNullOrEmpty(setField.SetElemStructAddr))
            return ConvertSetStructElementsToFields(setField);

        return ConvertSetScalarElementsToFields(setField);
    }

    /// <summary>
    /// Convert SetProperty struct elements to synthetic LiveFieldValue list, each carrying
    /// the element's absolute StructDataAddr for flattening. Element start offset within the
    /// TSparseArray data buffer is index * stride (no value offset — Set elements are the key).
    /// </summary>
    private static List<LiveFieldValue> ConvertSetStructElementsToFields(LiveFieldValue setField)
    {
        int stride = ContainerGeometry.SetStrideOf(setField);
        ulong dataBase = ParseHexAddr(setField.SetDataAddr);
        var fields = new List<LiveFieldValue>();

        foreach (var elem in setField.SetElements!)
        {
            var keyDisplay = !string.IsNullOrEmpty(elem.KeyPtrName) ? elem.KeyPtrName : elem.Key;
            var name = !string.IsNullOrEmpty(keyDisplay) ? $"[{elem.Index}] {keyDisplay}" : $"[{elem.Index}]";
            int offset = elem.Index * stride;

            // AbsAddr returns "" when the data base is unknown; an empty StructDataAddr
            // makes the emit phase fall back to a raw byte block gracefully.
            fields.Add(new LiveFieldValue
            {
                Name = name,
                TypeName = "StructProperty",
                Offset = offset,
                Size = setField.SetElemSize,
                StructDataAddr = AbsAddr(dataBase, offset),
                StructClassAddr = setField.SetElemStructAddr,
            });
        }

        return fields;
    }

    /// <summary>
    /// Convert MapProperty elements to synthetic LiveFieldValue list.
    /// Each map entry is represented by its VALUE at offset (sparseIndex * stride + valOffset).
    /// TMap.Data is TSparseArray: stride = AlignUp(valOffset + valueSize, 4) + 8 (hash overhead).
    /// valOffset = aligned byte offset of value within TPair (may differ from keySize).
    /// </summary>
    private static List<LiveFieldValue> ConvertMapElementsToFields(LiveFieldValue mapField)
    {
        // Use the DLL's aligned value offset (PR #277: real value alignment, not a
        // size guess — FName-valued maps land correctly); fall back to key size.
        int valOffset = ContainerGeometry.MapValueOffsetOf(mapField);
        int pairSize = valOffset + mapField.MapValueSize;
        int stride = ContainerGeometry.MapStrideOf(mapField);
        bool valStruct = mapField.MapValueType == "StructProperty"
                         && !string.IsNullOrEmpty(mapField.MapValueStructAddr);
        ulong dataBase = ParseHexAddr(mapField.MapDataAddr);
        var fields = new List<LiveFieldValue>();

        foreach (var elem in mapField.MapElements!)
        {
            var keyDisplay = !string.IsNullOrEmpty(elem.KeyPtrName) ? elem.KeyPtrName : elem.Key;
            var name = $"[{elem.Index}] {keyDisplay}";
            int offset = elem.Index * stride + valOffset;

            if (valStruct && dataBase != 0)
            {
                // Map&lt;…, Struct&gt;: stamp the value's absolute struct address (mirrors
                // CeXmlExportService.BuildContainerValueFields) so the emit phase flattens
                // its resolved sub-fields inline instead of a raw byte blob. StructTypeName
                // is left empty so the element label (Name) is used as the sub-field prefix.
                fields.Add(new LiveFieldValue
                {
                    Name = name,
                    TypeName = "StructProperty",
                    Offset = offset,
                    Size = mapField.MapValueSize,
                    StructDataAddr = AbsAddr(dataBase, offset),
                    StructClassAddr = mapField.MapValueStructAddr,
                });
            }
            else
            {
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
        int stride = ContainerGeometry.SetStrideOf(setField);
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
        int stride = ContainerGeometry.SetStrideOf(setField);
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
    /// Parse a hex address string ("0x18AB.." or "18AB..") into a ulong; 0 on empty/malformed.
    /// </summary>
    private static ulong ParseHexAddr(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var t = (s.StartsWith("0x") || s.StartsWith("0X")) ? s.Substring(2) : s;
        return ulong.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    }

    /// <summary>
    /// Format an absolute address (dataBase + offset) as "0x..", or "" when the base is
    /// unknown. Mirrors CeXmlExportService.AbsAddr so resolver keys and emit-time lookups
    /// of container element struct values use byte-identical strings.
    /// </summary>
    private static string AbsAddr(ulong dataBase, long offset)
        => dataBase == 0 ? "" : $"0x{dataBase + (ulong)offset:X}";

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
