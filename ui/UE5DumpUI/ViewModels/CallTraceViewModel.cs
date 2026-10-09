using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// [LIVEFUNCS-TIMELINE-2026-10-04] The Call Trace tab (experimental). It records nothing itself: a traced Live Funcs
/// recording fills the DLL's ring, and this tab reads it after Stop, names what it saw, frees the DLL's memory, and
/// shows the calls as a tree (T2, T3, T8). The plan and its decisions: docs/live-funcs-timeline-plan.md.
/// </summary>
public partial class CallTraceViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService? _platform;

    /// <summary>The panel the trace's settings and ticks belong to; this tab shows a read-only copy of them (T8).</summary>
    public LiveFuncsViewModel LiveFuncs { get; }

    /// <summary>[TRACE-UI-LOAD-MEMORY] Records a page asks for. A page arrives as ONE pipe line, and StreamReader keeps
    /// its pooled line buffers per thread: on lines of 14 million chars (262,144 records as base64) it kept 205 MB after
    /// a full collection, on lines of 1.75 million chars nothing (measured 2026-10-07). 32,768 records is 1.3 MB of
    /// records, 1.75 million chars; a 512 MB ring is about 410 pages.</summary>
    internal const int PageRecords = 32768;
    internal const int NamesPage = 20000;
    /// <summary>[LIVEFUNCS-STEP2] Slots a snapshot page asks for; the DLL also ends a page at about 1 MB of JSON.</summary>
    internal const int SnapPage = 4096;
    /// <summary>[TRACE-UI-LOAD-MEMORY] Each page leaves the pipe's line and its parsed document behind (~5 MB for a full
    /// page), and nothing collects them during a load of hundreds of pages: live on build 3634 a full 128 MB load still
    /// peaked at 1.27 GB working set. A collection at least every this many pages -- about 21 MB of records -- keeps
    /// them to ~85 MB; it takes milliseconds, the heap being a few large arrays of plain values. The slider's estimate
    /// was calibrated on this period (build 3636): with less memory free a load collects more often, never less.</summary>
    internal const int CollectEveryPages = 16;
    /// <summary>What a full page leaves behind until a collection (MB): its pipe line, 1.75 million chars as UTF-16,
    /// and its parsed document.</summary>
    internal const int PageGarbageMb = 5;
    /// <summary>The pages' garbage between two collections may take this share of the free physical memory.</summary>
    internal const int FreeMemoryShare = 32;

    /// <summary>[TRACE-UI-LOAD-MEMORY] The pages a load reads before it collects, from the physical memory free now (the
    /// maintainer, 2026-10-07): 1/FreeMemoryShare of it for the pages' garbage, between one page and CollectEveryPages.
    /// Free memory that cannot be read comes as long.MaxValue, so it keeps the calibrated period.</summary>
    internal static int PagesBetweenCollections(long availableBytes)
    {
        long budgetMb = Math.Max(0L, availableBytes >> 20) / FreeMemoryShare;
        return (int)Math.Clamp(budgetMb / PageGarbageMb, 1L, CollectEveryPages);
    }

    /// <summary>The collections the last load ran while it read.</summary>
    internal int CollectionsDuringLastLoad { get; private set; }
    // The last load's memory, for its log line: at its start, and the largest working set seen after a page; the time
    // its collections took and the shortest period the free memory asked for -- what the next live check weighs.
    private long _loadStartWs, _loadStartHeap, _loadPeakWs, _collectTicks;
    private int _loadMinEvery = CollectEveryPages;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private IList _rows = Array.Empty<CallTraceRow>();
    [ObservableProperty] private int _selectedIndex = -1;
    [ObservableProperty] private string _detailText = "";
    [ObservableProperty] private bool _hasTrace;
    /// <summary>The rows are the filter's matches, not the tree: "Show in tree" takes the selected one back.</summary>
    [ObservableProperty] private bool _isFiltered;

    private CallTrace? _trace;
    private CallTraceTree? _tree;
    private int[] _matches = Array.Empty<int>();
    private ulong _loadedGen;
    private CancellationTokenSource? _loadCts;
    /// <summary>An export is writing a file. Its own flag: an export that shared IsLoading cleared it under a running
    /// load, which re-enabled Load and started a second reader on the same ring.</summary>
    [ObservableProperty] private bool _isExporting;
    public bool CanExport => HasTrace && !IsLoading && !IsExporting;
    /// <summary>[LIVEFUNCS-STEP2] The parameters CSV has rows only when the trace took copies. A trace replaces the one
    /// on screen through HasTrace false (a load drops the shown trace first), so HasTrace's change announces this.</summary>
    public bool CanExportParams => CanExport && _trace?.Snapshots != null;
    public bool CanLoad => !IsLoading && !IsExporting;
    /// <summary>The DLL has a newer recording than the trace on screen, which could not be read (it kept no calls, or
    /// its stop did not quiesce): what is shown is not what the last recording traced.</summary>
    [ObservableProperty] private bool _shownIsOlder;
    partial void OnIsLoadingChanged(bool value) => OnExportStateChanged();
    partial void OnIsExportingChanged(bool value) => OnExportStateChanged();
    partial void OnHasTraceChanged(bool value) => OnExportStateChanged();
    private void OnExportStateChanged()
    {
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanExportParams));
        OnPropertyChanged(nameof(CanLoad));
    }
    /// <summary>An activation waiting on Live Funcs' stop and the probe: leaving the tab cancels it before a load starts.</summary>
    private CancellationTokenSource? _activationCts;

    private readonly KeywordSearchMemory _filterMemory;
    public ObservableCollection<string> FilterHistory => _filterMemory.History;

    // ---- the list's widths ([LIVEFUNCS-STEP2] U10): dragged from the header, shared by the header and every row ----

    // Each width's floor keeps its header wide enough to grab and drag back: a column dragged, or hand-edited in
    // ui-options.json, down to nothing would leave no edge to pull it out by.
    internal const double MinTimeColWidth = 40, MinDurationColWidth = 40, MinThreadColWidth = 32,
                          MinObjectColWidth = 80, MinDetailPaneWidth = 200;
    /// <summary>The ceiling for every width: a hand-edited value of millions of pixels would lay the panel out far
    /// past any screen.</summary>
    internal const double MaxWidth = 4096;
    /// <summary>The fixed layout the tab had before its widths could be dragged: where they start, and what the list's
    /// floor is made of.</summary>
    internal const double DefaultTimeColWidth = 96, DefaultDurationColWidth = 88, DefaultThreadColWidth = 64,
                          DefaultObjectColWidth = 260, DefaultDetailPaneWidth = 380;
    /// <summary>[CT-DETAIL-COVERS-LIST] What Function keeps of a row before the columns give way: a root row's expand
    /// glyph and about twenty characters of its name in Consolas 12, still a dozen beside its (p) and (s) marks.</summary>
    internal const double MinFunctionWidth = 160;
    /// <summary>A row is narrower than the list by its item's padding, Fluent's 12 on each side.</summary>
    internal const double RowChrome = 24;
    /// <summary>[CT-DETAIL-COVERS-LIST] The list's floor, 512: a row with Time, Duration and Thread at their default
    /// widths, Object at its floor and Function's slice, so a call can still be told and picked. The pane is never
    /// shown so wide that the list has less: a remembered or dragged width covered the whole list, its rows and the
    /// pane's own handle.</summary>
    internal const double MinListWidth = RowChrome + DefaultTimeColWidth + DefaultDurationColWidth
                                         + DefaultThreadColWidth + MinObjectColWidth + MinFunctionWidth;

    // The remembered widths: what the user dragged, saved in ui-options.json. A layout never writes them, so a width
    // the panel cannot show now comes back when it can.
    private double _timeColWidth = DefaultTimeColWidth, _durationColWidth = DefaultDurationColWidth,
                   _threadColWidth = DefaultThreadColWidth, _objectColWidth = DefaultObjectColWidth,
                   _detailPaneWidth = DefaultDetailPaneWidth;
    // [CT-DETAIL-COVERS-LIST] The widths the panel shows: the remembered ones fitted to its room (Refit).
    private double _shownTime = DefaultTimeColWidth, _shownDuration = DefaultDurationColWidth,
                   _shownThread = DefaultThreadColWidth, _shownObject = DefaultObjectColWidth,
                   _shownPane = DefaultDetailPaneWidth;
    private double _splitWidth = double.NaN;

    public double TimeColWidth
    {
        get => _timeColWidth;
        set { if (SetProperty(ref _timeColWidth, ClampWidth(value, MinTimeColWidth))) Refit(); }
    }
    public double DurationColWidth
    {
        get => _durationColWidth;
        set { if (SetProperty(ref _durationColWidth, ClampWidth(value, MinDurationColWidth))) Refit(); }
    }
    public double ThreadColWidth
    {
        get => _threadColWidth;
        set { if (SetProperty(ref _threadColWidth, ClampWidth(value, MinThreadColWidth))) Refit(); }
    }
    /// <summary>Docked at the right of each row: Function takes what the fixed columns leave.</summary>
    public double ObjectColWidth
    {
        get => _objectColWidth;
        set { if (SetProperty(ref _objectColWidth, ClampWidth(value, MinObjectColWidth))) Refit(); }
    }
    public double DetailPaneWidth
    {
        get => _detailPaneWidth;
        set { if (SetProperty(ref _detailPaneWidth, ClampWidth(value, MinDetailPaneWidth))) Refit(); }
    }

    /// <summary>NaN goes to the floor, not through: a Width of NaN is "auto" to Avalonia, and the column would size to
    /// its text row by row.</summary>
    private static double ClampWidth(double value, double min) => double.IsNaN(value) ? min : Math.Clamp(value, min, MaxWidth);

    /// <summary>[CT-DETAIL-COVERS-LIST] The width the list and the detail pane share, as the view last measured the
    /// panel (its width less its margin and the pane's handle); NaN until then, when every width shows as remembered.</summary>
    internal double SplitWidth
    {
        get => _splitWidth;
        set { if (SetProperty(ref _splitWidth, double.IsNaN(value) ? double.NaN : Math.Max(0, value))) Refit(); }
    }

    // What the header, the rows and the pane bind.
    public double ShownTimeColWidth => _shownTime;
    public double ShownDurationColWidth => _shownDuration;
    public double ShownThreadColWidth => _shownThread;
    public double ShownObjectColWidth => _shownObject;
    public double ShownDetailPaneWidth => _shownPane;

    private double[] RememberedColumns => [_timeColWidth, _durationColWidth, _threadColWidth, _objectColWidth];
    private double[] ShownColumns => [_shownTime, _shownDuration, _shownThread, _shownObject];
    /// <summary>Each column's floor, in the order the columns are fitted: left to right, Object last.</summary>
    private static readonly double[] ColumnFloors = [MinTimeColWidth, MinDurationColWidth, MinThreadColWidth, MinObjectColWidth];

    /// <summary>Fits the remembered widths to the room: the pane first, so the list keeps its floor, then the columns
    /// into the list the pane leaves. Raises a shown width only when it changes.</summary>
    private void Refit()
    {
        double pane = FitPane(_detailPaneWidth, _splitWidth);
        double[] cols = FitColumns(RememberedColumns, RowRoom(_splitWidth, pane));
        SetProperty(ref _shownPane, pane, nameof(ShownDetailPaneWidth));
        SetProperty(ref _shownTime, cols[0], nameof(ShownTimeColWidth));
        SetProperty(ref _shownDuration, cols[1], nameof(ShownDurationColWidth));
        SetProperty(ref _shownThread, cols[2], nameof(ShownThreadColWidth));
        SetProperty(ref _shownObject, cols[3], nameof(ShownObjectColWidth));
    }

    /// <summary>The pane as shown: as remembered, but never so wide that the list loses its floor. Where the panel is
    /// narrower than both floors the pane keeps its own 200 and the list takes what is left: the list below its floor
    /// still shows a row's Time and Duration to click, and the pane is where the chosen call is read -- cut below 200
    /// it wraps the call's text into fragments and its tabs into rows.</summary>
    internal static double FitPane(double remembered, double split)
        => double.IsNaN(split) ? remembered : Math.Min(remembered, Math.Max(MinDetailPaneWidth, split - MinListWidth));

    /// <summary>The room a row gives its cells beside a pane <paramref name="shownPane"/> wide. A list below its floor
    /// is laid out as at its floor and cut at its edge ([CT-COLUMNS-OVERLAP]): fitted to the narrower list, every
    /// column would shrink to its floor, Time and Duration included, to keep a Function slice that is cut anyway.</summary>
    internal static double RowRoom(double split, double shownPane)
        => double.IsNaN(split) ? double.NaN : Math.Max(split - shownPane, MinListWidth) - RowChrome;

    /// <summary>The widest column <paramref name="i"/> can show in a row of <paramref name="room"/>: what is left after
    /// Function's slice, the columns before it as <paramref name="shown"/> and the floors of the ones after it. So the
    /// columns give way from the right: Object first, the column a call is least told by, Time last.</summary>
    internal static double ColumnCap(int i, double[] shown, double room)
    {
        if (double.IsNaN(room)) return MaxWidth;
        double cap = room - MinFunctionWidth;
        for (int j = 0; j < ColumnFloors.Length; j++)
            if (j != i) cap -= j < i ? shown[j] : ColumnFloors[j];
        return Math.Max(ColumnFloors[i], cap);
    }

    /// <summary>[CT-DETAIL-COVERS-LIST] The columns as shown: each as remembered where it fits, else at its cap, never
    /// below its floor. Unfitted, a column wider than the list hid Function and the handles of the columns after it.</summary>
    internal static double[] FitColumns(double[] remembered, double room)
    {
        var shown = new double[remembered.Length];
        for (int i = 0; i < remembered.Length; i++)
            shown[i] = Math.Min(remembered[i], ColumnCap(i, shown, room));
        return shown;
    }

    // [CT-DETAIL-COVERS-LIST] A drag adds its step to the width SHOWN and remembers what it leaves shown. Added to a
    // remembered width the panel does not show, a drag past the limit would keep a margin that the next drag back has
    // to undo first, with nothing moving meanwhile.
    internal void DragTime(double step) => TimeColWidth = DraggedColumn(0, step);
    internal void DragDuration(double step) => DurationColWidth = DraggedColumn(1, step);
    internal void DragThread(double step) => ThreadColWidth = DraggedColumn(2, step);
    /// <summary>Object's step widens it: its handle is on its left edge.</summary>
    internal void DragObject(double step) => ObjectColWidth = DraggedColumn(3, step);
    /// <summary>The pane's step widens it: its handle is on its left edge.</summary>
    internal void DragDetailPane(double step)
        => DetailPaneWidth = Math.Clamp(_shownPane + step, MinDetailPaneWidth, FitPane(MaxWidth, _splitWidth));

    private double DraggedColumn(int i, double step)
    {
        double[] shown = ShownColumns;
        return Math.Clamp(shown[i] + step, ColumnFloors[i], ColumnCap(i, shown, RowRoom(_splitWidth, _shownPane)));
    }

    internal CallTrace? Trace => _trace;
    internal CallTraceTree? Tree => _tree;

    public CallTraceViewModel(IDumpService dump, ILoggingService log, LiveFuncsViewModel liveFuncs,
                              IPlatformService? platform = null)
    {
        _dump = dump;
        _log = log;
        _platform = platform;
        LiveFuncs = liveFuncs;
        _filterMemory = new KeywordSearchMemory(() => (FilterText, _matches.Length > 0));
        StatusText = Res.Get("str.CT.Status.Empty");
    }

    /// <summary>The tab was opened: load the DLL's trace if it holds one this tab has not read yet.</summary>
    public async Task OnActivatedAsync()
    {
        if (IsLoading) return;
        _activationCts?.Cancel();
        var activation = _activationCts = new CancellationTokenSource();
        // Leaving Live Funcs mid-recording stops it on the way here; read only once that stop is done.
        try { await LiveFuncs.PendingAutoStop; } catch { /* Live Funcs logs its own failure */ }
        if (activation.IsCancellationRequested) return;   // the user left the tab meanwhile
        TracePage probe;
        try
        {
            probe = await _dump.PeTraceGetAsync(0, 1, activation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            // An older DLL does not know the command; a dropped pipe has nothing to read. Neither is an error here.
            _log.Debug($"CallTrace: no trace to read ({ex.Message})");
            if (_trace == null) StatusText = Res.Get("str.CT.Status.NoTrace");
            return;
        }
        if (activation.IsCancellationRequested) return;
        var i = probe.Info;
        if (i.Allocated && !i.Tracing && i.Quiesced && i.Written > 0 && i.Gen != _loadedGen)
            await LoadAsync();
        else if (_trace == null)
            StatusText = i.Tracing ? Res.Get("str.CT.Status.StillRecording") : Res.Get("str.CT.Status.NoTrace");
        else if (i.Gen != _loadedGen && i.Gen != 0 && !i.Tracing)
        {
            // A newer recording exists but could not be read: say so over the older trace still on screen.
            ShownIsOlder = true;
            StatusText = Res.Get(i.Written == 0 ? "str.CT.Status.OlderNoCalls" : "str.CT.Status.OlderUnread");
        }
    }

    /// <summary>Read the DLL's stopped trace: every page of the ring, the names of what it saw, then free the DLL's
    /// memory and build the tree.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (!CanLoad) return;
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        bool reclaim = false;
        try
        {
            ClearError();
            IsLoading = true;
            Progress = 0;
            StatusText = Res.Get("str.CT.Status.Reading");

            var head = await _dump.PeTraceGetAsync(0, 1, ct);
            var info = head.Info;
            if (!info.Allocated || info.Written == 0)
            {
                // Nothing newer to read; a trace already on screen keeps its summary.
                StatusText = _trace != null ? Res.Format("str.CT.Status.NothingNewer", Summary(_trace))
                                            : Res.Get("str.CT.Status.NoTrace");
                return;
            }
            if (info.Tracing) { StatusText = Res.Get("str.CT.Status.StillRecording"); return; }
            if (!info.Quiesced) { StatusText = Res.Get("str.CT.Status.NotQuiesced"); return; }

            ulong kept = info.Written - info.FirstValid;
            reclaim = true;   // a window is about to be made: whatever happens next, collect it afterwards
            // Let go of the trace on screen first, and collect it: held beside the new window and columns it cost
            // ~0.4 GB of a 3.77 GB peak (a 512 MB load after a 128 MB one, build 3634). A read dropped after this
            // leaves no trace on screen; its status says why.
            DropShownTrace();
            CollectPageGarbage();
            _loadStartWs = _loadPeakWs = Environment.WorkingSet;
            _loadStartHeap = GC.GetTotalMemory(false);
            CollectionsDuringLastLoad = 0;   // the read's own, from here
            _collectTicks = 0;
            _loadMinEvery = CollectEveryPages;
            var read = await ReadAndBuildAsync(info, kept, ct);
            if (read.Trace == null)
            {
                StatusText = StringLookup(read.StatusKey ?? "str.CT.Status.Changed");
                return;
            }
            var trace = read.Trace;

            // Read: give the game its memory back now, not at the next Start or when the UI disconnects.
            try { await _dump.PeTraceReleaseAsync(info.Gen, CancellationToken.None); }
            catch (Exception ex) { _log.Warn($"CallTrace: release failed ({ex.Message})"); }

            _trace = trace;
            _traceModule = _engineState;
            _tree = new CallTraceTree(trace);
            _loadedGen = info.Gen;
            HasTrace = true;
            ShownIsOlder = false;
            LiveFuncs.HasTraceToOpen = false;
            _filterMemory.Flush();
            FilterText = "";
            ShowTree(-1);
            Progress = 1;
            StatusText = Summary(trace);
            _log.Info($"CallTrace: loaded {trace.Count} calls ({kept} records, gen {info.Gen}), " +
                      $"{read.Funcs} functions, {read.Objs} objects" +
                      (trace.Stacks is { } st
                          ? $", {st.Calls.Count} stacks ({st.Orphans} orphaned, {st.Unjoined} unjoined)" : ""));
        }
        catch (OperationCanceledException)
        {
            StatusText = Res.Get("str.CT.Status.Cancelled");
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = Res.Get("str.CT.Status.Failed");
            _log.Error("CallTrace load failed", ex);
        }
        finally
        {
            IsLoading = false;
            if (reclaim) ReclaimAfterLoad();
        }
    }

    /// <summary>[TRACE-UI-LOAD-MEMORY] Every page of the ring straight into one window, the names of what it saw, then
    /// the tree. The window lives only in here: once this returns nothing reaches it, so the collection after the load
    /// can give it back. A null trace comes with the status that says why the read was dropped.</summary>
    private async Task<(CallTrace? Trace, string? StatusKey, int Funcs, int Objs)> ReadAndBuildAsync(
        TraceInfo info, ulong kept, CancellationToken ct)
    {
        var all = new TraceRecord[kept];
        long n = 0;
        ulong from = info.FirstValid;
        int pagesSinceCollect = 0;
        while (from < info.Written && n < all.LongLength)
        {
            ct.ThrowIfCancellationRequested();
            // Never more than the window has left: the page is decoded into it.
            int max = (int)Math.Min(PageRecords, all.LongLength - n);
            var page = await _dump.PeTraceGetIntoAsync(from, max, all.AsMemory((int)n, (int)(all.LongLength - n)), ct);
            // The ring belongs to one recording; a new Start in between would hand back another one's records, and
            // a ring that is gone (another reader released it) or a page that does not move before the end would
            // leave a partial read that looks whole: in each case the read is dropped, not built.
            if (page.Info.Gen != info.Gen || !page.Info.Allocated || page.Next <= from)
                return (null, "str.CT.Status.Changed", 0, 0);
            n += page.Count;
            from = page.Next;
            NoteLoadPeak();
            // Read after every page: the load's own window, or the game, takes the free memory as the read goes. One
            // call into the OS a page, against a page of 1.3 MB from the pipe.
            int every = PagesBetweenCollections(_platform?.GetAvailablePhysicalMemoryBytes() ?? long.MaxValue);
            _loadMinEvery = Math.Min(_loadMinEvery, every);
            if (++pagesSinceCollect >= every)
            {
                CollectPageGarbage();
                pagesSinceCollect = 0;
            }
            Progress = 0.8 * (from - info.FirstValid) / Math.Max(1.0, kept);
            StatusText = Res.Format("str.CT.Status.ReadingRecords", n, kept);
        }

        StatusText = Res.Get("str.CT.Status.Naming");
        var funcs = new List<TraceFuncName>();
        for (int off = 0; ; off += NamesPage)
        {
            var p = await _dump.PeTraceFuncNamesAsync(info.Gen, off, NamesPage, ct);
            if (p.Stale) return (null, "str.CT.Status.Changed", 0, 0);
            funcs.AddRange(p.Items);
            if (p.Items.Count == 0 || off + p.Items.Count >= p.Total || p.Truncated) break;
        }
        Progress = 0.85;
        var objs = new List<TraceObjName>();
        for (int off = 0; ; off += NamesPage)
        {
            var p = await _dump.PeTraceObjNamesAsync(info.Gen, off, NamesPage, ct);
            if (p.Stale) return (null, "str.CT.Status.Changed", 0, 0);
            objs.AddRange(p.Items);
            if (p.Items.Count == 0 || off + p.Items.Count >= p.Total || p.Truncated) break;
            Progress = 0.85 + 0.1 * Math.Min(1.0, (off + p.Items.Count) / Math.Max(1.0, p.Total));
        }

        // [LIVEFUNCS-STEP2] The parameter snapshots, before the release that frees their rings with the trace: every
        // arm with its layout, then every ring's slots, decoded by the DLL with each slot's own arm. A gen that moves
        // or a stale answer drops the load, like the records' pages.
        List<SnapArm>? arms = null;
        var rings = new List<SnapRingInfo>();
        var slots = new List<SnapSlot>();
        ulong orphans = 0;
        // [LIVEFUNCS-STEP3] A Start that chose stacks alone allocates the buffer with no parameter ring and no arm:
        // there is nothing to ask the layouts of.
        if (info.Snap is { Allocated: true, Rings: > 0 })
        {
            StatusText = Res.Get("str.CT.Status.ReadingSnapshots");
            arms = new List<SnapArm>();
            for (int off = 0; ;)
            {
                var lp = await _dump.PeSnapLayoutsAsync(info.Gen, off, ct);
                if (lp.Stale || lp.Info.Gen != info.Gen) return (null, "str.CT.Status.Changed", 0, 0);
                if (off == 0) rings = lp.Rings.ToList();
                arms.AddRange(lp.Arms);
                if (lp.Arms.Count == 0 || lp.Next >= lp.Total || lp.Next <= off) break;
                off = lp.Next;
            }
            foreach (var ring in rings)
            {
                for (ulong slotFrom = ring.FirstValid; slotFrom < ring.Written;)
                {
                    ct.ThrowIfCancellationRequested();
                    var sp = await _dump.PeSnapGetAsync(info.Gen, ring.Ring, slotFrom, SnapPage, ct);
                    if (sp.Stale || sp.Info.Gen != info.Gen) return (null, "str.CT.Status.Changed", 0, 0);
                    slots.AddRange(sp.Items);
                    orphans += sp.Orphans;
                    if (sp.Next <= slotFrom) break;   // refused or nothing more: what was read stands
                    slotFrom = sp.Next;
                }
            }
        }

        // [LIVEFUNCS-STEP3] The native stacks, before the release too, and whatever the parameters were: a stacks-only
        // trace has none. Every reply carries every stack ring's window, so a first one-slot read fetches them, and each
        // ring is then paged over its window as the parameter rings are (review M1). A page whose slots were all
        // orphans -- the oldest ones, once the trace's ring lapped -- comes back empty yet moves on, so only a page
        // that does not move ends a ring.
        List<StackSlot>? stackSlots = null;
        ulong stackOrphans = 0;
        if (info.Stack is { Rings: > 0 })
        {
            StatusText = Res.Get("str.CT.Status.ReadingStacks");
            stackSlots = new List<StackSlot>();
            var head = await _dump.PeStackGetAsync(info.Gen, 0, 0, 1, ct);
            if (head.Stale || head.Info.Gen != info.Gen) return (null, "str.CT.Status.Changed", 0, 0);
            foreach (var ring in head.Rings)
            {
                for (ulong slotFrom = ring.FirstValid; slotFrom < ring.Written;)
                {
                    ct.ThrowIfCancellationRequested();
                    var sp = await _dump.PeStackGetAsync(info.Gen, ring.Ring, slotFrom, SnapPage, ct);
                    if (sp.Stale || sp.Info.Gen != info.Gen) return (null, "str.CT.Status.Changed", 0, 0);
                    stackSlots.AddRange(sp.Items);
                    stackOrphans += sp.Orphans;
                    if (sp.Next <= slotFrom) break;   // refused or nothing more: what was read stands
                    slotFrom = sp.Next;
                }
            }
        }

        StatusText = Res.Get("str.CT.Status.Building");
        CollectPageGarbage();   // the build's columns take the room the pages left, not new memory
        long count = n;
        // Everything is read: the build and the release no longer take the load's token. Cancelling now would only
        // leave the ring in the game until the next Start (review CT-RELEASE-CANCELLED).
        var trace = await Task.Run(() =>
        {
            var built = CallTraceBuilder.Build(all.AsSpan(0, (int)count), info, funcs, objs);
            if (arms != null) built.Snapshots = CallTraceSnapshots.Join(built, arms, rings, slots, orphans);
            if (stackSlots != null) built.Stacks = CallTraceStacks.Join(built, stackSlots, stackOrphans, info.Stack);
            return built;
        });
        NoteLoadPeak();   // the window and the columns at once: the load's structural peak
        return (trace, null, funcs.Count, objs.Count);
    }

    /// <summary>A full, blocking collection that does not compact: the pages' freed large objects go back on the free
    /// list, and the next pages reuse them instead of growing the heap.</summary>
    private void CollectPageGarbage()
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        _collectTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        CollectionsDuringLastLoad++;
    }

    private void NoteLoadPeak() => _loadPeakWs = Math.Max(_loadPeakWs, Environment.WorkingSet);

    /// <summary>Nothing the view model keeps reaches the shown trace any more, a cache built from it included: whatever
    /// still held it would keep it alive beside the next load's window and columns.</summary>
    private void DropShownTrace()
    {
        if (_trace == null) return;
        _trace = null;
        _tree = null;
        _matches = Array.Empty<int>();
        _codeIndexCache = null;
        IsFiltered = false;
        SelectedIndex = -1;
        Rows = Array.Empty<CallTraceRow>();
        DetailText = "";
        HasTrace = false;
        ShownIsOlder = false;
    }

    /// <summary>[TRACE-UI-LOAD-MEMORY] A load leaves its window and every page's reply behind, and nothing makes the GC
    /// run after it: on Avowed a 512 MB load held the working set at 3.25 GB a minute later (2026-10-07). One blocking,
    /// compacting collection gives it back -- a pause after a load of seconds, the snapshot capture's precedent -- and
    /// the log line is what the memory estimate beside the trace slider is checked against.</summary>
    private void ReclaimAfterLoad()
    {
        long ws = Environment.WorkingSet >> 20, heap = GC.GetTotalMemory(false) >> 20;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        _log.Info($"CallTrace: memory -- at the load's start heap {_loadStartHeap >> 20:N0} MB, working set " +
                  $"{_loadStartWs >> 20:N0} MB; working set at its peak (sampled per page) {_loadPeakWs >> 20:N0} MB, " +
                  $"{CollectionsDuringLastLoad} collections while it read ({_collectTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:N0} ms " +
                  $"in all, as often as every {_loadMinEvery} page(s) by the free memory); after it heap {heap:N0}->" +
                  $"{GC.GetTotalMemory(false) >> 20:N0} MB, working set {ws:N0}->{Environment.WorkingSet >> 20:N0} MB " +
                  "(after the compacting collection)");
    }

    [RelayCommand]
    private void CancelLoad() => _loadCts?.Cancel();

    /// <summary>The status line under a loaded trace. Its sentences come through <see cref="StringLookup"/>, so a test
    /// reads the ones a user reads.</summary>
    internal string Summary(CallTrace t)
    {
        var sb = new StringBuilder(Say("str.CT.Status.Loaded", t.Count, t.WindowSeconds));
        if (t.Info.FirstValid > 0) sb.Append(' ').Append(Say("str.CT.Status.Lapped", t.Info.FirstValid));
        if (t.ReturnsBeforeWindow == 1) sb.Append(' ').Append(StringLookup("str.CT.Status.BeforeWindowOne"));
        else if (t.ReturnsBeforeWindow > 0) sb.Append(' ').Append(Say("str.CT.Status.BeforeWindow", t.ReturnsBeforeWindow));
        // "Every call" only when nothing was left out; a left-out function's calls show under its caller (DLL-9).
        switch (ScopeKey(t.Info))
        {
            case "str.CT.Status.Scoped": sb.Append(' ').Append(Say("str.CT.Status.Scoped", t.Info.Ticked)); break;
            case "str.CT.Status.ScopedNames":
                sb.Append(' ').Append(Say("str.CT.Status.ScopedNames", t.Info.TickedNames)); break;
            case { } key: sb.Append(' ').Append(StringLookup(key)); break;
        }
        if (t.Info.Excluded > 0) sb.Append(' ').Append(Say("str.CT.Status.Excluded", t.Info.Excluded));
        // [TRACE-UNLOADED-NAMES] Always, whatever the buffer: how much is named only from a first call, and how much
        // has no name at all -- 0% included, so a clean trace says so.
        if (t.UnloadedFuncs > 0)
            sb.Append(' ').Append(Say("str.CT.Status.Unloaded", t.UnloadedFuncs, t.DistinctFuncs,
                                      CallTrace.ShareText(t.UnloadedCalls, t.Count)));
        sb.Append(' ').Append(Say("str.CT.Status.Unnamed", CallTrace.ShareText(t.UnnamedCalls, t.Count),
                                  t.UnnamedFuncs, t.DistinctFuncs));
        if (t.Snapshots is { } s)
            sb.Append(' ').Append(Say("str.CT.Status.Snapshots", s.CallsWithParams, s.Arms.Count,
                                      (long)(t.Info.Snap?.SkippedBudget ?? 0)));
        // [LIVEFUNCS-STEP3] The calls the budget left without a stack, recorded or not, and what the DLL measured a
        // capture to cost: that time falls inside its call's duration, so the durations above carry it.
        if (t.Stacks is { } st)
        {
            ulong skipped = st.Info?.SkippedBudget ?? 0, dropped = st.Info?.DroppedBudget ?? 0;
            sb.Append(' ').Append(Say("str.CT.Status.Stacks", st.Calls.Count, (long)(skipped + dropped), (long)dropped));
            if (st.Info is { Captures: > 0 } si && t.Info.QpcFreq > 0)
                sb.Append(' ').Append(Say("str.CT.Status.StackCost", (double)si.SpentTicks / si.Captures * 1e6 / t.Info.QpcFreq));
        }
        return sb.ToString();
    }

    /// <summary>[LIVEFUNCS-STEP2] Which sentence says what the trace followed: ticked functions (by address, or by
    /// name), only the chosen calls (T11), or every call -- that one only when nothing was left out. A DLL that
    /// predates names sends no `scoped`: its ticks by address say it.</summary>
    internal static string? ScopeKey(TraceInfo i)
    {
        if (i.SnapOnly) return "str.CT.Status.SnapOnly";
        if (i.Scoped ?? i.Ticked > 0)
            return i.TickedNames > 0 ? "str.CT.Status.ScopedNames" : "str.CT.Status.Scoped";
        return i.Excluded == 0 ? "str.CT.Status.Unscoped" : null;
    }

    // ---- the rows ----

    private void ShowTree(int selectCall)
    {
        if (_tree == null) { Rows = Array.Empty<CallTraceRow>(); return; }
        IsFiltered = false;
        Rows = new CallTraceRowList(_tree.Trace, _tree.Visible, _tree, indented: true);
        SelectedIndex = selectCall < 0 ? -1 : IndexOf(_tree.Visible, selectCall);
    }

    private static int IndexOf(IReadOnlyList<int> list, int call)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == call) return i;
        return -1;
    }

    partial void OnFilterTextChanged(string value) => ApplyCallFilter(value);

    /// <summary>[LIVEFUNCS-STEP2] U13: list only the calls that carry a parameter copy -- with the filter's text, or alone
    /// with none (an empty box would otherwise mean "the tree").</summary>
    [ObservableProperty] private bool _onlyWithParams;
    partial void OnOnlyWithParamsChanged(bool value) => ApplyCallFilter(FilterText);

    private void ApplyCallFilter(string value)
    {
        if (_trace == null || _tree == null) return;
        var terms = ObjectTreeFilter.SplitTerms(value);
        if (terms.Length == 0 && !OnlyWithParams)
        {
            _matches = Array.Empty<int>();
            ShowTree(-1);
            if (_trace != null) StatusText = Summary(_trace);
            return;
        }
        bool capped = false;
        if (terms.Length > 0)
        {
            _matches = CallTraceTree.Match(_trace, value, OnlyWithParams ? int.MaxValue : Constants.DefaultMaxQueryRows,
                                           out capped);
            if (OnlyWithParams) _matches = _matches.Where(c => _trace.Snapshots?.Has(c) == true).ToArray();
        }
        else
        {
            _matches = (_trace.Snapshots?.Calls ?? Array.Empty<int>()).ToArray();
        }
        if (_matches.Length > Constants.DefaultMaxQueryRows)
        {
            _matches = _matches[..Constants.DefaultMaxQueryRows];
            capped = true;
        }
        IsFiltered = true;
        Rows = new CallTraceRowList(_trace, _matches, null, indented: false);
        SelectedIndex = -1;
        StatusText = capped
            ? Res.Format("str.CT.Status.MatchesCapped", _matches.Length)
            : _matches.Length == 1 ? Res.Get("str.CT.Status.MatchesOne")
            : Res.Format("str.CT.Status.Matches", _matches.Length);
        if (terms.Length > 0) _filterMemory.Schedule(value);
    }

    partial void OnSelectedIndexChanged(int value)
    {
        int call = SelectedCall();
        DetailText = Detail(call);
        var p = Params(call);
        ParamRows = p.Rows;
        ParamsNote = string.Join(Environment.NewLine, p.Notes);
        ParamsHex = p.Hex;
        ShowStack(call);
    }

    // ---- [LIVEFUNCS-STEP2] U13: the selected call's parameters (view C) ----

    [ObservableProperty] private IReadOnlyList<ParamRow> _paramRows = Array.Empty<ParamRow>();
    [ObservableProperty] private string _paramsNote = "";
    [ObservableProperty] private string _paramsHex = "";

    /// <summary>What the Parameters tab shows for call <paramref name="i"/>: a row per parameter (a struct's members
    /// under it), the reasons that qualify them or say why there are none, and the raw copies. Nothing at all for a
    /// call that was not chosen: the tab then says so itself.</summary>
    internal (IReadOnlyList<ParamRow> Rows, IReadOnlyList<string> Notes, string Hex) Params(int i)
    {
        var t = _trace;
        var none = ((IReadOnlyList<ParamRow>)Array.Empty<ParamRow>(), (IReadOnlyList<string>)Array.Empty<string>(), "");
        if (t == null || i < 0 || i >= t.Count) return none;
        uint f = t.Flags[i];
        var s = t.Snapshots;
        var entry = s?.EntryOf(i);
        var after = s?.AfterOf(i);
        const uint Taken = 2, Lone = 4, Excluded = 8, Budget = 16;
        // [LIVEFUNCS-STEP3] Taken and Budget are the parameters' own flags; Lone and Excluded say a call was chosen
        // for something, and a call chosen for its stack alone carries them without a parameter to show.
        if ((f & (Taken | Budget)) == 0 && entry == null && after == null)
            return (Array.Empty<ParamRow>(), new[] { StringLookup("str.CT.Param.NotChosen") }, "");
        var notes = new List<string>();
        if ((f & Lone) != 0) notes.Add(StringLookup("str.CT.Param.Lone"));
        if ((f & Excluded) != 0) notes.Add(StringLookup("str.CT.Param.Excluded"));
        if ((f & Budget) != 0)
        {
            notes.Add(StringLookup("str.CT.Param.Budget"));
            return (Array.Empty<ParamRow>(), notes, "");
        }
        if (entry == null && after == null)
        {
            notes.Add(StringLookup("str.CT.Param.Overwritten"));
            return (Array.Empty<ParamRow>(), notes, "");
        }
        var hex = new StringBuilder();
        foreach (var slot in new[] { entry, after })
        {
            if (slot == null) continue;
            if ((slot.Flags & SnapSlot.NullParamsFlag) != 0) notes.Add(StringLookup("str.CT.Param.NullParams"));
            if ((slot.Flags & SnapSlot.CopyFaultFlag) != 0) notes.Add(StringLookup("str.CT.Param.CopyFault"));
            if ((slot.Flags & SnapSlot.TruncatedFlag) != 0) notes.Add(StringLookup("str.CT.Param.Truncated"));
            hex.AppendLine(Say(slot.IsAfter ? "str.CT.Param.HexAfter" : "str.CT.Param.HexAt", Convert.ToHexString(slot.Data)));
        }
        var arm = s!.ArmOf(entry ?? after!);
        var layout = arm?.Layout;
        if (layout == null)
        {
            notes.Add(Say("str.CT.Param.RawOnly", arm?.State ?? "", arm?.Why ?? ""));
            return (Array.Empty<ParamRow>(), notes.Distinct().ToList(), hex.ToString());
        }
        bool hasOutputs = layout.Fields.Any(p => p.Kind is "out" or "in_out" or "return");
        if (after == null && hasOutputs && t.Returned[i]) notes.Add(StringLookup("str.CT.Param.NoAfter"));
        var rows = new List<ParamRow>();
        for (int k = 0; k < layout.Fields.Count; k++)
            AddParamRows(rows, layout.Fields[k], "", entry, after, entry?.Values, after?.Values, k, 0);
        return (rows, notes.Distinct().ToList(), hex.ToString());
    }

    private static void AddParamRows(List<ParamRow> rows, SnapParam p, string prefix, SnapSlot? entry, SnapSlot? after,
                                     IReadOnlyList<SnapValue>? at, IReadOnlyList<SnapValue>? af, int k, int depth)
    {
        var a = at != null && k < at.Count ? at[k] : null;
        var b = af != null && k < af.Count ? af[k] : null;
        int len = p.Size * Math.Max(1, p.ArrayDim);
        rows.Add(new ParamRow
        {
            Name = prefix + p.Name,
            Kind = p.Kind,
            Type = p.Type,
            AtCall = a == null ? "" : a.Text,
            AtCallMark = MarkText(a),
            After = b == null || b.Mark == SnapMark.Missing && b.Text.Length == 0 ? "" : b.Text,
            AfterMark = MarkText(b),
            // Only where both copies hold the bytes, and they differ: never from the decoded text.
            Changed = entry != null && after != null && BytesDiffer(entry.Data, after.Data, p.Offset, len),
            Depth = depth,
        });
        if (depth >= 4) return;
        for (int j = 0; j < p.Sub.Count; j++)
        {
            // A member's offset is its struct's plus its own.
            var sub = p.Sub[j];
            var shifted = new SnapParam { Name = sub.Name, Kind = p.Kind, Type = sub.Type, Offset = p.Offset + sub.Offset,
                                          Size = sub.Size, ArrayDim = sub.ArrayDim, Sub = sub.Sub };
            AddParamRows(rows, shifted, prefix + "  ", entry, after, a?.Sub, b?.Sub, j, depth + 1);
        }
    }

    private static bool BytesDiffer(byte[] x, byte[] y, int off, int len)
    {
        if (len <= 0 || off < 0 || off + len > x.Length || off + len > y.Length) return false;
        return !x.AsSpan(off, len).SequenceEqual(y.AsSpan(off, len));
    }

    private static string MarkText(SnapValue? v) => v?.Mark switch
    {
        SnapMark.Now => "now",
        SnapMark.Gone => "gone",
        SnapMark.Header => "header",
        SnapMark.Raw => "hex",
        _ => "",
    };

    // ---- [LIVEFUNCS-STEP3] S3-U5: the selected call's native stack (view A), and a frame to Cheat Engine (view D) ----

    [ObservableProperty] private IReadOnlyList<StackFrameRow> _stackRows = Array.Empty<StackFrameRow>();
    [ObservableProperty] private string _stackNote = "";

    private void ShowStack(int call)
    {
        var s = Stack(call);
        StackRows = s.Rows;
        StackNote = string.Join(Environment.NewLine, s.Notes);
    }

    /// <summary>What the Call stack tab shows for call <paramref name="i"/>: a row per return address, nearest first, and
    /// the notes that say why there is no stack, or how far to trust the one there is.</summary>
    internal (IReadOnlyList<StackFrameRow> Rows, IReadOnlyList<string> Notes) Stack(int i)
    {
        var t = _trace;
        var none = ((IReadOnlyList<StackFrameRow>)Array.Empty<StackFrameRow>(), (IReadOnlyList<string>)Array.Empty<string>());
        if (t == null || i < 0 || i >= t.Count) return none;
        uint f = t.Flags[i];
        var slot = t.Stacks?.StackOf(i);
        const uint Lone = 4, Excluded = 8;
        if ((f & (StackInfo.TakenEntryFlag | StackInfo.BudgetEntryFlag)) == 0 && slot == null)
            return (Array.Empty<StackFrameRow>(), new[] { StringLookup("str.CT.Stack.NotChosen") });
        // A call chosen for anything carries Lone or Excluded: the parameters' sentences are written for either kind.
        var notes = new List<string>();
        if ((f & Lone) != 0) notes.Add(StringLookup("str.CT.Param.Lone"));
        if ((f & Excluded) != 0) notes.Add(StringLookup("str.CT.Param.Excluded"));
        if (slot == null)
        {
            notes.Add(StringLookup((f & StackInfo.BudgetEntryFlag) != 0 ? "str.CT.Stack.Budget" : "str.CT.Stack.Overwritten"));
            return (Array.Empty<StackFrameRow>(), notes);
        }
        if ((slot.Flags & StackSlot.Partial) != 0) notes.Add(StringLookup("str.CT.Stack.Partial"));
        if ((slot.Flags & StackSlot.Fault) != 0) notes.Add(StringLookup("str.CT.Stack.Fault"));
        if ((slot.Flags & StackSlot.More) != 0) notes.Add(StringLookup("str.CT.Stack.More"));
        if ((slot.Flags & StackSlot.BadSp) != 0) notes.Add(StringLookup("str.CT.Stack.BadSp"));
        if ((slot.Flags & StackSlot.LowStack) != 0) notes.Add(StringLookup("str.CT.Stack.LowStack"));
        if ((slot.Flags & StackSlot.NoCapturer) != 0) notes.Add(StringLookup("str.CT.Stack.NoCapturer"));

        var index = CodeIndexOf(t);
        var format = (AddressFormat)SelectedAddressFormatIndex;
        var rows = new StackFrameRow[slot.Frames.Count];
        int doubtful = -1;
        for (int k = 0; k < rows.Length; k++)
        {
            var site = slot.Frames[k];
            // A frame without unwind data was walked as a leaf, which reads its caller from the wrong slot when it is
            // not one: the frames below it are the ones in doubt, so the last frame's own lack casts none.
            if (doubtful < 0 && !site.Unwind && k < rows.Length - 1) doubtful = k;
            rows[k] = new StackFrameRow
            {
                Index = k,
                Address = FrameAddress(site, format),
                Where = FrameWhere(site, index),
                CopyText = FrameCopyText(site),
                Abs = site.Addr,
            };
        }
        if (doubtful >= 0) notes.Add(Say("str.CT.Stack.MayBeWrong", doubtful));
        // The walk ran after the entry was timed (D11): its cost is part of the duration the Call tab shows.
        if (slot.Ticks > 0 && t.Info.QpcFreq > 0)
            notes.Add(Say("str.CT.Stack.Cost", slot.Ticks * 1e6 / t.Info.QpcFreq));
        return (rows, notes);
    }

    // The index with the trace it was built from, so a redraw does not rebuild it. That is a strong reference to a whole
    // trace: a load drops it with the shown trace, or the old trace outlives its screen ([TRACE-UI-LOAD-MEMORY]). One
    // field, so the index cannot be dropped apart from its trace.
    private (CallTrace Trace, Dictionary<ulong, string[]> Index)? _codeIndexCache;

    private Dictionary<ulong, string[]> CodeIndexOf(CallTrace t)
    {
        if (_codeIndexCache is { } c && ReferenceEquals(c.Trace, t)) return c.Index;
        var index = CodeIndex(t);
        _codeIndexCache = (t, index);
        return index;
    }

    /// <summary>The trace's own native entries (pe_trace_names' code_addr, read for a function still live), each with the
    /// Class::Func of every traced function that starts there, in order. Identical code folding gives several functions
    /// one entry, so a frame there is none of them in particular.</summary>
    internal static Dictionary<ulong, string[]> CodeIndex(CallTrace t)
        => t.Funcs.Values
            .Where(f => f.CodeAddr != 0 && f.Named)
            .GroupBy(f => f.CodeAddr)
            .ToDictionary(g => g.Key,
                          g => g.Select(f => f.ClassName.Length > 0 ? f.ClassName + "::" + f.FuncName : f.FuncName)
                                .Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray());

    /// <summary>A frame's address as the Address setting writes it, against the frame's OWN module: a stack crosses
    /// modules, so the game module the trace was loaded from would give an RVA into the wrong image. The module's name
    /// is CE's (narrowed with this machine's code page), so a module+RVA form resolves in CE.</summary>
    internal static string FrameAddress(StackSite s, AddressFormat f)
        => AddressHelper.FormatAddress(s.Addr.ToString("X", CultureInfo.InvariantCulture), s.CeModule, s.ModuleBaseHex, f);

    /// <summary>What Copy puts on the clipboard, whatever the Address setting (D13): CE's module+RVA form, which names
    /// the same code after a relaunch moves the module; outside every module, this run's absolute address.</summary>
    internal static string FrameCopyText(StackSite s)
        => s.CeModule.Length > 0 ? CeForm(s.CeModule, s.Rva) : $"0x{s.Addr.ToString("X", CultureInfo.InvariantCulture)}";

    private static string CeForm(string ceModule, uint rva) => $"\"{ceModule}\"+{rva.ToString("X", CultureInfo.InvariantCulture)}";

    /// <summary>What a frame is, by the first rule that fits: the dumper's own image (the hook of an enclosing call), then
    /// ProcessEvent, then a traced native's entry, then the entry of any UFunction the DLL's index knows (an exec thunk
    /// reached without ProcessEvent), then an offset into the function holding it, else why none is known. The DLL tells
    /// own, ProcessEvent and the index's names from addresses only it has.</summary>
    internal string FrameWhere(StackSite s, IReadOnlyDictionary<ulong, string[]> codeIndex)
    {
        ulong off = s.Addr - s.Fn;
        if (s.Own) return StringLookup("str.CT.Stack.Hook");
        if (s.Known == "process_event") return Say("str.CT.Stack.ProcessEvent", off);
        if (s.Fn != 0 && codeIndex.TryGetValue(s.Fn, out var names))
            return names.Length == 1
                ? Say("str.CT.Stack.Native", names[0], off)
                : Say("str.CT.Stack.NativeShared", names[0], off, names.Length - 1);
        // [A1-INTERP-LABEL] The script functions' entry is the interpreter: the function the DLL names there is only the
        // lowest-addressed of them, so the line names the interpreter instead.
        if (s.Fn != 0 && s.Script)
            return s.Shared > 1 ? Say("str.CT.Stack.Interpreter", off, s.Shared) : Say("str.CT.Stack.InterpreterOne", off);
        if (s.Fn != 0 && s.FuncName.Length > 0)
        {
            string name = s.ClassName.Length > 0 ? s.ClassName + "::" + s.FuncName : s.FuncName;
            return s.Shared > 1 ? Say("str.CT.Stack.NativeIndexShared", name, off, s.Shared)
                                : Say("str.CT.Stack.Native", name, off);
        }
        if (s.Fn != 0)
            return Say("str.CT.Stack.Into", off,
                       s.CeModule.Length > 0 ? CeForm(s.CeModule, s.FnRva)
                                             : $"0x{s.Fn.ToString("X", CultureInfo.InvariantCulture)}");
        return StringLookup(s.Module.Length > 0 ? "str.CT.Stack.NoUnwind" : "str.CT.Stack.NoModule");
    }

    /// <summary>[LIVEFUNCS-STEP3] D13: the frame in CE's module form. The status says whether it arrived: a copy that
    /// silently failed would leave an older address to paste.</summary>
    [RelayCommand]
    private async Task CopyFrameAsync(StackFrameRow? r)
    {
        if (r == null || r.CopyText.Length == 0) return;
        bool copied = await ClipboardDelivery.TryAsync(_platform, r.CopyText);
        StatusText = Say(copied ? "str.CT.Stack.Copied" : "str.CT.Stack.CopyFailed", r.CopyText);
    }

    /// <summary>[LIVEFUNCS-STEP3] D13: CE's disassembler at the frame's absolute address. That address belongs to the
    /// process the trace was read from, so a trace kept past its connection sends nothing: the game now running may
    /// hold anything there.</summary>
    [RelayCommand]
    private async Task AsmFrameAsync(StackFrameRow? r)
    {
        if (r == null || r.Abs == 0) return;
        if (_loadedGen == 0)
        {
            StatusText = StringLookup("str.CT.Stack.AsmOldTrace");
            return;
        }
        string text = await AobMakerActions.AsmAsync(LiveFuncs.AobMaker,
                                                     "0x" + r.Abs.ToString("X", CultureInfo.InvariantCulture),
                                                     r.CopyText, _log);
        if (text.Length > 0) StatusText = text;
    }

    private int SelectedCall()
        => Rows is CallTraceRowList l && SelectedIndex >= 0 && SelectedIndex < l.Count ? l.CallAt(SelectedIndex) : -1;

    [RelayCommand]
    private void Toggle(CallTraceRow? row)
    {
        if (row == null || _tree == null || IsFiltered) return;
        _tree.Toggle(row.Call);
        ShowTree(row.Call);
    }

    [RelayCommand]
    private void ExpandAll()
    {
        if (_tree == null || IsFiltered) return;
        int keep = SelectedCall();
        _tree.ExpandAll();
        ShowTree(keep);
    }

    [RelayCommand]
    private void CollapseAll()
    {
        if (_tree == null || IsFiltered) return;
        _tree.CollapseAll();
        ShowTree(-1);
    }

    /// <summary>Take the selected match back to the tree, with its callers opened above it.</summary>
    [RelayCommand]
    private void ShowInTree()
    {
        int call = SelectedCall();
        if (_tree == null || call < 0) return;
        _filterMemory.Flush();
        _matches = Array.Empty<int>();
        _tree.Reveal(call);
        FilterText = "";        // shows the tree
        ShowTree(call);
    }

    /// <summary>What the detail pane says about one call: what ran and where its code is, on what, when and for how
    /// long, and the chain of callers above it -- the call stack at the UFunction level.</summary>
    internal string Detail(int i)
    {
        var t = _trace;
        if (t == null || i < 0 || i >= t.Count) return "";
        var sb = new StringBuilder();
        sb.AppendLine(Say("str.CT.Detail.Function", Label(t, i)));
        if (t.FuncUnloaded(i)) sb.AppendLine(StringLookup("str.CT.Detail.Unloaded"));
        if (t.FuncRecycled(i)) sb.AppendLine(StringLookup("str.CT.Detail.Recycled"));
        if (t.FuncReused(i)) sb.AppendLine(StringLookup("str.CT.Detail.Reused"));
        sb.AppendLine(Say("str.CT.Detail.FuncAddr", Addr(t.Func[i])));
        sb.AppendLine(NativeEntryLine(t.Funcs.TryGetValue(t.Func[i], out var f) ? f : null));
        if (t.Obj[i] != 0)
        {
            sb.AppendLine(t.ObjStale(i)
                ? Say("str.CT.Detail.ObjectStale", Addr(t.Obj[i]))
                : Say("str.CT.Detail.Object", t.ObjName(i), t.ObjClass(i), Addr(t.Obj[i])));
        }
        sb.AppendLine(Say("str.CT.Detail.Thread", t.Tid[i]));
        sb.AppendLine(Say("str.CT.Detail.Start", t.StartMs(i)));
        sb.AppendLine(t.DurationUs(i) is { } us ? Say("str.CT.Detail.Duration", us) : StringLookup("str.CT.Detail.NoReturn"));
        sb.AppendLine(Say("str.CT.Detail.Children", t.ChildCount[i]));
        if ((t.Flags[i] & TraceRecord.ScopeRootFlag) != 0) sb.AppendLine(StringLookup("str.CT.Detail.ScopeRoot"));
        sb.AppendLine();
        sb.AppendLine(StringLookup("str.CT.Detail.Callers"));
        var chain = new List<int>();
        for (int p = i; p >= 0; p = t.Parent[p]) chain.Add(p);
        chain.Reverse();
        for (int k = 0; k < chain.Count; k++)
            sb.Append(new string(' ', k * 2)).AppendLine(Label(t, chain[k]));
        return sb.ToString();
    }

    /// <summary>Where the sentences a test checks come from: en.axaml, through Res. A unit test has no Avalonia
    /// application for Res to ask, so it reads en.axaml itself and hands it in here.</summary>
    internal Func<string, string> StringLookup { get; set; } = Res.Get;

    private string Say(string key, params object[] args)
    {
        string template = StringLookup(key);
        return template.Length == 0 ? "" : string.Format(template, args);
    }

    // ---- the detail's addresses ([LIVEFUNCS-STEP2] U11) ----

    /// <summary>The toolbar's Address setting (an <see cref="AddressFormat"/>), fanned out by the main window as to the
    /// other tabs that show addresses.</summary>
    [ObservableProperty] private int _selectedAddressFormatIndex;
    partial void OnSelectedAddressFormatIndexChanged(int value)
    {
        int call = SelectedCall();
        DetailText = Detail(call);
        ShowStack(call);
    }

    private EngineState? _engineState;
    /// <summary>The game the trace was loaded from: its module turns a native entry into a CE address. Taken at the
    /// load, not read at display: a trace outlives its connection, and another game's module base would give an RVA
    /// into the wrong image.</summary>
    private EngineState? _traceModule;

    public void SetEngineState(EngineState state) => _engineState = state;

    /// <summary>An address as the Address setting writes it. Under "module+RVA" an address outside the module (the
    /// UFunction and the object are on the heap) falls back to plain hex: AddressHelper.FormatAddress's rule.</summary>
    private string Addr(ulong a)
        => AddressHelper.FormatAddress(a.ToString("X", CultureInfo.InvariantCulture), _traceModule?.CeModuleName,
                                       _traceModule?.ModuleBase, (AddressFormat)SelectedAddressFormatIndex);

    /// <summary>EFunctionFlags::FUNC_Native.</summary>
    private const uint FuncNative = 0x0000_0400;

    /// <summary>Where the function's own code is: a native function's entry (its execXxx thunk) as the address CE
    /// takes, or why no entry is shown. The DLL reads the entry only for a function still live, so an unloaded one's
    /// was never read.</summary>
    private string NativeEntryLine(TraceFuncName? f)
    {
        uint flags = f?.FunctionFlags ?? 0;
        // A script function's Func is the interpreter every Blueprint function shares. Its flags, read at its first
        // call, say so even after it was unloaded.
        if (flags != 0 && (flags & FuncNative) == 0) return StringLookup("str.CT.Detail.Script");
        if (f is { Live: false, Unloaded: true }) return StringLookup("str.CT.Detail.NativeNotRead");
        // Flags that could not be read cannot tell native from script, and a script function's Func would pass for
        // a native entry while pointing into the interpreter: no address rather than a misleading one.
        if (flags == 0 || f is not { Live: true }) return StringLookup("str.CT.Detail.KindUnknown");
        if (f.CodeAddr == 0) return StringLookup("str.CT.Detail.NativeNotFound");
        // In the module, the RVA CE resolves after a relaunch; anywhere else, the absolute address of this run. "In"
        // is AddressHelper.TryGetModuleRva's test: the module's size is not on the wire, so it means within 4 GiB above
        // the base.
        string hex = f.CodeAddr.ToString("X", CultureInfo.InvariantCulture);
        var m = _traceModule;
        return m != null && m.CeModuleName.Length > 0 && AddressHelper.TryGetModuleRva(hex, m.ModuleBase, out _)
            ? Say("str.CT.Detail.NativeEntry",
                  AddressHelper.FormatAddress(hex, m.CeModuleName, m.ModuleBase, AddressFormat.ModuleOffset))
            : Say("str.CT.Detail.NativeEntryOutside", Addr(f.CodeAddr));
    }

    internal static string Label(CallTrace t, int i)
    {
        string cls = t.ClassName(i);
        return cls.Length > 0 ? cls + "::" + t.FuncName(i) : t.FuncName(i);
    }

    // ---- export ----

    private enum ExportKind { Jsonl, Csv, ParamsCsv }

    [RelayCommand]
    private Task ExportJsonlAsync() => ExportAsync(".jsonl", "str.CT.Export.JsonlType", ExportKind.Jsonl);

    [RelayCommand]
    private Task ExportCsvAsync() => ExportAsync(".csv", "str.CT.Export.CsvType", ExportKind.Csv);

    /// <summary>[LIVEFUNCS-STEP2] The parameter copies, a row per parameter, in a file of their own
    /// (<see cref="CallTraceExport"/> says why).</summary>
    [RelayCommand]
    private Task ExportParamsCsvAsync() => ExportAsync(".csv", "str.CT.Export.ParamsCsvType", ExportKind.ParamsCsv);

    private async Task ExportAsync(string ext, string typeKey, ExportKind kind)
    {
        var t = _trace;
        if (t == null || _platform == null || !CanExport) return;
        var snaps = t.Snapshots;
        if (kind == ExportKind.ParamsCsv && snaps == null) return;
        bool jsonl = kind == ExportKind.Jsonl;
        try
        {
            ClearError();
            string stem = kind == ExportKind.ParamsCsv ? "call-trace-params-" : "call-trace-";
            string name = stem + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ext;
            string? path = await _platform.ShowSaveFileDialogAsync(name, Res.Get(typeKey), ext);
            if (string.IsNullOrEmpty(path)) return;
            IsExporting = true;
            await Task.Run(() =>
            {
                // CSV carries a BOM so a spreadsheet reads non-ASCII names, as the coordinate CSV does; JSONL does
                // not, like Live Funcs' Save .jsonl.
                using var w = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: !jsonl), 1 << 20);
                switch (kind)
                {
                    case ExportKind.Jsonl: CallTraceExport.WriteJsonl(t, w, DateTime.UtcNow); break;
                    case ExportKind.Csv:   CallTraceExport.WriteCsv(t, w); break;
                    default:               CallTraceExport.WriteParamsCsv(t, w); break;
                }
            });
            if (kind == ExportKind.ParamsCsv)
            {
                StatusText = Res.Format("str.CT.Export.ParamsDone", snaps!.CallsWithParams, path);
                _log.Info($"CallTrace: exported the parameters of {snaps.CallsWithParams} calls to {path}");
            }
            else
            {
                StatusText = Res.Format("str.CT.Export.Done", t.Count, path);
                _log.Info($"CallTrace: exported {t.Count} calls to {path}");
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = Res.Format("str.CT.Export.Failed", ex.Message);
            _log.Error("CallTrace export failed", ex);
        }
        finally { IsExporting = false; }
    }

    /// <summary>The pipe dropped. Every game process numbers its traces from 1, so the generation already shown means
    /// nothing in the next one: forget it, so the next process's first trace is read. The trace on screen stays, marked
    /// as from an earlier connection, and its frames' absolute addresses are no longer sent to CE; a load in progress
    /// stops.</summary>
    public void ClearOnDisconnect()
    {
        _loadedGen = 0;
        _loadCts?.Cancel();
        if (_trace != null) StatusText = Res.Get("str.CT.Status.EarlierConnection");
    }

    /// <summary>Leaving the tab: keep the keyword, and stop reading a trace the user walked away from.</summary>
    public void OnLeavingTab()
    {
        _filterMemory.Flush();
        _activationCts?.Cancel();
        _loadCts?.Cancel();
    }
}

