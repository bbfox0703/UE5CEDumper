using System;
using System.Collections.Generic;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// P3 CE-Lua export — docs/teleport-coord-library-spec.md §4 / §6.
/// The script cannot be executed here, so these assert the properties that would
/// otherwise only fail in Cheat Engine: escaping, the marker grammar, hygiene, and
/// the verified-API restriction.
/// </summary>
public class CoordLibraryScriptTests
{
    private static CoordEntry E(string label, string group = "G", string map = "Map01",
                                double x = 1, double y = 2, double z = 3)
        => new()
        {
            Uid = "u" + label.GetHashCode().ToString("x")[..4],
            Label = label, Group = group, Map = map,
            X = x, Y = y, Z = z, Pitch = 0, Yaw = 0, Roll = 0,
        };

    private static string Gen(params CoordEntry[] entries)
        => CoordLibraryScriptGenerator.Generate(entries, out _);

    // ── Marker grammar ───────────────────────────────────────────────────

    [Fact]
    public void Generate_EmitsOurOwnFenceNotAobMakers()
    {
        var s = Gen(E("A"));
        Assert.Contains("-- @UE5CD:COORDS v1", s);
        Assert.Contains("-- @UE5CD:END", s);
    }

    [Fact]
    public void Generate_NeverEmitsTheAobMakerNamespace()
    {
        // AOBMaker's end marker is feature-less and shared, and it matches with an
        // unanchored IndexOf. A block of ours carrying "@AOBMAKER:END" pasted into an
        // AA Toggle script would make AOBMaker slice from ITS start marker to OUR end,
        // parse zero entries, and silently untick every record in the tree.
        var s = Gen(E("A"), E("B"));
        Assert.DoesNotContain("@AOBMAKER:", s);
    }

    [Fact]
    public void Generate_MarkersAreOnTheirOwnLines()
    {
        // The parser anchors to line start (^--), unlike AOBMaker's IndexOf, which
        // also matches a marker sitting inside a string literal.
        var lines = Gen(E("A")).Split('\n');
        Assert.Contains(lines, l => l.Trim() == "-- @UE5CD:COORDS v1");
        Assert.Contains(lines, l => l.Trim() == "-- @UE5CD:END");
    }

    [Fact]
    public void Generate_CarriesTheGeneratedCodeSeparator()
    {
        // Copied verbatim from AOBMaker: inert, never parsed, but it is the author's
        // muscle memory across both tools for "config above, generated below".
        Assert.Contains("---- GENERATED CODE (do not edit below) ----", Gen(E("A")));
    }

    [Fact]
    public void Generate_DataBlockComesBeforeTheGeneratedSeparator()
    {
        var s = Gen(E("A"));
        Assert.True(s.IndexOf("-- @UE5CD:END", StringComparison.Ordinal) <
                    s.IndexOf("---- GENERATED CODE", StringComparison.Ordinal));
    }

    // ── Entry keys ───────────────────────────────────────────────────────

    [Fact]
    public void Generate_UsesUidNotId()
    {
        // AOBMaker's CtIdRenumberService classifies a script as an ID-check script on
        // ((?<![A-Za-z0-9_])id\s*=\s*)(\d+) alone and rewrites those literals, so an
        // entry key written as `id = 17` would be silently renumbered when the user
        // runs "renumber CT IDs" over a table holding this script.
        var s = Gen(E("A"));
        Assert.Contains("uid=", s);
        Assert.DoesNotMatch(@"(?<![A-Za-z0-9_])id\s*=\s*\d", s);
    }

    [Fact]
    public void Generate_UsesNamedFieldsNotPositionalTuples()
    {
        var s = Gen(E("Chest 1", "Chest", "Map01"));
        foreach (var key in new[] { "uid=", "label=", "group=", "map=", "x=", "y=", "z=",
                                    "pitch=", "yaw=", "roll=" })
        {
            Assert.Contains(key, s);
        }
    }

