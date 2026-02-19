using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Global Pointers panel.
/// </summary>
public partial class PointerPanelViewModel : ViewModelBase
{
    private readonly IPlatformService _platform;

    [ObservableProperty] private string _gObjectsAddress = "";
    [ObservableProperty] private string _gNamesAddress = "";
    [ObservableProperty] private string _gWorldAddress = "";
    [ObservableProperty] private int _ueVersion;
    [ObservableProperty] private int _totalObjects;
    [ObservableProperty] private bool _hasData;

    public PointerPanelViewModel(IPlatformService platform)
    {
        _platform = platform;
    }

    public void Update(string gobjects, string gnames, string gworld, int ueVersion, int totalObjects)
    {
        GObjectsAddress = gobjects;
        GNamesAddress = gnames;
        GWorldAddress = gworld;
        UeVersion = ueVersion;
        TotalObjects = totalObjects;
        HasData = true;
    }

    [RelayCommand]
    private async Task CopyGObjectsAsync()
    {
        if (!string.IsNullOrEmpty(GObjectsAddress))
            await _platform.CopyToClipboardAsync(GObjectsAddress);
    }

    [RelayCommand]
    private async Task CopyGNamesAsync()
    {
        if (!string.IsNullOrEmpty(GNamesAddress))
            await _platform.CopyToClipboardAsync(GNamesAddress);
    }

    [RelayCommand]
    private async Task CopyGWorldAsync()
    {
        if (!string.IsNullOrEmpty(GWorldAddress))
            await _platform.CopyToClipboardAsync(GWorldAddress);
    }
}
