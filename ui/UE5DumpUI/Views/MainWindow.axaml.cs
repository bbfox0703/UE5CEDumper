using System;
using Avalonia.Controls;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Audit fixes #16 / #17: dispose timer-owning child VMs when the
        // window closes, so background DispatcherTimers and Threading.Timer
        // callbacks don't fire post-close on torn-down state.
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is IDisposable d) d.Dispose();
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
