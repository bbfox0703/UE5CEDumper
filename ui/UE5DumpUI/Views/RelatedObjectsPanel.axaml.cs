using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class RelatedObjectsPanel : UserControl
{
    // AOT-safe sort comparers — the Address column displays a hex string but
    // sorts on the parsed ulong, and the template (actions) column has no
    // column-level binding. Without these the header click is a silent no-op
    // under AOT (the sort trap explained in Helpers/DataGridSortComparers.cs).
    private static readonly IReadOnlyDictionary<string, IComparer> RelatedSortComparers =
        new Dictionary<string, IComparer>
        {
            ["Relation"] = DataGridSortComparers.Ordinal<RelatedObject>(r => r.Relation),
            ["Display"]  = DataGridSortComparers.Ordinal<RelatedObject>(r => r.Display),
            ["Address"]  = DataGridSortComparers.Hex<RelatedObject>(r => r.AddressValue),
        };

    public RelatedObjectsPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("RelatedGrid")?.WireSortComparers(RelatedSortComparers);
    }

    private void TargetAddressInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is RelatedObjectsViewModel vm
            && vm.LoadCommand.CanExecute(null))
        {
            vm.LoadCommand.Execute(null);
            e.Handled = true;
        }
    }
}
