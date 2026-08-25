using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// Tab positions in the <c>MainWindow.axaml</c> TabControl, used by the
/// panel-to-panel navigation handlers that set
/// <see cref="MainWindowViewModel.SelectedTabIndex"/>. This is the single
/// source of truth for those indices — MUST stay in the same order as the
/// &lt;TabItem&gt; elements in MainWindow.axaml. (These indices silently
/// drifted before: the "ClassStruct" navigations hard-coded 7 but GameClassFilter
/// took index 7, pushing ClassStruct to 8.) The tab-switch *read* path
/// (AOBMaker re-check) instead matches on <c>TabItem.Tag</c> in
/// MainWindow.axaml.cs, which is reorder-proof and needs no entry here.
/// </summary>
internal enum MainTabIndex
{
    LiveWalker = 0,
    InstanceFinder = 1,
    PropertySearch = 2,
    InterestingFunctions = 3,
    InterestingProperties = 4,
    ValueSearch = 5,
    Console = 6,
    Teleport = 7,
    GameClassFilter = 8,
    ClassStruct = 9,
    RelatedObjects = 10,
    DumpExplorer = 11,   // offline "Dump All" .jsonl browser
    LiveFuncs = 12,      // Live ProcessEvent Call Profiler (behaviour-based discovery)
    // Fixed tail order: the experimental tabs (hidden unless opted in), then
    // Proxy Deploy (always 2nd-to-last), then System/Pointers (always last) —
    // regardless of any future tab additions. When experimental is off these
    // tabs collapse, so the visible last two are Proxy Deploy + System.
    DetectStats = 13,   // "Detect Player Stats" (P4, experimental)
    Snapshot = 14,
    SpcQuery = 15,
    ClassPivot = 16,
    ProxyDeploy = 17,
    Pointers = 18,   // the "System" tab (str.Tab.Pointers = "System")
}

