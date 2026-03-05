using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

public class KnownStructLayoutTests
{
    // --- FVector ---

    [Fact]
    public void FVector_UE4_Is12Bytes_ThreeFloats()
    {
        var layout = KnownStructLayouts.GetLayout("Vector", ueVersion: 427);
        Assert.NotNull(layout);
        Assert.Equal("Vector", layout.StructName);
        Assert.Equal(12, layout.TotalSize);
        Assert.Equal(3, layout.Fields.Count);

        Assert.Equal("X", layout.Fields[0].Name);
        Assert.Equal("FloatProperty", layout.Fields[0].TypeName);
        Assert.Equal(0, layout.Fields[0].Offset);
        Assert.Equal(4, layout.Fields[0].Size);

        Assert.Equal("Y", layout.Fields[1].Name);
        Assert.Equal(4, layout.Fields[1].Offset);

        Assert.Equal("Z", layout.Fields[2].Name);
        Assert.Equal(8, layout.Fields[2].Offset);
    }

    [Fact]
    public void FVector_UE5_Is24Bytes_ThreeDoubles()
    {
        var layout = KnownStructLayouts.GetLayout("Vector", ueVersion: 505);
        Assert.NotNull(layout);
        Assert.Equal("Vector", layout.StructName);
        Assert.Equal(24, layout.TotalSize);
        Assert.Equal(3, layout.Fields.Count);

        Assert.Equal("X", layout.Fields[0].Name);
        Assert.Equal("DoubleProperty", layout.Fields[0].TypeName);
        Assert.Equal(0, layout.Fields[0].Offset);
        Assert.Equal(8, layout.Fields[0].Size);

        Assert.Equal("Y", layout.Fields[1].Name);
        Assert.Equal(8, layout.Fields[1].Offset);

        Assert.Equal("Z", layout.Fields[2].Name);
        Assert.Equal(16, layout.Fields[2].Offset);
    }

    [Fact]
    public void FVector_UnknownVersion_TreatedAsUE4()
    {
        var layout = KnownStructLayouts.GetLayout("Vector", ueVersion: 0);
        Assert.NotNull(layout);
        Assert.Equal(12, layout.TotalSize);
        Assert.Equal("FloatProperty", layout.Fields[0].TypeName);
    }

    // --- FRotator ---

    [Fact]
    public void FRotator_UE4_Is12Bytes_PitchYawRoll()
    {
        var layout = KnownStructLayouts.GetLayout("Rotator", ueVersion: 427);
        Assert.NotNull(layout);
        Assert.Equal(12, layout.TotalSize);
        Assert.Equal(3, layout.Fields.Count);
        Assert.Equal("Pitch", layout.Fields[0].Name);
        Assert.Equal("Yaw", layout.Fields[1].Name);
        Assert.Equal("Roll", layout.Fields[2].Name);
        Assert.Equal("FloatProperty", layout.Fields[0].TypeName);
    }

    [Fact]
    public void FRotator_UE5_Is24Bytes_Doubles()
    {
        var layout = KnownStructLayouts.GetLayout("Rotator", ueVersion: 500);
        Assert.NotNull(layout);
        Assert.Equal(24, layout.TotalSize);
        Assert.Equal("DoubleProperty", layout.Fields[0].TypeName);
    }

    // --- FQuat ---

    [Fact]
    public void FQuat_UE4_Is16Bytes_FourFloats()
    {
        var layout = KnownStructLayouts.GetLayout("Quat", ueVersion: 427);
        Assert.NotNull(layout);
        Assert.Equal(16, layout.TotalSize);
        Assert.Equal(4, layout.Fields.Count);
        Assert.Equal("W", layout.Fields[3].Name);
        Assert.Equal(12, layout.Fields[3].Offset);
    }

    [Fact]
    public void FQuat_UE5_Is32Bytes_FourDoubles()
    {
        var layout = KnownStructLayouts.GetLayout("Quat", ueVersion: 505);
        Assert.NotNull(layout);
        Assert.Equal(32, layout.TotalSize);
        Assert.Equal("DoubleProperty", layout.Fields[3].TypeName);
        Assert.Equal(24, layout.Fields[3].Offset);
    }

    // --- FVector2D ---

    [Fact]
    public void FVector2D_UE4_Is8Bytes()
    {
        var layout = KnownStructLayouts.GetLayout("Vector2D", ueVersion: 427);
        Assert.NotNull(layout);
        Assert.Equal(8, layout.TotalSize);
        Assert.Equal(2, layout.Fields.Count);
    }

    [Fact]
    public void FVector2D_UE5_Is16Bytes()
    {
        var layout = KnownStructLayouts.GetLayout("Vector2D", ueVersion: 505);
        Assert.NotNull(layout);
        Assert.Equal(16, layout.TotalSize);
        Assert.Equal("DoubleProperty", layout.Fields[0].TypeName);
    }

