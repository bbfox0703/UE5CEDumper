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
        public int Disconnects { get; private set; }
        public bool IsConnected { get; private set; }
        public string? ConnectedTo { get; private set; }
        public HashSet<string> Busy { get; } = new();
        /// <summary>The pid get_pointers reports, per pipe name; default: the name's own pid.</summary>
        public Dictionary<string, int> ReportedPid { get; } = new();
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
            if (cmd == "get_pointers")
            {
                int pid = ReportedPid.TryGetValue(ConnectedTo!, out int r) ? r
                    : int.Parse(ConnectedTo!["UE5DumpBfx_".Length..]);
                return new JsonObject { ["ok"] = true, ["pid"] = pid, ["object_count"] = 100, ["gobjects_method"] = "aob" };
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
        public Rig(params int[] games)
        {
            Platform.Pipes = games.Select(PipeTargets.NameFor).ToList();
            foreach (int g in games) Platform.Processes[g] = ($"Game{g}.exe", $"Title {g}");
            Link = new McpGameLink(Pipe, Platform, new McpServerTests.NullLog());
        }

        public Task<JsonObject> Send(int? pid = null, string cmd = "get_object", int timeoutMs = 5000)
            => SendWith(TestContext.Current.CancellationToken, pid, cmd, timeoutMs);

        public Task<JsonObject> SendWith(CancellationToken ct, int? pid = null, string cmd = "get_object",
            int timeoutMs = 5000)
            => Link.SendAsync(pid, new JsonObject { ["cmd"] = cmd }, TimeSpan.FromMilliseconds(timeoutMs), ct);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        Assert.True(rig.Link.Scanned);
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
    public async Task ConnectedAndNoPid_StaysWithTheConnectedGame_EvenWhenAnotherStarts()
    {
        var rig = new Rig(100);
        await rig.Send();
        rig.Platform.Pipes = [PipeTargets.NameFor(100), PipeTargets.NameFor(200)];
        await rig.Send();
        Assert.Equal(["UE5DumpBfx_100"], rig.Pipe.Dialed);
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
            new McpServerTests.NullLog());
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
        var rig = new Rig(100, 200);
        await rig.Send(pid: 200);
        rig.Pipe.Die();
        // No pid: the game last used, though two run.
        await rig.Send();
        Assert.Equal("UE5DumpBfx_200", rig.Pipe.ConnectedTo);
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
    public void ScannedFrom_NeedsObjectsAndAFoundPool(long count, string? method, bool scanned)
    {
        var reply = new JsonObject { ["object_count"] = count };
        if (method is not null) reply["gobjects_method"] = method;
        Assert.Equal(scanned, McpGameLink.ScannedFrom(reply));
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
