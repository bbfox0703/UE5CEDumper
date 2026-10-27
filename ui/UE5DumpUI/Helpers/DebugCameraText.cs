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

    /// <summary><c>Res.Get</c> answers "" for a key it cannot resolve, and always does headless, so each sentence keeps
    /// its literal (the <see cref="AobMakerStatus"/> pattern).</summary>
    private static string Say(string key, string fallback)
    {
        var resolved = Res.Get(key);
        return string.IsNullOrEmpty(resolved) ? fallback : resolved;
    }
}
