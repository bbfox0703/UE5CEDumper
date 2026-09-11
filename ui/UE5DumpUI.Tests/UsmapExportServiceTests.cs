using System.Linq;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

public class UsmapExportServiceTests
{
    private static List<EnumDefinition> CreateTestEnums() =>
    [
        new EnumDefinition
        {
            Address = "0x100", Name = "EGameMode", FullPath = "/Script/Engine.EGameMode",
            Entries =
            [
                new EnumEntryValue { Value = 0, Name = "None" },
                new EnumEntryValue { Value = 1, Name = "Walking" },
                new EnumEntryValue { Value = 2, Name = "Flying" },
            ],
        },
    ];

    private static List<ClassInfoModel> CreateTestStructs() =>
    [
        new ClassInfoModel
        {
            Name = "AActor", FullPath = "/Script/Engine.Actor",
            SuperName = "UObject", PropertiesSize = 0x40,
            Fields =
            [
                new FieldInfoModel { Name = "bHidden", TypeName = "BoolProperty", Offset = 0x28, Size = 1 },
                new FieldInfoModel { Name = "InitialLifeSpan", TypeName = "FloatProperty", Offset = 0x30, Size = 4 },
            ],
        },
    ];

    [Fact]
    public void BuildUsmap_Header_CarriesTheVersioningFieldItsVersionRequires()
    {
        var bytes = UsmapExportService.BuildUsmap([], []);

        // Magic 0x30C4, little-endian.
        Assert.True(bytes.Length >= 16, "USMAP too short");
        Assert.Equal(0xC4, bytes[0]);
        Assert.Equal(0x30, bytes[1]);
        Assert.Equal(4, bytes[2]);                       // ExplicitEnumValues

        // Bytes 3..6 are the int32 bHasVersionInfo, mandatory for version >= 1. This is the
        // field whose absence made every previously exported file unopenable: a reader consumes
        // four bytes here, and without them it ate the low bytes of the payload size and threw
        // before reading a single name (audit #5 W1).
        Assert.Equal(0, BitConverter.ToInt32(bytes, 3));
        Assert.Equal(0, bytes[7]);                       // compression = None

        UsmapFile.Parse(bytes);                          // and the whole file must round-trip
    }

    [Fact]
    public void BuildUsmap_EmptyData_HasZeroCounts()
    {
        var f = UsmapFile.Parse(UsmapExportService.BuildUsmap([], []));

        // "None" is pre-registered so the write pass never has to extend the table (W7).
        Assert.Equal(new[] { "None" }, f.Names);
        Assert.Empty(f.Enums);
        Assert.Empty(f.Structs);
    }

    [Fact]
    public void BuildUsmap_NameTable_DeduplicatesNames()
    {
        // Two structs that share the "UObject" super name
        var structs = new List<ClassInfoModel>
        {
            new() { Name = "AActor", SuperName = "UObject", Fields = [] },
            new() { Name = "APawn", SuperName = "UObject", Fields = [] },
        };

        var f = UsmapFile.Parse(UsmapExportService.BuildUsmap([], structs));

        Assert.Contains("AActor", f.Names);
        Assert.Contains("UObject", f.Names);
        Assert.Contains("APawn", f.Names);
        Assert.Single(f.Names, n => n == "UObject");         // shared super registered once
        Assert.Equal("UObject", f.Structs[0].Super);
        Assert.Equal("UObject", f.Structs[1].Super);
    }

