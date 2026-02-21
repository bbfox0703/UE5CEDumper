using System.Collections.Generic;

namespace UE5DumpUI.Models;

/// <summary>
/// Result of walk_instance: live field values for a UObject.
/// </summary>
public sealed class InstanceWalkResult
{
    public string Address { get; init; } = "";
    public string Name { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string ClassAddr { get; init; } = "";
    public string OuterAddr { get; init; } = "";
    public string OuterName { get; init; } = "";
    public string OuterClassName { get; init; } = "";
    public List<LiveFieldValue> Fields { get; init; } = new();
}
