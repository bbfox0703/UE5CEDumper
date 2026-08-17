using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Proxy DLL Deploy tab.
/// Manages Steam game detection and proxy DLL deployment.
/// Not pipe-dependent — works independently of game connection.
/// </summary>
public partial class ProxyDeployViewModel : ViewModelBase
{
    private readonly IProxyDeployService _deploy;
    private readonly ILoggingService _log;

    /// <summary>Only used to reveal a leftover folder in Explorer. Optional so existing call sites
    /// and test doubles keep compiling; the command no-ops when it is absent.</summary>
    private readonly IPlatformService? _platform;

    [ObservableProperty] private bool _isScanning;
    // WHICH scan is running. IsScanning stays the mutual-exclusion guard, but it cannot
    // also drive the Cancel buttons: three commands set it and only two have a Cancel, so
    // every one of them showed BOTH buttons — each wired to a different command, whose
    // CanExecute (the wrapped command's CanBeCanceled) was false, rendering a disabled
    // ghost Cancel on the card that was not scanning. (B45)
    [ObservableProperty] private bool _isScanningDrives;
    [ObservableProperty] private bool _isScanningOrphans;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _sourceDllPath = "";
    [ObservableProperty] private string? _sourceDllVersion;
    /// <summary>Redeploy over OUR proxy even at the same version. Persisted
    /// (<c>ui-options.json</c>) — it is benign and reversible.</summary>
    [ObservableProperty] private bool _forceOverwrite;

    /// <summary>
    /// Allow replacing a DLL that is provably NOT ours (ReShade, Special K,
    /// Ultimate ASI Loader, a game-shipped wrapper).
    ///
    /// <para><b>Deliberately NOT persisted, and that is the fix — audit #5
    /// AC1.</b> This used to be the same flag as <see cref="ForceOverwrite"/>,
    /// which IS persisted, so a tick meaning "redeploy over my own DLL" became a
    /// standing cross-session, cross-game authorisation to destroy other
    /// programs' files — applied per game inside the deploy loop, over a
    /// Select All that can be an entire Steam library, with no confirmation and
    /// no backup. Resetting to false every launch keeps the destructive half
    /// tied to a decision the user made in the session they are looking at.</para>
    /// </summary>
    [ObservableProperty] private bool _allowForeignOverwrite;

    [ObservableProperty] private string? _lastOperationResult;

    /// <summary>
    /// Opt-in (default ON): show a per-game suggested proxy in the grid, derived
    /// from the .exe import table + the proxy the user last deployed for that game.
    /// Advisory only — never changes the selected proxy radio, never auto-deploys.
    /// </summary>
    [ObservableProperty] private bool _lkgSuggestEnabled = true;

