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

    // Address format
    [ObservableProperty] private int _selectedAddressFormatIndex;
    private AddressFormat AddrFormat => (AddressFormat)SelectedAddressFormatIndex;

    // --- Class name search ---
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

    // --- Address-to-Instance reverse lookup ---
    [ObservableProperty] private string _lookupAddress = "";
    [ObservableProperty] private string _lookupStatusText = "";
    [ObservableProperty] private bool _isLookingUp;

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

            var result = await _dump.FindInstancesAsync(SearchClassName.Trim());

            Instances.Clear();
            foreach (var r in result.Instances)
            {
                Instances.Add(r);
            }

            HasInstances = Instances.Count > 0;
            StatusText = result.Scanned > 0
                ? $"Found {Instances.Count} instances (scanned {result.Scanned:N0}, non-null {result.NonNull:N0}, named {result.Named:N0})"
                : $"Found {Instances.Count} instances";
            _log.Info($"FindInstances: '{SearchClassName}' -> {Instances.Count} results (scanned={result.Scanned}, nonNull={result.NonNull}, named={result.Named})");
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

    [RelayCommand]
    private async Task LookupAddressAsync()
    {
        if (string.IsNullOrWhiteSpace(LookupAddress)) return;

        try
        {
            ClearError();
            IsLookingUp = true;
            LookupStatusText = "Looking up...";
            ShowCeXml = false;

            var addrStr = AddressHelper.NormalizeAddress(LookupAddress, _engineState?.ModuleBase);

            var result = await _dump.FindByAddressAsync(addrStr);

            Instances.Clear();
            Fields.Clear();
            HasFields = false;

            if (result.Found)
            {
                var instance = new InstanceResult
                {
                    Address = result.Address,
                    Index = result.Index,
                    Name = result.Name,
                    ClassName = result.ClassName,
                    OuterAddr = result.OuterAddr,
                };
                Instances.Add(instance);
                HasInstances = true;
                SelectedInstance = instance;  // Auto-select to trigger field loading

                var matchInfo = result.MatchType == "exact"
                    ? "Exact UObject match"
                    : $"Inside {result.Name} (offset +0x{result.OffsetFromBase:X})";
                LookupStatusText = matchInfo;
                _log.Info($"FindByAddress: '{addrStr}' -> {matchInfo}");
            }
            else
            {
                HasInstances = false;
                LookupStatusText = "No UObject found at this address";
                _log.Info($"FindByAddress: '{addrStr}' -> not found");
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            LookupStatusText = "Lookup failed";
            _log.Error($"FindByAddress failed for '{LookupAddress}'", ex);
        }
        finally
        {
            IsLookingUp = false;
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

            // Compute base address for FieldAddress calculation
            ulong baseAddr = 0;
            try
            {
                if (!string.IsNullOrEmpty(result.Address))
                    baseAddr = Convert.ToUInt64(result.Address.Replace("0x", "").Replace("0X", ""), 16);
            }
            catch { /* ignore parse failures */ }

            Fields.Clear();
            foreach (var f in result.Fields)
            {
                if (baseAddr != 0)
                    f.FieldAddress = $"0x{baseAddr + (ulong)f.Offset:X}";
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
            IsLoadingFields = true;

            // Pre-resolve StructProperty inner fields via DLL
            var resolvedStructs = await CeXmlExportService.ResolveStructFieldsAsync(
                _dump, new List<LiveFieldValue>(Fields));

            // Compute root address in user-selected format
            var rootAddress = AddressHelper.FormatAddress(
                SelectedInstance.Address, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);

            var xml = CeXmlExportService.GenerateInstanceXml(
                rootAddress, SelectedInstance.Name, SelectedInstance.ClassName,
                new List<LiveFieldValue>(Fields), resolvedStructs);

            await _platform.CopyToClipboardAsync(xml);
            _log.Info($"CE XML copied to clipboard for instance {SelectedInstance.Name} ({resolvedStructs.Count} structs resolved)");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to export CE XML", ex);
        }
        finally
        {
            IsLoadingFields = false;
        }
    }

    [RelayCommand]
    private async Task CopyFieldAddressAsync(LiveFieldValue? field)
    {
        if (field == null || SelectedInstance == null) return;
        if (string.IsNullOrEmpty(SelectedInstance.Address)) return;

        try
        {
            var instanceAddr = Convert.ToUInt64(SelectedInstance.Address.Replace("0x", "").Replace("0X", ""), 16);
            var absAddr = instanceAddr + (ulong)field.Offset;
            var hexAddr = $"0x{absAddr:X}";

            var formatted = AddressHelper.FormatAddress(
                hexAddr, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
            await _platform.CopyToClipboardAsync(formatted);
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
            var formatted = AddressHelper.FormatAddress(
                instance.Address, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
            await _platform.CopyToClipboardAsync(formatted);
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

        try
        {
            var symbolName = instance.ClassName.Replace(" ", "_").Replace("-", "_");

            var formattedAddr = AddressHelper.FormatAddress(
                instance.Address, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);

            var xml = CeXmlExportService.GenerateRegisterSymbolXml(symbolName, formattedAddr);

            await _platform.CopyToClipboardAsync(xml);
            _log.Info($"CE AA script copied to clipboard for {instance.ClassName}");
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
