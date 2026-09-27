using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Works around Avalonia's TextBox putting the caret at the WRONG END when Home / End (or
/// Ctrl+Home / Ctrl+End) is pressed with text selected [TEXTBOX-HOMEEND-CARET].
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
/// select-all, Home to the end of a selection made right to left.</para>
/// <para>So the selection is collapsed onto the caret BEFORE the TextBox handles the key; its own
/// move then lands on the target whether the caret moves or not. Every TextBox gets it — the
/// inner boxes of AutoCompleteBox and NumericUpDown, and every window — through one tunnelling
/// class handler registered at startup (<see cref="Register"/>).</para>
/// </remarks>
public static class TextBoxHomeEndFix
{
    /// <summary>Install the fix for every TextBox in the application. Call once, at startup.</summary>
    public static void Register()
        => InputElement.KeyDownEvent.AddClassHandler<TextBox>(OnKeyDown, RoutingStrategies.Tunnel);

    /// <summary>True when <paramref name="e"/> is one of <paramref name="moves"/> — the platform's
    /// plain Home / End / Ctrl+Home / Ctrl+End gestures, the four moves that end in
    /// <c>ClearSelection()</c>. The Shift variants extend the selection and are not among them.</summary>
    public static bool IsPlainHomeOrEnd(KeyEventArgs e, params IEnumerable<KeyGesture>[] moves)
    {
        foreach (var gestures in moves)
            foreach (var g in gestures)
                if (g.Matches(e)) return true;
        return false;
    }

    /// <summary>The selection to set before the TextBox moves the caret: collapsed onto the caret
    /// when something is selected, null when nothing is (then the TextBox is already right).</summary>
    public static (int Start, int End)? CollapseOntoCaret(int selectionStart, int selectionEnd, int caretIndex)
        => selectionStart == selectionEnd ? null : (caretIndex, caretIndex);

    private static void OnKeyDown(TextBox box, KeyEventArgs e)
    {
        if (e.Handled) return;
        var keymap = Application.Current?.PlatformSettings?.HotkeyConfiguration;
        if (keymap == null || !IsPlainHomeOrEnd(e,
                keymap.MoveCursorToTheStartOfLine, keymap.MoveCursorToTheEndOfLine,
                keymap.MoveCursorToTheStartOfDocument, keymap.MoveCursorToTheEndOfDocument)) return;
        if (CollapseOntoCaret(box.SelectionStart, box.SelectionEnd, box.CaretIndex) is not { } sel) return;
        // SetCurrentValue, as the TextBox itself does: a local value would replace a binding.
        box.SetCurrentValue(TextBox.SelectionStartProperty, sel.Start);
        box.SetCurrentValue(TextBox.SelectionEndProperty, sel.End);
    }
}
