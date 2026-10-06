using System.Text;
using System.Text.Json;
using UE5DumpUI.Core;
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
    // Stub IDumpService that returns canned object lists, class walks and
    // enums. Subclasses StubDumpService so only the calls the dumper makes
    // need overriding.
    // ------------------------------------------------------------------

    private class FakeDumpForDump : StubDumpService, IDumpService
    {
        // [EXTPR-539-540-2026-10-02] D2: Dump All lists the enums once per run. An empty list unless a test
        // fills it; the flags and the throw are the detailed list's own answers.
        public List<EnumDefinition> Enums { get; } = new();
        public bool EnumNamesFailed { get; set; }
        public bool EnumsTruncated { get; set; }
        public Exception? EnumsThrow { get; set; }

        public override Task<List<EnumDefinition>> ListEnumsAsync(CancellationToken ct = default)
            => EnumsThrow != null ? Task.FromException<List<EnumDefinition>>(EnumsThrow) : Task.FromResult(Enums);

        Task<EnumListResult> IDumpService.ListEnumsDetailedAsync(CancellationToken ct)
            => EnumsThrow != null
                ? Task.FromException<EnumListResult>(EnumsThrow)
                : Task.FromResult(new EnumListResult { Enums = Enums, EnumNamesFailed = EnumNamesFailed, Truncated = EnumsTruncated });

        public List<UObjectNode> Objects { get; } = new();
        public Dictionary<string, ClassInfoModel> ClassWalks { get; } = new();
        public Dictionary<string, List<FunctionInfoModel>> FunctionWalks { get; } = new();
        public List<string> FunctionWalkAddrs { get; } = new();

        public override Task<ObjectListResult> GetObjectListAsync(int offset, int limit, CancellationToken ct = default, bool includePath = false)
        {
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
            FunctionWalkAddrs.Add(addr);
            return Task.FromResult(FunctionWalks.TryGetValue(addr, out var fns)
                ? fns
                : new List<FunctionInfoModel>());
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
    // Filter: Class + BPGC variants become class lines, structs struct lines; drop everything else
    // ==================================================================

    [Fact]
    public void Generate_FiltersToClassLikeMetas()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "UCharacter", "Class", "/Script/Engine.Character"));
        dump.Objects.Add(Obj("0x2", "BP_Player_C", "BlueprintGeneratedClass", "/Game/MyGame/BP_Player_C"));
        dump.Objects.Add(Obj("0x3", "MyAnimBP_C", "AnimBlueprintGeneratedClass", "/Game/Anim/MyAnimBP_C"));
        dump.Objects.Add(Obj("0x4", "MyWidget_C", "WidgetBlueprintGeneratedClass", "/Game/UI/MyWidget_C"));
        dump.Objects.Add(Obj("0x5", "FVector", "ScriptStruct"));            // a struct line, not a class line (D1)
        dump.Objects.Add(Obj("0x6", "Player_Default", "BP_Player_C"));      // dropped (live instance)
        dump.Objects.Add(Obj("0x7", "MyDynamic_C", "DynamicClass"));        // accepted

        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "UCharacter" };
        dump.ClassWalks["0x2"] = new ClassInfoModel { Name = "BP_Player_C" };
        dump.ClassWalks["0x3"] = new ClassInfoModel { Name = "MyAnimBP_C" };
        dump.ClassWalks["0x4"] = new ClassInfoModel { Name = "MyWidget_C" };
        dump.ClassWalks["0x7"] = new ClassInfoModel { Name = "MyDynamic_C" };
        dump.ClassWalks["0x5"] = new ClassInfoModel { Name = "FVector" };

        var lines = Dump(dump);
        var classLines = lines.Where(l => l.StartsWith("{\"kind\":\"class\"")).ToList();

        Assert.Equal(5, classLines.Count);  // 3 BPGC variants + 1 Class + 1 DynamicClass (FVector is a struct line, the instance dropped)
        Assert.Contains(classLines, l => l.Contains("\"name\":\"UCharacter\""));
        Assert.Contains(classLines, l => l.Contains("\"name\":\"BP_Player_C\""));
        Assert.Contains(classLines, l => l.Contains("\"name\":\"MyAnimBP_C\""));
        Assert.Contains(classLines, l => l.Contains("\"name\":\"MyWidget_C\""));
        Assert.Contains(classLines, l => l.Contains("\"name\":\"MyDynamic_C\""));
        Assert.DoesNotContain(classLines, l => l.Contains("\"name\":\"FVector\""));
        Assert.Contains(lines, l => l.StartsWith("{\"kind\":\"struct\"") && l.Contains("\"name\":\"FVector\""));
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

    // ==================================================================
    // [EXTPR-539-540-2026-10-02] D7: the class walk reports progress by time
    // ==================================================================

    /// <summary>A clock that moves only when the test moves it.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    /// <summary>Records reports on the calling thread; <c>Progress&lt;T&gt;</c> would post them.</summary>
    private sealed class RecordingProgress : IProgress<DumpProgress>
    {
        public List<DumpProgress> Reports { get; } = new();
        public void Report(DumpProgress value) => Reports.Add(value);
    }

    /// <summary>Each class's function walk takes <see cref="PerClass"/> of clock time.</summary>
    private sealed class TimedFakeDump : FakeDumpForDump
    {
        public required ManualClock Clock { get; init; }
        public TimeSpan PerClass { get; init; }

        public override Task<List<FunctionInfoModel>> WalkFunctionsAsync(string addr, CancellationToken ct = default)
        {
            Clock.Advance(PerClass);
            return base.WalkFunctionsAsync(addr, ct);
        }
    }

    private static List<DumpProgress> WalkReports(FakeDumpForDump dump, TimeProvider clock, int classCount)
    {
        for (int i = 1; i <= classCount; i++)
        {
            string addr = $"0x{i:X}";
            dump.Objects.Add(Obj(addr, $"C{i}", "Class"));
            dump.ClassWalks[addr] = new ClassInfoModel { Name = $"C{i}" };
        }
        var sink = new RecordingProgress();
        DumpAllService.GenerateAsync(
            dump, DefaultEngineState(), new MemoryStream(),
            new DumpOptions(IncludeInstanceCounts: false), sink,
            TestContext.Current.CancellationToken, clock).GetAwaiter().GetResult();
        return sink.Reports.Where(r => r.Phase == "Walking classes and structs").ToList();
    }

    [Fact]
    public void Generate_WalkProgress_IsNotDrivenByTheClassCount()
    {
        // 120 classes inside one interval: the old every-50 rule reported at 50 and 100.
        var clock = new ManualClock();
        var reports = WalkReports(new FakeDumpForDump(), clock, 120);

        var only = Assert.Single(reports);
        Assert.Equal(1, only.Done);   // the first class is reported at once
    }

    [Fact]
    public void Generate_WalkProgress_ReportsAgainEachTimeTheIntervalPasses()
    {
        var clock = new ManualClock();
        var dump = new TimedFakeDump { Clock = clock, PerClass = DumpAllService.ProgressReportInterval };
        var reports = WalkReports(dump, clock, 7);

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, reports.Select(r => r.Done).ToArray());
    }

    [Fact]
    public void Generate_WalkProgress_FasterThanTheInterval_ReportsEverySecondClass()
    {
        // Half an interval per class: every second class is due.
        var clock = new ManualClock();
        var dump = new TimedFakeDump { Clock = clock, PerClass = DumpAllService.ProgressReportInterval / 2 };
        var reports = WalkReports(dump, clock, 7);

        Assert.Equal(new[] { 1, 3, 5, 7 }, reports.Select(r => r.Done).ToArray());
    }

    // The walk flushes in chunks of WalkClassBatchChunkSize. The tests above stay inside one chunk, so
    // these two use enough classes for three: one throttle must pace the whole walk, and Done must keep
    // counting across chunks instead of starting again at 1.
    private const int ThreeChunks = DumpAllService.WalkClassBatchChunkSize * 2 + 50;

    [Fact]
    public void Generate_WalkProgress_OneIntervalAcrossChunks_ReportsOnce()
    {
        var reports = WalkReports(new FakeDumpForDump(), new ManualClock(), ThreeChunks);

        var only = Assert.Single(reports);
        Assert.Equal(1, only.Done);
    }

    [Fact]
    public void Generate_WalkProgress_DoneCountsOnAcrossChunks()
    {
        var clock = new ManualClock();
        var dump = new TimedFakeDump { Clock = clock, PerClass = DumpAllService.ProgressReportInterval };
        var reports = WalkReports(dump, clock, ThreeChunks);

        Assert.Equal(Enumerable.Range(1, ThreeChunks).ToArray(), reports.Select(r => r.Done).ToArray());
    }

    [Fact]
    public void ProgressThrottle_FirstCallIsDue_ThenWaitsForTheInterval()
    {
        var clock = new ManualClock();
        var throttle = new DumpAllService.ProgressThrottle(clock, TimeSpan.FromMilliseconds(500));

        Assert.True(throttle.Due());
        Assert.False(throttle.Due());
        clock.Advance(TimeSpan.FromMilliseconds(499));
        Assert.False(throttle.Due());
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(throttle.Due());
        Assert.False(throttle.Due());
    }

    // ==================================================================
    // [EXTPR-539-540-2026-10-02] D1: Dump All writes struct lines too. A ScriptStruct or
    // UserDefinedStruct row is walked like a class (one walk_class_batch slot) and written as
    // {"kind":"struct"}: a class line's identity, super and props, without what a struct does
    // not have (functions, instances, a Blueprint-class flag). walk_functions is not called
    // for it. classes_emitted keeps its meaning; structs get counters of their own.
    // ==================================================================

    private static FakeDumpForDump StructFixture()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "UCharacter", "Class", "/Game/X/UCharacter"));
        dump.Objects.Add(Obj("0x2", "FHitInfo", "ScriptStruct", "/Script/MyGame.HitInfo"));
        dump.Objects.Add(Obj("0x3", "S_Loot", "UserDefinedStruct", "/Game/Data/S_Loot"));
        dump.Objects.Add(Obj("0x4", "Default__FHitInfo", "ScriptStruct"));   // a CDO-shaped row: never a type
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "UCharacter", FullPath = "/Game/X/UCharacter" };
        dump.ClassWalks["0x2"] = new ClassInfoModel
        {
            Name = "FHitInfo", FullPath = "/Script/MyGame.HitInfo", SuperName = "FBaseInfo", SuperAddress = "0x9",
            PropertiesSize = 24,
            Fields = new List<FieldInfoModel>
            {
                new() { Name = "Damage", TypeName = "FloatProperty", Offset = 0x10, Size = 4 },
                new() { Name = "Target", TypeName = "ObjectProperty", Offset = 0x18, Size = 8, ObjClassName = "Actor" },
            },
        };
        dump.ClassWalks["0x3"] = new ClassInfoModel { Name = "S_Loot", FullPath = "/Game/Data/S_Loot", PropertiesSize = 8 };
        return dump;
    }

    [Fact]
    public void Generate_StructRows_AreWrittenAsStructLines()
    {
        var lines = Dump(StructFixture());

        var structs = lines.Where(l => l.StartsWith("{\"kind\":\"struct\"")).ToList();
        Assert.Equal(2, structs.Count);
        using var hit = JsonDocument.Parse(structs.Single(l => l.Contains("\"name\":\"FHitInfo\"")));
        var r = hit.RootElement;
        Assert.Equal("0x2", r.GetProperty("addr").GetString());
        Assert.Equal("/Script/MyGame.HitInfo", r.GetProperty("path").GetString());
        Assert.Equal("ScriptStruct", r.GetProperty("meta").GetString());
        Assert.Equal("FBaseInfo", r.GetProperty("super").GetString());
        Assert.Equal("0x9", r.GetProperty("super_addr").GetString());
        Assert.Equal(24, r.GetProperty("props_size").GetInt32());
        var props = r.GetProperty("props");
        Assert.Equal(2, props.GetArrayLength());
        Assert.Equal("Target", props[1].GetProperty("name").GetString());
        Assert.Equal("Actor", props[1].GetProperty("obj_class").GetString());
        // What a struct does not have.
        Assert.False(r.TryGetProperty("funcs", out _));
        Assert.False(r.TryGetProperty("instance_count", out _));
        Assert.False(r.TryGetProperty("is_bpgc", out _));

        Assert.Contains(structs, l => l.Contains("\"name\":\"S_Loot\"") && l.Contains("\"meta\":\"UserDefinedStruct\""));
        Assert.DoesNotContain(lines, l => l.Contains("Default__FHitInfo"));
        // The class is still a class line.
        Assert.Single(lines, l => l.StartsWith("{\"kind\":\"class\""));
    }

    [Fact]
    public void Generate_StructRows_AreNotAskedForFunctions()
    {
        var dump = StructFixture();

        Dump(dump);

        Assert.Equal(new[] { "0x1" }, dump.FunctionWalkAddrs.ToArray());
    }

    [Fact]
    public void Generate_Summary_CountsStructsApartFromClasses()
    {
        var lines = Dump(StructFixture());

        using var summary = JsonDocument.Parse(lines[^1]);
        var s = summary.RootElement;
        Assert.Equal(1, s.GetProperty("classes_emitted").GetInt32());
        Assert.Equal(2, s.GetProperty("structs_emitted").GetInt32());
        Assert.Equal(0, s.GetProperty("structs_skipped_engine").GetInt32());
    }

    [Fact]
    public void Generate_GameOnly_SkipsEngineStructs_AndCountsThem()
    {
        var dump = StructFixture();
        dump.Objects.Add(Obj("0x5", "FVector", "ScriptStruct", "//Script/CoreUObject/Vector"));
        dump.ClassWalks["0x5"] = new ClassInfoModel { Name = "FVector", FullPath = "//Script/CoreUObject/Vector" };

        var lines = Dump(dump, new DumpOptions(GameOnly: true));

        Assert.DoesNotContain(lines, l => l.Contains("\"name\":\"FVector\""));
        using var summary = JsonDocument.Parse(lines[^1]);
        Assert.Equal(2, summary.RootElement.GetProperty("structs_emitted").GetInt32());   // the game's two stay
        Assert.Equal(1, summary.RootElement.GetProperty("structs_skipped_engine").GetInt32());
    }

    [Fact]
    public async Task Generate_ResultAndProgress_CountStructsToo()
    {
        var dump = StructFixture();
        var sink = new RecordingProgress();
        var result = await DumpAllService.GenerateAsync(
            dump, DefaultEngineState(), new MemoryStream(), new DumpOptions(IncludeInstanceCounts: false), sink,
            TestContext.Current.CancellationToken, new ManualClock());

        Assert.Equal(1, result.ClassesEmitted);
        Assert.Equal(2, result.StructsEmitted);
        var done = sink.Reports[^1];
        Assert.Equal(3, done.Done);
        Assert.Contains("2 structs", done.Phase);
    }

    [Fact]
    public void Generate_ARefusedStructWalk_IsAnErrorLine_NotANamelessStruct()
    {
        // Ubel::WalkClassEx answers an address it refuses with an EMPTY ClassInfo, not an error. Written, it
        // would be a struct named "" that a struct diff matches with any other refused one; the SDK export
        // turns the same result into an error line [SDK-TYPE-NAMES].
        var dump = StructFixture();
        dump.Objects.Add(Obj("0x6", "FBroken", "ScriptStruct", "/Script/MyGame.Broken"));   // no walk result: empty

        var lines = Dump(dump);

        Assert.DoesNotContain(lines, l => l.StartsWith("{\"kind\":\"struct\"") && l.Contains("\"name\":\"\""));
        using var error = JsonDocument.Parse(lines.Single(l => l.StartsWith("{\"kind\":\"error\"")));
        Assert.Equal("0x6", error.RootElement.GetProperty("addr").GetString());
        Assert.Equal("FBroken", error.RootElement.GetProperty("name").GetString());
        using var summary = JsonDocument.Parse(lines[^1]);
        Assert.Equal(2, summary.RootElement.GetProperty("structs_emitted").GetInt32());
        Assert.Equal(1, summary.RootElement.GetProperty("errors").GetInt32());
    }

    [Fact]
    public async Task Generate_GameOnly_ResultCarriesTheStructSkipCount()
    {
        // DumpResult carries the summary line's counters, the struct skip count included.
        var dump = StructFixture();
        dump.Objects.Add(Obj("0x5", "FVector", "ScriptStruct", "//Script/CoreUObject/Vector"));
        dump.ClassWalks["0x5"] = new ClassInfoModel { Name = "FVector", FullPath = "//Script/CoreUObject/Vector" };

        var result = await DumpAllService.GenerateAsync(dump, DefaultEngineState(), new MemoryStream(),
            new DumpOptions(GameOnly: true), ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.StructsSkippedEngine);
    }

    // ==================================================================
    // [EXTPR-539-540-2026-10-02] D2: Dump All writes enum lines. One list_enums call after the
    // type walk; each enum becomes {"kind":"enum"} with its entries as {name, value}. The
    // summary says how many were written and skipped, whether the list was obtained at all,
    // and what the list alone cannot say (member names not locatable, a cut-short list), so a
    // reader comparing two dumps can tell an empty enum from one it could not read.
    // ==================================================================

    private static FakeDumpForDump EnumFixture()
    {
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "UCharacter", "Class", "/Game/X/UCharacter"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "UCharacter", FullPath = "/Game/X/UCharacter" };
        dump.Enums.Add(new EnumDefinition
        {
            Address = "0xE1", Name = "EKind", FullPath = "/Script/MyGame.EKind",
            Entries = new() { new() { Name = "EKind::A", Value = 0 }, new() { Name = "EKind::B", Value = 1 } },
        });
        dump.Enums.Add(new EnumDefinition
        {
            Address = "0xE2", Name = "E_Team", FullPath = "/Game/Data/E_Team",
            Entries = new() { new() { Name = "NewEnumerator0", Value = 0 }, new() { Name = "NewEnumerator1", Value = 5 } },
        });
        return dump;
    }

    [Fact]
    public void Generate_Enums_AreWrittenAsEnumLines_AfterTheTypes()
    {
        var lines = Dump(EnumFixture());

        var enums = lines.Where(l => l.StartsWith("{\"kind\":\"enum\"")).ToList();
        Assert.Equal(2, enums.Count);
        int lastType = lines.FindLastIndex(l => l.StartsWith("{\"kind\":\"class\"") || l.StartsWith("{\"kind\":\"struct\""));
        Assert.True(lines.IndexOf(enums[0]) > lastType);
        Assert.StartsWith("{\"kind\":\"summary\"", lines[^1]);

        using var team = JsonDocument.Parse(enums.Single(l => l.Contains("\"name\":\"E_Team\"")));
        var r = team.RootElement;
        Assert.Equal("0xE2", r.GetProperty("addr").GetString());
        Assert.Equal("/Game/Data/E_Team", r.GetProperty("path").GetString());
        var entries = r.GetProperty("entries");
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal("NewEnumerator1", entries[1].GetProperty("name").GetString());
        Assert.Equal(5, entries[1].GetProperty("value").GetInt64());
    }

    [Fact]
    public void Generate_Summary_CountsEnums_AndSaysTheListWasObtained()
    {
        var lines = Dump(EnumFixture());

        using var summary = JsonDocument.Parse(lines[^1]);
        var s = summary.RootElement;
        Assert.Equal(2, s.GetProperty("enums_emitted").GetInt32());
        Assert.Equal(0, s.GetProperty("enums_skipped_engine").GetInt32());
        Assert.True(s.GetProperty("enums_listed").GetBoolean());
        Assert.False(s.GetProperty("enum_names_failed").GetBoolean());
        Assert.False(s.GetProperty("enums_truncated").GetBoolean());
    }

    [Fact]
    public void Generate_GameOnly_SkipsEngineEnums_AndCountsThem()
    {
        var dump = EnumFixture();
        dump.Enums.Add(new EnumDefinition { Address = "0xE3", Name = "EAxis", FullPath = "//Script/CoreUObject/EAxis" });

        var lines = Dump(dump, new DumpOptions(GameOnly: true));

        Assert.DoesNotContain(lines, l => l.Contains("\"name\":\"EAxis\""));
        using var summary = JsonDocument.Parse(lines[^1]);
        Assert.Equal(2, summary.RootElement.GetProperty("enums_emitted").GetInt32());
        Assert.Equal(1, summary.RootElement.GetProperty("enums_skipped_engine").GetInt32());
    }

    [Fact]
    public void Generate_EnumNamesNotLocatable_IsRecorded_AndTheEnumsAreStillWritten()
    {
        var dump = EnumFixture();
        dump.EnumNamesFailed = true;
        dump.EnumsTruncated = true;

        var lines = Dump(dump);

        Assert.Equal(2, lines.Count(l => l.StartsWith("{\"kind\":\"enum\"")));
        using var summary = JsonDocument.Parse(lines[^1]);
        Assert.True(summary.RootElement.GetProperty("enum_names_failed").GetBoolean());
        Assert.True(summary.RootElement.GetProperty("enums_truncated").GetBoolean());
    }

    [Fact]
    public void Generate_AFailedEnumList_IsAnErrorLine_AndTheDumpCompletes()
    {
        var dump = EnumFixture();
        dump.EnumsThrow = new InvalidOperationException("pipe dropped");

        var lines = Dump(dump);

        Assert.Single(lines, l => l.StartsWith("{\"kind\":\"class\""));
        Assert.DoesNotContain(lines, l => l.StartsWith("{\"kind\":\"enum\""));
        using var error = JsonDocument.Parse(lines.Single(l => l.StartsWith("{\"kind\":\"error\"")));
        Assert.Equal("list_enums", error.RootElement.GetProperty("name").GetString());
        Assert.Contains("pipe dropped", error.RootElement.GetProperty("msg").GetString());
        using var summary = JsonDocument.Parse(lines[^1]);
        Assert.False(summary.RootElement.GetProperty("enums_listed").GetBoolean());
        Assert.Equal(0, summary.RootElement.GetProperty("enums_emitted").GetInt32());
        Assert.Equal(1, summary.RootElement.GetProperty("errors").GetInt32());
    }

    [Fact]
    public async Task Generate_ResultAndProgress_CountEnumsToo()
    {
        var sink = new RecordingProgress();
        var result = await DumpAllService.GenerateAsync(
            EnumFixture(), DefaultEngineState(), new MemoryStream(), new DumpOptions(IncludeInstanceCounts: false),
            sink, TestContext.Current.CancellationToken, new ManualClock());

        Assert.Equal(2, result.EnumsEmitted);
        var done = sink.Reports[^1];
        Assert.Equal(3, done.Done);   // 1 class + 0 structs + 2 enums
        Assert.Contains("2 enums", done.Phase);
    }

    // Review of 0c43deae: the enum list's warnings must reach the caller, as USMAP's do [P1-ENUMNAMES].

    [Fact]
    public async Task Generate_Result_CarriesTheEnumListsOwnWarnings()
    {
        var dump = EnumFixture();
        dump.EnumNamesFailed = true;
        dump.EnumsTruncated = true;

        var result = await DumpAllService.GenerateAsync(dump, DefaultEngineState(), new MemoryStream(),
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.EnumsListed);
        Assert.True(result.EnumNamesFailed);
        Assert.True(result.EnumsTruncated);
    }

    [Fact]
    public async Task Generate_AFailedEnumList_SaysSoInTheResult()
    {
        var dump = EnumFixture();
        dump.EnumsThrow = new InvalidOperationException("pipe dropped");

        var result = await DumpAllService.GenerateAsync(dump, DefaultEngineState(), new MemoryStream(),
            ct: TestContext.Current.CancellationToken);

        Assert.False(result.EnumsListed);
    }

    [Fact]
    public async Task Generate_ListingEnumsReport_DoesNotCarryTheTypeCount()
    {
        // The UI shows a report without a total as "Phase (Done)"; the types written so far next to
        // "Listing enums" read as an enum count.
        var sink = new RecordingProgress();
        await DumpAllService.GenerateAsync(EnumFixture(), DefaultEngineState(), new MemoryStream(),
            new DumpOptions(IncludeInstanceCounts: false), sink, TestContext.Current.CancellationToken, new ManualClock());

        var listing = Assert.Single(sink.Reports, r => r.Phase == "Listing enums");
        Assert.Equal(0, listing.Done);
    }

    // ==================================================================
    // [EXTPR-539-540-2026-10-02] D3: every function carries its parameters. walk_functions lists the
    // UFunction's whole property chain, and a Blueprint function's locals follow its parameters there, so
    // only the entries the DLL flags as parameters (CPF_Parm, the return included) are written. A DLL that
    // predates the flag falls back to UE's own definition, the leading num_parms entries, and the summary
    // counts the functions that needed it.
    // ==================================================================

    /// <summary>TryOpen: two parameters and the return, then two Blueprint locals. Tick: an empty chain.
    /// Recalc: one local and no parameter. <paramref name="flagged"/> false = a DLL that sends no flag.</summary>
    private static FakeDumpForDump ParamsFixture(bool flagged)
    {
        bool? Parm(bool isParm) => flagged ? isParm : null;
        var dump = new FakeDumpForDump();
        dump.Objects.Add(Obj("0x1", "BP_Door_C", "BlueprintGeneratedClass", "/Game/Door/BP_Door"));
        dump.ClassWalks["0x1"] = new ClassInfoModel { Name = "BP_Door_C", FullPath = "/Game/Door/BP_Door.BP_Door_C" };
        dump.FunctionWalks["0x1"] = new List<FunctionInfoModel>
        {
            new()
            {
                Name = "TryOpen", Address = "0xF1", NumParms = 3, ParmsSize = 0x18, ReturnType = "BoolProperty",
                Params = new()
                {
                    new() { Name = "Who", TypeName = "ObjectProperty", Offset = 0, Size = 8,
                            ObjectClassName = "Pawn", IsParm = Parm(true) },
                    new() { Name = "Where", TypeName = "StructProperty", Offset = 8, Size = 12, StructName = "Vector",
                            StructFields = new[] { new DynamicStructField("X", "FloatProperty", 0, 4) },
                            IsParm = Parm(true) },
                    new() { Name = "ReturnValue", TypeName = "BoolProperty", Offset = 0x14, Size = 1,
                            IsOut = true, IsReturn = true, IsParm = Parm(true) },
                    new() { Name = "CallFunc_IsValid_ReturnValue", TypeName = "BoolProperty", Offset = 0x18, Size = 1,
                            IsParm = Parm(false) },
                    new() { Name = "K2Node_DynamicCast_AsPawn", TypeName = "ObjectProperty", Offset = 0x20, Size = 8,
                            ObjectClassName = "Pawn", IsParm = Parm(false) },
                },
            },
            new() { Name = "Tick", Address = "0xF2" },
            new()
            {
                Name = "Recalc", Address = "0xF3",
                Params = new() { new() { Name = "Temp_int_Variable", TypeName = "IntProperty", Offset = 0, Size = 4,
                                         IsParm = Parm(false) } },
            },
        };
        return dump;
    }

    private static JsonElement[] FuncsOf(List<string> lines)
    {
        using var doc = JsonDocument.Parse(lines.Single(l => l.StartsWith("{\"kind\":\"class\"")));
        return doc.RootElement.GetProperty("funcs").EnumerateArray().Select(f => f.Clone()).ToArray();
    }

    private static string[] ParamNames(JsonElement func) =>
        func.GetProperty("params").EnumerateArray().Select(p => p.GetProperty("name").GetString()!).ToArray();

    [Fact]
    public void Generate_Funcs_CarryTheirParameters_NotTheBlueprintLocals()
    {
        var funcs = FuncsOf(Dump(ParamsFixture(flagged: true)));

        Assert.Equal(new[] { "Who", "Where", "ReturnValue" }, ParamNames(funcs[0]));
        Assert.Empty(ParamNames(funcs[1]));
        Assert.Empty(ParamNames(funcs[2]));
    }

    [Fact]
    public void Generate_AParameter_HasTheWalkersKeys_AndOutRetOnlyWhenSet()
    {
        var ps = FuncsOf(Dump(ParamsFixture(flagged: true)))[0].GetProperty("params").EnumerateArray().ToArray();

        var who = ps[0];
        Assert.Equal("ObjectProperty", who.GetProperty("type").GetString());
        Assert.Equal(0, who.GetProperty("offset").GetInt32());
        Assert.Equal(8, who.GetProperty("size").GetInt32());
        Assert.Equal("Pawn", who.GetProperty("obj_class").GetString());
        Assert.False(who.TryGetProperty("out", out _));
        Assert.False(who.TryGetProperty("ret", out _));
        Assert.False(who.TryGetProperty("struct_type", out _));

        // The struct's own line carries its fields; repeating them under every function that takes it would not.
        var where = ps[1];
        Assert.Equal("Vector", where.GetProperty("struct_type").GetString());
        Assert.False(where.TryGetProperty("struct_fields", out _));

        var ret = ps[2];
        Assert.True(ret.GetProperty("ret").GetBoolean());
        Assert.True(ret.GetProperty("out").GetBoolean());
    }

    [Fact]
    public void Generate_AnOlderDll_TakesTheLeadingNumParmsEntries_AndTheSummaryCountsThem()
    {
        var lines = Dump(ParamsFixture(flagged: false));
        var funcs = FuncsOf(lines);

        Assert.Equal(new[] { "Who", "Where", "ReturnValue" }, ParamNames(funcs[0]));
        Assert.Empty(ParamNames(funcs[2]));   // num_parms 0: the local is not a parameter either way
        using var summary = JsonDocument.Parse(lines[^1]);
        // TryOpen and Recalc were decided by num_parms; Tick had nothing to decide.
        Assert.Equal(2, summary.RootElement.GetProperty("params_from_num_parms").GetInt32());
    }

    [Fact]
    public void Generate_Summary_CountsNoNumParmsFallback_WhenTheDllFlagsParameters()
    {
        var lines = Dump(ParamsFixture(flagged: true));

        using var summary = JsonDocument.Parse(lines[^1]);
        Assert.Equal(0, summary.RootElement.GetProperty("params_from_num_parms").GetInt32());
    }

    [Fact]
    public void Generate_AnOlderDll_NumParmsPastTheList_WritesWhatIsThere()
    {
        // A misread num_parms (the [VND583-01] shape) must not fail the class.
        var dump = ParamsFixture(flagged: false);
        dump.FunctionWalks["0x1"][0] = new FunctionInfoModel
        {
            Name = "TryOpen", Address = "0xF1", NumParms = 75,
            Params = new() { new() { Name = "Who", TypeName = "ObjectProperty", Offset = 0, Size = 8 } },
        };

        var lines = Dump(dump);

        Assert.Equal(new[] { "Who" }, ParamNames(FuncsOf(lines)[0]));
        Assert.DoesNotContain(lines, l => l.StartsWith("{\"kind\":\"error\""));
    }
}
