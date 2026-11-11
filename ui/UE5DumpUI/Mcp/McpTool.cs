using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UE5DumpUI.Mcp;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D. One MCP tool: what <c>tools/list</c> advertises and what <c>tools/call</c>
/// runs. <paramref name="ReadOnly"/> becomes <c>readOnlyHint</c>, and the name's <c>read_</c> prefix is what lets one
/// Claude Code allow rule (<c>mcp__&lt;server&gt;__read_*</c>) admit every read; McpToolsTests pins that the two agree.
/// <para><paramref name="RequiresInteraction"/> (Stage D2, every <c>write_*</c> tool) becomes
/// <c>_meta["anthropic/requiresUserInteraction"]</c>: Claude Code then asks before every call in every permission
/// mode, an allow rule notwithstanding. An allow rule alone does not keep a tool from running in auto mode -- measured,
/// verification-register item 15 -- so a tool that changes the game carries the marker, not just its name.</para>
/// </summary>
internal sealed record McpTool(
    string Name,
    string Title,
    string Description,
    JsonObject InputSchema,
    bool ReadOnly,
    Func<McpArgs, CancellationToken, Task<McpToolResult>> Run,
    bool Idempotent = true,
    bool Destructive = false,
    bool RequiresInteraction = false);

/// <summary>What a tool returns: a JSON payload for the model, whether it is an error the model can act on, and an
/// optional note shown beside it (a reconnect, a cut reply).</summary>
internal sealed record McpToolResult(JsonNode Payload, bool IsError = false, string? Note = null)
{
    public static McpToolResult Fail(string message) => new(new JsonObject { ["error"] = message }, IsError: true);
}

/// <summary>A tool call the model can correct: a bad argument, a game that is not there. Reported as a result with
/// <c>isError</c>, never as a JSON-RPC error, so the model sees the text (the MCP spec's rule for tool errors).</summary>
internal sealed class McpToolError(string message) : Exception(message);

/// <summary>
/// A tool call's arguments, read leniently in type (a number may arrive as a numeric string) and strictly in value:
/// an address must be hex. The DLL's read handlers turn malformed hex into 0 and answer "not found", which would tell
/// the model the object does not exist; refusing here says what was wrong instead.
/// </summary>
internal sealed class McpArgs(JsonObject? args)
{
    private readonly JsonObject _args = args ?? new JsonObject();

    public bool Has(string name) => _args[name] is not null;

