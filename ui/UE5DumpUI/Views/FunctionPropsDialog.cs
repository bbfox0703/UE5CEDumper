using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.Views;

/// <summary>
/// Reverse edge: lists the properties a UFunction reads/writes (walk_function_props).
/// Opened from the Interesting Functions panel "Props" row button.
///
/// Two recovery methods, transparently chosen by the DLL: Blueprint/script
/// functions use an exact Kismet-bytecode scan (Path 1); native (C++) functions
/// — which have no bytecode — are disassembled (Path 2, Zydis) and their
/// [this+off] field accesses mapped back to class properties (heuristic, shown
/// with a Confidence column). Writes are best-effort; see the footer caveat.
///
/// Code-behind only (no XAML / CompiledBinding) to stay AOT-safe; columns use
/// FuncDataTemplate&lt;T&gt; typed lambdas, not the reflection Binding ctor.
/// </summary>
public sealed class FunctionPropsDialog : ManagedDialogWindow
{
    /// <summary>
    /// AOT-safe column sort comparers, keyed by SortMemberPath (audit #5 AF18).
    /// A code-built grid is not exempt from the rule the XAML panels follow: these
    /// are all DataGridTemplateColumns, so no binding roots the sort path and the
    /// reflection sort is trimmed away. Shared verbatim with
    /// <see cref="PropertyXrefDialog"/>'s twin grid where the property names agree.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IComparer> XrefSortComparers =
        new Dictionary<string, IComparer>
        {
            ["WriteCount"]  = DataGridSortComparers.Number<FunctionPropRef>(r => r.WriteCount),
            ["Occurrences"] = DataGridSortComparers.Number<FunctionPropRef>(r => r.Occurrences),
            ["Scope"]       = DataGridSortComparers.Ordinal<FunctionPropRef>(r => r.Scope),
            ["Confidence"]  = DataGridSortComparers.Ordinal<FunctionPropRef>(r => r.Confidence),
            ["Name"]        = DataGridSortComparers.Ordinal<FunctionPropRef>(r => r.Name),
            ["Type"]        = DataGridSortComparers.Ordinal<FunctionPropRef>(r => r.Type),
        };

    private readonly IDumpService _dump;
    private readonly IPlatformService _platform;
    private readonly string _funcName;
    private readonly string _funcAddr;
    private TextBlock _statusLabel = null!;
    private DataGrid _grid = null!;
    private Button _btnRefresh = null!;
    private Button _btnCopy = null!;
    private CheckBox _classFieldsOnly = null!;
    private DataGridColumn _confCol = null!;
    private List<FunctionPropRef> _allProps = new();
    private string _lastMethod = "bytecode";
    private int _lastUnmapped;
    private bool _lastBudgetHit;

