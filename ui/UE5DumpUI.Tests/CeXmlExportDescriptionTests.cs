using System.Collections.Generic;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Covers the memory-record Description format rework: the bare-Name default plus
/// the opt-in "+Offset" / "+Type" decorations on Copy CE XML / Copy CE Field.
/// Rules under test:
///   - default (both off): Description = bare node name (no [N x Type], no {Map},
///     no (Class), no object instance name)
///   - +Offset: appends the node's own field offset as bare uppercase hex
///   - +Type:   appends the node's class / struct / element type
///   - both:    "Name (OFFSET, Type)"
///   - object array/map/set elements drop the instance name; scalar map/set keys
///     and DataTable row names are kept
///   - a guessed field's Description is never decorated; its parents follow the rules
///   - the folded (Collapse chain) spine honours +Offset but never +Type
///   - enum DropDownList link keys stay consistent with the decorated owner desc
/// </summary>
public class CeXmlExportDescriptionTests
{
    // ----- Field fixtures -----------------------------------------------------

    // Unresolved object-pointer field (offset 0x7E0) → EmitNavigableField path.
    private static LiveFieldValue PointerField() => new()
    {
        Name = "AbilitySystemComponent", TypeName = "ObjectProperty", Offset = 0x7E0, Size = 8,
        PtrAddress = "0x12340000", PtrName = "AbilitySystemComponent",
        PtrClassName = "LSAbilitySystemComponent",
    };

    // Unresolved inline struct field (offset 0x30) → EmitNavigableField path.
    private static LiveFieldValue StructField() => new()
    {
        Name = "HealthData", TypeName = "StructProperty", Offset = 0x30, Size = 8,
        StructDataAddr = "0x12340030", StructTypeName = "FGameplayAttributeData",
    };

    // Scalar float array (offset 0x100).
    private static LiveFieldValue FloatArrayField() => new()
    {
        Name = "Scores", TypeName = "ArrayProperty", Offset = 0x100, Size = 16,
        ArrayCount = 2, ArrayInnerType = "FloatProperty", ArrayElemSize = 4,
        ArrayElements = new List<ArrayElementValue>
        {
            new() { Index = 0, Value = "1.5", Hex = "3FC00000" },
            new() { Index = 1, Value = "2.5", Hex = "40200000" },
        },
    };

    // Unresolved object array (offset 0x10A8) whose elements carry an instance name
    // (with the UE numeric suffix) plus a class.
    private static LiveFieldValue ObjectArrayField() => new()
    {
        Name = "SpawnedAttributes", TypeName = "ArrayProperty", Offset = 0x10A8, Size = 16,
        ArrayCount = 1, ArrayInnerType = "ObjectProperty", ArrayElemSize = 8,
        ArrayElements = new List<ArrayElementValue>
        {
            new()
            {
                Index = 0, Value = "ShieldAttributeSet_2147477186",
                PtrAddress = "0x3B0330400", PtrName = "ShieldAttributeSet_2147477186",
                PtrClassName = "ShieldAttributeSet",
            },
        },
    };

