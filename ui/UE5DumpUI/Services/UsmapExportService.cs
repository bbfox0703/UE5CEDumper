using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Generates USMAP binary mapping files compatible with FModel/CUE4Parse.
/// Format: USMAP v3 (LongFName), no compression.
/// </summary>
public static class UsmapExportService
{
    // USMAP magic number
    private const ushort Magic = 0x30C4;

    // Version: 3 = LongFName (uint16 name lengths instead of uint8)
    private const byte Version = 3;

    // Compression: 0 = None
    private const byte CompressionNone = 0;

    /// <summary>
    /// Property type enum matching EMappingsTypeFlags from Dumper-7/UE4SS.
    /// </summary>
    internal enum EPropertyType : byte
    {
        ByteProperty = 0,
        BoolProperty = 1,
        IntProperty = 2,
        FloatProperty = 3,
        ObjectProperty = 4,
        NameProperty = 5,
        DelegateProperty = 6,
        DoubleProperty = 7,
        ArrayProperty = 8,
        StructProperty = 9,
        StrProperty = 10,
        TextProperty = 11,
        InterfaceProperty = 12,
        MulticastDelegateProperty = 13,
        WeakObjectProperty = 14,
        LazyObjectProperty = 15,
        AssetObjectProperty = 16,  // SoftObjectProperty
        SoftObjectProperty = 17,
        UInt64Property = 18,
        UInt32Property = 19,
        UInt16Property = 20,
        Int64Property = 21,
        Int16Property = 22,
        Int8Property = 23,
        MapProperty = 24,
        SetProperty = 25,
        EnumProperty = 26,
        FieldPathProperty = 27,
        Unknown = 0xFF,
    }

    /// <summary>
    /// Generate a complete USMAP binary file from the connected game's data.
    /// </summary>
    public static async Task<byte[]> GenerateUsmapAsync(
        IDumpService dump, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        // 1. Collect enums
        progress?.Report("Collecting enums...");
        var enums = await dump.ListEnumsAsync(ct);
        progress?.Report($"Collected {enums.Count} enums");

        // 2. Collect all Class/ScriptStruct objects
        var structTargets = new List<(string addr, string name)>();
        int offset = 0;
        const int pageSize = 5000;
        int total = 0;

        do
        {
            ct.ThrowIfCancellationRequested();
            var page = await dump.GetObjectListAsync(offset, pageSize, ct);
            total = page.Total;

            foreach (var obj in page.Objects)
            {
                if (obj.ClassName is "Class" or "ScriptStruct")
                    structTargets.Add((obj.Address, obj.Name));
            }

            offset += page.Scanned > 0 ? page.Scanned : page.Objects.Count;
            progress?.Report($"Scanning objects... ({offset}/{total})");
        } while (offset < total);

        progress?.Report($"Walking {structTargets.Count} classes...");

        // 3. Walk each class to get field definitions
        var classInfos = new List<ClassInfoModel>();
        int walked = 0;

        foreach (var (addr, name) in structTargets)
        {
            ct.ThrowIfCancellationRequested();
            walked++;
            if (walked % 50 == 0)
                progress?.Report($"Walking classes... ({walked}/{structTargets.Count})");

            try
            {
                var classInfo = await dump.WalkClassAsync(addr, ct);
                classInfos.Add(classInfo);
            }
            catch
            {
                // Skip classes that fail to walk
            }
        }

        // 4. Build binary
        progress?.Report("Writing USMAP...");
        var bytes = BuildUsmap(enums, classInfos);
        progress?.Report($"Generated USMAP ({bytes.Length} bytes, {classInfos.Count} structs, {enums.Count} enums)");
        return bytes;
    }

    /// <summary>
    /// Build the USMAP binary from pre-collected data.
    /// Exposed for testing.
    /// </summary>
    internal static byte[] BuildUsmap(
        IReadOnlyList<EnumDefinition> enums,
        IReadOnlyList<ClassInfoModel> classInfos)
    {
        var nameTable = new NameTable();

        // Pre-register all names we'll need
        foreach (var e in enums)
        {
            nameTable.GetOrAdd(e.Name);
            foreach (var entry in e.Entries)
                nameTable.GetOrAdd(entry.Name);
        }

        foreach (var ci in classInfos)
        {
            nameTable.GetOrAdd(ci.Name);
            if (!string.IsNullOrEmpty(ci.SuperName))
                nameTable.GetOrAdd(ci.SuperName);
            foreach (var f in ci.Fields)
            {
                nameTable.GetOrAdd(f.Name);
                RegisterPropertyNames(nameTable, f);
            }
        }

        // Build the payload (name table + enums + structs)
        using var payload = new MemoryStream();
        using var w = new BinaryWriter(payload);

        WriteNameTable(w, nameTable);
        WriteEnums(w, enums, nameTable);
        WriteStructs(w, classInfos, nameTable);

        var payloadBytes = payload.ToArray();

        // Build the final file: header + uncompressed payload
        using var final = new MemoryStream();
        using var fw = new BinaryWriter(final);

        fw.Write(Magic);                          // uint16: magic
        fw.Write(Version);                        // uint8: version
        fw.Write(CompressionNone);                // uint8: compression
        fw.Write((uint)payloadBytes.Length);       // uint32: compressed size
        fw.Write((uint)payloadBytes.Length);       // uint32: decompressed size
        fw.Write(payloadBytes);

        return final.ToArray();
    }

