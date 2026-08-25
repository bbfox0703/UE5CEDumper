using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Generates USMAP binary mapping files compatible with FModel/CUE4Parse.
/// Format: USMAP v4 (ExplicitEnumValues), uncompressed.
///
/// <para><b>The declared version and the emitted layout must agree.</b> This writer used to stamp
/// version 3 and emit the version-0 body: no <c>bHasVersionInfo</c>, a <c>uint8</c> enum member
/// count, and a 2-byte ArrayDim. Each of those desynchronises the stream, and the first does it
/// before a single name is read, so no consumer could ever open the output (audit #5 W1). The
/// layout below is checked byte for byte against the two canonical writers vendored in this repo:
/// <c>vendor/RE-UE4SS/UE4SS/src/USMapGenerator/Generator.cpp</c> and
/// <c>vendor/Dumper-7/Dumper/Generator/Private/Generators/MappingGenerator.cpp</c>. Both emit v4.
/// Read them before touching this format again.</para>
/// </summary>
public static class UsmapExportService
{
    // USMAP magic number
    private const ushort Magic = 0x30C4;

    // EUsmapVersion, from the vendored writers: Initial=0, PackageVersioning=1, LongFName=2,
    // LargeEnums=3, ExplicitEnumValues=4 (Latest). We emit 4 -- what both canonical writers
    // produce today -- and every one of these thresholds changes the BYTES:
    //   >= PackageVersioning  : the int32 bHasVersionInfo header field becomes MANDATORY
    //   >= LongFName          : name lengths are uint16 (this writer always did that, which is
    //                           why the version can never go below 2)
    //   >= LargeEnums         : enum member counts are uint16, so a >255-member enum survives
    //   >= ExplicitEnumValues : each enum member carries its int64 value, so an enum with gaps
    //                           or explicit values is no longer flattened to 0..N-1
    private const byte Version = 4;

    // "No UE4/UE5 version info follows." The format spells this field 'bool' and a UE bool
    // serialises as 4 bytes, so the reader consumes an int32 here either way.
    private const int NoVersionInfo = 0;

    // Fallback for an unresolved struct/enum reference. Registered up front so the write pass
    // never has to extend the name table -- see NameTable.Seal.
    private const string NoneName = "None";

    // Compression: 0 = None
    private const byte CompressionNone = 0;

    /// <summary>
    /// Property type enum matching EMappingsTypeFlags from Dumper-7/UE4SS.
    /// Byte values are a binary contract with downstream consumers (CUE4Parse / UE4SS) —
    /// they MUST match the canonical ordering in RE-UE4SS USMapGenerator/Generator.cpp and
    /// Dumper-7 Engine/Public/Unreal/Enums.h. In particular OptionalProperty=28 sits BEFORE
    /// the UE5.5+ string variants, so its slot is reserved to keep 29/30 correct.
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
        OptionalProperty = 28,   // UE5.2+ TOptional<T> — wraps an inner ValueProperty
        Utf8StrProperty = 29,    // UE5.5+ FUtf8String (1-byte UTF-8)
        AnsiStrProperty = 30,    // UE5.5+ FAnsiString (1-byte ANSI)
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
        const int pageSize = Constants.GObjectsWalkPageSize;
        int total = 0;

        do
        {
            ct.ThrowIfCancellationRequested();
            var page = await dump.GetObjectListAsync(offset, pageSize, ct);
            total = page.Total;

            foreach (var obj in page.Objects)
            {
                // Mirrors the DLL-side `Aura::IsClassLikeMeta` whitelist (Class + the
                // BPGC variants + DynamicClass), exactly as SdkExportService and
                // DumpAllService already do. A bare `ClassName == "Class"` check
                // silently dropped every BlueprintGeneratedClass /
                // AnimBlueprintGeneratedClass / WidgetBlueprintGeneratedClass /
                // DynamicClass — thousands of structs on a normal shipped title
                // against a few hundred native ones, and the Blueprint classes are
                // precisely the ones a .usmap consumer needs to parse saved games and
                // network traffic. Same bug fixed in the DLL at build 673 and in the
                // SDK exporter at build 1986; this was the last unfixed mirror
                // (audit #5 W8).
                if (DumpAllService.IsClassLikeMetaName(obj.ClassName) || obj.ClassName == "ScriptStruct")
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

        // Pre-register all names we'll need. This pass is the ONLY place that may add to the
        // table; everything after WriteNameTable can only look up (see NameTable.Seal).
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

        // Every name the write pass can reference must exist BEFORE the table is serialised --
        // its length prefix is written once, up front. "None" is the fallback for an unresolved
        // struct/enum reference and used to be added mid-write, landing at an index past the end
        // of the table the file had already declared (audit #5 W7).
        nameTable.GetOrAdd(NoneName);

        // Build the payload (name table + enums + structs)
        using var payload = new MemoryStream();
        using var w = new BinaryWriter(payload);

        WriteNameTable(w, nameTable);
        // The table is now fixed: a later GetOrAdd would corrupt the file rather than extend it,
        // so make that a hard error instead of a silent one.
        nameTable.Seal();
        WriteEnums(w, enums, nameTable);
        WriteStructs(w, classInfos, nameTable);

        var payloadBytes = payload.ToArray();

        // Build the final file: header + uncompressed payload
        using var final = new MemoryStream();
        using var fw = new BinaryWriter(final);

        fw.Write(Magic);                           // uint16: magic
        fw.Write(Version);                         // uint8:  version
        fw.Write(NoVersionInfo);                   // int32:  bHasVersionInfo -- REQUIRED for version >= 1
        fw.Write(CompressionNone);                 // uint8:  compression method
        fw.Write((uint)payloadBytes.Length);       // uint32: compressed size
        fw.Write((uint)payloadBytes.Length);       // uint32: decompressed size
        fw.Write(payloadBytes);
        // No CEXT extensions block -- it is optional, and Dumper-7 emits v4 without one.

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
            w.Write(nameTable.IndexOf(e.Name));             // int32: name index

            // uint16 count (LargeEnums). The old uint8 both desynced the stream and silently
            // truncated any enum past 255 members.
            var count = (ushort)Math.Min(e.Entries.Count, ushort.MaxValue);
            w.Write(count);                                 // uint16: member count
            for (int i = 0; i < count; i++)
            {
                w.Write(e.Entries[i].Value);                   // int64: explicit value
                w.Write(nameTable.IndexOf(e.Entries[i].Name)); // int32: member name index
            }
        }
    }