    [Fact]
    public void BuildUsmap_SimpleStruct_CorrectPropertyEncoding()
    {
        var structs = new List<ClassInfoModel>
        {
            new()
            {
                Name = "FMyStruct", SuperName = "", PropertiesSize = 8,
                Fields =
                [
                    new FieldInfoModel { Name = "X", TypeName = "FloatProperty", Offset = 0, Size = 4 },
                    new FieldInfoModel { Name = "Y", TypeName = "FloatProperty", Offset = 4, Size = 4 },
                ],
            },
        };

        var f = UsmapFile.Parse(UsmapExportService.BuildUsmap([], structs));

        Assert.Empty(f.Enums);
        var st = Assert.Single(f.Structs);
        Assert.Equal("FMyStruct", st.Name);
        Assert.Null(st.Super);
        Assert.Equal(2, st.Props.Count);
        Assert.Equal(new[] { "X", "Y" }, st.Props.Select(p => p.Name));

        // ArrayDim is 1 for a scalar and occupies ONE byte. It used to be a hardcoded 2-byte 0,
        // which both slid the stream and told a reader to register no schema slots at all.
        Assert.All(st.Props, p => Assert.Equal(1, p.ArrayDim));
        Assert.Equal(new ushort[] { 0, 1 }, st.Props.Select(p => p.SchemaIndex));
        Assert.Equal(2, st.SlotCount);
    }

    [Fact]
    public void BuildUsmap_EnumProperty_RecursiveEncoding()
    {
        var enums = CreateTestEnums();
        var structs = new List<ClassInfoModel>
        {
            new()
            {
                Name = "FEnumStruct", Fields =
                [
                    new FieldInfoModel
                    {
                        Name = "Mode", TypeName = "EnumProperty",
                        EnumName = "EGameMode", Offset = 0, Size = 1,
                    },
                ],
            },
        };

        var bytes = UsmapExportService.BuildUsmap(enums, structs);
        Assert.True(bytes.Length > 12);
    }

    [Fact]
    public void BuildUsmap_ArrayOfStruct_NestedEncoding()
    {
        var structs = new List<ClassInfoModel>
        {
            new()
            {
                Name = "FParent", Fields =
                [
                    new FieldInfoModel
                    {
                        Name = "Items", TypeName = "ArrayProperty",
                        InnerType = "StructProperty", InnerStructType = "FVector",
                        Offset = 0, Size = 16,
                    },
                ],
            },
        };

        var bytes = UsmapExportService.BuildUsmap([], structs);
        Assert.True(bytes.Length > 12);
    }

    [Fact]
    public void BuildUsmap_MapProperty_KeyValueEncoding()
    {
        var structs = new List<ClassInfoModel>
        {
            new()
            {
                Name = "FMapHolder", Fields =
                [
                    new FieldInfoModel
                    {
                        Name = "Lookup", TypeName = "MapProperty",
                        KeyType = "StrProperty", ValueType = "IntProperty",
                        Offset = 0, Size = 80,
                    },
                ],
            },
        };

        var bytes = UsmapExportService.BuildUsmap([], structs);
        Assert.True(bytes.Length > 12);
    }

    [Fact]
    public void BuildUsmap_WithEnums_CorrectEntries()
    {
        var f = UsmapFile.Parse(UsmapExportService.BuildUsmap(CreateTestEnums(), []));

        var e = Assert.Single(f.Enums);
        Assert.Equal("EGameMode", e.Name);
        Assert.Equal(3, e.Members.Count);
        Assert.Equal(new[] { "None", "Walking", "Flying" }, e.Members.Select(m => m.Name));

        // The member count is a uint16 (LargeEnums) and each member carries its explicit int64
        // value (ExplicitEnumValues) — an enum with gaps is no longer flattened to 0..N-1.
        Assert.Equal(new long[] { 0, 1, 2 }, e.Members.Select(m => m.Value));
    }

    [Fact]
    public void MapPropertyType_KnownTypes_ReturnCorrect()
    {
        Assert.Equal(UsmapExportService.EPropertyType.IntProperty,
            UsmapExportService.MapPropertyType("IntProperty"));
        Assert.Equal(UsmapExportService.EPropertyType.FloatProperty,
            UsmapExportService.MapPropertyType("FloatProperty"));
        Assert.Equal(UsmapExportService.EPropertyType.ArrayProperty,
            UsmapExportService.MapPropertyType("ArrayProperty"));
        Assert.Equal(UsmapExportService.EPropertyType.MapProperty,
            UsmapExportService.MapPropertyType("MapProperty"));
        Assert.Equal(UsmapExportService.EPropertyType.EnumProperty,
            UsmapExportService.MapPropertyType("EnumProperty"));
        Assert.Equal(UsmapExportService.EPropertyType.StructProperty,
            UsmapExportService.MapPropertyType("StructProperty"));
    }

