using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Live Data Walker panel.
/// Browse GWorld hierarchy and navigate into any UObject by clicking pointers.
/// </summary>
public partial class LiveWalkerViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService _platform;

    // Cached GWorld walk result for back-navigation
    private WorldWalkResult? _cachedWorld;

    // Engine state for CE address formatting
    private EngineState? _engineState;

    // Navigation breadcrumb stack
    [ObservableProperty] private ObservableCollection<BreadcrumbItem> _breadcrumbs = new();
    [ObservableProperty] private ObservableCollection<LiveFieldValue> _fields = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _currentObjectName = "";
    [ObservableProperty] private string _currentClassName = "";
    [ObservableProperty] private string _currentAddress = "";
    [ObservableProperty] private bool _hasData;
    // CE XML output (kept for possible future use but no longer shown in panel)
    [ObservableProperty] private string _ceXmlOutput = "";
    [ObservableProperty] private bool _showCeXml;

    // Search
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private bool _hasSearchResults;

    public LiveWalkerViewModel(IDumpService dump, ILoggingService log, IPlatformService platform)
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
    private async Task StartFromWorldAsync()
    {
        try
        {
            ClearError();
            IsLoading = true;

            var world = await _dump.WalkWorldAsync(500);
            _cachedWorld = world;

            Breadcrumbs.Clear();
            Breadcrumbs.Add(new BreadcrumbItem
            {
                Address = world.WorldAddr,
                Label = "GWorld",
                IsPointerDeref = true,
                FieldOffset = 0,
                FieldName = "GWorld",
            });

            PopulateFromWorld(world);
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to load GWorld", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void PopulateFromWorld(WorldWalkResult world)
    {
        CurrentObjectName = world.WorldName;
        CurrentClassName = "UWorld";
        CurrentAddress = world.WorldAddr;
        HasData = true;
        ShowCeXml = false;

        Fields.Clear();

        // PersistentLevel as first navigable entry
        if (!string.IsNullOrEmpty(world.LevelAddr) && world.LevelAddr != "0x0")
        {
            Fields.Add(new LiveFieldValue
            {
                Name = world.LevelName ?? "PersistentLevel",
                TypeName = "ObjectProperty",
                Offset = 0,
                Size = 8,
                PtrAddress = world.LevelAddr,
                PtrName = world.LevelName ?? "PersistentLevel",
                PtrClassName = "ULevel",
            });
        }

        // Each actor as a navigable entry
        foreach (var actor in world.Actors)
        {
            Fields.Add(new LiveFieldValue
            {
                Name = actor.Name,
                TypeName = "ObjectProperty",
                Offset = 0,
                Size = 8,
                PtrAddress = actor.Address,
                PtrName = actor.Name,
                PtrClassName = actor.ClassName,
            });

            // Components as indented sub-entries
            foreach (var comp in actor.Components)
            {
                Fields.Add(new LiveFieldValue
                {
                    Name = $"  {actor.Name}.{comp.Name}",
                    TypeName = "ObjectProperty",
                    Offset = 0,
                    Size = 8,
                    PtrAddress = comp.Address,
                    PtrName = comp.Name,
                    PtrClassName = comp.ClassName,
                });
            }
        }
    }

    [RelayCommand]
    private async Task NavigateToFieldAsync(LiveFieldValue? field)
    {
        if (field == null || !field.IsNavigable) return;

        try
        {
            ClearError();
            IsLoading = true;

            if (!string.IsNullOrEmpty(field.PtrAddress) && field.PtrAddress != "0x0")
            {
                // ObjectProperty navigation (pointer dereference)
                await NavigateToAsync(field.PtrAddress, field.Name, field.Offset, field.Name, isPointer: true);
            }
            else if (!string.IsNullOrEmpty(field.StructDataAddr) && field.StructDataAddr != "0x0")
            {
                // StructProperty navigation: walk struct data using its class
                var result = await _dump.WalkInstanceAsync(field.StructDataAddr, field.StructClassAddr);
                var displayName = !string.IsNullOrEmpty(field.StructTypeName)
                    ? $"{field.Name} ({field.StructTypeName})"
                    : field.Name;
                Breadcrumbs.Add(new BreadcrumbItem
                {
                    Address = field.StructDataAddr,
                    Label = displayName,
                    ClassAddr = field.StructClassAddr,
                    FieldOffset = field.Offset,
                    FieldName = field.Name,
                    IsPointerDeref = false,
                });
                UpdateDisplay(result);
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to navigate to {field.Name}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToBreadcrumbAsync(BreadcrumbItem? item)
    {
        if (item == null) return;

        try
        {
            ClearError();
            IsLoading = true;

            // Remove all breadcrumbs after this one
            var idx = Breadcrumbs.IndexOf(item);
            if (idx < 0) return;

            while (Breadcrumbs.Count > idx + 1)
                Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);

            // If navigating back to GWorld, re-display actor list
            if (_cachedWorld != null && item.Address == _cachedWorld.WorldAddr)
            {
                PopulateFromWorld(_cachedWorld);
                return;
            }

            // Re-walk this object (pass ClassAddr for StructProperty navigation)
            var classAddr = string.IsNullOrEmpty(item.ClassAddr) ? null : item.ClassAddr;
            var result = await _dump.WalkInstanceAsync(item.Address, classAddr);
            UpdateDisplay(result);
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (Breadcrumbs.Count < 2) return;

        Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
        var prev = Breadcrumbs[^1];

        try
        {
            ClearError();
            IsLoading = true;

            // If going back to GWorld, re-display actor list
            if (_cachedWorld != null && prev.Address == _cachedWorld.WorldAddr)
            {
                PopulateFromWorld(_cachedWorld);
                return;
            }

            var classAddr = string.IsNullOrEmpty(prev.ClassAddr) ? null : prev.ClassAddr;
            var result = await _dump.WalkInstanceAsync(prev.Address, classAddr);
            UpdateDisplay(result);
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToAddressAsync(string? addr)
    {
        if (string.IsNullOrEmpty(addr)) return;

        try
        {
            ClearError();
            IsLoading = true;
            Breadcrumbs.Clear();
            await NavigateToAsync(addr, "Custom", 0, "Custom", isPointer: true);
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to navigate to {addr}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportCeXmlAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress) || Breadcrumbs.Count == 0) return;
        if (_engineState == null || string.IsNullOrEmpty(_engineState.ModuleName) || string.IsNullOrEmpty(_engineState.ModuleBase)) return;

        try
        {
            ClearError();

            // Compute root address as "Module.exe"+RVA
            var rootBc = Breadcrumbs[0];
            var rootModuleRva = ComputeModuleRva(rootBc.Address);

            var xml = CeXmlExportService.GenerateHierarchicalXml(
                rootModuleRva, rootBc.Label, Breadcrumbs, Fields);

            await _platform.CopyToClipboardAsync(xml);
            _log.Info($"CE XML copied to clipboard for {CurrentClassName}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to export CE XML", ex);
        }
    }

    /// <summary>
    /// Compute CE-compatible "Module.exe"+RVA string from an absolute address.
    /// </summary>
    private string ComputeModuleRva(string hexAddr)
    {
        var addr = Convert.ToUInt64(hexAddr.Replace("0x", "").Replace("0X", ""), 16);
        var moduleBase = Convert.ToUInt64(_engineState!.ModuleBase.Replace("0x", "").Replace("0X", ""), 16);
        var rva = addr - moduleBase;
        return $"\"{_engineState.ModuleName}\"+{rva:X}";
    }

    [RelayCommand]
    private async Task GenerateCeAAScriptAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress)) return;
        if (_engineState == null || string.IsNullOrEmpty(_engineState.ModuleName) || string.IsNullOrEmpty(_engineState.ModuleBase)) return;

        try
        {
            ClearError();
            var addr = Convert.ToUInt64(CurrentAddress.Replace("0x", "").Replace("0X", ""), 16);
            var moduleBase = Convert.ToUInt64(_engineState.ModuleBase.Replace("0x", "").Replace("0X", ""), 16);
            var rva = addr - moduleBase;

            var symbolName = !string.IsNullOrEmpty(CurrentClassName)
                ? CurrentClassName.Replace(" ", "_").Replace("-", "_")
                : "UE5_Symbol";

            var xml = CeXmlExportService.GenerateRegisterSymbolXml(
                symbolName, _engineState.ModuleName, rva);

            await _platform.CopyToClipboardAsync(xml);
            _log.Info($"CE AA script copied to clipboard for {CurrentClassName} at RVA {rva:X}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to generate CE AA script", ex);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress)) return;

        try
        {
            ClearError();
            IsLoading = true;

            // If refreshing GWorld view, re-fetch the world
            if (_cachedWorld != null && CurrentAddress == _cachedWorld.WorldAddr)
            {
                var world = await _dump.WalkWorldAsync(500);
                _cachedWorld = world;
                PopulateFromWorld(world);
                return;
            }

            var result = await _dump.WalkInstanceAsync(CurrentAddress);
            UpdateDisplay(result);
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CopyFieldAddressAsync(LiveFieldValue? field)
    {
        if (field == null || _engineState == null) return;
        if (string.IsNullOrEmpty(CurrentAddress) || string.IsNullOrEmpty(_engineState.ModuleName)) return;

        try
        {
            var instanceAddr = Convert.ToUInt64(CurrentAddress.Replace("0x", "").Replace("0X", ""), 16);
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
    private async Task CopyFieldNameAsync(LiveFieldValue? field)
    {
        if (field == null || string.IsNullOrEmpty(field.Name)) return;

        try
        {
            await _platform.CopyToClipboardAsync(field.Name);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy name for {field.Name}", ex);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplySearch(value);
    }

    private void ApplySearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (var f in Fields) f.IsSearchMatch = false;
            SearchMatchCount = 0;
            HasSearchResults = false;
        }
        else
        {
            int count = 0;
            foreach (var f in Fields)
            {
                bool match =
                    f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.TypeName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.DisplayValue.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.PtrClassName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(f.StructTypeName) && f.StructTypeName.Contains(query, StringComparison.OrdinalIgnoreCase));

                f.IsSearchMatch = match;
                if (match) count++;
            }

            SearchMatchCount = count;
            HasSearchResults = count > 0;
        }

        // Force DataGrid to re-evaluate row styles by resetting the collection
        var items = new ObservableCollection<LiveFieldValue>(Fields);
        Fields = items;
    }

    private async Task NavigateToAsync(string addr, string label, int fieldOffset, string fieldName, bool isPointer)
    {
        var result = await _dump.WalkInstanceAsync(addr);

        var displayName = !string.IsNullOrEmpty(result.Name) ? result.Name : label;
        Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = addr,
            Label = displayName,
            FieldOffset = fieldOffset,
            FieldName = fieldName,
            IsPointerDeref = isPointer,
        });

        UpdateDisplay(result);
    }

    private void UpdateDisplay(InstanceWalkResult result)
    {
        CurrentObjectName = result.Name;
        CurrentClassName = result.ClassName;
        CurrentAddress = result.Address;
        HasData = true;
        ShowCeXml = false;

        // Compute absolute field addresses
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
    }
}

/// <summary>
/// A breadcrumb navigation item, recording navigation history for CE XML export.
/// </summary>
public sealed class BreadcrumbItem
{
    public string Address { get; init; } = "";
    public string Label { get; init; } = "";
    public string ClassAddr { get; init; } = "";

    /// <summary>Offset of the field that was clicked to reach this level (hex).</summary>
    public int FieldOffset { get; init; }

    /// <summary>Field name (e.g., "m_pAttributeSetHealth").</summary>
    public string FieldName { get; init; } = "";

    /// <summary>True if navigation was through a pointer dereference (ObjectProperty), false for inline struct.</summary>
    public bool IsPointerDeref { get; init; }
}
