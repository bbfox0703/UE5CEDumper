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

    // Navigation breadcrumb stack
    [ObservableProperty] private ObservableCollection<BreadcrumbItem> _breadcrumbs = new();
    [ObservableProperty] private ObservableCollection<LiveFieldValue> _fields = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _currentObjectName = "";
    [ObservableProperty] private string _currentClassName = "";
    [ObservableProperty] private string _currentAddress = "";
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _ceXmlOutput = "";
    [ObservableProperty] private bool _showCeXml;

    public LiveWalkerViewModel(IDumpService dump, ILoggingService log)
    {
        _dump = dump;
        _log = log;
    }

    [RelayCommand]
    private async Task StartFromWorldAsync()
    {
        try
        {
            ClearError();
            IsLoading = true;

            var world = await _dump.WalkWorldAsync(500);

            // Navigate to GWorld
            Breadcrumbs.Clear();
            await NavigateToAsync(world.WorldAddr, "GWorld");
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

    [RelayCommand]
    private async Task NavigateToFieldAsync(LiveFieldValue? field)
    {
        if (field == null || !field.IsNavigable) return;

        try
        {
            ClearError();
            IsLoading = true;
            await NavigateToAsync(field.PtrAddress, field.Name);
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

            // Re-walk this object
            var result = await _dump.WalkInstanceAsync(item.Address);
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
            var result = await _dump.WalkInstanceAsync(prev.Address);
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
            await NavigateToAsync(addr, "Custom");
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
        if (string.IsNullOrEmpty(CurrentAddress)) return;

        try
        {
            ClearError();
            var ceInfo = await _dump.GetCePointerInfoAsync(CurrentAddress);
            var instance = new InstanceWalkResult
            {
                Address = CurrentAddress,
                Name = CurrentObjectName,
                ClassName = CurrentClassName,
                Fields = new List<LiveFieldValue>(Fields),
            };

            CeXmlOutput = CeXmlExportService.GenerateInstanceXml(ceInfo, instance);
            ShowCeXml = true;
            _log.Info($"CE XML exported for {CurrentClassName}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to export CE XML", ex);
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

    private async Task NavigateToAsync(string addr, string label)
    {
        var result = await _dump.WalkInstanceAsync(addr);

        var displayName = !string.IsNullOrEmpty(result.Name) ? result.Name : label;
        Breadcrumbs.Add(new BreadcrumbItem { Address = addr, Label = displayName });

        UpdateDisplay(result);
    }

    private void UpdateDisplay(InstanceWalkResult result)
    {
        CurrentObjectName = result.Name;
        CurrentClassName = result.ClassName;
        CurrentAddress = result.Address;
        HasData = true;
        ShowCeXml = false;

        Fields.Clear();
        foreach (var f in result.Fields)
        {
            Fields.Add(f);
        }
    }
}

/// <summary>
/// A breadcrumb navigation item.
/// </summary>
public sealed class BreadcrumbItem
{
    public string Address { get; init; } = "";
    public string Label { get; init; } = "";
}
