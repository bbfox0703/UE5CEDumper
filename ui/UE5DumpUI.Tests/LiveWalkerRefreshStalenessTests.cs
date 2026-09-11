using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// What a same-object, same-layout refresh must still carry across. [W1-CONTAINER-STALE]
/// [P4-CONTAINER-BASE] [P4-PTRCLASS]
///
/// Since [LWREFRESH-2026-08-21] a refresh copies values ONTO the surviving row
/// (<see cref="LiveFieldValue.CopyLiveValuesFrom"/>), and since [P4-OTHER-INSTANCE] only for the
/// same object with the same row layout. Some members can still change between two walks of that
/// same object, and the copy did not take them:
/// <list type="bullet">
/// <item>the map/set element lists, and the stride / value offset that describe them;</item>
/// <item>the container DATA addresses, which move when the container reallocates;</item>
/// <item>a retargeted pointer's class address, which the exporters use as a walk override;</item>
/// <item>and <c>ArrayElements</c>, which WAS copied, but is not observable and was assigned
/// after <c>ArrayCount</c>, so the repaint either did not fire or fired over the old list.</item>
/// </list>
/// The container DRILL has the same staleness without any refresh: it built element addresses from
/// whatever the row held when the grid was last walked.
/// Pure VM state — the stub dump service answers every walk, no running game needed.
/// </summary>
public class LiveWalkerRefreshStalenessTests
{
    private const string AddrA = "0x100000";

