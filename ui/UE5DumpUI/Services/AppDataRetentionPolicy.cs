using System;
using System.Collections.Generic;
using System.Linq;

namespace UE5DumpUI.Services;

/// <summary>
/// One per-game data file as the retention policy sees it.
/// </summary>
/// <param name="Name">The bare file NAME, never a path. The policy is deliberately
/// unable to name a file outside the folder its caller enumerated — everything it
/// returns is a name the caller already handed it.</param>
/// <param name="LastUsedUtc">When the file was last in use: the <c>LastWriteTimeUtc</c>
/// the store stamps on use, never the last-access time. See
/// <see cref="AppDataFolderMaintenance.LastUsedUtc"/> for why.</param>
public readonly record struct AppDataFileEntry(string Name, DateTime LastUsedUtc);

/// <summary>
/// Pure policy for the per-game data folders <see cref="AppDataFolderMaintenance"/> prepares:
/// which file belongs to which game, and which games have aged out. Zero
/// <c>System.IO</c> — the same split <c>ProxyOrphanScanner</c> uses, so the rules that
/// decide what gets DELETED are unit-testable without touching a disk.
///
/// <para><b>File naming.</b> Every file in these folders is
/// <c>{prefix}.{gameKey}.{rest}</c>: <c>snapshots.1A2B.db</c>, <c>snapshots.1A2B.db-wal</c>,
/// <c>snapshots.1A2B.db-shm</c>, <c>snapshots.1A2B.denylist.json</c>,
/// <c>bookmarks.1A2B.json</c>. The stores build the game key from a hex PE hash or a
/// sanitised module name, so it can never itself contain a <c>'.'</c> — which is
/// what makes "up to the first dot after the prefix" an exact parse rather than a
/// guess.</para>
///
/// <para><b>Ageing is per GAME, not per file.</b> A game's files are one unit: the
/// newest timestamp in the set governs the whole set. Per-file ageing would delete a
/// <c>.denylist.json</c> out from under a live <c>.db</c> (or, far worse, a <c>.db</c>
/// out from under nothing at all while its <c>-wal</c> survived to be adopted by the
/// next file of that name).</para>
/// </summary>
public static class AppDataRetentionPolicy
{
    /// <summary>
    /// The game key <paramref name="fileName"/> belongs to, or null when the name is
    /// not one of ours. Matching requires the literal <c>"{prefix}."</c> lead-in, so a
    /// neighbouring file such as <c>snapshots-backup.db</c> is never claimed.
    /// </summary>
    public static string? GameKeyOf(string? fileName, string prefix)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(prefix)) return null;

        // "{prefix}." — the dot is part of the match. Without it "snapshots" would also
        // claim "snapshots-backup.db", i.e. a file a user parked here by hand.
        if (fileName.Length <= prefix.Length + 1) return null;
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        if (fileName[prefix.Length] != '.') return null;

        var rest = fileName.AsSpan(prefix.Length + 1);
        int dot = rest.IndexOf('.');
        var key = dot < 0 ? rest : rest[..dot];
        return key.Length == 0 ? null : key.ToString();
    }

    /// <summary>True when <paramref name="fileName"/> is one of this prefix's files.</summary>
    public static bool BelongsTo(string? fileName, string prefix) =>
        GameKeyOf(fileName, prefix) != null;

    /// <summary>
    /// The file names to delete: every file of every game whose whole set has gone
    /// unused for longer than <paramref name="maxAgeDays"/>. Entries that don't match
    /// <paramref name="prefix"/> are ignored outright, never returned.
    ///
    /// <para><paramref name="maxAgeDays"/> &lt;= 0 disables the sweep and returns
    /// nothing. A zero-or-negative window would mean "everything is expired", and the
    /// one thing this function must not do when it is mis-configured is delete the
    /// user's whole snapshot corpus.</para>
    ///
    /// <para>A timestamp in the FUTURE (clock skew, a file copied off another machine)
    /// reads as fresh, which is the safe direction.</para>
    /// </summary>
    public static List<string> SelectExpired(
        IEnumerable<AppDataFileEntry> files, string prefix, DateTime nowUtc, int maxAgeDays)
    {
        var expired = new List<string>();
        if (files == null || maxAgeDays <= 0) return expired;

        var cutoff = nowUtc - TimeSpan.FromDays(maxAgeDays);

        var byGame = new Dictionary<string, (DateTime Newest, List<string> Names)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var f in files)
        {
            var key = GameKeyOf(f.Name, prefix);
            if (key == null) continue;               // not ours — never a delete candidate
            if (byGame.TryGetValue(key, out var g))
            {
                g.Names.Add(f.Name);
                byGame[key] = (f.LastUsedUtc > g.Newest ? f.LastUsedUtc : g.Newest, g.Names);
            }
            else
            {
                byGame[key] = (f.LastUsedUtc, new List<string> { f.Name });
            }
        }

        foreach (var g in byGame.Values)
            if (g.Newest < cutoff)
                expired.AddRange(g.Names);

        // Stable order so a log line / test assertion doesn't depend on hash ordering.
        expired.Sort(StringComparer.OrdinalIgnoreCase);
        return expired;
    }

    /// <summary>
    /// The order a game's files should be MOVED in during migration: the bare
    /// <c>.db</c> first, everything else after, name-stable within each group.
    ///
    /// <para>The <c>.db</c> is both the file most likely to be locked by something else
    /// and the one whose failure aborts the group, so attempting it first means a
    /// doomed migration touches nothing at all.</para>
    /// </summary>
    public static List<string> MoveOrder(IEnumerable<string> names) =>
        names.OrderBy(n => n.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
             .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
             .ToList();
}
