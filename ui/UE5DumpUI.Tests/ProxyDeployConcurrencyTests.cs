using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// What happens when two Proxy Deploy operations overlap.
///
/// <para><c>IsScanning</c>'s own declaration called itself "the mutual-exclusion guard" and it was
/// not one: three of the eight long operations SET it while six TESTED it, so the guard was
/// one-directional — a scan blocked a deploy, a deploy blocked nothing. Deploy and Undeploy could
/// run over the same <c>Binaries</c> folder at once and both write the single result line, and
/// Scan — which had no entry guard at all — could <c>Games.Clear()</c> the collection Update All
/// was iterating. (audit #5 AE4-AE7)</para>
///
/// <para>MEASURED, not assumed: <c>AsyncRelayCommand</c> reports <c>CanExecute == false</c> while it
/// runs, and Avalonia's Button gates on that — so a command already could not re-enter ITSELF from
/// its own button. What it never covered is two DIFFERENT commands, which is what these tests drive.
/// They call <c>ExecuteAsync</c> directly, which bypasses <c>CanExecute</c> exactly as the
/// property-changed and hotkey paths do.</para>
/// </summary>
public class ProxyDeployConcurrencyTests : IDisposable
{
    public void Dispose() => CleanTemp();

    // ── Harness ─────────────────────────────────────────────────────────────

    /// <summary>A service whose long operations park on a gate the test opens, so a second
    /// operation can be invoked while the first is genuinely mid-flight.</summary>
    private sealed class GatedService : IProxyDeployService
    {
        public readonly TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<string> Calls = new();
        public IReadOnlyList<DetectedGame> Games = Array.Empty<DetectedGame>();
        /// <summary>Raised from inside RefreshDeployStatusAsync, after the gate opens — lets a
        /// test mutate Games at the exact moment Update All is suspended.</summary>
        public Action? DuringDeploy;

        private async Task WaitAsync(string what)
        {
            Calls.Add(what);
            await Gate.Task;
        }

        public Task<IReadOnlyList<string>> GetSteamLibraryFoldersAsync(CancellationToken ct = default)
            => WaitAsync("steam-libs").ContinueWith(_ => (IReadOnlyList<string>)new[] { @"C:\Steam" });

        public Task<IReadOnlyList<DetectedGame>> FindUeGamesAsync(IReadOnlyList<string> libs, CancellationToken ct = default)
            => Task.FromResult(Games);

        /// <summary>Refreshes that are pending, oldest first. A test releases them out of order to
        /// reproduce "whichever continuation resumes last writes the grid".</summary>
        public readonly List<TaskCompletionSource> PendingRefreshes = new();
        /// <summary>Set true to make refreshes park until a test releases them.</summary>
        public bool ParkRefreshes;
        /// <summary>The types actually written to the grid, in the order they landed.</summary>
        public readonly List<ProxyType> Applied = new();

        public async Task RefreshDeployStatusAsync(IList<DetectedGame> games, string sourceDllPath, ProxyType proxyType,
                                                   IReadOnlySet<string>? preserve = null, CancellationToken ct = default)
        {
            Calls.Add($"refresh:{proxyType}");
            if (ParkRefreshes)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                PendingRefreshes.Add(tcs);
                await tcs.Task;
            }
            // Deliberately NOT honouring ct here. The real service checks the token inside its
            // worker loop but applies AFTER the await, so a cancel arriving in that window does
            // not stop the write — which is precisely the gap the post-await re-check closes.
            // A stub that threw on ct would model away the thing under test.
            Applied.Add(proxyType);
        }

        public async Task<bool> DeployAsync(string sourceDllPath, DetectedGame game, ProxyType proxyType,
                                            DeployOptions options = default, CancellationToken ct = default)
        {
            await WaitAsync($"deploy:{game.Name}");
            DuringDeploy?.Invoke();
            return true;
        }

        public async Task<bool> UndeployAsync(DetectedGame game, CancellationToken ct = default)
        {
            await WaitAsync($"undeploy:{game.Name}");
            return true;
        }

        public Task ApplyProxySuggestionsAsync(IReadOnlyList<DetectedGame> games,
            IReadOnlyDictionary<string, ProxyType> confirmedByExe,
            IReadOnlyDictionary<string, ProxyType> rememberedByGame,
            IReadOnlySet<string> injectedExes, bool enabled, CancellationToken ct = default)
            => Task.CompletedTask;

        public bool IsOurProxyDll(string dllPath) => true;

