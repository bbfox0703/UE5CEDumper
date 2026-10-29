using System;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Pure, dependency-free decision helpers for the auto-snapshot loop
/// (<see cref="ViewModels.SnapshotViewModel"/>). Kept out of the ViewModel so the
/// intricate pacing / retention / quota-growth rules are unit-testable without
/// driving a live timer loop.
/// </summary>
public static class AutoSnapshotPlanner
{
    private const long BytesPerMb = 1024L * 1024;

    /// <summary>Byte sizes of the quota presets, ascending. <c>SnapshotViewModel.QuotaOptions</c> is built from this
    /// list ([W1-QUOTA-UNLIMITED]: a raise to a preset the combo could not show would be written back lowered).</summary>
    public static readonly long[] QuotaPresetBytes =
    {
        512L * BytesPerMb,
        1024L * BytesPerMb,
        2048L * BytesPerMb,
        5120L * BytesPerMb,
    };

    /// <summary>Seconds to wait AFTER a capture ends before the next one begins:
    /// <c>max(intervalSec − captureDurationSec, minGapSec)</c>. This one formula
    /// satisfies both requirements — it auto-extends the effective interval when a
    /// capture runs longer than the interval, and always leaves at least
    /// <paramref name="minGapSec"/> of idle time (the game-impact breather).</summary>
    public static int NextGapSeconds(int intervalSec, double captureDurationSec, int minGapSec)
    {
        if (minGapSec < 0) minGapSec = 0;
        double remaining = intervalSec - captureDurationSec;
        double gap = Math.Max(remaining, minGapSec);
        int rounded = (int)Math.Round(gap, MidpointRounding.AwayFromZero);
        return Math.Max(rounded, minGapSec);
    }

    /// <summary>Decide whether the loop should stop itself after a capture cycle.
    /// <paramref name="autoCapturedCount"/> is the number of captures completed this
    /// session (already incremented for the just-finished one).
    /// <paramref name="lastSnapshotBytes"/> is the estimated size of the newest
    /// snapshot; <paramref name="quotaBytes"/> is the current byte quota (0 =
    /// unlimited).</summary>
    public static AutoStopReason EvaluatePostCapture(
        AutoSnapshotRetentionMode mode, int desiredCount, int autoCapturedCount,
        long lastSnapshotBytes, long quotaBytes, bool adjustQuota)
    {
        // The quota structurally can't retain more than one snapshot while the user
        // wants to keep several, and we're not allowed to grow it → no progress
        // possible, stop. (quotaBytes/2 avoids overflow on 2×lastSnapshotBytes.)
        if (!adjustQuota && quotaBytes > 0 && desiredCount > 1 &&
            lastSnapshotBytes > 0 && lastSnapshotBytes > quotaBytes / 2)
            return AutoStopReason.QuotaHoldsOne;

        if (mode == AutoSnapshotRetentionMode.FixedCount && autoCapturedCount >= desiredCount)
            return AutoStopReason.ReachedCount;

        return AutoStopReason.None;
    }

    /// <summary>When auto-adjust-quota is on, compute the quota (bytes) needed to
    /// retain <paramref name="desiredCount"/> snapshots of the observed size, chosen
    /// from the presets and only ever RAISED above <paramref name="currentQuotaBytes"/>.
    /// Returns <c>null</c> when even the largest preset is too small → caller should
    /// set "Unlimited". Returns <paramref name="currentQuotaBytes"/> unchanged when no
    /// growth is needed or the size can't be estimated. The disk-space guard remains
    /// the real ceiling regardless.</summary>
    public static long? RaiseQuotaBytes(long currentQuotaBytes, int desiredCount, long lastSnapshotBytes)
    {
        if (currentQuotaBytes <= 0) return currentQuotaBytes;   // already unlimited — never lower
        if (lastSnapshotBytes <= 0 || desiredCount <= 0) return currentQuotaBytes;

        long needed = (long)(desiredCount * lastSnapshotBytes * 1.2);   // 20% headroom
        if (needed <= currentQuotaBytes) return currentQuotaBytes;      // already big enough

        foreach (var preset in QuotaPresetBytes)
            if (preset > currentQuotaBytes && preset >= needed)
                return preset;
        return null;   // exceeds every preset → Unlimited
    }
}
