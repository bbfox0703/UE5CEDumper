using System.Collections.ObjectModel;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Cancellation semantics for the Live Walker "Copy CE XML" / "Copy CE Field" exports.
/// The drilldown pipe-walk is abortable via a per-export CancellationTokenSource; the
/// key subtlety is that the VM's <c>catch (OperationCanceledException)</c> is guarded
/// with <c>when (cts.IsCancellationRequested)</c> so that a BARE OperationCanceledException
/// NOT raised by our token — e.g. PipeClient's "Pipe disconnected during send" — is
/// reported as a real error, not silently mislabeled as a user cancellation.
/// </summary>
public class LiveWalkerExportCancelTests
{
    // Stub whose WalkInstanceAsync runs an optional pre-throw hook then throws a supplied
    // exception. Lets a test simulate either a user cancel (hook cancels the export, then
    // the in-flight OCE surfaces with the token cancelled) or a raw pipe disconnect (bare
    // OCE, token NOT cancelled).
    private sealed class WalkThrowStub : StubDumpService
    {
        public Action? BeforeThrow;
        public Func<Exception> Make = () => new OperationCanceledException();

        public override Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
            int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false,
            CancellationToken ct = default)
        {
            BeforeThrow?.Invoke();
            throw Make();
        }
    }

    private static LiveWalkerViewModel MakeVm(WalkThrowStub dump, MockLoggingService? log = null,
                                              IPlatformService? platform = null)
    {
        var vm = new LiveWalkerViewModel(dump, log ?? new MockLoggingService(),
            platform ?? new MockPlatformService(Path.GetTempPath()))
        {
            CurrentAddress = "0x1000",
            Breadcrumbs = new ObservableCollection<BreadcrumbItem>
            {
                new() { Address = "0x1000", Label = "Root", FieldName = "Root" },
            },
            // A StructProperty field forces a WalkInstance during ResolveDrilldown (structs
            // resolve regardless of drill depth), so the export always reaches the pipe call.
            Fields = new ObservableCollection<LiveFieldValue>
            {
                new() { Name = "S", TypeName = "StructProperty", Offset = 0,
                        StructClassAddr = "0xCLS", StructDataAddr = "0xDATA" },
            },
        };
        return vm;
    }

    [Fact]
    public async Task ExportCeXml_UserCancel_ReportsCancelled()
    {
        var dump = new WalkThrowStub();
        var vm = MakeVm(dump);
        // Simulate the user hitting Cancel mid-walk: cancel the export, then let the
        // in-flight OCE surface — now cts.IsCancellationRequested is true.
        dump.BeforeThrow = () => vm.CancelExportCommand.Execute(null);

        await vm.ExportCeXmlCommand.ExecuteAsync(null);

        Assert.Equal("Export cancelled.", vm.StatusText);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsExporting);
    }

    [Fact]
    public async Task ExportCeXml_PipeDisconnect_NotMislabeledAsCancel()
    {
        // Regression (review 2026-07-07): a mid-export pipe disconnect surfaces as a BARE
        // OperationCanceledException ("Pipe disconnected during send") that is NOT from the
        // export token. It must be reported as an error, never as a user cancellation.
        var dump = new WalkThrowStub { Make = () => new OperationCanceledException("Pipe disconnected during send") };
        var vm = MakeVm(dump);

        await vm.ExportCeXmlCommand.ExecuteAsync(null);

        Assert.NotEqual("Export cancelled.", vm.StatusText);
        Assert.Equal("Pipe disconnected during send", vm.ErrorMessage);
        Assert.False(vm.IsExporting);
    }

    [Fact]
    public async Task ExportCeField_PipeDisconnect_NotMislabeledAsCancel()
    {
        // Same guard on the Copy CE Field command (shares the guarded catch).
        var dump = new WalkThrowStub { Make = () => new OperationCanceledException("Pipe disconnected during send") };
        var vm = MakeVm(dump);
        vm.UpdateSelectedFields(new List<LiveFieldValue>(vm.Fields));

        await vm.ExportCeFieldXmlCommand.ExecuteAsync(null);

        Assert.NotEqual("Export cancelled.", vm.StatusText);
        Assert.Equal("Pipe disconnected during send", vm.ErrorMessage);
        Assert.False(vm.IsExporting);
    }

    // ── [LW-DISCONNECT-STALE-STATUS] review F3: a disconnect cancels the export through the SAME token ──
    //
    // ClearOnDisconnect cancels _exportCts and clears the status line; the export's guarded handler then saw its own
    // token cancelled and wrote "Export cancelled." over the cleared line, logging "cancelled by user" -- the very
    // mislabel that guard's comment says a mid-export disconnect must not get. Nobody pressed Cancel.

    /// <summary>A save dialog that answers a path, so Export CSX gets past it to the resolve.</summary>
    private sealed class SavePathPlatform : IPlatformService
    {
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetLogDirectoryPath() => Path.GetTempPath();
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string a, string b, string c)
            => Task.FromResult<string?>(Path.Combine(Path.GetTempPath(), $"lw_disconnect_{Guid.NewGuid():N}.CSX"));
    }

    private static void AssertSaysNothingOfAUserCancel(LiveWalkerViewModel vm, MockLoggingService log)
    {
        Assert.Equal("", vm.StatusText);
        Assert.True(string.IsNullOrEmpty(vm.ErrorMessage), $"ErrorMessage: '{vm.ErrorMessage}'");
        Assert.DoesNotContain(log.Messages, m => m.Contains("cancelled by user", StringComparison.Ordinal));
        Assert.False(vm.IsExporting);
    }

    [Fact]
    public async Task ExportCeXml_CancelledByADisconnect_IsNotReportedAsAUserCancel()
    {
        var log = new MockLoggingService();
        var dump = new WalkThrowStub();
        var vm = MakeVm(dump, log);
        dump.BeforeThrow = vm.ClearOnDisconnect;   // the pipe drops mid-walk; the walk's OCE surfaces on our token

        await vm.ExportCeXmlCommand.ExecuteAsync(null);

        AssertSaysNothingOfAUserCancel(vm, log);
    }

    [Fact]
    public async Task ExportCeField_CancelledByADisconnect_IsNotReportedAsAUserCancel()
    {
        var log = new MockLoggingService();
        var dump = new WalkThrowStub();
        var vm = MakeVm(dump, log);
        vm.UpdateSelectedFields(new List<LiveFieldValue>(vm.Fields));
        dump.BeforeThrow = vm.ClearOnDisconnect;

        await vm.ExportCeFieldXmlCommand.ExecuteAsync(null);

        AssertSaysNothingOfAUserCancel(vm, log);
    }

    [Fact]
    public async Task ExportCsx_CancelledByADisconnect_IsNotReportedAsAUserCancel()
    {
        var log = new MockLoggingService();
        var dump = new WalkThrowStub();
        var vm = MakeVm(dump, log, new SavePathPlatform());
        vm.HasData = true;
        dump.BeforeThrow = vm.ClearOnDisconnect;

        await vm.ExportCsx77Command.ExecuteAsync(null);

        AssertSaysNothingOfAUserCancel(vm, log);
    }

    [Fact]
    public async Task ExportCsx_UserCancel_StillReportsCancelled()
    {
        // The other half: the fix must not swallow a cancel the user did press.
        var dump = new WalkThrowStub();
        var vm = MakeVm(dump, platform: new SavePathPlatform());
        vm.HasData = true;
        dump.BeforeThrow = () => vm.CancelExportCommand.Execute(null);

        await vm.ExportCsx77Command.ExecuteAsync(null);

        Assert.Equal("Export cancelled.", vm.StatusText);
    }
}
