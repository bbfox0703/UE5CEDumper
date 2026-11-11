using System.Globalization;
using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Services;

namespace UE5DumpUI.Mcp;

/// <summary>A game serving the dumper's pipe, as the game list shows it.</summary>
internal sealed record McpGameInfo(int Pid, string Exe, string? WindowTitle, bool Connected);

/// <summary>Where the connected game's object scan stands.</summary>
internal enum McpScanState
{
    /// <summary>No scan has run (a proxy-loaded game waits for one), or Stop cancelled it.</summary>
    NotStarted,
    /// <summary>The scan is running; until it ends the offsets may not be validated and the object array may still be
    /// replaced by a recovered one.</summary>
    Running,
    /// <summary>The scan is done and found the object pool.</summary>
    Ready,
    /// <summary>The scan is done and found no object pool: scanning again finds nothing more.</summary>
    FoundNothing,
}

/// <summary>The connected game's scan state, with the two replies it was read from (envelope stripped).</summary>
internal sealed record McpScanReport(McpScanState State, JsonObject Pointers, JsonObject ScanStatus);

/// <summary>The MCP tools' view of the game: what is running, and one request at a time to the chosen game.</summary>
internal interface IMcpGame
{
    /// <summary>The games serving the dumper's pipe, and whether an old DLL's shared pipe is up too.</summary>
    (IReadOnlyList<McpGameInfo> Games, bool LegacyPresent) ListGames();

    /// <summary>The pid of the game this server is connected to, or 0.</summary>
    int ConnectedPid { get; }

    /// <summary>Sends <paramref name="request"/> to the game <paramref name="pid"/> names, or to the only one running;
    /// connects first when needed. With <paramref name="requireScan"/>, refuses (<see cref="McpToolError"/>) unless that
    /// game's scan is done and found objects, checked on the same connection under the same lock as the send. Throws
    /// <see cref="McpToolError"/> when there is no game to send to, the pipe is full or gone, or no reply came in time.
    /// </summary>
    Task<JsonObject> SendAsync(int? pid, JsonObject request, TimeSpan timeout, CancellationToken ct,
        bool requireScan = false);

    /// <summary>The game's scan state, read now.</summary>
    Task<McpScanReport> ScanStateAsync(int? pid, CancellationToken ct);

    /// <summary>Starts the game's scan only when none has run (<see cref="McpScanState.NotStarted"/>): the state is read
    /// and the scan started under one lock. Returns the state found, and trigger_scan's reply when it was sent.</summary>
    Task<(McpScanReport Before, JsonObject? Started)> StartScanAsync(int? pid, CancellationToken ct);

