using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class LiveFuncsPanel : UserControl
{
    // AOT-safe sort comparers for every column whose sort path no column binding
    // roots — a template column (no column-level Binding at all) or a text column
    // whose SortMemberPath differs from its Binding path. Their reflection sort is
    // trimmed under AOT (the sort trap explained in Helpers/DataGridSortComparers.cs). Class binds and sorts on the
    // same path, so it is rooted and needs nothing. Function became a template column for the unloaded marker
    // ([TRACE-UNLOADED-NAMES]), so it needs one.
    //
    // ⚠ ROOTED IS NOT THE SAME AS CORRECT. This comment used to include Params in that
    // list, and it was right that Params was rooted — and that is exactly why it went
    // unfixed. The question the AF16-AF23 sweep asked was "is the header inert under
    // trimming?", so a column that sorted perfectly well by the WRONG key passed.
    private static readonly IReadOnlyDictionary<string, IComparer> ResultsSortComparers =
        new Dictionary<string, IComparer>
        {
            ["Count"] = DataGridSortComparers.Number<PeProfileEntry>(r => r.Count),
            ["FirstSeq"] = DataGridSortComparers.Number<PeProfileEntry>(r => r.FirstSeq),
            ["Delta"] = DataGridSortComparers.Number<PeProfileEntry>(r => r.Delta),
            ["Kind"]  = DataGridSortComparers.Ordinal<PeProfileEntry>(r => r.Kind),
            ["TypeLabel"] = DataGridSortComparers.Ordinal<PeProfileEntry>(r => r.TypeLabel),
            // Period (audit #5 AF19). The column renders PeriodLabel but sorts on
            // MeanPeriodMs, so no column binding roots the sort path and the header was
            // inert under trimming — on the one column the Phase E cadence feature exists
            // for ("which callback fires on a regular timer?").
            ["MeanPeriodMs"] = DataGridSortComparers.Double<PeProfileEntry>(r => r.MeanPeriodMs),
            // Params (PARAMSSORT-2026-08-22). The cell shows "{NumParms} ({ParmsSize}B)"
            // but the column now sorts on NumParms, so nothing roots the sort path and a
            // comparer is required. It previously sorted on ParamsLabel — which was AOT-safe
            // (binding and sort path agreed, so the property was rooted) and WRONG: the
            // ordinal order of the label puts "11 (72B)" above "2 (9B)". Measured on
            // DumperTest 2026-08-22: 3,142 functions, two with >=10 parameters, so the
            // inversion is reachable on a stock host. AF20 fixed the Live Walker twin only,
            // because the audit asked "is the header inert under trimming?" and these three
            // were not inert — just wrong.
            ["NumParms"] = DataGridSortComparers.Number<PeProfileEntry>(r => r.NumParms),
            ["FuncName"] = DataGridSortComparers.Ordinal<PeProfileEntry>(r => r.FuncName),
        };

    public LiveFuncsPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("ResultsGrid")?.WireSortComparers(ResultsSortComparers);
        this.AttachFilterView<LiveFuncsViewModel>(this.FindControl<DataGrid>("ResultsGrid"), vm => vm.ResultsView);
        DataContextChanged += (_, _) => WireTrace();
    }

    // [LIVEFUNCS-TIMELINE-2026-10-04] The call trace's view-side pieces: every question the view model asks needs a
    // window to ask in, and the trace's columns show only with the experimental tabs on (T6). A DataGrid column is not
    // in the visual tree, so its visibility cannot be bound to the view model; it follows TraceAvailable from here.
    private LiveFuncsViewModel? _wired;

    private void WireTrace()
    {
        if (_wired != null) _wired.PropertyChanged -= OnVmPropertyChanged;
        _wired = DataContext as LiveFuncsViewModel;
        if (_wired == null) return;
        _wired.ConfirmTraceAllCalls = () => ConfirmDialog.ShowAsync(
            Core.Res.Get("str.LF.Trace.Confirm.Title"), Core.Res.Get("str.LF.Trace.Confirm.Message"),
            Core.Res.Get("str.LF.Trace.Confirm.Run"), Core.Res.Get("str.LF.Trace.Confirm.Cancel"));
        // [LIVEFUNCS-STEP3] T9.2's question; the view model writes it, as it names the function and the budget.
        _wired.ConfirmStackPerFrame = question => ConfirmDialog.ShowAsync(
            Core.Res.Get("str.LF.Stack.PerFrame.Title"), question,
            Core.Res.Get("str.LF.Stack.PerFrame.Run"), Core.Res.Get("str.LF.Stack.PerFrame.Cancel"));
        _wired.PropertyChanged += OnVmPropertyChanged;
        ApplyTickColumnVisibility();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveFuncsViewModel.TraceAvailable)) ApplyTickColumnVisibility();
    }

    private void ApplyTickColumnVisibility()
    {
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        if (grid == null || _wired == null) return;
        // [LIVEFUNCS-STEP2] [LIVEFUNCS-STEP3] The choice columns with it: they ride on the experimental trace. Every
        // column the axaml hides must be compared here, or it stays hidden for good; a test reads both files for that.
        string header = Core.Res.Get("str.LF.Col.Trace"), snapHeader = Core.Res.Get("str.LF.Col.Snapshot"),
               stackHeader = Core.Res.Get("str.LF.Col.Stack");
        foreach (var col in grid.Columns)
            if (col.Header is string h && (h == header || h == snapHeader || h == stackHeader))
                col.IsVisible = _wired.TraceAvailable;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
