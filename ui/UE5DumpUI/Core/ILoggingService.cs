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
    /// with 2-version rotation.
    /// </summary>
    void StartProcessMirror(string processName);

    /// <summary>
    /// Stop mirroring log output to the per-process subfolder.
    /// Call on pipe disconnect.
    /// </summary>
    void StopProcessMirror();
}
