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
        Assert.Single(result);
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

    [Fact]
    public void GenerateHierarchicalXml_GuessedFields_ExportWhenIncludeGuessed()
    {
        // A UScriptStruct with no reflected UPROPERTY fields (e.g.
        // CustomAbilityEffectDuration) is shown as "Guess What" raw fields
        // (Ubel::GuessGapTypes) with confidence-suffixed type labels and
        // IsGuessed=true. When the user explicitly FOCUSES a guessed field and does
        // Copy CE Field, the export passes includeGuessed=true and each guessed
        // scalar maps to its canonical CE VariableType and is emitted.
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "Root"),
            MakeBc("0x2000", "Buff", "BuffItemEffects", isPointer: true, offset: 0xD48),
        };

        var fields = new[]
        {
            new LiveFieldValue { Name = "i32@0",   TypeName = "Int32?",  Offset = 0x0, Size = 4, IsGuessed = true, TypedValue = "12345" },
            new LiveFieldValue { Name = "float@8", TypeName = "Float?",  Offset = 0x8, Size = 4, IsGuessed = true, TypedValue = "180" },
            new LiveFieldValue { Name = "float@C", TypeName = "Float",   Offset = 0xC, Size = 4, IsGuessed = true, TypedValue = "0.8164" },
            new LiveFieldValue { Name = "dbl@10",  TypeName = "Double?", Offset = 0x10, Size = 8, IsGuessed = true, TypedValue = "1.5" },
            new LiveFieldValue { Name = "byte@18", TypeName = "Byte?",   Offset = 0x18, Size = 1, IsGuessed = true, TypedValue = "3" },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"TestGame.exe\"+1000", "Root", breadcrumbs, fields, includeGuessed: true);

        // Each guessed scalar maps to its canonical CE VariableType and is emitted.
        Assert.Contains("<VariableType>Float</VariableType>", xml);
        Assert.Contains("<VariableType>4 Bytes</VariableType>", xml);
        Assert.Contains("<VariableType>Double</VariableType>", xml);
        Assert.Contains("<VariableType>Byte</VariableType>", xml);
        Assert.Contains("float@8", xml);
        Assert.Contains("i32@0", xml);
        Assert.Contains("byte@18", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_GuessedFields_SkippedByDefault()
    {
        // Bulk exports (Copy CE XML, and Copy CE Field on a struct/container/reflected
        // field) default to includeGuessed=false, so speculative guessed rows are NOT
        // dumped — only genuine reflected fields are emitted. Otherwise a struct export
        // would silently scatter a pile of Guess? fields the user never asked for.
        var breadcrumbs = new[] { MakeBc("0x1000", "Root") };
        var fields = new[]
        {
            new LiveFieldValue { Name = "RealHP",  TypeName = "FloatProperty", Offset = 0x0, Size = 4, TypedValue = "100" },
            new LiveFieldValue { Name = "i32@8",   TypeName = "Int32?", Offset = 0x8, Size = 4, IsGuessed = true, TypedValue = "12345" },
            new LiveFieldValue { Name = "float@C", TypeName = "Float?", Offset = 0xC, Size = 4, IsGuessed = true, TypedValue = "180" },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"TestGame.exe\"+1000", "Root", breadcrumbs, fields);  // includeGuessed defaults false

        Assert.Contains("RealHP", xml);       // reflected field emitted
        Assert.DoesNotContain("i32@8", xml);  // guessed rows dropped
        Assert.DoesNotContain("float@C", xml);
    }

    [Fact]
    public void MapFieldToCeRecordType_GuessedFloat_IsSingle()
    {
        // The same guessed-label mapping must drive the AOBMaker single-record push
        // (MapFieldToCeRecordType reuses MapCeField), so a guessed Float? row pushed
        // to CE lands as a Single, not a fallback 8-byte pointer.
        var floatRec = CeXmlExportService.MapFieldToCeRecordType(
            new LiveFieldValue { Name = "float@8", TypeName = "Float?", Offset = 0x8, Size = 4, IsGuessed = true });
        Assert.Equal(4, floatRec.ValueType); // CeVtSingle
        Assert.False(floatRec.ShowAsHex);

        var i32Rec = CeXmlExportService.MapFieldToCeRecordType(
            new LiveFieldValue { Name = "i32@0", TypeName = "Int32?", Offset = 0x0, Size = 4, IsGuessed = true });
        Assert.Equal(2, i32Rec.ValueType); // CeVtDword
        Assert.True(i32Rec.IsSigned);
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

        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0xABC"] = new()
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
        // Struct group description is now the bare field name (no struct-type suffix)
        Assert.Contains("\"Attributes\"", xml);
        Assert.DoesNotContain("Attributes (FGameplayAttributeData)", xml);
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
        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0xABC"] = new()
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

    // ===== GAS attribute flatten (Live Walker "Flatten GAS attributes" option) =====
    // U+25B8 ▸ is the segment separator written by EmitFlattenedStruct.

    /// <summary>Build a UAttributeSet-shaped object: two FGameplayAttributeData members
    /// (HealthPoint @0x30, MaxHealthPoint @0x40), each {float BaseValue @0x8, float
    /// CurrentValue @0xC} — the exact GAS shape from the Elliot CharacterAttributeSet.</summary>
    private static (LiveFieldValue[] fields, Dictionary<string, List<LiveFieldValue>> structs)
        BuildGasAttributeSet(string structTypeName = "GameplayAttributeData")
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "HealthPoint", TypeName = "StructProperty", Offset = 0x30, Size = 0x10,
                StructDataAddr = "0xH", StructClassAddr = "0xHC", StructTypeName = structTypeName
            },
            new LiveFieldValue
            {
                Name = "MaxHealthPoint", TypeName = "StructProperty", Offset = 0x40, Size = 0x10,
                StructDataAddr = "0xM", StructClassAddr = "0xMC", StructTypeName = structTypeName
            },
        };
        List<LiveFieldValue> AttrChildren() => new()
        {
            new LiveFieldValue { Name = "BaseValue", TypeName = "FloatProperty", Offset = 0x8, Size = 4 },
            new LiveFieldValue { Name = "CurrentValue", TypeName = "FloatProperty", Offset = 0xC, Size = 4 },
        };
        var structs = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0xH"] = AttrChildren(),
            ["0xM"] = AttrChildren(),
        };
        return (fields, structs);
    }

    [Fact]
    public void GenerateInstanceXml_FlattenGas_PromotesChildrenAtCombinedOffset()
    {
        var sep = "▸";
        var (fields, structs) = BuildGasAttributeSet();   // live spelling, no 'F' prefix

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "CharacterAttributeSet", "UCharacterAttributeSet",
            fields, structs, flattenGasAttributes: true);

        // Children promoted to "Struct ▸ Child" sibling leaves…
        Assert.Contains($"HealthPoint {sep} BaseValue", xml);
        Assert.Contains($"HealthPoint {sep} CurrentValue", xml);
        Assert.Contains($"MaxHealthPoint {sep} BaseValue", xml);
        // …at the COMBINED offset (struct member + child-in-struct): 0x30+8=0x38, 0x30+C=0x3C, 0x40+8=0x48.
        Assert.Contains("<Address>+38</Address>", xml);
        Assert.Contains("<Address>+3C</Address>", xml);
        Assert.Contains("<Address>+48</Address>", xml);
        Assert.Contains("<VariableType>Float</VariableType>", xml);
        // The intermediate parent group + bare child entries are gone.
        Assert.DoesNotContain("<Description>\"HealthPoint\"</Description>", xml);
        Assert.DoesNotContain("<Description>\"BaseValue\"</Description>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_FlattenGasOff_KeepsNestedAttributeGroup()
    {
        var sep = "▸";
        var (fields, structs) = BuildGasAttributeSet();

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "CharacterAttributeSet", "UCharacterAttributeSet",
            fields, structs);   // flattenGasAttributes defaults false

        // Default: nested parent group + BaseValue/CurrentValue children, no flatten separator.
        Assert.Contains("<Description>\"HealthPoint\"</Description>", xml);
        Assert.Contains("<Description>\"BaseValue\"</Description>", xml);
        Assert.DoesNotContain(sep, xml);
        // Child offset stays struct-relative (+8), NOT combined (+38).
        Assert.Contains("<Address>+8</Address>", xml);
        Assert.DoesNotContain("<Address>+38</Address>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_FlattenGas_AcceptsFPrefixedSpelling()
    {
        var sep = "▸";
        var (fields, structs) = BuildGasAttributeSet("FGameplayAttributeData");

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "AttrSet", "UAttrSet", fields, structs, flattenGasAttributes: true);

        Assert.Contains($"HealthPoint {sep} BaseValue", xml);
        Assert.Contains("<Address>+38</Address>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_FlattenGas_NonGasStructKeepsGroup()
    {
        var sep = "▸";
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Transform", TypeName = "StructProperty", Offset = 0x100, Size = 0x30,
                StructDataAddr = "0xT", StructClassAddr = "0xTC", StructTypeName = "Transform"
            },
        };
        var structs = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0xT"] = new()
            {
                new LiveFieldValue { Name = "Location.X", TypeName = "FloatProperty", Offset = 0x0, Size = 4 },
                new LiveFieldValue { Name = "Location.Y", TypeName = "FloatProperty", Offset = 0x4, Size = 4 },
            }
        };

        // Flatten ON, but the struct is NOT a GAS attribute → it must stay a nested group.
        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, structs, flattenGasAttributes: true);

        Assert.Contains("<Description>\"Transform\"</Description>", xml);
        Assert.DoesNotContain(sep, xml);
    }

    [Fact]
    public void GenerateInstanceXml_FlattenGas_OffsetAndTypeAnnotations()
    {
        var sep = "▸";
        var (fields, structs) = BuildGasAttributeSet();

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "AttrSet", "UAttrSet", fields, structs,
            descShowOffset: true, descShowType: true, flattenGasAttributes: true);

        // +Offset annotates BOTH segments; +Type appends the struct type ONCE at the very end.
        Assert.Contains($"HealthPoint (30) {sep} BaseValue (8) (GameplayAttributeData)", xml);
        // The type must NOT be repeated mid-name (i.e. not on the first segment).
        Assert.DoesNotContain($"(GameplayAttributeData) {sep}", xml);
    }

    // ===== Primitive-leaf struct flatten (Live Walker "Flatten primitive-leaf structs") =====
    // Superset of the GAS flatten: ANY terminal struct whose whole subtree is primitive inline
    // scalars (int/float/bool/enum) flattens; a pointer / string / container descendant blocks it.

    /// <summary>Build a one-StructProperty object: a non-GAS struct <paramref name="structType"/>
    /// at +0x50 whose children are supplied by <paramref name="children"/> (offsets struct-relative).</summary>
    private static (LiveFieldValue[] fields, Dictionary<string, List<LiveFieldValue>> structs)
        BuildLeafStruct(string structType, List<LiveFieldValue> children)
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Range", TypeName = "StructProperty", Offset = 0x50, Size = 0x10,
                StructDataAddr = "0xS", StructClassAddr = "0xSC", StructTypeName = structType
            },
        };
        var structs = new Dictionary<string, List<LiveFieldValue>> { ["0xS"] = children };
        return (fields, structs);
    }

    [Fact]
    public void GenerateInstanceXml_FlattenLeafStructs_PromotesAllPrimitiveStruct()
    {
        var sep = "▸";
        var (fields, structs) = BuildLeafStruct("StatRange", new()
        {
            new LiveFieldValue { Name = "Min", TypeName = "FloatProperty", Offset = 0x0, Size = 4 },
            new LiveFieldValue { Name = "Max", TypeName = "IntProperty",   Offset = 0x4, Size = 4 },
        });

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, structs, flattenLeafStructs: true);

        // Children promoted to "Range ▸ Child" leaves at the COMBINED offset (0x50+0, 0x50+4).
        Assert.Contains($"Range {sep} Min", xml);
        Assert.Contains($"Range {sep} Max", xml);
        Assert.Contains("<Address>+50</Address>", xml);
        Assert.Contains("<Address>+54</Address>", xml);
        Assert.Contains("<VariableType>Float</VariableType>", xml);
        Assert.Contains("<VariableType>4 Bytes</VariableType>", xml);
        // The intermediate parent group is gone.
        Assert.DoesNotContain("<Description>\"Range\"</Description>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_FlattenLeafStructs_FlattensVectorStruct()
    {
        var sep = "▸";
        var (fields, structs) = BuildLeafStruct("Vector", new()
        {
            new LiveFieldValue { Name = "X", TypeName = "FloatProperty", Offset = 0x0, Size = 4 },
            new LiveFieldValue { Name = "Y", TypeName = "FloatProperty", Offset = 0x4, Size = 4 },
            new LiveFieldValue { Name = "Z", TypeName = "FloatProperty", Offset = 0x8, Size = 4 },
        });

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, structs, flattenLeafStructs: true);

        Assert.Contains($"Range {sep} X", xml);
        Assert.Contains($"Range {sep} Z", xml);
        Assert.Contains("<Address>+58</Address>", xml);   // 0x50 + 0x8
        Assert.DoesNotContain("<Description>\"Range\"</Description>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_FlattenLeafStructs_AlsoFlattensGasStruct()
    {
        // The leaf-struct option is a SUPERSET — a GAS attribute is a {float,float} primitive
        // leaf, so it flattens even with the dedicated GAS toggle OFF.
        var sep = "▸";
        var (fields, structs) = BuildGasAttributeSet();

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "AttrSet", "UAttrSet", fields, structs,
            flattenGasAttributes: false, flattenLeafStructs: true);

        Assert.Contains($"HealthPoint {sep} BaseValue", xml);
        Assert.Contains("<Address>+38</Address>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_FlattenLeafStructs_KeepsStructWithPointer()
    {
        var sep = "▸";
        var (fields, structs) = BuildLeafStruct("Holder", new()
        {
            new LiveFieldValue { Name = "Count", TypeName = "IntProperty", Offset = 0x0, Size = 4 },
            // A pointer descendant must keep the struct grouped ("pointers stay unflattened").
            new LiveFieldValue
            {
                Name = "Owner", TypeName = "ObjectProperty", Offset = 0x8, Size = 8,
                PtrAddress = "0x0", PtrClassName = "AActor"
            },
        });

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, structs, flattenLeafStructs: true);

        // Group kept (Description = the field name "Range"); no flatten separator.
        Assert.Contains("<Description>\"Range\"</Description>", xml);
        Assert.DoesNotContain(sep, xml);
    }

    [Fact]
    public void GenerateInstanceXml_FlattenLeafStructs_KeepsStructWithString()
    {
        var sep = "▸";
        var (fields, structs) = BuildLeafStruct("Named", new()
        {
            new LiveFieldValue { Name = "Level", TypeName = "IntProperty", Offset = 0x0, Size = 4 },
            // A string (pointer-backed FString) descendant blocks the flatten.
            new LiveFieldValue { Name = "Label", TypeName = "StrProperty", Offset = 0x8, Size = 16 },
        });

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, structs, flattenLeafStructs: true);

        // Group kept (Description = the field name "Range"); no flatten separator.
        Assert.Contains("<Description>\"Range\"</Description>", xml);
        Assert.DoesNotContain(sep, xml);
    }

    [Fact]
    public void GenerateInstanceXml_FlattenLeafStructsOff_KeepsGroup()
    {
        var sep = "▸";
        var (fields, structs) = BuildLeafStruct("StatRange", new()
        {
            new LiveFieldValue { Name = "Min", TypeName = "FloatProperty", Offset = 0x0, Size = 4 },
            new LiveFieldValue { Name = "Max", TypeName = "FloatProperty", Offset = 0x4, Size = 4 },
        });

        // Default off (neither flatten toggle) → the all-primitive struct stays a nested group.
        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, structs);

        Assert.Contains("<Description>\"Range\"</Description>", xml);
        Assert.DoesNotContain(sep, xml);
        Assert.Contains("<Address>+50</Address>", xml);   // struct group at its own offset
        Assert.DoesNotContain("<Address>+54</Address>", xml);
    }

    // ===== Leaf-record flatten (Live Walker "Flatten leaf records (names/strings)") =====
    // Superset of the primitive-leaf flatten: ALSO accepts NameProperty + the FString family as
    // leaf children, so a save-data "record" struct flattens fully. A name child renders as a
    // 4-byte int; an FString child renders as a CE String leaf with Offsets=[0] (one Data deref).

    /// <summary>A record struct {int Score, enum Rank, FName MsID, FString PilotName} mirroring the
    /// SEED LifeStoryMissionRecord shape (offsets struct-relative; struct sits at +0x50).</summary>
    private static (LiveFieldValue[] fields, Dictionary<string, List<LiveFieldValue>> structs)
        BuildRecordStruct() => BuildLeafStruct("LifeStoryMissionRecord", new()
        {
            new LiveFieldValue { Name = "Score",     TypeName = "IntProperty",  Offset = 0x0, Size = 4 },
            new LiveFieldValue { Name = "Rank",      TypeName = "EnumProperty", Offset = 0x4, Size = 1 },
            new LiveFieldValue { Name = "MsID",      TypeName = "NameProperty", Offset = 0x8, Size = 8 },
            new LiveFieldValue { Name = "PilotName", TypeName = "StrProperty",  Offset = 0x10, Size = 16 },
        });

    [Fact]
    public void GenerateInstanceXml_FlattenLeafRecords_FlattensRecordWithNameAndString()
    {
        var sep = "▸";
        var (fields, structs) = BuildRecordStruct();

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, structs, flattenLeafRecords: true);

        // Every field promoted to a "Range ▸ Field" sibling leaf — no parent group.
        Assert.Contains($"Range {sep} Score", xml);
        Assert.Contains($"Range {sep} Rank", xml);
        Assert.Contains($"Range {sep} MsID", xml);
        Assert.Contains($"Range {sep} PilotName", xml);
        Assert.DoesNotContain("<Description>\"Range\"</Description>", xml);

        // Scalar at the COMBINED offset (0x50 + childOffset), 0-deref.
        Assert.Contains("<Address>+50</Address>", xml);   // Score  (0x50 + 0x0)
        Assert.Contains("<Address>+58</Address>", xml);   // MsID   (0x50 + 0x8)
        // FName renders as a 4-byte int leaf (no Offsets / deref).
        Assert.Contains("<VariableType>4 Bytes</VariableType>", xml);

        // FString child renders as a CE String leaf at the combined offset (0x50 + 0x10 = 0x60)
        // WITH Offsets=[0] (one deref of the inline FString.Data pointer) — the headline 1-deref case.
        Assert.Contains("<Address>+60</Address>", xml);
        Assert.Contains("<VariableType>String</VariableType>", xml);
        Assert.Matches(
            @"<Address>\+60</Address>\s*<Offsets>\s*<Offset>0</Offset>\s*</Offsets>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_StringLeaf_DefaultsToLength256()
    {
        // No ceStringLength passed → the CE String leaf keeps the historic 256-char window.
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "PlayerName", TypeName = "StrProperty", Offset = 0x30, Size = 16 },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        Assert.Contains("<VariableType>String</VariableType>", xml);
        Assert.Contains("<Length>256</Length>", xml);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(1024)]
    public void GenerateInstanceXml_StringLeaf_HonorsCeStringLength(int length)
    {
        // The "String Length" export option flows through to the CE String leaf's <Length>.
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "PlayerName", TypeName = "StrProperty", Offset = 0x30, Size = 16 },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, ceStringLength: length);

        Assert.Contains($"<Length>{length}</Length>", xml);
        Assert.DoesNotContain("<Length>256</Length>", xml.Replace($"<Length>{length}</Length>", ""));
    }

    [Fact]
    public void GenerateInstanceXml_FlattenLeafRecordsOff_KeepsRecordGroup()
    {
        var sep = "▸";
        var (fields, structs) = BuildRecordStruct();

        // Neither flatten toggle → the record stays a nested group.
        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, structs);

        Assert.Contains("<Description>\"Range\"</Description>", xml);
        Assert.DoesNotContain(sep, xml);
    }

    [Fact]
    public void GenerateInstanceXml_FlattenLeafStructs_DoesNotFlattenRecordWithNameString()
    {
        // REGRESSION: the OLD primitive-leaf toggle must NOT flatten a record carrying FName /
        // FString fields — only the new leaf-records toggle widens the gate. Guards that the
        // IsPrimitiveLeafField predicate stayed narrow.
        var sep = "▸";
        var (fields, structs) = BuildRecordStruct();

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, structs, flattenLeafStructs: true);

        Assert.Contains("<Description>\"Range\"</Description>", xml);
        Assert.DoesNotContain(sep, xml);
    }

    [Fact]
    public void GenerateInstanceXml_FlattenLeafRecords_StillKeepsStructWithPointer()
    {
        // Pointers are NOT terminal leaves — a pointer descendant keeps the struct grouped even
        // under the wider record gate.
        var sep = "▸";
        var (fields, structs) = BuildLeafStruct("Holder", new()
        {
            new LiveFieldValue { Name = "MsID", TypeName = "NameProperty", Offset = 0x0, Size = 8 },
            new LiveFieldValue
            {
                Name = "Owner", TypeName = "ObjectProperty", Offset = 0x8, Size = 8,
                PtrAddress = "0x0", PtrClassName = "AActor"
            },
        });

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, structs, flattenLeafRecords: true);

        Assert.Contains("<Description>\"Range\"</Description>", xml);
        Assert.DoesNotContain(sep, xml);
    }

    [Fact]
    public void GenerateInstanceXml_FlattenLeafRecords_KeepsStructWithFText()
    {
        // FText is deliberately EXCLUDED from the flattenable leaf set (its internal chain has no
        // clean CE encoding), so a TextProperty descendant keeps the struct grouped.
        var sep = "▸";
        var (fields, structs) = BuildLeafStruct("Captioned", new()
        {
            new LiveFieldValue { Name = "Score", TypeName = "IntProperty",  Offset = 0x0, Size = 4 },
            new LiveFieldValue { Name = "Title", TypeName = "TextProperty", Offset = 0x8, Size = 24 },
        });

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, structs, flattenLeafRecords: true);

        Assert.Contains("<Description>\"Range\"</Description>", xml);
        Assert.DoesNotContain(sep, xml);
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

        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0xABC"] = new()
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

        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0xABC"] = new()
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
    public void GenerateInstanceXml_ObjectProperty_EmitsLeafWith8BytesNotGroupHeader()
    {
        // Regression: a single ObjectProperty field used to fall through to
        // EmitNavigableField -> EmitGroupPlaceholder, producing a useless
        // <GroupHeader>1</GroupHeader> entry with NO <VariableType> — CE
        // rendered it as an empty folder. Now it must emit as a proper
        // 8-byte ShowAsHex leaf so CE can read the pointer value.
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "ScalabilityModifiers", TypeName = "ObjectProperty",
                Offset = 0x2C8, Size = 8,
                PtrAddress = "0x7FF453509278",
                PtrName = "ScalabilityModifiers",
                PtrClassName = "MapScalabilityModifierComponent",
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Outer wrapper for the instance is still a GroupHeader, but the
        // ScalabilityModifiers entry itself must be a leaf — extract just
        // its <CheatEntry> block to assert against.
        var scStart = xml.IndexOf("\"ScalabilityModifiers\"", StringComparison.Ordinal);
        Assert.True(scStart >= 0, "ScalabilityModifiers entry missing from XML");
        var leafEnd = xml.IndexOf("</CheatEntry>", scStart, StringComparison.Ordinal);
        var leafBlock = xml.Substring(scStart, leafEnd - scStart);

        Assert.Contains("<VariableType>8 Bytes</VariableType>", leafBlock);
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", leafBlock);
        Assert.Contains("<Address>+2C8</Address>", leafBlock);
        Assert.DoesNotContain("<GroupHeader>1</GroupHeader>", leafBlock);
        Assert.DoesNotContain("<CheatEntries>", leafBlock); // leaf has no children
    }

    [Fact]
    public void GenerateInstanceXml_ClassProperty_EmitsLeafWith8BytesNotGroupHeader()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "DefaultPawnClass", TypeName = "ClassProperty",
                Offset = 0x40, Size = 8,
                PtrAddress = "0x7FF000123456",
                PtrName = "Pawn",
                PtrClassName = "Class",
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        var scStart = xml.IndexOf("\"DefaultPawnClass\"", StringComparison.Ordinal);
        Assert.True(scStart >= 0);
        var leafEnd = xml.IndexOf("</CheatEntry>", scStart, StringComparison.Ordinal);
        var leafBlock = xml.Substring(scStart, leafEnd - scStart);

        Assert.Contains("<VariableType>8 Bytes</VariableType>", leafBlock);
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", leafBlock);
        Assert.DoesNotContain("<GroupHeader>1</GroupHeader>", leafBlock);
    }

    [Fact]
    public void GenerateInstanceXml_WeakObjectProperty_EmitsLeafWith8BytesNotGroupHeader()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "WeakOwner", TypeName = "WeakObjectProperty",
                Offset = 0x18, Size = 8,
                PtrAddress = "0x7FF000ABCDEF",
                PtrName = "OwnerActor",
                PtrClassName = "Actor",
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        var scStart = xml.IndexOf("\"WeakOwner\"", StringComparison.Ordinal);
        Assert.True(scStart >= 0);
        var leafEnd = xml.IndexOf("</CheatEntry>", scStart, StringComparison.Ordinal);
        var leafBlock = xml.Substring(scStart, leafEnd - scStart);

        Assert.Contains("<VariableType>8 Bytes</VariableType>", leafBlock);
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", leafBlock);
        Assert.DoesNotContain("<GroupHeader>1</GroupHeader>", leafBlock);
    }

    [Fact]
    public void GenerateInstanceXml_ObjectProperty_WithResolvedInstance_EmitsGroupHeaderWithChildren()
    {
        // ObjectProperty pre-resolved by ResolvePointerInstancesAsync should emit
        // as GroupHeader + Offsets=[0] + the target's fields as children — so CE
        // dereferences *(parent + 0x2C8) and lays the children out at their
        // natural offsets within MapScalabilityModifierComponent.
        const string ptrAddr = "0x7FF453509278";
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "ScalabilityModifiers", TypeName = "ObjectProperty",
                Offset = 0x2C8, Size = 8,
                PtrAddress = ptrAddr,
                PtrName = "ScalabilityModifiers",
                PtrClassName = "MapScalabilityModifierComponent",
            },
        };

        var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>
        {
            [ptrAddr] = new()
            {
                new LiveFieldValue { Name = "Health", TypeName = "FloatProperty", Offset = 0x40, Size = 4 },
                new LiveFieldValue { Name = "TickInterval", TypeName = "FloatProperty", Offset = 0xA0, Size = 4 },
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields,
            resolvedInstances: resolvedInstances);

        // Locate the ScalabilityModifiers entry block (description is now the bare name)
        var scStart = xml.IndexOf("\"ScalabilityModifiers\"",
            StringComparison.Ordinal);
        Assert.True(scStart >= 0, "Drilled ScalabilityModifiers entry missing");
        Assert.DoesNotContain("ScalabilityModifiers (MapScalabilityModifierComponent)", xml);

        // The drilled entry must be a GroupHeader with Offsets=[0]
        var headerEnd = xml.IndexOf("<CheatEntries>", scStart, StringComparison.Ordinal);
        Assert.True(headerEnd > scStart, "Drilled entry must open <CheatEntries>");
        var headerBlock = xml.Substring(scStart, headerEnd - scStart);
        Assert.Contains("<GroupHeader>1</GroupHeader>", headerBlock);
        Assert.Contains("<Address>+2C8</Address>", headerBlock);
        Assert.Contains("<Offset>0</Offset>", headerBlock);

        // Children should appear with their natural offsets relative to the
        // dereferenced target (NOT the parent instance + 0x2C8). Use scalar
        // fields the leaf emitter knows how to render.
        Assert.Contains("\"Health\"", xml);
        Assert.Contains("<Address>+40</Address>", xml);
        Assert.Contains("\"TickInterval\"", xml);
        Assert.Contains("<Address>+A0</Address>", xml);
        Assert.Contains("<VariableType>Float</VariableType>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_ObjectArray_Fabricate_ExtendsAndFillsWithTemplate()
    {
        // Copy CE Field fabricate: a TArray<Object*> with 2 walked slots (one live Item,
        // one null) and fabricateArrayCount=5 → 5 element rows. The live element's field
        // layout is the template; the null slot and the extended slots [2..4] replicate it,
        // so the CE table already has item rows a later save will populate.
        const string elem0Ptr = "0x1E727ADC10";
        var field = new LiveFieldValue
        {
            Name = "Cargo", TypeName = "ArrayProperty", ArrayInnerType = "ObjectProperty",
            ArrayCount = 2, ArrayElemSize = 8, Offset = 0xD8, ArrayDataAddr = "0x2000",
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, PtrAddress = elem0Ptr, PtrName = "Item_1", PtrClassName = "Item" },
                new() { Index = 1, PtrAddress = "0x0" }, // null slot within Num
            },
        };
        var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>
        {
            [elem0Ptr] = new()
            {
                new LiveFieldValue { Name = "ItemLevel", TypeName = "IntProperty", Offset = 0x58, Size = 4 },
                new LiveFieldValue { Name = "Amount", TypeName = "IntProperty", Offset = 0x9C, Size = 4 },
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "Inventory", "Inventory",
            new[] { field }, resolvedInstances: resolvedInstances, fabricateArrayCount: 5);

        // Extended past Num=2: element [4] exists at +20 (4 * 8).
        Assert.Contains("\"[4]\"", xml);
        Assert.Contains("<Address>+20</Address>", xml);
        // Null slot [1] is no longer a flat 8-byte leaf — it got the template group.
        Assert.Contains("\"[1]\"", xml);
        // Every slot (real [0] + fabricated [1..4]) carries the template's fields → 5 each.
        int itemLevel = System.Text.RegularExpressions.Regex.Matches(xml, "\"ItemLevel\"").Count;
        Assert.True(itemLevel >= 4, $"expected the template ItemLevel on multiple slots, got {itemLevel}");
        Assert.Contains("<Address>+58</Address>", xml); // template field offset preserved
    }

    [Fact]
    public void GenerateInstanceXml_Fabricate_NextLayerOnly_NestedArraysNotFabricated()
    {
        // "Next layer only": fabricate the SELECTED top-level array, but NOT arrays reached by
        // drilling THROUGH an object pointer. Otherwise a deep element template (an item's own
        // Mods/Attributes arrays) is fabricated to the target count at every nesting level,
        // exploding the output (256 items × 256 mods × …). Regression for the in-game 500k-line
        // Cargo blow-up.
        const string item0 = "0x1A000", mod0 = "0x1AAA0";
        var mods = new LiveFieldValue
        {
            Name = "Mods", TypeName = "ArrayProperty", ArrayInnerType = "ObjectProperty",
            ArrayCount = 1, ArrayElemSize = 8, Offset = 0x50, ArrayDataAddr = "0x1A050",
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, PtrAddress = mod0, PtrClassName = "Mod" },
            },
        };
        var items = new LiveFieldValue
        {
            Name = "Items", TypeName = "ArrayProperty", ArrayInnerType = "ObjectProperty",
            ArrayCount = 1, ArrayElemSize = 8, Offset = 0x10, ArrayDataAddr = "0x19000",
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, PtrAddress = item0, PtrClassName = "Item" },
            },
        };
        var resolved = new Dictionary<string, List<LiveFieldValue>>
        {
            [item0] = new() { mods },
            [mod0] = new() { new LiveFieldValue { Name = "Power", TypeName = "IntProperty", Offset = 0x8, Size = 4 } },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "Inv", "Inv", new[] { items },
            resolvedInstances: resolved, dedupShared: true, fabricateArrayCount: 8);

        // TOP array Items IS fabricated (walked=1 → 8 rows): element [7] exists.
        Assert.Contains("\"[7]\"", xml);
        // NESTED Mods array is NOT fabricated: it stays at its walked count (1). If it had
        // been fabricated, each of the 8 items would carry ~8 Mod slots → an explosion of
        // "Power" rows. With the gate, only the one real Mod expands (others dedup/absent).
        int powerRows = System.Text.RegularExpressions.Regex.Matches(xml, "\"Power\"").Count;
        Assert.True(powerRows <= 2, $"nested array was fabricated (explosion): {powerRows} Power rows");
    }

    [Fact]
    public void GenerateInstanceXml_ScalarArray_Fabricate_ExtendsLeaves()
    {
        var field = new LiveFieldValue
        {
            Name = "Counts", TypeName = "ArrayProperty", ArrayInnerType = "IntProperty",
            ArrayCount = 2, ArrayElemSize = 4, Offset = 0x20,
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, Value = "10" },
                new() { Index = 1, Value = "20" },
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "Obj", "Obj", new[] { field }, fabricateArrayCount: 4);

        // Extended to 4: [2] @ +8, [3] @ +C.
        Assert.Contains("\"[2]\"", xml);
        Assert.Contains("\"[3]\"", xml);
        Assert.Contains("<Address>+8</Address>", xml);
        Assert.Contains("<Address>+C</Address>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_Array_NoFabricateByDefault()
    {
        // fabricateArrayCount defaults to 0 (off) — Copy CE XML and any un-opted export
        // never pad the array.
        var field = new LiveFieldValue
        {
            Name = "Counts", TypeName = "ArrayProperty", ArrayInnerType = "IntProperty",
            ArrayCount = 2, ArrayElemSize = 4, Offset = 0x20,
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, Value = "10" },
                new() { Index = 1, Value = "20" },
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "Obj", "Obj", new[] { field });

        Assert.Contains("\"[1]\"", xml);
        Assert.DoesNotContain("\"[2]\"", xml);
    }

    [Fact]
    public void GenerateInstanceXml_ObjectArray_WithResolvedElement_DrillsElementGroup()
    {
        // Array<ObjectProperty> (e.g. SpawnedAttributes): exporting it with a
        // resolved element pointer must drill that element into a GroupHeader
        // (Offsets=[0] derefs the 8-byte element pointer) + the target's fields —
        // not a flat 8-byte leaf. Regression for the "Copy CE Field doesn't drill
        // an object-array element" bug.
        const string elem2Ptr = "0x1E727ADC10";
        var field = new LiveFieldValue
        {
            Name = "SpawnedAttributes", TypeName = "ArrayProperty",
            ArrayInnerType = "ObjectProperty",
            ArrayCount = 3, ArrayElemSize = 8, Offset = 0x10A8,
            ArrayDataAddr = "0x1E727ADC00",
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, PtrAddress = "0x1E040000", PtrName = "ShieldAttributeSet", PtrClassName = "ShieldAttributeSet" },
                new() { Index = 1, PtrAddress = "0x1E5F0000", PtrName = "ExtraAttributeSet",  PtrClassName = "ExtraAttributeSet" },
                new() { Index = 2, PtrAddress = elem2Ptr,     PtrName = "CharacterAttributeSet", PtrClassName = "CharacterAttributeSet" },
            },
        };
        var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>
        {
            [elem2Ptr] = new()
            {
                new LiveFieldValue { Name = "HealthPoint", TypeName = "FloatProperty", Offset = 0x30, Size = 4 },
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "BP_PlayerCharacter_C", "BP_PlayerCharacter_C",
            new[] { field }, resolvedInstances: resolvedInstances);

        // Element [2] must drill: GroupHeader + Address=+10 (index 2 * 8) + Offsets=[0].
        // Description is now the bare index "[2]" (instance name AND class dropped).
        var elemStart = xml.IndexOf("\"[2]\"", StringComparison.Ordinal);
        Assert.True(elemStart >= 0, "Element [2] entry missing");
        var headerEnd = xml.IndexOf("<CheatEntries>", elemStart, StringComparison.Ordinal);
        Assert.True(headerEnd > elemStart, "Element [2] must open <CheatEntries> (drilled group)");
        var headerBlock = xml.Substring(elemStart, headerEnd - elemStart);
        Assert.Contains("<GroupHeader>1</GroupHeader>", headerBlock);
        Assert.Contains("<Address>+10</Address>", headerBlock);
        Assert.Contains("<Offset>0</Offset>", headerBlock);
        // The instance name and class are no longer baked into the description.
        Assert.DoesNotContain("[2] CharacterAttributeSet", xml);
        // The target's child field appears at its natural offset within the element.
        Assert.Contains("\"HealthPoint\"", xml);
        Assert.Contains("<Address>+30</Address>", xml);
        Assert.Contains("<VariableType>Float</VariableType>", xml);
        // Unresolved sibling [0] stays a flat 8-byte leaf — bare index, no class suffix.
        Assert.Contains("\"[0]\"", xml);
        Assert.DoesNotContain("[0] ShieldAttributeSet", xml);
    }

    [Fact]
    public void GenerateInstanceXml_WeakObjectArray_ResolvedElement_DoesNotDrill()
    {
        // Regression guard: a WeakObjectProperty array slot is FWeakObjectPtr
        // {int32 ObjectIndex, int32 SerialNumber} — NOT a raw UObject*. Even when
        // the DLL resolves an element to a live object (PtrAddress in
        // resolvedInstances), drilling it (Offsets=[0]) would tell CE to deref a
        // non-pointer → garbage address. Only ObjectProperty/ClassProperty arrays
        // may drill; Weak/Soft/Lazy must keep their existing leaf handling.
        const string elemPtr = "0x1E727ADC10";
        var field = new LiveFieldValue
        {
            Name = "WeakRefs", TypeName = "ArrayProperty",
            ArrayInnerType = "WeakObjectProperty",
            ArrayCount = 1, ArrayElemSize = 8, Offset = 0x40,
            ArrayDataAddr = "0x1E727AD000",
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, PtrAddress = elemPtr, PtrName = "SomeActor", PtrClassName = "AActor" },
            },
        };
        var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>
        {
            [elemPtr] = new() { new LiveFieldValue { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4 } },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass",
            new[] { field }, resolvedInstances: resolvedInstances);

        // Must NOT drill the resolved target's fields into the weak-array element.
        Assert.DoesNotContain("\"Health\"", xml);
        Assert.DoesNotContain("<Address>+10</Address>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_ObjectProperty_NoMatchingResolvedInstance_FallsBackToFlatLeaf()
    {
        // resolvedInstances has different PtrAddress → no match → should fall
        // through to the standard 8 Bytes leaf path (after the build 534 fix).
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Lookup", TypeName = "ObjectProperty",
                Offset = 0x100, Size = 8,
                PtrAddress = "0x7FF000000000",
                PtrName = "Target",
                PtrClassName = "UTarget",
            },
        };

        var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0x7FFDEADBEEF0"] = new() // mismatched address
            {
                new LiveFieldValue { Name = "X", TypeName = "FloatProperty", Offset = 0, Size = 4 },
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields,
            resolvedInstances: resolvedInstances);

        var entryStart = xml.IndexOf("\"Lookup\"", StringComparison.Ordinal);
        var entryEnd = xml.IndexOf("</CheatEntry>", entryStart, StringComparison.Ordinal);
        var entryBlock = xml.Substring(entryStart, entryEnd - entryStart);

        Assert.Contains("<VariableType>8 Bytes</VariableType>", entryBlock);
        Assert.DoesNotContain("<GroupHeader>1</GroupHeader>", entryBlock);
        Assert.DoesNotContain("<CheatEntries>", entryBlock);
        // The mismatched-address entry should NOT have leaked in
        Assert.DoesNotContain("\"X\"", entryBlock);
    }

    [Fact]
    public void GenerateInstanceXml_ObjectProperty_ResolvedInstance_PreservesScalarLeafShape()
    {
        // Drilled-pointer children of common scalar types must still emit as
        // proper leaves (with VariableType, no GroupHeader) — this is the
        // most common shape inside any UObject target.
        const string ptrAddr = "0x7FF000000000";
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Comp", TypeName = "ObjectProperty",
                Offset = 0x40, Size = 8,
                PtrAddress = ptrAddr, PtrClassName = "UComponent",
            },
        };

        var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>
        {
            [ptrAddr] = new()
            {
                new LiveFieldValue { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4 },
                new LiveFieldValue { Name = "Mana", TypeName = "IntProperty", Offset = 0x14, Size = 4 },
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields,
            resolvedInstances: resolvedInstances);

        Assert.Contains("\"Health\"", xml);
        Assert.Contains("<VariableType>Float</VariableType>", xml);
        Assert.Contains("<Address>+10</Address>", xml);
        Assert.Contains("\"Mana\"", xml);
        Assert.Contains("<VariableType>4 Bytes</VariableType>", xml);
        Assert.Contains("<Address>+14</Address>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_DrilledPointer_NestedStructProperty_ExpandsViaResolvedStructsCascade()
    {
        // Reproduces the build-541 regression: drilling into ScalabilityModifiers
        // (ObjectProperty -> MapScalabilityModifierComponent) the inner
        // PrimaryComponentTick (StructProperty) used to render as an empty
        // GroupHeader placeholder because resolvedStructs was keyed by Offset
        // and only built for the root instance's struct fields. With the
        // cascade fix, struct fields inside drilled targets share the same
        // dict (keyed by StructDataAddr) and expand to real sub-fields.
        const string ptrAddr = "0x7FF453509278";
        const string structDataAddr = "0x7FF4535092A8"; // PrimaryComponentTick within the target
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "ScalabilityModifiers", TypeName = "ObjectProperty",
                Offset = 0x2C8, Size = 8,
                PtrAddress = ptrAddr,
                PtrClassName = "MapScalabilityModifierComponent",
            },
        };

        // Drilled target's children include a StructProperty
        var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>
        {
            [ptrAddr] = new()
            {
                new LiveFieldValue
                {
                    Name = "PrimaryComponentTick", TypeName = "StructProperty",
                    Offset = 0x30, Size = 0x30,
                    StructDataAddr = structDataAddr,
                    StructClassAddr = "0xDEADBEEF",
                    StructTypeName = "ActorComponentTickFunction",
                },
            },
        };

        // resolvedStructs has the cascaded entry for the inner StructProperty.
        // Without this, the StructProperty would render as a placeholder.
        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>
        {
            [structDataAddr] = new()
            {
                new LiveFieldValue { Name = "TickGroup", TypeName = "ByteProperty", Offset = 0x8, Size = 1 },
                new LiveFieldValue { Name = "TickInterval", TypeName = "FloatProperty", Offset = 0xC, Size = 4 },
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields,
            resolvedStructs: resolvedStructs,
            resolvedInstances: resolvedInstances);

        // Drilled pointer group exists (description is now the bare field name)
        Assert.Contains("\"ScalabilityModifiers\"", xml);
        Assert.DoesNotContain("ScalabilityModifiers (MapScalabilityModifierComponent)", xml);
        // Inner struct group exists (bare field name, no struct-type suffix)
        Assert.Contains("\"PrimaryComponentTick\"", xml);
        Assert.DoesNotContain("PrimaryComponentTick (ActorComponentTickFunction)", xml);
        // Inner struct's sub-fields rendered as real leaves (regression: would
        // have been a GroupHeader placeholder without the cascade fix)
        Assert.Contains("\"TickGroup\"", xml);
        Assert.Contains("\"TickInterval\"", xml);
        Assert.Contains("<VariableType>Byte</VariableType>", xml);
        Assert.Contains("<VariableType>Float</VariableType>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_OptionalProperty_NoStructInner_EmitsFlatLeaf()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "OptionalScalar", TypeName = "OptionalProperty",
                Offset = 0xA0, Size = 8,
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        var entryStart = xml.IndexOf("\"OptionalScalar\"", StringComparison.Ordinal);
        Assert.True(entryStart >= 0);
        var entryEnd = xml.IndexOf("</CheatEntry>", entryStart, StringComparison.Ordinal);
        var block = xml.Substring(entryStart, entryEnd - entryStart);

        Assert.Contains("<VariableType>8 Bytes</VariableType>", block);
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", block);
        Assert.Contains("<Address>+A0</Address>", block);
        Assert.DoesNotContain("<GroupHeader>1</GroupHeader>", block);
    }

    [Fact]
    public void GenerateInstanceXml_OptionalProperty_StructInner_ExpandsToStructGroup()
    {
        // OptionalProperty<FBox> with structDataAddr/structClassAddr stamped
        // by the walker — should render as the struct's sub-fields, NOT the
        // 8-byte hex fallback.
        const string structDataAddr = "0x7FF400000A0";
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "CellBounds", TypeName = "OptionalProperty",
                Offset = 0x88, Size = 0x40,
                StructDataAddr = structDataAddr,
                StructClassAddr = "0xDEAD",
                StructTypeName = "Box",
            },
        };

        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>
        {
            [structDataAddr] = new()
            {
                new LiveFieldValue { Name = "Min.X", TypeName = "DoubleProperty", Offset = 0, Size = 8 },
                new LiveFieldValue { Name = "Min.Y", TypeName = "DoubleProperty", Offset = 8, Size = 8 },
                new LiveFieldValue { Name = "IsValid", TypeName = "BoolProperty", Offset = 0x30, Size = 1 },
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields,
            resolvedStructs: resolvedStructs);

        // Struct group description is now the bare field name (no struct-type suffix)
        Assert.Contains("\"CellBounds\"", xml);
        Assert.DoesNotContain("CellBounds (Box)", xml);
        Assert.Contains("\"Min.X\"", xml);
        Assert.Contains("\"Min.Y\"", xml);
        Assert.Contains("\"IsValid\"", xml);
        Assert.Contains("<VariableType>Double</VariableType>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_NullObjectProperty_StillEmitsLeaf()
    {
        // ObjectProperty with no resolved target (null pointer) — IsNavigable
        // is false, but the field still needs an addressable entry so the
        // user can watch the pointer slot fill in later.
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "OptionalRef", TypeName = "ObjectProperty",
                Offset = 0x80, Size = 8,
                // No PtrAddress -> IsNavigable = false in the old code path
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        var scStart = xml.IndexOf("\"OptionalRef\"", StringComparison.Ordinal);
        Assert.True(scStart >= 0, "OptionalRef entry missing from XML");
        var leafEnd = xml.IndexOf("</CheatEntry>", scStart, StringComparison.Ordinal);
        var leafBlock = xml.Substring(scStart, leafEnd - scStart);

        Assert.Contains("<VariableType>8 Bytes</VariableType>", leafBlock);
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", leafBlock);
        Assert.DoesNotContain("<GroupHeader>1</GroupHeader>", leafBlock);
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

        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0xABC"] = new()
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
        // Struct group description is now the bare field name (no struct-type suffix)
        Assert.Contains("\"Stats\"", xml);
        Assert.DoesNotContain("Stats (FStats)", xml);
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

        // Group header description is now the bare field name (no array descriptor)
        Assert.Contains("\"DamageMultipliers\"", xml);
        Assert.DoesNotContain("[3 x FloatProperty", xml);
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

        // Placeholder description is now the bare field name (no array descriptor)
        Assert.Contains("\"Levels\"", xml);
        Assert.DoesNotContain("[5 x ObjectProperty", xml);
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
        // Description is now the bare field name (no array descriptor)
        Assert.Contains("\"Scores\"", xml);
        Assert.DoesNotContain("[2 x IntProperty", xml);
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

        // Header description is now the bare field name (full count no longer baked in)
        Assert.Contains("\"BigArray\"", xml);
        Assert.DoesNotContain("[100 x FloatProperty", xml);
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

        // Group header description is now the bare field name (no array descriptor)
        Assert.Contains("\"Levels\"", xml);
        Assert.DoesNotContain("[3 x ObjectProperty", xml);
        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);
        // Array group: Address=+80, Offsets=[0] (deref TArray.Data)
        Assert.Contains("<Address>+80</Address>", xml);
        Assert.Contains("<Offset>0</Offset>", xml);
        // Elements: bare index only (instance name + class dropped)
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("\"[1]\"", xml);
        Assert.Contains("\"[2]\"", xml); // null element, no name
        Assert.DoesNotContain("[0] PersistentLevel", xml);
        Assert.DoesNotContain("[1] SubLevel_01", xml);
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

        // Placeholder description is now the bare field name (no array descriptor)
        Assert.Contains("\"BigPtrArr\"", xml);
        Assert.DoesNotContain("[200 x ObjectProperty", xml);
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
        // Array group description is now the bare field name (no array descriptor)
        Assert.Contains("\"Components\"", xml);
        Assert.DoesNotContain("[2 x ObjectProperty", xml);
        Assert.Contains("<Address>+50</Address>", xml);
        // Elements: bare index only (instance name + class dropped)
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("\"[1]\"", xml);
        Assert.DoesNotContain("[0] MeshComp", xml);
        Assert.DoesNotContain("[1] CollisionComp", xml);
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

        // Group header description is now the bare field name (no array descriptor)
        Assert.Contains("\"WeakRefs\"", xml);
        Assert.DoesNotContain("[2 x WeakObjectProperty", xml);
        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);
        // Array group: Address=+90, Offsets=[0] (deref TArray.Data)
        Assert.Contains("<Address>+90</Address>", xml);
        Assert.Contains("<Offset>0</Offset>", xml);
        // Elements: bare index only (resolved name dropped)
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("\"[1]\"", xml); // stale, no name
        Assert.DoesNotContain("[0] PlayerChar", xml);
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

        // Array group header description is now the bare field name (no array descriptor)
        Assert.Contains("\"Positions\"", xml);
        Assert.DoesNotContain("[2 x Vector", xml);
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
        // Parent description is now the bare field name (no array descriptor)
        Assert.Contains("\"ShipTypes\"", xml);
        Assert.DoesNotContain("[3 x ByteProperty", xml);
        // Link key equals the (now bare) parent Description
        Assert.Contains("<DropDownListLink>ShipTypes</DropDownListLink>", xml);
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
        // (now the bare field name).
        var firstParentDesc = "StarterShips";
        Assert.DoesNotContain("StarterShips [2 x ByteProperty", xml);
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
        // Parent description is now the bare field name (no array descriptor)
        var parentDesc = "Locations";
        Assert.Contains("\"Locations\"", xml);
        Assert.DoesNotContain("Locations [3 x NameProperty", xml);
        // Link key equals the (now bare) parent Description
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
        // Descriptions are now the bare field name; the array descriptor is gone.
        Assert.DoesNotContain("Tags [2 x NameProperty", xml);
        // First array: bare name; second array: suffixed for uniqueness (collision)
        Assert.Contains("\"Tags\"", xml);
        Assert.Contains("\"Tags.001\"", xml);
        // Children of second array link to the suffixed parent
        Assert.Contains("<DropDownListLink>Tags.001</DropDownListLink>", xml);
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
        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0xABC"] = new()
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

        // Struct group exists; description is now the bare field name (no struct type)
        Assert.Contains("\"JobScore\"", xml);
        Assert.DoesNotContain("JobScore (FJobScore)", xml);
        Assert.Contains("CurrentRank", xml);
        // Array inside struct should be fully expanded (NOT a placeholder); the
        // description is now the bare field name (no array descriptor).
        Assert.Contains("\"ClaimedRewards\"", xml);
        Assert.DoesNotContain("[3 x IntProperty", xml);
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

        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0xABC"] = new()
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

        // Struct group exists; description is now the bare field name (no struct type)
        Assert.Contains("\"Settings\"", xml);
        Assert.DoesNotContain("Settings (FSettings)", xml);
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
        // Array group description is now the bare field name (no array descriptor)
        Assert.Contains("\"Scores\"", xml);
        Assert.DoesNotContain("[3 x IntProperty", xml);
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

        // Map group description is now the bare field name (no map descriptor)
        Assert.Contains("\"ScoreMap\"", xml);
        Assert.DoesNotContain("{Map: 2", xml);
        // Map group should have Offsets=[0] for TSparseArray.Data deref
        Assert.Contains("<Offset>0</Offset>", xml);
        // Stride = ComputeSetElementStride(4+4) = AlignUp(8,4)+8 = 16
        // Element at index 0: offset = 0*16 = 0
        Assert.Contains("<Address>+0</Address>", xml);
        // Element at index 3: offset = 3*16 = 48 = 0x30
        Assert.Contains("<Address>+30</Address>", xml);
        // Key at +0, Value at +keySize (4) — leaves are label-only (the dynamic
        // value is NOT baked into the description).
        Assert.Contains("\"Key\"", xml);
        Assert.Contains("\"Value\"", xml);
        Assert.DoesNotContain("Key: 42", xml);
        Assert.DoesNotContain("Value: 3.14", xml);
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
    public void GenerateInstanceXml_NonScalarMapKey_StillExpandsScalarValue()
    {
        // Map<Struct, Int>: a non-scalar KEY no longer collapses the whole map to a
        // placeholder (docs/ce-export-drilldown-spec.md). The map derefs its Data and
        // the scalar VALUE expands; the struct key just isn't emitted as a Key leaf.
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "StructMap", TypeName = "MapProperty", Offset = 0x80, Size = 0x50,
                MapCount = 3, MapKeyType = "StructProperty", MapValueType = "IntProperty",
                MapKeySize = 16, MapValueSize = 4, MapValueOffset = 16, MapDataAddr = "0x5000",
                MapElements = new List<ContainerElementValue>
                {
                    new() { Index = 0, Key = "(struct)", Value = "10" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Map group derefs TSparseArray::Data (Offsets=[0]) and expands the element.
        Assert.Contains("<Offset>0</Offset>", xml);
        Assert.Contains("[0] (struct)", xml);
        // Scalar Int value leaf is present (label only); the struct key gets no leaf.
        Assert.Contains("\"Value\"", xml);
        Assert.DoesNotContain("Value: 10", xml);
        Assert.DoesNotContain("Key:", xml);
    }

    [Fact]
    public void MapNameValue_UsesDropDownListNotBakedName()
    {
        // Map<Enum, Name>: the value column must NOT bake the resolved name into the
        // description; instead a DropDownList (rawInt → name) on the map group lets CE
        // show the live name. (The PlayerSelectMsUnitList shape.)
        var map = new LiveFieldValue
        {
            Name = "PlayerSelectMsUnitList", TypeName = "MapProperty", Offset = 0x100, Size = 0x50,
            MapCount = 2, MapKeyType = "EnumProperty", MapValueType = "NameProperty",
            MapKeySize = 1, MapValueSize = 8, MapValueOffset = 4, MapDataAddr = "0x6000",
            MapElements = new List<ContainerElementValue>
            {
                // ValueHex first 4 bytes (LE) = FName ComparisonIndex.
                new() { Index = 0, Key = "0", Value = "ms_stdag", ValueHex = "A4F2130000000000" },
                new() { Index = 1, Key = "1", Value = "bes_off",  ValueHex = "0ABC320000000000" },
            }
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"game.exe\"+1000", "Obj", "Cls", new List<LiveFieldValue> { map });

        // DropDownList present with rawInt:name entries (0x0013F2A4 = 1307812).
        Assert.Contains("<DropDownList", xml);
        Assert.Contains(":ms_stdag", xml);
        Assert.Contains(":bes_off", xml);
        // Value leaf is label-only — the dynamic name is NOT in the description.
        Assert.Contains("\"Value\"", xml);
        Assert.DoesNotContain("Value: ms_stdag", xml);
        // The leaves link to the shared list.
        Assert.Contains("<DropDownListLink>", xml);
    }

    [Fact]
    public void MapValueStruct_ExpandsWithResolvedFields()
    {
        // Map<Name, Struct> (the MissionInfoList shape): when the value struct is
        // resolved, each element's value expands to the struct's fields — the core
        // of the drilldown contract (was a placeholder before).
        var map = new LiveFieldValue
        {
            Name = "MissionInfoList", TypeName = "MapProperty", Offset = 0x2F8, Size = 0x50,
            MapCount = 1, MapKeyType = "NameProperty", MapValueType = "StructProperty",
            MapKeySize = 8, MapValueSize = 0x10, MapValueOffset = 8, MapDataAddr = "0x4000",
            MapValueStructAddr = "0xABC", MapValueStructType = "LifeMissionInfo",
            MapElements = new List<ContainerElementValue>
            {
                new() { Index = 0, Key = "mc1om_001", Value = "" },
            }
        };

        // Value addr for element 0 = MapDataAddr + 0*stride + valOffset = 0x4000 + 8 = 0x4008.
        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0x4008"] = new()
            {
                new() { Name = "Cleared", TypeName = "IntProperty", Offset = 0x0, Size = 4 },
                new() { Name = "Rank", TypeName = "IntProperty", Offset = 0x4, Size = 4 },
            }
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "Obj", "Cls",
            new List<LiveFieldValue> { map }, resolvedStructs);

        // Map group description is now the bare field name (no map descriptor)
        Assert.Contains("\"MissionInfoList\"", xml);
        Assert.DoesNotContain("{Map: 1", xml);
        Assert.Contains("[0] mc1om_001", xml);   // scalar/string key kept
        // The value struct's own fields are emitted (the whole point).
        Assert.Contains("\"Rank\"", xml);
        Assert.Contains("\"Cleared\"", xml);
    }

    [Fact]
    public void MapValueRecord_FlattenLeafRecords_CollapsesPerElementGroup()
    {
        // The SEED StoryMissionRecord shape: TMap<FName, record-struct>. With Flatten leaf records
        // on, the per-element [i] group collapses too — the Key and the value record's fields become
        // flat "[i] key ▸ Field" siblings at the COMBINED offset (element base + value offset + field
        // offset), instead of an [i] folder wrapping a Value sub-group.
        var map = new LiveFieldValue
        {
            Name = "StoryMissionRecord", TypeName = "MapProperty", Offset = 0x100, Size = 0x50,
            MapCount = 1, MapKeyType = "NameProperty", MapValueType = "StructProperty",
            MapKeySize = 8, MapValueSize = 0x30, MapValueOffset = 8, MapDataAddr = "0x4000",
            MapValueStructAddr = "0xABC", MapValueStructType = "LifeStoryMissionRecord",
            MapElements = new List<ContainerElementValue> { new() { Index = 0, Key = "mc1om_001" } },
        };
        // Element 0 value struct addr = MapDataAddr + 0*stride + valOffset = 0x4000 + 8 = 0x4008.
        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0x4008"] = new()
            {
                new() { Name = "Score",     TypeName = "IntProperty",  Offset = 0x0,  Size = 4 },
                new() { Name = "MsID",      TypeName = "NameProperty", Offset = 0x8,  Size = 8 },
                new() { Name = "PilotName", TypeName = "StrProperty",  Offset = 0x10, Size = 16 },
            }
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "Obj", "Cls",
            new List<LiveFieldValue> { map }, resolvedStructs, flattenLeafRecords: true);

        var sep = "▸";
        // Flat "[0] mc1om_001 ▸ Field" siblings — including the Key.
        Assert.Contains($"[0] mc1om_001 {sep} Key", xml);
        Assert.Contains($"[0] mc1om_001 {sep} Score", xml);
        Assert.Contains($"[0] mc1om_001 {sep} MsID", xml);
        Assert.Contains($"[0] mc1om_001 {sep} PilotName", xml);
        // The per-element group HEADER is gone (no bare "[0] mc1om_001" Description, no "Value" group).
        Assert.DoesNotContain("<Description>\"[0] mc1om_001\"</Description>", xml);
        Assert.DoesNotContain($"Value {sep}", xml);

        // Combined offsets relative to the map's dereferenced Data pointer:
        //   Key   = elemBase(0) + 0          = +0
        //   Score = elemBase(0) + valOff(8)  = +8
        //   MsID  = +8 + 0x8                 = +10
        Assert.Contains("<Address>+8</Address>", xml);
        Assert.Contains("<Address>+10</Address>", xml);
        // PilotName: combined +18 (0 + 8 + 0x10) rendered as a CE String with Offsets=[0] — the
        // FString.Data deref still resolves correctly THROUGH the collapsed map element.
        Assert.Contains("<VariableType>String</VariableType>", xml);
        Assert.Matches(
            @"<Address>\+18</Address>\s*<Offsets>\s*<Offset>0</Offset>\s*</Offsets>", xml);
    }

    /// <summary>A 2-element record map for the alternating-colour tests (stride =
    /// ((valOff 8 + size 0x30 + 3) &amp; ~3) + 8 = 0x40; value structs at 0x4008 / 0x4048).</summary>
    private static (LiveFieldValue map, Dictionary<string, List<LiveFieldValue>> structs)
        BuildTwoElementRecordMap()
    {
        var map = new LiveFieldValue
        {
            Name = "StoryMissionRecord", TypeName = "MapProperty", Offset = 0x100, Size = 0x50,
            MapCount = 2, MapKeyType = "NameProperty", MapValueType = "StructProperty",
            MapKeySize = 8, MapValueSize = 0x30, MapValueOffset = 8, MapDataAddr = "0x4000",
            MapValueStructAddr = "0xABC", MapValueStructType = "LifeStoryMissionRecord",
            MapElements = new List<ContainerElementValue>
            {
                new() { Index = 0, Key = "mc1om_001" },
                new() { Index = 1, Key = "mc1om_002" },
            },
        };
        static List<LiveFieldValue> Rec() => new()
        {
            new() { Name = "Score",     TypeName = "IntProperty", Offset = 0x0,  Size = 4 },
            new() { Name = "PilotName", TypeName = "StrProperty", Offset = 0x10, Size = 16 },
        };
        var structs = new Dictionary<string, List<LiveFieldValue>> { ["0x4008"] = Rec(), ["0x4048"] = Rec() };
        return (map, structs);
    }

    [Fact]
    public void MapValueRecord_AltColor_TintsRowsByParity()
    {
        var (map, structs) = BuildTwoElementRecordMap();

        // Even = RGB 0080FF (azure, the requested default) → CE COLORREF FF8000;
        // Odd  = RGB FF0000 (red)                         → CE COLORREF 0000FF.
        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "Obj", "Cls", new List<LiveFieldValue> { map }, structs,
            flattenLeafRecords: true,
            altColorEnabled: true, altRowColorEvenRgb: "0080FF", altRowColorOddRgb: "FF0000");

        // RGB → CE COLORREF byte-swap (RRGGBB → BBGGRR).
        Assert.Contains("<Color>FF8000</Color>", xml);
        Assert.Contains("<Color>0000FF</Color>", xml);
        // Parity is correct: the even element's row carries the even colour, the odd element's the
        // odd colour — checked WITHIN the same CheatEntry (no </CheatEntry> between Description and Color).
        Assert.Matches(
            @"\[0\] mc1om_001 ▸ Score""</Description>(?:(?!</CheatEntry>)[\s\S])*?<Color>FF8000</Color>", xml);
        Assert.Matches(
            @"\[1\] mc1om_002 ▸ Score""</Description>(?:(?!</CheatEntry>)[\s\S])*?<Color>0000FF</Color>", xml);
    }

    [Fact]
    public void MapValueRecord_AltColor_OddUnset_OnlyEvenTinted()
    {
        var (map, structs) = BuildTwoElementRecordMap();

        // The default shape: Even = azure, Odd = unset (no colour → CE theme).
        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "Obj", "Cls", new List<LiveFieldValue> { map }, structs,
            flattenLeafRecords: true,
            altColorEnabled: true, altRowColorEvenRgb: "0080FF", altRowColorOddRgb: null);

        Assert.Contains("<Color>FF8000</Color>", xml);          // even tinted
        // The odd element's rows carry no <Color> at all.
        Assert.DoesNotMatch(
            @"\[1\] mc1om_002 ▸ Score""</Description>(?:(?!</CheatEntry>)[\s\S])*?<Color>", xml);
    }

    [Fact]
    public void MapValueRecord_AltColorDisabled_NoColorEmitted()
    {
        var (map, structs) = BuildTwoElementRecordMap();

        // Colours configured but the feature is OFF → not a single <Color>.
        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "Obj", "Cls", new List<LiveFieldValue> { map }, structs,
            flattenLeafRecords: true,
            altColorEnabled: false, altRowColorEvenRgb: "0080FF", altRowColorOddRgb: "FF0000");

        Assert.DoesNotContain("<Color>", xml);
    }

    [Fact]
    public void AltColor_NotAppliedToUnflattenedLeaves()
    {
        // Colour is scoped to flattened container elements: with the flatten toggle OFF the map
        // keeps per-element groups and NO row is tinted, even though the colour feature is on.
        var (map, structs) = BuildTwoElementRecordMap();

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "Obj", "Cls", new List<LiveFieldValue> { map }, structs,
            flattenLeafRecords: false,
            altColorEnabled: true, altRowColorEvenRgb: "0080FF", altRowColorOddRgb: "FF0000");

        Assert.DoesNotContain("<Color>", xml);
    }

    // ===== Collapse single-leaf pointers (Feature B — the "指標指到 string" case) =====
    // A drilled pointer whose resolved target is a SINGLE terminal leaf collapses to one CE record
    // at the pointer field with a deref chain, instead of a GroupHeader + lone child.

    /// <summary>One ObjectProperty "Owner" at +0x2C8 → a target object (0xPTR) whose only field is
    /// <paramref name="childType"/> <paramref name="childName"/> at <paramref name="childOffset"/>.</summary>
    private static (LiveFieldValue[] fields, Dictionary<string, List<LiveFieldValue>> instances)
        BuildSingleChildPointer(string childName, string childType, int childOffset, int childSize)
    {
        const string ptr = "0x7FF453509278";
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Owner", TypeName = "ObjectProperty", Offset = 0x2C8, Size = 8,
                PtrAddress = ptr, PtrName = "Owner", PtrClassName = "UHolder",
            },
        };
        var instances = new Dictionary<string, List<LiveFieldValue>>
        {
            [ptr] = new() { new LiveFieldValue { Name = childName, TypeName = childType, Offset = childOffset, Size = childSize } },
        };
        return (fields, instances);
    }

    [Fact]
    public void CollapseLeafPointer_SingleString_EmitsTwoDerefStringLeaf()
    {
        var (fields, instances) = BuildSingleChildPointer("Value", "StrProperty", 0x10, 16);

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields,
            resolvedInstances: instances, collapseLeafPointers: true);

        // Collapsed to ONE CE String leaf "Owner ▸ Value": follow the pointer (Address=+2C8), then the
        // FString.Data buffer at +10 — Offsets=[0, 10] (two derefs). The +2C8 String-with-chain leaf
        // can't coexist with a non-collapsed group at +2C8, so the regex alone proves the collapse.
        Assert.Matches(
            @"Owner ▸ Value""</Description>(?:(?!</CheatEntry>)[\s\S])*?<VariableType>String</VariableType>"
            + @"(?:(?!</CheatEntry>)[\s\S])*?<Address>\+2C8</Address>\s*<Offsets>\s*<Offset>0</Offset>"
            + @"\s*<Offset>10</Offset>\s*</Offsets>", xml);
    }

    [Fact]
    public void CollapseLeafPointer_SingleScalar_EmitsOneDerefLeaf()
    {
        var (fields, instances) = BuildSingleChildPointer("Count", "IntProperty", 0x8, 4);

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields,
            resolvedInstances: instances, collapseLeafPointers: true);

        // Collapsed to ONE 4-byte leaf "Owner ▸ Count": Address=+2C8, Offsets=[8] (one deref). The
        // +2C8 leaf-with-offset can't coexist with a non-collapsed group at +2C8, proving collapse.
        Assert.Matches(
            @"Owner ▸ Count""</Description>(?:(?!</CheatEntry>)[\s\S])*?<Address>\+2C8</Address>"
            + @"\s*<Offsets>\s*<Offset>8</Offset>\s*</Offsets>", xml);
    }

    [Fact]
    public void CollapseLeafPointer_MultiField_KeepsGroup()
    {
        // Two fields behind the pointer → an object identity worth a boundary; keep the group.
        const string ptr = "0x7FF453509278";
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Owner", TypeName = "ObjectProperty", Offset = 0x2C8, Size = 8,
                PtrAddress = ptr, PtrName = "Owner", PtrClassName = "UHolder",
            },
        };
        var instances = new Dictionary<string, List<LiveFieldValue>>
        {
            [ptr] = new()
            {
                new LiveFieldValue { Name = "Value", TypeName = "StrProperty", Offset = 0x10, Size = 16 },
                new LiveFieldValue { Name = "Count", TypeName = "IntProperty", Offset = 0x20, Size = 4 },
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields,
            resolvedInstances: instances, collapseLeafPointers: true);

        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);   // group kept
        Assert.DoesNotContain("Owner ▸ Value", xml);            // not collapsed
    }

    [Fact]
    public void CollapseLeafPointerOff_KeepsGroup()
    {
        var (fields, instances) = BuildSingleChildPointer("Value", "StrProperty", 0x10, 16);

        // Toggle off (default) → the single-string pointer keeps its GroupHeader + String child.
        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields,
            resolvedInstances: instances, collapseLeafPointers: false);

        Assert.Contains("<GroupHeader>1</GroupHeader>", xml);
        Assert.DoesNotContain("Owner ▸ Value", xml);
    }

    [Fact]
    public void MapValueStruct_NotResolved_FallsBackGracefully()
    {
        // Same map but no resolved value struct (depth 0 / walk failed): must not
        // throw and must still emit the map group + element (value as a bare folder),
        // never the old whole-map placeholder.
        var map = new LiveFieldValue
        {
            Name = "MissionInfoList", TypeName = "MapProperty", Offset = 0x2F8, Size = 0x50,
            MapCount = 1, MapKeyType = "NameProperty", MapValueType = "StructProperty",
            MapKeySize = 8, MapValueSize = 0x10, MapValueOffset = 8, MapDataAddr = "0x4000",
            MapValueStructAddr = "0xABC", MapValueStructType = "LifeMissionInfo",
            MapElements = new List<ContainerElementValue> { new() { Index = 0, Key = "mc1om_001" } },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "Obj", "Cls", new List<LiveFieldValue> { map });

        Assert.Contains("[0] mc1om_001", xml);
        Assert.Contains("<Offset>0</Offset>", xml);   // map still derefs Data
    }

    [Fact]
    public async Task ResolveDrilldown_MapValueStruct_WalksValueStructs()
    {
        // The resolver must walk each Map<Name, Struct> value struct (at its computed
        // address) and populate resolvedStructs so the emitter can expand it.
        var stub = new StubDumpService();
        stub.RegisterStruct("0x4008", new InstanceWalkResult
        {
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Rank", TypeName = "IntProperty", Offset = 0x4, Size = 4 },
            }
        });

        var map = new LiveFieldValue
        {
            Name = "MissionInfoList", TypeName = "MapProperty", Offset = 0x2F8,
            MapCount = 1, MapKeyType = "NameProperty", MapValueType = "StructProperty",
            MapKeySize = 8, MapValueSize = 0x10, MapValueOffset = 8, MapDataAddr = "0x4000",
            MapValueStructAddr = "0xABC", MapValueStructType = "LifeMissionInfo",
            MapElements = new List<ContainerElementValue> { new() { Index = 0, Key = "mc1om_001" } },
        };

        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        await CeXmlExportService.ResolveDrilldownAsync(
            stub, new List<LiveFieldValue> { map }, resolvedStructs, resolvedInstances, depth: 2,
            ct: TestContext.Current.CancellationToken);

        // Value struct at 0x4000 + 0*stride + valOffset(8) = 0x4008 was walked.
        Assert.True(resolvedStructs.ContainsKey("0x4008"));
        Assert.Contains(resolvedStructs["0x4008"], f => f.Name == "Rank");
    }

    [Fact]
    public async Task ResolveDrilldown_Depth0_DoesNotWalkContainerValues()
    {
        // At depth 0 the export is flat — container values are not resolved.
        var stub = new StubDumpService();
        stub.RegisterStruct("0x4008", new InstanceWalkResult
        {
            Fields = new List<LiveFieldValue> { new() { Name = "Rank", TypeName = "IntProperty", Offset = 0, Size = 4 } }
        });
        var map = new LiveFieldValue
        {
            Name = "M", TypeName = "MapProperty", Offset = 0x10,
            MapCount = 1, MapKeyType = "NameProperty", MapValueType = "StructProperty",
            MapKeySize = 8, MapValueSize = 0x10, MapValueOffset = 8, MapDataAddr = "0x4000",
            MapValueStructAddr = "0xABC", MapValueStructType = "T",
            MapElements = new List<ContainerElementValue> { new() { Index = 0, Key = "k" } },
        };
        var rs = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        var ri = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        await CeXmlExportService.ResolveDrilldownAsync(stub, new List<LiveFieldValue> { map }, rs, ri, depth: 0,
            ct: TestContext.Current.CancellationToken);
        Assert.False(rs.ContainsKey("0x4008"));
    }

    [Fact]
    public async Task ResolveDrilldown_Depth1_ExpandsOneContainerLevelOnly()
    {
        // Map<Name, Struct> whose value struct holds a nested Map<Name, Struct>.
        // D=1 walks the OUTER value struct (0x4008) but NOT the inner map's value struct
        // (0x9008); D=2 walks both. Locks "Drill Depth from the current view, each
        // container level costs one" (docs/ce-export-drilldown-spec.md §4).
        //
        // Outer stride = AlignUp(8+0x10,4)+8 = 32; [0] value @ 0*32+8 = 8  -> 0x4008.
        // Inner stride = AlignUp(8+8,4)+8   = 24; [0] value @ 0*24+8 = 8  -> 0x9008.
        var stub = new StubDumpService();
        stub.RegisterStruct("0x4008", new InstanceWalkResult
        {
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Inner", TypeName = "MapProperty", Offset = 0,
                         MapCount = 1, MapKeyType = "NameProperty", MapValueType = "StructProperty",
                         MapKeySize = 8, MapValueSize = 8, MapValueOffset = 8, MapDataAddr = "0x9000",
                         MapValueStructAddr = "0xINNER", MapValueStructType = "FInner",
                         MapElements = new List<ContainerElementValue> { new() { Index = 0, Key = "ik" } } },
            }
        });
        stub.RegisterStruct("0x9008", new InstanceWalkResult
        {
            Fields = new List<LiveFieldValue> { new() { Name = "Leaf", TypeName = "IntProperty", Offset = 0, Size = 4 } }
        });

        var map = new LiveFieldValue
        {
            Name = "Outer", TypeName = "MapProperty", Offset = 0x40,
            MapCount = 1, MapKeyType = "NameProperty", MapValueType = "StructProperty",
            MapKeySize = 8, MapValueSize = 0x10, MapValueOffset = 8, MapDataAddr = "0x4000",
            MapValueStructAddr = "0xOUTER", MapValueStructType = "FOuter",
            MapElements = new List<ContainerElementValue> { new() { Index = 0, Key = "ok" } },
        };

        var rs1 = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        var ri1 = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        await CeXmlExportService.ResolveDrilldownAsync(stub, new List<LiveFieldValue> { map }, rs1, ri1, depth: 1,
            ct: TestContext.Current.CancellationToken);
        Assert.True(rs1.ContainsKey("0x4008"));    // outer value struct walked (one level)
        Assert.False(rs1.ContainsKey("0x9008"));   // inner map value struct NOT walked at D=1

        var rs2 = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        var ri2 = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        await CeXmlExportService.ResolveDrilldownAsync(stub, new List<LiveFieldValue> { map }, rs2, ri2, depth: 2,
            ct: TestContext.Current.CancellationToken);
        Assert.True(rs2.ContainsKey("0x4008"));
        Assert.True(rs2.ContainsKey("0x9008"));    // inner expands at D=2
    }

    // ========================================
    // ResolveDrilldown cancellation (Copy CE XML / Copy CE Field abort)
    // ========================================

    /// <summary>A StubDumpService whose WalkInstanceAsync always throws — lets a test
    /// prove that the export resolver distinguishes a cancellation (must propagate and
    /// abort the export) from an ordinary pipe/target failure (must be swallowed so the
    /// field falls back to a leaf).</summary>
    private sealed class ThrowingWalkStub : StubDumpService
    {
        private readonly Func<Exception> _make;
        public ThrowingWalkStub(Func<Exception> make) => _make = make;
        public override Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
            int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false,
            CancellationToken ct = default)
            => throw _make();
    }

    [Fact]
    public async Task ResolveDrilldown_CancelledToken_ThrowsOperationCanceled()
    {
        // A token cancelled before the walk begins aborts at the entry guard.
        var stub = new StubDumpService();
        stub.RegisterStruct("0xDATA", new InstanceWalkResult
        {
            Fields = new List<LiveFieldValue> { new() { Name = "Leaf", TypeName = "IntProperty", Offset = 0, Size = 4 } }
        });
        var field = new LiveFieldValue
        {
            Name = "S", TypeName = "StructProperty", Offset = 0,
            StructClassAddr = "0xCLS", StructDataAddr = "0xDATA",
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var rs = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        var ri = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CeXmlExportService.ResolveDrilldownAsync(
                stub, new List<LiveFieldValue> { field }, rs, ri, depth: 1, ct: cts.Token));
    }

    [Fact]
    public async Task ResolveDrilldown_StructWalkCancelled_Propagates()
    {
        // A cancel surfacing mid-walk (as OperationCanceledException from a struct
        // WalkInstance) must NOT be eaten by ResolveStructFieldsIntoAsync's pipe-error
        // catch — it must abort the whole export.
        var stub = new ThrowingWalkStub(() => new OperationCanceledException());
        var field = new LiveFieldValue
        {
            Name = "S", TypeName = "StructProperty", Offset = 0,
            StructClassAddr = "0xCLS", StructDataAddr = "0xDATA",
        };
        var rs = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        var ri = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CeXmlExportService.ResolveDrilldownAsync(
                stub, new List<LiveFieldValue> { field }, rs, ri, depth: 1,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolveDrilldown_PointerWalkCancelled_Propagates()
    {
        // Same guarantee on the pointer branch — WalkAndRecurseAsync's swallow-catch
        // must let a cancellation through, not fall back to a leaf.
        var stub = new ThrowingWalkStub(() => new OperationCanceledException());
        var field = new LiveFieldValue
        {
            Name = "Ptr", TypeName = "ObjectProperty", Offset = 0, PtrAddress = "0xAAA",
        };
        var rs = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        var ri = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CeXmlExportService.ResolveDrilldownAsync(
                stub, new List<LiveFieldValue> { field }, rs, ri, depth: 1,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolveDrilldown_OrdinaryWalkError_StillSwallowed()
    {
        // Positive control: a normal pipe/target failure (not a cancel) is still
        // tolerated — the struct is left unresolved (leaf fallback) and the export
        // completes without throwing. Guards against an over-broad catch.
        var stub = new ThrowingWalkStub(() => new InvalidOperationException("pipe boom"));
        var field = new LiveFieldValue
        {
            Name = "S", TypeName = "StructProperty", Offset = 0,
            StructClassAddr = "0xCLS", StructDataAddr = "0xDATA",
        };
        var rs = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        var ri = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);

        await CeXmlExportService.ResolveDrilldownAsync(
            stub, new List<LiveFieldValue> { field }, rs, ri, depth: 1,
            ct: TestContext.Current.CancellationToken);

        Assert.False(rs.ContainsKey("0xDATA"));   // unresolved → emit falls back to a leaf
    }

    [Fact]
    public void GenerateHierarchicalXml_BreadcrumbLength_DoesNotChangeFieldExpansion()
    {
        // Drill Depth is measured from the current view; the breadcrumb navigation path
        // costs nothing. A struct field resolved to depth D expands identically whether the
        // breadcrumb trail is 1 hop or 4 hops long.
        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal)
        {
            ["0x5000"] = new()
            {
                new() { Name = "X", TypeName = "FloatProperty", Offset = 0, Size = 4 },
                new() { Name = "Y", TypeName = "FloatProperty", Offset = 4, Size = 4 },
            }
        };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Loc", TypeName = "StructProperty", Offset = 0x10, Size = 8,
                     StructTypeName = "FVector2D", StructDataAddr = "0x5000", StructClassAddr = "0x6000" }
        };

        var shortBc = new[] { MakeBc("0x1000", "GWorld") };
        var longBc = new[]
        {
            MakeBc("0x1000", "GWorld"),
            MakeBc("0x2000", "A", "A", isPointer: true, offset: 0x8),
            MakeBc("0x3000", "B", "B", isPointer: true, offset: 0x10),
            MakeBc("0x4000", "C", "C", isPointer: true, offset: 0x18),
        };

        var xmlShort = CeXmlExportService.GenerateHierarchicalXml("0x1000", "GWorld", shortBc, fields, resolvedStructs);
        var xmlLong = CeXmlExportService.GenerateHierarchicalXml("0x1000", "GWorld", longBc, fields, resolvedStructs);

        // Both expand the struct's fields fully — breadcrumb length is irrelevant to depth.
        Assert.Contains("\"X\"", xmlShort);
        Assert.Contains("\"Y\"", xmlShort);
        Assert.Contains("\"X\"", xmlLong);
        Assert.Contains("\"Y\"", xmlLong);
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

        // Set group description is now the bare field name (no set descriptor)
        Assert.Contains("\"TagSet\"", xml);
        Assert.DoesNotContain("{Set: 3", xml);
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

        // Map group: offset 0x358 with Offsets=[0] for TSparseArray.Data deref.
        // Description is now the bare field name (no map descriptor).
        Assert.Contains("\"AttributeAugmentLevels\"", xml);
        Assert.DoesNotContain("{Map: 6", xml);
        Assert.Contains("<Address>+358</Address>", xml);

        // Map elements: element folder shows the key for orientation; the Key/Value
        // leaves are label-only (no baked-in dynamic value).
        Assert.Contains("[0] firepower", xml);
        Assert.Contains("[3] resistance", xml);
        Assert.Contains("\"Key\"", xml);
        Assert.Contains("\"Value\"", xml);
        Assert.DoesNotContain("Value: 5", xml);
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

        // Array group at +468 with Offsets=[0]. Description is now the bare field name.
        Assert.Contains("\"Ships\"", xml);
        Assert.DoesNotContain("[2 x ShipData", xml);
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
    public void StructArray_EnumSubField_UsesRealByteWidth_NonScalarSurfaced_ElementCollapses()
    {
        // Mirrors the SEED SaveSlotList[1] bug report: a struct array element
        // under a pointer chain with (a) a 1-byte EnumProperty sub-field that was
        // wrongly emitted as "4 Bytes" (CE then read the next field's bytes →
        // 5376 instead of 0), (b) a non-scalar StructProperty sub-field that was
        // silently dropped, and (c) an element folder [1] that did not collapse.
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "GWorld"),
            MakeBc("0x2000", "m_savedata", "m_savedata", isPointer: true, offset: 0x2A8),
        };
        var fields = new List<LiveFieldValue>
        {
            new()
            {
                Name = "SaveSlotList", TypeName = "ArrayProperty", Offset = 0x7D0, Size = 16,
                ArrayCount = 1, ArrayInnerType = "StructProperty", ArrayStructType = "LifeSaveDataSlot",
                ArrayElemSize = 0x6F8,
                ArrayElements = new List<ArrayElementValue>
                {
                    new()
                    {
                        Index = 1, Value = "",
                        StructFields = new List<StructSubFieldValue>
                        {
                            new() { Name = "m_last_access_date", TypeName = "StructProperty", Offset = 0x0, Size = 8 },
                            new() { Name = "CurrentStoryMissionSeriesId", TypeName = "EnumProperty", Offset = 0x10, Size = 1 },
                            new() { Name = "ScenarioFlag", TypeName = "IntProperty", Offset = 0x2F0, Size = 4 },
                        }
                    },
                }
            },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"game.exe\"+1000", "GWorld", breadcrumbs, fields,
            collapsePointerNodes: true);

        // (a) 1-byte enum → "Byte", never "4 Bytes" within its entry
        var seriesIdx = xml.IndexOf("\"CurrentStoryMissionSeriesId\"", StringComparison.Ordinal);
        Assert.True(seriesIdx >= 0);
        var seriesEntry = xml.Substring(seriesIdx,
            xml.IndexOf("</CheatEntry>", seriesIdx, StringComparison.Ordinal) - seriesIdx);
        Assert.Contains("<VariableType>Byte</VariableType>", seriesEntry);
        Assert.DoesNotContain("4 Bytes", seriesEntry);

        // Int sub-field stays 4 Bytes
        Assert.Contains("\"ScenarioFlag\"", xml);

        // (b) non-scalar struct sub-field surfaced as a placeholder, not dropped
        Assert.Contains("\"m_last_access_date\"", xml);

        // (c) the [1] element folder collapses (has Options)
        var elemIdx = xml.IndexOf("\"[1]\"", StringComparison.Ordinal);
        Assert.True(elemIdx >= 0);
        var elemHeader = xml.Substring(elemIdx,
            xml.IndexOf("<CheatEntries>", elemIdx, StringComparison.Ordinal) - elemIdx);
        Assert.Contains("moHideChildren", elemHeader);
    }

    [Fact]
    public void ScalarEnumProperty_WidthFollowsByteSize()
    {
        // 1-byte enum → "Byte"; 4-byte enum → "4 Bytes" (legacy width preserved).
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Force", TypeName = "EnumProperty", Offset = 0x11, Size = 1 },
            new() { Name = "WideEnum", TypeName = "EnumProperty", Offset = 0x14, Size = 4 },
        };
        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"game.exe\"+1000", "Obj", "Cls", fields);

        var forceIdx = xml.IndexOf("\"Force\"", StringComparison.Ordinal);
        Assert.Contains("<VariableType>Byte</VariableType>",
            xml.Substring(forceIdx, xml.IndexOf("</CheatEntry>", forceIdx, StringComparison.Ordinal) - forceIdx));

        var wideIdx = xml.IndexOf("\"WideEnum\"", StringComparison.Ordinal);
        Assert.Contains("<VariableType>4 Bytes</VariableType>",
            xml.Substring(wideIdx, xml.IndexOf("</CheatEntry>", wideIdx, StringComparison.Ordinal) - wideIdx));
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

        // Map group description is now the bare field name (no map descriptor)
        Assert.Contains("\"Scores\"", xml);
        Assert.DoesNotContain("{Map: 10", xml);
        // But only 1 element is emitted (scalar/string key kept)
        Assert.Contains("[5] speed", xml);
        // Key/Value leaves are label-only — the dynamic value is NOT baked into the
        // description (the stored int can change at runtime).
        Assert.Contains("\"Key\"", xml);
        Assert.Contains("\"Value\"", xml);
        Assert.DoesNotContain("Value: 200", xml);
        // No other element indices
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

        // Set group at +200 with Offsets=[0]. Description is now the bare field name.
        Assert.Contains("\"ActiveTags\"", xml);
        Assert.DoesNotContain("{Set: 2", xml);
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

        // Array group description is now the bare field name (no array descriptor)
        Assert.Contains("\"StreamingTextureGuids\"", xml);
        Assert.DoesNotContain("[3 x Guid", xml);
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

        // Array group with Offsets=[0]. Description is now the bare field name.
        Assert.Contains("\"StreamingTextureGuids\"", xml);
        Assert.DoesNotContain("[78 x Guid", xml);
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

        // Should still emit with Offsets=[0] (not a placeholder without deref).
        // Description is now the bare field name (no array descriptor).
        Assert.Contains("\"LargeStructArr\"", xml);
        Assert.DoesNotContain("[1000 x FData", xml);
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

        // Full chain preserved: GWorld -> OwningGameInstance -> LocalPlayers -> [0] -> PlayerController.
        // The container-view spine breadcrumb now uses the clean field name (no descriptor suffix).
        Assert.Contains("\"GWorld\"", xml);
        Assert.Contains("\"OwningGameInstance\"", xml);
        Assert.Contains("\"LocalPlayers\"", xml);
        Assert.DoesNotContain("LocalPlayers [1 x ObjectProperty", xml);
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

        // Inventory breadcrumb should emit Offsets=[0]. The container-view spine
        // breadcrumb now uses the clean field name (no descriptor suffix).
        Assert.Contains("<Address>+A0</Address>", xml);
        Assert.Contains("\"Inventory\"", xml);
        Assert.DoesNotContain("Inventory {Map: 5}", xml);
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

        // Level 1: RowMap group at +B0 with Offsets=[0] (deref TSparseArray.Data).
        // Description is now the bare field name (no DataTable descriptor).
        Assert.Contains("<Address>+B0</Address>", xml);
        Assert.Contains("<Offset>0</Offset>", xml);
        Assert.Contains("\"RowMap\"", xml);
        Assert.DoesNotContain("DataTable: 2 x RecipeRow", xml);

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
    public void GenerateInstanceXml_StrProperty_EmitsUnicodeStringNoCodePage()
    {
        var fields = new[]
        {
            new LiveFieldValue { Name = "WideName", TypeName = "StrProperty", Offset = 0x10, Size = 16 },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // FString (wchar_t*) → Unicode=1, CodePage=0
        Assert.Contains("<VariableType>String</VariableType>", xml);
        Assert.Contains("<Unicode>1</Unicode>", xml);
        Assert.Contains("<CodePage>0</CodePage>", xml);
        Assert.Contains("WideName", xml);
    }

    [Fact]
    public void GenerateInstanceXml_Utf8StrProperty_EmitsByteStringWithCodePage()
    {
        var fields = new[]
        {
            new LiveFieldValue { Name = "Utf8Name", TypeName = "Utf8StrProperty", Offset = 0x10, Size = 16 },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // FUtf8String (1-byte UTF-8) → Unicode=0, CodePage=1 (CE decodes multibyte UTF-8)
        Assert.Contains("<VariableType>String</VariableType>", xml);
        Assert.Contains("<Unicode>0</Unicode>", xml);
        Assert.Contains("<CodePage>1</CodePage>", xml);
        Assert.Contains("Utf8Name", xml);
    }

    [Fact]
    public void GenerateInstanceXml_AnsiStrProperty_EmitsByteStringNoCodePage()
    {
        var fields = new[]
        {
            new LiveFieldValue { Name = "AnsiName", TypeName = "AnsiStrProperty", Offset = 0x10, Size = 16 },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // FAnsiString (1-byte ANSI) → Unicode=0, CodePage=0
        Assert.Contains("<VariableType>String</VariableType>", xml);
        Assert.Contains("<Unicode>0</Unicode>", xml);
        Assert.Contains("<CodePage>0</CodePage>", xml);
        Assert.Contains("AnsiName", xml);
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

    [Fact]
    public void GenerateInstanceXml_ScalarEnumProperty_EmitsDropDownListNotDescription()
    {
        // A top-level (non-array) EnumProperty must surface its members as a CE
        // <DropDownList> of value:name options — NOT dumped into the record
        // <Description>. (Guards the enum-export contract regardless of which UE
        // enum-names container the DLL read them from.)
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "ItemType", TypeName = "EnumProperty", Offset = 0x10, Size = 4,
                EnumName = "Weapon", EnumValue = 1, EnumAddr = "0xE001",
                EnumEntries = new List<EnumEntryValue>
                {
                    new() { Value = 0, Name = "None" },
                    new() { Value = 1, Name = "Weapon" },
                    new() { Value = 2, Name = "Armor" },
                },
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "Inst", "UObject", fields);

        // DropDownList present with CE "value:name" options (BuildDropDownContent format).
        Assert.Contains("<DropDownList DisplayValueAsItem=\"1\">", xml);
        Assert.Contains("0:None", xml);
        Assert.Contains("1:Weapon", xml);
        Assert.Contains("2:Armor", xml);

        // The option list must NOT leak into any <Description> line.
        foreach (var line in xml.Split('\n'))
        {
            if (line.Contains("<Description>"))
            {
                Assert.DoesNotContain("1:Weapon", line);
                Assert.DoesNotContain("2:Armor", line);
            }
        }
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
        Assert.Contains("AOBScanModuleUE", xml);
        Assert.Contains("registerLuaFunctionHighlight('AOBScanModuleUE')", xml);
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

    // ========================================
    // Soft/Lazy/Interface ArrayProperty tests (Phase G/H/I)
    // ========================================

    [Fact]
    public void GenerateInstanceXml_SoftObjectArray_UE4Layout_EmitsPerElementGroupWithFNameLeaf()
    {
        // TArray<TSoftObjectPtr<UDataAsset>> on UE4 / UE5.0:
        //   element layout = WeakPtr(8) + Tag(8) + FName AssetPathName(8) + FString(16) = 0x28
        //   SoftArrayIsTopLevelAssetPath=false → single FName at +0x10
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "AssetRefs", TypeName = "ArrayProperty", Offset = 0x100, Size = 16,
                ArrayCount = 2, ArrayInnerType = "SoftObjectProperty", ArrayElemSize = 0x28,
                SoftArrayFNameSize = 8, SoftArrayIsTopLevelAssetPath = false,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "/Game/Items/IT_Potion.IT_Potion",
                            RawIntValue = 1234,
                            Hex = "00000000000000000000000000000000" },
                    new() { Index = 1, Value = "/Game/Items/IT_Sword.IT_Sword",
                            RawIntValue = 5678,
                            Hex = "00000000000000000000000000000000" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Outer array group header (Address=+100, Offsets=[0] derefs TArray.Data).
        // Description is now the bare field name (no array descriptor).
        Assert.Contains("\"AssetRefs\"", xml);
        Assert.DoesNotContain("[2 x SoftObjectProperty", xml);
        Assert.Contains("<Address>+100</Address>", xml);
        Assert.Contains("<Offset>0</Offset>", xml);

        // Per-element group descriptions keep the resolved asset path (asset identity)
        Assert.Contains("[0] /Game/Items/IT_Potion.IT_Potion", xml);
        Assert.Contains("[1] /Game/Items/IT_Sword.IT_Sword", xml);

        // Element groups at stride boundaries (0x28 = 40)
        Assert.Contains("<Address>+0</Address>", xml);
        Assert.Contains("<Address>+28</Address>", xml);

        // Per-element WeakPtr leaf (8 Bytes hex at +0)
        Assert.Contains("\"WeakPtr\"", xml);
        Assert.Contains("<VariableType>8 Bytes</VariableType>", xml);

        // Per-element FName AssetPath leaf at +10 (4 Bytes ComparisonIndex)
        Assert.Contains("\"AssetPath\"", xml);
        Assert.Contains("<Address>+10</Address>", xml);
        Assert.Contains("<VariableType>4 Bytes</VariableType>", xml);

        // Shared DropDownList built from the resolved asset paths (FName index → name)
        Assert.Contains("<DropDownList DisplayValueAsItem=\"1\">", xml);
        Assert.Contains("1234:/Game/Items/IT_Potion.IT_Potion", xml);
        Assert.Contains("5678:/Game/Items/IT_Sword.IT_Sword", xml);

        // No AssetName leaf in UE4 layout (only one FName)
        Assert.DoesNotContain("\"AssetName\"", xml);
    }

    [Fact]
    public void GenerateInstanceXml_SoftObjectArray_UE51Layout_EmitsPackageNameAndAssetName()
    {
        // TArray<TSoftObjectPtr<UDataAsset>> on UE5.1+:
        //   element layout = WeakPtr(8) + Tag(8) + FName PackageName(8) + FName AssetName(8)
        //                   + FString(16) = 0x30
        //   SoftArrayIsTopLevelAssetPath=true → TWO FNames at +0x10 / +0x18 (fnameSize=8)
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Assets", TypeName = "ArrayProperty", Offset = 0x80, Size = 16,
                ArrayCount = 1, ArrayInnerType = "SoftObjectProperty", ArrayElemSize = 0x30,
                SoftArrayFNameSize = 8, SoftArrayIsTopLevelAssetPath = true,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "/Game/Boss.BossActor",
                            RawIntValue = 9999,
                            Hex = "00000000000000000000000000000000" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Container header description is now the bare field name (no array descriptor)
        Assert.Contains("\"Assets\"", xml);
        Assert.DoesNotContain("[1 x SoftObjectProperty", xml);
        Assert.Contains("[0] /Game/Boss.BossActor", xml);   // asset path kept

        // Both FName leaves: PackageName at +10, AssetName at +18 (fnameSize=8)
        Assert.Contains("\"PackageName\"", xml);
        Assert.Contains("<Address>+10</Address>", xml);
        Assert.Contains("\"AssetName\"", xml);
        Assert.Contains("<Address>+18</Address>", xml);

        // No "AssetPath" label in UE5.1+ layout (split into Package + Asset)
        Assert.DoesNotContain("\"AssetPath\"", xml);
    }

    [Fact]
    public void GenerateInstanceXml_SoftObjectArray_CasePreservingName_AssetNameAt20()
    {
        // UE5.5+ CasePreservingName: fnameSize=16, so AssetName is at +0x20.
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Assets", TypeName = "ArrayProperty", Offset = 0x40, Size = 16,
                ArrayCount = 1, ArrayInnerType = "SoftObjectProperty", ArrayElemSize = 0x40,
                SoftArrayFNameSize = 16, SoftArrayIsTopLevelAssetPath = true,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "/Game/A.A", RawIntValue = 1, Hex = "" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        Assert.Contains("\"PackageName\"", xml);
        Assert.Contains("<Address>+10</Address>", xml);
        Assert.Contains("\"AssetName\"", xml);
        Assert.Contains("<Address>+20</Address>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_SoftClassArray_EmitsPerElementGroup()
    {
        // TArray<TSoftClassPtr<UClass>> — same shape as SoftObjectProperty
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "ClassRefs", TypeName = "ArrayProperty", Offset = 0x80, Size = 16,
                ArrayCount = 1, ArrayInnerType = "SoftClassProperty", ArrayElemSize = 0x28,
                SoftArrayFNameSize = 8, SoftArrayIsTopLevelAssetPath = false,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "/Game/AI/BP_Boss.BP_Boss_C",
                            RawIntValue = 4242,
                            Hex = "00000000000000000000000000000000" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Container header description is now the bare field name (no array descriptor)
        Assert.Contains("\"ClassRefs\"", xml);
        Assert.DoesNotContain("[1 x SoftClassProperty", xml);
        // Soft-class element keeps its asset path (an asset identity, not an instance name)
        Assert.Contains("[0] /Game/AI/BP_Boss.BP_Boss_C", xml);
        Assert.Contains("\"WeakPtr\"", xml);
        Assert.Contains("\"AssetPath\"", xml);
        Assert.Contains("<Address>+10</Address>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_SoftObjectArray_NoMetadata_FallsBackTo8ByteHex()
    {
        // Backwards-compat: legacy DLLs that don't emit SoftArrayFNameSize
        // should still produce an exportable XML — falls through to the old
        // single 8B-hex-per-element path via MapInnerTypeToCeField.
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Legacy", TypeName = "ArrayProperty", Offset = 0x60, Size = 16,
                ArrayCount = 1, ArrayInnerType = "SoftObjectProperty", ArrayElemSize = 0x28,
                // Intentionally no SoftArrayFNameSize / SoftArrayIsTopLevelAssetPath
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "/Game/A.A", Hex = "" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Container header description is now the bare field name (no array descriptor)
        Assert.Contains("\"Legacy\"", xml);
        Assert.DoesNotContain("[1 x SoftObjectProperty", xml);
        // No per-element group; the 8B leaf path still produces a usable entry
        Assert.Contains("<VariableType>8 Bytes</VariableType>", xml);
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", xml);
        Assert.DoesNotContain("\"WeakPtr\"", xml);
        Assert.DoesNotContain("\"AssetPath\"", xml);
    }

    [Fact]
    public void GenerateInstanceXml_LazyObjectArray_EmitsGroupWithElements()
    {
        // TArray<TLazyObjectPtr<AActor>> — element stride is 0x20
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "LazyRefs", TypeName = "ArrayProperty", Offset = 0x40, Size = 16,
                ArrayCount = 2, ArrayInnerType = "LazyObjectProperty", ArrayElemSize = 0x20,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "{12345678-9ABCDEF0-AABBCCDD-EEFF0011}",
                            Hex = "00000000000000000000000000000000" },
                    new() { Index = 1, Value = "{00000000-00000000-00000000-00000000}",
                            Hex = "00000000000000000000000000000000" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Container header description is now the bare field name (no array descriptor)
        Assert.Contains("\"LazyRefs\"", xml);
        Assert.DoesNotContain("[2 x LazyObjectProperty", xml);
        Assert.Contains("<Address>+40</Address>", xml);
        // Stride 0x20: [0] at +0, [1] at +20
        Assert.Contains("<Address>+0</Address>", xml);
        Assert.Contains("<Address>+20</Address>", xml);
        Assert.Contains("<VariableType>8 Bytes</VariableType>", xml);
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_InterfaceArray_EmitsGroupWithPointers()
    {
        // TArray<TScriptInterface<IDamageable>> — 16-byte elements: { UObject*, void* }
        // Description should show resolved UObject names for non-null entries
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "DamageHandlers", TypeName = "ArrayProperty", Offset = 0x60, Size = 16,
                ArrayCount = 3, ArrayInnerType = "InterfaceProperty", ArrayElemSize = 16,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "PlayerActor (BP_Player_C)",
                            Hex = "0000020C12340000ABCDEFAB12345678",
                            PtrAddress = "0x20C12340000",
                            PtrName = "PlayerActor", PtrClassName = "BP_Player_C" },
                    new() { Index = 1, Value = "Enemy_01 (BP_Enemy_C)",
                            Hex = "0000020C56780000FFEEDDCC11223344",
                            PtrAddress = "0x20C56780000",
                            PtrName = "Enemy_01", PtrClassName = "BP_Enemy_C" },
                    new() { Index = 2, Value = "null",
                            Hex = "00000000000000000000000000000000" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Container header description is now the bare field name (no array descriptor)
        Assert.Contains("\"DamageHandlers\"", xml);
        Assert.DoesNotContain("[3 x InterfaceProperty", xml);
        Assert.Contains("<Address>+60</Address>", xml);
        // Element descriptions are now the bare index (resolved names dropped)
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("\"[1]\"", xml);
        Assert.DoesNotContain("[0] PlayerActor", xml);
        Assert.DoesNotContain("[1] Enemy_01", xml);
        // Stride 16: [0] at +0, [1] at +10 (hex), [2] at +20 (hex)
        Assert.Contains("<Address>+0</Address>", xml);
        Assert.Contains("<Address>+10</Address>", xml);
        Assert.Contains("<Address>+20</Address>", xml);
        // Pointer type: 8 Bytes hex (UObject* at element start)
        Assert.Contains("<VariableType>8 Bytes</VariableType>", xml);
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", xml);
    }

    // ========================================
    // Delegate / Multicast ArrayProperty tests (Phase J/K)
    // ========================================

    [Fact]
    public void GenerateInstanceXml_DelegateArray_EmitsGroupWithBindings()
    {
        // TArray<FScriptDelegate> — element stride is 16 (no CasePreservingName)
        // Each element resolves to "Target::FunctionName" with PtrAddress for drilldown.
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "OnDeathHandlers", TypeName = "ArrayProperty", Offset = 0xA0, Size = 16,
                ArrayCount = 2, ArrayInnerType = "DelegateProperty", ArrayElemSize = 16,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "PlayerActor::OnPlayerDeath",
                            Hex = "0000020C12340000ABCDEFAB12345678",
                            PtrAddress = "0x20C12340000",
                            PtrName = "PlayerActor", PtrClassName = "BP_Player_C" },
                    new() { Index = 1, Value = "(unbound)",
                            Hex = "00000000000000000000000000000000" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Container header description is now the bare field name (no array descriptor)
        Assert.Contains("\"OnDeathHandlers\"", xml);
        Assert.DoesNotContain("[2 x DelegateProperty", xml);
        Assert.Contains("<Address>+A0</Address>", xml);
        // Element descriptions are now the bare index (resolved name dropped)
        Assert.Contains("\"[0]\"", xml);
        Assert.DoesNotContain("[0] PlayerActor", xml);
        // Stride 16: [0] at +0, [1] at +10 (hex)
        Assert.Contains("<Address>+0</Address>", xml);
        Assert.Contains("<Address>+10</Address>", xml);
        Assert.Contains("<VariableType>8 Bytes</VariableType>", xml);
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_DelegateArrayCasePreserving_StrideIs24()
    {
        // With CasePreservingName, FName is 16B, so FScriptDelegate = 8 + 16 = 24 (0x18)
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Handlers", TypeName = "ArrayProperty", Offset = 0x40, Size = 16,
                ArrayCount = 2, ArrayInnerType = "DelegateProperty", ArrayElemSize = 24,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "Actor1::OnHit",
                            Hex = "0000020CAA000000AABBCCDDEEFF11221234567812345678",
                            PtrAddress = "0x20CAA000000",
                            PtrName = "Actor1", PtrClassName = "BP_Enemy_C" },
                    new() { Index = 1, Value = "Actor2::OnHit",
                            Hex = "0000020CBB00000033445566778899AA1234567812345678",
                            PtrAddress = "0x20CBB000000",
                            PtrName = "Actor2", PtrClassName = "BP_Enemy_C" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Stride 24 (0x18): [0] at +0, [1] at +18 (hex)
        Assert.Contains("<Address>+0</Address>", xml);
        Assert.Contains("<Address>+18</Address>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_MulticastInlineDelegateArray_EmitsGroup()
    {
        // TArray<FMulticastScriptDelegate> — each element is 16B (TArray header)
        // Display value is "(N bindings) [...]" preview, no per-binding drill.
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Events", TypeName = "ArrayProperty", Offset = 0x60, Size = 16,
                ArrayCount = 2, ArrayInnerType = "MulticastInlineDelegateProperty", ArrayElemSize = 16,
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "(2 bindings) [Actor1::Tick, Actor2::Tick]",
                            Hex = "0000020CFF000000020000000200000" },
                    new() { Index = 1, Value = "(0 bindings)",
                            Hex = "00000000000000000000000000000000" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Container header description is now the bare field name (no array descriptor)
        Assert.Contains("\"Events\"", xml);
        Assert.DoesNotContain("[2 x MulticastInlineDelegateProperty", xml);
        Assert.Contains("<Address>+60</Address>", xml);
        // Stride 16: [0] at +0, [1] at +10 (hex)
        Assert.Contains("<Address>+0</Address>", xml);
        Assert.Contains("<Address>+10</Address>", xml);
        // 8 Bytes hex (FMulticastScriptDelegate.InvocationList::Data)
        Assert.Contains("<VariableType>8 Bytes</VariableType>", xml);
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_SingleMulticastDelegate_EmitsAsImplicitArray()
    {
        // A single FMulticastScriptDelegate field is exposed as an implicit
        // DelegateProperty array (TypeName=Multicast, ArrayInnerType=Delegate).
        // CE XML should emit it as an ArrayProperty group so users can copy
        // each binding's address chain.
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "m_OnPlayerPawnSetBlueprint",
                TypeName = "MulticastInlineDelegateProperty",
                Offset = 0x338, Size = 16,
                // Implicit array fields populated by DLL handler:
                ArrayCount = 2, ArrayInnerType = "DelegateProperty", ArrayElemSize = 16,
                TypedValue = "(2 bindings) [BP1::OnPawnSet, BP2::OnPawnSet]",
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "BP1::OnPawnSet",
                            Hex = "0000020CAA000000ABCDEFAB12345678",
                            PtrAddress = "0x20CAA000000",
                            PtrName = "BP1", PtrClassName = "BP_Test_C" },
                    new() { Index = 1, Value = "BP2::OnPawnSet",
                            Hex = "0000020CBB00000033445566778899AA",
                            PtrAddress = "0x20CBB000000",
                            PtrName = "BP2", PtrClassName = "BP_Test_C" },
                }
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Group description is now the bare field name (no array descriptor)
        Assert.Contains("\"m_OnPlayerPawnSetBlueprint\"", xml);
        Assert.DoesNotContain("[2 x DelegateProperty", xml);
        // Group at field offset, Offsets=[0] derefs InvocationList::Data
        Assert.Contains("<Address>+338</Address>", xml);
        Assert.Contains("<Offset>0</Offset>", xml);
        // Per-binding leaf: stride 16; description is now the bare index (BP name dropped)
        Assert.Contains("\"[0]\"", xml);
        Assert.Contains("\"[1]\"", xml);
        Assert.DoesNotContain("[0] BP1", xml);
        Assert.DoesNotContain("[1] BP2", xml);
        Assert.Contains("<Address>+0</Address>", xml);
        Assert.Contains("<Address>+10</Address>", xml);
        Assert.Contains("<VariableType>8 Bytes</VariableType>", xml);
        Assert.Contains("<ShowAsHex>1</ShowAsHex>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_SoftObjectArrayEmpty_EmitsPlaceholder()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "EmptySoft", TypeName = "ArrayProperty", Offset = 0x10, Size = 16,
                ArrayCount = 0, ArrayInnerType = "SoftObjectProperty", ArrayElemSize = 0x28,
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // Empty array → placeholder (no element entries)
        Assert.Contains("EmptySoft", xml);
        Assert.DoesNotContain("[0]", xml);
    }

    // ========================================
    // Collapse chain (breadcrumb spine flattening) tests
    // ========================================

    [Fact]
    public void FoldBreadcrumbSpine_UserExample_ThreePointersTwoInline_MergesToCeDocumentOrder()
    {
        // base → OwningGameInstance(ptr +180) → m_savedata(ptr +2A8) →
        // SaveSlotList(container +7D0) → [1](inline +6F8) → OriginalPlayer(inline +18)
        var bc = new List<BreadcrumbItem>
        {
            MakeBc("0x1000", "GWorld", isPointer: true),                                  // root
            MakeBc("0x2000", "OwningGameInstance", isPointer: true, offset: 0x180),
            MakeBc("0x3000", "m_savedata", isPointer: true, offset: 0x2A8),
            MakeBc("0x4000", "SaveSlotList", offset: 0x7D0, isContainerView: true),
            MakeBc("0x5000", "[1]", offset: 0x6F8),                                       // inline struct elem
            MakeBc("0x6000", "OriginalPlayer", offset: 0x18),                             // inline struct
        };

        var folded = CeXmlExportService.FoldBreadcrumbSpine(bc);

        Assert.NotNull(folded);
        Assert.Equal("+180", folded!.Address);
        // CE document order: outermost F = 0x6F8 + 0x18 = 0x710 first, then deref
        // offsets in reverse depth: 0x7D0 (SaveSlotList), 0x2A8 (m_savedata).
        Assert.Equal(new[] { 0x710, 0x7D0, 0x2A8 }, folded.Offsets);
        Assert.True(folded.ShowAsHex);
        Assert.Equal("OwningGameInstance ▸ m_savedata ▸ SaveSlotList ▸ [1] ▸ OriginalPlayer",
            folded.Description);
    }

    [Fact]
    public void FoldBreadcrumbSpine_TwoPointers_EmitsZeroThenChildOffset()
    {
        // Rule 1: two adjacent pointers fold to Offsets=[0, OffsetB] (0 FIRST).
        var bc = new List<BreadcrumbItem>
        {
            MakeBc("0x1", "Root", isPointer: true),
            MakeBc("0x2", "A", isPointer: true, offset: 0x100),
            MakeBc("0x3", "B", isPointer: true, offset: 0x200),
        };

        var folded = CeXmlExportService.FoldBreadcrumbSpine(bc);

        Assert.NotNull(folded);
        Assert.Equal("+100", folded!.Address);
        Assert.Equal(new[] { 0x0, 0x200 }, folded.Offsets);   // [F=0, deref B]
        Assert.True(folded.ShowAsHex);
    }

    [Fact]
    public void FoldBreadcrumbSpine_PointerThenInline_MergesTrailingInlineIntoFinalOffset()
    {
        // Rule 2: pointer A then inline struct B fold to Offsets=[OffsetB].
        var bc = new List<BreadcrumbItem>
        {
            MakeBc("0x1", "Root", isPointer: true),
            MakeBc("0x2", "A", isPointer: true, offset: 0x100),
            MakeBc("0x3", "B", offset: 0x40),    // inline
        };

        var folded = CeXmlExportService.FoldBreadcrumbSpine(bc);

        Assert.NotNull(folded);
        Assert.Equal("+100", folded!.Address);
        Assert.Equal(new[] { 0x40 }, folded.Offsets);
        Assert.True(folded.ShowAsHex);
    }

    [Fact]
    public void FoldBreadcrumbSpine_PureInlineSpine_NoOffsets()
    {
        // No dereference anywhere → single horizontal offset, no <Offsets>.
        var bc = new List<BreadcrumbItem>
        {
            MakeBc("0x1", "Root", isPointer: true),
            MakeBc("0x2", "A", offset: 0x10),    // inline
            MakeBc("0x3", "B", offset: 0x20),    // inline
        };

        var folded = CeXmlExportService.FoldBreadcrumbSpine(bc);

        Assert.NotNull(folded);
        Assert.Equal("+30", folded!.Address);
        Assert.Null(folded.Offsets);
        Assert.False(folded.ShowAsHex);
    }

    [Fact]
    public void FoldBreadcrumbSpine_FewerThanTwoNavNodes_ReturnsNull()
    {
        // root + 1 navigation node = nothing worth merging → no-op (emit nested).
        var bc = new List<BreadcrumbItem>
        {
            MakeBc("0x1", "Root", isPointer: true),
            MakeBc("0x2", "A", isPointer: true, offset: 0x100),
        };

        Assert.Null(CeXmlExportService.FoldBreadcrumbSpine(bc));
    }

    [Fact]
    public void GenerateAobWrappedXml_FlattenChain_CollapsesSpineToSingleEntry()
    {
        var bc = new List<BreadcrumbItem>
        {
            MakeBc("0x1000", "GWorld", isPointer: true),
            MakeBc("0x2000", "OwningGameInstance", isPointer: true, offset: 0x180),
            MakeBc("0x3000", "m_savedata", isPointer: true, offset: 0x2A8),
            MakeBc("0x4000", "SaveSlotList", offset: 0x7D0, isContainerView: true),
            MakeBc("0x5000", "[1]", offset: 0x6F8),
            MakeBc("0x6000", "OriginalPlayer", offset: 0x18),
        };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Val", TypeName = "IntProperty", Offset = 0x20, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateAobWrappedXml(
            "GWorld", bc, fields,
            aob: "48 8B 1D ?? ?? ?? ??", aobPos: 3, aobLen: 7, moduleName: "Game.exe",
            flattenChain: true);

        // Folded node: base address +180 with the merged multi-level offsets in order.
        Assert.Contains("<Address>+180</Address>", xml);
        Assert.Matches(
            @"<Offset>710</Offset>\s*<Offset>7D0</Offset>\s*<Offset>2A8</Offset>", xml);
        // Joined spine description (decision #1).
        Assert.Contains("OwningGameInstance ▸ m_savedata ▸ SaveSlotList ▸ [1] ▸ OriginalPlayer", xml);
        // Intermediate breadcrumbs are NO LONGER standalone group nodes.
        Assert.DoesNotContain("<Description>\"m_savedata\"</Description>", xml);
        Assert.DoesNotContain("<Description>\"OriginalPlayer\"</Description>", xml);
        // Leaf still emitted under the folded node.
        Assert.Contains("Val", xml);
        // base still dereferences the symbol (unchanged).
        Assert.Contains("<Description>\"base\"</Description>", xml);
    }

    [Fact]
    public void GenerateHierarchicalXml_FlattenChain_CollapsesSpine()
    {
        var bc = new List<BreadcrumbItem>
        {
            MakeBc("0x1000", "GWorld", isPointer: true),
            MakeBc("0x2000", "OwningGameInstance", isPointer: true, offset: 0x180),
            MakeBc("0x3000", "m_savedata", isPointer: true, offset: 0x2A8),
            MakeBc("0x4000", "SaveSlotList", offset: 0x7D0, isContainerView: true),
            MakeBc("0x5000", "[1]", offset: 0x6F8),
            MakeBc("0x6000", "OriginalPlayer", offset: 0x18),
        };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Val", TypeName = "IntProperty", Offset = 0x20, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "0x1000", "GWorld", bc, fields, flattenChain: true);

        Assert.Contains("<Address>+180</Address>", xml);
        Assert.Matches(
            @"<Offset>710</Offset>\s*<Offset>7D0</Offset>\s*<Offset>2A8</Offset>", xml);
        Assert.DoesNotContain("<Description>\"m_savedata\"</Description>", xml);
    }

    [Fact]
    public void GenerateAobWrappedXml_FlattenChainOff_KeepsNestedChain()
    {
        // Regression: default flattenChain=false preserves the per-breadcrumb nesting.
        var bc = new List<BreadcrumbItem>
        {
            MakeBc("0x1000", "GWorld", isPointer: true),
            MakeBc("0x2000", "OwningGameInstance", isPointer: true, offset: 0x180),
            MakeBc("0x3000", "m_savedata", isPointer: true, offset: 0x2A8),
        };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Val", TypeName = "IntProperty", Offset = 0x20, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateAobWrappedXml(
            "GWorld", bc, fields,
            aob: "48 8B 1D ?? ?? ?? ??", aobPos: 3, aobLen: 7, moduleName: "Game.exe");

        // Each breadcrumb is still its own group node.
        Assert.Contains("<Description>\"OwningGameInstance\"</Description>", xml);
        Assert.Contains("<Description>\"m_savedata\"</Description>", xml);
    }

    // ========================================
    // Map VALUE offset off-by-8 (SEED MsTunes {Map<FName, LifeMsTuneDataRow>}):
    // a drilled map-value struct's children landed 'valueOffset' bytes short
    // because the value crumb resolved to the element base instead of the value
    // base. LiveWalkerViewModel.MapValueDrillOffset now bakes valueOffset into
    // the crumb's FieldOffset; these cover the helper + the resulting CE chain.
    // ========================================

    [Fact]
    public void MapValueDrillOffset_MapParent_ReturnsAlignedValueOffset()
    {
        // FName-keyed map: value at +8 inside each TPair.
        var mapCrumb = new BreadcrumbItem
        {
            IsContainerView = true,
            ContainerField = new LiveFieldValue
            {
                TypeName = "MapProperty", MapCount = 132, MapKeyType = "NameProperty",
                MapKeySize = 8, MapValueOffset = 8,
            },
        };
        Assert.Equal(8, LiveWalkerViewModel.MapValueDrillOffset(mapCrumb));
    }

    [Fact]
    public void MapValueDrillOffset_MapParentNoAlignedOffset_FallsBackToKeySize()
    {
        var mapCrumb = new BreadcrumbItem
        {
            IsContainerView = true,
            ContainerField = new LiveFieldValue
            {
                TypeName = "MapProperty", MapCount = 3, MapKeyType = "StructProperty",
                MapKeySize = 16, MapValueOffset = 0,
            },
        };
        Assert.Equal(16, LiveWalkerViewModel.MapValueDrillOffset(mapCrumb));
    }

    [Fact]
    public void MapValueDrillOffset_NonMapParent_ReturnsZero()
    {
        // Pointer crumb (normal object drill) — no correction.
        Assert.Equal(0, LiveWalkerViewModel.MapValueDrillOffset(
            MakeBc("0x1", "Obj", "Obj", isPointer: true, offset: 0x30)));

        // Struct/Set array element values sit at the element base, so the array
        // container view also contributes no correction.
        var arrCrumb = new BreadcrumbItem
        {
            IsContainerView = true,
            ContainerField = new LiveFieldValue { TypeName = "ArrayProperty", ArrayCount = 4 },
        };
        Assert.Equal(0, LiveWalkerViewModel.MapValueDrillOffset(arrCrumb));

        // Null parent (navigating from the root).
        Assert.Equal(0, LiveWalkerViewModel.MapValueDrillOffset(null));
    }

    [Fact]
    public void GenerateHierarchicalXml_MapValueStructCrumb_ChildOffsetsChainFromValueBase()
    {
        // SEED repro: ... → MsTunes {Map<FName, LifeMsTuneDataRow>} (deref) →
        // [0] MSB_STR00 (the map VALUE struct) → its fields. After the off-by-8
        // fix the value crumb's FieldOffset includes valueOffset(8), so it
        // resolves to the value base and WeaponTuneList @ +0x18 chains correctly
        // (previously the value crumb sat at +0 = element base, dragging every
        // child 8 bytes short → CE dereferenced garbage).
        var breadcrumbs = new[]
        {
            MakeBc("0x1000", "GWorld"),
            // MsTunes map container view: derefs TSparseArray::Data at +250.
            new BreadcrumbItem
            {
                Address = "0x1000",
                Label = "MsTunes {Map: 132, NameProperty → StructProperty}",
                FieldName = "MsTunes",
                FieldOffset = 0x250,
                IsContainerView = true,
                ContainerField = new LiveFieldValue
                {
                    Name = "MsTunes", TypeName = "MapProperty", MapCount = 132,
                    MapKeyType = "NameProperty", MapKeySize = 8, MapValueOffset = 8,
                },
            },
            // [0] MSB_STR00 — map VALUE struct. FieldOffset already includes the
            // +8 valueOffset (index 0 * stride + 8), as the ViewModel now bakes in.
            MakeBc("0x2008", "[0] MSB_STR00 (LifeMsTuneDataRow)", "[0] MSB_STR00", offset: 0x8),
        };
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "BodyTune", TypeName = "StructProperty", Offset = 0x0, Size = 0x18 },
            new()
            {
                Name = "WeaponTuneList", TypeName = "ArrayProperty", Offset = 0x18, Size = 16,
                ArrayCount = 4, ArrayInnerType = "StructProperty", ArrayStructType = "LifeWeaponTune",
                ArrayElemSize = 0x28,
                ArrayElements = new List<ArrayElementValue> { new() { Index = 0, Value = "" } },
            },
            new() { Name = "PointRest", TypeName = "IntProperty", Offset = 0x28, Size = 4 },
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "0x1000", "GWorld", breadcrumbs, fields);

        // Map crumb derefs at +250.
        Assert.Contains("<Address>+250</Address>", xml);

        // The value-struct crumb resolves to the value base (+8 inside the element),
        // NOT the element base (+0).
        var msbIdx = xml.IndexOf("\"[0] MSB_STR00\"", StringComparison.Ordinal);
        Assert.True(msbIdx >= 0);
        var msbHeader = xml.Substring(msbIdx,
            xml.IndexOf("<CheatEntries>", msbIdx, StringComparison.Ordinal) - msbIdx);
        Assert.Contains("<Address>+8</Address>", msbHeader);

        // WeaponTuneList keeps its struct-relative offset 0x18 (now chained from
        // the value base) and derefs its TArray::Data.
        Assert.Contains("WeaponTuneList", xml);
        Assert.Contains("<Address>+18</Address>", xml);
    }

    // ========================================
    // System-component noise filter (Feature #2) tests
    // ========================================

    [Fact]
    public void GenerateInstanceXml_ExcludeSystemComponents_SkipsDrilledChild_KeepsTopLevelAndHealth()
    {
        // Top-level field (depth 1): a pointer to a parent instance — never filtered.
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "OwnerActor", TypeName = "ObjectProperty", Offset = 0x10, Size = 8,
                PtrAddress = "0xPARENT", PtrName = "Owner", PtrClassName = "MyGameActor",
            },
        };
        // Resolved children (depth 2): a normal float + a SoundCue pointer (system asset).
        var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0xPARENT"] = new()
            {
                new LiveFieldValue { Name = "Health", TypeName = "FloatProperty", Offset = 0x8, Size = 4 },
                new LiveFieldValue
                {
                    Name = "FootstepSound", TypeName = "ObjectProperty", Offset = 0x18, Size = 8,
                    PtrAddress = "0xSND", PtrName = "Footstep", PtrClassName = "SoundCue",
                },
            }
        };

        // Filter ON: the drilled SoundCue child is dropped; Health and the top-level
        // OwnerActor pointer are kept; the skip count is exactly 1.
        var on = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields,
            resolvedInstances: resolvedInstances, excludeSystemComponents: true);
        Assert.Contains("OwnerActor", on);
        Assert.Contains("Health", on);
        Assert.DoesNotContain("FootstepSound", on);
        Assert.Equal(1, CeXmlExportService.LastSystemFieldsSkipped);

        // Filter OFF: the SoundCue child is present and nothing is counted as skipped.
        var off = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields,
            resolvedInstances: resolvedInstances, excludeSystemComponents: false);
        Assert.Contains("FootstepSound", off);
        Assert.Equal(0, CeXmlExportService.LastSystemFieldsSkipped);
    }

    [Fact]
    public void GenerateInstanceXml_ExcludeSystemComponents_KeepsTopLevelSelectedSystemField()
    {
        // The user explicitly selected a SoundBase pointer. Even with the filter ON, a
        // TOP-LEVEL field is never dropped — only recursively-resolved children are filtered.
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "AmbientSound", TypeName = "ObjectProperty", Offset = 0x20, Size = 8,
                PtrAddress = "0xSND", PtrName = "Ambient", PtrClassName = "SoundBase",
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields, excludeSystemComponents: true);

        Assert.Contains("AmbientSound", xml);
        Assert.Equal(0, CeXmlExportService.LastSystemFieldsSkipped);
    }

    [Fact]
    public void GenerateInstanceXml_ExcludeSystemComponents_IgnoresStructTypeName()
    {
        // A drilled StructProperty child (depth 2) whose StructTypeName collides with a
        // ban-set name must STILL be kept — the filter tests PtrClassName only, so a plain
        // struct member (e.g. a GAS FGameplayAttributeData) is never dropped.
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "OwnerActor", TypeName = "ObjectProperty", Offset = 0x10, Size = 8,
                PtrAddress = "0xPARENT", PtrName = "Owner", PtrClassName = "MyGameActor",
            },
        };
        var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0xPARENT"] = new()
            {
                new LiveFieldValue
                {
                    Name = "HealthAttr", TypeName = "StructProperty", Offset = 0x8, Size = 8,
                    StructDataAddr = "0xATTR", StructClassAddr = "0xCLS",
                    StructTypeName = "Texture",   // deliberately a ban-set name (must be ignored)
                },
            }
        };
        var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>
        {
            ["0xATTR"] = new()
            {
                new LiveFieldValue { Name = "BaseValue", TypeName = "FloatProperty", Offset = 0x0, Size = 4 },
            }
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields,
            resolvedStructs: resolvedStructs, resolvedInstances: resolvedInstances,
            excludeSystemComponents: true);

        Assert.Contains("HealthAttr", xml);
        Assert.Contains("BaseValue", xml);
        Assert.Equal(0, CeXmlExportService.LastSystemFieldsSkipped);
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

    // --- Enum element width follows the real element size (audit #5 W6) ---------
    //
    // Element ADDRESSES are laid out with the DLL's element size, so a CE record wider than the
    // stride makes every element swallow the next ones. The rule was already applied to struct
    // sub-fields and to map keys/values; TArray and TSet were left out.

    [Fact]
    public void GenerateInstanceXml_ByteWideEnumArray_EmitsByteRecords()
    {
        // TArray<ECharacterState> where ECharacterState : uint8 -> stride 1.
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "States", TypeName = "ArrayProperty", Offset = 0x7D0, Size = 16,
                ArrayCount = 3, ArrayInnerType = "EnumProperty", ArrayElemSize = 1,
                ArrayInnerAddr = "0x5000",
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "2", RawIntValue = 2 },
                    new() { Index = 1, Value = "0", RawIntValue = 0 },
                    new() { Index = 2, Value = "5", RawIntValue = 5 },
                },
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        // A 4-byte record at 1-byte spacing would read elements 1..3 into element 0.
        Assert.Contains("<VariableType>Byte</VariableType>", xml);
        Assert.DoesNotContain("<VariableType>4 Bytes</VariableType>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_FourByteEnumArray_StillEmitsFourByteRecords()
    {
        // The other half of the rule: a genuinely 4-byte enum must not be narrowed.
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Modes", TypeName = "ArrayProperty", Offset = 0x40, Size = 16,
                ArrayCount = 2, ArrayInnerType = "EnumProperty", ArrayElemSize = 4,
                ArrayInnerAddr = "0x5000",
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "1", RawIntValue = 1 },
                    new() { Index = 1, Value = "2", RawIntValue = 2 },
                },
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        Assert.Contains("<VariableType>4 Bytes</VariableType>", xml);
    }

    [Fact]
    public void GenerateInstanceXml_ByteWideEnumSet_EmitsByteRecords()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "Flags", TypeName = "SetProperty", Offset = 0x90, Size = 80,
                SetCount = 2, SetElemType = "EnumProperty", SetElemSize = 1,
                SetDataAddr = "0x9000",
                SetElements = new List<ContainerElementValue>
                {
                    new() { Index = 0, Key = "1" },
                    new() { Index = 1, Key = "3" },
                },
            },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

        Assert.Contains("<VariableType>Byte</VariableType>", xml);
        Assert.DoesNotContain("<VariableType>4 Bytes</VariableType>", xml);
    }
}
