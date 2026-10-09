using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
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
/// [CT-COLUMNS-OVERLAP] The Call Trace list as Avalonia 12.1.3 lays it out when it is narrower than its four fixed
/// columns, as the step-3 walkthrough saw it. Each column keeps the width shown, none draws into the visible part of
/// the one on its left, and what does not fit is cut at the list's edge instead of drawing over the detail pane. A
/// source test cannot show this: the overlap came from where DockPanel puts a fixed width cell that no longer fits,
/// which no attribute in the file states. [CT-DETAIL-COVERS-LIST]: the list keeps its floor beside the detail pane
/// however wide the pane is remembered or dragged, and its rows can be clicked.
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

    /// <summary>A view model with its columns at their defaults (Time 96, Duration 88, Thread 64, Object 260), the
    /// detail pane remembered as wide as asked, and these rows.</summary>
    private static CallTraceViewModel NewVm(double detailPaneWidth, CallTraceRow[] rows)
    {
        LoadStrings();
        var dump = DispatchProxy.Create<IDumpService, Inert>();
        var log = DispatchProxy.Create<ILoggingService, Inert>();
        return new CallTraceViewModel(dump, log, new LiveFuncsViewModel(dump, log))
        {
            DetailPaneWidth = detailPaneWidth,
            Rows = rows,
        };
    }

    /// <summary>The panel in a window as wide as asked, showing <paramref name="rows"/>.</summary>
    private static (Window window, CallTracePanel panel, CallTraceViewModel vm) LaidIn(
        double windowWidth, double detailPaneWidth, params CallTraceRow[] rows)
    {
        var vm = NewVm(detailPaneWidth, rows);
        var panel = new CallTracePanel { DataContext = vm };
        var window = new Window { Content = panel, Width = windowWidth, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, panel, vm);
    }

    /// <summary>The panel in a 1000-wide window showing <see cref="Row"/>, the detail pane as wide as asked.</summary>
    private static (CallTracePanel panel, CallTraceViewModel vm) Laid(double detailPaneWidth)
    {
        var (_, panel, vm) = LaidIn(1000, detailPaneWidth, Row);
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
        ("Time", HeaderCell(panel, "str.CT.Col.Time"), vm.ShownTimeColWidth),
        ("Duration", HeaderCell(panel, "str.CT.Col.Duration"), vm.ShownDurationColWidth),
        ("Thread", HeaderCell(panel, "str.CT.Col.Thread"), vm.ShownThreadColWidth),
        ("Object", HeaderCell(panel, "str.CT.Col.Object"), vm.ShownObjectColWidth),
    ];

    private static (string name, Visual cell, double width)[] RowCells(Visual panel, CallTraceViewModel vm) =>
    [
        ("Time", Text(panel, Row.TimeText), vm.ShownTimeColWidth),
        ("Duration", Text(panel, Row.DurationText), vm.ShownDurationColWidth),
        ("Thread", Text(panel, Row.ThreadText), vm.ShownThreadColWidth),
        ("Object", Text(panel, Row.ObjectText), vm.ShownObjectColWidth),
    ];

    /// <summary>Every cell as wide as the view model shows it, and none starting inside what can be seen of the one
    /// before.</summary>
    private static void AssertApart(string what, Visual panel, (string name, Visual cell, double width)[] cells)
    {
        var spans = cells.Select(c => (c.name, span: Span(c.cell, panel), seen: VisibleRight(c.cell, panel), c.width)).ToList();
        string layout = string.Join(", ", spans.Select(s => $"{s.name} {s.span.left:0.#}-{s.span.right:0.#} (seen to {s.seen:0.#})"));
        foreach (var s in spans)
            Assert.True(Math.Abs(s.span.right - s.span.left - s.width) < 0.5,
                        $"{what}: {s.name} is {s.span.right - s.span.left:0.#} wide, shown at {s.width} ({layout})");
        for (int i = 1; i < spans.Count; i++)
            Assert.True(spans[i].span.left >= Math.Min(spans[i - 1].span.right, spans[i - 1].seen) - 0.5,
                        $"{what}: {spans[i].name} draws over {spans[i - 1].name} ({layout})");
    }

    // [CT-DETAIL-COVERS-LIST] The list is narrower than its columns only where the panel is narrower than both floors:
    // the pane keeps its 200 and the list, laid out as at its floor, is cut. 500: the list 276 wide (its header 272),
    // narrower than the four columns as the floor lays them out (328), so Function has nothing left and Object is cut.
    // 440: narrower than Time, Duration and Thread together (248), so Thread is cut too. (Before the floor, the step-3
    // walkthrough reached the same lists with the pane dragged to 700 and 760 in a 1000 window.)
    [Theory]
    [InlineData(500)]
    [InlineData(440)]
    public Task A_narrow_list_keeps_every_header_column_out_of_the_next(double window) => Headless.Run(() =>
    {
        var (_, panel, vm) = LaidIn(window, 380, Row);
        AssertApart("header", panel, HeaderCells(panel, vm));
    });

    [Theory]
    [InlineData(500)]
    [InlineData(440)]
    public Task A_narrow_list_keeps_every_row_column_out_of_the_next(double window) => Headless.Run(() =>
    {
        var (_, panel, vm) = LaidIn(window, 380, Row);
        AssertApart("row", panel, RowCells(panel, vm));
    });

    [Theory]
    [InlineData(500)]
    [InlineData(440)]
    public Task What_does_not_fit_is_cut_at_the_list_and_never_drawn_over_the_detail_pane(double window)
        => Headless.Run(() =>
    {
        var (_, panel, vm) = LaidIn(window, 380, Row);
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

    /// <summary>The panel in a 1600-wide window, where the list's floor leaves the pane room for any width asked here,
    /// its Call stack tab chosen and showing one frame: <see cref="Frame"/>, or with <paramref name="shortWhere"/> the
    /// same frame with a Where of a few words.</summary>
    private static (DataGrid grid, DataGridColumn where) StackLaid(double detailPaneWidth, bool shortWhere = false)
    {
        LoadDataGridTheme();
        var (_, panel, vm) = LaidIn(1600, detailPaneWidth, Row);
        vm.StackRows = new[] { shortWhere ? new StackFrameRow { Index = Frame.Index, Address = Frame.Address, Where = "+0x6F" } : Frame };
        panel.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
        var where = grid.Columns.Single(c => Equals(c.Header, Header("str.CT.Stack.Col.Where")));
        return (grid, where);
    }

    /// <summary>Dragged wide, the pane gives Where everything the three fixed columns leave: a long name is not cut
    /// beside empty space. "Everything" allows a vertical scroll bar's width. The short name is what tells this from a
    /// column sized to its text, which a long name alone would pass.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task A_wide_stack_pane_gives_Where_the_rest_of_its_width(bool shortWhere) => Headless.Run(() =>
    {
        var (grid, where) = StackLaid(900, shortWhere);
        double others = grid.Columns.Where(c => c != where).Sum(c => c.ActualWidth);
        double left = grid.Bounds.Width - others;
        Assert.True(where.ActualWidth > 420.5 && where.ActualWidth >= left - 20,
                    $"Where is {where.ActualWidth:0.#} wide in a grid of {grid.Bounds.Width:0.#} whose other columns take {others:0.#}");
    });

    /// <summary>The guard the fixed widths were there for: in a narrow pane Where keeps 420 and Address its 170, and the
    /// grid scrolls sideways instead of squeezing either. With Where a star column, the DataGrid takes a narrow pane's
    /// shortfall out of the other columns down to their floors: build 3644 showed Address cut to "Dump...".</summary>
    [Fact]
    public Task A_narrow_stack_pane_keeps_Where_420_and_Address_170_wide() => Headless.Run(() =>
    {
        var (grid, where) = StackLaid(300);
        var address = grid.Columns.Single(c => Equals(c.Header, Header("str.CT.Stack.Col.Address")));
        Assert.True(where.ActualWidth >= 419.5 && address.ActualWidth >= 169.5,
                    $"Where is {where.ActualWidth:0.#} and Address {address.ActualWidth:0.#} wide in a grid of {grid.Bounds.Width:0.#}");
    });

    // ---- [CT-DETAIL-COVERS-LIST] the list keeps its floor beside the detail pane ----

    private static readonly CallTraceRow Row2 = new()
    {
        TimeText = "1240.001", DurationText = "4.5", ThreadText = "52",
        FunctionText = "DumperTest58Actor_C::SnapProbe_Call", ObjectText = "DumperTest58Actor_1",
    };

    /// <summary>The list's floor, as the view model states it: a row's item padding, Time, Duration and Thread at their
    /// default widths, Object at its floor and <see cref="FunctionSlice"/> of Function.</summary>
    private const double ListFloor = 512;
    private const double FunctionSlice = 160;
    /// <summary>What the panel's width loses before the list and the pane share it: its margin on each side and the
    /// pane's handle.</summary>
    private const double PanelChrome = 8 + 8 + 8;

    private static ListBox List(Visual panel) => panel.GetVisualDescendants().OfType<ListBox>().Single();
    private static TabControl Pane(Visual panel) => panel.GetVisualDescendants().OfType<TabControl>().Single();
    private static Thumb PaneHandle(Visual panel) => ((Panel)Pane(panel).GetVisualParent()!).Children.OfType<Thumb>().Single();
    private static Thumb HeaderHandle(Visual panel, string key) => ((Panel)HeaderCell(panel, key)).Children.OfType<Thumb>().Single();

    /// <summary>A row's Function cell: the clipping panel that carries the whole name as its tooltip.</summary>
    private static DockPanel FunctionCell(Visual panel, CallTraceRow row)
        => panel.GetVisualDescendants().OfType<DockPanel>().Single(d => ToolTip.GetTip(d) as string == row.FunctionText);

    /// <summary>The list at its floor or wider, and in <paramref name="row"/> Time, Duration and Thread whole and at
    /// least <see cref="FunctionSlice"/> of Function: what a call is picked by.</summary>
    private static void AssertListFloor(Visual panel, CallTraceRow row)
    {
        double list = List(panel).Bounds.Width;
        Assert.True(list >= ListFloor - 0.5, $"the list is {list:0.#} wide, under its floor of {ListFloor}");
        foreach (var text in new[] { row.TimeText, row.DurationText, row.ThreadText })
        {
            var cell = Text(panel, text);
            var (left, right) = Span(cell, panel);
            double seen = VisibleRight(cell, panel);
            Assert.True(seen >= right - 0.5, $"the cell {text} at {left:0.#}-{right:0.#} is cut at {seen:0.#}");
        }
        var function = FunctionCell(panel, row);
        double shown = VisibleRight(function, panel) - Span(function, panel).left;
        Assert.True(shown >= FunctionSlice - 0.5, $"Function shows {shown:0.#} px, under {FunctionSlice}");
    }

    private static Point Centre(Visual v, TopLevel top)
        => v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), top)!.Value;

    /// <summary>A press and release near the left of what can be seen of <paramref name="target"/>.</summary>
    private static void Click(TopLevel top, Visual target)
    {
        var p = target.TranslatePoint(new Point(Math.Min(target.Bounds.Width / 2, 40), target.Bounds.Height / 2), top)!.Value;
        top.MouseDown(p, MouseButton.Left);
        top.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>One drag: pressed on the handle's centre, moved <paramref name="dx"/> across, released there.</summary>
    private static void Drag(TopLevel top, Visual handle, double dx)
    {
        var from = Centre(handle, top);
        var to = new Point(from.X + dx, from.Y);
        top.MouseDown(from, MouseButton.Left);
        top.MouseMove(to, RawInputModifiers.LeftMouseButton);
        top.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The maintainer's report on build 3645: a remembered pane wider than the panel covered the list, so no
    /// row could be clicked and the pane had nothing to show. However wide the pane is remembered, the list keeps its
    /// floor beside it and a click selects a row. 800: the app's narrowest window; 1000 with 766, the width remembered on
    /// the maintainer's machine.</summary>
    [Theory]
    [InlineData(800, 4096)]
    [InlineData(1000, 766)]
    public Task A_remembered_pane_wider_than_the_panel_leaves_the_list_its_floor_and_a_row_to_click(double window, double pane)
        => Headless.Run(() =>
    {
        var (top, panel, vm) = LaidIn(window, pane, Row, Row2);
        AssertListFloor(panel, Row2);
        Assert.Equal(window - PanelChrome - ListFloor, Pane(panel).Bounds.Width, 0.5);
        Click(top, FunctionCell(panel, Row2));
        Assert.Equal(1, vm.SelectedIndex);
    });

    /// <summary>The panel narrowed under a remembered pane (a smaller window, the object tree unfolded) fits the pane in
    /// the same layout; widened again, it gives the remembered width back.</summary>
    [Fact]
    public Task A_narrower_panel_refits_the_pane_and_a_wider_one_gives_back_the_remembered_width() => Headless.Run(() =>
    {
        var (top, panel, vm) = LaidIn(1600, 766, Row, Row2);
        Assert.Equal(766, Pane(panel).Bounds.Width, 0.5);
        top.Width = 800;
        Dispatcher.UIThread.RunJobs();
        AssertListFloor(panel, Row2);
        Assert.Equal(766.0, vm.DetailPaneWidth);
        top.Width = 1600;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(766, Pane(panel).Bounds.Width, 0.5);
    });

    /// <summary>The tab hidden while the window narrowed: shown again, its list still keeps its floor.</summary>
    [Fact]
    public Task The_tab_shown_again_after_the_window_narrowed_keeps_the_lists_floor() => Headless.Run(() =>
    {
        var vm = NewVm(766, new[] { Row, Row2 });
        var panel = new CallTracePanel { DataContext = vm };
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Other", Content = new TextBlock { Text = "other" } });
        tabs.Items.Add(new TabItem { Header = "Call Trace", Content = panel });
        tabs.SelectedIndex = 1;
        var window = new Window { Content = tabs, Width = 1600, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(766, Pane(panel).Bounds.Width, 0.5);

        tabs.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        window.Width = 800;
        Dispatcher.UIThread.RunJobs();
        tabs.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        AssertListFloor(panel, Row2);
    });

    /// <summary>A drag of the pane's handle past the list's floor stops there, and a drag back moves the pane at once:
    /// a drag adds to the width shown, so nothing past the limit is kept to be undone first.</summary>
    [Fact]
    public Task A_pane_drag_past_the_lists_floor_stops_there_and_the_next_drag_back_moves_at_once() => Headless.Run(() =>
    {
        var (top, panel, _) = LaidIn(1000, 380, Row);
        double limit = 1000 - PanelChrome - ListFloor;
        Drag(top, PaneHandle(panel), -400);
        Assert.Equal(limit, Pane(panel).Bounds.Width, 0.5);
        Assert.Equal(ListFloor, List(panel).Bounds.Width, 0.5);
        Drag(top, PaneHandle(panel), +30);
        Assert.Equal(limit - 30, Pane(panel).Bounds.Width, 0.5);
    });

    /// <summary>The same for a column: Thread dragged far to the right pushes Object to its floor and stops where
    /// Function keeps its slice; a drag back narrows it at once. The list is 596 wide, a row's cells 572.</summary>
    [Fact]
    public Task A_column_drag_past_its_limit_stops_where_Function_keeps_its_slice_and_drags_back_at_once() => Headless.Run(() =>
    {
        var (top, panel, _) = LaidIn(1000, 380, Row);
        double limit = 572 - FunctionSlice - 96 - 88 - 80;
        Drag(top, HeaderHandle(panel, "str.CT.Col.Thread"), +400);
        Assert.Equal(limit, HeaderCell(panel, "str.CT.Col.Thread").Bounds.Width, 0.5);
        Assert.Equal(80, Text(panel, Row.ObjectText).Bounds.Width, 0.5);
        AssertListFloor(panel, Row);
        Drag(top, HeaderHandle(panel, "str.CT.Col.Thread"), -10);
        Assert.Equal(limit - 10, HeaderCell(panel, "str.CT.Col.Thread").Bounds.Width, 0.5);
    });

    /// <summary>Each column's handle widens its own column by the step dragged, in the direction the handle faces:
    /// Time, Duration and Thread to the right, Object to the left. In a 1600 window no column is at a limit.</summary>
    [Theory]
    [InlineData("str.CT.Col.Time", 20)]
    [InlineData("str.CT.Col.Duration", 20)]
    [InlineData("str.CT.Col.Thread", 20)]
    [InlineData("str.CT.Col.Object", -20)]
    public Task Each_column_handle_widens_its_own_column_by_the_step_dragged(string key, double dx) => Headless.Run(() =>
    {
        var (top, panel, vm) = LaidIn(1600, 380, Row);
        var before = HeaderCells(panel, vm).Select(c => (c.name, c.cell.Bounds.Width)).ToList();
        Drag(top, HeaderHandle(panel, key), dx);
        var after = HeaderCells(panel, vm).Select(c => (c.name, c.cell.Bounds.Width)).ToList();
        string dragged = Header(key);
        for (int i = 0; i < before.Count; i++)
        {
            bool mine = Header("str.CT.Col." + before[i].name) == dragged;
            Assert.True(Math.Abs(after[i].Width - before[i].Width - (mine ? 20 : 0)) < 0.5,
                        $"{before[i].name} went from {before[i].Width:0.#} to {after[i].Width:0.#} on a drag of {key}");
        }
    });

    /// <summary>A view model given to a panel already laid out is fitted too, though the panel's size never changed: its
    /// pane width, bound and not yet fitted, changes the pane's, and Avalonia measures up to the panel again, which
    /// tells the view model the room. The panel needs no remeasure of its own on a new view model (a mutation pass
    /// removed one with this test still green).</summary>
    [Fact]
    public Task A_view_model_set_after_the_panel_was_laid_out_is_fitted_too() => Headless.Run(() =>
    {
        LoadStrings();
        var panel = new CallTracePanel();
        var window = new Window { Content = panel, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        panel.DataContext = NewVm(4096, new[] { Row, Row2 });
        Dispatcher.UIThread.RunJobs();
        AssertListFloor(panel, Row2);
    });
}
