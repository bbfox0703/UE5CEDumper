using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks down the KeywordTokenizer split rules. The scorer's
/// false-positive defence depends on these splits being predictable;
/// any regression here will cause the keyword tables to misfire.
/// </summary>
public class KeywordTokenizerTests
{
    // ==================================================================
    // Rule 1+2: separators (underscore, hyphen)
    // ==================================================================

    [Theory]
    [InlineData("BP_Player_C",      new[] { "bp", "player", "c" })]
    [InlineData("Foo_Bar_Baz",      new[] { "foo", "bar", "baz" })]
    [InlineData("__leadingTrailing__", new[] { "leading", "trailing" })]
    [InlineData("snake-case-name",  new[] { "snake", "case", "name" })]
    public void Tokenize_SeparatorsSplit(string input, string[] expected)
        => Assert.Equal(expected, KeywordTokenizer.Tokenize(input));

    // ==================================================================
    // Rule 3: lower-to-upper transition
    // ==================================================================

    [Theory]
    [InlineData("AddMoney",          new[] { "add", "money" })]
    [InlineData("AddPlayerCurrency", new[] { "add", "player", "currency" })]
    [InlineData("SetMaxHealth",      new[] { "set", "max", "health" })]
    [InlineData("getHealth",         new[] { "get", "health" })]   // lowercase first
    [InlineData("camelCase",         new[] { "camel", "case" })]
    public void Tokenize_LowerToUpperBoundary(string input, string[] expected)
        => Assert.Equal(expected, KeywordTokenizer.Tokenize(input));

    // ==================================================================
    // Rule 4: run of uppers followed by lower (acronym-ending boundary)
    // ==================================================================

    [Theory]
    [InlineData("HUDWidget",       new[] { "hud", "widget" })]
    [InlineData("BPCharacter",     new[] { "bp", "character" })]
    [InlineData("UMGPanel",        new[] { "umg", "panel" })]
    [InlineData("XMLParser",       new[] { "xml", "parser" })]
    [InlineData("HTTPSConnection", new[] { "https", "connection" })]
    public void Tokenize_AcronymEndingBoundary(string input, string[] expected)
        => Assert.Equal(expected, KeywordTokenizer.Tokenize(input));

    // ==================================================================
    // Acronym-only / all-caps tokens
    // ==================================================================

    [Theory]
    [InlineData("HP",        new[] { "hp" })]
    [InlineData("MP",        new[] { "mp" })]
    [InlineData("XP",        new[] { "xp" })]
    [InlineData("GetHP",     new[] { "get", "hp" })]
    [InlineData("PlayerHP",  new[] { "player", "hp" })]
    [InlineData("HPBar",     new[] { "hp", "bar" })]
    [InlineData("BP_PlayerHP_C", new[] { "bp", "player", "hp", "c" })]
    public void Tokenize_AcronymsKeepTogether(string input, string[] expected)
        => Assert.Equal(expected, KeywordTokenizer.Tokenize(input));

    // ==================================================================
    // The substring-noise cases tokenisation exists for.
    // These names CONTAIN the letter pair "mp"/"sp"/"tp" but must NOT
    // produce a token equal to those acronyms.
    // ==================================================================

    [Theory]
    [InlineData("Component",      new[] { "component" })]
    [InlineData("MyComponent",    new[] { "my", "component" })]
    [InlineData("Spawn",          new[] { "spawn" })]
    [InlineData("SpawnActor",     new[] { "spawn", "actor" })]
    [InlineData("GetTPSStream",   new[] { "get", "tps", "stream" })]
    public void Tokenize_NoSubstringFalsePositives(string input, string[] expected)
        => Assert.Equal(expected, KeywordTokenizer.Tokenize(input));

    // ==================================================================
    // Edge cases
    // ==================================================================

    [Fact]
    public void Tokenize_NullInput_ReturnsEmpty()
        => Assert.Empty(KeywordTokenizer.Tokenize(null));

    [Fact]
    public void Tokenize_EmptyInput_ReturnsEmpty()
        => Assert.Empty(KeywordTokenizer.Tokenize(""));

    [Fact]
    public void Tokenize_AllLowercase_SingleToken()
        => Assert.Equal(new[] { "lowercase" }, KeywordTokenizer.Tokenize("lowercase"));

    [Fact]
    public void Tokenize_AllUppercase_SingleToken()
        => Assert.Equal(new[] { "uppercase" }, KeywordTokenizer.Tokenize("UPPERCASE"));

    [Fact]
    public void Tokenize_SingleChar_OneToken()
    {
        Assert.Equal(new[] { "x" }, KeywordTokenizer.Tokenize("x"));
        Assert.Equal(new[] { "x" }, KeywordTokenizer.Tokenize("X"));
    }

    [Fact]
    public void Tokenize_DigitsAttachToNeighbour()
    {
        // We don't split on digit-to-letter or letter-to-digit boundaries
        // -- if the user wants digit boundaries they should use underscores.
        // UE BP names typically already underscore-separate the interesting
        // parts (K2Node_42, BndEvt__OnClicked_K2Node_42, etc).
        // The cost: function "Player100Health" is one token "player100health"
        // and won't match the "Health" keyword. Accept that miss; cooked
        // UE games rarely embed digits inside cheat-relevant verbs.
        Assert.Equal(new[] { "k2node42" }, KeywordTokenizer.Tokenize("K2Node42"));
        Assert.Equal(new[] { "exec1node" }, KeywordTokenizer.Tokenize("Exec1Node"));
        Assert.Equal(new[] { "player100health" },
            KeywordTokenizer.Tokenize("Player100Health"));
    }

    [Fact]
    public void TokenizeAsSet_DedupesRepeatedTokens()
    {
        // "AddHealthAddHealth" -> tokens ["add","health","add","health"]
        // -> as a set, just {"add","health"}. Confirms the scorer's
        // CountTokenHits won't over-credit repetitive names.
        var set = KeywordTokenizer.TokenizeAsSet("AddHealthAddHealth");
        Assert.Equal(2, set.Count);
        Assert.Contains("add", set);
        Assert.Contains("health", set);
    }

    [Fact]
    public void TokenizeAsSet_NullInput_ReturnsEmptySet()
    {
        var set = KeywordTokenizer.TokenizeAsSet(null);
        Assert.Empty(set);
    }
}
