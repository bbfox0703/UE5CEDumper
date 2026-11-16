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

/// <summary>A kind of grant. Two gameplay kinds and not one, so that "annoying" (god mode stays on, the game runs fast)
/// never quietly implies "may kill or lose the pawn". <see cref="Invoke"/> (Phase 1b) names one UFunction and
/// <see cref="Field"/> (Phase 2) one field of one class, each its grant's <see cref="McpGrantRecord.Target"/>.</summary>
public enum McpGrantKind { None, Toggles, Movement, Invoke, Field }

/// <summary>What a tool is to the grant gate: which hosts list it, whether the host is asked before each call, and
/// whether a call must be admitted. <see cref="GrantedInvoke"/> calls only a function an invoke grant names and
/// <see cref="GrantedField"/> writes only a field a field grant names, so only a grants host lists them.</summary>
public enum McpToolRole
{
    Read, Scan, GrantedWrite, PerCallWrite, Release, Grant, GrantAdmin, GrantedInvoke, GrantedField,
}

/// <summary>A grant as the session sees it: the game run it is for, how many calls it has let through, and for an
/// invoke grant the one function it names, by full name ("" for a kind grant).</summary>
public sealed record McpGrantRecord(long Id, McpGrantKind Kind, long Pid, string CreationTime, DateTime GrantedAtUtc,
    long Uses, string Target = "");

/// <summary>Proof that one call was admitted, for one game run. Only <see cref="McpGrants"/> makes one, and the gate
/// re-checks it at the send, so a capability cannot reach another game run or outlive a revoke.</summary>
public sealed class McpGrantCapability
{
    internal McpGrantCapability(long grantId, McpGrantKind kind, long pid, string creationTime, string target = "",
        string detail = "")
    {
        GrantId = grantId;
        Kind = kind;
        Pid = pid;
        CreationTime = creationTime;
        Target = target;
        Detail = detail;
    }

    /// <summary>The grant that admitted the call, or 0 for a call the host asked about on its own.</summary>
    public long GrantId { get; }
    public bool PerCall => GrantId == 0;
    public McpGrantKind Kind { get; }
    public long Pid { get; }
    public string CreationTime { get; }
    /// <summary>The function an invoke grant admitted the call for, by full name; "" otherwise.</summary>
    public string Target { get; }
    /// <summary>A field write's address and size, "0x...:N", which the gate holds the write_mem to; "" otherwise.
    /// </summary>
    public string Detail { get; }
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

    // The ON/OFF commands whose OFF ends a hold. fly_set also takes global settings, applied before enable, which no
    // release undoes, so IsRelease accepts an OFF only with nothing else in it. seethrough_set's OFF is one only with
    // own_claim: its explicit OFF drops every claim on See-through, the user's and CE's included.
    private static readonly HashSet<string> OffReleases = new(StringComparer.Ordinal)
    {
        "set_god_mode", "set_foreground_lock", "set_debug_camera", "fly_set", "seethrough_set",
    };

    private static readonly HashSet<string> NoCommands = new(StringComparer.Ordinal);

    private readonly ILoggingService? _log;
    private readonly object _lock = new();
    private readonly List<Entry> _grants = new();
    private long _nextId;
    private int _decision = -1;
    private readonly Func<Models.AiWriteBlockState>? _blockState;
    private long? _seenStamp;   // the block file's revoke stamp at the last look, under _lock

    /// <summary>Why a change is refused while the user blocks AI writes in the UE5DumpUI window. English, as every word
    /// the server sends: the --mcp process has no Avalonia application to look a translation up in.</summary>
    public const string BlockedReason =
        "the user turned on \"Block AI writes\" in the UE5DumpUI window, so no AI session may change the game until "
        + "they turn it off. Turning off or resetting what this server holds still works. This means stop: tell the "
        + "user, and do not try another route.";

    /// <summary>Why a change is refused while that switch's file cannot be read: blocked, but never claiming the user
    /// turned it on.</summary>
    public const string UnreadableReason =
        "the UE5DumpUI window's \"Block AI writes\" setting could not be read, so no change is allowed until it can be. "
        + "Turning off or resetting what this server holds still works.";

