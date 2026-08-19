using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Object Tree panel.
/// Loads ALL objects into an in-memory cache on "Load" click.
/// Client-side filter searches the full cache; UI displays at most
/// <see cref="Constants.ObjectTreeMaxDisplay"/> items via virtualized ListBox.
/// </summary>
public partial class ObjectTreeViewModel : ViewModelBase, IDisposable
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService _platform;
    private bool _disposed;

    // Engine state for address formatting
    private EngineState? _engineState;

    // All loaded nodes — full cache, unfiltered
    private readonly List<UObjectNode> _allNodes = new();

    // Cancellation for the current load operation
    private CancellationTokenSource? _loadCts;
    // Monotonic load generation: a search/reload supersedes an in-flight full load
    // so its paging loop stops appending into _allNodes after the cache was
    // replaced (otherwise search hits and full-list pages interleave).
    private int _loadGen;

    // Debounce timer for FilterText changes (200 ms)
    private System.Threading.Timer? _filterDebounce;

    /// <summary>Per-session remembered filter keywords (LRU) surfaced as the bottom
    /// text-filter box's AutoCompleteBox suggestions — see <see cref="KeywordSearchMemory"/>.
    /// (The top SearchText box uses the curated <see cref="SearchSuggestions"/> instead.)</summary>
    private readonly KeywordSearchMemory _filterMemory;
    public ObservableCollection<string> FilterHistory => _filterMemory.History;

    [ObservableProperty] private ObservableCollection<UObjectNode> _filteredNodes = new();
    [ObservableProperty] private UObjectNode? _selectedNode;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private int _selectedClassFilterIndex;
    /// <summary>When on, the filter hides reflection/type-layer rows (UClass, UFunction,
    /// UScriptStruct, UEnum, UPackage, and UE4's <c>FooProperty</c> family) so a global
    /// keyword search shows only live gameplay instances. Applied inside
    /// <see cref="ApplyFilter"/> over the ENTIRE loaded pool (<see cref="_allNodes"/>),
    /// not just the displayed page — see <see cref="Helpers.ReflectionMetaClassifier"/>.</summary>
    [ObservableProperty] private bool _instancesOnly;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _objectCount;
    [ObservableProperty] private string _displayCount = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private int _selectedAddressFormatIndex;

    /// <summary>
    /// Collapses the left Object Tree to a thin strip so the right-hand panels
    /// get the full window width. View state only (not persisted): the panel's
    /// ◀ button toggles it on, a slim re-expand strip with ▶ restores it, and
    /// MainWindow resizes the grid column / hides the splitter in response.
    /// </summary>
    [ObservableProperty] private bool _isCollapsed;

    /// <summary>Toggle the collapsed state of the Object Tree column.</summary>
    [RelayCommand]
    private void ToggleCollapse() => IsCollapsed = !IsCollapsed;

    /// <summary>Class type filter options. Index 0 = show all, others = exact ClassName match.</summary>
    public string[] ClassFilterOptions { get; } =
    [
        "All",
        "Class",
        "Package",
        "Function",
        "ScriptStruct",
        "Enum",
        "BlueprintGeneratedClass",
        "WidgetBlueprint",
        "UserDefinedStruct",
        "Level",
    ];

    /// <summary>
    /// Common search suggestions for UE class / object names. Curated to
    /// hit the everyday targets when reverse-engineering a game:
    /// player/character framework, components, gameplay systems, UMG/UI,
    /// world/level, GAS, and save/inventory. Stored as the unprefixed UE
    /// introspection name (no A/U prefixes — UClass::GetName drops them).
    /// </summary>
    public string[] SearchSuggestions { get; } =
    [
        // GAS
        "AbilitySystemComponent",
        "AttributeSet",
        "GameplayAbility",
        "GameplayEffect",
        // Components
        "ActorComponent",
        "AnimInstance",
        "AudioComponent",
        "CapsuleComponent",
        "CharacterMovementComponent",
        "InventoryComponent",
        "SceneComponent",
        "SkeletalMeshComponent",
        "StaticMeshComponent",
        // Player / character framework
        "Character",
        "LocalPlayer",
        "Pawn",
        "PlayerCameraManager",
        "PlayerController",
        "PlayerInput",
        "PlayerState",
        // Game framework
        "GameInstance",
        "GameMode",
        "GameState",
        "GameUserSettings",
        "HUD",
        "SaveGame",
        // World / level
        "Level",
        "World",
        "WorldSettings",
        // UMG / UI
        "UserWidget",
        "Widget",
    ];

    /// <summary>Fired when the selected node changes, for cross-VM communication.</summary>
    public event Action<UObjectNode?>? SelectionChanged;

    /// <summary>Raised by the right-click "Find Instances (Type)" menu item:
    /// pre-fill the Instances tab's class field with the node's ClassName and
    /// run the search. Payload = class name. Saves the copy-type /
    /// paste-into-Instances / Search round-trip.</summary>
    public event Action<string>? NavigateToInstanceFinder;

    /// <summary>Raised by the right-click "Find Instances (Type + Name)" menu
    /// item: pre-fill the Instances tab's class AND object-name fields, then run
    /// the search (the two are ANDed server-side). Payload = (className, objectName).</summary>
    public event Action<string, string>? NavigateToInstanceFinderWithName;

    /// <summary>Raised by the right-click "Open in Live Walker" menu item: drill into the
    /// selected object's live instance (walk its fields). Payload = object address. Mirrors
    /// the InstanceFinder / ValueSearch → Live Walker handoff.</summary>
    public event Action<string>? NavigateToLiveWalker;

    /// <summary>Raised by "Locate in GWorld": shortest pointer chain from GWorld to the
    /// selected object (forward BFS). Payload = object address.</summary>
    public event Action<string>? LocateInGWorld;

    /// <summary>Engine-rooted companion of <see cref="LocateInGWorld"/> (GEngine-rooted
    /// path search). Payload = object address.</summary>
    public event Action<string>? LocateInGameEngine;

    /// <summary>Raised by "Show Related Objects": load the selected object's owned graph
    /// (class / outer / components / ASC / AttributeSet). Payload = object address.</summary>
    public event Action<string>? NavigateToRelatedObjects;

    partial void OnSelectedNodeChanged(UObjectNode? value)
    {
        SelectionChanged?.Invoke(value);
    }

    partial void OnFilterTextChanged(string value)
    {
        if (_disposed) return;
        // Debounce filter to avoid per-keystroke scanning of large caches (486K+ items).
        // 200ms delay allows typing to complete before filtering starts.
        _filterDebounce?.Dispose();
        _filterDebounce = new System.Threading.Timer(
            _ => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyFilter),
            null, 200, Timeout.Infinite);
        // Remember the settled keyword once its own 700ms quiet period elapses; the
        // probe reads FilteredNodes.Count (populated by the 200ms ApplyFilter above).
        _filterMemory.Schedule(value);
    }

    /// <summary>
    /// Audit fix #16: dispose the debounce Timer and any in-flight load CTS
    /// when the VM is destroyed. Without this, a Timer callback fires after
    /// the VM is GC-eligible (Timer holds a strong root via the lambda),
    /// keeping the whole VM alive and potentially racing with finalization.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _filterDebounce?.Dispose();
        _filterDebounce = null;

        _filterMemory.Dispose();

        try { _loadCts?.Cancel(); } catch { /* already disposed */ }
        _loadCts?.Dispose();
        _loadCts = null;

        GC.SuppressFinalize(this);
    }

    partial void OnSelectedClassFilterIndexChanged(int value)
    {
        ApplyFilter();
    }

    partial void OnInstancesOnlyChanged(bool value)
    {
        ApplyFilter();
    }

    public ObjectTreeViewModel(IDumpService dump, ILoggingService log, IPlatformService platform)
    {
        _dump = dump;
        _log = log;
        _platform = platform;
        _filterMemory = new KeywordSearchMemory(() => (FilterText, FilteredNodes.Count > 0));
    }

    public void SetEngineState(EngineState state)
    {
        _engineState = state;
    }

    [RelayCommand]
    private async Task CopyClassNameAsync(UObjectNode? node)
    {
        if (node == null || string.IsNullOrEmpty(node.ClassName)) return;
        await _platform.CopyToClipboardAsync(node.ClassName);
    }

    [RelayCommand]
    private async Task CopyObjectNameAsync(UObjectNode? node)
    {
        if (node == null || string.IsNullOrEmpty(node.Name)) return;
        await _platform.CopyToClipboardAsync(node.Name);
    }

    [RelayCommand]
    private async Task CopyAddressAsync(UObjectNode? node)
    {
        if (node == null || string.IsNullOrEmpty(node.Address)) return;
        var formatted = AddressHelper.FormatAddress(
            node.Address, _engineState?.ModuleName, _engineState?.ModuleBase,
            (AddressFormat)SelectedAddressFormatIndex);
        await _platform.CopyToClipboardAsync(formatted);
    }

    /// <summary>Right-click "Find Instances (Type)": hand the node's ClassName to
    /// the Instances tab and auto-run the search (no clipboard round-trip).</summary>
    [RelayCommand]
    private void FindInstancesByType(UObjectNode? node)
    {
        if (node == null || string.IsNullOrEmpty(node.ClassName)) return;
        NavigateToInstanceFinder?.Invoke(node.ClassName);
    }

    /// <summary>Right-click "Find Instances (Type + Name)": hand both the node's
    /// ClassName and its object Name to the Instances tab (ANDed) and auto-run
    /// the search — narrows to the specific named instance(s) of that class.</summary>
    [RelayCommand]
    private void FindInstancesByTypeAndName(UObjectNode? node)
    {
        if (node == null || string.IsNullOrEmpty(node.ClassName)) return;
        NavigateToInstanceFinderWithName?.Invoke(node.ClassName, node.Name);
    }

    /// <summary>Right-click "Open in Live Walker": drill into THIS exact object (walk its
    /// live fields) — the per-hit handoff a global instance search needs, which the class-
    /// oriented "Find Instances" does not give. Any UObject works; a class row walks the
    /// UClass itself.</summary>
    [RelayCommand]
    private void OpenInLiveWalker(UObjectNode? node)
    {
        if (node == null || string.IsNullOrEmpty(node.Address)) return;
        NavigateToLiveWalker?.Invoke(node.Address);
    }

    /// <summary>Right-click "Locate in GWorld": shortest GWorld→object pointer chain
    /// (forward BFS). Meaningful for live instances; a class row is usually not reachable
    /// from the world graph (Live Walker reports that).</summary>
    [RelayCommand]
    private void LocateSelectedInGWorld(UObjectNode? node)
    {
        if (node == null || string.IsNullOrEmpty(node.Address)) return;
        LocateInGWorld?.Invoke(node.Address);
    }

    /// <summary>Right-click "Locate in GameEngine": engine-rooted companion of Locate in GWorld.</summary>
    [RelayCommand]
    private void LocateSelectedInGameEngine(UObjectNode? node)
    {
        if (node == null || string.IsNullOrEmpty(node.Address)) return;
        LocateInGameEngine?.Invoke(node.Address);
    }

    /// <summary>Right-click "Show Related Objects": load THIS object's owned graph
    /// (class / outer / Controller↔Pawn / components / ASC / AttributeSet).</summary>
    [RelayCommand]
    private void ShowRelatedObjects(UObjectNode? node)
    {
        if (node == null || string.IsNullOrEmpty(node.Address)) return;
        NavigateToRelatedObjects?.Invoke(node.Address);
    }

    /// <summary>
    /// Load ALL objects from the DLL into the in-memory cache.
    /// Uses large batch size (2000) for fast loading. Shows progress.
    /// Supports cancellation via <see cref="CancelLoadCommand"/>.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        // Cancel any previous load in progress
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        int gen = ++_loadGen;

        try
        {
            ClearError();
            IsLoading = true;
            StatusText = "Loading...";
            _filterMemory.Flush();   // commit a just-typed keyword before clearing the box (L17)
            _allNodes.Clear();
            FilterText = "";
            SelectedClassFilterIndex = 0;

            int offset = 0;
            int total = 0;

            do
            {
                ct.ThrowIfCancellationRequested();

                var result = await _dump.GetObjectListAsync(offset, Constants.ObjectTreePageSize, ct);
                if (gen != _loadGen) return;   // superseded by a newer load/search — stop appending
                total = result.Total;
                ObjectCount = total;

                foreach (var obj in result.Objects)
                {
                    _allNodes.Add(obj);
                }

                // Advance by scanned count (not returned count) to avoid stalling
                // when many objects in a range are unnamed/null
                offset += result.Scanned;

                // Update progress display
                StatusText = $"Loading... {_allNodes.Count:N0} / {total:N0}";

            } while (offset < total);

            ApplyFilter();
            var loadPct = total > 0 ? 100.0 * _allNodes.Count / total : 0;
            StatusText = $"Loaded {_allNodes.Count:N0} named objects (of {total:N0} total, {loadPct:F1}%)";
            _log.Info($"Loaded {_allNodes.Count:N0} named objects of {total:N0} total ({loadPct:F1}%)");
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load/search → leave the cache for that op to own.
            if (gen != _loadGen) return;
            // User cancelled — keep whatever was loaded so far
            ApplyFilter();
            StatusText = $"Loaded {_allNodes.Count:N0} of {ObjectCount:N0} (cancelled)";
            _log.Info($"Load cancelled at {_allNodes.Count:N0} of {ObjectCount:N0} objects");
        }
        catch (Exception ex)
        {
            // On error, keep whatever was loaded so far
            ApplyFilter();
            SetError(ex);
            StatusText = "Load failed";
            _log.Error("Failed to load objects", ex);
        }
        finally
        {
            if (gen == _loadGen) IsLoading = false;   // only the latest op owns the flag
        }
    }

    /// <summary>Cancel an ongoing load operation.</summary>
    [RelayCommand]
    private void CancelLoad()
    {
        _loadCts?.Cancel();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            // Empty search: reload the full list
            await LoadAsync();
            return;
        }

        // Supersede any in-flight full load before we replace _allNodes, so its
        // paging loop stops appending (otherwise search hits + full-list pages mix).
        //
        // Then REPLACE the source. The old code cancelled `_loadCts` and never made a
        // new one, so for the whole search the panel's only enabled control was a
        // Cancel button wired to an already-cancelled token — it could not do anything
        // (audit #5 V9). `search_objects` is a full GObjects sweep on the DLL side, so
        // this is the one call in this panel a user most wants to be able to abandon.
        // Shape copied from LoadAsync directly above, generation counter included, so
        // the two commands supersede each other symmetrically.
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        int gen = ++_loadGen;

        try
        {
            ClearError();
            IsLoading = true;
            StatusText = "Searching...";
            _filterMemory.Flush();   // commit a just-typed keyword before clearing the box (L17)
            FilterText = "";
            SelectedClassFilterIndex = 0;

            // Server-side keyword search across ALL objects. space=AND (each term hits
            // object name OR class name) + the Instances-only gate are applied server-side,
            // matching the bottom filter, so the top Search is a true global instance search.
            var result = await _dump.SearchObjectsAsync(SearchText, Constants.ObjectTreeSearchCap, InstancesOnly, ct);
            // Superseded by a newer load/search while the sweep was in flight — that op
            // owns _allNodes now, so do not overwrite what it has already put there.
            if (gen != _loadGen) return;
            _allNodes.Clear();
            ObjectCount = result.Total;

            foreach (var obj in result.Objects)
            {
                _allNodes.Add(obj);
            }

            ApplyFilter();

            if (FilteredNodes.Count > 0)
            {
                SelectedNode = FilteredNodes[0];
            }

            // On a hit cap, tell the user matches were dropped and how to see them all —
            // the top Search caps server-side, but Reload + the bottom filter scans the
            // whole loaded pool uncapped.
            StatusText = result.Truncated
                ? $"Found {result.Total:N0}+ results (capped — narrow the search, or Reload + filter for all)"
                : $"Found {result.Total:N0} results";
            _log.Info($"Search '{SearchText}' (instancesOnly={InstancesOnly}): {result.Total:N0} results{(result.Truncated ? " (capped)" : "")}");
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load/search -> that op owns the cache and the flag.
            if (gen != _loadGen) return;
            // User pressed Cancel. The previous result set is still on screen and is
            // still valid, so say so rather than blanking it (audit #5 V9).
            StatusText = "Search cancelled";
            _log.Info($"Search cancelled for '{SearchText}'");
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Search failed";
            _log.Error($"Search failed for '{SearchText}'", ex);
        }
        finally
        {
            if (gen == _loadGen) IsLoading = false;   // only the latest op owns the flag
        }
    }

    /// <summary>
    /// Apply client-side class + text filter on the full in-memory cache.
    /// Caps the displayed items at <see cref="Constants.ObjectTreeMaxDisplay"/>
    /// to keep the UI responsive (Avalonia ListBox virtualization handles rendering).
    /// </summary>
    /// <summary>Drop all process-scoped rows so a reconnect never shows objects
    /// (and addresses) from the previous game (audit X5). Client-side only — the
    /// pipe is already gone; does not touch persisted state (there is none here).</summary>
    public void ClearOnDisconnect()
    {
        _loadCts?.Cancel();
        _loadGen++;               // supersede any in-flight paging load
        SelectedNode = null;      // detach ClassStruct (null branch is a no-op)
        _allNodes.Clear();
        FilteredNodes.Clear();
        ObjectCount = 0;
        DisplayCount = "";
        StatusText = "";
    }

    private void ApplyFilter()
    {
        // Detach the bound selection before clearing: Avalonia's selection model
        // otherwise nulls SelectedNode DURING the Clear()'s CollectionChanged event,
        // firing the cross-VM SelectionChanged cascade reentrantly. SearchAsync
        // re-selects FilteredNodes[0] explicitly after this returns.
        SelectedNode = null;
        FilteredNodes.Clear();
        // Multi-term text filter: whitespace-separated terms are ANDed (each term
        // must hit Name / ClassName / Address), so "BP_ char" narrows to objects
        // matching both — the two-layer filter without a second box.
        var terms = ObjectTreeFilter.SplitTerms(FilterText);
        var classFilter = SelectedClassFilterIndex > 0
            ? ClassFilterOptions[SelectedClassFilterIndex] : null;

        int matchCount = 0;

        foreach (var node in _allNodes)
        {
            // Instances-only filter: drop reflection/type-layer rows (UClass, UFunction,
            // UScriptStruct, UEnum, UPackage, UE4 FooProperty) so only live gameplay
            // instances remain. Runs here — over the FULL _allNodes cache — so it filters
            // the whole loaded pool, not the display-capped page.
            if (InstancesOnly && !ReflectionMetaClassifier.IsLiveInstanceRow(node.ClassName))
                continue;

            // Class type filter (exact match on ClassName)
            if (classFilter != null &&
                !node.ClassName.Equals(classFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            // Text filter: every term must match Name, ClassName, or Address
            if (terms.Length > 0 &&
                !ObjectTreeFilter.MatchesAllTerms(terms, node.Name, node.ClassName, node.Address))
                continue;

            matchCount++;

            // Cap the UI display collection to prevent excessive rendering overhead
            if (FilteredNodes.Count < Constants.ObjectTreeMaxDisplay)
                FilteredNodes.Add(node);
        }

        bool hasAnyFilter = terms.Length > 0 || classFilter != null || InstancesOnly;
        var totalSuffix = ObjectCount > 0 && ObjectCount != _allNodes.Count
            ? $" / {ObjectCount:N0} total ({100.0 * _allNodes.Count / ObjectCount:F1}%)"
            : "";

        if (hasAnyFilter)
        {
            DisplayCount = matchCount > Constants.ObjectTreeMaxDisplay
                ? $"Filtered: {matchCount:N0} matches (showing {Constants.ObjectTreeMaxDisplay:N0})"
                : $"Filtered: {matchCount:N0} / {_allNodes.Count:N0}";
        }
        else
        {
            DisplayCount = matchCount > Constants.ObjectTreeMaxDisplay
                ? $"Objects: {_allNodes.Count:N0}{totalSuffix} (showing {Constants.ObjectTreeMaxDisplay:N0})"
                : $"Objects: {_allNodes.Count:N0}{totalSuffix}";
        }
    }
}
