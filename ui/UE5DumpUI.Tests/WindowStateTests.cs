using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

public class WindowStateTests
{
    // ────────────────────────────────────────────────────────────────
    // WindowStateStore — Format / Parse round-trip (pure, no IO)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void FormatParse_RoundTrips_Normal()
    {
        var rec = new WindowStateRecord(120, 240, 1400, 900, Maximized: false);
        var parsed = WindowStateStore.Parse(WindowStateStore.Format(rec));
        Assert.Equal(rec, parsed);
    }

    [Fact]
    public void FormatParse_RoundTrips_Maximized()
    {
        var rec = new WindowStateRecord(-1920, 0, 1600.5, 1000.25, Maximized: true);
        var parsed = WindowStateStore.Parse(WindowStateStore.Format(rec));
        Assert.Equal(rec, parsed);
    }

    [Fact]
    public void FormatParse_NegativeCoords_RoundTrip()
    {
        // A window on a left-hand second monitor saves negative X — must survive.
        var rec = new WindowStateRecord(-3000, -120, 1280, 720, Maximized: false);
        var parsed = WindowStateStore.Parse(WindowStateStore.Format(rec));
        Assert.Equal(rec, parsed);
    }

    [Fact]
    public void Parse_Empty_ReturnsNull()
    {
        Assert.Null(WindowStateStore.Parse(System.Array.Empty<string>()));
    }

    [Fact]
    public void Parse_MissingFields_ReturnsNull()
    {
        // No width / height → unusable.
        Assert.Null(WindowStateStore.Parse(new[] { "x=10", "y=20" }));
    }

    [Fact]
    public void Parse_NonPositiveSize_ReturnsNull()
    {
        Assert.Null(WindowStateStore.Parse(new[] { "x=10", "y=20", "w=0", "h=0", "max=0" }));
    }

