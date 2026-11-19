using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for the inheritance-aware computed properties on
/// <see cref="PropertySearchMatch"/>.
///
/// The DLL-side dedup (defining-class resolution, count accumulation,
/// preview-source selection) needs a live game to test end-to-end --
/// covered by manual smoke testing on Everspace 2 / Titan Quest II /
/// FF7 Rebirth. These tests cover the C#-side display contract:
/// what the user actually sees in the DataGrid.
/// </summary>
public class PropertySearchMatchTests
{
    [Fact]
    public void InheritanceBadge_ZeroInheritors_IsEmpty()
    {
        // A property unique to one class (typical BP-added field on a
        // game-specific class) gets no badge -- a clean column reads as
        // "this is the only place it lives, no inheritance noise".
        var m = new PropertySearchMatch { InheritedByCount = 0 };
        Assert.Equal("", m.InheritanceBadge);
    }

    [Fact]
    public void InheritanceBadge_OneInheritor_Singular()
    {
        var m = new PropertySearchMatch { InheritedByCount = 1 };
        Assert.Equal("+1 inheritor", m.InheritanceBadge);
    }

    [Theory]
    [InlineData(2,    "+2 inheritors")]
    [InlineData(47,   "+47 inheritors")]
    [InlineData(4823, "+4823 inheritors")]
    public void InheritanceBadge_MultipleInheritors_Plural(int count, string expected)
    {
        var m = new PropertySearchMatch { InheritedByCount = count };
        Assert.Equal(expected, m.InheritanceBadge);
    }

    [Fact]
    public void InheritanceTooltip_ZeroInheritors_HighlightsUniqueness()
    {
        // The "unique to this class" tooltip explicitly calls out the
        // game-specific signal so the user knows this field is likely
        // a BP-added cheat target (vs an engine inheritance).
        var m = new PropertySearchMatch
        {
            ClassName = "BP_PlayerCharacter_C",
            ClassPath = "/Game/Player/BP_PlayerCharacter.BP_PlayerCharacter_C",
            InheritedByCount = 0,
        };
        var tip = m.InheritanceTooltip;
        Assert.Contains("unique to BP_PlayerCharacter_C", tip);
        Assert.Contains("game-specific", tip);
        Assert.Contains("/Game/Player/BP_PlayerCharacter", tip);
    }

    [Fact]
    public void InheritanceTooltip_NonZero_ShowsDefiningClass()
    {
        // The "shared field" tooltip points the user at the canonical
        // defining class so they understand all 4823 child rows would
        // touch the same memory.
        var m = new PropertySearchMatch
        {
            ClassName         = "AActor",
            DefiningClassName = "AActor",
            DefiningClassPath = "/Script/Engine.Actor",
            PropOffset        = 0x88,
            InheritedByCount  = 4822,
        };
        var tip = m.InheritanceTooltip;
        Assert.Contains("Defined on AActor", tip);
        Assert.Contains("0x88", tip);
        Assert.Contains("4822 subclass", tip);
        Assert.Contains("identical effect", tip);
        Assert.Contains("/Script/Engine.Actor", tip);
    }

    [Fact]
    public void OffsetHex_Format_IsZeroX()
    {
        // Existing behaviour preserved -- "0x88" not "0X88" or "88".
        var m = new PropertySearchMatch { PropOffset = 0x88 };
        Assert.Equal("0x88", m.OffsetHex);

        var zero = new PropertySearchMatch { PropOffset = 0 };
        Assert.Equal("0x0", zero.OffsetHex);

        var bigger = new PropertySearchMatch { PropOffset = 0xDEAD };
        Assert.Equal("0xDEAD", bigger.OffsetHex);
    }

    [Fact]
    public void TypeDisplay_StructProperty_ShowsStructName()
    {
        var m = new PropertySearchMatch
        {
            PropType = "StructProperty",
            StructType = "Vector",
        };
        Assert.Equal("StructProperty (Vector)", m.TypeDisplay);
    }

    [Fact]
    public void TypeDisplay_ArrayProperty_ShowsInnerType()
    {
        var m = new PropertySearchMatch
        {
            PropType = "ArrayProperty",
            InnerType = "ObjectProperty",
        };
        Assert.Equal("ArrayProperty [ObjectProperty]", m.TypeDisplay);
    }

    [Fact]
    public void TypeDisplay_PlainScalar_ShowsTypeOnly()
    {
        var m = new PropertySearchMatch { PropType = "FloatProperty" };
        Assert.Equal("FloatProperty", m.TypeDisplay);
    }

    // === Deep / nested match gating ===

    [Fact]
    public void DirectField_NotNested_ShowsScalarActions_NoPathTooltip()
    {
        // A normal direct-field row: Copy Offset / Freeze apply, and the
        // Property column gets no extra tooltip (null => no popup).
        var m = new PropertySearchMatch { PropName = "Health", IsNested = false };
        Assert.True(m.ShowScalarActions);
        Assert.Null(m.PropNameTooltip);
    }

    [Fact]
    public void NestedMatch_HidesScalarActions_AndExplainsPath()
    {
        // A deep (nested) row has a dotted path and no class-absolute offset,
        // so Copy Offset / Freeze are hidden (ShowScalarActions == false) and
        // the tooltip explains the path is a drill route + names the class.
        var m = new PropertySearchMatch
        {
            ClassName = "BP_LifeSaveData_C",
            PropName  = "SaveSlotList[].MsTuneData.GP",
            IsNested  = true,
        };
        Assert.False(m.ShowScalarActions);
        var tip = m.PropNameTooltip;
        Assert.NotNull(tip);
        Assert.Contains("SaveSlotList[].MsTuneData.GP", tip);
        Assert.Contains("BP_LifeSaveData_C", tip);
        Assert.Contains("finder", tip);
    }

    [Fact]
    public void IsNested_DefaultsFalse()
    {
        // Back-compat: a row deserialized from an older DLL (no is_nested
        // field) defaults to a normal direct-field row.
        var m = new PropertySearchMatch();
        Assert.False(m.IsNested);
        Assert.True(m.ShowScalarActions);
    }
}
