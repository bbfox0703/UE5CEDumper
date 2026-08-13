using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;

namespace UE5DumpUI.Views;

public partial class PointerPanel : UserControl
{
    public PointerPanel()
    {
        InitializeComponent();
    }

    // Credit-footer URLs (AOBMaker + repo) open in the default browser. The
    // experimental opt-in is now a separate bare checkbox, so both credit links
    // are plain clickable text again. Mirrors CreditFooter's UseShellExecute pattern.
    private void OnUrlPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock tb || string.IsNullOrWhiteSpace(tb.Text)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = tb.Text,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // Swallow: failing to open a credit link must never crash the UI.
        }
    }
}