    private static void WriteNameTable(BinaryWriter w, NameTable table)
    {
        var names = table.GetOrderedNames();
        w.Write((uint)names.Length);
        foreach (var name in names)
        {
            // LongFName: uint16 length + chars (UTF-8)
            var bytes = System.Text.Encoding.UTF8.GetBytes(name);
            w.Write((ushort)bytes.Length);
            w.Write(bytes);
        }
    }

    private static void WriteEnums(BinaryWriter w, IReadOnlyList<EnumDefinition> enums, NameTable nameTable)
    {
        w.Write((uint)enums.Count);
        foreach (var e in enums)
        {
            w.Write(nameTable.GetIndex(e.Name));           // int32: name index
            var count = (byte)Math.Min(e.Entries.Count, 255);
            w.Write(count);                                 // uint8: member count
            for (int i = 0; i < count; i++)
            {
                w.Write(nameTable.GetIndex(e.Entries[i].Name)); // int32: member name index
            }
        }
    }

    private static void WriteStructs(BinaryWriter w, IReadOnlyList<ClassInfoModel> classInfos, NameTable nameTable)
    {
        w.Write((uint)classInfos.Count);
        foreach (var ci in classInfos)
        {
            w.Write(nameTable.GetIndex(ci.Name));           // int32: struct name index

            // Super struct index: -1 if none
            if (!string.IsNullOrEmpty(ci.SuperName) && nameTable.Contains(ci.SuperName))
                w.Write(nameTable.GetIndex(ci.SuperName));
            else
                w.Write(-1);                                // int32: super index (-1 = none)

            // Property count + serializable property count
            var propCount = (ushort)ci.Fields.Count;
            w.Write(propCount);                             // uint16: total property count
            w.Write(propCount);                             // uint16: serializable property count

            foreach (var f in ci.Fields)
            {
                // Schema index (same as property index for serializable properties)
                w.Write((ushort)0);                         // uint16: schema index (unused, 0)
                w.Write((ushort)0);                         // uint8: array dim (unused legacy)
                w.Write(nameTable.GetIndex(f.Name));        // int32: property name index
                WritePropertyType(w, f, nameTable);         // recursive property type
            }
        }
    }

    /// <summary>
    /// Write the recursive property type descriptor for a field.
    /// </summary>
    internal static void WritePropertyType(BinaryWriter w, FieldInfoModel f, NameTable nameTable)
    {
        var propType = MapPropertyType(f.TypeName);
        w.Write((byte)propType);

        switch (propType)
        {
            case EPropertyType.EnumProperty:
                // EnumProperty: write underlying type + enum name
                WriteInnerPropertyType(w, "ByteProperty");
                w.Write(nameTable.GetOrAdd(
                    !string.IsNullOrEmpty(f.EnumName) ? f.EnumName : "None"));
                break;

            case EPropertyType.StructProperty:
                w.Write(nameTable.GetOrAdd(
                    !string.IsNullOrEmpty(f.StructType) ? f.StructType : "None"));
                break;

            case EPropertyType.ArrayProperty:
                WriteInnerPropertyTypeFromField(w, f.InnerType, f.InnerStructType,
                    f.InnerObjClass, f.EnumName, nameTable);
                break;

            case EPropertyType.SetProperty:
                WriteInnerPropertyTypeFromField(w, f.ElemType, f.ElemStructType,
                    "", "", nameTable);
                break;

            case EPropertyType.MapProperty:
                WriteInnerPropertyTypeFromField(w, f.KeyType, f.KeyStructType,
                    "", "", nameTable);
                WriteInnerPropertyTypeFromField(w, f.ValueType, f.ValueStructType,
                    "", "", nameTable);
                break;

            case EPropertyType.ByteProperty:
                // If ByteProperty has an enum, write it as EnumProperty instead
                if (!string.IsNullOrEmpty(f.EnumName))
                {
                    // Already wrote ByteProperty type byte — that's correct for USMAP
                    // ByteProperty with enum name is separate from EnumProperty
                }
                break;

            // Simple types: no extra data needed
            default:
                break;
        }
    }

    private static void WriteInnerPropertyType(BinaryWriter w, string innerTypeName)
    {
        w.Write((byte)MapPropertyType(innerTypeName));
    }

