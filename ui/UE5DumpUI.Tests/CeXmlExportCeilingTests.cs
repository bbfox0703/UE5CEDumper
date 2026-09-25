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
