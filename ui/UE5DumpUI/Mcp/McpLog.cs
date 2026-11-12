using Serilog;
using UE5DumpUI.Core;

namespace UE5DumpUI.Mcp;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D. The MCP server's log: one file per process in the UI's log folder, and
/// nothing on stdout or stderr.
///
/// The UI's <see cref="Services.LoggingService"/> cannot serve here, for two reasons. It adds a Console sink, which
/// writes to stdout, and stdout carries the JSON-RPC stream. And on construction it archives the UI's own -0.log
/// files, which a running UI holds open. Several AI sessions each start a server of their own, so each process takes
/// a file named for its start time and pid.
///
/// Retention is the folder's age rule (<see cref="Constants.LogMaxAgeDays"/>): the UI's orphan sweep already deletes
/// any aged *.log there, and this process ages out the mcp-*.log files itself, for a machine where only the server
/// runs. Logging never throws: a server that cannot write its log still serves.
/// </summary>
internal sealed class McpLog : ILoggingService, IDisposable
{
    private const string OutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u4}] {Message:lj}{NewLine}{Exception}";

    // The text is a property written as-is, never parsed as a template (LoggingService's [PATH-SERILOG-BRACES]).
    private const string Verbatim = "{Message:l}";

    private readonly Serilog.Core.Logger? _logger;

    /// <summary>This process's log file, or null when it could not be opened.</summary>
    public string? FilePath { get; }

    /// <param name="logRoot">The Logs folder (<see cref="IPlatformService.GetLogDirectoryPath"/>).</param>
    public McpLog(string logRoot)
    {
        try
        {
            var dir = Path.Combine(logRoot, Constants.LogSubfolderName);
            Directory.CreateDirectory(dir);
            PruneAged(dir, DateTime.Now.AddDays(-Constants.LogMaxAgeDays));
            FilePath = Path.Combine(dir,
                $"{Constants.McpLogFilePrefix}{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    FilePath,
                    fileSizeLimitBytes: Constants.LogMaxSizeBytes,
                    // Without rolling, the size limit silently drops every later event (LoggingService's audit #4
                    // B31); without a null count, Serilog would evict by generation, which the age rule replaced.
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: null,
                    outputTemplate: OutputTemplate)
                .CreateLogger();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                      or NotSupportedException)
        {
            _logger = null;
            FilePath = null;
        }
    }

    /// <summary>Deletes this server's own logs (mcp-*.log) last written before <paramref name="cutoff"/>. Another
    /// server's live file is never that old, and a held one refuses the delete.</summary>
    internal static int PruneAged(string dir, DateTime cutoff)
    {
        int deleted = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, Constants.McpLogFilePrefix + "*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) >= cutoff) continue;
                    File.Delete(file);
                    deleted++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return deleted;
    }

    public void Info(string message) => Info(Constants.LogCatView, message);
    public void Warn(string message) => Warn(Constants.LogCatView, message);
    public void Error(string message) => Error(Constants.LogCatView, message);
    public void Error(string message, Exception ex) => Error(Constants.LogCatView, message, ex);
    public void Debug(string message) => Debug(Constants.LogCatView, message);

    public void Info(string category, string message) => _logger?.Information(Verbatim, Tag(category, message));
    public void Warn(string category, string message) => _logger?.Warning(Verbatim, Tag(category, message));
    public void Error(string category, string message) => _logger?.Error(Verbatim, Tag(category, message));
    public void Error(string category, string message, Exception ex)
        => _logger?.Error(ex, Verbatim, Tag(category, message));
    public void Debug(string category, string message) => _logger?.Debug(Verbatim, Tag(category, message));

    // One file holds every category, so each line names its own.
    private static string Tag(string category, string message) => $"[{category}] {message}";

    // The UI mirrors its log into a per-game folder on connect; this process has one file and no mirror.
    public void StartProcessMirror(string processName) { }
    public void StopProcessMirror() { }

    public void Dispose() => _logger?.Dispose();
}
