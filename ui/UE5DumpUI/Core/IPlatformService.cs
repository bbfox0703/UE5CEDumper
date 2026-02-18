namespace UE5DumpUI.Core;

/// <summary>
/// Platform-specific operations interface.
/// All OS-dependent calls must go through this interface.
/// </summary>
public interface IPlatformService
{
    /// <summary>Try to acquire single-instance mutex. Returns true if this is the first instance.</summary>
    bool TryAcquireSingleInstance();

    /// <summary>Release the single-instance mutex.</summary>
    void ReleaseSingleInstance();

    /// <summary>Get the application data folder path (%APPDATA%).</summary>
    string GetAppDataPath();

    /// <summary>Get the log directory path.</summary>
    string GetLogDirectoryPath();

    /// <summary>Copy text to clipboard.</summary>
    Task CopyToClipboardAsync(string text);
}
