using System.Collections.Generic;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for the deeply-nested container reach: the
/// <see cref="ContainerMatch"/> nested-chain model + display path, and the
/// flattened drill path the Live Walker walks to land on a value buried in
/// separately-allocated nested containers.
/// </summary>
public class DeepContainerChainTests
{
    // --- ContainerMatch.DisplayPath / DeepestIntraOffset / IsDeeplyNested ---

    [Fact]
    public void DisplayPath_ShallowMatch_UnchangedShape()
    {
        // A plain 1-level struct-element value (e.g. SaveSlotList[1].GP at +0x4D8).
        var m = new ContainerMatch
        {
            OwnerName    = "BP_LifeSaveData_C",
            FieldName    = "SaveSlotList",
            ElementIndex = 1,
            IntraOffset  = 0x4D8,
            InnerType    = "StructProperty",
        };

        Assert.False(m.IsDeeplyNested);
        Assert.Equal(0x4D8, m.DeepestIntraOffset);
        Assert.Equal("BP_LifeSaveData_C.SaveSlotList[1]+0x4D8", m.DisplayPath);
    }

    [Fact]
    public void DisplayPath_ShallowLeafElement_NoIntraSuffix()
    {
        // A scalar leaf element (the element IS the value) → no +0xK suffix.
        var m = new ContainerMatch
        {
            OwnerName    = "BP_Inventory_C",
            FieldName    = "Gold",
            ElementIndex = 3,
            IntraOffset  = 0,
        };

        Assert.Equal("BP_Inventory_C.Gold[3]", m.DisplayPath);
    }

    [Fact]
    public void DisplayPath_DeeplyNested_SpansFullChain()
    {
        // The SEED repro: SaveSlotList[1].MsTuneData.MsTunes[0].WeaponTuneList[0].Tunes[42].
        var m = new ContainerMatch
        {
            OwnerName    = "BP_LifeSaveData_C",
            FieldName    = "SaveSlotList",
            ElementIndex = 1,
            IntraOffset  = 0,        // outermost element is a struct we descended into
            InnerType    = "StructProperty",
            NestedChain  = new List<ContainerHop>
            {
                new() { FieldName = "MsTuneData.MsTunes", ElementIndex = 0, IntraOffset = 0, MapValueSide = true },
                new() { FieldName = "WeaponTuneList",      ElementIndex = 0, IntraOffset = 0 },
                new() { FieldName = "Tunes",               ElementIndex = 42, IntraOffset = 0 },
            },
        };

        Assert.True(m.IsDeeplyNested);
        Assert.Equal(0, m.DeepestIntraOffset);
        Assert.Equal(
            "BP_LifeSaveData_C.SaveSlotList[1].MsTuneData.MsTunes[0].WeaponTuneList[0].Tunes[42]",
            m.DisplayPath);
    }

    [Fact]
    public void DisplayPath_DeeplyNested_UsesDeepestIntraOffset()
    {
        // Deepest hop lands inside a struct element at +0x10 (not a leaf element).
        var m = new ContainerMatch
        {
            OwnerName    = "Save",
            FieldName    = "Slots",
            ElementIndex = 2,
            IntraOffset  = 0,
            NestedChain  = new List<ContainerHop>
            {
                new() { FieldName = "Items", ElementIndex = 5, IntraOffset = 0x10 },
            },
        };

        Assert.Equal(0x10, m.DeepestIntraOffset);
        Assert.Equal("Save.Slots[2].Items[5]+0x10", m.DisplayPath);
    }

    [Fact]
    public void DisplayPath_Deepest_NoteFromDeepestHop()
    {
        var m = new ContainerMatch
        {
            OwnerName    = "Save",
            FieldName    = "Slots",
            ElementIndex = 0,
            Note         = "",   // outermost is solid
            NestedChain  = new List<ContainerHop>
            {
                new() { FieldName = "Items", ElementIndex = 9, IntraOffset = 0, Note = "freed" },
            },
        };

        Assert.Equal("Save.Slots[0].Items[9] (freed)", m.DisplayPath);
    }

