using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class InterestingPropertiesPanel : UserControl
{
    // AOT-safe sort comparers for the columns whose sort property isn't
    // rooted by a column-level Binding: the three template columns (Score /
    // Cat / Location) and the Offset text column (sorts by PropOffset while
    // it displays OffsetHex). Without these the header click is a silent
    // no-op under AOT (the sort trap explained in Helpers/DataGridSortComparers.cs).
    private static readonly IReadOnlyDictionary<string, IComparer> ResultsSortComparers =
        new Dictionary<string, IComparer>
        {
            ["FinalScore"]    = DataGridSortComparers.Number<ScoredPropertyRow>(r => r.FinalScore),
            ["CategoryLabel"] = DataGridSortComparers.Ordinal<ScoredPropertyRow>(r => r.CategoryLabel),
            ["UnusualBadge"]  = DataGridSortComparers.Ordinal<ScoredPropertyRow>(r => r.UnusualBadge),
            ["PropOffset"]    = DataGridSortComparers.Number<ScoredPropertyRow>(r => r.PropOffset),
        };

    public InterestingPropertiesPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("ResultsGrid")?.WireSortComparers(ResultsSortComparers);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Forward the DataGrid's current multi-select to the VM command.
    /// AOT-safe: we cast each SelectedItem explicitly to
    /// <see cref="ScoredPropertyRow"/> rather than going through any
    /// reflection-based path, and the command parameter is a strongly-
    /// typed <see cref="System.Collections.Generic.IList{T}"/> so
    /// CommunityToolkit's [RelayCommand] doesn't need dynamic dispatch
    /// to bind it.
    /// </summary>
    private void OnGenerateCtClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InterestingPropertiesViewModel vm) return;
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        if (grid?.SelectedItems is null) return;

        var rows = new List<ScoredPropertyRow>(grid.SelectedItems.Count);
        foreach (var item in grid.SelectedItems)
        {
            if (item is ScoredPropertyRow r) rows.Add(r);
        }
        vm.GenerateCheatTableCommand.Execute(rows);
    }

    /// <summary>
    /// Forward the grid's multi-select (empty = all rows) to the batch Find
    /// Funcs command, which fills each row's inline "Funcs" column.
    /// </summary>
    private void OnBatchFindFuncsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InterestingPropertiesViewModel vm) return;
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        var rows = new List<ScoredPropertyRow>();
        if (grid?.SelectedItems is { } sel)
        {
            foreach (var item in sel)
                if (item is ScoredPropertyRow r) rows.Add(r);
        }
        vm.BatchFindFuncsCommand.Execute(rows);
    }
}
