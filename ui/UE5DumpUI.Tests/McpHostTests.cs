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

    // ── The shipped setup guide ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThePublish_ShipsTheSetupGuide_BesideTheExe()
    {
        // The maintainer (2026-11-11): the MCP setup document ships with every publish; release.yml zips dist\ whole.
        string build = Source("build.ps1");
        int src = build.IndexOf("scripts\\MCP-Setup.html", StringComparison.Ordinal);
        Assert.True(src > 0, "build.ps1 does not copy scripts\\MCP-Setup.html");
        Assert.Contains("Join-Path $DIST_DIR \"MCP-Setup.html\"", build[src..]);
        Assert.Contains("MCP-Setup.html", Source("scripts/DEPLOY_README.html"));
    }

    [Fact]
    public void TheSetupGuide_MatchesTheServer()
    {
        string guide = Source("scripts/MCP-Setup.html");
        Assert.Contains(Constants.McpSwitch, guide);
        Assert.Contains("claude mcp add --scope user " + Constants.McpServerName + " --", guide);
        Assert.Contains("\"mcp__" + Constants.McpServerName + "__read_*\"", guide);
        // Every tool it names is one the server offers.
        var catalog = McpTools.Create(new NoGame()).Select(t => t.Name).ToHashSet();
        var named = Regex.Matches(guide, "<code>((?:read|scan)_[a-z_]+)</code>").Select(m => m.Groups[1].Value)
            .Distinct().ToList();
        Assert.NotEmpty(named);
        foreach (var name in named) Assert.True(catalog.Contains(name), $"MCP-Setup.html names {name}");
    }

    [Fact]
    public void TheSetupGuide_KeepsTheSshServerCommandsMarkedAsReferenceOnly()
    {
        // The maintainer (2026-11-11): the Windows 11 OpenSSH commands are for reference only, may not work on a given
        // machine, and setting up an SSH server is outside this repository's scope.
        string guide = Source("scripts/MCP-Setup.html");
        int commands = guide.IndexOf("Add-WindowsCapability", StringComparison.Ordinal);
        int disclaimer = guide.IndexOf("For reference only.", StringComparison.Ordinal);
        Assert.True(disclaimer > 0 && disclaimer < commands, "the disclaimer must come before the commands");
        Assert.Contains("outside the scope", guide[disclaimer..commands]);
        Assert.Contains("third-party SSH server", guide[disclaimer..commands]);
    }

    private sealed class NoGame : IMcpGame
    {
        public (IReadOnlyList<McpGameInfo> Games, bool LegacyPresent) ListGames() => ([], false);
        public int ConnectedPid => 0;
        public Task<JsonObject> SendAsync(int? pid, JsonObject request, TimeSpan timeout, CancellationToken ct,
            bool requireScan = false) => throw new NotSupportedException();
        public Task<McpScanReport> ScanStateAsync(int? pid, CancellationToken ct) => throw new NotSupportedException();
        public Task<(McpScanReport Before, JsonObject? Started)> StartScanAsync(int? pid, CancellationToken ct)
            => throw new NotSupportedException();
        public string? TakeNotice() => null;
    }

    // ── The reply cut ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AReplyThatFits_IsLeftAlone()
    {
        var payload = new JsonObject { ["a"] = new JsonArray(1, 2, 3) };
        var (text, note, stubbed) = McpResultFit.Fit(payload, 1000);
        Assert.Null(note);
        Assert.False(stubbed);
        Assert.Equal("""{"a":[1,2,3]}""", text);
    }

    [Fact]
    public void ALargeReply_LosesWholeElementsFromItsLargestArray_AndTheNoteSaysHowMany()
    {
        var items = new JsonArray();
        for (int i = 0; i < 5000; i++) items.Add(new JsonObject { ["addr"] = $"0x{i:X8}", ["name"] = "Object_" + i });
        var payload = new JsonObject { ["total"] = 5000, ["objects"] = items, ["small"] = new JsonArray(1, 2) };
        var (text, note, stubbed) = McpResultFit.Fit(payload, 20_000);
        Assert.False(stubbed);
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
        var (_, note, _) = McpResultFit.Fit(payload, 10_000);
        Assert.Contains("class.fields keeps", note);
    }

    [Fact]
    public void AReplyThatCannotBeCut_BecomesAStubThatSaysWhy()
    {
        var payload = new JsonObject { ["blob"] = new string('x', 50_000) };
        var (text, note, stubbed) = McpResultFit.Fit(payload, 1000);
        Assert.True(stubbed);
        Assert.Contains("too large", JsonNode.Parse(text)!["error"]!.GetValue<string>());
        Assert.Contains("Narrow", note);
    }

    [Fact]
    public void AReplyTooLargeToCut_IsSentAsAnError_WithItsAdviceInsideTheOneBlock()
    {
        var result = McpServer.BuildResult(new McpToolResult(new JsonObject { ["blob"] = new string('x', 60_000) },
            Note: "reconnected"));
        Assert.True(result["isError"]!.GetValue<bool>());
        var content = result["content"]!.AsArray();
        Assert.Single(content);
        string note = JsonNode.Parse(content[0]!["text"]!.GetValue<string>())!["note"]!.GetValue<string>();
        Assert.Contains("reconnected", note);
        Assert.Contains("Narrow", note);
    }

    [Fact]
    public void AnErrorsNote_RidesInsideItsFirstBlock_WhichIsAllClaudeCodeShowsOfAnError()
    {
        // The connection gives each note once: a note in a second block of an error would be lost for good.
        var result = McpServer.BuildResult(McpToolResult.Fail("The DLL refused: session_not_found.")
            with { Note = "Value-scan sessions begun on it are gone." });
        var content = result["content"]!.AsArray();
        Assert.Single(content);
        var payload = JsonNode.Parse(content[0]!["text"]!.GetValue<string>())!;
        Assert.Contains("session_not_found", payload["error"]!.GetValue<string>());
        Assert.Contains("sessions begun on it are gone", payload["note"]!.GetValue<string>());
    }

    [Fact]
    public void ASuccessfulReplysNote_StaysASecondBlock()
    {
        var result = McpServer.BuildResult(new McpToolResult(new JsonObject { ["a"] = 1 }, Note: "switched"));
        var content = result["content"]!.AsArray();
        Assert.Equal(2, content.Count);
        Assert.Equal("Note: switched", content[1]!["text"]!.GetValue<string>());
        Assert.False(JsonNode.Parse(content[0]!["text"]!.GetValue<string>())!.AsObject().ContainsKey("note"));
    }

    // ── The DLL side of "background" ────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheDll_ScopesEveryDispatchByItsBackgroundKey_BeforeAnyCommandRuns()
    {
        // Fern.cpp reaches no C++ test target; Aura's half share and the scope are dll_core_test's
        // [PIPE-PER-PROCESS-2026-11-06] D block. Here: the dispatcher sets the scope from the request, after the id and
        // cmd are read and before the first command branch.
        string fern = Code(Source("dll/src/Fern.cpp"));
        int dispatch = fern.IndexOf("std::string Fern::DispatchCommand(", StringComparison.Ordinal);
        Assert.True(dispatch > 0);
        int cmdRead = fern.IndexOf("cmd = request.value(\"cmd\", \"\");", dispatch, StringComparison.Ordinal);
        int scope = fern.IndexOf("Aura::BackgroundWalkScope", dispatch, StringComparison.Ordinal);
        int firstCommand = fern.IndexOf("if (cmd == Renge::CMD_", dispatch, StringComparison.Ordinal);
        Assert.True(cmdRead > 0 && scope > cmdRead && scope < firstCommand,
            "DispatchCommand must open the BackgroundWalkScope before its first command");
        Assert.Contains("request.value(\"background\", false)", fern[scope..firstCommand]);
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
