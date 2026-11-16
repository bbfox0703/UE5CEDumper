using System.Globalization;
using System.Text.Json.Nodes;

namespace UE5DumpUI.Mcp;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] Phases 1, 1b and 2. The tools that give, list and end the session's write grants: the
/// two kind grants, an invoke grant naming one UFunction, and a field grant naming one field of one class.
///
/// <para><b>A grant tool is where the user is asked.</b> Claude Code's prompt shows a tool's name and its arguments,
/// nothing else (Phase 0, M1 and M10), so each grant's name states what it unlocks, and its one required argument,
/// <c>scope_ack</c>, must be a fixed sentence that says the same and how long it lasts: the user approves those words,
/// not a summary the model wrote. Both are destructive (they unlock destructive tools), so a host that runs
/// non-destructive tools on its own does not approve consent on the user's behalf.</para>
///
/// <para><b>read_grants and revoke_grants never touch the pipe.</b> read_grants is read-only, so a user's
/// <c>read_*</c> allow rule admits it; revoke_grants only narrows what the AI may do, so it is not asked about, and it
/// never releases a hold. Grants and holds are separate: a grant says the AI may take a hold, the DLL keeps the hold.
/// </para>
///
/// <para>A reply names the game by pid and exe image only. A window title is the game's own text, and nothing the game
/// controls is echoed to the model as part of a consent record.</para>
/// </summary>
internal static class McpGrantTools
{
    /// <summary>The grant tools, read_grants and revoke_grants. <paramref name="catalogue"/> is read when a grant is
    /// given, so the tools a grant covers are the catalogue's own, by role and kind, never a second list kept here.
    /// </summary>
    public static IEnumerable<McpTool> Create(IMcpGame game, McpGrants grants, IReadOnlyList<McpTool> catalogue)
    {
        yield return GrantTool(game, grants, catalogue, McpGrantKind.Toggles, Constants.McpGrantToolToggles,
            "Grant: gameplay toggles", Constants.McpGrantAckToggles);
        yield return GrantTool(game, grants, catalogue, McpGrantKind.Movement, Constants.McpGrantToolMovement,
            "Grant: movement and teleport", Constants.McpGrantAckMovement);
        yield return InvokeGrantTool(game, grants, catalogue);
        yield return FieldGrantTool(game, grants, catalogue);
        yield return ReadGrants(game, grants);
        yield return RevokeGrants(grants);
    }

    private const string Ends =
        "It ends when this game run ends (the game exits or restarts), when this AI session ends or its MCP server "
        + "reconnects, or on revoke_grants. A dropped pipe connection to the same game run keeps it; the holds the AI "
        + "took are released then and may be taken again.";

    private static McpTool GrantTool(IMcpGame game, McpGrants grants, IReadOnlyList<McpTool> catalogue,
        McpGrantKind kind, string name, string title, string ack) => new(
        name, title,
        "Asks the user for one grant, for one game run: afterwards " + McpGrants.Covers(kind) + " change without "
        + "asking again. Call it only after the user asked for such a change, never in advance, and first say in chat "
        + "what it covers and how long it lasts. scope_ack must be exactly the sentence its schema gives. Turning off "
        + "and resetting the holds this server took need no grant.",
        McpSchema.Object(
            ("scope_ack", McpSchema.Enum("This exact sentence, which the user sees and approves.", ack), true),
            McpWriteTools.PidProp),
        ReadOnly: false,
        async (a, ct) =>
        {
            int? pid = a.OptInt("pid");
            string given = (a.Str("scope_ack") ?? "").Trim();
            if (!string.Equals(given, ack, StringComparison.Ordinal))
                throw new McpToolError("Not granted: scope_ack must be exactly this sentence: \"" + ack + "\"");
            var ptrs = await McpWriteTools.RequireDllBuildAsync(game, pid, Constants.McpMinDllBuildForWrites, ct);
            var record = grants.Grant(kind, ptrs, out string? refusal)
                ?? throw new McpToolError(refusal ?? "Not granted.");

            var others = new JsonArray();
            foreach (var g in grants.List())
                if (g.Id != record.Id)
                    McpJson.Append(others, Describe(g, game));
            var payload = new JsonObject
            {
                ["granted"] = name,
                ["covers"] = McpGrants.Covers(kind),
                ["tools"] = CoveredTools(catalogue, kind),
                ["game"] = Game(game, record.Pid),
                ["ends"] = Ends,
                ["other_grants"] = others,
            };
            return McpWriteTools.Combine(new McpToolResult(payload), game.TakeNotice());
        }, Idempotent: true, Destructive: true, Role: McpToolRole.Grant, GrantKind: kind);

    private const int LookupSeconds = 30;

