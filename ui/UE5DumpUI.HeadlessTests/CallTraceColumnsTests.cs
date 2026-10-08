using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UE5DumpUI.Core;
using UE5DumpUI.ViewModels;
using UE5DumpUI.Views;
using Xunit;

namespace UE5DumpUI.HeadlessTests;

/// <summary>
/// [CT-COLUMNS-OVERLAP] The Call Trace list as Avalonia 12.1.3 lays it out, with the detail pane dragged wide as the
/// step-3 walkthrough left it: the list is narrower than its four fixed columns. Each column keeps its dragged width,
/// none draws into the visible part of the one on its left, and what does not fit is cut at the list's edge instead of
/// drawing over the detail pane. A source test cannot show this: the overlap came from where DockPanel puts a fixed
/// width cell that no longer fits, which no attribute in the file states.
/// </summary>
public class CallTraceColumnsTests
{
    /// <summary>Answers every call with its type's default: the panel is laid out, and nothing is ever loaded.</summary>
    public class Inert : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.ReturnType is { IsValueType: true } t && t != typeof(void) ? Activator.CreateInstance(t) : null;
    }

    private static bool _stringsLoaded;
    private static bool _dataGridThemeLoaded;

    /// <summary>The DataGrid's own theme, which App.axaml includes and the headless app does not: without it the
    /// Call stack grid has no template and lays out no column.</summary>
    private static void LoadDataGridTheme()
    {
        if (_dataGridThemeLoaded) return;
        Application.Current!.Styles.Add(new StyleInclude(new Uri("avares://UE5DumpUI"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"),
        });
        _dataGridThemeLoaded = true;
    }

    /// <summary>The panel's StaticResource strings: the headless app carries the theme and nothing of the app's own.
    /// Every test body runs on the one dispatcher thread, so the flag needs no lock.</summary>
    private static void LoadStrings()
    {
        if (_stringsLoaded) return;
        var strings = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://UE5DumpUI/Resources/Strings/en.axaml"));
        Application.Current!.Resources.MergedDictionaries.Add(strings);
        _stringsLoaded = true;
    }

    private static readonly CallTraceRow Row = new()
    {
        TimeText = "1234.567", DurationText = "12.3", ThreadText = "48",
        FunctionText = "DumperTest58Actor_C::ReceiveTick", ObjectText = "DumperTest58Actor_0",
    };

    /// <summary>The panel in a 1000-wide window, its columns at their defaults (Time 96, Duration 88, Thread 64,
    /// Object 260), the detail pane as wide as asked.</summary>
    private static (CallTracePanel panel, CallTraceViewModel vm) Laid(double detailPaneWidth)
    {
        LoadStrings();
        var dump = DispatchProxy.Create<IDumpService, Inert>();
        var log = DispatchProxy.Create<ILoggingService, Inert>();
        var vm = new CallTraceViewModel(dump, log, new LiveFuncsViewModel(dump, log))
        {
            DetailPaneWidth = detailPaneWidth,
            Rows = new[] { Row },
        };
        var panel = new CallTracePanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 1000, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (panel, vm);
    }

    private static TextBlock Text(Visual root, string text)
        => root.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == text);

    private static string Header(string key) => (string)Application.Current!.FindResource(key)!;

    /// <summary>A header cell is the Panel that holds its label and its drag handle.</summary>
    private static Visual HeaderCell(Visual panel, string key) => Text(panel, Header(key)).GetVisualParent()!;

    /// <summary>Where a visual starts and ends, in the panel's coordinates.</summary>
    private static (double left, double right) Span(Visual v, Visual panel)
    {
        double left = v.TranslatePoint(default, panel)!.Value.X;
        return (left, left + v.Bounds.Width);
    }

    /// <summary>How far right the cell can draw: its own edge, or the nearest clipping ancestor's if that is nearer.</summary>
    private static double VisibleRight(Visual cell, Visual panel)
    {
        double right = Span(cell, panel).right;
        foreach (var a in cell.GetVisualAncestors().TakeWhile(a => a != panel))
            if (a.ClipToBounds) right = Math.Min(right, Span(a, panel).right);
        return right;
    }

    private static (string name, Visual cell, double width)[] HeaderCells(Visual panel, CallTraceViewModel vm) =>
    [
        ("Time", HeaderCell(panel, "str.CT.Col.Time"), vm.TimeColWidth),
        ("Duration", HeaderCell(panel, "str.CT.Col.Duration"), vm.DurationColWidth),
        ("Thread", HeaderCell(panel, "str.CT.Col.Thread"), vm.ThreadColWidth),
        ("Object", HeaderCell(panel, "str.CT.Col.Object"), vm.ObjectColWidth),
    ];

    private static (string name, Visual cell, double width)[] RowCells(Visual panel, CallTraceViewModel vm) =>
    [
        ("Time", Text(panel, Row.TimeText), vm.TimeColWidth),
        ("Duration", Text(panel, Row.DurationText), vm.DurationColWidth),
        ("Thread", Text(panel, Row.ThreadText), vm.ThreadColWidth),
        ("Object", Text(panel, Row.ObjectText), vm.ObjectColWidth),
    ];

    /// <summary>Every cell as wide as it was dragged, and none starting inside what can be seen of the one before.</summary>
    private static void AssertApart(string what, Visual panel, (string name, Visual cell, double width)[] cells)
    {
        var spans = cells.Select(c => (c.name, span: Span(c.cell, panel), seen: VisibleRight(c.cell, panel), c.width)).ToList();
        string layout = string.Join(", ", spans.Select(s => $"{s.name} {s.span.left:0.#}-{s.span.right:0.#} (seen to {s.seen:0.#})"));
        foreach (var s in spans)
            Assert.True(Math.Abs(s.span.right - s.span.left - s.width) < 0.5,
                        $"{what}: {s.name} is {s.span.right - s.span.left:0.#} wide, dragged to {s.width} ({layout})");
        for (int i = 1; i < spans.Count; i++)
            Assert.True(spans[i].span.left >= Math.Min(spans[i - 1].span.right, spans[i - 1].seen) - 0.5,
                        $"{what}: {spans[i].name} draws over {spans[i - 1].name} ({layout})");
    }

    // 700: the walkthrough's case, the list (272 wide) narrower than the four columns (508), so Function has nothing
    // left and Object is cut. 760: narrower than Time, Duration and Thread together (248), so Thread is cut too.
    [Theory]
    [InlineData(700)]
    [InlineData(760)]
    public Task A_narrow_list_keeps_every_header_column_out_of_the_next(double detailPaneWidth) => Headless.Run(() =>
    {
        var (panel, vm) = Laid(detailPaneWidth);
        AssertApart("header", panel, HeaderCells(panel, vm));
    });

    [Theory]
    [InlineData(700)]
    [InlineData(760)]
    public Task A_narrow_list_keeps_every_row_column_out_of_the_next(double detailPaneWidth) => Headless.Run(() =>
    {
        var (panel, vm) = Laid(detailPaneWidth);
        AssertApart("row", panel, RowCells(panel, vm));
    });

    [Theory]
    [InlineData(700)]
    [InlineData(760)]
    public Task What_does_not_fit_is_cut_at_the_list_and_never_drawn_over_the_detail_pane(double detailPaneWidth)
        => Headless.Run(() =>
    {
        var (panel, vm) = Laid(detailPaneWidth);
        var tabs = panel.GetVisualDescendants().OfType<TabControl>().Single();
        var handle = ((Panel)tabs.GetVisualParent()!).Children.OfType<Thumb>().Single();
        double edge = Span(handle, panel).left;
        foreach (var (what, cells) in new[] { ("header", HeaderCells(panel, vm)), ("row", RowCells(panel, vm)) })
            foreach (var (name, cell, _) in cells)
            {
                double right = VisibleRight(cell, panel);
                Assert.True(right <= edge + 0.5,
                            $"the {what}'s {name} column draws to {right:0.#}, past the list's edge at {edge:0.#}");
            }
    });

    /// <summary>A guard for the wide layout, which a fix must leave as it was: with room to spare, Object stays docked at
    /// the list's right edge in the header and in the row, and Function takes what the fixed columns leave between them.
    /// The edge is the list's, not the row panel's own: a row panel aligned left shrinks to its text and takes Object
    /// with it.</summary>
    [Fact]
    public Task A_wide_list_keeps_Object_at_its_right_edge() => Headless.Run(() =>
    {
        var (panel, vm) = Laid(200);
        var tabs = panel.GetVisualDescendants().OfType<TabControl>().Single();
        double listEdge = Span(((Panel)tabs.GetVisualParent()!).Children.OfType<Thumb>().Single(), panel).left;
        var item = Text(panel, Row.ObjectText).FindAncestorOfType<ListBoxItem>()!;
        double itemEdge = Span(item, panel).right - item.Padding.Right;
        foreach (var (what, cells, end) in new[] { ("header", HeaderCells(panel, vm), listEdge), ("row", RowCells(panel, vm), itemEdge) })
        {
            AssertApart(what, panel, cells);
            double right = Span(cells.Single(c => c.name == "Object").cell, panel).right;
            Assert.True(Math.Abs(right - end) < 0.5, $"the {what}'s Object column ends at {right:0.#}, not at the list's edge {end:0.#}");
        }
    });

    // [CT-STACK-WHERE-WIDTH] The Call stack tab, one frame whose Where is longer than 420 pixels of Consolas.
    private static readonly StackFrameRow Frame = new()
    {
        Index = 3, Address = "\"DumperTest58-Win64-Shipping.exe\"+4C4BC0",
        Where = "native entry of DumperTest58Actor::SnapNest_Outer +0x73 (one of 2 functions that share this code)",
    };

    /// <summary>The panel as <see cref="Laid"/> lays it out, its Call stack tab chosen and showing <see cref="Frame"/>.</summary>
    private static (DataGrid grid, DataGridColumn where) StackLaid(double detailPaneWidth)
    {
        LoadDataGridTheme();
        var (panel, vm) = Laid(detailPaneWidth);
        vm.StackRows = new[] { Frame };
        panel.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
        var where = grid.Columns.Single(c => Equals(c.Header, Header("str.CT.Stack.Col.Where")));
        return (grid, where);
    }

    /// <summary>Dragged wide, the pane gives Where everything the three fixed columns leave: a long name is not cut
    /// beside empty space. "Everything" allows a vertical scroll bar's width.</summary>
    [Fact]
    public Task A_wide_stack_pane_gives_Where_the_rest_of_its_width() => Headless.Run(() =>
    {
        var (grid, where) = StackLaid(900);
        double others = grid.Columns.Where(c => c != where).Sum(c => c.ActualWidth);
        double left = grid.Bounds.Width - others;
        Assert.True(where.ActualWidth > 420.5 && where.ActualWidth >= left - 20,
                    $"Where is {where.ActualWidth:0.#} wide in a grid of {grid.Bounds.Width:0.#} whose other columns take {others:0.#}");
    });

    /// <summary>The guard the fixed width was there for: in a narrow pane Where keeps 420 and the grid scrolls sideways,
    /// instead of squeezing the name to a few letters.</summary>
    [Fact]
    public Task A_narrow_stack_pane_keeps_Where_420_wide() => Headless.Run(() =>
    {
        var (grid, where) = StackLaid(300);
        Assert.True(where.ActualWidth >= 419.5,
                    $"Where is {where.ActualWidth:0.#} wide in a grid of {grid.Bounds.Width:0.#}");
    });
}
