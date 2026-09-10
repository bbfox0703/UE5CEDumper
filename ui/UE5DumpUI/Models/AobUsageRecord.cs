using System.Text.Json.Serialization;

namespace UE5DumpUI.Models;

/// <summary>
/// Represents a single scan result for one target (GObjects/GNames/GWorld).
/// </summary>
public sealed class AobScanEntry
{
    /// <summary>Scan method: "aob", "data_scan", "string_ref", "pointer_scan", "not_found".</summary>
    public string Method { get; set; } = "";

    /// <summary>Winning pattern ID (e.g. "GOBJ_V1"). Empty if fallback was used.</summary>
    public string PatternId { get; set; } = "";

    /// <summary>Number of patterns tried during the scan.</summary>
    public int PatternsTried { get; set; }

    /// <summary>Number of patterns that had at least one match.</summary>
    public int PatternsHit { get; set; }
}

/// <summary>
/// Persisted record for a single game, keyed by PE hash.
/// Tracks which AOB patterns worked for this game across scan sessions.
/// </summary>
public sealed class AobUsageRecord
{
    /// <summary>PE hash: TimeDateStamp + SizeOfImage (16 hex chars).</summary>
    public string PeHash { get; set; } = "";

    /// <summary>Game executable name (e.g. "MyGame-Win64-Shipping.exe").</summary>
    public string GameName { get; set; } = "";

    /// <summary>Detected UE version (e.g. 504 = UE5.4).</summary>
    public int UEVersion { get; set; }

    /// <summary>Whether UE version was detected from PE resources.</summary>
    public bool VersionDetected { get; set; }

    /// <summary>
    /// Cached low-confidence flag (Tier 3 bare-pattern OR publisher-bias fallback).
    /// Mirrors EngineState.IsLowConfidence. The DLL restores this on a cache hit so the
    /// "set an override" nudge survives even when version detection is skipped.
    /// </summary>
    public bool LowConfidence { get; set; }

    /// <summary>
    /// Detection-logic revision that produced <see cref="UEVersion"/> (DLL Genau::kVersionDetectLogicRev).
    /// DLL-authoritative — the DLL stamps it; the UI only round-trips it. RecordScanAsync MUST NOT
    /// touch it (like <see cref="UEVersionUserOverride"/>), otherwise the DLL's stamp is erased and
    /// the next launch re-runs the slow version detection. 0 = absent / stamped by an older DLL.
    /// </summary>
    public int VersionDetectRev { get; set; }

    /// <summary>
    /// User-set persistent UE version override (0 / unset = no override).
    /// Written by the DLL via the set_ue_version_override pipe cmd.
    /// AobUsageService preserves this on round-trip — RecordScanAsync MUST NOT touch it.
    /// </summary>
    public int UEVersionUserOverride { get; set; }

    /// <summary>UTC timestamp the override was last set (ISO 8601). Empty when not set.</summary>
    public string UEVersionUserOverrideAt { get; set; } = "";

    /// <summary>
    /// Per-game GameThreadDispatch invoke timeout in milliseconds (0 = use Stark default of 5000ms).
    /// Written by the DLL via the set_invoke_timeout pipe cmd.
    /// AobUsageService preserves this on round-trip — RecordScanAsync MUST NOT touch it.
    /// </summary>
    public int InvokeTimeoutMs { get; set; }

    /// <summary>UTC timestamp the invoke timeout was last set (ISO 8601). Empty when not set.</summary>
    public string InvokeTimeoutMsAt { get; set; } = "";

    /// <summary>Scan results for each target.</summary>
    public AobScanEntry GObjects { get; set; } = new();
    public AobScanEntry GNames { get; set; } = new();
    public AobScanEntry GWorld { get; set; } = new();

    /// <summary>UTC timestamp of the most recent scan.</summary>
    public string LastScanUtc { get; set; } = "";

    /// <summary>How many times this game has been scanned.</summary>
    public int ScanCount { get; set; }
}

/// <summary>
/// Root object for the per-machine JSON usage file.
/// </summary>
public sealed class AobUsageFile
{
    /// <summary>Schema version for forward compatibility.</summary>
    /// <remarks>Always written, and said so explicitly: the context below omits type-default
    /// values (<c>WhenWritingDefault</c>), and a schema version must never be silently dropped and
    /// re-initialized on reload. Behaviour-neutral today — 1 is not 0 — but it states the intent
    /// tools/check_json_default_ignore.py enforces. ⚠ Do NOT drop <c>WhenWritingDefault</c> from this
    /// context instead: the file is shared with the DLL's Flamme, and writing every zero-valued field
    /// would change a cross-language file for no gain.</remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int Version { get; set; } = 1;

    /// <summary>Machine name that generated this file.</summary>
    public string MachineName { get; set; } = "";

    /// <summary>Per-game records keyed by PE hash.</summary>
    public Dictionary<string, AobUsageRecord> Games { get; set; } = new();
}

/// <summary>
/// Source-generated JSON serializer context for AOT/trimming compatibility.
/// System.Text.Json reflection is disabled in this app — all types must be registered here.
/// </summary>
[JsonSerializable(typeof(AobUsageFile))]
[JsonSerializable(typeof(AobUsageRecord))]
[JsonSerializable(typeof(AobScanEntry))]
[JsonSerializable(typeof(Dictionary<string, AobUsageRecord>))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
internal partial class AobUsageJsonContext : JsonSerializerContext
{
}
