using UE5DumpUI.Core;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [W1-PIPEBUSY-STATUS] The one user-facing sentence for "the AOBMaker bridge is not reachable", chosen by
/// <see cref="IAobMakerBridge.LastFailure"/>.
///
/// <para><b>Why this type exists.</b> 62f1596b taught the bridge to LOG a busy pipe as busy, but every status line
/// still said "open Cheat Engine with the AOBMaker plugin loaded" — live-verified on L64 (2026-09-22), where the
/// toolbar ⟳ gave that remedy for a pipe that existed and was held by another client. Opening Cheat Engine cannot
/// help there: the AOBMaker server is single-instance, so a second CE's plugin fails <c>CreateNamedPipe</c> (231)
/// and never serves. Shared so the toolbar, the Tools-menu injects, Live Walker and Teleport cannot disagree about
/// the same failure.</para>
///
/// <para>⚠ The toolbar status is capped at <c>MaxWidth="360"</c> with an ellipsis (about 50 characters visible, see
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
    /// that broke mid-request (CE closed), which is the old wording's case.</summary>
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
    /// line is the "no message" defect again — so each case keeps a short literal core, the
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
