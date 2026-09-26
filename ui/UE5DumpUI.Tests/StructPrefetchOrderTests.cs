using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The struct-tree prefetch must not change the export.
///
/// <para><see cref="CeXmlExportService"/> flattens nested structs DEPTH-first, and
/// that traversal order — together with the accumulated <c>Parent.Child</c> name
/// prefixes and summed offsets — <b>is</b> the field order of the emitted CE XML.
/// Batching is therefore done as a separate breadth-first PREFETCH feeding the
/// unchanged depth-first emit, rather than by reordering the recursion. These tests
/// pin that: same fields, same order, same names, same offsets — with far fewer
/// round-trips.</para>
/// </summary>
public class StructPrefetchOrderTests
{
    // A deliberately asymmetric tree, so a breadth-first leak would be obvious:
    //   root ── A ── A1
    //        └─ B ── B1 ── B1a
    private static readonly Dictionary<string, (string Name, string[] Children)> Tree = new()
    {
        ["0xROOT"] = ("root", new[] { "0xA", "0xB" }),
        ["0xA"] = ("A", new[] { "0xA1" }),
        ["0xA1"] = ("A1", Array.Empty<string>()),
        ["0xB"] = ("B", new[] { "0xB1" }),
        ["0xB1"] = ("B1", new[] { "0xB1a" }),
        ["0xB1a"] = ("B1a", Array.Empty<string>()),
    };

    private sealed class Fixture
    {
        public readonly MockPipeClient Pipe = new();
        public int SingleCalls, BatchCalls, WalkedInstances;
        public bool DisableBatch;

        public Fixture()
        {
            Pipe.SetHandler(req =>
            {
                string cmd = req["cmd"]!.GetValue<string>();
                if (cmd == "walk_instance")
                {
                    SingleCalls++; WalkedInstances++;
                    var o = Node(req["addr"]!.GetValue<string>());
                    o["ok"] = true;
                    return o;
                }
                if (cmd == "walk_instance_batch")
                {
                    if (DisableBatch) throw new InvalidOperationException("batch unsupported");
                    BatchCalls++;
                    var items = (JsonArray)req["items"]!;
                    var arr = new JsonArray();
                    foreach (var it in items)
                    {
                        WalkedInstances++;
                        arr.Add((JsonNode)Node(it!["addr"]!.GetValue<string>()));
                    }
                    return new JsonObject { ["ok"] = true, ["instances"] = arr };
                }
                throw new InvalidOperationException("unexpected cmd " + cmd);
            });
        }

        public DumpService Service => new(Pipe, new NoopLog(), UE5DumpUI.Core.IdentityCodePage.Instance);

