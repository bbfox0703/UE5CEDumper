using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.Views;

namespace UE5DumpUI.Mcp;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D2-3: raw memory writes and UFunction invokes, to the maintainer's decisions of
/// 2026-11-11. Both ask every time, as every write_* tool does, and both need the DLL from
/// <see cref="Constants.McpMinDllBuildForRawWrites"/>, which enforces what matters in the DLL itself:
///
/// <para><b>write_memory</b> writes data only. The DLL refuses code, read-only, guard and uncommitted pages for every
/// client and never changes page protection; this tool also sends the bytes the AI read (<c>expect</c>), so memory the
/// user, the game or another client changed since is not overwritten.</para>
///
/// <para><b>write_invoke</b> calls a UFunction on the game thread, its parameters by name. The server reads the
/// signature and builds the buffer with the Invoke dialog's <see cref="ParamBufferBuilder"/>, and decodes the return
/// value and out parameters as the dialog does. The DLL refuses it while the UI or CE has an invoke outstanding (each
/// queued request records its client), refuses the maintainer's deny list for a background client (network RPCs,
/// console commands, quit / travel / restart, destruction, save slots, the debug camera), never runs it off the game
/// thread, and drops it if this session ends while it is still queued. A call queued past its timeout (-5) is never
/// sent again: the server refuses a new invoke while one of its own is outstanding. It never sends direct_call or
/// set_invoke_timeout.</para>
/// </summary>
internal static class McpRawTools
{
    private const int MaxWriteBytes = 4096;
    private const int LookupSeconds = 30;
    private const int MaxSuperHops = 64;

    // Grimoire's codes the replies carry.
    private const long HeldByOther = -20;
    private const long WriteMemChanged = -23;
    private const long InvokeQueued = -5;
    private const long InvokeFaulted = -4;

    public static IEnumerable<McpTool> Create(IMcpGame game)
    {
        yield return WriteMemory(game);
        yield return Invoke(game);
    }

    // ── write_memory ────────────────────────────────────────────────────────────────────────────────────

    private static McpTool WriteMemory(IMcpGame game) => new(
        "write_memory", "Write memory",
        "Writes bytes at an address. Data only: the DLL refuses code, read-only, guard and uncommitted pages, and never "
        + "changes page protection. Pass the bytes there now as expect (read them with read_memory first): if the user, "
        + "the game or another client changed them since, nothing is written and the reply gives what is there. Up to "
        + "4096 bytes. Returns the bytes before and after. A field a gameplay hold keeps (god mode, a forced field) is "
        + "put back by it; change that through its write_* tool instead.",
        McpSchema.Object(
            ("addr", McpSchema.Addr("The first byte's address."), true),
            ("bytes", McpSchema.HexBytes("The bytes to write, as hex: \"0A000000\" is the int32 10."), true),
            ("expect", McpSchema.HexBytes("The bytes there now, as read_memory gave them: as many as bytes."), true),
            McpWriteTools.PidProp),
        ReadOnly: false,
        async (a, ct) =>
        {
            int? pid = a.OptInt("pid");
            string addr = a.Addr("addr");
            string bytes = a.HexBytes("bytes");
            string expect = a.HexBytes("expect");
            int count = bytes.Length / 2;
            if (count > MaxWriteBytes)
                throw new McpToolError($"Up to {MaxWriteBytes} bytes per write ({count} given).");
            if (expect.Length != bytes.Length)
                throw new McpToolError("'expect' must be as many bytes as 'bytes'.");
            await McpWriteTools.RequireDllBuildAsync(game, pid, Constants.McpMinDllBuildForRawWrites, ct);

            var timeout = TimeSpan.FromSeconds(LookupSeconds);
            var reply = await game.SendAsync(pid, new JsonObject
            {
                ["cmd"] = "write_mem", ["addr"] = addr, ["bytes"] = bytes, ["expect"] = expect,
            }, timeout, ct);
            if (!McpWriteTools.Truthy(reply, "ok"))
            {
                var fail = new JsonObject { ["error"] = McpJson.GetString(reply, "error") ?? "Not written." };
                if (McpJson.TryGetLong(reply["code"], out long code)) fail["code"] = code;
                if (code == WriteMemChanged && reply["current"] is JsonNode current) fail["current"] = current.DeepClone();
                return McpWriteTools.Combine(new McpToolResult(fail, IsError: true), game.TakeNotice());
            }
            var payload = new JsonObject
            {
                ["addr"] = addr,
                ["written"] = bytes,
                ["before"] = McpJson.GetString(reply, "before") ?? "",
            };
            var back = await game.SendAsync(pid, new JsonObject { ["cmd"] = "read_mem", ["addr"] = addr, ["size"] = count },
                timeout, ct);
            string? after = McpWriteTools.Truthy(back, "ok") ? McpJson.GetString(back, "bytes") : null;
            payload["after"] = after ?? "";
            string? note = after is null ? "The write was made, but reading it back failed."
                : !string.Equals(after, bytes, StringComparison.OrdinalIgnoreCase)
                    ? "Read back differently: the game (or a hold) changed it again at once."
                    : null;
            return McpWriteTools.Combine(new McpToolResult(payload, Note: note), game.TakeNotice());
        }, Idempotent: true, Destructive: true, RequiresInteraction: true);

