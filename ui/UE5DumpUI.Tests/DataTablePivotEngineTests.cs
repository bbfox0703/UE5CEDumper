using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// DataTable-native pivot engine. A DataTable is already a
/// RowName→struct map, so the pivot is zero-config: one group per row keyed by
/// RowName, struct fields as projected columns. Pure C#, no pipe.
/// </summary>
public class DataTablePivotEngineTests
{
    private static LiveFieldValue Field(string name, string type, string typed, string hex = "")
        => new() { Name = name, TypeName = type, TypedValue = typed, HexValue = hex };

    private static DataTableRowInfo Row(string rowName, string dataAddr, params LiveFieldValue[] fields)
        => new() { RowName = rowName, DataAddr = dataAddr, Fields = fields.ToList() };

    private static DataTableWalkResult Table(int rowCount, string structName, params DataTableRowInfo[] rows)
        => new() { RowCount = rowCount, RowStructName = structName, Rows = rows.ToList() };

    [Fact]
    public void Build_OneGroupPerRow_KeyedByRowName()
    {
        var dt = Table(2, "FItemRow",
            Row("Sword", "0x1000", Field("Damage", "IntProperty", "50")),
            Row("Shield", "0x2000", Field("Damage", "IntProperty", "0")));

        var res = DataTablePivotEngine.Build(dt, new[] { "Damage" });

        Assert.Equal(2, res.GroupCount);
        Assert.Equal(2, res.InstanceCount);
        Assert.False(res.Truncated);
        // Every row is its own group (RowName unique) → Count 1, no collisions.
        Assert.All(res.Rows, r => Assert.Equal(1, r.Count));
        Assert.Equal(new[] { "Sword", "Shield" }, res.Rows.Select(r => r.KeyValue));
        // Projected value carries the field name=value, and the row's data address
        // is the CE handoff target.
        Assert.Equal("Damage=50", res.Rows[0].ValuesDisplay);
        Assert.Equal("0x1000", res.Rows[0].ObjAddr);
    }

    [Fact]
    public void Build_EmptyValueFields_ProjectsAllFields()
    {
        var dt = Table(1, "FItemRow",
            Row("Sword", "0x1000",
                Field("Damage", "IntProperty", "50"),
                Field("Weight", "FloatProperty", "3")));

        var res = DataTablePivotEngine.Build(dt, System.Array.Empty<string>());

        Assert.Single(res.Rows);
        Assert.Contains("Damage=50", res.Rows[0].ValuesDisplay);
        Assert.Contains("Weight=3", res.Rows[0].ValuesDisplay);
    }

    [Fact]
    public void Build_SelectedFieldsOnly_AreProjected_InRequestedOrder()
    {
        var dt = Table(1, "FItemRow",
            Row("Sword", "0x1000",
                Field("Damage", "IntProperty", "50"),
                Field("Weight", "FloatProperty", "3"),
                Field("Rarity", "ByteProperty", "2")));

        var res = DataTablePivotEngine.Build(dt, new[] { "Rarity", "Damage" });

        // Only the two picked fields, in the order requested.
        Assert.Equal("Rarity=2   ·   Damage=50", res.Rows[0].ValuesDisplay);
    }

    [Fact]
    public void Build_UnnamedRow_RendersPlaceholderKey()
    {
        var dt = Table(1, "FRow", Row("", "0x1000", Field("V", "IntProperty", "1")));
        var res = DataTablePivotEngine.Build(dt, new[] { "V" });
        Assert.Equal("(unnamed)", res.Rows[0].KeyValue);
    }

    [Fact]
    public void Fields_AggregatesAcrossRows_TypeDistinctAndInstanceCounts()
    {
        var dt = Table(3, "FItemRow",
            Row("A", "0x1", Field("Damage", "IntProperty", "10", "0A000000"), Field("Tier", "ByteProperty", "1", "01")),
            Row("B", "0x2", Field("Damage", "IntProperty", "10", "0A000000"), Field("Tier", "ByteProperty", "2", "02")),
            Row("C", "0x3", Field("Damage", "IntProperty", "20", "14000000"), Field("Tier", "ByteProperty", "2", "02")));

        var fields = DataTablePivotEngine.Fields(dt);

        // First-seen field order preserved.
        Assert.Equal(new[] { "Damage", "Tier" }, fields.Select(f => f.Name));

        var damage = fields.First(f => f.Name == "Damage");
        Assert.Equal("IntProperty", damage.DeclaredType);
        Assert.Equal(3, damage.InstanceCount);   // present in all 3 rows
        Assert.Equal(2, damage.DistinctCount);    // 10, 10, 20 → 2 distinct

        var tier = fields.First(f => f.Name == "Tier");
        Assert.Equal(2, tier.DistinctCount);       // 1, 2, 2 → 2 distinct
    }

    [Fact]
    public void Build_EmptyTable_YieldsNoRows()
    {
        var dt = Table(0, "FItemRow");
        var res = DataTablePivotEngine.Build(dt, new[] { "Damage" });
        Assert.Empty(res.Rows);
        Assert.Equal(0, res.GroupCount);
    }
}
