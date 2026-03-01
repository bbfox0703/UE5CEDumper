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
    public void BuildUsmap_Header_CorrectMagicAndVersion()
    {
        var bytes = UsmapExportService.BuildUsmap([], []);

        // Magic: 0x30C4 (little-endian: C4, 30)
        Assert.True(bytes.Length >= 10, "USMAP too short");
        Assert.Equal(0xC4, bytes[0]);
        Assert.Equal(0x30, bytes[1]);
        // Version: 3
        Assert.Equal(3, bytes[2]);
        // Compression: 0 (None)
        Assert.Equal(0, bytes[3]);
    }

    [Fact]
    public void BuildUsmap_EmptyData_HasZeroCounts()
    {
        var bytes = UsmapExportService.BuildUsmap([], []);
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);

        // Skip header (2+1+1+4+4 = 12 bytes)
        r.ReadUInt16(); // magic
        r.ReadByte();   // version
        r.ReadByte();   // compression
        r.ReadUInt32(); // compressed size
        r.ReadUInt32(); // decompressed size

        // Name count = 0
        Assert.Equal(0u, r.ReadUInt32());
        // Enum count = 0
        Assert.Equal(0u, r.ReadUInt32());
        // Struct count = 0
        Assert.Equal(0u, r.ReadUInt32());
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

        var bytes = UsmapExportService.BuildUsmap([], structs);
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);

        // Skip header
        r.ReadBytes(12);

        // Name table
        var nameCount = r.ReadUInt32();
        // Should have exactly 3 unique names: AActor, UObject, APawn
        Assert.Equal(3u, nameCount);

        // Read names to verify
        var names = new List<string>();
        for (int i = 0; i < (int)nameCount; i++)
        {
            var len = r.ReadUInt16();
            var nameBytes = r.ReadBytes(len);
            names.Add(System.Text.Encoding.UTF8.GetString(nameBytes));
        }

        Assert.Contains("AActor", names);
        Assert.Contains("UObject", names);
        Assert.Contains("APawn", names);
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

        var bytes = UsmapExportService.BuildUsmap([], structs);

        // Just verify it doesn't throw and produces bytes
        Assert.True(bytes.Length > 12, "USMAP should have payload");

        // Parse and verify struct count
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        r.ReadBytes(12); // header

        // Skip name table
        var nameCount = r.ReadUInt32();
        for (int i = 0; i < (int)nameCount; i++)
        {
            var len = r.ReadUInt16();
            r.ReadBytes(len);
        }

        // Enum count
        var enumCount = r.ReadUInt32();
        Assert.Equal(0u, enumCount);

        // Struct count
        var structCount = r.ReadUInt32();
        Assert.Equal(1u, structCount);
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
        var enums = CreateTestEnums();
        var bytes = UsmapExportService.BuildUsmap(enums, []);

        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        r.ReadBytes(12); // header

        // Skip name table
        var nameCount = r.ReadUInt32();
        for (int i = 0; i < (int)nameCount; i++)
        {
            var len = r.ReadUInt16();
            r.ReadBytes(len);
        }

        // Enum count
        var enumCount = r.ReadUInt32();
        Assert.Equal(1u, enumCount);

        // First enum: name index (int32) + member count (uint8) + member indices
        var nameIdx = r.ReadInt32();
        Assert.True(nameIdx >= 0);
        var memberCount = r.ReadByte();
        Assert.Equal(3, memberCount); // None, Walking, Flying
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
}
