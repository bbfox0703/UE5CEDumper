using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UE5DumpUI.Core;

namespace UE5DumpUI.Mcp;

/// <summary>What the command line opened writes to: <c>--writes=any-host</c>, <c>--writes=off</c>, or neither.</summary>
public enum McpWriteMode { Default, AnyHost, Off }

/// <summary>How this session's host may write, decided once at initialize. <see cref="ReadOnly"/> lists no write;
/// <see cref="MarkedPerCall"/> asks before every write (the behaviour before grants); <see cref="Grants"/> asks once
/// per grant and per call only for the writes no grant covers.</summary>
public enum McpHostDecision { ReadOnly, MarkedPerCall, Grants }

/// <summary>A kind of grant. Two kinds and not one, so that "annoying" (god mode stays on, the game runs fast) never
/// quietly implies "may kill or lose the pawn".</summary>
public enum McpGrantKind { None, Toggles, Movement }

/// <summary>What a tool is to the grant gate: which hosts list it, whether the host is asked before each call, and
/// whether a call must be admitted.</summary>
public enum McpToolRole { Read, Scan, GrantedWrite, PerCallWrite, Release, Grant, GrantAdmin }

/// <summary>A grant as the session sees it: the game run it is for and how many calls it has admitted.</summary>
public sealed record McpGrantRecord(long Id, McpGrantKind Kind, long Pid, string CreationTime, DateTime GrantedAtUtc,
    long Uses);

/// <summary>Proof that one call was admitted, for one game run. Only <see cref="McpGrants"/> makes one, and the gate
/// re-checks it at the send, so a capability cannot reach another game run or outlive a revoke.</summary>
public sealed class McpGrantCapability
{
    internal McpGrantCapability(long grantId, McpGrantKind kind, long pid, string creationTime)
    {
        GrantId = grantId;
        Kind = kind;
        Pid = pid;
        CreationTime = creationTime;
    }

    /// <summary>The grant that admitted the call, or 0 for a call the host asked about on its own.</summary>
    public long GrantId { get; }
    public bool PerCall => GrantId == 0;
    public McpGrantKind Kind { get; }
    public long Pid { get; }
    public string CreationTime { get; }
}

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] Phase 1. The write grants of one MCP session: which host may write, how each DLL
/// command is classed, and the grants the user gave.
///
/// Claude Code's prompt for a marked tool offers only "Deny" and "Allow once", so before grants every repeat of a write
/// asked again. A grant tool now carries the prompt instead, and the writes it covers are admitted against the grant.
/// Nothing here trusts the model: the decision comes from the host's own initialize, a grant only from a call the host
/// asked the user about, and the gate in <see cref="McpGameLink"/> re-checks every state-changing command at the send.
/// The plan, its measurements and the rejected alternatives: docs/mcp-write-approval-eval.md.
///
/// A grant is keyed on the game RUN (pid and process start time), because the link silently reconnects to whichever
/// game answers, and a pid alone is reused. It ends with the server process, a new game run, or a revoke. It survives
/// a pipe drop to the same run, while the DLL drops the holds: the AI may take them again without a new prompt.
/// </summary>
public sealed partial class McpGrants
{
    // A request key that says how a command is sent, never what it changes; an OFF carrying anything else is not a
    // pure release.
    private static readonly HashSet<string> EnvelopeKeys = new(StringComparer.Ordinal)
    {
        "cmd", "if_free", "background", "id",
    };

    private static readonly HashSet<string> Reads = new(StringComparer.Ordinal)
    {
        "get_pointers", "scan_status", "trigger_scan", "search_objects", "find_object", "find_instances",
        "find_live_instance", "get_object", "find_by_address", "list_classes", "walk_class", "walk_functions",
        "walk_instance", "read_array_elements", "read_mem", "walk_world", "find_refs_to_uobject",
        "find_path_from_gworld", "get_related_objects", "get_current_target", "search_properties",
        "find_property_xrefs", "walk_function_props", "get_function_code_addr", "find_functions_by_class",
        "list_all_functions", "list_enums", "get_ce_pointer_info", "walk_datatable_rows", "begin_value_scan",
        "refine_value_scan", "query_candidates", "end_value_scan", "pipe_clients", "get_offsets", "get_holds",
        "teleport_get_pose", "get_movement_params", "get_time_state", "get_forced_fields", "seethrough_get_state",
    };

    private static readonly HashSet<string> Toggles = new(StringComparer.Ordinal)
    {
        "set_god_mode", "set_debug_camera", "set_foreground_lock", "seethrough_set", "set_mouse_cursor",
        "set_time_dilation",
    };

