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
    /// Called when a UObject is selected in the tree — loads its class.
    /// </summary>
    public async Task OnObjectSelected(UObjectNode? node)
    {
        if (node == null)
        {
            HasClass = false;
            Fields.Clear();
            return;
        }

        try
        {
            ClearError();
            // First get the object's UClass address via get_object
            var detail = await _dump.GetObjectAsync(node.Address);
            var classAddr = detail.ClassAddr;
            if (string.IsNullOrEmpty(classAddr) || classAddr == "0x0")
            {
                // If no class addr, try using the object address directly
                // (it might already be a UClass)
                classAddr = node.Address;
            }
            await LoadClassCommand.ExecuteAsync(classAddr);
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to load class for object at {node.Address}", ex);
        }
    }
}
