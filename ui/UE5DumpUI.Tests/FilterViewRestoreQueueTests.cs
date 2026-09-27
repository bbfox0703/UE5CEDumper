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
