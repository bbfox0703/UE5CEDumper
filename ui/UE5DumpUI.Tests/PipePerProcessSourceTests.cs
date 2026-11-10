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