    [Fact]
    public void MapPropertyType_UnknownType_ReturnsUnknown()
    {
        Assert.Equal(UsmapExportService.EPropertyType.Unknown,
            UsmapExportService.MapPropertyType("SomeFutureProperty"));
    }

    [Fact]
    public void MapPropertyType_Utf8StrProperty_ReturnsUtf8Str()
    {
        Assert.Equal(UsmapExportService.EPropertyType.Utf8StrProperty,
            UsmapExportService.MapPropertyType("Utf8StrProperty"));
    }

    [Fact]
    public void MapPropertyType_AnsiStrProperty_ReturnsAnsiStr()
    {
        Assert.Equal(UsmapExportService.EPropertyType.AnsiStrProperty,
            UsmapExportService.MapPropertyType("AnsiStrProperty"));
    }

    [Fact]
    public void MapPropertyType_OptionalProperty_ReturnsOptional()
    {
        Assert.Equal(UsmapExportService.EPropertyType.OptionalProperty,
            UsmapExportService.MapPropertyType("OptionalProperty"));
    }

    [Fact]
    public void WritePropertyType_OptionalStruct_WritesTypeThenInnerStruct()
    {
        // TOptional<FVector> → [OptionalProperty=28][StructProperty=9][name idx of "FVector"].
        var nameTable = new UsmapExportService.NameTable();
        var field = new FieldInfoModel
        {
            TypeName = "OptionalProperty",
            InnerType = "StructProperty",
            InnerStructType = "FVector",
        };

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        UsmapExportService.WritePropertyType(w, field, nameTable);
        w.Flush();
        var bytes = ms.ToArray();

        Assert.Equal(28, bytes[0]);                       // OptionalProperty
        Assert.Equal(9, bytes[1]);                        // inner StructProperty
        Assert.True(bytes.Length > 2);                    // followed by the struct name index
        Assert.Equal(0, nameTable.GetOrAdd("FVector"));   // "FVector" was registered (idx 0)
    }

    [Fact]
    public void WritePropertyType_OptionalObject_WritesTypeThenInnerObjectNoExtra()
    {
        // TOptional<UObject*> → [OptionalProperty=28][ObjectProperty=4], no trailing data.
        var nameTable = new UsmapExportService.NameTable();
        var field = new FieldInfoModel { TypeName = "OptionalProperty", InnerType = "ObjectProperty" };

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        UsmapExportService.WritePropertyType(w, field, nameTable);
        w.Flush();
        var bytes = ms.ToArray();

        Assert.Equal(new byte[] { 28, 4 }, bytes);  // ObjectProperty writes no extra bytes
    }

    // ---- [A4-USMAP-CONTAINER-ENUM] a container inner's TEnumAsByte takes the canonical enum shape too ----

    [Fact]
    public void WritePropertyType_ArrayOfTEnumAsByte_WritesTheCanonicalEnumShape()
    {
        // TArray<TEnumAsByte<E>> -> [ArrayProperty=8][EnumProperty=26][ByteProperty=0][E]. A bare [8][0] had a consumer
        // read one byte per element against the FName UE 5.8 serializes for each (PropertyByte.cpp).
        var nameTable = new UsmapExportService.NameTable();
        nameTable.GetOrAdd("EObjectTypeQuery");
        var field = new FieldInfoModel
        {
            TypeName = "ArrayProperty", InnerType = "ByteProperty", InnerEnumName = "EObjectTypeQuery",
        };

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        UsmapExportService.WritePropertyType(w, field, nameTable);
        w.Flush();

        Assert.Equal(new byte[] { 8, 26, 0, 0, 0, 0, 0 }, ms.ToArray());   // enum name index 0, as int32
    }

