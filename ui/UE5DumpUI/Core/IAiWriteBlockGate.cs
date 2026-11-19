using System;

namespace UE5DumpUI.Core;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] The window's "Block AI writes" switch. While it is on, every MCP server on this
/// machine (for this Windows user) refuses every change but an OFF or a reset of its own holds, and each turn-on ends
/// every AI session's grants. Only the window writes it; the servers only read it (Services.AiWriteBlockGate.ReadState),
/// so a forged file can at most block or revoke, never grant.
/// </summary>
public interface IAiWriteBlockGate
{
    /// <summary>The switch as last written or read.</summary>
    bool IsBlocked { get; }

    /// <summary>Bumped at every turn-on; a server that sees it change ends every grant.</summary>
    long RevokeStamp { get; }

    /// <summary>Turns the switch on or off and persists it. False when the file could not be written: the switch is
    /// then as it was, and the caller must say so, since a stop control that silently did nothing stops nothing.
    /// </summary>
    bool TrySetBlocked(bool blocked);

    /// <summary>Reads the file again, so the window never shows a tick the servers do not see (the file can be changed
    /// by another window or by hand). The read runs off the caller's thread, since the window re-reads it every two
    /// seconds; <see cref="Changed"/> is raised back on the caller's context when what it shows changes.</summary>
    Task ReloadAsync();

    /// <summary>Raised when <see cref="IsBlocked"/> or <see cref="RevokeStamp"/> changes.</summary>
    event EventHandler? Changed;
}