        /// <summary>One node: a scalar leaf plus a StructProperty per child.</summary>
        private static JsonObject Node(string addr)
        {
            var (name, children) = Tree.TryGetValue(addr, out var t)
                ? t : ("?", Array.Empty<string>());
            var fields = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = name + "_leaf", ["type"] = "IntProperty",
                    ["offset"] = 4, ["size"] = 4, ["value"] = "1",
                },
            };
            int off = 16;
            foreach (var c in children)
            {
                fields.Add((JsonNode)new JsonObject
                {
                    ["name"] = Tree[c].Name,
                    ["type"] = "StructProperty",
                    ["offset"] = off,
                    ["size"] = 8,
                    ["struct_class_addr"] = "0xSC",
                    ["struct_data_addr"] = c,
                });
                off += 16;
            }
            return new JsonObject
            {
                ["addr"] = addr, ["name"] = "Obj", ["class"] = "UObject",
                ["class_addr"] = "0xSC", ["outer"] = "0x0",
                ["outer_name"] = "", ["outer_class"] = "",
                ["fields"] = fields,
            };
        }
    }

    private sealed class NoopLog : UE5DumpUI.Core.ILoggingService
    {
        public void Info(string m) { }
        public void Warn(string m) { }
        public void Error(string m) { }
        public void Error(string m, Exception ex) { }
        public void Debug(string m) { }
        public void Info(string c, string m) { }
        public void Warn(string c, string m) { }
        public void Error(string c, string m) { }
        public void Error(string c, string m, Exception ex) { }
        public void Debug(string c, string m) { }
        public void StartProcessMirror(string p) { }
        public void StopProcessMirror() { }
    }

    /// <summary>The root's fields, as the export's first walk would produce them.</summary>
    private static List<LiveFieldValue> RootFields() => new()
    {
        new LiveFieldValue
        {
            Name = "A", TypeName = "StructProperty", Offset = 16, Size = 8,
            StructClassAddr = "0xSC", StructDataAddr = "0xA",
        },
        new LiveFieldValue
        {
            Name = "B", TypeName = "StructProperty", Offset = 32, Size = 8,
            StructClassAddr = "0xSC", StructDataAddr = "0xB",
        },
    };

    private static List<string> Flatten(Dictionary<string, List<LiveFieldValue>> resolved)
    {
        var lines = new List<string>();
        foreach (var kv in resolved.OrderBy(k => k.Key, StringComparer.Ordinal))
            foreach (var f in kv.Value)
                lines.Add($"{kv.Key}|{f.Name}|{f.TypeName}|{f.Offset}|{f.Size}");
        return lines;
    }

    [Fact]
    public async Task Prefetched_output_is_identical_to_the_unbatched_output()
    {
        // Batched path.
        var fxB = new Fixture();
        var batched = await CeXmlExportService.ResolveStructFieldsAsync(
            fxB.Service, RootFields());

        // Same work with the batch command unavailable — the pre-batching behaviour.
        var fxS = new Fixture { DisableBatch = true };
        var single = await CeXmlExportService.ResolveStructFieldsAsync(
            fxS.Service, RootFields());

        Assert.Equal(Flatten(single), Flatten(batched));
    }

    [Fact]
    public async Task Nested_names_and_offsets_accumulate_exactly_as_before()
    {
        var fx = new Fixture();
        var resolved = await CeXmlExportService.ResolveStructFieldsAsync(fx.Service, RootFields());

        // B's subtree flattens depth-first with dotted prefixes and summed offsets:
        //   B_leaf @4, B1.B1_leaf @16+4, B1.B1a.B1a_leaf @16+16+4
        var b = resolved["0xB"];
        Assert.Equal(new[] { "B_leaf", "B1.B1_leaf", "B1.B1a.B1a_leaf" },
                     b.Select(f => f.Name).ToArray());
        Assert.Equal(new[] { 4, 20, 36 }, b.Select(f => f.Offset).ToArray());
    }

    [Fact]
    public async Task Batching_collapses_the_round_trips()
    {
        var fxB = new Fixture();
        await CeXmlExportService.ResolveStructFieldsAsync(fxB.Service, RootFields());

        var fxS = new Fixture { DisableBatch = true };
        await CeXmlExportService.ResolveStructFieldsAsync(fxS.Service, RootFields());

        // Same instances walked either way — but breadth-first levels instead of
        // one round-trip per struct. That ratio is the whole point.
        Assert.True(fxB.BatchCalls > 0, "batch path was not taken");
        Assert.Equal(0, fxB.SingleCalls);
        Assert.True(fxB.BatchCalls < fxS.SingleCalls,
                    $"expected fewer round-trips, got {fxB.BatchCalls} vs {fxS.SingleCalls}");
    }

    [Fact]
    public async Task An_unsupported_batch_still_produces_the_full_result()
    {
        // Older DLL: every batch throws, the service falls back per chunk, and the
        // prefetch returns whatever it managed — the emit pass walks the rest live.
        var fx = new Fixture { DisableBatch = true };
        var resolved = await CeXmlExportService.ResolveStructFieldsAsync(fx.Service, RootFields());

        Assert.True(resolved.ContainsKey("0xA"));
        Assert.True(resolved.ContainsKey("0xB"));
        Assert.Equal(new[] { "B_leaf", "B1.B1_leaf", "B1.B1a.B1a_leaf" },
                     resolved["0xB"].Select(f => f.Name).ToArray());
    }
}
