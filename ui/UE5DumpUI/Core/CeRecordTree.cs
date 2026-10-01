namespace UE5DumpUI.Core;

/// <summary>
/// [AOBM-PLUSCE-FIDELITY] One record for the CE plugin's <c>CreateRecordTreeBegin</c> / <c>Chunk</c> / <c>End</c> batch.
/// It exists because <c>CreateMemoryRecord</c> carries only an address, a type and two display flags, so a bit-field
/// bool could only arrive as its whole byte and an FString as its 8-byte data pointer. A tree node carries the bits,
/// the pointer hop and the string length as well.
/// <para><see cref="Offsets"/> is in CE's .CT order: element 0 is the offset CE adds last.</para>
/// </summary>
public sealed record CeRecordNode(
    string Description,
    string Address,
    string TypeKeyword,
    bool ShowAsHex = false,
    bool IsSigned = false,
    int[]? Offsets = null,
    int? StringLength = null,
    bool Unicode = false,
    int? BitStart = null,
    int? BitLength = null)
{
    /// <summary>The plugin feature a node needs beyond the base batch, or null. A plugin that does not list it would
    /// build the record without the field it does not know and still answer success.</summary>
    public string? RequiredFeature => BitStart != null ? CeRecordTreeFeatures.BinaryBits : null;
}

/// <summary>[AOBM-PLUSCE-FIDELITY] The feature names the plugin lists in its <c>CreateRecordTreeBegin</c> reply.</summary>
public static class CeRecordTreeFeatures
{
    /// <summary>Since AOBMaker build 155: <c>bitStart</c> / <c>bitLength</c> reach <c>Binary.Startbit</c> / <c>Size</c>.</summary>
    public const string BinaryBits = "bulk.binaryBits";
}

/// <summary>[AOBM-PLUSCE-FIDELITY] How a record-tree push ended.</summary>
public enum RecordTreeOutcome
{
    /// <summary>Every node became a record.</summary>
    Created,
    /// <summary>The plugin has no record tree at all: it answered the Begin with "Unknown type", or this client
    /// cannot send one.</summary>
    Unsupported,
    /// <summary>The plugin has the tree but not a feature a node needs; the batch was closed before any record was
    /// made.</summary>
    MissingFeature,
    /// <summary>Cheat Engine was reached and did not make every record. Records it did make stay: the plugin does not
    /// roll a batch back.</summary>
    Failed,
    /// <summary>The bridge could not be reached.</summary>
    Unavailable,
}

/// <summary>[AOBM-PLUSCE-FIDELITY] The answer to a record-tree push.</summary>
public sealed record RecordTreeResult(RecordTreeOutcome Outcome, int Created, string? Message,
                                      IReadOnlyList<string> MissingFeatures)
{
    public static RecordTreeResult NotSupported { get; } =
        new(RecordTreeOutcome.Unsupported, 0, null, Array.Empty<string>());
}
