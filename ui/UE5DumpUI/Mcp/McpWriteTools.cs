using System.Globalization;
using System.Text.Json;
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
/// <para><b>Who is asked</b> ([MCP-WRITE-GRANTS-2026-11-14]). Each tool here is named <c>write_*</c> and has a role
/// and, for a granted write, the grant kind it needs; the host's decision does the rest (<see cref="McpGrants"/>).
/// Under grants a covered write goes through on the user's grant for this game run, and an OFF or a reset needs none;
/// on a host asked per call each one carries <c>requiresUserInteraction</c>, as before grants.</para>
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

    internal static readonly (string, JsonObject, bool) PidProp =
        ("pid", McpSchema.Int("Game process id. Needed only when several games run the dumper (read_games lists "
                              + "them); otherwise the only game is used.", 1, int.MaxValue), false);

    public static IEnumerable<McpTool> Create(IMcpGame game, Func<bool> experimentalEnabled, McpGrants grants)
    {
        // ── Reads around the writes ─────────────────────────────────────────────────────────────────────

        yield return Read(game, "read_holds", "Who holds what",
            "Every gameplay hold the DLL records an owner for (god mode, movement knobs, gravity direction, time "
            + "dilation, fly, Keep Foreground, debug camera, each force-field job): whether it is in force and who "
            + "holds it (\"you\": this server; a client pid; CE; none); pending: a debug-camera ON on its way, held by "
            + "its sender already; release_owed: an ended AI session's debug camera that the DLL turns off once the "
            + "game thread runs. A write_* tool changes only a hold that is free or already this server's. invokes: "
            + "each client's game-thread calls still queued or running (write_invoke waits for others' to clear).",
            [], "get_holds", needsScan: false);
        yield return Read(game, "read_pose", "Player pose",
            "The player pawn's location (x, y, z), rotation (pitch, yaw, roll), the map, and the pawn's address.",
            [], "teleport_get_pose", gameThreadCalls: 1);
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

        yield return Write(game, experimentalEnabled, grants, "write_god_mode", "God mode",
            "Turns god mode on or off: forces the player pawn's bCanBeDamaged to false and holds it. Refused when the "
            + "UI, a Cheat Engine record or another session holds god mode.",
            [("enable", McpSchema.Bool("On or off.", false), true)],
            (a, r) => r["enable"] = ReqBool(a, "enable"), "set_god_mode", kind: McpGrantKind.Toggles);

        yield return Write(game, experimentalEnabled, grants, "write_movement", "Movement knob",
            "Holds a movement knob of the player's movement component at its captured base times a multiplier "
            + "(walk_speed, gravity scale, jump), or resets it to the base. Refused when someone else holds that knob.",
            [("knob", McpSchema.Enum("Which knob.", "walk_speed", "gravity", "jump"), true),
             ("multiplier", McpSchema.Number("Times the base, e.g. 1.5; 0.1 to 10, and for jump 0.317 to 5.477 "
                 + "(the velocity for 10% to 30x the jump height). Leave out with reset.", Constants.McpMoveMultiplierMin,
                 Constants.McpMoveMultiplierMax), false),
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
                if (knob == "jump")
                    InRange(m, "multiplier", Constants.McpJumpVelocityMin, Constants.McpJumpVelocityMax,
                        " for jump (the velocity for 10% to 30x the jump height, as the UI's slider)");
                else
                    InRange(m, "multiplier", Constants.McpMoveMultiplierMin, Constants.McpMoveMultiplierMax, "");
                r["multiplier"] = m;
            }, "set_movement_multiplier", kind: McpGrantKind.Movement);

        yield return Write(game, experimentalEnabled, grants, "write_gravity_direction", "Gravity direction",
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
                double x = a.ReqDouble("x"), y = a.ReqDouble("y"), z = a.ReqDouble("z");
                // The DLL reads a zero vector as its own OFF, which a set would carry past the gate as no release.
                if (x == 0 && y == 0 && z == 0)
                    throw new McpToolError("'x', 'y' and 'z' are all 0, which is no direction. Pass reset to give the "
                                           + "game back its own gravity.");
                r["x"] = x;
                r["y"] = y;
                r["z"] = z;
            }, "set_gravity_direction", kind: McpGrantKind.Movement);

        yield return Write(game, experimentalEnabled, grants, "write_time_dilation", "Time dilation",
            "Holds world time dilation (target global) or the player pawn's own (target pawn) at a value, or resets it "
            + "to its natural value. 0.5 is half speed. Refused when someone else holds that lane.",
            [("target", McpSchema.Enum("Which lane.", "global", "pawn"), true),
             ("value", McpSchema.Number("The dilation, e.g. 0.5: above 0, at most 3 for global and 10 for pawn, as "
                 + "the UI's sliders. Leave out with reset.", 0, Constants.McpPawnTimeDilationMax, exclusiveMin: true),
                 false),
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
                double max = target == "global" ? Constants.McpWorldTimeDilationMax : Constants.McpPawnTimeDilationMax;
                if (v <= 0 || v > max)
                    throw new McpToolError($"'value' must be above 0 and at most {Show(max)} for {target}, as the UI's "
                                           + $"slider ({Show(v)} given).");
                r["value"] = v;
            }, "set_time_dilation", kind: McpGrantKind.Toggles);

        yield return DebugCamera(game, grants);

        yield return Write(game, experimentalEnabled, grants, "write_mouse_cursor", "Mouse cursor",
            "Shows or hides the game's mouse cursor (PlayerController.bShowMouseCursor). A one-time change the game may "
            + "set back; nothing holds it.",
            [("show", McpSchema.Bool("Show or hide.", true), true)],
            (a, r) => r["show"] = ReqBool(a, "show"), "set_mouse_cursor", ifFree: false,
            gameThreadCalls: 2, kind: McpGrantKind.Toggles);   // its input mode is set on the game thread, twice to show the cursor

        yield return Write(game, experimentalEnabled, grants, "write_fly", "Fly",
            "Experimental. Turns flying on or off and sets its speed, key preset (0 WASD, 1 numpad, 2 arrows) and "
            + "noclip. Refused when someone else holds fly, or while the UI's experimental features are off.",
            [("enable", McpSchema.Bool("On or off.", false), false),
             ("speed", McpSchema.Number("Speed in units per second, 50 to 20000.", Constants.McpFlySpeedMin,
                 Constants.McpFlySpeedMax), false),
             ("preset", McpSchema.Int("Key preset.", 0, 2), false),
             ("noclip", McpSchema.Bool("Through walls.", false), false)],
            (a, r) =>
            {
                if (!(a.Has("enable") || a.Has("speed") || a.Has("preset") || a.Has("noclip")))
                    throw new McpToolError("Pass at least one of enable, speed, preset, noclip.");
                if (a.Has("enable")) r["enable"] = a.Bool("enable", false);
                if (a.Has("speed"))
                    r["speed"] = InRange(a.ReqDouble("speed"), "speed", Constants.McpFlySpeedMin,
                        Constants.McpFlySpeedMax, "");
                if (a.Has("preset")) r["preset"] = a.IntIn("preset", 0, 0, 2);
                if (a.Has("noclip")) r["noclip"] = a.Bool("noclip", false);
            }, "fly_set", experimental: true, gameThreadCalls: 2, kind: McpGrantKind.Movement);   // fly OFF restores collision on the game thread

        yield return Write(game, experimentalEnabled, grants, "write_foreground_lock", "Keep Foreground",
            "Experimental. Makes the game believe it is the foreground window, so it keeps running (and game-thread "
            + "work keeps answering) while another window has the focus. Refused when someone else holds it, or while "
            + "the UI's experimental features are off.",
            [("enable", McpSchema.Bool("On or off.", false), true)],
            (a, r) => r["enable"] = ReqBool(a, "enable"), "set_foreground_lock", experimental: true,
            kind: McpGrantKind.Toggles);

        yield return SeeThrough(game, experimentalEnabled, grants);

        yield return Write(game, experimentalEnabled, grants, "write_force_field", "Force a field",
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
            }, "force_field", experimental: true, timeoutSeconds: 90, role: McpToolRole.PerCallWrite);

        yield return Write(game, experimentalEnabled, grants, "write_force_field_reset", "Release a forced field",
            "Experimental. Releases one force-field job this server holds, restoring the captured value. Refused for "
            + "a job someone else holds.",
            [("class_name", McpSchema.Str("The class."), true),
             ("field_name", McpSchema.Str("The field."), true)],
            (a, r) =>
            {
                r["class_name"] = a.ReqStr("class_name");
                r["field_name"] = a.ReqStr("field_name");
            }, "reset_field", experimental: true, role: McpToolRole.Release);

        yield return Teleport(game, experimentalEnabled, grants);
    }

    // ── Builders ────────────────────────────────────────────────────────────────────────────────────────

    private static McpTool Read(IMcpGame game, string name, string title, string description,
        (string, JsonObject, bool)[] props, string cmd, bool needsScan = true, int gameThreadCalls = 0)
    {
        var all = new List<(string, JsonObject, bool)>(props) { PidProp };
        return new McpTool(name, title, description, McpSchema.Object([.. all]), ReadOnly: true,
            async (a, ct) =>
            {
                int? pid = a.OptInt("pid");
                // A read that makes a game-thread call waits as long as the DLL may: ending first would drop the
                // connection, and every hold this server owns with it.
                var timeout = TimeSpan.FromSeconds(WriteSeconds);
                if (gameThreadCalls > 0)
                    timeout = InvokeTimeout(await game.SendAsync(pid, new JsonObject { ["cmd"] = "get_pointers" },
                        TimeSpan.FromSeconds(WriteSeconds), ct), gameThreadCalls);
                var reply = await game.SendAsync(pid, new JsonObject { ["cmd"] = cmd }, timeout, ct,
                    requireScan: needsScan);
                if (!Truthy(reply, "ok")) return DllError(reply, game.TakeNotice());
                McpTools.Strip(reply);
                return Combine(new McpToolResult(reply), game.TakeNotice());
            });
    }

    /// <summary>A tool that is one DLL command changing the game. <paramref name="fill"/> may name another command
    /// (a reset) by setting <c>cmd</c>.</summary>
    private static McpTool Write(IMcpGame game, Func<bool> experimentalEnabled, McpGrants grants, string name,
        string title,
        string description, (string, JsonObject, bool)[] props, Action<McpArgs, JsonObject> fill, string cmd,
        bool experimental = false, bool ifFree = true, int timeoutSeconds = WriteSeconds, int gameThreadCalls = 0,
        McpGrantKind kind = McpGrantKind.None, McpToolRole role = McpToolRole.GrantedWrite)
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
                var ptrs = await RequireWritableDllAsync(game, pid, ct);
                var grant = Admit(grants, role, kind, request, ptrs);
                // A command that makes game-thread calls on its way waits as long as the DLL may on each.
                var timeout = gameThreadCalls > 0
                    ? InvokeTimeout(ptrs, gameThreadCalls)
                    : TimeSpan.FromSeconds(timeoutSeconds);
                var reply = await game.SendAsync(pid, request, timeout, ct, requireScan: true, grant: grant);
                return Answer(reply, game.TakeNotice());
            }, Idempotent: true, Destructive: true, Role: role, GrantKind: kind);
    }

    /// <summary>See-through has its own owner model (Stage C): each client that turned it on holds a claim, released when
    /// it leaves, but an explicit OFF drops every claim. So this server turns it off only when its own claim is the only
    /// one; otherwise its claim simply ends with the session.</summary>
    private static McpTool SeeThrough(IMcpGame game, Func<bool> experimentalEnabled, McpGrants grants)
        => new("write_see_through", "See-through",
            "Experimental. Hides the occluders between the camera and the player pawn (count: how many nearest), or "
            + "ends this server's own claim on it: See-through stays on while the UI or Cheat Engine also has it on. "
            + "Refused while the UI's experimental features are off.",
            McpSchema.Object(("enable", McpSchema.Bool("On or off.", false), true),
                ("count", McpSchema.Int("Occluders to hide (turning on only).", 1, Constants.McpSeeThroughCountMax),
                    false), PidProp),
            ReadOnly: false,
            async (a, ct) =>
            {
                int? pid = a.OptInt("pid");
                RequireExperimental(experimentalEnabled, "See-through");
                bool enable = ReqBool(a, "enable");
                int? count = enable && a.Has("count") ? a.IntIn("count", 1, 1, Constants.McpSeeThroughCountMax) : null;
                var ptrs = await RequireWritableDllAsync(game, pid, ct);
                long build = McpJson.TryGetLong(ptrs["build_number"], out long b) ? b : 0;
                if (!enable && build < Constants.McpMinDllBuildForOwnClaim)
                    throw new McpToolError("Not changed: the game's DLL is build "
                        + build.ToString(CultureInfo.InvariantCulture) + ", older than "
                        + Constants.McpMinDllBuildForOwnClaim.ToString(CultureInfo.InvariantCulture) + ", which cannot "
                        + "end only this server's claim on See-through: an OFF there would end the user's and Cheat "
                        + "Engine's too. This server's claim ends with the session; an updated DLL lets it end sooner.");
                // An OFF ends only this server's claim (the DLL turns See-through off with the last one), so it is a
                // release; the count is a global setting, applied before enable, that nobody turning it off needs.
                var request = enable
                    ? new JsonObject { ["cmd"] = "seethrough_set", ["enable"] = true }
                    : new JsonObject
                    {
                        ["cmd"] = "seethrough_set", ["enable"] = false, ["own_claim"] = true, ["if_free"] = true,
                    };
                if (count is int n) request["count"] = n;
                var grant = Admit(grants, McpToolRole.GrantedWrite, McpGrantKind.Toggles, request, ptrs);
                // Hiding or showing each occluder is a game-thread call, up to 16 in turn.
                var reply = await game.SendAsync(pid, request, InvokeTimeout(ptrs, 16), ct, requireScan: true,
                    grant: grant);
                var result = Answer(reply, game.TakeNotice());
                if (enable || result.IsError) return result;
                string? stayed = McpJson.GetString(reply, "released") switch
                {
                    "still_held" => "See-through stays on: the UE5DumpUI window or a Cheat Engine record also has it "
                                    + "on. This server's claim is gone.",
                    "not_held" => "This server held no claim on See-through, so nothing changed.",
                    _ => null,
                };
                return stayed is null ? result : result with { Note = Join(stayed, result.Note) };
            }, Idempotent: true, Destructive: true, Role: McpToolRole.GrantedWrite, GrantKind: McpGrantKind.Toggles);

    private static string Join(string first, string? second) => second is null ? first : first + " " + second;

    /// <summary>The guarded debug-camera command answers in <c>state</c>, not <c>code</c> (D2-3 found the generic
    /// reply reading every refusal as success), and its toggle is a game-thread call, so it waits as long as the DLL
    /// may.</summary>
    private static McpTool DebugCamera(IMcpGame game, McpGrants grants) => new(
        "write_debug_camera", "Debug camera",
        "Turns the engine's debug camera on or off. It reads the state first and toggles only when needed. ON needs a "
        + "live CheatManager, which UE 5 leaves out of most Shipping games. Refused when someone else holds the camera. "
        + "A queued answer means the toggle runs when the game thread is free: do not send it again.",
        McpSchema.Object(("enable", McpSchema.Bool("On or off.", false), true), PidProp),
        ReadOnly: false,
        async (a, ct) =>
        {
            int? pid = a.OptInt("pid");
            bool enable = ReqBool(a, "enable");
            // 3706: a queued ON records its sender, so the camera this server turns on is its own to turn off and ends
            // with the session.
            var ptrs = await RequireDllBuildAsync(game, pid, Constants.McpMinDllBuildForRawWrites, ct);
            var request = new JsonObject { ["cmd"] = "set_debug_camera", ["enable"] = enable, ["if_free"] = true };
            var grant = Admit(grants, McpToolRole.GrantedWrite, McpGrantKind.Toggles, request, ptrs);
            var reply = await game.SendAsync(pid, request, InvokeTimeout(ptrs, 2), ct, requireScan: true,
                grant: grant);
            var answer = Answer(reply, null);   // a hold's or the queue's if_free refusal (code -20), a DLL error
            if (answer.IsError) return Combine(answer, game.TakeNotice());
            long state = McpJson.TryGetLong(reply["state"], out long s) ? s : -1;
            string? failure = state switch
            {
                -17 => "Not turned on: no CheatManager is live (UE 5 leaves it out of Shipping games by default), and "
                       + "the toggle does nothing on its class default object. Nothing was sent.",
                -6 => "Not sent: an earlier toggle the other way is still queued. Send it again once the game responds.",
                < 0 and not -5 => "The debug camera's state could not be read, or the toggle failed (state "
                                  + state.ToString(CultureInfo.InvariantCulture) + ").",
                // The toggle ran, and a fresh read still disagrees: it did not take.
                0 or 1 when (state == 1) != enable => "The toggle ran, but the camera is still "
                                                      + (state == 1 ? "on" : "off") + ".",
                _ => null,
            };
            if (failure is not null)
                return Combine(new McpToolResult(reply, IsError: true, Note: failure), game.TakeNotice());
            if (state == -5)
                return Combine(new McpToolResult(reply, Note: enable
                    ? "Queued: the game thread did not run the toggle in time. It runs when the game thread is free; do "
                      + "not send it again."
                    : "Queued: on UE 4.18-5.7 a queued OFF does not turn the camera off; send OFF again once the game "
                      + "responds."), game.TakeNotice());
            return Combine(new McpToolResult(reply), game.TakeNotice());
        }, Idempotent: true, Destructive: true, Role: McpToolRole.GrantedWrite, GrantKind: McpGrantKind.Toggles);

    /// <summary>Teleport to explicit coordinates only: never a marker slot (they are the user's), and with
    /// <c>keep_undo</c>, so the user's "Recall last" still returns where the user was. The pose before the jump comes
    /// back in <c>from</c>, so the AI can return on its own.</summary>
    private static McpTool Teleport(IMcpGame game, Func<bool> experimentalEnabled, McpGrants grants) => new(
        "write_teleport", "Teleport",
        "Moves the player pawn to explicit world coordinates (and rotation, when given); read_pose gives where it is "
        + "now. Returns the pose it left in "
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
            var ptrs = await RequireWritableDllAsync(game, pid, ct);
            var grant = Admit(grants, McpToolRole.GrantedWrite, McpGrantKind.Movement, request, ptrs);
            // Reading the pose of an attached pawn is a game-thread call (K2_GetActorLocation).
            var from = await game.SendAsync(pid, new JsonObject { ["cmd"] = "teleport_get_pose" },
                InvokeTimeout(ptrs, 1), ct, requireScan: true);
            // The jump makes up to seven game-thread calls in turn (the teleport, a component's world and relative
            // moves, a location read, the rotation, two movement stops): wait as long as the DLL may.
            var reply = await game.SendAsync(pid, request, InvokeTimeout(ptrs, 7), ct, requireScan: true,
                grant: grant);
            var result = Answer(reply, game.TakeNotice());
            if (!result.IsError && result.Payload is JsonObject payload && Truthy(from, "ok"))
            {
                var pose = new JsonObject();
                foreach (var key in new[] { "x", "y", "z", "pitch", "yaw", "roll", "map" })
                    if (from[key] is { } node) pose[key] = node.DeepClone();
                payload["from"] = pose;
            }
            return result;
        }, Idempotent: true, Destructive: true, Role: McpToolRole.GrantedWrite, GrantKind: McpGrantKind.Movement);

    // ── Guards and replies ──────────────────────────────────────────────────────────────────────────────

    /// <summary>A value that must lie in the UI's own range: refused outside, never clamped, before anything is sent.
    /// </summary>
    private static double InRange(double v, string name, double min, double max, string which)
    {
        if (v >= min && v <= max) return v;
        throw new McpToolError($"'{name}' must be from {Show(Math.Ceiling(min * 1000) / 1000)} to "
                               + $"{Show(Math.Floor(max * 1000) / 1000)}{which} ({Show(v)} given).");
    }

    // The bounds are worded at three decimals, rounded inward, so the words never name a value the check refuses.
    private static string Show(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Admits one state-changing request for the game run in <paramref name="ptrs"/>, the get_pointers reply
    /// the DLL build check fetched, so the build refusal still comes first. A release needs no admission and gets
    /// null; a refusal throws, naming the grant to ask for. The link's gate checks the capability again at the send.
    /// </summary>
    internal static McpGrantCapability? Admit(McpGrants grants, McpToolRole role, McpGrantKind kind,
        JsonObject request, JsonObject ptrs, string target = "", string detail = "")
    {
        // A release goes through without a grant only if this server holds what it ends, which the link reads at the
        // send; the grant, when there is one, goes along for the case where it does not.
        if (McpGrants.IsRelease(request)) return grants.Admit(role, kind, ptrs, out _, target, detail);
        var grant = grants.Admit(role, kind, ptrs, out string? refusal, target, detail);
        if (grant is not null) return grant;
        // An OFF that also carries a global setting (Fly's speed, preset or noclip) is no release.
        if (request["enable"] is JsonValue v && v.GetValueKind() == JsonValueKind.False)
            refusal += " Only an OFF with nothing else in it needs no grant, and only for a hold this server took.";
        throw new McpToolError(refusal ?? "Not changed.");
    }

    private static void RequireExperimental(Func<bool> experimentalEnabled, string feature)
    {
        if (!experimentalEnabled())
            throw new McpToolError($"{feature} is an experimental feature, and the UE5DumpUI window's experimental "
                                   + "features are off: the user turns them on in the UI (they stay off by default on "
                                   + "purpose). Not changed.");
    }

    /// <summary>A DLL older than <see cref="Constants.McpMinDllBuildForWrites"/> ignores if_free and keep_undo, so a
    /// write could replace what the user holds: refuse it before anything is sent. Returns get_pointers' reply.</summary>
    private static Task<JsonObject> RequireWritableDllAsync(IMcpGame game, int? pid, CancellationToken ct)
        => RequireDllBuildAsync(game, pid, Constants.McpMinDllBuildForWrites, ct);

    /// <summary>Refuses a DLL older than <paramref name="minBuild"/> before anything is sent; returns get_pointers'
    /// reply (build_number, invoke_timeout_ms, ue_version).</summary>
    internal static async Task<JsonObject> RequireDllBuildAsync(IMcpGame game, int? pid, int minBuild,
        CancellationToken ct)
    {
        var ptrs = await game.SendAsync(pid, new JsonObject { ["cmd"] = "get_pointers" },
            TimeSpan.FromSeconds(WriteSeconds), ct);
        long build = McpJson.TryGetLong(ptrs["build_number"], out long b) ? b : 0;
        if (build < minBuild)
            throw new McpToolError($"The game's DLL is build {build.ToString(CultureInfo.InvariantCulture)}, older "
                                   + $"than {minBuild.ToString(CultureInfo.InvariantCulture)}: it cannot keep this "
                                   + "server's changes off the user's, so nothing is changed through it. Update the DLL "
                                   + "(a proxy: Update All on the Proxy Deploy tab, then restart the game).");
        return ptrs;
    }

    /// <summary>How long to wait for a command that makes <paramref name="calls"/> game-thread calls one after another:
    /// the DLL may wait invoke_timeout_ms (the user sets it, up to 600 s) on each. Ending first would drop the connection
    /// -- and with it every hold this server owns -- while the DLL still waits.</summary>
    internal static TimeSpan InvokeTimeout(JsonObject ptrs, int calls)
    {
        long ms = McpJson.TryGetLong(ptrs["invoke_timeout_ms"], out long t) && t > 0 ? t : 5000;
        return TimeSpan.FromMilliseconds(Math.Max(calls, 1) * ms + 15000);
    }

    /// <summary>A write's reply: the DLL's refusal of another's hold, a module's refusal (a negative code), or the
    /// result.</summary>
    internal static McpToolResult Answer(JsonObject reply, string? notice)
    {
        if (!Truthy(reply, "ok")) return DllError(reply, notice);
        McpTools.Strip(reply);
        long code = McpJson.TryGetLong(reply["code"], out long c) ? c : 0;
        string? hold = McpJson.GetString(reply, "hold");
        if (code == HeldByOther && hold == InvokeQueueHold)
            return Combine(McpToolResult.Fail("Not changed: " + DescribeHolder(reply["holder"] as JsonObject)
                + " has a game-thread call still queued or running, and this server never queues beside it. Try again "
                + "once it has run (read_holds lists it)."), notice);
        if (code == HeldByOther)
            return Combine(McpToolResult.Fail("Not changed: " + (hold ?? "that hold")
                + " is held by " + DescribeHolder(reply["holder"] as JsonObject) + ". This server never replaces or "
                + "ends what someone else holds; the user can release it where it was set."), notice);
        if (code == NeedsGameThread)
            return Combine(McpToolResult.Fail("Not changed: the game-thread hook is not active, and this server's "
                + "game-thread calls run only on the game thread."), notice);
        if (code < 0)
            return Combine(new McpToolResult(reply, IsError: true,
                Note: "The game refused (code " + code.ToString(CultureInfo.InvariantCulture) + "): nothing was "
                      + "changed, or the change did not take."), notice);
        return Combine(new McpToolResult(reply), notice);
    }

    // Grimoire::HOLD_HELD_BY_OTHER: the DLL's answer to an if_free request for another's hold -- or, with the hold
    // Grimoire::HOLD_INVOKE_QUEUE, while another client's game-thread call is outstanding.
    private const long HeldByOther = -20;
    private const string InvokeQueueHold = "invoke_queue";
    // Stark::kInvokeNeedsGameThread: an if_free game-thread call while the ProcessEvent hook is down.
    private const long NeedsGameThread = -27;

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

    internal static bool Truthy(JsonObject obj, string key)
        => obj[key] is JsonValue v && v.TryGetValue<bool>(out bool b) && b;

    private static McpToolResult DllError(JsonObject reply, string? notice)
        => Combine(McpToolResult.Fail("The DLL refused: " + (McpJson.GetString(reply, "error") ?? "no reason given")
                                      + "."), notice);

    internal static McpToolResult Combine(McpToolResult result, string? notice)
        => notice is null ? result
         : result with { Note = result.Note is null ? notice : result.Note + " " + notice };
}
