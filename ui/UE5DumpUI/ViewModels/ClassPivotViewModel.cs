using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>One field of the pivot class, pickable as a projected value field
/// and annotated with key/value suitability scores.</summary>
public partial class PivotFieldPick : ObservableObject
{
    public PivotFieldInfo Info { get; }
    public double KeyScore  { get; }
    public int    ValueScore { get; }

    public PivotFieldPick(PivotFieldInfo info, double keyScore, int valueScore)
    {
        Info = info;
        KeyScore = keyScore;
        ValueScore = valueScore;
    }

    [ObservableProperty] private bool _isValue;

    /// <summary>Tick to add this field to the composite group key (Field mode). The
    /// group key becomes the TUPLE of the primary key field + every ticked key field.
    /// Inert in Identity / DataTable / Array modes (which have their own key).</summary>
    [ObservableProperty] private bool _isKey;

    public string Name          => Info.Name;
    public string DeclaredType  => Info.DeclaredType;
    public int    DistinctCount => Info.DistinctCount;
    public int    InstanceCount => Info.InstanceCount;
    public string KeyScoreDisplay => KeyScore.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>One snapshot offered in the Suggest-Targets picker. Tick 2-4 of them —
/// ordered oldest → newest = Before … After — to define the change-discovery
/// sequence. With 3-4 the engine can tell a one-time event from per-frame noise.</summary>
public partial class DiscoverSnapshotPick : ObservableObject
{
    public SnapshotMeta Meta { get; }
    public long   Id      => Meta.Id;
    public string Display => Meta.PickerDisplay;
    public DiscoverSnapshotPick(SnapshotMeta meta) => Meta = meta;
    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// ViewModel for the experimental Class Pivot tab. Groups one class's captured
/// instances by intrinsic identity or a chosen key field and projects value
/// fields per group. Pure C# over the SQLite corpus — no DLL/pipe. A key-field
/// suggestion (reusing PropertyScoringTable + a type/name/cardinality scorer)
/// dissolves the "user must guess the business key" problem. See
/// docs/experimental-snapshot-spc-pivot.md §"Phase C".
/// </summary>
public partial class ClassPivotViewModel : ViewModelBase
{
    private readonly ISnapshotStore _store;
    private readonly ILoggingService _log;
    private readonly IPlatformService? _platform;
    private readonly IDumpService? _dump;
    private EngineState? _engineState;
    private readonly List<PivotClassInfo> _allClasses = new();
    /// <summary>Rows of the selected DataTable, cached so Run projects without a
    /// second pipe round-trip (Phase C4).</summary>
    private DataTableWalkResult? _dataTable;
    /// <summary>Rows requested per DataTable walk. A single page keeps C4 simple;
    /// the status row reports when a table exceeds it.</summary>
    private const int DataTableRowLimit = 2000;
    // Monotonic guards so a stale (superseded) async load can't clobber the
    // collections after a newer snapshot/class selection. Rapidly switching the
    // class ComboBox would otherwise interleave two loads at the await boundary
    // and leave Fields holding a mix of two classes.
    private int _classLoadId;
    private int _fieldLoadId;
    private CancellationTokenSource? _pivotCts;   // heavy pivot run
    private CancellationTokenSource? _loadCts;    // class / field list loads (GROUP BY over ~1.7M rows)

    [ObservableProperty] private string _selectedSource = "Snapshot";
    [ObservableProperty] private SnapshotMeta? _selectedSnapshot;
    [ObservableProperty] private string _classFilter = "";
    [ObservableProperty] private PivotClassInfo? _selectedClass;
    [ObservableProperty] private DataTablePick? _selectedDataTable;
    [ObservableProperty] private PivotArrayFieldInfo? _selectedArrayField;
    [ObservableProperty] private string _selectedKeyMode = "Identity (object path)";
    [ObservableProperty] private string? _selectedKeyField;
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private PivotResultRow? _selectedResult;
    /// <summary>GWorld availability (gates the result 🌍 Locate button; ⚙ Engine is not
    /// GWorld-gated). Set from the engine state on connect.</summary>
    /// <summary>Client-side substring filter over the results grid (key + values).</summary>
    [ObservableProperty] private string _resultFilter = "";

    // --- Change-driven discovery (C3): the automatic front-door. Instead of making
    // the user guess which class/key to pivot when the target is unknown, find the
    // (class, prop) targets whose value MOVED between two snapshots (capture
    // before/after an in-game action), ranked by "looks like game state". The user
    // picks from a short list; "Use →" fills the pivot below and runs it. Pure C#
    // over the SQLite corpus (reuses the SPC intersection load). See
    // docs/experimental-snapshot-spc-pivot.md §"Phase C — C3".
    [ObservableProperty] private bool   _isDiscovering;
    [ObservableProperty] private string _discoverStatus = "";
    [ObservableProperty] private DiscoveryCandidate? _selectedDiscoverResult;
    /// <summary>The newest selected snapshot — the one the pivot targets on "Use →"
    /// (its values + addresses are current).</summary>
    private SnapshotMeta? _discoverNewest;
    private CancellationTokenSource? _discoverCts;

    public ObservableCollection<DiscoveryCandidate> DiscoverResults { get; } = new();

    /// <summary>The snapshots offered to the discovery picker (newest first). Tick
    /// 2-4 to define the Before … After sequence.</summary>
    public ObservableCollection<DiscoverSnapshotPick> DiscoverPicks { get; } = new();

    /// <summary>Discovery needs 2-4 distinct snapshots (Before … After). With 3-4 it
    /// can separate a one-time event from per-frame noise (the shape ranking).</summary>
    public bool CanDiscover => !IsDiscovering && !IsBusy
        && SelectedDiscoverCount is >= 2 and <= 4;

    /// <summary>How many snapshots the user has ticked in the discovery picker.</summary>
    public int SelectedDiscoverCount => DiscoverPicks.Count(p => p.IsSelected);

    /// <summary>Live hint under the picker reflecting the current selection count.</summary>
    public string DiscoverHint => SelectedDiscoverCount < 2
        ? "Pick at least 2 snapshots (Before … After)."
        : SelectedDiscoverCount > 4
            ? "Pick at most 4 snapshots."
            : $"{SelectedDiscoverCount} snapshots selected.";

