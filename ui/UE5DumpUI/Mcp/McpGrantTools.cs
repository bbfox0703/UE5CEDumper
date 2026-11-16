using System.Globalization;
using System.Text.Json.Nodes;

namespace UE5DumpUI.Mcp;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] Phase 1. The tools that give, list and end the session's write grants.
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
    /// <summary>The four tools. <paramref name="catalogue"/> is read when a grant is given, so the tools a grant covers
    /// are the catalogue's own, by role and kind, never a second list kept here.</summary>
    public static IEnumerable<McpTool> Create(IMcpGame game, McpGrants grants, IReadOnlyList<McpTool> catalogue)
    {
        yield return GrantTool(game, grants, catalogue, McpGrantKind.Toggles, Constants.McpGrantToolToggles,
            "Grant: gameplay toggles", Constants.McpGrantAckToggles);
        yield return GrantTool(game, grants, catalogue, McpGrantKind.Movement, Constants.McpGrantToolMovement,
            "Grant: movement and teleport", Constants.McpGrantAckMovement);
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
                               + "every call.",
            };
            return Task.FromResult(new McpToolResult(payload));
        }, Role: McpToolRole.GrantAdmin);

    private static McpTool RevokeGrants(McpGrants grants) => new(
        "revoke_grants", "End write grants",
        "Ends this session's write grants: one kind (toggles or movement), or every one when kind is left out. It only "
        + "narrows what the AI may do, and it releases no hold: turn each hold off with its write_* tool.",
        McpSchema.Object(("kind", McpSchema.Enum("Which grant; leave out for every one.", "toggles", "movement"),
            false)),
        ReadOnly: false,
        (a, _) =>
        {
            McpGrantKind? kind = a.Str("kind") switch
            {
                null => null,
                "toggles" => McpGrantKind.Toggles,
                "movement" => McpGrantKind.Movement,
                _ => throw new McpToolError("'kind' must be toggles or movement."),
            };
            int revoked = grants.Revoke(kind);
            var left = new JsonArray();
            foreach (var g in grants.List()) McpJson.Append(left, McpGrants.GrantToolFor(g.Kind));
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
            if (t.EffectiveRole == McpToolRole.GrantedWrite && t.GrantKind == kind)
                McpJson.Append(arr, t.Name);
        return arr;
    }

    private static JsonObject Describe(McpGrantRecord g, IMcpGame game)
    {
        var (games, _) = game.ListGames();
        var info = games.FirstOrDefault(x => x.Pid == g.Pid);
        return new JsonObject
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
