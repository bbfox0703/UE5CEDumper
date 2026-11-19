using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Pure tests for the GObjects-drift consistency bound that flags a snapshot
/// whose capture spanned a level transition / mass spawn-free. The threshold is
/// deliberately generous so ordinary gameplay churn never false-flags a good
/// capture (a false positive would auto-delete the user's good offline data).
/// </summary>
public class SnapshotConsistencyTests
{
    [Fact]
    public void StableCount_IsNotSuspect()
    {
        // An Elliot-sized stable capture: every chunk reports total=390017 (no churn).
        Assert.False(SnapshotConsistency.IsDriftSuspect(390017, 390017, 390017));
    }

    [Fact]
    public void SmallChurn_IsNotSuspect()
    {
        // A few hundred objects spawning/despawning during a large-game capture is
        // ordinary frame churn — well under the 2% / 2000-floor bound.
        Assert.False(SnapshotConsistency.IsDriftSuspect(390017, 389900, 390400));
    }

    [Fact]
    public void LevelTransitionSizedGrowth_IsSuspect()
    {
        // A level load adds thousands of objects → count grows past begin+threshold.
        Assert.True(SnapshotConsistency.IsDriftSuspect(390017, 390017, 410000));
    }

    [Fact]
    public void DriftBelowBegin_IsSuspect()
    {
        // GC compaction / level unload can also shrink the count by a large margin.
        Assert.True(SnapshotConsistency.IsDriftSuspect(390017, 360000, 390017));
    }

    [Fact]
    public void SmallGame_UsesAbsoluteFloor()
    {
        // On a tiny game (begin=5000) the 2% fraction (100) is below the 2000 floor,
        // so a 1500 swing is still NOT suspect, but a 2500 swing IS.
        Assert.Equal(2000, SnapshotConsistency.DriftThreshold(5000));
        Assert.False(SnapshotConsistency.IsDriftSuspect(5000, 5000, 6500));
        Assert.True(SnapshotConsistency.IsDriftSuspect(5000, 5000, 7600));
    }

    [Fact]
    public void LargeGame_UsesFraction()
    {
        // begin=500000 → 2% = 10000 exceeds the floor, so the bound scales up.
        Assert.Equal(10000, SnapshotConsistency.DriftThreshold(500000));
    }

    [Fact]
    public void NonPositiveBegin_NeverSuspect()
    {
        // An empty / early-failed capture (begin<=0) has nothing to compare — never flag.
        Assert.False(SnapshotConsistency.IsDriftSuspect(0, 0, 999999));
    }
}
