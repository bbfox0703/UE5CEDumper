using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.Views;

/// <summary>
/// Stage 2 (Invoke param picker): modal dialog that lists live UObject
/// instances matching an expected UClass, returns the chosen instance's
/// address back to the caller. Opens from <see cref="InvokeParamDialog"/>
/// when the user clicks "Pick..." next to a pointer-flavoured parameter.
///
/// Mechanics:
/// <list type="bullet">
/// <item>Auto-runs <c>FindInstancesAsync(expectedClassName, exactMatch:false)</c>
///     on open — substring match catches subclasses of the expected base
///     (which is what an ObjectProperty parameter genuinely accepts).</item>
/// <item>User can edit the class filter + toggle exact-match + refresh.</item>
/// <item>Double-click a row OR click "Use selected" → <c>Close(addressHex)</c>.</item>
/// <item>Cancel → <c>Close(null)</c>.</item>
/// </list>
///
/// Code-behind only (no XAML / CompiledBinding) to stay AOT-safe like the
/// rest of the project. Visuals mirror InvokeParamDialog's palette.
/// </summary>
public sealed class ObjectInstancePickerDialog : ManagedDialogWindow
{
    /// <summary>
    /// AOT-safe column sort comparers, keyed by SortMemberPath. Found by the
    /// repo-wide DataGrid sweep behind audit #5 AF16-AF23 — no finding named this
    /// dialog, and it is the seventh site with the identical shape.
    /// <para><b>It also carried a written justification for the defect</b>, which is
    /// how it survived: "sort is a runtime nice-to-have ... sortable headers still
    /// work in the non-AOT default build". True and beside the point — the build we
    /// SHIP is the trimmed one, so the headers were dead for every user. Wiring them
    /// costs four lines.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IComparer> PickerSortComparers =
        new Dictionary<string, IComparer>
        {
            ["Index"]     = DataGridSortComparers.Number<InstanceResult>(r => r.Index),
            ["Address"]   = DataGridSortComparers.Ordinal<InstanceResult>(r => r.Address),
            ["ClassName"] = DataGridSortComparers.Ordinal<InstanceResult>(r => r.ClassName),
            ["Name"]      = DataGridSortComparers.Ordinal<InstanceResult>(r => r.Name),
        };

    private readonly IDumpService _dump;
    private readonly string _expectedClassName;
    private TextBox _classBox = null!;
    private CheckBox _exactBox = null!;
    private TextBlock _statusLabel = null!;
    private DataGrid _grid = null!;
    private Button _btnUse = null!;
    private Button _btnRefresh = null!;
    private Button _btnCancel = null!;

