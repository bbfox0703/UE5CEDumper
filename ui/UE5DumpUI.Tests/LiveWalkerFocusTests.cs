using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for Value Search → Live Walker focus-on-field cross-navigation,
/// which shows in Live Walker where a found value (a TMap's included)
/// sits. The owning property row is matched by byte offset; for container
/// hits the trailing "[N]" element index is parsed so the post-load handler
/// can drill into the container. Here we lock the pure parser; the actual
/// scroll/drill is exercised live (needs a running game + populated Fields).
/// </summary>
public class LiveWalkerFocusTests
{
    [Theory]
    // Direct field — no element suffix.
    [InlineData("Health", -1)]
    [InlineData("MaxHealth", -1)]
    [InlineData("", -1)]
    [InlineData("NoBracket", -1)]
    // Container element — trailing "[N]".
    [InlineData("Cargo[3]", 3)]
    [InlineData("Set[0]", 0)]
    [InlineData("AttributeAugmentLevels.Value[2]", 2)]
    [InlineData("Inventory.Key[17]", 17)]
    [InlineData("Big[1234567]", 1234567)]
    // Not a drillable leaf element — must NOT parse an index.
    [InlineData("Cargo[3].ItemId", -1)]   // nested path after the bracket
    [InlineData("Weird[]", -1)]            // empty bracket
    [InlineData("Weird[-1]", -1)]          // negative index
    [InlineData("Weird[abc]", -1)]         // non-numeric
    public void ParseElementIndexSuffix_ExtractsTrailingIndex(string fieldName, int expected)
        => Assert.Equal(expected, LiveWalkerViewModel.ParseElementIndexSuffix(fieldName));
}
