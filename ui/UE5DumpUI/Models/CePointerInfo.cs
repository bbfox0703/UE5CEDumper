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

    /// <summary>
    /// CE offset chain (bottom-to-top). Direct layouts: [field, withinChunk*itemSize+objOffset,
    /// chunkIndex*8], off a <see cref="CeBase"/> at the array's Objects field. Under
    /// <see cref="PackedLayout"/> the GObjects-relative chain cannot be expressed, so this degrades
    /// to a single [field] hop off an absolute <see cref="CeBase"/>.
    /// </summary>
    public int[] CeOffsets { get; init; } = [];

    /// <summary>CE base address string: the array's Objects field, e.g. "Game.exe"+1BA1830 (GObjects plus
    /// the layout's Objects offset, since CE reads the base before its first offset), or an absolute
    /// address when packed.</summary>
    public string CeBase { get; init; } = "";

    /// <summary>
    /// True when the game uses the UE5.7+ *** UNVERIFIED *** packed FUObjectItem layout. The
    /// GObjects-relative pointer chain is unavailable (bit-packed object pointer); CeBase is the
    /// absolute object address and won't survive a restart / ASLR rebase. See <see cref="Warning"/>.
    /// </summary>
    public bool PackedLayout { get; init; }

    /// <summary>Human-readable caveat populated when <see cref="PackedLayout"/> is true (else empty).</summary>
    public string Warning { get; init; } = "";
}
