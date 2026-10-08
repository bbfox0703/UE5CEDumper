using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using static UE5DumpUI.HeadlessTests.LiveFuncsLayout;

namespace UE5DumpUI.HeadlessTests;

/// <summary>
/// [LF-COMPACT-TOP] What sits above the Live Funcs table, measured: with a stack chosen it took over half the panel's
/// height in the maintainer's window (the table's top at 490 of 868 px on the build before this item, measured here), and
/// the four-line stack warning was the biggest block. The layout these tests read is the one the file names but only
/// Avalonia decides: which lines wrap, what a hidden block gives back, where a control lands.
/// </summary>
public class LiveFuncsTopAreaTests
{
    private static ToggleButton DetailsToggle(Avalonia.Visual panel)
    {
        string label = Res("str.LF.Stack.WarningDetails");
        var toggle = panel.GetVisualDescendants().OfType<ToggleButton>().SingleOrDefault(t => t.Content as string == label);
        Assert.True(toggle != null, "the stack warning has no Details toggle");
        return toggle!;
    }

    /// <summary>(1) One line of the warning's essentials, its Details toggle on the same line, and the whole text only when
    /// asked for: the table moves down by the whole text when Details is pressed, and back when it is released.</summary>
    [Fact]
    public Task The_stack_warning_takes_one_line_and_Details_shows_the_whole_text() => Headless.Run(() =>
    {
        var (panel, vm) = LaidWithAStackChosen();
        Assert.False(vm.StackEstimateWarn);
        var line = Shown(panel, Res("str.LF.Stack.WarningShort"));
        Assert.Equal(1, Lines(line));
        Assert.DoesNotContain(TextBlocks(panel, Res("str.LF.Stack.Warning")), t => t.IsEffectivelyVisible);

        var toggle = DetailsToggle(panel);
        Assert.True(toggle.IsEffectivelyVisible, "Details is hidden with a stack chosen");
        var lineBox = Box(line, panel);
        var toggleBox = Box(toggle, panel);
        Assert.True(toggleBox.Top < lineBox.Bottom && lineBox.Top < toggleBox.Bottom,
                    $"Details is not on the warning's line (line {lineBox.Top:0.#}-{lineBox.Bottom:0.#}, "
                    + $"toggle {toggleBox.Top:0.#}-{toggleBox.Bottom:0.#})");

        double closed = GridTop(panel);
        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        var whole = Shown(panel, Res("str.LF.Stack.Warning"));
        Assert.True(Lines(whole) > 1, "the whole warning is not the long text");
        double open = GridTop(panel);
        Assert.True(open - closed >= whole.Bounds.Height - 0.5,
                    $"the table's top is {closed:0.#} with Details closed and {open:0.#} open; the text is {whole.Bounds.Height:0.#} high");

        toggle.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(TextBlocks(panel, Res("str.LF.Stack.Warning")), t => t.IsEffectivelyVisible);
        Assert.Equal(closed, GridTop(panel));
    });

    /// <summary>(1) The warning's one line turns orange with the estimate, as the four lines did: a capture measured at 100
    /// µs makes A::F's 25 a second cost 2.5 ms of the game's time, above the 2 ms line.</summary>
    [Fact]
    public Task The_one_line_turns_orange_with_the_estimate() => Headless.Run(() =>
    {
        var (panel, vm) = LaidWithAStackChosen(usPerCapture: 100);
        Assert.True(vm.StackEstimateWarn, $"the estimate is not orange: {vm.StackEstimate}");
        var line = Shown(panel, Res("str.LF.Stack.WarningShort"));
        var calm = TextBlocks(panel, Res("str.LF.Stack.WarningShort"))
                   .Where(t => !t.IsEffectivelyVisible).Select(t => t.Foreground?.ToString()).Single();
        Assert.NotEqual(calm, line.Foreground?.ToString());
        Assert.Equal(TextBlocks(panel, vm.StackEstimate).Single(t => t.IsEffectivelyVisible).Foreground?.ToString(),
                     line.Foreground?.ToString());
        Assert.Equal(1, Lines(line));
    });
}
