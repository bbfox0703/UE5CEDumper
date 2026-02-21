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

    /// <summary>Strip leading "0x" or "0X" prefix for clipboard copy.</summary>
    private static string StripHexPrefix(string addr)
        => addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? addr[2..] : addr;

    [RelayCommand]
    private async Task CopyGObjectsAsync()
    {
        if (!string.IsNullOrEmpty(GObjectsAddress))
            await _platform.CopyToClipboardAsync(StripHexPrefix(GObjectsAddress));
    }

    [RelayCommand]
    private async Task CopyGNamesAsync()
    {
        if (!string.IsNullOrEmpty(GNamesAddress))
            await _platform.CopyToClipboardAsync(StripHexPrefix(GNamesAddress));
    }

    [RelayCommand]
    private async Task CopyGWorldAsync()
    {
        if (!string.IsNullOrEmpty(GWorldAddress))
            await _platform.CopyToClipboardAsync(StripHexPrefix(GWorldAddress));
    }
}
