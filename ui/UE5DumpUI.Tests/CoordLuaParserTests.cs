using System;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// P4 Lua re-import — docs/teleport-coord-library-spec.md §4.4.
/// The parser's whole value is tolerating hand edits, so most of these feed it
/// deliberately mangled input. The four "do NOT inherit from AOBMaker" defects each
/// get their own test.
/// </summary>
public class CoordLuaParserTests
{
    private static string Wrap(string body) =>
        "-- @UE5CD:COORDS v1\nlocal COORDS = {\n" + body + "\n}\n-- @UE5CD:END\n";

    private const string OneEntry =
        "  { uid='k3f9', label='Chest 1', group='Chest', map='Map01', " +
        "x=87668.672, y=-24858.674, z=341.376, pitch=335.01, yaw=26.16, roll=0 },";

    // ── Happy path + round trip ─────────────────────────────────────────

    [Fact]
    public void Parse_OverflowingCoordinate_IsReported_NotImportedAsTheOrigin()
    {
        // [A3-COORD-NONFINITE] The number regex keeps the NaN / Infinity WORDS out, but 1e400 matches it and overflows to
        // +Infinity -- which was stored as 0 without an issue.
        var r = CoordLuaParser.Parse(Wrap("  { uid='a', label='A', map='M', x=1e400, y=2, z=3 },"));
        Assert.Empty(r.Entries);
        Assert.Contains(r.Issues, i => i.Column == "x");
    }

