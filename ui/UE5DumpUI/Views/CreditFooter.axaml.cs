using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;

namespace UE5DumpUI.Views;

public partial class CreditFooter : UserControl
{
    public CreditFooter()
    {
        InitializeComponent();
    }

    private void OnAobMakerUrlPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock tb)
            OpenUrl(tb.Text);
    }

    private void OnRepoUrlPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock tb)
            OpenUrl(tb.Text);
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // Swallow: failing to open a credit link must never crash the UI.
        }
    }
}
