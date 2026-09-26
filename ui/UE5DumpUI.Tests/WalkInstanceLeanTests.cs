using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The safety net for the LEAN walk payload (build 2351, measured in
/// docs/multipipe-eval.md §10.6).
///
/// <para><b>The claim being defended.</b> `lean: true` asks the DLL to omit the
/// keys a CE XML export never reads — the per-instance header and every decoded
/// VALUE (<c>hex</c> / <c>value</c> / <c>str_value</c> / <c>enum_name</c> /
/// <c>ptr_name</c> / element hex / struct-sub-field value …). That is a claim
/// about the EXPORTER, not about the wire: if any of those keys secretly
/// influences a description, a CE type or an offset, a lean export silently
/// produces a different — and wrong — cheat table. So the headline test runs the
/// same export twice, once on full payloads and once on lean ones, and demands
/// byte-identical XML.</para>
///
/// <para><b>The mirror.</b> <see cref="Lean"/> below strips exactly what
/// <c>Fern.cpp</c>'s <c>SerializeField(fv, lean)</c> / <c>EncodeInstanceWalkToJson</c>
/// omit. It is a hand-kept mirror of the C++ contract — the one thing that can
/// drift. Any key added to (or removed from) the DLL's lean gate belongs here in
/// the same commit.</para>
/// </summary>
public class WalkInstanceLeanTests
{
    // ── the mirror of the DLL's lean contract ──

    /// <summary>What the UI would deserialise from a LEAN response: the same
    /// object with the export-dead keys absent (a missing key parses to its
    /// default, which is what these blanks represent).</summary>
    private static InstanceWalkResult Lean(InstanceWalkResult r) => new()
    {
        // Instance header: lean keeps only `addr` (+ `stale`, not modelled here).
        Address = r.Address,
        Name = "", ClassName = "", ClassAddr = "",
        OuterAddr = "", OuterName = "", OuterClassName = "",
        IsDefinition = false, PropertiesSize = 0,
        Fields = r.Fields.Select(LeanField).ToList(),
    };

    private static LiveFieldValue LeanField(LiveFieldValue f) => new()
    {
        // kept
        Name = f.Name, TypeName = f.TypeName, Offset = f.Offset, Size = f.Size,
        IsGuessed = f.IsGuessed,
        PtrAddress = f.PtrAddress, PtrClassName = f.PtrClassName, PtrClassAddr = f.PtrClassAddr,
        BoolBitIndex = f.BoolBitIndex,
        ArrayCount = f.ArrayCount, ArrayInnerType = f.ArrayInnerType,
        ArrayStructType = f.ArrayStructType, ArrayElemSize = f.ArrayElemSize,
        ArrayDataAddr = f.ArrayDataAddr, ArrayStructClassAddr = f.ArrayStructClassAddr,
        SoftArrayFNameSize = f.SoftArrayFNameSize,
        SoftArrayIsTopLevelAssetPath = f.SoftArrayIsTopLevelAssetPath,
        ArrayEnumAddr = f.ArrayEnumAddr, ArrayEnumEntries = f.ArrayEnumEntries,
        MapCount = f.MapCount, MapKeyType = f.MapKeyType, MapValueType = f.MapValueType,
        MapKeySize = f.MapKeySize, MapValueSize = f.MapValueSize,
        MapValueOffset = f.MapValueOffset, MapDataAddr = f.MapDataAddr,
        MapKeyStructAddr = f.MapKeyStructAddr, MapKeyStructType = f.MapKeyStructType,
        MapValueStructAddr = f.MapValueStructAddr, MapValueStructType = f.MapValueStructType,
        SetCount = f.SetCount, SetElemType = f.SetElemType, SetElemSize = f.SetElemSize,
        SetDataAddr = f.SetDataAddr, SetElemStructAddr = f.SetElemStructAddr,
        SetElemStructType = f.SetElemStructType,
        StructDataAddr = f.StructDataAddr, StructClassAddr = f.StructClassAddr,
        StructTypeName = f.StructTypeName,
        EnumAddr = f.EnumAddr, EnumEntries = f.EnumEntries,

        // dropped: HexValue, TypedValue, StrValue, EnumName, EnumValue, PtrName,
        // BoolFieldMask, BoolByteOffset, ArrayInnerAddr — plus the per-element
        // strips below.
        ArrayElements = f.ArrayElements?.Select(LeanElem).ToList(),
        MapElements = f.MapElements?.Select(LeanContainerElem).ToList(),
        SetElements = f.SetElements?.Select(LeanContainerElem).ToList(),
    };

    private static ArrayElementValue LeanElem(ArrayElementValue e) => new()
    {
        Index = e.Index, Value = e.Value, EnumName = e.EnumName, RawIntValue = e.RawIntValue,
        PtrAddress = e.PtrAddress, PtrClassName = e.PtrClassName,
        Hex = "",        // "h" — the biggest single unused key measured
        PtrName = "",    // "pn" — display-only
        StructFields = e.StructFields?.Select(sf => new StructSubFieldValue
        {
            Name = sf.Name, TypeName = sf.TypeName, Offset = sf.Offset, Size = sf.Size,
            PtrClassName = sf.PtrClassName,
            Value = "",   // "v" — display-only
            PtrAddress = sf.PtrAddress, PtrName = "", PtrClassAddr = sf.PtrClassAddr,
        }).ToList(),
    };

    private static ContainerElementValue LeanContainerElem(ContainerElementValue e) => new()
    {
        Index = e.Index, Key = e.Key, Value = e.Value,
        ValueHex = e.ValueHex,       // "vh" IS read (dropdown key) — kept on purpose
        KeyHex = "",                 // "kh" — display-only
        KeyPtrName = e.KeyPtrName, KeyPtrAddress = e.KeyPtrAddress,
        KeyPtrClassName = e.KeyPtrClassName,
        ValuePtrName = e.ValuePtrName, ValuePtrAddress = e.ValuePtrAddress,
        ValuePtrClassName = e.ValuePtrClassName,
    };

    // ── fixture ──

    /// <summary>A stub that answers every walk from one table, optionally leaning
    /// the answer, and records whether the caller asked for lean.</summary>
    private sealed class LeanAwareStub : StubDumpService
    {
        private readonly Dictionary<string, InstanceWalkResult> _byAddr = new(StringComparer.Ordinal);
        private readonly bool _serveLean;
        public readonly List<bool> LeanRequests = new();

        public LeanAwareStub(bool serveLean) => _serveLean = serveLean;

        public void Register(string addr, params LiveFieldValue[] fields)
            => Register(addr, (IEnumerable<LiveFieldValue>)fields);

        public void Register(string addr, IEnumerable<LiveFieldValue> fields)
            => _byAddr[addr] = new InstanceWalkResult
            {
                Address = addr, Name = "Obj_" + addr, ClassName = "UObject",
                ClassAddr = "0xC1A55", OuterAddr = "0x0FE7", OuterName = "Level",
                OuterClassName = "ULevel", PropertiesSize = 512,
                Fields = fields.ToList(),
            };

        public override Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
            int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false,
            CancellationToken ct = default)
        {
            LeanRequests.Add(lean);
            var r = _byAddr.TryGetValue(addr, out var hit)
                ? hit
                : new InstanceWalkResult { Address = addr, Fields = new List<LiveFieldValue>() };
            return Task.FromResult(_serveLean ? Lean(r) : r);
        }

        public override async Task<IReadOnlyList<InstanceWalkResult>> WalkInstanceBatchAsync(
            IReadOnlyList<(string Addr, string? ClassAddr)> items, int arrayLimit = 64,
            int previewLimit = 2, bool fillGaps = false, bool lean = false,
            CancellationToken ct = default)
        {
            var outp = new List<InstanceWalkResult>(items.Count);
            foreach (var (a, c) in items)
                outp.Add(await WalkInstanceAsync(a, c, arrayLimit, previewLimit, fillGaps, lean, ct));
            return outp;
        }
    }

    /// <summary>One object's fields, covering every emit branch a lean payload could
    /// plausibly break: scalar, bool bitfield, FString, enum-with-entries, object
    /// pointer, inline struct, struct array with sub-fields, and a map whose values
    /// are structs. Parameterised by address so the SAME rich shape can sit at the
    /// root AND behind a walked pointer — the walked copy is the one the lean strip
    /// actually reaches, and therefore the one that gives this test teeth.</summary>
    private static List<LiveFieldValue> RichFields(
        string tag, string ptrAddr, string structAddr, string arrayDataAddr, string mapDataAddr) => new()
    {
        new LiveFieldValue
        {
            Name = tag + "Health", TypeName = "FloatProperty", Offset = 0x20, Size = 4,
            HexValue = "0000C842", TypedValue = "100",
        },
        new LiveFieldValue
        {
            Name = tag + "bIsDead", TypeName = "BoolProperty", Offset = 0x30, Size = 1,
            HexValue = "04", TypedValue = "true (bit 2, mask 0x04)",
            BoolBitIndex = 2, BoolFieldMask = 0x04, BoolByteOffset = 0,
        },
        new LiveFieldValue
        {
            Name = tag + "PlayerName", TypeName = "StrProperty", Offset = 0x38, Size = 16,
            HexValue = "A0B1C2D300000000", StrValue = "Frieren",
        },
        new LiveFieldValue
        {
            Name = tag + "Team", TypeName = "EnumProperty", Offset = 0x48, Size = 1,
            HexValue = "01", TypedValue = "1",
            EnumName = "ETeam::Blue", EnumValue = 1, EnumAddr = "0xE0000" + tag,
            EnumEntries = new List<EnumEntryValue>
            {
                new() { Value = 0, Name = "ETeam::Red" },
                new() { Value = 1, Name = "ETeam::Blue" },
            },
        },
        new LiveFieldValue
        {
            Name = tag + "Target", TypeName = "ObjectProperty", Offset = 0x50, Size = 8,
            HexValue = "0020000000000000", PtrAddress = ptrAddr,
            PtrName = "BP_Enemy_C_1", PtrClassName = "APawn", PtrClassAddr = "0xC0FFEE",
        },
        new LiveFieldValue
        {
            Name = tag + "Location", TypeName = "StructProperty", Offset = 0x60, Size = 12,
            HexValue = "000000000000000000000000",
            StructDataAddr = structAddr, StructClassAddr = "0x57RUC7", StructTypeName = "Vector",
        },
        new LiveFieldValue
        {
            Name = tag + "Tunes", TypeName = "ArrayProperty", Offset = 0x70, Size = 16,
            ArrayCount = 2, ArrayInnerType = "StructProperty", ArrayStructType = "Tune",
            ArrayElemSize = 8, ArrayDataAddr = arrayDataAddr, ArrayStructClassAddr = "0x70NE",
            ArrayInnerAddr = "0x1NNE2",
            ArrayElements = new List<ArrayElementValue>
            {
                new()
                {
                    Index = 0, Value = "{Tune}", Hex = "0100000002000000",
                    StructFields = new List<StructSubFieldValue>
                    {
                        new() { Name = "Level", TypeName = "IntProperty", Offset = 0, Size = 4, Value = "1" },
                        new() { Name = "Exp", TypeName = "IntProperty", Offset = 4, Size = 4, Value = "2" },
                    },
                },
                new()
                {
                    Index = 1, Value = "{Tune}", Hex = "0300000004000000",
                    StructFields = new List<StructSubFieldValue>
                    {
                        new() { Name = "Level", TypeName = "IntProperty", Offset = 0, Size = 4, Value = "3" },
                        new() { Name = "Exp", TypeName = "IntProperty", Offset = 4, Size = 4, Value = "4" },
                    },
                },
            },
        },
        new LiveFieldValue
        {
            Name = tag + "Missions", TypeName = "MapProperty", Offset = 0x90, Size = 80,
            MapCount = 1, MapKeyType = "NameProperty", MapValueType = "StructProperty",
            MapKeySize = 8, MapValueSize = 0x10, MapValueOffset = 8, MapDataAddr = mapDataAddr,
            MapValueStructAddr = "0x4155", MapValueStructType = "MissionInfo",
            MapElements = new List<ContainerElementValue>
            {
                new()
                {
                    Index = 0, Key = "mc1om_001", Value = "{MissionInfo}",
                    KeyHex = "1122334455667788", ValueHex = "0200000000000000",
                },
            },
        },
    };

    /// <summary>The panel's own fields. In the real app these come from the Live
    /// Walker grid load, which is NOT lean (the grid shows values) — so the root is
    /// deliberately full-fat in both runs, and only walked content differs.</summary>
    private static List<LiveFieldValue> RootFields()
        => RichFields("", "0x2000", "0x3000", "0x4000", "0x5000");

    private static LiveFieldValue[] XyzFields() =>
    [
        new LiveFieldValue { Name = "X", TypeName = "FloatProperty", Offset = 0, Size = 4, HexValue = "00000000", TypedValue = "0" },
        new LiveFieldValue { Name = "Y", TypeName = "FloatProperty", Offset = 4, Size = 4, HexValue = "00000000", TypedValue = "0" },
        new LiveFieldValue { Name = "Z", TypeName = "FloatProperty", Offset = 8, Size = 4, HexValue = "00000000", TypedValue = "0" },
    ];

    private static LeanAwareStub MakeStub(bool serveLean)
    {
        var stub = new LeanAwareStub(serveLean);

        // The root's pointer target carries the SAME rich shape, so the lean strip
        // reaches a bool bitfield, an FString, an enum dropdown, a struct array with
        // sub-fields and a struct-valued map that all end up in the emitted XML.
        stub.Register("0x2000", RichFields("E", "0x8000", "0x6000", "0x9000", "0xA000"));
        stub.Register("0x8000",
            new LiveFieldValue
            {
                Name = "DeepHP", TypeName = "IntProperty", Offset = 0x10, Size = 4,
                HexValue = "E8030000", TypedValue = "1000",
            });

        // Inline structs (root Location -> 0x3000, walked Location -> 0x6000)
        stub.Register("0x3000", XyzFields());
        stub.Register("0x6000", XyzFields());

        // Map value structs: MapDataAddr + 0*stride + valueOffset(8)
        stub.Register("0x5008",
            new LiveFieldValue { Name = "Rank", TypeName = "IntProperty", Offset = 4, Size = 4, HexValue = "02000000", TypedValue = "2" });
        stub.Register("0xA008",
            new LiveFieldValue { Name = "DeepRank", TypeName = "IntProperty", Offset = 4, Size = 4, HexValue = "03000000", TypedValue = "3" });
        return stub;
    }

    private static async Task<(string Xml, LeanAwareStub Stub)> ExportAsync(bool serveLean)
    {
        var stub = MakeStub(serveLean);
        var fields = RootFields();
        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);

        await CeXmlExportService.ResolveDrilldownAsync(
            stub, fields, resolvedStructs, resolvedInstances,
            depth: 2, arrayLimit: 64, onWalk: null, lean: true,
            ct: TestContext.Current.CancellationToken);

        var breadcrumbs = new[]
        {
            new BreadcrumbItem { Address = "0x1000", Label = "Root", FieldName = "Root" },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"TestGame.exe\"+1000", "Root", breadcrumbs, fields,
            resolvedStructs, resolvedInstances: resolvedInstances,
            descShowOffset: true, descShowType: true);
        return (xml, stub);
    }

    // ── the equivalence claim ──

    [Fact]
    public async Task Lean_and_full_payloads_produce_identical_CE_XML()
    {
        var (fullXml, _) = await ExportAsync(serveLean: false);
        var (leanXml, _) = await ExportAsync(serveLean: true);

        // Not "similar" — identical. The export is structural, so a lean payload
        // that changes ONE character means a key the exporter really does read
        // was dropped, and the lean flag must give it back.
        Assert.Equal(fullXml, leanXml);

        // Sanity: the fixture actually exercised the drill-down, so an identical
        // pair cannot be two identically-empty exports.
        Assert.Contains("Health", fullXml);       // root scalar
        Assert.Contains("EHealth", fullXml);      // pointer target walked (leaned)
        Assert.Contains("EPlayerName", fullXml);  // FString leaf inside leaned content
        Assert.Contains("DeepRank", fullXml);     // map value struct under the walk
        Assert.Contains("Level", fullXml);        // struct-array sub-field
        Assert.Contains("ETeam::Blue", fullXml);  // enum DropDownList
    }

    [Fact]
    public async Task The_CE_XML_resolve_asks_for_lean()
    {
        var (_, stub) = await ExportAsync(serveLean: true);
        Assert.NotEmpty(stub.LeanRequests);
        Assert.All(stub.LeanRequests, Assert.True);
    }

    [Fact]
    public async Task The_shared_resolver_does_NOT_lean_by_default()
    {
        // CsxExportService calls the SAME resolver, and CSX genuinely reads hex /
        // bool_mask / bool_byte_offset. Defaulting to lean would corrupt it, so the
        // default must stay full-fat.
        var stub = MakeStub(serveLean: false);
        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);

        await CeXmlExportService.ResolveDrilldownAsync(
            stub, RootFields(), resolvedStructs, resolvedInstances, depth: 2,
            ct: TestContext.Current.CancellationToken);

        Assert.NotEmpty(stub.LeanRequests);
        Assert.All(stub.LeanRequests, Assert.False);
    }

    // ── the wire flag ──

    [Fact]
    public async Task Lean_is_sent_on_the_wire_only_when_asked()
    {
        var pipe = new MockPipeClient();
        var seen = new List<JsonObject>();
        pipe.SetHandler(req =>
        {
            seen.Add(req);
            return new JsonObject { ["ok"] = true, ["addr"] = "0xA1", ["fields"] = new JsonArray() };
        });
        var svc = new DumpService(pipe, new MockLoggingService(), UE5DumpUI.Core.IdentityCodePage.Instance);

        await svc.WalkInstanceAsync("0xA1", ct: TestContext.Current.CancellationToken);
        await svc.WalkInstanceAsync("0xA1", lean: true, ct: TestContext.Current.CancellationToken);

        // Absent (not `false`) by default: the pre-lean request shape is unchanged,
        // so an older DLL sees exactly what it saw before.
        Assert.Null(seen[0]["lean"]);
        Assert.True(seen[1]!["lean"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Lean_is_sent_on_the_batch_wire_only_when_asked()
    {
        var pipe = new MockPipeClient();
        var seen = new List<JsonObject>();
        pipe.SetHandler(req =>
        {
            seen.Add(req);
            var arr = new JsonArray();
            foreach (var _ in (JsonArray)req["items"]!)
                arr.Add((JsonNode)new JsonObject { ["addr"] = "0xA1", ["fields"] = new JsonArray() });
            return new JsonObject { ["ok"] = true, ["instances"] = arr };
        });
        var svc = new DumpService(pipe, new MockLoggingService(), UE5DumpUI.Core.IdentityCodePage.Instance);
        var items = new[] { ("0xA1", (string?)null) };

        await svc.WalkInstanceBatchAsync(items, ct: TestContext.Current.CancellationToken);
        await svc.WalkInstanceBatchAsync(items, lean: true, ct: TestContext.Current.CancellationToken);

        Assert.Null(seen[0]["lean"]);
        Assert.True(seen[1]!["lean"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_lean_batch_that_falls_back_to_single_calls_stays_lean()
    {
        // The chunk-failure fallback replays single calls; if it dropped the flag
        // the payload would silently go back to full-fat mid-export.
        var pipe = new MockPipeClient();
        var seen = new List<JsonObject>();
        pipe.SetHandler(req =>
        {
            seen.Add(req);
            if (req["cmd"]!.GetValue<string>() == "walk_instance_batch")
                throw new InvalidOperationException("simulated older DLL");
            return new JsonObject { ["ok"] = true, ["addr"] = "0xA1", ["fields"] = new JsonArray() };
        });
        var svc = new DumpService(pipe, new MockLoggingService(), UE5DumpUI.Core.IdentityCodePage.Instance);

        await svc.WalkInstanceBatchAsync(new[] { ("0xA1", (string?)null) }, lean: true,
                                        ct: TestContext.Current.CancellationToken);

        var single = seen.Single(r => r["cmd"]!.GetValue<string>() == "walk_instance");
        Assert.True(single["lean"]!.GetValue<bool>());
    }
}