    // [MCP-WRITE-GRANTS-2026-11-14] Phase 1b (docs/mcp-write-approval-eval.md §4.4). One UFunction, named by its function
    // and its class's full path, shown to the user in that order (the prompt shows a long string whole, Phase 0 M1).
    // The prompt comes first (Claude Code asks before the tool runs); the tool then looks the function up, so a grant
    // that could never be used -- one the DLL refuses an AI's call of, or one whose arguments reach other functions or
    // properties by name, as far as walk_functions shows them -- is refused here instead of recorded.
    private static McpTool InvokeGrantTool(IMcpGame game, McpGrants grants, IReadOnlyList<McpTool> catalogue) => new(
        Constants.McpGrantToolInvoke, "Grant: one UFunction",
        "Asks the user for one grant, for one game run: afterwards " + Constants.McpInvokeGrantedTool + " calls this "
        + "one UFunction on any object, with any arguments, without asking again. function and class name it as "
        + "read_functions shows it in 'full': the class is everything before the last dot, e.g. //Script/Engine/Actor "
        + "for //Script/Engine/Actor.SetActorHiddenInGame. Call it only after the user asked for calls of this "
        + "function, never in advance, and first say in chat what it allows and how long it lasts. scope_ack must be "
        + "exactly the sentence its schema gives. Refused for a function the AI may not call, or one whose arguments "
        + "reach other functions or properties by name: write_invoke asks every call instead.",
        McpSchema.Object(
            ("function", McpSchema.Str("The UFunction's name, e.g. \"SetActorHiddenInGame\"."), true),
            ("class", McpSchema.Str("Its class's full path, everything before the function in read_functions' "
                                    + "'full', e.g. \"//Script/Engine/Actor\"."), true),
            ("scope_ack", McpSchema.Enum("This exact sentence, which the user sees and approves.",
                Constants.McpGrantAckInvoke), true),
            McpWriteTools.PidProp),
        ReadOnly: false,
        async (a, ct) =>
        {
            int? pid = a.OptInt("pid");
            string function = a.ReqStr("function").Trim();
            string cls = a.ReqStr("class").Trim();
            string given = (a.Str("scope_ack") ?? "").Trim();
            if (!string.Equals(given, Constants.McpGrantAckInvoke, StringComparison.Ordinal))
                throw new McpToolError("Not granted: scope_ack must be exactly this sentence: \""
                                       + Constants.McpGrantAckInvoke + "\"");
            var ptrs = await McpWriteTools.RequireDllBuildAsync(game, pid, Constants.McpMinDllBuildForInvokeGrants, ct);
            string target = cls + "." + function;
            if (!McpGrants.IsFunctionFullName(target))
                throw new McpToolError("Not granted: name one function and its class's full path, as read_functions "
                                       + "shows them in 'full' (e.g. //Script/Engine/Actor and SetActorHiddenInGame).");
            var fn = await FindGrantedFunctionAsync(game, pid, cls, target, ct);
            if (McpInvokeRules.WhyNotGrantable(fn) is { } why)
                throw new McpToolError("Not granted: " + fn.Name + " " + why + ". write_invoke asks the user about each "
                                       + "call instead, where the DLL allows it.");
            var record = grants.Grant(McpGrantKind.Invoke, ptrs, out string? refusal, target)
                ?? throw new McpToolError(refusal ?? "Not granted.");

            var others = new JsonArray();
            foreach (var g in grants.List())
                if (g.Id != record.Id)
                    McpJson.Append(others, Describe(g, game));
            var payload = new JsonObject
            {
                ["granted"] = Constants.McpGrantToolInvoke,
                ["function"] = target,
                ["covers"] = "calls of " + target + " on any object, with any arguments",
                ["tools"] = CoveredTools(catalogue, McpGrantKind.Invoke),
                ["game"] = Game(game, record.Pid),
                ["ends"] = Ends,
                ["other_grants"] = others,
            };
            return McpWriteTools.Combine(new McpToolResult(payload), game.TakeNotice());
        }, Idempotent: true, Destructive: true, Role: McpToolRole.Grant, GrantKind: McpGrantKind.Invoke);

