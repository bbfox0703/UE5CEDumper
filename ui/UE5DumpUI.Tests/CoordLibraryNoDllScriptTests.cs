using System;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// P5 no-DLL flavour — docs/teleport-coord-library-spec.md §3 D5.
/// The point of these is that the two flavours share one data block (so the round
/// trip cannot drift) while differing exactly where they must.
/// </summary>
public class CoordLibraryNoDllScriptTests
{
    private static CoordEntry E(string label, string group = "G", string map = "Map01")
        => new()
        {
            Uid = "u" + label.Length, Label = label, Group = group, Map = map,
            X = 1.5, Y = -2.25, Z = 3, Pitch = 10, Yaw = 20, Roll = 30,
        };

    private static string NoDll(params CoordEntry[] e)
        => CoordLibraryScriptGenerator.Generate(
            e, CoordLibraryScriptGenerator.Flavour.NoDll, out _);

    private static string Dll(params CoordEntry[] e)
        => CoordLibraryScriptGenerator.Generate(
            e, CoordLibraryScriptGenerator.Flavour.Dll, out _);

    // ── Shared data block (the property that keeps the round trip honest) ──

    [Fact]
    public void BothFlavours_EmitTheIdenticalDataBlock()
    {
        var entries = new[] { E("Chest 1", "Chest"), E("Boss", "") };
        string Block(string script)
        {
            int a = script.IndexOf(CoordLibraryScriptGenerator.MarkerStart, StringComparison.Ordinal);
            int b = script.IndexOf(CoordLibraryScriptGenerator.MarkerEnd, StringComparison.Ordinal);
            return script[a..b];
        }
        Assert.Equal(Block(Dll(entries)), Block(NoDll(entries)));
    }

    [Fact]
    public void NoDllScript_RoundTripsThroughTheSameParser()
    {
        var entries = new[] { E("it's here", "Fields"), E("寶箱", "寶箱", "Map02") };
        var parsed = CoordLuaParser.Parse(NoDll(entries));

        Assert.Empty(parsed.Issues);
        Assert.Equal(2, parsed.Entries.Count);
        Assert.Contains(parsed.Entries, e => e.Label == "it's here");
        Assert.Contains(parsed.Entries, e => e.Label == "寶箱");
    }

    // ── What must differ ─────────────────────────────────────────────────

    [Fact]
    public void NoDllScript_DoesNotTouchTheMailbox()
    {
        var s = NoDll(E("A"));
        Assert.DoesNotContain("g_invokeMailbox", s);
        Assert.DoesNotContain("MB_CMD", s);
        Assert.DoesNotContain("writeQword", s);
    }

    [Fact]
    public void DllScript_DoesUseTheMailbox()
    {
        var s = Dll(E("A"));
        Assert.Contains("g_invokeMailbox", s);
        Assert.Contains("MB_CMD", s);
    }

    [Fact]
    public void NoDllScript_WritesRelativeLocationThroughTheTrainerHelpers()
    {
        var s = NoDll(E("A"));
        Assert.Contains("UE5T_pawn()", s);
        Assert.Contains("UE5T.rootOff", s);
        Assert.Contains("UE5T.relLocOff", s);
        Assert.Contains("UE5T_wrv(a, e.x)", s);
        Assert.Contains("UE5T.vecWidth", s);
    }

    [Fact]
    public void NoDllScript_RestoresRotationViaTheControllerBackRef()
    {
        var s = NoDll(E("A"));
        Assert.Contains("UE5T.controllerOff", s);
        Assert.Contains("UE5T.ctrlRotOff", s);
        Assert.Contains("UE5T_wrv(r + UE5T.vecWidth, e.yaw)", s);
    }

    [Fact]
    public void NoDllScript_RequiresSetupAndSaysSo()
    {
        var s = NoDll(E("A"));
        Assert.Contains("UE5T_ready", s);
        Assert.Contains("UE5 Trainer: Setup", s);
    }

    [Fact]
    public void NoDllScript_HasNoMapGuardAndIsHonestAboutIt()
    {
        // The map name is only readable through the DLL. Rather than pretend, the
        // script says the entries are listed but not guarded.
        var s = NoDll(E("A"));
        Assert.Contains("local function currentMap() return nil end", s);
        Assert.Contains("NOT guarded", s);
        Assert.DoesNotContain("rgMap", s);
    }

