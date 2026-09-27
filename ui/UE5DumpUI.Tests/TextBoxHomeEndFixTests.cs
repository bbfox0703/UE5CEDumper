using Avalonia.Input;
using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [TEXTBOX-HOMEEND-CARET] Avalonia's TextBox puts the caret at the selection's START when a plain
/// Home / End finds the caret already on its target (measured on build 3574: End after the
/// AutoCompleteBox's select-all went to the start of the box). The fix collapses the selection
/// onto its active end first; these pin WHEN it acts. The TextBox itself is driven in
/// UE5DumpUI.HeadlessTests (TextBoxHomeEndTests).
/// </summary>
public class TextBoxHomeEndFixTests
{
    // Windows' plain Home / End / Ctrl+Home / Ctrl+End and page keys, as Avalonia's Win32 keymap
    // declares them.
    private static readonly KeyGesture[][] Moves =
    {
        new[] { new KeyGesture(Key.Home) }, new[] { new KeyGesture(Key.End) },
        new[] { new KeyGesture(Key.Home, KeyModifiers.Control) }, new[] { new KeyGesture(Key.End, KeyModifiers.Control) },
        new[] { new KeyGesture(Key.PageUp) }, new[] { new KeyGesture(Key.PageDown) },
    };

    private static KeyEventArgs Press(Key key, KeyModifiers modifiers = KeyModifiers.None)
        => new() { Key = key, KeyModifiers = modifiers };

    [Theory]
    [InlineData(Key.Home, KeyModifiers.None)]
    [InlineData(Key.End, KeyModifiers.None)]
    [InlineData(Key.Home, KeyModifiers.Control)]
    [InlineData(Key.End, KeyModifiers.Control)]
    [InlineData(Key.PageUp, KeyModifiers.None)]
    [InlineData(Key.PageDown, KeyModifiers.None)]
    public void The_plain_moves_that_end_in_ClearSelection_are_caught(Key key, KeyModifiers modifiers)
        => Assert.True(TextBoxHomeEndFix.IsSelectionClearingMove(Press(key, modifiers), Moves));

    [Theory]
    [InlineData(Key.Home, KeyModifiers.Shift)]                         // extends the selection
    [InlineData(Key.End, KeyModifiers.Shift)]
    [InlineData(Key.End, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(Key.Left, KeyModifiers.None)]
    [InlineData(Key.A, KeyModifiers.Control)]
    public void Other_keys_are_left_to_the_TextBox(Key key, KeyModifiers modifiers)
        => Assert.False(TextBoxHomeEndFix.IsSelectionClearingMove(Press(key, modifiers), Moves));

    [Fact]
    public void A_select_all_collapses_onto_its_active_end_so_End_stays_at_the_end()
    {
        // The measured case: "spawn" selected by AutoCompleteBox's SelectAll (0 -> 5).
        Assert.Equal((5, 5), TextBoxHomeEndFix.CollapseOntoActiveEnd(0, 5));
    }

    [Fact]
    public void A_right_to_left_selection_collapses_onto_its_active_end_so_Home_stays_at_the_start()
    {
        Assert.Equal((0, 0), TextBoxHomeEndFix.CollapseOntoActiveEnd(5, 0));
    }

    [Fact]
    public void Nothing_selected_needs_nothing()
    {
        Assert.Null(TextBoxHomeEndFix.CollapseOntoActiveEnd(3, 3));
    }
}
