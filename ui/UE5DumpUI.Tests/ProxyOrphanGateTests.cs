using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// AE4–AE7 step 4 — the Proxy Deploy panel's mutual-exclusion gate, on the arm nothing covered.
///
/// <para><c>ProxyDeployConcurrencyTests</c> pins Refresh / UpdateAll / Deploy / Undeploy / Scan
/// against each other. It never mentions orphans, so the **destructive** operation — the one that
/// recycles a chain of files per row — was the only long operation whose gate was untested in both
/// directions.</para>
///
/// <para>⭐ <b>Why this is a test and not a click.</b> The live checklist row has failed twice for
/// the same reason, and the second time the maintainer wrote it down verbatim: 「執行時間太短無法
/// 測試」 — the delete finished before the next button could be pressed, so the gate was never
/// exercised. That is a *timing* problem, not a logic problem, and a stub that parks mid-delete
/// removes the timing entirely. What a live run would still add is that the real buttons are bound
/// to these commands; what it cannot do reliably is hold a delete open.</para>
///
/// <para>ℹ️ The row's own ⚠ — "a confirmation dialog on screen is not a delete running" — is
/// literally true of this code and is pinned below, with the reason it is correct rather than a
/// hole.</para>
/// </summary>
public class ProxyOrphanGateTests
{
    // ── Harness ─────────────────────────────────────────────────────────────

    private static OrphanProxy Row(string dir, string dll = "version.dll")
        => new()
        {
            DllPath = $@"{dir}\{dll}",
            DllDirectory = dir,
            DllNames = dll,
            AuthorisedFiles = new[] { $@"{dir}\{dll}" },
            ChainDirs = new[] { dir },
            TopmostRemovableDir = dir,
            Verdict = OrphanVerdict.Deletable,
            EvidenceText = "test row",
        };

    /// <summary>Answers the two orphan members and can park either of them, so a second
    /// operation can be invoked while the first is genuinely mid-flight.</summary>
    private sealed class GatedOrphanService : IProxyDeployService
    {
        public IReadOnlyList<OrphanProxy> Found = Array.Empty<OrphanProxy>();
        public int FindCalls;
        public int RemoveCalls;

        /// <summary>Set to park every scan until the test releases it.</summary>
        public TaskCompletionSource? ScanGate;
        /// <summary>Runs at the top of each removal — the delete is provably in-flight here.</summary>
        public Action? OnRemove;

        public async Task<IReadOnlyList<OrphanProxy>> FindOrphanProxiesAsync(
            OrphanScanSources sources, IReadOnlySet<string> liveBinariesDirs,
            IProgress<OrphanScanProgress>? progress = null, CancellationToken ct = default)
        {
            FindCalls++;
            if (ScanGate is { } g) await g.Task;
            return Found;
        }

        public Task<OrphanRemovalResult> RemoveOrphanProxyAsync(
            OrphanProxy row, IReadOnlySet<string> liveBinariesDirs, CancellationToken ct = default)
        {
            RemoveCalls++;
            OnRemove?.Invoke();
            return Task.FromResult(new OrphanRemovalResult(true, "1 file moved to the Recycle Bin.", 1, 0));
        }

        private static T No<T>() => throw new NotSupportedException(
            "Not reachable from the orphan gate flow — if this fires, the flow changed.");

