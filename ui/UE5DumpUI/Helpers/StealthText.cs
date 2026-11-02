namespace UE5DumpUI.Helpers;

/// <summary>
/// [SOLIDE-RELATED-STOPS] Detect Stealth Meter's words for a search that stopped short. The search scores the fields of
/// the player's pawn and the objects it owns, the Related Objects walk's list, so a meter past where that walk stopped
/// is not found. The causes are the walk's own (<see cref="Core.PartialResultNotice.RelatedStopCauses"/>).
/// </summary>
public static class StealthText
{
    internal const string KeyStopsLead = "str.TP.StealthStopsLead";

    /// <summary>The status line's clause, or "" when the search finished or an older DLL did not say.</summary>
    public static string StopsClause(Models.RelatedObjectsStops? s)
    {
        var parts = Core.PartialResultNotice.RelatedStopCauses(s);
        return parts.Count == 0 ? ""
            : AobMakerStatus.Say(KeyStopsLead,
                " ⚠ Not every object the player owns was searched, so a meter may have been missed: {0}.",
                string.Join("; ", parts));
    }
}