    /// <summary>
    /// The proxy the user last DEPLOYED per game (keyed by DetectedGame.Name, the
    /// stable folder name — survives reinstall/patch, unlike peHash). Feeds the
    /// suggestion as a mini "last known good". Round-tripped via ProxyDeployUiOptions
    /// (MainWindowViewModel ApplyOptions/BuildOptions); persisted on change through
    /// <see cref="RequestOptionSave"/> because a Dictionary mutation is not tracked
    /// by the [ObservableProperty] save mechanism.
    /// </summary>
    public Dictionary<string, ProxyType> LastManualProxyByGame { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Games the user has successfully loaded via UI DLL injection, keyed by the
    /// .exe file name (available in the inject flow; stable across reinstall/patch).
    /// Injection is a UI-initiated action, so — unlike a plain-Connect proxy load —
    /// it is reliably known here. When a game is in this set but NOT in
    /// <see cref="LastManualProxyByGame"/>, the suggestion surfaces "injection ·
    /// no proxy deployed": injection is this game's known-good load method.
    /// </summary>
    public HashSet<string> InjectedGameExes { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Proxy CONFIRMED to have actually loaded a game — the DLL self-reported a
    /// proxy <c>load_mode</c> at connect AND the session stayed connected past the
    /// stability dwell (so it didn't load-then-crash). Keyed by .exe file name.
    /// The strongest known-good signal; wins over a merely-deployed pick. Recorded
    /// via <see cref="RecordConfirmedProxy"/> from MainWindowViewModel's gate.
    /// </summary>
    public Dictionary<string, ProxyType> ConfirmedProxyByExe { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Set by MainWindowViewModel: schedule a debounced options save after
    /// the remembered-pick map / injected set / confirmed map is mutated (none is an
    /// ObservableProperty, so the change-tracking save doesn't fire).</summary>
    public Action? RequestOptionSave { get; set; }

    /// <summary>
    /// Scan source: false = Steam library (default), true = generic drive scan.
    /// Bound to the Source radio pair at the top of the panel.
    /// </summary>
    [ObservableProperty] private bool _scanDrivesMode;

    // Status text colours — the top Status line + the last-result label turn a
    // prominent red when an operation reports failures (e.g. a deploy write blocked
    // by a file lock because the game is still running), green on full success, and
    // neutral gray otherwise. Bound to the TextBlock Foregrounds in the XAML.
    private const string StatusNeutral = "#888888";
    private const string StatusSuccess = "#4EC9B0";
    private const string StatusError   = "#F14C4C";
    [ObservableProperty] private string _statusColor = StatusNeutral;
    [ObservableProperty] private string _lastOperationColor = StatusNeutral;

    /// <summary>Set an operation's result on both the running Status line and the
    /// persistent last-result label, coloured red when any item failed (e.g. a
    /// file-locked write) or green on full success.</summary>
    private void SetOperationResult(string text, int fail)
    {
        LastOperationResult = text;
        StatusText = text;
        string color = fail > 0 ? StatusError : StatusSuccess;
        StatusColor = color;
        LastOperationColor = color;
        _log.Info("ProxyDeploy", text);
    }

    // ────────────────────────────────────────────────────────────────
    // The panel-wide exclusive gate
    // ────────────────────────────────────────────────────────────────

    /// <summary>What the panel is doing right now; null when idle. Exists so a refused
    /// operation can say what it is waiting FOR — the old message was always
    /// "Wait for scan to finish" even when the thing running was a deploy.</summary>
    private string? _busyWith;

    /// <summary>
    /// Take the panel's exclusive lock, or return null if something else holds it.
    /// <para>
    /// <see cref="IsScanning"/>'s own declaration already called itself "the mutual-exclusion
    /// guard" — it just was not one. Three of the eight long operations SET it while six
    /// TESTED it, so the guard was one-directional: a scan blocked a deploy, a deploy blocked
    /// nothing. Deploy and Undeploy could therefore run over the same <c>Binaries</c> folder at
    /// once and both write the single result line, and <see cref="ScanAsync"/> — which has no
    /// entry guard at all — could <c>Games.Clear()</c> the collection <see cref="UpdateAllAsync"/>
    /// was mid-way through. (audit #5 AE5 / AE6 / AE7)
    /// </para>
    /// <para>
    /// This does NOT replace what the MVVM toolkit already does. <c>AsyncRelayCommand</c> reports
    /// <c>CanExecute == false</c> while it is running and Avalonia's Button gates on that, so a
    /// command cannot re-enter ITSELF from its own button — measured, not assumed. The gap this
    /// closes is strictly CROSS-command: two different buttons, two different commands, no shared
    /// state between them. It also covers the paths no button owns (a property-changed handler,
    /// a hotkey, a test calling ExecuteAsync directly), where CanExecute is never consulted.
    /// </para>
    /// <para>
    /// No lock: every caller runs on the UI thread and the test-then-set is synchronous, before
    /// any await. If an operation is ever started from a worker this becomes a real race.
    /// </para>
    /// </summary>
    private BusyScope? TryBeginExclusive(string what)
    {
        if (IsScanning || IsRemovingOrphans) return null;
        _busyWith = what;
        IsScanning = true;
        return new BusyScope(this);
    }

    /// <summary>Releases the exclusive gate on dispose, so an early return or a throw cannot
    /// leave the panel wedged — which is how the flag came to be set by only three of the
    /// operations that test it.</summary>
    private sealed class BusyScope : IDisposable
    {
        private readonly ProxyDeployViewModel _vm;
        internal BusyScope(ProxyDeployViewModel vm) => _vm = vm;
        public void Dispose()
        {
            _vm.IsScanning = false;
            _vm._busyWith = null;
        }
    }

    /// <summary>The line a refused operation shows. Names the operation actually running.</summary>
    private string BusyMessage()
        => $"Busy: {_busyWith ?? "another operation"} is running — wait for it to finish";

    /// <summary>
    /// Which proxy DLL the user wants to deploy. Bound to the RadioButtons
    /// at the top of the panel. Changing this triggers a status refresh so
    /// the DataGrid reflects the deploy state of the newly-selected DLL.
    /// Defaults to version.dll — called at normal runtime (GetFileVersionInfo /
    /// COM / manifest parsing) rather than under the early loader lock, so it is
    /// the safest activation timing for the broadest set of games. (dxgi.dll is
    /// statically imported very early and some games call it before the CRT is
    /// initialised — e.g. Octopath Traveler instant-exits with the dxgi proxy;
    /// see docs/dev-log.md. Pick dxgi only for EXEs importing neither version
    /// nor dinput8.)
    /// </summary>
    [ObservableProperty] private ProxyType _selectedProxyType = ProxyType.Version;

    // Two-way bindings for radio button state — Avalonia RadioButtons bind
    // to bool, so we expose convenience properties that mirror SelectedProxyType.
    public bool IsVersionSelected
    {
        get => SelectedProxyType == ProxyType.Version;
        set { if (value) SelectedProxyType = ProxyType.Version; }
    }
    public bool IsDinput8Selected
    {
        get => SelectedProxyType == ProxyType.Dinput8;
        set { if (value) SelectedProxyType = ProxyType.Dinput8; }
    }
    public bool IsDxgiSelected
    {
        get => SelectedProxyType == ProxyType.Dxgi;
        set { if (value) SelectedProxyType = ProxyType.Dxgi; }
    }
    public bool IsWinmmSelected
    {
        get => SelectedProxyType == ProxyType.Winmm;
        set { if (value) SelectedProxyType = ProxyType.Winmm; }
    }

    /// <summary>
    /// Detected games. Non-replaceable: items are added/removed in place so that
    /// per-row PropertyChanged notifications from the DataGrid keep working.
    /// Replacing the collection (the previous approach) caused stale visuals
    /// because new ItemsSource swaps don't re-bind row containers reliably when
    /// the underlying items are the same instances.
    /// </summary>
    public ObservableCollection<DetectedGame> Games { get; } = new();

    /// <summary>
    /// Drives available for the generic (non-Steam) scan. Populated lazily on
    /// first switch to Scan Drives mode (or via Refresh Drives). Each carries an
    /// IsSelected checkbox and its physical-disk number for grouping.
    /// </summary>
    public ObservableCollection<DriveDescriptor> Drives { get; } = new();

    // Two-way mirror properties for the Steam / Scan-Drives radio pair.
    public bool IsSteamSource
    {
        get => !ScanDrivesMode;
        set { if (value) ScanDrivesMode = false; }
    }
    public bool IsDriveSource
    {
        get => ScanDrivesMode;
        set { if (value) ScanDrivesMode = true; }
    }

    /// <summary>Whether any games are selected for batch operations.</summary>
    public bool HasSelection => Games.Any(g => g.IsSelected);

    public ProxyDeployViewModel(IProxyDeployService deploy, ILoggingService log,
                                IPlatformService? platform = null)
    {
        _deploy = deploy;
        _log = log;
        _platform = platform;

        UpdateSourceDllInfo();
    }

    /// <summary>
    /// Locate the source DLL for the currently-selected proxy type.
    /// The proxy/ subdirectory next to the UI executable is kept separate
    /// so Windows DLL search order doesn't load our version.dll into the
    /// UI process itself.
    /// </summary>
    private void UpdateSourceDllInfo()
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            var dllName = SelectedProxyType.GetDllName();
            var dllPath = Path.Combine(exeDir, "proxy", dllName);
            SourceDllPath = dllPath;
            SourceDllVersion = File.Exists(dllPath)
                ? _deploy.GetDllVersion(dllPath)
                : null;

            StatusText = File.Exists(dllPath)
                ? $"Source: {dllName} v{SourceDllVersion ?? "?"}"
                : $"Source DLL not found: {dllPath}";
        }
        catch (Exception ex)
        {
            StatusText = $"Init error: {ex.Message}";
            _log.Error("ProxyDeploy", $"ViewModel init failed: {ex}");
        }
    }

    /// <summary>
    /// Triggered by the source-generated SelectedProxyType setter
    /// (from [ObservableProperty]). Re-resolves the source DLL path for
    /// the new proxy type and refreshes deploy status of detected games.
    /// </summary>
    partial void OnSelectedProxyTypeChanged(ProxyType value)
    {
        UpdateSourceDllInfo();
        // Notify radio button mirror properties so XAML stays in sync.
        OnPropertyChanged(nameof(IsVersionSelected));
        OnPropertyChanged(nameof(IsDinput8Selected));
        OnPropertyChanged(nameof(IsDxgiSelected));
        OnPropertyChanged(nameof(IsWinmmSelected));

        // If we already have games, re-evaluate their deploy status against
        // the new proxy type. Fire-and-forget — UI doesn't block on toggle.
        if (Games.Count > 0 && File.Exists(SourceDllPath))
        {
            // Supersede any in-flight refresh. The radios carry no IsEnabled binding and this
            // is a property-changed handler rather than a command, so nothing throttled it:
            // two quick clicks started two refreshes over the same Games, and whichever
            // CONTINUATION resumed last wrote the grid — which may be the one for the type the
            // radio no longer shows. Cancel + re-check, the idiom InstanceFinderViewModel:242
            // already uses for its own re-run. (audit #5 AE4)
            _typeRefreshCts?.Cancel();
            _typeRefreshCts?.Dispose();
            _typeRefreshCts = new CancellationTokenSource();
            _ = RefreshAfterTypeChangeAsync(value, _typeRefreshCts.Token);
        }
    }

    /// <summary>Cancels the previous type-change refresh so only the newest one applies.</summary>
    private CancellationTokenSource? _typeRefreshCts;

    /// <summary>A set of <c>DetectedGame.BinariesDir</c> keys for the games an operation
    /// failed on, handed to the refresh that follows it so the failure reason survives.
    /// Case-insensitive because it is compared against a Windows path.</summary>
    private static HashSet<string> NewBinariesDirSet() => new(StringComparer.OrdinalIgnoreCase);

    private async Task RefreshAfterTypeChangeAsync(ProxyType forType, CancellationToken ct)
    {
        try
        {
            await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, forType, ct: ct);

            // Cancellation stops a superseded refresh that is still COMPUTING, but not one that
            // had already finished and was about to apply — the service does its I/O in a
            // cancellable worker and then writes the grid AFTER that await returns. A cancel
            // landing in that window is too late, and the stale write goes through. So any
            // refresh that finds the radio has moved on corrects what it just wrote; without
            // this the grid keeps showing a type nobody selected and, as the finding put it,
            // "nothing ever re-runs".
            //
            // It must NOT gate on `ct`, and must not reuse it: the call that needs to correct
            // itself is by definition the SUPERSEDED one, so its own token is exactly the one
            // that was cancelled. Take the current one instead. One correction, not a loop — a
            // type change during it starts its own handler, which owns the outcome from there.
            if (forType != SelectedProxyType)
                await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, SelectedProxyType,
                                                       ct: _typeRefreshCts?.Token ?? default);
        }
        catch (OperationCanceledException) { /* superseded by a newer type change */ }
        catch (Exception ex)
        {
            _log.Warn("ProxyDeploy", $"Refresh after type change failed: {ex.Message}");
        }
    }

    /// <summary>Toggling the suggestion opt-in re-evaluates the grid (populate or
    /// clear the Suggested column). Fire-and-forget — the UI doesn't block.</summary>
    partial void OnLkgSuggestEnabledChanged(bool value)
    {
        if (Games.Count > 0)
            _ = ApplyProxySuggestionsAsync();
    }

    /// <summary>Compute the per-game proxy suggestion (import table + remembered
    /// pick) for every scanned game. Gated on <see cref="LkgSuggestEnabled"/> —
    /// when off, the service clears the suggestion fields. Advisory only.</summary>
    private async Task ApplyProxySuggestionsAsync(CancellationToken ct = default)
    {
        try
        {
            // Snapshot the known-good maps/set (taken on the calling — UI — thread)
            // so the background enrichment reads immutable copies; this avoids a race
            // with RecordConfirmedProxy / RememberInjection / deploy mutating them.
            var confirmed = new Dictionary<string, ProxyType>(ConfirmedProxyByExe, StringComparer.OrdinalIgnoreCase);
            var remembered = new Dictionary<string, ProxyType>(LastManualProxyByGame, StringComparer.OrdinalIgnoreCase);
            var injected = new HashSet<string>(InjectedGameExes, StringComparer.OrdinalIgnoreCase);

            await _deploy.ApplyProxySuggestionsAsync(
                Games, confirmed, remembered, injected, LkgSuggestEnabled, ct);
        }
        catch (OperationCanceledException) { /* scan cancelled */ }
        catch (Exception ex)
        {
            _log.Warn("ProxyDeploy", $"Proxy suggestion pass failed: {ex.Message}");
        }
    }

    /// <summary>Record that the user successfully loaded a game via UI injection
    /// (keyed by the .exe file name). Persists + re-evaluates the suggestion column
    /// so an injection-only game shows its known-good method. No-op if already known.</summary>
    private void RememberInjection(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return;
        string key = Path.GetFileName(exePath);
        if (string.IsNullOrEmpty(key)) return;

        if (InjectedGameExes.Add(key))
        {
            RequestOptionSave?.Invoke();
            if (Games.Count > 0)
                _ = ApplyProxySuggestionsAsync();
        }
    }

    /// <summary>
    /// Record a proxy CONFIRMED to have loaded a game — called from the connection
    /// stability gate (DLL self-reported a proxy load_mode + session stayed alive).
    /// Keyed by the .exe file name (from EngineState.ModuleName). Persists + re-runs
    /// the suggestion so the game upgrades to "confirmed working". Must be called on
    /// the UI thread (mutation + snapshot serialize there). No-op on a non-proxy DLL
    /// name or if the same confirmation is already recorded.
    /// </summary>
    public void RecordConfirmedProxy(string? exeName, string? proxyDllName)
    {
        if (string.IsNullOrEmpty(exeName)) return;
        if (ProxyTypeExtensions.FromDllName(proxyDllName) is not ProxyType type) return;

        if (!ConfirmedProxyByExe.TryGetValue(exeName, out var prev) || prev != type)
        {
            ConfirmedProxyByExe[exeName] = type;
            RequestOptionSave?.Invoke();
            if (Games.Count > 0)
                _ = ApplyProxySuggestionsAsync();
        }
    }

    [RelayCommand]
    private async Task ScanAsync(CancellationToken ct)
    {
        // No entry guard at all before this. That made Scan the one operation able to start
        // while UpdateAll was suspended at an await — and its Games.Clear() below is exactly
        // what invalidated UpdateAll's enumerator. (AE7's trigger)
        using var busy = TryBeginExclusive("Scan Steam");
        if (busy is null) { LastOperationResult = BusyMessage(); return; }

        try
        {
            ClearError();
            StatusColor = StatusNeutral;
            StatusText = "Detecting Steam libraries...";
            LastOperationResult = null;

            var libraries = await _deploy.GetSteamLibraryFoldersAsync(ct);
            if (libraries.Count == 0)
            {
                StatusText = "No Steam libraries found";
                return;   // the gate releases on dispose
            }

            StatusText = $"Scanning {libraries.Count} library folder(s)...";
            var found = await _deploy.FindUeGamesAsync(libraries, ct);

            Games.Clear();
            foreach (var g in found) Games.Add(g);

            if (Games.Count > 0 && File.Exists(SourceDllPath))
            {
                StatusText = "Checking deploy status...";
                await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, SelectedProxyType, ct: ct);
            }

            // Per-game proxy suggestion (import table + remembered pick), once per scan.
            await ApplyProxySuggestionsAsync(ct);

            StatusText = $"Found {Games.Count} UE game(s)";
            OnPropertyChanged(nameof(HasSelection));
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled";
        }
        catch (Exception ex)
        {
            StatusText = "Scan failed";
            StatusColor = StatusError;
            SetError(ex);
            _log.Error("ProxyDeploy", $"Scan failed: {ex.Message}");
        }
    }

    /// <summary>Triggered by the source-generated ScanDrivesMode setter. Keeps
    /// the radio mirror props in sync and lazily loads drives the first time the
    /// user switches to Scan Drives mode.</summary>
    partial void OnScanDrivesModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSteamSource));
        OnPropertyChanged(nameof(IsDriveSource));
        if (value && Drives.Count == 0 && !IsScanning)
            LoadDrivesCommand.Execute(null);
    }

    /// <summary>Guards <see cref="LoadDrivesAsync"/> against itself. The condition that fires it —
    /// <c>Drives.Count == 0</c> — stays true until the load COMPLETES, and ICommand.Execute does
    /// not consult CanExecute, so toggling the source radio off and on during the load starts a
    /// second one. Its <c>Drives.Clear()</c> then throws away the DetectedDrive instances the user
    /// has already ticked (IsSelected lives on them), silently resetting the drive selection.</summary>
    private bool _loadingDrives;

    [RelayCommand]
    private async Task LoadDrivesAsync(CancellationToken ct)
    {
        if (_loadingDrives) return;
        _loadingDrives = true;
        try
        {
            var drives = await _deploy.GetScannableDrivesAsync(ct);
            Drives.Clear();
            foreach (var d in drives) Drives.Add(d);
            StatusText = $"{Drives.Count} drive(s) available — select and scan";
            StatusColor = StatusNeutral;
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            _log.Warn("ProxyDeploy", $"LoadDrives failed: {ex.Message}");
        }
        finally
        {
            _loadingDrives = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanDrivesAsync(CancellationToken ct)
    {
        var selected = Drives.Where(d => d.IsSelected).ToList();
        if (selected.Count == 0)
        {
            LastOperationResult = "No drives selected";
            return;
        }

        using var busy = TryBeginExclusive("Scan drives");
        if (busy is null) { LastOperationResult = BusyMessage(); return; }

        try
        {
            ClearError();
            IsScanningDrives = true;
            StatusColor = StatusNeutral;
            StatusText = "Scanning drives for UE games...";
            LastOperationResult = null;

            // Constructed on the UI thread → callback marshals back to the UI thread.
            var progress = new Progress<DriveScanProgress>(p =>
                StatusText = $"{p.CurrentDrive} {p.Phase} — {p.GamesFound} found");

            var found = await _deploy.FindUeGamesOnDrivesAsync(selected, progress, ct);

            Games.Clear();
            foreach (var g in found) Games.Add(g);

            if (Games.Count > 0 && File.Exists(SourceDllPath))
            {
                StatusText = "Checking deploy status...";
                await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, SelectedProxyType, ct: ct);
            }

            // Per-game proxy suggestion (import table + remembered pick), once per scan.
            await ApplyProxySuggestionsAsync(ct);

            StatusText = $"Found {Games.Count} UE game(s)";
            OnPropertyChanged(nameof(HasSelection));
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled";
        }
        catch (Exception ex)
        {
            StatusText = "Scan failed";
            StatusColor = StatusError;
            SetError(ex);
            _log.Error("ProxyDeploy", $"Drive scan failed: {ex.Message}");
        }
        finally
        {
            IsScanningDrives = false;   // IsScanning is the gate's; it releases on dispose
        }
    }

    // ── Inject into a running game (Proxy Deploy button → process picker) ──

    /// <summary>Set by the panel code-behind: opens the process picker window and
    /// returns the chosen process (or null on cancel). A delegate so the VM never
    /// references a View.</summary>
    public Func<Task<GameProcessInfo?>>? PickProcessAsync { get; set; }

    // ── Leftover ("orphan") proxy cleanup ────────────────────────────────────
    //
    // Report-first, per-row opt-in. Every measured route to data loss in this feature lives in the
    // delete half; the detection half's worst outcome is a wrong row in a list. So: rows default
    // UNCHECKED, there is deliberately NO select-all, the scan never runs as part of Scan Steam or
    // Refresh, and the confirmation lists the exact paths before anything moves.

    /// <summary>Leftover proxy DLLs found by the cleanup scan. NEVER merged into
    /// <c>Games</c> — "Update all" walks <c>Games</c> and would redeploy a fresh proxy into the
    /// uninstalled game's folder, i.e. re-create exactly what this feature removes.</summary>
    public ObservableCollection<OrphanProxy> Orphans { get; } = new();

    [ObservableProperty] private bool _orphanScanRan;
    /// <summary>Folders the last orphan scan examined. An empty report must be able to say
    /// what it LOOKED AT, or it is indistinguishable from a scan that never ran.</summary>
    private int _orphanFoldersExamined;

    /// <summary>Only say "none found" after a scan has actually run — before that, silence.</summary>
    public bool ShowNoOrphansFound => OrphanScanRan && Orphans.Count == 0;

    /// <summary>Delete-button label, kept in en.axaml per the UI-strings rule.</summary>
    public string DeleteOrphansLabel =>
        Res.Format("str.ProxyDeploy.Orphans.Delete", SelectedOrphanCount);

    /// <summary>Set by the panel code-behind: shows the confirmation window and returns whether the
    /// user agreed. A delegate for the same reason <see cref="PickProcessAsync"/> is one.</summary>
    public Func<IReadOnlyList<OrphanProxy>, Task<bool>>? ConfirmOrphanRemovalAsync { get; set; }

    /// <summary>How many rows are checked. Re-raised manually at every mutation site, matching this
    /// VM's existing <c>HasSelection</c> idiom (there is no NotifyComputedProperties helper here).</summary>
    public int SelectedOrphanCount => Orphans.Count(o => o.IsSelected && o.IsActionable);

    /// <summary>True when the delete button should be enabled.</summary>
    public bool HasOrphanSelection => SelectedOrphanCount > 0;

    /// <summary>Re-raise the orphan selection computed properties. Called by the view when a row
    /// checkbox toggles, because a change inside a collection item is not observed by the collection.</summary>
    public void NotifyOrphanSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedOrphanCount));
        OnPropertyChanged(nameof(HasOrphanSelection));
        OnPropertyChanged(nameof(DeleteOrphansLabel));
        OnPropertyChanged(nameof(ShowNoOrphansFound));
        OnPropertyChanged(nameof(CanWriteOrphanReport));
    }

    private void OnOrphanRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OrphanProxy.IsSelected) or nameof(OrphanProxy.IsRemoved))
            NotifyOrphanSelectionChanged();
    }

    /// <summary>Reveal a leftover folder in Explorer so the user can look before agreeing.</summary>
    [RelayCommand]
    private async Task OpenOrphanFolderAsync(OrphanProxy? row)
    {
        if (row == null || _platform == null) return;
        try { await _platform.RevealInExplorerAsync(row.DllDirectory); }
        catch (Exception ex)
        {
            _log.Warn("ProxyDeploy", $"Could not open {row.DllDirectory}: {ex.Message}");
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanOrphansAsync(CancellationToken ct)
    {
        // Also blocked during a removal: a scan clears Orphans, which would pull the rows out from
        // under a running delete and erase its per-row report. TryBeginExclusive tests
        // IsRemovingOrphans too, so that half of the rule is now stated in one place.
        using var busy = TryBeginExclusive("Find leftovers");
        if (busy is null) { LastOperationResult = BusyMessage(); return; }

        try
        {
            ClearError();
            IsScanningOrphans = true;
            StatusColor = StatusNeutral;
            StatusText = "Looking for leftover proxy DLLs...";
            LastOperationResult = null;

            // Constructed on the UI thread → the callback marshals back to it.
            // Keep the last examined count: an empty report has to be able to say what it
            // LOOKED AT, or it proves nothing.
            int examined = 0;
            var progress = new Progress<OrphanScanProgress>(p => {
                examined = p.Examined;
                StatusText = $"Checking {p.Examined} folder(s) — {p.Found} leftover(s) found";
            });

            var found = await _deploy.FindOrphanProxiesAsync(
                OrphanScanSources.SteamShapeScan | OrphanScanSources.DeployLog | OrphanScanSources.DllLoadLog,
                LiveBinariesDirs(), progress, ct);

            foreach (var old in Orphans) old.PropertyChanged -= OnOrphanRowChanged;
            Orphans.Clear();
            foreach (var o in found)
            {
                // A change INSIDE a collection item is not observed by the collection, so the
                // checked-count would never update. Subscribe per row (and unsubscribe above, or a
                // repeated scan leaks handlers into rows the list no longer shows).
                o.PropertyChanged += OnOrphanRowChanged;
                Orphans.Add(o);
            }
            OrphanScanRan = true;
            _orphanFoldersExamined = examined;
            OnPropertyChanged(nameof(CanWriteOrphanReport));
            WriteOrphanReportCommand.NotifyCanExecuteChanged();
            NotifyOrphanSelectionChanged();

            // Name the NEXT action explicitly, and quote the button label verbatim ("Report…",
            // ellipsis and all) so it can be matched on screen. The scan finishing was being read
            // as "the report has been produced" — a fair assumption, since a scan that reports its
            // findings on screen looks finished — and the file was then never written. Saying which
            // button writes it costs one clause and removes the guess. The clean case also states
            // the coverage, because "found nothing" is only reassuring next to "…out of how many".
            // Blocked rows are counted SEPARATELY. A row that cannot be removed here (no working
            // Recycle Bin on its volume) is still a real leftover the user needs to know about, but
            // folding it into "N leftover proxy DLL(s) … exactly what would go" would promise an
            // action for something that is going nowhere.
            int blocked = Orphans.Count(o => !o.IsActionable);
            SetOperationResult(
                Orphans.Count == 0
                    ? $"No leftover proxy DLLs found ({_orphanFoldersExamined} folder(s) examined) — "
                      + "press “Report…” to save this result as a file."
                    : $"Found {Orphans.Count} leftover proxy DLL(s) — nothing removed yet."
                      + (blocked > 0
                            ? $" {blocked} cannot be removed from here — read the row for why."
                            : "")
                      + " Press “Report…” for a dry run of exactly what would go.",
                0);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled";
        }
        catch (Exception ex)
        {
            StatusText = "Leftover scan failed";
            StatusColor = StatusError;
            SetError(ex);
            _log.Error("ProxyDeploy", $"Orphan scan failed: {ex.Message}");
        }
        finally
        {
            IsScanningOrphans = false;   // IsScanning is the gate's; it releases on dispose
        }
    }

    /// <summary>True once a scan has RUN — not once it has found something.
    ///
    /// <para>A clean scan must still be able to write a report, and that is not a nicety: with no
    /// artifact, "scanned everything and found nothing" and "the scan never ran / looked in the
    /// wrong place / failed silently" are indistinguishable a week later. The report is the only
    /// durable record that the sweep happened and what it covered. <c>BuildReport</c> has always
    /// handled the empty case ("No leftover proxy DLLs were found."); it was this gate that made
    /// that text unreachable.</para></summary>
    public bool CanWriteOrphanReport => OrphanScanRan;

    /// <summary>
    /// Binaries folders of games we already know are installed. Passed to BOTH the scan and the
    /// removal so the installed-game veto is applied at both ends; giving the removal an empty set
    /// made the deleting path weaker than the scan that authorised it.
    /// </summary>
    private IReadOnlySet<string> LiveBinariesDirs() => new HashSet<string>(
        Games.Select(g => g.BinariesDir).Where(d => !string.IsNullOrEmpty(d)),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// REPORT (dry run): write what Execute WOULD do to a .txt and open it. Deliberately a separate
    /// button from the delete: the whole difficulty of this feature is convincing the user we will not
    /// remove the wrong thing, and the honest answer to that is to hand them the plan in a file they
    /// can read at their own pace, outside a modal dialog, before anything is authorised.
    ///
    /// <para>It shares the row objects with the delete path, so the two cannot describe different
    /// plans. What it cannot promise is that the world will not change in between — the report says so
    /// itself, and Execute re-evaluates from disk.</para>
    /// </summary>
    [RelayCommand]
    private async Task WriteOrphanReportAsync()
    {
        // Gate on "a scan has run", not on "it found something" — see CanWriteOrphanReport.
        // Log BOTH outcomes. Three rounds were spent on "I ran Report and no file appeared"
        // with nothing in the log to say whether the command had even been entered — the
        // success path logged nothing and the refusal logged nothing, so "it never ran" and
        // "it ran and failed" were indistinguishable. Same defect this whole audit is about.
        if (!OrphanScanRan)
        {
            _log.Info("ProxyDeploy", "Leftover report refused: no orphan scan has run in this session");
            LastOperationResult = "Nothing to report — run the scan first";
            return;
        }
        if (_platform == null) { LastOperationResult = "Report unavailable on this platform"; return; }

        try
        {
            ClearError();
            // GetAppDataPath() is %LOCALAPPDATA% itself, so the app's own folder segment
            // has to be added — every other consumer does. Without it the written record
            // of a destructive cleanup landed in %LOCALAPPDATA%\Reports: outside the
            // System-tab data wipe, and outside "send me your app data folder". Audit #4 B38.
            string dir = Path.Combine(_platform.GetAppDataPath(),
                                      Constants.LogFolderName, "Reports");
            Directory.CreateDirectory(dir);
            // Timestamped rather than overwritten so two scans can be compared, and so a report the
            // user is still reading is never replaced under them.
            string file = Path.Combine(dir, $"leftover-proxies-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

            // Same EntryAssembly + Version.Revision trick the System tab uses — see
            // PointerPanelViewModel.ReadUiBuildNumber for why "build" lives in Revision.
            int build = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.Revision ?? 0;

            string text = Services.ProxyOrphanScanner.BuildReport(
                Orphans.ToList(),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                build > 0 ? build.ToString() : "unknown",
                _orphanFoldersExamined);

            await File.WriteAllTextAsync(file, text);
            PruneAgedReports(dir);
            await _platform.OpenWithShellAsync(file);

            _log.Info("ProxyDeploy",
                      $"Leftover report written: {file} ({Orphans.Count} row(s), " +
                      $"{_orphanFoldersExamined} folder(s) examined)");
            SetOperationResult($"Report written: {file}", 0);
        }
        catch (Exception ex)
        {
            StatusColor = StatusError;
            SetError(ex);
            _log.Error("ProxyDeploy", $"Writing the leftover report failed: {ex.Message}");
        }
    }

    /// <summary>True while a removal is running. Separate from <see cref="IsScanning"/> so the panel
    /// can disable the scan buttons without the SCAN's cancel button appearing during a delete —
    /// which would offer to cancel an operation it is not wired to.</summary>
    [ObservableProperty] private bool _isRemovingOrphans;

    /// <summary>
    /// Age out old reports. Runs when a new report is WRITTEN, not at startup: these are
    /// user-initiated artefacts, so a launch that never touches the feature must not silently delete
    /// anything. Best-effort — failing to tidy up must never stop the report the user asked for.
    /// </summary>
    private void PruneAgedReports(string dir)
    {
        try
        {
            var files = Directory.EnumerateFiles(dir, "leftover-proxies-*.txt")
                .Select(p => (Path: p, Written: File.GetLastWriteTime(p)))
                .ToList();

            foreach (string old in Services.ProxyOrphanScanner.SelectExpiredReports(
                         files, DateTime.Now, Constants.ReportMaxAgeDays))
            {
                try
                {
                    File.Delete(old);
                    _log.Info("ProxyDeploy", $"Removed aged leftover report {Path.GetFileName(old)}");
                }
                catch { /* one undeletable report must not stop the rest */ }
            }
        }
        catch
        {
            // The report itself is already written; tidying is not worth surfacing an error for.
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedOrphansAsync(CancellationToken ct)
    {
        // IsScanning is shared with Scan Steam / Scan Drives / Refresh in this panel, and a delete
        // running concurrently with a scan would have the first finisher clear the flag for both.
        if (IsScanning || IsRemovingOrphans)
        {
            LastOperationResult = "Wait for the current operation to finish";
            return;
        }

        var picked = Orphans.Where(o => o.IsSelected && o.IsActionable).ToList();
        if (picked.Count == 0) { LastOperationResult = "Nothing checked"; return; }

        // The confirmation is mandatory: with no delegate wired we refuse rather than proceed
        // silently, because the dialog is where the exact paths are disclosed.
        if (ConfirmOrphanRemovalAsync == null)
        {
            LastOperationResult = "Confirmation dialog unavailable — nothing was removed";
            return;
        }
        if (!await ConfirmOrphanRemovalAsync(picked))
        {
            LastOperationResult = "Cancelled — nothing was removed";
            return;
        }

        int ok = 0, fail = 0, files = 0, dirs = 0;
        var live = LiveBinariesDirs();
        try
        {
            IsRemovingOrphans = true;
            foreach (var row in picked)
            {
                ct.ThrowIfCancellationRequested();
                var result = await _deploy.RemoveOrphanProxyAsync(row, live, ct);

                // Bound-property writes happen HERE, after the await, on the caller's thread.
                row.StatusText = result.Message;
                row.StatusIsError = !result.Success;
                row.IsRemoved = result.Success;
                // Unchecked either way: a pass is over when it is over. A failed row that stayed
                // checked would re-submit itself on the next click of a button whose label still
                // read "Delete checked (1)" — the retry has to be something the user asks for.
                row.IsSelected = false;

                if (result.Success)
                {
                    ok++;
                    files += result.FilesRecycled;
                    dirs += result.DirsRemoved;
                    DropOrphanRow(row);
                }
                else fail++;
            }
            NotifyOrphanSelectionChanged();
            SetOperationResult(DescribeCleanup(ok, picked.Count, files, dirs, fail, cancelled: false), fail);
        }
        catch (OperationCanceledException)
        {
            // Report what DID happen — a cancel that discards the tally would hide a half-pruned chain.
            NotifyOrphanSelectionChanged();
            SetOperationResult(DescribeCleanup(ok, picked.Count, files, dirs, fail, cancelled: true), fail);
        }
        catch (Exception ex)
        {
            StatusColor = StatusError;
            SetError(ex);
            _log.Error("ProxyDeploy", $"Orphan cleanup failed: {ex.Message}");
        }
        finally
        {
            IsRemovingOrphans = false;
        }
    }

    /// <summary>
    /// Take a cleaned row off the list, so what is on screen is what is still on disk.
    ///
    /// <para>This is the whole reason no automatic re-scan runs here, and the equivalence is exact
    /// rather than approximate: <c>Success</c> from <c>RemoveOrphanProxyAsync</c> means the proxy
    /// DLL is off disk (recycled, or already gone — a partial FOLDER prune is still a success), and
    /// the scan enumerates rows BY that DLL. A re-scan therefore could not find this row again, so
    /// dropping it produces the same list a scan would, without the seconds and without needing the
    /// scan's own guard against running while a removal is in flight.</para>
    ///
    /// <para>The re-scan is also strictly WORSE for the rows that stay: it would re-find every
    /// FAILED row with a blank status, and that status — "in use by a running program", "read-only,
    /// left alone deliberately" — is the only actionable output a failed delete produces.</para>
    ///
    /// <para>Unsubscribes first, mirroring the scan's own cleanup: the handler holds a reference
    /// back into this ViewModel.</para>
    /// </summary>
    private void DropOrphanRow(OrphanProxy row)
    {
        row.PropertyChanged -= OnOrphanRowChanged;
        Orphans.Remove(row);
    }

    /// <summary>
    /// Summary line for a cleanup pass.
    ///
    /// <para>It carries the file/folder TALLY because the rows that would have shown it are gone by
    /// the time anyone reads it — the per-row success messages are the one thing dropping rows
    /// costs, so the totals have to survive somewhere on screen. Per-row detail for both outcomes
    /// (including why a folder prune stopped early) is in the ProxyDeploy log regardless.</para>
    /// </summary>
    private static string DescribeCleanup(int ok, int attempted, int files, int dirs, int fail, bool cancelled)
    {
        string text = cancelled
            ? $"Cleanup cancelled after {ok} of {attempted} leftover(s)"
            : $"Cleaned {ok} of {attempted} leftover(s)";
        if (files > 0 || dirs > 0)
            text += $" — {files} file(s) recycled, {dirs} folder(s) removed";
        // Say where the failures went, or an emptied list reads as "everything worked".
        if (fail > 0)
            text += $"; {fail} still listed with the reason";
        return text;
    }

    /// <summary>Set by MainWindowViewModel: connect the pipe after a successful
    /// inject (best-effort auto-connect).</summary>
    public Func<Task>? RequestConnectAsync { get; set; }

    /// <summary>True once the pipe is actually connected. Needed because
    /// <see cref="RequestConnectAsync"/> is fire-and-forget — without a way to ASK, the
    /// post-inject connect could only be attempted once and hoped for.</summary>
    public Func<bool>? IsConnectedProbe { get; set; }

    /// <summary>Silences the top-bar "Connection Error" while the retry owns the message.</summary>
    public Action<bool>? SetConnectErrorSuppression { get; set; }

    /// <summary>How long to keep retrying the post-inject connect.</summary>
    /// <remarks>
    /// The injected DLL scans BEFORE it opens its pipe (the proxy path is the opposite —
    /// it opens the pipe first and scans on request), so the pipe simply does not exist
    /// for the length of a full AOB scan. Measured on a stock UE 5.4 Development package:
    /// 1 s auto-start delay + 7.8 s scan = the pipe appeared 8.8 s after injection, and
    /// the single immediate attempt reported "The operation has timed out."
    /// 45 s covers a slow scan with room to spare; each attempt is cheap and the status
    /// line counts them, so a genuinely dead DLL is obvious rather than silent.
    /// </remarks>
    public static readonly TimeSpan PostInjectConnectWindow = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan PostInjectRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>Load injection-candidate processes for the picker. showAll=false
    /// returns only UE games.</summary>
    public async Task<IReadOnlyList<GameProcessInfo>> ListGameProcessesAsync(bool showAll)
    {
        var all = await _deploy.ListGameProcessesAsync();
        return showAll ? all : all.Where(p => p.IsUe).ToList();
    }

    [RelayCommand]
    private async Task InjectIntoRunningGameAsync()
    {
        StatusColor = StatusNeutral;
        LastOperationColor = StatusNeutral;
        ClearError();

        // The injectable DLL sits next to the UI exe (dist\UE5Dumper.dll).
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var dllPath = Path.Combine(exeDir, "UE5Dumper.dll");
        if (!File.Exists(dllPath))
        {
            SetError($"UE5Dumper.dll not found next to the app: {dllPath}");
            return;
        }

        if (PickProcessAsync is null) return;
        GameProcessInfo? target;
        try
        {
            target = await PickProcessAsync();
        }
        catch (Exception ex)
        {
            SetError(ex);
            return;
        }
        if (target is null) return;   // cancelled

        // Already loaded (proxy / prior inject / CE .CT): re-injecting would
        // double-load UE5Dumper.dll and fight over the pipe. Skip straight to
        // connecting to the DLL that's already running.
        if (target.DumperLoaded)
        {
            _log.Info("ProxyDeploy",
                $"PID {target.Pid} already has the dumper loaded ({target.DumperLoadMode} "
                + $"{target.DumperVersion}) — connecting instead of re-injecting");
            SetOperationResult(
                $"{target.Name} already has {target.DumperStatusLine} — connecting...", 0);
            // If the module list shows this game was loaded via injection (not a
            // proxy), that's a known-good injection — remember it for the suggestion.
            if (target.DumperLoadMode?.Contains("inject", StringComparison.OrdinalIgnoreCase) == true)
                RememberInjection(target.Path);
            if (RequestConnectAsync is not null)
            {
                try { await RequestConnectAsync(); }
                catch (Exception ex) { _log.Warn("ProxyDeploy", $"Connect to already-loaded PID {target.Pid} failed: {ex.Message}"); }
            }
            return;
        }

        StatusText = $"Injecting UE5Dumper.dll into {target.Name} (PID {target.Pid})...";
        InjectResult result;
        try
        {
            result = await _deploy.InjectDllAsync(target.Pid, dllPath);
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Inject failed";
            StatusColor = StatusError;
            return;
        }

        // Game runs elevated → OpenProcess Access Denied. Auto-retry WITH elevation
        // (a headless UAC-prompt relaunch does just the inject; the UI stays running)
        // unless we're already admin (then elevation can't help).
        if (!result.Ok && result.AccessDenied && !_deploy.IsElevated())
        {
            StatusText = $"Access denied — requesting Administrator for {target.Name}...";
            try
            {
                result = await _deploy.InjectDllElevatedAsync(target.Pid, dllPath);
            }
            catch (Exception ex)
            {
                SetError(ex);
                StatusText = "Inject failed";
                StatusColor = StatusError;
                return;
            }
        }

        if (!result.Ok)
        {
            SetError(result.ErrorMessage ?? "Injection failed");
            StatusText = "Inject failed";
            StatusColor = StatusError;
            _log.Warn("ProxyDeploy", $"Inject into PID {target.Pid} failed: {result.ErrorMessage}");
            return;
        }

        _log.Info("ProxyDeploy", $"Injected UE5Dumper.dll into PID {target.Pid} (HMODULE=0x{result.HModule:X})");
        SetOperationResult($"Injected into {target.Name} (PID {target.Pid}) — connecting...", 0);
        // Injection is a known-good load method for this game — remember it so the
        // Suggested column flags an injection-only game (never had a proxy deployed).
        RememberInjection(target.Path);

        await ConnectWithRetryAsync(target.Name, target.Pid);
    }

    /// <summary>Connect to the freshly injected DLL, retrying until its pipe appears.</summary>
    /// <remarks>
    /// A single immediate attempt cannot work: the injected DLL runs its whole AOB scan
    /// before opening the pipe, so for several seconds there is nothing to connect TO.
    /// The failure looked like a broken injection ("Connection Error / The operation has
    /// timed out") when the injection had in fact succeeded and the DLL was mid-scan —
    /// the log showed the pipe opening 8.8 s later and then waiting for a client that
    /// never came back.
    /// </remarks>
    private async Task ConnectWithRetryAsync(string targetName, int pid)
    {
        if (RequestConnectAsync is null) return;

        var started  = DateTime.UtcNow;
        var deadline = started + PostInjectConnectWindow;
        int attempt  = 0;

        // The retry owns the user-facing message from here: every failed attempt inside the
        // window is expected, not an error, and letting the top bar flash red only to connect
        // successfully moments later is worse than saying nothing.
        SetConnectErrorSuppression?.Invoke(true);
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                attempt++;
                try { await RequestConnectAsync(); }
                catch (Exception ex)
                {
                    _log.Warn("ProxyDeploy", $"Auto-connect after inject attempt {attempt} failed: {ex.Message}");
                }

                // No probe wired (tests, or an older composition) — keep the old
                // single-shot behaviour rather than spinning for 45 s on no information.
                if (IsConnectedProbe is null) return;
                if (IsConnectedProbe())
                {
                    SetOperationResult(
                        $"Injected into {targetName} (PID {pid}) — connected after "
                        + $"{(DateTime.UtcNow - started).TotalSeconds:F0}s ({attempt} attempts).", 0);
                    return;
                }

                // Show BOTH numbers. Elapsed alone leaves "is it nearly out of patience?"
                // unanswerable, and a bare attempt count says nothing about how long is left.
                SetOperationResult(
                    $"Injected into {targetName} (PID {pid}) — waiting for the DLL to finish its "
                    + $"AOB scan and open its pipe: {(DateTime.UtcNow - started).TotalSeconds:F0}s elapsed, "
                    + $"{Math.Max(0, (deadline - DateTime.UtcNow).TotalSeconds):F0}s left "
                    + $"(attempt {attempt}).", 0);
                await Task.Delay(PostInjectRetryDelay);
            }
        }
        finally
        {
            SetConnectErrorSuppression?.Invoke(false);
        }

        SetOperationResult(
            $"Injected into {targetName} (PID {pid}), but its pipe never opened within "
            + $"{PostInjectConnectWindow.TotalSeconds:F0}s ({attempt} attempts). The injection "
            + "succeeded — check the DLL's init log.", 0);
        _log.Warn("ProxyDeploy", $"Post-inject connect gave up after {attempt} attempts");
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        if (Games.Count == 0) return;

        // Refresh neither tested nor set the guard, so it could run under a scan that was
        // about to Games.Clear() beneath it, and a deploy could start on top of it. (AE5)
        using var busy = TryBeginExclusive("Refresh");
        if (busy is null) { LastOperationResult = BusyMessage(); return; }

        try
        {
            ClearError();
            StatusColor = StatusNeutral;
            StatusText = "Refreshing status...";
            LastOperationResult = null;

            // Re-read source DLL version (may have changed)
            SourceDllVersion = File.Exists(SourceDllPath)
                ? _deploy.GetDllVersion(SourceDllPath)
                : null;

            await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, SelectedProxyType, ct: ct);
            await ApplyProxySuggestionsAsync(ct);
            StatusText = $"{Games.Count} game(s) — status refreshed";
        }
        catch (Exception ex)
        {
            StatusText = "Refresh failed";
            StatusColor = StatusError;
            SetError(ex);
        }
    }

    [RelayCommand]
    private async Task DeploySelectedAsync(CancellationToken ct)
    {
        StatusColor = StatusNeutral;
        LastOperationColor = StatusNeutral;
        using var busy = TryBeginExclusive("Deploy");
        if (busy is null) { LastOperationResult = BusyMessage(); return; }

        if (!File.Exists(SourceDllPath))
        {
            SetError($"Source DLL not found: {SourceDllPath}");
            return;
        }

        var selected = Games.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0)
        {
            LastOperationResult = "No games selected";
            return;
        }

        ClearError();
        int ok = 0, fail = 0;
        bool pickChanged = false;
        var failedDirs = NewBinariesDirSet();

        foreach (var game in selected)
        {
            ct.ThrowIfCancellationRequested();
            StatusText = $"Deploying to {game.Name}...";

            bool success = await _deploy.DeployAsync(SourceDllPath, game, SelectedProxyType,
                new DeployOptions(ForceSameVersion: ForceOverwrite,
                                  ForeignConsent:   AllowForeignOverwrite), ct);
            if (success)
            {
                ok++;
                // Remember what the user deployed for this game (mini "last known
                // good"), keyed by the stable folder name so it survives reinstall.
                if (!string.IsNullOrEmpty(game.Name))
                {
                    if (!LastManualProxyByGame.TryGetValue(game.Name, out var prev) || prev != SelectedProxyType)
                    {
                        LastManualProxyByGame[game.Name] = SelectedProxyType;
                        pickChanged = true;
                    }
                }
            }
            else { fail++; failedDirs.Add(game.BinariesDir); }
        }

        // Refresh status from disk to ensure DataGrid reflects actual state — EXCEPT for the
        // games this run failed on, whose Status/ErrorMessage DeployAsync just wrote. Their
        // disk state is "file absent", which refreshes to NotDeployed with a blank Error and
        // would leave the reason visible only in the log.
        await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, SelectedProxyType, failedDirs, ct);
        // Reflect the just-recorded pick in the Suggested column immediately.
        await ApplyProxySuggestionsAsync(ct);
        if (pickChanged) RequestOptionSave?.Invoke();

        SetOperationResult($"Deployed: {ok} success, {fail} failed", fail);
    }

    [RelayCommand]
    private async Task UndeploySelectedAsync(CancellationToken ct)
    {
        StatusColor = StatusNeutral;
        LastOperationColor = StatusNeutral;
        // Deploy and Undeploy are DIFFERENT commands, so the toolkit's per-command
        // CanExecute block never applied between them — they could run over the same
        // Binaries folder at once and both write the single result line. (AE6)
        using var busy = TryBeginExclusive("Remove");
        if (busy is null) { LastOperationResult = BusyMessage(); return; }

        var selected = Games.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0)
        {
            LastOperationResult = "No games selected";
            return;
        }

        ClearError();
        int ok = 0, fail = 0;
        var failedDirs = NewBinariesDirSet();

        foreach (var game in selected)
        {
            ct.ThrowIfCancellationRequested();
            StatusText = $"Removing from {game.Name}...";

            // Type-agnostic: removes every proxy flavour of ours in the folder, not
            // just SelectedProxyType (that radio governs deploying).
            bool success = await _deploy.UndeployAsync(game, ct);
            if (success) ok++;
            else { fail++; failedDirs.Add(game.BinariesDir); }
        }

        // Refresh status from disk to ensure DataGrid reflects actual state — but not for the
        // games this run failed on (a locked file, a foreign DLL we refused to touch): their
        // proxy IS still on disk, so the refresh would report a healthy DeployedCurrent and
        // erase the very reason the removal did not happen.
        await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, SelectedProxyType, failedDirs, ct);

        SetOperationResult($"Removed: {ok} success, {fail} failed", fail);
    }

    [RelayCommand]
    private async Task UpdateAllAsync(CancellationToken ct)
    {
        StatusColor = StatusNeutral;
        LastOperationColor = StatusNeutral;
        using var busy = TryBeginExclusive("Update All");
        if (busy is null) { LastOperationResult = BusyMessage(); return; }

        // Resolve the source DLL for EVERY proxy type (all are built side-by-
        // side into <exeDir>/proxy/). Update All updates each game's already-
        // deployed proxy DLL(s) to the latest of the SAME type — independent
        // of the selected radio button. So a new dxgi.dll replaces an old
        // dxgi.dll, a new version.dll replaces an old version.dll, etc. Adding
        // a 4th proxy type needs no change here (iterates the enum).
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var sources = Enum.GetValues<ProxyType>()
            .Select(t => (Type: t, Path: Path.Combine(exeDir, "proxy", t.GetDllName())))
            .Where(s => File.Exists(s.Path))
            .ToList();

        if (sources.Count == 0)
        {
            SetError($"No source proxy DLLs found in {Path.Combine(exeDir, "proxy")}");
            return;
        }

        ClearError();
        int updated = 0, fail = 0, upToDate = 0;
        var failedDirs = NewBinariesDirSet();

        // Snapshot. This used to enumerate the live bound ObservableCollection across an await,
        // so a Scan completing mid-loop (Games.Clear() + re-Add) invalidated the enumerator and
        // the next MoveNext threw InvalidOperationException. The gate above now stops the two
        // from overlapping at all; the snapshot is the belt to that braces, and costs one list.
        // Every other loop in this file already snapshots (`.Where(...).ToList()`). (AE7)
        var targets = Games.ToList();

        try
        {
            foreach (var game in targets)
            {
                ct.ThrowIfCancellationRequested();

                foreach (var (type, srcPath) in sources)
                {
                    string targetDll = Path.Combine(game.BinariesDir, type.GetDllName());

                    // Only update a proxy that is ALREADY deployed (and ours) for
                    // this game — never push a fresh type the user didn't choose.
                    if (!File.Exists(targetDll) || !_deploy.IsOurProxyDll(targetDll))
                        continue;

                    string? srcVer = _deploy.GetDllVersion(srcPath);
                    string? tgtVer = _deploy.GetDllVersion(targetDll);
                    if (srcVer != null && srcVer == tgtVer)
                    {
                        upToDate++;
                        continue;
                    }

                    StatusText = $"Updating {game.Name} ({type.GetDisplayName()})...";
                    // ForeignConsent stays FALSE: the loop above already refuses anything
                    // that is not our proxy, so Update All never needs it and must not
                    // acquire it by 'simplification'.
                    bool success = await _deploy.DeployAsync(srcPath, game, type,
                        new DeployOptions(ForceSameVersion: true, ForeignConsent: false), ct: ct);
                    if (success) updated++;
                    else { fail++; failedDirs.Add(game.BinariesDir); }
                }
            }

            // Refresh status from disk for the currently-selected type's view, keeping the reason
            // on any game this run failed to update (see DeploySelectedAsync).
            await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, SelectedProxyType, failedDirs, ct);

            if (updated == 0 && fail == 0)
            {
                string msg = upToDate > 0
                    ? $"All {upToDate} deployed proxy DLL(s) already up-to-date"
                    : "No deployed proxy DLLs to update";
                LastOperationResult = msg;
                StatusText = msg;
                StatusColor = StatusNeutral;
                LastOperationColor = StatusNeutral;
                _log.Info("ProxyDeploy", msg);
            }
            else
            {
                SetOperationResult($"Updated: {updated}, up-to-date: {upToDate}, failed: {fail}", fail);
            }
        }
        catch (OperationCanceledException)
        {
            // Report what DID get updated. Cancelling is not a reason to hide that N games were
            // already written to — the user needs to know the folders are no longer uniform.
            SetOperationResult($"Update All cancelled — updated: {updated}, failed: {fail}", fail);
        }
        catch (Exception ex)
        {
            // This method had no catch at all, and it is an AsyncRelayCommand: a faulted task on
            // the button path is rethrown onto the UI thread, so an exception here was a crash
            // risk, not merely a tally that never appeared. Sibling commands all catch. (AE7)
            SetOperationResult($"Update All failed — updated: {updated}, failed: {fail}", fail + 1);
            SetError(ex);
            _log.Error("ProxyDeploy", $"Update All failed: {ex.Message}");
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Selection helpers
    // ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var g in Games) g.IsSelected = true;
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private void UnselectAll()
    {
        foreach (var g in Games) g.IsSelected = false;
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private void InvertSelection()
    {
        foreach (var g in Games) g.IsSelected = !g.IsSelected;
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>
    /// Notify that selection changed (called from View).
    /// </summary>
    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
    }

}
