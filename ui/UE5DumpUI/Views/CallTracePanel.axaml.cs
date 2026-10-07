using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UE5DumpUI.Views;

/// <summary>[LIVEFUNCS-TIMELINE-2026-10-04] The Call Trace tab; everything it does is CallTraceViewModel's.</summary>
public partial class CallTracePanel : UserControl
{
    public CallTracePanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
