using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Property Search panel.
/// Searches property names + types across all UClass objects, plus a
/// client-side filter over already-fetched results so refining a hit
/// list doesn't need another DLL roundtrip.
/// </summary>
public partial class PropertySearchViewModel : ViewModelBase, IDisposable
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;

    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _typeFilter = "";
    [ObservableProperty] private string _resultFilter = "";
    [ObservableProperty] private bool _gameClassesOnly = true;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private ObservableCollection<PropertySearchMatch> _results = new();
    [ObservableProperty] private PropertySearchMatch? _selectedResult;

    /// <summary>
    /// Curated FProperty type names for the Type Filter autocomplete.
    /// Sorted alphabetically so the dropdown is predictable. Most users
    /// won't remember type spellings ("MulticastSparseDelegateProperty")
    /// — typing "sp", "del", "opt", "weak" etc. should narrow quickly.
    /// </summary>
    public string[] PropertyTypeSuggestions { get; } =
    [
        // Numerics
        "BoolProperty",
        "ByteProperty",
        "DoubleProperty",
        "FloatProperty",
        "Int8Property",
        "Int16Property",
        "Int64Property",
        "IntProperty",
        "UInt16Property",
        "UInt32Property",
        "UInt64Property",
        // Strings
        "NameProperty",
        "StrProperty",
        "TextProperty",
        // Pointers
        "ClassProperty",
        "InterfaceProperty",
        "ObjectProperty",
        // Weak / soft / lazy
        "LazyObjectProperty",
        "SoftClassProperty",
        "SoftObjectProperty",
        "WeakObjectProperty",
        // Containers
        "ArrayProperty",
        "MapProperty",
        "SetProperty",
        // Structs / enums
        "EnumProperty",
        "StructProperty",
        // Delegates
        "DelegateProperty",
        "MulticastDelegateProperty",
        "MulticastInlineDelegateProperty",
        "MulticastSparseDelegateProperty",
        // UE 5.2+ / rare
        "FieldPathProperty",
        "OptionalProperty",
    ];

    /// <summary>
    /// Full result set as last returned by the DLL — `Results` is a
    /// possibly-filtered view of this. Kept private so binding consumers
    /// always see the filtered subset.
    /// </summary>
    private List<PropertySearchMatch> _allResults = new();

    /// <summary>Debounce timer for client-side ResultFilter typing.</summary>
    private System.Threading.Timer? _resultFilterDebounce;
    private bool _disposed;

    /// <summary>
    /// Event raised when user wants to find instances of a class in Instance Finder.
    /// </summary>
    public event Action<string>? NavigateToInstanceFinder;

    /// <summary>
    /// Event raised when user wants to navigate to a class address in Live Walker.
    /// </summary>
    public event Action<string>? NavigateToLiveWalker;

    public PropertySearchViewModel(IDumpService dump, ILoggingService log)
    {
        _dump = dump;
        _log = log;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var trimmedQuery = (SearchQuery ?? "").Trim();
        // Parse comma-separated type filter; whitespace-only entries dropped.
        var types = (TypeFilter ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // DLL requires at least one constraint — name OR type.
        if (string.IsNullOrEmpty(trimmedQuery) && types.Length == 0)
        {
            StatusText = "Enter a property name or type filter";
            return;
        }

        try
        {
            ClearError();
            IsSearching = true;
            StatusText = "Searching...";

            var result = await _dump.SearchPropertiesAsync(
                trimmedQuery,
                types: types.Length > 0 ? types : null,
                gameOnly: GameClassesOnly);

            // Cache the full set so the client-side ResultFilter can refine
            // without another DLL roundtrip.
            _allResults = new List<PropertySearchMatch>(result.Results);
            ApplyResultFilter();

            var typeSuffix = types.Length > 0 ? $" [types: {string.Join(",", types)}]" : "";
            StatusText = $"Found {result.Total} properties in {result.ScannedClasses:N0} classes (scanned {result.ScannedObjects:N0} objects)";
            _log.Info($"SearchProperties: '{trimmedQuery}'{typeSuffix} -> {result.Total} results (classes={result.ScannedClasses}, objects={result.ScannedObjects})");
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Search failed";
            _log.Error($"SearchProperties failed for '{SearchQuery}'", ex);
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>
    /// Debounced — refilter the locally-cached _allResults whenever the
    /// user types in the client-side filter. Avoids per-keystroke
    /// rebuilding of the ObservableCollection on large result sets.
    /// </summary>
    partial void OnResultFilterChanged(string value)
    {
        if (_disposed) return;
        _resultFilterDebounce?.Dispose();
        _resultFilterDebounce = new System.Threading.Timer(
            _ => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyResultFilter),
            null, 150, Timeout.Infinite);
    }

    /// <summary>
    /// Rebuild Results from _allResults, applying ResultFilter as a
    /// case-insensitive substring across Class / Property / Type / Preview.
    /// Called on every search completion AND on each filter change.
    /// </summary>
    private void ApplyResultFilter()
    {
        var filter = (ResultFilter ?? "").Trim();
        Results.Clear();

        if (string.IsNullOrEmpty(filter))
        {
            foreach (var m in _allResults) Results.Add(m);
            return;
        }

        foreach (var m in _allResults)
        {
            if (MatchesFilter(m, filter))
                Results.Add(m);
        }
    }

    private static bool MatchesFilter(PropertySearchMatch m, string filter)
    {
        // Case-insensitive Contains across the columns the user can see.
        return ContainsCI(m.ClassName, filter)
            || ContainsCI(m.PropName,  filter)
            || ContainsCI(m.PropType,  filter)
            || ContainsCI(m.SuperName, filter)
            || ContainsCI(m.Preview,   filter);
    }

    private static bool ContainsCI(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void FindInstances(PropertySearchMatch? match)
    {
        if (match == null) return;
        NavigateToInstanceFinder?.Invoke(match.ClassName);
    }

    [RelayCommand]
    private void OpenInWalker(PropertySearchMatch? match)
    {
        if (match == null || string.IsNullOrEmpty(match.ClassAddr)) return;
        NavigateToLiveWalker?.Invoke(match.ClassAddr);
    }

    [RelayCommand]
    private void CopyOffset(PropertySearchMatch? match)
    {
        if (match == null) return;
        Avalonia.Input.Platform.IClipboard? clipboard = null;
        if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            clipboard = desktop.MainWindow?.Clipboard;
        }
        clipboard?.SetTextAsync(match.OffsetHex);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _resultFilterDebounce?.Dispose();
        _resultFilterDebounce = null;
        GC.SuppressFinalize(this);
    }
}
