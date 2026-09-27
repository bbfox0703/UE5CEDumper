using System.Collections.ObjectModel;
using UE5DumpUI.Services;

namespace UE5DumpUI.Helpers;

/// <summary>What the View should do with the state it captured, once the list has been rebuilt.</summary>
public enum FilterViewRestore
{
    /// <summary>An ordinary filter change: keep the rows that are still selected selected, and
    /// keep the first of them visible. A selection the filter hid entirely is parked by the View
    /// and comes back when an edit shows its rows again (<c>FilterViewRestoreQueue</c>).</summary>
    Narrowed,

    /// <summary>The user cleared a real keyword (2+ characters to empty in one step): the first
    /// selected row comes to the top; with nothing selected, the row that was at the top of the
    /// filtered list stays at the top.</summary>
    Cleared,
}

/// <summary>A View's selection (in its DISPLAY order, which a column sort makes differ from the
/// collection's) and its topmost visible row, captured before a rebuild. Rows are the bound
/// items themselves; <see cref="FilterViewKeeper.KeyOf"/> maps them when a rebuild makes new
/// row objects.</summary>
public sealed record FilterViewState(IReadOnlyList<object> Selected, object? TopRow);

/// <summary>
/// Keeps a keyword-filtered list's selection and scroll position across filter edits
/// [KEYWORD-BOX-VIEW-KEEP].
/// </summary>
/// <remarks>
/// <para>Every client-side filter box rebuilt its bound collection on every edit, and a
/// <c>Clear()</c> (or a new collection) throws a DataGrid back to row 0 and drops the selection:
/// the first character typed, a trailing space, and a clear all lost the user's place. The
/// maintainer's rules (2026-09-27): a rebuild that would show the SAME rows does not happen; a
/// rebuild keeps the rows that are still selected and keeps them visible; and a keyword cleared
/// from 2+ characters to empty brings the first selected row to the top, or, with nothing
/// selected, keeps the filtered list's top row at the top.</para>
/// <para>The selection and the scroll position live in the View (a column sort, a
/// multi-selection), so the View supplies <see cref="CaptureView"/> and <see cref="RestoreView"/>
/// (see <c>Views/FilterViewBinding</c>). They are PROPERTIES, not events: a panel that is
/// re-attached replaces them instead of stacking a second subscription.</para>
/// <para>"Cleared" is judged against the last keyword this keeper APPLIED, not the last one
/// typed, so a debounced box whose keyword went 2+ → empty inside one debounce window (never
/// applied) is not treated as a clear.</para>
/// </remarks>
public sealed class FilterViewKeeper
{
    private string[] _lastApplied = Array.Empty<string>();

    /// <summary>Set by the View: the current selection and top row, taken synchronously BEFORE
    /// the list changes. Null when no View is attached.</summary>
    public Func<FilterViewState?>? CaptureView { get; set; }

    /// <summary>Set by the View: re-select and scroll after the list changed.</summary>
    public Action<FilterViewState, FilterViewRestore>? RestoreView { get; set; }

    /// <summary>Set by the View: drop whatever selection it keeps for rows the filter hides, when
    /// <see cref="Forget"/> says the program reset the box.</summary>
    public Action? ForgetView { get; set; }

    /// <summary>A stable identity for a row, for boxes whose rebuild creates NEW row objects
    /// (a key such as an address or a uid). Null means reference identity.</summary>
    public Func<object, object>? KeyOf { get; init; }

    /// <summary>
    /// Replace <paramref name="target"/>'s contents with <paramref name="rows"/> — unless they are
    /// already the same rows in the same order, in which case nothing happens and the view does
    /// not move. <paramref name="detachSelection"/> nulls the bound <c>Selected*</c> properties
    /// first, as <see cref="UiCollection.Reset{T}"/> requires. <paramref name="keywords"/> are the
    /// list's keyword boxes as now applied, always in the same order. Returns whether the list
    /// changed.
    /// </summary>
    public bool Update<T>(ObservableCollection<T> target, IReadOnlyList<T> rows, Action detachSelection,
                          params string?[] keywords)
    {
        var mode = Advance(keywords);
        if (SameRows(target, rows)) return false;
        Rebuild(() => UiCollection.Reset(target, rows, detachSelection), mode);
        return true;
    }

    /// <summary>
    /// The same, for a box that swaps in a NEW collection (or rebuilds some other way):
    /// <paramref name="rebuild"/> does the swap; <paramref name="rowsChanged"/> false skips it.
    /// </summary>
    public bool Update(Action rebuild, bool rowsChanged, params string?[] keywords)
    {
        var mode = Advance(keywords);
        if (!rowsChanged) return false;
        Rebuild(rebuild, mode);
        return true;
    }

    /// <summary>Forget the applied keywords, and the View's parked selection: call before the
    /// PROGRAM empties a box (a reload, a navigation), so that is not taken for the user clearing
    /// it and a selection from before it does not come back.</summary>
    public void Forget()
    {
        _lastApplied = Array.Empty<string>();
        ForgetView?.Invoke();
    }

    /// <summary>True when <paramref name="rows"/> are exactly <paramref name="current"/>'s items,
    /// in order, by reference.</summary>
    public static bool SameRows<T>(IReadOnlyList<T> current, IReadOnlyList<T> rows)
    {
        if (current.Count != rows.Count) return false;
        for (int i = 0; i < rows.Count; i++)
            if (!ReferenceEquals(current[i], rows[i])) return false;
        return true;
    }

    /// <summary>True when the keyword went from 2+ (trimmed) characters to empty.</summary>
    public static bool IsClear(string? previous, string? now)
        => (previous?.Trim().Length ?? 0) >= 2 && string.IsNullOrWhiteSpace(now);

    // A clear is ONE box going 2+ -> empty while every other box of the list stays as it was: a
    // list with several boxes (a global keyword plus per-column ones) is cleared box by box.
    private FilterViewRestore Advance(string?[] keywords)
    {
        var now = keywords.Select(k => k ?? "").ToArray();
        bool cleared = false, othersSame = true;
        if (now.Length == _lastApplied.Length)
        {
            for (int i = 0; i < now.Length; i++)
            {
                if (IsClear(_lastApplied[i], now[i])) cleared = true;
                else if (!string.Equals(_lastApplied[i], now[i], StringComparison.Ordinal)) othersSame = false;
            }
        }
        _lastApplied = now;
        return cleared && othersSame ? FilterViewRestore.Cleared : FilterViewRestore.Narrowed;
    }

    private void Rebuild(Action rebuild, FilterViewRestore mode)
    {
        var state = CaptureView?.Invoke();
        rebuild();
        if (state != null)
            RestoreView?.Invoke(state, mode);
    }
}
