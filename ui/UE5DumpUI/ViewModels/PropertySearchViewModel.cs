using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Property Search panel.
/// Searches property names + types across all UClass objects, plus a
/// client-side filter over already-fetched results so refining a hit
/// list doesn't need another DLL roundtrip.
/// </summary>
public partial class PropertySearchViewModel : ViewModelBase, IDisposable
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IAobMakerBridge? _aobMaker;
    private readonly IPlatformService? _platform;
    private readonly IExperimentalGate? _experimentalGate;

    /// <summary>
    /// Cooldown for live AOBMaker availability re-checks so rapid grid
    /// interactions don't flood the pipe. Mirrors LiveWalkerViewModel.
    /// </summary>
    private DateTime _lastAobMakerCheck = DateTime.MinValue;
    private static readonly TimeSpan AobMakerCheckCooldown = TimeSpan.FromSeconds(5);

    [ObservableProperty] private bool _isAobMakerAvailable;

    /// <summary>
    /// Tooltip shown on the Freeze button when it's disabled, explaining
    /// why. Refreshed alongside <see cref="IsAobMakerAvailable"/>.
    /// Matches the existing LiveWalker / InterestingFuncs Notes-column UX.
    /// </summary>
    public string FreezeUnavailableTooltip => IsAobMakerAvailable
        ? "Generate an AA Script that locks this property across all live instances"
        : "AOBMaker CE Plugin not found — Freeze requires the plugin so the AA Script can be injected into the open CE table. " +
          "Setup: copy AOBMaker DLL into Cheat Engine's autorun/ folder and restart CE.";

    partial void OnIsAobMakerAvailableChanged(bool value)
        => OnPropertyChanged(nameof(FreezeUnavailableTooltip));

    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _typeFilter = "";
    [ObservableProperty] private string _resultFilter = "";
    [ObservableProperty] private bool _gameClassesOnly = true;

    /// <summary>
    /// Opt-in deep descent: when on, the search ALSO walks nested struct
    /// members + struct-typed container elements (TArray/TSet&lt;FStruct&gt;,
    /// TMap&lt;K,FStruct&gt;) so a field buried at e.g.
    /// SaveSlotList[].MsTuneData.GP becomes findable by name. Default off
    /// because the schema descent is slower and surfaces synthetic dotted-path
    /// rows; the fast shallow direct-field search is the default behaviour.
    /// </summary>
    [ObservableProperty] private bool _deepSearch;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private ObservableCollection<PropertySearchMatch> _results = new();
    [ObservableProperty] private bool _isXrefBatchRunning;
    [ObservableProperty] private PropertySearchMatch? _selectedResult;

    /// <summary>
    /// Curated FProperty type names for the Type Filter autocomplete.
    /// Sorted alphabetically so the dropdown is predictable. Most users
    /// won't remember type spellings ("MulticastSparseDelegateProperty")
    /// — typing "sp", "del", "opt", "weak" etc. should narrow quickly.
    /// </summary>
    public string[] PropertyTypeSuggestions { get; } =
    [
        // Numerics
        "BoolProperty",
        "ByteProperty",
        "DoubleProperty",
        "FloatProperty",
        "Int8Property",
        "Int16Property",
        "Int64Property",
        "IntProperty",
        "UInt16Property",
        "UInt32Property",
        "UInt64Property",
        // Strings
        "NameProperty",
        "StrProperty",
        "TextProperty",
        // Pointers
        "ClassProperty",
        "InterfaceProperty",
        "ObjectProperty",
        // Weak / soft / lazy
        "LazyObjectProperty",
        "SoftClassProperty",
        "SoftObjectProperty",
        "WeakObjectProperty",
        // Containers
        "ArrayProperty",
        "MapProperty",
        "SetProperty",
        // Structs / enums
        "EnumProperty",
        "StructProperty",
        // Delegates
        "DelegateProperty",
        "MulticastDelegateProperty",
        "MulticastInlineDelegateProperty",
        "MulticastSparseDelegateProperty",
        // UE 5.2+ / rare
        "FieldPathProperty",
        "OptionalProperty",
    ];

    /// <summary>
    /// Full result set as last returned by the DLL — `Results` is a
    /// possibly-filtered view of this. Kept private so binding consumers
    /// always see the filtered subset.
    /// </summary>
    private List<PropertySearchMatch> _allResults = new();

    /// <summary>Debounce timer for client-side ResultFilter typing.</summary>
    private System.Threading.Timer? _resultFilterDebounce;
    /// <summary>Per-keyword LRU memory for the ResultFilter box. The match count is
    /// client-side (synchronous after ApplyResultFilter), so it uses Schedule, not Commit.</summary>
    private readonly KeywordSearchMemory _resultFilterMemory;
    public ObservableCollection<string> ResultFilterHistory => _resultFilterMemory.History;
    private bool _disposed;

    /// <summary>
    /// Event raised when user wants to find instances of a class in Instance Finder.
    /// </summary>
    public event Action<string>? NavigateToInstanceFinder;

    /// <summary>
    /// Event raised when user wants to navigate to a class address in Live Walker.
    /// </summary>
    public event Action<string>? NavigateToLiveWalker;

    /// <summary>Raised to pivot the selected property in the experimental Class
    /// Pivot tab (className, propName). C5 right-click handoff.</summary>
    public event Action<string, string>? NavigateToPivot;

    /// <summary>Gates the "Pivot this property" context-menu item — set true only
    /// when the experimental Class Pivot tab is available. Mirrors the gate so the
    /// handoff stays invisible when experimental features are off.</summary>
    [ObservableProperty] private bool _pivotEnabled;

    /// <summary>Client-side class-noise filter: hides ticked classes (UI widgets,
    /// sound, system components) from <see cref="Results"/>. Lives over the full
    /// <see cref="_allResults"/> set; ticking re-runs <see cref="ApplyResultFilter"/>.</summary>
    public ClassFacetFilter ClassFilter { get; }

    /// <summary>Live "N hidden by class filter" note (empty when nothing hidden) —
    /// lets the user tell an empty grid caused by a stale class exclusion from a
    /// genuine miss. Recomputed by <see cref="ApplyResultFilter"/>.</summary>
    [ObservableProperty] private string _classFilterNote = "";

    public PropertySearchViewModel(IDumpService dump, ILoggingService log,
                                   IAobMakerBridge? aobMaker = null,
                                   IPlatformService? platform = null,
                                   IExperimentalGate? experimentalGate = null)
    {
        _dump = dump;
        _log = log;
        _aobMaker = aobMaker;
        _platform = platform;
        _experimentalGate = experimentalGate;
        _resultFilterMemory = new KeywordSearchMemory(() => (ResultFilter, Results.Count > 0));
        if (_experimentalGate != null)
            _experimentalGate.Changed += OnExperimentalGateChanged;
        ClassFilter = new ClassFacetFilter(ApplyResultFilter)
        {
            AutoDetectProvider = async names =>
                (await _dump.DetectNoiseClassesAsync(names))
                    .Select(n => (n.ClassName, n.IsNoise, n.Reason)).ToList(),
        };
        // Seed the availability flag from the bridge's cached value so the
        // first paint of the panel isn't always "unavailable" — the actual
        // pipe probe happens lazily in RefreshAobMakerAvailabilityAsync.
        _isAobMakerAvailable = aobMaker?.IsAvailable ?? false;
    }

    /// <summary>
    /// Re-probe AOBMaker pipe so the Freeze button enablement reflects the
    /// current state. Called on tab activation + before each Freeze click;
    /// cooldown prevents pipe spam on rapid interactions.
    /// </summary>
    public async Task RefreshAobMakerAvailabilityAsync()
    {
        if (_aobMaker == null)
        {
            IsAobMakerAvailable = false;
            return;
        }
        var now = DateTime.UtcNow;
        if (now - _lastAobMakerCheck < AobMakerCheckCooldown)
            return;
        _lastAobMakerCheck = now;
        try
        {
            IsAobMakerAvailable = await _aobMaker.CheckAvailabilityAsync();
        }
        catch
        {
            IsAobMakerAvailable = false;
        }
    }

    /// <summary>
    /// Raised when the ViewModel wants the View to show a modal dialog to
    /// collect a freeze value. The View handles the actual dialog creation
    /// (so the VM stays View-free / testable) and resolves the task with the
    /// validated Lua literal, or null on cancel.
    /// </summary>
    public Func<PropertySearchMatch, Task<string?>>? FreezeValuePrompt { get; set; }

    /// <summary>
    /// Last status message from a freeze-script attempt. Bound to the
    /// existing <see cref="StatusText"/> for now — separated only so the
    /// freeze flow can overwrite the same chrome the search uses.
    /// </summary>
    [RelayCommand]
    private async Task CopyFreezeScriptAsync(PropertySearchMatch? match)
    {
        if (match == null) return;
        if (_aobMaker == null)
        {
            StatusText = "Freeze unavailable — no AOBMaker bridge wired";
            return;
        }
        if (!FreezeScriptGenerator.IsTypeSupported(match.PropType))
        {
            StatusText = $"Freeze v1 does not support {match.PropType} (numeric + bool only)";
            return;
        }
        if (FreezeValuePrompt == null)
        {
            StatusText = "Freeze unavailable — value prompt not wired";
            return;
        }

        // Refresh availability synchronously so the user sees fresh state
        // (the cooldown skips actual network if recent).
        await RefreshAobMakerAvailabilityAsync();
        if (!IsAobMakerAvailable)
        {
            StatusText = "AOBMaker plugin not reachable — script not sent";
            return;
        }

        string? literal;
        try
        {
            literal = await FreezeValuePrompt(match);
        }
        catch (Exception ex)
        {
            StatusText = $"Freeze dialog error: {ex.Message}";
            return;
        }
        if (literal == null) return;  // user cancelled

        var p = BuildFreezeParams(match, literal);
        var script = FreezeScriptGenerator.Generate(p);
        var description = $"Freeze: {p.ClassName}::{p.PropertyName} = {literal}";

        bool sent;
        try
        {
            sent = await _aobMaker.CreateAAScriptAsync(description, script, autoActivate: false);
        }
        catch (Exception ex)
        {
            sent = false;
            StatusText = $"Freeze send error: {ex.Message}";
            return;
        }
        finally
        {
            IsAobMakerAvailable = _aobMaker?.IsAvailable ?? false;
        }

        StatusText = sent
            ? $"Freeze script created in CE: {description}"
            : "Freeze script not sent — AOBMaker rejected the request";
    }

    /// <summary>
    /// Map one search-result row + the dialog's validated literal onto the generator's
    /// input bundle.
    ///
    /// <para><c>internal static</c> so a test can assert the mapping directly, rather
    /// than needing the AOBMaker bridge and a modal dialog. It carries
    /// <see cref="PropertySearchMatch.PropSize"/> through, which is the whole of audit
    /// #5 Y15: the engine-reported width used to stop here, so an
    /// <c>enum class : uint8</c> was frozen with a 4-byte writer. <paramref name="literal"/>
    /// was validated by <see cref="Views.FreezeValueDialog"/> against
    /// <see cref="Views.FreezeValueDialog.HelperTypeFor"/> of this same match, so the
    /// value and the writer agree only as long as this keeps passing the size.</para>
    /// </summary>
    internal static FreezeScriptParams BuildFreezeParams(PropertySearchMatch match, string literal)
        => new()
        {
            // Prefer the defining class — that's where the property is
            // actually declared; freezing on it covers all subclasses.
            ClassName      = !string.IsNullOrEmpty(match.DefiningClassName)
                             ? match.DefiningClassName
                             : match.ClassName,
            PropertyName   = match.PropName,
            PropertyOffset = match.PropOffset,
            UeTypeName     = match.PropType,
            PropertySize   = match.PropSize,
            // Audit #5 AA1: the FBoolProperty bit mask used to stop here too, so a
            // packed `uint8 bFoo:1` was frozen by stamping the whole byte — wiping
            // up to 7 sibling bools and, unless the mask was 0x01, never setting the
            // intended one. Same dropped-field shape as PropertySize above.
            BoolFieldMask  = match.BoolFieldMask,
            ValueLiteral   = literal,
        };

    // ── Force field (Solide) — hold a discovered field across live instances ──

    /// <summary>Whether the per-row Force actions are exposed. Gated behind the
    /// shared Experimental toggle (like SeeThrough) — these are combat-affecting
    /// live writes across a class's instances. When off the context submenu hides.</summary>
    public bool ForceEnabled => _experimentalGate?.IsEnabled ?? false;

    /// <summary>Whether to show the Force submenu at all: the experimental gate AND a
    /// selected row that can actually be forced. Both halves are needed and neither can
    /// carry the other — the gate alone left the submenu open with nothing usable in it,
    /// and the row alone would bypass the experimental gate. (B36)</summary>
    public bool ForceMenuVisible => ForceEnabled && (SelectedResult?.CanForceAny ?? false);

    partial void OnSelectedResultChanged(PropertySearchMatch? value)
        => OnPropertyChanged(nameof(ForceMenuVisible));

    private void OnExperimentalGateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ForceEnabled));
        OnPropertyChanged(nameof(ForceMenuVisible));
    }

    /// <summary>Active force-field holds (from get_forced_fields) — the "N held"
    /// honesty list shown at the panel bottom.</summary>
    public ObservableCollection<ForcedFieldInfo> ForcedFields { get; } = new();

    [ObservableProperty] private bool _hasForcedFields;

    /// <summary>View-injected numeric-value prompt (keeps the VM dialog-free /
    /// testable). Returns the value, or null on cancel.</summary>
    public Func<PropertySearchMatch, Task<double?>>? ForceValuePrompt { get; set; }

    /// <summary>View-injected confirm for the higher-risk "Force → null" (nulling a
    /// live object pointer the game may later dereference). Returns true to proceed.</summary>
    public Func<PropertySearchMatch, Task<bool>>? ForceNullConfirm { get; set; }

    [RelayCommand]
    private Task ForceBoolOn(PropertySearchMatch? m) => ApplyForceAsync(m, "bool", on: true);

    [RelayCommand]
    private Task ForceBoolOff(PropertySearchMatch? m) => ApplyForceAsync(m, "bool", on: false);

    [RelayCommand]
    private async Task ForceNull(PropertySearchMatch? m)
    {
        if (m == null) return;
        if (ForceNullConfirm != null && !await ForceNullConfirm(m)) return;
        await ApplyForceAsync(m, "object_null");
    }

    [RelayCommand]
    private async Task ForceNumeric(PropertySearchMatch? m)
    {
        if (m == null) return;
        if (ForceValuePrompt == null) { StatusText = "Force value prompt not wired"; return; }
        double? v;
        try { v = await ForceValuePrompt(m); }
        catch (Exception ex) { StatusText = $"Force dialog error: {ex.Message}"; return; }
        if (v == null) return;   // cancelled
        await ApplyForceAsync(m, "numeric", value: v.Value);
    }

    /// <summary>Force <paramref name="m"/>'s field to a value on all live instances
    /// of its class and hold it (DLL force_field). Uses the concrete matched
    /// <see cref="PropertySearchMatch.ClassName"/> (not the base defining class) so
    /// the pool scan stays targeted.</summary>
    private async Task ApplyForceAsync(PropertySearchMatch? m, string kind, double value = 0, bool on = false)
    {
        if (m == null || string.IsNullOrEmpty(m.ClassName) || string.IsNullOrEmpty(m.PropName)) return;
        try
        {
            var r = await _dump.ForceFieldAsync(m.ClassName, m.PropName, kind, value, on);
            if (r.Code < 0)
                StatusText = $"Force {m.PropName}: DLL error {r.Code}.";
            else if (r.Held == 0)
                StatusText = $"Force {m.PropName}: 0 live instances of {m.ClassName} matched — nothing held.";
            else
            {
                var what = kind == "bool" ? (on ? "ON" : "OFF")
                         : kind == "object_null" ? "null"
                         : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                // A capped pool makes Held a floor, not a total. Say so — otherwise
                // "on 256 instance(s)" reads as "all of them" when it means "the first
                // 256 we walked, in ascending GObjects order, and there are more".
                StatusText = r.Truncated
                    ? $"✓ Holding {m.ClassName}::{m.PropName} = {what} on {r.Held} instance(s) — cap reached, more exist unheld."
                    : $"✓ Holding {m.ClassName}::{m.PropName} = {what} on {r.Held} instance(s).";
            }
            await RefreshForcedFieldsAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Force {m.PropName} failed: {ex.Message}";
            _log.Error($"PropertySearch Force({m.PropName}) failed", ex);
        }
    }

    /// <summary>Re-read the active holds from the DLL into <see cref="ForcedFields"/>.</summary>
    public async Task RefreshForcedFieldsAsync()
    {
        try
        {
            var fields = await _dump.GetForcedFieldsAsync();
            ForcedFields.Clear();
            foreach (var f in fields) ForcedFields.Add(f);
            HasForcedFields = ForcedFields.Count > 0;
        }
        catch (Exception ex)
        {
            _log.Error("PropertySearch RefreshForcedFields failed", ex);
        }
    }

    [RelayCommand]
    private async Task RemoveForcedField(ForcedFieldInfo? f)
    {
        if (f == null) return;
        try
        {
            await _dump.ResetFieldAsync(f.ClassName, f.FieldName);
            StatusText = $"Released {f.ClassName}::{f.FieldName}.";
            await RefreshForcedFieldsAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Release failed: {ex.Message}";
            _log.Error("PropertySearch RemoveForcedField failed", ex);
        }
    }

    [RelayCommand]
    private async Task ClearAllForcedFields()
    {
        try
        {
            await _dump.ResetAllFieldsAsync();
            StatusText = "Released all forced fields.";
            await RefreshForcedFieldsAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Release-all failed: {ex.Message}";
            _log.Error("PropertySearch ClearAllForcedFields failed", ex);
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var trimmedQuery = (SearchQuery ?? "").Trim();
        // Parse comma-separated type filter; whitespace-only entries dropped.
        var types = (TypeFilter ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // DLL requires at least one constraint — name OR type.
        if (string.IsNullOrEmpty(trimmedQuery) && types.Length == 0)
        {
            StatusText = "Enter a property name or type filter";
            return;
        }

        try
        {
            ClearError();
            IsSearching = true;
            StatusText = "Searching...";

            var result = await _dump.SearchPropertiesAsync(
                trimmedQuery,
                types: types.Length > 0 ? types : null,
                gameOnly: GameClassesOnly,
                deep: DeepSearch);

            // Cache the full set so the client-side ResultFilter can refine
            // without another DLL roundtrip.
            _allResults = new List<PropertySearchMatch>(result.Results);
            // Build the class histogram over the FULL result, then filter.
            ClassFilter.Rebuild(_allResults.Select(m => m.ClassName));
            ApplyResultFilter();

            var typeSuffix = types.Length > 0 ? $" [types: {string.Join(",", types)}]" : "";
            var deepSuffix = DeepSearch ? " [deep]" : "";
            // The cap/abort suffix is the whole point of D5/F4: without it a capped
            // search reads as a completed sweep, the user filters these rows client-
            // side, finds nothing, and concludes the field does not exist. The DLL
            // now reports what it actually walked, so the counts no longer contradict
            // the suffix. (Built here rather than in en.axaml to match the existing
            // StatusText line it extends.)
            var capSuffix = result.Aborted
                ? "  ⚠ SCAN CANCELLED - this list is partial"
                : result.Truncated
                    ? $"  ⚠ STOPPED at the {result.Total}-row cap - more matches exist, narrow the query or raise Max"
                    : "";
            StatusText = $"Found {result.Total} properties in {result.ScannedClasses:N0} classes (scanned {result.ScannedObjects:N0} objects){deepSuffix}{capSuffix}";
            _log.Info($"SearchProperties: '{trimmedQuery}'{typeSuffix} -> {result.Total} results (classes={result.ScannedClasses}, objects={result.ScannedObjects})");
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Search failed";
            _log.Error($"SearchProperties failed for '{SearchQuery}'", ex);
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>
    /// Debounced — refilter the locally-cached _allResults whenever the
    /// user types in the client-side filter. Avoids per-keystroke
    /// rebuilding of the ObservableCollection on large result sets.
    /// </summary>
    partial void OnResultFilterChanged(string value)
    {
        if (_disposed) return;
        _resultFilterDebounce?.Dispose();
        _resultFilterDebounce = new System.Threading.Timer(
            _ => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyResultFilter),
            null, 150, Timeout.Infinite);
        // Remember keywords that produce matches (client-side count is synchronous
        // after the debounced ApplyResultFilter — 700 ms memory delay > 150 ms filter).
        _resultFilterMemory.Schedule(value);
    }

    /// <summary>
    /// Rebuild Results from _allResults, applying ResultFilter with <b>space = AND</b>
    /// (term-level AND, field-level OR) across Class / Property / Type / Super / Preview
    /// via <see cref="ObjectTreeFilter.MatchesAllTerms(string[], string?[])"/> — the shared
    /// keyword-box semantics. Called on every search completion AND on each filter change.
    /// Internal so the AND behaviour has a deterministic unit test (no debounce).
    /// </summary>
    internal void ApplyResultFilter()
    {
        var terms = ObjectTreeFilter.SplitTerms(ResultFilter);
        SelectedResult = null;   // detach before rebuilding the selection-bound list
        Results.Clear();

        int hiddenByClass = 0;
        foreach (var m in _allResults)
        {
            // space = AND: every term must match at least one visible column.
            if (terms.Length > 0 &&
                !ObjectTreeFilter.MatchesAllTerms(terms, m.ClassName, m.PropName, m.PropType, m.SuperName, m.Preview))
                continue;
            // Class-noise exclusion last, so the count reflects rows that would
            // otherwise be visible.
            if (ClassFilter.IsExcluded(m.ClassName)) { hiddenByClass++; continue; }
            Results.Add(m);
        }
        ClassFilterNote = hiddenByClass > 0 ? $"{hiddenByClass} hidden by class filter" : "";
    }

    [RelayCommand]
    private void FindInstances(PropertySearchMatch? match)
    {
        if (match == null) return;
        NavigateToInstanceFinder?.Invoke(match.ClassName);
    }

    [RelayCommand]
    private void OpenInWalker(PropertySearchMatch? match)
    {
        if (match == null || string.IsNullOrEmpty(match.ClassAddr)) return;
        NavigateToLiveWalker?.Invoke(match.ClassAddr);
    }

    [RelayCommand]
    private void PivotThis(PropertySearchMatch? match)
    {
        match ??= SelectedResult;
        if (match == null || string.IsNullOrEmpty(match.ClassName)) return;
        NavigateToPivot?.Invoke(match.ClassName, match.PropName);
    }

    [RelayCommand]
    private void CopyOffset(PropertySearchMatch? match)
    {
        if (match == null) return;
        Avalonia.Input.Platform.IClipboard? clipboard = null;
        if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            clipboard = desktop.MainWindow?.Clipboard;
        }
        clipboard?.SetTextAsync(match.OffsetHex);
    }

    /// <summary>
    /// "Find functions using this field" — static Kismet-bytecode xref for
    /// the matched property's FProperty*. Opens the shared self-contained
    /// dialog (no tab impact).
    /// </summary>
    [RelayCommand]
    private async Task FindFieldXrefsAsync(PropertySearchMatch? match)
    {
        match ??= SelectedResult;
        if (match == null || string.IsNullOrEmpty(match.FieldAddr)) return;
        try
        {
            await Views.PropertyXrefDialog.ShowForFieldAsync(
                match.PropName, match.PropType, match.FieldAddr, _dump, _platform);
        }
        catch (Exception ex)
        {
            _log.Error($"FindFieldXrefs failed for {match.PropName}", ex);
        }
    }

    // Find Funcs is the EXPENSIVE direction — each call is a full game-wide
    // bytecode sweep — so warn past a low row count and update progress per row.
    private CancellationTokenSource? _xrefBatchCts;

    /// <summary>
    /// Batch "Find Funcs": run find_property_xrefs for the selected rows (or all
    /// results if none selected) and write a compact "N · func1, func2" summary
    /// into each match's inline <see cref="PropertySearchMatch.XrefInfo"/> cell.
    /// Cancellable. Nested (deep) rows carry a leaf FieldAddr, so they're included.
    /// </summary>
    [RelayCommand]
    private async Task BatchFindFuncsAsync(IList<PropertySearchMatch>? selected)
    {
        var targets = ((selected != null && selected.Count > 0) ? selected : (IList<PropertySearchMatch>)Results)
            .Where(m => !string.IsNullOrEmpty(m.FieldAddr)).ToList();
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

        var oldCts = _xrefBatchCts;          // dispose the prior run's CTS (L14)
        _xrefBatchCts = new CancellationTokenSource();
        oldCts?.Cancel();
        oldCts?.Dispose();
        var ct = _xrefBatchCts.Token;
        IsXrefBatchRunning = true;
        int done = 0, withFuncs = 0, cached = 0;
        try
        {
            foreach (var match in targets)
            {
                ct.ThrowIfCancellationRequested();
                // Skip rows already scanned (XrefInfo persists across filter changes).
                if (!string.IsNullOrEmpty(match.XrefInfo)) { cached++; continue; }
                try
                {
                    var res = await _dump.FindPropertyXrefsAsync(match.FieldAddr, true, 200, ct);
                    match.XrefInfo = XrefFormat.FunctionsSummary(res.Xrefs);
                    if (res.Xrefs.Count > 0) withFuncs++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    match.XrefInfo = "—";
                    _log.Error($"Batch Find Funcs failed for {match.PropName}", ex);
                }
                done++;
                StatusText = $"Find Funcs: {done}/{targets.Count} scanned ({withFuncs} referenced)…";
            }
            StatusText = $"Find Funcs done: {withFuncs}/{targets.Count} referenced by a function"
                       + (cached > 0 ? $" ({cached} cached)." : ".");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            StatusText = $"Find Funcs cancelled at {done}/{targets.Count}.";
        }
        catch (Exception ex)   // incl. a bare disconnect-OCE (token NOT cancelled) (L14)
        {
            _log.Error("Batch Find Funcs failed", ex);
            StatusText = $"Find Funcs failed at {done}/{targets.Count}.";
        }
        finally { IsXrefBatchRunning = false; }
    }

    /// <summary>Cancel an in-flight batch Find Funcs run.</summary>
    [RelayCommand]
    private void CancelXrefBatch() => _xrefBatchCts?.Cancel();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_experimentalGate != null)
            _experimentalGate.Changed -= OnExperimentalGateChanged;
        _xrefBatchCts?.Cancel();
        _xrefBatchCts?.Dispose();
        _resultFilterDebounce?.Dispose();
        _resultFilterDebounce = null;
        _resultFilterMemory.Dispose();
        GC.SuppressFinalize(this);
    }
}
