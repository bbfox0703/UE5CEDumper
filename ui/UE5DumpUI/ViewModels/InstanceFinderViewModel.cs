using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Instance Finder panel.
/// Search for instances by class name, view live values, export CE XML.
/// </summary>
public partial class InstanceFinderViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;

    [ObservableProperty] private string _searchClassName = "";
    [ObservableProperty] private ObservableCollection<InstanceResult> _instances = new();
    [ObservableProperty] private InstanceResult? _selectedInstance;
    [ObservableProperty] private ObservableCollection<LiveFieldValue> _fields = new();
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isLoadingFields;
    [ObservableProperty] private bool _hasInstances;
    [ObservableProperty] private bool _hasFields;
    [ObservableProperty] private string _ceXmlOutput = "";
    [ObservableProperty] private bool _showCeXml;
    [ObservableProperty] private string _statusText = "";

    /// <summary>
    /// Event raised when user wants to navigate to an address in the Live Walker.
    /// </summary>
    public event Action<string>? NavigateToLiveWalker;

    public InstanceFinderViewModel(IDumpService dump, ILoggingService log)
    {
        _dump = dump;
        _log = log;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchClassName)) return;

        try
        {
            ClearError();
            IsSearching = true;
            StatusText = "Searching...";
            ShowCeXml = false;

            var results = await _dump.FindInstancesAsync(SearchClassName.Trim());

            Instances.Clear();
            foreach (var r in results)
            {
                Instances.Add(r);
            }

            HasInstances = Instances.Count > 0;
            StatusText = $"Found {Instances.Count} instances";
            _log.Info($"FindInstances: '{SearchClassName}' -> {Instances.Count} results");
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Search failed";
            _log.Error($"FindInstances failed for '{SearchClassName}'", ex);
        }
        finally
        {
            IsSearching = false;
        }
    }

    partial void OnSelectedInstanceChanged(InstanceResult? value)
    {
        if (value != null)
        {
            _ = LoadInstanceFieldsAsync(value);
        }
        else
        {
            Fields.Clear();
            HasFields = false;
        }
    }

    private async Task LoadInstanceFieldsAsync(InstanceResult instance)
    {
        try
        {
            ClearError();
            IsLoadingFields = true;
            ShowCeXml = false;

            var result = await _dump.WalkInstanceAsync(instance.Address);

            Fields.Clear();
            foreach (var f in result.Fields)
            {
                Fields.Add(f);
            }

            HasFields = Fields.Count > 0;
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to walk instance at {instance.Address}", ex);
        }
        finally
        {
            IsLoadingFields = false;
        }
    }

    [RelayCommand]
    private async Task ExportCeXmlAsync()
    {
        if (SelectedInstance == null) return;

        try
        {
            ClearError();
            var ceInfo = await _dump.GetCePointerInfoAsync(SelectedInstance.Address);
            var instance = new InstanceWalkResult
            {
                Address = SelectedInstance.Address,
                Name = SelectedInstance.Name,
                ClassName = SelectedInstance.ClassName,
                Fields = new List<LiveFieldValue>(Fields),
            };

            CeXmlOutput = CeXmlExportService.GenerateInstanceXml(ceInfo, instance);
            ShowCeXml = true;
            _log.Info($"CE XML exported for instance {SelectedInstance.Name}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to export CE XML", ex);
        }
    }

    [RelayCommand]
    private void OpenInLiveWalker()
    {
        if (SelectedInstance == null) return;
        NavigateToLiveWalker?.Invoke(SelectedInstance.Address);
    }
}
