using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

public class SdkExportServiceTests
{
    // --- MapCppType (FieldInfoModel) ---

    [Fact]
    public void MapCppType_IntProperty_ReturnsInt32()
    {
        var field = new FieldInfoModel { TypeName = "IntProperty", Size = 4 };
        Assert.Equal("int32_t", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_FloatProperty_ReturnsFloat()
    {
        var field = new FieldInfoModel { TypeName = "FloatProperty", Size = 4 };
        Assert.Equal("float", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_BoolProperty_ReturnsBool()
    {
        var field = new FieldInfoModel { TypeName = "BoolProperty", Size = 1 };
        Assert.Equal("bool", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_StrProperty_ReturnsFString()
    {
        var field = new FieldInfoModel { TypeName = "StrProperty", Size = 16 };
        Assert.Equal("FString", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_ObjectProperty_WithClass_ReturnsPtr()
    {
        var field = new FieldInfoModel { TypeName = "ObjectProperty", ObjClassName = "APlayerController", Size = 8 };
        Assert.Equal("class APlayerController*", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_ObjectProperty_NoClass_ReturnsUObjectPtr()
    {
        var field = new FieldInfoModel { TypeName = "ObjectProperty", Size = 8 };
        Assert.Equal("class UObject*", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_ClassProperty_ReturnsSubclassOf()
    {
        var field = new FieldInfoModel { TypeName = "ClassProperty", ObjClassName = "AActor", Size = 8 };
        Assert.Equal("TSubclassOf<class AActor>", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_StructProperty_WithType_ReturnsStruct()
    {
        var field = new FieldInfoModel { TypeName = "StructProperty", StructType = "FVector", Size = 24 };
        Assert.Equal("struct FVector", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_StructProperty_NoType_ReturnsRawBytes()
    {
        var field = new FieldInfoModel { TypeName = "StructProperty", Size = 12 };
        Assert.Equal("uint8_t[0xC]", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_ArrayOfStruct_ReturnsTArray()
    {
        var field = new FieldInfoModel
        {
            TypeName = "ArrayProperty", InnerType = "StructProperty",
            InnerStructType = "FVector", Size = 16,
        };
        Assert.Equal("TArray<struct FVector>", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_ArrayOfObject_ReturnsTArrayPtr()
    {
        var field = new FieldInfoModel
        {
            TypeName = "ArrayProperty", InnerType = "ObjectProperty",
            InnerObjClass = "UActorComponent", Size = 16,
        };
        Assert.Equal("TArray<class UActorComponent*>", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_ArrayOfFloat_ReturnsTArrayFloat()
    {
        var field = new FieldInfoModel
        {
            TypeName = "ArrayProperty", InnerType = "FloatProperty", Size = 16,
        };
        Assert.Equal("TArray<float>", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_MapProperty_ReturnsCorrect()
    {
        var field = new FieldInfoModel
        {
            TypeName = "MapProperty",
            KeyType = "StrProperty", ValueType = "IntProperty",
            Size = 80,
        };
        Assert.Equal("TMap<FString, int32_t>", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_SetProperty_ReturnsCorrect()
    {
        var field = new FieldInfoModel
        {
            TypeName = "SetProperty", ElemType = "NameProperty", Size = 80,
        };
        Assert.Equal("TSet<FName>", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_EnumProperty_WithName_ReturnsEnumName()
    {
        var field = new FieldInfoModel { TypeName = "EnumProperty", EnumName = "EMovementMode", Size = 1 };
        Assert.Equal("EMovementMode", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_ByteProperty_WithEnum_ReturnsEnumName()
    {
        var field = new FieldInfoModel { TypeName = "ByteProperty", EnumName = "ENetRole", Size = 1 };
        Assert.Equal("ENetRole", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_ByteProperty_Raw_ReturnsUint8()
    {
        var field = new FieldInfoModel { TypeName = "ByteProperty", Size = 1 };
        Assert.Equal("uint8_t", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_WeakObjectProperty_ReturnsWeak()
    {
        var field = new FieldInfoModel { TypeName = "WeakObjectProperty", ObjClassName = "AActor", Size = 8 };
        Assert.Equal("TWeakObjectPtr<class AActor>", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppType_DelegateProperty_ReturnsDelegate()
    {
        var field = new FieldInfoModel { TypeName = "DelegateProperty", Size = 16 };
        Assert.Equal("FScriptDelegate", SdkExportService.MapCppType(field));
    }

    // --- MapCppType (LiveFieldValue) ---

    [Fact]
    public void MapCppTypeLive_ObjectProperty_WithClass()
    {
        var field = new LiveFieldValue
        {
            TypeName = "ObjectProperty", PtrClassName = "USceneComponent", Size = 8,
        };
        Assert.Equal("class USceneComponent*", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppTypeLive_StructProperty_WithType()
    {
        var field = new LiveFieldValue
        {
            TypeName = "StructProperty", StructTypeName = "FTransform", Size = 96,
        };
        Assert.Equal("struct FTransform", SdkExportService.MapCppType(field));
    }

    [Fact]
    public void MapCppTypeLive_ArrayProperty_WithInner()
    {
        var field = new LiveFieldValue
        {
            TypeName = "ArrayProperty", ArrayInnerType = "StructProperty",
            ArrayStructType = "FHitResult", Size = 16,
        };
        Assert.Equal("TArray<struct FHitResult>", SdkExportService.MapCppType(field));
    }

    // --- GenerateClassHeader ---

    [Fact]
    public void GenerateClassHeader_WithPadding_EmitsPad()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x100, Size = 4 },
            new() { Name = "MaxHealth", TypeName = "FloatProperty", Offset = 0x110, Size = 4 },
        };

        var header = SdkExportService.GenerateClassHeader("AMyActor", "AActor", 0x120, fields);

        Assert.Contains("struct AMyActor : public AActor", header);
        Assert.Contains("float Health;", header);
        Assert.Contains("float MaxHealth;", header);
        // Should have padding between Health (0x100+4=0x104) and MaxHealth (0x110)
        Assert.Contains("Pad_0104", header);
        Assert.Contains("[0x000C]", header); // 0x110 - 0x104 = 0xC
        // Tail padding from MaxHealth end (0x114) to 0x120
        Assert.Contains("Pad_0114", header);
        // Size comment
        Assert.Contains("Size: 0x0120", header);
    }

    [Fact]
    public void GenerateClassHeader_WithInheritance_EmitsSuper()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Speed", TypeName = "FloatProperty", Offset = 0x28, Size = 4 },
        };

        var header = SdkExportService.GenerateClassHeader("APawn", "AActor", 0x30, fields);

        Assert.Contains(": public AActor", header);
        Assert.Contains("float Speed;", header);
    }

    [Fact]
    public void GenerateClassHeader_BoolBitfield_EmitsComment()
    {
        var fields = new List<LiveFieldValue>
        {
            new()
            {
                Name = "bHidden", TypeName = "BoolProperty",
                Offset = 0x10, Size = 1, BoolFieldMask = 0x04,
            },
        };

        var header = SdkExportService.GenerateClassHeader("AActor", "", 0x18, fields);

        Assert.Contains("bool bHidden;", header);
        Assert.Contains("[Mask: 0x04]", header);
    }

    [Fact]
    public void GenerateClassHeader_NoSuper_NoInheritance()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Value", TypeName = "IntProperty", Offset = 0, Size = 4 },
        };

        var header = SdkExportService.GenerateClassHeader("FMyStruct", "", 4, fields);

        Assert.Contains("struct FMyStruct", header);
        Assert.Contains("{", header);
        Assert.DoesNotContain(": public", header);
    }

    // --- GenerateClassHeaderFromSchema ---

    [Fact]
    public void GenerateClassHeaderFromSchema_BasicFields_CorrectOutput()
    {
        var classInfo = new ClassInfoModel
        {
            Name = "AActor",
            FullPath = "/Script/Engine.Actor",
            SuperName = "UObject",
            PropertiesSize = 0x40,
            Fields =
            [
                new FieldInfoModel { Name = "bHidden", TypeName = "BoolProperty", Offset = 0x28, Size = 1 },
                new FieldInfoModel { Name = "InitialLifeSpan", TypeName = "FloatProperty", Offset = 0x30, Size = 4 },
            ],
        };

        var header = SdkExportService.GenerateClassHeaderFromSchema(classInfo);

        Assert.Contains("/Script/Engine.Actor", header);
        Assert.Contains("struct AActor : public UObject", header);
        Assert.Contains("bool bHidden;", header);
        Assert.Contains("float InitialLifeSpan;", header);
        Assert.Contains("Size: 0x0040", header);
    }

    [Fact]
    public void GenerateClassHeaderFromSchema_WithContainerTypes()
    {
        var classInfo = new ClassInfoModel
        {
            Name = "AActor",
            SuperName = "UObject",
            PropertiesSize = 0x80,
            Fields =
            [
                new FieldInfoModel
                {
                    Name = "OwnedComponents", TypeName = "ArrayProperty",
                    InnerType = "ObjectProperty", InnerObjClass = "UActorComponent",
                    Offset = 0x28, Size = 16,
                },
                new FieldInfoModel
                {
                    Name = "Tags", TypeName = "ArrayProperty",
                    InnerType = "NameProperty",
                    Offset = 0x38, Size = 16,
                },
            ],
        };

        var header = SdkExportService.GenerateClassHeaderFromSchema(classInfo);

        Assert.Contains("TArray<class UActorComponent*> OwnedComponents;", header);
        Assert.Contains("TArray<FName> Tags;", header);
    }

    // --- File header ---

    [Fact]
    public void GenerateClassHeader_HasAutoGeneratedComment()
    {
        var header = SdkExportService.GenerateClassHeader("Test", "", 0, []);
        Assert.Contains("Auto-generated by UE5CEDumper", header);
    }
}
