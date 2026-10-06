using System.IO;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [DUMPDIFF-UI] What the parity cases cannot show: reading a real file (blank lines, lines that are not JSON, the
/// object index), and the HTML report — names escaped, the sections the Markdown report has, the minimal mode, the
/// notes, and the line that points repository users at the command-line script.
/// </summary>
public class DumpDiffServiceTests
{
    private static string Fixture(string caseName, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "scripts", "analysis", "fixtures", "diff_dumps", caseName, file);
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException($"fixture {caseName}/{file} not found above the test binaries");
    }

    private static async Task<DumpDiffResult> DiffCaseAsync(string caseName, bool includeEngine = false)
    {
        var ct = TestContext.Current.CancellationToken;
        var o = await DumpDiffService.LoadAsync(Fixture(caseName, "old.jsonl"), ct: ct);
        var n = await DumpDiffService.LoadAsync(Fixture(caseName, "new.jsonl"), ct: ct);
        return DumpDiffService.Diff(o, n, includeEngine);
    }

    private static async Task<string> WriteTempAsync(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dumpdiff-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
        return path;
    }

    // ---- reading a file ----

    [Fact]
    public async Task A_line_that_is_not_JSON_is_skipped_and_counted_and_blank_lines_are_ignored()
    {
        var path = await WriteTempAsync(
            "{\"kind\":\"meta\",\"module\":\"G.exe\",\"dumper_build\":3626}\r\n" +
            "\r\n" +
            "{\"kind\":\"class\",\"name\":\"A\",\"path\":\"/Game/A\",\"props_size\":8}\n" +
            "{\"kind\":\"class\",\"name\":\"B\",\"path\n" +
            "   \n" +
            "{\"kind\":\"summary\",\"classes_emitted\":2}\n");
        try
        {
            var d = await DumpDiffService.LoadAsync(path, ct: TestContext.Current.CancellationToken);
            Assert.Single(d.Classes);
            Assert.Equal(1, d.BadLines);
            Assert.True(d.HasSummary);
            Assert.Equal(3626, d.DumperBuild);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task The_object_index_is_refused_with_the_class_dump_to_use_instead()
    {
        var path = await WriteTempAsync(
            "{\"kind\":\"meta\",\"file\":\"objects\",\"class_dump\":\"game.jsonl\"}\n" +
            "{\"kind\":\"object\",\"index\":0,\"addr\":\"0x1\",\"name\":\"A\",\"class\":\"Class\",\"outer\":\"\",\"path\":\"/A\"}\n");
        try
        {
            var ex = await Assert.ThrowsAsync<DumpDiffObjectIndexException>(
                () => DumpDiffService.LoadAsync(path, ct: TestContext.Current.CancellationToken));
            Assert.Equal("game.jsonl", ex.ClassDump);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task A_file_cut_off_before_its_summary_is_known_as_such()
    {
        var d = await DumpDiffService.LoadAsync(Fixture("new_dump_cut_off", "new.jsonl"),
            ct: TestContext.Current.CancellationToken);
        Assert.False(d.HasSummary);
        Assert.Equal(3622, d.DumperBuild);
    }

    // ---- the HTML report ----

    [Fact]
    public async Task The_report_is_a_whole_HTML_page_in_UTF_8()
    {
        var html = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("classes_basic"), minimal: false);
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.EndsWith("</html>\n", html);
    }

    [Fact]
    public async Task Every_name_from_the_dump_is_escaped()
    {
        var html = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("edges"), minimal: false);
        Assert.Contains("A&lt;b&gt;&amp;&quot;c&quot;", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("A<b>", html);
    }

    [Fact]
    public async Task The_full_report_lists_added_removed_and_changed_types_with_their_members()
    {
        var html = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("classes_basic"), minimal: false);
        Assert.Contains("Added Classes", html);
        Assert.Contains("ABrandNew", html);
        Assert.Contains("Removed Classes", html);
        Assert.Contains("AOldThing", html);
        Assert.Contains("Changed Classes", html);
        Assert.Contains("Moved fields", html);
        Assert.Contains("0x40", html);          // Health's old offset
        Assert.Contains("0x48", html);          // and its new one
        Assert.Contains("Function signatures changed", html);
        Assert.Contains("TakeDamage", html);
        Assert.Contains("Added properties", html);
        Assert.Contains("NewField", html);
        Assert.Contains("Removed properties", html);
        Assert.Contains("IsDead", html);
        Assert.Contains("Added functions", html);
        Assert.Contains("Heal", html);
    }

    [Fact]
    public async Task The_minimal_report_keeps_only_what_breaks_a_table_and_says_so()
    {
        var html = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("classes_basic"), minimal: true);
        Assert.Contains("Minimal mode", html);
        Assert.Contains("changed enum values", html);
        Assert.Contains("Breaking Changes", html);
        Assert.DoesNotContain("Added Classes", html);
        Assert.DoesNotContain("NewField", html);
        Assert.Contains("Health", html);

        // A class that only gained a field is changed, and breaks nothing.
        var addOnly = await DiffCaseAsync("add_only");
        Assert.Contains("Changed Classes (1)", DumpDiffHtmlRenderer.Render(addOnly, minimal: false));
        Assert.Contains("Breaking Changes (0 class(es))", DumpDiffHtmlRenderer.Render(addOnly, minimal: true));

        var structs = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("structs"), minimal: true);
        Assert.Contains("FSlot", structs);       // only grew: still breaking, it is an array stride
        Assert.Contains("FHit", structs);
        Assert.DoesNotContain("FNew", structs);
    }

    [Fact]
    public async Task Structs_enums_and_parameters_are_reported_by_name()
    {
        var structs = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("structs"), minimal: false);
        Assert.Contains("Changed Structs", structs);
        Assert.Contains("Added Structs", structs);
        Assert.Contains("FNew", structs);
        Assert.Contains("Removed Structs", structs);
        Assert.Contains("FGone", structs);

        var enums = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("enums"), minimal: false);
        Assert.Contains("Changed Enums", enums);
        Assert.Contains("EKind::B", enums);
        Assert.Contains("EKind::C", enums);

        var pars = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("params"), minimal: false);
        Assert.Contains("Pawn", pars);           // Source's class changed
        Assert.Contains("Rotator", pars);        // and the return's struct
    }

    [Fact]
    public async Task What_a_dump_cannot_say_is_said_in_the_report()
    {
        var skipped = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("structs_old_dump_predates"), minimal: false);
        Assert.Contains("Structs: not compared", skipped);
        Assert.Contains("Enums: not compared", skipped);

        var cut = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("new_dump_cut_off"), minimal: false);
        Assert.Contains("no summary line", cut);

        var failed = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("walk_failed"), minimal: false);
        Assert.Contains("walk failed in the new dump", failed);
        Assert.Contains("error line(s)", failed);

        var noNames = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("enums_no_names"), minimal: false);
        Assert.Contains("member names were unavailable", noNames);
        Assert.Contains("entries not compared", noNames);

        var numParms = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("params"), minimal: false);
        Assert.Contains("from num_parms", numParms);
    }

    [Fact]
    public async Task The_report_says_which_file_is_old_and_whether_engine_types_were_compared()
    {
        var gameOnly = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("modules_game_only"), minimal: false);
        // The labels themselves: the <title> names both files too, old first, so an order check there proves nothing.
        Assert.Contains("<b>Old</b>: <code>old.jsonl</code>", gameOnly);
        Assert.Contains("<b>New</b>: <code>new.jsonl</code>", gameOnly);
        Assert.Contains("Engine types: left out", gameOnly);

        var withEngine = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("modules_include_engine", includeEngine: true), minimal: false);
        Assert.Contains("Engine types: included", withEngine);
    }

    private static async Task<DumpDiffResult> DiffTextsAsync(string oldText, string newText)
    {
        var ct = TestContext.Current.CancellationToken;
        var o = await WriteTempAsync(oldText);
        var n = await WriteTempAsync(newText);
        try
        {
            return DumpDiffService.Diff(await DumpDiffService.LoadAsync(o, ct: ct),
                                        await DumpDiffService.LoadAsync(n, ct: ct), includeEngine: false);
        }
        finally { File.Delete(o); File.Delete(n); }
    }

    private const string StructSummary = "{\"kind\":\"summary\",\"structs_emitted\":1,\"enums_emitted\":0,\"enums_listed\":true}\n";

    [Fact]
    public async Task Numbers_read_the_same_in_every_culture()
    {
        // A culture whose minus sign is U+2212 (ICU's sv-SE): a struct that shrank must still read "(-4)".
        var diff = await DiffTextsAsync(
            "{\"kind\":\"meta\",\"module\":\"G.exe\"}\n{\"kind\":\"struct\",\"name\":\"FS\",\"path\":\"/Game/FS\",\"props_size\":12}\n" + StructSummary,
            "{\"kind\":\"meta\",\"module\":\"G.exe\"}\n{\"kind\":\"struct\",\"name\":\"FS\",\"path\":\"/Game/FS\",\"props_size\":8}\n" + StructSummary);
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
            var html = DumpDiffHtmlRenderer.Render(diff, minimal: false);
            Assert.Contains("(-4)", html);
            Assert.DoesNotContain("\u2212", html);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public async Task Two_games_are_called_out_in_the_report_itself()
    {
        // The status line says it too, but the browser covers it; the report is what is read and kept.
        var diff = await DiffCaseAsync("classes_basic");
        var other = DumpDiffHtmlRenderer.Render(diff, minimal: false, differentGames: true);
        Assert.Contains("different games", other);
        Assert.DoesNotContain("different games", DumpDiffHtmlRenderer.Render(diff, minimal: false));
    }

    [Fact]
    public async Task Every_value_a_dump_supplies_is_escaped_wherever_it_is_written()
    {
        // One hostile text per place a dump value reaches the page: the header, the added / removed listings, a
        // function's name, return type, flags and parameters, every member list of a changed class, an enum and its
        // enumerators.
        const string Meta = "{\"kind\":\"meta\",\"module\":\"<m>&.exe\",\"dumped_at\":\"<d>\"}\n";
        const string Summary = "{\"kind\":\"summary\",\"structs_emitted\":1,\"enums_emitted\":1,\"enums_listed\":true}\n";
        string Cls(string flags, string pclass, string props, string extraFunc) =>
            "{\"kind\":\"class\",\"name\":\"C\",\"path\":\"/Game/C\",\"props_size\":8,\"props\":[" + props + "]," +
            "\"funcs\":[{\"name\":\"<fn>\"," +
            "\"return_type\":\"<rt>\",\"num_parms\":1,\"parms_size\":8,\"flags\":\"" + flags + "\",\"params\":[{\"name\":\"<pn>\"," +
            "\"type\":\"ObjectProperty\",\"obj_class\":\"" + pclass + "\",\"offset\":0,\"size\":8}]}," + extraFunc + "]}\n";
        string Prop(string name, string type, int offset) =>
            "{\"name\":\"" + name + "\",\"type\":\"" + type + "\",\"offset\":" + offset + ",\"size\":4}";
        string Func(string name) =>
            "{\"name\":\"" + name + "\",\"return_type\":\"<frt>\",\"num_parms\":0,\"parms_size\":0,\"flags\":\"0x1\"}";
        var oldProps = string.Join(",", Prop("<mp>", "IntProperty", 0), Prop("<tp>", "<t1>", 4), Prop("<rp>", "<rpt>", 8));
        var newProps = string.Join(",", Prop("<mp>", "IntProperty", 12), Prop("<tp>", "<t2>", 4), Prop("<ap>", "<apt>", 16));
        string Enum(int v) =>
            "{\"kind\":\"enum\",\"name\":\"<en>\",\"path\":\"/Game/<en>\",\"entries\":[{\"name\":\"<ev>\",\"value\":" + v + "}]}\n";
        var diff = await DiffTextsAsync(
            Meta + Cls("<f1>", "<p1>", oldProps, Func("<rf>")) + "{\"kind\":\"struct\",\"name\":\"<rem>\",\"path\":\"/Game/<rem>\",\"props_size\":4}\n" + Enum(0) + Summary,
            Meta + Cls("<f2>", "<p2>", newProps, Func("<af>")) + "{\"kind\":\"class\",\"name\":\"<add>\",\"path\":\"/Game/<add>\",\"props_size\":4}\n" + Enum(1) + Summary);

        var html = DumpDiffHtmlRenderer.Render(diff, minimal: false);
        foreach (var raw in new[] { "<m>", "<d>", "<add>", "<rem>", "<fn>", "<rt>", "<f1>", "<f2>", "<pn>", "<p1>", "<p2>",
                                    "<mp>", "<tp>", "<t1>", "<t2>", "<rp>", "<rpt>", "<ap>", "<apt>", "<af>", "<rf>", "<frt>",
                                    "<en>", "<ev>" })
        {
            Assert.DoesNotContain(raw, html);
            Assert.Contains("&lt;" + raw[1..^1] + "&gt;", html);
        }
    }

    [Fact]
    public async Task One_line_points_repository_users_at_the_command_line_script()
    {
        // The maintainer's request (2026-10-06): diff_dumps.py is a CLI only a clone of the repository has.
        var html = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("classes_self"), minimal: false);
        Assert.Contains("scripts/analysis/diff_dumps.py", html);
        Assert.Contains("clone of the repository", html);
    }
}
