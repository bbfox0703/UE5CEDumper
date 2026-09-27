using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class ValueSearchPanel : UserControl
{
    // Maps a result column's SortMemberPath to the server-side sort key
    // (query_candidates wire string). The DataGrid is windowed (V3-C), so a
    // client-side column sort would only reorder the loaded page — instead a
    // header click re-sorts the WHOLE session set in the DLL. "Class.Field"
    // sorts by class (the label leads with the owning class).
    private static readonly IReadOnlyDictionary<string, string> ColumnSortKeys =
        new Dictionary<string, string>
        {
            ["LocationLabel"] = "class",
            ["FieldType"]     = "type",
            ["Value"]         = "value",
            ["FieldOffset"]   = "offset",
            ["Addr"]          = "addr",
            ["InstanceName"]  = "instance",
        };

    public ValueSearchPanel()
    {
        InitializeComponent();
        this.AttachFilterView<ValueSearchViewModel>(this.FindControl<DataGrid>("ResultsGrid"), vm => vm.CandidatesView);
        this.AttachFilterView<ValueSearchViewModel>(this.FindControl<DataGrid>("GroupResultsGrid"), vm => vm.GroupCandidatesView);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Cancel the built-in (window-only, AOT-reflection) sort and drive the
    // existing server-side sort instead — keeps the Sort picker + Desc toggle
    // in sync and sorts the full result set, not just the loaded page.
    private void ResultsGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not ValueSearchViewModel vm) return;
        var path = e.Column.SortMemberPath;
        if (!string.IsNullOrEmpty(path) && ColumnSortKeys.TryGetValue(path, out var key))
            vm.ApplyColumnSort(key);
    }
}