    private static string Xml(IReadOnlyList<LiveFieldValue> fields,
        bool offset = false, bool type = false) =>
        CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields,
            descShowOffset: offset, descShowType: type);

    // ----- Default: bare names ------------------------------------------------

    [Fact]
    public void Default_AllNodesShowBareNameOnly()
    {
        var xml = Xml(new[] { PointerField(), StructField(), FloatArrayField(), ObjectArrayField() });

        Assert.Contains("\"AbilitySystemComponent\"", xml);
        Assert.Contains("\"HealthData\"", xml);
        Assert.Contains("\"Scores\"", xml);
        Assert.Contains("\"SpawnedAttributes\"", xml);
        Assert.Contains("\"[0]\"", xml);

        // No type/class, no array descriptor, no instance name by default.
        Assert.DoesNotContain("(LSAbilitySystemComponent)", xml);
        Assert.DoesNotContain("(FGameplayAttributeData)", xml);
        Assert.DoesNotContain("[2 x FloatProperty", xml);
        Assert.DoesNotContain("ShieldAttributeSet_2147477186", xml);
        Assert.DoesNotContain("(ShieldAttributeSet)", xml);
    }

    // ----- +Offset ------------------------------------------------------------

    [Fact]
    public void ShowOffset_AppendsFieldOffsetHex()
    {
        var xml = Xml(new[] { PointerField(), StructField() }, offset: true);

        Assert.Contains("\"AbilitySystemComponent (7E0)\"", xml);
        Assert.Contains("\"HealthData (30)\"", xml);
        // Offset only — no type.
        Assert.DoesNotContain("LSAbilitySystemComponent", xml);
    }

    [Fact]
    public void ShowOffset_ArrayNodeAndElementsCarryOffsets()
    {
        var xml = Xml(new[] { FloatArrayField() }, offset: true);

        Assert.Contains("\"Scores (100)\"", xml);
        // Elements are at +0 / +4 within the dereferenced Data pointer.
        Assert.Contains("\"[0] (0)\"", xml);
        Assert.Contains("\"[1] (4)\"", xml);
    }

    // ----- +Type --------------------------------------------------------------

    [Fact]
    public void ShowType_AppendsClassStructAndElementClass()
    {
        var xml = Xml(new[] { PointerField(), StructField(), ObjectArrayField() }, type: true);

        Assert.Contains("\"AbilitySystemComponent (LSAbilitySystemComponent)\"", xml);
        Assert.Contains("\"HealthData (FGameplayAttributeData)\"", xml);
        // Object array element: class re-added, instance name still dropped.
        Assert.Contains("\"[0] (ShieldAttributeSet)\"", xml);
        Assert.DoesNotContain("ShieldAttributeSet_2147477186", xml);
        // No offsets when only +Type is on.
        Assert.DoesNotContain("(7E0", xml);
    }

    [Fact]
    public void ShowType_ArrayNodeShowsElementType()
    {
        var xml = Xml(new[] { FloatArrayField() }, type: true);
        Assert.Contains("\"Scores (FloatProperty)\"", xml);
    }

    // ----- Both ---------------------------------------------------------------

    [Fact]
    public void ShowBoth_RendersOffsetThenType()
    {
        var xml = Xml(new[] { PointerField() }, offset: true, type: true);
        Assert.Contains("\"AbilitySystemComponent (7E0, LSAbilitySystemComponent)\"", xml);
    }

    // ----- Scalar map key kept ------------------------------------------------

    [Fact]
    public void MapScalarKey_IsKept_AndNodeStripsDescriptor()
    {
        var map = new LiveFieldValue
        {
            Name = "ScoreMap", TypeName = "MapProperty", Offset = 0x50, Size = 80,
            MapCount = 1, MapKeyType = "IntProperty", MapValueType = "IntProperty",
            MapKeySize = 4, MapValueSize = 4, MapValueOffset = 4, MapDataAddr = "0x9000",
            MapElements = new List<ContainerElementValue>
            {
                new() { Index = 0, Key = "42", Value = "100", KeyHex = "2A000000", ValueHex = "64000000" },
            },
        };

        var def = Xml(new[] { map });
        Assert.Contains("\"ScoreMap\"", def);              // node: bare name
        Assert.Contains("\"[0] 42\"", def);                // scalar key kept
        Assert.DoesNotContain("{Map:", def);

        var typed = Xml(new[] { map }, type: true);
        Assert.Contains("\"ScoreMap (IntProperty → IntProperty)\"", typed);
    }

    // ----- Guessed field untouched, parents decorated -------------------------

    [Fact]
    public void GuessedField_DescriptionNeverDecorated_ButSpineIs()
    {
        var breadcrumbs = new[]
        {
            new BreadcrumbItem { Address = "0x1000", Label = "Root", FieldName = "GWorld" },
            new BreadcrumbItem
            {
                Address = "0x2000", Label = "Pawn", FieldName = "PlayerPawn",
                FieldOffset = 0x30, IsPointerDeref = true,
            },
        };
        var guessed = new LiveFieldValue
        {
            Name = "GuessedHP", TypeName = "FloatProperty", Offset = 0x40, Size = 4,
            IsGuessed = true, TypedValue = "777",
        };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "0x1000", "GWorld", breadcrumbs, new[] { guessed },
            includeGuessed: true, descShowOffset: true, descShowType: true);

        // Guessed leaf is verbatim — no "(40)" / type appended.
        Assert.Contains("\"GuessedHP\"", xml);
        Assert.DoesNotContain("GuessedHP (", xml);
        // The spine node above it IS decorated with its offset.
        Assert.Contains("\"PlayerPawn (30)\"", xml);
    }

    // ----- Folded spine: offset yes, type never -------------------------------

    [Fact]
    public void FoldedChain_AppliesOffset_ButNeverType()
    {
        // Three breadcrumbs (root + 2 spine) so the spine actually folds. The middle
        // hop is a container view carrying a ContainerField, which WOULD yield a type
        // in the nested spine — the folded spine must still suppress it.
        var containerField = new LiveFieldValue
        {
            Name = "PlayerArray", TypeName = "ArrayProperty", Offset = 0x8,
            ArrayCount = 1, ArrayInnerType = "ObjectProperty", ArrayElemSize = 8,
        };
        var breadcrumbs = new[]
        {
            new BreadcrumbItem { Address = "0x1000", Label = "Root", FieldName = "GWorld" },
            new BreadcrumbItem
            {
                Address = "0x2000", Label = "PlayerArray [1 x ObjectProperty]",
                FieldName = "PlayerArray", FieldOffset = 0x8,
                IsContainerView = true, ContainerField = containerField,
            },
            new BreadcrumbItem
            {
                Address = "0x3000", Label = "PlayerState", FieldName = "PlayerState",
                FieldOffset = 0x250, IsPointerDeref = true,
            },
        };
        var leaf = new LiveFieldValue { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4 };

        var folded = CeXmlExportService.GenerateHierarchicalXml(
            "0x1000", "GWorld", breadcrumbs, new[] { leaf },
            flattenChain: true, descShowOffset: true, descShowType: true);

        // Folded spine collapses both hops into one " ▸ "-joined Description with
        // per-hop offsets but NO type, even though descShowType is on.
        Assert.Contains("PlayerArray (8)", folded);
        Assert.Contains("PlayerState (250)", folded);
        Assert.Contains("▸", folded);
        Assert.DoesNotContain("ObjectProperty", folded);

        // Sanity: the NESTED spine (no fold) DOES surface the container type.
        var nested = CeXmlExportService.GenerateHierarchicalXml(
            "0x1000", "GWorld", breadcrumbs, new[] { leaf }, descShowType: true);
        Assert.Contains("\"PlayerArray (ObjectProperty)\"", nested);
    }

    // ----- AOB-wrapped path (the in-game Copy-CE-Field-with-AOB path) ---------

    [Fact]
    public void AobWrapped_ShowOffsetOnly_DecoratesSpineAndLeaves()
    {
        // Mirrors the in-game scenario: AOB checked → GenerateAobWrappedXml, with
        // ONLY +Offset on. The spine pointer nodes and the leaf must carry offsets.
        var breadcrumbs = new[]
        {
            new BreadcrumbItem { Address = "0x1000", Label = "Root", FieldName = "GWorld" },
            new BreadcrumbItem { Address = "0x2000", Label = "GameState", FieldName = "GameState", FieldOffset = 0x1B0, IsPointerDeref = true },
            new BreadcrumbItem { Address = "0x3000", Label = "PawnPrivate", FieldName = "PawnPrivate", FieldOffset = 0x340, IsPointerDeref = true },
        };
        var leaf = new LiveFieldValue { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4 };

        var xml = CeXmlExportService.GenerateAobWrappedXml(
            "GWorld", breadcrumbs, new[] { leaf },
            "AA BB CC", 0, 3, "Game.exe",
            descShowOffset: true, descShowType: false);

        Assert.Contains("\"GameState (1B0)\"", xml);
        Assert.Contains("\"PawnPrivate (340)\"", xml);
        Assert.Contains("\"Health (10)\"", xml);
        // Offset only — no type appended.
        Assert.DoesNotContain("FloatProperty)", xml);
    }

    // ----- +Type on spine pointer / array-element nodes (TargetClassName) ------

    [Fact]
    public void SpineType_PointerAndElementNodes_ShowTargetClass()
    {
        // GameState (pointer) and [0] (array element) carry the resolved target class
        // on the breadcrumb, so +Type surfaces it on the navigation spine — not just
        // on container-view nodes.
        var breadcrumbs = new[]
        {
            new BreadcrumbItem { Address = "0x1000", Label = "Root", FieldName = "GWorld" },
            new BreadcrumbItem { Address = "0x2000", Label = "GameState", FieldName = "GameState", FieldOffset = 0x1B0, TargetClassName = "BP_GameState_C", IsPointerDeref = true },
            new BreadcrumbItem { Address = "0x3000", Label = "[0]", FieldName = "[0]", FieldOffset = 0, TargetClassName = "PlayerState", IsPointerDeref = true },
            new BreadcrumbItem { Address = "0x4000", Label = "PawnPrivate", FieldName = "PawnPrivate", FieldOffset = 0x340, TargetClassName = "BP_PlayerCharacter_C", IsPointerDeref = true },
        };
        var leaf = new LiveFieldValue { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4 };

        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "0x1000", "GWorld", breadcrumbs, new[] { leaf }, descShowType: true);

        Assert.Contains("\"GameState (BP_GameState_C)\"", xml);
        Assert.Contains("\"[0] (PlayerState)\"", xml);
        Assert.Contains("\"PawnPrivate (BP_PlayerCharacter_C)\"", xml);

        // +Offset + +Type together on a spine pointer node → "name (offset, class)".
        var both = CeXmlExportService.GenerateHierarchicalXml(
            "0x1000", "GWorld", breadcrumbs, new[] { leaf },
            descShowOffset: true, descShowType: true);
        Assert.Contains("\"PawnPrivate (340, BP_PlayerCharacter_C)\"", both);
    }

    // ----- Emit safety budget (OOM guard) -------------------------------------

    [Fact]
    public void Emit_DenseObjectGraph_TruncatesInsteadOfOom()
    {
        // 10 fully-connected objects: every field points to one of the 10. The
        // per-PATH cycle guard (_emitPath) permits a shared object under many parents,
        // so without a global budget the drilldown re-expands them into millions of
        // entries → StringBuilder OutOfMemory (the in-game Copy CE XML crash). The guard
        // must stop it and flag the export truncated. [CEXML-CAP-60K] The guard is 256 Mi
        // characters now, so this runs under a lowered one rather than build half a gigabyte.
        var addrs = Enumerable.Range(0, 10).Select(i => $"0x{0x1000 + i:X}").ToArray();
        var resolved = new Dictionary<string, List<LiveFieldValue>>(System.StringComparer.Ordinal);
        foreach (var a in addrs)
        {
            var fields = new List<LiveFieldValue>();
            for (int j = 0; j < addrs.Length; j++)
                fields.Add(new LiveFieldValue
                {
                    Name = $"f{j}", TypeName = "ObjectProperty", Offset = j * 8, Size = 8,
                    PtrAddress = addrs[j], PtrName = $"Obj{j}", PtrClassName = "UObject",
                });
            resolved[a] = fields;
        }
        var root = new[]
        {
            new LiveFieldValue
            {
                Name = "Root", TypeName = "ObjectProperty", Offset = 0, Size = 8,
                PtrAddress = addrs[0], PtrName = "Obj0", PtrClassName = "UObject",
            },
        };

        const int budget = 2_000_000;
        string xml;
        using (CeXmlExportService.OverrideEmitCharBudgetForTest(budget))
            xml = CeXmlExportService.GenerateInstanceXml(
                "\"Game.exe\"+1000", "MyObj", "UMyClass", root, resolvedInstances: resolved);

        Assert.True(CeXmlExportService.LastExportTruncated);
        // Completed (no OOM/hang) and bounded by the guard, not millions of entries.
        Assert.True(xml.Length < budget + 16_384, $"xml unexpectedly large: {xml.Length} chars");
    }

    [Fact]
    public void Emit_SmallGraph_NotTruncated()
    {
        var xml = Xml(new[] { PointerField(), StructField(), FloatArrayField() });
        Assert.False(CeXmlExportService.LastExportTruncated);
    }

    // ----- Shared-object dedup -------------------------------------------------

    private static int CountOccurrences(string s, string sub) => s.Split(sub).Length - 1;

    [Fact]
    public void Dedup_SharedObject_ExpandedOnce_ThenFlatShared()
    {
        // Two fields point to the SAME resolved object. With dedup the first expands
        // its subtree; the second becomes a flat "(shared)" pointer leaf.
        var shared = new List<LiveFieldValue>
        {
            new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4 },
        };
        var resolved = new Dictionary<string, List<LiveFieldValue>>(System.StringComparer.Ordinal)
        {
            ["0xB000"] = shared,
        };
        var root = new[]
        {
            new LiveFieldValue { Name = "RefA", TypeName = "ObjectProperty", Offset = 0, Size = 8, PtrAddress = "0xB000", PtrName = "Obj", PtrClassName = "UObject" },
            new LiveFieldValue { Name = "RefB", TypeName = "ObjectProperty", Offset = 8, Size = 8, PtrAddress = "0xB000", PtrName = "Obj", PtrClassName = "UObject" },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", root, resolvedInstances: resolved, dedupShared: true);

        Assert.Equal(1, CountOccurrences(xml, "\"Health\""));   // expanded exactly once
        Assert.Contains("RefB (shared)", xml);                  // 2nd ref is a flat shared leaf
        Assert.False(CeXmlExportService.LastExportTruncated);
    }

    [Fact]
    public void Dedup_Off_ReexpandsSharedObject()
    {
        var shared = new List<LiveFieldValue>
        {
            new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4 },
        };
        var resolved = new Dictionary<string, List<LiveFieldValue>>(System.StringComparer.Ordinal)
        {
            ["0xB000"] = shared,
        };
        var root = new[]
        {
            new LiveFieldValue { Name = "RefA", TypeName = "ObjectProperty", Offset = 0, Size = 8, PtrAddress = "0xB000", PtrName = "Obj", PtrClassName = "UObject" },
            new LiveFieldValue { Name = "RefB", TypeName = "ObjectProperty", Offset = 8, Size = 8, PtrAddress = "0xB000", PtrName = "Obj", PtrClassName = "UObject" },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", root, resolvedInstances: resolved, dedupShared: false);

        Assert.Equal(2, CountOccurrences(xml, "\"Health\""));   // legacy: expanded under both
        Assert.DoesNotContain("(shared)", xml);
    }

    [Fact]
    public void Dedup_DenseGraph_StaysSmall_NotTruncated()
    {
        // The same 10-node fully-connected graph that OOMs without a cap — with dedup
        // each object is emitted once, so the export is tiny and never trips the budget.
        var addrs = Enumerable.Range(0, 10).Select(i => $"0x{0x2000 + i:X}").ToArray();
        var resolved = new Dictionary<string, List<LiveFieldValue>>(System.StringComparer.Ordinal);
        foreach (var a in addrs)
        {
            var fields = new List<LiveFieldValue>();
            for (int j = 0; j < addrs.Length; j++)
                fields.Add(new LiveFieldValue
                {
                    Name = $"f{j}", TypeName = "ObjectProperty", Offset = j * 8, Size = 8,
                    PtrAddress = addrs[j], PtrName = $"Obj{j}", PtrClassName = "UObject",
                });
            resolved[a] = fields;
        }
        var root = new[]
        {
            new LiveFieldValue { Name = "Root", TypeName = "ObjectProperty", Offset = 0, Size = 8, PtrAddress = addrs[0], PtrName = "Obj0", PtrClassName = "UObject" },
        };

        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", root, resolvedInstances: resolved, dedupShared: true);

        Assert.False(CeXmlExportService.LastExportTruncated);
        Assert.True(xml.Length < 200_000, $"dedup export unexpectedly large: {xml.Length} chars");
    }

    // ----- DropDownList link integrity with decoration ------------------------

    [Fact]
    public void EnumArrayDropDownLink_StaysConsistent_WhenDecorated()
    {
        var fields = new[]
        {
            new LiveFieldValue
            {
                Name = "ShipTypes", TypeName = "ArrayProperty", Offset = 0x200, Size = 16,
                ArrayCount = 2, ArrayInnerType = "ByteProperty", ArrayElemSize = 1,
                ArrayEnumAddr = "0x1234",
                ArrayEnumEntries = new List<EnumEntryValue>
                {
                    new() { Value = 0, Name = "EShip::Scout" },
                    new() { Value = 1, Name = "EShip::SpecOps" },
                },
                ArrayElements = new List<ArrayElementValue>
                {
                    new() { Index = 0, Value = "EShip::Scout", Hex = "00", EnumName = "EShip::Scout", RawIntValue = 0 },
                    new() { Index = 1, Value = "EShip::SpecOps", Hex = "01", EnumName = "EShip::SpecOps", RawIntValue = 1 },
                },
            },
        };

        var xml = Xml(fields, offset: true);

        // The owner GroupHeader description is decorated...
        Assert.Contains("\"ShipTypes (200)\"", xml);
        // ...and the child link key MUST match it exactly, or CE can't resolve the list.
        Assert.Contains("<DropDownListLink>ShipTypes (200)</DropDownListLink>", xml);
    }
}
