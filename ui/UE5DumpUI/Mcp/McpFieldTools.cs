using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.Mcp;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] Phase 2 (docs/mcp-write-approval-eval.md §4.5): <c>write_field</c> writes the one
/// field a field grant names, on a live instance of the granted class or of a subclass, without a prompt per call. The
/// grant is <see cref="McpGrantTools"/>' field grant, which finds the field with <see cref="FindGrantableFieldAsync"/>.
///
/// <para><b>The tool computes the address, and the gate holds the write to it.</b> The instance's class chain is walked
/// up to the granted class, whose layout gives the field's offset, and the instance walk must report the same field
/// there. The write is admitted for exactly that address and size (<see cref="McpGrants.FieldDetail"/>), and the gate
/// passes a write_mem nowhere else, so the capability cannot carry another write.</para>
///
/// <para><b>Values are checked as the Live Walker's editor checks them, and stricter.</b> A number must fit its type
/// (<see cref="FieldValueConverter.TryConvert"/>); an enum takes only one of its own entries, where the editor also
/// takes any number that fits; a packed bool is written through its single-bit mask, and an unresolved bit is refused
/// (<see cref="FieldValueConverter.PlanBoolWrite"/>). The write carries the bytes read just before it (expect), so a
/// value the game or the user changed in between is not overwritten. A class default object is refused: what it holds
/// reaches every instance spawned later, which is more than the grant's words say.</para>
/// </summary>
internal static class McpFieldTools
{
    private const int LookupSeconds = 30;
    private const int MaxSuperHops = 64;
    private const long WriteMemChanged = -23;   // Grimoire's code: the bytes there are no longer the expected ones
    private const string Instead = " write_memory asks the user every call instead.";

    public static IEnumerable<McpTool> Create(IMcpGame game, McpGrants grants)
    {
        yield return WriteField(game, grants);
    }

    /// <summary>Whether a field grant may name this field: a type the Live Walker's editor writes (a number, a bool or an
    /// enum), and one element of it.</summary>
    internal static bool IsGrantable(FieldInfoModel f)
        => FieldValueConverter.IsEditableType(f.TypeName) && f.ArrayDim == 1;

    /// <summary>The field a grant names: its class found by path and checked by its own full name (the DLL's path lookup
    /// folds separators, so its answer is checked, never trusted), then the field by its exact name in that class's
    /// layout, which lists the inherited fields too.</summary>
    internal static async Task<FieldInfoModel> FindGrantableFieldAsync(IMcpGame game, int? pid, string cls,
        string field, CancellationToken ct)
    {
        var lookup = TimeSpan.FromSeconds(LookupSeconds);
        var found = await game.SendAsync(pid, new JsonObject { ["cmd"] = "find_object", ["path"] = cls }, lookup, ct,
            requireScan: true);
        string? addr = McpWriteTools.Truthy(found, "ok") ? McpJson.GetString(found, "addr") : null;
        if (!IsAddress(addr))
            throw new McpToolError("Not granted: no class " + cls + " (read_class shows a class's full_path)." + Instead);
        var obj = await game.SendAsync(pid, new JsonObject { ["cmd"] = "get_object", ["addr"] = addr }, lookup, ct,
            requireScan: true);
        if (!string.Equals(McpJson.GetString(obj, "full_name"), cls, StringComparison.Ordinal))
            throw new McpToolError("Not granted: no class " + cls + " (the object found there is "
                                   + (McpJson.GetString(obj, "full_name") ?? "unnamed") + ")." + Instead);
        var walked = await game.SendAsync(pid, new JsonObject { ["cmd"] = "walk_class", ["addr"] = addr }, lookup, ct,
            requireScan: true);
        if (!McpWriteTools.Truthy(walked, "ok") || walked["class"] is not JsonObject c)
            throw new McpToolError("Not granted: " + cls + "'s layout could not be read." + Instead);
        var f = DumpService.DeserializeClassInfo(c).Fields
                    .FirstOrDefault(x => string.Equals(x.Name, field, StringComparison.Ordinal))
                ?? throw new McpToolError("Not granted: " + cls + " has no field " + field + " (read_class lists them)."
                                          + Instead);
        if (!IsGrantable(f))
            throw new McpToolError("Not granted: " + field + " is " + Kind(f) + "; a field grant covers a number, a bool or "
                                   + "an enum, one element of it." + Instead);
        return f;
    }

