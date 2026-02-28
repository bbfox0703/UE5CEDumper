using Avalonia.Controls;
using Avalonia.Input;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class InstanceFinderPanel : UserControl
{
    public InstanceFinderPanel()
    {
        InitializeComponent();
    }

    private void SearchClassNameInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is InstanceFinderViewModel vm
            && vm.SearchCommand.CanExecute(null))
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void LookupAddressInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is InstanceFinderViewModel vm
            && vm.LookupAddressCommand.CanExecute(null))
        {
            vm.LookupAddressCommand.Execute(null);
            e.Handled = true;
        }
    }
}
