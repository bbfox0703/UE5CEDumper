using Avalonia.Controls;
using Avalonia.Input;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class ObjectTreePanel : UserControl
{
    public ObjectTreePanel()
    {
        InitializeComponent();
        this.AttachFilterView<ObjectTreeViewModel>(this.FindControl<ListBox>("ObjectList"), vm => vm.NodesView);
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ObjectTreeViewModel vm)
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
