using System.Collections.ObjectModel;

namespace UE5DumpUI.Services;

/// <summary>
/// UI-thread helpers for safely rebuilding an <see cref="ObservableCollection{T}"/>
/// that is bound to a <c>SelectingItemsControl</c> (ComboBox / ListBox / DataGrid
/// SelectedItem). Avalonia's selection model throws — either
/// <c>ArgumentOutOfRangeException</c> (the selected index is stale during the
/// <c>Reset</c>) or <c>InvalidOperationException("Cannot change ObservableCollection
/// during a CollectionChanged event")</c> — if the collection is <c>Clear()</c>'d
/// while it still holds a live selection. Detaching the selection
/// <i>before</i> mutating avoids it, which is exactly what <see cref="Reset{T}"/> does.
/// </summary>
public static class UiCollection
{
    /// <summary>
    /// Atomically replace the contents of <paramref name="target"/> with
    /// <paramref name="items"/>. <paramref name="detachSelection"/> must null every
    /// bound <c>Selected*</c> property that points into <paramref name="target"/>;
    /// it runs first so the selection model has nothing stale to reconcile when the
    /// <c>Clear()</c> fires. Caller re-applies a selection afterwards if desired.
    /// Must be called on the UI thread.
    /// </summary>
    public static void Reset<T>(ObservableCollection<T> target,
                                IEnumerable<T> items,
                                System.Action detachSelection)
    {
        detachSelection();
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