    /// <summary>Pivot data source: persisted snapshots (scalar), persisted
    /// snapshot struct-arrays (inner-key pivot — Phase C6), or a live DataTable
    /// (zero-config — RowName is the key). DataTable mode needs a live connection.</summary>
    public IReadOnlyList<string> SourceOptions { get; } =
        new[] { "Snapshot", "Snapshot Array", "DataTable" };

    public IReadOnlyList<string> KeyModeOptions { get; } =
        new[] { "Identity (object path)", "Field" };

    public ObservableCollection<SnapshotMeta> Snapshots { get; } = new();
    public ObservableCollection<PivotClassInfo> Classes { get; } = new();
    public ObservableCollection<DataTablePick> DataTables { get; } = new();
    public ObservableCollection<PivotArrayFieldInfo> ArrayFields { get; } = new();
    public ObservableCollection<PivotFieldPick> Fields { get; } = new();
    public ObservableCollection<string> KeyFieldOptions { get; } = new();
    public ObservableCollection<PivotResultRow> Results { get; } = new();

    /// <summary>Raised to open a pivot group's representative object in Live Walker.</summary>
    public event Action<string>? NavigateToInstance;

    /// <summary>Locate the selected group's representative object within the GWorld
    /// graph (shortest pointer path GWorld → object, shown in Live Walker).</summary>
    public event Action<string>? LocateInGWorld;
    /// <summary>Engine-rooted counterpart (path rooted at the live UGameEngine) —
    /// reaches engine-layer objects the GWorld graph can't.</summary>
    public event Action<string>? LocateInGameEngine;

    /// <summary>A result row is selected, so its representative object can be located.</summary>
    public bool CanLocateResult => SelectedResult != null;
    /// <summary>Same precondition as <see cref="CanLocateResult"/>. NOT gated on the client
    /// IsGWorldAvailable flag: the DLL is the source of truth for GWorld, and a stale/false
    /// flag disabled this button on games where GWorld WAS resolved (audit #5 AE10).</summary>
    public bool CanLocateResultInGWorld => SelectedResult != null;

    partial void OnSelectedResultChanged(PivotResultRow? value)
    {
        OnPropertyChanged(nameof(CanLocateResult));
        OnPropertyChanged(nameof(CanLocateResultInGWorld));
    }
    partial void OnResultFilterChanged(string value)
    {
        ApplyResultFilter();
        _resultFilterMemory.Schedule(value);
    }

    public bool IsSnapshotSource  => SelectedSource == "Snapshot";
    public bool IsArraySource     => SelectedSource == "Snapshot Array";
    public bool IsDataTableSource => SelectedSource == "DataTable";
    /// <summary>Both snapshot modes pick from the same snapshot + class pickers.</summary>
    public bool UsesSnapshot      => IsSnapshotSource || IsArraySource;
    public bool IsFieldKeyMode    => SelectedKeyMode == "Field";
    /// <summary>The key-field picker is only meaningful for scalar snapshot Field
    /// mode; Array keys on the inner-key value, DataTable on RowName.</summary>
    public bool ShowKeyField => IsSnapshotSource && IsFieldKeyMode;

    public bool CanRunPivot => !IsBusy && (
        IsDataTableSource ? (SelectedDataTable != null && _dataTable != null) :
        IsArraySource     ? (SelectedSnapshot != null && SelectedClass != null && SelectedArrayField != null) :
        (SelectedSnapshot != null && SelectedClass != null
            && (!IsFieldKeyMode || !string.IsNullOrEmpty(SelectedKeyField))));

    /// <summary>The most recently started class/field load, started by a
    /// snapshot/class selection change. Exposed so tests can await the
    /// fire-and-forget chain deterministically; the live UI ignores it.</summary>
    public Task? PendingLoad { get; private set; }

    /// <summary>Per-session remembered class-filter keywords (LRU) surfaced as the
    /// class-filter box's AutoCompleteBox suggestions — see <see cref="KeywordSearchMemory"/>.</summary>
    private readonly KeywordSearchMemory _classFilterMemory;
    public ObservableCollection<string> ClassFilterHistory => _classFilterMemory.History;

    /// <summary>Per-session remembered result-filter keywords (LRU) surfaced as the
    /// results-grid filter box's AutoCompleteBox suggestions.</summary>
    private readonly KeywordSearchMemory _resultFilterMemory;
    public ObservableCollection<string> ResultFilterHistory => _resultFilterMemory.History;

    public ClassPivotViewModel(ISnapshotStore store, ILoggingService log,
                               IPlatformService? platform = null, IDumpService? dump = null)
    {
        _store = store;
        _log = log;
        _platform = platform;
        _dump = dump;
        _classFilterMemory  = new KeywordSearchMemory(() => (ClassFilter, Classes.Count > 0));
        _resultFilterMemory = new KeywordSearchMemory(() => (ResultFilter, Results.Count > 0));
    }

    public void SetEngineState(EngineState state)
    {
        _engineState = state;
        _store.SetActiveGame(state.PeHash);
        LoadDenylistFromStore();
        // A new connection invalidates any DataTable list/rows from a prior game.
        DataTables.Clear();
        _dataTable = null;
        SelectedDataTable = null;
        _ = RefreshAsync();
        if (IsDataTableSource) PendingLoad = RefreshDataTablesAsync();
    }

    // --- N1: Pivot-scope class denylist (right-click "Hide this class") ---
    // Independent from the SPC / Diff lists — hiding a class here only affects
    // the Pivot class picker.
    private HashSet<string> _excludedClasses = new(StringComparer.Ordinal);

    /// <summary>Classes the user hid from the Pivot picker (display + remove).</summary>
    public ObservableCollection<string> ActiveDenylist { get; } = new();

    /// <summary>True when at least one class is hidden — drives the chips bar's
    /// visibility (Avalonia won't bind an int Count to a bool IsVisible).</summary>
    public bool HasHiddenClasses => ActiveDenylist.Count > 0;

    private void LoadDenylistFromStore()
    {
        _excludedClasses = _store.GetClassDenylist(DenylistScope.Pivot);
        ActiveDenylist.Clear();
        foreach (var c in _excludedClasses.OrderBy(s => s, StringComparer.Ordinal))
            ActiveDenylist.Add(c);
        OnPropertyChanged(nameof(HasHiddenClasses));
    }