    [Fact]
    public void Parse_ReadsAGeneratedEntry()
    {
        var r = CoordLuaParser.Parse(Wrap(OneEntry));
        var e = Assert.Single(r.Entries);
        Assert.Equal("k3f9", e.Uid);
        Assert.Equal("Chest 1", e.Label);
        Assert.Equal("Chest", e.Group);
        Assert.Equal("Map01", e.Map);
        Assert.Equal(87668.672, e.X);
        Assert.Equal(-24858.674, e.Y);
        Assert.Equal(26.16, e.Yaw);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void GenerateThenParse_IsLosslessAcrossTheHardCases()
    {
        var original = new[]
        {
            new CoordEntry { Uid="a", Label="it's here", Group="Fields", Map="Map01",
                             X=CoordPrecision.Round(67162.3984375), Y=-20380.643, Z=35.791,
                             Pitch=347.71, Yaw=31.98, Roll=0 },
            new CoordEntry { Uid="b", Label="bad ]==] label", Group="", Map="Map02" },
            new CoordEntry { Uid="c", Label="寶箱 3（上層）", Group="寶箱", Map="Map03",
                             X=-4096.5, Y=8192.25, Z=120, Yaw=180 },
            new CoordEntry { Uid="d", Label="back\\slash \"quoted\"", Group="G", Map="M" },
        };

        var script = CoordLibraryScriptGenerator.Generate(original, out _);
        var r = CoordLuaParser.Parse(script);

        Assert.Empty(r.Issues);
        Assert.Equal(original.Length, r.Entries.Count);
        foreach (var want in original)
        {
            var got = Assert.Single(r.Entries, e => e.Uid == want.Uid);
            Assert.Equal(want.Label, got.Label);
            Assert.Equal(want.Group, got.Group);
            Assert.Equal(want.Map, got.Map);
            Assert.Equal(want.X, got.X);
            Assert.Equal(want.Y, got.Y);
            Assert.Equal(want.Z, got.Z);
            Assert.Equal(want.Pitch, got.Pitch);
            Assert.Equal(want.Yaw, got.Yaw);
            Assert.Equal(want.Roll, got.Roll);
        }
    }

    [Fact]
    public void GenerateParseGenerate_IsByteIdentical()
    {
        var entries = new[]
        {
            new CoordEntry { Uid="a", Label="Chest 1", Group="Chest", Map="M", X=1.5, Yaw=90 },
            new CoordEntry { Uid="b", Label="it's ]==]", Group="", Map="M2" },
        };
        var first = CoordLibraryScriptGenerator.Generate(entries, out _);
        var second = CoordLibraryScriptGenerator.Generate(CoordLuaParser.Parse(first).Entries, out _);
        Assert.Equal(first, second);
    }

    // ── Hand-edit tolerance (why the format is named-field) ─────────────

    [Fact]
    public void Parse_ToleratesReorderedFields()
    {
        var r = CoordLuaParser.Parse(Wrap(
            "  { z=3, label='A', x=1, map='M', y=2, uid='u', group='G' },"));
        var e = Assert.Single(r.Entries);
        Assert.Equal("A", e.Label);
        Assert.Equal(1, e.X);
        Assert.Equal(3, e.Z);
    }

    [Fact]
    public void Parse_IgnoresUnknownKeysFromANewerBuild()
    {
        // Forward compatibility: a v1 build must not choke on a v2 field.
        var r = CoordLuaParser.Parse(Wrap(
            "  { uid='u', label='A', map='M', x=1, y=2, z=3, colour='red', icon=7 },"));
        Assert.Equal("A", Assert.Single(r.Entries).Label);
    }

    [Fact]
    public void Parse_ToleratesMissingSeparatingComma()
    {
        var r = CoordLuaParser.Parse(Wrap(
            "  { label='A', map='M', x=1, y=2, z=3 }\n  { label='B', map='M', x=4, y=5, z=6 }"));
        Assert.Equal(2, r.Entries.Count);
    }

    [Fact]
    public void Parse_ToleratesAnEntrySplitOverLines()
    {
        var r = CoordLuaParser.Parse(Wrap(
            "  {\n    label='A',\n    map='M',\n    x=1,\n    y=2,\n    z=3,\n  },"));
        Assert.Equal("A", Assert.Single(r.Entries).Label);
    }

    [Fact]
    public void Parse_SkipsACommentedOutEntry()
    {
        var r = CoordLuaParser.Parse(Wrap(
            "  { label='Real', map='M', x=1, y=2, z=3 },\n" +
            "  -- a note the user added\n"));
        Assert.Equal("Real", Assert.Single(r.Entries).Label);
    }

    [Fact]
    public void Parse_ToleratesArbitraryWhitespace()
    {
        var r = CoordLuaParser.Parse(
            "  --   @UE5CD:COORDS   v1  \nlocal   COORDS   =   {\n" +
            "  {  label = 'A' ,  map='M', x = 1 , y=2, z=3 }  ,\n}\n  -- @UE5CD:END  \n");
        Assert.Equal("A", Assert.Single(r.Entries).Label);
    }

    [Fact]
    public void Parse_AcceptsBothQuoteStyles()
    {
        // AOBMaker's parser silently IGNORES single-quoted values and falls back to a
        // default -- exactly the quiet wrong answer this format avoids.
        var r = CoordLuaParser.Parse(Wrap(
            "  { label=\"Double\", group='Single', map='M', x=1, y=2, z=3 },"));
        var e = Assert.Single(r.Entries);
        Assert.Equal("Double", e.Label);
        Assert.Equal("Single", e.Group);
    }

    [Fact]
    public void Parse_IdentifierLookbehindStopsKeyShadowing()
    {
        var r = CoordLuaParser.Parse(Wrap(
            "  { xlabel='WRONG', label='RIGHT', map='M', x=1, y=2, z=3 },"));
        Assert.Equal("RIGHT", Assert.Single(r.Entries).Label);
    }

    [Fact]
    public void Parse_BracesInsideAQuotedLabelDoNotSplitTheEntry()
    {
        var r = CoordLuaParser.Parse(Wrap(
            "  { label='a { b } c', map='M', x=1, y=2, z=3 },"));
        Assert.Equal("a { b } c", Assert.Single(r.Entries).Label);
    }

    [Fact]
    public void Parse_MissingOptionalRotationDefaultsToZero()
    {
        var r = CoordLuaParser.Parse(Wrap("  { label='A', map='M', x=1, y=2, z=3 },"));
        var e = Assert.Single(r.Entries);
        Assert.Equal(0, e.Pitch);
        Assert.Equal(0, e.Roll);
    }

    [Fact]
    public void Parse_AcceptsNegativeAndExponentNumbers()
    {
        var r = CoordLuaParser.Parse(Wrap(
            "  { label='A', map='M', x=-1.5, y=2e3, z=1.4210855e-14 },"));
        var e = Assert.Single(r.Entries);
        Assert.Equal(-1.5, e.X);
        Assert.Equal(2000, e.Y);
        Assert.Equal(0, e.Z);          // rounded to the canonical precision
    }

    // ── The four AOBMaker defects we do NOT inherit ─────────────────────

    [Fact]
    public void Parse_UnsupportedVersion_IsDistinguishableFromNoBlock()
    {
        // AOBMaker bakes the version into the marker literal, so a v2 block reads as
        // "no markers found" and the user thinks their paste was truncated.
        var r = CoordLuaParser.Parse(
            "-- @UE5CD:COORDS v99\nlocal COORDS = {\n" + OneEntry + "\n}\n-- @UE5CD:END\n");
        Assert.Empty(r.Entries);
        var issue = Assert.Single(r.Issues);
        Assert.Contains("newer UE5CEDumper", issue.Reason);
        Assert.Contains("v99", issue.Reason);
    }

    [Fact]
    public void Parse_NoBlock_SaysSoSpecifically()
    {
        var r = CoordLuaParser.Parse("[ENABLE]\n{$lua}\nprint('unrelated script')\n{$asm}\n");
        Assert.Empty(r.Entries);
        Assert.Contains(r.Issues, i => i.Reason.Contains("no '-- @UE5CD:COORDS v1' block"));
    }

    [Fact]
    public void Parse_MarkerInsideAStringLiteralIsNotMistakenForTheFence()
    {
        // AOBMaker matches markers with an unanchored IndexOf, so a marker mentioned
        // inside print("...") is treated as real. Ours must start a line.
        var r = CoordLuaParser.Parse(
            "print(\"-- @UE5CD:COORDS v1 is the marker\")\nprint(\"-- @UE5CD:END\")\n");
        Assert.Empty(r.Entries);
        Assert.Contains(r.Issues, i => i.Reason.Contains("no '-- @UE5CD:COORDS v1' block"));
    }

    [Fact]
    public void Parse_MissingEndFence_ReportsTruncation()
    {
        var r = CoordLuaParser.Parse("-- @UE5CD:COORDS v1\nlocal COORDS = {\n" + OneEntry + "\n}\n");
        Assert.Empty(r.Entries);
        Assert.Contains(r.Issues, i => i.Reason.Contains("not closed"));
    }

    [Fact]
    public void Parse_NeverFailsSilently()
    {
        // The single worst property of AOBMaker's parsers: null or an empty list, no
        // throw, no diagnostic, ever.
        foreach (var input in new[] { "", "   ", "garbage", "-- @UE5CD:COORDS v1\n-- @UE5CD:END\n" })
        {
            var r = CoordLuaParser.Parse(input);
            Assert.True(r.Issues.Count > 0, $"input {input.Length} chars produced no diagnostic");
        }
    }

    [Fact]
    public void Parse_BadNumber_IsReportedWithALineNumber()
    {
        var r = CoordLuaParser.Parse(
            "-- @UE5CD:COORDS v1\n" +
            "local COORDS = {\n" +
            "  { label='Good', map='M', x=1, y=2, z=3 },\n" +
            "  { label='Bad', map='M', x=oops, y=2, z=3 },\n" +
            "}\n-- @UE5CD:END\n");
        Assert.Equal("Good", Assert.Single(r.Entries).Label);
        var issue = Assert.Single(r.Issues);
        Assert.Equal("x", issue.Column);
        Assert.Equal(4, issue.Line);
    }

    [Fact]
    public void Parse_EntryWithNoLabel_IsReportedNotSilentlySkipped()
    {
        var r = CoordLuaParser.Parse(Wrap("  { uid='u', map='M', x=1, y=2, z=3 },"));
        Assert.Empty(r.Entries);
        Assert.Contains(r.Issues, i => i.Column == "label" && i.Reason.Contains("no label"));
    }

    // ── The .CT clipboard form ──────────────────────────────────────────

    [Fact]
    public void Parse_DecodesAllFiveXmlEntitiesFromAPastedCheatTable()
    {
        // CheatTableBuilder.EscapeXml escapes FIVE entities but
        // CeXmlExportService.ExtractAssemblerScript reverses only three, so a paste of
        // the clipboard .CT form would otherwise carry literal &apos; / &quot; text.
        var script = CoordLibraryScriptGenerator.Generate(
            new[] { new CoordEntry { Uid = "a", Label = "it's <hot> & \"loud\"", Map = "M" } },
            out _);
        var xml = CheatTableBuilder.WrapAaScriptXml("Teleport: Coordinate Library (picker)", script);

        var r = CoordLuaParser.Parse(xml);

        Assert.Equal("it's <hot> & \"loud\"", Assert.Single(r.Entries).Label);
    }

    [Fact]
    public void LooksLikeCheatTableXml_OnlyFiresOnTheXmlForm()
    {
        Assert.True(CoordLuaParser.LooksLikeCheatTableXml("<?xml version=\"1.0\"?><CheatTable/>"));
        Assert.True(CoordLuaParser.LooksLikeCheatTableXml("  <AssemblerScript>x</AssemblerScript>"));
        Assert.False(CoordLuaParser.LooksLikeCheatTableXml("-- @UE5CD:COORDS v1\nlocal COORDS = {}"));
    }

    [Fact]
    public void Parse_RawScriptKeepsALiteralEntityText()
    {
        // A label that genuinely contains the text "&lt;" must not be decoded when the
        // paste is a raw script rather than the XML form.
        var r = CoordLuaParser.Parse(Wrap("  { label='a &lt; b', map='M', x=1, y=2, z=3 },"));
        Assert.Equal("a &lt; b", Assert.Single(r.Entries).Label);
    }

    // ── Unescaping ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"it\'s", "it's")]
    [InlineData(@"a\\b", @"a\b")]
    [InlineData(@"a\nb", "a\nb")]
    [InlineData(@"a\tb", "a\tb")]
    [InlineData(@"a\093b", "a]b")]        // the long-bracket-breaking decimal escape
    [InlineData("plain", "plain")]
    public void Unescape_ReversesTheSharedEscaper(string input, string expected)
        => Assert.Equal(expected, CoordLuaParser.Unescape(input));

    [Fact]
    public void Unescape_DecimalEscapeFollowedByADigitStaysSeparate()
    {
        // \093 then a literal '1' must not be read as \0931 (> 255).
        Assert.Equal("]1", CoordLuaParser.Unescape(@"\0931"));
    }

    [Fact]
    public void EscapeThenUnescape_RoundTrips()
    {
        foreach (var s in new[] { "it's", @"a\b", "a\tb", "]==]", "]]", "寶箱 \"3\"", "plain" })
            Assert.Equal(s, CoordLuaParser.Unescape(CeLuaHygiene.EscapeLuaString(s)));
    }

    // ── Brace scanning ──────────────────────────────────────────────────

    [Fact]
    public void ExtractBalanced_HandlesNesting()
        => Assert.Equal("a{b}c", CoordLuaParser.ExtractBalanced("{a{b}c}", 0));

    [Fact]
    public void ExtractBalanced_IgnoresBracesInsideStrings()
        => Assert.Equal("x='}'", CoordLuaParser.ExtractBalanced("{x='}'}", 0));

    [Fact]
    public void ExtractBalanced_UnbalancedReturnsNull()
        => Assert.Null(CoordLuaParser.ExtractBalanced("{a{b}", 0));

    [Fact]
    public void TopLevelBraceGroups_SplitsSiblingsOnly()
    {
        var groups = CoordLuaParser.TopLevelBraceGroups("{a},{b{c}},{d}").Select(g => g.Text).ToList();
        Assert.Equal(new[] { "a", "b{c}", "d" }, groups);
    }
}
