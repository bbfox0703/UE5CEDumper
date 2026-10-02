using System.Text;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Validates the JSONL dump shape (one `kind`-tagged object per line)
/// + class-meta filter (Class + BPGC variants included; non-class meta
/// dropped) + per-class projection (props inlined with offset/size/type;
/// optional funcs section). Drives the offline analyze_dumps.py
/// pipeline, so the schema must be locked down here.
/// </summary>
public class DumpAllServiceTests
{
    // ------------------------------------------------------------------
    // Stub IDumpService that returns canned object lists + class walks.
    // Subclasses StubDumpService so we only have to override what the
    // dumper actually touches: GetObjectListAsync, WalkClassAsync,
    // WalkFunctionsAsync.
    // ------------------------------------------------------------------

    private class FakeDumpForDump : StubDumpService
    {
        public List<UObjectNode> Objects { get; } = new();
        public Dictionary<string, ClassInfoModel> ClassWalks { get; } = new();
        public Dictionary<string, List<FunctionInfoModel>> FunctionWalks { get; } = new();
        public int FunctionWalkCalls { get; private set; }
        public List<bool> IncludePathRequests { get; } = new();
        public EnumListResult Enums { get; set; } = new();
        public bool ThrowOnEnums { get; set; }

        public override Task<ObjectListResult> GetObjectListAsync(int offset, int limit, CancellationToken ct = default, bool includePath = false)
        {
            IncludePathRequests.Add(includePath);
            var slice = Objects.Skip(offset).Take(limit).ToList();
            return Task.FromResult(new ObjectListResult
            {
                Total   = Objects.Count,
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

        public override Task<List<FunctionInfoModel>> WalkFunctionsAsync(string addr, CancellationToken ct = default)
        {
            FunctionWalkCalls++;
            return Task.FromResult(FunctionWalks.TryGetValue(addr, out var fns)
                ? fns
                : new List<FunctionInfoModel>());
        }

        public override Task<List<EnumDefinition>> ListEnumsAsync(CancellationToken ct = default)
            => Task.FromResult(Enums.Enums);

        public override Task<EnumListResult> ListEnumsDetailedAsync(CancellationToken ct = default)
        {
            if (ThrowOnEnums) throw new InvalidOperationException("list_enums failed");
            return Task.FromResult(Enums);
        }
    }

    // StubDumpService's WalkClassAsync / WalkFunctionsAsync /
    // GetObjectListAsync are non-virtual; un-seal in the parent file
    // so our subclass can override (similar to how
    // InterestingFunctionsViewModelTests un-sealed ListAllFunctionsAsync).
    // The override modifier on FakeDumpForDump is the contract the
    // CsxExportServiceTests-side stub needs to honour.

    private static UObjectNode Obj(string addr, string name, string className, string path = "") =>
        new() { Address = addr, Name = name, ClassName = className, FullPath = path };

    private static EngineState DefaultEngineState() => new()
    {
        UEVersion = 427,
        ModuleName = "TestGame-Win64-Shipping.exe",
        ModuleBase = "0x7FF600000000",
        GObjectsAddr = "0x7FF600100000",
        GNamesAddr   = "0x7FF600200000",
        GWorldAddr   = "0x7FF600300000",
        ObjectCount = 1000,
        PeHash = "ABC123",
    };

    private static List<string> Dump(FakeDumpForDump dump, DumpOptions? opts = null, EngineState? engineState = null)
    {
        var ms = new MemoryStream();
        DumpAllService.GenerateAsync(dump, engineState ?? DefaultEngineState(), ms, opts).GetAwaiter().GetResult();
        ms.Position = 0;
        using var sr = new StreamReader(ms, Encoding.UTF8);
        var lines = new List<string>();
        string? line;
        while ((line = sr.ReadLine()) != null) lines.Add(line);
        return lines;
    }

    // ==================================================================
    // Lifecycle: meta first, summary last
    // ==================================================================

    [Fact]
    public void Generate_EmptyObjects_EmitsMetaAndSummaryOnly()
    {
        var dump = new FakeDumpForDump();
        var lines = Dump(dump);

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("{\"kind\":\"meta\"", lines[0]);
        Assert.StartsWith("{\"kind\":\"summary\"", lines[^1]);
        // Meta contains UE version + module name + GObjects
        Assert.Contains("\"ue_version\":427", lines[0]);
        Assert.Contains("TestGame-Win64-Shipping.exe", lines[0]);
        // FUObjectItem layout defaults to classic (no UE5.7+ packing).
        Assert.Contains("\"item_layout\":\"classic\"", lines[0]);
        Assert.Contains("\"item_obj_offset\":0", lines[0]);
        Assert.Contains("\"packed_unverified\":false", lines[0]);
        // Summary counters
        Assert.Contains("\"classes_emitted\":0", lines[^1]);
    }

    [Fact]
    public void Generate_PackedLayout_MetaCarriesLayoutAndUnverifiedFlag()
    {
        var dump = new FakeDumpForDump();
        var packed = new EngineState
        {
            UEVersion = 570,
            ModuleName = "Packed-Win64-Shipping.exe",
            ModuleBase = "0x7FF600000000",
            GObjectsAddr = "0x7FF600100000",
            GNamesAddr = "0x7FF600200000",
            GWorldAddr = "0x7FF600300000",
            ObjectCount = 10,
            PeHash = "PACKED",
            ItemLayoutMode = "packed57",
            ItemPacked = true,
            ItemObjOffset = 0,
        };

        var lines = Dump(dump, engineState: packed);

        // Offline analysis can now distinguish a packed-reconstructed dump.
        Assert.Contains("\"item_layout\":\"packed57\"", lines[0]);
        Assert.Contains("\"packed_unverified\":true", lines[0]);
    }

    [Fact]
    public void Generate_UndetectedStride_MetaCarriesTheVerdict()
    {
        // [W4-STRIDE-TENTATIVE] A dump taken on a guessed stride must say so, beside packed_unverified.
        var dump = new FakeDumpForDump();
        var guessed = new EngineState
        {
            UEVersion = 505,
            ModuleName = "Guess-Win64-Shipping.exe",
            ModuleBase = "0x7FF600000000",
            GObjectsAddr = "0x7FF600100000",
            GNamesAddr = "0x7FF600200000",
            GWorldAddr = "0x7FF600300000",
            ObjectCount = 10,
            PeHash = "GUESS",
            ItemDetect = "undetected",
        };

        var lines = Dump(dump, engineState: guessed);

        Assert.Contains("\"item_detect\":\"undetected\"", lines[0]);
        Assert.Contains("\"stride_untrusted\":true", lines[0]);
    }

    // ==================================================================
    // Filter: accept Class + BPGC variants; drop everything else
    // ==================================================================

    [Fact]
    public void Generate_FiltersToClassLikeMetas()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "UCharacter", "Class", "/Script/Engine.Character"));
        dump.Objects.Add(Obj("0x2", "BP_Player_C", "BlueprintGeneratedClass", "/Game/MyGame/BP_Player_C"));
        dump.Objects.Add(Obj("0x3", "MyAnimBP_C", "AnimBlueprintGeneratedClass", "/Game/Anim/MyAnimBP_C"));
        dump.Objects.Add(Obj("0x4", "MyWidget_C", "WidgetBlueprintGeneratedClass", "/Game/UI/MyWidget_C"));
        dump.Objects.Add(Obj("0x5", "FVector", "ScriptStruct"));            // struct line, not a class
        dump.Objects.Add(Obj("0x6", "Player_Default", "BP_Player_C"));      // dropped (live instance)
        dump.Objects.Add(Obj("0x7", "MyDynamic_C", "DynamicClass"));        // accepted

        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "UCharacter" };
        dump.ClassWalks["0x2"] = new ClassInfoModel { Name = "BP_Player_C" };
        dump.ClassWalks["0x3"] = new ClassInfoModel { Name = "MyAnimBP_C" };
        dump.ClassWalks["0x4"] = new ClassInfoModel { Name = "MyWidget_C" };
        dump.ClassWalks["0x5"] = new ClassInfoModel
        {
            Name = "FVector",
            Fields = [new FieldInfoModel { Name = "X", TypeName = "DoubleProperty", Offset = 0, Size = 8 }],
        };
        dump.ClassWalks["0x7"] = new ClassInfoModel { Name = "MyDynamic_C" };

