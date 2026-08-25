using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// Snapshot Group Match (S3 UI) — the "Group" mode of the Snapshot tab. Finds
/// objects in a captured snapshot that hold ALL of N values (2-4) at distinct
/// numeric offsets, via the C# Orden port (Services.GroupMatch) behind
/// ISnapshotStore.GroupMatchAsync. S3 wires Mode A (single-snapshot absolute);
/// Mode B (cross-snapshot, relative predicates) follows in S4.
/// See docs/snapshot-group-match-spec.md.
/// </summary>
public partial class SnapshotViewModel
{
    private CancellationTokenSource? _groupCts;

    /// <summary>Diff / Group mode toggle for the lower results area. Off = Diff (default).</summary>
    [ObservableProperty] private bool _isGroupMode;

    /// <summary>The primary snapshot to search — the newest in Mode B.</summary>
    [ObservableProperty] private SnapshotMeta? _groupSnapshot;

    /// <summary>Optional OLDER snapshot to compare against (Mode B). Null / same as
    /// <see cref="GroupSnapshot"/> = Mode A (single-snapshot absolute). When set and
    /// distinct, each leaf carries its value across both snapshots so the relative
    /// predicates (Changed / Unchanged / Increased / Decreased) work — e.g. "Current
    /// HP decreased + Max HP unchanged".</summary>
    [ObservableProperty] private SnapshotMeta? _groupCompareSnapshot;

    /// <summary>True when a distinct compare snapshot is picked (Mode B).</summary>
    public bool IsGroupCompareMode =>
        GroupCompareSnapshot != null && GroupSnapshot != null && GroupCompareSnapshot.Id != GroupSnapshot.Id;

    partial void OnGroupCompareSnapshotChanged(SnapshotMeta? value)
    {
        OnPropertyChanged(nameof(IsGroupCompareMode));
        // The result rows' obj_addr come from the NEWER of the two snapshots, so the
        // session-validity gate must re-evaluate when the compare pick changes.
        OnPropertyChanged(nameof(CanUseGroupRowActions));
        OnPropertyChanged(nameof(CanLocateGroupRowInGWorld));
    }

    [ObservableProperty] private bool _isGroupMatching;

    /// <summary>Deep (opt-in, default off): also match nested container / struct-array
    /// element values (the captured `array_field` rows, e.g. a deeply-nested
    /// `…Tunes[2]`), folded into the owning object's block. Mirrors the live Group
    /// Scan's Deep toggle. Off = the object's direct fields only.</summary>
    [ObservableProperty] private bool _groupDeep;

