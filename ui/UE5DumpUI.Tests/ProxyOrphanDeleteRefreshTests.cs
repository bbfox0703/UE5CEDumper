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
/// What the leftover list shows AFTER a delete pass.
///
/// <para>The list used to keep every row it had scanned, so a cleaned row stayed on screen still
/// promising, in the future tense, to "Recycle version.dll, then remove up to 3 folder(s)" — for a
/// file that was already in the Recycle Bin. The only way to see the truth was to press
/// "Find leftovers" again by hand.</para>
///
/// <para>Cleaned rows are now dropped, which is EXACT rather than approximate: a successful removal
/// means the proxy DLL is off disk, and the scan enumerates rows by that DLL, so a re-scan could not
/// have found the row either. Failures stay — with their reason, which a re-scan would have
/// blanked — and everything is unchecked so a retry has to be asked for.</para>
///
/// <para>Driven through the REAL scan → delete path (a stub service, not a hand-populated
/// collection) so the per-row PropertyChanged wiring the scan installs is exercised too.</para>
/// </summary>
public class ProxyOrphanDeleteRefreshTests
{
    // ── Harness ─────────────────────────────────────────────────────────────

    private static OrphanProxy Row(string dir, string dll = "version.dll",
                                   OrphanVerdict verdict = OrphanVerdict.Deletable)
        => new()
        {
            DllPath = $@"{dir}\{dll}",
            DllDirectory = dir,
            DllNames = dll,
            AuthorisedFiles = new[] { $@"{dir}\{dll}" },
            ChainDirs = new[] { dir },
            TopmostRemovableDir = dir,
            Verdict = verdict,
            EvidenceText = "test row",
        };

    /// <summary>A VM whose scan returns <paramref name="rows"/> and whose removal answers per path.</summary>
    private static async Task<(ProxyDeployViewModel Vm, StubProxyDeployService Svc)> ScannedAsync(
        params OrphanProxy[] rows)
    {
        var svc = new StubProxyDeployService { Found = rows };
        var vm = new ProxyDeployViewModel(svc, new MockLoggingService())
        {
            // Mandatory: with no delegate the command refuses rather than deleting silently.
            ConfirmOrphanRemovalAsync = _ => Task.FromResult(true),
        };
        await vm.ScanOrphansCommand.ExecuteAsync(null);
        return (vm, svc);
    }

    private static string[] Dirs(ProxyDeployViewModel vm)
        => vm.Orphans.Select(o => o.DllDirectory).ToArray();

    // ── The regression ──────────────────────────────────────────────────────

    // ── [ORPHANCANCEL-2026-08-20] an interrupted row must still be accounted for ──
    //
    // Cancellation used to escape as an OperationCanceledException from inside the row, so
    // everything after that await was skipped: the tally, the untick, and the drop. A real run
    // logged THREE "Recycled leftover proxy" lines under a summary that said two, and left the
    // third tree half-pruned with its DLL already in the Recycle Bin and its folders intact —
    // invisible, because the row still advertised the work in the future tense.
    //
    // ⭐ Audit #4's root cause verbatim: the report and the reality computed by different paths.

