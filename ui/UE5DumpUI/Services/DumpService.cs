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

        return new EngineState
        {
            UEVersion = ueVersion,
            GObjectsAddr = ptrs["gobjects"]?.GetValue<string>() ?? "",
            GNamesAddr = ptrs["gnames"]?.GetValue<string>() ?? "",
            ObjectCount = ptrs["object_count"]?.GetValue<int>() ?? 0,
        };
    }

    public async Task<EngineState> GetPointersAsync(CancellationToken ct = default)
    {
        var res = await _pipe.SendAsync(new JsonObject { ["cmd"] = "get_pointers" }, ct);
        CheckResponse(res);

        return new EngineState
        {
            GObjectsAddr = res["gobjects"]?.GetValue<string>() ?? "",
            GNamesAddr = res["gnames"]?.GetValue<string>() ?? "",
            ObjectCount = res["object_count"]?.GetValue<int>() ?? 0,
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
