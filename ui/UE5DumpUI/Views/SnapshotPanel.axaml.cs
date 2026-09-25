using System;
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

    // [UI-SPACE-2026-09-25] Floors for the two star rows -- the saved list (row 3) keeps a header and ~3 rows, the
    // diff / group area (row 5) the mode switch + the Compare expander + a header and ~4 rows. (ninth review, R9-01)
    // Fixed floors pushed the Auto rows below the window: opening the Noise picker overflowed by 299 DIP on a 1067-DIP
    // screen (measured). So the floors are capped at the room the Auto rows leave, and shrink first.
    private const int SavedRow = 3;
    private const int LowerRow = 5;
    private const double SavedFloor = 110;
    private const double LowerFloor = 350;

    /// <summary>The MinHeights of the saved-list row and the diff / group row, given the room (panel height minus the
    /// Auto rows): the full floors when it holds them, else both scaled down in proportion, never more than the room.
    /// Pure, so a test pins it.</summary>
    public static (double Saved, double Lower) RowFloors(double room)
    {
        if (room >= SavedFloor + LowerFloor) return (SavedFloor, LowerFloor);
        if (room <= 0) return (0d, 0d);
        double k = room / (SavedFloor + LowerFloor);
        return (SavedFloor * k, LowerFloor * k);
    }

    public SnapshotPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("SnapshotsGrid")?.WireSortComparers(SnapshotsSortComparers);
        this.FindControl<DataGrid>("DiffGrid")?.WireSortComparers(DiffSortComparers);
        this.FindControl<DataGrid>("GroupGrid")?.WireSortComparers(GroupSortComparers);

        if (this.FindControl<Grid>("RootGrid") is { } root)
        {
            // Every layout pass: the Auto rows' natural heights (an Auto row measures its child unbounded, so
            // DesiredSize is what it asks for), the room left for the two star rows, the floors that fit it. Written
            // only when they change, so the relayout this causes settles on the next pass.
            root.LayoutUpdated += (_, _) =>
            {
                double autos = 0;
                foreach (var child in root.Children)
                {
                    int row = Grid.GetRow(child);
                    if (row != SavedRow && row != LowerRow && child.IsVisible)
                        autos += child.DesiredSize.Height;
                }
                var (saved, lower) = RowFloors(root.Bounds.Height - autos);
                if (Math.Abs(root.RowDefinitions[SavedRow].MinHeight - saved) > 0.5)
                    root.RowDefinitions[SavedRow].MinHeight = saved;
                if (Math.Abs(root.RowDefinitions[LowerRow].MinHeight - lower) > 0.5)
                    root.RowDefinitions[LowerRow].MinHeight = lower;
            };
        }
    }
}
