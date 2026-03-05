using UE5DumpUI.Models;
using UE5DumpUI.Views;
using Xunit;

namespace UE5DumpUI.Tests;

public class InvokeParamDialogTests
{
    // --- HexToBytes ---

    [Fact]
    public void HexToBytes_EmptyString_ReturnsEmpty()
    {
        var bytes = InvokeParamDialog.HexToBytes("");
        Assert.Empty(bytes);
    }

    [Fact]
    public void HexToBytes_SingleByte()
    {
        var bytes = InvokeParamDialog.HexToBytes("FF");
        Assert.Single(bytes);
        Assert.Equal(0xFF, bytes[0]);
    }

    [Fact]
    public void HexToBytes_MultipleByte()
    {
        var bytes = InvokeParamDialog.HexToBytes("2A000000");
        Assert.Equal(4, bytes.Length);
        Assert.Equal(0x2A, bytes[0]);
        Assert.Equal(0x00, bytes[1]);
    }

    [Fact]
    public void HexToBytes_LowercaseHex()
    {
        var bytes = InvokeParamDialog.HexToBytes("ff00ab");
        Assert.Equal(3, bytes.Length);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xAB, bytes[2]);
    }

    // --- DecodeParamValue ---

    [Fact]
    public void DecodeParamValue_BoolTrue()
    {
        var buf = new byte[] { 1 };
        var p = new FunctionParamModel { Name = "Flag", TypeName = "BoolProperty", Size = 1, Offset = 0 };
        Assert.Equal("true", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_BoolFalse()
    {
        var buf = new byte[] { 0 };
        var p = new FunctionParamModel { Name = "Flag", TypeName = "BoolProperty", Size = 1, Offset = 0 };
        Assert.Equal("false", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_Int32()
    {
        // 42 in little-endian = 0x2A000000
        var buf = new byte[] { 0x2A, 0x00, 0x00, 0x00 };
        var p = new FunctionParamModel { Name = "X", TypeName = "IntProperty", Size = 4, Offset = 0 };
        Assert.Equal("42", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_Float()
    {
        // float 1.0 = 0x3F800000, little-endian = 0000803F
        var buf = new byte[] { 0x00, 0x00, 0x80, 0x3F };
        var p = new FunctionParamModel { Name = "Speed", TypeName = "FloatProperty", Size = 4, Offset = 0 };
        Assert.Equal("1", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_Double()
    {
        // double 1.0 = 0x3FF0000000000000, little-endian = 000000000000F03F
        var buf = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0x3F };
        var p = new FunctionParamModel { Name = "Val", TypeName = "DoubleProperty", Size = 8, Offset = 0 };
        Assert.Equal("1", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_Int64()
    {
        // int64 256 = 0x100, little-endian = 0001000000000000
        var buf = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var p = new FunctionParamModel { Name = "Val", TypeName = "Int64Property", Size = 8, Offset = 0 };
        Assert.Equal("256", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_UInt64_ShowsHex()
    {
        var buf = new byte[] { 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var p = new FunctionParamModel { Name = "Ptr", TypeName = "ObjectProperty", Size = 8, Offset = 0 };
        Assert.Equal("0xFF", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_WithOffset()
    {
        // Buffer: [0x00, 0x00, 0x00, 0x00, 0x01]
        // Bool at offset 4
        var buf = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01 };
        var p = new FunctionParamModel { Name = "Flag", TypeName = "BoolProperty", Size = 1, Offset = 4 };
        Assert.Equal("true", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_OffsetOutOfRange_ReturnsQuestion()
    {
        var buf = new byte[] { 0x00 };
        var p = new FunctionParamModel { Name = "X", TypeName = "IntProperty", Size = 4, Offset = 10 };
        Assert.Equal("?", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_ByteProperty()
    {
        var buf = new byte[] { 42 };
        var p = new FunctionParamModel { Name = "Val", TypeName = "ByteProperty", Size = 1, Offset = 0 };
        Assert.Equal("42", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_UInt16()
    {
        // uint16 258 = 0x0102, little-endian = 0201
        var buf = new byte[] { 0x02, 0x01 };
        var p = new FunctionParamModel { Name = "Val", TypeName = "UInt16Property", Size = 2, Offset = 0 };
        Assert.Equal("258", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_EnumProperty()
    {
        // int32 3
        var buf = new byte[] { 0x03, 0x00, 0x00, 0x00 };
        var p = new FunctionParamModel { Name = "Mode", TypeName = "EnumProperty", Size = 4, Offset = 0 };
        Assert.Equal("3", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_UnknownType_FallsBackToSize()
    {
        // Unknown 4-byte type should fallback to int32 read
        var buf = new byte[] { 0x05, 0x00, 0x00, 0x00 };
        var p = new FunctionParamModel { Name = "X", TypeName = "SomeCustomProperty", Size = 4, Offset = 0 };
        Assert.Equal("5", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_UnknownType_LargeSize_ShowsHexDash()
    {
        // Unknown 3-byte type shows BitConverter hex
        var buf = new byte[] { 0xAA, 0xBB, 0xCC };
        var p = new FunctionParamModel { Name = "X", TypeName = "StructProperty", Size = 3, Offset = 0 };
        Assert.Equal("AA-BB-CC", InvokeParamDialog.DecodeParamValue(buf, p));
    }
}