    private static string Kind(FieldInfoModel f) => f.ArrayDim > 1
        ? "a static array of " + f.ArrayDim.ToString(CultureInfo.InvariantCulture)
        : "a " + f.TypeName;

    // ── write_field ─────────────────────────────────────────────────────────────────────────────────────

    private static McpTool WriteField(IMcpGame game, McpGrants grants) => new(
        Constants.McpFieldGrantedTool, "Write a granted field",
        "Writes the one field a field grant (" + Constants.McpGrantToolField + ") names, on a live instance of its "
        + "class or a subclass, without asking again. value as read_instance shows it: a number, true or false, or one of "
        + "an enum's entries by name or value. The bytes there are read just before the write and sent with it, so a "
        + "value the game or the user changed in between is not overwritten. Returns the bytes before and after. A class "
        + "default object (Default__) is refused, and so is a field no grant names: write_memory asks every call. A field "
        + "a gameplay hold keeps (god mode, a forced field) is put back by it.",
        McpSchema.Object(
            ("instance_addr", McpSchema.Addr("The live object's address, e.g. from read_find_instances."), true),
            ("field", McpSchema.Str("The field's name, as the grant names it, e.g. \"Health\"."), true),
            ("value", McpSchema.Str("The new value: a number (\"42\", \"0.5\"), true or false, or an enum entry's name "
                                    + "or value."), true),
            McpWriteTools.PidProp),
        ReadOnly: false,
        async (a, ct) =>
        {
            int? pid = a.OptInt("pid");
            string inst = a.Addr("instance_addr");
            string field = a.ReqStr("field").Trim();
            string text = ValueText(a.Node("value"));
            var ptrs = await McpWriteTools.RequireDllBuildAsync(game, pid, Constants.McpMinDllBuildForRawWrites, ct);
            var timeout = TimeSpan.FromSeconds(LookupSeconds);
            if (!McpArgs.TryParseHex(inst, out ulong instAddr) || instAddr == 0
                || !await McpRawTools.IsLiveObjectAsync(game, pid, inst, timeout, ct))
                throw new McpToolError("Not changed: " + inst + " is not a live object's own address "
                                       + "(read_find_instances gives one).");
            var obj = await game.SendAsync(pid, new JsonObject { ["cmd"] = "get_object", ["addr"] = inst }, timeout, ct,
                requireScan: true);
            if ((McpJson.GetString(obj, "name") ?? "").StartsWith("Default__", StringComparison.Ordinal))
                throw new McpToolError("Not changed: " + inst + " is a class default object: what it holds reaches every "
                                       + "instance spawned later, and a field grant covers live instances." + Instead);
            var (target, meta) = await GrantedFieldAsync(game, grants, pid, McpJson.GetString(obj, "class_addr"), field,
                timeout, ct);

            var walked = await game.SendAsync(pid, new JsonObject { ["cmd"] = "walk_instance", ["addr"] = inst }, timeout,
                ct, requireScan: true);
            var live = McpWriteTools.Truthy(walked, "ok")
                ? DumpService.DeserializeInstanceWalk(walked).Fields.FirstOrDefault(f =>
                    string.Equals(f.Name, field, StringComparison.Ordinal) && f.Offset == meta.Offset
                    && f.TypeName == meta.TypeName)
                : null;
            if (live is null)
                throw new McpToolError("Not changed: this object's walk shows no " + field + " at 0x"
                                       + meta.Offset.ToString("X", CultureInfo.InvariantCulture)
                                       + ", where its class puts it.");

            var (at, size, encode) = Plan(meta, live, text, instAddr + (ulong)meta.Offset);
            string atHex = "0x" + at.ToString("X", CultureInfo.InvariantCulture);
            var read = await game.SendAsync(pid, new JsonObject { ["cmd"] = "read_mem", ["addr"] = atHex, ["size"] = size },
                timeout, ct);
            string? expect = McpWriteTools.Truthy(read, "ok") ? McpJson.GetString(read, "bytes") : null;
            if (expect is null || expect.Length != size * 2 || !TryHex(expect, out byte[] current))
                throw new McpToolError("Not changed: " + field + " at " + atHex + " could not be read.");
            string bytes = Convert.ToHexString(encode(current));

            var request = new JsonObject
            {
                ["cmd"] = "write_mem", ["addr"] = atHex, ["bytes"] = bytes, ["expect"] = expect,
            };
            var grant = McpWriteTools.Admit(grants, McpToolRole.GrantedField, McpGrantKind.Field, request, ptrs, target,
                McpGrants.FieldDetail(at, size));
            var reply = await game.SendAsync(pid, request, timeout, ct, grant: grant);
            if (!McpWriteTools.Truthy(reply, "ok"))
            {
                var fail = new JsonObject { ["error"] = McpJson.GetString(reply, "error") ?? "Not written." };
                if (McpJson.TryGetLong(reply["code"], out long code)) fail["code"] = code;
                if (code == WriteMemChanged && reply["current"] is JsonNode now) fail["current"] = now.DeepClone();
                return McpWriteTools.Combine(new McpToolResult(fail, IsError: true), game.TakeNotice());
            }
            var back = await game.SendAsync(pid, new JsonObject { ["cmd"] = "read_mem", ["addr"] = atHex, ["size"] = size },
                timeout, ct);
            string? after = McpWriteTools.Truthy(back, "ok") ? McpJson.GetString(back, "bytes") : null;
            var payload = new JsonObject
            {
                ["field"] = target,
                ["instance_addr"] = inst,
                ["addr"] = atHex,
                ["value"] = text,
                ["was"] = live.TypedValue,
                ["before"] = McpJson.GetString(reply, "before") ?? expect,
                ["written"] = bytes,
                ["after"] = after ?? "",
            };
            string? note = after is null ? "The write was made, but reading it back failed."
                : !string.Equals(after, bytes, StringComparison.OrdinalIgnoreCase)
                    ? "Read back differently: the game (or a hold) changed it again at once."
                    : null;
            return McpWriteTools.Combine(new McpToolResult(payload, Note: note), game.TakeNotice());
        }, Idempotent: true, Destructive: true, Role: McpToolRole.GrantedField, GrantKind: McpGrantKind.Field);

