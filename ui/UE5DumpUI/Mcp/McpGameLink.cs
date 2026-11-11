using System.Globalization;
using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Services;

namespace UE5DumpUI.Mcp;

/// <summary>A game serving the dumper's pipe, as the game list shows it.</summary>
internal sealed record McpGameInfo(int Pid, string Exe, string? WindowTitle, bool Connected);

/// <summary>The MCP tools' view of the game: what is running, and one request at a time to the chosen game.</summary>
internal interface IMcpGame
{
    /// <summary>The games serving the dumper's pipe, and whether an old DLL's shared pipe is up too.</summary>
    (IReadOnlyList<McpGameInfo> Games, bool LegacyPresent) ListGames();

    /// <summary>The pid of the game this server is connected to, or 0.</summary>
    int ConnectedPid { get; }

    /// <summary>Whether the connected game's object scan is done, as last seen.</summary>
    bool Scanned { get; }

    /// <summary>Records that the connected game's scan is done (seen by a tool that asked).</summary>
    void MarkScanned();

    /// <summary>Sends <paramref name="request"/> to the game <paramref name="pid"/> names, or to the one already
    /// connected, or to the only one running; connects first when needed. Throws <see cref="McpToolError"/> with the
    /// reason when there is no game to send to, the pipe is full or gone, or the reply did not come in time.</summary>
    Task<JsonObject> SendAsync(int? pid, JsonObject request, TimeSpan timeout, CancellationToken ct);

    /// <summary>A note owed to the model about the connection (it was replaced, so sessions on the old one are
    /// gone), returned once; null when there is none.</summary>
    string? TakeNotice();
}

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D. The MCP server's one connection to a game's pipe.
///
/// <para><b>One connection, one request at a time.</b> The DLL serves each connection on a thread of its own, one
/// command after another, so a second connection would only buy a second queue, at the cost of a second of the six
/// slots. The UI keeps its own two; nothing this server sends waits behind the UI, or the UI behind it. Value-scan
/// sessions belong to the connection that began them (Stage C), so they live as long as this connection does.</para>
///
/// <para><b>Choosing the game</b> follows the UI's Connect: the game a call names, else the one already connected,
/// else the only one running. Several games and no pid is an error that lists them, since the model can pass the pid;
/// the UI's picker has no counterpart here. An old DLL's shared pipe is never dialled. The pid the DLL reports is
/// checked, as the UI checks it: a process that created a game's pipe name first would otherwise answer for it.</para>
///
/// <para><b>Cancelling.</b> The DLL has no cancel command: it stops a connection's command when the connection
/// breaks (checked every 200 ms). So a call that overruns its time, or that the client cancels, closes the
/// connection, and the next call opens a new one. Its scan sessions go with it, and the next reply says so.</para>
/// </summary>
internal sealed class McpGameLink : IMcpGame, IDisposable
{
    private readonly IPipeClient _pipe;
    private readonly IPlatformService _platform;
    private readonly ILoggingService _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private int _pid;          // the connected game, 0 when none
    private int _lastPid;      // the game last connected, kept across a dropped connection
    private bool _scanned;     // that game's scan, as last seen
    private string? _notice;   // owed to the model with the next reply

    public McpGameLink(IPipeClient pipe, IPlatformService platform, ILoggingService log)
    {
        _pipe = pipe;
        _platform = platform;
        _log = log;
    }

    public int ConnectedPid => _pipe.IsConnected ? _pid : 0;
    public bool Scanned => _pipe.IsConnected && _scanned;
    public void MarkScanned() => _scanned = true;

    public string? TakeNotice() => Interlocked.Exchange(ref _notice, null);

    public (IReadOnlyList<McpGameInfo> Games, bool LegacyPresent) ListGames()
    {
        var names = _platform.ListNamedPipes()
                    ?? throw new McpToolError("Could not list the named pipes on this machine.");
        var scan = PipeTargets.Scan(names);
        var games = new List<McpGameInfo>(scan.Pids.Count);
        foreach (int pid in scan.Pids)
        {
            var (image, title) = _platform.DescribeProcess(pid);
            games.Add(new McpGameInfo(pid, image, title, pid == ConnectedPid));
        }
        return (games, scan.LegacyPresent);
    }