        var lines = Dump(dump);
        var classLines = lines.Where(l => l.StartsWith("{\"kind\":\"class\"")).ToList();
        var structLines = lines.Where(l => l.StartsWith("{\"kind\":\"struct\"")).ToList();

        Assert.Equal(5, classLines.Count);  // Class + 3 BPGC variants + DynamicClass (instance dropped)
        Assert.Contains(classLines, l => l.Contains("\"name\":\"UCharacter\""));
        Assert.Contains(classLines, l => l.Contains("\"name\":\"BP_Player_C\""));
        Assert.Contains(classLines, l => l.Contains("\"name\":\"MyAnimBP_C\""));
        Assert.Contains(classLines, l => l.Contains("\"name\":\"MyWidget_C\""));
        Assert.Contains(classLines, l => l.Contains("\"name\":\"MyDynamic_C\""));
        Assert.DoesNotContain(classLines, l => l.Contains("\"name\":\"FVector\""));
        Assert.Single(structLines);
        Assert.Contains("\"name\":\"FVector\"", structLines[0]);
        Assert.Contains("\"name\":\"X\"", structLines[0]);
        Assert.DoesNotContain("instance_count", structLines[0]);
        Assert.DoesNotContain("\"funcs\"", structLines[0]);
    }

    [Fact]
    public void Generate_Struct_DoesNotWalkFunctions()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "FVector", "ScriptStruct"));
        dump.Objects.Add(Obj("0x2", "MyRow", "UserDefinedStruct", "/Game/MyRow.MyRow"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "FVector" };
        dump.ClassWalks["0x2"] = new ClassInfoModel { Name = "MyRow", FullPath = "/Game/MyRow.MyRow" };

        var lines = Dump(dump);
        var structLines = lines.Where(l => l.StartsWith("{\"kind\":\"struct\"")).ToList();

        Assert.Equal(2, structLines.Count);
        Assert.Equal(0, dump.FunctionWalkCalls);
        Assert.Contains(structLines, l => l.Contains("\"meta\":\"UserDefinedStruct\""));
    }

    [Fact]
    public void Generate_SkipsScriptStructClassDefaultObject()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "Default__ScriptStruct", "ScriptStruct"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "Default__ScriptStruct" };

        var lines = Dump(dump);
        Assert.DoesNotContain(lines, l => l.Contains("Default__ScriptStruct") && l.Contains("\"kind\":\"struct\""));
        Assert.DoesNotContain(lines, l => l.StartsWith("{\"kind\":\"struct\""));
    }

    [Fact]
    public void Generate_GameOnly_DropsEngineStructKeepsGameStruct()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "Vector", "ScriptStruct"));
        dump.Objects.Add(Obj("0x2", "MyRow", "UserDefinedStruct"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "Vector", FullPath = "//Script/CoreUObject/Vector" };
        dump.ClassWalks["0x2"] = new ClassInfoModel { Name = "MyRow", FullPath = "//Game/Rows/MyRow/MyRow" };

        var lines = Dump(dump, new DumpOptions(GameOnly: true, IncludeFunctions: false, IncludeInstanceCounts: false));
        var structLines = lines.Where(l => l.StartsWith("{\"kind\":\"struct\"")).ToList();

        Assert.Single(structLines);
        Assert.Contains("MyRow", structLines[0]);
        Assert.Contains("\"classes_skipped_engine\":0", lines[^1]);
    }

    // [DUMPALL-METACLASS-CDO] A metaclass's class-default object reads its METAclass (Default__Class's class is Class),
    // so a meta-only test admitted it and Dump All wrote it as a class -- the shape the SDK and USMAP exports shed with
    // IsExportedTypeRow. A Blueprint class's CDO (Default__BP_Player_C, class BP_Player_C) never passed the meta test.
    [Fact]
    public void Generate_SkipsTheMetaclassesClassDefaultObjects()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "Actor", "Class", "/Script/Engine.Actor"));
        dump.Objects.Add(Obj("0x2", "Default__Class", "Class", "/Script/CoreUObject.Default__Class"));
        dump.Objects.Add(Obj("0x3", "Default__BlueprintGeneratedClass", "BlueprintGeneratedClass",
            "/Script/Engine.Default__BlueprintGeneratedClass"));
        dump.Objects.Add(Obj("0x4", "Default__WidgetBlueprintGeneratedClass", "WidgetBlueprintGeneratedClass",
            "/Script/UMG.Default__WidgetBlueprintGeneratedClass"));
        foreach (var (addr, name) in new[] { ("0x1", "Actor"), ("0x2", "Default__Class"),
                     ("0x3", "Default__BlueprintGeneratedClass"), ("0x4", "Default__WidgetBlueprintGeneratedClass") })
            dump.ClassWalks[addr] = new ClassInfoModel { Name = name };

        var classLines = Dump(dump).Where(l => l.StartsWith("{\"kind\":\"class\"")).ToList();

        Assert.Single(classLines);
        Assert.Contains("\"name\":\"Actor\"", classLines[0]);
    }

    // ==================================================================
    // X4: GenerateAsync returns a DumpResult the completion line is built from
    // ==================================================================

    [Fact]
    public async Task Generate_ReturnsResult_WithCountsThatMatchTheSummaryLine()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "UCharacter", "Class"));
        dump.Objects.Add(Obj("0x2", "BP_Player_C", "BlueprintGeneratedClass"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "UCharacter" };
        dump.ClassWalks["0x2"] = new ClassInfoModel { Name = "BP_Player_C" };

        var ms = new MemoryStream();
        var result = await DumpAllService.GenerateAsync(
            dump, DefaultEngineState(), ms,
            new DumpOptions(IncludeInstanceCounts: false),
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ClassesEmitted);
        Assert.Equal(0, result.Errors);

        // The returned counts must equal what the trailing summary line reports —
        // the completion status is composed from the result, not the file length.
        ms.Position = 0;
        using var sr = new StreamReader(ms, Encoding.UTF8);
        var all = await sr.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"classes_emitted\":2", all);
        Assert.Contains("\"errors\":0", all);
    }

    // ==================================================================
    // GameOnly filter: skip /Script/Engine.*
    // ==================================================================

    [Fact]
    public void Generate_GameOnly_SkipsEnginePackagesButKeepsBPGC()
    {
        var dump = new FakeDumpForDump();
        // REALISTIC: get_object_list never sends a path, so obj.FullPath is "".
        // The engine-package skip must therefore key on the WALKED classInfo.FullPath.
        // (Passing a path to Obj here would mask the bug this test guards — the old
        // code skipped on the always-empty obj.FullPath and silently kept everything.)
        dump.Objects.Add(Obj("0x1", "UActor", "Class"));
        dump.Objects.Add(Obj("0x2", "MyPlayer", "BlueprintGeneratedClass"));
        // REAL Ubel::GetFullName wire format: "//Script/Engine/Actor" (double leading
        // slash, '/' separators) — NOT the "/Script/Engine.Actor" fiction the DLL never
        // emits. Using the fiction here would let a broken IsEnginePath pass green.
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "UActor", FullPath = "//Script/Engine/Actor" };
        dump.ClassWalks["0x2"] = new ClassInfoModel { Name = "MyPlayer", FullPath = "//Game/Foo/MyPlayer/MyPlayer_C" };

        var lines = Dump(dump, new DumpOptions(GameOnly: true, IncludeFunctions: false, IncludeInstanceCounts: false));
        var classLines = lines.Where(l => l.Contains("\"kind\":\"class\"")).ToList();

        Assert.Single(classLines);
        Assert.Contains("MyPlayer", classLines[0]);
        // Summary counts the skip
        var summary = lines[^1];
        Assert.Contains("\"classes_skipped_engine\":1", summary);
    }

    [Fact]
    public void Generate_GameOnly_PreWalkSkip_KeysOnObjectFullPath_WithoutWalking()
    {
        // With include_path=true the object list carries obj.FullPath, so GameOnly
        // skips engine classes BEFORE walking them. To prove the skip keys on
        // obj.FullPath (pre-walk) and not the walked classInfo.FullPath, give the
        // engine object an obj.FullPath of "/Script/Engine.Actor" but a WALKED path
        // of "/Game/…". If it were walked + post-filtered, the game path would let
        // it through and it would be emitted — so an empty class output proves the
        // pre-walk skip fired and the walk was never reached.
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "UActor", "Class", "//Script/Engine/Actor"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "UActor", FullPath = "//Game/Decoy/UActor/UActor_C" };

        var lines = Dump(dump, new DumpOptions(GameOnly: true, IncludeFunctions: false, IncludeInstanceCounts: false));
        var classLines = lines.Where(l => l.Contains("\"kind\":\"class\"")).ToList();

        Assert.Empty(classLines);
        Assert.Contains("\"classes_skipped_engine\":1", lines[^1]);
    }

    [Fact]
    public void Generate_Enums_WritesEntries_AndDropsEngineWhenGameOnly()
    {
        var dump = new FakeDumpForDump
        {
            Enums = new EnumListResult
            {
                Enums =
                [
                    new EnumDefinition
                    {
                        Name = "ELoot", Address = "0xA", FullPath = "//Game/ELoot",
                        Entries = [new EnumEntryValue { Name = "Chest", Value = 1 }],
                    },
                    new EnumDefinition
                    {
                        Name = "ENet", Address = "0xB", FullPath = "//Script/Engine/ENet",
                        Entries = [new EnumEntryValue { Name = "Ok", Value = 0 }],
                    },
                ],
            },
        };

        var lines = Dump(dump, new DumpOptions(GameOnly: true, IncludeFunctions: false, IncludeInstanceCounts: false));
        var enumLines = lines.Where(l => l.StartsWith("{\"kind\":\"enum\"")).ToList();

        Assert.Single(enumLines);
        Assert.Contains("\"n\":\"Chest\"", enumLines[0]);
        Assert.Contains("\"v\":1", enumLines[0]);
        Assert.Contains("\"enums_emitted\":1", lines[^1]);
    }

    [Fact]
    public void Generate_EnumNamesFailed_SetsSummaryFlag()
    {
        var dump = new FakeDumpForDump
        {
            Enums = new EnumListResult
            {
                EnumNamesFailed = true,
                Enums = [new EnumDefinition { Name = "ELoot", Address = "0xA", FullPath = "//Game/ELoot" }],
            },
        };

        var lines = Dump(dump, new DumpOptions(IncludeFunctions: false, IncludeInstanceCounts: false));

        Assert.Contains("\"enum_names_failed\":true", lines[^1]);
        Assert.Contains("\"entries\":[]", lines.First(l => l.StartsWith("{\"kind\":\"enum\"")));
    }

    [Fact]
    public void Generate_ListEnumsThrows_EmitsErrorAndKeepsTheClass()
    {
        var dump = new FakeDumpForDump { ThrowOnEnums = true };
        dump.Objects.Add(Obj("0x1", "Actor", "Class"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "Actor" };

        var lines = Dump(dump, new DumpOptions(IncludeFunctions: false, IncludeInstanceCounts: false));

        Assert.Contains(lines, l => l.StartsWith("{\"kind\":\"class\""));
        Assert.Contains(lines, l => l.Contains("\"kind\":\"error\"") && l.Contains("list_enums"));
        Assert.Contains("\"enums_emitted\":0", lines[^1]);
        Assert.Contains("\"errors\":1", lines[^1]);
    }

    [Fact]
    public void Generate_IncludeInstances_WritesLiveRowsNotReflection()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "Actor", "Class", "/Script/Engine.Actor"));
        dump.Objects.Add(Obj("0x2", "Default__GE_Fire", "GameplayEffect", "/Game/GE_Fire.Default__GE_Fire"));
        dump.Objects.Add(Obj("0x3", "Vector", "ScriptStruct", "/Script/CoreUObject.Vector"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "Actor" };
        dump.ClassWalks["0x3"] = new ClassInfoModel { Name = "Vector" };

        var lines = Dump(dump, new DumpOptions(
            IncludeInstances: true, IncludeInstanceCounts: false, IncludeFunctions: false));
        var instances = lines.Where(l => l.StartsWith("{\"kind\":\"instance\"")).ToList();

        Assert.Contains(dump.IncludePathRequests, requested => requested);
        Assert.Single(instances);
        Assert.Contains("Default__GE_Fire", instances[0]);
        Assert.Contains("\"class\":\"GameplayEffect\"", instances[0]);
        Assert.Contains("\"path\":\"/Game/GE_Fire.Default__GE_Fire\"", instances[0]);
        Assert.DoesNotContain(instances, l => l.Contains("ScriptStruct"));
        Assert.Contains("\"include_instances\":true", lines[0]);
    }

    [Fact]
    public void Generate_InstancesOff_DoesNotRequestPath()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "Actor", "Class"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "Actor" };

        Dump(dump, new DumpOptions(IncludeInstances: false, IncludeInstanceCounts: true, IncludeFunctions: false));

        Assert.DoesNotContain(dump.IncludePathRequests, requested => requested);
    }

    // ==================================================================
    // Class row contents — props inlined with offset/type
    // ==================================================================

    [Fact]
    public void Generate_ClassWithProps_EmbedsPropsInlineWithCorrectFields()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "BP_Player_C", "BlueprintGeneratedClass", "/Game/BP_Player_C"));
        dump.ClassWalks["0x1"] = new ClassInfoModel
        {
            Name = "BP_Player_C",
            FullPath = "/Game/BP_Player_C",
            SuperName = "Character",
            SuperAddress = "0xABCD",
            PropertiesSize = 2048,
            Fields = new List<FieldInfoModel>
            {
                new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x6BC, Size = 4 },
                new() { Name = "Inventory", TypeName = "ArrayProperty", Offset = 0x700, Size = 16, InnerType = "ObjectProperty" },
                new() { Name = "PlayerLocation", TypeName = "StructProperty", Offset = 0x800, Size = 12, StructType = "Vector" },
            }
        };

        var lines = Dump(dump);
        var classLine = lines.First(l => l.Contains("\"kind\":\"class\""));

        Assert.Contains("\"name\":\"BP_Player_C\"", classLine);
        Assert.Contains("\"is_bpgc\":true", classLine);
        Assert.Contains("\"props_size\":2048", classLine);
        Assert.Contains("\"super\":\"Character\"", classLine);

        Assert.Contains("\"name\":\"Health\"", classLine);
        Assert.Contains("\"type\":\"FloatProperty\"", classLine);
        Assert.Contains("\"offset\":1724", classLine); // 0x6BC = 1724
        Assert.Contains("\"inner_type\":\"ObjectProperty\"", classLine);
        Assert.Contains("\"struct_type\":\"Vector\"", classLine);
    }

    [Fact]
    public void Generate_EmitsPropFlagsArrayDimAndInnerStructType_WhenNonDefault()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "BP_Player_C", "BlueprintGeneratedClass", "/Game/BP_Player_C"));
        dump.ClassWalks["0x1"] = new ClassInfoModel
        {
            Name = "BP_Player_C",
            Fields = new List<FieldInfoModel>
            {
                // Non-zero CPF_* flags on a scalar → prop_flags emits, array_dim omitted.
                new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4,
                        PropertyFlags = 0x0040000000000001UL },
                // Static C-array (Foo[4]) → array_dim emits.
                new() { Name = "AmmoCounts", TypeName = "IntProperty", Offset = 0x20, Size = 4, ArrayDim = 4 },
                // TArray<FStruct> → inner_struct_type emits.
                new() { Name = "Effects", TypeName = "ArrayProperty", Offset = 0x30, Size = 16,
                        InnerType = "StructProperty", InnerStructType = "GameplayEffectSpec" },
                // All defaults (flags 0, dim 1) → neither key emitted.
                new() { Name = "Plain", TypeName = "IntProperty", Offset = 0x40, Size = 4 },
            }
        };

        var classLine = Dump(dump).First(l => l.Contains("\"kind\":\"class\""));

        // prop_flags: uppercase hex "0x…", no padding (byte-identical to the
        // DLL's Renge::AddrToStr wire form so the two never drift).
        Assert.Contains("\"prop_flags\":\"0x40000000000001\"", classLine);
        Assert.Contains("\"array_dim\":4", classLine);
        Assert.Contains("\"inner_struct_type\":\"GameplayEffectSpec\"", classLine);
        // Defaults are omitted — exactly one field carries each optional key.
        Assert.Equal(1, CountOccurrences(classLine, "\"prop_flags\""));
        Assert.Equal(1, CountOccurrences(classLine, "\"array_dim\""));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }

    [Fact]
    public void Generate_RegularClass_FlagsIsBpgcFalse()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "UCharacter", "Class", "/Script/Engine.Character"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "UCharacter" };

        var lines = Dump(dump);
        var classLine = lines.First(l => l.Contains("\"kind\":\"class\""));

        Assert.Contains("\"is_bpgc\":false", classLine);
        Assert.Contains("\"meta\":\"Class\"", classLine);
    }

    // ==================================================================
    // Functions section toggle
    // ==================================================================

    [Fact]
    public void Generate_IncludeFunctions_True_EmbedsFuncs()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "BP_Player_C", "BlueprintGeneratedClass", "/Game/BP_Player_C"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "BP_Player_C" };
        dump.FunctionWalks["0x1"] = new List<FunctionInfoModel>
        {
            new() { Name = "TakeDamage", Address = "0xF00", ReturnType = "void",
                    NumParms = 2, ParmsSize = 12, FunctionFlags = 0x4020600 },
        };

        var lines = Dump(dump, new DumpOptions(IncludeFunctions: true));
        var classLine = lines.First(l => l.Contains("\"kind\":\"class\""));

        Assert.Contains("\"funcs\":[", classLine);
        Assert.Contains("TakeDamage", classLine);
        Assert.Contains("\"flags\":\"0x4020600\"", classLine);
        Assert.Contains("\"num_parms\":2", classLine);
        Assert.Contains("\"params\":[]", classLine);
    }

    [Fact]
    public void Generate_IncludeFunctions_WritesParmList_NotStructFields()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "BP_Player_C", "BlueprintGeneratedClass", "/Game/BP_Player_C"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "BP_Player_C" };
        dump.FunctionWalks["0x1"] = new List<FunctionInfoModel>
        {
            new()
            {
                Name = "TakeDamage", Address = "0xF00", ReturnType = "void",
                NumParms = 2, ParmsSize = 140, FunctionFlags = 0x4020600,
                Params =
                [
                    new FunctionParamModel
                    {
                        Name = "Hit", TypeName = "StructProperty", Size = 136, Offset = 0,
                        StructName = "HitResult",
                        StructFields = [new DynamicStructField("bBlockingHit", "BoolProperty", 0, 1)],
                    },
                    new FunctionParamModel
                    {
                        Name = "OutHealth", TypeName = "FloatProperty", Size = 4, Offset = 136, IsOut = true,
                    },
                ],
            },
        };

        var lines = Dump(dump, new DumpOptions(IncludeFunctions: true));
        var classLine = lines.First(l => l.Contains("\"kind\":\"class\""));

        Assert.Contains("\"name\":\"Hit\"", classLine);
        Assert.Contains("\"type\":\"StructProperty\"", classLine);
        Assert.Contains("\"struct_type\":\"HitResult\"", classLine);
        Assert.Contains("\"offset\":0", classLine);
        Assert.Contains("\"size\":136", classLine);
        Assert.Contains("\"name\":\"OutHealth\"", classLine);
        Assert.Contains("\"out\":true", classLine);
        Assert.DoesNotContain("struct_fields", classLine);
        Assert.DoesNotContain("bBlockingHit", classLine);
        Assert.DoesNotContain("\"ret\":", classLine);
    }

    [Fact]
    public void Generate_IncludeFunctions_False_OmitsFuncs()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "BP_Player_C", "BlueprintGeneratedClass", "/Game/BP_Player_C"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "BP_Player_C" };
        dump.FunctionWalks["0x1"] = new List<FunctionInfoModel>
        {
            new() { Name = "TakeDamage", Address = "0xF00", NumParms = 2, ParmsSize = 12 },
        };

        var lines = Dump(dump, new DumpOptions(IncludeFunctions: false));
        var classLine = lines.First(l => l.Contains("\"kind\":\"class\""));

        Assert.DoesNotContain("\"funcs\"", classLine);
        Assert.DoesNotContain("TakeDamage", classLine);
    }

    // ==================================================================
    // Instance count toggle
    // ==================================================================

    [Fact]
    public void Generate_IncludeInstanceCounts_True_CountsPerClassName()
    {
        var dump = new FakeDumpForDump();
        // 3 instances of BP_Player_C, plus the class itself
        dump.Objects.Add(Obj("0x1", "BP_Player_C", "BlueprintGeneratedClass", "/Game/BP_Player_C"));
        dump.Objects.Add(Obj("0x2", "Player1", "BP_Player_C"));
        dump.Objects.Add(Obj("0x3", "Player2", "BP_Player_C"));
        dump.Objects.Add(Obj("0x4", "Player3", "BP_Player_C"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "BP_Player_C" };

        var lines = Dump(dump, new DumpOptions(IncludeInstanceCounts: true));
        var classLine = lines.First(l => l.Contains("\"kind\":\"class\""));

        Assert.Contains("\"instance_count\":3", classLine);
    }

    // ==================================================================
    // Robustness: errors don't abort the dump
    // ==================================================================

    [Fact]
    public void Generate_WalkClassThrows_EmitsErrorLineAndContinues()
    {
        var dump = new FailingWalkDump();
        dump.Objects.Add(Obj("0x1", "Good", "Class"));
        dump.Objects.Add(Obj("0x2", "Bad", "Class"));
        dump.Objects.Add(Obj("0x3", "AlsoGood", "Class"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "Good" };
        dump.ClassWalks["0x3"] = new ClassInfoModel { Name = "AlsoGood" };
        dump.ThrowOn.Add("0x2");

        var lines = Dump(dump);
        var classLines = lines.Where(l => l.Contains("\"kind\":\"class\"")).ToList();
        var errorLines = lines.Where(l => l.Contains("\"kind\":\"error\"")).ToList();

        Assert.Equal(2, classLines.Count);
        Assert.Single(errorLines);
        Assert.Contains("\"errors\":1", lines[^1]);
    }

    private sealed class FailingWalkDump : FakeDumpForDump
    {
        public HashSet<string> ThrowOn { get; } = new();

        public override Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default)
        {
            if (ThrowOn.Contains(addr))
                throw new InvalidOperationException($"simulated walk failure for {addr}");
            return base.WalkClassAsync(addr, ct);
        }
    }

    // ==================================================================
    // IsClassLikeMetaName + IsEnginePath helpers — quick contract lock
    // ==================================================================

    [Theory]
    [InlineData("Class", true)]
    [InlineData("BlueprintGeneratedClass", true)]
    [InlineData("AnimBlueprintGeneratedClass", true)]
    [InlineData("WidgetBlueprintGeneratedClass", true)]
    [InlineData("DynamicClass", true)]
    [InlineData("ScriptStruct", false)]
    [InlineData("Function", false)]
    [InlineData("Property", false)]
    [InlineData("", false)]
    public void IsClassLikeMetaName_AcceptsKnownClassFlavours(string meta, bool expected)
    {
        Assert.Equal(expected, DumpAllService.IsClassLikeMetaName(meta));
    }

    [Theory]
    // REAL Ubel::GetFullName wire format: DOUBLE leading slash, '/' separators.
    [InlineData("//Script/Engine/Actor", true)]
    [InlineData("//Script/CoreUObject/Object", true)]
    [InlineData("//Script/Niagara/NiagaraComponent", true)]
    [InlineData("//Script/EnhancedInput/InputAction", true)]   // newly-synced prefix
    [InlineData("//Script/Engine", true)]                      // exact package, no trailing segment
    // Tolerant of the alternate single-slash / dot-terminated form too.
    [InlineData("/Script/Engine.Actor", true)]
    // A game class must NOT be treated as engine.
    [InlineData("//Game/MyGame/BP_Player/BP_Player_C", false)]
    // A prefix that is only a substring (not a package boundary) must NOT match.
    [InlineData("//Script/EngineOverride/Foo", false)]
    [InlineData("", false)]
    [InlineData("///", false)]
    public void IsEnginePath_DetectsKnownEnginePrefixes(string path, bool expected)
    {
        Assert.Equal(expected, DumpAllService.IsEnginePath(path));
    }
}