    [Fact]
    public void Parse_Corrupt_ReturnsNull()
    {
        Assert.Null(WindowStateStore.Parse(new[] { "garbage", "not=a=number", "w=abc" }));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    public void Parse_MaximizedFlag_Variants(string val, bool expected)
    {
        var rec = WindowStateStore.Parse(new[] { "x=0", "y=0", "w=800", "h=600", $"max={val}" });
        Assert.NotNull(rec);
        Assert.Equal(expected, rec!.Value.Maximized);
    }

    [Fact]
    public void Parse_IgnoresCommentsAndBlankLines()
    {
        var rec = WindowStateStore.Parse(new[] { "# comment", "", "  ", "x=5", "y=6", "w=800", "h=600" });
        Assert.NotNull(rec);
        Assert.Equal(5, rec!.Value.X);
        Assert.Equal(6, rec.Value.Y);
    }

    // ────────────────────────────────────────────────────────────────
    // WindowPlacement.IsVisibleEnough — off-screen reset rule
    // ────────────────────────────────────────────────────────────────

    private static readonly List<(int, int, int, int)> SinglePrimary = new() { (0, 0, 1920, 1080) };

    [Fact]
    public void Visible_FullyOnScreen_True()
    {
        Assert.True(WindowPlacement.IsVisibleEnough(100, 100, 1400, 900, SinglePrimary));
    }

    [Fact]
    public void Visible_CompletelyOffToTheRight_False()
    {
        // Saved on a second monitor at x=3000 that no longer exists.
        Assert.False(WindowPlacement.IsVisibleEnough(3000, 100, 1400, 900, SinglePrimary));
    }

    [Fact]
    public void Visible_OnlySliverShowing_False()
    {
        // Just 10 px peeking in from the left edge — not grabbable.
        Assert.False(WindowPlacement.IsVisibleEnough(-1390, 100, 1400, 900, SinglePrimary));
    }

    [Fact]
    public void Visible_GrabbableChunkShowing_True()
    {
        // 120 px on-screen (== MinVisibleWidth) with full-height overlap → reachable.
        Assert.True(WindowPlacement.IsVisibleEnough(-1280, 100, 1400, 900, SinglePrimary));
    }

    [Fact]
    public void Visible_SecondMonitorRemovedVsPresent()
    {
        // Window saved on the second monitor (1920..3840).
        var onSecond = (2000, 100, 1400, 900);

        // Second monitor gone → not visible → caller resets.
        Assert.False(WindowPlacement.IsVisibleEnough(
            onSecond.Item1, onSecond.Item2, onSecond.Item3, onSecond.Item4, SinglePrimary));

        // Second monitor present → visible → keep placement.
        var dual = new List<(int, int, int, int)> { (0, 0, 1920, 1080), (1920, 0, 1920, 1080) };
        Assert.True(WindowPlacement.IsVisibleEnough(
            onSecond.Item1, onSecond.Item2, onSecond.Item3, onSecond.Item4, dual));
    }

    [Fact]
    public void Visible_NoScreens_False()
    {
        Assert.False(WindowPlacement.IsVisibleEnough(0, 0, 1400, 900, new List<(int, int, int, int)>()));
    }

    // ────────────────────────────────────────────────────────────────
    // WindowPlacement.CenterIn
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void CenterIn_CentersWithinScreen()
    {
        var (x, y) = WindowPlacement.CenterIn((0, 0, 1920, 1080), 1400, 900);
        Assert.Equal(260, x); // (1920-1400)/2
        Assert.Equal(90, y);  // (1080-900)/2
    }

    [Fact]
    public void CenterIn_WindowLargerThanScreen_ClampsToOrigin()
    {
        var (x, y) = WindowPlacement.CenterIn((0, 0, 800, 600), 1400, 900);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void CenterIn_RespectsScreenOffset()
    {
        var (x, y) = WindowPlacement.CenterIn((1920, 0, 1920, 1080), 1400, 900);
        Assert.Equal(1920 + 260, x);
        Assert.Equal(90, y);
    }

    // ── AF21: the guard must be given the PHYSICAL width, not the DIP width ──────────
    //
    // MainWindow.OnOpenedValidatePlacement computes
    //     int rw = (int)Math.Round(_normalWidth * scale);
    // and the `* scale` IS the AF21 fix. IsVisibleEnough itself was never wrong; it was being
    // handed a rect two-and-a-bit times too narrow on a HiDPI monitor.
    //
    // These pin the CONSEQUENCE, using this machine's real numbers (3840 px wide, 225%, a
    // 1,124-DIP window = 2,529 physical), because the live rig
    // tools/verify/af21_hidpi_placement.py derives its arms from exactly this arithmetic.

    private const double Af21Scale = 2.25;
    private const double Af21DipWidth = 1124.0;
    private static int Af21PhysWidth => (int)System.Math.Round(Af21DipWidth * Af21Scale);

    // ⚠ NOT SinglePrimary, which is 1920 wide. On a 1920 screen every right-edge probe below
    // lands entirely past the screen, both widths return false, and the "they agree" assertion
    // passes for the trivial reason instead of the real one. The live rig ran on 3840x2400 at
    // 225%; these must be the same screen or they are testing a different question.
    private static readonly List<(int, int, int, int)> Af21Screen = new() { (0, 0, 3840, 2400) };

    [Fact]
    public void Af21_OffTheLeft_TheDipWidthAndThePhysicalWidthDisagree()
    {
        // x = -1707 is the midpoint of the discriminating band the live rig uses.
        const int x = -1707;

        Assert.True(WindowPlacement.IsVisibleEnough(x, 146, Af21PhysWidth, 900, Af21Screen),
            "with the physical width the window is plainly reachable — 822 px of it is on screen");

        Assert.False(WindowPlacement.IsVisibleEnough(x, 146, (int)Af21DipWidth, 900, Af21Screen),
            "with the DIP width the same window reads as off-screen — this is the AF21 defect, "
            + "and its consequence was that a legitimate position got discarded");
    }

    [Fact]
    public void Af21_OffTheRight_TheyDoNotDisagree_SoTheRowsOwnStepCannotExposeIt()
    {
        // ⚠ The register row says to "move the window so roughly a third of it hangs off the RIGHT
        // edge". That step cannot reveal this defect and the arithmetic is why: off the right the
        // overlap is (screenW - x) for the physical rect but min(x + w, screenW) - x for the
        // narrower DIP rect — the DIP value is the LARGER one, so the buggy build is MORE
        // permissive there. Both accept, the tester sees a pass, and nothing was learned.
        int accepted = 0;
        foreach (int x in new[] { 2000, 2400, 2800, 3200 })
        {
            bool phys = WindowPlacement.IsVisibleEnough(x, 146, Af21PhysWidth, 900, Af21Screen);
            bool dip = WindowPlacement.IsVisibleEnough(x, 146, (int)Af21DipWidth, 900, Af21Screen);
            if (phys) accepted++;
            Assert.True(phys == dip,
                $"off the right at x={x} the two widths disagree, which would make the row's own "
                + "step a valid probe after all — re-check the correction recorded against AF21");
        }

        // Guard the guard: "they agree" is worthless if they agree on FALSE everywhere, which is
        // what happens the moment the screen is too narrow for these x values to mean anything.
        Assert.True(accepted >= 3,
            $"only {accepted} of the 4 right-edge probes were on screen at all — this assertion has"
            + " gone vacuous and is no longer evidence about the right edge");
    }

    [Fact]
    public void Af21_TheBandIsRealAndBounded()
    {
        // Below the band BOTH reject (genuinely off-screen — this is the live rig's arm C, the one
        // that shows the guard still rejects something and so can fail).
        const int belowBand = -2809;
        Assert.False(WindowPlacement.IsVisibleEnough(belowBand, 146, Af21PhysWidth, 900, Af21Screen));
        Assert.False(WindowPlacement.IsVisibleEnough(belowBand, 146, (int)Af21DipWidth, 900, Af21Screen));

        // Above the band BOTH accept (plainly on screen — arm A).
        Assert.True(WindowPlacement.IsVisibleEnough(200, 146, Af21PhysWidth, 900, Af21Screen));
        Assert.True(WindowPlacement.IsVisibleEnough(200, 146, (int)Af21DipWidth, 900, Af21Screen));
    }
}
