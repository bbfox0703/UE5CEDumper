using System;
using System.Collections.Generic;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Per-session "remembered keywords" store for the Live Walker field search.
/// Implements the requested rules:
///   - LRU: most-recently-used first, capped at <see cref="MaxEntries"/>.
///     ⚠ New entries only. Re-using an entry that is ALREADY remembered does NOT
///     move it to the front — see the ⚠ block in <see cref="Remember"/>: these
///     lists are AutoCompleteBox ItemsSources, and mutating one mid-interaction
///     makes the control revert the user's pick.
///   - Longest valid wins: a new keyword that EXTENDS a remembered one replaces
///     it (the shorter prefix is dropped); a new keyword that is a PREFIX of an
///     already-remembered (longer) one is ignored. So while typing
///     m → ma → mag → magi → magic, only the settled "magic" is kept — never the
///     intermediate prefixes.
///   - Only valid keywords are remembered: the caller passes only keywords that
///     yielded matches, and entries shorter than <see cref="MinLength"/> are
///     rejected here as a backstop.
/// All comparisons are case-insensitive; the original casing is preserved.
/// </summary>
public static class SearchKeywordHistory
{
    /// <summary>Maximum remembered keywords (LRU eviction beyond this).</summary>
    public const int MaxEntries = 8;

    /// <summary>Keywords shorter than this are never remembered.</summary>
    public const int MinLength = 2;

    /// <summary>
    /// Fold <paramref name="keyword"/> into <paramref name="history"/> in place,
    /// applying the LRU + longest-valid rules. The list is ordered
    /// most-recently-used first. Returns true when the history changed.
    /// </summary>
    public static bool Remember(IList<string> history, string? keyword, int maxEntries = MaxEntries)
    {
        if (history is null) return false;
        var k = keyword?.Trim() ?? "";
        if (k.Length < MinLength) return false;

        // Longest valid wins: a remembered entry that already EXTENDS k covers it,
        // so k adds nothing — keep the longer one, change nothing.
        //
        // ⚠ `>=`, NOT `>` — an EXACT duplicate must be a no-op too, and that is a
        // correctness requirement, not a tidiness one.
        //
        // Every one of these histories is bound as an AutoCompleteBox `ItemsSource`.
        // Avalonia's AutoCompleteBox handles `ItemsCollectionChanged` with an
        // unconditional `RefreshView()`, which clears the popup ListBox; on the
        // MOUSE-pick path the ListBox is still holding the picked item at that moment,
        // so clearing it drives `OnAdapterSelectionChanged` -> `SelectedItem = null`
        // WITHOUT the `_skipSelectedItemTextUpdate` guard the other null-assignment
        // sites set — and `OnSelectedItemChanged(null)` restores the box's text to
        // what the user had TYPED. Net effect: pick "RemoteRole" from the dropdown,
        // and ~700 ms later (when the debounce fires) the box silently reverts to "re".
        //
        // ANY CollectionChanged does it — content equality is irrelevant, an unrelated
        // add or remove reverts it just the same. Since the dropdown only ever offers
        // entries that are already IN the history, a picked keyword is always an exact
        // duplicate, so refusing to touch the list for a duplicate removes the trigger
        // for the only case a user can reach by clicking. A keyword that is genuinely
        // new still mutates the list — but it cannot have come from the dropdown, so
        // there is no selection to revert.
        //
        // The cost is deliberate: re-using a remembered keyword no longer moves it to
        // the front, so ordering is first-seen rather than strict LRU for entries that
        // already exist, and the first-seen casing is kept (which is what this class's
        // own doc promises). Reported by the maintainer 2026-08-21 on Live Walker;
        // all 20 keyword boxes shared it. `[LWFILTERREVERT-2026-08-21]`.
        for (int i = 0; i < history.Count; i++)
        {
            if (history[i].Length >= k.Length &&
                history[i].StartsWith(k, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Drop entries that k supersedes: any existing entry that is a prefix of k
        // (shorter run of the same keyword) or an exact case-insensitive duplicate.
        // Iterate backwards for safe in-place removal.
        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (k.StartsWith(history[i], StringComparison.OrdinalIgnoreCase))
                history.RemoveAt(i);
        }

        // Insert at the front (most-recently used) and enforce the LRU cap.
        history.Insert(0, k);
        while (history.Count > maxEntries)
            history.RemoveAt(history.Count - 1);

        return true;
    }
}
