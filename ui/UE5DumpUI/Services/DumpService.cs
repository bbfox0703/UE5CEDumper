using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Business logic service wrapping pipe client for UE5 operations.
/// </summary>
public sealed class DumpService : IDumpService
{
    private readonly IPipeClient _pipe;
    private readonly ILoggingService _log;

    public DumpService(IPipeClient pipe, ILoggingService log)
    {
        _pipe = pipe;
        _log = log;
    }

    public async Task<EngineState> InitAsync(CancellationToken ct = default)
    {
        var res = await _pipe.SendAsync(new JsonObject { ["cmd"] = "init" }, ct);
        CheckResponse(res);

        // Also get pointers
        var ptrs = await _pipe.SendAsync(new JsonObject { ["cmd"] = "get_pointers" }, ct);
        CheckResponse(ptrs);

        // UE version may come from init response or get_pointers response
        var ueVersion = res["ue_version"]?.GetValue<int>() ?? 0;
        if (ueVersion == 0)
        {
            ueVersion = ptrs["ue_version"]?.GetValue<int>() ?? 0;
        }

        // version_detected: true if PE/memory scan succeeded, false if using default/inferred.
        // Same prefer-init-fall-back-to-ptrs shape as the two flags below — both responses
        // read the same DLL global (Fern.cpp CMD_INIT vs FillPointerSnapshot).
        var versionDetected = res["version_detected"]?.GetValue<bool>()
                              ?? ptrs["version_detected"]?.GetValue<bool>() ?? true;
        // is_user_override / is_low_confidence: prefer init response (more authoritative), fall back to ptrs.
        var isUserOverride  = res["is_user_override"]?.GetValue<bool>()
                              ?? ptrs["is_user_override"]?.GetValue<bool>() ?? false;
        var isLowConfidence = res["is_low_confidence"]?.GetValue<bool>()
                              ?? ptrs["is_low_confidence"]?.GetValue<bool>() ?? false;
        // build_number (build 653+): on the init response AND on every pointer snapshot.
        // Older DLLs omit the field — preserved as 0 so the UI can warn "DLL pre-dates
        // the build-number probe; assume stale".
        var dllBuildNumber  = res["build_number"]?.GetValue<int>() ?? 0;

        return BuildEngineState(ptrs, ueVersion, versionDetected, isUserOverride, isLowConfidence,
                                dllBuildNumber, await TryGetOffsetsAsync(ct));
    }

    public async Task<EngineState> GetPointersAsync(CancellationToken ct = default)
    {
        var res = await _pipe.SendAsync(new JsonObject { ["cmd"] = "get_pointers" }, ct);
        CheckResponse(res);

        return BuildEngineState(res, offsets: await TryGetOffsetsAsync(ct));
    }

    /// <summary>
    /// Fetch the dynamic-offset verdict. Best-effort: a DLL that does not know
    /// <c>get_offsets</c> — or a pipe hiccup on what is only a diagnostic — returns null and
    /// <see cref="BuildEngineState"/> falls back to "validated", i.e. no warning. The
    /// alternative (letting this throw) would turn a missing diagnostic into a failed
    /// connect, which is a much worse trade for the thing it is diagnosing.
    /// (audit #5 U3/X3 — the command shipped in the DLL with zero clients.)
    /// </summary>
    private async Task<JsonObject?> TryGetOffsetsAsync(CancellationToken ct)
    {
        try
        {
            var res = await _pipe.SendAsync(new JsonObject { ["cmd"] = "get_offsets" }, ct);
            return res["error"] is null ? res : null;
        }
        catch (Exception ex)
        {
            _log.Warn($"get_offsets failed ({ex.Message}) — offset-validation banner unavailable");
            return null;
        }
    }

    public async Task<EngineState> SetUeVersionOverrideAsync(int version, bool persist = true, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "set_ue_version_override",
            ["version"] = version,
            ["persist"] = persist,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        _log.Info(Constants.LogCatInit,
            version == 0
                ? $"UE version override cleared (persisted={persist})"
                : $"UE version override set to {version} (persisted={persist})");
        // Re-fetch full state so the caller can update all panels with one ApplyState() call.
        return await GetPointersAsync(ct);
    }

    /// <summary>
    /// Adjust the per-game GameThreadDispatch invoke timeout (UFunction call wait).
    /// Pass 0 to clear the per-game override and revert to Stark::kDefaultInvokeTimeoutMs (5000).
    /// </summary>
    public async Task<EngineState> SetInvokeTimeoutAsync(int timeoutMs, bool persist = true, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "set_invoke_timeout",
            ["timeout_ms"] = timeoutMs,
            ["persist"] = persist,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        _log.Info(Constants.LogCatInit,
            timeoutMs == 0
                ? $"Invoke timeout cleared (persisted={persist})"
                : $"Invoke timeout set to {timeoutMs}ms (persisted={persist})");
        return await GetPointersAsync(ct);
    }

    /// <summary>
    /// Build EngineState from a get_pointers response, with optional overrides from init.
    ///
    /// CONTRACT: every optional parameter must carry an "absent" sentinel (null for bools,
    /// 0 for ints) and fall back to the wire field. A non-nullable default silently wins over
    /// the payload for callers that don't pass it — that is exactly how `versionDetected = true`
    /// made the "⚠ Version not detected" badge vanish on every GetPointersAsync refresh
    /// (invoke-timeout change, UE version override) while InitAsync still showed it.
    /// The DLL emits ue_version / version_detected / is_user_override / is_low_confidence /
    /// build_number on BOTH responses from the same cached globals (Fern.cpp CMD_INIT and
    /// FillPointerSnapshot), so the wire fallback can never disagree with the init value.
    /// </summary>
    private static EngineState BuildEngineState(JsonObject ptrs, int ueVersion = 0, bool? versionDetected = null,
                                                 bool? isUserOverride = null, bool? isLowConfidence = null,
                                                 int dllBuildNumber = 0, JsonObject? offsets = null)
    {
        if (ueVersion == 0)
            ueVersion = ptrs["ue_version"]?.GetValue<int>() ?? 0;

        var scanStats = ptrs["scan_stats"] as JsonObject;

        return new EngineState
        {
            UEVersion = ueVersion,
            // Absent on the wire reads as "detected", i.e. no warning — the right default for
            // a DLL that predates the flag. A DLL that HAS the flag always sends it, so a
            // pointer refresh keeps version_detected=false and the warning stays put.
            VersionDetected = versionDetected ?? ptrs["version_detected"]?.GetValue<bool>() ?? true,
            IsUserOverride = isUserOverride ?? ptrs["is_user_override"]?.GetValue<bool>() ?? false,
            IsLowConfidence = isLowConfidence ?? ptrs["is_low_confidence"]?.GetValue<bool>() ?? false,
            // Only the pointers payload carries this — an older DLL omits it, which reads as
            // false, i.e. "not gated", which is the right default for a DLL that predates the gate.
            IsVersionTooOld = ptrs["is_version_too_old"]?.GetValue<bool>() ?? false,
            // Absent (old DLL, or get_offsets unavailable) reads as validated — no banner,
            // matching the version_detected convention above. Only a DLL that positively
            // says "false" produces the warning. (audit #5 U3/X3)
            OffsetsValidated      = offsets?["validated"]?.GetValue<bool>() ?? true,
            OffsetsProbeRan       = offsets?["probe_ran"]?.GetValue<bool>() ?? false,
            OffsetsFallbackReason = offsets?["fallback_reason"]?.GetValue<string>() ?? "",
            PublisherThumbprint = ptrs["publisher_thumbprint"]?.GetValue<string>() ?? "",
            GObjectsAddr = ptrs["gobjects"]?.GetValue<string>() ?? "",
            GNamesAddr = ptrs["gnames"]?.GetValue<string>() ?? "",
            GWorldAddr = ptrs["gworld"]?.GetValue<string>() ?? "",
            SparseDelegatesAddr = ptrs["sparse_delegates"]?.GetValue<string>() ?? "",
            ObjectCount = ptrs["object_count"]?.GetValue<int>() ?? 0,
            ModuleName = ptrs["module_name"]?.GetValue<string>() ?? "",
            ProcessId  = ptrs["pid"]?.GetValue<int>() ?? 0,
            ModuleBase = ptrs["module_base"]?.GetValue<string>() ?? "",
            // How the DLL was loaded (self-reported); "" on older DLLs.
            LoadMode = ptrs["load_mode"]?.GetValue<string>() ?? "",
            // FUObjectItem layout (older DLLs omit these → classic/false fallback)
            ItemLayoutMode = ptrs["item_layout_mode"]?.GetValue<string>() ?? "classic",
            ItemPacked = ptrs["item_packed"]?.GetValue<bool>() ?? false,
            ItemObjOffset = ptrs["item_obj_offset"]?.GetValue<int>() ?? 0,
            GObjectsMethod = ptrs["gobjects_method"]?.GetValue<string>() ?? "aob",
            GNamesMethod = ptrs["gnames_method"]?.GetValue<string>() ?? "aob",
            GWorldMethod = ptrs["gworld_method"]?.GetValue<string>() ?? "aob",
            SparseDelegatesMethod = ptrs["sparse_delegates_method"]?.GetValue<string>() ?? "not_found",
            // AOB Usage Tracking
            PeHash = ptrs["pe_hash"]?.GetValue<string>() ?? "",
            // Per-launch session token (build 1227+; older DLLs omit it → ""
            // → GameSessionId degrades to "PeHash-" with no per-launch split).
            ProcessCreationTime = ptrs["process_creation_time"]?.GetValue<string>() ?? "",
            GObjectsPatternId = ptrs["gobjects_pattern_id"]?.GetValue<string>() ?? "",
            GNamesPatternId = ptrs["gnames_pattern_id"]?.GetValue<string>() ?? "",
            GWorldPatternId = ptrs["gworld_pattern_id"]?.GetValue<string>() ?? "",
            SparseDelegatesPatternId = ptrs["sparse_delegates_pattern_id"]?.GetValue<string>() ?? "",
            GObjectsScanAddr = ptrs["gobjects_scan_addr"]?.GetValue<string>() ?? "",
            GNamesScanAddr = ptrs["gnames_scan_addr"]?.GetValue<string>() ?? "",
            GWorldScanAddr = ptrs["gworld_scan_addr"]?.GetValue<string>() ?? "",
            SparseDelegatesScanAddr = ptrs["sparse_delegates_scan_addr"]?.GetValue<string>() ?? "",
            GObjectsPatternsTried = scanStats?["gobjects_tried"]?.GetValue<int>() ?? 0,
            GObjectsPatternsHit = scanStats?["gobjects_hit"]?.GetValue<int>() ?? 0,
            GNamesPatternsTried = scanStats?["gnames_tried"]?.GetValue<int>() ?? 0,
            GNamesPatternsHit = scanStats?["gnames_hit"]?.GetValue<int>() ?? 0,
            GWorldPatternsTried = scanStats?["gworld_tried"]?.GetValue<int>() ?? 0,
            GWorldPatternsHit = scanStats?["gworld_hit"]?.GetValue<int>() ?? 0,
            // GWorld AOB metadata (for CE symbol registration)
            GWorldAob = ptrs["gworld_aob"]?.GetValue<string>() ?? "",
            GWorldAobPos = ptrs["gworld_aob_pos"]?.GetValue<int>() ?? 0,
            GWorldAobLen = ptrs["gworld_aob_len"]?.GetValue<int>() ?? 0,
            // GEngine slot + AOB metadata (build 2394+; older DLLs omit these → "" / 0)
            GEngine = ptrs["gengine"]?.GetValue<string>() ?? "",
            GEngineMethod = ptrs["gengine_method"]?.GetValue<string>() ?? "not_found",
            GEnginePatternId = ptrs["gengine_pattern_id"]?.GetValue<string>() ?? "",
            GEngineScanAddr = ptrs["gengine_scan_addr"]?.GetValue<string>() ?? "",
            GEngineAob = ptrs["gengine_aob"]?.GetValue<string>() ?? "",
            GEngineAobPos = ptrs["gengine_aob_pos"]?.GetValue<int>() ?? 0,
            GEngineAobLen = ptrs["gengine_aob_len"]?.GetValue<int>() ?? 0,
            // GameThreadDispatch invoke timeout (effective value)
            InvokeTimeoutMs = ptrs["invoke_timeout_ms"]?.GetValue<int>() ?? 5000,
            // DLL build number — present in both init AND get_pointers responses
            // (build 653+). Prefer the explicit caller-supplied value; fall back
            // to ptrs so refreshes from get_pointers pick it up automatically.
            DllBuildNumber = dllBuildNumber != 0 ? dllBuildNumber
                                                 : (ptrs["build_number"]?.GetValue<int>() ?? 0),
        };
    }

    public async Task<int> GetObjectCountAsync(CancellationToken ct = default)
    {
        var res = await _pipe.SendAsync(new JsonObject { ["cmd"] = "get_object_count" }, ct);
        CheckResponse(res);
        return res["count"]?.GetValue<int>() ?? 0;
    }

    public async Task<ObjectListResult> GetObjectListAsync(int offset, int limit, CancellationToken ct = default, bool includePath = false)
    {
        var req = new JsonObject
        {
            ["cmd"] = "get_object_list",
            ["offset"] = offset,
            ["limit"] = limit
        };
        // Only ask for per-object full paths when the caller needs them
        // (DumpAllService GameOnly). Omitting the flag keeps the DLL on its
        // lean addr/name/class/outer path for the hot Object Tree paginate.
        if (includePath) req["include_path"] = true;
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new ObjectListResult
        {
            Total = res["total"]?.GetValue<int>() ?? 0,
            Scanned = res["scanned"]?.GetValue<int>() ?? limit, // Fallback to limit for backward compat
        };

        if (res["objects"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) continue;
                // Intern ClassName to deduplicate — most objects share a small set
                // of class names (Class, Package, Function, etc.). Saves ~19 MB
                // when loading 486K+ objects with ~500 unique class names.
                // FullPath is deliberately NOT interned (paths are near-unique) and
                // stays "" unless include_path was requested.
                result.Objects.Add(new UObjectNode
                {
                    Address = obj["addr"]?.GetValue<string>() ?? "",
                    Name = obj["name"]?.GetValue<string>() ?? "",
                    ClassName = string.Intern(obj["class"]?.GetValue<string>() ?? ""),
                    OuterAddr = obj["outer"]?.GetValue<string>() ?? "",
                    FullPath = obj["full_path"]?.GetValue<string>() ?? "",
                });
            }
        }

