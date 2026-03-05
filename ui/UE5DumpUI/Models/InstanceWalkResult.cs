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
    /// <summary>True when viewing a class/struct definition (not a live instance). Field offsets are schema-relative, not absolute addresses.</summary>
    public bool IsDefinition { get; init; }
    public List<LiveFieldValue> Fields { get; init; } = new();
}
