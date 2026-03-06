namespace UE5DumpUI.Models;

/// <summary>
/// Result from the "scan_status" command — polling response for trigger_scan progress.
/// </summary>
public sealed class ScanStatusResult
{
    /// <summary>True while the background scan thread is running.</summary>
    public bool Running { get; init; }

    /// <summary>Phase: 0=idle, 1=version, 2=GObjects, 3=GNames, 4=GWorld, 5=init, 6=dynoff, 7=complete.</summary>
    public int Phase { get; init; }

    /// <summary>Human-readable status text (e.g. "Scanning GObjects...").</summary>
    public string StatusText { get; init; } = "";

    /// <summary>Full engine state when scan is complete (phase >= 7). Null while scanning.</summary>
    public EngineState? EngineState { get; init; }
}
