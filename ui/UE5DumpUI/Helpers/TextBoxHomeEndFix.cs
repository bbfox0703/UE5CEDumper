using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Works around Avalonia's TextBox putting the caret at the WRONG END when Home / End (or
/// Ctrl+Home / Ctrl+End, or a page key) is pressed with text selected [TEXTBOX-HOMEEND-CARET].
/// </summary>
/// <remarks>
/// <para>Measured on build 3574 in the Interesting Funcs keyword box: a click on the box's padding
/// selects its whole text (AutoCompleteBox does that on focus), End then put the caret at the
/// START, and what was typed next went in front: "spawn" and " act" became " actspawn", a keyword
/// with no rows.</para>
/// <para>Cause, read from Avalonia 12.1.3's <c>TextBox.OnKeyDown</c>: each of the four plain moves
/// sets <c>CaretIndex</c> to its target and then calls <c>ClearSelection()</c>, which puts the
/// caret at <c>SelectionStart</c>. Moving the caret normally collapses the selection onto it
/// first; but when the caret already sits at the target, <c>CaretIndex</c> does not change,
/// nothing collapses, and the caret lands on the selection's start — End jumps to the start of a
/// select-all (the mirror case for Home needs the caret at the start with the selection anchored
/// at the end, which keyboard input does not produce in 12.1.3; it is covered anyway). The page
/// keys share the sink and never move the caret at all, so with a selection they ALWAYS land on
/// its start (found by review, confirmed on a real TextBox).</para>
/// <para>So the selection is collapsed BEFORE the TextBox handles the key; its own move then lands
/// on the target whether the caret moves or not. It collapses onto the ACTIVE end,
/// <c>SelectionEnd</c> — where the caret shows — not onto <c>CaretIndex</c>: keyboard selection
/// (Shift+arrows) leaves <c>CaretIndex</c> at the anchor, so in a multi-line box a selection made
/// down across lines would otherwise move on the anchor's line (found by review, pinned by the
/// headless tests). Every TextBox gets it — the inner boxes of AutoCompleteBox and NumericUpDown,
/// and every window — through one tunnelling class handler registered at startup
/// (<see cref="Register"/>).</para>
/// </remarks>
public static class TextBoxHomeEndFix
{
    /// <summary>Install the fix for every TextBox in the application. Call once, at startup; the
    /// returned handle removes it again (the headless tests compare with and without it).</summary>
    public static IDisposable Register()
        => InputElement.KeyDownEvent.AddClassHandler<TextBox>(OnKeyDown, RoutingStrategies.Tunnel);

    /// <summary>True when <paramref name="e"/> is one of <paramref name="moves"/> — the platform's
    /// plain Home / End / Ctrl+Home / Ctrl+End and page gestures, the moves that end in
    /// <c>ClearSelection()</c>. The Shift variants extend the selection and are not among them.</summary>
    public static bool IsSelectionClearingMove(KeyEventArgs e, params IEnumerable<KeyGesture>[] moves)
    {
        foreach (var gestures in moves)
            foreach (var g in gestures)
                if (g.Matches(e)) return true;
        return false;
    }

    /// <summary>The selection to set before the TextBox moves the caret: collapsed onto its active
    /// end when something is selected, null when nothing is (then the TextBox is already right).</summary>
    public static (int Start, int End)? CollapseOntoActiveEnd(int selectionStart, int selectionEnd)
        => selectionStart == selectionEnd ? null : (selectionEnd, selectionEnd);

    private static void OnKeyDown(TextBox box, KeyEventArgs e)
    {
        if (e.Handled) return;
        var keymap = Application.Current?.PlatformSettings?.HotkeyConfiguration;
        if (keymap == null || !IsSelectionClearingMove(e,
                keymap.MoveCursorToTheStartOfLine, keymap.MoveCursorToTheEndOfLine,
                keymap.MoveCursorToTheStartOfDocument, keymap.MoveCursorToTheEndOfDocument,
                keymap.PageUp, keymap.PageDown, keymap.PageLeft, keymap.PageRight)) return;
        if (CollapseOntoActiveEnd(box.SelectionStart, box.SelectionEnd) is not { } sel) return;
        // SetCurrentValue, as the TextBox itself does: a local value would replace a binding.
        // SelectionStart first: meeting SelectionEnd, it moves CaretIndex there too, which
        // collapses the selection and puts the presenter's caret on the active end.
        box.SetCurrentValue(TextBox.SelectionStartProperty, sel.Start);
        box.SetCurrentValue(TextBox.SelectionEndProperty, sel.End);
    }
}
