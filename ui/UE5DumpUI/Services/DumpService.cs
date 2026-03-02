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

        // version_detected: true if PE/memory scan succeeded, false if using default/inferred
        var versionDetected = res["version_detected"]?.GetValue<bool>() ?? true;

        return BuildEngineState(ptrs, ueVersion, versionDetected);
    }

    public async Task<EngineState> GetPointersAsync(CancellationToken ct = default)
    {
        var res = await _pipe.SendAsync(new JsonObject { ["cmd"] = "get_pointers" }, ct);
        CheckResponse(res);

        return BuildEngineState(res);
    }

    /// <summary>Build EngineState from a get_pointers response, with optional overrides from init.</summary>
    private static EngineState BuildEngineState(JsonObject ptrs, int ueVersion = 0, bool versionDetected = true)
    {
        if (ueVersion == 0)
            ueVersion = ptrs["ue_version"]?.GetValue<int>() ?? 0;

        var scanStats = ptrs["scan_stats"] as JsonObject;

        return new EngineState
        {
            UEVersion = ueVersion,
            VersionDetected = versionDetected,
            GObjectsAddr = ptrs["gobjects"]?.GetValue<string>() ?? "",
            GNamesAddr = ptrs["gnames"]?.GetValue<string>() ?? "",
            GWorldAddr = ptrs["gworld"]?.GetValue<string>() ?? "",
            ObjectCount = ptrs["object_count"]?.GetValue<int>() ?? 0,
            ModuleName = ptrs["module_name"]?.GetValue<string>() ?? "",
            ModuleBase = ptrs["module_base"]?.GetValue<string>() ?? "",
            GObjectsMethod = ptrs["gobjects_method"]?.GetValue<string>() ?? "aob",
            GNamesMethod = ptrs["gnames_method"]?.GetValue<string>() ?? "aob",
            GWorldMethod = ptrs["gworld_method"]?.GetValue<string>() ?? "aob",
            // AOB Usage Tracking
            PeHash = ptrs["pe_hash"]?.GetValue<string>() ?? "",
            GObjectsPatternId = ptrs["gobjects_pattern_id"]?.GetValue<string>() ?? "",
            GNamesPatternId = ptrs["gnames_pattern_id"]?.GetValue<string>() ?? "",
            GWorldPatternId = ptrs["gworld_pattern_id"]?.GetValue<string>() ?? "",
            GObjectsPatternsTried = scanStats?["gobjects_tried"]?.GetValue<int>() ?? 0,
            GObjectsPatternsHit = scanStats?["gobjects_hit"]?.GetValue<int>() ?? 0,
            GNamesPatternsTried = scanStats?["gnames_tried"]?.GetValue<int>() ?? 0,
            GNamesPatternsHit = scanStats?["gnames_hit"]?.GetValue<int>() ?? 0,
            GWorldPatternsTried = scanStats?["gworld_tried"]?.GetValue<int>() ?? 0,
            GWorldPatternsHit = scanStats?["gworld_hit"]?.GetValue<int>() ?? 0,
        };
    }

    public async Task<int> GetObjectCountAsync(CancellationToken ct = default)
    {
        var res = await _pipe.SendAsync(new JsonObject { ["cmd"] = "get_object_count" }, ct);
        CheckResponse(res);
        return res["count"]?.GetValue<int>() ?? 0;
    }

    public async Task<ObjectListResult> GetObjectListAsync(int offset, int limit, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "get_object_list",
            ["offset"] = offset,
            ["limit"] = limit
        };
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
                result.Objects.Add(new UObjectNode
                {
                    Address = obj["addr"]?.GetValue<string>() ?? "",
                    Name = obj["name"]?.GetValue<string>() ?? "",
                    ClassName = string.Intern(obj["class"]?.GetValue<string>() ?? ""),
                    OuterAddr = obj["outer"]?.GetValue<string>() ?? "",
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

    public async Task<ObjectListResult> SearchObjectsAsync(string query, int limit = 200, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "search_objects",
            ["query"] = query,
            ["limit"] = limit
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new ObjectListResult
        {
            Total = res["total"]?.GetValue<int>() ?? 0,
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

        var model = new ClassInfoModel
        {
            Name = classObj["name"]?.GetValue<string>() ?? "",
            FullPath = classObj["full_path"]?.GetValue<string>() ?? "",
            SuperAddress = classObj["super_addr"]?.GetValue<string>() ?? "",
            SuperName = classObj["super_name"]?.GetValue<string>() ?? "",
            PropertiesSize = classObj["props_size"]?.GetValue<int>() ?? 0,
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
                });
            }
        }

        return model;
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

    public async Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null, int arrayLimit = 64, CancellationToken ct = default)
    {
        var req = new JsonObject { ["cmd"] = "walk_instance", ["addr"] = addr };
        if (!string.IsNullOrEmpty(classAddr)) req["class_addr"] = classAddr;
        if (arrayLimit != 64) req["array_limit"] = arrayLimit;

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new InstanceWalkResult
        {
            Address = res["addr"]?.GetValue<string>() ?? "",
            Name = res["name"]?.GetValue<string>() ?? "",
            ClassName = res["class"]?.GetValue<string>() ?? "",
            ClassAddr = res["class_addr"]?.GetValue<string>() ?? "",
            OuterAddr = res["outer"]?.GetValue<string>() ?? "",
            OuterName = res["outer_name"]?.GetValue<string>() ?? "",
            OuterClassName = res["outer_class"]?.GetValue<string>() ?? "",
        };

        if (res["fields"] is JsonArray fields)
        {
            foreach (var f in fields)
            {
                if (f is not JsonObject fo) continue;
                result.Fields.Add(new LiveFieldValue
                {
                    Name = fo["name"]?.GetValue<string>() ?? "",
                    TypeName = fo["type"]?.GetValue<string>() ?? "",
                    Offset = fo["offset"]?.GetValue<int>() ?? 0,
                    Size = fo["size"]?.GetValue<int>() ?? 0,
                    HexValue = fo["hex"]?.GetValue<string>() ?? "",
                    TypedValue = fo["value"]?.GetValue<string>() ?? "",
                    PtrAddress = fo["ptr"]?.GetValue<string>() ?? "",
                    PtrName = fo["ptr_name"]?.GetValue<string>() ?? "",
                    PtrClassName = fo["ptr_class"]?.GetValue<string>() ?? "",
                    PtrClassAddr = fo["ptr_class_addr"]?.GetValue<string>() ?? "",
                    BoolBitIndex = fo["bool_bit"]?.GetValue<int>() ?? -1,
                    BoolFieldMask = fo["bool_mask"]?.GetValue<int>() ?? 0,
                    ArrayCount = fo["count"]?.GetValue<int>() ?? -1,
                    ArrayInnerType = fo["array_inner_type"]?.GetValue<string>() ?? "",
                    ArrayStructType = fo["array_struct_type"]?.GetValue<string>() ?? "",
                    ArrayElemSize = fo["array_elem_size"]?.GetValue<int>() ?? 0,
                    ArrayInnerAddr = fo["array_inner_addr"]?.GetValue<string>() ?? "",
                    ArrayDataAddr = fo["array_data_addr"]?.GetValue<string>() ?? "",
                    ArrayStructClassAddr = fo["array_struct_class_addr"]?.GetValue<string>() ?? "",
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
                    MapElements = ParseContainerElements(fo["map_elements"]),
                    SetCount = fo["set_count"]?.GetValue<int>() ?? -1,
                    SetElemType = fo["set_elem_type"]?.GetValue<string>() ?? "",
                    SetElemSize = fo["set_elem_size"]?.GetValue<int>() ?? 0,
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
                });
            }
        }

        return result;
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

    public async Task<FindInstancesResult> FindInstancesAsync(string className, bool exactMatch = false, int limit = 500, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "find_instances",
            ["class_name"] = className,
            ["exact_match"] = exactMatch,
            ["limit"] = limit
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new FindInstancesResult
        {
            Scanned = res["scanned"]?.GetValue<int>() ?? 0,
            NonNull = res["non_null"]?.GetValue<int>() ?? 0,
            Named = res["named"]?.GetValue<int>() ?? 0,
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
        };
    }

    public async Task<AddressLookupResult> FindByAddressAsync(string addr, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "find_by_address",
            ["addr"] = addr
        };
        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var found = res["found"]?.GetValue<bool>() ?? false;
        if (!found)
        {
            return new AddressLookupResult
            {
                Found = false,
                QueryAddress = addr,
            };
        }

        return new AddressLookupResult
        {
            Found = true,
            MatchType = res["match_type"]?.GetValue<string>() ?? "",
            Address = res["addr"]?.GetValue<string>() ?? "",
            Index = res["index"]?.GetValue<int>() ?? -1,
            Name = res["name"]?.GetValue<string>() ?? "",
            ClassName = res["class"]?.GetValue<string>() ?? "",
            OuterAddr = res["outer"]?.GetValue<string>() ?? "",
            OffsetFromBase = res["offset_from_base"]?.GetValue<int>() ?? 0,
            QueryAddress = res["query_addr"]?.GetValue<string>() ?? addr,
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
                        parms.Add(new FunctionParamModel
                        {
                            Name = po["name"]?.GetValue<string>() ?? "",
                            TypeName = po["type"]?.GetValue<string>() ?? "",
                            Size = po["size"]?.GetValue<int>() ?? 0,
                            IsOut = po["out"]?.GetValue<bool>() ?? false,
                            IsReturn = po["ret"]?.GetValue<bool>() ?? false,
                        });
                    }
                }

                result.Add(new FunctionInfoModel
                {
                    Name = fo["name"]?.GetValue<string>() ?? "",
                    FullName = fo["full"]?.GetValue<string>() ?? "",
                    Address = fo["addr"]?.GetValue<string>() ?? "",
                    FunctionFlags = fo["flags"]?.GetValue<uint>() ?? 0,
                    ReturnType = fo["ret"]?.GetValue<string>() ?? "",
                    Params = parms,
                });
            }
        }
        return result;
    }

    public async Task<PropertySearchResult> SearchPropertiesAsync(
        string query, string[]? types = null, bool gameOnly = true,
        int limit = 200, CancellationToken ct = default)
    {
        var req = new JsonObject
        {
            ["cmd"] = "search_properties",
            ["query"] = query,
            ["game_only"] = gameOnly,
            ["limit"] = limit
        };

        if (types is { Length: > 0 })
        {
            var arr = new JsonArray();
            foreach (var t in types) arr.Add(t);
            req["types"] = arr;
        }

        var res = await _pipe.SendAsync(req, ct);
        CheckResponse(res);

        var result = new PropertySearchResult
        {
            Total = res["total"]?.GetValue<int>() ?? 0,
            ScannedClasses = res["scanned_classes"]?.GetValue<int>() ?? 0,
            ScannedObjects = res["scanned_objects"]?.GetValue<int>() ?? 0,
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
                });
            }
        }

        return result;
    }

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