        /// <summary>Source newer than target, or UpdateAllAsync's "already up to date" branch
        /// skips every game and DeployAsync is never called — which would make the two Update All
        /// tests below pass while exercising nothing. (One of them silently did, until the
        /// throwing test showed the loop body was unreachable.)</summary>
        public string? GetDllVersion(string dllPath)
            => dllPath.Contains($"{Path.DirectorySeparatorChar}proxy{Path.DirectorySeparatorChar}",
                                StringComparison.OrdinalIgnoreCase) ? "2.0.0" : "1.0.0";

        private static T No<T>() => throw new NotSupportedException("not reachable from these flows");
        public Task<IReadOnlyList<OrphanProxy>> FindOrphanProxiesAsync(OrphanScanSources s, IReadOnlySet<string> l, IProgress<OrphanScanProgress>? p = null, CancellationToken ct = default) => No<Task<IReadOnlyList<OrphanProxy>>>();
        public Task<OrphanRemovalResult> RemoveOrphanProxyAsync(OrphanProxy r, IReadOnlySet<string> l, CancellationToken ct = default) => No<Task<OrphanRemovalResult>>();
        public Task<IReadOnlyList<DriveDescriptor>> GetScannableDrivesAsync(CancellationToken ct = default) => No<Task<IReadOnlyList<DriveDescriptor>>>();
        public Task<IReadOnlyList<DetectedGame>> FindUeGamesOnDrivesAsync(IReadOnlyList<DriveDescriptor> d, IProgress<DriveScanProgress>? p = null, CancellationToken ct = default) => No<Task<IReadOnlyList<DetectedGame>>>();
        public Task<IReadOnlyList<GameProcessInfo>> ListGameProcessesAsync(CancellationToken ct = default) => No<Task<IReadOnlyList<GameProcessInfo>>>();
        public Task<InjectResult> InjectDllAsync(int pid, string dllPath, CancellationToken ct = default) => No<Task<InjectResult>>();
        public bool IsElevated() => false;
        public Task<InjectResult> InjectDllElevatedAsync(int pid, string dllPath, CancellationToken ct = default) => No<Task<InjectResult>>();
    }

    // ── A real filesystem, because these code paths insist on one ────────────
    //
    // UpdateSourceDllInfo recomputes SourceDllPath as <exeDir>/proxy/<type>.dll on EVERY type
    // change, and UpdateAllAsync resolves its sources the same way and then does File.Exists on
    // each game's target — so neither can be driven with a stub service alone. Setting
    // vm.SourceDllPath by hand does not survive the first radio change.
    //
    // The proxy/ fixture is created next to the test host (build output, wiped by -Target Test)
    // and deliberately NOT deleted: several tests in this class need it, xunit may run them in
    // any order, and every writer writes identical bytes so there is nothing to race over.

    private static readonly string ExeDir =
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    private static readonly Lazy<bool> ProxySources = new(() =>
    {
        string dir = Path.Combine(ExeDir, "proxy");
        Directory.CreateDirectory(dir);
        foreach (var t in Enum.GetValues<ProxyType>())
        {
            string f = Path.Combine(dir, t.GetDllName());
            if (!File.Exists(f)) File.WriteAllBytes(f, new byte[] { 0x4D, 0x5A });
        }
        return true;
    });

    private readonly List<string> _tempDirs = new();

    /// <summary>A Binaries dir on disk. <paramref name="deployed"/> puts a proxy DLL in it, which
    /// is what makes UpdateAllAsync consider the game at all.</summary>
    private DetectedGame Game(string name, bool deployed = false)
    {
        string root = Path.Combine(Path.GetTempPath(), "ue5-proxy-conc", Guid.NewGuid().ToString("N"));
        string bin = Path.Combine(root, name, "Binaries", "Win64");
        Directory.CreateDirectory(bin);
        _tempDirs.Add(root);
        if (deployed)
            File.WriteAllBytes(Path.Combine(bin, ProxyType.Version.GetDllName()), new byte[] { 0x4D, 0x5A });
        return new DetectedGame
        {
            Name = name,
            BinariesDir = bin,
            ExePath = Path.Combine(bin, $"{name}.exe"),
            IsSelected = true,
        };
    }

    private void CleanTemp()
    {
        foreach (var d in _tempDirs)
            try { Directory.Delete(d, recursive: true); } catch { /* best effort */ }
        _tempDirs.Clear();
    }

    /// <summary>A VM with two selected games whose Binaries dirs exist on disk.</summary>
    private (ProxyDeployViewModel Vm, GatedService Svc) Ready(bool deployed = false)
    {
        _ = ProxySources.Value;
        var svc = new GatedService();
        var vm = new ProxyDeployViewModel(svc, new MockLoggingService());
        vm.Games.Add(Game("A", deployed));
        vm.Games.Add(Game("B", deployed));
        return (vm, svc);
    }

    /// <summary>Await something that MUST complete promptly, and fail fast if it does not.
    ///
    /// <para>Every "the second operation is refused" assertion below awaits a command that, if the
    /// gate regresses, stops being refused and instead parks on the same <see cref="GatedService"/>
    /// gate as the first one — which the test only opens afterwards. A bare await then deadlocks
    /// the whole suite instead of failing, which is what the first negative-control run did. A
    /// concurrency test that can hang forever when the fix regresses is not a usable test.</para>
    /// </summary>
    private static Task Refused(Task t) => t.WaitAsync(TimeSpan.FromSeconds(10));

    // ── AE6: two DIFFERENT commands over the same folder ─────────────────────

    [Fact]
    public async Task Undeploy_IsRefused_WhileDeployIsRunning()
    {
        var (vm, svc) = Ready();

        var deploy = vm.DeploySelectedCommand.ExecuteAsync(null);
        Assert.True(vm.IsScanning);                    // the gate is held

        await Refused(vm.UndeploySelectedCommand.ExecuteAsync(null));

        // The refusal is what matters; the message naming the holder is the part that makes it
        // actionable (it used to say "Wait for scan to finish" when no scan was running).
        Assert.Contains("Deploy", vm.LastOperationResult);
        Assert.DoesNotContain(svc.Calls, c => c.StartsWith("undeploy:", StringComparison.Ordinal));

        svc.Gate.SetResult();
        await deploy;
        Assert.False(vm.IsScanning);                   // and it releases
    }

    [Fact]
    public async Task Deploy_IsRefused_WhileUndeployIsRunning()
    {
        var (vm, svc) = Ready();

        var undeploy = vm.UndeploySelectedCommand.ExecuteAsync(null);
        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Contains("Remove", vm.LastOperationResult);
        Assert.DoesNotContain(svc.Calls, c => c.StartsWith("deploy:", StringComparison.Ordinal));

        svc.Gate.SetResult();
        await undeploy;
    }

    [Fact]
    public async Task TheRefusedOperation_DoesNotOverwriteTheRunningOnesResultLine()
    {
        // Both write LastOperationResult via the same SetOperationResult, so before the gate the
        // later finisher's line replaced the earlier's and one operation's outcome vanished.
        var (vm, svc) = Ready();

        var deploy = vm.DeploySelectedCommand.ExecuteAsync(null);
        await Refused(vm.UpdateAllCommand.ExecuteAsync(null));

        svc.Gate.SetResult();
        await deploy;

        Assert.Contains("Deployed:", vm.LastOperationResult);   // the winner's tally survives
    }

    // ── AE5: the operations that tested the flag but never set it ────────────

    [Theory]
    [InlineData("Refresh")]
    [InlineData("UpdateAll")]
    [InlineData("Deploy")]
    [InlineData("Undeploy")]
    public async Task EveryLongOperation_HoldsTheGate_NotJustTheScans(string which)
    {
        // Every case must reach an await INSIDE the gate, or the mid-flight assertion below is
        // unreachable and the test decays into `Assert.False(IsScanning)` after the fact — which
        // a build that never sets the flag passes just as happily. It used to be exactly that:
        // the assertion was guarded by `if (!running.IsCompleted)`, and Refresh and UpdateAll
        // both completed synchronously against the default harness, so the two commands AE5 was
        // ABOUT were the two this test silently skipped. Measured 2026-08-24 by running the real
        // Update All over 9 stale proxies: it finished inside one screenshot round-trip, so the
        // panel's bar is not observable by eye for these either — this assertion is the only
        // thing standing behind "the busy indicator appears for them".
        //   * UpdateAll — needs a proxy ALREADY on disk, else every game hits the
        //                 `!File.Exists(targetDll)` continue and DeployAsync is never called.
        //   * Refresh   — makes no gated service call of its own; park the status refresh.
        var (vm, svc) = Ready(deployed: which == "UpdateAll");
        if (which == "Refresh") svc.ParkRefreshes = true;

        Task running = which switch
        {
            "Refresh"  => vm.RefreshCommand.ExecuteAsync(null),
            "UpdateAll" => vm.UpdateAllCommand.ExecuteAsync(null),
            "Deploy"   => vm.DeploySelectedCommand.ExecuteAsync(null),
            _          => vm.UndeploySelectedCommand.ExecuteAsync(null),
        };

        Assert.False(running.IsCompleted,
            $"{which} never suspended, so the gate was never observed being HELD — the assertion below would be vacuous");
        Assert.True(vm.IsScanning,
            $"{which} ran without holding the gate; the panel's progress bar binds to this flag, so it would stay invisible");

        svc.Gate.SetResult();
        foreach (var pending in svc.PendingRefreshes) pending.TrySetResult();
        await running;
        Assert.False(vm.IsScanning);   // released on every path
    }

    [Fact]
    public async Task AScan_IsRefused_WhileADeployIsRunning()
    {
        // The direction that did not exist before: a deploy blocked nothing, so Scan could start
        // and Games.Clear() under it. Scan had no entry guard of its own at all.
        var (vm, svc) = Ready();

        var deploy = vm.DeploySelectedCommand.ExecuteAsync(null);
        await Refused(vm.ScanCommand.ExecuteAsync(null));

        Assert.DoesNotContain(svc.Calls, c => c == "steam-libs");
        Assert.Equal(2, vm.Games.Count);              // nothing cleared the grid

        svc.Gate.SetResult();
        await deploy;
    }

    // ── AE7: Update All over a collection something else can mutate ──────────

    [Fact]
    public async Task UpdateAll_SurvivesGamesBeingReplacedMidLoop()
    {
        // The gate now stops Scan overlapping Update All, so this drives the mutation directly —
        // the snapshot is what makes the loop survive it either way. Without both, the enumerator
        // throws InvalidOperationException, and UpdateAllAsync had no catch: on the button path a
        // faulted AsyncRelayCommand task is rethrown onto the UI thread.
        var (vm, svc) = Ready(deployed: true);
        svc.DuringDeploy = () => { vm.Games.Clear(); vm.Games.Add(Game("C")); };

        var update = vm.UpdateAllCommand.ExecuteAsync(null);
        svc.Gate.SetResult();

        await update;                                  // must not throw
        // Prove the loop body actually ran: without this the test passes when UpdateAllAsync
        // skips every game, which is how it was silently vacuous on the first attempt.
        Assert.Contains(svc.Calls, c => c.StartsWith("deploy:", StringComparison.Ordinal));
        // Not merely "did not throw": the catch would satisfy that while the loop aborted
        // half-way. The success tally is the only wording that means the loop RAN TO THE END,
        // so it is what discriminates the snapshot from the catch that backs it up.
        Assert.StartsWith("Updated:", vm.LastOperationResult);
    }

    [Fact]
    public async Task UpdateAll_ReportsATally_WhenTheServiceThrows()
    {
        var (vm, svc) = Ready(deployed: true);
        svc.DuringDeploy = () => throw new InvalidOperationException("boom");

        var update = vm.UpdateAllCommand.ExecuteAsync(null);
        svc.Gate.SetResult();
        await update;                                  // no rethrow onto the caller

        Assert.Contains("failed", vm.LastOperationResult, StringComparison.OrdinalIgnoreCase);
    }

    // ── AE4: the proxy-type radio race ───────────────────────────────────────

    [Fact]
    public async Task RapidTypeChanges_LeaveTheGridOnTheTypeTheRadioShows()
    {
        // Two quick clicks start two refreshes over the same Games with nothing throttling them
        // (the radios carry no IsEnabled binding, and this is a property-changed handler, not a
        // command, so CanExecute never applies). Whichever CONTINUATION resumes last writes the
        // grid — so this releases them in REVERSE order, the case where the stale one wins.
        var (vm, svc) = Ready();
        svc.ParkRefreshes = true;
        svc.Calls.Clear();

        vm.SelectedProxyType = ProxyType.Dinput8;
        vm.SelectedProxyType = ProxyType.Dxgi;
        Assert.Equal(2, svc.PendingRefreshes.Count);        // both genuinely in flight

        // Newest completes first, then the superseded one lands on top of it.
        svc.PendingRefreshes[1].SetResult();
        svc.PendingRefreshes[0].SetResult();

        // Drain, releasing anything that parks afterwards — the CORRECTION is itself a refresh,
        // so a test that only released the first two would deadlock the fix it is measuring.
        for (int i = 0; i < 200; i++)
        {
            await Task.Yield();
            foreach (var t in svc.PendingRefreshes.ToList()) t.TrySetResult();
            if (svc.Applied.LastOrDefault() == ProxyType.Dxgi && svc.PendingRefreshes.Count >= 3) break;
        }

        // The contract is not "the newest one runs" — it is that the grid ends up showing the
        // type the radio shows. A stale refresh landing last must be corrected, not ignored.
        Assert.Equal(ProxyType.Dxgi, svc.Applied[^1]);
    }

}
