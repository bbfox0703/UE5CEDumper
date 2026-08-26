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
    /// <summary>True when the DLL judged the class pointer recycled/garbage (implausible PropertiesSize) — the instance was freed and its slot reused. No fields are returned and fill_gaps must NOT be retried.</summary>
    public bool IsStale { get; init; }
    /// <summary>
    /// The reflected walk RAN and <see cref="Fields"/> is complete; only the DLL's
    /// Guess-What raw-byte pass was skipped, because the class is larger than the
    /// gap-fill work cap. NOT staleness — conflating the two is what reported a live
    /// 3.6 MB USaveGame to the user as freed. (SANEPROPS-2026-08-26)
    /// </summary>
    public bool GapFillSkipped { get; init; }
    /// <summary>UStruct::PropertiesSize — total struct/class size in bytes. Used to detect 0-field classes that should auto-enable fill_gaps.</summary>
    public int PropertiesSize { get; init; }
    public List<LiveFieldValue> Fields { get; init; } = new();
}
