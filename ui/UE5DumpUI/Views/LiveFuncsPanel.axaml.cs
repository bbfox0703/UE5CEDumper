using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.Views;

public partial class LiveFuncsPanel : UserControl
{
    // AOT-safe sort comparers for every column whose sort path no column binding
    // roots — a template column (no column-level Binding at all) or a text column
    // whose SortMemberPath differs from its Binding path. Their reflection sort is
    // trimmed under AOT (aot-pitfalls.md §4.5). Class and Function bind and sort on the
    // same path, so they are rooted and need nothing.
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
        };

    public LiveFuncsPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("ResultsGrid")?.WireSortComparers(ResultsSortComparers);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
