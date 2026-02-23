using System.Collections.Generic;

namespace UE5DumpUI.Models;

/// <summary>
/// Result of walk_world: GWorld hierarchy.
/// </summary>
public sealed class WorldWalkResult
{
    public string WorldAddr { get; init; } = "";
    public string WorldName { get; init; } = "";
    public string LevelAddr { get; init; } = "";
    public string LevelName { get; init; } = "";
    /// <summary>Offset of PersistentLevel field within UWorld (e.g., 0x30).</summary>
    public int LevelOffset { get; init; }
    public int ActorCount { get; init; }
    public string Error { get; init; } = "";
    public List<ActorInfo> Actors { get; init; } = new();
}

public sealed class ActorInfo
{
    public string Address { get; init; } = "";
    public string Name { get; init; } = "";
    public string ClassName { get; init; } = "";
    public int Index { get; init; }
    public List<ComponentInfo> Components { get; init; } = new();
}

public sealed class ComponentInfo
{
    public string Address { get; init; } = "";
    public string Name { get; init; } = "";
    public string ClassName { get; init; } = "";
}
