using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class LiveWalkerPanel : UserControl
{
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(60, 255, 200, 0));

    public LiveWalkerPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is LiveWalkerViewModel vm)
        {
            vm.ScrollToFieldRequested += OnScrollToFieldRequested;
            vm.ScrollToFirstSearchMatch += OnScrollToFirstSearchMatch;
        }
    }

    private void OnScrollToFieldRequested(string fieldName)
    {
        // Post to UI thread to ensure DataGrid has rendered the new items
        Dispatcher.UIThread.Post(() =>
        {
            var grid = this.FindControl<DataGrid>("FieldGrid");
            if (grid?.ItemsSource == null) return;

            var target = grid.ItemsSource.Cast<LiveFieldValue>()
                .FirstOrDefault(f => f.Name == fieldName);
            if (target != null)
            {
                grid.ScrollIntoView(target, null);
                grid.SelectedItem = target;
            }
        }, DispatcherPriority.Background);
    }

    private void OnScrollToFirstSearchMatch()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var grid = this.FindControl<DataGrid>("FieldGrid");
            if (grid?.ItemsSource == null) return;

            var target = grid.ItemsSource.Cast<LiveFieldValue>()
                .FirstOrDefault(f => f.IsSearchMatch);
            if (target != null)
                grid.ScrollIntoView(target, null);
        }, DispatcherPriority.Background);
    }

    private void FieldGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is LiveFieldValue field)
        {
            e.Row.Background = field.IsSearchMatch ? HighlightBrush : Brushes.Transparent;
        }
    }
}
