using Serilog;
using UE5DumpUI.Core;

namespace UE5DumpUI.Services;

/// <summary>
/// Serilog-based logging service implementation.
/// Logs to %LOCALAPPDATA%\UE5CEDumper\Logs with 5MB rolling, 5 versions.
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

        var logPath = Path.Combine(logDirectory, $"{Constants.LogFilePrefix}-.log");

        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Constants.LogMaxFiles,
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
            var mirrorPath = Path.Combine(mirrorDir, $"{Constants.LogFilePrefix}-.log");

            var newLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    mirrorPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: Constants.MirrorLogMaxFiles,
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
