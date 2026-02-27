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
/// Each file: 2-file rotation, 5MB cap.
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

        // Delete log folders older than 15 days
        CleanupOldLogFolders(Constants.LogMaxAgeDays);

        // Per-startup rotation for each category file
        foreach (var cat in CategoryNames)
        {
            RotateLogFiles(_moduleDir, cat, Constants.LogRotateMax);
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

            // Rotate mirror files for each category
            foreach (var cat in CategoryNames)
            {
                var mirrorPrefix = $"{Constants.MirrorLogPrefix}-{cat}";
                RotateLogFiles(mirrorDir, mirrorPrefix, Constants.LogRotateMax);
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

            CleanupProcessFolders();
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
                outputTemplate: OutputTemplate)
            .CreateLogger();
    }

    /// <summary>
    /// Rotate numbered log files: delete oldest, shift N-2 → N-1, ..., 0 → 1.
    /// After rotation, slot 0 is free for the new session's log.
    /// </summary>
    private static void RotateLogFiles(string directory, string prefix, int maxFiles)
    {
        try
        {
            var oldest = Path.Combine(directory, $"{prefix}-{maxFiles - 1}.log");
            if (File.Exists(oldest)) File.Delete(oldest);

            for (int i = maxFiles - 2; i >= 0; i--)
            {
                var src = Path.Combine(directory, $"{prefix}-{i}.log");
                var dst = Path.Combine(directory, $"{prefix}-{i + 1}.log");
                if (File.Exists(src)) File.Move(src, dst);
            }
        }
        catch
        {
            // Best effort — don't prevent app startup over log rotation
        }
    }

    /// <summary>
    /// Remove old daily-format log files (UE5DumpUI-YYYYMMDD.log) left over
    /// from previous Serilog RollingInterval.Day configuration.
    /// Also removes numbered files beyond the rotation max.
    /// </summary>
    private static void CleanupOldDailyLogs(string directory, string prefix)
    {
        try
        {
            foreach (var file in Directory.GetFiles(directory, $"{prefix}-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var suffix = name[(prefix.Length + 1)..];

                // Keep numbered files that match category format (e.g., "init-0")
                if (int.TryParse(suffix, out _)) continue;

                // Delete daily files (YYYYMMDD) and other non-matching patterns
                try { File.Delete(file); } catch { }
            }
        }
        catch
        {
            // Best effort
        }
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

    private void CleanupProcessFolders()
    {
        try
        {
            var dirs = Directory.GetDirectories(_logDirectory)
                .Select(d => new DirectoryInfo(d))
                .Where(d => d.Name != "." && d.Name != "..")
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .ToList();

            if (dirs.Count <= Constants.MaxProcessFolders) return;

            foreach (var old in dirs.Skip(Constants.MaxProcessFolders))
            {
                try
                {
                    old.Delete(true);
                    _initLogger.Debug("Cleaned up old process log folder: {Folder}", old.Name);
                }
                catch
                {
                    // Best effort
                }
            }
        }
        catch
        {
            // Best effort
        }
    }

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

                if (dir.LastWriteTimeUtc < cutoff)
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
