using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class ClassStructPanel : UserControl
{
    // [W4-HEXSORT] The field Address column sorts NUMERICALLY. It is binding-rooted, so without this comparer the default
    // sort orders the hex TEXT. The address-column pin in DataGridSortWiringTests guards it.
    private static readonly IReadOnlyDictionary<string, IComparer> FieldsSortComparers =
        new Dictionary<string, IComparer>
        {
            ["Address"] = DataGridSortComparers.Hex<FieldInfoModel>(r => r.AddressValue),
        };

    public ClassStructPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("ClassFieldsGrid")?.WireSortComparers(FieldsSortComparers);
        this.AttachFilterView<ClassStructViewModel>(this.FindControl<DataGrid>("ClassFieldsGrid"), vm => vm.FieldsView);
    }
}
