using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class InterestingFunctionsPanel : UserControl
{
    // AOT-safe sort comparers for the two template data columns (Score /
    // Cat), which have no column-level Binding, so their reflection sort is
    // trimmed under AOT (the sort trap explained in Helpers/DataGridSortComparers.cs),
    // and for Params below. The other text columns sort out-of-box.
    private static readonly IReadOnlyDictionary<string, IComparer> ResultsSortComparers =
        new Dictionary<string, IComparer>
        {
            ["FinalScore"]    = DataGridSortComparers.Number<ScoredFunctionRow>(r => r.FinalScore),
            ["CategoryLabel"] = DataGridSortComparers.Ordinal<ScoredFunctionRow>(r => r.CategoryLabel),
            // Params [PARAMSSORT-2026-08-22]. The cell shows "{NumParms} ({ParmsSize}B)"
            // but the column sorts on NumParms, so nothing roots the sort path and a
            // comparer is required. Sorting on ParamsLabel would be AOT-safe (binding and
            // sort path agree, so the property is rooted) and WRONG: the ordinal order of
            // the label puts "11 (72B)" above "2 (9B)".
            ["NumParms"] = DataGridSortComparers.Number<ScoredFunctionRow>(r => r.NumParms),
        };

    public InterestingFunctionsPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("ResultsGrid")?.WireSortComparers(ResultsSortComparers);
        this.AttachFilterView<InterestingFunctionsViewModel>(this.FindControl<DataGrid>("ResultsGrid"), vm => vm.ResultsView);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Forward the DataGrid's current multi-select to the VM command.
    /// Sister to <c>InterestingPropertiesPanel.OnGenerateCtClick</c>.
    /// Strongly-typed list keeps the CommunityToolkit [RelayCommand]
    /// binding AOT-friendly (no reflection-based dispatch).
    /// </summary>
    private void OnGenerateCtClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InterestingFunctionsViewModel vm) return;
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        if (grid?.SelectedItems is null) return;

        var rows = new List<ScoredFunctionRow>(grid.SelectedItems.Count);
        foreach (var item in grid.SelectedItems)
        {
            if (item is ScoredFunctionRow r) rows.Add(r);
        }
        vm.GenerateCheatTableCommand.Execute(rows);
    }

    /// <summary>
    /// Forward the grid's multi-select (empty = all rows) to the batch Props
    /// command, which fills each row's inline "Uses" column. Same selection
    /// forwarding as <see cref="OnGenerateCtClick"/>.
    /// </summary>
    private void OnBatchFindPropsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InterestingFunctionsViewModel vm) return;
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        var rows = new List<ScoredFunctionRow>();
        if (grid?.SelectedItems is { } sel)
        {
            foreach (var item in sel)
                if (item is ScoredFunctionRow r) rows.Add(r);
        }
        vm.BatchFindFuncPropsCommand.Execute(rows);
    }
}
