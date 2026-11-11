using System.Globalization;

namespace UE5DumpUI.Core;

/// <summary>What Connect does with the pipes it finds under \\.\pipe\.</summary>
public enum ConnectPlan
{
    /// <summary>No game serves UE5Dumper's pipe, old or new.</summary>
    NoPipe,
    /// <summary>Only the legacy name: a game runs an old DLL. Refuse, with an explanation, and show no list.</summary>
    LegacyOnly,
    /// <summary>Exactly one game, and no legacy pipe: connect at once.</summary>
    ConnectOne,
    /// <summary>Several games, or one beside the legacy pipe: let the user choose.</summary>
    Choose,
}

/// <summary>The dumper pipes found under \\.\pipe\: each game's pid (ascending), and whether the legacy name is there.</summary>
public sealed record PipeScan(IReadOnlyList<int> Pids, bool LegacyPresent);

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] The pipe name per game process, \\.\pipe\UE5DumpBfx_&lt;pid&gt;, and what Connect
/// makes of the names it finds. Pure: the listing itself comes from <see cref="IPlatformService.ListNamedPipes"/>.
/// <para>A listed name is always a live server: a pipe leaves \\.\pipe\ the moment its server process dies, a killed
/// one included (measured 2026-11-06). So a pid read from a name is a running game, never a stale entry.</para>
/// </summary>
public static class PipeTargets
{
    /// <summary>The pipe a game process serves, without the \\.\pipe\ path: what NamedPipeClientStream takes.</summary>
    public static string NameFor(int pid) => Constants.PipeNamePrefix + pid.ToString(CultureInfo.InvariantCulture);

    /// <summary>The pid in a per-process pipe name, or false for any other name. Exact: the prefix, then only ASCII
    /// digits, a positive pid. Windows compares pipe names without case, so the prefix does too. A rig's own pipe
    /// (pipe_remote_probe's "UE5DumpBfx-probe-...") is not a game.</summary>
    public static bool TryParsePid(string? pipeName, out int pid)
    {
        pid = 0;
        if (pipeName is null || !pipeName.StartsWith(Constants.PipeNamePrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var digits = pipeName.AsSpan(Constants.PipeNamePrefix.Length);
        if (digits.Length == 0) return false;
        foreach (char c in digits)
            if (c is < '0' or > '9') return false;
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out pid) && pid > 0;
    }

    /// <summary>The game pids (distinct, ascending) and the legacy name among <paramref name="pipeNames"/>.</summary>
    public static PipeScan Scan(IEnumerable<string> pipeNames)
    {
        var pids = new SortedSet<int>();
        bool legacy = false;
        foreach (var name in pipeNames)
        {
            if (TryParsePid(name, out int pid)) pids.Add(pid);
            else if (string.Equals(name, Constants.LegacyPipeName, StringComparison.OrdinalIgnoreCase)) legacy = true;
        }
        return new PipeScan(pids.ToList(), legacy);
    }

    /// <summary>One game alone connects at once. Several, or one beside the legacy pipe, need a choice: the legacy
    /// entry is shown so the user learns an old DLL is running, but it cannot be chosen. The legacy pipe alone is a
    /// refusal, not a list of one entry nobody can pick.</summary>
    public static ConnectPlan Decide(PipeScan scan) => scan.Pids.Count switch
    {
        0 => scan.LegacyPresent ? ConnectPlan.LegacyOnly : ConnectPlan.NoPipe,
        1 when !scan.LegacyPresent => ConnectPlan.ConnectOne,
        _ => ConnectPlan.Choose,
    };
}
