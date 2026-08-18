using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the "Related Objects" panel. Given a UObject (typically an
/// actor/pawn the user is inspecting — handed off from Instance Finder / Value
/// Search / Live Walker, or pasted manually), show the objects most useful for
/// understanding it: itself, its class/outer, its Controller↔Pawn counterpart,
/// and the sub-objects it OWNS (components, and for GAS games the
/// AbilitySystemComponent → its AttributeSets). Each row hands off to Locate in
/// GWorld / Open in Live Walker / Find Class — the same cross-tab event pattern
/// the other panels use.
/// </summary>
public partial class RelatedObjectsViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService _platform;

    [ObservableProperty] private string _targetAddress = "";
    [ObservableProperty] private string _queryClassName = "";
    [ObservableProperty] private ObservableCollection<RelatedObject> _related = new();
    [ObservableProperty] private RelatedObject? _selectedRelated;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";

    // --- Phase 2: current-target auto-detect (Edel) ---
    [ObservableProperty] private ObservableCollection<TargetCandidate> _targetCandidates = new();
    [ObservableProperty] private TargetCandidate? _selectedCandidate;
    [ObservableProperty] private bool _hasCandidates;

    // Set while DetectTarget seeds SelectedCandidate to the auto-pick, so the
    // selection-changed handler doesn't double-load what DetectTarget already loaded.
    private bool _suppressCandidateLoad;

    /// <summary>Navigate the selected row's object into the Live Walker.</summary>
    public event Action<string>? NavigateToLiveWalker;

    /// <summary>Locate the selected row's object within the GWorld graph
    /// (forward path search). Payload = object address.</summary>
    public event Action<string>? LocateInGWorld;

    /// <summary>Locate the selected row's object within the GameEngine graph
    /// (forward path search rooted at the live UGameEngine). Payload = object
    /// address. Complements <see cref="LocateInGWorld"/> — reaches engine-layer
    /// objects the GWorld graph never touches.</summary>
    public event Action<string>? LocateInGameEngine;

    /// <summary>Find all instances of the selected row's class. Payload = class name.</summary>
    public event Action<string>? NavigateToInstanceFinder;

    public RelatedObjectsViewModel(IDumpService dump, ILoggingService log, IPlatformService platform)
    {
        _dump = dump;
        _log = log;
        _platform = platform;
    }

    /// <summary>Drop the related-objects graph + detected-target candidates so a
    /// reconnect never shows (or navigates to) addresses from the previous game
    /// (audit X5). Client-side only. <see cref="_suppressCandidateLoad"/> gates the
    /// selection-changed pipe walk while we null the selection.</summary>
    public void ClearOnDisconnect()
    {
        _suppressCandidateLoad = true;
        try
        {
            SelectedCandidate = null;   // its changed-handler would otherwise walk the pipe
            SelectedRelated = null;
            Related.Clear();
            TargetCandidates.Clear();
            HasCandidates = false;
            TargetAddress = "";
            QueryClassName = "";
            StatusText = "";
        }
        finally { _suppressCandidateLoad = false; }
    }

    /// <summary>Handoff entry point from another panel: set the address and load.</summary>
    public async Task LoadForAddressAsync(string addr)
    {
        TargetAddress = addr;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var addr = (TargetAddress ?? "").Trim();
        if (string.IsNullOrEmpty(addr))
        {
            StatusText = "Enter or hand off a UObject address.";
            return;
        }
        try
        {
            IsBusy = true;
            StatusText = "Scanning related objects…";
            SelectedRelated = null;   // detach before rebuilding the selection-bound grid
            Related.Clear();
            QueryClassName = "";
            var result = await _dump.GetRelatedObjectsAsync(addr);
            foreach (var r in result.Related)
            {
                Related.Add(r);
                // The "Self" row carries the queried object's own class — surface
                // it in the header so the user knows what they're looking at.
                if (QueryClassName.Length == 0 && r.Relation == "Self")
                    QueryClassName = r.ClassName;
            }
            StatusText = Related.Count > 0
                ? $"{Related.Count} related object(s)."
                : "No related objects found (is the address a live UObject?).";
            _log.Info($"GetRelatedObjects: {addr} -> {Related.Count}");
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _log.Error($"GetRelatedObjects failed for {addr}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenInLiveWalker(RelatedObject? row)
    {
        row ??= SelectedRelated;
        if (row == null || string.IsNullOrEmpty(row.Address)) return;
        NavigateToLiveWalker?.Invoke(row.Address);
    }

    // "Locate in GWorld" is intentionally NOT gated on a C# GWorld-available
    // flag: the DLL's find_path is the source of truth, and gating the button on
    // a stale C# flag previously produced a silent no-op (build 1311). The
    // LiveWalker handler reports availability via its own StatusText.
    [RelayCommand]
    private void LocateRowInGWorld(RelatedObject? row)
    {
        row ??= SelectedRelated;
        if (row == null || string.IsNullOrEmpty(row.Address)) return;
        LocateInGWorld?.Invoke(row.Address);
    }

    // Same as LocateRowInGWorld but roots the path search at the live UGameEngine.
    // Not gated on a C# availability flag — the DLL's find_path is the source of
    // truth and reports availability via the LiveWalker StatusText (see above).
    [RelayCommand]
    private void LocateRowInGameEngine(RelatedObject? row)
    {
        row ??= SelectedRelated;
        if (row == null || string.IsNullOrEmpty(row.Address)) return;
        LocateInGameEngine?.Invoke(row.Address);
    }

    [RelayCommand]
    private void FindClass(RelatedObject? row)
    {
        row ??= SelectedRelated;
        if (row == null || string.IsNullOrEmpty(row.ClassName)) return;
        NavigateToInstanceFinder?.Invoke(row.ClassName);
    }

    [RelayCommand]
    private async Task CopyAddress(RelatedObject? row)
    {
        row ??= SelectedRelated;
        if (row == null || string.IsNullOrEmpty(row.Address)) return;
        await _platform.CopyToClipboardAsync(row.Address);
        StatusText = $"Copied {row.Address}";
    }

    /// <summary>
    /// Auto-detect the actor the local player is currently targeting (Edel) and
    /// load the top candidate into the related graph. Falls back gracefully:
    /// when nothing clearly looks like a target, the candidates are shown as
    /// greyed guesses and nothing is auto-loaded — the user picks one or pastes
    /// an address. Solves "I don't know which class-name to search".
    /// </summary>
    [RelayCommand]
    private async Task DetectTargetAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = "Detecting current target…";
            SelectedCandidate = null;   // detach before rebuilding the selection-bound list
            TargetCandidates.Clear();
            HasCandidates = false;
            var r = await _dump.DetectCurrentTargetAsync();
            foreach (var c in r.Candidates) TargetCandidates.Add(c);
            HasCandidates = TargetCandidates.Count > 0;
            StatusText = r.Note;
            _log.Info($"DetectCurrentTarget: resolved={r.Resolved} candidates={TargetCandidates.Count}");

            // Only auto-load when the detector is confident (top score > 0);
            // weak/zero-score guesses are shown but NOT auto-loaded.
            if (r.Resolved && TargetCandidates.Count > 0)
            {
                _suppressCandidateLoad = true;
                SelectedCandidate = TargetCandidates[0];
                _suppressCandidateLoad = false;
                await LoadForAddressAsync(TargetCandidates[0].Address);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _log.Error("DetectCurrentTarget failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>User picked a candidate in the list — load it into the graph
    /// (skip the auto-pick already loaded by DetectTarget, and re-picks of the
    /// currently-loaded address).</summary>
    partial void OnSelectedCandidateChanged(TargetCandidate? value)
    {
        if (_suppressCandidateLoad || value == null || string.IsNullOrEmpty(value.Address)) return;
        if (string.Equals(value.Address, TargetAddress, System.StringComparison.OrdinalIgnoreCase)) return;
        _ = LoadForAddressAsync(value.Address);
    }
}