        return result;
    }

    public async Task<ObjectDetail> GetObjectAsync(string addr, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "get_object", ["addr"] = addr };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        return new ObjectDetail
        {
            Address = res["addr"]?.GetValue<string>() ?? "",
            Name = res["name"]?.GetValue<string>() ?? "",
            FullName = res["full_name"]?.GetValue<string>() ?? "",
            ClassName = res["class"]?.GetValue<string>() ?? "",
            ClassAddr = res["class_addr"]?.GetValue<string>() ?? "",
            OuterName = res["outer"]?.GetValue<string>() ?? "",
            OuterAddr = res["outer_addr"]?.GetValue<string>() ?? "",
        };
    }

    public async Task<ObjectDetail> FindObjectAsync(string path, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "find_object", ["path"] = path };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        return new ObjectDetail
        {
            Address = res["addr"]?.GetValue<string>() ?? "",
            Name = res["name"]?.GetValue<string>() ?? "",
        };
    }

    public async Task<ObjectListResult> SearchObjectsAsync(string query, int limit = 200, bool instancesOnly = false, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "search_objects",
            ["query"] = query,
            ["limit"] = limit,
            ["instances_only"] = instancesOnly
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new ObjectListResult
        {
            Total = res["total"]?.GetValue<int>() ?? 0,
            Truncated = res["truncated"]?.GetValue<bool>() ?? false,
        };

        if (res["objects"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) continue;
                result.Objects.Add(new UObjectNode
                {
                    Address = obj["addr"]?.GetValue<string>() ?? "",
                    Name = obj["name"]?.GetValue<string>() ?? "",
                    ClassName = obj["class"]?.GetValue<string>() ?? "",
                    OuterAddr = obj["outer"]?.GetValue<string>() ?? "",
                });
            }
        }

        return result;
    }

    public async Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "walk_class", ["addr"] = addr };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var classObj = res["class"] as JsonObject;
        if (classObj == null) throw new InvalidOperationException("Missing class data in response");

        return DeserializeClassInfo(classObj);
    }

    /// <summary>
    /// Shared deserializer for the wire shape both <c>walk_class</c>
    /// (`res["class"]`) and <c>walk_class_batch</c> (each element of
    /// `res["classes"]`) emit. Centralising the field mapping is the
    /// C# half of the byte-equivalence contract: the batch path
    /// cannot drift from the single path because they parse with the
    /// same code.
    /// </summary>
    private static ClassInfoModel DeserializeClassInfo(JsonObject classObj)
    {
        var model = new ClassInfoModel
        {
            Name = classObj["name"]?.GetValue<string>() ?? "",
            FullPath = classObj["full_path"]?.GetValue<string>() ?? "",
            SuperAddress = classObj["super_addr"]?.GetValue<string>() ?? "",
            SuperName = classObj["super_name"]?.GetValue<string>() ?? "",
            PropertiesSize = classObj["props_size"]?.GetValue<int>() ?? 0,
            SuperPropertiesSize = classObj["super_props_size"]?.GetValue<int>() ?? 0,
        };

        if (classObj["fields"] is JsonArray fields)
        {
            foreach (var f in fields)
            {
                if (f is not JsonObject fo) continue;
                model.Fields.Add(new FieldInfoModel
                {
                    Address = fo["addr"]?.GetValue<string>() ?? "",
                    Name = fo["name"]?.GetValue<string>() ?? "",
                    TypeName = fo["type"]?.GetValue<string>() ?? "",
                    Offset = fo["offset"]?.GetValue<int>() ?? 0,
                    Size = fo["size"]?.GetValue<int>() ?? 0,
                    // Extended type metadata
                    StructType = fo["struct_type"]?.GetValue<string>() ?? "",
                    ObjClassName = fo["obj_class"]?.GetValue<string>() ?? "",
                    InnerType = fo["inner_type"]?.GetValue<string>() ?? "",
                    InnerStructType = fo["inner_struct_type"]?.GetValue<string>() ?? "",
                    InnerObjClass = fo["inner_obj_class"]?.GetValue<string>() ?? "",
                    KeyType = fo["key_type"]?.GetValue<string>() ?? "",
                    KeyStructType = fo["key_struct_type"]?.GetValue<string>() ?? "",
                    ValueType = fo["value_type"]?.GetValue<string>() ?? "",
                    ValueStructType = fo["value_struct_type"]?.GetValue<string>() ?? "",
                    ElemType = fo["elem_type"]?.GetValue<string>() ?? "",
                    ElemStructType = fo["elem_struct_type"]?.GetValue<string>() ?? "",
                    EnumName = fo["enum_name"]?.GetValue<string>() ?? "",
                    BoolFieldMask = fo["bool_mask"]?.GetValue<int>() ?? 0,
                    PropertyFlags = ParseFlagsHex(fo["prop_flags"]?.GetValue<string>()),
                    ArrayDim = fo["array_dim"]?.GetValue<int>() ?? 1,
                });
            }
        }

        return model;
    }

    /// <summary>Parse a "0x…"-prefixed (or bare) hex string into a
    /// <see cref="ulong"/>; empty/null/malformed → 0. Used for the
    /// uint64 <c>prop_flags</c> field, which is wired as a hex string so
    /// the high CPF_* bits survive without JSON-number precision loss.</summary>
    private static ulong ParseFlagsHex(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var span = s.AsSpan();
        if (span.StartsWith("0x") || span.StartsWith("0X")) span = span[2..];
        return ulong.TryParse(span, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    public async Task<List<ClassInfoModel>> WalkClassesBatchAsync(string[] addrs, CancellationToken ct = default)
    {
        var result = new List<ClassInfoModel>(addrs.Length);
        if (addrs.Length == 0) return result;

        var addrsArr = new JsonArray();
        foreach (var a in addrs)
        {
            // Empty/null addresses dropped here — the DLL also drops
            // them, so the result count may be < addrs.Length when the
            // caller passes empties. Callers are expected to filter
            // before invocation.
            if (!string.IsNullOrEmpty(a))
                addrsArr.Add((JsonNode?)JsonValue.Create(a));
        }

        var req = new JsonObject
        {
            ["cmd"]   = "walk_class_batch",
            ["addrs"] = addrsArr,
        };

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        if (res["classes"] is not JsonArray arr)
            throw new InvalidOperationException("Missing classes array in walk_class_batch response");

        foreach (var item in arr)
        {
            if (item is not JsonObject classObj) continue;
            result.Add(DeserializeClassInfo(classObj));
        }

        return result;
    }

    public async Task<byte[]> ReadMemAsync(string addr, int size, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "read_mem", ["addr"] = addr, ["size"] = size };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var hex = res["bytes"]?.GetValue<string>() ?? "";
        return Convert.FromHexString(hex);
    }

    public async Task WriteMemAsync(string addr, byte[] data, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "write_mem",
            ["addr"] = addr,
            ["bytes"] = Convert.ToHexString(data)
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
    }

    public async Task WatchAsync(string addr, int size, int intervalMs, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "watch",
            ["addr"] = addr,
            ["size"] = size,
            ["interval_ms"] = intervalMs
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
    }

    public async Task UnwatchAsync(string addr, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "unwatch", ["addr"] = addr };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
    }

    // --- Live Data Walker ---

    public async Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null, int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "walk_instance", ["addr"] = addr };
        if (!string.IsNullOrEmpty(classAddr)) req["class_addr"] = classAddr;
        if (arrayLimit != 64) req["array_limit"] = arrayLimit;
        if (previewLimit != 2) req["preview_limit"] = previewLimit;
        if (fillGaps) req["fill_gaps"] = true;
        // Emitted only when set: the default request stays byte-identical to the
        // pre-lean wire shape, so nothing else has to know the flag exists.
        if (lean) req["lean"] = true;

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return DeserializeInstanceWalk(res);
    }

    /// <summary>
    /// Shared deserialiser for one instance-walk object — used by BOTH
    /// <see cref="WalkInstanceAsync"/> and <see cref="WalkInstanceBatchAsync"/>.
    ///
    /// <para>Counterpart to the DLL's <c>EncodeInstanceWalkToJson</c>: one emitter
    /// on that side, one parser on this side, so the single and batch paths cannot
    /// disagree about a field. (Layer 2 of the walk_class_batch safety net — the
    /// export it feeds is a large file-shaped artefact where a silently dropped
    /// field is invisible until someone needs it months later.)</para>
    /// </summary>
    internal static InstanceWalkResult DeserializeInstanceWalk(JsonObject o)
    {
        var result = new InstanceWalkResult
        {
            Address = o["addr"]?.GetValue<string>() ?? "",
            Name = o["name"]?.GetValue<string>() ?? "",
            ClassName = o["class"]?.GetValue<string>() ?? "",
            ClassAddr = o["class_addr"]?.GetValue<string>() ?? "",
            OuterAddr = o["outer"]?.GetValue<string>() ?? "",
            OuterName = o["outer_name"]?.GetValue<string>() ?? "",
            OuterClassName = o["outer_class"]?.GetValue<string>() ?? "",
            IsDefinition = o["is_definition"]?.GetValue<bool>() ?? false,
            IsStale = o["stale"]?.GetValue<bool>() ?? false,
            PropertiesSize = o["props_size"]?.GetValue<int>() ?? 0,
        };

        if (o["fields"] is JsonArray fields)
        {
            foreach (var f in fields)
            {
                if (f is not JsonObject fo) continue;
                result.Fields.Add(ParseLiveFieldValue(fo));
            }
        }

        return result;
    }

    /// <summary>Chunk size for <see cref="WalkInstanceBatchAsync"/> — the same ~200
    /// the other batched fan-outs use. Bounds the JSON payload on both sides and
    /// keeps progress/cancellation granularity at roughly one update per chunk.</summary>
    public const int WalkInstanceBatchChunk = 200;

    public async Task<IReadOnlyList<InstanceWalkResult>> WalkInstanceBatchAsync(
        IReadOnlyList<(string Addr, string? ClassAddr)> items,
        int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false,
        bool lean = false, CancellationToken ct = default)
    {
        var all = new List<InstanceWalkResult>(items.Count);
        for (int start = 0; start < items.Count; start += WalkInstanceBatchChunk)
        {
            ct.ThrowIfCancellationRequested();
            int len = Math.Min(WalkInstanceBatchChunk, items.Count - start);

            var arr = new JsonArray();
            for (int i = start; i < start + len; i++)
            {
                var it = new JsonObject { ["addr"] = items[i].Addr };
                if (!string.IsNullOrEmpty(items[i].ClassAddr)) it["class_addr"] = items[i].ClassAddr;
                // (JsonNode) cast is REQUIRED, not cosmetic: without it overload
                // resolution picks JsonArray.Add<T>, which carries
                // RequiresUnreferencedCode + RequiresDynamicCode and fails the
                // Native-AOT publish (IL2026 / IL3050). The cast selects the
                // non-generic IList<JsonNode?>.Add. Same reason as every other
                // JsonArray.Add in this file.
                arr.Add((JsonNode)it);
            }

            var req = new JsonObject { ["cmd"] = "walk_instance_batch", ["items"] = arr };
            if (arrayLimit != 64) req["array_limit"] = arrayLimit;
            if (previewLimit != 2) req["preview_limit"] = previewLimit;
            if (fillGaps) req["fill_gaps"] = true;
            if (lean) req["lean"] = true;   // batch-level; see WalkInstanceAsync

            List<InstanceWalkResult>? chunk = null;
            try
            {
                var res = await _pipe.SendAsync(req, ct);
                CheckResponse(res);
                if (res["instances"] is JsonArray got)
                {
                    // A short/long reply means the batch and the request disagree —
                    // treat it as a batch failure and replay singly rather than
                    // silently mis-pairing results with their requested addresses.
                    if (got.Count == len)
                    {
                        chunk = new List<InstanceWalkResult>(len);
                        foreach (var e in got)
                            chunk.Add(e is JsonObject eo
                                ? DeserializeInstanceWalk(eo)
                                : new InstanceWalkResult());
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;   // a real user cancel, not a batch failure
            }
            catch (Exception ex)
            {
                _log.Warn(Constants.LogCatPipe,
                    $"walk_instance_batch chunk failed ({ex.Message}) — replaying {len} single calls");
            }

            if (chunk == null)
            {
                // Per-chunk fallback (the walk_class_batch precedent): an older DLL
                // that doesn't know the command, or any batch-level failure, degrades
                // to the single path rather than losing the chunk. Each single call
                // can fail independently, exactly as before batching existed.
                chunk = new List<InstanceWalkResult>(len);
                for (int i = start; i < start + len; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    try { chunk.Add(await WalkInstanceAsync(items[i].Addr, items[i].ClassAddr,
                                                            arrayLimit, previewLimit, fillGaps, lean, ct)); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch { chunk.Add(new InstanceWalkResult()); }
                }
            }

            all.AddRange(chunk);
        }
        return all;
    }

    public async Task<WorldWalkResult> WalkWorldAsync(int actorLimit = 200, int arrayLimit = 64, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "walk_world", ["limit"] = actorLimit };
        if (arrayLimit != 64) req["array_limit"] = arrayLimit;
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new WorldWalkResult
        {
            WorldAddr = res["world_addr"]?.GetValue<string>() ?? "",
            WorldName = res["world_name"]?.GetValue<string>() ?? "",
            LevelAddr = res["level_addr"]?.GetValue<string>() ?? "",
            LevelName = res["level_name"]?.GetValue<string>() ?? "",
            LevelOffset = res["level_offset"]?.GetValue<int>() ?? 0,
            ActorCount = res["actor_count"]?.GetValue<int>() ?? 0,
            // -1 on a pre-2818 DLL and whenever the level's Actors array was never
            // read at all, which is why the UI must test for < 0 and not for 0.
            ActorTotal = res["actor_total"]?.GetValue<int>() ?? -1,
            Truncated = res["truncated"]?.GetValue<bool>() ?? false,
            Error = res["error"]?.GetValue<string>() ?? "",
        };

        if (res["actors"] is JsonArray actors)
        {
            foreach (var a in actors)
            {
                if (a is not JsonObject ao) continue;
                var actor = new ActorInfo
                {
                    Address = ao["addr"]?.GetValue<string>() ?? "",
                    Name = ao["name"]?.GetValue<string>() ?? "",
                    ClassName = ao["class"]?.GetValue<string>() ?? "",
                    Index = ao["index"]?.GetValue<int>() ?? -1,
                };

                if (ao["components"] is JsonArray comps)
                {
                    foreach (var c in comps)
                    {
                        if (c is not JsonObject co) continue;
                        actor.Components.Add(new ComponentInfo
                        {
                            Address = co["addr"]?.GetValue<string>() ?? "",
                            Name = co["name"]?.GetValue<string>() ?? "",
                            ClassName = co["class"]?.GetValue<string>() ?? "",
                        });
                    }
                }

                result.Actors.Add(actor);
            }
        }

        return result;
    }

    public async Task<FindInstancesResult> FindInstancesAsync(string className, bool exactMatch = false, int limit = 500, bool newestFirst = false, string nameFilter = "", IReadOnlyList<string>? excludeClasses = null, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "find_instances",
            ["class_name"] = className,
            ["name_filter"] = nameFilter ?? "",
            ["exact_match"] = exactMatch,
            ["limit"] = limit,
            ["newest_first"] = newestFirst
        };
        // Server-side class-noise exclusion (omitted from the wire when empty).
        AttachExcludeClasses(req, excludeClasses);
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new FindInstancesResult
        {
            Scanned = res["scanned"]?.GetValue<int>() ?? 0,
            NonNull = res["non_null"]?.GetValue<int>() ?? 0,
            Named = res["named"]?.GetValue<int>() ?? 0,
            Truncated = res["truncated"]?.GetValue<bool>() ?? false,
            ClassHistogram = ParseClassHistogram(res),
            ClassDistinct = res["class_distinct"]?.GetValue<int>() ?? 0,
        };

        if (res["instances"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) continue;
                result.Instances.Add(new InstanceResult
                {
                    Address = obj["addr"]?.GetValue<string>() ?? "",
                    Index = obj["index"]?.GetValue<int>() ?? -1,
                    Name = obj["name"]?.GetValue<string>() ?? "",
                    ClassName = obj["class"]?.GetValue<string>() ?? "",
                    ClassAddress = obj["class_addr"]?.GetValue<string>() ?? "",
                    OuterAddr = obj["outer"]?.GetValue<string>() ?? "",
                });
            }
        }

        return result;
    }

    public async Task<CePointerInfo> GetCePointerInfoAsync(string addr, int fieldOffset = 0, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "get_ce_pointer_info",
            ["addr"] = addr,
            ["field_offset"] = fieldOffset
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var offsets = new List<int>();
        if (res["ce_offsets"] is JsonArray ceArr)
        {
            foreach (var o in ceArr)
            {
                offsets.Add(o?.GetValue<int>() ?? 0);
            }
        }

        return new CePointerInfo
        {
            Module = res["module"]?.GetValue<string>() ?? "",
            ModuleBase = res["module_base"]?.GetValue<string>() ?? "",
            GObjectsRva = res["gobjects_rva"]?.GetValue<string>() ?? "",
            InternalIndex = res["internal_index"]?.GetValue<int>() ?? 0,
            ChunkIndex = res["chunk_index"]?.GetValue<int>() ?? 0,
            WithinChunk = res["within_chunk"]?.GetValue<int>() ?? 0,
            FieldOffset = fieldOffset,
            CeOffsets = offsets.ToArray(),
            CeBase = res["ce_base"]?.GetValue<string>() ?? "",
            PackedLayout = res["packed_layout"]?.GetValue<bool>() ?? false,
            Warning = res["warning"]?.GetValue<string>() ?? "",
        };
    }

    public async Task<PackedConstsResult> SetPackedConstsAsync(int alignBits = 0, ulong ptrMaskBits = 0, bool force = false, int serialOff = -1, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "set_packed_consts",
            ["align_bits"] = alignBits,
            // Send the mask as a hex string so large values round-trip unambiguously.
            ["ptr_mask_bits"] = "0x" + ptrMaskBits.ToString("X"),
            ["force"] = force,
            ["serial_off"] = serialOff,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var samples = new List<PackedSample>();
        if (res["samples"] is JsonArray arr)
        {
            foreach (var s in arr)
            {
                if (s is null) continue;
                samples.Add(new PackedSample
                {
                    Index = s["index"]?.GetValue<int>() ?? 0,
                    Addr = s["addr"]?.GetValue<string>() ?? "",
                    Name = s["name"]?.GetValue<string>() ?? "",
                });
            }
        }

        return new PackedConstsResult
        {
            ItemLayoutMode = res["item_layout_mode"]?.GetValue<string>() ?? "classic",
            ItemPacked = res["item_packed"]?.GetValue<bool>() ?? false,
            ItemObjOffset = res["item_obj_offset"]?.GetValue<int>() ?? 0,
            ItemSize = res["item_size"]?.GetValue<int>() ?? 0,
            Samples = samples.ToArray(),
        };
    }

    public async Task<AddressLookupResult> FindByAddressAsync(string addr, int containerElemCap = 256, CancellationToken ct = default)
    {
        // Always opt in to the container scan so users can find addresses
        // that fall inside heap-allocated TArray buffers (rather than only
        // within UObject bounds). The DLL has a 5s deadline + per-class
        // cache so this stays interactive even for ~500K-object games.
        var req = new JsonObject
        {
            ["cmd"] = "find_by_address",
            ["addr"] = addr,
            ["scan_containers"] = true,
            // Opt into the recursive deep descent: when the fast shallow scan
            // finds nothing, the DLL descends struct-array / map-value / set
            // elements up to this depth to locate values in separately-allocated
            // nested containers. Only runs on a shallow miss, so the common
            // (fast) case is unaffected. 5 covers ~4 container-nesting levels.
            ["container_depth"] = 5,
            // Per-container element probe cap for the deep descent (configurable
            // via the Options flyout). Higher = deeper coverage, slower.
            ["container_elem_cap"] = containerElemCap,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var found = res["found"]?.GetValue<bool>() ?? false;

        ContainerScanStats? scanStats = null;
        if (res["container_scan"] is JsonObject scanNode)
        {
            scanStats = new ContainerScanStats
            {
                ObjectsScanned = scanNode["objects_scanned"]?.GetValue<int>() ?? 0,
                ObjectsTotal   = scanNode["objects_total"]?.GetValue<int>() ?? 0,
                ClassesPrimed  = scanNode["classes_primed"]?.GetValue<int>() ?? 0,
                DurationMs     = scanNode["duration_ms"]?.GetValue<long>() ?? 0,
                DeadlineHit    = scanNode["deadline_hit"]?.GetValue<bool>() ?? false,
                // Emitted by the DLL since build 1194 and dropped here until Z8/Z12.
                DeepScan       = scanNode["deep_scan"]?.GetValue<bool>() ?? false,
            };
        }

        var containerMatches = new List<ContainerMatch>();
        if (res["container_matches"] is JsonArray cmArr)
        {
            foreach (var node in cmArr)
            {
                if (node is not JsonObject m) continue;
                var nestedChain = new List<ContainerHop>();
                if (m["nested_chain"] is JsonArray chArr)
                {
                    foreach (var hn in chArr)
                    {
                        if (hn is not JsonObject h) continue;
                        nestedChain.Add(new ContainerHop
                        {
                            FieldOffset  = h["field_offset"]?.GetValue<int>() ?? 0,
                            FieldName    = h["field_name"]?.GetValue<string>() ?? "",
                            FieldType    = h["field_type"]?.GetValue<string>() ?? "",
                            InnerType    = h["inner_type"]?.GetValue<string>() ?? "",
                            ElementIndex = h["element_index"]?.GetValue<int>() ?? 0,
                            ElementSize  = h["element_size"]?.GetValue<int>() ?? 0,
                            IntraOffset  = h["intra_offset"]?.GetValue<int>() ?? 0,
                            DataAddress  = h["data_addr"]?.GetValue<string>() ?? "",
                            MapValueSide = h["map_value_side"]?.GetValue<bool>() ?? false,
                            Note         = h["note"]?.GetValue<string>() ?? "",
                        });
                    }
                }
                containerMatches.Add(new ContainerMatch
                {
                    OwnerAddress   = m["owner_addr"]?.GetValue<string>() ?? "",
                    OwnerIndex     = m["owner_index"]?.GetValue<int>() ?? -1,
                    OwnerName      = m["owner_name"]?.GetValue<string>() ?? "",
                    OwnerClassName = m["owner_class"]?.GetValue<string>() ?? "",
                    FieldOffset    = m["field_offset"]?.GetValue<int>() ?? 0,
                    FieldName      = m["field_name"]?.GetValue<string>() ?? "",
                    FieldType      = m["field_type"]?.GetValue<string>() ?? "",
                    InnerType      = m["inner_type"]?.GetValue<string>() ?? "",
                    ElementIndex   = m["element_index"]?.GetValue<int>() ?? 0,
                    ElementSize    = m["element_size"]?.GetValue<int>() ?? 0,
                    IntraOffset    = m["intra_offset"]?.GetValue<int>() ?? 0,
                    DataAddress    = m["data_addr"]?.GetValue<string>() ?? "",
                    Count          = m["count"]?.GetValue<int>() ?? 0,
                    Note           = m["note"]?.GetValue<string>() ?? "",
                    NestedChain    = nestedChain,
                });
            }
        }

        if (!found)
        {
            return new AddressLookupResult
            {
                Found = false,
                QueryAddress = addr,
                ContainerMatches = containerMatches,
                ContainerScan = scanStats,
            };
        }

        return new AddressLookupResult
        {
            Found = true,
            MatchType = res["match_type"]?.GetValue<string>() ?? "",
            MatchKind = res["match_kind"]?.GetValue<string>() ?? "",
            Address = res["addr"]?.GetValue<string>() ?? "",
            Index = res["index"]?.GetValue<int>() ?? -1,
            Name = res["name"]?.GetValue<string>() ?? "",
            ClassName = res["class"]?.GetValue<string>() ?? "",
            OuterAddr = res["outer"]?.GetValue<string>() ?? "",
            OffsetFromBase = res["offset_from_base"]?.GetValue<int>() ?? 0,
            QueryAddress = res["query_addr"]?.GetValue<string>() ?? addr,
            ContainerMatches = containerMatches,
            ContainerScan = scanStats,
        };
    }

    public async Task<FindReferencesResult> FindReferencesToUObjectAsync(
        string addr, int maxResults = 32, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "find_refs_to_uobject",
            ["addr"] = addr,
            ["max_results"] = maxResults,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        ContainerScanStats? scanStats = null;
        if (res["scan"] is JsonObject scanNode)
        {
            scanStats = new ContainerScanStats
            {
                ObjectsScanned = scanNode["objects_scanned"]?.GetValue<int>() ?? 0,
                ObjectsTotal   = scanNode["objects_total"]?.GetValue<int>() ?? 0,
                ClassesPrimed  = scanNode["classes_primed"]?.GetValue<int>() ?? 0,
                DurationMs     = scanNode["duration_ms"]?.GetValue<long>() ?? 0,
                DeadlineHit    = scanNode["deadline_hit"]?.GetValue<bool>() ?? false,
            };
        }

        var refs = new List<ReferenceMatch>();
        if (res["references"] is JsonArray refsArr)
        {
            foreach (var node in refsArr)
            {
                if (node is not JsonObject r) continue;
                refs.Add(new ReferenceMatch
                {
                    OwnerAddress   = r["owner_addr"]?.GetValue<string>() ?? "",
                    OwnerIndex     = r["owner_index"]?.GetValue<int>() ?? -1,
                    OwnerName      = r["owner_name"]?.GetValue<string>() ?? "",
                    OwnerClassName = r["owner_class"]?.GetValue<string>() ?? "",
                    FieldOffset    = r["field_offset"]?.GetValue<int>() ?? 0,
                    FieldName      = r["field_name"]?.GetValue<string>() ?? "",
                    FieldType      = r["field_type"]?.GetValue<string>() ?? "",
                    InnerType      = r["inner_type"]?.GetValue<string>() ?? "",
                    ElementIndex   = r["element_index"]?.GetValue<int>() ?? -1,
                });
            }
        }

        return new FindReferencesResult
        {
            QueryAddress = res["query_addr"]?.GetValue<string>() ?? addr,
            References   = refs,
            Scan         = scanStats,
        };
    }

    public async Task<RelatedObjectsResult> GetRelatedObjectsAsync(
        string addr, int maxResults = 128, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "get_related_objects",
            ["addr"] = addr,
            ["max_results"] = maxResults,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var related = new List<RelatedObject>();
        if (res["related"] is JsonArray arr)
        {
            foreach (var node in arr)
            {
                if (node is not JsonObject r) continue;
                related.Add(new RelatedObject
                {
                    Address       = r["addr"]?.GetValue<string>() ?? "",
                    Index         = r["index"]?.GetValue<int>() ?? -1,
                    Name          = r["name"]?.GetValue<string>() ?? "",
                    ClassName     = r["class"]?.GetValue<string>() ?? "",
                    Relation      = r["relation"]?.GetValue<string>() ?? "",
                    FieldName     = r["field_name"]?.GetValue<string>() ?? "",
                    FieldOffset   = r["field_offset"]?.GetValue<int>() ?? -1,
                    Depth         = r["depth"]?.GetValue<int>() ?? 0,
                    ParentAddress = r["parent_addr"]?.GetValue<string>() ?? "",
                });
            }
        }

        return new RelatedObjectsResult
        {
            QueryAddress = res["query_addr"]?.GetValue<string>() ?? addr,
            Related = related,
        };
    }

    public async Task<CurrentTargetResult> DetectCurrentTargetAsync(
        int maxCandidates = 8, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "get_current_target",
            ["max_candidates"] = maxCandidates,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var candidates = new List<TargetCandidate>();
        if (res["candidates"] is JsonArray arr)
        {
            foreach (var node in arr)
            {
                if (node is not JsonObject c) continue;
                candidates.Add(new TargetCandidate
                {
                    Address       = c["addr"]?.GetValue<string>() ?? "",
                    Index         = c["index"]?.GetValue<int>() ?? -1,
                    Name          = c["name"]?.GetValue<string>() ?? "",
                    ClassName     = c["class"]?.GetValue<string>() ?? "",
                    Score         = c["score"]?.GetValue<int>() ?? 0,
                    SourceAddress = c["source_addr"]?.GetValue<string>() ?? "",
                    SourceClass   = c["source_class"]?.GetValue<string>() ?? "",
                    FieldName     = c["field_name"]?.GetValue<string>() ?? "",
                    FieldOffset   = c["field_offset"]?.GetValue<int>() ?? -1,
                    Reason        = c["reason"]?.GetValue<string>() ?? "",
                });
            }
        }

        return new CurrentTargetResult
        {
            Resolved         = res["resolved"]?.GetValue<bool>() ?? false,
            World            = res["world"]?.GetValue<string>() ?? "",
            PlayerController = res["player_controller"]?.GetValue<string>() ?? "",
            PlayerPawn       = res["player_pawn"]?.GetValue<string>() ?? "",
            Note             = res["note"]?.GetValue<string>() ?? "",
            Candidates       = candidates,
        };
    }

    public async Task<GameEngineResult> ResolveGameEngineAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "resolve_game_engine" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        return new GameEngineResult
        {
            Found          = res["found"]?.GetValue<bool>() ?? false,
            Address        = res["addr"]?.GetValue<string>() ?? "",
            ClassName      = res["class"]?.GetValue<string>() ?? "",
            GameViewportOk = res["game_viewport_ok"]?.GetValue<bool>() ?? false,
            GameInstanceOk = res["game_instance_ok"]?.GetValue<bool>() ?? false,
        };
    }

    public async Task<GWorldPathResult> FindPathFromGWorldAsync(
        string target, string? objectAddr = null, int maxDepth = 5,
        CancellationToken ct = default, string rootKind = "gworld",
        bool deep = false, int containerDepth = 1)
    {
        var req = new JsonObject
        {
            ["cmd"] = "find_path_from_gworld",
            ["target"] = target,
            ["max_depth"] = maxDepth,
        };
        if (!string.IsNullOrEmpty(objectAddr))
            req["object_addr"] = objectAddr;
        // Default root is GWorld; only send root_kind for the engine-rooted variant
        // so the DLL's default path (and the pipe log) stay unchanged.
        if (rootKind != "gworld")
            req["root_kind"] = rootKind;
        // Opt-in deep flags — attach only when set so the common (fast) path and
        // the pipe log stay unchanged.
        if (deep)
            req["deep"] = true;
        if (containerDepth > 1)
            req["container_depth"] = containerDepth;

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var steps = new List<GWorldPathStep>();
        if (res["steps"] is JsonArray stepsArr)
        {
            foreach (var node in stepsArr)
            {
                if (node is not JsonObject s) continue;
                steps.Add(new GWorldPathStep
                {
                    From         = s["from"]?.GetValue<string>() ?? "",
                    To           = s["to"]?.GetValue<string>() ?? "",
                    FieldOffset  = s["field_offset"]?.GetValue<int>() ?? 0,
                    FieldName    = s["field_name"]?.GetValue<string>() ?? "",
                    FieldType    = s["field_type"]?.GetValue<string>() ?? "",
                    InnerType    = s["inner_type"]?.GetValue<string>() ?? "",
                    ElementIndex = s["element_index"]?.GetValue<int>() ?? -1,
                    ElemStride      = s["elem_stride"]?.GetValue<int>() ?? 0,
                    ElemValueOffset = s["elem_value_offset"]?.GetValue<int>() ?? 0,
                    ToName       = s["to_name"]?.GetValue<string>() ?? "",
                    ToClass      = s["to_class"]?.GetValue<string>() ?? "",
                });
            }
        }

        return new GWorldPathResult
        {
            Found             = res["found"]?.GetValue<bool>() ?? false,
            Status            = res["status"]?.GetValue<string>() ?? "",
            RootAddr          = res["root_addr"]?.GetValue<string>() ?? "",
            RootName          = res["root_name"]?.GetValue<string>() ?? "",
            TargetObj         = res["target_obj"]?.GetValue<string>() ?? "",
            TargetName        = res["target_name"]?.GetValue<string>() ?? "",
            TargetClass       = res["target_class"]?.GetValue<string>() ?? "",
            TargetIntraOffset = res["target_intra_offset"]?.GetValue<int>() ?? 0,
            MaxDepth          = res["max_depth"]?.GetValue<int>() ?? maxDepth,
            Depth             = res["depth"]?.GetValue<int>() ?? 0,
            Visited           = res["visited"]?.GetValue<int>() ?? 0,
            DurationMs        = res["duration_ms"]?.GetValue<long>() ?? 0,
            Steps             = steps,
        };
    }

    public async Task<FindPropertyXrefsResult> FindPropertyXrefsAsync(
        string propAddr, bool gameOnly = true, int maxResults = 200,
        CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "find_property_xrefs",
            ["prop_addr"] = propAddr,
            ["game_only"] = gameOnly,
            ["max_results"] = maxResults,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        PropertyXrefScanStats? scanStats = null;
        if (res["scan"] is JsonObject scanNode)
        {
            scanStats = new PropertyXrefScanStats
            {
                FunctionsScanned    = scanNode["functions_scanned"]?.GetValue<int>() ?? 0,
                FunctionsWithScript = scanNode["functions_with_script"]?.GetValue<int>() ?? 0,
                ObjectsTotal        = scanNode["objects_total"]?.GetValue<int>() ?? 0,
                DurationMs          = scanNode["duration_ms"]?.GetValue<long>() ?? 0,
                DeadlineHit         = scanNode["deadline_hit"]?.GetValue<bool>() ?? false,
            };
        }

        var xrefs = new List<PropertyXrefMatch>();
        if (res["xrefs"] is JsonArray xrefsArr)
        {
            foreach (var node in xrefsArr)
            {
                if (node is not JsonObject x) continue;
                xrefs.Add(new PropertyXrefMatch
                {
                    FunctionAddress   = x["func_addr"]?.GetValue<string>() ?? "",
                    FunctionName      = x["func_name"]?.GetValue<string>() ?? "",
                    FunctionFullName  = x["func_full"]?.GetValue<string>() ?? "",
                    OwnerClassName    = x["owner_class"]?.GetValue<string>() ?? "",
                    OwnerClassAddress = x["owner_class_addr"]?.GetValue<string>() ?? "",
                    Occurrences       = x["occurrences"]?.GetValue<int>() ?? 0,
                    WriteCount        = x["write_count"]?.GetValue<int>() ?? 0,
                    Kind              = x["kind"]?.GetValue<string>() ?? "",
                    EventName         = x["event"]?.GetValue<string>() ?? "",
                });
            }
        }

        return new FindPropertyXrefsResult
        {
            QueryAddress = res["query_addr"]?.GetValue<string>() ?? propAddr,
            Xrefs        = xrefs,
            Scan         = scanStats,
        };
    }

    public async Task<FindPropertyXrefsResult> FindFunctionsByClassAsync(
        string classAddr, bool gameOnly = true, int maxResults = 200,
        CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "find_functions_by_class",
            ["class_addr"] = classAddr,
            ["game_only"] = gameOnly,
            ["max_results"] = maxResults,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        PropertyXrefScanStats? scanStats = null;
        if (res["scan"] is JsonObject scanNode)
        {
            scanStats = new PropertyXrefScanStats
            {
                FunctionsScanned    = scanNode["functions_scanned"]?.GetValue<int>() ?? 0,
                FunctionsWithScript = scanNode["functions_with_script"]?.GetValue<int>() ?? 0,
                ObjectsTotal        = scanNode["objects_total"]?.GetValue<int>() ?? 0,
                DurationMs          = scanNode["duration_ms"]?.GetValue<long>() ?? 0,
                DeadlineHit         = scanNode["deadline_hit"]?.GetValue<bool>() ?? false,
            };
        }

        var xrefs = new List<PropertyXrefMatch>();
        if (res["xrefs"] is JsonArray xrefsArr)
        {
            foreach (var node in xrefsArr)
            {
                if (node is not JsonObject x) continue;
                xrefs.Add(new PropertyXrefMatch
                {
                    FunctionAddress   = x["func_addr"]?.GetValue<string>() ?? "",
                    FunctionName      = x["func_name"]?.GetValue<string>() ?? "",
                    FunctionFullName  = x["func_full"]?.GetValue<string>() ?? "",
                    OwnerClassName    = x["owner_class"]?.GetValue<string>() ?? "",
                    OwnerClassAddress = x["owner_class_addr"]?.GetValue<string>() ?? "",
                    Occurrences       = x["occurrences"]?.GetValue<int>() ?? 0,
                    WriteCount        = x["write_count"]?.GetValue<int>() ?? 0,
                    Kind              = x["kind"]?.GetValue<string>() ?? "",
                    EventName         = "",   // n/a for the class-param direction
                });
            }
        }

        return new FindPropertyXrefsResult
        {
            QueryAddress = res["query_addr"]?.GetValue<string>() ?? classAddr,
            Xrefs        = xrefs,
            Scan         = scanStats,
        };
    }

    public async Task<string> GetFunctionCodeAddrAsync(string funcAddr, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "get_function_code_addr",
            ["func_addr"] = funcAddr,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return res["code_addr"]?.GetValue<string>() ?? "";
    }

    public async Task<FunctionPropRefsResult> WalkFunctionPropsAsync(
        string funcAddr, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "walk_function_props",
            ["func_addr"] = funcAddr,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var props = new List<FunctionPropRef>();
        if (res["props"] is JsonArray arr)
        {
            foreach (var node in arr)
            {
                if (node is not JsonObject p) continue;
                props.Add(new FunctionPropRef
                {
                    PropAddress = p["prop_addr"]?.GetValue<string>() ?? "",
                    Name        = p["name"]?.GetValue<string>() ?? "",
                    Type        = p["type"]?.GetValue<string>() ?? "",
                    Occurrences = p["occurrences"]?.GetValue<int>() ?? 0,
                    WriteCount  = p["write_count"]?.GetValue<int>() ?? 0,
                    Scope       = p["scope"]?.GetValue<string>() ?? "",
                    Offset      = p["offset"]?.GetValue<int>() ?? -1,
                    Confidence  = p["confidence"]?.GetValue<string>() ?? "",
                });
            }
        }

        return new FunctionPropRefsResult
        {
            QueryAddress = res["query_addr"]?.GetValue<string>() ?? funcAddr,
            ScriptBytes  = res["script_bytes"]?.GetValue<int>() ?? 0,
            // Path 2: "bytecode" (exact) / "disasm" (native heuristic) / "none".
            // Default to "bytecode" for older DLLs that don't emit the field.
            Method       = res["method"]?.GetValue<string>() ?? "bytecode",
            Unmapped     = res["unmapped"]?.GetValue<int>() ?? 0,
            // AF7. Absent on an older DLL ⇒ false, i.e. "no truncation reported",
            // which is the same thing every caller assumed before the flag existed.
            BudgetHit    = res["budget_hit"]?.GetValue<bool>() ?? false,
            Props        = props,
        };
    }

    public async Task<ArrayElementsResult> ReadArrayElementsAsync(
        string instanceAddr, int fieldOffset,
        string innerAddr, string innerType, int elemSize,
        int offset = 0, int limit = 64, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "read_array_elements",
            ["addr"] = instanceAddr,
            ["field_offset"] = fieldOffset,
            ["inner_addr"] = innerAddr,
            ["inner_type"] = innerType,
            ["elem_size"] = elemSize,
            ["offset"] = offset,
            ["limit"] = limit,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        return new ArrayElementsResult
        {
            TotalCount = res["total"]?.GetValue<int>() ?? 0,
            ReadCount = res["read"]?.GetValue<int>() ?? 0,
            InnerType = res["inner_type"]?.GetValue<string>() ?? innerType,
            ElemSize = res["elem_size"]?.GetValue<int>() ?? elemSize,
            Elements = ParseArrayElements(res["elements"]) ?? new(),
        };
    }

    // --- DataTable Row Browsing ---

    public async Task<DataTableWalkResult> WalkDataTableRowsAsync(string addr, int offset = 0, int limit = 64, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "walk_datatable_rows",
            ["addr"] = addr,
            ["offset"] = offset,
            ["limit"] = limit,
        };

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new DataTableWalkResult
        {
            RowCount = res["row_count"]?.GetValue<int>() ?? 0,
            RowMapOffset = res["row_map_offset"]?.GetValue<int>() ?? 0,
            RowStructAddr = res["row_struct_addr"]?.GetValue<string>() ?? "",
            RowStructName = res["row_struct_name"]?.GetValue<string>() ?? "",
            FNameSize = res["fname_size"]?.GetValue<int>() ?? 0,
            Stride = res["stride"]?.GetValue<int>() ?? 0,
        };

        if (res["rows"] is JsonArray rows)
        {
            foreach (var r in rows)
            {
                if (r is not JsonObject ro) continue;
                var row = new DataTableRowInfo
                {
                    SparseIndex = ro["sparse_index"]?.GetValue<int>() ?? 0,
                    RowName = ro["row_name"]?.GetValue<string>() ?? "",
                    DataAddr = ro["data_addr"]?.GetValue<string>() ?? "",
                };
                if (ro["fields"] is JsonArray rowFields)
                {
                    foreach (var f in rowFields)
                    {
                        if (f is not JsonObject fo) continue;
                        row.Fields.Add(ParseLiveFieldValue(fo));
                    }
                }
                result.Rows.Add(row);
            }
        }

        return result;
    }

    // --- Shared JSON Parsing Helpers ---

    private static LiveFieldValue ParseLiveFieldValue(JsonObject fo)
    {
        return new LiveFieldValue
        {
            Name = fo["name"]?.GetValue<string>() ?? "",
            TypeName = fo["type"]?.GetValue<string>() ?? "",
            Offset = fo["offset"]?.GetValue<int>() ?? 0,
            Size = fo["size"]?.GetValue<int>() ?? 0,
            HexValue = fo["hex"]?.GetValue<string>() ?? "",
            TypedValue = fo["value"]?.GetValue<string>() ?? "",
            IsGuessed = fo["guessed"]?.GetValue<bool>() ?? false,
            PtrAddress = fo["ptr"]?.GetValue<string>() ?? "",
            PtrName = fo["ptr_name"]?.GetValue<string>() ?? "",
            PtrClassName = fo["ptr_class"]?.GetValue<string>() ?? "",
            PtrClassAddr = fo["ptr_class_addr"]?.GetValue<string>() ?? "",
            BoolBitIndex = fo["bool_bit"]?.GetValue<int>() ?? -1,
            BoolFieldMask = fo["bool_mask"]?.GetValue<int>() ?? 0,
            BoolByteOffset = fo["bool_byte_offset"]?.GetValue<int>() ?? 0,
            ArrayCount = fo["count"]?.GetValue<int>() ?? -1,
            ArrayInnerType = fo["array_inner_type"]?.GetValue<string>() ?? "",
            ArrayStructType = fo["array_struct_type"]?.GetValue<string>() ?? "",
            ArrayElemSize = fo["array_elem_size"]?.GetValue<int>() ?? 0,
            ArrayInnerAddr = fo["array_inner_addr"]?.GetValue<string>() ?? "",
            ArrayDataAddr = fo["array_data_addr"]?.GetValue<string>() ?? "",
            ArrayStructClassAddr = fo["array_struct_class_addr"]?.GetValue<string>() ?? "",
            SoftArrayFNameSize = fo["soft_fname_size"]?.GetValue<int>() ?? 0,
            SoftArrayIsTopLevelAssetPath = fo["soft_top_level_asset_path"]?.GetValue<bool>() ?? false,
            ArrayElements = ParseArrayElements(fo["elements"]),
            ArrayEnumAddr = fo["enum_addr"]?.GetValue<string>() ?? "",
            ArrayEnumEntries = ParseEnumEntries(fo["enum_entries"]),
            MapCount = fo["map_count"]?.GetValue<int>() ?? -1,
            MapKeyType = fo["map_key_type"]?.GetValue<string>() ?? "",
            MapValueType = fo["map_value_type"]?.GetValue<string>() ?? "",
            MapKeySize = fo["map_key_size"]?.GetValue<int>() ?? 0,
            MapValueSize = fo["map_value_size"]?.GetValue<int>() ?? 0,
            MapDataAddr = fo["map_data_addr"]?.GetValue<string>() ?? "",
            MapKeyStructAddr = fo["map_key_struct_addr"]?.GetValue<string>() ?? "",
            MapKeyStructType = fo["map_key_struct_type"]?.GetValue<string>() ?? "",
            MapValueStructAddr = fo["map_value_struct_addr"]?.GetValue<string>() ?? "",
            MapValueStructType = fo["map_value_struct_type"]?.GetValue<string>() ?? "",
            MapValueOffset = fo["map_value_offset"]?.GetValue<int>() ?? 0,
            MapStride = fo["map_stride"]?.GetValue<int>() ?? 0,
            MapElements = ParseContainerElements(fo["map_elements"]),
            SetCount = fo["set_count"]?.GetValue<int>() ?? -1,
            SetElemType = fo["set_elem_type"]?.GetValue<string>() ?? "",
            SetElemSize = fo["set_elem_size"]?.GetValue<int>() ?? 0,
            SetStride = fo["set_stride"]?.GetValue<int>() ?? 0,
            SetDataAddr = fo["set_data_addr"]?.GetValue<string>() ?? "",
            SetElemStructAddr = fo["set_elem_struct_addr"]?.GetValue<string>() ?? "",
            SetElemStructType = fo["set_elem_struct_type"]?.GetValue<string>() ?? "",
            SetElements = ParseContainerElements(fo["set_elements"]),
            StructDataAddr = fo["struct_data_addr"]?.GetValue<string>() ?? "",
            StructClassAddr = fo["struct_class_addr"]?.GetValue<string>() ?? "",
            StructTypeName = fo["struct_type"]?.GetValue<string>() ?? "",
            EnumName = fo["enum_name"]?.GetValue<string>() ?? "",
            EnumValue = fo["enum_value"]?.GetValue<long>() ?? 0,
            EnumAddr = fo["enum_addr"]?.GetValue<string>() ?? "",
            EnumEntries = ParseEnumEntries(fo["enum_entries"]),
            StrValue = fo["str_value"]?.GetValue<string>() ?? "",
        };
    }

    private static List<ArrayElementValue>? ParseArrayElements(JsonNode? node)
    {
        if (node is not JsonArray arr || arr.Count == 0) return null;

        var result = new List<ArrayElementValue>(arr.Count);
        foreach (var item in arr)
        {
            if (item is not JsonObject eo) continue;
            result.Add(new ArrayElementValue
            {
                Index = eo["i"]?.GetValue<int>() ?? 0,
                Value = eo["v"]?.GetValue<string>() ?? "",
                Hex = eo["h"]?.GetValue<string>() ?? "",
                EnumName = eo["en"]?.GetValue<string>() ?? "",
                RawIntValue = eo["rv"]?.GetValue<long>() ?? 0,
                // Phase D: pointer element fields
                PtrAddress = eo["pa"]?.GetValue<string>() ?? "",
                PtrName = eo["pn"]?.GetValue<string>() ?? "",
                PtrClassName = eo["pc"]?.GetValue<string>() ?? "",
                // Phase F: struct sub-fields
                StructFields = ParseStructSubFields(eo["sf"]),
            });
        }
        return result;
    }

    private static List<StructSubFieldValue>? ParseStructSubFields(JsonNode? node)
    {
        if (node is not JsonArray arr || arr.Count == 0) return null;

        var result = new List<StructSubFieldValue>(arr.Count);
        foreach (var item in arr)
        {
            if (item is not JsonObject sfObj) continue;
            result.Add(new StructSubFieldValue
            {
                Name = sfObj["n"]?.GetValue<string>() ?? "",
                TypeName = sfObj["t"]?.GetValue<string>() ?? "",
                Offset = sfObj["o"]?.GetValue<int>() ?? 0,
                Size = sfObj["s"]?.GetValue<int>() ?? 0,
                Value = sfObj["v"]?.GetValue<string>() ?? "",
                // Pointer resolution for ObjectProperty sub-fields
                PtrAddress = sfObj["pa"]?.GetValue<string>() ?? "",
                PtrName = sfObj["pn"]?.GetValue<string>() ?? "",
                PtrClassName = sfObj["pc"]?.GetValue<string>() ?? "",
                PtrClassAddr = sfObj["pca"]?.GetValue<string>() ?? "",
            });
        }
        return result;
    }

    private static List<ContainerElementValue>? ParseContainerElements(JsonNode? node)
    {
        if (node is not JsonArray arr || arr.Count == 0) return null;

        var result = new List<ContainerElementValue>(arr.Count);
        foreach (var item in arr)
        {
            if (item is not JsonObject eo) continue;
            result.Add(new ContainerElementValue
            {
                Index = eo["i"]?.GetValue<int>() ?? 0,
                Key = eo["k"]?.GetValue<string>() ?? "",
                Value = eo["v"]?.GetValue<string>() ?? "",
                KeyHex = eo["kh"]?.GetValue<string>() ?? "",
                ValueHex = eo["vh"]?.GetValue<string>() ?? "",
                KeyPtrName = eo["kn"]?.GetValue<string>() ?? "",
                KeyPtrAddress = eo["ka"]?.GetValue<string>() ?? "",
                KeyPtrClassName = eo["kc"]?.GetValue<string>() ?? "",
                ValuePtrName = eo["vn"]?.GetValue<string>() ?? "",
                ValuePtrAddress = eo["va"]?.GetValue<string>() ?? "",
                ValuePtrClassName = eo["vc"]?.GetValue<string>() ?? "",
            });
        }
        return result;
    }

    private static List<EnumEntryValue>? ParseEnumEntries(JsonNode? node)
    {
        if (node is not JsonArray arr || arr.Count == 0) return null;

        var result = new List<EnumEntryValue>(arr.Count);
        foreach (var item in arr)
        {
            if (item is not JsonObject obj) continue;
            result.Add(new EnumEntryValue
            {
                Value = obj["v"]?.GetValue<long>() ?? 0,
                Name = obj["n"]?.GetValue<string>() ?? "",
            });
        }
        return result;
    }

    public async Task<List<EnumDefinition>> ListEnumsAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "list_enums" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new List<EnumDefinition>();
        if (res["enums"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject eo) continue;
                result.Add(new EnumDefinition
                {
                    Address = eo["addr"]?.GetValue<string>() ?? "",
                    Name = eo["name"]?.GetValue<string>() ?? "",
                    FullPath = eo["full_path"]?.GetValue<string>() ?? "",
                    Entries = ParseEnumEntries(eo["entries"]) ?? new(),
                });
            }
        }
        return result;
    }

    public async Task<List<FunctionInfoModel>> WalkFunctionsAsync(string addr, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "walk_functions",
            ["addr"] = addr,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new List<FunctionInfoModel>();
        if (res["functions"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject fo) continue;

                var parms = new List<FunctionParamModel>();
                if (fo["params"] is JsonArray paramsArr)
                {
                    foreach (var pItem in paramsArr)
                    {
                        if (pItem is not JsonObject po) continue;
                        // Phase B: parse optional struct_fields array
                        var structFields = new List<DynamicStructField>();
                        if (po["struct_fields"] is JsonArray sfArr)
                        {
                            foreach (var sfItem in sfArr)
                            {
                                if (sfItem is not JsonObject sfo) continue;
                                structFields.Add(new DynamicStructField(
                                    sfo["name"]?.GetValue<string>() ?? "",
                                    sfo["type"]?.GetValue<string>() ?? "",
                                    sfo["offset"]?.GetValue<int>() ?? 0,
                                    sfo["size"]?.GetValue<int>() ?? 0));
                            }
                        }

                        parms.Add(new FunctionParamModel
                        {
                            Name = po["name"]?.GetValue<string>() ?? "",
                            TypeName = po["type"]?.GetValue<string>() ?? "",
                            Size = po["size"]?.GetValue<int>() ?? 0,
                            Offset = po["offset"]?.GetValue<int>() ?? -1,
                            IsOut = po["out"]?.GetValue<bool>() ?? false,
                            IsReturn = po["ret"]?.GetValue<bool>() ?? false,
                            StructName = po["struct_type"]?.GetValue<string>() ?? "",
                            StructFields = structFields,
                            // Stage 1 (Invoke param picker): expected UClass name
                            // for pointer-flavoured params. Empty when the DLL
                            // pre-dates the field or for non-pointer params.
                            ObjectClassName = po["obj_class"]?.GetValue<string>() ?? "",
                        });
                    }
                }

                result.Add(new FunctionInfoModel
                {
                    Name = fo["name"]?.GetValue<string>() ?? "",
                    FullName = fo["full"]?.GetValue<string>() ?? "",
                    Address = fo["addr"]?.GetValue<string>() ?? "",
                    FunctionFlags = fo["flags"]?.GetValue<uint>() ?? 0,
                    NumParms = fo["num_parms"]?.GetValue<byte>() ?? 0,
                    ParmsSize = fo["parms_size"]?.GetValue<ushort>() ?? 0,
                    ReturnValueOffset = fo["ret_offset"]?.GetValue<ushort>() ?? 0xFFFF,
                    ReturnType = fo["ret"]?.GetValue<string>() ?? "",
                    Params = parms,
                });
            }
        }
        return result;
    }

    public async Task<PropertySearchResult> SearchPropertiesAsync(
        string query, string[]? types = null, bool gameOnly = true,
        bool deep = false, int limit = 200, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "search_properties",
            ["query"] = query,
            ["game_only"] = gameOnly,
            ["deep"] = deep,
            ["limit"] = limit
        };

        if (types is { Length: > 0 })
        {
            var arr = new JsonArray();
            foreach (var t in types) arr.Add((JsonNode?)JsonValue.Create(t));
            req["types"] = arr;
        }

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new PropertySearchResult
        {
            Total = res["total"]?.GetValue<int>() ?? 0,
            ScannedClasses = res["scanned_classes"]?.GetValue<int>() ?? 0,
            ScannedObjects = res["scanned_objects"]?.GetValue<int>() ?? 0,
            // Absent on a pre-2818 DLL -> false, i.e. the old silent behaviour.
            Truncated = res["truncated"]?.GetValue<bool>() ?? false,
            Aborted = res["aborted"]?.GetValue<bool>() ?? false,
        };

        if (res["results"] is JsonArray arr2)
        {
            foreach (var item in arr2)
            {
                if (item is not JsonObject obj) continue;
                result.Results.Add(new PropertySearchMatch
                {
                    ClassName  = obj["class_name"]?.GetValue<string>() ?? "",
                    ClassAddr  = obj["class_addr"]?.GetValue<string>() ?? "",
                    ClassPath  = obj["class_path"]?.GetValue<string>() ?? "",
                    SuperName  = obj["super_name"]?.GetValue<string>() ?? "",
                    PropName   = obj["prop_name"]?.GetValue<string>() ?? "",
                    PropType   = obj["prop_type"]?.GetValue<string>() ?? "",
                    PropOffset = obj["prop_offset"]?.GetValue<int>() ?? 0,
                    PropSize   = obj["prop_size"]?.GetValue<int>() ?? 0,
                    StructType = obj["struct_type"]?.GetValue<string>() ?? "",
                    InnerType  = obj["inner_type"]?.GetValue<string>() ?? "",
                    PropertyFlags = ParseFlagsHex(obj["prop_flags"]?.GetValue<string>()),
                    Preview    = obj["preview"]?.GetValue<string>() ?? "",
                    // Inheritance-aware fields (build 610+) -- back-compat
                    // with older DLLs that didn't emit these: defaults to
                    // "" / 0, falls back to ClassName via the model's
                    // computed properties.
                    DefiningClassName = obj["defining_class_name"]?.GetValue<string>() ?? "",
                    DefiningClassAddr = obj["defining_class_addr"]?.GetValue<string>() ?? "",
                    DefiningClassPath = obj["defining_class_path"]?.GetValue<string>() ?? "",
                    InheritedByCount  = obj["inherited_by_count"]?.GetValue<int>() ?? 0,
                    FieldAddr         = obj["field_addr"]?.GetValue<string>() ?? "",
                    // FBoolProperty FieldMask. Absent for native bools AND for
                    // pre-AA1 DLLs → 0, which both mean "write the whole byte".
                    BoolFieldMask     = obj["bool_mask"]?.GetValue<int>() ?? 0,
                    // Deep-mode synthetic dotted-path leaf (build 1222). Absent
                    // on shallow rows + older DLLs → defaults false.
                    IsNested          = obj["is_nested"]?.GetValue<bool>() ?? false,
                });
            }
        }

        return result;
    }

    public async Task<PropertySearchBatchResult> SearchPropertiesBatchAsync(
        string[] queries, string[]? types = null, bool gameOnly = true,
        int limitPerQuery = 200, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "search_properties_batch",
            ["game_only"] = gameOnly,
            ["limit"] = limitPerQuery
        };

        var qarr = new JsonArray();
        foreach (var q in queries)
        {
            if (!string.IsNullOrEmpty(q))
                qarr.Add((JsonNode?)JsonValue.Create(q));
        }
        req["queries"] = qarr;

        if (types is { Length: > 0 })
        {
            var tarr = new JsonArray();
            foreach (var t in types) tarr.Add((JsonNode?)JsonValue.Create(t));
            req["types"] = tarr;
        }

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return ParseSearchPropertiesBatch(res);
    }

    /// <summary>
    /// Parse a search_properties_batch reply. Split out of the async caller so the wire
    /// contract can be tested without a pipe -- the per-query `truncated` flag went
    /// unparsed here for two builds precisely because nothing could reach this code
    /// (audit #5 X1).
    /// </summary>
    internal static PropertySearchBatchResult ParseSearchPropertiesBatch(JsonObject res)
    {
        var result = new PropertySearchBatchResult
        {
            QueryCount     = res["query_count"]?.GetValue<int>() ?? 0,
            Total          = res["total"]?.GetValue<int>() ?? 0,
            ScannedClasses = res["scanned_classes"]?.GetValue<int>() ?? 0,
            ScannedObjects = res["scanned_objects"]?.GetValue<int>() ?? 0,
            // Absent on a pre-2818 DLL -> false, i.e. the old silent behaviour.
            Aborted = res["aborted"]?.GetValue<bool>() ?? false,
        };

        if (res["per_query"] is JsonArray perQuery)
        {
            foreach (var envNode in perQuery)
            {
                if (envNode is not JsonObject envObj) continue;
                var envelope = new PropertySearchQueryEnvelope
                {
                    Query      = envObj["query"]?.GetValue<string>() ?? "",
                    MatchCount = envObj["match_count"]?.GetValue<int>() ?? 0,
                    Truncated  = envObj["truncated"]?.GetValue<bool>() ?? false,
                };
                if (envObj["results"] is JsonArray matches)
                {
                    foreach (var item in matches)
                    {
                        if (item is not JsonObject obj) continue;
                        envelope.Results.Add(new PropertySearchMatch
                        {
                            ClassName  = obj["class_name"]?.GetValue<string>() ?? "",
                            ClassAddr  = obj["class_addr"]?.GetValue<string>() ?? "",
                            ClassPath  = obj["class_path"]?.GetValue<string>() ?? "",
                            SuperName  = obj["super_name"]?.GetValue<string>() ?? "",
                            PropName   = obj["prop_name"]?.GetValue<string>() ?? "",
                            PropType   = obj["prop_type"]?.GetValue<string>() ?? "",
                            PropOffset = obj["prop_offset"]?.GetValue<int>() ?? 0,
                            PropSize   = obj["prop_size"]?.GetValue<int>() ?? 0,
                            StructType = obj["struct_type"]?.GetValue<string>() ?? "",
                            InnerType  = obj["inner_type"]?.GetValue<string>() ?? "",
                            PropertyFlags = ParseFlagsHex(obj["prop_flags"]?.GetValue<string>()),
                            // Preview intentionally omitted — batch path
                            // skips DLL-side Phase-2 instance scan; field
                            // stays empty by default.
                            DefiningClassName = obj["defining_class_name"]?.GetValue<string>() ?? "",
                            DefiningClassAddr = obj["defining_class_addr"]?.GetValue<string>() ?? "",
                            DefiningClassPath = obj["defining_class_path"]?.GetValue<string>() ?? "",
                            InheritedByCount  = obj["inherited_by_count"]?.GetValue<int>() ?? 0,
                            FieldAddr         = obj["field_addr"]?.GetValue<string>() ?? "",
                            // FBoolProperty FieldMask — see the single-query
                            // parser above. Freeze is reachable from the
                            // Interesting Properties rows this path feeds, so it
                            // needs the mask too. (audit #5 AA1)
                            BoolFieldMask     = obj["bool_mask"]?.GetValue<int>() ?? 0,
                        });
                    }
                }
                result.PerQuery.Add(envelope);
            }
        }

        return result;
    }

    /// <summary>Test hook: parse a raw reply body.</summary>
    internal static PropertySearchBatchResult ParseSearchPropertiesBatchForTest(string json)
        => ParseSearchPropertiesBatch((JsonObject)JsonNode.Parse(json)!);

    // --- Value Search (CE-style First Scan / Next Scan) ---

    private static ValueCandidate ParseValueCandidate(JsonObject obj) => new()
    {
        Addr              = obj["addr"]?.GetValue<string>() ?? "",
        InstanceAddr      = obj["instance_addr"]?.GetValue<string>() ?? "",
        InstanceIndex     = obj["instance_index"]?.GetValue<int>() ?? -1,
        FieldOffset       = obj["field_offset"]?.GetValue<int>() ?? 0,
        InstanceName      = obj["instance_name"]?.GetValue<string>() ?? "",
        ClassName         = obj["class_name"]?.GetValue<string>() ?? "",
        DefiningClassName = obj["defining_class_name"]?.GetValue<string>() ?? "",
        FieldName         = obj["field_name"]?.GetValue<string>() ?? "",
        FieldType         = obj["field_type"]?.GetValue<string>() ?? "",
        BoolFieldMask     = (byte)(obj["bool_field_mask"]?.GetValue<int>() ?? 0xFF),
        Value             = obj["value"]?.GetValue<string>() ?? "",
        // Native-C (P1): present only for raw-hole hits (absent => reflected).
        IsNativeField     = obj["is_native_c"]?.GetValue<bool>() ?? false,
        GuessedType       = obj["guessed_type"]?.GetValue<string>() ?? "",
    };

    private static bool IsStringDataType(ValueScanDataType dt) =>
        dt == ValueScanDataType.FString
        || dt == ValueScanDataType.FName
        || dt == ValueScanDataType.FText;

    // Parse the optional class-noise histogram (begin/refine value+group scan
    // responses). Empty when absent. Shared by all four parse paths.
    private static List<ClassCount> ParseClassHistogram(JsonNode? res)
    {
        var list = new List<ClassCount>();
        if (res?["class_histogram"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is JsonObject o)
                {
                    list.Add(new ClassCount
                    {
                        ClassName = o["class_name"]?.GetValue<string>() ?? "",
                        Count     = o["count"]?.GetValue<int>() ?? 0,
                    });
                }
            }
        }
        return list;
    }

    // Attach the optional exclude_classes string array to a query request,
    // omitting it entirely when empty so the common (no-exclude) page stays
    // byte-identical on the wire. The (JsonNode) cast on string dodges the
    // generic Add<T> AOT/trim trap (IL2026/IL3050).
    private static void AttachExcludeClasses(JsonObject req, IReadOnlyList<string>? excludeClasses)
    {
        if (excludeClasses == null || excludeClasses.Count == 0) return;
        var arr = new JsonArray();
        foreach (var c in excludeClasses)
            if (!string.IsNullOrEmpty(c)) arr.Add((JsonNode)c);
        if (arr.Count > 0) req["exclude_classes"] = arr;
    }

    public async Task<ValueScanBeginResult> BeginValueScanAsync(
        ValueScanDataType dataType,
        ValueScanType scanType,
        string value,
        string? value2 = null,
        bool gameOnly = true,
        int maxResults = Constants.ScanSessionMaxResults,
        Models.FloatRoundMode roundMode = Models.FloatRoundMode.Round,
        bool caseSensitive = false,
        bool parallel = true,
        bool batchRead = true,
        bool deep = false,
        bool nativeC = false,
        bool newestFirst = false,
        int pageSize = Constants.ScanSessionPageSize,
        int deadlineMs = Constants.ScanSessionDeadlineMs,
        bool autoSkipNoise = false,
        CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "begin_value_scan",
            ["data_type"] = dataType.ToString(),
            ["scan_type"] = scanType.ToString(),
            ["value"] = value,
            ["game_only"] = gameOnly,
            ["max_results"] = maxResults,
            // V3-C: only the first page comes back inline; the rest is paged
            // server-side via query_candidates.
            ["page_size"] = pageSize,
        };
        if (!string.IsNullOrEmpty(value2))
            req["value2"] = value2;
        // Per-panel rounding mode (Round/Trunc/Ceil): how a fractional float/double
        // target is reduced to the displayed integer before compare. DLL defaults to
        // Round if absent, so attach only for the non-default modes to keep the
        // common-case wire shape byte-identical.
        if (roundMode != Models.FloatRoundMode.Round)
            req["rounding_mode"] = Models.FloatRoundModeWire.ToWire(roundMode);
        // Case sensitivity is a string-only knob. Default = case-
        // insensitive (matches CE convention) → only attach when the
        // user explicitly opts to case-sensitive AND the type can use it.
        if (caseSensitive && IsStringDataType(dataType))
            req["case_sensitive"] = true;
        // Parallel scan is the DLL default (true) → only attach when the user
        // turns it OFF, so existing call sites stay byte-identical on the wire.
        // parallel=false forces a single-threaded DLL walk (stealthier vs
        // anti-tamper, slower).
        if (!parallel)
            req["parallel"] = false;
        // Batch read is the DLL default (true) → attach only when disabled.
        // batch_read=false forces one SEH read per field instead of one
        // body read per object.
        if (!batchRead)
            req["batch_read"] = false;
        // Deep container pass is opt-in (default off) → attach only when enabled.
        if (deep)
            req["deep"] = true;
        // Native-C raw-hole scan (P1) is opt-in (default off) → attach only when
        // enabled. The (JsonNode) cast dodges the generic JsonObject indexer's
        // Add<T> AOT/trim trap (IL2026/IL3050). native_align stays at the DLL
        // default (4) for now — no UI stride control yet.
        if (nativeC)
            req["native_c"] = (JsonNode)true;
        // Newest-first GObjects ordering is opt-in (default off) → attach only when
        // enabled (auto-set with native_c, but independently toggleable).
        if (newestFirst)
            req["newest_first"] = (JsonNode)true;
        // Scan deadline (Value Search "Timeout" slider). DLL default is 15000ms →
        // attach only when the user picked a different budget, keeping the common
        // case byte-identical on the wire. (JsonNode) cast dodges the AOT Add<T> trap.
        if (deadlineMs != Constants.ScanSessionDeadlineMs)
            req["deadline_ms"] = (JsonNode)deadlineMs;
        // "Auto detect Engine/System noise" pre-filter is opt-in (default off) →
        // attach only when on, keeping the common-case wire shape byte-identical.
        if (autoSkipNoise)
            req["auto_skip_noise"] = (JsonNode)true;

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new ValueScanBeginResult
        {
            SessionId      = res["session_id"]?.GetValue<ulong>() ?? 0,
            DataType       = res["data_type"]?.GetValue<string>() ?? "",
            Total          = res["total"]?.GetValue<int>() ?? 0,
            ScannedClasses = res["scanned_classes"]?.GetValue<int>() ?? 0,
            ScannedObjects = res["scanned_objects"]?.GetValue<int>() ?? 0,
            DurationMs     = res["duration_ms"]?.GetValue<long>() ?? 0,
            DeadlineHit    = res["deadline_hit"]?.GetValue<bool>() ?? false,
            ClassHistogram = ParseClassHistogram(res),
            ClassDistinct  = res["class_distinct"]?.GetValue<int>() ?? 0,
        };

        if (res["candidates"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is JsonObject obj) result.Candidates.Add(ParseValueCandidate(obj));
            }
        }

        return result;
    }

    public async Task<ValueScanRefineResult> RefineValueScanAsync(
        ulong sessionId,
        ValueScanType scanType,
        string? value = null,
        string? value2 = null,
        Models.FloatRoundMode roundMode = Models.FloatRoundMode.Round,
        bool caseSensitive = false,
        int pageSize = Constants.ScanSessionPageSize,
        CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "refine_value_scan",
            ["session_id"] = sessionId,
            ["scan_type"] = scanType.ToString(),
            ["page_size"] = pageSize,
        };
        if (value != null)  req["value"]  = value;
        if (value2 != null) req["value2"] = value2;
        // Refine doesn't know the session's DataType client-side -- the DLL
        // applies the rounding mode only to float/double members and ignores
        // case_sensitive on non-string sessions. Attach the rounding mode only
        // for the non-default modes so common refine paths stay byte-identical.
        if (roundMode != Models.FloatRoundMode.Round)
            req["rounding_mode"] = Models.FloatRoundModeWire.ToWire(roundMode);
        if (caseSensitive)   req["case_sensitive"] = true;

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new ValueScanRefineResult
        {
            SessionId  = res["session_id"]?.GetValue<ulong>() ?? sessionId,
            DataType   = res["data_type"]?.GetValue<string>() ?? "",
            ScanType   = res["scan_type"]?.GetValue<string>() ?? "",
            Total      = res["total"]?.GetValue<int>() ?? 0,
            DurationMs = res["duration_ms"]?.GetValue<long>() ?? 0,
            ClassHistogram = ParseClassHistogram(res),
            ClassDistinct  = res["class_distinct"]?.GetValue<int>() ?? 0,
        };

        if (res["candidates"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is JsonObject obj) result.Candidates.Add(ParseValueCandidate(obj));
            }
        }

        return result;
    }

    public async Task<ValueScanWindowResult> QueryCandidatesAsync(
        ulong sessionId,
        int offset,
        int limit,
        string? filter = null,
        string? sortKey = null,
        bool sortDesc = false,
        IReadOnlyList<string>? excludeClasses = null,
        CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "query_candidates",
            ["session_id"] = sessionId,
            ["offset"] = offset,
            ["limit"] = limit,
        };
        // Omit defaults so the common (no-filter, scan-order) page stays a
        // tight wire shape and is byte-identical across calls for caching.
        if (!string.IsNullOrEmpty(filter))  req["filter"]    = filter;
        if (!string.IsNullOrEmpty(sortKey)) req["sort_key"]  = sortKey;
        if (sortDesc)                       req["sort_desc"] = true;
        AttachExcludeClasses(req, excludeClasses);

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new ValueScanWindowResult
        {
            SessionId     = res["session_id"]?.GetValue<ulong>() ?? sessionId,
            DataType      = res["data_type"]?.GetValue<string>() ?? "",
            Total         = res["total"]?.GetValue<int>() ?? 0,
            FilteredTotal = res["filtered_total"]?.GetValue<int>() ?? 0,
            Offset        = res["offset"]?.GetValue<int>() ?? offset,
        };

        if (res["candidates"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is JsonObject obj) result.Candidates.Add(ParseValueCandidate(obj));
            }
        }

        return result;
    }

    public async Task EndValueScanAsync(ulong sessionId, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "end_value_scan",
            ["session_id"] = sessionId,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
    }

    // --- Multiple values group scan (CE-style "group scan", build 1276) ---

    // Decode ONE leaf of a group slot. Shared by the row's representative (inside
    // `slots`) and by query_group_slot_leaves' full list — the DLL emits both
    // through one encoder (Fern.cpp GroupLeafToJson), so they are decoded through
    // one decoder too. Slot-level fields (slot_index / value / scan_type / value2 /
    // matched_offsets / locked / match_count) are NOT read here; the caller adds
    // whichever of them apply.
    private static GroupSlotMatch ParseGroupSlotLeaf(JsonObject so) => new()
    {
        FieldName     = so["field_name"]?.GetValue<string>() ?? "",
        FieldOffset   = so["field_offset"]?.GetValue<int>() ?? 0,
        FieldType     = so["field_type"]?.GetValue<string>() ?? "",
        BoolFieldMask = (byte)(so["bool_field_mask"]?.GetValue<int>() ?? 0xFF),
        LeafValue     = so["leaf_value"]?.GetValue<string>() ?? "",
        Addr          = so["addr"]?.GetValue<string>() ?? "",
        // The DLL's `addr` IS the leaf's own address (GroupSlotMatch::leafAddr —
        // owner+offset for a direct field, the element's address for a deep one), so
        // the detail row may show it when there is no meaningful offset. The
        // Snapshot / SPC builders cannot say that and leave this false.
        HasLeafAddress = so["addr"] != null,
        OwnerAddr     = so["owner_addr"]?.GetValue<string>() ?? "",
        OwnerClass    = so["owner_class"]?.GetValue<string>() ?? "",
        // Native-C (P2): present only for raw-hole leaves.
        IsNativeField = so["is_native_c"]?.GetValue<bool>() ?? false,
        GuessedType   = so["guessed_type"]?.GetValue<string>() ?? "",
    };

    private static GroupCandidate ParseGroupCandidate(JsonObject obj)
    {
        var gc = new GroupCandidate
        {
            InstanceAddr      = obj["instance_addr"]?.GetValue<string>() ?? "",
            InstanceIndex     = obj["instance_index"]?.GetValue<int>() ?? -1,
            InstanceName      = obj["instance_name"]?.GetValue<string>() ?? "",
            ClassName         = obj["class_name"]?.GetValue<string>() ?? "",
            DefiningClassName = obj["defining_class_name"]?.GetValue<string>() ?? "",
        };
        if (obj["slots"] is JsonArray slots)
        {
            foreach (var s in slots)
            {
                if (s is not JsonObject so) continue;
                var sm = ParseGroupSlotLeaf(so);
                sm.SlotIndex = so["slot_index"]?.GetValue<int>() ?? 0;
                sm.Value     = so["value"]?.GetValue<string>() ?? "";
                sm.ScanType  = so["scan_type"]?.GetValue<string>() ?? "Exact";
                sm.Value2    = so["value2"]?.GetValue<string>() ?? "";
                sm.Locked    = so["locked"]?.GetValue<bool>() ?? false;
                // How many leaves the slot really kept. Absent on pre-2690 DLLs => 0,
                // which is what gates the "All fields" button off (GroupSlotMatch.
                // HasHiddenLeaves) — that DLL has no query_group_slot_leaves to serve
                // it. The "(+N)" annotation still appears there, computed from
                // matched_offsets, because it is display-only and cannot fail.
                sm.MatchCount = so["match_count"]?.GetValue<int>() ?? 0;
                if (so["matched_offsets"] is JsonArray mo)
                    foreach (var off in mo)
                        if (off != null) sm.MatchedOffsets.Add(off.GetValue<int>());
                // Denormalize the owner so per-slot handoffs are self-contained.
                sm.InstanceAddr = gc.InstanceAddr;
                sm.ClassName    = gc.ClassName;
                gc.Slots.Add(sm);
            }
        }
        return gc;
    }

    public async Task<GroupScanBeginResult> BeginGroupScanAsync(
        IReadOnlyList<GroupSlotInput> slots,
        bool gameOnly = true,
        int maxResults = Constants.ScanSessionMaxResults,
        bool deep = false,
        bool crossObject = false,
        bool nativeC = false,
        bool newestFirst = false,
        int pageSize = Constants.ScanSessionPageSize,
        int deadlineMs = Constants.ScanSessionDeadlineMs,
        bool autoSkipNoise = false,
        Models.FloatRoundMode roundMode = Models.FloatRoundMode.Round,
        int perSlotCap = Constants.GroupPerSlotCap,
        CancellationToken ct = default)
    {
        var values = new JsonArray();
        foreach (var sp in slots)
        {
            // Cast to JsonNode so overload resolution picks Add(JsonNode?) — the
            // generic Add<T>(T) is RequiresDynamicCode/UnreferencedCode (AOT-unsafe).
            var slot = new JsonObject
            {
                ["value"]     = sp.Value,
                ["data_type"] = sp.DataType.ToString(),
                ["scan_type"] = sp.ScanType.ToString(),  // per-slot predicate (P2)
            };
            if (sp.ScanType == ValueScanType.Between)
                slot["value2"] = sp.Value2;              // Between upper bound
            // Per-slot rounding mode (all slots share the panel mode). DLL defaults
            // to Round if absent → attach only for non-default modes.
            if (roundMode != Models.FloatRoundMode.Round)
                slot["rounding_mode"] = Models.FloatRoundModeWire.ToWire(roundMode);
            values.Add((JsonNode)slot);
        }
        var req = new JsonObject
        {
            ["cmd"] = "begin_group_scan",
            ["game_only"] = gameOnly,
            ["max_results"] = maxResults,
            ["page_size"] = pageSize,
            ["values"] = values,
        };
        if (deep)
            req["deep"] = true;
        if (crossObject)
            req["cross_object"] = true;
        // Native-C raw-hole pass (P2), opt-in → attach only when on (back-compat).
        if (nativeC)
            req["native_c"] = (JsonNode)true;
        // Newest-first ordering (P2): attach only when on (auto-set with native_c).
        if (newestFirst)
            req["newest_first"] = (JsonNode)true;
        // Scan deadline (Value Search "Timeout" slider). DLL default 15000ms →
        // attach only when changed, keeping the common case wire-identical.
        if (deadlineMs != Constants.ScanSessionDeadlineMs)
            req["deadline_ms"] = (JsonNode)deadlineMs;
        // "Auto detect Engine/System noise" pre-filter (opt-in, default off).
        if (autoSkipNoise)
            req["auto_skip_noise"] = (JsonNode)true;
        // Per-slot leaf budget. Attached only when moved off the default so the common
        // case stays wire-identical; the DLL clamps to 8-4096 regardless of what is sent.
        if (perSlotCap != Constants.GroupPerSlotCap)
            req["per_slot_cap"] = (JsonNode)perSlotCap;
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new GroupScanBeginResult
        {
            SessionId      = res["session_id"]?.GetValue<ulong>() ?? 0,
            Total          = res["total"]?.GetValue<int>() ?? 0,
            SlotCount      = res["slot_count"]?.GetValue<int>() ?? slots.Count,
            ScannedClasses = res["scanned_classes"]?.GetValue<int>() ?? 0,
            ScannedObjects = res["scanned_objects"]?.GetValue<int>() ?? 0,
            DurationMs     = res["duration_ms"]?.GetValue<long>() ?? 0,
            DeadlineHit    = res["deadline_hit"]?.GetValue<bool>() ?? false,
            // Absent on a pre-AE13 DLL => false => "no evidence of truncation", which is
            // the only honest default: the UI must not claim a cap it was not told about.
            PerSlotCapHit  = res["per_slot_cap_hit"]?.GetValue<bool>() ?? false,
            PerSlotCap     = res["per_slot_cap"]?.GetValue<int>() ?? 0,
            ClassHistogram = ParseClassHistogram(res),
            ClassDistinct  = res["class_distinct"]?.GetValue<int>() ?? 0,
        };
        if (res["candidates"] is JsonArray arr)
            foreach (var item in arr)
                if (item is JsonObject o) result.Candidates.Add(ParseGroupCandidate(o));
        return result;
    }

    // Refine takes the NEW (value, scan_type) per slot (same count as the first
    // scan). The per-slot width scope is fixed by the session. P2: a prev-value
    // scan type (Changed/Increased/...) carries no value — it compares against the
    // previous round — so the value is sent but ignored DLL-side for those.
    public async Task<GroupScanRefineResult> RefineGroupScanAsync(
        ulong sessionId,
        IReadOnlyList<GroupSlotInput> slots,
        int pageSize = Constants.ScanSessionPageSize,
        Models.FloatRoundMode roundMode = Models.FloatRoundMode.Round,
        CancellationToken ct = default)
    {
        var arr = new JsonArray();
        // Cast to JsonNode (see BeginGroupScanAsync) so the AOT-safe Add(JsonNode?)
        // overload is chosen instead of the generic Add<T>(T).
        foreach (var sp in slots)
        {
            var slot = new JsonObject
            {
                ["value"]     = sp.Value,
                ["scan_type"] = sp.ScanType.ToString(),
            };
            if (sp.ScanType == ValueScanType.Between)
                slot["value2"] = sp.Value2;
            // Per-slot rounding mode (all slots share the panel mode).
            if (roundMode != Models.FloatRoundMode.Round)
                slot["rounding_mode"] = Models.FloatRoundModeWire.ToWire(roundMode);
            arr.Add((JsonNode)slot);
        }
        var req = new JsonObject
        {
            ["cmd"] = "refine_group_scan",
            ["session_id"] = sessionId,
            ["page_size"] = pageSize,
            ["values"] = arr,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new GroupScanRefineResult
        {
            SessionId  = res["session_id"]?.GetValue<ulong>() ?? sessionId,
            Total      = res["total"]?.GetValue<int>() ?? 0,
            DurationMs = res["duration_ms"]?.GetValue<long>() ?? 0,
            PerSlotCapHit  = res["per_slot_cap_hit"]?.GetValue<bool>() ?? false,   // AE13
            PerSlotCap     = res["per_slot_cap"]?.GetValue<int>() ?? 0,
            ClassHistogram = ParseClassHistogram(res),
            ClassDistinct  = res["class_distinct"]?.GetValue<int>() ?? 0,
        };
        if (res["candidates"] is JsonArray ca)
            foreach (var item in ca)
                if (item is JsonObject o) result.Candidates.Add(ParseGroupCandidate(o));
        return result;
    }

    public async Task<GroupScanWindowResult> QueryGroupCandidatesAsync(
        ulong sessionId,
        int offset,
        int limit,
        string? filter = null,
        string? sortKey = null,
        bool sortDesc = false,
        IReadOnlyList<string>? excludeClasses = null,
        CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "query_group_candidates",
            ["session_id"] = sessionId,
            ["offset"] = offset,
            ["limit"] = limit,
        };
        if (!string.IsNullOrEmpty(filter))  req["filter"]    = filter;
        if (!string.IsNullOrEmpty(sortKey)) req["sort_key"]  = sortKey;
        if (sortDesc)                       req["sort_desc"] = true;
        AttachExcludeClasses(req, excludeClasses);

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new GroupScanWindowResult
        {
            SessionId     = res["session_id"]?.GetValue<ulong>() ?? sessionId,
            Total         = res["total"]?.GetValue<int>() ?? 0,
            FilteredTotal = res["filtered_total"]?.GetValue<int>() ?? 0,
            Offset        = res["offset"]?.GetValue<int>() ?? offset,
            PerSlotCapHit = res["per_slot_cap_hit"]?.GetValue<bool>() ?? false,   // AE13
            PerSlotCap    = res["per_slot_cap"]?.GetValue<int>() ?? 0,
        };
        if (res["candidates"] is JsonArray arr)
            foreach (var item in arr)
                if (item is JsonObject o) result.Candidates.Add(ParseGroupCandidate(o));
        return result;
    }

    /// <summary>Every leaf ONE slot of ONE group candidate kept, BY NAME.
    ///
    /// The results row can only display one assignment out of the many a candidate
    /// may satisfy; this is how the rest become visible and actionable. Fetched on
    /// demand for the expanded row — a page of candidates carries far too many
    /// leaves to inline. Each returned leaf is a <see cref="GroupSlotMatch"/>
    /// carrying the slot's own predicate and owner, so the per-slot handoffs work
    /// on it unchanged; <c>Locked</c> is set because a leaf IS an identified field.
    /// </summary>
    public async Task<IReadOnlyList<GroupSlotMatch>> QueryGroupSlotLeavesAsync(
        ulong sessionId,
        GroupSlotMatch slot,
        string instanceAddr,
        string className,
        int offset = 0,
        int limit = 0,
        CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"]           = "query_group_slot_leaves",
            ["session_id"]    = sessionId,
            ["instance_addr"] = instanceAddr,
            ["slot_index"]    = slot.SlotIndex,
        };
        // Tie-breaker: with `deep`, one object owns one candidate per container BLOCK
        // and they share an instance address, so instance_addr alone can resolve to
        // the wrong block. The displayed leaf's address identifies the right one.
        if (!string.IsNullOrEmpty(slot.Addr)) req["leaf_addr"] = slot.Addr;
        if (offset > 0) req["offset"] = offset;
        if (limit  > 0) req["limit"]  = limit;

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var leaves = new List<GroupSlotMatch>();
        if (res["leaves"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject o) continue;
                var leaf = ParseGroupSlotLeaf(o);
                // Carry the slot's identity so a leaf renders and hands off exactly
                // like the representative does.
                leaf.SlotIndex    = slot.SlotIndex;
                leaf.Value        = slot.Value;
                leaf.ScanType     = slot.ScanType;
                leaf.Value2       = slot.Value2;
                leaf.InstanceAddr = instanceAddr;
                leaf.ClassName    = className;
                // A leaf is one identified field, not a converging set.
                leaf.Locked       = true;
                leaves.Add(leaf);
            }
        }
        return leaves;
    }

    public async Task EndGroupScanAsync(ulong sessionId, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "end_group_scan",
            ["session_id"] = sessionId,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
    }

    public async Task<int> BeginSnapshotAsync(string dataType, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "begin_snapshot",
            ["data_type"] = dataType,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return res["total"]?.GetValue<int>() ?? 0;
    }

    public async Task<SnapshotChunkResult> SnapshotChunkAsync(
        string dataType, bool gameOnly, int offset, int limit,
        bool nativeC = false, bool autoSkipNoise = true,
        string numericFamily = "Any", CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "snapshot_chunk",
            ["data_type"] = dataType,
            ["game_only"] = gameOnly,
            ["offset"] = offset,
            ["limit"] = limit,
            // Auto detect Engine/System noise (source-level skip; default ON). Sent
            // unconditionally so the DLL applies the checkbox's real value (the DLL
            // default is OFF for flag-unaware callers).
            ["auto_skip_noise"] = autoSkipNoise,
        };
        // Native-C raw-hole capture (P3), opt-in → attach only when on (back-compat).
        if (nativeC)
            req["native_c"] = (JsonNode)true;
        // Type-family narrowing, opt-in → attach only when narrowed (the DLL
        // defaults to "Any", so a flag-unaware caller keeps the full capture).
        if (!string.IsNullOrEmpty(numericFamily) && numericFamily != "Any")
            req["numeric_family"] = numericFamily;
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new SnapshotChunkResult
        {
            Total       = res["total"]?.GetValue<int>() ?? 0,
            Scanned     = res["scanned"]?.GetValue<int>() ?? 0,
            WalkMs      = res["walk_ms"]?.GetValue<long>() ?? 0,
            SerializeMs = res["serialize_ms"]?.GetValue<long>() ?? 0,
            // C#-side split injected by PipeClient.ReadLoopAsync for this response.
            ReadMs      = res["_t_read_ms"]?.GetValue<long>() ?? 0,
            RxLogMs     = res["_t_rxlog_ms"]?.GetValue<long>() ?? 0,
            ParseMs     = res["_t_parse_ms"]?.GetValue<long>() ?? 0,
        };

        var buildSw = System.Diagnostics.Stopwatch.StartNew();
        if (res["objects"] is JsonArray arr)
        {
            foreach (var node in arr)
            {
                if (node is not JsonObject o) continue;
                var obj = new SnapshotCapturedObject
                {
                    Index          = o["index"]?.GetValue<int>() ?? -1,
                    Addr           = o["addr"]?.GetValue<string>() ?? "",
                    Name           = o["name"]?.GetValue<string>() ?? "",
                    ClassName      = o["class"]?.GetValue<string>() ?? "",
                    OuterClassName = o["outer_class"]?.GetValue<string>() ?? "",
                    Path           = o["path"]?.GetValue<string>() ?? "",
                };
                if (o["fields"] is JsonArray fields)
                {
                    foreach (var fn in fields)
                    {
                        if (fn is not JsonObject f) continue;
                        obj.Fields.Add(ParseSnapshotField(f));
                    }
                }
                if (o["arrays"] is JsonArray arrays)
                {
                    foreach (var an in arrays)
                    {
                        if (an is not JsonObject ao) continue;
                        var ca = new SnapshotCapturedArray { Field = ao["field"]?.GetValue<string>() ?? "" };
                        if (ao["elements"] is JsonArray elems)
                        {
                            foreach (var en in elems)
                            {
                                if (en is not JsonObject eo) continue;
                                var el = new SnapshotCapturedArrayElement
                                {
                                    Index    = eo["i"]?.GetValue<int>() ?? 0,
                                    KeyName  = eo["key_name"]?.GetValue<string>() ?? "",
                                    KeyValue = eo["key_value"]?.GetValue<string>() ?? "",
                                };
                                if (eo["fields"] is JsonArray ef)
                                {
                                    foreach (var fn in ef)
                                    {
                                        if (fn is not JsonObject f) continue;
                                        el.Fields.Add(ParseSnapshotField(f));
                                    }
                                }
                                ca.Elements.Add(el);
                            }
                        }
                        obj.Arrays.Add(ca);
                    }
                }
                result.Objects.Add(obj);
            }
        }
        buildSw.Stop();
        result.BuildMs = buildSw.ElapsedMilliseconds;

        return result;
    }

    private static SnapshotCapturedField ParseSnapshotField(JsonObject f) => new()
    {
        Name   = f["name"]?.GetValue<string>() ?? "",
        Offset = f["off"]?.GetValue<int>() ?? 0,
        Type   = f["type"]?.GetValue<string>() ?? "",
        Hex    = f["hex"]?.GetValue<string>() ?? "",
    };

    public async Task<ClassListResult> ListClassesAsync(
        bool gameOnly = true, int limit = 5000, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "list_classes",
            ["game_only"] = gameOnly,
            ["limit"] = limit
        };

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new ClassListResult
        {
            Total = res["total"]?.GetValue<int>() ?? 0,
            ScannedObjects = res["scanned_objects"]?.GetValue<int>() ?? 0,
            TotalClasses = res["total_classes"]?.GetValue<int>() ?? 0,
            RequestedLimit = limit,
            // Cap detection is the DLL's job (it is the only side that knows whether the
            // walk reached the end of GObjects), but a pre-2882 DLL omits the key. Fall
            // back to the same inference the DLL makes — a full page means it stopped —
            // so an older DLL still gets an honest "capped", not a silent "not found".
            Truncated = res["truncated"]?.GetValue<bool>()
                        ?? ((res["classes"] as JsonArray)?.Count ?? 0) >= limit,
        };

        if (res["classes"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) continue;
                result.Classes.Add(new GameClassEntry
                {
                    ClassName       = obj["class_name"]?.GetValue<string>() ?? "",
                    ClassAddr       = obj["class_addr"]?.GetValue<string>() ?? "",
                    ClassPath       = obj["class_path"]?.GetValue<string>() ?? "",
                    SuperName       = obj["super_name"]?.GetValue<string>() ?? "",
                    PropertyCount   = obj["property_count"]?.GetValue<int>() ?? 0,
                    PropertiesSize  = obj["properties_size"]?.GetValue<int>() ?? 0,
                    Score           = obj["score"]?.GetValue<int>() ?? 0,
                });
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<NoiseClassInfo>> DetectNoiseClassesAsync(
        IReadOnlyList<string> classNames, CancellationToken ct = default)
    {
        var result = new List<NoiseClassInfo>();
        if (classNames == null || classNames.Count == 0) return result;

        var names = new JsonArray();
        foreach (var n in classNames)
            if (!string.IsNullOrEmpty(n)) names.Add((JsonNode)n);
        if (names.Count == 0) return result;

        var req = new JsonObject
        {
            ["cmd"] = "detect_noise_classes",
            ["class_names"] = names,
        };

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        if (res["classes"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) continue;
                result.Add(new NoiseClassInfo
                {
                    ClassName = obj["class_name"]?.GetValue<string>() ?? "",
                    IsNoise   = obj["is_noise"]?.GetValue<bool>() ?? false,
                    Reason    = obj["reason"]?.GetValue<string>() ?? "",
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Enumerate every UFunction across every loaded UClass. Backs the
    /// "Interesting Functions Finder" panel.
    /// </summary>
    /// <param name="gameOnly">When true, skip engine-package classes
    ///     (/Script/Engine, /Script/CoreUObject, etc.). Typically reduces
    ///     the result set ~5x for shipping games.</param>
    /// <param name="limit">Hard cap on returned entries to keep the pipe
    ///     payload bounded -- defaults to 100k, well above the ~50k
    ///     ceiling typical for shipping UE games.</param>
    public async Task<AllFunctionsResult> ListAllFunctionsAsync(
        bool gameOnly = true, int limit = 100000, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "list_all_functions",
            ["game_only"] = gameOnly,
            ["limit"] = limit,
        };

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var functions = new List<AllFunctionEntry>(
            res["total"]?.GetValue<int>() ?? 0);
        if (res["functions"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) continue;
                functions.Add(new AllFunctionEntry
                {
                    ClassName     = obj["class_name"]?.GetValue<string>() ?? "",
                    ClassAddr     = obj["class_addr"]?.GetValue<string>() ?? "",
                    SuperName     = obj["super_name"]?.GetValue<string>() ?? "",
                    ClassPath     = obj["class_path"]?.GetValue<string>() ?? "",
                    FuncName      = obj["func_name"]?.GetValue<string>() ?? "",
                    FuncAddr      = obj["func_addr"]?.GetValue<string>() ?? "",
                    FunctionFlags = (uint)(obj["function_flags"]?.GetValue<long>() ?? 0L),
                    NumParms      = (byte)(obj["num_parms"]?.GetValue<int>() ?? 0),
                    ParmsSize     = (ushort)(obj["parms_size"]?.GetValue<int>() ?? 0),
                });
            }
        }

        return new AllFunctionsResult
        {
            Total          = res["total"]?.GetValue<int>()          ?? 0,
            ScannedObjects = res["scanned_objects"]?.GetValue<int>() ?? 0,
            ScannedClasses = res["scanned_classes"]?.GetValue<int>() ?? 0,
            TotalFunctions = res["total_functions"]?.GetValue<int>() ?? 0,
            // Absent on a pre-Z8 DLL → false / 0, i.e. "assume complete", which is the
            // pre-existing behaviour rather than a new false alarm. (audit #5 Z8)
            Truncated      = res["truncated"]?.GetValue<bool>() ?? false,
            Aborted        = res["aborted"]?.GetValue<bool>()   ?? false,
            Limit          = res["limit"]?.GetValue<int>()      ?? limit,
            Functions      = functions,
        };
    }

    // --- Live ProcessEvent Profiler (Live Funcs) ---

    /// <summary>Start recording per-UFunction fire counts. Forces the game-thread
    /// PE hook to install; returns its <c>hook_active</c> (false ⇒ counts stay 0).</summary>
    public async Task<PeProfileStartResult> PeProfileStartAsync(CancellationToken ct = default)
    {
        var res = await _pipe.SendAsync(new JsonObject { ["cmd"] = "pe_profile_start" }, ct);
        CheckResponse(res);
        return new PeProfileStartResult
        {
            HookActive = res["hook_active"]?.GetValue<bool>() ?? false,
            Detail     = res["hook_detail"]?.GetValue<string>() ?? "",
        };
    }

    /// <summary>Stop recording (idempotent). Counts are retained for a later get.</summary>
    public async Task PeProfileStopAsync(CancellationToken ct = default)
    {
        var res = await _pipe.SendAsync(new JsonObject { ["cmd"] = "pe_profile_stop" }, ct);
        CheckResponse(res);
    }

    /// <summary>Fetch the ranked fire-count table (top <paramref name="limit"/> by count).</summary>
    public async Task<PeProfileResult> PeProfileGetAsync(int limit = 200, CancellationToken ct = default)
    {
        var res = await _pipe.SendAsync(
            new JsonObject { ["cmd"] = "pe_profile_get", ["limit"] = limit }, ct);
        CheckResponse(res);

        var entries = new List<PeProfileEntry>();
        if (res["functions"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) continue;
                entries.Add(new PeProfileEntry
                {
                    ClassName = obj["class_name"]?.GetValue<string>() ?? "",
                    FuncName  = obj["func_name"]?.GetValue<string>() ?? "",
                    FuncAddr  = obj["func_addr"]?.GetValue<string>() ?? "",
                    NumParms  = (byte)(obj["num_parms"]?.GetValue<int>() ?? 0),
                    ParmsSize = (ushort)(obj["parms_size"]?.GetValue<int>() ?? 0),
                    Count     = obj["count"]?.GetValue<long>() ?? 0L,
                    FirstSeq  = obj["first_seq"]?.GetValue<long>() ?? 0L,
                    FunctionFlags = (uint)(obj["function_flags"]?.GetValue<long>() ?? 0L),
                    IsWidget  = obj["is_widget"]?.GetValue<bool>() ?? false,
                    MeanPeriodMs = obj["mean_period_ms"]?.GetValue<double>() ?? 0.0,
                    Cv           = obj["cv"]?.GetValue<double>() ?? 0.0,
                    GapSamples   = obj["gap_samples"]?.GetValue<long>() ?? 0L,
                });
            }
        }

        return new PeProfileResult
        {
            Recording     = res["recording"]?.GetValue<bool>() ?? false,
            DistinctFuncs = res["distinct_funcs"]?.GetValue<int>() ?? 0,
            TotalCalls    = res["total_calls"]?.GetValue<long>() ?? 0L,
            Entries       = entries,
        };
    }

    public async Task<DiagnosticsResult> GetDiagnosticsAsync(int limit = 25, CancellationToken ct = default)
    {
        var res = await _pipe.SendAsync(
            new JsonObject { ["cmd"] = "get_diagnostics", ["limit"] = limit }, ct);
        CheckResponse(res);

        var cmds = new List<DiagnosticsCommandEntry>();
        if (res["commands"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) continue;
                // JsonNum, not GetValue<T>: diagnostics must degrade, never throw.
                // One out-of-range field (a UINT64_MAX sentinel) previously blanked
                // the entire card. See JsonNum's remarks.
                cmds.Add(new DiagnosticsCommandEntry
                {
                    Cmd     = obj["cmd"]?.GetValue<string>() ?? "",
                    Count   = JsonNum.L(obj["count"]),
                    TotalMs = JsonNum.D(obj["total_ms"]),
                    MaxMs   = JsonNum.D(obj["max_ms"]),
                    LastMs  = JsonNum.D(obj["last_ms"]),
                    AvgMs   = JsonNum.D(obj["avg_ms"]),
                });
            }
        }

        double totalBusy = JsonNum.D(res["total_busy_ms"]);
        // Share-of-busy is derived here rather than on the wire: the DLL already
        // sends the total, and a percentage computed client-side can't disagree
        // with the rows it is computed from.
        foreach (var c in cmds)
            c.SharePercent = totalBusy > 0 ? c.TotalMs * 100.0 / totalBusy : 0.0;

        var p = res["process"] as JsonObject;
        var g = res["game_thread"] as JsonObject;

        return new DiagnosticsResult
        {
            UptimeMs        = JsonNum.L(res["uptime_ms"]),
            TotalDispatches = JsonNum.L(res["total_dispatches"]),
            TotalBusyMs     = totalBusy,
            BusyPercent     = JsonNum.D(res["busy_percent"]),
            GObjectsCount   = JsonNum.I(res["gobjects_count"]),
            Process = new DiagnosticsProcess
            {
                WorkingSetBytes = JsonNum.L(p?["working_set_bytes"]),
                PrivateBytes    = JsonNum.L(p?["private_bytes"]),
                PeakWorkingSet  = JsonNum.L(p?["peak_working_set"]),
                HandleCount     = JsonNum.I(p?["handle_count"]),
                ThreadCount     = JsonNum.I(p?["thread_count"]),
                CpuPercent      = JsonNum.D(p?["cpu_percent"], -1.0),
            },
            GameThread = new DiagnosticsGameThread
            {
                HookActive      = JsonNum.B(g?["hook_active"]),
                HookFireCount   = JsonNum.L(g?["hook_fire_count"]),
                // -1 = never fired. A pre-fix DLL sends UINT64_MAX here, which
                // JsonNum saturates to long.MaxValue — also > 0, so it still reads
                // as "unknown" rather than as a plausible age.
                MsSinceLastFire = JsonNum.L(g?["ms_since_last_fire"], -1L),
                Responsive      = JsonNum.B(g?["responsive"]),
                InvokeTimeoutMs = JsonNum.I(g?["invoke_timeout_ms"]),
            },
            Commands = cmds,
        };
    }

    public async Task ResetDiagnosticsAsync(CancellationToken ct = default)
    {
        var res = await _pipe.SendAsync(new JsonObject { ["cmd"] = "reset_diagnostics" }, ct);
        CheckResponse(res);
    }

    // --- Extra Scan (user-triggered aggressive fallback) ---

    public async Task<RescanStartResult> StartRescanAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "rescan" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        return new RescanStartResult
        {
            ScanningGObjects = res["scanning_gobjects"]?.GetValue<bool>() ?? false,
            ScanningGWorld = res["scanning_gworld"]?.GetValue<bool>() ?? false,
        };
    }

    public async Task<RescanStatusResult> GetRescanStatusAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "rescan_status" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        return new RescanStatusResult
        {
            Running = res["running"]?.GetValue<bool>() ?? false,
            Phase = res["phase"]?.GetValue<int>() ?? 0,
            StatusText = res["status_text"]?.GetValue<string>() ?? "",
            FoundGObjects = res["found_gobjects"]?.GetValue<bool>() ?? false,
            FoundGWorld = res["found_gworld"]?.GetValue<bool>() ?? false,
            GObjectsAddr = res["gobjects_addr"]?.GetValue<string>() ?? "",
            GWorldAddr = res["gworld_addr"]?.GetValue<string>() ?? "",
            GObjectsMethod = res["gobjects_method"]?.GetValue<string>() ?? "",
            GWorldMethod = res["gworld_method"]?.GetValue<string>() ?? "",
        };
    }

    public async Task<EngineState> ApplyRescanAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "apply_rescan" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        // apply_rescan returns refreshed pointer data — build EngineState from it
        return BuildEngineState(res);
    }

    public async Task TriggerScanAsync(CancellationToken ct = default)
    {
        // trigger_scan now starts an async background scan and returns immediately.
        var req = new JsonObject { ["cmd"] = "trigger_scan" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
    }

    public async Task<ScanStatusResult> GetScanStatusAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "scan_status" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        bool running = res["running"]?.GetValue<bool>() ?? false;
        int phase = res["phase"]?.GetValue<int>() ?? 0;
        string statusText = res["status_text"]?.GetValue<string>() ?? "";

        // When complete, scan_status includes full pointer data
        EngineState? engineState = null;
        if (!running && res["scanned"]?.GetValue<bool>() == true)
        {
            // scan_status completion carries the full FillPointerSnapshot payload, so
            // BuildEngineState reads ue_version / version_detected straight off `res`.
            // Re-parsing them here to pass back in would just be a second copy that can drift.
            engineState = BuildEngineState(res);
        }

        return new ScanStatusResult
        {
            Running = running,
            Phase = phase,
            StatusText = statusText,
            EngineState = engineState,
        };
    }

    public async Task<InvokeFunctionResult> InvokeFunctionAsync(
        string funcName,
        string? instanceAddr = null,
        string? className = null,
        int parmsSize = 0,
        string? paramsHex = null,
        bool directCall = false,
        IReadOnlyList<InvokeStringParam>? stringParams = null,
        CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "invoke_function",
            ["func_name"] = funcName,
        };

        if (!string.IsNullOrEmpty(instanceAddr))
            req["instance_addr"] = instanceAddr;

        if (!string.IsNullOrEmpty(className))
            req["class_name"] = className;

        if (parmsSize > 0)
            req["parms_size"] = parmsSize;

        if (!string.IsNullOrEmpty(paramsHex))
            req["params_hex"] = paramsHex;

        // Static-native fast path (KismetMathLibrary etc.) — only set when
        // caller has verified the function is safe to call off-thread. Default
        // false preserves the existing behavior for LiveWalker's Pipe Invoke.
        if (directCall)
            req["direct_call"] = true;

        // String INPUT params: sent as descriptors so the DLL builds each
        // by-value FString in the game process (its Data pointer must be a
        // valid game-process address; the 16-byte slots stay zeroed in
        // params_hex).
        if (stringParams is { Count: > 0 })
        {
            var arr = new JsonArray();
            foreach (var sp in stringParams)
            {
                // Cast to JsonNode? so the non-generic JsonArray.Add(JsonNode?)
                // overload is picked. The generic Add<T>(T) is trim/AOT-unsafe
                // (IL2026/IL3050) and overload resolution would otherwise select
                // it (identity conversion to JsonObject beats the reference
                // conversion to JsonNode). Only surfaces under the trimmed AOT
                // publish, not a plain Release build.
                arr.Add((JsonNode?)new JsonObject
                {
                    ["off"]  = sp.Offset,
                    ["wide"] = sp.Wide,
                    ["text"] = sp.Text,
                });
            }
            req["str_params"] = arr;
        }

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        return new InvokeFunctionResult
        {
            Result       = res["result"]?.GetValue<int>() ?? -999,
            InstanceAddr = res["instance_addr"]?.GetValue<string>() ?? "",
            FuncAddr     = res["func_addr"]?.GetValue<string>() ?? "",
            ParmsSize    = res["parms_size"]?.GetValue<int>() ?? 0,
            ResultHex    = res["result_hex"]?.GetValue<string>() ?? "",
            Message      = res["message"]?.GetValue<string>() ?? "",
            Error        = res["error"]?.GetValue<string>() ?? "",
        };
    }

    public async Task<int> GetDebugCameraStateAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "get_debug_camera_state" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return res["state"]?.GetValue<int>() ?? -1;
    }

    public async Task<int> SetDebugCameraAsync(bool enable, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "set_debug_camera",
            ["enable"] = enable,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return res["state"]?.GetValue<int>() ?? -1;
    }

    // === God Mode (Solitar) — docs/godmode-spec.md §6.1 ===

    public async Task<int> GetGodModeAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "get_god_mode" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return res["state"]?.GetValue<int>() ?? -1;
    }

    public async Task<int> SetGodModeAsync(bool enable, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "set_god_mode",
            ["enable"] = enable,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return res["state"]?.GetValue<int>() ?? -1;
    }

    // === Foreground lock (Grausam) — keep the game thread alive when unfocused ===

    public async Task<int> GetForegroundLockAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "get_foreground_lock" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return res["state"]?.GetValue<int>() ?? 0;
    }

    public async Task<int> SetForegroundLockAsync(bool enable, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "set_foreground_lock",
            ["enable"] = enable,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return res["state"]?.GetValue<int>() ?? -1;
    }

    // === Movement tuning (Laufen) ===

    private static MovementKnob ParseKnob(JsonNode? n)
    {
        if (n is null) return new MovementKnob();
        return new MovementKnob
        {
            Resolved    = n["resolved"]?.GetValue<bool>() ?? false,
            Current     = n["current"]?.GetValue<double>() ?? 0.0,
            Base        = n["base"]?.GetValue<double>() ?? 0.0,
            Multiplier  = n["multiplier"]?.GetValue<double>() ?? 1.0,
            Active      = n["active"]?.GetValue<bool>() ?? false,
            OwnerAddr   = n["owner_addr"]?.GetValue<string>() ?? "",
            FieldOffset = n["field_offset"]?.GetValue<int>() ?? -1,
            FieldName   = n["field_name"]?.GetValue<string>() ?? "",
        };
    }

    public async Task<MovementParams> GetMovementParamsAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "get_movement_params" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        var knobs = res["knobs"];
        return new MovementParams
        {
            Code      = res["code"]?.GetValue<int>() ?? 0,
            HasCmc    = res["has_cmc"]?.GetValue<bool>() ?? false,
            WalkSpeed = ParseKnob(knobs?["walk_speed"]),
            Gravity   = ParseKnob(knobs?["gravity"]),
            Jump      = ParseKnob(knobs?["jump"]),
            GravityDirection = ParseVectorKnob(res["gravity_direction"]),
        };
    }

    public async Task<TrainerOffsets> GetTrainerOffsetsAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "get_trainer_offsets" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        var o = new TrainerOffsets
        {
            Code         = res["code"]?.GetValue<int>() ?? 0,
            PawnToRoot   = res["pawn_to_root"]?.GetValue<int>() ?? -1,
            RootToRelLoc = res["root_to_relloc"]?.GetValue<int>() ?? -1,
            FVectorWidth = res["fvector_width"]?.GetValue<int>() ?? 0,
            PawnToCmc    = res["pawn_to_cmc"]?.GetValue<int>() ?? -1,
            WalkSpeedOff = res["walk_speed_off"]?.GetValue<int>() ?? -1,
            GravityOff   = res["gravity_off"]?.GetValue<int>() ?? -1,
            JumpOff      = res["jump_off"]?.GetValue<int>() ?? -1,
            CtrlRotOff   = res["ctrl_rot_off"]?.GetValue<int>() ?? -1,
            CtrlRotSize  = res["ctrl_rot_size"]?.GetValue<int>() ?? 0,
            PawnToController = res["pawn_to_controller"]?.GetValue<int>() ?? -1,
            MoveModeOff  = res["move_mode_off"]?.GetValue<int>() ?? -1,
            VelocityOff  = res["velocity_off"]?.GetValue<int>() ?? -1,
            VelocitySize = res["velocity_size"]?.GetValue<int>() ?? 0,
        };
        if (res["chain"] is JsonArray chain)
            foreach (var h in chain)
            {
                if (h is null) continue;
                o.Chain.Add(new TrainerChainHop
                {
                    Field  = h["field"]?.GetValue<string>() ?? "",
                    Offset = h["offset"]?.GetValue<int>() ?? 0,
                    Deref  = h["deref"]?.GetValue<bool>() ?? true,
                });
            }
        if (res["god_bits"] is JsonArray god)
            foreach (var g in god)
            {
                if (g is null) continue;
                o.GodBits.Add(new TrainerProtectBit
                {
                    Name       = g["name"]?.GetValue<string>() ?? "",
                    ByteOffset = g["byte_offset"]?.GetValue<int>() ?? -1,
                    Mask       = g["mask"]?.GetValue<int>() ?? 0,
                    Protect    = g["protect"]?.GetValue<int>() ?? 0,
                });
            }
        return o;
    }

    private static MovementVectorKnob ParseVectorKnob(JsonNode? n)
    {
        if (n is null) return new MovementVectorKnob();
        return new MovementVectorKnob
        {
            Resolved    = n["resolved"]?.GetValue<bool>() ?? false,
            X           = n["x"]?.GetValue<double>() ?? 0,
            Y           = n["y"]?.GetValue<double>() ?? 0,
            Z           = n["z"]?.GetValue<double>() ?? -1,
            Active      = n["active"]?.GetValue<bool>() ?? false,
            OwnerAddr   = n["owner_addr"]?.GetValue<string>() ?? "",
            FieldOffset = n["field_offset"]?.GetValue<int>() ?? -1,
            FieldName   = n["field_name"]?.GetValue<string>() ?? "",
        };
    }

    public async Task<MovementVectorResult> SetGravityDirectionAsync(double x, double y, double z, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "set_gravity_direction",
            ["x"] = x, ["y"] = y, ["z"] = z,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return ParseVectorResult(res);
    }

    public async Task<MovementVectorResult> ResetGravityDirectionAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "reset_gravity_direction" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return ParseVectorResult(res);
    }

    private static MovementVectorResult ParseVectorResult(JsonObject res) => new()
    {
        State    = res["state"]?.GetValue<int>() ?? (res["active"]?.GetValue<bool>() ?? false ? 1 : 0),
        Code     = res["code"]?.GetValue<int>() ?? 0,
        Resolved = res["resolved"]?.GetValue<bool>() ?? false,
        Active   = res["active"]?.GetValue<bool>() ?? false,
        X = res["x"]?.GetValue<double>() ?? 0,
        Y = res["y"]?.GetValue<double>() ?? 0,
        Z = res["z"]?.GetValue<double>() ?? -1,
    };

    public async Task<MovementSetResult> SetMovementMultiplierAsync(string knob, double multiplier, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "set_movement_multiplier",
            ["knob"] = knob,
            ["multiplier"] = multiplier,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return new MovementSetResult
        {
            State      = res["state"]?.GetValue<int>() ?? -1,
            Code       = res["code"]?.GetValue<int>() ?? 0,
            Active     = res["active"]?.GetValue<bool>() ?? false,
            Current    = res["current"]?.GetValue<double>() ?? 0.0,
            Base       = res["base"]?.GetValue<double>() ?? 0.0,
            Multiplier = res["multiplier"]?.GetValue<double>() ?? 1.0,
        };
    }

    public async Task<MovementSetResult> ResetMovementAsync(string knob, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "reset_movement", ["knob"] = knob };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        bool active = res["active"]?.GetValue<bool>() ?? false;
        return new MovementSetResult
        {
            State      = active ? 1 : 0,
            Code       = res["code"]?.GetValue<int>() ?? 0,
            Active     = active,
            Current    = res["current"]?.GetValue<double>() ?? 0.0,
            Base       = res["base"]?.GetValue<double>() ?? 0.0,
            Multiplier = res["multiplier"]?.GetValue<double>() ?? 1.0,
        };
    }

    // === Time dilation (Hemmung) — global slow-mo / freeze / speed-up ===

    private static TimeDilationKnob ParseTimeKnob(JsonNode? n)
    {
        if (n is null) return new TimeDilationKnob();
        return new TimeDilationKnob
        {
            Resolved    = n["resolved"]?.GetValue<bool>() ?? false,
            Current     = n["current"]?.GetValue<double>() ?? 0.0,
            Base        = n["base"]?.GetValue<double>() ?? 1.0,
            Value       = n["value"]?.GetValue<double>() ?? 1.0,
            Active      = n["active"]?.GetValue<bool>() ?? false,
            OwnerAddr   = n["owner_addr"]?.GetValue<string>() ?? "",
            FieldOffset = n["field_offset"]?.GetValue<int>() ?? -1,
            FieldName   = n["field_name"]?.GetValue<string>() ?? "",
        };
    }

    public async Task<TimeState> GetTimeStateAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "get_time_state" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        var dil = res["dilation"];
        return new TimeState
        {
            Code   = res["code"]?.GetValue<int>() ?? 0,
            Global = ParseTimeKnob(dil?["global"]),
            Pawn   = ParseTimeKnob(dil?["pawn"]),
        };
    }

    public async Task<ProtectState> GetProtectStateAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "get_protect_state" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        // Flat envelope (MakeResponse merge-patches the data object in), so these
        // are top-level. NOTE the live value arrives as "godmode", not "live" —
        // renaming it here would silently fall through to the ?? default and
        // report "no pawn" forever.
        return new ProtectState
        {
            Want       = res["want"]?.GetValue<int>() ?? 0,
            Live       = res["godmode"]?.GetValue<int>() ?? -1,
            Resolvable = res["resolvable"]?.GetValue<bool>() ?? false,
            Code       = res["code"]?.GetValue<int>() ?? 0,
        };
    }

    public async Task<TimeDilationSetResult> SetTimeDilationAsync(string target, double value, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "set_time_dilation", ["target"] = target, ["value"] = value };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        var k = ParseTimeKnob(res["dilation"]);
        return new TimeDilationSetResult
        {
            State   = res["state"]?.GetValue<int>() ?? -1,
            Code    = res["code"]?.GetValue<int>() ?? 0,
            Active  = k.Active,
            Current = k.Current,
            Base    = k.Base,
            Value   = k.Value,
        };
    }

    public async Task<TimeDilationSetResult> ResetTimeDilationAsync(string target, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "reset_time_dilation", ["target"] = target };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        var k = ParseTimeKnob(res["dilation"]);
        return new TimeDilationSetResult
        {
            State   = k.Active ? 1 : 0,
            Code    = res["code"]?.GetValue<int>() ?? 0,
            Active  = k.Active,
            Current = k.Current,
            Base    = k.Base,
            Value   = k.Value,
        };
    }

    // === Force-field hold + stealth meter (Solide) ===

    public async Task<ForceFieldResult> ForceFieldAsync(string className, string fieldName, string kind, double value = 0, bool on = false, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"]        = "force_field",
            ["class_name"] = className,
            ["field_name"] = fieldName,
            ["kind"]       = kind,
        };
        if (kind == "bool") req["on"] = on;
        else if (kind == "numeric") req["value"] = value;
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return new ForceFieldResult
        {
            Held      = res["held"]?.GetValue<int>() ?? 0,
            Resolved  = res["resolved"]?.GetValue<bool>() ?? false,
            Code      = res["code"]?.GetValue<int>() ?? 0,
            // Absent on an older DLL (and on held<=0, where the DLL omits it) → false.
            Truncated = res["truncated"]?.GetValue<bool>() ?? false,
        };
    }

    public async Task<int> ResetFieldAsync(string className, string fieldName, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"]        = "reset_field",
            ["class_name"] = className,
            ["field_name"] = fieldName,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return res["code"]?.GetValue<int>() ?? 0;
    }

    public async Task<int> ResetAllFieldsAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "reset_all_fields" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return res["code"]?.GetValue<int>() ?? 0;
    }

    public async Task<IReadOnlyList<ForcedFieldInfo>> GetForcedFieldsAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "get_forced_fields" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        var list = new List<ForcedFieldInfo>();
        if (res["fields"] is JsonArray arr)
        {
            foreach (var f in arr)
            {
                if (f == null) continue;
                list.Add(new ForcedFieldInfo
                {
                    ClassName   = f["class_name"]?.GetValue<string>() ?? "",
                    FieldName   = f["field_name"]?.GetValue<string>() ?? "",
                    Kind        = f["kind"]?.GetValue<string>() ?? "bool",
                    Value       = f["value"]?.GetValue<double>() ?? 0,
                    Held        = f["held"]?.GetValue<int>() ?? 0,
                    OwnerAddr   = f["owner_addr"]?.GetValue<string>() ?? "",
                    FieldOffset = f["field_offset"]?.GetValue<int>() ?? -1,
                    Truncated   = f["truncated"]?.GetValue<bool>() ?? false,
                });
            }
        }
        return list;
    }

    public async Task<IReadOnlyList<StealthCandidate>> FindStealthMeterAsync(int max = 8, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "find_stealth_meter", ["max"] = max };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        var list = new List<StealthCandidate>();
        if (res["candidates"] is JsonArray arr)
        {
            foreach (var c in arr)
            {
                if (c == null) continue;
                list.Add(new StealthCandidate
                {
                    ClassName = c["class_name"]?.GetValue<string>() ?? "",
                    ClassAddr = c["class_addr"]?.GetValue<string>() ?? "",
                    FieldName = c["field_name"]?.GetValue<string>() ?? "",
                    PropType  = c["prop_type"]?.GetValue<string>() ?? "",
                    OwnerAddr = c["owner_addr"]?.GetValue<string>() ?? "",
                    Current   = c["current"]?.GetValue<double>() ?? 0,
                    Score     = c["score"]?.GetValue<int>() ?? 0,
                });
            }
        }
        return list;
    }

    // === Fly (Dunste) — no-gravity keyboard-driven 3D flight ===

    public async Task<FlyStatus> FlySetAsync(bool? enable, double? speed, int? preset, bool? noclip, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "fly_set" };
        if (enable.HasValue) req["enable"] = enable.Value;
        if (speed.HasValue)  req["speed"]  = speed.Value;
        if (preset.HasValue) req["preset"] = preset.Value;
        if (noclip.HasValue) req["noclip"] = noclip.Value;
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return ParseFlyStatus(res);
    }

    public async Task<FlyStatus> FlyGetStateAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "fly_get_state" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return ParseFlyStatus(res);
    }

    private static FlyStatus ParseFlyStatus(JsonNode? res) => new FlyStatus
    {
        Code        = res?["code"]?.GetValue<int>() ?? 0,
        Active      = res?["active"]?.GetValue<bool>() ?? false,
        Noclip      = res?["noclip"]?.GetValue<bool>() ?? false,
        HasCmc      = res?["has_cmc"]?.GetValue<bool>() ?? false,
        Preset      = res?["preset"]?.GetValue<int>() ?? 0,
        Speed       = res?["speed"]?.GetValue<double>() ?? 0.0,
        CurrentMode = res?["current_mode"]?.GetValue<int>() ?? -1,
        State       = res?["state"]?.GetValue<int>() ?? -1,
    };

    // === See-through occluders (Schlacht) ===

    public async Task<SeeThroughStatus> SeeThroughSetAsync(bool? enable, int? count, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "seethrough_set" };
        if (enable.HasValue) req["enable"] = enable.Value;
        if (count.HasValue)  req["count"]  = count.Value;
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return ParseSeeThroughStatus(res);
    }

    public async Task<SeeThroughStatus> SeeThroughGetStateAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "seethrough_get_state" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return ParseSeeThroughStatus(res);
    }

    private static SeeThroughStatus ParseSeeThroughStatus(JsonNode? res) => new SeeThroughStatus
    {
        Code        = res?["code"]?.GetValue<int>() ?? 0,
        Active      = res?["active"]?.GetValue<bool>() ?? false,
        HasTarget   = res?["has_target"]?.GetValue<bool>() ?? false,
        HiddenCount = res?["hidden_count"]?.GetValue<int>() ?? 0,
        PierceCount = res?["pierce_count"]?.GetValue<int>() ?? 1,
        State       = res?["state"]?.GetValue<int>() ?? -1,
        // Older DLLs don't send it; assume healthy so their behaviour is unchanged.
        HookActive  = res?["hook_active"]?.GetValue<bool>() ?? true,
    };

    // === Teleport (Wirbel) — docs/teleport-spec.md §7 ===

    public async Task<TeleportPose> TeleportGetPoseAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "teleport_get_pose" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return ParsePose(res);
    }

    public async Task<TeleportPose> TeleportSaveMarkerAsync(int slot, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "teleport_save_marker", ["slot"] = slot };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return ParsePose(res);
    }

    public async Task<TeleportResult> TeleportRecallMarkerAsync(int slot, bool force, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "teleport_recall_marker",
            ["slot"] = slot,
            ["force"] = force,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return ParseTeleportResult(res);
    }

    public async Task<TeleportResult> TeleportRecallExplicitAsync(double x, double y, double z,
        double? pitch = null, double? yaw = null, double? roll = null, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "teleport_recall_marker",
            ["x"] = x, ["y"] = y, ["z"] = z,
        };
        if (pitch.HasValue) req["pitch"] = pitch.Value;
        if (yaw.HasValue)   req["yaw"]   = yaw.Value;
        if (roll.HasValue)  req["roll"]  = roll.Value;
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return ParseTeleportResult(res);
    }

    public async Task<TeleportResult> TeleportToCursorAsync(double zOffset, int channel,
        bool fallbackCenter, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "teleport_to_cursor",
            ["zOffset"] = zOffset,
            ["channel"] = channel,
            ["fallbackCenter"] = fallbackCenter,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return ParseTeleportResult(res);
    }

    public async Task<List<TeleportMarker>> TeleportGetMarkersAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "teleport_get_markers" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        var list = new List<TeleportMarker>();
        if (res["markers"] is JsonArray arr)
        {
            foreach (var node in arr)
            {
                if (node is not JsonObject m) continue;
                list.Add(new TeleportMarker
                {
                    Slot  = m["slot"]?.GetValue<int>() ?? 0,
                    Valid = m["valid"]?.GetValue<bool>() ?? false,
                    X = m["x"]?.GetValue<double>() ?? 0,
                    Y = m["y"]?.GetValue<double>() ?? 0,
                    Z = m["z"]?.GetValue<double>() ?? 0,
                    Pitch = m["pitch"]?.GetValue<double>() ?? 0,
                    Yaw   = m["yaw"]?.GetValue<double>() ?? 0,
                    Roll  = m["roll"]?.GetValue<double>() ?? 0,
                    Map   = m["map"]?.GetValue<string>() ?? "",
                });
            }
        }
        return list;
    }

    public async Task<int> TeleportClearMarkerAsync(int slot, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "teleport_clear_marker", ["slot"] = slot };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return res["code"]?.GetValue<int>() ?? -1;
    }

    public async Task<TeleportResult> TeleportRecallLastAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "teleport_recall_last" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return ParseTeleportResult(res);
    }

    public async Task<TeleportPov> TeleportGetPovAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "teleport_get_pov" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return new TeleportPov
        {
            Code  = res["code"]?.GetValue<int>() ?? -1,
            CamX  = res["camX"]?.GetValue<double>() ?? 0,
            CamY  = res["camY"]?.GetValue<double>() ?? 0,
            CamZ  = res["camZ"]?.GetValue<double>() ?? 0,
            Pitch = res["pitch"]?.GetValue<double>() ?? 0,
            Yaw   = res["yaw"]?.GetValue<double>() ?? 0,
            Roll  = res["roll"]?.GetValue<double>() ?? 0,
            Fov   = res["fov"]?.GetValue<double>() ?? 0,
            Source = res["source"]?.GetValue<string>() ?? "invoke",
            HasPawn = res["hasPawn"]?.GetValue<bool>() ?? false,
            PawnX = res["pawnX"]?.GetValue<double>() ?? 0,
            PawnY = res["pawnY"]?.GetValue<double>() ?? 0,
            PawnZ = res["pawnZ"]?.GetValue<double>() ?? 0,
            CamOwnerAddr      = res["cam_owner_addr"]?.GetValue<string>() ?? "",
            CamLocFieldOffset = res["cam_loc_field_offset"]?.GetValue<int>() ?? -1,
            CamLocFieldName   = res["cam_loc_field_name"]?.GetValue<string>() ?? "",
            CamRotFieldOffset = res["cam_rot_field_offset"]?.GetValue<int>() ?? -1,
            CamRotFieldName   = res["cam_rot_field_name"]?.GetValue<string>() ?? "",
            CamFovFieldOffset = res["cam_fov_field_offset"]?.GetValue<int>() ?? -1,
            CamFovFieldName   = res["cam_fov_field_name"]?.GetValue<string>() ?? "",
        };
    }

    public async Task<TeleportPose> TeleportRelativeAsync(double distance, bool horizontal,
        CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "teleport_relative",
            ["distance"] = distance,
            ["horizontal"] = horizontal,
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        return ParsePose(res);
    }

    public async Task<int> SetMouseCursorAsync(bool show, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "set_mouse_cursor", ["show"] = show };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        int code = res["code"]?.GetValue<int>() ?? -1;
        if (code != 0) return -1;
        return (res["state"]?.GetValue<bool>() ?? false) ? 1 : 0;
    }

    public async Task<int> GetMouseCursorAsync(CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "get_mouse_cursor" };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);
        int code = res["code"]?.GetValue<int>() ?? -1;
        if (code != 0) return -1;
        return (res["state"]?.GetValue<bool>() ?? false) ? 1 : 0;
    }

    private static TeleportPose ParsePose(JsonObject res) => new()
    {
        Code  = res["code"]?.GetValue<int>() ?? -1,
        X = res["x"]?.GetValue<double>() ?? 0,
        Y = res["y"]?.GetValue<double>() ?? 0,
        Z = res["z"]?.GetValue<double>() ?? 0,
        Pitch = res["pitch"]?.GetValue<double>() ?? 0,
        Yaw   = res["yaw"]?.GetValue<double>() ?? 0,
        Roll  = res["roll"]?.GetValue<double>() ?? 0,
        Map    = res["map"]?.GetValue<string>() ?? "",
        Source = res["source"]?.GetValue<string>() ?? "raw",
        PawnAddr    = res["pawn_addr"]?.GetValue<string>() ?? "",
        HasMovement = res["has_movement"]?.GetValue<bool>() ?? false,
        VelX  = res["vel_x"]?.GetValue<double>() ?? 0,
        VelY  = res["vel_y"]?.GetValue<double>() ?? 0,
        VelZ  = res["vel_z"]?.GetValue<double>() ?? 0,
        AccX  = res["acc_x"]?.GetValue<double>() ?? 0,
        AccY  = res["acc_y"]?.GetValue<double>() ?? 0,
        AccZ  = res["acc_z"]?.GetValue<double>() ?? 0,
        Speed = res["speed"]?.GetValue<double>() ?? 0,
        LocOwnerAddr   = res["loc_owner_addr"]?.GetValue<string>() ?? "",
        LocFieldOffset = res["loc_field_offset"]?.GetValue<int>() ?? 0,
        LocFieldName   = res["loc_field_name"]?.GetValue<string>() ?? "",
        VelOwnerAddr   = res["vel_owner_addr"]?.GetValue<string>() ?? "",
        VelFieldOffset = res["vel_field_offset"]?.GetValue<int>() ?? 0,
        VelFieldName   = res["vel_field_name"]?.GetValue<string>() ?? "",
        AccOwnerAddr   = res["acc_owner_addr"]?.GetValue<string>() ?? "",
        AccFieldOffset = res["acc_field_offset"]?.GetValue<int>() ?? 0,
        AccFieldName   = res["acc_field_name"]?.GetValue<string>() ?? "",
        RotOwnerAddr   = res["rot_owner_addr"]?.GetValue<string>() ?? "",
        RotFieldOffset = res["rot_field_offset"]?.GetValue<int>() ?? 0,
        RotFieldName   = res["rot_field_name"]?.GetValue<string>() ?? "",
    };

    private static TeleportResult ParseTeleportResult(JsonObject res) => new()
    {
        Code = res["code"]?.GetValue<int>() ?? -1,
        Tier = res["tier"]?.GetValue<int>() ?? 0,
        CurrentMap = res["map"]?.GetValue<string>(),
        MarkerMap  = res["markerMap"]?.GetValue<string>(),
        UsedCenter = res["usedCenter"]?.GetValue<bool>() ?? false,
        HitX = res["hitX"]?.GetValue<double>() ?? 0,
        HitY = res["hitY"]?.GetValue<double>() ?? 0,
        HitZ = res["hitZ"]?.GetValue<double>() ?? 0,
    };

    private static void CheckResponse(JsonObject res)
    {
        var ok = res["ok"]?.GetValue<bool>() ?? false;
        if (!ok)
        {
            var error = res["error"]?.GetValue<string>() ?? "Unknown error";
            throw new InvalidOperationException($"DLL error: {error}");
        }
    }
}
