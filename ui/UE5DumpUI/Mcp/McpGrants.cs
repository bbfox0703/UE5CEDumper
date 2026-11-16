using System.Text.Json.Nodes;
using UE5DumpUI.Core;

namespace UE5DumpUI.Mcp;

public enum McpWriteMode { Default, AnyHost, Off }

public enum McpHostDecision { ReadOnly, MarkedPerCall, Grants }

public enum McpGrantKind { None, Toggles, Movement }

public enum McpToolRole { Read, Scan, GrantedWrite, PerCallWrite, Release, Grant, GrantAdmin }

public sealed record McpGrantRecord(long Id, McpGrantKind Kind, long Pid, string CreationTime, DateTime GrantedAtUtc,
    long Uses);

public sealed class McpGrantCapability
{
    internal McpGrantCapability(long grantId, McpGrantKind kind, long pid, string creationTime)
    {
        GrantId = grantId;
        Kind = kind;
        Pid = pid;
        CreationTime = creationTime;
    }

    public long GrantId { get; }
    public bool PerCall => GrantId == 0;
    public McpGrantKind Kind { get; }
    public long Pid { get; }
    public string CreationTime { get; }
}

// RED: the surface only; McpGrantsTests pins the behaviour.
public sealed class McpGrants
{
    public McpGrants(McpWriteMode mode = McpWriteMode.Default, ILoggingService? log = null) => Mode = mode;

    public McpWriteMode Mode { get; }
    public McpHostDecision Decision => McpHostDecision.ReadOnly;
    public bool IsDecided => false;

    public McpHostDecision Decide(string? clientName, string? clientVersion) => McpHostDecision.ReadOnly;

    public static McpHostDecision DecideFor(McpWriteMode mode, string? clientName, string? clientVersion)
        => McpHostDecision.ReadOnly;

    public static IReadOnlySet<string> ReadCommands { get; } = new HashSet<string>();
    public static IReadOnlySet<string> ReleaseCommands { get; } = new HashSet<string>();
    public static IReadOnlySet<string> PerCallCommands { get; } = new HashSet<string>();
    public static IReadOnlySet<string> CommandsOf(McpGrantKind kind) => new HashSet<string>();
    public static McpGrantKind KindOf(string? cmd) => McpGrantKind.None;
    public static bool IsRelease(JsonObject request) => false;

    public McpGrantRecord? Grant(McpGrantKind kind, JsonObject ptrs, out string? refusal)
    {
        refusal = null;
        return null;
    }

    public McpGrantCapability? Admit(McpToolRole role, McpGrantKind kind, JsonObject ptrs, out string? refusal)
    {
        refusal = null;
        return null;
    }

    public int Revoke(McpGrantKind? kind = null, long? pid = null) => 0;
    public IReadOnlyList<McpGrantRecord> List() => [];
    public int Clear() => 0;

    public string? Check(JsonObject request, McpGrantCapability? cap, long connectedPid, string? connectedCreationTime)
        => null;
}
