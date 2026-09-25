using System.Collections.Generic;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [R7-X6] The export's entry ceiling (<see cref="CeXmlExportService.MaxEmitEntries"/>) must hold inside EVERY
/// container emitter, not only between fields. Found live: DumperTest's NestedBag (two 20,000-pair TMaps, the actor's
/// LAST property) copied 98,890 entries at Array Limit 16384 with LastExportTruncated false -- the map emitter never
/// checked the budget, and nothing emitted after it could notice. Each case puts one container LAST, with more entries
/// than the whole budget, so only a check inside its own element loop can stop it.
/// </summary>
public class CeXmlExportCeilingTests
{
    // One check per element: the emitter may finish the element it started, so allow one element's worth of entries.
    private const int Slack = 4;

    private static int Entries(string xml) => xml.Split("<CheatEntry>").Length - 1;

    private static string Export(LiveFieldValue last)
    {
        var fields = new[]
        {
            new LiveFieldValue { Name = "Before", TypeName = "IntProperty", Offset = 0x28, Size = 4, TypedValue = "1" },
            last,
        };
        return CeXmlExportService.GenerateInstanceXml("\"Game.exe\"+1000", "MyObj", "UMyClass", fields);
    }

    [Fact]
    public void Map_EmittedLast_StopsAtTheCeiling_AndSaysSo()
    {
        const int n = 25_000;   // group + Key + Value per pair: 75,000 entries
        var map = new LiveFieldValue
        {
            Name = "PairsA", TypeName = "MapProperty", Offset = 0x100, Size = 80,
            MapCount = n, MapKeyType = "IntProperty", MapValueType = "IntProperty",
            MapKeySize = 4, MapValueSize = 4, MapValueOffset = 4, MapDataAddr = "0x9000",
            MapElements = Enumerable.Range(0, n).Select(i => new ContainerElementValue
            {
                Index = i, Key = i.ToString(), Value = i.ToString(), KeyHex = "00000000", ValueHex = "00000000",
            }).ToList(),
        };

        var xml = Export(map);

        Assert.True(CeXmlExportService.LastExportTruncated);
        Assert.InRange(Entries(xml), CeXmlExportService.MaxEmitEntries, CeXmlExportService.MaxEmitEntries + Slack);
    }

    [Fact]
    public void Set_EmittedLast_StopsAtTheCeiling_AndSaysSo()
    {
        const int n = 70_000;   // one leaf per element
        var set = new LiveFieldValue
        {
            Name = "BigSet", TypeName = "SetProperty", Offset = 0x100, Size = 0x50,
            SetCount = n, SetElemType = "IntProperty", SetElemSize = 4,
            SetElements = Enumerable.Range(0, n).Select(i => new ContainerElementValue
            {
                Index = i, Key = i.ToString(), KeyHex = "00000000",
            }).ToList(),
        };

        var xml = Export(set);

        Assert.True(CeXmlExportService.LastExportTruncated);
        Assert.InRange(Entries(xml), CeXmlExportService.MaxEmitEntries, CeXmlExportService.MaxEmitEntries + Slack);
    }

    [Fact]
    public void SoftObjectArray_EmittedLast_StopsAtTheCeiling_AndSaysSo()
    {
        const int n = 25_000;   // group + WeakPtr + AssetPath per element: 75,000 entries
        var arr = new LiveFieldValue
        {
            Name = "AssetRefs", TypeName = "ArrayProperty", Offset = 0x100, Size = 16,
            ArrayCount = n, ArrayInnerType = "SoftObjectProperty", ArrayElemSize = 0x28,
            SoftArrayFNameSize = 8, SoftArrayIsTopLevelAssetPath = false,
            ArrayElements = Enumerable.Range(0, n).Select(i => new ArrayElementValue
            {
                Index = i, Value = $"/Game/A{i}.A{i}", Hex = "00",
            }).ToList(),
        };

        var xml = Export(arr);

        Assert.True(CeXmlExportService.LastExportTruncated);
        Assert.InRange(Entries(xml), CeXmlExportService.MaxEmitEntries, CeXmlExportService.MaxEmitEntries + Slack);
    }

    // The same invariant for every other element loop that had no check of its own. Each would need an unusually big
    // container to overshoot (arrays arrive 4,096 per fetch), but the ceiling is documented as a hard stop.

    private static void AssertStopsAtTheCeiling(LiveFieldValue last)
    {
        var xml = Export(last);
        Assert.True(CeXmlExportService.LastExportTruncated);
        Assert.InRange(Entries(xml), CeXmlExportService.MaxEmitEntries, CeXmlExportService.MaxEmitEntries + Slack);
    }