        public Task<IReadOnlyList<string>> GetSteamLibraryFoldersAsync(CancellationToken ct = default) => No<Task<IReadOnlyList<string>>>();
        public Task<IReadOnlyList<DetectedGame>> FindUeGamesAsync(IReadOnlyList<string> libraryPaths, CancellationToken ct = default) => No<Task<IReadOnlyList<DetectedGame>>>();
        public Task<IReadOnlyList<DriveDescriptor>> GetScannableDrivesAsync(CancellationToken ct = default) => No<Task<IReadOnlyList<DriveDescriptor>>>();
        public Task<IReadOnlyList<DetectedGame>> FindUeGamesOnDrivesAsync(IReadOnlyList<DriveDescriptor> selectedDrives, IProgress<DriveScanProgress>? progress = null, CancellationToken ct = default) => No<Task<IReadOnlyList<DetectedGame>>>();
        public Task<IReadOnlyList<GameProcessInfo>> ListGameProcessesAsync(CancellationToken ct = default) => No<Task<IReadOnlyList<GameProcessInfo>>>();
        public Task<InjectResult> InjectDllAsync(int pid, string dllPath, CancellationToken ct = default) => No<Task<InjectResult>>();
        public Task<InjectResult> InjectDllElevatedAsync(int pid, string dllPath, CancellationToken ct = default) => No<Task<InjectResult>>();
        public Task RefreshDeployStatusAsync(IList<DetectedGame> games, string sourceDllPath, ProxyType proxyType, IReadOnlySet<string>? preserveBinariesDirs = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> DeployAsync(string sourceDllPath, DetectedGame game, ProxyType proxyType, DeployOptions options = default, CancellationToken ct = default) => No<Task<bool>>();
        public Task<bool> UndeployAsync(DetectedGame game, CancellationToken ct = default) => No<Task<bool>>();
        public Task ApplyProxySuggestionsAsync(IReadOnlyList<DetectedGame> games, IReadOnlyDictionary<string, ProxyType> confirmedByExe, IReadOnlyDictionary<string, ProxyType> rememberedByGame, IReadOnlySet<string> injectedExes, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public bool IsOurProxyDll(string dllPath) => true;
        public string? GetDllVersion(string dllPath) => "1.0.0";
        public bool IsElevated() => false;
    }

    private static async Task<(ProxyDeployViewModel Vm, GatedOrphanService Svc)> ScannedAsync(
        params OrphanProxy[] rows)
    {
        var svc = new GatedOrphanService { Found = rows };
        var vm = new ProxyDeployViewModel(svc, new MockLoggingService())
        {
            ConfirmOrphanRemovalAsync = _ => Task.FromResult(true),
        };
        await vm.ScanOrphansCommand.ExecuteAsync(null);
        Assert.Equal(rows.Length, vm.Orphans.Count);   // the harness itself must work
        return (vm, svc);
    }

    // ── The gate, both directions ───────────────────────────────────────────

    /// <summary>A scan started while a delete is mid-flight must be refused — not queued, not
    /// run. A scan repopulates <c>Orphans</c>, and the delete loop is iterating a snapshot of it
    /// and calling <c>DropOrphanRow</c>, so the two together mutate one collection from two
    /// places.</summary>
    [Fact]
    public async Task AScan_IsRefused_WhileAnOrphanDeleteIsRunning()
    {
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"), Row(@"C:\g\B"));
        foreach (var o in vm.Orphans) o.IsSelected = true;

        int findCallsBefore = svc.FindCalls;
        string? refusedWith = null;
        bool busyDuringDelete = false;

        svc.OnRemove = () =>
        {
            busyDuringDelete = vm.IsBusy;
            // Fire the scan from inside the delete. The stub's scan completes synchronously, so
            // if the gate were open this would run to completion and clear the grid before the
            // delete's next iteration — exactly the race the gate exists for.
            _ = vm.ScanOrphansCommand.ExecuteAsync(null);
            refusedWith = vm.LastOperationResult;
        };

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        Assert.True(busyDuringDelete, "the delete must hold the gate while it runs");
        Assert.Equal(findCallsBefore, svc.FindCalls);          // the scan never reached the service
        Assert.NotNull(refusedWith);
        Assert.Contains("Busy", refusedWith!, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBusy);                                // released on the way out
    }

    /// <summary>...and the other direction, which is the one the live row could never reach: a
    /// delete invoked while a scan is running must not remove anything.</summary>
    [Fact]
    public async Task AnOrphanDelete_IsRefused_WhileAScanIsRunning()
    {
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"));
        vm.Orphans[0].IsSelected = true;

        svc.ScanGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task scanning = vm.ScanOrphansCommand.ExecuteAsync(null);
        Assert.False(scanning.IsCompleted, "the scan must be parked, or this test proves nothing");
        Assert.True(vm.IsBusy);

        // WaitAsync so a regression that queues instead of refusing fails as a timeout rather
        // than hanging the suite.
        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null)
                 .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(0, svc.RemoveCalls);                       // nothing was recycled
        Assert.Contains("Wait for the current operation",
                        vm.LastOperationResult ?? "", StringComparison.OrdinalIgnoreCase);

        svc.ScanGate.SetResult();
        await scanning;
        Assert.False(vm.IsBusy);
    }

    /// <summary>The gate is released even when the delete removes nothing, so one refused or
    /// empty pass cannot wedge the panel for the rest of the session.</summary>
    [Fact]
    public async Task TheGate_IsReleased_WhenNothingWasChecked()
    {
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"));   // nothing selected

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        Assert.Equal(0, svc.RemoveCalls);
        Assert.False(vm.IsBusy);
        // ...and a scan afterwards is NOT refused.
        await vm.ScanOrphansCommand.ExecuteAsync(null);
        Assert.Equal(2, svc.FindCalls);
    }

    // ── The window the checklist warns about ────────────────────────────────

    /// <summary>
    /// ⚠ <b>A confirmation dialog on screen is NOT a delete in progress</b>, and this pins that
    /// as intended rather than as a hole. <c>IsRemovingOrphans</c> is set AFTER
    /// <c>ConfirmOrphanRemovalAsync</c> returns, so while the dialog is up the panel is not busy.
    ///
    /// <para>That is correct for two reasons. The dialog is modal
    /// (<c>OrphanCleanupConfirmDialog.ShowAsync</c> → <c>ShowDialog(owner)</c>), so no other panel
    /// button is reachable while it is open; and holding the exclusive gate across a dialog the
    /// user may sit on indefinitely would lock the panel on a prompt that can still be
    /// cancelled — the "Cancelled — nothing was removed" path must leave nothing behind.</para>
    ///
    /// <para>It is also exactly why the live checklist row says a visible dialog does not count as
    /// having tested the gate.</para>
    /// </summary>
    [Fact]
    public async Task TheConfirmDialog_IsNotTheGate_AndThatIsDeliberate()
    {
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"));
        vm.Orphans[0].IsSelected = true;

        var dialogOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool busyWhileDialogUp = true;
        vm.ConfirmOrphanRemovalAsync = async _ =>
        {
            busyWhileDialogUp = vm.IsBusy;
            dialogOpen.SetResult();
            return true;
        };

        Task deleting = vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);
        await dialogOpen.Task;
        await deleting;

        Assert.False(busyWhileDialogUp);      // not busy yet — the dialog is not the gate
        Assert.Equal(1, svc.RemoveCalls);     // and the delete did then run
        Assert.False(vm.IsBusy);
    }
}
