using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Console panel — surface every UFUNCTION(exec)
/// reachable in the target game and provide a one-click invoke flow.
///
/// Why this exists: many games ship debug / cheat entry points marked
/// with the UE <c>exec</c> specifier (FUNC_Exec = 0x00000200). The cooker
/// preserves these in Shipping builds (often inside UCheatManager
/// subclasses), so the developer's own `fly`, `ghost`, `god`,
/// `setspeed` etc. remain reachable at runtime. Discovering and
/// invoking them sidesteps the entire "find UFunction → build
/// ParamBuffer → invoke" workflow — the game author has already
/// curated the cheat surface for us.
///
/// Load uses the existing <c>list_all_functions</c> pipe command (same
/// payload as Interesting Funcs) and filters client-side to entries
/// with <see cref="AllFunctionEntry.IsExec"/> == true. GameOnly defaults
/// to <b>false</b> because UCheatManager and many engine-side exec
/// classes live in /Script/Engine; filtering them out by default would
/// hide most hits.
///
/// Run uses the existing <see cref="IDumpService.InvokeFunctionAsync"/>
/// path with classname-only resolution (DLL finds the first non-CDO
/// instance). Works without a ParamBuffer for no-arg exec commands —
/// the common case. For exec commands with parameters, the Run path
/// raises <see cref="RequestParameterInvoke"/> so MainWindow can open
/// the standard InvokeParamDialog flow.
/// </summary>
public partial class ConsoleViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;

    /// <summary>Full unfiltered list of exec UFunctions returned by the
    /// most recent Load. Filter operates against this and rebuilds
    /// <see cref="Results"/>.</summary>
    private List<AllFunctionEntry> _allExec = new();

    /// <summary>
    /// Sticky-instance cache: className → last successfully-resolved
    /// non-CDO instance address (hex string).
    ///
    /// Why: many exec commands are <b>stateful per-instance</b> —
    /// <c>UCheatManager::ToggleDebugCamera</c> flips its own
    /// <c>DebugCameraControllerRef</c> member (null → Enable, set →
    /// Disable); <c>god</c>/<c>slomo</c>/set-then-reset commands mutate
    /// the owning object. Such commands MUST land on the <b>same</b>
    /// UObject on every invoke. Plain classname resolution
    /// (<c>UE5_FindInstanceOfClass</c>) re-scans GObjects and returns
    /// "the first non-CDO instance" each call — but the very first invoke
    /// can mutate the object graph (e.g. EnableDebugCamera spawns an
    /// <c>ADebugCameraController</c>), so a later call may resolve a
    /// <i>different</i> instance whose toggle state is fresh → it silently
    /// re-runs Enable instead of Disable. The classic symptom is
    /// "Debug Camera turns on but won't turn off".
    ///
    /// Fix: pin the address the DLL actually used (echoed back as
    /// <c>instance_addr</c>) per class and reuse it on the next invoke of
    /// any exec on that class. Cleared on <see cref="LoadAsync"/> (a
    /// re-discover is treated as a fresh session — e.g. after reconnecting
    /// to a different game process) and self-heals on invoke failure
    /// (stale pin → drop + re-resolve, see <see cref="RunEntryAsync"/>).
    /// </summary>
    private readonly Dictionary<string, string> _stickyInstance =
        new(StringComparer.Ordinal);

    /// <summary>Cap on history entries — keeps the tail readable + the
    /// AOT-trimmed binary's ObservableCollection bounded. Old entries
    /// drop off the front when this is exceeded.</summary>
    private const int MaxHistoryEntries = 20;

    [ObservableProperty] private bool   _gameOnly = false;     // exec funcs often engine-side
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private bool   _isRunning;
    [ObservableProperty] private string _statusText =
        "Click Load to discover UFUNCTION(exec) commands in this game.";
    [ObservableProperty] private ObservableCollection<AllFunctionEntry> _results = new();
    [ObservableProperty] private AllFunctionEntry? _selectedResult;
    [ObservableProperty] private ObservableCollection<ConsoleHistoryEntry> _history = new();
    [ObservableProperty] private string _commandInput = "";

    // ── Debug Camera state-aware helper ────────────────────────────────
    // ToggleDebugCamera is a stateful toggle: it flips its CheatManager's
    // DebugCameraControllerRef (null → Enable, set → Disable). Blind Run
    // can't tell which direction it will go, so "turn it off" is
    // unreliable. These surface the live state (read from that very
    // member) and offer deterministic Force On / Force Off.

    /// <summary>True when the loaded exec list contains a
    /// <c>ToggleDebugCamera</c> command — drives visibility of the Debug
    /// Camera control row in the panel.</summary>
    [ObservableProperty] private bool _hasDebugCameraToggle;

    /// <summary>Live Debug Camera state for the badge: "ON" / "OFF" /
    /// "Unknown". Read from the CheatManager's DebugCameraControllerRef.</summary>
    [ObservableProperty] private string _debugCameraState = "Unknown";

    /// <summary>Badge foreground colour (hex string → IBrush via Avalonia's
    /// built-in converter, same pattern as ConsoleHistoryEntry.BadgeColor).</summary>
    [ObservableProperty] private string _debugCameraBadgeColor = "#888888";

    /// <summary>True while a Debug Camera probe / force is in flight —
    /// disables the row's buttons to prevent overlapping toggles.</summary>
    [ObservableProperty] private bool _isDebugCameraBusy;

    /// <summary>The discovered ToggleDebugCamera exec entry (class + func),
    /// captured at Load. Null when the game ships no such command.</summary>
    private AllFunctionEntry? _debugCamEntry;

    /// <summary>
    /// Raised when the user activates an exec command that has parameters
    /// (NumParms &gt; 0). MainWindow handler fetches full FunctionInfoModel
    /// via walk_functions then opens InvokeParamDialog so the user can
    /// supply values. No-arg exec commands run directly via
    /// <see cref="IDumpService.InvokeFunctionAsync"/> and don't fire this
    /// event.
    ///
    /// Payload is (className, funcName, classAddr). The address rides along
    /// because <c>list_all_functions</c> already supplies it per row: the
    /// handler used to re-derive it from <c>list_classes</c>, which returns a
    /// CAPPED page, so every exec on a class past the cap died on
    /// "Class X not found" (audit #5 X2).
    /// </summary>
    public event Action<string, string, string>? RequestParameterInvoke;

    /// <summary>Per-row "Live" action — same contract as Interesting
    /// Funcs. MainWindow tries find_instance → LiveWalker → ClassStruct
    /// fallback.</summary>
    public event Action<string, string>? NavigateToFunction;

    /// <summary>Per-row "AA(B)" action — same contract as Interesting
    /// Funcs. MainWindow handler fetches params + opens InvokeParamDialog
    /// in CopyBakedScript mode. Carries the row's class address for the
    /// same reason as <see cref="RequestParameterInvoke"/>.</summary>
    public event Action<string, string, string>? RequestCopyBakedScript;

    /// <summary>"Copy CE Script" on the Debug Camera row — MainWindow builds
    /// the [ENABLE]/[DISABLE] setDebugCamera memory-record script via
    /// <see cref="Services.DebugCameraScriptGenerator"/> and ships it to
    /// AOBMaker / clipboard. No args: the script is self-contained.</summary>
    public event Action? RequestDebugCameraCeScript;

    /// <summary>Per-session remembered filter keywords (LRU) surfaced as the
    /// filter box's AutoCompleteBox suggestions — see <see cref="KeywordSearchMemory"/>.</summary>
    private readonly KeywordSearchMemory _filterMemory;
    public ObservableCollection<string> FilterHistory => _filterMemory.History;

    public ConsoleViewModel(IDumpService dump, ILoggingService log)
    {
        _dump = dump;
        _log = log;
        _filterMemory = new KeywordSearchMemory(() => (FilterText, Results.Count > 0));
    }

    /// <summary>Drop exec discovery + the pinned per-class live instance addresses so
    /// a reconnect never invokes against the previous game's instances or shows its
    /// rows/history (audit X5). Client-side only. LoadAsync already clears
    /// <c>_stickyInstance</c> on a re-discover; this covers the disconnect gap.</summary>
    public void ClearOnDisconnect()
    {
        _allExec = new List<AllFunctionEntry>();
        SelectedResult = null;
        Results.Clear();
        History.Clear();
        _stickyInstance.Clear();     // live per-class instance addresses — process-scoped
        _debugCamEntry = null;
        HasDebugCameraToggle = false;
        SetDebugCameraState(null);
        StatusText = "Click Load to discover UFUNCTION(exec) commands in this game.";
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
        _filterMemory.Schedule(value);
    }

    /// <summary>
    /// When the user selects a row, refresh the
    /// <see cref="SelectedExecHint"/> binding so the cooker-strip
    /// warning footer toggles per-selection. Single notification —
    /// the hint is computed on demand from <see cref="SelectedResult"/>
    /// to keep this side-effect-free.
    /// </summary>
    partial void OnSelectedResultChanged(AllFunctionEntry? value)
        => OnPropertyChanged(nameof(SelectedExecHint));

    /// <summary>
    /// Footer-line hint shown below the status row when the currently-
    /// selected exec is likely to be one of the
    /// <c>#if !UE_BUILD_SHIPPING</c> stripped engine commands
    /// (UCheatManager::Fly/Ghost/God/Walk/Slomo/ChangeSize/Teleport
    /// etc., or a game-defined subclass). Empty otherwise — the panel
    /// binds visibility to non-empty.
    ///
    /// Detection is a cheap class-name / super-name substring match
    /// against "CheatManager"; catches the canonical engine class +
    /// the typical game-defined subclasses
    /// (<c>MyGameCheatManager</c>, <c>BP_CheatManager_C</c>) without
    /// needing a full super-chain walk. See memory
    /// <c>feedback_ucheatmanager_stripped</c> for the diagnostic
    /// rationale.
    /// </summary>
    public string SelectedExecHint
    {
        get
        {
            if (SelectedResult is null) return "";
            return IsLikelyUCheatManagerExec(SelectedResult)
                ? "⚠ UCheatManager subclasses are often body-stripped in cooked Shipping " +
                  "(Result=0 + no in-game effect). Try a game-specific exec or BC " +
                  "function for verification. See memory feedback_ucheatmanager_stripped."
                : "";
        }
    }

    /// <summary>
    /// True when <paramref name="entry"/>'s class or immediate super
    /// name contains the substring "CheatManager" (case-insensitive).
    /// Catches the engine class, game-defined subclasses, and BPGCs.
    /// Public + static for direct test coverage so the heuristic stays
    /// regression-proof without needing a VM lifecycle.
    /// </summary>
    public static bool IsLikelyUCheatManagerExec(AllFunctionEntry entry)
    {
        if (entry is null) return false;
        const string needle = "CheatManager";
        bool ClassMatches(string s) => !string.IsNullOrEmpty(s)
            && s.Contains(needle, StringComparison.OrdinalIgnoreCase);
        return ClassMatches(entry.ClassName) || ClassMatches(entry.SuperName);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            ClearError();
            IsLoading = true;
            // A re-discover is a fresh-session boundary: drop any pinned
            // instances so we don't carry stale addresses across a pipe
            // reconnect to a different game process.
            _stickyInstance.Clear();
            StatusText = "Scanning all UFunctions for exec flag (FUNC_Exec=0x200)...";

            var result = await _dump.ListAllFunctionsAsync(gameOnly: GameOnly);

            _allExec = await Task.Run(() =>
            {
                var list = new List<AllFunctionEntry>();
                foreach (var entry in result.Functions)
                {
                    if (entry.IsExec) list.Add(entry);
                }
                // Sort by Class then Func for deterministic UI ordering;
                // exec commands have no scoring concept (the engine flag
                // IS the curation signal).
                list.Sort((a, b) =>
                {
                    int cmp = string.Compare(a.ClassName, b.ClassName,
                                              StringComparison.Ordinal);
                    if (cmp != 0) return cmp;
                    return string.Compare(a.FuncName, b.FuncName,
                                           StringComparison.Ordinal);
                });
                return list;
            });

            ApplyFilter();
            DetectDebugCameraToggle();

            // A capped walk cannot support a claim about the GAME, only about the page.
            // `list_all_functions` stops at its row cap and, until audit #5 Z8, said
            // nothing about it — so "No UFUNCTION(exec) commands found in this game
            // (scanned 100,000 functions…)" was written from a scan that may never have
            // reached the classes in question, and the user stopped looking. The
            // wording below is a claim about the SCAN whenever the scan was partial.
            var capSuffix = result.Aborted
                ? PartialResultNotice.Cancelled("scan")
                : result.Truncated
                    ? PartialResultNotice.RowCap(result.Limit, "functions",
                          "tick \"Game classes only\" to skip engine classes so the cap "
                          + "reaches further into the game's own classes")
                    : "";

            if (_allExec.Count == 0)
            {
                StatusText = result.Total <= 0
                    ? "Load returned 0 functions — pipe issue?"
                    : result.IsPartial
                        // Partial scan: say what was searched, NOT what the game has.
                        ? $"No UFUNCTION(exec) commands in the {result.Total:N0} functions "
                          + $"scanned so far (from {result.ClassesWithFunctions:N0} classes) — "
                          + $"this scan did not finish, so it is not evidence the game has "
                          + $"none.{capSuffix}"
                        : $"No UFUNCTION(exec) commands found in this game "
                          + $"(scanned {result.Total:N0} functions from "
                          + $"{result.ClassesWithFunctions:N0} classes). The cooker "
                          + $"may have stripped them, or the game doesn't use "
                          + $"this pattern.";
            }
            else
            {
                StatusText = $"{_allExec.Count} exec commands discovered " +
                             $"({result.ScannedClasses:N0} classes scanned, " +
                             $"{result.Total:N0} total UFunctions).{capSuffix}";
            }

            _log.Info($"Console.Load: exec={_allExec.Count} of total={result.Total} " +
                      $"(gameOnly={GameOnly}, scanned={result.ScannedObjects:N0} objects, " +
                      $"truncated={result.Truncated}, aborted={result.Aborted})");
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Load failed";
            _log.Error("Console.Load failed", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Rebuild <see cref="Results"/> from <see cref="_allExec"/> applying
    /// the name substring filter. Order is preserved from the pre-sorted
    /// underlying list.
    /// </summary>
    private void ApplyFilter()
    {
        SelectedResult = null;   // detach before rebuilding the selection-bound list
        Results.Clear();
        if (_allExec.Count == 0) return;

        // Space-separated terms are ANDed (each must hit FuncName or ClassName),
        // so "add money" narrows to entries matching both — the shared Object Tree
        // filter semantics.
        var terms = ObjectTreeFilter.SplitTerms(FilterText);

        foreach (var entry in _allExec)
        {
            if (terms.Length > 0 &&
                !ObjectTreeFilter.MatchesAllTerms(terms, entry.FuncName, entry.ClassName))
            {
                continue;
            }
            Results.Add(entry);
        }
    }

    [RelayCommand]
    private void ClearFilter()
    {
        _filterMemory.Flush();   // commit a just-typed keyword before clearing the box (AE16)
        FilterText = "";
    }

    /// <summary>
    /// Run the currently selected exec command. No-arg commands invoke
    /// directly via <see cref="IDumpService.InvokeFunctionAsync"/>;
    /// commands with parameters raise <see cref="RequestParameterInvoke"/>
    /// so MainWindow can open the InvokeParamDialog flow.
    /// </summary>
    [RelayCommand]
    private async Task RunSelectedAsync()
    {
        var row = SelectedResult;
        if (row == null)
        {
            StatusText = "Pick an exec command from the list first.";
            return;
        }
        await RunEntryAsync(row);
    }

    /// <summary>
    /// Run the command typed in the textbox. Resolves against the
    /// discovered exec list by exact funcName match (case-insensitive).
    /// If multiple classes implement the same exec name (rare but
    /// possible), takes the first hit by class-then-func sort order.
    ///
    /// This is the "type the command and press Enter" workflow,
    /// equivalent to a UE in-game console line.
    /// </summary>
    [RelayCommand]
    private async Task RunCommandTextAsync()
    {
        var raw = (CommandInput ?? "").Trim();
        if (raw.Length == 0)
        {
            StatusText = "Type a command name first (e.g. 'fly' or 'god').";
            return;
        }

        // Strip optional leading slash so users typing "/fly" still work.
        if (raw.StartsWith('/')) raw = raw.Substring(1);

        // For v1 we only support no-arg matching. If the user types
        // "setspeed 5" we still resolve "setspeed" but warn that args
        // can't be parsed yet — they need to use the Run-selected flow
        // which opens InvokeParamDialog for typed params.
        var firstSpace = raw.IndexOf(' ');
        var commandName = firstSpace > 0 ? raw.Substring(0, firstSpace) : raw;
        var hasInlineArgs = firstSpace > 0;

        AllFunctionEntry? match = null;
        foreach (var entry in _allExec)
        {
            if (string.Equals(entry.FuncName, commandName,
                              StringComparison.OrdinalIgnoreCase))
            {
                match = entry;
                break;
            }
        }

        if (match == null)
        {
            StatusText = $"No exec command named '{commandName}' " +
                         $"(load the list first, or check spelling).";
            return;
        }

        if (hasInlineArgs && match.NumParms > 0)
        {
            StatusText = $"'{commandName}' takes {match.NumParms} param(s); " +
                         $"inline args not yet supported — opening param dialog.";
            RequestParameterInvoke?.Invoke(match.ClassName, match.FuncName, match.ClassAddr);
            return;
        }

        await RunEntryAsync(match);
    }

    /// <summary>
    /// Core run path — chosen entry → either direct invoke (no-arg) or
    /// param dialog (has args). Records the outcome in
    /// <see cref="History"/>.
    /// </summary>
    private async Task RunEntryAsync(AllFunctionEntry entry)
    {
        if (entry.NumParms > 0)
        {
            StatusText = $"{entry.FuncName} takes {entry.NumParms} param(s) — " +
                         $"opening param dialog.";
            RequestParameterInvoke?.Invoke(entry.ClassName, entry.FuncName, entry.ClassAddr);
            return;
        }

        try
        {
            ClearError();
            IsRunning = true;
            StatusText = $"Invoking {entry.ClassName}::{entry.FuncName}…";

            // Sticky instance: reuse the address the DLL last resolved for
            // this class so stateful toggles (ToggleDebugCamera, god, slomo…)
            // hit the SAME UObject on every invoke. See _stickyInstance for
            // the full "can't turn Debug Camera off" rationale.
            _stickyInstance.TryGetValue(entry.ClassName, out var pinnedAddr);
            bool usedPin = !string.IsNullOrEmpty(pinnedAddr);

            var result = await _dump.InvokeFunctionAsync(
                funcName: entry.FuncName,
                instanceAddr: usedPin ? pinnedAddr : null,
                className: entry.ClassName,
                parmsSize: 0,
                paramsHex: null,
                directCall: false);

            // A pinned address can go stale (object freed, level change, pipe
            // reconnect). If the pinned call failed, drop the pin and retry
            // once with a fresh classname resolution so a dead pin self-heals
            // instead of permanently breaking the command.
            bool reResolved = false;
            if (!result.Success && usedPin)
            {
                _stickyInstance.Remove(entry.ClassName);
                reResolved = true;
                usedPin = false;
                result = await _dump.InvokeFunctionAsync(
                    funcName: entry.FuncName,
                    instanceAddr: null,
                    className: entry.ClassName,
                    parmsSize: 0,
                    paramsHex: null,
                    directCall: false);
            }

            // Remember the resolved instance so the next toggle lands on the
            // same object. The DLL echoes the address it actually used.
            if (result.Success && !string.IsNullOrEmpty(result.InstanceAddr))
                _stickyInstance[entry.ClassName] = result.InstanceAddr;

            var resultText = result.Success
                ? (string.IsNullOrEmpty(result.Message) ? "OK" : result.Message)
                : (string.IsNullOrEmpty(result.Error)
                    ? $"Result code {result.Result}"
                    : result.Error);

            AppendHistory(entry, result.Success, resultText);

            // Surface which instance the call landed on, and whether it was a
            // pinned reuse / a self-heal re-resolve, so the user can tell a
            // toggle is hitting one stable object across on/off cycles.
            var pinNote = !string.IsNullOrEmpty(result.InstanceAddr)
                ? (reResolved ? $" (re-resolved {result.InstanceAddr})"
                   : usedPin  ? $" (pinned {result.InstanceAddr})"
                              : $" (instance {result.InstanceAddr})")
                : "";

            StatusText = result.Success
                ? $"✓ {entry.FuncName}: {resultText}{pinNote}"
                : $"✗ {entry.FuncName}: {resultText}{pinNote}";

            _log.Info($"Console.Run: {entry.ClassName}::{entry.FuncName} → " +
                      $"success={result.Success}, code={result.Result}, " +
                      $"instance={result.InstanceAddr}, usedPin={usedPin}, " +
                      $"reResolved={reResolved}");
        }
        catch (Exception ex)
        {
            AppendHistory(entry, success: false, ex.Message);
            SetError(ex);
            StatusText = $"✗ {entry.FuncName} failed: {ex.Message}";
            _log.Error($"Console.Run threw: {entry.ClassName}::{entry.FuncName}", ex);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void AppendHistory(AllFunctionEntry entry, bool success, string resultText)
    {
        History.Insert(0, new ConsoleHistoryEntry
        {
            When       = DateTime.Now,
            ClassName  = entry.ClassName,
            FuncName   = entry.FuncName,
            Success    = success,
            ResultText = resultText,
        });
        while (History.Count > MaxHistoryEntries)
        {
            History.RemoveAt(History.Count - 1);
        }
    }

    /// <summary>Per-row "Live" — try find_instance + open in LiveWalker;
    /// MainWindow falls back to ClassStruct on CDO-only classes.</summary>
    [RelayCommand]
    private void OpenInLiveWalker(AllFunctionEntry? row)
    {
        if (row == null) return;
        NavigateToFunction?.Invoke(row.ClassName, row.FuncName);
    }

    /// <summary>Per-row "AA(B)" — shortcut into the Copy AA Script flow,
    /// same contract as Interesting Funcs.</summary>
    [RelayCommand]
    private void CopyAaScript(AllFunctionEntry? row)
    {
        if (row == null) return;
        RequestCopyBakedScript?.Invoke(row.ClassName, row.FuncName, row.ClassAddr);
    }

    /// <summary>Re-run a history entry — looks up the same class+func in
    /// the discovered list and invokes again. If the load list has been
    /// cleared (re-launch), surfaces a hint to reload.</summary>
    [RelayCommand]
    private async Task ReplayHistoryAsync(ConsoleHistoryEntry? entry)
    {
        if (entry == null) return;
        AllFunctionEntry? match = null;
        foreach (var e in _allExec)
        {
            if (e.ClassName == entry.ClassName && e.FuncName == entry.FuncName)
            {
                match = e;
                break;
            }
        }
        if (match == null)
        {
            StatusText = $"Can't replay {entry.FuncName} — reload the list first.";
            return;
        }
        await RunEntryAsync(match);
    }

    /// <summary>Test seam — unit tests build a list of fixture
    /// AllFunctionEntry rows + a stub dump service, then call this to
    /// drive the same code path Load would after a real pipe call.
    /// Keeps tests free of the gameOnly/IsLoading state-machine
    /// concerns.</summary>
    public void SeedForTests(IEnumerable<AllFunctionEntry> entries)
    {
        var list = new List<AllFunctionEntry>();
        foreach (var e in entries)
        {
            if (e.IsExec) list.Add(e);
        }
        list.Sort((a, b) =>
        {
            int cmp = string.Compare(a.ClassName, b.ClassName,
                                      StringComparison.Ordinal);
            if (cmp != 0) return cmp;
            return string.Compare(a.FuncName, b.FuncName,
                                   StringComparison.Ordinal);
        });
        _allExec = list;
        ApplyFilter();
        DetectDebugCameraToggle();
    }

    // ──────────────────────────────────────────────────────────────────
    // Debug Camera state-aware helper
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scan the loaded exec list for a <c>ToggleDebugCamera</c> command and
    /// wire up the Debug Camera control row. Resets the badge to "Unknown"
    /// — the live state is read on demand via the ↻ / Force buttons (a Load
    /// can happen on a main menu with no live CheatManager).
    /// </summary>
    private void DetectDebugCameraToggle()
    {
        _debugCamEntry = null;
        foreach (var e in _allExec)
        {
            if (string.Equals(e.FuncName, "ToggleDebugCamera",
                              StringComparison.OrdinalIgnoreCase))
            {
                _debugCamEntry = e;
                break;
            }
        }
        HasDebugCameraToggle = _debugCamEntry != null;
        SetDebugCameraState(null);   // unknown until probed
    }

    /// <summary>Update the badge text + colour from a tri-state read
    /// (true=ON, false=OFF, null=Unknown).</summary>
    private void SetDebugCameraState(bool? on)
    {
        (DebugCameraState, DebugCameraBadgeColor) = on switch
        {
            true  => ("ON",      "#4EC9B0"),   // green — active
            false => ("OFF",     "#999999"),   // grey — inactive
            null  => ("Unknown", "#888888"),
        };
    }

    /// <summary>Map the DLL's tri-state Debug Camera result (1=on, 0=off,
    /// -1=unknown) onto the badge.</summary>
    private void ApplyDebugCameraState(int state)
        => SetDebugCameraState(state switch { 1 => true, 0 => false, _ => (bool?)null });

    /// <summary>↻ — re-read and display the live Debug Camera state. The
    /// two-hop reflection read lives DLL-side (get_debug_camera_state); this
    /// is a thin bridge so the UI and CE Lua share one implementation.</summary>
    [RelayCommand]
    private async Task RefreshDebugCameraStateAsync()
    {
        if (_debugCamEntry == null) return;
        try
        {
            ClearError();
            IsDebugCameraBusy = true;
            var state = await _dump.GetDebugCameraStateAsync();
            ApplyDebugCameraState(state);
            StatusText = state switch
            {
                1 => "Debug Camera is ON.",
                0 => "Debug Camera is OFF.",
                _ => "Debug Camera state unknown (no live CheatManager — " +
                     "enter gameplay so a PlayerController spawns, then ↻).",
            };
        }
        catch (Exception ex)
        {
            ApplyDebugCameraState(-1);
            SetError(ex);
            StatusText = $"Debug Camera state read failed: {ex.Message}";
            _log.Error("Console.RefreshDebugCameraState failed", ex);
        }
        finally
        {
            IsDebugCameraBusy = false;
        }
    }

    [RelayCommand]
    private Task ForceDebugCameraOnAsync()  => ForceDebugCameraAsync(wantOn: true);

    [RelayCommand]
    private Task ForceDebugCameraOffAsync() => ForceDebugCameraAsync(wantOn: false);

    /// <summary>"Copy CE Script" — hand off to MainWindow to generate the
    /// setDebugCamera [ENABLE]/[DISABLE] memory record and deliver it to
    /// AOBMaker / clipboard.</summary>
    [RelayCommand]
    private void CopyDebugCameraScript() => RequestDebugCameraCeScript?.Invoke();

    /// <summary>
    /// Deterministic enable/disable — delegates to the DLL (set_debug_camera),
    /// which reads state, toggles only when needed, and on a disable the game's
    /// stripped ToggleDebugCamera can't honour, switches the local player's
    /// controller back to the original PlayerController. Idempotent; returns
    /// the resulting state. UI and CE Lua share this one DLL implementation.
    /// </summary>
    private async Task ForceDebugCameraAsync(bool wantOn)
    {
        if (_debugCamEntry == null) return;
        var want = wantOn ? "ON" : "OFF";
        try
        {
            ClearError();
            IsDebugCameraBusy = true;
            var state = await _dump.SetDebugCameraAsync(wantOn);
            ApplyDebugCameraState(state);
            StatusText = state switch
            {
                1 when wantOn  => "✓ Debug Camera forced ON.",
                0 when !wantOn => "✓ Debug Camera forced OFF.",
                -1 => $"Force {want}: no live CheatManager / unreadable state " +
                      "(enter gameplay first).",
                _  => $"⚠ Force {want}: state is now {(state == 1 ? "ON" : "OFF")} " +
                      "— the game may re-drive the camera.",
            };
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = $"Force {want} failed: {ex.Message}";
            _log.Error($"Console.ForceDebugCamera({want}) failed", ex);
        }
        finally
        {
            IsDebugCameraBusy = false;
        }
    }
}
