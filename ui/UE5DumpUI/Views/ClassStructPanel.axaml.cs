using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.Views;

public partial class ClassStructPanel : UserControl
{
    // [W4-HEXSORT] The field Address column sorts NUMERICALLY. It is binding-rooted, so it wired nothing and the default
    // sort ordered the hex TEXT. Not one of W4's nine: the address-column pin in DataGridSortWiringTests found it.
    private static readonly IReadOnlyDictionary<string, IComparer> FieldsSortComparers =
        new Dictionary<string, IComparer>
        {
            ["Address"] = DataGridSortComparers.Hex<FieldInfoModel>(r => r.AddressValue),
        };

    public ClassStructPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("ClassFieldsGrid")?.WireSortComparers(FieldsSortComparers);
    }
}
