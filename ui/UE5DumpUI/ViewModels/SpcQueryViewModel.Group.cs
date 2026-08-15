using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// SPC GROUP mode — the Single / Group toggle of the SPC Query tab. Finds OBJECTS in
/// the captured-snapshot corpus that hold ALL of N (2–4) fields, each satisfying its
/// OWN per-snapshot predicate CHAIN across the selected snapshots, at distinct
/// offsets. The N-snapshot, object-aware generalisation of Snapshot Group Match Mode
/// B, via <see cref="ISnapshotStore.SpcGroupQueryAsync"/> (SPC intersection +
/// <see cref="Services.GroupMatch"/> SDR). Reuses the snapshot picker (the matrix is
/// stored per pick, edited one slot-column at a time via <see cref="ActiveGroupSlot"/>)
/// and the shared GroupCandidate / GroupSlotMatch master-detail. See
/// docs/group-value-scan-spec.md §3.1.
/// </summary>
public partial class SpcQueryViewModel
{
    private CancellationTokenSource? _groupCts;

    /// <summary>Single / Group mode toggle. Off = single-value SPC (default).</summary>
    [ObservableProperty] private bool _isGroupMode;
    [ObservableProperty] private bool _isGroupMatching;
    [ObservableProperty] private string _groupStatusText = "";
    [ObservableProperty] private GroupCandidate? _selectedGroupCandidate;

    /// <summary>Which value-slot's column the picker grid currently edits (0-based).
    /// The slot selector binds a ComboBox SelectedIndex to this.</summary>
    [ObservableProperty] private int _activeGroupSlot;

    /// <summary>The 2–4 value-slots (each: a width scope; the per-snapshot chain lives
    /// on the picks).</summary>
    public ObservableCollection<SpcGroupSlotVm> GroupSlots { get; } = new()
    {
        new SpcGroupSlotVm(1),
        new SpcGroupSlotVm(2),
    };

    public ObservableCollection<GroupCandidate> GroupCandidates { get; } = new();

    /// <summary>Slot selector labels ("Value 1".."Value N").</summary>
    public ObservableCollection<string> ActiveSlotChoices { get; } = new() { "Value 1", "Value 2" };

    /// <summary>Per-slot width scope (multi-numeric meta types only).</summary>
    public IReadOnlyList<ValueScanDataType> GroupScopeOptions { get; } =
        new[] { ValueScanDataType.NumericNoByte, ValueScanDataType.NumericAll };

    public bool CanAddGroupSlot    => GroupSlots.Count < 4;
    public bool CanRemoveGroupSlot => GroupSlots.Count > 2;

    /// <summary>At least two snapshots picked, 2–4 slots, and not mid-match.</summary>
    public bool CanRunGroupQuery => SelectedCount >= 2 && GroupSlots.Count is >= 2 and <= 4 && !IsGroupMatching;

    partial void OnIsGroupMatchingChanged(bool value) => OnPropertyChanged(nameof(CanRunGroupQuery));

    partial void OnIsGroupModeChanged(bool value)
    {
        // Cancel the OTHER mode's in-flight op; re-point the picker facades on entry.
        if (value) { _queryCts?.Cancel(); ApplyActiveGroupSlotToPicks(); }
        else       { _groupCts?.Cancel(); }
    }

    partial void OnActiveGroupSlotChanged(int value)
    {
        ApplyActiveGroupSlotToPicks();
        OnPropertyChanged(nameof(EditingSlotLabel));
    }

    /// <summary>Prominent "Now editing: Value N" label beside the Values header, so it's
    /// obvious which slot's column the snapshot grid below is currently editing.</summary>
    public string EditingSlotLabel => $"Now editing: Value {ActiveGroupSlot + 1}";

    /// <summary>Re-point every pick's active-slot facade at the current slot.</summary>
    internal void ApplyActiveGroupSlotToPicks()
    {
        foreach (var p in SnapshotPicks) p.SetActiveGroupSlot(ActiveGroupSlot);
    }

    [RelayCommand]
    private void AddGroupSlot()
    {
        if (!CanAddGroupSlot) return;
        GroupSlots.Add(new SpcGroupSlotVm(GroupSlots.Count + 1));
        ActiveSlotChoices.Add($"Value {GroupSlots.Count}");
        RaiseGroupSlotGates();
    }

