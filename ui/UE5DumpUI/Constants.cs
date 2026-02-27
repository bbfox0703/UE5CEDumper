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

    // Logging — category-routed to separate files
    public const string LogFolderName = "UE5CEDumper";
    public const string LogSubFolder = "Logs";
    public const string LogSubfolderName = "UE5DumpUI";      // UI module subfolder under Logs/
    public const string MirrorLogPrefix = "ui";               // Prefix for mirror files in game folders
    public const int LogRotateMax = 2;                        // 2-file rotation per category
    public const long LogMaxSizeBytes = 8 * 1024 * 1024;     // 8MB per file

    // Per-process mirror logging
    public const int MaxProcessFolders = 20;           // Clean up oldest beyond this
    public const int LogMaxAgeDays = 15;               // Delete log folders older than this

    // Log category names
    public const string LogCatInit = "init";
    public const string LogCatPipe = "pipe";
    public const string LogCatView = "view";

    // Pipe Communication
    public const int PipeConnectTimeoutMs = 5000;
    public const int DefaultPageSize = 200;
    public const int DefaultHexViewSize = 256;
    public const int DefaultWatchIntervalMs = 500;

    // Object Tree
    public const int ObjectTreePageSize = 2000;     // Batch size for loading all objects
    public const int ObjectTreeMaxDisplay = 5000;   // Max items shown in FilteredNodes ListBox

    // Live Walker auto-refresh
    public const int DefaultAutoRefreshIntervalSec = 10;
    public const int MinAutoRefreshIntervalSec = 8;
    public const int AutoRefreshBenchmarkBufferSec = 5; // Extra seconds added to benchmarked duration

    // UI
    public const int DefaultWindowWidth = 1400;
    public const int DefaultWindowHeight = 900;
    public const int TreePanelWidth = 350;
}
