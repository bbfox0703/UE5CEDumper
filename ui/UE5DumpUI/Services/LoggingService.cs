using Serilog;
using UE5DumpUI.Core;

namespace UE5DumpUI.Services;

/// <summary>
/// Serilog-based logging service with category-based file routing.
///
/// Category files (under Logs/UE5DumpUI/ subfolder):
///   init-0.log — app lifecycle, version, connection events
///   pipe-0.log — pipe TX/RX JSON lines, connect/disconnect
///   view-0.log — UI operations, search, navigation, export (default)
///
/// Per-process mirror files (under Logs/{ProcessName}/):
///   ui-init-0.log, ui-pipe-0.log, ui-view-0.log
///   Prefixed with "ui-" to avoid collision with DLL log files.
///
/// Each file: 5MB cap. Retention is by AGE, not generation count — at startup the
/// previous session's -0.log is archived to -YYYYMMDD-HHMMSS.log (stamped from its
/// own mtime), and archives older than Constants.LogMaxAgeDays are deleted. A file
/// count could not express "keep 15 days": rotation runs on every startup, so a few
/// restarts discarded everything before them regardless of date. Mirrors
/// Grimoire::LOG_RETENTION_DAYS in the DLL's Sein logger.
///
/// Startup housekeeping runs in three passes, widening as it goes:
///   1. per-category archive + prune of THIS folder  (ArchivePreviousLog / PruneAgedLogs)
///   2. whole per-game folders untouched past the window (CleanupOldLogFolders)
///   3. an age-only sweep of every remaining *.log, here AND in the per-game folders
///      (PurgeOrphanedLogs) — the backstop for files pass 1 cannot see at all,
///      because its globs are keyed on the categories that exist TODAY.
/// </summary>
public sealed class LoggingService : ILoggingService, IDisposable
{
    private const string OutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u4}] {Message:lj}{NewLine}{Exception}";

    // Category log file names (without rotation suffix)
    private static readonly string[] CategoryNames = [
        Constants.LogCatInit,   // "init"
        Constants.LogCatPipe,   // "pipe"
        Constants.LogCatView,   // "view"
    ];

    private readonly string _logDirectory;
    private readonly string _moduleDir;  // Logs/UE5DumpUI/

    // Category loggers (main)
    private readonly Serilog.Core.Logger _initLogger;
    private readonly Serilog.Core.Logger _pipeLogger;
    private readonly Serilog.Core.Logger _viewLogger;

    // Console logger (shared)
    private readonly Serilog.Core.Logger _consoleLogger;

    // Per-process mirror loggers
    private readonly object _mirrorLock = new();
    private Serilog.Core.Logger? _mirrorInitLogger;
    private Serilog.Core.Logger? _mirrorPipeLogger;
    private Serilog.Core.Logger? _mirrorViewLogger;

    public LoggingService(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(logDirectory);

        // Create UI module subfolder: Logs/UE5DumpUI/
        _moduleDir = Path.Combine(logDirectory, Constants.LogSubfolderName);
        Directory.CreateDirectory(_moduleDir);

        // Clean up old daily format files from previous versions
        CleanupOldDailyLogs(_moduleDir, "UE5DumpUI");
        // Also clean root for leftover old-format files
        CleanupOldDailyLogs(logDirectory, "UE5DumpUI");

        // Per-startup: archive the previous session, then age out old archives.
        foreach (var cat in CategoryNames)
        {
            ArchivePreviousLog(_moduleDir, cat);
            PruneAgedLogs(_moduleDir, cat, Constants.LogMaxAgeDays);
        }

        // Create category loggers
        _initLogger = CreateFileLogger(Path.Combine(_moduleDir, "init-0.log"));
        _pipeLogger = CreateFileLogger(Path.Combine(_moduleDir, "pipe-0.log"));
        _viewLogger = CreateFileLogger(Path.Combine(_moduleDir, "view-0.log"));

        // Console logger (shared across all categories)
        _consoleLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u4}] {Message:lj}{NewLine}")
            .CreateLogger();

        // Retention, in cheapest-first order: whole aged-out folders, then the
        // per-file orphan sweep over whatever survived.
        //
        // These run AFTER the loggers exist so they can report what they did.
        // CleanupOldLogFolders used to run before _initLogger was assigned, so its
        // "Deleted old log folder" line dereferenced null, threw into the adjacent
        // best-effort catch, and was never once written — the folders were being
        // deleted silently.
        CleanupOldLogFolders(Constants.LogMaxAgeDays);
        PurgeOrphanedLogs(Constants.LogMaxAgeDays);

        _initLogger.Information("LoggingService initialized, log dir: {LogDir}", _moduleDir);
    }

    // ================================================================
    // Category resolution
    // ================================================================

    private Serilog.Core.Logger ResolveLogger(string? category) => category switch
    {
        Constants.LogCatInit => _initLogger,
        Constants.LogCatPipe => _pipeLogger,
        _ => _viewLogger,
    };

    private Serilog.Core.Logger? ResolveMirrorLogger(string? category)
    {
        lock (_mirrorLock)
        {
            return category switch
            {
                Constants.LogCatInit => _mirrorInitLogger,
                Constants.LogCatPipe => _mirrorPipeLogger,
                _ => _mirrorViewLogger,
            };
        }
    }

    // ================================================================
    // Default-category methods (route to "view")
    // ================================================================

    public void Info(string message) => Info(Constants.LogCatView, message);
    public void Warn(string message) => Warn(Constants.LogCatView, message);
    public void Error(string message) => Error(Constants.LogCatView, message);
    public void Error(string message, Exception ex) => Error(Constants.LogCatView, message, ex);
    public void Debug(string message) => Debug(Constants.LogCatView, message);

    // ================================================================
    // Category-aware methods
    // ================================================================

    public void Info(string category, string message)
    {
        ResolveLogger(category).Information(message);
        _consoleLogger.Information(message);
        ResolveMirrorLogger(category)?.Information(message);
    }

    public void Warn(string category, string message)
    {
        ResolveLogger(category).Warning(message);
        _consoleLogger.Warning(message);
        ResolveMirrorLogger(category)?.Warning(message);
    }

    public void Error(string category, string message)
    {
        ResolveLogger(category).Error(message);
        _consoleLogger.Error(message);
        ResolveMirrorLogger(category)?.Error(message);
    }

    public void Error(string category, string message, Exception ex)
    {
        ResolveLogger(category).Error(ex, message);
        _consoleLogger.Error(ex, message);
        ResolveMirrorLogger(category)?.Error(ex, message);
    }

    public void Debug(string category, string message)
    {
        ResolveLogger(category).Debug(message);
        _consoleLogger.Debug(message);
        ResolveMirrorLogger(category)?.Debug(message);
    }

    // ================================================================
    // Per-process mirror logging
    // ================================================================

    public void StartProcessMirror(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;

        var safeName = SanitizeFolderName(processName);
        var mirrorDir = Path.Combine(_logDirectory, safeName);

        try
        {
            Directory.CreateDirectory(mirrorDir);

            // Archive the previous session's mirror files, then age out old archives.
            foreach (var cat in CategoryNames)
            {
                var mirrorPrefix = $"{Constants.MirrorLogPrefix}-{cat}";
                ArchivePreviousLog(mirrorDir, mirrorPrefix);
                PruneAgedLogs(mirrorDir, mirrorPrefix, Constants.LogMaxAgeDays);
            }
            // Also clean up old-format mirror files
            CleanupOldDailyLogs(mirrorDir, "UE5DumpUI");

            var newInit = CreateFileLogger(Path.Combine(mirrorDir, $"{Constants.MirrorLogPrefix}-init-0.log"));
            var newPipe = CreateFileLogger(Path.Combine(mirrorDir, $"{Constants.MirrorLogPrefix}-pipe-0.log"));
            var newView = CreateFileLogger(Path.Combine(mirrorDir, $"{Constants.MirrorLogPrefix}-view-0.log"));

            lock (_mirrorLock)
            {
                _mirrorInitLogger?.Dispose();
                _mirrorPipeLogger?.Dispose();
                _mirrorViewLogger?.Dispose();
                _mirrorInitLogger = newInit;
                _mirrorPipeLogger = newPipe;
                _mirrorViewLogger = newView;
            }

            _initLogger.Information("Process mirror log started: {MirrorDir}", mirrorDir);
            newInit.Information("Mirror log started for process: {Process}", processName);

            // No folder-count cleanup here — see the AF9 note above. Folder retention
            // is CleanupOldLogFolders(LogMaxAgeDays), which the constructor already ran.
        }
        catch (Exception ex)
        {
            _initLogger.Warning("Failed to start process mirror log: {Error}", ex.Message);
        }
    }

    public void StopProcessMirror()
    {
        lock (_mirrorLock)
        {
            if (_mirrorInitLogger != null)
            {
                _mirrorInitLogger.Information("Mirror log stopped");
                _mirrorInitLogger.Dispose();
                _mirrorInitLogger = null;
            }
            _mirrorPipeLogger?.Dispose();
            _mirrorPipeLogger = null;
            _mirrorViewLogger?.Dispose();
            _mirrorViewLogger = null;
        }
    }

    public void Dispose()
    {
        StopProcessMirror();
        _initLogger.Dispose();
        _pipeLogger.Dispose();
        _viewLogger.Dispose();
        _consoleLogger.Dispose();
    }

    // ================================================================
    // Helpers
    // ================================================================

    private static Serilog.Core.Logger CreateFileLogger(string filePath)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                filePath,
                fileSizeLimitBytes: Constants.LogMaxSizeBytes,
                // WITHOUT this, fileSizeLimitBytes is a KILL switch, not a cap: Serilog
                // defaults rollOnFileSizeLimit to false and rollingInterval to Infinite,
                // so the sink has no roll point and silently drops every event once the
                // limit is reached — for the rest of the process, with no error and no
                // SelfLog. Measured against these exact package versions: 1 file created,
                // frozen at the limit, 69 of 500 events kept, and the post-limit warnings
                // and errors simply absent. Meanwhile docs/architecture.md and the
                // CLAUDE.md log rule both promise the 8 MB cap ARCHIVES mid-session, and
                // the DLL half (Sein::RotateIfNeeded) genuinely does. Audit #4 B31.
                rollOnFileSizeLimit: true,
                // Serilog defaults this to 31 as soon as rolling is enabled. A COUNT limit
                // is precisely the retention policy this project deliberately replaced with
                // an age-based one, so leaving it defaulted would reinstate generation-count
                // eviction by the back door. Retention stays owned by PruneAgedLogs, which
                // still sees the rolled files: they are named "{prefix}-0_001.log", which
                // matches its "{prefix}-*.log" glob and does not end in "-0.log", so the
                // live-file guard correctly leaves only the active file alone.
                retainedFileCountLimit: null,
                outputTemplate: OutputTemplate)
            .CreateLogger();
    }

    /// <summary>
    /// Archive the previous session's <c>{prefix}-0.log</c> to
    /// <c>{prefix}-YYYYMMDD-HHMMSS.log</c>, stamped with the file's OWN last-write
    /// time, and migrate any legacy numbered generation left by older builds.
    /// Slot 0 is free for the new session afterwards.
    /// </summary>
    /// <remarks>
    /// Replaces the old fixed generation shuffle (delete N-1, shift the rest). That
    /// scheme could not express an age: it ran on every startup, so a few restarts
    /// discarded everything before them no matter how recent. Retention is now by
    /// age via <see cref="PruneAgedLogs"/>, matching the DLL's Sein logger.
    /// Stamping from the file's own mtime rather than "now" is what keeps the age
    /// prune honest — otherwise archiving would reset a stale log's clock.
    /// </remarks>
    internal static void ArchivePreviousLog(string directory, string prefix)
    {
        try
        {
            ArchiveOne(Path.Combine(directory, $"{prefix}-0.log"), directory, prefix);

            // Legacy generations from builds before age-based retention. Without this
            // they orphan: nothing rotates them any more, and PruneAgedLogs keys on
            // *.log mtime so it would only reach them once they aged out anyway.
            for (int i = 1; i <= 9; i++)
                ArchiveOne(Path.Combine(directory, $"{prefix}-{i}.log"), directory, prefix);
        }
        catch
        {
            // Best effort — don't prevent app startup over log rotation
        }
    }

    internal static void ArchiveOne(string src, string directory, string prefix)
    {
        try
        {
            if (!File.Exists(src)) return;
            var stamp = File.GetLastWriteTime(src).ToString("yyyyMMdd-HHmmss");

            // Two archives in the same second are possible on a restart loop.
            for (int dup = 0; dup < 100; dup++)
            {
                var suffix = dup == 0 ? stamp : $"{stamp}-{dup}";
                var dst = Path.Combine(directory, $"{prefix}-{suffix}.log");
                if (File.Exists(dst)) continue;
                File.Move(src, dst);
                return;
            }
            File.Delete(src);
        }
        catch
        {
            // Locked by another instance, or unreadable — leave it; the age prune
            // will collect it later.
        }
    }

    /// <summary>
    /// Delete archived <c>{prefix}-*.log</c> files past the retention window.
    /// Never touches <c>-0.log</c>, which is the live file for this session.
    /// </summary>
    internal static void PruneAgedLogs(string directory, string prefix, int maxAgeDays)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-maxAgeDays);
            foreach (var file in Directory.GetFiles(directory, $"{prefix}-*.log"))
            {
                if (Path.GetFileName(file).EndsWith("-0.log", StringComparison.Ordinal)) continue;
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
                }
                catch { /* locked — try again next startup */ }
            }
        }
        catch
        {
            // Best effort
        }
    }

    /// <summary>
    /// Age out every <c>*.log</c> the per-category sweeps structurally cannot see,
    /// across the UI's own folder and every per-game folder.
    ///
    /// <para><b>Why this is needed.</b> <see cref="PruneAgedLogs"/> globs
    /// <c>{prefix}-*.log</c> for each prefix in the CURRENT category list, so the
    /// moment a category is renamed or retired its files stop matching any glob and
    /// live forever. Observed in the wild: the UI folder still held
    /// <c>walk-0.log</c>…<c>walk-3.log</c> and <c>ui-view-1.log</c> from a 2026-03
    /// build five months later — <c>walk</c> is no longer a UI category, and the
    /// <c>ui-</c> mirror prefix is only ever a GAME-folder shape. Being named
    /// <c>-0.log</c>, they also slipped past that method's live-file guard.</para>
    ///
    /// <para><b>Why age alone is the right (and safe) rule.</b> This deliberately
    /// knows nothing about categories, which is what lets it sweep the DLL-written
    /// game folders too — the UI does not track <c>Sein</c>'s category list and
    /// should not have to. A file that is being actively written has an mtime of
    /// NOW, so it can never be older than the retention window: a running game's
    /// live <c>-0.log</c> files protect themselves, including categories the UI has
    /// never heard of. The explicit live-name guard below is only a belt for the
    /// handles THIS process is about to hold open.</para>
    /// </summary>
    private void PurgeOrphanedLogs(int maxAgeDays)
    {
        // The files this process is about to write. Serilog holds them open (so a
        // delete would fail anyway) but naming them keeps the intent explicit.
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in CategoryNames) live.Add($"{cat}-0.log");

        int deleted = PruneOrphanedLogs(_moduleDir, maxAgeDays, live);

        try
        {
            foreach (var dir in Directory.GetDirectories(_logDirectory))
            {
                // The UI's own folder was just swept above, with its live files named.
                if (string.Equals(Path.GetFileName(dir), Constants.LogSubfolderName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                deleted += PruneOrphanedLogs(dir, maxAgeDays);
            }
        }
        catch { /* best effort — never block startup on housekeeping */ }

        if (deleted > 0)
            _initLogger.Information(
                "Purged {Count} orphaned log file(s) older than {MaxAge}d " +
                "(retired categories / legacy generation names)", deleted, maxAgeDays);
    }

    /// <summary>
    /// Delete every <c>*.log</c> in <paramref name="directory"/> whose mtime is past
    /// the retention window. Returns how many went, so startup can report it.
    /// See <see cref="PurgeOrphanedLogs"/> for why matching on age alone is correct.
    /// </summary>
    /// <param name="liveFileNames">Names to never touch regardless of age.</param>
    internal static int PruneOrphanedLogs(string directory, int maxAgeDays,
                                         ISet<string>? liveFileNames = null)
    {
        int deleted = 0;
        try
        {
            var cutoff = DateTime.Now.AddDays(-maxAgeDays);
            foreach (var file in Directory.GetFiles(directory, "*.log"))
            {
                if (liveFileNames?.Contains(Path.GetFileName(file)) == true) continue;
                try
                {
                    if (File.GetLastWriteTime(file) >= cutoff) continue;
                    File.Delete(file);
                    deleted++;
                }
                catch
                {
                    // Locked — a running game's DLL still owns it. Retry next startup.
                }
            }
        }
        catch
        {
            // Best effort
        }
        return deleted;
    }

    /// <summary>
    /// Remove old daily-format log files (UE5DumpUI-YYYYMMDD.log) left over
    /// from previous Serilog RollingInterval.Day configuration.
    /// Also removes numbered files beyond the rotation max.
    /// </summary>
    internal static void CleanupOldDailyLogs(string directory, string prefix)
    {
        try
        {
            foreach (var file in Directory.GetFiles(directory, $"{prefix}-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var suffix = name[(prefix.Length + 1)..];

                // Keep the live file and any legacy 1-digit generation ("...-0").
                // Bounded to ONE digit deliberately: the original check was an
                // unbounded int.TryParse, and "20260304" parses fine (it fits in
                // int32), so every daily-format file took this branch — meaning
                // this method never once deleted the format it exists to remove.
                if (suffix.Length == 1 && char.IsAsciiDigit(suffix[0])) continue;

                // Keep our OWN archives — "YYYYMMDD-HHMMSS" (optionally "-N" on a
                // same-second collision). Without this guard every archive written
                // by ArchivePreviousLog would be deleted on the next startup, since
                // its suffix is not a bare integer. The old daily format this method
                // exists to remove was "YYYYMMDD" with no time part, so requiring the
                // "-HHMMSS" separates the two unambiguously.
                if (IsArchiveSuffix(suffix)) continue;

                // Delete daily files (YYYYMMDD) and other non-matching patterns
                try { File.Delete(file); } catch { }
            }
        }
        catch
        {
            // Best effort
        }
    }

    /// <summary>
    /// True for "YYYYMMDD-HHMMSS" or "YYYYMMDD-HHMMSS-N" — the shape
    /// <see cref="ArchiveOne"/> produces. Deliberately stricter than a date check:
    /// the legacy daily format was "YYYYMMDD" with no time part, and that one MUST
    /// still be deleted.
    /// </summary>
    internal static bool IsArchiveSuffix(string suffix)
    {
        var parts = suffix.Split('-');
        if (parts.Length is < 2 or > 3) return false;
        if (parts[0].Length != 8 || !parts[0].All(char.IsAsciiDigit)) return false;
        if (parts[1].Length != 6 || !parts[1].All(char.IsAsciiDigit)) return false;
        return parts.Length == 2 || parts[2].All(char.IsAsciiDigit);
    }

    private static string SanitizeFolderName(string name)
    {
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
            name = name.Replace(c, '_');

        return string.IsNullOrWhiteSpace(name) ? "unknown" : name;
    }

    /// <summary>
    /// Last-write time of the newest file anywhere under <paramref name="dir"/>, falling
    /// back to the directory's own timestamp only when it is empty. Shared by both
    /// sweeps so they cannot disagree about which folder is "recent".
    /// </summary>
    private static DateTime NewestWriteUtc(DirectoryInfo dir)
    {
        try
        {
            var files = dir.GetFiles("*", SearchOption.AllDirectories);
            return files.Length > 0 ? files.Max(f => f.LastWriteTimeUtc) : dir.LastWriteTimeUtc;
        }
        catch
        {
            // Unreadable → treat as brand new, so an access error can never be the
            // reason a folder gets deleted.
            return DateTime.UtcNow;
        }
    }

    // ================================================================
    // REMOVED: the count-based per-process folder cap (audit #5 AF9).
    //
    // `CleanupProcessFolders` evicted every per-game folder past the newest 20 and
    // logged it at Debug. Three things make that indefensible rather than merely
    // redundant:
    //
    //  1. CLAUDE.md's Log-output rule says retention is BY AGE, not by generation
    //     count, and explains why a count cannot express it. Constants.cs said the
    //     same thing four lines above `MaxProcessFolders = 20`, so the file
    //     contradicted itself.
    //  2. There was nothing to guard against. A folder is named after the GAME
    //     (SanitizeFolderName(processName)), not the PID — relaunching a game reuses
    //     its folder, so the count is "distinct games played in 21 days". It cannot
    //     run away.
    //  3. It deleted RECURSIVELY, and the folder is shared: the DLL writes five
    //     categories into the same directory under an age-only policy of its own
    //     (Sein.cpp's PruneStaleProcessFolders, whose comment already reads
    //     "Age-based, matching the file policy"). So the UI silently destroyed logs
    //     the DLL had deliberately kept — the two halves of one folder disagreeing.
    //
    // CleanupOldLogFolders(Constants.LogMaxAgeDays) runs from the constructor and is
    // now the sole owner of folder retention, which is what the docs always claimed.
    // ================================================================

    /// <summary>
    /// Delete log subfolders (and their contents) that haven't been written to
    /// for more than <paramref name="maxAgeDays"/> days.
    /// Runs at UI startup to prevent unbounded log accumulation.
    /// The UI module folder (UE5DumpUI) is never deleted.
    /// </summary>
    private void CleanupOldLogFolders(int maxAgeDays)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
            var dirs = Directory.GetDirectories(_logDirectory)
                .Select(d => new DirectoryInfo(d))
                .Where(d => d.Name != "." && d.Name != "..")
                .ToList();

            foreach (var dir in dirs)
            {
                // Never delete the UI module's own folder
                if (string.Equals(dir.Name, Constants.LogSubfolderName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                // Judge by the NEWEST file inside, not the directory's own mtime —
                // see NewestWriteUtc for why. Shared with CleanupProcessFolders so the
                // two sweeps cannot disagree about which folder is "recent"; they used
                // to, and the count-based one was the wrong half.
                if (NewestWriteUtc(dir) < cutoff)
                {
                    try
                    {
                        dir.Delete(true);
                        _initLogger.Information(
                            "Deleted old log folder (>{MaxAge}d): {Folder}",
                            maxAgeDays, dir.Name);
                    }
                    catch
                    {
                        // Best effort — folder may be locked by another process
                    }
                }
            }
        }
        catch
        {
            // Best effort — don't prevent app startup
        }
    }
}
