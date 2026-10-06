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
        Assert.Contains("old.jsonl", gameOnly);
        Assert.Contains("new.jsonl", gameOnly);
        Assert.True(gameOnly.IndexOf("old.jsonl", StringComparison.Ordinal) < gameOnly.IndexOf("new.jsonl", StringComparison.Ordinal));
        Assert.Contains("Engine types: left out", gameOnly);

        var withEngine = DumpDiffHtmlRenderer.Render(await DiffCaseAsync("modules_include_engine", includeEngine: true), minimal: false);
        Assert.Contains("Engine types: included", withEngine);
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
