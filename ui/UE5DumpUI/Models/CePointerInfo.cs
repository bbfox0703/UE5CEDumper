namespace UE5DumpUI.Models;

/// <summary>
/// CE pointer chain information for a GObjects instance.
/// </summary>
public sealed class CePointerInfo
{
    public string Module { get; init; } = "";
    public string ModuleBase { get; init; } = "";
    public string GObjectsRva { get; init; } = "";
    public int InternalIndex { get; init; }
    public int ChunkIndex { get; init; }
    public int WithinChunk { get; init; }
    public int FieldOffset { get; init; }

    /// <summary>CE offset chain (bottom-to-top): field, withinChunk*16, chunkIndex*8, 0.</summary>
    public int[] CeOffsets { get; init; } = [];

    /// <summary>CE base address string, e.g. "Game.exe"+1BA1820.</summary>
    public string CeBase { get; init; } = "";
}
