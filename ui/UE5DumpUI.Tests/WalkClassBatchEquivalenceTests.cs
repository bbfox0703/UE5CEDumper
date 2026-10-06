using System.Text;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The build-693 walk_class_batch optimisation collapses N pipe round-
/// trips into N / chunkSize. The user's explicit concern when greenlighting
/// it: "if SDK export silently drops a class or field after the change, I
/// can't tell." This test file is the byte-equivalence safety net.
///
/// Strategy:
///   1. Build a realistic fixture (250 classes, mixed BPGC variants,
///      ScriptStruct, every property type, functions, inheritance,
///      engine-vs-game paths).
///   2. Run each consumer (SdkExportService, DumpAllService) with TWO
///      stubs producing identical raw data but exposing it via different
///      pipe paths:
///        - The BATCH stub answers WalkClassesBatchAsync from a single
///          dictionary lookup, mirroring the DLL contract.
///        - The FORCED-FALLBACK stub throws InvalidOperationException
///          from WalkClassesBatchAsync, forcing the consumer to fall
///          back to N WalkClassAsync calls.
///   3. Assert the two outputs are byte-identical.
///
/// If a refactor in SdkExport or DumpAll drops a class, mis-orders the
/// fallback, or mishandles partial chunks, this test fails.
/// </summary>
public class WalkClassBatchEquivalenceTests
{
    // ------------------------------------------------------------------
    // Test stubs: same backing data, different pipe paths
    // ------------------------------------------------------------------

    /// <summary>Stub that exposes both batched and single-call paths via
    /// the same dictionary lookup — the realistic happy-path simulation.</summary>
    private sealed class HappyPathDump : StubDumpService
    {
        public override Task<List<EnumDefinition>> ListEnumsAsync(CancellationToken ct = default)
            => Task.FromResult(new List<EnumDefinition>());   // Dump All lists enums once per run (D2)
        public List<UObjectNode> Objects { get; } = new();
        public Dictionary<string, ClassInfoModel> ClassWalks { get; } = new();
        public Dictionary<string, List<FunctionInfoModel>> FunctionWalks { get; } = new();

        public override Task<ObjectListResult> GetObjectListAsync(int offset, int limit, CancellationToken ct = default, bool includePath = false)
        {
            var slice = Objects.Skip(offset).Take(limit).ToList();
            return Task.FromResult(new ObjectListResult
            {
                Total = Objects.Count, Scanned = slice.Count, Objects = slice,
            });
        }

        public override Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default)
            => Task.FromResult(ClassWalks.TryGetValue(addr, out var ci)
                ? ci : new ClassInfoModel { Fields = new List<FieldInfoModel>() });

        public override Task<List<FunctionInfoModel>> WalkFunctionsAsync(string addr, CancellationToken ct = default)
            => Task.FromResult(FunctionWalks.TryGetValue(addr, out var fns)
                ? fns : new List<FunctionInfoModel>());

