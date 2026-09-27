using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// A pool for <see cref="SdkExportService.GenerateFullSdkAsync"/>: each entry is one GObjects row plus
/// what <c>walk_class</c> returns for it. Paths use the DLL's own <c>Ubel::GetFullName</c> shape —
/// the package name keeps its leading '/', so a native type reads <c>//Script/Module/Name</c>.
/// </summary>
internal sealed class SdkPoolDump : StubDumpService
{
    private readonly List<UObjectNode> _objects = new();
    private readonly Dictionary<string, ClassInfoModel> _walks = new();

    public SdkPoolDump Add(string addr, string name, string meta, string path,
                           string superAddr = "", string superName = "", int size = 4,
                           params FieldInfoModel[] fields)
    {
        _objects.Add(new UObjectNode { Address = addr, Name = name, ClassName = meta });
        var info = new ClassInfoModel
        {
            Name = name, FullPath = path, SuperAddress = superAddr, SuperName = superName,
            PropertiesSize = size,
        };
        info.Fields.AddRange(fields);
        _walks[addr] = info;
        return this;
    }

    public override Task<ObjectListResult> GetObjectListAsync(int offset, int limit, CancellationToken ct = default, bool includePath = false)
    {
        var slice = _objects.Skip(offset).Take(limit).ToList();
        return Task.FromResult(new ObjectListResult { Total = _objects.Count, Scanned = slice.Count, Objects = slice });
    }

    public override Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default) =>
        Task.FromResult(_walks[addr]);

    /// <summary>The whole-pool header, synchronously: every task this fake returns is already complete.</summary>
    public string Sdk() => SdkExportService.GenerateFullSdkAsync(this).GetAwaiter().GetResult();
}

/// <summary>
/// TYPE names in the SDK header — a struct's own name, its super, and every type a member spells —
/// were emitted verbatim.
///
/// <para>Measured on a real whole-pool export (DumperTest 5.4, 7,894 structs): every AnimBlueprint
/// carries its own <c>AnimBlueprintGeneratedConstantData</c>, so ABP_Manny's and ABP_Quinn's collide,
/// and because Quinn is a child AnimBP its struct came out as
/// <c>struct AnimBlueprintGeneratedConstantData : public AnimBlueprintGeneratedConstantData</c>.
/// <c>NameTypes.h</c>'s <c>INVALID_OBJECTNAME_CHARACTERS</c> does not include <c>-</c>, so an asset
/// name can carry one into a class name.</para>
/// </summary>
public class SdkTypeNameTests
{
    private const string Manny = "//Game/Characters/Mannequins/Animations/ABP_Manny/ABP_Manny_C";
    private const string Quinn = "//Game/Characters/Mannequins/Animations/ABP_Quinn/ABP_Quinn_C";
    private const string ConstData = "AnimBlueprintGeneratedConstantData";

    private static FieldInfoModel StructMember(string name, string structType, int offset, int size = 4) =>
        new() { Name = name, TypeName = "StructProperty", StructType = structType, Offset = offset, Size = size };

    private static FieldInfoModel PtrMember(string name, string objClass, int offset) =>
        new() { Name = name, TypeName = "ObjectProperty", ObjClassName = objClass, Offset = offset, Size = 8 };

    private static FieldInfoModel Int(string name, int offset) =>
        new() { Name = name, TypeName = "IntProperty", Offset = offset, Size = 4 };

    /// <summary>The header's lines without their trailing comments, so a UE name quoted in a comment is not code.</summary>
    private static IEnumerable<string> CodeLines(string header) =>
        header.Split('\n').Select(l => l.TrimEnd('\r'))
              .Select(l => l.IndexOf("//", StringComparison.Ordinal) is var c and >= 0 ? l[..c] : l)
              .Where(l => l.Trim().Length > 0);

    // ------------------------------------------------------------------
    // One struct, no pool: every type spelling goes through the same sanitiser.
    // ------------------------------------------------------------------

