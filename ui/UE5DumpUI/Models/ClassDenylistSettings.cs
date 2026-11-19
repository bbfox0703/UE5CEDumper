using System.Text.Json.Serialization;

namespace UE5DumpUI.Models;

/// <summary>
/// Which experimental tab a class denylist belongs to. Each tab keeps its OWN
/// independent list — a class hidden in SPC Query does NOT affect Snapshot Diff
/// or Class Pivot (the user asked for per-tab isolation). All three persist into
/// one JSON file per game (see <see cref="ClassDenylistSettings"/>).
/// </summary>
public enum DenylistScope
{
    Diff,
    Spc,
    Pivot,
}

/// <summary>
/// Per-game class denylists — class FQNs the user marked as noise, kept
/// SEPARATELY per experimental tab. Persisted next to the per-game snapshot DB
/// (snapshots.&lt;pe_hash&gt;.denylist.json) so they auto-follow the game and
/// survive DB FIFO eviction (eviction drops snapshots, not the noise picks).
/// </summary>
public sealed class ClassDenylistSettings
{
    /// <summary>Classes hidden on the Snapshot Diff tab.</summary>
    public List<string> Diff { get; set; } = new();
    /// <summary>Classes hidden on the SPC Query tab.</summary>
    public List<string> Spc { get; set; } = new();
    /// <summary>Classes hidden from the Class Pivot picker (right-click "Hide").</summary>
    public List<string> Pivot { get; set; } = new();
}

/// <summary>Source-generated JSON context for AOT/trim safety.</summary>
[JsonSerializable(typeof(ClassDenylistSettings))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
internal partial class ClassDenylistJsonContext : JsonSerializerContext
{
}