    /// <summary>The function a grant names: its class found by path and checked by its own full name (the DLL's path
    /// lookup folds separators, so its answer is checked, never trusted), then the class's own functions.</summary>
    private static async Task<Models.FunctionInfoModel> FindGrantedFunctionAsync(IMcpGame game, int? pid, string cls,
        string target, CancellationToken ct)
    {
        var lookup = TimeSpan.FromSeconds(LookupSeconds);
        var found = await game.SendAsync(pid, new JsonObject { ["cmd"] = "find_object", ["path"] = cls }, lookup, ct,
            requireScan: true);
        string? addr = McpWriteTools.Truthy(found, "ok") ? McpJson.GetString(found, "addr") : null;
        if (addr is null || !McpArgs.TryParseHex(addr, out ulong a) || a == 0)
            throw new McpToolError("Not granted: no class " + cls + " (read_functions shows its path in 'full').");
        var obj = await game.SendAsync(pid, new JsonObject { ["cmd"] = "get_object", ["addr"] = addr }, lookup, ct,
            requireScan: true);
        if (!string.Equals(McpJson.GetString(obj, "full_name"), cls, StringComparison.Ordinal))
            throw new McpToolError("Not granted: no class " + cls + " (the object found there is "
                                   + (McpJson.GetString(obj, "full_name") ?? "unnamed") + ").");
        var funcs = await game.SendAsync(pid, new JsonObject { ["cmd"] = "walk_functions", ["addr"] = addr }, lookup,
            ct, requireScan: true);
        var fn = McpWriteTools.Truthy(funcs, "ok")
            ? Services.DumpService.ParseFunctions(funcs)
                .FirstOrDefault(f => string.Equals(f.FullName, target, StringComparison.Ordinal))
            : null;
        return fn ?? throw new McpToolError("Not granted: " + cls + " declares no UFunction "
                                            + McpGrants.FunctionNameOf(target) + " (read_functions lists them).");
    }

    // [MCP-WRITE-GRANTS-2026-11-14] Phase 2 (docs/mcp-write-approval-eval.md §4.5). One field of one class, named by the
    // field and the class's full path, in that order, as the invoke grant names its function. The class bounds the
    // instances (its own and its subclasses'); the field must be a type write_field writes, so a grant that could never
    // be used is refused here instead of recorded.
    private static McpTool FieldGrantTool(IMcpGame game, McpGrants grants, IReadOnlyList<McpTool> catalogue) => new(
        Constants.McpGrantToolField, "Grant: one field of one class",
        "Asks the user for one grant, for one game run: afterwards " + Constants.McpFieldGrantedTool + " writes this "
        + "one field on every live instance of this class and its subclasses (NPCs included), with any value its type "
        + "allows, without asking again. field and class name it as read_class shows them: the field's name, and the "
        + "class's full_path, e.g. //Script/Engine/Character. Only a number, a bool or an enum field, one element. Call it "
        + "only after the user asked for changes of this field, never in advance, and first say in chat what it allows "
        + "and how long it lasts. scope_ack must be exactly the sentence its schema gives. write_memory asks every call "
        + "instead, for anything else.",
        McpSchema.Object(
            ("field", McpSchema.Str("The field's name, as read_class lists it, e.g. \"Health\"."), true),
            ("class", McpSchema.Str("The class's full path, read_class' full_path, e.g. \"//Script/Engine/Character\"."),
                true),
            ("scope_ack", McpSchema.Enum("This exact sentence, which the user sees and approves.",
                Constants.McpGrantAckField), true),
            McpWriteTools.PidProp),
        ReadOnly: false,
        async (a, ct) =>
        {
            int? pid = a.OptInt("pid");
            string field = a.ReqStr("field").Trim();
            string cls = a.ReqStr("class").Trim();
            string given = (a.Str("scope_ack") ?? "").Trim();
            if (!string.Equals(given, Constants.McpGrantAckField, StringComparison.Ordinal))
                throw new McpToolError("Not granted: scope_ack must be exactly this sentence: \""
                                       + Constants.McpGrantAckField + "\"");
            var ptrs = await McpWriteTools.RequireDllBuildAsync(game, pid, Constants.McpMinDllBuildForFieldWrites, ct);
            string target = cls + ":" + field;
            if (!McpGrants.IsFieldTarget(target))
                throw new McpToolError("Not granted: name one field and its class's full path, as read_class shows them "
                                       + "(e.g. Health and //Script/Engine/Character). write_memory asks every call "
                                       + "instead.");
            var f = await McpFieldTools.FindGrantableFieldAsync(game, pid, cls, field, ct);
            var record = grants.Grant(McpGrantKind.Field, ptrs, out string? refusal, target)
                ?? throw new McpToolError(refusal ?? "Not granted.");

            var others = new JsonArray();
            foreach (var g in grants.List())
                if (g.Id != record.Id)
                    McpJson.Append(others, Describe(g, game));
            var payload = new JsonObject
            {
                ["granted"] = Constants.McpGrantToolField,
                ["field"] = target,
                ["type"] = f.TypeName,
                ["covers"] = "writes of " + target + " on every live instance of the class and its subclasses, any value "
                             + "its type allows",
                ["tools"] = CoveredTools(catalogue, McpGrantKind.Field),
                ["game"] = Game(game, record.Pid),
                ["ends"] = Ends,
                ["other_grants"] = others,
            };
            return McpWriteTools.Combine(new McpToolResult(payload), game.TakeNotice());
        }, Idempotent: true, Destructive: true, Role: McpToolRole.Grant, GrantKind: McpGrantKind.Field);