    private sealed class Entry
    {
        public required long Id { get; init; }
        public required McpGrantKind Kind { get; init; }
        public required long Pid { get; init; }
        public required string CreationTime { get; init; }
        public required DateTime GrantedAtUtc { get; init; }
        public string Target { get; init; } = "";
        public long Uses { get; set; }

        public McpGrantRecord Snapshot() => new(Id, Kind, Pid, CreationTime, GrantedAtUtc, Uses, Target);
    }

    public McpGrants(McpWriteMode mode = McpWriteMode.Default, ILoggingService? log = null,
        Func<Models.AiWriteBlockState>? blockState = null)
    {
        Mode = mode;
        _log = log;
        _blockState = blockState;
    }

    /// <summary>Reads the user's "Block AI writes" switch: null while changes are allowed, else why not. A revoke stamp
    /// other than the one seen last ends every grant, so a turn-on revokes even when the switch is off again by the next
    /// call; the first look revokes nothing, since no grant can be older than it.</summary>
    private string? LookAtBlock()
    {
        if (_blockState is null) return null;
        Models.AiWriteBlockState state;
        try
        {
            state = _blockState();
        }
        catch (Exception)
        {
            state = new Models.AiWriteBlockState(false, 0, true);   // a reader that failed blocks, as an unreadable file
        }
        int revoked = 0;
        if (!state.Unreadable)
        {
            lock (_lock)
            {
                if (_seenStamp is long seen && seen != state.RevokeStamp)
                {
                    revoked = _grants.Count;
                    _grants.Clear();
                }
                _seenStamp = state.RevokeStamp;
            }
        }
        if (revoked > 0)
            _log?.Info(Constants.LogCatPipe, $"[MCP] Block AI writes was turned on: {revoked} grant(s) revoked.");
        return state.Unreadable ? UnreadableReason : state.Blocked ? BlockedReason : null;
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

    /// <summary>Whether a host lists a tool of this role. tools/list and tools/call both ask this, so a model that
    /// remembers a write by name gets nothing from a host that does not list it.</summary>
    public static bool IsListed(McpToolRole role, McpHostDecision decision) => role switch
    {
        McpToolRole.Read or McpToolRole.Scan => true,
        McpToolRole.Grant or McpToolRole.GrantAdmin or McpToolRole.GrantedInvoke or McpToolRole.GrantedField
            => decision == McpHostDecision.Grants,
        _ => decision != McpHostDecision.ReadOnly,
    };

    /// <summary>Whether the host is asked before every call of a tool of this role (Claude Code's
    /// requiresUserInteraction marker). Under grants a grant and the per-call writes ask, and the writes a grant covers
    /// and the releases do not: the gate admits those. Asked per call, every write asks, as before grants.</summary>
    public static bool IsMarked(McpToolRole role, McpHostDecision decision) => decision switch
    {
        McpHostDecision.Grants => role is McpToolRole.Grant or McpToolRole.PerCallWrite,
        McpHostDecision.MarkedPerCall =>
            role is McpToolRole.GrantedWrite or McpToolRole.PerCallWrite or McpToolRole.Release,
        _ => false,
    };

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
    /// <summary>The writes no kind grant covers, asked before every call unless an invoke or field grant names the one
    /// function or field (<see cref="Check"/>).</summary>
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

    /// <summary>Whether a request has the shape of a release, classed from its body, not from the tool that built it:
    /// one of the explicit resets, or an ON/OFF command whose only state field is <c>enable</c> as a JSON false, either
    /// carrying <c>if_free</c> true, without which the DLL skips its holder check. The DLL reads a missing enable as
    /// false, so absence is never taken for a release. The shape is not enough on its own: the gate passes it only while
    /// this server holds what it ends (<see cref="ReleasedHold"/>, read from get_holds at the send), because god mode's
    /// OFF writes the normal value to every protection bool, whoever made the pawn invulnerable, and a reset writes its
    /// knob's base, whoever changed it.</summary>
    public static bool IsRelease(JsonObject request)
    {
        string? cmd = McpJson.GetString(request, "cmd");
        if (cmd is null) return false;
        if (request["if_free"] is not JsonValue f || f.GetValueKind() != JsonValueKind.True) return false;
        if (Resets.Contains(cmd)) return true;
        if (!OffReleases.Contains(cmd)) return false;
        bool seeThrough = cmd == "seethrough_set";
        bool off = false, ownClaim = false;
        foreach (var (key, value) in request)
        {
            if (key == "enable")
            {
                if (value is not JsonValue v || v.GetValueKind() != JsonValueKind.False) return false;
                off = true;
            }
            else if (key == "own_claim" && seeThrough)
            {
                if (value is not JsonValue o || o.GetValueKind() != JsonValueKind.True) return false;
                ownClaim = true;
            }
            else if (!EnvelopeKeys.Contains(key)) return false;
        }
        return off && (ownClaim || !seeThrough);
    }

    /// <summary>The hold a release would end, named as get_holds names it (the DLL's own mapping of a command to the
    /// holds it changes); null for a request that names none, such as a knob the DLL would refuse.</summary>
    public static string? ReleasedHold(JsonObject request)
    {
        string? Str(string key) => McpJson.GetString(request, key);
        switch (Str("cmd"))
        {
            case "set_god_mode": return Constants.HoldGodMode;
            case "set_foreground_lock": return Constants.HoldForegroundLock;
            case "set_debug_camera": return Constants.HoldDebugCamera;
            case "fly_set": return Constants.HoldFly;
            case "reset_gravity_direction": return Constants.HoldGravityDirection;
            case "reset_movement":
                return Str("knob") is "walk_speed" or "gravity" or "jump" ? Constants.HoldMovementPrefix + Str("knob") : null;
            case "reset_time_dilation":
                string? target = request.ContainsKey("target") ? Str("target") : "global";   // the DLL's default
                return target is "global" or "pawn" ? Constants.HoldTimePrefix + target : null;
            case "reset_field":
                string? cls = Str("class_name"), field = Str("field_name");
                return string.IsNullOrEmpty(cls) || string.IsNullOrEmpty(field)
                    ? null
                    : Constants.HoldForceFieldPrefix + cls + Constants.HoldForceFieldSeparator + field;
            default: return null;
        }
    }

    /// <summary>A release that touches only what this server holds by its very shape: See-through's own-claim OFF, which
    /// the DLL turns into dropping the caller's claim (nothing at all when it holds none). It needs no holds read.
    /// </summary>
    public static bool IsOwnClaimRelease(JsonObject request)
        => IsRelease(request) && McpJson.GetString(request, "cmd") == "seethrough_set";

    /// <summary>Whether get_holds says this server holds what <paramref name="request"/> ends: in force, or an ON of its
    /// own still on its way (the debug camera), with this client as the holder.</summary>
    private static bool Owns(JsonObject request, JsonObject? holds)
        => HolderOf(request, holds) is JsonObject holder && IsTrue(holder["you"]);

    /// <summary>Who holds what <paramref name="request"/> ends, when it is in force (or an ON is on its way); null when
    /// nobody does or get_holds does not say.</summary>
    private static JsonObject? HolderOf(JsonObject request, JsonObject? holds)
    {
        string? hold = ReleasedHold(request);
        if (hold is null || holds?["holds"] is not JsonArray arr) return null;
        foreach (var node in arr)
        {
            if (node is not JsonObject h || McpJson.GetString(h, "hold") != hold) continue;
            bool inForce = IsTrue(h["active"]) || IsTrue(h["pending"]);
            return inForce ? h["holder"] as JsonObject ?? new JsonObject() : null;
        }
        return null;
    }

    private static bool IsTrue(JsonNode? node) => node is JsonValue v && v.GetValueKind() == JsonValueKind.True;

    /// <summary>The grant kind a change needs, resets included: a reset of what this server does not hold changes the
    /// game like a set.</summary>
    private static McpGrantKind ChangeKind(string cmd) => cmd switch
    {
        "reset_movement" or "reset_gravity_direction" => McpGrantKind.Movement,
        "reset_time_dilation" => McpGrantKind.Toggles,
        _ => KindOf(cmd),
    };

    // ── Grants ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The grant tool that covers a kind, as the model must call it.</summary>
    public static string GrantToolFor(McpGrantKind kind) => kind switch
    {
        McpGrantKind.Toggles => Constants.McpGrantToolToggles,
        McpGrantKind.Movement => Constants.McpGrantToolMovement,
        McpGrantKind.Invoke => Constants.McpGrantToolInvoke,
        McpGrantKind.Field => Constants.McpGrantToolField,
        _ => "",
    };

    /// <summary>Whether a target names exactly one field of one class: the class's full path (walk_class' full_path,
    /// from the root, no colon), one colon, then the field's name, with what <see cref="IsFunctionFullName"/> refuses
    /// refused here too.</summary>
    public static bool IsFieldTarget(string? target)
    {
        if (string.IsNullOrEmpty(target) || target[0] != '/') return false;
        int colon = target.IndexOf(':');
        if (colon <= 1 || colon != target.LastIndexOf(':') || colon == target.Length - 1) return false;
        foreach (char c in target)
            if (char.IsControl(c) || c is ',' or ';' or '|' or '\\')
                return false;
        string field = target[(colon + 1)..];
        if (field.Trim().Length != field.Length || !field.Any(c => char.IsLetterOrDigit(c) || c == '_')) return false;
        return !target[..colon].Any(char.IsWhiteSpace);
    }

    /// <summary>A field write's capability detail: its address and size.</summary>
    public static string FieldDetail(ulong address, int size)
        => "0x" + address.ToString("X", System.Globalization.CultureInfo.InvariantCulture) + ":"
           + size.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool TryParseDetail(string detail, out ulong address, out int size)
    {
        address = 0;
        size = 0;
        int colon = detail.LastIndexOf(':');
        return colon > 0 && McpArgs.TryParseHex(detail[..colon], out address) && address != 0
               && int.TryParse(detail[(colon + 1)..], System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out size) && size > 0;
    }

    /// <summary>Whether a target names exactly one function in Ubel::GetFullName's form, as walk_functions sends it
    /// (<c>//Script/Engine/Actor.SetActorHiddenInGame</c>): a path from the root, a dot, then the function. The DLL
    /// compares full names ordinally, so no character is a wildcard; a Blueprint function may hold a space, '?' or '*'
    /// ("Open Door"), which Blueprint names allow. Refused: a control character, and the characters no Blueprint name
    /// may hold that read as a list (',', ';', '|', a backslash); and a function part with no letter, digit or
    /// underscore, or
    /// with blanks around it.</summary>
    public static bool IsFunctionFullName(string? target)
    {
        if (string.IsNullOrEmpty(target) || target[0] != '/') return false;
        int dot = target.LastIndexOf('.');
        if (dot <= 1 || dot == target.Length - 1) return false;
        foreach (char c in target)
            if (char.IsControl(c) || c is ',' or ';' or '|' or '\\')
                return false;
        string function = target[(dot + 1)..];
        if (function.Trim().Length != function.Length || !function.Any(c => char.IsLetterOrDigit(c) || c == '_'))
            return false;
        return target.IndexOf('/', dot) < 0 && !target[..dot].Any(char.IsWhiteSpace);
    }

    /// <summary>The function's own name: what follows the last dot of its full name.</summary>
    public static string FunctionNameOf(string fullName) => fullName[(fullName.LastIndexOf('.') + 1)..];

    /// <summary>What a kind covers, in the words a refusal and a grant reply use.</summary>
    public static string Covers(McpGrantKind kind) => kind switch
    {
        McpGrantKind.Toggles =>
            "god mode, the debug camera, the mouse cursor, time dilation, See-through and Keep Foreground",
        McpGrantKind.Movement =>
            "the movement knobs, gravity direction, Fly and teleport, which may kill or lose the player's pawn",
        McpGrantKind.Invoke => "one UFunction, on any object, with any arguments",
        McpGrantKind.Field => "one field of one class, on every instance of it and its subclasses",
        _ => "",
    };

    /// <summary>Records a grant for the game run named in a get_pointers reply. A run with no real start time is
    /// refused: get_pointers sends "0" when GetProcessTimes failed, and a key of (pid, "0") would match every later
    /// run of a reused pid.</summary>
    public McpGrantRecord? Grant(McpGrantKind kind, JsonObject ptrs, out string? refusal, string target = "")
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
        // An invoke grant names exactly one function, a field grant one class field; a kind grant names none.
        bool targetOk = kind switch
        {
            McpGrantKind.Invoke => IsFunctionFullName(target),
            McpGrantKind.Field => IsFieldTarget(target),
            _ => target.Length == 0,
        };
        if (!targetOk)
        {
            refusal = kind switch
            {
                McpGrantKind.Invoke =>
                    "Not granted: name exactly one function by its full name, as read_functions shows it in 'full'.",
                McpGrantKind.Field => "Not granted: name exactly one field and its class's full path.",
                _ => "Not granted: this grant names no function or field.",
            };
            return null;
        }
        if (LookAtBlock() is { } blocked)
        {
            refusal = "Not granted: " + blocked;
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
            var entry = Find(kind, pid, creationTime, target);
            if (entry is null)
            {
                entry = new Entry
                {
                    Id = ++_nextId, Kind = kind, Pid = pid, CreationTime = creationTime, GrantedAtUtc = DateTime.UtcNow,
                    Target = target,
                };
                _grants.Add(entry);
                added = true;
            }
            record = entry.Snapshot();
        }
        if (added)
            _log?.Info(Constants.LogCatPipe, $"[MCP] Grant {record.Id}: {kind} for game {pid} (run {creationTime})"
                + (target.Length > 0 ? $": {target}." : "."));
        refusal = null;
        return record;
    }