    [Fact]
    public async Task AnInterruptedRow_StillCountsWhatItDid()
    {
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"), Row(@"C:\g\B"));
        // B recycles its file, then sees the cancel.
        svc.CancelPartwayFor.Add(@"C:\g\B\version.dll", (files: 1, dirs: 0));
        foreach (var o in vm.Orphans) o.IsSelected = true;

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        // A completed (1 file) + B's interrupted 1 = 2. Reporting 1 is the defect.
        Assert.Contains("2 file(s) recycled", vm.LastOperationResult);
        Assert.Contains("cancelled", vm.LastOperationResult, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnInterruptedRow_StaysOnTheList_Unticked_AndSaysWhatHappened()
    {
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"), Row(@"C:\g\B"));
        svc.CancelPartwayFor.Add(@"C:\g\B\version.dll", (files: 1, dirs: 0));
        foreach (var o in vm.Orphans) o.IsSelected = true;

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        // A is gone (completed); B stays, because its folder chain is half-pruned and that is
        // exactly what the user needs to still be able to see.
        var left = Assert.Single(vm.Orphans);
        Assert.Equal(@"C:\g\B", left.DllDirectory);

        // Unticked, per the loop's own rule: "a pass is over when it is over". A row left ticked
        // would re-submit itself under a button still labelled "Delete checked (1)".
        Assert.False(left.IsSelected);

        // And the status has to replace the future-tense promise about a file already recycled.
        Assert.Contains("Interrupted", left.StatusText);
    }

    [Fact]
    public async Task AnInterruptedRow_IsNotCountedAsAFailure()
    {
        // Cancelling is the user's own decision, not an error, and calling it one would send them
        // looking for a locked file that does not exist.
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"));
        svc.CancelPartwayFor.Add(@"C:\g\A\version.dll", (files: 1, dirs: 2));
        vm.Orphans[0].IsSelected = true;

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        Assert.DoesNotContain("failed", vm.LastOperationResult, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 file(s) recycled", vm.LastOperationResult);
        Assert.Contains("2 folder(s) removed", vm.LastOperationResult);
    }

    [Fact]
    public async Task APartlyLockedRow_AlsoCountsWhatItRecycled()
    {
        // The same defect without any cancel: the tally lived inside `if (result.Success)`, so a
        // row that recycled one of its two DLLs and then hit a lock reported nothing at all.
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"));
        svc.PartialFailFor.Add(@"C:\g\A\version.dll",
            (why: "1 file moved to the Recycle Bin; dinput8.dll is in use.", files: 1, dirs: 0));
        vm.Orphans[0].IsSelected = true;

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        Assert.Contains("1 file(s) recycled", vm.LastOperationResult);
    }

    [Fact]
    public async Task ACleanedRow_LeavesTheList()
    {
        var (vm, _) = await ScannedAsync(Row(@"C:\g\A"), Row(@"C:\g\B"));
        vm.Orphans[0].IsSelected = true;

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        // Not "greyed out", not "marked done" — gone, because it is gone from disk.
        Assert.Equal(new[] { @"C:\g\B" }, Dirs(vm));
    }

    [Fact]
    public async Task CleaningEverything_EmptiesTheList_AndSaysSoWhereTheRowsWere()
    {
        var (vm, _) = await ScannedAsync(Row(@"C:\g\A"), Row(@"C:\g\B"));
        foreach (var o in vm.Orphans) o.IsSelected = true;

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        Assert.Empty(vm.Orphans);
        // OrphanScanRan stays true, so the panel's green "none found" line takes over from the list
        // instead of leaving an unexplained blank where two rows used to be.
        Assert.True(vm.ShowNoOrphansFound);
    }

    [Fact]
    public async Task AFailedRow_StaysWithItsReason()
    {
        // The reason IS the output of a failed delete — "in use by a running program" tells the user
        // to close the game. An automatic re-scan would re-find this row with a blank status, which
        // is exactly why the refresh is done by dropping rows rather than by re-scanning.
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"), Row(@"C:\g\B"));
        svc.FailFor.Add(@"C:\g\A\version.dll", "In use by a running program: version.dll.");
        foreach (var o in vm.Orphans) o.IsSelected = true;

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        var left = Assert.Single(vm.Orphans);
        Assert.Equal(@"C:\g\A", left.DllDirectory);
        Assert.Contains("In use by a running program", left.StatusText);
        Assert.True(left.StatusIsError);
        Assert.False(vm.ShowNoOrphansFound);
    }

    // ── Unchecked all ───────────────────────────────────────────────────────

    [Fact]
    public async Task AFailedRow_IsLeftUnchecked_SoTheNextClickCannotResubmitIt()
    {
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"));
        svc.FailFor.Add(@"C:\g\A\version.dll", "Read-only, left alone deliberately.");
        vm.Orphans[0].IsSelected = true;

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        Assert.False(vm.Orphans[0].IsSelected);
        Assert.Equal(0, vm.SelectedOrphanCount);
        Assert.False(vm.HasOrphanSelection);   // the Delete button greys out: the pass is over
    }

    // NOTE: the "Delete checked (N)" LABEL is deliberately not asserted here. It resolves through
    // the Avalonia resource dictionary (Res.Format), which is not loaded in a headless run, so it
    // returns "" and the test would be measuring the resource system rather than this behaviour.
    // SelectedOrphanCount / HasOrphanSelection above are the same fact, testably.

    // ── The tally the vanished rows took with them ──────────────────────────

    [Fact]
    public async Task TheSummaryCarriesTheFileAndFolderTotals()
    {
        // The per-row success messages are the one thing dropping rows costs, so the totals have to
        // survive on screen — otherwise "Cleaned 2 of 2" is all that is left of a folder prune.
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"), Row(@"C:\g\B"));
        svc.DirsRemovedPerRow = 3;
        foreach (var o in vm.Orphans) o.IsSelected = true;

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        Assert.Contains("Cleaned 2 of 2", vm.LastOperationResult);
        Assert.Contains("2 file(s) recycled", vm.LastOperationResult);
        Assert.Contains("6 folder(s) removed", vm.LastOperationResult);
    }

    [Fact]
    public async Task TheSummarySaysWhereTheFailuresWent()
    {
        // Without this an emptied-but-for-one list reads as "everything worked".
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"), Row(@"C:\g\B"));
        svc.FailFor.Add(@"C:\g\A\version.dll", "Could not remove: version.dll.");
        foreach (var o in vm.Orphans) o.IsSelected = true;

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        Assert.Contains("Cleaned 1 of 2", vm.LastOperationResult);
        Assert.Contains("1 still listed", vm.LastOperationResult);
    }

    // ── Nothing else regressed ──────────────────────────────────────────────

    [Fact]
    public async Task AnUncheckedRow_IsUntouched()
    {
        var (vm, _) = await ScannedAsync(Row(@"C:\g\A"), Row(@"C:\g\B"));
        vm.Orphans[1].IsSelected = true;

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        var left = Assert.Single(vm.Orphans);
        Assert.Equal(@"C:\g\A", left.DllDirectory);
        Assert.Equal("", left.StatusText);      // never attempted, so nothing to report
    }

    [Fact]
    public async Task ADroppedRow_NoLongerDrivesTheSelectionCount()
    {
        // DropOrphanRow unsubscribes before removing. If it did not, the row would still be able to
        // push notifications into a ViewModel that no longer lists it — the leak the scan path
        // already guards against when it clears the collection.
        var (vm, _) = await ScannedAsync(Row(@"C:\g\A"));
        var row = vm.Orphans[0];
        row.IsSelected = true;

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);
        Assert.Empty(vm.Orphans);

        row.IsSelected = true;                  // a detached row talking to nobody
        Assert.Equal(0, vm.SelectedOrphanCount);
        Assert.False(vm.HasOrphanSelection);
    }

    [Fact]
    public async Task RefusingTheConfirmation_ChangesNothing()
    {
        var svc = new StubProxyDeployService { Found = new[] { Row(@"C:\g\A") } };
        var vm = new ProxyDeployViewModel(svc, new MockLoggingService())
        {
            ConfirmOrphanRemovalAsync = _ => Task.FromResult(false),
        };
        await vm.ScanOrphansCommand.ExecuteAsync(null);
        vm.Orphans[0].IsSelected = true;

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        Assert.Single(vm.Orphans);
        Assert.True(vm.Orphans[0].IsSelected);   // still checked: nothing was attempted
        Assert.Equal(0, svc.RemoveCalls);
    }

    // ── Stub ────────────────────────────────────────────────────────────────

    // ── AE20: the delete could not be stopped ───────────────────────────────

    /// <summary>
    /// <c>DeleteSelectedOrphansAsync</c> takes a <c>CancellationToken</c>, re-checks it
    /// between rows and carries a whole cancelled-reporting path — and nothing in the
    /// app could call <c>Cancel()</c> on it, because the command did not declare
    /// <c>IncludeCancelCommand</c> and no other caller existed. Five of the panel's
    /// seven token-taking commands were in that state, this one destructively.
    ///
    /// <para>NEGATIVE CONTROL: remove <c>CancelOperationCommand</c> (or the
    /// <c>DeleteSelectedOrphansCommand</c> entry from its fan-out list) and the loop
    /// runs to completion — <c>RemoveCalls</c> is 2 and the result line reports a
    /// finished pass.</para>
    /// </summary>
    [Fact]
    public async Task AE20_TheDestructiveDelete_CanBeCancelledMidRun()
    {
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"), Row(@"C:\g\B"));
        foreach (var o in vm.Orphans) o.IsSelected = true;
        svc.OnRemove = () => vm.CancelOperationCommand.Execute(null);

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        Assert.Equal(1, svc.RemoveCalls);   // the second row never started
        // And the tally still reports what DID happen — a cancel that discarded it
        // would hide a half-pruned chain.
        Assert.Contains("cancel", (vm.LastOperationResult ?? "").ToLowerInvariant());
    }

    /// <summary>Negative control: with nobody cancelling, the same pass completes.
    /// Without this the test above would also pass against a delete that simply
    /// stopped working.</summary>
    [Fact]
    public async Task AE20_WithoutACancel_TheSamePassCompletes()
    {
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"), Row(@"C:\g\B"));
        foreach (var o in vm.Orphans) o.IsSelected = true;

        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        Assert.Equal(2, svc.RemoveCalls);
        Assert.DoesNotContain("cancel", (vm.LastOperationResult ?? "").ToLowerInvariant());
    }

    /// <summary>The Cancel button's gate is the panel's own exclusive-gate predicate,
    /// so it is enabled exactly while something cancellable is running.</summary>
    [Fact]
    public async Task AE20_IsBusy_TracksTheExclusiveGate()
    {
        var (vm, svc) = await ScannedAsync(Row(@"C:\g\A"));
        Assert.False(vm.IsBusy);

        bool busyDuringDelete = false;
        vm.Orphans[0].IsSelected = true;
        svc.OnRemove = () => busyDuringDelete = vm.IsBusy;
        await vm.DeleteSelectedOrphansCommand.ExecuteAsync(null);

        Assert.True(busyDuringDelete);
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// Answers the two orphan members; everything else on the interface is unreachable from this
    /// flow and throws rather than returning a plausible default that could mask a wrong call.
    /// </summary>
    private sealed class StubProxyDeployService : IProxyDeployService
    {
        public IReadOnlyList<OrphanProxy> Found = System.Array.Empty<OrphanProxy>();
        public readonly Dictionary<string, string> FailFor = new(System.StringComparer.OrdinalIgnoreCase);
        /// <summary>DllPath -> what the row managed before it was interrupted. [ORPHANCANCEL-2026-08-20]</summary>
        public readonly Dictionary<string, (int files, int dirs)> CancelPartwayFor = new();
        /// <summary>DllPath -> a row that recycled something and THEN failed. Same accounting hole, no cancel involved.</summary>
        public readonly Dictionary<string, (string why, int files, int dirs)> PartialFailFor = new();
        public int DirsRemovedPerRow;
        public int RemoveCalls;
        /// <summary>Runs at the top of each removal — the only moment from which a test
        /// can cancel the loop the way a user's Cancel click does (audit #5 AE20).</summary>
        public System.Action? OnRemove;

        public Task<IReadOnlyList<OrphanProxy>> FindOrphanProxiesAsync(
            OrphanScanSources sources, IReadOnlySet<string> liveBinariesDirs,
            IProgress<OrphanScanProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(Found);

        public Task<OrphanRemovalResult> RemoveOrphanProxyAsync(
            OrphanProxy row, IReadOnlySet<string> liveBinariesDirs, CancellationToken ct = default)
        {
            RemoveCalls++;
            OnRemove?.Invoke();
            // [ORPHANCANCEL-2026-08-20] A row can be interrupted PART-WAY: it recycled its file and
            // then saw the cancel. The partial counts are the whole point — they used to be thrown
            // away with the exception.
            if (PartialFailFor.TryGetValue(row.DllPath, out var pf))
                return Task.FromResult(new OrphanRemovalResult(false, pf.why, pf.files, pf.dirs));
            if (CancelPartwayFor.TryGetValue(row.DllPath, out var partial))
                return Task.FromResult(new OrphanRemovalResult(
                    false, "Interrupted — 1 file(s) recycled, 0 folder(s) removed; the rest was "
                         + "left in place", partial.files, partial.dirs, Cancelled: true));
            return Task.FromResult(FailFor.TryGetValue(row.DllPath, out var why)
                ? new OrphanRemovalResult(false, why, 0, 0)
                : new OrphanRemovalResult(true, "1 file moved to the Recycle Bin.", 1, DirsRemovedPerRow));
        }

        private static T No<T>() => throw new System.NotSupportedException(
            "Not reachable from the orphan delete flow — if this fires, the flow changed.");

        public Task<IReadOnlyList<string>> GetSteamLibraryFoldersAsync(CancellationToken ct = default) => No<Task<IReadOnlyList<string>>>();
        public Task<IReadOnlyList<DetectedGame>> FindUeGamesAsync(IReadOnlyList<string> libraryPaths, CancellationToken ct = default) => No<Task<IReadOnlyList<DetectedGame>>>();
        public Task<IReadOnlyList<DriveDescriptor>> GetScannableDrivesAsync(CancellationToken ct = default) => No<Task<IReadOnlyList<DriveDescriptor>>>();
        public Task<IReadOnlyList<DetectedGame>> FindUeGamesOnDrivesAsync(IReadOnlyList<DriveDescriptor> selectedDrives, IProgress<DriveScanProgress>? progress = null, CancellationToken ct = default) => No<Task<IReadOnlyList<DetectedGame>>>();
        public Task<IReadOnlyList<GameProcessInfo>> ListGameProcessesAsync(CancellationToken ct = default) => No<Task<IReadOnlyList<GameProcessInfo>>>();
        public Task<InjectResult> InjectDllAsync(int pid, string dllPath, CancellationToken ct = default) => No<Task<InjectResult>>();
        public bool IsElevated() => false;
        public Task<InjectResult> InjectDllElevatedAsync(int pid, string dllPath, CancellationToken ct = default) => No<Task<InjectResult>>();
        public Task RefreshDeployStatusAsync(IList<DetectedGame> games, string sourceDllPath, ProxyType proxyType, IReadOnlySet<string>? preserveBinariesDirs = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> DeployAsync(string sourceDllPath, DetectedGame game, ProxyType proxyType, DeployOptions options = default, CancellationToken ct = default) => No<Task<bool>>();
        public Task<bool> UndeployAsync(DetectedGame game, CancellationToken ct = default) => No<Task<bool>>();
        public Task ApplyProxySuggestionsAsync(IReadOnlyList<DetectedGame> games, IReadOnlyDictionary<string, ProxyType> confirmedByExe, IReadOnlyDictionary<string, ProxyType> rememberedByGame, IReadOnlySet<string> injectedExes, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public bool IsOurProxyDll(string dllPath) => true;
        public string? GetDllVersion(string dllPath) => null;
    }
}
