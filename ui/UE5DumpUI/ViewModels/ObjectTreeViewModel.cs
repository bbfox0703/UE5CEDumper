using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Object Tree panel.
/// Loads ALL objects into an in-memory cache on "Load" click.
/// Client-side filter searches the full cache; UI displays at most
/// <see cref="Constants.ObjectTreeMaxDisplay"/> items via virtualized ListBox.
/// </summary>
public partial class ObjectTreeViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService _platform;

    // All loaded nodes — full cache, unfiltered
    private readonly List<UObjectNode> _allNodes = new();

    // Cancellation for the current load operation
    private CancellationTokenSource? _loadCts;

    // Debounce timer for FilterText changes (200 ms)
    private System.Threading.Timer? _filterDebounce;

    [ObservableProperty] private ObservableCollection<UObjectNode> _filteredNodes = new();
    [ObservableProperty] private UObjectNode? _selectedNode;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private int _selectedClassFilterIndex;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _objectCount;
    [ObservableProperty] private string _displayCount = "";
    [ObservableProperty] private string _statusText = "";

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

    /// <summary>Common search suggestions for UE class names.</summary>
    public string[] SearchSuggestions { get; } =
    [
        "AbilitySystemComponent",
        "ACharacter",
        "ActorComponent",
        "APawn",
        "AttributeSet",
        "AttributesComponent",
        "Character",
        "CharacterMovementComponent",
        "GameMode",
        "GameState",
        "HUD",
        "LocalPlayer",
        "Pawn",
        "PlayerController",
        "PlayerState",
        "UAttributeSet",
        "Widget",
    ];

    /// <summary>Fired when the selected node changes, for cross-VM communication.</summary>
    public event Action<UObjectNode?>? SelectionChanged;

    partial void OnSelectedNodeChanged(UObjectNode? value)
    {
        SelectionChanged?.Invoke(value);
    }

    partial void OnFilterTextChanged(string value)
    {
        // Debounce filter to avoid per-keystroke scanning of large caches (486K+ items).
        // 200ms delay allows typing to complete before filtering starts.
        _filterDebounce?.Dispose();
        _filterDebounce = new System.Threading.Timer(
            _ => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyFilter),
            null, 200, Timeout.Infinite);
    }

    partial void OnSelectedClassFilterIndexChanged(int value)
    {
        ApplyFilter();
    }

    public ObjectTreeViewModel(IDumpService dump, ILoggingService log, IPlatformService platform)
    {
        _dump = dump;
        _log = log;
        _platform = platform;
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
        await _platform.CopyToClipboardAsync(node.Address);
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

        try
        {
            ClearError();
            IsLoading = true;
            StatusText = "Loading...";
            _allNodes.Clear();
            FilterText = "";
            SelectedClassFilterIndex = 0;

            int offset = 0;
            int total = 0;

            do
            {
                ct.ThrowIfCancellationRequested();

                var result = await _dump.GetObjectListAsync(offset, Constants.ObjectTreePageSize, ct);
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
            StatusText = $"Loaded {_allNodes.Count:N0} objects";
            _log.Info($"Loaded {_allNodes.Count:N0} of {total:N0} objects");
        }
        catch (OperationCanceledException)
        {
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
            IsLoading = false;
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

        try
        {
            ClearError();
            IsLoading = true;
            StatusText = "Searching...";
            FilterText = "";
            SelectedClassFilterIndex = 0;

            // Server-side case-insensitive partial search across ALL objects
            var result = await _dump.SearchObjectsAsync(SearchText, 2000);
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

            StatusText = $"Found {result.Total:N0} results";
            _log.Info($"Search '{SearchText}': found {result.Total:N0} results");
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Search failed";
            _log.Error($"Search failed for '{SearchText}'", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Apply client-side class + text filter on the full in-memory cache.
    /// Caps the displayed items at <see cref="Constants.ObjectTreeMaxDisplay"/>
    /// to keep the UI responsive (Avalonia ListBox virtualization handles rendering).
    /// </summary>
    private void ApplyFilter()
    {
        FilteredNodes.Clear();
        var textFilter = FilterText?.Trim() ?? "";
        var classFilter = SelectedClassFilterIndex > 0
            ? ClassFilterOptions[SelectedClassFilterIndex] : null;

        int matchCount = 0;

        foreach (var node in _allNodes)
        {
            // Class type filter (exact match on ClassName)
            if (classFilter != null &&
                !node.ClassName.Equals(classFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            // Text filter (substring match on Name, ClassName, or Address)
            if (!string.IsNullOrEmpty(textFilter) &&
                !node.Name.Contains(textFilter, StringComparison.OrdinalIgnoreCase) &&
                !node.ClassName.Contains(textFilter, StringComparison.OrdinalIgnoreCase) &&
                !node.Address.Contains(textFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            matchCount++;

            // Cap the UI display collection to prevent excessive rendering overhead
            if (FilteredNodes.Count < Constants.ObjectTreeMaxDisplay)
                FilteredNodes.Add(node);
        }

        bool hasAnyFilter = !string.IsNullOrEmpty(textFilter) || classFilter != null;
        if (hasAnyFilter)
        {
            DisplayCount = matchCount > Constants.ObjectTreeMaxDisplay
                ? $"Filtered: {matchCount:N0} matches (showing {Constants.ObjectTreeMaxDisplay:N0})"
                : $"Filtered: {matchCount:N0} / {_allNodes.Count:N0}";
        }
        else
        {
            DisplayCount = matchCount > Constants.ObjectTreeMaxDisplay
                ? $"Objects: {_allNodes.Count:N0} (showing {Constants.ObjectTreeMaxDisplay:N0})"
                : $"Objects: {_allNodes.Count:N0}";
        }
    }
}
