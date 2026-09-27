using UE5DumpUI.Core;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [FLY-EXPORT-EXPERIMENTAL] The maintainer's call (2026-09-26): the Fly CE records follow the
/// Experimental features switch. With it OFF the Fly card is hidden, so Add action records and
/// Save .CT must not hand the user four Fly records they cannot see in the UI; with it ON, both
/// export them as before. Every other record group is unaffected either way.
/// </summary>
public class FlyExportFollowsExperimentalTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"UE5DumpFlyExport_{Guid.NewGuid():N}");

    public FlyExportFollowsExperimentalTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Add_action_records_ships_Fly_only_while_experimental_is_on(bool experimental)
    {
        var bridge = new RecordingBridge();
        var vm = new TeleportViewModel(new StubDumpService(), new MockLoggingService(), new SavingPlatform(_dir),
            aobMaker: bridge, experimentalGate: new Gate(experimental));

        await vm.AddActionsToCeCommand.ExecuteAsync(null);

        int fly = bridge.Descriptions.Count(d => d.StartsWith("Fly:", StringComparison.Ordinal));
        Assert.Equal(experimental ? FlyScriptGenerator.BuildBatchRows().Count : 0, fly);
        Assert.Contains(bridge.Descriptions, d => d.StartsWith("Movement:", StringComparison.Ordinal));
        Assert.Contains(bridge.Descriptions, d => d.StartsWith("Time:", StringComparison.Ordinal));
        // [WIKI-REVIEW-TEXT] The status line must not describe Fly records it did not send.
        Assert.Equal(experimental, vm.StatusText.Contains("fly", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_CT_writes_Fly_only_while_experimental_is_on(bool experimental)
    {
        var platform = new SavingPlatform(_dir);
        var vm = new TeleportViewModel(new StubDumpService(), new MockLoggingService(), platform,
            experimentalGate: new Gate(experimental));

        await vm.SaveCtCommand.ExecuteAsync(null);

        var ct = File.ReadAllText(platform.SavedPath);
        foreach (var row in FlyScriptGenerator.BuildBatchRows())
            Assert.Equal(experimental, ct.Contains(row.Description, StringComparison.Ordinal));
        Assert.Contains("Movement", ct, StringComparison.Ordinal);
    }

    internal sealed class Gate(bool enabled) : IExperimentalGate
    {
        public bool IsEnabled { get; set; } = enabled;
        public int SnapshotQuotaMb { get; set; } = 1024;
        public bool IsLocked => false;
        public void Lock() { }
        public event EventHandler? Changed { add { } remove { } }
    }

    internal sealed class SavingPlatform(string dir) : IPlatformService
    {
        public string SavedPath { get; } = Path.Combine(dir, "teleport.CT");
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => dir;
        public string GetLogDirectoryPath() => Path.Combine(dir, "Logs");
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST-MACHINE";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName, string filterExtension)
            => Task.FromResult<string?>(SavedPath);
    }

    internal sealed class RecordingBridge : IAobMakerBridge
    {
        public List<string> Descriptions { get; } = new();
        public bool IsAvailable => true;
        public AobMakerFailure LastFailure => AobMakerFailure.None;
        public Task<bool> CheckAvailabilityAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> NavigateHexViewAsync(string hexAddress, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> NavigateDisassemblerAsync(string hexAddress, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CreateAAScriptAsync(string description, string script,
            bool autoActivate = true, string? group = null, CancellationToken ct = default)
        {
            Descriptions.Add(description);
            return Task.FromResult(true);
        }
        public Task<bool> CreateSymbolScriptAsync(string name, string aob, int pos, int aoblen,
            string symbol, string module, bool autoActivate = true, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> CreateMemoryRecordAsync(string description, string address, int valueType,
            bool isSigned = false, bool showAsHex = false, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<(bool Ok, string? ErrorMessage)> InjectTableFileAsync(string fileName, string content,
            CancellationToken ct = default)
            => Task.FromResult<(bool, string?)>((false, null));
    }
}
