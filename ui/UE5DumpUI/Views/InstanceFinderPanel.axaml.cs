using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class InstanceFinderPanel : UserControl
{
    // [W4-HEXSORT] The address columns sort NUMERICALLY. Each is binding-rooted (AOT-safe), so it wired nothing -- and
    // the default sort ordered the hex TEXT: "0x9" after "0x10", a 12-character module address after every 13-character
    // heap one. "HexValue" is deliberately absent: a raw byte dump in memory order, for which text order is memcmp order.
    private static readonly IReadOnlyDictionary<string, IComparer> InstancesSortComparers =
        new Dictionary<string, IComparer>
        {
            ["Address"] = DataGridSortComparers.Hex<InstanceResult>(r => r.AddressValue),
        };

    private static readonly IReadOnlyDictionary<string, IComparer> ContainerMatchesSortComparers =
        new Dictionary<string, IComparer>
        {
            ["OwnerAddress"] = DataGridSortComparers.Hex<ContainerMatch>(r => r.OwnerAddressValue),
        };

    private static readonly IReadOnlyDictionary<string, IComparer> InstanceFieldsSortComparers =
        new Dictionary<string, IComparer>
        {
            ["FieldAddress"] = DataGridSortComparers.Hex<LiveFieldValue>(r => r.FieldAddressValue),
        };

    public InstanceFinderPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("InstancesGrid")?.WireSortComparers(InstancesSortComparers);
        this.FindControl<DataGrid>("ContainerMatchesGrid")?.WireSortComparers(ContainerMatchesSortComparers);
        this.FindControl<DataGrid>("InstanceFieldsGrid")?.WireSortComparers(InstanceFieldsSortComparers);
    }

    /// <summary>Forward the grid's multi-select (empty = all filtered rows) to the
    /// batch Find Func command, which fills each row's inline "Funcs" column
    /// (deduped by class within the run).</summary>
    private void OnBatchFindFuncsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstanceFinderViewModel vm) return;
        var grid = this.FindControl<DataGrid>("InstancesGrid");
        var rows = new List<InstanceResult>();
        if (grid?.SelectedItems is { } sel)
        {
            foreach (var item in sel)
                if (item is InstanceResult r) rows.Add(r);
        }
        vm.BatchFindFuncsCommand.Execute(rows);
    }

    private void SearchClassNameInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is InstanceFinderViewModel vm
            && vm.SearchCommand.CanExecute(null))
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void LookupAddressInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is InstanceFinderViewModel vm
            && vm.LookupAddressCommand.CanExecute(null))
        {
            vm.LookupAddressCommand.Execute(null);
            e.Handled = true;
        }
    }
}
