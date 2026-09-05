using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

public class SdkExportServiceTests
{
    // --- MapCppDecl (FieldInfoModel) ---

    [Fact]
    public void MapCppDecl_IntProperty_ReturnsInt32()
    {
        var field = new FieldInfoModel { TypeName = "IntProperty", Size = 4 };
        Assert.Equal("int32_t", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_FloatProperty_ReturnsFloat()
    {
        var field = new FieldInfoModel { TypeName = "FloatProperty", Size = 4 };
        Assert.Equal("float", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_BoolProperty_ReturnsBool()
    {
        var field = new FieldInfoModel { TypeName = "BoolProperty", Size = 1 };
        Assert.Equal("bool", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_StrProperty_ReturnsFString()
    {
        var field = new FieldInfoModel { TypeName = "StrProperty", Size = 16 };
        Assert.Equal("FString", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_Utf8StrProperty_ReturnsFUtf8String()
    {
        // UE5.5+ FUtf8String — a distinct UE type, not an FString alias.
        var field = new FieldInfoModel { TypeName = "Utf8StrProperty", Size = 16 };
        Assert.Equal("FUtf8String", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_AnsiStrProperty_ReturnsFAnsiString()
    {
        // UE5.5+ FAnsiString.
        var field = new FieldInfoModel { TypeName = "AnsiStrProperty", Size = 16 };
        Assert.Equal("FAnsiString", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_Utf8StrArray_ReturnsTArrayOfFUtf8String()
    {
        // Inner-type mapping (MapScalarInnerType) must also resolve the new variants.
        var field = new FieldInfoModel
        {
            TypeName = "ArrayProperty", InnerType = "Utf8StrProperty", Size = 16,
        };
        Assert.Equal("TArray<FUtf8String>", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_ObjectProperty_WithClass_ReturnsPtr()
    {
        var field = new FieldInfoModel { TypeName = "ObjectProperty", ObjClassName = "APlayerController", Size = 8 };
        Assert.Equal("class APlayerController*", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_ObjectProperty_NoClass_ReturnsUObjectPtr()
    {
        var field = new FieldInfoModel { TypeName = "ObjectProperty", Size = 8 };
        Assert.Equal("class UObject*", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_ClassProperty_ReturnsSubclassOf()
    {
        var field = new FieldInfoModel { TypeName = "ClassProperty", ObjClassName = "AActor", Size = 8 };
        Assert.Equal("TSubclassOf<class AActor>", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_StructProperty_WithType_ReturnsStruct()
    {
        var field = new FieldInfoModel { TypeName = "StructProperty", StructType = "FVector", Size = 24 };
        Assert.Equal("struct FVector", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_StructProperty_NoType_ReturnsRawBytes()
    {
        // The extent is NOT part of the type — it belongs after the identifier. See CppDecl.
        var field = new FieldInfoModel { TypeName = "StructProperty", Size = 12 };
        var decl = SdkExportService.MapCppDecl(field);
        Assert.Equal("uint8_t", decl.Type);
        Assert.Equal("[0xC]", decl.ArraySuffix);
    }

    [Fact]
    public void MapCppDecl_ArrayOfStruct_ReturnsTArray()
    {
        var field = new FieldInfoModel
        {
            TypeName = "ArrayProperty", InnerType = "StructProperty",
            InnerStructType = "FVector", Size = 16,
        };
        Assert.Equal("TArray<struct FVector>", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_ArrayOfObject_ReturnsTArrayPtr()
    {
        var field = new FieldInfoModel
        {
            TypeName = "ArrayProperty", InnerType = "ObjectProperty",
            InnerObjClass = "UActorComponent", Size = 16,
        };
        Assert.Equal("TArray<class UActorComponent*>", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_ArrayOfFloat_ReturnsTArrayFloat()
    {
        var field = new FieldInfoModel
        {
            TypeName = "ArrayProperty", InnerType = "FloatProperty", Size = 16,
        };
        Assert.Equal("TArray<float>", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_MapProperty_ReturnsCorrect()
    {
        var field = new FieldInfoModel
        {
            TypeName = "MapProperty",
            KeyType = "StrProperty", ValueType = "IntProperty",
            Size = 80,
        };
        Assert.Equal("TMap<FString, int32_t>", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_SetProperty_ReturnsCorrect()
    {
        var field = new FieldInfoModel
        {
            TypeName = "SetProperty", ElemType = "NameProperty", Size = 80,
        };
        Assert.Equal("TSet<FName>", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_EnumProperty_WithName_ReturnsEnumName()
    {
        var field = new FieldInfoModel { TypeName = "EnumProperty", EnumName = "EMovementMode", Size = 1 };
        Assert.Equal("EMovementMode", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_ByteProperty_WithEnum_ReturnsEnumName()
    {
        var field = new FieldInfoModel { TypeName = "ByteProperty", EnumName = "ENetRole", Size = 1 };
        Assert.Equal("ENetRole", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_ByteProperty_Raw_ReturnsUint8()
    {
        var field = new FieldInfoModel { TypeName = "ByteProperty", Size = 1 };
        Assert.Equal("uint8_t", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_WeakObjectProperty_ReturnsWeak()
    {
        var field = new FieldInfoModel { TypeName = "WeakObjectProperty", ObjClassName = "AActor", Size = 8 };
        Assert.Equal("TWeakObjectPtr<class AActor>", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDecl_DelegateProperty_ReturnsDelegate()
    {
        var field = new FieldInfoModel { TypeName = "DelegateProperty", Size = 16 };
        Assert.Equal("FScriptDelegate", SdkExportService.MapCppDecl(field).Type);
    }

    // --- MapCppDecl (LiveFieldValue) ---

    [Fact]
    public void MapCppDeclLive_ObjectProperty_WithClass()
    {
        var field = new LiveFieldValue
        {
            TypeName = "ObjectProperty", PtrClassName = "USceneComponent", Size = 8,
        };
        Assert.Equal("class USceneComponent*", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDeclLive_StructProperty_WithType()
    {
        var field = new LiveFieldValue
        {
            TypeName = "StructProperty", StructTypeName = "FTransform", Size = 96,
        };
        Assert.Equal("struct FTransform", SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void MapCppDeclLive_ArrayProperty_WithInner()
    {
        var field = new LiveFieldValue
        {
            TypeName = "ArrayProperty", ArrayInnerType = "StructProperty",
            ArrayStructType = "FHitResult", Size = 16,
        };
        Assert.Equal("TArray<struct FHitResult>", SdkExportService.MapCppDecl(field).Type);
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
    public void GenerateClassHeader_BoolBitfield_EmitsBitfieldAtItsTrueBit()
    {
        // Mask 0x04 is a PACKED bitfield (one bit, bit 2), not a native bool. It used to be
        // emitted as a whole `bool`, which put it at bit 0 and — once a class had more than one
        // such flag — made the struct longer than the game's (audit #5 W3). The mask comment,
        // which is what this test was originally written for, is unchanged.
        var fields = new List<LiveFieldValue>
        {
            new()
            {
                Name = "bHidden", TypeName = "BoolProperty",
                Offset = 0x10, Size = 1, BoolFieldMask = 0x04,
            },
        };

        var header = SdkExportService.GenerateClassHeader("AActor", "", 0x18, fields);

        Assert.Contains("uint8_t bHidden : 1;", header);
        Assert.Contains("uint8_t : 2;", header);          // bits 0-1 are UE's, not ours to reuse
        Assert.Contains("[Mask: 0x04]", header);
        Assert.DoesNotContain("bool bHidden;", header);
    }

    [Fact]
    public void GenerateClassHeader_EightPackedBools_DoNotGrowTheStruct()
    {
        // The case the old emitter could not survive: eight flags share ONE byte at 0x10, and a
        // float follows at 0x14. Emitting eight `bool` members advanced the cursor to 0x18, past
        // the float — and padding is only emitted when the next offset is AHEAD of the cursor, so
        // nothing could compensate and every later member was displaced.
        var fields = new List<LiveFieldValue>();
        for (int bit = 0; bit < 8; bit++)
            fields.Add(new LiveFieldValue
            {
                Name = $"bFlag{bit}", TypeName = "BoolProperty",
                Offset = 0x10, Size = 1, BoolFieldMask = 1 << bit,
            });
        fields.Add(new LiveFieldValue
        {
            Name = "Speed", TypeName = "FloatProperty", Offset = 0x14, Size = 4,
        });

        var header = SdkExportService.GenerateClassHeader("AActor", "", 0x18, fields);

        for (int bit = 0; bit < 8; bit++)
            Assert.Contains($"uint8_t bFlag{bit} : 1;", header);
        Assert.DoesNotContain("uint8_t : ", header);       // all eight bits used, no filler
        Assert.Contains("float Speed;", header);
        // The eight flags occupy 0x10..0x11, so 3 bytes of padding must reach the float at 0x14.
        Assert.Contains("Pad_0011[0x0003]", header);
    }

    [Fact]
    public void GenerateClassHeader_NativeBool_StaysAWholeBool()
    {
        // FieldMask 0xFF is a real `bool` that owns its byte — it must NOT become a bitfield.
        var fields = new List<LiveFieldValue>
        {
            new()
            {
                Name = "bReplicates", TypeName = "BoolProperty",
                Offset = 0x10, Size = 1, BoolFieldMask = 0xFF,
            },
        };

        var header = SdkExportService.GenerateClassHeader("AActor", "", 0x18, fields);

        Assert.Contains("bool bReplicates;", header);
        Assert.DoesNotContain(" : 1;", header);
    }

    // --- Empty-base structs (audit A7) ---

    [Fact]
    public void GenerateClassHeaderFromSchema_EmptyBase_OwnFieldAtOffsetZeroSurvives()
    {
        // THE REAL SHAPE, not an invented deep intrusion. UE sets a native USTRUCT's
        // PropertiesSize from CppStructOps->GetSize() (Class.cpp:947), so an EMPTY base
        // reports 1 -- while C++ empty-base optimisation puts the derived struct's first
        // member at offset 0. `Offset >= superPropsSize` then dropped it, and the trailing
        // pad pass replaced it with uint8_t Pad_0001[0x0003].
        //
        // BEFORE the fix this test fails on the first assert: "float Weight;" is absent and
        // "Pad_0001" is present in its place.
        //
        // UE 5.8.2 ships 62 such bases with 302 property-bearing children -- FEmptyPayload
        // (Engine module, no editor guard), the FMassFragment family, FEditorDataStorageColumn.
        var classInfo = new ClassInfoModel
        {
            Name = "FAttributePayload",
            SuperName = "FEmptyPayload",
            PropertiesSize = 4,
            SuperPropertiesSize = 1,   // sizeof(empty struct) == 1, NOT 0
            OwnPropertiesStart = 0,    // EBO: the derived member really is at 0
            Fields = { new FieldInfoModel { Name = "Weight", TypeName = "FloatProperty", Offset = 0, Size = 4 } },
        };

        var header = SdkExportService.GenerateClassHeaderFromSchema(classInfo);

        Assert.Contains(": public FEmptyPayload", header);
        Assert.Contains("float Weight;", header);
        Assert.DoesNotContain("Pad_0001", header);
    }

    [Fact]
    public void GenerateClassHeaderFromSchema_EmptyBase_NegativeOwnStartKeepsW2Behaviour()
    {
        // ⚠ THE REGRESSION GUARD. -1 means "the DLL said nothing", and folding it into the
        // min() would drive ownStart to -1/0 and re-emit the ENTIRE inherited chain -- audit
        // #5 W2, a far worse bug than the one A7 fixes. A pre-fix DLL must behave exactly as
        // it did before.
        var classInfo = new ClassInfoModel
        {
            Name = "BP_Player_C",
            SuperName = "AActor",
            PropertiesSize = 0x2A0,
            SuperPropertiesSize = 0x290,
            OwnPropertiesStart = -1,   // pre-fix DLL
            Fields =
            {
                new FieldInfoModel { Name = "RootComponent", TypeName = "ObjectProperty", Offset = 0x120, Size = 8 },
                new FieldInfoModel { Name = "Health", TypeName = "FloatProperty", Offset = 0x290, Size = 4 },
            },
        };

        var header = SdkExportService.GenerateClassHeaderFromSchema(classInfo);

        Assert.Contains("float Health;", header);
        Assert.DoesNotContain("RootComponent", header);
    }

    [Fact]
    public void GenerateClassHeaderFromSchema_OwnStartNeverRaisesTheFloor()
    {
        // Only ever a LOWER floor. A class whose own properties start ABOVE the super's size
        // (padding between base and derived) must not have inherited fields let back in.
        var classInfo = new ClassInfoModel
        {
            Name = "Derived",
            SuperName = "Base",
            PropertiesSize = 0x20,
            SuperPropertiesSize = 0x10,
            OwnPropertiesStart = 0x18,   // higher than the super size
            Fields =
            {
                new FieldInfoModel { Name = "Inherited", TypeName = "IntProperty", Offset = 0x08, Size = 4 },
                new FieldInfoModel { Name = "Own", TypeName = "IntProperty", Offset = 0x18, Size = 4 },
            },
        };

        var header = SdkExportService.GenerateClassHeaderFromSchema(classInfo);

        Assert.Contains("Own;", header);
        Assert.DoesNotContain("Inherited", header);
    }

    [Fact]
    public void GenerateClassHeaderFromSchema_EmptyStructIsEmittedEmptySoEboApplies()
    {
        // ⚠ The SECOND half of A7, and fixing ownStart alone does not cover it. The empty
        // BASE itself has PropertiesSize 1, so the trailing-pad pass emitted
        // `uint8_t Pad_0000[0x0001];` -- which makes the emitted base NON-empty, defeats
        // empty-base optimisation in the generated C++, and puts every derived struct's first
        // member at 1 where the game has it at 0. `struct X {};` is already sizeof 1, so the
        // size comment stays honest.
        var classInfo = new ClassInfoModel
        {
            Name = "FEmptyPayload",
            SuperName = "",
            PropertiesSize = 1,
            SuperPropertiesSize = 0,
            OwnPropertiesStart = -1,   // genuinely declares nothing
        };

        var header = SdkExportService.GenerateClassHeaderFromSchema(classInfo);

        Assert.DoesNotContain("Pad_", header);
        Assert.Contains("Size: 0x0001", header);
    }

    [Fact]
    public void GenerateClassHeaderFromSchema_OpaqueStructKeepsItsPadding()
    {
        // NEGATIVE CONTROL for the above: the suppression is narrow. A struct with no
        // properties but a real size must still be padded to that size, or every consumer
        // of the header gets the wrong sizeof.
        var classInfo = new ClassInfoModel
        {
            Name = "FOpaqueThing",
            SuperName = "",
            PropertiesSize = 0x10,
            SuperPropertiesSize = 0,
            OwnPropertiesStart = -1,
        };

        var header = SdkExportService.GenerateClassHeaderFromSchema(classInfo);

        Assert.Contains("Pad_", header);
        Assert.Contains("Size: 0x0010", header);
    }

    // --- Inherited properties (audit #5 W2) ---

    [Fact]
    public void GenerateClassHeaderFromSchema_InheritedFieldsAreNotRedeclared()
    {
        // walk_class returns the WHOLE SuperStruct chain, so without the boundary the emitter
        // re-declares every base property inside a struct that already inherits it — which
        // compiles, and puts offsetof wrong for every derived class.
        var classInfo = new ClassInfoModel
        {
            Name = "BP_Player_C",
            SuperName = "AActor",
            PropertiesSize = 0x2A0,
            SuperPropertiesSize = 0x290,
            Fields =
            {
                new FieldInfoModel { Name = "RootComponent", TypeName = "ObjectProperty", Offset = 0x120, Size = 8 },
                new FieldInfoModel { Name = "bHidden", TypeName = "BoolProperty", Offset = 0x88, Size = 1, BoolFieldMask = 0x01 },
                new FieldInfoModel { Name = "Health", TypeName = "FloatProperty", Offset = 0x290, Size = 4 },
            },
        };

        var header = SdkExportService.GenerateClassHeaderFromSchema(classInfo);

        Assert.Contains(": public AActor", header);
        Assert.Contains("float Health;", header);
        Assert.DoesNotContain("RootComponent", header);
        Assert.DoesNotContain("bHidden", header);
    }

    [Fact]
    public void GenerateClassHeaderFromSchema_WithoutBoundary_KeepsTheLegacyHeuristic()
    {
        // An older DLL sends no super_props_size. The emitter must still produce what it always
        // did rather than dropping everything or nothing.
        var classInfo = new ClassInfoModel
        {
            Name = "BP_Player_C",
            SuperName = "AActor",
            PropertiesSize = 0x2A0,
            SuperPropertiesSize = 0,
            Fields =
            {
                new FieldInfoModel { Name = "Health", TypeName = "FloatProperty", Offset = 0x290, Size = 4 },
            },
        };

        var header = SdkExportService.GenerateClassHeaderFromSchema(classInfo);

        Assert.Contains("float Health;", header);
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

    // ------------------------------------------------------------------
    // GenerateFullSdkAsync — class-like meta filter (build 689 fix).
    // The original `obj.ClassName is "Class" or "ScriptStruct"` silently
    // dropped every BlueprintGeneratedClass — same bug class that the
    // DLL build 673 fix addressed across SearchProperties / ListClasses
    // / EnumerateAllFunctions. Mirrors the DumpAllService filter.
    // ------------------------------------------------------------------

    private sealed class FakeSdkDump : StubDumpService
    {
        public List<UObjectNode> Objects { get; } = new();
        public Dictionary<string, ClassInfoModel> ClassWalks { get; } = new();

        public override Task<ObjectListResult> GetObjectListAsync(int offset, int limit, CancellationToken ct = default, bool includePath = false)
        {
            var slice = Objects.Skip(offset).Take(limit).ToList();
            return Task.FromResult(new ObjectListResult
            {
                Total = Objects.Count,
                Scanned = slice.Count,
                Objects = slice,
            });
        }

        public override Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default)
        {
            return Task.FromResult(ClassWalks.TryGetValue(addr, out var ci)
                ? ci
                : new ClassInfoModel { Fields = new List<FieldInfoModel>() });
        }
    }

    private static UObjectNode Obj(string addr, string name, string className, string path = "") =>
        new() { Address = addr, Name = name, ClassName = className, FullPath = path };

    [Fact]
    public async Task GenerateFullSdkAsync_AcceptsAllClassLikeMetasAndScriptStruct()
    {
        var dump = new FakeSdkDump();
        dump.Objects.Add(Obj("0x1", "AActor",      "Class",                          "/Script/Engine.Actor"));
        dump.Objects.Add(Obj("0x2", "BP_Player_C", "BlueprintGeneratedClass",        "/Game/MyGame/BP_Player_C"));
        dump.Objects.Add(Obj("0x3", "MyAnimBP_C",  "AnimBlueprintGeneratedClass",    "/Game/Anim/MyAnimBP_C"));
        dump.Objects.Add(Obj("0x4", "MyWidget_C",  "WidgetBlueprintGeneratedClass",  "/Game/UI/MyWidget_C"));
        dump.Objects.Add(Obj("0x5", "MyDynamic_C", "DynamicClass",                   "/Game/MyGame/MyDynamic_C"));
        dump.Objects.Add(Obj("0x6", "FVector",     "ScriptStruct",                   "/Script/CoreUObject.Vector"));
        dump.Objects.Add(Obj("0x7", "Player1",     "BP_Player_C",                    "/Game/Persistent.Player1")); // live instance — dropped
        dump.Objects.Add(Obj("0x8", "Func1",       "Function",                       "/Script/Engine.Func1"));     // dropped

        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "AActor",      SuperName = "UObject", PropertiesSize = 0x40 };
        dump.ClassWalks["0x2"] = new ClassInfoModel { Name = "BP_Player_C", SuperName = "AActor",  PropertiesSize = 0x80 };
        dump.ClassWalks["0x3"] = new ClassInfoModel { Name = "MyAnimBP_C",  SuperName = "AActor",  PropertiesSize = 0x40 };
        dump.ClassWalks["0x4"] = new ClassInfoModel { Name = "MyWidget_C",  SuperName = "UObject", PropertiesSize = 0x40 };
        dump.ClassWalks["0x5"] = new ClassInfoModel { Name = "MyDynamic_C", SuperName = "UObject", PropertiesSize = 0x40 };
        dump.ClassWalks["0x6"] = new ClassInfoModel { Name = "FVector",     SuperName = "",        PropertiesSize = 0x0C };

        var sdk = await SdkExportService.GenerateFullSdkAsync(dump, ct: TestContext.Current.CancellationToken);

        // All 5 class-like metas + the ScriptStruct should be in the output;
        // the live instance and the Function should NOT.
        Assert.Contains("struct AActor", sdk);
        Assert.Contains("struct BP_Player_C", sdk);
        Assert.Contains("struct MyAnimBP_C", sdk);
        Assert.Contains("struct MyWidget_C", sdk);
        Assert.Contains("struct MyDynamic_C", sdk);
        Assert.Contains("struct FVector", sdk);
        Assert.DoesNotContain("struct Player1", sdk);
        Assert.DoesNotContain("struct Func1", sdk);
    }

    // --- Enum underlying-type inference (Dumper-7 parity: don't truncate wide enums) ---

    private static EnumDefinition MakeEnum(string name, params long[] values)
    {
        var def = new EnumDefinition { Name = name };
        for (int i = 0; i < values.Length; i++)
            def.Entries.Add(new EnumEntryValue { Name = $"{name}_{i}", Value = values[i] });
        return def;
    }

    [Fact]
    public void InferEnumUnderlyingType_SmallUnsignedValues_ReturnsUint8()
    {
        Assert.Equal("uint8_t", SdkExportService.InferEnumUnderlyingType(MakeEnum("E", 0, 1, 2, 255).Entries));
    }

    [Fact]
    public void InferEnumUnderlyingType_ValueOver255_WidensToUint16()
    {
        // 256 cannot fit in uint8_t — the exact case Dumper-7 fixed.
        Assert.Equal("uint16_t", SdkExportService.InferEnumUnderlyingType(MakeEnum("E", 0, 256).Entries));
    }

    [Fact]
    public void InferEnumUnderlyingType_LargeValues_WidenToUint32AndUint64()
    {
        Assert.Equal("uint32_t", SdkExportService.InferEnumUnderlyingType(MakeEnum("E", 0, 70000).Entries));
        Assert.Equal("uint64_t", SdkExportService.InferEnumUnderlyingType(MakeEnum("E", 0, 5_000_000_000L).Entries));
    }

    [Fact]
    public void InferEnumUnderlyingType_NegativeValues_SelectSignedType()
    {
        Assert.Equal("int8_t", SdkExportService.InferEnumUnderlyingType(MakeEnum("E", -1, 0, 1).Entries));
        Assert.Equal("int16_t", SdkExportService.InferEnumUnderlyingType(MakeEnum("E", -1, 1000).Entries));
    }

    [Fact]
    public void InferEnumUnderlyingType_Empty_DefaultsToUint8()
    {
        Assert.Equal("uint8_t", SdkExportService.InferEnumUnderlyingType(MakeEnum("E").Entries));
    }

    [Fact]
    public void GenerateEnumDefinition_WideValues_EmitsWiderUnderlyingType()
    {
        var def = MakeEnum("EBigEnum", 0, 1, 1000);
        var code = SdkExportService.GenerateEnumDefinition(def);
        Assert.Contains("enum class EBigEnum : uint16_t", code);
        Assert.DoesNotContain(": uint8_t", code);
    }

    [Fact]
    public void GenerateEnumDefinition_ExplicitUnderlyingType_Wins()
    {
        var def = MakeEnum("EForced", 0, 1000);
        var code = SdkExportService.GenerateEnumDefinition(def, "uint8_t");
        Assert.Contains("enum class EForced : uint8_t", code);
    }
}
