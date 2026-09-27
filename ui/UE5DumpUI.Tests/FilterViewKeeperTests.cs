using System.Collections.ObjectModel;
using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [KEYWORD-BOX-VIEW-KEEP] The shared keeper every client-side filter box uses: no rebuild for
/// the same rows, a capture BEFORE and a restore AFTER a real one, and "cleared" only for a
/// keyword that went from 2+ characters to empty (as last applied).
/// </summary>
public class FilterViewKeeperTests
{
    private sealed class Probe
    {
        public List<string> Log { get; } = new();
        public List<FilterViewRestore> Modes { get; } = new();

        public FilterViewKeeper Attach(ObservableCollection<string> list)
        {
            var k = new FilterViewKeeper();
            k.CaptureView = () => { Log.Add("capture:" + string.Join(",", list)); return new FilterViewState(new object[] { "b" }, "a"); };
            k.RestoreView = (_, mode) => { Log.Add("restore:" + string.Join(",", list)); Modes.Add(mode); };
            return k;
        }
    }

    [Fact]
    public void The_same_rows_are_not_rebuilt_and_the_view_is_not_touched()
    {
        var list = new ObservableCollection<string> { "a", "b" };
        var probe = new Probe();
        var k = probe.Attach(list);
        int detached = 0, resets = 0;
        list.CollectionChanged += (_, _) => resets++;

        bool changed = k.Update(list, new[] { "a", "b" }, () => detached++, "a ");

        Assert.False(changed);
        Assert.Equal(0, resets);
        Assert.Equal(0, detached);
        Assert.Empty(probe.Log);
    }

    [Fact]
    public void A_real_change_captures_before_and_restores_after()
    {
        var list = new ObservableCollection<string> { "a", "b", "c" };
        var probe = new Probe();
        var k = probe.Attach(list);
        int detached = 0;

        bool changed = k.Update(list, new[] { "b" }, () => detached++, "b");

        Assert.True(changed);
        Assert.Equal(1, detached);
        Assert.Equal(new[] { "capture:a,b,c", "restore:b" }, probe.Log);
        Assert.Equal(new[] { FilterViewRestore.Narrowed }, probe.Modes);
    }

    [Theory]
    [InlineData("I32", "", FilterViewRestore.Cleared)]
    [InlineData("I3", "   ", FilterViewRestore.Cleared)]
    [InlineData("I", "", FilterViewRestore.Narrowed)]       // 1 -> 0: indistinguishable from backspacing
    [InlineData("I32", "I3", FilterViewRestore.Narrowed)]
    [InlineData("", "I", FilterViewRestore.Narrowed)]
    public void Cleared_means_two_or_more_characters_to_empty(string from, string to, FilterViewRestore expected)
    {
        var list = new ObservableCollection<string> { "a" };
        var probe = new Probe();
        var k = probe.Attach(list);
        k.Update(list, new[] { "x" }, () => { }, from);
        probe.Modes.Clear();

        k.Update(list, new[] { "y" }, () => { }, to);

        Assert.Equal(new[] { expected }, probe.Modes);
    }

    [Fact]
    public void Cleared_is_judged_against_the_last_APPLIED_keyword()
    {
        // A skipped rebuild (same rows) still advances the applied keyword, and Forget() makes a
        // program-made clear an ordinary change.
        var list = new ObservableCollection<string> { "a" };
        var probe = new Probe();
        var k = probe.Attach(list);
        k.Update(list, new[] { "a" }, () => { }, "ab");      // same rows: skipped, but "ab" applied
        k.Update(list, new[] { "a", "b" }, () => { }, "");
        Assert.Equal(new[] { FilterViewRestore.Cleared }, probe.Modes);

        probe.Modes.Clear();
        k.Update(list, new[] { "a" }, () => { }, "ab");
        k.Forget();
        k.Update(list, new[] { "a", "b", "c" }, () => { }, "");
        Assert.Equal(new[] { FilterViewRestore.Narrowed, FilterViewRestore.Narrowed }, probe.Modes);
    }

    [Theory]
    [InlineData("abc", "x", "", "x", FilterViewRestore.Cleared)]     // one box cleared, the other kept
    [InlineData("abc", "x", "", "", FilterViewRestore.Narrowed)]     // two boxes changed at once
    [InlineData("abc", "x", "abc", "", FilterViewRestore.Narrowed)]  // the other box went 1 -> 0
    [InlineData("abc", "xy", "abc", "", FilterViewRestore.Cleared)]
    public void With_several_boxes_a_clear_is_one_box_emptied_while_the_rest_stay(
        string a0, string b0, string a1, string b1, FilterViewRestore expected)
    {
        var list = new ObservableCollection<string> { "a" };
        var probe = new Probe();
        var k = probe.Attach(list);
        k.Update(list, new[] { "x" }, () => { }, a0, b0);
        probe.Modes.Clear();

        k.Update(list, new[] { "y" }, () => { }, a1, b1);

        Assert.Equal(new[] { expected }, probe.Modes);
    }

    [Fact]
    public void The_rebuild_overload_skips_when_the_rows_did_not_change()
    {
        var list = new ObservableCollection<string> { "a" };
        var probe = new Probe();
        var k = probe.Attach(list);
        int rebuilds = 0;

        Assert.False(k.Update(() => rebuilds++, rowsChanged: false, "ab"));
        Assert.True(k.Update(() => rebuilds++, rowsChanged: true, ""));

        Assert.Equal(1, rebuilds);
        Assert.Equal(new[] { FilterViewRestore.Cleared }, probe.Modes);
    }

    [Fact]
    public void Without_a_View_a_change_still_rebuilds()
    {
        var list = new ObservableCollection<string> { "a", "b" };
        var k = new FilterViewKeeper();

        Assert.True(k.Update(list, new[] { "b" }, () => { }, "b"));
        Assert.Equal(new[] { "b" }, list);
    }
}
