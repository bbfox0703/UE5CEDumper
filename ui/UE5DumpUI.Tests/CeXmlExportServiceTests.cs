using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

public class CeXmlExportServiceTests
{
    // ========================================
    // CleanBreadcrumbs tests
    // ========================================

    [Fact]
    public void CleanBreadcrumbs_NoCycle_ReturnsSameList()
    {
        var breadcrumbs = new[]
        {
            MakeBc("0xA", "Root"),
            MakeBc("0xB", "Child1", "m_pChild1", isPointer: true),
            MakeBc("0xC", "Child2", "m_pChild2", isPointer: true),
        };

        var result = CeXmlExportService.CleanBreadcrumbs(breadcrumbs);

        Assert.Equal(3, result.Count);
        Assert.Equal("0xA", result[0].Address);
        Assert.Equal("0xB", result[1].Address);
        Assert.Equal("0xC", result[2].Address);
    }

    [Fact]
    public void CleanBreadcrumbs_SimpleCycle_RemovesLoop()
    {
        // A → B → C(parent) → A → B
        // Should become: A → B
        var breadcrumbs = new[]
        {
            MakeBc("0xA", "StatsComp"),
            MakeBc("0xB", "AttrSet", "m_pAttrSet", isPointer: true),
            MakeBc("0xC", "ParentActor", "Outer", isPointer: true),
            MakeBc("0xA", "StatsComp", "m_pStatsComp", isPointer: true),
            MakeBc("0xB", "AttrSet", "m_pAttrSet", isPointer: true),
        };

        var result = CeXmlExportService.CleanBreadcrumbs(breadcrumbs);

        Assert.Equal(2, result.Count);
        Assert.Equal("0xA", result[0].Address);
        Assert.Equal("0xB", result[1].Address);
    }

    [Fact]
    public void CleanBreadcrumbs_PartialCycle_KeepsPathAfterLoop()
    {
        // A → B → C(parent) → A → B → D (new destination)
        // Should become: A → B → D
        var breadcrumbs = new[]
        {
            MakeBc("0xA", "Root"),
            MakeBc("0xB", "Mid", "field1", isPointer: true),
            MakeBc("0xC", "Parent", "Outer", isPointer: true),
            MakeBc("0xA", "Root", "m_pRoot", isPointer: true),
            MakeBc("0xB", "Mid", "field1", isPointer: true),
            MakeBc("0xD", "Leaf", "field2", isPointer: true),
        };

        var result = CeXmlExportService.CleanBreadcrumbs(breadcrumbs);

        Assert.Equal(3, result.Count);
        Assert.Equal("0xA", result[0].Address);
        Assert.Equal("0xB", result[1].Address);
        Assert.Equal("0xD", result[2].Address);
    }

    [Fact]
    public void CleanBreadcrumbs_OnlyRoot_ReturnsSingle()
    {
        var breadcrumbs = new[] { MakeBc("0xA", "Root") };

        var result = CeXmlExportService.CleanBreadcrumbs(breadcrumbs);

        Assert.Single(result);
        Assert.Equal("0xA", result[0].Address);
    }

    [Fact]
    public void CleanBreadcrumbs_DoubleBackAndForth_RemovesBothCycles()
    {
        // A → B → A → B → A → B
        // First cycle: A@0 == A@2 → remove [1..2] → [A, B@3, A@4, B@5]
        // Second cycle: A@0 == A@4 → remove [1..4(now index 2)] → [A, B@5]
        var breadcrumbs = new[]
        {
            MakeBc("0xA", "Root"),
            MakeBc("0xB", "Child", "field", isPointer: true),
            MakeBc("0xA", "Root", "Outer", isPointer: true),
            MakeBc("0xB", "Child", "field", isPointer: true),
            MakeBc("0xA", "Root", "Outer", isPointer: true),
            MakeBc("0xB", "Child", "field", isPointer: true),
        };

        var result = CeXmlExportService.CleanBreadcrumbs(breadcrumbs);

        Assert.Equal(2, result.Count);
        Assert.Equal("0xA", result[0].Address);
        Assert.Equal("0xB", result[1].Address);
    }