    /// <summary>Per-panel rounding mode (Round/Trunc/Ceil): how a fractional
    /// float/double target is reduced to the displayed integer before compare. Shared
    /// by the single-value match (which also funnels through GroupMatchAsync) and the
    /// group match. Persisted; default <see cref="FloatRoundMode.Round"/>.</summary>
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
        // Group rows render their own live Between preview from this mode (display-only;
        // the actual match still uses the panel-level mode).
        foreach (var row in GroupInputs) row.RoundMode = value;
    }

    [ObservableProperty] private string _groupStatusText = "";
    [ObservableProperty] private GroupCandidate? _selectedGroupCandidate;

    /// <summary>The 2-4 editable input rows (reuses the live group's row model).</summary>
    public ObservableCollection<GroupSlotInput> GroupInputs { get; } = new()
    {
        new GroupSlotInput(),
        new GroupSlotInput(),
    };

    public ObservableCollection<GroupCandidate> GroupCandidates { get; } = new();

    /// <summary>Per-slot width scope (multi-numeric meta types only).</summary>
    public IReadOnlyList<ValueScanDataType> GroupDataTypeOptions { get; } =
        new[] { ValueScanDataType.NumericNoByte, ValueScanDataType.NumericAll };

    /// <summary>Per-slot predicates. Absolute (Exact/Bigger/Smaller/Between) work in
    /// both modes; the relative four (Changed/Unchanged/Increased/Decreased) need a
    /// "Compare with" snapshot (Mode B) — the store rejects them with a clear message
    /// otherwise, so they're always offered.</summary>
    public IReadOnlyList<ValueScanType> GroupScanTypeOptions { get; } = new[]
    {
        ValueScanType.Exact, ValueScanType.Bigger, ValueScanType.Smaller, ValueScanType.Between,
        ValueScanType.Changed, ValueScanType.Unchanged, ValueScanType.Increased, ValueScanType.Decreased,
    };

    public bool CanAddGroupRow    => GroupInputs.Count < 4;
    public bool CanRemoveGroupRow => GroupInputs.Count > 2;

    /// <summary>A snapshot is picked, 2-4 input rows, and no match running.</summary>
    public bool CanRunGroupMatch =>
        GroupSnapshot != null && GroupInputs.Count is >= 2 and <= 4 && !IsGroupMatching;

    /// <summary>The NEWER of the two selected snapshots — the one whose (current-session)
    /// obj_addr the result rows carry in Mode B (the store reads display fields from
    /// max(id)). Mode A = the single snapshot.</summary>
    private SnapshotMeta? NewestGroupSnapshot =>
        GroupCompareSnapshot == null ? GroupSnapshot
        : GroupSnapshot == null ? GroupCompareSnapshot
        : (GroupCompareSnapshot.Id > GroupSnapshot.Id ? GroupCompareSnapshot : GroupSnapshot);

    /// <summary>The group results' obj_addr is only live in the CURRENT session — gate
    /// the per-slot Live / Copy handoffs (a cross-session address is stale), mirroring
    /// the diff rows. Validates the NEWER snapshot (the address source), so a larger-id
    /// compare snapshot from a stale session can't be mis-gated as live.</summary>
    public bool CanUseGroupRowActions =>
        !string.IsNullOrEmpty(_currentSessionId) && NewestGroupSnapshot?.GameSessionId == _currentSessionId;

    // NOT gated on the client IsGWorldAvailable flag (audit #5 AE10).
    public bool CanLocateGroupRowInGWorld => CanUseGroupRowActions;

    partial void OnGroupSnapshotChanged(SnapshotMeta? value)
    {
        OnPropertyChanged(nameof(CanRunGroupMatch));
        OnPropertyChanged(nameof(CanUseGroupRowActions));
        OnPropertyChanged(nameof(CanLocateGroupRowInGWorld));
        OnPropertyChanged(nameof(IsGroupCompareMode));   // depends on both snapshots
    }

    partial void OnIsGroupMatchingChanged(bool value) => OnPropertyChanged(nameof(CanRunGroupMatch));

    partial void OnIsGroupModeChanged(bool value)
    {
        if (!value) return;
        // Default the group snapshot to the newest when first switching to Group mode.
        if (GroupSnapshot == null && Snapshots.Count > 0)
            GroupSnapshot = Snapshots[0];
        // With exactly two snapshots the "Compare with" target is unambiguous, so pre-fill
        // it (the OTHER snapshot) → Mode B is ready in one switch. With 1 (no compare) or
        // 3+ (ambiguous which to compare) leave it empty for the user to pick.
        if (GroupCompareSnapshot == null && Snapshots.Count == 2)
            GroupCompareSnapshot = Snapshots.FirstOrDefault(s => s.Id != GroupSnapshot?.Id);
    }

    [RelayCommand]
    private void AddGroupRow()
    {
        if (!CanAddGroupRow) return;
        GroupInputs.Add(new GroupSlotInput { RoundMode = SelectedRoundingMode });
        RaiseGroupRowGates();
    }

    [RelayCommand]
    private void RemoveGroupRow()
    {
        if (!CanRemoveGroupRow) return;
        GroupInputs.RemoveAt(GroupInputs.Count - 1);
        RaiseGroupRowGates();
    }

    private void RaiseGroupRowGates()
    {
        OnPropertyChanged(nameof(CanAddGroupRow));
        OnPropertyChanged(nameof(CanRemoveGroupRow));
        OnPropertyChanged(nameof(CanRunGroupMatch));
    }

    [RelayCommand]
    private void ClearGroup()
    {
        SelectedGroupCandidate = null;
        GroupCandidates.Clear();
        GroupStatusText = "";
    }

    /// <summary>Drop the compare snapshot — back to Mode A (single-snapshot absolute).</summary>
    [RelayCommand]
    private void ClearGroupCompare() => GroupCompareSnapshot = null;

    [RelayCommand]
    private async Task RunGroupMatchAsync()
    {
        if (!CanRunGroupMatch) return;
        _groupCts?.Cancel();
        _groupCts?.Dispose();
        var cts = _groupCts = new CancellationTokenSource();
        var ct = cts.Token;

        ClearError();
        IsGroupMatching = true;
        GroupStatusText = "Matching…";
        try
        {
            var query = new SnapshotGroupQuery
            {
                Deep = GroupDeep,
                RoundMode = SelectedRoundingMode,
                ExcludedClasses = _excludedClasses.Count > 0 ? _excludedClasses : null,
                Slots = GroupInputs.Select(g => new SnapshotGroupSlotInput
                {
                    DataType = g.DataType,
                    ScanType = g.ScanType,
                    Value = g.Value,
                    Value2 = g.Value2,
                    RoundMode = SelectedRoundingMode,
                }).ToList(),
            };
            query.SnapshotIds.Add(GroupSnapshot!.Id);
            if (GroupCompareSnapshot != null && GroupCompareSnapshot.Id != GroupSnapshot!.Id)
                query.SnapshotIds.Add(GroupCompareSnapshot.Id);   // Mode B (store orders oldest→newest)
            var res = await Task.Run(() => _store.GroupMatchAsync(query, ct), ct);

            SelectedGroupCandidate = null;
            GroupCandidates.Clear();
            if (!string.IsNullOrEmpty(res.Error))
            {
                GroupStatusText = res.Error!;
                return;
            }
            foreach (var c in res.Candidates) GroupCandidates.Add(c);
            var trunc = res.Truncated ? $" (showing first {GroupCandidates.Count:N0})" : "";
            // AF13: the DLL sibling has said this since AE13; the snapshot corpus
            // truncated a slot's witness list at the same cap and said nothing, so a
            // field that DID match read as a field that did not. Applies to the
            // no-match line too — a truncated slot is exactly when a miss may be the
            // cap rather than the answer.
            var capNote = res.PerSlotCapHit
                ? PartialResultNotice.PerSlotWitnessCap(res.PerSlotCap) : "";
            GroupStatusText = (res.Total == 0
                ? $"No objects hold all {GroupInputs.Count} values."
                : $"{res.Total:N0} object(s) matched{trunc}  ·  scanned {res.ScannedObjects:N0}")
                + capNote;
        }
        catch (OperationCanceledException)
        {
            GroupStatusText = "Match cancelled.";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Snapshot: group match failed", ex);
            SetError(ex);
        }
        finally
        {
            if (ReferenceEquals(_groupCts, cts))
            {
                IsGroupMatching = false;
                _groupCts.Dispose();
                _groupCts = null;
            }
        }
    }

    // ---- per-row / per-slot handoffs (same-session obj_addr, like the diff rows) ----

    /// <summary>Open the matched object in the Live Walker tab.</summary>
    [RelayCommand]
    private void OpenGroupInLiveWalker(GroupCandidate? cand)
    {
        if (cand == null || string.IsNullOrEmpty(cand.InstanceAddr)) return;
        NavigateToInstance?.Invoke(cand.InstanceAddr);
    }

    /// <summary>Copy a slot's leaf address (obj_addr + offset) to the clipboard.</summary>
    [RelayCommand]
    private async Task CopyGroupSlotAddressAsync(GroupSlotMatch? slot)
    {
        if (slot == null || _platform == null || string.IsNullOrEmpty(slot.Addr)) return;
        try
        {
            var hex = slot.Addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? slot.Addr.Substring(2) : slot.Addr;
            await _platform.CopyToClipboardAsync(hex);
            GroupStatusText = $"Copied {hex}  ({slot.ClassName}::{slot.FieldName})";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "Snapshot: copy group slot address failed", ex);
        }
    }

    /// <summary>Locate the slot's owning object in the GWorld graph (in-session only).</summary>
    [RelayCommand]
    private void LocateGroupSlotInGWorld(GroupSlotMatch? slot)
    {
        if (slot == null || string.IsNullOrEmpty(slot.InstanceAddr)) return;
        LocateInGWorld?.Invoke(slot.InstanceAddr, slot.FieldOffset, slot.FieldName);
    }

    /// <summary>Engine-rooted counterpart (not gated on GWorld availability).</summary>
    [RelayCommand]
    private void LocateGroupSlotInGameEngine(GroupSlotMatch? slot)
    {
        if (slot == null || string.IsNullOrEmpty(slot.InstanceAddr)) return;
        LocateInGameEngine?.Invoke(slot.InstanceAddr, slot.FieldOffset, slot.FieldName);
    }
}