    [Fact]
    public void WritePropertyType_MapWithEnumKeyAndValue_WritesEachItsOwnEnum()
    {
        // A TEnumAsByte key and an EnumProperty value: each carries its OWN enum -- the map passed "" for both.
        var nameTable = new UsmapExportService.NameTable();
        nameTable.GetOrAdd("EKey");      // idx 0
        nameTable.GetOrAdd("EValue");    // idx 1
        var field = new FieldInfoModel
        {
            TypeName = "MapProperty",
            KeyType = "ByteProperty", KeyEnumName = "EKey",
            ValueType = "EnumProperty", ValueEnumName = "EValue",
        };

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        UsmapExportService.WritePropertyType(w, field, nameTable);
        w.Flush();
        var bytes = ms.ToArray();

        Assert.Equal((byte)UsmapExportService.EPropertyType.MapProperty, bytes[0]);
        Assert.Equal(new byte[] { 26, 0, 0, 0, 0, 0, 26, 0, 1, 0, 0, 0 }, bytes[1..]);
    }

    // ---- review 5 of 53baca47: the Set and Optional arms of the fix were unpinned ----

    [Fact]
    public void WritePropertyType_SetOfTEnumAsByte_WritesTheCanonicalEnumShape()
    {
        var nameTable = new UsmapExportService.NameTable();
        nameTable.GetOrAdd("EObjectTypeQuery");   // idx 0
        var field = new FieldInfoModel
        {
            TypeName = "SetProperty", ElemType = "ByteProperty", ElemEnumName = "EObjectTypeQuery",
        };

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        UsmapExportService.WritePropertyType(w, field, nameTable);
        w.Flush();

        Assert.Equal(new byte[] { 25, 26, 0, 0, 0, 0, 0 }, ms.ToArray());   // [Set][Enum][Byte][E]
    }

    [Fact]
    public void WritePropertyType_OptionalOfTEnumAsByte_WritesTheCanonicalEnumShape()
    {
        var nameTable = new UsmapExportService.NameTable();
        nameTable.GetOrAdd("EObjectTypeQuery");   // idx 0
        var field = new FieldInfoModel
        {
            TypeName = "OptionalProperty", InnerType = "ByteProperty", InnerEnumName = "EObjectTypeQuery",
        };

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        UsmapExportService.WritePropertyType(w, field, nameTable);
        w.Flush();

        Assert.Equal(new byte[] { 28, 26, 0, 0, 0, 0, 0 }, ms.ToArray());   // [Optional][Enum][Byte][E]
    }

    [Fact]
    public void WritePropertyType_ArrayOfPlainBytes_StaysAByteArray()
    {
        // The control, green before and after: no enum, no fake enum shape.
        var nameTable = new UsmapExportService.NameTable();
        var field = new FieldInfoModel { TypeName = "ArrayProperty", InnerType = "ByteProperty" };

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        UsmapExportService.WritePropertyType(w, field, nameTable);
        w.Flush();

        Assert.Equal(new byte[] { 8, 0 }, ms.ToArray());
    }

    // ---- [P1-ENUMNAMES] the .usmap shipped with every enum empty, and never said why ----

    [Fact]
    public void EnumCollectionNote_NamesAFailedLatchAndATruncation()
    {
        var failed = UsmapExportService.EnumCollectionNote(new EnumListResult
        {
            Enums = new() { new EnumDefinition { Name = "ENetRole" } },
            EnumNamesFailed = true,
        });
        Assert.Contains("enum member names are unavailable", failed, StringComparison.Ordinal);

        var cut = UsmapExportService.EnumCollectionNote(new EnumListResult { Enums = new(), Truncated = true });
        Assert.Contains("cut short", cut, StringComparison.Ordinal);
    }

    [Fact]
    public void EnumCollectionNote_AHealthyList_IsJustTheCount()
    {
        // The control, green before and after.
        Assert.Equal("Collected 2 enums", UsmapExportService.EnumCollectionNote(
            new EnumListResult { Enums = new() { new EnumDefinition(), new EnumDefinition() } }));
    }

    // ---- review 5 of 7e5a71fc: the note was a progress line only, overwritten one pipe round-trip later ----

