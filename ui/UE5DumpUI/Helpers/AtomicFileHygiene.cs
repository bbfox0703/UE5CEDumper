using System.Globalization;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Naming and selection policy for the "stage into a temp file, rename over the original"
/// idiom, plus the quarantine naming its failure mode needs. PURE — every member is a
/// string/date computation with no <c>System.IO</c> call, so the rules can be unit-tested
/// without a disk, and the services keep the I/O.
///
/// WHY IT EXISTS (audit #5 AC4/AC5/AC6). Seven services in <c>Services/</c> hand-roll the
/// same write-temp-then-move dance and there was no shared helper to reuse; two of them
/// (<c>BookmarkStore</c>, <c>CoordinateLibraryStore</c>) clean up a stale temp and the rest
/// do not. <c>AobUsageService</c> was the worst case because its temp name carries the PID
/// — a distinctly named full copy of the cache per affected launch, i.e. UNBOUNDED residue
/// in <c>%LOCALAPPDATA%\UE5CEDumper\</c>, against the app-data rule that the root holds only
/// files "app-wide and fixed in number". This deliberately does NOT wrap the write itself:
/// migrating the other six sites is a separate change, and a policy object they can all
/// adopt later is worth more than a seventh copy of the mechanics.
///
/// The temp naming is byte-compatible with the DLL's <c>Flamme::MakeTempPath</c>, and the
/// age guard matches <c>Flamme::SweepOrphanTemps</c>: AGE, never PID liveness. Both
/// processes write the same cache file, writing it takes milliseconds, and a liveness test
/// would race a process that is mid-write right now.
/// </summary>
public static class AtomicFileHygiene
{
    /// <summary>Separates the target name from the writer's PID: <c>&lt;file&gt;.tmp.&lt;pid&gt;</c>.</summary>
    public const string TempInfix = ".tmp.";

    /// <summary>Separates the target name from the quarantine timestamp.</summary>
    public const string CorruptInfix = ".corrupt-";

    /// <summary>A staging file this old cannot belong to a live write.</summary>
    public static readonly TimeSpan StaleTempAge = TimeSpan.FromHours(1);

    /// <summary>How many quarantined copies to keep. Bounded on purpose: the point is to
    /// stop destroying data, not to grow an unbounded pile in an app-data root whose rule
    /// is "app-wide and fixed in number".</summary>
    public const int MaxCorruptCopies = 5;

    /// <summary>Per-process staging path for <paramref name="filePath"/>.</summary>
    public static string TempPathFor(string filePath, int processId)
        => filePath + TempInfix + processId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Prefix every staging file for <paramref name="fileName"/> starts with.
    /// Scoped to one file's own name — never a folder wildcard — so a sweep cannot reach
    /// a sibling, and on this machine not even the same cache for a different machine
    /// name.</summary>
    public static string TempPrefixFor(string fileName) => fileName + TempInfix;

    /// <summary>Prefix every quarantined copy of <paramref name="fileName"/> starts with.</summary>
    public static string CorruptPrefixFor(string fileName) => fileName + CorruptInfix;

    /// <summary>
    /// Is <paramref name="candidateName"/> an abandoned staging file for
    /// <paramref name="fileName"/>? Requires BOTH the exact prefix (with something after
    /// it, so the bare prefix is not a match) and an mtime at least
    /// <paramref name="maxAge"/> old.
    /// </summary>
    public static bool IsStaleTemp(string fileName, string candidateName,
                                   DateTime candidateMtimeUtc, DateTime nowUtc, TimeSpan maxAge)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(candidateName))
            return false;

        var prefix = TempPrefixFor(fileName);
        if (candidateName.Length <= prefix.Length)
            return false;
        if (!candidateName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        return nowUtc - candidateMtimeUtc >= maxAge;
    }

    /// <summary>
    /// Name to move a corrupt <paramref name="fileName"/> aside as. Milliseconds are in the
    /// stamp so two detections in the same second cannot collide — and if one somehow did,
    /// the move fails and the caller must refuse to hand back an empty document rather than
    /// quietly overwrite the original.
    /// </summary>
    public static string QuarantineNameFor(string fileName, DateTime whenUtc)
        => fileName + CorruptInfix + whenUtc.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);

    /// <summary>
    /// Which quarantined copies to delete, oldest first, keeping the newest
    /// <paramref name="keep"/>. The embedded stamp is fixed-width and zero-padded, so a
    /// lexicographic sort IS chronological.
    /// </summary>
    public static IReadOnlyList<string> SelectCorruptCopiesToPrune(
        IEnumerable<string> candidateNames, string fileName, int keep)
    {
        if (keep < 0) keep = 0;
        var prefix = CorruptPrefixFor(fileName);

        var matches = candidateNames
            .Where(n => !string.IsNullOrEmpty(n)
                        && n.Length > prefix.Length
                        && n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int excess = matches.Count - keep;
        return excess <= 0 ? Array.Empty<string>() : matches.GetRange(0, excess);
    }
}