    // ── Escaping ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("it's here")]
    [InlineData("back\\slash")]
    [InlineData("quote\"inside")]
    [InlineData("寶箱 3（上層）")]
    [InlineData("<tag> & more")]
    public void Generate_EscapesLabelsSafely(string label)
    {
        var s = Gen(E(label));
        Assert.DoesNotContain("]==]", s);
        // The label must appear exactly as the shared escaper renders it -- so an
        // apostrophe is backslash-escaped rather than closing the Lua literal early,
        // while everything harmless (quotes, angle brackets, CJK) passes through.
        Assert.Contains($"label='{CeLuaHygiene.EscapeLuaString(label)}'", s);
    }

    [Fact]
    public void Generate_ApostropheDoesNotCloseTheLuaLiteral()
    {
        var s = Gen(E("it's here"));
        Assert.Contains(@"label='it\'s here'", s);
    }

    [Fact]
    public void Generate_NeverEmitsTheAobMakerWrapperTerminator()
    {
        // AOBMaker's plugin wraps the WHOLE script in [==[ ... ]==] at a hardcoded
        // level and does not escape the body, so this sequence must not appear
        // anywhere -- even inside a quoted string, where Lua itself is fine.
        var s = Gen(E("bad ]==] label", "grp ]==]", "map ]=]"));
        Assert.DoesNotContain("]==]", s);
        Assert.Contains("\\093", s);
    }

    [Fact]
    public void Generate_KeepsCjkVerbatim()
        => Assert.Contains("寶箱", Gen(E("寶箱 3", "寶箱")));

    // ── Hygiene (project MUST-rule) ──────────────────────────────────────

    [Fact]
    public void Generate_HasTheDebugPreamble()
    {
        var s = Gen(E("A"));
        Assert.Contains("local DEBUG = UE5_DEBUG or 0", s);
        Assert.Contains("local function dbg(", s);
    }

    [Fact]
    public void Generate_IsInteractive_SoItDoesNotAutoCloseTheLuaWindow()
    {
        // An interactive picker must NOT use the momentary auto-close path -- the
        // window has to stay open while the user is using it. The untick belongs on
        // OnClose instead.
        var s = Gen(E("A"));
        Assert.DoesNotContain(CeLuaHygiene.CloseCall, s);
        Assert.Contains("OnClose", s);
        Assert.Contains("memrec.Active = false", s);
        Assert.Contains("return caFree", s);
    }

    [Fact]
    public void Generate_GuardsSyntaxCheck()
        => Assert.Contains("if syntaxcheck then return end", Gen(E("A")));

    [Fact]
    public void Generate_HasBothEnableAndDisableBlocks()
    {
        var s = Gen(E("A"));
        Assert.Contains("[ENABLE]", s);
        Assert.Contains("[DISABLE]", s);
    }

    // ── Verified-API restriction ─────────────────────────────────────────

    [Fact]
    public void Generate_UsesOnlyVerifiedCeControls()
    {
        // createListBox / createComboBox appear NOWHERE in this repo and are not
        // verified against CE's API. The project rule is to never invent one.
        var s = Gen(E("A"));
        Assert.DoesNotContain("createListBox", s);
        Assert.DoesNotContain("createComboBox", s);
        Assert.DoesNotContain("createCheckBox", s);
        Assert.DoesNotContain("createStringGrid", s);

        foreach (var api in new[] { "createForm", "createPanel", "createLabel",
                                    "createEdit", "createButton", "createListView" })
        {
            Assert.Contains(api, s);
        }
    }

    [Fact]
    public void Generate_CreatesTheAlClientListViewLast()
    {
        // Panel creation order is load-bearing in CE: alTop panels stack in creation
        // order and the alClient control must be created LAST or it does not fill.
        var s = Gen(E("A"));
        int lastPanel = s.LastIndexOf("createPanel", StringComparison.Ordinal);
        int listView = s.IndexOf("createListView", StringComparison.Ordinal);
        Assert.True(lastPanel > 0 && listView > lastPanel,
            "the alClient ListView must be created after every alTop/alBottom panel");
    }