/// <summary>One visible row of the call tree, made when the list asks for it.</summary>
public sealed class CallTraceRow
{
    public int Call { get; init; }
    public double Indent { get; init; }
    public string Glyph { get; init; } = "";
    public string TimeText { get; init; } = "";
    public string DurationText { get; init; } = "";
    public string ThreadText { get; init; } = "";
    public string FunctionText { get; init; } = "";
    /// <summary>[TRACE-UNLOADED-NAMES] Gone when the trace was read: the view marks it beside the name.</summary>
    public bool FuncUnloaded { get; init; }
    /// <summary>Its address held another function during the recording (review DLL-3): marked too.</summary>
    public bool FuncReused { get; init; }
    public string ObjectText { get; init; } = "";
    /// <summary>[LIVEFUNCS-STEP2] U10: the Object cell's tooltip: its whole text, which a narrow column cuts; none for a
    /// call with no object, where an empty tooltip would still pop up.</summary>
    public string? ObjectTip => ObjectText.Length > 0 ? ObjectText : null;
    public bool ObjectStale { get; init; }
    /// <summary>A stale object shows its address dimmed: what is there now may not be what was called.</summary>
    public double ObjectOpacity => ObjectStale ? 0.5 : 1.0;
    public bool IsScopeRoot { get; init; }
    /// <summary>[LIVEFUNCS-STEP2] The call carries a parameter copy: marked in the tree.</summary>
    public bool HasParams { get; init; }
    /// <summary>[LIVEFUNCS-STEP3] The call carries its native stack: marked in the tree too.</summary>
    public bool HasStack { get; init; }
}

