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

    /// <summary>The cleanup a connection runs after its handler returns: from its unregistering to the notify that
    /// lets Stop's drain see it gone.</summary>
    private static string ConnectionCleanup(string code)
    {
        int start = code.IndexOf("SetPhase(*conn, Connection::Phase::Unregistering)", StringComparison.Ordinal);
        Assert.True(start >= 0, "Fern.cpp: the Unregistering phase is gone -- re-point this pin");
        int end = CleanupEnd(code, start);
        Assert.True(end > start, "Fern.cpp: no end to the cleanup -- re-point this pin");
        return code[start..end];
    }

    /// <summary>The body of `if (cmd == Renge::CMD_X) {` up to the next command's `if (cmd == ...)`.</summary>
    private static string CommandBranch(string code, string renge)
    {
        var m = Regex.Match(code, $@"\bif\s*\(\s*cmd\s*==\s*Renge::{renge}\s*\)");
        Assert.True(m.Success, $"Fern.cpp: no branch for Renge::{renge} -- re-point this pin");
        var next = Regex.Match(code[(m.Index + m.Length)..], @"\bif\s*\(\s*cmd\s*==\s*Renge::CMD_");
        return next.Success ? code.Substring(m.Index, m.Length + next.Index) : code[m.Index..];
    }

    /// <summary>Stage C: what a client turns on is that client's. The last pipe client to leave used to turn
    /// See-through off for everyone -- CE's record included, which then showed ticked over a feature that was off --
    /// and to drop the LiveFuncs recording whoever started it, while a client that left with another still connected
    /// left both running for nobody. A departing client process now releases what it owns; only caches and
    /// diagnostics wait for the last client.</summary>
    [Fact]
    public void ADepartingClientReleasesOnlyWhatItTurnedOn()
    {
        var code = CodeOnly(DllSrc("Fern.cpp"));
        var cleanup = ConnectionCleanup(code);
        Assert.Matches(@"\bReleaseOwnedBy\s*\(\s*conn->ownerKey\s*\)", cleanup);
        Assert.DoesNotMatch(@"\bSchlacht::SetEnabled\s*\(", cleanup);
        Assert.DoesNotMatch(@"\bLinie::Reset\s*\(", cleanup);
        Assert.DoesNotMatch(@"\bJoinArmWorker\s*\(", cleanup);

        int def = code.IndexOf("void Fern::ReleaseOwnedBy(", StringComparison.Ordinal);
        Assert.True(def >= 0, "Fern.cpp: Fern::ReleaseOwnedBy is not defined");
        int open = code.IndexOf('{', def);
        int depth = 0, close = open;
        for (; close < code.Length; close++)
        {
            if (code[close] == '{') depth++;
            else if (code[close] == '}' && --depth == 0) break;
        }
        var body = code[open..close];
        Assert.Matches(@"\bSchlacht::Release\s*\(\s*owner\s*,", body);
        // The profile goes only when this client started it, decided under the lock every profile command takes.
        int lk = body.IndexOf("m_profileMu", StringComparison.Ordinal);
        int own = Regex.Match(body, @"\bm_profileOwner\s*==\s*owner\b|\bowner\s*==\s*m_profileOwner\b").Index;
        int reset = body.IndexOf("Linie::Reset()", StringComparison.Ordinal);
        Assert.True(lk >= 0 && own > lk && reset > own,
            "ReleaseOwnedBy must take m_profileMu, check m_profileOwner == owner, then Linie::Reset");
    }

    /// <summary>The owner key is the client process, read before the connection is registered; CE is one more
    /// owner; and both places that turn See-through on say who asked.</summary>
    [Fact]
    public void EveryEnableNamesItsOwner()
    {
        var code = CodeOnly(DllSrc("Fern.cpp"));
        int key = code.IndexOf("conn->ownerKey = Voll::ClientOwnerKey(", StringComparison.Ordinal);
        int reg = code.IndexOf("m_conns.push_back(conn)", StringComparison.Ordinal);
        Assert.True(key >= 0 && reg >= 0 && key < reg, "AcceptLoop must set conn->ownerKey before registering it");
        Assert.Matches(@"\bSchlacht::SetEnabled\s*\([^;]*,\s*conn->ownerKey\s*\)", CommandBranch(code, "CMD_SEE_THROUGH_SET"));
        Assert.Matches(@"\bSchlacht::SetEnabled\s*\([^;]*,\s*Schlacht::kOwnerCe\s*\)", CodeOnly(DllSrc("Mimic.cpp")));
    }

    /// <summary>Starting a recording joins the old arm worker and installs a new one, and the pair is not atomic:
    /// two clients starting at once destroyed a worker whose thread still ran. Every command that starts, stops or
    /// frees the recording takes m_profileMu, and a start records who owns it.</summary>
    [Fact]
    public void TheRecordingHasOneOwnerAndOneWriterAtATime()
    {
        var code = CodeOnly(DllSrc("Fern.cpp"));
        var start = CommandBranch(code, "CMD_PE_PROFILE_START");
        Assert.Matches(@"std::lock_guard<std::mutex>\s+\w+\s*\(\s*m_profileMu\s*\)", start);
        Assert.Matches(@"\bm_profileOwner\s*=\s*conn->ownerKey\s*;", start);
        foreach (var renge in new[] { "CMD_PE_PROFILE_STOP", "CMD_PE_TRACE_RELEASE" })
            Assert.Matches(@"std::lock_guard<std::mutex>\s+\w+\s*\(\s*m_profileMu\s*\)", CommandBranch(code, renge));
    }

    /// <summary>Stage C: a scan session is the connection's that began it. Every use of a session names the
    /// connection asking, a departing connection drops its own sessions whoever stays, and the last connection drops
    /// none of anyone's -- its DropAll used to wipe a session a new client had just begun.</summary>
    [Fact]
    public void AScanSessionIsItsConnections()
    {
        var code = CodeOnly(DllSrc("Fern.cpp"));
        var uses = Regex.Matches(code, @"\bRadar::(?:Group)?SessionManager::Instance\(\)\.(\w+)\(\s*([^,)]*)");
        var unnamed = uses.Where(m => m.Groups[1].Value is not ("DropAll")
                                      && m.Groups[2].Value.Trim() != "conn->seq")
                          .Select(m => m.Value).ToList();
        Assert.True(uses.Count >= 11, $"Fern.cpp: only {uses.Count} session-manager call(s) found -- re-point this pin");
        Assert.True(unnamed.Count == 0, "session calls that do not name their connection:\n" + string.Join('\n', unnamed));

        var cleanup = ConnectionCleanup(code);
        Assert.Matches(@"\bRadar::SessionManager::Instance\(\)\.DropOwner\(\s*conn->seq\s*\)", cleanup);
        Assert.Matches(@"\bRadar::GroupSessionManager::Instance\(\)\.DropOwner\(\s*conn->seq\s*\)", cleanup);
        Assert.DoesNotMatch(@"\bDropAll\s*\(", cleanup);
    }

    /// <summary>A Begin read its new session back without checking the read: had the session gone in between, it
    /// answered ok with total 0 and a dead id.</summary>
    [Theory]
    [InlineData("CMD_BEGIN_VALUE_SCAN", "SessionManager", "ViewWith")]
    [InlineData("CMD_BEGIN_GROUP_SCAN", "GroupSessionManager", "QueryWith")]
    public void ABeginNeverAnswersWithADeadSession(string renge, string manager, string read)
    {
        var branch = CommandBranch(CodeOnly(DllSrc("Fern.cpp")), renge);
        var m = Regex.Match(branch, $@"\bbool\s+(\w+)\s*=\s*Radar::{manager}::Instance\(\)\.{read}\(");
        Assert.True(m.Success, $"{renge}: the read-back of the new session must keep its result");
        Assert.Matches($@"if\s*\(\s*!\s*{m.Groups[1].Value}\s*\)", branch);
    }

    /// <summary>A session is reachable only from the connection that began it, so all of a session's commands ride
    /// the UI's one bulk lane: one moved to the interactive lane would answer session_not_found every time.</summary>
    [Theory]
    [InlineData("begin_value_scan")]
    [InlineData("refine_value_scan")]
    [InlineData("end_value_scan")]
    [InlineData("query_candidates")]
    [InlineData("begin_group_scan")]
    [InlineData("refine_group_scan")]
    [InlineData("end_group_scan")]
    [InlineData("query_group_candidates")]
    [InlineData("query_group_slot_leaves")]
    public void EverySessionCommandRidesTheBulkLane(string cmd)
        => Assert.True(UE5DumpUI.Services.LaneRoutingPipeClient.IsBulk(cmd), $"{cmd} is not on the bulk lane");

    /// <summary>The body of a member function definition, braces matched.</summary>
    private static string FunctionBody(string code, string signatureStart)
    {
        int def = code.IndexOf(signatureStart, StringComparison.Ordinal);
        Assert.True(def >= 0, $"Fern.cpp: {signatureStart} not found -- re-point this pin");
        int open = code.IndexOf('{', def);
        int depth = 0, close = open;
        for (; close < code.Length; close++)
        {
            if (code[close] == '{') depth++;
            else if (code[close] == '}' && --depth == 0) break;
        }
        return code[open..(close + 1)];
    }

    /// <summary>Stage C: a watch is its connection's. Watches were keyed by address alone, so another client's
    /// unwatch stopped this one's and its watch replaced it -- and a replace racing a re-watch freed an entry whose
    /// thread still ran.</summary>
    [Fact]
    public void AWatchIsItsConnections()
    {
        Assert.Matches(@"std::unordered_map<\s*Kanne::WatchKey\s*,", CodeOnly(DllSrc("Fern.h")));
        var code = CodeOnly(DllSrc("Fern.cpp"));
        Assert.Matches(@"\bStopWatch\s*\(\s*conn->seq\s*,", CommandBranch(code, "CMD_UNWATCH"));
        Assert.Matches(@"\bStopWatchesForConnection\s*\(\s*conn->seq\s*\)", ConnectionCleanupWhole(code));
        var start = FunctionBody(code, "Fern::StartWatch(");
        Assert.DoesNotMatch(@"\bStopWatch\s*\(", start);            // no stop-then-relock: one lock replaces
        Assert.Matches(@"\bm_running\b", start);                     // a watch after StopAllWatches is refused
        Assert.Matches(@"Grimoire::PIPE_WATCH_MAX_PER_CONNECTION", start);
        Assert.Matches(@"Grimoire::PIPE_WATCH_MAX_TOTAL", start);
    }

    /// <summary>Stage C (the maintainer's choice, 2026-11-10): the watch thread does no pipe I/O. It reads memory
    /// and leaves the latest sample in its Kanne::WatchSlot; the owner's handler sends it after each command, on its
    /// own thread, so no join can wait behind a write blocked by the owner's parked read, and an interval cannot make
    /// a join wait (the old sleep could not be interrupted).</summary>
    [Fact]
    public void AWatchThreadNeverTouchesThePipe()
    {
        var code = CodeOnly(DllSrc("Fern.cpp"));
        var start = FunctionBody(code, "Fern::StartWatch(");
        Assert.DoesNotMatch(@"\bWriteLine\s*\(", start);
        Assert.DoesNotMatch(@"\bsleep_for\s*\(", start);
        Assert.Matches(@"\bWaitInterval\s*\(", start);
        int dispatch = code.IndexOf("std::string response = DispatchCommand(conn, line);", StringComparison.Ordinal);
        int flush = code.IndexOf("FlushWatchEvents(*conn)", StringComparison.Ordinal);
        int reply = code.IndexOf("WriteLine(*conn, response)", StringComparison.Ordinal);
        Assert.True(dispatch >= 0 && flush > dispatch && reply > flush,
            "HandleConnection must send its watch samples after DispatchCommand and before the reply");
        // The floor is Grimoire's. No ceiling: the red asked for one because a stop had to wait out the interval,
        // and an interruptible wait removed that reason.
        var watch = CommandBranch(code, "CMD_WATCH");
        Assert.Matches(@"Grimoire::PIPE_WATCH_INTERVAL_MIN_MS", watch);
    }

    private static string ConnectionCleanupWhole(string code)
    {
        int start = code.IndexOf("SetPhase(*conn, Connection::Phase::StoppingWatches)", StringComparison.Ordinal);
        Assert.True(start >= 0, "Fern.cpp: the StoppingWatches phase is gone -- re-point this pin");
        return code[start..CleanupEnd(code, start)];
    }

    /// <summary>Where a connection's cleanup ends: its teardown count's release, or (before that guard) the notify.</summary>
    private static int CleanupEnd(string code, int start)
    {
        int reset = code.IndexOf("teardown.reset()", start, StringComparison.Ordinal);
        int notify = code.IndexOf("m_connCv.notify_all()", start, StringComparison.Ordinal);
        return reset >= 0 && (notify < 0 || reset < notify) ? reset : notify;
    }

    /// <summary>Stage C: trigger_scan and rescan start one job at a time. The old start read `running`, cleared caches
    /// and reset results, then set it: two clients both passed, raced join/assign on one thread (the loser's thread
    /// detached and Stop never joined it), and rescan's second reset wiped the first's results. The job also ran
    /// unbound, so ANY client dying with a command in flight cancelled it.</summary>
    [Theory]
    [InlineData("CMD_TRIGGER_SCAN", "m_scan")]
    [InlineData("CMD_RESCAN", "m_rescan")]
    public void AScanJobStartsOnceAndOwnsItsCancel(string renge, string state)
    {
        var code = CodeOnly(DllSrc("Fern.cpp"));
        var branch = CommandBranch(code, renge);
        Assert.Matches($@"\b{state}\.job\.TryStart\s*\(\s*m_running\s*,", branch);
        Assert.DoesNotMatch($@"\b{state}\.running\b", branch);
        // The maintainer's choice (2026-11-10): a scan another client started is in progress, not a failure.
        Assert.Contains("data[\"in_progress\"]", NoComments(DllSrc("Fern.cpp")));
        Assert.Matches(@"Start::AlreadyRunning", branch);
    }

    /// <summary>Stop cancels each job through its own token and joins it -- and no longer clears `running` first,
    /// which let a connection still dispatching during Stop start a job and join the same thread beside Stop.</summary>
    [Fact]
    public void StopCancelsAndJoinsEachJob()
    {
        var code = CodeOnly(DllSrc("Fern.cpp"));
        Assert.Matches(@"\bm_scan\.job\.StopAndJoin\s*\(\s*\)", code);
        Assert.Matches(@"\bm_rescan\.job\.StopAndJoin\s*\(\s*\)", code);
        Assert.DoesNotMatch(@"\bm_(?:re)?scan\.running\.store\s*\(", code);
    }

    /// <summary>apply_rescan read each result twice from fields a running rescan writes, and could publish 0 and call
    /// Aura::Init(0). It takes one copy, under the lock the writer holds. And a rescan refuses while a scan is still
    /// publishing the pointers it would decide from.</summary>
    [Fact]
    public void RescanResultsAreReadOnceUnderTheirLock()
    {
        var code = CodeOnly(DllSrc("Fern.cpp"));
        var apply = CommandBranch(code, "CMD_APPLY_RESCAN");
        Assert.Single(Regex.Matches(apply, @"\bm_rescan\.Results\s*\(\s*\)"));
        Assert.DoesNotMatch(@"\bm_rescan\.found", apply);
        Assert.DoesNotMatch(@"\bm_rescan\.found", CommandBranch(code, "CMD_RESCAN_STATUS"));
        var rescan = CommandBranch(code, "CMD_RESCAN");
        Assert.Matches(@"\bm_scan\.job\.Running\s*\(\s*\)", rescan);
        Assert.Matches(@"\bg_initInProgress\b", rescan);
    }

    /// <summary>Stage C: the hint cache is changed under one lock. Four writers each read the file, changed one record
    /// and published the whole file, so two at once dropped one change -- while the reply to `persist` said it was
    /// saved. One read-modify-write (UpdateCacheRecord) under a named mutex, which a second copy of the DLL and a second
    /// game share; the reader takes it too, so its open handle cannot make a writer's rename fail.</summary>
    [Fact]
    public void TheHintCacheIsChangedUnderOneLock()
    {
        var code = CodeOnly(DllSrc("Flamme.cpp"));
        var writes = Regex.Matches(code, @"\bWriteJsonAtomic\s*\(").Count;
        Assert.True(writes == 2, $"Flamme.cpp: {writes} WriteJsonAtomic( occurrence(s); expected the definition and the one call in UpdateCacheRecord");
        Assert.Matches(@"\bWriteJsonAtomic\s*\(", FunctionBody(code, "UpdateCacheRecord("));
        Assert.Matches(@"\bCacheLock\s+\w+\s*\(", FunctionBody(code, "ScanHints LoadHints("));
        Assert.Matches(@"\bCreateMutexW\s*\(", code);
        // The reply says what happened to the write, not what was asked.
        Assert.DoesNotMatch(@"data\[""persisted""\]\s*=\s*persist\s*;", NoComments(DllSrc("Fern.cpp")));
    }

    /// <summary>A connection erases itself from m_conns and only then releases what it owns, so Stop's drain --
    /// which waited for m_conns to empty -- could finish, and run its own resets, while that release still ran. The
    /// drain waits for the teardowns too.</summary>
    [Fact]
    public void StopWaitsForTheTeardownsNotJustTheRegistry()
    {
        var code = CodeOnly(DllSrc("Fern.cpp"));
        Assert.Matches(@"return\s+m_conns\.empty\(\)\s*&&\s*m_tearingDown\s*==\s*0\s*;", code);
        var cleanup = ConnectionCleanup(code);
        int up = cleanup.IndexOf("teardown.emplace(*this)", StringComparison.Ordinal);
        int erase = cleanup.IndexOf("m_conns.erase(", StringComparison.Ordinal);
        int release = cleanup.IndexOf("ReleaseOwnedBy(", StringComparison.Ordinal);
        Assert.True(up >= 0 && erase > up && release > erase,
            "the cleanup must count itself in (TeardownCount) before it erases, and release what it owned after");
        // Review finding (2026-11-10): a bare decrement at the end was skipped by a throw in between, and every later
        // Stop then waited out its full drain. The count goes out in TeardownCount's destructor, on every way out.
        Assert.DoesNotMatch(@"--m_tearingDown", cleanup);
        var guard = FunctionBody(CodeOnly(DllSrc("Fern.h")), "~TeardownCount(");
        Assert.Matches(@"--\s*\w+\.m_tearingDown", guard);
        Assert.Matches(@"\bm_connCv\.notify_all\s*\(", guard);
    }

    /// <summary>A Stop whose drain times out while a teardown is still releasing used to log "0 left" and name no
    /// one. It says how many teardowns it waited for.</summary>
    [Fact]
    public void AStopThatTimesOutSaysWhatItWaitedFor()
        => Assert.Matches(@"\btearingLeft\s*=\s*m_tearingDown\b", CodeOnly(DllSrc("Fern.cpp")));

    /// <summary>pipe_clients read a holder's command name, then its start time, then whether it was in flight: a
    /// command starting in between was reported running under the previous command's name and age. The flag is read
    /// first, as the writer raises it last. And `phase` is a short stable key, not Stop's diagnostic sentence.</summary>
    [Fact]
    public void AHolderIsReadInTheOrderItIsWritten()
    {
        var body = FunctionBody(CodeOnly(DllSrc("Fern.cpp")), "Fern::SnapshotHolders(");
        int flag = body.IndexOf("inFlight.load(", StringComparison.Ordinal);
        int name = body.IndexOf("diagMutex", StringComparison.Ordinal);
        int start = body.IndexOf("cmdStartMs.load(", StringComparison.Ordinal);
        Assert.True(flag >= 0 && name > flag && start > flag, "SnapshotHolders must read inFlight before the name and time");
        // Verification of the fix: the name and its start time were still two steps, so a command just begun could
        // carry the previous command's age. Both are written and read under diagMutex.
        var code = CodeOnly(DllSrc("Fern.cpp"));
        Assert.Matches(@"std::lock_guard<std::mutex>\s+dlk\s*\(\s*conn->diagMutex\s*\)\s*;\s*conn->cmdName\s*=\s*cmd\s*;\s*conn->cmdStartMs\.store\(", code);
        Assert.Matches(@"std::lock_guard<std::mutex>\s+dlk\s*\(\s*c->diagMutex\s*\)\s*;\s*h\.lastCmd\s*=\s*c->cmdName\s*;\s*started\s*=\s*c->cmdStartMs\.load\(", body);
        Assert.Matches(@"\bPhaseKey\s*\(", body);
        Assert.DoesNotMatch(@"\bPhaseName\s*\(", body);
    }

    /// <summary>scan_status and rescan_status read `running` and then the result in separate steps: another client's
    /// start in between reset the result, and the reply said "finished, nothing found". Each reads one snapshot.
    /// rescan's "in progress" reply echoed flags the starter had not written yet.</summary>
    [Fact]
    public void AScanStatusIsOneSnapshot()
    {
        var code = CodeOnly(DllSrc("Fern.cpp"));
        var scan = CommandBranch(code, "CMD_SCAN_STATUS");
        Assert.Matches(@"\bm_scan\.job\.Status\s*\(\s*\)", scan);
        Assert.DoesNotMatch(@"\bm_scan\.job\.(?:Running|Completed)\s*\(", scan);
        var rescan = CommandBranch(code, "CMD_RESCAN_STATUS");
        int locked = rescan.IndexOf("lock(m_rescan.statusMutex)", StringComparison.Ordinal);
        Assert.True(locked >= 0, "rescan_status must take m_rescan.statusMutex");
        int open = rescan.LastIndexOf('{', locked);
        int depth = 0, close = open;
        for (; close < rescan.Length; close++)
        {
            if (rescan[close] == '{') depth++;
            else if (rescan[close] == '}' && --depth == 0) break;
        }
        var inside = rescan[open..(close + 1)];
        var outside = rescan[..open] + rescan[(close + 1)..];
        int lockAt = locked - open;   // the lock statement, within `inside`
        foreach (var read in new[] { "m_rescan.job.Running()", "m_rescan.phase.load()", "m_rescan.result" })
            Assert.True(inside.IndexOf(read, StringComparison.Ordinal) > lockAt,
                $"rescan_status must read {read} under the lock, after it is taken");
        Assert.DoesNotMatch(@"\bm_rescan\.(?:job\.Running|Results)\s*\(", outside);
        Assert.DoesNotMatch(@"\bscanningG(?:Objects|World)\b", code);
        // scan_status: the phase the UI's completion test reads comes after the snapshot. ScanProgress' phase 7 is
        // stored before the job ends, so a phase read after "not running, completed" is 7.
        int snapshot = scan.IndexOf("m_scan.job.Status()", StringComparison.Ordinal);
        int phase = scan.IndexOf("SP::phase.load(", StringComparison.Ordinal);
        Assert.True(snapshot >= 0 && phase > snapshot, "scan_status must read the phase after the job's snapshot");
    }

    /// <summary>A watch whose map insert threw left its thread detached, sampling until the process ended. The slot
    /// goes into the map before the thread starts.</summary>
    [Fact]
    public void AWatchIsInTheMapBeforeItsThreadStarts()
    {
        var start = FunctionBody(CodeOnly(DllSrc("Fern.cpp")), "Fern::StartWatch(");
        int slot = start.IndexOf("try_emplace(", StringComparison.Ordinal);
        int thread = start.IndexOf("std::thread(", StringComparison.Ordinal);
        Assert.True(slot >= 0 && thread > slot, "StartWatch must take its map slot before it starts the thread");
    }

    /// <summary>`persisted` reports the write, so what follows from it must too: Auto's pending state, and the UI's
    /// log line.</summary>
    [Fact]
    public void WhatFollowsFromPersistedFollowsTheWrite()
    {
        Assert.Matches(@"AutoPendsUntilNextLaunch\s*\(\s*plan\.outcome\s*,\s*persisted\s*\)", CodeOnly(DllSrc("Fern.cpp")));
        var ui = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Services/DumpService.cs"));
        Assert.DoesNotContain("persisted={persist}", ui);
    }

    // ── Stage D2-1: who holds each gameplay hold ────────────────────────────────────────────────────────────────────
    // The ledger and its rules are dll_helpers_test's Test_Voll_HoldLedger. Fern.cpp and Mimic.cpp reach no test target,
    // so where they use it is pinned here.

    /// <summary>Every pipe command that changes a hold runs under the hold's lock, and an if_free refusal is decided
    /// under it, before the first command branch: a check made anywhere else would race the other writers.</summary>
    [Fact]
    public void DispatchTakesTheHoldLocks_AndChecksIfFree_BeforeAnyCommandRuns()
    {
        string fern = CodeOnly(DllSrc("Fern.cpp"));
        int dispatch = fern.IndexOf("std::string Fern::DispatchCommand(", StringComparison.Ordinal);
        int firstCommand = fern.IndexOf("if (cmd == Renge::CMD_", dispatch, StringComparison.Ordinal);
        string head = fern[dispatch..firstCommand];
        int changed = head.IndexOf("Voll::HoldsChangedBy(", StringComparison.Ordinal);
        int locks = head.IndexOf("Voll::Holds().LockFor(", StringComparison.Ordinal);
        int ifFree = head.IndexOf("Voll::HeldByOther(", StringComparison.Ordinal);
        int recorder = head.IndexOf("HoldRecorder", StringComparison.Ordinal);
        Assert.True(changed > 0 && locks > changed && ifFree > locks && recorder > ifFree,
            "DispatchCommand must name the holds, lock them, decide if_free, then record the owner -- in that order");
        Assert.Contains("Grimoire::HOLD_HELD_BY_OTHER", head);
    }

    /// <summary>What a background client turned on ends with it; a foreground client's holds outlive it.</summary>
    [Fact]
    public void ABackgroundClientsHolds_AreReleasedWhenItsProcessLeaves()
    {
        string fern = CodeOnly(DllSrc("Fern.cpp"));
        int start = fern.IndexOf("void Fern::ReleaseOwnedBy(", StringComparison.Ordinal);
        string body = fern[start..fern.IndexOf("void Fern::MonitorLoop(", start, StringComparison.Ordinal)];
        int gate = body.IndexOf("IsBackgroundOwner(owner)", StringComparison.Ordinal);
        int owned = body.IndexOf("Voll::Holds().OwnedBy(owner)", StringComparison.Ordinal);
        Assert.True(gate > 0 && owned > gate, "only a background owner's holds are released");
        Assert.Contains("ReleaseHold(hold)", body[owned..]);
        Assert.Contains("Voll::Holds().Owner(hold) != owner", body[owned..]);   // a hold taken over meanwhile stays
    }

    /// <summary>The mailbox is the third writer: every command of it that changes a hold goes through the same lock
    /// and names CE as the owner.</summary>
    [Theory]
    [InlineData("HandleSetDebugCamera", "HOLD_DEBUG_CAMERA")]
    [InlineData("HandleProtect", "HOLD_GOD_MODE")]
    [InlineData("HandleForeground", "HOLD_FOREGROUND_LOCK")]
    [InlineData("HandleMovement", "HOLD_GRAVITY_DIRECTION")]
    [InlineData("HandleMovement", "HOLD_MOVEMENT_PREFIX")]
    [InlineData("HandleTime", "HOLD_TIME_PREFIX")]
    [InlineData("HandleFly", "HOLD_FLY")]
    public void TheMailbox_ChangesEachHoldAsCe(string handler, string hold)
    {
        string mimic = CodeOnly(DllSrc("Mimic.cpp"));
        int start = mimic.IndexOf("static void " + handler + "() {", StringComparison.Ordinal);
        Assert.True(start > 0, handler);
        int end = mimic.IndexOf("\nstatic void ", start + 10, StringComparison.Ordinal);
        string body = mimic[start..(end > 0 ? end : mimic.Length)];
        Assert.Contains("ChangeHoldAsCe", body);
        Assert.Contains("Grimoire::" + hold, body);
    }

    /// <summary>Every pipe command that turns a hold on, off or changes it names that hold: one the ledger missed
    /// would let an if_free request through and never be released.</summary>
    [Fact]
    public void EveryHoldChangingCommand_NamesItsHold()
    {
        string voll = DllSrc("Voll.h");
        int start = voll.IndexOf("inline std::vector<std::string> HoldsChangedBy(", StringComparison.Ordinal);
        string body = voll[start..voll.IndexOf("inline nlohmann::json HoldOwnerJson(", start, StringComparison.Ordinal)];
        foreach (var cmd in new[]
                 {
                     "set_god_mode", "set_foreground_lock", "set_gravity_direction", "reset_gravity_direction",
                     "fly_set", "set_debug_camera", "set_movement_multiplier", "reset_movement", "set_time_dilation",
                     "reset_time_dilation", "force_field", "reset_field", "reset_all_fields",
                 })
            Assert.Contains("\"" + cmd + "\"", body);
        // ...and each of those is a real command (Renge.h), so a rename cannot leave the ledger matching nothing.
        string renge = DllSrc("Renge.h");
        foreach (var cmd in new[] { "set_god_mode", "fly_set", "reset_all_fields", "set_time_dilation" })
            Assert.Contains("= \"" + cmd + "\";", renge);
    }

    // ── Stage D2-3: write_mem data only; invoke owners; the AI's deny list ──────────────────────────────────────────
    // The pure rules are dll_helpers_test's (Lager::ClassifyWriteRegion, Stark::InvokeBlocks,
    // Voll::InvokeRefusedForBackground) and dll_core_test's (Macht::ClassifyWriteRange / WriteDataBytes on real pages).
    // Fern, Stark, Frieren and Mimic reach no test target, so where they use them, and in what order, is pinned here.

    private static string Between(string text, string start, string end)
    {
        int s = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(s >= 0, "missing: " + start);
        int e = text.IndexOf(end, s + start.Length, StringComparison.Ordinal);
        Assert.True(e > s, "missing after it: " + end);
        return text[s..e];
    }

    /// <summary>write_mem classifies the whole range, reads it, checks expect, then writes without a protection flip.</summary>
    [Fact]
    public void WriteMem_ClassifiesTheRange_ChecksExpect_ThenWritesData()
    {
        string body = Between(NoComments(DllSrc("Fern.cpp")), "if (cmd == Renge::CMD_WRITE_MEM) {",
            "return Renge::MakeResponse(id, data).dump();");
        int classify = body.IndexOf("Macht::ClassifyWriteRange(addr, bytes.size())", StringComparison.Ordinal);
        int read = body.IndexOf("Macht::ReadBytesSafe(addr, before.data(), before.size())", StringComparison.Ordinal);
        int expect = body.IndexOf("if (expected != before)", StringComparison.Ordinal);
        int write = body.IndexOf("Macht::WriteDataBytes(addr, bytes.data(), bytes.size())", StringComparison.Ordinal);
        Assert.True(classify > 0 && read > classify && expect > read && write > expect,
            "classify, read, compare with expect, then write -- in that order");
        Assert.DoesNotContain("Macht::WriteBytes(", body);   // the protection-flipping writer
        foreach (var code in new[] { "WRITE_MEM_EXECUTABLE", "WRITE_MEM_NOT_WRITABLE", "WRITE_MEM_CHANGED" })
            Assert.Contains("Grimoire::" + code, body);
        Assert.Contains("data[\"before\"]", body);
    }

    /// <summary>An if_free enqueue is decided under the queue's lock before the push, and each request counts from the
    /// push until it is answered.</summary>
    [Fact]
    public void TheInvokeQueue_DecidesIfFreeUnderItsLock_AndCountsUntilAnswered()
    {
        string stark = CodeOnly(DllSrc("Stark.cpp"));
        string enqueue = Between(stark, "int32_t EnqueueInvoke(", "auto status = future.wait_for(");
        int lockAt = enqueue.IndexOf("std::lock_guard<std::mutex> lock(s_queueMutex);", StringComparison.Ordinal);
        int check = enqueue.IndexOf("if (t_invokeIfFree)", StringComparison.Ordinal);
        int blocks = enqueue.IndexOf("InvokeBlocks(other, count, req->owner)", StringComparison.Ordinal);
        int push = enqueue.IndexOf("s_invokeQueue.push_back(req);", StringComparison.Ordinal);
        int count = enqueue.IndexOf("++s_outstanding[req->owner];", StringComparison.Ordinal);
        Assert.True(lockAt > 0 && check > lockAt && blocks > check && push > blocks && count > push,
            "lock, if_free check, push, count -- in that order, all under the lock");
        Assert.Contains("req->owner = t_invokeOwner;", enqueue);
        string refusal = enqueue[check..push];
        Assert.Contains("t_lastInvokeBlocker = other;", refusal);
        Assert.Contains("return kInvokeHeldByOther;", refusal);

        // Answered = settled: the drain (run or dropped), shutdown, an owner's drop and a waiter's abandon each take it
        // off the count, once.
        string drain = Between(stark, "ShouldDrainQueue(depth, InOwnPeCall(), onGameThread)) {",
            "static void __fastcall HookedProcessEvent(");
        int ran = drain.IndexOf("CallProcessEventSEH(req->instance, req->ufunc, req->params);", StringComparison.Ordinal);
        int settle = drain.IndexOf("SettleLocked(*req);", ran, StringComparison.Ordinal);
        int promise = drain.IndexOf("req->promise.set_value(result);", ran, StringComparison.Ordinal);
        Assert.True(ran > 0 && settle > ran && promise > settle, "settled before the promise wakes its caller");
        Assert.Contains("SettleLocked(*req);",
            Between(drain, "if (req->dropIfAbandoned && !ClaimRequest(req->state, kRequestRunning)) {", "continue;"));
        // One request at a time, each taken under the lock just before it runs: nothing waits outside the queue's reach.
        string loop = Between(drain, "for (; budget > 0; --budget) {", "CallProcessEventSEH(");
        int lockAtPop = loop.IndexOf("std::lock_guard<std::mutex> lock(s_queueMutex);", StringComparison.Ordinal);
        int pop = loop.IndexOf("s_invokeQueue.pop_front();", StringComparison.Ordinal);
        Assert.True(lockAtPop > 0 && pop > lockAtPop, "each request popped under the lock inside the loop");
        Assert.Single(Regex.Matches(drain, @"s_invokeQueue\.front\(\)"));   // no batch filled before the loop
        // ...and only at the top level: a request's own ProcessEvent calls do not drain the rest inside it.
        Assert.Contains("if (!t_draining && ShouldDrainQueue(depth, InOwnPeCall(), onGameThread)) {", stark);
        Assert.Contains("DrainScope() { t_draining = true; }", drain);
        Assert.Contains("SettleLocked(*req);", Between(stark, "void Shutdown() {", "bool IsHookActive()"));
        string drop = Between(stark, "int32_t DropQueuedInvokesOf(uint64_t owner) {", "void SetInvokeTimeoutMs(");
        Assert.Contains("ClaimRequest(req.state, kRequestAbandoned)", drop);
        Assert.Contains("SettleLocked(req);", drop);
        string abandon = Between(enqueue + stark[stark.IndexOf("auto status = future.wait_for(", StringComparison.Ordinal)..],
            "if (status == std::future_status::timeout && dropIfAbandoned) {", "if (status == std::future_status::timeout) {");
        Assert.Contains("SettleLocked(*req);", abandon);
        Assert.Contains("if (req.settled) return;", stark);
    }

    /// <summary>A pending debug-camera ON is its sender's alone: recorded where the toggle is sent, counted as held by
    /// the if_free check, and given by the recorder to its sender only.</summary>
    [Fact]
    public void APendingDebugCameraOn_IsItsSendersAlone()
    {
        string frieren = CodeOnly(DllSrc("Frieren.cpp"));
        string set = Between(frieren, "int32_t UE5_SetDebugCamera(int32_t enable) {", "struct InFlight {");
        int inFlight = set.IndexOf("s_dbgCamInFlight = want ? 1 : 0;", StringComparison.Ordinal);
        int sender = set.IndexOf("s_dbgCamOwner = Stark::CurrentInvokeOwner();", StringComparison.Ordinal);
        Assert.True(inFlight > 0 && sender > inFlight, "the sender recorded where the toggle is sent");
        string pending = Between(frieren, "bool PendingOn(uint64_t& owner) {", "}  ");
        Assert.Contains("owner = on ? s_dbgCamOwner : 0;", pending);

        // A foreground client that joins a pending toggle (told -5, "your toggle is queued") takes it over: in the
        // queue (retagged, so the first sender's session end does not drop it) and for the hold.
        string join = Between(frieren, "static void JoinPendingToggle(int32_t answer, bool queued) {", "\n}");
        Assert.Contains("answer != Stark::kInvokeTimedOutStillQueued || Stark::CurrentInvokeIfFree()", join);
        Assert.Contains("Stark::RetagQueued(s_dbgCamQueuedId, me)", join);
        Assert.Contains("s_dbgCamJoinedBy = me;", join);
        Assert.Contains("if (s_dbgCamJoinedBy != 0) Stark::RetagQueued(queuedId, s_dbgCamJoinedBy);", frieren);
        Assert.Equal(2, Regex.Matches(set, @"JoinPendingToggle\(answer, (true|false)\);").Count);

        string fern = CodeOnly(DllSrc("Fern.cpp"));
        Assert.Contains("return hold == Grimoire::HOLD_DEBUG_CAMERA && FrierenDebugCamera::PendingOn(sender);", fern);
        string head = Between(fern, "std::string Fern::DispatchCommand(", "if (cmd == Renge::CMD_INIT) {");
        Assert.Contains("const bool inForce = HoldActive(h) || HoldPending(h, sender);", head);
        // The recorder's three branches: in force -> the caller (or a joiner of the caller's toggle); on its way -> its
        // sender only; off -> nobody.
        string recorder = Between(head, "struct HoldRecorder {", "} holdRecorder{");
        int active = recorder.IndexOf("if (HoldActive(h)) {", StringComparison.Ordinal);
        int joined = recorder.IndexOf("FrierenDebugCamera::ConsumeJoin(caller, joinedBy)", StringComparison.Ordinal);
        int setActive = recorder.IndexOf("Voll::Holds().SetOwner(h, joined ? joinedBy : caller);", StringComparison.Ordinal);
        int pendingAt = recorder.IndexOf("} else if (HoldPending(h, sender)) {", StringComparison.Ordinal);
        int senderOnly = recorder.IndexOf("if (sender == caller) Voll::Holds().SetOwner(h, caller);", StringComparison.Ordinal);
        int off = recorder.IndexOf("Voll::Holds().SetOwner(h, Voll::kHoldOwnerNone);", StringComparison.Ordinal);
        Assert.True(active > 0 && joined > active && setActive > joined && pendingAt > setActive && senderOnly > pendingAt
                    && off > senderOnly, "active (or its joiner), pending (its sender only), off (nobody) -- in order");
        Assert.DoesNotContain("HoldActive(h) || HoldPending(h) ? caller", head);

        string handler = Between(NoComments(DllSrc("Fern.cpp")), "if (cmd == Renge::CMD_SET_DEBUG_CAMERA) {",
            "return Renge::MakeResponse(id, data).dump();");
        Assert.Contains("state == Stark::kInvokeHeldByOther", handler);
        Assert.Contains("Grimoire::HOLD_HELD_BY_OTHER", handler);
        Assert.Contains("Grimoire::HOLD_INVOKE_QUEUE", handler);
        Assert.Contains("Stark::LastInvokeBlocker()", handler);
        Assert.Contains("state == Stark::kInvokeNeedsGameThread", handler);

        // CE's recorder agrees: its answer that sent nothing never takes another client's pending ON.
        string mimic = CodeOnly(DllSrc("Mimic.cpp"));
        string ce = Between(mimic, "static int32_t ChangeHoldAsCe(", "static void HandleSetDebugCamera()");
        int skip = ce.IndexOf("FrierenDebugCamera::PendingOn(sender) && sender != Voll::kHoldOwnerCe", StringComparison.Ordinal);
        int setCe = ce.IndexOf("Voll::Holds().SetOwner(hold, Voll::kHoldOwnerCe);", StringComparison.Ordinal);
        Assert.True(skip > 0 && setCe > skip, "the pending check before CE is named");
    }

    /// <summary>[DBGCAM-RELEASE-STALLED] An AI's camera release that timed out with the game thread stalled is owed,
    /// and the monitor sends it again once the queued OFF has run -- under the camera's hold lock, so a client taking the
    /// camera meanwhile is seen first.</summary>
    [Fact]
    public void AStalledCameraRelease_IsOwed_AndSentAgainOnceTheQueuedOffHasRun()
    {
        string fern = CodeOnly(DllSrc("Fern.cpp"));
        Assert.Contains("static_assert(Voll::kOwedOffQueued == Stark::kInvokeTimedOutStillQueued", fern);
        string release = Between(fern, "void Fern::ReleaseOwnedBy(", "void Fern::MonitorLoop(");
        int code = release.IndexOf("code = HoldActive(hold) ? ReleaseHold(hold) : 0;", StringComparison.Ordinal);
        int owed = release.IndexOf("hold == Grimoire::HOLD_DEBUG_CAMERA && code == Stark::kInvokeTimedOutStillQueued",
            StringComparison.Ordinal);
        int store = release.IndexOf("s_cameraReleaseOwedBy.store(owner);", StringComparison.Ordinal);
        Assert.True(code > 0 && owed > code && store > owed, "the release's -5 records the owed release");

        string loop = Between(fern, "void Fern::MonitorLoop() {", "static std::string ClientExeName(");
        Assert.Contains("ServeOwedCameraRelease();", loop);

        string serve = Between(fern, "void ServeOwedCameraRelease() {", "\n}");
        int load = serve.IndexOf("s_cameraReleaseOwedBy.load()", StringComparison.Ordinal);
        int lockAt = serve.IndexOf("Voll::Holds().LockFor(Grimoire::HOLD_DEBUG_CAMERA)", StringComparison.Ordinal);
        int decide = serve.IndexOf("Voll::OwedCameraRelease(Voll::Holds().Owner(Grimoire::HOLD_DEBUG_CAMERA)",
            StringComparison.Ordinal);
        int send = serve.IndexOf("UE5_SetDebugCamera(0)", StringComparison.Ordinal);
        int sent = serve.IndexOf("Voll::OwedCameraReleaseSent(", StringComparison.Ordinal);
        int clear = serve.IndexOf("s_cameraReleaseOwedBy.compare_exchange_strong(owedBy, 0)", StringComparison.Ordinal);
        Assert.True(load > 0 && lockAt > load && decide > lockAt && send > decide && sent > send && clear > sent,
            "owed? -> the hold lock -> decide -> send -> what it answered -> cleared, a newer owed release kept");
        Assert.Contains("FrierenDebugCamera::ToggleOutstanding()", serve);
        Assert.Contains("FrierenDebugCamera::PendingOn(sender)", serve);
        Assert.Contains("Stark::GameThreadLiveness::Stalled", serve);

        string frieren = CodeOnly(DllSrc("Frieren.cpp"));
        string outstanding = Between(frieren, "bool ToggleOutstanding() {", "\n}");
        Assert.Contains("return s_dbgCamInFlight >= 0 || s_dbgCamQueued.StillQueued();", outstanding);

        // get_holds says so, beside the camera's entry.
        string holds = Between(NoComments(DllSrc("Fern.cpp")), "if (cmd == Renge::CMD_GET_HOLDS) {", "data[\"holds\"] = arr;");
        Assert.Contains("j[\"release_owed\"] = true;", holds);
    }

    /// <summary>Every command's invokes belong to its client; a departed AI's queued ones go before its holds.</summary>
    [Fact]
    public void Fern_SetsTheInvokeOwner_AndDropsADepartedAisQueue()
    {
        string fern = CodeOnly(DllSrc("Fern.cpp"));
        string head = Between(fern, "std::string Fern::DispatchCommand(", "if (cmd == Renge::CMD_INIT) {");
        Assert.Contains("const Stark::InvokeOwnerScope invokeOwner(conn->ownerKey, request.value(", head);

        string release = Between(fern, "void Fern::ReleaseOwnedBy(", "void Fern::MonitorLoop(");
        int gate = release.IndexOf("if (!IsBackgroundOwner(owner) || OwnerStillConnected(owner)) return;",
            StringComparison.Ordinal);
        int drop = release.IndexOf("Stark::DropQueuedInvokesOf(owner)", StringComparison.Ordinal);
        int holds = release.IndexOf("Voll::Holds().OwnedBy(owner)", StringComparison.Ordinal);
        Assert.True(gate > 0 && drop > gate && holds > drop, "only a background owner's, and before its holds");

        string mimic = CodeOnly(DllSrc("Mimic.cpp"));
        Assert.Contains("const Stark::InvokeOwnerScope ceInvokes(Voll::kHoldOwnerCe, false);", mimic);
    }

    /// <summary>A background client's refusals come before anything is allocated or queued, and the queue's conflict
    /// answers as a hold refusal does.</summary>
    [Fact]
    public void InvokeFunction_RefusesForTheAi_BeforeAllocating_AndNamesTheConflict()
    {
        string body = Between(NoComments(DllSrc("Fern.cpp")), "if (cmd == Renge::CMD_INVOKE_FUNCTION) {",
            "if (cmd == Renge::CMD_");
        int deny = body.IndexOf("Voll::InvokeRefusedForBackground(", StringComparison.Ordinal);
        int blocker = body.IndexOf("Stark::IfFreeBlocker(conn->ownerKey)", StringComparison.Ordinal);
        int convert = body.IndexOf("FrierenInvoke::PrepareObjectParams(", StringComparison.Ordinal);
        int alloc = body.IndexOf("strAllocs", StringComparison.Ordinal);
        int call = body.IndexOf("UE5_CallProcessEventEx(instanceAddr, ufuncAddr, paramPtr", StringComparison.Ordinal);
        Assert.True(deny > 0 && blocker > deny && convert > blocker && alloc > convert && call > alloc,
            "the deny list, then the conflict, before the conversion, the strings and the call");
        Assert.Contains("if (conn->background.load())", body);
        Assert.Contains("if (directCall)", body[..alloc]);
        int held = body.IndexOf("callResult == Stark::kInvokeHeldByOther", StringComparison.Ordinal);
        Assert.True(held > call);
        string mapping = body[held..];
        Assert.Contains("Grimoire::HOLD_HELD_BY_OTHER", mapping);
        Assert.Contains("Grimoire::HOLD_INVOKE_QUEUE", mapping);
        Assert.Contains("Stark::LastInvokeBlocker()", mapping);

        // The definition, not the forward declaration above it: its parameter list ends in ") {".
        string routed = Between(CodeOnly(DllSrc("Frieren.cpp")),
            "bool dropIfAbandoned, std::shared_future<int32_t>* queuedOut) {", "int32_t UE5_CallProcessEventEx(uintptr_t instance");
        int hook = routed.IndexOf("if (Stark::IsHookActive()) {", StringComparison.Ordinal);
        int refuse = routed.IndexOf("if (Stark::CurrentInvokeIfFree()) return Stark::kInvokeNeedsGameThread;",
            StringComparison.Ordinal);
        int direct = routed.IndexOf("pProcessEvent(reinterpret_cast<void*>(instance)", StringComparison.Ordinal);
        Assert.True(hook > 0 && refuse > hook && direct > refuse,
            "the queue first; with the hook down an if_free invoke is refused before the off-thread call");

        string holds = Between(NoComments(DllSrc("Fern.cpp")), "if (cmd == Renge::CMD_GET_HOLDS) {", "if (cmd == Renge::CMD_");
        Assert.Contains("Stark::OutstandingInvokes()", holds);
        Assert.Contains("data[\"invokes\"]", holds);
    }

    // ── Stage D2-2: an AI's teleport leaves the user's undo point ───────────────────────────────────────────────────

    /// <summary>The "last" slot is one for the whole process: an explicit-pose recall sent with keep_undo must reach
    /// Wirbel, and Wirbel must then skip the save, or an AI's jump overwrites where the user's Recall last returns.
    /// Wirbel.cpp and Fern.cpp reach no test target.</summary>
    [Fact]
    public void AnExplicitTeleport_WithKeepUndo_LeavesTheLastSlot()
    {
        string wirbel = CodeOnly(DllSrc("Wirbel.cpp"));
        int start = wirbel.IndexOf("int32_t RecallExplicit(const Pose& pose, bool hasRot, uint8_t* tierOut, bool keepUndo)",
            StringComparison.Ordinal);
        Assert.True(start > 0, "RecallExplicit takes keepUndo");
        string body = wirbel[start..wirbel.IndexOf("\n}", start, StringComparison.Ordinal)];
        Assert.Contains("if (!keepUndo) SaveLastImpl();", body);
        Assert.Single(Regex.Matches(body, @"SaveLastImpl\(\)"));

        string fern = NoComments(DllSrc("Fern.cpp"));   // the key is a string literal, which CodeOnly blanks
        Assert.Contains("Wirbel::RecallExplicit(p, hasRot, &tier, request.value(\"keep_undo\", false))", fern);
    }
}
