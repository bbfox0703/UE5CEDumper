using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Instance Finder panel.
/// Search for instances by class name, view live values, export CE XML.
/// </summary>
public partial class InstanceFinderViewModel : ViewModelBase, IDisposable
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService _platform;
    private bool _disposed;

    private EngineState? _engineState;

    // Monotonic guard for the field walk: a fast instance selection must not be
    // clobbered by a slower walk_instance for a previously-selected instance
    // (which would render A's fields under B and desync the CE XML export).
    private int _fieldLoadId;

    // Monotonic guard for the class search: a re-run triggered by a class-noise
    // toggle (or a fresh manual search) supersedes any in-flight search, so a
    // slow response can't clobber _allInstances after a newer one already landed.
    private int _searchGen;

    // True once a class/name search has run (so a class-noise toggle re-RUNS the
    // server scan with the new exclusion set). False on the reverse-address path,
    // where a toggle on leftover facets must NOT trigger a class search.
    private bool _hasActiveClassSearch;

    // The class/name query that produced the current results — replayed verbatim by
    // a class-noise re-run (the toggle changed the exclusion set, not the query, and
    // the user may have edited the boxes without pressing Search).
    private string _lastClassQuery = "";
    private string _lastNameQuery = "";

    // Cancels an in-flight class-noise re-run when another toggle lands fast
    // (debounce-by-cancel — discrete ticks, so no timer needed here).
    private CancellationTokenSource? _reRunCts;

    // Debounce timer for the temporary keyword filter box (200 ms; mirrors Object Tree).
    private System.Threading.Timer? _filterDebounce;

    // Address format
    [ObservableProperty] private int _selectedAddressFormatIndex;
    private AddressFormat AddrFormat => (AddressFormat)SelectedAddressFormatIndex;

    /// <summary>Whether CE XML export should collapse pointer/array nodes.</summary>
    public bool CollapsePointerNodes { get; set; }

    /// <summary>Max array element count for inline reading (2^N, default 128).</summary>
    private int _arrayLimit = Constants.DefaultArrayLimit;
    public int ArrayLimit
    {
        get => _arrayLimit;
        set
        {
            if (_arrayLimit == value) return;
            _arrayLimit = value;
            // Auto-refresh selected instance with new limit
            if (SelectedInstance != null)
                _ = LoadInstanceFieldsAsync(SelectedInstance);
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
            // Auto-refresh selected instance with new limit
            if (SelectedInstance != null)
                _ = LoadInstanceFieldsAsync(SelectedInstance);
        }
    }

    /// <summary>Max CE DropDownList entries (2^N, default 512). Used during CE XML export.</summary>
    public int DropDownLimit { get; set; } = Constants.DefaultDropDownLimit;

    /// <summary>CE String leaf display length (2^N chars; 16..4096, default 256).
    /// Seeded from the toolbar master; passed to CE XML export.</summary>
    public int CeStringLength { get; set; } = Constants.DefaultCeStringLength;

    // --- Class name search ---
    [ObservableProperty] private string _searchClassName = "";
    /// <summary>Optional object-name filter (case-insensitive substring), ANDed
    /// with the class query. Resolved DLL-side (filter-then-cap) so it doesn't
    /// miss matches the way a client-side filter on capped class results would —
    /// but the returned list is still capped at <c>limit</c>; truncation is
    /// surfaced in the status text. Either box may be empty: class-only,
    /// name-only, or both.</summary>
    [ObservableProperty] private string _searchObjectName = "";
    [ObservableProperty] private bool _exactMatch;

    /// <summary>Opt-in: scan GObjects from the high (newest-allocated) end so the
    /// most-recently-spawned instances survive the result cap — use to catch a
    /// just-spawned enemy. Off (default) keeps the lowest indices (CDO /
    /// class-default / earliest instances — good for finding a Blueprint's
    /// template/defaults).</summary>
    [ObservableProperty] private bool _newestFirst;

    /// <summary>Configurable result cap (the server returns at most this many rows;
    /// the GObjects scan itself is always exhaustive). Raised from the old hard 500
    /// to 5000 so a wanted instance is far less likely to fall off the end, and
    /// server-side class-noise exclusion frees cap slots on top. Clamped 100..50000
    /// (UI NumericUpDown + a DLL-side clamp).</summary>
    [ObservableProperty] private int _instanceSearchCap = Constants.DefaultInstanceSearchCap;

    /// <summary>Temporary client-side keyword filter over the CURRENTLY-RETURNED
    /// rows (whitespace-separated terms ANDed across Name / Class / Address, like
    /// the Object Tree box). Separate from the class-noise <see cref="ClassFilter"/>:
    /// this only narrows what's already on screen (cap-blind, instant), it does not
    /// re-query the server. Debounced 200 ms.</summary>
    [ObservableProperty] private string _instanceFilterText = "";

    [ObservableProperty] private ObservableCollection<InstanceResult> _instances = new();
    [ObservableProperty] private bool _isXrefBatchRunning;
    [ObservableProperty] private InstanceResult? _selectedInstance;
    [ObservableProperty] private ObservableCollection<LiveFieldValue> _fields = new();
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isLoadingFields;
    [ObservableProperty] private bool _hasInstances;
    [ObservableProperty] private bool _hasFields;
    [ObservableProperty] private string _ceXmlOutput = "";
    [ObservableProperty] private bool _showCeXml;
    [ObservableProperty] private string _statusText = "";

    // --- Address-to-Instance reverse lookup ---
    [ObservableProperty] private string _lookupAddress = "";
    [ObservableProperty] private string _lookupStatusText = "";
    [ObservableProperty] private bool _isLookingUp;

    // Container-aware results: addresses that fall inside an ArrayProperty
    // heap buffer rather than within a UObject's PropertiesSize.
    [ObservableProperty] private ObservableCollection<ContainerMatch> _containerMatches = new();
    [ObservableProperty] private bool _hasContainerMatches;

    /// <summary>
    /// Event raised when user wants to navigate to an address in the Live Walker.
    /// </summary>
    public event Action<string>? NavigateToLiveWalker;

    /// <summary>
    /// Event raised when the user wants to locate a found instance within the
    /// GWorld object graph (forward path search). Payload = instance address.
    /// </summary>
    public event Action<string>? LocateInGWorld;

    /// <summary>Engine-rooted counterpart of <see cref="LocateInGWorld"/> (path search
    /// rooted at the live UGameEngine). Payload = instance address.</summary>
    public event Action<string>? LocateInGameEngine;

    /// <summary>
    /// Event raised to locate the OWNER of a container match within the GWorld
    /// graph (the looked-up address fell inside a container element). The whole
    /// <see cref="ContainerMatch"/> is passed so the walker can drill the full
    /// (possibly multi-level) container chain to land ON the value — including
    /// deeply-nested values found by the recursive deep scan.
    /// </summary>
    public event Action<ContainerMatch>? LocateContainerInGWorld;

    /// <summary>Engine-rooted counterpart of <see cref="LocateContainerInGWorld"/>
    /// (path search rooted at the live UGameEngine).</summary>
    public event Action<ContainerMatch>? LocateContainerInGameEngine;

    /// <summary>Event raised to show the selected instance's related objects
    /// (components, GAS ASC → AttributeSets, Controller↔Pawn) in the Related
    /// tab. Payload = instance address.</summary>
    public event Action<string>? NavigateToRelatedObjects;

    /// <summary>True when GWorld is available — gates the "Locate in GWorld" button.</summary>

    /// <summary>Per-container element probe cap for the recursive deep container
    /// scan (the find_by_address fallback that locates values in deeply-nested,
    /// separately-allocated containers). Set from the top Options flyout.</summary>
    [ObservableProperty] private int _deepScanElemCap = 256;

    /// <summary>Full (capped) class-search result. <see cref="Instances"/> is a
    /// class-noise-filtered projection of this; kept so the filter can re-project
    /// without re-searching. Only the class-search path populates it — the
    /// reverse-address lookup path clears it (its single result isn't a class set).</summary>
    private List<InstanceResult> _allInstances = new();

    /// <summary>Client-side class-noise filter: hides ticked classes (UI widgets,
    /// sound, system components) from <see cref="Instances"/>. Ticking re-runs
    /// <see cref="ApplyInstanceFilter"/> over <see cref="_allInstances"/>.</summary>
    public ClassFacetFilter ClassFilter { get; }

    /// <summary>Live "N hidden by class filter" note (empty when nothing hidden).
    /// Recomputed on every search + every class-filter toggle so the user can tell
    /// an empty grid caused by a stale exclusion from a genuine miss.</summary>
    [ObservableProperty] private string _classFilterNote = "";

    /// <summary>Per-session remembered keywords (LRU) surfaced as the temporary
    /// keyword box's AutoCompleteBox suggestions — see <see cref="KeywordSearchMemory"/>.
    /// A keyword is remembered only once typing settles AND it yielded rows.</summary>
    private readonly KeywordSearchMemory _filterMemory;
    public ObservableCollection<string> InstanceFilterHistory => _filterMemory.History;

    public InstanceFinderViewModel(IDumpService dump, ILoggingService log, IPlatformService platform)
    {
        _dump = dump;
        _log = log;
        _platform = platform;
        _filterMemory = new KeywordSearchMemory(() => (InstanceFilterText, Instances.Count > 0));
        ClassFilter = new ClassFacetFilter(OnClassFilterChanged)
        {
            AutoDetectProvider = async names =>
                (await _dump.DetectNoiseClassesAsync(names))
                    .Select(n => (n.ClassName, n.IsNoise, n.Reason)).ToList(),
        };
    }

    /// <summary>Drop the instance list + walked fields so a reconnect never shows
    /// instances (and addresses) from the previous game or offers a stale field walk
    /// (audit X5). Bumps the search/field tickets so an in-flight response can't
    /// repaint after this. Client-side only. Null <see cref="SelectedInstance"/>
    /// FIRST — a non-null selection's changed-handler triggers a pipe field walk.</summary>
    public void ClearOnDisconnect()
    {
        _reRunCts?.Cancel();
        try { _xrefBatchCts?.Cancel(); } catch { /* already disposed */ }
        _searchGen++;
        _fieldLoadId++;
        _hasActiveClassSearch = false;
        _lastClassQuery = "";
        _lastNameQuery = "";
        SelectedInstance = null;   // detach before clearing (null branch is a no-op)
        Instances.Clear();
        _allInstances.Clear();
        ClassFilter.Reset();
        ClassFilterNote = "";
        ContainerMatches.Clear();
        Fields.Clear();
        HasFields = false;
        HasContainerMatches = false;
        StatusText = "";
        LookupStatusText = "";
    }

    /// <summary>Class-noise picker changed. When a class search is active the
    /// exclusion is server-authoritative — re-RUN the scan with the new
    /// <see cref="ClassFacetFilter.ExcludedClasses"/> so an instance previously
    /// buried past the cap can surface (and an unticked class returns). We also
    /// re-project the current rows immediately for instant feedback while the
    /// re-run is in flight. With no active search (reverse-address path), it's a
    /// plain client re-projection.</summary>
    private void OnClassFilterChanged()
    {
        ApplyInstanceFilter();   // instant: hide/show within the already-returned rows
        if (!_hasActiveClassSearch) return;

        // Supersede any in-flight re-run, then replay the SAME query server-side
        // with the updated exclusion set.
        _reRunCts?.Cancel();
        _reRunCts = new CancellationTokenSource();
        var ct = _reRunCts.Token;
        _ = ReRunWithExclusionsAsync(ct);
    }

    private async Task ReRunWithExclusionsAsync(CancellationToken ct)
    {
        try
        {
            await RunSearchCoreAsync(_lastClassQuery, _lastNameQuery, ClassFilter.ExcludedClasses, ct);
        }
        catch (OperationCanceledException)
        {
            // A newer toggle (or a fresh search) superseded this re-run — drop it.
        }
    }

    /// <summary>Re-project <see cref="_allInstances"/> into the bound
    /// <see cref="Instances"/> collection, hiding classes the user ticked in the
    /// class-noise filter. Runs after a class search and on every filter toggle.
    /// Preserves the current selection (and its loaded field grid) when the
    /// selected instance survives the filter — so toggling a noise class doesn't
    /// wipe what the user is inspecting.</summary>
    private void ApplyInstanceFilter()
    {
        var prev = SelectedInstance;
        // Two client-side filters layered: the class-noise picker (server-authoritative
        // after a re-run; this leg is belt-and-suspenders + instant pre-re-run feedback)
        // AND the temporary keyword box (whitespace = AND across Name/Class/Address).
        var terms = ObjectTreeFilter.SplitTerms(InstanceFilterText);
        UiCollection.Reset(
            Instances,
            _allInstances.Where(i => !ClassFilter.IsExcluded(i.ClassName)
                                     && (terms.Length == 0
                                         || ObjectTreeFilter.MatchesAllTerms(terms, i.Name, i.ClassName, i.Address))),
            () => SelectedInstance = null);
        HasInstances = Instances.Count > 0;
        // After a server-side exclude re-run the excluded rows are no longer in
        // _allInstances, so "hidden" counts keyword-filtered rows (plus, briefly, the
        // just-ticked class before the re-run lands). Either way it's "hidden from the
        // returned set" — keep the label generic so it never mis-attributes.
        int hidden = _allInstances.Count - Instances.Count;
        ClassFilterNote = hidden > 0 ? $"{hidden} hidden by filter" : "";
        // Restore selection (and its field walk) if it wasn't filtered out.
        if (prev != null && Instances.Contains(prev))
            SelectedInstance = prev;
    }

    /// <summary>Debounce the temporary keyword box (200 ms) then re-project the
    /// returned rows — purely client-side, no server round-trip. Mirrors the Object
    /// Tree filter so typing doesn't re-filter on every keystroke.</summary>
    partial void OnInstanceFilterTextChanged(string value)
    {
        if (_disposed) return;
        _filterDebounce?.Dispose();
        _filterDebounce = new System.Threading.Timer(
            _ => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyInstanceFilter),
            null, 200, Timeout.Infinite);
        // Remember the keyword once typing settles (probe reads Instances.Count after
        // the 200 ms re-project has landed). Longer debounce than the filter itself.
        _filterMemory.Schedule(value);
    }

    /// <summary>Dispose the keyword debounce timer and cancel any in-flight class
    /// re-run when the VM is torn down — the Timer/CTS lambdas otherwise root the VM
    /// and can fire after teardown (mirrors ObjectTreeViewModel.Dispose).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _filterDebounce?.Dispose();
        _filterDebounce = null;

        _filterMemory.Dispose();

        try { _reRunCts?.Cancel(); } catch { /* already disposed */ }
        _reRunCts?.Dispose();
        _reRunCts = null;

        try { _xrefBatchCts?.Cancel(); } catch { /* already disposed */ }
        _xrefBatchCts?.Dispose();
        _xrefBatchCts = null;

        GC.SuppressFinalize(this);
    }

    public void SetEngineState(EngineState state)
    {
        _engineState = state;
    }

    /// <summary>Cross-tab handoff entry point (the per-row "inst" buttons on
    /// Property Search / Value Search / Interesting Funcs+Props / Classes /
    /// Related). Runs a class-name search and clears any stale Object-name
    /// filter so the handoff lists instances of the class itself, not ANDed
    /// with a leftover name the user typed earlier.</summary>
    public async Task SearchForClassAsync(string className)
    {
        SearchObjectName = "";
        SearchClassName = className;
        if (SearchCommand.CanExecute(null))
            await SearchCommand.ExecuteAsync(null);
    }

    /// <summary>Cross-tab handoff that pre-fills BOTH the class field AND the
    /// object-name filter, then runs the search (the Object Tree right-click
    /// "Find Instances (Type + Name)"). Unlike <see cref="SearchForClassAsync"/>
    /// this keeps the supplied object name rather than clearing it, so the result
    /// is narrowed to that specific named instance of the class.</summary>
    public async Task SearchForClassAndNameAsync(string className, string objectName)
    {
        SearchClassName = className;
        SearchObjectName = objectName ?? "";
        if (SearchCommand.CanExecute(null))
            await SearchCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var className = SearchClassName?.Trim() ?? "";
        var nameFilter = SearchObjectName?.Trim() ?? "";
        // Need at least one criterion — both empty is a no-op (mirrors the DLL guard).
        if (className.Length == 0 && nameFilter.Length == 0) return;

        // A fresh manual query: drop any server-side exclusions left from a PREVIOUS
        // search (now that exclusion persists server-side, a class hidden for "Pawn"
        // would otherwise silently filter "Inventory" — possibly with no visible row
        // to untick). Reset() does NOT fire onChanged, so this won't re-run here.
        _reRunCts?.Cancel();          // supersede an in-flight class-noise re-run
        ClassFilter.Reset();
        _hasActiveClassSearch = true;
        _lastClassQuery = className;
        _lastNameQuery = nameFilter;

        await RunSearchCoreAsync(className, nameFilter, ClassFilter.ExcludedClasses);
    }

    /// <summary>Core class/name scan, shared by the manual search and the
    /// class-noise re-run. <paramref name="excludeClasses"/> is sent server-side so
    /// excluded classes never consume a result-cap slot; the response's full-pool
    /// histogram drives the picker. A monotonic <see cref="_searchGen"/> guard drops
    /// a stale response when a newer search/re-run has already landed.</summary>
    private async Task RunSearchCoreAsync(string className, string nameFilter,
        IReadOnlyList<string> excludeClasses, CancellationToken ct = default)
    {
        int gen = ++_searchGen;
        bool reRun = excludeClasses.Count > 0;
        try
        {
            ClearError();
            IsSearching = true;
            StatusText = reRun ? "Re-searching..." : "Searching...";
            ShowCeXml = false;

            var result = await _dump.FindInstancesAsync(
                className, ExactMatch, limit: InstanceSearchCap, newestFirst: NewestFirst,
                nameFilter: nameFilter, excludeClasses: excludeClasses, ct: ct);
            if (gen != _searchGen) return;   // superseded by a newer search/re-run

            // Keep the full (post-exclude) result so the keyword box can re-project
            // without re-searching. The picker is driven by the SERVER histogram —
            // tallied over the full matched pool PRE-exclude — so an excluded class
            // (or one whose instances all sit past the cap) still appears and can be
            // unticked. RebuildFromCounts preserves the excluded set and does NOT
            // fire onChanged (no re-run loop). The histogram is over the full scan,
            // so it's exact — never "partial".
            _allInstances = new List<InstanceResult>(result.Instances);
            ClassFilter.RebuildFromCounts(
                result.ClassHistogram.Select(c => (c.ClassName, c.Count)),
                result.ClassDistinct, countsPartial: false);
            ApplyInstanceFilter();

            int found = _allInstances.Count;
            // truncated now means "more NON-EXCLUDED matches exist past the cap" — so
            // point at the two levers that recover them: exclude noise, or raise Max.
            var capNote = result.Truncated
                ? $" — ⚠ capped at {found} of max {InstanceSearchCap}; exclude noise classes, raise Max, or narrow"
                : "";
            if (result.Scanned > 0)
            {
                var pct = result.NonNull > 0 ? 100.0 * result.Named / result.NonNull : 0;
                StatusText = $"Found {found} instances (scanned {result.Scanned:N0}, non-null {result.NonNull:N0}, named {result.Named:N0} ({pct:F1}%)){capNote}";
            }
            else
            {
                StatusText = $"Found {found} instances{capNote}";
            }
            _log.Info($"FindInstances: class='{className}' name='{nameFilter}' exclude={excludeClasses.Count} -> {found} results (scanned={result.Scanned}, nonNull={result.NonNull}, named={result.Named}, distinct={result.ClassDistinct})");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // OUR re-run was superseded by a newer toggle / fresh search — let the
            // caller (ReRunWithExclusionsAsync) swallow it; not an error. The manual
            // path passes ct=default, so this filter never matches there.
            throw;
        }
        catch (Exception ex)
        {
            if (gen != _searchGen) return;   // stale failure — don't clobber newer state
            SetError(ex);
            StatusText = "Search failed";
            _log.Error($"FindInstances failed for class='{className}' name='{nameFilter}'", ex);
        }
        finally
        {
            if (gen == _searchGen) IsSearching = false;   // only the latest op owns the flag
        }
    }

    [RelayCommand]
    private async Task LookupAddressAsync()
    {
        if (string.IsNullOrWhiteSpace(LookupAddress)) return;

        // Reverse-address lookup is not a class set: supersede any in-flight class
        // search / class-noise re-run (cancel + bump the gen guard so a late response
        // can't overwrite the single lookup result) and stop class-noise toggles from
        // re-running a class search over the now-stale leftover facets.
        _reRunCts?.Cancel();
        _searchGen++;
        _hasActiveClassSearch = false;

        try
        {
            ClearError();
            IsLookingUp = true;
            LookupStatusText = "Looking up...";
            ShowCeXml = false;

            // Reject malformed input up-front. Prior behavior surfaced
            // "No UObject found at this address" for "0xajsd;jald" — misleading,
            // because the DLL silently parsed it to 0 (noexcept StrToAddr) and
            // searched for a UObject at 0 instead.
            if (!AddressHelper.TryNormalizeAddress(LookupAddress, _engineState?.ModuleBase, out var addrStr))
            {
                LookupStatusText = "Invalid address — expected hex (e.g. 0x7FF... or module.exe+RVA)";
                SelectedInstance = null;   // detach before clearing the bound collection
                Instances.Clear();
                _allInstances.Clear();
                ClassFilter.Reset();       // the lookup result isn't a class set
                ClassFilterNote = "";
                ContainerMatches.Clear();
                Fields.Clear();
                HasFields = false;
                HasContainerMatches = false;
                return;
            }

            var result = await _dump.FindByAddressAsync(addrStr, DeepScanElemCap);

            SelectedInstance = null;   // detach before clearing the bound collection
            Instances.Clear();
            _allInstances.Clear();
            ClassFilter.Reset();       // the lookup result isn't a class set
            ClassFilterNote = "";
            Fields.Clear();
            HasFields = false;
            ContainerMatches.Clear();

            // Container matches: address falls inside a UObject's TArray heap buffer.
            // The DLL always runs this scan when scan_containers=true, so we can
            // surface them even when the standard UObject containment check
            // already produced a match (the container path is more specific).
            foreach (var cm in result.ContainerMatches)
                ContainerMatches.Add(cm);
            HasContainerMatches = ContainerMatches.Count > 0;

            // Build a "[scanned X/Y in Zms]" suffix so the user can tell a
            // clean miss from a deadline-truncated scan — important when
            // testing on big games (FF7 Rebirth ~430K objects).
            string scanSuffix = "";
            if (result.ContainerScan is { } cs && cs.ObjectsTotal > 0)
            {
                if (cs.DeadlineHit)
                    scanSuffix = $"  [scanned {cs.ObjectsScanned}/{cs.ObjectsTotal} in {cs.DurationMs}ms — DEADLINE HIT, retry to continue]";
                else
                    scanSuffix = $"  [scanned {cs.ObjectsScanned}/{cs.ObjectsTotal} in {cs.DurationMs}ms]";
            }

            if (result.Found)
            {
                var instance = new InstanceResult
                {
                    Address = result.Address,
                    Index = result.Index,
                    Name = result.Name,
                    ClassName = result.ClassName,
                    OuterAddr = result.OuterAddr,
                };
                Instances.Add(instance);
                HasInstances = true;

                // "nearest" means the address is BEYOND the UObject's
                // PropertiesSize — we are NOT inside it (typically raw heap /
                // native allocation, or a container buffer the value lives in).
                // Don't auto-walk it: presenting its fields implies the value
                // lives there, which is misleading. Surface it as a clickable
                // hint and warn instead. ("backward" is a real subobject the DLL
                // validated, so auto-walking it is genuinely useful — keep it.)
                bool lowConfidence = result.MatchKind == "nearest";
                if (!lowConfidence)
                    SelectedInstance = instance;  // Auto-select to trigger field loading

                // Be honest about confidence — "nearest" / "backward" mean addr
                // is BEYOND the UObject's PropertiesSize, often misleading
                // (especially for heap-allocated container data).
                var matchInfo = result.MatchKind switch
                {
                    "exact"    => "Exact UObject match",
                    "contains" => $"Inside {result.Name} (offset +0x{result.OffsetFromBase:X})",
                    "backward" => $"Past {result.Name} (offset +0x{result.OffsetFromBase:X}) — backward scan",
                    "nearest"  => $"⚠ Not inside any UObject — this value is likely raw heap / native data. Nearest is {result.Name} at +0x{result.OffsetFromBase:X} (click the row to inspect it anyway).",
                    _          => $"Match: {result.Name} (offset +0x{result.OffsetFromBase:X})",
                };
                if (HasContainerMatches)
                    matchInfo += $" — found in {ContainerMatches.Count} container(s) below";
                LookupStatusText = matchInfo + scanSuffix;
                _log.Info($"FindByAddress: '{addrStr}' -> {matchInfo}{scanSuffix}");
            }
            else if (HasContainerMatches)
            {
                HasInstances = false;
                LookupStatusText = $"Inside {ContainerMatches.Count} container(s) — see list below" + scanSuffix;
                _log.Info($"FindByAddress: '{addrStr}' -> {ContainerMatches.Count} container matches only{scanSuffix}");
            }
            else
            {
                HasInstances = false;
                LookupStatusText = "No UObject found at this address" + scanSuffix;
                _log.Info($"FindByAddress: '{addrStr}' -> not found{scanSuffix}");
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            LookupStatusText = "Lookup failed";
            _log.Error($"FindByAddress failed for '{LookupAddress}'", ex);
        }
        finally
        {
            IsLookingUp = false;
        }
    }

    [RelayCommand]
    private void OpenContainerOwner(ContainerMatch? match)
    {
        if (match == null || string.IsNullOrEmpty(match.OwnerAddress)) return;
        // Live Walker doesn't yet auto-drill into the array element; opening
        // the owner UObject lets the user click into the field manually.
        // Pass a hint string in status so they know what to look for.
        LookupStatusText = $"Opened {match.DisplayPath} — drill into '{match.FieldName}' in Live Walker";
        NavigateToLiveWalker?.Invoke(match.OwnerAddress);
    }

    partial void OnSelectedInstanceChanged(InstanceResult? value)
    {
        if (value != null)
        {
            _ = LoadInstanceFieldsAsync(value);
        }
        else
        {
            // Supersede any in-flight field walk AND clear the loading flag here:
            // the superseded walk bails in its finally without touching the flag
            // (id != _fieldLoadId), and no successor load runs to reset it.
            _fieldLoadId++;
            IsLoadingFields = false;
            Fields.Clear();
            HasFields = false;
        }
    }

    private async Task LoadInstanceFieldsAsync(InstanceResult instance)
    {
        int id = ++_fieldLoadId;
        try
        {
            ClearError();
            IsLoadingFields = true;
            ShowCeXml = false;

            var result = await _dump.WalkInstanceAsync(instance.Address, arrayLimit: ArrayLimit, previewLimit: PreviewLimit);
            if (id != _fieldLoadId) return;   // a newer selection / limit change superseded us

            // Compute base address for FieldAddress calculation
            ulong baseAddr = 0;
            try
            {
                if (!string.IsNullOrEmpty(result.Address))
                    baseAddr = Convert.ToUInt64(result.Address.Replace("0x", "").Replace("0X", ""), 16);
            }
            catch { /* ignore parse failures */ }

            Fields.Clear();
            foreach (var f in result.Fields)
            {
                if (baseAddr != 0)
                    f.FieldAddress = $"0x{baseAddr + (ulong)f.Offset:X}";
                Fields.Add(f);
            }

            HasFields = Fields.Count > 0;
        }
        catch (Exception ex)
        {
            if (id != _fieldLoadId) return;   // stale failure — don't clobber newer state
            SetError(ex);
            _log.Error($"Failed to walk instance at {instance.Address}", ex);
        }
        finally
        {
            if (id == _fieldLoadId)   // only the latest walk owns the loading flag
                IsLoadingFields = false;
        }
    }

    [RelayCommand]
    private async Task ExportCeXmlAsync()
    {
        if (SelectedInstance == null) return;

        try
        {
            ClearError();
            IsLoadingFields = true;

            // Pre-resolve StructProperty inner fields via DLL
            StatusText = "Resolving struct fields...";
            // lean: this resolve feeds GenerateInstanceXml (a CE XML export), which
            // reads structure only — see the LEAN contract in Fern.cpp / §10.6.
            var resolvedStructs = await CeXmlExportService.ResolveStructFieldsAsync(
                _dump, new List<LiveFieldValue>(Fields), arrayLimit: ArrayLimit, lean: true);

            // Compute root address in user-selected format
            var rootAddress = AddressHelper.FormatAddress(
                SelectedInstance.Address, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);

            StatusText = "Generating CE XML...";
            var xml = CeXmlExportService.GenerateInstanceXml(
                rootAddress, SelectedInstance.Name, SelectedInstance.ClassName,
                new List<LiveFieldValue>(Fields), resolvedStructs,
                collapsePointerNodes: CollapsePointerNodes,
                maxDropDownEntries: DropDownLimit,
                ceStringLength: CeStringLength);

            await _platform.CopyToClipboardAsync(xml);
            StatusText = "";
            _log.Info($"CE XML copied to clipboard for instance {SelectedInstance.Name} ({resolvedStructs.Count} structs resolved)");
        }
        catch (Exception ex)
        {
            StatusText = "";
            SetError(ex);
            _log.Error("Failed to export CE XML", ex);
        }
        finally
        {
            IsLoadingFields = false;
        }
    }

    [RelayCommand]
    private async Task CopyFieldAddressAsync(LiveFieldValue? field)
    {
        if (field == null || SelectedInstance == null) return;
        if (string.IsNullOrEmpty(SelectedInstance.Address)) return;

        try
        {
            var instanceAddr = Convert.ToUInt64(SelectedInstance.Address.Replace("0x", "").Replace("0X", ""), 16);
            var absAddr = instanceAddr + (ulong)field.Offset;
            var hexAddr = $"0x{absAddr:X}";

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
    private async Task CopyInstanceAddressAsync(InstanceResult? instance)
    {
        if (instance == null || string.IsNullOrEmpty(instance.Address)) return;

        try
        {
            var formatted = AddressHelper.FormatAddress(
                instance.Address, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
            await _platform.CopyToClipboardAsync(formatted);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy instance address for {instance.Name}", ex);
        }
    }

    /// <summary>Copy an instance's object name to the clipboard (mirrors Live
    /// Walker's per-object Name copy).</summary>
    [RelayCommand]
    private async Task CopyInstanceNameAsync(InstanceResult? instance)
    {
        if (instance == null || string.IsNullOrEmpty(instance.Name)) return;
        try { await _platform.CopyToClipboardAsync(instance.Name); }
        catch (Exception ex) { _log.Error("Failed to copy instance name", ex); }
    }

    /// <summary>Copy an instance's class name to the clipboard (mirrors Live
    /// Walker's per-object Class copy).</summary>
    [RelayCommand]
    private async Task CopyInstanceClassNameAsync(InstanceResult? instance)
    {
        if (instance == null || string.IsNullOrEmpty(instance.ClassName)) return;
        try { await _platform.CopyToClipboardAsync(instance.ClassName); }
        catch (Exception ex) { _log.Error("Failed to copy instance class name", ex); }
    }

    /// <summary>"Find Func": which UFunctions take this instance's class as a
    /// parameter/return value (find_functions_by_class — reflection, so native
    /// functions are included too, unlike the per-field bytecode xref). Opens
    /// the shared xref dialog in class mode.</summary>
    [RelayCommand]
    private async Task FindFunctionsForInstanceAsync(InstanceResult? instance)
    {
        if (instance == null || string.IsNullOrEmpty(instance.ClassAddress)) return;
        await Views.PropertyXrefDialog.ShowForClassAsync(
            instance.ClassName, instance.ClassAddress, _dump, _platform);
    }

    // Batch Find Func over the currently-filtered instance rows (or selected).
    // Many rows share a class, so dedup by ClassAddress within the run — each
    // distinct class is a full game-wide sweep done once. Skip already-scanned
    // (XrefInfo persists across the keyword filter).
    private CancellationTokenSource? _xrefBatchCts;

    [RelayCommand]
    private async Task BatchFindFuncsAsync(IList<InstanceResult>? selected)
    {
        var targets = ((selected != null && selected.Count > 0) ? selected : (IList<InstanceResult>)Instances).ToList();
        if (targets.Count == 0) { StatusText = "No instances to scan."; return; }

        // Cost is driven by DISTINCT not-yet-done classes, not raw row count.
        int distinctClasses = targets
            .Where(i => !string.IsNullOrEmpty(i.ClassAddress) && string.IsNullOrEmpty(i.XrefInfo))
            .Select(i => i.ClassAddress).Distinct().Count();
        if (distinctClasses > Constants.XrefBatchWarnThreshold)
        {
            var ok = await Views.ConfirmDialog.ShowAsync(
                "Batch: find functions",
                $"Scan {distinctClasses} distinct classes (across {targets.Count} rows)? Each class "
              + "runs a FULL game-wide reflection sweep; rows sharing a class reuse one scan. "
              + "Tip: narrow the filter first.",
                confirmText: "Run");
            if (!ok) return;
        }

        var oldCts = _xrefBatchCts;          // dispose the prior run's CTS (L14)
        _xrefBatchCts = new CancellationTokenSource();
        oldCts?.Cancel();
        oldCts?.Dispose();
        var ct = _xrefBatchCts.Token;
        IsXrefBatchRunning = true;
        var classCache = new Dictionary<string, string>();
        int rows = 0, classesScanned = 0, reused = 0;
        try
        {
            foreach (var inst in targets)
            {
                ct.ThrowIfCancellationRequested();
                rows++;
                if (!string.IsNullOrEmpty(inst.XrefInfo)) { reused++; continue; }
                if (string.IsNullOrEmpty(inst.ClassAddress)) { inst.XrefInfo = "—"; continue; }
                if (classCache.TryGetValue(inst.ClassAddress, out var hit)) { inst.XrefInfo = hit; reused++; continue; }
                try
                {
                    var res = await _dump.FindFunctionsByClassAsync(inst.ClassAddress, true, 200, ct);
                    var summary = XrefFormat.FunctionsSummary(res.Xrefs);
                    classCache[inst.ClassAddress] = summary;
                    inst.XrefInfo = summary;
                    classesScanned++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    inst.XrefInfo = "—";
                    _log.Error($"Batch Find Func failed for {inst.ClassName}", ex);
                }
                if ((rows & 0x7) == 0)
                    StatusText = $"Find Func: {classesScanned} classes scanned ({rows}/{targets.Count} rows)…";
            }
            StatusText = $"Find Func done: {classesScanned} classes scanned across {targets.Count} rows"
                       + (reused > 0 ? $" ({reused} reused/cached)." : ".");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            StatusText = $"Find Func cancelled ({classesScanned} classes scanned).";
        }
        catch (Exception ex)   // incl. a bare disconnect-OCE (token NOT cancelled) (L14)
        {
            _log.Error("Batch Find Func failed", ex);
            StatusText = $"Find Func failed ({classesScanned} classes scanned).";
        }
        finally { IsXrefBatchRunning = false; }
    }

    /// <summary>Cancel an in-flight batch Find Func run.</summary>
    [RelayCommand]
    private void CancelXrefBatch() => _xrefBatchCts?.Cancel();

    /// <summary>Copy a container-match row's owning UObject address to the
    /// clipboard (mirrors the per-instance copy + Value Search's Copy Address).</summary>
    [RelayCommand]
    private async Task CopyContainerAddressAsync(ContainerMatch? match)
    {
        if (match == null || string.IsNullOrEmpty(match.OwnerAddress)) return;

        try
        {
            var formatted = AddressHelper.FormatAddress(
                match.OwnerAddress, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
            await _platform.CopyToClipboardAsync(formatted);
            LookupStatusText = $"Copied {formatted}  ({match.OwnerClassName})";
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy container owner address for {match.OwnerClassName}", ex);
        }
    }

    [RelayCommand]
    private async Task GenerateCeAAScriptAsync(InstanceResult? instance)
    {
        if (instance == null || string.IsNullOrEmpty(instance.Address)) return;

        try
        {
            var symbolName = instance.ClassName.Replace(" ", "_").Replace("-", "_");

            var formattedAddr = AddressHelper.FormatAddress(
                instance.Address, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);

            var xml = CeXmlExportService.GenerateRegisterSymbolXml(symbolName, formattedAddr);

            await _platform.CopyToClipboardAsync(xml);
            _log.Info($"CE AA script copied to clipboard for {instance.ClassName}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to generate CE AA script", ex);
        }
    }

    [RelayCommand]
    private void OpenInLiveWalker()
    {
        if (SelectedInstance == null) return;
        NavigateToLiveWalker?.Invoke(SelectedInstance.Address);
    }

    [RelayCommand]
    private void LocateSelectedInGWorld()
    {
        if (SelectedInstance == null) return;
        LocateInGWorld?.Invoke(SelectedInstance.Address);
    }

    // Engine-rooted counterpart — not gated on IsGWorldAvailable (engine
    // availability is independent of GWorld; the DLL reports no_engine via the
    // Live Walker banner).
    [RelayCommand]
    private void LocateSelectedInGameEngine()
    {
        if (SelectedInstance == null) return;
        LocateInGameEngine?.Invoke(SelectedInstance.Address);
    }

    [RelayCommand]
    private void ShowRelatedObjects()
    {
        if (SelectedInstance == null) return;
        NavigateToRelatedObjects?.Invoke(SelectedInstance.Address);
    }

    /// <summary>Locate a container match's OWNER within the GWorld graph — the
    /// looked-up address is a value inside a container element, so reach the
    /// owning object (via the shortest GWorld path) and auto-drill into the
    /// element so the user lands next to the value.</summary>
    [RelayCommand]
    private void LocateContainerOwnerInGWorld(ContainerMatch? match)
    {
        if (match == null || string.IsNullOrEmpty(match.OwnerAddress)) return;
        // Hand off the whole match — the Live Walker reaches the owner via the
        // GWorld path and drills the full container chain (outermost → element →
        // … → deepest value), which covers both 1-level struct-element values
        // and deeply-nested values from the recursive deep scan.
        LocateContainerInGWorld?.Invoke(match);
    }

    // Engine-rooted counterpart — not gated on IsGWorldAvailable (see above).
    [RelayCommand]
    private void LocateContainerOwnerInGameEngine(ContainerMatch? match)
    {
        if (match == null || string.IsNullOrEmpty(match.OwnerAddress)) return;
        LocateContainerInGameEngine?.Invoke(match);
    }
}
