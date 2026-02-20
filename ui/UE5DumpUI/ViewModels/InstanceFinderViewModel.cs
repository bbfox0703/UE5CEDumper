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
    private readonly IPlatformService _platform;

    private EngineState? _engineState;

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

    public InstanceFinderViewModel(IDumpService dump, ILoggingService log, IPlatformService platform)
    {
        _dump = dump;
        _log = log;
        _platform = platform;
    }

    public void SetEngineState(EngineState state)
    {
        _engineState = state;
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
    private async Task CopyFieldAddressAsync(LiveFieldValue? field)
    {
        if (field == null || _engineState == null || SelectedInstance == null) return;
        if (string.IsNullOrEmpty(SelectedInstance.Address) || string.IsNullOrEmpty(_engineState.ModuleName)) return;

        try
        {
            var instanceAddr = Convert.ToUInt64(SelectedInstance.Address.Replace("0x", "").Replace("0X", ""), 16);
            var moduleBase = Convert.ToUInt64(_engineState.ModuleBase.Replace("0x", "").Replace("0X", ""), 16);

            var absAddr = instanceAddr + (ulong)field.Offset;
            var rva = absAddr - moduleBase;

            var ceFormat = $"\"{_engineState.ModuleName}\"+{rva:X}";
            await _platform.CopyToClipboardAsync(ceFormat);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy address for {field.Name}", ex);
        }
    }

    [RelayCommand]
    private async Task CopyInstanceAddressAsync(InstanceResult? instance)
    {
        if (instance == null || string.IsNullOrEmpty(instance.Address)) return;

        try
        {
            if (_engineState != null && !string.IsNullOrEmpty(_engineState.ModuleName) && !string.IsNullOrEmpty(_engineState.ModuleBase))
            {
                var addr = Convert.ToUInt64(instance.Address.Replace("0x", "").Replace("0X", ""), 16);
                var moduleBase = Convert.ToUInt64(_engineState.ModuleBase.Replace("0x", "").Replace("0X", ""), 16);
                var rva = addr - moduleBase;
                var ceFormat = $"\"{_engineState.ModuleName}\"+{rva:X}";
                await _platform.CopyToClipboardAsync(ceFormat);
            }
            else
            {
                await _platform.CopyToClipboardAsync(instance.Address);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy instance address for {instance.Name}", ex);
        }
    }

    [RelayCommand]
    private async Task GenerateCeAAScriptAsync(InstanceResult? instance)
    {
        if (instance == null || string.IsNullOrEmpty(instance.Address)) return;
        if (_engineState == null || string.IsNullOrEmpty(_engineState.ModuleName) || string.IsNullOrEmpty(_engineState.ModuleBase)) return;

        try
        {
            var addr = Convert.ToUInt64(instance.Address.Replace("0x", "").Replace("0X", ""), 16);
            var moduleBase = Convert.ToUInt64(_engineState.ModuleBase.Replace("0x", "").Replace("0X", ""), 16);
            var rva = addr - moduleBase;

            // CE-compatible symbol name: replace invalid chars
            var symbolName = instance.ClassName.Replace(" ", "_").Replace("-", "_");

            var xml = CeXmlExportService.GenerateRegisterSymbolXml(
                symbolName, _engineState.ModuleName, rva);

            await _platform.CopyToClipboardAsync(xml);
            _log.Info($"CE AA script copied to clipboard for {instance.ClassName} at RVA {rva:X}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to generate CE AA script", ex);
        }
    }

    [RelayCommand]
    private void OpenInLiveWalker()
    {
        if (SelectedInstance == null) return;
        NavigateToLiveWalker?.Invoke(SelectedInstance.Address);
    }
}
