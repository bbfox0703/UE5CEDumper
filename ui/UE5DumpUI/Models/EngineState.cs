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
}
