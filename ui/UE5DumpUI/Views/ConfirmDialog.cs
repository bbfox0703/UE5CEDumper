using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;

namespace UE5DumpUI.Views;

/// <summary>
/// Minimal reusable Yes/No confirmation dialog (code-behind only, AOT-safe like
/// the other dialogs in this folder), e.g. a warning before a large batch xref run
/// ("scan 250 properties — each is a full GObjects sweep?"). Returns true
/// when the user confirms.
/// </summary>
public sealed class ConfirmDialog : Window
{
    private bool _result;

    /// <summary>Resolve the desktop owner and show the dialog modally. Returns
    /// false if confirmed=false or there is no owner window.</summary>
    public static async Task<bool> ShowAsync(
        string title, string message,
        string confirmText = "Run", string cancelText = "Cancel")
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } owner)
            return false;
        var dlg = new ConfirmDialog(title, message, confirmText, cancelText);
        await dlg.ShowDialog(owner);
        return dlg._result;
    }

    private ConfirmDialog(string title, string message, string confirmText, string cancelText)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        MinHeight = 130;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));

        var root = new DockPanel { Margin = new Thickness(16) };

        var msg = new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        };
        DockPanel.SetDock(msg, Dock.Top);
        root.Children.Add(msg);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        var btnCancel = new Button { Content = cancelText, Padding = new Thickness(14, 6) };
        btnCancel.Click += (_, _) => { _result = false; Close(); };
        var btnOk = new Button
        {
            Content = confirmText,
            Padding = new Thickness(14, 6),
            Foreground = new SolidColorBrush(Color.Parse("#E5C07B")),
            BorderBrush = new SolidColorBrush(Color.Parse("#E5C07B")),
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
        };
        btnOk.Click += (_, _) => { _result = true; Close(); };
        btnRow.Children.Add(btnCancel);
        btnRow.Children.Add(btnOk);
        DockPanel.SetDock(btnRow, Dock.Bottom);
        root.Children.Add(btnRow);

        Content = root;
    }
}
