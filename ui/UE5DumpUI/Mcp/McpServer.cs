using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UE5DumpUI.Core;

namespace UE5DumpUI.Mcp;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D. A Model Context Protocol server over stdio, tools only, written against the
/// 2025-11-25 revision (maintainer, 2026-11-11: hand-rolled, no SDK in the AOT image).
///
/// <para><b>Framing.</b> One JSON-RPC message per line in each direction, UTF-8, LF. Nothing but these messages may
/// reach stdout; McpHost points <see cref="Console.Out"/> at stderr before anything runs.</para>
///
/// <para><b>Which revision.</b> <c>initialize</c> echoes a revision this server speaks and answers its newest
/// otherwise. 2026-07-28 removed the handshake; its clients first send <c>server/discover</c>, and Claude Code
/// documents that a server answering it with an error gets the handshake instead, so it gets method-not-found. A
/// stdio server must not exit on it: the client would then skip the probe, but it would also have lost the server.</para>
///
/// <para><b>Concurrency.</b> Each request runs on its own task, so a <c>notifications/cancelled</c> for a long call
/// is read while that call runs. The tools still reach the game one at a time, through the one pipe connection
/// (<see cref="McpGameLink"/>). A cancelled request gets no response, as the spec asks. stdin closing ends the server:
/// Claude Code closes it and ends the process 300 ms later on Windows.</para>
/// </summary>
internal sealed class McpServer
{
    private readonly IReadOnlyList<McpTool> _tools;
    private readonly Dictionary<string, McpTool> _byName;
    private readonly ILoggingService _log;
    private readonly Stream _out;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inflight = new();
    private readonly ConcurrentDictionary<Task, byte> _running = new();
    private readonly string _version;

    public McpServer(IReadOnlyList<McpTool> tools, ILoggingService log, Stream output, string version)
    {
        _tools = tools;
        _byName = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        _log = log;
        _out = output;
        _version = version;
    }

    /// <summary>What the client loads before any tool (Claude Code cuts it at 4,096 characters): how the tools fit
    /// together, which no single tool description can say.</summary>
    internal const string Instructions =
        "UE5CEDumper reads a running Unreal Engine 4 / 5 game through its UE5Dumper DLL (injected, or loaded by a "
        + "proxy DLL). read_games lists the games running it; pass pid to a tool only when there are several. "
        + "read_status comes first: it shows the engine globals and whether the object scan is done. A game loaded "
        + "through a proxy DLL needs scan_start (then read_status until scanned is true) before anything else "
        + "answers.\n"
        + "Addresses are hex strings (\"0x7FF6...\"). A typical path: read_search_objects, read_find_instances or "
        + "read_find_live_instance for an object's address; read_instance for its live field values; read_class "
        + "for its class layout (addr = class_addr); read_functions for its UFunctions. read_find_refs shows who "
        + "points at an object, read_path_from_root how to reach it from GWorld, read_ce_pointer a Cheat Engine "
        + "pointer to it. read_value_scan_begin / _refine / _results / _end is a Cheat Engine-style value search "
        + "over UPROPERTY fields.\n"
        + "read_* tools only read. scan_start starts a scan on a game whose scan never ran. write_* tools change the "
        + "game (god mode, movement, time dilation, fly, see-through, Keep Foreground, debug camera, cursor, forced "
        + "fields, teleport); the user is asked before each one. They never replace or end what the user, a Cheat "
        + "Engine record or another session holds: such a call is refused, naming the holder (read_holds shows who "
        + "holds what). The rule binds this server only: the user's UE5DumpUI window and Cheat Engine records can "
        + "change or end anything this server holds at any time, and it is then theirs. Everything this server turns "
        + "on is released when the session ends. Teleport takes explicit "
        + "coordinates (read_pose first) and leaves the user's markers and undo point alone. Fly, see-through, Keep "
        + "Foreground and forced fields follow the UI's experimental switch.\n"
        + "Calls run one at a time on this server's own pipe connection; a UE5DumpUI window has its own connections and "
        + "is not disturbed. A reply that was cut to fit says so: narrow the request, or page with offset / limit.";

    /// <summary>Serves until <paramref name="input"/> ends. Returns the process exit code.</summary>
    public async Task<int> RunAsync(Stream input, CancellationToken ct = default)
    {
        using var reader = new StreamReader(input, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
            {
                break;   // the client went away
            }
            if (line is null) break;   // EOF: the client closed stdin
            if (string.IsNullOrWhiteSpace(line)) continue;
            Track(HandleLineAsync(line));
        }

        // stdin closed, but stdout may still be read: a one-shot client pipes its requests in and reads the answers.
        // What already runs gets EofGrace to answer; then it is cancelled, which closes the pipe connection, so the
        // DLL stops the command rather than finishing a walk nobody will read.
        var all = Task.WhenAll(_running.Keys);
        if (await Task.WhenAny(all, Task.Delay(EofGrace)) != all)
        {
            foreach (var cts in _inflight.Values) TryCancel(cts);
            await Task.WhenAny(Task.WhenAll(_running.Keys), Task.Delay(TimeSpan.FromSeconds(1)));
        }
        _log.Info(Constants.LogCatInit, "[MCP] stdin closed; server exiting.");
        return 0;
    }

