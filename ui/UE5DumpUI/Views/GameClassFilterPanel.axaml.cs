using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class GameClassFilterPanel : UserControl
{
    // AOT-safe sort comparers for the Score template column (no column-level
    // Binding) and the Size text column (sorts by PropertiesSize while it
    // displays SizeHex). Without these the header click is a silent no-op
    // under AOT (the sort trap explained in Helpers/DataGridSortComparers.cs). Other text columns sort out-of-box.
    private static readonly IReadOnlyDictionary<string, IComparer> ResultsSortComparers =
        new Dictionary<string, IComparer>
        {
            ["Score"]          = DataGridSortComparers.Number<GameClassEntry>(r => r.Score),
            ["PropertiesSize"] = DataGridSortComparers.Number<GameClassEntry>(r => r.PropertiesSize),
        };

    public GameClassFilterPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("ResultsGrid")?.WireSortComparers(ResultsSortComparers);
    }

    /// <summary>Forward the grid's multi-select (empty = all filtered rows) to the
    /// batch Find Func command, which fills each row's inline "Funcs" column.</summary>
    private void OnBatchFindFuncClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameClassFilterViewModel vm) return;
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        var rows = new List<GameClassEntry>();
        if (grid?.SelectedItems is { } sel)
        {
            foreach (var item in sel)
                if (item is GameClassEntry entry) rows.Add(entry);
        }
        vm.BatchFindFuncCommand.Execute(rows);
    }
}
