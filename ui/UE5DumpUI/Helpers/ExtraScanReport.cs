using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Helpers;

/// <summary>
/// What the Pointers panel says once an Extra Scan's result was applied, from the job's status and apply_rescan's reply.
/// [RESCAN-DECOY-REINIT] A replace names the GObjects apply chose, which need not be the job's first hit; an apply
/// that acted on nothing says so instead of "Found".
/// </summary>
public static class ExtraScanReport
{
    /// <param name="ResultText">The result line.</param>
    /// <param name="StatusText">The status line.</param>
    /// <param name="Applied">The apply published something (the status line says so).</param>
    public readonly record struct Report(string ResultText, string StatusText, bool Applied);

    public static Report Describe(RescanStatusResult status, ApplyRescanResult applied)
    {
        string outcome = applied.GObjectsOutcome;
        string addr = applied.State.GObjectsAddr;
        if (status.FoundGObjects && outcome == Constants.RescanOutcomeReplaced)
            return new Report(Res.Format("str.Pointers.Scan.Replaced", addr, applied.State.ObjectCount),
                              Res.Get("str.Pointers.Scan.Applied"), true);
        // The published GObjects was kept: it reads objects now (the engine was not up when init ran).
        if (status.FoundGObjects && outcome == Constants.RescanOutcomeReinitInPlace)
            return new Report(Res.Format("str.Pointers.Scan.ReinitInPlace", addr, applied.State.ObjectCount),
                              Res.Get("str.Pointers.Scan.Applied"), true);

        // A fill publishes the job's first hit; a DLL that predates the outcome key only ever filled.
        bool gobjectsApplied = status.FoundGObjects && (outcome == "" || outcome == Constants.RescanOutcomeFilled);
        var parts = new List<string>();
        if (gobjectsApplied) parts.Add(Res.Format("str.Pointers.Scan.FoundGObjects", addr));
        if (status.FoundGWorld) parts.Add(Res.Format("str.Pointers.Scan.FoundGWorld", status.GWorldAddr));
        if (status.FoundGObjects && !gobjectsApplied)
        {
            string why = outcome == Constants.RescanOutcomeNoneQualified
                ? Res.Get("str.Pointers.Scan.NoneQualified")
                : Res.Format("str.Pointers.Scan.NotApplied", outcome);
            if (parts.Count == 0)
                return new Report(why, Res.Get("str.Pointers.Scan.CompleteNoResults"), false);
            // GWorld applied in the same run: say both.
            return new Report(Res.Format("str.Pointers.Scan.Found", string.Join(", ", parts)) + " " + why,
                              Res.Get("str.Pointers.Scan.Applied"), true);
        }
        return new Report(Res.Format("str.Pointers.Scan.Found", string.Join(", ", parts)),
                          Res.Get("str.Pointers.Scan.Applied"), true);
    }
}
