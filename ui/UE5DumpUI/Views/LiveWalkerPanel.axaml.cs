using Avalonia.Controls;
using Avalonia.Media;
using UE5DumpUI.Models;

namespace UE5DumpUI.Views;

public partial class LiveWalkerPanel : UserControl
{
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(60, 255, 200, 0));

    public LiveWalkerPanel()
    {
        InitializeComponent();
    }

    private void FieldGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is LiveFieldValue field)
        {
            e.Row.Background = field.IsSearchMatch ? HighlightBrush : Brushes.Transparent;
        }
    }
}
