using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.Views;

public partial class ConsolePanel : UserControl
{
    // AOT-safe sort comparers for every column whose sort path no column binding roots
    // (Helpers/DataGridSortComparers.cs). Class / Command / Flags bind and sort on the
    // same path and need nothing.
            // Params (PARAMSSORT-2026-08-22). The cell shows "{NumParms} ({ParmsSize}B)"
            // but the column now sorts on NumParms, so nothing roots the sort path and a
            // comparer is required. It previously sorted on ParamsLabel — which was AOT-safe
            // (binding and sort path agreed, so the property was rooted) and WRONG: the
            // ordinal order of the label puts "11 (72B)" above "2 (9B)". Measured on
            // DumperTest 2026-08-22: 3,142 functions, two with >=10 parameters, so the
            // inversion is reachable on a stock host. AF20 fixed the Live Walker twin only,
            // because the audit asked "is the header inert under trimming?" and these three
            // were not inert — just wrong.
    private static readonly IReadOnlyDictionary<string, IComparer> ResultsSortComparers =
        new Dictionary<string, IComparer>
        {
            ["NumParms"] = DataGridSortComparers.Number<AllFunctionEntry>(r => r.NumParms),
        };

    public ConsolePanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("ResultsGrid")?.WireSortComparers(ResultsSortComparers);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
