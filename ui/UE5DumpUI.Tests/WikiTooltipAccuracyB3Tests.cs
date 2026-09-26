using System.Text.RegularExpressions;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [WIKI-TIPS-B3] Teleport / Snapshot / SPC / Class Pivot / Detect Stats text checked against the
/// code, from the Wiki re-translation pass. Where the fact lives in code (record counts, which
/// hotkey section holds a row, the God Mode badge states) the expected text is derived from it.
/// </summary>
public class WikiTooltipAccuracyB3Tests
{
    private const string TeleportVm = "ui/UE5DumpUI/ViewModels/TeleportViewModel.cs";

    [Fact]
    public void Teleport_export_tips_state_the_record_counts_the_builders_produce()
    {
        int teleport = TeleportScriptGenerator.BuildBatchRows().Count;
        int movement = MovementScriptGenerator.BuildBatchRows(100, 100, 100, 0, 0, -1).Count;
        int time = TimeDilationScriptGenerator.BuildBatchRows(1.0).Count;
        int fly = FlyScriptGenerator.BuildBatchRows().Count;
        foreach (var key in new[] { "str.Tip.TP.AddActions", "str.Tip.TP.SaveCt" })
        {
            var tip = EnString(key);
            Assert.Contains($"{teleport} teleport", tip, StringComparison.Ordinal);
            Assert.Contains($"{movement} movement", tip, StringComparison.Ordinal);
            Assert.Contains($"{time} time", tip, StringComparison.Ordinal);
            Assert.Contains($"{fly} Fly", tip, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Gravity_direction_is_UE5_3_everywhere_as_the_DLL_says()
    {
        // Laufen.h: UE5.3+ (stock 5.3 honours GravityDirection, [R7-X7]).
        Assert.Contains("UE5.3+", File.ReadAllText(Repo("dll/src/Laufen.h")), StringComparison.Ordinal);
        foreach (var rel in new[] { "ui/UE5DumpUI/Resources/Strings/en.axaml", TeleportVm,
                                    "ui/UE5DumpUI/Services/MovementScriptGenerator.cs",
                                    "ui/UE5DumpUI/Models/TeleportModels.cs" })
            Assert.DoesNotContain("UE5.4+", File.ReadAllText(Repo(rel)), StringComparison.Ordinal);
        Assert.Contains("UE5.3+", EnString("str.Tip.TP.GdRefresh"), StringComparison.Ordinal);
        Assert.DoesNotContain("not yet exposed", EnString("str.TP.GrHint"), StringComparison.Ordinal);
    }

    [Fact]
    public void Hotkey_hints_name_the_section_that_holds_the_row()
    {
        var vm = File.ReadAllText(Repo(TeleportVm));
        var sectionOf = Regex.Matches(vm,
                @"\b(Experimental)?HotkeyRows\.Add\(new TeleportHotkeyRow \{ ActionId = ""[^""]+"",\s*DisplayName = ""([^""]+)""")
            .ToDictionary(m => m.Groups[2].Value,
                          m => EnString(m.Groups[1].Success ? "str.TP.ExpHkHeader" : "str.TP.HkHeader"));
        foreach (var (hint, row) in new[] { ("str.TP.SjHotkeyHint", "Super Jump toggle"),
                                            ("str.TP.FlyHotkeyHint", "Fly toggle"),
                                            ("str.TP.SeeThroughHotkeyHint", "See-through toggle"),
                                            ("str.TP.GdHotkeyHint", "Gravity Dir toggle") })
        {
            var text = EnString(hint);
            Assert.Contains($"\"{row}\"", text, StringComparison.Ordinal);
            Assert.Contains(sectionOf[row], text, StringComparison.Ordinal);
        }

        var expHint = EnString("str.TP.ExpHkHint");
        foreach (Match m in Regex.Matches(vm, @"\bExperimentalHotkeyRows\.Add\(new TeleportHotkeyRow \{ ActionId = ""[^""]+"",\s*DisplayName = ""([^""]+)"""))
            Assert.Contains(m.Groups[1].Value, expHint, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_BugItGo_hint_says_it_runs_the_field()
    {
        var vm = File.ReadAllText(Repo(TeleportVm));
        var hint = Regex.Match(vm, @"DisplayName = ""Run BugItGo"",\s*Hint = ""([^""]+)""");
        Assert.True(hint.Success);
        Assert.DoesNotContain("last BugIt", hint.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("BugItGo field", hint.Groups[1].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void God_Mode_refresh_tip_names_every_badge_state()
    {
        var vm = File.ReadAllText(Repo(TeleportVm));
        int at = vm.IndexOf("private void ApplyProtectState(", StringComparison.Ordinal);
        var body = vm[at..vm.IndexOf("};", at, StringComparison.Ordinal)];
        var states = Regex.Matches(body, @"=> \(""([^""]+)"",").Select(m => m.Groups[1].Value).ToList();
        Assert.True(states.Count >= 5, "badge states not found");
        var tip = EnString("str.Tip.TP.GmRefresh");
        foreach (var s in states)
            Assert.Contains(s, tip, StringComparison.Ordinal);
    }

    [Fact]
    public void No_UI_string_cites_a_repo_document_or_carries_CJK_text()
    {
        var cjk = new Regex(@"[぀-ヿ㐀-䶿一-鿿]");
        foreach (var (key, value) in AllStrings())
        {
            Assert.False(Regex.IsMatch(value, @"docs/|\.md\b"), $"{key} cites a repo document: {value}");
            Assert.False(cjk.IsMatch(value), $"{key} carries CJK text in an English string: {value}");
        }
    }

    [Fact]
    public void Snapshot_SPC_Pivot_Detect_texts_match_the_code()
    {
        // DenylistScope keeps Diff / Spc / Pivot apart; the hint is shown on two of them.
        Assert.DoesNotContain("also filters", EnString("str.Noise.PanelHint"), StringComparison.Ordinal);
        // SnapshotStore.DiscoverChangesAsync is a local SQLite query, not the DLL.
        Assert.DoesNotContain("server-side", EnString("str.Pivot.Discover.Limits"), StringComparison.Ordinal);
        // DetectStatsViewModel filters with ObjectTreeFilter.MatchesAllTerms.
        var detect = EnString("str.Tip.Detect.Filter");
        Assert.DoesNotContain("substring", detect, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ANDed", detect, StringComparison.Ordinal);
    }

    private static string Repo(string rel) => NumericInputCoercionTests.RepoFile(rel);

    private static IEnumerable<(string Key, string Value)> AllStrings()
    {
        var axaml = File.ReadAllText(Repo("ui/UE5DumpUI/Resources/Strings/en.axaml"));
        foreach (Match m in Regex.Matches(axaml, @"x:Key=""(str\.[^""]+)"">(.*?)</sys:String>", RegexOptions.Singleline))
            yield return (m.Groups[1].Value, System.Net.WebUtility.HtmlDecode(m.Groups[2].Value));
    }

    private static string EnString(string key)
    {
        foreach (var (k, v) in AllStrings())
            if (k == key) return v;
        Assert.Fail($"{key} missing from en.axaml");
        return "";
    }
}