    private static McpTool ReadGrants(IMcpGame game, McpGrants grants) => new(
        "read_grants", "Write grants",
        "This session's write grants: which grant, the game (pid, exe), when it was given, how many writes it has "
        + "let through, and whether that game is still running. Sends nothing to the game.",
        McpSchema.Object(), ReadOnly: true,
        (_, _) =>
        {
            var arr = new JsonArray();
            foreach (var g in grants.List()) McpJson.Append(arr, Describe(g, game));
            var payload = new JsonObject
            {
                ["decision"] = grants.Decision.ToString(),
                ["grants"] = arr,
                ["per_call"] = "write_memory, write_invoke and write_force_field take no grant and are asked about on "
                               + "every call; " + Constants.McpInvokeGrantedTool + " calls only a function an invoke "
                               + "grant names, and " + Constants.McpFieldGrantedTool + " writes only a field a field "
                               + "grant names.",
            };
            return Task.FromResult(new McpToolResult(payload));
        }, Role: McpToolRole.GrantAdmin);

    private static McpTool RevokeGrants(McpGrants grants) => new(
        "revoke_grants", "End write grants",
        "Ends this session's write grants: one kind (toggles, movement, invoke or field -- every invoke or field "
        + "grant, or the one whose function or field is given), or every one when kind is left out. It only narrows what "
        + "the AI may do, and it releases no hold: turn each hold off with its write_* tool.",
        McpSchema.Object(
            ("kind", McpSchema.Enum("Which grant; leave out for every one.", "toggles", "movement", "invoke", "field"),
                false),
            ("function", McpSchema.Str("With kind invoke: the full name of the one function whose grant ends."),
                false),
            ("field", McpSchema.Str("With kind field: the one field whose grant ends, as read_grants shows it (its "
                                    + "class's full path, a colon, the field)."), false)),
        ReadOnly: false,
        (a, _) =>
        {
            McpGrantKind? kind = a.Str("kind") switch
            {
                null => null,
                "toggles" => McpGrantKind.Toggles,
                "movement" => McpGrantKind.Movement,
                "invoke" => McpGrantKind.Invoke,
                "field" => McpGrantKind.Field,
                _ => throw new McpToolError("'kind' must be toggles, movement, invoke or field."),
            };
            string? function = a.Str("function")?.Trim();
            if (function is not null && kind != McpGrantKind.Invoke)
                throw new McpToolError("'function' goes with kind invoke.");
            string? field = a.Str("field")?.Trim();
            if (field is not null && kind != McpGrantKind.Field)
                throw new McpToolError("'field' goes with kind field.");
            int revoked = grants.Revoke(kind, target: function ?? field);
            var left = new JsonArray();
            foreach (var g in grants.List())
                McpJson.Append(left, McpGrants.GrantToolFor(g.Kind) + (g.Target.Length > 0 ? " " + g.Target : ""));
            var payload = new JsonObject
            {
                ["revoked"] = revoked,
                ["remaining"] = left,
                ["note"] = "The holds the AI took stay until it turns them off or the session ends, and a write already "
                           + "sent is not stopped.",
            };
            return Task.FromResult(new McpToolResult(payload));
        }, Idempotent: true, Destructive: false, Role: McpToolRole.GrantAdmin);

    private static JsonArray CoveredTools(IReadOnlyList<McpTool> catalogue, McpGrantKind kind)
    {
        var arr = new JsonArray();
        foreach (var t in catalogue)
            if (t.EffectiveRole is McpToolRole.GrantedWrite or McpToolRole.GrantedInvoke or McpToolRole.GrantedField
                && t.GrantKind == kind)
                McpJson.Append(arr, t.Name);
        return arr;
    }

    private static JsonObject Describe(McpGrantRecord g, IMcpGame game)
    {
        var (games, _) = game.ListGames();
        var info = games.FirstOrDefault(x => x.Pid == g.Pid);
        var o = new JsonObject
        {
            ["tool"] = McpGrants.GrantToolFor(g.Kind),
            ["covers"] = McpGrants.Covers(g.Kind),
            ["pid"] = g.Pid,
            ["exe"] = info?.Exe ?? "",
            ["granted_at"] = g.GrantedAtUtc.ToString("u", CultureInfo.InvariantCulture),
            ["uses"] = g.Uses,
            // The same pid listed may be a later run, which the grant does not cover; the gate tells them apart.
            ["game_running"] = info is not null,
        };
        if (g.Target.Length > 0) o[g.Kind == McpGrantKind.Field ? "field" : "function"] = g.Target;
        return o;
    }

    private static JsonObject Game(IMcpGame game, long pid)
    {
        var (games, _) = game.ListGames();
        return new JsonObject
        {
            ["pid"] = (int)pid,   // as read_games gives it
            ["exe"] = games.FirstOrDefault(x => x.Pid == pid)?.Exe ?? "",
        };
    }
}