    /// <summary>How long requests still running when stdin closes get to answer. Claude Code ends the process 300 ms
    /// after closing stdin, so this matters only to a client that waits.</summary>
    internal static readonly TimeSpan EofGrace = TimeSpan.FromSeconds(2);

    private void Track(Task task)
    {
        _running[task] = 0;
        task.ContinueWith(t => _running.TryRemove(t, out _), TaskScheduler.Default);
    }

    /// <summary>Handles one line from the client: a request (answered), a notification (never answered), or
    /// something that is neither (answered with an error when it carries an id).</summary>
    internal async Task HandleLineAsync(string line)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            await WriteAsync(McpJson.Error(null, McpErrorCode.ParseError, "Parse error: the line is not JSON."));
            return;
        }
        if (node is not JsonObject msg)
        {
            // JSON-RPC batches were removed from MCP in 2025-06-18.
            await WriteAsync(McpJson.Error(null, McpErrorCode.InvalidRequest, "Invalid request: expected one object."));
            return;
        }

        string? method = McpJson.GetString(msg, "method");
        bool hasId = msg.ContainsKey("id");
        JsonNode? id = msg["id"];

        if (method is null)
        {
            // A response to a request this server never sends (it sends none): nothing to do.
            if (msg.ContainsKey("result") || msg.ContainsKey("error")) return;
            if (hasId) await WriteAsync(McpJson.Error(id, McpErrorCode.InvalidRequest, "Invalid request: no method."));
            return;
        }

        var parameters = msg["params"] as JsonObject;
        if (!hasId)
        {
            HandleNotification(method, parameters);
            return;
        }

        string? key = McpJson.IdKey(id);
        if (key is null)
        {
            await WriteAsync(McpJson.Error(null, McpErrorCode.InvalidRequest,
                "Invalid request: the id must be a string or an integer."));
            return;
        }

        using var cts = new CancellationTokenSource();
        if (!_inflight.TryAdd(key, cts))
        {
            // With the id, the client would take this for the running request's answer and then get a second one.
            await WriteAsync(McpJson.Error(null, McpErrorCode.InvalidRequest,
                "Invalid request: a request with id " + McpJson.Write(id) + " is still running."));
            return;
        }
        try
        {
            JsonNode result = await DispatchAsync(method, parameters, cts.Token);
            if (!cts.IsCancellationRequested) await WriteAsync(McpJson.Result(id, result));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Cancelled by the client: the spec asks for no response.
        }
        catch (McpRpcException e)
        {
            if (!cts.IsCancellationRequested) await WriteAsync(McpJson.Error(id, e.Code, e.Message));
        }
        catch (Exception e)
        {
            _log.Error(Constants.LogCatInit, $"[MCP] {method} failed", e);
            if (!cts.IsCancellationRequested)
                await WriteAsync(McpJson.Error(id, McpErrorCode.InternalError, "Internal error: " + e.Message));
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }

    private void HandleNotification(string method, JsonObject? parameters)
    {
        switch (method)
        {
            case "notifications/cancelled":
                string? key = McpJson.IdKey(parameters?["requestId"]);
                // Unknown or finished ids are expected: the response and the cancel can cross.
                if (key is not null && _inflight.TryGetValue(key, out var cts)) TryCancel(cts);
                break;
            case "notifications/initialized":
                _log.Info(Constants.LogCatInit, "[MCP] Client initialized.");
                break;
            // Any other notification needs nothing from a tools-only server.
        }
    }

    private Task<JsonNode> DispatchAsync(string method, JsonObject? parameters, CancellationToken ct) => method switch
    {
        "initialize" => Task.FromResult(Initialize(parameters)),
        "ping" => Task.FromResult<JsonNode>(new JsonObject()),
        "tools/list" => Task.FromResult<JsonNode>(new JsonObject { ["tools"] = ListTools() }),
        "tools/call" => CallToolAsync(parameters, ct),
        _ => throw new McpRpcException(McpErrorCode.MethodNotFound, "Method not found: " + method
             + (method == "server/discover" ? " (this server speaks the initialize handshake, protocol "
                                              + Constants.McpLatestProtocolVersion + ")" : "")),
    };

    private JsonNode Initialize(JsonObject? parameters)
    {
        string? asked = McpJson.GetString(parameters, "protocolVersion");
        string version = asked is not null && Array.IndexOf(Constants.McpProtocolVersions, asked) >= 0
            ? asked
            : Constants.McpLatestProtocolVersion;
        string client = McpJson.GetString(parameters?["clientInfo"] as JsonObject, "name") ?? "?";
        _log.Info(Constants.LogCatInit, $"[MCP] initialize from {client}: asked {asked ?? "none"}, answered {version}.");
        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = Constants.McpServerName,
                ["title"] = Constants.McpServerTitle,
                ["version"] = _version,
            },
            ["instructions"] = Instructions,
        };
    }

    private JsonArray ListTools()
    {
        var arr = new JsonArray();
        foreach (var t in _tools)
        {
            var entry = new JsonObject
            {
                ["name"] = t.Name,
                ["title"] = t.Title,
                ["description"] = t.Description,
                ["inputSchema"] = t.InputSchema.DeepClone(),
                ["annotations"] = new JsonObject
                {
                    ["title"] = t.Title,
                    ["readOnlyHint"] = t.ReadOnly,
                    ["destructiveHint"] = t.Destructive,
                    ["idempotentHint"] = t.Idempotent,
                    // Every tool acts on the local game and nothing beyond it.
                    ["openWorldHint"] = false,
                },
            };
            if (t.RequiresInteraction)
                entry["_meta"] = new JsonObject { [Constants.McpRequiresInteractionKey] = true };
            McpJson.Append(arr, entry);
        }
        return arr;
    }

    private async Task<JsonNode> CallToolAsync(JsonObject? parameters, CancellationToken ct)
    {
        string? name = McpJson.GetString(parameters, "name");
        if (name is null || !_byName.TryGetValue(name, out var tool))
            throw new McpRpcException(McpErrorCode.InvalidParams, "Unknown tool: " + (name ?? "(none)"));

        if (parameters?["arguments"] is not (null or JsonObject))
            throw new McpRpcException(McpErrorCode.InvalidParams, "arguments must be an object.");
        var args = new McpArgs(parameters?["arguments"] as JsonObject);

        McpToolResult result;
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            result = await tool.Run(args, ct);
        }
        catch (McpToolError e)
        {
            result = McpToolResult.Fail(e.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is not McpRpcException)
        {
            _log.Error(Constants.LogCatInit, $"[MCP] tool {name} failed", e);
            result = McpToolResult.Fail("Internal error in " + name + ": " + e.Message);
        }
        _log.Info(Constants.LogCatPipe, $"[MCP] {name} {(result.IsError ? "error" : "ok")} in {started.ElapsedMilliseconds} ms");
        return BuildResult(result);
    }

    /// <summary>The <c>tools/call</c> result: the payload as one JSON text block, cut to fit, and the note, when
    /// there is one, as a second block. No <c>structuredContent</c>: which of the two Claude Code hands the model
    /// is not settled, and sending both would double the reply against its size cap.
    /// <para>An error's note goes inside its one block instead (a <c>note</c> member): Claude Code hands the model only
    /// the first block of an error result, and a note it drops is lost, since the connection gives each note once.
    /// A reply too large to cut is an error too.</para></summary>
    internal static JsonObject BuildResult(McpToolResult result)
    {
        bool isError = result.IsError;
        string? note = result.Note;
        if (isError && note is not null && result.Payload is JsonObject errorPayload)
        {
            errorPayload["note"] = note;
            note = null;
        }
        var fit = McpResultFit.Fit(result.Payload);
        string text = fit.Text;
        if (fit.Stubbed)
        {
            isError = true;
            // The stub replaced the payload: whatever the note said joins the stub's own advice inside the one block.
            var stub = (JsonObject)JsonNode.Parse(text)!;
            stub["note"] = note is null ? fit.Note : note + " " + fit.Note;
            text = McpJson.Write(stub);
            note = null;
        }
        else if (fit.Note is not null)
        {
            note = note is null ? fit.Note : note + " " + fit.Note;
        }

        var content = new JsonArray((JsonNode?)new JsonObject { ["type"] = "text", ["text"] = text });
        if (note is not null)
            McpJson.Append(content, new JsonObject { ["type"] = "text", ["text"] = "Note: " + note });
        var obj = new JsonObject { ["content"] = content };
        if (isError) obj["isError"] = true;
        return obj;
    }

    private async Task WriteAsync(JsonObject message)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(McpJson.Write(message) + "\n");
        await _writeLock.WaitAsync();
        try
        {
            await _out.WriteAsync(bytes);
            await _out.FlushAsync();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            _log.Warn(Constants.LogCatPipe, $"[MCP] Could not write to stdout: {e.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static void TryCancel(CancellationTokenSource cts)
    {
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}
