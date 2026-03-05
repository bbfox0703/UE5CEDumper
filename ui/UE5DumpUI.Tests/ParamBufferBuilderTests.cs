using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

public class ParamBufferBuilderTests
{
    [Fact]
    public void BuildParamsHex_ZeroParmsSize_ReturnsEmpty()
    {
        var result = ParamBufferBuilder.BuildParamsHex(
            Array.Empty<FunctionParamModel>(),
            Array.Empty<string>(),
            parmsSize: 0);

        Assert.Equal("", result);
    }

    [Fact]
    public void BuildParamsHex_Int32AtOffset0()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Amount", TypeName = "IntProperty", Size = 4, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "42" }, parmsSize: 4);

        // 42 decimal = 0x2A, little-endian int32 = 2A000000
        Assert.Equal("2A000000", hex);
    }

    [Fact]
    public void BuildParamsHex_FloatAtOffset4()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Speed", TypeName = "FloatProperty", Size = 4, Offset = 4 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "1.0" }, parmsSize: 8);

        // float 1.0 = 3F800000 (big-endian), little-endian bytes = 0000803F at offset 4
        // Full buffer: 00000000 0000803F
        Assert.Equal("000000000000803F", hex);
    }

    [Fact]
    public void BuildParamsHex_BoolAtOffset8()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Flag", TypeName = "BoolProperty", Size = 1, Offset = 8 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "1" }, parmsSize: 9);

        // 8 zero bytes + 0x01
        Assert.Equal("000000000000000001", hex);
    }

    [Fact]
    public void BuildParamsHex_MixedTypes()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "X", TypeName = "IntProperty", Size = 4, Offset = 0 },
            new() { Name = "Y", TypeName = "FloatProperty", Size = 4, Offset = 4 },
            new() { Name = "Flag", TypeName = "BoolProperty", Size = 1, Offset = 8 },
        };
        var values = new[] { "100", "2.5", "1" };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, values, parmsSize: 12);

        // int32 100 = 0x64 → 64000000
        // float 2.5 = 40200000 → 00002040
        // bool 1 = 01
        // Remaining 3 bytes = 000000
        Assert.StartsWith("64000000", hex);
        Assert.Equal(24, hex.Length); // 12 bytes = 24 hex chars
    }

    [Fact]
    public void BuildParamsHex_HexInput_ParsedCorrectly()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Target", TypeName = "ObjectProperty", Size = 8, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "0xFF" }, parmsSize: 8);

        // ulong 0xFF = 255, little-endian = FF00000000000000
        Assert.Equal("FF00000000000000", hex);
    }

    [Fact]
    public void BuildParamsHex_Int64Property()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Val", TypeName = "Int64Property", Size = 8, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "256" }, parmsSize: 8);

        // int64 256 = 0x100, little-endian = 0001000000000000
        Assert.Equal("0001000000000000", hex);
    }

    [Fact]
    public void BuildParamsHex_DoubleProperty()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Val", TypeName = "DoubleProperty", Size = 8, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "1.0" }, parmsSize: 8);

        // double 1.0 = 3FF0000000000000 → little-endian: 000000000000F03F
        Assert.Equal("000000000000F03F", hex);
    }

    [Fact]
    public void BuildParamsHex_BufferZeroPadded()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "X", TypeName = "IntProperty", Size = 4, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "1" }, parmsSize: 16);

        // 16 bytes = 32 hex chars. First 4 bytes = int32(1), rest zeroed
        Assert.Equal(32, hex.Length);
        Assert.StartsWith("01000000", hex);
        Assert.EndsWith("000000000000000000000000", hex);
    }

    [Fact]
    public void BuildParamsHex_InvalidInput_DefaultsToZero()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "X", TypeName = "IntProperty", Size = 4, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "not_a_number" }, parmsSize: 4);

        // Invalid input → default 0
        Assert.Equal("00000000", hex);
    }

    [Fact]
    public void BuildParamsHex_UInt16Property()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Val", TypeName = "UInt16Property", Size = 2, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "258" }, parmsSize: 2);

        // uint16 258 = 0x0102, little-endian = 0201
        Assert.Equal("0201", hex);
    }

    [Fact]
    public void BuildParamsHex_EnumProperty()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Mode", TypeName = "EnumProperty", Size = 4, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "3" }, parmsSize: 4);

        Assert.Equal("03000000", hex);
    }

    // --- ShortTypeName ---

    [Theory]
    [InlineData("BoolProperty", "bool")]
    [InlineData("IntProperty", "int32")]
    [InlineData("FloatProperty", "float")]
    [InlineData("ObjectProperty", "UObject*")]
    [InlineData("StructProperty", "struct")]
    [InlineData("SomeCustomProperty", "SomeCustom")]
    public void ShortTypeName_MapsCorrectly(string typeName, string expected)
    {
        Assert.Equal(expected, ParamBufferBuilder.ShortTypeName(typeName));
    }

    // --- WriteStructParam ---

    [Fact]
    public void WriteStructParam_FVector_UE4_WritesThreeFloats()
    {
        var layout = KnownStructLayouts.GetLayout("Vector", ueVersion: 427)!;
        var buf = new byte[12];
        var values = new[] { "1.0", "2.0", "3.0" };

        ParamBufferBuilder.WriteStructParam(buf, 0, layout.Fields, values);

        // float 1.0 = 0x3F800000 → LE: 0000803F
        Assert.Equal("0000803F00000040", Convert.ToHexString(buf[..8]));
        // float 3.0 = 0x40400000 → LE: 00004040
        Assert.Equal("00004040", Convert.ToHexString(buf[8..]));
    }

    [Fact]
    public void WriteStructParam_FVector_UE5_WritesThreeDoubles()
    {
        var layout = KnownStructLayouts.GetLayout("Vector", ueVersion: 505)!;
        var buf = new byte[24];
        var values = new[] { "1.0", "2.0", "3.0" };

        ParamBufferBuilder.WriteStructParam(buf, 0, layout.Fields, values);

        // double 1.0 = 000000000000F03F (LE)
        Assert.Equal("000000000000F03F", Convert.ToHexString(buf[..8]));
        // double 2.0 = 0000000000000040 (LE)
        Assert.Equal("0000000000000040", Convert.ToHexString(buf[8..16]));
        // double 3.0 = 0000000000000840 (LE)
        Assert.Equal("0000000000000840", Convert.ToHexString(buf[16..]));
    }

    [Fact]
    public void WriteStructParam_FColor_WritesBGRA()
    {
        var layout = KnownStructLayouts.GetLayout("Color", ueVersion: 505)!;
        var buf = new byte[4];
        // B=0, G=128, R=255, A=200
        var values = new[] { "0", "128", "255", "200" };

        ParamBufferBuilder.WriteStructParam(buf, 0, layout.Fields, values);

        Assert.Equal(0, buf[0]);   // B
        Assert.Equal(128, buf[1]); // G
        Assert.Equal(255, buf[2]); // R
        Assert.Equal(200, buf[3]); // A
    }

    [Fact]
    public void WriteStructParam_WithBaseOffset()
    {
        var layout = KnownStructLayouts.GetLayout("IntPoint", ueVersion: 505)!;
        var buf = new byte[16]; // IntPoint at offset 8
        var values = new[] { "10", "20" };

        ParamBufferBuilder.WriteStructParam(buf, 8, layout.Fields, values);

        // First 8 bytes should be zero
        Assert.Equal("0000000000000000", Convert.ToHexString(buf[..8]));
        // int32 10 at offset 8, int32 20 at offset 12
        Assert.Equal("0A00000014000000", Convert.ToHexString(buf[8..]));
    }

    // --- GetDefaultValue ---

    [Theory]
    [InlineData("FloatProperty", "0.0")]
    [InlineData("BoolProperty", "0")]
    [InlineData("ObjectProperty", "0x0")]
    [InlineData("IntProperty", "0")]
    public void GetDefaultValue_ReturnsExpected(string typeName, string expected)
    {
        Assert.Equal(expected, ParamBufferBuilder.GetDefaultValue(typeName));
    }
}
