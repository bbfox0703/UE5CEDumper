using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.Views;

public partial class SnapshotPanel : UserControl
{
    // AOT-safe sort comparers. Found by the repo-wide DataGrid sweep behind audit #5
    // AF16-AF23; no finding named this panel. Each column below renders one property
    // and sorts on another, so no column binding roots the sort path and the
    // reflection sort is trimmed (Helpers/DataGridSortComparers.cs class doc).
    private static readonly IReadOnlyDictionary<string, IComparer> SnapshotsSortComparers =
        new Dictionary<string, IComparer>
        {
            // Binding=LabelDisplay, which prefixes "⚠ " on an unusable snapshot — so a
            // string sort on the DISPLAY would file every warned row under "⚠".
            ["Label"]    = DataGridSortComparers.Ordinal<SnapshotMeta>(r => r.Label),
            // Binding=EstSizeDisplay ("1.2 GB") — the numeric backing property is the
            // only one that orders sizes correctly (working-lessons.md §3.2 rule 1).
            ["EstBytes"] = DataGridSortComparers.Number<SnapshotMeta>(r => r.EstBytes),
        };

    private static readonly IReadOnlyDictionary<string, IComparer> DiffSortComparers =
        new Dictionary<string, IComparer>
        {
            // Binding=DirectionGlyph ("▲"/"▼"/"") — sort on the enum so Up/Down group.
            ["Direction"] = DataGridSortComparers.Number<SnapshotDiffRow>(r => (long)r.Direction),
        };

    private static readonly IReadOnlyDictionary<string, IComparer> GroupSortComparers =
        new Dictionary<string, IComparer>
        {
            // Binding=LocationLabel ("Class (DeclaringClass)") but sorts on ClassName.
            ["ClassName"] = DataGridSortComparers.Ordinal<GroupCandidate>(r => r.ClassName),
        };

    public SnapshotPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("SnapshotsGrid")?.WireSortComparers(SnapshotsSortComparers);
        this.FindControl<DataGrid>("DiffGrid")?.WireSortComparers(DiffSortComparers);
        this.FindControl<DataGrid>("GroupGrid")?.WireSortComparers(GroupSortComparers);
    }
}
