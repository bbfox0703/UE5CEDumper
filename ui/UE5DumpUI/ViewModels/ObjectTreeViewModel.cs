using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Object Tree panel.
/// </summary>
public partial class ObjectTreeViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService _platform;

    // All loaded nodes (unfiltered)
    private readonly List<UObjectNode> _allNodes = new();

    [ObservableProperty] private ObservableCollection<UObjectNode> _filteredNodes = new();
    [ObservableProperty] private UObjectNode? _selectedNode;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private int _selectedClassFilterIndex;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _objectCount;
    [ObservableProperty] private string _displayCount = "";

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

    /// <summary>Common search suggestions for UE5 class names.</summary>
    public string[] SearchSuggestions { get; } =
    [
        "AttributesComponent",
        "AbilitySystemComponent",
        "PlayerController",
        "PlayerState",
        "GameMode",
        "GameState",
        "Character",
        "Pawn",
        "HUD",
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
        ApplyFilter();
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

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            ClearError();
            IsLoading = true;
            _allNodes.Clear();
            FilterText = "";
            SelectedClassFilterIndex = 0;

            int offset = 0;
            int total = 0;

            do
            {
                var result = await _dump.GetObjectListAsync(offset, Constants.DefaultPageSize);
                total = result.Total;
                ObjectCount = total;

                foreach (var obj in result.Objects)
                {
                    _allNodes.Add(obj);
                }

                // Advance by scanned count (not returned count) to avoid stalling
                // when many objects in a range are unnamed/null
                offset += result.Scanned;

                // Limit initial load to prevent UI freeze
                if (_allNodes.Count >= 2000) break;

            } while (offset < total);

            ApplyFilter();
            _log.Info($"Loaded {_allNodes.Count} of {total} objects");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to load objects", ex);
        }
        finally
        {
            IsLoading = false;
        }
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

            _log.Info($"Search '{SearchText}': found {result.Total} results");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Search failed for '{SearchText}'", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        FilteredNodes.Clear();
        var textFilter = FilterText?.Trim() ?? "";
        var classFilter = SelectedClassFilterIndex > 0
            ? ClassFilterOptions[SelectedClassFilterIndex] : null;

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

            FilteredNodes.Add(node);
        }

        bool hasAnyFilter = !string.IsNullOrEmpty(textFilter) || classFilter != null;
        DisplayCount = hasAnyFilter
            ? $"Filtered: {FilteredNodes.Count} / {_allNodes.Count}"
            : $"Objects: {_allNodes.Count} (of {ObjectCount})";
    }
}
