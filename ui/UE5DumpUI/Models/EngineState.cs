namespace UE5DumpUI.Models;

/// <summary>
/// Represents the current state of the UE5 engine connection.
/// </summary>
public sealed class EngineState
{
    public int UEVersion { get; init; }
    public string GObjectsAddr { get; init; } = "";
    public string GNamesAddr { get; init; } = "";
    public string GWorldAddr { get; init; } = "";
    public int ObjectCount { get; init; }
}
