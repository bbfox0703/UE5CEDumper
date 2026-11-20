using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LIVEFUNCS-UI-SNAP-MEMORY] What a Call Trace load keeps of its parameter copies: a decoded value that recurs is held
/// once, and past the decoded budget the oldest calls keep only their raw bytes. Every copy here is built afresh, as
/// each page's parse builds it, so sharing is the keeper's doing and never the fixture's.
/// </summary>
public class SnapCopyKeeperTests
{
    private static SnapValue Leaf(string text, SnapMark mark = SnapMark.Exact) => new() { Text = text, Mark = mark };

    /// <summary>A struct parameter of <paramref name="members"/> fields, each "i" plus <paramref name="salt"/>.</summary>
    private static SnapValue Struct(int members, string salt = "")
        => new() { Text = "(...)", Sub = Enumerable.Range(0, members).Select(i => Leaf($"{i}{salt}")).ToList() };

    private static SnapSlot Slot(ulong seq, IReadOnlyList<SnapValue>? values, byte fill = 1, int len = 64) => new()
    {
        EntrySeq = seq, Index = seq, Values = values, Data = Enumerable.Repeat(fill, len).ToArray(), Len = len,
    };

    [Fact]
    public void Two_identical_copies_share_one_decoded_tree_and_one_byte_array()
    {
        var k = new SnapCopyKeeper(long.MaxValue);
        k.Add(Slot(1, new[] { Leaf("7"), Struct(5) }));
        k.Add(Slot(2, new[] { Leaf("7"), Struct(5) }));
        var a = k.Slots[0];
        var b = k.Slots[1];
        Assert.Same(a.Values, b.Values);
        Assert.Same(a.Values![1].Sub, b.Values![1].Sub);
        Assert.Same(a.Data, b.Data);
        Assert.Equal("4", b.Values[1].Sub[4].Text);
    }

    [Fact]
    public void A_copy_that_differs_in_one_field_still_shares_what_it_has_in_common()
    {
        var k = new SnapCopyKeeper(long.MaxValue);
        k.Add(Slot(1, new[] { Leaf("7"), Struct(5) }));
        k.Add(Slot(2, new[] { Leaf("8"), Struct(5) }, fill: 2));
        var a = k.Slots[0].Values!;
        var b = k.Slots[1].Values!;
        Assert.NotSame(a, b);
        Assert.Equal("7", a[0].Text);
        Assert.Equal("8", b[0].Text);
        Assert.Same(a[1], b[1]);                     // the struct they agree on
        Assert.NotSame(k.Slots[0].Data, k.Slots[1].Data);
    }

    [Fact]
    public void The_same_text_with_another_mark_is_another_value()
    {
        var k = new SnapCopyKeeper(long.MaxValue);
        k.Add(Slot(1, new[] { Leaf("Hero (BP_Hero_C)", SnapMark.Now) }));
        k.Add(Slot(2, new[] { Leaf("Hero (BP_Hero_C)", SnapMark.Gone) }));
        Assert.Equal(SnapMark.Now, k.Slots[0].Values![0].Mark);
        Assert.Equal(SnapMark.Gone, k.Slots[1].Values![0].Mark);
    }

    [Fact]
    public void A_repeated_copy_adds_almost_nothing_to_what_is_counted()
    {
        var k = new SnapCopyKeeper(long.MaxValue);
        k.Add(Slot(1, new[] { Struct(400) }));
        long first = k.DecodedBytes;
        for (ulong s = 2; s <= 100; s++) k.Add(Slot(s, new[] { Struct(400) }));
        Assert.True(first > 400 * 40, $"one 400-field copy counted {first} bytes");
        Assert.True(k.DecodedBytes - first < first / 10, $"99 repeats added {k.DecodedBytes - first} to {first}");
    }

    [Fact]
    public void Past_the_budget_the_oldest_calls_keep_only_their_raw_bytes()
    {
        var probe = new SnapCopyKeeper(long.MaxValue);
        probe.Add(Slot(1, new[] { Struct(50, "a") }));
        long one = probe.DecodedBytes;

        var k = new SnapCopyKeeper(one * 3 + one / 2);   // room for three distinct copies
        for (ulong s = 1; s <= 6; s++) k.Add(Slot(s, new[] { Struct(50, "s" + s) }, fill: (byte)s));
        Assert.True(k.DecodedBytes <= k.DecodedBudgetBytes, $"{k.DecodedBytes} counted against {k.DecodedBudgetBytes}");
        Assert.Equal(3, k.Dropped);
        foreach (var s in k.Slots.Take(3))
        {
            Assert.Null(s.Values);
            Assert.True(s.ValuesDropped);
            Assert.Equal(64, s.Data.Length);            // the raw copy stays
        }
        foreach (var s in k.Slots.Skip(3))
        {
            Assert.NotNull(s.Values);
            Assert.False(s.ValuesDropped);
        }
    }

    [Fact]
    public void Oldest_means_the_earliest_call_whatever_ring_brought_it_first()
    {
        var probe = new SnapCopyKeeper(long.MaxValue);
        probe.Add(Slot(1, new[] { Struct(50, "a") }));
        long one = probe.DecodedBytes;

        var k = new SnapCopyKeeper(one * 2 + one / 2);
        k.Add(Slot(10, new[] { Struct(50, "x") }));     // ring 0: later calls
        k.Add(Slot(11, new[] { Struct(50, "y") }));
        k.Add(Slot(3, new[] { Struct(50, "z") }));      // ring 1: an earlier call, read after
        Assert.Equal(1, k.Dropped);
        Assert.True(k.Slots.Single(s => s.EntrySeq == 3).ValuesDropped);
        Assert.NotNull(k.Slots.Single(s => s.EntrySeq == 10).Values);
    }

    [Fact]
    public void A_slot_without_values_is_kept_as_it_came()
    {
        var k = new SnapCopyKeeper(long.MaxValue);
        k.Add(new SnapSlot { EntrySeq = 1, RawOnly = "not_read_before_stop", Data = new byte[] { 1, 2 } });
        Assert.Null(k.Slots[0].Values);
        Assert.False(k.Slots[0].ValuesDropped);
        Assert.Equal("not_read_before_stop", k.Slots[0].RawOnly);
    }

    /// <summary>The measured case in small: a large struct parameter that does not change between calls. Without
    /// sharing, 4,000 copies of 400 fields hold about 4,000 x 400 values; shared, every copy is the same tree and the same
    /// bytes, and the count is one copy's. Checked by identity, not by the heap's size, which tests running beside this
    /// one move.</summary>
    [Fact]
    public void Four_thousand_copies_of_an_unchanging_400_field_struct_hold_one_tree()
    {
        var k = new SnapCopyKeeper(long.MaxValue);
        k.Add(Slot(1, new[] { Leaf("0.016"), Struct(400) }, len: 1984));
        long one = k.DecodedBytes, raw = k.RawBytes;
        for (ulong s = 2; s <= 4000; s++) k.Add(Slot(s, new[] { Leaf("0.016"), Struct(400) }, len: 1984));
        var first = k.Slots[0];
        Assert.All(k.Slots, s => Assert.Same(first.Values, s.Values));
        Assert.All(k.Slots, s => Assert.Same(first.Data, s.Data));
        Assert.Equal(one, k.DecodedBytes);
        Assert.Equal(raw, k.RawBytes);
    }
}
