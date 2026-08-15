using UE5DumpUI.ViewModels;
using UE5DumpUI.Views;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Property Search "Force → value…" literal handling — audit #5 AF6.
///
/// The prompt used to return <c>double?</c>, so a value the Freeze dialog had already
/// validated per-type but the caller could not convert came back as null — the same
/// answer as pressing Cancel. Force then returned silently and the user was told
/// nothing at all.
///
/// The precision half is the more dangerous one and was not in the finding:
/// <c>double.TryParse</c> SUCCEEDS on 9223372036854775807 and yields
/// 9223372036854775808, so a wide Int64Property would have been held at a number the
/// user never typed, indefinitely, by the re-assert worker.
/// </summary>
public class ForceValuePromptTests
{
    [Theory]
    [InlineData("42", 42d)]
    [InlineData("-7", -7d)]
    [InlineData("0", 0d)]
    [InlineData("100.5", 100.5d)]
    [InlineData("-0.25", -0.25d)]
    [InlineData("  37  ", 37d)]           // the dialog does not trim for us
    [InlineData("9007199254740992", 9007199254740992d)]   // 2^53 — the last exact integer
    public void AcceptsValuesADoubleCanCarry(string literal, double expected)
    {
        var r = PropertySearchPanel.ParseForceLiteral(literal);

        Assert.False(r.Cancelled);
        Assert.Null(r.Error);
        Assert.Equal(expected, r.Value);
    }

    // The rule: a double holds every integer up to 2^53 exactly, and past that only the
    // multiples of its exponent's step. Each case below states which side of that it lands
    // on. Written as explicit expectations rather than a computed oracle on purpose — every
    // concise way to compute "does this survive a double" is itself wrong at the ends of the
    // range: (long)(double)v SATURATES, and (decimal)(double)v rounds to 15 significant
    // digits. A test whose oracle has the same bug as the code proves nothing.
    [Theory]
    // long.MaxValue: 2^63-1, an odd number far above 2^53 — becomes 2^63. Rejected.
    [InlineData("9223372036854775807", true)]
    // 2^53 + 1: the first integer a double cannot hold. Rejected.
    [InlineData("9007199254740993", true)]
    // long.MinValue: -2^63, a power of two, so it IS exact. Accepted.
    [InlineData("-9223372036854775808", false)]
    // 2^62: also a power of two, well above 2^53, and also exact. Accepted.
    [InlineData("4611686018427387904", false)]
    public void WideIntegersAreJudgedByWhetherADoubleHoldsThemExactly(
        string literal, bool expectRejected)
    {
        var r = PropertySearchPanel.ParseForceLiteral(literal);

        Assert.False(r.Cancelled);   // NOT reported as a cancel either way — the whole point
        if (expectRejected)
        {
            Assert.NotNull(r.Error);
            Assert.Contains("Refused", r.Error);
        }
        else
        {
            Assert.Null(r.Error);
        }
    }

    [Fact]
    public void TheRejectedValueIsNeverSilentlySubstituted()
    {
        // 2^53 + 1 lands one step past exactness and stays inside the long range, so the
        // substitute can be named precisely. "It didn't work" is not actionable when the
        // number the user typed looks perfectly reasonable.
        var r = PropertySearchPanel.ParseForceLiteral("9007199254740993");

        Assert.NotNull(r.Error);
        Assert.Contains("9007199254740993", r.Error);   // what you asked for
        Assert.Contains("9007199254740992", r.Error);   // what it would actually have held
    }

    // NOT covered here on purpose: "12,34,56" is ACCEPTED as 123456. NumberStyles.Any
    // includes AllowThousands, which is the behaviour this path has always had and is
    // unchanged by AF6 — pinning it either way would be asserting a decision nobody made.
    [Theory]
    [InlineData("abc")]
    [InlineData("0x1F")]        // hex is not accepted by the numeric styles used here
    [InlineData("--5")]
    public void RejectsUnparseableLiteralsWithAReason(string literal)
    {
        var r = PropertySearchPanel.ParseForceLiteral(literal);

        Assert.False(r.Cancelled);
        Assert.NotNull(r.Error);
        Assert.Contains(literal, r.Error);   // echo it back — the user cannot see our parser
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void RejectsNonFiniteValuesThatWouldReachGameMemory(string literal)
    {
        // These parse cleanly under NumberStyles.Any and would be written into a live
        // float/double field and then HELD there by the re-assert worker.
        var r = PropertySearchPanel.ParseForceLiteral(literal);

        Assert.NotNull(r.Error);
        Assert.Contains("finite", r.Error);
    }

    [Fact]
    public void CancelAndRejectAreDistinguishable()
    {
        var cancel = ForceValuePromptResult.Cancel();
        var reject = ForceValuePromptResult.Reject("nope");
        var accept = ForceValuePromptResult.Accept(5);

        Assert.True(cancel.Cancelled);
        Assert.Null(cancel.Error);

        Assert.False(reject.Cancelled);
        Assert.Equal("nope", reject.Error);

        Assert.False(accept.Cancelled);
        Assert.Null(accept.Error);
        Assert.Equal(5, accept.Value);
    }
}
