using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UE5DumpUI.Core;

namespace UE5DumpUI.Services;

/// <summary>
/// Owns the on-disk layout of the per-game data folders under
/// <c>%LOCALAPPDATA%\UE5CEDumper</c>: creates <c>Snapshots\</c> / <c>Bookmarks\</c> /
/// <c>TeleportCoords\</c>,
/// MOVES anything still sitting at the old flat root into them, and — for the folders
/// that opt in — ages out the games nobody has touched in a while.
///
/// <para><b>Retention is per CALLER, not per folder.</b> <see cref="SnapshotStore"/>
/// passes <see cref="Constants.DataMaxAgeDays"/>; <see cref="BookmarkStore"/> and
/// <see cref="CoordinateLibraryStore"/> pass 0, which disables the sweep outright --
/// both hold hand-made data nobody can regenerate. Migration is the shared part; what a folder is
/// worth keeping forever is the store's call, not this file's.</para>
///
/// <para><b>Called from the store constructors, not from App.</b> The store that READS
/// a folder is the one that populates it, so the ordering cannot drift: there is no way
/// to construct a <see cref="SnapshotStore"/> or <see cref="BookmarkStore"/> that opens
/// files before its folder has been migrated. A composition-root call site would be one
/// reorder away from silently reading an empty new folder while the user's data sat in
/// the old one.</para>
///
/// <para><b>Once per process per folder.</b> Both operations are idempotent, but a
/// second pass is pure cost and a second set of log lines, and tests construct stores in
/// a loop. The gate is keyed by folder+prefix so a test's temp directory is unaffected
/// by the real one.</para>
///
/// <para><b>Every failure is survivable.</b> A file that will not move stays where it is
/// (and the rest of its game stays with it); a file that will not delete is logged and
/// skipped. Neither is ever fatal — the worst outcome is a folder that stays untidy,
/// which is strictly better than a store that will not open.</para>
/// </summary>
public static class AppDataFolderMaintenance
{
    private static readonly ConcurrentDictionary<string, bool> s_done =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ensure <c>{rootDir}\{subFolder}</c> exists, migrate this prefix's files out of
    /// <paramref name="rootDir"/> into it, prune the games that have aged out, and
    /// return the folder the caller should use from now on.
    ///
    /// <para>Returns the destination path even when every step fails — the caller needs
    /// somewhere to write, and a store that throws from its constructor takes the whole
    /// app down.</para>
    /// </summary>
    public static string Prepare(
        string rootDir, string subFolder, string filePrefix, int maxAgeDays, ILoggingService? log)
    {
        var destDir = Path.Combine(rootDir, subFolder);
        try { Directory.CreateDirectory(destDir); }
        catch (Exception ex)
        {
            log?.Warn(Constants.LogCatInit,
                $"AppDataFolderMaintenance: could not create '{destDir}' ({ex.Message}) — using it anyway");
        }

        if (!s_done.TryAdd($"{destDir}|{filePrefix}", true)) return destDir;

        MigrateFromLegacyRoot(rootDir, destDir, filePrefix, log);
        PruneAged(destDir, filePrefix, maxAgeDays, log);
        return destDir;
    }

    // ================================================================
    // Migration: old flat root -> per-family subfolder
    // ================================================================

    /// <summary>
    /// Move every <c>{prefix}.*</c> file from <paramref name="rootDir"/> into
    /// <paramref name="destDir"/>. Returns how many files moved.
    ///
    /// <para><b>A game's files move as a GROUP, or not at all.</b> Half a move is data
    /// loss, not untidiness: a SQLite <c>.db</c> that lands in the new folder without
    /// the <c>-wal</c> it was checkpointing has silently dropped every transaction that
    /// WAL still held, and the abandoned <c>-wal</c> is then live bait for the next file
    /// to take that name. So each group is attempted in
    /// <see cref="AppDataRetentionPolicy.MoveOrder"/> and rolled back on the first
    /// failure.</para>
    ///
    /// <para><b>An existing destination file is never overwritten.</b> It aborts that
    /// group and leaves the root copy in place — the case that produces it is running an
    /// older build (which writes to the root again) after migrating, and there is no way
    /// to tell from the outside which of the two copies the user wants. Untidy and
    /// logged beats silently discarding one of them.</para>
    /// </summary>
    internal static int MigrateFromLegacyRoot(
        string rootDir, string destDir, string prefix, ILoggingService? log)
    {
        // Same folder (a caller passing subFolder = "") would make every move a no-op
        // rename onto itself; bail rather than churn.
        if (PathsEqual(rootDir, destDir)) return 0;

        List<string> names;
        try
        {
            names = Directory.EnumerateFiles(rootDir, prefix + ".*")
                             .Select(Path.GetFileName)
                             .Where(n => AppDataRetentionPolicy.BelongsTo(n, prefix))
                             .Select(n => n!)
                             .ToList();
        }
        catch { return 0; }        // root gone / unreadable — nothing to migrate

        if (names.Count == 0) return 0;

        int moved = 0, kept = 0;
        foreach (var group in names.GroupBy(
                     n => AppDataRetentionPolicy.GameKeyOf(n, prefix)!, StringComparer.OrdinalIgnoreCase))
        {
            var done = new List<(string From, string To)>();
            try
            {
                foreach (var n in AppDataRetentionPolicy.MoveOrder(group))
                {
                    var from = Path.Combine(rootDir, n);
                    var to   = Path.Combine(destDir, n);
                    if (File.Exists(to))
                        throw new IOException($"'{n}' already exists in the new folder");
                    File.Move(from, to);
                    done.Add((from, to));
                }
                moved += done.Count;
            }
            catch (Exception ex)
            {
                foreach (var (from, to) in done)
                    try { File.Move(to, from); } catch { /* best effort — see class doc */ }
                kept += group.Count();
                log?.Warn(Constants.LogCatInit,
                    $"AppDataFolderMaintenance: left '{prefix}.{group.Key}.*' at the old location ({ex.Message})");
            }
        }

        if (moved > 0 || kept > 0)
            log?.Info(Constants.LogCatInit,
                $"AppDataFolderMaintenance: moved {moved} '{prefix}' file(s) into '{destDir}'" +
                (kept > 0 ? $", left {kept} behind" : ""));
        return moved;
    }