    private static readonly HashSet<string> Movement = new(StringComparer.Ordinal)
    {
        "set_movement_multiplier", "set_gravity_direction", "fly_set", "teleport_recall_marker",
    };

    // Asked every call whatever the host: a raw write, an invoke and a class-wide forced field each reach further than
    // one switch, and a kind grant would hide that.
    private static readonly HashSet<string> PerCall = new(StringComparer.Ordinal)
    {
        "force_field", "write_mem", "invoke_function",
    };

    // Named one by one, never by the reset_ prefix: reset_all_fields ends what every client holds.
    private static readonly HashSet<string> Resets = new(StringComparer.Ordinal)
    {
        "reset_movement", "reset_gravity_direction", "reset_time_dilation", "reset_field",
    };

    // The ON/OFF commands whose OFF ends only a hold. fly_set and seethrough_set also take global settings, applied
    // before enable, which no release undoes; IsRelease therefore accepts their OFF only with nothing else in it.
    private static readonly HashSet<string> OffReleases = new(StringComparer.Ordinal)
    {
        "set_god_mode", "set_foreground_lock", "set_debug_camera", "seethrough_set", "fly_set",
    };

    private static readonly HashSet<string> NoCommands = new(StringComparer.Ordinal);

    private readonly ILoggingService? _log;
    private readonly object _lock = new();
    private readonly List<Entry> _grants = new();
    private long _nextId;
    private int _decision = -1;

    private sealed class Entry
    {
        public required long Id { get; init; }
        public required McpGrantKind Kind { get; init; }
        public required long Pid { get; init; }
        public required string CreationTime { get; init; }
        public required DateTime GrantedAtUtc { get; init; }
        public long Uses { get; set; }

        public McpGrantRecord Snapshot() => new(Id, Kind, Pid, CreationTime, GrantedAtUtc, Uses);
    }

    public McpGrants(McpWriteMode mode = McpWriteMode.Default, ILoggingService? log = null)
    {
        Mode = mode;
        _log = log;
    }

    public McpWriteMode Mode { get; }

    /// <summary>The host's decision; <see cref="McpHostDecision.ReadOnly"/> until the first initialize, so a call that
    /// comes before it gets nothing.</summary>
    public McpHostDecision Decision
    {
        get
        {
            int d = Volatile.Read(ref _decision);
            return d < 0 ? McpHostDecision.ReadOnly : (McpHostDecision)d;
        }
    }

    public bool IsDecided => Volatile.Read(ref _decision) >= 0;

    /// <summary>Decides from the first initialize and keeps that decision: a client that introduces itself again must
    /// not widen what it may do, and the tool list never changes (the server declares no list_changed).</summary>
    public McpHostDecision Decide(string? clientName, string? clientVersion)
    {
        Interlocked.CompareExchange(ref _decision, (int)DecideFor(Mode, clientName, clientVersion), -1);
        return Decision;
    }

    /// <summary>Claude Code at or above the floor gets grants. Doubt about Claude Code (an older, missing or
    /// unparseable version) falls back to asking every call, never to trust, whatever the flag says. Any other host
    /// only reads unless its own registration passes --writes=any-host.</summary>
    public static McpHostDecision DecideFor(McpWriteMode mode, string? clientName, string? clientVersion)
    {
        if (mode == McpWriteMode.Off) return McpHostDecision.ReadOnly;
        if (string.Equals(clientName, Constants.McpClaudeCodeClientName, StringComparison.Ordinal))
            return TryParseVersion(clientVersion, out var v) && v >= Constants.McpClaudeCodeMinVersion
                ? McpHostDecision.Grants
                : McpHostDecision.MarkedPerCall;
        return mode == McpWriteMode.AnyHost ? McpHostDecision.Grants : McpHostDecision.ReadOnly;
    }