    private class EmptyObjectsStub : StubDumpService
    {
        public override Task<List<EnumDefinition>> ListEnumsAsync(CancellationToken ct = default)
            => Task.FromResult(new List<EnumDefinition>());

        public override Task<ObjectListResult> GetObjectListAsync(
            int offset, int limit, CancellationToken ct = default, bool includePath = false)
            => Task.FromResult(new ObjectListResult { Total = 0, Scanned = 0, Objects = new List<UObjectNode>() });
    }

    /// <summary>Re-implements the default interface member, so the export sees a FAILED latch.</summary>
    private sealed class FailedEnumNamesStub : EmptyObjectsStub, IDumpService
    {
        Task<EnumListResult> IDumpService.ListEnumsDetailedAsync(CancellationToken ct)
            => Task.FromResult(new EnumListResult { Enums = new(), EnumNamesFailed = true });
    }

    [Fact]
    public async Task GenerateUsmapAsync_KeepsTheEnumWarningForTheFinalStatus()
    {
        var warnings = new List<string>();

        await UsmapExportService.GenerateUsmapAsync(new FailedEnumNamesStub(),
            ct: TestContext.Current.CancellationToken, warnings: warnings);

        var w = Assert.Single(warnings);
        Assert.Contains("enum member names are unavailable", w, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateUsmapAsync_AHealthyEnumList_LeavesNoWarning()
    {
        var warnings = new List<string>();

        await UsmapExportService.GenerateUsmapAsync(new EmptyObjectsStub(),
            ct: TestContext.Current.CancellationToken, warnings: warnings);

        Assert.Empty(warnings);
    }

    [Fact]
    public void EPropertyType_NewMembers_HaveCanonicalByteValues()
    {
        // Binary contract with CUE4Parse / UE4SS. Values MUST match the canonical
        // ordering in RE-UE4SS USMapGenerator/Generator.cpp and Dumper-7 Enums.h.
        Assert.Equal(28, (byte)UsmapExportService.EPropertyType.OptionalProperty);
        Assert.Equal(29, (byte)UsmapExportService.EPropertyType.Utf8StrProperty);
        Assert.Equal(30, (byte)UsmapExportService.EPropertyType.AnsiStrProperty);
    }

    [Fact]
    public void NameTable_GetOrAdd_DeduplicatesCorrectly()
    {
        var table = new UsmapExportService.NameTable();
        var idx1 = table.GetOrAdd("Hello");
        var idx2 = table.GetOrAdd("World");
        var idx3 = table.GetOrAdd("Hello"); // duplicate

        Assert.Equal(idx1, idx3); // Same index
        Assert.NotEqual(idx1, idx2);
        Assert.Equal(2, table.Count);
    }

    // --- Round-trip: the checks that would have caught W1 -----------------------

    [Fact]
    public void BuildUsmap_FullFile_RoundTripsWithNothingLeftOver()
    {
        // Every container shape in one file, so a width mistake anywhere shows up as a desync
        // rather than as a plausible-looking number.
        var structs = new List<ClassInfoModel>
        {
            new()
            {
                Name = "FEverything", SuperName = "UObject",
                Fields =
                [
                    new FieldInfoModel { Name = "Mode", TypeName = "EnumProperty", EnumName = "EGameMode" },
                    new FieldInfoModel { Name = "Where", TypeName = "StructProperty", StructType = "FVector" },
                    new FieldInfoModel { Name = "Items", TypeName = "ArrayProperty", InnerType = "StructProperty", InnerStructType = "FVector" },
                    new FieldInfoModel { Name = "Tags", TypeName = "SetProperty", ElemType = "NameProperty" },
                    new FieldInfoModel { Name = "Lookup", TypeName = "MapProperty", KeyType = "StrProperty", ValueType = "IntProperty" },
                    new FieldInfoModel { Name = "Maybe", TypeName = "OptionalProperty", InnerType = "StructProperty", InnerStructType = "FVector" },
                    new FieldInfoModel { Name = "Hp", TypeName = "IntProperty" },
                ],
            },
        };

        var f = UsmapFile.Parse(UsmapExportService.BuildUsmap(CreateTestEnums(), structs));

        var st = Assert.Single(f.Structs);
        Assert.Equal("UObject", st.Super);
        Assert.Equal(7, st.Props.Count);
    }

    [Fact]
    public void BuildUsmap_StaticArrayProperty_CountsEverySchemaSlot()
    {
        // A static C array (Foo[4]) occupies four schema slots but is ONE record. The two counts
        // in a struct header are therefore different numbers; emitting Fields.Count for both made
        // a reader that walks slots disagree with the records it was handed.
        var structs = new List<ClassInfoModel>
        {
            new()
            {
                Name = "FArrays",
                Fields =
                [
                    new FieldInfoModel { Name = "Corners", TypeName = "IntProperty", ArrayDim = 4 },
                    new FieldInfoModel { Name = "Tail", TypeName = "IntProperty", ArrayDim = 1 },
                ],
            },
        };

        var f = UsmapFile.Parse(UsmapExportService.BuildUsmap([], structs));
        var st = Assert.Single(f.Structs);

        Assert.Equal(2, st.Props.Count);          // two records
        Assert.Equal(5, st.SlotCount);            // but five slots
        Assert.Equal(4, st.Props[0].ArrayDim);
        Assert.Equal(0, st.Props[0].SchemaIndex);
        Assert.Equal(4, st.Props[1].SchemaIndex); // the next slot after Corners[0..3]
    }

    [Fact]
    public void BuildUsmap_LargeEnum_IsNotTruncatedAt255()
    {
        // The old uint8 member count silently dropped everything past 255 — a data loss on top
        // of the desync, and exactly what LargeEnums exists to prevent.
        var entries = new List<EnumEntryValue>();
        for (int i = 0; i < 300; i++)
            entries.Add(new EnumEntryValue { Value = i, Name = $"Member{i}" });

        var enums = new List<EnumDefinition> { new() { Name = "EBig", Entries = entries } };

        var f = UsmapFile.Parse(UsmapExportService.BuildUsmap(enums, []));

        Assert.Equal(300, Assert.Single(f.Enums).Members.Count);
    }

    [Fact]
    public void BuildUsmap_UnregisteredStructName_DoesNotWriteAnIndexPastTheTable()
    {
        // The name table's length is written once, up front. A struct/enum reference resolved
        // during the write pass used to be APPENDED, producing an index past the end of the
        // table the file had already declared (audit #5 W7). UsmapFile.Parse range-checks every
        // name index, so this test fails loudly if that ever comes back.
        var structs = new List<ClassInfoModel>
        {
            new()
            {
                Name = "FHolder",
                Fields =
                [
                    // Map key/value struct names are NOT pre-registered by RegisterPropertyNames'
                    // sibling paths, so these exercise the fallback.
                    new FieldInfoModel { Name = "Lookup", TypeName = "MapProperty", KeyType = "StructProperty", ValueType = "StructProperty" },
                ],
            },
        };

        var f = UsmapFile.Parse(UsmapExportService.BuildUsmap([], structs));
        Assert.Single(f.Structs);
    }

    // ---- [A4-USMAP-ENUM-UNDERLYING] an enum's underlying type is its REAL width ----
    //
    // In unversioned cooked data an enum UPROPERTY is serialized as an integer of its own size, and CUE4Parse
    // takes that size from the mapping. Every EnumProperty was written with a Byte underlying type, so a
    // `: uint32` enum read 1 byte of 4 and misaligned every later property of its object.

    [Theory]
    [InlineData(1, (byte)0)]    // ByteProperty
    [InlineData(2, (byte)20)]   // UInt16Property
    [InlineData(4, (byte)2)]    // IntProperty
    [InlineData(8, (byte)21)]   // Int64Property
    [InlineData(3, (byte)0)]    // no enum is 3 bytes: Byte, never the unmapped 0xFF
    public void BuildUsmap_EnumProperty_WritesTheUnderlyingTypeOfItsSize(int size, byte underlying)
    {
        var structs = new List<ClassInfoModel>
        {
            new()
            {
                Name = "FSpace",
                Fields = [ new FieldInfoModel { Name = "Space", TypeName = "EnumProperty", EnumName = "EGameMode", Offset = 0, Size = size } ],
            },
        };

        var f = UsmapFile.Parse(UsmapExportService.BuildUsmap(CreateTestEnums(), structs));

        var p = Assert.Single(Assert.Single(f.Structs).Props);
        Assert.Equal((byte)26, p.Type);
        Assert.Equal(underlying, p.Underlying);
        Assert.Equal("EGameMode", p.EnumName);
    }

    [Fact]
    public void BuildUsmap_ByteEnum_IsWrittenAsTheCanonicalEnumShape()
    {
        // Arm 3: a TEnumAsByte (a ByteProperty carrying an enum) was written as a bare ByteProperty, so every
        // consumer showed its number, not its name. Both canonical writers emit [26][0][enumName].
        var structs = new List<ClassInfoModel>
        {
            new()
            {
                Name = "FHolder",
                Fields = [ new FieldInfoModel { Name = "Mode", TypeName = "ByteProperty", EnumName = "EGameMode", Offset = 0, Size = 1 } ],
            },
        };

        var f = UsmapFile.Parse(UsmapExportService.BuildUsmap(CreateTestEnums(), structs));

        var p = Assert.Single(Assert.Single(f.Structs).Props);
        Assert.Equal((byte)26, p.Type);
        Assert.Equal((byte)0, p.Underlying);
        Assert.Equal("EGameMode", p.EnumName);
    }

    [Fact]
    public void BuildUsmap_PlainByte_StaysAByteProperty()
    {
        // The control, green before and after: a ByteProperty with no enum is a plain byte.
        var structs = new List<ClassInfoModel>
        {
            new() { Name = "FHolder", Fields = [ new FieldInfoModel { Name = "Count", TypeName = "ByteProperty", Offset = 0, Size = 1 } ] },
        };

        var f = UsmapFile.Parse(UsmapExportService.BuildUsmap(CreateTestEnums(), structs));

        Assert.Equal((byte)0, Assert.Single(Assert.Single(f.Structs).Props).Type);
    }
}

// ---- USMAP round-trip reader -------------------------------------------------
//
// Audit #5 W1: the writer stamped version 3 and emitted the version-0 body, so no consumer could
// open the file. Every test in this suite passed anyway, because each one skipped a hardcoded
// 12-byte header and read the fields at the widths the WRITER happened to use — they encoded the
// bug rather than checking it.
//
// This reader parses the file the way a consumer does, at the widths the canonical writers define
// (vendor/RE-UE4SS/UE4SS/src/USMapGenerator/Generator.cpp and vendor/Dumper-7/.../
// MappingGenerator.cpp). Its most valuable assertion is not any single field: it is that the
// stream is FULLY CONSUMED. A leftover or missing byte anywhere — a width, a count, a forgotten
// field — surfaces as a desync here, which is precisely what nothing could see before.
internal sealed class UsmapFile
{
    internal sealed record UsmapEnum(string Name, List<(long Value, string Name)> Members);
    internal sealed record UsmapProp(ushort SchemaIndex, byte ArrayDim, string Name,
        byte Type = 0, byte Underlying = 0xFF, string? EnumName = null);
    internal sealed record UsmapStruct(string Name, string? Super, ushort SlotCount, List<UsmapProp> Props);

    public byte Version;
    public int HasVersionInfo;
    public byte Compression;
    public uint CompressedSize;
    public uint DecompressedSize;
    public List<string> Names = new();
    public List<UsmapEnum> Enums = new();
    public List<UsmapStruct> Structs = new();

    public static UsmapFile Parse(byte[] bytes)
    {
        var f = new UsmapFile();
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);

        var magic = r.ReadUInt16();
        Assert.Equal(0x30C4, magic);

        f.Version = r.ReadByte();
        // version >= PackageVersioning(1) => an int32 bHasVersionInfo is mandatory here.
        Assert.True(f.Version >= 1, "version must be >= 1 for this layout");
        f.HasVersionInfo = r.ReadInt32();
        Assert.Equal(0, f.HasVersionInfo);   // we never emit UE4/UE5 version blocks
        f.Compression = r.ReadByte();
        f.CompressedSize = r.ReadUInt32();
        f.DecompressedSize = r.ReadUInt32();

        // Uncompressed: both sizes describe the same payload, and it is the rest of the file.
        Assert.Equal(f.CompressedSize, f.DecompressedSize);
        Assert.Equal(bytes.Length - ms.Position, f.CompressedSize);

        var nameCount = r.ReadUInt32();
        for (uint i = 0; i < nameCount; i++)
        {
            var len = r.ReadUInt16();               // LongFName: uint16 length
            f.Names.Add(System.Text.Encoding.UTF8.GetString(r.ReadBytes(len)));
        }

        string NameAt(int idx)
        {
            Assert.InRange(idx, 0, f.Names.Count - 1);   // catches an index past the table (W7)
            return f.Names[idx];
        }

        var enumCount = r.ReadUInt32();
        for (uint i = 0; i < enumCount; i++)
        {
            var enumName = NameAt(r.ReadInt32());
            var members = new List<(long, string)>();
            var memberCount = r.ReadUInt16();       // LargeEnums: uint16, not uint8
            for (int m = 0; m < memberCount; m++)
            {
                var value = r.ReadInt64();          // ExplicitEnumValues
                members.Add((value, NameAt(r.ReadInt32())));
            }
            f.Enums.Add(new UsmapEnum(enumName, members));
        }

        // Mirrors WritePropertyType / WriteInnerPropertyTypeFromField. An enum's underlying type is itself a
        // property type, read through the inner reader -- it was skipped as ONE byte, so nothing could see
        // what was written there. [A4-USMAP-ENUM-UNDERLYING]
        (byte Type, byte Underlying, string? EnumName) ReadInner()
        {
            var t = r.ReadByte();
            switch (t)
            {
                case 9:  r.ReadInt32(); break;                                   // StructProperty -> name index
                case 26: { var u = ReadInner().Type; return (t, u, NameAt(r.ReadInt32())); }   // Enum -> underlying + name
            }
            return (t, 0xFF, null);
        }

        (byte Type, byte Underlying, string? EnumName) ReadPropertyType()
        {
            var t = r.ReadByte();
            switch (t)
            {
                case 26: { var u = ReadInner().Type; return (t, u, NameAt(r.ReadInt32())); }   // EnumProperty
                case 9:  r.ReadInt32(); break;                    // StructProperty
                case 8:                                            // ArrayProperty
                case 25:                                           // SetProperty
                case 28: ReadInner(); break;                       // OptionalProperty
                case 24: ReadInner(); ReadInner(); break;          // MapProperty (key, value)
            }
            return (t, 0xFF, null);
        }

        var structCount = r.ReadUInt32();
        for (uint i = 0; i < structCount; i++)
        {
            var name = NameAt(r.ReadInt32());
            var superIdx = r.ReadInt32();
            string? super = superIdx >= 0 ? NameAt(superIdx) : null;

            var slotCount = r.ReadUInt16();      // sum of ArrayDim
            var recordCount = r.ReadUInt16();    // number of records that follow

            var props = new List<UsmapProp>();
            for (int p = 0; p < recordCount; p++)
            {
                var schemaIdx = r.ReadUInt16();
                var arrayDim = r.ReadByte();     // ONE byte
                var propName = NameAt(r.ReadInt32());
                var (type, underlying, enumName) = ReadPropertyType();
                props.Add(new UsmapProp(schemaIdx, arrayDim, propName, type, underlying, enumName));
            }
            f.Structs.Add(new UsmapStruct(name, super, slotCount, props));
        }

        // The assertion that makes this reader worth having.
        Assert.Equal(bytes.Length, ms.Position);
        return f;
    }
}
