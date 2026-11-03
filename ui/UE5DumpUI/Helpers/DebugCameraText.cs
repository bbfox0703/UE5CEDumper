using UE5DumpUI.Core;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [DEBUGCAM-QUEUED-OFF-NOESCALATE] The Force ON / OFF status for a Debug Camera toggle that timed out and stays queued
/// (<see cref="Constants.DebugCameraToggleQueuedResult"/>), one wording for the Console and Teleport cards.
///
/// <para>The two directions are not symmetric. A queued ON still turns the camera on when it drains. A queued OFF
/// does not turn it off on stock UE 4.18-5.7: it drains as a toggle on the ORIGINAL controller's CheatManager, whose
/// outer is not the debug camera controller, so the stock toggle calls EnableDebugCamera, which does nothing once that
/// controller has lost its Player -- only the swap back a COMPLETED OFF escalates to turns the camera off (L83,
/// measured on DumperTest 5.4 Development). Escalating on the queued result instead is the fix the 2026-08-13 audit
/// refuted: the toggle that drains after the swap turns the camera back on.</para>
///
/// <para>[DBGCAM-QUEUEDON-UNDO-TEXT] A queued ON still says not to press again, and the reason is only that the toggle
/// is already queued. What a second queued toggle does when it drains depends on the engine: on stock UE 4.18-5.7 it
/// lands on the same CheatManager and does nothing, for the reason above; UE 5.8 (source-read) can turn the camera back
/// off. So "a second toggle would undo the first" held only on some engines.</para>
/// </summary>
internal static class DebugCameraText
{
    internal const string KeyQueuedOn = "str.DbgCam.QueuedOn";
    internal const string KeyQueuedOff = "str.DbgCam.QueuedOff";

    internal static string Queued(bool wantOn) => wantOn
        ? Say(KeyQueuedOn, "⏳ Force ON: the toggle is QUEUED — the game thread is busy (stalled or unfocused). " +
                           "It will run when the game thread is free. Do not press Force ON again: " +
                           "the toggle is already queued.")
        : Say(KeyQueuedOff, "⏳ Force OFF did not complete — the game thread is busy (stalled or unfocused), and a " +
                            "queued OFF does not turn the camera off on most engines: " +
                            "press Force OFF again once the game responds.");

    internal const string KeyOnTemplate = "str.DbgCam.OnTemplate";

    /// <summary>[INVOKE-NONSTATIC-ON-CDO] A Force's status, plus the DLL's warning when the toggle ran on the
    /// CheatManager's class default object: there is no live CheatManager, so the toggle does nothing there. Only a DLL
    /// from builds 3676-3681 sends it; a newer one refuses that ON (<see cref="NoLiveCheatManagerText"/>).</summary>
    internal static string WithTemplateNote(string status, bool? onTemplate)
        => onTemplate == true ? status + " " + OnTemplateText : status;

    internal static string OnTemplateText => Say(KeyOnTemplate,
        "⚠ The toggle targets the CheatManager's class default object: there is no live CheatManager (usual in a " +
        "Shipping build), and a toggle there does nothing.");

    internal const string KeyNoLiveCheatManager = "str.DbgCam.NoLiveCheatManager";

    /// <summary>[DEBUGCAM-FORCEON-CDO] A Force ON the DLL did not send (<see cref="Constants.DebugCameraNoLiveCheatManagerResult"/>):
    /// no CheatManager is live, and ToggleDebugCamera does nothing on its class default object.</summary>
    internal static string NoLiveCheatManagerText => Say(KeyNoLiveCheatManager,
        "⛔ Force ON was not sent — there is no live CheatManager, and ToggleDebugCamera does nothing on its class " +
        "default object. UE 5 leaves it out of Shipping builds by default; elsewhere, enter gameplay first. The camera " +
        "stays OFF.");

    internal const string KeyForceFailed = "str.DbgCam.ForceFailed";

    /// <summary>[DEBUGCAM-FORCEON-CDO] A Force answered -1. It no longer means "no live CheatManager" (that is
    /// <see cref="NoLiveCheatManagerText"/>): the state did not read, or the toggle never ran.</summary>
    internal static string ForceFailed(bool wantOn) => AobMakerStatus.Say(KeyForceFailed,
        "Force {0} failed: the Debug Camera state could not be read, or the toggle did not run (see the DLL log).",
        wantOn ? "ON" : "OFF");

    internal const string KeyStateUnknown = "str.DbgCam.StateUnknown";

    /// <summary>[DEBUGCAM-FORCEON-CDO] ↻ read -1. With no live CheatManager the state reads OFF (no Debug Camera can be
    /// live), so -1 is a read that failed, never "enter gameplay".</summary>
    internal static string StateUnknownText => Say(KeyStateUnknown,
        "Debug Camera state unknown — the DLL could not read it (see the DLL log).");

    internal const string KeyBlockedOn = "str.DbgCam.BlockedOn";
    internal const string KeyBlockedOff = "str.DbgCam.BlockedOff";

    /// <summary>[DBGCAM-OFF-OVER-QUEUED-ON] A Force the DLL did not send (<see cref="Constants.DebugCameraToggleBlockedResult"/>):
    /// an earlier toggle of the other direction is still queued, so the state it would read predates that toggle.</summary>
    internal static string Blocked(bool wantOn) => wantOn
        ? Say(KeyBlockedOn, "⏳ Force ON was not sent — an earlier Force OFF is still queued (the game thread is busy). " +
                            "Press Force ON again once the game responds.")
        : Say(KeyBlockedOff, "⏳ Force OFF was not sent — an earlier Force ON is still queued (the game thread is busy) " +
                             "and will turn the camera on. Press Force OFF again once the game responds.");

    /// <summary><c>Res.Get</c> answers "" for a key it cannot resolve, and always does headless, so each sentence keeps
    /// its literal (the <see cref="AobMakerStatus"/> pattern).</summary>
    private static string Say(string key, string fallback)
    {
        var resolved = Res.Get(key);
        return string.IsNullOrEmpty(resolved) ? fallback : resolved;
    }
}
