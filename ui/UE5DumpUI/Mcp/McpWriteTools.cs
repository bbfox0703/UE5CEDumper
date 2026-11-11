using System.Globalization;
using System.Text.Json.Nodes;

namespace UE5DumpUI.Mcp;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D2-2. The tools that change the game: the gameplay toggles, the force field and
/// teleport, and the reads an AI needs around them. The maintainer's D2 decisions (2026-11-11) shape every one:
///
/// <para><b>Never over the user.</b> Each request that changes a hold carries <c>if_free</c>: the DLL refuses it, under
/// the hold's own lock, when anyone else -- the UI, a Cheat Engine record, another AI session, or nobody recorded --
/// holds it (D2-1, build 3704). The refusal comes back as a tool error naming the holder. Nothing here ever sends
/// <c>reset_all_fields</c> or touches the marker slots, and a teleport keeps the user's undo point
/// (<c>keep_undo</c>).</para>
///
/// <para><b>Asked every time.</b> Every tool here is named <c>write_*</c> and carries
/// <c>requiresUserInteraction</c>: Claude Code asks before each call in every permission mode.</para>
///
/// <para><b>Ends with the session.</b> The server marks its requests background, so the DLL releases what it holds when
/// the server exits.</para>
///
/// <para><b>The experimental opt-in binds the AI too.</b> Fly, See-through, Keep Foreground and the force field refuse
/// while the UI's experimental switch is off: an AI-started feature would otherwise run behind a card the user cannot
/// see. The switch is read on every call, so the UI's change takes effect at once.</para>
///
/// <para><b>A DLL older than <see cref="Constants.McpMinDllBuildForWrites"/></b> ignores if_free and keep_undo, so a
/// write there could replace what the user holds: every write tool refuses it.</para>
/// </summary>
internal static class McpWriteTools
{
    private const int WriteSeconds = 30;

    private static readonly (string, JsonObject, bool) PidProp =
        ("pid", McpSchema.Int("Game process id. Needed only when several games run the dumper (read_games lists "
                              + "them); otherwise the only game is used.", 1, int.MaxValue), false);