    [Fact]
    public void Generate_ReusesAnAlreadyOpenWindow()
    {
        var s = Gen(E("A"));
        Assert.Contains("Destroyed", s);
        Assert.Contains("BringToFront", s);
    }

    // ── Regressions from the first in-game run (build 2269) ──────────────

    [Fact]
    public void Generate_OnClose_DropsTheGlobalBeforeUntickingTheRecord()
    {
        // Shipped broken: OnClose set memrec.Active = false, which runs the
        // [DISABLE] block, which closed the form it could still see through the
        // global -- re-entering OnClose and HANGING Cheat Engine. Nil-ing the
        // global first makes [DISABLE]'s guard fail, so the close happens once. It
        // also stops [DISABLE] touching a caFree'd form object afterwards.
        var s = Gen(E("A"));
        int nilGlobal = s.IndexOf("UE5CD_form = nil", StringComparison.Ordinal);
        // Match the CODE line, not the explanatory comment above it (which also
        // contains the words "memrec.Active = false").
        int untick = s.IndexOf("if memrec ~= nil and memrec.Active then", StringComparison.Ordinal);
        Assert.True(nilGlobal > 0, "OnClose must drop the global");
        Assert.True(untick > nilGlobal,
            "the global must be nil-ed BEFORE the untick that triggers [DISABLE]");
    }

    [Fact]
    public void Generate_DisableBlock_GuardsOnTheGlobalBeingPresent()
    {
        var s = Gen(E("A"));
        int disable = s.IndexOf("[DISABLE]", StringComparison.Ordinal);
        var tail = s[disable..];
        Assert.Contains("if UE5CD_form ~= nil and not UE5CD_form.Destroyed", tail);
    }

    [Fact]
    public void Generate_LaysOutFromClientWidthNotHardcodedPixels()
    {
        // Shipped broken: control positions assumed the form got the 820px it asked
        // for. It did not (a ~577px window), so the map selector was clipped. Every
        // width is now a function of the REAL client width, which also survives a
        // user resize.
        var s = Gen(E("A", "Chest"));
        Assert.Contains("local CW = UE5CD_form.ClientWidth", s);
        Assert.Contains("bsSizeable", s);
        // No unverified control properties: CE Lua raises on an unknown one, which
        // would take the whole script down. "Anchor" was tried and removed.
        Assert.DoesNotContain(".Anchor", s);
        Assert.Contains("math.max(240, CW - 20)", s);      // radio groups
        Assert.Contains("math.max(120, CW - 70)", s);      // filter edit
        Assert.Contains("math.floor((CW - 40) / 6)", s);   // list columns
    }

    [Fact]
    public void Generate_UsesTheReferenceProvenRadioGroupHeight()
    {
        // 56 is the ONE height proven by the reference form (its group is always a
        // single item row). Anything smaller clipped the caption and the buttons.
        var s = Gen(E("A", "Chest"));
        Assert.Contains("rgMap.Height = 56", s);
        Assert.DoesNotContain("rgMap.Height = 40", s);
        Assert.DoesNotContain("rgGroup.Height = 52", s);
    }

    [Fact]
    public void Generate_ButtonsAreWideEnoughAndDoNotOverlap()
    {
        // "Force teleport" at 130 rendered as "orce telepor". Widths are generous and
        // the Lefts are chained off them, so widening one can never overlap the next.
        var s = Gen(E("A"));
        Assert.Contains("btnForce.Width = 200", s);
        Assert.Contains("btnForce.Left = bx", s);
        Assert.Contains("btnClose.Left = bx", s);
        Assert.DoesNotContain("btnClose.Left = 290", s);
    }

