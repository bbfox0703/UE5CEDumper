using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The live "what will this actually match" preview shown after a Between / SPC
/// absolute bound (build 1702). Verifies the user's driving example — Between
/// 11.5~13.2 under Round → integer fields match <c>12~13</c> while float fields
/// keep <c>11.5~13.2</c> — plus the per-mode coercion, scope adaptivity, the
/// whole-bound collapse, and the unparseable/empty fall-through to "".
/// </summary>
public class RoundModePreviewTests
{
    // ---- the headline case: fractional Between, mixed numeric (Both) ----
    [Theory]
    [InlineData(FloatRoundMode.Round, "→ int 12~13 · float 11.5~13.2")]
    [InlineData(FloatRoundMode.Trunc, "→ int 11~13 · float 11.5~13.2")]
    [InlineData(FloatRoundMode.Ceil,  "→ int 12~14 · float 11.5~13.2")]
    public void Between_FractionalBounds_BothScope_SplitsIntAndFloat(FloatRoundMode mode, string expected)
    {
        Assert.Equal(expected, RoundModePreview.Between("11.5", "13.2", mode, RoundModePreview.Scope.Both));
    }

    // ---- type-scoped surfaces only show their own interpretation ----
    [Theory]
    [InlineData(FloatRoundMode.Round, "→ 12~13")]
    [InlineData(FloatRoundMode.Trunc, "→ 11~13")]
    [InlineData(FloatRoundMode.Ceil,  "→ 12~14")]
    public void Between_IntOnlyScope_ShowsCoercedRange(FloatRoundMode mode, string expected)
    {
        Assert.Equal(expected, RoundModePreview.Between("11.5", "13.2", mode, RoundModePreview.Scope.IntOnly));
    }

    [Fact]
    public void Between_FloatOnlyScope_ShowsLiteralRange_ModeIndependent()
    {
        foreach (var mode in new[] { FloatRoundMode.Round, FloatRoundMode.Trunc, FloatRoundMode.Ceil })
            Assert.Equal("→ 11.5~13.2", RoundModePreview.Between("11.5", "13.2", mode, RoundModePreview.Scope.FloatOnly));
    }

    // ---- whole bounds: int == float, so Both collapses to one range ----
    [Fact]
    public void Between_WholeBounds_CollapsesToSingleRange()
    {
        Assert.Equal("→ 11~13", RoundModePreview.Between("11", "13", FloatRoundMode.Round, RoundModePreview.Scope.Both));
        // Trunc/Ceil of whole numbers are identities → same range.
        Assert.Equal("→ 11~13", RoundModePreview.Between("11", "13", FloatRoundMode.Ceil, RoundModePreview.Scope.Both));
    }

    // ---- reversed bounds are min/max-normalised (mirrors the match code) ----
    [Fact]
    public void Between_ReversedBounds_AreNormalised()
    {
        Assert.Equal("→ int 12~13 · float 11.5~13.2",
            RoundModePreview.Between("13.2", "11.5", FloatRoundMode.Round, RoundModePreview.Scope.Both));
    }

    // ---- negatives reduce toward the correct direction per mode ----
    [Fact]
    public void Between_Negative_Round_AwayFromZero()
    {
        // Round = away-from-zero: -11.5 → -12, -2.5 → -3. Normalised int range -12~-3.
        Assert.Equal("→ int -12~-3 · float -11.5~-2.5",
            RoundModePreview.Between("-11.5", "-2.5", FloatRoundMode.Round, RoundModePreview.Scope.Both));
    }

    [Fact]
    public void Between_Negative_TruncTowardZero()
    {
        // Trunc toward zero: -11.5 → -11, -2.5 → -2. Normalised int range -11~-2.
        Assert.Equal("→ int -11~-2 · float -11.5~-2.5",
            RoundModePreview.Between("-11.5", "-2.5", FloatRoundMode.Trunc, RoundModePreview.Scope.Both));
    }

    // ---- missing / unparseable bounds → empty (label hides) ----
    [Theory]
    [InlineData("", "13.2")]
    [InlineData("11.5", "")]
    [InlineData("abc", "13.2")]
    [InlineData("11.5", "xyz")]
    public void Between_BadBounds_ReturnsEmpty(string lo, string hi)
    {
        Assert.Equal("", RoundModePreview.Between(lo, hi, FloatRoundMode.Round, RoundModePreview.Scope.Both));
    }

    // ---- [W2-BETWEEN-PREVIEW] a bound the real parser refuses previews NOTHING ----
    // NumberStyles.Any read an FVector's "1,2,3" as the single number 123 and accepted "1,000", "(5)" and "5-" -- all of
    // which the DLL (std::stod / std::stoll, the whole string consumed) refuses. A preview of a number nobody will match
    // is worse than no preview.
    [Theory]
    [InlineData("1,2,3", "4,5,6")]   // an FVector / FRotator / FTransform bound -- NOT one number
    [InlineData("1,000", "2,000")]   // thousands separators
    [InlineData("(5)", "10")]        // an accounting-style negative
    [InlineData("5-", "10")]         // a trailing sign
    public void Between_BoundsTheDllRefuses_PreviewNothing(string lo, string hi)
    {
        foreach (var scope in new[] { RoundModePreview.Scope.Both, RoundModePreview.Scope.IntOnly, RoundModePreview.Scope.FloatOnly })
            Assert.Equal("", RoundModePreview.Between(lo, hi, FloatRoundMode.Round, scope));
    }