    // ── write_invoke ────────────────────────────────────────────────────────────────────────────────────

    private static McpTool Invoke(IMcpGame game) => new(
        "write_invoke", "Invoke a UFunction",
        "Calls a UFunction on a live object, on the game thread, with its parameters by name: the server reads the "
        + "signature (read_functions shows it) and builds the call. instance_addr is a live object "
        + "(read_find_live_instance). Returns the return value and out parameters. A call the game thread did not run in "
        + "time is queued and runs later, unless this session ends first: never send it again; read_holds lists it until "
        + "it runs. Refused while the UE5DumpUI window or a Cheat Engine record has an invoke outstanding, and for "
        + "network RPCs, console commands, quitting or travelling, destroying actors, writing or deleting save slots, "
        + "and the debug camera (use write_debug_camera).",
        McpSchema.Object(
            ("instance_addr", McpSchema.Addr("The object to call it on."), true),
            ("function", McpSchema.Str("The UFunction's name, e.g. \"SetActorHiddenInGame\"."), true),
            ("params", McpSchema.FreeObject("Parameters by name. A number, true / false or a string; an object as its "
                                            + "hex address; a struct as an object of its fields. One left out is zero."),
                false),
            McpWriteTools.PidProp),
        ReadOnly: false, (a, ct) => RunInvokeAsync(game, a, ct), Idempotent: false, Destructive: true,
        RequiresInteraction: true);

    private static async Task<McpToolResult> RunInvokeAsync(IMcpGame game, McpArgs a, CancellationToken ct)
    {
        int? pid = a.OptInt("pid");
        string inst = a.Addr("instance_addr");
        string funcName = a.ReqStr("function").Trim();
        JsonObject given = a.Obj("params") ?? new JsonObject();
        var ptrs = await McpWriteTools.RequireDllBuildAsync(game, pid, Constants.McpMinDllBuildForRawWrites, ct);
        int ueVersion = McpJson.TryGetLong(ptrs["ue_version"], out long ue) ? (int)ue : 0;
        var lookup = TimeSpan.FromSeconds(LookupSeconds);

        // Never resend after -5: while this server's own call is still queued, refuse the next one.
        var holds = await game.SendAsync(pid, new JsonObject { ["cmd"] = "get_holds" }, lookup, ct);
        if (OwnOutstanding(holds) is int mine and > 0)
            return McpWriteTools.Combine(McpToolResult.Fail(
                "Not invoked: this server's previous invoke (" + mine.ToString(CultureInfo.InvariantCulture) + ") is "
                + "still queued. It runs when the game thread is free, or is dropped when this session ends; do not "
                + "send it again."), game.TakeNotice());

        var obj = await game.SendAsync(pid, new JsonObject { ["cmd"] = "get_object", ["addr"] = inst }, lookup, ct,
            requireScan: true);
        string? classAddr = McpWriteTools.Truthy(obj, "ok") ? McpJson.GetString(obj, "class_addr") : null;
        if (string.IsNullOrEmpty(classAddr) || !McpArgs.TryParseHex(classAddr, out ulong cls) || cls == 0)
            throw new McpToolError($"{inst} is not a live object the DLL knows (read_find_live_instance gives one).");

        var fn = await FindFunctionAsync(game, pid, classAddr, funcName, lookup, ct)
                 ?? throw new McpToolError($"{McpJson.GetString(obj, "class") ?? "Its class"} has no UFunction "
                                           + $"'{funcName}' (read_functions lists them).");

        var call = await BuildCallAsync(game, pid, fn, given, lookup, ct);
        var request = new JsonObject
        {
            ["cmd"] = "invoke_function",
            ["func_name"] = fn.Name,
            ["instance_addr"] = inst,
            ["expect_class_addr"] = classAddr,
            ["if_free"] = true,
        };
        if (call.Size > 0)
        {
            request["parms_size"] = call.Size;
            request["params_hex"] = Convert.ToHexString(call.Buffer);
        }
        if (call.Strings.Count > 0)
        {
            var arr = new JsonArray();
            foreach (var sp in call.Strings)
                McpJson.Append(arr, new JsonObject { ["off"] = sp.Offset, ["wide"] = sp.Wide, ["text"] = sp.Text });
            request["str_params"] = arr;
        }

        var reply = await game.SendAsync(pid, request, McpWriteTools.InvokeTimeout(ptrs), ct, requireScan: true);
        return McpWriteTools.Combine(Answer(reply, fn, ueVersion), game.TakeNotice());
    }

