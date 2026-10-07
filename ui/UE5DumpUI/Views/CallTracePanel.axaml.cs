using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

/// <summary>[LIVEFUNCS-TIMELINE-2026-10-04] The Call Trace tab; everything it does is CallTraceViewModel's, apart from
/// turning a thumb's drag into a width.</summary>
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

    // [LIVEFUNCS-STEP2] U10: a Thumb reports the pointer's offset from where it was pressed, measured from the thumb
    // itself. The thumb sits on the edge it resizes and moves with it, so each report is the step since the last one:
    // added to the width as it comes. The view model clamps the result.
    private CallTraceViewModel? Vm => DataContext as CallTraceViewModel;

    private void OnTimeThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (Vm is { } vm) vm.TimeColWidth += e.Vector.X;
    }

    private void OnDurationThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (Vm is { } vm) vm.DurationColWidth += e.Vector.X;
    }

    private void OnThreadThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (Vm is { } vm) vm.ThreadColWidth += e.Vector.X;
    }

    /// <summary>Object is docked at the right and its handle is its left edge: a drag to the left widens it.</summary>
    private void OnObjectThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (Vm is { } vm) vm.ObjectColWidth -= e.Vector.X;
    }

    /// <summary>The detail pane is docked at the right and its handle is its left edge: a drag to the left widens it.</summary>
    private void OnDetailPaneThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (Vm is { } vm) vm.DetailPaneWidth -= e.Vector.X;
    }
}
