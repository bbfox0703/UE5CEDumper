namespace UE5DumpUI.Core;

/// <summary>
/// Platform-independent logging interface.
/// </summary>
public interface ILoggingService
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
    void Error(string message, Exception ex);
    void Debug(string message);

    /// <summary>
    /// Start mirroring log output to a per-process subfolder.
    /// Call on pipe connect with the game process name.
    /// Creates &lt;logDir&gt;/&lt;processName&gt;/UE5DumpUI-.log with 2-version rotation.
    /// </summary>
    void StartProcessMirror(string processName);

    /// <summary>
    /// Stop mirroring log output to the per-process subfolder.
    /// Call on pipe disconnect.
    /// </summary>
    void StopProcessMirror();
}