    // --- FColor (stable across versions) ---

    [Fact]
    public void FColor_Is4Bytes_BGRA()
    {
        var layout = KnownStructLayouts.GetLayout("Color", ueVersion: 427);
        Assert.NotNull(layout);
        Assert.Equal(4, layout.TotalSize);
        Assert.Equal(4, layout.Fields.Count);
        Assert.Equal("B", layout.Fields[0].Name);
        Assert.Equal("ByteProperty", layout.Fields[0].TypeName);
        Assert.Equal("G", layout.Fields[1].Name);
        Assert.Equal("R", layout.Fields[2].Name);
        Assert.Equal("A", layout.Fields[3].Name);
    }

    [Fact]
    public void FColor_SameInUE5()
    {
        var ue4 = KnownStructLayouts.GetLayout("Color", ueVersion: 427);
        var ue5 = KnownStructLayouts.GetLayout("Color", ueVersion: 505);
        Assert.NotNull(ue4);
        Assert.NotNull(ue5);
        Assert.Equal(ue4.TotalSize, ue5.TotalSize);
    }

    // --- FLinearColor ---

    [Fact]
    public void FLinearColor_Is16Bytes_RGBA_Floats()
    {
        var layout = KnownStructLayouts.GetLayout("LinearColor", ueVersion: 505);
        Assert.NotNull(layout);
        Assert.Equal(16, layout.TotalSize);
        Assert.Equal(4, layout.Fields.Count);
        Assert.Equal("R", layout.Fields[0].Name);
        Assert.Equal("FloatProperty", layout.Fields[0].TypeName);
        Assert.Equal("A", layout.Fields[3].Name);
    }

    // --- FGuid ---

    [Fact]
    public void FGuid_Is16Bytes_FourUInt32()
    {
        var layout = KnownStructLayouts.GetLayout("Guid", ueVersion: 505);
        Assert.NotNull(layout);
        Assert.Equal(16, layout.TotalSize);
        Assert.Equal(4, layout.Fields.Count);
        Assert.Equal("UInt32Property", layout.Fields[0].TypeName);
        Assert.Equal("A", layout.Fields[0].Name);
        Assert.Equal("D", layout.Fields[3].Name);
    }

    // --- FIntPoint ---

    [Fact]
    public void FIntPoint_Is8Bytes()
    {
        var layout = KnownStructLayouts.GetLayout("IntPoint", ueVersion: 505);
        Assert.NotNull(layout);
        Assert.Equal(8, layout.TotalSize);
        Assert.Equal(2, layout.Fields.Count);
        Assert.Equal("IntProperty", layout.Fields[0].TypeName);
    }

    // --- FIntVector ---

    [Fact]
    public void FIntVector_Is12Bytes()
    {
        var layout = KnownStructLayouts.GetLayout("IntVector", ueVersion: 505);
        Assert.NotNull(layout);
        Assert.Equal(12, layout.TotalSize);
        Assert.Equal(3, layout.Fields.Count);
    }

    // --- GameplayTag / DateTime / Timespan ---

    [Theory]
    [InlineData("GameplayTag")]
    [InlineData("DateTime")]
    [InlineData("Timespan")]
    public void Int64BasedStructs_Are8Bytes(string structName)
    {
        var layout = KnownStructLayouts.GetLayout(structName, ueVersion: 505);
        Assert.NotNull(layout);
        Assert.Equal(8, layout.TotalSize);
        Assert.Single(layout.Fields);
        Assert.Equal("Int64Property", layout.Fields[0].TypeName);
    }

    // --- Unknown struct ---

    [Fact]
    public void UnknownStruct_ReturnsNull()
    {
        Assert.Null(KnownStructLayouts.GetLayout("SomeCustomStruct", ueVersion: 505));
    }

    [Fact]
    public void NullOrEmpty_ReturnsNull()
    {
        Assert.Null(KnownStructLayouts.GetLayout("", ueVersion: 505));
        Assert.Null(KnownStructLayouts.GetLayout(null!, ueVersion: 505));
    }

    // --- IsKnown ---

    [Theory]
    [InlineData("Vector", true)]
    [InlineData("Rotator", true)]
    [InlineData("Quat", true)]
    [InlineData("Color", true)]
    [InlineData("Guid", true)]
    [InlineData("GameplayTag", true)]
    [InlineData("SomeCustomStruct", false)]
    [InlineData("FVector", false)] // Must match exact UE name without F prefix
    public void IsKnown_ChecksCorrectly(string structName, bool expected)
    {
        Assert.Equal(expected, KnownStructLayouts.IsKnown(structName));
    }
}