    // Digits and dots only: Version.TryParse alone takes a sign or spaces inside a part.
    [GeneratedRegex("^[0-9]{1,9}(\\.[0-9]{1,9}){1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionShape();

    private static bool TryParseVersion(string? text, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        string t = text?.Trim() ?? "";
        return VersionShape().IsMatch(t) && Version.TryParse(t, out version);
    }

    // ── Classes of command ──────────────────────────────────────────────────────────────────────────────

    /// <summary>The commands that change nothing in the game: the gate passes them with no capability.</summary>
    public static IReadOnlySet<string> ReadCommands => Reads;
    /// <summary>The resets that end only what this server holds.</summary>
    public static IReadOnlySet<string> ReleaseCommands => Resets;
    /// <summary>The writes no grant covers, asked before every call.</summary>
    public static IReadOnlySet<string> PerCallCommands => PerCall;

    public static IReadOnlySet<string> CommandsOf(McpGrantKind kind) => kind switch
    {
        McpGrantKind.Toggles => Toggles,
        McpGrantKind.Movement => Movement,
        _ => NoCommands,
    };

    public static McpGrantKind KindOf(string? cmd)
    {
        if (cmd is null) return McpGrantKind.None;
        if (Toggles.Contains(cmd)) return McpGrantKind.Toggles;
        return Movement.Contains(cmd) ? McpGrantKind.Movement : McpGrantKind.None;
    }

    /// <summary>A release is classed from the request body, not from the tool that built it: one of the explicit
    /// resets, or an ON/OFF command whose only state field is <c>enable</c> as a JSON false. The DLL reads a missing
    /// enable as false, so absence is never taken for a release.</summary>
    public static bool IsRelease(JsonObject request)
    {
        string? cmd = McpJson.GetString(request, "cmd");
        if (cmd is null) return false;
        if (Resets.Contains(cmd)) return true;
        if (!OffReleases.Contains(cmd)) return false;
        bool off = false;
        foreach (var (key, value) in request)
        {
            if (key == "enable")
            {
                if (value is not JsonValue v || v.GetValueKind() != JsonValueKind.False) return false;
                off = true;
            }
            else if (!EnvelopeKeys.Contains(key)) return false;
        }
        return off;
    }

    // ── Grants ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The grant tool that covers a kind, as the model must call it.</summary>
    public static string GrantToolFor(McpGrantKind kind) => kind switch
    {
        McpGrantKind.Toggles => Constants.McpGrantToolToggles,
        McpGrantKind.Movement => Constants.McpGrantToolMovement,
        _ => "",
    };

    /// <summary>What a kind covers, in the words a refusal and a grant reply use.</summary>
    public static string Covers(McpGrantKind kind) => kind switch
    {
        McpGrantKind.Toggles =>
            "god mode, the debug camera, the mouse cursor, time dilation, See-through and Keep Foreground",
        McpGrantKind.Movement =>
            "the movement knobs, gravity direction, Fly and teleport, which may kill or lose the player's pawn",
        _ => "",
    };

    /// <summary>Records a grant for the game run named in a get_pointers reply. A run with no real start time is
    /// refused: get_pointers sends "0" when GetProcessTimes failed, and a key of (pid, "0") would match every later
    /// run of a reused pid.</summary>
    public McpGrantRecord? Grant(McpGrantKind kind, JsonObject ptrs, out string? refusal)
    {
        if (Decision != McpHostDecision.Grants)
        {
            refusal = "Not granted: this session does not use grants.";
            return null;
        }
        if (kind == McpGrantKind.None)
        {
            refusal = "Not granted: no such grant.";
            return null;
        }
        if (!TryRun(ptrs, out long pid, out string creationTime, out refusal)) return null;
        if (creationTime == "0")
        {
            refusal = "Not granted: the game did not report when it started, so a grant could outlive this game run.";
            return null;
        }
        McpGrantRecord record;
        bool added = false;
        lock (_lock)
        {
            var entry = Find(kind, pid, creationTime);
            if (entry is null)
            {
                entry = new Entry
                {
                    Id = ++_nextId, Kind = kind, Pid = pid, CreationTime = creationTime, GrantedAtUtc = DateTime.UtcNow,
                };
                _grants.Add(entry);
                added = true;
            }
            record = entry.Snapshot();
        }
        if (added)
            _log?.Info(Constants.LogCatPipe, $"[MCP] Grant {record.Id}: {kind} for game {pid} (run {creationTime}).");
        refusal = null;
        return record;
    }

    /// <summary>Admits one write for the game run named in the get_pointers reply the tool already fetched; the tool
    /// passes the capability to the send, where the gate checks it again. Never read the link's connected pid here
    /// instead: the link may reconnect between that reply and the send, and the gate is what catches it.</summary>
    public McpGrantCapability? Admit(McpToolRole role, McpGrantKind kind, JsonObject ptrs, out string? refusal)
    {
        if (role is not (McpToolRole.GrantedWrite or McpToolRole.PerCallWrite))
        {
            refusal = "Not changed: this tool does not write.";
            return null;
        }
        var decision = Decision;
        if (decision == McpHostDecision.ReadOnly)
        {
            refusal = "Not changed: this session only reads.";
            return null;
        }
        if (!TryRun(ptrs, out long pid, out string creationTime, out refusal)) return null;
        if (role == McpToolRole.PerCallWrite) return new McpGrantCapability(0, McpGrantKind.None, pid, creationTime);
        if (kind == McpGrantKind.None)
        {
            refusal = "Not changed: this write has no grant kind.";
            return null;
        }
        if (decision == McpHostDecision.MarkedPerCall) return new McpGrantCapability(0, kind, pid, creationTime);

        long id;
        lock (_lock)
        {
            var entry = Find(kind, pid, creationTime);
            if (entry is null)
            {
                refusal = "Not changed: no grant covers this for this game. Call " + GrantToolFor(kind)
                    + " once the user has asked for this change; it covers " + Covers(kind)
                    + " until this game run or this session ends.";
                return null;
            }
            id = entry.Id;
            entry.Uses++;
        }
        _log?.Info(Constants.LogCatPipe, $"[MCP] Grant {id} admitted a {kind} write to game {pid}.");
        refusal = null;
        return new McpGrantCapability(id, kind, pid, creationTime);
    }

    /// <summary>Ends grants; a null kind or pid matches every one. It only narrows, and it never touches a hold.</summary>
    public int Revoke(McpGrantKind? kind = null, long? pid = null)
    {
        int removed;
        lock (_lock)
            removed = _grants.RemoveAll(e => (kind is null || e.Kind == kind) && (pid is null || e.Pid == pid));
        if (removed > 0)
            _log?.Info(Constants.LogCatPipe,
                $"[MCP] Revoked {removed} grant(s) (kind {kind?.ToString() ?? "any"}, game {pid?.ToString() ?? "any"}).");
        return removed;
    }

    public IReadOnlyList<McpGrantRecord> List()
    {
        lock (_lock) return _grants.Select(e => e.Snapshot()).ToList();
    }

    /// <summary>Ends every grant: the seam for the user's switch that blocks AI writes.</summary>
    public int Clear() => Revoke();

    private Entry? Find(McpGrantKind kind, long pid, string creationTime)
        => _grants.Find(e => e.Kind == kind && e.Pid == pid
            && string.Equals(e.CreationTime, creationTime, StringComparison.Ordinal));

    private static bool TryRun(JsonObject ptrs, out long pid, out string creationTime, out string? refusal)
    {
        creationTime = McpJson.GetString(ptrs, "process_creation_time") ?? "";
        if (!McpJson.TryGetLong(ptrs["pid"], out pid) || pid <= 0)
        {
            refusal = "Not changed: the game did not report its process id.";
            return false;
        }
        if (creationTime.Length == 0)
        {
            refusal = "Not changed: the game did not report when it started.";
            return false;
        }
        refusal = null;
        return true;
    }

    // ── The gate's check ────────────────────────────────────────────────────────────────────────────────

    /// <summary>The gate's verdict on one request about to be sent to the connected game run: null to send it, or why
    /// not. A read passes; on a host that writes, a release passes; anything else needs a capability for this run and
    /// this command, whose grant is still in the table, so a revoke that lands before the send wins. A command in no
    /// class is refused, so a future tool that forgets to ask for admission fails closed.</summary>
    public string? Check(JsonObject request, McpGrantCapability? cap, long connectedPid, string? connectedCreationTime)
    {
        string? cmd = McpJson.GetString(request, "cmd");
        if (cmd is null) return "the request names no command.";
        if (Reads.Contains(cmd)) return null;
        var decision = Decision;
        if (decision == McpHostDecision.ReadOnly) return "this session only reads.";
        if (IsRelease(request)) return null;

        var kind = KindOf(cmd);
        bool perCall = PerCall.Contains(cmd);
        if (kind == McpGrantKind.None && !perCall) return cmd + " is not a command this server sends.";
        if (cap is null) return cmd + " changes the game, and this call was not admitted.";
        if (cap.Pid != connectedPid
            || !string.Equals(cap.CreationTime, connectedCreationTime, StringComparison.Ordinal))
            return "the connection now reaches another game run than the one this call was admitted for.";
        string notCovered = "this call's admission does not cover " + cmd + ".";
        if (perCall) return cap.PerCall && cap.Kind == McpGrantKind.None ? null : notCovered;
        if (cap.Kind != kind) return notCovered;
        // A per-call admission stands in for a grant only where the host asks before every write.
        if (cap.PerCall) return decision == McpHostDecision.MarkedPerCall ? null : notCovered;
        lock (_lock)
        {
            var entry = Find(kind, cap.Pid, cap.CreationTime);
            return entry is not null && entry.Id == cap.GrantId ? null : "the grant for this was revoked.";
        }
    }
}