    [Fact]
    public void Generate_MeasuresThePanelFromRealControlHeights()
    {
        // Control heights come from CE's font metrics, which cannot be known here --
        // so the panel is sized from what the children actually occupy.
        var s = Gen(E("A", "Chest"));
        Assert.Contains("local lastTop = rgGroup.Top + rgGroup.Height", s);
        Assert.Contains("pnlTop.Height = lastTop + 8", s);
    }

    [Fact]
    public void Generate_NoControlKeepsAHardcodedWidthWiderThanTheForm()
    {
        // The specific clipping bug: a 790px-wide radio group inside a form that
        // turned out to be ~577px. Nothing may assert a fixed width like that again.
        var s = Gen(E("A", "Chest"));
        foreach (var bad in new[] { ".Width = 790", ".Width = 300", ".Width = 260" })
            Assert.DoesNotContain(bad, s);
    }
    // ── Mailbox protocol ─────────────────────────────────────────────────

    [Fact]
    public void Generate_UsesTheRealMailboxOffsets()
    {
        // Hand-written offsets here were wrong once already; they must come from
        // CeMailboxLayout, which mirrors dll/src/Mimic.h.
        var s = Gen(E("A"));
        Assert.Contains($"{CeMailboxLayout.OffCmd}, {CeMailboxLayout.OffStatus}, {CeMailboxLayout.OffResult}", s);
        Assert.Contains(CeMailboxLayout.OffParamsData, s);
    }

    [Fact]
    public void Generate_WritesCmdLast()
    {
        // The DLL polls cmd; writing it before the operands would dispatch a
        // half-filled mailbox.
        var s = Gen(E("A"));
        int inst = s.IndexOf("writeQword(mb + MB_INST", StringComparison.Ordinal);
        int cmd = s.IndexOf("writeInteger(mb + MB_CMD", StringComparison.Ordinal);
        Assert.True(inst > 0 && cmd > inst, "cmd must be written after the operands");
    }

    [Fact]
    public void Generate_TreatsATimeoutAsAnError()
    {
        // A timeout must not fall through to a success path: `call` answers nil + a
        // reason, and go() turns any non-nil reason into a showMessage and returns.
        var s = Gen(E("A"));
        Assert.Contains("return nil, _msg", s);
        Assert.Contains("if err ~= nil then showMessage('[Coordinate Library] ' .. err); return end", s);

        // 'the DLL did not respond' was a GUESS, and the mailbox already distinguishes
        // the two causes — which send the user to opposite places.
        Assert.DoesNotContain("did not respond", s);
        Assert.Contains("never saw this command", s);
        Assert.Contains("never finished it", s);
    }

    [Fact]
    public void Generate_ReadsTheCurrentMapFromThePoseBlock()
    {
        // GET_POSE puts a null-terminated map name at params+48 (Mimic.h `Cmd`);
        // that is what makes the picker's map guard possible at all.
        Assert.Contains("MB_PARAMS + 48", Gen(E("A")));
    }

    // ── Picker behaviour ─────────────────────────────────────────────────

    [Fact]
    public void Generate_HasBothPlainAndForceTeleport()
    {
        var s = Gen(E("A"));
        Assert.Contains("'Teleport'", s);
        Assert.Contains("'Force teleport'", s);
        Assert.Contains("go(false)", s);
        Assert.Contains("go(true)", s);
    }

    [Fact]
    public void Generate_DoubleClickUsesTheGuardedPathNotForce()
    {
        var s = Gen(E("A"));
        Assert.Contains("lv.OnDblClick = function() go(false) end", s);
    }

    [Fact]
    public void Generate_FilterIsSpaceAndNotSubstring()
    {
        // Mirrors the app's MUST-rule: whitespace-separated terms combined with AND.
        var s = Gen(E("A"));
        Assert.Contains("gmatch(string.lower(text or ''), '%S+')", s);
        Assert.Contains("for i = 1, #ts do", s);
    }

