using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.Views;

public partial class DumpExplorerPanel : UserControl
{
    // AOT-safe sort comparers — under trimming the DataGrid can't discover a
    // sortable member by reflection, so header clicks are a silent no-op without
    // these (see the sort trap explained in Helpers/DataGridSortComparers.cs). The Offset column displays a hex string
    // but sorts on the numeric value.
    private static readonly IReadOnlyDictionary<string, IComparer> DumpSortComparers =
        new Dictionary<string, IComparer>
        {
            ["KindLabel"]  = DataGridSortComparers.Ordinal<DumpEntry>(e => e.KindLabel),
            ["Name"]       = DataGridSortComparers.Ordinal<DumpEntry>(e => e.Name),
            ["OwnerClass"] = DataGridSortComparers.Ordinal<DumpEntry>(e => e.OwnerClass),
            ["TypeInfo"]   = DataGridSortComparers.Ordinal<DumpEntry>(e => e.TypeInfo),
            ["Offset"]     = DataGridSortComparers.Number<DumpEntry>(e => e.Offset),
            ["Path"]       = DataGridSortComparers.Ordinal<DumpEntry>(e => e.Path),
        };

    public DumpExplorerPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("MatchedGrid")?.WireSortComparers(DumpSortComparers);
        this.FindControl<DataGrid>("UnmatchedGrid")?.WireSortComparers(DumpSortComparers);
    }

    /// <summary>
    /// On (re)load — including when the user switches back to this tab — scroll the
    /// last-acted-on row back into view. A Jump / Instances / Copy path action sets the
    /// grid selection (<c>DumpExplorerViewModel.SelectEntry</c>) so it survives the tab
    /// switch the action triggers; this brings that selected row back into view on
    /// return. Deferred so the DataGrid has materialized its row containers first
    /// (ScrollIntoView no-ops otherwise — same reason as PropertySearchPanel).
    /// </summary>
    private void OnPanelLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ScrollGridToSelection("MatchedGrid");
            ScrollGridToSelection("UnmatchedGrid");
        }, DispatcherPriority.Background);
    }

    private void ScrollGridToSelection(string gridName)
    {
        var grid = this.FindControl<DataGrid>(gridName);
        if (grid?.SelectedItem is not { } sel) return;
        try { grid.ScrollIntoView(sel, null); }
        catch { /* defensive: recycled grid / missing row */ }
    }
}