/// <summary>[LIVEFUNCS-STEP2] One row of the Parameters tab: a parameter, or a struct member under it.</summary>
public sealed class ParamRow
{
    public string Name { get; init; } = "";
    /// <summary>"in", "const_ref", "out", "in_out" or "return".</summary>
    public string Kind { get; init; } = "";
    public string Type { get; init; } = "";
    public string AtCall { get; init; } = "";
    /// <summary>How far to trust it: "now" (named as it is now), "gone", "header" (data not copied), "hex"; "" exact.</summary>
    public string AtCallMark { get; init; } = "";
    public string After { get; init; } = "";
    public string AfterMark { get; init; } = "";
    /// <summary>The raw bytes differ between the copy at the call and the one after it.</summary>
    public bool Changed { get; init; }
    public int Depth { get; init; }
}

/// <summary>[LIVEFUNCS-STEP3] One row of the Call stack tab: a return address on the call's native stack.</summary>
public sealed class StackFrameRow
{
    /// <summary>Its place on the stack, 0 the nearest: the notes name a frame by it.</summary>
    public int Index { get; init; }
    /// <summary>As the Address setting writes it.</summary>
    public string Address { get; init; } = "";
    public string Where { get; init; } = "";
    /// <summary>What Copy gives, whatever the Address setting.</summary>
    public string CopyText { get; init; } = "";
    /// <summary>The address in the process the trace was read from.</summary>
    public ulong Abs { get; init; }
}

