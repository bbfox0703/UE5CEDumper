using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Mcp;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D1. The MCP server's one connection to a game: which game it dials, the pid
/// check after connecting, and what a timeout, a cancel or a dead pipe leave behind. The pipe and the process list
/// are doubles; McpToolsTests covers what is sent over the connection.
/// </summary>
public sealed class McpGameLinkTests
{
    // ── Doubles ─────────────────────────────────────────────────────────────────────────────────────────

    private sealed class Platform : IPlatformService
    {
        public IReadOnlyList<string>? Pipes { get; set; } = Array.Empty<string>();
        public Dictionary<int, (string, string?)> Processes { get; } = new();
        public string LogRoot { get; } = Path.Combine(Path.GetTempPath(), "McpLinkNoLogs");
        public IReadOnlyList<string>? ListNamedPipes() => Pipes;
        public (string ImageName, string? WindowTitle) DescribeProcess(int pid)
            => Processes.TryGetValue(pid, out var p) ? p : ("", null);
        /// <summary>Whether this server runs elevated.</summary>
        public bool ElevatedSelf { get; set; }
        public bool IsElevated() => ElevatedSelf;
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetLogDirectoryPath() => LogRoot;
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName, string filterExtension)
            => Task.FromResult<string?>(null);
    }

    /// <summary>A pipe whose connect and replies follow a script. A reply that is a Task waits for it, so a test can
    /// hold a command "running in the DLL".</summary>
    private sealed class Pipe : IPipeClient
    {
        public List<string> Dialed { get; } = new();
        public List<string> Sent { get; } = new();
        public List<JsonObject> Requests { get; } = new();
        /// <summary>What scan_status answers: a finished scan unless a test says otherwise.</summary>
        public JsonObject ScanStatus { get; set; } = new() { ["running"] = false, ["phase"] = 7 };
        /// <summary>The object count get_pointers reports.</summary>
        public long ObjectCount { get; set; } = 100;
        public int Disconnects { get; private set; }
        public bool IsConnected { get; private set; }
        public string? ConnectedTo { get; private set; }
        public HashSet<string> Busy { get; } = new();
        /// <summary>Names served by an elevated game: Windows refuses a non-elevated client, as NamedPipeClientStream
        /// reports it (measured 2026-11-11, verification-register item 18).</summary>
        public HashSet<string> Elevated { get; } = new();
        /// <summary>The pid get_pointers reports, per pipe name; default: the name's own pid.</summary>
        public Dictionary<string, int> ReportedPid { get; } = new();
        /// <summary>The process start time get_pointers reports, per pipe name; default: <see cref="RunA"/>. A new
        /// value under the same name is the game restarted under the same pid.</summary>
        public Dictionary<string, string> CreationTime { get; } = new();
        public Func<JsonObject, CancellationToken, Task<JsonObject>>? Handler { get; set; }

        public event Action<bool>? ConnectionStateChanged { add { } remove { } }
        public event Action<JsonObject>? EventReceived { add { } remove { } }
        public event Action<PipeLogEntry>? Activity { add { } remove { } }
        public event Action<bool>? GameThreadStalledChanged { add { } remove { } }

        public Task ConnectAsync(string pipeName, CancellationToken ct = default)
        {
            Dialed.Add(pipeName);
            if (IsConnected) return Task.CompletedTask;
            if (Busy.Contains(pipeName)) throw new TimeoutException("The operation has timed out.");
            if (Elevated.Contains(pipeName)) throw new UnauthorizedAccessException("Access to the path is denied.");
            IsConnected = true;
            ConnectedTo = pipeName;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            Disconnects++;
            IsConnected = false;
            return Task.CompletedTask;
        }

        /// <summary>The pipe dies under the client: what PipeClient's read loop ending looks like.</summary>
        public void Die() => IsConnected = false;

        public async Task<JsonObject> SendAsync(JsonObject request, CancellationToken ct = default)
        {
            if (!IsConnected) throw new InvalidOperationException("Not connected to pipe server");
            string cmd = request["cmd"]!.GetValue<string>();
            Sent.Add(cmd);
            Requests.Add((JsonObject)request.DeepClone());
            if (cmd == "get_pointers")
            {
                int pid = ReportedPid.TryGetValue(ConnectedTo!, out int r) ? r
                    : int.Parse(ConnectedTo!["UE5DumpBfx_".Length..]);
                return new JsonObject
                {
                    ["ok"] = true, ["pid"] = pid, ["object_count"] = ObjectCount,
                    ["gobjects_method"] = ObjectCount > 0 ? "aob" : "not_found",
                    ["process_creation_time"] = CreationTime.TryGetValue(ConnectedTo!, out var run) ? run : RunA,
                };
            }
            if (cmd == "scan_status")
            {
                var s = (JsonObject)ScanStatus.DeepClone();
                s["ok"] = true;
                return s;
            }
            if (Handler is not null) return await Handler(request, ct);
            return new JsonObject { ["ok"] = true, ["cmd_echo"] = cmd };
        }

        public void Dispose() { }
    }

    private sealed class Rig
    {
        public Platform Platform { get; } = new();
        public Pipe Pipe { get; } = new();
        public McpGameLink Link { get; }
        public McpGrants Grants { get; }
        public Rig(params int[] games) : this(Granting(), games) { }
        public Rig(McpGrants grants, params int[] games)
        {
            Grants = grants;
            Platform.Pipes = games.Select(PipeTargets.NameFor).ToList();
            foreach (int g in games) Platform.Processes[g] = ($"Game{g}.exe", $"Title {g}");
            Link = new McpGameLink(Pipe, Platform, new McpServerTests.NullLog(), grants);
        }

        public Task<JsonObject> Send(int? pid = null, string cmd = "get_object", int timeoutMs = 5000)
            => SendWith(TestContext.Current.CancellationToken, pid, cmd, timeoutMs);

        public Task<JsonObject> SendWith(CancellationToken ct, int? pid = null, string cmd = "get_object",
            int timeoutMs = 5000, bool requireScan = false)
            => Link.SendAsync(pid, new JsonObject { ["cmd"] = cmd }, TimeSpan.FromMilliseconds(timeoutMs), ct,
                requireScan);

        public Task<JsonObject> SendScanned(int? pid = null, string cmd = "get_object")
            => SendWith(TestContext.Current.CancellationToken, pid, cmd, requireScan: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string RunA = "1DB5F0A2C3E4D50";
    private const string RunB = "1DB5F0A2C3E4D99";

    /// <summary>A session whose host takes grants, as a recent Claude Code's does.</summary>
    private static McpGrants Granting()
    {
        var g = new McpGrants();
        g.Decide(Constants.McpClaudeCodeClientName, "2.1.246");
        return g;
    }

    // ── The write gate ([MCP-WRITE-GRANTS-2026-11-14]) ──────────────────────────────────────────────────

    /// <summary>A capability admitted for a game run, as a write tool gets one from the get_pointers reply it fetched
    /// before its send.</summary>
    private static McpGrantCapability Admitted(McpGrants grants, int pid, string run)
    {
        var ptrs = new JsonObject { ["ok"] = true, ["pid"] = pid, ["process_creation_time"] = run };
        Assert.NotNull(grants.Grant(McpGrantKind.Toggles, ptrs, out _));
        return grants.Admit(McpToolRole.GrantedWrite, McpGrantKind.Toggles, ptrs, out _)!;
    }

    private static Task<JsonObject> Write(Rig rig, JsonObject request, McpGrantCapability? cap)
        => rig.Link.SendAsync(null, request, TimeSpan.FromSeconds(5), Ct, grant: cap);

    private static JsonObject GodOn() => new() { ["cmd"] = "set_god_mode", ["enable"] = true, ["if_free"] = true };

    [Fact]
    public async Task AWriteAdmittedForTheConnectedRun_IsSent()
    {
        var rig = new Rig(4242);
        await Write(rig, GodOn(), Admitted(rig.Grants, 4242, RunA));
        Assert.Equal(["get_pointers", "set_god_mode"], rig.Pipe.Sent);
    }

    [Fact]
    public async Task AWriteNobodyAdmitted_IsRefusedAtTheSend_AndNothingIsSent()
    {
        // The choke point: a tool that forgets to ask for admission, or a tool added later, fails closed here.
        var rig = new Rig(4242);
        var e = await Assert.ThrowsAsync<McpToolError>(() => Write(rig, GodOn(), null));
        Assert.Contains("set_god_mode", e.Message);
        Assert.DoesNotContain("set_god_mode", rig.Pipe.Sent);
    }

    [Fact]
    public async Task AReleaseAndAReadNeedNoAdmission()
    {
        var rig = new Rig(4242);
        await Write(rig, new JsonObject { ["cmd"] = "set_god_mode", ["enable"] = false, ["if_free"] = true }, null);
        await rig.Send(cmd: "get_holds");
        Assert.Equal(["get_pointers", "set_god_mode", "get_holds"], rig.Pipe.Sent);
    }

    [Fact]
    public async Task AWriteAdmittedForOneRun_NeverReachesTheNextRunOfTheSamePid()
    {
        // The game restarted under the same pid after the tool's get_pointers; the link reconnects to whatever
        // answers, so the run it reached is checked again under the lock.
        var rig = new Rig(4242);
        await rig.Send();
        var cap = Admitted(rig.Grants, 4242, RunA);
        rig.Pipe.Die();
        rig.Pipe.CreationTime["UE5DumpBfx_4242"] = RunB;
        var e = await Assert.ThrowsAsync<McpToolError>(() => Write(rig, GodOn(), cap));
        Assert.Contains("another game run", e.Message);
        Assert.DoesNotContain("set_god_mode", rig.Pipe.Sent);
    }

    [Fact]
    public async Task AWriteAdmittedForOneGame_NeverReachesAnother()
    {
        var rig = new Rig(100);
        var cap = Admitted(rig.Grants, 4242, RunA);   // a game that has gone; game 100 is the only one now
        await Assert.ThrowsAsync<McpToolError>(() => Write(rig, GodOn(), cap));
        Assert.DoesNotContain("set_god_mode", rig.Pipe.Sent);
    }

    [Fact]
    public async Task ARevokeThatLandsBeforeTheSend_Wins()
    {
        var rig = new Rig(4242);
        var cap = Admitted(rig.Grants, 4242, RunA);
        rig.Grants.Revoke();
        var e = await Assert.ThrowsAsync<McpToolError>(() => Write(rig, GodOn(), cap));
        Assert.Contains("revoked", e.Message);
        Assert.DoesNotContain("set_god_mode", rig.Pipe.Sent);
    }

    [Fact]
    public async Task ADroppedConnectionToTheSameRun_KeepsTheAdmission()
    {
        // The DLL drops the holds with the connection; the grant stays, and the AI may take them again.
        var rig = new Rig(4242);
        await rig.Send();
        var cap = Admitted(rig.Grants, 4242, RunA);
        rig.Pipe.Die();
        await Write(rig, GodOn(), cap);
        Assert.Equal(["get_pointers", "get_object", "get_pointers", "set_god_mode"], rig.Pipe.Sent);
    }

    [Fact]
    public async Task OnAReadOnlyHost_OnlyReadsAreSent()
    {
        var rig = new Rig(new McpGrants(), 4242);
        await rig.Send(cmd: "get_holds");
        await Assert.ThrowsAsync<McpToolError>(() =>
            Write(rig, new JsonObject { ["cmd"] = "reset_movement", ["knob"] = "jump", ["if_free"] = true }, null));
        Assert.Equal(["get_pointers", "get_holds"], rig.Pipe.Sent);
    }

    // ── Choosing the game ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OneGame_IsDialledByItsOwnPipeName_AndConfirmedByPid()
    {
        var rig = new Rig(4242);
        var reply = await rig.Send();
        Assert.Equal(["UE5DumpBfx_4242"], rig.Pipe.Dialed);
        Assert.Equal(["get_pointers", "get_object"], rig.Pipe.Sent);
        Assert.Equal("get_object", reply["cmd_echo"]!.GetValue<string>());
        Assert.Equal(4242, rig.Link.ConnectedPid);
    }

    [Fact]
    public async Task EveryRequest_IsMarkedBackground_SoItsWalksLeaveTheUiHalfTheWorkers()
    {
        // Aura::BackgroundWalkScope: a GObjects walk a background request starts takes at most half the free workers.
        var rig = new Rig(4242);
        await rig.SendScanned();
        Assert.NotEmpty(rig.Pipe.Requests);
        Assert.All(rig.Pipe.Requests, r => Assert.True(r["background"]!.GetValue<bool>(), r["cmd"]!.ToString()));
    }

    [Fact]
    public async Task NoGame_SaysHowToStartOne()
    {
        var e = await Assert.ThrowsAsync<McpToolError>(() => new Rig().Send());
        Assert.Contains("No game is running", e.Message);
    }

    [Fact]
    public async Task OnlyTheLegacyPipe_IsNeverDialled_AndSaysToUpdateTheProxy()
    {
        var rig = new Rig();
        rig.Platform.Pipes = [Constants.LegacyPipeName];
        var e = await Assert.ThrowsAsync<McpToolError>(() => rig.Send());
        Assert.Contains("Update All", e.Message);
        Assert.Empty(rig.Pipe.Dialed);
    }

    [Fact]
    public async Task OneGameBesideTheLegacyPipe_IsTheAnswer()
    {
        // The UI shows a list here, to show the old DLL too; with one game that can be dialled the server needs none.
        var rig = new Rig(4242);
        rig.Platform.Pipes = [PipeTargets.NameFor(4242), Constants.LegacyPipeName];
        await rig.Send();
        Assert.Equal(["UE5DumpBfx_4242"], rig.Pipe.Dialed);
    }

    [Fact]
    public async Task SeveralGames_AndNoPid_IsAnErrorListingThem()
    {
        var rig = new Rig(100, 200);
        var e = await Assert.ThrowsAsync<McpToolError>(() => rig.Send());
        Assert.Contains("pass pid", e.Message);
        Assert.Contains("100 Game100.exe \"Title 100\"", e.Message);
        Assert.Contains("200 Game200.exe", e.Message);
        Assert.Empty(rig.Pipe.Dialed);
    }

    [Fact]
    public async Task APidThatIsNotListed_IsRefusedWithTheList()
    {
        var rig = new Rig(100);
        var e = await Assert.ThrowsAsync<McpToolError>(() => rig.Send(pid: 999));
        Assert.Contains("999", e.Message);
        Assert.Contains("100 Game100.exe", e.Message);
        Assert.Empty(rig.Pipe.Dialed);
    }

    [Fact]
    public async Task SeveralGames_AndNoPid_IsAnError_EvenWhileConnected()
    {
        // An address belongs to one game: a call without a pid would follow whichever game another call had just
        // switched to, and read the address in the wrong process.
        var rig = new Rig(100);
        await rig.Send();
        rig.Platform.Pipes = [PipeTargets.NameFor(100), PipeTargets.NameFor(200)];
        var e = await Assert.ThrowsAsync<McpToolError>(() => rig.Send());
        Assert.Contains("pass pid", e.Message);
        await rig.Send(pid: 100);
        Assert.Equal(["UE5DumpBfx_100"], rig.Pipe.Dialed);
    }

    [Fact]
    public async Task AGameThatCameBackAsANewProcess_IsNamedInANotice()
    {
        // Game 100 exits and starts again as 300: addresses from 100 mean nothing in 300, and the reply must say so.
        var rig = new Rig(100);
        await rig.Send();
        rig.Pipe.Die();
        rig.Platform.Pipes = [PipeTargets.NameFor(300)];
        rig.Platform.Processes[300] = ("Game100.exe", "Title");
        await rig.Send();
        Assert.Equal("UE5DumpBfx_300", rig.Pipe.ConnectedTo);
        string? notice = rig.Link.TakeNotice();
        Assert.Contains("Game 100 is gone", notice);
        Assert.Contains("addresses from the earlier game do not apply", notice);
    }

    // ── The scan check ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheScanCheck_ReadsScanStatusAndPointersOnTheConnection_AndKeepsReady()
    {
        var rig = new Rig(100);
        await rig.SendScanned();
        await rig.SendScanned();
        Assert.Equal(1, rig.Pipe.Sent.Count(c => c == "scan_status"));
        Assert.Equal(2, rig.Pipe.Sent.Count(c => c == "get_object"));
    }

    [Fact]
    public async Task ARunningScan_IsRefused_EvenWithObjectsPublished_ThenServedOnceItEnds()
    {
        // The pool is published (object_count > 0) before the scan validates its offsets.
        var rig = new Rig(100);
        rig.Pipe.ScanStatus = new JsonObject { ["running"] = true, ["phase"] = 6, ["status_text"] = "Validating" };
        var e = await Assert.ThrowsAsync<McpToolError>(() => rig.SendScanned());
        Assert.Contains("still running (phase 6 of 7: Validating)", e.Message);
        Assert.DoesNotContain("get_object", rig.Pipe.Sent);

        rig.Pipe.ScanStatus = new JsonObject { ["running"] = false, ["phase"] = 7 };
        await rig.SendScanned();
        Assert.Contains("get_object", rig.Pipe.Sent);
    }

    [Fact]
    public async Task AScanThatFoundNothing_IsNamedSo_NotAsUnscanned()
    {
        var rig = new Rig(100);
        rig.Pipe.ObjectCount = 0;
        var e = await Assert.ThrowsAsync<McpToolError>(() => rig.SendScanned());
        Assert.Contains("without finding the object pool", e.Message);
        var report = await rig.Link.ScanStateAsync(null, Ct);
        Assert.Equal(McpScanState.FoundNothing, report.State);
    }

    [Fact]
    public async Task StartScan_SendsTriggerScanOnlyWhenNoScanHasRun()
    {
        var rig = new Rig(100);
        var (before, started) = await rig.Link.StartScanAsync(null, Ct);
        Assert.Equal(McpScanState.Ready, before.State);
        Assert.Null(started);
        Assert.DoesNotContain("trigger_scan", rig.Pipe.Sent);

        rig.Pipe.ScanStatus = new JsonObject { ["running"] = false, ["phase"] = 0 };
        rig.Pipe.ObjectCount = 0;
        (before, started) = await rig.Link.StartScanAsync(null, Ct);
        Assert.Equal(McpScanState.NotStarted, before.State);
        Assert.NotNull(started);
        Assert.Contains("trigger_scan", rig.Pipe.Sent);
    }

    [Fact]
    public async Task ADifferentPid_SwitchesGames_AndSaysTheOldSessionsAreGone()
    {
        var rig = new Rig(100, 200);
        await rig.Send(pid: 100);
        Assert.Null(rig.Link.TakeNotice());
        await rig.Send(pid: 200);
        Assert.Equal(1, rig.Pipe.Disconnects);
        Assert.Equal("UE5DumpBfx_200", rig.Pipe.ConnectedTo);
        Assert.Equal(200, rig.Link.ConnectedPid);
        string? notice = rig.Link.TakeNotice();
        Assert.Contains("Switched from game 100 to game 200", notice);
        Assert.Null(rig.Link.TakeNotice());
    }

    [Fact]
    public async Task AnotherProcessAnsweringTheName_IsRefused_AndDisconnected()
    {
        var rig = new Rig(100);
        rig.Pipe.ReportedPid["UE5DumpBfx_100"] = 555;
        var e = await Assert.ThrowsAsync<McpToolError>(() => rig.Send());
        Assert.Contains("process 555", e.Message);
        Assert.False(rig.Pipe.IsConnected);
        Assert.Equal(0, rig.Link.ConnectedPid);
    }

    [Fact]
    public async Task AnElevatedGame_IsNamedAsSuch_NotAnInternalError()
    {
        // Measured 2026-11-11: a non-elevated server against a game running as administrator got "Internal error in
        // read_status: Access to the path is denied." -- UAC refuses the pipe, and the model needs to know why.
        var rig = new Rig(100);
        rig.Pipe.Elevated.Add("UE5DumpBfx_100");
        var e = await Assert.ThrowsAsync<McpToolError>(() => rig.Send());
        Assert.Contains("administrator", e.Message);
        Assert.Contains("elevated", e.Message);
        // The mirror of the elevated server's case: each fails if the server reaches the other's branch.
        Assert.Contains("this server does not", e.Message);
        Assert.DoesNotContain("another process", e.Message);
        Assert.False(rig.Pipe.IsConnected);
    }

    [Fact]
    public async Task AnElevatedServerRefused_DoesNotSayTheServerIsNotElevated()
    {
        // [UI-ELEVATED-ACCESS-DENIED] The DLL's pipe always lets an administrator in, so a refusal to an elevated server
        // means something else set that pipe's permissions. The text used to say "this server does not" run elevated
        // without asking.
        var rig = new Rig(100);
        rig.Platform.ElevatedSelf = true;
        rig.Pipe.Elevated.Add("UE5DumpBfx_100");
        var e = await Assert.ThrowsAsync<McpToolError>(() => rig.Send());
        Assert.DoesNotContain("this server does not", e.Message);
        Assert.Contains("another process", e.Message);
        Assert.False(rig.Pipe.IsConnected);
    }

    [Fact]
    public async Task AFullPipe_SaysEveryConnectionIsTaken()
    {
        var rig = new Rig(100);
        rig.Pipe.Busy.Add("UE5DumpBfx_100");
        var e = await Assert.ThrowsAsync<McpToolError>(() => rig.Send());
        Assert.Contains("Every connection", e.Message);
    }

    [Fact]
    public async Task APipeGoneWhileDialling_SaysItIsGone()
    {
        var rig = new Rig(100);
        rig.Pipe.Busy.Add("UE5DumpBfx_100");
        int calls = 0;
        // Listed for the resolve, gone by the re-list after the timeout.
        var link = new McpGameLink(rig.Pipe, new GoneAfterFirstList(rig.Platform, () => ++calls),
            new McpServerTests.NullLog(), rig.Grants);
        var e = await Assert.ThrowsAsync<McpToolError>(() =>
            link.SendAsync(null, new JsonObject { ["cmd"] = "get_object" }, TimeSpan.FromSeconds(5), Ct));
        Assert.Contains("gone", e.Message);
    }

    private sealed class GoneAfterFirstList(Platform inner, Func<int> count) : IPlatformService
    {
        public IReadOnlyList<string>? ListNamedPipes() => count() == 1 ? inner.Pipes : Array.Empty<string>();
        public (string ImageName, string? WindowTitle) DescribeProcess(int pid) => inner.DescribeProcess(pid);
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => inner.GetAppDataPath();
        public string GetLogDirectoryPath() => inner.GetLogDirectoryPath();
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName, string filterExtension)
            => Task.FromResult<string?>(null);
    }

    // ── Timeouts, cancels and dead pipes ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACommandThatOverrunsItsTime_ClosesTheConnection_WhichIsTheDllsCancel()
    {
        var rig = new Rig(100);
        rig.Pipe.Handler = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new JsonObject(); };
        var e = await Assert.ThrowsAsync<McpToolError>(() => rig.Send(timeoutMs: 50));
        Assert.Contains("did not answer within", e.Message);
        Assert.False(rig.Pipe.IsConnected);
        Assert.Equal(1, rig.Pipe.Disconnects);

        // The next call reconnects to the same game and owes the model a word about the sessions.
        rig.Pipe.Handler = null;
        await rig.Send();
        Assert.Equal(["UE5DumpBfx_100", "UE5DumpBfx_100"], rig.Pipe.Dialed);
        Assert.Contains("sessions begun on it are gone", rig.Link.TakeNotice());
    }

    [Fact]
    public async Task AClientCancel_ClosesTheConnection_AndStaysACancel()
    {
        var rig = new Rig(100);
        var started = new TaskCompletionSource();
        rig.Pipe.Handler = async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new JsonObject();
        };
        using var cts = new CancellationTokenSource();
        var call = rig.SendWith(cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.False(rig.Pipe.IsConnected);
    }

    [Fact]
    public async Task APipeThatDiedBetweenCalls_IsReopenedToTheSameGame_WithANotice()
    {
        var rig = new Rig(100);
        await rig.Send();
        rig.Pipe.Die();
        await rig.Send();
        Assert.Equal(["UE5DumpBfx_100", "UE5DumpBfx_100"], rig.Pipe.Dialed);
        Assert.Contains("had closed", rig.Link.TakeNotice());
    }

    [Fact]
    public async Task APipeThatDiesDuringACommand_IsReportedAndNotRetried()
    {
        var rig = new Rig(100);
        rig.Pipe.Handler = (_, _) => throw new IOException("Pipe broken.");
        var e = await Assert.ThrowsAsync<McpToolError>(() => rig.Send());
        Assert.Contains("pipe closed during get_object", e.Message);
        Assert.Equal(1, rig.Pipe.Sent.Count(c => c == "get_object"));
    }

    [Fact]
    public async Task CallsGoOneAtATime_OverTheOneConnection()
    {
        var rig = new Rig(100);
        int running = 0, most = 0;
        rig.Pipe.Handler = async (_, _) =>
        {
            int now = Interlocked.Increment(ref running);
            most = Math.Max(most, now);
            await Task.Delay(20, Ct);
            Interlocked.Decrement(ref running);
            return new JsonObject { ["ok"] = true };
        };
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => rig.Send()));
        Assert.Equal(1, most);
        Assert.Single(rig.Pipe.Dialed);
    }

    // ── Scanned ─────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(24530, "aob", true)]
    [InlineData(0, "aob", false)]
    [InlineData(24530, "not_found", false)]
    [InlineData(0, null, false)]
    public void HasObjects_NeedsObjectsAndAFoundPool(long count, string? method, bool has)
    {
        var reply = new JsonObject { ["object_count"] = count };
        if (method is not null) reply["gobjects_method"] = method;
        Assert.Equal(has, McpGameLink.HasObjects(reply));
    }

    [Theory]
    [InlineData(true, 6, 24530, "Running")]   // objects published, offsets not yet validated
    [InlineData(true, 7, 24530, "Running")]   // phase 7 is stored just before the job ends
    [InlineData(false, 7, 24530, "Ready")]    // proxy mode after its scan, and injected mode
    [InlineData(false, 7, 0, "FoundNothing")]
    [InlineData(false, 0, 0, "NotStarted")]   // a proxy-loaded game before any scan
    [InlineData(false, 5, 24530, "NotStarted")]   // Stop cancelled it part-way
    public void StateFrom_ScanStatusAndPointersTogether(bool running, int phase, long count, string state)
    {
        var status = new JsonObject { ["running"] = running, ["phase"] = phase };
        var pointers = new JsonObject { ["object_count"] = count, ["gobjects_method"] = count > 0 ? "aob" : "not_found" };
        Assert.Equal(Enum.Parse<McpScanState>(state), McpGameLink.StateFrom(status, pointers));
    }

    [Fact]
    public void TheGameList_MarksTheConnectedGame()
    {
        var rig = new Rig(100, 200);
        var (games, legacy) = rig.Link.ListGames();
        Assert.False(legacy);
        Assert.Equal([100, 200], games.Select(g => g.Pid).ToArray());
        Assert.All(games, g => Assert.False(g.Connected));
        Assert.Equal("Game100.exe", games[0].Exe);
    }
}