    /// <summary>The granted target this instance's field falls under: the instance's class, then each super in turn,
    /// the first whose full path and this field a field grant names, with the field's layout from that class. Whether the
    /// grant is this game run's is the admission's call.</summary>
    private static async Task<(string Target, FieldInfoModel Field)> GrantedFieldAsync(IMcpGame game, McpGrants grants,
        int? pid, string? classAddr, string field, TimeSpan timeout, CancellationToken ct)
    {
        var targets = grants.List().Where(g => g.Kind == McpGrantKind.Field).Select(g => g.Target)
            .ToHashSet(StringComparer.Ordinal);
        string? addr = classAddr;
        for (int hop = 0; targets.Count > 0 && hop < MaxSuperHops && IsAddress(addr); hop++)
        {
            var walked = await game.SendAsync(pid, new JsonObject { ["cmd"] = "walk_class", ["addr"] = addr }, timeout,
                ct, requireScan: true);
            if (!McpWriteTools.Truthy(walked, "ok") || walked["class"] is not JsonObject c) break;
            var info = DumpService.DeserializeClassInfo(c);
            string target = info.FullPath + ":" + field;
            if (info.FullPath.Length > 0 && targets.Contains(target))
            {
                var f = info.Fields.FirstOrDefault(x => string.Equals(x.Name, field, StringComparison.Ordinal));
                if (f is null || !IsGrantable(f))
                    throw new McpToolError("Not changed: " + target + " is not a field write_field can write in this "
                                           + "game's layout." + Instead);
                return (target, f);
            }
            addr = info.SuperAddress;
        }
        throw new McpToolError("Not changed: no field grant covers " + field + " on this object's class or its supers. "
                               + "Call " + Constants.McpGrantToolField + " with the field and its class once the user "
                               + "has asked for it, or use write_memory, asked every call.");
    }

