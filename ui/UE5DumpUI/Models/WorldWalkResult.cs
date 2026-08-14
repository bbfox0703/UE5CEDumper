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
    /// <summary>Actors in THIS page — the DLL clamps to the requested limit.</summary>
    public int ActorCount { get; init; }
    /// <summary>The level's real actor-array element count, or -1 when the array was
    /// never read (pre-2818 DLL, or the Actors property was not found — see
    /// <see cref="Error"/>). Test for &lt; 0, not for 0. (audit #5 D5/F6)</summary>
    public int ActorTotal { get; init; } = -1;
    /// <summary>The actor list is a page: <see cref="ActorTotal"/> exceeds the limit.</summary>
    public bool Truncated { get; init; }
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
