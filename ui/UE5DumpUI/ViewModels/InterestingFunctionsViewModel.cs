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
/// ViewModel for the Interesting Functions Finder panel.
///
/// Workflow:
/// 1. User clicks Load -> DLL <c>list_all_functions</c> returns every
///    UFunction across every UClass (~50k for a big game).
/// 2. <see cref="KeywordScoringTable"/> scores + categorises each row
///    client-side. Cheap (~50k rows process in &lt;1s).
/// 3. UI displays rows sorted by FinalScore descending, filtered by
///    name substring, category chip, and (optional) "Show All" toggle
///    that bypasses the InterestingThreshold cutoff.
/// 4. Per-row actions: Open in LiveWalker (with find_instance fallback
///    to ClassStruct when no live instance exists) and Copy AA Script
///    (Baked) -- shortcut into the same dialog that LiveWalker's
///    AA(Baked) button opens, fetching params on demand.
///
/// Cross-tab navigation events are fired up to MainWindowViewModel
/// which knows how to switch tabs + invoke the right commands on
/// LiveWalkerViewModel / ClassStructViewModel. Mirrors the
/// GameClassFilterViewModel pattern.
/// </summary>
public partial class InterestingFunctionsViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IAobMakerBridge? _aobMaker;
    private readonly IPlatformService? _platform;

    /// <summary>Cooldown between AOBMaker availability re-checks. The
    /// pipe connect attempt has a 2s timeout when CE isn't running, so
    /// without throttling rapid tab switches would stack 2s-blocking
    /// checks. Mirrors <see cref="LiveWalkerViewModel"/>'s pattern.</summary>
    private static readonly TimeSpan AobMakerCheckCooldown = TimeSpan.FromSeconds(5);
    private DateTime _lastAobMakerCheck = DateTime.MinValue;

    /// <summary>Full unfiltered list of scored rows. Filter operates
    /// against this and rebuilds <see cref="Results"/>.</summary>
    private List<ScoredFunctionRow> _allRows = new();

    /// <summary>Raw entries from the last Load — kept so the
    /// <see cref="GameplayActions"/> opt-in can re-score without a pipe
    /// round-trip (the entry set is unchanged; only the scoring differs).</summary>
    private IReadOnlyList<AllFunctionEntry> _entries = Array.Empty<AllFunctionEntry>();

    /// <summary>Which <see cref="GameplayActions"/> mode <see cref="_allRows"/>
    /// was actually scored with. The grid and the CheckBox can disagree because
    /// a toggle raised while a Load is in flight is dropped by
    /// <see cref="RescoreAsync"/>'s busy guard; comparing this against the live
    /// property is how the two are reconciled once the Load finishes.</summary>
    private bool _scoredWithGameplayActions;

    /// <summary>The re-score <see cref="LoadAsync"/> kicked off to reconcile a
    /// toggle that arrived while it was busy, or null when the modes already
    /// agreed. Exists purely as a test seam — production code never reads it.</summary>
    internal Task? PendingRescore { get; private set; }

    [ObservableProperty] private bool   _gameOnly = true;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private FunctionCategory? _categoryFilter; // null = All
    [ObservableProperty] private bool   _showAll;     // false = hide rows below threshold
    /// <summary>Opt-in (default OFF): fold the Gameplay-Action keyword pack
    /// (Dash/Interact/Open/Buy/Sell/Shop/Vendor/…) into scoring so
    /// character-control + shop verbs the cheat-oriented buckets omit can
    /// clear the threshold. Toggling re-scores the loaded set in place.</summary>
    [ObservableProperty] private bool   _gameplayActions;
    /// <summary>Opt-in filter (default OFF): show only functions that are
    /// BlueprintCallable or Exec. Gameplay/control/shop entry points are
    /// almost always one of these two (both flags survive cooking), so this
    /// hides native getters/setters/plumbing noise. Pure display filter —
    /// no re-score, no threshold change.</summary>
    [ObservableProperty] private bool   _callableOnly;
    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _statusText = "Click Load to scan all UFunctions";
    [ObservableProperty] private ObservableCollection<ScoredFunctionRow> _results = new();
    [ObservableProperty] private ScoredFunctionRow? _selectedResult;
    [ObservableProperty] private bool _isXrefBatchRunning;

    /// <summary>True when the AOBMaker CE Plugin pipe responded to the
    /// last availability check. Defaults true so the UI doesn't gray
    /// out before we've had a chance to probe; first
    /// <see cref="CheckAobMakerAsync"/> on Load updates it.</summary>
    [ObservableProperty] private bool _isAobMakerAvailable = true;

    // Static category list used by the chip dropdown (in chip-display order).
    public IReadOnlyList<FunctionCategory?> CategoryOptions { get; } = new FunctionCategory?[]
    {
        null,                          // "All"
        FunctionCategory.Stats,
        FunctionCategory.Inventory,
        FunctionCategory.Movement,
        FunctionCategory.Combat,
        FunctionCategory.Utility,
        FunctionCategory.GameplayAction,
        FunctionCategory.Other,
    };

    // ------------------------------------------------------------------
    // Cross-tab navigation events. MainWindowViewModel subscribes and
    // routes to the right destination panel. Two events:
    //
    // - NavigateToFunction(className, funcName): try to find a live
    //   non-CDO instance of `className`; on hit, switch to LiveWalker
    //   tab + walk to instance + auto-scroll to `funcName` row. On
    //   miss, switch to ClassStruct tab + walk class metadata.
    // - RequestCopyBakedScript(className, funcName, classAddr): fetch full
    //   FunctionInfoModel via walk_functions, then open the Copy AA
    //   Script dialog (or fast-path no-arg generation). classAddr comes
    //   straight off the row (list_all_functions supplies it) so the
    //   handler never has to re-derive it from the CAPPED list_classes
    //   page, which used to fail the whole action for any class past the
    //   cap (audit #5 X2).
    // ------------------------------------------------------------------

    public event Action<string, string>? NavigateToFunction;
    public event Action<string, string, string>? RequestCopyBakedScript;

    /// <summary>Raised by the per-row "inst" button to open the function's class
    /// in the Instance Finder tab. Payload = class name.</summary>
    public event Action<string>? NavigateToInstanceFinder;

    /// <summary>Raised to locate a live instance of the function's class within
    /// the GWorld object graph. Payload = class name (MainWindow resolves a
    /// representative instance via find_instance, then runs the path search).</summary>
    public event Action<string>? LocateInGWorld;

    /// <summary>Engine-rooted counterpart of <see cref="LocateInGWorld"/> (path search
    /// rooted at the live UGameEngine). Payload = class name.</summary>
    public event Action<string>? LocateInGameEngine;

    /// <summary>True when GWorld is available — gates the per-row "Locate in GWorld" button.</summary>

    /// <summary>Raised when the user clicks "Generate Cheat Table" on
    /// the multi-select toolbar. Subscribers (MainWindow) open the
    /// save dialog and write the payload — IO out of the VM keeps the
    /// command testable.</summary>
    public event Action<string /*defaultFileName*/, string /*ctXml*/>? RequestSaveCheatTable;

    /// <summary>
    /// Raised when a row's "Name" button is clicked. MainWindow handler
    /// hands the string to the platform clipboard service. Keeps this VM
    /// free of an IPlatformService dependency so the test stubs stay tiny.
    /// </summary>
    public event Action<string>? RequestCopyText;

    /// <summary>Client-side class-noise filter: hides ticked classes (UI widgets,
    /// sound, system components) from <see cref="Results"/>. Lives over the full
    /// <see cref="_allRows"/> set; ticking re-runs <see cref="ApplyFilter"/>.</summary>
    public ClassFacetFilter ClassFilter { get; }

    /// <summary>Live "N hidden by class filter" note (empty when nothing hidden) —
    /// lets the user tell an empty grid caused by a stale class exclusion from a
    /// genuine miss. Recomputed by <see cref="ApplyFilter"/>.</summary>
    [ObservableProperty] private string _classFilterNote = "";

    /// <summary>Per-session remembered filter keywords (LRU) surfaced as the
    /// filter box's AutoCompleteBox suggestions — see <see cref="KeywordSearchMemory"/>.</summary>
    private readonly KeywordSearchMemory _filterMemory;
    public ObservableCollection<string> FilterHistory => _filterMemory.History;

    public InterestingFunctionsViewModel(IDumpService dump, ILoggingService log,
                                          IAobMakerBridge? aobMaker = null,
                                          IPlatformService? platform = null)
    {
        _dump = dump;
        _log = log;
        _aobMaker = aobMaker;
        _platform = platform;
        _filterMemory = new KeywordSearchMemory(() => (FilterText, Results.Count > 0));
        ClassFilter = new ClassFacetFilter(ApplyFilter)
        {
            AutoDetectProvider = async names =>
                (await _dump.DetectNoiseClassesAsync(names))
                    .Select(n => (n.ClassName, n.IsNoise, n.Reason)).ToList(),
        };
    }

    /// <summary>Drop scored functions so a reconnect never shows rows (and live
    /// UClass* addresses) from the previous game (audit X5). Client-side only.</summary>
    public void ClearOnDisconnect()
    {
        _xrefBatchCts?.Cancel();
        _allRows = new List<ScoredFunctionRow>();
        _entries = Array.Empty<AllFunctionEntry>();
        _scoredWithGameplayActions = false;
        _lastScan = default;   // don't let a re-score quote the PREVIOUS game's scan (Z15)
        SelectedResult = null;
        Results.Clear();
        ClassFilter.Reset();
        ClassFilterNote = "";
        StatusText = "Click Load to scan all UFunctions";
    }

    /// <summary>
    /// Reverse edge: "Props" row action — list the properties this function
    /// reads/writes (static bytecode scan). Opens a self-contained dialog
    /// (no tab impact).
    /// </summary>
    [RelayCommand]
    private async Task FindFuncPropsAsync(ScoredFunctionRow? row)
    {
        row ??= SelectedResult;
        if (row == null || string.IsNullOrEmpty(row.FuncAddr)) return;
        try
        {
            await Views.FunctionPropsDialog.ShowForFunctionAsync(
                row.FuncName, row.FuncAddr, _dump, _platform);
        }
        catch (Exception ex)
        {
            _log.Error($"FindFuncProps failed for {row.FuncName}", ex);
        }
    }

    // Props direction is cheap (one function's own bytecode per call), so warn
    // only past a high row count.
    private CancellationTokenSource? _xrefBatchCts;
    private const int XrefBatchWarnThreshold = 100;

    /// <summary>
    /// Batch "Props": run walk_function_props for the selected rows (or all
    /// current results if none selected) and write a compact "N · name1, name2"
    /// summary into each row's inline <see cref="ScoredFunctionRow.XrefInfo"/>
    /// cell — so the user sees the (often 0) result inline instead of opening
    /// the modal per row. Cancellable; only class-member fields are counted.
    /// </summary>
    [RelayCommand]
    private async Task BatchFindFuncPropsAsync(IList<ScoredFunctionRow>? selected)
    {
        var targets = (selected != null && selected.Count > 0)
            ? selected.ToList()
            : Results.ToList();
        if (targets.Count == 0) { StatusText = "No rows to scan — Load first."; return; }

        if (targets.Count > XrefBatchWarnThreshold)
        {
            var ok = await Views.ConfirmDialog.ShowAsync(
                "Batch: properties used",
                $"Analyse {targets.Count} functions? Each reads one function's own "
              + "bytecode (fast). Tip: select fewer rows first (Ctrl/Shift+click) to scope it.",
                confirmText: "Run");
            if (!ok) return;
        }

        var oldCts = _xrefBatchCts;          // dispose the prior run's CTS (L14)
        _xrefBatchCts = new CancellationTokenSource();
        oldCts?.Cancel();
        oldCts?.Dispose();
        var ct = _xrefBatchCts.Token;
        IsXrefBatchRunning = true;
        int done = 0, withFields = 0, cached = 0;
        try
        {
            foreach (var row in targets)
            {
                ct.ThrowIfCancellationRequested();
                // Skip rows already scanned (XrefInfo persists across filter changes
                // since the row instance is reused) — re-batch after a new filter only
                // does the newly-included, not-yet-done rows.
                if (!string.IsNullOrEmpty(row.XrefInfo)) { cached++; continue; }
                if (string.IsNullOrEmpty(row.FuncAddr)) { row.XrefInfo = "—"; continue; }
                try
                {
                    var res = await _dump.WalkFunctionPropsAsync(row.FuncAddr, ct);
                    var fields = res.Props.Where(p => p.IsClassField).ToList();
                    if (fields.Count > 0)
                    {
                        withFields++;
                        var preview = string.Join(", ", fields.Take(2).Select(p => p.Name));
                        row.XrefInfo = fields.Count > 2 ? $"{fields.Count} · {preview}, …"
                                                        : $"{fields.Count} · {preview}";
                    }
                    else row.XrefInfo = "0";
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    row.XrefInfo = "—";
                    _log.Error($"Batch props failed for {row.FuncName}", ex);
                }
                done++;
                if ((done & 0x7) == 0)
                    StatusText = $"Props: {done}/{targets.Count} scanned ({withFields} use class fields)…";
            }
            StatusText = $"Props done: {withFields}/{targets.Count} use class fields"
                       + (cached > 0 ? $" ({cached} cached)." : ".");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            StatusText = $"Props batch cancelled at {done}/{targets.Count}.";
        }
        catch (Exception ex)
        {
            // NOT a cancel. PipeClient distinguishes three causes as of audit #5 AC10:
            // a caller cancel carries OUR ct (caught above), a deliberate teardown
            // carries the client's own token, and an unexpected pipe death — the game
            // crashing, the DLL unloading — arrives as an IOException. A bare
            // `catch (OperationCanceledException)` reported all of them as
            // "Props batch cancelled at N/M", so a dead game read as "you pressed
            // Cancel" and nothing was logged at all. (audit #5 Z5; audit #3's L14
            // applied at the two sites it missed.)
            _log.Error("Batch props failed", ex);
            StatusText = $"Props batch failed at {done}/{targets.Count}.";
        }
        finally { IsXrefBatchRunning = false; }
    }

    /// <summary>Cancel an in-flight batch Props run.</summary>
    [RelayCommand]
    private void CancelXrefBatch() => _xrefBatchCts?.Cancel();

    /// <summary>
    /// Per-row hint shown in the Notes column. Same value across every
    /// row in the current grid -- the column is per-row in the AXAML
    /// layout but the data is VM-level. When AOBMaker is unavailable
    /// the AA(B) shortcut still works (clipboard fallback) but the
    /// CE-side workflow is degraded; this surfaces that to the user
    /// without burying it in a tooltip.
    /// </summary>
    public string AobMakerNote => IsAobMakerAvailable
        ? ""
        : "AOBMaker plugin not found — AA Script export will fall back to clipboard";

    // Filter inputs all funnel into a single ApplyFilter pass. Each
    // partial method fires once per ObservableProperty change.
    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
        _filterMemory.Schedule(value);
    }
    partial void OnCategoryFilterChanged(FunctionCategory? value) => ApplyFilter();
    partial void OnShowAllChanged(bool value)              => ApplyFilter();
    partial void OnCallableOnlyChanged(bool value)        => ApplyFilter();
    // Gameplay-Action opt-in changes per-row scores + categories, so a full
    // re-score (not just a re-filter) is needed. Fire-and-forget: the scoring
    // runs on a worker thread to keep the toggle responsive on large games.
    partial void OnGameplayActionsChanged(bool value)     => _ = RescoreAsync();
    partial void OnIsAobMakerAvailableChanged(bool value)
        => OnPropertyChanged(nameof(AobMakerNote));

    /// <summary>
    /// Probe AOBMaker pipe and update <see cref="IsAobMakerAvailable"/>.
    /// Runs the bridge's CheckAvailabilityAsync (2s pipe-connect timeout
    /// when CE not running) so cheap to call on tab activation.
    /// </summary>
    public async Task CheckAobMakerAsync()
    {
        if (_aobMaker == null) { IsAobMakerAvailable = false; return; }
        _lastAobMakerCheck = DateTime.UtcNow;
        try
        {
            IsAobMakerAvailable = await _aobMaker.CheckAvailabilityAsync();
        }
        catch
        {
            IsAobMakerAvailable = false;
        }
    }

    /// <summary>
    /// Fire-and-forget AOBMaker availability check with cooldown so
    /// rapid tab switches don't stack 2s-blocking pipe-connect attempts.
    /// Called from MainWindow's tab-switch handler.
    /// </summary>
    public void TryCheckAobMaker()
    {
        if (_aobMaker == null) return;
        if (DateTime.UtcNow - _lastAobMakerCheck < AobMakerCheckCooldown) return;
        _ = CheckAobMakerAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            ClearError();
            IsLoading = true;
            StatusText = "Loading all UFunctions (this can take 2-10s on large games)...";

            var result = await _dump.ListAllFunctionsAsync(gameOnly: GameOnly);

            // Cache the raw entries so the Gameplay-Action opt-in can
            // re-score without re-fetching (see RescoreAsync).
            _entries = result.Functions;
            bool includeActions = GameplayActions;

            // Score on the worker thread to keep the UI responsive when
            // the result set is in the tens-of-thousands range.
            _scoredWithGameplayActions = includeActions;
            _allRows = await Task.Run(() => ScoreEntries(_entries, includeActions));

            // Keep the scan facts so RescoreAsync can rewrite the SAME status line
            // rather than leaving the previous scoring's numbers on screen. (Z15)
            _lastScan = new LoadScanFacts(result.Total, result.ScannedClasses,
                                          result.ScannedObjects, result.Truncated,
                                          result.Aborted, result.Limit);

            // Build the class histogram over the FULL scored set, then filter.
            // countsPartial: a capped/aborted walk makes the picker's per-class counts
            // a lower bound. Until Z8 there was no flag to pass here at all — the DLL
            // emitted no truncation marker for list_all_functions. (audit #5 Z4 + Z8)
            ClassFilter.Rebuild(_allRows.Select(r => r.ClassName),
                                countsPartial: _lastScan.IsPartial);
            ApplyFilter();

            StatusText = BuildStatusLine(_lastScan, CountAboveThreshold(_allRows));
            _log.Info($"ListAllFunctions: total={result.Total} classes={result.ScannedClasses} " +
                      $"interesting={CountAboveThreshold(_allRows)} (gameOnly={GameOnly}, " +
                      $"truncated={result.Truncated}, aborted={result.Aborted})");

            // Refresh AOBMaker state at end of load so the Notes column
            // reflects current connectivity. Fire-and-forget: this MUST NOT
            // be awaited. CheckAvailabilityAsync pays AobMakerBridgeService's
            // 2s pipe-connect timeout whenever CE isn't running -- the normal
            // state for this panel -- and awaiting it held IsLoading true for
            // those 2s AFTER the grid was already painted and the final status
            // written, so the panel looked idle while RescoreAsync's
            // `if (IsLoading ...) return;` guard silently swallowed every
            // Gameplay-Actions toggle. Every other AOBMaker probe in the UI is
            // fire-and-forget for the same reason; this was the lone `await`.
            // Dropping the Task is safe -- CheckAobMakerAsync swallows its own
            // exceptions into IsAobMakerAvailable = false.
            _ = CheckAobMakerAsync();
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Load failed";
            _log.Error("ListAllFunctions failed", ex);
        }
        finally
        {
            IsLoading = false;

            // Reconcile the toggle against what the rows were actually scored
            // with. Anything the user changed while IsLoading was true was
            // discarded by RescoreAsync's guard, and nothing else re-runs -- so
            // without this the grid stays scored for the OTHER mode forever and
            // the only recovery is untick+retick, which is not discoverable.
            //
            // Comparing the two values rather than latching a "something was
            // swallowed" bool is deliberate: it is direction-agnostic, and a
            // user who toggles twice inside the window lands back on the mode
            // already scored, which correctly does nothing.
            //
            // Fire-and-forget so the load command completes now; RescoreAsync
            // re-reads GameplayActions itself, so a further toggle is still
            // handled by the normal OnGameplayActionsChanged path. The task is
            // kept (rather than discarded into `_`) only so the VM tests can
            // await the reconciliation instead of calling RescoreAsync
            // themselves — calling it directly is the seam that let the
            // existing coverage pass straight through this defect.
            PendingRescore = (_entries.Count > 0 && GameplayActions != _scoredWithGameplayActions)
                ? RescoreAsync()
                : null;
        }
    }

    /// <summary>
    /// Score every entry with the given Gameplay-Action opt-in and return
    /// the rows sorted (score desc, then class, then func). Static + pure
    /// so both <see cref="LoadAsync"/> and the re-score-on-toggle path
    /// (<see cref="RescoreAsync"/>) share identical scoring + ordering.
    /// Intended to run on a worker thread (tens of thousands of rows).
    /// </summary>
    private static List<ScoredFunctionRow> ScoreEntries(
        IReadOnlyList<AllFunctionEntry> entries, bool includeGameplayActions)
    {
        var rows = new List<ScoredFunctionRow>(entries.Count);
        foreach (var entry in entries)
        {
            var s = KeywordScoringTable.Score(entry, includeGameplayActions);
            rows.Add(new ScoredFunctionRow
            {
                Entry       = entry,
                FinalScore  = s.FinalScore,
                Category    = s.Category,
                KeywordHits = s.KeywordHits,
                ClassBonus  = s.ClassBonus,
                FlagBonus   = s.FlagBonus,
            });
        }
        // Sort once after scoring; ApplyFilter preserves order without
        // re-sorting since it walks the pre-sorted list.
        rows.Sort((a, b) =>
        {
            int cmp = b.FinalScore.CompareTo(a.FinalScore);
            if (cmp != 0) return cmp;
            cmp = string.Compare(a.ClassName, b.ClassName, StringComparison.Ordinal);
            if (cmp != 0) return cmp;
            return string.Compare(a.FuncName, b.FuncName, StringComparison.Ordinal);
        });
        return rows;
    }

    /// <summary>
    /// Re-score the already-loaded entries with the current
    /// <see cref="GameplayActions"/> opt-in and rebuild the grid. Toggling
    /// the pack changes per-row scores + categories, so a full re-score is
    /// needed — but the entry set is unchanged, so the class-noise histogram
    /// is left intact (no re-fetch, no Rebuild). No-op before the first Load
    /// or while one is in flight (Load reads the current toggle itself).
    /// Internal so the VM tests can await a deterministic re-score.
    /// </summary>
    internal async Task RescoreAsync()
    {
        if (IsLoading || _entries.Count == 0) return;
        var entries = _entries;
        bool includeActions = GameplayActions;
        _scoredWithGameplayActions = includeActions;
        _allRows = await Task.Run(() => ScoreEntries(entries, includeActions));
        ApplyFilter();
        // Re-scoring MOVES the threshold count — that is the entire point of the
        // Gameplay-Actions pack — so the status line must follow it. It used to be
        // written only by LoadAsync, so a toggle re-scored every row and left the
        // panel reporting the PREVIOUS mode's "above threshold: N" underneath the new
        // grid. Distinct from Z1 (the re-score not running at all); this is the
        // re-score running and the report not following. (audit #5 Z15)
        StatusText = BuildStatusLine(_lastScan, CountAboveThreshold(_allRows));
    }

    // ── Status line (shared by LoadAsync and RescoreAsync) ───────────────────

    /// <summary>Scan facts from the last <see cref="LoadAsync"/>, kept so a re-score
    /// can rebuild the identical sentence with only the score-derived number changed.
    /// Default (all zeros) before the first load, where <see cref="BuildStatusLine"/>
    /// is never called.</summary>
    private LoadScanFacts _lastScan;

    /// <summary>The <c>list_all_functions</c> facts the status line quotes. A record
    /// struct, so a re-score cannot accidentally quote HALF of a newer scan.</summary>
    internal readonly record struct LoadScanFacts(
        int Total, int ScannedClasses, int ScannedObjects,
        bool Truncated, bool Aborted, int Limit)
    {
        /// <summary>The DLL walk stopped early — the row count is a page, not the pool.</summary>
        public bool IsPartial => Truncated || Aborted;
    }

    internal static int CountAboveThreshold(IReadOnlyList<ScoredFunctionRow> rows)
    {
        int n = 0;
        foreach (var r in rows)
            if (r.FinalScore >= KeywordScoringTable.InterestingThreshold) n++;
        return n;
    }

    /// <summary>
    /// The panel's one status sentence. Pure + internal so both writers share it
    /// verbatim (Z15) and so the truncation disclosure (Z8) is unit-testable without a
    /// pipe: the walk is capped at <see cref="LoadScanFacts.Limit"/> rows and emitted no
    /// marker at all before Z8, so "N functions across M classes" read as a complete
    /// census of the game.
    /// </summary>
    internal static string BuildStatusLine(LoadScanFacts f, int interesting)
    {
        var capSuffix = f.Aborted
            ? PartialResultNotice.Cancelled()
            : f.Truncated
                ? PartialResultNotice.RowCap(f.Limit, "functions",
                      "tick \"Game classes only\" to skip engine classes, or scan a narrower game")
                : "";
        return $"{f.Total:N0} functions across {f.ScannedClasses:N0} classes  " +
               $"({interesting:N0} above threshold {KeywordScoringTable.InterestingThreshold}, " +
               $"scanned {f.ScannedObjects:N0} objects){capSuffix}";
    }

    /// <summary>
    /// Rebuild <see cref="Results"/> from <see cref="_allRows"/> applying
    /// name + category + threshold filters. Order is preserved from the
    /// pre-sorted full list (Score desc, then Class, then Func) so the
    /// DataGrid doesn't need to re-sort.
    /// </summary>
    private void ApplyFilter()
    {
        SelectedResult = null;   // detach before rebuilding the selection-bound list
        Results.Clear();
        if (_allRows.Count == 0) return;

        // Space-separated terms are ANDed (each must hit FuncName or ClassName),
        // so "add money" narrows to entries matching both — the shared Object Tree
        // filter semantics. Cheat-table workflow: user usually remembers either
        // "AddMoney" or "PlayerInventory", and either term keeps both entry points.
        var terms      = ObjectTreeFilter.SplitTerms(FilterText);
        var cat        = CategoryFilter;
        var threshold  = ShowAll ? int.MinValue : KeywordScoringTable.InterestingThreshold;

        int hiddenByClass = 0;
        var callableOnly = CallableOnly;
        foreach (var row in _allRows)
        {
            if (row.FinalScore < threshold) continue;
            if (cat.HasValue && row.Category != cat.Value) continue;
            // BlueprintCallable/Exec filter — gameplay/control/shop entry points
            // are almost always one of these; hides native getter/plumbing noise.
            if (callableOnly && !(row.IsBlueprintCallable || row.IsExec)) continue;
            if (terms.Length > 0 &&
                !ObjectTreeFilter.MatchesAllTerms(terms, row.FuncName, row.ClassName))
            {
                continue;
            }
            // Class-noise exclusion last, so the count reflects rows that would
            // otherwise be visible.
            if (ClassFilter.IsExcluded(row.ClassName)) { hiddenByClass++; continue; }
            Results.Add(row);
        }
        ClassFilterNote = hiddenByClass > 0 ? $"{hiddenByClass} hidden by class filter" : "";
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _filterMemory.Flush();   // commit a just-typed keyword before clearing the box (AE16)
        FilterText      = "";
        CategoryFilter  = null;
        ShowAll         = false;
        CallableOnly    = false;
        // GameplayActions is a scoring MODE (re-scores the set), not a view
        // filter — left untouched so "Clear" doesn't silently re-score.
    }

    /// <summary>Per-row action: fire navigate event so MainWindow can
    /// try LiveWalker (with find_instance) or fall back to ClassStruct.</summary>
    [RelayCommand]
    private void OpenInLiveWalker(ScoredFunctionRow? row)
    {
        if (row == null) return;
        NavigateToFunction?.Invoke(row.ClassName, row.FuncName);
    }

    /// <summary>Per-row "inst" action: open this function's class in the
    /// Instance Finder tab (pre-fill the class name + run the search). Mirrors
    /// the Property Search / Value Search "inst" handoff.</summary>
    [RelayCommand]
    private void OpenInInstanceFinder(ScoredFunctionRow? row)
    {
        if (row == null || string.IsNullOrEmpty(row.ClassName)) return;
        NavigateToInstanceFinder?.Invoke(row.ClassName);
    }

    /// <summary>Locate a live instance of this function's class within the GWorld
    /// graph (parent mode — stops at the object that points to the instance).</summary>
    [RelayCommand]
    private void LocateRowInGWorld(ScoredFunctionRow? row)
    {
        if (row == null || string.IsNullOrEmpty(row.ClassName)) return;
        LocateInGWorld?.Invoke(row.ClassName);
    }

    /// <summary>Engine-rooted counterpart — not gated on IsGWorldAvailable (engine
    /// availability is independent of GWorld; the DLL reports no_engine via the banner).</summary>
    [RelayCommand]
    private void LocateRowInGameEngine(ScoredFunctionRow? row)
    {
        if (row == null || string.IsNullOrEmpty(row.ClassName)) return;
        LocateInGameEngine?.Invoke(row.ClassName);
    }

    /// <summary>Per-row action: shortcut into the Copy AA Script flow
    /// without going through LiveWalker first. MainWindow handler
    /// fetches the full FunctionInfoModel via walk_functions then
    /// opens InvokeParamDialog in CopyBakedScript mode.</summary>
    [RelayCommand]
    private void CopyAaScript(ScoredFunctionRow? row)
    {
        if (row == null) return;
        RequestCopyBakedScript?.Invoke(row.ClassName, row.FuncName, row.ClassAddr);
    }

    /// <summary>Per-row action: copy the function name (no class prefix)
    /// to the clipboard. MainWindow handler routes this through the
    /// platform service so AOT / clipboard fallback semantics live in
    /// one place. Bare name (not Class::Func) keeps it pasteable into
    /// CE script editors and grep workflows directly.</summary>
    [RelayCommand]
    private void CopyFunctionName(ScoredFunctionRow? row)
    {
        if (row == null) return;
        if (string.IsNullOrEmpty(row.FuncName)) return;
        RequestCopyText?.Invoke(row.FuncName);
        StatusText = $"Copied function name: {row.FuncName}";
    }

    // ------------------------------------------------------------------
    // Multi-row batch: turn the user's current DataGrid selection into a
    // single CT file (one baked-invoke entry per row, grouped by
    // Category). For functions with parameters the BakedValues list is
    // intentionally empty — the generated script's helper zero-fills the
    // PARAMS buffer, and the user edits the baked PARAMS table in CE
    // before activating. The script header comment guides them.
    // ------------------------------------------------------------------

    /// <summary>Build the CT batch from <paramref name="selected"/>.
    /// Public + static for direct test coverage (no need to stand up
    /// the full VM to verify the row mapping).</summary>
    public static List<CheatTableRow> BuildRowsFromSelection(
        IEnumerable<ScoredFunctionRow> selected)
    {
        var rows = new List<CheatTableRow>();
        foreach (var sr in selected)
        {
            if (sr is null) continue;
            if (string.IsNullOrEmpty(sr.ClassName) || string.IsNullOrEmpty(sr.FuncName))
                continue;

            string desc = sr.NumParms == 0
                ? $"{sr.ClassName}::{sr.FuncName}()"
                : $"{sr.ClassName}::{sr.FuncName}  ({sr.ParamsLabel})  -- edit baked PARAMS in CE";

            rows.Add(new CtFunctionRow
            {
                Category    = KeywordScoringTable.DisplayName(sr.Category),
                Description = desc,
                ClassName   = sr.ClassName,
                FuncName    = sr.FuncName,
                ParmsSize   = sr.ParmsSize,
                // No baked values for batch entries -- user fills the
                // PARAMS table in CE per-row before activating. The
                // helper zero-fills the buffer so the script is safe
                // to enable accidentally (worst case = func runs with
                // all-zero args).
                BakedValues = Array.Empty<BakedParamValue>(),
            });
        }
        return rows;
    }

    /// <summary>"Generate Cheat Table from Selection" command. Same
    /// shape as the Interesting Properties sibling: VM stays IO-free,
    /// MainWindow handles the save dialog.</summary>
    [RelayCommand]
    private void GenerateCheatTable(IList<ScoredFunctionRow>? selected)
    {
        if (selected is null || selected.Count == 0)
        {
            StatusText = "Select 2+ rows first (Ctrl/Shift+click).";
            return;
        }

        var rows = BuildRowsFromSelection(selected);
        if (rows.Count == 0)
        {
            StatusText = "Selection produced 0 valid rows — nothing to write.";
            return;
        }

        var now = DateTime.Now;
        string title = $"UE5CEDumper Interesting Functions — " +
                       $"{rows.Count} row{(rows.Count == 1 ? "" : "s")} " +
                       $"@ {now:yyyy-MM-dd HH:mm}";
        string ct = CheatTableBuilder.Build(title, rows);
        string defaultName = CheatTableBuilder.DefaultFileName(
            processName: "InterestingFunctions", now);

        StatusText = $"Generated {rows.Count} invoke entries " +
                     "(edit baked PARAMS in CE for funcs with arguments).";
        RequestSaveCheatTable?.Invoke(defaultName, ct);
    }
}
