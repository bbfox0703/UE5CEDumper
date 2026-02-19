namespace UE5DumpUI.Models;

/// <summary>
/// A single instance found by FindInstances.
/// </summary>
public sealed class InstanceResult
{
    public string Address { get; init; } = "";
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string OuterAddr { get; init; } = "";
}