    /// <summary>Admits one write for the game run named in the get_pointers reply the tool already fetched; the tool
    /// passes the capability to the send, where the gate checks it again. Never read the link's connected pid here
    /// instead: the link may reconnect between that reply and the send, and the gate is what catches it.</summary>
    public McpGrantCapability? Admit(McpToolRole role, McpGrantKind kind, JsonObject ptrs, out string? refusal,
        string target = "", string detail = "")
    {
        if (role is not (McpToolRole.GrantedWrite or McpToolRole.PerCallWrite or McpToolRole.GrantedInvoke
            or McpToolRole.GrantedField))
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
        if (LookAtBlock() is { } blocked)
        {
            refusal = "Not changed: " + blocked;
            return null;
        }
        if (!TryRun(ptrs, out long pid, out string creationTime, out refusal)) return null;
        if (role == McpToolRole.PerCallWrite) return new McpGrantCapability(0, McpGrantKind.None, pid, creationTime);
        if (role == McpToolRole.GrantedInvoke) return AdmitInvoke(decision, kind, pid, creationTime, target, out refusal);
        if (role == McpToolRole.GrantedField)
            return AdmitField(decision, kind, pid, creationTime, target, detail, out refusal);
        if (kind is McpGrantKind.None or McpGrantKind.Invoke or McpGrantKind.Field)
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
        }
        _log?.Info(Constants.LogCatPipe, $"[MCP] Grant {id} admitted a {kind} write to game {pid}.");
        refusal = null;
        return new McpGrantCapability(id, kind, pid, creationTime);
    }

    // Only under grants, only for an invoke grant on the very function resolved: there is no per-call stand-in for this
    // role, since write_invoke is the per-call route.
    private McpGrantCapability? AdmitInvoke(McpHostDecision decision, McpGrantKind kind, long pid, string creationTime,
        string target, out string? refusal)
    {
        if (decision != McpHostDecision.Grants || kind != McpGrantKind.Invoke || !IsFunctionFullName(target))
        {
            refusal = "Not invoked: this call needs an invoke grant; write_invoke asks the user every call instead.";
            return null;
        }
        long id;
        lock (_lock)
        {
            var entry = Find(McpGrantKind.Invoke, pid, creationTime, target);
            if (entry is null)
            {
                refusal = "Not invoked: no grant covers " + target + " for this game. Call " + Constants.McpGrantToolInvoke
                    + " with this function and its class once the user has asked for it, or use write_invoke, asked "
                    + "every call.";
                return null;
            }
            id = entry.Id;
        }
        _log?.Info(Constants.LogCatPipe, $"[MCP] Grant {id} admitted a call of {target} in game {pid}.");
        refusal = null;
        return new McpGrantCapability(id, McpGrantKind.Invoke, pid, creationTime, target);
    }

    // Only under grants, only for a field grant on the very class field the write resolved, and only with the address
    // and size it computed, which the gate holds the write_mem to.
    private McpGrantCapability? AdmitField(McpHostDecision decision, McpGrantKind kind, long pid, string creationTime,
        string target, string detail, out string? refusal)
    {
        if (decision != McpHostDecision.Grants || kind != McpGrantKind.Field || !IsFieldTarget(target)
            || !TryParseDetail(detail, out _, out _))
        {
            refusal = "Not changed: this write needs a field grant; write_memory asks the user every call instead.";
            return null;
        }
        long id;
        lock (_lock)
        {
            var entry = Find(McpGrantKind.Field, pid, creationTime, target);
            if (entry is null)
            {
                refusal = "Not changed: no grant covers " + target + " for this game. Call " + Constants.McpGrantToolField
                    + " with this field and its class once the user has asked for it, or use write_memory, asked every "
                    + "call.";
                return null;
            }
            id = entry.Id;
        }
        _log?.Info(Constants.LogCatPipe, $"[MCP] Grant {id} admitted a write of {target} at {detail} in game {pid}.");
        refusal = null;
        return new McpGrantCapability(id, McpGrantKind.Field, pid, creationTime, target, detail);
    }

    /// <summary>Ends grants; a null kind, pid or target matches every one. It only narrows, and it never touches a hold.
    /// </summary>
    public int Revoke(McpGrantKind? kind = null, long? pid = null, string? target = null)
    {
        int removed;
        lock (_lock)
            removed = _grants.RemoveAll(e => (kind is null || e.Kind == kind) && (pid is null || e.Pid == pid)
                && (target is null || string.Equals(e.Target, target, StringComparison.Ordinal)));
        if (removed > 0)
            _log?.Info(Constants.LogCatPipe,
                $"[MCP] Revoked {removed} grant(s) (kind {kind?.ToString() ?? "any"}, game {pid?.ToString() ?? "any"}).");
        return removed;
    }

    public IReadOnlyList<McpGrantRecord> List()
    {
        LookAtBlock();   // a turn-on since the last look ends the grants before they are listed
        lock (_lock) return _grants.Select(e => e.Snapshot()).ToList();
    }

    /// <summary>The fields this game run's field grants name, which write_field resolves an instance's class chain
    /// against. A grant of another run (an earlier one of a reused pid, another game) would end the walk at a class whose
    /// admission, keyed on this run, then fails.
    /// <para>Under the user's block, <paramref name="blocked"/> says so and the set is empty: the block ended every grant,
    /// and "no grant covers it" would send the model after a grant or write_memory, which the block forbids.</para>
    /// </summary>
    public IReadOnlySet<string> FieldTargets(JsonObject ptrs, out string? blocked)
    {
        blocked = LookAtBlock();
        if (blocked is not null || !TryRun(ptrs, out long pid, out string creationTime, out _))
            return new HashSet<string>();
        lock (_lock)
            return _grants.Where(e => e.Kind == McpGrantKind.Field && e.Pid == pid
                                      && string.Equals(e.CreationTime, creationTime, StringComparison.Ordinal))
                .Select(e => e.Target).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Ends every grant: the seam for the user's switch that blocks AI writes.</summary>
    public int Clear() => Revoke();

    private Entry? Find(McpGrantKind kind, long pid, string creationTime, string target = "")
        => _grants.Find(e => e.Kind == kind && e.Pid == pid
            && string.Equals(e.CreationTime, creationTime, StringComparison.Ordinal)
            && string.Equals(e.Target, target, StringComparison.Ordinal));

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
    /// not. A read passes; on a host that writes, a release of what this server holds (<paramref name="holds"/>, the
    /// get_holds reply read right before) passes; anything else needs a capability for this run and this command, whose
    /// grant is still in the table, so a revoke that lands before the send wins. A command in no class is refused, so a
    /// future tool that forgets to ask for admission fails closed.</summary>
    public string? Check(JsonObject request, McpGrantCapability? cap, long connectedPid, string? connectedCreationTime,
        JsonObject? holds = null)
    {
        string? cmd = McpJson.GetString(request, "cmd");
        if (cmd is null) return "the request names no command.";
        if (Reads.Contains(cmd)) return null;
        var decision = Decision;
        if (decision == McpHostDecision.ReadOnly) return "this session only reads.";
        string? blocked = LookAtBlock();
        bool releaseShape = IsRelease(request);
        if (releaseShape && (IsOwnClaimRelease(request) || Owns(request, holds))) return null;
        // An AI can still clean up what it holds; anything else waits for the user to lift the block.
        if (blocked is not null) return blocked;
        // Someone else's hold: the DLL's if_free would refuse it whatever the admission, so say whose it is, as that
        // refusal does, instead of asking for a grant.
        if (releaseShape && HolderOf(request, holds) is JsonObject other)
            return ReleasedHold(request) + " is held by " + McpWriteTools.DescribeHolder(other) + ". This server never "
                   + "replaces or ends what someone else holds; the user can release it where it was set.";

        var kind = ChangeKind(cmd);
        bool perCall = PerCall.Contains(cmd);
        string notOurs = releaseShape
            ? "this server does not hold " + (ReleasedHold(request) ?? cmd) + ", so ending it is no release: "
            : "";
        if (kind == McpGrantKind.None && !perCall)
            return releaseShape ? notOurs + "it is never ended through this server." : cmd + " is not a command this server sends.";
        if (cap is null)
            return notOurs + cmd + " changes the game"
                   + (kind != McpGrantKind.None ? " and needs " + GrantToolFor(kind) + "." : ", and this call was not admitted.");
        if (cap.Pid != connectedPid
            || !string.Equals(cap.CreationTime, connectedCreationTime, StringComparison.Ordinal))
            return "the connection now reaches another game run than the one this call was admitted for.";
        string notCovered = "this call's admission does not cover " + cmd + ".";
        if (perCall)
        {
            if (cmd == "invoke_function" && cap.Kind == McpGrantKind.Invoke && !cap.PerCall)
                return CheckGrantedInvoke(request, cap, decision);
            if (cmd == "write_mem" && cap.Kind == McpGrantKind.Field && !cap.PerCall)
                return CheckGrantedField(request, cap, decision);
            return cap.PerCall && cap.Kind == McpGrantKind.None ? null : notCovered;
        }
        if (cap.Kind != kind) return notCovered;
        // A per-call admission stands in for a grant only where the host asks before every write.
        if (cap.PerCall) return decision == McpHostDecision.MarkedPerCall ? null : notCovered;
        lock (_lock)
        {
            var entry = Find(kind, cap.Pid, cap.CreationTime);
            if (entry is null || entry.Id != cap.GrantId) return "the grant for this was revoked.";
            // Counted here, where the grant is what lets the call through: an admission never sent, or an OFF of the
            // server's own hold (passed above on that), is not the grant's doing.
            entry.Uses++;
            return null;
        }
    }

    /// <summary>A write under a field grant: write_mem at exactly the address and size the tool computed for the
    /// granted field, so the capability cannot carry another write.</summary>
    private string? CheckGrantedField(JsonObject request, McpGrantCapability cap, McpHostDecision decision)
    {
        if (decision != McpHostDecision.Grants) return "this session does not use grants.";
        if (!TryParseDetail(cap.Detail, out ulong address, out int size)
            || !McpArgs.TryParseHex(McpJson.GetString(request, "addr"), out ulong addr) || addr != address
            || (McpJson.GetString(request, "bytes") ?? "").Length != size * 2)
            return "this write is not to " + cap.Target + " where its admission placed it.";
        lock (_lock)
        {
            var entry = Find(McpGrantKind.Field, cap.Pid, cap.CreationTime, cap.Target);
            if (entry is null || entry.Id != cap.GrantId) return "the grant for this was revoked.";
            entry.Uses++;
            return null;
        }
    }

    /// <summary>A call under an invoke grant: the request must be in the DLL's grant mode, which checks the function it
    /// resolves against expect_func_full -- the gate sees only the short name the DLL resolves -- and name the grant's
    /// own function both ways.</summary>
    private string? CheckGrantedInvoke(JsonObject request, McpGrantCapability cap, McpHostDecision decision)
    {
        if (decision != McpHostDecision.Grants) return "this session does not use grants.";
        if (!IsTrue(request["grant"]))
            return "a call under an invoke grant must ask the DLL to check it (grant).";
        if (!string.Equals(McpJson.GetString(request, "expect_func_full"), cap.Target, StringComparison.Ordinal)
            || !string.Equals(McpJson.GetString(request, "func_name"), FunctionNameOf(cap.Target),
                StringComparison.Ordinal))
            return "this call is not to " + cap.Target + ", the function its grant names.";
        lock (_lock)
        {
            var entry = Find(McpGrantKind.Invoke, cap.Pid, cap.CreationTime, cap.Target);
            if (entry is null || entry.Id != cap.GrantId) return "the grant for this was revoked.";
            entry.Uses++;
            return null;
        }
    }
}