/// <summary>
/// The rows as a read-only list the ListBox virtualizes over: a row object exists only for the rows on screen, made
/// on demand from the trace's columns. A trace holds millions of calls; an ObservableCollection of row objects for
/// them would cost more than the trace itself. A changed tree is a new list, never an edited one.
/// </summary>
public sealed class CallTraceRowList : IList, IReadOnlyList<CallTraceRow>
{
    private readonly CallTrace _t;
    private readonly IReadOnlyList<int> _calls;
    private readonly CallTraceTree? _tree;
    private readonly bool _indented;
    private readonly Dictionary<int, CallTraceRow> _cache = new();

    public CallTraceRowList(CallTrace t, IReadOnlyList<int> calls, CallTraceTree? tree, bool indented)
    {
        _t = t; _calls = calls; _tree = tree; _indented = indented;
    }

    public int Count => _calls.Count;
    public int CallAt(int index) => _calls[index];

    public CallTraceRow this[int index]
    {
        get
        {
            if (_cache.TryGetValue(index, out var row)) return row;
            if (_cache.Count > 4096) _cache.Clear();
            int i = _calls[index];
            bool hasKids = _t.ChildCount[i] > 0;
            row = new CallTraceRow
            {
                Call = i,
                Indent = _indented ? _t.Depth[i] * 14.0 : 0,
                Glyph = !hasKids || _tree == null ? "" : _tree.IsExpanded(i) ? "▾" : "▸",
                TimeText = _t.StartMs(i).ToString("F3", CultureInfo.InvariantCulture),
                DurationText = _t.DurationUs(i) is { } us ? us.ToString("F1", CultureInfo.InvariantCulture) : "—",
                ThreadText = _t.Tid[i].ToString(CultureInfo.InvariantCulture),
                FunctionText = CallTraceViewModel.Label(_t, i) + (hasKids ? $"  ({_t.ChildCount[i]:N0})" : ""),
                FuncUnloaded = _t.FuncUnloaded(i),
                FuncReused = _t.FuncReused(i),
                ObjectText = _t.ObjName(i),
                ObjectStale = _t.ObjStale(i),
                IsScopeRoot = (_t.Flags[i] & TraceRecord.ScopeRootFlag) != 0,
                HasParams = _t.Snapshots?.Has(i) == true,
                HasStack = _t.Stacks?.Has(i) == true,
            };
            _cache[index] = row;
            return row;
        }
    }

    public IEnumerator<CallTraceRow> GetEnumerator()
    {
        for (int k = 0; k < Count; k++) yield return this[k];
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // IList: read-only, for the ItemsControl's index access.
    object? IList.this[int index] { get => this[index]; set => throw new NotSupportedException(); }
    bool IList.IsReadOnly => true;
    bool IList.IsFixedSize => true;
    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    bool IList.Contains(object? value) => value is CallTraceRow r && IndexOf(r) >= 0;
    int IList.IndexOf(object? value) => value is CallTraceRow r ? IndexOf(r) : -1;
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;
    void ICollection.CopyTo(Array array, int index)
    {
        for (int k = 0; k < Count; k++) array.SetValue(this[k], index + k);
    }

    /// <summary>Rows are made on demand, so a row is found by its call, not by reference.</summary>
    private int IndexOf(CallTraceRow r)
    {
        for (int k = 0; k < _calls.Count; k++) if (_calls[k] == r.Call) return k;
        return -1;
    }
}
