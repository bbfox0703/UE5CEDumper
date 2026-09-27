using System.Collections.ObjectModel;
using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [KEYWORD-BOX-VIEW-KEEP] A burst of keyword edits faster than the posted restores. Measured on
/// build 3574 (Interesting Funcs): one row picked, "spawn act" typed and Ctrl+A, Delete sent at
/// once -- the clear left the row at the bottom edge instead of the top, because every capture
/// after the first read the detached, empty selection. The View itself needs a live check; this
/// pins the scheduling it now relies on.
/// </summary>
public class FilterViewRestoreQueueTests
{
    private static FilterViewState State(params object[] selected) => new(selected, null);
    private static readonly FilterViewState Empty = State();

    [Fact]
    public void With_nothing_queued_a_capture_reads_the_control()
    {
        var q = new FilterViewRestoreQueue();
        var live = State("a");
        Assert.Same(live, q.Capture(() => live));
    }

    [Fact]
    public void While_a_restore_is_queued_an_emptied_control_hands_over_the_carried_state()
    {
        var q = new FilterViewRestoreQueue();
        var picked = State("a");
        q.Schedule(picked);

        Assert.Same(picked, q.Capture(() => Empty));   // the rebuild detached the selection
    }

    [Fact]
    public void A_selection_made_after_the_rebuild_wins_over_the_carried_one()
    {
        var q = new FilterViewRestoreQueue();
        q.Schedule(State("a"));
        var clicked = State("b");

        Assert.Same(clicked, q.Capture(() => clicked));
    }

    [Fact]
    public void Only_the_newest_restore_runs_and_then_captures_read_the_control_again()
    {
        var q = new FilterViewRestoreQueue();
        int first = q.Schedule(State("a"));
        int second = q.Schedule(State("a"));

        Assert.False(q.IsNewest(first));
        Assert.False(q.Begin(first));
        Assert.True(q.Begin(second));
        Assert.True(q.IsNewest(second));
        Assert.Same(Empty, q.Capture(() => Empty));
    }

    [Fact]
    public void A_later_step_of_a_replaced_restore_is_not_the_newest()
    {
        var q = new FilterViewRestoreQueue();
        int first = q.Schedule(State("a"));
        Assert.True(q.Begin(first));                    // it ran...
        q.Schedule(State("a"));                         // ...and a newer rebuild queued another

        Assert.False(q.IsNewest(first));                // so its posted scroll stands down
    }

    // ── A selection the filter hides entirely is parked (the maintainer's call, 2026-09-27) ──

    [Fact]
    public void A_restore_that_found_none_of_its_selection_parks_it_for_the_next_empty_capture()
    {
        var q = new FilterViewRestoreQueue();
        var picked = State("a");
        Assert.True(q.Begin(q.Schedule(picked)));
        q.Settle(picked, found: 0);                    // a typo: no row of it is shown

        var shownNow = new FilterViewState(Array.Empty<object>(), "top");
        var got = q.Capture(() => shownNow);

        Assert.True(q.HasParked);
        Assert.Equal(new object[] { "a" }, got.Selected);
        Assert.Equal("top", got.TopRow);                // where the control is now, not then
    }

    [Fact]
    public void Showing_any_of_it_again_ends_the_parking()
    {
        var q = new FilterViewRestoreQueue();
        var picked = State("a", "b");
        q.Settle(picked, found: 0);
        q.Settle(picked, found: 1);                    // a partly hidden selection is not parked

        Assert.False(q.HasParked);
        Assert.Same(Empty, q.Capture(() => Empty));
    }

    [Fact]
    public void A_live_selection_and_a_queued_restore_both_win_over_the_parked_rows()
    {
        var q = new FilterViewRestoreQueue();
        q.Settle(State("a"), found: 0);
        var clicked = State("b");
        Assert.Same(clicked, q.Capture(() => clicked));

        var carried = State("c");
        q.Schedule(carried);
        Assert.Same(carried, q.Capture(() => Empty));
    }

    [Fact]
    public void Unpark_drops_the_parked_rows()
    {
        var q = new FilterViewRestoreQueue();
        q.Settle(State("a"), found: 0);

        q.Unpark();

        Assert.False(q.HasParked);
        Assert.Same(Empty, q.Capture(() => Empty));
    }

