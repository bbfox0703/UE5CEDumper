using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

namespace UE5DumpUI.Models;

/// <summary>
/// Minimal pipe message model for AOBMaker CE Plugin bridge.
/// Wire format: 4-byte LE uint32 length prefix + UTF-8 JSON payload.
/// One class carries the request and reply fields of every command this app sends; each is written only when set.
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

    // Optional target group-node description. Non-empty → the AA-script record is
    // nested under a single-level IsGroupHeader folder of that description (created
    // if absent; a Type-11 script name-collision yields a fresh group). Omitted →
    // address-list root (back-compatible). Handled by the AOBMaker CE plugin's
    // CreateAAScript handler (docs/aobmaker-integration.md).
    [JsonPropertyName("group")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Group { get; set; }

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

    // --- InjectTableFile fields ---
    // Embeds an arbitrary text/Lua file directly into the currently open
    // CE table via findTableFile + createTableFile + Stream.write.

    [JsonPropertyName("fileName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileName { get; set; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }

    // --- CreateMemoryRecord fields ---
    // Adds a single typed memory record to CE's address list via the plugin's
    // addresslist.createMemoryRecord(). ValueType is a CE TVariableType code
    // (0=Byte,1=Word,2=Dword,3=Qword,4=Single,5=Double,6=String,7=UnicodeString,
    // 8=ByteArray,9=Binary). ShowAsHex requires an AOBMaker CE plugin compiled
    // on/after 2026-06-07 (older builds ignore it; default false is back-compatible).

    // Nullable so valueType is ALWAYS emitted for CreateMemoryRecord even when 0
    // (Byte), yet omitted entirely from unrelated messages (NavigateHexView, etc.).
    [JsonPropertyName("valueType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ValueType { get; set; }

    [JsonPropertyName("isSigned")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsSigned { get; set; }

    [JsonPropertyName("showAsHex")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ShowAsHex { get; set; }

    // --- GetAttachedProcess reply fields [AOBM-ATTACH-CHECK] ---
    // Read-only for us: the plugin fills them, we never send them. Nullable so a reply that
    // lacks the field reads as "cannot tell" rather than as pid 0, which means "CE has no
    // process open" -- a real answer the check reports.

    [JsonPropertyName("processId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProcessId { get; set; }

    [JsonPropertyName("processName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcessName { get; set; }

    // --- activation result [AOBM-ACTIVATE-RESULT] ---
    // Read-only for us. A plugin from AOBMaker v20260930 (build 155) on sends them on a success reply to an
    // autoActivate request; older ones never do. Nullable so an absent field reads as "not known", never as false.

    [JsonPropertyName("activated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Activated { get; set; }

    [JsonPropertyName("symbolRegistered")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SymbolRegistered { get; set; }
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
