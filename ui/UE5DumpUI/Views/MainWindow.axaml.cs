using Avalonia.Controls;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Stop Live Walker auto-refresh when the user switches away from the Live Walker tab.
    /// Auto-refresh is for monitoring live data — no point polling while viewing other tabs.
    /// </summary>
    private void MainTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tabs) return;
        if (DataContext is not MainWindowViewModel vm) return;

        // Tab index 0 = Live Walker (first tab in the TabControl)
        if (tabs.SelectedIndex != 0 && vm.LiveWalker.IsAutoRefreshing)
        {
            vm.LiveWalker.StopAutoRefreshTimer();
        }
    }
}