    /// <summary>Right-click "Hide this class" → add to the Pivot denylist and
    /// drop it from the picker. Operates on the right-clicked row (or the
    /// selected class as a fallback).</summary>
    [RelayCommand]
    private async Task HideClassAsync(PivotClassInfo? cls)
    {
        var name = cls?.ClassName ?? SelectedClass?.ClassName;
        if (string.IsNullOrEmpty(name) || _excludedClasses.Contains(name)) return;
        var updated = new HashSet<string>(_excludedClasses, StringComparer.Ordinal) { name };
        _store.SetClassDenylist(DenylistScope.Pivot, updated);
        LoadDenylistFromStore();
        if (ReferenceEquals(SelectedClass, cls)) SelectedClass = null;
        await LoadClassesAsync();   // refresh the dropdown without the hidden class
    }

    /// <summary>Remove one class from the Pivot denylist (chip click) → it
    /// reappears in the picker.</summary>
    [RelayCommand]
    private async Task RemoveFromDenylistAsync(string? className)
    {
        if (string.IsNullOrEmpty(className) || !_excludedClasses.Contains(className)) return;
        var updated = new HashSet<string>(_excludedClasses, StringComparer.Ordinal);
        updated.Remove(className);
        _store.SetClassDenylist(DenylistScope.Pivot, updated);
        LoadDenylistFromStore();
        await LoadClassesAsync();
    }

    /// <summary>Drop every class from the Pivot denylist.</summary>
    [RelayCommand]
    private async Task ClearDenylistAsync()
    {
        if (_excludedClasses.Count == 0) return;
        _store.SetClassDenylist(DenylistScope.Pivot, new HashSet<string>(StringComparer.Ordinal));
        LoadDenylistFromStore();
        await LoadClassesAsync();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunPivot));
        OnPropertyChanged(nameof(CanDiscover));
    }
    partial void OnIsDiscoveringChanged(bool value)         => OnPropertyChanged(nameof(CanDiscover));
    partial void OnSelectedSnapshotChanged(SnapshotMeta? value)
    {
        // While RefreshAsync rebuilds the Snapshots collection, the detach
        // (SelectedSnapshot=null) and the final re-selection must NOT re-enter the
        // class load — that runs Classes.Clear() inside the Snapshots Clear/Add and
        // trips Avalonia's "Source collection was modified during selection update".
        // RefreshAsync triggers the load itself once the rebuild has settled.
        if (_refreshing) return;
        PendingLoad = LoadClassesAsync();
    }
    partial void OnSelectedKeyFieldChanged(string? value) => OnPropertyChanged(nameof(CanRunPivot));
    partial void OnSelectedDataTableChanged(DataTablePick? value) => PendingLoad = LoadDataTableFieldsAsync();
    partial void OnSelectedArrayFieldChanged(PivotArrayFieldInfo? value)
    {
        OnPropertyChanged(nameof(CanRunPivot));
        PendingLoad = LoadArrayPropsAsync();
    }

    partial void OnSelectedSourceChanged(string value)
    {
        OnPropertyChanged(nameof(IsSnapshotSource));
        OnPropertyChanged(nameof(IsArraySource));
        OnPropertyChanged(nameof(IsDataTableSource));
        OnPropertyChanged(nameof(UsesSnapshot));
        OnPropertyChanged(nameof(ShowKeyField));
        OnPropertyChanged(nameof(CanRunPivot));
        // Switching source clears the cross-source picker/result state so a stale
        // field list or row can't be Run against the wrong source. Detach bound
        // selections before clearing (Avalonia selection-model safety).
        SelectedKeyField = null;
        SelectedArrayField = null;
        SelectedResult = null;
        Fields.Clear();
        KeyFieldOptions.Clear();
        ArrayFields.Clear();
        Results.Clear();
        StatusText = "";
        if (IsDataTableSource)
        {
            if (DataTables.Count == 0) PendingLoad = RefreshDataTablesAsync();
        }
        else
        {
            // Snapshot vs Snapshot-Array have different class lists (array classes
            // are a subset) — reload for the new source.
            PendingLoad = LoadClassesAsync();
        }
    }