    // --- LiveWalkerViewModel.BuildContainerDrillPath ---

    [Fact]
    public void BuildDrillPath_ShallowMatch_SingleHop()
    {
        var m = new ContainerMatch
        {
            FieldName    = "SaveSlotList",
            ElementIndex = 1,
            IntraOffset  = 0x4D8,
        };

        var hops = LiveWalkerViewModel.BuildContainerDrillPath(m);

        Assert.Single(hops);
        Assert.Equal(("SaveSlotList", 1, 0x4D8), hops[0]);
    }

    [Fact]
    public void BuildDrillPath_DeeplyNested_OutermostFirstThenChain()
    {
        var m = new ContainerMatch
        {
            FieldName    = "SaveSlotList",
            ElementIndex = 1,
            IntraOffset  = 0,
            NestedChain  = new List<ContainerHop>
            {
                new() { FieldName = "MsTuneData.MsTunes", ElementIndex = 0,  IntraOffset = 0 },
                new() { FieldName = "WeaponTuneList",      ElementIndex = 0,  IntraOffset = 0 },
                new() { FieldName = "Tunes",               ElementIndex = 42, IntraOffset = 0 },
            },
        };

        var hops = LiveWalkerViewModel.BuildContainerDrillPath(m);

        Assert.Equal(4, hops.Count);
        Assert.Equal(("SaveSlotList", 1, 0), hops[0]);
        Assert.Equal(("MsTuneData.MsTunes", 0, 0), hops[1]);
        Assert.Equal(("WeaponTuneList", 0, 0), hops[2]);
        Assert.Equal(("Tunes", 42, 0), hops[3]);   // deepest = the value's element
    }

    // --- LiveWalkerViewModel.TryParseContainerPath (Value Search / SPC 🌍 deep-drill) ---

    [Fact]
    public void TryParseContainerPath_SingleStructArrayInner()
    {
        Assert.True(LiveWalkerViewModel.TryParseContainerPath("SaveSlotList[1].GP", out var segs));
        Assert.Equal(2, segs.Count);
        Assert.Equal(("SaveSlotList", 1), segs[0]);
        Assert.Equal(("GP", -1), segs[1]);
    }

    [Fact]
    public void TryParseContainerPath_LeadingDirectStructThenArray()
    {
        Assert.True(LiveWalkerViewModel.TryParseContainerPath("Save.SaveSlotList[2].MsTuneData.GP2", out var segs));
        Assert.Equal(4, segs.Count);
        Assert.Equal(("Save", -1), segs[0]);          // leading direct struct
        Assert.Equal(("SaveSlotList", 2), segs[1]);
        Assert.Equal(("MsTuneData", -1), segs[2]);
        Assert.Equal(("GP2", -1), segs[3]);
    }

    [Fact]
    public void TryParseContainerPath_LeafArrayElement_IsAContainerPath()
    {
        // A bare leaf-array element ("Items[3]") IS a drillable container path
        // (the element itself is the value) — the multi-"[N]" drill handles it.
        Assert.True(LiveWalkerViewModel.TryParseContainerPath("Items[3]", out var segs));
        Assert.Single(segs);
        Assert.Equal(("Items", 3), segs[0]);
    }

    [Fact]
    public void TryParseContainerPath_DeeplyNested_SEEDRepro()
    {
        Assert.True(LiveWalkerViewModel.TryParseContainerPath(
            "SaveSlotList[0].MsTuneData.MsTunes[0].WeaponTuneList[0].Tunes[2]", out var segs));
        Assert.Equal(5, segs.Count);
        Assert.Equal(("SaveSlotList", 0), segs[0]);
        Assert.Equal(("MsTuneData", -1), segs[1]);
        Assert.Equal(("MsTunes", 0), segs[2]);
        Assert.Equal(("WeaponTuneList", 0), segs[3]);
        Assert.Equal(("Tunes", 2), segs[^1]);   // last segment = the value's element
    }

