using System.Text.Json.Serialization;

namespace UE5DumpUI.Models;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] The UE5DumpUI window's "Block AI writes" switch, persisted in
/// %LOCALAPPDATA%\UE5CEDumper\ai-writes.json, which every MCP server process reads on every call that would change the
/// game. A file of its own rather than a key in experimental.json: that file is rewritten whole from memory by its
/// gate, so a quota change there would put back a stale stamp.
/// </summary>
public sealed class AiWriteBlockSettings
{
    /// <summary>While true, every MCP server refuses every change but an OFF or a reset of its own hold.</summary>
    public bool Blocked { get; set; }

    /// <summary>Bumped at every turn-on. A server that sees a stamp other than the one it saw last ends every grant,
    /// so even an on and off between two of its calls revokes; the comparison is inequality, never "newer", because
    /// a deleted file restarts the count.</summary>
    public long RevokeStamp { get; set; }
}

/// <summary>The switch as a server reads it. <see cref="Unreadable"/>: the file exists but could not be read or parsed,
/// which a server treats as blocked, in words of its own.</summary>
public readonly record struct AiWriteBlockState(bool Blocked, long RevokeStamp, bool Unreadable);

/// <summary>
/// Source-generated JSON context (reflection-based JSON is disabled in the trimmed AOT build).
/// </summary>
/// <remarks>⛔ No <c>DefaultIgnoreCondition = WhenWritingDefault</c>: <c>blocked: false</c> and <c>revokeStamp: 0</c>
/// must be written, not omitted (tools/check_json_default_ignore.py; ExperimentalSettings' context says what an
/// omitted default breaks).
/// </remarks>
[JsonSerializable(typeof(AiWriteBlockSettings))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class AiWriteBlockJsonContext : JsonSerializerContext
{
}