    [Fact]
    public void An_empty_capture_parks_nothing()
    {
        var q = new FilterViewRestoreQueue();
        q.Settle(Empty, found: 0);
        Assert.False(q.HasParked);
    }

    [Fact]
    public void The_keepers_Forget_drops_the_Views_parked_rows()
    {
        int forgotten = 0;
        var keeper = new FilterViewKeeper { ForgetView = () => forgotten++ };

        keeper.Forget();

        Assert.Equal(1, forgotten);
    }

    /// <summary>Build 3576, measured: a row picked, a keyword that shows no rows, then the clear —
    /// the row did not come back. Through the real keeper, with a fake View that settles each
    /// restore the way the binding does.</summary>
    [Fact]
    public void A_pick_hidden_by_a_typo_comes_back_to_the_top_when_the_keyword_is_cleared()
    {
        var list = new ObservableCollection<string> { "SpawnActor", "Spawn_Generation", "Jump" };
        var selected = new List<object>();
        var ran = new List<(FilterViewState State, FilterViewRestore Mode)>();
        var q = new FilterViewRestoreQueue();
        var keeper = new FilterViewKeeper
        {
            CaptureView = () => q.Capture(() => new FilterViewState(selected.ToList(), null)),
        };
        keeper.RestoreView = (state, mode) =>
        {
            int ticket = q.Schedule(state);
            if (!q.Begin(ticket)) return;                // runs at once: no burst here
            var found = state.Selected.Where(o => list.Contains((string)o)).ToList();
            q.Settle(state, found.Count);
            selected.AddRange(found);
            ran.Add((state, mode));
        };
        void Edit(string keyword, params string[] rows)
            => keeper.Update(list, rows, () => selected.Clear(), keyword);

        Edit("spawn", "SpawnActor", "Spawn_Generation");
        selected.Add("Spawn_Generation");               // the user picks a row
        Edit("spawnx");                                  // a typo: nothing matches
        Assert.Empty(selected);
        Edit("", "SpawnActor", "Spawn_Generation", "Jump");   // select all, Delete

        var (st, mode) = ran[^1];
        Assert.Equal(FilterViewRestore.Cleared, mode);
        Assert.Equal("Spawn_Generation", Assert.Single(st.Selected));
        Assert.Equal(new object[] { "Spawn_Generation" }, selected);
    }

    /// <summary>The measured burst, end to end through the real keeper: a fake View whose rebuild
    /// detaches the selection and whose restores only run when the "dispatcher" drains.</summary>
    [Fact]
    public void A_clear_typed_before_the_restores_ran_brings_the_original_pick_to_the_top()
    {
        var list = new ObservableCollection<string> { "SpawnActor", "Spawn_Generation", "Jump" };
        var selected = new List<object> { "Spawn_Generation" };
        var queued = new List<Action>();
        var ran = new List<(FilterViewState State, FilterViewRestore Mode)>();
        var q = new FilterViewRestoreQueue();
        var keeper = new FilterViewKeeper
        {
            CaptureView = () => q.Capture(() => new FilterViewState(selected.ToList(), null)),
        };
        keeper.RestoreView = (state, mode) =>
        {
            int ticket = q.Schedule(state);
            queued.Add(() => { if (q.Begin(ticket)) ran.Add((state, mode)); });
        };
        void Edit(string keyword, params string[] rows)
            => keeper.Update(list, rows, () => selected.Clear(), keyword);

        Edit("spawn", "SpawnActor", "Spawn_Generation");
        foreach (var run in queued) run();              // the pick is shown again
        queued.Clear(); ran.Clear();
        selected.Add("Spawn_Generation");

        Edit("s", "SpawnActor", "Spawn_Generation", "Jump");     // the box's text replaced by typing...
        Edit("spawn act", "SpawnActor", "Spawn_Generation");
        Edit("", "SpawnActor", "Spawn_Generation", "Jump");      // ...and Ctrl+A, Delete, all before a restore ran
        foreach (var run in queued) run();

        var (st, mode) = Assert.Single(ran);
        Assert.Equal(FilterViewRestore.Cleared, mode);
        Assert.Equal("Spawn_Generation", Assert.Single(st.Selected));
    }
}
