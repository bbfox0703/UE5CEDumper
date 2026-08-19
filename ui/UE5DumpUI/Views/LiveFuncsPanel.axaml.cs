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
    // trimmed under AOT (aot-pitfalls.md §4.5). Class / Function / Params bind and
    // sort on the same path, so they are rooted and need nothing.
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