    public async Task<JsonObject> SendAsync(int? pid, JsonObject request, TimeSpan timeout, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(pid, ct);
            string cmd = McpJson.GetString(request, "cmd") ?? "?";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                return await _pipe.SendAsync(request, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // The DLL is still running it: closing the connection is what stops it.
                bool byClient = ct.IsCancellationRequested;
                await DropAsync(byClient
                    ? $"{cmd} was cancelled by the client"
                    : $"{cmd} did not answer within {timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s");
                if (byClient) throw;
                throw new McpToolError(
                    $"{cmd} did not answer within {timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s. "
                    + "The connection was closed, which makes the DLL stop it; the next call reconnects. "
                    + "A game that is paused or loading answers slowly.");
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The pipe died under the request: the game exited, or its DLL stopped. Never retried, since the
                // command may have run.
                await DropAsync($"the pipe closed during {cmd}");
                throw new McpToolError(
                    $"The game's pipe closed during {cmd}: the game may have exited or unloaded the DLL. "
                    + "Call read_games to see what is running.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureConnectedAsync(int? pid, CancellationToken ct)
    {
        if (_pipe.IsConnected && (pid is null || pid == _pid)) return;

        if (_pid != 0 && !_pipe.IsConnected)
            Notice($"The connection to game {_pid} had closed; this call opened a new one. "
                   + "Value-scan sessions begun before are gone.");

        int target = ResolveTarget(pid);
        if (_pipe.IsConnected)
        {
            int from = _pid;
            await DropAsync(null);   // switching games
            Notice($"Switched from game {from} to game {target}. Value-scan sessions begun on the other are gone.");
        }

        _pid = 0;
        _scanned = false;
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

        JsonObject ptrs;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            ptrs = await _pipe.SendAsync(new JsonObject { ["cmd"] = "get_pointers" }, cts.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or InvalidOperationException
                                      or ObjectDisposedException)
        {
            await DropAsync(null);
            if (ct.IsCancellationRequested) throw;
            throw new McpToolError($"Game {target} accepted the connection but did not answer get_pointers.");
        }

        int reported = ptrs["pid"] is JsonValue pv && pv.TryGetValue<int>(out int p) ? p : 0;
        if (reported != 0 && reported != target)
        {
            await DropAsync(null);
            throw new McpToolError($"{name} was answered by process {reported}, not game {target}: another "
                                   + "process created that pipe name. Not connected.");
        }

        _pid = target;
        _lastPid = target;
        _scanned = ScannedFrom(ptrs);
        _log.Info(Constants.LogCatInit, $"[MCP] Connected to {name} (scanned: {_scanned}).");
    }

    /// <summary>The game a call goes to: the one it names (which must be listed), else the only one running.</summary>
    private int ResolveTarget(int? pid)
    {
        var names = _platform.ListNamedPipes()
                    ?? throw new McpToolError("Could not list the named pipes on this machine.");
        var scan = PipeTargets.Scan(names);

        if (pid is int p)
        {
            if (scan.Pids.Contains(p)) return p;
            throw new McpToolError($"No game with pid {p} serves the dumper's pipe. " + Listing(scan));
        }

        // A dropped connection reconnects to the same game while it still runs, whatever else started meanwhile.
        if (_lastPid != 0 && scan.Pids.Contains(_lastPid)) return _lastPid;

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
        throw new McpToolError("Several games run the UE5Dumper DLL: pass pid. " + Listing(scan));
    }

    private const string LegacyText =
        "Only an old UE5Dumper DLL's pipe (\\\\.\\pipe\\" + Constants.LegacyPipeName + ") is up: the game runs a DLL "
        + "from before per-game pipes, usually a proxy DLL never updated. Close the game, update the proxy (UE5DumpUI's "
        + "Proxy Deploy tab, Update All) and start it again.";

    private string Listing(PipeScan scan)
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

    /// <summary>The scan is done when the object pool is found and holds objects. A proxy-loaded game's pipe comes up
    /// before its scan, and every object command then answers with zeros.</summary>
    internal static bool ScannedFrom(JsonObject reply)
    {
        long count = reply["object_count"] is JsonValue c && c.TryGetValue<long>(out long n) ? n : 0;
        string? method = McpJson.GetString(reply, "gobjects_method");
        return count > 0 && method != "not_found";
    }

    private async Task DropAsync(string? why)
    {
        int was = _pid;
        try { await _pipe.DisconnectAsync(); }
        catch (Exception e) { _log.Warn(Constants.LogCatPipe, $"[MCP] Disconnect failed: {e.Message}"); }
        _pid = 0;
        _scanned = false;
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
