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
/// Layer 3 of the walk_class_batch safety net, applied to
/// <c>walk_instance_batch</c>: prove the batched path yields output identical to
/// the single path, and that a batch failure degrades instead of losing data.
///
/// <para>The net exists because the consumer is a CE export — a large,
/// file-shaped artefact where one silently dropped or mis-paired field is
/// invisible until somebody needs it months later. Layer 1 is structural (the
/// DLL batch is a trivial loop over the single-call path); layer 2 is the shared
/// serialiser/deserialiser pair; this is layer 3.</para>
/// </summary>
public class WalkInstanceBatchEquivalenceTests
{
    // -- Fixture: reuses the existing MockPipeClient via its handler hook rather
    // than introducing a second IPipeClient fake that could drift out of sync. --

    private sealed class Fixture
    {
        public readonly MockPipeClient Pipe = new();
        public int BatchCalls, SingleCalls;
        /// <summary>Batch replies throw, forcing the per-chunk single-call fallback.</summary>
        public bool BreakBatch;
        /// <summary>Batch returns one row short - the mis-pairing hazard.</summary>
        public bool ShortBatch;
        public readonly List<string> Commands = new();

        public Fixture()
        {
            Pipe.SetHandler(req =>
            {
                string cmd = req["cmd"]!.GetValue<string>();
                Commands.Add(cmd);

                if (cmd == "walk_instance")
                {
                    SingleCalls++;
                    var one = Instance(req["addr"]!.GetValue<string>());
                    one["ok"] = true;
                    return one;
                }

                if (cmd == "walk_instance_batch")
                {
                    BatchCalls++;
                    if (BreakBatch) throw new InvalidOperationException("simulated batch failure");

                    var items = (JsonArray)req["items"]!;
                    var arr = new JsonArray();
                    int n = ShortBatch ? Math.Max(0, items.Count - 1) : items.Count;
                    for (int i = 0; i < n; i++)
                        arr.Add(Instance(items[i]!["addr"]!.GetValue<string>()));
                    return new JsonObject { ["ok"] = true, ["instances"] = arr };
                }

                throw new InvalidOperationException("unexpected cmd " + cmd);
            });
        }

        public DumpService Service => new(Pipe, new NoopLog(), UE5DumpUI.Core.IdentityCodePage.Instance);

        /// <summary>One instance's wire shape - deliberately exercising the OPTIONAL
        /// keys (stale / props_size present, is_definition absent), since those are
        /// where an independently-written batch encoder diverges first.</summary>
        private static JsonObject Instance(string addr) => new()
        {
            ["addr"] = addr,
            ["name"] = "Obj_" + addr,
            ["class"] = "AActor",
            ["class_addr"] = "0xC1A55",
            ["outer"] = "0x0FE7",
            ["outer_name"] = "Level",
            ["outer_class"] = "ULevel",
            ["stale"] = true,
            ["props_size"] = 512,
            ["fields"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Health", ["type"] = "FloatProperty",
                    ["offset"] = 128, ["value"] = "100.0", ["size"] = 4,
                },
                new JsonObject
                {
                    ["name"] = "Target", ["type"] = "ObjectProperty",
                    ["offset"] = 136, ["value"] = "0xBEEF", ["size"] = 8,
                    ["ptr_addr"] = "0xBEEF", ["ptr_class_addr"] = "0xC1A55",
                },
            },
        };
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

    /// <summary>The runner's per-test token — every awaited call that takes one gets it,
    /// so a cancelled run stops here instead of at the next checkpoint (xUnit1051).</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void AssertSame(InstanceWalkResult a, InstanceWalkResult b, string because)
    {
        Assert.Equal(a.Address, b.Address);
        Assert.Equal(a.Name, b.Name);
        Assert.Equal(a.ClassName, b.ClassName);
        Assert.Equal(a.ClassAddr, b.ClassAddr);
        Assert.Equal(a.OuterAddr, b.OuterAddr);
        Assert.Equal(a.OuterName, b.OuterName);
        Assert.Equal(a.OuterClassName, b.OuterClassName);
        Assert.Equal(a.IsDefinition, b.IsDefinition);
        Assert.Equal(a.IsStale, b.IsStale);
        Assert.Equal(a.PropertiesSize, b.PropertiesSize);
        Assert.Equal(a.Fields.Count, b.Fields.Count);
        for (int i = 0; i < a.Fields.Count; i++)
        {
            Assert.Equal(a.Fields[i].Name, b.Fields[i].Name);
            Assert.Equal(a.Fields[i].TypeName, b.Fields[i].TypeName);
            Assert.Equal(a.Fields[i].Offset, b.Fields[i].Offset);
            Assert.Equal(a.Fields[i].HexValue, b.Fields[i].HexValue);
            Assert.Equal(a.Fields[i].TypedValue, b.Fields[i].TypedValue);
            Assert.Equal(a.Fields[i].IsGuessed, b.Fields[i].IsGuessed);
            Assert.Equal(a.Fields[i].BoolBitIndex, b.Fields[i].BoolBitIndex);
            Assert.Equal(a.Fields[i].Size, b.Fields[i].Size);
            Assert.Equal(a.Fields[i].PtrAddress, b.Fields[i].PtrAddress);
            Assert.Equal(a.Fields[i].PtrClassAddr, b.Fields[i].PtrClassAddr);
        }
        Assert.True(true, because);
    }

