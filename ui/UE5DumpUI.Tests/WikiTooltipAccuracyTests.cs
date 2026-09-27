using System.Text.RegularExpressions;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [WIKI-TIPS-B2] Tooltips checked against the behaviour they describe, found by the Wiki
/// re-translation pass. Where the fact lives in code (a slider range, a VM default, the
/// category table) the expected text is derived from it, so the tooltip cannot drift again
/// without a failure; the rest pin the claim that was wrong.
/// </summary>
public class WikiTooltipAccuracyTests
{
    [Fact]
    public void Result_filters_say_their_keywords_are_ANDed_not_substring_matched()
    {
        // All go through ObjectTreeFilter.MatchesAllTerms (term-level AND); the last two were
        // missed by the first pass and found by review [WIKI-REVIEW-TEXT].
        foreach (var key in new[] { "str.Tip.PropertySearch.ResultFilter", "str.Tip.IF.Filter", "str.Tip.IP.Filter",
                                    "str.Tip.LiveWalker.FuncFilter", "str.Tip.Con.Filter" })
        {
            var tip = EnString(key);
            Assert.DoesNotContain("substring", tip, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ANDed", tip, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Value_Search_timeout_range_matches_the_slider_and_the_VM_default()
    {
        var panel = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Views/ValueSearchPanel.axaml"));
        var slider = Regex.Match(panel,
            @"<Slider Value=""\{Binding ScanTimeoutSeconds\}""\s+Minimum=""(\d+)"" Maximum=""(\d+)""");
        Assert.True(slider.Success, "the ScanTimeoutSeconds slider was not found");

        // [WIKI-REVIEW-TESTS] A fresh install takes the default from the persisted options, which
        // overwrite the VM initializer at startup; pin the initializer to it so neither can drift.
        int settingsDefault = new UE5DumpUI.Models.UiOptionsSettings().ValueSearch.ScanTimeoutSeconds;
        var vm = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/ViewModels/ValueSearchViewModel.cs"));
        var dflt = Regex.Match(vm, @"_scanTimeoutSeconds = (\d+);");
        Assert.True(dflt.Success, "the ScanTimeoutSeconds initializer was not found");
        Assert.Equal(settingsDefault.ToString(), dflt.Groups[1].Value);

        var expected = $"{slider.Groups[1].Value}–{slider.Groups[2].Value}s, default {settingsDefault}";
        Assert.Contains(expected, EnString("str.Tip.VS.Timeout"), StringComparison.Ordinal);
        Assert.Contains(expected, EnString("str.Tip.VS.MaxResults"), StringComparison.Ordinal);
    }

    [Fact]
    public void Props_tip_does_not_say_native_functions_show_nothing()
    {
        // FunctionPropsDialog disassembles native functions (method "disasm", a Conf column);
        // only an unresolved UFunction::Func leaves it with nothing.
        var tip = EnString("str.Tip.IF.Props");
        Assert.DoesNotContain("show nothing", tip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("heuristic", tip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Locate_in_GWorld_tip_claims_no_GWorld_gate()
    {
        // Audit #5 AE10 removed the client-side IsGWorldAvailable gate on purpose.
        Assert.DoesNotContain("Disabled until", EnString("str.Tip.IP.LocateGWorld"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clear_filters_tip_names_the_BP_Exec_only_toggle_it_resets()
    {
        // InterestingFunctionsViewModel.ClearFilters also sets CallableOnly = false.
        Assert.Contains(EnString("str.IF.CallableOnly"), EnString("str.Tip.IF.ClearFilters"), StringComparison.Ordinal);
    }

    [Fact]
    public void Batch_Props_tip_does_not_say_native_functions_show_0()
    {
        // [WIKI-REVIEW-TEXT] The batch calls the same WalkFunctionPropsAsync as Props (native =
        // disassembly heuristic), and a row nothing could analyse reads PartialResultNotice.NotAnalysedCell.
        var tip = EnString("str.Tip.IF.BatchProps");
        Assert.DoesNotContain("native funcs", tip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(UE5DumpUI.Core.PartialResultNotice.NotAnalysedCell, tip, StringComparison.Ordinal);
    }

    [Fact]
    public void Property_category_tip_names_every_category_the_filter_can_show()
    {
        // [WIKI-REVIEW-TEXT] The IF twin below was fixed in B2; this one omitted Timing.
        var tip = EnString("str.Tip.IP.Category");
        foreach (var cat in Enum.GetValues<PropertyCategory>())
            Assert.Contains(PropertyScoringTable.DisplayName(cat), tip, StringComparison.Ordinal);
    }

    [Fact]
    public void Category_tip_names_every_category_the_filter_can_show()
    {
        var tip = EnString("str.Tip.IF.Category");
        foreach (var cat in Enum.GetValues<FunctionCategory>())
            Assert.Contains(KeywordScoringTable.DisplayName(cat), tip, StringComparison.Ordinal);
    }

    private static string EnString(string key)
    {
        var axaml = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Resources/Strings/en.axaml"));
        var open = $"x:Key=\"{key}\">";
        int start = axaml.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{key} missing from en.axaml");
        start += open.Length;
        return System.Net.WebUtility.HtmlDecode(
            axaml[start..axaml.IndexOf("</sys:String>", start, StringComparison.Ordinal)]);
    }
}