    [Fact]
    public void Generate_ComparesMapsCaseInsensitively()
    {
        var s = Gen(E("A"));
        Assert.Contains("string.lower(e.map) ~= string.lower(mapNow)", s);
    }

    [Fact]
    public void Generate_CapsRenderedRows()
        => Assert.Contains("DISPLAY_CAP", Gen(E("A")));

    // ── Sorting + groups ─────────────────────────────────────────────────

    [Fact]
    public void Generate_PreSortsByGroupThenNaturalLabel()
    {
        var s = Gen(E("Chest 10", "Chest"), E("Chest 2", "Chest"), E("Alpha", "Areas"));
        int alpha = s.IndexOf("label='Alpha'", StringComparison.Ordinal);
        int c2 = s.IndexOf("label='Chest 2'", StringComparison.Ordinal);
        int c10 = s.IndexOf("label='Chest 10'", StringComparison.Ordinal);
        Assert.True(alpha < c2, "Areas sorts before Chest");
        Assert.True(c2 < c10, "Chest 2 must precede Chest 10 (natural order)");
    }

    [Fact]
    public void Generate_ReportsGroupsThatGotNoRadioButton()
    {
        var entries = Enumerable.Range(0, CoordLibraryScriptGenerator.MaxGroupButtons + 3)
            .Select(i => E($"Item {i}", $"Group{i:D2}"))
            .ToArray();

        CoordLibraryScriptGenerator.Generate(entries, out var folded);

        Assert.Equal(3, folded.Count);
        // Folded groups are NOT dropped from the data -- they stay findable via All
        // plus the filter box.
        var s = CoordLibraryScriptGenerator.Generate(entries, out _);
        foreach (var g in folded) Assert.Contains($"group='{g}'", s);
    }

    [Fact]
    public void Generate_NoGroups_OmitsTheGroupRadioGroupEntirely()
    {
        var s = Gen(E("A", group: ""), E("B", group: ""));
        Assert.DoesNotContain("rgGroup", s);
        Assert.Contains("rgMap", s);          // the map scope selector always exists
    }

    [Fact]
    public void Generate_EmptyLibrary_StillProducesAValidShell()
    {
        var s = CoordLibraryScriptGenerator.Generate(Array.Empty<CoordEntry>(), out var folded);
        Assert.Contains("local COORDS = {", s);
        Assert.Contains("-- @UE5CD:END", s);
        Assert.Empty(folded);
    }

    // ── Z tolerance ──────────────────────────────────────────────────────

    [Fact]
    public void Generate_BakesZToleranceIntoTheFencedBlock()
    {
        // It lives INSIDE the fence so it survives the round trip: the picker
        // teleports on its own, so it needs the value the app was using.
        var s = CoordLibraryScriptGenerator.Generate(
            new[] { E("A") }, CoordLibraryScriptGenerator.Flavour.Dll, 25, out _);
        int fenceStart = s.IndexOf(CoordLibraryScriptGenerator.MarkerStart, StringComparison.Ordinal);
        int fenceEnd = s.IndexOf(CoordLibraryScriptGenerator.MarkerEnd, StringComparison.Ordinal);
        var fence = s[fenceStart..fenceEnd];
        Assert.Contains("local Z_TOLERANCE = 25", fence);
    }

    [Fact]
    public void Generate_AppliesZToleranceToTheTeleportNotTheStoredValue()
    {
        var s = CoordLibraryScriptGenerator.Generate(
            new[] { E("A", x: 1, y: 2, z: 3) }, CoordLibraryScriptGenerator.Flavour.Dll,
            25, out _);
        // The entry keeps its captured Z ...
        Assert.Contains("z=3", s);
        // ... and only the arrival is lifted.
        Assert.Contains("writeDouble(pd + 16, e.z + Z_TOLERANCE)", s);
    }

    [Fact]
    public void Generate_ZeroToleranceIsStillEmittedSoTheRoundTripIsExplicit()
    {
        var s = Gen(E("A"));
        Assert.Contains("local Z_TOLERANCE = 0", s);
    }

