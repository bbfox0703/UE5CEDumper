using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.Views;

/// <summary>
/// Shows which UFunctions reference a given FProperty (find_property_xrefs):
/// answers "which methods use this field?". Opened from the Class Struct panel
/// field grid context menu.
///
/// Static Kismet-bytecode scan — Blueprint/script functions only. Native
/// (C++) functions have empty bytecode and are invisible; this is surfaced in
/// the footer so an empty result on an engine field doesn't read as a failure.
///
/// Code-behind only (no XAML / CompiledBinding) to stay AOT-safe like
/// ObjectInstancePickerDialog. Columns use FuncDataTemplate&lt;T&gt; (typed
/// lambdas) rather than the reflection-based <c>new Binding(string)</c> ctor.
/// </summary>
public sealed class PropertyXrefDialog : ManagedDialogWindow
{
    /// <summary>
    /// AOT-safe column sort comparers, keyed by SortMemberPath (audit #5 AF23).
    /// Occurrences / WriteCount sort on the NUMERIC property even though the cells
    /// render "3W / 4R" — a string sort of AccessSummary would be meaningless.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IComparer> XrefSortComparers =
        new Dictionary<string, IComparer>
        {
            ["Kind"]             = DataGridSortComparers.Ordinal<PropertyXrefMatch>(r => r.Kind),
            ["Occurrences"]      = DataGridSortComparers.Number<PropertyXrefMatch>(r => r.Occurrences),
            ["WriteCount"]       = DataGridSortComparers.Number<PropertyXrefMatch>(r => r.WriteCount),
            ["OwnerClassName"]   = DataGridSortComparers.Ordinal<PropertyXrefMatch>(r => r.OwnerClassName),
            ["EventName"]        = DataGridSortComparers.Ordinal<PropertyXrefMatch>(r => r.EventName),
            ["FunctionFullName"] = DataGridSortComparers.Ordinal<PropertyXrefMatch>(r => r.FunctionFullName),
        };

    private readonly IDumpService _dump;
    private readonly IPlatformService _platform;
    private readonly string _fieldName;
    private readonly string _propAddr;
    private readonly string _classAddr;
    // Class mode: scan find_functions_by_class (which UFunctions take this class
    // as a param) instead of find_property_xrefs. Reuses the same grid/columns.
    private readonly bool _classMode;
    private CheckBox _gameOnlyBox = null!;
    private TextBlock _statusLabel = null!;
    private DataGrid _grid = null!;
    private Button _btnRefresh = null!;
    private Button _btnCopy = null!;
    private Button _btnDisasm = null!;
    private Button _btnLocate = null!;
    private Button _btnClose = null!;

    /// <summary>App-global AOBMaker bridge for the "Disassemble in CE" push. A
    /// code-behind dialog can't take per-instance DI, so MainWindow sets this
    /// once at startup; null/unavailable hides the button.</summary>
    public static IAobMakerBridge? SharedAobMaker;

    /// <summary>Raised when the user clicks "Locate class" on a row — MainWindow
    /// subscribes once, resolves a live (non-CDO) instance of the class, and
    /// navigates to Live Walker (GWorld path). Modal: the dialog closes first.</summary>
    public static event Action<string>? LocateClassInGWorldRequested;

