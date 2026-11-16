using UE5DumpUI.Models;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] §4.10. The words on a gameplay card that say who other than this window holds its
/// feature, from get_holds -- or, for See-through, from its claims. Once grants took the per-call prompt away, this is
/// how the user sees what an AI session holds; the card's OFF or Reset still takes it over, since the window's
/// requests carry no if_free.
///
/// A holder counts only while its hold is in force (or an ON is on its way): the DLL names the last holder even after
/// the hold ended. A holder pid that no longer runs is said to have exited -- typically this window's own holds from
/// before a restart, which outlive it under its old pid -- though a reused pid would name another process.
/// </summary>
internal static class HolderText
{
    internal const string KeyAi = "str.TP.HolderAi";
    internal const string KeyAiNoPid = "str.TP.HolderAiNoPid";
    internal const string KeyCe = "str.TP.HolderCe";
    internal const string KeyOther = "str.TP.HolderOther";
    internal const string KeyOtherNoPid = "str.TP.HolderOtherNoPid";
    internal const string KeyExited = "str.TP.HolderExited";
    internal const string KeyUnrecorded = "str.TP.HolderUnrecorded";
    internal const string KeyReleaseOwed = "str.TP.HolderReleaseOwed";
    internal const string KeyStOne = "str.TP.HolderStOne";
    internal const string KeyStMany = "str.TP.HolderStMany";
    internal const string KeyStCeOne = "str.TP.HolderStCeOne";
    internal const string KeyStCeMany = "str.TP.HolderStCeMany";

    /// <summary>The label for one hold, or "" when nobody but this window holds it. <paramref name="exited"/> answers
    /// whether a holder pid no longer runs.</summary>
    internal static string For(HoldEntry? hold, Func<long, bool> exited)
    {
        if (hold is null || !hold.InForce || hold.Holder.You) return "";
        var h = hold.Holder;
        if (h.Kind == Constants.HoldKindCe) return AobMakerStatus.Say(KeyCe, "Held by Cheat Engine");
        if (h.Kind != Constants.HoldKindClient)
            return AobMakerStatus.Say(KeyUnrecorded, "Held by a program that recorded no owner");
        if (h.Pid is long pid && exited(pid))
            return AobMakerStatus.Say(KeyExited, "Held by a program that has exited (pid {0})", pid);
        if (h.Background)
            return h.Pid is long ai
                ? AobMakerStatus.Say(KeyAi, "Held by an AI session (pid {0})", ai)
                : AobMakerStatus.Say(KeyAiNoPid, "Held by an AI session");
        return h.Pid is long other
            ? AobMakerStatus.Say(KeyOther, "Held by another program (pid {0})", other)
            : AobMakerStatus.Say(KeyOtherNoPid, "Held by another program");
    }

    /// <summary>See-through's label: its claims carry no pid, so it counts the ones that are neither this window's nor
    /// Cheat Engine's. When the DLL does not say whether the window holds one (before 3714), only Cheat Engine's is
    /// named: counting the window's own claim as another's would send the user after a holder that is not there.
    /// </summary>
    internal static string ForSeeThrough(SeeThroughStatus status)
    {
        if (!status.Active) return "";
        int others = status.You is bool mine
            ? Math.Max(0, status.Claims - (mine ? 1 : 0) - (status.CeClaimed ? 1 : 0))
            : 0;
        if (status.CeClaimed)
            return others switch
            {
                0 => AobMakerStatus.Say(KeyCe, "Held by Cheat Engine"),
                1 => AobMakerStatus.Say(KeyStCeOne, "Held by Cheat Engine and another program or AI session"),
                _ => AobMakerStatus.Say(KeyStCeMany, "Held by Cheat Engine and {0} other programs or AI sessions",
                    others),
            };
        return others switch
        {
            0 => "",
            1 => AobMakerStatus.Say(KeyStOne, "Held by another program or AI session"),
            _ => AobMakerStatus.Say(KeyStMany, "Held by {0} other programs or AI sessions", others),
        };
    }
}
