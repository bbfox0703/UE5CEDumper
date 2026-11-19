namespace UE5DumpUI.Services;

/// <summary>
/// Pure, dependency-free free-disk-space guard for the snapshot DB drive. Before
/// ANY snapshot capture (manual or auto), the UI checks that the drive holding the
/// per-game SQLite DB has enough free space; if not, the write is refused so a
/// multi-GB capture can't fill the disk holding <c>%LOCALAPPDATA%</c>.
///
/// The required free space is the SMALLER of a percentage of the whole drive and an
/// absolute floor: defaults <see cref="DefaultMinPercent"/>% and
/// <see cref="DefaultMinGb"/> GB — e.g. a 1 TB drive → min(≈100 GB, 50 GB) = 50 GB;
/// a 200 GB drive → min(20 GB, 50 GB) = 20 GB. Unknown totals (0, from a test double
/// / non-Windows platform) collapse the percentage term to 0, so the guard never
/// blocks when it can't measure the drive.
/// </summary>
public static class SnapshotDiskGuard
{
    public const int DefaultMinPercent = 10;
    public const int DefaultMinGb = 50;

    private const long BytesPerGb = 1024L * 1024 * 1024;

    /// <summary>Free bytes the drive must have to allow a write: the SMALLER of the two
    /// thresholds. A 0 (or negative) threshold DISABLES that term (treated as "no
    /// constraint"), so <c>minPercent</c> 0 → the GB floor alone, <c>minGb</c> 0 → the
    /// percentage alone, both 0 → no guard. A 0/unknown <paramref name="totalBytes"/>
    /// also disables the percentage term (it can't be computed). Returns 0 when every
    /// term is disabled (the guard never blocks).</summary>
    public static long RequiredFreeBytes(long totalBytes, int minPercent, int minGb)
    {
        // /100 first avoids overflow on multi-TB drives; long.MaxValue = "term disabled".
        long pct = (minPercent > 0 && totalBytes > 0) ? totalBytes / 100 * minPercent : long.MaxValue;
        long abs = minGb > 0 ? (long)minGb * BytesPerGb : long.MaxValue;
        long req = System.Math.Min(pct, abs);
        return req == long.MaxValue ? 0 : req;
    }

    /// <summary>True when <paramref name="freeBytes"/> meets or exceeds the required
    /// free space (<see cref="RequiredFreeBytes"/>). A required threshold of 0
    /// (unknown total / both floors 0) always passes.</summary>
    public static bool HasEnoughFree(long freeBytes, long totalBytes, int minPercent, int minGb)
        => freeBytes >= RequiredFreeBytes(totalBytes, minPercent, minGb);
}