    [Fact]
    public void Standalone_IllegalCharactersInTypeNames_AreSanitisedEverywhere()
    {
        var info = new ClassInfoModel
        {
            Name = "BP_Door-Big_C", SuperName = "BP_Door-Base_C", PropertiesSize = 0x40,
            Fields =
            {
                PtrMember("Target", "BP_Enemy-Boss_C", 0x00),
                StructMember("Item", "S_Item-Data", 0x08, 8),
                new FieldInfoModel { Name = "Kind", TypeName = "EnumProperty", EnumName = "E_Type-A", Offset = 0x10, Size = 1 },
                new FieldInfoModel { Name = "Items", TypeName = "ArrayProperty", InnerType = "StructProperty", InnerStructType = "S_Item-Data", Offset = 0x18, Size = 0x10 },
                new FieldInfoModel { Name = "Spawn", TypeName = "ClassProperty", ObjClassName = "BP_Enemy-Boss_C", Offset = 0x28, Size = 8 },
                new FieldInfoModel { Name = "Bag", TypeName = "MapProperty", KeyType = "StructProperty", KeyStructType = "S_Item-Data", ValueType = "IntProperty", Offset = 0x30, Size = 0x10 },
            },
        };
        var header = SdkExportService.GenerateClassHeaderFromSchema(info);

        Assert.Contains("struct BP_Door_Big_C : public BP_Door_Base_C", header);
        Assert.Contains("class BP_Enemy_Boss_C* Target;", header);
        Assert.Contains("struct S_Item_Data Item;", header);
        Assert.Contains("E_Type_A Kind;", header);
        Assert.Contains("TArray<struct S_Item_Data> Items;", header);
        Assert.Contains("TSubclassOf<class BP_Enemy_Boss_C> Spawn;", header);
        Assert.Contains("TMap<struct S_Item_Data, int32_t> Bag;", header);
        Assert.DoesNotContain(CodeLines(header), l => l.Contains('-'));
    }

    [Fact]
    public void Standalone_KeywordTypeNames_AreSuffixed()
    {
        var info = new ClassInfoModel
        {
            Name = "union", SuperName = "", PropertiesSize = 0x08,
            Fields = { StructMember("Value", "default", 0x00, 8) },
        };
        var header = SdkExportService.GenerateClassHeaderFromSchema(info);

        Assert.Contains("struct union_0", header);
        Assert.Contains("struct default_0 Value;", header);
    }

