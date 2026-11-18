using System.Text.RegularExpressions;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [DENKEN-WRITE-SITES] Denken keeps the address of each native instruction that writes a field (dll_helpers_test's
/// Test_Denken_KeepsWriteSites). The way out -- Aura's mapping onto a property, Fern's walk_function_props reply, the Props
/// dialog's column and its "Write site in CE" action -- sits in files no C++ test target runs a game through, so it is
/// pinned here from the source; the reply's parsing is DumpServiceTests'.
/// </summary>
public sealed class DenkenWriteSitesSourceTests
{
    private static string Src(string rel) =>
        File.ReadAllText(NumericInputCoercionTests.RepoFile(rel)).Replace("\r\n", "\n");

    [Fact]
    public void AuraCarriesTheWriteSitesOntoTheProperty()
    {
        string aura = Src("dll/src/Aura.cpp");
        Assert.Matches(@"ref\.writeSites\s+= acc\.writeSites;", aura);
        Assert.Matches(@"ref\.writeSitesViaThis\s+= acc\.writeSitesViaThis;", aura);
        Assert.Matches(@"ref\.writeSitesTruncated\s+= acc\.writeSitesTruncated;", aura);
        Assert.Matches(@"ref\.sharedOffset\s+= shared;", aura);
        // The live path goes through the tested mapping.
        Assert.Contains("out.refs = MapNativeAccesses(r.accesses, ci.Fields, out.unmappedAccesses);", aura);
        Assert.Matches(@"std::vector<uintptr_t>\s+writeSites;", Src("dll/src/Aura.h"));
    }

    [Fact]
    public void WalkFunctionPropsSendsTheWriteSites()
    {
        string fern = Src("dll/src/Fern.cpp");
        int at = fern.IndexOf("if (cmd == Renge::CMD_WALK_FUNCTION_PROPS) {", StringComparison.Ordinal);
        Assert.True(at > 0, "walk_function_props' handler not found -- re-point this pin");
        string handler = fern[at..fern.IndexOf("return Renge::MakeResponse(id, data).dump();", at, StringComparison.Ordinal)];
        Assert.Matches(@"for \(uintptr_t \w+ : r\.writeSites\) \w+\.push_back\(Renge::AddrToStr\(\w+\)\);", handler);
        Assert.Contains("rj[\"write_sites\"]", handler);
        Assert.Contains("rj[\"write_sites_truncated\"] = true;", handler);
        Assert.Contains("rj[\"write_sites_via_this\"] = r.writeSitesViaThis;", handler);
        Assert.Contains("rj[\"shared_offset\"] = r.sharedOffset;", handler);
    }

    [Fact]
    public void ThePropsDialogShowsAndOpensTheWriteSites()
    {
        string dialog = Src("ui/UE5DumpUI/Views/FunctionPropsDialog.cs");
        Assert.Contains("x?.WriteSitesText", dialog);
        // Through the shared helper, which names a busy or absent AOBMaker pipe (review WS-UI-1, [W1-PIPEBUSY-STATUS]).
        Assert.Matches(@"AobMakerActions\.MoveViewAsync\(bridge, x\.WriteSites\[0\], x\.Name, null, disassembler: true\)", dialog);
        Assert.DoesNotContain("NavigateDisassemblerAsync(", dialog);
        // Copy copies every site (review WS-TEST-1: the tooltip's Join matched the old pin as well).
        Assert.Contains("string text = string.Join(Environment.NewLine, x.WriteSites);", dialog);
        // The two buttons follow the selection, after every scan too (WS-TEST-1).
        Assert.Contains("_btnCopySites.IsEnabled = hasSites;", dialog);
        Assert.Contains("bool up = SharedAobMaker?.IsAvailable == true;", dialog);
        Assert.Contains("_btnSiteInCe.IsEnabled = hasSites && up;", dialog);
        Assert.Contains("_grid.SelectionChanged += (_, _) => UpdateSelectionButtons();", dialog);
        Assert.Matches(@"finally\s*\{\s*_btnRefresh\.IsEnabled = true;\s*UpdateSelectionButtons\(\);", dialog);
        string en = Src("ui/UE5DumpUI/Resources/Strings/en.axaml");
        foreach (string key in new[] { "str.FunctionProps.WriteSitesHeader", "str.FunctionProps.WriteSiteInCe",
                                       "str.FunctionProps.CopyWriteSites", "str.FunctionProps.WriteSiteViaThis",
                                       "str.FunctionProps.WriteSiteOtherBase", "str.FunctionProps.SharedOffsetNote" })
            Assert.Contains($"x:Key=\"{key}\"", en);
    }
}