    // ── The equivalence claim ──

    [Fact]
    public async Task Batch_result_is_identical_to_the_single_call_result()
    {
        var addrs = new[] { "0xA1", "0xB2", "0xC3" };

        var singleFx = new Fixture();
        var single = new List<InstanceWalkResult>();
        foreach (var a in addrs)
            single.Add(await singleFx.Service.WalkInstanceAsync(a, "0xC1A55", ct: Ct));

        var batchFx = new Fixture();
        var batched = await batchFx.Service.WalkInstanceBatchAsync(
            addrs.Select(a => (a, (string?)"0xC1A55")).ToList(), ct: Ct);

        Assert.Equal(single.Count, batched.Count);
        for (int i = 0; i < single.Count; i++)
            AssertSame(single[i], batched[i], "batch must match single field-for-field");

        // ...and the point of the exercise: 3 round-trips became 1.
        Assert.Equal(3, singleFx.SingleCalls);
        Assert.Equal(1, batchFx.BatchCalls);
        Assert.Equal(0, batchFx.SingleCalls);
    }

    [Fact]
    public async Task Optional_keys_survive_the_batch_path()
    {
        // stale / props_size present, is_definition absent — the optional keys are
        // where an independently-written batch encoder diverges first.
        var r = (await new Fixture().Service.WalkInstanceBatchAsync(
            new[] { ("0xA1", (string?)null) }, ct: Ct))[0];
        Assert.True(r.IsStale);
        Assert.Equal(512, r.PropertiesSize);
        Assert.False(r.IsDefinition);
    }

    [Fact]
    public async Task Results_stay_positionally_aligned_with_the_request()
    {
        var addrs = Enumerable.Range(0, 7).Select(i => $"0x{i:X}").ToList();
        var batched = await new Fixture().Service.WalkInstanceBatchAsync(
            addrs.Select(a => (a, (string?)null)).ToList(), ct: Ct);

        Assert.Equal(addrs.Count, batched.Count);
        for (int i = 0; i < addrs.Count; i++)
            Assert.Equal(addrs[i], batched[i].Address);
    }

    // ── Degradation, not data loss ──

    [Fact]
    public async Task A_failed_batch_replays_the_chunk_as_single_calls()
    {
        // The older-DLL / batch-error path: results must still be complete.
        var fx = new Fixture { BreakBatch = true };
        var addrs = new[] { "0xA1", "0xB2", "0xC3" };

        var batched = await fx.Service.WalkInstanceBatchAsync(
            addrs.Select(a => (a, (string?)null)).ToList(), ct: Ct);

        Assert.Equal(3, batched.Count);
        Assert.Equal(3, fx.SingleCalls);              // fell back
        for (int i = 0; i < addrs.Length; i++)
            Assert.Equal(addrs[i], batched[i].Address);
    }

    [Fact]
    public async Task A_short_batch_reply_falls_back_rather_than_mis_pairing()
    {
        // The dangerous failure: N requests, N-1 replies. Consuming them positionally
        // would silently attach one instance's fields to a DIFFERENT address, which
        // in a CE export is a wrong pointer chain that looks perfectly valid.
        var fx = new Fixture { ShortBatch = true };
        var addrs = new[] { "0xA1", "0xB2", "0xC3" };

        var batched = await fx.Service.WalkInstanceBatchAsync(
            addrs.Select(a => (a, (string?)null)).ToList(), ct: Ct);

        Assert.Equal(3, batched.Count);
        Assert.Equal(3, fx.SingleCalls);
        for (int i = 0; i < addrs.Length; i++)
            Assert.Equal(addrs[i], batched[i].Address);
    }

    [Fact]
    public async Task Chunking_splits_large_inputs_but_keeps_order()
    {
        int n = DumpService.WalkInstanceBatchChunk * 2 + 37;
        var addrs = Enumerable.Range(0, n).Select(i => $"0x{i:X}").ToList();
        var fx = new Fixture();

        var batched = await fx.Service.WalkInstanceBatchAsync(
            addrs.Select(a => (a, (string?)null)).ToList(), ct: Ct);

        Assert.Equal(n, batched.Count);
        Assert.Equal(3, fx.BatchCalls);              // 200 + 200 + 37
        for (int i = 0; i < n; i++)
            Assert.Equal(addrs[i], batched[i].Address);
    }

    [Fact]
    public async Task An_empty_input_issues_no_calls()
    {
        var fx = new Fixture();
        var batched = await fx.Service.WalkInstanceBatchAsync(
            Array.Empty<(string, string?)>(), ct: Ct);
        Assert.Empty(batched);
        Assert.Empty(fx.Commands);
    }
}
