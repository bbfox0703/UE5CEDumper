using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UE5DumpUI.Core;
using UE5DumpUI.ViewModels;
using UE5DumpUI.Views;
using Xunit;

namespace UE5DumpUI.HeadlessTests;

/// <summary>
/// Where Live Funcs' Clear choices lands, as Avalonia 12.1.3 lays the panel out: on the line just
/// above the table and over its three choice columns (Trace, Params?, Stack?), so it reads as clearing those, and on a
/// line the panel already had, so the controls above the table grow no taller. The file names the panel the button is
/// in; only a layout shows where that panel ends up and how tall it gets.
/// </summary>
public class LiveFuncsClearChoicesTests
{
    private sealed class Gate(bool enabled) : IExperimentalGate
    {
        public bool IsEnabled { get; set; } = enabled;
        public int SnapshotQuotaMb { get; set; }
        public bool IsLocked => false;
        public void Lock() { }
        public event EventHandler? Changed { add { } remove { } }
    }

    private static bool _loaded;

    /// <summary>The panel's strings and the DataGrid's theme, which the app's own App.axaml adds: without the theme the
    /// grid has no template, so no column headers to measure against. Every test body runs on the one dispatcher
    /// thread, so the flag needs no lock.</summary>
    private static void LoadAppResources()
    {
        if (_loaded) return;
        var strings = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://UE5DumpUI/Resources/Strings/en.axaml"));
        Application.Current!.Resources.MergedDictionaries.Add(strings);
        Application.Current.Styles.Add((IStyle)AvaloniaXamlLoader.Load(
            new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")));
        _loaded = true;
    }

    private static string Res(string key)
    {
        Assert.True(Application.Current!.TryFindResource(key, out var value) && value is string,
                    $"{key} is not in en.axaml");
        return (string)value!;
    }

    /// <summary>The panel in the maintainer's 1389x868 window, nothing fetched, the experimental trace on or off.</summary>
    private static (LiveFuncsPanel panel, LiveFuncsViewModel vm) Laid(bool experimental = true)
    {
        LoadAppResources();
        var dump = DispatchProxy.Create<IDumpService, CallTraceColumnsTests.Inert>();
        var log = DispatchProxy.Create<ILoggingService, CallTraceColumnsTests.Inert>();
        var vm = new LiveFuncsViewModel(dump, log, null, experimentalGate: new Gate(experimental));
        var panel = new LiveFuncsPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 1389, Height = 868 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (panel, vm);
    }

    private static Button ClearChoices(Visual panel)
    {
        string label = Res("str.LF.ClearChoices");
        var button = panel.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Content as string == label);
        Assert.True(button != null, "the panel has no Clear choices button");
        return button!;
    }

    private static DataGridColumnHeader ColumnHeader(Visual panel, string key)
    {
        string header = Res(key);
        return panel.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => h.Content as string == header);
    }

    /// <summary>A visual's box in the panel's coordinates.</summary>
    private static Rect Box(Visual v, Visual panel) => new(v.TranslatePoint(default, panel)!.Value, v.Bounds.Size);

    private static double GridTop(Visual panel)
        => Box(panel.GetVisualDescendants().OfType<DataGrid>().Single(), panel).Top;

    [Fact]
    public Task Clear_choices_sits_on_the_line_above_the_table_over_its_three_choice_columns() => Headless.Run(() =>
    {
        var (panel, vm) = Laid();
        var button = ClearChoices(panel);
        Assert.True(button.IsEffectivelyVisible, "Clear choices is hidden with the trace available");
        Assert.True(button.IsEffectivelyEnabled, "Clear choices is disabled with nothing recording");
        Assert.Same(vm.ClearChoicesCommand, button.Command);

        var box = Box(button, panel);
        double gridTop = GridTop(panel);
        var trace = Box(ColumnHeader(panel, "str.LF.Col.Trace"), panel);
        var stack = Box(ColumnHeader(panel, "str.LF.Col.Stack"), panel);
        string layout = $"button {box.Left:0.#}-{box.Right:0.#} x {box.Top:0.#}-{box.Bottom:0.#}, table top {gridTop:0.#}, "
                      + $"choice columns {trace.Left:0.#}-{stack.Right:0.#}";

        // Above the table, with no line of controls between: less than its own height of room under it.
        Assert.True(box.Bottom <= gridTop + 0.5, $"Clear choices is not above the table ({layout})");
        Assert.True(gridTop - box.Bottom < box.Height, $"something sits between Clear choices and the table ({layout})");
        // Over the three choice columns, from the first one's left edge to no further than the last one's right.
        Assert.True(box.Left >= trace.Left - 0.5 && box.Right <= stack.Right + 0.5,
                    $"Clear choices is not over the choice columns ({layout})");
    });

    /// <summary>The line the button shares was there before it: taking the button out moves the table up by less than
    /// the button's height, where a line of its own would move it by at least that.</summary>
    [Fact]
    public Task Clear_choices_adds_no_line_above_the_table() => Headless.Run(() =>
    {
        var (panel, _) = Laid();
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
