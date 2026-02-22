using Serilog;
using UE5DumpUI.Core;

namespace UE5DumpUI.Services;

/// <summary>
/// Serilog-based logging service implementation.
/// Logs to %LOCALAPPDATA%\UE5CEDumper\Logs with per-startup rotation.
/// Each app startup shifts existing logs: -0.log → -1.log → ... → -(N-1).log
/// Matches the DLL-side convention (UE5Dumper-scan-0.log, -1.log, etc.).
/// Supports per-process mirror logging via StartProcessMirror/StopProcessMirror.
/// </summary>
public sealed class LoggingService : ILoggingService, IDisposable
{
    private const string OutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u4}] {Message:lj}{NewLine}{Exception}";

    private readonly Serilog.Core.Logger _logger;
    private readonly string _logDirectory;
    private readonly object _mirrorLock = new();
    private Serilog.Core.Logger? _mirrorLogger;

    public LoggingService(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(logDirectory);

        // Per-startup rotation: shift -0 → -1 → -2 → ... → -(N-1), delete oldest
        RotateLogFiles(logDirectory, Constants.LogFilePrefix, Constants.LogMaxFiles);

        // Clean up old daily format files (UE5DumpUI-YYYYMMDD.log) from previous versions
        CleanupOldDailyLogs(logDirectory, Constants.LogFilePrefix);

        var logFile = Path.Combine(logDirectory, $"{Constants.LogFilePrefix}-0.log");

        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                logFile,
                fileSizeLimitBytes: Constants.LogMaxSizeBytes,
                outputTemplate: OutputTemplate)
            .WriteTo.Console(outputTemplate: "[{Level:u4}] {Message:lj}{NewLine}")
            .CreateLogger();

        _logger.Information("LoggingService initialized, log dir: {LogDir}", logDirectory);
    }

    public void Info(string message)
    {
        _logger.Information(message);
        lock (_mirrorLock) { _mirrorLogger?.Information(message); }
    }

    public void Warn(string message)
    {
        _logger.Warning(message);
        lock (_mirrorLock) { _mirrorLogger?.Warning(message); }
    }

    public void Error(string message)
    {
        _logger.Error(message);
        lock (_mirrorLock) { _mirrorLogger?.Error(message); }
    }

    public void Error(string message, Exception ex)
    {
        _logger.Error(ex, message);
        lock (_mirrorLock) { _mirrorLogger?.Error(ex, message); }
    }

    public void Debug(string message)
    {
        _logger.Debug(message);
        lock (_mirrorLock) { _mirrorLogger?.Debug(message); }
    }

    public void StartProcessMirror(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;

        // Sanitize process name for use as folder name
        var safeName = SanitizeFolderName(processName);
        var mirrorDir = Path.Combine(_logDirectory, safeName);

        try
        {
            Directory.CreateDirectory(mirrorDir);

            // Per-startup rotation for mirror logs too
            RotateLogFiles(mirrorDir, Constants.LogFilePrefix, Constants.MirrorLogMaxFiles);
            CleanupOldDailyLogs(mirrorDir, Constants.LogFilePrefix);

            var mirrorFile = Path.Combine(mirrorDir, $"{Constants.LogFilePrefix}-0.log");

            var newLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    mirrorFile,
                    fileSizeLimitBytes: Constants.LogMaxSizeBytes,
                    outputTemplate: OutputTemplate)
                .CreateLogger();

            lock (_mirrorLock)
            {
                _mirrorLogger?.Dispose();
                _mirrorLogger = newLogger;
            }

            _logger.Information("Process mirror log started: {MirrorDir}", mirrorDir);
            newLogger.Information("Mirror log started for process: {Process}", processName);

            // Clean up old process folders
            CleanupProcessFolders();
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to start process mirror log: {Error}", ex.Message);
        }
    }

    public void StopProcessMirror()
    {
        lock (_mirrorLock)
        {
            if (_mirrorLogger != null)
            {
                _mirrorLogger.Information("Mirror log stopped");
                _mirrorLogger.Dispose();
                _mirrorLogger = null;
            }
        }
    }

    public void Dispose()
    {
        StopProcessMirror();
        _logger.Dispose();
    }

    // ================================================================
    // Per-startup log rotation
    // ================================================================

    /// <summary>
    /// Rotate numbered log files: delete oldest, shift N-2 → N-1, ..., 0 → 1.
    /// After rotation, slot 0 is free for the new session's log.
    /// Matches DLL-side rotation convention (UE5Dumper-scan-0.log, -1.log, ...).
    /// </summary>
    private static void RotateLogFiles(string directory, string prefix, int maxFiles)
    {
        try
        {
            // Delete the oldest file (slot N-1)
            var oldest = Path.Combine(directory, $"{prefix}-{maxFiles - 1}.log");
            if (File.Exists(oldest)) File.Delete(oldest);

            // Shift each file: i → i+1 (from N-2 down to 0)
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
    /// from the previous Serilog RollingInterval.Day configuration.
    /// Also removes the base file without date suffix (UE5DumpUI-.log).
    /// </summary>
    private static void CleanupOldDailyLogs(string directory, string prefix)
    {
        try
        {
            // Match files like "UE5DumpUI-20260222.log" and "UE5DumpUI-.log"
            foreach (var file in Directory.GetFiles(directory, $"{prefix}-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var suffix = name[(prefix.Length + 1)..]; // part after "UE5DumpUI-"

                // Keep numbered files (0, 1, 2, ...) — those are the new format
                if (int.TryParse(suffix, out _)) continue;

                // Delete daily files (YYYYMMDD) and the bare dash file ("")
                try { File.Delete(file); } catch { }
            }
        }
        catch
        {
            // Best effort
        }
    }

    /// <summary>
    /// Remove characters invalid in folder names and trim the extension.
    /// E.g. "ff7rebirth_.exe" -> "ff7rebirth_"
    /// </summary>
    private static string SanitizeFolderName(string name)
    {
        // Remove .exe extension if present
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        // Replace invalid path chars
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
            name = name.Replace(c, '_');

        return string.IsNullOrWhiteSpace(name) ? "unknown" : name;
    }

    /// <summary>
    /// Keep at most MaxProcessFolders subfolders, removing the oldest by last write time.
    /// </summary>
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
                    _logger.Debug("Cleaned up old process log folder: {Folder}", old.Name);
                }
                catch
                {
                    // Best effort — don't fail logging over cleanup
                }
            }
        }
        catch
        {
            // Best effort
        }
    }
}
