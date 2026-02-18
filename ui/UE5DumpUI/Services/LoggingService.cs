using Serilog;
using UE5DumpUI.Core;

namespace UE5DumpUI.Services;

/// <summary>
/// Serilog-based logging service implementation.
/// Logs to %LOCALAPPDATA%\UE5CEDumper\Logs with 5MB rolling, 5 versions.
/// </summary>
public sealed class LoggingService : ILoggingService, IDisposable
{
    private readonly Serilog.Core.Logger _logger;

    public LoggingService(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);

        var logPath = Path.Combine(logDirectory, $"{Constants.LogFilePrefix}-.log");

        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Constants.LogMaxFiles,
                fileSizeLimitBytes: Constants.LogMaxSizeBytes,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u4}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console(outputTemplate: "[{Level:u4}] {Message:lj}{NewLine}")
            .CreateLogger();

        _logger.Information("LoggingService initialized, log dir: {LogDir}", logDirectory);
    }

    public void Info(string message) => _logger.Information(message);
    public void Warn(string message) => _logger.Warning(message);
    public void Error(string message) => _logger.Error(message);
    public void Error(string message, Exception ex) => _logger.Error(ex, message);
    public void Debug(string message) => _logger.Debug(message);

    public void Dispose() => _logger.Dispose();
}
