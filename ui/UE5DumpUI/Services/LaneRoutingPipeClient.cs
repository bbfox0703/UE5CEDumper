using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Two-connection lane router (Path A — docs/multipipe-eval.md §9). Presents a
/// single <see cref="IPipeClient"/> to the rest of the app while internally
/// holding TWO connections to the same DLL pipe server:
///   • interactive — light, fast browse commands (Live Walker, properties,
///     teleport, godmode/movement, pagination);
///   • bulk        — heavy, long-running commands (snapshot, value/group scan,
///     find_*, search_*, list_*, invoke).
/// Each connection is its own handle served by its own DLL thread, so a heavy
/// command on the bulk lane never blocks a light command on the interactive
/// lane (and the synchronous-pipe single-handle deadlock can't occur).
///
/// Reconnect policy: if EITHER lane drops unexpectedly, BOTH are torn down so a
/// reconnect re-establishes both together — this matters because the DLL resets
/// its per-command cancel flag only when its connection registry goes empty
/// (first-connection-of-a-session), so a half-reconnect could leave a stale
/// cancel set. See §9.3 / §9.7.
/// </summary>
public sealed class LaneRoutingPipeClient : IPipeClient
{
    private readonly PipeClient _interactive;
    private readonly PipeClient _bulk;
    private readonly ILoggingService _log;

    // Commands routed to the BULK lane. Everything else goes interactive.
    // MUST-bulk (build the Aura class-metadata caches → must never run
    // concurrently with another such command, which a single serial bulk lane
    // guarantees): find_by_address, find_path_from_gworld, find_refs_to_uobject,
    // begin/refine value+group scan, snapshot_chunk. SHOULD-bulk (long; keep off
    // the interactive lane so Live Walker stays responsive): the rest below.
    private static readonly HashSet<string> BulkCommands = new(StringComparer.Ordinal)
    {
        "begin_value_scan", "refine_value_scan", "end_value_scan", "query_candidates",
        "begin_group_scan", "refine_group_scan", "end_group_scan", "query_group_candidates",
        // Same lane as its session's other commands: it takes GroupSessionManager::mu_,
        // which refine_group_scan holds for the WHOLE refine. On the interactive lane an
        // expanded row would block Live Walker behind a running refine.
        "query_group_slot_leaves",
        "begin_snapshot", "snapshot_chunk",
        "find_instances", "find_by_address", "find_refs_to_uobject", "find_path_from_gworld",
        "search_properties", "search_properties_batch",
        "list_classes", "list_all_functions", "list_enums",
        "search_objects", "detect_noise_classes", "walk_world",
        "get_related_objects", "get_current_target",
        "find_property_xrefs", "find_functions_by_class", "walk_function_props",
        "walk_datatable_rows",
        "invoke_function",
        "rescan", "trigger_scan", "apply_rescan",
    };

    private bool _lastReported;          // last combined IsConnected we raised
    private bool _tearingDown;           // guards the reconnect-both teardown

    // Single combined game-thread-stalled level. Both lanes report EVERY
    // per-response observation (PipeClient no longer edge-suppresses per lane —
    // that was the audit-X7 root cause), and this one latch dedupes so the
    // consumer still only hears transitions. Fixes the banner sticking ON when
    // the lane that saw the pause went idle after the game resumed.
    private readonly Helpers.GameThreadStalledLevel _stalled = new();

    public LaneRoutingPipeClient(ILoggingService log)
    {
        _log = log;
        _interactive = new PipeClient(log, "I");
        _bulk = new PipeClient(log, "B");

        // Forward responses-as-events + activity from both lanes.
        _interactive.EventReceived += e => EventReceived?.Invoke(e);
        _bulk.EventReceived        += e => EventReceived?.Invoke(e);
        _interactive.Activity      += a => Activity?.Invoke(a);
        _bulk.Activity             += a => Activity?.Invoke(a);
        // Both lanes observe the same global game-thread state, each whenever it
        // gets a response. Feed every observation into the one combined latch and
        // raise only on a real level change.
        _interactive.GameThreadStalledChanged += RaiseStalledIfChanged;
        _bulk.GameThreadStalledChanged        += RaiseStalledIfChanged;

        _interactive.ConnectionStateChanged += _ => OnChildStateChanged();
        _bulk.ConnectionStateChanged        += _ => OnChildStateChanged();
    }

    private void RaiseStalledIfChanged(bool stalled)
    {
        if (_stalled.Observe(stalled) is bool level)
            GameThreadStalledChanged?.Invoke(level);
    }

    public bool IsConnected => _interactive.IsConnected && _bulk.IsConnected;
    public event Action<bool>? ConnectionStateChanged;
    public event Action<JsonObject>? EventReceived;
    public event Action<PipeLogEntry>? Activity;
    public event Action<bool>? GameThreadStalledChanged;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // Connect both lanes; on partial failure, tear the other down so we
        // never run half-connected.
        // Reset the combined stall level so the first observation after a
        // (re)connect always raises, even if the game was already paused before
        // this connection (mirrors the per-lane reset PipeClient used to do).
        _stalled.Reset();
        try
        {
            await _interactive.ConnectAsync(ct).ConfigureAwait(false);
            await _bulk.ConnectAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            try { await _interactive.DisconnectAsync().ConfigureAwait(false); } catch { }
            try { await _bulk.DisconnectAsync().ConfigureAwait(false); } catch { }
            throw;
        }
        RaiseCombined();
    }

    public async Task DisconnectAsync()
    {
        _tearingDown = true;
        try
        {
            await _interactive.DisconnectAsync().ConfigureAwait(false);
            await _bulk.DisconnectAsync().ConfigureAwait(false);
        }
        finally
        {
            _tearingDown = false;
        }
        RaiseCombined();
    }

    public Task<JsonObject> SendAsync(JsonObject request, CancellationToken ct = default)
    {
        var cmd = request["cmd"]?.GetValue<string>();
        var lane = (cmd != null && BulkCommands.Contains(cmd)) ? _bulk : _interactive;
        return lane.SendAsync(request, ct);
    }

    // A child connection changed state. If one lane dropped unexpectedly (not
    // during our own DisconnectAsync), tear BOTH down so the next connect is a
    // clean both-lane reconnect (see class remarks). Always re-raise combined.
    private void OnChildStateChanged()
    {
        if (!_tearingDown && _lastReported && !IsConnected)
        {
            _log.Warn(Constants.LogCatPipe, "Pipe lane dropped — tearing down both lanes for a clean reconnect");
            _ = DisconnectAsync();   // fire-and-forget; RaiseCombined runs inside
            return;
        }
        RaiseCombined();
    }

    private void RaiseCombined()
    {
        bool now = IsConnected;
        if (now == _lastReported) return;
        _lastReported = now;
        ConnectionStateChanged?.Invoke(now);
    }

    public void Dispose()
    {
        _interactive.Dispose();
        _bulk.Dispose();
    }
}