    // ================================================================
    // Retention: drop the games nobody has touched in maxAgeDays
    // ================================================================

    /// <summary>
    /// Delete every file of every game in <paramref name="dir"/> that has gone unused for
    /// longer than <paramref name="maxAgeDays"/>. Returns how many files went.
    /// File-level removal, not a row purge — the whole point is to reclaim the disk a
    /// long-dead game's multi-GB DB is holding.
    /// </summary>
    internal static int PruneAged(string dir, string prefix, int maxAgeDays, ILoggingService? log)
    {
        if (maxAgeDays <= 0) return 0;

        List<AppDataFileEntry> entries;
        try
        {
            entries = Directory.EnumerateFiles(dir, prefix + ".*")
                               .Where(p => AppDataRetentionPolicy.BelongsTo(Path.GetFileName(p), prefix))
                               .Select(p => new FileInfo(p))
                               .Select(fi => new AppDataFileEntry(fi.Name, LastUsedUtc(fi)))
                               .ToList();
        }
        catch { return 0; }

        var expired = AppDataRetentionPolicy.SelectExpired(entries, prefix, DateTime.UtcNow, maxAgeDays);
        if (expired.Count == 0) return 0;

        int deleted = 0;
        foreach (var name in expired)
        {
            try
            {
                File.Delete(Path.Combine(dir, name));
                deleted++;
            }
            catch (Exception ex)
            {
                log?.Warn(Constants.LogCatInit,
                    $"AppDataFolderMaintenance: could not delete aged '{name}' ({ex.Message}) — in use?");
            }
        }

        if (deleted > 0)
            log?.Info(Constants.LogCatInit,
                $"AppDataFolderMaintenance: deleted {deleted} '{prefix}' file(s) unused for {maxAgeDays}+ days");
        return deleted;
    }

    /// <summary>
    /// When a file was last USED: <c>LastWriteTimeUtc</c>, and deliberately NOT
    /// <c>max(LastWriteTimeUtc, LastAccessTimeUtc)</c>.
    ///
    /// <para><b>Last-access is not a usage signal on Windows — it is noise.</b> The
    /// obvious reading is that a write time means "unmodified" while an access time means
    /// "unused", so the max of the two is strictly better. Measured on the maintainer's
    /// own app-data folder, it is strictly worse: <c>fsutil behavior query
    /// DisableLastAccess</c> reports <c>2</c> (System Managed, updates ENABLED), and every
    /// file there had an access time of "today" against write times weeks old — a
    /// <c>bookmarks.*.json</c> last written 2026-06-24 read as accessed 2026-08-04. Any
    /// antivirus scan, backup pass or search indexer refreshes it. Feeding that to the
    /// sweep does not make retention safer; it makes retention never fire, which is a
    /// silent no-op dressed as a feature.</para>
    ///
    /// <para>So "used" is something this app RECORDS rather than infers:
    /// <see cref="TouchUsed"/> stamps the write time when a game becomes active, and
    /// nothing outside this process writes to these files. Explicit, and immune to every
    /// background reader on the machine.</para>
    /// </summary>
    internal static DateTime LastUsedUtc(FileInfo fi)
    {
        try { return fi.LastWriteTimeUtc; }
        catch { return DateTime.UtcNow; }   // unreadable metadata reads as fresh, never as expired
    }

    /// <summary>
    /// Record that a file is in use right now, so the age sweep counts OPENING a game —
    /// not only capturing into it — as use. No-op when the file does not exist: this must
    /// never be the thing that creates one.
    ///
    /// <para>Stamps the write time, for the reason <see cref="LastUsedUtc"/> gives.
    /// Metadata only: SQLite does not consult a DB file's mtime, so this cannot affect
    /// the store.</para>
    /// </summary>
    public static void TouchUsed(string path)
    {
        try
        {
            if (File.Exists(path)) File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch { /* best effort — a stamp we could not write only costs freshness */ }
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }
}