    [Fact]
    public void DllScript_KeepsTheMapScopeSelector()
        => Assert.Contains("rgMap", Dll(E("A")));

    [Fact]
    public void NoDllScript_CarriesTheWeakWriteCaveat()
    {
        // Same warning the existing standalone trainer gives: on some games the
        // coordinates change but the character does not visibly move. Users must not
        // read that as a bug.
        var s = NoDll(E("A"));
        Assert.Contains("may not visibly move", s);
        Assert.Contains("go stale when the game", s);
    }

    // ── Everything else must still hold ──────────────────────────────────

    [Fact]
    public void NoDllScript_UsesOnlyVerifiedCeControls()
    {
        var s = NoDll(E("A"));
        Assert.DoesNotContain("createListBox", s);
        Assert.DoesNotContain("createComboBox", s);
        Assert.Contains("createListView", s);
        Assert.Contains("createForm", s);
    }

    [Fact]
    public void NoDllScript_NeverEmitsTheAobMakerWrapperTerminator()
        => Assert.DoesNotContain("]==]", NoDll(E("bad ]==] label")));

    [Fact]
    public void NoDllScript_KeepsTheInteractiveHygieneShape()
    {
        var s = NoDll(E("A"));
        Assert.DoesNotContain(CeLuaHygiene.CloseCall, s);   // interactive: no auto-close
        Assert.Contains("return caFree", s);
        Assert.Contains("if syntaxcheck then return end", s);
    }

    [Fact]
    public void NoDllScript_StillCreatesTheAlClientListViewLast()
    {
        var s = NoDll(E("A"));
        int lastPanel = s.LastIndexOf("createPanel", StringComparison.Ordinal);
        int listView = s.IndexOf("createListView", StringComparison.Ordinal);
        Assert.True(lastPanel > 0 && listView > lastPanel);
    }

    [Fact]
    public void NoDllScript_UsesADistinctRecordDescription()
    {
        // So both flavours can sit in one cheat table without colliding.
        Assert.NotEqual(CoordLibraryScriptGenerator.RecordDescription,
                        CoordLibraryScriptGenerator.NoDllRecordDescription);
    }

    // ── What an in-game run of the emitted scripts needs ──────

