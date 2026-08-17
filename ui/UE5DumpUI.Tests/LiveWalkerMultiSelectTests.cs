using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for the Copy CE Field(s) multi-selection behaviour:
/// - <see cref="LiveWalkerViewModel.FilterContainerToElement"/> retains
///   multiple matching elements (Array / Map / Set / DataTable) and falls
///   back to the whole container when no selection has a parseable sparse
///   index.
/// - End-to-end via <see cref="CeXmlExportService.GenerateHierarchicalXml"/>
///   to confirm multiple scalar selections emit a single CE root with N
///   leaves under the same pointer chain.
/// </summary>
public class LiveWalkerMultiSelectTests
{
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

    private static LiveFieldValue MakeSynthetic(int sparseIndex, string suffix = "")
    {
        return new LiveFieldValue
        {
            Name = string.IsNullOrEmpty(suffix) ? $"[{sparseIndex}]" : $"[{sparseIndex}] {suffix}",
        };
    }

    // ------------------------------------------------------------------
    // FilterContainerToElement — Array
    // ------------------------------------------------------------------

    [Fact]
    public void FilterContainer_ArrayMultiElement_KeepsAllSelectedIndices()
    {
        var container = new LiveFieldValue
        {
            Name = "Inventory",
            TypeName = "ArrayProperty",
            ArrayCount = 5,
            ArrayInnerType = "IntProperty",
            ArrayElemSize = 4,
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, Value = "10" },
                new() { Index = 1, Value = "20" },
                new() { Index = 2, Value = "30" },
                new() { Index = 3, Value = "40" },
                new() { Index = 4, Value = "50" },
            }
        };

        // Select rows [1] and [3]
        var selected = new List<LiveFieldValue> { MakeSynthetic(1), MakeSynthetic(3) };
        var filtered = LiveWalkerViewModel.FilterContainerToElement(container, selected);

        Assert.NotNull(filtered.ArrayElements);
        Assert.Equal(2, filtered.ArrayElements!.Count);
        Assert.Contains(filtered.ArrayElements, e => e.Index == 1);
        Assert.Contains(filtered.ArrayElements, e => e.Index == 3);
        // Container metadata preserved (count, type, etc.)
        Assert.Equal(5, filtered.ArrayCount);
        Assert.Equal("IntProperty", filtered.ArrayInnerType);
    }

    [Fact]
    public void FilterContainer_ArraySingleSelection_RetainsOnlyThatElement()
    {
        // Backward compat: single-selection still works
        var container = new LiveFieldValue
        {
            Name = "Inventory",
            TypeName = "ArrayProperty",
            ArrayCount = 3,
            ArrayInnerType = "FloatProperty",
            ArrayElemSize = 4,
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, Value = "1.0" },
                new() { Index = 1, Value = "2.0" },
                new() { Index = 2, Value = "3.0" },
            }
        };

        var selected = new List<LiveFieldValue> { MakeSynthetic(2) };
        var filtered = LiveWalkerViewModel.FilterContainerToElement(container, selected);

        Assert.NotNull(filtered.ArrayElements);
        Assert.Single(filtered.ArrayElements!);
        Assert.Equal(2, filtered.ArrayElements![0].Index);
    }

    // ------------------------------------------------------------------
    // FilterContainerToElement — Map / Set / DataTable
    // ------------------------------------------------------------------

    [Fact]
    public void FilterContainer_MapMultiElement_KeepsAllSelectedIndices()
    {
        var container = new LiveFieldValue
        {
            Name = "Stats",
            TypeName = "MapProperty",
            MapCount = 4,
            MapKeyType = "NameProperty",
            MapValueType = "IntProperty",
            MapKeySize = 8,
            MapValueSize = 4,
            MapElements = new List<ContainerElementValue>
            {
                new() { Index = 0, Key = "HP",     Value = "100" },
                new() { Index = 1, Key = "MP",     Value = "50" },
                new() { Index = 2, Key = "Stam",   Value = "75" },
                new() { Index = 3, Key = "Energy", Value = "20" },
            }
        };

        // User selects [0] HP and [2] Stam
        var selected = new List<LiveFieldValue>
        {
            MakeSynthetic(0, "HP"),
            MakeSynthetic(2, "Stam"),
        };
        var filtered = LiveWalkerViewModel.FilterContainerToElement(container, selected);

        Assert.NotNull(filtered.MapElements);
        Assert.Equal(2, filtered.MapElements!.Count);
        Assert.Contains(filtered.MapElements, e => e.Index == 0 && e.Key == "HP");
        Assert.Contains(filtered.MapElements, e => e.Index == 2 && e.Key == "Stam");
        // Display count remains the full map count so the user sees the
        // header description "Map: 4" instead of "Map: 2".
        Assert.Equal(4, filtered.MapCount);
    }

    [Fact]
    public void FilterContainer_SetMultiElement_KeepsAllSelectedIndices()
    {
        var container = new LiveFieldValue
        {
            Name = "Tags",
            TypeName = "SetProperty",
            SetCount = 3,
            SetElemType = "IntProperty",
            SetElemSize = 4,
            SetElements = new List<ContainerElementValue>
            {
                new() { Index = 0, Key = "7" },
                new() { Index = 1, Key = "13" },
                new() { Index = 2, Key = "42" },
            }
        };

        var selected = new List<LiveFieldValue> { MakeSynthetic(0), MakeSynthetic(2) };
        var filtered = LiveWalkerViewModel.FilterContainerToElement(container, selected);

        Assert.NotNull(filtered.SetElements);
        Assert.Equal(2, filtered.SetElements!.Count);
        Assert.Contains(filtered.SetElements, e => e.Index == 0);
        Assert.Contains(filtered.SetElements, e => e.Index == 2);
    }

    [Fact]
    public void FilterContainer_DataTableMultiRow_KeepsAllSelectedIndices()
    {
        var container = new LiveFieldValue
        {
            Name = "WeaponTable",
            TypeName = "DataTable",
            DataTableRowCount = 4,
            DataTableStructName = "FWeaponRow",
            DataTableRowData = new List<DataTableRowInfo>
            {
                new() { SparseIndex = 0, RowName = "Sword" },
                new() { SparseIndex = 1, RowName = "Bow" },
                new() { SparseIndex = 2, RowName = "Staff" },
                new() { SparseIndex = 3, RowName = "Dagger" },
            }
        };

        var selected = new List<LiveFieldValue>
        {
            MakeSynthetic(1, "Bow"),
            MakeSynthetic(3, "Dagger"),
        };
        var filtered = LiveWalkerViewModel.FilterContainerToElement(container, selected);

        Assert.NotNull(filtered.DataTableRowData);
        Assert.Equal(2, filtered.DataTableRowData!.Count);
        Assert.Contains(filtered.DataTableRowData, r => r.SparseIndex == 1);
        Assert.Contains(filtered.DataTableRowData, r => r.SparseIndex == 3);
    }

    // ------------------------------------------------------------------
    // Fallback paths
    // ------------------------------------------------------------------

    [Fact]
    public void FilterContainer_NoParseableIndex_ReturnsWholeContainer()
    {
        // Original single-select behaviour: if the selected field name
        // doesn't follow the "[N]" pattern we can't filter, so emit the
        // whole container.
        var container = new LiveFieldValue
        {
            Name = "Inventory",
            TypeName = "ArrayProperty",
            ArrayCount = 2,
            ArrayInnerType = "IntProperty",
            ArrayElemSize = 4,
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, Value = "10" },
                new() { Index = 1, Value = "20" },
            }
        };

        var selected = new List<LiveFieldValue>
        {
            new() { Name = "NotAnElement" },
        };
        var filtered = LiveWalkerViewModel.FilterContainerToElement(container, selected);

        // Same object returned (whole container) — both elements still
        // present.
        Assert.Same(container, filtered);
    }

    [Fact]
    public void FilterContainer_EmptySelection_ReturnsWholeContainer()
    {
        var container = new LiveFieldValue
        {
            Name = "Stuff",
            TypeName = "ArrayProperty",
            ArrayCount = 1,
            ArrayElements = new List<ArrayElementValue> { new() { Index = 0, Value = "x" } },
        };

        var filtered = LiveWalkerViewModel.FilterContainerToElement(
            container, new List<LiveFieldValue>());

        Assert.Same(container, filtered);
    }

    [Fact]
    public void FilterContainer_MixedParseableUnparseable_KeepsParseableSubset()
    {
        // Defensive: a stray non-synthetic row in the selection (shouldn't
        // happen in practice but guard anyway). The parseable ones still
        // get filtered, the unparseable one is silently dropped.
        var container = new LiveFieldValue
        {
            Name = "Inventory",
            TypeName = "ArrayProperty",
            ArrayCount = 3,
            ArrayInnerType = "IntProperty",
            ArrayElemSize = 4,
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, Value = "10" },
                new() { Index = 1, Value = "20" },
                new() { Index = 2, Value = "30" },
            }
        };

        var selected = new List<LiveFieldValue>
        {
            MakeSynthetic(0),
            new() { Name = "RandomNonSyntheticRow" },
            MakeSynthetic(2),
        };
        var filtered = LiveWalkerViewModel.FilterContainerToElement(container, selected);

        Assert.NotNull(filtered.ArrayElements);
        Assert.Equal(2, filtered.ArrayElements!.Count);
        Assert.Contains(filtered.ArrayElements, e => e.Index == 0);
        Assert.Contains(filtered.ArrayElements, e => e.Index == 2);
    }

    // ------------------------------------------------------------------
    // End-to-end: multi-select non-container view -> single XML root
    // ------------------------------------------------------------------

    [Fact]
    public void GenerateHierarchicalXml_MultipleScalarFields_EmitsSingleRootWithAllLeaves()
    {
        // Sibling fields under one pointer chain — the caller passes a
        // multi-element list and the emitter produces one root + N leaves.
        var breadcrumbs = new[] { MakeBc("0x1000", "Root") };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4 },
            new() { Name = "Mana",   TypeName = "FloatProperty", Offset = 0x14, Size = 4 },
            new() { Name = "Level",  TypeName = "IntProperty",   Offset = 0x18, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+1000", "Root", breadcrumbs, fields);

        // One XML declaration (single combined output, not three separate ones)
        Assert.Equal(1, CountOccurrences(xml, "<?xml"));
        // One root group + 3 leaves
        Assert.Equal(4, CountOccurrences(xml, "<CheatEntry>"));
        Assert.Contains("\"Health\"", xml);
        Assert.Contains("\"Mana\"",   xml);
        Assert.Contains("\"Level\"",  xml);
        Assert.Contains("<Address>+10</Address>", xml);
        Assert.Contains("<Address>+14</Address>", xml);
        Assert.Contains("<Address>+18</Address>", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_MultipleArrayElements_EmitsOneArrayGroupWithSelectedLeaves()
    {
        // Container-view multi-select: VM passes one container with 2
        // filtered elements, the emitter wraps them under one array group.
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "Root"),
            MakeBc("0x2000", "Player", "Player", isPointer: true, offset: 0x50),
        };
        var fields = new List<LiveFieldValue>
        {
            new()
            {
                Name = "Scores", TypeName = "ArrayProperty", Offset = 0x80, Size = 16,
                ArrayCount = 5, ArrayInnerType = "IntProperty", ArrayElemSize = 4,
                // Pre-filtered by the VM to indices 1 and 3
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 1, Value = "20" },
                    new() { Index = 3, Value = "40" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+1000", "Root", breadcrumbs, fields);

        // Exactly one CE document
        Assert.Equal(1, CountOccurrences(xml, "<?xml"));
        // Array group description is now the bare field name (no array descriptor)
        Assert.Contains("\"Scores\"", xml);
        Assert.DoesNotContain("[5 x IntProperty", xml);
        // Both selected elements appear; the unselected ones do not
        Assert.Contains("\"[1]\"", xml);
        Assert.Contains("\"[3]\"", xml);
        Assert.DoesNotContain("\"[0]\"", xml);
        Assert.DoesNotContain("\"[2]\"", xml);
        Assert.DoesNotContain("\"[4]\"", xml);
    }

    // ------------------------------------------------------------------
    // PathStepToBreadcrumbs — GWorld-path array-element hop splitting
    // ------------------------------------------------------------------

    [Fact]
    public void PathStepToBreadcrumbs_ObjectArrayElement_SplitsIntoContainerPlusElement()
    {
        // A GWorld-path hop through a TArray<ObjectProperty> element must expand
        // into TWO crumbs: the array field (deref TArray::Data) + the element
        // (deref the pointer at index*8). Regression for the Locate-in-GWorld
        // "[0] missing → wrong addresses downstream" Copy CE Field bug.
        var step = new GWorldPathStep
        {
            From = "0x3AB399520", To = "0x3AB399C40",
            FieldOffset = 0x2E0, FieldName = "PlayerArray",
            FieldType = "ArrayProperty", InnerType = "ObjectProperty",
            ElementIndex = 0, ToName = "PlayerState", ToClass = "PlayerState",
        };

        var crumbs = LiveWalkerViewModel.PathStepToBreadcrumbs(step);

        Assert.Equal(2, crumbs.Count);
        // Level 1: array field — container-view deref of TArray::Data at +2E0.
        Assert.Equal(0x2E0, crumbs[0].FieldOffset);
        Assert.True(crumbs[0].IsContainerView);
        Assert.Equal("PlayerArray", crumbs[0].FieldName);
        // Level 2: element pointer at index*8 = 0, pointer deref to PlayerState.
        Assert.Equal(0, crumbs[1].FieldOffset);
        Assert.True(crumbs[1].IsPointerDeref);
        Assert.False(crumbs[1].IsContainerView);
        Assert.Equal("[0]", crumbs[1].FieldName);
        Assert.Equal("0x3AB399C40", crumbs[1].Address);
    }

    [Fact]
    public void PathStepToBreadcrumbs_ObjectArrayElement_ElementOffsetIsIndexTimes8()
    {
        var step = new GWorldPathStep
        {
            From = "0xA", To = "0xB", FieldOffset = 0x10, FieldName = "Arr",
            FieldType = "ArrayProperty", InnerType = "ClassProperty", ElementIndex = 2,
        };
        var crumbs = LiveWalkerViewModel.PathStepToBreadcrumbs(step);
        Assert.Equal(2, crumbs.Count);
        Assert.Equal(0x10, crumbs[1].FieldOffset);  // index 2 * 8-byte ptr stride = 0x10
    }

    [Fact]
    public void PathStepToBreadcrumbs_WorldLevel_EmitsSingleNonDerefNavCrumb()
    {
        // The streaming/World-Partition recovery (Aura::RecoverViaWorldLevel) emits
        // a synthetic world -> level hop reached via ULevel::OwningWorld (a
        // back-reference, not a forward pointer). It must render as a plain nav
        // anchor (navigate by Address, NOT a pointer deref) so CE export doesn't
        // fabricate an offset for a hop that has none.
        var step = new GWorldPathStep
        {
            From = "0x100", To = "0x200", FieldOffset = -1, FieldName = "Levels",
            FieldType = "WorldLevel", ElementIndex = -1,
            ToName = "PersistentLevel", ToClass = "Level",
        };
        var crumbs = LiveWalkerViewModel.PathStepToBreadcrumbs(step);
        Assert.Single(crumbs);
        Assert.Equal("0x200", crumbs[0].Address);          // navigate to the level by address
        Assert.False(crumbs[0].IsPointerDeref);            // NOT a deref — no static offset
        Assert.False(crumbs[0].IsContainerView);
        Assert.Equal("PersistentLevel", crumbs[0].Label);  // resolved level name
    }

    [Fact]
    public void PathStepToBreadcrumbs_LevelActor_EmitsSingleNonDerefNavCrumb()
    {
        // Audit #5 F8. The level -> actor hop is synthetic for the same reason the
        // world -> level hop above is: ULevel::Actors is declared
        // `TArray<TObjectPtr<AActor>> Actors;` with NO UPROPERTY, so there is no
        // reflected offset and no element index to publish. The lookup that used to
        // produce them could not see the field at all -- which is why ok_via_level
        // never fired -- and its fuzzy fallback could bind "Actors" to
        // DestroyedReplicatedStaticActors, which IS reflected, and scan the wrong
        // array.
        var step = new GWorldPathStep
        {
            From = "0x200", To = "0x300", FieldOffset = -1, FieldName = "Actors",
            FieldType = "LevelActor", ElementIndex = -1,
            ToName = "BP_Enemy_C_2", ToClass = "BP_Enemy_C",
        };
        var crumbs = LiveWalkerViewModel.PathStepToBreadcrumbs(step);
        Assert.Single(crumbs);
        Assert.Equal("0x300", crumbs[0].Address);      // navigate to the actor by address
        Assert.False(crumbs[0].IsPointerDeref);        // NOT a deref -- no static offset exists
        Assert.False(crumbs[0].IsContainerView);       // and NOT an array element view
        Assert.Equal("BP_Enemy_C_2", crumbs[0].Label);
    }

    /// <summary>A -1 hop must keep the GWorld-walkable CE export gate CLOSED: a
    /// back-reference cannot be reproduced by a forward walk, so exporting a
    /// pointer chain through it would fabricate one.</summary>
    [Theory]
    [InlineData("WorldLevel")]
    [InlineData("LevelActor")]
    public void PathStepToBreadcrumbs_SyntheticHops_CarryNoForwardOffset(string fieldType)
    {
        var step = new GWorldPathStep
        {
            From = "0x1", To = "0x2", FieldOffset = -1, FieldName = "X",
            FieldType = fieldType, ElementIndex = -1, ToName = "N",
        };
        var crumbs = LiveWalkerViewModel.PathStepToBreadcrumbs(step);
        Assert.Single(crumbs);
        Assert.True(crumbs[0].FieldOffset < 0);
        Assert.False(crumbs[0].IsPointerDeref);
    }

    [Fact]
    public void PathStepToBreadcrumbs_DirectPointerField_SingleCrumb()
    {
        var step = new GWorldPathStep
        {
            From = "0xA", To = "0xB", FieldOffset = 0x340, FieldName = "PawnPrivate",
            FieldType = "ObjectProperty", ElementIndex = -1, ToName = "BP_PlayerCharacter_C",
        };
        var crumbs = LiveWalkerViewModel.PathStepToBreadcrumbs(step);
        Assert.Single(crumbs);
        Assert.Equal(0x340, crumbs[0].FieldOffset);
        Assert.True(crumbs[0].IsPointerDeref);
        Assert.False(crumbs[0].IsContainerView);
    }

    [Fact]
    public void PathStepToBreadcrumbs_StructArrayElement_WithoutStride_DoesNotSplit()
    {
        // A struct-array element with NO carried stride (legacy / non-deep path)
        // can't be split into a correct element deref — keep the single crumb
        // rather than emit a wrong index*8 deref. (The deep BFS DOES carry a
        // stride; see the next test.)
        var step = new GWorldPathStep
        {
            From = "0xA", To = "0xB", FieldOffset = 0x10, FieldName = "Items",
            FieldType = "ArrayProperty", InnerType = "StructProperty", ElementIndex = 1,
        };
        var crumbs = LiveWalkerViewModel.PathStepToBreadcrumbs(step);
        Assert.Single(crumbs);
    }

    [Fact]
    public void PathStepToBreadcrumbs_DeepStructArrayElement_SplitsWithStrideAndPtrOffset()
    {
        // Aura's deep BFS edge: an object pointer stored inside a TArray<FStruct>
        // element (e.g. Inventory[2].ItemActor). It carries the element struct
        // stride + the pointer's within-element offset, so it must split into a
        // container crumb (deref TArray::Data at the array field) + an element
        // crumb at index*stride + ptrOffset.
        // The DLL emits FieldName = the container field name only (Aura deep BFS
        // uses cfe.name + ".Key"/".Value" suffix — never the inner pointer field),
        // so the container crumb names the real field and back-nav re-hydration matches.
        var step = new GWorldPathStep
        {
            From = "0x100", To = "0x200",
            FieldOffset = 0x80, FieldName = "Inventory",
            FieldType = "ArrayProperty", InnerType = "StructProperty", ElementIndex = 2,
            ElemStride = 0x30, ElemValueOffset = 0x18,
            ToName = "BP_Item_C", ToClass = "BP_Item_C",
        };

        var crumbs = LiveWalkerViewModel.PathStepToBreadcrumbs(step);

        Assert.Equal(2, crumbs.Count);
        Assert.Equal(0x80, crumbs[0].FieldOffset);
        Assert.True(crumbs[0].IsContainerView);
        Assert.Equal("Inventory", crumbs[0].FieldName);          // container field named correctly
        Assert.Equal(2 * 0x30 + 0x18, crumbs[1].FieldOffset);   // 0x78
        Assert.True(crumbs[1].IsPointerDeref);
        Assert.False(crumbs[1].IsContainerView);
        Assert.Equal("[2]", crumbs[1].FieldName);
        Assert.Equal("0x200", crumbs[1].Address);
    }

    [Fact]
    public void PathStepToBreadcrumbs_MapValueElement_SplitsWithStrideAndValueOffset()
    {
        // A GWorld-path hop through a TMap<K, UObject*> value must split into a
        // container crumb (deref TSparseArray::Data at the map field) + an element
        // crumb at index*pairStride + valueOffset. The container crumb drops the
        // ".Value" suffix so it names the real TMap field.
        var step = new GWorldPathStep
        {
            From = "0x100", To = "0x200",
            FieldOffset = 0xC0, FieldName = "SpawnedAttributes.Value",
            FieldType = "MapProperty", InnerType = "Object", ElementIndex = 3,
            ElemStride = 0x18, ElemValueOffset = 0x10,
            ToName = "AttributeSet", ToClass = "AttributeSet",
        };

        var crumbs = LiveWalkerViewModel.PathStepToBreadcrumbs(step);

        Assert.Equal(2, crumbs.Count);
        Assert.Equal(0xC0, crumbs[0].FieldOffset);
        Assert.True(crumbs[0].IsContainerView);
        Assert.Equal("SpawnedAttributes", crumbs[0].FieldName);          // ".Value" stripped
        Assert.Equal(3 * 0x18 + 0x10, crumbs[1].FieldOffset);           // 0x58
        Assert.True(crumbs[1].IsPointerDeref);
        Assert.False(crumbs[1].IsContainerView);
        Assert.Equal("[3].Value", crumbs[1].FieldName);
        Assert.Equal("0x200", crumbs[1].Address);
    }

    [Fact]
    public void PathStepToBreadcrumbs_MapKeyElement_SplitsWithZeroValueOffset()
    {
        // Map KEY edge: value offset is 0 → element at index*pairStride.
        var step = new GWorldPathStep
        {
            From = "0x100", To = "0x200",
            FieldOffset = 0x40, FieldName = "Lookup.Key",
            FieldType = "MapProperty", InnerType = "Object", ElementIndex = 2,
            ElemStride = 0x18, ElemValueOffset = 0,
        };
        var crumbs = LiveWalkerViewModel.PathStepToBreadcrumbs(step);
        Assert.Equal(2, crumbs.Count);
        Assert.Equal("Lookup", crumbs[0].FieldName);                    // ".Key" stripped
        Assert.Equal(2 * 0x18, crumbs[1].FieldOffset);                  // 0x30
        Assert.Equal("[2].Key", crumbs[1].FieldName);
    }

    [Fact]
    public void PathStepToBreadcrumbs_SetElement_SplitsAtIndexTimesStride()
    {
        var step = new GWorldPathStep
        {
            From = "0x100", To = "0x200",
            FieldOffset = 0x80, FieldName = "ActiveActors",
            FieldType = "SetProperty", InnerType = "Object", ElementIndex = 4,
            ElemStride = 0x10, ElemValueOffset = 0,
        };
        var crumbs = LiveWalkerViewModel.PathStepToBreadcrumbs(step);
        Assert.Equal(2, crumbs.Count);
        Assert.True(crumbs[0].IsContainerView);
        Assert.Equal("ActiveActors", crumbs[0].FieldName);
        Assert.Equal(4 * 0x10, crumbs[1].FieldOffset);                  // 0x40
        Assert.Equal("[4]", crumbs[1].FieldName);
    }

    [Fact]
    public void PathStepToBreadcrumbs_InterfaceArrayElement_SplitsAtIndexTimes16()
    {
        // TArray<FScriptInterface>: 16-byte slot with the object pointer at elem+0 →
        // splittable via the DLL-threaded stride (16), value offset 0.
        var step = new GWorldPathStep
        {
            From = "0x100", To = "0x200",
            FieldOffset = 0x50, FieldName = "Listeners",
            FieldType = "ArrayProperty", InnerType = "InterfaceProperty", ElementIndex = 3,
            ElemStride = 16, ElemValueOffset = 0,
        };
        var crumbs = LiveWalkerViewModel.PathStepToBreadcrumbs(step);
        Assert.Equal(2, crumbs.Count);
        Assert.True(crumbs[0].IsContainerView);
        Assert.Equal("Listeners", crumbs[0].FieldName);
        Assert.Equal(3 * 16, crumbs[1].FieldOffset);    // 0x30
        Assert.True(crumbs[1].IsPointerDeref);
        Assert.Equal("[3]", crumbs[1].FieldName);
    }

    [Fact]
    public void PathStepToBreadcrumbs_MapElement_NoStride_KeepsSingleCrumb()
    {
        // Defensive: an older DLL that doesn't carry ElemStride (==0) must not split
        // (a 0-stride element offset would be wrong) — fall back to one crumb.
        var step = new GWorldPathStep
        {
            From = "0x100", To = "0x200",
            FieldOffset = 0x40, FieldName = "Lookup.Value",
            FieldType = "MapProperty", InnerType = "Object", ElementIndex = 2,
            ElemStride = 0, ElemValueOffset = 0,
        };
        var crumbs = LiveWalkerViewModel.PathStepToBreadcrumbs(step);
        Assert.Single(crumbs);
        Assert.Equal(0x40, crumbs[0].FieldOffset);
    }

    [Fact]
    public void GenerateHierarchicalXml_PathThroughObjectArrayElement_EmitsElementDerefNode()
    {
        // End-to-end: a GWorld path GameState → PlayerArray[0] → PlayerState →
        // PawnPrivate must emit the array field (+2E0, deref Data), THEN the
        // element ([0] +0, deref ptr), THEN PawnPrivate (+340) NESTED under the
        // element — not PawnPrivate directly under the array (which would apply
        // +340 to the Data buffer base).
        var steps = new[]
        {
            new GWorldPathStep { From="0xW", To="0x3AB399520", FieldOffset=0x1B0, FieldName="GameState", FieldType="ObjectProperty", ElementIndex=-1, ToName="GameState" },
            new GWorldPathStep { From="0x3AB399520", To="0x3AB399C40", FieldOffset=0x2E0, FieldName="PlayerArray", FieldType="ArrayProperty", InnerType="ObjectProperty", ElementIndex=0, ToName="PlayerState" },
            new GWorldPathStep { From="0x3AB399C40", To="0x13F828040", FieldOffset=0x340, FieldName="PawnPrivate", FieldType="ObjectProperty", ElementIndex=-1, ToName="BP_PlayerCharacter_C" },
        };
        var breadcrumbs = new List<BreadcrumbItem> { MakeBc("0xW", "GWorld", "GWorld", isPointer: true) };
        foreach (var s in steps)
            breadcrumbs.AddRange(LiveWalkerViewModel.PathStepToBreadcrumbs(s));

        var fields = new List<LiveFieldValue>
            { new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x30, Size = 4 } };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+1000", "GWorld", breadcrumbs, fields);

        // The element [0] deref node must exist (was missing → the bug).
        int elemIdx = xml.IndexOf("\"[0]\"", StringComparison.Ordinal);
        Assert.True(elemIdx >= 0, "element [0] deref node missing from the chain");
        Assert.Contains("<Address>+2E0</Address>", xml);  // PlayerArray (deref Data)
        Assert.Contains("<Address>+340</Address>", xml);  // PawnPrivate
        // PawnPrivate must come AFTER [0] in the document (nested under it), so the
        // +340 resolves against PlayerState, not the TArray::Data buffer.
        int pawnIdx = xml.IndexOf("\"PawnPrivate\"", StringComparison.Ordinal);
        Assert.True(pawnIdx > elemIdx, "PawnPrivate must nest under the [0] element");
    }

    // ------------------------------------------------------------------
    // DedupeConsecutiveBreadcrumbs — collapse redundant duplicate crumbs
    // ------------------------------------------------------------------

    [Fact]
    public void DedupeConsecutiveBreadcrumbs_CollapsesIdenticalContainerNeighbors_KeepsLater()
    {
        // Two identical consecutive container crumbs (e.g. a Locate-in-GWorld
        // path-synthetic SpawnedAttributes(C) + the user re-entering it) collapse
        // to one — keeping the LATER crumb (it has the live ContainerField).
        var cf = new LiveFieldValue
        {
            Name = "SpawnedAttributes", TypeName = "ArrayProperty",
            ArrayInnerType = "ObjectProperty", ArrayCount = 3,
        };
        var list = new List<BreadcrumbItem>
        {
            MakeBc("0x5E1BD8A0", "ASC", "AbilitySystemComponent", isPointer: true, offset: 0x7E0),
            new BreadcrumbItem { Address = "0x5E1BD8A0", Label = "SpawnedAttributes", FieldName = "SpawnedAttributes", FieldOffset = 0x10A8, IsContainerView = true },                      // synthetic, no ContainerField
            new BreadcrumbItem { Address = "0x5E1BD8A0", Label = "SpawnedAttributes", FieldName = "SpawnedAttributes", FieldOffset = 0x10A8, IsContainerView = true, ContainerField = cf }, // real
        };

        var result = CeXmlExportService.DedupeConsecutiveBreadcrumbs(list);

        Assert.Equal(2, result.Count);
        Assert.Equal("AbilitySystemComponent", result[0].FieldName);
        Assert.Equal("SpawnedAttributes", result[1].FieldName);
        Assert.NotNull(result[1].ContainerField);  // kept the later (richer) crumb
    }

    [Fact]
    public void DedupeConsecutiveBreadcrumbs_KeepsDistinctSplitCrumbs()
    {
        // The PathStepToBreadcrumbs split (container + element) must NOT be
        // collapsed — they differ by name/address/kind.
        var list = new List<BreadcrumbItem>
        {
            new BreadcrumbItem { Address = "0x3AB399520", FieldName = "PlayerArray", FieldOffset = 0x2E0, IsContainerView = true },
            new BreadcrumbItem { Address = "0x3AB399C40", FieldName = "[0]", FieldOffset = 0x0, IsPointerDeref = true },
        };
        var result = CeXmlExportService.DedupeConsecutiveBreadcrumbs(list);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GenerateHierarchicalXml_DuplicateConsecutiveContainerCrumbs_EmitsSingleDeref()
    {
        // A spine carrying two identical consecutive container crumbs must collapse
        // to ONE CE deref level (CleanBreadcrumbs dedups), not double-deref +10A8.
        var breadcrumbs = new List<BreadcrumbItem>
        {
            MakeBc("0x1000", "Root"),
            new BreadcrumbItem { Address = "0x5000", Label = "ASC", FieldName = "ASC", FieldOffset = 0x7E0, IsPointerDeref = true },
            new BreadcrumbItem { Address = "0x6000", Label = "Spawned", FieldName = "Spawned", FieldOffset = 0x10A8, IsContainerView = true },
            new BreadcrumbItem { Address = "0x6000", Label = "Spawned", FieldName = "Spawned", FieldOffset = 0x10A8, IsContainerView = true },
        };
        var fields = new List<LiveFieldValue>
            { new() { Name = "X", TypeName = "FloatProperty", Offset = 0x10, Size = 4 } };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"g.exe\"+1000", "Root", breadcrumbs, fields);

        Assert.Equal(1, CountOccurrences(xml, "<Address>+10A8</Address>"));
    }

    // Deep distinct chain (the Gundam SEED repro) must pass through the dedup +
    // clean passes UNTOUCHED — guards DedupeConsecutiveBreadcrumbs / CleanBreadcrumbs
    // against over-collapsing a legitimate deeply-nested export.
    private static List<BreadcrumbItem> SeedDeepChain() => new()
    {
        MakeBc("0x100", "GWorld", "GWorld", isPointer: true),
        new BreadcrumbItem { Address = "0x200", FieldName = "OwningGameInstance",  FieldOffset = 0x180, IsPointerDeref = true },
        new BreadcrumbItem { Address = "0x300", FieldName = "m_savedata",          FieldOffset = 0x2A8, IsPointerDeref = true },
        new BreadcrumbItem { Address = "0x400", FieldName = "SaveSlotList",        FieldOffset = 0x7D0, IsContainerView = true },
        new BreadcrumbItem { Address = "0x500", FieldName = "[1]",                 FieldOffset = 0x6F8, IsPointerDeref = true },
        new BreadcrumbItem { Address = "0x600", FieldName = "MsTuneData.MsTunes",  FieldOffset = 0x0,   IsContainerView = true },
        new BreadcrumbItem { Address = "0x700", FieldName = "[0]",                 FieldOffset = 0x8,   IsPointerDeref = true },
        new BreadcrumbItem { Address = "0x800", FieldName = "WeaponTuneList",      FieldOffset = 0x18,  IsContainerView = true },
    };

    [Fact]
    public void DedupeConsecutiveBreadcrumbs_DeepDistinctChain_Unchanged()
    {
        var chain = SeedDeepChain();
        var result = CeXmlExportService.DedupeConsecutiveBreadcrumbs(chain);
        Assert.Equal(chain.Count, result.Count);
        for (int i = 0; i < chain.Count; i++)
            Assert.Equal(chain[i].FieldName, result[i].FieldName);
    }

    [Fact]
    public void CleanBreadcrumbs_DeepDistinctChain_PreservesAllLevels()
    {
        var chain = SeedDeepChain();
        var result = CeXmlExportService.CleanBreadcrumbs(chain);
        Assert.Equal(chain.Count, result.Count);
    }

    // --- Back-nav re-hydration of path-synthetic container crumbs (follow-up b) ---
    //
    // PathStepToBreadcrumbs splits a Locate-in-GWorld object-pointer-array hop into a
    // container crumb whose ContainerField is null (the GWorld path step carries no
    // TArray::Data base / element count / resolved element list). Back-nav onto such a
    // crumb must LAZILY re-walk the parent object and re-populate the ARRAY ELEMENT
    // view — not fall through to the parent object's field grid (the pre-fix
    // mis-render). These tests drive that through the real Back-nav commands.

    private const string SynthParentAddr = "0x1000";
    private const int SynthArrayOffset = 0x10A8;

    private static StubDumpService MakeStubWithArrayParent()
    {
        var dump = new StubDumpService();
        dump.RegisterStruct(SynthParentAddr, new InstanceWalkResult
        {
            Name = "MyActor",
            ClassName = "ACharacter",
            Address = SynthParentAddr,
            Fields = new List<LiveFieldValue>
            {
                // Parent-grid signature field — must NOT surface if re-hydration worked.
                new() { Name = "SomeOtherField", Offset = 0x10, TypeName = "IntProperty" },
                // The object-pointer array the synthetic crumb refers to.
                new()
                {
                    Name = "SpawnedAttributes",
                    Offset = SynthArrayOffset,
                    TypeName = "ArrayProperty",
                    ArrayCount = 2,
                    ArrayInnerType = "ObjectProperty",
                    ArrayElemSize = 8,
                    ArrayDataAddr = "0x5000",
                    ArrayElements = new List<ArrayElementValue>
                    {
                        new() { Index = 0, PtrAddress = "0x6000", PtrName = "CharacterAttributeSet", PtrClassName = "AttributeSet" },
                        new() { Index = 1, PtrAddress = "0x6010", PtrName = "OtherAttributeSet", PtrClassName = "AttributeSet" },
                    },
                },
            },
        });
        return dump;
    }

    private static LiveWalkerViewModel MakeVm(StubDumpService dump)
        => new LiveWalkerViewModel(dump, new MockLoggingService(),
                                   new MockPlatformService(System.IO.Path.GetTempPath()));

    private static BreadcrumbItem SyntheticArrayContainerCrumb()
        => new BreadcrumbItem
        {
            Address = SynthParentAddr,
            Label = "SpawnedAttributes",
            FieldName = "SpawnedAttributes",
            FieldOffset = SynthArrayOffset,
            IsContainerView = true,
            ContainerField = null,   // the path-synthetic hallmark
        };

    [Fact]
    public async Task NavigateToBreadcrumb_SyntheticContainerCrumb_RehydratesContainerView()
    {
        var vm = MakeVm(MakeStubWithArrayParent());
        var container = SyntheticArrayContainerCrumb();
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = SynthParentAddr, Label = "MyActor", FieldName = "MyActor" });
        vm.Breadcrumbs.Add(container);
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = "0x6000", Label = "[0]", FieldName = "[0]", IsPointerDeref = true });

        await vm.NavigateToBreadcrumbCommand.ExecuteAsync(container);

        // Array element view (NOT the parent object grid).
        Assert.Equal("Array<ObjectProperty>", vm.CurrentClassName);
        Assert.Contains(vm.Fields, f => f.Name == "[0]" && f.PtrName == "CharacterAttributeSet");
        Assert.Contains(vm.Fields, f => f.Name == "[1]");
        Assert.DoesNotContain(vm.Fields, f => f.Name == "SomeOtherField"); // parent-grid signature
        Assert.Equal(2, vm.Breadcrumbs.Count);                             // trailing element crumb truncated
    }

    [Fact]
    public async Task GoBack_SyntheticContainerCrumb_RehydratesContainerView()
    {
        var vm = MakeVm(MakeStubWithArrayParent());
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = SynthParentAddr, Label = "MyActor", FieldName = "MyActor" });
        vm.Breadcrumbs.Add(SyntheticArrayContainerCrumb());
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = "0x6000", Label = "[0]", FieldName = "[0]", IsPointerDeref = true });

        await vm.GoBackCommand.ExecuteAsync(null);

        // GoBack pops the element crumb, landing on the synthetic container crumb,
        // which must re-hydrate the array element view rather than the parent grid.
        Assert.Equal("Array<ObjectProperty>", vm.CurrentClassName);
        Assert.Contains(vm.Fields, f => f.Name == "[0]");
        Assert.DoesNotContain(vm.Fields, f => f.Name == "SomeOtherField");
    }

    [Fact]
    public async Task NavigateToBreadcrumb_SyntheticContainerCrumb_NoLiveMatch_FallsThroughToReWalk()
    {
        // If the parent re-walk no longer contains the array field (e.g. memory moved),
        // the helper returns false and the existing parent re-walk renders the object grid.
        var dump = new StubDumpService();
        dump.RegisterStruct(SynthParentAddr, new InstanceWalkResult
        {
            Name = "MyActor", ClassName = "ACharacter", Address = SynthParentAddr,
            Fields = new List<LiveFieldValue> { new() { Name = "SomeOtherField", Offset = 0x10, TypeName = "IntProperty" } },
        });
        var vm = MakeVm(dump);
        var container = SyntheticArrayContainerCrumb();
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = SynthParentAddr, Label = "MyActor", FieldName = "MyActor" });
        vm.Breadcrumbs.Add(container);

        await vm.NavigateToBreadcrumbCommand.ExecuteAsync(container);

        // Graceful degradation: parent object grid, no exception.
        Assert.Contains(vm.Fields, f => f.Name == "SomeOtherField");
        Assert.NotEqual("Array<ObjectProperty>", vm.CurrentClassName);
    }

    [Fact]
    public async Task Refresh_SyntheticContainerCrumb_KeepsContainerView()
    {
        // Auto-refresh / Refresh while viewing a re-hydrated synthetic container must
        // re-walk the parent and keep the array element view — not revert to a grid by
        // re-walking the stale CurrentAddress.
        var vm = MakeVm(MakeStubWithArrayParent());
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = SynthParentAddr, Label = "MyActor", FieldName = "MyActor" });
        vm.Breadcrumbs.Add(SyntheticArrayContainerCrumb());
        vm.CurrentAddress = "0x6000";   // stale deeper address; refresh must NOT render it as a grid
        vm.HasData = true;

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("Array<ObjectProperty>", vm.CurrentClassName);
        Assert.Contains(vm.Fields, f => f.Name == "[0]");
        Assert.DoesNotContain(vm.Fields, f => f.Name == "SomeOtherField");
    }

    [Fact]
    public async Task LoadBookmark_SyntheticContainerCrumb_RehydratesContainerView()
    {
        // A bookmark saved while viewing a re-hydrated synthetic container must restore
        // the array element view on load — not the parent object grid (4th re-display site).
        var vm = MakeVm(MakeStubWithArrayParent());
        var slot = vm.BookmarkSlots[0];
        slot.SavedBreadcrumbs = new List<BreadcrumbItem>
        {
            new() { Address = SynthParentAddr, Label = "MyActor", FieldName = "MyActor" },
            SyntheticArrayContainerCrumb(),
        };
        slot.SavedAddress = SynthParentAddr;
        slot.IsOccupied = true;

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        Assert.Equal("Array<ObjectProperty>", vm.CurrentClassName);
        Assert.Contains(vm.Fields, f => f.Name == "[0]");
        Assert.DoesNotContain(vm.Fields, f => f.Name == "SomeOtherField");
    }

    [Fact]
    public async Task GoBack_AtRoot_PreBookmarkRestore_SyntheticContainer_Rehydrates()
    {
        // Covers the 3rd wiring site: GoBack-at-root pre-bookmark restore branch.
        var vm = MakeVm(MakeStubWithArrayParent());

        // Pre-bookmark state ends on a synthetic container crumb.
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = SynthParentAddr, Label = "MyActor", FieldName = "MyActor" });
        vm.Breadcrumbs.Add(SyntheticArrayContainerCrumb());
        vm.CurrentAddress = SynthParentAddr;

        // Load a single-crumb bookmark — captures the pre-bookmark trail and leaves
        // Breadcrumbs.Count == 1, arming the Back-at-root restore branch.
        var slot = vm.BookmarkSlots[0];
        slot.SavedBreadcrumbs = new List<BreadcrumbItem>
        {
            new() { Address = "0x2000", Label = "Other", FieldName = "Other" },
        };
        slot.SavedAddress = "0x2000";
        slot.IsOccupied = true;
        await vm.LoadBookmarkCommand.ExecuteAsync(slot);
        Assert.Single(vm.Breadcrumbs);

        // Back at root restores the pre-bookmark trail; its tail (synthetic container)
        // must re-hydrate the array element view.
        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.Equal("Array<ObjectProperty>", vm.CurrentClassName);
        Assert.Contains(vm.Fields, f => f.Name == "[0]");
        Assert.DoesNotContain(vm.Fields, f => f.Name == "SomeOtherField");
    }

    [Fact]
    public async Task NavigateToBreadcrumb_SyntheticContainer_FieldFoundButNotPopulatable_FallsThrough()
    {
        // willRepopulate==false branch: the field matches by name+offset but is no longer
        // a populatable container (e.g. the array emptied to count 0 between scan and
        // back-nav) → fall through to the parent grid rather than a stale/empty view.
        var dump = new StubDumpService();
        dump.RegisterStruct(SynthParentAddr, new InstanceWalkResult
        {
            Name = "MyActor", ClassName = "ACharacter", Address = SynthParentAddr,
            Fields = new List<LiveFieldValue>
            {
                new()
                {
                    Name = "SpawnedAttributes", Offset = SynthArrayOffset,
                    TypeName = "ArrayProperty",
                    ArrayCount = 0, ArrayInnerType = "ObjectProperty",   // present but empty
                },
            },
        });
        var vm = MakeVm(dump);
        var container = SyntheticArrayContainerCrumb();
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = SynthParentAddr, Label = "MyActor", FieldName = "MyActor" });
        vm.Breadcrumbs.Add(container);

        await vm.NavigateToBreadcrumbCommand.ExecuteAsync(container);

        Assert.NotEqual("Array<ObjectProperty>", vm.CurrentClassName);
        Assert.Contains(vm.Fields, f => f.Name == "SpawnedAttributes");
    }

    private static int CountOccurrences(string source, string substring)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }
}
