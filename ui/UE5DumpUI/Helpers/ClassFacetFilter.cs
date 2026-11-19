using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Reusable "class noise" filter for result-list panels.
///
/// It builds a Top-N "class → hit count" histogram from the current full result
/// set and lets the user tick classes to HIDE (UI widgets, sound, system
/// components, …). Ticking re-filters the list IMMEDIATELY — no Apply button —
/// via the <c>onChanged</c> callback the owner supplies. An owner whose full
/// result set lives in a C# collection re-runs its filter pass, which calls
/// <see cref="IsExcluded"/> per row; an owner whose full set lives in the DLL
/// re-queries with <see cref="ExcludedClasses"/> and feeds the server's counts
/// back through <see cref="RebuildFromCounts"/>.
///
/// The exclusion is purely a display filter: the full raw result set stays
/// intact, so unticking (or <see cref="ClearCommand"/>) restores hidden rows
/// — there is no risk of losing real data. The excluded set is preserved
/// across rebuilds so a class the user marked as noise stays hidden across
/// re-searches in the same session.
///
/// This is the cheap sibling of the SPC / Diff / Pivot picker (NoiseRowVm +
/// DenylistScope), which persists per game; this exclusion lasts the session
/// only. When the full set is already in memory the histogram + filter are
/// instant and need zero pipe traffic. AOT-safe: plain LINQ + an
/// ObservableCollection, no reflection.
/// </summary>
public sealed partial class ClassFacetFilter : ObservableObject
{
    private readonly Action _onChanged;
    private readonly HashSet<string> _excluded = new(StringComparer.Ordinal);

    // Auto-detect bookkeeping: the matched rule per auto-flagged class
    // (so the green hint survives a re-scan rebuild), and the classes the user
    // explicitly UN-ticked after an auto-suggestion (so a later Auto-detect click
    // respects the keep instead of silently re-hiding them).
    private readonly Dictionary<string, string> _noiseReasonByClass = new(StringComparer.Ordinal);
    private readonly HashSet<string> _autoKept = new(StringComparer.Ordinal);

    // Guards bulk Picked writes (Rebuild / Clear) from re-entrantly firing the
    // per-row toggle handler (which would re-run the owner's filter once per row).
    private bool _suppress;

    /// <summary>Top-N class rows shown in the picker, each with a "Hide" tick.</summary>
    public ObservableCollection<ClassFacetRow> Facets { get; } = new();

    /// <summary>True when there is at least one facet row (gates the picker button).</summary>
    [ObservableProperty] private bool _hasFacets;

    /// <summary>How many classes are currently hidden (drives the button badge).</summary>
    [ObservableProperty] private int _excludedCount;

    /// <summary>Total distinct classes in the source set (shown in the summary).</summary>
    [ObservableProperty] private int _distinctCount;

    /// <summary>True when the source list was capped / truncated, so the counts
    /// are a partial lower bound (e.g. Instance Finder hit its result cap).</summary>
    [ObservableProperty] private bool _countsPartial;

    /// <summary>One-line summary line shown under the picker.</summary>
    [ObservableProperty] private string _summary = "";

    /// <summary>True when any class is hidden — drives the button badge visibility.</summary>
    public bool AnyExcluded => ExcludedCount > 0;

    /// <summary>Whether the picker button should be openable. Stays true while
    /// anything is hidden EVEN IF the current result has no facets (e.g. a 0-row
    /// search, or a search whose every match is in a hidden class) — otherwise a
    /// stale exclusion would be stuck behind a disabled button with no way to
    /// reach <see cref="ClearCommand"/>.</summary>
    public bool CanOpen => HasFacets || AnyExcluded;

    partial void OnHasFacetsChanged(bool value)
    {
        OnPropertyChanged(nameof(CanOpen));
        OnPropertyChanged(nameof(CanAutoDetect));
        AutoDetectCommand.NotifyCanExecuteChanged();
    }

