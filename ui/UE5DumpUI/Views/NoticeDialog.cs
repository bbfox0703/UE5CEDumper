using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using UE5DumpUI.Core;

namespace UE5DumpUI.Views;

/// <summary>
/// A message the user must read, with one OK button (code-behind only, AOT-safe like ConfirmDialog, which always offers
/// a second choice). Connect uses it to refuse a game that runs an old DLL [PIPE-PER-PROCESS-2026-11-06].
/// </summary>
public sealed class NoticeDialog : Window
{
    /// <summary>Show the message modally over the main window; returns when it is closed. Does nothing when there is
    /// no main window.</summary>
    public static async Task ShowAsync(string title, string message)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } owner)
            return;
        await new NoticeDialog(title, message).ShowDialog(owner);
    }

    private NoticeDialog(string title, string message)
    {
        Title = title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MinHeight = 130;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));

        var root = new DockPanel { Margin = new Thickness(16) };
        var ok = new Button
        {
            Content = Res.Get("str.Notice.Ok"), Padding = new Thickness(18, 6),
            HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true,
        };
        ok.Click += (_, _) => Close();
        DockPanel.SetDock(ok, Dock.Bottom);
        root.Children.Add(ok);
        root.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });
        Content = root;
    }
}