    [Fact]
    public void NoDllScript_SetupMessage_DoesNotBreakItsOwnLuaString()
    {
        // The message contains apostrophes. Inside a Lua SINGLE-quoted string written
        // from C#, C# collapses its own \' escape to a bare quote, so the emitted Lua is
        // an unterminated string and the whole script fails to compile in CE ("'end'
        // expected"). The Lua literal must therefore be DOUBLE-quoted.
        var s = NoDll(E("A"));
        Assert.Contains("\"enable 'UE5 Trainer: Setup' first\"", s);
        Assert.DoesNotContain("'enable 'UE5", s);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothFlavours_HaveBalancedSingleQuotesOnEveryLine(bool dllFlavour)
    {
        // Structural guard for the class of bug above: an unterminated single-quoted
        // Lua string takes the whole generated script down in CE. Walk each line as
        // Lua would -- a "--" only starts a comment when it is OUTSIDE a string, which
        // matters because several emitted messages contain "--" themselves.
        var script = dllFlavour ? Dll(E("A")) : NoDll(E("A"));
        var lines = script.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            char quote = '\0';   // current string delimiter, or NUL outside one
            for (int c = 0; c < line.Length; c++)
            {
                char ch = line[c];
                if (quote != '\0')
                {
                    if (ch == '\\') { c++; continue; }        // escaped char
                    if (ch == quote) quote = '\0';
                    continue;
                }
                if (ch == '\'' || ch == '\"') { quote = ch; continue; }
                if (ch == '-' && c + 1 < line.Length && line[c + 1] == '-') break;  // comment
            }
            Assert.True(quote == '\0',
                $"line {i + 1} leaves a string literal open: {lines[i]}");
        }
    }
    [Fact]
    public void BothFlavours_UseDifferentFormGlobals()
    {
        // With the SAME "UE5CD_form" global in both flavours, the re-open guard makes
        // whichever record is enabled SECOND just re-show the other flavour's window
        // and return -- its own picker is never built. Both records can sit in one
        // table, so this is reachable.
        var dllScript = Dll(E("A"));
        var noDllScript = NoDll(E("A"));

        Assert.Contains("UE5CD_form_nodll", noDllScript);
        Assert.DoesNotContain("UE5CD_form_nodll", dllScript);
        // The DLL flavour must not accidentally match the no-DLL global either.
        Assert.Contains("UE5CD_form = createForm", dllScript);
        Assert.Contains("UE5CD_form_nodll = createForm", noDllScript);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothFlavours_DisableBlockGuardsSyntaxCheck(bool dllFlavour)
    {
        // The DISABLE block needs the guard as much as ENABLE does.
        var s = dllFlavour ? Dll(E("A")) : NoDll(E("A"));
        int disable = s.IndexOf("[DISABLE]", StringComparison.Ordinal);
        Assert.Contains("if syntaxcheck then return end", s[disable..]);
    }

    [Fact]
    public void NoDllScript_VerifiesTheWriteStuckInsteadOfClaimingSuccess()
    {
        // The raw RelativeLocation write is the known-weak path: on games that do
        // not refresh the cached world transform it silently does not stick. Reading
        // back turns an invisible failure into a diagnosis, and stops the picker
        // reporting "Teleported to X" next to a motionless character.
        var s = NoDll(E("A"));
        Assert.Contains("local backX = UE5T_rdv(a)", s);
        Assert.Contains("did not stick", s);
    }

    [Fact]
    public void NoDllScript_PositionWriteMatchesTheTrainerTpRecall()
    {
        // The user's own acceptance criterion: if the logic matches
        // "UE5 Trainer: TP Recall position", it passes. Same helper, same deref
        // chain, same base, same three strides.
        var s = NoDll(E("A"));
        Assert.Contains("local p = UE5T_pawn()", s);
        Assert.Contains("local rc = p and UE5T_deref(p, UE5T.rootOff)", s);
        Assert.Contains("local a = rc + UE5T.relLocOff", s);
        Assert.Contains("UE5T_wrv(a, e.x)", s);
        Assert.Contains("UE5T_wrv(a + UE5T.vecWidth, e.y)", s);
        // Z is the ONE deliberate divergence: the Z-tolerance lift. Everything else --
        // helper, deref chain, base, stride -- is byte-identical to TP Recall.
        Assert.Contains("UE5T_wrv(a + 2 * UE5T.vecWidth, e.z + Z_TOLERANCE)", s);
    }

    [Fact]
    public void NoDllScript_AppliesZToleranceToTheRawWrite()
    {
        var s = CoordLibraryScriptGenerator.Generate(
            new[] { E("A") }, CoordLibraryScriptGenerator.Flavour.NoDll, 20, out _);
        Assert.Contains("local Z_TOLERANCE = 20", s);
        Assert.Contains("UE5T_wrv(a + 2 * UE5T.vecWidth, e.z + Z_TOLERANCE)", s);
    }

    [Fact]
    public void BothFlavours_RereadTheMapAtTeleportTimeNotAtFormOpen()
    {
        // A map captured once when the window opens is stale after the game changes
        // level: the guard would compare against a map already left, and there is no
        // UI poll behind the generated picker to fix it up.
        foreach (var s in new[] { Dll(E("A")), NoDll(E("A")) })
        {
            int go = s.IndexOf("local function go(force)", StringComparison.Ordinal);
            Assert.True(go > 0);
            Assert.Contains("local fresh, ferr = currentMap()", s[go..]);
        }
    }

    [Fact]
    public void NoDllScript_StillHasBothTeleportButtons()
    {
        var s = NoDll(E("A"));
        Assert.Contains("'Teleport'", s);
        Assert.Contains("'Force teleport'", s);
    }

    [Fact]
    public void NoDllScript_GroupRadioGroupStillWorks()
    {
        var s = NoDll(E("A", "Chest"), E("B", "Fields"));
        Assert.Contains("rgGroup", s);
        Assert.Contains("'Chest'", s);
    }
}
