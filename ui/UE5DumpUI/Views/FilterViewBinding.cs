using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UE5DumpUI.Core;
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
    /// <summary>Where every restore decision is logged (Debug, "view"), set once at startup: the
    /// selection and scroll outcome is timing-dependent and only visible on a live UI, and the
    /// first failure measured on it could not be explained without it.</summary>
    public static ILoggingService? Log { get; set; }

    /// <summary>Attach <paramref name="grid"/> to the keeper <paramref name="pick"/> returns from
    /// <paramref name="owner"/>'s view model, now and whenever the DataContext changes (a panel is
    /// built before its view model arrives).</summary>
    public static void AttachFilterView<TVm>(this StyledElement owner, DataGrid? grid,
                                             Func<TVm, FilterViewKeeper> pick) where TVm : class
    {
        if (grid == null) return;
        void Hook() { if (owner.DataContext is TVm vm) Attach(grid, pick(vm)); }
        owner.DataContextChanged += (_, _) => Hook();
        Hook();
    }

    /// <summary>The same, for a ListBox.</summary>
    public static void AttachFilterView<TVm>(this StyledElement owner, ListBox? list,
                                             Func<TVm, FilterViewKeeper> pick) where TVm : class
    {
        if (list == null) return;
        void Hook() { if (owner.DataContext is TVm vm) Attach(list, pick(vm)); }
        owner.DataContextChanged += (_, _) => Hook();
        Hook();
    }

    /// <summary>One attached control's restore state.</summary>
    private sealed class Session
    {
        public readonly FilterViewRestoreQueue Queue = new();

        /// <summary>True from a capture to the end of its rebuild, and while a restore selects:
        /// a selection change then is the detach, a view model's own re-select or the restore —
        /// not the user, so it must not drop a parked selection.</summary>
        public bool Ours;

        public void UserChangedSelection(string name)
        {
            if (Ours || !Queue.HasParked) return;
            Queue.Unpark();
            Log?.Debug($"FilterView {name}: the user changed the selection, the hidden one is dropped");
        }
    }

    private static FilterViewState Capture(Session session, Func<FilterViewState> live, string name)
    {
        session.Ours = true;
        FilterViewState? shown = null;
        var state = session.Queue.Capture(() => shown = live());
        string source = ReferenceEquals(state, shown) ? "from the control" : "carried or parked";
        Log?.Debug($"FilterView {name}: capture {state.Selected.Count} selected ({source}), the control shows {shown?.Selected.Count ?? 0}");
        return state;
    }

    public static void Attach(DataGrid grid, FilterViewKeeper keeper)
    {
        var session = new Session();
        string name = grid.Name ?? "grid";
        grid.SelectionChanged += (_, _) => session.UserChangedSelection(name);
        keeper.ForgetView = () => session.Queue.Unpark();
        keeper.CaptureView = () => Capture(session,
            () => new FilterViewState(SelectedInDisplayOrder(grid), TopRow(grid)), name);
        keeper.RestoreView = (state, mode) => Restore(session,
            items: () => DisplayItems(grid),
            current: () => grid.SelectedItem,
            select: rows =>
            {
                if (grid.SelectionMode == DataGridSelectionMode.Single || rows.Count == 1)
                    grid.SelectedItem = rows.Count > 0 ? rows[0] : null;
                else
                    foreach (var r in rows) grid.SelectedItems.Add(r);
            },
            scrollIntoView: row => grid.ScrollIntoView(row, null),
            selectedCount: () => grid.SelectedItems.Count,
            name,
            state, mode, keeper.KeyOf);
    }

    public static void Attach(ListBox list, FilterViewKeeper keeper)
    {
        var session = new Session();
        string name = list.Name ?? "list";
        list.SelectionChanged += (_, _) => session.UserChangedSelection(name);
        keeper.ForgetView = () => session.Queue.Unpark();
        keeper.CaptureView = () => Capture(session, () =>
        {
            var selected = new List<object>();
            if (list.SelectedItems != null)
                foreach (var o in list.SelectedItems) if (o != null) selected.Add(o);
            if (selected.Count == 0 && list.SelectedItem != null) selected.Add(list.SelectedItem);
            return new FilterViewState(OrderBy(selected, ListItems(list)), TopContainer<ListBoxItem>(list));
        }, name);
        keeper.RestoreView = (state, mode) => Restore(session,
            items: () => ListItems(list),
            current: () => list.SelectedItem,
            select: rows => list.SelectedItem = rows.Count > 0 ? rows[0] : null,
            scrollIntoView: row => list.ScrollIntoView(row),
            selectedCount: () => list.SelectedItems?.Count ?? (list.SelectedItem != null ? 1 : 0),
            name,
            state, mode, keeper.KeyOf);
    }

    private static void Restore(Session session,
                                Func<List<object>> items, Func<object?> current, Action<List<object>> select,
                                Action<object> scrollIntoView, Func<int> selectedCount, string name,
                                FilterViewState state, FilterViewRestore mode, Func<object, object>? keyOf)
    {
        // The rebuild that followed the capture is done: selection changes are the user's again.
        session.Ours = false;
        var queue = session.Queue;
        // Keys typed faster than this runs rebuild again: only the newest restore of the burst
        // runs, carrying the selection the user had before it (FilterViewRestoreQueue).
        int ticket = queue.Schedule(state);
        Log?.Debug($"FilterView {name}: queued #{ticket} {mode}, carrying {state.Selected.Count} selected, top row {(state.TopRow != null ? "known" : "none")}");
        void Scroll(object row)
        {
            if (queue.IsNewest(ticket)) scrollIntoView(row);
        }

        // After the rebuild's own layout pass, like every restore in this UI.
        Dispatcher.UIThread.Post(() =>
        {
            if (!queue.Begin(ticket))
            {
                Log?.Debug($"FilterView {name}: #{ticket} dropped, a newer restore replaces it");
                return;
            }
            var now = items();
            if (now.Count == 0)
            {
                queue.Settle(state, 0);
                Log?.Debug($"FilterView {name}: #{ticket} ran on an empty list, {(queue.HasParked ? "the selection parked" : "nothing to park")}");
                return;
            }
            object Key(object o) => keyOf?.Invoke(o) ?? o;
            var byKey = new Dictionary<object, object>();
            foreach (var o in now) byKey.TryAdd(Key(o), o);
            object? Find(object o) => byKey.TryGetValue(Key(o), out var hit) ? hit : null;

            var kept = OrderBy(state.Selected.Select(Find).OfType<object>().ToList(), now);
            queue.Settle(state, kept.Count);

            // A rebuild leaves the list unselected, so a selection here was made after it. The
            // view model re-selecting the row it had (it does that itself where selecting has
            // side effects -- an editor sync, a pipe walk) still gets the scroll; anything else
            // -- a navigation that emptied the box and then picked its own row -- wins outright.
            if (current() is { } picked)
            {
                var pickedKey = Key(picked);
                if (!kept.Any(k => Equals(Key(k), pickedKey)))
                {
                    Log?.Debug($"FilterView {name}: #{ticket} {mode} backs off, {now.Count} rows, {kept.Count} of {state.Selected.Count} found, another row was picked after the rebuild");
                    return;
                }
            }
            else if (kept.Count > 0)
            {
                session.Ours = true;
                try { select(kept); }
                finally { session.Ours = false; }
            }
            Log?.Debug($"FilterView {name}: #{ticket} {mode} ran, {now.Count} rows, {kept.Count} of {state.Selected.Count} found, {selectedCount()} selected now{(queue.HasParked ? ", the selection parked" : "")}");
            Dispatcher.UIThread.Post(() =>
                Log?.Debug($"FilterView {name}: #{ticket} settled, {selectedCount()} selected"),
                DispatcherPriority.ApplicationIdle);

            var first = kept.Count > 0 ? kept[0] : null;
            if (mode == FilterViewRestore.Narrowed)
            {
                if (first != null)
                    Dispatcher.UIThread.Post(() => Scroll(first), DispatcherPriority.Background);
                return;
            }

            var toTop = first ?? (state.TopRow != null ? Find(state.TopRow) : null);
            if (toTop == null) return;
            Dispatcher.UIThread.Post(() =>
            {
                Scroll(now[^1]);
                Dispatcher.UIThread.Post(() => Scroll(toTop), DispatcherPriority.Background);
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
