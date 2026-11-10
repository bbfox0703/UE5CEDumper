namespace UE5DumpUI.Core;

/// <summary>What Connect does with the pipes it finds under \\.\pipe\.</summary>
public enum ConnectPlan
{
    NoPipe,
    LegacyOnly,
    ConnectOne,
    Choose,
}

/// <summary>The dumper pipes found under \\.\pipe\: each game's pid (ascending), and whether the legacy name is there.</summary>
public sealed record PipeScan(IReadOnlyList<int> Pids, bool LegacyPresent);

/// <summary>[PIPE-PER-PROCESS-2026-11-06] red: stubs until the green commit.</summary>
public static class PipeTargets
{
    public static string NameFor(int pid) => Constants.LegacyPipeName;

    public static bool TryParsePid(string? pipeName, out int pid)
    {
        pid = 0;
        return false;
    }

    public static PipeScan Scan(IEnumerable<string> pipeNames) => new(Array.Empty<int>(), false);

    public static ConnectPlan Decide(PipeScan scan) => ConnectPlan.ConnectOne;
}