        // Batched lookup answers from the same dictionary so each
        // returned ClassInfoModel is the SAME REFERENCE the single-call
        // path returns — element-for-element equality is trivially true.
        public override Task<List<ClassInfoModel>> WalkClassesBatchAsync(string[] addrs, CancellationToken ct = default)
        {
            var result = new List<ClassInfoModel>(addrs.Length);
            foreach (var a in addrs)
            {
                result.Add(ClassWalks.TryGetValue(a, out var ci)
                    ? ci : new ClassInfoModel { Fields = new List<FieldInfoModel>() });
            }
            return Task.FromResult(result);
        }
    }

    /// <summary>Stub that throws on every WalkClassesBatchAsync call,
    /// forcing the consumer's per-class fallback path. Used to prove
    /// the fallback produces identical output to the batched path.</summary>
    private sealed class ForcedFallbackDump : StubDumpService
    {
        public override Task<List<EnumDefinition>> ListEnumsAsync(CancellationToken ct = default)
            => Task.FromResult(new List<EnumDefinition>());   // Dump All lists enums once per run (D2)
        public List<UObjectNode> Objects { get; } = new();
        public Dictionary<string, ClassInfoModel> ClassWalks { get; } = new();
        public Dictionary<string, List<FunctionInfoModel>> FunctionWalks { get; } = new();

        public int BatchAttempts { get; private set; }

        public override Task<ObjectListResult> GetObjectListAsync(int offset, int limit, CancellationToken ct = default, bool includePath = false)
        {
            var slice = Objects.Skip(offset).Take(limit).ToList();
            return Task.FromResult(new ObjectListResult
            {
                Total = Objects.Count, Scanned = slice.Count, Objects = slice,
            });
        }

        public override Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default)
            => Task.FromResult(ClassWalks.TryGetValue(addr, out var ci)
                ? ci : new ClassInfoModel { Fields = new List<FieldInfoModel>() });

        public override Task<List<FunctionInfoModel>> WalkFunctionsAsync(string addr, CancellationToken ct = default)
            => Task.FromResult(FunctionWalks.TryGetValue(addr, out var fns)
                ? fns : new List<FunctionInfoModel>());

        public override Task<List<ClassInfoModel>> WalkClassesBatchAsync(string[] addrs, CancellationToken ct = default)
        {
            BatchAttempts++;
            throw new InvalidOperationException(
                "Forced fallback — simulating a DLL that doesn't implement walk_class_batch.");
        }
    }

    // ------------------------------------------------------------------
    // Fixture builder — same data populated into both stub variants.
    // ------------------------------------------------------------------

    private static (List<UObjectNode> objects,
                    Dictionary<string, ClassInfoModel> classWalks,
                    Dictionary<string, List<FunctionInfoModel>> functionWalks)
        BuildFixture(int classCount)
    {
        var objects = new List<UObjectNode>();
        var classWalks = new Dictionary<string, ClassInfoModel>();
        var functionWalks = new Dictionary<string, List<FunctionInfoModel>>();

        // Cycle through every meta + property + function permutation
        // so the fixture exercises every code path in the writers
        // (struct types, container inner types, enums, bool masks,
        // bpgc-vs-class meta, engine-vs-game paths, function flags).
        var metas = new[]
        {
            "Class", "BlueprintGeneratedClass", "AnimBlueprintGeneratedClass",
            "WidgetBlueprintGeneratedClass", "DynamicClass", "ScriptStruct"
        };
        var fieldTypes = new[]
        {
            "FloatProperty", "IntProperty", "BoolProperty", "StrProperty",
            "ObjectProperty", "ClassProperty", "StructProperty",
            "ArrayProperty", "MapProperty", "SetProperty",
            "EnumProperty", "ByteProperty", "DelegateProperty",
        };

        for (int i = 0; i < classCount; i++)
        {
            string addr = $"0x{(0x10000000 + i):X8}";
            string meta = metas[i % metas.Length];
            // Half engine, half game — exercises GameOnly filter.
            string path = (i % 2 == 0)
                ? $"/Script/Engine.EngineClass{i}"
                : $"/Game/MyGame/BP_Class_{i}_C";
            string name = (i % 2 == 0) ? $"EngineClass{i}" : $"BP_Class_{i}_C";

            objects.Add(new UObjectNode
            {
                Address = addr, Name = name, ClassName = meta, FullPath = path,
            });

            // Build a ClassInfoModel with ~10 fields covering each type
            // category. Field-count varies (5-15) so different StringBuilder
            // sizes get exercised.
            int fieldCount = 5 + (i % 11);
            var fields = new List<FieldInfoModel>(fieldCount);
            int offset = 0x28;
            for (int j = 0; j < fieldCount; j++)
            {
                string typeName = fieldTypes[j % fieldTypes.Length];

                // Build the field with EVERY optional metadata bucket
                // populated where applicable. init-only properties on
                // FieldInfoModel mean we have to set them all here in
                // one initializer expression — no mutation after.
                fields.Add(new FieldInfoModel
                {
                    Name = $"Field_{j}",
                    TypeName = typeName,
                    Offset = offset,
                    Size = 8,
                    Address = $"0x{(0x20000000 + i * 100 + j):X8}",

                    StructType   = typeName == "StructProperty" ? $"FStruct{j}" : "",
                    ObjClassName = typeName switch
                    {
                        "ObjectProperty" => $"UObjectType{j}",
                        "ClassProperty"  => "AActor",
                        _                => "",
                    },
                    InnerType        = typeName == "ArrayProperty" ? "StructProperty" : "",
                    InnerStructType  = typeName == "ArrayProperty" ? $"FInner{j}"     : "",
                    InnerObjClass    = "",
                    KeyType          = typeName == "MapProperty" ? "NameProperty" : "",
                    KeyStructType    = "",
                    ValueType        = typeName == "MapProperty" ? "StructProperty" : "",
                    ValueStructType  = typeName == "MapProperty" ? $"FVal{j}"        : "",
                    ElemType         = typeName == "SetProperty" ? "ObjectProperty" : "",
                    ElemStructType   = "",
                    EnumName         = typeName switch
                    {
                        "EnumProperty"                     => $"EEnumName{j}",
                        "ByteProperty" when (j % 2) == 0   => $"EByteEnum{j}",
                        _                                  => "",
                    },
                    BoolFieldMask    = typeName == "BoolProperty" ? (1 << (j % 8)) : 0,
                    // Exercise the prop_flags (hex) + array_dim JSONL emit —
                    // varied non-zero flags + occasional static-array dim so
                    // both new conditional-emit branches are covered by the
                    // batch↔fallback byte-equivalence assertion.
                    PropertyFlags    = 0x0040000000000001UL | ((ulong)j << 8),
                    ArrayDim         = (j % 4 == 3) ? 4 : 1,
                });

                offset += 16;
            }

            // Some classes get functions, some don't. Engine ones get
            // engine-flavoured names; game ones get gameplay names.
            classWalks[addr] = new ClassInfoModel
            {
                Name = name,
                FullPath = path,
                SuperName = (i > 0) ? $"BaseClass{i - 1}" : "UObject",
                SuperAddress = (i > 0) ? $"0x{(0x10000000 + i - 1):X8}" : "",
                PropertiesSize = offset,
                Fields = fields,
            };

            // ~30% of classes get functions.
            if (i % 3 == 0)
            {
                var fns = new List<FunctionInfoModel>();
                int fnCount = 1 + (i % 4);
                for (int k = 0; k < fnCount; k++)
                {
                    fns.Add(new FunctionInfoModel
                    {
                        Name = $"Func_{i}_{k}",
                        Address = $"0x{(0x30000000 + i * 10 + k):X8}",
                        ReturnType = (k % 2 == 0) ? "void" : "FloatProperty",
                        NumParms = (byte)(k % 4),
                        ParmsSize = (ushort)(k * 8),
                        FunctionFlags = 0x4020600u + (uint)k,
                    });
                }
                functionWalks[addr] = fns;
            }
        }

        return (objects, classWalks, functionWalks);
    }

    private static HappyPathDump MakeHappy(int classCount)
    {
        var dump = new HappyPathDump();
        var (objects, classWalks, functionWalks) = BuildFixture(classCount);
        dump.Objects.AddRange(objects);
        foreach (var kv in classWalks) dump.ClassWalks[kv.Key] = kv.Value;
        foreach (var kv in functionWalks) dump.FunctionWalks[kv.Key] = kv.Value;
        return dump;
    }

    private static ForcedFallbackDump MakeFallback(int classCount)
    {
        var dump = new ForcedFallbackDump();
        var (objects, classWalks, functionWalks) = BuildFixture(classCount);
        dump.Objects.AddRange(objects);
        foreach (var kv in classWalks) dump.ClassWalks[kv.Key] = kv.Value;
        foreach (var kv in functionWalks) dump.FunctionWalks[kv.Key] = kv.Value;
        return dump;
    }

    private static EngineState DefaultEngineState() => new()
    {
        UEVersion = 505,
        ModuleName = "TestGame-Win64-Shipping.exe",
        ModuleBase = "0x7FF600000000",
        GObjectsAddr = "0x7FF600100000",
        GNamesAddr = "0x7FF600200000",
        GWorldAddr = "0x7FF600300000",
        ObjectCount = 1_000_000,
        PeHash = "ABC123",
    };

    private static List<string> DumpToLines(StubDumpService dump, DumpOptions? opts = null)
    {
        var ms = new MemoryStream();
        DumpAllService.GenerateAsync(dump, DefaultEngineState(), ms, opts).GetAwaiter().GetResult();
        ms.Position = 0;
        using var sr = new StreamReader(ms, Encoding.UTF8);
        var lines = new List<string>();
        string? line;
        while ((line = sr.ReadLine()) != null) lines.Add(NormalizeLine(line));
        return lines;
    }

    /// <summary>
    /// Strip the wall-clock <c>"dumped_at":"&lt;ISO timestamp&gt;"</c> field
    /// from the meta line so two runs produce byte-equal output. The
    /// timestamp drifts by milliseconds between consecutive runs and
    /// is unrelated to batch correctness — any other meta drift would
    /// still surface in the comparison.
    /// </summary>
    private static string NormalizeLine(string line)
    {
        const string key = "\"dumped_at\":\"";
        int start = line.IndexOf(key, StringComparison.Ordinal);
        if (start < 0) return line;
        int valStart = start + key.Length;
        int valEnd = line.IndexOf('"', valStart);
        if (valEnd < 0) return line;
        // Replace the timestamp content with a placeholder. Length-
        // neutral changes would also be fine here.
        return line.Substring(0, valStart) + "<TS>" + line.Substring(valEnd);
    }

    // ==================================================================
    // SdkExportService — batch path output equals fallback path output
    // ==================================================================

    [Theory]
    [InlineData(0)]    // empty edge case
    [InlineData(1)]    // single-element chunk
    [InlineData(199)]  // chunk-size minus one
    [InlineData(200)]  // exactly one full chunk
    [InlineData(201)]  // one full + one of size 1
    [InlineData(400)]  // two full chunks
    [InlineData(250)]  // realistic mid-size
    public async Task SdkExport_BatchedPathEqualsFallbackPath(int classCount)
    {
        var happy    = MakeHappy(classCount);
        var fallback = MakeFallback(classCount);

        var ct = TestContext.Current.CancellationToken;
        string batchedSdk  = await SdkExportService.GenerateFullSdkAsync(happy,    ct: ct);
        string fallbackSdk = await SdkExportService.GenerateFullSdkAsync(fallback, ct: ct);

        // Byte-for-byte equality. Any drift between the batched and
        // fallback paths fails here.
        Assert.Equal(batchedSdk, fallbackSdk);

        // Sanity: the fallback path did exercise WalkClassesBatchAsync
        // (so we know we actually tested the fallback, not just two
        // identical happy paths). Empty class lists skip the loop
        // entirely.
        if (classCount > 0)
            Assert.True(fallback.BatchAttempts > 0,
                "ForcedFallbackDump should have rejected at least one batch attempt");
    }

    // ==================================================================
    // DumpAllService — batch path output equals fallback path output
    // ==================================================================

    [Theory]
    [InlineData(0,   true,  true)]
    [InlineData(1,   true,  true)]
    [InlineData(199, true,  false)]
    [InlineData(200, false, true)]
    [InlineData(201, true,  true)]
    [InlineData(250, true,  true)]
    public void DumpAll_BatchedPathEqualsFallbackPath(int classCount, bool includeFunctions, bool includeInstanceCounts)
    {
        var happy    = MakeHappy(classCount);
        var fallback = MakeFallback(classCount);

        var opts = new DumpOptions(
            GameOnly: false,
            IncludeFunctions: includeFunctions,
            IncludeInstanceCounts: includeInstanceCounts);

        var batchedLines  = DumpToLines(happy, opts);
        var fallbackLines = DumpToLines(fallback, opts);

        // Line-by-line equality (JSONL — newline-significant).
        Assert.Equal(batchedLines.Count, fallbackLines.Count);
        for (int i = 0; i < batchedLines.Count; i++)
        {
            Assert.Equal(batchedLines[i], fallbackLines[i]);
        }

        if (classCount > 0)
            Assert.True(fallback.BatchAttempts > 0,
                "ForcedFallbackDump should have rejected at least one batch attempt");
    }

    [Fact]
    public void DumpAll_GameOnlyFilter_BatchedPathEqualsFallbackPath()
    {
        // 250 classes split engine/game 50/50 — GameOnly filter retains
        // 125 and skips 125. Validates the chunk buffer doesn't mis-
        // count the skipped engine entries.
        var happy    = MakeHappy(250);
        var fallback = MakeFallback(250);

        var opts = new DumpOptions(GameOnly: true);

        var batchedLines  = DumpToLines(happy, opts);
        var fallbackLines = DumpToLines(fallback, opts);

        Assert.Equal(batchedLines, fallbackLines);
        Assert.Contains(batchedLines, l => l.Contains("\"classes_skipped_engine\":125"));
    }

    // ==================================================================
    // Defensive: a returned batch with the WRONG element count must
    // trigger fallback (preserves output even if a future DLL bug ever
    // truncated responses).
    // ==================================================================

    private sealed class TruncatedBatchDump : StubDumpService
    {
        public override Task<List<EnumDefinition>> ListEnumsAsync(CancellationToken ct = default)
            => Task.FromResult(new List<EnumDefinition>());   // Dump All lists enums once per run (D2)
        public List<UObjectNode> Objects { get; } = new();
        public Dictionary<string, ClassInfoModel> ClassWalks { get; } = new();
        public Dictionary<string, List<FunctionInfoModel>> FunctionWalks { get; } = new();

        public override Task<ObjectListResult> GetObjectListAsync(int offset, int limit, CancellationToken ct = default, bool includePath = false)
        {
            var slice = Objects.Skip(offset).Take(limit).ToList();
            return Task.FromResult(new ObjectListResult
            {
                Total = Objects.Count, Scanned = slice.Count, Objects = slice,
            });
        }

        public override Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default)
            => Task.FromResult(ClassWalks.TryGetValue(addr, out var ci)
                ? ci : new ClassInfoModel { Fields = new List<FieldInfoModel>() });

        public override Task<List<FunctionInfoModel>> WalkFunctionsAsync(string addr, CancellationToken ct = default)
            => Task.FromResult(FunctionWalks.TryGetValue(addr, out var fns)
                ? fns : new List<FunctionInfoModel>());

        // Returns ONE FEWER result than the input → mismatched count
        // should trigger consumer's fallback to single calls.
        public override Task<List<ClassInfoModel>> WalkClassesBatchAsync(string[] addrs, CancellationToken ct = default)
        {
            var result = new List<ClassInfoModel>(addrs.Length);
            for (int i = 0; i < addrs.Length - 1; i++)
            {
                result.Add(ClassWalks.TryGetValue(addrs[i], out var ci)
                    ? ci : new ClassInfoModel { Fields = new List<FieldInfoModel>() });
            }
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task SdkExport_TruncatedBatch_FallsBackToSingleCalls_OutputUnchanged()
    {
        const int classCount = 250;

        var happy = MakeHappy(classCount);
        var truncated = new TruncatedBatchDump();
        var (objects, classWalks, functionWalks) = BuildFixture(classCount);
        truncated.Objects.AddRange(objects);
        foreach (var kv in classWalks) truncated.ClassWalks[kv.Key] = kv.Value;
        foreach (var kv in functionWalks) truncated.FunctionWalks[kv.Key] = kv.Value;

        var ct = TestContext.Current.CancellationToken;
        string happySdk      = await SdkExportService.GenerateFullSdkAsync(happy,     ct: ct);
        string truncatedSdk  = await SdkExportService.GenerateFullSdkAsync(truncated, ct: ct);

        Assert.Equal(happySdk, truncatedSdk);
    }
}