    /// <summary>This server's own outstanding invokes, from get_holds; null when the DLL reports none.</summary>
    internal static int? OwnOutstanding(JsonObject holds)
    {
        if (holds["invokes"] is not JsonArray arr) return null;
        foreach (var item in arr)
        {
            if (item is not JsonObject o || o["holder"] is not JsonObject h) continue;
            if (h["you"] is JsonValue y && y.TryGetValue<bool>(out bool you) && you
                && McpJson.TryGetLong(o["outstanding"], out long n))
                return (int)n;
        }
        return null;
    }

    /// <summary>The function the DLL will call: walk_functions lists a class's own, so the super chain is followed as
    /// the DLL's lookup does, an exact name first and then any case.</summary>
    private static async Task<FunctionInfoModel?> FindFunctionAsync(IMcpGame game, int? pid, string classAddr,
        string name, TimeSpan timeout, CancellationToken ct)
    {
        string? addr = classAddr;
        for (int hop = 0; hop < MaxSuperHops && !string.IsNullOrEmpty(addr); hop++)
        {
            var fr = await game.SendAsync(pid, new JsonObject { ["cmd"] = "walk_functions", ["addr"] = addr },
                timeout, ct, requireScan: true);
            if (McpWriteTools.Truthy(fr, "ok"))
            {
                var funcs = DumpService.ParseFunctions(fr);
                var hit = funcs.FirstOrDefault(f => f.Name == name)
                          ?? funcs.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
            }
            var cr = await game.SendAsync(pid, new JsonObject { ["cmd"] = "walk_class", ["addr"] = addr },
                timeout, ct, requireScan: true);
            string? super = (cr["class"] as JsonObject) is { } c ? McpJson.GetString(c, "super_addr") : null;
            addr = super is not null && McpArgs.TryParseHex(super, out ulong s) && s != 0 ? super : null;
        }
        return null;
    }

    internal sealed record InvokeCall(byte[] Buffer, int Size, List<InvokeStringParam> Strings);

    /// <summary>The call's buffer, by the Invoke dialog's rules (ParamBufferBuilder): each value range-checked before it
    /// is written, a string sent as the DLL's descriptor, a struct by its fields, FText refused, a container or delegate
    /// only empty, an object pointer only a live object's address. Every rule refuses rather than send a silently wrong
    /// argument.</summary>
    internal static async Task<InvokeCall> BuildCallAsync(IMcpGame game, int? pid, FunctionInfoModel fn, JsonObject given,
        TimeSpan timeout, CancellationToken ct)
    {
        var inputs = fn.InputParams.ToList();
        foreach (var (key, _) in given)
            if (!inputs.Any(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)))
                throw new McpToolError($"{fn.Name} has no parameter '{key}'. Its parameters: "
                                       + (inputs.Count == 0 ? "none" : string.Join(", ", inputs.Select(p => p.Name))) + ".");

