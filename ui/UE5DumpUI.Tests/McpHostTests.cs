using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UE5DumpUI.Mcp;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D1. What surrounds the MCP server: the reply-size cut, its log, and the
/// properties of <c>UE5DumpUI.exe --mcp</c> that only the source shows -- the branch comes before the single-instance
/// mutex, and nothing under Mcp/ writes to stdout, which carries the protocol.
/// </summary>
public sealed class McpHostTests
{
    private static string Source(string relative) =>
        File.ReadAllText(NumericInputCoercionTests.RepoFile(relative)).Replace("\r\n", "\n");

    /// <summary>Comments removed, so a sentence about Console.Write does not count as one.</summary>
    private static string Code(string s)
    {
        s = Regex.Replace(s, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(s, @"//[^\n]*", "");
    }

    // ── The branch ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheMcpBranch_ComesBeforeTheSingleInstanceMutex_AndBeforeAvalonia()
    {
        // A server must run beside the UI, and several servers beside each other (one per AI session).
        string main = Code(Source("ui/UE5DumpUI/Program.cs"));
        int branch = main.IndexOf("Constants.McpSwitch", StringComparison.Ordinal);
        int mutex = main.IndexOf("TryAcquireSingleInstance", StringComparison.Ordinal);
        int avalonia = main.IndexOf("BuildAvaloniaApp()", StringComparison.Ordinal);
        Assert.True(branch > 0, "Program.Main has no --mcp branch");
        Assert.True(branch < mutex && branch < avalonia, "the --mcp branch must come first");
        Assert.Contains("return Mcp.McpHost.Run();", main);
        Assert.Equal("--mcp", Constants.McpSwitch);
    }

    [Fact]
    public void NothingUnderMcp_WritesToStdout_ButTheServersOwnWriter()
    {
        // stdout carries JSON-RPC: one stray line and the client drops the server.
        string dir = Path.GetDirectoryName(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Mcp/McpHost.cs"))!;
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
        {
            string code = Code(File.ReadAllText(file));
            string name = Path.GetFileName(file);
            Assert.DoesNotMatch(@"Console\.(Write|WriteLine|Out\.Write)", code);
            Assert.DoesNotContain("WriteTo.Console", code);
            if (name != "McpHost.cs") Assert.DoesNotContain("OpenStandardOutput", code);
        }
    }

    [Fact]
    public void TheHost_PointsConsoleOutAtStderr_BeforeTheServerExists()
    {
        string host = Code(Source("ui/UE5DumpUI/Mcp/McpHost.cs"));
        int setOut = host.IndexOf("Console.SetOut(", StringComparison.Ordinal);
        Assert.True(setOut > 0);
        Assert.Contains("OpenStandardError", host[setOut..host.IndexOf(';', setOut)]);
        Assert.True(setOut < host.IndexOf("new McpServer(", StringComparison.Ordinal));
        Assert.True(setOut < host.IndexOf("new McpLog(", StringComparison.Ordinal));
    }

    // ── The reply cut ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AReplyThatFits_IsLeftAlone()
    {
        var payload = new JsonObject { ["a"] = new JsonArray(1, 2, 3) };
        var (text, note) = McpResultFit.Fit(payload, 1000);
        Assert.Null(note);
        Assert.Equal("""{"a":[1,2,3]}""", text);
    }

    [Fact]
    public void ALargeReply_LosesWholeElementsFromItsLargestArray_AndTheNoteSaysHowMany()
    {
        var items = new JsonArray();
        for (int i = 0; i < 5000; i++) items.Add(new JsonObject { ["addr"] = $"0x{i:X8}", ["name"] = "Object_" + i });
        var payload = new JsonObject { ["total"] = 5000, ["objects"] = items, ["small"] = new JsonArray(1, 2) };
        var (text, note) = McpResultFit.Fit(payload, 20_000);
        Assert.InRange(text.Length, 1, 20_000);
        var back = JsonNode.Parse(text)!;   // still JSON
        int kept = back["objects"]!.AsArray().Count;
        Assert.InRange(kept, 100, 4999);
        Assert.Equal("Object_0", back["objects"]![0]!["name"]!.GetValue<string>());   // cut from the end
        Assert.Equal(2, back["small"]!.AsArray().Count);
        Assert.Contains($"objects keeps {kept} of 5000", note);
    }

    [Fact]
    public void TheCut_FindsANestedArray_AndNamesItsPath()
    {
        var fields = new JsonArray();
        for (int i = 0; i < 2000; i++) fields.Add(new JsonObject { ["name"] = "Field" + i, ["type"] = "IntProperty" });
        var payload = new JsonObject { ["class"] = new JsonObject { ["name"] = "C", ["fields"] = fields } };
        var (_, note) = McpResultFit.Fit(payload, 10_000);
        Assert.Contains("class.fields keeps", note);
    }

    [Fact]
    public void AReplyThatCannotBeCut_BecomesAStubThatSaysWhy()
    {
        var payload = new JsonObject { ["blob"] = new string('x', 50_000) };
        var (text, note) = McpResultFit.Fit(payload, 1000);
        Assert.Contains("too large", JsonNode.Parse(text)!["error"]!.GetValue<string>());
        Assert.Contains("Narrow", note);
    }

    [Fact]
    public void TheCap_StaysUnderClaudeCodesFileThreshold()
    {
        // Claude Code saves a result over 50,000 characters to a file the model must then open.
        Assert.InRange(McpResultFit.MaxChars, 10_000, 50_000);
    }

    [Fact]
    public void BuildResult_SendsNoStructuredContent_AndMarksErrors()
    {
        // Sending structuredContent beside the text would double a reply against the client's size cap.
        var ok = McpServer.BuildResult(new McpToolResult(new JsonObject { ["a"] = 1 }));
        Assert.False(ok.ContainsKey("structuredContent"));
        Assert.False(ok.ContainsKey("isError"));
        var bad = McpServer.BuildResult(McpToolResult.Fail("nope"));
        Assert.True(bad["isError"]!.GetValue<bool>());
    }

    // ── The log ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheLog_IsAFileOfThisProcess_InTheUisLogFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "mcplog-" + Guid.NewGuid().ToString("N")[..8]);
        using (var log = new McpLog(root))
        {
            log.Info(Constants.LogCatPipe, "hello {braces} stay");
            Assert.NotNull(log.FilePath);
            Assert.Equal(Path.Combine(root, Constants.LogSubfolderName), Path.GetDirectoryName(log.FilePath));
            Assert.StartsWith(Constants.McpLogFilePrefix, Path.GetFileName(log.FilePath));
            Assert.Contains("-" + Environment.ProcessId + ".log", log.FilePath);
        }
        string text = File.ReadAllText(Directory.EnumerateFiles(Path.Combine(root, Constants.LogSubfolderName)).Single());
        Assert.Contains("[pipe] hello {braces} stay", text);
    }

    [Fact]
    public void ThePrune_TakesOnlyItsOwnAgedLogs()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mcpprune-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string agedOwn = Path.Combine(dir, "mcp-20260101-000000-1.log");
        string freshOwn = Path.Combine(dir, "mcp-20261111-000000-2.log");
        string agedUi = Path.Combine(dir, "view-0.log");   // the UI's: its own sweep decides
        foreach (var f in new[] { agedOwn, freshOwn, agedUi }) File.WriteAllText(f, "x");
        File.SetLastWriteTime(agedOwn, DateTime.Now.AddDays(-40));
        File.SetLastWriteTime(agedUi, DateTime.Now.AddDays(-40));
        int deleted = McpLog.PruneAged(dir, DateTime.Now.AddDays(-Constants.LogMaxAgeDays));
        Assert.Equal(1, deleted);
        Assert.False(File.Exists(agedOwn));
        Assert.True(File.Exists(freshOwn));
        Assert.True(File.Exists(agedUi));
    }

    [Fact]
    public void TheLog_NeverThrows_WhenItsFolderCannotBeMade()
    {
        // A file where the folder should be: the server still serves, without a log.
        string root = Path.Combine(Path.GetTempPath(), "mcplogfile-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllText(root, "not a folder");
        using var log = new McpLog(root);
        Assert.Null(log.FilePath);
        log.Info("still fine");
    }
}
