using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.Views;

public partial class SpcPanel : UserControl
{
    // AOT-safe sort comparer. Found by the repo-wide DataGrid sweep behind audit #5
    // AF16-AF23; no finding named this panel. The Group grid's Class column renders
    // LocationLabel and sorts on ClassName, so nothing roots the sort path and the
    // reflection sort is trimmed (Helpers/DataGridSortComparers.cs class doc). Same
    // column, same defect as SnapshotPanel's Group grid — they share GroupCandidate.
    private static readonly IReadOnlyDictionary<string, IComparer> GroupSortComparers =
        new Dictionary<string, IComparer>
        {
            ["ClassName"] = DataGridSortComparers.Ordinal<GroupCandidate>(r => r.ClassName),
        };

    public SpcPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("GroupGrid")?.WireSortComparers(GroupSortComparers);
    }
}