    [Theory]
    [InlineData("Health")]        // plain field — no "[N]"
    [InlineData("")]              // empty
    [InlineData("[0].GP")]        // no name before "["
    [InlineData("Cargo[].GP")]    // empty index
    [InlineData("Cargo[-1]")]     // negative index
    [InlineData("A.[2]")]         // empty segment name before "["
    [InlineData("Stats.Health")]  // dotted plain field — still no "[N]"
    public void TryParseContainerPath_RejectsNonContainerOrMalformed(string name)
    {
        Assert.False(LiveWalkerViewModel.TryParseContainerPath(name, out _));
    }

    [Fact]
    public void TryParseContainerPath_NestedStructPath_DrillsWhenIndexNotRequired()
    {
        // Camera POV fields are nested two struct levels deep with no "[N]".
        // Default (container-only) rejects them; requireIndex:false accepts + parses.
        Assert.False(LiveWalkerViewModel.TryParseContainerPath("CameraCachePrivate.POV.Location", out _));
        Assert.True(LiveWalkerViewModel.TryParseContainerPath(
            "CameraCachePrivate.POV.Location", out var segs, requireIndex: false));
        Assert.Equal(3, segs.Count);
        Assert.Equal(("CameraCachePrivate", -1), segs[0]);
        Assert.Equal(("POV", -1), segs[1]);
        Assert.Equal(("Location", -1), segs[^1]);
        // A single bare field is still NOT a path even with requireIndex:false.
        Assert.False(LiveWalkerViewModel.TryParseContainerPath("Velocity", out _, requireIndex: false));
    }

    // --- LiveWalkerViewModel.FindFieldByOffsetOrContaining (Locate-in-GWorld
    //     scroll-to-field; nested-struct leaves like GAS FGameplayAttributeData) ---

    [Fact]
    public void FindFieldByOffsetOrContaining_ExactOffsetWins()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "MaximumHealth",      Offset = 0xC8 },
            new() { Name = "CurrentHealth",      Offset = 0x118 },
            new() { Name = "RegenerationHealth", Offset = 0x128 },
        };
        Assert.Equal("CurrentHealth",
            LiveWalkerViewModel.FindFieldByOffsetOrContaining(fields, 0x118)?.Name);
    }

    [Fact]
    public void FindFieldByOffsetOrContaining_NestedStructLeaf_LandsOnContainingField()
    {
        // GAS FGameplayAttributeData: the matched leaf is CurrentHealth.CurrentValue
        // at owner+0x120 — inside the CurrentHealth StructProperty at 0x118, with no
        // top-level row at 0x120 — so it lands on CurrentHealth. A different struct's
        // inner value (MaximumHealth.CurrentValue at 0xD0) lands on MaximumHealth, so
        // distinct slots reach distinct rows, never both the same one (a TQ2 shape).
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "MaximumHealth",         Offset = 0xC8 },
            new() { Name = "FreeReservationHealth", Offset = 0xD8 },
            new() { Name = "CurrentHealth",         Offset = 0x118 },
            new() { Name = "RegenerationHealth",    Offset = 0x128 },
        };
        Assert.Equal("CurrentHealth",
            LiveWalkerViewModel.FindFieldByOffsetOrContaining(fields, 0x120)?.Name);
        Assert.Equal("MaximumHealth",
            LiveWalkerViewModel.FindFieldByOffsetOrContaining(fields, 0xD0)?.Name);
    }

    [Fact]
    public void FindFieldByOffsetOrContaining_OffsetBeforeAnyField_ReturnsNull()
    {
        var fields = new List<LiveFieldValue> { new() { Name = "A", Offset = 0x10 } };
        Assert.Null(LiveWalkerViewModel.FindFieldByOffsetOrContaining(fields, 0x4));
    }
}
