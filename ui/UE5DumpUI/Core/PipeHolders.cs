namespace UE5DumpUI.Core;

/// <summary>One holder of a game's pipe connections, as the DLL logged it.</summary>
public sealed record PipeHolder(int Pid, string Exe, string LastCmd, bool InFlight);

/// <summary>The holders of the DLL's latest at-capacity line for one pipe: when it was logged, who, how many did not
/// fit the line, and whether a later "slot freed" line has superseded it.</summary>
public sealed record PipeHoldersSeen(string Time, IReadOnlyList<PipeHolder> Holders, int More, bool Superseded);

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage C. When every connection of a game's pipe is taken, no client can connect to
/// ask who holds them, so the DLL writes the holders on its at-capacity line in its pipe log. This reads that line.
/// </summary>
public static class PipeHolders
{
    /// <summary>The latest at-capacity line naming <paramref name="pipeName"/> (bare, e.g. UE5DumpBfx_668) in a DLL
    /// pipe log, or null when there is none or it cannot be read.</summary>
    public static PipeHoldersSeen? ParseLatest(string? logText, string pipeName) => null;   // red: not read yet
}
