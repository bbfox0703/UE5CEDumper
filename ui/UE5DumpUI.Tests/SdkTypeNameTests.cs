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

    /// <summary>A class the DLL refuses: <c>Ubel::WalkClassEx</c> answers with an EMPTY ClassInfo
    /// (no name, no path, no fields), not an error.</summary>
    public SdkPoolDump AddRefused(string addr, string name, string meta)
    {
        _objects.Add(new UObjectNode { Address = addr, Name = name, ClassName = meta });
        _walks[addr] = new ClassInfoModel();
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
            Name = "BP_Door-Big_C", SuperName = "BP_Door-Base_C", PropertiesSize = 0x60,
            Fields =
            {
                PtrMember("Target", "BP_Enemy-Boss_C", 0x00),
                StructMember("Item", "S_Item-Data", 0x08, 8),
                new FieldInfoModel { Name = "Kind", TypeName = "EnumProperty", EnumName = "E_Type-A", Offset = 0x10, Size = 1 },
                new FieldInfoModel { Name = "Items", TypeName = "ArrayProperty", InnerType = "StructProperty", InnerStructType = "S_Item-Data", Offset = 0x18, Size = 0x10 },
                new FieldInfoModel { Name = "Spawn", TypeName = "ClassProperty", ObjClassName = "BP_Enemy-Boss_C", Offset = 0x28, Size = 8 },
                new FieldInfoModel { Name = "Bag", TypeName = "MapProperty", KeyType = "StructProperty", KeyStructType = "S_Item-Data", ValueType = "IntProperty", Offset = 0x30, Size = 0x10 },
                new FieldInfoModel { Name = "Loot", TypeName = "MapProperty", KeyType = "IntProperty", ValueType = "StructProperty", ValueStructType = "S_Item-Data", Offset = 0x40, Size = 0x10 },
                new FieldInfoModel { Name = "Kinds", TypeName = "SetProperty", ElemType = "StructProperty", ElemStructType = "S_Item-Data", Offset = 0x50, Size = 0x10 },
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
        Assert.Contains("TMap<int32_t, struct S_Item_Data> Loot;", header);
        Assert.Contains("TSet<struct S_Item_Data> Kinds;", header);
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
            new() { Name = "Items", TypeName = "ArrayProperty", ArrayInnerType = "StructProperty", ArrayStructType = "S_Item-Data", Offset = 0x10, Size = 0x10 },
            new() { Name = "Bag", TypeName = "MapProperty", MapKeyType = "StructProperty", MapKeyStructType = "S_Key-X", MapValueType = "StructProperty", MapValueStructType = "S_Item-Data", Offset = 0x20, Size = 0x10 },
            new() { Name = "Kinds", TypeName = "SetProperty", SetElemType = "StructProperty", SetElemStructType = "S_Item-Data", Offset = 0x30, Size = 0x10 },
            new() { Name = "Kind", TypeName = "EnumProperty", EnumName = "E_Type-A", Offset = 0x40, Size = 1 },
        };
        var header = SdkExportService.GenerateClassHeader("BP_Door-Big_C", "Actor-X", 0x48, fields);

        Assert.Contains("struct BP_Door_Big_C : public Actor_X", header);
        // [SDK-LIVE-VALUE-TYPES] A live row's pointee class and enum name are VALUES (the runtime
        // class, the value's name), so they are not types at all -- only the struct names a live
        // row reads from the declared property are, and those are sanitised.
        Assert.Contains("class UObject* Target;", header);
        Assert.Contains("struct S_Item_Data Item;", header);
        Assert.Contains("TArray<struct S_Item_Data> Items;", header);
        Assert.Contains("TMap<struct S_Key_X, struct S_Item_Data> Bag;", header);
        Assert.Contains("TSet<struct S_Item_Data> Kinds;", header);
        Assert.Contains("uint8_t Kind;", header);
        Assert.DoesNotContain(CodeLines(header), l => l.Contains('-'));
    }

    [Fact]
    public void Standalone_ASuperOfTheSameName_IsNeverTheStructItself()
    {
        // A child Blueprint named like its parent in another folder, exported alone. The super is
        // the type the user already has under its plain name, so the exported struct is the one
        // renamed: from its path when there is one, else with a suffix.
        var live = SdkExportService.GenerateClassHeader("BP_Door_C", "BP_Door_C", 0x08,
            new List<LiveFieldValue> { new() { Name = "V", TypeName = "IntProperty", Offset = 0x04, Size = 4 } });
        Assert.Contains("struct BP_Door_C_0 : public BP_Door_C", live);

        var schema = SdkExportService.GenerateClassHeaderFromSchema(new ClassInfoModel
        {
            Name = ConstData, SuperName = ConstData, SuperAddress = "0x10",
            FullPath = Quinn + "." + ConstData, PropertiesSize = 0x0C,
            Fields = { Int("QuinnOnly", 8) },
        });
        Assert.Contains($"struct {ConstData}_ABP_Quinn_C : public {ConstData}", schema);
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
    public void Pool_TheSuperFollowsItsAddress_NotANearerNamesake()
    {
        // A third holder sits in Quinn's own package, nearer than Manny's: by name it would win.
        // The super is the one SuperAddress points at.
        var sdk = AnimBlueprintPool()
            .Add("0x50", ConstData, "ScriptStruct", Quinn + "_Extra." + ConstData, size: 4,
                 fields: new[] { Int("Extra", 0) })
            .Sdk();

        Assert.Contains($"struct {ConstData}_ABP_Quinn_C : public {ConstData}_ABP_Manny_C", sdk);
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
        // A /Game ScriptStruct named like an engine struct (a UserDefinedStruct never reaches the pool
        // today -- [SDK-UDS-MISSING]): the engine one keeps `Vector`, and each side's references
        // resolve to the holder nearest to them.
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
    public void Pool_WhenTheNearestOuterIsShared_BothHoldersGoOneLevelOut()
    {
        // Both nearest outers are `Shared`, so depth 1 would name both `X_C_Shared`; the whole group
        // moves to depth 2 together, whatever the GObjects order.
        SdkPoolDump Pool(bool reversed)
        {
            var d = new SdkPoolDump();
            var a = ("0x1", "//Game/A/Shared/X/X_C");
            var b = ("0x2", "//Game/B/Shared/X/X_C");
            foreach (var (addr, path) in reversed ? new[] { b, a } : new[] { a, b })
                d.Add(addr, "X_C", "BlueprintGeneratedClass", path, fields: new[] { Int("V", 0) });
            return d;
        }

        foreach (var sdk in new[] { Pool(false).Sdk(), Pool(true).Sdk() })
        {
            var n = sdk.Replace("\r\n", "\n");
            Assert.Contains("// //Game/A/Shared/X/X_C\nstruct X_C_A_Shared\n", n);
            Assert.Contains("// //Game/B/Shared/X/X_C\nstruct X_C_B_Shared\n", n);
        }
    }

    [Fact]
    public void Pool_ReferenceKind_PicksTheClassOrTheStruct()
    {
        // A class and a struct may share a short name in different modules; a pointer names a class,
        // a by-value member names a struct. Both are equally near the referrer, so the tie-break (the
        // lower path) would pick the STRUCT's /ModA -- only the kind filter sends the pointer to the
        // class. Both GObjects orders, so neither can win by being listed first.
        foreach (bool structFirst in new[] { false, true })
        {
            var d = new SdkPoolDump();
            if (structFirst) d.Add("0x2", "Foo", "ScriptStruct", "//Script/ModA/Foo", fields: new[] { Int("B", 0) });
            d.Add("0x1", "Foo", "Class", "//Script/ModB/Foo", fields: new[] { Int("A", 0) });
            if (!structFirst) d.Add("0x2", "Foo", "ScriptStruct", "//Script/ModA/Foo", fields: new[] { Int("B", 0) });
            var sdk = d.Add("0x3", "BP_Z_C", "BlueprintGeneratedClass", "//Game/Z/BP_Z/BP_Z_C", size: 0x10,
                            fields: new[] { PtrMember("P", "Foo", 0), StructMember("S", "Foo", 8) })
                       .Sdk();

            Assert.Contains("class Foo_ModB* P;", sdk);
            Assert.Contains("struct Foo_ModA S;", sdk);
        }
    }

    [Fact]
    public void Pool_NoHolderOfTheRightKind_TheReferenceIsOnlySanitised()
    {
        // Two classes named Item, and a by-value member of a struct named Item that is not in the pool:
        // a by-value member cannot name a class, so it must not bind to either of them.
        var sdk = new SdkPoolDump()
            .Add("0x1", "Item", "Class", "//Script/ModA/Item", fields: new[] { Int("A", 0) })
            .Add("0x2", "Item", "Class", "//Script/ModB/Item", fields: new[] { Int("B", 0) })
            .Add("0x3", "BP_Bag_C", "BlueprintGeneratedClass", "//Game/Bag/BP_Bag/BP_Bag_C", size: 0x10,
                 fields: new[] { StructMember("Inv", "Item", 0, 0x10) })
            .Sdk();

        Assert.Contains("struct Item Inv;", sdk);
    }

    [Fact]
    public void Pool_ATieOnPath_IsBrokenByPath_NotByGObjectsOrder()
    {
        // Both BP_Door_C share only `Game` with the referrer: the reference must not change when the
        // load order does. The lower path (ordinal) wins.
        foreach (bool reversed in new[] { false, true })
        {
            var d = new SdkPoolDump();
            var a = ("0x1", "//Game/A/BP_Door/BP_Door_C");
            var b = ("0x2", "//Game/B/BP_Door/BP_Door_C");
            foreach (var (addr, path) in reversed ? new[] { b, a } : new[] { a, b })
                d.Add(addr, "BP_Door_C", "BlueprintGeneratedClass", path, fields: new[] { Int("V", 0) });
            var sdk = d.Add("0x3", "BP_Z_C", "BlueprintGeneratedClass", "//Game/C/BP_Z/BP_Z_C", size: 8,
                            fields: new[] { PtrMember("Door", "BP_Door_C", 0) })
                       .Sdk();

            Assert.Contains("class BP_Door_C_A* Door;", sdk);
        }
    }

    [Fact]
    public void Pool_AFolderNamedAfterTheAsset_IsKeptWhenItIsTheOnlyDifference()
    {
        // Dropping every `BP_Door` token leaves both holders with [Game, Props]; the paths still differ,
        // so the names must come from the paths -- not from a counter in GObjects order.
        foreach (bool reversed in new[] { false, true })
        {
            var d = new SdkPoolDump();
            var a = ("0x1", "//Game/Props/BP_Door/BP_Door_C");
            var b = ("0x2", "//Game/Props/BP_Door/BP_Door/BP_Door_C");
            foreach (var (addr, path) in reversed ? new[] { b, a } : new[] { a, b })
                d.Add(addr, "BP_Door_C", "BlueprintGeneratedClass", path, fields: new[] { Int("V", 0) });
            var n = d.Sdk().Replace("\r\n", "\n");

            Assert.Contains("// //Game/Props/BP_Door/BP_Door_C\nstruct BP_Door_C_Props_BP_Door\n", n);
            Assert.Contains("// //Game/Props/BP_Door/BP_Door/BP_Door_C\nstruct BP_Door_C_BP_Door_BP_Door\n", n);
        }
    }

    [Fact]
    public void Pool_TwoGroupsWantingOneName_BothMoveOut_WhateverTheOrder()
    {
        // Group Foo's depth-1 name for /P/Bar_R and group Foo_Bar's for /R are both `Foo_Bar_R`.
        SdkPoolDump Pool(bool reversed)
        {
            var foo = new[] { ("0x1", "Foo", "//Game/P/Bar_R/Foo"), ("0x2", "Foo", "//Game/Q/Baz/Foo") };
            var bar = new[] { ("0x3", "Foo_Bar", "//Game/R/Foo_Bar"), ("0x4", "Foo_Bar", "//Game/S/Foo_Bar") };
            var d = new SdkPoolDump();
            foreach (var (addr, name, path) in reversed ? bar.Concat(foo) : foo.Concat(bar))
                d.Add(addr, name, "ScriptStruct", path, fields: new[] { Int("V", 0) });
            return d;
        }

        foreach (var sdk in new[] { Pool(false).Sdk(), Pool(true).Sdk() })
        {
            var n = sdk.Replace("\r\n", "\n");
            Assert.Contains("// //Game/P/Bar_R/Foo\nstruct Foo_P_Bar_R\n", n);
            Assert.Contains("// //Game/Q/Baz/Foo\nstruct Foo_Q_Baz\n", n);
            Assert.Contains("// //Game/R/Foo_Bar\nstruct Foo_Bar_Game_R\n", n);
            Assert.Contains("// //Game/S/Foo_Bar\nstruct Foo_Bar_Game_S\n", n);
        }
    }

    [Fact]
    public void Pool_AQualifiedName_NeverTakesAnotherTypesRealName()
    {
        // Group Foo's depth-1 name for /A/Bar is `Foo_Bar` -- the real name of the other group, which
        // has no plain keeper of its own. Foo moves out a level; Foo_Bar is untouched.
        foreach (bool reversed in new[] { false, true })
        {
            var foo = new[] { ("0x1", "Foo", "//Game/A/Bar/Foo"), ("0x2", "Foo", "//Game/B/Baz/Foo") };
            var bar = new[] { ("0x3", "Foo_Bar", "//Game/X/Foo_Bar"), ("0x4", "Foo_Bar", "//Game/Y/Foo_Bar") };
            var d = new SdkPoolDump();
            foreach (var (addr, name, path) in reversed ? bar.Concat(foo) : foo.Concat(bar))
                d.Add(addr, name, "ScriptStruct", path, fields: new[] { Int("V", 0) });
            var n = d.Sdk().Replace("\r\n", "\n");

            Assert.Contains("// //Game/A/Bar/Foo\nstruct Foo_A_Bar\n", n);
            Assert.Contains("// //Game/B/Baz/Foo\nstruct Foo_B_Baz\n", n);
            Assert.Contains("// //Game/X/Foo_Bar\nstruct Foo_Bar_X\n", n);
            Assert.Contains("// //Game/Y/Foo_Bar\nstruct Foo_Bar_Y\n", n);
        }
    }

    [Fact]
    public void Pool_AQualifiedName_SkipsAnotherTypesKeptName()
    {
        // Depth 1 of the Door_C pair is `Door_C_A`, which a real type keeps: the pair goes to depth 2.
        var n = new SdkPoolDump()
            .Add("0x1", "Door_C", "BlueprintGeneratedClass", "//Game/A/Door/Door_C", fields: new[] { Int("V", 0) })
            .Add("0x2", "Door_C", "BlueprintGeneratedClass", "//Game/B/Door/Door_C", fields: new[] { Int("V", 0) })
            .Add("0x3", "Door_C_A", "BlueprintGeneratedClass", "//Game/Z/Door_C_A/Door_C_A", fields: new[] { Int("V", 0) })
            .Sdk().Replace("\r\n", "\n");

        Assert.Contains("// //Game/A/Door/Door_C\nstruct Door_C_Game_A\n", n);
        Assert.Contains("// //Game/B/Door/Door_C\nstruct Door_C_Game_B\n", n);
        Assert.Contains("// //Game/Z/Door_C_A/Door_C_A\nstruct Door_C_A\n", n);
    }

    [Fact]
    public void Pool_ARefusedClass_GetsTheErrorLine_NotAnEmptyStruct()
    {
        // Ubel::WalkClassEx answers a refused class (the P3R USaveGame case) with an EMPTY ClassInfo.
        // Emitting it as `struct MySaveGame {}` would look like real data and hand every derived class
        // a base of size 0; it gets the same ERROR line as a failed walk, and references keep its name.
        var n = new SdkPoolDump()
            .AddRefused("0x200", "MySaveGame", "Class")
            .Add("0x300", "BP_Save_C", "BlueprintGeneratedClass", "//Game/Save/BP_Save/BP_Save_C", "0x200", "MySaveGame", 0x44,
                 new FieldInfoModel { Name = "Gold", TypeName = "IntProperty", Offset = 0x40, Size = 4 })
            .Sdk().Replace("\r\n", "\n");

        Assert.Contains("// ERROR: Failed to walk MySaveGame at 0x200", n);
        Assert.DoesNotContain("struct MySaveGame\n", n);
        Assert.Contains("struct BP_Save_C : public MySaveGame\n", n);
    }

    [Fact]
    public void Pool_ASuperWithoutAnAddress_NamedLikeTheClass_IsSpelledApart()
    {
        // No SuperAddress (an older DLL) and the super's name is the class's own, with no other holder:
        // the name cannot pick a type, but it must not pick the class itself.
        var n = new SdkPoolDump()
            .Add("0x1", "BP_Door_C", "BlueprintGeneratedClass", "//Game/Level2/BP_Door/BP_Door_C", "", "BP_Door_C",
                 fields: new[] { Int("V", 0) })
            .Sdk().Replace("\r\n", "\n");

        Assert.Contains("struct BP_Door_C : public BP_Door_C_0\n", n);
    }

    [Fact]
    public void Pool_ASuperMissingFromThePool_IsNeverTheStructItself()
    {
        // The super's address is not in the pool and its name equals the class's own: the class must
        // not resolve its super to itself.
        var n = new SdkPoolDump()
            .Add("0x1", "BP_Door_C", "BlueprintGeneratedClass", "//Game/Level2/BP_Door/BP_Door_C", "0x99", "BP_Door_C",
                 fields: new[] { Int("V", 0) })
            .Sdk().Replace("\r\n", "\n");

        Assert.Contains("struct BP_Door_C_Level2 : public BP_Door_C\n", n);
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

    // ------------------------------------------------------------------
    // [SDK-UDS-MISSING] Blueprint user-defined structs join the pool.
    // A UDS's GObjects row reads `UserDefinedStruct`; its members carry GUID-suffixed names.
    // ------------------------------------------------------------------

    [Fact]
    public void Pool_AUserDefinedStruct_IsDefined_AndItsReferencesFollowIt()
    {
        var n = new SdkPoolDump()
            .Add("0x1", "S_Item", "UserDefinedStruct", "//Game/Data/S_Item/S_Item",
                 fields: new[] { Int("Count_2_0123456789ABCDEF0123456789ABCDEF", 0) })
            .Add("0x2", "BP_Bag_C", "BlueprintGeneratedClass", "//Game/Bag/BP_Bag/BP_Bag_C", size: 0x18,
                 fields: new[]
                 {
                     StructMember("Item", "S_Item", 0, 4),
                     new FieldInfoModel { Name = "Items", TypeName = "ArrayProperty", InnerType = "StructProperty", InnerStructType = "S_Item", Offset = 0x08, Size = 0x10 },
                 })
            .Sdk().Replace("\r\n", "\n");

        Assert.Contains("// //Game/Data/S_Item/S_Item\nstruct S_Item\n", n);
        Assert.Contains("struct S_Item Item;", n);
        Assert.Contains("TArray<struct S_Item> Items;", n);
    }

    [Fact]
    public void Pool_AUserDefinedStruct_IsAStruct_ForTheKindFilter()
    {
        // A native class and a UDS share the name Item: a by-value member names the UDS.
        var sdk = new SdkPoolDump()
            .Add("0x1", "Item", "Class", "//Script/M/Item", fields: new[] { Int("A", 0) })
            .Add("0x2", "Item", "UserDefinedStruct", "//Game/Data/Item/Item", fields: new[] { Int("B", 0) })
            .Add("0x3", "BP_Bag_C", "BlueprintGeneratedClass", "//Game/Bag/BP_Bag/BP_Bag_C", size: 0x10,
                 fields: new[] { StructMember("Inv", "Item", 0, 4), PtrMember("Owner", "Item", 8) })
            .Sdk();

        Assert.Contains("struct Item_Data Inv;", sdk);
        Assert.Contains("class Item* Owner;", sdk);
    }

    [Fact]
    public void Pool_SharingOnlyTheMountPoint_IsNotLocality()
    {
        // A Blueprint in an unrelated /Game folder names `Vector` by value: sharing `Game` alone does not
        // make a /Game UDS nearer than the engine struct.
        var sdk = new SdkPoolDump()
            .Add("0x1", "Vector", "ScriptStruct", "//Script/CoreUObject/Vector", size: 0x18, fields: new[] { Int("X", 0) })
            .Add("0x2", "Vector", "UserDefinedStruct", "//Game/Maps/Stuff/Vector/Vector", size: 0x18, fields: new[] { Int("Mine", 0) })
            .Add("0x3", "BP_Y_C", "BlueprintGeneratedClass", "//Game/Other/BP_Y/BP_Y_C", size: 0x18,
                 fields: new[] { StructMember("Loc", "Vector", 0, 0x18) })
            .Sdk();

        var bp = sdk[sdk.IndexOf("struct BP_Y_C", StringComparison.Ordinal)..];
        Assert.Contains("struct Vector Loc;", bp[..bp.IndexOf("};", StringComparison.Ordinal)]);
    }

    [Fact]
    public void Pool_ANamesakeOfTheWrongSize_IsNotChosen()
    {
        // Same folder as a 4-byte UDS `Vector`, but the member is 0x18 bytes: it is the engine's.
        // A struct member's size is its struct's aligned PropertiesSize, so a holder that cannot be
        // that size is not the one it names.
        var sdk = new SdkPoolDump()
            .Add("0x1", "Vector", "ScriptStruct", "//Script/CoreUObject/Vector", size: 0x18, fields: new[] { Int("X", 0) })
            .Add("0x2", "Vector", "UserDefinedStruct", "//Game/Maps/Stuff/Vector/Vector", size: 4, fields: new[] { Int("Mine", 0) })
            .Add("0x3", "BP_X_C", "BlueprintGeneratedClass", "//Game/Maps/Stuff/BP_X/BP_X_C", size: 0x20,
                 fields: new[] { StructMember("Loc", "Vector", 0, 0x18), StructMember("Mine", "Vector", 0x18, 4) })
            .Sdk();

        var bp = sdk[sdk.IndexOf("struct BP_X_C", StringComparison.Ordinal)..];
        bp = bp[..bp.IndexOf("};", StringComparison.Ordinal)];
        Assert.Contains("struct Vector Loc;", bp);
        Assert.Contains("struct Vector_Stuff Mine;", bp);
    }

    [Fact]
    public void BuiltIns_CoverEveryTypeTheEmitterSpellsItself()
    {
        // Derived, not listed: every property type the emitter's source names, alone and as every
        // container's inner, through both metadata shapes, with no UE type names -- so what comes out
        // is only the header's own spellings. A new one must join SdkTypeNames.BuiltIns, or a pool
        // type of that name would redefine it.
        var root = FindRepoRoot();
        Assert.NotNull(root);
        var src = File.ReadAllText(Path.Combine(root!, "ui", "UE5DumpUI", "Services", "SdkExportService.cs"));
        var types = System.Text.RegularExpressions.Regex.Matches(src, "\"(\\w+Property)\"")
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
        Assert.True(types.Count > 20, $"only {types.Count} property types found in the emitter's source");

        var spelled = new HashSet<string>(StringComparer.Ordinal);
        void Collect(string cppType)
        {
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(cppType, "[A-Za-z_][A-Za-z0-9_]*"))
                spelled.Add(m.Value);
        }
        foreach (var t in types)
        {
            foreach (var inner in types.Append(""))
            {
                Collect(SdkExportService.MapCppDecl(new FieldInfoModel
                    { TypeName = t, InnerType = inner, KeyType = inner, ValueType = inner, ElemType = inner, Size = 4 }).Type);
                Collect(SdkExportService.MapCppDecl(new LiveFieldValue
                    { TypeName = t, ArrayInnerType = inner, MapKeyType = inner, MapValueType = inner, SetElemType = inner, Size = 4 }).Type);
            }
        }

        var language = new HashSet<string>(StringComparer.Ordinal)
        {
            "struct", "class", "bool", "float", "double",
            "int8_t", "int16_t", "int32_t", "int64_t", "uint8_t", "uint16_t", "uint32_t", "uint64_t",
        };
        var missing = spelled.Where(s => !language.Contains(s) && !SdkTypeNames.BuiltIns.Contains(s)).ToList();
        Assert.True(missing.Count == 0, "spelled by the emitter but not reserved: " + string.Join(", ", missing));
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "build.ps1"))
                && Directory.Exists(Path.Combine(dir.FullName, "docs")))
                return dir.FullName;
        }
        return null;
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
