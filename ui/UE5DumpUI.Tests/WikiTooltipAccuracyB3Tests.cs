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
    public void Save_CT_tip_states_the_record_counts_the_builders_produce()
    {
        // SaveCtAsync is built from exactly these four builders.
        var tip = EnString("str.Tip.TP.SaveCt");
        Assert.Contains($"{TeleportScriptGenerator.BuildBatchRows().Count} teleport", tip, StringComparison.Ordinal);
        Assert.Contains($"{MovementScriptGenerator.BuildBatchRows(100, 100, 100, 0, 0, -1).Count} movement", tip, StringComparison.Ordinal);
        Assert.Contains($"{TimeDilationScriptGenerator.BuildBatchRows(1.0).Count} time", tip, StringComparison.Ordinal);
        Assert.Contains($"{FlyScriptGenerator.BuildBatchRows().Count} Fly", tip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_actions_tip_states_the_record_counts_the_command_sends()
    {
        // [WIKI-REVIEW-TESTS] AddActionsToCeAsync does not use the .CT builders -- its lists are its
        // own -- so count what the command actually sends, with Experimental on (every group present).
        var dir = Path.Combine(Path.GetTempPath(), $"UE5DumpAddActions_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var bridge = new FlyExportFollowsExperimentalTests.RecordingBridge();
            var vm = new UE5DumpUI.ViewModels.TeleportViewModel(new StubDumpService(), new MockLoggingService(),
                new FlyExportFollowsExperimentalTests.SavingPlatform(dir), aobMaker: bridge,
                experimentalGate: new FlyExportFollowsExperimentalTests.Gate(true));
            await vm.AddActionsToCeCommand.ExecuteAsync(null);

            int Sent(string prefix) => bridge.Descriptions.Count(d => d.StartsWith(prefix, StringComparison.Ordinal));
            var tip = EnString("str.Tip.TP.AddActions");
            Assert.Contains($"{Sent("Teleport:")} teleport", tip, StringComparison.Ordinal);
            Assert.Contains($"{Sent("Movement:")} movement", tip, StringComparison.Ordinal);
            Assert.Contains($"{Sent("Time:")} time", tip, StringComparison.Ordinal);
            Assert.Contains($"{Sent("Fly:")} Fly", tip, StringComparison.Ordinal);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ } }
    }

    [Fact]
    public void Gravity_direction_is_UE5_3_everywhere_as_the_DLL_says()
    {
        // Laufen.h: UE5.3+ (stock 5.3 honours GravityDirection, [R7-X7]).
        Assert.Contains("UE5.3+", File.ReadAllText(Repo("dll/src/Laufen.h")), StringComparison.Ordinal);
        // Comments too: "pre-5.4" beside a UE5.3+ edit slipped past a plain "UE5.4+" check
        // [WIKI-REVIEW-TEXT]. Test files may still quote the old message as history.
        var stale = new Regex(@"pre-5\.4|5\.4\+", RegexOptions.IgnoreCase);
        foreach (var rel in new[] { "ui/UE5DumpUI/Resources/Strings/en.axaml", TeleportVm,
                                    "ui/UE5DumpUI/Services/MovementScriptGenerator.cs",
                                    "ui/UE5DumpUI/Models/TeleportModels.cs",
                                    "ui/UE5DumpUI/Core/IDumpService.cs" })
        {
            var hit = stale.Match(File.ReadAllText(Repo(rel)));
            Assert.False(hit.Success, $"{rel} still says \"{hit.Value}\"");
        }
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
    public void Cursor_hotkey_tip_spells_out_each_rung_of_the_ladder()
    {
        // [TP-CURSORHK-TIP] "Ctrl+F8→F5" read as a key sequence. Each modifier's first and last
        // candidate must appear as a full combo, taken from the service's own ladder.
        var svc = File.ReadAllText(Repo("ui/UE5DumpUI/Services/WindowsGlobalHotkeyService.cs"));
        var ladder = Regex.Matches(svc, @"\(HotkeyModifiers\.(\w+),\s*VK_F\d+,\s*""([^""]+)""\)")
            .Select(m => (Mod: m.Groups[1].Value, Label: m.Groups[2].Value)).ToList();
        Assert.True(ladder.Count >= 4, "the cursor-hotkey ladder was not found");
        var tip = EnString("str.Tip.TP.CursorHotkey");
        foreach (var rung in ladder.GroupBy(r => r.Mod))
        {
            Assert.Contains(rung.First().Label, tip, StringComparison.Ordinal);
            Assert.Contains(rung.Last().Label, tip, StringComparison.Ordinal);
        }
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

    // [UI-TEXT-AUDIT-2026-11-18] UT-17. The Functions filter matches the interface a function comes through as well as
    // its name (FilterBoxViewKeepTests pins the matcher); the tip says so, in the words the Name column shows it with.
    [Fact]
    public void Function_filter_tip_says_it_matches_the_interface_too()
    {
        var via = EnString("str.LiveWalker.ViaInterface");   // "{0}  (via {1})"
        var shown = via[via.IndexOf('(')..via.IndexOf("{1}", StringComparison.Ordinal)].TrimEnd();
        var tip = EnString("str.Tip.LiveWalker.FuncFilter");
        Assert.Contains(shown, tip, StringComparison.Ordinal);
        Assert.Contains("interface", tip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ANDed", tip, StringComparison.Ordinal);
    }

    // [UI-TEXT-AUDIT-2026-11-18] UT-16. The Options tip names every setting its flyout holds and says they are kept;
    // each control the flyout binds is in the window's persisted sets.
    [Fact]
    public void Options_tip_names_every_setting_in_the_flyout_and_says_they_are_kept()
    {
        var doc = System.Xml.Linq.XDocument.Load(Repo("ui/UE5DumpUI/Views/MainWindow.axaml"));
        var flyout = doc.Descendants().Single(e => e.Name.LocalName == "DropDownButton"
            && (string?)e.Attribute("Content") == "{StaticResource str.Toolbar.Options}");
        var tip = EnString("str.Tip.Toolbar.Options");
        Assert.DoesNotContain("per session", tip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kept across restarts", tip, StringComparison.Ordinal);

        // A label is a toolbar string on a control that is always shown; a conditional warning is not a setting.
        var labels = flyout.Descendants()
            .Where(e => e.Attribute("IsVisible") is null)
            .Select(e => Regex.Match((string?)e.Attribute("Text") ?? (string?)e.Attribute("Content") ?? "",
                                     @"^\{StaticResource (str\.Toolbar\.[\w.]+)\}$"))
            .Where(m => m.Success)
            .Select(m => EnString(m.Groups[1].Value).TrimEnd(':'))
            .ToList();
        Assert.True(labels.Count >= 8, $"only {labels.Count} labels found in the Options flyout");
        foreach (var label in labels)
            Assert.Contains(label, tip, StringComparison.Ordinal);

        var vm = File.ReadAllText(Repo("ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs"));
        string Set(string name)
        {
            int at = vm.IndexOf($"HashSet<string> {name} = new()", StringComparison.Ordinal);
            Assert.True(at >= 0, $"{name} was not found");
            return vm[at..vm.IndexOf("};", at, StringComparison.Ordinal)];
        }
        var main = Set("MainPersist");
        var liveWalker = Set("LiveWalkerPersist");
        foreach (var control in flyout.Descendants().Where(e => e.Name.LocalName is "Slider" or "CheckBox"))
        {
            var b = Regex.Match((string?)control.Attribute("Value") ?? (string?)control.Attribute("IsChecked") ?? "",
                                @"^\{Binding (LiveWalker\.)?(\w+)\}$");
            Assert.True(b.Success, $"unexpected binding on a {control.Name.LocalName} in the Options flyout");
            Assert.Contains(b.Groups[1].Success ? $"nameof(LiveWalkerViewModel.{b.Groups[2].Value})" : $"nameof({b.Groups[2].Value})",
                            b.Groups[1].Success ? liveWalker : main, StringComparison.Ordinal);
        }
    }

    // [UI-TEXT-AUDIT-2026-11-18] UT-14. String Len reaches every Live Walker output that writes a string record or a
    // CSX string child, not only the two CE XML copies; the tip names each by its button.
    [Fact]
    public void String_Len_tip_names_every_output_that_uses_it()
    {
        var lw = File.ReadAllText(Repo("ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs")).Replace("\r\n", "\n");
        foreach (var method in new[] { "ExportCeXmlAsync", "ExportCeFieldXmlAsync", "ExportCsxCoreAsync",
                                       "PushCsxToCeAsync", "PushCeFieldToCeAsync", "AddRecordToCeAsync" })
        {
            int at = lw.IndexOf($" Task {method}(", StringComparison.Ordinal);
            Assert.True(at >= 0, $"{method} was not found");
            var body = lw[at..lw.IndexOf("\n    }\n", at, StringComparison.Ordinal)];
            Assert.Contains("CeStringLength", body, StringComparison.Ordinal);
        }

        var copyCeField = new UE5DumpUI.ViewModels.LiveWalkerViewModel(new StubDumpService(), new MockLoggingService(),
            new MockPlatformService(Path.GetTempPath())).ExportCeFieldButtonLabel;
        var tip = EnString("str.Tip.StringLength");
        foreach (var label in new[] { EnString("str.LiveWalker.ExportCE"), copyCeField, EnString("str.LiveWalker.ExportCsx"),
                                      EnString("str.LiveWalker.PushCsxToCe"), EnString("str.LiveWalker.AddToCe"),
                                      EnString("str.LiveWalker.PushCeField") })
            Assert.Contains(label, tip, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_hotkey_hint_names_Gravity_Dir_and_Get_POV()
    {
        // [UI-TEXT-AUDIT-2026-11-18] UT-08: both rows are in the main hotkey list that str.TP.HkHint introduces.
        var vm = File.ReadAllText(Repo(TeleportVm));
        var hint = EnString("str.TP.HkHint");
        foreach (var (row, words) in new[] { ("Gravity Dir toggle", "Gravity Dir"), ("Get POV", "Get POV") })
        {
            Assert.Matches(@"(?<!Experimental)HotkeyRows\.Add\(new TeleportHotkeyRow \{ ActionId = ""[^""]+"",\s*DisplayName = """
                           + Regex.Escape(row) + @"""", vm);
            Assert.Contains(words, hint, StringComparison.Ordinal);
        }
    }

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
