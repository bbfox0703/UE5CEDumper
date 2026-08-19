using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Live Funcs panel — the Live ProcessEvent Call Profiler.
///
/// Behaviour-based UFunction discovery (the root-cause answer to "which function
/// does this game call to open the shop / dash?", which name heuristics can't):
/// 1. Start → DLL forces the game-thread ProcessEvent hook up and begins counting
///    every UFunction the game dispatches.
/// 2. The user ALT-TABs to the game and performs the action (open shop, dash).
/// 3. Stop → freezes the table; the VM immediately fetches + ranks it by fire count.
///
/// The ranked rows hand off to Live Walker (open the function on a live instance)
/// exactly like the Interesting Functions finder, so the discovered function can be
/// invoked right away.
/// </summary>
public partial class LiveFuncsViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;

    /// <summary>How many top rows to fetch from the DLL (ranked by fire count).</summary>
    private const int FetchLimit = 300;

    /// <summary>Full unfiltered result set — the filter rebuilds <see cref="Results"/> from this.</summary>
    private List<PeProfileEntry> _allEntries = new();

    /// <summary>Baseline fire counts keyed by "ClassName::FuncName" (stable across GC,
    /// unlike a transient instance address). Empty = no baseline captured. When Diff
    /// mode is on, each fetch is compared against this to surface action-specific
    /// functions (new / increased) instead of per-frame Tick noise.</summary>
    private Dictionary<string, long> _baseline = new();

    /// <summary>Rows the DLL actually sent for the last fetch, and the distinct count it
    /// recorded BEFORE the cap. The DLL sorts the whole table by count desc and emits only
    /// the first <see cref="FetchLimit"/> rows, while <c>distinct_funcs</c> stays pre-cap
    /// (pipe-protocol.md), so <c>shown &lt; distinct</c> is a conservative, correct test for
    /// "not everything is on screen" — it is also true when stale UFunction pointers were
    /// dropped or a cooperative abort cut the emit loop short, and all three mean the same
    /// thing to the user.</summary>
    private int _lastShown;
    private int _lastDistinct;

    /// <summary>Was the page the baseline was captured from truncated, and how big was the
    /// table it came from? This matters more than it looks: the DLL's cap keeps the HIGHEST
    /// counts, and this panel exists to find a function with a LOW one. A baseline taken
    /// from a capped page is missing exactly the rare idle functions, so on the action fetch
    /// they miss <c>_baseline</c>, get flagged IsNew, sort to the very top and survive the
    /// default New/changed-only filter — the tool then presents fabricated rows as the
    /// answer. We still allow it (refusing would disable Diff on precisely the busy games it
    /// is for), but NEW then means "not in the idle top N", not "did not fire while idle",
    /// and both status lines have to say so.</summary>
    private bool _baselineTruncated;
    private int  _baselineDistinct;

    /// <summary>True when the last fetch did not show every recorded function.</summary>
    private bool LastTruncated => _lastShown < _lastDistinct;

    [ObservableProperty] private bool   _isRecording;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _statusText = "Click Start, do an in-game action (open shop / dash), then Stop.";
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private ObservableCollection<PeProfileEntry> _results = new();
    [ObservableProperty] private PeProfileEntry? _selectedResult;
    /// <summary>When on (and a baseline exists), show Δ vs baseline and rank
    /// new/increased functions to the top instead of ranking by raw call count.</summary>
    [ObservableProperty] private bool   _diffMode;
    /// <summary>In diff mode: show ONLY functions that are new or fired more than the
    /// baseline — the action-specific set. Off shows the full diff.</summary>
    [ObservableProperty] private bool   _newChangedOnly = true;
    /// <summary>Hide functions whose owning class is a UI widget (UUserWidget-derived).
    /// A widget's own methods all fire on creation, flooding a shop/menu diff — the
    /// opener you want lives on a persistent controller/subsystem, not the widget.</summary>
    [ObservableProperty] private bool   _hideWidgets;
    /// <summary>Hide event handlers / delegate signatures (On*/callbacks — FUNC_Event /
    /// FUNC_Delegate). Those are reactions the engine fires AT the game, not imperative
    /// functions you'd invoke; hiding them leaves the callable entry points.</summary>
    [ObservableProperty] private bool   _hideEvents;
    /// <summary>Show ONLY functions firing at a regular timer-like cadence (Phase E):
    /// enough fires, a low coefficient-of-variation, and a period outside the per-frame
    /// (Tick) band. The behaviour-based way to find the callback that drives a cooldown /
    /// spawn / DoT — record while IDLE (the inverse of the action-diff flow). Native
    /// C++/lambda timers bypass ProcessEvent and never appear, so this is an aid.</summary>
    [ObservableProperty] private bool   _periodicOnly;
    /// <summary>Sort by call-stream order (earliest first fire at the top) instead of by
    /// count/diff. The causal ordering: an action's entry point fires before the reactions
    /// it triggers, so combined with New/changed-only this floats the true opener to the top.</summary>
    [ObservableProperty] private bool   _earliestFirst;
    [ObservableProperty] private string _baselineStatus = "No baseline — record idle, then Set Baseline.";

    /// <summary>Per-session remembered filter keywords (LRU) surfaced as the filter
    /// box's AutoCompleteBox suggestions — see <see cref="KeywordSearchMemory"/>.
    /// The match count is client-side and synchronous, so Schedule (not Commit) is
    /// the correct hook.</summary>
    private readonly KeywordSearchMemory _filterMemory;
    public ObservableCollection<string> FilterHistory => _filterMemory.History;

    /// <summary>Raised by the per-row "Live" action so MainWindow can open the
    /// function in Live Walker (find a live non-CDO instance, else Class Struct).
    /// Payload = (className, funcName). Mirrors InterestingFunctionsViewModel.</summary>
    public event Action<string, string>? NavigateToFunction;

    /// <summary>Raised by the per-row "Name" action; MainWindow routes it through
    /// the platform clipboard so this VM stays free of IPlatformService.</summary>
    public event Action<string>? RequestCopyText;

    public LiveFuncsViewModel(IDumpService dump, ILoggingService log, IPlatformService? platform = null)
    {
        _dump = dump;
        _log = log;
        _filterMemory = new KeywordSearchMemory(() => (FilterText, Results.Count > 0));
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
        _filterMemory.Schedule(value);
    }
    partial void OnDiffModeChanged(bool value) => ApplyDiffAndFilter();
    partial void OnEarliestFirstChanged(bool value) => ApplyDiffAndFilter();
    partial void OnNewChangedOnlyChanged(bool value) => ApplyFilter();
    partial void OnHideWidgetsChanged(bool value) => ApplyFilter();
    partial void OnHideEventsChanged(bool value) => ApplyFilter();
    partial void OnPeriodicOnlyChanged(bool value) => ApplyFilter();

    private static string Key(PeProfileEntry e) => $"{e.ClassName}::{e.FuncName}";

    /// <summary>Capture the current results as the baseline (idle reference) and turn
    /// on Diff mode. The next recording is then shown as new/increased vs this.</summary>
    [RelayCommand]
    private void SetBaseline()
    {
        if (_allEntries.Count == 0)
        {
            StatusText = "Record an idle window first (Start → wait → Stop), then Set Baseline.";
            return;
        }
        // Max, not First: _allEntries is re-sorted in place by ApplyDiffAndFilter, and the
        // Earliest-first toggle orders it by FirstSeq rather than by count — so First() made
        // the captured baseline value depend on a VIEW setting. Max is what First() was
        // reaching for and is independent of however the grid happens to be sorted.
        _baseline = _allEntries.GroupBy(Key).ToDictionary(g => g.Key, g => g.Max(x => x.Count));
        _baselineTruncated = LastTruncated;
        _baselineDistinct  = _lastDistinct;
        DiffMode = true;   // triggers ApplyDiffAndFilter via OnDiffModeChanged
        BaselineStatus = _baselineTruncated
            ? $"⚠ PARTIAL baseline: {_baseline.Count:N0} of {_baselineDistinct:N0} idle funcs "
              + "(the rest ranked below the fetch cap). A row can show as NEW just for having "
              + "been below the cut — treat NEW as \"not in the idle top N\"."
            : $"Baseline: {_baseline.Count} funcs. Now record the ACTION — new/increased rows float to the top.";
        StatusText = "Baseline set. Start → perform the action (open shop) → Stop.";
    }

    /// <summary>Drop the baseline and leave diff mode (back to raw count ranking).</summary>
    [RelayCommand]
    private void ClearBaseline()
    {
        _baseline = new();
        _baselineTruncated = false;
        _baselineDistinct  = 0;
        DiffMode = false;  // triggers ApplyDiffAndFilter
        BaselineStatus = "No baseline — record idle, then Set Baseline.";
    }

    /// <summary>Start recording. Forces the game-thread PE hook up first; if it
    /// couldn't install (vtable detection failed on this game), warns that counts
    /// will stay 0.</summary>
    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsRecording) return;
        try
        {
            ClearError();
            IsBusy = true;
            var start = await _dump.PeProfileStartAsync();
            IsRecording = true;
            StatusText = start.HookActive
                ? "Recording… ALT-TAB to the game, perform the action (open shop / dash), then click Stop."
                : string.IsNullOrEmpty(start.Detail)
                    ? "No PE hook — counts stay 0. Change to another map/scene and Start again."
                    : start.Detail;   // self-contained reason from the DLL
            _log.Info($"LivePEProfiler: start (hook_active={start.HookActive})");
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Start failed";
            _log.Error("LivePEProfiler start failed", ex);
        }
        finally { IsBusy = false; }
    }

    /// <summary>Stop recording, then immediately fetch + rank the fire-count table.</summary>
    [RelayCommand]
    private async Task StopAsync()
    {
        if (!IsRecording) return;
        try
        {
            ClearError();
            IsBusy = true;
            await _dump.PeProfileStopAsync();
            await FetchAndPopulateAsync();
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Stop failed";
            _log.Error("LivePEProfiler stop failed", ex);
        }
        // Clear the recording UI state even if the stop round-trip threw, so a failed
        // Stop doesn't leave the tab stuck "recording" (which would swallow Start). (L16)
        finally { IsBusy = false; IsRecording = false; }
    }

    /// <summary>Re-fetch the current table without stopping — a live peek while
    /// recording, or a re-pull after Stop.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            ClearError();
            IsBusy = true;
            await FetchAndPopulateAsync();
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Refresh failed";
            _log.Error("LivePEProfiler refresh failed", ex);
        }
        finally { IsBusy = false; }
    }

    private async Task FetchAndPopulateAsync()
    {
        var result = await _dump.PeProfileGetAsync(FetchLimit);
        _allEntries   = result.Entries;
        _lastShown    = result.Entries.Count;
        _lastDistinct = result.DistinctFuncs;
        ApplyDiffAndFilter();

        // House convention for surfacing a cap (SnapshotViewModel / SpcQueryViewModel).
        // Spelled out rather than "(capped at 300)" because WHICH rows were cut is the
        // point here: the DLL keeps the highest counts, and the function this panel is
        // for has a low one.
        string trunc = LastTruncated
            ? $" (showing top {_lastShown:N0} of {_lastDistinct:N0} by count)"
            : "";

        bool diff = DiffMode && _baseline.Count > 0;
        if (result.DistinctFuncs == 0)
        {
            StatusText = "No UFunctions recorded. Was the game running (unpaused) during the window? "
              + "If it stayed 0, the PE hook may not be installed on this game.";
        }
        else if (diff)
        {
            int newCount = _allEntries.Count(e => e.IsNew);
            int increased = _allEntries.Count(e => !e.IsNew && e.Delta > 0);
            // newCount/increased are counted over the PAGE, so they cannot be reported
            // against the pre-cap table size — "3 NEW of 900" invited reading 900 as the
            // population those 3 were selected from, when only 300 were ever examined.
            StatusText = $"vs baseline: {newCount} NEW + {increased} increased "
              + $"(of {_lastShown:N0} shown; {_lastDistinct:N0} recorded). "
              + (_baselineTruncated || LastTruncated
                  ? "⚠ Capped fetch: NEW means \"not in the idle top N\", not \"did not fire while "
                    + "idle\" — a rare idle function below the cut also shows as NEW. Narrow the "
                    + "window or use the filter before trusting the top rows."
                  : "The action's function is almost certainly among the NEW rows at the top.");
        }
        else
        {
            StatusText = $"{result.DistinctFuncs:N0} distinct functions, {result.TotalCalls:N0} total calls"
              + trunc
              + (result.Recording ? " (still recording)" : "")
              + ". Tip: Set Baseline on an idle window, then re-record to isolate the action.";
        }
    }

    /// <summary>Compute per-row Δ vs the baseline (when Diff mode is on) + order the
    /// full set, then rebuild the filtered view. In diff mode, NEW functions rank
    /// first, then biggest increase, then rarest — so the action-specific function
    /// (which didn't fire while idle) surfaces at the top instead of drowning under
    /// per-frame Tick noise. Off, rows keep the DLL's count-desc ranking.</summary>
    private void ApplyDiffAndFilter()
    {
        bool diff = DiffMode && _baseline.Count > 0;
        foreach (var e in _allEntries)
        {
            if (diff)
            {
                if (_baseline.TryGetValue(Key(e), out var baseCount))
                {
                    e.IsNew = false;
                    e.Delta = e.Count - baseCount;
                }
                else { e.IsNew = true; e.Delta = e.Count; }
            }
            else { e.IsNew = false; e.Delta = 0; }
        }

        if (EarliestFirst)
        {
            // Causal order: earliest first-fire on top (FirstSeq 0 = unknown → sink last).
            _allEntries = _allEntries
                .OrderBy(e => e.FirstSeq <= 0 ? long.MaxValue : e.FirstSeq)
                .ToList();
        }
        else if (diff)
        {
            _allEntries = _allEntries.OrderByDescending(e => e.IsNew)
                                     .ThenByDescending(e => e.Delta)
                                     .ThenBy(e => e.Count)
                                     .ToList();
        }
        else
        {
            _allEntries = _allEntries.OrderByDescending(e => e.Count).ToList();
        }

        ApplyFilter();
    }

    /// <summary>Clear the fetched results + filter (does not touch a live recording).</summary>
    [RelayCommand]
    private void Clear()
    {
        _allEntries = new();
        // The tab-leave path (OnLeavingTab) already flushes; the Clear BUTTON did not,
        // and it is the one reachable while the user is still looking at the matches
        // their keyword produced. (audit #5 AE16, sibling site)
        _filterMemory.Flush();
        FilterText = "";
        Results.Clear();
        SelectedResult = null;
        StatusText = "Cleared.";
    }

    /// <summary>Rebuild <see cref="Results"/> from <see cref="_allEntries"/> applying
    /// the name filter (space = AND over func + class name, the shared filter
    /// semantics). Order is preserved (the DLL already ranked by count desc).</summary>
    private void ApplyFilter()
    {
        SelectedResult = null;
        Results.Clear();
        if (_allEntries.Count == 0) return;

        var terms = ObjectTreeFilter.SplitTerms(FilterText);
        bool diffNewOnly = DiffMode && _baseline.Count > 0 && NewChangedOnly;
        foreach (var e in _allEntries)
        {
            // In diff mode, "New/changed only" hides the unchanged baseline noise.
            if (diffNewOnly && !(e.IsNew || e.Delta > 0)) continue;
            // Hide transient UI-widget methods so the persistent opener surfaces.
            if (HideWidgets && e.IsWidget) continue;
            // Hide event/delegate reactions so imperative callables surface.
            if (HideEvents && e.IsEventLike) continue;
            // Periodic-only: keep just the regular timer-like cadence functions.
            if (PeriodicOnly && !e.IsPeriodic) continue;
            if (terms.Length > 0 &&
                !ObjectTreeFilter.MatchesAllTerms(terms, e.FuncName, e.ClassName))
            {
                continue;
            }
            Results.Add(e);
        }
    }

    /// <summary>Per-row "Live" action: open this function on a live instance of its
    /// class in Live Walker (MainWindow does the find_instance → walk handoff).</summary>
    [RelayCommand]
    private void OpenInLiveWalker(PeProfileEntry? row)
    {
        if (row == null || string.IsNullOrEmpty(row.ClassName)) return;
        NavigateToFunction?.Invoke(row.ClassName, row.FuncName);
    }

    /// <summary>Per-row "Name" action: copy the function name to the clipboard.</summary>
    [RelayCommand]
    private void CopyFuncName(PeProfileEntry? row)
    {
        if (row == null || string.IsNullOrEmpty(row.FuncName)) return;
        RequestCopyText?.Invoke(row.FuncName);
        StatusText = $"Copied function name: {row.FuncName}";
    }

    /// <summary>Called when the user navigates away from the Live Funcs tab. Flushes
    /// the keyword memory and auto-stops any live recording so a forgotten session
    /// doesn't keep the game thread taking the profile mutex on every PE call.</summary>
    public void OnLeavingTab()
    {
        _filterMemory.Flush();
        if (IsRecording) _ = AutoStopOnLeaveAsync();
    }

    private async Task AutoStopOnLeaveAsync()
    {
        // Clear the UI state up-front (before the round-trip) so returning to the tab and
        // clicking Start inside the stop window isn't swallowed by StartAsync's guard. (L16)
        IsRecording = false;
        try { await _dump.PeProfileStopAsync(); }
        catch (Exception ex) { _log.Error("LivePEProfiler auto-stop failed", ex); }
        StatusText = "Recording auto-stopped (left the tab). Re-open and Refresh to see counts.";
    }

    /// <summary>Reset the recording UI state on pipe disconnect. The DLL (Linie) already
    /// drops its recording on last-client-gone; this keeps the UI in sync so a reconnect
    /// doesn't show a stuck "recording" that swallows the next Start. (L16)</summary>
    public void ResetOnDisconnect()
    {
        if (IsRecording) IsRecording = false;
    }
}
