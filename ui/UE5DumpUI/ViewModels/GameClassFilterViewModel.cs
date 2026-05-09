using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Game Class Filter panel.
/// Lists all UClass objects, optionally filtering out engine classes.
/// Client-side filters: text (name), Super class, Package prefix.
/// </summary>
public partial class GameClassFilterViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;

    private List<GameClassEntry> _allResults = new();

    [ObservableProperty] private bool _gameClassesOnly = true;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _superFilter = "";
    [ObservableProperty] private string _packageFilter = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private ObservableCollection<GameClassEntry> _results = new();
    [ObservableProperty] private GameClassEntry? _selectedResult;

    /// <summary>Distinct SuperName values from loaded results (for AutoCompleteBox).</summary>
    [ObservableProperty] private List<string> _superSuggestions = new();

    /// <summary>Distinct Package prefixes from loaded results (for AutoCompleteBox).</summary>
    [ObservableProperty] private List<string> _packageSuggestions = new();

    /// <summary>Event raised when user wants to find instances of a class.</summary>
    public event Action<string>? NavigateToInstanceFinder;

    /// <summary>Event raised when user wants to navigate to a class address in Live Walker.</summary>
    public event Action<string>? NavigateToLiveWalker;

    /// <summary>Event raised when user wants to walk a class in ClassStruct panel.</summary>
    public event Action<string>? NavigateToClassStruct;

    public GameClassFilterViewModel(IDumpService dump, ILoggingService log)
    {
        _dump = dump;
        _log = log;
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnSuperFilterChanged(string value) => ApplyFilter();
    partial void OnPackageFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            ClearError();
            IsLoading = true;
            StatusText = "Loading...";

            var result = await _dump.ListClassesAsync(gameOnly: GameClassesOnly);

            _allResults = result.Classes;
            RebuildSuggestions();
            ApplyFilter();

            StatusText = $"{result.Total} classes (scanned {result.ScannedObjects:N0} objects, {result.TotalClasses} total UClasses)";
            _log.Info($"ListClasses: {result.Total} results (gameOnly={GameClassesOnly}, scanned={result.ScannedObjects})");
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Load failed";
            _log.Error("ListClasses failed", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Extract distinct Super names and Package prefixes from loaded results.
    /// Package prefix logic lives on <see cref="GameClassEntry"/> (so the
    /// new Package column in the DataGrid sees the same value).
    /// </summary>
    private void RebuildSuggestions()
    {
        // Distinct super names, sorted
        var supers = _allResults
            .Select(e => e.SuperName)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        SuperSuggestions = supers;

        // Distinct package prefixes (first 2 path segments, e.g. "/Script/Engine", "/Game")
        var packages = _allResults
            .Select(e => e.Package)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        PackageSuggestions = packages;
    }

    private void ApplyFilter()
    {
        Results.Clear();
        var nameFilter = FilterText.Trim();
        var superF = SuperFilter.Trim();
        var pkgF = PackageFilter.Trim();

        // Collect matching entries first, then sort by score descending
        var filtered = new List<GameClassEntry>();

        foreach (var entry in _allResults)
        {
            // Name filter: substring match on ClassName, SuperName, or ClassPath
            if (!string.IsNullOrEmpty(nameFilter)
                && !entry.ClassName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)
                && !entry.SuperName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)
                && !entry.ClassPath.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Super filter: exact match on SuperName
            if (!string.IsNullOrEmpty(superF)
                && !entry.SuperName.Equals(superF, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Package filter: prefix match on the Package column. Using
            // the derived prefix (rather than the full ClassPath) means
            // typing "/Game" matches /Game/* without false-matching paths
            // that happen to start with /Game-as-substring of something.
            if (!string.IsNullOrEmpty(pkgF)
                && !entry.Package.StartsWith(pkgF, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filtered.Add(entry);
        }

        // Sort by heuristic score descending (DLL already sorts, but re-sort after filter)
        filtered.Sort((a, b) =>
        {
            int cmp = b.Score.CompareTo(a.Score);
            return cmp != 0 ? cmp : string.Compare(a.ClassName, b.ClassName, StringComparison.Ordinal);
        });

        foreach (var entry in filtered)
        {
            Results.Add(entry);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FilterText = "";
        SuperFilter = "";
        PackageFilter = "";
    }

    [RelayCommand]
    private void FindInstances(GameClassEntry? entry)
    {
        if (entry == null) return;
        NavigateToInstanceFinder?.Invoke(entry.ClassName);
    }

    [RelayCommand]
    private void OpenInWalker(GameClassEntry? entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.ClassAddr)) return;
        NavigateToLiveWalker?.Invoke(entry.ClassAddr);
    }

    [RelayCommand]
    private void WalkClass(GameClassEntry? entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.ClassAddr)) return;
        NavigateToClassStruct?.Invoke(entry.ClassAddr);
    }
}
