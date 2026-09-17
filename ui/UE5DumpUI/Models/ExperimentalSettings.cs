using System.Text.Json.Serialization;

namespace UE5DumpUI.Models;

/// <summary>
/// Persisted opt-in flag for the experimental analysis tabs (Snapshot /
/// SPC Query / Class Pivot). Stored as a tiny JSON file under
/// %LOCALAPPDATA%\UE5CEDumper\experimental.json so the choice survives
/// restarts. See <see cref="Services.ExperimentalGate"/>.
/// </summary>
public sealed class ExperimentalSettings
{
    /// <summary>True once the user ticks the System-tab credit checkbox.</summary>
    public bool Enabled { get; set; }

    /// <summary>Per-game snapshot DB size cap, in MB. When a capture pushes the
    /// active game's DB over this, the oldest snapshots are auto-dropped (FIFO).
    /// 0 = unlimited. Default 1 GB.</summary>
    public int SnapshotQuotaMb { get; set; } = 1024;
}

/// <summary>
/// Source-generated JSON serializer context for AOT/trimming compatibility.
/// System.Text.Json reflection is disabled in this app — all types must be
/// registered here.
/// </summary>
/// <remarks>
/// ⛔ NO <c>DefaultIgnoreCondition = WhenWritingDefault</c> on this context. It compares against
/// <c>default(T)</c>, NOT against the initializer: "Unlimited" is <c>SnapshotQuotaMb = 0</c> =
/// <c>default(int)</c>, so the key was OMITTED from the file, the next launch re-ran the
/// <c>= 1024</c> initializer, and the snapshot store then FIFO-deleted the snapshots the user had
/// opted to keep — with a second entrance needing no user action, because <c>ApplyAutoQuota</c>
/// picks Unlimited by itself once the retained set outgrows the top preset. [W1-QUOTA-UNLIMITED].
/// The rule is written in docs/teleport-coord-library-spec.md ("MUST NOT be WhenWritingDefault")
/// and enforced by tools/check_json_default_ignore.py. Writing <c>"enabled": false</c> as well is
/// harmless: old and new builds read it identically.
/// </remarks>
[JsonSerializable(typeof(ExperimentalSettings))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class ExperimentalSettingsJsonContext : JsonSerializerContext
{
}
