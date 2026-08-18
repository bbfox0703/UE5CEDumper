using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Pins the X7 fix: the "game thread paused" banner is now one combined latch fed
/// by every per-response observation from both lanes, instead of two independent
/// per-lane edge latches. The decisive test is the resume case that the old code
/// got wrong — one lane sees the pause, then goes idle while the OTHER lane carries
/// traffic after the game resumes.
/// </summary>
public class GameThreadStalledLevelTests
{
    [Fact]
    public void ClearsOnResume_WhenTheOtherLaneCarriesTrafficAfterAnIdleLaneSawThePause()
    {
        var level = new GameThreadStalledLevel();

        // Bulk lane observes the pause mid-scan → raise ON.
        Assert.Equal((bool?)true, level.Observe(true));

        // Bulk goes idle. After the game resumes the interactive lane observes
        // "not stalled". With two independent per-lane latches this never raised
        // (the interactive lane's own last value was already false), so the banner
        // stuck ON. One combined latch clears it.
        Assert.Equal((bool?)false, level.Observe(false));
    }

    [Fact]
    public void DedupesRepeatObservations_SoAPagingBurstDoesNotSpam()
    {
        var level = new GameThreadStalledLevel();
        Assert.Equal((bool?)true, level.Observe(true));
        Assert.Null(level.Observe(true));    // unchanged → no raise
        Assert.Null(level.Observe(true));
        Assert.Equal((bool?)false, level.Observe(false));
        Assert.Null(level.Observe(false));
    }

    [Fact]
    public void NegativeControl_FreshLevelIsNotStalled_SoAFirstNotStalledDoesNotRaise()
    {
        var level = new GameThreadStalledLevel();
        Assert.Null(level.Observe(false));   // already false — no spurious OFF
    }

    [Fact]
    public void Reset_MakesTheFirstObservationRaiseAgain()
    {
        var level = new GameThreadStalledLevel();
        Assert.Equal((bool?)true, level.Observe(true));
        Assert.Null(level.Observe(true));    // steady ON

        level.Reset();                       // (re)connect

        // Without Reset this would be suppressed as unchanged; after Reset the
        // first post-connect observation must raise even if the game was already paused.
        Assert.Equal((bool?)true, level.Observe(true));
    }
}