/// <summary>
/// Main window ViewModel — orchestrates connection and child ViewModels.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private bool _disposed;
    private readonly IPipeClient _pipeClient;
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService _platform;
    // Held so Dispose can detach it from the static PropertyXrefDialog event
    // (the lambda captures `this`; without unsubscribe each VM would leak —
    // matters for the test suite, which builds the VM repeatedly).
    private readonly Action<string> _xrefLocateHandler;

    /// <summary>
    /// Platform service, exposed so the window code-behind can route the
    /// global focus-in IME-close through the same abstraction (the
    /// Platform Abstraction rule forbids direct P/Invoke outside it).
    /// </summary>
    public IPlatformService Platform => _platform;
    private readonly AobUsageService? _aobUsage;
    private readonly IAobMakerBridge? _aobMaker;  // captured so InterestingFunctions handlers can ship AA Scripts
    private readonly IExperimentalGate? _experimentalGate;
    private EngineState? _engineState;

    [ObservableProperty] private string _statusText = "Disconnected";
    [ObservableProperty] private string _windowTitle = "UE5 Dump UI";
    [ObservableProperty] private bool _isConnected;

    /// <summary>Always-visible top-bar banner: true while the game thread is
    /// paused/suspended (not ticking ProcessEvent). Live-camera / function-invoke
    /// features time out in this state, and if Teleport auto-refresh is polling,
    /// its POV reads would starve memory scans behind them — so the user gets a
    /// heads-up on why things feel slow (and that scans still work). Fed by the
    /// DLL's per-response liveness flag via IPipeClient.GameThreadStalledChanged;
    /// reset to false on disconnect so the banner never sticks. Bound directly to
    /// the banner's IsVisible.</summary>
    [ObservableProperty] private bool _gameThreadStalled;

    /// <summary>Filled in when MORE THAN ONE running process currently hosts our dumper DLL.
    /// Empty otherwise, and bound directly to the banner's IsVisible via
    /// <see cref="HasMultipleDumperHosts"/>.
    ///
    /// The pipe name is a single global, so every injected game serves the SAME name and a
    /// connecting client lands on whichever instance happens to be free. There is no way to
    /// ask for a particular game. That produces two bad outcomes the user cannot otherwise
    /// distinguish: a connect that fails because the instances are taken, and — far worse — a
    /// connect that SUCCEEDS against the wrong game and quietly shows its data. Naming the
    /// process we actually reached is the cheapest honest answer until the pipe is
    /// per-process.</summary>
    [ObservableProperty] private string _multipleDumperHostsWarning = "";

    public bool HasMultipleDumperHosts => !string.IsNullOrEmpty(MultipleDumperHostsWarning);

    partial void OnMultipleDumperHostsWarningChanged(string value)
        => OnPropertyChanged(nameof(HasMultipleDumperHosts));

    /// <summary>Warn when more than one running process currently hosts our dumper DLL.
    ///
    /// Fire-and-forget: it enumerates processes, which is slow enough that blocking the connect
    /// path on it would be felt, and a late warning is still useful. Failures are swallowed —
    /// this is advisory, and an enumeration that hits access-denied must never break a
    /// connection that otherwise worked.</summary>
    private async Task CheckForCompetingDumperHostsAsync(EngineState state)
    {
        if (_proxyDeployForChecks == null) return;
        // Process enumeration is slow, so this can land after the user has disconnected
        // and reconnected — or after the disconnect that just cleared the banner. Publish
        // only into the session we were started for. Same shape as ScheduleProxyConfirmation.
        int epoch = _sessionEpoch;
        try
        {
            var procs = await _proxyDeployForChecks.ListGameProcessesAsync();
            if (epoch != _sessionEpoch) return;
            var hosts = procs.Where(p => p.DumperLoaded).ToList();
            // Exclude the process we actually reached (by PID, or by module name when
            // the DLL reports no PID) so the connected game is never listed among its
            // own competitors. Null => 0/1 hosts, nothing to warn about (X9).
            var banner = Helpers.CompetingHostBanner.Build(hosts, state.ProcessId, state.ModuleName);
            if (banner is not { } b) { MultipleDumperHostsWarning = ""; return; }

            MultipleDumperHostsWarning =
                $"{hosts.Count} processes have the dumper DLL loaded. You are connected to "
                + $"{b.ConnectedLabel} — also loaded: {string.Join(", ", b.Others)}. "
                + "The pipe name is shared, so which game you reach is first-come-first-served. "
                + "Close the ones you are not using.";
            _log.Warn(Constants.LogCatInit, MultipleDumperHostsWarning);
        }
        catch (Exception ex)
        {
            _log.Warn(Constants.LogCatInit, $"Competing-dumper-host check failed: {ex.Message}");
        }
    }

    /// <summary>Kept so the post-connect ambiguity check can enumerate processes. Null when the
    /// host did not supply one (tests), in which case the check simply never runs.</summary>
    private readonly IProxyDeployService? _proxyDeployForChecks;

    /// <summary>Global stale-DLL badge shown in the always-visible top bar:
    /// only while connected AND the DLL build differs from / pre-dates the UI's.
    /// Mirrors the per-tab Diagnostics badge (PointerPanelViewModel) but is
    /// visible from every tab, so a hand-deployed old proxy DLL is noticed
    /// before scanning with mismatched offsets. (Re-raised when Pointers'
    /// warning state changes — see ctor — and when IsConnected flips.)</summary>
    public bool ShowBuildMismatchBadge => IsConnected && Pointers.ShowGlobalBuildWarning;
    public string BuildMismatchBadgeText => Pointers.GlobalBuildWarningText;

    /// <summary>Positive counterpart to <see cref="ShowBuildMismatchBadge"/>: true while
    /// connected AND the DLL build matches the UI's. Shown as a subtle "DLL &lt;n&gt;" next to
    /// the version so a current deploy is visibly confirmed — "no badge" alone is ambiguous
    /// with "the warning is broken".</summary>
    public bool ShowDllBuildOk => IsConnected && Pointers.BuildVersionsMatch;
    public string DllBuildOkText => $"DLL {Pointers.DllBuildNumber}";

    /// <summary>Global "unverified UE5.7+ packed layout" badge in the always-visible top bar:
    /// only while connected AND the game runs the *** UNVERIFIED *** packed FUObjectItem layout.
    /// Tells the user that reconstructed addresses and every export are best-effort.</summary>
    public bool ShowPackedLayoutBadge => IsConnected && Pointers.ShowPackedLayoutBadge;
    public string PackedLayoutBadgeText => Pointers.PackedLayoutBadgeText;

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBuildMismatchBadge));
        OnPropertyChanged(nameof(ShowDllBuildOk));
        OnPropertyChanged(nameof(ShowPackedLayoutBadge));
    }
    [ObservableProperty] private bool _needsScan;       // True when connected but scan not yet done (proxy DLL mode)
    [ObservableProperty] private bool _isScanning;      // True while trigger_scan is in progress
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private int _selectedAddressFormatIndex;
    [ObservableProperty] private bool _collapsePointerNodes;
    [ObservableProperty] private int _arrayLimitExponent = 7; // 2^7 = 128
    [ObservableProperty] private int _dropDownLimitExponent = 9; // 2^9 = 512
    [ObservableProperty] private int _csxDrilldownDepth; // 0 = flat (dummy), 1+ = real child structures
    [ObservableProperty] private int _previewLimit = Constants.DefaultPreviewLimit; // Struct preview sub-field count (0-6)
    [ObservableProperty] private int _deepScanElemCapExponent = 8; // 2^8 = 256 (find_by_address deep scan per-container cap)
    [ObservableProperty] private int _ceStringLengthExponent = 8; // 2^8 = 256 (CE String leaf <Length>; floored at 2^4 = 16)
    [ObservableProperty] private int _fabricateArrayCountExponent = 2; // 2^2 = 4 rows (default); 0 = off, 2^N = Copy CE Field array fabricate count

    // Always-visible top-toolbar AOBMaker status (mirrors the per-tab indicators).
    [ObservableProperty] private bool _isAobMakerAvailable;

    /// <summary>True when an AOBMaker bridge was supplied — gates the toolbar status chip.</summary>
    public bool IsAobMakerConfigured => _aobMaker != null;

    /// <summary>Computed array element limit: 2^ArrayLimitExponent (2..16384).</summary>
    public int ArrayLimit => 1 << ArrayLimitExponent;

    /// <summary>Computed CE DropDownList max entries: 2^DropDownLimitExponent (64..8192).</summary>
    public int DropDownLimit => 1 << DropDownLimitExponent;

    /// <summary>Computed CE String leaf display length: 2^CeStringLengthExponent (16..4096).</summary>
    public int CeStringLength => 1 << CeStringLengthExponent;

    /// <summary>Computed Copy CE Field array fabricate count: 0 (off) or 2^FabricateArrayCountExponent
    /// (2..512). When &gt; 0, Copy CE Field on a TArray pads it to this many element rows.</summary>
    public int FabricateArrayCount => FabricateArrayCountExponent <= 0 ? 0 : (1 << FabricateArrayCountExponent);

    /// <summary>Toolbar readout for the fabricate slider — "Off" at 0, the count otherwise,
    /// plus a warning past 256 (large exports slow Cheat Engine and can hit the entry cap).</summary>
    public string FabricateArrayCountLabel => FabricateArrayCount switch
    {
        0 => "Off",
        > 256 => $"{FabricateArrayCount} ⚠ large — CE may lag / truncate",
        _ => FabricateArrayCount.ToString(),
    };

    /// <summary>Amber readout past 256 (use-at-your-own-risk band), default grey otherwise.</summary>
    public Avalonia.Media.IBrush FabricateArrayCountBrush => FabricateArrayCount > 256
        ? Avalonia.Media.SolidColorBrush.Parse("#F4A747")
        : Avalonia.Media.SolidColorBrush.Parse("#D4D4D4");

    /// <summary>Show warning when array limit &gt;= 256 (high memory usage).</summary>
    public bool ShowArrayLimitWarning => ArrayLimitExponent >= 8;

    /// <summary>Computed per-container element cap for the find_by_address deep
    /// container scan: 2^DeepScanElemCapExponent (16..4096).</summary>
    public int DeepScanElemCap => 1 << DeepScanElemCapExponent;

    /// <summary>
    /// Experimental analysis tabs (Snapshot / SPC Query / Class Pivot) stay
    /// hidden unless the user opts in via the System-tab credit checkbox.
    /// Backed by the shared <see cref="IExperimentalGate"/> so the toggle
    /// (owned by <see cref="PointerPanelViewModel"/>) and this tab-visibility
    /// flag stay in sync. See docs/experimental-snapshot-spc-pivot.md Phase 0.
    /// </summary>
    public bool ExperimentalEnabled
    {
        get => _experimentalGate?.IsEnabled ?? false;
        set
        {
            if (_experimentalGate == null || _experimentalGate.IsEnabled == value) return;
            _experimentalGate.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Lock the experimental opt-in for the rest of this session. Called the
    /// first time the user opens one of the experimental tabs (Snapshot /
    /// SPC Query / Class Pivot) while enabled — from that point the System-tab
    /// opt-in checkbox can no longer be unticked. Session-only (a restart clears
    /// the lock). Idempotent and a no-op when the gate isn't enabled.
    /// </summary>
    public void LockExperimental()
    {
        if (_experimentalGate is { IsEnabled: true, IsLocked: false })
            _experimentalGate.Lock();
    }

    /// <summary>C5: switch to the Class Pivot tab and hand off the chosen
    /// class/property. Guarded so it's inert when the pivot tab isn't available.</summary>
    private async void HandlePivotHandoff(string className, string? propName)
    {
        if (Pivot == null) return;
        try
        {
            SelectedTabIndex = (int)MainTabIndex.ClassPivot;
            await Pivot.PivotForAsync(className, propName);
        }
        catch (Exception ex)
        {
            _log.Error($"Pivot handoff error: {className}.{propName}", ex);
        }
    }

    /// <summary>Enable the "Pivot this property" context-menu items only when the
    /// experimental Class Pivot tab is both present and opted in — so the handoff
    /// stays invisible while experimental features are off.</summary>
    private void UpdatePivotHandoffEnabled()
    {
        bool on = ExperimentalEnabled && Pivot != null;
        if (PropertySearch != null)        PropertySearch.PivotEnabled = on;
        if (InterestingProperties != null) InterestingProperties.PivotEnabled = on;
        if (LiveWalker != null)            LiveWalker.PivotEnabled = on;
        ValueSearch.PivotEnabled = on;
    }

    /// <summary>
    /// Cross-tab "show me this class": DETACH the Object Tree highlight, then load
    /// <paramref name="classAddr"/> into the Class/Struct panel. Every cross-tab class
    /// handoff goes through here.
    ///
    /// <para>The detach is the fix for <c>[TREERECLICK-2026-08-22]</c>. After a handoff the
    /// panel shows a class that no tree node selected, yet the tree kept highlighting the
    /// node the user picked earlier — the UI asserting a selection that is not what is on
    /// screen. Worse, that lie was unrecoverable by the obvious gesture: an Avalonia
    /// <c>ListBox</c> writes <c>SelectedItem</c> only when it CHANGES, so clicking the
    /// still-highlighted node raised nothing, <see cref="ObjectTreeViewModel.SelectionChanged"/>
    /// never fired (<c>ObjectTreeViewModel.cs:168</c>) and no walk reached the pipe. Clearing
    /// the selection fixes both at once: the tree stops lying, and the next click on that
    /// node is a real change, so it loads.</para>
    ///
    /// <para>⛔ The fix does NOT belong in <c>ClassStructViewModel</c>, which is already
    /// correct — <c>LoadClassAsync</c> clears its dedupe key via <c>BeginLoad(nodeAddr:
    /// null)</c> (<c>ClassStructViewModel.cs:250</c>), which is exactly why clicking a
    /// DIFFERENT row always recovered. Nor does it belong in a pointer handler on the tree:
    /// re-raising the event lands back in <c>OnObjectSelected</c>, whose dedupe
    /// (<c>:358</c>) swallows it, so that buys nothing this does not — and bypassing that
    /// dedupe is pinned against by <c>RepeatedSelectionOfSameNode_WalksOnlyOnce</c>.</para>
    ///
    /// <para>Order is load-bearing: clear FIRST. <c>OnObjectSelected(null)</c> returns at
    /// <c>ClassStructViewModel.cs:349</c> before its first await and deliberately takes no
    /// load ticket, so it cannot supersede the load started on the next line; and if that
    /// load throws, the tree is left honestly unselected rather than pointing at a stale
    /// node. This is the same bare null write as <see cref="ObjectTreeViewModel.ClearOnDisconnect"/>
    /// (<c>:496</c>); it touches no collection, so the reentrancy note at
    /// <c>ObjectTreeViewModel.cs:505-510</c> — which is about the selection model reacting
    /// to <c>FilteredNodes.Clear()</c> mid-method — does not apply.</para>
    /// </summary>
    private async Task ShowClassInClassStructAsync(string classAddr)
    {
        ObjectTree.SelectedNode = null;
        await ClassStruct.LoadClassCommand.ExecuteAsync(classAddr);
    }

    /// <summary>Address format options for toolbar ComboBox.</summary>
    public string[] AddressFormatOptions { get; } =
    [
        "Hex (no prefix)",
        "Hex (0x prefix)",
        "Module+Offset",
    ];

    /// <summary>
    /// Application version string read from assembly metadata (e.g. "v1.0.0.37").
    /// </summary>
    public string AppVersion { get; } = GetAppVersion();

    private static string GetAppVersion()
    {
        var ver = Assembly.GetEntryAssembly()?.GetName().Version;
        return ver != null ? $"v{ver}" : "";
    }

    // Child ViewModels
    public ObjectTreeViewModel ObjectTree { get; }
    public ClassStructViewModel ClassStruct { get; }
    public PointerPanelViewModel Pointers { get; }
    public LiveWalkerViewModel LiveWalker { get; }
    public InstanceFinderViewModel InstanceFinder { get; }
    public PropertySearchViewModel PropertySearch { get; }
    public GameClassFilterViewModel GameClassFilter { get; }
    public InterestingFunctionsViewModel InterestingFunctions { get; }
    /// <summary>Live ProcessEvent Call Profiler — behaviour-based UFunction
    /// discovery (Start → do an in-game action → Stop → see what fired). Finds
    /// game-specific functions (OpenShop / Dash) that name heuristics can't.</summary>
    public LiveFuncsViewModel LiveFuncs { get; }
    public InterestingPropertiesViewModel InterestingProperties { get; }
    public ValueSearchViewModel ValueSearch { get; }
    public RelatedObjectsViewModel RelatedObjects { get; }
    /// <summary>Offline "Dump All" (.jsonl) browser — one keyword search over
    /// classes + properties + functions, split by what's live in the current
    /// game (matched by object path), with jump-to-Live-Walker for matches.</summary>
    public DumpExplorerViewModel DumpExplorer { get; }
    /// <summary>Experimental "Detect Player Stats" tab (P4) — shortlists likely
    /// HP/MP/Gold fields + confirms them live. Gated behind
    /// <see cref="ExperimentalEnabled"/>. Non-null (no store dependency; the
    /// snapshot signal is opt-in and degrades gracefully when the store is null).</summary>
    public DetectStatsViewModel DetectStats { get; }
    public ConsoleViewModel Console { get; }
    public TeleportViewModel Teleport { get; }
    public ProxyDeployViewModel? ProxyDeploy { get; }
    /// <summary>Experimental Snapshot tab — null when no snapshot store was
    /// injected (e.g. in unit tests). Gated behind <see cref="ExperimentalEnabled"/>.</summary>
    public SnapshotViewModel? Snapshot { get; }
    /// <summary>Experimental SPC Query tab — shares the snapshot store with
    /// <see cref="Snapshot"/>. Null when no store was injected.</summary>
    public SpcQueryViewModel? Spc { get; }
    /// <summary>Experimental Class Pivot tab — shares the snapshot store.
    /// Null when no store was injected.</summary>
    public ClassPivotViewModel? Pivot { get; }

    partial void OnSelectedAddressFormatIndexChanged(int value)
    {
        ObjectTree.SelectedAddressFormatIndex = value;
        LiveWalker.SelectedAddressFormatIndex = value;
        InstanceFinder.SelectedAddressFormatIndex = value;
    }

    partial void OnCollapsePointerNodesChanged(bool value)
    {
        LiveWalker.CollapsePointerNodes = value;
        InstanceFinder.CollapsePointerNodes = value;
    }

    partial void OnArrayLimitExponentChanged(int value)
    {
        OnPropertyChanged(nameof(ArrayLimit));
        OnPropertyChanged(nameof(ShowArrayLimitWarning));
        LiveWalker.ArrayLimit = ArrayLimit;
        InstanceFinder.ArrayLimit = ArrayLimit;
    }

    partial void OnDropDownLimitExponentChanged(int value)
    {
        OnPropertyChanged(nameof(DropDownLimit));
        LiveWalker.DropDownLimit = DropDownLimit;
        InstanceFinder.DropDownLimit = DropDownLimit;
    }

    partial void OnCeStringLengthExponentChanged(int value)
    {
        OnPropertyChanged(nameof(CeStringLength));
        LiveWalker.CeStringLength = CeStringLength;
        InstanceFinder.CeStringLength = CeStringLength;
    }

    partial void OnFabricateArrayCountExponentChanged(int value)
    {
        OnPropertyChanged(nameof(FabricateArrayCount));
        OnPropertyChanged(nameof(FabricateArrayCountLabel));
        OnPropertyChanged(nameof(FabricateArrayCountBrush));
        // Copy CE Field is Live Walker only, so this fans out to just that VM.
        LiveWalker.FabricateArrayCount = FabricateArrayCount;
    }

    partial void OnCsxDrilldownDepthChanged(int value)
    {
        LiveWalker.CsxDrilldownDepth = value;
        OnPropertyChanged(nameof(CsxDrilldownDepthBrush));
    }

    partial void OnDeepScanElemCapExponentChanged(int value)
    {
        OnPropertyChanged(nameof(DeepScanElemCap));
        InstanceFinder.DeepScanElemCap = DeepScanElemCap;
    }

    /// <summary>Toolbar slider colour — default 0-3, then yellow (4) → orange →
    /// deep red (8) to flag exponential output growth. Max is 8.</summary>
    public Avalonia.Media.IBrush CsxDrilldownDepthBrush => CsxDrilldownDepth switch
    {
        >= 8 => Avalonia.Media.SolidColorBrush.Parse("#E02828"),
        7    => Avalonia.Media.SolidColorBrush.Parse("#E04A2C"),
        6    => Avalonia.Media.SolidColorBrush.Parse("#E0702C"),
        5    => Avalonia.Media.SolidColorBrush.Parse("#E69A17"),
        4    => Avalonia.Media.SolidColorBrush.Parse("#E6C217"),
        _    => Avalonia.Media.SolidColorBrush.Parse("#D4D4D4"),
    };

    partial void OnPreviewLimitChanged(int value)
    {
        LiveWalker.PreviewLimit = value;
        InstanceFinder.PreviewLimit = value;
    }

    // NOTE: the on-tab-switch AOBMaker re-check used to live here as an
    // OnSelectedTabIndexChanged switch keyed on magic tab indices, which
    // silently drifted when tabs were inserted (Pointers ended up checking
    // the wrong tab). It now lives in MainWindow.axaml.cs's
    // MainTabs_SelectionChanged, routed by TabItem.Tag so it can't drift.

    public MainWindowViewModel(
        IPipeClient pipeClient,
        IDumpService dump,
        ILoggingService log,
        IPlatformService platform,
        AobUsageService? aobUsage = null,
        IAobMakerBridge? aobMaker = null,
        IProxyDeployService? proxyDeploy = null,
        IExperimentalGate? experimentalGate = null,
        ISnapshotStore? snapshotStore = null,
        IGlobalHotkeyService? globalHotkeys = null,
        BookmarkStore? bookmarks = null,
        CoordinateLibraryStore? coordLibrary = null,
        ILogCompressionService? logCompression = null)
    {
        _pipeClient = pipeClient;
        _dump = dump;
        _log = log;
        _platform = platform;
        _aobUsage = aobUsage;
        _aobMaker = aobMaker;
        _experimentalGate = experimentalGate;

        // Keep tab visibility in sync when the toggle is flipped elsewhere
        // (the checkbox lives on the System tab / PointerPanelViewModel). Also
        // re-gate the "Pivot this property" context-menu handoff (C5).
        if (experimentalGate != null)
            experimentalGate.Changed += (_, _) =>
            {
                OnPropertyChanged(nameof(ExperimentalEnabled));
                UpdatePivotHandoffEnabled();
            };

        ObjectTree = new ObjectTreeViewModel(dump, log, platform);
        ClassStruct = new ClassStructViewModel(dump, log, platform);
        Pointers = new PointerPanelViewModel(platform, dump, log, aobMaker, aobUsage, experimentalGate, snapshotStore, pipeClient, logCompression);
        LiveWalker = new LiveWalkerViewModel(dump, log, platform, aobMaker, bookmarks);
        InstanceFinder = new InstanceFinderViewModel(dump, log, platform);
        PropertySearch = new PropertySearchViewModel(dump, log, aobMaker, platform, experimentalGate);
        GameClassFilter = new GameClassFilterViewModel(dump, log, platform);
        InterestingFunctions = new InterestingFunctionsViewModel(dump, log, aobMaker, platform);
        LiveFuncs = new LiveFuncsViewModel(dump, log, platform);
        InterestingProperties = new InterestingPropertiesViewModel(dump, log, platform);
        ValueSearch = new ValueSearchViewModel(dump, log);
        RelatedObjects = new RelatedObjectsViewModel(dump, log, platform);
        DumpExplorer = new DumpExplorerViewModel(dump, log, platform);
        // Detect Player Stats (P4). snapshotStore is optional — the behavioral
        // signal is opt-in and no-ops (with a note) when it's null.
        DetectStats = new DetectStatsViewModel(dump, log, snapshotStore);
        Console = new ConsoleViewModel(dump, log);
        Teleport = new TeleportViewModel(dump, log, platform, aobMaker, globalHotkeys, experimentalGate, coordLibrary);
        if (snapshotStore != null)
        {
            Snapshot = new SnapshotViewModel(dump, snapshotStore, log, experimentalGate, platform);
            // Diff row -> open its object in Live Walker (same shape as ValueSearch).
            Snapshot.NavigateToInstance += async (addr) =>
            {
                try
                {
                    SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                    await LiveWalker.NavigateToAddressCommand.ExecuteAsync(addr);
                }
                catch (Exception ex)
                {
                    _log.Error($"Snapshot NavigateToInstance handler error: {addr}", ex);
                }
            };
            // Diff row -> Locate in GWorld (value/reach: land on the owning object,
            // scroll to the changed field — same shape as SPC / Value Search).
            Snapshot.LocateInGWorld += async (addr, fieldOffset, fieldName) =>
            {
                try
                {
                    SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                    await LiveWalker.LocateInGWorldAsync(addr, fieldOffset, fieldName, stopAtParent: false);
                }
                catch (Exception ex)
                {
                    _log.Error($"Snapshot LocateInGWorld handler error: {addr}", ex);
                }
            };
            Snapshot.LocateInGameEngine += async (addr, fieldOffset, fieldName) =>
            {
                try
                {
                    SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                    await LiveWalker.LocateInGameEngineAsync(addr, fieldOffset, fieldName, stopAtParent: false);
                }
                catch (Exception ex)
                {
                    _log.Error($"Snapshot LocateInGameEngine handler error: {addr}", ex);
                }
            };

            Spc = new SpcQueryViewModel(snapshotStore, log, platform);
            // SPC hit -> open its object in Live Walker (newest snapshot's addr).
            Spc.NavigateToInstance += async (addr) =>
            {
                try
                {
                    SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                    await LiveWalker.NavigateToAddressCommand.ExecuteAsync(addr);
                }
                catch (Exception ex)
                {
                    _log.Error($"SPC NavigateToInstance handler error: {addr}", ex);
                }
            };
            // SPC hit -> Locate in GWorld (value/reach: land on the owning object,
            // scroll to the changed field).
            Spc.LocateInGWorld += async (addr, fieldOffset, fieldName) =>
            {
                try
                {
                    SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                    await LiveWalker.LocateInGWorldAsync(addr, fieldOffset, fieldName, stopAtParent: false);
                }
                catch (Exception ex)
                {
                    _log.Error($"SPC LocateInGWorld handler error: {addr}", ex);
                }
            };
            Spc.LocateInGameEngine += async (addr, fieldOffset, fieldName) =>
            {
                try
                {
                    SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                    await LiveWalker.LocateInGameEngineAsync(addr, fieldOffset, fieldName, stopAtParent: false);
                }
                catch (Exception ex)
                {
                    _log.Error($"SPC LocateInGameEngine handler error: {addr}", ex);
                }
            };

            Pivot = new ClassPivotViewModel(snapshotStore, log, platform, dump);
            // Pivot group -> open its representative object in Live Walker.
            Pivot.NavigateToInstance += async (addr) =>
            {
                try
                {
                    SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                    await LiveWalker.NavigateToAddressCommand.ExecuteAsync(addr);
                }
                catch (Exception ex)
                {
                    _log.Error($"Pivot NavigateToInstance handler error: {addr}", ex);
                }
            };
            Pivot.LocateInGWorld += async (addr) =>
            {
                try
                {
                    SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                    await LiveWalker.LocateInGWorldAsync(addr, 0, null, stopAtParent: false);
                }
                catch (Exception ex)
                {
                    _log.Error($"Pivot LocateInGWorld handler error: {addr}", ex);
                }
            };
            Pivot.LocateInGameEngine += async (addr) =>
            {
                try
                {
                    SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                    await LiveWalker.LocateInGameEngineAsync(addr, 0, null, stopAtParent: false);
                }
                catch (Exception ex)
                {
                    _log.Error($"Pivot LocateInGameEngine handler error: {addr}", ex);
                }
            };

            // C5: right-click "Pivot this property" from the three source panels ->
            // switch to the Class Pivot tab and pre-select the class/property.
            PropertySearch.NavigateToPivot        += (cls, prop) => HandlePivotHandoff(cls, prop);
            InterestingProperties.NavigateToPivot += (cls, prop) => HandlePivotHandoff(cls, prop);
            LiveWalker.NavigateToPivot            += (cls, prop) => HandlePivotHandoff(cls, prop);
            // Value-locator -> pivot: a value-scan hit already carries class + field.
            ValueSearch.NavigateToPivot           += (cls, prop) => HandlePivotHandoff(cls, prop);

            // "Remove all snapshot data" (System tab) deletes every snapshot DB file —
            // the experimental tabs' cached lists are now stale, so refresh them to the
            // empty state.
            Pointers.SnapshotDataRemoved += () =>
            {
                _ = Snapshot?.RefreshCommand.ExecuteAsync(null);
                _ = Spc?.RefreshCommand.ExecuteAsync(null);
                _ = Pivot?.RefreshCommand.ExecuteAsync(null);
            };
        }
        // Gate the handoff menu items to the experimental flag (and pivot existence).
        UpdatePivotHandoffEnabled();

        _proxyDeployForChecks = proxyDeploy;

        if (proxyDeploy != null)
        {
            // platform is passed only so the leftover-proxy rows can offer "Open folder".
            ProxyDeploy = new ProxyDeployViewModel(proxyDeploy, log, platform);
            // Auto-connect the pipe after a successful in-UI DLL injection.
            ProxyDeploy.RequestConnectAsync = () => ConnectCommand.ExecuteAsync(null);
            // Lets the post-inject retry ASK whether it worked instead of assuming.
            ProxyDeploy.IsConnectedProbe = () => IsConnected;
            ProxyDeploy.SetConnectErrorSuppression = v => SuppressConnectErrors = v;
            // Persist the remembered-proxy map (a Dictionary mutation isn't caught
            // by the [ObservableProperty] change-tracking save).
            ProxyDeploy.RequestOptionSave = ScheduleOptionSave;
        }

        // Mirror the per-tab stale-DLL warning into the always-visible top-bar
        // badge so a version mismatch is noticed from any tab.
        Pointers.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PointerPanelViewModel.ShowGlobalBuildWarning)
                               or nameof(PointerPanelViewModel.GlobalBuildWarningText)
                               or nameof(PointerPanelViewModel.BuildVersionsMatch)
                               or nameof(PointerPanelViewModel.DllBuildNumber))
            {
                OnPropertyChanged(nameof(ShowBuildMismatchBadge));
                OnPropertyChanged(nameof(BuildMismatchBadgeText));
                OnPropertyChanged(nameof(ShowDllBuildOk));
                OnPropertyChanged(nameof(DllBuildOkText));
            }
            if (e.PropertyName is nameof(PointerPanelViewModel.ShowPackedLayoutBadge)
                               or nameof(PointerPanelViewModel.PackedLayoutBadgeText))
            {
                OnPropertyChanged(nameof(ShowPackedLayoutBadge));
                OnPropertyChanged(nameof(PackedLayoutBadgeText));
            }
            if (e.PropertyName == nameof(PointerPanelViewModel.IsAobMakerAvailable))
                IsAobMakerAvailable = Pointers.IsAobMakerAvailable;
        };

        // Mirror the per-tab AOBMaker availability (LiveWalker + Pointers each
        // probe on their own tab activation) into the always-visible top-toolbar
        // chip so its state stays correct from any tab without a manual refresh.
        IsAobMakerAvailable = _aobMaker?.IsAvailable ?? false;
        LiveWalker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LiveWalkerViewModel.IsAobMakerAvailable))
                IsAobMakerAvailable = LiveWalker.IsAobMakerAvailable;
        };

        // Wire Pointers Extra Scan -> refresh all panels after rescan results applied
        Pointers.RescanApplied += async () =>
        {
            try
            {
                var state = await _dump.GetPointersAsync();
                _engineState = state;

                Pointers.Update(state);

                ObjectTree.SetEngineState(state);
                LiveWalker.SetEngineState(state);
                InstanceFinder.SetEngineState(state);
                ValueSearch.SetEngineState(state);
                Snapshot?.SetEngineState(state);
                Spc?.SetEngineState(state);
                Pivot?.SetEngineState(state);
                Teleport.SetEngineState(state);
                // Load this game's coordinate library. Keyed by MODULE NAME (not PE
                // hash) so it survives a game patch. Idempotent -- clears in-memory
                // first -- so calling it from both fan-out sites is safe.
                Teleport.LoadCoordLibraryForGame(state.ModuleName);

                _ = LiveWalker.CheckAobMakerAsync();
                _ = Teleport.CheckAobMakerAsync();

                StatusText = $"Connected — UE{state.UEVersion} ({state.ObjectCount} objects)";
                _ = CheckForCompetingDumperHostsAsync(state);

                // Re-load objects if tree was empty
                if (ObjectTree.ObjectCount == 0 && state.ObjectCount > 0)
                    _ = ObjectTree.LoadCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                _log.Error("RescanApplied refresh error", ex);
            }
        };

        // Wire cross-VM communication
        // Wrap async lambdas in try/catch to prevent async void from crashing the app
        ObjectTree.SelectionChanged += async (node) =>
        {
            try
            {
                await ClassStruct.OnObjectSelected(node);
            }
            catch (Exception ex)
            {
                _log.Error("SelectionChanged handler error", ex);
            }
        };

        // Wire ObjectTree right-click -> InstanceFinder (find instances of the
        // selected node's class, optionally ANDed with its object name) + tab
        // switch + auto-run. Saves the copy-type / paste-into-Instances / Search
        // round-trip. Mirrors the GameClassFilter / PropertySearch handoffs.
        ObjectTree.NavigateToInstanceFinder += async (className) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.InstanceFinder;
                await InstanceFinder.SearchForClassAsync(className);
            }
            catch (Exception ex)
            {
                _log.Error($"ObjectTree NavigateToInstanceFinder handler error: {className}", ex);
            }
        };
        ObjectTree.NavigateToInstanceFinderWithName += async (className, name) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.InstanceFinder;
                await InstanceFinder.SearchForClassAndNameAsync(className, name);
            }
            catch (Exception ex)
            {
                _log.Error($"ObjectTree NavigateToInstanceFinderWithName handler error: {className}", ex);
            }
        };

        // Wire InstanceFinder -> LiveWalker navigation + tab switch
        InstanceFinder.NavigateToLiveWalker += async (addr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker; // Switch to Live Walker tab
                await LiveWalker.NavigateToAddressCommand.ExecuteAsync(addr);
            }
            catch (Exception ex)
            {
                _log.Error("NavigateToLiveWalker handler error", ex);
            }
        };

        // Wire InstanceFinder -> "Locate in GWorld". The selected row IS the
        // target object, so land ON it (stopAtParent: false) — same as Value
        // Search / Snapshot / SPC. Parent-stop left the user on the holder
        // object (e.g. BP_LifeGameInstance_C.m_savedata) instead of the object
        // they picked; the full GWorld→…→target spine is still in the
        // breadcrumb, so the holder is one click up via Parent ↑.
        InstanceFinder.LocateInGWorld += async (addr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.LocateInGWorldAsync(addr, 0, null, stopAtParent: false);
            }
            catch (Exception ex)
            {
                _log.Error($"InstanceFinder LocateInGWorld handler error: {addr}", ex);
            }
        };
        InstanceFinder.LocateInGameEngine += async (addr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.LocateInGameEngineAsync(addr, 0, null, stopAtParent: false);
            }
            catch (Exception ex)
            {
                _log.Error($"InstanceFinder LocateInGameEngine handler error: {addr}", ex);
            }
        };

        // Wire Object Tree row drill-downs -> Live Walker / Locate. The selected row IS
        // the target object (land ON it, stopAtParent: false) — same shape as the
        // InstanceFinder handoffs above. Gives a global-search hit the per-instance drill
        // the class-oriented "Find Instances" can't.
        ObjectTree.NavigateToLiveWalker += async (addr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.NavigateToAddressCommand.ExecuteAsync(addr);
            }
            catch (Exception ex)
            {
                _log.Error($"ObjectTree NavigateToLiveWalker handler error: {addr}", ex);
            }
        };
        ObjectTree.LocateInGWorld += async (addr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.LocateInGWorldAsync(addr, 0, null, stopAtParent: false);
            }
            catch (Exception ex)
            {
                _log.Error($"ObjectTree LocateInGWorld handler error: {addr}", ex);
            }
        };
        ObjectTree.LocateInGameEngine += async (addr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.LocateInGameEngineAsync(addr, 0, null, stopAtParent: false);
            }
            catch (Exception ex)
            {
                _log.Error($"ObjectTree LocateInGameEngine handler error: {addr}", ex);
            }
        };

        // Wire InstanceFinder container match -> "Locate in GWorld" (the address is
        // a value inside a container element → reach the owning object + drill the
        // full container chain, including deeply-nested values).
        InstanceFinder.LocateContainerInGWorld += async (match) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.LocateContainerInGWorldAsync(match);
            }
            catch (Exception ex)
            {
                _log.Error($"InstanceFinder LocateContainerInGWorld handler error: {match.OwnerAddress}", ex);
            }
        };
        InstanceFinder.LocateContainerInGameEngine += async (match) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.LocateContainerInGameEngineAsync(match);
            }
            catch (Exception ex)
            {
                _log.Error($"InstanceFinder LocateContainerInGameEngine handler error: {match.OwnerAddress}", ex);
            }
        };

        // Wire RelatedObjects -> LiveWalker / "Locate in GWorld" / InstanceFinder.
        // Each related-object row lands ON the picked object (stopAtParent: false),
        // same as Instance Finder / Value Search.
        RelatedObjects.NavigateToLiveWalker += async (addr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.NavigateToAddressCommand.ExecuteAsync(addr);
            }
            catch (Exception ex)
            {
                _log.Error($"RelatedObjects NavigateToLiveWalker handler error: {addr}", ex);
            }
        };
        RelatedObjects.LocateInGWorld += async (addr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.LocateInGWorldAsync(addr, 0, null, stopAtParent: false);
            }
            catch (Exception ex)
            {
                _log.Error($"RelatedObjects LocateInGWorld handler error: {addr}", ex);
            }
        };
        RelatedObjects.LocateInGameEngine += async (addr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.LocateInGameEngineAsync(addr, 0, null, stopAtParent: false);
            }
            catch (Exception ex)
            {
                _log.Error($"RelatedObjects LocateInGameEngine handler error: {addr}", ex);
            }
        };

        // Dump Explorer: a matched row hands off its owning class's CURRENT live
        // address to the Live Walker (same pattern as the other panels).
        DumpExplorer.NavigateToLiveWalker += async (addr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.NavigateToAddressCommand.ExecuteAsync(addr);
            }
            catch (Exception ex)
            {
                _log.Error($"DumpExplorer NavigateToLiveWalker handler error: {addr}", ex);
            }
        };
        // Dump Explorer: the class -> instances bridge (find live instances of a
        // row's owning class in the Instance Finder). From there the existing
        // Related -> Locate-in-GWorld/GameEngine flow handles instances.
        DumpExplorer.NavigateToInstanceFinder += async (className) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.InstanceFinder;
                await InstanceFinder.SearchForClassAsync(className);
            }
            catch (Exception ex)
            {
                _log.Error($"DumpExplorer NavigateToInstanceFinder handler error: {className}", ex);
            }
        };
        RelatedObjects.NavigateToInstanceFinder += async (className) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.InstanceFinder;
                await InstanceFinder.SearchForClassAsync(className);
            }
            catch (Exception ex)
            {
                _log.Error($"RelatedObjects NavigateToInstanceFinder handler error: {className}", ex);
            }
        };

        // Wire Teleport "Locate in GWorld" -> land ON the player pawn (the object
        // whose Current Pose is shown), same shape as Instance Finder / Related.
        Teleport.LocateInGWorld += async (addr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.LocateInGWorldAsync(addr, 0, null, stopAtParent: false);
            }
            catch (Exception ex)
            {
                _log.Error($"Teleport LocateInGWorld handler error: {addr}", ex);
            }
        };
        Teleport.LocateInGameEngine += async (addr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.LocateInGameEngineAsync(addr, 0, null, stopAtParent: false);
            }
            catch (Exception ex)
            {
                _log.Error($"Teleport LocateInGameEngine handler error: {addr}", ex);
            }
        };
        // Per-vector locate (position / velocity): land ON the FVector field inside
        // its owning component (RootComponent / CharacterMovement) — same value-
        // landing handoff Value Search uses (owner addr + field offset + name).
        Teleport.LocateValueInGWorld += async (owner, fieldOffset, fieldName) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.LocateInGWorldAsync(owner, fieldOffset, fieldName, stopAtParent: false);
            }
            catch (Exception ex)
            {
                _log.Error($"Teleport LocateValueInGWorld handler error: {owner}+0x{fieldOffset:X}", ex);
            }
        };

        // Wire Instance Finder / Value Search / Live Walker -> Related Objects:
        // hand the chosen object's address to the Related tab and load its graph.
        async Task OpenRelatedAsync(string addr)
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.RelatedObjects;
                await RelatedObjects.LoadForAddressAsync(addr);
            }
            catch (Exception ex)
            {
                _log.Error($"NavigateToRelatedObjects handler error: {addr}", ex);
            }
        }
        InstanceFinder.NavigateToRelatedObjects += async (addr) => await OpenRelatedAsync(addr);
        ValueSearch.NavigateToRelatedObjects += async (addr) => await OpenRelatedAsync(addr);
        LiveWalker.NavigateToRelatedObjects += async (addr) => await OpenRelatedAsync(addr);
        ObjectTree.NavigateToRelatedObjects += async (addr) => await OpenRelatedAsync(addr);

        // Wire LiveWalker -> InstanceFinder (per-field "inst" button: open the
        // field's pointed-to object class + switch tab + auto-run, mirroring the
        // Property Search / Interesting Funcs+Props "inst" handoff).
        LiveWalker.NavigateToInstanceFinder += async (className) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.InstanceFinder;
                await InstanceFinder.SearchForClassAsync(className);
            }
            catch (Exception ex)
            {
                _log.Error($"LiveWalker NavigateToInstanceFinder handler error: {className}", ex);
            }
        };

        // Wire PropertySearch -> InstanceFinder (pre-fill class name +
        // switch tab + auto-run the search). Pre-fill alone left the user
        // having to click Search again, which they correctly flagged as
        // friction — the whole point of "Find Instances" is to see live
        // instances of that class, so trigger the query immediately.
        PropertySearch.NavigateToInstanceFinder += async (className) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.InstanceFinder; // Switch to Instance Finder tab
                await InstanceFinder.SearchForClassAsync(className);
            }
            catch (Exception ex)
            {
                _log.Error("NavigateToInstanceFinder handler error", ex);
            }
        };

        // Wire PropertySearch -> LiveWalker navigation + tab switch
        PropertySearch.NavigateToLiveWalker += async (addr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker; // Switch to Live Walker tab
                await LiveWalker.NavigateToAddressCommand.ExecuteAsync(addr);
            }
            catch (Exception ex)
            {
                _log.Error("PropertySearch NavigateToLiveWalker handler error", ex);
            }
        };

        // Wire GameClassFilter -> InstanceFinder (pre-fill class name +
        // switch tab + auto-run the search). Same rationale as the
        // PropertySearch wiring above — clicking "Find Instances" should
        // produce instances on screen without an extra Search click.
        GameClassFilter.NavigateToInstanceFinder += async (className) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.InstanceFinder; // Switch to Instance Finder tab
                await InstanceFinder.SearchForClassAsync(className);
            }
            catch (Exception ex)
            {
                _log.Error("GameClassFilter NavigateToInstanceFinder handler error", ex);
            }
        };

        // Wire GameClassFilter -> LiveWalker navigation + tab switch
        GameClassFilter.NavigateToLiveWalker += async (addr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker; // Switch to Live Walker tab
                await LiveWalker.NavigateToAddressCommand.ExecuteAsync(addr);
            }
            catch (Exception ex)
            {
                _log.Error("GameClassFilter NavigateToLiveWalker handler error", ex);
            }
        };

        // Wire GameClassFilter -> ClassStruct (walk class schema + switch tab)
        GameClassFilter.NavigateToClassStruct += async (classAddr) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.ClassStruct; // Switch to ClassStruct tab
                await ShowClassInClassStructAsync(classAddr);
            }
            catch (Exception ex)
            {
                _log.Error("GameClassFilter NavigateToClassStruct handler error", ex);
            }
        };

        // Wire InterestingFunctions / InterestingProperties -> InstanceFinder
        // (per-row "inst" button: pre-fill class name + switch tab + auto-run,
        // mirroring the Property Search / Value Search "inst" handoff).
        InterestingFunctions.NavigateToInstanceFinder += async (className) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.InstanceFinder;
                await InstanceFinder.SearchForClassAsync(className);
            }
            catch (Exception ex)
            {
                _log.Error($"InterestingFunctions NavigateToInstanceFinder handler error: {className}", ex);
            }
        };
        InterestingProperties.NavigateToInstanceFinder += async (className) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.InstanceFinder;
                await InstanceFinder.SearchForClassAsync(className);
            }
            catch (Exception ex)
            {
                _log.Error($"InterestingProperties NavigateToInstanceFinder handler error: {className}", ex);
            }
        };

        // Wire InterestingFunctions -> Live Walker (with find_instance fallback to ClassStruct).
        // The Finder gives us (className, funcName), but Live Walker is instance-based:
        //   1. Try FindInstancesAsync(className, exactMatch=true) -- pick the first non-CDO live instance.
        //   2. On hit: switch to Live Walker tab + navigate to that instance + auto-scroll to funcName.
        //   3. On miss (CDO-only class, or class not yet instantiated): switch to ClassStruct tab so
        //      the user at least sees the function in the class metadata, with a status hint.
        InterestingFunctions.NavigateToFunction += async (className, funcName) =>
        {
            try
            {
                if (string.IsNullOrEmpty(className)) return;

                var instances = await _dump.FindInstancesAsync(className, exactMatch: true, limit: 5);
                // Skip CDO entries (their name typically starts with "Default__"); pick the first
                // real instance so Live Walker has something to walk.
                string? liveAddr = null;
                foreach (var inst in instances.Instances)
                {
                    if (string.IsNullOrEmpty(inst.Address)) continue;
                    if (inst.Name.StartsWith("Default__", StringComparison.Ordinal)) continue;
                    liveAddr = inst.Address;
                    break;
                }

                if (!string.IsNullOrEmpty(liveAddr))
                {
                    SelectedTabIndex = (int)MainTabIndex.LiveWalker; // Live Walker
                    await LiveWalker.NavigateToAddressCommand.ExecuteAsync(liveAddr);
                    // Function Goto: TrySelectFunctionByNameAsync awaits any
                    // in-flight LoadFunctionsAsync (NavigateToAddress fires
                    // it forget-style so fields render fast). Without that
                    // await the first click after a class change finds an
                    // empty function list and reports "function not selected".
                    var picked = await LiveWalker.TrySelectFunctionByNameAsync(funcName);
                    StatusText = picked
                        ? $"Navigated to {className}::{funcName} (live instance {liveAddr})"
                        : $"Navigated to {className} @ {liveAddr}; function '{funcName}' not in this class";
                    _log.Info($"InterestingFunctions -> LiveWalker: {className}::{funcName} @ {liveAddr}" +
                              (picked ? "" : " (function not selected)"));
                }
                else
                {
                    SelectedTabIndex = (int)MainTabIndex.ClassStruct; // ClassStruct fallback
                    // Look up the class address via ListClasses since Find Instances came back empty.
                    var classes = await _dump.ListClassesAsync(gameOnly: false);
                    var match = classes.FindClassAddr(className);
                    if (match.Found)
                    {
                        await ShowClassInClassStructAsync(match.Addr);
                        StatusText = $"No live instance of {className}; showing class metadata";
                        _log.Info($"InterestingFunctions -> ClassStruct fallback: {className}::{funcName}");
                    }
                    else
                    {
                        StatusText = $"No live instance of {className}, and the class {match.MissReason}";
                        _log.Warn($"InterestingFunctions navigate: {className} {match.MissReason}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"InterestingFunctions NavigateToFunction handler error: {className}::{funcName}", ex);
            }
        };

        // Wire Live Funcs (PE profiler) -> Live Walker, same instance-based handoff
        // as the Interesting Functions finder: find a live non-CDO instance of the
        // class, open it in Live Walker, and auto-select the discovered function.
        LiveFuncs.NavigateToFunction += async (className, funcName) =>
        {
            try
            {
                if (string.IsNullOrEmpty(className)) return;

                var instances = await _dump.FindInstancesAsync(className, exactMatch: true, limit: 5);
                string? liveAddr = null;
                foreach (var inst in instances.Instances)
                {
                    if (string.IsNullOrEmpty(inst.Address)) continue;
                    if (inst.Name.StartsWith("Default__", StringComparison.Ordinal)) continue;
                    liveAddr = inst.Address;
                    break;
                }

                if (!string.IsNullOrEmpty(liveAddr))
                {
                    SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                    await LiveWalker.NavigateToAddressCommand.ExecuteAsync(liveAddr);
                    var picked = await LiveWalker.TrySelectFunctionByNameAsync(funcName);
                    StatusText = picked
                        ? $"Navigated to {className}::{funcName} (live instance {liveAddr})"
                        : $"Navigated to {className} @ {liveAddr}; function '{funcName}' not in this class";
                    _log.Info($"LiveFuncs -> LiveWalker: {className}::{funcName} @ {liveAddr}" +
                              (picked ? "" : " (function not selected)"));
                }
                else
                {
                    SelectedTabIndex = (int)MainTabIndex.ClassStruct;
                    var classes = await _dump.ListClassesAsync(gameOnly: false);
                    var match = classes.FindClassAddr(className);
                    if (match.Found)
                    {
                        await ShowClassInClassStructAsync(match.Addr);
                        StatusText = $"No live instance of {className}; showing class metadata";
                    }
                    else
                    {
                        StatusText = $"No live instance of {className}, and the class {match.MissReason}";
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"LiveFuncs NavigateToFunction handler error: {className}::{funcName}", ex);
            }
        };

        // Wire Live Funcs -> clipboard (copy function name). VM stays IPlatformService-free.
        LiveFuncs.RequestCopyText += async (text) =>
        {
            if (string.IsNullOrEmpty(text)) return;
            try { await _platform.CopyToClipboardAsync(text); }
            catch (Exception ex)
            {
                _log.Error($"LiveFuncs clipboard copy failed: {ex.Message}", ex);
            }
        };

        // Wire InterestingFunctions -> "Locate in GWorld": resolve a live (non-CDO)
        // instance of the function's class (same find_instance path as
        // NavigateToFunction above), then run the GWorld path search in parent mode
        // (stop before drilling into the instance). A function isn't itself a world
        // object, so the meaningful target is "where do instances of this class live".
        InterestingFunctions.LocateInGWorld += async (className) =>
        {
            try
            {
                if (string.IsNullOrEmpty(className)) return;
                var instances = await _dump.FindInstancesAsync(className, exactMatch: true, limit: 5);
                string? liveAddr = null;
                foreach (var inst in instances.Instances)
                {
                    if (string.IsNullOrEmpty(inst.Address)) continue;
                    if (inst.Name.StartsWith("Default__", StringComparison.Ordinal)) continue;
                    liveAddr = inst.Address;
                    break;
                }
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                if (string.IsNullOrEmpty(liveAddr))
                {
                    LiveWalker.StatusText = $"No live (non-CDO) instance of {className} to locate in GWorld.";
                    return;
                }
                await LiveWalker.LocateInGWorldAsync(liveAddr, 0, null, stopAtParent: true);
            }
            catch (Exception ex)
            {
                _log.Error($"InterestingFunctions LocateInGWorld handler error: {className}", ex);
            }
        };

        // Xref dialog (code-behind, no per-instance DI): give it the app's AOBMaker
        // bridge for "Disassemble in CE", and handle its "Locate class" request by
        // resolving a live (non-CDO) instance + navigating to Live Walker — same
        // class-name path as the Interesting Functions locate just above.
        Views.PropertyXrefDialog.SharedAobMaker = aobMaker;
        _xrefLocateHandler = async (className) =>
        {
            try
            {
                if (string.IsNullOrEmpty(className)) return;
                var instances = await _dump.FindInstancesAsync(className, exactMatch: true, limit: 5);
                string? liveAddr = null;
                foreach (var inst in instances.Instances)
                {
                    if (string.IsNullOrEmpty(inst.Address)) continue;
                    if (inst.Name.StartsWith("Default__", StringComparison.Ordinal)) continue;
                    liveAddr = inst.Address;
                    break;
                }
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                if (string.IsNullOrEmpty(liveAddr))
                {
                    LiveWalker.StatusText = $"No live (non-CDO) instance of {className} to locate in GWorld.";
                    return;
                }
                await LiveWalker.LocateInGWorldAsync(liveAddr, 0, null, stopAtParent: true);
            }
            catch (Exception ex)
            {
                _log.Error($"Xref dialog LocateClass handler error: {className}", ex);
            }
        };
        Views.PropertyXrefDialog.LocateClassInGWorldRequested += _xrefLocateHandler;

        InterestingFunctions.LocateInGameEngine += async (className) =>
        {
            try
            {
                if (string.IsNullOrEmpty(className)) return;
                var instances = await _dump.FindInstancesAsync(className, exactMatch: true, limit: 5);
                string? liveAddr = null;
                foreach (var inst in instances.Instances)
                {
                    if (string.IsNullOrEmpty(inst.Address)) continue;
                    if (inst.Name.StartsWith("Default__", StringComparison.Ordinal)) continue;
                    liveAddr = inst.Address;
                    break;
                }
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                if (string.IsNullOrEmpty(liveAddr))
                {
                    LiveWalker.StatusText = $"No live (non-CDO) instance of {className} to locate in GameEngine.";
                    return;
                }
                await LiveWalker.LocateInGameEngineAsync(liveAddr, 0, null, stopAtParent: true);
            }
            catch (Exception ex)
            {
                _log.Error($"InterestingFunctions LocateInGameEngine handler error: {className}", ex);
            }
        };

        // Wire InterestingProperties -> Live Walker. Same pattern as
        // InterestingFunctions: try find_instance for a non-CDO live address,
        // fall back to ClassStruct when none. We don't scroll to the
        // specific property row in round 1 (LiveWalker has no public
        // ScrollToField yet) — the property name is left in the status
        // text so the user knows what to look for.
        InterestingProperties.NavigateToProperty += async (className, propName) =>
        {
            try
            {
                if (string.IsNullOrEmpty(className)) return;

                var instances = await _dump.FindInstancesAsync(className, exactMatch: true, limit: 5);
                string? liveAddr = null;
                foreach (var inst in instances.Instances)
                {
                    if (string.IsNullOrEmpty(inst.Address)) continue;
                    if (inst.Name.StartsWith("Default__", StringComparison.Ordinal)) continue;
                    liveAddr = inst.Address;
                    break;
                }

                if (!string.IsNullOrEmpty(liveAddr))
                {
                    SelectedTabIndex = (int)MainTabIndex.LiveWalker; // Live Walker
                    await LiveWalker.NavigateToAddressCommand.ExecuteAsync(liveAddr);
                    // Pre-fill the search box so the user lands with the
                    // property highlighted instead of having to scroll.
                    LiveWalker.SearchText = propName;
                    StatusText = $"Navigated to {className} (live instance {liveAddr}); searching {propName}";
                    _log.Info($"InterestingProperties -> LiveWalker: {className}.{propName} @ {liveAddr}");
                }
                else
                {
                    SelectedTabIndex = (int)MainTabIndex.ClassStruct; // ClassStruct fallback
                    var classes = await _dump.ListClassesAsync(gameOnly: false);
                    var match = classes.FindClassAddr(className);
                    if (match.Found)
                    {
                        await ShowClassInClassStructAsync(match.Addr);
                        StatusText = $"No live instance of {className}; showing class metadata (look for {propName})";
                        _log.Info($"InterestingProperties -> ClassStruct fallback: {className}.{propName}");
                    }
                    else
                    {
                        StatusText = $"No live instance of {className}, and the class {match.MissReason}";
                        _log.Warn($"InterestingProperties navigate: {className} {match.MissReason}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"InterestingProperties NavigateToProperty handler error: {className}.{propName}", ex);
            }
        };

        // Wire InterestingProperties -> "Locate in GWorld": same className→live-instance
        // resolution as InterestingFunctions (a property is a class-level definition,
        // not a world object, so we locate where instances of its class live).
        InterestingProperties.LocateInGWorld += async (className) =>
        {
            try
            {
                if (string.IsNullOrEmpty(className)) return;
                var instances = await _dump.FindInstancesAsync(className, exactMatch: true, limit: 5);
                string? liveAddr = null;
                foreach (var inst in instances.Instances)
                {
                    if (string.IsNullOrEmpty(inst.Address)) continue;
                    if (inst.Name.StartsWith("Default__", StringComparison.Ordinal)) continue;
                    liveAddr = inst.Address;
                    break;
                }
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                if (string.IsNullOrEmpty(liveAddr))
                {
                    LiveWalker.StatusText = $"No live (non-CDO) instance of {className} to locate in GWorld.";
                    return;
                }
                await LiveWalker.LocateInGWorldAsync(liveAddr, 0, null, stopAtParent: true);
            }
            catch (Exception ex)
            {
                _log.Error($"InterestingProperties LocateInGWorld handler error: {className}", ex);
            }
        };
        InterestingProperties.LocateInGameEngine += async (className) =>
        {
            try
            {
                if (string.IsNullOrEmpty(className)) return;
                var instances = await _dump.FindInstancesAsync(className, exactMatch: true, limit: 5);
                string? liveAddr = null;
                foreach (var inst in instances.Instances)
                {
                    if (string.IsNullOrEmpty(inst.Address)) continue;
                    if (inst.Name.StartsWith("Default__", StringComparison.Ordinal)) continue;
                    liveAddr = inst.Address;
                    break;
                }
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                if (string.IsNullOrEmpty(liveAddr))
                {
                    LiveWalker.StatusText = $"No live (non-CDO) instance of {className} to locate in GameEngine.";
                    return;
                }
                await LiveWalker.LocateInGameEngineAsync(liveAddr, 0, null, stopAtParent: true);
            }
            catch (Exception ex)
            {
                _log.Error($"InterestingProperties LocateInGameEngine handler error: {className}", ex);
            }
        };

        InterestingProperties.RequestCopyText += async (text) =>
        {
            if (string.IsNullOrEmpty(text)) return;
            try { await _platform.CopyToClipboardAsync(text); }
            catch (Exception ex)
            {
                _log.Error($"InterestingProperties clipboard copy failed: {ex.Message}", ex);
            }
        };

        // --- Detect Player Stats (P4) — same class->live-instance->GWorld /
        //     Instance Finder / clipboard handoffs as Interesting Properties. ---
        DetectStats.LocateInGWorld += async (className) =>
        {
            try
            {
                if (string.IsNullOrEmpty(className)) return;
                var instances = await _dump.FindInstancesAsync(className, exactMatch: true, limit: 5);
                string? liveAddr = null;
                foreach (var inst in instances.Instances)
                {
                    if (string.IsNullOrEmpty(inst.Address)) continue;
                    if (inst.Name.StartsWith("Default__", StringComparison.Ordinal)) continue;
                    liveAddr = inst.Address;
                    break;
                }
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                if (string.IsNullOrEmpty(liveAddr))
                {
                    LiveWalker.StatusText = $"No live (non-CDO) instance of {className} to locate in GWorld.";
                    return;
                }
                await LiveWalker.LocateInGWorldAsync(liveAddr, 0, null, stopAtParent: true);
            }
            catch (Exception ex)
            {
                _log.Error($"DetectStats LocateInGWorld handler error: {className}", ex);
            }
        };
        DetectStats.NavigateToInstanceFinder += async (className) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.InstanceFinder;
                await InstanceFinder.SearchForClassAsync(className);
            }
            catch (Exception ex)
            {
                _log.Error($"DetectStats NavigateToInstanceFinder handler error: {className}", ex);
            }
        };
        DetectStats.RequestCopyText += async (text) =>
        {
            if (string.IsNullOrEmpty(text)) return;
            try { await _platform.CopyToClipboardAsync(text); }
            catch (Exception ex)
            {
                _log.Error($"DetectStats clipboard copy failed: {ex.Message}", ex);
            }
        };

        // Wire InterestingProperties -> CT save dialog. The VM builds
        // the .CT payload and emits (defaultFileName, ctXml); we own
        // the platform-specific save dialog + write here so the VM
        // stays IO-free and unit-testable. The CT filter matches what
        // CE associates with the .CT extension on Windows.
        InterestingProperties.RequestSaveCheatTable += async (defaultName, ctXml) =>
        {
            await SaveCheatTableAsync(defaultName, ctXml, "InterestingProperties");
        };

        // Wire ValueSearch -> LiveWalker (open candidate's owning instance)
        // + clipboard. Same shape as InstanceFinder.NavigateToLiveWalker
        // since ValueSearch already has the instance address resolved.
        ValueSearch.NavigateToInstance += async (addr, fieldOffset, fieldName) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;  // Live Walker
                // Focus the candidate's owning field (matched by offset) so the
                // user lands ON the matched value — not just the instance's
                // field list — and drills to the element for container hits.
                await LiveWalker.NavigateToInstanceFieldAsync(addr, fieldOffset, fieldName);
            }
            catch (Exception ex)
            {
                _log.Error($"ValueSearch NavigateToInstance handler error: {addr}", ex);
            }
        };

        // Wire ValueSearch -> "Locate in GWorld" (property value → reach the
        // owning object + scroll to the value field).
        ValueSearch.LocateInGWorld += async (addr, fieldOffset, fieldName) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.LocateInGWorldAsync(addr, fieldOffset, fieldName, stopAtParent: false);
            }
            catch (Exception ex)
            {
                _log.Error($"ValueSearch LocateInGWorld handler error: {addr}", ex);
            }
        };
        ValueSearch.LocateInGameEngine += async (addr, fieldOffset, fieldName) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.LiveWalker;
                await LiveWalker.LocateInGameEngineAsync(addr, fieldOffset, fieldName, stopAtParent: false);
            }
            catch (Exception ex)
            {
                _log.Error($"ValueSearch LocateInGameEngine handler error: {addr}", ex);
            }
        };
        ValueSearch.RequestCopyText += async (text) =>
        {
            if (string.IsNullOrEmpty(text)) return;
            try { await _platform.CopyToClipboardAsync(text); }
            catch (Exception ex)
            {
                _log.Error($"ValueSearch clipboard copy failed: {ex.Message}", ex);
            }
        };

        // Wire ValueSearch -> InstanceFinder (the per-row "inst" button:
        // pre-fill the hit's owning class + switch tab + auto-run the search,
        // mirroring the Property Search / Game Class "finder" handoff above).
        ValueSearch.NavigateToInstanceFinder += async (className) =>
        {
            try
            {
                SelectedTabIndex = (int)MainTabIndex.InstanceFinder; // Switch to Instance Finder tab
                await InstanceFinder.SearchForClassAsync(className);
            }
            catch (Exception ex)
            {
                _log.Error("ValueSearch NavigateToInstanceFinder handler error", ex);
            }
        };

        // Wire InterestingFunctions -> clipboard. The VM avoids holding
        // IPlatformService directly so its test stubs stay minimal; the
        // MainWindow knows the platform service and can do the actual
        // copy here. Status text already set by the VM.
        InterestingFunctions.RequestCopyText += async (text) =>
        {
            if (string.IsNullOrEmpty(text)) return;
            try { await _platform.CopyToClipboardAsync(text); }
            catch (Exception ex)
            {
                _log.Error($"InterestingFunctions clipboard copy failed: {ex.Message}", ex);
            }
        };

        // Wire InterestingFunctions -> CT save dialog (sister to the
        // Properties hookup above).
        InterestingFunctions.RequestSaveCheatTable += async (defaultName, ctXml) =>
        {
            await SaveCheatTableAsync(defaultName, ctXml, "InterestingFunctions");
        };

        // Wire InterestingFunctions -> Copy AA Script (Baked).
        // Walks the class to fetch the chosen UFunction's full param metadata, then either
        // generates a no-arg script directly (fast path) or opens InvokeParamDialog in
        // CopyBakedScript mode for the user to fill values.
        InterestingFunctions.RequestCopyBakedScript += async (className, funcName, rowClassAddr) =>
        {
            try
            {
                if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(funcName)) return;

                // Need a class address to walk_functions. The row already has one — it
                // came from list_all_functions — so use it. See ResolveClassAddrAsync
                // for why re-deriving it from list_classes was a bug (audit #5 X2).
                var classAddr = await ResolveClassAddrAsync(className, rowClassAddr);
                if (string.IsNullOrEmpty(classAddr)) return;

                var functions = await _dump.WalkFunctionsAsync(classAddr);
                var funcMatch = functions.FirstOrDefault(
                    f => f.Name.Equals(funcName, StringComparison.Ordinal));
                if (funcMatch == null)
                {
                    StatusText = $"{className}::{funcName} not in walk_functions output";
                    return;
                }

                // Find a live instance so the script's invokeUFunction has a target. The
                // helper uses CMD_INVOKE_BY_NAME which finds an instance itself, but
                // running it now lets us surface a clear error if the class is CDO-only.
                var instances = await _dump.FindInstancesAsync(className, exactMatch: true, limit: 1);
                string instanceAddr = "";
                foreach (var inst in instances.Instances)
                {
                    if (!inst.Name.StartsWith("Default__", StringComparison.Ordinal))
                    {
                        instanceAddr = inst.Address;
                        break;
                    }
                }

                // Fast path: TRULY trivial functions only (no inputs AND no return).
                // Functions like KismetSystemLibrary::GetGameName have no inputs
                // but DO return a value -- they need the dialog so the Verify
                // Return Value toggle is reachable. Mirrors LiveWalker's path.
                var inputParams = funcMatch.Params.Where(p => !p.IsReturn).ToList();
                var hasReturn = funcMatch.Params.Any(p => p.IsReturn);
                if (inputParams.Count == 0 && !hasReturn)
                {
                    var script = Services.BakedScriptGenerator.Generate(
                        className, funcName, funcMatch.ParmsSize,
                        Array.Empty<Models.BakedParamValue>());
                    var description = $"Invoke (baked, no args): {className}::{funcName}";
                    // Probe live before send: IsAvailable is only a cache of the
                    // last connect, so without this refresh a CE started/closed
                    // since then makes us assert the wrong "connected" state (X8).
                    if (_aobMaker != null)
                        await _aobMaker.CheckAvailabilityAsync();
                    bool wasAvailable = _aobMaker?.IsAvailable ?? false;
                    bool sentToCe = false;
                    if (_aobMaker != null && wasAvailable)
                        sentToCe = await _aobMaker.CreateAAScriptAsync(description, script, autoActivate: false);
                    if (!sentToCe)
                        // Wrap as paste-able CE memory-record XML (a bare AA body can't
                        // be pasted into a record).
                        await _platform.CopyToClipboardAsync(
                            Services.CheatTableBuilder.WrapAaScriptXml(description, script));
                    // Sync VM-level state so InterestingFunctions tab's Notes
                    // column reflects post-send reality.
                    if (_aobMaker != null)
                        InterestingFunctions.IsAobMakerAvailable = _aobMaker.IsAvailable;
                    StatusText = sentToCe
                        ? $"AA Script created in CE: {funcName}"
                        : wasAvailable
                            ? $"⚠ AOBMaker pipe broke (CE closed?) — script copied as CE XML (paste into CE's address list)"
                            : $"AOBMaker not connected — script copied as CE XML, paste into CE's address list ({funcName})";
                    _log.Info($"InterestingFunctions baked AA Script (no args) " +
                              $"{(sentToCe ? "sent to CE" : "to clipboard")}: " +
                              $"{className}::{funcName} (wasAvailable={wasAvailable})");
                    return;
                }

                // Otherwise open the dialog in CopyBakedScript mode.
                if (Avalonia.Application.Current?.ApplicationLifetime is not
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    || desktop.MainWindow is not { } owner)
                    return;

                var dialog = new Views.InvokeParamDialog(
                    className, funcName, inputParams, funcMatch.Params, funcMatch.ParmsSize,
                    instanceAddr, _dump, _engineState?.UEVersion ?? 0,
                    aobMaker: _aobMaker, platform: _platform,
                    mode: Views.InvokeDialogMode.CopyBakedScript);
                var result = await dialog.ShowDialog<string?>(owner);
                StatusText = result == "ok"
                    ? $"AA Script ready: {className}::{funcName}"
                    : $"AA Script export cancelled: {funcName}";
                _log.Info($"InterestingFunctions CopyBakedScript dialog " +
                          $"{(result == "ok" ? "completed" : "cancelled")}: {className}::{funcName}");
            }
            catch (Exception ex)
            {
                _log.Error($"InterestingFunctions RequestCopyBakedScript handler error: {className}::{funcName}", ex);
                StatusText = $"AA Script export failed: {ex.Message}";
            }
        };

        // ─────────────────────────────────────────────────────────────
        // Console panel wiring (Console = UFUNCTION(exec) discovery+invoke)
        //
        // Mirrors the InterestingFunctions handler bodies above. Duplicated
        // intentionally for v1 — a future shared-helper extraction would
        // touch GameClassFilter / InterestingFunctions / InterestingProperties
        // / Console (4 callers), worth its own refactor pass.
        // ─────────────────────────────────────────────────────────────

        Console.NavigateToFunction += async (className, funcName) =>
        {
            try
            {
                if (string.IsNullOrEmpty(className)) return;

                var instances = await _dump.FindInstancesAsync(className, exactMatch: true, limit: 5);
                string? liveAddr = null;
                foreach (var inst in instances.Instances)
                {
                    if (string.IsNullOrEmpty(inst.Address)) continue;
                    if (inst.Name.StartsWith("Default__", StringComparison.Ordinal)) continue;
                    liveAddr = inst.Address;
                    break;
                }

                if (!string.IsNullOrEmpty(liveAddr))
                {
                    SelectedTabIndex = (int)MainTabIndex.LiveWalker; // Live Walker
                    await LiveWalker.NavigateToAddressCommand.ExecuteAsync(liveAddr);
                    var picked = await LiveWalker.TrySelectFunctionByNameAsync(funcName);
                    StatusText = picked
                        ? $"Navigated to {className}::{funcName} (live instance {liveAddr})"
                        : $"Navigated to {className} @ {liveAddr}; exec '{funcName}' not in this class";
                    _log.Info($"Console -> LiveWalker: {className}::{funcName} @ {liveAddr}" +
                              (picked ? "" : " (function not selected)"));
                }
                else
                {
                    SelectedTabIndex = (int)MainTabIndex.ClassStruct; // ClassStruct fallback
                    var classes = await _dump.ListClassesAsync(gameOnly: false);
                    var match = classes.FindClassAddr(className);
                    if (match.Found)
                    {
                        await ShowClassInClassStructAsync(match.Addr);
                        StatusText = $"No live instance of {className}; showing class metadata " +
                                     $"(exec '{funcName}' — UCheatManager subclasses often need an active PlayerController)";
                        _log.Info($"Console -> ClassStruct fallback: {className}::{funcName}");
                    }
                    else
                    {
                        StatusText = $"No live instance of {className}, and the class {match.MissReason}";
                        _log.Warn($"Console navigate: {className} {match.MissReason}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Console NavigateToFunction handler error: {className}::{funcName}", ex);
            }
        };

        // Console -> RequestParameterInvoke fires when a multi-param exec
        // command is selected. Opens the standard InvokeParamDialog in
        // PipeInvoke mode so the user fills values + presses FIRE to run.
        Console.RequestParameterInvoke += async (className, funcName, rowClassAddr) =>
        {
            try
            {
                if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(funcName)) return;

                var classAddr = await ResolveClassAddrAsync(className, rowClassAddr);
                if (string.IsNullOrEmpty(classAddr)) return;

                var functions = await _dump.WalkFunctionsAsync(classAddr);
                var funcMatch = functions.FirstOrDefault(
                    f => f.Name.Equals(funcName, StringComparison.Ordinal));
                if (funcMatch == null)
                {
                    StatusText = $"{className}::{funcName} not in walk_functions output";
                    return;
                }

                var instances = await _dump.FindInstancesAsync(className, exactMatch: true, limit: 1);
                string instanceAddr = "";
                foreach (var inst in instances.Instances)
                {
                    if (!inst.Name.StartsWith("Default__", StringComparison.Ordinal))
                    {
                        instanceAddr = inst.Address;
                        break;
                    }
                }

                if (Avalonia.Application.Current?.ApplicationLifetime is not
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    || desktop.MainWindow is not { } owner)
                    return;

                var inputParams = funcMatch.Params.Where(p => !p.IsReturn).ToList();
                var dialog = new Views.InvokeParamDialog(
                    className, funcName, inputParams, funcMatch.Params, funcMatch.ParmsSize,
                    instanceAddr, _dump, _engineState?.UEVersion ?? 0,
                    aobMaker: _aobMaker, platform: _platform,
                    mode: Views.InvokeDialogMode.PipeInvoke);
                var result = await dialog.ShowDialog<string?>(owner);
                StatusText = result == "ok"
                    ? $"exec {className}::{funcName} dialog closed"
                    : $"exec {funcName} cancelled";
                _log.Info($"Console PipeInvoke dialog " +
                          $"{(result == "ok" ? "completed" : "cancelled")}: {className}::{funcName}");
            }
            catch (Exception ex)
            {
                _log.Error($"Console RequestParameterInvoke handler error: {className}::{funcName}", ex);
                StatusText = $"exec dialog failed: {ex.Message}";
            }
        };

        // Console -> RequestCopyBakedScript reuses the InterestingFunctions
        // logic body. Same shape as above (no-arg fast path + dialog path).
        Console.RequestCopyBakedScript += async (className, funcName, rowClassAddr) =>
        {
            try
            {
                if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(funcName)) return;

                var classAddr = await ResolveClassAddrAsync(className, rowClassAddr);
                if (string.IsNullOrEmpty(classAddr)) return;

                var functions = await _dump.WalkFunctionsAsync(classAddr);
                var funcMatch = functions.FirstOrDefault(
                    f => f.Name.Equals(funcName, StringComparison.Ordinal));
                if (funcMatch == null)
                {
                    StatusText = $"{className}::{funcName} not in walk_functions output";
                    return;
                }

                var instances = await _dump.FindInstancesAsync(className, exactMatch: true, limit: 1);
                string instanceAddr = "";
                foreach (var inst in instances.Instances)
                {
                    if (!inst.Name.StartsWith("Default__", StringComparison.Ordinal))
                    {
                        instanceAddr = inst.Address;
                        break;
                    }
                }

                var inputParams = funcMatch.Params.Where(p => !p.IsReturn).ToList();
                var hasReturn = funcMatch.Params.Any(p => p.IsReturn);
                if (inputParams.Count == 0 && !hasReturn)
                {
                    var script = Services.BakedScriptGenerator.Generate(
                        className, funcName, funcMatch.ParmsSize,
                        Array.Empty<Models.BakedParamValue>());
                    var description = $"exec (baked, no args): {className}::{funcName}";
                    // Probe live before send — IsAvailable is a stale connect cache (X8).
                    if (_aobMaker != null)
                        await _aobMaker.CheckAvailabilityAsync();
                    bool wasAvailable = _aobMaker?.IsAvailable ?? false;
                    bool sentToCe = false;
                    if (_aobMaker != null && wasAvailable)
                        sentToCe = await _aobMaker.CreateAAScriptAsync(description, script, autoActivate: false);
                    if (!sentToCe)
                        // Wrap as paste-able CE memory-record XML (a bare AA body can't
                        // be pasted into a record).
                        await _platform.CopyToClipboardAsync(
                            Services.CheatTableBuilder.WrapAaScriptXml(description, script));
                    StatusText = sentToCe
                        ? $"AA Script created in CE: {funcName}"
                        : wasAvailable
                            ? $"⚠ AOBMaker pipe broke (CE closed?) — script copied as CE XML (paste into CE's address list)"
                            : $"AOBMaker not connected — script copied as CE XML, paste into CE's address list ({funcName})";
                    _log.Info($"Console baked AA Script (no args) " +
                              $"{(sentToCe ? "sent to CE" : "to clipboard")}: " +
                              $"{className}::{funcName} (wasAvailable={wasAvailable})");
                    return;
                }

                if (Avalonia.Application.Current?.ApplicationLifetime is not
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    || desktop.MainWindow is not { } owner)
                    return;

                var dialog = new Views.InvokeParamDialog(
                    className, funcName, inputParams, funcMatch.Params, funcMatch.ParmsSize,
                    instanceAddr, _dump, _engineState?.UEVersion ?? 0,
                    aobMaker: _aobMaker, platform: _platform,
                    mode: Views.InvokeDialogMode.CopyBakedScript);
                var result = await dialog.ShowDialog<string?>(owner);
                StatusText = result == "ok"
                    ? $"AA Script ready: {className}::{funcName}"
                    : $"AA Script export cancelled: {funcName}";
                _log.Info($"Console CopyBakedScript dialog " +
                          $"{(result == "ok" ? "completed" : "cancelled")}: {className}::{funcName}");
            }
            catch (Exception ex)
            {
                _log.Error($"Console RequestCopyBakedScript handler error: {className}::{funcName}", ex);
                StatusText = $"AA Script export failed: {ex.Message}";
            }
        };

        // Console -> RequestDebugCameraCeScript builds the stateful
        // setDebugCamera [ENABLE]/[DISABLE] memory-record script and ships it
        // to AOBMaker (a CE AA-script record) or the clipboard. Self-contained:
        // no class/func resolution needed — the helper resolves the export.
        Console.RequestDebugCameraCeScript += async () =>
        {
            try
            {
                var script = Services.DebugCameraScriptGenerator.Generate();
                const string description = "Debug Camera (force on/off): setDebugCamera";
                // Probe live before send — IsAvailable is a stale connect cache (X8).
                if (_aobMaker != null)
                    await _aobMaker.CheckAvailabilityAsync();
                bool wasAvailable = _aobMaker?.IsAvailable ?? false;
                bool sentToCe = false;
                if (_aobMaker != null && wasAvailable)
                    sentToCe = await _aobMaker.CreateAAScriptAsync(description, script, autoActivate: false);
                if (!sentToCe)
                    // Wrap as paste-able CE memory-record XML (a bare AA body can't be
                    // pasted into a record). The script is self-contained (talks to the
                    // mailbox directly) — no ue5_invoke_helper.lua needed.
                    await _platform.CopyToClipboardAsync(
                        Services.CheatTableBuilder.WrapAaScriptXml(description, script));
                StatusText = sentToCe
                    ? "Debug Camera AA Script created in CE (tick = ON, untick = OFF)."
                    : wasAvailable
                        ? "⚠ AOBMaker pipe broke (CE closed?) — Debug Camera script copied as CE XML (paste into CE's address list)."
                        : "AOBMaker not connected — Debug Camera script copied as CE XML — paste into CE's address list (tick = ON, untick = OFF).";
                _log.Info($"Console Debug Camera CE script " +
                          $"{(sentToCe ? "sent to CE" : "to clipboard")} (wasAvailable={wasAvailable})");
            }
            catch (Exception ex)
            {
                _log.Error("Console RequestDebugCameraCeScript handler error", ex);
                StatusText = $"Debug Camera script export failed: {ex.Message}";
            }
        };

        _pipeClient.ConnectionStateChanged += (connected) =>
        {
            if (!connected) _log.StopProcessMirror();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsConnected = connected;
                StatusText = connected ? "Connected" : "Disconnected";
                Teleport.SetConnected(connected);
                DumpExplorer.SetConnected(connected);
                if (connected)
                {
                    // Fresh connection-scoped cancellation for this session's
                    // long-running exports (X6). All field access is on the UI thread.
                    _connectionCts.Dispose();
                    _connectionCts = new CancellationTokenSource();
                }
                else
                {
                    WindowTitle = "UE5 Dump UI";
                    GameThreadStalled = false;   // clear the paused banner on disconnect
                    // Abort any in-flight export bound to the pipe that just dropped
                    // so it stops round-tripping to a dead game (X6).
                    _connectionCts.Cancel();
                    // End the session epoch and cancel any pending proxy-confirm dwell, so
                    // a proxy that just crashed the game isn't recorded as working when the
                    // user reconnects within the 20 s dwell. (M8)
                    _sessionEpoch++;
                    _proxyConfirmTimer?.Dispose();
                    _proxyConfirmTimer = null;
                    LiveFuncs.ResetOnDisconnect();   // clear stuck "recording" UI state (L16)
                    // The banner names a PID. Left standing it pins a dead one for the
                    // rest of the session and keeps warning about a conflict that ended
                    // when the game closed. (B9)
                    MultipleDumperHostsWarning = "";
                    ResetPanelsOnDisconnect();   // clear stale process-scoped rows (X5)
                }
            });
        };

        // The DLL flags a paused/suspended game thread on every response; surface
        // it as an always-visible banner so a user who paused the game and switched
        // to the UI understands why live-camera features time out (and that memory
        // scans still work). Marshalled to the UI thread — raised off a pipe thread.
        _pipeClient.GameThreadStalledChanged += (stalled) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => GameThreadStalled = stalled);
    }

    /// <summary>
    /// Audit fixes #16/#17: dispose owned child VMs that hold timers /
    /// CancellationTokenSources. Called from MainWindow.Closed so timer
    /// callbacks don't fire after the window is gone. Any child VM that is
    /// IDisposable (owns a timer / CTS / SQLite handle) MUST be disposed here —
    /// the VM's own Dispose() has no other caller.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Detach from the static xref-dialog hooks so this VM doesn't leak.
        Views.PropertyXrefDialog.LocateClassInGWorldRequested -= _xrefLocateHandler;
        Views.PropertyXrefDialog.SharedAobMaker = null;

        ObjectTree.Dispose();
        LiveWalker.Dispose();
        // InstanceFinder owns a keyword-filter debounce Timer + a class-noise re-run
        // CTS (the Timer/CTS lambdas root the VM); dispose so they can't fire after teardown.
        InstanceFinder.Dispose();
        // PropertySearch is IDisposable (owns a debounce System.Threading.Timer);
        // its Dispose had no caller before, leaking the timer until process exit.
        PropertySearch.Dispose();
        // Teleport owns a DispatcherTimer (auto-refresh) — dispose so it can't
        // tick after the window closes.
        Teleport.Dispose();
        // Cancel a pending LKG proxy-confirm dwell so its callback can't fire after
        // the window is gone. (M8)
        _proxyConfirmTimer?.Dispose();
        _proxyConfirmTimer = null;
        // Cancel + dispose the connection-scoped export cancellation (X6).
        try { _connectionCts.Cancel(); } catch { }
        _connectionCts.Dispose();

        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            ClearError();
            // Skipped while a retry loop owns the message: otherwise every attempt
            // overwrites the countdown with "Connecting..." and the user sees no
            // progress at all -- which is what widening the window to 45 s looked like.
            if (!SuppressConnectErrors) StatusText = "Connecting...";
            LiveWalker.ClearAllBookmarks();

            await _pipeClient.ConnectAsync();

            var state = await _dump.InitAsync();
            _engineState = state;
            IsConnected = true;

            // Detect proxy DLL mode: connected but scan not yet done
            // (UE version 0 and all pointers at 0x0 / empty)
            bool notScanned = state.UEVersion == 0
                && state.ObjectCount == 0
                && (string.IsNullOrEmpty(state.GObjectsAddr) || state.GObjectsAddr == "0x0");

            if (notScanned)
            {
                NeedsScan = true;
                StatusText = "Connected — waiting for scan (load a save first, then click Start Scan)";

                if (!string.IsNullOrEmpty(state.ModuleName))
                {
                    WindowTitle = $"UE5 Dump UI — {state.ModuleName}";
                    _log.StartProcessMirror(state.ModuleName);
                }

                _log.Info(Constants.LogCatInit, "Connected (proxy mode — scan not yet triggered)");
            }
            else
            {
                NeedsScan = false;
                ApplyEngineState(state);
            }
        }
        catch (Exception ex)
        {
            // During the post-inject retry window a failed attempt is EXPECTED -- the DLL
            // has not opened its pipe yet -- so shouting "Connection Error" in red is a lie
            // that then resolves itself a few seconds later. The retry owns the message
            // while it is running; a real failure still lands here once it gives up.
            if (SuppressConnectErrors)
            {
                _log.Info(Constants.LogCatInit, $"Connect attempt failed while waiting for the DLL: {ex.Message}");
                return;
            }
            StatusText = "Connection Error";
            SetError(ex);
            _log.Error(Constants.LogCatInit, "Connection failed", ex);
        }
    }

    /// <summary>Set while something is deliberately retrying <c>ConnectCommand</c> and owns
    /// the user-facing message itself (see ProxyDeployViewModel's post-inject retry).
    /// Suppresses BOTH the red "Connection Error" on an expected failure and the
    /// "Connecting..." status — the latter because it repaints once per attempt and would
    /// otherwise erase the countdown a moment after it appears.</summary>
    public bool SuppressConnectErrors { get; set; }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        try
        {
            ClearError();
            ObjectTree.CancelLoadCommand.Execute(null);
            _log.StopProcessMirror();
            await _pipeClient.DisconnectAsync();
            StatusText = "Disconnected";
            WindowTitle = "UE5 Dump UI";
            IsConnected = false;
            NeedsScan = false;
            IsScanning = false;
        }
        catch (OperationCanceledException)
        {
            // Expected during disconnect
            StatusText = "Disconnected";
            IsConnected = false;
        }
        catch (Exception ex)
        {
            // Suppress pipe-related errors during disconnect
            if (ex is IOException or ObjectDisposedException)
            {
                StatusText = "Disconnected";
                IsConnected = false;
            }
            else
            {
                SetError(ex);
            }
        }
        finally
        {
            LiveWalker.ClearAllBookmarks();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Global panel/Live-Walker OPTIONS persistence (stable preferences only).
    //
    // Wiring lives here (not in the giant ctor) and is kicked off by App after
    // construction via InitializeOptionsPersistence. LOAD applies saved values
    // under _suppressOptionSave so the resulting PropertyChanged storm doesn't
    // re-save. SAVE is a single debounced write triggered when any *persistable*
    // property of a tracked VM changes — filtered by the per-VM name sets so the
    // constant live-data churn (Fields/StatusText/results/selections) never saves.
    // ──────────────────────────────────────────────────────────────────────

    private UiOptionsStore? _uiOptions;
    private bool _suppressOptionSave;
    private Timer? _optionSaveDebounce;
    private const int OptionSaveDebounceMs = 400;

    /// <summary>
    /// Called by App right after construction: load the saved options, apply them
    /// (suppressed), then start tracking changes for debounced save-on-change.
    /// </summary>
    public void InitializeOptionsPersistence(UiOptionsStore store)
    {
        _uiOptions = store;
        var o = store.Load();

        _suppressOptionSave = true;
        try { ApplyOptions(o); }
        catch (Exception ex) { _log.Error("UiOptions apply failed", ex); }
        finally { _suppressOptionSave = false; }

        WireOptionSaveTracking();
    }

    /// <summary>Cancel the debounce and write the current options immediately
    /// (called on app shutdown so a change made &lt;debounce before exit lands).</summary>
    public void FlushOptions()
    {
        _optionSaveDebounce?.Change(Timeout.Infinite, Timeout.Infinite);
        SaveOptionsNow();
    }

    private void ScheduleOptionSave()
    {
        if (_uiOptions == null) return;
        _optionSaveDebounce ??= new Timer(_ => SaveOptionsNow());
        _optionSaveDebounce.Change(OptionSaveDebounceMs, Timeout.Infinite);
    }

    // Reads simple value-type VM properties only (bool/int/double/enum/string) —
    // safe to run on the debounce threadpool thread (no collections, no UI objects).
    private void SaveOptionsNow()
    {
        try { _uiOptions?.Save(BuildOptions()); }
        catch (Exception ex) { _log.Error("UiOptions save failed", ex); }
    }

    private void Track(ObservableObject vm, HashSet<string> persistable)
    {
        vm.PropertyChanged += (_, e) =>
        {
            if (_suppressOptionSave) return;
            if (e.PropertyName != null && persistable.Contains(e.PropertyName))
                ScheduleOptionSave();
        };
    }

    private void WireOptionSaveTracking()
    {
        Track(this, MainPersist);
        Track(LiveWalker, LiveWalkerPersist);
        Track(ValueSearch, ValueSearchPersist);
        Track(InstanceFinder, InstanceFinderPersist);
        Track(PropertySearch, PropertySearchPersist);
        Track(Teleport, TeleportPersist);
        Track(InterestingFunctions, InterestingFuncsPersist);
        Track(InterestingProperties, InterestingPropsPersist);
        Track(Console, ConsolePersist);
        Track(GameClassFilter, GameClassFilterPersist);
        if (Snapshot != null) Track(Snapshot, SnapshotPersist);
        if (Spc != null) Track(Spc, SpcPersist);
        if (Pivot != null) Track(Pivot, PivotPersist);
        if (ProxyDeploy != null) Track(ProxyDeploy, ProxyDeployPersist);
        Track(Pointers, SystemPersist);
    }

    // Persistable property-name sets — used both to filter PropertyChanged and as
    // the single source of truth for what each VM persists. nameof keeps them
    // compile-safe against renames.
    private static readonly HashSet<string> MainPersist = new()
    {
        nameof(SelectedAddressFormatIndex), nameof(CollapsePointerNodes),
        nameof(ArrayLimitExponent), nameof(DropDownLimitExponent),
        nameof(CsxDrilldownDepth), nameof(PreviewLimit), nameof(DeepScanElemCapExponent),
        nameof(CeStringLengthExponent), nameof(FabricateArrayCountExponent),
    };
    private static readonly HashSet<string> LiveWalkerPersist = new()
    {
        nameof(LiveWalkerViewModel.CollapseChain), nameof(LiveWalkerViewModel.DescShowOffset),
        nameof(LiveWalkerViewModel.DescShowType),
        // AobSymbolPreference (the INTENT), NOT UseAobSymbol (the gated live value) —
        // a fallback-GWorld force-uncheck changes UseAobSymbol but must not trigger a save.
        nameof(LiveWalkerViewModel.AobSymbolPreference),
        nameof(LiveWalkerViewModel.FlattenGasAttributes),
        nameof(LiveWalkerViewModel.FlattenLeafStructs), nameof(LiveWalkerViewModel.FlattenLeafRecords),
        nameof(LiveWalkerViewModel.CollapseLeafPointers),
        nameof(LiveWalkerViewModel.FlattenColorEnabled), nameof(LiveWalkerViewModel.FlattenColorEven),
        nameof(LiveWalkerViewModel.FlattenColorOdd),
        nameof(LiveWalkerViewModel.DedupSharedObjects),
        nameof(LiveWalkerViewModel.ExcludeSystemComponents), nameof(LiveWalkerViewModel.GWorldLocateDepth),
        nameof(LiveWalkerViewModel.GWorldLocateDeep),
        nameof(LiveWalkerViewModel.AutoRefreshIntervalSec),
    };
    private static readonly HashSet<string> ValueSearchPersist = new()
    {
        nameof(ValueSearchViewModel.SelectedDataType), nameof(ValueSearchViewModel.SelectedScanType),
        nameof(ValueSearchViewModel.GameOnly), nameof(ValueSearchViewModel.MaxResults),
        nameof(ValueSearchViewModel.ScanTimeoutSeconds), nameof(ValueSearchViewModel.ParallelScan),
        nameof(ValueSearchViewModel.BatchRead), nameof(ValueSearchViewModel.DeepScan),
        nameof(ValueSearchViewModel.CrossObjectScan), nameof(ValueSearchViewModel.NativeCScan),
        nameof(ValueSearchViewModel.NewestFirst), nameof(ValueSearchViewModel.PreFilterNoise),
        nameof(ValueSearchViewModel.SelectedRoundingMode), nameof(ValueSearchViewModel.CaseSensitive),
    };
    private static readonly HashSet<string> SnapshotPersist = new()
    {
        nameof(SnapshotViewModel.GameOnly), nameof(SnapshotViewModel.AutoSkipNoise),
        nameof(SnapshotViewModel.IncludeNativeFields), nameof(SnapshotViewModel.SelectedScope),
        nameof(SnapshotViewModel.SelectedFamily), nameof(SnapshotViewModel.SelectedMaxDataset),
        nameof(SnapshotViewModel.ShowUsageBar), nameof(SnapshotViewModel.GroupDeep),
        nameof(SnapshotViewModel.SelectedRoundingMode),
        // Auto snapshot settings (the running toggle is session-only — not persisted).
        nameof(SnapshotViewModel.AutoSnapshotIntervalSec), nameof(SnapshotViewModel.RetentionMode),
        nameof(SnapshotViewModel.AutoSnapshotCount), nameof(SnapshotViewModel.AutoSnapshotAdjustQuota),
        nameof(SnapshotViewModel.SnapshotMinFreePercent), nameof(SnapshotViewModel.SnapshotMinFreeGb),
    };
    private static readonly HashSet<string> InstanceFinderPersist = new()
    {
        nameof(InstanceFinderViewModel.ExactMatch), nameof(InstanceFinderViewModel.NewestFirst),
        nameof(InstanceFinderViewModel.InstanceSearchCap), nameof(InstanceFinderViewModel.DeepScanElemCap),
    };
    private static readonly HashSet<string> PropertySearchPersist = new()
    {
        nameof(PropertySearchViewModel.GameClassesOnly), nameof(PropertySearchViewModel.DeepSearch),
    };
    private static readonly HashSet<string> TeleportPersist = new()
    {
        nameof(TeleportViewModel.ZOffset), nameof(TeleportViewModel.TraceChannel),
        nameof(TeleportViewModel.FallbackToCenter), nameof(TeleportViewModel.CursorHotkeyEnabled),
        nameof(TeleportViewModel.RelativeDistance), nameof(TeleportViewModel.RelativeHorizontal),
        nameof(TeleportViewModel.CoordSetRotation), nameof(TeleportViewModel.AutoRefresh),
        // ApplyOptions/BuildOptions already round-trip these two; without them here a
        // slider change never schedules a save, so the value only landed if some other
        // Teleport option happened to change too (X10).
        nameof(TeleportViewModel.WorldTimeDilation), nameof(TeleportViewModel.PawnTimeDilation),
    };
    private static readonly HashSet<string> SpcPersist = new()
    {
        nameof(SpcQueryViewModel.SelectedJoinMode),
        nameof(SpcQueryViewModel.SelectedRoundingMode),
    };
    private static readonly HashSet<string> PivotPersist = new()
    {
        nameof(ClassPivotViewModel.SelectedSource), nameof(ClassPivotViewModel.SelectedKeyMode),
    };
    private static readonly HashSet<string> InterestingFuncsPersist = new()
    {
        nameof(InterestingFunctionsViewModel.GameOnly), nameof(InterestingFunctionsViewModel.ShowAll),
    };
    private static readonly HashSet<string> InterestingPropsPersist = new()
    {
        nameof(InterestingPropertiesViewModel.GameOnly), nameof(InterestingPropertiesViewModel.UnusualOnly),
        nameof(InterestingPropertiesViewModel.ShowAll),
    };
    private static readonly HashSet<string> ConsolePersist = new() { nameof(ConsoleViewModel.GameOnly) };
    private static readonly HashSet<string> GameClassFilterPersist = new() { nameof(GameClassFilterViewModel.GameClassesOnly) };
    private static readonly HashSet<string> ProxyDeployPersist = new()
    {
        nameof(ProxyDeployViewModel.SelectedProxyType), nameof(ProxyDeployViewModel.ForceOverwrite),
        nameof(ProxyDeployViewModel.ScanDrivesMode), nameof(ProxyDeployViewModel.LkgSuggestEnabled),
    };
    // System tab. LogCompressionSupported is NOT here on purpose: it is a fact about the
    // volume, re-derived every launch, not a preference.
    private static readonly HashSet<string> SystemPersist = new()
    {
        nameof(PointerPanelViewModel.AutoCompressLogs),
    };

    /// <summary>Apply saved options to every VM. Runs under _suppressOptionSave.
    /// Setters with side effects (address-format fan-out, scan-timeout clamp,
    /// NativeCScan→NewestFirst) still fire — only the SAVE is suppressed.</summary>
    private void ApplyOptions(UiOptionsSettings o)
    {
        // Main display controls first — their OnChanged fans out to child VMs.
        SelectedAddressFormatIndex = o.Main.SelectedAddressFormatIndex;
        CollapsePointerNodes = o.Main.CollapsePointerNodes;
        ArrayLimitExponent = o.Main.ArrayLimitExponent;
        DropDownLimitExponent = o.Main.DropDownLimitExponent;
        CsxDrilldownDepth = o.Main.CsxDrilldownDepth;
        PreviewLimit = o.Main.PreviewLimit;
        DeepScanElemCapExponent = o.Main.DeepScanElemCapExponent;
        CeStringLengthExponent = o.Main.CeStringLengthExponent;
        FabricateArrayCountExponent = o.Main.FabricateArrayCountExponent;

        var lw = o.LiveWalker;
        LiveWalker.CollapseChain = lw.CollapseChain;
        LiveWalker.DescShowOffset = lw.DescShowOffset;
        LiveWalker.DescShowType = lw.DescShowType;
        // Restore the AOB intent; the live checkbox is re-derived against the gate
        // (CanUseAobSymbol) the moment an engine state / GWorld root arrives.
        LiveWalker.AobSymbolPreference = lw.UseAobSymbol;
        LiveWalker.FlattenGasAttributes = lw.FlattenGasAttributes;
        LiveWalker.FlattenLeafStructs = lw.FlattenLeafStructs;
        LiveWalker.FlattenLeafRecords = lw.FlattenLeafRecords;
        LiveWalker.CollapseLeafPointers = lw.CollapseLeafPointers;
        LiveWalker.FlattenColorEnabled = lw.FlattenColorEnabled;
        LiveWalker.FlattenColorEven = lw.FlattenColorEven;
        LiveWalker.FlattenColorOdd = lw.FlattenColorOdd;
        LiveWalker.DedupSharedObjects = lw.DedupSharedObjects;
        LiveWalker.ExcludeSystemComponents = lw.ExcludeSystemComponents;
        LiveWalker.GWorldLocateDepth = lw.GWorldLocateDepth;
        LiveWalker.GWorldLocateDeep = lw.GWorldLocateDeep;
        LiveWalker.AutoRefreshIntervalSec = lw.AutoRefreshIntervalSec;

        var vs = o.ValueSearch;
        vs_Apply(vs);

        var inf = o.InstanceFinder;
        InstanceFinder.ExactMatch = inf.ExactMatch;
        InstanceFinder.NewestFirst = inf.NewestFirst;
        InstanceFinder.InstanceSearchCap = inf.InstanceSearchCap;
        InstanceFinder.DeepScanElemCap = inf.DeepScanElemCap;

        PropertySearch.GameClassesOnly = o.PropertySearch.GameClassesOnly;
        PropertySearch.DeepSearch = o.PropertySearch.DeepSearch;
        // Clamp on LOAD as well as in the control: ui-options.json is a plain file a
        // user can edit, and a hand-written 0 there would make every search return
        // nothing with no visible cause. [PROPSEARCHCAP-2026-08-19]
        PropertySearch.PropertySearchCap = Math.Clamp(
            o.PropertySearch.PropertySearchCap,
            Constants.MinSearchCap, Constants.MaxSearchCap);

        var tp = o.Teleport;
        Teleport.ZOffset = tp.ZOffset;
        Teleport.TraceChannel = tp.TraceChannel;
        Teleport.FallbackToCenter = tp.FallbackToCenter;
        Teleport.CursorHotkeyEnabled = tp.CursorHotkeyEnabled;
        Teleport.RelativeDistance = tp.RelativeDistance;
        Teleport.RelativeHorizontal = tp.RelativeHorizontal;
        Teleport.CoordSetRotation = tp.CoordSetRotation;
        Teleport.AutoRefresh = tp.AutoRefresh;
        Teleport.WorldTimeDilation = tp.WorldTimeDilation;
        Teleport.PawnTimeDilation = tp.PawnTimeDilation;

        InterestingFunctions.GameOnly = o.InterestingFuncs.GameOnly;
        InterestingFunctions.ShowAll = o.InterestingFuncs.ShowAll;

        InterestingProperties.GameOnly = o.InterestingProps.GameOnly;
        InterestingProperties.UnusualOnly = o.InterestingProps.UnusualOnly;
        InterestingProperties.ShowAll = o.InterestingProps.ShowAll;

        Console.GameOnly = o.Console.GameOnly;
        GameClassFilter.GameClassesOnly = o.GameClassFilter.GameClassesOnly;
        // Clamped on LOAD too: ui-options.json is plain text a user can edit, and a
        // hand-written 0 would make the Classes tab return nothing with no visible cause.
        GameClassFilter.ClassListCap = Math.Clamp(
            o.GameClassFilter.ClassListCap, Constants.MinSearchCap, Constants.MaxSearchCap);

        if (Snapshot != null)
        {
            var sn = o.Snapshot;
            Snapshot.GameOnly = sn.GameOnly;
            Snapshot.AutoSkipNoise = sn.AutoSkipNoise;
            Snapshot.IncludeNativeFields = sn.IncludeNativeFields;
            Snapshot.SelectedScope = sn.SelectedScope;
            Snapshot.SelectedFamily = sn.SelectedFamily;
            Snapshot.SelectedMaxDataset = sn.SelectedMaxDataset;
            Snapshot.ShowUsageBar = sn.ShowUsageBar;
            Snapshot.GroupDeep = sn.GroupDeep;
            Snapshot.SelectedRoundingMode = sn.RoundingMode;
            Snapshot.AutoSnapshotIntervalSec = sn.AutoSnapshotIntervalSec;
            Snapshot.RetentionMode = sn.RetentionMode;
            Snapshot.AutoSnapshotCount = sn.AutoSnapshotCount;
            Snapshot.AutoSnapshotAdjustQuota = sn.AutoSnapshotAdjustQuota;
            Snapshot.SnapshotMinFreePercent = sn.SnapshotMinFreePercent;
            Snapshot.SnapshotMinFreeGb = sn.SnapshotMinFreeGb;
        }
        if (Spc != null)
        {
            Spc.SelectedJoinMode = o.Spc.SelectedJoinMode;
            Spc.SelectedRoundingMode = o.Spc.RoundingMode;
        }
        if (Pivot != null)
        {
            Pivot.SelectedSource = o.Pivot.SelectedSource;
            Pivot.SelectedKeyMode = o.Pivot.SelectedKeyMode;
        }
        Pointers.AutoCompressLogs = o.System.AutoCompressLogs;
        if (ProxyDeploy != null)
        {
            ProxyDeploy.SelectedProxyType = o.ProxyDeploy.SelectedProxyType;
            ProxyDeploy.ForceOverwrite = o.ProxyDeploy.ForceOverwrite;
            ProxyDeploy.ScanDrivesMode = o.ProxyDeploy.ScanDrivesMode;
            ProxyDeploy.LkgSuggestEnabled = o.ProxyDeploy.LkgSuggestEnabled;
            ProxyDeploy.LastManualProxyByGame.Clear();
            foreach (var (name, type) in o.ProxyDeploy.LastManualProxyByGame)
                ProxyDeploy.LastManualProxyByGame[name] = type;
            ProxyDeploy.InjectedGameExes.Clear();
            foreach (var exe in o.ProxyDeploy.InjectedGameExes)
                ProxyDeploy.InjectedGameExes.Add(exe);
            ProxyDeploy.ConfirmedProxyByExe.Clear();
            foreach (var (exe, type) in o.ProxyDeploy.ConfirmedProxyByExe)
                ProxyDeploy.ConfirmedProxyByExe[exe] = type;
        }
    }

    // NativeCScan's setter forces NewestFirst on/off, so apply it BEFORE NewestFirst
    // — otherwise the side effect would clobber the saved NewestFirst value.
    private void vs_Apply(ValueSearchUiOptions vs)
    {
        ValueSearch.SelectedDataType = vs.SelectedDataType;
        ValueSearch.SelectedScanType = vs.SelectedScanType;
        ValueSearch.GameOnly = vs.GameOnly;
        ValueSearch.MaxResults = vs.MaxResults;
        ValueSearch.ScanTimeoutSeconds = vs.ScanTimeoutSeconds;
        ValueSearch.ParallelScan = vs.ParallelScan;
        ValueSearch.BatchRead = vs.BatchRead;
        ValueSearch.DeepScan = vs.DeepScan;
        ValueSearch.CrossObjectScan = vs.CrossObjectScan;
        ValueSearch.NativeCScan = vs.NativeCScan;     // may flip NewestFirst (side effect)
        ValueSearch.NewestFirst = vs.NewestFirst;     // saved value wins (applied last)
        ValueSearch.PreFilterNoise = vs.PreFilterNoise;
        ValueSearch.SelectedRoundingMode = vs.RoundingMode;
        ValueSearch.CaseSensitive = vs.CaseSensitive;
    }

    /// <summary>Snapshot the current option values from every VM into a settings object.</summary>
    private UiOptionsSettings BuildOptions()
    {
        var o = new UiOptionsSettings();

        o.Main.SelectedAddressFormatIndex = SelectedAddressFormatIndex;
        o.Main.CollapsePointerNodes = CollapsePointerNodes;
        o.Main.ArrayLimitExponent = ArrayLimitExponent;
        o.Main.DropDownLimitExponent = DropDownLimitExponent;
        o.Main.CsxDrilldownDepth = CsxDrilldownDepth;
        o.Main.PreviewLimit = PreviewLimit;
        o.Main.DeepScanElemCapExponent = DeepScanElemCapExponent;
        o.Main.CeStringLengthExponent = CeStringLengthExponent;
        o.Main.FabricateArrayCountExponent = FabricateArrayCountExponent;

        o.LiveWalker.CollapseChain = LiveWalker.CollapseChain;
        o.LiveWalker.DescShowOffset = LiveWalker.DescShowOffset;
        o.LiveWalker.DescShowType = LiveWalker.DescShowType;
        // Persist the AOB INTENT, not the live effective checkbox — a fallback-GWorld
        // game force-unchecks UseAobSymbol but must not erase the stored preference.
        o.LiveWalker.UseAobSymbol = LiveWalker.AobSymbolPreference;
        o.LiveWalker.FlattenGasAttributes = LiveWalker.FlattenGasAttributes;
        o.LiveWalker.FlattenLeafStructs = LiveWalker.FlattenLeafStructs;
        o.LiveWalker.FlattenLeafRecords = LiveWalker.FlattenLeafRecords;
        o.LiveWalker.CollapseLeafPointers = LiveWalker.CollapseLeafPointers;
        o.LiveWalker.FlattenColorEnabled = LiveWalker.FlattenColorEnabled;
        o.LiveWalker.FlattenColorEven = LiveWalker.FlattenColorEven;
        o.LiveWalker.FlattenColorOdd = LiveWalker.FlattenColorOdd;
        o.LiveWalker.DedupSharedObjects = LiveWalker.DedupSharedObjects;
        o.LiveWalker.ExcludeSystemComponents = LiveWalker.ExcludeSystemComponents;
        o.LiveWalker.GWorldLocateDepth = LiveWalker.GWorldLocateDepth;
        o.LiveWalker.GWorldLocateDeep = LiveWalker.GWorldLocateDeep;
        o.LiveWalker.AutoRefreshIntervalSec = LiveWalker.AutoRefreshIntervalSec;

        o.ValueSearch.SelectedDataType = ValueSearch.SelectedDataType;
        o.ValueSearch.SelectedScanType = ValueSearch.SelectedScanType;
        o.ValueSearch.GameOnly = ValueSearch.GameOnly;
        o.ValueSearch.MaxResults = ValueSearch.MaxResults;
        o.ValueSearch.ScanTimeoutSeconds = ValueSearch.ScanTimeoutSeconds;
        o.ValueSearch.ParallelScan = ValueSearch.ParallelScan;
        o.ValueSearch.BatchRead = ValueSearch.BatchRead;
        o.ValueSearch.DeepScan = ValueSearch.DeepScan;
        o.ValueSearch.CrossObjectScan = ValueSearch.CrossObjectScan;
        o.ValueSearch.NativeCScan = ValueSearch.NativeCScan;
        o.ValueSearch.NewestFirst = ValueSearch.NewestFirst;
        o.ValueSearch.PreFilterNoise = ValueSearch.PreFilterNoise;
        o.ValueSearch.RoundingMode = ValueSearch.SelectedRoundingMode;
        o.ValueSearch.CaseSensitive = ValueSearch.CaseSensitive;

        o.InstanceFinder.ExactMatch = InstanceFinder.ExactMatch;
        o.InstanceFinder.NewestFirst = InstanceFinder.NewestFirst;
        o.InstanceFinder.InstanceSearchCap = InstanceFinder.InstanceSearchCap;
        o.InstanceFinder.DeepScanElemCap = InstanceFinder.DeepScanElemCap;

        o.PropertySearch.GameClassesOnly = PropertySearch.GameClassesOnly;
        o.PropertySearch.DeepSearch = PropertySearch.DeepSearch;
        o.PropertySearch.PropertySearchCap = PropertySearch.PropertySearchCap;

        o.Teleport.ZOffset = Teleport.ZOffset;
        o.Teleport.TraceChannel = Teleport.TraceChannel;
        o.Teleport.FallbackToCenter = Teleport.FallbackToCenter;
        o.Teleport.CursorHotkeyEnabled = Teleport.CursorHotkeyEnabled;
        o.Teleport.RelativeDistance = Teleport.RelativeDistance;
        o.Teleport.RelativeHorizontal = Teleport.RelativeHorizontal;
        o.Teleport.CoordSetRotation = Teleport.CoordSetRotation;
        o.Teleport.AutoRefresh = Teleport.AutoRefresh;
        o.Teleport.WorldTimeDilation = Teleport.WorldTimeDilation;
        o.Teleport.PawnTimeDilation = Teleport.PawnTimeDilation;

        o.InterestingFuncs.GameOnly = InterestingFunctions.GameOnly;
        o.InterestingFuncs.ShowAll = InterestingFunctions.ShowAll;

        o.InterestingProps.GameOnly = InterestingProperties.GameOnly;
        o.InterestingProps.UnusualOnly = InterestingProperties.UnusualOnly;
        o.InterestingProps.ShowAll = InterestingProperties.ShowAll;

        o.Console.GameOnly = Console.GameOnly;
        o.GameClassFilter.GameClassesOnly = GameClassFilter.GameClassesOnly;
        o.GameClassFilter.ClassListCap = GameClassFilter.ClassListCap;

        if (Snapshot != null)
        {
            o.Snapshot.GameOnly = Snapshot.GameOnly;
            o.Snapshot.AutoSkipNoise = Snapshot.AutoSkipNoise;
            o.Snapshot.IncludeNativeFields = Snapshot.IncludeNativeFields;
            o.Snapshot.SelectedScope = Snapshot.SelectedScope;
            o.Snapshot.SelectedFamily = Snapshot.SelectedFamily;
            o.Snapshot.SelectedMaxDataset = Snapshot.SelectedMaxDataset;
            o.Snapshot.ShowUsageBar = Snapshot.ShowUsageBar;
            o.Snapshot.GroupDeep = Snapshot.GroupDeep;
            o.Snapshot.RoundingMode = Snapshot.SelectedRoundingMode;
            o.Snapshot.AutoSnapshotIntervalSec = Snapshot.AutoSnapshotIntervalSec;
            o.Snapshot.RetentionMode = Snapshot.RetentionMode;
            o.Snapshot.AutoSnapshotCount = Snapshot.AutoSnapshotCount;
            o.Snapshot.AutoSnapshotAdjustQuota = Snapshot.AutoSnapshotAdjustQuota;
            o.Snapshot.SnapshotMinFreePercent = Snapshot.SnapshotMinFreePercent;
            o.Snapshot.SnapshotMinFreeGb = Snapshot.SnapshotMinFreeGb;
        }
        if (Spc != null)
        {
            o.Spc.SelectedJoinMode = Spc.SelectedJoinMode;
            o.Spc.RoundingMode = Spc.SelectedRoundingMode;
        }
        if (Pivot != null)
        {
            o.Pivot.SelectedSource = Pivot.SelectedSource;
            o.Pivot.SelectedKeyMode = Pivot.SelectedKeyMode;
        }
        if (ProxyDeploy != null)
        {
            o.ProxyDeploy.SelectedProxyType = ProxyDeploy.SelectedProxyType;
            o.ProxyDeploy.ForceOverwrite = ProxyDeploy.ForceOverwrite;
            o.ProxyDeploy.ScanDrivesMode = ProxyDeploy.ScanDrivesMode;
            o.ProxyDeploy.LkgSuggestEnabled = ProxyDeploy.LkgSuggestEnabled;
            o.ProxyDeploy.LastManualProxyByGame =
                new Dictionary<string, ProxyType>(ProxyDeploy.LastManualProxyByGame, StringComparer.OrdinalIgnoreCase);
            o.ProxyDeploy.InjectedGameExes = new List<string>(ProxyDeploy.InjectedGameExes);
            o.ProxyDeploy.ConfirmedProxyByExe =
                new Dictionary<string, ProxyType>(ProxyDeploy.ConfirmedProxyByExe, StringComparer.OrdinalIgnoreCase);
        }
        o.System.AutoCompressLogs = Pointers.AutoCompressLogs;

        return o;
    }

    /// <summary>
    /// Clear every panel that holds PROCESS-SCOPED state on disconnect (audit X5).
    /// The old fan-out reset only Teleport / DumpExplorer / LiveFuncs, so a reconnect
    /// (often to a DIFFERENT game) left the other panels showing rows whose addresses
    /// belonged to the previous process — and still offered jumps to them.
    ///
    /// Every clear here is CLIENT-SIDE ONLY (the pipe is already gone), and the ones
    /// that hold live DLL state say so: ValueSearch forgets its two scan sessions
    /// without the End_* pipe call; PropertySearch drops the forced-fields mirror
    /// without a reset-holds call; LiveWalker stops its auto-refresh timer. Panels
    /// that legitimately PERSIST are deliberately excluded: Teleport / DumpExplorer
    /// (handled via SetConnected(false) above), LiveFuncs (ResetOnDisconnect above),
    /// ProxyDeploy (disk/OS state, connection-independent), and the disk-backed
    /// snapshot corpora in Snapshot / Spc / Pivot (only their live session id /
    /// DataTable pick / auto-loop is reset, never the saved rows). Runs on the UI thread.
    /// </summary>
    private void ResetPanelsOnDisconnect()
    {
        try
        {
            ObjectTree.ClearOnDisconnect();
            ClassStruct.ClearOnDisconnect();
            Pointers.ClearOnDisconnect();
            LiveWalker.ClearOnDisconnect();
            InstanceFinder.ClearOnDisconnect();
            PropertySearch.ClearOnDisconnect();
            GameClassFilter.ClearOnDisconnect();
            InterestingFunctions.ClearOnDisconnect();
            InterestingProperties.ClearOnDisconnect();
            ValueSearch.ClearOnDisconnect();
            RelatedObjects.ClearOnDisconnect();
            DetectStats.ClearOnDisconnect();
            Console.ClearOnDisconnect();
            Snapshot?.ClearOnDisconnect();
            Spc?.ClearOnDisconnect();
            Pivot?.ClearOnDisconnect();
        }
        catch (Exception ex)
        {
            // A panel-clear must never break the disconnect path.
            _log.Error("ResetPanelsOnDisconnect failed", ex);
        }
    }

    /// <summary>
    /// Apply a fully-scanned engine state to all child ViewModels.
    /// Shared between ConnectAsync (normal mode) and TriggerScanAsync (proxy mode).
    /// </summary>
    private void ApplyEngineState(EngineState state)
    {
        // Fresh session (connect / proxy trigger_scan): blank the Extra-Scan strip so
        // no result from the previous game survives. Update() no longer does this —
        // it is also the plain pointer refresh, and doing it there wiped the scan's
        // own result seconds after the user saw it (audit #5 V10).
        Pointers.ResetScanState();
        Pointers.Update(state);

        ObjectTree.SetEngineState(state);
        LiveWalker.SetEngineState(state);
        // Load this game's persisted bookmarks (SetEngineState above captured the PE
        // hash). Self-clears in-memory first, so it's safe on both connect and a
        // game-change re-scan. Synchronous (tiny file).
        LiveWalker.LoadBookmarksForGame(state.PeHash);
        InstanceFinder.SetEngineState(state);
        ValueSearch.SetEngineState(state);
        Teleport.SetConnected(true);   // refresh markers once the DLL is scanned
        Teleport.SetEngineState(state);
        Teleport.LoadCoordLibraryForGame(state.ModuleName);
        Snapshot?.SetEngineState(state);
        Spc?.SetEngineState(state);
        Pivot?.SetEngineState(state);

        // Fire-and-forget: check AOBMaker availability for Live Walker + Teleport
        _ = LiveWalker.CheckAobMakerAsync();
        _ = Teleport.CheckAobMakerAsync();

        // Fire-and-forget: persist AOB usage data (failure must not block UI)
        if (_aobUsage != null)
            _ = _aobUsage.RecordScanAsync(state);

        // Phase 2 LKG: if the DLL reports a PROXY loaded this game, remember it as
        // confirmed-working — but only after the session proves stable (guards a
        // proxy that loads + connects then crashes the game seconds into play).
        ScheduleProxyConfirmation(state);

        // Warn when more than one process has the dumper loaded. This is where every
        // other post-connect action lives, and both ConnectAsync and the proxy
        // TriggerScanAsync funnel through here — the check used to run ONLY from the
        // Pointers.RescanApplied lambda, i.e. only after a UE-override apply or an Extra
        // Scan, so an ordinary Connect never raised it. The pipe name is shared, so
        // Connect lands on whichever server is free: the tree fills with the WRONG
        // GAME'S data and nothing else on screen reveals it. ADDED here rather than
        // moved — RescanApplied is a duplicated hand-rolled fan-out that never reaches
        // this method, so moving the call would delete the one path that already worked.
        // The check is idempotent. (B9)
        _ = CheckForCompetingDumperHostsAsync(state);

        StatusText = $"Connected — UE{state.UEVersion} ({state.ObjectCount} objects)";

        if (!string.IsNullOrEmpty(state.ModuleName))
        {
            WindowTitle = $"UE5 Dump UI — {state.ModuleName}";
            _log.StartProcessMirror(state.ModuleName);
        }

        _log.Info(Constants.LogCatInit, $"Connected: UE{state.UEVersion}, {state.ObjectCount} objects, module={state.ModuleName}");

        // Auto-load objects
        _ = ObjectTree.LoadCommand.ExecuteAsync(null);
    }

    // Stability dwell before a proxy load is recorded as confirmed-working: long
    // enough that a proxy which loads + connects then crashes the game is NOT
    // recorded, short enough to fire in a normal session.
    private const int ProxyConfirmDwellMs = 20_000;
    private Timer? _proxyConfirmTimer;

    // Cancels connection-scoped long-running work (the streaming exports) when the
    // pipe drops mid-operation. Each export links its own CTS to this token, so a
    // disconnect aborts the export's dead-pipe round-trips instead of hammering a
    // gone game — and its services' ct checks stop being dead code (X6). Recreated
    // on connect, cancelled on disconnect. ONLY the exports use it, so cancelling
    // it can't cancel anything unrelated.
    private CancellationTokenSource _connectionCts = new();
    // Bumped on every disconnect. A proxy-confirm dwell captures the epoch when it is
    // scheduled and only records if the epoch is unchanged when it fires — so a proxy
    // that crashed its game (disconnect) can't be recorded against a later reconnect,
    // even to a different game. (M8)
    private int _sessionEpoch;

    /// <summary>The LKG proxy-confirm gate: record the dwelled proxy ONLY if the same
    /// connection is still up (isConnected) AND no disconnect happened since it was
    /// scheduled (scheduledEpoch == currentEpoch). Pure so the gate is unit-testable.</summary>
    internal static bool ShouldConfirmProxy(bool isConnected, int scheduledEpoch, int currentEpoch)
        => isConnected && scheduledEpoch == currentEpoch;

    /// <summary>
    /// Phase 2 LKG stability gate. When the DLL self-reports that a PROXY loaded
    /// this game (<see cref="EngineState.LoadMode"/> = "proxy:&lt;dll&gt;"), wait a
    /// short dwell and — if the session is still connected — record it as the game's
    /// confirmed-working proxy (keyed by the game .exe name so it survives reinstall).
    /// The dwell guards a proxy that loads + connects then crashes the game: the
    /// connect alone is not proof the game keeps running. Non-proxy load modes
    /// (injected / CE .CT / older DLLs with no load_mode) are ignored here.
    /// </summary>
    private void ScheduleProxyConfirmation(EngineState state)
    {
        // Cancel any dwell still pending from a prior connection BEFORE the early returns,
        // so a non-proxy reconnect can't leave a stale timer alive to record the previous
        // session's proxy. (M8)
        _proxyConfirmTimer?.Dispose();
        _proxyConfirmTimer = null;

        if (ProxyDeploy is null) return;

        const string prefix = "proxy:";
        string mode = state.LoadMode ?? "";
        if (!mode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;

        string proxyDll = mode[prefix.Length..];   // "version.dll" / "dinput8.dll" / "dxgi.dll"
        string exeName = state.ModuleName;          // bare game .exe file name
        if (string.IsNullOrEmpty(proxyDll) || string.IsNullOrEmpty(exeName)) return;

        int epoch = _sessionEpoch;   // the connection that scheduled this dwell
        _proxyConfirmTimer = new Timer(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // Record ONLY if the SAME connection is still up after the dwell. A crash
                // (or any disconnect) bumps _sessionEpoch, so a proxy that loaded +
                // connected then crashed the game — even if the user reconnects to a
                // DIFFERENT game within the dwell — is never recorded as working. (M8)
                if (ShouldConfirmProxy(IsConnected, epoch, _sessionEpoch))
                    ProxyDeploy?.RecordConfirmedProxy(exeName, proxyDll);
            }),
            null, ProxyConfirmDwellMs, Timeout.Infinite);
    }

    /// <summary>
    /// Trigger AOB scan from the UI. Used in proxy DLL mode where the DLL starts
    /// the pipe server without scanning. The user clicks "Start Scan" after the
    /// game has loaded a save / reached the main world.
    /// </summary>
    [RelayCommand]
    private async Task TriggerScanAsync()
    {
        try
        {
            ClearError();
            IsScanning = true;
            StatusText = "Starting scan...";

            // trigger_scan now returns immediately — scan runs in background
            await _dump.TriggerScanAsync();

            // Poll scan_status every 500ms until complete
            while (true)
            {
                await Task.Delay(500);

                var status = await _dump.GetScanStatusAsync();
                StatusText = $"Scanning... {status.StatusText}";

                if (!status.Running)
                {
                    if (status.Phase >= 7 && status.EngineState != null)
                    {
                        _engineState = status.EngineState;
                        NeedsScan = false;
                        IsScanning = false;
                        ApplyEngineState(status.EngineState);
                        return;
                    }
                    // The scan stopped without reaching a complete engine state —
                    // surface a failure instead of polling scan_status forever.
                    IsScanning = false;
                    StatusText = "Scan did not complete";
                    SetError(new InvalidOperationException(
                        $"Scan ended at phase {status.Phase} without an engine state"));
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            IsScanning = false;
            StatusText = "Scan failed";
            SetError(ex);
            _log.Error(Constants.LogCatInit, "TriggerScan failed", ex);
        }
    }

    // --- Export Commands ---

    [RelayCommand]
    private async Task ExportSymbolsX64dbgAsync()
    {
        await ExportSymbolsAsync("x64dbg Database (*.dd64)", ".dd64",
            (symbols, moduleName) => SymbolExportService.GenerateX64dbgDatabase(symbols, moduleName));
    }

    [RelayCommand]
    private async Task ExportSymbolsGhidraAsync()
    {
        await ExportSymbolsAsync("Ghidra Symbols (*.txt)", ".txt",
            (symbols, _) => SymbolExportService.GenerateGhidraSymbols(symbols));
    }

    [RelayCommand]
    private async Task ExportSymbolsIdaAsync()
    {
        await ExportSymbolsAsync("IDA Script (*.idc)", ".idc",
            (symbols, _) => SymbolExportService.GenerateIdaScript(symbols));
    }

    /// <summary>
    /// Tools menu: stream the embedded <c>ue5_invoke_helper.lua</c> to a
    /// user-chosen file. The helper is required at runtime by every
    /// "Copy AA Script (Baked)" output -- once per .CT the user picks
    /// Tools -> Export CE Helper Lua File... here, then drags the file
    /// into their table via Cheat Engine's Table -> Add File...
    /// menu. Doesn't need an active DLL connection.
    /// </summary>
    [RelayCommand]
    private async Task ExportCeHelperLuaAsync()
    {
        try
        {
            var savePath = await _platform.ShowSaveFileDialogAsync(
                defaultFileName:  HelperLuaResource.DefaultFileName,
                filterName:       "CE Lua Helper (*.lua)",
                filterExtension:  ".lua");
            if (string.IsNullOrEmpty(savePath))
            {
                _log.Info("Export CE Helper Lua: user cancelled");
                return;
            }

            var content = HelperLuaResource.Read();
            await File.WriteAllTextAsync(savePath, content);

            _log.Info($"Exported CE helper lua: {savePath} " +
                      $"({content.Length:N0} chars)");
            StatusText = $"CE helper exported: {Path.GetFileName(savePath)}";
        }
        catch (Exception ex)
        {
            _log.Error("Export CE Helper Lua failed", ex);
            StatusText = $"Export CE helper failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Resolve a class NAME to its UClass address for the cross-panel handoffs.
    ///
    /// <paramref name="rowClassAddr"/> is the address the raising panel already holds
    /// (Interesting Funcs and Console both build their rows from
    /// <c>list_all_functions</c>, which carries <c>class_addr</c> per entry). When it is
    /// present this returns it unchanged and issues NO pipe call at all.
    ///
    /// The fallback path exists for callers with no address, and it is the reason this
    /// helper exists: <c>list_classes</c> returns at most <c>limit</c> rows and the DLL
    /// stops walking GObjects the moment it has them, so on a large title a real class is
    /// simply not in the page. Every one of these handlers used to re-derive the address
    /// from that page and abort with "Class X not found" — about the class whose own row
    /// the user had just clicked (audit #5 X2). A miss now says WHICH kind of miss it was.
    /// </summary>
    /// <returns>The class address, or "" when it could not be resolved (StatusText set).</returns>
    private async Task<string> ResolveClassAddrAsync(string className, string rowClassAddr = "")
    {
        if (!string.IsNullOrEmpty(rowClassAddr)) return rowClassAddr;

        var classes = await _dump.ListClassesAsync(gameOnly: false);
        var lookup = classes.FindClassAddr(className);
        if (lookup.Found) return lookup.Addr;

        StatusText = $"Class {className} {lookup.MissReason}";
        _log.Warn($"ResolveClassAddr: {className} {lookup.MissReason} " +
                  $"(list_classes returned {classes.Classes.Count} rows, truncated={lookup.Truncated})");
        return "";
    }

    /// <summary>
    /// Shared CT save handler — opens the platform save-file dialog with
    /// the VM-supplied default filename + .CT filter, writes the XML
    /// payload (UTF-8, no BOM — CE's loader handles either but the
    /// existing UE5CEDumper.CT ships without BOM so we stay consistent),
    /// and surfaces success/error in the top status bar.
    ///
    /// <paramref name="source"/> is a short label for the log entry
    /// (e.g. "InterestingProperties" / "InterestingFunctions") so a
    /// later grep through the user's logs can identify which tab
    /// generated which file.
    /// </summary>
    private async Task SaveCheatTableAsync(
        string defaultFileName, string ctXml, string source)
    {
        try
        {
            var savePath = await _platform.ShowSaveFileDialogAsync(
                defaultFileName: defaultFileName,
                filterName:      "Cheat Engine Table (*.CT)",
                filterExtension: ".CT");
            if (string.IsNullOrEmpty(savePath))
            {
                _log.Info($"Save Cheat Table ({source}): user cancelled");
                return;
            }
            // Avalonia's open-file dialog returns the chosen filter's
            // extension as a hint; some platforms append it twice if
            // the user typed an extension. Strip any duplicate.
            await File.WriteAllTextAsync(savePath, ctXml,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _log.Info($"Saved Cheat Table ({source}): {savePath} " +
                      $"({ctXml.Length:N0} chars)");
            StatusText = $"Saved: {Path.GetFileName(savePath)}";
        }
        catch (Exception ex)
        {
            _log.Error($"Save Cheat Table ({source}) failed", ex);
            StatusText = $"Save Cheat Table failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Top-toolbar "&#x27F3;" button: re-probe whether Cheat Engine is running with
    /// the AOBMaker plugin loaded and update the always-visible status chip. Navigation
    /// and Add-to-CE actions already re-probe on use, so this is purely for at-a-glance
    /// feedback. Propagates the result to the Live Walker and Pointers panels so all
    /// three indicators (and their button enablement) agree.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAobMakerAsync()
    {
        if (_aobMaker == null)
        {
            IsAobMakerAvailable = false;
            return;
        }

        try
        {
            var ok = await _aobMaker.CheckAvailabilityAsync();
            IsAobMakerAvailable = ok;
            LiveWalker.IsAobMakerAvailable = ok;
            Pointers.IsAobMakerAvailable = ok;
            StatusText = ok
                ? "AOBMaker plugin connected"
                : "AOBMaker plugin not detected — open Cheat Engine with the AOBMaker plugin loaded";
        }
        catch (Exception ex)
        {
            IsAobMakerAvailable = false;
            _log.Error("Refresh AOBMaker status failed", ex);
        }
    }

    /// <summary>
    /// Tools menu: ship the embedded <c>ue5_invoke_helper.lua</c> straight
    /// into the currently open CE table via the AOBMaker plugin pipe
    /// (<c>InjectTableFile</c>). Replaces the manual save-to-disk +
    /// <c>Table -&gt; Add File...</c> dance.
    /// Probes <see cref="IAobMakerBridge.IsAvailable"/> first via
    /// <see cref="IAobMakerBridge.CheckAvailabilityAsync"/> so a stale
    /// availability flag (CE closed since the last check) doesn't fire
    /// off a guaranteed-to-fail pipe round-trip.
    /// </summary>
    [RelayCommand]
    private async Task InjectCeHelperLuaAsync()
    {
        if (_aobMaker == null)
        {
            StatusText = "AOBMaker plugin not configured";
            return;
        }

        // Show an in-flight status so successive clicks can be told apart
        // even when both end in the same outcome — without this the user
        // sees the previous run's text frozen on screen until the new
        // run finishes, which reads as "the click did nothing".
        StatusText = $"Injecting {HelperLuaResource.DefaultFileName} into CE table...";

        try
        {
            await _aobMaker.CheckAvailabilityAsync();
            if (!_aobMaker.IsAvailable)
            {
                StatusText = "Inject helper: AOBMaker not connected — open Cheat Engine with the AOBMaker plugin loaded";
                return;
            }

            var content = HelperLuaResource.Read();
            var (ok, error) = await _aobMaker.InjectTableFileAsync(
                HelperLuaResource.DefaultFileName, content);

            if (ok)
            {
                _log.Info($"Injected {HelperLuaResource.DefaultFileName} into CE table " +
                          $"({content.Length:N0} chars)");
                StatusText = $"Inject helper OK: {HelperLuaResource.DefaultFileName} embedded ({content.Length:N0} bytes)";
            }
            else if (!string.IsNullOrEmpty(error))
            {
                StatusText = $"Inject helper failed: {error} — use Export to disk + Add File... fallback";
            }
            else
            {
                StatusText = "Inject helper failed (no plugin response — CE closed?) — use Export to disk + Add File... fallback";
            }
        }
        catch (Exception ex)
        {
            _log.Error("Inject CE Helper Lua failed", ex);
            StatusText = $"Inject helper failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Tools menu: push the DLL-injection bootstrap into the CE table the user
    /// ALREADY has open, as an [ENABLE]/[DISABLE] memory record
    /// (<see cref="Services.CeInjectScriptGenerator"/>), so they never have to
    /// open the standalone <c>UE5CEDumper.CT</c> first. Cheat Engine holds one
    /// table at a time, which is what made the <c>.CT</c> route a two-stage load;
    /// the <c>.CT</c> stays shipped as the developer / no-AOBMaker path.
    ///
    /// Falls back to CE record XML on the clipboard when the AOBMaker plugin
    /// isn't reachable — the same pattern the Teleport / invoke pushes use.
    /// </summary>
    [RelayCommand]
    private async Task InjectCeBootstrapAsync()
    {
        // The injectable DLL sits next to the UI exe (dist\UE5Dumper.dll) — the
        // same resolution ProxyDeployViewModel.InjectIntoRunningGameAsync uses.
        // Resolve it HERE rather than in the generator so a missing file is a
        // clear error instead of a script that fails inside Cheat Engine.
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var dllPath = Path.Combine(exeDir, "UE5Dumper.dll");
        if (!File.Exists(dllPath))
        {
            StatusText = $"Inject bootstrap: UE5Dumper.dll not found next to the app ({dllPath})";
            return;
        }

        StatusText = "Building CE inject bootstrap...";

        try
        {
            var script = Services.CeInjectScriptGenerator.Generate(dllPath);
            var description = Services.CeInjectScriptGenerator.RecordDescription;

            // Sample availability before sending so "pipe broke mid-send" reads
            // differently from "CE was never running".
            if (_aobMaker != null)
                await _aobMaker.CheckAvailabilityAsync();
            bool wasAvailable = _aobMaker?.IsAvailable ?? false;

            bool sentToCe = false;
            if (_aobMaker != null && wasAvailable)
            {
                // autoActivate stays false: this injects a DLL, so the user ticks
                // it deliberately.
                sentToCe = await _aobMaker.CreateAAScriptAsync(
                    description, script, autoActivate: false,
                    group: Services.CeInjectScriptGenerator.RecordGroup);
            }

            if (!sentToCe)
            {
                // A bare AA body can't be pasted into a record — wrap as CE
                // memory-record XML.
                await _platform.CopyToClipboardAsync(
                    Services.CheatTableBuilder.WrapAaScriptXml(description, script));
            }

            StatusText = sentToCe
                ? "Inject bootstrap added to the current CE table — tick it to inject"
                : wasAvailable
                    ? "⚠ AOBMaker pipe broke (CE closed?) — bootstrap copied as CE XML, paste into your address list"
                    : "AOBMaker not connected — bootstrap copied as CE XML, paste into your address list";
            _log.Info($"CE inject bootstrap {(sentToCe ? "sent to CE" : "to clipboard")} " +
                      $"(dll={dllPath}, wasAvailable={wasAvailable})");
        }
        catch (Exception ex)
        {
            _log.Error("Inject CE bootstrap failed", ex);
            StatusText = $"Inject bootstrap failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Locate a Cheat Engine installation from a RUNNING CE process. Deliberately
    /// not the registry: that would need a new platform-abstraction surface, and a
    /// running CE is both the common case (the user is about to use it) and the
    /// authoritative answer for WHICH install of several is in play.
    /// Returns the install directory, or null when CE isn't running.
    /// </summary>
    private async Task<string?> TryFindCheatEngineDirAsync()
    {
        if (ProxyDeploy == null) return null;
        try
        {
            // showAll: CE is not a UE game, so the UE-only filter would hide it.
            var procs = await ProxyDeploy.ListGameProcessesAsync(showAll: true);
            foreach (var p in procs)
            {
                if (string.IsNullOrEmpty(p.Path)) continue;
                var name = Path.GetFileNameWithoutExtension(p.Path);
                // cheatengine-x86_64.exe / cheatengine-i386.exe / "Cheat Engine.exe"
                if (name.Replace(" ", "").StartsWith("cheatengine", StringComparison.OrdinalIgnoreCase))
                    return Path.GetDirectoryName(p.Path);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Locate Cheat Engine failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Tools menu: install the autorun helper into Cheat Engine's <c>autorun\</c>
    /// folder (<see cref="Services.CeAutorunScriptGenerator"/>). CE runs that folder
    /// at start-up, so <c>ue5_inject()</c> then exists in EVERY table permanently —
    /// the only delivery route needing neither the standalone <c>.CT</c> nor the
    /// AOBMaker plugin.
    ///
    /// Writes straight into a running CE's install when we can find one; otherwise
    /// falls back to the save dialog so the user can place it by hand.
    /// </summary>
    [RelayCommand]
    private async Task InstallCeAutorunAsync()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var dllPath = Path.Combine(exeDir, "UE5Dumper.dll");
        if (!File.Exists(dllPath))
        {
            StatusText = $"Install CE autorun: UE5Dumper.dll not found next to the app ({dllPath})";
            return;
        }

        StatusText = "Locating Cheat Engine...";

        try
        {
            var content = Services.CeAutorunScriptGenerator.Generate(dllPath);
            var fileName = Services.CeAutorunScriptGenerator.DefaultFileName;

            var ceDir = await TryFindCheatEngineDirAsync();
            string? target = null;
            bool autoLocated = false;

            if (ceDir != null)
            {
                var autorunDir = Path.Combine(
                    ceDir, Services.CeAutorunScriptGenerator.AutorunFolderName);
                try
                {
                    // CE ships the folder, but a portable/trimmed copy may not have it —
                    // creating it is safe and is what CE itself expects to find.
                    Directory.CreateDirectory(autorunDir);
                    var autoTarget = Path.Combine(autorunDir, fileName);
                    await File.WriteAllTextAsync(autoTarget, content);
                    target = autoTarget;
                    autoLocated = true;
                }
                catch (Exception ex) when (Helpers.FileWriteFault.IsPlacementDenied(ex))
                {
                    // CE lives under %ProgramFiles% for most installs, so the auto-place
                    // needs elevation and throws here. That is exactly when the manual
                    // fallback is needed — the old code skipped it and reported failure (X12).
                    _log.Warn(Constants.LogCatInit,
                        $"CE autorun auto-place denied ({ex.Message}); falling back to manual save dialog");
                }
            }

            if (!autoLocated)
            {
                // No running CE to point at, or its folder was not writable — let the
                // user place the file. The dialog default name matches what CE expects.
                StatusText = ceDir == null
                    ? "Cheat Engine not running — choose its autorun folder..."
                    : "Cheat Engine's autorun folder is not writable — choose where to place it...";
                target = await _platform.ShowSaveFileDialogAsync(
                    defaultFileName: fileName,
                    filterName: "CE autorun Lua (*.lua)",
                    filterExtension: ".lua");
                if (string.IsNullOrEmpty(target))
                {
                    StatusText = "Install CE autorun: cancelled";
                    return;
                }
                await File.WriteAllTextAsync(target, content);
            }

            _log.Info($"Installed CE autorun helper: {target} ({content.Length:N0} chars, " +
                      $"dll={dllPath}, autoLocated={autoLocated})");

            // Whether auto-located or hand-placed, the file only takes effect on the
            // NEXT CE start — say so, or the user will click and see nothing happen.
            var placed = Path.GetDirectoryName(target) ?? target;
            var looksRight = string.Equals(
                Path.GetFileName(placed),
                Services.CeAutorunScriptGenerator.AutorunFolderName,
                StringComparison.OrdinalIgnoreCase);
            StatusText = looksRight
                ? $"CE autorun helper installed to {placed} — restart Cheat Engine, then use ue5_inject()"
                : $"⚠ Written to {placed}, which is not an 'autorun' folder — CE only runs files inside <CheatEngine>\\autorun\\";
        }
        catch (Exception ex)
        {
            _log.Error("Install CE autorun failed", ex);
            StatusText = $"Install CE autorun failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Tools menu: stream the embedded <c>ue5_freeze_helper.lua</c> to a
    /// user-chosen file. Manual-fallback companion to
    /// <see cref="InjectFreezeHelperLuaAsync"/> for cases where AOBMaker
    /// isn't installed.
    /// </summary>
    [RelayCommand]
    private async Task ExportFreezeHelperLuaAsync()
    {
        try
        {
            var savePath = await _platform.ShowSaveFileDialogAsync(
                defaultFileName:  FreezeHelperLuaResource.DefaultFileName,
                filterName:       "CE Lua Freeze Helper (*.lua)",
                filterExtension:  ".lua");
            if (string.IsNullOrEmpty(savePath))
            {
                _log.Info("Export Freeze Helper Lua: user cancelled");
                return;
            }

            var content = FreezeHelperLuaResource.Read();
            await File.WriteAllTextAsync(savePath, content);

            _log.Info($"Exported freeze helper lua: {savePath} " +
                      $"({content.Length:N0} chars)");
            StatusText = $"Freeze helper exported: {Path.GetFileName(savePath)}";
        }
        catch (Exception ex)
        {
            _log.Error("Export Freeze Helper Lua failed", ex);
            StatusText = $"Export freeze helper failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Tools menu: ship the embedded <c>ue5_freeze_helper.lua</c> straight
    /// into the currently open CE table via the AOBMaker plugin
    /// (<c>InjectTableFile</c>). Sister to <see cref="InjectCeHelperLuaAsync"/>;
    /// the two helpers coexist in one .CT.
    /// </summary>
    [RelayCommand]
    private async Task InjectFreezeHelperLuaAsync()
    {
        if (_aobMaker == null)
        {
            StatusText = "AOBMaker plugin not configured";
            return;
        }

        StatusText = $"Injecting {FreezeHelperLuaResource.DefaultFileName} into CE table...";

        try
        {
            await _aobMaker.CheckAvailabilityAsync();
            if (!_aobMaker.IsAvailable)
            {
                StatusText = "Inject freeze helper: AOBMaker not connected — open Cheat Engine with the AOBMaker plugin loaded";
                return;
            }

            var content = FreezeHelperLuaResource.Read();
            var (ok, error) = await _aobMaker.InjectTableFileAsync(
                FreezeHelperLuaResource.DefaultFileName, content);

            if (ok)
            {
                _log.Info($"Injected {FreezeHelperLuaResource.DefaultFileName} into CE table " +
                          $"({content.Length:N0} chars)");
                StatusText = $"Inject freeze helper OK: {FreezeHelperLuaResource.DefaultFileName} embedded ({content.Length:N0} bytes)";
            }
            else if (!string.IsNullOrEmpty(error))
            {
                StatusText = $"Inject freeze helper failed: {error} — use Export to disk + Add File... fallback";
            }
            else
            {
                StatusText = "Inject freeze helper failed (no plugin response — CE closed?) — use Export to disk + Add File... fallback";
            }
        }
        catch (Exception ex)
        {
            _log.Error("Inject Freeze Helper Lua failed", ex);
            StatusText = $"Inject freeze helper failed: {ex.Message}";
        }
    }

    private async Task ExportSymbolsAsync(
        string filterName, string filterExtension,
        Func<IReadOnlyList<SymbolEntry>, string, string> generator)
    {
        if (_engineState == null) return;

        try
        {
            ClearError();
            var moduleName = _engineState.ModuleName;
            if (string.IsNullOrEmpty(moduleName)) moduleName = "game.exe";
            var safeModule = Path.GetFileNameWithoutExtension(moduleName);

            var filePath = await _platform.ShowSaveFileDialogAsync(
                $"{safeModule}_symbols", filterName, filterExtension);
            if (string.IsNullOrEmpty(filePath)) return;

            StatusText = "Collecting symbols...";

            var progress = new Progress<string>(msg =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText = msg));

            var symbols = await SymbolExportService.CollectSymbolsAsync(
                _dump, moduleName, _engineState.ModuleBase, progress);

            StatusText = "Writing file...";
            var content = generator(symbols, moduleName);
            await File.WriteAllTextAsync(filePath, content);

            StatusText = $"Exported {symbols.Count} symbols";
            _log.Info($"Symbols exported to {filePath} ({symbols.Count} entries)");
        }
        catch (Exception ex)
        {
            StatusText = "Export failed";
            SetError(ex);
            _log.Error("Symbol export failed", ex);
        }
    }

    [RelayCommand]
    private async Task ExportFullSdkAsync()
    {
        if (_engineState == null) return;

        try
        {
            ClearError();
            var moduleName = _engineState.ModuleName;
            if (string.IsNullOrEmpty(moduleName)) moduleName = "game";
            var safeModule = Path.GetFileNameWithoutExtension(moduleName);

            var filePath = await _platform.ShowSaveFileDialogAsync(
                $"{safeModule}_SDK", "C++ Header (*.h)", ".h");
            if (string.IsNullOrEmpty(filePath)) return;

            StatusText = "Generating SDK...";
            var progress = new Progress<string>(msg =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText = msg));

            // Cancellation linked to the connection so a mid-export disconnect aborts
            // the service's per-class walk (its ct checks were dead code before) (X6).
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token);
            var content = await SdkExportService.GenerateFullSdkAsync(_dump, progress, cts.Token);
            await File.WriteAllTextAsync(filePath, content, cts.Token);

            StatusText = "SDK exported";
            _log.Info($"Full SDK exported to {filePath}");
        }
        catch (OperationCanceledException)
        {
            StatusText = "SDK export cancelled (disconnected)";
            _log.Info("Full SDK export cancelled");
        }
        catch (Exception ex)
        {
            StatusText = "Export failed";
            SetError(ex);
            _log.Error("Full SDK export failed", ex);
        }
    }

    /// <summary>
    /// Stream a full classes-and-properties dump to a JSON-Lines file
    /// for offline analysis. Used to feed the
    /// <c>scripts/analysis/analyze_dumps.py</c> aggregator that derives
    /// keyword tables / class bonuses from real-game data instead of
    /// hand-curated guesses.
    /// </summary>
    [RelayCommand]
    private async Task ExportDumpAllAsync()
    {
        if (_engineState == null) return;

        // Temp-then-rename: the dump streams to <file>.partial and is published to
        // the chosen name only after GenerateAsync returns (which happens only after
        // the trailing summary line is written), so an abort/disconnect/crash never
        // leaves a truncated .jsonl at the final name (X11).
        string? tempPath = null;
        try
        {
            ClearError();
            var moduleName = _engineState.ModuleName;
            if (string.IsNullOrEmpty(moduleName)) moduleName = "game";
            var safeModule = Path.GetFileNameWithoutExtension(moduleName);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

            var filePath = await _platform.ShowSaveFileDialogAsync(
                $"{safeModule}-dump-{stamp}", "Dump JSON Lines (*.jsonl)", ".jsonl");
            if (string.IsNullOrEmpty(filePath)) return;

            StatusText = "Dumping classes...";
            var progress = new Progress<DumpProgress>(p =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    StatusText = p.Total > 0
                        ? $"{p.Phase} ({p.Done}/{p.Total})"
                        : $"{p.Phase} ({p.Done})";
                }));

            var options = new DumpOptions(
                GameOnly: false,                           // Capture engine too; analysis can filter
                IncludeFunctions: true,
                IncludeInstanceCounts: true,
                DumperBuildNumber: GetBuildNumber(),
                DumperCommit: null);                        // Not yet plumbed through

            // Cancellation linked to the connection so a mid-dump disconnect aborts
            // the (now dead) per-class round-trips instead of hanging (X6).
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token);
            var ct = cts.Token;

            tempPath = filePath + ".partial";
            DumpResult result;
            await using (var fs = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024, useAsync: true))
            {
                result = await DumpAllService.GenerateAsync(_dump, _engineState, fs, options, progress, ct);
            }   // fs flushed + closed here, so File.Move below can take the file

            File.Move(tempPath, filePath, overwrite: true);
            tempPath = null;   // published — don't delete on a later throw

            var byteLength = new FileInfo(filePath).Length;
            // Report from what the dump ACTUALLY produced (class/error counts), not
            // from the file's byte length, and format the size in floating point (X4).
            StatusText = Helpers.DumpCompletionFormatter.Format(
                result, byteLength, Path.GetFileName(filePath));
            _log.Info($"DumpAll exported to {filePath} ({byteLength} bytes, " +
                      $"{result.ClassesEmitted} classes, {result.Errors} errors)");

            // Offer a one-click load in the Dump Explorer tab (no auto-load — the
            // user may re-export or be mid-operation there).
            DumpExplorer.SetLastExportPath(filePath);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Dump cancelled (disconnected)";
            _log.Info("DumpAll export cancelled");
            TryDeletePartial(tempPath);
        }
        catch (Exception ex)
        {
            StatusText = "Dump failed";
            SetError(ex);
            _log.Error("DumpAll export failed", ex);
            TryDeletePartial(tempPath);
        }
    }

    /// <summary>Best-effort cleanup of an unpublished temp dump file (X11).</summary>
    private static void TryDeletePartial(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { /* leftover .partial is harmless */ }
    }

    /// <summary>
    /// Same EntryAssembly + Version.Revision trick the System tab uses
    /// (see PointerPanelViewModel.ReadUiBuildNumber for the two-trap
    /// rationale).
    /// </summary>
    private static int GetBuildNumber()
    {
        var rev = Assembly.GetEntryAssembly()?.GetName().Version?.Revision ?? 0;
        return rev > 0 ? rev : 0;
    }

    [RelayCommand]
    private async Task ExportUsmapAsync()
    {
        if (_engineState == null) return;

        try
        {
            ClearError();
            var moduleName = _engineState.ModuleName;
            if (string.IsNullOrEmpty(moduleName)) moduleName = "game";
            var safeModule = Path.GetFileNameWithoutExtension(moduleName);

            var filePath = await _platform.ShowSaveFileDialogAsync(
                $"{safeModule}", "USMAP (*.usmap)", ".usmap");
            if (string.IsNullOrEmpty(filePath)) return;

            StatusText = "Generating USMAP...";
            var progress = new Progress<string>(msg =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText = msg));

            // Cancellation linked to the connection so a mid-export disconnect aborts
            // the service's walk (its ct checks were dead code before) (X6).
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token);
            var bytes = await UsmapExportService.GenerateUsmapAsync(_dump, progress, cts.Token);
            await File.WriteAllBytesAsync(filePath, bytes, cts.Token);

            StatusText = "USMAP exported";
            _log.Info($"USMAP exported to {filePath} ({bytes.Length} bytes)");
        }
        catch (OperationCanceledException)
        {
            StatusText = "USMAP export cancelled (disconnected)";
            _log.Info("USMAP export cancelled");
        }
        catch (Exception ex)
        {
            StatusText = "Export failed";
            SetError(ex);
            _log.Error("USMAP export failed", ex);
        }
    }
}