    [Fact]
    public void LiveWalkerPath_TypeNamesAreSanitisedToo()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Target", TypeName = "ObjectProperty", PtrClassName = "BP_Enemy-Boss_C", Offset = 0x00, Size = 8 },
            new() { Name = "Item", TypeName = "StructProperty", StructTypeName = "S_Item-Data", Offset = 0x08, Size = 8 },
        };
        var header = SdkExportService.GenerateClassHeader("BP_Door-Big_C", "Actor-X", 0x10, fields);

        Assert.Contains("struct BP_Door_Big_C : public Actor_X", header);
        Assert.Contains("class BP_Enemy_Boss_C* Target;", header);
        Assert.Contains("struct S_Item_Data Item;", header);
    }

    // ------------------------------------------------------------------
    // The whole pool: names are made unique, and references follow them.
    // ------------------------------------------------------------------

    private static SdkPoolDump AnimBlueprintPool() => new SdkPoolDump()
        .Add("0x1", "AnimBlueprintConstantData", "ScriptStruct", "//Script/Engine/AnimBlueprintConstantData",
             fields: new[] { Int("Base", 0) })
        .Add("0x10", ConstData, "ScriptStruct", Manny + "." + ConstData, "0x1", "AnimBlueprintConstantData", 8,
             Int("MannyOnly", 4))
        .Add("0x20", ConstData, "ScriptStruct", Quinn + "." + ConstData, "0x10", ConstData, 12,
             Int("QuinnOnly", 8))
        .Add("0x30", "ABP_Manny_C", "AnimBlueprintGeneratedClass", Manny, size: 8,
             fields: new[] { StructMember("Consts", ConstData, 0, 8) })
        .Add("0x40", "ABP_Quinn_C", "AnimBlueprintGeneratedClass", Quinn, size: 12,
             fields: new[] { StructMember("Consts", ConstData, 0, 12) });

    [Fact]
    public void Pool_AnimBlueprintDuplicates_AreQualifiedByTheirOuter()
    {
        var sdk = AnimBlueprintPool().Sdk();

        Assert.Contains($"struct {ConstData}_ABP_Manny_C : public AnimBlueprintConstantData", sdk);
        Assert.Contains($"struct {ConstData}_ABP_Quinn_C : public {ConstData}_ABP_Manny_C", sdk);
        Assert.DoesNotContain($"struct {ConstData} : public {ConstData}", sdk);
    }

    [Fact]
    public void Pool_MemberReferences_ResolveToTheNearestHolder()
    {
        // Each AnimBP's member names only the short type name; the holder under the same outer wins.
        var sdk = AnimBlueprintPool().Sdk();
        var manny = sdk[sdk.IndexOf("struct ABP_Manny_C", StringComparison.Ordinal)..];
        var quinn = sdk[sdk.IndexOf("struct ABP_Quinn_C", StringComparison.Ordinal)..];

        Assert.StartsWith("struct ABP_Manny_C", manny);
        Assert.Contains($"struct {ConstData}_ABP_Manny_C Consts;", manny[..manny.IndexOf("};", StringComparison.Ordinal)]);
        Assert.Contains($"struct {ConstData}_ABP_Quinn_C Consts;", quinn[..quinn.IndexOf("};", StringComparison.Ordinal)]);
    }

    [Fact]
    public void Pool_TheOneNativeHolder_KeepsThePlainName()
    {
        // A user-defined struct named like an engine struct: the engine one keeps `Vector`, and each
        // side's references resolve to the holder nearest to them.
        var sdk = new SdkPoolDump()
            .Add("0x1", "Vector", "ScriptStruct", "//Script/CoreUObject/Vector", size: 0x18,
                 fields: new[] { Int("X", 0) })
            .Add("0x2", "Vector", "ScriptStruct", "//Game/Maps/Stuff/Vector/Vector", size: 4,
                 fields: new[] { Int("Mine", 0) })
            .Add("0x3", "Actor", "Class", "//Script/Engine/Actor", size: 0x18,
                 fields: new[] { StructMember("Loc", "Vector", 0, 0x18) })
            .Add("0x4", "BP_X_C", "BlueprintGeneratedClass", "//Game/Maps/Stuff/BP_X/BP_X_C", size: 4,
                 fields: new[] { StructMember("Loc", "Vector", 0, 4) })
            .Sdk();

        Assert.Contains("struct Vector\n", sdk.Replace("\r\n", "\n"));
        Assert.Contains("struct Vector_Stuff\n", sdk.Replace("\r\n", "\n"));
        var actor = sdk[sdk.IndexOf("struct Actor", StringComparison.Ordinal)..];
        var bp = sdk[sdk.IndexOf("struct BP_X_C", StringComparison.Ordinal)..];
        Assert.Contains("struct Vector Loc;", actor[..actor.IndexOf("};", StringComparison.Ordinal)]);
        Assert.Contains("struct Vector_Stuff Loc;", bp[..bp.IndexOf("};", StringComparison.Ordinal)]);
    }

    [Fact]
    public void Pool_SameClassNameInTwoFolders_IsQualifiedByFolder_WhateverTheOrder()
    {
        SdkPoolDump Pool(bool reversed)
        {
            var d = new SdkPoolDump();
            var a = ("0x1", "//Game/A/BP_Door/BP_Door_C");
            var b = ("0x2", "//Game/B/BP_Door/BP_Door_C");
            foreach (var (addr, path) in reversed ? new[] { b, a } : new[] { a, b })
                d.Add(addr, "BP_Door_C", "BlueprintGeneratedClass", path, fields: new[] { Int("V", 0) });
            return d;
        }

        foreach (var sdk in new[] { Pool(false).Sdk(), Pool(true).Sdk() })
        {
            var n = sdk.Replace("\r\n", "\n");
            Assert.Contains("// //Game/A/BP_Door/BP_Door_C\nstruct BP_Door_C_A\n", n);
            Assert.Contains("// //Game/B/BP_Door/BP_Door_C\nstruct BP_Door_C_B\n", n);
        }
    }

    [Fact]
    public void Pool_ReferenceKind_PicksTheClassOrTheStruct()
    {
        // A class and a struct may share a short name in different modules; a pointer names a class,
        // a by-value member names a struct.
        var sdk = new SdkPoolDump()
            .Add("0x1", "Foo", "Class", "//Script/ModA/Foo", fields: new[] { Int("A", 0) })
            .Add("0x2", "Foo", "ScriptStruct", "//Script/ModB/Foo", fields: new[] { Int("B", 0) })
            .Add("0x3", "BP_Z_C", "BlueprintGeneratedClass", "//Game/Z/BP_Z/BP_Z_C", size: 0x10,
                 fields: new[] { PtrMember("P", "Foo", 0), StructMember("S", "Foo", 8) })
            .Sdk();

        Assert.Contains("class Foo_ModA* P;", sdk);
        Assert.Contains("struct Foo_ModB S;", sdk);
    }

    [Fact]
    public void Pool_ASanitisedName_NeverTakesARealTypesName()
    {
        var sdk = new SdkPoolDump()
            .Add("0x1", "Foo_Bar", "ScriptStruct", "//Game/P/Foo_Bar/Foo_Bar", fields: new[] { Int("Real", 0) })
            .Add("0x2", "Foo-Bar", "ScriptStruct", "//Game/Q/Foo-Bar/Foo-Bar", fields: new[] { Int("Dashed", 0) })
            .Sdk().Replace("\r\n", "\n");

        Assert.Contains("// //Game/P/Foo_Bar/Foo_Bar\nstruct Foo_Bar\n", sdk);
        Assert.Contains("// //Game/Q/Foo-Bar/Foo-Bar\nstruct Foo_Bar_Q\n", sdk);
    }

    [Fact]
    public void Pool_ATypeNamedLikeAHeaderBuiltIn_IsRenamed_AndSoAreItsReferences()
    {
        // The header spells TArray / FName / UObject itself; a pool type of that name would redefine it.
        var sdk = new SdkPoolDump()
            .Add("0x1", "TArray", "ScriptStruct", "//Script/Weird/TArray", fields: new[] { Int("V", 0) })
            .Add("0x2", "User", "Class", "//Script/Weird/User", fields: new[] { StructMember("Arr", "TArray", 0) })
            .Sdk().Replace("\r\n", "\n");

        Assert.Contains("struct TArray_Weird\n", sdk);
        Assert.Contains("struct TArray_Weird Arr;", sdk);
    }

    [Fact]
    public void Pool_AReferenceToATypeOutsideThePool_IsOnlySanitised()
    {
        // `UObject` is the emitter's own fallback spelling and is not in the pool: it must stay as is.
        var sdk = new SdkPoolDump()
            .Add("0x1", "Holder", "Class", "//Script/M/Holder", size: 0x10,
                 fields: new[] { PtrMember("O", "UObject", 0), PtrMember("E", "Else-Where", 8) })
            .Sdk();

        Assert.Contains("class UObject* O;", sdk);
        Assert.Contains("class Else_Where* E;", sdk);
    }
}