    private static void WriteStructs(BinaryWriter w, IReadOnlyList<ClassInfoModel> classInfos, NameTable nameTable)
    {
        w.Write((uint)classInfos.Count);
        foreach (var ci in classInfos)
        {
            w.Write(nameTable.IndexOf(ci.Name));            // int32: struct name index

            // Super struct index: -1 if none
            if (!string.IsNullOrEmpty(ci.SuperName) && nameTable.Contains(ci.SuperName))
                w.Write(nameTable.IndexOf(ci.SuperName));
            else
                w.Write(-1);                                // int32: super index (-1 = none)

            // These two counts are NOT the same number. The first is the sum of every property's
            // ArrayDim -- a static array Foo[4] occupies four schema slots -- and the second is
            // how many property RECORDS follow. Writing Fields.Count for both makes a reader that
            // walks schema slots disagree with the records it is actually handed.
            int totalSlots = 0;
            foreach (var f in ci.Fields) totalSlots += ArrayDimOf(f);

            w.Write((ushort)Math.Min(totalSlots, ushort.MaxValue));       // uint16: schema slot count
            w.Write((ushort)Math.Min(ci.Fields.Count, ushort.MaxValue));  // uint16: record count

            int slot = 0;
            foreach (var f in ci.Fields)
            {
                var dim = ArrayDimOf(f);
                w.Write((ushort)Math.Min(slot, ushort.MaxValue)); // uint16: this property's schema index
                w.Write((byte)dim);                         // uint8:  array dim. ONE byte, and the real
                                                            //         value: a hardcoded 2-byte 0 slid the
                                                            //         stream AND told the reader to
                                                            //         register no slots at all.
                w.Write(nameTable.IndexOf(f.Name));         // int32:  property name index
                WritePropertyType(w, f, nameTable);         // recursive property type
                slot += dim;
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
                w.Write(nameTable.IndexOf(
                    !string.IsNullOrEmpty(f.EnumName) ? f.EnumName : "None"));
                break;

            case EPropertyType.StructProperty:
                w.Write(nameTable.IndexOf(
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

            case EPropertyType.OptionalProperty:
                // TOptional<T>: write the wrapped value type recursively (mirrors UE4SS
                // FOptionalProperty / CUE4Parse). The DLL fills InnerType/InnerStructType/
                // InnerObjClass for OptionalProperty exactly as it does for ArrayProperty.
                WriteInnerPropertyTypeFromField(w, f.InnerType, f.InnerStructType,
                    f.InnerObjClass, f.EnumName, nameTable);
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
                w.Write(nameTable.IndexOf(
                    !string.IsNullOrEmpty(structType) ? structType : "None"));
                break;

            case EPropertyType.EnumProperty:
                WriteInnerPropertyType(w, "ByteProperty");
                w.Write(nameTable.IndexOf(
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
            // UE5.5+ string variants — pure scalar, no extra type bytes (like StrProperty).
            "Utf8StrProperty" => EPropertyType.Utf8StrProperty,
            "AnsiStrProperty" => EPropertyType.AnsiStrProperty,
            // UE5.2+ TOptional<T> — WritePropertyType recurses into the wrapped value type.
            "OptionalProperty" => EPropertyType.OptionalProperty,
            _ => EPropertyType.Unknown,
        };
    }

    /// <summary>
    /// Static C-array dimension of a property (Foo[4] -> 4), floored at 1. A property always
    /// occupies at least one schema slot, and a 0 here tells a reader to register none.
    /// </summary>
    private static int ArrayDimOf(FieldInfoModel f) => f.ArrayDim > 0 ? f.ArrayDim : 1;

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

        private bool _sealed;

        public int GetOrAdd(string name)
        {
            if (_map.TryGetValue(name, out var idx))
                return idx;
            if (_sealed)
            {
                // Appending here would hand out an index past the end of the table already
                // written to the file, which is a silently corrupt export. Fail loudly instead
                // (audit #5 W7).
                throw new InvalidOperationException(
                    $"USMAP name table is sealed; '{name}' was not registered before serialization.");
            }
            idx = _ordered.Count;
            _map[name] = idx;
            _ordered.Add(name);
            return idx;
        }

        /// <summary>
        /// Freeze the table. Called once the name block has been written; from then on the set of
        /// valid indices is fixed by the file itself.
        /// </summary>
        public void Seal() => _sealed = true;

        /// <summary>
        /// Index of an already-registered name, or the index of "None" for anything unregistered.
        /// Never appends — this is the only lookup the write pass may use.
        /// </summary>
        public int IndexOf(string name)
        {
            if (_map.TryGetValue(name, out var idx)) return idx;
            return _map.TryGetValue(NoneName, out var none) ? none : 0;
        }

        public bool Contains(string name) => _map.ContainsKey(name);

        public string[] GetOrderedNames() => _ordered.ToArray();

        public int Count => _ordered.Count;
    }
}
