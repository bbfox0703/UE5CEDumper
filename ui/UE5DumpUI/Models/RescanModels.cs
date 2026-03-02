namespace UE5DumpUI.Models;

/// <summary>
/// Result from the "rescan" command — indicates what targets are being scanned.
/// </summary>
public sealed class RescanStartResult
{
    public bool ScanningGObjects { get; init; }
    public bool ScanningGWorld { get; init; }
}

/// <summary>
/// Result from the "rescan_status" command — polling response for scan progress.
/// </summary>
public sealed class RescanStatusResult
{
    /// <summary>True while the background scan thread is running.</summary>
    public bool Running { get; init; }

    /// <summary>Phase: 0=idle, 1=GObjects, 2=GWorld, 3=complete.</summary>
    public int Phase { get; init; }

    /// <summary>Human-readable status text (e.g. "Scanning GObjects (.data heuristic)...").</summary>
    public string StatusText { get; init; } = "";

    /// <summary>True if GObjects was found by Extra Scan.</summary>
    public bool FoundGObjects { get; init; }

    /// <summary>True if GWorld was found by Extra Scan.</summary>
    public bool FoundGWorld { get; init; }

    /// <summary>GObjects address found (hex string), empty if not found.</summary>
    public string GObjectsAddr { get; init; } = "";

    /// <summary>GWorld address found (hex string), empty if not found.</summary>
    public string GWorldAddr { get; init; } = "";

    /// <summary>Scan method used for GObjects (e.g. "data_heuristic").</summary>
    public string GObjectsMethod { get; init; } = "";

    /// <summary>Scan method used for GWorld (e.g. "instance_scan").</summary>
    public string GWorldMethod { get; init; } = "";
}
