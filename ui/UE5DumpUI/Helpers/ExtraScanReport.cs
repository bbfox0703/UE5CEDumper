using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Helpers;

/// <summary>
/// What the Pointers panel says once an Extra Scan's result was applied, from the job's status and apply_rescan's reply.
/// </summary>
public static class ExtraScanReport
{
    /// <param name="ResultText">The result line.</param>
    /// <param name="StatusText">The status line.</param>
    /// <param name="Applied">Something changed: the panels refresh.</param>
    public readonly record struct Report(string ResultText, string StatusText, bool Applied);

    public static Report Describe(RescanStatusResult status, ApplyRescanResult applied)
    {
        var parts = new List<string>();
        if (status.FoundGObjects) parts.Add($"GObjects: {status.GObjectsAddr}");
        if (status.FoundGWorld) parts.Add($"GWorld: {status.GWorldAddr}");
        _ = applied;
        return new Report($"Found: {string.Join(", ", parts)}", Res.Get("str.Pointers.Scan.Applied"), true);
    }
}
