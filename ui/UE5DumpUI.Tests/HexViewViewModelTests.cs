using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

public class HexViewViewModelTests
{
    [Fact]
    public void FormatRow_BasicBytes_CorrectHexAndAscii()
    {
        byte[] data = [0x48, 0x65, 0x6C, 0x6C, 0x6F]; // "Hello"
        var row = HexViewViewModel.FormatRow(0, data);

        Assert.Equal("00000000", row.Offset);
        Assert.Contains("48 65 6C 6C 6F", row.HexPart);
        Assert.Equal("Hello", row.AsciiPart);
    }

    [Fact]
    public void FormatRow_NonPrintableBytes_ReplacedWithDot()
    {
        byte[] data = [0x00, 0x01, 0x41, 0xFF]; // NUL, SOH, 'A', 0xFF
        var row = HexViewViewModel.FormatRow(0x100, data);

        Assert.Equal("00000100", row.Offset);
        Assert.Equal("..A.", row.AsciiPart);
    }

    [Fact]
    public void FormatRow_FullRow_16BytesFormatted()
    {
        byte[] data = new byte[16];
        for (int i = 0; i < 16; i++) data[i] = (byte)(0x30 + i); // '0' through '?'
        var row = HexViewViewModel.FormatRow(0, data);

        Assert.Equal("00000000", row.Offset);
        Assert.Equal(16, row.AsciiPart.Length);
        Assert.StartsWith("30 31 32", row.HexPart);
    }

    [Fact]
    public void FormatRow_EmptyBytes_EmptyStrings()
    {
        var row = HexViewViewModel.FormatRow(0, ReadOnlySpan<byte>.Empty);

        Assert.Equal("00000000", row.Offset);
        Assert.Equal("", row.HexPart);
        Assert.Equal("", row.AsciiPart);
    }
}