    private static void WriteInnerPropertyTypeFromField(
        BinaryWriter w, string innerType, string structType, string objClass,
        string enumName, NameTable nameTable)
    {
        var propType = MapPropertyType(innerType);
        w.Write((byte)propType);

        switch (propType)
        {
            case EPropertyType.StructProperty:
                w.Write(nameTable.GetOrAdd(
                    !string.IsNullOrEmpty(structType) ? structType : "None"));
                break;

            case EPropertyType.EnumProperty:
                WriteInnerPropertyType(w, "ByteProperty");
                w.Write(nameTable.GetOrAdd(
                    !string.IsNullOrEmpty(enumName) ? enumName : "None"));
                break;

            case EPropertyType.ObjectProperty:
            case EPropertyType.WeakObjectProperty:
            case EPropertyType.LazyObjectProperty:
            case EPropertyType.AssetObjectProperty:
            case EPropertyType.SoftObjectProperty:
                // These don't need extra data in USMAP
                break;
        }
    }

    internal static EPropertyType MapPropertyType(string typeName)
    {
        return typeName switch
        {
            "ByteProperty" => EPropertyType.ByteProperty,
            "BoolProperty" => EPropertyType.BoolProperty,
            "IntProperty" => EPropertyType.IntProperty,
            "FloatProperty" => EPropertyType.FloatProperty,
            "ObjectProperty" => EPropertyType.ObjectProperty,
            "ClassProperty" => EPropertyType.ObjectProperty,
            "NameProperty" => EPropertyType.NameProperty,
            "DelegateProperty" => EPropertyType.DelegateProperty,
            "DoubleProperty" => EPropertyType.DoubleProperty,
            "ArrayProperty" => EPropertyType.ArrayProperty,
            "StructProperty" => EPropertyType.StructProperty,
            "StrProperty" => EPropertyType.StrProperty,
            "TextProperty" => EPropertyType.TextProperty,
            "InterfaceProperty" => EPropertyType.InterfaceProperty,
            "MulticastDelegateProperty" => EPropertyType.MulticastDelegateProperty,
            "MulticastInlineDelegateProperty" => EPropertyType.MulticastDelegateProperty,
            "MulticastSparseDelegateProperty" => EPropertyType.MulticastDelegateProperty,
            "WeakObjectProperty" => EPropertyType.WeakObjectProperty,
            "LazyObjectProperty" => EPropertyType.LazyObjectProperty,
            "SoftObjectProperty" => EPropertyType.SoftObjectProperty,
            "SoftClassProperty" => EPropertyType.SoftObjectProperty,
            "UInt64Property" => EPropertyType.UInt64Property,
            "UInt32Property" => EPropertyType.UInt32Property,
            "UInt16Property" => EPropertyType.UInt16Property,
            "Int64Property" => EPropertyType.Int64Property,
            "Int16Property" => EPropertyType.Int16Property,
            "Int8Property" => EPropertyType.Int8Property,
            "MapProperty" => EPropertyType.MapProperty,
            "SetProperty" => EPropertyType.SetProperty,
            "EnumProperty" => EPropertyType.EnumProperty,
            "FieldPathProperty" => EPropertyType.FieldPathProperty,
            _ => EPropertyType.Unknown,
        };
    }

    private static void RegisterPropertyNames(NameTable table, FieldInfoModel f)
    {
        if (!string.IsNullOrEmpty(f.StructType)) table.GetOrAdd(f.StructType);
        if (!string.IsNullOrEmpty(f.EnumName)) table.GetOrAdd(f.EnumName);
        if (!string.IsNullOrEmpty(f.InnerStructType)) table.GetOrAdd(f.InnerStructType);
        if (!string.IsNullOrEmpty(f.ElemStructType)) table.GetOrAdd(f.ElemStructType);
        if (!string.IsNullOrEmpty(f.KeyStructType)) table.GetOrAdd(f.KeyStructType);
        if (!string.IsNullOrEmpty(f.ValueStructType)) table.GetOrAdd(f.ValueStructType);
    }

    /// <summary>
    /// Name table: maps strings to sequential integer indices.
    /// </summary>
    internal sealed class NameTable
    {
        private readonly Dictionary<string, int> _map = new();
        private readonly List<string> _ordered = new();

        public int GetOrAdd(string name)
        {
            if (_map.TryGetValue(name, out var idx))
                return idx;
            idx = _ordered.Count;
            _map[name] = idx;
            _ordered.Add(name);
            return idx;
        }

        public int GetIndex(string name) =>
            _map.TryGetValue(name, out var idx) ? idx : GetOrAdd(name);

        public bool Contains(string name) => _map.ContainsKey(name);

        public string[] GetOrderedNames() => _ordered.ToArray();

        public int Count => _ordered.Count;
    }
}
