using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using UE5DumpUI.Helpers;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Views;

public partial class TeleportPanel : UserControl
{
    /// <summary>AOT-safe sort comparers for the Coordinate Library grid. Five of its
    /// columns were dead under trimming: X/Y/Z/Yaw sort on a NESTED path
    /// (<c>Entry.X</c>) that no column binding roots, and Dist sorts on
    /// <c>Distance</c> while binding <c>DistanceText</c> — both are exactly the trap
    /// <c>Helpers/DataGridSortComparers</c> documents. Label/Group/Map worked, so the
    /// symptom was "some headers do nothing", which reads as a flaky app. (B16)</summary>
    private static readonly IReadOnlyDictionary<string, System.Collections.IComparer> CoordSortComparers =
        new Dictionary<string, System.Collections.IComparer>
        {
            ["Label"]    = Helpers.DataGridSortComparers.Ordinal<CoordRow>(r => r.Label),
            ["Group"]    = Helpers.DataGridSortComparers.Ordinal<CoordRow>(r => r.Group),
            ["Map"]      = Helpers.DataGridSortComparers.Ordinal<CoordRow>(r => r.Map),
            ["Entry.X"]  = Helpers.DataGridSortComparers.Double<CoordRow>(r => r.Entry.X),
            ["Entry.Y"]  = Helpers.DataGridSortComparers.Double<CoordRow>(r => r.Entry.Y),
            ["Entry.Z"]  = Helpers.DataGridSortComparers.Double<CoordRow>(r => r.Entry.Z),
            ["Entry.Yaw"] = Helpers.DataGridSortComparers.Double<CoordRow>(r => r.Entry.Yaw),
            ["Distance"] = Helpers.DataGridSortComparers.Double<CoordRow>(r => r.Distance),
        };

    public TeleportPanel()
    {
        InitializeComponent();
        this.FindControl<DataGrid>("CoordGrid")?.WireSortComparers(CoordSortComparers);
        // Tunnel KeyDown so we intercept the capture key before any focused
        // child (e.g. the BugItGo TextBox) consumes it.
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        // Suppress focus-driven auto-scroll: clicking a partially-visible control
        // (e.g. the "Global cursor hotkey" checkbox) otherwise makes the
        // ScrollViewer bring it into view, scrolling the panel and eating the
        // first click (the second click then works). Handling RequestBringIntoView
        // on the content root — a child of the ScrollContentPresenter — marks it
        // handled before the presenter's class handler scrolls. Manual wheel /
        // scrollbar are unaffected (they don't raise this event).
        ContentRoot.AddHandler(RequestBringIntoViewEvent, OnRequestBringIntoView);
        this.AttachFilterView<TeleportViewModel>(this.FindControl<DataGrid>("CoordGrid"), vm => vm.CoordView);
    }

    private void OnRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
        => e.Handled = true;

    // ── Quick-jump nav (right-click → jump to a card) ──
    // Experimental-gated (the card list would otherwise reveal experimental card
    // names when the opt-in is off). The menu is rebuilt on each open from the
    // currently-VISIBLE cards, so hidden/experimental cards are never listed.
    private void OnJumpMenuOpening(object? sender, CancelEventArgs e)
    {
        if (DataContext is not TeleportViewModel vm || !vm.ExperimentalEnabled)
        {
            e.Cancel = true;   // no quick-jump menu unless experimental features are on
            return;
        }
        if (sender is not ContextMenu menu) { e.Cancel = true; return; }

        var items = new List<object>();
        foreach (var child in ContentRoot.Children)
        {
            if (child is not Border card || !card.IsEffectivelyVisible) continue;
            var title = ExtractCardTitle(card);
            if (string.IsNullOrWhiteSpace(title)) continue;
            var target = card;   // capture per-iteration
            var mi = new MenuItem { Header = title };
            mi.Click += (_, _) => ScrollCardIntoView(target);
            items.Add(mi);
        }
        if (items.Count == 0) { e.Cancel = true; return; }
        menu.ItemsSource = items;
    }

    /// <summary>A card's label = its header TextBlock (the SemiBold one), falling
    /// back to the first non-empty TextBlock in the card.</summary>
    private static string? ExtractCardTitle(Border card)
    {
        TextBlock? header = null, first = null;
        foreach (var t in card.GetVisualDescendants().OfType<TextBlock>())
        {
            if (string.IsNullOrWhiteSpace(t.Text)) continue;
            first ??= t;
            if (t.FontWeight == FontWeight.SemiBold) { header = t; break; }
        }
        return (header ?? first)?.Text?.Trim();
    }

    /// <summary>Scroll a card to the top of the viewport. Uses the ScrollViewer's
    /// Offset directly because ContentRoot swallows RequestBringIntoView (the
    /// focus-auto-scroll suppression above), which would defeat BringIntoView().</summary>
    private void ScrollCardIntoView(Border card)
    {
        if (card.TranslatePoint(new Point(0, 0), ContentRoot) is { } p)
            Scroller.Offset = new Vector(Scroller.Offset.X, Math.Max(0, p.Y - 6));
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TeleportViewModel vm || !vm.IsCapturingHotkey)
            return;
        if (vm.ApplyCapturedKey(e.Key, e.KeyModifiers))
            e.Handled = true;
    }
}
