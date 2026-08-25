using System.Collections.ObjectModel;
using System.Collections.Generic;
using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks down the Live Walker remembered-keyword rules: LRU ordering, the
/// 8-entry cap, the "longest valid wins" prefix collapse, and the minimum
/// length backstop.
/// </summary>
public class SearchKeywordHistoryTests
{
    private static List<string> New() => new();

    // ── Basic add / validity ─────────────────────────────────────────────────

    [Fact]
    public void Remember_AddsValidKeyword()
    {
        var h = New();
        Assert.True(SearchKeywordHistory.Remember(h, "magic"));
        Assert.Equal(new[] { "magic" }, h);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("m")]   // below MinLength (2)
    public void Remember_RejectsTooShortOrBlank(string? keyword)
    {
        var h = New();
        Assert.False(SearchKeywordHistory.Remember(h, keyword));
        Assert.Empty(h);
    }

    [Fact]
    public void Remember_TrimsWhitespace()
    {
        var h = New();
        SearchKeywordHistory.Remember(h, "  magic  ");
        Assert.Equal(new[] { "magic" }, h);
    }

    // ── LRU ordering ─────────────────────────────────────────────────────────

    [Fact]
    public void Remember_MostRecentFirst()
    {
        var h = New();
        SearchKeywordHistory.Remember(h, "health");
        SearchKeywordHistory.Remember(h, "mana");
        SearchKeywordHistory.Remember(h, "stamina");
        Assert.Equal(new[] { "stamina", "mana", "health" }, h);
    }

    /// <summary>
    /// Re-using a remembered keyword must NOT reorder the list.
    ///
    /// This deliberately reverses the old "touch moves to front" behaviour. These
    /// histories are bound as AutoCompleteBox ItemsSources, and ANY CollectionChanged
    /// on one makes the control rebuild its popup and revert the user's pick back to
    /// what they had typed ([LWFILTERREVERT-2026-08-21]). A dropdown only ever offers
    /// entries already in the history, so the pick path IS the duplicate path — which
    /// is why a duplicate must be a no-op rather than a cheap reorder.
    /// </summary>
    [Fact]
    public void Remember_ReusingKeyword_DoesNotReorder_SoAnAutoCompletePickSurvives()
    {
        var h = New();
        SearchKeywordHistory.Remember(h, "health");
        SearchKeywordHistory.Remember(h, "mana");
        bool changed = SearchKeywordHistory.Remember(h, "health");   // re-use
        Assert.False(changed);
        Assert.Equal(new[] { "mana", "health" }, h);                 // untouched order
    }

    [Fact]
    public void Remember_ExactDuplicate_CaseInsensitive_NoGrowth()
    {
        var h = New();
        SearchKeywordHistory.Remember(h, "Magic");
        bool changed = SearchKeywordHistory.Remember(h, "magic");
        Assert.False(changed);
        Assert.Single(h);
        // FIRST-seen casing is kept. The class doc has always promised "the original
        // casing is preserved"; the old move-to-front path quietly broke that promise.
        Assert.Equal("Magic", h[0]);
    }

    /// <summary>
    /// THE ASSERTION THAT ACTUALLY CATCHES THE BUG - count EVENTS, not contents.
    ///
    /// The reported failure had a history whose contents were byte-identical before and
    /// after, so every content-comparing assertion in this file passed while the app was
    /// broken. What reverts the AutoCompleteBox is the CollectionChanged notification
    /// itself: Avalonia answers it with an unconditional RefreshView(), which clears the
    /// popup ListBox and drives the box's text back to what the user typed. Contents are
    /// not the signal; the event is.
    /// </summary>
    [Fact]
    public void Remember_Duplicate_RaisesNoCollectionChanged()
    {
        var h = new ObservableCollection<string> { "RemoteRole", "Rotation", "Velocity" };
        int events = 0;
        h.CollectionChanged += (_, _) => events++;

        bool changed = SearchKeywordHistory.Remember(h, "RemoteRole");

        Assert.False(changed);
        Assert.Equal(0, events);   // was 2 (RemoveAt + Insert) before the fix

        // And the same for a duplicate that is NOT already at the front - the mouse can
        // pick any row in the dropdown, so position must not matter.
        events = 0;
        Assert.False(SearchKeywordHistory.Remember(h, "Velocity"));
        Assert.Equal(0, events);
    }

    /// <summary>The other half: a genuinely NEW keyword must still be remembered, so the
    /// no-op above cannot have been achieved by making Remember inert.</summary>
    [Fact]
    public void Remember_NewKeyword_StillMutatesAndNotifies()
    {
        var h = new ObservableCollection<string> { "RemoteRole" };
        int events = 0;
        h.CollectionChanged += (_, _) => events++;

        Assert.True(SearchKeywordHistory.Remember(h, "Velocity"));
        Assert.True(events > 0);
        Assert.Equal("Velocity", h[0]);
    }

    // ── Longest valid wins ───────────────────────────────────────────────────

    [Fact]
    public void Remember_LongerKeyword_ReplacesPrefix()
    {
        var h = New();
        SearchKeywordHistory.Remember(h, "magi");
        SearchKeywordHistory.Remember(h, "magic");   // extends "magi"
        Assert.Equal(new[] { "magic" }, h);
    }

    [Fact]
    public void Remember_PrefixOfExisting_Ignored()
    {
        var h = New();
        SearchKeywordHistory.Remember(h, "magic");
        Assert.False(SearchKeywordHistory.Remember(h, "magi"));   // shorter prefix
        Assert.Equal(new[] { "magic" }, h);
    }

    [Fact]
    public void Remember_TypingProgression_KeepsOnlyLongest()
    {
        var h = New();
        // Simulate the debounce firing at several typing pauses on the way to "magic".
        SearchKeywordHistory.Remember(h, "ma");
        SearchKeywordHistory.Remember(h, "mag");
        SearchKeywordHistory.Remember(h, "magi");
        SearchKeywordHistory.Remember(h, "magic");
        Assert.Equal(new[] { "magic" }, h);
    }

    [Fact]
    public void Remember_PrefixCollapse_DoesNotTouchUnrelated()
    {
        var h = New();
        SearchKeywordHistory.Remember(h, "health");
        SearchKeywordHistory.Remember(h, "mag");
        SearchKeywordHistory.Remember(h, "magic");   // collapses "mag", keeps "health"
        Assert.Equal(new[] { "magic", "health" }, h);
    }

    // ── LRU cap ──────────────────────────────────────────────────────────────

    [Fact]
    public void Remember_EvictsBeyondMax()
    {
        var h = New();
        for (int i = 0; i < SearchKeywordHistory.MaxEntries + 3; i++)
            SearchKeywordHistory.Remember(h, $"kw{i:D2}");

        Assert.Equal(SearchKeywordHistory.MaxEntries, h.Count);
        // Newest kept, oldest evicted.
        Assert.Equal($"kw{SearchKeywordHistory.MaxEntries + 2:D2}", h[0]);
        Assert.DoesNotContain("kw00", h);
    }
}
