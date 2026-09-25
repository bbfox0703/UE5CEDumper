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
        /// <summary>Raised from inside UndeployAsync, after the gate opens.</summary>
        public Action? DuringUndeploy;
        /// <summary>Opt-in: make RefreshDeployStatusAsync honour a cancelled token, as the REAL
        /// service does inside its worker loop. Off by default -- see the note in the method.</summary>
        public bool ThrowOnCancelledRefresh;

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
        /// <summary>Opt-in: clear StatusDetail on every row outside the preserve set, as the REAL refresh does
        /// (it rewrites the row from disk). [PROXY-DOUBLE-GUARD] A note written before the refresh would not
        /// survive it.</summary>
        public bool ClearDetailsOnRefresh;
        /// <summary>What the refresh writes into Details for a row it rewrites (null = nothing), e.g. the real
        /// refresh's "Multiple proxy DLLs deployed …" warning for a doubled folder.</summary>
        public Func<DetectedGame, string?>? RefreshDetail;
        /// <summary>The preserve set each refresh was given: a row in it is NOT rewritten from disk.</summary>
        public readonly List<HashSet<string>> Preserves = new();
        /// <summary>Overrides DeployAsync's result per game (null = success).</summary>
        public Func<DetectedGame, bool>? DeployResult;
        /// <summary>The import-risk note a successful DeployAsync writes into Details (the real one:
        /// ProxyImportAnalyzer.DescribeDeployAdvisory), or null for none.</summary>
        public Func<DetectedGame, ProxyType, string?>? DeployNote;
        /// <summary>Overrides DeployAsync's result per (game, type) -- a doubled folder can fail one type and not the other.</summary>
        public Func<DetectedGame, ProxyType, bool>? DeployResultByType;
        /// <summary>Opt-in: DeployAsync writes Status / StatusDetail as the real service does.</summary>
        public bool SimulateStatus;
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
            if (ThrowOnCancelledRefresh) ct.ThrowIfCancellationRequested();
            Preserves.Add(preserve is null ? new HashSet<string>() : new HashSet<string>(preserve));
            if (ClearDetailsOnRefresh)
                foreach (var g in games)
                    if (preserve is null || !preserve.Contains(g.BinariesDir)) g.StatusDetail = RefreshDetail?.Invoke(g);
            Applied.Add(proxyType);
        }

        /// <summary>Every deploy the VM asked for, with the options it passed. [PROXY-FORCE-UPDATEALL] Kept apart
        /// from <see cref="Calls"/>, whose exact "deploy:A" strings other tests assert.</summary>
        public readonly List<(string Game, ProxyType Type, DeployOptions Options, string Source)> Deploys = new();
        /// <summary>Overrides <see cref="GetDllVersion"/> (null = the default newer-source pair).</summary>
        public Func<string, string?>? VersionOf;
        /// <summary>Overrides <see cref="IsOurProxyDll"/> (null = every DLL is ours).</summary>
        public Func<string, bool>? IsOurs;

        public async Task<bool> DeployAsync(string sourceDllPath, DetectedGame game, ProxyType proxyType,
                                            DeployOptions options = default, CancellationToken ct = default)
        {
            Deploys.Add((game.Name, proxyType, options, sourceDllPath));
            await WaitAsync($"deploy:{game.Name}");
            DuringDeploy?.Invoke();
            bool ok = DeployResultByType?.Invoke(game, proxyType) ?? DeployResult?.Invoke(game) ?? true;
            if (SimulateStatus)
            {
                // As the real service's ApplyStatus: a success writes DeployedCurrent, a locked target ErrorLocked.
                game.Status = ok ? ProxyDeployStatus.DeployedCurrent : ProxyDeployStatus.ErrorLocked;
                game.StatusDetail = ok ? null : "Target in use (game running?) or write-protected";
            }
            // As the real service: a successful deploy writes its one-shot import-risk note (or nothing) into Details.
            if (ok && DeployNote != null) game.StatusDetail = DeployNote(game, proxyType);
            return ok;
        }

        public async Task<bool> UndeployAsync(DetectedGame game, CancellationToken ct = default)
        {
            await WaitAsync($"undeploy:{game.Name}");
            DuringUndeploy?.Invoke();
            return true;
        }

        public Task ApplyProxySuggestionsAsync(IReadOnlyList<DetectedGame> games,
            IReadOnlyDictionary<string, ProxyType> confirmedByExe,
            IReadOnlyDictionary<string, ProxyType> rememberedByGame,
            IReadOnlySet<string> injectedExes, bool enabled, CancellationToken ct = default)
            => Task.CompletedTask;

        public bool IsOurProxyDll(string dllPath) => IsOurs?.Invoke(dllPath) ?? true;
        /// <summary>[PROXY-PRODUCTNAME-UNREADABLE] Which proxy-named files cannot be read (null = none).</summary>
        public Func<string, bool>? Unreadable;
        public bool IsUnreadableDll(string dllPath) => Unreadable?.Invoke(dllPath) ?? false;

        /// <summary>Source newer than target, or -- with Force Overwrite off, the default -- UpdateAllAsync's
        /// "already up to date" branch skips every game and DeployAsync is never called — which would make the two Update All
        /// tests below pass while exercising nothing. (One of them silently did, until the
        /// throwing test showed the loop body was unreachable.)</summary>
        public string? GetDllVersion(string dllPath)
            => VersionOf is { } v ? v(dllPath) : dllPath.Contains($"{Path.DirectorySeparatorChar}proxy{Path.DirectorySeparatorChar}",
                                StringComparison.OrdinalIgnoreCase) ? "2.0.0" : "1.0.0";

        private static T No<T>() => throw new NotSupportedException("not reachable from these flows");
        public Task<IReadOnlyList<OrphanProxy>> FindOrphanProxiesAsync(OrphanScanSources s, IReadOnlySet<string> l, IProgress<OrphanScanProgress>? p = null, CancellationToken ct = default) => No<Task<IReadOnlyList<OrphanProxy>>>();
        public Task<OrphanRemovalResult> RemoveOrphanProxyAsync(OrphanProxy r, IReadOnlySet<string> l, CancellationToken ct = default) => No<Task<OrphanRemovalResult>>();
        public Task<IReadOnlyList<DriveDescriptor>> GetScannableDrivesAsync(CancellationToken ct = default) => No<Task<IReadOnlyList<DriveDescriptor>>>();
        public Task<IReadOnlyList<DetectedGame>> FindUeGamesOnDrivesAsync(IReadOnlyList<DriveDescriptor> d, IProgress<DriveScanProgress>? p = null, IReadOnlyList<string>? excludedFolderNames = null, CancellationToken ct = default) => No<Task<IReadOnlyList<DetectedGame>>>();
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

    /// <summary>A Binaries dir holding exactly the given proxy DLLs (all "ours" unless the stub's IsOurs says
    /// otherwise). [PROXY-DOUBLE-GUARD]</summary>
    private DetectedGame Game(string name, params ProxyType[] deployed)
    {
        var g = Game(name, deployed: false);
        foreach (var t in deployed)
            File.WriteAllBytes(Path.Combine(g.BinariesDir, t.GetDllName()), new byte[] { 0x4D, 0x5A });
        return g;
    }

    private (ProxyDeployViewModel Vm, GatedService Svc) ReadyWith(params DetectedGame[] games)
    {
        _ = ProxySources.Value;
        var svc = new GatedService { ClearDetailsOnRefresh = true };
        var vm = new ProxyDeployViewModel(svc, new MockLoggingService());
        foreach (var g in games) vm.Games.Add(g);
        return (vm, svc);
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

    // ── [A3-DEPLOY-CANCEL]: "Cancel operation" during Deploy / Undeploy / Refresh ─────────
    //
    // AE20 made Cancel reach all nine commands, but DeploySelectedAsync and UndeploySelectedAsync
    // had no try/catch: the cancel rethrew out of the AsyncRelayCommand onto the dispatcher, where
    // DispatcherFaultGuard refuses to swallow it and the process dies. Here the escape is the
    // ExecuteAsync task faulting with the same exception the dispatcher would have received.

    [Fact]
    public async Task Deploy_CancelledMidRun_DoesNotEscape_AndReportsThePartialTally()
    {
        var (vm, svc) = Ready();
        svc.Gate.SetResult();
        svc.DuringDeploy = () => vm.CancelOperationCommand.Execute(null);   // cancel during game A

        var ex = await Record.ExceptionAsync(() => Refused(vm.DeploySelectedCommand.ExecuteAsync(null)));

        Assert.Null(ex);
        Assert.Contains("deploy:A", svc.Calls);
        Assert.DoesNotContain("deploy:B", svc.Calls);                        // the second never started
        Assert.Contains("cancel", (vm.LastOperationResult ?? "").ToLowerInvariant());
        Assert.Contains("deployed: 1", vm.LastOperationResult ?? "");
        Assert.False(vm.IsScanning);                                          // the gate was released
    }

    [Fact]
    public async Task Deploy_CancelledAfterAPickChanged_StillSavesIt()
    {
        var (vm, svc) = Ready();
        svc.Gate.SetResult();
        bool saved = false;
        vm.RequestOptionSave = () => saved = true;
        svc.DuringDeploy = () => vm.CancelOperationCommand.Execute(null);

        await Record.ExceptionAsync(() => Refused(vm.DeploySelectedCommand.ExecuteAsync(null)));

        Assert.True(vm.LastManualProxyByGame.ContainsKey("A"));
        Assert.True(saved, "game A was deployed with a new pick; cancelling must not lose it");
    }

    [Fact]
    public async Task Deploy_OneGame_CancelReachingTheFinalRefresh_DoesNotEscape()
    {
        // "Even a ONE-game deploy reaches it, through the post-loop refresh's token": the loop ends
        // before any re-check, and the refresh -- the real service honours its token -- throws.
        var (vm, svc) = Ready();
        vm.Games[1].IsSelected = false;
        svc.Gate.SetResult();
        svc.ThrowOnCancelledRefresh = true;
        svc.DuringDeploy = () => vm.CancelOperationCommand.Execute(null);

        var ex = await Record.ExceptionAsync(() => Refused(vm.DeploySelectedCommand.ExecuteAsync(null)));

        Assert.Null(ex);
        Assert.Contains("cancel", (vm.LastOperationResult ?? "").ToLowerInvariant());
        Assert.Contains("deployed: 1", vm.LastOperationResult ?? "");
        // The grid was brought back in line WITHOUT the cancelled token: a refresh landed after the
        // throwing one, and nothing was reported as an error. (review of b8d09045)
        Assert.NotEmpty(svc.Applied);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Undeploy_CancelledMidRun_DoesNotEscape_AndReportsThePartialTally()
    {
        var (vm, svc) = Ready();
        svc.Gate.SetResult();
        svc.ThrowOnCancelledRefresh = true;   // the catch's own refresh must avoid the cancelled token
        svc.DuringUndeploy = () => vm.CancelOperationCommand.Execute(null);

        var ex = await Record.ExceptionAsync(() => Refused(vm.UndeploySelectedCommand.ExecuteAsync(null)));

        Assert.Null(ex);
        Assert.DoesNotContain("undeploy:B", svc.Calls);
        Assert.Contains("cancel", (vm.LastOperationResult ?? "").ToLowerInvariant());
        Assert.Contains("removed: 1", vm.LastOperationResult ?? "");
        Assert.NotEmpty(svc.Applied);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Undeploy_OneGame_CancelReachingTheFinalRefresh_DoesNotEscape()
    {
        // The Remove twin of the one-game Deploy case (review of b8d09045): the real UndeployAsync
        // never checks its token inside the worker, so a one-game Remove cancelled mid-run finishes
        // that game and reaches the post-loop refresh with the cancelled token.
        var (vm, svc) = Ready();
        vm.Games[1].IsSelected = false;
        svc.Gate.SetResult();
        svc.ThrowOnCancelledRefresh = true;
        svc.DuringUndeploy = () => vm.CancelOperationCommand.Execute(null);

        var ex = await Record.ExceptionAsync(() => Refused(vm.UndeploySelectedCommand.ExecuteAsync(null)));

        Assert.Null(ex);
        Assert.Contains("cancel", (vm.LastOperationResult ?? "").ToLowerInvariant());
        Assert.Contains("removed: 1", vm.LastOperationResult ?? "");
        Assert.NotEmpty(svc.Applied);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAll_Cancelled_BringsTheGridBackInLine()
    {
        // Update All's cancel path -- the model Deploy's was copied from -- reported its partial
        // tally but never refreshed, so the grid kept the old versions for the games it HAD
        // written. (review of b8d09045)
        var (vm, svc) = Ready(deployed: true);
        svc.Gate.SetResult();
        svc.ThrowOnCancelledRefresh = true;
        svc.DuringDeploy = () => vm.CancelOperationCommand.Execute(null);   // cancel during game A

        var ex = await Record.ExceptionAsync(() => Refused(vm.UpdateAllCommand.ExecuteAsync(null)));

        Assert.Null(ex);
        Assert.Contains("cancel", (vm.LastOperationResult ?? "").ToLowerInvariant());
        Assert.Contains("updated: 1", vm.LastOperationResult ?? "");
        Assert.NotEmpty(svc.Applied);                 // a refresh landed after the cancel
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Refresh_Cancelled_IsNeutral_NotAFailure()
    {
        // Refresh's catch(Exception) showed the user's own cancel as a red "Refresh failed".
        var (vm, svc) = Ready();
        svc.ParkRefreshes = true;
        svc.ThrowOnCancelledRefresh = true;

        var refresh = vm.RefreshCommand.ExecuteAsync(null);
        vm.CancelOperationCommand.Execute(null);
        svc.PendingRefreshes[0].SetResult();
        await Refused(refresh);

        Assert.DoesNotContain("failed", (vm.StatusText ?? "").ToLowerInvariant());
        Assert.Contains("cancel", (vm.StatusText ?? "").ToLowerInvariant());
        Assert.Null(vm.ErrorMessage);
        Assert.Equal("#888888", vm.StatusColor);        // neutral, not the red of a failure
    }

    [Fact]
    public async Task Deploy_WithoutACancel_CompletesAsBefore()
    {
        // The control: nobody cancels, both games deploy, and the tally says nothing of a cancel.
        var (vm, svc) = Ready();
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Contains("deploy:B", svc.Calls);
        Assert.DoesNotContain("cancel", (vm.LastOperationResult ?? "").ToLowerInvariant());
    }

    // ── [A3-RADIO-MIDDEPLOY]: the proxy-type radios stay live during Deploy ─────────────────
    //
    // A click mid-Deploy re-targeted every remaining game to another DLL flavour, and
    // LastManualProxyByGame recorded the NEW flavour for the game in flight. The recorded fix binds
    // the four radios -- not their panel, which also holds the LKG checkbox -- to !IsBusy. Pinned
    // from the AXAML (the window cannot be constructed here).
    private static string PanelXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? path = null;
        for (int i = 0; i < 8 && dir is not null && path is null; i++, dir = dir.Parent)
        {
            var c = Path.Combine(dir.FullName, "ui", "UE5DumpUI", "Views", "ProxyDeployPanel.axaml");
            if (File.Exists(c)) path = c;
        }
        Assert.NotNull(path);
        return File.ReadAllText(path!);
    }

    [Fact]
    public void ProxyTypeRadios_AreDisabledWhileBusy_TheLkgCheckboxIsNot()
    {
        var xaml = PanelXaml();
        var radios = System.Text.RegularExpressions.Regex.Matches(xaml, @"<RadioButton GroupName=""ProxyType""[^>]*>");
        Assert.Equal(4, radios.Count);
        Assert.All(radios, m => Assert.Contains(@"IsEnabled=""{Binding !IsBusy}""", m.Value));

        var lkg = System.Text.RegularExpressions.Regex.Match(xaml, @"<CheckBox[^>]*LkgSuggestEnabled[^>]*>");
        Assert.True(lkg.Success, "the LKG checkbox is gone -- re-point this pin");
        Assert.DoesNotContain("IsEnabled", lkg.Value);

        // The recorded-unsafe variants: disabling the foreign-overwrite checkbox, or the panel that
        // holds the radios (it also holds the LKG checkbox). (review of b8d09045)
        var foreign = System.Text.RegularExpressions.Regex.Match(xaml, @"<CheckBox[^>]*AllowForeignOverwrite[^>]*>");
        Assert.True(foreign.Success, "the foreign-overwrite checkbox is gone -- re-point this pin");
        Assert.DoesNotContain("IsEnabled", foreign.Value);
        int firstRadio = xaml.IndexOf(@"GroupName=""ProxyType""", StringComparison.Ordinal);
        int panelOpen = xaml.LastIndexOf("<StackPanel", firstRadio, StringComparison.Ordinal);
        Assert.True(panelOpen >= 0, "the radios' panel is gone -- re-point this pin");
        string panelTag = xaml.Substring(panelOpen, xaml.IndexOf('>', panelOpen) - panelOpen + 1);
        Assert.DoesNotContain("IsEnabled", panelTag);

        // ...nor through the foreign-overwrite checkbox's OWN panel: disabling that parent disables the
        // checkbox as surely as an attribute on it. (review of 64b28058)
        int foreignAt = xaml.IndexOf("AllowForeignOverwrite", StringComparison.Ordinal);
        int foreignPanel = xaml.LastIndexOf("<StackPanel", foreignAt, StringComparison.Ordinal);
        Assert.True(foreignPanel >= 0, "the foreign-overwrite checkbox's panel is gone -- re-point this pin");
        string foreignPanelTag = xaml.Substring(foreignPanel, xaml.IndexOf('>', foreignPanel) - foreignPanel + 1);
        Assert.DoesNotContain("IsEnabled", foreignPanelTag);
    }

    [Fact]
    public void UseConfirmed_IsNotPersisted_AndStaysClickableDuringARun()
    {
        // [PROXY-USE-CONFIRMED] It changes WHAT Deploy writes, so -- like the foreign box -- it must not become a
        // standing choice carried in from an earlier session: no bool for it in the persisted options.
        Assert.DoesNotContain(typeof(ProxyDeployUiOptions).GetProperties(),
            p => p.PropertyType == typeof(bool) && p.Name.Contains("Confirmed", StringComparison.Ordinal));

        // Read once per run, so it stays clickable, as Force and the foreign box do.
        var box = System.Text.RegularExpressions.Regex.Match(PanelXaml(), @"<CheckBox[^>]*UseConfirmedProxy[^>]*>");
        Assert.True(box.Success, "the confirmed-working checkbox is gone -- re-point this pin");
        Assert.DoesNotContain("IsEnabled", box.Value);
    }

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

    // ── [PROXY-FORCE-UPDATEALL] Update All honours Force Overwrite ────────────
    //
    // Reported by the maintainer: with Force Overwrite ticked, Update All skipped every proxy whose version
    // equalled the source's and said "already up-to-date". A rebuild that kept its build number was never
    // redeployed. Force Overwrite means rewrite OUR proxy whatever its version -- in Update All too.

    private const string SameVersion = "1.0.0.3552";

    // ── [PROXY-PRODUCTNAME-UNREADABLE] a proxy-named file we cannot read is treated conservatively ──

    private static bool IsWinmm(string p) => p.EndsWith("winmm.dll", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task Deploy_AnUnreadableProxyNamedFile_IsNotJoinedByASecondProxy()
    {
        // It may be one of ours we cannot read: adding version.dll next to it could make a double.
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Winmm));
        svc.IsOurs = p => !IsWinmm(p);
        svc.Unreadable = IsWinmm;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Empty(svc.Deploys);
        // (second review, UNREAD-RESULTLINE-CLAIMS-OURS) Counted as what it is, not as 'another of our proxies'.
        Assert.Contains("cannot read: 1", vm.LastOperationResult);
        Assert.DoesNotContain("another of our proxies", vm.LastOperationResult);
        Assert.Contains("winmm.dll", vm.Games[0].StatusDetail ?? "");
        Assert.Contains("cannot be read", vm.Games[0].StatusDetail ?? "");
    }

    [Fact]
    public async Task Deploy_TheSelectedTypeAlreadyOurs_BesideAnUnreadableFile_FollowsTheSameTypeRules()
    {
        // (second review, UNREAD-VM-GUARD-OWNTYPE) The unreadable guard skipped even when the selected type was already
        // ours there -- 'does not add a second proxy' was false, and the service backstop and Update All disagreed.
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Version, ProxyType.Winmm));
        svc.IsOurs = p => !IsWinmm(p);
        svc.Unreadable = IsWinmm;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Equal(ProxyType.Version, Assert.Single(svc.Deploys).Type);
        Assert.DoesNotContain("cannot read", vm.LastOperationResult);
    }

    [Fact]
    public async Task UpdateAll_TheSelectedNameUnreadable_SaysItOnce()
    {
        // (second review, UNREAD-UPDATEALL-DUP-NOTE) The refresh already reports the selected name as unreadable; the
        // note repeated the sentence, with '..' between.
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Version));
        svc.IsOurs = _ => false;
        svc.Unreadable = p => p.EndsWith("version.dll", StringComparison.OrdinalIgnoreCase);
        svc.RefreshDetail = g => "Cannot read version.dll here (access denied, or held open by another program) — "
                                 + "cannot tell whose it is.";
        svc.Gate.SetResult();

        await Refused(vm.UpdateAllCommand.ExecuteAsync(null));

        string detail = vm.Games[0].StatusDetail ?? "";
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(detail, "Cannot read version.dll"));
        Assert.DoesNotContain("..", detail);
    }

    [Fact]
    public async Task UseConfirmed_AFolderWithAnUnreadableProxyNamedFile_IsNotClean()
    {
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Winmm));
        svc.IsOurs = p => !IsWinmm(p);
        svc.Unreadable = IsWinmm;
        vm.ConfirmedProxyByExe[Exe("A")] = ProxyType.Dxgi;
        vm.UseConfirmedProxy = true;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Empty(svc.Deploys);                                   // neither dxgi (not clean) nor version (guard)
    }

    [Fact]
    public async Task UpdateAll_AnUnreadableProxy_IsCounted_AndSaysWhy()
    {
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Version));
        svc.IsOurs = _ => false;
        svc.Unreadable = p => p.EndsWith("version.dll", StringComparison.OrdinalIgnoreCase);
        // As the real refresh: it reports the selected name as unreadable (second review: the VM adds only "Not updated.").
        svc.RefreshDetail = _ => UE5DumpUI.Services.ProxyDeployService.DescribeUnreadable(new[] { "version.dll" });
        svc.Gate.SetResult();

        await Refused(vm.UpdateAllCommand.ExecuteAsync(null));

        Assert.Empty(svc.Deploys);
        Assert.Contains("cannot read: 1", vm.LastOperationResult ?? "");
        Assert.Contains("Cannot read version.dll", vm.Games[0].StatusDetail ?? "");
        Assert.Contains("Not updated", vm.Games[0].StatusDetail ?? "");
    }

    // (third review, UNREAD-NOTE-DOUBLED-OTHERNAME) The real refresh names an unreadable file at ANY proxy name, not
    // only the selected one -- so a note that restated it came out twice. And a row the refresh PRESERVES (a failure in
    // the same folder) is never refreshed, so there the note must name the file itself.
    private static string RealRefreshNaming(params string[] unreadable) =>
        UE5DumpUI.Services.ProxyDeployService.DescribeUnreadable(unreadable);

    private static int Count(string text, string what) =>
        System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(what)).Count;

    [Fact]
    public async Task Deploy_AnUnreadableOtherName_TheRefreshedRowSaysItOnce()
    {
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Winmm));
        svc.IsOurs = p => !IsWinmm(p);
        svc.Unreadable = IsWinmm;
        svc.RefreshDetail = _ => RealRefreshNaming("winmm.dll");
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        string detail = vm.Games[0].StatusDetail ?? "";
        Assert.Equal(1, Count(detail, "access denied"));
        Assert.Contains("Skipped", detail);
        Assert.Contains("cannot read: 1", vm.LastOperationResult);
    }

    [Fact]
    public async Task UpdateAll_AnUnreadableOtherName_TheRefreshedRowSaysItOnce()
    {
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Version, ProxyType.Winmm));
        svc.IsOurs = p => !IsWinmm(p);
        svc.Unreadable = IsWinmm;
        svc.VersionOf = _ => SameVersion;                  // version.dll (ours) is current: nothing to rewrite
        svc.RefreshDetail = _ => RealRefreshNaming("winmm.dll");
        svc.Gate.SetResult();

        await Refused(vm.UpdateAllCommand.ExecuteAsync(null));

        string detail = vm.Games[0].StatusDetail ?? "";
        Assert.Equal(1, Count(detail, "access denied"));
        // (fourth review, R4-UPDATEALL-SHORT-NOTE-NAME-UNPINNED) The short note names its file: two unreadable files
        // must not read "Not updated. Not updated."
        Assert.Contains("Not updated: winmm.dll.", detail);
        Assert.Contains("cannot read: 1", vm.LastOperationResult);
    }

    [Fact]
    public async Task UpdateAll_APreservedRow_StillNamesTheUnreadableFile()
    {
        // version.dll (the radio's) cannot be read; winmm.dll (ours) fails to write, so the row is preserved and the
        // refresh never names version.dll. Details said '<the lock failure> Not updated.' beside 'cannot read: 1'.
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Version, ProxyType.Winmm));
        svc.IsOurs = IsWinmm;
        svc.Unreadable = p => p.EndsWith("version.dll", StringComparison.OrdinalIgnoreCase);
        svc.VersionOf = _ => SameVersion;
        vm.ForceOverwrite = true;
        svc.SimulateStatus = true;
        svc.DeployResultByType = (_, t) => t != ProxyType.Winmm;
        svc.Gate.SetResult();

        await Refused(vm.UpdateAllCommand.ExecuteAsync(null));

        string detail = vm.Games[0].StatusDetail ?? "";
        Assert.Contains("Cannot read version.dll", detail);
        Assert.Contains("Target in use", detail);
        Assert.Equal(1, Count(detail, "access denied"));
        Assert.Contains("cannot read: 1", vm.LastOperationResult);
        Assert.Contains("failed: 1", vm.LastOperationResult);
    }

    // ── [PROXY-RISKNOTE-WIPED] the one-shot import-risk note outlives the refresh ──
    //
    // DeployAsync writes an advisory when a flavour may not load (BYPASS: imported, so a System32 copy can pre-empt it;
    // NEVER-LOADS: a static-only flavour nothing names). Both fail silently when real, so the note is the one nudge
    // at deploy time -- and the same run's refresh, which rewrites every row outside failedDirs from disk, erased it.

    private const string RiskNote = "version.dll is imported by the game -- an already-mapped System32 copy can pre-empt it";

    [Fact]
    public async Task Deploy_TheImportRiskNote_SurvivesTheRefresh()
    {
        var (vm, svc) = ReadyWith(Game("A"));
        svc.DeployNote = (_, _) => RiskNote;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.StartsWith("Deployed: 1 success", vm.LastOperationResult);
        Assert.Contains(RiskNote, vm.Games[0].StatusDetail ?? "");
    }

    [Fact]
    public async Task Deploy_ASwitchedRow_KeepsTheSwitchNote_AndTheRiskNote()
    {
        var (vm, svc) = ReadyWith(Game("A"));
        vm.ConfirmedProxyByExe[Exe("A")] = ProxyType.Winmm;
        vm.UseConfirmedProxy = true;
        svc.DeployNote = (_, _) => RiskNote;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Contains("confirmed working", vm.Games[0].StatusDetail ?? "");
        Assert.Contains(RiskNote, vm.Games[0].StatusDetail ?? "");
    }

    [Fact]
    public async Task UpdateAll_TheImportRiskNote_SurvivesTheRefresh()
    {
        var (vm, svc) = Ready(deployed: true);
        svc.ClearDetailsOnRefresh = true;
        svc.VersionOf = _ => SameVersion;
        vm.ForceOverwrite = true;
        svc.DeployNote = (_, _) => RiskNote;
        svc.Gate.SetResult();

        await Refused(vm.UpdateAllCommand.ExecuteAsync(null));

        Assert.StartsWith("Updated: 2", vm.LastOperationResult);
        Assert.All(vm.Games, g => Assert.Contains(RiskNote, g.StatusDetail ?? ""));
    }

    [Fact]
    public async Task UpdateAll_ADoubledFolder_ALaterSuccessDoesNotHideAnEarlierFailure()
    {
        // (second review, UPDATEALL-DOUBLED-FAIL-OVERWRITTEN, pre-existing) version.dll locked (the running game maps
        // it), winmm.dll rewritten: the success's status overwrote the failure on a row the refresh preserves -- the
        // grid said DeployedCurrent with no reason while the line said 'failed: 1'.
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Version, ProxyType.Winmm));
        svc.VersionOf = _ => SameVersion;
        vm.ForceOverwrite = true;
        svc.SimulateStatus = true;
        svc.DeployResultByType = (_, t) => t != ProxyType.Version;
        svc.Gate.SetResult();

        await Refused(vm.UpdateAllCommand.ExecuteAsync(null));

        Assert.Contains("failed: 1", vm.LastOperationResult);
        Assert.Equal(ProxyDeployStatus.ErrorLocked, vm.Games[0].Status);
        Assert.Contains("Target in use", vm.Games[0].StatusDetail ?? "");
    }

    [Fact]
    public async Task UpdateAll_SameVersion_Force_RewritesEveryDeployedProxy()
    {
        var (vm, svc) = Ready(deployed: true);
        svc.VersionOf = _ => SameVersion;
        vm.ForceOverwrite = true;
        svc.Gate.SetResult();

        await Refused(vm.UpdateAllCommand.ExecuteAsync(null));

        Assert.Equal(new[] { "A", "B" }, svc.Deploys.Select(d => d.Game));
        // The forced path now RELIES on this: with ForceSameVersion false the real service answers AlreadyCurrent,
        // returns true WITHOUT writing, and the line below would claim a rewrite that never happened. (the fix's
        // correctness skeptic)
        Assert.All(svc.Deploys, d => Assert.True(d.Options.ForceSameVersion));
        Assert.StartsWith("Updated: 2", vm.LastOperationResult);
        Assert.Contains("same version", vm.LastOperationResult);
        Assert.DoesNotContain("already up-to-date", vm.LastOperationResult);
    }

    [Fact]
    public async Task UpdateAll_SameVersion_NoForce_SkipsEveryDeployedProxy()
    {
        // The control: unticked, a same-version proxy is still skipped -- and the message says how to force it.
        var (vm, svc) = Ready(deployed: true);
        svc.VersionOf = _ => SameVersion;
        svc.Gate.SetResult();

        await Refused(vm.UpdateAllCommand.ExecuteAsync(null));

        Assert.Empty(svc.Deploys);
        Assert.Contains("All 2 deployed proxy DLL(s) already up-to-date", vm.LastOperationResult);
        Assert.Contains("Force Overwrite", vm.LastOperationResult);
    }

    [Fact]
    public async Task UpdateAll_Force_NeverTouchesForeignOrUndeployed_AndNeverGrantsForeignConsent()
    {
        // AC1 stays: Force reaches only a proxy that is already deployed AND ours, and Update All never
        // passes foreign consent -- not even with "Replace other tools' DLLs" ticked beside it.
        var (vm, svc) = Ready(deployed: true);
        char sep = Path.DirectorySeparatorChar;
        svc.VersionOf = _ => SameVersion;
        svc.IsOurs = p => !p.Contains($"{sep}A{sep}Binaries{sep}", StringComparison.Ordinal);   // A's DLL is foreign
        vm.ForceOverwrite = true;
        vm.AllowForeignOverwrite = true;
        svc.Gate.SetResult();

        await Refused(vm.UpdateAllCommand.ExecuteAsync(null));

        var only = Assert.Single(svc.Deploys);                       // not A (foreign), and on B only the
        Assert.Equal(("B", ProxyType.Version), (only.Game, only.Type)); // type that is deployed -- no new type
        Assert.False(only.Options.ForeignConsent);
    }

    [Fact]
    public async Task UpdateAll_ForceUntickedMidRun_StillCoversTheWholeRun()
    {
        // The checkbox stays live during a run. Read per proxy, an untick half-way split one Update All into
        // two policies; it is read once, when the user pressed the button.
        var (vm, svc) = Ready(deployed: true);
        svc.VersionOf = _ => SameVersion;
        vm.ForceOverwrite = true;
        svc.DuringDeploy = () => vm.ForceOverwrite = false;
        svc.Gate.SetResult();

        await Refused(vm.UpdateAllCommand.ExecuteAsync(null));

        Assert.Equal(new[] { "A", "B" }, svc.Deploys.Select(d => d.Game));
        Assert.StartsWith("Updated: 2", vm.LastOperationResult);
    }

    [Fact]
    public async Task UpdateAll_Force_CancelledMidRun_ReportsWhatItWrote()
    {
        var (vm, svc) = Ready(deployed: true);
        svc.VersionOf = _ => SameVersion;
        vm.ForceOverwrite = true;
        svc.DuringDeploy = () => vm.CancelOperationCommand.Execute(null);   // cancel during game A
        svc.Gate.SetResult();

        var ex = await Record.ExceptionAsync(() => Refused(vm.UpdateAllCommand.ExecuteAsync(null)));

        Assert.Null(ex);
        Assert.Contains("cancel", (vm.LastOperationResult ?? "").ToLowerInvariant());
        Assert.Contains("updated: 1", vm.LastOperationResult ?? "");
    }

    // ── [PROXY-DEPLOY-NOOP-COUNT] Deploy does not report a write that never happened ──────
    //
    // The same situation through the Deploy button: Force off, same version. The real service answers
    // AlreadyCurrent and returns TRUE without writing, so the VM counted it and showed "Deployed: 2 success".

    [Fact]
    public async Task Deploy_SameVersion_NoForce_SaysAlreadyCurrent_NotDeployed()
    {
        var (vm, svc) = Ready(deployed: true);   // version.dll deployed; Version is the selected proxy
        svc.VersionOf = _ => SameVersion;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Empty(svc.Deploys);
        Assert.StartsWith("Deployed: 0 success", vm.LastOperationResult);
        Assert.Contains("already current: 2", vm.LastOperationResult);
        Assert.Contains("Force Overwrite", vm.LastOperationResult);
        // Nothing was written: neutral, as Update All shows the same situation -- not the success colour.
        // (the fix's tests-and-text skeptic)
        Assert.Equal("#888888", vm.LastOperationColor);
        Assert.Equal("#888888", vm.StatusColor);
    }

    [Fact]
    public async Task Deploy_SameVersion_Force_Deploys()
    {
        var (vm, svc) = Ready(deployed: true);
        svc.VersionOf = _ => SameVersion;
        vm.ForceOverwrite = true;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Equal(new[] { "A", "B" }, svc.Deploys.Select(d => d.Game));
        Assert.All(svc.Deploys, d => Assert.True(d.Options.ForceSameVersion));
        Assert.StartsWith("Deployed: 2 success", vm.LastOperationResult);
        Assert.DoesNotContain("already current", vm.LastOperationResult);
    }

    [Fact]
    public async Task Deploy_SameVersion_ForeignTarget_StillGoesToTheService()
    {
        // Only OUR same-version proxy is short-circuited; a foreign DLL at any version stays the service's call,
        // so its consent logic (refuse, or replace with "Replace other tools' DLLs") is untouched.
        var (vm, svc) = Ready(deployed: true);
        svc.VersionOf = _ => SameVersion;
        svc.IsOurs = _ => false;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Equal(new[] { "A", "B" }, svc.Deploys.Select(d => d.Game));
    }

    // ── [PROXY-DOUBLE-GUARD] Deploy never adds a second of our proxy types ──────────────
    //
    // Maintainer request: Select All + Deploy over games that already carry ANOTHER of our proxies made doubles,
    // each to be fixed by hand. The guard skips those games -- even with Force Overwrite, and without consuming
    // foreign consent -- and says why in Details, AFTER the refresh that would otherwise wipe it.

    [Fact]
    public async Task Deploy_OtherOfOurs_IsSkipped_EvenWithForce()
    {
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Winmm), Game("B", ProxyType.Dxgi));   // radio: version.dll
        vm.ForceOverwrite = true;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Empty(svc.Deploys);
        Assert.Contains("skipped: 2", vm.LastOperationResult);
        Assert.Equal("#888888", vm.LastOperationColor);               // nothing written: neutral
        Assert.Empty(vm.LastManualProxyByGame);                        // a skipped game is not the user's pick
        Assert.StartsWith("Skipped:", vm.Games[0].StatusDetail);       // survives the refresh...
        Assert.DoesNotContain(vm.Games[0].BinariesDir, svc.Preserves.Last());   // ...which DID rewrite the row
        Assert.Contains("winmm.dll", vm.Games[0].StatusDetail);
        Assert.Contains("dxgi.dll", vm.Games[1].StatusDetail);
    }

    [Fact]
    public async Task Deploy_ForeignTargetPlusOtherOfOurs_IsSkipped_AndConsentIsNotConsumed()
    {
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Version, ProxyType.Winmm));
        char sep = Path.DirectorySeparatorChar;
        svc.IsOurs = p => !p.EndsWith($"{sep}version.dll", StringComparison.OrdinalIgnoreCase);   // version.dll is foreign
        vm.AllowForeignOverwrite = true;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Empty(svc.Deploys);
        Assert.Contains("skipped: 1", vm.LastOperationResult);
    }

    [Fact]
    public async Task Deploy_ForeignAtAnotherName_IsNotOurs_SoItStillDeploys()
    {
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Winmm));
        svc.IsOurs = p => !p.EndsWith("winmm.dll", StringComparison.OrdinalIgnoreCase);    // someone else's winmm.dll
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Equal(new[] { ("A", ProxyType.Version) }, svc.Deploys.Select(d => (d.Game, d.Type)));
        Assert.DoesNotContain("skipped", vm.LastOperationResult);
    }

    [Fact]
    public async Task Deploy_AlreadyDoubledFolder_SameType_FollowsForce()
    {
        // A folder that is doubled already: redeploying the type that is THERE adds nothing new.
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Version, ProxyType.Winmm));
        svc.VersionOf = _ => SameVersion;
        vm.ForceOverwrite = true;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Equal(new[] { ("A", ProxyType.Version) }, svc.Deploys.Select(d => (d.Game, d.Type)));
        Assert.StartsWith("Deployed: 1 success", vm.LastOperationResult);
    }

    [Fact]
    public async Task Deploy_AlreadyDoubledFolder_SameType_NoForce_IsAlreadyCurrent()
    {
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Version, ProxyType.Winmm));
        svc.VersionOf = _ => SameVersion;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Empty(svc.Deploys);
        Assert.Contains("already current: 1", vm.LastOperationResult);
        Assert.DoesNotContain("skipped", vm.LastOperationResult);
    }

    [Fact]
    public async Task Deploy_SkippingAnAlreadyDoubledFolder_KeepsTheDoubleWarning()
    {
        // The skip note replaced the refresh's "Multiple proxy DLLs deployed …" -- losing the one warning about the
        // double on exactly the folder this guard exists for. (the text skeptic)
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Dxgi, ProxyType.Winmm));
        svc.RefreshDetail = _ => "Multiple proxy DLLs deployed (dxgi.dll, winmm.dll) — only one will activate at runtime";
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Contains("Multiple proxy DLLs deployed", vm.Games[0].StatusDetail);
        Assert.Contains("Skipped:", vm.Games[0].StatusDetail);
    }

    [Fact]
    public async Task Deploy_CancelledAfterASkip_KeepsTheSkipNote_AndCountsIt()
    {
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Winmm), Game("B"));
        svc.ThrowOnCancelledRefresh = true;
        svc.DuringDeploy = () => vm.CancelOperationCommand.Execute(null);   // cancel during B, after A was skipped
        svc.Gate.SetResult();

        var ex = await Record.ExceptionAsync(() => Refused(vm.DeploySelectedCommand.ExecuteAsync(null)));

        Assert.Null(ex);
        Assert.Contains("cancel", (vm.LastOperationResult ?? "").ToLowerInvariant());
        Assert.Contains("skipped: 1", vm.LastOperationResult ?? "");
        Assert.StartsWith("Skipped:", vm.Games[0].StatusDetail);
    }

    [Fact]
    public async Task UpdateAll_AlreadyDoubledFolder_StillUpdatesEveryTypeThere()
    {
        // Update All only rewrites types already present, so the guard has nothing to say to it.
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Version, ProxyType.Winmm));
        svc.Gate.SetResult();

        await Refused(vm.UpdateAllCommand.ExecuteAsync(null));

        Assert.Equal(new[] { ProxyType.Version, ProxyType.Winmm }, svc.Deploys.Select(d => d.Type).OrderBy(t => t));
    }

    // ── [PROXY-USE-CONFIRMED] "Use confirmed-working proxy" ─────────────────────────────
    //
    // Maintainer request: for a game with a CONFIRMED-WORKING proxy on record and none of ours in its folder (a
    // reinstall, say), deploy the recorded type instead of the radio's. Read from ConfirmedProxyByExe -- never from
    // the Suggested column, which also carries last-used and the default.

    [Theory]
    [InlineData(ProxyType.Version, false, (int)ProxyType.Winmm, 0, ProxyType.Version, false)]   // box off
    [InlineData(ProxyType.Version, true,  -1,                   0, ProxyType.Version, false)]   // no record
    [InlineData(ProxyType.Version, true,  (int)ProxyType.Winmm, 0, ProxyType.Winmm,   true)]    // the substitution
    [InlineData(ProxyType.Version, true,  (int)ProxyType.Version, 0, ProxyType.Version, false)] // record == radio
    [InlineData(ProxyType.Version, true,  (int)ProxyType.Winmm, 1, ProxyType.Version, false)]   // folder not clean
    public void PickDeployType_SubstitutesOnlyForACleanFolderWithARecord(
        ProxyType radio, bool useConfirmed, int confirmed, int oursPresent, ProxyType expected, bool substituted)
    {
        var got = ProxyDeployViewModel.PickDeployType(radio, useConfirmed,
            confirmed < 0 ? null : (ProxyType)confirmed, oursPresent);
        Assert.Equal((expected, substituted), got);
    }

    private static string Exe(string game) => $"{game}.exe";

    [Fact]
    public async Task UseConfirmed_CleanFolder_DeploysTheConfirmedType_AndRemembersIt_WithoutForeignConsent()
    {
        var (vm, svc) = ReadyWith(Game("A"));
        vm.ConfirmedProxyByExe[Exe("A")] = ProxyType.Winmm;
        vm.UseConfirmedProxy = true;
        vm.AllowForeignOverwrite = true;                           // a switched row never passes it
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        var d = Assert.Single(svc.Deploys);
        Assert.Equal(("A", ProxyType.Winmm), (d.Game, d.Type));
        Assert.EndsWith($"{Path.DirectorySeparatorChar}proxy{Path.DirectorySeparatorChar}winmm.dll", d.Source);
        Assert.False(d.Options.ForeignConsent);
        Assert.Equal(ProxyType.Winmm, vm.LastManualProxyByGame["A"]);   // the type actually deployed
        Assert.Contains("confirmed type used: 1", vm.LastOperationResult);
        Assert.Equal("Deployed winmm.dll (confirmed working) instead of version.dll", vm.Games[0].StatusDetail);
    }

    [Fact]
    public async Task UseConfirmed_ASharedExeName_UsesTheRadio_AndSaysWhy()
    {
        // [PROXY-CONFIRM-SHARED-EXE] Two detected games ship Shared.exe: the record cannot say which one it came from,
        // so neither gets it -- the radio's type, and a note (not a silent fallback).
        DetectedGame SharedExe(DetectedGame g) => new()
        {
            Name = g.Name, BinariesDir = g.BinariesDir, ExePath = Path.Combine(g.BinariesDir, "Shared.exe"),
            IsSelected = true,
        };
        var (vm, svc) = ReadyWith(SharedExe(Game("A")), SharedExe(Game("B")));
        vm.ConfirmedProxyByExe["Shared.exe"] = ProxyType.Winmm;
        vm.UseConfirmedProxy = true;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Equal(new[] { ("A", ProxyType.Version), ("B", ProxyType.Version) },
                     svc.Deploys.Select(x => (x.Game, x.Type)));
        Assert.DoesNotContain("confirmed type used", vm.LastOperationResult);
        Assert.All(vm.Games, g => Assert.Contains("shared exe name", g.StatusDetail ?? "", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("confirmed record not used: 2", vm.LastOperationResult);
    }

    [Fact]
    public async Task UseConfirmed_TheSameExeInOneFolder_IsNotShared()
    {
        // Only DIFFERENT folders make a name ambiguous: one game listed once keeps its record.
        var (vm, svc) = ReadyWith(Game("A"));
        vm.ConfirmedProxyByExe[Exe("A")] = ProxyType.Winmm;
        vm.UseConfirmedProxy = true;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Equal(ProxyType.Winmm, Assert.Single(svc.Deploys).Type);
    }

    [Fact]
    public async Task UseConfirmed_Off_UsesTheRadio()
    {
        var (vm, svc) = ReadyWith(Game("A"));
        vm.ConfirmedProxyByExe[Exe("A")] = ProxyType.Winmm;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Equal(new[] { ("A", ProxyType.Version) }, svc.Deploys.Select(x => (x.Game, x.Type)));
    }

    [Fact]
    public async Task UseConfirmed_NoRecord_UsesTheRadio()
    {
        var (vm, svc) = ReadyWith(Game("A"));
        vm.UseConfirmedProxy = true;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Equal(new[] { ("A", ProxyType.Version) }, svc.Deploys.Select(x => (x.Game, x.Type)));
        Assert.DoesNotContain("confirmed type used", vm.LastOperationResult);
    }

    [Fact]
    public async Task UseConfirmed_RadioTypeAlreadyDeployed_DoesNotSwitch()
    {
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Version));
        vm.ConfirmedProxyByExe[Exe("A")] = ProxyType.Winmm;
        vm.UseConfirmedProxy = true;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Equal(new[] { ("A", ProxyType.Version) }, svc.Deploys.Select(x => (x.Game, x.Type)));
    }

    [Fact]
    public async Task UseConfirmed_AnotherOfOursDeployed_IsStillSkipped()
    {
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Dxgi));
        vm.ConfirmedProxyByExe[Exe("A")] = ProxyType.Winmm;
        vm.UseConfirmedProxy = true;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Empty(svc.Deploys);
        Assert.Contains("skipped: 1", vm.LastOperationResult);
    }

    [Fact]
    public async Task UseConfirmed_MissingSource_FailsThatRowOnly_NoSilentFallback()
    {
        var (vm, svc) = ReadyWith(Game("A"), Game("B"));
        vm.ConfirmedProxyByExe[Exe("A")] = ProxyType.Winmm;
        vm.UseConfirmedProxy = true;
        var all = vm.ResolveSources();
        vm.ResolveSources = () => all.Where(kv => kv.Key != ProxyType.Winmm).ToDictionary(kv => kv.Key, kv => kv.Value);
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Equal(new[] { ("B", ProxyType.Version) }, svc.Deploys.Select(x => (x.Game, x.Type)));   // not A as version
        Assert.StartsWith("Deployed: 1 success, 1 failed", vm.LastOperationResult);
        Assert.Contains("winmm.dll", vm.Games[0].StatusDetail);
        Assert.Contains("not found", vm.Games[0].StatusDetail);
    }

    [Fact]
    public async Task UseConfirmed_ForeignFileAtTheConfirmedName_FailsThatRow_AndSaysWhy()
    {
        // The confirmed name is taken by another program's file (ReShade's dxgi.dll, say). A switched row never
        // replaces it -- and the row must say so, naming the confirmed type, not read as a refusal of the radio's.
        var (vm, svc) = ReadyWith(Game("A", ProxyType.Winmm));
        svc.IsOurs = p => !p.EndsWith("winmm.dll", StringComparison.OrdinalIgnoreCase);    // winmm.dll is foreign
        vm.ConfirmedProxyByExe[Exe("A")] = ProxyType.Winmm;
        vm.UseConfirmedProxy = true;
        vm.AllowForeignOverwrite = true;
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Empty(svc.Deploys);
        Assert.StartsWith("Deployed: 0 success, 1 failed", vm.LastOperationResult);
        Assert.Contains("winmm.dll", vm.Games[0].StatusDetail);
        Assert.Contains("confirmed working", vm.Games[0].StatusDetail);
        Assert.Contains("another program", vm.Games[0].StatusDetail);
    }

    [Fact]
    public async Task UseConfirmed_ASwitchedRowThatFails_SaysItWasTheConfirmedType()
    {
        var (vm, svc) = ReadyWith(Game("A"));
        vm.ConfirmedProxyByExe[Exe("A")] = ProxyType.Winmm;
        vm.UseConfirmedProxy = true;
        svc.DeployResult = _ => false;                                  // locked, say
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.StartsWith("Deployed: 0 success, 1 failed", vm.LastOperationResult);
        Assert.Contains("winmm.dll (confirmed working)", vm.Games[0].StatusDetail);
    }

    [Fact]
    public async Task UseConfirmed_IsReadOncePerRun()
    {
        var (vm, svc) = ReadyWith(Game("A"), Game("B"));
        vm.ConfirmedProxyByExe[Exe("A")] = ProxyType.Winmm;
        vm.ConfirmedProxyByExe[Exe("B")] = ProxyType.Winmm;
        vm.UseConfirmedProxy = true;
        svc.DuringDeploy = () => vm.UseConfirmedProxy = false;       // unticked while A is being written
        svc.Gate.SetResult();

        await Refused(vm.DeploySelectedCommand.ExecuteAsync(null));

        Assert.Equal(new[] { ProxyType.Winmm, ProxyType.Winmm }, svc.Deploys.Select(x => x.Type));
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