    /// <summary>Where and what to write: a bool through its plan (a packed one read-modify-written from the byte read
    /// just before), anything else converted as the Live Walker's editor converts it, to exactly the field's size.
    /// </summary>
    private static (ulong Addr, int Size, Func<byte[], byte[]> Encode) Plan(FieldInfoModel meta, LiveFieldValue live,
        string text, ulong fieldAddr)
    {
        if (meta.TypeName == "BoolProperty")
        {
            if (!FieldValueConverter.TryParseBool(text, out bool on))
                throw new McpToolError("Not changed: " + meta.Name + " is a bool: pass true or false.");
            var mode = FieldValueConverter.PlanBoolWrite(live.BoolNative, live.BoolFieldMask);
            if (mode == FieldValueConverter.BoolWriteMode.Refuse)
                throw new McpToolError("Not changed: " + meta.Name + "'s bit could not be resolved on this engine; its "
                                       + "byte may hold up to 8 packed bools, and writing it whole could flip them.");
            ulong at = fieldAddr + (ulong)live.BoolByteOffset;
            int mask = live.BoolFieldMask;
            return mode == FieldValueConverter.BoolWriteMode.NativeByte
                ? (at, 1, _ => [on ? (byte)1 : (byte)0])
                : (at, 1, current => [FieldValueConverter.ApplyBoolMask(current[0], mask, on)]);
        }
        var entries = live.EnumEntries;
        if (entries is { Count: > 0 } && !IsEntry(entries, text))
            throw new McpToolError("Not changed: '" + text + "' is not one of " + meta.Name + "'s entries "
                                   + "(read_instance shows them).");
        var (ok, data, error) = FieldValueConverter.TryConvert(meta.TypeName, text, meta.Size, entries);
        if (!ok) throw new McpToolError("Not changed: " + meta.Name + " cannot hold '" + text + "': " + error + ".");
        if (data.Length != meta.Size)
            throw new McpToolError("Not changed: " + meta.Name + " is "
                                   + meta.Size.ToString(CultureInfo.InvariantCulture) + " bytes, and the value converts to "
                                   + data.Length.ToString(CultureInfo.InvariantCulture) + ".");
        return (fieldAddr, data.Length, _ => data);
    }

    private static bool IsEntry(IReadOnlyList<EnumEntryValue> entries, string text)
    {
        bool numeric = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value);
        return entries.Any(e => string.Equals(e.Name, text, StringComparison.OrdinalIgnoreCase)
                                || (numeric && e.Value == value));
    }

    private static string ValueText(JsonNode? node)
    {
        if (node is JsonValue v)
            switch (v.GetValueKind())
            {
                case JsonValueKind.String: return v.GetValue<string>().Trim();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                case JsonValueKind.Number: return v.ToJsonString();
            }
        throw new McpToolError("'value' is required: a number, true or false, or an enum entry.");
    }

    private static bool IsAddress(string? s) => s is not null && McpArgs.TryParseHex(s, out ulong v) && v != 0;

    private static bool TryHex(string hex, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
