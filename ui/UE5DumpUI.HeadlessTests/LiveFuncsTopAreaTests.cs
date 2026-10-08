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

    /// <summary>(3) No line for the baseline until there is one or Diff is on, and the table takes the room; the hint the
    /// line gave is on Set Baseline's tooltip.</summary>
    [Fact]
    public Task The_baseline_line_takes_no_room_until_there_is_a_baseline_or_Diff_is_on() => Headless.Run(() =>
    {
        var (panel, vm) = Laid();
        var line = TextBlocks(panel, vm.BaselineStatus).Single();
        Assert.False(line.IsEffectivelyVisible, "the baseline line shows with no baseline and Diff off");
        double without = GridTop(panel);

        vm.DiffMode = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(line.IsEffectivelyVisible, "the baseline line is hidden with Diff on");
        Assert.True(GridTop(panel) - without >= line.Bounds.Height - 0.5,
                    $"the table's top is {without:0.#} without the line and {GridTop(panel):0.#} with it");

        var setBaseline = panel.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == Res("str.LF.SetBaseline"));
        Assert.Contains("record idle, then Set Baseline", ToolTip.GetTip(setBaseline) as string ?? "", StringComparison.Ordinal);
    });

    private static Button FoldButton(Avalonia.Visual panel, UE5DumpUI.ViewModels.LiveFuncsViewModel vm)
        => panel.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.ToggleCaptureSettingsCommand));

    /// <summary>The section's own controls: its sliders, the Trace switch and the stack budget's radios.</summary>
    private static List<Control> SectionControls(Avalonia.Visual panel)
    {
        string trace = Res("str.LF.Trace.Enable");
        return panel.GetVisualDescendants().OfType<Control>()
                    .Where(c => c is Slider || c is RadioButton || (c is CheckBox box && box.Content as string == trace))
                    .ToList();
    }

    /// <summary>(2) Folded, the capture settings give their room to the table and the header sums them up; the Start row,
    /// the baseline row and the status line stay; unfolded, everything is back where it was. Measured with a stack chosen,
    /// the case the maintainer found taking most of the panel: folded, the table starts in the window's top third.</summary>
    [Fact]
    public Task Folded_the_settings_give_their_room_to_the_table_and_the_header_sums_them_up() => Headless.Run(() =>
    {
        var (panel, vm) = LaidWithAStackChosen();
        var fold = FoldButton(panel, vm);
        Assert.Equal(Res("str.LF.Settings.Collapse"), fold.Content as string);
        Assert.All(SectionControls(panel).Where(c => c is Slider), c => Assert.True(c.IsEffectivelyVisible));
        Assert.DoesNotContain(TextBlocks(panel, vm.CaptureSummary), t => t.IsEffectivelyVisible);
        double unfolded = GridTop(panel);

        fold.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.CaptureSettingsCollapsed);
        Assert.Equal(Res("str.LF.Settings.Expand"), fold.Content as string);
        Assert.DoesNotContain(SectionControls(panel), c => c.IsEffectivelyVisible);
        Assert.DoesNotContain(TextBlocks(panel, vm.StackEstimate), t => t.IsEffectivelyVisible);
        var summary = Shown(panel, vm.CaptureSummary);
        Assert.Contains(Res("str.LF.Stack.WarningShort"), summary.Text, StringComparison.Ordinal);
        var foldBox = Box(fold, panel);
        Assert.True(Box(summary, panel).Top < foldBox.Bottom, "the summary is not on the header's line");
        Assert.True(panel.GetVisualDescendants().OfType<Button>()
                         .Single(b => b.Content as string == Res("str.LF.SetBaseline")).IsEffectivelyVisible);
        Assert.True(TextBlocks(panel, vm.StatusText).Single().IsEffectivelyVisible);

        double folded = GridTop(panel);
        Assert.True(folded < unfolded, $"the table's top is {unfolded:0.#} unfolded and {folded:0.#} folded");
        Assert.True(folded < 868 / 3.0, $"folded, the table's top is {folded:0.#} of 868 px");

        fold.Command.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(unfolded, GridTop(panel));
    });

    /// <summary>(2) Folding never hides a warning: with memory over what is free (a 512 MB trace and 1 GB free) and with
    /// stacks over 2 ms of the game's time a second, the summary on the header is the orange one, as the lines it stands
    /// for are; with nothing to warn of it is not.</summary>
    [Fact]
    public Task Folded_a_warning_still_shows_in_orange() => Headless.Run(() =>
    {
        var (calmPanel, calmVm) = LaidWithAStackChosen();
        calmVm.CaptureSettingsCollapsed = true;
        Dispatcher.UIThread.RunJobs();
        string calm = Shown(calmPanel, calmVm.CaptureSummary).Foreground?.ToString() ?? "";

        var (panel, vm) = LaidWithAStackChosen(availableBytes: 1L << 30);
        vm.TraceBufferExponent = 9;
        vm.CaptureSettingsCollapsed = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.TraceMemoryOverAvailable);
        // The memory line's orange TextBlock: its own IsVisible is set, though the folded section hides it.
        string orange = TextBlocks(panel, vm.TraceMemoryEstimate).Single(t => t.IsVisible).Foreground?.ToString() ?? "";
        var summary = Shown(panel, vm.CaptureSummary);
        Assert.Equal(orange, summary.Foreground?.ToString());
        Assert.NotEqual(calm, orange);
        Assert.Contains("⚠", summary.Text, StringComparison.Ordinal);

        var (stackPanel, stackVm) = LaidWithAStackChosen(usPerCapture: 100);
        stackVm.CaptureSettingsCollapsed = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(stackVm.StackEstimateWarn);
        Assert.Equal(orange, Shown(stackPanel, stackVm.CaptureSummary).Foreground?.ToString());
    });

    /// <summary>(2) Open in Call Trace is an action after a traced Stop, not a setting: folding leaves it on screen.</summary>
    [Fact]
    public Task Open_in_Call_Trace_stays_on_screen_when_the_settings_are_folded() => Headless.Run(() =>
    {
        var (panel, vm) = LaidWithAStackChosen(usPerCapture: 20);
        Assert.True(vm.HasTraceToOpen);
        vm.CaptureSettingsCollapsed = true;
        Dispatcher.UIThread.RunJobs();
        var open = panel.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == Res("str.LF.Trace.Open"));
        Assert.True(open.IsEffectivelyVisible, "Open in Call Trace is hidden with the settings folded");
        Assert.True(Box(open, panel).Top < Box(FoldButton(panel, vm), panel).Bottom, "Open in Call Trace is not on the header's line");
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
