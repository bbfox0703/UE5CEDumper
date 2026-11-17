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
        Assert.Contains("ref.writeSites = acc.writeSites;", aura);
        Assert.Contains("ref.writeSitesTruncated = acc.writeSitesTruncated;", aura);
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
    }

    [Fact]
    public void ThePropsDialogShowsAndOpensTheWriteSites()
    {
        string dialog = Src("ui/UE5DumpUI/Views/FunctionPropsDialog.cs");
        Assert.Contains("x?.WriteSitesText", dialog);
        Assert.Matches(@"NavigateDisassemblerAsync\(\w+\.WriteSites\[0\]", dialog);
        Assert.Contains("string.Join(Environment.NewLine, ", dialog);
        string en = Src("ui/UE5DumpUI/Resources/Strings/en.axaml");
        foreach (string key in new[] { "str.FunctionProps.WriteSitesHeader", "str.FunctionProps.WriteSiteInCe",
                                       "str.FunctionProps.CopyWriteSites" })
            Assert.Contains($"x:Key=\"{key}\"", en);
    }
}
