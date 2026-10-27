using UE5DumpUI.Core;

namespace UE5DumpUI.Helpers;

/// <summary>
/// What a CONVENIENCE copy -- an address, a path, a name -- says when the clipboard refused the write.
///
/// <para>These copies end in a status line naming what was copied, and that line used to be set whatever
/// <see cref="IPlatformService.CopyToClipboardAsync"/> returned. Its bool is the only failure signal (it never throws
/// for an ordinary clipboard failure, [PASTECRASH-2026-08-18]), so a refused write still read "Copied 7FF6...": the
/// user then pastes the PREVIOUS clipboard into Cheat Engine instead of re-clicking. Each such site now claims the
/// copy only on true and shows this line, on the same status line, on false.</para>
///
/// <para>Kept apart from <see cref="ClipboardDelivery"/>: that type is for a generated script whose loss runs the
/// wrong script, and its gate's predicate is that payload. A copy that makes no claim at all needs nothing here.</para>
/// </summary>
internal static class ClipboardConvenience
{
    internal const string KeyNotCopied = "str.Clipboard.NotCopied";

    /// <summary>
    /// The line for a refused copy of <paramref name="what"/>, the text that did not arrive. The imperative leads,
    /// as in <see cref="ClipboardDelivery.FailureText"/>: a status line can be cut short with an ellipsis, and the
    /// caller's text varies in length ([CLIPELLIPSIS-2026-09-09]).
    /// </summary>
    internal static string NotCopiedText(string what)
        => AobMakerStatus.Say(KeyNotCopied,
            "Not copied, so do not paste: the clipboard still holds what was there before ({0} did not arrive). " +
            "Try again.", what);
}
