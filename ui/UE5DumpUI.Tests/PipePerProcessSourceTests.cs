using System.Text.RegularExpressions;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] The DLL serves \\.\pipe\UE5DumpBfx_&lt;pid&gt;, one name per game process, so a client
/// names the game it wants instead of landing on whichever game listened first. Fern.cpp, Frieren.cpp and Heiter.cpp
/// reach no C++ test target, so their use of the name is pinned here from the source; the name, the probe reading and
/// the first-instance classification themselves are unit-tested in dll_helpers_test (Voll.h).
/// </summary>
public sealed class PipePerProcessSourceTests
{
    private static string DllSrc(string file) =>
        File.ReadAllText(NumericInputCoercionTests.RepoFile("dll/src/" + file)).Replace("\r\n", "\n");

    /// <summary>Comments removed; string literals kept, since the legacy-name check reads them.</summary>
    private static string NoComments(string s)
    {
        s = Regex.Replace(s, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return string.Join('\n', s.Split('\n').Select(l =>
        {
            // A "//" inside a string literal is not a comment: cut at the first one outside quotes.
            bool inStr = false;
            for (int i = 0; i + 1 < l.Length; i++)
            {
                char ch = l[i];
                if (ch == '\\' && inStr) { i++; continue; }
                if (ch == '"') inStr = !inStr;
                else if (!inStr && ch == '/' && l[i + 1] == '/') return l[..i];
            }
            return l;
        }));
    }

    /// <summary>Comments and string / char literals removed: only code is left.</summary>
    private static string CodeOnly(string s)
    {
        s = NoComments(s);
        s = Regex.Replace(s, "\"(?:\\\\.|[^\"\\\\\\n])*\"", "\"\"");
        return Regex.Replace(s, @"'(?:\\.|[^'\\\n])'", "''");
    }

    private static IEnumerable<(string File, string Text)> DllSources()
    {
        var dir = Path.GetDirectoryName(NumericInputCoercionTests.RepoFile("dll/src/Fern.cpp"))!;
        return Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cpp", StringComparison.Ordinal) || p.EndsWith(".h", StringComparison.Ordinal))
            .Select(p => (Path.GetFileName(p), File.ReadAllText(p).Replace("\r\n", "\n")));
    }

    /// <summary>The protocol break is the decision: the DLL no longer serves the legacy name, so a UI that finds it
    /// knows a game runs an old DLL. Any string in the DLL's code that names the pipe names the per-process form.
    /// A message to the user counts too: telling them to connect to the old name would be wrong.</summary>
    [Fact]
    public void TheDllNamesNoLegacyPipe()
    {
        var legacy = new Regex(@"pipe\\\\+UE5DumpBfx(?!_)");
        var hits = DllSources()
            .SelectMany(f => NoComments(f.Text).Split('\n').Select((l, i) => (f.File, Line: i + 1, l)))
            .Where(x => legacy.IsMatch(x.l))
            .Select(x => $"{x.File}:{x.Line}: {x.l.Trim()}")
            .ToList();
        Assert.True(hits.Count == 0, "the legacy pipe name is still in the DLL's code:\n" + string.Join('\n', hits));
    }

    /// <summary>A startup probe that reads ERROR_PIPE_BUSY as "no pipe" starts a second server on this process's
    /// name whenever every instance is taken -- a stale proxy and a fresh injection then both serve. Every probe of
    /// the pipe in the two startup paths goes through Voll::ProbeFoundServer, which reads 231 as "a server is
    /// there".</summary>
    [Theory]
    [InlineData("Frieren.cpp")]   // UE5_StartPipeServer
    [InlineData("Heiter.cpp")]    // the auto-start
    public void EveryStartupProbeReadsBusyAsAServer(string file)
    {
        var code = CodeOnly(DllSrc(file));
        int opens = Regex.Matches(code, @"\bCreateFileW\s*\(\s*Voll::OwnPipeName\(\)\.c_str\(\)").Count;
        int reads = Regex.Matches(code, @"\bVoll::ProbeFoundServer\s*\(").Count;
        Assert.True(opens == 1, $"{file}: {opens} open(s) of Voll::OwnPipeName(), expected the one startup probe");
        Assert.True(reads == opens, $"{file}: {opens} probe(s) but {reads} Voll::ProbeFoundServer reading(s)");
    }

    /// <summary>A deferring watcher asks whether the name is still served every 500 ms for up to five minutes.
    /// Opening the pipe to ask connects to the server each time, and a server with no other client tears its
    /// session down for each one; WaitNamedPipeW answers without connecting, and Voll::WaitSaysServed reads its busy
    /// answer as "still served".</summary>
    [Theory]
    [InlineData("Frieren.cpp")]   // WatchForPipeAndClaim
    [InlineData("Heiter.cpp")]    // the auto-start watcher
    public void EveryWatcherAsksWithoutConnecting(string file)
    {
        var code = CodeOnly(DllSrc(file));
        int waits = Regex.Matches(code, @"\bWaitNamedPipeW\s*\(\s*Voll::OwnPipeName\(\)\.c_str\(\)\s*,\s*Voll::kWatchWaitMs\s*\)").Count;
        int reads = Regex.Matches(code, @"\bVoll::WaitSaysServed\s*\(").Count;
        Assert.True(waits == 1, $"{file}: {waits} WaitNamedPipeW(Voll::OwnPipeName(), Voll::kWatchWaitMs), expected 1");
        Assert.True(reads == waits, $"{file}: {waits} wait(s) but {reads} Voll::WaitSaysServed reading(s)");
    }

    /// <summary>Stage C: the connection thread names every request through Voll::ExtractCmdName, which tolerates the
    /// space Python's json.dumps writes after a colon -- the old inline match for `"cmd":"` named no rig's command. And
    /// the name is written BEFORE inFlight goes up, so whoever sees a command in flight sees its name.</summary>
    [Fact]
    public void EveryRequestIsNamedWhateverItsJsonSpacing()
    {
        var src = NoComments(DllSrc("Fern.cpp"));
        Assert.DoesNotContain("""line.find("\"cmd\":\"")""", src);
        var code = CodeOnly(DllSrc("Fern.cpp"));
        Assert.Matches(@"\bVoll::ExtractCmdName\s*\(\s*line\s*\)", code);
        int named = code.IndexOf("conn->cmdName = cmd", StringComparison.Ordinal);
        int flight = code.IndexOf("conn->inFlight.store(true", StringComparison.Ordinal);
        Assert.True(named >= 0 && flight >= 0 && named < flight,
            "HandleConnection must write cmdName before it raises inFlight");
    }

    /// <summary>Stage C, the maintainer's policy: no client is disconnected for idling, but who holds the connections
    /// can be seen. The accept thread reads the client's pid BEFORE the connection is registered (so every reader
    /// that finds it in m_conns sees it), pipe_clients answers, and the at-capacity line -- what a client that
    /// cannot connect reads from the log -- carries the holders.</summary>
    [Fact]
    public void WhoHoldsThePipeIsRecordedAndShown()
    {
        var code = CodeOnly(DllSrc("Fern.cpp"));
        int id = code.IndexOf("GetNamedPipeClientProcessId(", StringComparison.Ordinal);
        int reg = code.IndexOf("m_conns.push_back(conn)", StringComparison.Ordinal);
        Assert.True(id >= 0 && reg >= 0 && id < reg, "AcceptLoop must read the client's pid before registering it");
        Assert.Matches(@"\bcmd\s*==\s*Renge::CMD_PIPE_CLIENTS\b", code);
        int enter = code.IndexOf("case Voll::AcceptLog::EnterCapacity:", StringComparison.Ordinal);
        Assert.True(enter >= 0, "Fern.cpp: the EnterCapacity case is gone -- re-point this pin");
        int end = code.IndexOf("break;", enter, StringComparison.Ordinal);
        var branch = code[enter..end];
        Assert.Contains("SnapshotHolders(", branch);
        Assert.Contains("Grimoire::PIPE_HOLDERS_TOKEN", branch);
    }

    /// <summary>The words of the at-capacity line exist three times -- the DLL that writes them (Grimoire.h), the UI
    /// and pipe_client.py that read them -- and a reader that drifts from the writer finds nothing, silently.</summary>
    [Theory]
    [InlineData("PIPE_AT_CAPACITY_TEXT", Constants.PipeAtCapacityText, "AT_CAPACITY_TEXT")]
    [InlineData("PIPE_HOLDERS_TOKEN", Constants.PipeHoldersToken, "HOLDERS_TOKEN")]
    [InlineData("PIPE_HOLDERS_MORE_TOKEN", Constants.PipeHoldersMoreToken, "HOLDERS_MORE_TOKEN")]
    [InlineData("PIPE_SLOT_FREED_TEXT", Constants.PipeSlotFreedText, "SLOT_FREED_TEXT")]
    public void TheHoldersLineReadsAsTheDllWritesIt(string grimoire, string ui, string python)
    {
        var dll = Regex.Match(DllSrc("Grimoire.h"), $@"\b{grimoire}\s*=\s*""([^""]*)""");
        Assert.True(dll.Success, $"Grimoire.h: {grimoire} not found");
        Assert.Equal(dll.Groups[1].Value, ui);
        var py = Regex.Match(File.ReadAllText(NumericInputCoercionTests.RepoFile("tools/verify/pipe_client.py")),
                             $@"(?m)^{python}\s*=\s*""([^""]*)""");
        Assert.True(py.Success, $"pipe_client.py: {python} not found");
        Assert.Equal(dll.Groups[1].Value, py.Groups[1].Value);
    }

    /// <summary>Stage B: six clients at once -- the UI's two lanes, a rig, an MCP client, and two more for a lane
    /// reconnect whose old instances are still unwinding. Windows takes 1..254 (255 is PIPE_UNLIMITED_INSTANCES).
    /// A name another copy created first keeps that copy's limit (measured 2026-11-10: a second create asking for 6
    /// succeeds and GetNamedPipeInfo still reports 3), so the log states the limit in force, read from the
    /// handle.</summary>
    [Fact]
    public void TheDllTakesSixClientsAndLogsTheLimitInForce()
    {
        var h = CodeOnly(DllSrc("Fern.h"));
        Assert.Matches(@"\bstatic\s+constexpr\s+DWORD\s+kMaxPipeInstances\s*=\s*6\s*;", h);
        Assert.Matches(@"\bstatic_assert\s*\(\s*kMaxPipeInstances\s*>=\s*1\s*&&\s*kMaxPipeInstances\s*<\s*PIPE_UNLIMITED_INSTANCES\b", h);
        Assert.Matches(@"\bGetNamedPipeInfo\s*\(\s*pipe\s*,", CodeOnly(DllSrc("Fern.cpp")));
    }

    /// <summary>After a Stop whose drain timed out, this copy's own connections still hold their instances, so a
    /// restart's probe reads busy. That busy pipe is this copy's, not another's: UE5_StartPipeServer restarts the
    /// server rather than deferring to itself.</summary>
    [Fact]
    public void ARestartIsNotDeferredToThisCopysOwnConnections()
    {
        var code = CodeOnly(DllSrc("Frieren.cpp"));
        int start = code.IndexOf("bool UE5_StartPipeServer()", StringComparison.Ordinal);
        Assert.True(start >= 0, "Frieren.cpp: UE5_StartPipeServer not found -- re-point this pin");
        var body = code[start..];
        int decide = body.IndexOf("Voll::DecideStart", StringComparison.Ordinal);
        int straggler = body.IndexOf("s_pipeServer.HasStragglerConnections()", StringComparison.Ordinal);
        Assert.True(straggler >= 0 && straggler < decide,
            "UE5_StartPipeServer must ask s_pipeServer.HasStragglerConnections() before Voll::DecideStart defers");
    }

    /// <summary>Fern serves this process's name and checks its first instance: another process that created the
    /// name first gets no server beside it, and a second copy of the DLL in this process is logged.</summary>
    [Fact]
    public void FernServesItsOwnNameAndChecksItsFirstInstance()
    {
        var code = CodeOnly(DllSrc("Fern.cpp"));
        var create = Regex.Match(code, @"\bCreateNamedPipeW\s*\(\s*([^,]+),");
        Assert.True(create.Success, "Fern.cpp: no CreateNamedPipeW call -- re-point this pin");
        Assert.Equal("Voll::OwnPipeName().c_str()", create.Groups[1].Value.Trim());
        Assert.Matches(@"\bVoll::ClassifyFirstInstance\s*\(", code);
        Assert.Matches(@"\bGetNamedPipeServerProcessId\s*\(", code);
        // The Stop wake-poke must dial this process's name: dialling anything else wakes nobody, and Stop waits.
        Assert.DoesNotMatch(@"\bGrimoire::PIPE_NAME\b", code);
    }
}