    [RelayCommand]
    private void RemoveGroupSlot()
    {
        if (!CanRemoveGroupSlot) return;
        // Move selection off the slot being removed FIRST, so the bound ComboBox's
        // selected-item-removed path can't transiently force SelectedIndex (and thus
        // ActiveGroupSlot) to -1. CanRemoveGroupSlot ⇒ Count ≥ 3, so Count-2 ≥ 1.
        if (ActiveGroupSlot >= GroupSlots.Count - 1) ActiveGroupSlot = GroupSlots.Count - 2;
        GroupSlots.RemoveAt(GroupSlots.Count - 1);
        ActiveSlotChoices.RemoveAt(ActiveSlotChoices.Count - 1);
        RaiseGroupSlotGates();
    }

    private void RaiseGroupSlotGates()
    {
        OnPropertyChanged(nameof(CanAddGroupSlot));
        OnPropertyChanged(nameof(CanRemoveGroupSlot));
        OnPropertyChanged(nameof(CanRunGroupQuery));
    }

    [RelayCommand]
    private void ClearGroup()
    {
        SelectedGroupCandidate = null;
        GroupCandidates.Clear();
        GroupStatusText = "";
    }

    [RelayCommand]
    private async Task RunGroupQueryAsync()
    {
        var selected = SnapshotPicks.Where(p => p.IsSelected).OrderBy(p => p.Id).ToList();
        if (selected.Count < 2)      { GroupStatusText = "Select at least two snapshots."; return; }
        if (GroupSlots.Count is < 2 or > 4) { GroupStatusText = "A group needs 2-4 values."; return; }

        _groupCts?.Cancel();
        _groupCts?.Dispose();
        var cts = _groupCts = new CancellationTokenSource();
        var ct = cts.Token;

        ClearError();
        IsGroupMatching = true;
        GroupStatusText = "Matching… (intersecting fields, then grouping per object)";
        SelectedGroupCandidate = null;   // detach before clearing the bound results grid
        GroupCandidates.Clear();
        try
        {
            var query = new SpcGroupQuery
            {
                JoinMode      = ParseJoinMode(SelectedJoinMode),
                ClassContains = ClassFilter.Trim(),
                PropContains  = PropFilter.Trim(),
                RoundMode     = SelectedRoundingMode,
                ExcludedClasses = _excludedClasses.Count > 0 ? _excludedClasses : null,
            };
            foreach (var p in selected) query.SnapshotIds.Add(p.Id);
            for (int s = 0; s < GroupSlots.Count; s++)
            {
                var slot = new SpcGroupSlot { Scope = GroupSlots[s].Scope };
                for (int i = 0; i < selected.Count; i++)
                {
                    var pick = selected[i];
                    slot.Chain.Add(new SpcGroupCell
                    {
                        // Index 0 is the baseline (no predecessor) → forced Any.
                        Predicate = i == 0 ? SpcPredicateKind.Any : ParsePredicate(pick.GroupPredicateOf(s)),
                        Absolute  = pick.ToGroupAbsolutePredicate(s),
                    });
                }
                query.Slots.Add(slot);
            }

            // Off the UI thread — the intersection over ~1.8M rows would freeze the window.
            var res = await Task.Run(() => _store.SpcGroupQueryAsync(query, ct), ct);
            if (!string.IsNullOrEmpty(res.Error)) { GroupStatusText = res.Error!; return; }

            foreach (var c in res.Candidates) GroupCandidates.Add(c);
            var trunc = res.Truncated ? $" (showing first {GroupCandidates.Count:N0})" : "";
            var spanSessions = selected.Select(p => p.Meta.GameSessionId).Distinct().Count();
            var sess = spanSessions > 1 ? $", {spanSessions} sessions" : "";
            GroupStatusText = res.Total == 0
                ? $"No object holds all {GroupSlots.Count} values across {selected.Count} snapshots{sess}."
                : $"{res.Total:N0} object(s) matched{trunc}  ·  scanned {res.ScannedObjects:N0} ({SelectedJoinMode}).";
        }
        catch (OperationCanceledException)
        {
            GroupStatusText = "Match cancelled.";
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatView, "SPC: group query failed", ex);
            SetError(ex);
            GroupStatusText = "Query failed.";
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

    // ---- per-row / per-slot handoffs (reuse the events + the single-mode session gates) ----

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
            _log.Error(Constants.LogCatView, "SPC: copy group slot address failed", ex);
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

/// <summary>One SPC group value-slot in the input strip: a number + a width scope.
/// The per-snapshot chain for the slot lives on each <see cref="SpcSnapshotPick"/>
/// (edited via the active-slot facades).</summary>
public partial class SpcGroupSlotVm : ObservableObject
{
    public SpcGroupSlotVm(int number) { Number = number; }
    public int    Number { get; }
    public string Label  => $"Value {Number}";
    [ObservableProperty] private ValueScanDataType _scope = ValueScanDataType.NumericNoByte;
}
