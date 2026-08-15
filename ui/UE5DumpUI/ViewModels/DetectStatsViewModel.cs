using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// Experimental "Detect Player Stats" panel (P4 — Layer 3 confirmation).
///
/// One button that runs the shipped structural scorer (P2a/P2b), takes the
/// top-scoring candidate stat fields, and CONFIRMS each against the live game:
///   - a live (non-CDO) instance of the owning class exists, and
///   - the field holds a plausible numeric value right now (or is a GAS attribute),
///   - plus the static signals the scorer already computes (Max sibling / GAS / flags),
///   - and — opt-in — the field DECREASED across the two most-recent snapshots
///     (capture one, take damage / spend gold, capture again).
///
/// The combined confidence ranks the list; the confident ones are marked
/// "✓ confirmed". This is a LOW-ACCURACY shortlist to verify, never a guarantee —
/// the panel shows a prominent disclaimer. Single-player only. Gated behind the
/// experimental opt-in (the tab is hidden unless ExperimentalEnabled).
/// </summary>
public partial class DetectStatsViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly ISnapshotStore? _snapshotStore;

    // Bounds so one "Detect" click can't fan out into thousands of pipe
    // round-trips: score a wide net, but only probe live instances for the
    // top classes.
    private const int MaxCandidates    = 80;   // scored fields kept before probing
    private const int MaxClassesProbed = 30;   // distinct classes we resolve+walk

    // Confirmation boosts folded onto the scorer's FinalScore.
    private const int LiveInstanceBonus      = 2;
    private const int ValuePlausibleBonus    = 3;
    private const int SnapshotDecreasedBonus = 3;
    /// <summary>Confidence at/above which a live+plausible candidate is "confirmed".</summary>
    private const int ConfirmThreshold = 9;

    private static readonly HashSet<string> NumericTypes = new(StringComparer.Ordinal)
    {
        "FloatProperty", "DoubleProperty",
        "IntProperty", "Int64Property", "Int16Property", "Int8Property",
        "UInt32Property", "UInt64Property", "UInt16Property", "ByteProperty",
    };

    /// <summary>Full detected set; <see cref="Results"/> is this filtered by
    /// <see cref="FilterText"/>.</summary>
    private List<DetectedStat> _allResults = new();

    [ObservableProperty] private ObservableCollection<DetectedStat> _results = new();
    [ObservableProperty] private DetectedStat? _selectedResult;
    /// <summary>Client-side filter on property / class / category name (substring).</summary>
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText =
        "Click Detect to shortlist likely HP / MP / Gold fields and confirm them live.";
    /// <summary>Opt-in behavioral signal: cross-reference the two most-recent
    /// snapshots and boost fields that decreased (capture → take damage → capture).</summary>
    [ObservableProperty] private bool _useSnapshotSignal;
    /// <summary>Gates the per-row 🌍 handoff (set by MainWindow from HasGWorld).</summary>
    [ObservableProperty] private bool _isGWorldAvailable;

    /// <summary>Per-session remembered filter keywords (LRU) surfaced as the
    /// filter box's AutoCompleteBox suggestions — see <see cref="KeywordSearchMemory"/>.</summary>
    private readonly KeywordSearchMemory _filterMemory;
    public ObservableCollection<string> FilterHistory => _filterMemory.History;

    // Cross-tab handoffs (same event pattern the other finder panels use).
    public event Action<string>? LocateInGWorld;          // payload = class name
    public event Action<string>? NavigateToInstanceFinder; // payload = class name
    public event Action<string>? RequestCopyText;          // payload = text

    public DetectStatsViewModel(IDumpService dump, ILoggingService log,
                                ISnapshotStore? snapshotStore = null)
    {
        _dump = dump;
        _log = log;
        _snapshotStore = snapshotStore;
        _filterMemory = new KeywordSearchMemory(() => (FilterText, Results.Count > 0));
    }

    [RelayCommand]
    private async Task DetectAsync()
    {
        try
        {
            IsBusy = true;
            Results.Clear();
            StatusText = "Scanning candidate stat fields…";

            // 1. Run the shipped scorer (identical machinery to Interesting Properties).
            var batch = await _dump.SearchPropertiesBatchAsync(
                PropertyScoringTable.SeedQueries, types: null, gameOnly: true, limitPerQuery: 200);

            var dedup = new Dictionary<(string, string, int),
                                       (PropertySearchMatch m, PropertyScoringTable.ScoreResult s)>();
            foreach (var env in batch.PerQuery)
            {
                if (env?.Results == null) continue;
                foreach (var m in env.Results)
                {
                    var key = (
                        string.IsNullOrEmpty(m.DefiningClassName) ? m.ClassName : m.DefiningClassName,
                        m.PropName, m.PropOffset);
                    if (dedup.ContainsKey(key)) continue;
                    dedup[key] = (m, PropertyScoringTable.Score(m));
                }
            }
            var pairBonuses = PropertyScoringTable.ComputeStatPairBonuses(dedup.Values.Select(v => v.m));

            var scored = dedup
                .Select(kv =>
                {
                    int pb = pairBonuses.TryGetValue(kv.Key, out var b) ? b : 0;
                    return (kv.Value.m, kv.Value.s, score: kv.Value.s.FinalScore + pb, pair: pb);
                })
                .Where(x => x.score >= PropertyScoringTable.InterestingThreshold)
                .OrderByDescending(x => x.score)
                .Take(MaxCandidates)
                .ToList();

            if (scored.Count == 0)
            {
                StatusText = "No candidate stat fields scored above threshold.";
                return;
            }

            // 2. Optional behavioral signal from the two most-recent snapshots
            //    (runs off the UI thread — see TryLoadDecreasedFieldsAsync).
            var (snapChanges, snapNote) = UseSnapshotSignal
                ? await TryLoadDecreasedFieldsAsync()
                : (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "");

            // 3. Group by defining class; resolve ONE live instance + ONE walk per
            //    class (bounded), then confirm every candidate field of that class.
            var byClass = scored
                .GroupBy(x => string.IsNullOrEmpty(x.m.DefiningClassName) ? x.m.ClassName : x.m.DefiningClassName)
                .OrderByDescending(g => g.Max(x => x.score))
                .ToList();
            int probeTarget = Math.Min(MaxClassesProbed, byClass.Count);

            var results = new List<DetectedStat>();
            int classesProbed = 0;
            foreach (var grp in byClass)
            {
                bool probe = classesProbed < MaxClassesProbed;
                if (probe) classesProbed++;

                StatusText = $"Confirming {grp.Key} ({classesProbed}/{probeTarget})…";

                InstanceWalkResult? walk = null;
                bool liveExists = false;
                if (probe)
                {
                    try
                    {
                        var inst = await _dump.FindInstancesAsync(grp.Key, exactMatch: true, limit: 3);
                        var live = inst.Instances.FirstOrDefault(i =>
                            !string.IsNullOrEmpty(i.Address) &&
                            !i.Name.StartsWith("Default__", StringComparison.Ordinal));
                        if (live != null)
                        {
                            liveExists = true;
                            walk = await _dump.WalkInstanceAsync(live.Address);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"DetectStats live probe failed for {grp.Key}", ex);
                    }
                }

                foreach (var x in grp)
                {
                    var lf = walk?.Fields?.FirstOrDefault(f =>
                        f.Name == x.m.PropName && f.Offset == x.m.PropOffset);
                    bool plausible = lf != null && IsPlausibleNumeric(lf, out _);
                    bool gas = x.s.IsGasAttribute;
                    bool valueOk = plausible || (gas && liveExists);
                    string liveVal = plausible ? (lf?.TypedValue ?? "") : (gas && liveExists ? "(GAS)" : "");
                    bool decreased = snapChanges.TryGetValue(x.m.PropName, out var snapChg);
                    bool hasMax = x.pair > 0;

                    int conf = x.score
                             + (liveExists ? LiveInstanceBonus : 0)
                             + (plausible ? ValuePlausibleBonus : 0)
                             + (decreased ? SnapshotDecreasedBonus : 0);
                    bool confirmed = liveExists && valueOk && conf >= ConfirmThreshold;

                    results.Add(new DetectedStat
                    {
                        Match = x.m,
                        Category = x.s.Category,
                        BaseScore = x.score,
                        Confidence = conf,
                        IsConfirmed = confirmed,
                        LiveInstanceExists = liveExists,
                        ValuePlausible = plausible,
                        HasMaxSibling = hasMax,
                        IsGasAttribute = gas,
                        SnapshotDecreased = decreased,
                        LiveValue = liveVal,
                        SnapshotChange = decreased ? (snapChg ?? "") : "",
                    });
                }
            }

            _allResults = results
                .OrderByDescending(r => r.IsConfirmed)
                .ThenByDescending(r => r.Confidence)
                .ThenBy(r => r.ClassName, StringComparer.Ordinal)
                .ToList();
            ApplyFilter();

            int confirmedCount = results.Count(r => r.IsConfirmed);
            // Same cap caveat as Interesting Properties: this panel drives the SAME batch call at
            // the same 200-row-per-query limit, so a common seed ("Max", "Count", "Health") caps
            // routinely and the candidate list is a page, not the pool (audit #5 X1).
            int cappedQueries = batch.TruncatedQueries.Count;
            var capSuffix = batch.Aborted
                ? "  ⚠ scan cancelled — partial"
                : cappedQueries > 0
                    ? $"  ⚠ {cappedQueries} keyword(s) hit the 200-row cap — more candidates exist"
                    : "";
            StatusText =
                $"{results.Count} candidates · {confirmedCount} confirmed"
                + (UseSnapshotSignal ? $" · snapshot: {snapNote}" : "")
                + capSuffix
                + "  — reference only, verify before use.";
        }
        catch (Exception ex)
        {
            StatusText = "Detect failed — see logs.";
            _log.Error("DetectStats detect failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Load the set of field NAMES that DECREASED between the two most-recent
    /// usable snapshots (Mode-B "value went down on damage/spend" signal).
    /// Coarse (matched by property name only, per the panel's low-accuracy
    /// disclaimer) and fully defensive — any failure returns an empty set + note.
    /// </summary>
    private async Task<(Dictionary<string, string> changes, string note)> TryLoadDecreasedFieldsAsync()
    {
        var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_snapshotStore == null) return (changes, "unavailable");
        try
        {
            // SnapshotStore's SQLite runs SYNCHRONOUSLY on the caller (its own docs
            // say "wrap in Task.Run, never the UI thread"). A whole-corpus diff can be
            // multi-hundred-ms, which froze the window ("not responding"), so push it
            // to a thread-pool thread and only touch the result back here.
            var diff = await Task.Run(async () =>
            {
                var snaps = await _snapshotStore.ListSnapshotsAsync();
                var usable = snaps.Where(s => s.IsUsable).Take(2).ToList(); // newest first
                if (usable.Count < 2) return null;
                var filter = new SnapshotDiffFilter
                {
                    Direction = SnapshotDiffDirection.Down,
                    IncludeAddedRemoved = false,
                    MaxRows = 20_000,
                };
                return await _snapshotStore.DiffSnapshotsAsync(usable[1].Id, usable[0].Id, filter);
            });

            if (diff == null) return (changes, "need ≥2 usable snapshots");
            foreach (var row in diff.Changed)
            {
                // First (any) decreased row per field name backs the Δ display.
                if (!changes.ContainsKey(row.PropName))
                    changes[row.PropName] = $"{row.OldValue} → {row.NewValue}";
            }
            return (changes, $"{diff.Changed.Count} fields decreased");
        }
        catch (Exception ex)
        {
            _log.Error("DetectStats snapshot signal failed", ex);
            return (changes, "error");
        }
    }

    /// <summary>True when the field is a numeric UPROPERTY whose live typed value
    /// parses to a finite number in a sane magnitude (rejects NaN and pointer-sized
    /// mis-reads). Non-zero is NOT required — 0 HP is a valid value.</summary>
    private static bool IsPlausibleNumeric(LiveFieldValue lf, out double val)
    {
        val = 0;
        if (!NumericTypes.Contains(lf.TypeName)) return false;
        var s = (lf.TypedValue ?? "").Trim();
        if (s.Length == 0) return false;
        // TypedValue may carry a trailing annotation (e.g. "100 (0x64)") — try the
        // whole string first, then the leading token.
        if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out val))
        {
            var head = s.Split(' ')[0];
            if (!double.TryParse(head, NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                return false;
        }
        return double.IsFinite(val) && Math.Abs(val) < 1e9;
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
        _filterMemory.Schedule(value);
    }

    /// <summary>Rebuild <see cref="Results"/> from <see cref="_allResults"/>,
    /// keeping the pre-sorted order, applying the whitespace-separated AND-terms
    /// filter over the property / class / category names.</summary>
    private void ApplyFilter()
    {
        Results.Clear();
        var terms = ObjectTreeFilter.SplitTerms(FilterText);
        foreach (var r in _allResults)
        {
            if (terms.Length > 0
                && !ObjectTreeFilter.MatchesAllTerms(terms, r.PropName, r.ClassName, r.CategoryLabel))
            {
                continue;
            }
            Results.Add(r);
        }
    }

    // ------------------------------------------------------------------
    // Per-row cross-tab handoffs (mirror InterestingProperties)
    // ------------------------------------------------------------------

    [RelayCommand]
    private void LocateRowInGWorld(DetectedStat? row)
    {
        if (row == null || !IsGWorldAvailable || string.IsNullOrEmpty(row.ClassName)) return;
        LocateInGWorld?.Invoke(row.ClassName);
    }

    [RelayCommand]
    private void OpenInInstanceFinder(DetectedStat? row)
    {
        if (row == null || string.IsNullOrEmpty(row.ClassName)) return;
        NavigateToInstanceFinder?.Invoke(row.ClassName);
    }

    [RelayCommand]
    private void CopyPropertyName(DetectedStat? row)
    {
        if (row == null || string.IsNullOrEmpty(row.PropName)) return;
        RequestCopyText?.Invoke(row.PropName);
        StatusText = $"Copied property name: {row.PropName}";
    }
}
