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
        // A -> B -> C(parent) -> A -> B
        // Should become: A -> B
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
        // A -> B -> C(parent) -> A -> B -> D (new destination)
        // Should become: A -> B -> D
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
        // A -> B -> A -> B -> A -> B
        // First cycle: A@0 == A@2 -> remove [1..2] -> [A, B@3, A@4, B@5]
        // Second cycle: A@0 == A@4 -> remove [1..4(now index 2)] -> [A, B@5]
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
        // A -> Parent(B) -- no cycle, just upward navigation
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
        // FieldName and FieldOffset -- important for correct CE pointer chain
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
        // The surviving entry should be the one AFTER the removed cycle (index 3 -> now index 1)
        Assert.Equal("m_pRevisited", result[1].FieldName);
        Assert.Equal(0x200, result[1].FieldOffset);
    }

    [Fact]
    public void CleanBreadcrumbs_ContainerView_NotRemovedAsCycle()
    {
        // Container breadcrumbs share parent's address (by design: they show TArray elements
        // of the same object). CleanBreadcrumbs must NOT remove them as cycles.
        // Path: A -> B(ptr) -> LocalPlayers(container, same addr as B) -> C(ptr, element)
        var breadcrumbs = new[]
        {
            MakeBc("0xA", "GWorld"),
            MakeBc("0xB", "OwningGameInstance", "OwningGameInstance", isPointer: true, offset: 0x1D8),
            MakeBc("0xB", "LocalPlayers [1 x ObjectProperty]", "LocalPlayers",
                isPointer: false, offset: 0x38, isContainerView: true),
            MakeBc("0xC", "[0] TQ2LocalPlayer", "[0]", isPointer: true, offset: 0),
        };

        var result = CeXmlExportService.CleanBreadcrumbs(breadcrumbs);

        // All 4 entries must survive — the container view is NOT a cycle
        Assert.Equal(4, result.Count);
        Assert.Equal("0xA", result[0].Address);
        Assert.Equal("0xB", result[1].Address);
        Assert.True(result[2].IsContainerView);
        Assert.Equal("0xC", result[3].Address);
    }

    [Fact]
    public void CleanBreadcrumbs_ContainerView_RealCycleStillRemoved()
    {
        // A real cycle after a container view is still removed.
        // Path: A -> Container(A) -> B -> A (cycle back to A)
        var breadcrumbs = new[]
        {
            MakeBc("0xA", "Root"),
            MakeBc("0xA", "Items [3 x ObjectProperty]", "Items",
                isPointer: false, offset: 0x50, isContainerView: true),
            MakeBc("0xB", "[0] SomeObj", "[0]", isPointer: true, offset: 0),
            MakeBc("0xA", "Root", "Outer", isPointer: true, offset: 0),
        };

        var result = CeXmlExportService.CleanBreadcrumbs(breadcrumbs);

        // Cycle A@0 == A@3 → remove [1..3] → only root remains
        // (But container at [1] is skipped, so we compare A@0 with A@3 → remove [1..3])
        Assert.Equal(1, result.Count);
        Assert.Equal("0xA", result[0].Address);
    }

    // ========================================
    // GenerateHierarchicalXml integration tests
    // ========================================

    [Fact]
    public void GenerateHierarchicalXml_WithCycle_ProducesCleanXml()
    {
        // Simulate: stats_comp -> attrSet -> parent -> stats_comp -> attrSet
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
        // Should contain the direct path: root -> m_pAttrSet -> BaseValue
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
    // Resolved struct expansion tests
    // ========================================

    [Fact]
    public void GenerateInstanceXml_WithResolvedStruct_EmitsRealFieldNames()
    {
        var fields = new[]
        {
            new LiveFieldValue { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4 },
            new LiveFieldValue
            {
                Name = "Attributes", TypeName = "StructProperty", Offset = 0x20, Size = 16,
                StructDataAddr = "0xABC", StructClassAddr = "0xDEF", StructTypeName = "FGameplayAttributeData"
            },
        };

        var resolvedStructs = new Dictionary<int, List<LiveFieldValue>>
        {
            [0x20] = new()
            {
                new LiveFieldValue { Name = "BaseValue", TypeName = "FloatProperty", Offset = 0x8, Size = 4 },
                new LiveFieldValue { Name = "CurrentValue", TypeName = "FloatProperty", Offset = 0xC, Size = 4 },
            }
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, resolvedStructs);

        // Real field names should appear instead of #1, #2
        Assert.Contains("BaseValue", xml);
        Assert.Contains("CurrentValue", xml);
        Assert.DoesNotContain("#1", xml);
        Assert.DoesNotContain("#2", xml);
        // Struct type name should appear in the group description
        Assert.Contains("FGameplayAttributeData", xml);
        // Scalar field still works
        Assert.Contains("Health", xml);
    }

    [Fact]
    public void GenerateInstanceXml_WithNestedStruct_FlattenedFields()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Transform", TypeName = "StructProperty", Offset = 0x100, Size = 0x30,
                StructDataAddr = "0xABC", StructClassAddr = "0xDEF", StructTypeName = "FTransform"
            },
        };

        // Flattened: FTransform has Location (FVector) with X, Y, Z
        var resolvedStructs = new Dictionary<int, List<LiveFieldValue>>
        {
            [0x100] = new()
            {
                new LiveFieldValue { Name = "Location.X", TypeName = "FloatProperty", Offset = 0x0, Size = 4 },
                new LiveFieldValue { Name = "Location.Y", TypeName = "FloatProperty", Offset = 0x4, Size = 4 },
                new LiveFieldValue { Name = "Location.Z", TypeName = "FloatProperty", Offset = 0x8, Size = 4 },
                new LiveFieldValue { Name = "Scale", TypeName = "FloatProperty", Offset = 0x20, Size = 4 },
            }
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, resolvedStructs);

        // Nested struct fields should be flattened with dot-prefix
        Assert.Contains("Location.X", xml);
        Assert.Contains("Location.Y", xml);
        Assert.Contains("Location.Z", xml);
        Assert.Contains("Scale", xml);
        // All children should be Float type
        Assert.Contains("<VariableType>Float</VariableType>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_StructWithBoolBitfield_EmitsBinaryType()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Flags", TypeName = "StructProperty", Offset = 0x50, Size = 4,
                StructDataAddr = "0xABC", StructClassAddr = "0xDEF", StructTypeName = "FFlags"
            },
        };

        var resolvedStructs = new Dictionary<int, List<LiveFieldValue>>
        {
            [0x50] = new()
            {
                new LiveFieldValue { Name = "bIsActive", TypeName = "BoolProperty", Offset = 0x0, Size = 1, BoolBitIndex = 0 },
                new LiveFieldValue { Name = "bIsVisible", TypeName = "BoolProperty", Offset = 0x0, Size = 1, BoolBitIndex = 1 },
                new LiveFieldValue { Name = "Count", TypeName = "ByteProperty", Offset = 0x1, Size = 1 },
            }
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, resolvedStructs);

        Assert.Contains("bIsActive", xml);
        Assert.Contains("bIsVisible", xml);
        Assert.Contains("Count", xml);
        Assert.Contains("<VariableType>Binary</VariableType>", xml);
        Assert.Contains("<BitStart>0</BitStart>", xml);
        Assert.Contains("<BitStart>1</BitStart>", xml);
        Assert.Contains("<VariableType>Byte</VariableType>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_StructWithPointer_EmitsPlaceholder()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Data", TypeName = "StructProperty", Offset = 0x30, Size = 16,
                StructDataAddr = "0xABC", StructClassAddr = "0xDEF", StructTypeName = "FData"
            },
        };

        var resolvedStructs = new Dictionary<int, List<LiveFieldValue>>
        {
            [0x30] = new()
            {
                new LiveFieldValue { Name = "Value", TypeName = "IntProperty", Offset = 0x0, Size = 4 },
                new LiveFieldValue
                {
                    Name = "Owner", TypeName = "ObjectProperty", Offset = 0x8, Size = 8,
                    PtrAddress = "0x999", PtrName = "SomeObj", PtrClassName = "UObj"
                },
            }
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, resolvedStructs);

        Assert.Contains("Value", xml);
        Assert.Contains("Owner", xml);
        // Pointer should have ShowAsHex
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_NoResolvedStruct_FallsBackToPlaceholder()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "UnresolvableStruct", TypeName = "StructProperty", Offset = 0x40, Size = 8,
                StructDataAddr = "0xABC", StructClassAddr = "0xDEF", StructTypeName = "FUnknown"
            },
        };

        // No resolved structs provided
        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Should fall back to GroupPlaceholder
        Assert.Contains("UnresolvableStruct", xml);
        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_WithResolvedStruct_UnderPointerParent()
    {
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "Root"),
            MakeBc("0x2000", "Child", "m_pChild", isPointer: true, offset: 0x100),
        };

        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Stats", TypeName = "StructProperty", Offset = 0x20, Size = 16,
                StructDataAddr = "0xABC", StructClassAddr = "0xDEF", StructTypeName = "FStats"
            },
        };

        var resolvedStructs = new Dictionary<int, List<LiveFieldValue>>
        {
            [0x20] = new()
            {
                new LiveFieldValue { Name = "HP", TypeName = "FloatProperty", Offset = 0x0, Size = 4 },
                new LiveFieldValue { Name = "MP", TypeName = "FloatProperty", Offset = 0x4, Size = 4 },
            }
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"Game.exe\"+1000", "Root", breadcrumbs, fields, resolvedStructs);

        // Breadcrumb m_pChild: Address=+100, Offsets=[0] (pointer dereference)
        Assert.Contains("<Address>+100</Address>", xml);
        Assert.Contains("<Offset>0</Offset>", xml);
        // Struct Stats: Address=+20 (inline, no additional dereference)
        Assert.Contains("<Address>+20</Address>", xml);
        Assert.Contains("FStats", xml);
        // Struct children: HP at +0, MP at +4 (relative to struct start)
        Assert.Contains("HP", xml);
        Assert.Contains("MP", xml);
    }

    // ========================================
    // ArrayProperty CE XML tests (Phase C)
    // ========================================

    [Fact]
    public void GenerateInstanceXml_ScalarArray_EmitsGroupWithElements()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "DamageMultipliers", TypeName = "ArrayProperty", Offset = 0x100, Size = 16,
                ArrayCount = 3, ArrayInnerType = "FloatProperty", ArrayElemSize = 4,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "1.5", Hex = "3FC00000" },
                    new() { Index = 1, Value = "2.0", Hex = "40000000" },
                    new() { Index = 2, Value = "0.5", Hex = "3F000000" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Group header with array description
        Assert.Contains("DamageMultipliers [3 x FloatProperty (4B)]", xml);
        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);
        // Array group: Address=+100, Offsets=[0] (dereference TArray.Data pointer)
        Assert.Contains("<Address>+100</Address>", xml);
        Assert.Contains("<Offset>0</Offset>", xml);
        // Element entries: simple offsets from deref'd Data pointer
        Assert.Contains("[0]", xml);
        Assert.Contains("[1]", xml);
        Assert.Contains("[2]", xml);
        Assert.Contains("<VariableType>Float</VariableType>", xml);
        // Element addresses: +0, +4, +8 (no Offsets, parent group already deref'd Data)
        Assert.Contains("<Address>+0</Address>", xml);
        Assert.Contains("<Address>+4</Address>", xml);
        Assert.Contains("<Address>+8</Address>", xml);
        // Elements should NOT have their own Offset entries (only group has Offset=0)
        Assert.DoesNotContain("<Offset>4</Offset>", xml);
        Assert.DoesNotContain("<Offset>8</Offset>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_EnumArrayWithoutEntries_FallbackDropDown()
    {
        // When ArrayEnumEntries is absent but elements have EnumName,
        // the fallback builds a DropDownList from element values
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "ShipTypes", TypeName = "ArrayProperty", Offset = 0x200, Size = 16,
                ArrayCount = 2, ArrayInnerType = "ByteProperty", ArrayElemSize = 1,
                // No ArrayEnumEntries → fallback builds DropDownList from element EnumName values
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "0", Hex = "00", EnumName = "EShip::Scout", RawIntValue = 0 },
                    new() { Index = 1, Value = "1", Hex = "01", EnumName = "EShip::SpecOps", RawIntValue = 1 },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Fallback DropDownList should be present with enum names
        Assert.Contains("<DropDownList DisplayValueAsItem=\"1\">", xml);
        Assert.Contains("0:EShip::Scout", xml);
        Assert.Contains("1:EShip::SpecOps", xml);
        Assert.Contains("<VariableType>Byte</VariableType>", xml);
        // Element descriptions simplified to [N] when DropDownList is active
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("\"[1]\"", xml);
        Assert.DoesNotContain("[0] EShip::Scout", xml);
    }

    [Fact]
    public void GenerateInstanceXml_EmptyArray_EmitsPlaceholder()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "EmptyArr", TypeName = "ArrayProperty", Offset = 0x50, Size = 16,
                ArrayCount = 0, ArrayInnerType = "FloatProperty", ArrayElemSize = 4,
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Should be a placeholder (GroupHeader, no CheatEntries children inside it)
        Assert.Contains("EmptyArr", xml);
        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);
        // No element entries (no [0], [1])
        Assert.DoesNotContain("[0]", xml);
    }

    [Fact]
    public void GenerateInstanceXml_NonScalarArray_EmitsPlaceholder()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Levels", TypeName = "ArrayProperty", Offset = 0x80, Size = 16,
                ArrayCount = 5, ArrayInnerType = "ObjectProperty", ArrayElemSize = 8,
                ArrayStructType = "",
                // No ArrayElements (ObjectProperty is non-scalar, Phase B skips it)
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Placeholder with type info in description
        Assert.Contains("Levels [5 x ObjectProperty (8B)]", xml);
        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);
        // No element entries
        Assert.DoesNotContain("[0]", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_ArrayUnderPointer_CorrectChain()
    {
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "Root"),
            MakeBc("0x2000", "Child", "m_pChild", isPointer: true, offset: 0x100),
        };

        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Scores", TypeName = "ArrayProperty", Offset = 0x30, Size = 16,
                ArrayCount = 2, ArrayInnerType = "IntProperty", ArrayElemSize = 4,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "100", Hex = "64000000" },
                    new() { Index = 1, Value = "200", Hex = "C8000000" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"Game.exe\"+1000", "Root", breadcrumbs, fields);

        // Breadcrumb m_pChild: Address=+100, Offsets=[0] (pointer dereference)
        Assert.Contains("<Address>+100</Address>", xml);
        // Array group Scores: Address=+30, Offsets=[0] (TArray.Data dereference)
        Assert.Contains("Scores [2 x IntProperty (4B)]", xml);
        Assert.Contains("<Address>+30</Address>", xml);
        Assert.Contains("<Offset>0</Offset>", xml);
        Assert.Contains("<VariableType>4 Bytes</VariableType>", xml);
        // Elements: simple offsets from deref'd Data pointer (no Offsets)
        Assert.Contains("[0]", xml);
        Assert.Contains("[1]", xml);
        // No double deref — each layer handles its own dereference
        Assert.DoesNotContain("<Offset>30</Offset>", xml);
        Assert.DoesNotContain("<Offset>4</Offset>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_LargeArray_CappedByInlineElements()
    {
        // ArrayCount=100 but only 3 inline elements (Phase B caps at 64, test with 3)
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "BigArray", TypeName = "ArrayProperty", Offset = 0x40, Size = 16,
                ArrayCount = 100, ArrayInnerType = "FloatProperty", ArrayElemSize = 4,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "1.0", Hex = "3F800000" },
                    new() { Index = 1, Value = "2.0", Hex = "40000000" },
                    new() { Index = 2, Value = "3.0", Hex = "40400000" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Header shows full count (100)
        Assert.Contains("BigArray [100 x FloatProperty (4B)]", xml);
        // Only 3 element entries (capped by inline data)
        Assert.Contains("[0]", xml);
        Assert.Contains("[1]", xml);
        Assert.Contains("[2]", xml);
        Assert.DoesNotContain("[3]", xml);
    }

    // ========================================
    // Pointer Array CE XML tests (Phase D)
    // ========================================

    [Fact]
    public void GenerateInstanceXml_PointerArray_EmitsGroupWithElements()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Levels", TypeName = "ArrayProperty", Offset = 0x80, Size = 16,
                ArrayCount = 3, ArrayInnerType = "ObjectProperty", ArrayElemSize = 8,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "PersistentLevel (Level)", Hex = "0000020C12340000",
                        PtrAddress = "0x20C12340000", PtrName = "PersistentLevel", PtrClassName = "Level" },
                    new() { Index = 1, Value = "SubLevel_01 (Level)", Hex = "0000020C56780000",
                        PtrAddress = "0x20C56780000", PtrName = "SubLevel_01", PtrClassName = "Level" },
                    new() { Index = 2, Value = "null", Hex = "0000000000000000",
                        PtrAddress = "", PtrName = "", PtrClassName = "" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Group header with array type info
        Assert.Contains("Levels [3 x ObjectProperty (8B)]", xml);
        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);
        // Array group: Address=+80, Offsets=[0] (deref TArray.Data)
        Assert.Contains("<Address>+80</Address>", xml);
        Assert.Contains("<Offset>0</Offset>", xml);
        // Elements with resolved names in descriptions
        Assert.Contains("[0] PersistentLevel (Level)", xml);
        Assert.Contains("[1] SubLevel_01 (Level)", xml);
        Assert.Contains("[2]", xml); // null element, no name
        // Pointer type: 8 Bytes, ShowAsHex
        Assert.Contains("<VariableType>8 Bytes</VariableType>", xml);
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", xml);
        // Element offsets: +0, +8, +10 (8 bytes per pointer)
        Assert.Contains("<Address>+0</Address>", xml);
        Assert.Contains("<Address>+8</Address>", xml);
        Assert.Contains("<Address>+10</Address>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_PointerArrayNoElements_StillPlaceholder()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "BigPtrArr", TypeName = "ArrayProperty", Offset = 0xA0, Size = 16,
                ArrayCount = 200, ArrayInnerType = "ObjectProperty", ArrayElemSize = 8,
                // No ArrayElements — exceeds 64-element cap
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Placeholder with type info
        Assert.Contains("BigPtrArr [200 x ObjectProperty (8B)]", xml);
        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);
        Assert.DoesNotContain("[0]", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_PointerArrayUnderPointerBreadcrumb()
    {
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "Root"),
            MakeBc("0x2000", "Child", "m_pChild", isPointer: true, offset: 0x100),
        };

        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Components", TypeName = "ArrayProperty", Offset = 0x50, Size = 16,
                ArrayCount = 2, ArrayInnerType = "ObjectProperty", ArrayElemSize = 8,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "MeshComp (StaticMeshComponent)",
                        Hex = "0000020CABC00000",
                        PtrAddress = "0x20CABC00000", PtrName = "MeshComp",
                        PtrClassName = "StaticMeshComponent" },
                    new() { Index = 1, Value = "CollisionComp (BoxComponent)",
                        Hex = "0000020CDEF00000",
                        PtrAddress = "0x20CDEF00000", PtrName = "CollisionComp",
                        PtrClassName = "BoxComponent" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"Game.exe\"+1000", "Root", breadcrumbs, fields);

        // Breadcrumb m_pChild: pointer dereference
        Assert.Contains("<Address>+100</Address>", xml);
        // Array group
        Assert.Contains("Components [2 x ObjectProperty (8B)]", xml);
        Assert.Contains("<Address>+50</Address>", xml);
        // Elements
        Assert.Contains("[0] MeshComp (StaticMeshComponent)", xml);
        Assert.Contains("[1] CollisionComp (BoxComponent)", xml);
        Assert.Contains("<VariableType>8 Bytes</VariableType>", xml);
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_WeakObjectArray_EmitsGroupWithElements()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "WeakRefs", TypeName = "ArrayProperty", Offset = 0x90, Size = 16,
                ArrayCount = 2, ArrayInnerType = "WeakObjectProperty", ArrayElemSize = 8,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "PlayerChar (Character)", Hex = "0000004200000003",
                        PtrAddress = "0x20C11110000", PtrName = "PlayerChar", PtrClassName = "Character" },
                    new() { Index = 1, Value = "null (stale)", Hex = "0000002500000007",
                        PtrAddress = "", PtrName = "", PtrClassName = "" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Group header with weak object array type info
        Assert.Contains("WeakRefs [2 x WeakObjectProperty (8B)]", xml);
        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);
        // Array group: Address=+90, Offsets=[0] (deref TArray.Data)
        Assert.Contains("<Address>+90</Address>", xml);
        Assert.Contains("<Offset>0</Offset>", xml);
        // Elements: resolved name in description
        Assert.Contains("[0] PlayerChar (Character)", xml);
        Assert.Contains("[1]", xml); // stale, no name
        // WeakObjectProperty: 8 Bytes, ShowAsHex
        Assert.Contains("<VariableType>8 Bytes</VariableType>", xml);
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", xml);
        // Element offsets: +0, +8 (8 bytes per weak ptr)
        Assert.Contains("<Address>+0</Address>", xml);
        Assert.Contains("<Address>+8</Address>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_StructArray_EmitsPerElementGroup()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Positions", TypeName = "ArrayProperty", Offset = 0x60, Size = 16,
                ArrayCount = 2, ArrayInnerType = "StructProperty", ArrayStructType = "Vector",
                ArrayElemSize = 12,
                ArrayElements = new List<ArrayElementValue>
                {
                    new()
                    {
                        Index = 0, Value = "{X=100.0, Y=200.0, Z=0.0}", Hex = "0000C84200004843",
                        StructFields = new List<StructSubFieldValue>
                        {
                            new() { Name = "X", TypeName = "FloatProperty", Offset = 0, Size = 4, Value = "100.0" },
                            new() { Name = "Y", TypeName = "FloatProperty", Offset = 4, Size = 4, Value = "200.0" },
                            new() { Name = "Z", TypeName = "FloatProperty", Offset = 8, Size = 4, Value = "0.0" },
                        }
                    },
                    new()
                    {
                        Index = 1, Value = "{X=50.0, Y=-10.0, Z=300.0}", Hex = "00004842000020C1",
                        StructFields = new List<StructSubFieldValue>
                        {
                            new() { Name = "X", TypeName = "FloatProperty", Offset = 0, Size = 4, Value = "50.0" },
                            new() { Name = "Y", TypeName = "FloatProperty", Offset = 4, Size = 4, Value = "-10.0" },
                            new() { Name = "Z", TypeName = "FloatProperty", Offset = 8, Size = 4, Value = "300.0" },
                        }
                    },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Array group header with struct type info
        Assert.Contains("Positions [2 x Vector (12B)]", xml);
        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);
        // Array group: Address=+60, Offsets=[0] (deref TArray.Data)
        Assert.Contains("<Address>+60</Address>", xml);
        Assert.Contains("<Offset>0</Offset>", xml);
        // Element [0] group at offset +0
        Assert.Contains("[0]", xml);
        Assert.Contains("<Address>+0</Address>", xml);
        // Element [1] group at offset +C (12 bytes)
        Assert.Contains("[1]", xml);
        Assert.Contains("<Address>+C</Address>", xml);
        // Sub-fields: Float type leaves
        Assert.Contains("<VariableType>Float</VariableType>", xml);
        // Sub-field names
        Assert.Contains("X", xml);
        Assert.Contains("Y", xml);
        Assert.Contains("Z", xml);
        // Sub-field offsets within element: +0, +4, +8
        Assert.Contains("<Address>+4</Address>", xml);
        Assert.Contains("<Address>+8</Address>", xml);
    }

    // ========================================
    // CE DropDownList tests
    // ========================================

    [Fact]
    public void GenerateInstanceXml_EnumArray_EmitsDropDownList()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "ShipTypes", TypeName = "ArrayProperty", Offset = 0x200, Size = 16,
                ArrayCount = 3, ArrayInnerType = "ByteProperty", ArrayElemSize = 1,
                ArrayEnumAddr = "0x1234",
                ArrayEnumEntries = new List<EnumEntryValue>
                {
                    new() { Value = 0, Name = "EShip::Scout" },
                    new() { Value = 1, Name = "EShip::SpecOps" },
                    new() { Value = 2, Name = "EShip::Gunship" },
                },
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "EShip::Scout", Hex = "00", EnumName = "EShip::Scout", RawIntValue = 0 },
                    new() { Index = 1, Value = "EShip::SpecOps", Hex = "01", EnumName = "EShip::SpecOps", RawIntValue = 1 },
                    new() { Index = 2, Value = "EShip::Gunship", Hex = "02", EnumName = "EShip::Gunship", RawIntValue = 2 },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // DropDownList on parent GroupHeader (not on first child)
        Assert.Contains("<DropDownList DisplayValueAsItem=\"1\">", xml);
        Assert.Contains("0:EShip::Scout", xml);
        Assert.Contains("1:EShip::SpecOps", xml);
        Assert.Contains("2:EShip::Gunship", xml);
        // Parent description contains the array info
        Assert.Contains("ShipTypes [3 x ByteProperty (1B)]", xml);
        // All children use DropDownListLink (element content, not attribute) referencing parent
        Assert.Contains("<DropDownListLink>ShipTypes [3 x ByteProperty (1B)]</DropDownListLink>", xml);
        // Child descriptions are simplified to [N] only (no enum names)
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("\"[1]\"", xml);
        Assert.Contains("\"[2]\"", xml);
        // Enum names should NOT appear in child descriptions
        Assert.DoesNotContain("[0] EShip::Scout", xml);
    }

    [Fact]
    public void GenerateInstanceXml_SharedEnumArray_EmitsDropDownListLink()
    {
        // Two arrays with the same enum type (same ArrayEnumAddr)
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "StarterShips", TypeName = "ArrayProperty", Offset = 0x100, Size = 16,
                ArrayCount = 2, ArrayInnerType = "ByteProperty", ArrayElemSize = 1,
                ArrayEnumAddr = "0xABCD",
                ArrayEnumEntries = new List<EnumEntryValue>
                {
                    new() { Value = 0, Name = "EShip::Scout" },
                    new() { Value = 1, Name = "EShip::SpecOps" },
                },
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "EShip::Scout", Hex = "00", EnumName = "EShip::Scout", RawIntValue = 0 },
                    new() { Index = 1, Value = "EShip::SpecOps", Hex = "01", EnumName = "EShip::SpecOps", RawIntValue = 1 },
                }
            },
            new LiveFieldValue
            {
                Name = "AvailableShips", TypeName = "ArrayProperty", Offset = 0x200, Size = 16,
                ArrayCount = 1, ArrayInnerType = "ByteProperty", ArrayElemSize = 1,
                ArrayEnumAddr = "0xABCD",  // Same enum type as above
                ArrayEnumEntries = new List<EnumEntryValue>
                {
                    new() { Value = 0, Name = "EShip::Scout" },
                    new() { Value = 1, Name = "EShip::SpecOps" },
                },
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "EShip::SpecOps", Hex = "01", EnumName = "EShip::SpecOps", RawIntValue = 1 },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // First array's parent GroupHeader has the DropDownList
        Assert.Contains("<DropDownList DisplayValueAsItem=\"1\">", xml);
        // Exactly 1 DropDownList (on first array's parent only)
        int listCount = CountOccurrences(xml, "<DropDownList ");
        Assert.Equal(1, listCount);
        // Second array's parent uses DropDownListLink to first array's parent Description
        var firstParentDesc = "StarterShips [2 x ByteProperty (1B)]";
        Assert.Contains($"<DropDownListLink>{firstParentDesc}</DropDownListLink>", xml);
        // All children from both arrays use DropDownListLink referencing first parent
        int linkCount = CountOccurrences(xml, $"<DropDownListLink>{firstParentDesc}</DropDownListLink>");
        // 2 children from first array + 1 parent from second array + 1 child from second array = 4
        Assert.True(linkCount >= 4, $"Expected at least 4 DropDownListLink entries, got {linkCount}");
    }

    [Fact]
    public void GenerateInstanceXml_NameArray_EmitsDropDownList()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Locations", TypeName = "ArrayProperty", Offset = 0x80, Size = 16,
                ArrayCount = 3, ArrayInnerType = "NameProperty", ArrayElemSize = 8,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "S01L04", Hex = "12340000", RawIntValue = 0x1234 },
                    new() { Index = 1, Value = "S01L08", Hex = "56780000", RawIntValue = 0x5678 },
                    new() { Index = 2, Value = "S02L01", Hex = "9ABC0000", RawIntValue = 0x9ABC },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // DropDownList on parent GroupHeader (not on first child)
        Assert.Contains("<DropDownList DisplayValueAsItem=\"1\">", xml);
        Assert.Contains("4660:S01L04", xml);   // 0x1234 = 4660 decimal
        Assert.Contains("22136:S01L08", xml);   // 0x5678 = 22136 decimal
        Assert.Contains("39612:S02L01", xml);   // 0x9ABC = 39612 decimal
        // Parent description
        var parentDesc = "Locations [3 x NameProperty (8B)]";
        Assert.Contains(parentDesc, xml);
        // All children use DropDownListLink (element content) referencing parent
        Assert.Contains($"<DropDownListLink>{parentDesc}</DropDownListLink>", xml);
        // Child descriptions are simplified to [N] only (no name values)
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("\"[1]\"", xml);
        Assert.Contains("\"[2]\"", xml);
        // Name values should NOT appear in child descriptions
        Assert.DoesNotContain("[0] S01L04", xml);
    }

    [Fact]
    public void GenerateInstanceXml_DuplicateDescEnumArray_AppendsSuffix()
    {
        // Two NameProperty arrays with the SAME description (same name, count, type)
        // CE uses Description as DropDownListLink key, so duplicates need .001 suffix
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Tags", TypeName = "ArrayProperty", Offset = 0x100, Size = 16,
                ArrayCount = 2, ArrayInnerType = "NameProperty", ArrayElemSize = 8,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "TagA", Hex = "11110000", RawIntValue = 0x1111 },
                    new() { Index = 1, Value = "TagB", Hex = "22220000", RawIntValue = 0x2222 },
                }
            },
            new LiveFieldValue
            {
                Name = "Tags", TypeName = "ArrayProperty", Offset = 0x200, Size = 16,
                ArrayCount = 2, ArrayInnerType = "NameProperty", ArrayElemSize = 8,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "TagC", Hex = "33330000", RawIntValue = 0x3333 },
                    new() { Index = 1, Value = "TagD", Hex = "44440000", RawIntValue = 0x4444 },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Exactly 2 DropDownList entries (each NameProperty array gets its own, no sharing)
        int listCount = CountOccurrences(xml, "<DropDownList ");
        Assert.Equal(2, listCount);
        // First array: original description
        Assert.Contains("\"Tags [2 x NameProperty (8B)]\"", xml);
        // Second array: suffixed description for uniqueness
        Assert.Contains("\"Tags [2 x NameProperty (8B)].001\"", xml);
        // Children of second array link to the suffixed parent
        Assert.Contains("<DropDownListLink>Tags [2 x NameProperty (8B)].001</DropDownListLink>", xml);
    }

    // ========================================
    // Struct containing ArrayProperty tests
    // ========================================

    [Fact]
    public void GenerateInstanceXml_ResolvedStructWithArray_ExpandsArrayElements()
    {
        // Simulates: StructProperty "JobScore" containing ArrayProperty "ClaimedRewards" with elements
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "JobScore", TypeName = "StructProperty", Offset = 0x100, Size = 32,
                StructDataAddr = "0xABC", StructClassAddr = "0xDEF", StructTypeName = "FJobScore"
            },
        };

        // Resolved struct has a scalar field + an ArrayProperty with inline elements
        var resolvedStructs = new Dictionary<int, List<LiveFieldValue>>
        {
            [0x100] = new()
            {
                new LiveFieldValue { Name = "CurrentRank", TypeName = "IntProperty", Offset = 0x0, Size = 4 },
                new LiveFieldValue
                {
                    Name = "ClaimedRewards", TypeName = "ArrayProperty", Offset = 0x8, Size = 16,
                    ArrayCount = 3, ArrayInnerType = "IntProperty", ArrayElemSize = 4,
                    ArrayElements = new List<ArrayElementValue>
                    {
                        new() { Index = 0, Value = "10", Hex = "0A000000" },
                        new() { Index = 1, Value = "20", Hex = "14000000" },
                        new() { Index = 2, Value = "30", Hex = "1E000000" },
                    }
                },
            }
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, resolvedStructs);

        // Struct group should exist
        Assert.Contains("FJobScore", xml);
        Assert.Contains("CurrentRank", xml);
        // Array inside struct should be fully expanded (NOT a placeholder)
        Assert.Contains("ClaimedRewards [3 x IntProperty (4B)]", xml);
        // Array elements should be present
        Assert.Contains("[0]", xml);
        Assert.Contains("[1]", xml);
        Assert.Contains("[2]", xml);
        // Array group should have Offsets=[0] for TArray.Data deref
        Assert.Contains("<Offset>0</Offset>", xml);
        // Struct scalar field
        Assert.Contains("<VariableType>4 Bytes</VariableType>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_ResolvedStructWithEnumArray_EmitsDropDownFallback()
    {
        // Simulates: StructProperty containing ByteProperty/EnumProperty array where
        // ArrayEnumEntries is empty but elements have EnumName set (fallback path)
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Settings", TypeName = "StructProperty", Offset = 0x50, Size = 32,
                StructDataAddr = "0xABC", StructClassAddr = "0xDEF", StructTypeName = "FSettings"
            },
        };

        var resolvedStructs = new Dictionary<int, List<LiveFieldValue>>
        {
            [0x50] = new()
            {
                new LiveFieldValue
                {
                    Name = "Modes", TypeName = "ArrayProperty", Offset = 0x0, Size = 16,
                    ArrayCount = 2, ArrayInnerType = "ByteProperty", ArrayElemSize = 1,
                    // No ArrayEnumEntries (empty/null) — triggers fallback path
                    ArrayElements = new List<ArrayElementValue>
                    {
                        new() { Index = 0, Value = "0", Hex = "00", EnumName = "EMode::Easy", RawIntValue = 0 },
                        new() { Index = 1, Value = "1", Hex = "01", EnumName = "EMode::Hard", RawIntValue = 1 },
                    }
                },
            }
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, resolvedStructs);

        // Struct group exists
        Assert.Contains("FSettings", xml);
        // Array should have DropDownList from element enum names (fallback)
        Assert.Contains("<DropDownList DisplayValueAsItem=\"1\">", xml);
        Assert.Contains("0:EMode::Easy", xml);
        Assert.Contains("1:EMode::Hard", xml);
        // Element descriptions simplified to [N]
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("\"[1]\"", xml);
        // Enum names should NOT be in element descriptions when DropDownList is active
        Assert.DoesNotContain("[0] EMode::Easy", xml);
    }

    [Fact]
    public void GenerateInstanceXml_EnumFallbackDropDown_BuildsFromElementValues()
    {
        // Direct (non-struct) array with no ArrayEnumEntries but elements have EnumName
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Ranks", TypeName = "ArrayProperty", Offset = 0x80, Size = 16,
                ArrayCount = 3, ArrayInnerType = "EnumProperty", ArrayElemSize = 4,
                // ArrayEnumEntries is null → isEnumArray=false
                // But elements have EnumName → isEnumFallback=true
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "EJobRank::Rank1", Hex = "00000000", EnumName = "EJobRank::Rank1", RawIntValue = 0 },
                    new() { Index = 1, Value = "EJobRank::Rank2", Hex = "01000000", EnumName = "EJobRank::Rank2", RawIntValue = 1 },
                    new() { Index = 2, Value = "EJobRank::Rank1", Hex = "00000000", EnumName = "EJobRank::Rank1", RawIntValue = 0 },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // DropDownList should be present (fallback from element names)
        Assert.Contains("<DropDownList DisplayValueAsItem=\"1\">", xml);
        Assert.Contains("0:EJobRank::Rank1", xml);
        Assert.Contains("1:EJobRank::Rank2", xml);
        // Deduplicated: Rank1 appears twice in elements but only once in DropDownList
        int rank1Count = CountOccurrences(xml, "0:EJobRank::Rank1");
        Assert.Equal(1, rank1Count);
        // All children use DropDownListLink
        Assert.Contains("<DropDownListLink>", xml);
        // Element descriptions are simplified [N]
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("\"[1]\"", xml);
        Assert.Contains("\"[2]\"", xml);
    }

    // ========================================
    // Collapse pointer nodes tests
    // ========================================

    [Fact]
    public void GenerateInstanceXml_CollapsePointerNodes_EmitsOptionsOnPointerAndArrayGroups()
    {
        // Arrange: one scalar field + one array with elements (has Offsets=[0] → gets Options)
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4 },
            new()
            {
                Name = "Scores", TypeName = "ArrayProperty", Offset = 0x20, Size = 16,
                ArrayCount = 2, ArrayInnerType = "IntProperty", ArrayElemSize = 4,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "100" },
                    new() { Index = 1, Value = "200" },
                }
            },
        };

        // Act
        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"game.exe\"+1234", "MyObj", "MyClass", fields,
            collapsePointerNodes: true);

        // Assert: root node does NOT have Options (absolute address, no +prefix)
        // Array group (Offsets=[0], address starts with +) DOES have Options
        var optionsTag = "<Options moHideChildren=\"1\" moDeactivateChildrenAsWell=\"1\"/>";
        int optionsCount = CountOccurrences(xml, optionsTag);
        Assert.Equal(1, optionsCount); // Only the array group, not the root

        // Verify root doesn't have it
        var rootIdx = xml.IndexOf("\"MyClass: MyObj\"", StringComparison.Ordinal);
        var rootEnd = xml.IndexOf("</CheatEntry>", rootIdx, StringComparison.Ordinal);
        // The first Options should come AFTER the root's GroupHeader, inside the array group
        var firstOptionsIdx = xml.IndexOf(optionsTag, StringComparison.Ordinal);
        Assert.True(firstOptionsIdx > rootIdx);
    }

    [Fact]
    public void GenerateInstanceXml_NoCollapse_NoOptionsEmitted()
    {
        // Arrange: same array field
        var fields = new List<LiveFieldValue>
        {
            new()
            {
                Name = "Scores", TypeName = "ArrayProperty", Offset = 0x20, Size = 16,
                ArrayCount = 2, ArrayInnerType = "IntProperty", ArrayElemSize = 4,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "100" },
                    new() { Index = 1, Value = "200" },
                }
            },
        };

        // Act: collapse OFF (default)
        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"game.exe\"+1234", "MyObj", "MyClass", fields,
            collapsePointerNodes: false);

        // Assert: no Options elements at all
        Assert.DoesNotContain("<Options", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_CollapsePointerNodes_EmitsOptionsOnBreadcrumbPointers()
    {
        // Arrange: breadcrumb with pointer navigation (intermediate level)
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "Root"),
            MakeBc("0x2000", "Child", "m_pChild", isPointer: true, offset: 0x50),
        };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Value", TypeName = "IntProperty", Offset = 0x10, Size = 4 },
        };

        // Act
        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+1000", "Root", breadcrumbs, fields,
            collapsePointerNodes: true);

        // Assert: breadcrumb pointer node gets Options, root does not
        var optionsTag = "<Options moHideChildren=\"1\" moDeactivateChildrenAsWell=\"1\"/>";
        int optionsCount = CountOccurrences(xml, optionsTag);
        Assert.Equal(1, optionsCount); // Only the pointer breadcrumb, not root
    }

    // ========================================
    // Single-field (Copy CE Field) tests
    // ========================================

    [Fact]
    public void GenerateHierarchicalXml_SingleScalarField_EmitsOnlyThatField()
    {
        var breadcrumbs = new[] { MakeBc("0x1000", "Root") };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+1000", "Root", breadcrumbs, fields);

        // Should contain exactly 2 CheatEntry nodes (root group + 1 leaf)
        Assert.Equal(2, CountOccurrences(xml, "<CheatEntry>"));
        Assert.Contains("\"Health\"", xml);
        Assert.Contains("<VariableType>Float</VariableType>", xml);
        Assert.Contains("<Address>+10</Address>", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_SingleArrayField_EmitsGroupWithElements()
    {
        var breadcrumbs = new[] { MakeBc("0x1000", "Root") };
        var fields = new List<LiveFieldValue>
        {
            new()
            {
                Name = "Scores", TypeName = "ArrayProperty", Offset = 0x20, Size = 16,
                ArrayCount = 3, ArrayInnerType = "IntProperty", ArrayElemSize = 4,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "10" },
                    new() { Index = 1, Value = "20" },
                    new() { Index = 2, Value = "30" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+1000", "Root", breadcrumbs, fields);

        // Root group + array group + 3 element leaves = 5 CheatEntry nodes
        Assert.Equal(5, CountOccurrences(xml, "<CheatEntry>"));
        Assert.Contains("Scores [3 x IntProperty (4B)]", xml);
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("\"[1]\"", xml);
        Assert.Contains("\"[2]\"", xml);
        // Array group should have Offsets=[0] for TArray.Data deref
        Assert.Contains("<Offset>0</Offset>", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_SingleFieldWithBreadcrumbs_PreservesPointerChain()
    {
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "Root"),
            MakeBc("0x2000", "Player", "m_pPlayer", isPointer: true, offset: 0x50),
        };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Level", TypeName = "IntProperty", Offset = 0x18, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+1000", "Root", breadcrumbs, fields);

        // Root group + breadcrumb pointer group + 1 leaf = 3 CheatEntry nodes
        Assert.Equal(3, CountOccurrences(xml, "<CheatEntry>"));
        Assert.Contains("\"Root\"", xml);
        Assert.Contains("\"m_pPlayer\"", xml);
        Assert.Contains("\"Level\"", xml);
        // Breadcrumb pointer should have offset and dereference
        Assert.Contains("<Address>+50</Address>", xml);
        Assert.Contains("<Address>+18</Address>", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_SingleBoolField_EmitsBitField()
    {
        var breadcrumbs = new[] { MakeBc("0x1000", "Root") };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "bIsAlive", TypeName = "BoolProperty", Offset = 0x30, Size = 1,
                     BoolBitIndex = 3, BoolFieldMask = 0x08 },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+1000", "Root", breadcrumbs, fields);

        Assert.Equal(2, CountOccurrences(xml, "<CheatEntry>"));
        Assert.Contains("\"bIsAlive\"", xml);
        Assert.Contains("<VariableType>Binary</VariableType>", xml);
        Assert.Contains("<BitStart>3</BitStart>", xml);
        Assert.Contains("<BitLength>1</BitLength>", xml);
    }

    // ========================================
    // MapProperty / SetProperty CE XML tests
    // ========================================

    [Fact]
    public void GenerateInstanceXml_ScalarMap_EmitsGroupWithKeyValueElements()
    {
        // Map<IntProperty, FloatProperty> with 2 allocated elements at sparse indices 0,3
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "ScoreMap", TypeName = "MapProperty", Offset = 0x100, Size = 0x50,
                MapCount = 2, MapKeyType = "IntProperty", MapValueType = "FloatProperty",
                MapKeySize = 4, MapValueSize = 4,
                MapElements = new List<ContainerElementValue>
                {
                    new() { Index = 0, Key = "42", Value = "3.14", KeyHex = "2A000000", ValueHex = "C3F54840" },
                    new() { Index = 3, Key = "99", Value = "2.71", KeyHex = "63000000", ValueHex = "AE472D40" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Map group should exist with description
        Assert.Contains("ScoreMap {Map: 2, IntProperty", xml);
        // Map group should have Offsets=[0] for TSparseArray.Data deref
        Assert.Contains("<Offset>0</Offset>", xml);
        // Stride = ComputeSetElementStride(4+4) = AlignUp(8,4)+8 = 16
        // Element at index 0: offset = 0*16 = 0
        Assert.Contains("<Address>+0</Address>", xml);
        // Element at index 3: offset = 3*16 = 48 = 0x30
        Assert.Contains("<Address>+30</Address>", xml);
        // Key at +0, Value at +keySize (4)
        Assert.Contains("Key: 42", xml);
        Assert.Contains("Value: 3.14", xml);
        Assert.Contains("Key: 99", xml);
        Assert.Contains("Value: 2.71", xml);
        // Key type is 4 Bytes (IntProperty), Value type is Float
        Assert.Contains("<VariableType>4 Bytes</VariableType>", xml);
        Assert.Contains("<VariableType>Float</VariableType>", xml);
        // Element descriptions include key values
        Assert.Contains("[0] 42", xml);
        Assert.Contains("[3] 99", xml);
    }

    [Fact]
    public void GenerateInstanceXml_EmptyMap_EmitsPlaceholder()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "EmptyMap", TypeName = "MapProperty", Offset = 0x80, Size = 0x50,
                MapCount = 0, MapKeyType = "IntProperty", MapValueType = "FloatProperty",
                MapKeySize = 4, MapValueSize = 4,
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Should be a placeholder (GroupHeader=1, no Offsets, no children)
        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);
        Assert.Contains("EmptyMap", xml);
        // No Offsets=[0] (not a pointer deref)
        Assert.DoesNotContain("<Offset>0</Offset>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_NonScalarMapKey_EmitsPlaceholder()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "StructMap", TypeName = "MapProperty", Offset = 0x80, Size = 0x50,
                MapCount = 3, MapKeyType = "StructProperty", MapValueType = "IntProperty",
                MapKeySize = 16, MapValueSize = 4,
                MapElements = new List<ContainerElementValue>
                {
                    new() { Index = 0, Key = "(struct)", Value = "10" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // StructProperty key is non-scalar → placeholder
        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);
        // No Offsets=[0] (not a pointer deref since it's a placeholder)
        Assert.DoesNotContain("<Offset>0</Offset>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_ScalarSet_EmitsGroupWithElements()
    {
        // Set<IntProperty> with 3 elements at sparse indices 0,1,5
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "TagSet", TypeName = "SetProperty", Offset = 0x100, Size = 0x50,
                SetCount = 3, SetElemType = "IntProperty", SetElemSize = 4,
                SetElements = new List<ContainerElementValue>
                {
                    new() { Index = 0, Key = "10", KeyHex = "0A000000" },
                    new() { Index = 1, Key = "20", KeyHex = "14000000" },
                    new() { Index = 5, Key = "50", KeyHex = "32000000" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Set group should exist
        Assert.Contains("TagSet {Set: 3, IntProperty}", xml);
        // Set group should have Offsets=[0] for TSparseArray.Data deref
        Assert.Contains("<Offset>0</Offset>", xml);
        // Stride = ComputeSetElementStride(4) = AlignUp(4,4)+8 = 12 = 0xC
        // Element at index 0: offset = 0*12 = 0
        Assert.Contains("<Address>+0</Address>", xml);
        // Element at index 1: offset = 1*12 = 12 = 0xC
        Assert.Contains("<Address>+C</Address>", xml);
        // Element at index 5: offset = 5*12 = 60 = 0x3C
        Assert.Contains("<Address>+3C</Address>", xml);
        // Element descriptions include values
        Assert.Contains("[0] 10", xml);
        Assert.Contains("[1] 20", xml);
        Assert.Contains("[5] 50", xml);
        // Element type is 4 Bytes (IntProperty)
        Assert.Contains("<VariableType>4 Bytes</VariableType>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_EmptySet_EmitsPlaceholder()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "EmptySet", TypeName = "SetProperty", Offset = 0x80, Size = 0x50,
                SetCount = 0, SetElemType = "IntProperty", SetElemSize = 4,
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);
        Assert.Contains("EmptySet", xml);
        Assert.DoesNotContain("<Offset>0</Offset>", xml);
    }

    // ========================================
    // Container View CE XML tests
    // (simulates the fix: container breadcrumb stripped, ContainerField as leaf)
    // ========================================

    [Fact]
    public void GenerateHierarchicalXml_MapContainerField_UnderPointerChain_EmitsCorrectStructure()
    {
        // Simulates: GWorld -> OwningGameInstance (+1D8, deref) -> PlayerData (+1C0, deref)
        // ContainerField: AttributeAugmentLevels (MapProperty, offset 0x358)
        // The container breadcrumb is NOT included (stripped by ViewModel fix).
        var breadcrumbs = new[]
        {
            MakeBc("0x1DF67CE0150", "GWorld"),
            MakeBc("0x1DEC856EDC0", "OwningGameInstance", "OwningGameInstance", isPointer: true, offset: 0x1D8),
            MakeBc("0x1DF847C24A0", "PlayerData", "PlayerData", isPointer: true, offset: 0x1C0),
        };
        var fields = new List<LiveFieldValue>
        {
            new()
            {
                Name = "AttributeAugmentLevels", TypeName = "MapProperty", Offset = 0x358, Size = 0x50,
                MapCount = 6, MapKeyType = "NameProperty", MapValueType = "IntProperty",
                MapKeySize = 8, MapValueSize = 4,
                MapElements = new List<ContainerElementValue>
                {
                    new() { Index = 0, Key = "firepower", Value = "5", KeyHex = "37F3000000000000" },
                    new() { Index = 3, Key = "resistance", Value = "481", KeyHex = "4EF3000000000000" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "1DF67CE0150", "GWorld", breadcrumbs, fields);

        // Verify full pointer chain is preserved
        Assert.Contains("\"GWorld\"", xml);
        Assert.Contains("\"OwningGameInstance\"", xml);
        Assert.Contains("<Address>+1D8</Address>", xml);
        Assert.Contains("\"PlayerData\"", xml);
        Assert.Contains("<Address>+1C0</Address>", xml);

        // Map group: offset 0x358 with Offsets=[0] for TSparseArray.Data deref
        Assert.Contains("AttributeAugmentLevels {Map: 6, NameProperty", xml);
        Assert.Contains("<Address>+358</Address>", xml);

        // Map elements: key/value per-element groups
        Assert.Contains("[0] firepower", xml);
        Assert.Contains("Key: firepower", xml);
        Assert.Contains("Value: 5", xml);
        Assert.Contains("[3] resistance", xml);
        Assert.Contains("Key: resistance", xml);
        Assert.Contains("Value: 481", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_StructArrayContainerField_UnderPointerChain_EmitsElementsWithSubFields()
    {
        // Simulates: GWorld -> OwningGameInstance -> PlayerData
        // ContainerField: Ships (ArrayProperty, StructProperty inner, with struct sub-fields)
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "GWorld"),
            MakeBc("0x2000", "OwningGameInstance", "OwningGameInstance", isPointer: true, offset: 0x1D8),
            MakeBc("0x3000", "PlayerData", "PlayerData", isPointer: true, offset: 0x1C0),
        };
        var fields = new List<LiveFieldValue>
        {
            new()
            {
                Name = "Ships", TypeName = "ArrayProperty", Offset = 0x468, Size = 16,
                ArrayCount = 2, ArrayInnerType = "StructProperty", ArrayStructType = "ShipData",
                ArrayElemSize = 976,
                ArrayElements = new List<ArrayElementValue>
                {
                    new()
                    {
                        Index = 0, Value = "",
                        StructFields = new List<StructSubFieldValue>
                        {
                            new() { Name = "HealthRatio", TypeName = "FloatProperty", Offset = 0x360, Size = 4 },
                            new() { Name = "ArmorRatio", TypeName = "FloatProperty", Offset = 0x364, Size = 4 },
                        }
                    },
                    new()
                    {
                        Index = 1, Value = "",
                        StructFields = new List<StructSubFieldValue>
                        {
                            new() { Name = "HealthRatio", TypeName = "FloatProperty", Offset = 0x360, Size = 4 },
                            new() { Name = "ArmorRatio", TypeName = "FloatProperty", Offset = 0x364, Size = 4 },
                        }
                    },
                }
            },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+1000", "GWorld", breadcrumbs, fields);

        // Array group at +468 with Offsets=[0]
        Assert.Contains("Ships [2 x ShipData (976B)]", xml);
        Assert.Contains("<Address>+468</Address>", xml);

        // Element [0] at +0, [1] at +elemSize = 976 = 0x3D0
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("<Address>+3D0</Address>", xml);  // elem 1 offset

        // Struct sub-fields within element
        Assert.Contains("\"HealthRatio\"", xml);
        Assert.Contains("\"ArmorRatio\"", xml);
        Assert.Contains("<VariableType>Float</VariableType>", xml);
        Assert.Contains("<Address>+360</Address>", xml);
        Assert.Contains("<Address>+364</Address>", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_MapSingleElement_OnlySelectedElementEmitted()
    {
        // Simulates "Copy CE Field" for a single map element.
        // The ViewModel creates a filtered ContainerField with only the selected element.
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "Root"),
            MakeBc("0x2000", "Player", "Player", isPointer: true, offset: 0x50),
        };
        var fields = new List<LiveFieldValue>
        {
            new()
            {
                Name = "Scores", TypeName = "MapProperty", Offset = 0x80, Size = 0x50,
                MapCount = 10, MapKeyType = "NameProperty", MapValueType = "IntProperty",
                MapKeySize = 8, MapValueSize = 4,
                // Only 1 element (filtered by ViewModel)
                MapElements = new List<ContainerElementValue>
                {
                    new() { Index = 5, Key = "speed", Value = "200" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+1000", "Root", breadcrumbs, fields);

        // Map group shows full count in description
        Assert.Contains("Scores {Map: 10, NameProperty", xml);
        // But only 1 element is emitted
        Assert.Contains("[5] speed", xml);
        Assert.Contains("Key: speed", xml);
        Assert.Contains("Value: 200", xml);
        // No other element indices
        Assert.DoesNotContain("[0]", xml);
        Assert.DoesNotContain("[1]", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_SetContainerField_UnderPointerChain_EmitsElements()
    {
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "Root"),
            MakeBc("0x2000", "Player", "Player", isPointer: true, offset: 0x50),
        };
        var fields = new List<LiveFieldValue>
        {
            new()
            {
                Name = "ActiveTags", TypeName = "SetProperty", Offset = 0x200, Size = 0x50,
                SetCount = 2, SetElemType = "IntProperty", SetElemSize = 4,
                SetElements = new List<ContainerElementValue>
                {
                    new() { Index = 0, Key = "7", KeyHex = "07000000" },
                    new() { Index = 2, Key = "42", KeyHex = "2A000000" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+1000", "Root", breadcrumbs, fields);

        // Pointer chain preserved
        Assert.Contains("\"Root\"", xml);
        Assert.Contains("\"Player\"", xml);
        Assert.Contains("<Address>+50</Address>", xml);

        // Set group at +200 with Offsets=[0]
        Assert.Contains("ActiveTags {Set: 2, IntProperty}", xml);
        Assert.Contains("<Address>+200</Address>", xml);

        // Elements
        Assert.Contains("[0] 7", xml);
        Assert.Contains("[2] 42", xml);
        Assert.Contains("<VariableType>4 Bytes</VariableType>", xml);
    }

    // ========================================
    // StructProperty array without sub-fields tests
    // ========================================

    [Fact]
    public void GenerateInstanceXml_StructArrayNoSubFields_EmitsGroupWithOffsetsAndPlaceholderElements()
    {
        // Array<StructProperty> (Guid, 16B) with 3 elements but no StructFields resolution
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "StreamingTextureGuids", TypeName = "ArrayProperty", Offset = 0x130, Size = 16,
                ArrayCount = 3, ArrayInnerType = "StructProperty", ArrayStructType = "Guid",
                ArrayElemSize = 16,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "", StructFields = null },
                    new() { Index = 1, Value = "", StructFields = null },
                    new() { Index = 2, Value = "", StructFields = null },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"game.exe\"+1000", "MyLevel", "ULevel", fields);

        // Array group should exist with description
        Assert.Contains("StreamingTextureGuids [3 x Guid (16B)]", xml);
        // CRITICAL: Must have Offsets=[0] to dereference TArray.Data pointer
        Assert.Contains("<Offset>0</Offset>", xml);
        // Per-element placeholder groups at stride offsets
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("\"[1]\"", xml);
        Assert.Contains("\"[2]\"", xml);
        // Element 0 at +0, Element 1 at +16 = +10, Element 2 at +32 = +20
        Assert.Contains("<Address>+10</Address>", xml);
        Assert.Contains("<Address>+20</Address>", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_StructArrayNoSubFields_UnderPointerChain()
    {
        // Simulates StreamingTextureGuids under GWorld -> PersistentLevel chain
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "GWorld"),
            MakeBc("0x2000", "PersistentLevel", "PersistentLevel", isPointer: true, offset: 0x30),
        };
        var fields = new List<LiveFieldValue>
        {
            new()
            {
                Name = "StreamingTextureGuids", TypeName = "ArrayProperty", Offset = 0x130, Size = 16,
                ArrayCount = 78, ArrayInnerType = "StructProperty", ArrayStructType = "Guid",
                ArrayElemSize = 16,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "" },
                    new() { Index = 1, Value = "" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+1000", "GWorld", breadcrumbs, fields);

        // Full chain preserved
        Assert.Contains("\"GWorld\"", xml);
        Assert.Contains("\"PersistentLevel\"", xml);
        Assert.Contains("<Address>+30</Address>", xml);

        // Array group with Offsets=[0]
        Assert.Contains("StreamingTextureGuids [78 x Guid (16B)]", xml);
        Assert.Contains("<Address>+130</Address>", xml);
        Assert.Contains("<Offset>0</Offset>", xml);

        // Per-element placeholders
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("\"[1]\"", xml);
    }

    [Fact]
    public void GenerateInstanceXml_StructArrayNoElements_EmitsGroupWithOffsetsOnly()
    {
        // StructProperty array with count > 0 but no inline elements
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "LargeStructArr", TypeName = "ArrayProperty", Offset = 0x200, Size = 16,
                ArrayCount = 1000, ArrayInnerType = "StructProperty", ArrayStructType = "FData",
                ArrayElemSize = 64,
                ArrayElements = null,
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Should still emit with Offsets=[0] (not a placeholder without deref)
        Assert.Contains("LargeStructArr [1000 x FData (64B)]", xml);
        Assert.Contains("<Offset>0</Offset>", xml);
        // No element children (no inline data)
        Assert.DoesNotContain("\"[0]\"", xml);
    }

    // ========================================
    // Container breadcrumb in middle of chain (not last)
    // Simulates navigating through an ArrayProperty of ObjectProperty
    // ========================================

    [Fact]
    public void CleanBreadcrumbs_ContainerViewPreserved_NotRemovedAsCycle()
    {
        // GWorld -> OwningGameInstance -> LocalPlayers(container, same addr) -> [0](pointer)
        var breadcrumbs = new[]
        {
            MakeBc("0x100", "GWorld"),
            MakeBc("0x200", "OwningGameInstance", "OwningGameInstance", isPointer: true, offset: 0x1D8),
            MakeBc("0x200", "LocalPlayers [1 x ObjectProperty (8B)]", "LocalPlayers",
                isPointer: false, offset: 0x38, isContainerView: true),
            MakeBc("0x300", "[0] TQ2LocalPlayer", "[0]", isPointer: true, offset: 0),
        };

        var result = CeXmlExportService.CleanBreadcrumbs(breadcrumbs);

        Assert.Equal(4, result.Count);
        Assert.True(result[2].IsContainerView);
        Assert.Equal("LocalPlayers", result[2].FieldName);
    }

    [Fact]
    public void GenerateHierarchicalXml_ContainerBreadcrumb_EmitsPointerDerefForArrayData()
    {
        // Simulates: GWorld -> OwningGameInstance(ptr) -> LocalPlayers(container) -> [0](ptr)
        // The container breadcrumb should emit Offsets=[0] for TArray::Data dereference.
        // The element breadcrumb [0] should emit Offsets=[0] for ObjectProperty dereference.
        var breadcrumbs = new[]
        {
            MakeBc("0x100", "GWorld"),
            MakeBc("0x200", "OwningGameInstance", "OwningGameInstance", isPointer: true, offset: 0x1D8),
            MakeBc("0x200", "LocalPlayers [1 x ObjectProperty (8B)]", "LocalPlayers",
                isPointer: false, offset: 0x38, isContainerView: true),
            MakeBc("0x300", "[0] TQ2LocalPlayer", "[0]", isPointer: true, offset: 0),
        };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "PlayerController", TypeName = "ObjectProperty", Offset = 0x30, Size = 8,
                PtrAddress = "0x400", PtrName = "TQ2PlayerController" },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+100", "GWorld", breadcrumbs, fields);

        // Full chain preserved: GWorld -> OwningGameInstance -> LocalPlayers -> [0] -> PlayerController
        Assert.Contains("\"GWorld\"", xml);
        Assert.Contains("\"OwningGameInstance\"", xml);
        Assert.Contains("LocalPlayers [1 x ObjectProperty (8B)]", xml);
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("\"PlayerController\"", xml);

        // OwningGameInstance: offset 0x1D8 with pointer deref
        Assert.Contains("<Address>+1D8</Address>", xml);

        // LocalPlayers: offset 0x38 with pointer deref (TArray::Data)
        Assert.Contains("<Address>+38</Address>", xml);

        // [0]: offset 0x0 with pointer deref (ObjectProperty)
        Assert.Contains("<Address>+0</Address>", xml);

        // Should have 3 Offset=0 entries: OwningGameInstance, LocalPlayers, [0]
        Assert.Equal(3, CountOccurrences(xml, "<Offset>0</Offset>"));
    }

    [Fact]
    public void GenerateHierarchicalXml_ContainerBreadcrumb_MapProperty_EmitsDeref()
    {
        // Map container breadcrumb should also get Offsets=[0] for TSparseArray::Data
        var breadcrumbs = new[]
        {
            MakeBc("0x100", "Root"),
            MakeBc("0x200", "Player", "Player", isPointer: true, offset: 0x50),
            MakeBc("0x200", "Inventory {Map: 5}", "Inventory",
                isPointer: false, offset: 0xA0, isContainerView: true),
            MakeBc("0x300", "[2] SomeItem", "[2]", isPointer: true, offset: 0x24),
        };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "ItemName", TypeName = "NameProperty", Offset = 0x10, Size = 8 },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+100", "Root", breadcrumbs, fields);

        // Inventory breadcrumb should emit Offsets=[0]
        Assert.Contains("<Address>+A0</Address>", xml);
        Assert.Contains("Inventory {Map: 5}", xml);
        // 3 derefs: Player, Inventory (map data), [2] (element pointer)
        Assert.Equal(3, CountOccurrences(xml, "<Offset>0</Offset>"));
    }

    // ========================================
    // Helper

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }

    // ========================================
    // DataTable CE XML tests
    // ========================================

    [Fact]
    public void GenerateInstanceXml_DataTableRows_Emits2LevelDerefChain()
    {
        // DataTable RowMap with 2 rows, stride=24, fnameSize=8
        // Row 0: sparseIndex=0, row ptr at offset 0*24+8=8
        // Row 1: sparseIndex=2, row ptr at offset 2*24+8=56=0x38
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "RowMap", TypeName = "DataTableRows", Offset = 0xB0, Size = 0,
                DataTableRowCount = 2, DataTableStructName = "RecipeRow",
                DataTableFNameSize = 8, DataTableStride = 24,
                DataTableRowStructAddr = "0xABC",
                DataTableRowData = new List<DataTableRowInfo>
                {
                    new()
                    {
                        SparseIndex = 0, RowName = "Recipe_Sword", DataAddr = "0x1000",
                        Fields = new List<LiveFieldValue>
                        {
                            new() { Name = "Damage", TypeName = "FloatProperty", Offset = 0x0, Size = 4, TypedValue = "25.0" },
                            new() { Name = "Level", TypeName = "IntProperty", Offset = 0x4, Size = 4, TypedValue = "5" },
                        }
                    },
                    new()
                    {
                        SparseIndex = 2, RowName = "Recipe_Shield", DataAddr = "0x2000",
                        Fields = new List<LiveFieldValue>
                        {
                            new() { Name = "Damage", TypeName = "FloatProperty", Offset = 0x0, Size = 4, TypedValue = "10.0" },
                            new() { Name = "Level", TypeName = "IntProperty", Offset = 0x4, Size = 4, TypedValue = "3" },
                        }
                    },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "DT_Recipes", "UDataTable", fields);

        // Level 1: RowMap group at +B0 with Offsets=[0] (deref TSparseArray.Data)
        Assert.Contains("<Address>+B0</Address>", xml);
        Assert.Contains("<Offset>0</Offset>", xml);
        Assert.Contains("DataTable: 2 x RecipeRow", xml);

        // Level 2: Row 0 at +8 (0*24+8) with Offsets=[0] (deref uint8*)
        Assert.Contains("<Address>+8</Address>", xml);
        Assert.Contains("[0] Recipe_Sword", xml);

        // Level 2: Row 2 at +38 (2*24+8=56=0x38) with Offsets=[0] (deref uint8*)
        Assert.Contains("<Address>+38</Address>", xml);
        Assert.Contains("[2] Recipe_Shield", xml);

        // Level 3: Field leaves inside rows
        Assert.Contains("Damage", xml);
        Assert.Contains("Level", xml);
        Assert.Contains("<VariableType>Float</VariableType>", xml);
        Assert.Contains("<VariableType>4 Bytes</VariableType>", xml);

        // Each row group should have Offsets=[0] for uint8* deref
        // Count total Offset elements: Level 1 (1) + Row 0 (1) + Row 2 (1) = 3
        var offsetCount = CountOccurrences(xml, "<Offset>0</Offset>");
        Assert.Equal(3, offsetCount);
    }

    [Fact]
    public void GenerateInstanceXml_DataTableRows_EmptyRows_EmitsPlaceholder()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "RowMap", TypeName = "DataTableRows", Offset = 0xB0, Size = 0,
                DataTableRowCount = 5, DataTableStructName = "TestRow",
                DataTableFNameSize = 0, DataTableStride = 0,
                // No row data — placeholder expected
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "DT_Test", "UDataTable", fields);

        // Should emit as placeholder (GroupHeader without children)
        Assert.Contains("<Address>+B0</Address>", xml);
        Assert.Contains("GroupHeader", xml);
        // Should NOT have Offsets (placeholder has no deref)
        Assert.DoesNotContain("<Offset>0</Offset>", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_DataTableRowDrillIn_2LevelPointerChain()
    {
        // Simulate drill-in: DataTable → RowMap container → Row struct
        // BC[0]: DataTable instance (pointer deref)
        // BC[1]: RowMap container (container view, offset=0xB0)
        // BC[2]: Row data (pointer deref for uint8*, offset=0x38)
        var breadcrumbs = new[]
        {
            MakeBc("0x7FF6AA000", "DT_Recipes", isPointer: true),
            MakeBc("0x7FF6AA000", "RowMap [2 x RecipeRow]", "RowMap", offset: 0xB0, isContainerView: true),
            MakeBc("0x1234000", "[2] Recipe_Shield (RecipeRow)", "[2] Recipe_Shield", isPointer: true, offset: 0x38),
        };

        var rowFields = new List<LiveFieldValue>
        {
            new() { Name = "Damage", TypeName = "FloatProperty", Offset = 0x0, Size = 4 },
            new() { Name = "Level", TypeName = "IntProperty", Offset = 0x4, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"Game.exe\"+5000", "DT_Recipes", breadcrumbs, rowFields);

        // Root: absolute address
        Assert.Contains("\"Game.exe\"+5000", xml);

        // BC[1] (RowMap container): +B0 with Offsets=[0] (deref Data)
        Assert.Contains("<Address>+B0</Address>", xml);

        // BC[2] (Row): +38 with Offsets=[0] (deref uint8*)
        Assert.Contains("<Address>+38</Address>", xml);

        // Leaf fields: +0 (Damage), +4 (Level)
        Assert.Contains("<Address>+0</Address>", xml);
        Assert.Contains("<Address>+4</Address>", xml);

        // 2-level deref: BC[1] and BC[2] each have Offsets=[0]
        var offsetCount = CountOccurrences(xml, "<Offset>0</Offset>");
        Assert.Equal(2, offsetCount);

        // Field types present
        Assert.Contains("<VariableType>Float</VariableType>", xml);
        Assert.Contains("<VariableType>4 Bytes</VariableType>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_DataTableRows_RowWithStrProperty_EmitsUnicodeString()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "RowMap", TypeName = "DataTableRows", Offset = 0xB0, Size = 0,
                DataTableRowCount = 1, DataTableStructName = "ItemRow",
                DataTableFNameSize = 8, DataTableStride = 24,
                DataTableRowStructAddr = "0xABC",
                DataTableRowData = new List<DataTableRowInfo>
                {
                    new()
                    {
                        SparseIndex = 0, RowName = "Item_Potion", DataAddr = "0x3000",
                        Fields = new List<LiveFieldValue>
                        {
                            new() { Name = "DisplayName", TypeName = "StrProperty", Offset = 0x0, Size = 16 },
                            new() { Name = "Price", TypeName = "IntProperty", Offset = 0x10, Size = 4 },
                        }
                    },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "DT_Items", "UDataTable", fields);

        // StrProperty should emit as String type with Unicode
        Assert.Contains("<VariableType>String</VariableType>", xml);
        Assert.Contains("<Unicode>1</Unicode>", xml);
        Assert.Contains("DisplayName", xml);
    }

    [Fact]
    public void GenerateInstanceXml_DataTableRows_RowWithEnumField_EmitsDropDown()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "RowMap", TypeName = "DataTableRows", Offset = 0xB0, Size = 0,
                DataTableRowCount = 1, DataTableStructName = "ItemRow",
                DataTableFNameSize = 8, DataTableStride = 24,
                DataTableRowStructAddr = "0xABC",
                DataTableRowData = new List<DataTableRowInfo>
                {
                    new()
                    {
                        SparseIndex = 0, RowName = "Item_Sword", DataAddr = "0x3000",
                        Fields = new List<LiveFieldValue>
                        {
                            new()
                            {
                                Name = "ItemType", TypeName = "EnumProperty", Offset = 0x0, Size = 4,
                                EnumName = "Weapon", EnumValue = 1, EnumAddr = "0xE001",
                                EnumEntries = new List<EnumEntryValue>
                                {
                                    new() { Value = 0, Name = "None" },
                                    new() { Value = 1, Name = "Weapon" },
                                    new() { Value = 2, Name = "Armor" },
                                }
                            },
                        }
                    },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "DT_Items", "UDataTable", fields);

        // EnumProperty in row should have DropDownList
        Assert.Contains("DropDownList", xml);
        Assert.Contains("Weapon", xml);
        Assert.Contains("Armor", xml);
    }

    // ========================================
    // GenerateAobWrappedXml tests
    // ========================================

    [Fact]
    public void GenerateAobWrappedXml_ContainsAAScript()
    {
        var breadcrumbs = new[]
        {
            MakeBc("0x12A37FFB5A0", "World"),
            MakeBc("0x12A38001000", "Level", "PersistentLevel", isPointer: true, offset: 0x30),
        };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x100, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateAobWrappedXml(
            "World", breadcrumbs, fields,
            aob: "48 8B 1D ?? ?? ?? ??", aobPos: 3, aobLen: 7,
            moduleName: "Game.exe");

        Assert.Contains("Auto Assembler Script", xml);
        Assert.Contains("AOBScanModule", xml);
        Assert.Contains("registerSymbol", xml);
        Assert.Contains("unregisterSymbol", xml);
        Assert.Contains("\"base\"", xml);
        Assert.Contains("48 8B 1D ?? ?? ?? ??", xml);
        Assert.Contains("Health", xml);
    }

    [Fact]
    public void GenerateAobWrappedXml_SymbolHas6HexSuffix()
    {
        var breadcrumbs = new[] { MakeBc("0xA", "World") };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "X", TypeName = "FloatProperty", Offset = 0, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateAobWrappedXml(
            "World", breadcrumbs, fields,
            aob: "48 8B 1D", aobPos: 3, aobLen: 7,
            moduleName: "Test.exe");

        // Symbol name pattern: gworld_addr_XXXXXX (6 hex chars)
        Assert.Matches(@"gworld_addr_[0-9A-F]{6}", xml);
    }

    [Fact]
    public void GenerateAobWrappedXml_BaseEntryDereferencesSymbol()
    {
        var breadcrumbs = new[] { MakeBc("0xA", "World") };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "HP", TypeName = "IntProperty", Offset = 0x10, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateAobWrappedXml(
            "World", breadcrumbs, fields,
            aob: "48 8B 1D", aobPos: 3, aobLen: 7,
            moduleName: "Test.exe");

        // base entry has Offset 0 for pointer dereference
        Assert.Contains("<Offset>0</Offset>", xml);
        // base entry uses symbol as address
        Assert.Matches(@"<Address>gworld_addr_[0-9A-F]{6}</Address>", xml);
    }

    [Fact]
    public void GenerateAobWrappedXml_RootOnlyBreadcrumb_FieldsUnderBase()
    {
        var breadcrumbs = new[] { MakeBc("0xA", "World") };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Score", TypeName = "IntProperty", Offset = 0x20, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateAobWrappedXml(
            "World", breadcrumbs, fields,
            aob: "48 8B 1D", aobPos: 3, aobLen: 7,
            moduleName: "Test.exe");

        Assert.Contains("Score", xml);
        // Verify XML is well-formed (has closing tags)
        Assert.Contains("</CheatTable>", xml);
    }

    [Fact]
    public void GenerateAobWrappedXml_DisableSection_ContainsUnregister()
    {
        var breadcrumbs = new[] { MakeBc("0xA", "World") };
        var fields = new List<LiveFieldValue>();

        var xml = CeXmlExportService.GenerateAobWrappedXml(
            "World", breadcrumbs, fields,
            aob: "48 8B 1D", aobPos: 3, aobLen: 7,
            moduleName: "Test.exe");

        Assert.Contains("[DISABLE]", xml);
        Assert.Contains("unregisterSymbol", xml);
    }

    [Fact]
    public void GenerateAobWrappedXml_MultipleBreadcrumbs_EmitsIntermediateGroups()
    {
        var breadcrumbs = new[]
        {
            MakeBc("0xA", "World"),
            MakeBc("0xB", "GameInstance", "OwningGameInstance", isPointer: true, offset: 0x158),
            MakeBc("0xC", "Player", "GamePlayer", isPointer: true, offset: 0x128),
        };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "HP", TypeName = "IntProperty", Offset = 0x7C, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateAobWrappedXml(
            "World", breadcrumbs, fields,
            aob: "48 8B 1D ?? ?? ?? ??", aobPos: 3, aobLen: 7,
            moduleName: "Game.exe");

        Assert.Contains("OwningGameInstance", xml);
        Assert.Contains("GamePlayer", xml);
        Assert.Contains("+158", xml);
        Assert.Contains("+128", xml);
        Assert.Contains("HP", xml);
    }

    [Fact]
    public void GenerateAobWrappedXml_UniqueSymbolsAcrossCalls()
    {
        var breadcrumbs = new[] { MakeBc("0xA", "World") };
        var fields = new List<LiveFieldValue>();

        var xml1 = CeXmlExportService.GenerateAobWrappedXml(
            "World", breadcrumbs, fields,
            aob: "48 8B 1D", aobPos: 3, aobLen: 7,
            moduleName: "Test.exe");
        var xml2 = CeXmlExportService.GenerateAobWrappedXml(
            "World", breadcrumbs, fields,
            aob: "48 8B 1D", aobPos: 3, aobLen: 7,
            moduleName: "Test.exe");

        // Extract symbol names — should differ between calls (with very high probability)
        var match1 = System.Text.RegularExpressions.Regex.Match(xml1, @"gworld_addr_([0-9A-F]{6})");
        var match2 = System.Text.RegularExpressions.Regex.Match(xml2, @"gworld_addr_([0-9A-F]{6})");
        Assert.True(match1.Success);
        Assert.True(match2.Success);
        // Probability of collision: 1/~15M — effectively impossible
        Assert.NotEqual(match1.Groups[1].Value, match2.Groups[1].Value);
    }

    [Fact]
    public void GenerateAobWrappedXml_OptionsHideChildren()
    {
        var breadcrumbs = new[] { MakeBc("0xA", "World") };
        var fields = new List<LiveFieldValue>();

        var xml = CeXmlExportService.GenerateAobWrappedXml(
            "World", breadcrumbs, fields,
            aob: "48 8B 1D", aobPos: 3, aobLen: 7,
            moduleName: "Test.exe");

        // AA Script root entry should have moHideChildren
        Assert.Contains("moHideChildren=\"1\"", xml);
        Assert.Contains("moDeactivateChildrenAsWell=\"1\"", xml);
    }

    private static BreadcrumbItem MakeBc(string addr, string label,
        string fieldName = "", bool isPointer = false, int offset = 0,
        bool isContainerView = false)
    {
        return new BreadcrumbItem
        {
            Address = addr,
            Label = label,
            FieldName = string.IsNullOrEmpty(fieldName) ? label : fieldName,
            FieldOffset = offset,
            IsPointerDeref = isPointer,
            IsContainerView = isContainerView,
        };
    }
}
