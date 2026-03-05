using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Mock pipe client for testing DumpService.
/// </summary>
public sealed class MockPipeClient : IPipeClient
{
    public bool IsConnected { get; set; } = true;
    public event Action<bool>? ConnectionStateChanged;
    public event Action<JsonObject>? EventReceived;

    private Func<JsonObject, JsonObject>? _handler;

    public void SetHandler(Func<JsonObject, JsonObject> handler) => _handler = handler;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        ConnectionStateChanged?.Invoke(true);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        ConnectionStateChanged?.Invoke(false);
        return Task.CompletedTask;
    }

    public Task<JsonObject> SendAsync(JsonObject request, CancellationToken ct = default)
    {
        if (_handler != null)
            return Task.FromResult(_handler(request));

        return Task.FromResult(new JsonObject { ["ok"] = true });
    }

    public void SimulateEvent(JsonObject evt) => EventReceived?.Invoke(evt);

    public void Dispose() { }
}

/// <summary>
/// Mock logging service for testing.
/// </summary>
public sealed class MockLoggingService : ILoggingService
{
    public List<string> Messages { get; } = new();
    public void Info(string message) => Messages.Add($"[INFO] {message}");
    public void Warn(string message) => Messages.Add($"[WARN] {message}");
    public void Error(string message) => Messages.Add($"[ERROR] {message}");
    public void Error(string message, Exception ex) => Messages.Add($"[ERROR] {message}: {ex.Message}");
    public void Debug(string message) => Messages.Add($"[DEBUG] {message}");
    public void Info(string category, string message) => Messages.Add($"[INFO:{category}] {message}");
    public void Warn(string category, string message) => Messages.Add($"[WARN:{category}] {message}");
    public void Error(string category, string message) => Messages.Add($"[ERROR:{category}] {message}");
    public void Error(string category, string message, Exception ex) => Messages.Add($"[ERROR:{category}] {message}: {ex.Message}");
    public void Debug(string category, string message) => Messages.Add($"[DEBUG:{category}] {message}");
    public void StartProcessMirror(string processName) { }
    public void StopProcessMirror() { }
}

public class DumpServiceTests
{
    private readonly MockPipeClient _pipe = new();
    private readonly MockLoggingService _log = new();

    private DumpService CreateService() => new(_pipe, _log);

    [Fact]
    public async Task InitAsync_ParsesResponse()
    {
        int callCount = 0;
        _pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>();
            callCount++;
            if (cmd == "init")
                return new JsonObject { ["ok"] = true, ["ue_version"] = 504 };
            if (cmd == "get_pointers")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["gobjects"] = "0x7FF600A12340",
                    ["gnames"] = "0x7FF600B56780",
                    ["object_count"] = 58432
                };
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.InitAsync();

        Assert.Equal(504, state.UEVersion);
        Assert.True(state.VersionDetected);  // Default when field absent
        Assert.Equal("0x7FF600A12340", state.GObjectsAddr);
        Assert.Equal(58432, state.ObjectCount);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task InitAsync_ParsesVersionDetectedFalse()
    {
        _pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>();
            if (cmd == "init")
                return new JsonObject { ["ok"] = true, ["ue_version"] = 504, ["version_detected"] = false };
            if (cmd == "get_pointers")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["gobjects"] = "0x7FF600A12340",
                    ["gnames"] = "0x7FF600B56780",
                    ["object_count"] = 32759,
                    ["version_detected"] = false
                };
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.InitAsync();