    public static IEnumerable<McpTool> Create(IMcpGame game, Func<bool> experimentalEnabled)
    {
        // ── Reads around the writes ─────────────────────────────────────────────────────────────────────

        yield return Read(game, "read_holds", "Who holds what",
            "Every gameplay hold the DLL records an owner for (god mode, movement knobs, gravity direction, time "
            + "dilation, fly, Keep Foreground, debug camera, each force-field job): whether it is in force and who "
            + "holds it (\"you\": this server; a client pid; CE; none). A write_* tool changes only a hold that is free "
            + "or already this server's.",
            [], "get_holds", needsScan: false);
        yield return Read(game, "read_pose", "Player pose",
            "The player pawn's location (x, y, z), rotation (pitch, yaw, roll), the map, and the pawn's address.",
            [], "teleport_get_pose");
        yield return Read(game, "read_movement", "Movement knobs",
            "Walk speed, gravity scale and jump velocity of the player's movement component: current value, captured "
            + "base, multiplier, whether a hold is active; and gravity direction (UE 5.3+).",
            [], "get_movement_params");
        yield return Read(game, "read_time_dilation", "Time dilation",
            "World time dilation (global) and the player pawn's own (pawn): current value, natural value, held value, "
            + "whether a hold is active.",
            [], "get_time_state");
        yield return Read(game, "read_forced_fields", "Forced fields",
            "The force-field jobs: each holds a field on every live instance of a class and its subclasses.",
            [], "get_forced_fields");

        // ── Toggles and holds ───────────────────────────────────────────────────────────────────────────

        yield return Write(game, experimentalEnabled, "write_god_mode", "God mode",
            "Turns god mode on or off: forces the player pawn's bCanBeDamaged to false and holds it. Refused when the "
            + "UI, a Cheat Engine record or another session holds god mode.",
            [("enable", McpSchema.Bool("On or off.", false), true)],
            (a, r) => r["enable"] = ReqBool(a, "enable"), "set_god_mode");

        yield return Write(game, experimentalEnabled, "write_movement", "Movement knob",
            "Holds a movement knob of the player's movement component at its captured base times a multiplier "
            + "(walk_speed, gravity scale, jump), or resets it to the base. Refused when someone else holds that knob.",
            [("knob", McpSchema.Enum("Which knob.", "walk_speed", "gravity", "jump"), true),
             ("multiplier", McpSchema.Number("Times the base, e.g. 1.5. Leave out with reset."), false),
             ("reset", McpSchema.Bool("Reset the knob to its base instead.", false), false)],
            (a, r) =>
            {
                string knob = a.ReqStr("knob");
                if (knob is not ("walk_speed" or "gravity" or "jump"))
                    throw new McpToolError("'knob' must be walk_speed, gravity or jump.");
                r["knob"] = knob;
                if (a.Bool("reset", false))
                {
                    r["cmd"] = "reset_movement";
                    return;
                }
                double m = a.ReqDouble("multiplier");
                if (m <= 0) throw new McpToolError("'multiplier' must be above 0.");
                r["multiplier"] = m;
            }, "set_movement_multiplier");

        yield return Write(game, experimentalEnabled, "write_gravity_direction", "Gravity direction",
            "Holds the player's gravity direction (UE 5.3+) at a vector, or resets it. Refused when someone else holds "
            + "it. A sideways or upward vector can carry the pawn off the map; some games do not respawn it.",
            [("x", McpSchema.Number("X of the direction."), false),
             ("y", McpSchema.Number("Y of the direction."), false),
             ("z", McpSchema.Number("Z of the direction (normal gravity is 0, 0, -1)."), false),
             ("reset", McpSchema.Bool("Reset to the game's own gravity instead.", false), false)],
            (a, r) =>
            {
                if (a.Bool("reset", false))
                {
                    r["cmd"] = "reset_gravity_direction";
                    return;
                }
                r["x"] = a.ReqDouble("x");
                r["y"] = a.ReqDouble("y");
                r["z"] = a.ReqDouble("z");
            }, "set_gravity_direction");

        yield return Write(game, experimentalEnabled, "write_time_dilation", "Time dilation",
            "Holds world time dilation (target global) or the player pawn's own (target pawn) at a value, or resets it "
            + "to its natural value. 0.5 is half speed. Refused when someone else holds that lane.",
            [("target", McpSchema.Enum("Which lane.", "global", "pawn"), true),
             ("value", McpSchema.Number("The dilation, e.g. 0.5. Leave out with reset."), false),
             ("reset", McpSchema.Bool("Reset the lane instead.", false), false)],
            (a, r) =>
            {
                string target = a.ReqStr("target");
                if (target is not ("global" or "pawn")) throw new McpToolError("'target' must be global or pawn.");
                r["target"] = target;
                if (a.Bool("reset", false))
                {
                    r["cmd"] = "reset_time_dilation";
                    return;
                }
                double v = a.ReqDouble("value");
                if (v <= 0) throw new McpToolError("'value' must be above 0.");
                r["value"] = v;
            }, "set_time_dilation");

        yield return Write(game, experimentalEnabled, "write_debug_camera", "Debug camera",
            "Turns the engine's debug camera on or off (needs a live CheatManager, which UE 5 leaves out of most "
            + "Shipping games). Runs on the game thread; an answer of -5 means the toggle is queued and will still run: "
            + "do not send it again.",
            [("enable", McpSchema.Bool("On or off.", false), true)],
            (a, r) => r["enable"] = ReqBool(a, "enable"), "set_debug_camera");

        yield return Write(game, experimentalEnabled, "write_mouse_cursor", "Mouse cursor",
            "Shows or hides the game's mouse cursor (PlayerController.bShowMouseCursor). A one-time change the game may "
            + "set back; nothing holds it.",
            [("show", McpSchema.Bool("Show or hide.", true), true)],
            (a, r) => r["show"] = ReqBool(a, "show"), "set_mouse_cursor", ifFree: false);

        yield return Write(game, experimentalEnabled, "write_fly", "Fly",
            "Experimental. Turns flying on or off and sets its speed, key preset (0 WASD, 1 numpad, 2 arrows) and "
            + "noclip. Refused when someone else holds fly, or while the UI's experimental features are off.",
            [("enable", McpSchema.Bool("On or off.", false), false),
             ("speed", McpSchema.Number("Speed in units per second."), false),
             ("preset", McpSchema.Int("Key preset.", 0, 2), false),
             ("noclip", McpSchema.Bool("Through walls.", false), false)],
            (a, r) =>
            {
                if (!(a.Has("enable") || a.Has("speed") || a.Has("preset") || a.Has("noclip")))
                    throw new McpToolError("Pass at least one of enable, speed, preset, noclip.");
                if (a.Has("enable")) r["enable"] = a.Bool("enable", false);
                if (a.Has("speed")) r["speed"] = a.ReqDouble("speed");
                if (a.Has("preset")) r["preset"] = a.Int("preset", 0, 0, 2);
                if (a.Has("noclip")) r["noclip"] = a.Bool("noclip", false);
            }, "fly_set", experimental: true);

        yield return Write(game, experimentalEnabled, "write_foreground_lock", "Keep Foreground",
            "Experimental. Makes the game believe it is the foreground window, so it keeps running (and game-thread "
            + "work keeps answering) while another window has the focus. Refused when someone else holds it, or while "
            + "the UI's experimental features are off.",
            [("enable", McpSchema.Bool("On or off.", false), true)],
            (a, r) => r["enable"] = ReqBool(a, "enable"), "set_foreground_lock", experimental: true);

        yield return SeeThrough(game, experimentalEnabled);

        yield return Write(game, experimentalEnabled, "write_force_field", "Force a field",
            "Experimental. Holds a field at a value on every live instance of a class and its subclasses: kind bool "
            + "(on) or numeric (value). Refused when someone else holds that class and field, or while the UI's "
            + "experimental features are off. Clearing an object pointer is not offered: it cannot be undone.",
            [("class_name", McpSchema.Str("The class, e.g. \"BP_Enemy_C\"."), true),
             ("field_name", McpSchema.Str("The field, e.g. \"bIsInvulnerable\"."), true),
             ("kind", McpSchema.Enum("bool or numeric.", "bool", "numeric"), true),
             ("on", McpSchema.Bool("For kind bool: the value to hold.", false), false),
             ("value", McpSchema.Number("For kind numeric: the value to hold."), false)],
            (a, r) =>
            {
                r["class_name"] = a.ReqStr("class_name");
                r["field_name"] = a.ReqStr("field_name");
                string kind = a.ReqStr("kind");
                if (kind is not ("bool" or "numeric"))
                    throw new McpToolError("'kind' must be bool or numeric (clearing an object pointer is not offered).");
                r["kind"] = kind;
                if (kind == "bool") r["on"] = a.Bool("on", false);
                else r["value"] = a.ReqDouble("value");
            }, "force_field", experimental: true, timeoutSeconds: 90);

        yield return Write(game, experimentalEnabled, "write_force_field_reset", "Release a forced field",
            "Experimental. Releases one force-field job this server holds, restoring the captured value. Refused for "
            + "a job someone else holds.",
            [("class_name", McpSchema.Str("The class."), true),
             ("field_name", McpSchema.Str("The field."), true)],
            (a, r) =>
            {
                r["class_name"] = a.ReqStr("class_name");
                r["field_name"] = a.ReqStr("field_name");
            }, "reset_field", experimental: true);

        yield return Teleport(game, experimentalEnabled);
    }