    public string? Str(string name)
    {
        var node = _args[name];
        if (node is null) return null;
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.String) return v.GetValue<string>();
        throw new McpToolError($"'{name}' must be a string.");
    }

    public string ReqStr(string name)
    {
        var s = Str(name);
        if (string.IsNullOrWhiteSpace(s)) throw new McpToolError($"'{name}' is required.");
        return s;
    }

    public long? OptLong(string name)
    {
        var node = _args[name];
        if (node is null) return null;
        if (node is JsonValue v)
        {
            if (McpJson.TryGetLong(v, out long n)) return n;
            if (v.GetValueKind() == JsonValueKind.String
                && long.TryParse(v.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long p))
                return p;
        }
        throw new McpToolError($"'{name}' must be an integer.");
    }

    public int? OptInt(string name)
    {
        long? v = OptLong(name);
        if (v is null) return null;
        if (v < int.MinValue || v > int.MaxValue) throw new McpToolError($"'{name}' is out of range.");
        return (int)v;
    }

    /// <summary>An integer clamped to [min, max]; the schema advertises the same bounds.</summary>
    public int Int(string name, int def, int min, int max) => Math.Clamp(OptInt(name) ?? def, min, max);

    /// <summary>A finite number, or null when absent. A numeric string is read too.</summary>
    public double? OptDouble(string name)
    {
        var node = _args[name];
        if (node is null) return null;
        if (node is JsonValue v)
        {
            if (v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<double>(out double d) && double.IsFinite(d))
                return d;
            if (McpJson.TryGetLong(v, out long l)) return l;
            if (v.GetValueKind() == JsonValueKind.String
                && double.TryParse(v.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out double p)
                && double.IsFinite(p))
                return p;
        }
        throw new McpToolError($"'{name}' must be a number.");
    }

    public double ReqDouble(string name) => OptDouble(name) ?? throw new McpToolError($"'{name}' is required.");

    public bool Bool(string name, bool def)
    {
        var node = _args[name];
        if (node is null) return def;
        if (node is JsonValue v)
        {
            if (v.GetValueKind() == JsonValueKind.True) return true;
            if (v.GetValueKind() == JsonValueKind.False) return false;
            if (v.GetValueKind() == JsonValueKind.String && bool.TryParse(v.GetValue<string>(), out bool b)) return b;
        }
        throw new McpToolError($"'{name}' must be true or false.");
    }

    /// <summary>A string array, or null when absent. A lone string is taken as a one-element array.</summary>
    public List<string>? StrList(string name)
    {
        var node = _args[name];
        if (node is null) return null;
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.String) return [v.GetValue<string>()];
        if (node is not JsonArray arr) throw new McpToolError($"'{name}' must be an array of strings.");
        var list = new List<string>(arr.Count);
        foreach (var item in arr)
        {
            if (item is JsonValue s && s.GetValueKind() == JsonValueKind.String) list.Add(s.GetValue<string>());
            else throw new McpToolError($"'{name}' must be an array of strings.");
        }
        return list;
    }

    /// <summary>A required address, normalised to "0x" + upper-case hex.</summary>
    public string Addr(string name)
        => OptAddr(name) ?? throw new McpToolError($"'{name}' (a hex address) is required.");

    /// <summary>An optional address, normalised to "0x" + upper-case hex; null when absent. Accepts a hex string with
    /// or without 0x, or a JSON integer (a user address fits a double exactly).</summary>
    public string? OptAddr(string name)
    {
        var node = _args[name];
        if (node is null) return null;
        ulong value;
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.String)
        {
            if (!TryParseHex(v.GetValue<string>(), out value))
                throw new McpToolError($"'{name}' must be a hex address such as \"0x7FF6A1B2C3D0\".");
        }
        else if (McpJson.TryGetLong(node, out long l) && l > 0)
        {
            value = (ulong)l;
        }
        else throw new McpToolError($"'{name}' must be a hex address such as \"0x7FF6A1B2C3D0\".");
        if (value == 0) throw new McpToolError($"'{name}' is 0, which is never an object.");
        return "0x" + value.ToString("X", CultureInfo.InvariantCulture);
    }

    internal static bool TryParseHex(string? text, out ulong value)
    {
        value = 0;
        if (text is null) return false;
        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        if (s.Length is 0 or > 16) return false;
        foreach (char c in s)
            if (!char.IsAsciiHexDigit(c)) return false;
        return ulong.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>Builds the JSON Schemas tools advertise. Draft 2020-12 is MCP's default; only plain keywords are used,
/// since Claude Code flattens a root-level anyOf / oneOf.</summary>
internal static class McpSchema
{
    public static JsonObject Object(params (string Name, JsonObject Schema, bool Required)[] props)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, schema, req) in props)
        {
            // A copy: a JsonNode has one parent, and the shared properties (pid, name_filter) join many schemas.
            properties[name] = schema.DeepClone();
            if (req) McpJson.Append(required, name);
        }
        var obj = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
        if (required.Count > 0) obj["required"] = required;
        return obj;
    }

    public static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

    public static JsonObject Enum(string description, params string[] values)
    {
        var arr = new JsonArray();
        foreach (var v in values) McpJson.Append(arr, v);
        return new JsonObject { ["type"] = "string", ["description"] = description, ["enum"] = arr };
    }

    public static JsonObject Addr(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description + " Hex, e.g. \"0x7FF6A1B2C3D0\".",
        ["pattern"] = "^(0[xX])?[0-9A-Fa-f]{1,16}$",
    };

    public static JsonObject Int(string description, int min, int max, int? def = null)
    {
        var o = new JsonObject
        {
            ["type"] = "integer",
            ["description"] = description,
            ["minimum"] = min,
            ["maximum"] = max,
        };
        if (def is int d) o["default"] = d;
        return o;
    }

    public static JsonObject Long(string description) => new() { ["type"] = "integer", ["description"] = description };

    public static JsonObject Number(string description) => new() { ["type"] = "number", ["description"] = description };

    public static JsonObject Bool(string description, bool def) => new()
    {
        ["type"] = "boolean",
        ["description"] = description,
        ["default"] = def,
    };

    public static JsonObject StrArray(string description) => new()
    {
        ["type"] = "array",
        ["description"] = description,
        ["items"] = new JsonObject { ["type"] = "string" },
    };
}
