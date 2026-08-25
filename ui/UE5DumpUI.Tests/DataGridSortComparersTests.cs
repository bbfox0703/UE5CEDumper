using System.Collections;
using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Unit tests for the AOT-safe DataGrid sort comparer factories
/// (<see cref="DataGridSortComparers"/>). These back the column-header sort
/// on every result grid whose sort property isn't rooted by a column-level
/// Binding (template columns + mismatched-SortMemberPath text columns) — the
/// reflection-free path that survives Native-AOT trimming (aot-pitfalls.md
/// §4.5). The factories are pure (delegate-driven), so we exercise the
/// returned IComparer directly without a DataGrid.
/// </summary>
public class DataGridSortComparersTests
{
    private sealed record Row(int N, string S, double D, bool B, ulong H);

    private static Row R(int n = 0, string s = "", double d = 0, bool b = false, ulong h = 0)
        => new(n, s, d, b, h);

    [Fact]
    public void Number_OrdersByIntegerKey()
    {
        IComparer c = DataGridSortComparers.Number<Row>(r => r.N);
        Assert.True(c.Compare(R(n: 1), R(n: 2)) < 0);
        Assert.True(c.Compare(R(n: 5), R(n: 2)) > 0);
        Assert.Equal(0, c.Compare(R(n: 3), R(n: 3)));
        // The DIGIT BOUNDARY, which is the only place a numeric column and a string
        // column disagree: "10" sorts BEFORE "9" as text, after it as a number. This is
        // the offline substitute for AF16's open residual, which asked for a live field
        // with >=10 references to tell the two apart. There is no string path to find --
        // PropertyXrefDialog.cs:40 wires this very comparer over PropertyXref.Occurrences,
        // an int -- so the residual was unreachable by construction and is closed here
        // instead of by a game launch. [AF16-BYCONSTRUCTION-2026-08-24]
        Assert.True(c.Compare(R(n: 9), R(n: 10)) < 0);
        Assert.True(c.Compare(R(n: 10), R(n: 9)) > 0);
    }

    [Fact]
    public void Ordinal_IsCaseInsensitive_AndAlphabetical()
    {
        IComparer c = DataGridSortComparers.Ordinal<Row>(r => r.S);
        Assert.Equal(0, c.Compare(R(s: "abc"), R(s: "ABC")));
        Assert.True(c.Compare(R(s: "apple"), R(s: "Banana")) < 0);
    }

    [Fact]
    public void Double_OrdersByFloatingKey()
    {
        IComparer c = DataGridSortComparers.Double<Row>(r => r.D);
        Assert.True(c.Compare(R(d: 1.5), R(d: 2.5)) < 0);
        Assert.True(c.Compare(R(d: 9.0), R(d: 8.99)) > 0);
    }

    [Fact]
    public void Bool_OrdersFalseBeforeTrue()
    {
        IComparer c = DataGridSortComparers.Bool<Row>(r => r.B);
        Assert.True(c.Compare(R(b: false), R(b: true)) < 0);
        Assert.Equal(0, c.Compare(R(b: true), R(b: true)));
    }

    [Fact]
    public void Hex_OrdersByUnsignedValue()
    {
        IComparer c = DataGridSortComparers.Hex<Row>(r => r.H);
        Assert.True(c.Compare(R(h: 0x10), R(h: 0x20)) < 0);
        // A high-bit-set address must order ABOVE a small one (unsigned, not
        // a sign-flipped negative).
        Assert.True(c.Compare(R(h: 0xFFFFFFFFFFFFFFFF), R(h: 1)) > 0);
    }

    [Fact]
    public void Comparers_ReturnZero_OnTypeMismatch()
    {
        // The grid feeds the comparer boxed row objects; a foreign type must
        // be treated as equal (no throw) rather than crashing the sort.
        IComparer c = DataGridSortComparers.Number<Row>(r => r.N);
        Assert.Equal(0, c.Compare("not a row", 123));
        Assert.Equal(0, c.Compare(R(n: 1), "x"));
    }
}