        int size = BakedScriptGenerator.BakedParmsSize(fn);
        var buf = new byte[Math.Max(size, 0)];
        var strings = new List<InvokeStringParam>();
        foreach (var p in inputs)
        {
            JsonNode? node = Given(given, p.Name);
            if (p.Offset < 0 || p.Offset >= buf.Length) continue;
            string type = p.TypeName;
            if (ParamBufferBuilder.IsRefusedParam(type))
            {
                if (!IsEmpty(node)) throw new McpToolError($"'{p.Name}' is an FText, which cannot be passed in.");
                continue;
            }
            if (ParamBufferBuilder.IsEmptyOnlyParam(type))
            {
                if (!IsEmpty(node)) throw new McpToolError($"'{p.Name}' ({ParamBufferBuilder.ShortTypeName(type)}) can "
                                                           + "only be passed empty.");
                continue;
            }
            if (ParamBufferBuilder.IsStringType(type))
            {
                if (node is null) continue;
                if (p.IsOut) throw new McpToolError($"'{p.Name}' is an out string: the function fills it; leave it out.");
                strings.Add(new InvokeStringParam(p.Offset, ParamBufferBuilder.IsWideString(type), Text(node)));
                continue;
            }
            if (type == "StructProperty" && p.StructFields.Count > 0)
            {
                if (node is null) continue;
                if (node is not JsonObject fields)
                    throw new McpToolError($"'{p.Name}' is a {p.StructName}: pass an object of its fields.");
                foreach (var (key, _) in fields)
                    if (!p.StructFields.Any(f => string.Equals(f.Name, key, StringComparison.OrdinalIgnoreCase)))
                        throw new McpToolError($"{p.StructName} has no field '{key}'. Its fields: "
                                               + string.Join(", ", p.StructFields.Select(f => f.Name)) + ".");
                var values = p.StructFields.Select(f => Given(fields, f.Name) is { } v ? Text(v)
                    : ParamBufferBuilder.IsStringType(f.TypeName) ? "" : "0").ToArray();
                if (!ParamBufferBuilder.TryValidateStructSubFields(p.StructFields, values, out string bad, out string err))
                    throw new McpToolError($"'{p.Name}.{bad}': {err}");
                ParamBufferBuilder.WriteStructParam(buf, p.Offset, p.StructFields, values);
                continue;
            }
            if (ParamBufferBuilder.IsUnwritableParam(type))
            {
                if (!IsEmpty(node)) throw new McpToolError($"'{p.Name}' ({ParamBufferBuilder.ShortTypeName(type)}) has no "
                                                           + "fields the DLL could read, so it cannot be passed in.");
                continue;
            }
            if (node is null) continue;
            string text = Text(node).Trim();
            if (ParamBufferBuilder.IsPickablePointerType(type) && McpArgs.TryParseHex(text, out ulong ptr) && ptr != 0)
            {
                // The DLL passes a non-object straight to the call: an address must be a live object.
                var po = await game.SendAsync(pid, new JsonObject { ["cmd"] = "get_object", ["addr"] = text }, timeout, ct,
                    requireScan: true);
                if (!McpWriteTools.Truthy(po, "ok") || string.IsNullOrEmpty(McpJson.GetString(po, "class_addr")))
                    throw new McpToolError($"'{p.Name}' must be a live object's address; {text} is not one.");
                text = "0x" + ptr.ToString("X", CultureInfo.InvariantCulture);
            }
            if (!ParamBufferBuilder.TryValidateScalar(type, p.Size, text, out string rangeErr))
                throw new McpToolError($"'{p.Name}': {rangeErr}");
            ParamBufferBuilder.WriteParam(buf, p.Offset, type, p.Size, text);
        }
        return new InvokeCall(buf, size, strings);
    }

    private static JsonNode? Given(JsonObject given, string name)
    {
        if (given[name] is { } exact) return exact;
        foreach (var (key, value) in given)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return value;
        return null;
    }

    private static string Text(JsonNode node) => node is JsonValue v
        ? v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => v.ToJsonString(),
        }
        : node.ToJsonString();

    private static bool IsEmpty(JsonNode? node) => node switch
    {
        null => true,
        JsonArray arr => arr.Count == 0,
        JsonObject obj => obj.Count == 0,
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.Null or JsonValueKind.False => true,
            JsonValueKind.String => v.GetValue<string>().Length == 0,
            JsonValueKind.Number => Text(v) == "0",
            _ => false,
        },
        _ => false,
    };

    /// <summary>An invoke's reply: a refusal (ok:false, nothing ran), the other client's outstanding invoke (code -20),
    /// a call that ran (its return value and out parameters, decoded as the Invoke dialog does), one still queued (-5,
    /// whose buffer is the INPUT, never shown as output), or one that did not run.</summary>
    internal static McpToolResult Answer(JsonObject reply, FunctionInfoModel fn, int ueVersion)
    {
        if (!McpWriteTools.Truthy(reply, "ok"))
        {
            if (McpJson.TryGetLong(reply["code"], out long code) && code == HeldByOther)
                return McpToolResult.Fail("Not invoked: " + McpWriteTools.DescribeHolder(reply["holder"] as JsonObject)
                                          + " has an invoke still queued or running, and this server never queues "
                                          + "beside it. Try again once it has run (read_holds lists it).");
            return McpToolResult.Fail(McpJson.GetString(reply, "error") ?? "Not invoked.");
        }
        long result = McpJson.TryGetLong(reply["result"], out long r) ? r : -999;
        var payload = new JsonObject
        {
            ["function"] = fn.Name,
            ["instance"] = McpJson.GetString(reply, "instance_name") ?? "",
            ["class"] = McpJson.GetString(reply, "instance_class_name") ?? "",
            ["result"] = result,
        };
        if (reply["game_thread_stalled"] is JsonNode stalled) payload["game_thread_stalled"] = stalled.DeepClone();
        if (result == InvokeQueued)
        {
            payload["queued"] = true;
            return new McpToolResult(payload, Note: "Queued: the game thread did not run the call in time. It runs when "
                + "the game thread is free, unless this session ends first. Do not send it again; read_holds lists it "
                + "until it runs.");
        }
        if (result != 0)
        {
            string why = result == InvokeFaulted
                ? "it was entered and faulted inside (it may have done part of its work)"
                : "it did not run";
            return new McpToolResult(payload, IsError: true,
                Note: "The call failed (code " + result.ToString(CultureInfo.InvariantCulture) + "): " + why + ". "
                      + (McpJson.GetString(reply, "error") ?? ""));
        }

        string hex = McpJson.GetString(reply, "result_hex") ?? "";
        if (hex.Length > 0)
        {
            var bytes = InvokeParamDialog.HexToBytes(hex);
            var strings = DumpService.ParseStringResults(reply["str_results"] as JsonArray);
            var outs = new JsonObject();
            foreach (var p in fn.Parameters.Where(p => p.IsReturn || p.IsOut))
            {
                string value = Decode(bytes, p, strings, ueVersion);
                if (p.IsReturn) payload["return"] = value;
                else outs[p.Name] = value;
            }
            if (outs.Count > 0) payload["out"] = outs;
        }
        string? note = reply["nonstatic_on_template"] is JsonValue t && t.TryGetValue<bool>(out bool warn) && warn
            ? "The object is a template (a class default or an archetype), and the function is not static: it may "
              + "have done nothing there."
            : null;
        return new McpToolResult(payload, Note: note);
    }

    private static string Decode(byte[] bytes, FunctionParamModel p, IReadOnlyDictionary<int, string?> strings,
        int ueVersion)
    {
        var layout = InvokeParamDialog.ResolveTrustedLayout(p.TypeName, p.StructName, p.Size, ueVersion);
        if (layout is not null) return InvokeParamDialog.DecodeStructParamValue(bytes, p, layout);
        if (p.TypeName == "StructProperty" && p.StructFields.Count > 0)
            return InvokeParamDialog.DecodeDynamicStructParamValue(bytes, p);
        return InvokeParamDialog.DecodeParamValue(bytes, p, strings);
    }
}
