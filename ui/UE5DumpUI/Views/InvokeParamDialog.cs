using System.Buffers.Binary;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.Views;

/// <summary>
/// Two entry-points for this dialog (each opened by a different LiveWalker
/// button on a UFunction row):
/// <list type="bullet">
/// <item><see cref="PipeInvoke"/> -- shows FIRE + Copy AA Script + Cancel.
///     User can either test the call live in-app via the pipe, or bake the
///     current form values into a redistributable AA Script.</item>
/// <item><see cref="CopyBakedScript"/> -- shows Copy AA Script + Cancel
///     only. FIRE is hidden so a user opening the dialog from the
///     "Copy AA Script (Baked)" button can't accidentally invoke the
///     function on their live game.</item>
/// </list>
/// </summary>
public enum InvokeDialogMode
{
    /// <summary>FIRE + Copy AA Script + Cancel. Default.</summary>
    PipeInvoke,
    /// <summary>Copy AA Script + Cancel. FIRE button hidden.</summary>
    CopyBakedScript,
}

/// <summary>
/// Modal dialog for UFunction invocation. Either fires the call live via
/// the pipe (FIRE button) or bakes the current form values into a
/// redistributable AA Script (Copy AA Script button), depending on the
/// <see cref="InvokeDialogMode"/> the dialog was opened in.
///
/// FIRE executes ProcessEvent via pipe and displays decoded results inline.
/// The dialog stays open after invocation so the user can read return values.
/// Copy AA Script generates a script via <see cref="BakedScriptGenerator"/>,
/// pushes it to AOBMaker (or clipboard fallback), and closes.
///
/// Returns: "ok" if any action completed, null if cancelled.
/// </summary>
public sealed class InvokeParamDialog : Window
{
    private readonly List<TextBox> _edits = new();
    private readonly IReadOnlyList<FunctionParamModel> _inputParams;
    private readonly IReadOnlyList<FunctionParamModel> _allParams;
    private readonly int _parmsSize;
    private readonly string _className;
    private readonly string _funcName;
    private readonly string _instanceAddr;
    private readonly IDumpService _dump;
    private readonly IAobMakerBridge? _aobMaker;
    private readonly IPlatformService? _platform;
    private readonly int _ueVersion;
    private readonly InvokeDialogMode _mode;

    // Struct expansion: param index → list of (sub-field, TextBox) pairs
    // Uses DynamicStructField as unified type for both known and DLL-discovered layouts.
    private readonly Dictionary<int, List<(DynamicStructField sf, TextBox edit)>> _structEdits = new();

    private TextBlock _resultLabel = null!;
    private DataGrid _structuredReturnGrid = null!;
    private TextBlock _structuredReturnHeader = null!;
    private Button _btnFire = null!;
    private Button _btnCopyBaked = null!;
    private Button _btnClose = null!;
    private CheckBox _chkVerifyReturn = null!;
    private int _fireCount;

    /// <summary>
    /// The return param of the current invocation (or null when the
    /// function is void / there's no CPF_ReturnParm flag set). Pre-
    /// resolved at construction time so the FIRE handler can quickly
    /// gate the structured-return panel without re-scanning
    /// <see cref="_allParams"/> on every fire.
    /// </summary>
    private readonly FunctionParamModel? _returnParam;

