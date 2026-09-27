using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UE5DumpUI.Helpers;

namespace UE5DumpUI.Views;

/// <summary>
/// The View half of <see cref="FilterViewKeeper"/> [KEYWORD-BOX-VIEW-KEEP]: captures a list
/// control's selection (in display order) and top row before a filter rebuild, and puts them
/// back after it. One call per box, from the panel, when its DataContext arrives.
/// </summary>
/// <remarks>
/// Scrolling a row to the TOP: <c>ScrollIntoView</c> means "make visible", and a row BELOW the
/// viewport lands on the bottom edge while a row ABOVE it lands on the top edge. After a rebuild
/// the control is at its first row, so it is scrolled to the last row first; the target is then
/// above the viewport and comes back as the first visible row (the recipe `[LW-BACK-SCROLL]`
/// measured on the Live Walker grid).
/// </remarks>
public static class FilterViewBinding
{
    public static void Attach(DataGrid grid, FilterViewKeeper keeper)
    {
        keeper.CaptureView = () => new FilterViewState(SelectedInDisplayOrder(grid), TopRow(grid));
        keeper.RestoreView = (state, mode) => Restore(
            items: () => DisplayItems(grid),
            select: rows =>
            {
                if (grid.SelectionMode == DataGridSelectionMode.Single || rows.Count == 1)
                    grid.SelectedItem = rows.Count > 0 ? rows[0] : null;
                else
                    foreach (var r in rows) grid.SelectedItems.Add(r);
            },
            scrollIntoView: row => grid.ScrollIntoView(row, null),
            state, mode, keeper.KeyOf);
    }

    public static void Attach(ListBox list, FilterViewKeeper keeper)
    {
        keeper.CaptureView = () =>
        {
            var selected = new List<object>();
            if (list.SelectedItems != null)
                foreach (var o in list.SelectedItems) if (o != null) selected.Add(o);
            if (selected.Count == 0 && list.SelectedItem != null) selected.Add(list.SelectedItem);
            return new FilterViewState(OrderBy(selected, ListItems(list)), TopContainer<ListBoxItem>(list));
        };
        keeper.RestoreView = (state, mode) => Restore(
            items: () => ListItems(list),
            select: rows => list.SelectedItem = rows.Count > 0 ? rows[0] : null,
            scrollIntoView: row => list.ScrollIntoView(row),
            state, mode, keeper.KeyOf);
    }

    private static void Restore(Func<List<object>> items, Action<List<object>> select, Action<object> scrollIntoView,
                                FilterViewState state, FilterViewRestore mode, Func<object, object>? keyOf)
    {
        // After the rebuild's own layout pass, like every restore in this UI.
        Dispatcher.UIThread.Post(() =>
        {
            var now = items();
            if (now.Count == 0) return;
            var byKey = new Dictionary<object, object>();
            foreach (var o in now) byKey.TryAdd(keyOf?.Invoke(o) ?? o, o);
            object? Find(object o) => byKey.TryGetValue(keyOf?.Invoke(o) ?? o, out var hit) ? hit : null;

            var kept = OrderBy(state.Selected.Select(Find).OfType<object>().ToList(), now);
            if (kept.Count > 0) select(kept);

            var first = kept.Count > 0 ? kept[0] : null;
            if (mode == FilterViewRestore.Narrowed)
            {
                if (first != null)
                    Dispatcher.UIThread.Post(() => scrollIntoView(first), DispatcherPriority.Background);
                return;
            }

            var toTop = first ?? (state.TopRow != null ? Find(state.TopRow) : null);
            if (toTop == null) return;
            Dispatcher.UIThread.Post(() =>
            {
                scrollIntoView(now[^1]);
                Dispatcher.UIThread.Post(() => scrollIntoView(toTop), DispatcherPriority.Background);
            }, DispatcherPriority.Background);
        }, DispatcherPriority.Background);
    }

    // The grid's own order: its collection view, which a column sort reorders.
    private static List<object> DisplayItems(DataGrid grid)
    {
        IEnumerable? source = grid.CollectionView ?? grid.ItemsSource;
        var list = new List<object>();
        if (source != null) foreach (var o in source) if (o != null) list.Add(o);
        return list;
    }

    private static List<object> ListItems(ListBox list)
    {
        var items = new List<object>();
        if (list.ItemsSource != null) foreach (var o in list.ItemsSource) if (o != null) items.Add(o);
        return items;
    }

    private static List<object> SelectedInDisplayOrder(DataGrid grid)
    {
        var selected = new List<object>();
        foreach (var o in grid.SelectedItems) if (o != null) selected.Add(o);
        if (selected.Count == 0 && grid.SelectedItem != null) selected.Add(grid.SelectedItem);
        return OrderBy(selected, DisplayItems(grid));
    }

    private static List<object> OrderBy(List<object> subset, List<object> order)
    {
        if (subset.Count < 2) return subset;
        var rank = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < order.Count; i++) rank.TryAdd(order[i], i);
        return subset.OrderBy(o => rank.TryGetValue(o, out var r) ? r : int.MaxValue).ToList();
    }

    private static object? TopRow(DataGrid grid) => TopContainer<DataGridRow>(grid);

    // The topmost realised row whose top edge is at or below the control's top (rows scrolled
    // above the viewport translate to a negative Y).
    private static object? TopContainer<TRow>(Control owner) where TRow : Control
    {
        object? top = null;
        double bestY = double.MaxValue;
        foreach (var row in owner.GetVisualDescendants().OfType<TRow>())
        {
            if (!row.IsVisible || row.DataContext == null) continue;
            var pt = row.TranslatePoint(new Point(0, 0), owner);
            if (pt is { } p && p.Y >= -1 && p.Y < bestY) { bestY = p.Y; top = row.DataContext; }
        }
        return top;
    }
}
