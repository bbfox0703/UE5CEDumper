namespace UE5DumpUI.Core;

/// <summary>
/// Platform-independent logging interface with category-based routing.
/// Categories route to separate log files:
///   "init" — app lifecycle, version, connection events → init.log
///   "pipe" — pipe TX/RX, connect/disconnect           → pipe.log
///   "view" — UI operations, search, export (default)  → view.log
/// </summary>
public interface ILoggingService
{
    /// <summary>Log at INFO level to the default category ("view").</summary>
    void Info(string message);

    /// <summary>Log at WARN level to the default category ("view").</summary>
    void Warn(string message);

    /// <summary>Log at ERROR level to the default category ("view").</summary>
    void Error(string message);

    /// <summary>Log at ERROR level with exception to the default category ("view").</summary>
    void Error(string message, Exception ex);

    /// <summary>Log at DEBUG level to the default category ("view").</summary>
    void Debug(string message);

    /// <summary>Log at INFO level to a specific category file.</summary>
    void Info(string category, string message);

    /// <summary>Log at WARN level to a specific category file.</summary>
    void Warn(string category, string message);

    /// <summary>Log at ERROR level to a specific category file.</summary>
    void Error(string category, string message);

    /// <summary>Log at ERROR level with exception to a specific category file.</summary>
    void Error(string category, string message, Exception ex);

    /// <summary>Log at DEBUG level to a specific category file.</summary>
    void Debug(string category, string message);

    /// <summary>
    /// Start mirroring log output to a per-process subfolder.
    /// Call on pipe connect with the game process name.
    /// Creates &lt;logDir&gt;/&lt;processName&gt;/ui-{init,pipe,view}-0.log
    /// with the same AGE-based retention as the root logs: the previous session's -0.log is
    /// archived to -YYYYMMDD-HHMMSS.log and archives older than Constants.LogMaxAgeDays are
    /// deleted. (audit #5 Z17 — this said "2-version rotation", the generation-count policy
    /// that CLAUDE.md's app-data rule explicitly replaced and explains cannot express the
    /// requirement: rotation runs on every process start, so N launches evict everything
    /// earlier regardless of date. The code has always followed the age rule.)
    /// </summary>
    void StartProcessMirror(string processName);

    /// <summary>
    /// Stop mirroring log output to the per-process subfolder.
    /// Call on pipe disconnect.
    /// </summary>
    void StopProcessMirror();
}
