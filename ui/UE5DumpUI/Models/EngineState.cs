namespace UE5DumpUI.Models;

/// <summary>
/// Represents the current state of the UE5 engine connection.
/// </summary>
public sealed class EngineState
{
    public int UEVersion { get; init; }
    public bool VersionDetected { get; init; } = true;
    public string GObjectsAddr { get; init; } = "";
    public string GNamesAddr { get; init; } = "";
    public string GWorldAddr { get; init; } = "";
    public int ObjectCount { get; init; }
    public string ModuleName { get; init; } = "";
    public string ModuleBase { get; init; } = "";

    /// <summary>How each pointer was found: "aob", "data_scan", "string_ref", "pointer_scan", "not_found"</summary>
    public string GObjectsMethod { get; init; } = "aob";
    public string GNamesMethod { get; init; } = "aob";
    public string GWorldMethod { get; init; } = "aob";

    // --- AOB Usage Tracking ---
    /// <summary>PE hash: TimeDateStamp + SizeOfImage (16 hex chars). Unique per game build.</summary>
    public string PeHash { get; init; } = "";

    /// <summary>Winning pattern ID for each target (e.g. "GOBJ_V1"). Empty if fallback was used.</summary>
    public string GObjectsPatternId { get; init; } = "";
    public string GNamesPatternId { get; init; } = "";
    public string GWorldPatternId { get; init; } = "";

    /// <summary>AOB scan hit addresses (instruction that references the pointer).</summary>
    public string GObjectsScanAddr { get; init; } = "";
    public string GNamesScanAddr { get; init; } = "";
    public string GWorldScanAddr { get; init; } = "";

    /// <summary>Per-target scan statistics.</summary>
    public int GObjectsPatternsTried { get; init; }
    public int GObjectsPatternsHit { get; init; }
    public int GNamesPatternsTried { get; init; }
    public int GNamesPatternsHit { get; init; }
    public int GWorldPatternsTried { get; init; }
    public int GWorldPatternsHit { get; init; }
}
