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
    [ObservableProperty] private bool _versionDetected = true;
    [ObservableProperty] private int _totalObjects;
    [ObservableProperty] private bool _hasData;

    // Scan method for each pointer: "aob", "data_scan", "string_ref", "pointer_scan", "not_found"
    [ObservableProperty] private string _gObjectsMethod = "aob";
    [ObservableProperty] private string _gNamesMethod = "aob";
    [ObservableProperty] private string _gWorldMethod = "aob";

    // Pattern IDs: which AOB pattern won the scan (e.g. "GOBJ_V1")
    [ObservableProperty] private string _gObjectsPatternId = "";
    [ObservableProperty] private string _gNamesPatternId = "";
    [ObservableProperty] private string _gWorldPatternId = "";

    // Per-target scan statistics (for red/green indicator)
    [ObservableProperty] private int _gObjectsPatternsHit;
    [ObservableProperty] private int _gNamesPatternsHit;
    [ObservableProperty] private int _gWorldPatternsHit;

    /// <summary>True when version detection failed — shows warning in UI.</summary>
    public bool ShowVersionWarning => HasData && !VersionDetected;

    /// <summary>True when GObjects was found via fallback (not AOB).</summary>
    public bool ShowGObjectsWarning => HasData && GObjectsMethod != "aob";

    /// <summary>True when GNames was found via fallback (not AOB).</summary>
    public bool ShowGNamesWarning => HasData && GNamesMethod != "aob";

    /// <summary>True when GWorld was not found at all.</summary>
    public bool ShowGWorldWarning => HasData && GWorldMethod == "not_found";

    /// <summary>True when ALL GObjects AOB patterns failed (0 hits).</summary>
    public bool GObjectsAobAllFailed => HasData && GObjectsPatternsHit == 0;

    /// <summary>True when ALL GNames AOB patterns failed (0 hits).</summary>
    public bool GNamesAobAllFailed => HasData && GNamesPatternsHit == 0;

    /// <summary>True when ALL GWorld AOB patterns failed (0 hits).</summary>
    public bool GWorldAobAllFailed => HasData && GWorldPatternsHit == 0;

    /// <summary>Formatted scan method label for GObjects.</summary>
    public string GObjectsMethodLabel => FormatMethodLabel(GObjectsMethod);

    /// <summary>Formatted scan method label for GNames.</summary>
    public string GNamesMethodLabel => FormatMethodLabel(GNamesMethod);

    /// <summary>True when GObjects has a non-empty pattern ID to display.</summary>
    public bool HasGObjectsPatternId => HasData && !string.IsNullOrEmpty(GObjectsPatternId);

    /// <summary>True when GNames has a non-empty pattern ID to display.</summary>
    public bool HasGNamesPatternId => HasData && !string.IsNullOrEmpty(GNamesPatternId);

    /// <summary>True when GWorld has a non-empty pattern ID to display.</summary>
    public bool HasGWorldPatternId => HasData && !string.IsNullOrEmpty(GWorldPatternId);

    public PointerPanelViewModel(IPlatformService platform)
    {
        _platform = platform;
    }

    public void Update(string gobjects, string gnames, string gworld,
                       int ueVersion, bool versionDetected, int totalObjects,
                       string gobjectsMethod = "aob", string gnamesMethod = "aob",
                       string gworldMethod = "aob",
                       string gobjectsPatternId = "", string gnamesPatternId = "",
                       string gworldPatternId = "",
                       int gobjectsPatternsHit = 0, int gnamesPatternsHit = 0,
                       int gworldPatternsHit = 0)
    {
        GObjectsAddress = gobjects;
        GNamesAddress = gnames;
        GWorldAddress = gworld;
        UeVersion = ueVersion;
        VersionDetected = versionDetected;
        TotalObjects = totalObjects;
        GObjectsMethod = gobjectsMethod;
        GNamesMethod = gnamesMethod;
        GWorldMethod = gworldMethod;
        GObjectsPatternId = gobjectsPatternId;
        GNamesPatternId = gnamesPatternId;
        GWorldPatternId = gworldPatternId;
        GObjectsPatternsHit = gobjectsPatternsHit;
        GNamesPatternsHit = gnamesPatternsHit;
        GWorldPatternsHit = gworldPatternsHit;
        HasData = true;
        NotifyComputedProperties();
    }

    private void NotifyComputedProperties()
    {
        OnPropertyChanged(nameof(ShowVersionWarning));
        OnPropertyChanged(nameof(ShowGObjectsWarning));
        OnPropertyChanged(nameof(ShowGNamesWarning));
        OnPropertyChanged(nameof(ShowGWorldWarning));
        OnPropertyChanged(nameof(GObjectsAobAllFailed));
        OnPropertyChanged(nameof(GNamesAobAllFailed));
        OnPropertyChanged(nameof(GWorldAobAllFailed));
        OnPropertyChanged(nameof(GObjectsMethodLabel));
        OnPropertyChanged(nameof(GNamesMethodLabel));
        OnPropertyChanged(nameof(HasGObjectsPatternId));
        OnPropertyChanged(nameof(HasGNamesPatternId));
        OnPropertyChanged(nameof(HasGWorldPatternId));
    }

    private static string FormatMethodLabel(string method) => method switch
    {
        "data_scan" => "data scan",
        "string_ref" => "string ref",
        "pointer_scan" => "pointer scan",
        "not_found" => "not found",
        _ => method,
    };

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
