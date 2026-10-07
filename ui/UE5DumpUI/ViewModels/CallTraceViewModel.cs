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

    /// <summary>Records per pe_trace_get page: the DLL's maximum, 10 MB of records.</summary>
    internal const int PageRecords = 262144;
    internal const int NamesPage = 20000;

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

    private readonly KeywordSearchMemory _filterMemory;
    public ObservableCollection<string> FilterHistory => _filterMemory.History;

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
        // Leaving Live Funcs mid-recording stops it on the way here; read only once that stop is done.
        try { await LiveFuncs.PendingAutoStop; } catch { /* Live Funcs logs its own failure */ }
        TracePage probe;
        try
        {
            probe = await _dump.PeTraceGetAsync(0, 1);
        }
        catch (Exception ex)
        {
            // An older DLL does not know the command; a dropped pipe has nothing to read. Neither is an error here.
            _log.Debug($"CallTrace: no trace to read ({ex.Message})");
            if (_trace == null) StatusText = Res.Get("str.CT.Status.NoTrace");
            return;
        }
        var i = probe.Info;
        if (i.Allocated && !i.Tracing && i.Quiesced && i.Written > 0 && i.Gen != _loadedGen)
            await LoadAsync();
        else if (_trace == null)
            StatusText = i.Tracing ? Res.Get("str.CT.Status.StillRecording") : Res.Get("str.CT.Status.NoTrace");
    }

    /// <summary>Read the DLL's stopped trace: every page of the ring, the names of what it saw, then free the DLL's
    /// memory and build the tree.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        try
        {
            ClearError();
            IsLoading = true;
            Progress = 0;
            StatusText = Res.Get("str.CT.Status.Reading");

            var head = await _dump.PeTraceGetAsync(0, 1, ct);
            var info = head.Info;
            if (!info.Allocated || info.Written == 0) { StatusText = Res.Get("str.CT.Status.NoTrace"); return; }
            if (info.Tracing) { StatusText = Res.Get("str.CT.Status.StillRecording"); return; }
            if (!info.Quiesced) { StatusText = Res.Get("str.CT.Status.NotQuiesced"); return; }

            ulong kept = info.Written - info.FirstValid;
            var all = new TraceRecord[kept];
            long n = 0;
            ulong from = info.FirstValid;
            while (from < info.Written)
            {
                ct.ThrowIfCancellationRequested();
                var page = await _dump.PeTraceGetAsync(from, PageRecords, ct);
                // The ring belongs to one recording; a new Start in between would hand back another one's records.
                if (page.Info.Gen != info.Gen) { StatusText = Res.Get("str.CT.Status.Changed"); return; }
                var recs = CallTraceBuilder.Decode(page.Data);
                int take = (int)Math.Min(recs.Length, all.LongLength - n);
                Array.Copy(recs, 0, all, n, take);
                n += take;
                if (page.Next <= from) break;   // nothing more: never loop on a page that did not move
                from = page.Next;
                Progress = 0.8 * (from - info.FirstValid) / Math.Max(1.0, kept);
                StatusText = Res.Format("str.CT.Status.ReadingRecords", n, kept);
            }

            StatusText = Res.Get("str.CT.Status.Naming");
            var funcs = new List<TraceFuncName>();
            for (int off = 0; ; off += NamesPage)
            {
                var p = await _dump.PeTraceFuncNamesAsync(info.Gen, off, NamesPage, ct);
                if (p.Stale) { StatusText = Res.Get("str.CT.Status.Changed"); return; }
                funcs.AddRange(p.Items);
                if (p.Items.Count == 0 || off + p.Items.Count >= p.Total || p.Truncated) break;
            }
            Progress = 0.85;
            var objs = new List<TraceObjName>();
            for (int off = 0; ; off += NamesPage)
            {
                var p = await _dump.PeTraceObjNamesAsync(info.Gen, off, NamesPage, ct);
                if (p.Stale) { StatusText = Res.Get("str.CT.Status.Changed"); return; }
                objs.AddRange(p.Items);
                if (p.Items.Count == 0 || off + p.Items.Count >= p.Total || p.Truncated) break;
                Progress = 0.85 + 0.1 * Math.Min(1.0, (off + p.Items.Count) / Math.Max(1.0, p.Total));
            }

            StatusText = Res.Get("str.CT.Status.Building");
            long count = n;
            var trace = await Task.Run(() => CallTraceBuilder.Build(all.AsSpan(0, (int)count), info, funcs, objs), ct);

            // Read: give the game its memory back now, not at the next Start or when the UI disconnects.
            try { await _dump.PeTraceReleaseAsync(info.Gen, ct); }
            catch (Exception ex) { _log.Warn($"CallTrace: release failed ({ex.Message})"); }

            _trace = trace;
            _tree = new CallTraceTree(trace);
            _loadedGen = info.Gen;
            HasTrace = true;
            LiveFuncs.HasTraceToOpen = false;
            _filterMemory.Flush();
            FilterText = "";
            ShowTree(-1);
            Progress = 1;
            StatusText = Summary(trace);
            _log.Info($"CallTrace: loaded {trace.Count} calls ({kept} records, gen {info.Gen}), " +
                      $"{funcs.Count} functions, {objs.Count} objects");
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
        }
    }

    [RelayCommand]
    private void CancelLoad() => _loadCts?.Cancel();

    internal static string Summary(CallTrace t)
    {
        var sb = new StringBuilder(Res.Format("str.CT.Status.Loaded", t.Count, t.WindowSeconds));
        if (t.Info.FirstValid > 0) sb.Append(' ').Append(Res.Format("str.CT.Status.Lapped", t.Info.FirstValid));
        if (t.ReturnsBeforeWindow > 0) sb.Append(' ').Append(Res.Format("str.CT.Status.BeforeWindow", t.ReturnsBeforeWindow));
        sb.Append(' ').Append(t.Info.Ticked > 0 ? Res.Format("str.CT.Status.Scoped", t.Info.Ticked) : Res.Get("str.CT.Status.Unscoped"));
        if (t.Info.Excluded > 0) sb.Append(' ').Append(Res.Format("str.CT.Status.Excluded", t.Info.Excluded));
        return sb.ToString();
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

    partial void OnFilterTextChanged(string value)
    {
        if (_trace == null || _tree == null) return;
        var terms = ObjectTreeFilter.SplitTerms(value);
        if (terms.Length == 0)
        {
            _matches = Array.Empty<int>();
            ShowTree(-1);
            if (_trace != null) StatusText = Summary(_trace);
            return;
        }
        _matches = CallTraceTree.Match(_trace, value, Constants.DefaultMaxQueryRows, out bool capped);
        IsFiltered = true;
        Rows = new CallTraceRowList(_trace, _matches, null, indented: false);
        SelectedIndex = -1;
        StatusText = capped
            ? Res.Format("str.CT.Status.MatchesCapped", _matches.Length)
            : Res.Format("str.CT.Status.Matches", _matches.Length);
        _filterMemory.Schedule(value);
    }

    partial void OnSelectedIndexChanged(int value) => DetailText = Detail(SelectedCall());

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

    /// <summary>What the detail pane says about one call: what ran, on what, when and for how long, and the chain of
    /// callers above it -- the call stack at the UFunction level.</summary>
    internal string Detail(int i)
    {
        var t = _trace;
        if (t == null || i < 0 || i >= t.Count) return "";
        var sb = new StringBuilder();
        sb.AppendLine(Res.Format("str.CT.Detail.Function", Label(t, i)));
        sb.AppendLine(Res.Format("str.CT.Detail.FuncAddr", "0x" + t.Func[i].ToString("X", CultureInfo.InvariantCulture)));
        if (t.Obj[i] != 0)
        {
            sb.AppendLine(t.ObjStale(i)
                ? Res.Format("str.CT.Detail.ObjectStale", "0x" + t.Obj[i].ToString("X", CultureInfo.InvariantCulture))
                : Res.Format("str.CT.Detail.Object", t.ObjName(i), t.ObjClass(i),
                             "0x" + t.Obj[i].ToString("X", CultureInfo.InvariantCulture)));
        }
        sb.AppendLine(Res.Format("str.CT.Detail.Thread", t.Tid[i]));
        sb.AppendLine(Res.Format("str.CT.Detail.Start", t.StartMs(i)));
        sb.AppendLine(t.DurationUs(i) is { } us ? Res.Format("str.CT.Detail.Duration", us) : Res.Get("str.CT.Detail.NoReturn"));
        sb.AppendLine(Res.Format("str.CT.Detail.Children", t.ChildCount[i]));
        if ((t.Flags[i] & TraceRecord.ScopeRootFlag) != 0) sb.AppendLine(Res.Get("str.CT.Detail.ScopeRoot"));
        sb.AppendLine();
        sb.AppendLine(Res.Get("str.CT.Detail.Callers"));
        var chain = new List<int>();
        for (int p = i; p >= 0; p = t.Parent[p]) chain.Add(p);
        chain.Reverse();
        for (int k = 0; k < chain.Count; k++)
            sb.Append(new string(' ', k * 2)).AppendLine(Label(t, chain[k]));
        return sb.ToString();
    }

    internal static string Label(CallTrace t, int i)
    {
        string cls = t.ClassName(i);
        return cls.Length > 0 ? cls + "::" + t.FuncName(i) : t.FuncName(i);
    }

    // ---- export ----

    [RelayCommand]
    private Task ExportJsonlAsync() => ExportAsync(".jsonl", "str.CT.Export.JsonlType", jsonl: true);

    [RelayCommand]
    private Task ExportCsvAsync() => ExportAsync(".csv", "str.CT.Export.CsvType", jsonl: false);

    private async Task ExportAsync(string ext, string typeKey, bool jsonl)
    {
        var t = _trace;
        if (t == null || _platform == null) return;
        try
        {
            ClearError();
            string name = "call-trace-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ext;
            string? path = await _platform.ShowSaveFileDialogAsync(name, Res.Get(typeKey), ext);
            if (string.IsNullOrEmpty(path)) return;
            IsLoading = true;
            await Task.Run(() =>
            {
                // CSV carries a BOM so a spreadsheet reads non-ASCII names, as the coordinate CSV does; JSONL does
                // not, like Live Funcs' Save .jsonl.
                using var w = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: !jsonl), 1 << 20);
                if (jsonl) CallTraceExport.WriteJsonl(t, w, DateTime.UtcNow);
                else CallTraceExport.WriteCsv(t, w);
            });
            StatusText = Res.Format("str.CT.Export.Done", t.Count, path);
            _log.Info($"CallTrace: exported {t.Count} calls to {path}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = Res.Format("str.CT.Export.Failed", ex.Message);
            _log.Error("CallTrace export failed", ex);
        }
        finally { IsLoading = false; }
    }

    /// <summary>Leaving the tab: keep the keyword, and stop reading a trace the user walked away from.</summary>
    public void OnLeavingTab()
    {
        _filterMemory.Flush();
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
    public string ObjectText { get; init; } = "";
    public bool ObjectStale { get; init; }
    /// <summary>A stale object shows its address dimmed: what is there now may not be what was called.</summary>
    public double ObjectOpacity => ObjectStale ? 0.5 : 1.0;
    public bool IsScopeRoot { get; init; }
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
                ObjectText = _t.ObjName(i),
                ObjectStale = _t.ObjStale(i),
                IsScopeRoot = (_t.Flags[i] & TraceRecord.ScopeRootFlag) != 0,
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