    [Fact]
    public void CleanBreadcrumbs_CaseInsensitiveAddressMatch()
    {
        // Addresses might differ in case: "0xA" vs "0xa"
        var breadcrumbs = new[]
        {
            MakeBc("0xABC", "Root"),
            MakeBc("0xDEF", "Child", "field", isPointer: true),
            MakeBc("0xabc", "Root", "Outer", isPointer: true),
            MakeBc("0xdef", "Child", "field", isPointer: true),
        };

        var result = CeXmlExportService.CleanBreadcrumbs(breadcrumbs);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void CleanBreadcrumbs_OuterOnlyPath_NoChange()
    {
        // A → Parent(B) — no cycle, just upward navigation
        var breadcrumbs = new[]
        {
            MakeBc("0xA", "Child"),
            MakeBc("0xB", "Parent", "Outer", isPointer: true, offset: 0),
        };

        var result = CeXmlExportService.CleanBreadcrumbs(breadcrumbs);

        Assert.Equal(2, result.Count);
        Assert.Equal("0xA", result[0].Address);
        Assert.Equal("0xB", result[1].Address);
    }

    [Fact]
    public void CleanBreadcrumbs_RetainFieldInfoFromLastOccurrence()
    {
        // When cycle is removed, the entry AFTER the cycle retains its original
        // FieldName and FieldOffset — important for correct CE pointer chain
        var breadcrumbs = new[]
        {
            MakeBc("0xA", "Root"),
            MakeBc("0xB", "Child1", "m_pOriginal", isPointer: true, offset: 0x100),
            MakeBc("0xA", "Root", "Outer", isPointer: true, offset: 0),
            MakeBc("0xB", "Child1", "m_pRevisited", isPointer: true, offset: 0x200),
        };

        var result = CeXmlExportService.CleanBreadcrumbs(breadcrumbs);

        Assert.Equal(2, result.Count);
        Assert.Equal("0xA", result[0].Address);
        // The surviving entry should be the one AFTER the removed cycle (index 3 → now index 1)
        Assert.Equal("m_pRevisited", result[1].FieldName);
        Assert.Equal(0x200, result[1].FieldOffset);
    }

    // ========================================
    // GenerateHierarchicalXml integration tests
    // ========================================

    [Fact]
    public void GenerateHierarchicalXml_WithCycle_ProducesCleanXml()
    {
        // Simulate: stats_comp → attrSet → parent → stats_comp → attrSet
        // CE XML should only show one level of nesting (root + attrSet fields)
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "StatsComp"),
            MakeBc("0x2000", "AttrSet", "m_pAttrSet", isPointer: true, offset: 0x100),
            MakeBc("0x3000", "Actor", "Outer", isPointer: true, offset: 0),
            MakeBc("0x1000", "StatsComp", "m_pStatsComp", isPointer: true, offset: 0x840),
            MakeBc("0x2000", "AttrSet", "m_pAttrSet", isPointer: true, offset: 0x100),
        };

        var fields = new[]
        {
            new LiveFieldValue { Name = "BaseValue", TypeName = "FloatProperty", Offset = 0x20, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"TestGame.exe\"+1000", "StatsComp", breadcrumbs, fields);

        // Should NOT contain "Outer" in the XML (cycle removed)
        Assert.DoesNotContain("Outer", xml);
        // Should NOT contain "m_pStatsComp" (cycle removed)
        Assert.DoesNotContain("m_pStatsComp", xml);
        // Should contain the direct path: root → m_pAttrSet → BaseValue
        Assert.Contains("m_pAttrSet", xml);
        Assert.Contains("BaseValue", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_NoCycle_PreservesFullPath()
    {
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "Root"),
            MakeBc("0x2000", "Child", "m_pChild", isPointer: true, offset: 0x100),
        };

        var fields = new[]
        {
            new LiveFieldValue { Name = "Health", TypeName = "FloatProperty", Offset = 0x20, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"TestGame.exe\"+1000", "Root", breadcrumbs, fields);

        Assert.Contains("m_pChild", xml);
        Assert.Contains("Health", xml);
    }

    // ========================================
    // Helper
    // ========================================

    private static BreadcrumbItem MakeBc(string addr, string label,
        string fieldName = "", bool isPointer = false, int offset = 0)
    {
        return new BreadcrumbItem
        {
            Address = addr,
            Label = label,
            FieldName = string.IsNullOrEmpty(fieldName) ? label : fieldName,
            FieldOffset = offset,
            IsPointerDeref = isPointer,
        };
    }
}
