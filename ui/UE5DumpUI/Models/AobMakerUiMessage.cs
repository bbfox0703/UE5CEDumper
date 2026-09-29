using System.Text.Json.Serialization;

namespace UE5DumpUI.Models;

/// <summary>
/// [AOBM-GNAMES-SYMBOL] Wire model for AOBMaker.UI's own pipe (<c>\\.\pipe\AOBMaker</c>), the one that hosts
/// <c>GenerateAob</c>. Kept apart from <see cref="AobMakerMessage"/> on purpose: that one talks to the Cheat Engine
/// plugin, a different server with its own field set. Names follow AOBMaker's <c>PipeMessage</c>.
/// <para>Every reply field is nullable: AOBMaker writes <c>success</c> only when it is true, and <c>pos</c> /
/// <c>aoblen</c> only for a RIP-relative seed, so "absent" is information here, not a default.</para>
/// </summary>
public class AobMakerUiMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    // --- request ---

    [JsonPropertyName("address")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Address { get; set; }

    [JsonPropertyName("processId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProcessId { get; set; }

    // --- reply ---

    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Success { get; set; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonPropertyName("aob")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Aob { get; set; }

    [JsonPropertyName("injectionOffset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? InjectionOffset { get; set; }

    [JsonPropertyName("matchCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MatchCount { get; set; }

    [JsonPropertyName("pos")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Pos { get; set; }

    [JsonPropertyName("aoblen")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AobLen { get; set; }

    [JsonPropertyName("module")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Module { get; set; }
}

/// <summary>Source-generated context for <see cref="AobMakerUiMessage"/> (Native AOT: no reflection).</summary>
[JsonSerializable(typeof(AobMakerUiMessage))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class AobMakerUiJsonContext : JsonSerializerContext
{
}
