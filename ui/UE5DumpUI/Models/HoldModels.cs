namespace UE5DumpUI.Models;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] §4.10. What <c>get_holds</c> reports (DLL 3704+): every gameplay hold whose owner the
/// DLL records, whether it is in force, and who holds it. The window labels each gameplay card from it. Hold names
/// are <see cref="Constants"/>' Hold* words, matched whole and ordinally.
/// </summary>
public sealed class HoldsReport
{
    public IReadOnlyList<HoldEntry> Holds { get; init; } = Array.Empty<HoldEntry>();

    /// <summary>The entry for <paramref name="hold"/>, or null when the DLL listed none (a force-field job that does
    /// not exist, a hold an older DLL does not know).</summary>
    public HoldEntry? Find(string hold)
    {
        foreach (var h in Holds)
            if (string.Equals(h.Hold, hold, StringComparison.Ordinal)) return h;
        return null;
    }
}

/// <summary>One hold. The DLL names a holder even for a hold no longer in force, so a holder counts only while
/// <see cref="Active"/> or <see cref="Pending"/>.</summary>
public sealed class HoldEntry
{
    public string Hold { get; init; } = "";
    public bool Active { get; init; }
    /// <summary>A debug-camera ON still on its way (DLL 3706+): held by its sender already.</summary>
    public bool Pending { get; init; }
    /// <summary>An ended AI session's debug camera the DLL turns off once the game thread runs (DLL 3707+).</summary>
    public bool ReleaseOwed { get; init; }
    public HoldOwner Holder { get; init; } = new();

    public bool InForce => Active || Pending;
}

/// <summary>Who holds a hold, as <c>get_holds</c> says it.</summary>
public sealed class HoldOwner
{
    /// <summary><c>none</c> (no owner recorded), <c>ce</c> (a Cheat Engine record), or <c>client</c> (a pipe client).
    /// </summary>
    public string Kind { get; init; } = "none";
    /// <summary>The holder's process id; null when the DLL keyed the owner by its connection (its pid was unreadable).
    /// </summary>
    public long? Pid { get; init; }
    /// <summary>A client whose requests are marked background: an MCP server, i.e. an AI session.</summary>
    public bool Background { get; init; }
    /// <summary>This window's own process holds it (both of its pipe lanes share the owner).</summary>
    public bool You { get; init; }
}
