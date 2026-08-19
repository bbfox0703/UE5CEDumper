using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Class Structure panel.
/// </summary>
public partial class ClassStructViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService _platform;

    [ObservableProperty] private string _className = "";
    [ObservableProperty] private string _classPath = "";
    [ObservableProperty] private string _superName = "";
    [ObservableProperty] private int _propertiesSize;
    [ObservableProperty] private ObservableCollection<FieldInfoModel> _fields = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasClass;
    /// <summary>UClass* of the currently loaded class — for the per-class Find Func.</summary>
    [ObservableProperty] private string _loadedClassAddr = "";

    /// <summary>Field row the user right-clicked (drives the xref context menu).</summary>
    [ObservableProperty] private FieldInfoModel? _selectedField;

    /// <summary>Client-side filter text (substring over field name + type).</summary>
    [ObservableProperty] private string _fieldFilter = "";

    /// <summary>Full unfiltered field set; <see cref="Fields"/> is the
    /// filtered view rebuilt by <see cref="ApplyFieldFilter"/>.</summary>
    private readonly List<FieldInfoModel> _allFields = new();

    /// <summary>Per-session remembered filter keywords (LRU) surfaced as the
    /// field-filter box's AutoCompleteBox suggestions — see <see cref="KeywordSearchMemory"/>.</summary>
    private readonly KeywordSearchMemory _filterMemory;
    public ObservableCollection<string> FieldFilterHistory => _filterMemory.History;

    /// <summary>
    /// True when a class is loaded but has zero instance fields. The
    /// canonical example is <c>BlueprintFunctionLibrary</c> subclasses
    /// (e.g. <c>GameplayLib</c>) -- pure utility classes whose only
    /// content is static methods. Without this hint the user sees an
    /// empty DataGrid after a cross-tab fallback from Interesting Funcs
    /// and can't tell "broken load" from "this class genuinely has no
    /// fields". UI binds this to a help banner.
    /// </summary>
    public bool HasNoFields => HasClass && !IsLoading && _allFields.Count == 0;

    partial void OnHasClassChanged(bool value)   => OnPropertyChanged(nameof(HasNoFields));
    partial void OnIsLoadingChanged(bool value)  => OnPropertyChanged(nameof(HasNoFields));
    partial void OnFieldFilterChanged(string value)
    {
        ApplyFieldFilter();
        _filterMemory.Schedule(value);
    }

    /// <summary>Rebuild <see cref="Fields"/> from <see cref="_allFields"/>,
    /// applying <see cref="FieldFilter"/> as whitespace-separated AND terms
    /// (each term must hit field name OR type). Field lists are small
    /// (hundreds), so no debounce.</summary>
    private void ApplyFieldFilter()
    {
        var terms = ObjectTreeFilter.SplitTerms(FieldFilter);
        // Detach before clearing the selection-bound grid (audit #5 AE14). This was
        // the one panel of five missing the line its siblings carry verbatim —
        // ConsoleViewModel:341, GameClassFilterViewModel:151,
        // InterestingFunctionsViewModel:587, InterestingPropertiesViewModel:448.
        // Avalonia's DataGrid keeps SelectedItem pointing at a row that is no longer
        // in ItemsSource, so the xref context menu and the "Find Funcs" column kept
        // acting on a FieldInfoModel the grid had stopped showing.
        SelectedField = null;
        Fields.Clear();
        foreach (var f in _allFields)
        {
            if (terms.Length == 0
                || ObjectTreeFilter.MatchesAllTerms(terms, f.Name, f.TypeName))
            {
                Fields.Add(f);
            }
        }
    }

    /// <summary>
    /// The tree node whose class the panel is showing <b>or is loading</b>, as of
    /// the newest request. <c>null</c> when the content did not come from a tree
    /// selection (a cross-tab handoff) or when the newest tree-driven load failed
    /// or loaded nothing.
    ///
    /// Renamed from <c>_lastLoadedNodeAddress</c> (audit #5 AE3), because that name
    /// was the false premise: it is written when a load is CLAIMED, so it names an
    /// attempt, not an accomplished display. Read as "last LOADED", the natural
    /// repair is to move the write after the walk — which would break the dedupe
    /// this exists for, since a duplicate arriving while the first walk is still in
    /// flight would no longer be caught.
    ///
    /// Written only via <see cref="BeginLoad"/>, and released again by whichever
    /// path ends up showing nothing new, so a failed load can always be retried by
    /// re-selecting the same node.
    /// </summary>
    private string? _shownNodeAddress;

    /// <summary>
    /// Claim the panel for a new request: record whose it is and take the next
    /// ticket. Both entry points go through here, which makes "you cannot take a
    /// ticket without recording whose panel this is" true by construction.
    /// </summary>
    /// <param name="nodeAddr">
    /// The tree node this load is for, or <c>null</c> for a cross-tab load — which
    /// deliberately shows a class that no tree node selected, so the key must be
    /// CLEARED rather than left naming the previously selected node. Leaving it
    /// stale is AE3's third path and needs neither a failure nor any concurrency:
    /// two ordinary clicks pin the panel.
    /// </param>
    private int BeginLoad(string? nodeAddr)
    {
        _shownNodeAddress = nodeAddr;
        return ++_loadId;
    }

    /// <summary>
    /// Monotonic ticket for "which request owns the panel". Same idiom (and
    /// spelling family) as <c>ObjectTreeViewModel._loadGen</c>,
    /// <c>InstanceFinderViewModel._fieldLoadId</c> and
    /// <c>ClassPivotViewModel._classLoadId</c>; plain <c>++</c> like all three,
    /// because every entry point is reached on the UI thread.
    ///
    /// Why it is needed here, which is NOT guessable from the code (audit #5 AE2):
    /// nothing upstream serializes this handler. <c>ObjectTreeViewModel</c> raises
    /// <c>SelectionChanged</c> as a bare <c>Action</c>, so MainWindowViewModel's
    /// <c>async</c> subscriber returns to the message loop at its first await and
    /// the next selection runs straight into <see cref="OnObjectSelected"/>.
    /// <c>AsyncRelayCommand</c> is no help either: <c>CanExecute</c> goes false
    /// while running (so a bound Button self-disables) but <c>ExecuteAsync</c>
    /// runs anyway — measured build 3038, see <c>ProxyDeployConcurrencyTests</c>.
    ///
    /// The losing pair is specifically **instance-then-class-like**, and it loses
    /// by ORDERING rather than by timing: the two branches of
    /// <see cref="OnObjectSelected"/> issue a different NUMBER of round-trips
    /// (an instance needs <c>get_object</c> first, a UClass does not) over one
    /// strictly FIFO pipe lane, so the older gesture's walk is issued third and
    /// answered third — deterministically last.
    ///
    /// ⚠ The ticket MUST be claimed here at GESTURE time, not inside
    /// <see cref="LoadClassAsync"/>. Claiming it in the command inverts the fix:
    /// the stale instance selection ENTERS the command last (its <c>get_object</c>
    /// hop delays entry), so it would take the HIGHEST ticket and win legitimately.
    /// </summary>
    private int _loadId;

    public ClassStructViewModel(IDumpService dump, ILoggingService log, IPlatformService platform)
    {
        _dump = dump;
        _log = log;
        _platform = platform;
        _filterMemory = new KeywordSearchMemory(() => (FieldFilter, Fields.Count > 0));
    }

    /// <summary>Drop the currently-shown class so a reconnect never displays fields
    /// (and a UClass* address) from the previous game (audit X5). Bumps the load
    /// ticket so an in-flight WalkClass can't repaint after this. Client-side only.</summary>
    public void ClearOnDisconnect()
    {
        _loadId++;                 // supersede any in-flight WalkClass repaint
        _shownNodeAddress = null;
        SelectedField = null;
        _allFields.Clear();
        Fields.Clear();
        ClassName = "";
        ClassPath = "";
        SuperName = "";
        PropertiesSize = 0;
        LoadedClassAddr = "";
        HasClass = false;
    }

    /// <summary>
    /// "Find functions using this field" — static Kismet-bytecode cross-reference
    /// for the field's FProperty*. Opens a self-contained dialog (no tab impact).
    /// </summary>
    [RelayCommand]
    private async Task FindFieldXrefsAsync(FieldInfoModel? field)
    {
        field ??= SelectedField;
        if (field == null || string.IsNullOrEmpty(field.Address) || field.Address == "0x0")
            return;

        try
        {
            await Views.PropertyXrefDialog.ShowForFieldAsync(
                field.Name, field.TypeName, field.Address, _dump, _platform);
            _log.Info($"FindFieldXrefs dialog closed for {ClassName}.{field.Name} ({field.Address})");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"FindFieldXrefs failed for {field.Name}", ex);
        }
    }

    /// <summary>"Find Class Funcs": which UFunctions take this whole class as a
    /// parameter or return value (find_functions_by_class — reflection, native
    /// functions included). Distinct from the per-FIELD "Find Funcs" column.
    ///
    /// The try/catch is copied verbatim from <see cref="FindFieldXrefsAsync"/>
    /// directly above — same feature, same call shape, and this method had none
    /// at all (audit #5 AE11). <c>find_functions_by_class</c> is a bulk-lane
    /// command that fails on pipe teardown, a <c>Tot::Requested()</c> abort or a
    /// stale <see cref="LoadedClassAddr"/>; without the catch the dialog threw,
    /// the panel's ErrorMessage line stayed blank and nothing reached
    /// <c>view-*.log</c> either — a button that silently did nothing and left no
    /// trace to grep for.</summary>
    [RelayCommand]
    private async Task FindClassFuncAsync()
    {
        if (string.IsNullOrEmpty(LoadedClassAddr)) return;
        try
        {
            await Views.PropertyXrefDialog.ShowForClassAsync(
                ClassName, LoadedClassAddr, _dump, _platform);
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"FindClassFunc failed for {ClassName} ({LoadedClassAddr})", ex);
        }
    }

    /// <summary>
    /// Cross-tab entry point ("show me this class"), bound as
    /// <c>LoadClassCommand</c> and invoked from five handoff sites in
    /// MainWindowViewModel as well as from <see cref="OnObjectSelected"/>.
    /// Signature deliberately unchanged so those callers keep compiling.
    /// </summary>
    [RelayCommand]
    private async Task LoadClassAsync(string? classAddr)
    {
        // Order is load-bearing: reject the no-op BEFORE claiming a ticket.
        // A request that does no work must not supersede a live load — it would
        // bail without ever reaching the `finally`, leaving the spinner owned by
        // a ticket nobody will retire. InstanceFinderViewModel documents the same
        // rule on its null branch.
        if (string.IsNullOrEmpty(classAddr) || classAddr == "0x0") return;

        await LoadClassCoreAsync(classAddr, BeginLoad(nodeAddr: null));
    }

    /// <summary>
    /// The actual walk + panel write, executed on behalf of ticket
    /// <paramref name="gen"/>. Every write to the panel is gated on that ticket
    /// still being the newest, so a superseded request returns silently instead
    /// of repainting over the selection the user actually made (audit #5 AE2).
    /// Four guard points, ported from <c>InstanceFinderViewModel</c>'s
    /// <c>LoadInstanceFieldsAsync</c> — the one site in this repo that guards all
    /// four (success write, failure write, the loading flag, and the early exit).
    /// </summary>
    private async Task LoadClassCoreAsync(string classAddr, int gen)
    {
        try
        {
            // Both of these precede the only await, so no stale request can reach
            // them — a superseded load can never wipe a newer error or spinner.
            ClearError();
            IsLoading = true;

            var ci = await _dump.WalkClassAsync(classAddr);
            if (gen != _loadId) return;   // a newer selection / handoff superseded us

            ClassName = ci.Name;
            ClassPath = ci.FullPath;
            SuperName = ci.SuperName;
            PropertiesSize = ci.PropertiesSize;
            LoadedClassAddr = classAddr;
            HasClass = true;

            _allFields.Clear();
            _allFields.AddRange(ci.Fields);
            ApplyFieldFilter();
            // Fields.Count change doesn't fire HasNoFields; nudge it. (Note this
            // particular nudge is inert — HasNoFields also requires !IsLoading,
            // still true here — the real notification arrives via
            // OnIsLoadingChanged when the finally clears it. Kept because the
            // dependency it names is genuine; harmless either way.)
            OnPropertyChanged(nameof(HasNoFields));

            _log.Info($"Loaded class: {ci.Name} ({ci.Fields.Count} fields)");
        }
        catch (Exception ex)
        {
            // Log unconditionally — a superseded request that failed is still a
            // real diagnostic — but only the newest may PAINT the failure, or a
            // stale error banner lands over a panel that loaded fine.
            _log.Error($"Failed to load class at {classAddr}", ex);
            if (gen != _loadId) return;
            // Nothing new is on screen, so release the dedupe key — otherwise
            // re-selecting the same node is refused forever and the panel stays
            // pinned on whatever it happened to be showing (audit #5 AE3).
            _shownNodeAddress = null;
            SetError(ex);
        }
        finally
        {
            if (gen == _loadId)   // only the latest load owns the flag
                IsLoading = false;
        }
    }

    /// <summary>
    /// Called when a UObject is selected in the ObjectTree — loads its class.
    ///
    /// Disposition:
    ///   - If the clicked node IS a class-like UObject (UClass /
    ///     UScriptStruct / UEnum / UFunction or any subclass thereof),
    ///     walk its address DIRECTLY. Going through GetObjectAsync would
    ///     return its metaclass (UClass-of-Class, UClass-of-ScriptStruct,
    ///     etc.) whose FProperty chain is empty in UE — that produced the
    ///     "shows /Script/CoreUObject/Class with 0 fields" bug the user
    ///     reported on LocalPlayer.
    ///   - Otherwise it's an instance — fetch its UClass via get_object.
    ///
    /// A null `node` does NOT blank the panel: Avalonia's ListBox raises
    /// SelectionChanged with null whenever its ItemsSource collection
    /// changes (filter typing, a fresh load, suggestion auto-selection).
    /// We keep the last successfully-loaded class visible until another
    /// real selection arrives.
    ///
    /// We also dedupe consecutive selections of the same node, which is
    /// reachable mainly as node → null → same-node: <c>ApplyFilter</c> nulls
    /// <c>SelectedNode</c> on every debounced filter keystroke, and a reload
    /// can hand back a different <see cref="UObjectNode"/> instance for the
    /// same address. The key is claimed BEFORE the load, so a duplicate that
    /// arrives while the first walk is still in flight is caught too — and it
    /// is RELEASED by any path that ends up showing nothing new, so a failed
    /// load can always be retried by re-selecting the same node (audit #5 AE3).
    ///
    /// The <c>node == null</c> early return deliberately does NOT supersede an
    /// in-flight load. Contrast <c>InstanceFinderViewModel</c>, which does bump
    /// its counter on its null branch — there a null is a real deselect, here it
    /// is filter-typing noise, and superseding would cancel a legitimate load on
    /// every character typed. Do not "harmonise" the two.
    /// </summary>
    public async Task OnObjectSelected(UObjectNode? node)
    {
        if (node == null) return;
        // Dedupe. `&& HasClass` used to be here; it was dropped with AE3, because
        // its only job was keeping a COLD-start failure retryable and the key is
        // now released on every failure instead. Keeping it preserved two defects:
        // HasClass is assigned in exactly one place in all of ui/ and never reset,
        // so after the first success it is permanently true and armed the pin; and
        // while the FIRST walk is still in flight it is still false, so a
        // node -> null -> node re-fire fell through and issued a second walk for
        // the same class, whose earlier-finishing load then retired the spinner.
        if (_shownNodeAddress == node.Address) return;

        // Claim the panel for THIS gesture, before any await. See _loadId — doing
        // it here rather than inside LoadClassAsync is what makes the guard work
        // at all, because the losing request is the one that enters the command
        // LAST.
        int gen = BeginLoad(node.Address);

        try
        {
            ClearError();

            string classAddr;
            if (IsClassLikeNode(node.ClassName))
            {
                // The clicked object is itself a UClass / UScriptStruct /
                // UEnum / UFunction — walk it directly.
                classAddr = node.Address;
            }
            else
            {
                // Instance: walk its UClass. Fall back to the object
                // address only if the metaclass lookup fails.
                var detail = await _dump.GetObjectAsync(node.Address);
                // Superseded while we were resolving the metaclass. Bail BEFORE
                // issuing the walk, so the stale round-trip is never put on the
                // wire at all — this is the exact asymmetry AE2 describes, and
                // skipping it saves a hop on a contended lane rather than adding one.
                if (gen != _loadId) return;
                classAddr = detail.ClassAddr;
                if (string.IsNullOrEmpty(classAddr) || classAddr == "0x0")
                    classAddr = node.Address;
            }

            await LoadClassCoreAsync(classAddr, gen);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to load class for object at {node.Address}", ex);
            if (gen != _loadId) return;   // stale failure — don't paint over a newer panel
            _shownNodeAddress = null;     // retryable: see the release in LoadClassCoreAsync
            SetError(ex);
        }
    }

    /// <summary>
    /// True when the node's class name indicates the node IS itself a
    /// walkable type definition (UClass family, UScriptStruct family,
    /// UEnum family, UFunction family) rather than a runtime instance.
    /// </summary>
    private static bool IsClassLikeNode(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        // Any UClass-derived: Class, BlueprintGeneratedClass,
        // WidgetBlueprintGeneratedClass, AnimBlueprintGeneratedClass, etc.
        if (className.EndsWith("Class", StringComparison.Ordinal)) return true;
        return className switch
        {
            "ScriptStruct" or "UserDefinedStruct" => true,
            "Enum" or "UserDefinedEnum" => true,
            "Function" or "DelegateFunction" => true,
            _ => false,
        };
    }
}
