using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Class Structure panel.
/// </summary>
public partial class ClassStructViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;

    [ObservableProperty] private string _className = "";
    [ObservableProperty] private string _classPath = "";
    [ObservableProperty] private string _superName = "";
    [ObservableProperty] private int _propertiesSize;
    [ObservableProperty] private ObservableCollection<FieldInfoModel> _fields = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasClass;

    /// <summary>
    /// Address of the UObject whose class is currently displayed. Used to
    /// dedupe the "selection bounces twice" Avalonia ListBox behaviour and
    /// to ignore a stale null-fire that would otherwise blank the panel.
    /// </summary>
    private string? _lastLoadedNodeAddress;

    public ClassStructViewModel(IDumpService dump, ILoggingService log)
    {
        _dump = dump;
        _log = log;
    }

    [RelayCommand]
    private async Task LoadClassAsync(string? classAddr)
    {
        if (string.IsNullOrEmpty(classAddr) || classAddr == "0x0") return;

        try
        {
            ClearError();
            IsLoading = true;

            var ci = await _dump.WalkClassAsync(classAddr);

            ClassName = ci.Name;
            ClassPath = ci.FullPath;
            SuperName = ci.SuperName;
            PropertiesSize = ci.PropertiesSize;
            HasClass = true;

            Fields.Clear();
            foreach (var f in ci.Fields)
            {
                Fields.Add(f);
            }

            _log.Info($"Loaded class: {ci.Name} ({ci.Fields.Count} fields)");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to load class at {classAddr}", ex);
        }
        finally
        {
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
                classAddr = detail.ClassAddr;
                if (string.IsNullOrEmpty(classAddr) || classAddr == "0x0")
                    classAddr = node.Address;
            }

            _lastLoadedNodeAddress = node.Address;
            await LoadClassCommand.ExecuteAsync(classAddr);
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to load class for object at {node.Address}", ex);
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