    partial void OnExcludedCountChanged(int value)
    {
        OnPropertyChanged(nameof(AnyExcluded));
        OnPropertyChanged(nameof(CanOpen));
    }

    // ── Opt-in, safe auto-detect (provider-injected) ─────────────────────────

    private Func<IReadOnlyList<string>, Task<IReadOnlyList<(string className, bool isNoise, string reason)>>>? _autoDetectProvider;

    /// <summary>Optional async classifier the owner injects (a DLL round-trip via
    /// <c>detect_noise_classes</c>). When set, the picker shows an "Auto-detect"
    /// button; clicking it pre-ticks the facet rows the classifier flags as
    /// engine/system noise (safe rules only) and labels each with the matched
    /// rule. Null = the button is hidden (no auto-detect on this surface).</summary>
    public Func<IReadOnlyList<string>, Task<IReadOnlyList<(string className, bool isNoise, string reason)>>>? AutoDetectProvider
    {
        get => _autoDetectProvider;
        set
        {
            _autoDetectProvider = value;
            OnPropertyChanged(nameof(HasAutoDetect));
            OnPropertyChanged(nameof(CanAutoDetect));
            AutoDetectCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>True when an auto-detect provider is wired (drives button visibility).</summary>
    public bool HasAutoDetect => _autoDetectProvider != null;

    /// <summary>Drives the auto-detect button's enabled state.</summary>
    public bool CanAutoDetect => _autoDetectProvider != null && HasFacets && !IsAutoDetecting;

    [ObservableProperty] private bool _isAutoDetecting;

    partial void OnIsAutoDetectingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAutoDetect));
        AutoDetectCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Classify the current facet classes and pre-tick the engine/system
    /// ones (opt-in, one-shot, reversible — the user can untick or Clear).</summary>
    [RelayCommand(CanExecute = nameof(CanAutoDetect))]
    private async Task AutoDetectAsync()
    {
        var provider = _autoDetectProvider;
        if (provider == null || Facets.Count == 0) return;
        var names = Facets.Select(f => f.ClassName).ToList();
        IsAutoDetecting = true;
        try
        {
            var verdicts = await provider(names);
            int flagged = ApplyAutoSuggestions(verdicts);
            // Explain the outcome so a click is never a silent no-op.
            Summary = flagged > 0
                ? $"Auto-detect: {flagged} system class(es) hidden"
                : "Auto-detect: no system classes found";
        }
        catch
        {
            // Best-effort — a classify failure (e.g. game not connected) leaves
            // the picker untouched, but tell the user instead of doing nothing.
            Summary = "Auto-detect unavailable (is the game connected?)";
        }
        finally
        {
            IsAutoDetecting = false;
        }
    }

    // Pre-tick the noise rows + remember each matched rule (so the hint survives a
    // re-scan rebuild). Adds to the excluded set and fires onChanged ONCE if
    // anything new got hidden. Never UN-ticks (won't undo manual picks) and
    // respects classes the user deliberately kept (_autoKept). Returns the count
    // newly hidden.
    private int ApplyAutoSuggestions(IReadOnlyList<(string className, bool isNoise, string reason)> verdicts)
    {
        var byName = new Dictionary<string, (bool isNoise, string reason)>(StringComparer.Ordinal);
        foreach (var v in verdicts)
            if (!string.IsNullOrEmpty(v.className)) byName[v.className] = (v.isNoise, v.reason);

        int flagged = 0;
        _suppress = true;
        foreach (var row in Facets)
        {
            if (!byName.TryGetValue(row.ClassName, out var verdict)) continue;
            if (!verdict.isNoise) continue;                    // only act on noise verdicts
            if (_autoKept.Contains(row.ClassName)) continue;   // respect a deliberate keep
            row.NoiseReason = verdict.reason;
            _noiseReasonByClass[row.ClassName] = verdict.reason;   // persist across rebuilds
            if (_excluded.Add(row.ClassName))
            {
                row.Picked = true;             // suppressed: no per-row re-filter
                flagged++;
            }
        }
        _suppress = false;

        if (flagged > 0)
        {
            ExcludedCount = _excluded.Count;
            UpdateSummary();
            _onChanged();
        }
        return flagged;
    }