    /// <summary>Resolve owner window + show. No-op without an address/platform/window.</summary>
    public static async Task ShowForFunctionAsync(
        string funcName, string funcAddr, IDumpService dump, IPlatformService? platform)
    {
        if (string.IsNullOrEmpty(funcAddr) || funcAddr == "0x0" || platform == null)
            return;
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } owner)
            return;
        var dialog = new FunctionPropsDialog(funcName, funcAddr, dump, platform);
        await dialog.ShowDialog(owner);
    }

    public FunctionPropsDialog(string funcName, string funcAddr, IDumpService dump, IPlatformService platform)
    {
        _funcName = funcName ?? "";
        _funcAddr = funcAddr ?? "";
        _dump = dump;
        _platform = platform;

        Title = $"Properties used by: {_funcName}";
        Width = 760;
        MinWidth = 520;
        Height = 520;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));

        var root = new DockPanel { Margin = new Thickness(12) };

        var topRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,Auto,8,Auto"),
            Margin = new Thickness(0, 0, 0, 8),
        };
        var targetLbl = new TextBlock
        {
            Text = $"{_funcName}  @ {_funcAddr}",
            Foreground = new SolidColorBrush(Color.Parse("#DCDCAA")),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(targetLbl, 0);
        topRow.Children.Add(targetLbl);
        _classFieldsOnly = new CheckBox
        {
            Content = "Class fields only",
            IsChecked = true,   // hide BP compiler locals (CallFunc_*) by default
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _classFieldsOnly.IsCheckedChanged += (_, _) => ApplyFilter();
        Grid.SetColumn(_classFieldsOnly, 2);
        topRow.Children.Add(_classFieldsOnly);
        ToolTip.SetTip(_classFieldsOnly, "Show only class-member fields (hide the function's "
            + "locals / temporaries).");
        _btnRefresh = new Button { Content = "Refresh", Padding = new Thickness(10, 4) };
        _btnRefresh.Click += async (_, _) => await RunScanAsync();
        Grid.SetColumn(_btnRefresh, 4);
        topRow.Children.Add(_btnRefresh);
        ToolTip.SetTip(_btnRefresh, "Re-analyze the function.");
        DockPanel.SetDock(topRow, Dock.Top);
        root.Children.Add(topRow);

        _statusLabel = new TextBlock
        {
            Text = "Scanning…",
            Foreground = new SolidColorBrush(Color.Parse("#808080")),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap,
        };
        DockPanel.SetDock(_statusLabel, Dock.Top);
        root.Children.Add(_statusLabel);

        var caveat = new TextBlock
        {
            Text = "Bytecode (Blueprint/script): exact — writes best-effort, wrapped LHS "
                 + "(Other.Field / Struct.Member / Arr[i]) may read as a read. "
                 + "Native disasm (C++): heuristic — recovered from x64 machine code; "
                 + "low-confidence rows are [reg+off] matches whose base isn't provably "
                 + "'this'. Verify in CE before relying on a hit.",
            Foreground = new SolidColorBrush(Color.Parse("#7A7A7A")),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        DockPanel.SetDock(caveat, Dock.Bottom);
        root.Children.Add(caveat);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        _btnCopy = new Button
        {
            Content = "Copy property name",
            Padding = new Thickness(12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = false,
        };
        _btnCopy.Click += OnCopyClicked;
        btnRow.Children.Add(_btnCopy);
        ToolTip.SetTip(_btnCopy, "Copy the selected property's name to the clipboard.");
        var btnClose = new Button { Content = "Close", Padding = new Thickness(14, 6) };
        btnClose.Click += (_, _) => Close();
        btnRow.Children.Add(btnClose);
        ToolTip.SetTip(btnClose, "Close this dialog.");
        DockPanel.SetDock(btnRow, Dock.Bottom);
        root.Children.Add(btnRow);

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
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Access",
            Width = new DataGridLength(90),
            SortMemberPath = nameof(FunctionPropRef.WriteCount),
            CellTemplate = new FuncDataTemplate<FunctionPropRef>(
                (x, _) => new TextBlock
                {
                    Text = x?.AccessSummary ?? "",
                    Foreground = new SolidColorBrush(Color.Parse(
                        (x?.WriteCount ?? 0) > 0 ? "#E0A050" : "#808080")),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        });
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Refs",
            Width = new DataGridLength(60),
            SortMemberPath = nameof(FunctionPropRef.Occurrences),
            CellTemplate = new FuncDataTemplate<FunctionPropRef>(
                (x, _) => new TextBlock
                {
                    Text = x?.Occurrences.ToString() ?? "",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        });
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Scope",
            Width = new DataGridLength(80),
            SortMemberPath = nameof(FunctionPropRef.Scope),
            CellTemplate = new FuncDataTemplate<FunctionPropRef>(
                (x, _) => new TextBlock
                {
                    Text = x?.Scope ?? "",
                    Foreground = new SolidColorBrush(Color.Parse(
                        (x?.IsClassField ?? false) ? "#4EC9B0" : "#808080")),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        });
        // Confidence column — only meaningful for native disasm (Path 2); hidden
        // for the exact bytecode path. Low-confidence rows are tinted amber.
        _confCol = new DataGridTemplateColumn
        {
            Header = "Conf",
            Width = new DataGridLength(70),
            IsVisible = false,
            SortMemberPath = nameof(FunctionPropRef.Confidence),
            CellTemplate = new FuncDataTemplate<FunctionPropRef>(
                (x, _) => new TextBlock
                {
                    Text = x?.Confidence ?? "",
                    Foreground = new SolidColorBrush(Color.Parse(
                        (x?.IsLowConfidence ?? false) ? "#E0A050" : "#4EC9B0")),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        };
        _grid.Columns.Add(_confCol);
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Property",
            Width = new DataGridLength(240),
            SortMemberPath = nameof(FunctionPropRef.Name),
            CellTemplate = new FuncDataTemplate<FunctionPropRef>(
                (x, _) => new TextBlock
                {
                    Text = x?.Name ?? "",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        });
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Type",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            SortMemberPath = nameof(FunctionPropRef.Type),
            CellTemplate = new FuncDataTemplate<FunctionPropRef>(
                (x, _) => new TextBlock
                {
                    Text = x?.Type ?? "",
                    Foreground = new SolidColorBrush(Color.Parse("#569CD6")),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        });
        // AOT-safe sort comparers (audit #5 AF18). Every column above is a
        // DataGridTemplateColumn — no column-level Binding at all — so nothing roots
        // its SortMemberPath and the reflection sort is trimmed: all six headers were
        // clickable and inert in the shipped trimmed build. Same rule and same helper
        // as the XAML panels (Helpers/DataGridSortComparers.cs class doc).
        _grid.WireSortComparers(XrefSortComparers);
        _grid.SelectionChanged += (_, _) => _btnCopy.IsEnabled = _grid.SelectedItem is FunctionPropRef;
        _grid.DoubleTapped += (_, _) => OnCopyClicked(null, null!);
        root.Children.Add(_grid);

        Content = root;
        Opened += async (_, _) => await RunScanAsync();
    }

    private async Task RunScanAsync()
    {
        if (string.IsNullOrEmpty(_funcAddr) || _funcAddr == "0x0")
        {
            _statusLabel.Text = "No function address.";
            _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
            return;
        }

        _btnRefresh.IsEnabled = false;
        _statusLabel.Text = "Scanning…";
        _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#808080"));

        try
        {
            var res = await _dump.WalkFunctionPropsAsync(_funcAddr);
            _allProps = res.Props;
            _lastMethod = res.Method;
            _lastUnmapped = res.Unmapped;
            _lastBudgetHit = res.BudgetHit;

            // Confidence column is only meaningful for the native disasm path.
            _confCol.IsVisible = res.Method == "disasm";

            if (res.Method == "none")
            {
                _grid.ItemsSource = null;
                _statusLabel.Text =
                    "Native (C++) function — no analysis available (UFunction::Func "
                    + "offset unresolved on this build).";
                _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#D4D4D4"));
            }
            else
            {
                ApplyFilter();
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Scan failed: {ex.Message}";
            _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
            _grid.ItemsSource = null;
        }
        finally
        {
            _btnRefresh.IsEnabled = true;
            _btnCopy.IsEnabled = _grid.SelectedItem is FunctionPropRef;
        }
    }

    private void ApplyFilter()
    {
        bool fieldsOnly = _classFieldsOnly.IsChecked == true;
        var shown = fieldsOnly
            ? _allProps.Where(p => p.IsClassField).ToList()
            : _allProps;
        _grid.ItemsSource = shown;

        int writers = 0;
        foreach (var p in shown) if (p.WriteCount > 0) writers++;
        var hiddenNote = fieldsOnly && _allProps.Count > shown.Count
            ? $"  ({_allProps.Count - shown.Count} locals/temporaries hidden)" : "";
        // Path 2 (native disasm): flag the heuristic source + any unmapped accesses.
        // "blueprint_no_script" is a REFUSAL, not an empty result, and it must not read like
        // one. The DLL reaches it when a script/Blueprint function has no usable Script buffer:
        // its `Func` points at the shared interpreter, so disassembling it would analyse
        // UObject::ProcessInternal and attribute the interpreter's field accesses to this
        // function. Before the gate went in (Aura.cpp, AnalyzeNativeFunctionProps) this case
        // arrived here as "disasm" and printed "[native disasm — heuristic, N unmapped]" —
        // claiming to have disassembled the function it never looked at.
        var methodNote = _lastMethod == "disasm"
            ? "  [native disasm — heuristic"
              + (_lastUnmapped > 0 ? $", {_lastUnmapped} unmapped]" : "]")
            : _lastMethod == "blueprint_no_script"
            ? "  [Blueprint function with no readable bytecode — NOTHING was analysed, "
              + "so this is not \"no properties\"]"
            : "";
        // AF7: and say so when the decoder stopped early, because "no writer found"
        // is what this dialog is read for. Amber, not green — a truncated list must
        // not look like a settled answer.
        var budgetNote = _lastBudgetHit ? PartialResultNotice.DisassemblyBudget() : "";
        _statusLabel.Text = $"{shown.Count} propert{(shown.Count == 1 ? "y" : "ies")} "
                          + $"({writers} written){hiddenNote}{methodNote}{budgetNote}";
        _statusLabel.Foreground = new SolidColorBrush(Color.Parse(
            _lastBudgetHit ? "#E0A050" : shown.Count > 0 ? "#4EC9B0" : "#D4D4D4"));
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (_grid.SelectedItem is not FunctionPropRef x || string.IsNullOrEmpty(x.Name)) return;
        await _platform.CopyToClipboardAsync(x.Name);
        _statusLabel.Text = $"Copied: {x.Name}";
        _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#4EC9B0"));
    }
}