    // ── Builders ────────────────────────────────────────────────────────────────────────────────────────

    private static McpTool Read(IMcpGame game, string name, string title, string description,
        (string, JsonObject, bool)[] props, string cmd, bool needsScan = true)
    {
        var all = new List<(string, JsonObject, bool)>(props) { PidProp };
        return new McpTool(name, title, description, McpSchema.Object([.. all]), ReadOnly: true,
            async (a, ct) =>
            {
                var reply = await game.SendAsync(a.OptInt("pid"), new JsonObject { ["cmd"] = cmd },
                    TimeSpan.FromSeconds(WriteSeconds), ct, requireScan: needsScan);
                if (!Truthy(reply, "ok")) return DllError(reply, game.TakeNotice());
                McpTools.Strip(reply);
                return Combine(new McpToolResult(reply), game.TakeNotice());
            });
    }

    /// <summary>A tool that is one DLL command changing the game. <paramref name="fill"/> may name another command
    /// (a reset) by setting <c>cmd</c>.</summary>
    private static McpTool Write(IMcpGame game, Func<bool> experimentalEnabled, string name, string title,
        string description, (string, JsonObject, bool)[] props, Action<McpArgs, JsonObject> fill, string cmd,
        bool experimental = false, bool ifFree = true, int timeoutSeconds = WriteSeconds)
    {
        var all = new List<(string, JsonObject, bool)>(props) { PidProp };
        return new McpTool(name, title, description, McpSchema.Object([.. all]), ReadOnly: false,
            async (a, ct) =>
            {
                int? pid = a.OptInt("pid");
                if (experimental) RequireExperimental(experimentalEnabled, title);
                var request = new JsonObject { ["cmd"] = cmd };
                fill(a, request);
                if (ifFree) request["if_free"] = true;
                await RequireWritableDllAsync(game, pid, ct);
                var reply = await game.SendAsync(pid, request, TimeSpan.FromSeconds(timeoutSeconds), ct,
                    requireScan: true);
                return Answer(reply, game.TakeNotice());
            }, Idempotent: true, Destructive: true, RequiresInteraction: true);
    }

