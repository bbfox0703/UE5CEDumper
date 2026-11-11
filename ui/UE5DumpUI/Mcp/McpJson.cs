using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UE5DumpUI.Mcp;

/// <summary>JSON-RPC 2.0 error codes the server answers with (the JSON-RPC specification's own).</summary>
internal static class McpErrorCode
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
}

/// <summary>A request the server answers with a JSON-RPC error rather than a result.</summary>
internal sealed class McpRpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D. Building and writing JSON-RPC messages without reflection: the DOM
/// (<see cref="JsonNode"/>) and a <see cref="Utf8JsonWriter"/>, as the pipe client does, so the trimmed AOT exe needs
/// no serializer metadata.
/// </summary>
internal static class McpJson
{
    // Relaxed escaping keeps the text an AI reads legible: the default encoder writes "TArray<int32>" as
    // "TArray<int32>" and every non-ASCII name as \u escapes. It still escapes control characters and quotes,
    // and nothing here is ever embedded in HTML, which is all the stricter default guards against.
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    /// <summary>One JSON text with no line break in it: a writer that is not indented never emits one, and a newline
    /// inside a string is escaped. stdio framing depends on it (one message per line).</summary>
    public static string Write(JsonNode? node)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            if (node is null) writer.WriteNullValue();
            else node.WriteTo(writer);
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static JsonObject Result(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = result,
    };

    public static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };

    /// <summary>A string member, or null when it is absent or not a string.</summary>
    public static string? GetString(JsonObject? obj, string key)
        => obj?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>A request id must be a string or an integer (JSON-RPC 2.0; MCP forbids null). Its key tells
    /// <c>notifications/cancelled</c> which request it names: "s:" + text or "n:" + digits, so "1" and 1 differ.</summary>
    public static string? IdKey(JsonNode? id)
    {
        if (id is not JsonValue v) return null;
        if (v.GetValueKind() == JsonValueKind.String) return "s:" + v.GetValue<string>();
        return TryGetLong(v, out long n) ? "n:" + n : null;
    }

    /// <summary>An integral JSON number as a long, however the value was made. <c>TryGetValue&lt;long&gt;</c> alone
    /// is not enough: a value parsed from text answers it, but one built from an int answers only
    /// <c>TryGetValue&lt;int&gt;</c>.</summary>
    public static bool TryGetLong(JsonNode? node, out long value)
    {
        value = 0;
        if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.Number) return false;
        if (v.TryGetValue<long>(out value)) return true;
        if (v.TryGetValue<int>(out int i)) { value = i; return true; }
        if (v.TryGetValue<uint>(out uint u)) { value = u; return true; }
        if (v.TryGetValue<double>(out double d) && d == Math.Floor(d) && Math.Abs(d) < 9.0e15)
        {
            value = (long)d;
            return true;
        }
        return false;
    }
}
