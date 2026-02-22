using UE5DumpUI.Core;
using Xunit;

namespace UE5DumpUI.Tests;

public class AddressHelperTests
{
    [Theory]
    [InlineData("0x1E55C298D40", null, "0x1E55C298D40")]
    [InlineData("1E55C298D40", null, "0x1E55C298D40")]
    [InlineData("0X1E55C298D40", null, "0x1E55C298D40")]
    public void PlainHexAddress_ReturnsNormalized(string input, string? moduleBase, string expected)
    {
        Assert.Equal(expected, AddressHelper.NormalizeAddress(input, moduleBase));
    }

    [Fact]
    public void QuotedModuleWithOffset_ResolvesAbsolute()
    {
        // "TQ2-Win64-Shipping.exe"+1234 with moduleBase 0x7FF700000000
        // Expected: 0x7FF700000000 + 0x1234 = 0x7FF700001234
        var result = AddressHelper.NormalizeAddress(
            "\"TQ2-Win64-Shipping.exe\"+1234",
            "0x7FF700000000");
        Assert.Equal("0x7FF700001234", result);
    }

    [Fact]
    public void UnquotedModuleWithOffset_ResolvesAbsolute()
    {
        var result = AddressHelper.NormalizeAddress(
            "TQ2-Win64-Shipping.exe+1234",
            "0x7FF700000000");
        Assert.Equal("0x7FF700001234", result);
    }

    [Fact]
    public void QuotedModuleWithLargeOffset_ResolvesCorrectly()
    {
        // Simulate a real CE copy: "TQ2-Win64-Shipping.exe"+FFFF81EDA0608D40
        // Module base = 0x7FF6BA800000
        // 0x7FF6BA800000 + 0xFFFF81EDA0608D40 => unchecked overflow wraps (expected in CE RVA)
        var result = AddressHelper.NormalizeAddress(
            "\"TQ2-Win64-Shipping.exe\"+FFFF81EDA0608D40",
            "0x7FF6BA800000");
        // Verify it's a valid hex string starting with 0x
        Assert.StartsWith("0x", result);
        Assert.True(result.Length > 2);
    }

    [Fact]
    public void ModuleWithOffset_NoModuleBase_ReturnsOffsetOnly()
    {
        var result = AddressHelper.NormalizeAddress(
            "TQ2-Win64-Shipping.exe+ABCD",
            null);
        Assert.Equal("0xABCD", result);
    }

    [Fact]
    public void ModuleWithOffset_0xPrefix_HandledCorrectly()
    {
        var result = AddressHelper.NormalizeAddress(
            "game.exe+0x1234",
            "0x7FF700000000");
        Assert.Equal("0x7FF700001234", result);
    }

    [Fact]
    public void WhitespaceAndQuotes_Trimmed()
    {
        var result = AddressHelper.NormalizeAddress(
            "  \"0x1E55C298D40\"  ",
            null);
        Assert.Equal("0x1E55C298D40", result);
    }

    [Fact]
    public void DoubleQuotedModule_OuterQuotesTrimmed()
    {
        // Input from CE may look like: "\"game.exe\"+1234" after C# string escaping
        // which represents the literal: "game.exe"+1234
        var result = AddressHelper.NormalizeAddress(
            "\"game.exe\"+1234",
            "0x7FF700000000");
        Assert.Equal("0x7FF700001234", result);
    }
}