    private static LiveWalkerViewModel MakeVm(StubDumpService dump)
        => new(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()));

    private static InstanceWalkResult Walk(LiveFieldValue container) => new()
    {
        Address = AddrA, Name = "Chest", ClassName = "BP_Chest_C", ClassAddr = "0x900000",
        Fields = new List<LiveFieldValue>
        {
            new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4, TypedValue = "100" },
            container,
        },
    };

    private static LiveFieldValue ArrayRow(string dataAddr, params string[] values) => new()
    {
        Name = "Slots", TypeName = "ArrayProperty", Offset = 0x40, Size = 16,
        ArrayCount = values.Length, ArrayInnerType = "IntProperty", ArrayElemSize = 4,
        ArrayDataAddr = dataAddr,
        ArrayElements = values.Select((v, i) => new ArrayElementValue { Index = i, Value = v }).ToList(),
    };

    private static LiveFieldValue MapRow(int count, string v0, string dataAddr = "0x700000",
                                         int stride = 24, int valueOffset = 8) => new()
    {
        Name = "Loot", TypeName = "MapProperty", Offset = 0x50, Size = 80,
        MapCount = count, MapKeyType = "IntProperty", MapValueType = "IntProperty",
        MapKeySize = 4, MapValueSize = 4,
        // The DLL publishes the geometry and the data address only when the map had elements.
        MapDataAddr = count > 0 ? dataAddr : "",
        MapStride = count > 0 ? stride : 0,
        MapValueOffset = count > 0 ? valueOffset : 0,
        MapElements = count > 0
            ? new List<ContainerElementValue>
              {
                  new() { Index = 0, Key = "1", Value = v0 },
                  new() { Index = 1, Key = "2", Value = "20" },
              }
            : null,
    };

    private static LiveFieldValue SetRow(string e0) => new()
    {
        Name = "Tags", TypeName = "SetProperty", Offset = 0x60, Size = 80,
        SetCount = 2, SetElemType = "IntProperty", SetElemSize = 4, SetStride = 12,
        SetDataAddr = "0x780000",
        SetElements = new List<ContainerElementValue>
        {
            new() { Index = 0, Key = e0 },
            new() { Index = 1, Key = "5" },
        },
    };

    private static async Task<LiveWalkerViewModel> OnA(StubDumpService dump, InstanceWalkResult first)
    {
        dump.RegisterStruct(AddrA, first);
        var vm = MakeVm(dump);
        await vm.NavigateToAddressCommand.ExecuteAsync(AddrA);
        return vm;
    }

    /// <summary>Every PropertyChanged the row raises, with DisplayValue as it read AT THAT MOMENT —
    /// a repaint that fires before the new data is in place repaints the old text.</summary>
    private static List<(string Prop, string Display)> Track(LiveFieldValue row)
    {
        var seen = new List<(string, string)>();
        row.PropertyChanged += (_, e) => seen.Add((e.PropertyName ?? "", row.DisplayValue));
        return seen;
    }

    private static void AssertRepaintedWithTheNewText(LiveFieldValue row, List<(string Prop, string Display)> seen)
    {
        var display = seen.Where(s => s.Prop == nameof(LiveFieldValue.DisplayValue)).ToList();
        Assert.True(display.Count > 0, "DisplayValue was never raised, so the cell never repainted");
        Assert.Equal(row.DisplayValue, display[^1].Display);
        Assert.Contains(seen, s => s.Prop == nameof(LiveFieldValue.ValueTooltip));
    }

    // ── [W1-CONTAINER-STALE] the element lists ─────────────────────────────────

    [Fact]
    public async Task Refresh_MapEntriesChange_TheKeptRowShowsTheNewEntries()
    {
        var dump = new StubDumpService();
        var vm = await OnA(dump, Walk(MapRow(2, "10")));
        var row = vm.Fields[1];
        var seen = Track(row);

        dump.RegisterStruct(AddrA, Walk(MapRow(2, "99")));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Same(row, vm.Fields[1]);   // same object, same layout: the row survives
        // Exporters read this list straight off the row, so this is export data, not just a cell.
        Assert.Equal("99", row.MapElements![0].Value);
        Assert.Contains("1: 99", row.DisplayValue);
        AssertRepaintedWithTheNewText(row, seen);
    }

    [Fact]
    public async Task Refresh_SetElementsChange_TheKeptRowShowsTheNewElements()
    {
        var dump = new StubDumpService();
        var vm = await OnA(dump, Walk(SetRow("3")));
        var row = vm.Fields[1];
        var seen = Track(row);

        dump.RegisterStruct(AddrA, Walk(SetRow("8")));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Same(row, vm.Fields[1]);
        Assert.Equal("8", row.SetElements![0].Key);
        AssertRepaintedWithTheNewText(row, seen);
    }

    [Fact]
    public async Task Refresh_ArrayValuesChange_AtTheSameCount_Repaints()
    {
        // ArrayElements WAS copied, but nothing raised: an unchanged count raises no notification.
        var dump = new StubDumpService();
        var vm = await OnA(dump, Walk(ArrayRow("0x500000", "1", "2", "3")));
        var row = vm.Fields[1];
        var seen = Track(row);

        dump.RegisterStruct(AddrA, Walk(ArrayRow("0x500000", "1", "2", "7")));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains("7", row.DisplayValue);
        AssertRepaintedWithTheNewText(row, seen);
    }

    [Fact]
    public async Task Refresh_ArrayGrows_TheRepaintSeesTheNewList_NotTheOldOne()
    {
        // ArrayCount used to be assigned first: its notification fired while the OLD list was
        // still in place, and the later list assignment raised nothing.
        var dump = new StubDumpService();
        var vm = await OnA(dump, Walk(ArrayRow("0x500000", "1", "2", "3")));
        var row = vm.Fields[1];
        var seen = Track(row);

        dump.RegisterStruct(AddrA, Walk(ArrayRow("0x500000", "1", "2", "3", "4")));
        await vm.RefreshCommand.ExecuteAsync(null);

        AssertRepaintedWithTheNewText(row, seen);
    }

    // ── [P4-CONTAINER-BASE] the data addresses, and the geometry that travels with them ─────

    [Fact]
    public async Task Refresh_ArrayReallocated_TheRowTakesTheNewDataAddress_AndTheDrillUsesIt()
    {
        var dump = new StubDumpService();
        var vm = await OnA(dump, Walk(ArrayRow("0x500000", "1", "2", "3")));

        // Grew past Max: reallocated to a new buffer.
        dump.RegisterStruct(AddrA, Walk(ArrayRow("0x600000", "1", "2", "3", "4")));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("0x600000", vm.Fields[1].ArrayDataAddr);

        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields[1]);

        // Element [2] at the LIVE buffer. Before the fix: current values next to addresses in the
        // freed allocation, and an inline edit wrote into freed heap.
        var e2 = Assert.Single(vm.Fields, f => f.Name == "[2]");
        Assert.Equal("0x600008", e2.FieldAddress);
    }

    [Fact]
    public async Task Refresh_MapGainsItsFirstEntries_TheGeometryAndBaseArriveWithThem()
    {
        // The trap recorded under [W1-CONTAINER-STALE]: stride, value offset and data address are
        // published only when the map HAS elements. Fresh elements paired with the first walk's
        // zero stride fall back to the client-side guess audit #5 V2 retired.
        var dump = new StubDumpService();
        var vm = await OnA(dump, Walk(MapRow(0, "")));
        var row = vm.Fields[1];
        Assert.False(row.IsContainerNavigable);
        var seen = Track(row);

        dump.RegisterStruct(AddrA, Walk(MapRow(2, "10")));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Same(row, vm.Fields[1]);
        Assert.Equal(24, row.MapStride);
        Assert.Equal(8, row.MapValueOffset);
        Assert.Equal("0x700000", row.MapDataAddr);
        Assert.Equal(2, row.MapElements!.Count);
        // The [] drill button binds to this; without a notification a realized row never shows it.
        Assert.True(row.IsContainerNavigable);
        Assert.Contains(seen, s => s.Prop == nameof(LiveFieldValue.IsContainerNavigable));
    }

    // ── [P4-PTRCLASS] ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_PointerRetargetsToAnotherClass_TheRowTakesTheNewClassAddress()
    {
        static InstanceWalkResult PawnWalk(string ptr, string name, string cls, string clsAddr) => new()
        {
            Address = AddrA, Name = "PC", ClassName = "BP_PC_C", ClassAddr = "0x900000",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Pawn", TypeName = "ObjectProperty", Offset = 0x30, Size = 8,
                        PtrAddress = ptr, PtrName = name, PtrClassName = cls, PtrClassAddr = clsAddr },
            },
        };

        var dump = new StubDumpService();
        var vm = await OnA(dump, PawnWalk("0xAAA000", "Hero", "BP_Hero_C", "0x910000"));
        var row = vm.Fields[0];

        dump.RegisterStruct(AddrA, PawnWalk("0xBBB000", "Car", "BP_Car_C", "0x920000"));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Same(row, vm.Fields[0]);
        Assert.Equal("0xBBB000", row.PtrAddress);
        // Both exporters pass this to walk_instance as the class override: the old one walked the
        // car with the hero's class.
        Assert.Equal("0x920000", row.PtrClassAddr);
    }

    // ── The drill, with no refresh at all ──────────────────────────────────────

    [Fact]
    public async Task Drill_AfterTheArrayReallocatedSinceTheWalk_UsesTheLiveBuffer()
    {
        var dump = new StubDumpService();
        var vm = await OnA(dump, Walk(ArrayRow("0x500000", "1", "2", "3")));

        // The game reallocated the array after the grid was walked. No refresh.
        dump.RegisterStruct(AddrA, Walk(ArrayRow("0x600000", "1", "2", "9")));

        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields[1]);

        var e2 = Assert.Single(vm.Fields, f => f.Name == "[2]");
        Assert.Equal("0x600008", e2.FieldAddress);
        Assert.Equal("9", e2.TypedValue);
    }

    [Fact]
    public async Task Drill_WhenTheReReadFindsNoSuchRow_OpensTheRowAsItWas_AndSaysSo()
    {
        var dump = new StubDumpService();
        var vm = await OnA(dump, Walk(ArrayRow("0x500000", "1", "2", "3")));

        // The address no longer answers with that object (freed, the slot reused by another class).
        dump.RegisterStruct(AddrA, new InstanceWalkResult
        {
            Address = AddrA, ClassName = "BP_Other_C", Fields = new List<LiveFieldValue>(),
        });

        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields[1]);

        var e2 = Assert.Single(vm.Fields, f => f.Name == "[2]");
        Assert.Equal("0x500008", e2.FieldAddress);   // what the row held: nothing fresher was copied
        Assert.Contains("Could not re-read 'Slots'", vm.StatusText);
    }

    [Fact]
    public async Task Drill_AfterTheMapReallocatedSinceTheWalk_UsesTheLiveBuffer()
    {
        var dump = new StubDumpService();
        var vm = await OnA(dump, Walk(MapRow(2, "10", dataAddr: "0x700000")));

        dump.RegisterStruct(AddrA, Walk(MapRow(2, "44", dataAddr: "0x800000")));

        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields[1]);

        // Element [1]'s VALUE: base + 1 * stride(24) + valueOffset(8).
        var e1 = Assert.Single(vm.Fields, f => f.Name.StartsWith("[1]"));
        Assert.Equal("0x800020", e1.FieldAddress);
        var e0 = Assert.Single(vm.Fields, f => f.Name.StartsWith("[0]"));
        Assert.Contains("44", e0.TypedValue);
    }
}