    public InvokeParamDialog(
        string className, string funcName,
        IReadOnlyList<FunctionParamModel> inputParams,
        IReadOnlyList<FunctionParamModel> allParams,
        int parmsSize,
        string instanceAddr,
        IDumpService dump,
        int ueVersion = 0,
        IAobMakerBridge? aobMaker = null,
        IPlatformService? platform = null,
        InvokeDialogMode mode = InvokeDialogMode.PipeInvoke)
    {
        _inputParams = inputParams;
        _allParams = allParams;
        _parmsSize = parmsSize;
        _className = className;
        _funcName = funcName;
        _instanceAddr = instanceAddr;
        _dump = dump;
        _aobMaker = aobMaker;
        _platform = platform;
        _ueVersion = ueVersion;
        _mode = mode;

        // Pre-resolve the return param so the structured-return panel
        // doesn't have to re-scan _allParams on each FIRE. UE marks
        // exactly one param with CPF_ReturnParm; if more than one
        // claim it (rare; cooker bug), the first wins.
        FunctionParamModel? rp = null;
        foreach (var p in _allParams)
        {
            if (p.IsReturn) { rp = p; break; }
        }
        _returnParam = rp;

        Title = $"Invoke: {className}::{funcName}";
        Width = 560;
        MinWidth = 420;
        // Default starting Height grows up to a sensible cap; on a small
        // screen Avalonia clamps to the work area for us. The hard
        // MaxHeight=700 cap from the previous version was the actual
        // overflow bug -- on a 4K monitor a 12-param function still
        // showed a tiny scrollable strip when there was room for the
        // whole form. Bumping to 1100 lets the user see all params on
        // typical 1080p+ displays without manual resize, while
        // CanResize=true still lets them shrink it.
        Height = 480;
        MaxHeight = 1100;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        SizeToContent = SizeToContent.Height;  // grow to fit form, then cap at MaxHeight
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));

        var root = new DockPanel { Margin = new Thickness(16) };

        // Header (top, fixed)
        var header = new TextBlock
        {
            Text = $"{className}::{funcName}  (ParmsSize={parmsSize})",
            Foreground = new SolidColorBrush(Color.Parse("#DCDCAA")),
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // Bottom panel: buttons + result (fixed at bottom)
        var bottomPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Spacing = 8,
        };

        _btnFire = new Button
        {
            Content = "FIRE",
            Width = 100,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
            Background = new SolidColorBrush(Color.Parse("#4E7A25")),
            // CopyBakedScript mode: hide FIRE so the user can't accidentally
            // invoke the function on their live game when they only meant to
            // export an AA Script.
            IsVisible = _mode == InvokeDialogMode.PipeInvoke,
        };
        _btnFire.Click += OnFireClicked;

        // New: Copy AA Script (Baked) -- bakes current form values into a
        // self-contained AA Script via BakedScriptGenerator and hands it
        // to AOBMaker (if connected) or the clipboard. Available in BOTH
        // modes because the user filling the form may decide to either
        // FIRE-then-export or export-without-firing.
        _btnCopyBaked = new Button
        {
            Content = "Copy AA Script",
            Width = 130,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
            Background = new SolidColorBrush(Color.Parse("#3F6FA8")),
        };
        Avalonia.Controls.ToolTip.SetTip(_btnCopyBaked,
            "Bake current values into a CE AA Script + copy to clipboard / AOBMaker. " +
            "Requires ue5_invoke_helper.lua to be embedded in your CE table " +
            "(Table -> Add File...).");
        _btnCopyBaked.Click += OnCopyBakedScriptClicked;

        _btnClose = new Button
        {
            Content = "Close",
            Width = 80,
            // CopyBakedScript mode: no FIRE means no result to read; the
            // user finishes via Copy or Cancel, so Close adds noise.
            IsVisible = _mode == InvokeDialogMode.PipeInvoke,
        };
        _btnClose.Click += (_, _) => Close("ok");

        var btnCancel = new Button
        {
            Content = "Cancel",
            Width = 80,
        };
        btnCancel.Click += (_, _) => Close(null);

        btnPanel.Children.Add(_btnFire);
        btnPanel.Children.Add(_btnCopyBaked);
        btnPanel.Children.Add(_btnClose);
        btnPanel.Children.Add(btnCancel);
        bottomPanel.Children.Add(btnPanel);

        // Verify return value: opt-in toggle that switches the generated AA
        // Script into a diagnostic mode -- emits a Before/After raw-byte
        // dump of the params buffer plus a decoded print of the return slot,
        // and skips the auto-close-engine timer so the user can read both.
        // Default OFF so the production "ship a one-shot cheat" flow stays
        // silent on success. Visible only for the baked-script path; FIRE
        // already shows decoded values inline in PipeInvoke mode.
        _chkVerifyReturn = new CheckBox
        {
            Content = "Verify return value (print result, keep engine open)",
            IsChecked = false,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
        };
        Avalonia.Controls.ToolTip.SetTip(_chkVerifyReturn,
            "When checked, the generated AA Script prints the params buffer " +
            "(before/after) and decodes the return value, then leaves the " +
            "Lua engine open so you can read it. Use to debug 'function ran " +
            "but return is 0' situations. Off = silent on success, auto-close.");
        bottomPanel.Children.Add(_chkVerifyReturn);

        // Result area
        _resultLabel = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#4EC9B0")),
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            IsVisible = false,
        };
        bottomPanel.Children.Add(_resultLabel);

        // Structured-return property grid (pick #5, build 772+). Visible
        // only when the function's return param is a StructProperty
        // whose layout we can resolve (KnownStructLayouts hit OR DLL-
        // supplied dynamic StructFields). Three columns mirror the
        // dialog's `Name (Type, off) = value` text decode so the grid
        // and the result-label decode never disagree.
        _structuredReturnHeader = new TextBlock
        {
            Text = "Return value (decoded):",
            Foreground = new SolidColorBrush(Color.Parse("#AAB8D0")),
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 2),
            IsVisible = false,
        };
        bottomPanel.Children.Add(_structuredReturnHeader);

        _structuredReturnGrid = new DataGrid
        {
            IsReadOnly = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            FontSize = 11,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            // Bounded height so the grid never dominates the dialog
            // — the dialog already shows the single-line decode in
            // _resultLabel, this panel is supplementary.
            MaxHeight = 220,
            IsVisible = false,
        };
        // Columns are DataGridTemplateColumn with FuncDataTemplate so
        // the per-cell value is read via a typed lambda — no reflection,
        // no string-path Binding. The latter would trigger IL2026 +
        // IL3050 trim/AOT warnings (Avalonia's Binding(String) ctor
        // uses dynamic dispatch), and the project's CLAUDE.md mandates
        // Native AOT compatibility. Each FuncDataTemplate is invoked
        // once per row at materialization, which is fine for our
        // workflow (the grid's ItemsSource is replaced wholesale on
        // each successful FIRE — see UpdateStructuredReturnGrid).
        AddStructuredReturnColumn("Field",  140,
            row => row.Name);
        AddStructuredReturnColumn("Type",   140,
            row => row.Type);
        AddStructuredReturnColumn("Value",  220,
            row => row.Value);
        AddStructuredReturnColumn("Offset",  80,
            row => $"0x{row.Offset:X}");
        bottomPanel.Children.Add(_structuredReturnGrid);

        DockPanel.SetDock(bottomPanel, Dock.Bottom);
        root.Children.Add(bottomPanel);

        // Scrollable param fields (fills remaining space)
        var paramPanel = new StackPanel();

        if (inputParams.Count == 0)
        {
            paramPanel.Children.Add(new TextBlock
            {
                Text = "(no input parameters -- will invoke directly)",
                Foreground = new SolidColorBrush(Color.Parse("#808080")),
                FontSize = 12,
                Margin = new Thickness(0, 4),
            });
        }
        else
        {
            for (int i = 0; i < inputParams.Count; i++)
            {
                var p = inputParams[i];

                // Check if this is a known struct that we can expand
                var structLayout = ResolveTrustedLayout(p.TypeName, p.StructName, p.Size, _ueVersion);

                // Build a unified sub-field list from known layout or DLL-discovered fields
                IReadOnlyList<DynamicStructField>? expandFields = null;
                string expandSource;
                if (structLayout != null)
                {
                    // Known engine struct (version-aware LWC)
                    expandFields = structLayout.Fields
                        .Select(sf => new DynamicStructField(sf.Name, sf.TypeName, sf.Offset, sf.Size))
                        .ToList();
                    expandSource = "known";
                }
                else if (p.TypeName == "StructProperty" && p.StructFields.Count > 0)
                {
                    // Phase B: DLL-discovered dynamic struct layout
                    expandFields = p.StructFields;
                    expandSource = "dynamic";
                }
                else
                {
                    expandFields = null;
                    expandSource = "";
                }

                if (expandFields != null && expandFields.Count > 0)
                {
                    // Expand struct into sub-fields (works for both known and dynamic)
                    var structName = !string.IsNullOrEmpty(p.StructName) ? p.StructName : "struct";
                    var sourceTag = expandSource == "dynamic" ? " ⚡" : "";
                    var structLabel = $"{p.Name}  [F{structName}{sourceTag}, {p.Size}B, off={p.Offset}{(p.IsOut ? ", out" : "")}]";
                    paramPanel.Children.Add(new TextBlock
                    {
                        Text = structLabel,
                        Foreground = new SolidColorBrush(Color.Parse("#569CD6")),
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 4, 0, 0),
                    });

                    // Placeholder in _edits (not used for structs — _structEdits handles them)
                    _edits.Add(null!);

                    var subEdits = new List<(DynamicStructField, TextBox)>();
                    foreach (var sf in expandFields)
                    {
                        var sfShortType = ParamBufferBuilder.ShortTypeName(sf.TypeName);
                        var sfLabel = $"  .{sf.Name}  [{sfShortType}]";

                        var row = new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("Auto,8,*"),
                            Margin = new Thickness(12, 1),
                        };

                        var lbl = new TextBlock
                        {
                            Text = sfLabel,
                            Foreground = new SolidColorBrush(Color.Parse("#9CDCFE")),
                            FontSize = 12,
                            VerticalAlignment = VerticalAlignment.Center,
                            MinWidth = 260,
                        };
                        Grid.SetColumn(lbl, 0);
                        row.Children.Add(lbl);

                        var edt = new TextBox
                        {
                            Text = ParamBufferBuilder.GetDefaultValue(sf.TypeName),
                            MinWidth = 120,
                            FontSize = 12,
                            Padding = new Thickness(4, 2),
                        };
                        Grid.SetColumn(edt, 2);
                        row.Children.Add(edt);
                        subEdits.Add((sf, edt));

                        paramPanel.Children.Add(row);
                    }

                    _structEdits[i] = subEdits;
                }
                else
                {
                    // Normal scalar param
                    var shortType = ParamBufferBuilder.ShortTypeName(p.TypeName);
                    var structSuffix = (p.TypeName == "StructProperty" && !string.IsNullOrEmpty(p.StructName))
                        ? $" ({p.StructName})"
                        : "";
                    // Stage 1: surface the expected UClass for pointer-flavoured
                    // params so the user knows what kind of object the param wants
                    // (e.g. "UObject*: AActor" instead of bare "UObject*"). Empty
                    // ObjectClassName falls through to the original label.
                    var objClassSuffix = !string.IsNullOrEmpty(p.ObjectClassName)
                        ? $": {p.ObjectClassName}"
                        : "";
                    var label = $"{p.Name}  [{shortType}{objClassSuffix}{structSuffix}, {p.Size}B, off={p.Offset}{(p.IsOut ? ", out" : "")}]";

                    // Stage 2: pointer-flavoured params (UObject*/UClass*/Soft*/
                    // Weak*/Lazy*/Interface) get three extra buttons after the
                    // textbox — [Pick…] [null] [self]. Non-pointer params keep
                    // the original two-column layout.
                    bool isPointer = ParamBufferBuilder.IsPickablePointerType(p.TypeName);
                    var row = new Grid
                    {
                        ColumnDefinitions = isPointer
                            ? new ColumnDefinitions("Auto,8,*,4,Auto,4,Auto,4,Auto")
                            : new ColumnDefinitions("Auto,8,*"),
                        Margin = new Thickness(0, 2),
                    };

                    var lbl = new TextBlock
                    {
                        Text = label,
                        Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        MinWidth = 280,
                    };
                    Grid.SetColumn(lbl, 0);
                    row.Children.Add(lbl);

                    var edt = new TextBox
                    {
                        Text = ParamBufferBuilder.GetDefaultValue(p.TypeName),
                        MinWidth = 120,
                        FontSize = 12,
                        Padding = new Thickness(4, 2),
                    };
                    Grid.SetColumn(edt, 2);
                    row.Children.Add(edt);
                    _edits.Add(edt);

                    if (isPointer)
                    {
                        // [Pick…] — opens the live-instance picker pre-filtered
                        // to the param's expected UClass (from Stage 1). Greyed
                        // out when ObjectClassName is empty because the picker
                        // has nothing to pre-seed; users can still use the
                        // textbox directly or go via the full InstanceFinder
                        // panel.
                        var btnPick = new Button
                        {
                            Content = "Pick…",
                            Padding = new Thickness(8, 2),
                            FontSize = 11,
                            IsEnabled = !string.IsNullOrEmpty(p.ObjectClassName),
                            Tag = (p.ObjectClassName, edt),
                        };
                        btnPick.Click += OnPickPointerClicked;
                        Grid.SetColumn(btnPick, 4);
                        row.Children.Add(btnPick);

                        // [null] — quick 0x0 fill for optional pointer params
                        // (WorldContextObject, default-null Inputs, etc.).
                        var btnNull = new Button
                        {
                            Content = "null",
                            Padding = new Thickness(8, 2),
                            FontSize = 11,
                            Tag = edt,
                        };
                        btnNull.Click += OnNullPointerClicked;
                        Grid.SetColumn(btnNull, 6);
                        row.Children.Add(btnNull);

                        // [self] — fill with the invoke target's own address.
                        // Useful for functions that take their owning UObject
                        // as a param (rare but happens for utility functions
                        // that re-target themselves). Disabled when there's
                        // no target instance (definition-only views).
                        var btnSelf = new Button
                        {
                            Content = "self",
                            Padding = new Thickness(8, 2),
                            FontSize = 11,
                            IsEnabled = !string.IsNullOrEmpty(_instanceAddr),
                            Tag = edt,
                        };
                        btnSelf.Click += OnSelfPointerClicked;
                        Grid.SetColumn(btnSelf, 8);
                        row.Children.Add(btnSelf);
                    }

                    paramPanel.Children.Add(row);
                }
            }
        }

        var scroll = new ScrollViewer
        {
            Content = paramPanel,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            // Floor on the scrollable area: when the FIRE result panel
            // grows after a successful invoke (decoded post-call buffer
            // listing), DockPanel would otherwise let the bottom panel
            // squish the scroll area down to a sliver. 200px keeps at
            // least ~6 param rows visible regardless of result-area
            // expansion.
            MinHeight = 200,
        };

        root.Children.Add(scroll);
        Content = root;
    }

    // Stage 2: pointer-param helper buttons. Each handler is light enough to
    // inline; centralising them here keeps the row-construction code at
    // declaration site readable.

    private async void OnPickPointerClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not (string expectedClass, TextBox edt)) return;

        // async void: an unguarded throw here (e.g. ShowDialog with a closing
        // owner) would escape to the dispatcher and crash the app, matching the
        // try/catch the sibling handlers (OnFireClicked/OnCopyBakedScriptClicked) use.
        try
        {
            var picker = new ObjectInstancePickerDialog(expectedClass, _dump);
            var picked = await picker.ShowDialog<string?>(this);
            if (!string.IsNullOrEmpty(picked))
                edt.Text = picked;
        }
        catch (Exception ex)
        {
            _resultLabel.Text = $"Pick failed: {ex.Message}";
        }
    }

    private void OnNullPointerClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TextBox edt)
            edt.Text = "0x0";
    }

    private void OnSelfPointerClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TextBox edt && !string.IsNullOrEmpty(_instanceAddr))
            edt.Text = _instanceAddr;
    }

    private async void OnFireClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _btnFire.IsEnabled = false;
        _btnFire.Content = "FIRING...";
        _resultLabel.IsVisible = true;
        _resultLabel.Foreground = new SolidColorBrush(Color.Parse("#808080"));
        _resultLabel.Text = "Invoking ProcessEvent...";
        // Clear any prior structured-return grid so the user isn't
        // looking at stale rows from the previous fire while the new
        // call is in-flight. Re-populated on the success path below.
        _structuredReturnGrid.ItemsSource  = null;
        _structuredReturnGrid.IsVisible    = false;
        _structuredReturnHeader.IsVisible  = false;
        _fireCount++;

        try
        {
            // Build param hex from input fields (struct-aware). String params are
            // NOT written here — an FString's Data pointer must live in the game
            // process, so the DLL builds them from these descriptors and patches
            // the (zeroed) 16-byte slots.
            string? paramsHex = null;
            var stringParams = new List<InvokeStringParam>();
            if (_inputParams.Count > 0 && _parmsSize > 0)
            {
                var buf = new byte[_parmsSize];
                for (int i = 0; i < _inputParams.Count; i++)
                {
                    var param = _inputParams[i];
                    if (param.Offset < 0 || param.Offset >= _parmsSize) continue;

                    if (_structEdits.TryGetValue(i, out var subEdits))
                    {
                        // Struct param: write each sub-field (DynamicStructField overload)
                        var subFields = subEdits.Select(se => se.sf).ToArray();
                        var subValues = subEdits.Select(se => se.edit.Text ?? "0").ToArray();
                        ParamBufferBuilder.WriteStructParam(buf, param.Offset,
                            (IReadOnlyList<DynamicStructField>)subFields, subValues);
                    }
                    else if (ParamBufferBuilder.IsStringType(param.TypeName))
                    {
                        // String param. Only build INPUT strings: an OUT FString
                        // (the callee fills it) must stay a zeroed/empty FString,
                        // else the callee's reassignment would free our non-FMemory
                        // Data buffer and crash. Leaving the 16-byte slot zeroed is
                        // the correct empty FString for an out param.
                        if (!param.IsOut)
                        {
                            stringParams.Add(new InvokeStringParam(
                                param.Offset,
                                ParamBufferBuilder.IsWideString(param.TypeName),
                                _edits[i]?.Text ?? ""));
                        }
                    }
                    else
                    {
                        // Scalar param
                        var text = (_edits[i]?.Text ?? "0").Trim();
                        // Range-check BEFORE writing. Every integer write in
                        // ParamBufferBuilder masks to the field width, so an
                        // out-of-range value used to reach the game truncated with
                        // nothing said — 9999 into a 1-byte param fired 15. Refusing
                        // the whole invoke is right here: a UFunction called with one
                        // silently-wrong argument is worse than one not called.
                        // (audit #5, the width family on the FIRE path.)
                        if (!ParamBufferBuilder.TryValidateScalar(
                                param.TypeName, param.Size, text, out var rangeErr))
                        {
                            _resultLabel.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
                            _resultLabel.Text = $"ERROR: {param.Name}: {rangeErr}";
                            return;
                        }
                        ParamBufferBuilder.WriteParam(buf, param.Offset, param.TypeName, param.Size, text);
                    }
                }
                paramsHex = Convert.ToHexString(buf);
            }

            // Execute via pipe
            var result = await _dump.InvokeFunctionAsync(
                _funcName,
                instanceAddr: _instanceAddr,
                parmsSize: _parmsSize,
                paramsHex: paramsHex,
                stringParams: stringParams.Count > 0 ? stringParams : null);

            // Display results
            ShowResult(result);
        }
        catch (Exception ex)
        {
            _resultLabel.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
            _resultLabel.Text = $"ERROR: {ex.Message}";
        }
        finally
        {
            _btnFire.IsEnabled = true;
            _btnFire.Content = $"FIRE ({_fireCount})";
        }
    }

    private void ShowResult(InvokeFunctionResult result)
    {
        _resultLabel.IsVisible = true;

        var lines = new List<string>();

        if (result.Success)
        {
            lines.Add($"[#{_fireCount}] ProcessEvent OK  (result={result.Result})");
        }
        else
        {
            var errorDetail = !string.IsNullOrEmpty(result.Error)
                ? result.Error
                : $"error code {result.Result}";
            lines.Add($"[#{_fireCount}] INVOKE FAILED: {errorDetail}");
            _resultLabel.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
            _resultLabel.Text = string.Join("\n", lines);
            return;
        }

        // Decode ALL param values from the post-call buffer
        // (shows return values, out params, and even input params after the call)
        if (!string.IsNullOrEmpty(result.ResultHex) && _parmsSize > 0)
        {
            try
            {
                var bytes = HexToBytes(result.ResultHex);
                lines.Add("--- Post-call buffer ---");

                foreach (var p in _allParams)
                {
                    // Use struct-aware decoding for known structs, then dynamic, then scalar
                    string decoded;
                    // Trusted form: the same size cross-check the INPUT boxes have
                    // used since Y7. Decoding the post-call buffer on a
                    // size-contradicted layout renames and re-offsets every value
                    // the user reads back (audit #5 AC2).
                    var structLayout = ResolveTrustedLayout(
                        p.TypeName, p.StructName, p.Size, _ueVersion);
                    if (structLayout != null)
                        decoded = DecodeStructParamValue(bytes, p, structLayout);
                    else if (p.TypeName == "StructProperty" && p.StructFields.Count > 0)
                        decoded = DecodeDynamicStructParamValue(bytes, p);
                    else
                        decoded = DecodeParamValue(bytes, p);

                    var tag = p.IsReturn ? " (return)"
                            : p.IsOut ? " (out)"
                            : "";
                    // Highlight return/out params, or detect by name convention
                    var isReturnByName = p.Name.Contains("ReturnValue", StringComparison.OrdinalIgnoreCase);
                    if (isReturnByName && !p.IsReturn)
                        tag = " (return*)";  // * = detected by name, not flag

                    lines.Add($"  {p.Name}{tag} = {decoded}");
                }

                // Also show raw hex (truncated)
                var rawHex = result.ResultHex.Length > 64
                    ? result.ResultHex[..64] + "..."
                    : result.ResultHex;
                lines.Add($"  raw: {rawHex}");
            }
            catch
            {
                lines.Add($"  result_hex: {result.ResultHex}");
            }
        }

        _resultLabel.Foreground = new SolidColorBrush(Color.Parse("#4EC9B0"));
        _resultLabel.Text = string.Join("\n", lines);

        // Pick #5: structured return-value DataGrid. Decode the return
        // param's struct sub-fields (when present) and surface them as
        // a small property grid below the text decode. Hidden when the
        // return is non-struct or no layout is resolvable; the existing
        // text decode in _resultLabel remains the primary signal.
        UpdateStructuredReturnGrid(result);
    }

    /// <summary>
    /// Adds one DataGridTemplateColumn to the structured-return grid,
    /// reading each cell's text via the supplied
    /// <paramref name="textSelector"/> lambda. Keeps the call sites
    /// declarative and centralises the per-cell TextBlock styling
    /// (padding / vertical alignment / monospace font inherited from
    /// the DataGrid). AOT-safe — the FuncDataTemplate is materialised
    /// once per row from a strongly-typed delegate, so no reflection
    /// fires at trim/AOT analysis time.
    /// </summary>
    private void AddStructuredReturnColumn(
        string header, double width, Func<StructFieldValue, string> textSelector)
    {
        _structuredReturnGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = header,
            Width  = new DataGridLength(width),
            CellTemplate = new FuncDataTemplate<StructFieldValue>(
                (row, _) => new TextBlock
                {
                    Text                = row is null ? "" : textSelector(row),
                    Margin              = new Thickness(6, 2),
                    VerticalAlignment   = VerticalAlignment.Center,
                    FontFamily          = new FontFamily("Consolas, Courier New, monospace"),
                    FontSize            = 11,
                },
                supportsRecycling: false),
        });
    }

    /// <summary>
    /// Populate (or hide) the structured-return DataGrid based on the
    /// post-FIRE result. Pure helper — extracted so the FIRE handler
    /// stays linear + the decode logic gets independently testable via
    /// <see cref="StructReturnDecoder.Decode"/>.
    /// </summary>
    private void UpdateStructuredReturnGrid(InvokeFunctionResult result)
    {
        // Default: hide everything. Each early return below leaves the
        // grid in this state so a string of subsequent invokes against
        // void / non-struct returns doesn't flash stale rows.
        _structuredReturnGrid.ItemsSource = null;
        _structuredReturnGrid.IsVisible   = false;
        _structuredReturnHeader.IsVisible = false;

        if (_returnParam is null) return;
        if (!result.Success) return;
        if (string.IsNullOrEmpty(result.ResultHex)) return;
        if (!StructReturnDecoder.CanDecode(_returnParam, _ueVersion)) return;

        byte[] bytes;
        try { bytes = HexToBytes(result.ResultHex); }
        catch { return; }

        var rows = StructReturnDecoder.Decode(bytes, _returnParam, _ueVersion);
        if (rows.Count == 0) return;

        _structuredReturnGrid.ItemsSource = rows;
        _structuredReturnGrid.IsVisible   = true;
        _structuredReturnHeader.IsVisible = true;
        // Header label includes the struct type so the user sees the
        // "what" at a glance — useful when chaining many invokes that
        // return different structs.
        string structLabel = string.IsNullOrEmpty(_returnParam.StructName)
            ? "Return value (decoded):"
            : $"Return value (decoded — {_returnParam.StructName}):";
        _structuredReturnHeader.Text = structLabel;
    }

    /// <summary>
    /// "Copy AA Script (Baked)" handler: snapshot the current form values,
    /// flatten any expanded structs into individual scalar entries (each
    /// at parent_offset + sub_offset), generate the AA Script, and ship
    /// it via AOBMaker / clipboard.
    /// </summary>
    private async void OnCopyBakedScriptClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _btnCopyBaked.IsEnabled = false;
        _resultLabel.IsVisible = true;
        _resultLabel.Foreground = new SolidColorBrush(Color.Parse("#808080"));
        _resultLabel.Text = "Generating AA Script...";

        try
        {
            var bakedValues = CollectBakedValues();
            // Pull the return-value param (if any) from _allParams. The
            // generator's verify mode uses Offset + UeTypeName to emit
            // a typed readUFunctionReturn call. Skipped for void-return
            // functions; verify mode then just prints "(void return)".
            var returnParam = BuildReturnBakedParam();
            var verify = _chkVerifyReturn.IsChecked == true;
            var script = BakedScriptGenerator.Generate(
                _className, _funcName, _parmsSize, bakedValues,
                returnParam: returnParam, verifyReturn: verify);

            // Prefer AOBMaker (creates the AA Script entry directly in CE);
            // fall back to clipboard for users running without the plugin.
            // Sample IsAvailable BEFORE the send so we can distinguish
            // 'pipe broke mid-send' (was available, now isn't) from
            // 'never configured' (was already false) in the result message.
            var description = $"Invoke (baked): {_className}::{_funcName}";
            bool wasAvailable = _aobMaker?.IsAvailable ?? false;
            bool sentToCe = false;
            if (_aobMaker != null && wasAvailable)
            {
                sentToCe = await _aobMaker.CreateAAScriptAsync(
                    description, script, autoActivate: false);
            }

            // Params that could not be rendered as a Lua literal were baked as 0 with an
            // --[[unparsed:…]] marker. Saying "N baked param(s)" over that reported a
            // clean export of a script that will call the game with the wrong arguments
            // (audit #5 Y14), so name them instead — computed from the SAME renderer the
            // generator used, not from a second parse that could disagree.
            var unparsed = bakedValues
                .Where(v => BakedScriptGenerator.IsUnparsedLiteral(v.UeTypeName, v.LiteralText))
                .Select(v => v.ParamName)
                .ToList();
            string paramNote = unparsed.Count == 0
                ? $"{bakedValues.Count} baked param(s)"
                : $"{bakedValues.Count} baked param(s) — ⚠ {unparsed.Count} could NOT be " +
                  $"parsed and {(unparsed.Count == 1 ? "was" : "were")} baked as 0: " +
                  string.Join(", ", unparsed);

            if (sentToCe)
            {
                _resultLabel.Foreground = new SolidColorBrush(
                    Color.Parse(unparsed.Count == 0 ? "#4EC9B0" : "#E0A050"));
                _resultLabel.Text = $"AA Script created in CE: {description}\n" +
                    $"({paramNote}; helper file required in your .CT)";
            }
            else if (_platform != null)
            {
                // Wrap as paste-able CE memory-record XML. A bare [ENABLE]/[DISABLE] AA
                // body cannot be pasted into CE's address list on its own — it has to be a
                // CheatEntry with VariableType = Auto Assembler Script. Every other
                // clipboard fallback in the app went through WrapAaScriptXml at build
                // 1986, including the no-arg Console sibling; this one was missed
                // (audit #5 Y12).
                var xml = Services.CheatTableBuilder.WrapAaScriptXml(description, script);
                bool copied = await _platform.CopyToClipboardAsync(xml);
                if (!copied)
                {
                    // CopyToClipboardAsync never throws — it reports failure. Claiming a
                    // successful copy over a clipboard that does not hold the script is
                    // the same defect as Y14 one branch up.
                    _resultLabel.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
                    _resultLabel.Text = "ERROR: could not write to the clipboard — the AA " +
                        "Script was NOT delivered. Another application may be holding the " +
                        "clipboard open; try again.";
                    return;
                }
                if (wasAvailable)
                {
                    // Pipe was up at start; send failed. CE likely closed
                    // between availability check and send.
                    _resultLabel.Foreground = new SolidColorBrush(Color.Parse("#E0A050"));
                    _resultLabel.Text =
                        $"⚠ AOBMaker pipe broke mid-send (CE closed?).\n" +
                        $"AA Script copied as CE XML ({xml.Length:N0} chars) as a fallback " +
                        $"({paramNote}).\nPaste into CE's address list once CE is ready.";
                }
                else
                {
                    _resultLabel.Foreground = new SolidColorBrush(
                        Color.Parse(unparsed.Count == 0 ? "#4EC9B0" : "#E0A050"));
                    _resultLabel.Text = $"AOBMaker not connected.\nAA Script copied as CE XML " +
                        $"({xml.Length:N0} chars; {paramNote}) — paste into CE's address list." +
                        $"\nDon't forget to embed ue5_invoke_helper.lua in your .CT " +
                        $"(Table -> Add File...).";
                }
            }
            else
            {
                _resultLabel.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
                _resultLabel.Text = "ERROR: No clipboard service and AOBMaker " +
                    "unavailable -- cannot deliver script.";
                return;
            }

            // CopyBakedScript mode only: auto-close after a short delay so
            // the user reads the success message but doesn't have to click.
            // PipeInvoke mode keeps the dialog open so the user can also FIRE.
            if (_mode == InvokeDialogMode.CopyBakedScript)
            {
                await Task.Delay(800);
                Close("ok");
            }
        }
        catch (Exception ex)
        {
            _resultLabel.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
            _resultLabel.Text = $"ERROR: {ex.Message}";
        }
        finally
        {
            _btnCopyBaked.IsEnabled = true;
        }
    }

    /// <summary>
    /// Locate the return-value parameter in <see cref="_allParams"/> and
    /// wrap it as a <see cref="BakedParamValue"/> for the generator's
    /// verify-mode emit. Returns <c>null</c> for void-return functions
    /// (the generator then prints just "(void return)" on success).
    /// LiteralText is unused for the return slot (helper reads, not writes)
    /// so we pass an empty string.
    /// </summary>
    internal BakedParamValue? BuildReturnBakedParam()
    {
        foreach (var p in _allParams)
        {
            if (!p.IsReturn) continue;
            if (p.Offset < 0 || p.Offset >= _parmsSize) continue;
            return new BakedParamValue(
                ParamName:   string.IsNullOrEmpty(p.Name) ? "ReturnValue" : p.Name,
                UeTypeName:  p.TypeName,
                Size:        p.Size,
                Offset:      p.Offset,
                LiteralText: "");
        }
        return null;
    }

    /// <summary>
    /// Snapshot the form into a flat list of <see cref="BakedParamValue"/>.
    /// Struct params are flattened to one entry per sub-field (offset =
    /// parent.Offset + subfield.Offset, name = "Parent.Sub"), so the
    /// generator + helper handle them as ordinary scalars.
    /// </summary>
    internal IReadOnlyList<BakedParamValue> CollectBakedValues()
    {
        var list = new List<BakedParamValue>(_inputParams.Count);
        for (int i = 0; i < _inputParams.Count; i++)
        {
            var p = _inputParams[i];
            if (p.Offset < 0 || p.Offset >= _parmsSize) continue;

            if (_structEdits.TryGetValue(i, out var subEdits))
            {
                // Struct param: emit one BakedParamValue per sub-field
                foreach (var (sf, edit) in subEdits)
                {
                    list.Add(new BakedParamValue(
                        ParamName:   $"{p.Name}.{sf.Name}",
                        UeTypeName:  sf.TypeName,
                        Size:        sf.Size,
                        Offset:      p.Offset + sf.Offset,
                        LiteralText: (edit.Text ?? "0").Trim()));
                }
            }
            else
            {
                // OUT string params must stay a zeroed/empty FString (the callee
                // fills them). Baking one would make the helper build an FString
                // the callee then FMemory::Free's -> crash. Skip so the slot
                // stays zeroed. An OUT FString must stay an empty struct.
                if (ParamBufferBuilder.IsStringType(p.TypeName) && p.IsOut)
                    continue;

                var text = (_edits[i]?.Text ?? "0").Trim();
                list.Add(new BakedParamValue(
                    ParamName:   p.Name,
                    UeTypeName:  p.TypeName,
                    Size:        p.Size,
                    Offset:      p.Offset,
                    LiteralText: text));
            }
        }
        return list;
    }

    /// <summary>Decode a single param value from the post-call buffer bytes.</summary>
    internal static string DecodeParamValue(byte[] buf, FunctionParamModel p)
    {
        if (p.Offset < 0 || p.Offset >= buf.Length) return "?";
        int available = buf.Length - p.Offset;
        var span = buf.AsSpan(p.Offset);

        return p.TypeName switch
        {
            "BoolProperty" => buf[p.Offset] != 0 ? "true" : "false",
            "ByteProperty" or "Int8Property" => buf[p.Offset].ToString(),
            "Int16Property" when available >= 2
                => BinaryPrimitives.ReadInt16LittleEndian(span).ToString(),
            "UInt16Property" when available >= 2
                => BinaryPrimitives.ReadUInt16LittleEndian(span).ToString(),
            "FloatProperty" when available >= 4
                => BinaryPrimitives.ReadSingleLittleEndian(span).ToString(CultureInfo.InvariantCulture),
            "DoubleProperty" when available >= 8
                => BinaryPrimitives.ReadDoubleLittleEndian(span).ToString(CultureInfo.InvariantCulture),
            "IntProperty" or "UInt32Property" when available >= 4
                => BinaryPrimitives.ReadInt32LittleEndian(span).ToString(),
            // EnumProperty is decoded at the width the ENGINE reported, never 4.
            // It used to be grouped with IntProperty above, so UE's dominant shape
            // — `enum class E : uint8` — was read as 4 bytes whenever 4 bytes of
            // buffer remained: the returned value was the enum byte plus three
            // bytes belonging to whatever followed it. The tell is that a 1-byte
            // enum at the very END of the buffer decoded correctly (the guard
            // failed and it fell through to the size switch) while the same enum
            // mid-buffer did not. This is the READ side of the mistake Y2 fixed on
            // the write side of this very file — found by the width-family grep.
            "EnumProperty" => DecodeBySize(buf, p.Offset, available, p.Size),
            "Int64Property" when available >= 8
                => BinaryPrimitives.ReadInt64LittleEndian(span).ToString(),
            "UInt64Property" or "ObjectProperty" or "ClassProperty"
                or "NameProperty" or "SoftObjectProperty" or "WeakObjectProperty"
                or "InterfaceProperty" when available >= 8
                => $"0x{BinaryPrimitives.ReadUInt64LittleEndian(span):X}",
            // Shared with EnumProperty above so the two cannot drift -- the mirror
            // of ParamBufferBuilder.WriteBySize on the write side.
            _ => DecodeBySize(buf, p.Offset, available, p.Size),
        };
    }

    /// <summary>
    /// Decode an integer param at whatever width the engine reported. Used by the
    /// size-driven fallback and by EnumProperty, whose width is 1/2/4/8 and is NOT
    /// implied by its type name (audit #5, the width family).
    /// </summary>
    private static string DecodeBySize(byte[] buf, int offset, int available, int size)
    {
        var span = buf.AsSpan(offset);
        return size switch
        {
            1 => buf[offset].ToString(),
            2 when available >= 2 => BinaryPrimitives.ReadInt16LittleEndian(span).ToString(),
            4 when available >= 4 => BinaryPrimitives.ReadInt32LittleEndian(span).ToString(),
            8 when available >= 8 => $"0x{BinaryPrimitives.ReadUInt64LittleEndian(span):X}",
            _ => BitConverter.ToString(buf, offset, Math.Min(Math.Max(size, 0), available)),
        };
    }

    /// <summary>Decode a struct param using DLL-discovered dynamic sub-fields. Returns "FieldA=val, FieldB=val" style.</summary>
    internal static string DecodeDynamicStructParamValue(byte[] buf, FunctionParamModel p)
    {
        if (p.StructFields.Count == 0) return DecodeParamValue(buf, p);

        var parts = new List<string>(p.StructFields.Count);
        foreach (var sf in p.StructFields)
        {
            var subParam = new FunctionParamModel
            {
                Name = sf.Name,
                TypeName = sf.TypeName,
                Size = sf.Size,
                Offset = p.Offset + sf.Offset,
            };
            var val = DecodeParamValue(buf, subParam);
            parts.Add($"{sf.Name}={val}");
        }
        return string.Join(", ", parts);
    }

    /// <summary>Decode a struct param using known sub-field layout. Returns "X=1.0, Y=2.0, Z=3.0" style.</summary>
    internal static string DecodeStructParamValue(byte[] buf, FunctionParamModel p,
        KnownStructLayouts.StructLayout layout)
    {
        var parts = new List<string>(layout.Fields.Count);
        foreach (var sf in layout.Fields)
        {
            var subParam = new FunctionParamModel
            {
                Name = sf.Name,
                TypeName = sf.TypeName,
                Size = sf.Size,
                Offset = p.Offset + sf.Offset,
            };
            var val = DecodeParamValue(buf, subParam);
            parts.Add($"{sf.Name}={val}");
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// The hardcoded layout for a struct param, but ONLY when it agrees with the engine-reported
    /// size — plus this view's "is it even a StructProperty" test.
    ///
    /// <para><b>The rule itself now lives on <see cref="KnownStructLayouts.GetTrustedLayout"/></b>,
    /// beside the table it guards. It was written here as a private helper (Y7), which meant the
    /// two consumers in <c>StructReturnDecoder</c> structurally could not reach it — a Service
    /// cannot depend on a View — so this dialog refused a size-contradicted layout for its INPUT
    /// boxes and accepted the same layout for the RESULT grid. That is audit #5 AC2. The name is
    /// kept so existing callers and tests do not churn; the behaviour is defined in one place.</para>
    /// </summary>
    internal static KnownStructLayouts.StructLayout? ResolveTrustedLayout(
        string typeName, string? structName, int engineSize, int ueVersion)
    {
        if (typeName != "StructProperty") return null;
        return KnownStructLayouts.GetTrustedLayout(structName, engineSize, ueVersion);
    }

    internal static byte[] HexToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber);
        }
        return bytes;
    }
}
