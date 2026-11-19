using UE5DumpUI.Core;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [W1-PIPEBUSY-STATUS] The shared user-facing sentence for "the AOBMaker bridge is not reachable", chosen by
/// <see cref="IAobMakerBridge.LastFailure"/>.
///
/// <para><b>Why this type exists.</b> A busy or refused pipe is not a missing plugin, and the remedy differs: the
/// AOBMaker server is single-instance, so a second Cheat Engine's plugin fails <c>CreateNamedPipe</c> (231) and never
/// serves -- "open Cheat Engine with the AOBMaker plugin loaded" cannot help a pipe another client holds. A caller
/// that takes its sentence from here reports the bridge's last failure; text written elsewhere for the same state can
/// call a busy or refused pipe a missing plugin. [W1-PIPEBUSY-STATUS]</para>
///
/// <para>⚠ The toolbar status is a fixed 360-wide slot with an ellipsis (about 50 characters visible, see
/// <see cref="ClipboardDelivery.FailureText"/>), so each sentence puts its discriminating word ("not connected" /
/// "busy" / "refused") in the first ~25 characters. No trailing period: callers append a clause.</para>
/// </summary>
internal static class AobMakerUnavailable
{
    internal const string KeyAbsent = "str.AobMaker.Unavailable.Absent";
    internal const string KeyBusy   = "str.AobMaker.Unavailable.Busy";
    internal const string KeyDenied = "str.AobMaker.Unavailable.Denied";
    internal const string KeyFailed = "str.AobMaker.Unavailable.Failed";

    /// <summary>The en.axaml key for <paramref name="failure"/>. <see cref="AobMakerFailure.None"/> maps to
    /// <see cref="KeyAbsent"/>: a caller asking for this text after a connect that SUCCEEDED is looking at a pipe
    /// that broke mid-request (CE closed), which the absent wording fits.</summary>
    internal static string KeyFor(AobMakerFailure failure) => failure switch
    {
        AobMakerFailure.Busy   => KeyBusy,
        AobMakerFailure.Denied => KeyDenied,
        AobMakerFailure.Failed or AobMakerFailure.Cancelled => KeyFailed,
        _ => KeyAbsent,
    };

    /// <summary>The status sentence for <paramref name="bridge"/>'s last failure. A null bridge reads as absent.</summary>
    internal static string Text(IAobMakerBridge? bridge)
    {
        var failure = bridge?.LastFailure ?? AobMakerFailure.Absent;
        var resolved = Res.Get(KeyFor(failure));
        return string.IsNullOrEmpty(resolved) ? Fallback(failure) : resolved;
    }

    /// <summary>
    /// <c>Res.Get</c> returns an EMPTY string for a key it cannot resolve (and always, headless), and an empty status
    /// line tells the user nothing — so each case keeps a short literal core, the
    /// <c>PointerPanelViewModel.OrFallback</c> pattern. en.axaml supplies the full wording.
    /// </summary>
    private static string Fallback(AobMakerFailure failure) => failure switch
    {
        AobMakerFailure.Busy   => "AOBMaker pipe busy — another program holds its only connection",
        AobMakerFailure.Denied => "AOBMaker pipe refused this app (access denied)",
        AobMakerFailure.Failed or AobMakerFailure.Cancelled => "AOBMaker not connected — the connection attempt failed",
        _ => "AOBMaker not connected — open Cheat Engine with the AOBMaker plugin loaded",
    };
}
