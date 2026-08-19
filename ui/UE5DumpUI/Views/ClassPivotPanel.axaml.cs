using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class ClassPivotPanel : UserControl
{
    // AOT-safe sort comparers. Found by the repo-wide DataGrid sweep that closed
    // audit #5 AF16-AF23 — this panel was never named by a finding, and had the same
    // defect in five columns. A DataGridTextColumn is only rooted when its
    // SortMemberPath equals its own Binding path; every column below renders a
    // FORMATTED string and sorts on the numeric/raw backing property, so nothing
    // roots the sort path and the reflection sort is trimmed away
    // (Helpers/DataGridSortComparers.cs class doc).
    private static readonly IReadOnlyDictionary<string, IComparer> DiscoverSortComparers =
        new Dictionary<string, IComparer>
        {
            // Binding=ChangedDisplay ("3/40") — sorting that string puts 10 before 3.
            ["InstancesChanged"] = DataGridSortComparers.Number<DiscoveryCandidate>(r => r.InstancesChanged),
            // Binding=CategoryName, sort on the numeric score behind the category.
            ["InterestScore"]    = DataGridSortComparers.Number<DiscoveryCandidate>(r => r.InterestScore),
            // Binding=ShapeLabel, sort on the numeric shape score.
            ["ShapeScore"]       = DataGridSortComparers.Double<DiscoveryCandidate>(r => r.ShapeScore),
            // Binding=ScoreDisplay ("12.5") — a string sort is wrong past 9.
            ["Score"]            = DataGridSortComparers.Double<DiscoveryCandidate>(r => r.Score),
        };

    private static readonly IReadOnlyDictionary<string, IComparer> FieldPickSortComparers =
        new Dictionary<string, IComparer>
        {
            // Binding=KeyScoreDisplay ("0.83").
            ["KeyScore"] = DataGridSortComparers.Double<PivotFieldPick>(r => r.KeyScore),
        };

    public ClassPivotPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("DiscoverGrid")?.WireSortComparers(DiscoverSortComparers);
        this.FindControl<DataGrid>("FieldPickGrid")?.WireSortComparers(FieldPickSortComparers);
    }
}
