using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

namespace UE5DumpUI.Models;

/// <summary>
/// Minimal pipe message model for AOBMaker CE Plugin bridge.
/// Wire format: 4-byte LE uint32 length prefix + UTF-8 JSON payload.
/// Includes fields for NavigateHexView, NavigateDisassembler, CreateAAScript, and CreateSymbolScript.
/// </summary>
public class AobMakerMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("address")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Address { get; set; }

    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    // --- CreateAAScript fields ---

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("script")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Script { get; set; }

    [JsonPropertyName("autoActivate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AutoActivate { get; set; }

    // --- CreateSymbolScript fields ---
    // CE Plugin's BuildSymbolScanScript() generates an AA script from these AOB parameters.

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("aob")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Aob { get; set; }

    [JsonPropertyName("pos")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Pos { get; set; }

    [JsonPropertyName("aoblen")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AobLen { get; set; }

    [JsonPropertyName("symbol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Symbol { get; set; }

    [JsonPropertyName("module")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Module { get; set; }
}

/// <summary>
/// System.Text.Json source generator context for AOBMaker bridge messages (Native AOT compatible).
/// Provides a <see cref="Relaxed"/> instance with <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>
/// to avoid \uXXXX encoding of single quotes, angle brackets, and non-ASCII characters —
/// CE Plugin's Lua JSON parser doesn't handle \uXXXX escapes properly.
/// </summary>
[JsonSerializable(typeof(AobMakerMessage))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class AobMakerJsonContext : JsonSerializerContext
{
    private static AobMakerJsonContext? _relaxed;

    /// <summary>
    /// Context instance using UnsafeRelaxedJsonEscaping — avoids \uXXXX for characters
    /// like single quotes, angle brackets, and non-ASCII. Use for script content transmission.
    /// </summary>
    public static AobMakerJsonContext Relaxed => _relaxed ??= new(new System.Text.Json.JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    });
}
