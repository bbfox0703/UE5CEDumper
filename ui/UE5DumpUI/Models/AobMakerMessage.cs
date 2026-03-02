using System.Text.Json.Serialization;

namespace UE5DumpUI.Models;

/// <summary>
/// Minimal pipe message model for AOBMaker CE Plugin bridge.
/// Wire format: 4-byte LE uint32 length prefix + UTF-8 JSON payload.
/// Only includes fields used by UE5DumpUI (NavigateHexView, DisassembleRange).
/// </summary>
public class AobMakerMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("address")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Address { get; set; }

    [JsonPropertyName("countBefore")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int CountBefore { get; set; }

    [JsonPropertyName("countAfter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int CountAfter { get; set; }

    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }
}

/// <summary>
/// System.Text.Json source generator context for AOBMaker bridge messages (Native AOT compatible).
/// </summary>
[JsonSerializable(typeof(AobMakerMessage))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class AobMakerJsonContext : JsonSerializerContext;
