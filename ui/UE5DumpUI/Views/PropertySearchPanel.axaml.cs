using Avalonia.Controls;
using Avalonia.Input;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class PropertySearchPanel : UserControl
{
    public PropertySearchPanel()
    {
        InitializeComponent();
    }

    private void SearchQueryInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is PropertySearchViewModel vm
            && vm.SearchCommand.CanExecute(null))
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
