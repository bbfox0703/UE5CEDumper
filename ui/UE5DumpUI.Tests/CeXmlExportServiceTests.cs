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
    public void GenerateInstanceXml_EnumArray_ElementDescIncludesEnumName()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "ShipTypes", TypeName = "ArrayProperty", Offset = 0x200, Size = 16,
                ArrayCount = 2, ArrayInnerType = "ByteProperty", ArrayElemSize = 1,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "0", Hex = "00", EnumName = "EShip::Scout" },
                    new() { Index = 1, Value = "1", Hex = "01", EnumName = "EShip::SpecOps" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Enum names in element descriptions
        Assert.Contains("[0] EShip::Scout", xml);
        Assert.Contains("[1] EShip::SpecOps", xml);
        Assert.Contains("<VariableType>Byte</VariableType>", xml);
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
