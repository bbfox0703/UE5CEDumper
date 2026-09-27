using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.HeadlessTests;

/// <summary>
/// [TEXTBOX-HOMEEND-CARET] on a real Avalonia TextBox. Measured on build 3574: a click on a
/// keyword box's padding selects its text, End then put the caret at the START, and " act"
/// typed after "spawn" made " actspawn". Each case runs twice where it matters — without the fix
/// (Avalonia's own behaviour, so a future Avalonia that fixes it shows up here) and with it.
/// </summary>
public class TextBoxHomeEndTests
{
    private static TextBox ShowBox(string text, bool multiline = false)
    {
        var box = new TextBox { Text = text, AcceptsReturn = multiline, Width = 300 };
        new Window { Content = box, Width = 400, Height = 200 }.Show();
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        return box;
    }

    private static void Press(Control target, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var top = TopLevel.GetTopLevel(target)!;
        var physical = key switch
        {
            Key.Home => PhysicalKey.Home,
            Key.End => PhysicalKey.End,
            Key.PageUp => PhysicalKey.PageUp,
            Key.PageDown => PhysicalKey.PageDown,
            Key.Down => PhysicalKey.ArrowDown,
            _ => PhysicalKey.None,
        };
        top.KeyPress(key, modifiers, physical, null);
        top.KeyRelease(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Type(Control target, string text)
    {
        TopLevel.GetTopLevel(target)!.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>What an AutoCompleteBox's focus leaves behind: everything selected, the caret
    /// where it was — at the end.</summary>
    private static void SelectAllWithCaretAtEnd(TextBox box)
    {
        box.CaretIndex = box.Text!.Length;
        box.SelectAll();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task Without_the_fix_End_after_a_select_all_goes_to_the_START() => Headless.Run(() =>
    {
        // Avalonia's own behaviour, the defect. If this starts failing after an Avalonia
        // upgrade, Avalonia fixed it and TextBoxHomeEndFix can be retired.
        var box = ShowBox("spawn");
        SelectAllWithCaretAtEnd(box);

        Press(box, Key.End);

        Assert.Equal(0, box.CaretIndex);
    });

    [Fact]
    public Task End_after_a_select_all_stays_at_the_end_and_typing_appends() => Headless.Run(() =>
    {
        using var fix = TextBoxHomeEndFix.Register();
        var box = ShowBox("spawn");
        SelectAllWithCaretAtEnd(box);

        Press(box, Key.End);
        Type(box, " act");

        Assert.Equal("spawn act", box.Text);
    });

    /// <summary>The mirror image: the caret at the selection's left end while SelectionStart is
    /// its right end. (Shift+Home does NOT leave this — Avalonia keeps CaretIndex at the anchor
    /// there — so the state is set directly.)</summary>
    private static void SelectRightToLeftWithCaretAtStart(TextBox box)
    {
        box.CaretIndex = 0;
        box.SelectionStart = box.Text!.Length;
        box.SelectionEnd = 0;
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task Without_the_fix_Home_with_the_caret_already_at_the_start_goes_to_the_END() => Headless.Run(() =>
    {
        var box = ShowBox("spawn");
        SelectRightToLeftWithCaretAtStart(box);

        Press(box, Key.Home);

        Assert.Equal(5, box.CaretIndex);
    });

    [Fact]
    public Task Home_with_the_caret_already_at_the_start_stays_there() => Headless.Run(() =>
    {
        using var fix = TextBoxHomeEndFix.Register();
        var box = ShowBox("spawn");
        SelectRightToLeftWithCaretAtStart(box);

        Press(box, Key.Home);
        Type(box, "X");

        Assert.Equal("Xspawn", box.Text);
    });

    [Fact]
    public Task Ctrl_End_after_a_select_all_stays_at_the_end() => Headless.Run(() =>
    {
        using var fix = TextBoxHomeEndFix.Register();
        var box = ShowBox("spawn");
        SelectAllWithCaretAtEnd(box);

        Press(box, Key.End, RawInputModifiers.Control);

        Assert.Equal(5, box.CaretIndex);
        Assert.Equal(box.SelectionStart, box.SelectionEnd);
    });

    [Theory]
    [InlineData(1)]                                         // inside line 1: End moves
    [InlineData(2)]                                         // at the end of "ab": End may not move
    public Task In_a_multi_line_box_End_with_a_selection_lands_where_End_without_one_does(int caret) => Headless.Run(() =>
    {
        // Where "end of line" falls is the text layout's business (the headless one counts the
        // line break differently from the desktop one) -- the rule is only that a selection must
        // not change it.
        using var fix = TextBoxHomeEndFix.Register();
        var plain = ShowBox("ab\ncd", multiline: true);
        plain.CaretIndex = caret;
        Press(plain, Key.End);

        var box = ShowBox("ab\ncd", multiline: true);
        box.CaretIndex = caret;
        box.SelectionStart = 0;
        box.SelectionEnd = caret;                           // selected up to the caret
        Dispatcher.UIThread.RunJobs();
        Press(box, Key.End);

        Assert.Equal(plain.CaretIndex, box.CaretIndex);
        Assert.True(box.CaretIndex >= 2 && box.CaretIndex <= 3, $"line 1's end, got {box.CaretIndex}");
    });

    [Theory]
    [InlineData(Key.End)]
    [InlineData(Key.Home)]
    public Task A_selection_made_down_across_lines_moves_on_the_line_the_caret_is_on(Key key) => Headless.Run(() =>
    {
        // Keyboard selection leaves CaretIndex at the ANCHOR and puts the visible caret at
        // SelectionEnd: after Shift+Down from the start the caret shows on line 2, so End /
        // Home must act on line 2 -- as Windows does -- not on the anchor's line 1.
        using var fix = TextBoxHomeEndFix.Register();
        var line2 = ShowBox("ab\ncd", multiline: true);
        line2.CaretIndex = 4;                               // inside line 2
        Press(line2, key);                                  // where End / Home lands on line 2

        var box = ShowBox("ab\ncd", multiline: true);
        box.CaretIndex = 0;
        Press(box, Key.Down, RawInputModifiers.Shift);      // selects onto line 2
        Assert.NotEqual(box.SelectionStart, box.SelectionEnd);

        Press(box, key);

        Assert.Equal(line2.CaretIndex, box.CaretIndex);
    });

    [Theory]
    [InlineData(Key.PageDown)]
    [InlineData(Key.PageUp)]
    public Task A_page_key_with_a_select_all_leaves_the_caret_on_the_active_end(Key key) => Headless.Run(() =>
    {
        // Same sink as Home / End (found by review): the page moves only scroll, then
        // ClearSelection puts the caret at SelectionStart -- the start of a select-all.
        using var fix = TextBoxHomeEndFix.Register();
        var box = ShowBox("spawn");
        SelectAllWithCaretAtEnd(box);

        Press(box, key);
        Type(box, " act");

        Assert.Equal("spawn act", box.Text);
    });

    [Fact]
    public Task Shift_End_still_extends_the_selection() => Headless.Run(() =>
    {
        using var fix = TextBoxHomeEndFix.Register();
        var box = ShowBox("spawn");
        box.CaretIndex = 0;

        Press(box, Key.End, RawInputModifiers.Shift);

        Assert.Equal(0, box.SelectionStart);
        Assert.Equal(5, box.SelectionEnd);
    });

    [Fact]
    public Task An_AutoCompleteBox_focused_from_elsewhere_appends_after_End() => Headless.Run(() =>
    {
        // The measured case end to end: the keyword box is an AutoCompleteBox, whose focus
        // selects its text.
        using var fix = TextBoxHomeEndFix.Register();
        var other = new Button { Content = "grid" };
        var auto = new AutoCompleteBox { Text = "spawn", Width = 300 };
        new Window { Content = new StackPanel { Children = { other, auto } }, Width = 400, Height = 200 }.Show();
        other.Focus();
        Dispatcher.UIThread.RunJobs();
        var inner = auto.GetVisualDescendants().OfType<TextBox>().First();
        inner.CaretIndex = 5;
        auto.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, inner.SelectionStart);              // the premise: focus selected everything
        Assert.Equal(5, inner.SelectionEnd);

        Press(inner, Key.End);
        Type(inner, " act");

        Assert.Equal("spawn act", auto.Text);
    });
}
