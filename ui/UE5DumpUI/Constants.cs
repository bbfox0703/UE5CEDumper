namespace UE5DumpUI;

/// <summary>
/// Centralized constants for the UI application.
/// </summary>
public static class Constants
{
    // Named Pipe
    public const string PipeName = "UE5DumpBfx";

    // Application
    public const string AppName = "UE5DumpUI";
    public const string MutexName = "Global\\UE5DumpUI_SingleInstance";
    public const string AppVersion = "1.0.0";

    // Logging
    public const string LogFolderName = "UE5CEDumper";
    public const string LogSubFolder = "Logs";
    public const string LogFilePrefix = "UE5DumpUI";
    public const int LogMaxFiles = 5;
    public const long LogMaxSizeBytes = 5 * 1024 * 1024; // 5MB

    // Per-process mirror logging
    public const int MirrorLogMaxFiles = 2;           // 2-version rotation per process
    public const int MaxProcessFolders = 20;           // Clean up oldest beyond this

    // Pipe Communication
    public const int PipeConnectTimeoutMs = 5000;
    public const int DefaultPageSize = 200;
    public const int DefaultHexViewSize = 256;
    public const int DefaultWatchIntervalMs = 500;

    // UI
    public const int DefaultWindowWidth = 1400;
    public const int DefaultWindowHeight = 900;
    public const int TreePanelWidth = 350;
}
