using System.Threading.Tasks;
using UE5DumpUI.Core;

namespace UE5DumpUI.Helpers;

/// <summary>
/// The clipboard write for a DELIVERY copy — one where the clipboard IS the
/// deliverable, not a convenience.
///
/// <para><b>Why this type exists.</b> The 2026-09-08 blind-spot sweep classified all
/// 57 <see cref="IPlatformService.CopyToClipboardAsync"/> call sites. 55 dropped the
/// result and 29 of those then claimed success anyway. The population splits cleanly,
/// and the split is what makes it fixable:</para>
/// <list type="bullet">
///   <item><b>Convenience copy</b> (26 sites) — an address, a name, a class name. If it
///   silently does nothing the user re-clicks. Dropping the result there is a nuisance,
///   not a defect, and wiring 26 of them would be noise.</item>
///   <item><b>Delivery copy</b> (14 sites) — a generated CE script / memory-record XML
///   that the status line then tells the user to <i>paste into Cheat Engine</i>. If the
///   write did nothing, the user pastes <b>whatever was on the clipboard before</b> —
///   an older AA script from an earlier button press — into CE and runs it against a
///   live game. Every one of those 14 was a defect; none of the 26 was.</item>
/// </list>
///
/// <para><b>The result is the only signal.</b>
/// <see cref="IPlatformService.CopyToClipboardAsync"/> documents that it never throws
/// for an ordinary clipboard failure — that is the contract, because these are all
/// <c>[RelayCommand]</c>s and a faulted async command reaching the dispatcher closes the
/// app ([PASTECRASH-2026-08-18]). So the <c>try/catch</c> most of these sites already
/// have is <b>dead code for a real clipboard failure</b>, and only the returned
/// <c>bool</c> can tell the caller whether to claim success.</para>
///
/// <para><b>Shape to copy</b>, modelled on
/// <c>PointerPanelViewModel.ReportSymbolRegistration</c> — one shared reporter, so two
/// cards cannot report the same outcome differently:</para>
/// <code>
///   if (!await ClipboardDelivery.TryAsync(_platform, xml))
///   {
///       SetError(ClipboardDelivery.FailureText("the CE AA script"));
///       return;
///   }
///   StatusText = "... copied ...";
/// </code>
///
/// <para>⚠ Do NOT route the convenience copies through here. The whole reason
/// <c>tools/check_ce_untick_placement.py</c>'s sibling gate can exist is that the
/// delivery predicate has an <b>empty</b> legitimate population — a discarded copy whose
/// payload is a generated script is always wrong. Diluting that with 26 legitimate
/// address copies is what turns a 0-baseline check into a 26-line waiver list, which is
/// the design this repo already refuted twice (see <c>check_property_family.py</c>'s
/// header, and working-lessons §2.18).</para>
/// </summary>
internal static class ClipboardDelivery
{
    /// <summary>
    /// Write <paramref name="payload"/> to the clipboard. Returns <c>true</c> only when
    /// it actually arrived — a null platform counts as failure, because the caller is
    /// about to claim delivery either way.
    /// </summary>
    internal static async Task<bool> TryAsync(IPlatformService? platform, string payload)
    {
        if (platform is null || string.IsNullOrEmpty(payload)) return false;
        return await platform.CopyToClipboardAsync(payload).ConfigureAwait(true);
    }

    /// <summary>
    /// The one message a failed delivery shows. <paramref name="what"/> names the thing
    /// that did NOT arrive, e.g. "the CE AA script" — it is read mid-sentence.
    /// </summary>
    /// <remarks>
    /// Says <b>NOT delivered</b> rather than "copy failed": the actionable part is that
    /// the clipboard still holds something else, so pasting now runs the wrong script.
    /// </remarks>
    internal static string FailureText(string what) =>
        $"ERROR: could not write to the clipboard — {what} was NOT delivered. " +
        "The clipboard still holds whatever was there before, so do not paste. " +
        "Another application may be holding it open; try again.";
}
