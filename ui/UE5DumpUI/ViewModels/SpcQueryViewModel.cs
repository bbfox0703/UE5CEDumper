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

/// <summary>
/// One selectable snapshot row in the SPC picker: a snapshot plus whether it is
/// part of the query sequence and which directional predicate applies to it.
/// </summary>
public partial class SpcSnapshotPick : ObservableObject
{
    public SnapshotMeta Meta { get; }

    public SpcSnapshotPick(SnapshotMeta meta, IReadOnlyList<string> predicateOptions)
    {
        Meta = meta;
        PredicateOptions = predicateOptions;
    }

    [ObservableProperty] private bool   _isSelected;
    /// <summary>True for the oldest CHECKED snapshot — the baseline. Its predicate
    /// is forced to "Any" and its picker is disabled (there's nothing before it to
    /// compare against). Recomputed by the VM whenever the selection changes.</summary>
    [ObservableProperty] private bool   _isBaseline;
    /// <summary>How this snapshot's value compares to the previous selected one
    /// (display string from <see cref="PredicateOptions"/>). Ignored for the
    /// oldest selected snapshot — that one is always the baseline.</summary>
    [ObservableProperty] private string _selectedPredicate = "Any";

    /// <summary>The predicate picker is editable only for a selected, non-baseline
    /// snapshot.</summary>
    public bool PredicateEnabled => IsSelected && !IsBaseline;

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(PredicateEnabled));
        OnPropertyChanged(nameof(AbsEnabled));
    }
    partial void OnIsBaselineChanged(bool value)
    {
        OnPropertyChanged(nameof(PredicateEnabled));
        if (!value) return;
        SelectedPredicate = "Any";   // the baseline is always "Any" (single mode)
        // Group mode: scrub EVERY slot's directional predicate for this snapshot too, so a
        // stale non-Any value can't silently re-surface if the row later stops being the
        // baseline (mirrors the single-mode reset; query-time already forces index 0 = Any).
        foreach (var cell in GroupCells) cell.Predicate = "Any";
        OnPropertyChanged(nameof(GroupActivePredicate));
    }

    public IReadOnlyList<string> PredicateOptions { get; }

    // --- Optional absolute (value-window) predicate, applied IN ADDITION to the
    //     directional one, before the cap. Cuts directional-but-irrelevant noise. ---
    public IReadOnlyList<string> AbsKindOptions { get; } =
        new[] { "(any value)", "Exact", "Between", "≥", "≤" };
    [ObservableProperty] private string _absKind = "(any value)";
    [ObservableProperty] private string _absLow  = "";
    [ObservableProperty] private string _absHigh = "";

    /// <summary>The panel's rounding mode, pushed in by the VM so each row renders its
    /// own live absolute-window preview. Display-only — the query always uses the
    /// panel-level mode.</summary>
    [ObservableProperty] private FloatRoundMode _roundMode = FloatRoundMode.Round;

    /// <summary>Value boxes are editable only for a selected snapshot.</summary>
    public bool AbsEnabled => IsSelected;
    public bool ShowAbsLow  => AbsKind is "Exact" or "Between" or "≥";
    public bool ShowAbsHigh => AbsKind is "Between" or "≤";

    /// <summary>Live "what will this actually match" preview for the single-query
    /// absolute window (e.g. <c>→ int 12~13 · float 11.5~13.2</c> under Round). SPC
    /// scans the whole captured corpus → both interpretations. Empty for "(any value)"
    /// or unparseable bounds (the cell hides the label).</summary>
    public string AbsPreview =>
        RoundModePreview.SpcAbsolute(AbsKind, AbsLow, AbsHigh, RoundMode, RoundModePreview.Scope.Both);

    partial void OnAbsKindChanged(string value)
    {
        OnPropertyChanged(nameof(ShowAbsLow));
        OnPropertyChanged(nameof(ShowAbsHigh));
        OnPropertyChanged(nameof(AbsPreview));
    }

    partial void OnAbsLowChanged(string value)  => OnPropertyChanged(nameof(AbsPreview));
    partial void OnAbsHighChanged(string value) => OnPropertyChanged(nameof(AbsPreview));

    partial void OnRoundModeChanged(FloatRoundMode value)
    {
        OnPropertyChanged(nameof(AbsPreview));
        OnPropertyChanged(nameof(GroupAbsPreview));
    }

    /// <summary>Compile this row's UI choice into an absolute predicate.</summary>
    public SpcAbsolutePredicate ToAbsolutePredicate()
    {
        double Lo() => double.TryParse(AbsLow.Trim(),  System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        double Hi() => double.TryParse(AbsHigh.Trim(), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        return AbsKind switch
        {
            "Exact"        => new SpcAbsolutePredicate { Kind = SpcAbsoluteKind.Exact,   Low = Lo() },
            "Between"      => new SpcAbsolutePredicate { Kind = SpcAbsoluteKind.Between, Low = Lo(), High = Hi() },
            "≥"       => new SpcAbsolutePredicate { Kind = SpcAbsoluteKind.AtLeast, Low = Lo() },
            "≤"       => new SpcAbsolutePredicate { Kind = SpcAbsoluteKind.AtMost,  High = Hi() },
            _              => new SpcAbsolutePredicate { Kind = SpcAbsoluteKind.None },
        };
    }

    // ===================== SPC GROUP (multi-value) matrix =====================
    // In Group mode the picker grid edits ONE value-slot's column at a time (the
    // ActiveGroupSlot, chosen via the slot selector). Each snapshot row therefore
    // holds a cell per possible slot (max 4); the GroupActive* facades read/write the
    // active slot's cell so the same grid columns serve every slot. The full N×M
    // matrix is the union of every selected pick's cells over the chosen slots.

    /// <summary>One cell per possible value-slot (max 4) for THIS snapshot.</summary>
    public SpcGroupCellVm[] GroupCells { get; } = { new(), new(), new(), new() };

    private int _activeGroupSlot;

    /// <summary>Point the GroupActive* facades at <paramref name="slot"/> (0-based) and
    /// notify, so the picker grid shows that slot's column for this snapshot.</summary>
    public void SetActiveGroupSlot(int slot)
    {
        _activeGroupSlot = slot < 0 ? 0 : slot > 3 ? 3 : slot;
        OnPropertyChanged(nameof(GroupActivePredicate));
        OnPropertyChanged(nameof(GroupActiveAbsKind));
        OnPropertyChanged(nameof(GroupActiveAbsLow));
        OnPropertyChanged(nameof(GroupActiveAbsHigh));
        OnPropertyChanged(nameof(GroupShowAbsLow));
        OnPropertyChanged(nameof(GroupShowAbsHigh));
        OnPropertyChanged(nameof(GroupAbsPreview));
    }

    public string GroupActivePredicate
    {
        get => GroupCells[_activeGroupSlot].Predicate;
        set { if (GroupCells[_activeGroupSlot].Predicate != value) { GroupCells[_activeGroupSlot].Predicate = value; OnPropertyChanged(); } }
    }
    public string GroupActiveAbsKind
    {
        get => GroupCells[_activeGroupSlot].AbsKind;
        set
        {
            if (GroupCells[_activeGroupSlot].AbsKind == value) return;
            GroupCells[_activeGroupSlot].AbsKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GroupShowAbsLow));
            OnPropertyChanged(nameof(GroupShowAbsHigh));
            OnPropertyChanged(nameof(GroupAbsPreview));
        }
    }
    public string GroupActiveAbsLow
    {
        get => GroupCells[_activeGroupSlot].AbsLow;
        set { if (GroupCells[_activeGroupSlot].AbsLow != value) { GroupCells[_activeGroupSlot].AbsLow = value; OnPropertyChanged(); OnPropertyChanged(nameof(GroupAbsPreview)); } }
    }
    public string GroupActiveAbsHigh
    {
        get => GroupCells[_activeGroupSlot].AbsHigh;
        set { if (GroupCells[_activeGroupSlot].AbsHigh != value) { GroupCells[_activeGroupSlot].AbsHigh = value; OnPropertyChanged(); OnPropertyChanged(nameof(GroupAbsPreview)); } }
    }
    public bool GroupShowAbsLow  => GroupActiveAbsKind is "Exact" or "Between" or "≥";
    public bool GroupShowAbsHigh => GroupActiveAbsKind is "Between" or "≤";

    /// <summary>Live preview for the ACTIVE slot's absolute window in the group matrix
    /// (same format as <see cref="AbsPreview"/>). Recomputed when the active cell's
    /// kind/bounds change, the active slot switches, or the rounding mode changes.</summary>
    public string GroupAbsPreview =>
        RoundModePreview.SpcAbsolute(GroupActiveAbsKind, GroupActiveAbsLow, GroupActiveAbsHigh,
                                     RoundMode, RoundModePreview.Scope.Both);

    /// <summary>This snapshot's directional predicate for slot <paramref name="slot"/>.</summary>
    public string GroupPredicateOf(int slot) => GroupCells[slot].Predicate;

    /// <summary>Compile this snapshot's absolute window for slot <paramref name="slot"/>.</summary>
    public SpcAbsolutePredicate ToGroupAbsolutePredicate(int slot)
    {
        var cell = GroupCells[slot];
        double Lo() => double.TryParse(cell.AbsLow.Trim(),  System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        double Hi() => double.TryParse(cell.AbsHigh.Trim(), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        return cell.AbsKind switch
        {
            "Exact"   => new SpcAbsolutePredicate { Kind = SpcAbsoluteKind.Exact,   Low = Lo() },
            "Between" => new SpcAbsolutePredicate { Kind = SpcAbsoluteKind.Between, Low = Lo(), High = Hi() },
            "≥"  => new SpcAbsolutePredicate { Kind = SpcAbsoluteKind.AtLeast, Low = Lo() },
            "≤"  => new SpcAbsolutePredicate { Kind = SpcAbsoluteKind.AtMost,  High = Hi() },
            _         => new SpcAbsolutePredicate { Kind = SpcAbsoluteKind.None },
        };
    }

    /// <summary>Carry the group matrix across a picker rebuild (a Snapshot-tab capture
    /// refreshes the list — don't wipe a half-built matrix).</summary>
    public void CopyGroupCellsFrom(SpcSnapshotPick other)
    {
        for (int i = 0; i < GroupCells.Length && i < other.GroupCells.Length; i++)
        {
            GroupCells[i].Predicate = other.GroupCells[i].Predicate;
            GroupCells[i].AbsKind   = other.GroupCells[i].AbsKind;
            GroupCells[i].AbsLow    = other.GroupCells[i].AbsLow;
            GroupCells[i].AbsHigh   = other.GroupCells[i].AbsHigh;
        }
    }

    public long   Id         => Meta.Id;
    public string Label      => Meta.Label;
    public string CapturedAt => Meta.CapturedAt;
    /// <summary>Short tail of the game-session id so the user can see at a glance
    /// which snapshots come from different game launches (the cross-session case).</summary>
    public string SessionShort
    {
        get
        {
            var s = Meta.GameSessionId;
            if (string.IsNullOrEmpty(s)) return "";
            int dash = s.LastIndexOf('-');
            return dash >= 0 && dash + 1 < s.Length ? s[(dash + 1)..] : s;
        }
    }
}

/// <summary>
/// ViewModel for the experimental SPC Query tab. Pure C# over the SQLite
/// snapshot corpus — no DLL/pipe. The user selects two or more snapshots (which
/// may span game sessions), assigns a directional predicate to each, picks a
/// join mode, and runs the chain. Generalises CE's "Unknown Initial Value +
/// Increased/Decreased" to N persisted, cross-session snapshots. See
/// docs/experimental-snapshot-spc-pivot.md §"Phase B".
/// </summary>
public partial class SpcQueryViewModel : ViewModelBase
{
    private readonly ISnapshotStore _store;
    private readonly ILoggingService _log;
    private readonly IPlatformService? _platform;
    private EngineState? _engineState;
    private CancellationTokenSource? _queryCts;   // heavy in-memory SPC op

    [ObservableProperty] private string _selectedJoinMode = "Strict";
    [ObservableProperty] private string _classFilter = "";
    [ObservableProperty] private string _propFilter = "";
    [ObservableProperty] private bool   _isQuerying;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _warningText = "";
    [ObservableProperty] private SpcResultRow? _selectedResult;

    /// <summary>Per-panel rounding mode (Round/Trunc/Ceil): how a fractional
    /// float/double absolute target is reduced to the displayed integer before
    /// compare. Shared by single + group SPC queries. Persisted; default
    /// <see cref="FloatRoundMode.Round"/>.</summary>
    [ObservableProperty] private FloatRoundMode _selectedRoundingMode = FloatRoundMode.Round;

    /// <summary>Picker options for <see cref="SelectedRoundingMode"/>.</summary>
    public IReadOnlyList<FloatRoundMode> RoundingModeOptions { get; } =
        new[] { FloatRoundMode.Round, FloatRoundMode.Trunc, FloatRoundMode.Ceil };

    /// <summary>One-line explanation of the active rounding mode (UI hint).</summary>
    public string RoundingModeHint => SelectedRoundingMode switch
    {
        FloatRoundMode.Trunc => "Integer fields truncate a decimal target (10.9 → 10; Between 10.9–11.1 → 10–11). Float/double values are used as-is.",
        FloatRoundMode.Ceil  => "Integer fields round a decimal target up (10.9 → 11; Between 10.9–11.1 → 11–12). Float/double values are used as-is.",
        _                    => "Integer fields round a decimal target to nearest (10.9 → 11; Between 10.9–11.1 → 11–11). Float/double values are used as-is.",
    };

    partial void OnSelectedRoundingModeChanged(FloatRoundMode value)
    {
        OnPropertyChanged(nameof(RoundingModeHint));
        // Each snapshot row renders its own live absolute-window preview from this mode.
        foreach (var p in SnapshotPicks) p.RoundMode = value;
    }

    /// <summary>Directional predicate options (display strings). v1 is
    /// type-agnostic: directions only, no type/value entry.</summary>
    public IReadOnlyList<string> PredicateOptions { get; } =
        new[] { "Any", "Unchanged", "Changed", "Increased", "Decreased" };

    public IReadOnlyList<string> JoinModeOptions { get; } =
        new[] { "Strict", "Loose", "In-session" };

    /// <summary>Available snapshots (newest first), each pickable into the chain.</summary>
    public ObservableCollection<SpcSnapshotPick> SnapshotPicks { get; } = new();

    public ObservableCollection<SpcResultRow> Results { get; } = new();

    // --- N1: per-game class denylist (noise picker) ---

    /// <summary>Top-N classes ranked by hit count over the last result set, each
    /// row toggleable. Apply pushes the selection into the per-game denylist and
    /// re-runs the query (so the user sees the cleaned result immediately).</summary>
    public ObservableCollection<NoiseRowVm> NoiseRows { get; } = new();

    /// <summary>The currently-active per-game denylist (loaded from the store).
    /// Empty when no game is wired. Display-only — mutations happen via
    /// ApplyNoisePicksAsync.</summary>
    public ObservableCollection<string> ActiveDenylist { get; } = new();
    [ObservableProperty] private bool _noisePanelOpen;

    private HashSet<string> _excludedClasses = new(StringComparer.Ordinal);

    // --- Client-side result filtering (over the last query's full result set) ---
    [ObservableProperty] private string _resultClassFilter  = "";
    [ObservableProperty] private string _resultFieldFilter  = "";
    [ObservableProperty] private string _resultObjectFilter = "";
    [ObservableProperty] private string _resultGlobalFilter = "";
    // Value-sequence range: bounds on the first and last value of the sequence,
    // applied on demand (Apply button) and cleared by Reset.
    [ObservableProperty] private string _seqFirstMin = "";
    [ObservableProperty] private string _seqFirstMax = "";
    [ObservableProperty] private string _seqLastMin = "";
    [ObservableProperty] private string _seqLastMax = "";

    /// <summary>Per-session remembered global-filter keywords (LRU), surfaced as the
    /// global result filter box's AutoCompleteBox suggestions — see
    /// <see cref="KeywordSearchMemory"/>. Only the class/field/object pickers stay
    /// data-derived; the free-text global box gets keyword memory.</summary>
    private readonly KeywordSearchMemory _resultGlobalMemory;
    public ObservableCollection<string> ResultGlobalHistory => _resultGlobalMemory.History;

    partial void OnResultClassFilterChanged(string value)  => ApplyResultFilter();
    partial void OnResultFieldFilterChanged(string value)  => ApplyResultFilter();
    partial void OnResultObjectFilterChanged(string value) => ApplyResultFilter();
    partial void OnResultGlobalFilterChanged(string value)
    {
        ApplyResultFilter();
        _resultGlobalMemory.Schedule(value);
    }

    /// <summary>Distinct Class/Field/Object candidates from the last result set,
    /// feeding the AutoCompleteBox pickers (partial match).</summary>
    public ObservableCollection<string> ResultClassOptions  { get; } = new();
    public ObservableCollection<string> ResultFieldOptions  { get; } = new();
    public ObservableCollection<string> ResultObjectOptions { get; } = new();

    private readonly List<SpcResultRow> _allResults = new();
    private string _resultSummary = "";

    /// <summary>Raised to open a result row's object in the Live Walker tab.</summary>
    public event Action<string>? NavigateToInstance;

    /// <summary>Raised to locate a result row's owning object within the GWorld
    /// object graph. Payload = (owning object address, changed field byte offset,
    /// field name).</summary>
    public event Action<string, int, string>? LocateInGWorld;

    /// <summary>Engine-rooted counterpart of <see cref="LocateInGWorld"/> (path search
    /// rooted at the live UGameEngine). Same payload.</summary>
    public event Action<string, int, string>? LocateInGameEngine;

    /// <summary>True when GWorld is available — gates the per-row "Locate in GWorld" button.</summary>

    // Live game session id (PeHash-CreationTime; unique per launch even on
    // no-ASLR games). Result rows carry the NEWEST selected snapshot's live
    // ObjAddr (the store hands the last id's address), so the per-row
    // Live/Addr/GWorld actions are only valid when that newest snapshot belongs
    // to the current live session. See EngineState.GameSessionId.
    private string _currentSessionId = "";

    /// <summary>GameSessionId of the newest selected snapshot (by Id == capture
    /// time) — the one whose live ObjAddr the result rows carry.</summary>
    private string NewestSelectedSessionId =>
        SnapshotPicks.Where(p => p.IsSelected).OrderBy(p => p.Id).LastOrDefault()?.Meta.GameSessionId ?? "";

    /// <summary>True only when the newest selected snapshot belongs to the CURRENT
    /// live session, so the result rows' ObjAddr is still valid in the running game.
    /// Gates the per-row Live / Addr actions (a cross-session address is stale).</summary>
    public bool CanUseResultRowActions =>
        !string.IsNullOrEmpty(_currentSessionId) && NewestSelectedSessionId == _currentSessionId;
    /// <summary>As above, additionally requiring GWorld for the 🌍 button.</summary>
    // NOT gated on the client IsGWorldAvailable flag (audit #5 AE10).
    public bool CanLocateResultRowInGWorld => CanUseResultRowActions;


    private void RaiseResultRowActionGates()
    {
        OnPropertyChanged(nameof(CanUseResultRowActions));
        OnPropertyChanged(nameof(CanLocateResultRowInGWorld));
    }

    public int SelectedCount => SnapshotPicks.Count(p => p.IsSelected);

    /// <summary>At least two snapshots picked and not mid-query.</summary>
    public bool CanRunQuery => SelectedCount >= 2 && !IsQuerying;

    public SpcQueryViewModel(ISnapshotStore store, ILoggingService log,
                             IPlatformService? platform = null)
    {
        _store = store;
        _log = log;
        _platform = platform;
        _resultGlobalMemory = new KeywordSearchMemory(() => (ResultGlobalFilter, Results.Count > 0));
        // Don't list yet — the per-game DB isn't known until a game connects.
    }

    partial void OnIsQueryingChanged(bool value) => OnPropertyChanged(nameof(CanRunQuery));

    public void SetEngineState(EngineState state)
    {
        _engineState = state;
        _currentSessionId = state.GameSessionId;   // PeHash-CreationTime; matches capture-time GameSessionId
        RaiseResultRowActionGates();
        _store.SetActiveGame(state.PeHash);
        LoadDenylistFromStore();
        _ = RefreshAsync();
    }

    /// <summary>Drop the live session id so per-row Live/Addr actions auto-disable, and
    /// cancel any in-flight query, on disconnect (audit X5). The disk-backed corpus +
    /// results are PRESERVED — a reconnect re-scopes via SetEngineState.</summary>
    public void ClearOnDisconnect()
    {
        CancelPendingWork();
        _currentSessionId = "";
        RaiseResultRowActionGates();
    }

    /// <summary>Reload the per-game denylist after SetActiveGame switched the
    /// active DB. Resets the local cache + the bound display collection.</summary>
    private void LoadDenylistFromStore()
    {
        _excludedClasses = _store.GetClassDenylist(DenylistScope.Spc);
        ActiveDenylist.Clear();
        foreach (var c in _excludedClasses.OrderBy(s => s, StringComparer.Ordinal))
            ActiveDenylist.Add(c);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        try
        {
            // Off the UI thread (SQLite "*Async" is synchronous) — this also posts
            // the rebuild as a fresh dispatcher work item, avoiding the "cannot
            // change ObservableCollection during a CollectionChanged event" crash.
            var list = await Task.Run(() => _store.ListSnapshotsAsync());
            // Exclude UNUSABLE snapshots (captures that spanned a GObjects drift): SPC
            // joins across snapshots, so an inconsistent one silently poisons results.
            // Display oldest-first: the baseline (oldest selected) then sits at the
            // top and the predicate chain reads top-to-bottom in time order.
            var ordered = list.Where(m => m.IsUsable).OrderBy(m => m.Id).ToList();
            // Preserve current selections/predicates across a refresh so a capture
            // on the Snapshot tab doesn't wipe a half-built query — including the SPC
            // group matrix (every pick's per-slot cells).
            var prev = SnapshotPicks.ToDictionary(p => p.Id, p => (p.IsSelected, p.SelectedPredicate));
            var prevPick = SnapshotPicks.ToDictionary(p => p.Id);
            foreach (var p in SnapshotPicks) p.PropertyChanged -= OnPickChanged;

            // Build the fresh picks detached, then swap in one shot.
            var fresh = new List<SpcSnapshotPick>(ordered.Count);
            foreach (var m in ordered)
            {
                var pick = new SpcSnapshotPick(m, PredicateOptions) { RoundMode = SelectedRoundingMode };
                if (prev.TryGetValue(m.Id, out var s))
                {
                    pick.IsSelected = s.IsSelected;
                    pick.SelectedPredicate = s.SelectedPredicate;
                }
                if (prevPick.TryGetValue(m.Id, out var old)) pick.CopyGroupCellsFrom(old);
                pick.SetActiveGroupSlot(ActiveGroupSlot);   // point the group facades at the current slot
                pick.PropertyChanged += OnPickChanged;
                fresh.Add(pick);
            }
            // Convenience: first visit pre-selects the two newest (now at the tail)
            // so a directional query is one predicate edit away.
            if (prev.Count == 0 && fresh.Count >= 2)
            {
                fresh[^1].IsSelected = true;
                fresh[^2].IsSelected = true;
            }
            UiCollection.Reset(SnapshotPicks, fresh, () => { /* no SelectedItem binding */ });
            RecomputeBaselines();
            AutoSelectJoinMode();
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(CanRunQuery));
            OnPropertyChanged(nameof(CanRunGroupQuery));
            UpdateWarning();
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "SPC: list failed", ex);
            SetError(ex);
        }
    }

    private void OnPickChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpcSnapshotPick.IsSelected))
        {
            RecomputeBaselines();
            AutoSelectJoinMode();
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(CanRunQuery));
            OnPropertyChanged(nameof(CanRunGroupQuery));
            RaiseResultRowActionGates();   // newest-selected snapshot may have changed
            UpdateWarning();
        }
    }

    // --- Auto join-mode selection ---
    // Same-session snapshots are best joined by gobjects_index (In-session): it
    // tracks each physical object exactly, including transient objects whose
    // normalised path collides (e.g. every //Engine/Transient/Item folds to the
    // same Strict key, so Strict collapses 4 distinct items into 1 mismatched
    // candidate and the predicate fails — the "materials don't show up" bug).
    // Cross-session snapshots have no stable index, so they fall back to Strict.
    // The user can still override the combo manually.
    private bool _joinModeUserOverride;
    private bool _settingJoinModeProgrammatically;

    partial void OnSelectedJoinModeChanged(string value)
    {
        if (!_settingJoinModeProgrammatically) _joinModeUserOverride = true;
    }

    private void AutoSelectJoinMode()
    {
        if (_joinModeUserOverride) return;
        var ticked = SnapshotPicks.Where(p => p.IsSelected).ToList();
        if (ticked.Count < 2) return;
        bool sameSession = ticked.Select(p => p.Meta.GameSessionId).Distinct().Count() == 1;
        var target = sameSession ? "In-session" : "Strict";
        if (SelectedJoinMode != target)
        {
            _settingJoinModeProgrammatically = true;
            SelectedJoinMode = target;
            _settingJoinModeProgrammatically = false;
        }
    }

    /// <summary>Mark the oldest CHECKED snapshot as the baseline (predicate forced
    /// to "Any", picker disabled); everything else is non-baseline.</summary>
    private void RecomputeBaselines()
    {
        var baseline = SnapshotPicks.Where(p => p.IsSelected).OrderBy(p => p.Id).FirstOrDefault();
        foreach (var p in SnapshotPicks) p.IsBaseline = ReferenceEquals(p, baseline);
    }

    /// <summary>A 2-snapshot directional query matches very broadly (a single
    /// up/down step is true for a huge fraction of fields → often the row cap). Warn
    /// the user to add a third snapshot to make the sequence discriminating.</summary>
    private void UpdateWarning()
    {
        int sel = SelectedCount;
        WarningText = sel == 2
            ? "Only 2 snapshots selected: a single direction matches very broadly and often hits the result cap. Add a 3rd snapshot to make the sequence discriminating."
            : "";
    }

    /// <summary>Cancel an in-flight SPC query — called when the user navigates
    /// away from the SPC tab. The in-memory intersection over ~1.8M rows is the
    /// heaviest experimental op; left running while another tab loads, it pegs a
    /// core + allocates GBs (the tab-switch hang the user reported).</summary>
    public void CancelPendingWork() { _queryCts?.Cancel(); _groupCts?.Cancel(); }

    [RelayCommand]
    private async Task RunQueryAsync()
    {
        var selected = SnapshotPicks.Where(p => p.IsSelected)
                                    .OrderBy(p => p.Id)   // oldest -> newest
                                    .ToList();
        if (selected.Count < 2)
        {
            StatusText = "Select at least two snapshots.";
            return;
        }

        // Cancel a prior in-flight query before starting a new one.
        _queryCts?.Cancel();
        _queryCts?.Dispose();
        var cts = _queryCts = new CancellationTokenSource();
        var ct = cts.Token;

        ClearError();
        IsQuerying = true;
        StatusText = "Running SPC query… (intersecting fields across snapshots)";
        SelectedResult = null;   // detach before clearing the bound results grid
        Results.Clear();
        try
        {
            var query = new SpcQuery
            {
                JoinMode      = ParseJoinMode(SelectedJoinMode),
                ClassContains = ClassFilter.Trim(),
                PropContains  = PropFilter.Trim(),
                RoundMode     = SelectedRoundingMode,
                // N1: hand the current denylist to the store so denylisted classes
                // never enter the candidate dict (cuts memory + match cost both).
                ExcludedClasses = _excludedClasses.Count > 0 ? _excludedClasses : null,
            };
            for (int i = 0; i < selected.Count; i++)
            {
                query.SnapshotIds.Add(selected[i].Id);
                // Index 0 is the baseline; its predicate is forced to Any.
                query.Predicates.Add(i == 0 ? SpcPredicateKind.Any
                                            : ParsePredicate(selected[i].SelectedPredicate));
                // Optional per-snapshot absolute value window (applied before the cap).
                query.AbsolutePredicates.Add(selected[i].ToAbsolutePredicate());
            }

            // Off the UI thread — the N-way self-join over ~1.8M rows would
            // otherwise freeze the window for seconds.
            var res = await Task.Run(() => _store.SpcQueryAsync(query, ct), ct);
            _allResults.Clear();
            _allResults.AddRange(res.Rows);
            RebuildResultOptions();
            RebuildNoiseRows(res.TopContributors);

            var trunc = res.Truncated ? $" (capped at {query.MaxRows:N0})" : "";
            var spanSessions = selected.Select(p => p.Meta.GameSessionId).Distinct().Count();
            var sess = spanSessions > 1 ? $", {spanSessions} sessions" : "";
            _resultSummary = res.Rows.Count == 0
                ? $"No fields match the chain across {selected.Count} snapshots{sess}."
                : $"{res.Rows.Count:N0} match{trunc} across {selected.Count} snapshots{sess} ({SelectedJoinMode}).";
            ApplyResultFilter();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Query cancelled.";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "SPC: query failed", ex);
            SetError(ex);
            StatusText = "Query failed.";
        }
        finally
        {
            // Clear busy only if THIS run is still current (a newer run may have
            // superseded us after cancellation).
            if (ReferenceEquals(_queryCts, cts))
            {
                IsQuerying = false;
                _queryCts.Dispose();
                _queryCts = null;
            }
        }
    }

    private void ApplyResultFilter()
    {
        if (_allResults.Count == 0 && Results.Count == 0) { StatusText = _resultSummary; return; }
        // Space-separated terms are ANDed per box (each term must hit the box's field;
        // the global box ORs across all displayed columns) — the shared Object Tree
        // filter semantics.
        var clsTerms  = ObjectTreeFilter.SplitTerms(ResultClassFilter);
        var fldTerms  = ObjectTreeFilter.SplitTerms(ResultFieldFilter);
        var objTerms  = ObjectTreeFilter.SplitTerms(ResultObjectFilter);
        var globTerms = ObjectTreeFilter.SplitTerms(ResultGlobalFilter);
        double? fMin = ParseBound(SeqFirstMin), fMax = ParseBound(SeqFirstMax);
        double? lMin = ParseBound(SeqLastMin),  lMax = ParseBound(SeqLastMax);

        SelectedResult = null;   // detach before clearing the bound results grid
        Results.Clear();
        foreach (var r in _allResults)
        {
            if (clsTerms.Length  > 0 && !ObjectTreeFilter.MatchesAllTerms(clsTerms, r.ClassName)) continue;
            if (fldTerms.Length  > 0 && !ObjectTreeFilter.MatchesAllTerms(fldTerms, r.PropName)) continue;
            if (objTerms.Length  > 0 && !ObjectTreeFilter.MatchesAllTerms(objTerms, r.NormPath)) continue;
            if (globTerms.Length > 0 && !ObjectTreeFilter.MatchesAllTerms(
                    globTerms, r.ClassName, r.PropName, r.NormPath, r.DeclaredType, r.SequenceDisplay)) continue;
            if (!WithinRange(SeqFirst(r), fMin, fMax)) continue;
            if (!WithinRange(SeqLast(r),  lMin, lMax)) continue;
            Results.Add(r);
        }
        StatusText = _resultSummary +
            (Results.Count != _allResults.Count ? $"  ·  showing {Results.Count:N0}" : "");
    }

    private static string SeqFirst(SpcResultRow r) => r.Values.Count > 0 ? r.Values[0] : "";
    private static string SeqLast(SpcResultRow r)  => r.Values.Count > 0 ? r.Values[^1] : "";

    private static double? ParseBound(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static bool WithinRange(string rendered, double? min, double? max)
    {
        if (min is null && max is null) return true;
        if (!double.TryParse(rendered, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return false;
        if (min is not null && v < min.Value) return false;
        if (max is not null && v > max.Value) return false;
        return true;
    }

    /// <summary>Apply the value-sequence range (first/last) to the loaded results.</summary>
    [RelayCommand]
    private void ApplyResultRange() => ApplyResultFilter();

    /// <summary>Clear the value-sequence range back to unbounded, then re-filter.</summary>
    [RelayCommand]
    private void ResetResultRange()
    {
        SeqFirstMin = SeqFirstMax = SeqLastMin = SeqLastMax = "";
        ApplyResultFilter();
    }

    private void RebuildResultOptions()
    {
        FillDistinct(ResultClassOptions,  _allResults.Select(r => r.ClassName));
        FillDistinct(ResultFieldOptions,  _allResults.Select(r => r.PropName));
        FillDistinct(ResultObjectOptions, _allResults.Select(r => r.NormPath));
    }

    private static void FillDistinct(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var v in values.Where(s => !string.IsNullOrEmpty(s))
                                 .Distinct().OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            target.Add(v);
    }

    /// <summary>Open the selected result's object in the Live Walker tab.</summary>
    [RelayCommand]
    private void OpenInLiveWalker(SpcResultRow? row)
    {
        // Stale-session gating is enforced on the button (IsEnabled =
        // CanUseResultRowActions); the command itself stays guard-free for testability.
        if (row == null || string.IsNullOrEmpty(row.ObjAddr)) return;
        NavigateToInstance?.Invoke(row.ObjAddr);
    }

    /// <summary>Locate this row's owning object within the GWorld graph (reach mode —
    /// lands on the object and scrolls to the changed field).</summary>
    [RelayCommand]
    private void LocateRowInGWorld(SpcResultRow? row)
    {
        if (row == null || string.IsNullOrEmpty(row.ObjAddr)) return;
        LocateInGWorld?.Invoke(row.ObjAddr, row.PropOffset, row.PropName);
    }

    /// <summary>Engine-rooted counterpart — not gated on IsGWorldAvailable (engine
    /// availability is independent of GWorld; the DLL reports no_engine via the banner).</summary>
    [RelayCommand]
    private void LocateRowInGameEngine(SpcResultRow? row)
    {
        if (row == null || string.IsNullOrEmpty(row.ObjAddr)) return;
        LocateInGameEngine?.Invoke(row.ObjAddr, row.PropOffset, row.PropName);
    }

    /// <summary>Copy the matched field's live address (newest snapshot's obj_addr
    /// + offset) to the clipboard — a quick handoff into CE.</summary>
    [RelayCommand]
    private async Task CopyAddressAsync(SpcResultRow? row)
    {
        if (row == null || _platform == null) return;
        try
        {
            var hex = row.ObjAddr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? row.ObjAddr.Substring(2) : row.ObjAddr;
            if (ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var baseAddr))
            {
                ulong addr = baseAddr + (ulong)row.PropOffset;
                await _platform.CopyToClipboardAsync($"{addr:X}");
                StatusText = $"Copied {addr:X}  ({row.ClassName}::{row.PropName})";
            }
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "SPC: copy address failed", ex);
        }
    }

    private static SpcPredicateKind ParsePredicate(string s) => s switch
    {
        "Unchanged" => SpcPredicateKind.Unchanged,
        "Changed"   => SpcPredicateKind.Changed,
        "Increased" => SpcPredicateKind.Increased,
        "Decreased" => SpcPredicateKind.Decreased,
        _           => SpcPredicateKind.Any,
    };

    private static SpcJoinMode ParseJoinMode(string s) => s switch
    {
        "Loose"      => SpcJoinMode.Loose,
        "In-session" => SpcJoinMode.InSession,
        _            => SpcJoinMode.Strict,
    };

    // --- N1: noise picker helpers ---

    /// <summary>Rebuild the noise rows from a fresh result's Top-N contributors.
    /// All rows start unchecked — the user opts in by ticking + Apply.</summary>
    private void RebuildNoiseRows(IReadOnlyList<ClassNoiseRow> top)
    {
        NoiseRows.Clear();
        foreach (var c in top)
        {
            NoiseRows.Add(new NoiseRowVm
            {
                ClassName        = c.ClassName,
                HitCount         = c.HitCount,
                SamplePropsDisplay = c.SamplePropsDisplay,
            });
        }
    }

    /// <summary>Commit the ticked noise rows into the per-game denylist, persist,
    /// and re-run the SPC query so the user sees the cleaned result immediately.
    /// Untick all + Apply is the "clear" path.</summary>
    [RelayCommand]
    private async Task ApplyNoisePicksAsync()
    {
        // Build a FRESH set rather than mutating _excludedClasses in place: an
        // in-flight RunQueryAsync (background thread) may still be iterating the
        // captured reference via deny.Contains(), and HashSet is not safe for
        // concurrent read+write. Persist + reload then swaps _excludedClasses to a
        // new instance, leaving the running query's copy untouched. (Additive —
        // Apply never removes a previously-denied class; that's RemoveFromDenylist.)
        var updated = new HashSet<string>(_excludedClasses, StringComparer.Ordinal);
        bool changed = false;
        foreach (var row in NoiseRows)
            if (row.Picked && updated.Add(row.ClassName)) changed = true;
        if (!changed)
        {
            StatusText = "No noise classes picked — tick one or more rows first.";
            return;
        }
        _store.SetClassDenylist(DenylistScope.Spc, updated);
        LoadDenylistFromStore();   // reassigns _excludedClasses to a fresh instance
        await RunQueryAsync();
    }

    /// <summary>Untick all noise-picker rows (without touching the persisted
    /// denylist). Distinct from Clear all, which empties the denylist.</summary>
    [RelayCommand]
    private void ResetNoisePicks()
    {
        foreach (var row in NoiseRows) row.Picked = false;
    }

    /// <summary>Remove one class from the per-game denylist (used by the active-
    /// denylist chips). Re-runs the query so the user sees the un-cleaned result
    /// immediately.</summary>
    [RelayCommand]
    private async Task RemoveFromDenylistAsync(string? className)
    {
        if (string.IsNullOrEmpty(className) || !_excludedClasses.Contains(className)) return;
        var updated = new HashSet<string>(_excludedClasses, StringComparer.Ordinal);
        updated.Remove(className);
        _store.SetClassDenylist(DenylistScope.Spc, updated);
        LoadDenylistFromStore();
        await RunQueryAsync();
    }

    /// <summary>Drop every class from the denylist + re-run.</summary>
    [RelayCommand]
    private async Task ClearDenylistAsync()
    {
        if (_excludedClasses.Count == 0) return;
        _store.SetClassDenylist(DenylistScope.Spc, new HashSet<string>(StringComparer.Ordinal));
        LoadDenylistFromStore();
        await RunQueryAsync();
    }
}

/// <summary>One Top-N noise picker row in the SPC / Diff side panel. The
/// <see cref="Picked"/> flag is the user's tick; Apply collects them into the
/// per-game denylist.</summary>
public partial class NoiseRowVm : ObservableObject
{
    [ObservableProperty] private bool _picked;
    public string ClassName          { get; set; } = "";
    public int    HitCount           { get; set; }
    public string SamplePropsDisplay { get; set; } = "";
}

/// <summary>
/// Plain storage for one (snapshot, value-slot) cell of the SPC group matrix: a
/// directional predicate + an optional absolute window, as display strings. The
/// owning <see cref="SpcSnapshotPick"/> exposes the ACTIVE slot's cell via its
/// GroupActive* facades (which raise change notification), so these need no own
/// observability.
/// </summary>
public sealed class SpcGroupCellVm
{
    public string Predicate { get; set; } = "Any";
    public string AbsKind   { get; set; } = "(any value)";
    public string AbsLow    { get; set; } = "";
    public string AbsHigh   { get; set; } = "";
}
