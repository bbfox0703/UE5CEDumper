using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Interesting Properties Finder panel (B' / round 1).
///
/// Pragmatic loading strategy: rather than add a new DLL command, the VM
/// issues N parallel <c>search_properties</c> calls — one per seed
/// keyword from <see cref="PropertyScoringTable.SeedQueries"/>. Each
/// call returns up to <see cref="PerQueryLimit"/> matches; results are
/// deduped client-side by (DefiningClassName, PropName, PropOffset),
/// scored, sorted, and filtered.
///
/// Trade-offs:
///   - Captures the ~80-95% of high-relevance hits without needing
///     a new pipe command (saves a DLL build + protocol change for
///     round 1).
///   - Misses long-tail keywords not in the seed list. Iterate by
///     adding to <see cref="PropertyScoringTable.SeedQueries"/>.
///
/// The Unusual Location signal (LocalPlayer / GameViewportClient / HUD
/// / CheatManager) is the key reason this tab exists separate from
/// regular Property Search — it surfaces cheat-relevant fields that
/// live OUTSIDE the conventional Character/Pawn/PlayerState containers.
/// </summary>
public partial class InterestingPropertiesViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService? _platform;

    /// <summary>How many matches each seed-query call asks for. Default
    /// 200 mirrors the existing PropertySearch tab's limit. Total
    /// pre-dedup is up to SeedQueries.Length * PerQueryLimit which is
    /// ~6000 entries — typically dedupes down to a few hundred to a
    /// few thousand.</summary>
    public const int PerQueryLimit = 200;

    private List<ScoredPropertyRow> _allRows = new();

    [ObservableProperty] private bool   _gameOnly = true;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private PropertyCategory? _categoryFilter; // null = All
    [ObservableProperty] private bool   _unusualOnly;  // ⚠ rows only
    [ObservableProperty] private bool   _showAll;      // bypass threshold
    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _statusText = "Click Load to scan for interesting properties";
    [ObservableProperty] private ObservableCollection<ScoredPropertyRow> _results = new();
    [ObservableProperty] private bool _isXrefBatchRunning;
    [ObservableProperty] private ScoredPropertyRow? _selectedResult;

    // Category dropdown values (in display order).
    public IReadOnlyList<PropertyCategory?> CategoryOptions { get; } = new PropertyCategory?[]
    {
        null,                              // "All"
        PropertyCategory.Stats,
        PropertyCategory.Combat,
        PropertyCategory.Resources,
        PropertyCategory.Movement,
        PropertyCategory.Utility,
        PropertyCategory.Timing,
        PropertyCategory.Other,
    };

    // ------------------------------------------------------------------
    // Cross-tab navigation events. MainWindow routes these to the right
    // destination. Single event for now (the cheat workflow lives in
    // Live Walker on a real instance); add Class Struct fallback if a
    // user reports finding zero live instances of a class repeatedly.
    // ------------------------------------------------------------------

    public event Action<string, string>? NavigateToProperty;
    public event Action<string>? RequestCopyText;

    /// <summary>Raised by the per-row "inst" button to open the property's class
    /// in the Instance Finder tab. Payload = class name.</summary>
    public event Action<string>? NavigateToInstanceFinder;

    /// <summary>Raised to locate a live instance of the property's class within the
    /// GWorld object graph. Payload = class name (MainWindow resolves a
    /// representative non-CDO instance via find_instance, then runs the path
    /// search). Mirrors the Interesting Functions per-row 🌍 handoff.</summary>
    public event Action<string>? LocateInGWorld;

    /// <summary>Engine-rooted counterpart of <see cref="LocateInGWorld"/> (path search
    /// rooted at the live UGameEngine). Payload = class name.</summary>
    public event Action<string>? LocateInGameEngine;

    /// <summary>True when GWorld is available — gates the per-row "Locate in GWorld"
    /// button (set by MainWindow from the engine state's HasGWorld flag).</summary>
    [ObservableProperty] private bool _isGWorldAvailable;

    /// <summary>Raised to pivot the selected property in the experimental Class
    /// Pivot tab (className, propName). C5 right-click handoff.</summary>
    public event Action<string, string>? NavigateToPivot;

    /// <summary>Gates the "Pivot this property" context-menu item — true only when
    /// the experimental Class Pivot tab is available (mirrors the gate).</summary>
    [ObservableProperty] private bool _pivotEnabled;

    /// <summary>Raised when the user clicks "Generate Cheat Table" on
    /// the multi-select toolbar. Subscribers (MainWindow) open the save
    /// dialog and write the payload — keeping IO out of the VM keeps it
    /// testable.</summary>
    public event Action<string /*defaultFileName*/, string /*ctXml*/>? RequestSaveCheatTable;

    /// <summary>Client-side class-noise filter: hides ticked classes (UI widgets,
    /// sound, system components) from <see cref="Results"/>. Lives over the full
    /// <see cref="_allRows"/> set; ticking re-runs <see cref="ApplyFilter"/>.</summary>
    public ClassFacetFilter ClassFilter { get; }

    /// <summary>Live "N hidden by class filter" note (empty when nothing hidden) —
    /// lets the user tell an empty grid caused by a stale class exclusion from a
    /// genuine miss. Recomputed by <see cref="ApplyFilter"/>.</summary>
    [ObservableProperty] private string _classFilterNote = "";

    /// <summary>Per-session remembered filter keywords (LRU) surfaced as the
    /// filter box's AutoCompleteBox suggestions — see <see cref="KeywordSearchMemory"/>.</summary>
    private readonly KeywordSearchMemory _filterMemory;
    public ObservableCollection<string> FilterHistory => _filterMemory.History;

    public InterestingPropertiesViewModel(IDumpService dump, ILoggingService log,
                                          IPlatformService? platform = null)
    {
        _dump = dump;
        _log  = log;
        _platform = platform;
        ClassFilter = new ClassFacetFilter(ApplyFilter)
        {
            AutoDetectProvider = async names =>
                (await _dump.DetectNoiseClassesAsync(names))
                    .Select(n => (n.ClassName, n.IsNoise, n.Reason)).ToList(),
        };
        _filterMemory = new KeywordSearchMemory(() => (FilterText, Results.Count > 0));
    }

    /// <summary>
    /// "Find functions using this field" — bridges the property scoring to the
    /// functions that actually reference the scored field (static bytecode
    /// xref). Opens the shared self-contained dialog (no tab impact).
    /// </summary>
    [RelayCommand]
    private async Task FindFieldXrefsAsync(ScoredPropertyRow? row)
    {
        row ??= SelectedResult;
        if (row == null || string.IsNullOrEmpty(row.Match.FieldAddr)) return;
        try
        {
            await Views.PropertyXrefDialog.ShowForFieldAsync(
                row.PropName, row.PropType, row.Match.FieldAddr, _dump, _platform);
        }
        catch (Exception ex)
        {
            _log.Error($"FindFieldXrefs failed for {row.PropName}", ex);
        }
    }

    // Find Funcs is the EXPENSIVE direction — each call is a full game-wide
    // bytecode sweep — so warn past a low row count and update progress per row.
    private CancellationTokenSource? _xrefBatchCts;

    /// <summary>
    /// Batch "Find Funcs": run find_property_xrefs for the selected rows (or all
    /// results if none selected) and write a compact "N · func1, func2" summary
    /// into each row's inline <see cref="ScoredPropertyRow.XrefInfo"/> cell.
    /// Cancellable; rows without a field address are skipped.
    /// </summary>
    [RelayCommand]
    private async Task BatchFindFuncsAsync(IList<ScoredPropertyRow>? selected)
    {
        var targets = ((selected != null && selected.Count > 0) ? selected : (IList<ScoredPropertyRow>)Results)
            .Where(r => !string.IsNullOrEmpty(r.Match.FieldAddr)).ToList();
        if (targets.Count == 0) { StatusText = "No rows with a field address to scan."; return; }

        if (targets.Count > Constants.XrefBatchWarnThreshold)
        {
            var ok = await Views.ConfirmDialog.ShowAsync(
                "Batch: find functions",
                $"Scan {targets.Count} properties? Each runs a FULL game-wide bytecode "
              + "sweep (hundreds of ms each), so this can take a while. Tip: select fewer "
              + "rows first (Ctrl/Shift+click) to scope it.",
                confirmText: "Run");
            if (!ok) return;
        }

        _xrefBatchCts?.Cancel();
        _xrefBatchCts = new CancellationTokenSource();
        var ct = _xrefBatchCts.Token;
        IsXrefBatchRunning = true;
        int done = 0, withFuncs = 0, cached = 0;
        try
        {
            foreach (var row in targets)
            {
                ct.ThrowIfCancellationRequested();
                // Skip rows already scanned (XrefInfo persists across filter changes).
                if (!string.IsNullOrEmpty(row.XrefInfo)) { cached++; continue; }
                try
                {
                    var res = await _dump.FindPropertyXrefsAsync(row.Match.FieldAddr, true, 200, ct);
                    row.XrefInfo = XrefFormat.FunctionsSummary(res.Xrefs);
                    if (res.Xrefs.Count > 0) withFuncs++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    row.XrefInfo = "—";
                    _log.Error($"Batch Find Funcs failed for {row.PropName}", ex);
                }
                done++;
                StatusText = $"Find Funcs: {done}/{targets.Count} scanned ({withFuncs} referenced)…";
            }
            StatusText = $"Find Funcs done: {withFuncs}/{targets.Count} referenced by a function"
                       + (cached > 0 ? $" ({cached} cached)." : ".");
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Find Funcs cancelled at {done}/{targets.Count}.";
        }
        finally { IsXrefBatchRunning = false; }
    }

    /// <summary>Cancel an in-flight batch Find Funcs run.</summary>
    [RelayCommand]
    private void CancelXrefBatch() => _xrefBatchCts?.Cancel();

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
        _filterMemory.Schedule(value);
    }
    partial void OnCategoryFilterChanged(PropertyCategory? value)   => ApplyFilter();
    partial void OnUnusualOnlyChanged(bool value)                   => ApplyFilter();
    partial void OnShowAllChanged(bool value)                       => ApplyFilter();

    /// <summary>
    /// Send ONE batched search_properties_batch call carrying all
    /// SeedQueries. DLL walks GObjects + class fields once and checks
    /// every property against every keyword, returning per-query result
    /// envelopes. Wall-time drops from ~42s (legacy sequential pipe
    /// loop, build 681-682) to ~1.5s for a 4400-class game.
    ///
    /// Why progress is now one-shot: the speedup is so large that the
    /// per-keyword loop disappears entirely on the wire. Status text
    /// just shows the single round-trip + scoring phase.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            ClearError();
            IsLoading = true;

            var queries = PropertyScoringTable.SeedQueries;
            StatusText = $"Walking GObjects with {queries.Length} keyword queries (single batched call)...";

            var batch = await _dump.SearchPropertiesBatchAsync(
                queries, types: null, gameOnly: GameOnly, limitPerQuery: PerQueryLimit);

            StatusText = $"Scoring + dedup ({batch.Total:N0} raw hits across " +
                         $"{batch.ScannedClasses:N0} classes)...";

            // Flatten the per-query envelopes into a single list for the
            // existing dedup/score loop; that loop dedupes by
            // (DefiningClassName, PropName, PropOffset) so per-query
            // duplicates (same field matched by multiple keywords)
            // collapse into one ScoredPropertyRow.
            var results = new PropertySearchResult[batch.PerQuery.Count];
            for (int i = 0; i < batch.PerQuery.Count; i++)
            {
                var env = batch.PerQuery[i];
                results[i] = new PropertySearchResult
                {
                    Total          = env.MatchCount,
                    ScannedClasses = batch.ScannedClasses,
                    ScannedObjects = batch.ScannedObjects,
                    Truncated      = env.Truncated,
                    Aborted        = batch.Aborted,
                    Results        = env.Results,
                };
            }

            // Score + dedup happens off the UI thread; on huge games the
            // pre-dedup set can hit ~6000 entries.
            _allRows = await Task.Run(() =>
            {
                // Pass 1 — dedup by (defining class, prop name, offset). Keep
                // the first occurrence + its base ScoreResult; the seed-query
                // order isn't semantically meaningful so first-win is fine.
                var dedup = new Dictionary<(string, string, int),
                                           (PropertySearchMatch m, PropertyScoringTable.ScoreResult s)>();
                foreach (var r in results)
                {
                    if (r?.Results == null) continue;
                    foreach (var m in r.Results)
                    {
                        var key = (
                            string.IsNullOrEmpty(m.DefiningClassName) ? m.ClassName : m.DefiningClassName,
                            m.PropName,
                            m.PropOffset);
                        if (dedup.ContainsKey(key)) continue;
                        dedup[key] = (m, PropertyScoringTable.Score(m));
                    }
                }

                // Pass 2 — Current/Max stat-pair bonuses over the deduped set.
                // This needs cross-field context within a class (a lone Score
                // call can't see siblings), so it runs here, not in Score.
                var pairBonuses = PropertyScoringTable.ComputeStatPairBonuses(
                    dedup.Values.Select(v => v.m));

                // Pass 3 — materialise rows with the combined score.
                var rows = new List<ScoredPropertyRow>(dedup.Count);
                foreach (var kv in dedup)
                {
                    var (m, s) = kv.Value;
                    int pair = pairBonuses.TryGetValue(kv.Key, out var pb) ? pb : 0;
                    rows.Add(new ScoredPropertyRow
                    {
                        Match              = m,
                        FinalScore         = s.FinalScore + pair,
                        Category           = s.Category,
                        KeywordHits        = s.KeywordHits,
                        ClassBonus         = s.ClassBonus,
                        IsUnusualLocation  = s.IsUnusualLocation,
                        StructuralBonus    = s.StructuralBonus,
                        IsGasAttribute     = s.IsGasAttribute,
                        PairBonus          = pair,
                    });
                }

                rows.Sort((a, b) =>
                {
                    // Unusual hits float to the top within equal-score
                    // bands, then by score, then by class+prop for stable
                    // ordering.
                    int cmp = b.FinalScore.CompareTo(a.FinalScore);
                    if (cmp != 0) return cmp;
                    cmp = b.IsUnusualLocation.CompareTo(a.IsUnusualLocation);
                    if (cmp != 0) return cmp;
                    cmp = string.Compare(a.ClassName, b.ClassName, StringComparison.Ordinal);
                    if (cmp != 0) return cmp;
                    return string.Compare(a.PropName, b.PropName, StringComparison.Ordinal);
                });

                return rows;
            });

            // Build the class histogram over the FULL deduped set, then filter.
            ClassFilter.Rebuild(_allRows.Select(r => r.ClassName));
            ApplyFilter();

            int interesting = 0;
            int unusual = 0;
            foreach (var r in _allRows)
            {
                if (r.FinalScore >= PropertyScoringTable.InterestingThreshold) interesting++;
                if (r.IsUnusualLocation) unusual++;
            }

            // A capped sweep must SAY so. Without this the panel reports a row count that
            // reads as "this is the pool", the user filters it, does not find their field and
            // concludes it does not exist -- the exact report class the D5/F4 fix was written
            // to end, surviving here because the batch path never parsed the flag (audit #5 X1).
            var capped = batch.TruncatedQueries;
            var capSuffix = batch.Aborted
                ? "  ⚠ SCAN CANCELLED - this list is partial"
                : capped.Count > 0
                    ? $"  ⚠ {capped.Count} of {queries.Length} keywords STOPPED at the " +
                      $"{PerQueryLimit}-row cap ({string.Join(", ", capped.Take(3))}" +
                      $"{(capped.Count > 3 ? ", …" : "")}) - more matches exist"
                    : "";
            StatusText =
                $"{_allRows.Count:N0} unique properties  " +
                $"(threshold {PropertyScoringTable.InterestingThreshold}+: {interesting:N0}, " +
                $"⚠ unusual: {unusual:N0}){capSuffix}";
            _log.Info($"InterestingProperties load: queries={queries.Length} " +
                      $"unique={_allRows.Count} interesting={interesting} unusual={unusual} " +
                      $"(gameOnly={GameOnly})");
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Load failed";
            _log.Error("InterestingProperties load failed", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Rebuild <see cref="Results"/> from <see cref="_allRows"/> applying
    /// name + category + Unusual + threshold filters. Order preserved
    /// from the pre-sorted full list.
    /// </summary>
    private void ApplyFilter()
    {
        Results.Clear();
        if (_allRows.Count == 0) return;

        // Space-separated terms are ANDed (each must hit PropName or ClassName),
        // so "health max" narrows to entries matching both — the shared Object Tree
        // filter semantics.
        var terms      = ObjectTreeFilter.SplitTerms(FilterText);
        var cat        = CategoryFilter;
        var threshold  = ShowAll ? int.MinValue : PropertyScoringTable.InterestingThreshold;
        var unusualGate = UnusualOnly;

        int hiddenByClass = 0;
        foreach (var row in _allRows)
        {
            if (row.FinalScore < threshold) continue;
            if (cat.HasValue && row.Category != cat.Value) continue;
            if (unusualGate && !row.IsUnusualLocation) continue;
            if (terms.Length > 0 &&
                !ObjectTreeFilter.MatchesAllTerms(terms, row.PropName, row.ClassName))
            {
                continue;
            }
            // Class-noise exclusion last, so the count reflects rows that would
            // otherwise be visible.
            if (ClassFilter.IsExcluded(row.ClassName)) { hiddenByClass++; continue; }
            Results.Add(row);
        }
        ClassFilterNote = hiddenByClass > 0 ? $"{hiddenByClass} hidden by class filter" : "";
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FilterText     = "";
        CategoryFilter = null;
        UnusualOnly    = false;
        ShowAll        = false;
    }

    /// <summary>Per-row action: fire navigate event so MainWindow can
    /// open the property's owning class in Live Walker (or fall back
    /// to Class Struct if no live instance).</summary>
    [RelayCommand]
    private void OpenInLiveWalker(ScoredPropertyRow? row)
    {
        if (row == null) return;
        NavigateToProperty?.Invoke(row.ClassName, row.PropName);
    }

    /// <summary>Per-row "inst" action: open this property's class in the Instance
    /// Finder tab (pre-fill the class name + run the search). Mirrors the
    /// Property Search / Value Search "inst" handoff.</summary>
    [RelayCommand]
    private void OpenInInstanceFinder(ScoredPropertyRow? row)
    {
        if (row == null || string.IsNullOrEmpty(row.ClassName)) return;
        NavigateToInstanceFinder?.Invoke(row.ClassName);
    }

    /// <summary>Per-row 🌍 action: locate a live instance of this property's class
    /// within the GWorld graph (parent mode — stops at the object that points to the
    /// instance). MainWindow resolves the representative instance. Mirrors the
    /// Interesting Functions LocateRowInGWorld command.</summary>
    [RelayCommand]
    private void LocateRowInGWorld(ScoredPropertyRow? row)
    {
        if (row == null || !IsGWorldAvailable || string.IsNullOrEmpty(row.ClassName)) return;
        LocateInGWorld?.Invoke(row.ClassName);
    }

    /// <summary>Engine-rooted counterpart — not gated on IsGWorldAvailable (engine
    /// availability is independent of GWorld; the DLL reports no_engine via the banner).</summary>
    [RelayCommand]
    private void LocateRowInGameEngine(ScoredPropertyRow? row)
    {
        if (row == null || string.IsNullOrEmpty(row.ClassName)) return;
        LocateInGameEngine?.Invoke(row.ClassName);
    }

    /// <summary>Per-row action: pivot the property's class in the Class Pivot tab.</summary>
    [RelayCommand]
    private void PivotThis(ScoredPropertyRow? row)
    {
        row ??= SelectedResult;
        if (row == null || string.IsNullOrEmpty(row.ClassName)) return;
        NavigateToPivot?.Invoke(row.ClassName, row.PropName);
    }

    /// <summary>Per-row action: copy bare property name to clipboard.</summary>
    [RelayCommand]
    private void CopyPropertyName(ScoredPropertyRow? row)
    {
        if (row == null) return;
        if (string.IsNullOrEmpty(row.PropName)) return;
        RequestCopyText?.Invoke(row.PropName);
        StatusText = $"Copied property name: {row.PropName}";
    }

    // ------------------------------------------------------------------
    // Multi-row batch: turn the user's current DataGrid selection into a
    // single CT file (one freeze entry per row, grouped by Category).
    // Rows whose UE type isn't freeze-supported (StructProperty,
    // ArrayProperty, etc.) are dropped from the batch with a status-
    // line warning rather than silently skipped.
    // ------------------------------------------------------------------

    /// <summary>Default Lua literal for a freeze CFG.value, chosen
    /// per UE type. Users edit this in CE before activating; the
    /// values here are "obvious cheat" placeholders (max-ish for
    /// numerics, true for bool).</summary>
    internal static string DefaultFreezeLiteral(string ueTypeName) => ueTypeName switch
    {
        "BoolProperty"    => "true",
        "ByteProperty"    => "255",
        "Int8Property"    => "99",
        "Int16Property"   => "9999",
        "UInt16Property"  => "9999",
        "IntProperty"     => "99999",
        "UInt32Property"  => "99999",
        "EnumProperty"    => "0",
        "Int64Property"   => "99999",
        "UInt64Property"  => "99999",
        "FloatProperty"   => "9999.0",
        "DoubleProperty"  => "9999.0",
        _                 => "0",
    };

    /// <summary>Build the CT batch from <paramref name="selected"/>. Public
    /// + static for direct test coverage (no need to stand up the full VM
    /// to verify the row mapping + skip-reason accounting).</summary>
    public static (
        List<CheatTableRow> rows,
        int skippedUnsupported,
        int skippedMissingOffset) BuildRowsFromSelection(
            IEnumerable<ScoredPropertyRow> selected)
    {
        var rows = new List<CheatTableRow>();
        int skippedUnsupported = 0;
        int skippedMissingOffset = 0;
        foreach (var sr in selected)
        {
            if (sr is null) continue;
            if (!FreezeScriptGenerator.IsTypeSupported(sr.PropType))
            {
                skippedUnsupported++;
                continue;
            }
            // PropOffset==0 is valid (root of a struct), but DefiningClassName
            // empty signals a malformed row from a partial pipe reply —
            // drop it rather than emit a broken script.
            string targetClass = string.IsNullOrEmpty(sr.DefiningClassName)
                ? sr.ClassName : sr.DefiningClassName;
            if (string.IsNullOrEmpty(targetClass))
            {
                skippedMissingOffset++;
                continue;
            }

            var fp = new FreezeScriptParams
            {
                ClassName       = targetClass,
                PropertyName    = sr.PropName,
                PropertyOffset  = sr.PropOffset,
                UeTypeName      = sr.PropType,
                PropertySize    = sr.PropSize,
                ValueLiteral    = DefaultFreezeLiteral(sr.PropType),
            };
            string desc = targetClass == sr.ClassName
                ? $"{sr.ClassName}::{sr.PropName}"
                : $"{sr.ClassName}::{sr.PropName} (defined on {sr.DefiningClassName})";
            rows.Add(new CtPropertyRow
            {
                Category     = PropertyScoringTable.DisplayName(sr.Category),
                Description  = desc,
                FreezeParams = fp,
            });
        }
        return (rows, skippedUnsupported, skippedMissingOffset);
    }

    /// <summary>"Generate Cheat Table from Selection" command. The
    /// DataGrid binds its SelectedItems via the panel code-behind; we
    /// take the snapshot as a parameter so the VM doesn't have to
    /// reach into Avalonia's SelectedItems collection (which has AOT-
    /// hostile dynamic typing). The panel converts SelectedItems
    /// to an IList&lt;ScoredPropertyRow&gt; before invoking.</summary>
    [RelayCommand]
    private void GenerateCheatTable(IList<ScoredPropertyRow>? selected)
    {
        if (selected is null || selected.Count == 0)
        {
            StatusText = "Select 2+ rows first (Ctrl/Shift+click).";
            return;
        }

        var (rows, skippedUnsupported, skippedMissingOffset) =
            BuildRowsFromSelection(selected);

        if (rows.Count == 0)
        {
            StatusText = skippedUnsupported > 0
                ? $"Selected rows aren't freeze-supported " +
                  $"(struct / array / non-scalar types). 0 entries in batch."
                : "Selection produced 0 valid rows — nothing to write.";
            return;
        }

        var now = DateTime.Now;
        string title = $"UE5CEDumper Interesting Properties — " +
                       $"{rows.Count} row{(rows.Count == 1 ? "" : "s")} " +
                       $"@ {now:yyyy-MM-dd HH:mm}";
        string ct = CheatTableBuilder.Build(title, rows);
        string defaultName = CheatTableBuilder.DefaultFileName(
            processName: "InterestingProperties", now);

        var skipped = skippedUnsupported + skippedMissingOffset;
        StatusText = skipped > 0
            ? $"Generated {rows.Count} entries (skipped {skipped} unsupported / malformed)."
            : $"Generated {rows.Count} entries.";

        RequestSaveCheatTable?.Invoke(defaultName, ct);
    }
}
