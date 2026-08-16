using System.Linq;
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
    public event Action<PipeLogEntry>? Activity { add { } remove { } }
    public event Action<bool>? GameThreadStalledChanged { add { } remove { } }

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
    // Thread-safe by necessity, not by taste. The code under test logs from several
    // threads at once (the capture's producer/consumer Task.Runs, the store cleanup),
    // and a bare List<string>.Add racing with itself corrupts the backing array or
    // throws IndexOutOfRangeException / ArgumentException from inside the logger. That
    // exception then propagates out of _log.Info(...) into the caller's catch — for
    // SnapshotViewModel.CaptureCoreAsync it lands in the handler that DELETES the
    // partial snapshot, which is indistinguishable from a real capture failure. A test
    // double must never be able to fail the thing it is observing.
    private readonly object _gate = new();
    private readonly List<string> _messages = new();

    /// <summary>A point-in-time copy — safe to enumerate while the subject keeps logging.</summary>
    public List<string> Messages { get { lock (_gate) return new List<string>(_messages); } }

    private void Add(string line) { lock (_gate) _messages.Add(line); }

    public void Info(string message) => Add($"[INFO] {message}");
    public void Warn(string message) => Add($"[WARN] {message}");
    public void Error(string message) => Add($"[ERROR] {message}");
    public void Error(string message, Exception ex) => Add($"[ERROR] {message}: {Describe(ex)}");
    public void Debug(string message) => Add($"[DEBUG] {message}");
    public void Info(string category, string message) => Add($"[INFO:{category}] {message}");
    public void Warn(string category, string message) => Add($"[WARN:{category}] {message}");
    public void Error(string category, string message) => Add($"[ERROR:{category}] {message}");
    public void Error(string category, string message, Exception ex) => Add($"[ERROR:{category}] {message}: {Describe(ex)}");
    public void Debug(string category, string message) => Add($"[DEBUG:{category}] {message}");
    public void StartProcessMirror(string processName) { }
    public void StopProcessMirror() { }

    // Type + message + a few frames. A bare ex.Message is enough for a SqliteException
    // ("database is locked") but useless for, say, a NullReferenceException — and a
    // CI-only failure gives you exactly one shot at the information.
    private static string Describe(Exception ex)
    {
        var frames = (ex.StackTrace ?? "")
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Take(3);
        var inner = ex.InnerException is { } ie ? $" <- {ie.GetType().Name}: {ie.Message}" : "";
        return $"{ex.GetType().Name}: {ex.Message}{inner} @ {string.Join(" / ", frames)}";
    }
}

public class DumpServiceTests
{
    private readonly MockPipeClient _pipe = new();
    private readonly MockLoggingService _log = new();

    private DumpService CreateService() => new(_pipe, _log);

    [Fact]
    public async Task InvokeFunctionAsync_StringParams_SerializedAsStrParamsArray()
    {
        JsonObject? lastReq = null;
        _pipe.SetHandler(req =>
        {
            if (req["cmd"]?.GetValue<string>() == "invoke_function")
                lastReq = req;
            return new JsonObject { ["ok"] = true, ["result"] = 0, ["parms_size"] = 16 };
        });

        var svc = CreateService();
        await svc.InvokeFunctionAsync(
            "SetPlayerName",
            instanceAddr: "0x1000",
            parmsSize: 16,
            paramsHex: "00000000000000000000000000000000",
            stringParams: new[]
            {
                new InvokeStringParam(0, Wide: true, "Hero"),
                new InvokeStringParam(16, Wide: false, "utf8"),
            },
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(lastReq);
        var arr = lastReq!["str_params"] as JsonArray;
        Assert.NotNull(arr);
        Assert.Equal(2, arr!.Count);

        var wide = arr[0]!.AsObject();
        Assert.Equal(0, wide["off"]!.GetValue<int>());
        Assert.True(wide["wide"]!.GetValue<bool>());
        Assert.Equal("Hero", wide["text"]!.GetValue<string>());

        var narrow = arr[1]!.AsObject();
        Assert.Equal(16, narrow["off"]!.GetValue<int>());
        Assert.False(narrow["wide"]!.GetValue<bool>());
        Assert.Equal("utf8", narrow["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task InvokeFunctionAsync_NoStringParams_OmitsStrParams()
    {
        JsonObject? lastReq = null;
        _pipe.SetHandler(req =>
        {
            if (req["cmd"]?.GetValue<string>() == "invoke_function")
                lastReq = req;
            return new JsonObject { ["ok"] = true, ["result"] = 0 };
        });

        var svc = CreateService();
        await svc.InvokeFunctionAsync("DoThing", instanceAddr: "0x1000",
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(lastReq);
        Assert.False(lastReq!.ContainsKey("str_params"));
    }

    [Fact]
    public void ParamBufferBuilder_ClassifiesStringTypes()
    {
        Assert.True(ParamBufferBuilder.IsStringType("StrProperty"));
        Assert.True(ParamBufferBuilder.IsStringType("Utf8StrProperty"));
        Assert.True(ParamBufferBuilder.IsStringType("AnsiStrProperty"));
        Assert.False(ParamBufferBuilder.IsStringType("IntProperty"));

        Assert.True(ParamBufferBuilder.IsWideString("StrProperty"));
        Assert.False(ParamBufferBuilder.IsWideString("Utf8StrProperty"));
        Assert.False(ParamBufferBuilder.IsWideString("AnsiStrProperty"));
    }

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
        var state = await svc.InitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(504, state.UEVersion);
        Assert.True(state.VersionDetected);  // Default when field absent
        Assert.Equal("0x7FF600A12340", state.GObjectsAddr);
        Assert.Equal(58432, state.ObjectCount);
        // init + get_pointers + get_offsets. The third is new in the X3 fix (audit #5):
        // the DLL's offset-validation verdict had been published and never fetched, so
        // nothing could tell the user the walker was running on unmeasured defaults.
        // Kept as an exact count deliberately — this assertion is what would catch an
        // accidental extra round-trip being added to the connect path.
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task InitAsync_ParsesUserOverrideAndLowConfidence()
    {
        _pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>();
            if (cmd == "init")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["ue_version"] = 427,
                    ["version_detected"] = true,
                    ["is_user_override"] = true,
                    ["is_low_confidence"] = false,
                    ["publisher_thumbprint"] = "SQUARE_ENIX",
                };
            if (cmd == "get_pointers")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["gobjects"] = "0x7FF600A12340",
                    ["gnames"] = "0x7FF600B56780",
                    ["object_count"] = 1024,
                    ["ue_version"] = 427,
                    ["is_user_override"] = true,
                    ["publisher_thumbprint"] = "SQUARE_ENIX",
                };
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.InitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(427, state.UEVersion);
        Assert.True(state.IsUserOverride);
        Assert.False(state.IsLowConfidence);
        Assert.Equal("SQUARE_ENIX", state.PublisherThumbprint);
    }

    [Fact]
    public async Task InitAsync_ParsesLowConfidenceFlag()
    {
        _pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>();
            if (cmd == "init")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["ue_version"] = 504,
                    ["version_detected"] = true,
                    ["is_user_override"] = false,
                    ["is_low_confidence"] = true,
                };
            if (cmd == "get_pointers")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["gobjects"] = "0x1",
                    ["gnames"] = "0x2",
                    ["object_count"] = 0,
                };
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.InitAsync(TestContext.Current.CancellationToken);

        Assert.True(state.IsLowConfidence);
        Assert.False(state.IsUserOverride);
    }

    [Fact]
    public async Task GetPointersAsync_CarriesPreUE4SentinelAndRefusalFlag()
    {
        // A pre-UE4 (UE3) refusal reaches the UI as ue_version = 300 (the sentinel) plus
        // is_version_too_old = true. Both must survive the get_pointers-only path, because the
        // sentinel is the ONLY thing that tells PointerPanelViewModel which of the two refusal
        // banners to show — there is deliberately no extra pipe field for it.
        _pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>();
            if (cmd == "get_pointers")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["ue_version"] = 300,
                    ["is_version_too_old"] = true,
                    ["gobjects"] = "0x0",
                    ["gnames"] = "0x0",
                    ["object_count"] = 0,
                };
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.GetPointersAsync(TestContext.Current.CancellationToken);

        Assert.Equal(300, state.UEVersion);
        Assert.True(state.IsVersionTooOld);
    }

