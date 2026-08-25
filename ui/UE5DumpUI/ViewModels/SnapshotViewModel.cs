using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Channels;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the experimental Snapshot tab. Captures a type-agnostic
/// point-in-time snapshot of every numeric UPROPERTY of every (scoped) UObject
/// by streaming snapshot_chunk pipe calls into the SQLite store, then lists the
/// saved snapshots. Foundation for SPC Query / Class Pivot. See
/// docs/experimental-snapshot-spc-pivot.md Phase A.
/// </summary>
public partial class SnapshotViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ISnapshotStore _store;
    private readonly ILoggingService _log;
    private readonly IExperimentalGate? _gate;
    private readonly IPlatformService? _platform;
    private EngineState? _engineState;
    // Live game session id (PeHash-CreationTime; the process creation time is
    // unique per launch even on no-ASLR games). Diff rows' ObjAddr is the New
    // snapshot's session-local address, so the per-row Live/Addr/GWorld actions
    // are only valid when the New (DiffB) snapshot belongs to the current live
    // session. See EngineState.GameSessionId.
    private string _currentSessionId = "";
    private CancellationTokenSource? _cts;        // capture (streaming) op
    private CancellationTokenSource? _diffCts;    // diff (heavy in-memory) op

    // Capture heartbeat: the chunk loop only refreshes status AFTER each chunk
    // returns, so a slow (deep-container) chunk freezes the display for many
    // seconds and looks hung. A UI-thread timer ticks the elapsed clock + an
    // animated indicator while capturing — the UI thread is free during the
    // chunk's await, so the user always sees life even when the object count is
    // frozen between chunk completions. (build 1212)
    private DispatcherTimer? _captureHeartbeat;
    private DateTime _captureStart;
    private DateTime _lastChunkAt;
    private int _capOffset, _capTotal, _capObjects, _capFields;
    private int _heartbeatPhase;
    // Phase-0 telemetry totals (ms): DLL walk, DLL JSON build, full fetch round-trip
    // (walk+serialize+pipe+parse), and SQLite write. parse = fetch - walk - serialize.
    private long _capWalkMs, _capSerMs, _capFetchMs, _capWriteMs;
    // Split of the old "parse+pipe" bucket (C#-side): pipe read, the "Pipe RX" debug log,
    // the JsonNode DOM parse, and the model-build loop. The remainder (parse+pipe minus
    // these four) ≈ the DLL's .dump() + the pipe transfer.
    private long _capReadMs, _capRxLogMs, _capParseMs, _capBuildMs;

    [ObservableProperty] private string _label = "";
    [ObservableProperty] private bool   _gameOnly = true;
    /// <summary>Auto detect Engine/System noise (default ON). When on, the DLL skips
    /// pure engine/system classes (UI widgets, textures, sounds, Niagara, anim
    /// instances, /Script engine packages) BEFORE they enter the snapshot — saving
    /// capture time + DB size at the source, vs the post-capture Noise Picker that
    /// only filters the finished snapshot. A gameplay guardrail force-keeps
    /// Actor/Pawn/Character/component-derived classes, so a player Pawn's X/Y/Z is
    /// never dropped. Irreversible (re-capture to bring a skipped class back).</summary>
    [ObservableProperty] private bool   _autoSkipNoise = true;
    /// <summary>Native-C (P3, opt-in, default OFF). When on, each captured object also
    /// gets its unmanaged holes (non-UPROPERTY raw bytes) guessed + normalized into
    /// synthetic "&lt;raw@0xNN&gt;" fields, so SPC diff / Class Pivot can track native
    /// (HP/MP) values. Slower + larger capture; Pointer/Padding guesses are dropped.</summary>
    [ObservableProperty] private bool   _includeNativeFields;
    [ObservableProperty] private string _selectedScope = "NumericNoByte";
    /// <summary>Type-family narrowing applied ON TOP of the scope: "All numeric"
    /// (default), "Integers only" (drop Float/Double), or "Floats only" (drop every
    /// integer width). A source-level cut for type-specific hunts (floats = HP/MP/
    /// coords; integers = counts/flags/IDs) that shrinks the DB without touching the
    /// scope. Lossy by design — re-capture to bring the other family back.</summary>
    [ObservableProperty] private string _selectedFamily = "All numeric";
    /// <summary>Opt-in max-dataset cap: when the capture's live on-disk
    /// size (db + WAL) crosses this, the paging loop stops gracefully and KEEPS the
    /// partial snapshot (first-N objects). "Off" = no cap (prior behaviour). This is
    /// the in-capture ceiling the per-game quota lacks (quota only evicts whole older
    /// snapshots AFTER a capture finishes).</summary>
    [ObservableProperty] private string _selectedMaxDataset = "Off";
    [ObservableProperty] private string _estimateText = "";
    [ObservableProperty] private bool   _isEstimating;
    [ObservableProperty] private bool   _isCapturing;
    [ObservableProperty] private bool   _isDeleting;
    [ObservableProperty] private double _progress;          // 0..1
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private SnapshotMeta? _selectedSnapshot;
    [ObservableProperty] private string _selectedQuotaLabel = "1 GB";
    [ObservableProperty] private string _usageText = "";
    [ObservableProperty] private string _allGamesText = "";
    [ObservableProperty] private double _usageRatio;        // 0..1 for the bar
    [ObservableProperty] private bool   _showUsageBar = true;

    // --- Auto snapshot (periodic capture) ---
    // Persisted SETTINGS (defaults mirror SnapshotUiOptions). The interval, retention
    // policy, quota-auto-grow toggle and the free-disk-space guard thresholds.
    [ObservableProperty] private int    _autoSnapshotIntervalSec = 900;
    [ObservableProperty] private AutoSnapshotRetentionMode _retentionMode = AutoSnapshotRetentionMode.KeepRecent;
    [ObservableProperty] private int    _autoSnapshotCount = 10;
    [ObservableProperty] private bool   _autoSnapshotAdjustQuota;
    // Free-disk-space guard (blocks ALL captures — manual + auto): needs at least
    // min(percent% of drive, GB) free. Defaults 10% / 50 GB (whichever is smaller).
    [ObservableProperty] private int    _snapshotMinFreePercent = SnapshotDiskGuard.DefaultMinPercent;
    [ObservableProperty] private int    _snapshotMinFreeGb = SnapshotDiskGuard.DefaultMinGb;

    // Session-only running state (NOT persisted — a manual toggle that never
    // auto-resumes on the next launch, per the feature decision).
    [ObservableProperty] private bool   _autoSnapshotEnabled;
    [ObservableProperty] private string _autoStatusText = "";

    /// <summary>Minimum idle gap between the end of one snapshot and the start of
    /// the next (the game-impact breather + the interval floor).</summary>
    private const int AutoSnapshotMinIntervalSec = 60;

    // Ranges for the four NumericUpDown inputs on this panel. These MUST equal the
    // Minimum/Maximum written on the controls in SnapshotPanel.axaml — the coercion below
    // is what the user actually gets, so a control that allows more than the view model
    // accepts would silently snap their input. SnapshotIntervalCoercionTests reads both
    // files and fails on any disagreement, so the pair cannot drift apart unnoticed.
    private const int AutoSnapshotMaxIntervalSec  = 86400;   // 24 h
    private const int AutoSnapshotMinCount        = 1;
    private const int AutoSnapshotMaxCount        = 10000;
    private const int SnapshotMinFreePercentFloor = 0;
    private const int SnapshotMinFreePercentCeil  = 99;
    private const int SnapshotMinFreeGbFloor      = 0;
    private const int SnapshotMinFreeGbCeil       = 100000;

    private CancellationTokenSource? _autoCts;   // auto-loop lifetime
    private Task? _autoLoopTask;

    /// <summary>Test hook: await the running auto-snapshot loop to a deterministic stop.</summary>
    internal Task? AutoLoopTaskForTests => _autoLoopTask;

    public IReadOnlyList<AutoSnapshotRetentionMode> RetentionModeOptions { get; } =
        new[] { AutoSnapshotRetentionMode.KeepRecent, AutoSnapshotRetentionMode.FixedCount };

    /// <summary>Auto interval / retention / guard inputs are editable only when the
    /// loop is stopped and no capture is running.</summary>
    public bool CanEditAutoSettings => !AutoSnapshotEnabled && !IsCapturing;

    /// <summary>The Start/Stop toggle is enabled when connected (to start) or when
    /// already running (to stop).</summary>
    public bool CanToggleAuto => AutoSnapshotEnabled || _engineState != null;

    // ── NumericUpDown façades ────────────────────────────────────────────────
    // [SNAPINTERVAL-2026-08-20] NumericUpDown.Value is decimal?, and an emptied box drives it
    // to null. Bound straight at the int properties above, a compiled binding cannot convert
    // that and shows the user a raw
    //   System.InvalidCastException: Could not convert '(null)' (null) to System.Int32
    // while the field sits blank — with the auto-capture loop still running at a value the
    // screen no longer names. These four decimal? properties are what the AXAML binds, so no
    // conversion is attempted and NumericInput.Coerce decides what an empty or out-of-range
    // box means. Rationale and the two rejected alternatives: Helpers/NumericInput.cs.
    //
    // Each getter widens the int (always representable); each setter coerces and assigns back
    // through the observable property, so persistence and the auto loop keep reading one value.

    public decimal? AutoSnapshotIntervalSecValue
    {
        get => AutoSnapshotIntervalSec;
        set
        {
            AutoSnapshotIntervalSec = NumericInput.Coerce( value, AutoSnapshotIntervalSec, AutoSnapshotMinIntervalSec, AutoSnapshotMaxIntervalSec);
            // Notify UNCONDITIONALLY. A rejected or emptied entry leaves the backing value
            // unchanged, so nothing else would raise a change and the control would keep
            // painting an empty box while a different value was in force. Round 1 of
            // [SNAPINTERVAL-2026-08-20] fixed the exception and left exactly that behind;
            // the live check is what caught it.
            OnPropertyChanged();
        }
    }

    public decimal? AutoSnapshotCountValue
    {
        get => AutoSnapshotCount;
        set
        {
            AutoSnapshotCount = NumericInput.Coerce( value, AutoSnapshotCount, AutoSnapshotMinCount, AutoSnapshotMaxCount);
            // Notify UNCONDITIONALLY. A rejected or emptied entry leaves the backing value
            // unchanged, so nothing else would raise a change and the control would keep
            // painting an empty box while a different value was in force. Round 1 of
            // [SNAPINTERVAL-2026-08-20] fixed the exception and left exactly that behind;
            // the live check is what caught it.
            OnPropertyChanged();
        }
    }

    public decimal? SnapshotMinFreePercentValue
    {
        get => SnapshotMinFreePercent;
        set
        {
            SnapshotMinFreePercent = NumericInput.Coerce( value, SnapshotMinFreePercent, SnapshotMinFreePercentFloor, SnapshotMinFreePercentCeil);
            // Notify UNCONDITIONALLY. A rejected or emptied entry leaves the backing value
            // unchanged, so nothing else would raise a change and the control would keep
            // painting an empty box while a different value was in force. Round 1 of
            // [SNAPINTERVAL-2026-08-20] fixed the exception and left exactly that behind;
            // the live check is what caught it.
            OnPropertyChanged();
        }
    }

    public decimal? SnapshotMinFreeGbValue
    {
        get => SnapshotMinFreeGb;
        set
        {
            SnapshotMinFreeGb = NumericInput.Coerce( value, SnapshotMinFreeGb, SnapshotMinFreeGbFloor, SnapshotMinFreeGbCeil);
            // Notify UNCONDITIONALLY. A rejected or emptied entry leaves the backing value
            // unchanged, so nothing else would raise a change and the control would keep
            // painting an empty box while a different value was in force. Round 1 of
            // [SNAPINTERVAL-2026-08-20] fixed the exception and left exactly that behind;
            // the live check is what caught it.
            OnPropertyChanged();
        }
    }

    // The int properties stay the canonical ones — settings load, the auto loop and the disk
    // guard all read them, and none of those paths goes through a façade. So they keep their
    // own clamps (a hand-edited ui-options.json reaches them directly) and each one re-notifies
    // its façade, otherwise a programmatic change would leave the control showing the old number.

    partial void OnAutoSnapshotIntervalSecChanged(int value)
    {
        if (value < AutoSnapshotMinIntervalSec) { AutoSnapshotIntervalSec = AutoSnapshotMinIntervalSec; return; }
        if (value > AutoSnapshotMaxIntervalSec) { AutoSnapshotIntervalSec = AutoSnapshotMaxIntervalSec; return; }
        OnPropertyChanged(nameof(AutoSnapshotIntervalSecValue));
    }

    partial void OnAutoSnapshotCountChanged(int value)
    {
        if (value < AutoSnapshotMinCount) { AutoSnapshotCount = AutoSnapshotMinCount; return; }
        if (value > AutoSnapshotMaxCount) { AutoSnapshotCount = AutoSnapshotMaxCount; return; }
        OnPropertyChanged(nameof(AutoSnapshotCountValue));
    }

    partial void OnSnapshotMinFreePercentChanged(int value)
    {
        if (value < SnapshotMinFreePercentFloor) { SnapshotMinFreePercent = SnapshotMinFreePercentFloor; return; }
        if (value > SnapshotMinFreePercentCeil) { SnapshotMinFreePercent = SnapshotMinFreePercentCeil; return; }
        OnPropertyChanged(nameof(SnapshotMinFreePercentValue));
    }

    partial void OnSnapshotMinFreeGbChanged(int value)
    {
        if (value < SnapshotMinFreeGbFloor) { SnapshotMinFreeGb = SnapshotMinFreeGbFloor; return; }
        if (value > SnapshotMinFreeGbCeil) { SnapshotMinFreeGb = SnapshotMinFreeGbCeil; return; }
        OnPropertyChanged(nameof(SnapshotMinFreeGbValue));
    }

    partial void OnAutoSnapshotEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditAutoSettings));
        OnPropertyChanged(nameof(CanToggleAuto));
        OnPropertyChanged(nameof(CanManualCapture));
        if (value) StartAutoSnapshot();
        else       StopAutoSnapshot();
    }

    // Collapsible sections (E): the capture + compare regions fold away to give
    // the diff grid more room. Capture is force-opened while capturing.
    [ObservableProperty] private bool _captureSectionOpen = true;
    [ObservableProperty] private bool _compareSectionOpen = true;

    // --- Diff (compare two snapshots) ---
    [ObservableProperty] private SnapshotMeta? _diffA;      // old
    [ObservableProperty] private SnapshotMeta? _diffB;      // new
    [ObservableProperty] private string _diffClassFilter = "";
    [ObservableProperty] private string _diffPropFilter = "";
    [ObservableProperty] private string _diffObjectFilter = "";
    [ObservableProperty] private string _selectedDiffDirection = "Any";
    [ObservableProperty] private bool   _isDiffing;
    [ObservableProperty] private string _diffStatusText = "";
    [ObservableProperty] private SnapshotDiffRow? _selectedDiffRow;

    // Global filter (matches across every displayed column) + Old/New numeric
    // range. The range is applied on demand (Apply button) and cleared by Reset;
    // the text/global filters are live.
    [ObservableProperty] private string _diffGlobalFilter = "";
    [ObservableProperty] private string _diffOldMin = "";
    [ObservableProperty] private string _diffOldMax = "";
    [ObservableProperty] private string _diffNewMin = "";
    [ObservableProperty] private string _diffNewMax = "";

    partial void OnDiffGlobalFilterChanged(string value)
    {
        ApplyDiffFilter();
        _globalMemory.Schedule(value);
    }

    /// <summary>Per-session remembered global-filter keywords (LRU) surfaced as the
    /// global filter box's AutoCompleteBox suggestions — see <see cref="KeywordSearchMemory"/>.
    /// Only the global box gets memory; the Class/Field/Object boxes already surface
    /// data-derived distinct suggestions (Diff*Options).</summary>
    private readonly KeywordSearchMemory _globalMemory;
    public ObservableCollection<string> DiffGlobalHistory => _globalMemory.History;

    // Distinct candidate values from the last diff, feeding the Class/Field/Object
    // AutoCompleteBox pickers (partial match).
    public ObservableCollection<string> DiffClassOptions  { get; } = new();
    public ObservableCollection<string> DiffFieldOptions  { get; } = new();
    public ObservableCollection<string> DiffObjectOptions { get; } = new();

    // Full unfiltered changed set from the last Run Diff; the grid (DiffRows)
    // is a live client-side filter of this so typing in the filter boxes is
    // instant (no SQL re-query).
    private readonly List<SnapshotDiffRow> _allDiff = new();
    private string _diffSummary = "";

    /// <summary>Raised to open a diff row's object in the Live Walker tab.</summary>
    public event Action<string>? NavigateToInstance;

    /// <summary>Raised to locate a diff row's owning object in the GWorld graph
    /// (reach mode — land on the object, scroll to the changed field). Args:
    /// (objAddr, fieldOffset, fieldName). Same shape as SPC / Value Search.</summary>
    public event Action<string, int, string>? LocateInGWorld;

    /// <summary>Engine-rooted counterpart of <see cref="LocateInGWorld"/> (path search
    /// rooted at the live UGameEngine). Same payload.</summary>
    public event Action<string, int, string>? LocateInGameEngine;

    public IReadOnlyList<string> DiffDirectionOptions { get; } =
        new[] { "Any", "Increased", "Decreased" };

    public ObservableCollection<SnapshotDiffRow> DiffRows { get; } = new();

    // --- N1: per-game class denylist (noise picker) ---
    public ObservableCollection<NoiseRowVm> NoiseRows { get; } = new();
    public ObservableCollection<string> ActiveDenylist { get; } = new();
    [ObservableProperty] private bool _noisePanelOpen;
    private HashSet<string> _excludedClasses = new(StringComparer.Ordinal);

    /// <summary>Two distinct snapshots picked and not mid-diff.</summary>
    public bool CanRunDiff => DiffA != null && DiffB != null && DiffA != DiffB && !IsDiffing && !IsCapturing;

    /// <summary>True only when the New (DiffB) snapshot — whose session-local
    /// ObjAddr the diff rows carry — belongs to the CURRENT live session, so its
    /// addresses are still valid in the running game. Gates the per-row Live / Addr
    /// actions (a cross-session address is stale).</summary>
    public bool CanUseDiffRowActions =>
        !string.IsNullOrEmpty(_currentSessionId) && DiffB?.GameSessionId == _currentSessionId;
    /// <summary>As above, additionally requiring GWorld for the 🌍 button.</summary>
    // NOT gated on the client IsGWorldAvailable flag — see AE10 in ClassPivotViewModel.
    public bool CanLocateDiffRowInGWorld => CanUseDiffRowActions;

    partial void OnDiffAChanged(SnapshotMeta? value)   => OnPropertyChanged(nameof(CanRunDiff));
    partial void OnDiffBChanged(SnapshotMeta? value)
    {
        OnPropertyChanged(nameof(CanRunDiff));
        RaiseDiffRowActionGates();
    }
    private void RaiseDiffRowActionGates()
    {
        OnPropertyChanged(nameof(CanUseDiffRowActions));
        OnPropertyChanged(nameof(CanLocateDiffRowInGWorld));
        OnPropertyChanged(nameof(CanUseGroupRowActions));
        OnPropertyChanged(nameof(CanLocateGroupRowInGWorld));
    }
    partial void OnIsDiffingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunDiff));
        OnPropertyChanged(nameof(CanCapture));
        OnPropertyChanged(nameof(CanManualCapture));
        OnPropertyChanged(nameof(CanEditSettings));
    }

    /// <summary>Capture scope / quota / pickers are locked while a capture, diff,
    /// or size estimate is running, so the user can't change settings mid-operation.</summary>
    public bool CanEditSettings => !IsCapturing && !IsDiffing && !IsEstimating;

    /// <summary>A capture OR a size estimate is in flight — both surface the Cancel
    /// button (CancelCommand cancels whichever is running) and hide Capture/Estimate.</summary>
    public bool IsBusy => IsCapturing || IsEstimating;

    // Filter boxes narrow the loaded result live (client-side).
    partial void OnDiffClassFilterChanged(string value)      => ApplyDiffFilter();
    partial void OnDiffPropFilterChanged(string value)       => ApplyDiffFilter();
    partial void OnDiffObjectFilterChanged(string value)     => ApplyDiffFilter();
    partial void OnSelectedDiffDirectionChanged(string value) => ApplyDiffFilter();

    private void ApplyDiffFilter()
    {
        if (_allDiff.Count == 0 && DiffRows.Count == 0) return;
        // Whitespace-separated terms are ANDed (shared Object Tree filter semantics).
        // The Class/Field/Object boxes each AND within their single field; the global
        // box ANDs each term across every displayed column. Cache the split once per
        // rebuild (not per row).
        var clsTerms  = ObjectTreeFilter.SplitTerms(DiffClassFilter);
        var propTerms = ObjectTreeFilter.SplitTerms(DiffPropFilter);
        var objTerms  = ObjectTreeFilter.SplitTerms(DiffObjectFilter);
        var globTerms = ObjectTreeFilter.SplitTerms(DiffGlobalFilter);
        SnapshotDiffDirection? dir = SelectedDiffDirection switch
        {
            "Increased" => SnapshotDiffDirection.Up,
            "Decreased" => SnapshotDiffDirection.Down,
            _           => null,
        };
        double? oldMin = ParseBound(DiffOldMin), oldMax = ParseBound(DiffOldMax);
        double? newMin = ParseBound(DiffNewMin), newMax = ParseBound(DiffNewMax);

        SelectedDiffRow = null;   // detach before clearing the bound results grid
        DiffRows.Clear();
        foreach (var r in _allDiff)
        {
            if (clsTerms.Length  > 0 && !ObjectTreeFilter.MatchesAllTerms(clsTerms, r.ClassName)) continue;
            if (propTerms.Length > 0 && !ObjectTreeFilter.MatchesAllTerms(propTerms, r.PropName)) continue;
            if (objTerms.Length  > 0 && !ObjectTreeFilter.MatchesAllTerms(objTerms, r.NormPath)) continue;
            if (dir.HasValue && r.Direction != dir.Value) continue;
            if (globTerms.Length > 0 &&
                !ObjectTreeFilter.MatchesAllTerms(globTerms, r.ClassName, r.PropName, r.NormPath, r.OldValue, r.NewValue))
                continue;
            if (!WithinRange(r.OldValue, oldMin, oldMax)) continue;
            if (!WithinRange(r.NewValue, newMin, newMax)) continue;
            DiffRows.Add(r);
        }
        DiffStatusText = _diffSummary +
            (DiffRows.Count != _allDiff.Count ? $"  ·  showing {DiffRows.Count:N0}" : "");
    }

    private static double? ParseBound(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;

    // A value passes when no bound is set, or it parses numerically and falls
    // within the inclusive bounds. A set bound on a non-numeric value rejects.
    private static bool WithinRange(string rendered, double? min, double? max)
    {
        if (min is null && max is null) return true;
        if (!double.TryParse(rendered, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return false;
        if (min is not null && v < min.Value) return false;
        if (max is not null && v > max.Value) return false;
        return true;
    }

    /// <summary>Apply the Old/New numeric range to the loaded diff (button-driven).</summary>
    [RelayCommand]
    private void ApplyDiffRange() => ApplyDiffFilter();

    /// <summary>Clear the Old/New range back to unbounded, then re-filter.</summary>
    [RelayCommand]
    private void ResetDiffRange()
    {
        DiffOldMin = DiffOldMax = DiffNewMin = DiffNewMax = "";
        ApplyDiffFilter();
    }

    // Rebuild the distinct Class/Field/Object picker candidates from the loaded set.
    private void RebuildDiffOptions()
    {
        FillDistinct(DiffClassOptions,  _allDiff.Select(r => r.ClassName));
        FillDistinct(DiffFieldOptions,  _allDiff.Select(r => r.PropName));
        FillDistinct(DiffObjectOptions, _allDiff.Select(r => r.NormPath));
    }

    private static void FillDistinct(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var v in values.Where(s => !string.IsNullOrEmpty(s))
                                 .Distinct().OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            target.Add(v);
    }

    /// <summary>Capture scope: NumericNoByte (default, excludes 1-byte) or
    /// NumericAll (includes Int8/UInt8 — floods on small values).</summary>
    public IReadOnlyList<string> ScopeOptions { get; } = new[] { "NumericNoByte", "NumericAll" };

    /// <summary>Type-family picker labels. Maps to the wire family via <see cref="FamilyWire"/>.</summary>
    public IReadOnlyList<string> FamilyOptions { get; } =
        new[] { "All numeric", "Integers only", "Floats only" };

    /// <summary>The wire string ("Any" / "IntegersOnly" / "FloatsOnly") for the DLL.</summary>
    private string FamilyWire => SelectedFamily switch
    {
        "Integers only" => "IntegersOnly",
        "Floats only"   => "FloatsOnly",
        _               => "Any",
    };

    /// <summary>Max-dataset cap presets. "Off" disables the in-capture ceiling.</summary>
    public IReadOnlyList<string> MaxDatasetOptions { get; } =
        new[] { "Off", "512 MB", "1 GB", "2 GB", "4 GB" };

    /// <summary>The cap in bytes, or 0 when "Off" (no in-capture ceiling).</summary>
    private long MaxDatasetBytes => SelectedMaxDataset switch
    {
        "512 MB" => 512L * 1024 * 1024,
        "1 GB"   => 1024L * 1024 * 1024,
        "2 GB"   => 2L * 1024 * 1024 * 1024,
        "4 GB"   => 4L * 1024 * 1024 * 1024,
        _        => 0,
    };

    /// <summary>Per-game DB quota presets. "Unlimited" disables eviction.</summary>
    public IReadOnlyList<string> QuotaOptions { get; } =
        new[] { "512 MB", "1 GB", "2 GB", "5 GB", "Unlimited" };

    public ObservableCollection<SnapshotMeta> Snapshots { get; } = new();

    /// <summary>True once connected (engine state available) and not mid-capture,
    /// mid-diff, or mid-estimate. Also gates the auto loop's own capture attempts.</summary>
    public bool CanCapture => _engineState != null && !IsCapturing && !IsDiffing && !IsEstimating;

    /// <summary>Gates the MANUAL Capture / Estimate buttons: capturable and the auto
    /// loop isn't running (which owns capturing while enabled).</summary>
    public bool CanManualCapture => CanCapture && !AutoSnapshotEnabled;

    private int QuotaMb => _gate?.SnapshotQuotaMb ?? LabelToMb(SelectedQuotaLabel);
    private long QuotaBytes => QuotaMb <= 0 ? 0 : (long)QuotaMb * 1024 * 1024;

    public SnapshotViewModel(IDumpService dump, ISnapshotStore store, ILoggingService log,
                             IExperimentalGate? gate = null, IPlatformService? platform = null)
    {
        _dump = dump;
        _store = store;
        _log = log;
        _gate = gate;
        _platform = platform;
        _globalMemory = new KeywordSearchMemory(() => (DiffGlobalFilter, DiffRows.Count > 0));
        if (_gate != null) _selectedQuotaLabel = MbToLabel(_gate.SnapshotQuotaMb);
        // Don't list yet — the per-game DB isn't known until a game connects.
    }

    partial void OnSelectedQuotaLabelChanged(string value)
    {
        if (_gate != null) _gate.SnapshotQuotaMb = LabelToMb(value);
        ShowUsageBar = QuotaMb > 0;
        _ = UpdateUsageAsync();
    }

    private static int LabelToMb(string label) => label switch
    {
        "512 MB"    => 512,
        "1 GB"      => 1024,
        "2 GB"      => 2048,
        "5 GB"      => 5120,
        "Unlimited" => 0,
        _           => 1024,
    };

    private static string MbToLabel(int mb) => mb switch
    {
        512  => "512 MB",
        1024 => "1 GB",
        2048 => "2 GB",
        5120 => "5 GB",
        0    => "Unlimited",
        _    => "1 GB",
    };

    public void SetEngineState(EngineState state)
    {
        // A (re)connect resets auto snapshot — the running toggle is session-only and
        // the DB just re-scoped to this game; the user re-arms explicitly.
        StopAutoSnapshot();
        _engineState = state;
        _currentSessionId = state.GameSessionId;   // PeHash-CreationTime; matches capture-time GameSessionId
        RaiseDiffRowActionGates();
        // Scope the store to this game's DB, then load its saved snapshots.
        _store.SetActiveGame(state.PeHash);
        LoadDenylistFromStore();
        OnPropertyChanged(nameof(CanCapture));
        OnPropertyChanged(nameof(CanManualCapture));
        OnPropertyChanged(nameof(CanToggleAuto));
        // Kept (rather than discarded into `_`) only so the VM tests can await THIS
        // refresh instead of starting a second one that races it — see PendingRefresh.
        PendingRefresh = RefreshAsync();
    }

    /// <summary>The snapshot-list refresh <see cref="SetEngineState"/> kicked off.
    /// Exists purely as a test seam — production code never reads it.
    ///
    /// Both this and <see cref="RefreshCommand"/> end in
    /// <c>UiCollection.Reset(Snapshots, …)</c>, which Clear()s the collection and
    /// re-Add()s every row. A test that called SetEngineState and then awaited
    /// RefreshCommand ran the two CONCURRENTLY over the same ObservableCollection:
    /// measured 25/4000 in-process iterations observed a corrupt list — duplicated
    /// rows (both refreshes appending after a single Clear) and torn null slots from
    /// the unsynchronised List&lt;T&gt; growth, which surfaced as an NRE deep inside
    /// OnIsGroupModeChanged rather than as anything naming a race.</summary>
    internal Task? PendingRefresh { get; private set; }

    /// <summary>Stop the auto-snapshot loop (it re-scans the game on a tick and would
    /// otherwise hammer a dead pipe) and drop the live session id so per-row Live/Addr
    /// actions auto-disable on disconnect (audit X5). The saved snapshot corpus + diff
    /// rows are disk-backed and PRESERVED — a reconnect re-scopes via SetEngineState.</summary>
    public void ClearOnDisconnect()
    {
        StopAutoSnapshot();
        _currentSessionId = "";
        RaiseDiffRowActionGates();
    }

    private void LoadDenylistFromStore()
    {
        _excludedClasses = _store.GetClassDenylist(DenylistScope.Diff);
        ActiveDenylist.Clear();
        foreach (var c in _excludedClasses.OrderBy(s => s, StringComparer.Ordinal))
            ActiveDenylist.Add(c);
    }

    partial void OnIsCapturingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCapture));
        OnPropertyChanged(nameof(CanEditSettings));  // lock Scope/GameOnly/Quota/Label during capture
        OnPropertyChanged(nameof(CanRunDiff));        // and the Run Diff button
        OnPropertyChanged(nameof(IsBusy));            // swap Capture/Estimate <-> Cancel
        OnPropertyChanged(nameof(CanEditAutoSettings));
        OnPropertyChanged(nameof(CanManualCapture));
    }

    partial void OnIsEstimatingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCapture));       // Capture + Estimate buttons gate on this
        OnPropertyChanged(nameof(CanManualCapture));
        OnPropertyChanged(nameof(CanEditSettings));  // lock pickers while sampling
        OnPropertyChanged(nameof(IsBusy));            // show Cancel so a long estimate is cancellable
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var list = await Task.Run(() => _store.ListSnapshotsAsync());
            // Preserve the diff picks across a refresh (a capture finishes -> this
            // runs) by id, since Reset detaches every selection bound to Snapshots.
            long? keepA = DiffA?.Id, keepB = DiffB?.Id, keepG = GroupSnapshot?.Id, keepGC = GroupCompareSnapshot?.Id;
            UiCollection.Reset(Snapshots, list,
                () => { SelectedSnapshot = null; DiffA = null; DiffB = null; GroupSnapshot = null; GroupCompareSnapshot = null; });
            if (keepA.HasValue) DiffA = Snapshots.FirstOrDefault(s => s.Id == keepA.Value);
            if (keepB.HasValue) DiffB = Snapshots.FirstOrDefault(s => s.Id == keepB.Value);
            // Group mode: preserve the primary pick (default to newest) AND the optional
            // compare pick (Mode B) — else a capture-triggered refresh silently drops the
            // compare snapshot and reverts Mode B to Mode A. Don't default the compare:
            // null = Mode A is the intended default.
            if (keepG.HasValue) GroupSnapshot = Snapshots.FirstOrDefault(s => s.Id == keepG.Value);
            GroupSnapshot ??= Snapshots.FirstOrDefault(s => s.IsUsable);
            if (keepGC.HasValue) GroupCompareSnapshot = Snapshots.FirstOrDefault(s => s.Id == keepGC.Value);
            await UpdateUsageAsync();
            // Convenience: default the diff pickers to the two newest USABLE snapshots
            // (A = older, B = newer) so "Run Diff" is one click after capturing. Unusable
            // (drift-flagged) snapshots are never auto-selected.
            if (DiffA == null && DiffB == null)
            {
                var usable = Snapshots.Where(s => s.IsUsable).ToList();
                if (usable.Count >= 2)
                {
                    DiffB = usable[0];   // newest usable
                    DiffA = usable[1];   // second-newest usable
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Snapshot: list failed", ex);
            SetError(ex);
        }
    }

    private async Task UpdateUsageAsync()
    {
        try
        {
            var u = await Task.Run(() => _store.GetUsageAsync());
            ShowUsageBar = QuotaMb > 0;
            if (QuotaMb > 0)
            {
                UsageText  = $"{SnapshotFormat.Bytes(u.GameDbBytes)} / {MbToLabel(QuotaMb)}";
                UsageRatio = QuotaBytes > 0 ? Math.Min(1.0, u.GameDbBytes / (double)QuotaBytes) : 0;
            }
            else
            {
                UsageText  = $"{SnapshotFormat.Bytes(u.GameDbBytes)} (no limit)";
                UsageRatio = 0;
            }
            AllGamesText = $"All games on disk: {SnapshotFormat.Bytes(u.AllGamesBytes)}";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Snapshot: usage query failed", ex);
        }
    }

    [RelayCommand]
    private async Task CaptureAsync() => await CaptureCoreAsync(isAuto: false);

    /// <summary>
    /// The capture engine shared by the manual Capture button and the auto-snapshot
    /// loop. Enforces the free-disk-space guard first (blocks ALL captures), streams
    /// the snapshot into SQLite, and returns a <see cref="CaptureOutcome"/> so the auto
    /// loop can react. <paramref name="externalCt"/> (the auto loop's token) is linked
    /// into <c>_cts</c> so both the Cancel button and the Stop toggle abort the capture.
    /// The byte-quota eviction is applied inline ONLY for a manual capture; the auto
    /// loop owns retention + quota-grow, so it passes <paramref name="isAuto"/> true to
    /// skip the inline eviction and enforce its own policy afterward.
    /// </summary>
    private async Task<CaptureOutcome> CaptureCoreAsync(bool isAuto, CancellationToken externalCt = default)
    {
        if (!CanCapture) return CaptureOutcome.NotReady;
        ClearError();

        // Record what this heavy operation costs the DLL dispatcher. Automatic
        // rather than a measurement session: the evidence then accumulates from
        // real use instead of only the scenario somebody thought to test. Labelled
        // manual vs auto because the auto loop's cadence is its own question.
        // Free-disk-space guard — refuse ALL captures (manual + auto) when the DB
        // drive is below the required free space, so a multi-GB capture can't fill it.
        if (!DiskGuardPasses())
        {
            var msg = DiskGuardMessage();
            StatusText = msg;
            _log.Warn(Constants.LogCatView, "Snapshot: " + msg);
            return CaptureOutcome.DiskLow;
        }

        // Probe opened AFTER the guard: a refused capture used to cost two get_diagnostics
        // round-trips and file a "Snapshot capture" sample for a capture that never started.
        // Worse here than elsewhere because the AUTO loop retries on a timer, so a full disk
        // produced a steady stream of fake samples. (audit #5 AE8, sibling site)
        await using var _perf = await Services.DiagnosticsProbe.BeginAsync(
            _dump, _log, isAuto ? "Snapshot capture (auto)" : "Snapshot capture");

        var engine = _engineState!;
        var dataType = SelectedScope;
        // Lock the capture options ONCE: every snapshot_chunk in the paging loop
        // below must carry the same values, so a mid-capture toggle can't make the
        // chunks inconsistent. (The checkboxes are also disabled via CanEditSettings
        // while capturing — this is belt-and-suspenders.)
        bool gameOnly       = GameOnly;
        bool autoSkipNoise  = AutoSkipNoise;
        bool includeNative  = IncludeNativeFields;
        string numericFamily = FamilyWire;     // "Any" / "IntegersOnly" / "FloatsOnly"
        long maxBytes        = MaxDatasetBytes; // 0 = no in-capture cap
        // Free-disk guard thresholds captured for the mid-capture poll (protects against
        // a single huge capture that passed the pre-check but fills the drive as it runs).
        int  guardPct    = SnapshotMinFreePercent;
        int  guardGb     = SnapshotMinFreeGb;
        bool guardActive = _platform != null;
        string dbPath    = _store.DatabasePath;
        // Max-dataset cap: the consumer sets capReached=1 when the live
        // db+WAL size crosses maxBytes; the producer then stops gracefully and the
        // PARTIAL snapshot is kept + finalised (NOT deleted like a user-cancel).
        int  capReached      = 0;
        long cappedAtBytes   = 0;
        // Mid-capture low-disk stop shares the capReached graceful-stop path (KEEPS the
        // partial — deleting on a nearly-full disk is unsafe, VACUUM needs scratch space).
        int  diskLowReached  = 0;
        long diskLowAtBytes  = 0;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _cts.Token;
        IsCapturing = true;
        CaptureSectionOpen = true;   // force the capture region visible while capturing
        Progress = 0;
        EstimateText = "";           // clear any stale pre-flight estimate from a prior run
        _captureStart = DateTime.UtcNow;
        _lastChunkAt  = _captureStart;
        _capOffset = _capTotal = _capObjects = _capFields = 0;
        _capWalkMs = _capSerMs = _capFetchMs = _capWriteMs = 0;
        _capReadMs = _capRxLogMs = _capParseMs = _capBuildMs = 0;
        _heartbeatPhase = 0;

        CaptureOutcome outcome = CaptureOutcome.Failed;
        long snapshotId = 0;

        // Delete + reclaim the partial snapshot left by ANY abnormal exit — user cancel,
        // pipe disconnect (bare OCE), an unexpected pipe death (IOException), or any other
        // mid-capture failure. is_usable defaults to 1 and CompleteSnapshotAsync never ran,
        // so leaving the row would surface a truncated snapshot to SPC/Pivot/diff as if
        // complete. Reclaim too: a cancelled snapshot can be multi-GB and DELETE alone
        // leaves the file size unchanged until a Delete All. NO ct: the capture token may
        // already be cancelled, but this cleanup (incl. a possibly multi-second VACUUM)
        // must run to completion.
        async Task RemovePartialAsync()
        {
            StatusText = "Removing incomplete snapshot…";
            try { await Task.Run(() => _store.DeleteSnapshotAsync(snapshotId, reclaim: true)); }
            catch (Exception ex) { _log.Warn(Constants.LogCatView, $"Snapshot: partial cleanup failed: {ex.Message}"); }
            await RefreshAsync();
            await UpdateUsageAsync();   // reflect the reclaimed space in the usage bar
        }

        try
        {
            // Auto-reclaim any previously-flagged UNUSABLE snapshots (captures that
            // spanned a GObjects drift) before starting a fresh one — deferred cleanup
            // so the user saw them flagged in the list until now. Best-effort: a
            // cleanup hiccup must never block a new capture.
            try
            {
                int purged = await Task.Run(() => _store.DeleteUnusableSnapshotsAsync(ct), ct);
                if (purged > 0)
                    _log.Info(Constants.LogCatView, $"Snapshot: auto-removed {purged} unusable snapshot(s) before capture");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.Warn(Constants.LogCatView, $"Snapshot: unusable cleanup skipped: {ex.Message}"); }

            int total = await _dump.BeginSnapshotAsync(dataType, ct);
            _capTotal = total;
            StartCaptureHeartbeat();

            var meta = new SnapshotMeta
            {
                Label         = string.IsNullOrWhiteSpace(Label) ? DefaultLabel() : Label.Trim(),
                CapturedAt    = DateTime.UtcNow.ToString("o"),
                PeHash        = engine.PeHash,
                // PeHash-CreationTime: the process creation time differs per
                // launch even on no-ASLR games (where ModuleBase is constant),
                // so it distinguishes game restarts (sessions) of the same build
                // for cross-session SPC + the stale-session row-action gate.
                GameSessionId = engine.GameSessionId,
                UeVersion     = engine.UEVersion,
                Scope         = dataType,
            };
            snapshotId = await _store.CreateSnapshotAsync(meta, ct);

            // Producer/consumer: fetch chunk N+1 over the pipe while chunk N is written
            // to SQLite, so the DLL walk and the DB write OVERLAP instead of running
            // back-to-back. The consumer owns one bulk-capture session (single connection,
            // bulk pragmas, commit-every-N). A linked CTS lets a consumer (write) failure
            // stop the producer without deadlocking on the bounded channel, and vice-versa.
            int objectCount = 0, fieldCount = 0;
            // Consistency guard: watch the GObjects count each chunk reports. A level
            // transition / mass spawn-free mid-capture swings it by thousands, so the
            // snapshot becomes a temporally-inconsistent mix of two worlds. On drift we
            // stop early and finalize the partial as UNUSABLE (excluded from SPC/Pivot,
            // auto-removed before the next capture). Written only by the producer task;
            // read after WhenAll (the await is the memory barrier).
            int minTotal = total, maxTotal = total;
            bool driftDetected = false;
            await using (var session = await _store.BeginCaptureSessionAsync(ct))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var lct = linked.Token;
                var channel = Channel.CreateBounded<SnapshotChunkResult>(
                    new BoundedChannelOptions(2) { SingleReader = true, SingleWriter = true });

                var producer = Task.Run(async () =>
                {
                    try
                    {
                        int offset = 0;
                        // Stop fetching once the consumer flags the max-dataset cap (graceful;
                        // the buffered chunks already in the channel still drain + persist).
                        while (!lct.IsCancellationRequested && Volatile.Read(ref capReached) == 0)
                        {
                            var fetchSw = System.Diagnostics.Stopwatch.StartNew();
                            var chunk = await _dump.SnapshotChunkAsync(
                                dataType, gameOnly, offset, Constants.SnapshotChunkSize,
                                includeNative, autoSkipNoise, numericFamily, lct);
                            fetchSw.Stop();
                            // Phase-0 telemetry: full fetch round-trip + DLL-reported splits
                            // + the C#-side parse+pipe breakdown (read / RX-log / parse / build).
                            _capFetchMs += fetchSw.ElapsedMilliseconds;
                            _capWalkMs  += chunk.WalkMs;
                            _capSerMs   += chunk.SerializeMs;
                            _capReadMs  += chunk.ReadMs;
                            _capRxLogMs += chunk.RxLogMs;
                            _capParseMs += chunk.ParseMs;
                            _capBuildMs += chunk.BuildMs;
                            offset += chunk.Scanned;
                            _capOffset = offset;     // display-only; heartbeat reads on UI thread
                            _lastChunkAt = DateTime.UtcNow;
                            // Track GObjects-count drift across chunks (begin is folded in
                            // by the seeded min/max), then persist this still-valid chunk.
                            if (chunk.Total > 0)
                            {
                                if (chunk.Total < minTotal) minTotal = chunk.Total;
                                if (chunk.Total > maxTotal) maxTotal = chunk.Total;
                            }
                            await channel.Writer.WriteAsync(chunk, lct);
                            if (!driftDetected &&
                                SnapshotConsistency.IsDriftSuspect(total, minTotal, maxTotal))
                            {
                                driftDetected = true;
                                break;   // world churned mid-capture — stop; finalize partial as unusable
                            }
                            if (chunk.Scanned == 0 || offset >= chunk.Total) break;
                        }
                    }
                    // Swallow cancellation ONLY when OUR token actually cancelled it — a
                    // genuine user Cancel/Stop (ct→lct) or the consumer's linked.Cancel() on
                    // a write failure. A pipe DISCONNECT mid-chunk surfaces as a *bare*
                    // OperationCanceledException (PipeClient completes pending requests via
                    // TrySetCanceled() with NO token) while lct is NOT cancelled — that must
                    // NOT be swallowed: letting it fault the producer makes Task.WhenAll
                    // rethrow, so CompleteSnapshotAsync (which would mark the row usable) is
                    // skipped and the outer catch deletes the partial. Swallowing it would
                    // finalise a half-captured snapshot as is_usable=1 (the column default),
                    // poisoning SPC/Pivot/diff with a truncated-but-"complete" dataset.
                    catch (OperationCanceledException) when (lct.IsCancellationRequested) { }
                    finally { channel.Writer.TryComplete(); }
                }, lct);

                var consumer = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var chunk in channel.Reader.ReadAllAsync(lct))
                        {
                            if (chunk.Objects.Count == 0) continue;
                            var writeSw = System.Diagnostics.Stopwatch.StartNew();
                            int n = session.WriteChunk(snapshotId, chunk.Objects, lct);
                            writeSw.Stop();
                            _capWriteMs += writeSw.ElapsedMilliseconds;
                            objectCount += chunk.Objects.Count;
                            fieldCount  += n;
                            _capObjects = objectCount;   // display-only
                            _capFields  = fieldCount;

                            // Max-dataset cap: once the live db+WAL footprint crosses the
                            // ceiling, signal the producer to stop. We KEEP what's written
                            // (a partial snapshot of the first-N objects), so this finalises
                            // normally below — distinct from the user-cancel delete path.
                            // The free-disk guard shares this graceful-stop path: if the
                            // drive drops below the required free space mid-capture, stop
                            // and keep the partial too.
                            if (Volatile.Read(ref capReached) == 0)
                            {
                                long live = session.CurrentSizeBytes();
                                if (maxBytes > 0 && live >= maxBytes)
                                {
                                    cappedAtBytes = live;
                                    Volatile.Write(ref capReached, 1);
                                }
                                else if (guardActive &&
                                         !SnapshotDiskGuard.HasEnoughFree(
                                             _platform!.GetFreeDiskSpaceBytes(dbPath),
                                             _platform!.GetTotalDiskSpaceBytes(dbPath),
                                             guardPct, guardGb))
                                {
                                    diskLowAtBytes = live;
                                    Volatile.Write(ref diskLowReached, 1);
                                    Volatile.Write(ref capReached, 1);   // graceful stop, keep partial
                                }
                            }
                        }
                    }
                    catch { linked.Cancel(); throw; }   // unblock the producer on a write failure
                }, lct);

                await Task.WhenAll(producer, consumer);

                StopCaptureHeartbeat();   // stop ticking before the terminal messages
                StatusText = "Finalising…";
                // Incremental pivot counts on the session connection — replaces the ~10s
                // COUNT(DISTINCT) GROUP BY ×2 the lazy build runs (the documented finalize
                // freeze). Dispose then restores pragmas + closes.
                await session.CompleteSnapshotAsync(snapshotId, objectCount, fieldCount, !driftDetected, ct);
            }

            // FIFO eviction: drop oldest snapshots of this game until the DB fits the
            // quota (the just-captured one is always kept). For an AUTO capture the loop
            // owns retention + quota-grow, so the inline eviction is skipped here.
            bool wasDiskLow = Volatile.Read(ref diskLowReached) != 0;
            bool wasCapped  = Volatile.Read(ref capReached) != 0 && !wasDiskLow;
            string evicted = "";
            if (!isAuto)
            {
                int dropped = await Task.Run(() => _store.EnforceQuotaAsync(QuotaBytes, ct), ct);
                evicted = dropped > 0 ? $" — dropped {dropped} oldest (quota)" : "";
            }
            var cappedNote = wasCapped
                ? $" — stopped at {SnapshotFormat.Bytes(cappedAtBytes)} cap (partial: first {_capOffset:N0} of {total:N0} objects)"
                : "";
            var diskLowNote = wasDiskLow
                ? $" — ⚠ low disk space at {SnapshotFormat.Bytes(diskLowAtBytes)}; kept partial (first {_capOffset:N0} of {total:N0} objects)"
                : "";
            // Phase-0 telemetry: one summary line with the full cost breakdown for
            // before/after comparison. parse+pipe is now split into pipe-read / RX-log /
            // json-parse / model-build; the leftover (other) ≈ the DLL .dump() + pipe transfer.
            long parseMs = Math.Max(0, _capFetchMs - _capWalkMs - _capSerMs);
            long otherMs = Math.Max(0, parseMs - _capReadMs - _capRxLogMs - _capParseMs - _capBuildMs);
            _log.Info(Constants.LogCatView,
                $"Capture done: {objectCount:N0} objects, {fieldCount:N0} fields in " +
                $"{FmtSpan(DateTime.UtcNow - _captureStart)} — walk {_capWalkMs}ms, serialize {_capSerMs}ms, " +
                $"parse+pipe {parseMs}ms [read {_capReadMs} / rxlog {_capRxLogMs} / jsonparse {_capParseMs} / " +
                $"build {_capBuildMs} / other {otherMs}], write {_capWriteMs}ms");

            // A large capture parses MILLIONS of transient field objects (JSON DOM +
            // model build); under the default Workstation GC the grown heap stays
            // committed, so after a multi-snapshot session the working set can sit at
            // several GB even though almost nothing is live — which is what made the UI
            // look like it "ate too much" before the user could even use Class Pivot.
            // Reclaim + COMPACT now (a natural pause, negligible vs the ~45s capture) and
            // log managed-heap vs working-set so retention is measurable: if heap drops,
            // the memory was reclaimable garbage; if it stays high, it's a live retainer.
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            proc.Refresh();
            long beforeWs = proc.WorkingSet64 / 1048576, beforeHeap = GC.GetTotalMemory(false) / 1048576;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            proc.Refresh();
            _log.Info(Constants.LogCatView,
                $"Post-capture memory: managed heap {beforeHeap:N0}->{GC.GetTotalMemory(false) / 1048576:N0} MB, " +
                $"working set {beforeWs:N0}->{proc.WorkingSet64 / 1048576:N0} MB (after compacting GC)");

            var driftNote = driftDetected
                ? $" — ⚠ GObjects changed mid-capture ({total:N0}→{maxTotal:N0}); marked UNUSABLE (excluded from SPC/Pivot, auto-removed before next capture)"
                : "";
            outcome = (wasCapped || wasDiskLow) ? CaptureOutcome.Partial : CaptureOutcome.Success;
            StatusText = $"Captured {objectCount:N0} objects, {fieldCount:N0} fields{driftNote}{cappedNote}{diskLowNote}{evicted}";
            Label = "";
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            // A genuine user Cancel/Stop cancels OUR token; a pipe DISCONNECT surfaces as a
            // *bare* OperationCanceledException (PipeClient completes pending requests via
            // TrySetCanceled() with no token) while the token is NOT cancelled. Report the
            // disconnect as a FAILURE, not a cancel: outcome=Cancelled would make the
            // auto-loop's `case Cancelled` skip StopAutoSnapshot() and wedge
            // (AutoSnapshotEnabled stuck true, _autoCts leaked, manual capture disabled). (M7)
            // BOTH paths still delete the partial — it is is_usable=1 by default. (H1)
            bool userCancelled = ct.IsCancellationRequested;
            outcome = userCancelled ? CaptureOutcome.Cancelled : CaptureOutcome.Failed;
            StopCaptureHeartbeat();
            if (snapshotId > 0)
            {
                await RemovePartialAsync();
                StatusText = userCancelled
                    ? "Capture cancelled — incomplete data removed."
                    : "Capture failed (disconnected) — incomplete data removed.";
            }
            else
            {
                StatusText = userCancelled ? "Capture cancelled." : "Capture failed (disconnected).";
            }
        }
        catch (Exception ex)
        {
            outcome = CaptureOutcome.Failed;
            StopCaptureHeartbeat();
            _log.Error(Constants.LogCatView, "Snapshot: capture failed", ex);
            SetError(ex);
            // A non-OCE mid-capture failure (e.g. an unexpected pipe death → IOException,
            // or a between-chunks disconnect → InvalidOperationException) leaves the same
            // is_usable=1 partial — delete it so it can't surface as a complete snapshot. (H1)
            if (snapshotId > 0)
            {
                await RemovePartialAsync();
                StatusText = "Capture failed — incomplete data removed.";
            }
            else
            {
                StatusText = "Capture failed.";
            }
        }
        finally
        {
            StopCaptureHeartbeat();   // idempotent — belt-and-suspenders cleanup
            IsCapturing = false;
            Progress = 0;
            _cts?.Dispose();
            _cts = null;
        }
        return outcome;
    }

    /// <summary>
    /// Start the capture heartbeat: a UI-thread timer that re-renders the status
    /// line every 400 ms so the elapsed clock + animated dots keep moving even
    /// while a single slow chunk (deep-container objects) is in flight — the
    /// difference between "still working" and "looks hung". The UI thread is free
    /// during the chunk's await, so the tick fires.
    /// </summary>
    private void StartCaptureHeartbeat()
    {
        StopCaptureHeartbeat();
        RenderCaptureStatus();   // paint once immediately
        _captureHeartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _captureHeartbeat.Tick += OnCaptureHeartbeat;
        _captureHeartbeat.Start();
    }

    private void StopCaptureHeartbeat()
    {
        if (_captureHeartbeat == null) return;
        _captureHeartbeat.Stop();
        _captureHeartbeat.Tick -= OnCaptureHeartbeat;
        _captureHeartbeat = null;
    }

    private void OnCaptureHeartbeat(object? sender, EventArgs e)
    {
        _heartbeatPhase++;
        RenderCaptureStatus();
    }

    /// <summary>
    /// Compose the live "Capturing…" status from the latest chunk counts + a
    /// live elapsed clock. Animated trailing dots move every tick so the line is
    /// visibly alive even when the object count is frozen mid-chunk; if the
    /// current batch has been running a few seconds, say so explicitly — the
    /// "is it hung or working?" reassurance for deep-container objects.
    /// </summary>
    private void RenderCaptureStatus()
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _captureStart;
        // Progress is computed HERE (UI thread, via the heartbeat) from the producer's
        // _capOffset/_capTotal, so the background producer never mutates the bound
        // ObservableProperty off-thread.
        Progress = _capTotal > 0 ? Math.Min(1.0, _capOffset / (double)_capTotal) : 0;
        string dots = new string('.', _heartbeatPhase % 4);   // animate 0..3 dots
        string timing = $" · {FmtSpan(elapsed)} elapsed";
        if (Progress >= 0.02)
        {
            // ETA from fraction-done; auto-omits while a slow chunk makes the
            // estimate go stale (remain ≤ 0), leaving just the ticking clock.
            var remain = TimeSpan.FromSeconds(elapsed.TotalSeconds / Progress) - elapsed;
            if (remain > TimeSpan.Zero) timing += $", ~{FmtSpan(remain)} left";
        }
        var batchAge = now - _lastChunkAt;
        string batch = batchAge > TimeSpan.FromSeconds(3)
            ? $" · still scanning this batch ({FmtSpan(batchAge)})"
            : "";
        // {dots,-3}: fixed 3-char slot so the count column doesn't jitter as the
        // dots animate.
        StatusText = $"Capturing{dots,-3} {_capOffset:N0}/{_capTotal:N0} ({Progress:P0}) — " +
                     $"{_capObjects:N0} objects, {_capFields:N0} fields{timing}{CapturePerfSuffix()}{batch}";
    }

    // Phase-0 telemetry: a compact walk/serialize/parse/write breakdown (seconds),
    // shown once the totals are meaningful so we optimise capture on real numbers.
    private string CapturePerfSuffix()
    {
        long parse = _capFetchMs - _capWalkMs - _capSerMs;
        if (parse < 0) parse = 0;
        if (_capFetchMs + _capWriteMs < 500) return "";   // too early to be useful
        return $" · walk {_capWalkMs / 1000.0:0.0}s / ser {_capSerMs / 1000.0:0.0}s / " +
               $"parse {parse / 1000.0:0.0}s / write {_capWriteMs / 1000.0:0.0}s";
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        _estimateCts?.Cancel();
        // Cancelling during an auto snapshot stops the whole loop (spec: cancelling an
        // auto snapshot is just as cancellable), not just the in-flight capture.
        if (_autoCts != null) StopAutoSnapshot();
    }

    // ----------------------------------------------------------------------
    // Auto snapshot (periodic capture) loop + free-disk-space guard
    // ----------------------------------------------------------------------

    /// <summary>Does the DB drive currently have enough free space to allow a write?
    /// True when no platform service is available (tests / non-Windows) so the guard
    /// never blocks on a measurement it can't take.</summary>
    private bool DiskGuardPasses()
    {
        if (_platform == null) return true;
        long free  = _platform.GetFreeDiskSpaceBytes(_store.DatabasePath);
        long total = _platform.GetTotalDiskSpaceBytes(_store.DatabasePath);
        return SnapshotDiskGuard.HasEnoughFree(free, total, SnapshotMinFreePercent, SnapshotMinFreeGb);
    }

    /// <summary>Human message for a blocked capture: drive, free, and required free.</summary>
    private string DiskGuardMessage()
    {
        long free  = _platform?.GetFreeDiskSpaceBytes(_store.DatabasePath) ?? 0;
        long total = _platform?.GetTotalDiskSpaceBytes(_store.DatabasePath) ?? 0;
        long need  = SnapshotDiskGuard.RequiredFreeBytes(total, SnapshotMinFreePercent, SnapshotMinFreeGb);
        var root   = System.IO.Path.GetPathRoot(_store.DatabasePath) ?? "?";
        return $"Low disk space on {root}: {SnapshotFormat.Bytes(free)} free, needs ≥ {SnapshotFormat.Bytes(need)}. Capture skipped.";
    }

    /// <summary>Estimated on-disk size of the newest snapshot (for quota-grow math).</summary>
    private long NewestSnapshotBytes()
        => Snapshots.Count > 0 ? Snapshots[0].EstBytes : 0;

    /// <summary>Raise (never lower) the byte quota so it can retain the desired count,
    /// bumping through presets or to Unlimited. No-op when there's no gate.</summary>
    private void ApplyAutoQuota(long? bytes)
    {
        if (_gate == null) return;
        if (bytes == null)                        // exceeds every preset → Unlimited
        {
            if (_gate.SnapshotQuotaMb != 0) SelectedQuotaLabel = MbToLabel(0);
            return;
        }
        int mb = (int)(bytes.Value / (1024L * 1024));
        if (mb == _gate.SnapshotQuotaMb) return;  // no change
        // Setting the label routes through OnSelectedQuotaLabelChanged → gate + usage.
        SelectedQuotaLabel = MbToLabel(mb);
    }

    private void StartAutoSnapshot()
    {
        if (_autoCts != null) return;             // already running
        if (_engineState == null)
        {
            AutoStatusText = "Connect to a game before starting auto snapshot.";
            AutoSnapshotEnabled = false;
            return;
        }
        _autoCts = new CancellationTokenSource();
        AutoStatusText = "Auto snapshot started.";
        _autoLoopTask = RunAutoLoopAsync(_autoCts.Token);
    }

    /// <summary>Stop the auto-snapshot loop (idempotent). Cancels the loop token — which
    /// also cancels any in-flight capture (its CTS is linked to this one) — and clears
    /// the toggle. Safe to call from the loop itself, on disconnect, and on shutdown.</summary>
    public void StopAutoSnapshot()
    {
        var cts = _autoCts;
        _autoCts = null;
        if (cts != null)
        {
            try { cts.Cancel(); } catch { /* already disposed */ }
            cts.Dispose();
        }
        if (AutoSnapshotEnabled) AutoSnapshotEnabled = false;   // reflect in UI (idempotent)
    }

    /// <summary>The periodic-capture loop. Runs on the UI sync-context (started from a
    /// property setter), so ObservableProperty writes here are UI-thread safe; heavy
    /// store ops go through Task.Run. Retention + quota-grow live here (not in the
    /// capture engine). Stops itself on completion, low disk, or capture failure.</summary>
    private async Task RunAutoLoopAsync(CancellationToken ct)
    {
        int captured = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Wait until capturable — a manual diff / estimate may be running.
                while (!ct.IsCancellationRequested && !CanCapture)
                {
                    AutoStatusText = "Auto: waiting (busy)…";
                    await Task.Delay(1000, ct);
                }
                if (ct.IsCancellationRequested) break;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var outcome = await CaptureCoreAsync(isAuto: true, ct);
                sw.Stop();
                if (ct.IsCancellationRequested) break;

                switch (outcome)
                {
                    case CaptureOutcome.DiskLow:
                        AutoStatusText = "Auto stopped: low disk space.";
                        StopAutoSnapshot();
                        return;
                    case CaptureOutcome.Failed:
                    case CaptureOutcome.NotReady:
                        AutoStatusText = "Auto stopped: capture failed (disconnected?).";
                        StopAutoSnapshot();
                        return;
                    case CaptureOutcome.Cancelled:
                        // A genuine user cancel already cancelled the loop token, so we break
                        // at the top of the loop before reaching here; a disconnect now
                        // returns Failed (above), not Cancelled. StopAutoSnapshot is
                        // idempotent — call it defensively so NO Cancelled path can leave the
                        // loop wedged with AutoSnapshotEnabled stuck on. (M7)
                        StopAutoSnapshot();
                        return;
                }

                // Success / Partial — a snapshot was written.
                captured++;
                long lastBytes = NewestSnapshotBytes();

                // Auto-grow the quota FIRST (so the byte eviction below won't drop
                // below the desired retention), then enforce byte quota, then count.
                if (AutoSnapshotAdjustQuota && QuotaBytes > 0 && _gate != null)
                    ApplyAutoQuota(AutoSnapshotPlanner.RaiseQuotaBytes(QuotaBytes, AutoSnapshotCount, lastBytes));
                await Task.Run(() => _store.EnforceQuotaAsync(QuotaBytes, ct), ct);
                if (RetentionMode == AutoSnapshotRetentionMode.KeepRecent)
                    await Task.Run(() => _store.EnforceCountAsync(AutoSnapshotCount, ct), ct);
                await RefreshAsync();
                await UpdateUsageAsync();

                // Stop conditions: reached the fixed count, or the quota can only hold
                // one snapshot while more are wanted (adjust off).
                var stop = AutoSnapshotPlanner.EvaluatePostCapture(
                    RetentionMode, AutoSnapshotCount, captured, lastBytes, QuotaBytes, AutoSnapshotAdjustQuota);
                if (stop == AutoStopReason.ReachedCount)
                {
                    AutoStatusText = $"Auto done: captured {captured} snapshot(s).";
                    StopAutoSnapshot();
                    return;
                }
                if (stop == AutoStopReason.QuotaHoldsOne)
                {
                    AutoStatusText = "Auto stopped: quota only holds one snapshot — raise the quota or enable Auto-adjust.";
                    StopAutoSnapshot();
                    return;
                }

                // The capture may have consumed the remaining headroom — re-check disk
                // before committing to a long wait.
                if (!DiskGuardPasses())
                {
                    AutoStatusText = "Auto stopped: low disk space.";
                    StopAutoSnapshot();
                    return;
                }

                // Wait the computed gap (auto-extends past a long capture; ≥ 60 s idle)
                // with a 1 s countdown so the status line stays alive.
                int gap = AutoSnapshotPlanner.NextGapSeconds(
                    AutoSnapshotIntervalSec, sw.Elapsed.TotalSeconds, AutoSnapshotMinIntervalSec);
                for (int rem = gap; rem > 0 && !ct.IsCancellationRequested; rem--)
                {
                    AutoStatusText = $"Auto: next snapshot in {rem}s  ·  captured {captured}";
                    await Task.Delay(1000, ct);
                }
            }
        }
        catch (OperationCanceledException) { /* stopped */ }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Snapshot: auto loop error", ex);
            AutoStatusText = "Auto stopped: error.";
            StopAutoSnapshot();
        }
    }

    private CancellationTokenSource? _estimateCts;

    // How the size pre-flight samples: SampleWindows windows of SampleWindowSize
    // objects each, spread evenly across the GObjects index space so the estimate
    // isn't biased by the low-index engine-class cluster (which game_only / noise
    // skip mostly drops). Walked but NEVER written to the DB.
    private const int SampleWindows = 5;
    private const int SampleWindowSize = 4096;

    /// <summary>Pre-flight size estimate: walk a spread-out SAMPLE of objects with the
    /// CURRENT capture options (never writing the DB), model each row's stored bytes,
    /// and extrapolate to the full object count — so the user gets a "~X GB" go/no-go
    /// before committing to a multi-GB capture.</summary>
    [RelayCommand]
    private async Task EstimateSizeAsync()
    {
        if (!CanCapture) return;
        ClearError();
        var dataType = SelectedScope;
        bool gameOnly      = GameOnly;
        bool autoSkipNoise = AutoSkipNoise;
        bool includeNative = IncludeNativeFields;
        string family      = FamilyWire;
        long  capBytes     = MaxDatasetBytes;

        _estimateCts = new CancellationTokenSource();
        var ct = _estimateCts.Token;
        IsEstimating = true;
        EstimateText = "Estimating…";
        try
        {
            int total = await _dump.BeginSnapshotAsync(dataType, ct);
            if (total <= 0) { EstimateText = "No objects to capture."; return; }

            long sampledBytes = 0, sampledObjects = 0;
            int windows = Math.Min(SampleWindows, Math.Max(1, total / SampleWindowSize + 1));
            for (int i = 0; i < windows; i++)
            {
                ct.ThrowIfCancellationRequested();
                // Spread the windows across [0, total): i/windows of the way in.
                int offset = (int)((long)total * i / windows);
                var chunk = await _dump.SnapshotChunkAsync(
                    dataType, gameOnly, offset, SampleWindowSize,
                    includeNative, autoSkipNoise, family, ct);
                sampledBytes   += SnapshotSizeEstimate.EstimateChunkBytes(chunk.Objects);
                sampledObjects += chunk.Scanned;
                EstimateText = $"Estimating… sampled {sampledObjects:N0} objects";
            }

            if (sampledObjects <= 0) { EstimateText = "Sample captured no fields — try a wider scope."; return; }

            long est = SnapshotSizeEstimate.Extrapolate(sampledBytes, sampledObjects, total);
            string warn = capBytes > 0 && est > capBytes
                ? $"  ⚠ exceeds the {SnapshotFormat.Bytes(capBytes)} cap — capture would stop early"
                : "";
            EstimateText =
                $"Estimated ~{SnapshotFormat.Bytes(est)} for {total:N0} objects " +
                $"(sampled {sampledObjects:N0}).{warn}";
            _log.Info(Constants.LogCatView,
                $"Snapshot estimate: ~{SnapshotFormat.Bytes(est)} for {total:N0} objects " +
                $"(sampled {sampledObjects:N0} -> {SnapshotFormat.Bytes(sampledBytes)}; " +
                $"scope={dataType} family={family} gameOnly={gameOnly} noise={autoSkipNoise} native={includeNative})");
        }
        // Only a genuine user cancel (our token) reads as "cancelled"; a bare
        // disconnect-OCE (token NOT cancelled) falls through to "failed". (L15)
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { EstimateText = "Estimate cancelled."; }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Snapshot: size estimate failed", ex);
            EstimateText = "Estimate failed.";
        }
        finally
        {
            IsEstimating = false;
            _estimateCts?.Dispose();
            _estimateCts = null;
        }
    }

    /// <summary>Reveal the active game's snapshot DB in the OS file browser.</summary>
    [RelayCommand]
    private async Task OpenDbFolderAsync()
    {
        if (_platform == null) return;
        try { await _platform.RevealInExplorerAsync(_store.DatabasePath); }
        catch (Exception ex) { _log.Error(Constants.LogCatView, "Snapshot: open DB folder failed", ex); }
    }

    /// <summary>Cancel any in-flight heavy diff query — called when the user
    /// switches away from the Snapshot tab so a big in-memory diff doesn't keep
    /// burning CPU while another tab competes (the root cause of the tab-switch
    /// UI hang). The streaming capture op is deliberately NOT cancelled here —
    /// it yields between chunks and the user shouldn't silently lose a capture.</summary>
    public void CancelPendingWork()
    {
        _diffCts?.Cancel();
        // A group match over a big snapshot is the same heavy in-memory op (the whole
        // snapshot, off the UI thread) — abort it on tab-switch / window-close too.
        _groupCts?.Cancel();
    }

    [RelayCommand]
    private async Task RunDiffAsync()
    {
        if (!CanRunDiff) return;
        // Cancel a prior in-flight diff before starting a new one.
        _diffCts?.Cancel();
        _diffCts?.Dispose();
        var cts = _diffCts = new CancellationTokenSource();
        var ct = cts.Token;

        ClearError();
        IsDiffing = true;
        DiffStatusText = "Running diff… (large snapshots can take a while)";
        try
        {
            // Load the full changed set (capped); the filter boxes narrow it
            // client-side afterward so typing is instant. N1: hand the per-game
            // denylist to the store so denied classes are filtered before the cap.
            var filter = new SnapshotDiffFilter
            {
                ExcludedClasses = _excludedClasses.Count > 0 ? _excludedClasses : null,
            };
            // Auto-swap if the user picked Old newer than New: snapshot Id is
            // monotonic (= capture order), so the lower Id is always the older one.
            // Diffing with A older than B keeps the Increased/Decreased directions
            // meaningful instead of silently inverted.
            long idA = DiffA!.Id, idB = DiffB!.Id;
            bool swapped = false;
            if (idA > idB) { (idA, idB) = (idB, idA); swapped = true; }
            var diff = await Task.Run(() => _store.DiffSnapshotsAsync(idA, idB, filter, ct), ct);
            _allDiff.Clear();
            _allDiff.AddRange(diff.Changed);
            RebuildDiffOptions();
            RebuildNoiseRows(diff.TopContributors);
            var trunc = diff.Truncated ? $" (capped at {filter.MaxRows:N0})" : "";
            var swapNote = swapped ? "  ·  (auto-swapped Old/New to capture order)" : "";
            _diffSummary =
                $"{diff.Changed.Count:N0} changed{trunc}  ·  +{diff.AddedCount:N0} added  ·  −{diff.RemovedCount:N0} removed{swapNote}";
            ApplyDiffFilter();
        }
        catch (OperationCanceledException)
        {
            DiffStatusText = "Diff cancelled.";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Snapshot: diff failed", ex);
            SetError(ex);
        }
        finally
        {
            // Only clear the busy flag if THIS run is still the current one (a
            // newer run may have superseded us after cancellation).
            if (ReferenceEquals(_diffCts, cts))
            {
                IsDiffing = false;
                _diffCts.Dispose();
                _diffCts = null;
            }
        }
    }

    /// <summary>Open the selected diff row's object in the Live Walker tab.</summary>
    [RelayCommand]
    private void OpenInLiveWalker(SnapshotDiffRow? row)
    {
        // Stale-session gating is enforced on the button (IsEnabled =
        // CanUseDiffRowActions); the command itself stays guard-free for testability.
        if (row == null || string.IsNullOrEmpty(row.ObjAddr)) return;
        NavigateToInstance?.Invoke(row.ObjAddr);
    }

    /// <summary>Locate this diff row's owning object in the GWorld graph (reach
    /// mode — land on it, scroll to the changed field via PropName, which may be a
    /// deep container path). Only valid for an in-session diff (ObjAddr is the
    /// session-local address from snapshot B); a cross-session address fails the
    /// path search gracefully.</summary>
    [RelayCommand]
    private void LocateRowInGWorld(SnapshotDiffRow? row)
    {
        if (row == null || string.IsNullOrEmpty(row.ObjAddr)) return;
        LocateInGWorld?.Invoke(row.ObjAddr, row.PropOffset, row.PropName);
    }

    /// <summary>Engine-rooted counterpart — not gated on IsGWorldAvailable (engine
    /// availability is independent of GWorld; the DLL reports no_engine via the banner).</summary>
    [RelayCommand]
    private void LocateRowInGameEngine(SnapshotDiffRow? row)
    {
        if (row == null || string.IsNullOrEmpty(row.ObjAddr)) return;
        LocateInGameEngine?.Invoke(row.ObjAddr, row.PropOffset, row.PropName);
    }

    /// <summary>Copy the changed field's live address (obj_addr + offset) to the
    /// clipboard — a quick handoff into CE. Valid for an in-session diff.</summary>
    [RelayCommand]
    private async Task CopyDiffAddressAsync(SnapshotDiffRow? row)
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
                DiffStatusText = $"Copied {addr:X}  ({row.ClassName}::{row.PropName})";
            }
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Snapshot: copy address failed", ex);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(SnapshotMeta? meta)
    {
        if (meta == null || IsCapturing || IsDeleting || IsDiffing) return;
        IsDeleting = true;
        StatusText = "Deleting snapshot…";
        try
        {
            // DELETE over ~1.7M field rows + ExecuteNonQueryAsync runs synchronously
            // under Microsoft.Data.Sqlite, so run it off the UI thread to keep the
            // window responsive.
            await Task.Run(() => _store.DeleteSnapshotAsync(meta.Id));
            // Detach any selection pointing at the row before removing it.
            if (ReferenceEquals(SelectedSnapshot, meta)) SelectedSnapshot = null;
            if (ReferenceEquals(DiffA, meta)) DiffA = null;
            if (ReferenceEquals(DiffB, meta)) DiffB = null;
            Snapshots.Remove(meta);
            await UpdateUsageAsync();
            StatusText = "Snapshot deleted.";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Snapshot: delete failed", ex);
            SetError(ex);
        }
        finally
        {
            IsDeleting = false;
        }
    }

    /// <summary>Delete EVERY snapshot for the active game (truncate + VACUUM).
    /// Off the UI thread; guarded against running during capture/delete.</summary>
    [RelayCommand]
    private async Task DeleteAllAsync()
    {
        if (IsCapturing || IsDeleting || IsDiffing || Snapshots.Count == 0) return;
        IsDeleting = true;
        StatusText = "Deleting all snapshots…";
        try
        {
            await Task.Run(() => _store.DeleteAllSnapshotsAsync());
            SelectedSnapshot = null; DiffA = null; DiffB = null;
            Snapshots.Clear();
            await UpdateUsageAsync();
            StatusText = "All snapshots deleted.";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Snapshot: delete-all failed", ex);
            SetError(ex);
        }
        finally
        {
            IsDeleting = false;
        }
    }

    private static string DefaultLabel()
        => $"Snapshot {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

    /// <summary>Compact human duration: "8s" / "1m23s" / "1h02m05s".</summary>
    private static string FmtSpan(TimeSpan t) =>
        t.TotalHours >= 1   ? $"{(int)t.TotalHours}h{t.Minutes:D2}m{t.Seconds:D2}s"
        : t.TotalMinutes >= 1 ? $"{t.Minutes}m{t.Seconds:D2}s"
        : $"{t.Seconds}s";

    // --- N1: noise picker helpers (shared shape with SpcQueryViewModel) ---

    private void RebuildNoiseRows(IReadOnlyList<ClassNoiseRow> top)
    {
        NoiseRows.Clear();
        foreach (var c in top)
        {
            NoiseRows.Add(new NoiseRowVm
            {
                ClassName          = c.ClassName,
                HitCount           = c.HitCount,
                SamplePropsDisplay = c.SamplePropsDisplay,
            });
        }
    }

    [RelayCommand]
    private async Task ApplyNoisePicksAsync()
    {
        // Fresh set, not in-place mutation — an in-flight RunDiffAsync (background
        // thread) may still be reading the captured reference via deny.Contains();
        // HashSet isn't safe for concurrent read+write. See SpcQueryViewModel for
        // the full rationale.
        var updated = new HashSet<string>(_excludedClasses, StringComparer.Ordinal);
        bool changed = false;
        foreach (var row in NoiseRows)
            if (row.Picked && updated.Add(row.ClassName)) changed = true;
        if (!changed)
        {
            DiffStatusText = "No noise classes picked — tick one or more rows first.";
            return;
        }
        _store.SetClassDenylist(DenylistScope.Diff, updated);
        LoadDenylistFromStore();
        await RunDiffAsync();
    }

    /// <summary>Untick all noise-picker rows (without touching the persisted
    /// denylist). Distinct from Clear all, which empties the denylist.</summary>
    [RelayCommand]
    private void ResetNoisePicks()
    {
        foreach (var row in NoiseRows) row.Picked = false;
    }

    [RelayCommand]
    private async Task RemoveFromDenylistAsync(string? className)
    {
        if (string.IsNullOrEmpty(className) || !_excludedClasses.Contains(className)) return;
        var updated = new HashSet<string>(_excludedClasses, StringComparer.Ordinal);
        updated.Remove(className);
        _store.SetClassDenylist(DenylistScope.Diff, updated);
        LoadDenylistFromStore();
        await RunDiffAsync();
    }

    [RelayCommand]
    private async Task ClearDenylistAsync()
    {
        if (_excludedClasses.Count == 0) return;
        _store.SetClassDenylist(DenylistScope.Diff, new HashSet<string>(StringComparer.Ordinal));
        LoadDenylistFromStore();
        await RunDiffAsync();
    }
}