    /// <summary>A note owed to the model about the connection (the game changed, the connection was replaced and
    /// sessions on it are gone), returned once; null when there is none.</summary>
    string? TakeNotice();
}

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D. The MCP server's one connection to a game's pipe.
///
/// <para><b>One connection, one request at a time.</b> The DLL serves each connection on a thread of its own, one
/// command after another, so a second connection would only buy a second queue, at the cost of a second of the six
/// slots. The UI keeps its own two; nothing this server sends waits behind the UI, or the UI behind it. Value-scan
/// sessions belong to the connection that began them (Stage C), so they live as long as this connection does. Every
/// request is marked <c>"background"</c>: a GObjects walk it starts takes at most half of the DLL's free workers
/// (Aura::BackgroundWalkScope), so the user's own scan or snapshot in the UI is never left on one thread.</para>
///
/// <para><b>Choosing the game, every call, under the lock:</b> the game a call names (which must be listed), else the
/// only one running. Several games and no pid is an error that lists them, even while connected: an address belongs to
/// one game, and a call that followed whichever game another call had just switched to would read it in the wrong one.
/// An old DLL's shared pipe is never dialled. The pid the DLL reports is checked, as the UI checks it.</para>
///
/// <para><b>Scanned</b> means the scan finished and found the object pool, read from scan_status (not running, phase 7)
/// and get_pointers together. The pool is published before the scan validates its offsets and may still be replaced by
/// a recovered one, so a pool alone is not yet a scanned game. Only Ready is kept for the connection; every other state
/// is read again.</para>
///
/// <para><b>Cancelling.</b> The DLL has no cancel command: it stops a connection's command when the connection
/// breaks (checked every 200 ms). So a call that overruns its time, or that the client cancels, closes the
/// connection, and the next call opens a new one. Its scan sessions go with it, and the next reply says so.</para>
/// </summary>
internal sealed class McpGameLink : IMcpGame, IDisposable
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    private readonly IPipeClient _pipe;
    private readonly IPlatformService _platform;
    private readonly ILoggingService _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private int _pid;          // the connected game, 0 when none
    private int _lastPid;      // the game last connected, kept across a dropped connection
    private bool _ready;       // the connected game's scan is done and found objects
    private string? _notice;   // owed to the model with the next reply

    public McpGameLink(IPipeClient pipe, IPlatformService platform, ILoggingService log)
    {
        _pipe = pipe;
        _platform = platform;
        _log = log;
    }

    public int ConnectedPid => _pipe.IsConnected ? _pid : 0;

    public string? TakeNotice() => Interlocked.Exchange(ref _notice, null);

    public (IReadOnlyList<McpGameInfo> Games, bool LegacyPresent) ListGames()
    {
        var scan = Listing();
        var games = new List<McpGameInfo>(scan.Pids.Count);
        foreach (int pid in scan.Pids)
        {
            var (image, title) = _platform.DescribeProcess(pid);
            games.Add(new McpGameInfo(pid, image, title, pid == ConnectedPid));
        }
        return (games, scan.LegacyPresent);
    }

    public async Task<JsonObject> SendAsync(int? pid, JsonObject request, TimeSpan timeout, CancellationToken ct,
        bool requireScan = false)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(pid, ct);
            if (requireScan && !_ready)
            {
                var report = await ProbeAsync(ct);
                if (report.State != McpScanState.Ready) throw new McpToolError(Explain(report));
            }
            return await SendCoreAsync(request, timeout, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<McpScanReport> ScanStateAsync(int? pid, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(pid, ct);
            return await ProbeAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(McpScanReport Before, JsonObject? Started)> StartScanAsync(int? pid, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(pid, ct);
            var before = await ProbeAsync(ct);
            if (before.State != McpScanState.NotStarted) return (before, null);
            var reply = await SendCoreAsync(new JsonObject { ["cmd"] = "trigger_scan" }, ProbeTimeout, ct);
            return (before, reply);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>What the model should do about a game that is not <see cref="McpScanState.Ready"/>.</summary>
    internal static string Explain(McpScanReport report) => report.State switch
    {
        McpScanState.Running =>
            "The game's object scan is still running" + PhaseText(report.ScanStatus)
            + ": call read_status until scanned is true, then call again.",
        McpScanState.FoundNothing =>
            "The game's scan finished without finding the object pool (GObjects), so object tools have nothing to "
            + "read, and scanning again finds nothing more. The UE5DumpUI window's Extra Scan searches further.",
        McpScanState.NotStarted =>
            "The game is not scanned yet, and object tools would answer with nothing. Once the game world is loaded, "
            + "call scan_start, then read_status until scanned is true.",
        _ => "The game is scanned.",
    };

    private static string PhaseText(JsonObject status)
    {
        string? text = McpJson.GetString(status, "status_text");
        return McpJson.TryGetLong(status["phase"], out long phase)
            ? string.IsNullOrEmpty(text) ? $" (phase {phase} of 7)" : $" (phase {phase} of 7: {text})"
            : "";
    }

    /// <summary>scan_status and get_pointers, read on the connection under the lock. Ready is kept for the connection.
    /// </summary>
    private async Task<McpScanReport> ProbeAsync(CancellationToken ct)
    {
        var status = await SendCoreAsync(new JsonObject { ["cmd"] = "scan_status" }, ProbeTimeout, ct);
        var pointers = await SendCoreAsync(new JsonObject { ["cmd"] = "get_pointers" }, ProbeTimeout, ct);
        McpTools.Strip(status);
        McpTools.Strip(pointers);
        var state = StateFrom(status, pointers);
        _ready = state == McpScanState.Ready;
        return new McpScanReport(state, pointers, status);
    }

    /// <summary>The scan's state from scan_status and get_pointers. A running scan is Running whatever the pointers
    /// say: they are published before the offsets are validated. A finished one (phase 7, in proxy and injected mode
    /// alike) is Ready when it found objects. Anything else never ran, or Stop cancelled it.</summary>
    internal static McpScanState StateFrom(JsonObject scanStatus, JsonObject pointers)
    {
        if (scanStatus["running"] is JsonValue r && r.TryGetValue<bool>(out bool running) && running)
            return McpScanState.Running;
        bool finished = McpJson.TryGetLong(scanStatus["phase"], out long phase) && phase >= 7;
        if (!finished) return McpScanState.NotStarted;
        return HasObjects(pointers) ? McpScanState.Ready : McpScanState.FoundNothing;
    }

    /// <summary>The object pool is found and holds objects. A proxy-loaded game's pipe comes up before its scan, and
    /// every object command then answers with zeros.</summary>
    internal static bool HasObjects(JsonObject pointers)
    {
        long count = McpJson.TryGetLong(pointers["object_count"], out long n) ? n : 0;
        return count > 0 && McpJson.GetString(pointers, "gobjects_method") != "not_found";
    }

    /// <summary>One request on the connection, marked background, with a time limit. Overrun and client cancel both
    /// close the connection, which is the DLL's cancel.</summary>
    private async Task<JsonObject> SendCoreAsync(JsonObject request, TimeSpan timeout, CancellationToken ct)
    {
        string cmd = McpJson.GetString(request, "cmd") ?? "?";
        request["background"] = true;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await _pipe.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException)
        {
            bool byClient = ct.IsCancellationRequested;
            string secs = timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
            await DropAsync(byClient ? $"{cmd} was cancelled by the client" : $"{cmd} did not answer within {secs} s");
            if (byClient) throw;
            throw new McpToolError($"{cmd} did not answer within {secs} s. The connection was closed, which makes the "
                                   + "DLL stop it; the next call reconnects. A game that is paused or loading answers "
                                   + "slowly.");
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The pipe died under the request: the game exited, or its DLL stopped. Never retried, since the command
            // may have run.
            await DropAsync($"the pipe closed during {cmd}");
            throw new McpToolError($"The game's pipe closed during {cmd}: the game may have exited or unloaded the "
                                   + "DLL. Call read_games to see what is running.");
        }
    }

    private async Task EnsureConnectedAsync(int? pid, CancellationToken ct)
    {
        int target = ResolveTarget(pid);
        if (_pipe.IsConnected && target == _pid) return;

        if (_pipe.IsConnected)
        {
            int from = _pid;
            await DropAsync(null);   // switching games
            Notice($"Switched from game {from} to game {target}. Value-scan sessions begun on the other are gone.");
        }
        else if (_lastPid != 0 && _lastPid != target)
        {
            // The game this server last talked to is not the one this call reaches: it exited (and perhaps came back
            // as a new process), or the call named another.
            bool gone = !Listing().Pids.Contains(_lastPid);
            Notice(gone
                ? $"Game {_lastPid} is gone; this call reached game {target}, a different process: addresses from "
                  + "the earlier game do not apply here."
                : $"This call reached game {target}, not game {_lastPid}, which this server used before.");
        }
        else if (_pid != 0)
        {
            Notice($"The connection to game {_pid} had closed; this call opened a new one. Value-scan sessions begun "
                   + "before are gone.");
        }

        _pid = 0;
        _ready = false;
        string name = PipeTargets.NameFor(target);
        try
        {
            await _pipe.ConnectAsync(name, ct);
        }
        catch (TimeoutException)
        {
            // Listed a moment ago, yet no instance came free in time: list again before saying why.
            var now = _platform.ListNamedPipes();
            if (now is not null && !PipeTargets.Scan(now).Pids.Contains(target))
                throw new McpToolError($"Game {target}'s pipe is gone: the game exited or unloaded the DLL.");
            throw new McpToolError($"Every connection to game {target}'s pipe is taken (the DLL serves 6), or the "
                                   + "game is not responding." + HoldersNote(target));
        }
        catch (UnauthorizedAccessException)
        {
            // Measured 2026-11-11: a game running as administrator creates its pipe owned by Administrators, and UAC
            // refuses it to a non-elevated client. Listing the pipe needs no access, so the game still shows in
            // read_games.
            throw new McpToolError($"Windows refused the connection to game {target}: the game runs as administrator "
                                   + "(elevated) and this server does not. Start the AI client elevated, or start the "
                                   + "game normally. A server started over SSH runs elevated for an administrator "
                                   + "account.");
        }

        JsonObject ptrs;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);
            ptrs = await _pipe.SendAsync(new JsonObject { ["cmd"] = "get_pointers", ["background"] = true }, cts.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or InvalidOperationException
                                      or ObjectDisposedException)
        {
            await DropAsync(null);
            if (ct.IsCancellationRequested) throw;
            throw new McpToolError($"Game {target} accepted the connection but did not answer get_pointers.");
        }

        long reported = McpJson.TryGetLong(ptrs["pid"], out long p) ? p : 0;
        if (reported != 0 && reported != target)
        {
            await DropAsync(null);
            throw new McpToolError($"{name} was answered by process {reported}, not game {target}: another "
                                   + "process created that pipe name. Not connected.");
        }

        _pid = target;
        _lastPid = target;
        _log.Info(Constants.LogCatInit, $"[MCP] Connected to {name}.");
    }

    private PipeScan Listing()
        => PipeTargets.Scan(_platform.ListNamedPipes()
                            ?? throw new McpToolError("Could not list the named pipes on this machine."));

    /// <summary>The game a call goes to: the one it names (which must be listed), else the only one running.</summary>
    private int ResolveTarget(int? pid)
    {
        var scan = Listing();
        if (pid is int p)
        {
            if (scan.Pids.Contains(p)) return p;
            throw new McpToolError($"No game with pid {p} serves the dumper's pipe. " + Describe(scan));
        }

        switch (PipeTargets.Decide(scan))
        {
            case ConnectPlan.ConnectOne:
                return scan.Pids[0];
            case ConnectPlan.NoPipe:
                throw new McpToolError("No game is running the UE5Dumper DLL. Inject it, or start a game with a "
                                       + "proxy DLL deployed, then call again.");
            case ConnectPlan.LegacyOnly:
                throw new McpToolError(LegacyText);
        }

        // Choose: one game beside the legacy pipe still has one answer, since the legacy pipe cannot be dialled.
        if (scan.Pids.Count == 1) return scan.Pids[0];
        throw new McpToolError("Several games run the UE5Dumper DLL: pass pid on every call. " + Describe(scan));
    }

    private const string LegacyText =
        "Only an old UE5Dumper DLL's pipe (\\\\.\\pipe\\" + Constants.LegacyPipeName + ") is up: the game runs a DLL "
        + "from before per-game pipes, usually a proxy DLL never updated. Close the game, update the proxy (UE5DumpUI's "
        + "Proxy Deploy tab, Update All) and start it again.";

    private string Describe(PipeScan scan)
    {
        if (scan.Pids.Count == 0)
            return scan.LegacyPresent ? LegacyText : "No game runs it now.";
        var rows = new List<string>(scan.Pids.Count);
        foreach (int pid in scan.Pids)
        {
            var (image, title) = _platform.DescribeProcess(pid);
            rows.Add(string.IsNullOrEmpty(title) ? $"{pid} {image}" : $"{pid} {image} \"{title}\"");
        }
        return "Running: " + string.Join("; ", rows) + ".";
    }

    /// <summary>Who holds a full pipe, from the game DLL's at-capacity log line; "" when it cannot be read.</summary>
    private string HoldersNote(int pid)
    {
        string image = _platform.DescribeProcess(pid).ImageName;
        if (string.IsNullOrEmpty(image)) return "";
        var seen = PipeHolders.ParseLatest(
            DumperLogTail.Read(_platform.GetLogDirectoryPath(), image, "pipe"), PipeTargets.NameFor(pid));
        if (seen is null || seen.Superseded || seen.Holders.Count == 0) return "";
        var list = seen.Holders.Select(h => string.IsNullOrEmpty(h.LastCmd)
            ? $"{h.Exe} ({h.Pid})"
            : $"{h.Exe} ({h.Pid}, {(h.InFlight ? "running" : "last")} {h.LastCmd})");
        string more = seen.More > 0 ? $" and {seen.More} more" : "";
        return $" Holders at {seen.Time}: {string.Join("; ", list)}{more}.";
    }

    private async Task DropAsync(string? why)
    {
        int was = _pid;
        try { await _pipe.DisconnectAsync(); }
        catch (Exception e) { _log.Warn(Constants.LogCatPipe, $"[MCP] Disconnect failed: {e.Message}"); }
        _pid = 0;
        _ready = false;
        if (why is not null && was != 0)
        {
            _log.Warn(Constants.LogCatPipe, $"[MCP] Closed the connection to game {was}: {why}.");
            Notice($"The connection to game {was} was closed ({why}). Value-scan sessions begun on it are gone.");
        }
    }

    private void Notice(string text)
    {
        // Two notices before the model sees one: keep both.
        string? old = Interlocked.Exchange(ref _notice, text);
        if (old is not null) Interlocked.Exchange(ref _notice, old + " " + text);
    }

    public void Dispose() => _gate.Dispose();
}
