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

    /// <summary>Configurable row cap. The GObjects walk is always exhaustive and
    /// <c>TotalClasses</c> counts to the end regardless ([CLASSTOTAL-2026-08-18]); this bounds only
    /// the rows materialized. Clamped 100..50000 by the NumericUpDown and again DLL-side.
    ///
    /// <para>[CLASSCAP] — the status line's cap note advises raising the cap, and advice must
    /// name a lever the toolbar exposes; this is that lever.
    /// (The sentence that advises a cap with no control is deliberately not quoted here:
    /// <c>ClassListCapTests</c> greps this file for it, so a doc quote would defeat the
    /// assertion.)</para></summary>
    [ObservableProperty] private int _classListCap = Constants.DefaultClassListCap;

    /// <inheritdoc cref="ClassListCap"/>
    /// <remarks>[SNAPINTERVAL-2026-08-20] façade — NumericUpDown.Value is decimal? and an emptied
    /// box drives it to null, which a compiled binding cannot convert to int. Absorbs the empty box
    /// only; range belongs to the control. Notifies unconditionally so a rejected entry repaints
    /// instead of leaving the box blank while a different value is in force.</remarks>
    public decimal? ClassListCapValue
    {
        get => (decimal)ClassListCap;
        set
        {
            ClassListCap = NumericInput.KeepCurrentIfEmpty(value, ClassListCap);
            OnPropertyChanged();
        }
    }

    partial void OnClassListCapChanged(int value) => OnPropertyChanged(nameof(ClassListCapValue));
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _superFilter = "";
    [ObservableProperty] private string _packageFilter = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private ObservableCollection<GameClassEntry> _results = new();
    /// <summary>Keeps the Results grid's selection and scroll position across filter edits
    /// [KEYWORD-BOX-VIEW-KEEP]; the panel attaches the grid to it.</summary>
    public FilterViewKeeper ResultsView { get; } = new();
    [ObservableProperty] private GameClassEntry? _selectedResult;
    [ObservableProperty] private bool _isXrefBatchRunning;

    /// <summary>Distinct SuperName values from loaded results (for AutoCompleteBox).</summary>
    [ObservableProperty] private List<string> _superSuggestions = new();

    /// <summary>Distinct Package prefixes from loaded results (for AutoCompleteBox).</summary>
    [ObservableProperty] private List<string> _packageSuggestions = new();

    /// <summary>Warning shown beside the Super / Package pickers when the class walk
    /// stopped at its cap, so their dropdowns are a sample rather than the set of
    /// values that exist. Empty (and hidden) on a complete load.</summary>
    [ObservableProperty] private string _suggestionsNote = "";

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

    /// <summary>Drop the class list so a reconnect never shows classes (and live
    /// UClass* addresses) from the previous game. Client-side only.</summary>
    public void ClearOnDisconnect()
    {
        _xrefBatchCts?.Cancel();
        _allResults = new List<GameClassEntry>();
        SelectedResult = null;       // detach before clearing the selection-bound grid
        Results.Clear();
        SuperSuggestions = new List<string>();
        PackageSuggestions = new List<string>();
        SuggestionsNote = "";
        StatusText = "";
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

            // The log line names the Game classes only value this load was sent with: the box stays clickable while
            // the load runs.
            bool gameOnlySent = GameClassesOnly;
            var result = await _dump.ListClassesAsync(gameOnly: gameOnlySent,
                                                      limit: ClassListCap);

            _allResults = result.Classes;
            RebuildSuggestions(result.Truncated, result.Total, result.TotalClasses);
            ApplyFilter();

            // A capped list must SAY so, AND say how many it capped out of. The DLL
            // counts every class to the end of GObjects even after it stops collecting
            // rows, so `TotalClasses` is the honest pool total, not a copy of the cap
            // ([CLASSTOTAL-2026-08-18]). On a truncated walk the two numbers
            // differ meaningfully — "5,000 of 6,609" — so filtering a page and finding
            // nothing is distinguishable from the class not existing, and the
            // user can see how far above the cap the real count is.
            // ⚠ "or raise the cap" is offered ONLY while the cap can actually go higher.
            // [CLASSCAP]: advice must name no lever the user cannot reach, and at the
            // ceiling the cap control cannot go higher, so the advice would be a lie there.
            // The cap note names RequestedLimit, the cap this load was sent with: the Max box can change while
            // the load runs.
            var capNote = result.Truncated
                ? "  ⚠ STOPPED at the " + result.RequestedLimit.ToString("N0") + "-row cap — filter to narrow"
                  + PartialResultNotice.RaiseMaxClause(result.RequestedLimit, Constants.MaxSearchCap)
                : "";
            StatusText = $"{result.Total:N0} classes shown of {result.TotalClasses:N0} total (scanned {result.ScannedObjects:N0} objects){capNote}";
            _log.Info($"ListClasses: {result.Total} results (gameOnly={gameOnlySent}, " +
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
    ///
    /// <para>
    /// The truncation facts are parameters rather than something this method reads back
    /// off <c>_allResults</c>, because they cannot be derived from it: a capped page and
    /// a complete list of the same length are identical here. <see cref="LoadAsync"/> has
    /// them from the same reply its status line reads, so the dropdowns built from a sample
    /// admit the cap just as that status line does.
    /// </para>
    /// </summary>
    /// <param name="truncated">The DLL stopped collecting at its row cap.</param>
    /// <param name="shown">Classes actually returned.</param>
    /// <param name="poolTotal">Classes counted to the end of GObjects — the honest pool
    /// total, which the DLL keeps counting past the cap ([CLASSTOTAL]).</param>
    private void RebuildSuggestions(bool truncated, int shown, int poolTotal)
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

        SuggestionsNote = truncated
            ? PartialResultNotice.DerivedListFromCappedPage(
                  "Super / Package suggestions", shown, poolTotal, "classes")
            : "";
    }

    private void ApplyFilter()
    {
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

        // Detach before rebuilding the selection-bound grid; unchanged rows are not rebuilt.
        ResultsView.Update(Results, filtered, () => SelectedResult = null,
                           FilterText, SuperFilter, PackageFilter);
    }

    [RelayCommand]
    private void ClearFilters()
    {
        // Commit a just-typed keyword BEFORE blanking the box (CLAUDE.md's keyword-search
        // rule: "Flush() before clearing the box on tab-switch/navigation"). Schedule()
        // only arms a 700 ms debounce, so a keyword typed, matched and then cleared inside
        // that window would be thrown away — and Clear is the single most likely thing to
        // do straight after reading the results.
        _filterMemory.Flush();
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

        var oldCts = _xrefBatchCts;          // dispose the prior run's CTS
        _xrefBatchCts = new CancellationTokenSource();
        oldCts?.Cancel();
        oldCts?.Dispose();
        var ct = _xrefBatchCts.Token;
        IsXrefBatchRunning = true;
        int done = 0, withFuncs = 0, cached = 0, partial = 0, cappedRows = 0, xrefCap = 0;
        try
        {
            foreach (var entry in targets)
            {
                ct.ThrowIfCancellationRequested();
                // Skip rows already scanned (XrefInfo persists across filter changes) —
                // but NOT one whose previous sweep hit the deadline, or the partial answer
                // becomes permanent for the session and a re-run cannot improve it.
                if (!string.IsNullOrEmpty(entry.XrefInfo) && !XrefFormat.IsPartialCell(entry.XrefInfo))
                { cached++; continue; }
                try
                {
                    var res = await _dump.FindFunctionsByClassAsync(entry.ClassAddr, true, 200, ct);
                    // The DLL runs each sweep against a real 30 s budget and latches
                    // `scan.deadline_hit` when it runs out. Discarding it would write a bare
                    // "0" for a class whose reflection sweep never finished, which reads as
                    // "no function takes this class" — the opposite of what a timeout
                    // establishes.
                    bool deadline = res.Scan?.DeadlineHit ?? false;
                    bool capped = res.Scan?.CapHit ?? false;   // [W3-XREF-CAP] its own cause, never the deadline's
                    entry.XrefInfo = XrefFormat.FunctionsSummary(res.Xrefs, deadline, capped);
                    if (res.Xrefs.Count > 0) withFuncs++;
                    if (deadline) partial++;
                    if (capped) { cappedRows++; xrefCap = res.Scan!.Cap; }
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
                       + (cached > 0 ? $" ({cached} cached)." : ".")
                       + PartialResultNotice.BatchPartialClause(partial, targets.Count, "class", units: "classes")
                       + PartialResultNotice.BatchCapClause(cappedRows, targets.Count, xrefCap, "class", units: "classes");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            StatusText = $"Find Func cancelled at {done}/{targets.Count}.";
        }
        catch (Exception ex)
        {
            // NOT a cancel. PipeClient distinguishes three causes:
            // a caller cancel carries OUR ct (caught above), a deliberate teardown carries
            // the client's own token, and an unexpected pipe death — the game crashing, the
            // DLL unloading — arrives as an IOException. A bare catch would report all of
            // them as "Find Func cancelled at N/M", so a dead game would read as "you
            // pressed Cancel" and nothing would be logged.
            _log.Error("Batch Find Func failed", ex);
            StatusText = $"Find Func failed at {done}/{targets.Count}.";
        }
        finally { IsXrefBatchRunning = false; }
    }

    /// <summary>Cancel an in-flight batch Find Func run.</summary>
    [RelayCommand]
    private void CancelXrefBatch() => _xrefBatchCts?.Cancel();
}
