using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the "Dump Explorer" panel — an offline browser over a "Dump
/// All" JSON-Lines file (see <see cref="DumpAllService"/>). One keyword box
/// searches classes, properties AND functions at once (no need to pick a
/// category or switch tabs first), and results split into two groups:
///
///   • In current game    — the owning class resolves to a live object in the
///     connected game (matched by object PATH, so it survives game restarts);
///     each row can jump straight to that live class in the Live Walker.
///   • Not in current game — the class isn't live right now (dump from another
///     session/game, or not yet spawned); shown read-only as a metadata reference.
///
/// Parsing is fully offline; the live-match / jump features require a connected
/// (and scanned) game and reuse the existing GObjects list + Live Walker handoff.
/// </summary>
public partial class DumpExplorerViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService _platform;

    /// <summary>Max rows populated into EACH group grid — the full match count is
    /// still reported so the user knows results were capped.</summary>
    private const int ResultCap = 2000;

    // Full flattened corpus (kept off the observable collections; the grids show
    // a filtered, capped slice rebuilt on every search/category change).
    private readonly List<DumpEntry> _all = new();

    /// <summary>
    /// The loaded file's own "meta" line — which GAME and which BUILD it came from.
    /// Retained (not just formatted into the header and dropped) because the live match
    /// joins on bare class NAMES, and engine class names — Object, Actor, Pawn,
    /// PlayerController — are identical in every UE title. Without an identity check,
    /// loading game A's dump while game B is connected marks those rows "in current game"
    /// with a live address from B, and Jump opens B's object under A's label. Null until
    /// a file is loaded.
    /// </summary>
    private DumpMetaLine? _loadedMeta;

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _searchText = "";
    /// <summary>0 = All, 1 = Class, 2 = Property, 3 = Function.</summary>
    [ObservableProperty] private int _selectedCategoryIndex;
    [ObservableProperty] private string _statusText =
        "Load a “Dump All” (.jsonl) file to browse its classes, properties and functions.";
    [ObservableProperty] private string _headerText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasFile;
    [ObservableProperty] private bool _isGameConnected;
    [ObservableProperty] private bool _liveChecked;

    /// <summary>Path of the most recent in-session Export ▸ Dump All (empty until
    /// one is exported). Enables the one-click "load last export" button — we do
    /// NOT auto-load, because the user may re-export repeatedly or the tab may be
    /// mid-operation.</summary>
    [ObservableProperty] private string _lastExportPath = "";

    [ObservableProperty] private ObservableCollection<DumpEntry> _matched = new();
    [ObservableProperty] private ObservableCollection<DumpEntry> _unmatched = new();
    [ObservableProperty] private string _matchedHeader = "In current game";
    [ObservableProperty] private string _unmatchedHeader = "Not in current game";

    /// <summary>Row selected in the Matched / Unmatched grid (bound TwoWay to each
    /// grid's SelectedItem). A per-row action (Jump / Instances / Copy path) sets the
    /// acted-on row here via <see cref="SelectEntry"/> even when the user clicked the
    /// button without first selecting the row, so the selection survives the tab switch
    /// the action triggers and is restored (and scrolled back into view) on return.
    /// Only one is non-null at a time — selecting in one grid clears the other.</summary>
    [ObservableProperty] private DumpEntry? _matchedSelected;
    [ObservableProperty] private DumpEntry? _unmatchedSelected;

    // Long-running load / re-check cancellation (guarded OCE — see catch blocks).
    private CancellationTokenSource? _opCts;

    /// <summary>Combined match count (matched + unmatched, pre-cap) from the most
    /// recent <see cref="ApplyFilter"/>. The keyword-memory probe reads this to
    /// decide whether a settled search term produced any hits and is worth
    /// remembering — searching classes/props/funcs, not just the capped grid.</summary>
    private int _lastMatchTotal;

    /// <summary>Per-session remembered search keywords (LRU) surfaced as the search
    /// box's AutoCompleteBox suggestions — see <see cref="KeywordSearchMemory"/>.</summary>
    private readonly KeywordSearchMemory _searchMemory;
    public ObservableCollection<string> SearchHistory => _searchMemory.History;

    /// <summary>Jump the selected row's owning class into the Live Walker.
    /// Payload = the CURRENT live address (only raised for matched rows).</summary>
    public event Action<string>? NavigateToLiveWalker;

    /// <summary>Find live instances of a row's owning class (class → Instances tab).
    /// The correct bridge from a matched class to a locatable instance — Locate in
    /// GWorld/GameEngine is instance-oriented, so it stays downstream (Related),
    /// not on a class row here. Payload = class name.</summary>
    public event Action<string>? NavigateToInstanceFinder;

    public DumpExplorerViewModel(IDumpService dump, ILoggingService log, IPlatformService platform)
    {
        _dump = dump;
        _log = log;
        _platform = platform;
        _searchMemory = new KeywordSearchMemory(() => (SearchText, _lastMatchTotal > 0));
    }

    /// <summary>Fanned in from MainWindowViewModel on connect/disconnect. On
    /// disconnect the previous live match is invalidated: its <c>LiveAddr</c>s are
    /// now stale (a reconnect / game restart re-homes every object), so matched
    /// rows must stop offering a jump to a dead address until the user Re-checks
    /// against the new game.</summary>
    public void SetConnected(bool connected)
    {
        IsGameConnected = connected;
        if (!connected && LiveChecked)
        {
            foreach (var e in _all) { e.IsMatched = false; e.LiveAddr = ""; }
            LiveChecked = false;
            if (HasFile) ApplyFilter();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (HasFile) ApplyFilter();
        _searchMemory.Schedule(value);
    }

    partial void OnSelectedCategoryIndexChanged(int value)
    {
        if (HasFile) ApplyFilter();
    }

    [RelayCommand(CanExecute = nameof(CanStartOp))]
    private async Task LoadFileAsync()
    {
        var path = await _platform.ShowOpenFileDialogAsync("Dump JSON Lines (*.jsonl)", ".jsonl");
        if (string.IsNullOrEmpty(path)) return;
        await LoadFromPathAsync(path);
    }

    private bool CanStartOp() => !IsBusy;

    /// <summary>Called by MainWindowViewModel after a successful Export ▸ Dump All
    /// so the "load last export" button can pull it in without a file dialog.</summary>
    public void SetLastExportPath(string path) => LastExportPath = path ?? "";

    /// <summary>Filename of <see cref="LastExportPath"/> for the button tooltip.</summary>
    public string LastExportFileName =>
        string.IsNullOrEmpty(LastExportPath) ? "" : System.IO.Path.GetFileName(LastExportPath);

    [RelayCommand(CanExecute = nameof(CanLoadLastExport))]
    private async Task LoadLastExportAsync()
    {
        if (!string.IsNullOrEmpty(LastExportPath))
            await LoadFromPathAsync(LastExportPath);
    }

    private bool CanLoadLastExport() => !string.IsNullOrEmpty(LastExportPath) && !IsBusy;

    /// <summary>Parse a specific .jsonl dump and (if a game is connected) split by
    /// live match. Public so it can be driven from a handoff / test without the dialog.</summary>
    public async Task LoadFromPathAsync(string path)
    {
        // Capture THIS op's CTS locally so a superseding op replacing _opCts can't
        // misroute our cancellation (see feedback-pipeclient-bare-oce-cancel-guard).
        CancelInFlight();
        var cts = new CancellationTokenSource();
        _opCts = cts;
        var ct = cts.Token;
        // [R7-D-02] The parse reports through StatusProgress: Progress<int> + Dispatcher.Post queued each report twice,
        // so a "Parsing dump… N rows" still queued when the parse returned could replace a status set after it.
        // Completing the helper as soon as the parse returns drops every such report; the statuses after it are
        // plain assignments.
        var progress = new StatusProgress(msg => StatusText = msg);
        try
        {
            IsBusy = true;
            HasFile = false;
            LiveChecked = false;
            FilePath = path;
            StatusText = "Parsing dump…";
            _all.Clear();
            _loadedMeta = null;
            Matched = new ObservableCollection<DumpEntry>();
            Unmatched = new ObservableCollection<DumpEntry>();

            var parseProgress = progress.For<int>(n => $"Parsing dump… {n:N0} rows");

            var model = await Task.Run(() => DumpJsonlReader.ReadAsync(path, parseProgress, ct), ct);
            progress.Complete($"Parsed {model.Entries.Count:N0} rows");
            ct.ThrowIfCancellationRequested();

            _all.AddRange(model.Entries);
            _loadedMeta = model.Meta;
            HeaderText = BuildHeader(model);
            HasFile = _all.Count > 0;

            if (!HasFile)
            {
                StatusText = "No class metadata found in this file (is it a Dump All .jsonl?).";
                return;
            }

            _log.Info($"DumpExplorer loaded {System.IO.Path.GetFileName(path)}: " +
                      $"{model.ClassCount} classes / {model.PropertyCount} props / {model.FunctionCount} funcs");

            // Auto-classify against the live game if one is connected; otherwise
            // everything lands in "Not in current game" until the user Re-checks.
            if (IsGameConnected)
            {
                await RunLiveMatchAsync(ct);
            }
            else
            {
                ApplyFilter();
                StatusText = "Loaded. Connect a game and click “Re-check live” to split by what's in the current game.";
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            progress.Complete("Load cancelled.");
        }
        catch (Exception ex)
        {
            progress.Complete($"Load failed: {ex.Message}");
            SetError(ex);
            _log.Error("DumpExplorer load failed", ex);
        }
        finally
        {
            EndOp(cts);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRecheck))]
    private async Task RecheckLiveAsync()
    {
        CancelInFlight();
        var cts = new CancellationTokenSource();
        _opCts = cts;
        try
        {
            IsBusy = true;
            await RunLiveMatchAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            StatusText = "Re-check cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Re-check failed: {ex.Message}";
            _log.Error("DumpExplorer live re-check failed", ex);
        }
        finally
        {
            EndOp(cts);
        }
    }

    /// <summary>Finish an op: only the CURRENT op clears IsBusy / _opCts (a
    /// superseded op must not reset state owned by the op that replaced it), and
    /// dispose this op's CTS either way.</summary>
    private void EndOp(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_opCts, cts))
        {
            _opCts = null;
            IsBusy = false;
        }
        cts.Dispose();
    }

    private bool CanRecheck() => HasFile && IsGameConnected && !IsBusy;

    [RelayCommand]
    private void Cancel() => CancelInFlight();

    [RelayCommand]
    private void OpenInLiveWalker(DumpEntry? row)
    {
        SelectEntry(row);
        if (row is null) return;
        if (!row.IsMatched || string.IsNullOrEmpty(row.LiveAddr))
        {
            StatusText = "Not in current game — no live object to jump to.";
            return;
        }
        NavigateToLiveWalker?.Invoke(row.LiveAddr);
    }

    [RelayCommand]
    private void FindInstances(DumpEntry? row)
    {
        SelectEntry(row);
        if (row is null || string.IsNullOrEmpty(row.OwningClassName)) return;
        NavigateToInstanceFinder?.Invoke(row.OwningClassName);
    }

    [RelayCommand]
    private async Task CopyPathAsync(DumpEntry? row)
    {
        SelectEntry(row);
        if (row is null || string.IsNullOrEmpty(row.Path)) return;
        await _platform.CopyToClipboardAsync(row.Path);
        StatusText = $"Copied path: {row.Path}";
    }

    /// <summary>Make <paramref name="row"/> the current selection in whichever grid
    /// holds it, clearing the other grid's selection so exactly one row stays selected.
    /// Called by every per-row action so clicking its button auto-selects the row even
    /// when it wasn't selected first — the selection then persists across the tab switch
    /// the action triggers. No-op if the row isn't in either (capped-out) collection.</summary>
    private void SelectEntry(DumpEntry? row)
    {
        if (row is null) return;
        if (Matched.Contains(row))
        {
            MatchedSelected = row;
            UnmatchedSelected = null;
        }
        else if (Unmatched.Contains(row))
        {
            UnmatchedSelected = row;
            MatchedSelected = null;
        }
    }

    // ------------------------------------------------------------------
    // Live match: one GObjects pass -> class short name -> current address.
    // Keyed by the class's short FName (not full path) because the live object
    // list (get_object_list) only exposes the short name — full paths aren't on
    // the wire, and a per-class find_object is an O(n) scan (O(n^2) overall).
    // Names are stable across restarts (unlike addresses), which is what makes
    // this restart-safe; the trade-off is same-named classes in different
    // packages collide (last one indexed wins) — rare, and the row still opens a
    // real live class the user can verify in the Live Walker.
    // ------------------------------------------------------------------

    /// <summary>
    /// The wrong-game gate, as a pure function of the two identities (module name + pe_hash). Refused = a different
    /// GAME; otherwise the caveat (possibly "") that prefixes the match status.
    ///
    /// Tier 2 — same game, different BUILD. Class names survive a patch; addresses and offsets need not. Worth
    /// matching, not worth asserting silently. Deliberately NOT a refusal: pe_hash is per-build, so refusing here
    /// would also reject a dump the user took of this very game last week, which is the feature's normal use.
    ///
    /// [PATH-UI-LEGACY-QMARK] A module name containing '?' (never legal in a file name) is what a DLL older than
    /// [PATH-MODULE-NAME-UTF8] reports for a non-ASCII exe. It is unknown, not a name: two such names being equal
    /// does not make two games the same ("???.exe" == "???.exe"), and one such name differing from a real one does
    /// not make them different (a dump taken through the old DLL, of this very exe). Equal pe_hashes still prove
    /// the same exe; anything less cannot confirm it.
    /// </summary>
    internal static (bool Refused, string Caveat) JudgeIdentity(string fileModule, string filePeHash,
                                                                 string liveModule, string livePeHash)
    {
        bool fileLossy = fileModule.Contains('?'), liveLossy = liveModule.Contains('?');
        bool samePe = filePeHash.Length > 0 && livePeHash.Length > 0
                      && string.Equals(filePeHash, livePeHash, StringComparison.OrdinalIgnoreCase);
        if (fileLossy || liveLossy)
            return (false, samePe
                ? ""
                : "An older DLL reported the game's name with '?' for its non-ASCII characters — could not "
                  + "confirm it is this game. ");

        if (fileModule.Length > 0 && liveModule.Length > 0 &&
            !string.Equals(fileModule, liveModule, StringComparison.OrdinalIgnoreCase))
            return (true, "");

        if (fileModule.Length > 0 && liveModule.Length > 0)
        {
            if (filePeHash.Length > 0 && livePeHash.Length > 0 && !samePe)
                return (false, "Different build of the same game — offsets may have moved. ");
            if (filePeHash.Length == 0 || livePeHash.Length == 0)
                return (false, "Build identity unknown (no pe_hash) — matched on module name only. ");
            return (false, "");
        }
        // Pre-pe_hash / hand-made file, or a DLL that reported no module: match, but never let the absence of the
        // field read as a positive identity check.
        return (false, "Dump carries no game identity — could not confirm it is this game. ");
    }

    private async Task RunLiveMatchAsync(CancellationToken ct)
    {
        if (!IsGameConnected)
        {
            StatusText = "Connect a game first, then Re-check.";
            return;
        }

        // Identity gate. The join below is on bare class NAMES, and every UE title has an
        // Object / Actor / Pawn / PlayerController, so a cross-game match is not a rare
        // edge — it is the guaranteed outcome of loading the wrong file, and it looks like
        // a successful match. Read the live identity HERE rather than fanning EngineState
        // into the VM: SetConnected(true) can fire before an EngineState exists, and that
        // ordering window is the known wrong-game bleed (todo.md C2) — a gate that races
        // its own input would be the bug it is meant to prevent.
        string liveModule = "", livePeHash = "";
        try
        {
            var state = await _dump.GetPointersAsync(ct);
            liveModule = state.ModuleName ?? "";
            livePeHash = state.PeHash ?? "";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Can't establish identity → don't match. Declining is honest; matching anyway
            // is the silent mislabel this gate exists to stop.
            _log.Error("DumpExplorer live-match identity probe failed", ex);
            StatusText = "Could not read the connected game's identity — live match skipped. Re-check to retry.";
            return;
        }

        var fileModule = _loadedMeta?.Module ?? "";
        var filePeHash = _loadedMeta?.PeHash ?? "";

        var (refused, caveat) = JudgeIdentity(fileModule, filePeHash, liveModule, livePeHash);

        // Tier 1 — different GAME. Refuse outright; nothing here is transferable.
        if (refused)
        {
            foreach (var e in _all) { e.IsMatched = false; e.LiveAddr = ""; }
            LiveChecked = false;
            ApplyFilter();
            StatusText = $"Live match refused: this dump is from {fileModule}, but the connected game is {liveModule}.";
            _log.Warn($"DumpExplorer live match refused: dump module '{fileModule}' != live module '{liveModule}'");
            return;
        }


        StatusText = "Scanning the live game's classes…";
        var index = await BuildLiveClassIndexAsync(ct);
        ct.ThrowIfCancellationRequested();

        int matched = 0;
        // A distinct-class tally so the status reflects classes, not rows.
        var seenClasses = new HashSet<string>(StringComparer.Ordinal);
        int liveClasses = 0;
        foreach (var e in _all)
        {
            bool ok = index.TryGetValue(e.OwningClassName, out var live)
                      && !string.IsNullOrEmpty(live);
            e.IsMatched = ok;
            e.LiveAddr = ok ? live! : "";
            if (ok) matched++;
            if (e.Kind == DumpEntryKind.Class && seenClasses.Add(e.OwningClassName) && ok) liveClasses++;
        }

        LiveChecked = true;
        ApplyFilter();
        StatusText = caveat +
                     $"Live match: {liveClasses:N0} class(es) in the current game " +
                     $"({matched:N0} of {_all.Count:N0} rows matched).";
    }

    /// <summary>Paginate GObjects once and index class-like objects by their short
    /// name -> CURRENT live address. Name (not path) because that's all
    /// get_object_list carries; same-name collisions resolve last-wins.</summary>
    private async Task<Dictionary<string, string>> BuildLiveClassIndexAsync(CancellationToken ct)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        int offset = 0;
        const int pageSize = Constants.GObjectsWalkPageSize;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await _dump.GetObjectListAsync(offset, pageSize, ct);
            foreach (var o in page.Objects)
            {
                if (string.IsNullOrEmpty(o.Name)) continue;
                if (!DumpAllService.IsClassLikeMetaName(o.ClassName)) continue;
                dict[o.Name] = o.Address;   // short class name -> current live address
            }
            int advanced = page.Scanned > 0 ? page.Scanned : page.Objects.Count;
            offset += advanced;
            if (advanced == 0 || offset >= page.Total) break;
        }
        return dict;
    }

    // ------------------------------------------------------------------
    // Filtering.
    // ------------------------------------------------------------------

    [RelayCommand]
    private void ApplyFilter()
    {
        // R4 — the last panel not using the shared space=AND helpers. Splitting on ' '
        // alone missed tab/newline-separated input (pasting a column out of a
        // spreadsheet), and hand-rolling the match is how a rule drifts.
        //
        // MEASURED before choosing the shape (500K entries, 3 terms, mean of 5 passes):
        //   concat haystack + Ordinal           26.7 ms   <- what this used to do
        //   FOUR fields + OrdinalIgnoreCase     55.1 ms   <- 2x WORSE
        //   concat haystack + OrdinalIgnoreCase 25.8 ms   <- chosen
        // All three produced identical hit counts. So the four-field split the audit
        // suggested as the ideal shape is rejected ON A NUMBER, not on taste: this panel
        // is the one that loads a whole offline dump, and doubling its filter cost to
        // satisfy a rule whose observable behaviour is already identical would be a bad
        // trade. The single pre-lowered haystack IS the field-OR set, concatenated at
        // parse time.
        var terms = ObjectTreeFilter.SplitTerms(SearchText);

        DumpEntryKind? kindFilter = SelectedCategoryIndex switch
        {
            1 => DumpEntryKind.Class,
            2 => DumpEntryKind.Property,
            3 => DumpEntryKind.Function,
            _ => null,
        };

        var matched = new List<DumpEntry>();
        var unmatched = new List<DumpEntry>();
        int matchedTotal = 0, unmatchedTotal = 0;

        foreach (var e in _all)
        {
            if (kindFilter.HasValue && e.Kind != kindFilter.Value) continue;
            if (!ObjectTreeFilter.MatchesAllTerms(terms, e.Haystack)) continue;

            if (e.IsMatched)
            {
                matchedTotal++;
                if (matched.Count < ResultCap) matched.Add(e);
            }
            else
            {
                unmatchedTotal++;
                if (unmatched.Count < ResultCap) unmatched.Add(e);
            }
        }

        Matched = new ObservableCollection<DumpEntry>(matched);
        Unmatched = new ObservableCollection<DumpEntry>(unmatched);

        // Settled-signal for keyword-memory: total hits across both groups (pre-cap),
        // so a remembered keyword reflects real matches even when the grid is capped.
        _lastMatchTotal = matchedTotal + unmatchedTotal;

        MatchedHeader = LiveChecked
            ? $"✅ In current game — {GroupCountLabel(matched.Count, matchedTotal)}"
            : "✅ In current game — (run Re-check live)";
        UnmatchedHeader = LiveChecked
            ? $"⚠ Not in current game — {GroupCountLabel(unmatched.Count, unmatchedTotal)}"
            : $"⚠ Not checked yet — {GroupCountLabel(unmatched.Count, unmatchedTotal)}";
    }

    private static string GroupCountLabel(int shown, int total) =>
        shown < total ? $"showing {shown:N0} of {total:N0}" : $"{total:N0}";

    private static string BuildHeader(DumpFileModel model)
    {
        var m = model.Meta;
        var counts = $"{model.ClassCount:N0} classes · {model.PropertyCount:N0} props · {model.FunctionCount:N0} funcs";
        if (m is null) return counts;
        var ue = m.UeVersion > 0 ? $"UE {m.UeVersion / 100}.{m.UeVersion % 100}" : "UE ?";
        var mod = string.IsNullOrEmpty(m.Module) ? "" : $" · {m.Module}";
        var when = string.IsNullOrEmpty(m.DumpedAt) ? "" : $" · {m.DumpedAt}";
        return $"{ue}{mod} · {counts}{when}";
    }

    private void CancelInFlight()
    {
        try { _opCts?.Cancel(); } catch { /* already disposed */ }
    }

    partial void OnHasFileChanged(bool value) => RecheckLiveCommand.NotifyCanExecuteChanged();
    partial void OnIsGameConnectedChanged(bool value) => RecheckLiveCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value)
    {
        RecheckLiveCommand.NotifyCanExecuteChanged();
        LoadFileCommand.NotifyCanExecuteChanged();
        LoadLastExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnLastExportPathChanged(string value)
    {
        LoadLastExportCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(LastExportFileName));
    }
}