    [Fact]
    public async Task GetPointersAsync_OmittedRefusalFlagReadsAsNotGated()
    {
        // An older DLL omits is_version_too_old entirely; "not gated" is the right default.
        _pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>();
            if (cmd == "get_pointers")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["ue_version"] = 504,
                    ["gobjects"] = "0x1",
                    ["gnames"] = "0x2",
                };
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.GetPointersAsync(TestContext.Current.CancellationToken);

        Assert.False(state.IsVersionTooOld);
    }

    [Fact]
    public async Task SetUeVersionOverrideAsync_SendsCorrectPayloadAndRefetches()
    {
        JsonObject? lastOverrideReq = null;
        int getPointersCount = 0;
        _pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>();
            if (cmd == "set_ue_version_override")
            {
                lastOverrideReq = req;
                return new JsonObject
                {
                    ["ok"] = true,
                    ["ue_version"] = 427,
                    ["is_user_override"] = true,
                    ["persisted"] = true,
                };
            }
            if (cmd == "get_pointers")
            {
                getPointersCount++;
                return new JsonObject
                {
                    ["ok"] = true,
                    ["gobjects"] = "0x10",
                    ["gnames"] = "0x20",
                    ["object_count"] = 5,
                    ["ue_version"] = 427,
                    ["is_user_override"] = true,
                };
            }
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.SetUeVersionOverrideAsync(427, persist: true, TestContext.Current.CancellationToken);

        Assert.NotNull(lastOverrideReq);
        Assert.Equal(427, lastOverrideReq!["version"]?.GetValue<int>());
        Assert.True(lastOverrideReq["persist"]?.GetValue<bool>());
        Assert.Equal(427, state.UEVersion);
        Assert.True(state.IsUserOverride);
        Assert.Equal(1, getPointersCount);   // SetUeVersionOverride re-fetches state once
    }

    [Fact]
    public async Task SetUeVersionOverrideAsync_ZeroClearsOverride()
    {
        JsonObject? lastReq = null;
        _pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>();
            if (cmd == "set_ue_version_override")
            {
                lastReq = req;
                return new JsonObject { ["ok"] = true, ["is_user_override"] = false };
            }
            if (cmd == "get_pointers")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["gobjects"] = "0x10",
                    ["gnames"] = "0x20",
                    ["object_count"] = 0,
                    ["is_user_override"] = false,
                };
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.SetUeVersionOverrideAsync(0, persist: true, TestContext.Current.CancellationToken);

        Assert.Equal(0, lastReq!["version"]?.GetValue<int>());
        Assert.False(state.IsUserOverride);
    }

    [Fact]
    public async Task SetInvokeTimeoutAsync_SendsCorrectPayloadAndRefetches()
    {
        // Reproduces the Meltopia case from the old logs: 4x GameThreadDispatch invoke
        // timeout (5s) errors fired on Blueprint widget delegates. UI raises the timeout
        // to 15s for that game; the value must round-trip and surface in the next state.
        JsonObject? lastReq = null;
        int getPointersCount = 0;
        _pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>();
            if (cmd == "set_invoke_timeout")
            {
                lastReq = req;
                return new JsonObject
                {
                    ["ok"] = true,
                    ["invoke_timeout_ms"] = 15000,
                    ["persisted"] = true,
                };
            }
            if (cmd == "get_pointers")
            {
                getPointersCount++;
                return new JsonObject
                {
                    ["ok"] = true,
                    ["gobjects"] = "0x10",
                    ["gnames"] = "0x20",
                    ["object_count"] = 5,
                    ["invoke_timeout_ms"] = 15000,
                };
            }
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.SetInvokeTimeoutAsync(15000, persist: true, TestContext.Current.CancellationToken);

        Assert.NotNull(lastReq);
        Assert.Equal(15000, lastReq!["timeout_ms"]?.GetValue<int>());
        Assert.True(lastReq["persist"]?.GetValue<bool>());
        Assert.Equal(15000, state.InvokeTimeoutMs);
        Assert.Equal(1, getPointersCount);   // SetInvokeTimeout re-fetches state once
    }

    [Fact]
    public async Task SetInvokeTimeoutAsync_ZeroClearsOverride()
    {
        // 0 → DLL clears the override and reverts to Stark::kDefaultInvokeTimeoutMs (5000).
        JsonObject? lastReq = null;
        _pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>();
            if (cmd == "set_invoke_timeout")
            {
                lastReq = req;
                return new JsonObject { ["ok"] = true, ["invoke_timeout_ms"] = 5000 };
            }
            if (cmd == "get_pointers")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["gobjects"] = "0x10",
                    ["gnames"] = "0x20",
                    ["object_count"] = 0,
                    ["invoke_timeout_ms"] = 5000,
                };
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.SetInvokeTimeoutAsync(0, persist: true, TestContext.Current.CancellationToken);

        Assert.Equal(0, lastReq!["timeout_ms"]?.GetValue<int>());
        Assert.Equal(5000, state.InvokeTimeoutMs);
    }

    [Fact]
    public async Task GetPointersAsync_DefaultsInvokeTimeoutWhenAbsent()
    {
        // Old DLL builds (or response paths) won't include invoke_timeout_ms.
        // EngineState should fall back to 5000 (the Stark default), not 0.
        _pipe.SetHandler(req =>
        {
            if (req["cmd"]?.GetValue<string>() == "get_pointers")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["gobjects"] = "0x10",
                    ["gnames"] = "0x20",
                    ["object_count"] = 0,
                };
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.GetPointersAsync(TestContext.Current.CancellationToken);
        Assert.Equal(5000, state.InvokeTimeoutMs);
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
        var state = await svc.InitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(504, state.UEVersion);
        Assert.False(state.VersionDetected);
    }

    // Regression: BuildEngineState took `bool versionDetected = true` — a non-nullable default
    // with no "absent" sentinel — and never read ptrs["version_detected"]. InitAsync passed the
    // real value, GetPointersAsync did not, so the honest
    // "⚠ Version not detected — inferred from engine analysis (custom UE build?)" badge
    // (PointerPanelViewModel.ShowVersionWarning) showed after connect and then VANISHED on the
    // next pointer refresh. The DLL puts the field on every snapshot (Fern.cpp
    // FillPointerSnapshot) — the UI has to read it.
    [Fact]
    public async Task GetPointersAsync_ParsesVersionDetectedFalse()
    {
        _pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>();
            if (cmd == "get_pointers")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["gobjects"] = "0x7FF600A12340",
                    ["gnames"] = "0x7FF600B56780",
                    ["object_count"] = 32759,
                    ["ue_version"] = 504,
                    ["version_detected"] = false,
                    ["is_low_confidence"] = true,
                };
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.GetPointersAsync(TestContext.Current.CancellationToken);

        Assert.Equal(504, state.UEVersion);
        Assert.False(state.VersionDetected);
        // is_low_confidence was already read from the wire (bool? param) — pin it so the two
        // flags can't diverge again.
        Assert.True(state.IsLowConfidence);
    }

    [Fact]
    public async Task GetPointersAsync_VersionDetectedDefaultsTrueWhenFieldAbsent()
    {
        // A DLL that predates the flag omits it → "detected", i.e. no warning badge.
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["gobjects"] = "0x10",
            ["gnames"] = "0x20",
            ["object_count"] = 1,
        });

        var svc = CreateService();
        var state = await svc.GetPointersAsync(TestContext.Current.CancellationToken);

        Assert.True(state.VersionDetected);
        Assert.False(state.IsLowConfidence);
    }

    // The user-visible symptom: any refresh routed through GetPointersAsync (invoke-timeout
    // change, UE version override) used to reset VersionDetected to the `= true` default.
    // Models The Adventures of Elliot: ueVersion=504, versionDetected=false, lowConfidence=true.
    [Fact]
    public async Task SetInvokeTimeoutAsync_PreservesVersionDetectedFalse()
    {
        _pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>();
            if (cmd == "set_invoke_timeout")
                return new JsonObject { ["ok"] = true, ["invoke_timeout_ms"] = 15000, ["persisted"] = true };
            if (cmd == "get_pointers")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["gobjects"] = "0x10",
                    ["gnames"] = "0x20",
                    ["object_count"] = 5,
                    ["ue_version"] = 504,
                    ["version_detected"] = false,
                    ["is_low_confidence"] = true,
                    ["is_user_override"] = false,
                    ["invoke_timeout_ms"] = 15000,
                };
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.SetInvokeTimeoutAsync(15000, persist: true, TestContext.Current.CancellationToken);

        Assert.Equal(15000, state.InvokeTimeoutMs);
        Assert.False(state.VersionDetected);   // warning badge must survive the refresh
        Assert.True(state.IsLowConfidence);
        Assert.False(state.IsUserOverride);
    }

    [Fact]
    public async Task GetScanStatusAsync_CompletionParsesVersionDetectedFalse()
    {
        // scan_status completion carries the same FillPointerSnapshot payload as get_pointers.
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["running"] = false,
            ["phase"] = 3,
            ["status_text"] = "Complete",
            ["scanned"] = true,
            ["gobjects"] = "0x10",
            ["gnames"] = "0x20",
            ["object_count"] = 7,
            ["ue_version"] = 504,
            ["version_detected"] = false,
        });

        var svc = CreateService();
        var status = await svc.GetScanStatusAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(status.EngineState);
        Assert.Equal(504, status.EngineState!.UEVersion);
        Assert.False(status.EngineState.VersionDetected);
    }

    [Fact]
    public async Task GetObjectCountAsync_ReturnsCount()
    {
        _pipe.SetHandler(_ => new JsonObject { ["ok"] = true, ["count"] = 12345 });

        var svc = CreateService();
        var count = await svc.GetObjectCountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(12345, count);
    }

    [Fact]
    public async Task GetRelatedObjectsAsync_ParsesGraphAndPreservesOrder()
    {
        _pipe.SetHandler(req =>
        {
            Assert.Equal("get_related_objects", req["cmd"]?.GetValue<string>());
            Assert.Equal("0x7FF6AA00", req["addr"]?.GetValue<string>());
            return new JsonObject
            {
                ["ok"] = true,
                ["query_addr"] = "0x7FF6AA00",
                ["related"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["addr"] = "0x7FF6AA00", ["index"] = 100,
                        ["name"] = "BP_Enemy_C_0", ["class"] = "BP_Enemy_C",
                        ["relation"] = "Self", ["field_name"] = "",
                        ["field_offset"] = -1, ["depth"] = 0, ["parent_addr"] = "0x0",
                    },
                    new JsonObject
                    {
                        ["addr"] = "0x7FF6BB00", ["index"] = 200,
                        ["name"] = "AbilitySystem_0", ["class"] = "AbilitySystemComponent",
                        ["relation"] = "AbilitySystem (ASC)", ["field_name"] = "AbilitySystem",
                        ["field_offset"] = 0x2A8, ["depth"] = 1, ["parent_addr"] = "0x7FF6AA00",
                    },
                    new JsonObject
                    {
                        ["addr"] = "0x7FF6CC00", ["index"] = 300,
                        ["name"] = "HealthSet_0", ["class"] = "MyHealthAttributeSet",
                        ["relation"] = "AttributeSet", ["field_name"] = "SpawnedAttributes[0]",
                        ["field_offset"] = 0x1B0, ["depth"] = 2, ["parent_addr"] = "0x7FF6BB00",
                    },
                },
            };
        });

        var svc = CreateService();
        var result = await svc.GetRelatedObjectsAsync("0x7FF6AA00", ct: TestContext.Current.CancellationToken);

        Assert.Equal("0x7FF6AA00", result.QueryAddress);
        Assert.Equal(3, result.Related.Count);

        var self = result.Related[0];
        Assert.Equal("Self", self.Relation);
        Assert.Equal("BP_Enemy_C", self.ClassName);

        var asc = result.Related[1];
        Assert.Equal("AbilitySystem (ASC)", asc.Relation);
        Assert.Equal(0x2A8, asc.FieldOffset);
        Assert.Equal(1, asc.Depth);
        Assert.Equal("0x7FF6AA00", asc.ParentAddress);

        var attr = result.Related[2];
        Assert.Equal("AttributeSet", attr.Relation);
        Assert.Equal(2, attr.Depth);
        Assert.Equal("SpawnedAttributes[0]", attr.FieldName);
        Assert.Contains("0x1B0", attr.FieldDisplay);   // offset hint surfaced
    }

    [Fact]
    public async Task DetectCurrentTargetAsync_ParsesChainAndPreservesCandidateOrder()
    {
        _pipe.SetHandler(req =>
        {
            Assert.Equal("get_current_target", req["cmd"]?.GetValue<string>());
            Assert.Equal(8, req["max_candidates"]?.GetValue<int>());
            return new JsonObject
            {
                ["ok"] = true,
                ["resolved"] = true,
                ["world"] = "0x1AD0000",
                ["player_controller"] = "0x7FF6AA00",
                ["player_pawn"] = "0x7FF6BB00",
                ["note"] = "Detected target: Enemy_0 (BP_Enemy_C) — score 95 via CurrentTarget.",
                ["candidates"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["addr"] = "0x7FF6CC00", ["index"] = 500,
                        ["name"] = "Enemy_0", ["class"] = "BP_Enemy_C",
                        ["score"] = 95, ["source_addr"] = "0x7FF6AA00",
                        ["source_class"] = "BP_PlayerController_C",
                        ["field_name"] = "CurrentTarget", ["field_offset"] = 0x3C0,
                        ["reason"] = "field 'CurrentTarget', is-Pawn",
                    },
                    new JsonObject
                    {
                        ["addr"] = "0x7FF6DD00", ["index"] = 600,
                        ["name"] = "Ally_0", ["class"] = "BP_Ally_C",
                        ["score"] = 45, ["source_addr"] = "0x7FF6BB00",
                        ["source_class"] = "BP_Pawn_C",
                        ["field_name"] = "FocusActor", ["field_offset"] = -1,
                        ["reason"] = "field 'FocusActor', is-Pawn",
                    },
                },
            };
        });

        var svc = CreateService();
        var result = await svc.DetectCurrentTargetAsync(ct: TestContext.Current.CancellationToken);

        Assert.True(result.Resolved);
        Assert.Equal("0x7FF6BB00", result.PlayerPawn);
        Assert.StartsWith("Detected target", result.Note);
        Assert.Equal(2, result.Candidates.Count);

        var top = result.Candidates[0];   // order preserved (server ranks best-first)
        Assert.Equal("Enemy_0", top.Name);
        Assert.Equal(95, top.Score);
        Assert.Equal("CurrentTarget", top.FieldName);
        Assert.Equal(0x3C0, top.FieldOffset);
        Assert.Contains("CurrentTarget", top.ScoreDisplay);

        var second = result.Candidates[1];
        Assert.Equal("Ally_0", second.Name);
        Assert.Equal(-1, second.FieldOffset);   // container/path edge tolerated
    }

    [Fact]
    public async Task ResolveGameEngineAsync_ParsesFoundEngine()
    {
        _pipe.SetHandler(req =>
        {
            Assert.Equal("resolve_game_engine", req["cmd"]?.GetValue<string>());
            return new JsonObject
            {
                ["ok"] = true,
                ["found"] = true,
                ["addr"] = "0x1AD12340",
                ["class"] = "GameEngine",
                ["game_viewport_ok"] = true,
                ["game_instance_ok"] = true,
            };
        });

        var svc = CreateService();
        var result = await svc.ResolveGameEngineAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Found);
        Assert.Equal("0x1AD12340", result.Address);
        Assert.Equal("GameEngine", result.ClassName);
        Assert.True(result.GameViewportOk);
        Assert.True(result.GameInstanceOk);
    }

    [Fact]
    public async Task ResolveGameEngineAsync_ParsesNotFound()
    {
        _pipe.SetHandler(req =>
        {
            Assert.Equal("resolve_game_engine", req["cmd"]?.GetValue<string>());
            return new JsonObject { ["ok"] = true, ["found"] = false };
        });

        var svc = CreateService();
        var result = await svc.ResolveGameEngineAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Found);
        Assert.Equal("", result.Address);
        Assert.False(result.GameInstanceOk);
    }

    [Fact]
    public async Task FindInstancesAsync_SendsNewestFirstFlag()
    {
        bool? sentNewestFirst = null;
        _pipe.SetHandler(req =>
        {
            sentNewestFirst = req["newest_first"]?.GetValue<bool>();
            return new JsonObject { ["ok"] = true, ["instances"] = new JsonArray() };
        });

        var svc = CreateService();
        await svc.FindInstancesAsync("BP_Enemy_C", newestFirst: true, ct: TestContext.Current.CancellationToken);

        Assert.True(sentNewestFirst);   // default path (false) is covered by every other find_instances test
    }

    [Fact]
    public async Task FindInstancesAsync_SendsClassAndNameFilter()
    {
        string? sentClass = null;
        string? sentName = null;
        _pipe.SetHandler(req =>
        {
            sentClass = req["class_name"]?.GetValue<string>();
            sentName = req["name_filter"]?.GetValue<string>();
            return new JsonObject { ["ok"] = true, ["instances"] = new JsonArray() };
        });

        var svc = CreateService();
        await svc.FindInstancesAsync("WB_HUD_MagicStone_C", nameFilter: "MagicStone",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal("WB_HUD_MagicStone_C", sentClass);
        Assert.Equal("MagicStone", sentName);
    }

    [Fact]
    public async Task FindInstancesAsync_NameOnly_SendsEmptyClass()
    {
        string? sentClass = null;
        string? sentName = null;
        _pipe.SetHandler(req =>
        {
            sentClass = req["class_name"]?.GetValue<string>();
            sentName = req["name_filter"]?.GetValue<string>();
            return new JsonObject { ["ok"] = true, ["instances"] = new JsonArray() };
        });

        var svc = CreateService();
        await svc.FindInstancesAsync("", nameFilter: "MagicStone",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal("", sentClass);
        Assert.Equal("MagicStone", sentName);
    }

    [Fact]
    public async Task FindInstancesAsync_ParsesTruncatedFlag()
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["truncated"] = true,
            ["instances"] = new JsonArray(),
        });

        var svc = CreateService();
        var result = await svc.FindInstancesAsync("Component",
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task FindInstancesAsync_TruncatedDefaultsFalse()
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["instances"] = new JsonArray(),
        });

        var svc = CreateService();
        var result = await svc.FindInstancesAsync("PlayerController",
            ct: TestContext.Current.CancellationToken);

        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task FindInstancesAsync_SendsExcludeClassesAndLimit()
    {
        JsonNode? sentExclude = null;
        int? sentLimit = null;
        _pipe.SetHandler(req =>
        {
            sentExclude = req["exclude_classes"];
            sentLimit = req["limit"]?.GetValue<int>();
            return new JsonObject { ["ok"] = true, ["instances"] = new JsonArray() };
        });

        var svc = CreateService();
        await svc.FindInstancesAsync("Actor", limit: 5000,
            excludeClasses: new[] { "WidgetTree", "SoundCue" },
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(5000, sentLimit);
        var arr = Assert.IsType<JsonArray>(sentExclude);
        Assert.Equal(new[] { "WidgetTree", "SoundCue" },
            arr.Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public async Task FindInstancesAsync_OmitsExcludeClasses_WhenEmpty()
    {
        bool hasKey = true;
        _pipe.SetHandler(req =>
        {
            // Absent (not just null) so the common no-exclude page stays byte-identical.
            hasKey = req.AsObject().ContainsKey("exclude_classes");
            return new JsonObject { ["ok"] = true, ["instances"] = new JsonArray() };
        });

        var svc = CreateService();
        await svc.FindInstancesAsync("PlayerController",
            excludeClasses: System.Array.Empty<string>(),
            ct: TestContext.Current.CancellationToken);

        Assert.False(hasKey);
    }

    [Fact]
    public async Task FindInstancesAsync_ParsesClassHistogramAndDistinct()
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["instances"] = new JsonArray(),
            ["class_distinct"] = 137,
            ["class_histogram"] = new JsonArray
            {
                new JsonObject { ["class_name"] = "BP_Enemy_C", ["count"] = 42 },
                new JsonObject { ["class_name"] = "WidgetTree", ["count"] = 9 },
            },
        });

        var svc = CreateService();
        var result = await svc.FindInstancesAsync("Actor",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(137, result.ClassDistinct);
        Assert.Equal(2, result.ClassHistogram.Count);
        Assert.Equal("BP_Enemy_C", result.ClassHistogram[0].ClassName);
        Assert.Equal(42, result.ClassHistogram[0].Count);
        Assert.Equal("WidgetTree", result.ClassHistogram[1].ClassName);
        Assert.Equal(9, result.ClassHistogram[1].Count);
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
        var ci = await svc.WalkClassAsync("0x7FF000", TestContext.Current.CancellationToken);

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
        var data = await svc.ReadMemAsync("0x100", 5, TestContext.Current.CancellationToken);

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
            () => svc.FindObjectAsync("/Game/Missing", TestContext.Current.CancellationToken));

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
        var result = await svc.WalkInstanceAsync("0x100", ct: TestContext.Current.CancellationToken);

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
        var result = await svc.WalkInstanceAsync("0x100", ct: TestContext.Current.CancellationToken);

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
        var result = await svc.WalkInstanceAsync("0x100", ct: TestContext.Current.CancellationToken);

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
        var funcs = await svc.WalkFunctionsAsync("0x7FF000", TestContext.Current.CancellationToken);

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
        var funcs = await svc.WalkFunctionsAsync("0x7FF000", TestContext.Current.CancellationToken);

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
        var funcs = await svc.WalkFunctionsAsync("0x7FF000", TestContext.Current.CancellationToken);

        var param = funcs[0].Params[0];
        Assert.Equal("CustomStruct", param.StructName);
        Assert.Empty(param.StructFields);
    }

    // --- WalkInstanceAsync: definition object (ScriptStruct/Class) ---

    [Fact]
    public async Task WalkInstanceAsync_DefinitionObject_ParsesFieldsCorrectly()
    {
        // Simulates walk_instance response when instanceAddr is a UScriptStruct definition.
        // DLL detects className="ScriptStruct" and returns field definitions via WalkClassEx.
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["addr"] = "0x7FF12345",
            ["name"] = "JackDataTableCoinShop",
            ["class"] = "ScriptStruct",
            ["class_addr"] = "0x7FF00100",
            ["outer"] = "0x7FF00200",
            ["outer_name"] = "CoinShopPackage",
            ["outer_class"] = "Package",
            ["is_definition"] = true,
            ["fields"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "ItemID",
                    ["type"] = "IntProperty",
                    ["offset"] = 0,
                    ["size"] = 4,
                    // No hex or value — definition only
                },
                new JsonObject
                {
                    ["name"] = "Price",
                    ["type"] = "FloatProperty",
                    ["offset"] = 4,
                    ["size"] = 4,
                    ["value"] = "",
                },
                new JsonObject
                {
                    ["name"] = "ItemClass",
                    ["type"] = "ObjectProperty",
                    ["offset"] = 8,
                    ["size"] = 8,
                    ["value"] = "\u2192 ItemBase",
                },
            }
        });

        var svc = CreateService();
        var result = await svc.WalkInstanceAsync("0x7FF12345", ct: TestContext.Current.CancellationToken);

        Assert.Equal("JackDataTableCoinShop", result.Name);
        Assert.Equal("ScriptStruct", result.ClassName);
        Assert.True(result.IsDefinition);
        Assert.Equal(3, result.Fields.Count);

        Assert.Equal("ItemID", result.Fields[0].Name);
        Assert.Equal("IntProperty", result.Fields[0].TypeName);
        Assert.Equal(0, result.Fields[0].Offset);
        Assert.Equal(4, result.Fields[0].Size);

        Assert.Equal("Price", result.Fields[1].Name);
        Assert.Equal("FloatProperty", result.Fields[1].TypeName);
        Assert.Equal(4, result.Fields[1].Offset);

        Assert.Equal("ItemClass", result.Fields[2].Name);
        Assert.Equal("ObjectProperty", result.Fields[2].TypeName);
        Assert.Equal("\u2192 ItemBase", result.Fields[2].TypedValue);
    }

    [Fact]
    public async Task WalkInstanceAsync_DefinitionObject_EmptyFields()
    {
        // UScriptStruct with no properties → empty field list
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["addr"] = "0x7FF12345",
            ["name"] = "EmptyStruct",
            ["class"] = "ScriptStruct",
            ["class_addr"] = "0x7FF00100",
            ["outer"] = "0x0",
            ["outer_name"] = "",
            ["outer_class"] = "",
            ["fields"] = new JsonArray()
        });

        var svc = CreateService();
        var result = await svc.WalkInstanceAsync("0x7FF12345", ct: TestContext.Current.CancellationToken);

        Assert.Equal("EmptyStruct", result.Name);
        Assert.Equal("ScriptStruct", result.ClassName);
        // Backward compat: no is_definition key → false
        Assert.False(result.IsDefinition);
        Assert.Empty(result.Fields);
    }

    // --- WalkInstanceAsync: Guess What (fill_gaps / guessed flag) ---

    [Fact]
    public async Task WalkInstanceAsync_ParsesGuessedFlag()
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["addr"] = "0x100",
            ["name"] = "TestObj",
            ["class"] = "TestClass",
            ["fields"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Health",
                    ["type"] = "FloatProperty",
                    ["offset"] = 0x90,
                    ["size"] = 4,
                    ["value"] = "100",
                    ["hex"] = "0000C842",
                },
                new JsonObject
                {
                    ["name"] = "?0xC0_i32",
                    ["type"] = "Int32?",
                    ["offset"] = 0xC0,
                    ["size"] = 4,
                    ["value"] = "171",
                    ["hex"] = "AB000000",
                    ["guessed"] = true,
                },
            }
        });

        var svc = CreateService();
        var result = await svc.WalkInstanceAsync("0x100", ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Fields.Count);
        Assert.False(result.Fields[0].IsGuessed);
        Assert.True(result.Fields[1].IsGuessed);
        Assert.Equal("?0xC0_i32", result.Fields[1].Name);
        Assert.Equal("Int32?", result.Fields[1].TypeName);
    }

    [Fact]
    public async Task WalkInstanceAsync_FillGapsParam_SentWhenTrue()
    {
        JsonObject? capturedRequest = null;
        _pipe.SetHandler(req =>
        {
            capturedRequest = req;
            return new JsonObject
            {
                ["ok"] = true,
                ["addr"] = "0x100",
                ["name"] = "TestObj",
                ["class"] = "TestClass",
                ["fields"] = new JsonArray()
            };
        });

        var svc = CreateService();
        await svc.WalkInstanceAsync("0x100", fillGaps: true, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest!["fill_gaps"]?.GetValue<bool>());
    }

    [Fact]
    public async Task WalkInstanceAsync_FillGapsParam_NotSentWhenFalse()
    {
        JsonObject? capturedRequest = null;
        _pipe.SetHandler(req =>
        {
            capturedRequest = req;
            return new JsonObject
            {
                ["ok"] = true,
                ["addr"] = "0x100",
                ["name"] = "TestObj",
                ["class"] = "TestClass",
                ["fields"] = new JsonArray()
            };
        });

        var svc = CreateService();
        await svc.WalkInstanceAsync("0x100", fillGaps: false, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(capturedRequest);
        Assert.Null(capturedRequest!["fill_gaps"]);
    }

    [Fact]
    public void GuessedField_IsNotEditable()
    {
        var field = new LiveFieldValue
        {
            Name = "?0xC0_i32",
            TypeName = "Int32?",
            Offset = 0xC0,
            Size = 4,
            TypedValue = "171",
            HexValue = "AB000000",
            IsGuessed = true,
        };
        field.FieldAddress = "0x1000C0";

        Assert.False(field.IsEditable);
    }

    [Fact]
    public void GuessedField_IsNotNavigable()
    {
        var field = new LiveFieldValue
        {
            Name = "?0xC0_ptr",
            TypeName = "Pointer?",
            Offset = 0xC0,
            Size = 8,
            TypedValue = "0x7FF12345",
            HexValue = "4523F17F00000000",
            PtrAddress = "0x7FF12345",
            IsGuessed = true,
        };

        Assert.False(field.IsNavigable);
        Assert.False(field.IsContainerNavigable);
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
        var result = await svc.ReadArrayElementsAsync("0x100", 256, "0x200", "IntProperty", 4, ct: TestContext.Current.CancellationToken);

        Assert.Equal(128, result.TotalCount);
        Assert.Equal(3, result.ReadCount);
        Assert.Equal("IntProperty", result.InnerType);
        Assert.Equal(4, result.ElemSize);
        Assert.Equal(3, result.Elements.Count);
        Assert.Equal(42, int.Parse(result.Elements[0].Value));
        Assert.Equal("2A000000", result.Elements[0].Hex);
    }

    // --- WalkFunctionPropsAsync: Path 1 (bytecode) vs Path 2 (native disasm) ---

    [Fact]
    public async Task WalkFunctionPropsAsync_ParsesBytecodeMethod()
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["query_addr"] = "0x100",
            ["script_bytes"] = 12544,
            ["method"] = "bytecode",
            ["unmapped"] = 0,
            ["props"] = new JsonArray
            {
                new JsonObject
                {
                    ["prop_addr"] = "0x200",
                    ["name"] = "CurrentHealth",
                    ["type"] = "FloatProperty",
                    ["occurrences"] = 5,
                    ["write_count"] = 2,
                    ["scope"] = "instance",
                    // bytecode rows carry no offset/confidence
                },
            }
        });

        var svc = CreateService();
        var res = await svc.WalkFunctionPropsAsync("0x100", TestContext.Current.CancellationToken);

        Assert.Equal("bytecode", res.Method);
        Assert.False(res.IsDisasm);
        Assert.Equal(12544, res.ScriptBytes);
        Assert.Single(res.Props);
        Assert.Equal("CurrentHealth", res.Props[0].Name);
        Assert.Equal(-1, res.Props[0].Offset);            // absent → -1
        Assert.Equal("", res.Props[0].Confidence);        // absent → ""
        Assert.True(res.Props[0].IsClassField);
    }

    [Fact]
    public async Task WalkFunctionPropsAsync_ParsesNativeDisasmMethod()
    {
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["query_addr"] = "0x100",
            ["script_bytes"] = 0,                  // native — no bytecode
            ["method"] = "disasm",
            ["unmapped"] = 3,
            ["props"] = new JsonArray
            {
                new JsonObject
                {
                    ["prop_addr"] = "0x200",
                    ["name"] = "MaxHealth",
                    ["type"] = "FloatProperty",
                    ["occurrences"] = 2,
                    ["write_count"] = 1,
                    ["scope"] = "instance",
                    ["offset"] = 0x1C0,
                    ["confidence"] = "high",
                },
                new JsonObject
                {
                    ["prop_addr"] = "0x208",
                    ["name"] = "Stamina",
                    ["type"] = "FloatProperty",
                    ["occurrences"] = 1,
                    ["write_count"] = 0,
                    ["scope"] = "instance",
                    ["offset"] = 0x1C8,
                    ["confidence"] = "low",
                },
            }
        });

        var svc = CreateService();
        var res = await svc.WalkFunctionPropsAsync("0x100", TestContext.Current.CancellationToken);

        Assert.Equal("disasm", res.Method);
        Assert.True(res.IsDisasm);
        Assert.Equal(0, res.ScriptBytes);
        Assert.Equal(3, res.Unmapped);
        Assert.Equal(2, res.Props.Count);

        Assert.Equal(0x1C0, res.Props[0].Offset);
        Assert.Equal("high", res.Props[0].Confidence);
        Assert.False(res.Props[0].IsLowConfidence);

        Assert.Equal("low", res.Props[1].Confidence);
        Assert.True(res.Props[1].IsLowConfidence);
    }

    [Fact]
    public async Task WalkFunctionPropsAsync_ParsesNoneMethod()
    {
        // Native function but UFunction::Func offset unresolved on this build.
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["query_addr"] = "0x100",
            ["script_bytes"] = 0,
            ["method"] = "none",
            ["unmapped"] = 0,
            ["props"] = new JsonArray()
        });

        var svc = CreateService();
        var res = await svc.WalkFunctionPropsAsync("0x100", TestContext.Current.CancellationToken);

        Assert.Equal("none", res.Method);
        Assert.False(res.IsDisasm);
        Assert.Empty(res.Props);
    }

    [Fact]
    public async Task WalkFunctionPropsAsync_DefaultsToBytecodeWhenMethodAbsent()
    {
        // Older DLLs don't emit "method" — must default to "bytecode", not "none",
        // so existing bytecode results keep rendering.
        _pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true,
            ["query_addr"] = "0x100",
            ["script_bytes"] = 256,
            ["props"] = new JsonArray
            {
                new JsonObject
                {
                    ["prop_addr"] = "0x200",
                    ["name"] = "Gold",
                    ["type"] = "IntProperty",
                    ["occurrences"] = 1,
                    ["write_count"] = 1,
                    ["scope"] = "instance",
                },
            }
        });

        var svc = CreateService();
        var res = await svc.WalkFunctionPropsAsync("0x100", TestContext.Current.CancellationToken);

        Assert.Equal("bytecode", res.Method);
        Assert.Equal(0, res.Unmapped);
        Assert.Single(res.Props);
    }

    // --- UE5.7+ packed FUObjectItem layout surfacing ---

    [Fact]
    public async Task InitAsync_ParsesPackedItemLayout()
    {
        _pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>();
            if (cmd == "init")
                return new JsonObject { ["ok"] = true, ["ue_version"] = 507 };
            if (cmd == "get_pointers")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["gobjects"] = "0x7FF600A12340",
                    ["object_count"] = 1024,
                    ["item_packed"] = true,
                    ["item_layout_mode"] = "packed57",
                    ["item_obj_offset"] = 0,
                    ["item_size"] = 24,
                };
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.InitAsync(TestContext.Current.CancellationToken);

        Assert.True(state.ItemPacked);
        Assert.Equal("packed57", state.ItemLayoutMode);
        Assert.Equal(0, state.ItemObjOffset);
    }

    [Fact]
    public async Task InitAsync_DefaultsItemLayoutToClassicWhenKeysAbsent()
    {
        // Older DLLs omit the item_* keys → must default to classic / not-packed.
        _pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>();
            if (cmd == "init")
                return new JsonObject { ["ok"] = true, ["ue_version"] = 504 };
            if (cmd == "get_pointers")
                return new JsonObject
                {
                    ["ok"] = true,
                    ["gobjects"] = "0x7FF600A12340",
                    ["object_count"] = 1024,
                };
            return new JsonObject { ["ok"] = true };
        });

        var svc = CreateService();
        var state = await svc.InitAsync(TestContext.Current.CancellationToken);

        Assert.False(state.ItemPacked);
        Assert.Equal("classic", state.ItemLayoutMode);
        Assert.Equal(0, state.ItemObjOffset);
    }

    [Fact]
    public async Task GetCePointerInfo_PackedLayout_DegradesToAbsoluteAddress()
    {
        _pipe.SetHandler(req => new JsonObject
        {
            ["ok"] = true,
            ["packed_layout"] = true,
            ["warning"] = "UE5.7+ packed FUObjectItem layout (UNVERIFIED): absolute address only.",
            ["ce_base"] = "0x1F809E08FB0",
            ["ce_offsets"] = new JsonArray { 0x40 },
            ["internal_index"] = 123,
        });

        var svc = CreateService();
        var info = await svc.GetCePointerInfoAsync("0x1F809E08FB0", 0x40, TestContext.Current.CancellationToken);

        Assert.True(info.PackedLayout);
        Assert.False(string.IsNullOrEmpty(info.Warning));
        Assert.Equal("0x1F809E08FB0", info.CeBase);
        Assert.Single(info.CeOffsets);            // degraded: just the field hop
        Assert.Equal(0x40, info.CeOffsets[0]);
    }

    [Fact]
    public async Task GetCePointerInfo_DirectLayout_KeepsFullGObjectsChain()
    {
        _pipe.SetHandler(req => new JsonObject
        {
            ["ok"] = true,
            ["packed_layout"] = false,
            ["ce_base"] = "\"Game.exe\"+1BA1820",
            ["ce_offsets"] = new JsonArray { 0x40, 0x108, 0x18, 0 },  // field, item+objOff, chunk, deref
        });

        var svc = CreateService();
        var info = await svc.GetCePointerInfoAsync("0x1F809E08FB0", 0x40, TestContext.Current.CancellationToken);

        Assert.False(info.PackedLayout);
        Assert.Equal("", info.Warning);
        Assert.Equal(4, info.CeOffsets.Length);   // full GObjects→chunk→item→field chain
    }

    [Fact]
    public async Task SetPackedConsts_ParsesModeAndSamples()
    {
        _pipe.SetHandler(req =>
        {
            Assert.Equal("set_packed_consts", req["cmd"]?.GetValue<string>());
            Assert.True(req["force"]?.GetValue<bool>());
            return new JsonObject
            {
                ["ok"] = true,
                ["item_packed"] = true,
                ["item_layout_mode"] = "packed57",
                ["item_obj_offset"] = 0,
                ["item_size"] = 24,
                ["samples"] = new JsonArray
                {
                    new JsonObject { ["index"] = 0, ["addr"] = "0x1F800000000", ["name"] = "CoreUObject" },
                    new JsonObject { ["index"] = 1, ["addr"] = "0x1F800000100", ["name"] = "Package" },
                },
            };
        });

        var svc = CreateService();
        var res = await svc.SetPackedConstsAsync(alignBits: 3, ptrMaskBits: 0x3FFF, force: true,
            ct: TestContext.Current.CancellationToken);

        Assert.True(res.ItemPacked);
        Assert.Equal("packed57", res.ItemLayoutMode);
        Assert.Equal(24, res.ItemSize);
        Assert.Equal(2, res.Samples.Length);
        Assert.Equal("CoreUObject", res.Samples[0].Name);
        Assert.Equal(1, res.Samples[1].Index);
    }

    // === Per-launch session token (build 1227) ===

    [Fact]
    public async Task GetPointersAsync_ParsesProcessCreationTime_IntoGameSessionId()
    {
        // The DLL emits process_creation_time in the get_pointers snapshot; it
        // must flow into EngineState and fold into GameSessionId = PeHash-CreationTime.
        _pipe.SetHandler(req => new JsonObject
        {
            ["ok"] = true,
            ["gobjects"] = "0x10",
            ["gnames"] = "0x20",
            ["object_count"] = 5,
            ["pe_hash"] = "ABCD1234",
            ["module_base"] = "0x7FF600000000",
            ["process_creation_time"] = "01D9ABCDEF012345",
        });

        var svc = CreateService();
        var state = await svc.GetPointersAsync(TestContext.Current.CancellationToken);

        Assert.Equal("01D9ABCDEF012345", state.ProcessCreationTime);
        Assert.Equal("ABCD1234-01D9ABCDEF012345", state.GameSessionId);
    }

    [Fact]
    public async Task GetPointersAsync_OlderDll_NoCreationTime_GameSessionIdDegrades()
    {
        // Back-compat: a DLL older than build 1227 omits process_creation_time →
        // ProcessCreationTime defaults to "" → GameSessionId = "PeHash-".
        _pipe.SetHandler(req => new JsonObject
        {
            ["ok"] = true,
            ["gobjects"] = "0x10",
            ["gnames"] = "0x20",
            ["object_count"] = 5,
            ["pe_hash"] = "ABCD1234",
            ["module_base"] = "0x7FF600000000",
        });

        var svc = CreateService();
        var state = await svc.GetPointersAsync(TestContext.Current.CancellationToken);

        Assert.Equal("", state.ProcessCreationTime);
        Assert.Equal("ABCD1234-", state.GameSessionId);
    }

    // === Property Search: deep descent (build 1222) ===

    [Fact]
    public async Task SearchPropertiesAsync_DefaultDeepFalse_SendsDeepFalse()
    {
        // The shallow direct-field search must stay the default: an unspecified
        // deep arg sends deep=false so older/expensive descent never runs unasked.
        JsonObject? lastReq = null;
        _pipe.SetHandler(req =>
        {
            lastReq = req;
            return new JsonObject
            {
                ["ok"] = true,
                ["total"] = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["results"] = new JsonArray(),
            };
        });

        var svc = CreateService();
        await svc.SearchPropertiesAsync("GP", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(lastReq);
        Assert.Equal("search_properties", lastReq!["cmd"]?.GetValue<string>());
        Assert.False(lastReq["deep"]?.GetValue<bool>());
    }

    [Fact]
    public async Task SearchPropertiesAsync_DeepTrue_SendsDeepAndParsesIsNested()
    {
        // deep=true must reach the wire, and a nested result row's is_nested
        // flag must round-trip onto the model (gates Copy Offset / Freeze).
        JsonObject? lastReq = null;
        _pipe.SetHandler(req =>
        {
            lastReq = req;
            return new JsonObject
            {
                ["ok"] = true,
                ["total"] = 2,
                ["scanned_classes"] = 1,
                ["scanned_objects"] = 100,
                ["results"] = new JsonArray
                {
                    // shallow row — no is_nested field (older-DLL / direct-field shape)
                    new JsonObject
                    {
                        ["class_name"] = "BP_LifeSaveData_C",
                        ["prop_name"] = "GP",
                        ["prop_type"] = "IntProperty",
                        ["prop_offset"] = 0x40,
                        ["field_addr"] = "0x1000",
                    },
                    // deep row — synthetic dotted path + is_nested = true
                    new JsonObject
                    {
                        ["class_name"] = "BP_LifeSaveData_C",
                        ["prop_name"] = "SaveSlotList[].MsTuneData.GP",
                        ["prop_type"] = "IntProperty",
                        ["prop_offset"] = 0x18,
                        ["field_addr"] = "0x2000",
                        ["is_nested"] = true,
                    },
                },
            };
        });

        var svc = CreateService();
        var res = await svc.SearchPropertiesAsync(
            "GP", deep: true, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(lastReq);
        Assert.True(lastReq!["deep"]?.GetValue<bool>());

        Assert.Equal(2, res.Results.Count);
        // shallow row: is_nested absent → false → scalar actions stay visible
        Assert.False(res.Results[0].IsNested);
        Assert.True(res.Results[0].ShowScalarActions);
        // deep row: is_nested true → nested, scalar actions hidden, path tooltip shown
        Assert.True(res.Results[1].IsNested);
        Assert.False(res.Results[1].ShowScalarActions);
        Assert.Equal("SaveSlotList[].MsTuneData.GP", res.Results[1].PropName);
        Assert.NotNull(res.Results[1].PropNameTooltip);
    }

    // "Auto detect Engine/System noise" PRE-filter: the opt-in toggle must reach the
    // wire as auto_skip_noise=true ONLY when enabled (off keeps the request byte-
    // identical, matching the other opt-in scan toggles).
    [Fact]
    public async Task BeginValueScanAsync_AutoSkipNoise_AttachedOnlyWhenOn()
    {
        JsonObject? captured = null;
        _pipe.SetHandler(req => { captured = req; return new JsonObject { ["ok"] = true }; });
        var svc = CreateService();

        await svc.BeginValueScanAsync(ValueScanDataType.Int32, ValueScanType.Exact, "100",
            ct: TestContext.Current.CancellationToken);
        Assert.NotNull(captured);
        Assert.False(captured!.ContainsKey("auto_skip_noise"));   // default off → omitted

        await svc.BeginValueScanAsync(ValueScanDataType.Int32, ValueScanType.Exact, "100",
            autoSkipNoise: true, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(captured);
        Assert.True(captured!["auto_skip_noise"]?.GetValue<bool>());
    }

    [Fact]
    public async Task BeginGroupScanAsync_PerSlotCap_AttachedOnlyWhenMovedOffTheDefault()
    {
        // The cap decides what a later Changed/Decreased refine can re-read, so it is a
        // real setting rather than a tuning knob -- but the common case must stay
        // wire-identical, or every existing capture would show a spurious new field.
        JsonObject? captured = null;
        _pipe.SetHandler(req => { captured = req; return new JsonObject { ["ok"] = true }; });
        var svc = CreateService();
        var slots = new List<GroupSlotInput>
        {
            new() { DataType = ValueScanDataType.NumericNoByte, ScanType = ValueScanType.Exact, Value = "100" },
            new() { DataType = ValueScanDataType.NumericNoByte, ScanType = ValueScanType.Exact, Value = "50" },
        };

        await svc.BeginGroupScanAsync(slots, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(captured);
        Assert.False(captured!.ContainsKey("per_slot_cap"));   // default → omitted

        await svc.BeginGroupScanAsync(slots, perSlotCap: 512, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(captured);
        Assert.Equal(512, captured!["per_slot_cap"]?.GetValue<int>());
    }

    [Fact]
    public async Task BeginGroupScanAsync_AutoSkipNoise_AttachedOnlyWhenOn()
    {
        JsonObject? captured = null;
        _pipe.SetHandler(req => { captured = req; return new JsonObject { ["ok"] = true }; });
        var svc = CreateService();
        var slots = new List<GroupSlotInput>
        {
            new() { DataType = ValueScanDataType.NumericNoByte, ScanType = ValueScanType.Exact, Value = "100" },
            new() { DataType = ValueScanDataType.NumericNoByte, ScanType = ValueScanType.Exact, Value = "50" },
        };

        await svc.BeginGroupScanAsync(slots, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(captured);
        Assert.False(captured!.ContainsKey("auto_skip_noise"));   // default off → omitted

        await svc.BeginGroupScanAsync(slots, autoSkipNoise: true, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(captured);
        Assert.True(captured!["auto_skip_noise"]?.GetValue<bool>());
    }

    // --- search_properties_batch truncation (audit #5 X1) ----------------------
    //
    // The DLL has emitted per-query `truncated` and batch `aborted` since the D5/F4 fix.
    // The single-query parser read them; this batch twin, ~80 lines away in the same
    // file, did not -- so the two discovery panels presented a capped page as the pool,
    // which is the exact report class F4 was written to end.

    [Fact]
    public void ParseSearchPropertiesBatch_CarriesPerQueryTruncation()
    {
        var json = """
        {"per_query":[
           {"query":"Max","match_count":200,"truncated":true,"results":[]},
           {"query":"Zzz","match_count":3,"truncated":false,"results":[]}],
         "query_count":2,"total":203,"scanned_classes":10,"scanned_objects":99,
         "aborted":false}
        """;

        var r = DumpService.ParseSearchPropertiesBatchForTest(json);

        Assert.True(r.PerQuery[0].Truncated);
        Assert.False(r.PerQuery[1].Truncated);
        Assert.Equal(new[] { "Max" }, r.TruncatedQueries);
        Assert.False(r.Aborted);
    }

    [Fact]
    public void ParseSearchPropertiesBatch_CarriesAborted()
    {
        var json = """
        {"per_query":[{"query":"Max","match_count":1,"results":[]}],
         "query_count":1,"total":1,"scanned_classes":1,"scanned_objects":1,
         "aborted":true}
        """;

        var r = DumpService.ParseSearchPropertiesBatchForTest(json);

        Assert.True(r.Aborted);
    }

    [Fact]
    public void ParseSearchPropertiesBatch_OlderDllWithoutTheFlags_StaysSilent()
    {
        // Backward-safe: a pre-2818 DLL omits both keys, and the old silent behaviour
        // (no cap warning) must remain -- not a spurious warning on every scan.
        var json = """
        {"per_query":[{"query":"Max","match_count":5,"results":[]}],
         "query_count":1,"total":5,"scanned_classes":1,"scanned_objects":1}
        """;

        var r = DumpService.ParseSearchPropertiesBatchForTest(json);

        Assert.False(r.Aborted);
        Assert.False(r.PerQuery[0].Truncated);
        Assert.Empty(r.TruncatedQueries);
    }

    // ------------------------------------------------------------------
    // list_classes truncation (audit #5 X2)
    //
    // The class list is a PAGE, not the pool: the DLL stops walking GObjects the
    // moment it has `limit` rows. Callers resolve class NAMES out of it, so a class
    // past the cap has to be distinguishable from one that does not exist.
    // ------------------------------------------------------------------

    private static JsonObject ClassListResponse(int rows, bool? truncated)
    {
        var arr = new JsonArray();
        for (int i = 0; i < rows; i++)
            arr.Add(new JsonObject
            {
                ["class_name"] = $"Class{i}",
                ["class_addr"] = $"0x{i:X}",
            });

        var res = new JsonObject
        {
            ["ok"] = true,
            ["total"] = rows,
            ["scanned_objects"] = 900_000,
            ["total_classes"] = rows,
            ["classes"] = arr,
        };
        if (truncated.HasValue) res["truncated"] = truncated.Value;
        return res;
    }

    [Fact]
    public async Task ListClassesAsync_CarriesTruncatedFlagAndLimit()
    {
        _pipe.SetHandler(_ => ClassListResponse(rows: 3, truncated: true));

        var svc = CreateService();
        var r = await svc.ListClassesAsync(gameOnly: false, limit: 3,
                                           ct: TestContext.Current.CancellationToken);

        Assert.True(r.Truncated);
        Assert.Equal(3, r.RequestedLimit);
    }

    [Fact]
    public async Task ListClassesAsync_FullWalkIsNotTruncated()
    {
        // The DLL walked to the end and says so — a short page must NOT be flagged,
        // or every lookup miss would claim the class "may still exist".
        _pipe.SetHandler(_ => ClassListResponse(rows: 2, truncated: false));

        var svc = CreateService();
        var r = await svc.ListClassesAsync(gameOnly: false, limit: 5000,
                                           ct: TestContext.Current.CancellationToken);

        Assert.False(r.Truncated);
    }

    [Fact]
    public async Task ListClassesAsync_OlderDllWithoutTheFlag_InfersFromAFullPage()
    {
        // A pre-2882 DLL omits the key. A FULL page is still evidence the walk stopped,
        // so the caveat must survive the older DLL rather than silently degrading to
        // "not found" — which is the bug this fix exists to end.
        _pipe.SetHandler(_ => ClassListResponse(rows: 4, truncated: null));

        var svc = CreateService();
        var full = await svc.ListClassesAsync(gameOnly: false, limit: 4,
                                              ct: TestContext.Current.CancellationToken);
        Assert.True(full.Truncated);

        // …and a SHORT page from the same older DLL must stay silent (asserts an
        // absence: no spurious cap warning on a game with few classes).
        var partial = await svc.ListClassesAsync(gameOnly: false, limit: 5000,
                                                 ct: TestContext.Current.CancellationToken);
        Assert.False(partial.Truncated);
    }

    [Fact]
    public void FindClassAddr_HitReturnsAddress()
    {
        var list = new ClassListResult
        {
            Truncated = true,
            RequestedLimit = 5000,
            Classes = { new GameClassEntry { ClassName = "BP_Player_C", ClassAddr = "0x1234" } },
        };

        var hit = list.FindClassAddr("BP_Player_C");

        Assert.True(hit.Found);
        Assert.Equal("0x1234", hit.Addr);
    }

    [Fact]
    public void FindClassAddr_MissOnACappedListSaysTheClassMayStillExist()
    {
        var list = new ClassListResult
        {
            Truncated = true,
            RequestedLimit = 5000,
            Classes = { new GameClassEntry { ClassName = "Other", ClassAddr = "0x1" } },
        };

        var miss = list.FindClassAddr("BP_Player_C");

        Assert.False(miss.Found);
        Assert.Contains("CAPPED", miss.MissReason);
        Assert.Contains("5,000", miss.MissReason);
        Assert.Contains("may still exist", miss.MissReason);
    }

    [Fact]
    public void FindClassAddr_MissOnACompleteListSaysNotFound()
    {
        // The other direction: a full walk that genuinely lacks the class must NOT
        // hedge, or the caveat becomes noise on every real miss.
        var list = new ClassListResult
        {
            Truncated = false,
            RequestedLimit = 5000,
            Classes = { new GameClassEntry { ClassName = "Other", ClassAddr = "0x1" } },
        };

        var miss = list.FindClassAddr("BP_Player_C");

        Assert.False(miss.Found);
        Assert.Equal("not found", miss.MissReason);
    }

    [Fact]
    public void FindClassAddr_RowWithNoAddressIsNotAHit()
    {
        // list_classes can emit a name with an empty class_addr; treating that as a hit
        // hands "" to walk_functions, which fails later and further from the cause.
        var list = new ClassListResult
        {
            RequestedLimit = 5000,
            Classes = { new GameClassEntry { ClassName = "BP_Player_C", ClassAddr = "" } },
        };

        Assert.False(list.FindClassAddr("BP_Player_C").Found);
    }
}