    /// <summary>
    /// Resolve the desktop owner window and show the xref dialog for a field.
    /// No-op if there is no FProperty address, no platform service, or no
    /// owner window. Shared by the Class Struct, Property Search, and
    /// Interesting Properties panels so the owner-resolution lives in one place.
    /// </summary>
    public static async Task ShowForFieldAsync(
        string fieldName, string fieldType, string propAddr,
        IDumpService dump, IPlatformService? platform)
    {
        if (string.IsNullOrEmpty(propAddr) || propAddr == "0x0" || platform == null)
            return;
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } owner)
            return;
        var dialog = new PropertyXrefDialog(fieldName, fieldType, propAddr, dump, platform);
        await dialog.ShowDialog(owner);
    }

    /// <summary>
    /// Show the dialog in CLASS mode: which UFunctions take <paramref name="classAddr"/>
    /// (a UClass / UScriptStruct) as a parameter or return value
    /// (find_functions_by_class). Reflection-based, so native functions are
    /// included. Shared by the Instances and Classes surfaces.
    /// </summary>
    public static async Task ShowForClassAsync(
        string className, string classAddr,
        IDumpService dump, IPlatformService? platform)
    {
        if (string.IsNullOrEmpty(classAddr) || classAddr == "0x0" || platform == null)
            return;
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } owner)
            return;
        var dialog = new PropertyXrefDialog(className, classAddr, dump, platform);
        await dialog.ShowDialog(owner);
    }

    public PropertyXrefDialog(string fieldName, string fieldType, string propAddr,
                              IDumpService dump, IPlatformService platform)
        : this(dump, platform, classMode: false,
               fieldName: fieldName ?? "", propAddr: propAddr ?? "", classAddr: "",
               title: $"Functions using field: {fieldName ?? ""}",
               targetText: $"{fieldType} {fieldName}  @ {propAddr}",
               caveat: "Note: Blueprint/script functions only. Native (C++) functions have no bytecode "
                     + "and cannot be detected here — an empty result on an engine field is expected.")
    { }

    private PropertyXrefDialog(string className, string classAddr,
                               IDumpService dump, IPlatformService platform)
        : this(dump, platform, classMode: true,
               fieldName: className ?? "", propAddr: "", classAddr: classAddr ?? "",
               title: $"Functions taking class: {className ?? ""}",
               targetText: $"class {className}  @ {classAddr}",
               caveat: "Reflection scan of UFunction parameters — finds functions that take this "
                     + "class/struct as a parameter or return value (native functions included). "
                     + "Functions that only touch the class via native machine code are not listed.")
    { }

    private PropertyXrefDialog(IDumpService dump, IPlatformService platform, bool classMode,
                               string fieldName, string propAddr, string classAddr,
                               string title, string targetText, string caveat)
    {
        _fieldName = fieldName;
        _propAddr = propAddr;
        _classAddr = classAddr;
        _classMode = classMode;
        _dump = dump;
        _platform = platform;

        Title = title;
        Width = 860;
        MinWidth = 560;
        Height = 520;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));

        var root = new DockPanel { Margin = new Thickness(12) };

        // === Top: target + controls ===
        var topRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,Auto,8,Auto"),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var targetLbl = new TextBlock
        {
            Text = targetText,
            Foreground = new SolidColorBrush(Color.Parse("#DCDCAA")),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(targetLbl, 0);
        topRow.Children.Add(targetLbl);

        _gameOnlyBox = new CheckBox
        {
            Content = "Game only",
            IsChecked = true,
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_gameOnlyBox, 2);
        topRow.Children.Add(_gameOnlyBox);
        ToolTip.SetTip(_gameOnlyBox, "Restrict the scan to game (non-engine) classes.");

        _btnRefresh = new Button { Content = "Refresh", Padding = new Thickness(10, 4) };
        _btnRefresh.Click += async (_, _) => await RunScanAsync();
        Grid.SetColumn(_btnRefresh, 4);
        topRow.Children.Add(_btnRefresh);
        ToolTip.SetTip(_btnRefresh, "Re-run the scan.");

        DockPanel.SetDock(topRow, Dock.Top);
        root.Children.Add(topRow);

        // === Status (under top row) ===
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

        // === Footer: native-coverage caveat + buttons ===
        var caveatBlock = new TextBlock
        {
            Text = caveat,
            Foreground = new SolidColorBrush(Color.Parse("#7A7A7A")),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        DockPanel.SetDock(caveatBlock, Dock.Bottom);
        root.Children.Add(caveatBlock);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        // Disassemble in CE: resolve UFunction->Func via the DLL, then jump CE's
        // disassembler there + drop a labeled address-list row. Hidden when no
        // AOBMaker bridge is available.
        _btnDisasm = new Button
        {
            Content = "⚙ Disassemble in CE",
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = false,   // enabled on selection + when AOBMaker is connected
        };
        _btnDisasm.Click += OnDisassembleClicked;
        // Conditional hint: different text depending on whether the AOBMaker CE
        // plugin is connected (the button is visible-but-disabled when absent so
        // the user can see why).
        ToolTip.SetTip(_btnDisasm, SharedAobMaker?.IsAvailable == true
            ? "Resolve the selected function's native code address (UFunction->Func) and jump "
            + "CE's disassembler to it (also drops a labeled byte-array row in CE's address "
            + "list). Native functions only — Blueprint functions point at the shared "
            + "interpreter and will report no per-function code."
            : "AOBMaker CE plugin not connected — connect it in CE to push the function's "
            + "code address to the disassembler.");
        btnRow.Children.Add(_btnDisasm);

        // Locate the selected row's owning class in GWorld (resolve a live
        // instance + switch to Live Walker). Modal: close before navigating.
        _btnLocate = new Button
        {
            Content = "🌍 Locate class",
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = false,
        };
        _btnLocate.Click += OnLocateClicked;
        btnRow.Children.Add(_btnLocate);
        ToolTip.SetTip(_btnLocate, "Find a live (non-CDO) instance of the selected row's owning "
            + "class and show its path from GWorld in Live Walker.");

        _btnCopy = new Button
        {
            Content = "Copy function path",
            Padding = new Thickness(12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = false,
        };
        _btnCopy.Click += OnCopyClicked;
        btnRow.Children.Add(_btnCopy);
        ToolTip.SetTip(_btnCopy, "Copy the selected function's full path to the clipboard.");

        _btnClose = new Button { Content = "Close", Padding = new Thickness(14, 6) };
        _btnClose.Click += (_, _) => Close();
        btnRow.Children.Add(_btnClose);
        ToolTip.SetTip(_btnClose, "Close this dialog.");

        DockPanel.SetDock(btnRow, Dock.Bottom);
        root.Children.Add(btnRow);

        // === Center: xref grid ===
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
            Header = "Kind",
            Width = new DataGridLength(90),
            SortMemberPath = nameof(PropertyXrefMatch.Kind),
            CellTemplate = new FuncDataTemplate<PropertyXrefMatch>(
                (x, _) => new TextBlock
                {
                    Text = x?.Kind ?? "",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        });
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Refs",
            Width = new DataGridLength(60),
            SortMemberPath = nameof(PropertyXrefMatch.Occurrences),
            CellTemplate = new FuncDataTemplate<PropertyXrefMatch>(
                (x, _) => new TextBlock
                {
                    Text = x?.Occurrences.ToString() ?? "",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        });
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Access",
            Width = new DataGridLength(90),
            SortMemberPath = nameof(PropertyXrefMatch.WriteCount),
            CellTemplate = new FuncDataTemplate<PropertyXrefMatch>(
                (x, _) => new TextBlock
                {
                    Text = x?.AccessSummary ?? "",
                    // Writes stand out (orange); pure reads stay muted.
                    Foreground = new SolidColorBrush(Color.Parse(
                        (x?.WriteCount ?? 0) > 0 ? "#E0A050" : "#808080")),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        });
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Owner Class",
            Width = new DataGridLength(220),
            SortMemberPath = nameof(PropertyXrefMatch.OwnerClassName),
            CellTemplate = new FuncDataTemplate<PropertyXrefMatch>(
                (x, _) => new TextBlock
                {
                    Text = x?.OwnerClassName ?? "",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        });
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Event",
            Width = new DataGridLength(160),
            SortMemberPath = nameof(PropertyXrefMatch.EventName),
            CellTemplate = new FuncDataTemplate<PropertyXrefMatch>(
                (x, _) => new TextBlock
                {
                    Text = x?.EventName ?? "",
                    Foreground = new SolidColorBrush(Color.Parse("#C586C0")),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                }, supportsRecycling: false),
        });
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Function",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            SortMemberPath = nameof(PropertyXrefMatch.FunctionFullName),
            CellTemplate = new FuncDataTemplate<PropertyXrefMatch>(
                (x, _) => new TextBlock
                {
                    Text = x?.FunctionFullName ?? "",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                }, supportsRecycling: false),
        });
        // AOT-safe sort comparers (audit #5 AF23) — the twin of
        // FunctionPropsDialog's grid, and it carried the same defect: six
        // DataGridTemplateColumns, CanUserSortColumns = true, no comparer, so the
        // reflection sort is trimmed and every header is inert in the shipped build.
        _grid.WireSortComparers(XrefSortComparers);
        _grid.SelectionChanged += (_, _) =>
        {
            bool has = _grid.SelectedItem is PropertyXrefMatch;
            _btnCopy.IsEnabled = has;
            _btnDisasm.IsEnabled = has && SharedAobMaker?.IsAvailable == true;
            _btnLocate.IsEnabled = has;
        };
        _grid.DoubleTapped += (_, _) => OnCopyClicked(null, null!);

        root.Children.Add(_grid);

        Content = root;

        Opened += async (_, _) => await RunScanAsync();
    }

    private async Task RunScanAsync()
    {
        var target = _classMode ? _classAddr : _propAddr;
        if (string.IsNullOrEmpty(target) || target == "0x0")
        {
            _statusLabel.Text = _classMode
                ? "No UClass address for this row."
                : "No FProperty address for this field.";
            _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
            return;
        }

        _btnRefresh.IsEnabled = false;
        _statusLabel.Text = _classMode ? "Scanning parameters…" : "Scanning bytecode…";
        _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#808080"));

        try
        {
            var gameOnly = _gameOnlyBox.IsChecked == true;
            var res = _classMode
                ? await _dump.FindFunctionsByClassAsync(_classAddr, gameOnly)
                : await _dump.FindPropertyXrefsAsync(_propAddr, gameOnly);
            _grid.ItemsSource = res.Xrefs;

            var s = res.Scan;
            var stats = s == null
                ? ""
                : (_classMode
                    ? $" — scanned {s.FunctionsScanned:N0} funcs ({s.FunctionsWithScript:N0} matched) "
                    : $" — scanned {s.FunctionsScanned:N0} funcs ({s.FunctionsWithScript:N0} with bytecode) ")
                + $"over {s.ObjectsTotal:N0} objects in {s.DurationMs}ms"
                + (s.DeadlineHit ? " [DEADLINE HIT — partial]" : "");

            _statusLabel.Text = _classMode
                ? $"{res.Xrefs.Count} function(s) take this class{stats}"
                : $"{res.Xrefs.Count} function(s) reference this field{stats}";
            _statusLabel.Foreground = new SolidColorBrush(Color.Parse(
                res.Xrefs.Count > 0 ? "#4EC9B0" : "#D4D4D4"));
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
            bool has = _grid.SelectedItem is PropertyXrefMatch;
            _btnCopy.IsEnabled = has;
            _btnDisasm.IsEnabled = has && SharedAobMaker?.IsAvailable == true;
            _btnLocate.IsEnabled = has;
        }
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (_grid.SelectedItem is not PropertyXrefMatch x || string.IsNullOrEmpty(x.FunctionFullName))
            return;
        await _platform.CopyToClipboardAsync(x.FunctionFullName);
        _statusLabel.Text = $"Copied: {x.FunctionFullName}";
        _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#4EC9B0"));
    }

    // "Disassemble in CE": resolve the UFunction's native code entry point
    // (UFunction->Func) via the DLL, then drop a labeled CE address-list row and
    // jump CE's disassembler there. Native funcs land on the execXxx thunk; BP
    // funcs land on the interpreter (noted to the user).
    private async void OnDisassembleClicked(object? sender, RoutedEventArgs e)
    {
        if (_grid.SelectedItem is not PropertyXrefMatch x || string.IsNullOrEmpty(x.FunctionAddress)) return;
        var bridge = SharedAobMaker;
        if (bridge == null || !bridge.IsAvailable) return;
        _btnDisasm.IsEnabled = false;
        try
        {
            var codeAddr = await _dump.GetFunctionCodeAddrAsync(x.FunctionAddress);
            if (string.IsNullOrEmpty(codeAddr) || codeAddr == "0x0")
            {
                _statusLabel.Text = $"No native code address for {x.FunctionName} "
                                  + "(Blueprint-only function, or UFunction::Func offset not yet detected).";
                _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#E0A050"));
                return;
            }
            var bareHex = codeAddr.Replace("0x", "").Replace("0X", "");
            // ByteArray (8) + showAsHex so the user can right-click → "find what executes".
            await bridge.CreateMemoryRecordAsync(x.FunctionName + " (code)", bareHex, 8, false, true);
            await bridge.NavigateDisassemblerAsync(bareHex);
            _statusLabel.Text = $"Pushed {x.FunctionName} → CE disassembler @ {codeAddr}";
            _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#4EC9B0"));
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"CE push failed: {ex.Message}";
            _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
        }
        finally
        {
            _btnDisasm.IsEnabled = _grid.SelectedItem is PropertyXrefMatch && SharedAobMaker?.IsAvailable == true;
        }
    }

    // "Locate class": resolve a live instance of the row's owning class in GWorld
    // and switch to Live Walker. Modal dialog: close FIRST so the main window
    // regains focus, then raise the app-global request MainWindow handles.
    private void OnLocateClicked(object? sender, RoutedEventArgs e)
    {
        if (_grid.SelectedItem is not PropertyXrefMatch x || string.IsNullOrEmpty(x.OwnerClassName)) return;
        var cls = x.OwnerClassName;
        Close();
        LocateClassInGWorldRequested?.Invoke(cls);
    }
}
