using System.IO;
using UE5DumpUI.Models;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Pure builder for the competing-dumper-host banner's "you are connected to X —
/// also loaded: Y, Z" split (audit X9).
///
/// The old code excluded self with <c>p.Pid != state.ProcessId</c>. When the DLL
/// reports no PID (older builds send 0), <c>state.ProcessId</c> is 0 and every real
/// process has a non-zero PID, so nothing was excluded and the game you are
/// actually connected to was listed among its own competitors. Here, when the PID
/// is unknown we identify self by module name instead — and only ONCE, so a second
/// running instance of the same game survives as a genuine competitor.
/// </summary>
internal static class CompetingHostBanner
{
    /// <summary>The banner content, or <c>null</c> when there is no ambiguity to
    /// warn about (0 or 1 hosts).</summary>
    internal readonly record struct Banner(string ConnectedLabel, List<string> Others);

    /// <summary>
    /// Build the connected-label / others split. <paramref name="connectedPid"/> ≤ 0
    /// means the DLL did not report a PID; self is then matched by
    /// <paramref name="connectedModule"/>.
    /// </summary>
    internal static Banner? Build(
        IReadOnlyList<GameProcessInfo> hosts, int connectedPid, string connectedModule)
    {
        if (hosts == null || hosts.Count <= 1) return null;

        string connectedLabel = connectedPid > 0
            ? $"{connectedModule} (PID {connectedPid})"
            : connectedModule;

        var others = new List<string>(hosts.Count);
        bool selfExcluded = false;
        foreach (var p in hosts)
        {
            bool isSelf;
            if (connectedPid > 0)
            {
                isSelf = p.Pid == connectedPid;
            }
            else
            {
                // No PID from the DLL: identify self by module name, once. Two
                // instances of the same game => one is self, one is a real
                // competitor that must NOT be excluded.
                isSelf = !selfExcluded && NameMatchesModule(p.Name, connectedModule);
            }

            if (isSelf) { selfExcluded = true; continue; }
            others.Add($"{p.Name} (PID {p.Pid})");
        }

        return new Banner(connectedLabel, others);
    }

    /// <summary>Whether a process file name refers to the connected module.
    /// Tolerant of the <c>.exe</c> extension being present on one side only, and
    /// case-insensitive (Windows file names).</summary>
    internal static bool NameMatchesModule(string procName, string moduleName)
    {
        if (string.IsNullOrEmpty(procName) || string.IsNullOrEmpty(moduleName))
            return false;
        if (string.Equals(procName, moduleName, StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(
            Path.GetFileNameWithoutExtension(procName),
            Path.GetFileNameWithoutExtension(moduleName),
            StringComparison.OrdinalIgnoreCase);
    }
}