    [Fact]
    public void GenerateThenParse_RoundTripsZTolerance()
    {
        var s = CoordLibraryScriptGenerator.Generate(
            new[] { E("A") }, CoordLibraryScriptGenerator.Flavour.Dll, 12.5, out _);
        var parsed = CoordLuaParser.Parse(s);
        Assert.Equal(12.5, parsed.ZTolerance);
    }

    [Fact]
    public void Parse_BlockWithoutZTolerance_LeavesItNullNotZero()
    {
        // A block written before the field existed must not silently RESET the
        // user's setting to 0 -- null means "not carried", which the VM ignores.
        var parsed = CoordLuaParser.Parse(
            "-- @UE5CD:COORDS v1\nlocal COORDS = {\n" +
            "  { label='A', map='M', x=1, y=2, z=3 },\n}\n-- @UE5CD:END\n");
        Assert.Single(parsed.Entries);
        Assert.Null(parsed.ZTolerance);
    }

    [Fact]
    public void CsvParse_NeverCarriesZTolerance()
    {
        // By design: a per-row column would imply per-row meaning.
        var parsed = CoordCsvCodec.Parse("label,map,x,y,z\nA,M,1,2,3\n");
        Assert.Null(parsed.ZTolerance);
        Assert.DoesNotContain("olerance", CoordCsvCodec.Write(new[] { E("A") }));
    }

    // ── Numeric contract ─────────────────────────────────────────────────

    [Fact]
    public void Generate_UsesTheCanonicalPrecisionSoLuaMatchesCsvAndJson()
    {
        var e = E("A", x: 67162.3984375, y: -20380.642578125, z: 35.79132080078125);
        var s = CoordLibraryScriptGenerator.Generate(new[] { e }, out _);
        Assert.Contains("x=67162.398", s);
        Assert.Contains("y=-20380.643", s);
        Assert.Contains("z=35.791", s);
    }

    [Fact]
    public void Generate_NumbersAreCultureInvariant()
    {
        var prev = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");
            var s = Gen(E("A", x: 1234.5));
            Assert.Contains("x=1234.5", s);   // never "1234,5"
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = prev; }
    }
}

public class CeLuaHygieneEscapeTests
{
    [Theory]
    [InlineData("a]==]b")]
    [InlineData("]==]")]
    [InlineData("]]")]
    [InlineData("]=]")]
    [InlineData("]===]")]
    public void EscapeLuaString_BreaksClosingLongBracketsOfAnyLevel(string s)
    {
        var escaped = CeLuaHygiene.EscapeLuaString(s);
        Assert.DoesNotContain("]==]", escaped);
        Assert.Contains("\\093", escaped);
    }

    [Theory]
    [InlineData("it's", @"it\'s")]
    [InlineData("a\\b", @"a\\b")]
    [InlineData("a\nb", @"a\nb")]
    [InlineData("a\rb", @"a\rb")]
    [InlineData("a\tb", @"a\tb")]
    public void EscapeLuaString_HandlesTheOrdinaryEscapes(string input, string expected)
        => Assert.Equal(expected, CeLuaHygiene.EscapeLuaString(input));

    [Fact]
    public void EscapeLuaString_LeavesALoneBracketAlone()
        => Assert.Equal("a]b", CeLuaHygiene.EscapeLuaString("a]b"));

    [Fact]
    public void EscapeLuaString_KeepsCjkAndDoubleQuotes()
        => Assert.Equal("寶箱 \"3\"", CeLuaHygiene.EscapeLuaString("寶箱 \"3\""));

    [Fact]
    public void EscapeLuaString_EmptyAndNullAreSafe()
    {
        Assert.Equal("", CeLuaHygiene.EscapeLuaString(""));
        Assert.Equal("", CeLuaHygiene.EscapeLuaString(null));
    }
}
