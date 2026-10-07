using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
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
    /// <summary>The save dialog Save .jsonl asks for a path; null in hosts without one, where Save does nothing.</summary>
    private readonly IPlatformService? _platform;

    /// <summary>[EXTPR-539-540-2026-10-02] How many rows a fetch asks the DLL for, as a power of two (the
    /// slider's position). The DLL ranks the recording by call count and sends the top rows, so a small limit
    /// cuts exactly the low-count functions this panel is for; the user raises it when the status says rows
    /// were cut. The panel's slider has the same bounds; a value from ui-options.json is clamped here.</summary>
    internal const int FetchLimitMinExponent = 6;
    internal const int FetchLimitMaxExponent = 15;
    [ObservableProperty] private int _fetchLimitExponent = 9;

    /// <summary>The fetch limit itself, 2^<see cref="FetchLimitExponent"/>.</summary>
    public int FetchLimit => 1 << FetchLimitExponent;

    /// <summary>[EXTPR-539-540-2026-10-02] L3: hide rows with fewer calls than 2^<see cref="MinCallsExponent"/>.
    /// Read at Start and kept for that capture, so the rows on screen never change under a moved slider (the
    /// maintainer's choice over filtering at once). Only the view is filtered: SetBaseline reads every fetched row,
    /// or an idle function with few calls would be missing from the baseline and come back as a false NEW.</summary>
    internal const int MinCallsMinExponent = 0;
    internal const int MinCallsMaxExponent = 5;
    [ObservableProperty] private int _minCallsExponent;

    /// <summary>The minimum itself, 2^<see cref="MinCallsExponent"/>.</summary>
    public int MinCalls => 1 << MinCallsExponent;

    /// <summary>The minimum the running or last capture was started with, and the one the rows on screen were
    /// fetched under. They differ between a Start and its first fetch, and stay apart when a recording ends without
    /// one (leaving the tab, a disconnect, a failed Stop): the rows on screen keep their own minimum.</summary>
    private int _captureMinCalls = 1;
    private int _shownMinCalls = 1;

    /// <summary>The lowest call count on the last page. Every row a higher fetch limit would add has at most this
    /// many calls, so when it is below Min calls the added rows would all be hidden.</summary>
    private long _lastPageMinCount;

    /// <summary>The limit a running recording fetches with, fixed at Start: a peek and Stop's own fetch then
    /// rank the same table the same way whatever happens to the slider meanwhile (it is disabled while
    /// recording, but a value can still reach the property).</summary>
    private int _recordingFetchLimit;

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
    /// (pipe-protocol.md), so <c>shown &lt; distinct</c> — less the per-frame functions left out on request and the
    /// ones the DLL had no name for — is a conservative, correct test for "not everything is on screen" — it is also
    /// true when an older DLL dropped the functions unloaded since they fired (build 3634 sends them, named) or a
    /// cooperative abort cut the emit loop short, and all of these mean the same thing to the user.</summary>
    private int _lastShown;
    private int _lastDistinct;
    private long _lastTotalCalls;
    /// <summary>[TRACE-UNLOADED-NAMES] The last fetch's whole-table counts: functions unloaded since they fired (sent,
    /// named from their first call) and functions with no name at all (never sent, so not "cut" by the limit). 0 from
    /// a DLL older than the counts.</summary>
    private int _lastUnloaded;
    private int _lastUnnamed;
    internal int LastUnloadedFuncs => _lastUnloaded;
    internal int LastUnnamedFuncs => _lastUnnamed;
    /// <summary>Whether the DLL was still recording when the rows on screen were fetched (a peek).</summary>
    private bool _lastRecordingAtFetch;

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

    /// <summary>The limit the last fetch asked for, and whether the baseline's page was cut by ITS limit and at
    /// which value. An incomplete page is not always the cap's doing: the DLL leaves out functions it has no name
    /// for (and, before build 3634, every one unloaded since it fired; all still counted in distinct_funcs), and an
    /// abort can cut the emit loop. Only a page that came back as long as the limit was cut by it, and only then
    /// does a higher limit bring rows back.</summary>
    private int  _lastLimit;
    private bool _baselineCapHit;
    private int  _baselineLimit;

    /// <summary>True when the last fetch did not show every recorded function it was asked for: the per-frame ones
    /// the DLL left out on request are not missing, and neither are the ones it had no name to send for.</summary>
    private bool LastTruncated => _lastShown < _lastDistinct - _lastPerFrameHidden - _lastUnnamed;

    /// <summary>The last page was cut by the fetch limit itself (see <see cref="_lastLimit"/>).</summary>
    private bool LastCapHit => LastTruncated && _lastShown >= _lastLimit;

    private static bool BelowMaximum(int limit) => limit < (1 << FetchLimitMaxExponent);

    /// <summary>[EXTPR-539-540-2026-10-02] Would a higher Fetch limit show what is missing? For the last page,
    /// or for the baseline in diff mode (which then has to be recorded again). Picks the remedy the status
    /// lines offer.</summary>
    internal bool RaiseFetchLimitHelps =>
        LastPageRaiseHelps
        || (_baseline.Count > 0 && _baselineCapHit && BelowMaximum(_baselineLimit));

    /// <summary>The last page's part of <see cref="RaiseFetchLimitHelps"/>. The baseline's part ignores Min calls,
    /// because SetBaseline reads every fetched row.</summary>
    private bool LastPageRaiseHelps =>
        LastCapHit && BelowMaximum(_lastLimit) && _lastPageMinCount >= _shownMinCalls;

    [ObservableProperty] private bool   _isRecording;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _statusText = "Click Start, do an in-game action (open shop / dash), then Stop.";
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private ObservableCollection<PeProfileEntry> _results = new();
    /// <summary>Keeps the Results grid's selection and scroll position across filter edits
    /// [KEYWORD-BOX-VIEW-KEEP]; the panel attaches the grid to it. Keyed on the UFunction
    /// address because every fetch makes new row objects for the same functions.</summary>
    public FilterViewKeeper ResultsView { get; } = new()
    {
        KeyOf = o => o is PeProfileEntry e && !string.IsNullOrEmpty(e.FuncAddr) ? e.FuncAddr : o,
    };
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

    /// <summary>[LIVEFUNCS-HIDE-PERFRAME] Ask the DLL to leave out the functions that fire every frame through the
    /// recording, BEFORE the fetch limit, so its rows go to the low-count functions this panel is for (a filter here
    /// could not bring back what the limit cut). Opt-in; fixed at Start like the fetch limit.</summary>
    [ObservableProperty] private bool   _hidePerFrame;

    /// <summary>The option a running recording fetches with, fixed at Start (see <see cref="_recordingFetchLimit"/>).</summary>
    private bool _recordingHidePerFrame;

    /// <summary>Whether the rows on screen were asked for without the per-frame functions, whether the DLL did it
    /// (one older than the option answers no count and leaves nothing out), and how many it left out. Those are no
    /// rows the limit cut, so the cut is counted without them.</summary>
    private bool _lastPerFrameAsked;
    private bool _lastPerFrameEffective;
    private int  _lastPerFrameHidden;
    /// <summary>Whether the baseline's page had the per-frame functions left out: against a page fetched the other
    /// way, every one of them reads NEW, or is missing.</summary>
    private bool _baselinePerFrameEffective;

    /// <summary>Which functions the last page and the baseline's page left out as per-frame, by address. The DLL
    /// decides per recording, so a Tick left out of an idle baseline comes back in an action that paused the game for
    /// a menu; with no baseline row it would read NEW, at the top of the list the baseline exists to clean.</summary>
    private HashSet<string> _lastPerFrameAddrs = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _baselinePerFrameAddrs = new(StringComparer.OrdinalIgnoreCase);

    internal int LastPerFrameHidden => _lastPerFrameHidden;
    internal bool PerFrameUnsupported => _lastPerFrameAsked && !_lastPerFrameEffective;
    internal bool BaselinePerFrameMismatch => _baseline.Count > 0 && _baselinePerFrameEffective != _lastPerFrameEffective;

    /// <summary>What the status line adds about the option: the count left out, or that this DLL cannot.</summary>
    private string PerFrameNote() =>
        PerFrameUnsupported ? Res.Get("str.LF.PerFrame.Unsupported")
        : _lastPerFrameEffective ? Res.Format("str.LF.PerFrame.Hidden", _lastPerFrameHidden)
        : "";

    /// <summary>[TRACE-UNLOADED-NAMES] What the status line adds about functions no longer at their address.</summary>
    private string UnloadedNote() =>
        (_lastUnloaded > 0 ? Res.Format("str.LF.Unloaded.Note", _lastUnloaded) : "")
        + (_lastUnnamed > 0 ? Res.Format("str.LF.Unnamed.Note", _lastUnnamed) : "");
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

    /// <summary>
    /// Raised by the per-row "Name" action: ask the host to put <c>text</c> on the clipboard. Returns whether it ACTUALLY
    /// arrived, so the raiser can decide what to claim.
    ///
    /// <para><b>Why this is <c>Func&lt;string, Task&lt;bool&gt;&gt;</c> and not
    /// <c>Action&lt;string&gt;</c>.</b> As an <c>Action</c> the handler was an async
    /// lambda, i.e. effectively <c>async void</c>: <c>Invoke</c> returned at the first
    /// <c>await</c>, so the caller's <c>StatusText = "Copied ..."</c> ran BEFORE the copy
    /// was even attempted, and the bool the handler eventually got had nowhere to go. That
    /// is strictly worse than the call-site cases the same sweep found -- there the result
    /// at least existed at the moment of the claim (blind-spot sweep round 3, sub-shape
    /// (b)). MainWindowViewModel's own comment said it out loud: "Status text already set
    /// by the VM."</para>
    ///
    /// <para>⚠ A multicast <c>Func</c> returns only the LAST handler's value. Each of
    /// these events is wired exactly once, in <c>MainWindowViewModel</c>; a second
    /// subscriber would silently decide the answer for everyone.</para>
    /// </summary>
    public event Func<string, Task<bool>>? RequestCopyText;

    /// <summary>[AOBMAKER-EVAL-2026-09-29] The shared AOBMaker availability the ASM button reads. Never null:
    /// without a bridge it simply stays unavailable.</summary>
    public Helpers.AobMakerStatus AobMaker { get; }

    // ---- [LIVEFUNCS-TIMELINE-2026-10-04] The call trace: armed by this panel's Start, read in the Call Trace tab.
    // Experimental (T6): the controls show, and a Start asks for a trace, only while the experimental tabs are on.
    // The plan and its decisions: docs/live-funcs-timeline-plan.md.

    private readonly IExperimentalGate? _experimentalGate;
    public bool TraceAvailable => _experimentalGate?.IsEnabled ?? false;

    /// <summary>The trace buffer as a power of two in MB (T1): 32 to 512. How much the game can spare is the user's
    /// call; the estimate beside the slider says how long it would last.</summary>
    internal const int TraceBufferMinExponent = 5;
    internal const int TraceBufferMaxExponent = 9;
    [ObservableProperty] private bool _traceEnabled;
    [ObservableProperty] private int _traceBufferExponent = 6;
    /// <summary>T5 (b): leave out the functions the previous recording found firing every frame.</summary>
    [ObservableProperty] private bool _traceExcludePerFrame;
    public int TraceBufferMb => 1 << TraceBufferExponent;
    public string TraceBufferText => Res.Format("str.LF.Trace.BufferMb", TraceBufferMb);

    /// <summary>Two 40-byte records per call: its entry and its return.</summary>
    internal const int TraceBytesPerCall = 80;
    /// <summary>The last fetch's calls per second over the window its table covers; 0 before any.</summary>
    private double _lastCallsPerSecond;
    internal double LastCallsPerSecond => _lastCallsPerSecond;

    /// <summary>Seconds a buffer of <paramref name="bytes"/> keeps when every call is traced at
    /// <paramref name="callsPerSecond"/>; ticked functions and the per-frame exclusion make it last longer.</summary>
    internal static double EstimateSeconds(long bytes, double callsPerSecond)
        => callsPerSecond <= 0 ? 0 : bytes / (callsPerSecond * TraceBytesPerCall);

    public string TraceEstimate
    {
        get
        {
            if (_lastCallsPerSecond <= 0) return Res.Get("str.LF.Trace.EstimateNone");
            double s = EstimateSeconds((long)TraceBufferMb << 20, _lastCallsPerSecond);
            string span = s < 120 ? Res.Format("str.LF.Trace.Seconds", Math.Round(s))
                                  : Res.Format("str.LF.Trace.Minutes", Math.Round(s / 60, 1));
            return Res.Format("str.LF.Trace.Estimate", span, Math.Round(_lastCallsPerSecond));
        }
    }

    // [TRACE-UI-LOAD-MEMORY] D3: what a buffer that fills costs, from the buffer alone, so it shows before any
    // recording. The game commits the whole ring at Start and holds it until this UI has read it. While this UI loads
    // it holds the window (the ring's own size), the trace's columns (73 of every 80 bytes a call takes in the ring)
    // and the tree's state, plus one page's reply in flight; after the load, only the columns and the tree. 2 x N is
    // that structure; the extra quarter is what the live check of 2026-10-07 (build 3636, Avowed) needed for "up to"
    // to hold: a full 128 MB load peaked 303 MB over its start, a full 512 MB one 1,206 MB over a freshly started UI.
    internal const double TraceUiPeakFactor = 2.25;
    internal const int    TraceUiPageMb     = 45;
    public int TraceGameMb   => TraceBufferMb;
    public int TraceUiPeakMb => (int)(TraceBufferMb * TraceUiPeakFactor) + TraceUiPageMb;
    public int TraceUiHeldMb => TraceBufferMb;
    /// <summary>Physical memory free when last asked (MB); long.MaxValue when unknown.</summary>
    private long _availableMb = long.MaxValue;
    /// <summary>D3: above the memory free now, the estimate is a warning; Start still runs.</summary>
    public bool TraceMemoryOverAvailable => _availableMb != long.MaxValue && TraceGameMb + TraceUiPeakMb > _availableMb;
    public string TraceMemoryEstimate => Res.Format(
        TraceMemoryOverAvailable ? "str.LF.Trace.MemoryOver" : "str.LF.Trace.Memory",
        MemText(TraceGameMb), MemText(TraceUiPeakMb), MemText(TraceUiHeldMb), MemText(_availableMb));

    private static string MemText(long mb) => mb < 1024 ? Res.Format("str.LF.Trace.BufferMb", mb)
                                                        : Res.Format("str.LF.Trace.Gb", mb / 1024.0);

    /// <summary>Read the free memory again: when the slider moves, when Trace is ticked, when the tab is shown, when
    /// the experimental tabs change, and at Start -- memory moves while the slider waits (a game launched after the
    /// UI takes most of it; review INT-4).</summary>
    private void RefreshAvailableMemory()
    {
        long bytes = _platform?.GetAvailablePhysicalMemoryBytes() ?? long.MaxValue;
        _availableMb = bytes == long.MaxValue ? long.MaxValue : bytes >> 20;
        OnPropertyChanged(nameof(TraceMemoryOverAvailable));
        OnPropertyChanged(nameof(TraceMemoryEstimate));
    }

    partial void OnTraceEnabledChanged(bool value) => RefreshAvailableMemory();

    /// <summary>The ticked functions, keyed by Class::Func (stable across fetches) with every address the last fetch
    /// saw under that name, which is what the DLL matches on: a class is named by its short name, so two classes in
    /// different folders can share a key, and a tick by name traces both. An address is good only within the
    /// connection that fetched it: a disconnect clears the ticks, and the rows left on screen cannot be ticked until a
    /// fetch replaces them.</summary>
    private readonly Dictionary<string, HashSet<string>> _ticked = new(StringComparer.Ordinal);
    /// <summary>The rows on screen came from a connection that has since dropped: their addresses belong to a process
    /// that may be gone, so they cannot be ticked and are nothing to tick for T7.</summary>
    private bool _rowsFromEarlierConnection;
    /// <summary>The ticked functions as Class::Func, for this panel and the Call Trace tab's read-only copy (T8).</summary>
    public ObservableCollection<string> TickedFunctions { get; } = new();
    /// <summary>Whether the rows on screen can be ticked: not while recording (a recording traces the ticks it started
    /// with), and not when they came from an earlier connection.</summary>
    public bool CanTick => !IsRecording && !_rowsFromEarlierConnection;
    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(CanTick));
    public bool HasTickedFunctions => TickedFunctions.Count > 0;
    public string TickedCountText => Res.Format("str.LF.Trace.TickedCount", TickedFunctions.Count);

    /// <summary>T7: asked before a traced Start with nothing ticked while there are rows to tick from. The view sets
    /// it; without one a Start that needs the question does not start.</summary>
    public Func<Task<bool>>? ConfirmTraceAllCalls { get; set; }
    /// <summary>T7: answered yes once in this session, so a later Start with nothing ticked runs at once. Cancel
    /// does not count as asked.</summary>
    private bool _traceAllConfirmed;

    /// <summary>[TRACE-UNLOADED-NAMES] Ticks the DLL left out at the last traced Start: unloaded since the fetch that
    /// showed them (review UI-1).</summary>
    internal int LastTickedDropped { get; private set; }

    /// <summary>The running recording asked for a trace (fixed at Start).</summary>
    private bool _recordingTrace;
    /// <summary>The trace's state after the last traced recording stopped; null before one.</summary>
    public TraceInfo? LastTraceInfo { get; private set; }
    /// <summary>A traced recording stopped and its trace waits in the DLL: offer the Call Trace tab.</summary>
    [ObservableProperty] private bool _hasTraceToOpen;
    /// <summary>Raised by "Open in Call Trace"; the main window switches tabs and loads the trace.</summary>
    public event Action? NavigateToCallTrace;

    public LiveFuncsViewModel(IDumpService dump, ILoggingService log, IPlatformService? platform = null,
                              Helpers.AobMakerStatus? aobMaker = null, IExperimentalGate? experimentalGate = null)
    {
        _dump = dump;
        _log = log;
        _platform = platform;
        AobMaker = aobMaker ?? new Helpers.AobMakerStatus(null);
        _filterMemory = new KeywordSearchMemory(() => (FilterText, Results.Count > 0));
        _experimentalGate = experimentalGate;
        if (_experimentalGate != null)
            _experimentalGate.Changed += (_, _) => { OnPropertyChanged(nameof(TraceAvailable)); RefreshAvailableMemory(); };
        RefreshAvailableMemory();
    }

    partial void OnTraceBufferExponentChanged(int value)
    {
        int clamped = Math.Clamp(value, TraceBufferMinExponent, TraceBufferMaxExponent);
        if (clamped != value)
        {
            TraceBufferExponent = clamped;   // re-enters with the clamped value
            return;
        }
        OnPropertyChanged(nameof(TraceBufferMb));
        OnPropertyChanged(nameof(TraceBufferText));
        OnPropertyChanged(nameof(TraceEstimate));
        OnPropertyChanged(nameof(TraceGameMb));
        OnPropertyChanged(nameof(TraceUiPeakMb));
        OnPropertyChanged(nameof(TraceUiHeldMb));
        RefreshAvailableMemory();
    }

    /// <summary>Tick or untick a row for the trace. Not while recording: the ticks a recording traces are the ones
    /// it started with.</summary>
    [RelayCommand]
    private void ToggleTick(PeProfileEntry? row)
    {
        // [TRACE-UNLOADED-NAMES] An unloaded row's address is dead: it never ticks, and a live row of the same name
        // sends only the live addresses.
        if (row == null || !CanTick || row.IsUnloaded || string.IsNullOrEmpty(row.FuncAddr)) return;
        string key = Key(row);
        var same = _allEntries.Where(e => Key(e) == key && !e.IsUnloaded && !string.IsNullOrEmpty(e.FuncAddr)).ToList();
        if (!same.Contains(row)) same.Add(row);
        bool tick = !_ticked.Remove(key);
        if (tick) _ticked[key] = new HashSet<string>(same.Select(e => e.FuncAddr), StringComparer.OrdinalIgnoreCase);
        foreach (var e in same) e.IsTicked = tick;
        RefreshTickedList();
    }

    [RelayCommand]
    private void ClearTicks()
    {
        if (IsRecording) return;
        _ticked.Clear();
        foreach (var e in _allEntries) e.IsTicked = false;
        RefreshTickedList();
    }

    private void RefreshTickedList()
    {
        TickedFunctions.Clear();
        foreach (var k in _ticked.Keys.OrderBy(k => k, StringComparer.Ordinal)) TickedFunctions.Add(k);
        OnPropertyChanged(nameof(HasTickedFunctions));
        OnPropertyChanged(nameof(TickedCountText));
    }

    [RelayCommand]
    private void OpenCallTrace() => NavigateToCallTrace?.Invoke();

    /// <summary>What a Start asks of the trace, or null for a Start without one. Null too when the Start must not
    /// run, with <paramref name="refusal"/> set to the status string that says why.</summary>
    private async Task<TraceStartOptions?> TraceOptionsForStartAsync(Ref<string?> refusal)
    {
        if (!TraceAvailable || !TraceEnabled) return null;
        var ticked = _ticked.Values.SelectMany(a => a).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // [TRACE-UNLOADED-NAMES] Ticked, but every ticked function was unloaded since it fired: no address to scope
        // on. Not T7's case -- the user did tick -- and never a trace of every call, the opposite of what was asked.
        if (ticked.Count == 0 && _ticked.Count > 0)
        {
            refusal.Value = "str.LF.Trace.TickedAllUnloaded";
            return null;
        }
        // T7: only when there is something to tick. The first recording, or any Start with no row that can be ticked
        // (none, or only unloaded ones), records every call without asking.
        if (ticked.Count == 0 && _allEntries.Any(e => !e.IsUnloaded) && !_rowsFromEarlierConnection
            && !_traceAllConfirmed)
        {
            var confirm = ConfirmTraceAllCalls;
            if (confirm == null || !await confirm())
            {
                refusal.Value = "str.LF.Trace.StartCancelled";
                return null;
            }
            _traceAllConfirmed = true;
        }
        return new TraceStartOptions
        {
            Bytes = (long)TraceBufferMb << 20,
            Ticked = ticked,
            ExcludePerFrame = TraceExcludePerFrame,
        };
    }

    /// <summary>A box for an out-value an async method can set.</summary>
    private sealed class Ref<T> { public T Value = default!; }

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

    partial void OnFetchLimitExponentChanged(int value)
    {
        int clamped = Math.Clamp(value, FetchLimitMinExponent, FetchLimitMaxExponent);
        if (clamped != value)
        {
            FetchLimitExponent = clamped;   // re-enters with the clamped value, which raises FetchLimit
            return;
        }
        OnPropertyChanged(nameof(FetchLimit));
    }

    partial void OnMinCallsExponentChanged(int value)
    {
        int clamped = Math.Clamp(value, MinCallsMinExponent, MinCallsMaxExponent);
        if (clamped != value)
        {
            MinCallsExponent = clamped;   // re-enters with the clamped value, which raises MinCalls
            return;
        }
        OnPropertyChanged(nameof(MinCalls));
    }

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
        _baselineDistinct  = _lastDistinct - _lastPerFrameHidden;   // what it could have fetched; see LastTruncated
        _baselineCapHit    = LastCapHit;
        _baselineLimit     = _lastLimit;
        _baselinePerFrameEffective = _lastPerFrameEffective;
        _baselinePerFrameAddrs = new(_lastPerFrameAddrs, StringComparer.OrdinalIgnoreCase);
        // OnDiffModeChanged re-applies the diff only when DiffMode CHANGES; with diff already on, a new baseline
        // would leave every row's Delta / IsNew against the old one.
        if (DiffMode) ApplyDiffAndFilter();
        else DiffMode = true;   // OnDiffModeChanged applies it
        BaselineStatus = _baselineTruncated
            ? $"⚠ PARTIAL baseline: {_baseline.Count:N0} of {_baselineDistinct:N0} idle funcs "
              + "(the rest were not fetched). A row can show as NEW just for having "
              + "been below the cut — treat NEW as \"not in the idle top N\"."
              + (_baselineCapHit && BelowMaximum(_baselineLimit) ? " " + Res.Get("str.LF.Cap.BaselineRemedy") : "")
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
        _baselineCapHit    = false;
        _baselineLimit     = 0;
        _baselinePerFrameEffective = false;
        _baselinePerFrameAddrs = new(StringComparer.OrdinalIgnoreCase);
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
            var refusal = new Ref<string?>();
            var trace = await TraceOptionsForStartAsync(refusal);
            if (refusal.Value != null)
            {
                StatusText = Res.Get(refusal.Value);
                return;
            }
            if (trace != null) RefreshAvailableMemory();
            // Any Start gives up the previous trace: the DLL frees it before it tries a new buffer, so even a refused
            // Start leaves nothing to open (review DLL-4).
            HasTraceToOpen = false;
            LastTraceInfo = null;
            var start = trace == null ? await _dump.PeProfileStartAsync() : await _dump.PeProfileStartAsync(trace);
            _recordingFetchLimit = FetchLimit;
            _recordingHidePerFrame = HidePerFrame;
            _captureMinCalls = MinCalls;
            // Traced only when the DLL says it armed the trace: an older one ignores the request and records plain.
            _recordingTrace = trace != null && start.Trace != null;
            IsRecording = true;
            StatusText = start.HookActive
                ? "Recording… ALT-TAB to the game, perform the action (open shop / dash), then click Stop."
                : string.IsNullOrEmpty(start.Detail)
                    ? "No PE hook — counts stay 0. Change to another map/scene and Start again."
                    : start.Detail;   // self-contained reason from the DLL
            if (trace != null && start.HookActive)
            {
                StatusText += " " + (start.Trace == null ? Res.Get("str.LF.Trace.NotArmed")
                    : trace.Ticked.Count > 0
                        ? Res.Format("str.LF.Trace.RecordingTicked", TraceBufferMb, trace.Ticked.Count)
                        : Res.Format("str.LF.Trace.RecordingAll", TraceBufferMb));
                LastTickedDropped = start.Trace?.TickedDropped ?? 0;
                if (LastTickedDropped > 0)
                    StatusText += " " + Res.Format("str.LF.Trace.TickedDropped", LastTickedDropped);
                // D3: a warning, never a refusal -- the memory is the user's call (T1).
                if (start.Trace != null && TraceMemoryOverAvailable)
                    StatusText += " " + Res.Format("str.LF.Trace.MemoryWarnStart", MemText(_availableMb));
            }
            _log.Info($"LivePEProfiler: start (hook_active={start.HookActive}, trace={(trace == null ? "off" : $"{TraceBufferMb} MB, {trace.Ticked.Count} ticked, exclude_per_frame={trace.ExcludePerFrame}")})");
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
        bool traced = _recordingTrace;
        try
        {
            ClearError();
            IsBusy = true;
            var traceInfo = await _dump.PeProfileStopWithTraceAsync();
            _recordingTrace = false;
            NoteStoppedTrace(traced, traceInfo);
            await FetchAndPopulateAsync();
            if (traced) StatusText += " " + TraceStopNote();
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Stop failed";
            _log.Error("LivePEProfiler stop failed", ex);
        }
        // Clear the recording UI state even if the stop round-trip threw, so a failed
        // Stop doesn't leave the tab stuck "recording" (which would swallow Start). (L16)
        finally { IsBusy = false; IsRecording = false; _recordingTrace = false; }
    }

    /// <summary>What a stopped traced recording left in the DLL; offers the Call Trace tab when there is something
    /// to read.</summary>
    private void NoteStoppedTrace(bool traced, TraceInfo? info)
    {
        if (!traced) return;
        LastTraceInfo = info;
        HasTraceToOpen = info is { Allocated: true, Quiesced: true } && info.Written > 0;
    }

    private string TraceStopNote()
    {
        var i = LastTraceInfo;
        // No trace object at all is a DLL without the trace. One that wrote nothing is reported empty, and the DLL has
        // already given its ring back (review DLL-5), so it reads as not allocated.
        if (i == null) return Res.Get("str.LF.Trace.NoneKept");
        if (i.Written == 0) return Res.Get("str.LF.Trace.Empty");
        if (!i.Quiesced) return Res.Get("str.LF.Trace.NotQuiesced");
        if (!i.Allocated) return Res.Get("str.LF.Trace.NoneKept");
        return i.FirstValid > 0
            ? Res.Format("str.LF.Trace.KeptLast", i.Kept, i.Written)
            : Res.Format("str.LF.Trace.KeptAll", i.Kept);
    }

    /// <summary>[EXTPR-539-540-2026-10-02] L2: save the rows on screen (what the filter, the check boxes and Min calls
    /// leave) to a JSON Lines file, in the order the game first called them (<see cref="Helpers.LiveFuncsJsonl"/>). Not while
    /// recording, when the table is still changing; the button is disabled then too. A peek's rows can outlive the
    /// recording (it can end without a final fetch), so the summary says when the rows came from one.</summary>
    [RelayCommand]
    private async Task SaveJsonlAsync()
    {
        if (IsRecording) return;
        if (_platform == null)
        {
            _log.Warn("LivePEProfiler: Save .jsonl has no save dialog in this host");
            return;
        }
        if (Results.Count == 0)
        {
            StatusText = Res.Get("str.LF.Save.Empty");
            return;
        }
        // Taken before the dialog: the rows saved are the rows on screen when Save was pressed.
        var rows = Results.ToList();
        bool diff = DiffMode && _baseline.Count > 0;
        var summary = new Helpers.LiveFuncsJsonl.Summary(
            rows.Count, _allEntries.Count, _lastDistinct, _lastTotalCalls, _lastLimit, _lastRecordingAtFetch,
            FilterText ?? "", HideWidgets, HideEvents, PeriodicOnly, _shownMinCalls,
            diff, NewChangedOnly, diff ? _baseline.Count : 0, diff && _baselineTruncated, diff ? _baselineDistinct : 0,
            DateTime.UtcNow, _lastPerFrameAsked, _lastPerFrameEffective ? _lastPerFrameHidden : null,
            diff && _baselinePerFrameEffective);
        try
        {
            ClearError();
            string defaultName = "live-funcs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".jsonl";
            string? path = await _platform.ShowSaveFileDialogAsync(defaultName, Res.Get("str.LF.Save.FileType"), ".jsonl");
            if (string.IsNullOrEmpty(path)) return;
            await File.WriteAllTextAsync(path, Helpers.LiveFuncsJsonl.Format(summary, rows),
                                         new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            StatusText = Res.Format("str.LF.Save.Done", rows.Count, path);
            _log.Info($"LivePEProfiler: saved {rows.Count} row(s) to {path}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = Res.Format("str.LF.Save.Failed", ex.Message);
            _log.Error("LivePEProfiler save failed", ex);
        }
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
        int limit = IsRecording ? _recordingFetchLimit : FetchLimit;
        bool skipPerFrame = IsRecording ? _recordingHidePerFrame : HidePerFrame;
        var result = await _dump.PeProfileGetAsync(limit, skipPerFrame);
        _lastLimit    = limit;
        _lastPerFrameAsked     = skipPerFrame;
        _lastPerFrameEffective = skipPerFrame && result.PerFrameHidden.HasValue;
        _lastPerFrameHidden    = _lastPerFrameEffective ? result.PerFrameHidden!.Value : 0;
        _lastPerFrameAddrs     = new(_lastPerFrameEffective ? result.PerFrameFuncs : Array.Empty<string>(),
                                     StringComparer.OrdinalIgnoreCase);
        _allEntries   = result.Entries;
        if (_rowsFromEarlierConnection)
        {
            _rowsFromEarlierConnection = false;
            OnPropertyChanged(nameof(CanTick));
        }
        // The ticks are kept by name; the new rows carry them, and their addresses are the ones the DLL matches on. A
        // name this page does not show keeps the addresses it had. A name whose rows here are all unloaded keeps its
        // tick with no address: theirs are dead, and the tick follows the function if a later fetch finds it loaded.
        var refreshed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var e in _allEntries)
        {
            string k = Key(e);
            if (!_ticked.ContainsKey(k)) continue;
            if (e.IsUnloaded)
            {
                if (!refreshed.ContainsKey(k)) refreshed[k] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            e.IsTicked = true;
            if (string.IsNullOrEmpty(e.FuncAddr)) continue;
            if (!refreshed.TryGetValue(k, out var addrs))
                refreshed[k] = addrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            addrs.Add(e.FuncAddr);
        }
        foreach (var kv in refreshed) _ticked[kv.Key] = kv.Value;
        if (result.WindowMs is > 0 && result.TotalCalls > 0)
        {
            _lastCallsPerSecond = result.TotalCalls / (result.WindowMs.Value / 1000.0);
            OnPropertyChanged(nameof(TraceEstimate));
        }
        _lastShown    = result.Entries.Count;
        _lastDistinct = result.DistinctFuncs;
        _lastTotalCalls = result.TotalCalls;
        _lastUnloaded = result.UnloadedFuncs ?? 0;
        _lastUnnamed  = result.UnnamedFuncs ?? 0;
        _lastRecordingAtFetch = result.Recording;
        _lastPageMinCount = result.Entries.Count > 0 ? result.Entries.Min(e => e.Count) : 0;
        _shownMinCalls = _captureMinCalls;   // before the filter runs over the new rows
        ApplyDiffAndFilter();

        // House convention for surfacing a cap (SnapshotViewModel / SpcQueryViewModel).
        // Spelled out rather than "(capped at N)" because WHICH rows were cut is the
        // point here: the DLL keeps the highest counts, and the function this panel is
        // for has a low one.
        string trunc = LastTruncated
            ? $" (showing top {_lastShown:N0} of {_lastDistinct - _lastPerFrameHidden:N0} by count"
              + (LastPageRaiseHelps ? Res.Get("str.LF.Cap.MoreRows") : "") + ")"
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
            // A NEW row Min calls hid is counted above but not on screen, so the claim that the action's function
            // is among the NEW rows shown would point at a table that does not hold it.
            bool newRowHidden = _allEntries.Any(e => e.IsNew && e.Count < _shownMinCalls);
            // newCount/increased are counted over the PAGE, so they cannot be reported
            // against the pre-cap table size — "3 NEW of 900" invited reading 900 as the
            // population those 3 were selected from, when only the fetched page was examined.
            StatusText = $"vs baseline: {newCount} NEW + {increased} increased "
              + $"(of {_lastShown:N0} shown; {_lastDistinct:N0} recorded{PerFrameNote()}{UnloadedNote()}). "
              + (BaselinePerFrameMismatch ? Res.Get("str.LF.PerFrame.BaselineMismatch") + " " : "")
              + (_baselineTruncated || LastTruncated
                  ? "⚠ Capped fetch: NEW means \"not in the idle top N\", not \"did not fire while "
                    + "idle\" — a rare idle function below the cut also shows as NEW. "
                    + (RaiseFetchLimitHelps ? Res.Get("str.LF.Cap.DiffRaise") + " "
                       : LastCapHit || _baselineCapHit ? Res.Get("str.LF.Cap.DiffShorter") + " " : "")
                    + "The filter narrows only the rows already fetched."
                  : newRowHidden || BaselinePerFrameMismatch ? "" : "The action's function is almost certainly among the NEW rows at the top.");
        }
        else
        {
            StatusText = $"{result.DistinctFuncs:N0} distinct functions, {result.TotalCalls:N0} total calls"
              + PerFrameNote()
              + UnloadedNote()
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
                else if (_baselinePerFrameAddrs.Contains(e.FuncAddr))
                {
                    // Fired every frame while idle and was left out of the baseline: not new, and its idle count is
                    // unknown, so no increase is claimed either.
                    e.IsNew = false;
                    e.Delta = 0;
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
        var terms = ObjectTreeFilter.SplitTerms(FilterText);
        bool diffNewOnly = DiffMode && _baseline.Count > 0 && NewChangedOnly;
        var rows = new List<PeProfileEntry>();
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
            // Min calls the rows were fetched under. No exemption for NEW rows (R1): the default 1 hides nothing.
            if (e.Count < _shownMinCalls) continue;
            if (terms.Length > 0 &&
                !ObjectTreeFilter.MatchesAllTerms(terms, e.FuncName, e.ClassName))
            {
                continue;
            }
            rows.Add(e);
        }
        // Detach before rebuilding the selection-bound list; unchanged rows are not rebuilt.
        ResultsView.Update(Results, rows, () => SelectedResult = null, FilterText);
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
    private async Task CopyFuncNameAsync(PeProfileEntry? row)
    {
        if (row == null || string.IsNullOrEmpty(row.FuncName)) return;
        var handler = RequestCopyText;
        bool copied = handler is not null && await handler(row.FuncName);
        StatusText = copied
            ? $"Copied function name: {row.FuncName}"
            : $"Could not copy '{row.FuncName}' -- the clipboard refused the write.";
    }

    /// <summary>[AOBM-FUNC-DISASM] The function that fired, in CE's disassembler, plus a record to right-click.</summary>
    [RelayCommand]
    private async Task AsmFuncAsync(PeProfileEntry? row)
    {
        // [TRACE-UNLOADED-NAMES] An unloaded row's address is dead: nothing to disassemble, nothing to push to CE.
        if (row == null || row.IsUnloaded || string.IsNullOrEmpty(row.FuncAddr)) return;
        StatusText = await Helpers.AobMakerActions.DisassembleFunctionAsync(AobMaker, _dump, row.FuncAddr, row.FuncName, _log);
    }

    /// <summary>Called when the Live Funcs tab is shown: the trace slider's memory line reads the free memory again.</summary>
    public void OnEnteringTab() => RefreshAvailableMemory();

    /// <summary>Called when the user navigates away from the Live Funcs tab. Flushes
    /// the keyword memory and auto-stops any live recording so a forgotten session
    /// doesn't keep the game thread taking the profile mutex on every PE call.</summary>
    public void OnLeavingTab()
    {
        _filterMemory.Flush();
        if (IsRecording) PendingAutoStop = AutoStopOnLeaveAsync();
    }

    /// <summary>[LIVEFUNCS-TIMELINE-2026-10-04] The stop leaving the tab started; the Call Trace tab, opened by that
    /// same tab switch, waits for it before asking the DLL for the trace the stop finishes.</summary>
    public Task PendingAutoStop { get; private set; } = Task.CompletedTask;

    private async Task AutoStopOnLeaveAsync()
    {
        // Clear the UI state up-front (before the round-trip) so returning to the tab and
        // clicking Start inside the stop window isn't swallowed by StartAsync's guard. (L16)
        IsRecording = false;
        bool traced = _recordingTrace;
        _recordingTrace = false;
        bool stopped = false;
        try
        {
            NoteStoppedTrace(traced, await _dump.PeProfileStopWithTraceAsync());
            stopped = true;
        }
        catch (Exception ex) { _log.Error("LivePEProfiler auto-stop failed", ex); }
        StatusText = "Recording auto-stopped (left the tab). Re-open and Refresh to see counts.";
        // Only a stop that went through says anything about this recording's trace.
        if (traced && stopped) StatusText += " " + TraceStopNote();
    }

    /// <summary>Reset the recording UI state on pipe disconnect. The DLL (Linie) already
    /// drops its recording on last-client-gone; this keeps the UI in sync so a reconnect
    /// doesn't show a stuck "recording" that swallows the next Start. (L16)</summary>
    public void ResetOnDisconnect()
    {
        if (IsRecording) IsRecording = false;
        // [LIVEFUNCS-TIMELINE-2026-10-04] The DLL dropped the trace with the client, and the ticks' addresses belong
        // to the process that is gone; so does the call rate the estimate came from.
        _recordingTrace = false;
        HasTraceToOpen = false;
        LastTraceInfo = null;
        _ticked.Clear();
        foreach (var e in _allEntries) e.IsTicked = false;
        _rowsFromEarlierConnection = _allEntries.Count > 0;
        OnPropertyChanged(nameof(CanTick));
        RefreshTickedList();
        _lastCallsPerSecond = 0;
        OnPropertyChanged(nameof(TraceEstimate));
    }
}
