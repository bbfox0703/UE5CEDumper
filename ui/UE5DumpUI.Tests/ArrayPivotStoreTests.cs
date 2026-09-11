using System.IO;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Phase C6 — array-element pivot against a real temp-file SQLite store. Captures a
/// cargo-style struct array (FCargoSlot{ ItemID, Quantity }) on two owners and pivots
/// it by inner-key value (ItemID), proving the reorder-/owner-immune inner join.
/// </summary>
public class ArrayPivotStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public ArrayPivotStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UE5DumpArrPivot_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(new MockPlatformService(_tempDir));
        _store.SetActiveGame("ARRPIVOT");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    private static string IntHex(int v) =>
        string.Concat(BitConverter.GetBytes(v).Select(b => b.ToString("X2")));

    // One owner with a Cargo array of (ItemID inner key, Quantity inner numeric).
    private static SnapshotCapturedObject Cargo(int idx, params (string item, int qty)[] slots)
    {
        var o = new SnapshotCapturedObject
        {
            Index = idx, Addr = $"0x{0x2000 + idx:X}", Name = $"PS_{idx}",
            ClassName = "PlayerState", OuterClassName = "World", Path = $"/G.M:L.PlayerState_{idx}",
        };
        var arr = new SnapshotCapturedArray { Field = "Cargo" };
        int ei = 0;
        foreach (var (item, qty) in slots)
        {
            var el = new SnapshotCapturedArrayElement { Index = ei++, KeyName = "ItemID", KeyValue = item };
            el.Fields.Add(new SnapshotCapturedField { Name = "Quantity", Type = "IntProperty", Hex = IntHex(qty), Offset = 0x8 });
            arr.Elements.Add(el);
        }
        o.Arrays.Add(arr);
        return o;
    }

    private async Task<long> SeedCargoAsync(CancellationToken ct)
    {
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "cargo" }, ct);
        await _store.WriteChunkAsync(id, new[]
        {
            Cargo(1, ("Fuel", 100), ("Ore", 50)),
            Cargo(2, ("Fuel", 80)),
        }, ct);
        await _store.FinalizeSnapshotAsync(id, 2, 3, ct);
        return id;
    }

    [Fact]
    public async Task ListArrayClasses_OnlyClassesWithCapturedArrays()
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await SeedCargoAsync(ct);

        var classes = await _store.ListPivotArrayClassesAsync(id, ct);
        var ps = Assert.Single(classes);
        Assert.Equal("PlayerState", ps.ClassName);
        Assert.Equal(2, ps.InstanceCount);   // two owners
    }

    [Fact]
    public async Task ListArrayFields_ReportsInnerKeyAndElementRows()
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await SeedCargoAsync(ct);

        var fields = await _store.ListPivotArrayFieldsAsync(id, "PlayerState", ct);
        var cargo = Assert.Single(fields);
        Assert.Equal("Cargo", cargo.ArrayField);
        Assert.Equal("ItemID", cargo.InnerKeyName);
        Assert.Equal(3, cargo.ElementCount);   // 3 elements -- one inner prop each, so rows agree here
    }

    [Fact]
    public async Task ListArrayFields_CountsElements_NotInnerPropRows()
    {
        // [W1-ARRAYCOUNT] One row is stored per inner prop per element, and the count was COUNT(*)
        // over those rows, so the picker's "elements" grew with every inner numeric prop. The test
        // above pinned it with a ONE-prop fixture, where rows and elements happen to agree.
        var ct = TestContext.Current.CancellationToken;
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "cargo2" }, ct);
        await _store.WriteChunkAsync(id, new[] { TwoPropCargo(1, 2), TwoPropCargo(2, 1) }, ct);
        await _store.FinalizeSnapshotAsync(id, 2, 6, ct);

        var cargo = Assert.Single(await _store.ListPivotArrayFieldsAsync(id, "PlayerState", ct));
        Assert.Equal(3, cargo.ElementCount);   // 3 elements, 6 inner-prop rows
    }

    private static SnapshotCapturedObject TwoPropCargo(int idx, int elements)
    {
        var o = new SnapshotCapturedObject
        {
            Index = idx, Addr = $"0x{0x3000 + idx:X}", Name = $"PS_{idx}",
            ClassName = "PlayerState", OuterClassName = "World", Path = $"/G.M:L.PlayerState_{idx}",
        };
        var arr = new SnapshotCapturedArray { Field = "Cargo" };
        for (int e = 0; e < elements; e++)
        {
            var el = new SnapshotCapturedArrayElement { Index = e, KeyName = "ItemID", KeyValue = $"Item{e}" };
            el.Fields.Add(new SnapshotCapturedField { Name = "Quantity", Type = "IntProperty", Hex = IntHex(10 + e), Offset = 0x8 });
            el.Fields.Add(new SnapshotCapturedField { Name = "Weight",   Type = "IntProperty", Hex = IntHex(20 + e), Offset = 0xC });
            arr.Elements.Add(el);
        }
        o.Arrays.Add(arr);
        return o;
    }

    [Fact]
    public async Task ListArrayProps_ReportsInnerNumericProps()
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await SeedCargoAsync(ct);

        var props = await _store.ListPivotArrayPropsAsync(id, "PlayerState", "Cargo", ct);
        var qty = Assert.Single(props);
        Assert.Equal("Quantity", qty.Name);
        Assert.Equal(3, qty.DistinctCount);    // {100, 50, 80}
    }

    [Fact]
    public async Task PivotArray_GroupsByInnerKeyValue_AcrossOwners()
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await SeedCargoAsync(ct);

        var res = await _store.PivotArrayAsync(new ArrayPivotQuery
        {
            SnapshotId = id, ClassName = "PlayerState", ArrayField = "Cargo",
            ValueProps = new() { "Quantity" },
        }, ct);

        Assert.Equal(2, res.GroupCount);       // Fuel, Ore
        Assert.Equal(3, res.InstanceCount);    // 3 elements total
        // Fuel spans both owners (qty 100 + 80) → most populous, collision-rendered.
        var fuel = res.Rows[0];
        Assert.Equal("Fuel", fuel.KeyValue);
        Assert.Equal(2, fuel.Count);
        Assert.Equal("Quantity=⟨2: 100,80⟩", fuel.ValuesDisplay);
        // Ore is a single element.
        var ore = res.Rows[1];
        Assert.Equal("Ore", ore.KeyValue);
        Assert.Equal(1, ore.Count);
        Assert.Equal("Quantity=50", ore.ValuesDisplay);
    }

    // One owner with a Cargo array whose elements carry NO inner key (SelectArrayInnerKey found none, or a
    // leaf container): the population the review of 4920cb89 found collapsing into one "(no key)" group.
    private static SnapshotCapturedObject Keyless(int idx, params int[] qtys)
    {
        var o = new SnapshotCapturedObject
        {
            Index = idx, Addr = $"0x{0x3000 + idx:X}", Name = $"PS_{idx}",
            ClassName = "PlayerState", OuterClassName = "World", Path = $"/G.M:L.PlayerState_{idx}",
        };
        var arr = new SnapshotCapturedArray { Field = "Cargo" };
        for (int e = 0; e < qtys.Length; e++)
        {
            var el = new SnapshotCapturedArrayElement { Index = e };   // no KeyName / KeyValue
            el.Fields.Add(new SnapshotCapturedField { Name = "Quantity", Type = "IntProperty", Hex = IntHex(qtys[e]), Offset = 0x8 });
            arr.Elements.Add(el);
        }
        o.Arrays.Add(arr);
        return o;
    }

    [Fact]
    public async Task PivotArray_KeylessElements_GroupByTheirIndex()
    {
        // Every keyless element of every owner got the constant key "(no key)" -- ONE group -- while the
        // status line said "elem index group(s)". Element 3, the one that changed, could not be singled out.
        var ct = TestContext.Current.CancellationToken;
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "keyless" }, ct);
        await _store.WriteChunkAsync(id, new[] { Keyless(1, 10, 20, 30), Keyless(2, 11, 21) }, ct);
        await _store.FinalizeSnapshotAsync(id, 2, 5, ct);

        var res = await _store.PivotArrayAsync(new ArrayPivotQuery
        {
            SnapshotId = id, ClassName = "PlayerState", ArrayField = "Cargo",
            ValueProps = new() { "Quantity" },
        }, ct);

        Assert.Equal(3, res.GroupCount);                                  // [0], [1], [2]
        Assert.Equal(5, res.InstanceCount);
        Assert.DoesNotContain(res.Rows, r => r.KeyValue == "(no key)");
        Assert.Equal(2, res.Rows.Single(r => r.KeyValue == "[0]").Count); // index 0 of both owners
        Assert.Equal(1, res.Rows.Single(r => r.KeyValue == "[2]").Count);
    }

    [Fact]
    public async Task PivotArray_ScalarPivotIgnoresArrayRows()
    {
        // The scalar class pivot must not see array-element rows (array_field IS NULL
        // guard) — PlayerState has only array data, so it lists no scalar fields.
        var ct = TestContext.Current.CancellationToken;
        long id = await SeedCargoAsync(ct);

        var scalarFields = await _store.ListPivotFieldsAsync(id, "PlayerState", ct);
        Assert.Empty(scalarFields);
        var scalarClasses = await _store.ListPivotClassesAsync(id, ct);
        Assert.Empty(scalarClasses);  // no scalar (non-array) rows captured
    }
}
