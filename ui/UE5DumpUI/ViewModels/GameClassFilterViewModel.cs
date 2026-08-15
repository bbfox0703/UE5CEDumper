using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
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
    private readonly IPlatformService _platform;

    private List<GameClassEntry> _allResults = new();

    [ObservableProperty] private bool _gameClassesOnly = true;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _superFilter = "";
    [ObservableProperty] private string _packageFilter = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private ObservableCollection<GameClassEntry> _results = new();
    [ObservableProperty] private GameClassEntry? _selectedResult;
    [ObservableProperty] private bool _isXrefBatchRunning;

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

    /// <summary>Per-session remembered filter keywords (LRU) surfaced as the
    /// free-text filter box's AutoCompleteBox suggestions — see <see cref="KeywordSearchMemory"/>.</summary>
    private readonly KeywordSearchMemory _filterMemory;
    public ObservableCollection<string> FilterHistory => _filterMemory.History;

    public GameClassFilterViewModel(IDumpService dump, ILoggingService log, IPlatformService platform)
    {
        _dump = dump;
        _log = log;
        _platform = platform;
        _filterMemory = new KeywordSearchMemory(() => (FilterText, Results.Count > 0));
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
        _filterMemory.Schedule(value);
    }
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

            // A capped list must SAY so. The DLL stops walking GObjects the moment it has
            // `limit` rows, and `TotalClasses` is incremented in lockstep with the results
            // vector — so on a truncated walk BOTH numbers here are the cap, and the line
            // reads as "that is all of them" (audit #5 X2, same cap as the class-address
            // lookups). Filtering a page and finding nothing is then indistinguishable
            // from the class not existing.
            var capNote = result.Truncated
                ? $"  ⚠ STOPPED at the {result.RequestedLimit:N0}-row cap — more classes exist"
                : "";
            StatusText = $"{result.Total} classes (scanned {result.ScannedObjects:N0} objects, {result.TotalClasses} total UClasses){capNote}";
            _log.Info($"ListClasses: {result.Total} results (gameOnly={GameClassesOnly}, " +
                      $"scanned={result.ScannedObjects}, truncated={result.Truncated})");
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
        SelectedResult = null;   // detach before rebuilding the selection-bound grid
        Results.Clear();
        // Space-separated terms are ANDed (each must hit ClassName, SuperName or
        // ClassPath) — the shared Object Tree filter semantics, so "BP_ char"
        // keeps rows matching BOTH terms in any order.
        var terms = ObjectTreeFilter.SplitTerms(FilterText);
        var superF = SuperFilter.Trim();
        var pkgF = PackageFilter.Trim();

        // Collect matching entries first, then sort by score descending
        var filtered = new List<GameClassEntry>();

        foreach (var entry in _allResults)
        {
            // Name filter: AND-terms substring match on ClassName, SuperName, or ClassPath
            if (terms.Length > 0
                && !ObjectTreeFilter.MatchesAllTerms(terms, entry.ClassName, entry.SuperName, entry.ClassPath))
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

    /// <summary>"Find Func": which UFunctions take this class as a parameter or
    /// return value (find_functions_by_class — reflection, native functions
    /// included). Opens the shared xref dialog in class mode.</summary>
    [RelayCommand]
    private async Task FindFunctionsForClassAsync(GameClassEntry? entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.ClassAddr)) return;
        await Views.PropertyXrefDialog.ShowForClassAsync(
            entry.ClassName, entry.ClassAddr, _dump, _platform);
    }

    // Batch Find Func over the currently-filtered classes (or selected). Each
    // find_functions_by_class is a full game-wide reflection sweep → warn past a
    // low count, cancellable, skip already-scanned (XrefInfo persists across filter).
    private CancellationTokenSource? _xrefBatchCts;

    [RelayCommand]
    private async Task BatchFindFuncAsync(IList<GameClassEntry>? selected)
    {
        var targets = ((selected != null && selected.Count > 0) ? selected : (IList<GameClassEntry>)Results)
            .Where(e => !string.IsNullOrEmpty(e.ClassAddr)).ToList();
        if (targets.Count == 0) { StatusText = "No classes with an address to scan."; return; }

        if (targets.Count > Constants.XrefBatchWarnThreshold)
        {
            var ok = await Views.ConfirmDialog.ShowAsync(
                "Batch: find functions",
                $"Scan {targets.Count} classes? Each runs a FULL game-wide reflection "
              + "sweep, so this can take a while. Tip: narrow the filter or select fewer "
              + "rows first.",
                confirmText: "Run");
            if (!ok) return;
        }

        _xrefBatchCts?.Cancel();
        _xrefBatchCts = new CancellationTokenSource();
        var ct = _xrefBatchCts.Token;
        IsXrefBatchRunning = true;
        int done = 0, withFuncs = 0, cached = 0;
        try
        {
            foreach (var entry in targets)
            {
                ct.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(entry.XrefInfo)) { cached++; continue; }
                try
                {
                    var res = await _dump.FindFunctionsByClassAsync(entry.ClassAddr, true, 200, ct);
                    entry.XrefInfo = XrefFormat.FunctionsSummary(res.Xrefs);
                    if (res.Xrefs.Count > 0) withFuncs++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    entry.XrefInfo = "—";
                    _log.Error($"Batch Find Func failed for {entry.ClassName}", ex);
                }
                done++;
                StatusText = $"Find Func: {done}/{targets.Count} scanned ({withFuncs} taken by a function)…";
            }
            StatusText = $"Find Func done: {withFuncs}/{targets.Count} taken by a function"
                       + (cached > 0 ? $" ({cached} cached)." : ".");
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Find Func cancelled at {done}/{targets.Count}.";
        }
        finally { IsXrefBatchRunning = false; }
    }

    /// <summary>Cancel an in-flight batch Find Func run.</summary>
    [RelayCommand]
    private void CancelXrefBatch() => _xrefBatchCts?.Cancel();
}
