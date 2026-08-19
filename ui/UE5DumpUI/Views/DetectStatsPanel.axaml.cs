using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.Views;

/// <summary>
/// Experimental "Detect Player Stats" panel (P4). One button shortlists likely
/// HP / MP / Gold fields and confirms them against the live game; results are a
/// low-accuracy reference (see the in-panel disclaimer). Logic lives in
/// <see cref="ViewModels.DetectStatsViewModel"/>.
/// </summary>
public partial class DetectStatsPanel : UserControl
{
    // AOT-safe sort comparers (audit #5 AF16/AF17). Every column here sets
    // CanUserSort="True", but two of the seven sort on a path NO column binding
    // roots — "✓" renders ConfirmBadge while sorting on IsConfirmed, and "Offset"
    // renders OffsetHex while sorting on PropOffset. Under trimming the reflection
    // sort cannot resolve either path, so those two headers were live and inert in
    // the shipped build. The other five sort on their own Binding path and are
    // rooted by it (Helpers/DataGridSortComparers.cs class doc).
    //
    // PropOffset is deliberately the NUMERIC backing property, not OffsetHex —
    // working-lessons.md §3.2 rule 1: a hex STRING sorts lexicographically, which
    // puts 0x100 before 0x20.
    private static readonly IReadOnlyDictionary<string, IComparer> DetectSortComparers =
        new Dictionary<string, IComparer>
        {
            ["IsConfirmed"] = DataGridSortComparers.Bool<DetectedStat>(r => r.IsConfirmed),
            ["PropOffset"]  = DataGridSortComparers.Number<DetectedStat>(r => r.PropOffset),
        };

    public DetectStatsPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("DetectGrid")?.WireSortComparers(DetectSortComparers);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
