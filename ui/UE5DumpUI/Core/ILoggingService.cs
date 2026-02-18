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
}
