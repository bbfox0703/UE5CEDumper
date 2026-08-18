using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// MainWindowViewModel.InjectCeHelperLuaCommand wiring tests.
///
/// The command is a thin orchestrator: probe AOBMaker availability ->
/// read embedded helper -> hand off to bridge -> set status text. The
/// VM doesn't touch the pipe client / dump service in this path, so
/// the surrounding infrastructure is stubbed out as no-ops. These tests
/// validate the four end-states the user can land in:
///   - bridge missing entirely (DI didn't supply one)
///   - bridge present but CE not running -> graceful "use Export" hint
///   - bridge present, inject succeeds -> success status
///   - bridge present, inject fails -> fallback hint
/// </summary>
public class MainWindowInjectHelperTests
{
    [Fact]
    public async Task InjectCeHelperLua_NoBridge_StatusTextHintsConfigMissing()
    {
        var vm = BuildVm(aobMaker: null);

        await vm.InjectCeHelperLuaCommand.ExecuteAsync(null);

        Assert.Contains("AOBMaker", vm.StatusText);
    }

    [Fact]
    public async Task InjectCeHelperLua_BridgeUnavailable_StatusTextHintsCe()
    {
        var bridge = new RecordingBridge { NextAvailability = false };
        var vm = BuildVm(aobMaker: bridge);

        await vm.InjectCeHelperLuaCommand.ExecuteAsync(null);

        Assert.Equal(1, bridge.CheckCalls);
        Assert.Equal(0, bridge.InjectCalls);
        Assert.Contains("AOBMaker not connected", vm.StatusText);
    }

    [Fact]
    public async Task InjectCeHelperLua_Success_PassesEmbeddedHelperToBridge()
    {
        var bridge = new RecordingBridge { NextAvailability = true, NextInjectResult = true };
        var vm = BuildVm(aobMaker: bridge);

        await vm.InjectCeHelperLuaCommand.ExecuteAsync(null);

        Assert.Equal(1, bridge.InjectCalls);
        Assert.Equal(HelperLuaResource.DefaultFileName, bridge.LastInjectFileName);
        // The embedded helper is a non-trivial Lua module -- a good
        // floor without coupling to its exact byte count.
        Assert.NotNull(bridge.LastInjectContent);
        Assert.True(bridge.LastInjectContent!.Length > 200,
            $"helper content was suspiciously short ({bridge.LastInjectContent.Length} chars)");
        Assert.Contains("Inject helper OK", vm.StatusText);
        Assert.Contains(HelperLuaResource.DefaultFileName, vm.StatusText);
    }

    [Fact]
    public async Task InjectCeHelperLua_BridgeReturnsFalse_StatusOffersExportFallback()
    {
        var bridge = new RecordingBridge { NextAvailability = true, NextInjectResult = false };
        var vm = BuildVm(aobMaker: bridge);

        await vm.InjectCeHelperLuaCommand.ExecuteAsync(null);

        Assert.Equal(1, bridge.InjectCalls);
        Assert.Contains("Export to disk", vm.StatusText);
    }

    [Fact]
    public async Task InjectCeHelperLua_BridgeReturnsError_StatusSurfacesPluginMessage()
    {
        // Plugin returned an explicit failure reason (e.g. real bug from
        // pre-fix builds: "Stream size mismatch: wrote 10008, stream has 0").
        // The user can't tell "wrong CE state" apart from a real plugin
        // bug unless we actually surface the plugin's text.
        var bridge = new RecordingBridge
        {
            NextAvailability = true,
            NextInjectResult = false,
            NextInjectError = "Stream size mismatch: wrote 10008, stream has 0"
        };
        var vm = BuildVm(aobMaker: bridge);

        await vm.InjectCeHelperLuaCommand.ExecuteAsync(null);

        Assert.Equal(1, bridge.InjectCalls);
        Assert.Contains("Stream size mismatch", vm.StatusText);
        Assert.Contains("Export to disk", vm.StatusText);
    }

    // ------------------------------------------------------------------
    // Wiring helpers
    // ------------------------------------------------------------------

    private static MainWindowViewModel BuildVm(IAobMakerBridge? aobMaker)
        => new MainWindowViewModel(
            pipeClient: new NoopPipeClient(),
            dump: new StubDumpService(),
            log: new NoopLog(),
            platform: new NoopPlatform(),
            aobUsage: null,
            aobMaker: aobMaker,
            proxyDeploy: null);

    private sealed class RecordingBridge : IAobMakerBridge
    {
        public bool NextAvailability { get; set; }
        public bool NextInjectResult { get; set; }
        public string? NextInjectError { get; set; }
        public int CheckCalls { get; private set; }
        public int InjectCalls { get; private set; }
        public string? LastInjectFileName { get; private set; }
        public string? LastInjectContent { get; private set; }

        public bool IsAvailable { get; private set; }

        public Task<bool> CheckAvailabilityAsync(CancellationToken ct = default)
        {
            CheckCalls++;
            IsAvailable = NextAvailability;
            return Task.FromResult(NextAvailability);
        }

        public Task<(bool Ok, string? ErrorMessage)> InjectTableFileAsync(string fileName, string content,
            CancellationToken ct = default)
        {
            InjectCalls++;
            LastInjectFileName = fileName;
            LastInjectContent = content;
            return Task.FromResult((NextInjectResult, NextInjectError));
        }

        public Task<bool> NavigateHexViewAsync(string hexAddress, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> NavigateDisassemblerAsync(string hexAddress, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> CreateAAScriptAsync(string description, string script,
            bool autoActivate = true, string? group = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CreateSymbolScriptAsync(string name, string aob, int pos, int aoblen,
            string symbol, string module, bool autoActivate = true, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> CreateMemoryRecordAsync(string description, string address, int valueType,
            bool isSigned = false, bool showAsHex = false, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class NoopPipeClient : IPipeClient
    {
        public bool IsConnected => false;
        public event Action<bool>? ConnectionStateChanged { add { } remove { } }
        public event Action<JsonObject>? EventReceived { add { } remove { } }
        public event Action<UE5DumpUI.Models.PipeLogEntry>? Activity { add { } remove { } }
        public event Action<bool>? GameThreadStalledChanged { add { } remove { } }
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<JsonObject> SendAsync(JsonObject request, CancellationToken ct = default)
            => Task.FromResult(new JsonObject());
        public void Dispose() { }
    }

    private sealed class NoopPlatform : IPlatformService
    {
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => "";
        public string GetLogDirectoryPath() => "";
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "test";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName,
            string filterName, string filterExtension) => Task.FromResult<string?>(null);
    }

    private sealed class NoopLog : ILoggingService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Error(string message, Exception ex) { }
        public void Debug(string message) { }
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message) { }
        public void Error(string category, string message, Exception ex) { }
        public void Debug(string category, string message) { }
        public void StartProcessMirror(string processName) { }
        public void StopProcessMirror() { }
    }
}