    [Fact]
    public void ScalarArray_EmittedLast_StopsAtTheCeiling_AndSaysSo() => AssertStopsAtTheCeiling(new LiveFieldValue
    {
        Name = "Ints", TypeName = "ArrayProperty", Offset = 0x100, Size = 16,
        ArrayCount = 70_000, ArrayInnerType = "IntProperty", ArrayElemSize = 4,
        ArrayElements = Enumerable.Range(0, 70_000)
            .Select(i => new ArrayElementValue { Index = i, Value = i.ToString(), Hex = "00000000" }).ToList(),
    });

    [Fact]
    public void StringArray_EmittedLast_StopsAtTheCeiling_AndSaysSo() => AssertStopsAtTheCeiling(new LiveFieldValue
    {
        Name = "Names", TypeName = "ArrayProperty", Offset = 0x100, Size = 16,
        ArrayCount = 70_000, ArrayInnerType = "StrProperty", ArrayElemSize = 16,
        ArrayElements = Enumerable.Range(0, 70_000)
            .Select(i => new ArrayElementValue { Index = i, Value = $"s{i}" }).ToList(),
    });

    [Fact]
    public void StructArray_EmittedLast_StopsAtTheCeiling_AndSaysSo() => AssertStopsAtTheCeiling(new LiveFieldValue
    {
        Name = "Positions", TypeName = "ArrayProperty", Offset = 0x100, Size = 16,
        ArrayCount = 25_000, ArrayInnerType = "StructProperty", ArrayStructType = "Vector", ArrayElemSize = 8,
        ArrayElements = Enumerable.Range(0, 25_000).Select(i => new ArrayElementValue
        {
            Index = i, Value = "{X=0, Y=0}", Hex = "00",
            StructFields = new List<StructSubFieldValue>
            {
                new() { Name = "X", TypeName = "FloatProperty", Offset = 0, Size = 4, Value = "0.0" },
                new() { Name = "Y", TypeName = "FloatProperty", Offset = 4, Size = 4, Value = "0.0" },
            },
        }).ToList(),
    });

    [Fact]
    public void UnresolvedStructArray_EmittedLast_StopsAtTheCeiling_AndSaysSo() => AssertStopsAtTheCeiling(new LiveFieldValue
    {
        Name = "Blobs", TypeName = "ArrayProperty", Offset = 0x100, Size = 16,
        ArrayCount = 70_000, ArrayInnerType = "StructProperty", ArrayStructType = "Blob", ArrayElemSize = 8,
        ArrayElements = Enumerable.Range(0, 70_000)
            .Select(i => new ArrayElementValue { Index = i, Value = "", Hex = "00" }).ToList(),
    });

    [Fact]
    public void DataTableRows_EmittedLast_StopAtTheCeiling_AndSaySo() => AssertStopsAtTheCeiling(new LiveFieldValue
    {
        Name = "RowMap", TypeName = "DataTableRows", Offset = 0xB0, Size = 0,
        DataTableRowCount = 30_000, DataTableStructName = "RecipeRow",
        DataTableFNameSize = 8, DataTableStride = 24, DataTableRowStructAddr = "0xABC",
        DataTableRowData = Enumerable.Range(0, 30_000).Select(i => new DataTableRowInfo
        {
            SparseIndex = i, RowName = $"Row_{i}", DataAddr = $"0x{0x10000 + i * 0x10:X}",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Damage", TypeName = "FloatProperty", Offset = 0x0, Size = 4, TypedValue = "1.0" },
                new() { Name = "Level", TypeName = "IntProperty", Offset = 0x4, Size = 4, TypedValue = "1" },
            },
        }).ToList(),
    });

    [Fact]
    public void Map_UnderTheCeiling_IsNotTruncated()
    {
        // Control: the check must not fire early.
        const int n = 1_000;
        var map = new LiveFieldValue
        {
            Name = "PairsA", TypeName = "MapProperty", Offset = 0x100, Size = 80,
            MapCount = n, MapKeyType = "IntProperty", MapValueType = "IntProperty",
            MapKeySize = 4, MapValueSize = 4, MapValueOffset = 4, MapDataAddr = "0x9000",
            MapElements = Enumerable.Range(0, n).Select(i => new ContainerElementValue
            {
                Index = i, Key = i.ToString(), Value = i.ToString(), KeyHex = "00000000", ValueHex = "00000000",
            }).ToList(),
        };

        var xml = Export(map);

        Assert.False(CeXmlExportService.LastExportTruncated);
        Assert.Contains($"\"[{n - 1}] {n - 1}\"", xml);   // the last pair was emitted
        Assert.True(Entries(xml) >= 3 * n);
    }
}
