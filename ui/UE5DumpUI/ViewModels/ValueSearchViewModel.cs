using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>One entry in the Value Search server-side sort picker (V3-C).
/// <see cref="Label"/> is shown in the combo; <see cref="Key"/> is the
/// query_candidates wire string ("" / "scan" / "value" / "class" / "field"
/// / "type" / "offset" / "addr" / "instance").</summary>
public sealed record ValueSortOption(string Label, string Key);

/// <summary>
/// ViewModel for the Value Search panel (build 733+, port from
/// discrete Phase 27b shape).
///
/// Workflow:
///   1. User picks DataType + ScanType + Value(s), clicks First Scan.
///   2. DLL walks GObjects + UProperty fields matching DataType,
///      applies predicate, returns enriched candidates + a sessionId.
///   3. User narrows with Next Scan: changes ScanType / value(s) and
///      hits Refine. Prev-value scan types (Changed / Unchanged /
///      Increased / Decreased) compare against candidate snapshots
///      from the previous round.
///   4. New Scan ends the session and resets to step 1.
///
/// Native C++ fields (non-UPROPERTY) are NOT scanned -- this is a
/// hard contract surfaced in the panel's banner. See memory
/// project_value_search_caveats for rationale.
/// </summary>
public partial class ValueSearchViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;

    /// <summary>Cancels the in-flight First/Next scan. Cancelling abandons
    /// the UI-side wait immediately; the DLL self-terminates the scan at its
    /// own deadline (usually sub-second) and the orphaned response is
    /// discarded. (The single synchronous pipe can't interrupt a scan
    /// mid-flight without disconnecting — see Cancel.h / Fern monitor.)</summary>
    private System.Threading.CancellationTokenSource? _scanCts;

    // ------------------------------------------------------------------
    // Inputs
    // ------------------------------------------------------------------

    [ObservableProperty] private ValueScanDataType _selectedDataType = ValueScanDataType.Int32;
    [ObservableProperty] private ValueScanType     _selectedScanType = ValueScanType.Exact;
    [ObservableProperty] private string _value  = "";
    [ObservableProperty] private string _value2 = "";
    [ObservableProperty] private bool   _gameOnly = true;
    [ObservableProperty] private int    _maxResults = 50000;

    /// <summary>Scan deadline in SECONDS (the "Timeout" slider, 10–90s). When a
    /// First / Group scan exceeds this wall-clock budget the DLL bails early and
    /// returns whatever matched so far (the status banner notes the truncation).
    /// Default 25s; raise it for huge games that keep truncating, lower it for
    /// snappier scans on small ones. Threaded to the DLL as <c>deadline_ms</c> on
    /// begin_value_scan / begin_group_scan.</summary>
    [ObservableProperty] private int    _scanTimeoutSeconds = 25;

    /// <summary>Leaves kept PER SLOT per object in a group scan. NOT a speed knob — this
    /// list is what a later Changed/Decreased/Unchanged refine re-reads, and leaves are
    /// collected base-class-first, so too small a value silently hides a derived class's
    /// OWN fields behind its inherited ones. At the old fixed 8 every AActor stored only
    /// PrimaryActorTick/CustomTimeDilation and a Changed refine pruned every candidate.
    /// The DLL clamps to [8, 4096] regardless of what is sent.</summary>
    [ObservableProperty] private int    _groupPerSlotCap = Constants.GroupPerSlotCap;

    /// <summary>Powers of two from the DLL's minimum to its maximum. Only these are
    /// offered: nothing between them changes behaviour, so a free-entry box would just
    /// invite a value the DLL then clamps.</summary>
    public IReadOnlyList<int> PerSlotCapChoices { get; } = BuildPerSlotCapChoices();

    private static IReadOnlyList<int> BuildPerSlotCapChoices()
    {
        var list = new List<int>();
        for (int v = Constants.GroupPerSlotCapMin; v <= Constants.GroupPerSlotCapMax; v *= 2)
            list.Add(v);
        return list;
    }

    partial void OnGroupPerSlotCapChanged(int value)
    {
        if (value < Constants.GroupPerSlotCapMin)      GroupPerSlotCap = Constants.GroupPerSlotCapMin;
        else if (value > Constants.GroupPerSlotCapMax) GroupPerSlotCap = Constants.GroupPerSlotCapMax;
    }

    // Defensive clamp (the slider already bounds 10–90) so a programmatic / restored
    // value can't push an out-of-band deadline onto the wire.
    partial void OnScanTimeoutSecondsChanged(int value)
    {
        if (value < 10) ScanTimeoutSeconds = 10;
        else if (value > 90) ScanTimeoutSeconds = 90;
    }

    /// <summary>When true (default) the DLL walks GObjects with worker threads
    /// (fast). Turn off to force a single-threaded scan so concurrent
    /// cross-thread memory reads don't trip a game's anti-tamper — slower but
    /// stealthier. Only the First Scan parallelizes; Refine is already serial.</summary>
    [ObservableProperty] private bool   _parallelScan = true;

    /// <summary>When true (default) the DLL reads each object's fixed-width leaf
    /// fields in one body read (fewer SEH reads + better locality on the
    /// scattered object walk). Turn off to force one read per field — slower,
    /// but reads only the exact bytes each field needs. First Scan only.</summary>
    [ObservableProperty] private bool   _batchRead = true;

    /// <summary>Opt-in deep-container pass (default OFF). When on, the scan also
    /// walks deeply-nested containers (struct-arrays, struct-valued maps, nested
    /// TArray/TSet) so a value buried inside e.g.
    /// <c>SaveSlotList[1].MsTuneData.MsTunes[0].WeaponTuneList[0].Tunes[N]</c> is
    /// found. Heavier per object — shared by Single and Group modes. In Group mode
    /// each numeric array / struct-array element becomes its own match block.</summary>
    [ObservableProperty] private bool   _deepScan;

    /// <summary>Opt-in cross-object pass (Group mode only, default OFF; P4). When on,
    /// each actor's block also folds in the numeric leaves of the sub-objects it OWNS
    /// (its components + a GAS ASC's AttributeSets), so a group whose values are
    /// distributed across {actor, components, attribute sets} is matched as one block.
    /// Ownership + value driven (not class-name driven).</summary>
    [ObservableProperty] private bool   _crossObjectScan;

    /// <summary>Native-C (P1, opt-in, default OFF). When on, the scan ALSO inspects
    /// each object's unmanaged holes — the byte ranges within the object that no
    /// UPROPERTY covers — for the requested numeric value, so native (non-UPROPERTY)
    /// C++ members like HP/MP become findable. Intentionally noisy on first scan:
    /// pair with Newest-first + Next-Scan refine. Numeric data types only.</summary>
    [ObservableProperty] private bool   _nativeCScan;

    /// <summary>Walk GObjects newest-first (high index → low) so that when results
    /// hit the cap the survivors are the most-recently-allocated instances rather
    /// than low-index CDOs/templates. Auto-enabled when Native-C is turned on (the
    /// user may then uncheck it); auto-cleared when Native-C is turned off. Useful
    /// on its own for catching a just-spawned actor. Default OFF.</summary>
    [ObservableProperty] private bool   _newestFirst;

    // Couple Newest-first to Native-C per the owner's rule: enabling Native-C
    // pre-checks Newest-first (the common case — native first-scans get truncated
    // and you usually want the newest instances), but the user can uncheck it
    // (e.g. when hunting an operated-character value that's allocated early and
    // persists). Disabling Native-C clears Newest-first if it's still set.
    partial void OnNativeCScanChanged(bool value)
    {
        if (value) NewestFirst = true;
        else if (NewestFirst) NewestFirst = false;
    }

    /// <summary>"Auto detect Engine/System noise" PRE-filter (opt-in, default OFF).
    /// When on, pure engine/system classes (UI widgets, textures, sounds, particle/
    /// Niagara systems, anim instances, /Script engine packages) are skipped at the
    /// SOURCE during the scan, so their instances never enter the candidate set —
    /// a looser, heuristic complement to the exact post-scan class picker. A
    /// gameplay guardrail force-keeps Actor / Pawn / Character / ActorComponent /
    /// Controller / PlayerState / GameInstance-derived classes, so a player Pawn's
    /// X/Y/Z (and HP/MP in components or GAS AttributeSets) is never source-skipped.
    /// Shared by Single + Group modes; locked at First Scan (refine reuses survivors).</summary>
    [ObservableProperty] private bool _preFilterNoise;

    /// <summary>Per-panel rounding mode: how a fractional float/double target is
    /// reduced to the integer the game DISPLAYS before compare (Round/Trunc/Ceil).
    /// Replaces the old ± tolerance band. Persisted; default <see cref="FloatRoundMode.Round"/>.
    /// Integer fields are reduce-invariant; a fractional target/bound is coerced via
    /// the mode. Shown for every type except Bool + the 3 string types.</summary>
    [ObservableProperty] private FloatRoundMode _selectedRoundingMode = FloatRoundMode.Round;

    /// <summary>Picker options for <see cref="SelectedRoundingMode"/>.</summary>
    public IReadOnlyList<FloatRoundMode> RoundingModeOptions { get; } =
        new[] { FloatRoundMode.Round, FloatRoundMode.Trunc, FloatRoundMode.Ceil };

    /// <summary>One-line explanation of the active rounding mode (UI hint).</summary>
    public string RoundingModeHint => SelectedRoundingMode switch
    {
        FloatRoundMode.Trunc => "Integer fields truncate a decimal target (10.9 → 10; Between 10.9–11.1 → 10–11). Float/double values are used as-is.",
        FloatRoundMode.Ceil  => "Integer fields round a decimal target up (10.9 → 11; Between 10.9–11.1 → 11–12). Float/double values are used as-is.",
        _                    => "Integer fields round a decimal target to nearest (10.9 → 11; Between 10.9–11.1 → 11–11). Float/double values are used as-is.",
    };

    partial void OnSelectedRoundingModeChanged(FloatRoundMode value)
    {
        OnPropertyChanged(nameof(RoundingModeHint));
        OnPropertyChanged(nameof(BetweenPreview));
        SyncGroupRowRoundMode();   // group rows render their own preview from this mode
    }

    /// <summary>Live "what will this actually match" preview for a single Between scan,
    /// shown after the upper-bound box (e.g. <c>→ int 12~13 · float 11.5~13.2</c> under
    /// Round). Adapts to the selected DataType: integer types show only the coerced
    /// integer range, Float/Double/vector show only the literal float range, the mixed
    /// numeric meta types show both. Empty for non-Between / non-numeric / unparseable
    /// bounds (the label hides itself).</summary>
    public string BetweenPreview =>
        SelectedScanType == ValueScanType.Between && SupportsRoundingMode
            ? RoundModePreview.Between(Value, Value2, SelectedRoundingMode, BetweenPreviewScope)
            : "";

    /// <summary>Which field interpretation(s) the live preview shows for the current
    /// DataType — float-only for the float / vector types, both for the mixed numeric
    /// meta types, integer-only for the fixed-width integer types.</summary>
    private RoundModePreview.Scope BetweenPreviewScope => SelectedDataType switch
    {
        ValueScanDataType.Float or ValueScanDataType.Double
            or ValueScanDataType.FVector or ValueScanDataType.FRotator
            or ValueScanDataType.FTransform => RoundModePreview.Scope.FloatOnly,
        ValueScanDataType.NumericNoByte or ValueScanDataType.NumericAll
            => RoundModePreview.Scope.Both,
        _   => RoundModePreview.Scope.IntOnly,
    };

    partial void OnValueChanged(string value)  => OnPropertyChanged(nameof(BetweenPreview));
    partial void OnValue2Changed(string value) => OnPropertyChanged(nameof(BetweenPreview));

    /// <summary>Push the panel's rounding mode into every group row so each renders its
    /// own live Between preview (the actual scan still uses the panel-level mode).</summary>
    private void SyncGroupRowRoundMode()
    {
        foreach (var row in GroupInputs) row.RoundMode = SelectedRoundingMode;
    }

    /// <summary>Opt-in case sensitivity for string scans. Default
    /// false matches CE's case-insensitive convention. Has no effect
    /// for non-string DataTypes (the wire serializer strips it).</summary>
    [ObservableProperty] private bool _caseSensitive = false;

    public IReadOnlyList<ValueScanDataType> DataTypeOptions { get; } = new[]
    {
        // Multi-numeric meta scan: one pass over every word/dword/qword/
        // float/double field, comparing per declared width. Listed first
        // because it's the natural "I don't know the stored type yet"
        // starting point. "No byte" excludes 1-byte + bool fields so a
        // small value doesn't explode the candidate set.
        ValueScanDataType.NumericNoByte,
        // Same one-pass scan but INCLUDING 1-byte fields. Listed right
        // after the no-byte variant; the VM shows a result-volume warning
        // when it's selected (small values flood 1-byte fields).
        ValueScanDataType.NumericAll,
        ValueScanDataType.Int32,
        ValueScanDataType.Int64,
        ValueScanDataType.Int16,
        ValueScanDataType.Int8,
        ValueScanDataType.UInt32,
        ValueScanDataType.UInt64,
        ValueScanDataType.UInt16,
        ValueScanDataType.UInt8,
        ValueScanDataType.Float,
        ValueScanDataType.Double,
        ValueScanDataType.Bool,
        // Phase 2A — string types. FText is best-effort (cooked games
        // strip a lot of metadata so display strings may be unresolvable;
        // we keep it as a third option since the UE NameProperty /
        // StrProperty / TextProperty fields are common enough that
        // exposing all three is friendlier than guessing).
        ValueScanDataType.FString,
        ValueScanDataType.FName,
        ValueScanDataType.FText,
        // Phase 2B — vector types. FTransform is wire-stable but
        // currently returns zero hits pending per-version Translation
        // offset detection; deliberately not exposed in the dropdown
        // until that lands.
        ValueScanDataType.FVector,
        ValueScanDataType.FRotator,
    };

    /// <summary>Full set of scan-type values; the panel uses
    /// <see cref="VisibleScanTypeOptions"/> instead so the dropdown
    /// shows only entries valid for the current DataType.</summary>
    private static readonly ValueScanType[] s_allScanTypes = new[]
    {
        ValueScanType.Exact,
        ValueScanType.Bigger,
        ValueScanType.Smaller,
        ValueScanType.Between,
        ValueScanType.Changed,
        ValueScanType.Unchanged,
        ValueScanType.Increased,
        ValueScanType.Decreased,
        ValueScanType.Contains,
        ValueScanType.StartsWith,
        ValueScanType.EndsWith,
    };

    /// <summary>Scan types valid for the current DataType. Numeric +
    /// Vector use ordering predicates; string types use substring
    /// predicates. Mirror of the DLL's <c>IsScanTypeValidFor</c>.</summary>
    public IReadOnlyList<ValueScanType> VisibleScanTypeOptions =>
        FilterScanTypes(SelectedDataType);

    /// <summary>Static helper: filter the global scan-type list by a
    /// given DataType. Public so unit tests can lock the contract
    /// without standing up a ViewModel instance.</summary>
    public static IReadOnlyList<ValueScanType> FilterScanTypes(ValueScanDataType dt)
    {
        return s_allScanTypes.Where(st => IsScanTypeValidFor(dt, st)).ToList();
    }

    /// <summary>True when the (dataType, scanType) pair is a legal
    /// combination. Mirror of DLL <c>ValueScan::IsScanTypeValidFor</c>:
    ///   String : Exact / Contains / StartsWith / EndsWith / Changed / Unchanged
    ///   Vector / Numeric: Exact / Bigger / Smaller / Between / Changed /
    ///                     Unchanged / Increased / Decreased
    /// </summary>
    public static bool IsScanTypeValidFor(ValueScanDataType dt, ValueScanType st)
    {
        bool isString = IsStringDataType(dt);
        bool isSubstring = st == ValueScanType.Contains
                        || st == ValueScanType.StartsWith
                        || st == ValueScanType.EndsWith;
        if (isString)
        {
            return st == ValueScanType.Exact
                || st == ValueScanType.Contains
                || st == ValueScanType.StartsWith
                || st == ValueScanType.EndsWith
                || st == ValueScanType.Changed
                || st == ValueScanType.Unchanged;
        }
        // Numeric + Vector: substring predicates reject; everything
        // else is valid.
        return !isSubstring;
    }

    public static bool IsStringDataType(ValueScanDataType dt) =>
        dt == ValueScanDataType.FString
        || dt == ValueScanDataType.FName
        || dt == ValueScanDataType.FText;

    public static bool IsVectorDataType(ValueScanDataType dt) =>
        dt == ValueScanDataType.FVector
        || dt == ValueScanDataType.FRotator
        || dt == ValueScanDataType.FTransform;

    // ------------------------------------------------------------------
    // Output state
    // ------------------------------------------------------------------

    [ObservableProperty] private ulong _sessionId;
    [ObservableProperty] private string _statusText = "Click First Scan to scan for a value across all UPROPERTY fields.";
    [ObservableProperty] private bool   _isScanning;
    [ObservableProperty] private string _errorMessage = "";
    /// <summary>The bound result rows shown by the grid — the CURRENT
    /// server-returned window (V3-C), NOT the full set. A typed
    /// ObservableCollection (not a DataGridCollectionView) so the grid's
    /// compiled column bindings infer the row type. Filter / sort / paging run
    /// server-side over the full session set held in the DLL.</summary>
    [ObservableProperty] private ObservableCollection<ValueCandidate> _candidates = new();
    [ObservableProperty] private ValueCandidate? _selectedCandidate;

    /// <summary>Full candidate count held in the DLL session.</summary>
    [ObservableProperty] private int _total;
    /// <summary>Count after the keyword filter (== <see cref="Total"/> when no
    /// filter is active).</summary>
    [ObservableProperty] private int _filteredTotal;
    /// <summary>One-line "showing N of M" window status for the panel.</summary>
    [ObservableProperty] private string _windowStatus = "";

    /// <summary>True while the loaded window is smaller than the (filtered)
    /// total — drives the Load More button.</summary>
    public bool HasMore => Candidates.Count < FilteredTotal;

    partial void OnCandidatesChanged(ObservableCollection<ValueCandidate> value)
        => OnPropertyChanged(nameof(HasMore));
    partial void OnFilteredTotalChanged(int value)
        => OnPropertyChanged(nameof(HasMore));

    // Server sort key (wire string: "" / "scan" / "value" / "class" / "field"
    // / "instance" / "type" / "offset" / "addr" / "index"); driven by the
    // DataGrid column-sort handler in the panel code-behind.
    private string _sortKey = "";
    private bool   _sortDesc;

    // Set while a single ApplyColumnSort updates BOTH the sort key and the
    // direction, so the two property-change handlers don't each kick off a
    // (superseded) server query — ApplyColumnSort issues exactly one reload.
    private bool _suppressSortReload;
    private bool _suppressGroupSortReload;

    // Rows fetched per window (begin/refine first page + Load More + reloads).
    private const int PageSize = 1000;

    private System.Threading.CancellationTokenSource? _viewCts;
    private System.Threading.CancellationTokenSource? _filterCts;

    /// <summary>Case-insensitive keyword filter — now SERVER-SIDE over the full
    /// session set (V3-C). A loaded-window-only filter would be untrustworthy
    /// ("no match" couldn't distinguish "not in the data" from "not loaded"),
    /// so a change reloads window 0 from the DLL (debounced).</summary>
    [ObservableProperty] private string _filterText = "";

    /// <summary>Per-session remembered filter keywords (LRU) surfaced as the
    /// single-mode filter box's AutoCompleteBox suggestions — see
    /// <see cref="KeywordSearchMemory"/>. The filter is server-side (the DLL windows
    /// candidates) and asynchronous, so a debounced <c>Schedule</c> probe would race
    /// the pipe round-trip and read a stale count. Instead we <c>Commit</c> the keyword
    /// from <see cref="LoadWindowAsync"/> once the result count for it has landed.</summary>
    private readonly KeywordSearchMemory _filterMemory;
    public ObservableCollection<string> FilterHistory => _filterMemory.History;

    partial void OnFilterTextChanged(string value) => _ = DebouncedReloadAsync();

    private bool IsDefaultView =>
        string.IsNullOrEmpty(FilterText)
        && (string.IsNullOrEmpty(_sortKey) || _sortKey == "scan")
        && !_sortDesc
        && !ClassFilter.AnyExcluded;   // class exclusion needs the server window path

    /// <summary>After a First/Next scan: adopt <paramref name="total"/> and
    /// show window 0. When the view is default (no filter, scan order) the
    /// inline first page returned by begin/refine is shown directly (no extra
    /// round-trip); otherwise the active filter/sort are re-applied
    /// server-side over the new set.</summary>
    private async Task ApplyScanResultAsync(int total, IList<ValueCandidate> inlineFirstPage)
    {
        Total = total;
        if (IsDefaultView)
        {
            FilteredTotal = total;
            Candidates = new ObservableCollection<ValueCandidate>(inlineFirstPage);
            UpdateWindowStatus();
        }
        else
        {
            await LoadWindowAsync(reset: true);
        }
    }

    /// <summary>Fetch a window from the DLL with the current filter/sort.
    /// reset=true replaces the window (page 0); reset=false appends the next
    /// page (Load More). A newer query cancels an in-flight one.</summary>
    private async Task LoadWindowAsync(bool reset)
    {
        if (!HasSession) return;
        _viewCts?.Cancel();
        var cts = _viewCts = new System.Threading.CancellationTokenSource();
        try
        {
            int offset = reset ? 0 : Candidates.Count;
            var w = await _dump.QueryCandidatesAsync(
                SessionId, offset, PageSize,
                string.IsNullOrEmpty(FilterText) ? null : FilterText,
                string.IsNullOrEmpty(_sortKey) ? null : _sortKey,
                _sortDesc, ClassFilter.ExcludedClasses, cts.Token);
            Total = w.Total;
            FilteredTotal = w.FilteredTotal;
            if (reset)
                Candidates = new ObservableCollection<ValueCandidate>(w.Candidates);
            else
                foreach (var c in w.Candidates) Candidates.Add(c);
            OnPropertyChanged(nameof(HasMore));
            UpdateWindowStatus();
            // Filter keyword-memory: remember the settled keyword only now that the
            // server-reported count for it has landed (a page-0 reload = a filter/sort
            // change). Commit — not a timer probe — so it never reads a stale count.
            if (reset) _filterMemory.Commit();
        }
        catch (OperationCanceledException) { /* superseded by a newer query */ }
        catch (Exception ex)
        {
            ErrorMessage = $"Query failed: {ex.Message}";
            _log.Error("ValueSearch query_candidates failed", ex);
        }
        finally
        {
            if (ReferenceEquals(_viewCts, cts)) { _viewCts?.Dispose(); _viewCts = null; }
        }
    }

    private async Task DebouncedReloadAsync()
    {
        if (!HasSession) return;
        _filterCts?.Cancel();
        var cts = _filterCts = new System.Threading.CancellationTokenSource();
        try
        {
            await Task.Delay(250, cts.Token);
            await LoadWindowAsync(reset: true);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_filterCts, cts)) { _filterCts?.Dispose(); _filterCts = null; }
        }
    }

    // --- V3-C server-side sort picker. Replaces client-side column-header
    // sort, which could only reorder the loaded window (misleading once the
    // set is windowed). Sorting runs in the DLL over the FULL session set. ---

    /// <summary>Sort options for the picker. Label is shown in the combo; Key
    /// is the query_candidates wire string.</summary>
    public IReadOnlyList<ValueSortOption> SortOptions { get; } = new[]
    {
        new ValueSortOption("Scan order", "scan"),
        new ValueSortOption("Value",      "value"),
        new ValueSortOption("Class",      "class"),
        new ValueSortOption("Field",      "field"),
        new ValueSortOption("Type",       "type"),
        new ValueSortOption("Offset",     "offset"),
        new ValueSortOption("Address",    "addr"),
        new ValueSortOption("Instance",   "instance"),
    };

    [ObservableProperty] private ValueSortOption? _selectedSortOption;
    [ObservableProperty] private bool _sortDescending;

    partial void OnSelectedSortOptionChanged(ValueSortOption? value) => ApplyUiSort();
    partial void OnSortDescendingChanged(bool value) => ApplyUiSort();

    private void ApplyUiSort()
    {
        _sortKey  = SelectedSortOption?.Key ?? "";
        _sortDesc = SortDescending;
        if (_suppressSortReload) return;
        if (HasSession) _ = LoadWindowAsync(reset: true);
    }

    /// <summary>Drive the server-side sort from a DataGrid column-header click.
    /// The grid is windowed (V3-C) so client-side column sorting would only
    /// reorder the loaded page — instead a header click maps to one of the
    /// <see cref="SortOptions"/> keys and re-sorts the WHOLE session set in the
    /// DLL (keeping the Sort picker + Desc toggle in sync). Clicking the active
    /// column toggles direction; clicking a new column starts ascending.</summary>
    public void ApplyColumnSort(string sortKey)
    {
        var opt = SortOptions.FirstOrDefault(o => o.Key == sortKey);
        if (opt == null) return;

        if (SelectedSortOption?.Key == sortKey)
        {
            SortDescending = !SortDescending;          // toggle direction (single reload)
            return;
        }

        // New column → switch the sort key and reset to ascending. Suppress
        // the per-property reload so the two changes coalesce into ONE server
        // query (otherwise the first would only be superseded/cancelled).
        _suppressSortReload = true;
        SortDescending = false;
        SelectedSortOption = opt;
        _suppressSortReload = false;
        ApplyUiSort();
    }

    [RelayCommand]
    private Task LoadMoreAsync() => LoadWindowAsync(reset: false);

    private void UpdateWindowStatus()
    {
        if (Total == 0) { WindowStatus = ""; return; }
        string filt = (FilteredTotal != Total) ? $" (filtered from {Total})" : "";
        WindowStatus = HasMore
            ? $"Showing {Candidates.Count} of {FilteredTotal}{filt} — Load More for the rest"
            : $"Showing all {FilteredTotal}{filt}";
    }

    /// <summary>True when a scan session is active (between First Scan
    /// and New Scan / End). Drives the enablement of the Next Scan
    /// button and the visibility of the New Scan reset button.</summary>
    public bool HasSession => SessionId != 0;
    partial void OnSessionIdChanged(ulong value) => OnPropertyChanged(nameof(HasSession));

    /// <summary>True when the selected ScanType compares against a
    /// previously-observed value (Changed / Unchanged / Increased /
    /// Decreased). The Value / Value2 input boxes are hidden when this
    /// is set so the user doesn't think a value is required.</summary>
    public bool RequiresValueInput => !IsPrevValueScanType(SelectedScanType);

    /// <summary>True when ScanType is Between -- the second value box
    /// becomes visible.</summary>
    public bool RequiresValue2Input => SelectedScanType == ValueScanType.Between;

    partial void OnSelectedScanTypeChanged(ValueScanType value)
    {
        OnPropertyChanged(nameof(RequiresValueInput));
        OnPropertyChanged(nameof(RequiresValue2Input));
        OnPropertyChanged(nameof(BetweenPreview));
    }

    /// <summary>True when the rounding-mode picker applies — every numeric +
    /// vector type. Hidden for Bool and the 3 string types (no fractional target to
    /// reduce). The rounding mode reduces a fractional float/double target to the
    /// displayed integer before compare.</summary>
    public bool SupportsRoundingMode =>
        SelectedDataType != ValueScanDataType.Bool
        && !IsStringDataType(SelectedDataType);

    /// <summary>True for the multi-numeric meta types (NumericNoByte /
    /// NumericAll) that fan out over a fixed member set instead of a
    /// single fixed-width compare. Mirror of DLL
    /// <c>ValueScan::IsMultiNumericDataType</c>.</summary>
    public static bool IsMultiNumericDataType(ValueScanDataType dt) =>
        dt == ValueScanDataType.NumericNoByte
        || dt == ValueScanDataType.NumericAll;

    /// <summary>Non-empty when the selected DataType warrants a caution
    /// to the user. Currently only NumericAll: including 1-byte fields
    /// means small values (0/1/255) match a very large number of fields,
    /// so the candidate set can explode. Empty for every other type
    /// (drives an orange hint TextBlock via IsNotNullOrEmpty).</summary>
    public string DataTypeWarning =>
        SelectedDataType == ValueScanDataType.NumericAll
            ? "NumericAll includes 1-byte fields — small values (e.g. 0 / 1 / 255) " +
              "will match a very large number of fields and can flood the results. " +
              "Use a larger / more specific value, or pick NumericNoByte to skip 1-byte fields."
            : "";

    /// <summary>True when the selected DataType is a string type
    /// (FString / FName / FText) — drives the Case-sensitive checkbox
    /// visibility. Non-string types hide it because the wire layer
    /// strips the flag for them anyway.</summary>
    public bool SupportsCaseSensitive => IsStringDataType(SelectedDataType);

    partial void OnSelectedDataTypeChanged(ValueScanDataType value)
    {
        OnPropertyChanged(nameof(SupportsRoundingMode));
        OnPropertyChanged(nameof(SupportsCaseSensitive));
        OnPropertyChanged(nameof(DataTypeWarning));
        OnPropertyChanged(nameof(VisibleScanTypeOptions));
        OnPropertyChanged(nameof(BetweenPreview));   // scope (int/float/both) depends on the type
        // If the currently-selected ScanType is no longer valid for
        // the new DataType (e.g. user switched from Int32+Bigger to
        // FString+Bigger), snap it to a sensible default that exists
        // in every (DataType, ScanType) matrix cell -- Exact.
        if (!IsScanTypeValidFor(value, SelectedScanType))
        {
            SelectedScanType = ValueScanType.Exact;
        }
    }

    public static bool IsPrevValueScanType(ValueScanType st) => st switch
    {
        ValueScanType.Changed
            or ValueScanType.Unchanged
            or ValueScanType.Increased
            or ValueScanType.Decreased => true,
        _ => false,
    };

    public static bool IsFirstScanType(ValueScanType st) => st switch
    {
        ValueScanType.Exact
            or ValueScanType.Bigger
            or ValueScanType.Smaller
            or ValueScanType.Between
            or ValueScanType.Contains
            or ValueScanType.StartsWith
            or ValueScanType.EndsWith => true,
        _ => false,
    };

    // ------------------------------------------------------------------
    // Cross-tab navigation (Open in Live Walker via address)
    // ------------------------------------------------------------------

    // (instanceAddr, fieldOffset, fieldDisplayName). Carries the candidate's
    // owning-property byte offset + display name so Live Walker can focus the
    // exact field that produced the hit instead of just opening the instance.
    // Field names aren't unique (inherited members, map .Key/.Value), so the
    // Walker matches the row by offset; the display name supplies the "[N]"
    // element suffix for container hits.
    public event Action<string, int, string>? NavigateToInstance;
    public event Action<string>? RequestCopyText;

    /// <summary>Raised to open the chosen candidate's owning class in the
    /// Instance Finder tab — pre-fills the class name and auto-runs the
    /// search (mirrors the Property Search / Game Class "finder" handoff).
    /// Payload = class name.</summary>
    public event Action<string>? NavigateToInstanceFinder;

    /// <summary>Raised to pivot the chosen candidate's (className, fieldName) in the
    /// experimental Class Pivot tab — the value-locator → pivot handoff (mirrors the
    /// C5 right-click handoff on the property panels). A value-scan hit already
    /// carries ClassName + FieldName, so "I can see this value on screen" reaches a
    /// grouped pivot in one click.</summary>
    public event Action<string, string>? NavigateToPivot;

    /// <summary>Gates the per-row "Pivot" button — true only when the experimental
    /// Class Pivot tab is available. Hidden when experimental features are off.</summary>
    [ObservableProperty] private bool _pivotEnabled;

    /// <summary>Raised to locate the chosen candidate within the GWorld object graph
    /// (forward path search). Payload = (owning instance address, value field byte
    /// offset, value field display name — for the "[N]" container element suffix).</summary>
    public event Action<string, int, string>? LocateInGWorld;

    /// <summary>Engine-rooted counterpart of <see cref="LocateInGWorld"/> — the path
    /// search is rooted at the live UGameEngine instead of GWorld. Same payload.</summary>
    public event Action<string, int, string>? LocateInGameEngine;

    /// <summary>Raised to show the chosen candidate's owning object's related
    /// objects (components, GAS ASC → AttributeSets, Controller↔Pawn) in the
    /// Related tab. Payload = owning instance address.</summary>
    public event Action<string>? NavigateToRelatedObjects;

    /// <summary>True when GWorld is available — gates the per-row "Locate in GWorld" button.</summary>

    /// <summary>Server-side class-noise filter for SINGLE mode. Unlike the
    /// client-side panels, the candidate set is windowed in the DLL — so the
    /// histogram comes from the begin/refine response and ticking re-queries the
    /// window with an <c>exclude_classes</c> list. Ticking triggers a server
    /// window reload (debounce-free; the DLL filter is fast).</summary>
    public ClassFacetFilter ClassFilter { get; }

    /// <summary>Server-side class-noise filter for GROUP mode (buckets on the
    /// candidate's object-level class).</summary>
    public ClassFacetFilter GroupClassFilter { get; }

    public ValueSearchViewModel(IDumpService dump, ILoggingService log)
    {
        _dump = dump;
        _log  = log;
        _filterMemory = new KeywordSearchMemory(() => (FilterText, FilteredTotal > 0));
        _groupFilterMemory = new KeywordSearchMemory(() => (GroupFilterText, GroupFilteredTotal > 0));
        _selectedSortOption = SortOptions[0];  // scan order
        _selectedGroupSortOption = GroupSortOptions[0];  // scan order (group mode)
        ClassFilter = new ClassFacetFilter(() => { if (HasSession) _ = LoadWindowAsync(reset: true); })
        {
            AutoDetectProvider = AutoDetectNoiseAsync,
        };
        GroupClassFilter = new ClassFacetFilter(() => { if (HasGroupSession) _ = LoadGroupWindowAsync(reset: true); })
        {
            AutoDetectProvider = AutoDetectNoiseAsync,
        };
    }

    public void SetEngineState(EngineState state)
    {
    }

    // Shared auto-detect provider for both class filters: classify the facet
    // class names via the DLL (safe rules) and adapt to the helper's tuple shape.
    private async Task<IReadOnlyList<(string className, bool isNoise, string reason)>>
        AutoDetectNoiseAsync(IReadOnlyList<string> names)
    {
        var verdicts = await _dump.DetectNoiseClassesAsync(names);
        return verdicts.Select(n => (n.ClassName, n.IsNoise, n.Reason)).ToList();
    }

    [RelayCommand]
    private async Task FirstScanAsync()
    {
        if (IsScanning) return;

        if (!IsFirstScanType(SelectedScanType))
        {
            ErrorMessage = "First Scan supports targeted predicates only (Exact / " +
                           "Bigger / Smaller / Between / Contains / StartsWith / EndsWith). " +
                           "Use Next Scan for Changed / Unchanged / Increased / Decreased.";
            return;
        }
        if (!IsScanTypeValidFor(SelectedDataType, SelectedScanType))
        {
            ErrorMessage = $"Scan type '{SelectedScanType}' is not valid for {SelectedDataType}.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Value))
        {
            ErrorMessage = "Value is required.";
            return;
        }
        if (SelectedScanType == ValueScanType.Between && string.IsNullOrWhiteSpace(Value2))
        {
            ErrorMessage = "Between requires Value and Value2.";
            return;
        }

        // Record what this heavy operation costs the DLL dispatcher. Automatic rather than
        // a measurement session: the evidence then accumulates from real use instead of only
        // the scenario somebody thought to test. Degrades to a no-op when not connected;
        // never affects the operation.
        //
        // AFTER validation, deliberately. Opening it above the four early returns cost two
        // get_diagnostics round-trips on every rejected click and filed a "Value Scan (First)"
        // measurement for a scan that never ran — polluting the very dataset the probe exists
        // to collect, with samples whose duration is the validation, not the scan.
        // (audit #5 AE8)
        await using var _perf = await Services.DiagnosticsProbe.BeginAsync(_dump, _log, "Value Scan (First)");

        var cts = _scanCts = new System.Threading.CancellationTokenSource();
        try
        {
            IsScanning  = true;
            ErrorMessage = "";
            StatusText  = $"Scanning {SelectedDataType} fields...";

            // If a session is already open (user changed mind without
            // clicking New Scan), retire it first so the DLL doesn't
            // accumulate orphan sessions.
            await EndSessionIfAnyAsync();

            // Rounding mode reduces a fractional float/double target to the displayed
            // integer before compare; only meaningful for numeric/vector types, but the
            // DLL ignores it for integer + string scans anyway.
            var effRound = SupportsRoundingMode ? SelectedRoundingMode : FloatRoundMode.Round;
            // Case sensitivity only passes through for string types;
            // wire serializer enforces the same restriction.
            bool effCase = SupportsCaseSensitive && CaseSensitive;
            var result = await _dump.BeginValueScanAsync(
                SelectedDataType, SelectedScanType, Value,
                SelectedScanType == ValueScanType.Between ? Value2 : null,
                GameOnly, MaxResults, effRound, effCase, ParallelScan, BatchRead, DeepScan,
                NativeCScan, NewestFirst, PageSize, ScanTimeoutSeconds * 1000,
                PreFilterNoise, cts.Token);

            SessionId = result.SessionId;
            // Populate the class-noise picker from the server histogram BEFORE
            // applying the window (ApplyScanResultAsync consults ClassFilter via
            // IsDefaultView). countsPartial when the scan hit its deadline / cap.
            ClassFilter.RebuildFromCounts(
                result.ClassHistogram.Select(c => (c.ClassName, c.Count)),
                result.ClassDistinct, countsPartial: result.DeadlineHit);
            await ApplyScanResultAsync(result.Total, result.Candidates);

            var summary = $"First Scan: {result.Total} candidates in {result.DurationMs} ms " +
                          $"(scanned {result.ScannedObjects} objects, " +
                          $"{result.ScannedClasses} classes with matching fields)";
            if (result.DeadlineHit)
                summary += $"  ⚠ truncated ({ScanTimeoutSeconds}s deadline / result cap) — raise the Timeout slider or narrow the predicate";
            StatusText = summary;
        }
        catch (OperationCanceledException)
        {
            StatusText = "First Scan cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"First Scan failed: {ex.Message}";
            _log.Error($"ValueSearch First Scan failed", ex);
        }
        finally
        {
            IsScanning = false;
            if (ReferenceEquals(_scanCts, cts)) { _scanCts?.Dispose(); _scanCts = null; }
        }
    }

    [RelayCommand]
    private async Task NextScanAsync()
    {
        if (IsScanning || !HasSession) return;

        bool needsValue = !IsPrevValueScanType(SelectedScanType);
        if (needsValue && string.IsNullOrWhiteSpace(Value))
        {
            ErrorMessage = "Value is required for this Scan Type.";
            return;
        }
        if (SelectedScanType == ValueScanType.Between && string.IsNullOrWhiteSpace(Value2))
        {
            ErrorMessage = "Between requires Value and Value2.";
            return;
        }

        // Same ordering as First Scan above, and for the same reason — this site has the
        // identical defect and the finding named only First. (audit #5 AE8)
        await using var _perf = await Services.DiagnosticsProbe.BeginAsync(_dump, _log, "Value Scan (Next)");

        var cts = _scanCts = new System.Threading.CancellationTokenSource();
        try
        {
            IsScanning  = true;
            ErrorMessage = "";
            StatusText  = $"Refining ({SelectedScanType})...";

            var effRound = SupportsRoundingMode ? SelectedRoundingMode : FloatRoundMode.Round;
            bool effCase = SupportsCaseSensitive && CaseSensitive;
            var result = await _dump.RefineValueScanAsync(
                SessionId, SelectedScanType,
                needsValue ? Value : null,
                SelectedScanType == ValueScanType.Between ? Value2 : null,
                effRound, effCase, PageSize, cts.Token);

            // Refine prunes the existing set (no re-scan), so the histogram is exact.
            ClassFilter.RebuildFromCounts(
                result.ClassHistogram.Select(c => (c.ClassName, c.Count)),
                result.ClassDistinct);
            await ApplyScanResultAsync(result.Total, result.Candidates);

            StatusText = $"Next Scan ({SelectedScanType}): {result.Total} surviving candidates " +
                         $"in {result.DurationMs} ms";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Next Scan cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Next Scan failed: {ex.Message}";
            _log.Error($"ValueSearch Refine failed", ex);
        }
        finally
        {
            IsScanning = false;
            if (ReferenceEquals(_scanCts, cts)) { _scanCts?.Dispose(); _scanCts = null; }
        }
    }

    [RelayCommand]
    private async Task NewScanAsync()
    {
        await EndSessionIfAnyAsync();
        Candidates = new ObservableCollection<ValueCandidate>();
        Total = 0;
        FilteredTotal = 0;
        // Reset the bound PICKER, not just the private key. Assigning `_sortKey` alone left
        // the combo still displaying the previous sort while the next scan ran in scan order,
        // and re-selecting the option the combo already shows raises no change notification —
        // so the user could not get that sort back without picking something else first.
        // (audit #5 AE9)
        _suppressSortReload = true;
        SelectedSortOption = SortOptions[0];   // "Scan order"
        SortDescending = false;
        _suppressSortReload = false;
        ApplyUiSort();   // re-syncs _sortKey/_sortDesc even when the picker was already at [0]
        // ClassFilter is reset by EndSessionIfAnyAsync above (single source of truth).
        UpdateWindowStatus();
        StatusText = "Session ended. Configure a new scan and click First Scan.";
        ErrorMessage = "";
    }

    /// <summary>Cancel an in-flight First/Next scan. The UI stops waiting
    /// immediately; the DLL self-terminates the scan at its deadline.</summary>
    [RelayCommand]
    private void CancelScan() => _scanCts?.Cancel();

    [RelayCommand]
    private void OpenInLiveWalker(ValueCandidate? candidate)
    {
        if (candidate == null) return;
        if (string.IsNullOrEmpty(candidate.InstanceAddr)) return;
        NavigateToInstance?.Invoke(candidate.InstanceAddr, candidate.FieldOffset, candidate.FieldName);
    }

    /// <summary>Open this hit's owning class in the Instance Finder tab,
    /// pre-filling the class name and running the search.</summary>
    [RelayCommand]
    private void OpenInInstanceFinder(ValueCandidate? candidate)
    {
        candidate ??= SelectedCandidate;
        if (candidate == null || string.IsNullOrEmpty(candidate.ClassName)) return;
        NavigateToInstanceFinder?.Invoke(candidate.ClassName);
    }

    /// <summary>Show this hit's owning object's related objects (components, GAS
    /// ASC → AttributeSets, Controller↔Pawn) in the Related tab.</summary>
    [RelayCommand]
    private void ShowRelatedObjects(ValueCandidate? candidate)
    {
        candidate ??= SelectedCandidate;
        if (candidate == null || string.IsNullOrEmpty(candidate.InstanceAddr)) return;
        NavigateToRelatedObjects?.Invoke(candidate.InstanceAddr);
    }

    // The reason "Locate in GWorld" can't run for `addr`, or null if it can. Lets
    // the handoffs report WHY a click did nothing instead of returning silently —
    // a "click does nothing, no message" no-op is impossible to diagnose otherwise.
    //
    // Intentionally does NOT gate on the C# IsGWorldAvailable flag: the DLL's
    // find_path_from_gworld is the source of truth for GWorld (it returns an
    // "invalid" / "no path" status when there's no live UWorld, which the locate
    // flow surfaces). Gating here on a stale/false IsGWorldAvailable was disabling
    // the button on games where GWorld was actually resolved (e.g. TQ2, proxy mode).
    private static string? GWorldLocateBlockReason(string? addr) =>
        string.IsNullOrEmpty(addr)
            ? "No owning-object address to locate for this match."
            : null;

    [RelayCommand]
    private void LocateCandidateInGWorld(ValueCandidate? candidate)
    {
        if (candidate == null) return;
        var reason = GWorldLocateBlockReason(candidate.InstanceAddr);
        if (reason != null) { StatusText = reason; return; }
        _log.Info($"Value Search Locate in GWorld -> {candidate.InstanceAddr} (off=0x{candidate.FieldOffset:X}, {candidate.FieldName})");
        LocateInGWorld?.Invoke(candidate.InstanceAddr, candidate.FieldOffset, candidate.FieldName);
    }

    [RelayCommand]
    private void LocateCandidateInGameEngine(ValueCandidate? candidate)
    {
        if (candidate == null) return;
        var reason = GWorldLocateBlockReason(candidate.InstanceAddr);
        if (reason != null) { StatusText = reason; return; }
        _log.Info($"Value Search Locate in GameEngine -> {candidate.InstanceAddr} (off=0x{candidate.FieldOffset:X}, {candidate.FieldName})");
        LocateInGameEngine?.Invoke(candidate.InstanceAddr, candidate.FieldOffset, candidate.FieldName);
    }

    [RelayCommand]
    private void CopyAddress(ValueCandidate? candidate)
    {
        if (candidate == null) return;
        if (string.IsNullOrEmpty(candidate.Addr)) return;
        RequestCopyText?.Invoke(candidate.Addr);
    }

    /// <summary>Hand this hit's (class, field) to the Class Pivot tab — the
    /// value-locator → pivot handoff. The hit already resolved both, so the user
    /// goes from "I see this value" straight to grouping its class by this field.</summary>
    [RelayCommand]
    private void PivotThis(ValueCandidate? candidate)
    {
        candidate ??= SelectedCandidate;
        if (candidate == null || string.IsNullOrEmpty(candidate.ClassName)) return;
        NavigateToPivot?.Invoke(candidate.ClassName, candidate.FieldName);
    }

    private async Task EndSessionIfAnyAsync()
    {
        if (SessionId == 0) return;
        try
        {
            await _dump.EndValueScanAsync(SessionId);
        }
        catch (Exception ex)
        {
            // Idempotent on the DLL side, but log defensively -- a
            // stale session that fails to end here will time out via
            // the DLL's 5-min idle expiry.
            _log.Warn($"ValueSearch End session {SessionId} failed: {ex.Message}");
        }
        finally
        {
            SessionId = 0;
            // The class-noise facets + exclusions belong to the retired session —
            // clear them so a fresh First Scan can't inherit a stale exclude_classes
            // (a class hidden for a different value would silently filter the new
            // result, sometimes with no visible row to untick). The subsequent
            // RebuildFromCounts repopulates from the new scan's histogram.
            ClassFilter.Reset();
        }
    }

    // ==================================================================
    // Group mode — Multiple values group scan (build 1276)
    //
    // A separate, parallel workflow that finds OBJECTS holding ALL of N values
    // (2..4) at distinct numeric-property offsets. Shares the IsScanning flag,
    // CancelScan, GameOnly/MaxResults inputs, and all four cross-tab handoff
    // events with single mode; everything else (inputs, session, results,
    // windowing) is its own state so the single-value path is untouched.
    // ==================================================================

    /// <summary>Single (default) vs Group input/results mode toggle.</summary>
    [ObservableProperty] private bool _isGroupMode;
    public bool IsSingleMode => !IsGroupMode;
    partial void OnIsGroupModeChanged(bool value) => OnPropertyChanged(nameof(IsSingleMode));

    /// <summary>The 2..4 editable input rows for a group scan.</summary>
    public ObservableCollection<GroupSlotInput> GroupInputs { get; } = new()
    {
        new GroupSlotInput(), new GroupSlotInput(),
    };

    public bool CanAddGroupRow    => GroupInputs.Count < 4;
    public bool CanRemoveGroupRow => GroupInputs.Count > 2;

    /// <summary>Per-slot width scope choices (P1): the multi-numeric meta types
    /// only. NumericNoByte (default) fans out over int16..double; NumericAll also
    /// includes 1-byte fields.</summary>
    public IReadOnlyList<ValueScanDataType> GroupDataTypeOptions { get; } = new[]
    {
        ValueScanDataType.NumericNoByte,
        ValueScanDataType.NumericAll,
    };

    /// <summary>Per-slot scan-type choices for group mode (P2). First-scan types
    /// (Exact / Bigger / Smaller) compare against the row's value; prev-value types
    /// (Changed / Unchanged / Increased / Decreased) compare against the previous
    /// round and need a session, so they're only valid on a refine — the value box
    /// hides for them. Between + substring predicates are intentionally excluded.</summary>
    public IReadOnlyList<ValueScanType> GroupScanTypeOptions { get; } = new[]
    {
        ValueScanType.Exact,
        ValueScanType.Bigger,
        ValueScanType.Smaller,
        ValueScanType.Between,
        ValueScanType.Changed,
        ValueScanType.Unchanged,
        ValueScanType.Increased,
        ValueScanType.Decreased,
    };

    /// <summary>The bound group-result rows (current server window). Each row is
    /// one object; its Slots expand in the master-detail grid.</summary>
    [ObservableProperty] private ObservableCollection<GroupCandidate> _groupCandidates = new();
    [ObservableProperty] private GroupCandidate? _selectedGroupCandidate;

    [ObservableProperty] private ulong _groupSessionId;
    public bool HasGroupSession => GroupSessionId != 0;
    partial void OnGroupSessionIdChanged(ulong value) => OnPropertyChanged(nameof(HasGroupSession));

    [ObservableProperty] private int _groupTotal;
    [ObservableProperty] private int _groupFilteredTotal;
    [ObservableProperty] private string _groupWindowStatus = "";

    public bool GroupHasMore => GroupCandidates.Count < GroupFilteredTotal;
    partial void OnGroupCandidatesChanged(ObservableCollection<GroupCandidate> value)
        => OnPropertyChanged(nameof(GroupHasMore));
    partial void OnGroupFilteredTotalChanged(int value)
        => OnPropertyChanged(nameof(GroupHasMore));

    [ObservableProperty] private string _groupFilterText = "";

    /// <summary>Per-session remembered filter keywords (LRU) for the group-mode
    /// filter box's AutoCompleteBox suggestions — see <see cref="KeywordSearchMemory"/>.
    /// Server-side + async filter, so the keyword is <c>Commit</c>ted from
    /// <see cref="LoadGroupWindowAsync"/> once the group result count lands (a debounced
    /// probe would race the pipe round-trip).</summary>
    private readonly KeywordSearchMemory _groupFilterMemory;
    public ObservableCollection<string> GroupFilterHistory => _groupFilterMemory.History;

    partial void OnGroupFilterTextChanged(string value) => _ = DebouncedGroupReloadAsync();

    /// <summary>Server-side sort picker for the group results (object-level rows).</summary>
    public IReadOnlyList<ValueSortOption> GroupSortOptions { get; } = new[]
    {
        new ValueSortOption("Scan order",  "scan"),
        new ValueSortOption("Class",       "class"),
        new ValueSortOption("Instance",    "instance"),
        new ValueSortOption("First value", "value"),
        new ValueSortOption("First offset","offset"),
    };
    [ObservableProperty] private ValueSortOption? _selectedGroupSortOption;
    [ObservableProperty] private bool _groupSortDescending;
    partial void OnSelectedGroupSortOptionChanged(ValueSortOption? value) => ApplyGroupUiSort();
    partial void OnGroupSortDescendingChanged(bool value) => ApplyGroupUiSort();

    private string _groupSortKey = "";
    private bool   _groupSortDesc;
    private System.Threading.CancellationTokenSource? _groupViewCts;
    private System.Threading.CancellationTokenSource? _groupFilterCts;

    [RelayCommand]
    private void AddGroupRow()
    {
        if (GroupInputs.Count >= 4) return;
        GroupInputs.Add(new GroupSlotInput { RoundMode = SelectedRoundingMode });
        OnPropertyChanged(nameof(CanAddGroupRow));
        OnPropertyChanged(nameof(CanRemoveGroupRow));
    }

    [RelayCommand]
    private void RemoveGroupRow()
    {
        if (GroupInputs.Count <= 2) return;
        GroupInputs.RemoveAt(GroupInputs.Count - 1);
        OnPropertyChanged(nameof(CanAddGroupRow));
        OnPropertyChanged(nameof(CanRemoveGroupRow));
    }

    private bool GroupValuesValid(bool firstScan, out string error)
    {
        error = "";
        if (GroupInputs.Count is < 2 or > 4) { error = "Group scan needs 2 to 4 values."; return false; }
        foreach (var g in GroupInputs)
        {
            bool prevValue = IsPrevValueScanType(g.ScanType);
            // Prev-value predicates need a baseline that only exists after a First
            // Scan — reject them up front so the user does First Scan (with a
            // targeted type) first, then refines with Increased/etc.
            if (firstScan && prevValue)
            {
                error = $"Scan type '{g.ScanType}' needs a previous scan — First Scan with Exact / Bigger / Smaller, then Next Scan with it.";
                return false;
            }
            // A targeted predicate needs a value; a prev-value one doesn't.
            if (!prevValue && string.IsNullOrWhiteSpace(g.Value))
            {
                error = "Every targeted group value is required.";
                return false;
            }
            // Between needs the upper bound too.
            if (g.ScanType == ValueScanType.Between && string.IsNullOrWhiteSpace(g.Value2))
            {
                error = "A Between row needs both a low and a high value.";
                return false;
            }
        }
        return true;
    }

    [RelayCommand]
    private async Task GroupFirstScanAsync()
    {
        if (IsScanning) return;
        if (!GroupValuesValid(firstScan: true, out var err)) { ErrorMessage = err; return; }

        var cts = _scanCts = new System.Threading.CancellationTokenSource();
        try
        {
            IsScanning = true;
            ErrorMessage = "";
            StatusText = $"Group scanning for {GroupInputs.Count} values...";

            await EndGroupSessionIfAnyAsync();

            var result = await _dump.BeginGroupScanAsync(
                GroupInputs.ToList(), GameOnly, MaxResults, DeepScan, CrossObjectScan,
                NativeCScan, NewestFirst, PageSize, ScanTimeoutSeconds * 1000,
                PreFilterNoise, SelectedRoundingMode, GroupPerSlotCap, cts.Token);

            GroupSessionId = result.SessionId;
            GroupClassFilter.RebuildFromCounts(
                result.ClassHistogram.Select(c => (c.ClassName, c.Count)),
                result.ClassDistinct, countsPartial: result.DeadlineHit);
            await ApplyGroupScanResultAsync(result.Total, result.Candidates);

            var summary = $"Group First Scan: {result.Total} matching objects in {result.DurationMs} ms " +
                          $"(scanned {result.ScannedObjects} objects, {result.ScannedClasses} classes)";
            if (result.DeadlineHit)
                summary += $"  ⚠ truncated ({ScanTimeoutSeconds}s deadline / result cap) — raise the Timeout slider or refine";
            StatusText = summary;
        }
        catch (OperationCanceledException) { StatusText = "Group First Scan cancelled."; }
        catch (Exception ex)
        {
            ErrorMessage = $"Group First Scan failed: {ex.Message}";
            _log.Error("ValueSearch group first scan failed", ex);
        }
        finally
        {
            IsScanning = false;
            if (ReferenceEquals(_scanCts, cts)) { _scanCts?.Dispose(); _scanCts = null; }
        }
    }

    [RelayCommand]
    private async Task GroupNextScanAsync()
    {
        if (IsScanning || !HasGroupSession) return;
        if (!GroupValuesValid(firstScan: false, out var err)) { ErrorMessage = err; return; }

        var cts = _scanCts = new System.Threading.CancellationTokenSource();
        try
        {
            IsScanning = true;
            ErrorMessage = "";
            StatusText = "Refining group scan...";

            var result = await _dump.RefineGroupScanAsync(GroupSessionId, GroupInputs.ToList(), PageSize, SelectedRoundingMode, cts.Token);

            GroupClassFilter.RebuildFromCounts(
                result.ClassHistogram.Select(c => (c.ClassName, c.Count)),
                result.ClassDistinct);
            await ApplyGroupScanResultAsync(result.Total, result.Candidates);
            StatusText = $"Group Next Scan: {result.Total} surviving objects in {result.DurationMs} ms";
        }
        catch (OperationCanceledException) { StatusText = "Group Next Scan cancelled."; }
        catch (Exception ex)
        {
            ErrorMessage = $"Group Next Scan failed: {ex.Message}";
            _log.Error("ValueSearch group refine failed", ex);
        }
        finally
        {
            IsScanning = false;
            if (ReferenceEquals(_scanCts, cts)) { _scanCts?.Dispose(); _scanCts = null; }
        }
    }

    [RelayCommand]
    private async Task GroupNewScanAsync()
    {
        await EndGroupSessionIfAnyAsync();
        GroupCandidates = new ObservableCollection<GroupCandidate>();
        GroupTotal = 0;
        GroupFilteredTotal = 0;
        // Same picker reset as NewScanAsync — the group mode had the identical defect and
        // the finding named only the single-scan side. (audit #5 AE9)
        _suppressGroupSortReload = true;
        SelectedGroupSortOption = GroupSortOptions[0];   // "Scan order"
        GroupSortDescending = false;
        _suppressGroupSortReload = false;
        ApplyGroupUiSort();
        // GroupClassFilter is reset by EndGroupSessionIfAnyAsync above.
        UpdateGroupWindowStatus();
        StatusText = "Group session ended. Configure values and click Group First Scan.";
        ErrorMessage = "";
    }

    private bool IsGroupDefaultView =>
        string.IsNullOrEmpty(GroupFilterText)
        && (string.IsNullOrEmpty(_groupSortKey) || _groupSortKey == "scan")
        && !_groupSortDesc
        && !GroupClassFilter.AnyExcluded;

    private async Task ApplyGroupScanResultAsync(int total, IList<GroupCandidate> inlineFirstPage)
    {
        GroupTotal = total;
        if (IsGroupDefaultView)
        {
            GroupFilteredTotal = total;
            GroupCandidates = new ObservableCollection<GroupCandidate>(inlineFirstPage);
            UpdateGroupWindowStatus();
        }
        else
        {
            await LoadGroupWindowAsync(reset: true);
        }
    }

    private async Task LoadGroupWindowAsync(bool reset)
    {
        if (!HasGroupSession) return;
        _groupViewCts?.Cancel();
        var cts = _groupViewCts = new System.Threading.CancellationTokenSource();
        try
        {
            int offset = reset ? 0 : GroupCandidates.Count;
            var w = await _dump.QueryGroupCandidatesAsync(
                GroupSessionId, offset, PageSize,
                string.IsNullOrEmpty(GroupFilterText) ? null : GroupFilterText,
                string.IsNullOrEmpty(_groupSortKey) ? null : _groupSortKey,
                _groupSortDesc, GroupClassFilter.ExcludedClasses, cts.Token);
            GroupTotal = w.Total;
            GroupFilteredTotal = w.FilteredTotal;
            if (reset)
                GroupCandidates = new ObservableCollection<GroupCandidate>(w.Candidates);
            else
                foreach (var c in w.Candidates) GroupCandidates.Add(c);
            OnPropertyChanged(nameof(GroupHasMore));
            UpdateGroupWindowStatus();
            // Filter keyword-memory: commit the settled keyword once its server count
            // has landed (page-0 reload), never on a timer that could race the reload.
            if (reset) _groupFilterMemory.Commit();
        }
        catch (OperationCanceledException) { /* superseded */ }
        catch (Exception ex)
        {
            ErrorMessage = $"Group query failed: {ex.Message}";
            _log.Error("ValueSearch query_group_candidates failed", ex);
        }
        finally
        {
            if (ReferenceEquals(_groupViewCts, cts)) { _groupViewCts?.Dispose(); _groupViewCts = null; }
        }
    }

    private async Task DebouncedGroupReloadAsync()
    {
        if (!HasGroupSession) return;
        _groupFilterCts?.Cancel();
        var cts = _groupFilterCts = new System.Threading.CancellationTokenSource();
        try
        {
            await Task.Delay(250, cts.Token);
            await LoadGroupWindowAsync(reset: true);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_groupFilterCts, cts)) { _groupFilterCts?.Dispose(); _groupFilterCts = null; }
        }
    }

    private void ApplyGroupUiSort()
    {
        _groupSortKey  = SelectedGroupSortOption?.Key ?? "";
        _groupSortDesc = GroupSortDescending;
        if (_suppressGroupSortReload) return;
        if (HasGroupSession) _ = LoadGroupWindowAsync(reset: true);
    }

    [RelayCommand]
    private Task GroupLoadMoreAsync() => LoadGroupWindowAsync(reset: false);

    private void UpdateGroupWindowStatus()
    {
        if (GroupTotal == 0) { GroupWindowStatus = ""; return; }
        string filt = (GroupFilteredTotal != GroupTotal) ? $" (filtered from {GroupTotal})" : "";
        GroupWindowStatus = GroupHasMore
            ? $"Showing {GroupCandidates.Count} of {GroupFilteredTotal}{filt} — Load More for the rest"
            : $"Showing all {GroupFilteredTotal}{filt}";
    }

    // --- Group handoffs. Slot leaves reuse the SAME four events as single mode
    // (each GroupSlotMatch carries its owning InstanceAddr + ClassName so the
    // handoff is self-contained); the master row hands its class to Instance
    // Finder. ---

    [RelayCommand]
    private void OpenGroupSlotInLiveWalker(GroupSlotMatch? slot)
    {
        // HandoffAddr is the leaf's direct owner — the owned sub-object for a
        // cross-object leaf (P4), else the actor — so Live Walker opens the object
        // that actually holds the field.
        if (slot == null || string.IsNullOrEmpty(slot.HandoffAddr)) return;
        NavigateToInstance?.Invoke(slot.HandoffAddr, slot.FieldOffset, slot.FieldName);
    }

    [RelayCommand]
    private void LocateGroupSlotInGWorld(GroupSlotMatch? slot)
    {
        if (slot == null) return;
        var reason = GWorldLocateBlockReason(slot.HandoffAddr);
        if (reason != null) { StatusText = reason; return; }
        _log.Info($"Group Locate in GWorld -> {slot.HandoffAddr} (off=0x{slot.FieldOffset:X}, {slot.FieldName})");
        LocateInGWorld?.Invoke(slot.HandoffAddr, slot.FieldOffset, slot.FieldName);
    }

    [RelayCommand]
    private void LocateGroupSlotInGameEngine(GroupSlotMatch? slot)
    {
        if (slot == null) return;
        var reason = GWorldLocateBlockReason(slot.HandoffAddr);
        if (reason != null) { StatusText = reason; return; }
        _log.Info($"Group Locate in GameEngine -> {slot.HandoffAddr} (off=0x{slot.FieldOffset:X}, {slot.FieldName})");
        LocateInGameEngine?.Invoke(slot.HandoffAddr, slot.FieldOffset, slot.FieldName);
    }

    [RelayCommand]
    private void CopyGroupSlotAddress(GroupSlotMatch? slot)
    {
        if (slot == null || string.IsNullOrEmpty(slot.Addr)) return;
        RequestCopyText?.Invoke(slot.Addr);
    }

    [RelayCommand]
    private void PivotGroupSlot(GroupSlotMatch? slot)
    {
        // PivotClassName is the leaf's direct owner class — the owned sub-object's
        // class for a cross-object leaf (P4 inc 2), else the candidate actor — so the
        // Pivot lands on the class that actually declares the field.
        if (slot == null || string.IsNullOrEmpty(slot.PivotClassName) || string.IsNullOrEmpty(slot.FieldName)) return;
        NavigateToPivot?.Invoke(slot.PivotClassName, slot.FieldName);
    }

    /// <summary>Fill a slot's <see cref="GroupSlotMatch.Leaves"/> with EVERY field it
    /// matched, by name.
    ///
    /// A row can only display one assignment. On the DumperTest sample a
    /// <c>Changed</c> + <c>Unchanged</c> refine kept {Health.CurrentValue, TickCount}
    /// for slot 0 and 36 leaves including FrozenInt for slot 1 — two equally valid
    /// pairs, one row — and the row's Health pair was read as "TickCount/FrozenInt
    /// were missed". Everything else existed only as raw integers in
    /// <c>matched_offsets</c>, and an integer cannot say that 1308 is FrozenInt.
    ///
    /// Each returned leaf is a full <see cref="GroupSlotMatch"/>, so the per-slot
    /// handoffs (Live Walker / Copy / Pivot / Locate) act on it unchanged.
    ///
    /// <b>Toggles.</b> A second press collapses the list rather than re-fetching it —
    /// with two slots open the details can run to dozens of rows, and the button that
    /// opened them is the obvious thing to press to close them. Collapsing is purely
    /// local (clear the collection); re-expanding re-queries, so the values are always
    /// current rather than a stale snapshot from the first press.</summary>
    [RelayCommand]
    private async Task LoadGroupSlotLeavesAsync(GroupSlotMatch? slot)
    {
        if (slot == null || !HasGroupSession || string.IsNullOrEmpty(slot.InstanceAddr)) return;
        if (slot.Leaves.Count > 0)
        {
            // Collapse. Bump the token too: an in-flight fetch for THIS slot must not
            // land after the user has closed the list and re-open it.
            _groupLeafLoadId++;
            slot.Leaves.Clear();
            StatusText = $"Slot {slot.SlotIndex + 1}: field list collapsed.";
            return;
        }
        // Two quick clicks (or two slots at once) race: without a generation token
        // the loser's Clear()+Add() can land after the winner's and leave one slot
        // showing another's fields.
        var gen = ++_groupLeafLoadId;
        try
        {
            var leaves = await _dump.QueryGroupSlotLeavesAsync(
                GroupSessionId, slot, slot.InstanceAddr, slot.ClassName);
            if (gen != _groupLeafLoadId) return;
            slot.Leaves.Clear();
            foreach (var leaf in leaves) slot.Leaves.Add(leaf);
            StatusText = $"Slot {slot.SlotIndex + 1}: {slot.Leaves.Count} matching field(s) in " +
                         $"{slot.ClassName} — the row displays one of them. " +
                         $"Press \"All fields\" again to collapse.";
        }
        catch (Exception ex)
        {
            if (gen != _groupLeafLoadId) return;
            ErrorMessage = $"Listing slot fields failed: {ex.Message}";
            _log.Error("ValueSearch load group slot leaves failed", ex);
        }
    }

    /// <summary>Generation token for <see cref="LoadGroupSlotLeavesAsync"/> — only the
    /// newest request may write its results.</summary>
    private int _groupLeafLoadId;

    [RelayCommand]
    private void OpenGroupInInstanceFinder(GroupCandidate? candidate)
    {
        candidate ??= SelectedGroupCandidate;
        if (candidate == null || string.IsNullOrEmpty(candidate.ClassName)) return;
        NavigateToInstanceFinder?.Invoke(candidate.ClassName);
    }

    private async Task EndGroupSessionIfAnyAsync()
    {
        if (GroupSessionId == 0) return;
        try
        {
            await _dump.EndGroupScanAsync(GroupSessionId);
        }
        catch (Exception ex)
        {
            _log.Warn($"ValueSearch End group session {GroupSessionId} failed: {ex.Message}");
        }
        finally
        {
            GroupSessionId = 0;
            GroupClassFilter.Reset();   // see EndSessionIfAnyAsync — stale-exclusion guard
        }
    }
}
