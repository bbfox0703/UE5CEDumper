using System.Text.Json.Nodes;
using UE5DumpUI.Core;
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
        Assert.Equal("0x7FF600A12340", state.GObjectsAddr);
        Assert.Equal(58432, state.ObjectCount);
        Assert.Equal(2, callCount);
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
}