    /// <summary>See-through has its own owner model (Stage C): each client that turned it on holds a claim, released when
    /// it leaves, but an explicit OFF drops every claim. So this server turns it off only when its own claim is the only
    /// one; otherwise its claim simply ends with the session.</summary>
    private static McpTool SeeThrough(IMcpGame game, Func<bool> experimentalEnabled)
    {
        var claimedBy = new HashSet<int>();   // the games where this server turned it on
        return new McpTool("write_see_through", "See-through",
            "Experimental. Hides the occluders between the camera and the player pawn (count: how many nearest), or "
            + "turns that off. Turning it off is refused while the UI or Cheat Engine also has it on: an OFF would end "
            + "theirs too. Refused while the UI's experimental features are off.",
            McpSchema.Object(("enable", McpSchema.Bool("On or off.", false), true),
                ("count", McpSchema.Int("Occluders to hide.", 1, 16), false), PidProp),
            ReadOnly: false,
            async (a, ct) =>
            {
                int? pid = a.OptInt("pid");
                RequireExperimental(experimentalEnabled, "See-through");
                bool enable = ReqBool(a, "enable");
                await RequireWritableDllAsync(game, pid, ct);
                var timeout = TimeSpan.FromSeconds(WriteSeconds);
                if (!enable)
                {
                    var state = await game.SendAsync(pid, new JsonObject { ["cmd"] = "seethrough_get_state" }, timeout,
                        ct, requireScan: true);
                    bool active = Truthy(state, "active");
                    long claims = McpJson.TryGetLong(state["claims"], out long c) ? c : 0;
                    bool ours = claimedBy.Contains(game.ConnectedPid);
                    if (active && (!ours || claims > 1 || Truthy(state, "ce_claimed")))
                        return Combine(McpToolResult.Fail(
                            "Not changed: See-through is also on for the UE5DumpUI window or a Cheat Engine record "
                            + "(" + claims.ToString(CultureInfo.InvariantCulture) + " claim(s)), and an OFF would end "
                            + "theirs too. This server's own claim ends with the session."), game.TakeNotice());
                }
                var request = new JsonObject { ["cmd"] = "seethrough_set", ["enable"] = enable };
                if (a.Has("count")) request["count"] = a.Int("count", 1, 1, 16);
                var reply = await game.SendAsync(pid, request, timeout, ct, requireScan: true);
                var result = Answer(reply, game.TakeNotice());
                if (!result.IsError)
                {
                    if (enable) claimedBy.Add(game.ConnectedPid);
                    else claimedBy.Remove(game.ConnectedPid);
                }
                return result;
            }, Idempotent: true, Destructive: true, RequiresInteraction: true);
    }

    /// <summary>Teleport to explicit coordinates only: never a marker slot (they are the user's), and with
    /// <c>keep_undo</c>, so the user's "Recall last" still returns where the user was. The pose before the jump comes
    /// back in <c>from</c>, so the AI can return on its own.</summary>
    private static McpTool Teleport(IMcpGame game, Func<bool> experimentalEnabled) => new(
        "write_teleport", "Teleport",
        "Moves the player pawn to explicit world coordinates (and rotation, when given). Returns the pose it left in "
        + "\"from\", to return to. Leaves the user's marker slots and \"Recall last\" alone. A point in the air or the "
        + "void can kill the pawn; some games do not respawn it.",
        McpSchema.Object(("x", McpSchema.Number("World X."), true), ("y", McpSchema.Number("World Y."), true),
            ("z", McpSchema.Number("World Z."), true), ("pitch", McpSchema.Number("Pitch, degrees."), false),
            ("yaw", McpSchema.Number("Yaw, degrees."), false), ("roll", McpSchema.Number("Roll, degrees."), false),
            PidProp),
        ReadOnly: false,
        async (a, ct) =>
        {
            int? pid = a.OptInt("pid");
            var request = new JsonObject
            {
                ["cmd"] = "teleport_recall_marker",
                ["x"] = a.ReqDouble("x"),
                ["y"] = a.ReqDouble("y"),
                ["z"] = a.ReqDouble("z"),
                ["keep_undo"] = true,
            };
            foreach (var key in new[] { "pitch", "yaw", "roll" })
                if (a.OptDouble(key) is double v) request[key] = v;
            await RequireWritableDllAsync(game, pid, ct);
            var timeout = TimeSpan.FromSeconds(WriteSeconds);
            var from = await game.SendAsync(pid, new JsonObject { ["cmd"] = "teleport_get_pose" }, timeout, ct,
                requireScan: true);
            var reply = await game.SendAsync(pid, request, timeout, ct, requireScan: true);
            var result = Answer(reply, game.TakeNotice());
            if (!result.IsError && result.Payload is JsonObject payload && Truthy(from, "ok"))
            {
                var pose = new JsonObject();
                foreach (var key in new[] { "x", "y", "z", "pitch", "yaw", "roll", "map" })
                    if (from[key] is { } node) pose[key] = node.DeepClone();
                payload["from"] = pose;
            }
            return result;
        }, Idempotent: true, Destructive: true, RequiresInteraction: true);

