using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class PropertySearchPanel : UserControl
{
    public PropertySearchPanel()
    {
        InitializeComponent();
        // Restore the user's last selected row + scroll position whenever the
        // panel re-attaches to the visual tree. Avalonia's TabControl swaps
        // out inactive tab content; the VM keeps SelectedResult populated, but
        // the freshly-shown DataGrid doesn't auto-scroll to its SelectedItem
        // (and visually appears to have lost the selection because the
        // highlighted row is offscreen). Calling ScrollIntoView after the
        // grid is laid out brings the row back into view.
        Loaded += OnPanelLoaded;
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

    private void OnPanelLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not PropertySearchViewModel vm) return;
        if (vm.SelectedResult == null) return;
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        if (grid == null) return;
        // Defer until the DataGrid has its row visuals materialized — without
        // the dispatcher hop ScrollIntoView no-ops because the row isn't in
        // the realized container set yet.
        Dispatcher.UIThread.Post(() =>
        {
            try { grid.ScrollIntoView(vm.SelectedResult, null); }
            catch { /* defensive: missing row, recycled grid, etc. */ }
        }, DispatcherPriority.Background);
    }
}
