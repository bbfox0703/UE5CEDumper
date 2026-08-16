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
    /// Address of the UObject whose class is currently displayed. Used to
    /// dedupe the "selection bounces twice" Avalonia ListBox behaviour and
    /// to ignore a stale null-fire that would otherwise blank the panel.
    /// </summary>
    private string? _lastLoadedNodeAddress;

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
    /// functions included). Distinct from the per-FIELD "Find Funcs" column.</summary>
    [RelayCommand]
    private async Task FindClassFuncAsync()
    {
        if (string.IsNullOrEmpty(LoadedClassAddr)) return;
        await Views.PropertyXrefDialog.ShowForClassAsync(
            ClassName, LoadedClassAddr, _dump, _platform);
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

        await LoadClassCoreAsync(classAddr, ++_loadId);
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
    /// We also dedupe consecutive selections of the same node — the
    /// listbox occasionally fires a second SelectionChanged for the same
    /// item right after a click, and re-walking the class is wasteful.
    /// </summary>
    public async Task OnObjectSelected(UObjectNode? node)
    {
        if (node == null) return;
        if (_lastLoadedNodeAddress == node.Address && HasClass) return;

        // Claim the panel for THIS gesture, before any await. See _loadId — doing
        // it here rather than inside LoadClassAsync is what makes the guard work
        // at all, because the losing request is the one that enters the command
        // LAST.
        int gen = ++_loadId;

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

            _lastLoadedNodeAddress = node.Address;
            await LoadClassCoreAsync(classAddr, gen);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to load class for object at {node.Address}", ex);
            if (gen != _loadId) return;   // stale failure — don't paint over a newer panel
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
