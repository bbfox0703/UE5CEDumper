using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class ConsolePanel : UserControl
{
    // AOT-safe sort comparers for every column whose sort path no column binding roots
    // (Helpers/DataGridSortComparers.cs). A column that binds and sorts on the same
    // path needs nothing.
    private static readonly IReadOnlyDictionary<string, IComparer> ResultsSortComparers =
        new Dictionary<string, IComparer>
        {
            // Params [PARAMSSORT-2026-08-22]. The cell shows "{NumParms} ({ParmsSize}B)"
            // but the column sorts on NumParms, so nothing roots the sort path and a
            // comparer is required. Sorting on ParamsLabel instead would be AOT-safe
            // (binding and sort path agree, so the property is rooted) and WRONG: the
            // ordinal order of the label puts "11 (72B)" above "2 (9B)", which one
            // function with 10 or more parameters is enough to reach.
            ["NumParms"] = DataGridSortComparers.Number<AllFunctionEntry>(r => r.NumParms),
        };

    public ConsolePanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("ResultsGrid")?.WireSortComparers(ResultsSortComparers);
        this.AttachFilterView<ConsoleViewModel>(this.FindControl<DataGrid>("ResultsGrid"), vm => vm.ResultsView);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Enter in the command line runs the typed command, as Run does.</summary>
    private void CommandInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ConsoleViewModel vm
            && vm.RunCommandTextCommand.CanExecute(null))
        {
            vm.RunCommandTextCommand.Execute(null);
            e.Handled = true;
        }
    }
}
