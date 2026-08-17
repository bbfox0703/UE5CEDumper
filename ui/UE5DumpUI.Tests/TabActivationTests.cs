using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Audit #5 AF5 — the tab-activation source guard.
///
/// <para>Avalonia's <c>SelectionChanged</c> bubbles. A ComboBox or ListBox
/// inside the selected tab raises it and it reaches the main TabControl, whose
/// handler then re-runs the whole per-tab activation routine — cancelling
/// in-flight work and rebuilding lists — because the user picked an item in the
/// tab they were already on.</para>
///
/// <para><c>sender</c> cannot tell the two apart: it is the TabControl either
/// way, which is precisely why the old code could not. <c>e.Source</c> can.</para>
///
/// <para>Pure by construction (both parameters are <c>object?</c>), so this
/// needs no toolkit — the same shape as <see cref="LiveWalkerNavShortcuts"/>.</para>
/// </summary>
public class TabActivationTests
{
    // Plain sentinels: the predicate is reference identity, so nothing about a
    // real control matters here.
    private static readonly object TabControl = new();
    private static readonly object ChildSelector = new();

    [Fact]
    public void EventFromTheTabControlItself_IsARealTabSwitch()
    {
        Assert.True(TabActivation.ShouldRunActivation(TabControl, TabControl));
    }

    /// <summary>The regression this guard exists for: a ComboBox / ListBox inside
    /// the current tab bubbling its own SelectionChanged up to the TabControl.</summary>
    [Fact]
    public void EventBubbledFromAChildSelector_IsNotATabSwitch()
    {
        Assert.False(TabActivation.ShouldRunActivation(ChildSelector, TabControl));
    }

    /// <summary>An unknown source must not activate. "Probably a tab switch" is
    /// the guess this exists to remove.</summary>
    [Theory]
    [InlineData(true,  false)]
    [InlineData(false, true)]
    [InlineData(true,  true)]
    public void UnknownSourceOrTarget_NeverActivates(bool nullSource, bool nullTab)
    {
        var source = nullSource ? null : ChildSelector;
        var tab    = nullTab    ? null : TabControl;

        Assert.False(TabActivation.ShouldRunActivation(source, tab));
    }

    /// <summary>Identity, not equality — a control with a custom Equals must not
    /// be able to impersonate the TabControl.</summary>
    [Fact]
    public void EqualButDistinctObject_DoesNotCountAsTheTabControl()
    {
        var a = new AlwaysEqual();
        var b = new AlwaysEqual();

        Assert.True(a.Equals(b));                                   // the trap
        Assert.False(TabActivation.ShouldRunActivation(a, b));      // …not taken
        Assert.True(TabActivation.ShouldRunActivation(a, a));
    }

    private sealed class AlwaysEqual
    {
        public override bool Equals(object? obj) => obj is AlwaysEqual;
        public override int GetHashCode() => 1;
    }
}
