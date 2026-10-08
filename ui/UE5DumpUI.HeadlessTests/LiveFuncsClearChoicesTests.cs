using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UE5DumpUI.ViewModels;
using Xunit;
using static UE5DumpUI.HeadlessTests.LiveFuncsLayout;

namespace UE5DumpUI.HeadlessTests;

/// <summary>
/// Where Live Funcs' Clear choices lands, as Avalonia 12.1.3 lays the panel out: on the capture settings' header line
/// ([LF-COMPACT-TOP]), beside the button that folds them, so it reads as clearing the section's choices and shows whether
/// the section is folded or not; and on a line the panel already has, so the controls above the table grow no taller.
/// The file names the panel the button is in; only a layout shows where that panel ends up and how tall it gets.
/// </summary>
public class LiveFuncsClearChoicesTests
{
    private static Button ClearChoices(Visual panel)
    {
        string label = Res("str.LF.ClearChoices");
        var button = panel.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Content as string == label);
        Assert.True(button != null, "the panel has no Clear choices button");
        return button!;
    }

    private static Button FoldButton(Visual panel, LiveFuncsViewModel vm)
    {
        var button = panel.GetVisualDescendants().OfType<Button>()
                          .SingleOrDefault(b => ReferenceEquals(b.Command, vm.ToggleCaptureSettingsCommand));
        Assert.True(button != null, "the panel has no button that folds the capture settings");
        return button!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Clear_choices_sits_on_the_capture_settings_header_folded_or_not(bool folded) => Headless.Run(() =>
    {
        var (panel, vm) = Laid();
        vm.CaptureSettingsCollapsed = folded;
        Dispatcher.UIThread.RunJobs();
        var button = ClearChoices(panel);
        Assert.True(button.IsEffectivelyVisible, "Clear choices is hidden with the trace available");
        Assert.True(button.IsEffectivelyEnabled, "Clear choices is disabled with nothing recording");
        Assert.Same(vm.ClearChoicesCommand, button.Command);

        var box = Box(button, panel);
        var fold = Box(FoldButton(panel, vm), panel);
        double gridTop = GridTop(panel);
        string layout = $"button {box.Left:0.#}-{box.Right:0.#} x {box.Top:0.#}-{box.Bottom:0.#}, "
                      + $"fold {fold.Left:0.#}-{fold.Right:0.#} x {fold.Top:0.#}-{fold.Bottom:0.#}, table top {gridTop:0.#}";
        // On the fold button's line, just after it.
        Assert.True(box.Top < fold.Bottom && fold.Top < box.Bottom, $"Clear choices is not on the header's line ({layout})");
        Assert.True(box.Left >= fold.Right - 0.5 && box.Left - fold.Right < box.Width, $"Clear choices does not follow the fold ({layout})");
        Assert.True(box.Bottom <= gridTop, $"Clear choices is not above the table ({layout})");
    });

    /// <summary>The line the button shares was there before it: taking the button out moves the table up by less than
    /// the button's height, where a line of its own would move it by at least that. Folded and unfolded alike.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Clear_choices_adds_no_line_above_the_table(bool folded) => Headless.Run(() =>
    {
        var (panel, vm) = Laid();
        vm.CaptureSettingsCollapsed = folded;
        Dispatcher.UIThread.RunJobs();
        var button = ClearChoices(panel);
        double withIt = GridTop(panel);
        button.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        double without = GridTop(panel);
        Assert.True(withIt - without < button.Bounds.Height,
                    $"the table's top is {withIt:0.#} with Clear choices and {without:0.#} without: a line of its own");
    });

    [Fact]
    public Task Clear_choices_is_disabled_while_recording_and_hidden_with_the_columns() => Headless.Run(() =>
    {
        var (panel, vm) = Laid();
        var button = ClearChoices(panel);
        vm.IsRecording = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(button.IsEffectivelyEnabled, "Clear choices stays enabled while recording");

        var (hidden, _) = Laid(experimental: false);
        Assert.False(ClearChoices(hidden).IsEffectivelyVisible, "Clear choices shows without the trace's columns");
        Assert.DoesNotContain(hidden.GetVisualDescendants().OfType<DataGridColumnHeader>(),
                              h => h.Content as string == Res("str.LF.Col.Trace") && h.IsEffectivelyVisible);
    });
}