    // ── Guards and replies ──────────────────────────────────────────────────────────────────────────────

    private static void RequireExperimental(Func<bool> experimentalEnabled, string feature)
    {
        if (!experimentalEnabled())
            throw new McpToolError($"{feature} is an experimental feature, and the UE5DumpUI window's experimental "
                                   + "features are off: the user turns them on in the UI (they stay off by default on "
                                   + "purpose). Not changed.");
    }

    /// <summary>A DLL older than <see cref="Constants.McpMinDllBuildForWrites"/> ignores if_free and keep_undo, so a
    /// write could replace what the user holds: refuse it before anything is sent.</summary>
    private static async Task RequireWritableDllAsync(IMcpGame game, int? pid, CancellationToken ct)
    {
        var ptrs = await game.SendAsync(pid, new JsonObject { ["cmd"] = "get_pointers" },
            TimeSpan.FromSeconds(WriteSeconds), ct);
        long build = McpJson.TryGetLong(ptrs["build_number"], out long b) ? b : 0;
        if (build < Constants.McpMinDllBuildForWrites)
            throw new McpToolError($"The game's DLL is build {build.ToString(CultureInfo.InvariantCulture)}, older "
                                   + $"than {Constants.McpMinDllBuildForWrites.ToString(CultureInfo.InvariantCulture)}: "
                                   + "it cannot tell this server's changes from the user's, so nothing is changed "
                                   + "through it. Update the DLL (a proxy: Update All on the Proxy Deploy tab, then "
                                   + "restart the game).");
    }

    /// <summary>A write's reply: the DLL's refusal of another's hold, a module's refusal (a negative code), or the
    /// result.</summary>
    internal static McpToolResult Answer(JsonObject reply, string? notice)
    {
        if (!Truthy(reply, "ok")) return DllError(reply, notice);
        McpTools.Strip(reply);
        long code = McpJson.TryGetLong(reply["code"], out long c) ? c : 0;
        if (code == HeldByOther)
            return Combine(McpToolResult.Fail("Not changed: " + (McpJson.GetString(reply, "hold") ?? "that hold")
                + " is held by " + DescribeHolder(reply["holder"] as JsonObject) + ". This server never replaces or "
                + "ends what someone else holds; the user can release it where it was set."), notice);
        if (code < 0)
            return Combine(new McpToolResult(reply, IsError: true,
                Note: "The game refused (code " + code.ToString(CultureInfo.InvariantCulture) + "): nothing was "
                      + "changed, or the change did not take."), notice);
        return Combine(new McpToolResult(reply), notice);
    }

    // Grimoire::HOLD_HELD_BY_OTHER: the DLL's answer to an if_free request for another's hold.
    private const long HeldByOther = -20;

    internal static string DescribeHolder(JsonObject? holder)
    {
        string kind = McpJson.GetString(holder, "kind") ?? "none";
        string pid = holder?["pid"] is JsonValue p && McpJson.TryGetLong(p, out long n)
            ? " (pid " + n.ToString(CultureInfo.InvariantCulture) + ")" : "";
        bool background = holder?["background"] is JsonValue bv && bv.TryGetValue<bool>(out bool bg) && bg;
        return kind switch
        {
            "ce" => "a Cheat Engine record",
            "client" when background => "another AI session" + pid,
            "client" => "the UE5DumpUI window or another program" + pid,
            _ => "something that recorded no owner (a Cheat Engine script, or a DLL from before owners were recorded)",
        };
    }

    private static bool ReqBool(McpArgs a, string name)
        => a.Has(name) ? a.Bool(name, false) : throw new McpToolError($"'{name}' is required.");

    private static bool Truthy(JsonObject obj, string key)
        => obj[key] is JsonValue v && v.TryGetValue<bool>(out bool b) && b;

    private static McpToolResult DllError(JsonObject reply, string? notice)
        => Combine(McpToolResult.Fail("The DLL refused: " + (McpJson.GetString(reply, "error") ?? "no reason given")
                                      + "."), notice);

    private static McpToolResult Combine(McpToolResult result, string? notice)
        => notice is null ? result
         : result with { Note = result.Note is null ? notice : result.Note + " " + notice };
}
