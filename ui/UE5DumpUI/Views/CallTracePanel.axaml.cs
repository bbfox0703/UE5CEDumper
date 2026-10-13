using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

/// <summary>[LIVEFUNCS-TIMELINE-2026-10-04] The Call Trace tab; everything it does is CallTraceViewModel's, apart from
/// turning a thumb's drag into a width and telling the view model how much room the panel has.</summary>
public partial class CallTracePanel : UserControl
{
    private readonly DockPanel? _root;
    private readonly Thumb? _paneHandle;

    public CallTracePanel()
    {
        InitializeComponent();
        _root = this.FindControl<DockPanel>("Root");
        _paneHandle = this.FindControl<Thumb>("PaneHandle");
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // [LIVEFUNCS-STEP2] U10: a Thumb reports the pointer's offset from where it was pressed, measured from the thumb
    // itself. The thumb sits on the edge it resizes and moves with it, so each report is the step since the last one:
    // added to the width shown as it comes. The view model clamps the result and remembers it where it moved the width.
    private CallTraceViewModel? Vm => DataContext as CallTraceViewModel;

    /// <summary>[CT-DETAIL-COVERS-LIST] Tells the view model the width the list and the detail pane share, so a
    /// remembered width the panel can no longer show, however the panel came to be narrower, is fitted. It is told
    /// before the children are measured only to spare measuring them twice: told after, the widths it changes
    /// invalidate them, and Avalonia measures them again before anything is arranged, to the same layout.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Vm is { } vm && _root != null && double.IsFinite(availableSize.Width))
            vm.SplitWidth = availableSize.Deflate(Padding + BorderThickness + _root.Margin).Width - (_paneHandle?.Width ?? 0);
        return base.MeasureOverride(availableSize);
    }

    /// <summary>[CT-FLOOR-208] A view model set after the last measure has never been told the room. One that replaces
    /// another leaves the panel's own size as it was, so nothing else measures the panel again, and the new view
    /// model's remembered pane would stay unfitted over the list.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        InvalidateMeasure();
    }

    private void OnTimeThumbDragDelta(object? sender, VectorEventArgs e) => Vm?.DragTime(e.Vector.X);

    private void OnDurationThumbDragDelta(object? sender, VectorEventArgs e) => Vm?.DragDuration(e.Vector.X);

    private void OnThreadThumbDragDelta(object? sender, VectorEventArgs e) => Vm?.DragThread(e.Vector.X);

    /// <summary>Object is docked at the right and its handle is its left edge: a drag to the left widens it.</summary>
    private void OnObjectThumbDragDelta(object? sender, VectorEventArgs e) => Vm?.DragObject(-e.Vector.X);

    /// <summary>The detail pane is docked at the right and its handle is its left edge: a drag to the left widens it.</summary>
    private void OnDetailPaneThumbDragDelta(object? sender, VectorEventArgs e) => Vm?.DragDetailPane(-e.Vector.X);
}
