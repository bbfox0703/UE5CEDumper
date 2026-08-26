using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Live Data Walker panel.
/// Browse GWorld hierarchy and navigate into any UObject by clicking pointers.
/// </summary>
public partial class LiveWalkerViewModel : ViewModelBase, IDisposable
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService _platform;
    private readonly IAobMakerBridge? _aobMaker;
    private bool _disposed;

    // Cached GWorld walk result for back-navigation
    private WorldWalkResult? _cachedWorld;

    // Engine state for CE address formatting
    private EngineState? _engineState;

    // Cancels an in-flight Copy CE XML / Copy CE Field export (the ResolveDrilldown
    // pipe-walk phase, which can be long for deep/wide object graphs). Recreated per
    // export; the Cancel button binds to CancelExportCommand.
    private CancellationTokenSource? _exportCts;

    // Navigation breadcrumb stack
    [ObservableProperty] private ObservableCollection<BreadcrumbItem> _breadcrumbs = new();

    // ── Browser-style forward history ───────────────────────────────────────
    // Breadcrumbs is a PATH, not a history: Back and a breadcrumb jump TRUNCATE
    // it and the removed crumbs were previously dropped, so there was nothing to
    // go forward to. They now land here (newest on top) until the next fresh
    // navigation invalidates them, exactly like a browser drops its forward
    // history when you follow a new link.
    //
    // Everything needed to re-render a level already lives on its BreadcrumbItem
    // (that is how Back re-renders `prev`), so replaying one is just pushing the
    // crumb back and rendering it — no extra state is captured here.
    private readonly Stack<ForwardStep> _forwardStack = new();

    /// <summary>
    /// One step <c>GoForwardAsync</c> can replay. Exactly one field is non-null,
    /// because the walker has TWO kinds of backwards navigation and a bare crumb
    /// can only express one of them:
    ///
    /// <list type="bullet">
    /// <item><b>Crumb</b> — a level Back / a breadcrumb jump TRUNCATED off the spine.
    /// The levels below it are still on screen, so Forward re-APPENDS this one.</item>
    /// <item><b>Spine</b> — a whole spine that a re-rooting navigation REPLACED
    /// (stepping back out of a bookmark restore, or out of an address re-root).
    /// Its crumbs do not attach to what is on screen now, so Forward swaps the
    /// whole list back in.</item>
    /// </list>
    ///
    /// A single stack holds both so one Forward press always means "undo the last
    /// Back", whichever kind it was — which is the only way the two can interleave
    /// correctly (walk out of a bookmark spine with N Backs, step out of the spine,
    /// then Forward N+1 times and arrive exactly where you started).
    /// </summary>
    private readonly record struct ForwardStep(BreadcrumbItem? Crumb, SpineSnapshot? Spine);

    /// <summary>
    /// A whole breadcrumb spine plus the world cache it was rendered against, kept
    /// so a navigation that REPLACED the spine can be undone. The crumb list is a
    /// shallow copy — the same <see cref="BreadcrumbItem"/> objects stay live, which
    /// is what carries each level's captured view state (selection + scroll anchor)
    /// through the round trip.
    /// </summary>
    private sealed record SpineSnapshot(List<BreadcrumbItem> Crumbs, WorldWalkResult? CachedWorld);

    /// <summary>Set while Back / Forward swaps the spine, so the CollectionChanged
    /// invalidation hook doesn't mistake that Clear+Add (or a Forward's re-push) for
    /// a fresh navigation and wipe the very history we're walking.</summary>
    private bool _replayingHistory;

    /// <summary>Drives the Forward button's IsEnabled. Kept as state rather than a
    /// computed property because <see cref="_forwardStack"/> isn't observable.</summary>
    [ObservableProperty] private bool _canGoForward;
    [ObservableProperty] private ObservableCollection<LiveFieldValue> _fields = new();
    [ObservableProperty] private bool _isLoading;

    /// <summary>True while a Copy CE XML / Copy CE Field export is resolving its object
    /// graph — drives the Cancel button's visibility. Distinct from <see cref="IsLoading"/>,
    /// which many navigation flows also raise, so the Cancel button appears only for the
    /// abortable export walk (not for every load).</summary>
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _currentObjectName = "";
    [ObservableProperty] private string _currentClassName = "";
    [ObservableProperty] private string _currentAddress = "";
    [NotifyPropertyChangedFor(nameof(ShowEmptyStateLogo))]
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private LiveFieldValue? _selectedField;

    /// <summary>Prominent failure message shown as a centered warning banner in the
    /// empty grid area — used for "Locate in GWorld" failures (not_reachable,
    /// deadline, …) and locate exceptions. Empty = no banner. This replaces the
    /// misleading idle-logo empty state so a failed locate doesn't look identical to
    /// "nothing loaded yet"; the actionable reason (<see cref="GWorldPathFailureStatus"/>)
    /// is no longer buried in the low-contrast top status line.</summary>
    [NotifyPropertyChangedFor(nameof(HasLocateFailure))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyStateLogo))]
    [ObservableProperty] private string _locateFailureMessage = "";

    /// <summary>The failure banner's title — auto-switches between "Locate in GWorld
    /// failed" and "Locate in GameEngine failed" so the heading matches the root the
    /// user picked (the body already names the root; this keeps them consistent). Set
    /// by each locate method from its rootLabel; only shown while a banner is up.</summary>
    [ObservableProperty] private string _locateFailureTitle = "Locate failed";

    /// <summary>True when a <see cref="LocateFailureMessage"/> is present — drives the
    /// failure banner's visibility.</summary>
    public bool HasLocateFailure => !string.IsNullOrEmpty(LocateFailureMessage);

    /// <summary>Show the idle app-logo empty state ONLY when there is no data AND no
    /// locate failure to report — so the failure banner takes over the empty area
    /// instead of the logo.</summary>
    public bool ShowEmptyStateLogo => !HasData && string.IsNullOrEmpty(LocateFailureMessage);

    /// <summary>Whenever real data is shown (HasData goes true) the failure banner is
    /// retired structurally — independent of which path displayed the data (some nav
    /// paths set HasData directly and bypass UpdateDisplay). The banner is only ever
    /// raised while HasData is false, so this never clobbers a still-relevant banner.</summary>
    partial void OnHasDataChanged(bool value)
    {
        if (value) LocateFailureMessage = "";
        OnPropertyChanged(nameof(ShowBookmarkBar));

        // Data is showing again — the point at which a suspended auto-refresh can
        // meaningfully come back. Hooked HERE rather than in UpdateDisplay for the exact
        // reason this method's own comment gives above: "Start from GWorld" and the four
        // container views set HasData directly and never reach UpdateDisplay, and the
        // GWorld root is the first thing most reconnects do.
        if (value) ResumeAutoRefreshIfPending();
    }

    /// <summary>
    /// The other half of the resume trigger. Paired with <see cref="OnHasDataChanged"/>
    /// so the re-arm is ORDER-INDEPENDENT: the population paths set these two in
    /// different orders and <see cref="AutoRefreshCadence.ShouldResume"/> needs both, so
    /// whichever lands second is the one that fires. Neither alone is sufficient.
    /// </summary>
    partial void OnCurrentAddressChanged(string value)
    {
        if (!string.IsNullOrEmpty(value)) ResumeAutoRefreshIfPending();
    }

    /// <summary>Show the bookmark toolbar when there's a live object OR any saved
    /// bookmark exists — so persisted bookmarks are clickable right after connecting,
    /// before the user has navigated anywhere.</summary>
    public bool ShowBookmarkBar => HasData || AnyBookmarkOccupied;

    // Multi-selection snapshot. Updated by LiveWalkerPanel's SelectionChanged
    // handler whenever the DataGrid's SelectedItems changes. Drives Copy CE
    // Field(s) export — everything else (drill-down, copy buttons, edit) acts
    // on the row whose own button was clicked, so multi-select doesn't affect
    // those flows. SelectedField is still the focus anchor for search /
    // bookmark / scroll-to logic.
    private readonly List<LiveFieldValue> _selectedFieldsSnapshot = new();
    [ObservableProperty] private int _selectedFieldsCount;

    public bool HasSelectedFields => SelectedFieldsCount > 0;

    public string ExportCeFieldButtonLabel => SelectedFieldsCount > 1
        ? $"Copy CE Fields ({SelectedFieldsCount})"
        : "Copy CE Field";

    partial void OnSelectedFieldsCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelectedFields));
        OnPropertyChanged(nameof(ExportCeFieldButtonLabel));
    }

    /// <summary>
    /// Sync the multi-selection snapshot from the DataGrid's SelectedItems
    /// collection. Called by LiveWalkerPanel.FieldGrid_SelectionChanged.
    /// Filters out non-LiveFieldValue entries defensively (Avalonia's
    /// SelectedItems is typed as IList).
    /// </summary>
    public void UpdateSelectedFields(System.Collections.IEnumerable? selectedItems)
    {
        _selectedFieldsSnapshot.Clear();
        if (selectedItems != null)
        {
            foreach (var item in selectedItems)
            {
                if (item is LiveFieldValue f) _selectedFieldsSnapshot.Add(f);
            }
        }
        SelectedFieldsCount = _selectedFieldsSnapshot.Count;
    }

    [ObservableProperty] private string _currentOuterAddr = "";
    [ObservableProperty] private string _currentOuterName = "";
    [ObservableProperty] private string _currentOuterClassName = "";
    [ObservableProperty] private bool _hasParent;
    // UFunction display. _allFunctions holds the unfiltered set received
    // from the DLL; Functions is the user-visible filtered subset rebuilt
    // by ApplyFunctionFilter() whenever the filter text changes. Function
    // counts on UE-derived classes can climb past 200 entries (Character /
    // PlayerController inheritance chains), so the filter is a usability
    // floor — not a perf optimization.
    private readonly List<FunctionInfoModel> _allFunctions = new();
    private Task? _pendingFunctionsLoad;
    [ObservableProperty] private ObservableCollection<FunctionInfoModel> _functions = new();
    [ObservableProperty] private bool _hasFunctions;
    [ObservableProperty] private FunctionInfoModel? _selectedFunction;
    [ObservableProperty] private string _functionFilter = "";

    /// <summary>Per-session remembered Functions-filter keywords (LRU) surfaced as
    /// the filter box's AutoCompleteBox suggestions — see <see cref="KeywordSearchMemory"/>.
    /// Separate from the field-search <see cref="SearchHistory"/> memory.</summary>
    private readonly KeywordSearchMemory _functionFilterMemory;
    public ObservableCollection<string> FunctionFilterHistory => _functionFilterMemory.History;

    /// <summary>
    /// Two-way binding for the Functions Expander. Defaults to collapsed
    /// because most navigation in LiveWalker is field-focused; cross-tab
    /// jumps from Interesting Funcs flip this to true via
    /// <see cref="TrySelectFunctionByName"/> so the user lands with the
    /// target function already visible.
    /// </summary>
    [ObservableProperty] private bool _isFunctionsExpanded;

    partial void OnFunctionFilterChanged(string value)
    {
        ApplyFunctionFilter();
        _functionFilterMemory.Schedule(value);
    }

    /// <summary>Clear the function-filter box. Flushes the keyword memory first — the
    /// sibling of what <see cref="ClearFieldSearchForNavigation"/> already does for the
    /// FIELD search box (<c>FlushPendingSearchKeyword</c>); the function box had the
    /// Schedule half wired and neither flush. (audit #5 AE16)</summary>
    [RelayCommand]
    private void ClearFunctionFilter()
    {
        _functionFilterMemory.Flush();
        FunctionFilter = "";
    }
    private string _currentClassAddr = "";
    private bool _isDefinitionView;  // True when displaying a class/struct definition (no live data)
    private DataTableWalkResult? _cachedDataTableRows;  // Cached DataTable row data

    // CE XML output (kept for possible future use but no longer shown in panel)
    [ObservableProperty] private string _ceXmlOutput = "";
    [ObservableProperty] private bool _showCeXml;

    // Address format
    [ObservableProperty] private int _selectedAddressFormatIndex;
    private AddressFormat AddrFormat => (AddressFormat)SelectedAddressFormatIndex;

    /// <summary>Whether CE XML export should collapse pointer/array nodes.</summary>
    public bool CollapsePointerNodes { get; set; }

    /// <summary>
    /// Whether Copy CE XML / Copy CE Field should collapse the GWorld-&gt;...-&gt;target
    /// pointer spine into a single CE multi-level-pointer entry (base + one folded
    /// node + the target field with its drill-down). LiveWalker-local toggle —
    /// affects only the two clipboard exports, not CSX / .h / AA Script.
    /// </summary>
    [ObservableProperty] private bool _collapseChain;

    /// <summary>Max array element count for inline reading (2^N, default 128).</summary>
    private int _arrayLimit = Constants.DefaultArrayLimit;
    public int ArrayLimit
    {
        get => _arrayLimit;
        set
        {
            if (_arrayLimit == value) return;
            _arrayLimit = value;
            // Auto-refresh current view with new limit
            if (!string.IsNullOrEmpty(CurrentAddress))
                RefreshCommand.Execute(null);
        }
    }

    /// <summary>Max struct sub-fields to show in preview (0 = none, default 2, max 6).</summary>
    private int _previewLimit = Constants.DefaultPreviewLimit;
    public int PreviewLimit
    {
        get => _previewLimit;
        set
        {
            if (_previewLimit == value) return;
            _previewLimit = value;
            // Auto-refresh current view with new limit
            if (!string.IsNullOrEmpty(CurrentAddress))
                RefreshCommand.Execute(null);
        }
    }

    /// <summary>Max CE DropDownList entries (2^N, default 512). Used during CE XML export.</summary>
    public int DropDownLimit { get; set; } = Constants.DefaultDropDownLimit;

    /// <summary>CE String leaf display length (2^N chars; 16..4096, default 256).
    /// Seeded from the toolbar master; passed to CE XML / CE Field export.</summary>
    public int CeStringLength { get; set; } = Constants.DefaultCeStringLength;

    /// <summary>Copy CE Field array fabricate count (0 = off). When &gt; 0, Copy CE Field on a
    /// selected TArray pads it to this many element rows using a resolved element's layout.
    /// Seeded from the toolbar master; passed ONLY to the Copy CE Field export (not Copy CE XML).</summary>
    public int FabricateArrayCount { get; set; }

    /// <summary>CSX drilldown depth (0 = flat/dummy, 1-4 normal, 5-6 deep / warning band).
    /// Each extra level can multiply CE XML / CSX output exponentially because every
    /// ObjectProperty hit fans out to its own field tree. 4 was the historic ceiling
    /// after the cycle-elision fix in build 552 hit a 2GB StringBuilder OOM at depth 2
    /// on UWorld back-edges; raising to 6 is safe with the current cycle guard, but
    /// the slider colour shifts to amber/red at 5-6 to flag the size impact.</summary>
    [ObservableProperty] private int _csxDrilldownDepth;

    // === Locate in GWorld (forward BFS path search) ===
    // User-set search depth (how many pointer hops down from GWorld to look),
    // and live GWorld availability (drives gray-out of the feature).
    [ObservableProperty] private int _gWorldLocateDepth = 7;

    /// <summary>Opt-in deep "Locate in GWorld" (default OFF). When on, the forward
    /// BFS also follows object pointers stored inside one struct-element container
    /// level (TArray&lt;FStruct&gt; etc.) — reaching objects referenced ONLY from a
    /// struct-array element — and a bare value address in a deeply-nested heap
    /// container is attributed to its owner via the deep container scan. Heavier
    /// (reads each struct-array's elements per visited node); the analogue of the
    /// Value Search "Deep" option. The container-scan depth used when on.</summary>
    [ObservableProperty] private bool _gWorldLocateDeep;

    // Container-scan depth handed to find_path when GWorldLocateDeep is on (matches
    // the Value Search deep default; >1 enables the deep target-owner attribution).
    private const int kGWorldDeepContainerDepth = 4;

    /// <summary>Foreground brush for the depth display — default at 0-3, then
    /// warms from yellow (4) through orange to deep red (8) as the export
    /// cost grows. Max is 8.</summary>
    public Avalonia.Media.IBrush CsxDrilldownDepthBrush => CsxDrilldownDepth switch
    {
        >= 8 => Avalonia.Media.SolidColorBrush.Parse("#E02828"),  // deep red — very large output
        7    => Avalonia.Media.SolidColorBrush.Parse("#E04A2C"),  // red-orange
        6    => Avalonia.Media.SolidColorBrush.Parse("#E0702C"),  // orange
        5    => Avalonia.Media.SolidColorBrush.Parse("#E69A17"),  // amber
        4    => Avalonia.Media.SolidColorBrush.Parse("#E6C217"),  // yellow — first warning band
        _    => Avalonia.Media.SolidColorBrush.Parse("#D4D4D4"),  // default 0-3
    };

    partial void OnCsxDrilldownDepthChanged(int value)
        => OnPropertyChanged(nameof(CsxDrilldownDepthBrush));

    // Search
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private bool _hasSearchResults;

    /// <summary>Remembered field-search keywords for this session (LRU, max 8,
    /// longest-valid wins). Drives the search box's AutoCompleteBox suggestions.
    /// Persists across tab switches even though the live <see cref="SearchText"/>
    /// is cleared on switch; lives for the VM (= app session) lifetime.</summary>
    [ObservableProperty] private ObservableCollection<string> _searchHistory = new();

    // Debounce so we only remember a keyword the user has SETTLED on (typing
    // pauses), not every intermediate prefix while typing toward it.
    private Timer? _searchHistoryDebounce;

    // AOBMaker CE Plugin integration
    [ObservableProperty] private bool _isAobMakerAvailable;

    /// <summary>
    /// Per-row hint shown in the Functions DataGrid Notes column. Same
    /// value across every row in the current grid; the column is per-row
    /// in AXAML but the data is VM-level. When AOBMaker is unavailable
    /// the AA(B) shortcut still works (clipboard fallback) but the
    /// in-CE workflow is degraded; this surfaces that to the user without
    /// requiring them to hover for a tooltip.
    /// </summary>
    public string AobMakerNote => IsAobMakerAvailable
        ? ""
        : "AOBMaker plugin not found — AA Script export will fall back to clipboard";

    partial void OnIsAobMakerAvailableChanged(bool value)
        => OnPropertyChanged(nameof(AobMakerNote));

    // AOB Symbol toggle for CE XML export. The AOB anchor only makes sense when
    // the Live Walker root is GWorld (the AOB symbol resolves GWorld); from any
    // other root (e.g. "Start from GameEngine" / "Open in Live Walker") the
    // export must use a direct absolute address, so the checkbox is disabled.
    //
    // Split into two values so the checkbox can be PERSISTED without a fallback
    // game silently clobbering the stored choice:
    //   • AobSymbolPreference — the user's remembered INTENT (persisted in
    //     LiveWalkerUiOptions). Only a real checkbox click writes it.
    //   • UseAobSymbol        — the live EFFECTIVE value the checkbox binds to and
    //     the exporter reads = AobSymbolPreference gated by CanUseAobSymbol.
    // When GWorld came from a FALLBACK (no AOB) or the root isn't GWorld the box is
    // force-unchecked (CanUseAobSymbol == false), but the preference is kept intact
    // via the _suppressAobPreferenceCapture guard — so a later game/root where the
    // AOB *is* available restores the user's choice. See ReconcileAobSymbol().
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseAobSymbol))]
    private bool _useAobSymbol;

    /// <summary>The PERSISTED user intent for the AOB anchor (survives a UI restart via
    /// LiveWalkerUiOptions). Decoupled from the live <see cref="UseAobSymbol"/> so an
    /// automatic force-uncheck (fallback GWorld / non-GWorld root) never erases it.</summary>
    [ObservableProperty]
    private bool _aobSymbolPreference;

    /// <summary>Set while <see cref="ReconcileAobSymbol"/> writes <see cref="UseAobSymbol"/>
    /// programmatically, so <see cref="OnUseAobSymbolChanged"/> below does NOT mistake a
    /// gated re-derive (incl. the fallback force-uncheck) for a user click.</summary>
    private bool _suppressAobPreferenceCapture;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseAobSymbol))]
    private bool _isAobSymbolAvailable;

    /// <summary>True when breadcrumb[0] is the synthetic GWorld root — the only
    /// root for which the AOB anchor is valid. Recomputed from Breadcrumbs on
    /// every change (subscribed in the constructor).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseAobSymbol))]
    private bool _isRootGWorld;

    /// <summary>Gates the AOB checkbox's IsEnabled: the AOB symbol exists AND the
    /// current Live Walker root is GWorld.</summary>
    public bool CanUseAobSymbol => IsAobSymbolAvailable && IsRootGWorld;

    /// <summary>A real checkbox click (the box is only enabled when CanUseAobSymbol)
    /// records the persisted preference. Guarded so ReconcileAobSymbol's programmatic
    /// writes — including the fallback-GWorld force-uncheck — never overwrite it.</summary>
    partial void OnUseAobSymbolChanged(bool value)
    {
        if (_suppressAobPreferenceCapture) return;
        AobSymbolPreference = value;
    }

    /// <summary>Applying the persisted preference (or the user recording a new one) re-derives
    /// the live checkbox against the current gate, so a preference restored at startup shows up
    /// the moment the gate is already open.</summary>
    partial void OnAobSymbolPreferenceChanged(bool value) => ReconcileAobSymbol();

    /// <summary>Re-derive the live <see cref="UseAobSymbol"/> checkbox from the persisted
    /// <see cref="AobSymbolPreference"/> and the current <see cref="CanUseAobSymbol"/> gate,
    /// WITHOUT capturing the write back into the preference. Force-unchecks when the gate is
    /// closed (fallback GWorld / non-GWorld root) yet keeps the preference, and restores it
    /// when the gate re-opens.</summary>
    private void ReconcileAobSymbol()
    {
        bool desired = CanUseAobSymbol && AobSymbolPreference;
        if (UseAobSymbol == desired) return;
        _suppressAobPreferenceCapture = true;
        try { UseAobSymbol = desired; }
        finally { _suppressAobPreferenceCapture = false; }
    }

    // Guess What toggle: fill gaps between known fields with heuristic guesses
    [ObservableProperty] private bool _fillGaps;

    // Memory-record Description opt-ins for Copy CE XML / Copy CE Field. Off by
    // default (Description = bare Name). DescShowOffset appends the node's +offset;
    // DescShowType appends its class / struct / element type. Both honoured by the
    // nested spine + every drilldown node; the folded (Collapse chain) spine honours
    // offset only. Threaded into the CeXmlExportService Generate* calls.
    [ObservableProperty] private bool _descShowOffset;
    [ObservableProperty] private bool _descShowType;

    // Flatten GAS attributes (FGameplayAttributeData) one level in Copy CE XML / Copy CE
    // Field (default OFF). When on, an attribute struct's BaseValue/CurrentValue children
    // are promoted to sibling leaves named "HealthPoint ▸ BaseValue" at the combined offset
    // instead of a nested parent group — fewer nodes, easier to read/edit in CE. Scoped to
    // GameplayAttributeData structs only (CeXmlExportService gates the rest).
    [ObservableProperty] private bool _flattenGasAttributes;

    // Flatten primitive-leaf structs one level in Copy CE XML / Copy CE Field (default OFF).
    // A SUPERSET of Flatten GAS attributes: when on, ANY terminal StructProperty whose entire
    // flattened subtree is primitive inline scalars (int / float / bool / enum) — e.g. FVector,
    // FRotator, a plain {min,max} struct — has its children promoted to sibling leaves at the
    // combined offset instead of a nested group. Structs containing a pointer, string, FName,
    // container, or unresolved nested struct keep their group (CeXmlExportService gates the rest).
    [ObservableProperty] private bool _flattenLeafStructs;

    // Flatten leaf records one level in Copy CE XML / Copy CE Field (default OFF). A SUPERSET of
    // Flatten primitive-leaf structs: also accepts NameProperty and the FString family as leaf
    // children, so save-data "record" structs ({int Score, ERankID Rank, FName MsID, FString
    // PilotName}, incl. those inside a TMap/TArray) flatten fully. No field-count cap — the
    // all-terminal-leaf requirement is the safety gate (CeXmlExportService.IsTerminalLeafField).
    [ObservableProperty] private bool _flattenLeafRecords;

    // Collapse a drilled pointer whose target is a SINGLE terminal leaf (scalar / FName / FString)
    // into one CE record at the pointer field (Copy CE XML / Copy CE Field). The "pointer to a
    // string" case: avoids the extra group layer when the pointee holds a single watchable value.
    // scalar/name → +ptrOff, Offsets=[childOff]; FString → +ptrOff, Offsets=[0, childOff]. Default OFF.
    [ObservableProperty] private bool _collapseLeafPointers;

    // Alternating row colours for flattened container records (Copy CE XML / Copy CE Field): when
    // a TMap/TArray of records is flattened, each element's rows are tinted by index parity so the
    // records stay separable in CE. Even = struct[0],[2],…; Odd = struct[1],[3],…. Colours are RGB
    // hex "RRGGBB" (null/empty = no colour for that parity → CE theme); converted to CE COLORREF at
    // export. Default: enabled, Even = azure (0080FF, = CE FF8000), Odd = unset. Edited via the
    // "Record Colors…" dialog (FlattenColorDialog); persisted in LiveWalkerUiOptions.
    [ObservableProperty] private bool _flattenColorEnabled = true;
    [ObservableProperty] private string? _flattenColorEven = "0080FF";
    [ObservableProperty] private string? _flattenColorOdd;

    /// <summary>Open the Record Colors editor; the callback writes the picked colours back to the
    /// persisted VM properties. Lives here (not the Options MenuFlyout) because a colour picker
    /// needs more room than a menu item gives.</summary>
    [RelayCommand]
    private void OpenFlattenColors()
        => Views.FlattenColorDialog.ShowFor(
            FlattenColorEnabled, FlattenColorEven, FlattenColorOdd,
            (enabled, even, odd) =>
            {
                FlattenColorEnabled = enabled;
                FlattenColorEven = even;
                FlattenColorOdd = odd;
            });

    // Dedup shared objects in Copy CE XML / Copy CE Field drilldown (default ON).
    // A shared object (e.g. one PlayerState reached from several fields) is expanded
    // ONCE; later references become a flat "(shared)" pointer leaf. Prevents the
    // combinatorial blow-up that otherwise OOMs Copy CE XML on a dense object graph.
    [ObservableProperty] private bool _dedupSharedObjects = true;

    // Skip system/engine asset fields (Widget, SoundBase, Texture, Material, Particle,
    // Niagara, AnimInstance …) when DRILLING into pointer/struct children in Copy CE XML /
    // Copy CE Field (default ON). A CE user rarely watches those. Only the recursively-
    // resolved children are filtered — the top-level fields the user explicitly selected
    // are always kept (CeXmlExportService gates on emit depth). Name-based, conservative:
    // gameplay classes (Actor/Pawn/Character/components/Controller/PlayerState/GameInstance)
    // are never dropped, and a "N system fields hidden" note shows when anything was skipped.
    [ObservableProperty] private bool _excludeSystemComponents = true;

    // AOBMaker CE Plugin detection cooldown (avoids spamming pipe connect on rapid navigation)
    private DateTime _lastAobMakerCheck = DateTime.MinValue;
    private static readonly TimeSpan AobMakerCheckCooldown = TimeSpan.FromSeconds(5);

    // Auto-refresh
    [ObservableProperty] private bool _isAutoRefreshing;
    [ObservableProperty] private int _autoRefreshIntervalSec = Constants.DefaultAutoRefreshIntervalSec;

    // ── NumericUpDown façades ────────────────────────────────────────────────
    // [SNAPINTERVAL-2026-08-20] NumericUpDown.Value is decimal? (measured: Avalonia 12.1.1), and
    // clearing the text box drives it to null with no way to opt out at the control. Bound straight
    // at the non-nullable properties above, a COMPILED binding — which this app uses everywhere —
    // cannot convert that and paints a raw
    //   System.InvalidCastException: Could not convert '(null)' (null) to System.Int32
    // in a validation line under the control, leaving the field blank while the old value is still
    // the one in force. Binding a decimal? instead means no conversion is attempted.
    //
    // ⚠ These only absorb the empty box; they do not clamp. Range belongs to whoever already states
    // it (the control's Minimum/Maximum, or a view-model guard such as OnAutoRefreshIntervalSecChanged),
    // and several of these inputs have no meaningful range at all. See Helpers/NumericInput.cs.

    /// <inheritdoc cref="AutoRefreshIntervalSec"/>
    public decimal? AutoRefreshIntervalSecValue
    {
        get => (decimal)AutoRefreshIntervalSec;
        set
        {
            AutoRefreshIntervalSec = NumericInput.KeepCurrentIfEmpty(value, AutoRefreshIntervalSec);
            // Notify UNCONDITIONALLY. A rejected or emptied entry leaves the backing value
            // unchanged, so nothing else would raise a change and the control would keep
            // painting an empty box while a different value was in force. Round 1 of
            // [SNAPINTERVAL-2026-08-20] fixed the exception and left exactly that behind;
            // the live check is what caught it.
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private int _autoRefreshMinSec = Constants.MinAutoRefreshIntervalSec;
    [ObservableProperty] private string _autoRefreshStatusText = "sec";
    private DispatcherTimer? _autoRefreshTimer;
    private DispatcherTimer? _countdownTimer;
    private int _countdownRemaining;
    private bool _isAutoRefreshBenchmarked;
    private bool _isAutoRefreshing_InProgress; // Guard against overlapping refreshes
    // Auto-refresh was ON when something out of the user's control stopped it (pipe
    // disconnect / tab switch). Re-armed by ResumeAutoRefreshIfPending once the panel
    // is rooted on data again — a user untick or a navigation re-root never sets it.
    private bool _autoRefreshResumePending;
    private bool _isEditing; // True while a cell is being edited (suppresses auto-refresh)

    // Bookmark slots (4 fixed slots)
    [ObservableProperty] private ObservableCollection<BookmarkSlot> _bookmarkSlots = new();
    [ObservableProperty] private bool _isBookmarkSaveMode;  // True while waiting for user to pick a slot

    /// <summary>
    /// The spine that the last RE-ROOTING navigation replaced, so Back can put it
    /// back once the user has walked out of the new one. Originally captured only by
    /// a bookmark load ("Back after bookmark"); it now also covers
    /// <see cref="NavigateToAddressAsync"/>, which is the sink for the Go box, the
    /// Find Refs owner drill and every cross-tab "Open in Live Walker" handoff — all
    /// of which used to <see cref="ObservableCollection{T}.Clear"/> the spine with no
    /// way back at all.
    ///
    /// <para>ONE deep, deliberately. Each entry pins a whole crumb list, and a crumb
    /// can hold a container field's element list — the same reason the forward stack
    /// stores name+offset view records rather than live rows. Re-rooting twice
    /// therefore loses the first spine; that is the depth the bookmark path has
    /// always had, and it is bounded rather than growing with a long session.</para>
    ///
    /// <para>Explicit "start over" actions (Start from GWorld / GameEngine) and the
    /// Locate-in-GWorld re-spine deliberately CLEAR this instead of capturing: the
    /// user asked for a fresh root, so offering a Back into the discarded one would
    /// contradict the button they just pressed.</para>
    /// </summary>
    private SpineSnapshot? _replacedSpine;

    // Per-game bookmark persistence (keyed by PE hash). _bookmarks is null when the
    // store wasn't injected (tests). _activePeHash identifies which game's file to
    // write. _suppressBookmarkPersist gates the save while hydrating slots from disk
    // so the load doesn't immediately re-save.
    private readonly BookmarkStore? _bookmarks;
    private string _activePeHash = "";
    private bool _suppressBookmarkPersist;

    /// <summary>
    /// Raised when the View should scroll the DataGrid to a specific field name.
    /// The View subscribes to this and calls ScrollIntoView on the DataGrid.
    /// </summary>
    public event Action<string>? ScrollToFieldRequested;

    /// <summary>
    /// Raised when the View should scroll the DataGrid to the first search match.
    /// </summary>
    public event Action? ScrollToFirstSearchMatch;

    /// <summary>
    /// Raised when the View should scroll to (and the selection already points
    /// at) a specific field row. Carries the exact object so match navigation
    /// lands on the right row even when field names repeat (container elements).
    /// </summary>
    public event Action<LiveFieldValue>? ScrollFieldIntoView;

    /// <summary>
    /// Raised when the View should scroll the FunctionGrid to a specific
    /// UFunction by name. Used by cross-tab navigation from Interesting
    /// Funcs so the user lands on the correct row even when the function
    /// list scrolls past the visible area.
    /// </summary>
    public event Action<string>? ScrollToFunctionRequested;
    private string _lastScrolledSearchText = "";

    /// <summary>Raised to pivot the selected field's owning class in the
    /// experimental Class Pivot tab (className, fieldName). C5 right-click handoff.</summary>
    public event Action<string, string>? NavigateToPivot;

    /// <summary>
    /// Raised synchronously while saving a bookmark so the View can report the
    /// DataGrid's topmost visible row (written into the carrier) for scroll restore.
    /// </summary>
    public event Action<ViewAnchorRef>? CaptureViewAnchor;

    /// <summary>
    /// Raised after a bookmark finishes loading: the View re-selects the saved
    /// field rows (matched by name + offset) and scrolls the saved anchor row back
    /// into view, so the bookmark returns to what the user was looking at.
    /// </summary>
    public event Action<IReadOnlyList<BookmarkFieldRef>, BookmarkFieldRef?>? RestoreBookmarkView;

    /// <summary>Raised to show the currently-walked object's related objects
    /// (components, GAS ASC → AttributeSets, Controller↔Pawn) in the Related
    /// tab. Payload = current object address.</summary>
    public event Action<string>? NavigateToRelatedObjects;

    /// <summary>Raised by a field row's "inst" button to open that field's pointed-to
    /// object class in the Instance Finder tab (pre-fill class name + run search).
    /// Payload = class name. Mirrors the Property Search / Interesting Funcs+Props
    /// "inst" handoff (MainWindow switches tab + runs SearchForClassAsync).</summary>
    public event Action<string>? NavigateToInstanceFinder;

    /// <summary>Gates the "Pivot this property" context-menu item — true only when
    /// the experimental Class Pivot tab is available (mirrors the gate).</summary>
    [ObservableProperty] private bool _pivotEnabled;

    /// <summary>Per-field action: pivot the current class on the selected field in
    /// the Class Pivot tab. Inert for synthetic container views (Array/Map/Set/
    /// DataTable labels) — the handoff just reports the class isn't in a snapshot.</summary>
    [RelayCommand]
    private void PivotThis(LiveFieldValue? field)
    {
        field ??= SelectedField;
        if (field == null || string.IsNullOrEmpty(CurrentClassName) || string.IsNullOrEmpty(field.Name))
            return;
        NavigateToPivot?.Invoke(CurrentClassName, field.Name);
    }

    public LiveWalkerViewModel(IDumpService dump, ILoggingService log, IPlatformService platform,
                               IAobMakerBridge? aobMaker = null, BookmarkStore? bookmarks = null)
    {
        _dump = dump;
        _log = log;
        _platform = platform;
        _aobMaker = aobMaker;
        _bookmarks = bookmarks;

        // Functions-filter keyword memory: remember a settled keyword only when it
        // yielded at least one visible function row.
        _functionFilterMemory = new KeywordSearchMemory(() => (FunctionFilter, Functions.Count > 0));

        // Initialize the bookmark slots
        for (int i = 0; i < Constants.BookmarkSlotCount; i++)
            BookmarkSlots.Add(new BookmarkSlot { SlotIndex = i });

        // Keep IsRootGWorld in sync with the breadcrumb root so the AOB option
        // auto-disables whenever the walk root isn't GWorld (Start from
        // GameEngine / Open in Live Walker / etc.). Subscribing here covers every
        // mutation site (Clear/Add on navigate, RemoveAt on Back) at once.
        Breadcrumbs.CollectionChanged += (_, e) =>
        {
            IsRootGWorld = Breadcrumbs.Count > 0 && Breadcrumbs[0].FieldName == "GWorld";

            // Forward-history invalidation rides the same subscription for the same
            // reason: there are well over a dozen crumb-push sites and a handful of
            // Clear sites, and a hand-placed ClearForwardStack() at each would
            // silently miss the NEXT navigation path someone adds. (Don't restate the
            // counts here — an earlier revision of this comment said "7 and 6" and was
            // 14 and 7 by the time anyone re-read it. The drift is the argument.)
            // Add/Reset = a fresh navigation, so any forward history is dead.
            // Remove = Back / a breadcrumb jump, which PUSHES onto the stack and must
            // not clear it. _replayingHistory exempts Back's and Forward's own spine
            // swaps, which are replays of history rather than new navigations.
            if (_replayingHistory) return;
            if (e.Action is NotifyCollectionChangedAction.Add
                         or NotifyCollectionChangedAction.Reset)
                ClearForwardStack();
        };

        // An open cell editor cannot outlive the rows it was opened on. Avalonia tears
        // such an edit down WITHOUT raising CellEditEnded, and CellEditEnded is the ONLY
        // place the view clears IsEditing — so a stranded `true` silently vetoed every
        // auto-refresh tick for the rest of the session ([AUTOREFRESH-2026-08-19]).
        // Subscribing to the collection covers every repopulation site at once, for the
        // same reason the Breadcrumbs hook above does: SIX populate methods set HasData
        // and rebuild the grid without going through UpdateDisplay, and a hand-placed
        // clear at each would miss the seventh someone adds next.
        HookFieldsRebuild(Fields);
    }

    private ObservableCollection<LiveFieldValue>? _hookedFields;

    private void HookFieldsRebuild(ObservableCollection<LiveFieldValue> fields)
    {
        if (ReferenceEquals(_hookedFields, fields)) return;
        if (_hookedFields != null) _hookedFields.CollectionChanged -= OnFieldsRebuilt;
        _hookedFields = fields;
        fields.CollectionChanged += OnFieldsRebuilt;
    }

    private void OnFieldsRebuilt(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Reset  == Clear() + re-Add, the full-rebuild branch of UpdateDisplay.
        // Replace == `Fields[i] = newFields[i]`, its in-place branch (kept because it
        //            preserves DataGrid scroll). It swaps the row OBJECT out from under
        //            any open editor, so it kills the edit just as dead as a Clear does —
        //            and it is the branch a same-object Refresh actually takes, i.e. the
        //            common one. Missing it left the latch strandable on the hot path.
        // Add/Remove are deliberately NOT here: appending a row does not invalidate an
        // editor open on a different one.
        if (e.Action is NotifyCollectionChangedAction.Reset
                     or NotifyCollectionChangedAction.Replace)
            IsEditing = false;
    }

    /// <summary>The collection OBJECT is swapped wholesale by the field-search apply
    /// path, which orphans a subscription made on the previous one — re-hook, and treat
    /// the swap itself as a rebuild.</summary>
    partial void OnFieldsChanged(ObservableCollection<LiveFieldValue> value)
    {
        HookFieldsRebuild(value);
        IsEditing = false;
    }

    public void SetEngineState(EngineState state)
    {
        _engineState = state;
        _activePeHash = state?.PeHash ?? "";
        IsAobSymbolAvailable = !string.IsNullOrEmpty(state?.GWorldAob);
    }

    partial void OnIsAobSymbolAvailableChanged(bool value)
    {
        // AOB just became (un)available for this game: a fallback GWorld has no AOB,
        // so the box force-unchecks; a real AOB restores the stored preference. The
        // preference itself is untouched (ReconcileAobSymbol is guarded).
        ReconcileAobSymbol();
    }

    partial void OnIsRootGWorldChanged(bool value)
    {
        // Entering/leaving a GWorld root re-derives the AOB checkbox from the stored
        // preference: force-unchecked off a non-GWorld root (e.g. Start from GameEngine)
        // so a stale check can't drive an AOB export the symbol doesn't anchor, and
        // restored when GWorld is the root again. Preference untouched (guarded).
        ReconcileAobSymbol();
    }

    // Guard so AutoFillGapsRetryAsync can auto-CHECK the Guess? toggle (to reflect
    // that guessing is active) without firing a second redundant re-walk — it already
    // performs the fill_gaps walk itself and returns that result to the caller.
    private bool _suppressFillGapsRefresh;

    partial void OnFillGapsChanged(bool value)
    {
        if (_suppressFillGapsRefresh) return;
        // Toggle triggers refresh to rebuild field list with/without guessed fields
        if (!string.IsNullOrEmpty(CurrentAddress))
            RefreshCommand.Execute(null);
    }

    /// <summary>Set the Guess? (FillGaps) toggle WITHOUT triggering its auto-refresh —
    /// used when a navigation has already produced the gap-filled result.</summary>
    private void SetFillGapsSilently(bool value)
    {
        if (FillGaps == value) return;
        _suppressFillGapsRefresh = true;
        try { FillGaps = value; }
        finally { _suppressFillGapsRefresh = false; }
    }

    /// <summary>Clear error message, status text, and any locate-failure banner
    /// (e.g., at the start of a new operation).</summary>
    private void ClearStatus()
    {
        ClearError();
        StatusText = "";
        LocateFailureMessage = "";
    }

    [RelayCommand]
    private async Task StartFromWorldAsync()
    {
        // Fresh root walk (GWorld actor list) — drop any field-search filter.
        ClearFieldSearchForNavigation();
        try
        {
            ClearStatus();
            IsLoading = true;
            StopAutoRefreshTimer();
            _replacedSpine = null;   // explicit "start over" — see _replacedSpine
            IsBookmarkSaveMode = false;

            var world = await _dump.WalkWorldAsync(Constants.WorldWalkMaxDepth, arrayLimit: ArrayLimit);
            _cachedWorld = world;

            Breadcrumbs.Clear();
            Breadcrumbs.Add(new BreadcrumbItem
            {
                Address = world.WorldAddr,
                Label = "GWorld",
                IsPointerDeref = true,
                FieldOffset = 0,
                FieldName = "GWorld",
            });

            PopulateFromWorld(world);

            // Show DLL-side error if world walk was partial (e.g. PersistentLevel not found)
            if (!string.IsNullOrEmpty(world.Error))
            {
                SetError(new InvalidOperationException(world.Error));
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to load GWorld", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Root the Live Walker on the live GEngine object. The DLL resolves it by a
    /// reflected member (GameViewport), not by class name, so it works across UE
    /// versions / game UGameEngine subclasses. This is a non-GWorld root: the AOB
    /// option stays disabled and CE export anchors on the engine's absolute
    /// (session-only) address. No GWorld-style follow-on features are wired —
    /// the user drills GameInstance / GameViewport / World manually.
    /// </summary>
    [RelayCommand]
    private async Task StartFromGameEngineAsync()
    {
        try
        {
            ClearStatus();
            IsLoading = true;
            StopAutoRefreshTimer();
            _replacedSpine = null;   // explicit "start over" — see _replacedSpine
            IsBookmarkSaveMode = false;

            var engine = await _dump.ResolveGameEngineAsync();
            if (!engine.Found || string.IsNullOrEmpty(engine.Address))
            {
                StatusText = "GameEngine not found — no live UEngine with a GameViewport (scan first / load a level)";
                _log.Info("StartFromGameEngine: resolve returned no live engine");
                return;
            }

            // Walk the engine as the root. FieldName "GameEngine" marks a
            // non-GWorld root (≠ the "GWorld" / "Custom" markers), so IsRootGWorld
            // stays false and the AOB checkbox disables itself.
            Breadcrumbs.Clear();
            References.Clear();
            HasReferences = false;
            await NavigateToAsync(engine.Address, "GameEngine", 0, "GameEngine", isPointer: true,
                                  // Re-roots the walker (Breadcrumbs.Clear() above), so there is
                                  // no parent to be stale against. NOT an oversight.
                                  expectedParent: null);

            var note = engine.GameInstanceOk ? "" : "  (GameInstance null — engine may be mid-boot)";
            StatusText = $"Started from GameEngine ({engine.ClassName}){note}";
            _log.Info($"StartFromGameEngine: {engine.ClassName} @ {engine.Address} " +
                      $"viewportOk={engine.GameViewportOk} instanceOk={engine.GameInstanceOk}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to start from GameEngine", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// True when <paramref name="crumb"/> is the synthetic GWorld actor-list root
    /// (the "Start from GWorld" view). Only this crumb may be re-displayed via
    /// <see cref="PopulateFromWorld"/>. A deeper breadcrumb such as OwningWorld can
    /// resolve to the very same UWorld address, but it was reached through a normal
    /// pointer field and must be walked as an instance — otherwise navigating or
    /// restoring a bookmark to it swaps the saved object for the GWorld actor list
    /// (headed by the world name, e.g. "PLV_game"). The FieldName=="GWorld" marker
    /// is unique to the synthetic root (no UObject field is named "GWorld"); this
    /// mirrors the Breadcrumbs.Count==1 guard the auto-refresh path already uses.
    /// </summary>
    private bool IsGWorldActorListRoot(BreadcrumbItem? crumb) =>
        crumb != null
        && crumb.FieldName == "GWorld"
        && _cachedWorld != null
        && crumb.Address == _cachedWorld.WorldAddr;

    private void PopulateFromWorld(WorldWalkResult world)
    {
        CurrentObjectName = world.WorldName;
        CurrentClassName = "UWorld";
        CurrentAddress = world.WorldAddr;
        HasData = true;
        ShowCeXml = false;
        HasParent = false;
        _isDefinitionView = false;
        CurrentOuterAddr = "";
        CurrentOuterName = "";
        CurrentOuterClassName = "";

        Fields.Clear();

        // The actor list is a PAGE. Before build 2818 the reply carried only the page
        // size, so a 500-actor page and a 500-actor level were identical on the wire
        // and an actor at index 1877 simply was not there. ActorTotal < 0 means the
        // array was never read at all -- the DLL sets Error in that case and the
        // caller surfaces it, so say nothing extra here. (audit #5 D5/F6)
        if (world.Truncated && world.ActorTotal > world.ActorCount)
        {
            CurrentObjectName =
                $"{world.WorldName}  ⚠ showing {world.ActorCount:N0} of {world.ActorTotal:N0} actors";
        }

        // Compute base address for FieldAddress display
        ulong worldBase = 0;
        try
        {
            if (!string.IsNullOrEmpty(world.WorldAddr))
                worldBase = Convert.ToUInt64(world.WorldAddr.Replace("0x", "").Replace("0X", ""), 16);
        }
        catch { /* ignore parse failures */ }

        // PersistentLevel as first navigable entry (offset from DLL walk_world response)
        if (!string.IsNullOrEmpty(world.LevelAddr) && world.LevelAddr != "0x0")
        {
            var pLevel = new LiveFieldValue
            {
                Name = world.LevelName ?? "PersistentLevel",
                TypeName = "ObjectProperty",
                Offset = world.LevelOffset,
                Size = 8,
                PtrAddress = world.LevelAddr,
                PtrName = world.LevelName ?? "PersistentLevel",
                PtrClassName = "ULevel",
            };
            if (worldBase != 0)
                pLevel.FieldAddress = $"0x{worldBase + (ulong)world.LevelOffset:X}";
            Fields.Add(pLevel);
        }

        // Each actor as a navigable entry.
        //
        // HasNoParentOffset, and it is not cosmetic: these rows are RECONSTRUCTED from
        // each actor's Outer, because ULevel::Actors carries no UPROPERTY (audit #5
        // F8/F9) — there is no offset from UWorld to an actor, and no element index
        // either. Offset 0 here is "unknown", not "at +0", and a CE pointer chain that
        // believes it emits [UWorld + 0] = the world's VTABLE POINTER. The same hop
        // built by Locate-in-GWorld has always been marked (PathStepToBreadcrumbs
        // stamps FieldOffset = -1 for a "LevelActor" step); this list was the copy that
        // never got the marker, so every export through it was silently wrong.
        //
        // Components hang off their actor and are found the same way, so they inherit it.
        foreach (var actor in world.Actors)
        {
            Fields.Add(new LiveFieldValue
            {
                Name = actor.Name,
                TypeName = "ObjectProperty",
                Offset = 0,
                HasNoParentOffset = true,
                Size = 8,
                PtrAddress = actor.Address,
                PtrName = actor.Name,
                PtrClassName = actor.ClassName,
            });

            // Components as indented sub-entries
            foreach (var comp in actor.Components)
            {
                Fields.Add(new LiveFieldValue
                {
                    Name = $"  {actor.Name}.{comp.Name}",
                    TypeName = "ObjectProperty",
                    Offset = 0,
                    HasNoParentOffset = true,
                    Size = 8,
                    PtrAddress = comp.Address,
                    PtrName = comp.Name,
                    PtrClassName = comp.ClassName,
                });
            }
        }
    }

    /// <summary>
    /// When <paramref name="parent"/> is a Map container view, the per-element
    /// value offset (aligned value offset, falling back to key size) — exactly
    /// the offset <see cref="PopulateMapContainerFields"/> uses to place each
    /// element's value inside the TPair. A drilled Map value's raw field offset
    /// is only the element-base offset (index*stride); adding this lands the
    /// CE/CSX pointer chain on the value rather than valueOffset bytes short.
    /// Returns 0 for any non-map parent (direct struct fields and struct/set
    /// array elements have value == element base), so it is a safe additive
    /// correction to a drilled breadcrumb's FieldOffset.
    /// </summary>
    internal static int MapValueDrillOffset(BreadcrumbItem? parent)
    {
        if (parent is { IsContainerView: true, ContainerField: { } cf }
            && cf.MapCount > 0 && !string.IsNullOrEmpty(cf.MapKeyType))
        {
            return ContainerGeometry.MapValueOffsetOf(cf);
        }
        return 0;
    }

    [RelayCommand]
    private async Task NavigateToFieldAsync(LiveFieldValue? field)
    {
        if (field == null || !field.IsNavigable) return;

        // Re-check AOBMaker CE Plugin availability (detects CE start/close, cooldown-throttled)
        TryCheckAobMaker();

        try
        {
            ClearStatus();
            IsLoading = true;

            // Capture the parent AT GESTURE TIME, not after the walk. A post-await check
            // alone only catches "drill first, Back second"; the reverse ordering commits
            // its damage synchronously before the await ever returns. Audit #5 AE2 paid for
            // this exact lesson already: "The ticket is claimed at GESTURE time ... Claimed
            // in the command it would invert the fix."
            var parentAtGesture = CurrentCrumb;

            // Save the clicked field name on the current breadcrumb for scroll restoration on Back
            if (Breadcrumbs.Count > 0)
                Breadcrumbs[^1].ScrollHintFieldName = field.Name;
            // Full view state too (selection + scroll anchor) — the richer rung of
            // the restore ladder. ScrollHintFieldName above stays as the fallback
            // for crumbs that never get captured.
            CaptureCrumbViewState(field);

            // CE/CSX chain offsets are relative to the parent's RESOLVED address.
            // When drilling a Map element's VALUE, the parent (the Map container
            // view) resolves to the element-storage base, but the value sits at
            // +valueOffset inside each element (the key is at the front of the
            // TPair). field.Offset is only the element-base offset (index*stride),
            // so the breadcrumb must carry the FULL offset to the value or every
            // child lands valueOffset bytes short (the off-by-8 on FName-keyed
            // maps). Zero for non-map parents, so other navigation is unchanged.
            //
            // A row with no parent offset (a GWorld actor-list entry) carries the -1
            // sentinel instead. -1 is the established marker for "this hop cannot be
            // reproduced by a forward offset" — PathStepToBreadcrumbs stamps it, and
            // BuildAaScript's gworldWalkable gate already tests FieldOffset >= 0. Adding
            // MapValueDrillOffset to it would turn the sentinel back into a number.
            int navOffset = field.HasNoParentOffset
                ? -1
                : field.Offset + MapValueDrillOffset(
                    Breadcrumbs.Count > 0 ? Breadcrumbs[^1] : null);

            if (!string.IsNullOrEmpty(field.PtrAddress) && field.PtrAddress != "0x0")
            {
                // ObjectProperty navigation (pointer dereference)
                await NavigateToAsync(field.PtrAddress, field.Name, navOffset, field.Name, isPointer: true,
                                      expectedParent: parentAtGesture);
            }
            else if (!string.IsNullOrEmpty(field.StructDataAddr) && field.StructDataAddr != "0x0")
            {
                // StructProperty navigation: walk struct data using its class
                var result = await _dump.WalkInstanceAsync(field.StructDataAddr, field.StructClassAddr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit, fillGaps: FillGaps);
                result = await AutoFillGapsRetryAsync(result, field.StructDataAddr, field.StructClassAddr);
                var displayName = !string.IsNullOrEmpty(field.StructTypeName)
                    ? $"{field.Name} ({field.StructTypeName})"
                    : field.Name;

                // Same staleness window as the pointer branch above — two awaits happened
                // between the gesture and this append, and navOffset was computed from the
                // parent BEFORE them.
                if (!IsStillOnParent(parentAtGesture))
                {
                    StatusText = $"Navigation superseded — '{field.Name}' was discarded (you moved while it loaded).";
                    _log.Info($"NAV✕Struct {field.Name} discarded: parent changed during the walk");
                    return;
                }

                // DataTable row navigation: the uint8* is a pointer that needs dereference,
                // not an inline struct. Set IsPointerDeref=true for correct CE XML pointer chain.
                var isDataTableRow = Breadcrumbs.Count > 0 && Breadcrumbs[^1].IsDataTableView;

                Breadcrumbs.Add(new BreadcrumbItem
                {
                    Address = field.StructDataAddr,
                    Label = displayName,
                    ClassAddr = field.StructClassAddr,
                    FieldOffset = navOffset,
                    FieldName = field.Name,
                    TargetClassName = field.StructTypeName,
                    IsPointerDeref = isDataTableRow,
                });
                _log.Info($"NAV→Struct {field.Name} addr={field.StructDataAddr} off={FormatCrumbOffset(navOffset)} dtRow={isDataTableRow} | BC={FormatBreadcrumbTrace()}");
                UpdateDisplay(result);
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to navigate to {field.Name}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToContainerAsync(LiveFieldValue? field)
    {
        if (field == null || !field.IsContainerNavigable) return;

        // Drilling into a container shows different data (its elements) — the
        // field-search keyword no longer applies. (This path rebuilds Fields via
        // Populate*ContainerFields, bypassing UpdateDisplay's own clear.)
        ClearFieldSearchForNavigation();

        try
        {
            ClearStatus();
            IsLoading = true;

            // Save scroll hint on current breadcrumb
            if (Breadcrumbs.Count > 0)
                Breadcrumbs[^1].ScrollHintFieldName = field.Name;
            CaptureCrumbViewState(field);

            if (field.DataTableRowCount > 0 && _cachedDataTableRows != null)
            {
                NavigateToDataTableContainer(field, _cachedDataTableRows);
            }
            else if (field.ArrayCount > 0 && !string.IsNullOrEmpty(field.ArrayInnerType))
            {
                await NavigateToArrayContainerAsync(field);
            }
            else if (field.MapCount > 0 && !string.IsNullOrEmpty(field.MapKeyType))
            {
                NavigateToMapContainer(field);
            }
            else if (field.SetCount > 0 && !string.IsNullOrEmpty(field.SetElemType))
            {
                NavigateToSetContainer(field);
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to navigate to container {field.Name}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task NavigateToArrayContainerAsync(LiveFieldValue field)
    {
        var typeLabel = !string.IsNullOrEmpty(field.ArrayStructType)
            ? field.ArrayStructType : field.ArrayInnerType;
        var label = $"{field.Name} [{field.ArrayCount} x {typeLabel}]";

        // Fetch elements BEFORE adding breadcrumb — if the DLL call fails,
        // we must not leave a stale breadcrumb that causes repeated entries.
        var parentAddr = CurrentAddress;
        // ...and capture WHICH parent, at gesture time. parentAddr alone is not an
        // identity: a container drill pushes a crumb without changing CurrentAddress,
        // so an address-only check lets the very mislabel this guards against through.
        var parentAtGesture = CurrentCrumb;
        List<ArrayElementValue> elements;
        if (field.ArrayElements != null && field.ArrayElements.Count >= field.ArrayCount)
        {
            // All elements already inline (complete set)
            elements = field.ArrayElements;
        }
        else if (field.ArrayElements is { Count: > 0 } && IsPointerOrStructArrayType(field.ArrayInnerType))
        {
            // Pointer/struct arrays: use inline elements (Phase D/E/F resolved names).
            // read_array_elements is scalar-only and cannot resolve pointer names.
            elements = field.ArrayElements;
        }
        else if (!string.IsNullOrEmpty(field.ArrayInnerAddr) && !string.IsNullOrEmpty(parentAddr))
        {
            // Scalar arrays: fetch full element list from DLL (Phase B)
            var result = await _dump.ReadArrayElementsAsync(
                parentAddr, field.Offset, field.ArrayInnerAddr,
                field.ArrayInnerType, field.ArrayElemSize, 0, field.ArrayCount);
            elements = result.Elements;
        }
        else
        {
            elements = field.ArrayElements ?? new();
        }

        // Only add breadcrumb after successful element retrieval — and only if the
        // user is still where they launched this from. parentAddr was captured before
        // the await above, so BOTH the address and the offset can be stale.
        if (!IsStillOnParent(parentAtGesture))
        {
            StatusText = $"Navigation superseded — '{field.Name}' was discarded (you moved while it loaded).";
            _log.Info($"NAV✕Array {field.Name} discarded: parent changed during the element fetch");
            return;
        }

        // Scalar arrays are re-fetched in full above; only pointer/struct arrays fall back to the
        // capped inline preview, so compare the elements actually shown against the true count.
        label += ContainerTruncation.BadgeSuffix(elements.Count, field.ArrayCount);
        var arrTruncStatus = ContainerTruncation.StatusLine(elements.Count, field.ArrayCount);
        if (arrTruncStatus.Length > 0) StatusText = arrTruncStatus;

        Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = parentAddr,
            Label = label,
            FieldOffset = field.Offset,
            FieldName = field.Name,
            IsPointerDeref = false,
            IsContainerView = true,
            ContainerField = field,
        });
        _log.Info($"NAV→Container {field.Name} addr={parentAddr} off=0x{field.Offset:X} | BC={FormatBreadcrumbTrace()}");

        // Persist the displayed elements onto the cached field so a later Back-navigation
        // re-renders the SAME rows. GoBackAsync→RepopulateContainerView reads
        // ContainerField.ArrayElements; for a struct array whose inner struct reflects 0
        // fields the inline walk preview returns no elements, so the rows above were
        // fetched on-demand into the LOCAL `elements` list — without this assignment the
        // cached field stays empty and Back showed an EMPTY grid. No-op when `elements`
        // already is field.ArrayElements (the inline-complete branch).
        field.ArrayElements = elements;

        PopulateArrayContainerFields(elements, field);
    }

    private void NavigateToMapContainer(LiveFieldValue field)
    {
        var keyLabel = !string.IsNullOrEmpty(field.MapKeyType) ? field.MapKeyType : "?";
        var valLabel = !string.IsNullOrEmpty(field.MapValueType) ? field.MapValueType : "?";
        var received = field.MapElements?.Count ?? 0;
        var label = $"{field.Name} {{Map: {field.MapCount}, {keyLabel} \u2192 {valLabel}}}"
            + ContainerTruncation.BadgeSuffix(received, field.MapCount);

        Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = CurrentAddress,
            Label = label,
            FieldOffset = field.Offset,
            FieldName = field.Name,
            IsPointerDeref = false,
            IsContainerView = true,
            ContainerField = field,
        });
        _log.Info($"NAV→MapContainer {field.Name} addr={CurrentAddress} off=0x{field.Offset:X} | BC={FormatBreadcrumbTrace()}");

        var mapTruncStatus = ContainerTruncation.StatusLine(received, field.MapCount);
        if (mapTruncStatus.Length > 0) StatusText = mapTruncStatus;

        PopulateMapContainerFields(field.MapElements ?? new(), field);
    }

    private void NavigateToSetContainer(LiveFieldValue field)
    {
        var elemLabel = !string.IsNullOrEmpty(field.SetElemType) ? field.SetElemType : "?";
        var received = field.SetElements?.Count ?? 0;
        var label = $"{field.Name} {{Set: {field.SetCount}, {elemLabel}}}"
            + ContainerTruncation.BadgeSuffix(received, field.SetCount);

        Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = CurrentAddress,
            Label = label,
            FieldOffset = field.Offset,
            FieldName = field.Name,
            IsPointerDeref = false,
            IsContainerView = true,
            ContainerField = field,
        });
        _log.Info($"NAV→SetContainer {field.Name} addr={CurrentAddress} off=0x{field.Offset:X} | BC={FormatBreadcrumbTrace()}");

        var setTruncStatus = ContainerTruncation.StatusLine(received, field.SetCount);
        if (setTruncStatus.Length > 0) StatusText = setTruncStatus;

        PopulateSetContainerFields(field.SetElements ?? new(), field);
    }

    /// <summary>Preview text for the synthetic RowMap field injected into a DataTable's
    /// field grid. Pure so the truncation badge can be pinned without a dispatcher —
    /// <see cref="TryLoadDataTableRowsAsync"/>, its only caller, runs fire-and-forget and
    /// lands on the UI thread.</summary>
    internal static string DataTableFieldPreview(DataTableWalkResult dt) =>
        $"{{DataTable: {dt.RowCount} rows, {dt.RowStructName}}}"
        + ContainerTruncation.BadgeSuffix(dt.Rows.Count, dt.RowCount);

    /// <summary>internal for the audit #5 V8 drill test: the DataTable branch of
    /// NavigateToContainer is otherwise reachable only through a fire-and-forget
    /// dispatcher hop that populates <c>_cachedDataTableRows</c>.</summary>
    internal void NavigateToDataTableContainer(LiveFieldValue field, DataTableWalkResult dtResult)
    {
        // The DataTable drill is the container path the [CONTAINERCAP] badge sweep did
        // not reach, and it was the worst of the family: the crumb printed RowCount —
        // the TRUE total the DLL reports — over a grid holding only the rows that fit
        // WalkDataTableRowsAsync's fixed page (64). A 5,000-row table therefore
        // announced "5000" and showed 64, so a row that simply had not been fetched
        // read as a row the table does not contain (audit #5 V8).
        var received = dtResult.Rows.Count;
        var label = $"RowMap [{dtResult.RowCount} x {dtResult.RowStructName}]"
            + ContainerTruncation.BadgeSuffix(received, dtResult.RowCount);

        Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = CurrentAddress,
            Label = label,
            FieldOffset = dtResult.RowMapOffset,
            FieldName = field.Name,
            IsPointerDeref = false,
            IsContainerView = true,
            IsDataTableView = true,
            ContainerField = field,
            DataTableData = dtResult,
        });
        _log.Info($"NAV\u2192DataTable {field.Name} addr={CurrentAddress} rows={received}/{dtResult.RowCount} struct={dtResult.RowStructName} | BC={FormatBreadcrumbTrace()}");

        // FixedCap wording, not StatusLine: the Array Limit slider does NOT govern this
        // view (WalkDataTableRowsAsync uses its own page size), so sending the user to
        // that slider would be a second false statement on top of the first.
        var dtTruncStatus = ContainerTruncation.FixedCapStatusLine(received, dtResult.RowCount, "rows");
        if (dtTruncStatus.Length > 0) StatusText = dtTruncStatus;

        PopulateDataTableRowFields(dtResult);
    }

    private void PopulateDataTableRowFields(DataTableWalkResult dtResult)
    {
        // Header carries the badge too — on CurrentObjectName, matching the Array / Map /
        // Set populate siblings — so a Back-navigation into this view (which re-enters
        // here without re-running NavigateToDataTableContainer) still says it.
        CurrentObjectName = "RowMap"
            + ContainerTruncation.BadgeSuffix(dtResult.Rows.Count, dtResult.RowCount);
        CurrentClassName = $"DataTable<{dtResult.RowStructName}>";
        HasData = true;
        ShowCeXml = false;
        HasParent = false;
        CurrentOuterAddr = "";
        CurrentOuterName = "";
        CurrentOuterClassName = "";

        Fields.Clear();
        foreach (var row in dtResult.Rows)
        {
            // Build preview from first 2 scalar fields
            var preview = "";
            var previewParts = new List<string>();
            foreach (var fv in row.Fields)
            {
                if (previewParts.Count >= 2) break;
                if (!string.IsNullOrEmpty(fv.TypedValue) && fv.TypedValue != "0" && fv.TypedValue != "0.0"
                    && fv.TypeName != "ObjectProperty" && fv.TypeName != "ClassProperty")
                {
                    previewParts.Add($"{fv.Name}={fv.TypedValue}");
                }
                else if (!string.IsNullOrEmpty(fv.StrValue))
                {
                    var s = fv.StrValue.Length > 30 ? fv.StrValue[..30] + "..." : fv.StrValue;
                    previewParts.Add($"{fv.Name}=\"{s}\"");
                }
                else if (!string.IsNullOrEmpty(fv.PtrName))
                {
                    previewParts.Add($"{fv.Name}={fv.PtrName}");
                }
            }
            if (previewParts.Count > 0)
                preview = " | " + string.Join(", ", previewParts);

            // Actual byte offset of the uint8* pointer within TSparseArray data
            int rowPtrOffset = row.SparseIndex * dtResult.Stride + dtResult.FNameSize;
            var f = new LiveFieldValue
            {
                Name = $"[{row.SparseIndex}] {row.RowName}",
                TypeName = "StructProperty",
                Offset = rowPtrOffset,
                Size = 0,
                TypedValue = $"{{{dtResult.RowStructName}}}{preview}",
                // Enable struct navigation to drill into the row data
                StructDataAddr = row.DataAddr,
                StructClassAddr = dtResult.RowStructAddr,
                StructTypeName = dtResult.RowStructName,
            };
            if (!string.IsNullOrEmpty(row.DataAddr))
                f.FieldAddress = row.DataAddr;
            Fields.Add(f);
        }
    }

    private void PopulateArrayContainerFields(List<ArrayElementValue> elements, LiveFieldValue sourceField)
    {
        var typeLabel = !string.IsNullOrEmpty(sourceField.ArrayStructType)
            ? sourceField.ArrayStructType : sourceField.ArrayInnerType;
        CurrentObjectName = sourceField.Name
            + ContainerTruncation.BadgeSuffix(elements.Count, sourceField.ArrayCount);
        CurrentClassName = $"Array<{typeLabel}>";
        HasData = true;
        ShowCeXml = false;
        // Disable Parent button for container views (not a UObject)
        HasParent = false;
        CurrentOuterAddr = "";
        CurrentOuterName = "";
        CurrentOuterClassName = "";

        // Parse TArray::Data base address for computing element addresses
        ulong dataBase = 0;
        if (!string.IsNullOrEmpty(sourceField.ArrayDataAddr))
            ulong.TryParse(sourceField.ArrayDataAddr.Replace("0x", "").Replace("0X", ""),
                System.Globalization.NumberStyles.HexNumber, null, out dataBase);

        // Check if this is a struct array with navigation metadata
        bool isStructArray = sourceField.ArrayInnerType == "StructProperty"
            && !string.IsNullOrEmpty(sourceField.ArrayStructClassAddr);

        Fields.Clear();
        foreach (var elem in elements)
        {
            // Compute element address for struct navigation
            var elemAddr = (isStructArray && dataBase != 0 && sourceField.ArrayElemSize > 0)
                ? $"0x{dataBase + (ulong)(elem.Index * sourceField.ArrayElemSize):X}" : "";

            var f = new LiveFieldValue
            {
                Name = $"[{elem.Index}]",
                TypeName = sourceField.ArrayInnerType,
                Offset = elem.Index * sourceField.ArrayElemSize,
                Size = sourceField.ArrayElemSize,
                HexValue = elem.Hex,
                TypedValue = !string.IsNullOrEmpty(elem.PtrName)
                    ? (!string.IsNullOrEmpty(elem.PtrClassName)
                        ? $"{elem.PtrName} ({elem.PtrClassName})"
                        : elem.PtrName)
                    : (!string.IsNullOrEmpty(elem.EnumName) ? elem.EnumName : elem.Value),
                PtrAddress = elem.PtrAddress,
                PtrName = elem.PtrName,
                PtrClassName = elem.PtrClassName,
                EnumName = elem.EnumName,
                // Struct navigation for StructProperty elements
                StructDataAddr = elemAddr,
                StructClassAddr = isStructArray ? sourceField.ArrayStructClassAddr : "",
                StructTypeName = isStructArray ? sourceField.ArrayStructType : "",
            };
            if (dataBase != 0 && sourceField.ArrayElemSize > 0)
                f.FieldAddress = $"0x{dataBase + (ulong)(elem.Index * sourceField.ArrayElemSize):X}";
            Fields.Add(f);
        }
        ApplyPendingElementScroll();
    }

    // internal (not private) so a test can drive the real seam: the geometry bug this method
    // carried (audit #5 V1) was invisible to every test that exercised the helpers in isolation.
    internal void PopulateMapContainerFields(List<ContainerElementValue> elements, LiveFieldValue sourceField)
    {
        var keyLabel = !string.IsNullOrEmpty(sourceField.MapKeyType) ? sourceField.MapKeyType : "?";
        var valLabel = !string.IsNullOrEmpty(sourceField.MapValueType) ? sourceField.MapValueType : "?";
        CurrentObjectName = sourceField.Name
            + ContainerTruncation.BadgeSuffix(elements.Count, sourceField.MapCount);
        CurrentClassName = $"Map<{keyLabel}, {valLabel}>";
        HasData = true;
        ShowCeXml = false;
        HasParent = false;
        CurrentOuterAddr = "";
        CurrentOuterName = "";
        CurrentOuterClassName = "";

        // Parse TSparseArray::Data base address for computing element addresses
        ulong dataBase = 0;
        if (!string.IsNullOrEmpty(sourceField.MapDataAddr))
            ulong.TryParse(sourceField.MapDataAddr.Replace("0x", "").Replace("0X", ""),
                System.Globalization.NumberStyles.HexNumber, null, out dataBase);
        // Geometry comes from the DLL (it is the only side that knows alignof(Key)/alignof(Value)),
        // never from a client-side re-derivation — see ContainerGeometry.
        int valOffset = ContainerGeometry.MapValueOffsetOf(sourceField);
        int stride = ContainerGeometry.MapStrideOf(sourceField);

        // Check if value type is StructProperty with navigation metadata
        bool isStructValue = sourceField.MapValueType == "StructProperty"
            && !string.IsNullOrEmpty(sourceField.MapValueStructAddr);

        Fields.Clear();
        if (elements.Count == 0)
        {
            // Show metadata summary when element data couldn't be read
            StatusText = $"Map has {sourceField.MapCount} entries but element data could not be read (key={keyLabel} sz={sourceField.MapKeySize}, val={valLabel} sz={sourceField.MapValueSize})";
        }
        foreach (var elem in elements)
        {
            var keyDisplay = !string.IsNullOrEmpty(elem.KeyPtrName) ? elem.KeyPtrName : elem.Key;
            var valDisplay = !string.IsNullOrEmpty(elem.ValuePtrName) ? elem.ValuePtrName : elem.Value;

            // Value's absolute address: entry start + aligned value offset.
            ulong valAddr = ContainerGeometry.MapValueAddress(sourceField, dataBase, elem.Index);
            var valStructAddr = (isStructValue && valAddr != 0) ? $"0x{valAddr:X}" : "";

            var f = new LiveFieldValue
            {
                Name = $"[{elem.Index}] {keyDisplay}",
                TypeName = sourceField.MapValueType,
                Offset = elem.Index * stride,
                // The row DESCRIBES THE VALUE: TypeName is the value's type and FieldAddress below
                // is the value's address, so Size must be the value's size too. It used to be the
                // whole pair, which reaches FieldValueConverter.TryConvert as the write length —
                // a TMap<int32, enum4> pair of 8 made an enum edit write 8 bytes over a 4-byte
                // value, clobbering the next element's key (audit #5 V1).
                Size = sourceField.MapValueSize,
                HexValue = !string.IsNullOrEmpty(elem.ValueHex) ? $"{elem.KeyHex} | {elem.ValueHex}" : elem.KeyHex,
                TypedValue = $"{keyDisplay} \u2192 {valDisplay}",
                // Enable → navigation for ObjectProperty values
                PtrAddress = elem.ValuePtrAddress,
                PtrName = elem.ValuePtrName,
                PtrClassName = elem.ValuePtrClassName,
                // Struct navigation for StructProperty values
                StructDataAddr = valStructAddr,
                StructClassAddr = isStructValue ? sourceField.MapValueStructAddr : "",
                StructTypeName = isStructValue ? sourceField.MapValueStructType : "",
            };
            // FieldAddress is the VALUE's address, not the element base.
            //
            // A TPair stores the key FIRST, so the element base IS the key. Every consumer of
            // FieldAddress on this row treats it as the row's TypeName — which is MapValueType:
            // the inline editor writes there (CommitFieldEditAsync), "+CE" pushes it as a record
            // typed from the value, the Hex button navigates there, and the Address column shows
            // it. Publishing the element base aimed all four at the key, so an inline edit of a
            // TMap<FName,int32> value wrote the user's 4 bytes over the FName key — silently
            // corrupting the map in a live game (audit #5 V1).
            //
            // Offset deliberately stays the ELEMENT BASE offset: MapValueDrillOffset() adds
            // valOffset back when a drill-down builds a breadcrumb, so adding it here as well
            // would double-count it in every CE/CSX pointer chain.
            if (valAddr != 0)
                f.FieldAddress = $"0x{valAddr:X}";
            Fields.Add(f);
        }
        ApplyPendingElementScroll();
    }

    /// <summary>
    /// Re-populate the container view from a (potentially refreshed) container field.
    /// Dispatches to the appropriate populate helper based on container type.
    /// </summary>
    private void RepopulateContainerView(LiveFieldValue containerField, BreadcrumbItem? bc = null)
    {
        // DataTable rows: use cached DataTableWalkResult from breadcrumb
        if (bc is { IsDataTableView: true, DataTableData: not null })
        {
            PopulateDataTableRowFields(bc.DataTableData);
        }
        else if (containerField.ArrayCount > 0 && !string.IsNullOrEmpty(containerField.ArrayInnerType))
        {
            PopulateArrayContainerFields(containerField.ArrayElements ?? new(), containerField);
        }
        else if (containerField.MapCount > 0 && !string.IsNullOrEmpty(containerField.MapKeyType))
        {
            PopulateMapContainerFields(containerField.MapElements ?? new(), containerField);
        }
        else if (containerField.SetCount > 0 && !string.IsNullOrEmpty(containerField.SetElemType))
        {
            PopulateSetContainerFields(containerField.SetElements ?? new(), containerField);
        }
    }

    /// <summary>
    /// Re-populate a path-synthetic container crumb on Back-navigation.
    ///
    /// <see cref="PathStepToBreadcrumbs"/> emits the array-field level of a
    /// Locate-in-GWorld object-pointer-array hop as a container crumb whose
    /// <see cref="BreadcrumbItem.ContainerField"/> is deliberately left null — the
    /// GWorld path step carries no TArray::Data base / element count / resolved
    /// element list, so the view cannot be rebuilt from the crumb alone. The normal
    /// container re-populate branch is gated on ContainerField != null, so without
    /// this such a crumb would fall through to a plain parent re-walk and render the
    /// PARENT object's field grid instead of the array element view (a silent
    /// mis-render — the crumb label says e.g. "SpawnedAttributes" but the grid shows
    /// the owning object). Here we lazily hydrate it: re-walk the parent object live
    /// (the crumb's Address is the parent), match the container field by name +
    /// offset (the same lookup <c>RefreshCurrentView</c> uses), and re-populate the
    /// container element view from the freshly-resolved field. Returns true if it
    /// handled the crumb; false (no live match / not a container) lets the caller
    /// fall through to the existing re-walk.
    /// </summary>
    private async Task<bool> TryRepopulateSyntheticContainerAsync(BreadcrumbItem item)
    {
        if (!item.IsContainerView || item.ContainerField != null) return false;

        var classAddr = string.IsNullOrEmpty(item.ClassAddr) ? null : item.ClassAddr;
        var result = await _dump.WalkInstanceAsync(item.Address, classAddr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit, fillGaps: FillGaps);
        result = await AutoFillGapsRetryAsync(result, item.Address, classAddr);

        var field = result.Fields.FirstOrDefault(f => f.Name == item.FieldName && f.Offset == item.FieldOffset);
        if (field == null) return false;

        // Only handle when the matched field actually resolves to a populatable
        // container (mirrors RepopulateContainerView's non-DataTable branches);
        // otherwise let the caller fall through to a normal re-walk.
        bool willRepopulate =
            (field.ArrayCount > 0 && !string.IsNullOrEmpty(field.ArrayInnerType)) ||
            (field.MapCount > 0 && !string.IsNullOrEmpty(field.MapKeyType)) ||
            (field.SetCount > 0 && !string.IsNullOrEmpty(field.SetElemType));
        if (!willRepopulate) return false;

        RepopulateContainerView(field, item);
        _log.Info($"NAV⇒SyntheticContainer rehydrated {item.FieldName} @ {item.Address} off={FormatCrumbOffset(item.FieldOffset)}");
        return true;
    }

    private void PopulateSetContainerFields(List<ContainerElementValue> elements, LiveFieldValue sourceField)
    {
        var elemLabel = !string.IsNullOrEmpty(sourceField.SetElemType) ? sourceField.SetElemType : "?";
        CurrentObjectName = sourceField.Name
            + ContainerTruncation.BadgeSuffix(elements.Count, sourceField.SetCount);
        CurrentClassName = $"Set<{elemLabel}>";
        HasData = true;
        ShowCeXml = false;
        HasParent = false;
        CurrentOuterAddr = "";
        CurrentOuterName = "";
        CurrentOuterClassName = "";

        // Parse TSparseArray::Data base address for computing element addresses
        ulong dataBase = 0;
        if (!string.IsNullOrEmpty(sourceField.SetDataAddr))
            ulong.TryParse(sourceField.SetDataAddr.Replace("0x", "").Replace("0X", ""),
                System.Globalization.NumberStyles.HexNumber, null, out dataBase);
        int stride = ContainerGeometry.SetStrideOf(sourceField);

        // Check if element type is StructProperty with navigation metadata
        bool isStructElem = sourceField.SetElemType == "StructProperty"
            && !string.IsNullOrEmpty(sourceField.SetElemStructAddr);

        Fields.Clear();
        foreach (var elem in elements)
        {
            var display = !string.IsNullOrEmpty(elem.KeyPtrName) ? elem.KeyPtrName : elem.Key;

            // Compute struct element address
            var structAddr = (isStructElem && dataBase != 0 && stride > 0)
                ? $"0x{dataBase + (ulong)(elem.Index * stride):X}" : "";

            var f = new LiveFieldValue
            {
                Name = $"[{elem.Index}]",
                TypeName = sourceField.SetElemType,
                Offset = elem.Index * stride,
                Size = sourceField.SetElemSize,
                HexValue = elem.KeyHex,
                TypedValue = display,
                // Enable → navigation for ObjectProperty elements
                PtrAddress = elem.KeyPtrAddress,
                PtrName = elem.KeyPtrName,
                PtrClassName = elem.KeyPtrClassName,
                // Struct navigation for StructProperty elements
                StructDataAddr = structAddr,
                StructClassAddr = isStructElem ? sourceField.SetElemStructAddr : "",
                StructTypeName = isStructElem ? sourceField.SetElemStructType : "",
            };
            if (dataBase != 0 && stride > 0)
                f.FieldAddress = $"0x{dataBase + (ulong)(elem.Index * stride):X}";
            Fields.Add(f);
        }
        ApplyPendingElementScroll();
    }

    /// <summary>
    /// Apply a pending "[N]" scroll hint left over after Open-from-Find-Refs's
    /// auto-drill chain. The first scroll hint (container field name) was
    /// consumed by UpdateDisplay; UpdateDisplay re-armed
    /// _pendingScrollFieldName with "[N]" before triggering NavigateToContainer
    /// so the freshly-built container Fields list scrolls to the matching
    /// element entry. Map elements use the "[N] keyDisplay" naming pattern, so
    /// we accept either an exact match or a "[N] " prefix.
    /// </summary>
    private void ApplyPendingElementScroll()
    {
        if (string.IsNullOrEmpty(_pendingScrollFieldName)) return;
        var hint = _pendingScrollFieldName;
        // Only intercept "[N]" element hints here — non-bracket hints belong
        // to the UpdateDisplay scroll path (object-instance fields).
        if (hint.Length < 3 || hint[0] != '[' || !hint.EndsWith("]")) return;

        _pendingScrollFieldName = null;
        var hit = Fields.FirstOrDefault(f =>
            f.Name == hint || f.Name.StartsWith(hint + " ", StringComparison.Ordinal));
        if (hit != null)
        {
            SelectedField = hit;
            ScrollToFieldRequested?.Invoke(hit.Name);
            _log.Info($"PopulateContainer: auto-scrolled to '{hit.Name}' (element hint '{hint}')");
        }
        else
        {
            _log.Info($"PopulateContainer: element hint '{hint}' not found");
        }
    }

    /// <summary>
    /// Post-match container drill shared by the by-name (Find Refs) and
    /// by-offset (Value Search) scroll paths in UpdateDisplay. When an
    /// element index is pending and the matched field is a navigable
    /// container, drill in and leave a "[N]" hint so the freshly-built
    /// element view scrolls to the matched entry. No-op for direct fields
    /// (pending index &lt; 0) or non-container matches.
    /// </summary>
    private void TryDrillIntoMatchedContainer(LiveFieldValue hit)
    {
        if (_pendingDrillElementIndex < 0) return;
        var elemIndex = _pendingDrillElementIndex;
        _pendingDrillElementIndex = -1;
        if (hit.IsContainerNavigable)
        {
            // Stage the element scroll hint so PopulateContainerFields
            // (called by NavigateToContainerAsync) picks it up.
            _pendingScrollFieldName = $"[{elemIndex}]";
            _log.Info($"UpdateDisplay: auto-drill into container '{hit.Name}' element [{elemIndex}]");
            _ = NavigateToContainerAsync(hit);
        }
        else
        {
            _log.Info($"UpdateDisplay: skipped auto-drill — '{hit.Name}' is not container-navigable");
        }
    }

    /// <summary>
    /// Create a container field copy retaining only the elements matching the
    /// selected synthetic fields (one or more). Extracts sparse indices from
    /// each selected field's "[N]" or "[N] description" name pattern.
    /// Used by Copy CE Field(s) export to emit one container with N filtered
    /// elements instead of N separate top-level entries — preserves CE's
    /// hierarchical structure (container header + nested elements under same
    /// pointer chain).
    /// If no selected field has a parseable sparse index, returns the whole
    /// container (preserving the original single-select fallback).
    /// </summary>
    internal static LiveFieldValue FilterContainerToElement(
        LiveFieldValue containerField, IReadOnlyList<LiveFieldValue> selectedFields)
    {
        var indices = new HashSet<int>();
        foreach (var f in selectedFields)
        {
            var idx = ParseSparseIndex(f.Name);
            if (idx.HasValue) indices.Add(idx.Value);
        }
        if (indices.Count == 0) return containerField;

        if (containerField.DataTableRowCount > 0 && containerField.DataTableRowData != null)
        {
            return new LiveFieldValue
            {
                Name = containerField.Name,
                TypeName = containerField.TypeName,
                Offset = containerField.Offset,
                Size = containerField.Size,
                DataTableRowCount = containerField.DataTableRowCount,
                DataTableStructName = containerField.DataTableStructName,
                DataTableFNameSize = containerField.DataTableFNameSize,
                DataTableStride = containerField.DataTableStride,
                DataTableRowStructAddr = containerField.DataTableRowStructAddr,
                DataTableRowData = containerField.DataTableRowData
                    .Where(r => indices.Contains(r.SparseIndex)).ToList(),
            };
        }

        if (containerField.MapCount > 0 && containerField.MapElements != null)
        {
            return new LiveFieldValue
            {
                Name = containerField.Name,
                TypeName = containerField.TypeName,
                Offset = containerField.Offset,
                Size = containerField.Size,
                MapCount = containerField.MapCount,
                MapKeyType = containerField.MapKeyType,
                MapValueType = containerField.MapValueType,
                MapKeySize = containerField.MapKeySize,
                MapValueSize = containerField.MapValueSize,
                // Geometry MUST survive the filter or the emitters silently fall back to a
                // guess: MapValueOffset was already being dropped here while the sibling clone
                // in CeXmlExportService preserved it, so exporting SELECTED map elements laid
                // out differently from exporting the same map whole (audit #5 V5).
                MapValueOffset = containerField.MapValueOffset,
                MapStride = containerField.MapStride,
                MapDataAddr = containerField.MapDataAddr,
                MapKeyStructAddr = containerField.MapKeyStructAddr,
                MapKeyStructType = containerField.MapKeyStructType,
                MapValueStructAddr = containerField.MapValueStructAddr,
                MapValueStructType = containerField.MapValueStructType,
                MapElements = containerField.MapElements.Where(e => indices.Contains(e.Index)).ToList(),
            };
        }

        if (containerField.SetCount > 0 && containerField.SetElements != null)
        {
            return new LiveFieldValue
            {
                Name = containerField.Name,
                TypeName = containerField.TypeName,
                Offset = containerField.Offset,
                Size = containerField.Size,
                SetCount = containerField.SetCount,
                SetElemType = containerField.SetElemType,
                SetElemSize = containerField.SetElemSize,
                SetStride = containerField.SetStride,
                SetDataAddr = containerField.SetDataAddr,
                SetElemStructAddr = containerField.SetElemStructAddr,
                SetElemStructType = containerField.SetElemStructType,
                SetElements = containerField.SetElements.Where(e => indices.Contains(e.Index)).ToList(),
            };
        }

        if (containerField.ArrayCount > 0 && containerField.ArrayElements != null)
        {
            return new LiveFieldValue
            {
                Name = containerField.Name,
                TypeName = containerField.TypeName,
                Offset = containerField.Offset,
                Size = containerField.Size,
                ArrayCount = containerField.ArrayCount,
                ArrayInnerType = containerField.ArrayInnerType,
                ArrayStructType = containerField.ArrayStructType,
                ArrayElemSize = containerField.ArrayElemSize,
                ArrayInnerAddr = containerField.ArrayInnerAddr,
                ArrayDataAddr = containerField.ArrayDataAddr,
                ArrayStructClassAddr = containerField.ArrayStructClassAddr,
                SoftArrayFNameSize = containerField.SoftArrayFNameSize,
                SoftArrayIsTopLevelAssetPath = containerField.SoftArrayIsTopLevelAssetPath,
                ArrayElements = containerField.ArrayElements.Where(e => indices.Contains(e.Index)).ToList(),
                ArrayEnumAddr = containerField.ArrayEnumAddr,
                ArrayEnumEntries = containerField.ArrayEnumEntries,
            };
        }

        return containerField; // fallback: emit whole container
    }

    /// <summary>Parse sparse index from "[N]" or "[N] name" patterns.</summary>
    private static int? ParseSparseIndex(string name)
    {
        if (string.IsNullOrEmpty(name) || name[0] != '[') return null;
        var endBracket = name.IndexOf(']');
        if (endBracket <= 1) return null;
        if (int.TryParse(name.Substring(1, endBracket - 1), out var index))
            return index;
        return null;
    }

    /// <summary>
    /// Check if an array inner type requires Phase D/E/F resolution (pointer names, struct fields).
    /// read_array_elements (Phase B) only handles scalars; pointer/struct arrays must use
    /// the inline elements from walk_instance which have full resolution.
    /// </summary>
    private static bool IsPointerOrStructArrayType(string innerType)
        => innerType is "ObjectProperty" or "ClassProperty"
            or "WeakObjectProperty"
            or "SoftObjectProperty" or "SoftClassProperty"
            or "LazyObjectProperty"
            or "InterfaceProperty"
            or "DelegateProperty"
            or "MulticastDelegateProperty" or "MulticastInlineDelegateProperty"
            or "StructProperty";

    /// <summary>
    /// Detect fields whose container element count exceeds the loaded element count.
    /// Returns a warning string listing the truncated fields, or null if none.
    /// </summary>
    /// <summary>
    /// Record a spine re-root in the log. The user-facing note goes to
    /// <see cref="StatusText"/> only, which reaches no log and is gone the moment the
    /// next status replaces it — so a session's logs showed the re-anchor solely as a
    /// <c>bcCount</c> SMALLER than the crumb count in the same line's <c>BC=</c> trace.
    /// That is a genuine witness, but it reads as a contradiction, and it is
    /// indistinguishable from an export rooted somewhere that legitimately has one crumb
    /// (a GameEngine root). Say it outright instead. No-op when nothing was dropped.
    /// </summary>
    private void LogReanchor(IReadOnlyList<BreadcrumbItem> before, IReadOnlyList<BreadcrumbItem> after)
    {
        if (after.Count >= before.Count) return;
        var root = after.Count > 0 ? (after[0].FieldName ?? after[0].Label) : "(none)";
        _log.Info($"CEXML re-anchored: dropped {before.Count - after.Count} offset-less hop(s); "
                + $"root is now {root} @ {(after.Count > 0 ? after[0].Address : "")} "
                + "(absolute, session-only — no pointer chain from GWorld exists)");
    }

    /// <summary>
    /// Render a breadcrumb / field offset for a LOG line. A negative value is the
    /// "this hop has no offset" sentinel (a GWorld actor-list entry, a World-Partition
    /// recovery hop), and `0x{-1:X}` prints it as <c>0xFFFFFFFF</c> — which is both
    /// meaningless as an offset and confusable with the very defect the sentinel exists
    /// to prevent (`+FFFFFFFF` in an emitted CE table). Logs are this project's primary
    /// evidence channel, so print what it MEANS.
    /// </summary>
    internal static string FormatCrumbOffset(int offset)
        => offset < 0 ? "none" : $"0x{offset:X}";

    /// <summary>
    /// One-line note for a CE export whose spine was re-rooted by
    /// <see cref="CeXmlExportService.AnchorAtLastUnchainableHop"/>. Empty when nothing
    /// was dropped, so the normal export line is unchanged.
    ///
    /// <para>The user asked for a chain from GWorld and is getting one from an absolute
    /// address instead — that is a real downgrade (it dies on the next restart) and
    /// saying so is the difference between a session-only table and a table the user
    /// thinks is restart-stable. It replaces a chain that was simply WRONG: the dropped
    /// hop had no offset, so the old export walked <c>[UWorld + 0]</c> into the world's
    /// vtable pointer.</para>
    /// </summary>
    internal static string ReanchorNote(
        IReadOnlyList<BreadcrumbItem> before, IReadOnlyList<BreadcrumbItem> after)
    {
        if (after.Count >= before.Count) return "";
        var root = after.Count > 0 ? after[0] : null;
        var name = root == null ? "the object"
                 : !string.IsNullOrEmpty(root.Label) ? root.Label
                 : root.FieldName;
        return $" ⚠ Chain re-rooted at {name} (absolute address, session-only): "
             + "it is reached through its Outer, not through a field of the world, "
             + "so no pointer chain from GWorld exists.";
    }

    private static string? BuildContainerLimitWarning(IEnumerable<LiveFieldValue> fields, int arrayLimit)
    {
        var truncated = new List<string>();
        foreach (var f in fields)
        {
            if (f.ArrayCount > arrayLimit)
            {
                int loaded = f.ArrayElements?.Count ?? 0;
                truncated.Add($"{f.Name} (Array: {f.ArrayCount} total, {loaded} loaded)");
            }
            if (f.MapCount > arrayLimit)
            {
                int loaded = f.MapElements?.Count ?? 0;
                truncated.Add($"{f.Name} (Map: {f.MapCount} total, {loaded} loaded)");
            }
            if (f.SetCount > arrayLimit)
            {
                int loaded = f.SetElements?.Count ?? 0;
                truncated.Add($"{f.Name} (Set: {f.SetCount} total, {loaded} loaded)");
            }
        }
        if (truncated.Count == 0) return null;
        return $"⚠ Container element limit ({arrayLimit}): {string.Join(", ", truncated)}";
    }

    [RelayCommand]
    private async Task NavigateToBreadcrumbAsync(BreadcrumbItem? item)
    {
        if (item == null) return;

        // Jumping to a breadcrumb shows different data — drop the field filter.
        ClearFieldSearchForNavigation();

        // Snapshot the level being left while the grid still shows it, so Forward
        // (and a later Back into it) can restore selection + scroll. Harmless when
        // the jump turns out to be a no-op (clicking the deepest crumb).
        CaptureCrumbViewState();

        try
        {
            ClearStatus();
            IsLoading = true;

            // Remove all breadcrumbs after this one
            var idx = Breadcrumbs.IndexOf(item);
            if (idx < 0) return;

            var removedCount = Breadcrumbs.Count - idx - 1;
            // A breadcrumb jump is N Backs, so the truncated crumbs go onto the
            // forward history deepest-LAST — that puts the nearest one on top, and
            // repeated Forward walks back down the way we came.
            while (Breadcrumbs.Count > idx + 1)
            {
                var dropped = Breadcrumbs[^1];
                Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
                PushForward(dropped);
            }

            _log.Info($"NAV⇒BC[{idx}] {item.FieldName ?? item.Label} removed={removedCount} | BC={FormatBreadcrumbTrace()}");

            // If navigating back to a container view, re-populate from saved field
            if (item.IsContainerView && item.ContainerField != null)
            {
                RepopulateContainerView(item.ContainerField, item);
                RestoreCrumbView(item);
                return;
            }

            // Path-synthetic container crumb (IsContainerView but no live ContainerField,
            // from PathStepToBreadcrumbs): lazily re-hydrate the container view from a live
            // parent walk instead of falling through to a parent-grid re-walk.
            if (item.IsContainerView && item.ContainerField == null
                && await TryRepopulateSyntheticContainerAsync(item))
            {
                RestoreCrumbView(item);
                return;
            }

            // If navigating back to the GWorld actor-list root, re-display the
            // actor list. A deeper crumb sharing the world address (OwningWorld)
            // is NOT the root — it falls through to a normal instance walk.
            if (IsGWorldActorListRoot(item))
            {
                PopulateFromWorld(_cachedWorld!);
                RestoreCrumbView(item);
                return;
            }

            // Re-walk this object (pass ClassAddr for StructProperty navigation)
            var classAddr = string.IsNullOrEmpty(item.ClassAddr) ? null : item.ClassAddr;
            var result = await _dump.WalkInstanceAsync(item.Address, classAddr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit, fillGaps: FillGaps);
            result = await AutoFillGapsRetryAsync(result, item.Address, classAddr);
            UpdateDisplay(result);

            RestoreCrumbView(item);
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Drop the forward history (a fresh navigation happened).</summary>
    private void ClearForwardStack()
    {
        if (_forwardStack.Count == 0) return;
        _forwardStack.Clear();
        CanGoForward = false;
    }

    /// <summary>Record a crumb that Back / a breadcrumb jump just removed, so
    /// Forward can put it back.</summary>
    private void PushForward(BreadcrumbItem crumb)
    {
        _forwardStack.Push(new ForwardStep(crumb, null));
        CanGoForward = true;
    }

    /// <summary>Record a whole spine that Back just stepped out of, so Forward can
    /// swap it back in. Sits on the SAME stack as the crumb steps so the two
    /// interleave in the order the user actually pressed Back.</summary>
    private void PushForwardSpine(SpineSnapshot spine)
    {
        _forwardStack.Push(new ForwardStep(null, spine));
        CanGoForward = true;
    }

    /// <summary>
    /// Swap the entire spine for <paramref name="crumbs"/>. Guarded, because the
    /// Clear+Add underneath would otherwise reach the CollectionChanged hook as a
    /// Reset followed by Adds — i.e. as a fresh navigation — and wipe the forward
    /// history that this very swap is a step of.
    /// </summary>
    private void ReplaceSpine(IReadOnlyList<BreadcrumbItem> crumbs)
    {
        _replayingHistory = true;
        try
        {
            Breadcrumbs.Clear();
            foreach (var bc in crumbs)
                Breadcrumbs.Add(bc);
        }
        finally { _replayingHistory = false; }
    }

    /// <summary>
    /// Snapshot the spine currently on screen so a re-rooting navigation can be
    /// undone by Back. Call BEFORE clearing the spine, and only from navigations
    /// that REPLACE the root — see <see cref="_replacedSpine"/> for the ones that
    /// deliberately don't.
    /// </summary>
    private void CaptureReplacedSpine()
    {
        if (Breadcrumbs.Count == 0) { _replacedSpine = null; return; }

        // Snapshot the view too, so the Back that restores this trail also restores
        // what was on screen. The list copy is shallow, so the capture lands on the
        // same crumb objects the restore will read.
        CaptureCrumbViewState();
        _replacedSpine = new SpineSnapshot(Breadcrumbs.ToList(), _cachedWorld);
    }

    /// <summary>
    /// One-line status telling the user that Back now leads out of a re-rooted spine.
    /// It is the ONLY affordance for it: the Back button carries no enabled-state (it
    /// is always clickable) and the breadcrumb strip shows the new spine, so without
    /// this the return path is invisible. Empty when there is nothing to go back to.
    /// </summary>
    private string ReRootedHint()
    {
        if (_replacedSpine is not { } prev || prev.Crumbs.Count == 0) return "";
        var leaf = prev.Crumbs[^1];
        var name = leaf.FieldName ?? leaf.Label;
        return string.IsNullOrEmpty(name)
            ? "← Back returns to the previous object"
            : $"← Back returns to {name}";
    }

    /// <summary>
    /// Snapshot what the user is looking at RIGHT NOW onto the current (deepest)
    /// breadcrumb, so Back / Forward / a breadcrumb jump can put it back: the rows
    /// they had selected (multi-select) and the topmost visible row as a scroll
    /// anchor.
    ///
    /// MUST run while the grid still shows THIS level — i.e. before Fields is
    /// repopulated. (IsLoading is safe to have flipped: it only shows a 2px
    /// ProgressBar, the DataGrid rows stay realised.)
    ///
    /// Reuses the bookmark carrier — <see cref="CaptureViewAnchor"/> is raised
    /// synchronously and the View fills <see cref="ViewAnchorRef.TopRow"/> before
    /// returning, so the value is readable on the next line.
    /// </summary>
    /// <param name="drilledRow">The row whose →/{}/[] button started this navigation,
    /// when there is one. Those are Buttons inside a DataGridTemplateColumn cell and
    /// Avalonia's Button marks PointerPressed handled, so clicking one need not select
    /// its row — without this the "highlight the row you drilled through on Back"
    /// affordance would be lost whenever nothing else was selected. Only consulted
    /// when the user had no selection of their own, which always wins.</param>
    private void CaptureCrumbViewState(LiveFieldValue? drilledRow = null)
    {
        if (Breadcrumbs.Count == 0) return;
        var crumb = Breadcrumbs[^1];

        // Prefer the multi-select snapshot; fall back to the single SelectedField
        // anchor, which the paths that set selection in code (match navigation,
        // element auto-scroll) update without a grid SelectionChanged round-trip;
        // then to the row that was drilled.
        var selected = _selectedFieldsSnapshot
            .Select(f => new BookmarkFieldRef(f.Name, f.Offset))
            .ToList();
        if (selected.Count == 0 && SelectedField != null)
            selected.Add(new BookmarkFieldRef(SelectedField.Name, SelectedField.Offset));
        if (selected.Count == 0 && drilledRow != null)
            selected.Add(new BookmarkFieldRef(drilledRow.Name, drilledRow.Offset));
        crumb.ViewSelectedFields = selected;

        var anchor = new ViewAnchorRef();
        CaptureViewAnchor?.Invoke(anchor);
        crumb.ViewTopRow = anchor.TopRow;
    }

    /// <summary>
    /// Put the grid back to what <paramref name="crumb"/> was showing when the user
    /// left it. Three rungs, most-faithful first:
    ///
    /// <list type="number">
    /// <item>A captured view state (multi-selection + top-row scroll anchor),
    /// replayed through the bookmark restore path — it matches rows by name+offset
    /// with a name-only fallback and silently skips rows that are no longer there.</item>
    /// <item>The legacy <see cref="BreadcrumbItem.ScrollHintFieldName"/> (the row the
    /// user clicked to drill in). Every crumb that predates a capture has only this:
    /// the synthetic spines from Locate-in-GWorld and from bookmark re-resolution,
    /// plus any crumb the user reached without leaving it through a captured path.</item>
    /// <item>Nothing — no selection, grid stays at the top. Correct when the user had
    /// nothing selected, and ALSO the automatic outcome when a Forward re-walk found
    /// the object gone: rung 1 then matches no rows and degrades to this by itself.</item>
    /// </list>
    /// </summary>
    private void RestoreCrumbView(BreadcrumbItem crumb)
    {
        if (crumb.ViewSelectedFields is { Count: > 0 } || crumb.ViewTopRow != null)
        {
            RestoreBookmarkView?.Invoke(
                crumb.ViewSelectedFields ?? new List<BookmarkFieldRef>(), crumb.ViewTopRow);
            return;
        }
        if (!string.IsNullOrEmpty(crumb.ScrollHintFieldName))
            ScrollToFieldRequested?.Invoke(crumb.ScrollHintFieldName);
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        // Cancel bookmark save mode on any navigation
        IsBookmarkSaveMode = false;

        // Back shows different (previous) data — drop the field-search filter.
        ClearFieldSearchForNavigation();

        // At the root of a spine that a RE-ROOTING navigation replaced (a bookmark
        // load, or an address re-root — the Go box / Find Refs / a cross-tab handoff):
        // step back out of it and restore the spine that was on screen before.
        //
        // This is the one Back that REPLACES the spine instead of truncating it, and
        // that is why it needs its own forward step. Clearing + refilling Breadcrumbs
        // reaches the invalidation hook as Reset-then-Add — a fresh navigation — so
        // before ReplaceSpine existed this Back silently wiped the forward entries the
        // user had just built up walking OUT of this very spine, and greyed the Forward
        // button on the one press that most obviously ought to be undoable.
        if (Breadcrumbs.Count < 2 && _replacedSpine != null)
        {
            try
            {
                ClearStatus();
                IsLoading = true;

                // Swap: the spine on screen becomes a Forward step, the saved one comes
                // back. Capture the view first so Forward re-renders this root with the
                // selection + scroll the user is leaving it with.
                CaptureCrumbViewState();
                var abandoned = new SpineSnapshot(Breadcrumbs.ToList(), _cachedWorld);
                var restore = _replacedSpine;
                _replacedSpine = null;   // consumed: the slot is one deep

                ReplaceSpine(restore.Crumbs);
                _cachedWorld = restore.CachedWorld;
                // Only offer Forward when there is something to go forward TO. The
                // spine can legitimately be EMPTY here: a re-root captures the outgoing
                // spine and clears the collection BEFORE validating the address, so a
                // typo in the Go box leaves an empty walker with a live slot. Back out
                // of that is right; a Forward back INTO an empty spine would walk the
                // "" address.
                if (abandoned.Crumbs.Count > 0)
                    PushForwardSpine(abandoned);

                // Set BEFORE the render dispatch, not after: all four render paths below
                // return early, and UpdateDisplay's freed/recycled warning must be able to
                // overwrite this rather than be overwritten by it.
                StatusText = "Returned to the previous view";
                _log.Info($"NAV←Back out of re-rooted spine | BC={FormatBreadcrumbTrace()}");

                var lastBc = Breadcrumbs.LastOrDefault();
                if (lastBc != null)
                {
                    if (lastBc.IsContainerView && lastBc.ContainerField != null)
                    {
                        RepopulateContainerView(lastBc.ContainerField, lastBc);
                        RestoreCrumbView(lastBc);
                        return;
                    }
                    if (lastBc.IsContainerView && lastBc.ContainerField == null
                        && await TryRepopulateSyntheticContainerAsync(lastBc))
                    {
                        RestoreCrumbView(lastBc);
                        return;
                    }
                    if (IsGWorldActorListRoot(lastBc))
                    {
                        PopulateFromWorld(_cachedWorld!);
                        RestoreCrumbView(lastBc);
                        return;
                    }
                    var classAddr = string.IsNullOrEmpty(lastBc.ClassAddr) ? null : lastBc.ClassAddr;
                    var result = await _dump.WalkInstanceAsync(
                        lastBc.Address, classAddr,
                        arrayLimit: ArrayLimit, previewLimit: PreviewLimit, fillGaps: FillGaps);
                    result = await AutoFillGapsRetryAsync(result, lastBc.Address, classAddr);
                    UpdateDisplay(result);
                    RestoreCrumbView(lastBc);
                }
            }
            catch (Exception ex)
            {
                // The spine has already been swapped and the forward step pushed. That
                // is CONSISTENT — what is on screen is the restored spine, and Forward
                // still offers the one we left — so unlike GoForwardAsync's optimistic
                // push there is nothing to roll back here.
                SetError(ex);
            }
            finally
            {
                IsLoading = false;
            }
            return;
        }

        if (Breadcrumbs.Count < 2) return;

        // Re-check AOBMaker CE Plugin availability (detects CE start/close, cooldown-throttled)
        TryCheckAobMaker();

        // Snapshot the level we're leaving (grid still shows it) so Forward can
        // restore its selection + scroll position, then hand the crumb to the
        // forward history. RemoveAt raises a Remove, which the invalidation hook
        // deliberately ignores.
        CaptureCrumbViewState();
        var removed = Breadcrumbs[^1];
        Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
        PushForward(removed);
        var prev = Breadcrumbs[^1];
        _log.Info($"NAV←Back removed={removed.FieldName ?? removed.Label} | BC={FormatBreadcrumbTrace()}");

        try
        {
            ClearStatus();
            IsLoading = true;

            // If going back to a container view, re-populate from saved field
            if (prev.IsContainerView && prev.ContainerField != null)
            {
                RepopulateContainerView(prev.ContainerField, prev);
                RestoreCrumbView(prev);
                return;
            }

            // Path-synthetic container crumb: re-hydrate from a live parent walk
            // (see TryRepopulateSyntheticContainerAsync) rather than re-walking to the parent grid.
            if (prev.IsContainerView && prev.ContainerField == null
                && await TryRepopulateSyntheticContainerAsync(prev))
            {
                RestoreCrumbView(prev);
                return;
            }

            // If going back to the GWorld actor-list root, re-display the actor
            // list. A deeper crumb sharing the world address (OwningWorld) is not
            // the root — it falls through to a normal instance walk below.
            if (IsGWorldActorListRoot(prev))
            {
                PopulateFromWorld(_cachedWorld!);
                RestoreCrumbView(prev);
                return;
            }

            var classAddr = string.IsNullOrEmpty(prev.ClassAddr) ? null : prev.ClassAddr;
            var result = await _dump.WalkInstanceAsync(prev.Address, classAddr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit, fillGaps: FillGaps);
            result = await AutoFillGapsRetryAsync(result, prev.Address, classAddr);
            UpdateDisplay(result);

            RestoreCrumbView(prev);
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Browser-style Forward: undo the last Back (or breadcrumb jump), whichever kind
    /// it was. A crumb step carries everything needed to re-render its level, so it is
    /// pushed back onto the spine and rendered through the same four cases Back uses;
    /// a spine step swaps the whole spine back in and re-arms
    /// <see cref="_replacedSpine"/>, so the Back that produced it stays available.
    ///
    /// Unlike Back, this re-READS the object from the game, so the level may have
    /// changed underneath us — UE can free or recycle a UObject while the user is
    /// elsewhere. That degrades in two layers: a class-name mismatch is reported and
    /// the (now meaningless) selection is dropped, and a merely-changed field list is
    /// absorbed by the restore ladder, which skips rows it can't match. Container
    /// levels re-render from cached element data and never re-read memory at all.
    /// </summary>
    [RelayCommand]
    private async Task GoForwardAsync()
    {
        if (_forwardStack.Count == 0) return;

        // Cancel bookmark save mode on any navigation (mirrors Back).
        IsBookmarkSaveMode = false;

        // Forward shows different (next) data — drop the field-search filter.
        ClearFieldSearchForNavigation();

        // Re-check AOBMaker CE Plugin availability (detects CE start/close, cooldown-throttled)
        TryCheckAobMaker();

        // Snapshot the level we're leaving so an immediate Back returns to it intact.
        CaptureCrumbViewState();

        var step = _forwardStack.Pop();
        CanGoForward = _forwardStack.Count > 0;

        BreadcrumbItem next;
        if (step.Spine is { } spine)
        {
            // Spine step — the mirror image of the Back that produced it. What is on
            // screen goes back into the one-deep re-root slot so that Back can step
            // out again, and the saved spine is swapped in whole. Nothing is appended:
            // these crumbs do not belong to the spine we are leaving.
            _replacedSpine = new SpineSnapshot(Breadcrumbs.ToList(), _cachedWorld);
            ReplaceSpine(spine.Crumbs);
            _cachedWorld = spine.CachedWorld;

            next = Breadcrumbs.LastOrDefault() ?? new BreadcrumbItem();
            _log.Info($"NAV→Fwd spine ({spine.Crumbs.Count} crumbs) left={_forwardStack.Count} | BC={FormatBreadcrumbTrace()}");
        }
        else
        {
            next = step.Crumb!;
            // Re-pushing a crumb is a replay, not a fresh navigation — suppress the
            // CollectionChanged invalidation hook so the REST of the forward history
            // survives and the user can keep going forward.
            _replayingHistory = true;
            try { Breadcrumbs.Add(next); }
            finally { _replayingHistory = false; }

            _log.Info($"NAV→Fwd {next.FieldName ?? next.Label} left={_forwardStack.Count} | BC={FormatBreadcrumbTrace()}");
        }

        try
        {
            ClearStatus();
            IsLoading = true;

            // Container view with a live field: re-render from the cached elements
            // (no memory re-read, so this case cannot go stale).
            if (next.IsContainerView && next.ContainerField != null)
            {
                RepopulateContainerView(next.ContainerField, next);
                RestoreCrumbView(next);
                return;
            }

            // Path-synthetic container crumb: re-hydrate from a live parent walk.
            if (next.IsContainerView && next.ContainerField == null
                && await TryRepopulateSyntheticContainerAsync(next))
            {
                RestoreCrumbView(next);
                return;
            }

            if (IsGWorldActorListRoot(next))
            {
                PopulateFromWorld(_cachedWorld!);
                RestoreCrumbView(next);
                return;
            }

            var classAddr = string.IsNullOrEmpty(next.ClassAddr) ? null : next.ClassAddr;
            var result = await _dump.WalkInstanceAsync(next.Address, classAddr,
                arrayLimit: ArrayLimit, previewLimit: PreviewLimit, fillGaps: FillGaps);
            result = await AutoFillGapsRetryAsync(result, next.Address, classAddr);
            UpdateDisplay(result);

            // Honest degrade (same check the bookmark load path uses): a class-name
            // mismatch means the address now holds a DIFFERENT object, so restoring
            // the old selection would dress up wrong data as a successful return.
            // TargetClassName is empty on crumbs pushed before the class was known,
            // so only compare when we actually have one.
            if (!string.IsNullOrEmpty(next.TargetClassName)
                && !string.Equals(CurrentClassName, next.TargetClassName, StringComparison.Ordinal))
            {
                StatusText = "Forward: object changed — selection not restored";
                _log.Warn($"NAV→Fwd class mismatch at {next.Address}: " +
                          $"expected {next.TargetClassName}, got {CurrentClassName}");
                return;
            }

            RestoreCrumbView(next);
        }
        catch (Exception ex)
        {
            // Undo the optimistic move. Back can afford to leave its spine truncated
            // on failure — that spine is still CONSISTENT — but Forward would leave a
            // level sitting on the spine that never rendered. Put the user back where
            // they pressed the button, with the step still available to retry.
            if (step.Spine is { } failedSpine)
            {
                // Spine step: the swap already happened and _replacedSpine holds the
                // spine we came from, so putting it back is the same swap reversed.
                if (_replacedSpine is { } cameFrom)
                {
                    ReplaceSpine(cameFrom.Crumbs);
                    _cachedWorld = cameFrom.CachedWorld;
                }
                _replacedSpine = null;
                PushForwardSpine(failedSpine);
            }
            else if (Breadcrumbs.Count > 0 && ReferenceEquals(Breadcrumbs[^1], next))
            {
                // (Remove doesn't trip the invalidation hook; the re-push is guarded
                // for symmetry with the Add above.)
                _replayingHistory = true;
                try { Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1); }
                finally { _replayingHistory = false; }
                PushForward(next);
            }
            SetError(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task GoToParentAsync()
    {
        if (string.IsNullOrEmpty(CurrentOuterAddr) || CurrentOuterAddr == "0x0") return;

        // Parent (Outer) is a different object — drop the field-search filter.
        ClearFieldSearchForNavigation();

        // Snapshot the level being left so a Back from the parent restores the
        // selection + scroll position instead of landing on a blank grid.
        CaptureCrumbViewState();

        try
        {
            ClearStatus();
            IsLoading = true;

            // Navigate to the parent (OuterPrivate) object
            var parentAddr = CurrentOuterAddr;

            // Add current object as a breadcrumb before navigating up
            // so user can go back down via breadcrumbs
            Breadcrumbs.Add(new BreadcrumbItem
            {
                Address = parentAddr,
                Label = !string.IsNullOrEmpty(CurrentOuterName) ? CurrentOuterName : "Parent",
                IsPointerDeref = true,
                FieldOffset = 0,
                FieldName = "Outer",
            });

            var result = await _dump.WalkInstanceAsync(parentAddr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit, fillGaps: FillGaps);
            result = await AutoFillGapsRetryAsync(result, parentAddr);
            UpdateDisplay(result);
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to navigate to parent {CurrentOuterAddr}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // === Find References (reverse pointer scan) ===
    //
    // OuterPrivate / Parent gives the naming-hierarchy parent (often
    // /Engine/Transient for runtime-spawned objects), not the logical
    // gameplay owner. Find References reverse-scans every UObject for
    // pointers to the current one, surfacing answers like "this Item is
    // PlayerInventory.Items[3]". Results render in a panel above the
    // field grid; user clicks Open to navigate to that owner.

    [ObservableProperty] private ObservableCollection<ReferenceMatch> _references = new();
    [ObservableProperty] private bool _hasReferences;
    [ObservableProperty] private string _referencesHeader = "";

    // Optional scroll-to hint applied once the next WalkInstance result
    // populates the Fields collection. Used by Open-from-references so the
    // user lands directly on the field that holds the pointer.
    private string? _pendingScrollFieldName;

    // Value Search cross-nav focus: the owning property's byte offset to
    // scroll to once the next walk result populates Fields. Field NAMES
    // aren't unique (inherited members, map .Key/.Value), so Value Search
    // matches the row by OFFSET; the by-name hint above stays for Find Refs.
    private int? _pendingScrollFieldOffset;

    // Optional auto-drill index applied alongside _pendingScrollFieldName.
    // When >= 0 and the resolved field is container-navigable, the post-
    // load handler navigates into the container view AND sets a follow-up
    // scroll hint for the element entry "[N]" so Open-from-Find-Refs lands
    // directly on the matched element instead of stopping at the container.
    private int _pendingDrillElementIndex = -1;

    [RelayCommand]
    private async Task FindReferencesAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress) || CurrentAddress == "0x0") return;

        // Snapshot WHAT this scan is about, before the await. find_refs_to_uobject runs on
        // the BULK lane (LaneRoutingPipeClient) while walk_instance runs on the interactive
        // one, with no ordering between them, and the DLL-side scan has a 30-second
        // deadline — so the user can be somewhere else entirely by the time it lands.
        // Nothing gates the panel meanwhile: IsLoading is bound only to a ProgressBar's
        // IsVisible, never to IsEnabled.
        //
        // The header must be composed from THIS snapshot, not from live VM state, or it
        // reads "References to <wherever you are now>" over the rows of the object you
        // scanned. That is not cosmetic: Open on such a row pre-arms the scroll hint from
        // the referring field and re-roots the walker, so the user gets a real navigation
        // into an object that references something else entirely.
        var scanAddr   = CurrentAddress;
        var scanName   = CurrentObjectName;
        var scanCrumb  = CurrentCrumb;

        try
        {
            ClearStatus();
            IsLoading = true;
            StatusText = "Searching for references…";

            var result = await _dump.FindReferencesToUObjectAsync(scanAddr);

            // Address alone is NOT an identity here: a container drill pushes a crumb and
            // changes CurrentObjectName while leaving CurrentAddress untouched, which is
            // exactly the "References to Items" mislabel. Compare the crumb by reference.
            if (CurrentAddress != scanAddr || !ReferenceEquals(CurrentCrumb, scanCrumb))
            {
                StatusText = $"Reference scan for {scanName} finished, but you navigated away — results discarded.";
                _log.Info($"FindReferences: {scanAddr} -> {result.References.Count} matches DISCARDED (walker moved)");
                return;
            }

            References.Clear();
            foreach (var r in result.References)
                References.Add(r);
            HasReferences = References.Count > 0;

            string scanSuffix = "";
            if (result.Scan is { } cs && cs.ObjectsTotal > 0)
            {
                scanSuffix = cs.DeadlineHit
                    ? $"  [scanned {cs.ObjectsScanned}/{cs.ObjectsTotal} in {cs.DurationMs}ms — DEADLINE HIT, retry to continue]"
                    : $"  [scanned {cs.ObjectsScanned}/{cs.ObjectsTotal} in {cs.DurationMs}ms]";
            }

            if (HasReferences)
            {
                ReferencesHeader = $"References to {scanName} ({References.Count})" + scanSuffix;
                StatusText = $"Found {References.Count} reference(s)" + scanSuffix;
                _log.Info($"FindReferences: {scanAddr} -> {References.Count} matches{scanSuffix}");
            }
            else
            {
                ReferencesHeader = $"References to {scanName} (none found)" + scanSuffix;
                HasReferences = true;  // Show empty panel so user sees scan completed
                StatusText = "No references found — likely held by a non-reflected pointer (TUniquePtr / raw pointer / non-UObject struct)" + scanSuffix;
                _log.Info($"FindReferences: {scanAddr} -> 0 matches{scanSuffix}");
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"FindReferences failed for {scanAddr}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearReferences()
    {
        References.Clear();
        HasReferences = false;
        ReferencesHeader = "";
    }

    [RelayCommand]
    private void ShowRelatedObjects()
    {
        if (string.IsNullOrEmpty(CurrentAddress) || CurrentAddress == "0x0") return;
        NavigateToRelatedObjects?.Invoke(CurrentAddress);
    }

    [RelayCommand]
    private async Task OpenReferenceOwnerAsync(ReferenceMatch? match)
    {
        if (match == null || string.IsNullOrEmpty(match.OwnerAddress)) return;

        // Pre-arm the scroll hint so when the new owner's Fields list
        // populates we auto-select the field that held the pointer.
        var firstSegment = (match.FieldName ?? "").Split('.')[0];
        _pendingScrollFieldName = string.IsNullOrEmpty(firstSegment) ? null : firstSegment;

        // Container hits (Array/Map/Set element) — pre-arm the drill index
        // so the post-load handler also navigates into the container view
        // and lands on element "[N]". Only auto-drill when the FieldName
        // refers DIRECTLY to the container (or "<Container>.Key" /
        // "<Container>.Value" for map-side hits) — nested struct paths
        // like "Stats.Equipment" require a manual struct drill the user
        // has to do themselves, so we don't auto-drill those.
        var fieldName = match.FieldName ?? "";
        var canAutoDrill = match.ElementIndex >= 0
            && !string.IsNullOrEmpty(firstSegment)
            && (fieldName == firstSegment
                || fieldName == firstSegment + ".Key"
                || fieldName == firstSegment + ".Value");
        _pendingDrillElementIndex = canAutoDrill ? match.ElementIndex : -1;

        await NavigateToAddressAsync(match.OwnerAddress);

        // Status hint so the user knows where to look on the new page (the field
        // that's holding the pointer), plus the way back out of the re-root.
        //
        // Set AFTER the navigation, not before: NavigateToAddressAsync opens with
        // ClearStatus(), so the hint this method used to set never survived to the
        // screen — it was written and wiped within the same click.
        if (Breadcrumbs.Count > 0)
            StatusText = BuildOpenedRefStatus(match);
    }

    /// <summary>Status line for an Open-from-Find-Refs landing: where the pointer was
    /// held, then how to get back.</summary>
    private string BuildOpenedRefStatus(ReferenceMatch match)
    {
        var where = $"Opened {match.OwnerName} — held the previous object in '{match.FieldName}'"
                  + (match.ElementIndex >= 0 ? $"[{match.ElementIndex}]" : "");
        var back = ReRootedHint();
        return string.IsNullOrEmpty(back) ? where : $"{where}  ·  {back}";
    }

    [RelayCommand]
    private async Task NavigateToAddressAsync(string? addr)
    {
        if (string.IsNullOrEmpty(addr)) return;

        // Drop stale Find Refs auto-drill state if a different navigation
        // path takes over before the chained drill kicks in. Guarded:
        // OpenReferenceOwnerAsync sets _pendingDrillElementIndex *before*
        // calling NavigateToAddressAsync, so we mustn't clobber it on the
        // call this command receives from that method. Detection: the
        // pending hint set by OpenReferenceOwnerAsync is non-empty.
        // NavigateToInstanceFieldAsync (Value Search) pre-arms the same
        // drill index alongside _pendingScrollFieldOffset, so preserve it
        // for that path too.
        if (string.IsNullOrEmpty(_pendingScrollFieldName) && !_pendingScrollFieldOffset.HasValue)
            _pendingDrillElementIndex = -1;

        try
        {
            ClearStatus();
            IsLoading = true;
            StopAutoRefreshTimer();
            // This is a RE-ROOT: the spine below is about to be thrown away wholesale,
            // not truncated, so Back has nothing to pop and used to do nothing at all.
            // Hand the outgoing spine to the one-deep re-root slot and Back can put the
            // user back where they were — which matters most on the paths that reach
            // here from a single click: the Find Refs owner drill, and every cross-tab
            // "Open in Live Walker" handoff.
            CaptureReplacedSpine();
            IsBookmarkSaveMode = false;
            Breadcrumbs.Clear();
            // Stale references panel from a previous lookup target shouldn't
            // hang around when we navigate elsewhere — references are
            // about the now-current UObject, not the new one.
            References.Clear();
            HasReferences = false;

            // Normalize address: supports CE formats like "module.exe"+offset,
            // quoted module names ("module.exe"+offset), and plain hex.
            // Strict validation — garbage like "0xlkaskdlaj" surfaces as a clean
            // status message instead of silently navigating to address 0.
            if (!AddressHelper.TryNormalizeAddress(addr, _engineState?.ModuleBase, out var normalizedAddr))
            {
                StatusText = "Invalid address — expected hex (e.g. 0x7FF... or module.exe+RVA)";
                return;
            }

            await NavigateToAsync(normalizedAddr, "Custom", 0, "Custom", isPointer: true,
                                  // Go box / bookmark / cross-tab handoff: re-roots via
                                  // Breadcrumbs.Clear() above, so no parent is expected.
                                  expectedParent: null);
            StatusText = ReRootedHint();
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to navigate to {addr}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Navigate to a UObject instance and focus the field that produced a
    /// Value Search candidate. The owning property row is matched by byte
    /// OFFSET (field names aren't unique — inherited members + map
    /// .Key/.Value collide); if the candidate is a container element (display
    /// name ends in "[N]") the matched container is drilled and the element
    /// row [N] is selected. Falls back to a plain navigation when the field
    /// can't be located as a top-level row (e.g. a hit inside a nested
    /// struct, which the user must drill manually — same as Find Refs).
    /// </summary>
    public async Task NavigateToInstanceFieldAsync(string? addr, int fieldOffset, string? fieldName)
    {
        if (string.IsNullOrEmpty(addr)) return;
        // A container path (e.g. "Cargo[3].ItemId" or the deep
        // "SaveSlotList[0]...Tunes[2]") can't be reached by the single
        // offset/element pending-scroll — deep candidates carry fieldOffset=0,
        // which would otherwise mis-select the first offset-0 field. Walk the
        // owner then drill the full path explicitly (shared with LocateInGWorld).
        if (TryParseContainerPath(fieldName, out var pathSegs))
        {
            _pendingScrollFieldOffset = null;
            _pendingScrollFieldName = null;
            _pendingDrillElementIndex = -1;
            await NavigateToAddressAsync(addr);
            await DrillDisplayPathAsync(pathSegs);
            return;
        }
        // Pre-arm focus state BEFORE navigating; the post-walk UpdateDisplay
        // handler consumes it once Fields is populated. (Mirrors how
        // OpenReferenceOwnerAsync pre-arms the by-name hint for Find Refs.)
        _pendingScrollFieldOffset = fieldOffset;
        _pendingDrillElementIndex = ParseElementIndexSuffix(fieldName ?? "");
        await NavigateToAddressAsync(addr);
    }

    /// <summary>
    /// "Locate in GWorld": compute the shortest pointer chain from the live
    /// UWorld down to <paramref name="objectAddr"/> (the owning UObject), then
    /// REPLACE the breadcrumb spine with that path and land on the target.
    ///
    /// <paramref name="stopAtParent"/> distinguishes the two behaviours:
    ///   • false (land ON the target — Value Search / Snapshot / SPC AND the
    ///     Instance Finder selected object): build the full GWorld→…→target
    ///     spine, open the target node, and (for a container VALUE) scroll to /
    ///     drill the value field (<paramref name="scrollFieldOffset"/> /
    ///     <paramref name="scrollFieldName"/> "[N]").
    ///   • true  (stop at the PARENT — Interesting Funcs "where do instances of
    ///     this class live"): drop the final node, land on the holder, and
    ///     highlight the pointer field that leads to the target.
    ///
    /// On failure the reason is surfaced via <see cref="LocateFailureMessage"/> as a
    /// prominent in-grid banner (a user-initiated cancel keeps the current view and
    /// uses the mild top status line instead).
    /// </summary>
    /// <summary>Depth below which deep objects (e.g. GAS attributes, ~7 hops from
    /// GWorld — verified TQ2) are likely missed, so a locate at a smaller depth
    /// warrants a heads-up before it runs.</summary>
    private const int RecommendedGWorldLocateDepth = 7;

    /// <summary>Session-scoped: the low-depth locate warning is shown at most once
    /// (firing on every locate would be noise — the default depth resolves most
    /// objects). Reset only by restarting the app.</summary>
    private bool _lowDepthLocateWarned;

    public async Task LocateInGWorldAsync(string? objectAddr, int scrollFieldOffset,
                                          string? scrollFieldName, bool stopAtParent,
                                          CancellationToken ct = default, string rootKind = "gworld")
    {
        if (string.IsNullOrEmpty(objectAddr)) return;

        // Proactive heads-up (once per session) when the depth is below where deep
        // objects typically sit — the BFS is depth-bounded, so a too-small depth
        // silently misses reachable-but-deep targets (GAS attributes are ~7 hops;
        // verified TQ2: found at depth 8, missed at depth 5). Ask before wasting a
        // search; the user can Cancel to raise the depth / enable Deep first.
        if (GWorldLocateDepth < RecommendedGWorldLocateDepth && !_lowDepthLocateWarned)
        {
            _lowDepthLocateWarned = true;   // set first so a re-entrant locate can't re-ask
            bool proceed = await UE5DumpUI.Views.ConfirmDialog.ShowAsync(
                "Locate depth may be too small",
                $"'Locate in GWorld depth' is set to {GWorldLocateDepth}. The search is "
                + $"depth-bounded, so deeper objects — e.g. GAS attributes sit ~{RecommendedGWorldLocateDepth} "
                + "hops from GWorld — may not be found at this depth. Raise 'Locate in GWorld depth' "
                + "in Options ⚙ (and/or enable Deep) for a better chance, or proceed anyway at depth "
                + $"{GWorldLocateDepth}?",
                confirmText: "Proceed anyway",
                cancelText: "Cancel");
            if (!proceed) return;
        }

        // User-facing label for the chosen BFS root ("GWorld" vs "GameEngine").
        string rootLabel = rootKind == "engine" ? "GameEngine" : "GWorld";
        LocateFailureTitle = $"Locate in {rootLabel} failed";
        try
        {
            ClearStatus();
            IsLoading = true;
            StopAutoRefreshTimer();
            _replacedSpine = null;   // Locate re-spines the SAME object — see _replacedSpine
            IsBookmarkSaveMode = false;

            var path = await _dump.FindPathFromGWorldAsync(objectAddr, objectAddr, GWorldLocateDepth, ct, rootKind,
                GWorldLocateDeep, GWorldLocateDeep ? kGWorldDeepContainerDepth : 1);

            if (!path.Found)
            {
                if (path.Status == "cancelled")
                {
                    // User-initiated cancel — preserve the current view and report
                    // it as a mild top-line status (not a failure banner).
                    StatusText = GWorldPathFailureStatus(path, rootLabel);
                    return;
                }
                // Don't leave the previous object on screen as if it were the
                // result — clear it and raise a prominent failure banner so the
                // empty grid doesn't read as "nothing loaded yet".
                ClearDisplayedNode();
                LocateFailureMessage = GWorldPathFailureStatus(path, rootLabel);
                return;
            }

            BuildBreadcrumbSpineFromPath(path, objectAddr, stopAtParent, rootKind);

            // Value inside a container path (Value Search / SPC "Array[N]...[M]",
            // e.g. "SaveSlotList[1].GP" or the deep
            // "SaveSlotList[0].MsTuneData.MsTunes[0].WeaponTuneList[0].Tunes[2]"):
            // the single-shot pending-scroll can't chain container → element →
            // inner field across multiple "[N]" levels, so drill the full path
            // explicitly after reaching the owner — parity with the Instance
            // Finder structured-chain deep-drill.
            // requireIndex:false so a pure nested-struct path (camera POV fields:
            // "CameraCachePrivate.POV.Location") also drills, not just container "[N]".
            if (!stopAtParent && TryParseContainerPath(scrollFieldName, out var pathSegs, requireIndex: false))
            {
                _pendingScrollFieldOffset = null;
                _pendingScrollFieldName = null;
                _pendingDrillElementIndex = -1;

                var ownerAddr = Breadcrumbs[^1].Address;
                var ownerResult = await _dump.WalkInstanceAsync(ownerAddr, arrayLimit: ArrayLimit,
                                                                previewLimit: PreviewLimit, fillGaps: FillGaps, ct: ct);
                ownerResult = await AutoFillGapsRetryAsync(ownerResult, ownerAddr);
                UpdateDisplay(ownerResult);

                bool landed = await DrillDisplayPathAsync(pathSegs);
                // Fall back to the byte-offset scroll ONLY when the drill failed
                // WITHOUT navigating away — i.e. the first path segment isn't a field
                // on THIS object, so we're still on the owner. That's the cross-object
                // case (P4): the field name is the path FROM THE CANDIDATE to the owner
                // (e.g. "GameCharacters[0].MP" on a manager — the owner IS the find_path
                // target and holds MP as a DIRECT field at scrollFieldOffset). If the
                // drill instead navigated partway in before failing (e.g. a single-value
                // deep path whose container reallocated), we're no longer on the owner,
                // so skip the fallback rather than land on an unrelated row.
                if (!landed && Breadcrumbs.Count > 0 && Breadcrumbs[^1].Address == ownerAddr)
                    landed = ScrollToFieldByOffset(scrollFieldOffset);
                _log.Info($"LocateIn{rootLabel}: reach+container-path-drill, {path.Depth} hop(s), landed={landed} | BC={FormatBreadcrumbTrace()}");
                StatusText = (landed
                    ? $"Located via {rootLabel} — {path.Depth} hop(s) to {path.TargetName}; landed on {scrollFieldName}."
                    : $"Located via {rootLabel} — {path.Depth} hop(s) to {path.TargetName} (drill into {scrollFieldName} manually).")
                    + GWorldViaLevelNote(path);
                return;
            }

            // Decide the field to scroll/highlight once the display node is walked.
            if (stopAtParent)
            {
                // Highlight the pointer on the parent that leads to the target,
                // but do NOT auto-drill into it (stop before the class).
                if (path.Steps.Count > 0)
                {
                    _pendingScrollFieldOffset = path.Steps[^1].FieldOffset;
                    _pendingDrillElementIndex = -1;
                }
            }
            else
            {
                // Land on the value field inside the owning object (auto-drill
                // into a container element when the field name carried a "[N]").
                _pendingScrollFieldOffset = scrollFieldOffset;
                _pendingDrillElementIndex = ParseElementIndexSuffix(scrollFieldName ?? "");
            }

            var displayAddr = Breadcrumbs[^1].Address;
            var result = await _dump.WalkInstanceAsync(displayAddr, arrayLimit: ArrayLimit,
                                                       previewLimit: PreviewLimit, fillGaps: FillGaps, ct: ct);
            result = await AutoFillGapsRetryAsync(result, displayAddr);
            UpdateDisplay(result);

            _log.Info($"LocateIn{rootLabel}: {(stopAtParent ? "parent" : "reach")} mode, {path.Depth} hop(s), " +
                      $"visited {path.Visited}, {path.DurationMs}ms | BC={FormatBreadcrumbTrace()}");

            StatusText = (stopAtParent
                ? $"Located via {rootLabel} — {path.Depth} hop(s); parent of {path.TargetName} ({path.TargetClass})."
                : $"Located via {rootLabel} — {path.Depth} hop(s) to {path.TargetName} ({path.TargetClass}).")
                + GWorldViaLevelNote(path);
        }
        catch (Exception ex)
        {
            // Surface the exception prominently (ErrorMessage isn't bound in this
            // panel — without this a thrown locate would be invisible to the user).
            ClearDisplayedNode();
            LocateFailureMessage = $"Locate in {rootLabel} failed: {ex.Message}";
            SetError(ex);
            _log.Error($"LocateIn{rootLabel} failed for {objectAddr}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Suffix noting a streaming/World-Partition recovery — the world→level
    /// hop was reached via ULevel::OwningWorld (a back-reference), not a forward
    /// static pointer, so the chain isn't a clean CE pointer chain.</summary>
    private static string GWorldViaLevelNote(GWorldPathResult path) =>
        path.Status == "ok_via_level"
            ? " (via the world's level list — streaming/WP actor; the world→level hop is a back-reference, not a static pointer)"
            : "";

    /// <summary>Map a failed path search to an actionable status message.
    /// <paramref name="rootLabel"/> is "GWorld" (default) or "GameEngine" — it tailors
    /// the not_reachable explanation, since an engine root reaches engine-layer objects
    /// but NOT most world actors (the level-list recovery is World-root only).</summary>
    private string GWorldPathFailureStatus(GWorldPathResult path, string rootLabel = "GWorld") => path.Status switch
    {
        // not_reachable means the DEPTH-BOUNDED BFS didn't find the target within
        // `GWorldLocateDepth` hops — it only explores nodes up to that depth, NOT the
        // whole reachable graph (verified in-game: depth 5 → not_reachable/visited
        // 57,658, depth 8 → found at 7 hops/visited 130,860). So the target is often
        // simply DEEPER than the current depth: raising 'Locate in GWorld depth' (and/or
        // enabling Deep, which follows container struct-element pointers the normal walk
        // skips) can make it reachable. The DLL doesn't distinguish a depth-capped miss
        // from a truly-exhausted frontier, so we suggest the depth/Deep knobs first
        // rather than claiming the object doesn't exist.
        "not_reachable"  => rootLabel == "GameEngine"
            ? $"Not reachable from GameEngine within depth {GWorldLocateDepth} — no forward chain from the engine to this object was found (searched {path.Visited:N0}). Try raising 'Locate in GWorld depth' in Options ⚙ and/or enabling 'Deep (nested containers)'. Note: an engine root reaches engine / GameInstance / LocalPlayer / subsystems best; most WORLD actors are easier via 🌍 Locate in GWorld (its level-list recovery is World-root only)."
            : $"Not reachable within depth {GWorldLocateDepth} — no forward pointer chain from GWorld to this object was found yet (searched {path.Visited:N0} objects), and it isn't in a level's actor list. It may be DEEPER than {GWorldLocateDepth} hops: raise 'Locate in GWorld depth' in Options ⚙, and/or enable 'Deep (nested containers)'. Still nothing? A streaming/World-Partition actor appears once loaded/aggro'd — or use 🔗 Related from an Instance Finder hit, or Find Refs to find a holder.",
        "deadline"       => $"{rootLabel} path search timed out at depth {GWorldLocateDepth} (visited {path.Visited:N0}). Try a smaller depth.",
        "visited_cap"    => $"{rootLabel} path search space too large at depth {GWorldLocateDepth} (visited {path.Visited:N0}). Try a smaller depth.",
        "cancelled"      => $"{rootLabel} path search cancelled.",
        "no_gworld"      => "GWorld is not available (AOB scan found no UWorld).",
        "no_engine"      => "The live UGameEngine could not be resolved (no active engine with GameViewport / GameInstance).",
        "invalid_target" => "Could not resolve the target object in GObjects.",
        _                => $"No {rootLabel} path found ({path.Status}).",
    };

    /// <summary>"Locate in GameEngine": the same forward path search as
    /// <see cref="LocateInGWorldAsync"/> but rooted at the live UGameEngine instead of
    /// GWorld. Reaches engine-layer objects (GameInstance / LocalPlayer / engine
    /// subsystems) the GWorld graph never touches — but is NOT a superset: an engine
    /// root sits one hop above the world, so streaming / World-Partition actors
    /// (recovered for a World root via ULevel::OwningWorld) are typically
    /// not_reachable from here.</summary>
    public Task LocateInGameEngineAsync(string? objectAddr, int scrollFieldOffset,
                                        string? scrollFieldName, bool stopAtParent,
                                        CancellationToken ct = default)
        => LocateInGWorldAsync(objectAddr, scrollFieldOffset, scrollFieldName, stopAtParent,
                               ct, rootKind: "engine");

    /// <summary>"Locate in GameEngine" for a container-match value — the engine-rooted
    /// counterpart of <see cref="LocateContainerInGWorldAsync"/>.</summary>
    public Task LocateContainerInGameEngineAsync(ContainerMatch match, CancellationToken ct = default)
        => LocateContainerInGWorldAsync(match, ct, rootKind: "engine");

    /// <summary>
    /// Replace the breadcrumb spine with the GWorld→target path: a GWorld root
    /// node + one node per hop. <paramref name="stopAtParent"/> drops the final
    /// (target) node so the view lands on the parent pointer.
    /// </summary>
    private void BuildBreadcrumbSpineFromPath(GWorldPathResult path, string objectAddr, bool stopAtParent,
                                              string rootKind = "gworld")
    {
        int stepCount = path.Steps.Count;
        int includedSteps = stopAtParent ? Math.Max(0, stepCount - 1) : stepCount;

        Breadcrumbs.Clear();
        References.Clear();
        HasReferences = false;

        // The root crumb's FieldName is the re-resolution ANCHOR marker, not a real
        // field: "GWorld" (live UWorld) vs "GameEngine" (live UGameEngine). It MUST
        // reflect the BFS root so a bookmark saved from this spine re-resolves against
        // the right anchor after a game restart (see TryReresolveBookmarkSpineAsync) —
        // an engine-rooted spine mislabeled "GWorld" would re-walk the wrong object and
        // fall back to stale addresses. Label keeps the human-facing root name.
        bool engineRoot = rootKind == "engine";
        Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = !string.IsNullOrEmpty(path.RootAddr) ? path.RootAddr : objectAddr,
            Label = !string.IsNullOrEmpty(path.RootName) ? path.RootName : (engineRoot ? "GameEngine" : "GWorld"),
            IsPointerDeref = true,
            FieldOffset = 0,
            FieldName = engineRoot ? "GameEngine" : "GWorld",
        });

        for (int i = 0; i < includedSteps; i++)
            foreach (var bc in PathStepToBreadcrumbs(path.Steps[i]))
                Breadcrumbs.Add(bc);
    }

    /// <summary>
    /// Convert one GWorld-path hop into the breadcrumb level(s) it represents.
    ///
    /// A container element hop crosses TWO dereferences — deref the container field
    /// (the <c>TArray::Data</c> / <c>TSparseArray::Data</c> pointer sits at
    /// <c>field+0</c>), then deref the element pointer at its computed offset within
    /// that buffer — but the DLL path collapses both into ONE hop (FieldOffset =
    /// container field, ElementIndex = the element). Emitting it as a single
    /// breadcrumb makes the CE chain stop at the Data buffer and apply the next
    /// field's offset to IT instead of to the element's target object (wrong
    /// addresses for everything below). So such a hop is split into TWO crumbs — a
    /// container crumb (deref Data) + an element crumb (deref the element pointer) —
    /// matching what manual navigation produces.
    ///
    /// Element geometry: object/class arrays use the implicit 8-byte <c>UObject*</c>
    /// stride; Map/Set hops carry their element stride + the map value's within-pair
    /// offset on the step (<see cref="GWorldPathStep.ElemStride"/> /
    /// <see cref="GWorldPathStep.ElemValueOffset"/>) so the element offset is
    /// <c>ElementIndex*stride + valueOffset</c>. We only split when the stride is
    /// known; struct arrays and other inner types keep the single crumb.
    /// </summary>
    internal static IReadOnlyList<BreadcrumbItem> PathStepToBreadcrumbs(GWorldPathStep s)
    {
        var label = !string.IsNullOrEmpty(s.ToName) ? s.ToName
                  : (!string.IsNullOrEmpty(s.FieldName) ? s.FieldName : "(node)");

        // Synthetic back-reference hops from the streaming/World-Partition recovery
        // (Aura::RecoverViaWorldLevel). Neither is a forward static pointer, so both
        // are plain navigation anchors (navigate by Address) — NOT pointer derefs.
        // Marking them non-deref keeps CE export from fabricating an offset for a hop
        // that has none.
        //   WorldLevel — world → level, reached via ULevel::OwningWorld.
        //   LevelActor — level → actor, reached via the actor's Outer. Synthetic for
        //                the same reason (audit #5 F8): ULevel::Actors carries no
        //                UPROPERTY, so there is no reflected offset OR element index
        //                to publish, and the array lookup that used to produce them
        //                is what made this whole recovery unreachable.
        if (s.FieldType == "WorldLevel" || s.FieldType == "LevelActor")
        {
            return new[]
            {
                new BreadcrumbItem
                {
                    Address = s.To,
                    Label = label,
                    FieldOffset = -1,
                    FieldName = s.FieldType == "LevelActor" ? "(level actor)" : "(world level)",
                    IsPointerDeref = false,
                },
            };
        }

        // Resolve the element stride + within-element pointer offset for splittable
        // container hops. Object/class arrays are the hardcoded 8-byte pointer slot
        // (independent of the path step). Map/Set and interface arrays (FScriptInterface,
        // 16-byte slot, ptr at elem+0) carry their stride on the step.
        int splitStride = 0, splitValueOffset = 0;
        if (s.ElementIndex >= 0 && s.FieldType == "ArrayProperty"
            && (s.InnerType == "ObjectProperty" || s.InnerType == "ClassProperty"))
        {
            splitStride = 8;            // ObjectProperty stride = 8 (pointer)
            splitValueOffset = 0;
        }
        else if (s.ElementIndex >= 0 && s.ElemStride > 0
                 && (s.FieldType == "MapProperty" || s.FieldType == "SetProperty"
                     || (s.FieldType == "ArrayProperty"
                         && (s.InnerType == "InterfaceProperty" || s.InnerType == "StructProperty"))))
        {
            splitStride = s.ElemStride;
            // Within-element offset of the followed pointer: map value within-pair
            // offset; for a deep StructProperty element it's the object pointer's
            // offset within the struct element (Aura deep BFS); 0 for set / map
            // key / interface.
            splitValueOffset = s.ElemValueOffset;
        }

        if (splitStride > 0)
        {
            // The container label drops the Map ".Key"/".Value" suffix so it names
            // the real field — and so Back-nav re-hydration (which matches the crumb
            // against a fresh parent walk by FieldName+FieldOffset) finds it.
            string baseName = StripContainerKeyValueSuffix(s.FieldName);
            string kvHint = s.FieldName.EndsWith(".Key", StringComparison.Ordinal) ? ".Key"
                          : s.FieldName.EndsWith(".Value", StringComparison.Ordinal) ? ".Value" : "";
            return new[]
            {
                // Level 1 — the container field: deref the Data pointer (at field+0).
                // Flagged as a container view so CleanBreadcrumbs skips it as a cycle
                // endpoint (it shares the parent object's resolved region).
                // ContainerField stays null (path-derived); Back-nav re-hydrates it
                // via a live parent walk.
                new BreadcrumbItem
                {
                    Address = !string.IsNullOrEmpty(s.From) ? s.From : s.To,
                    Label = !string.IsNullOrEmpty(baseName) ? baseName : "(container)",
                    FieldOffset = s.FieldOffset,
                    FieldName = baseName,
                    IsContainerView = true,
                },
                // Level 2 — the element pointer at index*stride (+ value offset).
                new BreadcrumbItem
                {
                    Address = s.To,
                    Label = $"[{s.ElementIndex}]{kvHint}",
                    FieldOffset = s.ElementIndex * splitStride + splitValueOffset,
                    FieldName = $"[{s.ElementIndex}]{kvHint}",
                    TargetClassName = s.ToClass,
                    IsPointerDeref = true,
                },
            };
        }

        if (s.ElementIndex >= 0) label += $"[{s.ElementIndex}]";
        return new[]
        {
            new BreadcrumbItem
            {
                Address = s.To,
                Label = label,
                FieldOffset = s.FieldOffset,
                FieldName = s.FieldName,
                TargetClassName = s.ToClass,
                IsPointerDeref = true,  // every edge we followed is a pointer deref
            },
        };
    }

    /// <summary>Drop a Map step's trailing ".Key"/".Value" suffix so the container
    /// crumb names the underlying TMap field (used for Back-nav re-hydration match).</summary>
    private static string StripContainerKeyValueSuffix(string fieldName)
    {
        if (fieldName.EndsWith(".Key", StringComparison.Ordinal)
            || fieldName.EndsWith(".Value", StringComparison.Ordinal))
        {
            int dot = fieldName.LastIndexOf('.');
            if (dot > 0) return fieldName[..dot];
        }
        return fieldName;
    }

    /// <summary>
    /// "Locate in GWorld" for a container-match value (Instance Finder by-address).
    /// Reaches the owning object via the shortest GWorld path, then drills the
    /// full container chain — outermost container → element [N] → (nested
    /// container → element → …) — to land ON the value, even when it lives in a
    /// deeply-nested, separately-allocated container (the deep-scan case). The
    /// single-shot <c>_pendingScroll*</c> path can't chain multiple container
    /// levels, so the drill is an explicit awaited sequence.
    /// </summary>
    public async Task LocateContainerInGWorldAsync(ContainerMatch match, CancellationToken ct = default,
                                                   string rootKind = "gworld")
    {
        if (match == null || string.IsNullOrEmpty(match.OwnerAddress)) return;
        string rootLabel = rootKind == "engine" ? "GameEngine" : "GWorld";
        LocateFailureTitle = $"Locate in {rootLabel} failed";
        try
        {
            ClearStatus();
            IsLoading = true;
            StopAutoRefreshTimer();
            _replacedSpine = null;   // Locate re-spines the SAME object — see _replacedSpine
            IsBookmarkSaveMode = false;

            var path = await _dump.FindPathFromGWorldAsync(match.OwnerAddress, match.OwnerAddress,
                                                           GWorldLocateDepth, ct, rootKind,
                                                           GWorldLocateDeep, GWorldLocateDeep ? kGWorldDeepContainerDepth : 1);
            if (!path.Found)
            {
                if (path.Status == "cancelled")
                {
                    StatusText = GWorldPathFailureStatus(path, rootLabel);
                    return;
                }
                ClearDisplayedNode();
                LocateFailureMessage = GWorldPathFailureStatus(path, rootLabel);
                return;
            }

            BuildBreadcrumbSpineFromPath(path, match.OwnerAddress, stopAtParent: false, rootKind);

            // Explicit awaited drill — clear the single-shot pending state.
            _pendingScrollFieldOffset = null;
            _pendingScrollFieldName = null;
            _pendingDrillElementIndex = -1;

            var ownerAddr = Breadcrumbs[^1].Address;
            var ownerResult = await _dump.WalkInstanceAsync(ownerAddr, arrayLimit: ArrayLimit,
                                                            previewLimit: PreviewLimit, fillGaps: FillGaps, ct: ct);
            ownerResult = await AutoFillGapsRetryAsync(ownerResult, ownerAddr);
            UpdateDisplay(ownerResult);   // land on owner

            int totalHops = 1 + match.NestedChain.Count;
            int drilled = await DrillContainerChainAsync(match);

            _log.Info($"LocateContainerIn{rootLabel}: {path.Depth} hop(s) to {match.OwnerName}, " +
                      $"drilled {drilled}/{totalHops} container level(s), {path.DurationMs}ms | BC={FormatBreadcrumbTrace()}");
            StatusText = drilled >= totalHops
                ? $"Located via {rootLabel} — {path.Depth} hop(s); landed on {match.DisplayPath}."
                : $"Located via {rootLabel} — {path.Depth} hop(s) to {match.OwnerName}; drilled {drilled}/{totalHops} level(s). " +
                  $"Continue manually: {match.DisplayPath}.";
        }
        catch (Exception ex)
        {
            ClearDisplayedNode();
            LocateFailureMessage = $"Locate in {rootLabel} failed: {ex.Message}";
            SetError(ex);
            _log.Error($"LocateContainerIn{rootLabel} failed for {match.OwnerAddress}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Drill the container chain of <paramref name="match"/> hop-by-hop from the
    /// currently-displayed owning object, landing on the value. Each hop:
    /// navigate any intermediate DIRECT struct segments (dotted name) → drill the
    /// container → select element [N]; nested hops then drill INTO the struct
    /// element to continue. The deepest hop scrolls to the value (a field of the
    /// struct element at its intra-offset, or the leaf element itself). Returns
    /// the number of hops drilled; stops early (and reports) if a hop can't be
    /// matched in the live view.
    /// </summary>
    /// <summary>
    /// Flatten a container match into the ordered drill path, outermost-first:
    /// the match's own (outermost) container hop followed by each nested-chain
    /// hop. Each entry is (container field dotted-name, element index, intra
    /// offset). The last entry is the deepest hop whose intra-offset locates the
    /// value. Pure — unit-tested.
    /// </summary>
    internal static List<(string fieldName, int elementIndex, int intraOffset)> BuildContainerDrillPath(ContainerMatch match)
    {
        var hops = new List<(string fieldName, int elementIndex, int intraOffset)>
        {
            (match.FieldName, match.ElementIndex, match.IntraOffset),
        };
        foreach (var h in match.NestedChain)
            hops.Add((h.FieldName, h.ElementIndex, h.IntraOffset));
        return hops;
    }

    private async Task<int> DrillContainerChainAsync(ContainerMatch match)
    {
        var hops = BuildContainerDrillPath(match);

        int drilled = 0;
        for (int hi = 0; hi < hops.Count; hi++)
        {
            var (fieldName, elementIndex, intraOffset) = hops[hi];
            bool isLast = hi == hops.Count - 1;

            // Navigate leading DIRECT-struct segments (e.g. "MsTuneData" in
            // "MsTuneData.MsTunes"); the last segment is the container itself.
            var segments = fieldName.Split('.');
            for (int s = 0; s < segments.Length - 1; s++)
            {
                var structField = Fields.FirstOrDefault(f => f.Name == segments[s] && f.IsStructNavigation);
                if (structField == null) return drilled;   // can't continue
                await NavigateToFieldAsync(structField);
            }

            var containerName = segments[^1];
            var containerField = Fields.FirstOrDefault(f => f.Name == containerName && f.IsContainerNavigable);
            if (containerField == null) return drilled;
            await NavigateToContainerAsync(containerField);   // → element view

            var elemRow = Fields.FirstOrDefault(f =>
                f.Name == $"[{elementIndex}]" ||
                f.Name.StartsWith($"[{elementIndex}] ", StringComparison.Ordinal));
            if (elemRow == null) return drilled;

            if (!isLast)
            {
                // Must descend into the struct element to reach the next hop.
                if (!elemRow.IsStructNavigation) return drilled;
                await NavigateToFieldAsync(elemRow);
                drilled++;
                continue;
            }

            // Deepest hop — land on the value.
            if (elemRow.IsStructNavigation)
            {
                // Value is a field INSIDE the struct element (at intraOffset).
                await NavigateToFieldAsync(elemRow);
                var leaf = Fields.FirstOrDefault(f => f.Offset == intraOffset);
                if (leaf != null)
                {
                    SelectedField = leaf;
                    ScrollToFieldRequested?.Invoke(leaf.Name);
                }
            }
            else
            {
                // The element itself is the value (leaf element, e.g. TArray<int>).
                SelectedField = elemRow;
                ScrollToFieldRequested?.Invoke(elemRow.Name);
            }
            drilled++;
        }
        return drilled;
    }

    /// <summary>
    /// Extract a trailing "[N]" container-element index from a Value Search
    /// field display name (e.g. "Cargo[3]" or "Augments.Value[2]"). Returns
    /// -1 for a direct field with no element suffix, an empty/negative
    /// bracket, or a non-leaf path like "Cargo[3].ItemId" (not a drillable
    /// element row).
    /// </summary>
    internal static int ParseElementIndexSuffix(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName) || fieldName[^1] != ']') return -1;
        int open = fieldName.LastIndexOf('[');
        if (open < 0 || open >= fieldName.Length - 2) return -1;  // no '[' or "[]"
        var inner = fieldName.Substring(open + 1, fieldName.Length - open - 2);
        return int.TryParse(inner, out var idx) && idx >= 0 ? idx : -1;
    }

    /// <summary>
    /// Parse a Value Search / SPC candidate display name into an ordered drill
    /// path of segments, each either a DIRECT struct field ("Name", index -1) or
    /// a CONTAINER element ("Name", index N from "Name[N]"). Returns true only
    /// when the path contains at least one "[N]" element (so it needs container
    /// drilling); a plain field ("Health"), an empty/malformed name, or a bracket
    /// not at a segment's end returns false so the caller falls back to the
    /// single-offset scroll. Handles arbitrary depth, e.g.
    /// "SaveSlotList[0].MsTuneData.MsTunes[0].WeaponTuneList[0].Tunes[2]". Pure —
    /// unit-tested. Generalises the former single-"[N]" struct-array parser.
    /// </summary>
    /// <summary>
    /// Resolve the field row a byte-offset scroll hint should land on: the field
    /// at <paramref name="wantOffset"/> exactly, or — when none exists because the
    /// leaf lives inside a nested struct (a GAS <c>FGameplayAttributeData.CurrentValue</c>
    /// at owner+0x120 sits inside the <c>CurrentHealth</c> StructProperty at 0x118) —
    /// the containing top-level field, i.e. the one with the largest offset ≤ the
    /// leaf offset. Returns null only when no field is at or before the offset.
    /// Pure / static so the contract is unit-testable without a populated grid.
    /// </summary>
    internal static LiveFieldValue? FindFieldByOffsetOrContaining(
        IReadOnlyList<LiveFieldValue> fields, int wantOffset)
    {
        LiveFieldValue? exact = null, containing = null;
        foreach (var f in fields)
        {
            if (f.Offset == wantOffset) { exact = f; break; }
            if (f.Offset <= wantOffset && (containing == null || f.Offset > containing.Offset))
                containing = f;
        }
        return exact ?? containing;
    }

    /// <summary>
    /// Select + scroll the currently-displayed field list to the row at
    /// <paramref name="wantOffset"/> (exact, else the containing top-level field
    /// via <see cref="FindFieldByOffsetOrContaining"/>). Returns false when no row
    /// is at or before the offset. Shared by the UpdateDisplay scroll hint and the
    /// Locate-in-GWorld container-drill fallback.
    /// </summary>
    private bool ScrollToFieldByOffset(int wantOffset)
    {
        var hit = FindFieldByOffsetOrContaining(Fields, wantOffset);
        if (hit == null)
        {
            _log.Info($"ScrollToFieldByOffset: offset 0x{wantOffset:X} not found among top-level fields");
            _pendingDrillElementIndex = -1;
            return false;
        }
        SelectedField = hit;
        ScrollToFieldRequested?.Invoke(hit.Name);
        _log.Info($"ScrollToFieldByOffset: offset 0x{wantOffset:X} -> field '{hit.Name}' @0x{hit.Offset:X}");
        TryDrillIntoMatchedContainer(hit);
        return true;
    }

    internal static bool TryParseContainerPath(string? fieldName,
                                               out List<(string name, int index)> segments,
                                               bool requireIndex = true)
    {
        segments = new List<(string name, int index)>();
        if (string.IsNullOrEmpty(fieldName)) return false;

        bool hasIndex = false;
        foreach (var raw in fieldName.Split('.'))
        {
            if (raw.Length == 0) return false;                 // empty segment — malformed
            if (raw[^1] == ']')
            {
                int open = raw.LastIndexOf('[');
                if (open <= 0) return false;                   // "]" with no name before "["
                var idxStr = raw.Substring(open + 1, raw.Length - open - 2);
                if (!int.TryParse(idxStr, out var idx) || idx < 0) return false;  // "[]"/"[-1]"/non-numeric
                segments.Add((raw.Substring(0, open), idx));
                hasIndex = true;
            }
            else if (raw.IndexOf('[') >= 0)
            {
                return false;                                   // bracket not at the end — unexpected
            }
            else
            {
                segments.Add((raw, -1));
            }
        }
        // A container path (has a "[N]") always qualifies. With requireIndex=false a
        // pure nested-struct path (≥2 dotted segments, e.g.
        // "CameraCachePrivate.POV.Location") also qualifies — DrillDisplayPathAsync
        // walks struct segments via its index<0 branch the same way. The default
        // (requireIndex=true) preserves the container-only contract (a bare or
        // dotted no-index field is rejected → caller uses the byte-offset scroll).
        return hasIndex || (!requireIndex && segments.Count >= 2);
    }

    /// <summary>
    /// From the currently-displayed owning object, drill an arbitrary-depth
    /// display path (parsed by <see cref="TryParseContainerPath"/>) to land ON the
    /// value. Each segment is either a direct struct field (descend by name) or a
    /// container element "Name[N]" (drill the container by name → select element
    /// [N]; if not the last segment, descend into the struct element to continue).
    /// The final segment is selected/scrolled to. Returns true on a full landing.
    /// Generalises the single-level struct-array drill to multi-"[N]" paths so
    /// deep Value Search / SPC candidates reach exactly — parity with the Instance
    /// Finder structured-chain drill (<see cref="DrillContainerChainAsync"/>).
    /// </summary>
    private async Task<bool> DrillDisplayPathAsync(List<(string name, int index)> segments)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            var (name, index) = segments[i];
            bool isLast = i == segments.Count - 1;

            if (index >= 0)
            {
                // Container element: drill the container (by name) then select [N].
                var containerField = Fields.FirstOrDefault(f => f.Name == name && f.IsContainerNavigable);
                if (containerField == null) return false;
                await NavigateToContainerAsync(containerField);

                var elemRow = Fields.FirstOrDefault(f =>
                    f.Name == $"[{index}]" ||
                    f.Name.StartsWith($"[{index}] ", StringComparison.Ordinal));
                if (elemRow == null) return false;

                if (isLast)
                {
                    // The element itself is the value (leaf element, e.g. TArray<int>).
                    SelectedField = elemRow;
                    ScrollToFieldRequested?.Invoke(elemRow.Name);
                    return true;
                }
                // Descend into the struct element to reach the next segment.
                if (!elemRow.IsStructNavigation) return false;
                await NavigateToFieldAsync(elemRow);
            }
            else
            {
                // Direct struct field.
                if (isLast)
                {
                    var leaf = Fields.FirstOrDefault(f => f.Name == name);
                    if (leaf == null) return false;
                    SelectedField = leaf;
                    ScrollToFieldRequested?.Invoke(leaf.Name);
                    return true;
                }
                var structField = Fields.FirstOrDefault(f => f.Name == name && f.IsStructNavigation);
                if (structField == null) return false;
                await NavigateToFieldAsync(structField);
            }
        }
        return true;   // unreachable for a non-empty path (last segment always returns)
    }

    [RelayCommand]
    private void SaveBookmarkToSlot(BookmarkSlot? slot)
    {
        IsBookmarkSaveMode = false;
        if (slot == null || Breadcrumbs.Count == 0 || string.IsNullOrEmpty(CurrentAddress)) return;

        slot.SavedBreadcrumbs = Breadcrumbs.ToList();
        slot.SavedAddress = CurrentAddress;
        slot.SavedObjectName = CurrentObjectName;
        slot.SavedClassName = CurrentClassName;
        slot.SavedClassAddr = _currentClassAddr;
        slot.SavedCachedWorld = _cachedWorld;

        // Capture the selected rows (one or many) so loading re-selects them.
        // Prefer the multi-select snapshot; fall back to the single SelectedField
        // anchor (set when a row is clicked without a grid SelectionChanged sync).
        slot.SavedSelectedFields = _selectedFieldsSnapshot
            .Select(f => new BookmarkFieldRef(f.Name, f.Offset))
            .ToList();
        if (slot.SavedSelectedFields.Count == 0 && SelectedField != null)
            slot.SavedSelectedFields.Add(new BookmarkFieldRef(SelectedField.Name, SelectedField.Offset));

        // Capture the scroll anchor (topmost visible row; View fills this in synchronously).
        var anchor = new ViewAnchorRef();
        CaptureViewAnchor?.Invoke(anchor);
        slot.SavedTopRow = anchor.TopRow;

        // Truncate label for button display
        var label = !string.IsNullOrEmpty(CurrentObjectName) ? CurrentObjectName : CurrentClassName;
        if (label.Length > 14) label = label[..14] + "..";
        slot.Label = label;
        slot.IsOccupied = true;  // also refreshes the computed TooltipText

        StatusText = $"Bookmark {slot.DisplayNumber} saved";
        var topName = slot.SavedTopRow?.Name ?? "-";
        _log.Info($"Bookmark saved slot={slot.SlotIndex} addr={CurrentAddress} name={CurrentObjectName} sel={slot.SavedSelectedFields.Count} top={topName}");

        PersistBookmarks();
    }

    [RelayCommand]
    private void ToggleBookmarkSaveMode()
    {
        if (Breadcrumbs.Count == 0 || string.IsNullOrEmpty(CurrentAddress))
            return;
        IsBookmarkSaveMode = !IsBookmarkSaveMode;
    }

    [RelayCommand]
    private void CancelBookmarkSave()
    {
        IsBookmarkSaveMode = false;
    }

    [RelayCommand]
    private async Task LoadBookmarkAsync(BookmarkSlot? slot)
    {
        if (slot == null) return;

        // If in save mode, redirect to save instead of loading
        if (IsBookmarkSaveMode)
        {
            SaveBookmarkToSlot(slot);
            return;
        }

        if (!slot.IsOccupied) return;

        // Loading a bookmark jumps to different data — drop the field filter.
        ClearFieldSearchForNavigation();

        try
        {
            ClearStatus();
            IsLoading = true;
            StopAutoRefreshTimer();

            // Save current state for Back-after-bookmark. Shares the re-root slot with
            // NavigateToAddressAsync — a bookmark load and an address re-root replace
            // the spine the same way, so they get the same one Back out of it.
            CaptureReplacedSpine();

            // Restore breadcrumbs. A persisted bookmark's saved addresses go stale
            // after a game RESTART — ASLR re-randomizes every pointer, so the saved
            // leaf address (and each intermediate hop) is dead. The spine's FIELD
            // identity (name + offset + deref/container kind) is stable, though, so
            // first try to RE-RESOLVE it from a live anchor (GWorld / GameEngine) down,
            // reconstructing fresh addresses — that's what makes a bookmark survive a
            // restart. On any failure (no live root / a hop no longer matches) fall back
            // to the saved addresses: still valid within the same game process, and
            // honestly reported as stale otherwise.
            var resolvedSpine = await TryReresolveBookmarkSpineAsync(slot.SavedBreadcrumbs);
            Breadcrumbs.Clear();
            if (resolvedSpine != null)
            {
                foreach (var bc in resolvedSpine) Breadcrumbs.Add(bc);
                // _cachedWorld was refreshed by the re-resolution (GWorld root).
            }
            else
            {
                foreach (var bc in slot.SavedBreadcrumbs)
                    Breadcrumbs.Add(bc);
                _cachedWorld = slot.SavedCachedWorld;
            }

            // Re-display the saved view. Branches are mutually exclusive and all
            // rebuild Fields, so selection + scroll restore happens once at the end.
            // restoredFully = false marks a STALE persisted bookmark (address no longer
            // resolves to the saved object) so we skip applying its now-meaningless
            // selection/scroll and tell the user instead of showing wrong data.
            var lastBc = Breadcrumbs.LastOrDefault();
            bool restoredFully = true;
            if (lastBc != null)
            {
                if (lastBc.IsContainerView && lastBc.ContainerField != null)
                {
                    RepopulateContainerView(lastBc.ContainerField, lastBc);
                }
                else if (lastBc.IsContainerView && lastBc.ContainerField == null
                         && await TryRepopulateSyntheticContainerAsync(lastBc))
                {
                    // Path-synthetic container crumb (no live ContainerField): re-hydrated
                    // from a live parent walk inside the condition — mirrors the 3 Back-nav
                    // dispatch sites so a bookmark saved on such a view restores the array
                    // element view, not the parent object grid. Falls through to the walk
                    // below when no live match (graceful degradation).
                }
                else if (lastBc.FieldName == "GWorld")
                {
                    // GWorld actor-list root. A persisted bookmark carries no cached world,
                    // so re-walk it fresh — GWorld is a stable singleton, so this restores
                    // correctly even after a game restart. An in-session bookmark reuses the
                    // cached walk.
                    _cachedWorld ??= await _dump.WalkWorldAsync(Constants.WorldWalkMaxDepth, arrayLimit: ArrayLimit);
                    PopulateFromWorld(_cachedWorld);
                }
                else
                {
                    var classAddr = string.IsNullOrEmpty(lastBc.ClassAddr) ? null : lastBc.ClassAddr;
                    var result = await _dump.WalkInstanceAsync(
                        lastBc.Address, classAddr,
                        arrayLimit: ArrayLimit, previewLimit: PreviewLimit, fillGaps: FillGaps);
                    result = await AutoFillGapsRetryAsync(result, lastBc.Address, classAddr);
                    UpdateDisplay(result);

                    // Staleness guard (safety net): spine re-resolution above normally
                    // makes a restart land on the right object, but when it fell back to
                    // the saved addresses (re-anchor failed) those may be dead after a
                    // restart and the walk lands on garbage / a different object. Detect
                    // via a class-name mismatch and degrade honestly (keep the bookmark,
                    // drop the now-meaningless selection).
                    if (!string.IsNullOrEmpty(slot.SavedClassName)
                        && !string.Equals(CurrentClassName, slot.SavedClassName, StringComparison.Ordinal))
                    {
                        restoredFully = false;
                    }
                }
            }

            if (restoredFully)
            {
                // Re-select the rows the user had selected + restore the scroll position.
                RestoreBookmarkView?.Invoke(slot.SavedSelectedFields, slot.SavedTopRow);
                StatusText = $"Bookmark {slot.DisplayNumber} loaded";
            }
            else
            {
                StatusText = $"Bookmark {slot.DisplayNumber} stale (game may have restarted) — re-create it";
            }
            var topName = slot.SavedTopRow?.Name ?? "-";
            _log.Info($"Bookmark loaded slot={slot.SlotIndex} addr={slot.SavedAddress} sel={slot.SavedSelectedFields.Count} top={topName} full={restoredFully}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = $"Bookmark {slot.DisplayNumber} invalid — address may no longer be valid";
            _log.Error($"Bookmark load failed slot={slot.SlotIndex}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Re-resolve a persisted bookmark's navigation spine against the LIVE process so the
    /// bookmark survives a game restart. A saved breadcrumb's <c>Address</c> is only valid
    /// while the game process that produced it is alive — after a restart ASLR
    /// re-randomizes every pointer, so the saved leaf address and every intermediate hop
    /// are dead. The spine's FIELD identity (name + offset + deref/container kind) is
    /// stable, though, so we re-walk it from a live anchor (GWorld / GameEngine) down and
    /// rebuild each crumb with a fresh address.
    ///
    /// Returns a fresh breadcrumb list (new immutable <see cref="BreadcrumbItem"/>s with
    /// live addresses + re-attached container fields) on full success, or <c>null</c> when
    /// the spine can't be re-anchored: a non-GWorld/GameEngine root, a hop whose field no
    /// longer matches by name+offset, a null pointer, or a DataTable view (un-persisted
    /// walk state). On null the caller keeps the saved addresses — still valid within the
    /// same game process, honestly reported as stale otherwise.
    ///
    /// Side effect: on success <see cref="_cachedWorld"/> is refreshed to the live world
    /// (null for an engine root) so the GWorld-root display branch reuses it.
    /// </summary>
    private async Task<List<BreadcrumbItem>?> TryReresolveBookmarkSpineAsync(
        IReadOnlyList<BreadcrumbItem> saved)
    {
        if (saved.Count == 0) return null;
        var root = saved[0];

        // Resolve the root anchor fresh. Only GWorld / GameEngine roots have a stable live
        // anchor; an address-rooted ("Custom") bookmark has nothing to re-walk from.
        string curAddr;
        WorldWalkResult? freshWorld = null;
        try
        {
            if (root.FieldName == "GWorld")
            {
                freshWorld = await _dump.WalkWorldAsync(Constants.WorldWalkMaxDepth, arrayLimit: ArrayLimit);
                if (freshWorld == null || string.IsNullOrEmpty(freshWorld.WorldAddr)) return null;
                curAddr = freshWorld.WorldAddr;
            }
            else if (root.FieldName == "GameEngine")
            {
                var engine = await _dump.ResolveGameEngineAsync();
                if (engine is not { Found: true } || string.IsNullOrEmpty(engine.Address)) return null;
                curAddr = engine.Address;
            }
            else
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Bookmark re-resolve: root anchor '{root.FieldName}' failed: {ex.Message}");
            return null;
        }

        var rebuilt = new List<BreadcrumbItem>(saved.Count)
        {
            CloneCrumbWithAddress(root, curAddr, root.ClassAddr, null),
        };

        // A null classAddr lets the DLL resolve the object's UClass from its vtable; only
        // struct hops carry an explicit UScriptStruct* address.
        string? curClassAddr = null;
        LiveFieldValue? pendingContainer = null;

        for (int i = 1; i < saved.Count; i++)
        {
            var c = saved[i];

            // DataTable row views carry un-persisted walk state (DataTableData) — can't
            // re-resolve; bail so the caller falls back (matches pre-fix behaviour).
            if (c.IsDataTableView) return null;

            try
            {
                if (c.IsContainerView)
                {
                    // Container-view crumb shares the parent object's address; re-find the
                    // container field by name+offset and re-attach it (fresh elements).
                    var parent = await WalkForReresolveAsync(curAddr, curClassAddr);
                    var field = MatchSpineField(parent, c);
                    if (field == null || !field.IsContainerNavigable) return null;
                    rebuilt.Add(CloneCrumbWithAddress(c, curAddr, c.ClassAddr, field));
                    pendingContainer = field;
                    // curAddr / curClassAddr unchanged — the element crumb does the deref.
                }
                else if (IsElementCrumb(c.FieldName))
                {
                    // Element crumb "[N]" (optionally ".Key"/".Value"): deref element N of
                    // the pending container to its target object / inline-struct address.
                    if (pendingContainer == null) return null;
                    int? idx = ParseSparseIndex(c.FieldName);
                    if (idx == null) return null;
                    var hop = ResolveContainerElementHop(pendingContainer, idx.Value, c.FieldName);
                    if (hop == null) return null;
                    curAddr = hop.Value.Addr;
                    curClassAddr = string.IsNullOrEmpty(hop.Value.ClassAddr) ? null : hop.Value.ClassAddr;
                    rebuilt.Add(CloneCrumbWithAddress(c, curAddr, curClassAddr ?? "", null));
                    pendingContainer = null;
                }
                else
                {
                    // Plain pointer / inline-struct field hop.
                    var parent = await WalkForReresolveAsync(curAddr, curClassAddr);
                    var field = MatchSpineField(parent, c);
                    if (field == null) return null;
                    if (!string.IsNullOrEmpty(field.PtrAddress) && field.PtrAddress != "0x0")
                    {
                        curAddr = field.PtrAddress;
                        curClassAddr = null;   // object pointer — DLL resolves the class
                    }
                    else if (!string.IsNullOrEmpty(field.StructDataAddr) && field.StructDataAddr != "0x0")
                    {
                        curAddr = field.StructDataAddr;
                        curClassAddr = string.IsNullOrEmpty(field.StructClassAddr) ? null : field.StructClassAddr;
                    }
                    else
                    {
                        return null;   // field no longer navigable
                    }
                    rebuilt.Add(CloneCrumbWithAddress(c, curAddr, curClassAddr ?? "", null));
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"Bookmark re-resolve: hop '{c.FieldName}' failed: {ex.Message}");
                return null;
            }
        }

        _cachedWorld = freshWorld;   // refreshed live world (null for an engine root)
        _log.Info($"Bookmark re-resolved spine from live {root.FieldName}: {rebuilt.Count} crumb(s)");
        return rebuilt;
    }

    /// <summary>Walk a UObject / struct for spine re-resolution. Uses the user's current
    /// FillGaps toggle but does NOT auto-toggle it — the named navigable fields the spine
    /// matches on come from reflection regardless of gap-fill, and the terminal display
    /// re-walks the leaf afterwards with the full auto-fill-gaps logic.</summary>
    private Task<InstanceWalkResult> WalkForReresolveAsync(string addr, string? classAddr)
        => _dump.WalkInstanceAsync(addr, classAddr, arrayLimit: ArrayLimit,
                                   previewLimit: PreviewLimit, fillGaps: FillGaps);

    /// <summary>Match a saved spine crumb to a field in a fresh parent walk. Primary key is
    /// name+offset (the stable spine identity); falls back to a UNIQUE name-only match so a
    /// small offset shift across a game patch still resolves, while refusing an ambiguous
    /// duplicate-name match.</summary>
    internal static LiveFieldValue? MatchSpineField(InstanceWalkResult walk, BreadcrumbItem crumb)
    {
        foreach (var f in walk.Fields)
            if (f.Name == crumb.FieldName && f.Offset == crumb.FieldOffset)
                return f;
        LiveFieldValue? byName = null;
        foreach (var f in walk.Fields)
        {
            if (f.Name != crumb.FieldName) continue;
            if (byName != null) return null;   // ambiguous — refuse
            byName = f;
        }
        return byName;
    }

    /// <summary>True for a container ELEMENT crumb ("[N]", "[N].Key", "[N].Value").</summary>
    internal static bool IsElementCrumb(string fieldName)
        => !string.IsNullOrEmpty(fieldName) && fieldName.Length >= 2 && fieldName[0] == '[';

    /// <summary>
    /// Resolve a container element crumb to its drill target (object pointer address, or an
    /// inline-struct data address + UScriptStruct*). Mirrors the element-address maths in
    /// <see cref="PopulateArrayContainerFields"/> / <see cref="PopulateMapContainerFields"/>
    /// / <see cref="PopulateSetContainerFields"/>. Returns null when the element (or its
    /// pointer) is missing, so re-resolution bails to the saved-address fallback.
    /// </summary>
    internal static (string Addr, string ClassAddr)? ResolveContainerElementHop(
        LiveFieldValue container, int index, string crumbName)
    {
        // --- Array ---
        if (container.ArrayCount > 0 && !string.IsNullOrEmpty(container.ArrayInnerType))
        {
            if (container.ArrayInnerType == "StructProperty"
                && !string.IsNullOrEmpty(container.ArrayStructClassAddr))
            {
                ulong dataBase = ParseHexAddr(container.ArrayDataAddr);
                if (dataBase != 0 && container.ArrayElemSize > 0)
                    return ($"0x{dataBase + (ulong)(index * container.ArrayElemSize):X}",
                            container.ArrayStructClassAddr);
                return null;
            }
            var elem = container.ArrayElements?.FirstOrDefault(e => e.Index == index);
            return (elem != null && !string.IsNullOrEmpty(elem.PtrAddress) && elem.PtrAddress != "0x0")
                ? (elem.PtrAddress, "") : null;
        }

        // --- Map (value by default; key when the crumb names ".Key") ---
        if (container.MapCount > 0 && !string.IsNullOrEmpty(container.MapKeyType))
        {
            bool wantKey = crumbName.EndsWith(".Key", StringComparison.Ordinal);
            if (!wantKey && container.MapValueType == "StructProperty"
                && !string.IsNullOrEmpty(container.MapValueStructAddr))
            {
                ulong dataBase = ParseHexAddr(container.MapDataAddr);
                int valOffset = ContainerGeometry.MapValueOffsetOf(container);
                int stride = ContainerGeometry.MapStrideOf(container);
                if (dataBase != 0 && stride > 0)
                    return ($"0x{dataBase + (ulong)(index * stride) + (ulong)valOffset:X}",
                            container.MapValueStructAddr);
                return null;
            }
            var elem = container.MapElements?.FirstOrDefault(e => e.Index == index);
            if (elem == null) return null;
            var ptr = wantKey ? elem.KeyPtrAddress : elem.ValuePtrAddress;
            return (!string.IsNullOrEmpty(ptr) && ptr != "0x0") ? (ptr, "") : null;
        }

        // --- Set ---
        if (container.SetCount > 0 && !string.IsNullOrEmpty(container.SetElemType))
        {
            if (container.SetElemType == "StructProperty"
                && !string.IsNullOrEmpty(container.SetElemStructAddr))
            {
                ulong dataBase = ParseHexAddr(container.SetDataAddr);
                int stride = ContainerGeometry.SetStrideOf(container);
                if (dataBase != 0 && stride > 0)
                    return ($"0x{dataBase + (ulong)(index * stride):X}", container.SetElemStructAddr);
                return null;
            }
            var elem = container.SetElements?.FirstOrDefault(e => e.Index == index);
            return (elem != null && !string.IsNullOrEmpty(elem.KeyPtrAddress) && elem.KeyPtrAddress != "0x0")
                ? (elem.KeyPtrAddress, "") : null;
        }

        return null;
    }

    /// <summary>Parse a "0x..." address string to ulong (0 on empty / malformed).</summary>
    internal static ulong ParseHexAddr(string addr)
    {
        if (string.IsNullOrEmpty(addr)) return 0;
        var s = addr.Replace("0x", "").Replace("0X", "");
        return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    }

    /// <summary>Build a fresh breadcrumb from a saved spine crumb with a re-resolved
    /// address / class / container field, copying the (stable) spine identity verbatim.
    /// <see cref="BreadcrumbItem"/> is init-only, so re-resolution rebuilds rather than
    /// mutates.</summary>
    private static BreadcrumbItem CloneCrumbWithAddress(
        BreadcrumbItem src, string addr, string classAddr, LiveFieldValue? containerField)
        => new()
        {
            Address = addr,
            Label = src.Label,
            ClassAddr = classAddr,
            FieldOffset = src.FieldOffset,
            FieldName = src.FieldName,
            TargetClassName = src.TargetClassName,
            IsPointerDeref = src.IsPointerDeref,
            ScrollHintFieldName = src.ScrollHintFieldName,
            IsContainerView = src.IsContainerView,
            ContainerField = containerField ?? src.ContainerField,
            IsDataTableView = src.IsDataTableView,
            DataTableData = src.DataTableData,
        };

    [RelayCommand]
    private void ClearBookmark(BookmarkSlot? slot)
    {
        if (slot == null) return;
        slot.IsOccupied = false;  // also refreshes the computed TooltipText (empty hint)
        slot.Label = "";
        slot.SavedBreadcrumbs.Clear();
        slot.SavedAddress = "";
        slot.SavedObjectName = "";
        slot.SavedClassName = "";
        slot.SavedClassAddr = "";
        slot.SavedCachedWorld = null;
        slot.SavedSelectedFields.Clear();
        slot.SavedTopRow = null;
    }

    /// <summary>Clear all bookmark slots IN MEMORY (called on connect/disconnect).
    /// Does NOT touch the persisted file — that only happens on the user's explicit
    /// "clear all" (<see cref="ClearAllBookmarksAndPersist"/>).</summary>
    public void ClearAllBookmarks()
    {
        foreach (var slot in BookmarkSlots)
            ClearBookmark(slot);
        _replacedSpine = null;
        IsBookmarkSaveMode = false;
    }

    /// <summary>User action — clear ONE slot and persist the change to disk.</summary>
    [RelayCommand]
    private void ClearBookmarkSlot(BookmarkSlot? slot)
    {
        if (slot == null) return;
        ClearBookmark(slot);     // in-memory reset
        PersistBookmarks();
    }

    /// <summary>User action — clear ALL slots and delete the game's bookmark file.</summary>
    [RelayCommand]
    private void ClearAllBookmarksAndPersist()
    {
        ClearAllBookmarks();
        if (_bookmarks != null && !string.IsNullOrEmpty(_activePeHash))
            _bookmarks.Delete(_activePeHash);
        OnPropertyChanged(nameof(AnyBookmarkOccupied));
        OnPropertyChanged(nameof(ShowBookmarkBar));
        StatusText = "All bookmarks cleared";
    }

    /// <summary>True when any slot is occupied — gates the "clear all" button visibility.</summary>
    public bool AnyBookmarkOccupied => BookmarkSlots.Any(s => s.IsOccupied);

    // ── Per-game bookmark persistence ──────────────────────────────────────

    /// <summary>Write the current occupied slots to the active game's file. No-op
    /// without a store / active game, or while hydrating (suppressed).</summary>
    private void PersistBookmarks()
    {
        OnPropertyChanged(nameof(AnyBookmarkOccupied));
        OnPropertyChanged(nameof(ShowBookmarkBar));
        if (_bookmarks == null || _suppressBookmarkPersist || string.IsNullOrEmpty(_activePeHash)) return;
        try
        {
            var file = new BookmarkFile { PeHash = _activePeHash };
            foreach (var slot in BookmarkSlots)
                if (slot.IsOccupied)
                    file.Slots.Add(ToPersisted(slot));
            _bookmarks.Save(_activePeHash, file);
        }
        catch (Exception ex)
        {
            _log.Error($"PersistBookmarks failed (pe={_activePeHash})", ex);
        }
    }

    /// <summary>Load the given game's bookmarks into the slots, replacing whatever's
    /// in memory. Called from MainWindowViewModel on connect / game-change. Hydration
    /// is suppressed so it doesn't re-save, and addresses are kept as same-process
    /// fast-path hints (the load path validates + degrades on a game restart).</summary>
    public void LoadBookmarksForGame(string peHash)
    {
        _activePeHash = peHash ?? "";
        if (_bookmarks == null || string.IsNullOrEmpty(_activePeHash)) return;

        _suppressBookmarkPersist = true;
        try
        {
            foreach (var slot in BookmarkSlots) ClearBookmark(slot);   // start clean (idempotent)

            var file = _bookmarks.Load(_activePeHash);
            foreach (var pb in file.Slots)
            {
                if (pb.SlotIndex >= 0 && pb.SlotIndex < BookmarkSlots.Count)
                    HydrateSlot(BookmarkSlots[pb.SlotIndex], pb);
            }
            _log.Info($"Bookmarks loaded for pe={_activePeHash}: {file.Slots.Count} slot(s)");
        }
        catch (Exception ex)
        {
            _log.Error($"LoadBookmarksForGame failed (pe={_activePeHash})", ex);
        }
        finally
        {
            _suppressBookmarkPersist = false;
            OnPropertyChanged(nameof(AnyBookmarkOccupied));
        OnPropertyChanged(nameof(ShowBookmarkBar));
        }
    }

    private static PersistedBookmark ToPersisted(BookmarkSlot slot) => new()
    {
        SlotIndex = slot.SlotIndex,
        Label = slot.Label,
        SavedObjectName = slot.SavedObjectName,
        SavedClassName = slot.SavedClassName,
        SavedAddress = slot.SavedAddress,
        SavedClassAddr = slot.SavedClassAddr,
        Breadcrumbs = slot.SavedBreadcrumbs.Select(bc => new PersistedCrumb
        {
            Address = bc.Address,
            Label = bc.Label,
            ClassAddr = bc.ClassAddr,
            FieldOffset = bc.FieldOffset,
            FieldName = bc.FieldName,
            TargetClassName = bc.TargetClassName,
            IsPointerDeref = bc.IsPointerDeref,
            IsContainerView = bc.IsContainerView,
        }).ToList(),
        SelectedFields = slot.SavedSelectedFields
            .Select(f => new PersistedFieldRef { Name = f.Name, Offset = f.Offset }).ToList(),
        TopRow = slot.SavedTopRow is { } t ? new PersistedFieldRef { Name = t.Name, Offset = t.Offset } : null,
    };

    // Rebuild a live BookmarkSlot from its persisted form. The breadcrumb's live-only
    // members (ContainerField / DataTableData) are left null and SavedCachedWorld stays
    // null — the load path re-walks GWorld or re-synthesizes the container as needed.
    private static void HydrateSlot(BookmarkSlot slot, PersistedBookmark pb)
    {
        slot.Label = pb.Label;
        slot.SavedObjectName = pb.SavedObjectName;
        slot.SavedClassName = pb.SavedClassName;
        slot.SavedAddress = pb.SavedAddress;
        slot.SavedClassAddr = pb.SavedClassAddr;
        slot.SavedCachedWorld = null;
        slot.SavedBreadcrumbs = pb.Breadcrumbs.Select(c => new BreadcrumbItem
        {
            Address = c.Address,
            Label = c.Label,
            ClassAddr = c.ClassAddr,
            FieldOffset = c.FieldOffset,
            FieldName = c.FieldName,
            TargetClassName = c.TargetClassName,
            IsPointerDeref = c.IsPointerDeref,
            IsContainerView = c.IsContainerView,
        }).ToList();
        slot.SavedSelectedFields = pb.SelectedFields
            .Select(f => new BookmarkFieldRef(f.Name, f.Offset)).ToList();
        slot.SavedTopRow = pb.TopRow is { } t ? new BookmarkFieldRef(t.Name, t.Offset) : null;
        slot.IsOccupied = true;   // marks filled + refreshes the tooltip
    }

    [RelayCommand]
    private async Task ExportCeXmlAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress) || Breadcrumbs.Count == 0) return;
        if (IsExporting) return;   // an export is already running — its Cancel button is showing

        // Record what this heavy operation costs the DLL dispatcher. Automatic
        // rather than a measurement session: the evidence then accumulates from
        // real use instead of only the scenario somebody thought to test. Degrades
        // to a no-op when not connected; never affects the operation.
        await using var _perf = await Services.DiagnosticsProbe.BeginAsync(_dump, _log, "Copy CE XML");

        var cts = _exportCts = new CancellationTokenSource();
        try
        {
            ClearStatus();
            IsLoading = true;
            IsExporting = true;

            // Container view: strip container breadcrumb, use original ContainerField.
            // Container breadcrumbs share the parent's Address, which causes CleanBreadcrumbs
            // to falsely detect a cycle and remove them. Using parent breadcrumbs + ContainerField
            // lets EmitFields dispatch to EmitMapProperty/EmitArrayProperty/EmitSetProperty correctly.
            var lastBc = Breadcrumbs[^1];
            var isContainerView = lastBc.IsContainerView && lastBc.ContainerField != null;
            var breadcrumbsForXml = isContainerView
                ? (IReadOnlyList<BreadcrumbItem>)Breadcrumbs.Take(Breadcrumbs.Count - 1).ToList()
                : Breadcrumbs;
            var fieldsForXml = isContainerView
                ? new List<LiveFieldValue> { lastBc.ContainerField! }
                : new List<LiveFieldValue>(Fields);

            // Drop everything above the last hop that has no offset (a GWorld actor-list
            // entry, a World-Partition recovery hop). Those are reached by a BACK-reference
            // — an actor's Outer, ULevel::OwningWorld — so no forward offset exists and the
            // chain that used to be emitted read [UWorld + 0], i.e. the world's vtable.
            // Re-rooting there costs restart-stability and buys a chain that is right.
            var reanchoredForXml = CeXmlExportService.AnchorAtLastUnchainableHop(breadcrumbsForXml);
            var reanchorWarn = ReanchorNote(breadcrumbsForXml, reanchoredForXml);
            LogReanchor(breadcrumbsForXml, reanchoredForXml);
            breadcrumbsForXml = reanchoredForXml;

            _log.Info($"CEXML export: containerView={isContainerView} bcCount={breadcrumbsForXml.Count} | BC={FormatBreadcrumbTrace()}");

            // Pre-check CleanBreadcrumbs to log any cycle removals
            var cleaned = CeXmlExportService.CleanBreadcrumbs(breadcrumbsForXml);
            if (cleaned.Count != breadcrumbsForXml.Count)
            {
                _log.Info($"CEXML CleanBC: {breadcrumbsForXml.Count}→{cleaned.Count} removed={breadcrumbsForXml.Count - cleaned.Count}");
                for (int i = 0; i < cleaned.Count; i++)
                {
                    var bc = cleaned[i];
                    var flags = bc.IsContainerView ? "C" : bc.IsPointerDeref ? "P" : "S";
                    _log.Info($"  [{i}] {bc.FieldName ?? bc.Label} ({flags}) off={FormatCrumbOffset(bc.FieldOffset)} addr={bc.Address}");
                }
            }

            // Unified drilldown resolve (docs/ce-export-drilldown-spec.md Phase A):
            // structs (flatten) + pointers + CONTAINER ELEMENT VALUES (Map/Set/struct-
            // array values that are themselves structs/objects), recursively to
            // CsxDrilldownDepth — so a Map<Name, Struct> / Set<Struct> / nested
            // Map-of-Struct expands in the export, matching what the UI can drill.
            StatusText = CsxDrilldownDepth > 0
                ? "Resolving struct + pointer + container fields..."
                : "Resolving struct fields...";
            var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
            var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
            int lastShown = 0;
            await CeXmlExportService.ResolveDrilldownAsync(
                _dump, fieldsForXml, resolvedStructs, resolvedInstances,
                depth: CsxDrilldownDepth, arrayLimit: ArrayLimit,
                onWalk: () =>
                {
                    // Live indicator: objects (structs + pointer targets) resolved so far,
                    // throttled so a deep/wide map doesn't spam the bound StatusText.
                    int n = resolvedStructs.Count + resolvedInstances.Count;
                    if (n - lastShown >= 16) { lastShown = n; StatusText = $"Resolving… {n} objects"; }
                },
                // Lean payload: this resolve feeds a CE XML export, which reads
                // structure (name/offset/type/drill-down) and never a live VALUE.
                // Measured at ~24-38% fewer bytes (multipipe-eval.md 10.6), which is
                // the payload-proportional IPC + UI parse that batching cannot touch.
                lean: true,
                ct: cts.Token);

            var rootBc = breadcrumbsForXml[0];

            // AOB mode requires a GWorld-rooted breadcrumb chain. When the object was
            // opened via Instance Finder / Address Lookup there is no GWorld→object path,
            // so we fall back to direct-address mode to avoid generating a wrong base.
            var isGWorldRoot = rootBc.FieldName == "GWorld";
            var useAob = UseAobSymbol && isGWorldRoot && !string.IsNullOrEmpty(_engineState?.GWorldAob);
            if (UseAobSymbol && !isGWorldRoot)
                _log.Info("CEXML: AOB requested but root is not GWorld — falling back to direct address");

            StatusText = "Generating CE XML...";
            string xml;
            if (useAob)
            {
                xml = CeXmlExportService.GenerateAobWrappedXml(
                    rootBc.Label, breadcrumbsForXml, fieldsForXml,
                    _engineState!.GWorldAob, _engineState.GWorldAobPos, _engineState.GWorldAobLen,
                    _engineState.ModuleName,
                    resolvedStructs,
                    collapsePointerNodes: CollapsePointerNodes,
                    maxDropDownEntries: DropDownLimit,
                    ceStringLength: CeStringLength,
                    resolvedInstances: resolvedInstances,
                    flattenChain: CollapseChain,
                    descShowOffset: DescShowOffset,
                    descShowType: DescShowType,
                    dedupShared: DedupSharedObjects,
                    excludeSystemComponents: ExcludeSystemComponents,
                    flattenGasAttributes: FlattenGasAttributes,
                    flattenLeafStructs: FlattenLeafStructs,
                    flattenLeafRecords: FlattenLeafRecords,
                    altColorEnabled: FlattenColorEnabled,
                    altRowColorEvenRgb: FlattenColorEven,
                    altRowColorOddRgb: FlattenColorOdd,
                    collapseLeafPointers: CollapseLeafPointers);
            }
            else
            {
                var rootAddress = AddressHelper.FormatAddress(
                    rootBc.Address, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
                xml = CeXmlExportService.GenerateHierarchicalXml(
                    rootAddress, rootBc.Label, breadcrumbsForXml, fieldsForXml, resolvedStructs,
                    collapsePointerNodes: CollapsePointerNodes,
                    maxDropDownEntries: DropDownLimit,
                    ceStringLength: CeStringLength,
                    resolvedInstances: resolvedInstances,
                    flattenChain: CollapseChain,
                    descShowOffset: DescShowOffset,
                    descShowType: DescShowType,
                    dedupShared: DedupSharedObjects,
                    excludeSystemComponents: ExcludeSystemComponents,
                    flattenGasAttributes: FlattenGasAttributes,
                    flattenLeafStructs: FlattenLeafStructs,
                    flattenLeafRecords: FlattenLeafRecords,
                    altColorEnabled: FlattenColorEnabled,
                    altRowColorEvenRgb: FlattenColorEven,
                    altRowColorOddRgb: FlattenColorOdd,
                    collapseLeafPointers: CollapseLeafPointers);
            }

            await _platform.CopyToClipboardAsync(xml);
            var limitWarn = BuildContainerLimitWarning(fieldsForXml, ArrayLimit);
            var aobFallbackWarn = (UseAobSymbol && !isGWorldRoot) ? "AOB skipped (no GWorld path)" : null;
            // Final indicator: objects (structs + pointer targets) walked + XML line count.
            int objCount = resolvedStructs.Count + resolvedInstances.Count;
            int lineCount = xml.Count(c => c == '\n') + 1;
            var statusExtra = aobFallbackWarn != null ? " " + aobFallbackWarn
                : (limitWarn != null ? " " + limitWarn : "");
            var truncWarn = CeXmlExportService.LastExportTruncated
                ? " ⚠ Truncated (object graph too large) — lower Drill Depth or use Copy CE Field"
                : "";
            var sysWarn = CeXmlExportService.LastSystemFieldsSkipped > 0
                ? $" {CeXmlExportService.LastSystemFieldsSkipped} system fields hidden"
                : "";
            StatusText = $"Copied: {objCount} objects, {lineCount} XML lines.{statusExtra}{truncWarn}{sysWarn}{reanchorWarn}";
            _log.Info($"CE XML copied to clipboard for {CurrentClassName} (AOB={useAob}, " +
                $"descOffset={DescShowOffset}, descType={DescShowType}, " +
                $"{resolvedStructs.Count} structs / {resolvedInstances.Count} pointers resolved, depth={CsxDrilldownDepth})");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // User hit Cancel — not an error; leave the clipboard untouched. The token
            // guard matters: a bare OCE that is NOT from our token (e.g. PipeClient's
            // "Pipe disconnected during send") must fall through to the generic handler
            // so a real mid-export disconnect isn't mislabeled as a user cancellation.
            StatusText = "Export cancelled.";
            _log.Info("CE XML export cancelled by user");
        }
        catch (Exception ex)
        {
            StatusText = "";
            SetError(ex);
            _log.Error("Failed to export CE XML", ex);
        }
        finally
        {
            IsLoading = false;
            IsExporting = false;
            if (ReferenceEquals(_exportCts, cts)) _exportCts = null;
            cts.Dispose();
        }
    }

    /// <summary>Abort the in-flight Copy CE XML / Copy CE Field export (cancels the
    /// ResolveDrilldown pipe-walk). Shared by both export commands — only one runs at a
    /// time (guarded by <see cref="IsExporting"/>). No-op if nothing is exporting.</summary>
    [RelayCommand]
    private void CancelExport() => _exportCts?.Cancel();

    // Two thin commands feed the Export CSX dropdown (LiveWalkerPanel.axaml): the legacy
    // Pre-CE-7.7 byte form and the CE 7.7+ Binary (bit-switch) form. Both delegate to one core.
    [RelayCommand]
    private Task ExportCsxPre77Async() => ExportCsxCoreAsync(CsxFormat.PreCe77);

    [RelayCommand]
    private Task ExportCsx77Async() => ExportCsxCoreAsync(CsxFormat.Ce77Plus);

    private async Task ExportCsxCoreAsync(CsxFormat format)
    {
        if (string.IsNullOrEmpty(CurrentAddress) || !HasData) return;
        if (IsExporting) return;   // an export is already running — its Cancel button is showing

        // The CTS is created only AFTER the save dialog, so cancel covers the abortable
        // resolve/write work — not the interactive dialog (which has its own cancel).
        CancellationTokenSource? cts = null;
        try
        {
            ClearStatus();

            // Build struct name: "ClassName_ObjectName" or "ClassName"
            var structName = !string.IsNullOrEmpty(CurrentObjectName)
                ? $"{CurrentClassName}_{CurrentObjectName}".Replace(" ", "_")
                : CurrentClassName.Replace(" ", "_");
            // Sanitize for file name and XML attribute
            structName = structName.Replace("<", "").Replace(">", "").Replace("\"", "");
            // Sanitize for file system: remove invalid chars
            var safeFileName = string.Join("_",
                structName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            // Show save-file dialog; user picks folder + file name
            var filePath = await _platform.ShowSaveFileDialogAsync(
                safeFileName, "CE Structure Dissect (*.CSX)", ".CSX");
            if (string.IsNullOrEmpty(filePath)) return; // user cancelled the save dialog

            cts = _exportCts = new CancellationTokenSource();
            IsLoading = true;
            IsExporting = true;
            StatusText = CsxDrilldownDepth > 0 ? "Resolving struct + pointer fields..." : "Resolving struct fields...";
            var csx = await CsxExportService.GenerateCsxAsync(
                _dump, structName, Fields, arrayLimit: ArrayLimit, drilldownDepth: CsxDrilldownDepth,
                format: format, ct: cts.Token);

            // Write to file (overwrite if exists — user already confirmed via dialog). No
            // token here on purpose: cancel aborts the slow resolve above; once we have the
            // full CSX we let the quick write finish so a completed export is never truncated.
            await File.WriteAllTextAsync(filePath, csx);

            // Surface a truncation note so a partial export (a container clipped by
            // ArrayLimit) doesn't silently read as complete — same note Copy CE XML shows.
            var limitWarn = BuildContainerLimitWarning(Fields, ArrayLimit);
            StatusText = limitWarn ?? "";
            var formatLabel = format == CsxFormat.Ce77Plus ? "CE 7.7+ Binary" : "Pre-CE 7.7";
            _log.Info($"CSX ({formatLabel}) exported to {filePath} for {CurrentClassName}"
                + (limitWarn != null ? $" ({limitWarn})" : ""));
        }
        catch (OperationCanceledException) when (cts is { IsCancellationRequested: true })
        {
            // User hit Cancel during the resolve — not an error. No file was written (the
            // OCE unwinds before WriteAllTextAsync), so nothing partial is left behind.
            StatusText = "Export cancelled.";
            _log.Info("CSX export cancelled by user");
        }
        catch (UnauthorizedAccessException)
        {
            StatusText = "";
            SetError("Cannot write to the selected location — access denied.");
            _log.Error("CSX export failed: access denied");
        }
        catch (Exception ex)
        {
            StatusText = "";
            SetError(ex);
            _log.Error("Failed to export CSX", ex);
        }
        finally
        {
            IsLoading = false;
            // Only touch the export flag/CTS if we actually started one (past the dialog).
            if (cts != null)
            {
                IsExporting = false;
                if (ReferenceEquals(_exportCts, cts)) _exportCts = null;
                cts.Dispose();
            }
        }
    }

    [RelayCommand]
    private async Task ExportCeFieldXmlAsync()
    {
        // Use the multi-selection snapshot; fall back to SelectedField for
        // robustness if SelectionChanged hasn't synced yet (e.g. when the
        // command fires programmatically right after a single-row selection).
        var selectedSnapshot = _selectedFieldsSnapshot.Count > 0
            ? new List<LiveFieldValue>(_selectedFieldsSnapshot)
            : (SelectedField != null ? new List<LiveFieldValue> { SelectedField } : new List<LiveFieldValue>());

        if (selectedSnapshot.Count == 0 || string.IsNullOrEmpty(CurrentAddress) || Breadcrumbs.Count == 0) return;

        // Record what this heavy operation costs the DLL dispatcher. Automatic
        // rather than a measurement session: the evidence then accumulates from
        // real use instead of only the scenario somebody thought to test. Degrades
        // to a no-op when not connected; never affects the operation.
        //
        // AFTER the early return, like ExportCeXmlAsync above — opening it first spent two
        // get_diagnostics round-trips and filed a "Copy CE Field" sample every time the
        // command fired with nothing selected. (audit #5 AE8, sibling site)
        await using var _perf = await Services.DiagnosticsProbe.BeginAsync(_dump, _log, "Copy CE Field");
        if (IsExporting) return;   // an export is already running — its Cancel button is showing

        // Guessed ("Guess?") fields export only when the user explicitly focuses
        // guessed field(s) — i.e. the whole selection is guessed. A mixed or reflected
        // selection (and Copy CE XML / container exports) drops guessed rows so a bulk
        // export never silently dumps speculative guesses. Guessed fields are always
        // scalar leaves, so an all-guessed selection has no children to recurse into.
        bool includeGuessed = selectedSnapshot.All(f => f.IsGuessed);

        var cts = _exportCts = new CancellationTokenSource();
        try
        {
            ClearStatus();
            IsLoading = true;
            IsExporting = true;

            // Collapse consecutive duplicate crumbs FIRST (before the container
            // split below), so a redundant trailing container crumb — e.g. a
            // Locate-in-GWorld path-synthetic SpawnedAttributes(C) followed by the
            // user re-entering that same container — doesn't leave one copy in the
            // spine while the other becomes the field (which double-derefs the array
            // field offset). The later duplicate is kept (it carries the live
            // ContainerField needed by FilterContainerToElement).
            var dedupedBc = CeXmlExportService.DedupeConsecutiveBreadcrumbs(Breadcrumbs);

            // Container view: strip container breadcrumb, build ONE filtered
            // ContainerField containing all selected elements (preserves CE's
            // hierarchical structure — header + nested elements under same
            // pointer chain — instead of N detached top-level entries).
            var lastBc = dedupedBc[^1];
            var isContainerView = lastBc.IsContainerView && lastBc.ContainerField != null;

            IReadOnlyList<BreadcrumbItem> breadcrumbsForXml;
            List<LiveFieldValue> fieldsForXml;

            // Struct-array elements: in the array container view each element row is a
            // StructProperty navigation (StructDataAddr = element address, StructClassAddr =
            // element UScriptStruct). The shallow read_array_elements preview only carries
            // scalar/pointer sub-fields, so the FilterContainerToElement path below would drop
            // nested struct/map fields. Instead keep the array breadcrumb (its Offsets=[0]
            // derefs TArray::Data) and export the selected element rows AS struct fields, so
            // ResolveStructFieldsAsync re-walks each element in full — nested structs/maps
            // expand exactly like drilling into the element.
            bool isStructElementSelection = isContainerView
                && lastBc.ContainerField!.ArrayInnerType == "StructProperty"
                && !string.IsNullOrEmpty(lastBc.ContainerField.ArrayStructClassAddr)
                && selectedSnapshot.Count > 0
                && selectedSnapshot.All(f => f.TypeName == "StructProperty"
                                             && !string.IsNullOrEmpty(f.StructDataAddr));

            if (isStructElementSelection)
            {
                breadcrumbsForXml = dedupedBc;
                fieldsForXml = selectedSnapshot;
            }
            else if (isContainerView)
            {
                breadcrumbsForXml = dedupedBc.Take(dedupedBc.Count - 1).ToList();
                fieldsForXml = new List<LiveFieldValue>
                    { FilterContainerToElement(lastBc.ContainerField!, selectedSnapshot) };
            }
            else
            {
                breadcrumbsForXml = dedupedBc;
                fieldsForXml = selectedSnapshot;
            }

            // Same re-anchor as Copy CE XML — see the comment there. Both entry points do
            // it because both feed the generator, and AnchorAtLastUnchainableHop is
            // idempotent, so a spine that is already anchored passes through untouched.
            var reanchoredForXml = CeXmlExportService.AnchorAtLastUnchainableHop(breadcrumbsForXml);
            var reanchorWarn = ReanchorNote(breadcrumbsForXml, reanchoredForXml);
            LogReanchor(breadcrumbsForXml, reanchoredForXml);
            breadcrumbsForXml = reanchoredForXml;

            var fieldSummary = selectedSnapshot.Count == 1
                ? $"field={selectedSnapshot[0].Name}"
                : $"fields={selectedSnapshot.Count}({string.Join(",", selectedSnapshot.Take(5).Select(f => f.Name))}{(selectedSnapshot.Count > 5 ? "…" : "")})";
            _log.Info($"CEFieldXML export: {fieldSummary} containerView={isContainerView} bcCount={breadcrumbsForXml.Count} | BC={FormatBreadcrumbTrace()}");

            // Pre-check CleanBreadcrumbs to log any cycle removals
            var cleaned = CeXmlExportService.CleanBreadcrumbs(breadcrumbsForXml);
            if (cleaned.Count != breadcrumbsForXml.Count)
            {
                _log.Info($"CEFieldXML CleanBC: {breadcrumbsForXml.Count}→{cleaned.Count} removed={breadcrumbsForXml.Count - cleaned.Count}");
                for (int i = 0; i < cleaned.Count; i++)
                {
                    var bc = cleaned[i];
                    var flags = bc.IsContainerView ? "C" : bc.IsPointerDeref ? "P" : "S";
                    _log.Info($"  [{i}] {bc.FieldName ?? bc.Label} ({flags}) off={FormatCrumbOffset(bc.FieldOffset)} addr={bc.Address}");
                }
            }

            // Unified drilldown resolve (docs/ce-export-drilldown-spec.md Phase A) —
            // structs + pointers + container element values (Map/Set/struct-array
            // struct/object values), recursively to CsxDrilldownDepth.
            StatusText = CsxDrilldownDepth > 0
                ? "Resolving struct + pointer + container fields..."
                : "Resolving struct fields...";
            var resolvedStructs = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
            var resolvedInstances = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
            int lastShown = 0;
            await CeXmlExportService.ResolveDrilldownAsync(
                _dump, fieldsForXml, resolvedStructs, resolvedInstances,
                depth: CsxDrilldownDepth, arrayLimit: ArrayLimit,
                onWalk: () =>
                {
                    // Live indicator: objects (structs + pointer targets) resolved so far,
                    // throttled so a deep/wide map doesn't spam the bound StatusText.
                    int n = resolvedStructs.Count + resolvedInstances.Count;
                    if (n - lastShown >= 16) { lastShown = n; StatusText = $"Resolving… {n} objects"; }
                },
                // Lean payload: this resolve feeds a CE XML export, which reads
                // structure (name/offset/type/drill-down) and never a live VALUE.
                // Measured at ~24-38% fewer bytes (multipipe-eval.md 10.6), which is
                // the payload-proportional IPC + UI parse that batching cannot touch.
                lean: true,
                ct: cts.Token);

            var rootBc = breadcrumbsForXml[0];

            // Same GWorld-root guard as ExportCeXmlAsync
            var isGWorldRoot = rootBc.FieldName == "GWorld";
            var useAob = UseAobSymbol && isGWorldRoot && !string.IsNullOrEmpty(_engineState?.GWorldAob);
            if (UseAobSymbol && !isGWorldRoot)
                _log.Info("CEFieldXML: AOB requested but root is not GWorld — falling back to direct address");

            StatusText = "Generating CE Field XML...";
            string xml;
            if (useAob)
            {
                xml = CeXmlExportService.GenerateAobWrappedXml(
                    rootBc.Label, breadcrumbsForXml, fieldsForXml,
                    _engineState!.GWorldAob, _engineState.GWorldAobPos, _engineState.GWorldAobLen,
                    _engineState.ModuleName,
                    resolvedStructs,
                    collapsePointerNodes: CollapsePointerNodes,
                    maxDropDownEntries: DropDownLimit,
                    ceStringLength: CeStringLength,
                    resolvedInstances: resolvedInstances,
                    flattenChain: CollapseChain,
                    includeGuessed: includeGuessed,
                    fabricateArrayCount: FabricateArrayCount,
                    descShowOffset: DescShowOffset,
                    descShowType: DescShowType,
                    dedupShared: DedupSharedObjects,
                    excludeSystemComponents: ExcludeSystemComponents,
                    flattenGasAttributes: FlattenGasAttributes,
                    flattenLeafStructs: FlattenLeafStructs,
                    flattenLeafRecords: FlattenLeafRecords,
                    altColorEnabled: FlattenColorEnabled,
                    altRowColorEvenRgb: FlattenColorEven,
                    altRowColorOddRgb: FlattenColorOdd,
                    collapseLeafPointers: CollapseLeafPointers);
            }
            else
            {
                var rootAddress = AddressHelper.FormatAddress(
                    rootBc.Address, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
                xml = CeXmlExportService.GenerateHierarchicalXml(
                    rootAddress, rootBc.Label, breadcrumbsForXml, fieldsForXml, resolvedStructs,
                    collapsePointerNodes: CollapsePointerNodes,
                    maxDropDownEntries: DropDownLimit,
                    ceStringLength: CeStringLength,
                    resolvedInstances: resolvedInstances,
                    flattenChain: CollapseChain,
                    includeGuessed: includeGuessed,
                    fabricateArrayCount: FabricateArrayCount,
                    descShowOffset: DescShowOffset,
                    descShowType: DescShowType,
                    dedupShared: DedupSharedObjects,
                    excludeSystemComponents: ExcludeSystemComponents,
                    flattenGasAttributes: FlattenGasAttributes,
                    flattenLeafStructs: FlattenLeafStructs,
                    flattenLeafRecords: FlattenLeafRecords,
                    altColorEnabled: FlattenColorEnabled,
                    altRowColorEvenRgb: FlattenColorEven,
                    altRowColorOddRgb: FlattenColorOdd,
                    collapseLeafPointers: CollapseLeafPointers);
            }

            await _platform.CopyToClipboardAsync(xml);
            var limitWarn = BuildContainerLimitWarning(fieldsForXml, ArrayLimit);
            var aobFallbackWarn = (UseAobSymbol && !isGWorldRoot) ? "AOB skipped (no GWorld path)" : null;
            // Final indicator: objects (structs + pointer targets) walked + XML line count.
            int objCount = resolvedStructs.Count + resolvedInstances.Count;
            int lineCount = xml.Count(c => c == '\n') + 1;
            var statusExtra = aobFallbackWarn != null ? " " + aobFallbackWarn
                : (limitWarn != null ? " " + limitWarn : "");
            var truncWarn = CeXmlExportService.LastExportTruncated
                ? " ⚠ Truncated (object graph too large) — lower Drill Depth or use Copy CE Field"
                : "";
            var sysWarn = CeXmlExportService.LastSystemFieldsSkipped > 0
                ? $" {CeXmlExportService.LastSystemFieldsSkipped} system fields hidden"
                : "";
            StatusText = $"Copied: {objCount} objects, {lineCount} XML lines.{statusExtra}{truncWarn}{sysWarn}{reanchorWarn}";
            _log.Info($"CE Field XML copied: {selectedSnapshot.Count} field(s) (AOB={useAob}, includeGuessed={includeGuessed}, " +
                $"descOffset={DescShowOffset}, descType={DescShowType}, " +
                $"{resolvedInstances.Count} pointer targets resolved at depth={CsxDrilldownDepth})");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // User hit Cancel — not an error; leave the clipboard untouched. The token
            // guard matters: a bare OCE that is NOT from our token (e.g. PipeClient's
            // "Pipe disconnected during send") must fall through to the generic handler
            // so a real mid-export disconnect isn't mislabeled as a user cancellation.
            StatusText = "Export cancelled.";
            _log.Info("CE Field XML export cancelled by user");
        }
        catch (Exception ex)
        {
            StatusText = "";
            SetError(ex);
            _log.Error("Failed to export CE Field XML", ex);
        }
        finally
        {
            IsLoading = false;
            IsExporting = false;
            if (ReferenceEquals(_exportCts, cts)) _exportCts = null;
            cts.Dispose();
        }
    }

    /// <summary>
    /// Direct-push variant of Copy CE Field: instead of generating CE XML to the clipboard,
    /// push each selected field straight into CE's address list as a typed memory record via
    /// the AOBMaker plugin (the multi-select batch form of the per-row +CE button). This is a
    /// FLAT push — one top-level record per selected field, typed via
    /// <see cref="CeXmlExportService.MapFieldToCeRecordType"/>. It intentionally does NOT
    /// reproduce the hierarchical pointer-chain / container-element structure that
    /// <see cref="ExportCeFieldXmlAsync"/> builds for the clipboard; use Copy CE Field for that.
    /// </summary>
    [RelayCommand]
    private async Task PushCeFieldToCeAsync()
    {
        if (_aobMaker == null) return;

        // Same selection source as ExportCeFieldXmlAsync (snapshot, falling back to the
        // single selected row if SelectionChanged hasn't synced yet).
        var selected = _selectedFieldsSnapshot.Count > 0
            ? new List<LiveFieldValue>(_selectedFieldsSnapshot)
            : (SelectedField != null ? new List<LiveFieldValue> { SelectedField } : new List<LiveFieldValue>());
        if (selected.Count == 0)
        {
            StatusText = "No fields selected";
            return;
        }

        try
        {
            ClearStatus();
            int ok = 0, fail = 0, skipped = 0;
            foreach (var field in selected)
            {
                // Fields without a resolved address (e.g. container/struct headers) can't
                // become a flat record — skip rather than push a bogus address.
                if (string.IsNullOrEmpty(field.FieldAddress)) { skipped++; continue; }

                var t = CeXmlExportService.MapFieldToCeRecordType(field);
                var added = await _aobMaker.CreateMemoryRecordAsync(
                    Services.PackedLayoutNotice.RecordNamePrefix + field.Name,
                    StripHexPrefix(field.FieldAddress), t.ValueType, t.IsSigned, t.ShowAsHex);
                if (added)
                {
                    ok++;
                }
                else
                {
                    fail++;
                    // If the bridge lost the pipe (CE closed mid-batch) stop now rather than
                    // eating one 2 s connect timeout per remaining field.
                    if (!_aobMaker.IsAvailable) break;
                }
            }

            IsAobMakerAvailable = _aobMaker.IsAvailable;
            if (!_aobMaker.IsAvailable && ok == 0)
            {
                StatusText = "AOBMaker not connected — open CE with the plugin loaded";
            }
            else
            {
                var extra = (fail > 0 ? $", {fail} failed" : "") + (skipped > 0 ? $", {skipped} skipped" : "");
                StatusText = $"Added to CE: {ok} record(s){extra}";
            }
            _log.Info($"CE Field push: {ok} added, {fail} failed, {skipped} skipped (of {selected.Count} selected)");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to push CE Field records to CE", ex);
        }
    }

    /// <summary>
    /// Compute CE-compatible "Module.exe"+RVA string from an absolute address.
    /// </summary>
    private string ComputeModuleRva(string hexAddr)
    {
        var addr = Convert.ToUInt64(hexAddr.Replace("0x", "").Replace("0X", ""), 16);
        var moduleBase = Convert.ToUInt64(_engineState!.ModuleBase.Replace("0x", "").Replace("0X", ""), 16);
        var rva = addr - moduleBase;
        return $"\"{_engineState.ModuleName}\"+{rva:X}";
    }

    [RelayCommand]
    private async Task GenerateCeAAScriptAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress)) return;

        try
        {
            ClearStatus();
            // Sanitize to a valid CE symbol / XML token — container leaves carry
            // class names like "Array<int>" / "Map<FName, int>" whose < > , would
            // otherwise corrupt the <Description> XML and the registerSymbol name.
            var symbolName = SanitizeCeSymbol(CurrentClassName);

            var (xml, note) = BuildAaScript(symbolName);

            // When the AOBMaker plugin is reachable, push the AA script straight into
            // CE's address list (same one-click handoff as the per-field +CE buttons)
            // instead of only copying XML to the clipboard. CreateAAScript wants the
            // raw assembler body — the un-escaped text inside <AssemblerScript> — not
            // the XML wrapper. autoActivate:false matches clipboard semantics: the
            // entry lands in the list disabled; the user ticks it to register the
            // symbol (and, on a GWorld-walked script, run the walk).
            bool wasAvailable = _aobMaker?.IsAvailable ?? false;
            bool sentToCe = false;
            if (_aobMaker != null && wasAvailable)
            {
                var script = CeXmlExportService.ExtractAssemblerScript(xml);
                if (!string.IsNullOrEmpty(script))
                    sentToCe = await _aobMaker.CreateAAScriptAsync(
                        $"\"{symbolName}\"", script, autoActivate: false);
                IsAobMakerAvailable = _aobMaker.IsAvailable;
            }

            if (sentToCe)
            {
                StatusText = $"AA script added to CE address list — {note} (tick it to register the symbol)";
                _log.Info($"CE AA script pushed to CE for {CurrentClassName} — {note}");
            }
            else
            {
                await _platform.CopyToClipboardAsync(xml);
                StatusText = wasAvailable
                    ? $"⚠ AOBMaker pipe broke (CE closed?) — CE AA script copied to clipboard — {note}"
                    : $"CE AA script copied — {note}";
                _log.Info($"CE AA script copied for {CurrentClassName} — {note}");
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to generate CE AA script", ex);
        }
    }

    /// <summary>
    /// Build the "Copy CE AA Script" output. When the live path is rooted at
    /// GWorld and forward-walkable, emit a RESTART-STABLE script that walks
    /// GWorld → … → this object at enable time — AOB-anchored when GWorld itself
    /// came from an AOB scan (UseAobSymbol + a known GWorld AOB), otherwise a
    /// hardcoded GWorld base the user updates after a restart. Any other path
    /// keeps the legacy hardcoded absolute address (dies on ASLR, but is all we
    /// can do off a non-GWorld root). Returns the XML + a one-line status note.
    /// </summary>
    private (string xml, string note) BuildAaScript(string symbolName)
    {
        var spine = CeXmlExportService.CleanBreadcrumbs(
            CeXmlExportService.DedupeConsecutiveBreadcrumbs(Breadcrumbs));

        // Gate: rooted at the synthetic GWorld crumb, every later crumb is a real
        // forward offset (a WorldLevel back-reference recovery hop carries
        // FieldOffset -1 and cannot be reproduced by a forward walk), and the
        // spine actually lands on the object we're about to register.
        bool gworldWalkable =
            spine.Count > 0
            && spine[0].FieldName == "GWorld"
            && spine.Skip(1).All(bc => bc.FieldOffset >= 0)
            && AddressesEqual(spine[^1].Address, CurrentAddress);

        if (gworldWalkable)
        {
            // Respect the AOB checkbox (same condition as Copy CE Field's useAob):
            // unchecked → hardcoded GWorld base even when an AOB is available.
            var useAob = UseAobSymbol && !string.IsNullOrEmpty(_engineState?.GWorldAob);
            if (useAob)
            {
                return (CeXmlExportService.GenerateGWorldWalkedSymbolXml(
                            symbolName, spine, useAob: true,
                            _engineState!.GWorldAob, _engineState.GWorldAobPos, _engineState.GWorldAobLen,
                            gworldSlotAddr: ""),
                        "GWorld AOB walk (restart-stable)");
            }
            if (_engineState != null && _engineState.HasGWorld)
            {
                return (CeXmlExportService.GenerateGWorldWalkedSymbolXml(
                            symbolName, spine, useAob: false,
                            aob: "", aobPos: 0, aobLen: 0,
                            gworldSlotAddr: _engineState.GWorldAddr),
                        "GWorld hardcoded-base walk — update the GWorld value after a restart");
            }
        }

        // Legacy hardcoded address. Distinguish "root isn't GWorld" from "GWorld
        // root but the spine can't be forward-walked" (a WorldLevel -1 hop, a
        // spine that doesn't land on the object, or no resolvable GWorld base) so
        // the status line isn't misleading.
        var note = (spine.Count > 0 && spine[0].FieldName == "GWorld")
            ? "hardcoded address (GWorld path not forward-walkable)"
            : "hardcoded address (not a GWorld-rooted path)";
        var formattedAddr = AddressHelper.FormatAddress(
            CurrentAddress, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
        return (CeXmlExportService.GenerateRegisterSymbolXml(symbolName, formattedAddr), note);
    }

    /// <summary>Compare two CE address strings for equality, tolerant of
    /// 0x-prefix and hex-digit casing. Returns false if either fails to parse.</summary>
    private static bool AddressesEqual(string a, string b)
        => AddressHelper.TryNormalizeAddress(a, null, out var na)
           && AddressHelper.TryNormalizeAddress(b, null, out var nb)
           && string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reduce a class name to a valid CE symbol / XML token: every char
    /// that isn't a letter, digit, or underscore becomes '_' (so container names
    /// like "Array&lt;int&gt;" / "Map&lt;FName, int&gt;" can't corrupt the XML or the
    /// registerSymbol name). Falls back to "UE5_Symbol" when nothing survives.</summary>
    private static string SanitizeCeSymbol(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "UE5_Symbol";
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var s = new string(chars).Trim('_');
        return string.IsNullOrEmpty(s) ? "UE5_Symbol" : s;
    }

    [RelayCommand]
    private async Task ExportSdkHeaderAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress) || !HasData) return;

        try
        {
            ClearStatus();

            var structName = !string.IsNullOrEmpty(CurrentObjectName)
                ? $"{CurrentClassName}_{CurrentObjectName}".Replace(" ", "_")
                : CurrentClassName.Replace(" ", "_");
            structName = structName.Replace("<", "").Replace(">", "").Replace("\"", "");
            var safeFileName = string.Join("_",
                structName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            var filePath = await _platform.ShowSaveFileDialogAsync(
                safeFileName, "C++ Header (*.h)", ".h");
            if (string.IsNullOrEmpty(filePath)) return;

            IsLoading = true;
            StatusText = "Generating SDK header...";

            // Get the superclass name from the first breadcrumb's class info if available
            var superName = "";
            var superPropsSize = 0;
            if (Breadcrumbs.Count > 0)
            {
                var bc = Breadcrumbs[^1];
                if (!string.IsNullOrEmpty(bc.ClassAddr))
                {
                    try
                    {
                        var classInfo = await _dump.WalkClassAsync(bc.ClassAddr);
                        superName = classInfo.SuperName;
                        // Where this class's own properties start — without it the header
                        // re-declares every inherited property (audit #5 W2).
                        superPropsSize = classInfo.SuperPropertiesSize;
                    }
                    catch
                    {
                        // Non-critical — just emit without super
                    }
                }
            }

            // Estimate properties size from the last field end or use a safe heuristic
            var propsSize = 0;
            if (Fields.Count > 0)
            {
                var lastField = Fields.OrderByDescending(f => f.Offset + f.Size).First();
                propsSize = lastField.Offset + lastField.Size;
            }

            var header = SdkExportService.GenerateClassHeader(
                CurrentClassName, superName, propsSize, Fields.ToList(),
                fullPath: null, superPropsSize: superPropsSize);

            await File.WriteAllTextAsync(filePath, header);

            StatusText = "";
            _log.Info($"SDK header exported to {filePath} for {CurrentClassName}");
        }
        catch (UnauthorizedAccessException)
        {
            StatusText = "";
            SetError("Cannot write to the selected location — access denied.");
            _log.Error("SDK header export failed: access denied");
        }
        catch (Exception ex)
        {
            StatusText = "";
            SetError(ex);
            _log.Error("Failed to export SDK header", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress)) return;

        // Re-check AOBMaker CE Plugin availability (detects CE start/close, cooldown-throttled)
        TryCheckAobMaker();

        // Snapshot address before async call — if user navigates while we're awaiting,
        // CurrentAddress will differ and we discard the stale result.
        var addressAtStart = CurrentAddress;
        var breadcrumbCountAtStart = Breadcrumbs.Count;

        // Remember the selected row so a refresh (manual or auto) lands back on
        // it instead of resetting to the top. UpdateDisplay either replaces the
        // field objects in-place (drops the selection binding) or fully rebuilds
        // (drops scroll too); restoring by name+offset covers both. Empty when
        // nothing is selected, so we never yank an un-selected list around.
        var keepFieldName   = SelectedField?.Name;
        var keepFieldOffset = SelectedField?.Offset ?? int.MinValue;

        // Hard deadline: if the DLL hangs walking a recycled/destroyed object,
        // cancel the pipe request instead of leaving IsLoading stuck forever.
        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(Constants.LiveWalkerRefreshTimeoutMs));
        var ct = timeoutCts.Token;

        try
        {
            ClearStatus();
            IsLoading = true;

            // If refreshing a container view, re-walk the parent instance and re-extract container data.
            // Path-synthetic container crumbs (from PathStepToBreadcrumbs) carry no live ContainerField,
            // so match by the crumb's own field name+offset in that case — otherwise refresh would skip
            // this branch and re-walk a stale address, reverting the re-hydrated container to a grid
            // (mirrors TryRepopulateSyntheticContainerAsync on the Back-nav side).
            if (Breadcrumbs.Count > 0 && Breadcrumbs[^1].IsContainerView)
            {
                var containerBc = Breadcrumbs[^1];

                // DataTable container: re-fetch rows directly
                if (containerBc.IsDataTableView)
                {
                    var dtResult = await _dump.WalkDataTableRowsAsync(containerBc.Address, ct: ct);
                    if (CurrentAddress != addressAtStart || Breadcrumbs.Count != breadcrumbCountAtStart) return;
                    containerBc.DataTableData = dtResult;
                    PopulateDataTableRowFields(dtResult);
                    return;
                }

                // Re-walk the parent instance to get fresh container data
                string? parentClassAddr = null;
                if (Breadcrumbs.Count >= 2)
                {
                    var parentBc = Breadcrumbs[^2];
                    if (!string.IsNullOrEmpty(parentBc.ClassAddr))
                        parentClassAddr = parentBc.ClassAddr;
                }

                var parentResult = await _dump.WalkInstanceAsync(containerBc.Address, parentClassAddr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit, fillGaps: FillGaps, ct: ct);
                if (CurrentAddress != addressAtStart || Breadcrumbs.Count != breadcrumbCountAtStart) return;

                // Find the container field by name and offset in the refreshed result. Use the live
                // ContainerField identity when present, else the crumb's own field name+offset.
                var matchName   = containerBc.ContainerField?.Name   ?? containerBc.FieldName;
                var matchOffset = containerBc.ContainerField?.Offset ?? containerBc.FieldOffset;
                var updatedField = parentResult.Fields
                    .FirstOrDefault(f => f.Name == matchName && f.Offset == matchOffset);

                if (updatedField != null)
                {
                    RepopulateContainerView(updatedField);
                    RestoreSelectedField(keepFieldName, keepFieldOffset);
                }
                return;
            }

            // If refreshing GWorld view (first breadcrumb only), re-fetch the world.
            // Must check Breadcrumbs.Count == 1 because a sub-World (e.g. S01L04) can share
            // the same address as GWorld — without this guard, auto-refresh at deeper levels
            // would incorrectly show the GWorld actor list instead of instance fields.
            if (_cachedWorld != null && CurrentAddress == _cachedWorld.WorldAddr
                && Breadcrumbs.Count == 1)
            {
                var world = await _dump.WalkWorldAsync(Constants.WorldWalkMaxDepth, arrayLimit: ArrayLimit, ct: ct);
                if (CurrentAddress != addressAtStart || Breadcrumbs.Count != breadcrumbCountAtStart) return;
                _cachedWorld = world;
                PopulateFromWorld(world);
                RestoreSelectedField(keepFieldName, keepFieldOffset);
                return;
            }

            // Pass ClassAddr from current breadcrumb (needed for StructProperty context;
            // without it the DLL interprets struct memory as UObject → garbage → empty grid)
            string? classAddr = null;
            if (Breadcrumbs.Count > 0)
            {
                var current = Breadcrumbs[^1];
                if (!string.IsNullOrEmpty(current.ClassAddr))
                    classAddr = current.ClassAddr;
            }

            var result = await _dump.WalkInstanceAsync(CurrentAddress, classAddr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit, fillGaps: FillGaps, ct: ct);
            result = await AutoFillGapsRetryAsync(result, CurrentAddress, classAddr);
            if (CurrentAddress != addressAtStart || Breadcrumbs.Count != breadcrumbCountAtStart) return;
            // Refresh re-walks the SAME object — keep the active field-search filter.
            UpdateDisplay(result, clearFieldSearch: false);
            RestoreSelectedField(keepFieldName, keepFieldOffset);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            SetError(new TimeoutException(
                $"Refresh timed out after {Constants.LiveWalkerRefreshTimeoutMs / 1000}s — " +
                "the target object may have been destroyed in-game."));
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// After a refresh rebuilds the field grid, re-select the row that was
    /// selected before (matched by name, preferring the same byte offset) and
    /// scroll it back into view — so Refresh / auto-refresh doesn't reset to the
    /// top. No-op when nothing was selected or the field is gone.
    /// </summary>
    private void RestoreSelectedField(string? name, int offset)
    {
        // ⚠ CLEAR, don't just return. Refresh replaces the rows in place
        // (`Fields[i] = newFields[i]`), which the DataGridCollectionView splits into a
        // Remove+Add per row; each one nudges CURRENCY, and the TwoWay SelectedItem
        // binding writes whatever row currency landed on back into SelectedField. So by
        // the time we get here the grid may already have invented a selection the user
        // never made — measured against the real DataGrid as ~N CurrentChanged events
        // with currency settling near the end of the list.
        //
        // That is the reported defect: search "RemoteRole", press Refresh, and the UI
        // selects an unrelated row (0x720 CachedConnectionPlayerId) that is merely near
        // the bottom of the realized range. `[LWREFRESH-2026-08-21]`
        //
        // All three callers are inside RefreshAsync and pass the SAME name captured
        // before the walk, so an empty name provably means "nothing was selected before"
        // — and the honest restore of "nothing" is null, not "leave the phantom".
        if (string.IsNullOrEmpty(name)) { SelectedField = null; return; }

        LiveFieldValue? exact = null, byName = null;
        foreach (var f in Fields)
        {
            if (f.Name != name) continue;
            byName ??= f;
            if (f.Offset == offset) { exact = f; break; }
        }

        var hit = exact ?? byName;
        // Same reasoning when the previously-selected field is GONE from the new walk:
        // we cannot restore what the user had, and a grid-invented row is worse than an
        // empty selection because the next action (copy address, drill, freeze) would
        // silently act on it.
        if (hit == null) { SelectedField = null; return; }

        SelectedField = hit;
        ScrollToFieldRequested?.Invoke(hit.Name);
    }

    [RelayCommand]
    private async Task CopyFieldAddressAsync(LiveFieldValue? field)
    {
        if (field == null) return;

        try
        {
            // Prefer the field's already-resolved absolute address (the same value
            // shown in the Address column and used by the Hex / +CE / Edit buttons).
            // Only fall back to CurrentAddress + Offset when it's missing:
            // CurrentAddress is the OWNING struct, which is WRONG for a container-
            // element view — the element lives in a separate heap buffer
            // (TArray::Data / TSparseArray::Data), not at owner+Offset. Recomputing
            // there landed on the owning struct's field at the same offset instead.
            string hexAddr;
            if (!string.IsNullOrEmpty(field.FieldAddress) && field.FieldAddress != "0x0")
            {
                hexAddr = field.FieldAddress;
            }
            else if (!string.IsNullOrEmpty(CurrentAddress))
            {
                var instanceAddr = Convert.ToUInt64(CurrentAddress.Replace("0x", "").Replace("0X", ""), 16);
                hexAddr = $"0x{instanceAddr + (ulong)field.Offset:X}";
            }
            else return;

            var formatted = AddressHelper.FormatAddress(
                hexAddr, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
            await _platform.CopyToClipboardAsync(formatted);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy address for {field.Name}", ex);
        }
    }

    [RelayCommand]
    private async Task CopyFieldNameAsync(LiveFieldValue? field)
    {
        if (field == null || string.IsNullOrEmpty(field.Name)) return;

        try
        {
            await _platform.CopyToClipboardAsync(field.Name);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy name for {field.Name}", ex);
        }
    }

    /// <summary>Open the Instance Finder for this field's pointed-to object class.
    /// Only meaningful for object-pointer fields — PtrClassName holds the runtime
    /// class of the referenced object (empty for scalars / null pointers), which
    /// also gates the button's visibility. MainWindow switches to the Instance
    /// Finder tab and runs the search.</summary>
    [RelayCommand]
    private void OpenFieldInInstanceFinder(LiveFieldValue? field)
    {
        if (field == null || string.IsNullOrEmpty(field.PtrClassName)) return;
        NavigateToInstanceFinder?.Invoke(field.PtrClassName);
    }

    [RelayCommand]
    private async Task CopyPtrAddressAsync(LiveFieldValue? field)
    {
        if (field == null || string.IsNullOrEmpty(field.PtrAddress)) return;

        try
        {
            var formatted = AddressHelper.FormatAddress(
                field.PtrAddress, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
            await _platform.CopyToClipboardAsync(formatted);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy ptr address for {field.Name}", ex);
        }
    }

    // --- AOBMaker CE Plugin: hex view navigation ---

    /// <summary>Check AOBMaker availability (called after data load).</summary>
    public async Task CheckAobMakerAsync()
    {
        if (_aobMaker == null) return;
        _lastAobMakerCheck = DateTime.UtcNow;
        try
        {
            IsAobMakerAvailable = await _aobMaker.CheckAvailabilityAsync();
        }
        catch { IsAobMakerAvailable = false; }
    }

    /// <summary>
    /// Fire-and-forget AOBMaker availability check with cooldown.
    /// Detects both CE starting (buttons enable) and CE closing (buttons disable).
    /// Skips if last check was within <see cref="AobMakerCheckCooldown"/> to avoid
    /// spamming pipe connects on rapid navigation (2s timeout when CE not running).
    /// Public so MainWindow's tab-switch handler can also re-check on tab activation.
    /// </summary>
    public void TryCheckAobMaker()
    {
        if (_aobMaker == null) return;
        if (DateTime.UtcNow - _lastAobMakerCheck < AobMakerCheckCooldown) return;
        _ = CheckAobMakerAsync();
    }

    /// <summary>Strip leading "0x" prefix for AOBMaker hex navigation.</summary>
    private static string StripHexPrefix(string addr)
        => addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? addr[2..] : addr;

    // --- Live Edit Mode ---

    /// <summary>Whether a field value is currently being edited. Suppresses auto-refresh.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Commit an inline field edit: validate, convert to bytes, write to game memory.
    /// </summary>
    public async Task CommitFieldEditAsync(LiveFieldValue field, string newValue)
    {
        if (field == null || string.IsNullOrEmpty(field.FieldAddress)) return;

        try
        {
            ClearStatus();

            // BoolProperty: read-modify-write with bitmask
            if (field.TypeName == "BoolProperty")
            {
                if (!FieldValueConverter.TryParseBool(newValue, out var boolVal))
                {
                    StatusText = $"Invalid bool value: {newValue} (expected true/false/1/0)";
                    return;
                }

                // Write address = field address + boolByteOffset
                var baseAddr = Convert.ToUInt64(
                    field.FieldAddress.Replace("0x", "").Replace("0X", ""), 16);
                var writeAddr = $"0x{baseAddr + (ulong)field.BoolByteOffset:X}";

                // Read current byte, apply mask, write back
                var currentBytes = await _dump.ReadMemAsync(writeAddr, 1);
                var modified = FieldValueConverter.ApplyBoolMask(
                    currentBytes[0], field.BoolFieldMask, boolVal);

                await _dump.WriteMemAsync(writeAddr, new[] { modified });
            }
            else
            {
                // Standard scalar / enum conversion
                var (success, data, error) = FieldValueConverter.TryConvert(
                    field.TypeName, newValue, field.Size, field.EnumEntries);

                if (!success)
                {
                    StatusText = $"Invalid value for {field.Name}: {error}";
                    return;
                }

                await _dump.WriteMemAsync(field.FieldAddress, data);
            }

            // Refresh to show updated value, then restore selection to the edited row
            var editedName = field.Name;
            var editedOffset = field.Offset;
            await RefreshAsync();

            var restored = Fields?.FirstOrDefault(f => f.Name == editedName && f.Offset == editedOffset);
            if (restored != null)
                SelectedField = restored;

            StatusText = $"Written: {field.Name} = {newValue}";
            _log.Info($"EDIT {field.Name} ({field.TypeName}) @ {field.FieldAddress} = {newValue}");
        }
        catch (Exception ex)
        {
            StatusText = $"Write failed for {field.Name}: {ex.Message}";
            _log.Error($"Failed to write {field.Name} @ {field.FieldAddress}", ex);
        }
    }

    [RelayCommand]
    private async Task HexFieldAddressAsync(LiveFieldValue? field)
    {
        if (_aobMaker == null || field == null || string.IsNullOrEmpty(field.FieldAddress)) return;
        try
        {
            await _aobMaker.NavigateHexViewAsync(StripHexPrefix(field.FieldAddress));
        }
        catch (Exception ex)
        {
            _log.Error($"AOBMaker HEX field failed for {field.Name}", ex);
        }
    }

    [RelayCommand]
    private async Task HexPtrAddressAsync(LiveFieldValue? field)
    {
        if (_aobMaker == null || field == null || string.IsNullOrEmpty(field.PtrAddress)) return;
        try
        {
            await _aobMaker.NavigateHexViewAsync(StripHexPrefix(field.PtrAddress));
        }
        catch (Exception ex)
        {
            _log.Error($"AOBMaker HEX ptr failed for {field.Name}", ex);
        }
    }

    [RelayCommand]
    private async Task HexObjectAddressAsync()
    {
        if (_aobMaker == null || string.IsNullOrEmpty(CurrentAddress)) return;
        try
        {
            await _aobMaker.NavigateHexViewAsync(StripHexPrefix(CurrentAddress));
        }
        catch (Exception ex)
        {
            _log.Error("AOBMaker HEX object address failed", ex);
        }
    }

    // --- AOBMaker CE Plugin: one-click "Add to CE" memory record ---

    /// <summary>
    /// Add a single typed CE memory record at this field's own address (instance base +
    /// offset), labelled with the field name and typed to match the field. One-click
    /// alternative to copy-address-then-build-the-record-by-hand, so the user can jump
    /// straight to CE's "Find out what accesses this address". Batch adds go through
    /// the existing multi-select Copy CE Field (clipboard).
    /// </summary>
    [RelayCommand]
    private async Task AddFieldToCeAsync(LiveFieldValue? field)
    {
        if (_aobMaker == null || field == null || string.IsNullOrEmpty(field.FieldAddress)) return;
        var t = CeXmlExportService.MapFieldToCeRecordType(field);
        await AddRecordToCeAsync(field.Name, field.FieldAddress, t, "field");
    }

    /// <summary>
    /// Add a single CE memory record at this field's pointer target (the dereferenced
    /// object/struct base), typed as an 8-byte hex pointer. Only meaningful for navigable
    /// pointer fields (PtrAddress populated).
    /// </summary>
    [RelayCommand]
    private async Task AddPtrToCeAsync(LiveFieldValue? field)
    {
        if (_aobMaker == null || field == null || string.IsNullOrEmpty(field.PtrAddress)) return;
        await AddRecordToCeAsync(field.Name, field.PtrAddress, CeXmlExportService.PointerRecordType, "ptr");
    }

    /// <summary>
    /// Shared back-end for the per-row Add-to-CE buttons: push one typed memory record to
    /// CE via the AOBMaker plugin and reflect the outcome in the status line. Keeps the
    /// always-visible toolbar chip honest by syncing <see cref="IsAobMakerAvailable"/> to
    /// the bridge's post-call state.
    /// </summary>
    private async Task AddRecordToCeAsync(string name, string address,
        CeXmlExportService.CeRecordType t, string kind)
    {
        try
        {
            var ok = await _aobMaker!.CreateMemoryRecordAsync(
                Services.PackedLayoutNotice.RecordNamePrefix + name,
                StripHexPrefix(address), t.ValueType, t.IsSigned, t.ShowAsHex);
            IsAobMakerAvailable = _aobMaker.IsAvailable;
            StatusText = ok
                ? $"Added to CE: {name}"
                : (_aobMaker.IsAvailable
                    ? $"CE rejected record for {name}"
                    : "AOBMaker not connected — open CE with the plugin loaded");
        }
        catch (Exception ex)
        {
            _log.Error($"AOBMaker Add-to-CE ({kind}) failed for {name}", ex);
            StatusText = $"Add to CE failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CopyCurrentAddressAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress)) return;

        try
        {
            var formatted = AddressHelper.FormatAddress(
                CurrentAddress, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
            await _platform.CopyToClipboardAsync(formatted);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy current address", ex);
        }
    }

    [RelayCommand]
    private async Task CopyCurrentNameAsync()
    {
        if (string.IsNullOrEmpty(CurrentObjectName)) return;

        try
        {
            await _platform.CopyToClipboardAsync(CurrentObjectName);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy current name", ex);
        }
    }

    [RelayCommand]
    private async Task CopyCurrentClassNameAsync()
    {
        if (string.IsNullOrEmpty(CurrentClassName)) return;

        try
        {
            await _platform.CopyToClipboardAsync(CurrentClassName);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy current class name", ex);
        }
    }

    [RelayCommand]
    private async Task CopyOuterAddressAsync()
    {
        if (string.IsNullOrEmpty(CurrentOuterAddr) || CurrentOuterAddr == "0x0") return;

        try
        {
            var formatted = AddressHelper.FormatAddress(
                CurrentOuterAddr, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
            await _platform.CopyToClipboardAsync(formatted);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy outer address", ex);
        }
    }

    [RelayCommand]
    private async Task CopyOuterNameAsync()
    {
        if (string.IsNullOrEmpty(CurrentOuterName)) return;

        try
        {
            await _platform.CopyToClipboardAsync(CurrentOuterName);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy outer name", ex);
        }
    }

    [RelayCommand]
    private async Task CopyOuterClassNameAsync()
    {
        if (string.IsNullOrEmpty(CurrentOuterClassName)) return;

        try
        {
            await _platform.CopyToClipboardAsync(CurrentOuterClassName);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy outer class name", ex);
        }
    }

    /// <summary>Navigate CE's hex view to the Outer object's address (AOBMaker plugin).
    /// Mirrors <see cref="HexObjectAddressAsync"/> but targets the Outer (parent) base.</summary>
    [RelayCommand]
    private async Task HexOuterAddressAsync()
    {
        if (_aobMaker == null || string.IsNullOrEmpty(CurrentOuterAddr) || CurrentOuterAddr == "0x0") return;
        try
        {
            await _aobMaker.NavigateHexViewAsync(StripHexPrefix(CurrentOuterAddr));
        }
        catch (Exception ex)
        {
            _log.Error("AOBMaker HEX outer address failed", ex);
        }
    }

    // ========================================
    // Auto-refresh
    // ========================================

    /// <summary>
    /// Reacts to IsAutoRefreshing changes (driven by ToggleButton.IsChecked binding).
    /// Starts or stops the auto-refresh timer accordingly.
    /// </summary>
    partial void OnIsAutoRefreshingChanged(bool value)
    {
        if (value)
            StartAutoRefreshTimer();
        else
            StopAutoRefreshTimer();
    }

    partial void OnAutoRefreshIntervalSecChanged(int value)
    {
        OnPropertyChanged(nameof(AutoRefreshIntervalSecValue));
        // Enforce minimum interval (dynamic minimum from benchmark)
        if (value < AutoRefreshMinSec)
        {
            AutoRefreshIntervalSec = AutoRefreshMinSec;
            return;
        }

        // Update timer interval if already running
        if (_autoRefreshTimer != null && _autoRefreshTimer.IsEnabled)
        {
            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(value);
            _countdownRemaining = value; // Reset countdown to new interval
        }
    }

    private void StartAutoRefreshTimer()
    {
        // Stop existing timer, but don't reset IsAutoRefreshing
        if (_autoRefreshTimer != null)
        {
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Tick -= OnAutoRefreshTick;
            _autoRefreshTimer = null;
        }

        // Reset benchmark state — first tick will measure refresh duration
        _isAutoRefreshBenchmarked = false;

        var interval = Math.Max(AutoRefreshIntervalSec, AutoRefreshMinSec);
        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(interval)
        };
        _autoRefreshTimer.Tick += OnAutoRefreshTick;
        _autoRefreshTimer.Start();

        // Start 1-second countdown timer for status display
        StopCountdownTimer();
        _autoRefreshResumePending = false;   // we ARE running now; nothing left to resume
        _countdownRemaining = interval;
        AutoRefreshStatusText = AutoRefreshCadence.LabelFor(_countdownRemaining);
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();
    }

    private void StopCountdownTimer()
    {
        if (_countdownTimer != null)
        {
            _countdownTimer.Stop();
            _countdownTimer.Tick -= OnCountdownTick;
            _countdownTimer = null;
        }
    }

    /// <summary>
    /// One second of the status countdown. Delegates the rule to
    /// <see cref="AutoRefreshCadence.Step"/>, which re-arms at zero — the shipped
    /// version decremented and clamped here while the ONLY reset lived past the
    /// refresh tick's early-return guard, so a permanently-skipped tick pinned the
    /// label at "0s" forever with the Auto toggle still reading ON
    /// (<c>[AUTOREFRESH-2026-08-19]</c>).
    /// </summary>
    private void OnCountdownTick(object? sender, EventArgs e)
    {
        var step = AutoRefreshCadence.Step(
            _countdownRemaining,
            AutoRefreshCadence.NormalizeInterval(AutoRefreshIntervalSec, AutoRefreshMinSec),
            CurrentSkipReason());

        _countdownRemaining = step.Remaining;
        AutoRefreshStatusText = step.Label;
    }

    /// <summary>Why the next auto-refresh tick would (or would not) do any work.</summary>
    private AutoRefreshSkip CurrentSkipReason()
        => AutoRefreshCadence.Classify(_isAutoRefreshing_InProgress, _isEditing, HasData, CurrentAddress);

    public void StopAutoRefreshTimer() => StopAutoRefreshTimer(resumable: false);

    /// <param name="resumable">
    /// True when something OUTSIDE the user's control stopped auto-refresh — the pipe
    /// dropping, or switching away from the Live Walker tab. The panel then re-arms
    /// itself (<see cref="ResumeAutoRefreshIfPending"/>) once it is rooted on data
    /// again, so a reconnect or a trip to another tab does not silently leave Auto off.
    /// False for a user untick and for every navigation re-root: those must stay off
    /// until the user ticks Auto again.
    /// </param>
    public void StopAutoRefreshTimer(bool resumable)
    {
        // Capture before the teardown clears both witnesses.
        bool wasRunning = _autoRefreshTimer != null || IsAutoRefreshing;

        if (_autoRefreshTimer != null)
        {
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Tick -= OnAutoRefreshTick;
            _autoRefreshTimer = null;
        }

        StopCountdownTimer();
        IsAutoRefreshing = false;

        // Reset dynamic minimum and benchmark state on stop (tab switch, navigation, etc.)
        AutoRefreshMinSec = Constants.MinAutoRefreshIntervalSec;
        _isAutoRefreshBenchmarked = false;
        AutoRefreshStatusText = AutoRefreshCadence.LabelIdle;

        // MUST be last: the `IsAutoRefreshing = false` above re-enters this method
        // through OnIsAutoRefreshingChanged with resumable:false, which would clear
        // the flag again if it were set any earlier.
        //
        // Only a stop of a RUNNING timer may write the flag. A stop that had nothing to
        // stop leaves it alone — otherwise the very path the maintainer walked would
        // eat it: disconnect arms the resume, then the first navigation after the
        // reconnect (StartFromWorld / NavigateToAddress / Locate / bookmark) calls the
        // plain non-resumable overload and would clear it a moment before UpdateDisplay
        // got the chance to act on it.
        if (wasRunning)
            _autoRefreshResumePending = resumable;
    }

    /// <summary>
    /// Re-arm auto-refresh after the reason it was suspended has gone away — a
    /// reconnect that re-roots the panel, or coming back to the Live Walker tab.
    /// No-op unless something out of the user's control stopped it AND there is now
    /// an object to refresh; see <see cref="AutoRefreshCadence.ShouldResume"/>.
    /// </summary>
    public void ResumeAutoRefreshIfPending()
    {
        if (!AutoRefreshCadence.ShouldResume(_autoRefreshResumePending, IsAutoRefreshing,
                                             HasData, CurrentAddress))
            return;

        _autoRefreshResumePending = false;
        IsAutoRefreshing = true;   // -> OnIsAutoRefreshingChanged -> StartAutoRefreshTimer
    }

    /// <summary>Drop the live walk state so a reconnect never shows an object (and
    /// its live addresses) from the previous game, and STOP the auto-refresh timer so
    /// it can't re-walk a dead pipe (audit X5). Deliberately preserves the persisted
    /// per-game <see cref="BookmarkSlots"/> / <c>_activePeHash</c> / SearchHistory —
    /// those are reloaded on reconnect and must survive a disconnect.</summary>
    public void ClearOnDisconnect()
    {
        // resumable: the PIPE stopped auto-refresh, not the user. X5 correctly stops the
        // timer (it would otherwise re-walk a dead pipe, and after a reconnect to a
        // DIFFERENT game it would walk the previous game's addresses) — but stopping it
        // with no way back left Auto silently off for the rest of the session. The panel
        // re-arms itself from OnHasDataChanged / OnCurrentAddressChanged once it is
        // rooted on data again — NOT from UpdateDisplay, which "Start from GWorld" and
        // the container views never reach.
        StopAutoRefreshTimer(resumable: true);   // also sets IsAutoRefreshing = false
        _exportCts?.Cancel();

        Breadcrumbs.Clear();
        Fields.Clear();
        Functions.Clear();
        References.Clear();
        ClearForwardStack();
        _replacedSpine = null;
        _cachedWorld = null;
        _selectedFieldsSnapshot.Clear();
        SelectedFieldsCount = 0;

        SelectedField = null;
        CurrentAddress = "";
        CurrentObjectName = "";
        CurrentClassName = "";
        HasData = false;
        LocateFailureMessage = "";
    }

    /// <summary>
    /// Audit fix #17: stop both DispatcherTimers when the VM is destroyed.
    /// Without this, a still-registered Tick handler keeps the VM rooted by
    /// the Avalonia dispatcher, so the timer fires post-disposal — at best
    /// wasting work, at worst crashing on stale state.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // StopAutoRefreshTimer already handles both _autoRefreshTimer and
        // _countdownTimer (via StopCountdownTimer) — single call covers it.
        StopAutoRefreshTimer();

        if (_hookedFields != null)
        {
            _hookedFields.CollectionChanged -= OnFieldsRebuilt;
            _hookedFields = null;
        }

        _searchHistoryDebounce?.Dispose();
        _searchHistoryDebounce = null;

        _functionFilterMemory.Dispose();

        // Abort any in-flight export walk so a pending drilldown unwinds on teardown.
        _exportCts?.Cancel();
        _exportCts?.Dispose();
        _exportCts = null;

        GC.SuppressFinalize(this);
    }

    private async void OnAutoRefreshTick(object? sender, EventArgs e)
    {
        // Anti-flooding: skip if a refresh is already in progress or no data to refresh.
        // Uses a dedicated flag (_isAutoRefreshing_InProgress) to prevent re-entrant calls
        // from the DispatcherTimer firing while a previous refresh is still awaiting.
        if (CurrentSkipReason() != AutoRefreshSkip.None)
        {
            // A SKIPPED tick still has to re-arm the countdown. The shipped version
            // returned here with the reset stranded further down, so a tick that kept
            // skipping froze the label at "0s" forever and reported nothing at all
            // ([AUTOREFRESH-2026-08-19]). OnCountdownTick now shows the reason.
            _countdownRemaining = AutoRefreshCadence.NormalizeInterval(AutoRefreshIntervalSec, AutoRefreshMinSec);
            return;
        }

        _isAutoRefreshing_InProgress = true;
        try
        {
            var sw = Stopwatch.StartNew();
            await RefreshAsync();
            sw.Stop();

            var durationSec = (int)Math.Ceiling(sw.Elapsed.TotalSeconds);

            // Benchmark: on first successful auto-refresh, check if the interval is too short.
            // If refresh took longer than the user's interval, auto-clamp the minimum.
            if (!_isAutoRefreshBenchmarked)
            {
                _isAutoRefreshBenchmarked = true;

                if (durationSec >= AutoRefreshIntervalSec)
                {
                    var newMin = durationSec + Constants.AutoRefreshBenchmarkBufferSec;
                    AutoRefreshMinSec = newMin;
                    AutoRefreshIntervalSec = newMin;

                    // Restart timer with the new interval
                    if (_autoRefreshTimer != null)
                    {
                        _autoRefreshTimer.Interval = TimeSpan.FromSeconds(newMin);
                    }

                    _log.Info($"Auto-refresh: benchmark {durationSec}s, clamped interval to {newMin}s");
                }
            }
        }
        catch
        {
            // Silently ignore auto-refresh errors to avoid flooding the UI with error dialogs
        }
        finally
        {
            // Re-arm in the FINALLY, not on the success path: a throwing RefreshAsync
            // would otherwise strand the countdown at 0 exactly like a skipped tick did.
            _countdownRemaining = AutoRefreshCadence.NormalizeInterval(AutoRefreshIntervalSec, AutoRefreshMinSec);
            _isAutoRefreshing_InProgress = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplySearch(value);

        // Schedule a "remember this keyword" pass once typing settles. Only the
        // keyword the user pauses on is kept (longest-valid), so intermediate
        // prefixes typed on the way to it are never remembered. Clearing or a
        // sub-minimum keyword cancels any pending pass and schedules nothing.
        if (_disposed) return;
        _searchHistoryDebounce?.Dispose();
        _searchHistoryDebounce = null;
        if ((value?.Trim().Length ?? 0) < SearchKeywordHistory.MinLength) return;
        _searchHistoryDebounce = new Timer(
            _ => Dispatcher.UIThread.Post(FinalizeSearchKeyword),
            null, 700, Timeout.Infinite);
    }

    /// <summary>
    /// Commit the current search text to the keyword history if it's a valid,
    /// settled keyword (>= MinLength chars AND produced at least one match).
    /// Runs on the UI thread after the typing-debounce fires.
    /// </summary>
    private void FinalizeSearchKeyword()
    {
        if (_disposed) return;
        var k = SearchText?.Trim() ?? "";
        if (k.Length < SearchKeywordHistory.MinLength || SearchMatchCount <= 0) return;
        SearchKeywordHistory.Remember(SearchHistory, k);
    }

    /// <summary>
    /// Commit any pending (debounced) keyword immediately. Call this BEFORE
    /// clearing <see cref="SearchText"/> on a tab switch so a quick
    /// type-then-switch (within the debounce window) still remembers the keyword
    /// — a tab switch is itself a strong "settled" signal.
    /// </summary>
    public void FlushPendingSearchKeyword()
    {
        _searchHistoryDebounce?.Dispose();
        _searchHistoryDebounce = null;
        FinalizeSearchKeyword();
    }

    /// <summary>
    /// Clear the field-search keyword because the grid is navigating to DIFFERENT
    /// data (drill-down into a container, Back, Parent, breadcrumb, bookmark, the
    /// GWorld actor-list root, or a fresh walk) — a leftover filter no longer
    /// applies. The remembered-keyword history is flushed first so the keyword we
    /// were filtering by is kept, then the live text is cleared. No-op when the
    /// box is already empty. Deliberately NOT called from Refresh / auto-refresh
    /// (same object, refreshed values) so an active filter survives a refresh.
    /// Some navigation paths rebuild Fields directly and bypass UpdateDisplay
    /// (container drill-down, world root, synthetic-container re-hydration), so
    /// this is called at the navigation command entry points too, not only here.
    /// </summary>
    private void ClearFieldSearchForNavigation()
    {
        if (string.IsNullOrEmpty(SearchText)) return;
        FlushPendingSearchKeyword();
        SearchText = "";
    }

    /// <summary>
    /// Stamp <see cref="LiveFieldValue.IsSearchMatch"/> across <paramref name="target"/>
    /// for <paramref name="query"/> and return the match count.
    /// <para>
    /// The matcher ONLY — none of the two side effects <see cref="ApplySearch"/> adds on
    /// top (the scroll-to-first-match, and the whole-collection swap that forces the
    /// DataGrid to re-realize rows). <see cref="UpdateDisplay"/> needs the matcher without
    /// either: it is installing brand-new row objects anyway, so the rows re-realize on
    /// their own, and the swap would reset the scroll position its in-place replacement
    /// exists to preserve.
    /// </para>
    /// <para>
    /// <c>internal</c> for the tests (InternalsVisibleTo): the V6 defect was that this
    /// step did not run on a refresh, which is a fact about a pure function over a field
    /// list and needs no live pipe to pin.
    /// </para>
    /// </summary>
    internal static int MarkSearchMatches(IEnumerable<LiveFieldValue> target, string query)
    {
        // Space-separated terms are ANDed: each term must hit at least one of the
        // scanned fields (term-level AND, field-level OR) — the shared Object Tree
        // filter semantics. This stays a HIGHLIGHTER: matching rows are re-coloured
        // (IsSearchMatch), no rows are removed.
        var terms = ObjectTreeFilter.SplitTerms(query);

        // Require at least 2 characters — single char matches too broadly
        bool active = !string.IsNullOrWhiteSpace(query)
                      && query.Trim().Length >= 2
                      && terms.Length > 0;

        int count = 0;
        foreach (var f in target)
        {
            bool match = active
                && ObjectTreeFilter.MatchesAllTerms(
                    terms, f.Name, f.TypeName, f.DisplayValue, f.PtrClassName, f.StructTypeName);

            f.IsSearchMatch = match;
            if (match) count++;
        }
        return count;
    }

    private void ApplySearch(string query)
    {
        int count = MarkSearchMatches(Fields, query);
        SearchMatchCount = count;
        HasSearchResults = count > 0;

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            _lastScrolledSearchText = "";
        }
        else
        {
            // Scroll to first match when search text changes and has results
            if (count > 0 && query != _lastScrolledSearchText)
            {
                _lastScrolledSearchText = query;
                ScrollToFirstSearchMatch?.Invoke();
            }
        }

        // Force DataGrid to re-evaluate row styles by resetting the collection
        var items = new ObservableCollection<LiveFieldValue>(Fields);
        Fields = items;
    }

    /// <summary>Move the selection to the next highlighted search match
    /// (down arrow). Wraps from the last match back to the first.</summary>
    [RelayCommand]
    private void NextSearchMatch() => NavigateSearchMatch(+1);

    /// <summary>Move the selection to the previous highlighted search match
    /// (up arrow). Wraps from the first match back to the last.</summary>
    [RelayCommand]
    private void PrevSearchMatch() => NavigateSearchMatch(-1);

    /// <summary>Step the selection through the highlighted search matches.
    /// Search only re-colours matching rows; this lets the user actually jump
    /// between them (setting SelectedField anchors the grid selection). When
    /// the current selection isn't a match, forward starts at the first match
    /// and backward at the last. Stepping wraps around both ends.</summary>
    private void NavigateSearchMatch(int direction)
    {
        if (!HasSearchResults) return;
        var matches = Fields.Where(f => f.IsSearchMatch).ToList();
        if (matches.Count == 0) return;

        int cur = SelectedField != null ? matches.IndexOf(SelectedField) : -1;
        int next = cur < 0
            ? (direction > 0 ? 0 : matches.Count - 1)
            : (cur + direction + matches.Count) % matches.Count;

        var target = matches[next];
        SelectedField = target;
        ScrollFieldIntoView?.Invoke(target);
    }

    /// <summary>
    /// The crumb a drill-down gesture was launched FROM, captured at gesture time so a
    /// post-await append can tell whether the spine it is about to grow is still the one
    /// the user was looking at. Audit V3/V4.
    ///
    /// ⚠ Identity, not <c>Breadcrumbs.Count</c>. Back-then-Forward, and Back-then-a-different-drill,
    /// both restore the count while changing the parent — a count check passes and the
    /// corruption lands anyway. Crumbs are re-used BY IDENTITY across Back/Forward
    /// (LiveWalkerForwardNavTests asserts <c>Assert.Same(leaf, vm.Breadcrumbs[1])</c>), so
    /// reference equality is exactly the right test.
    ///
    /// ⚠ <c>null</c> means "no expectation", and it is a legitimate answer, not a missing one.
    /// Two of the three <see cref="NavigateToAsync"/> callers re-root the walker with
    /// <c>Breadcrumbs.Clear()</c> first (Game Engine start, and the Go box / bookmark /
    /// cross-tab "Open in Live Walker" handoff) — they have no parent to be stale against,
    /// and making this parameter demand one would silently kill every one of those paths.
    /// The parameter is required-but-nullable so a new caller has to state which it is.
    /// </summary>
    private BreadcrumbItem? CurrentCrumb => Breadcrumbs.Count > 0 ? Breadcrumbs[^1] : null;

    private bool IsStillOnParent(BreadcrumbItem? expectedParent)
        => expectedParent is null || ReferenceEquals(CurrentCrumb, expectedParent);

    private async Task NavigateToAsync(string addr, string label, int fieldOffset, string fieldName,
                                       bool isPointer, BreadcrumbItem? expectedParent)
    {
        var result = await _dump.WalkInstanceAsync(addr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit, fillGaps: FillGaps);
        result = await AutoFillGapsRetryAsync(result, addr);

        // The walk above can take seconds. If the user went Back / Forward / jumped a
        // breadcrumb meanwhile, appending here grafts this object onto a DIFFERENT
        // parent, and the resulting crumb's FieldOffset belongs to a spine that no
        // longer exists — which then ships into CE XML, CSX and a persisted bookmark.
        if (!IsStillOnParent(expectedParent))
        {
            StatusText = $"Navigation superseded — '{fieldName}' was discarded (you moved while it loaded).";
            _log.Info($"NAV✕ {fieldName} addr={addr} discarded: parent changed during the walk");
            return;
        }

        var displayName = !string.IsNullOrEmpty(result.Name) ? result.Name : label;
        Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = addr,
            Label = displayName,
            FieldOffset = fieldOffset,
            FieldName = fieldName,
            TargetClassName = result.ClassName,
            IsPointerDeref = isPointer,
        });

        _log.Info($"NAV→ {fieldName} addr={addr} off={FormatCrumbOffset(fieldOffset)} ptr={isPointer} | BC={FormatBreadcrumbTrace()}");
        UpdateDisplay(result);
    }

    /// <summary>
    /// Auto-enable Guess? (fill_gaps) when a walk returns 0 fields but PropertiesSize
    /// indicates data exists. This gives users raw byte analysis instead of an
    /// empty panel for structs/objects with no reflected UPROPERTY fields — e.g. a
    /// USTRUCT whose members lack UPROPERTY, so its ChildProperties chain is empty
    /// (UE still reflects the type + size, but not the individual fields).
    /// Only triggers when the FillGaps toggle is off (avoids double-fill_gaps).
    ///
    /// Rather than silently filling behind an UNCHECKED toggle (confusing — the user
    /// sees guessed rows but the checkbox says off), this CHECKS the Guess? toggle
    /// (via SetFillGapsSilently, so it doesn't fire a second re-walk) and re-walks
    /// with gap-filling. The checkbox now reflects reality; the user can uncheck it.
    /// Once on, subsequent walks fill gaps directly so this path stops triggering.
    ///
    /// Threshold is PropertiesSize &gt; 0 (any non-empty layout). A real UObject's
    /// PropertiesSize always includes its header (~0x28+), and the DLL's gap-fill
    /// clamps guesses to [header, PropertiesSize), so a header-only UObject still
    /// yields no guessed rows — nothing is fabricated. The previous &gt; 0x30 floor
    /// silently excluded small raw structs (e.g. a 36-byte / 0x24 struct), leaving
    /// the grid AND Copy CE XML / CE Field empty for them.
    /// </summary>
    private async Task<InstanceWalkResult> AutoFillGapsRetryAsync(
        InstanceWalkResult result, string addr, string? classAddr = null)
    {
        // Never auto-retry a stale/recycled object: the DLL already judged its
        // class pointer garbage. Firing fill_gaps on it asks the walker to guess
        // types across a bogus multi-hundred-MB PropertiesSize, which wedges the
        // single-threaded pipe (the "switch back to Live Walker froze the pipe"
        // bug). The DLL caps this too, but bail here so we don't even send it.
        if (result.IsStale)
        {
            _log.Warn($"Skipping fill_gaps auto-retry for {addr}: object is stale (recycled class pointer)");
            return result;
        }

        // PropertiesSize sanity bound mirrors the DLL's kMaxSanePropertiesSize
        // (1 MB). A larger value is garbage, not a genuine 0-field class.
        const int MaxSanePropertiesSize = 1 * 1024 * 1024;
        if (result.Fields.Count == 0 && result.PropertiesSize > 0
            && result.PropertiesSize <= MaxSanePropertiesSize && !FillGaps)
        {
            _log.Info($"Auto fill_gaps: 0 fields but propsSize={result.PropertiesSize}, auto-checking Guess? + retrying with fill_gaps for {addr}");
            SetFillGapsSilently(true);
            result = await _dump.WalkInstanceAsync(addr, classAddr,
                arrayLimit: ArrayLimit, previewLimit: PreviewLimit, fillGaps: true);
        }
        return result;
    }

    /// <summary>Format breadcrumb trail for debug logging.</summary>
    private string FormatBreadcrumbTrace()
    {
        if (Breadcrumbs.Count == 0) return "(empty)";
        var parts = new List<string>(Breadcrumbs.Count);
        foreach (var bc in Breadcrumbs)
        {
            var flags = bc.IsContainerView ? "C" : bc.IsPointerDeref ? "P" : "S";
            parts.Add($"{bc.FieldName ?? bc.Label}({flags},{FormatCrumbOffset(bc.FieldOffset)},{bc.Address?[^4..]})");
        }
        return string.Join(" > ", parts);
    }

    [RelayCommand]
    private async Task GenerateInvokeScriptAsync(FunctionInfoModel? func)
    {
        if (func == null || string.IsNullOrEmpty(CurrentClassName)) return;

        try
        {
            ClearStatus();
            var script = InvokeScriptGenerator.Generate(CurrentClassName, func.Name, func);
            var description = $"Invoke: {CurrentClassName}::{func.Name}";

            // Sample availability before send so we can distinguish 'pipe
            // broke mid-send' (was available, now isn't) from 'never
            // configured / CE not running' (was already false). Note:
            // this command is also IsEnabled-bound to IsAobMakerAvailable
            // in the AXAML, so wasAvailable=false here would only happen
            // if the user clicked between availability flips -- the
            // clipboard fallback below still produces a usable script.
            bool wasAvailable = _aobMaker?.IsAvailable ?? false;
            if (_aobMaker != null && wasAvailable)
            {
                var sent = await _aobMaker.CreateAAScriptAsync(description, script, autoActivate: false);
                if (sent)
                {
                    _log.Info($"Invoke script sent to CE: {description}");
                    StatusText = $"Invoke script created in CE: {func.Name}";
                    if (_aobMaker != null) IsAobMakerAvailable = _aobMaker.IsAvailable;
                    return;
                }
            }

            // Fallback: copy as paste-able CE memory-record XML (a bare AA body
            // can't be pasted into a CE record — wrap it, same as the Global-Pointer
            // records). If we thought CE was present (button shouldn't have been
            // clickable then), surface a pipe-broken warning too.
            await _platform.CopyToClipboardAsync(
                Services.CheatTableBuilder.WrapAaScriptXml(description, script));
            if (_aobMaker != null) IsAobMakerAvailable = _aobMaker.IsAvailable;
            StatusText = wasAvailable
                ? $"⚠ AOBMaker pipe broke (CE closed?) — invoke script copied as CE XML (paste into CE's address list)"
                : $"Invoke script copied as CE XML — paste into CE's address list ({func.Name})";
            _log.Info($"Invoke script copied as CE XML: {description} (wasAvailable={wasAvailable})");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to generate invoke script for {func.Name}", ex);
        }
    }

    [RelayCommand]
    private async Task InvokeViaPipeAsync(FunctionInfoModel? func)
    {
        if (func == null || string.IsNullOrEmpty(CurrentAddress)) return;

        try
        {
            ClearStatus();

            if (Avalonia.Application.Current?.ApplicationLifetime is not
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                || desktop.MainWindow is not { } owner)
                return;

            var inputParams = func.Params.Where(p => !p.IsReturn).ToList();

            // Dialog owns the entire invoke lifecycle:
            // - Shows input fields (or "no params" message)
            // - FIRE button calls InvokeFunctionAsync internally
            // - Copy AA Script button bakes current values via BakedScriptGenerator
            //   and pushes to AOBMaker / clipboard
            // - Decoded results shown inline (return values, out params)
            // - Returns "ok" on Close, null on Cancel
            var dialog = new Views.InvokeParamDialog(
                CurrentClassName, func.Name, inputParams, func.Params, func.ParmsSize,
                CurrentAddress, _dump, _engineState?.UEVersion ?? 0,
                aobMaker: _aobMaker, platform: _platform,
                mode: Views.InvokeDialogMode.PipeInvoke);

            var dialogResult = await dialog.ShowDialog<string?>(owner);

            StatusText = dialogResult == "ok"
                ? $"Invoke dialog closed: {CurrentClassName}::{func.Name}"
                : $"Invoke cancelled: {func.Name}";

            _log.Info($"Pipe invoke dialog {(dialogResult == "ok" ? "completed" : "cancelled")}: " +
                      $"{CurrentClassName}::{func.Name} inst={CurrentAddress}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to invoke {func?.Name} via pipe", ex);
        }
    }

    /// <summary>
    /// Third UFunction-row button: opens the InvokeParamDialog in
    /// CopyBakedScript mode (FIRE hidden) so the user can fill the form
    /// and ship a non-interactive AA Script for inclusion in their .CT.
    /// For zero-param functions the dialog is skipped -- the script is
    /// generated immediately from an empty BakedParamValue list.
    /// </summary>
    [RelayCommand]
    private async Task CopyBakedScriptAsync(FunctionInfoModel? func)
    {
        if (func == null || string.IsNullOrEmpty(CurrentClassName)) return;

        try
        {
            ClearStatus();

            var inputParams = func.Params.Where(p => !p.IsReturn).ToList();
            var hasReturn = func.Params.Any(p => p.IsReturn);

            // Fast-path: TRULY trivial functions only (no inputs AND no return).
            // For functions that return a value but take no inputs (e.g.
            // KismetSystemLibrary::GetGameName, KismetMathLibrary::GetPI),
            // we MUST show the dialog so the Verify Return Value toggle is
            // reachable -- otherwise the user has no way to print/inspect
            // what the function actually returned.
            if (inputParams.Count == 0 && !hasReturn)
            {
                var script = Services.BakedScriptGenerator.Generate(
                    CurrentClassName, func.Name, func.ParmsSize,
                    Array.Empty<Models.BakedParamValue>());
                var description = $"Invoke (baked, no args): {CurrentClassName}::{func.Name}";
                // Sample availability BEFORE the send so we can distinguish
                // 'pipe broke mid-send' (was available, now isn't) from
                // 'not configured' (was already false).
                bool wasAvailable = _aobMaker?.IsAvailable ?? false;
                bool sentToCe = false;
                if (_aobMaker != null && wasAvailable)
                    sentToCe = await _aobMaker.CreateAAScriptAsync(description, script, autoActivate: false);
                if (!sentToCe)
                    await _platform.CopyToClipboardAsync(
                        Services.CheatTableBuilder.WrapAaScriptXml(description, script));
                // Sync the VM-level flag from whatever the bridge ended up at,
                // so the Notes column reflects post-send reality on the next
                // repaint.
                if (_aobMaker != null) IsAobMakerAvailable = _aobMaker.IsAvailable;

                StatusText = sentToCe
                    ? $"AA Script created in CE: {func.Name}"
                    : wasAvailable
                        ? $"⚠ AOBMaker pipe broke (CE closed?) — script copied as CE XML (paste into CE's address list)"
                        : $"AOBMaker not connected — script copied as CE XML, paste into CE's address list ({func.Name})";
                _log.Info($"Baked AA Script (no args) {(sentToCe ? "sent to CE" : "to clipboard")}: " +
                          $"{CurrentClassName}::{func.Name} (wasAvailable={wasAvailable})");
                return;
            }

            if (Avalonia.Application.Current?.ApplicationLifetime is not
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                || desktop.MainWindow is not { } owner)
                return;

            var dialog = new Views.InvokeParamDialog(
                CurrentClassName, func.Name, inputParams, func.Params, func.ParmsSize,
                CurrentAddress, _dump, _engineState?.UEVersion ?? 0,
                aobMaker: _aobMaker, platform: _platform,
                mode: Views.InvokeDialogMode.CopyBakedScript);

            var dialogResult = await dialog.ShowDialog<string?>(owner);

            StatusText = dialogResult == "ok"
                ? $"AA Script ready: {CurrentClassName}::{func.Name}"
                : $"AA Script export cancelled: {func.Name}";

            _log.Info($"CopyBakedScript dialog {(dialogResult == "ok" ? "completed" : "cancelled")}: " +
                      $"{CurrentClassName}::{func.Name}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to generate baked script for {func?.Name}", ex);
        }
    }

    /// <summary>
    /// Empty the displayed node — fields, breadcrumb spine, header + parent info —
    /// so a FAILED Locate-in-GWorld doesn't leave the previous object on screen
    /// looking like the result. The caller sets StatusText with the reason after.
    /// (Inverse of <see cref="UpdateDisplay"/>.)
    /// </summary>
    private void ClearDisplayedNode()
    {
        Fields.Clear();   // fires OnFieldsRebuilt -> clears any stranded IsEditing latch
        Breadcrumbs.Clear();
        SelectedField = null;
        HasData = false;
        ShowCeXml = false;
        CurrentObjectName = "";
        CurrentClassName = "";
        CurrentAddress = "";
        CurrentOuterAddr = "";
        CurrentOuterName = "";
        CurrentOuterClassName = "";
        HasParent = false;
    }

    /// <param name="clearFieldSearch">When true (every navigation — drill-down,
    /// Back, Parent, Go, breadcrumb, bookmark, start), clear the field-search
    /// keyword: the grid is now showing DIFFERENT data so a leftover filter no
    /// longer applies. Refresh / auto-refresh pass false (same object, refreshed
    /// values) so the active filter survives a refresh. The remembered-keyword
    /// history (LRU) is flushed first, then the live text is cleared.</param>
    private void UpdateDisplay(InstanceWalkResult result, bool clearFieldSearch = true)
    {
        if (clearFieldSearch)
            ClearFieldSearchForNavigation();

        CurrentObjectName = result.Name;
        CurrentClassName = result.ClassName;
        CurrentAddress = result.Address;
        HasData = true;   // OnHasDataChanged retires any failure banner once data shows
        ShowCeXml = false;

        // Update parent (Outer) info
        CurrentOuterAddr = result.OuterAddr;
        CurrentOuterName = result.OuterName;
        CurrentOuterClassName = result.OuterClassName;
        HasParent = !string.IsNullOrEmpty(result.OuterAddr) && result.OuterAddr != "0x0";

        // Inline structs are not UObjects — they don't have OuterPrivate or FName at
        // the UObject::Name offset. The DLL reads garbage when walking a struct address
        // as if it were a UObject, producing corrupted name strings.
        // Override CurrentObjectName with the breadcrumb label (set from field metadata
        // during navigation) and disable the Parent button / clear Outer info.
        if (Breadcrumbs.Count > 0 && !Breadcrumbs[^1].IsPointerDeref
            && !string.IsNullOrEmpty(Breadcrumbs[^1].ClassAddr))
        {
            CurrentObjectName = Breadcrumbs[^1].Label;
            HasParent = false;
            CurrentOuterAddr = "";
            CurrentOuterName = "";
            CurrentOuterClassName = "";
        }

        // Track whether this is a definition view (schema-only, no live values)
        _isDefinitionView = result.IsDefinition;

        // Stale object: the DLL judged the class pointer recycled/garbage (the
        // instance was freed and its memory slot reused — common after returning
        // to Live Walker from a long Snapshot / Class-Pivot pass on the same
        // address). The field grid is empty; surface why instead of showing a
        // silently-blank grid, and never auto-retry fill_gaps on it.
        if (result.IsStale)
        {
            StatusText = "⚠ This object appears to have been freed/recycled — re-open it from \U0001F30D GWorld or the finder.";
            _log.Warn($"UpdateDisplay: stale/recycled object at {result.Address} (implausible PropertiesSize) — fields unavailable");
        }

        // Compute absolute field addresses
        ulong baseAddr = 0;
        try
        {
            if (!string.IsNullOrEmpty(result.Address))
                baseAddr = Convert.ToUInt64(result.Address.Replace("0x", "").Replace("0X", ""), 16);
        }
        catch { /* ignore parse failures */ }

        // Update fields. When refreshing the same object (same field count and layout),
        // replace items in-place to preserve DataGrid scroll position.
        // When navigating to a different object, do a full clear+rebuild.
        var newFields = result.Fields;
        foreach (var f in newFields)
        {
            if (baseAddr != 0)
                f.FieldAddress = $"0x{baseAddr + (ulong)f.Offset:X}";
        }

        // Re-apply the field-search highlight to the NEW row objects.
        //
        // Refresh / auto-refresh pass clearFieldSearch:false precisely so the keyword
        // survives — but these are fresh LiveFieldValue instances whose IsSearchMatch
        // defaults to false, and nothing re-ran the matcher. Highlights vanished on
        // every refresh while SearchMatchCount and the ↑/↓ match-stepper kept
        // advertising the previous walk's N matches. (audit #5 U1/V6)
        //
        // Marked BEFORE the rows are installed, and via MarkSearchMatches rather than
        // ApplySearch: the row background is painted from LoadingRow when a row is
        // realized, so replacing the row object is enough to repaint it, whereas
        // ApplySearch's trailing whole-collection swap would reset the scroll position
        // the in-place replacement below exists to preserve — and with a filter active,
        // auto-refresh would then jerk the grid to the top on every tick.
        //
        // When clearFieldSearch is true, ClearFieldSearchForNavigation above has already
        // emptied SearchText, so this correctly clears the flags and zeroes the counts.
        int searchMatches = MarkSearchMatches(newFields, SearchText);
        SearchMatchCount = searchMatches;
        HasSearchResults = searchMatches > 0;

        if (Fields.Count == newFields.Count && Fields.Count > 0
            && Fields[0].Name == newFields[0].Name)
        {
            // Same layout — copy the fresh values ONTO the existing rows.
            //
            // ⚠ This used to be `Fields[i] = newFields[i]` under a comment claiming it
            // "preserves scroll position". Measured on DumperTest, it does the opposite: the
            // DataGridCollectionView splits each indexer assignment into Remove+Add and the grid
            // ends up EXACTLY ONE ROW higher per Refresh, cumulatively — the reported "the match
            // sits one row below the viewport". [LWREFRESH-2026-08-21]
            //
            // Two alternatives were measured and rejected before this one: a single Clear+Add
            // (one Reset) jumps the grid to the TOP, which is worse; and restoring a captured
            // anchor afterwards cannot work at all, because ScrollIntoView means "make visible",
            // not "put at top", and is a no-op for a drift smaller than the viewport.
            //
            // The rows keep their identity, so the collection raises nothing and the grid does not
            // move. LiveFieldValue is observable for exactly the members that can differ between
            // two walks of the same object, which is what repaints the cells.
            for (int i = 0; i < newFields.Count; i++)
                Fields[i].CopyLiveValuesFrom(newFields[i]);

            // The marker above ran over newFields, which are now discarded, so re-run it over the
            // rows the grid is actually showing. Not optional and not merely tidy: clearing the
            // keyword marks zero matches on newFields while the surviving rows would keep their
            // old flags, leaving a highlight on rows that no longer match anything.
            MarkSearchMatches(Fields, SearchText);

            // The editing latch used to be cleared by the Replace notification this loop no longer
            // raises. Clearing it here keeps the previous guarantee exactly — a stranded `true`
            // vetoes every auto-refresh tick for the rest of the session, silently, and that is a
            // far worse failure than ending an edit a beat early.
            //
            // ⚠ It may now be over-cautious rather than necessary: the latch was stranded because
            // Avalonia tears an open editor down when the ROW OBJECT is replaced without raising
            // CellEditEnded, and the row object is no longer replaced. Whether the editor survives
            // a value copy is unverified, so this keeps the old, safe behaviour rather than betting
            // on the new one.
            IsEditing = false;
        }
        else
        {
            // Different layout — full rebuild
            Fields.Clear();
            foreach (var f in newFields)
                Fields.Add(f);
        }

        // Apply pending scroll-to-field hint (e.g. set by OpenReferenceOwner).
        // Setting SelectedField alone does NOT scroll the DataGrid — Avalonia's
        // DataGrid only auto-scrolls on user-driven selection. Raise the
        // ScrollToFieldRequested event so the View calls ScrollIntoView, the
        // same path used by edit-commit and inline drill navigation.
        if (!string.IsNullOrEmpty(_pendingScrollFieldName))
        {
            var hint = _pendingScrollFieldName;
            _pendingScrollFieldName = null;
            var hit = Fields.FirstOrDefault(f => f.Name == hint);
            if (hit != null)
            {
                SelectedField = hit;
                ScrollToFieldRequested?.Invoke(hint);
                _log.Info($"UpdateDisplay: auto-scrolled to '{hint}' (pending scroll hint)");
                TryDrillIntoMatchedContainer(hit);
            }
            else
            {
                _log.Info($"UpdateDisplay: pending scroll hint '{hint}' not found in field list");
                // Drop the drill hint too — without the container field we
                // have nothing to drill into.
                _pendingDrillElementIndex = -1;
            }
        }
        else if (_pendingScrollFieldOffset is int wantOffset)
        {
            // Value Search cross-nav: match the owning property row by byte
            // offset (names aren't unique). Lands on the container row for a
            // map/array/set hit; TryDrillIntoMatchedContainer then drills to
            // the matched element when the display name carried a "[N]".
            _pendingScrollFieldOffset = null;
            ScrollToFieldByOffset(wantOffset);
        }

        // Store class address and load functions asynchronously. Track
        // the in-flight task so cross-tab navigators (e.g. Interesting
        // Funcs -> Live) can await it before calling
        // TrySelectFunctionByName -- otherwise the call races with the
        // function-list population and the row never gets selected.
        _currentClassAddr = result.ClassAddr;
        _pendingFunctionsLoad = LoadFunctionsAsync(result.ClassAddr);

        // DataTable detection: if this is a DataTable, fetch rows and inject synthetic RowMap field.
        // Capture the breadcrumb depth so the fire-and-forget load can detect a
        // navigation that happened during its round-trip (audit #7).
        _cachedDataTableRows = null;
        if (result.ClassName == "DataTable" && !string.IsNullOrEmpty(result.Address))
            _ = TryLoadDataTableRowsAsync(result.Address, Breadcrumbs.Count);
    }

    /// <summary>
    /// Detect DataTable and inject a synthetic RowMap field for container navigation.
    /// Called fire-and-forget from UpdateDisplay to avoid blocking the UI.
    /// </summary>
    private async Task TryLoadDataTableRowsAsync(string dataTableAddr, int bcAtStart)
    {
        try
        {
            var dtResult = await _dump.WalkDataTableRowsAsync(dataTableAddr);

            // Inject a synthetic "RowMap" field at the end of the field list
            var syntheticField = new LiveFieldValue
            {
                Name = "RowMap",
                TypeName = "DataTableRows",
                Offset = dtResult.RowMapOffset,
                Size = 0,
                // Badge here too: this row is what the user clicks to drill in, so the
                // "only 64 of these are actually fetched" fact belongs BEFORE the click,
                // not only after it (audit #5 V8).
                TypedValue = DataTableFieldPreview(dtResult),
                DataTableRowCount = dtResult.RowCount,
                DataTableStructName = dtResult.RowStructName,
                DataTableFNameSize = dtResult.FNameSize,
                DataTableStride = dtResult.Stride,
                DataTableRowStructAddr = dtResult.RowStructAddr,
                DataTableRowData = dtResult.Rows,
            };

            // Apply on the UI thread, GUARDED (audit #7): UpdateDisplay fires this
            // and forgets it. If the user navigated to another object during the
            // WalkDataTableRows round-trip, landing the synthetic RowMap (and
            // caching its rows) would attach the previous DataTable's data to the
            // CURRENT object's field grid — a stale-data glitch. Drop the result
            // when the displayed object changed under us. Both the check and the
            // mutation run on the UI thread so CurrentAddress / Breadcrumbs read
            // consistently and _cachedDataTableRows is written race-free.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (CurrentAddress != dataTableAddr || Breadcrumbs.Count != bcAtStart)
                {
                    _log.Info($"DataTable RowMap load superseded for {dataTableAddr} " +
                              $"(now {CurrentAddress}, bc {Breadcrumbs.Count}/{bcAtStart}) — dropped");
                    return;
                }
                _cachedDataTableRows = dtResult;
                Fields.Add(syntheticField);
            });
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to load DataTable rows for {dataTableAddr}", ex);
        }
    }

    private async Task LoadFunctionsAsync(string classAddr)
    {
        if (string.IsNullOrEmpty(classAddr) || classAddr == "0x0")
        {
            _allFunctions.Clear();
            Functions.Clear();
            HasFunctions = false;
            return;
        }

        try
        {
            var funcs = await _dump.WalkFunctionsAsync(classAddr);
            _allFunctions.Clear();
            _allFunctions.AddRange(funcs);
            HasFunctions = funcs.Count > 0;
            ApplyFunctionFilter();
        }
        catch
        {
            _allFunctions.Clear();
            Functions.Clear();
            HasFunctions = false;
        }
    }

    /// <summary>
    /// Rebuild the visible <see cref="Functions"/> collection from
    /// <see cref="_allFunctions"/> using <see cref="FunctionFilter"/>.
    /// Substring match on function name (case-insensitive). Empty filter
    /// shows everything. Mirrors the InterestingFunctions filter pattern
    /// so the UX is consistent across the two UFunction views.
    /// </summary>
    private void ApplyFunctionFilter()
    {
        Functions.Clear();
        if (_allFunctions.Count == 0) return;

        // Space-separated terms are ANDed (each must hit the function name) — the
        // shared Object Tree filter semantics. Empty filter shows everything.
        var terms = ObjectTreeFilter.SplitTerms(FunctionFilter);
        if (terms.Length == 0)
        {
            foreach (var f in _allFunctions) Functions.Add(f);
            return;
        }

        foreach (var f in _allFunctions)
        {
            if (ObjectTreeFilter.MatchesAllTerms(terms, f.Name))
                Functions.Add(f);
        }
    }

    /// <summary>
    /// Public entry point for cross-tab navigation: scroll to and select
    /// the named function in the Functions DataGrid. Returns true when the
    /// function was found and made current; false when the class has no
    /// such function (caller should still expand the section so the user
    /// can see the full list).
    /// </summary>
    public async Task<bool> TrySelectFunctionByNameAsync(string functionName)
    {
        if (string.IsNullOrEmpty(functionName)) return false;

        // Wait for any in-flight LoadFunctionsAsync triggered by the
        // preceding NavigateToAddress to finish. UpdateDisplay() kicks the
        // function load fire-and-forget so fields render immediately;
        // without this await the cross-tab navigator races the loader and
        // sees an empty _allFunctions on the first click after a class
        // change. The second click then succeeds because the previous
        // load already completed -- exactly the "(function not selected)"
        // pattern observed in the live-test logs.
        if (_pendingFunctionsLoad is { IsCompleted: false } pending)
        {
            try { await pending; }
            catch { /* loader logs its own error path; treat as miss */ }
        }

        if (_allFunctions.Count == 0) return false;

        // Clear filter first — a previously typed filter could hide the
        // target row even though it's in the underlying list. Flush before blanking:
        // this is a NAVIGATION clear, the case the keyword-search rule names. (AE16)
        if (!string.IsNullOrEmpty(FunctionFilter))
        {
            _functionFilterMemory.Flush();
            FunctionFilter = "";
        }

        // Auto-expand the section so the user can actually see the target
        // row without an extra click after a cross-tab navigation.
        IsFunctionsExpanded = true;

        foreach (var f in Functions)
        {
            if (string.Equals(f.Name, functionName, StringComparison.Ordinal))
            {
                SelectedFunction = f;
                ScrollToFunctionRequested?.Invoke(functionName);
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// A breadcrumb navigation item, recording navigation history for CE XML export.
/// </summary>
public sealed class BreadcrumbItem
{
    public string Address { get; init; } = "";
    public string Label { get; init; } = "";
    public string ClassAddr { get; init; } = "";

    /// <summary>Offset of the field that was clicked to reach this level (hex).</summary>
    public int FieldOffset { get; init; }

    /// <summary>Field name (e.g., "m_pAttributeSetHealth").</summary>
    public string FieldName { get; init; } = "";

    /// <summary>Class name of the object this breadcrumb resolves to (the pointer
    /// target / array element / struct type). Drives the +Type opt-in for a spine
    /// node that is NOT a container view (GameState, PawnPrivate, an array element
    /// [0]). Container-view nodes surface their element type via ContainerField
    /// instead. Empty when the class wasn't known at push time.</summary>
    public string TargetClassName { get; init; } = "";

    /// <summary>True if navigation was through a pointer dereference (ObjectProperty), false for inline struct.</summary>
    public bool IsPointerDeref { get; init; }

    /// <summary>Field name the user was looking at before drilling in. Used to restore scroll position on Back.</summary>
    public string? ScrollHintFieldName { get; set; }

    /// <summary>
    /// Rows that were SELECTED when the user last left this level (multi-select),
    /// replayed by Back / Forward / a breadcrumb jump. Stored as name+offset
    /// records rather than <see cref="LiveFieldValue"/> references: the forward
    /// stack can hold several levels at once and must not pin their field lists
    /// alive. Null when never captured — crumbs built by Locate-in-GWorld or a
    /// bookmark spine re-resolution only ever have <see cref="ScrollHintFieldName"/>,
    /// and the restore ladder falls back to that.
    /// </summary>
    public List<BookmarkFieldRef>? ViewSelectedFields { get; set; }

    /// <summary>Topmost visible row when the user last left this level — the scroll
    /// anchor (the Avalonia DataGrid exposes no pixel-offset API, so the position is
    /// restored by scrolling this row back into view). Null when never captured, or
    /// when the grid had no realised rows.</summary>
    public BookmarkFieldRef? ViewTopRow { get; set; }

    /// <summary>True if this breadcrumb represents a container element view (Array/Map/Set/DataTable).</summary>
    public bool IsContainerView { get; init; }

    /// <summary>The source container field (for refreshing container views).</summary>
    public LiveFieldValue? ContainerField { get; init; }

    /// <summary>True if this breadcrumb represents a DataTable row container view.</summary>
    public bool IsDataTableView { get; init; }

    /// <summary>Cached DataTable walk result (for refreshing DataTable row views).</summary>
    public DataTableWalkResult? DataTableData { get; set; }
}

/// <summary>
/// A saved bookmark slot capturing LiveWalker navigation state.
/// </summary>
public sealed class BookmarkSlot : ObservableObject
{
    public int SlotIndex { get; init; }

    /// <summary>1-based display number for UI binding.</summary>
    public int DisplayNumber => SlotIndex + 1;

    private bool _isOccupied;
    public bool IsOccupied
    {
        get => _isOccupied;
        // TooltipText is computed from IsOccupied + the saved metadata (which is
        // always assigned before IsOccupied flips true), so refresh the hint here.
        set { if (SetProperty(ref _isOccupied, value)) OnPropertyChanged(nameof(TooltipText)); }
    }

    private string _label = "";
    public string Label { get => _label; set => SetProperty(ref _label, value); }

    /// <summary>
    /// Hover hint for the slot button. Always non-empty so the user can tell an
    /// empty slot from a filled one before clicking: empty slots explain how to
    /// save, occupied slots show the target and invite a jump-back.
    /// </summary>
    public string TooltipText => IsOccupied
        ? $"Jump to bookmark {DisplayNumber}: {SavedClassName} :: {SavedObjectName}\n{SavedAddress}\nClick to restore this view (object, selected rows, scroll)."
        : $"Bookmark {DisplayNumber}: empty - no bookmark saved.\nClick ★ then this slot to save the current view here.";

    // Saved navigation state
    public List<BreadcrumbItem> SavedBreadcrumbs { get; set; } = new();
    public string SavedAddress { get; set; } = "";
    public string SavedObjectName { get; set; } = "";
    public string SavedClassName { get; set; } = "";
    public string SavedClassAddr { get; set; } = "";
    public WorldWalkResult? SavedCachedWorld { get; set; }

    /// <summary>Field rows (name + byte offset) the user had selected at save time.</summary>
    public List<BookmarkFieldRef> SavedSelectedFields { get; set; } = new();

    /// <summary>
    /// Topmost visible field row at save time — the anchor used to restore the
    /// scroll position. Null when no row was visible (e.g. empty grid). The
    /// Avalonia DataGrid exposes no public pixel-offset scroll API, so the view
    /// position is restored by scrolling this row back into view.
    /// </summary>
    public BookmarkFieldRef? SavedTopRow { get; set; }
}

/// <summary>Identifies a field row for bookmark re-selection (name + byte offset).</summary>
public sealed record BookmarkFieldRef(string Name, int Offset);

/// <summary>
/// Mutable carrier letting a synchronous event handler hand a value back to the
/// raiser. Used to pull the DataGrid's topmost-visible row from the View into the
/// ViewModel when a bookmark is saved (for scroll-position restore).
/// </summary>
public sealed class ViewAnchorRef { public BookmarkFieldRef? TopRow; }
