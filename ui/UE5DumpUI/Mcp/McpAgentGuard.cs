using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UE5DumpUI.Mcp;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] <c>UE5DumpUI.exe --mcp-agent-guard</c> as a Claude Code PreToolUse hook:
/// it refuses a write or a grant asked for by a subagent or a workflow agent.
///
/// Subagents and workflow agents call the session's own MCP server, so they hold the
/// grants the user gave the main conversation, and a subagent's grant prompt looks exactly like the main one's. The
/// server cannot tell them apart; the hook input can, since Claude Code gives a call made inside a subagent an
/// <c>agent_id</c>. The exe is the hook so that every user of a release has it, with nothing else installed.
///
/// It decides only for write_* and grant_* tools; reads, scan_start and revoke_grants (which only narrows) go to the
/// usual rules. Input it cannot read is refused: a guard that let an unreadable call through would guard nothing.
/// </summary>
internal static partial class McpAgentGuard
{
    private const string Reason =
        "UE5CEDumper: a subagent or workflow agent may not change the game. Only the main conversation may, where the "
        + "user approves each grant; ask it to make the change.";

    // Any server's name: the matcher in the user's settings already narrows the hook to this one.
    [GeneratedRegex("^mcp__[A-Za-z0-9_-]+?__(?:write|grant)_", RegexOptions.CultureInvariant)]
    private static partial Regex GuardedTool();

    /// <summary>The hook's output for one call: the deny decision, or null to leave the call to the usual rules.
    /// </summary>
    public static string? Decide(string hookInput)
    {
        JsonObject? input;
        try
        {
            input = JsonNode.Parse(hookInput) as JsonObject;
        }
        catch (JsonException)
        {
            input = null;
        }
        if (input is null) return Deny();
        string? tool = McpJson.GetString(input, "tool_name");
        if (tool is not null && !GuardedTool().IsMatch(tool)) return null;
        // Any agent_id but an empty one: a value of an unexpected shape still says the call came from an agent.
        bool subagent = input["agent_id"] is { } id
                        && !(id is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == "");
        return subagent ? Deny() : null;
    }

    private static string Deny() => new JsonObject
    {
        ["hookSpecificOutput"] = new JsonObject
        {
            ["hookEventName"] = "PreToolUse",
            ["permissionDecision"] = "deny",
            ["permissionDecisionReason"] = Reason,
        },
    }.ToJsonString();
}