    // The control: SPC's absolute window is NOT read by the DLL -- SpcQueryViewModel's own Lo() / Hi() parse it, with
    // NumberStyles.Any -- so its preview keeps THAT grammar. Tightening SPC's preview would hide values SPC matches.
    [Fact]
    public void SpcAbsolute_KeepsTheSpcQuerysOwnGrammar()
    {
        Assert.Equal("→ 1000", RoundModePreview.SpcAbsolute("Exact", "1,000", "", FloatRoundMode.Round, RoundModePreview.Scope.Both));
        Assert.Equal("→ 1000~2000",
            RoundModePreview.SpcAbsolute("Between", "1,000", "2,000", FloatRoundMode.Round, RoundModePreview.Scope.Both));
        Assert.Equal("→ -5", RoundModePreview.SpcAbsolute("Exact", "(5)", "", FloatRoundMode.Round, RoundModePreview.Scope.Both));
        Assert.Equal("→ ≥-5", RoundModePreview.SpcAbsolute("≥", "(5)", "", FloatRoundMode.Round, RoundModePreview.Scope.Both));
    }

    // ---- SPC absolute kinds ----
    [Fact]
    public void SpcAbsolute_AnyValue_IsEmpty()
    {
        Assert.Equal("", RoundModePreview.SpcAbsolute("(any value)", "", "", FloatRoundMode.Round, RoundModePreview.Scope.Both));
    }

    [Fact]
    public void SpcAbsolute_Between_MatchesBetween()
    {
        Assert.Equal("→ int 12~13 · float 11.5~13.2",
            RoundModePreview.SpcAbsolute("Between", "11.5", "13.2", FloatRoundMode.Round, RoundModePreview.Scope.Both));
    }

    [Theory]
    [InlineData("Exact", "11.5", "", FloatRoundMode.Round, "→ int 12 · float 11.5")]
    [InlineData("Exact", "12",   "", FloatRoundMode.Round, "→ 12")]                 // whole → collapse
    [InlineData("≥",     "11.5", "", FloatRoundMode.Trunc, "→ int ≥11 · float ≥11.5")]
    [InlineData("≤",     "",     "13.2", FloatRoundMode.Ceil, "→ int ≤14 · float ≤13.2")]
    public void SpcAbsolute_PointKinds(string kind, string lo, string hi, FloatRoundMode mode, string expected)
    {
        Assert.Equal(expected, RoundModePreview.SpcAbsolute(kind, lo, hi, mode, RoundModePreview.Scope.Both));
    }

    [Fact]
    public void SpcAbsolute_AtLeast_BadBound_IsEmpty()
    {
        Assert.Equal("", RoundModePreview.SpcAbsolute("≥", "", "", FloatRoundMode.Round, RoundModePreview.Scope.Both));
    }

    // ---- per-row model recomputes reactively (notifications wired) ----
    [Fact]
    public void GroupSlotInput_BetweenPreview_ReactsToValueAndMode()
    {
        var row = new GroupSlotInput { ScanType = ValueScanType.Between, Value = "11.5", Value2 = "13.2" };
        var raised = new System.Collections.Generic.List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.Equal("→ int 12~13 · float 11.5~13.2", row.BetweenPreview);   // default Round

        row.RoundMode = FloatRoundMode.Trunc;
        Assert.Contains(nameof(GroupSlotInput.BetweenPreview), raised);
        Assert.Equal("→ int 11~13 · float 11.5~13.2", row.BetweenPreview);

        row.Value2 = "20";
        Assert.Equal("→ int 11~20 · float 11.5~20", row.BetweenPreview);
    }

    [Fact]
    public void GroupSlotInput_BetweenPreview_EmptyForNonBetween()
    {
        var row = new GroupSlotInput { ScanType = ValueScanType.Exact, Value = "11.5", Value2 = "13.2" };
        Assert.Equal("", row.BetweenPreview);
    }

    // ---- delegation: SnapshotNumeric must agree with the canonical math ----
    [Theory]
    [InlineData(11.5, FloatRoundMode.Round, 12.0)]
    [InlineData(11.5, FloatRoundMode.Trunc, 11.0)]
    [InlineData(11.5, FloatRoundMode.Ceil,  12.0)]
    [InlineData(13.0, FloatRoundMode.Ceil,  13.0)]   // whole returned unchanged
    public void CoerceIntTarget_MatchesAcrossLayers(double v, FloatRoundMode mode, double expected)
    {
        Assert.Equal(expected, RoundModePreview.CoerceIntTarget(v, mode));
        Assert.Equal(expected, UE5DumpUI.Services.SnapshotNumeric.CoerceIntTarget(v, mode));
    }
}