    partial void OnSelectedKeyModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsFieldKeyMode));
        OnPropertyChanged(nameof(ShowKeyField));
        OnPropertyChanged(nameof(CanRunPivot));
    }

    partial void OnSelectedClassChanged(PivotClassInfo? value)
    {
        OnPropertyChanged(nameof(CanRunPivot));
        PendingLoad = IsArraySource ? LoadArrayFieldsAsync() : LoadFieldsAsync();
    }

    partial void OnClassFilterChanged(string value)
    {
        ApplyClassFilter();
        _classFilterMemory.Schedule(value);
    }

    /// <summary>C5 right-click handoff target: switch to scalar Snapshot source,
    /// select <paramref name="className"/> in the newest snapshot, and surface
    /// <paramref name="propName"/> (ticked as a value field unless it became the
    /// auto-suggested key). No-op with a hint if no snapshot or class match. The
    /// caller (MainWindowViewModel) has already switched to the Class Pivot tab.</summary>
    public async Task PivotForAsync(string className, string? propName)
    {
        try
        {
            ClearError();
            SelectedSource = "Snapshot";
            if (Snapshots.Count == 0)
                await RefreshAsync();             // loads snapshots -> newest -> classes
            var pending = PendingLoad;            // class load kicked off by the above
            if (pending != null) { try { await pending; } catch { /* surfaced via SetError */ } }

            if (SelectedSnapshot == null)
            {
                StatusText = "No snapshot captured yet — capture one on the Snapshot tab first.";
                return;
            }

            if (await SelectClassAndTickPropAsync(className, propName) && !string.IsNullOrEmpty(propName))
                StatusText = $"Ready: {className} · {propName} — press Run Pivot.";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, $"Pivot: handoff for {className}.{propName} failed", ex);
            SetError(ex);
        }
    }

    /// <summary>Select <paramref name="className"/> in the CURRENTLY selected
    /// snapshot and tick <paramref name="propName"/> as a value field. Assumes the
    /// snapshot + class list are already loaded; awaits the field load it triggers.
    /// Returns false (with a status hint) when the class isn't in the snapshot.
    /// Shared by the C5 right-click handoff (<see cref="PivotForAsync"/>) and the
    /// change-driven discovery "Use →" (<see cref="UseDiscoverCandidateAsync"/>).</summary>
    private async Task<bool> SelectClassAndTickPropAsync(string className, string? propName)
    {
        // Clear any active class filter so the target class is visible in the picker, then
        // match against the FULL list (_allClasses) — Classes itself is now the filtered
        // view, which might not contain the handoff target.
        if (!string.IsNullOrEmpty(ClassFilter)) ClassFilter = "";   // OnClassFilterChanged → rebuilds Classes to the full set
        var match = _allClasses.FirstOrDefault(c => c.ClassName == className);
        if (match == null)
        {
            StatusText = $"'{className}' is not in the selected snapshot — capture it, then retry.";
            return false;
        }

        if (ReferenceEquals(SelectedClass, match))
            PendingLoad = LoadFieldsAsync();  // already selected → no change event; reload explicitly
        else
            SelectedClass = match;            // triggers the field load
        var pending = PendingLoad;
        if (pending != null) { try { await pending; } catch { /* surfaced via SetError */ } }

        if (!string.IsNullOrEmpty(propName) && SelectedKeyField != propName)
        {
            var pick = Fields.FirstOrDefault(f => f.Name == propName);
            if (pick != null) pick.IsValue = true;
        }
        return true;
    }

    // Re-entrancy guard for the Snapshots rebuild: coalesces overlapping refreshes and
    // suppresses the selection-triggered class load while the collection is mutating
    // (see OnSelectedSnapshotChanged). Without it, a recapture-then-open sequence hits
    // Avalonia's "Source collection was modified during selection update" — RefreshAsync
    // then throws, the snapshot list silently fails to update, and the user sees stale
    // ("old data") snapshots.
    private bool _refreshing;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_refreshing) return;   // coalesce overlapping refreshes (rapid tab re-entry / clicks)
        // Off the UI thread: Microsoft.Data.Sqlite "*Async" runs synchronously on the
        // caller, so awaiting it on the UI thread would block + run the collection
        // rebuild inline inside a binding event (the crash).
        // Exclude UNUSABLE snapshots (captures that spanned a GObjects drift): a
        // pivot over temporally-inconsistent rows mixes two worlds' instances.
        var list = (await Task.Run(() => _store.ListSnapshotsAsync()))
            .Where(m => m.IsUsable).ToList();
        _refreshing = true;
        try
        {
            // Rebuild with the selection-triggered class load SUPPRESSED (the detach +
            // re-select must not run Classes.Clear() during the Snapshots Clear/Add).
            UiCollection.Reset(Snapshots, list, () => SelectedSnapshot = null);
            // Drop cache entries for snapshots that no longer exist (deleted) so a
            // recaptured Id can't ever read a stale class/field list.
            var liveIds = new HashSet<long>(list.Select(s => s.Id));
            foreach (var k in _classCache.Keys.Where(k => !liveIds.Contains(k.Item1)).ToList())
                _classCache.Remove(k);
            foreach (var k in _fieldCache.Keys.Where(k => !liveIds.Contains(k.Item1)).ToList())
                _fieldCache.Remove(k);
            // Default to the newest snapshot.
            SelectedSnapshot = Snapshots.Count > 0 ? Snapshots[0] : null;
            // Rebuild the discovery picker, defaulting to the two newest ticked — the
            // common "capture before/after an action" flow.
            RebuildDiscoverPicks();
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Pivot: list snapshots failed", ex);
            SetError(ex);
        }
        finally
        {
            _refreshing = false;
        }
        // Now that the collection rebuild has settled and suppression is lifted, load
        // classes for the final selection once — no re-entrancy into the selection model.
        if (SelectedSnapshot != null) PendingLoad = LoadClassesAsync();
    }

    // (Re)build the Suggest-Targets picker from the current snapshot list (newest
    // first), defaulting to the two newest ticked. Subscribes each pick so CanDiscover
    // / the count / the hint update live as the user ticks 2-4.
    private void RebuildDiscoverPicks()
    {
        foreach (var p in DiscoverPicks) p.PropertyChanged -= OnDiscoverPickChanged;
        DiscoverPicks.Clear();
        for (int i = 0; i < Snapshots.Count; i++)
        {
            var pick = new DiscoverSnapshotPick(Snapshots[i]) { IsSelected = i < 2 };
            pick.PropertyChanged += OnDiscoverPickChanged;
            DiscoverPicks.Add(pick);
        }
        OnPropertyChanged(nameof(CanDiscover));
        OnPropertyChanged(nameof(SelectedDiscoverCount));
        OnPropertyChanged(nameof(DiscoverHint));
    }

    private void OnDiscoverPickChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DiscoverSnapshotPick.IsSelected)) return;
        OnPropertyChanged(nameof(CanDiscover));
        OnPropertyChanged(nameof(SelectedDiscoverCount));
        OnPropertyChanged(nameof(DiscoverHint));
    }

    // Per-snapshot class-list cache. A snapshot is write-once / immutable, so its
    // class list never changes — compute it ONCE and reuse for the rest of the
    // session instead of re-running the ~1.7M-row GROUP BY on every dropdown
    // change. Keyed by (snapshotId, arrayMode); pruned in RefreshAsync when a
    // snapshot is deleted. The denylist filter is applied on top of the cached
    // list (so hiding a class never needs a re-scan).
    private readonly Dictionary<(long, bool), IReadOnlyList<PivotClassInfo>> _classCache = new();

    private async Task LoadClassesAsync()
    {
        OnPropertyChanged(nameof(CanRunPivot));
        // Supersede any in-flight (stale) class load at ENTRY — the cache-hit and
        // empty early-return paths below must bump this too, so a slower scan
        // started by a prior snapshot bails (id mismatch) instead of clobbering the
        // picker when it finally completes. Bumping only on the cache-MISS path left
        // a hole: a miss in flight, then a switch to an ALREADY-cached snapshot, let
        // the miss complete and overwrite the cached list with the wrong snapshot's.
        int id = ++_classLoadId;
        if (SelectedSnapshot == null) { _allClasses.Clear(); Classes.Clear(); return; }
        long snapId = SelectedSnapshot.Id;
        bool arrayMode = IsArraySource;
        var key = (snapId, arrayMode);

        // Cache hit → apply instantly, no scan, no thread.
        if (_classCache.TryGetValue(key, out var cachedList))
        {
            ApplyClassList(cachedList);
            StatusText = $"{_allClasses.Count:N0} classes — filter + pick one to pivot.  {CacheNote()}";
            return;
        }

        // Cache miss → scan once. Cancel a prior in-flight load so rapidly
        // changing the snapshot doesn't stack several heavy scans on the pool.
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = _loadCts = new CancellationTokenSource();
        var ct = cts.Token;
        StatusText = "Loading classes… (first time for this snapshot)";
        try
        {
            // Array mode lists only classes that captured struct-array elements.
            var list = await Task.Run(() => arrayMode
                ? _store.ListPivotArrayClassesAsync(snapId, ct)
                : _store.ListPivotClassesAsync(snapId, ct), ct);
            // Cache BEFORE the supersession guard: the (snapId, arrayMode) key is immutable
            // (write-once snapshot), so a superseded-but-complete result is still correct to
            // cache. This collapses a rapid re-fire to cache hits instead of re-opening the DB
            // every time (defence against any picker re-selection storm hammering the DB).
            _classCache[key] = list;
            if (id != _classLoadId) return;   // superseded -> skip the UI apply only
            ApplyClassList(list);
            StatusText = $"{_allClasses.Count:N0} classes — filter + pick one to pivot.  {CacheNote()}";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection — leave the status to the new load.
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Pivot: list classes failed", ex);
            SetError(ex);
        }
    }

    // Apply a (cached or fresh) class list to the picker, filtered by the
    // Pivot-scope denylist. N1: hidden classes never appear (independent of the
    // SPC / Diff denylists — per-tab isolation).
    private void ApplyClassList(IReadOnlyList<PivotClassInfo> list)
    {
        _allClasses.Clear();
        foreach (var c in list)
            if (!_excludedClasses.Contains(c.ClassName)) _allClasses.Add(c);
        ApplyClassFilter();
    }

    /// <summary>Lightweight memory/cache readout for the status line (the user asked to see
    /// "how much memory is used"). The cached class + per-class field lists live on the managed
    /// heap; this shows the cached-field-set count and the current heap size. Cheap to compute.</summary>
    private string CacheNote()
        => $"({_fieldCache.Count} field-set(s) cached · ~{GC.GetTotalMemory(false) / (1024.0 * 1024.0):N0} MB heap)";

    private void ApplyClassFilter()
    {
        // Filter the class dropdown by space-separated AND terms over the ClassFilter box
        // (each term must hit ClassName or Display, so "attribute set" surfaces
        // "CharacterAttributeSet") — the shared Object Tree filter semantics. The picker
        // binds this filtered Classes collection to a plain ListBox — its Text box is an
        // AutoCompleteBox whose Text ONLY (never SelectedItem) is bound, so the internal
        // Text<->SelectedItem reconciliation that oscillated SelectedClass and froze the UI
        // never happens. Rebuild via UiCollection.Reset, which DETACHES SelectedClass before
        // mutating the collection — that detach is what makes a per-keystroke rebuild safe
        // (the old Clear()/Add() without the detach is what dropped clicks). Typing here sets
        // SelectedClass=null → no field load, no DB — so filtering never touches the DB.
        var terms = ObjectTreeFilter.SplitTerms(ClassFilter);
        var view = (terms.Length == 0
            ? (IEnumerable<PivotClassInfo>)_allClasses
            : _allClasses.Where(c =>
                ObjectTreeFilter.MatchesAllTerms(terms, c.ClassName, c.Display))).ToList();

        // Skip the rebuild when the filtered set is identical to what's already shown. A spurious
        // re-filter (same text — e.g. fired again right after a click) must NOT Clear()/Add() the
        // collection: the UiCollection.Reset detach below would null SelectedClass and wipe the
        // just-picked class. That detach firing post-click is the "click flickers then clears, no
        // selection" bug seen with BOTH the ComboBox and the ListBox.
        if (Classes.Count == view.Count && Classes.SequenceEqual(view)) return;

        // Genuine filter change: rebuild (the detach is required for Avalonia selection-model
        // safety), then RE-SELECT the previously-picked class if it survived the filter — so even
        // a real filter change can't silently drop a live selection that still matches.
        var keep = SelectedClass;
        UiCollection.Reset(Classes, view, () => SelectedClass = null);
        if (keep != null && Classes.Contains(keep)) SelectedClass = keep;
    }

    // Per-(snapshot, class) field-list cache — same immutability rationale as the
    // class cache: a snapshot's fields for a class never change, so scan once.
    private readonly Dictionary<(long, string), IReadOnlyList<PivotFieldInfo>> _fieldCache = new();

    private async Task LoadFieldsAsync()
    {
        // Supersede any in-flight (stale) field load at ENTRY so a slower scan from
        // a prior class can't clobber the picker — including when the newer class is
        // served from the cache-hit path below (which returns without a scan). Same
        // rationale as LoadClassesAsync: bumping only on cache-MISS left the hole.
        int id = ++_fieldLoadId;
        if (SelectedSnapshot == null || SelectedClass == null)
        {
            Fields.Clear(); KeyFieldOptions.Clear(); Results.Clear();
            return;
        }
        long snapId = SelectedSnapshot.Id;
        string cls = SelectedClass.ClassName;
        var key = (snapId, cls);

        // Cache hit → rebuild the picker instantly (no scan).
        if (_fieldCache.TryGetValue(key, out var cachedFields))
        {
            ApplyFieldList(cachedFields, cls);
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = _loadCts = new CancellationTokenSource();
        var ct = cts.Token;
        StatusText = "Loading fields…";
        try
        {
            var fields = await Task.Run(() => _store.ListPivotFieldsAsync(snapId, cls, ct), ct);
            // Cache BEFORE the supersession guard: the (snapId, cls) key is immutable
            // (write-once snapshot), so a superseded-but-complete result is still correct to
            // cache. This collapses a rapid re-fire to cache hits (no repeated OpenAsync) —
            // the defence that makes the ~135/sec DB-open storm impossible regardless of any
            // future picker re-selection storm.
            _fieldCache[key] = fields;
            // A newer class selection superseded us — skip the UI apply only (so a stale load
            // can't wipe the latest); the cache above is still populated.
            if (id != _fieldLoadId) return;
            ApplyFieldList(fields, cls);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer class/snapshot selection.
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Pivot: list fields failed", ex);
            SetError(ex);
        }
    }

    // Rebuild the field picker (+ key suggestion + value pre-ticks) from a cached
    // or freshly-loaded field list. Pure UI work — no DB.
    private void ApplyFieldList(IReadOnlyList<PivotFieldInfo> fields, string cls)
    {
        SelectedKeyField = null;   // detach selections before clearing bound lists
        SelectedResult = null;
        Fields.Clear();
        KeyFieldOptions.Clear();
        Results.Clear();
        foreach (var f in fields)
        {
            Fields.Add(new PivotFieldPick(f,
                PivotKeyScorer.KeyScore(f), PivotKeyScorer.ValueScore(cls, f.Name)));
            KeyFieldOptions.Add(f.Name);
        }

        // Suggest a key: pick the best-scoring field. If it scores well, default
        // to Field mode on it; otherwise fall back to Identity.
        var suggested = PivotKeyScorer.SuggestKey(fields);
        if (suggested != null && PivotKeyScorer.KeyScore(suggested) >= 0.5)
        {
            SelectedKeyField = suggested.Name;
            SelectedKeyMode  = "Field";
        }
        else
        {
            SelectedKeyField = KeyFieldOptions.FirstOrDefault();
            SelectedKeyMode  = "Identity (object path)";
        }

        // Pre-tick the most interesting value fields (excludes the key), capped so
        // a wide class doesn't project dozens of columns.
        int ticked = 0;
        foreach (var p in Fields.OrderByDescending(p => p.ValueScore))
        {
            if (ticked >= 3) break;
            if (p.Name == SelectedKeyField) continue;
            if (p.ValueScore >= PropertyScoringTable.InterestingThreshold)
            {
                p.IsValue = true;
                ticked++;
            }
        }
        StatusText = $"{Fields.Count} fields · suggested key: {SelectedKeyField ?? "(none)"}  {CacheNote()}";
    }

    /// <summary>List live DataTable instances (and subclasses) for the C4 picker.</summary>
    [RelayCommand]
    private async Task RefreshDataTablesAsync()
    {
        if (_dump == null) return;
        try
        {
            var res = await _dump.FindInstancesAsync("DataTable", exactMatch: false, limit: 1000);
            var picks = res.Instances
                .Where(i => i.ClassName.IndexOf("DataTable", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Select(i => new DataTablePick(i.Name, i.Address, i.ClassName));
            UiCollection.Reset(DataTables, picks, () => SelectedDataTable = null);
            StatusText = $"{DataTables.Count} DataTable(s) found";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Pivot: list DataTables failed", ex);
            SetError(ex);
        }
    }

    /// <summary>Walk the selected DataTable and surface its row struct fields as the
    /// value-field picker. RowName is the implicit key — no key discovery needed.</summary>
    private async Task LoadDataTableFieldsAsync()
    {
        // Bump the guard at entry (not after the null-check) so a null/empty
        // selection also supersedes any in-flight load — otherwise a slow load
        // for the previous DataTable could complete and repopulate Fields after
        // the selection was cleared (same race fixed in LoadClasses/Fields).
        int id = ++_fieldLoadId;
        _dataTable = null;
        SelectedKeyField = null;
        SelectedResult = null;
        Fields.Clear();
        KeyFieldOptions.Clear();
        Results.Clear();
        OnPropertyChanged(nameof(CanRunPivot));
        if (_dump == null || SelectedDataTable == null) return;

        try
        {
            var dt = await _dump.WalkDataTableRowsAsync(SelectedDataTable.Address, 0, DataTableRowLimit);
            if (id != _fieldLoadId) return;   // a newer DataTable selection superseded us
            _dataTable = dt;

            var fields = DataTablePivotEngine.Fields(dt);
            string structName = dt.RowStructName;
            foreach (var f in fields)
            {
                Fields.Add(new PivotFieldPick(f,
                    PivotKeyScorer.KeyScore(f), PivotKeyScorer.ValueScore(structName, f.Name)));
                KeyFieldOptions.Add(f.Name);
            }

            // Pre-tick the most interesting value fields (there is no key to exclude).
            int ticked = 0;
            foreach (var p in Fields.OrderByDescending(p => p.ValueScore))
            {
                if (ticked >= 3) break;
                if (p.ValueScore >= PropertyScoringTable.InterestingThreshold) { p.IsValue = true; ticked++; }
            }
            // If nothing scored as interesting, tick the first few so Run shows data.
            if (ticked == 0)
                foreach (var p in Fields.Take(3)) p.IsValue = true;

            OnPropertyChanged(nameof(CanRunPivot));
            var trunc = dt.RowCount > dt.Rows.Count ? $" (showing {dt.Rows.Count:N0} of {dt.RowCount:N0})" : "";
            StatusText = $"{(string.IsNullOrEmpty(structName) ? "DataTable" : structName)}: " +
                         $"{dt.Rows.Count:N0} rows{trunc} · key = RowName";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Pivot: walk DataTable failed", ex);
            SetError(ex);
        }
    }

    /// <summary>List the captured struct-array fields of the selected class (C6).</summary>
    private async Task LoadArrayFieldsAsync()
    {
        // Bump at entry so a null selection also supersedes in-flight loads.
        int id = ++_classLoadId;
        SelectedArrayField = null;   // detach selections before clearing bound lists
        SelectedKeyField = null;
        SelectedResult = null;
        ArrayFields.Clear();
        Fields.Clear();
        KeyFieldOptions.Clear();
        Results.Clear();
        OnPropertyChanged(nameof(CanRunPivot));
        if (SelectedSnapshot == null || SelectedClass == null) return;
        long snapId = SelectedSnapshot.Id;
        string cls = SelectedClass.ClassName;
        try
        {
            var list = await Task.Run(() => _store.ListPivotArrayFieldsAsync(snapId, cls));
            if (id != _classLoadId) return;   // a newer class/snapshot superseded us
            foreach (var af in list) ArrayFields.Add(af);
            // Auto-select the first array field (triggers prop load).
            SelectedArrayField = ArrayFields.FirstOrDefault();
            StatusText = $"{ArrayFields.Count} array field(s) in {cls}";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Pivot: list array fields failed", ex);
            SetError(ex);
        }
    }

    /// <summary>List the inner numeric props of the selected struct-array as the
    /// value-field picker. The inner-key value is the implicit group key (C6).</summary>
    private async Task LoadArrayPropsAsync()
    {
        // Bump at entry so a null selection also supersedes in-flight loads.
        int id = ++_fieldLoadId;
        SelectedKeyField = null;
        SelectedResult = null;
        Fields.Clear();
        KeyFieldOptions.Clear();
        Results.Clear();
        OnPropertyChanged(nameof(CanRunPivot));
        if (SelectedSnapshot == null || SelectedClass == null || SelectedArrayField == null) return;
        long snapId = SelectedSnapshot.Id;
        string cls = SelectedClass.ClassName;
        string af = SelectedArrayField.ArrayField;
        try
        {
            var props = await Task.Run(() => _store.ListPivotArrayPropsAsync(snapId, cls, af));
            if (id != _fieldLoadId) return;
            foreach (var f in props)
            {
                Fields.Add(new PivotFieldPick(f,
                    PivotKeyScorer.KeyScore(f), PivotKeyScorer.ValueScore(cls, f.Name)));
                KeyFieldOptions.Add(f.Name);
            }
            // Pre-tick interesting inner props; fall back to the first few.
            int ticked = 0;
            foreach (var p in Fields.OrderByDescending(p => p.ValueScore))
            {
                if (ticked >= 3) break;
                if (p.ValueScore >= PropertyScoringTable.InterestingThreshold) { p.IsValue = true; ticked++; }
            }
            if (ticked == 0)
                foreach (var p in Fields.Take(3)) p.IsValue = true;

            OnPropertyChanged(nameof(CanRunPivot));
            string keyName = string.IsNullOrEmpty(SelectedArrayField.InnerKeyName)
                ? "(elem index)" : SelectedArrayField.InnerKeyName;
            StatusText = $"{af}: {Fields.Count} inner prop(s) · key = {keyName}";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Pivot: list array props failed", ex);
            SetError(ex);
        }
    }

    /// <summary>The composite Field-mode group key: the primary key field
    /// (<see cref="SelectedKeyField"/>) first, then every additionally-ticked
    /// <see cref="PivotFieldPick.IsKey"/> field in grid order, de-duplicated. A single
    /// entry reproduces the legacy single-key grouping exactly.</summary>
    private List<string> CollectKeyFields()
    {
        var keys = new List<string>();
        if (!string.IsNullOrEmpty(SelectedKeyField)) keys.Add(SelectedKeyField);
        foreach (var f in Fields)
            if (f.IsKey && !keys.Contains(f.Name)) keys.Add(f.Name);
        return keys;
    }

    [RelayCommand]
    private async Task RunPivotAsync()
    {
        if (!CanRunPivot) return;
        // Cancel a prior in-flight pivot before starting a new one.
        _pivotCts?.Cancel();
        _pivotCts?.Dispose();
        var cts = _pivotCts = new CancellationTokenSource();
        var ct = cts.Token;

        ClearError();
        IsBusy = true;
        StatusText = "Running pivot…";
        SelectedResult = null;   // detach before clearing the bound results grid
        _allResults.Clear();
        Results.Clear();
        try
        {
            if (IsDataTableSource)
            {
                // Zero-config: RowName is the key, every row its own group.
                var valueFields = Fields.Where(f => f.IsValue).Select(f => f.Name).ToList();
                var res = DataTablePivotEngine.Build(_dataTable!, valueFields);
                SetResults(res.Rows);
                StatusText = $"{res.GroupCount:N0} rows · key = RowName · {_dataTable!.RowStructName}";
                return;
            }

            if (IsArraySource)
            {
                // Group struct-array elements by inner-key value (reorder-immune).
                var aq = new ArrayPivotQuery
                {
                    SnapshotId = SelectedSnapshot!.Id,
                    ClassName  = SelectedClass!.ClassName,
                    ArrayField = SelectedArrayField!.ArrayField,
                    ValueProps = Fields.Where(f => f.IsValue).Select(f => f.Name).ToList(),
                };
                var arrRes = await Task.Run(() => _store.PivotArrayAsync(aq, ct), ct);
                SetResults(arrRes.Rows);
                var arrTrunc = arrRes.Truncated ? $" (capped at {aq.MaxGroups:N0})" : "";
                string keyName = string.IsNullOrEmpty(SelectedArrayField.InnerKeyName)
                    ? "elem index" : SelectedArrayField.InnerKeyName;
                StatusText = $"{arrRes.GroupCount:N0} {keyName} group(s){arrTrunc} from {arrRes.InstanceCount:N0} elements · {aq.ArrayField}";
                return;
            }

            var query = new PivotQuery
            {
                SnapshotId  = SelectedSnapshot!.Id,
                ClassName   = SelectedClass!.ClassName,
                KeyMode     = IsFieldKeyMode ? PivotKeyMode.Field : PivotKeyMode.Identity,
                KeyField    = SelectedKeyField ?? "",
                KeyFields   = IsFieldKeyMode ? CollectKeyFields() : new List<string>(),
                ValueFields = Fields.Where(f => f.IsValue).Select(f => f.Name).ToList(),
            };
            var snapRes = await Task.Run(() => _store.PivotAsync(query, ct), ct);
            SetResults(snapRes.Rows);

            var snapTrunc = snapRes.Truncated ? $" (capped at {query.MaxGroups:N0})" : "";
            var keyDesc = IsFieldKeyMode ? $"key={string.Join(" · ", query.EffectiveKeyFields)}" : "identity";
            StatusText = $"{snapRes.GroupCount:N0} groups{snapTrunc} from {snapRes.InstanceCount:N0} instances · {keyDesc}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Pivot cancelled.";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Pivot: run failed", ex);
            SetError(ex);
            StatusText = "Pivot failed.";
        }
        finally
        {
            if (ReferenceEquals(_pivotCts, cts))
            {
                IsBusy = false;
                _pivotCts.Dispose();
                _pivotCts = null;
            }
        }
    }

    /// <summary>Change-driven discovery: rank the (class, prop) targets that moved
    /// between the two chosen snapshots. The result feeds the pivot via "Use →".</summary>
    [RelayCommand]
    private async Task RunDiscoverAsync()
    {
        if (!CanDiscover) return;
        _discoverCts?.Cancel();
        _discoverCts?.Dispose();
        var cts = _discoverCts = new CancellationTokenSource();
        var ct = cts.Token;

        ClearError();
        IsDiscovering = true;
        DiscoverStatus = "Scanning for changes…";
        SelectedDiscoverResult = null;   // detach before clearing the bound grid
        DiscoverResults.Clear();
        try
        {
            // Order the ticked snapshots chronologically (lower id = older) so the
            // value sequence + direction (↑/↓) read oldest → newest. The newest is the
            // pivot target (its values + addresses are current).
            var selected = DiscoverPicks.Where(p => p.IsSelected).OrderBy(p => p.Id).ToList();
            _discoverNewest = selected[^1].Meta;

            var query = new DiscoveryQuery
            {
                JoinMode        = SpcJoinMode.Strict,
                ExcludedClasses = _excludedClasses.Count > 0 ? _excludedClasses : null,
                MaxResults      = 200,
            };
            foreach (var p in selected) query.SnapshotIds.Add(p.Id);

            var res = await Task.Run(() => _store.DiscoverChangesAsync(query, ct), ct);
            foreach (var row in res.Rows) DiscoverResults.Add(row);

            var trunc = res.Truncated ? $" (top {res.Rows.Count:N0} of {res.ChangedGroups:N0})" : "";
            DiscoverStatus = res.Rows.Count == 0
                ? "No fields changed across the selected snapshots."
                : $"{res.ChangedGroups:N0} changed target(s){trunc} — pick one, then Use →.";
        }
        catch (OperationCanceledException)
        {
            DiscoverStatus = "Discovery cancelled.";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Pivot: discovery failed", ex);
            SetError(ex);
            DiscoverStatus = "Discovery failed.";
        }
        finally
        {
            if (ReferenceEquals(_discoverCts, cts))
            {
                IsDiscovering = false;
                _discoverCts.Dispose();
                _discoverCts = null;
            }
        }
    }

    /// <summary>"Use →": take a discovered candidate and pivot it — switch to the
    /// newer snapshot of the discovery pair, select the candidate's class, tick its
    /// property, and run the pivot so the grouped result appears immediately. This
    /// is the payoff: the user never had to know the class or key up front.</summary>
    [RelayCommand]
    public async Task UseDiscoverCandidateAsync(DiscoveryCandidate? cand)
    {
        if (cand == null) return;
        try
        {
            ClearError();
            SelectedSource = "Snapshot";
            var target = _discoverNewest ?? SelectedSnapshot;
            if (target != null && !ReferenceEquals(SelectedSnapshot, target))
                SelectedSnapshot = target;    // triggers the class load
            var pending = PendingLoad;
            if (pending != null) { try { await pending; } catch { /* surfaced via SetError */ } }

            if (SelectedSnapshot == null)
            {
                StatusText = "No snapshot selected — capture one first.";
                return;
            }
            if (await SelectClassAndTickPropAsync(cand.ClassName, cand.PropName))
            {
                // The discovered property IS the thing the user wants to watch, so
                // always project it — and group by Identity so its value shows per
                // instance. (Don't let the key auto-suggester consume the discovered
                // property as the group key, which would hide its value in Identity
                // mode / collapse it in Field mode.)
                SelectedKeyMode = "Identity (object path)";
                var pick = Fields.FirstOrDefault(f => f.Name == cand.PropName);
                if (pick != null) pick.IsValue = true;
                await RunPivotAsync();        // show the grouped pivot of the target now
            }
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, $"Pivot: use discovery candidate {cand.ClassName}.{cand.PropName} failed", ex);
            SetError(ex);
        }
    }

    /// <summary>Cancel an in-flight pivot run, discovery, AND any class/field list
    /// load — called when the user navigates away from the Class Pivot tab so a
    /// heavy GROUP BY / scan doesn't keep burning CPU.</summary>
    public void CancelPendingWork()
    {
        _pivotCts?.Cancel();
        _loadCts?.Cancel();
        _discoverCts?.Cancel();
    }

    // Full (unfiltered) result set; Results is the filtered view bound to the grid.
    private readonly List<PivotResultRow> _allResults = new();

    // Store the pivot output + apply the current result filter into the bound grid.
    private void SetResults(IEnumerable<PivotResultRow> rows)
    {
        _allResults.Clear();
        _allResults.AddRange(rows);
        ApplyResultFilter();
    }

    // Filter the results grid by space-separated AND terms over key + values (each term
    // must hit KeyValue or ValuesDisplay). Empty filter shows all. Detaches the selection
    // before the rebuild (selection-model safe).
    private void ApplyResultFilter()
    {
        var terms = ObjectTreeFilter.SplitTerms(ResultFilter);
        IEnumerable<PivotResultRow> view = _allResults;
        if (terms.Length > 0)
            view = _allResults.Where(r =>
                ObjectTreeFilter.MatchesAllTerms(terms, r.KeyValue, r.ValuesDisplay));
        UiCollection.Reset(Results, view, () => SelectedResult = null);
    }

    /// <summary>Open the selected group's representative object in Live Walker.</summary>
    [RelayCommand]
    private void OpenInLiveWalker(PivotResultRow? row)
    {
        if (row == null || string.IsNullOrEmpty(row.ObjAddr)) return;
        NavigateToInstance?.Invoke(row.ObjAddr);
    }

    /// <summary>Locate the selected group's representative object via the GWorld graph.</summary>
    [RelayCommand]
    private void LocateResultInGWorld(PivotResultRow? row)
    {
        if (row == null || string.IsNullOrEmpty(row.ObjAddr)) return;
        LocateInGWorld?.Invoke(row.ObjAddr);
    }

    /// <summary>Engine-rooted locate — not gated on GWorld (the DLL reports no_engine).</summary>
    [RelayCommand]
    private void LocateResultInGameEngine(PivotResultRow? row)
    {
        if (row == null || string.IsNullOrEmpty(row.ObjAddr)) return;
        LocateInGameEngine?.Invoke(row.ObjAddr);
    }

    /// <summary>Copy the representative instance's base address to the clipboard.</summary>
    [RelayCommand]
    private async Task CopyAddressAsync(PivotResultRow? row)
    {
        if (row == null || _platform == null || string.IsNullOrEmpty(row.ObjAddr)) return;
        try
        {
            var hex = row.ObjAddr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? row.ObjAddr.Substring(2) : row.ObjAddr;
            await _platform.CopyToClipboardAsync(hex);
            StatusText = $"Copied {hex}  ({row.KeyValue})";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Pivot: copy address failed", ex);
        }
    }
}