    /// <param name="onChanged">Invoked whenever the exclusion set changes; the
    /// owner re-applies the exclusion, by its filter pass (which consults
    /// <see cref="IsExcluded"/>) or by a re-query with <see cref="ExcludedClasses"/>.</param>
    public ClassFacetFilter(Action onChanged)
        => _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

    /// <summary>True when <paramref name="className"/> is currently hidden. Call
    /// from the owner's ApplyFilter to skip excluded rows.</summary>
    public bool IsExcluded(string? className)
        => _excluded.Count > 0 && !string.IsNullOrEmpty(className) && _excluded.Contains(className);

    /// <summary>Snapshot of the currently-hidden class names. For SERVER-SIDE
    /// owners: pass this to the DLL query as its class-exclusion list so the
    /// histogram-driven exclusion is applied over the full result set, not just
    /// the loaded page. A fresh array each call so an
    /// in-flight async query holds a stable copy.</summary>
    public IReadOnlyList<string> ExcludedClasses
        => _excluded.Count == 0 ? System.Array.Empty<string>() : _excluded.ToArray();

    /// <summary>
    /// Rebuild the Top-N histogram from the current FULL result set. Preserves
    /// the excluded set (re-ticks rebuilt rows that are still hidden). Does NOT
    /// fire <c>onChanged</c> — the owner is expected to (re)apply its own filter
    /// right after, or is mid-load.
    /// </summary>
    /// <param name="classNames">One class name per row in the FULL result set
    /// (duplicates expected — they are what gets counted).</param>
    /// <param name="topN">How many top classes to show (default 40).</param>
    /// <param name="countsPartial">Source list was capped (counts are a lower bound).</param>
    public void Rebuild(IEnumerable<string?> classNames, int topN = 40, bool countsPartial = false)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cn in classNames)
        {
            if (string.IsNullOrEmpty(cn)) continue;
            counts[cn] = counts.TryGetValue(cn, out var n) ? n + 1 : 1;
        }

        var top = counts
            .OrderByDescending(k => k.Value)
            .ThenBy(k => k.Key, StringComparer.Ordinal)
            .Take(topN)
            .Select(kv => (kv.Key, kv.Value));
        PopulateFacets(top, counts.Count, countsPartial);
    }

    /// <summary>
    /// Rebuild from a PRE-COUNTED histogram (server-side path: the DLL owns the
    /// full result set, so it computes the class counts over all of it and
    /// sends back the Top-N). <paramref name="counts"/> is
    /// expected already sorted (count desc); <paramref name="distinctTotal"/> is
    /// the true number of distinct classes (≥ counts.Count when the server
    /// capped). Preserves the excluded set like <see cref="Rebuild"/>.
    /// </summary>
    public void RebuildFromCounts(IEnumerable<(string className, int count)> counts,
                                  int distinctTotal, bool countsPartial = false)
    {
        var list = counts.Where(c => !string.IsNullOrEmpty(c.className)).ToList();
        PopulateFacets(list, Math.Max(distinctTotal, list.Count), countsPartial);
    }

    // Shared facet (re)population. `top` is the already-sorted, already-capped
    // (className, count) sequence; `distinctTotal` is the full distinct-class
    // count (may exceed top.Count when capped).
    private void PopulateFacets(IEnumerable<(string className, int count)> top,
                                int distinctTotal, bool countsPartial)
    {
        _suppress = true;
        Facets.Clear();
        foreach (var (className, count) in top)
        {
            Facets.Add(new ClassFacetRow(this)
            {
                ClassName = className,
                HitCount  = count,
                Picked    = _excluded.Contains(className),
                // Re-seed the auto-detect rule hint so it survives a re-scan
                // rebuild (mirrors how Picked is re-seeded from _excluded).
                NoiseReason = _noiseReasonByClass.TryGetValue(className, out var rsn) ? rsn : "",
            });
        }
        _suppress = false;

        DistinctCount = distinctTotal;
        CountsPartial = countsPartial;
        HasFacets     = Facets.Count > 0;
        ExcludedCount = _excluded.Count;
        UpdateSummary();
    }

    // Called by a row when its Hide checkbox toggles.
    internal void NotifyRowToggled(ClassFacetRow row)
    {
        if (_suppress || string.IsNullOrEmpty(row.ClassName)) return;
        if (row.Picked)
        {
            _excluded.Add(row.ClassName);
            _autoKept.Remove(row.ClassName);   // re-hiding it -> no longer "kept"
        }
        else
        {
            _excluded.Remove(row.ClassName);
            // Unticking a class Auto-detect had flagged is a deliberate keep:
            // remember it so a later Auto-detect won't silently re-hide it, and
            // drop the now-contradictory green rule hint.
            if (_noiseReasonByClass.Remove(row.ClassName))
            {
                row.NoiseReason = "";
                _autoKept.Add(row.ClassName);
            }
        }
        ExcludedCount = _excluded.Count;
        UpdateSummary();
        _onChanged();
    }

    /// <summary>Clear every exclusion (untick all + restore hidden rows), then
    /// re-filter. The "show everything again" path.</summary>
    [RelayCommand]
    private void Clear()
    {
        if (_excluded.Count == 0) return;
        _excluded.Clear();
        _noiseReasonByClass.Clear();
        _autoKept.Clear();
        _suppress = true;
        foreach (var r in Facets) { r.Picked = false; r.NoiseReason = ""; }
        _suppress = false;
        ExcludedCount = 0;
        UpdateSummary();
        _onChanged();
    }

    /// <summary>Drop the histogram AND exclusions entirely (e.g. when the owner
    /// switches to a different result context that the facets don't describe —
    /// the Instance Finder reverse-address lookup). Does NOT fire
    /// <c>onChanged</c>: the caller is rebuilding its list itself.</summary>
    public void Reset()
    {
        _excluded.Clear();
        _noiseReasonByClass.Clear();
        _autoKept.Clear();
        _suppress = true;
        Facets.Clear();
        _suppress = false;
        HasFacets     = false;
        ExcludedCount = 0;
        DistinctCount = 0;
        CountsPartial = false;
        Summary       = "";
    }

    private void UpdateSummary()
    {
        if (!HasFacets) { Summary = ""; return; }
        Summary = ExcludedCount > 0
            ? $"{ExcludedCount} hidden · {DistinctCount} distinct class(es)"
            : $"{DistinctCount} distinct class(es)";
    }
}

/// <summary>One Top-N row in a <see cref="ClassFacetFilter"/> picker. Toggling
/// <see cref="Picked"/> hides/shows that class in the owning list immediately.</summary>
public sealed partial class ClassFacetRow : ObservableObject
{
    private readonly ClassFacetFilter _owner;

    public ClassFacetRow(ClassFacetFilter owner) => _owner = owner;

    public string ClassName { get; init; } = "";
    public int    HitCount  { get; init; }

    /// <summary>"ClassName  (1,234)" — the picker row label.</summary>
    public string Display => $"{ClassName}  ({HitCount:N0})";

    /// <summary>User's "hide this class" tick. Toggling re-filters live.</summary>
    [ObservableProperty] private bool _picked;

    partial void OnPickedChanged(bool value) => _owner.NotifyRowToggled(this);

    /// <summary>The matched auto-detect rule (e.g. "engine package" / "engine
    /// base class"); empty unless Auto-detect flagged this class. Shown as a
    /// dim hint next to the row so the user can audit the suggestion.</summary>
    [ObservableProperty] private string _noiseReason = "";
}