    public ObjectInstancePickerDialog(string expectedClassName, IDumpService dump)
    {
        _expectedClassName = expectedClassName ?? "";
        _dump = dump;

        Title = string.IsNullOrEmpty(_expectedClassName)
            ? "Pick UObject instance"
            : $"Pick UObject instance — expected: {_expectedClassName}";
        Width = 720;
        MinWidth = 480;
        Height = 520;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));

        var root = new DockPanel { Margin = new Thickness(12) };

        // === Top: search row ===
        var topRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,8,*,8,Auto,8,Auto"),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var classLbl = new TextBlock
        {
            Text = "Class filter:",
            Foreground = new SolidColorBrush(Color.Parse("#DCDCAA")),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(classLbl, 0);
        topRow.Children.Add(classLbl);

        _classBox = new TextBox
        {
            Text = _expectedClassName,
            FontSize = 12,
            Padding = new Thickness(4, 2),
        };
        Grid.SetColumn(_classBox, 2);
        topRow.Children.Add(_classBox);

        _exactBox = new CheckBox
        {
            Content = "Exact",
            IsChecked = false,
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_exactBox, 4);
        topRow.Children.Add(_exactBox);

        _btnRefresh = new Button
        {
            Content = "Refresh",
            Padding = new Thickness(10, 4),
        };
        _btnRefresh.Click += OnRefreshClicked;
        Grid.SetColumn(_btnRefresh, 6);
        topRow.Children.Add(_btnRefresh);

        DockPanel.SetDock(topRow, Dock.Top);
        root.Children.Add(topRow);

        // === Status label (under search row) ===
        _statusLabel = new TextBlock
        {
            Text = "Searching...",
            Foreground = new SolidColorBrush(Color.Parse("#808080")),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6),
        };
        DockPanel.SetDock(_statusLabel, Dock.Top);
        root.Children.Add(_statusLabel);

        // === Bottom: action buttons ===
        var btnRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };

        _btnUse = new Button
        {
            Content = "Use selected",
            Padding = new Thickness(14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = false,
        };
        _btnUse.Click += OnUseClicked;
        btnRow.Children.Add(_btnUse);

        _btnCancel = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(14, 6),
        };
        _btnCancel.Click += (_, _) => Close(null);
        btnRow.Children.Add(_btnCancel);

        DockPanel.SetDock(btnRow, Dock.Bottom);
        root.Children.Add(btnRow);

        // === Center: instance grid ===
        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserResizeColumns = true,
            CanUserReorderColumns = false,
            CanUserSortColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            FontSize = 12,
        };
        // AOT-safe columns: typed lambda templates instead of
        // `new Binding(propertyName)` -- the string-based Binding ctor is
        // ReflectionBinding (IL2026 / IL3050) and would silently render
        // blank cells under PublishAot=true. FuncDataTemplate<T> resolves
        // each cell value via direct property access, no reflection.
        // SortMemberPath + an explicit CustomSortComparer (wired below): the path
        // alone is not enough, because the reflection sort behind it is trimmed in
        // the build we actually ship. See PickerSortComparers.
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Index",
            Width = new DataGridLength(70),
            SortMemberPath = nameof(InstanceResult.Index),
            CellTemplate = new FuncDataTemplate<InstanceResult>(
                (ir, _) => new TextBlock
                {
                    Text = ir?.Index.ToString() ?? "",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        });
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Address",
            Width = new DataGridLength(180),
            SortMemberPath = nameof(InstanceResult.Address),
            CellTemplate = new FuncDataTemplate<InstanceResult>(
                (ir, _) => new TextBlock
                {
                    Text = ir?.Address ?? "",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        });
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Class",
            Width = new DataGridLength(200),
            SortMemberPath = nameof(InstanceResult.ClassName),
            CellTemplate = new FuncDataTemplate<InstanceResult>(
                (ir, _) => new TextBlock
                {
                    Text = ir?.ClassName ?? "",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        });
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Name",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            SortMemberPath = nameof(InstanceResult.Name),
            CellTemplate = new FuncDataTemplate<InstanceResult>(
                (ir, _) => new TextBlock
                {
                    Text = ir?.Name ?? "",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        });
        _grid.WireSortComparers(PickerSortComparers);
        _grid.SelectionChanged += (_, _) => _btnUse.IsEnabled = _grid.SelectedItem is InstanceResult;
        _grid.DoubleTapped += OnGridDoubleTapped;

        root.Children.Add(_grid);

        Content = root;

        Opened += async (_, _) => await RunSearchAsync();
    }

    private async void OnRefreshClicked(object? sender, RoutedEventArgs e)
        => await RunSearchAsync();

    private async Task RunSearchAsync()
    {
        var cls = _classBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(cls))
        {
            _statusLabel.Text = "Enter a class name to search.";
            _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
            _grid.ItemsSource = null;
            return;
        }

        _btnRefresh.IsEnabled = false;
        _statusLabel.Text = "Searching...";
        _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#808080"));

        try
        {
            var exact = _exactBox.IsChecked == true;
            var res = await _dump.FindInstancesAsync(cls, exactMatch: exact);
            _grid.ItemsSource = res.Instances;

            // Status line: tells the user how many we got, and the diagnostic
            // counts from the DLL (scanned / non-null / named) — same info
            // the InstanceFinderPanel surfaces, so users get a consistent
            // feel for "is the game even alive right now".
            _statusLabel.Text = $"{res.Instances.Count} instance(s) — "
                              + $"scanned {res.Scanned:N0}, non-null {res.NonNull:N0}, named {res.Named:N0}"
                              + (exact ? " (exact match)" : " (substring match — catches subclasses)");
            _statusLabel.Foreground = new SolidColorBrush(Color.Parse(
                res.Instances.Count > 0 ? "#4EC9B0" : "#D4D4D4"));
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Search failed: {ex.Message}";
            _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
            _grid.ItemsSource = null;
        }
        finally
        {
            _btnRefresh.IsEnabled = true;
            _btnUse.IsEnabled = _grid.SelectedItem is InstanceResult;
        }
    }

    private void OnUseClicked(object? sender, RoutedEventArgs e)
    {
        if (_grid.SelectedItem is InstanceResult ir && !string.IsNullOrEmpty(ir.Address))
            Close(ir.Address);
    }

    private void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Mirrors the LiveWalker / InstanceFinder UX: double-click is the
        // primary "this one" gesture, distinct from single-click selection.
        if (_grid.SelectedItem is InstanceResult ir && !string.IsNullOrEmpty(ir.Address))
            Close(ir.Address);
    }
}