        Assert.Equal(504, state.UEVersion);
        Assert.False(state.VersionDetected);
    }

    [Fact]
    public async Task GetObjectCountAsync_ReturnsCount()
    {
        _pipe.SetHandler(_ => new JsonObject { ["ok"] = true, ["count"] = 12345 });

        var svc = CreateService();
        var count = await svc.GetObjectCountAsync();

        Assert.Equal(12345, count);
    }

    [Fact]
    public async Task WalkClassAsync_ParsesFields()
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["class"] = new JsonObject
            {
                ["name"] = "BP_Player_C",
                ["full_path"] = "/Game/BP_Player.BP_Player_C",
                ["super_addr"] = "0x100",
                ["super_name"] = "Character",
                ["props_size"] = 1024,
                ["fields"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["addr"] = "0x200",
                        ["name"] = "Health",
                        ["type"] = "FloatProperty",
                        ["offset"] = 720,
                        ["size"] = 4
                    }
                }
            }
        });

        var svc = CreateService();
        var ci = await svc.WalkClassAsync("0x7FF000");

        Assert.Equal("BP_Player_C", ci.Name);
        Assert.Equal(1024, ci.PropertiesSize);
        Assert.Single(ci.Fields);
        Assert.Equal("Health", ci.Fields[0].Name);
        Assert.Equal(720, ci.Fields[0].Offset);
    }

    [Fact]
    public async Task ReadMemAsync_DecodesHex()
    {
        _pipe.SetHandler(_ => new JsonObject { ["ok"] = true, ["bytes"] = "48656C6C6F" });

        var svc = CreateService();
        var data = await svc.ReadMemAsync("0x100", 5);

        Assert.Equal(5, data.Length);
        Assert.Equal((byte)'H', data[0]);
        Assert.Equal((byte)'o', data[4]);
    }

    [Fact]
    public async Task ErrorResponse_ThrowsException()
    {
        _pipe.SetHandler(_ => new JsonObject { ["ok"] = false, ["error"] = "Object not found" });

        var svc = CreateService();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.FindObjectAsync("/Game/Missing"));

        Assert.Contains("Object not found", ex.Message);
    }

    [Fact]
    public async Task WalkInstanceAsync_ParsesInlineArrayElements()
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["addr"] = "0x100",
            ["name"] = "TestObj",
            ["class"] = "Actor",
            ["class_addr"] = "0x200",
            ["outer"] = "0x0",
            ["outer_name"] = "",
            ["outer_class"] = "",
            ["fields"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Multipliers",
                    ["type"] = "ArrayProperty",
                    ["offset"] = 256,
                    ["size"] = 16,
                    ["count"] = 3,
                    ["array_inner_type"] = "FloatProperty",
                    ["array_elem_size"] = 4,
                    ["array_inner_addr"] = "0x7FF601234560",
                    ["elements"] = new JsonArray
                    {
                        new JsonObject { ["i"] = 0, ["v"] = "1.5", ["h"] = "0000C03F" },
                        new JsonObject { ["i"] = 1, ["v"] = "2", ["h"] = "00000040" },
                        new JsonObject { ["i"] = 2, ["v"] = "0.5", ["h"] = "0000003F" },
                    }
                }
            }
        });

        var svc = CreateService();
        var result = await svc.WalkInstanceAsync("0x100");

        Assert.Single(result.Fields);
        var field = result.Fields[0];
        Assert.Equal("Multipliers", field.Name);
        Assert.Equal("ArrayProperty", field.TypeName);
        Assert.Equal(3, field.ArrayCount);
        Assert.Equal("FloatProperty", field.ArrayInnerType);
        Assert.Equal(4, field.ArrayElemSize);
        Assert.Equal("0x7FF601234560", field.ArrayInnerAddr);
        Assert.NotNull(field.ArrayElements);
        Assert.Equal(3, field.ArrayElements!.Count);
        Assert.Equal(0, field.ArrayElements[0].Index);
        Assert.Equal("1.5", field.ArrayElements[0].Value);
        Assert.Equal("0000C03F", field.ArrayElements[0].Hex);
        Assert.Equal("2", field.ArrayElements[1].Value);
    }

    [Fact]
    public async Task WalkInstanceAsync_ParsesEnumArrayElements()
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["addr"] = "0x100",
            ["name"] = "TestObj",
            ["class"] = "Actor",
            ["class_addr"] = "0x200",
            ["outer"] = "0x0",
            ["outer_name"] = "",
            ["outer_class"] = "",
            ["fields"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Roles",
                    ["type"] = "ArrayProperty",
                    ["offset"] = 300,
                    ["size"] = 16,
                    ["count"] = 2,
                    ["array_inner_type"] = "EnumProperty",
                    ["array_elem_size"] = 1,
                    ["elements"] = new JsonArray
                    {
                        new JsonObject { ["i"] = 0, ["v"] = "0", ["h"] = "00", ["en"] = "ROLE_Authority" },
                        new JsonObject { ["i"] = 1, ["v"] = "2", ["h"] = "02", ["en"] = "ROLE_SimulatedProxy" },
                    }
                }
            }
        });

        var svc = CreateService();
        var result = await svc.WalkInstanceAsync("0x100");

        var field = result.Fields[0];
        Assert.NotNull(field.ArrayElements);
        Assert.Equal("ROLE_Authority", field.ArrayElements![0].EnumName);
        Assert.Equal("ROLE_SimulatedProxy", field.ArrayElements[1].EnumName);
    }

    [Fact]
    public async Task WalkInstanceAsync_NoElements_ArrayElementsNull()
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["addr"] = "0x100",
            ["name"] = "TestObj",
            ["class"] = "Actor",
            ["class_addr"] = "0x200",
            ["outer"] = "0x0",
            ["outer_name"] = "",
            ["outer_class"] = "",
            ["fields"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "BigArray",
                    ["type"] = "ArrayProperty",
                    ["offset"] = 100,
                    ["size"] = 16,
                    ["count"] = 500,
                    ["array_inner_type"] = "IntProperty",
                    ["array_elem_size"] = 4,
                }
            }
        });

        var svc = CreateService();
        var result = await svc.WalkInstanceAsync("0x100");

        var field = result.Fields[0];
        Assert.Equal(500, field.ArrayCount);
        Assert.Null(field.ArrayElements);
    }

    // --- WalkFunctionsAsync: struct_fields parsing ---

    [Fact]
    public async Task WalkFunctionsAsync_ParsesStructFields()
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["count"] = 1,
            ["functions"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "SetAttribute",
                    ["full"] = "Function SetAttribute",
                    ["addr"] = "0x100",
                    ["flags"] = (uint)0,
                    ["num_parms"] = (byte)1,
                    ["parms_size"] = (ushort)8,
                    ["ret_offset"] = (ushort)0xFFFF,
                    ["ret"] = "",
                    ["params"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "NewValue",
                            ["type"] = "StructProperty",
                            ["size"] = 8,
                            ["offset"] = 0,
                            ["out"] = false,
                            ["ret"] = false,
                            ["struct_type"] = "GameplayAttributeData",
                            ["struct_fields"] = new JsonArray
                            {
                                new JsonObject { ["name"] = "BaseValue", ["type"] = "FloatProperty", ["offset"] = 0, ["size"] = 4 },
                                new JsonObject { ["name"] = "CurrentValue", ["type"] = "FloatProperty", ["offset"] = 4, ["size"] = 4 },
                            }
                        }
                    }
                }
            }
        });

        var svc = CreateService();
        var funcs = await svc.WalkFunctionsAsync("0x7FF000");

        Assert.Single(funcs);
        Assert.Single(funcs[0].Params);
        var param = funcs[0].Params[0];
        Assert.Equal("StructProperty", param.TypeName);
        Assert.Equal("GameplayAttributeData", param.StructName);
        Assert.Equal(2, param.StructFields.Count);
        Assert.Equal("BaseValue", param.StructFields[0].Name);
        Assert.Equal("FloatProperty", param.StructFields[0].TypeName);
        Assert.Equal(0, param.StructFields[0].Offset);
        Assert.Equal(4, param.StructFields[0].Size);
        Assert.Equal("CurrentValue", param.StructFields[1].Name);
        Assert.Equal(4, param.StructFields[1].Offset);
    }

    [Fact]
    public async Task WalkFunctionsAsync_MissingStructFields_EmptyList()
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["count"] = 1,
            ["functions"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "OldFunc",
                    ["full"] = "Function OldFunc",
                    ["addr"] = "0x100",
                    ["flags"] = (uint)0,
                    ["num_parms"] = (byte)1,
                    ["parms_size"] = (ushort)4,
                    ["ret_offset"] = (ushort)0xFFFF,
                    ["ret"] = "",
                    ["params"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "Amount",
                            ["type"] = "IntProperty",
                            ["size"] = 4,
                            ["offset"] = 0,
                            ["out"] = false,
                            ["ret"] = false,
                            // No struct_fields key — backward compat
                        }
                    }
                }
            }
        });

        var svc = CreateService();
        var funcs = await svc.WalkFunctionsAsync("0x7FF000");

        Assert.Single(funcs);
        var param = funcs[0].Params[0];
        Assert.Empty(param.StructFields);
    }

    [Fact]
    public async Task WalkFunctionsAsync_StructParamNoFields_EmptyList()
    {
        // StructProperty with struct_type but no struct_fields (DLL couldn't resolve)
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["count"] = 1,
            ["functions"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "DoThing",
                    ["full"] = "Function DoThing",
                    ["addr"] = "0x100",
                    ["flags"] = (uint)0,
                    ["num_parms"] = (byte)1,
                    ["parms_size"] = (ushort)16,
                    ["ret_offset"] = (ushort)0xFFFF,
                    ["ret"] = "",
                    ["params"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "Data",
                            ["type"] = "StructProperty",
                            ["size"] = 16,
                            ["offset"] = 0,
                            ["out"] = false,
                            ["ret"] = false,
                            ["struct_type"] = "CustomStruct",
                            // No struct_fields
                        }
                    }
                }
            }
        });

        var svc = CreateService();
        var funcs = await svc.WalkFunctionsAsync("0x7FF000");

        var param = funcs[0].Params[0];
        Assert.Equal("CustomStruct", param.StructName);
        Assert.Empty(param.StructFields);
    }

    [Fact]
    public async Task ReadArrayElementsAsync_ParsesResponse()
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["total"] = 128,
            ["read"] = 3,
            ["inner_type"] = "IntProperty",
            ["elem_size"] = 4,
            ["elements"] = new JsonArray
            {
                new JsonObject { ["i"] = 0, ["v"] = "42", ["h"] = "2A000000" },
                new JsonObject { ["i"] = 1, ["v"] = "99", ["h"] = "63000000" },
                new JsonObject { ["i"] = 2, ["v"] = "-1", ["h"] = "FFFFFFFF" },
            }
        });

        var svc = CreateService();
        var result = await svc.ReadArrayElementsAsync("0x100", 256, "0x200", "IntProperty", 4);

        Assert.Equal(128, result.TotalCount);
        Assert.Equal(3, result.ReadCount);
        Assert.Equal("IntProperty", result.InnerType);
        Assert.Equal(4, result.ElemSize);
        Assert.Equal(3, result.Elements.Count);
        Assert.Equal(42, int.Parse(result.Elements[0].Value));
        Assert.Equal("2A000000", result.Elements[0].Hex);
    }
}
