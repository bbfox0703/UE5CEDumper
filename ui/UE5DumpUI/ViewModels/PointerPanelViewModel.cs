using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Global Pointers panel.
/// </summary>
public partial class PointerPanelViewModel : ViewModelBase
{
    private readonly IPlatformService _platform;
    private readonly IDumpService? _dump;
    private readonly ILoggingService? _log;
    private readonly IAobMakerBridge? _aobMaker;
    private readonly AobUsageService? _aobUsage;

    [ObservableProperty] private string _gObjectsAddress = "";
    [ObservableProperty] private string _gNamesAddress = "";
    [ObservableProperty] private string _gWorldAddress = "";
    [ObservableProperty] private int _ueVersion;
    [ObservableProperty] private bool _versionDetected = true;
    [ObservableProperty] private bool _isUserOverride;
    [ObservableProperty] private bool _isLowConfidence;
    [ObservableProperty] private string _publisherThumbprint = "";
    [ObservableProperty] private string _selectedUeVersionOverride = "Auto";
    [ObservableProperty] private bool _isApplyingOverride;
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

    // AOB scan hit addresses (instruction that references the pointer)
    [ObservableProperty] private string _gObjectsScanAddr = "";
    [ObservableProperty] private string _gNamesScanAddr = "";
    [ObservableProperty] private string _gWorldScanAddr = "";

    // Per-target scan statistics (for red/green indicator)
    [ObservableProperty] private int _gObjectsPatternsHit;
    [ObservableProperty] private int _gNamesPatternsHit;
    [ObservableProperty] private int _gWorldPatternsHit;

    // --- GWorld AOB metadata (for CreateSymbolScript) ---
    [ObservableProperty] private string _gworldAob = "";
    [ObservableProperty] private int _gworldAobPos;
    [ObservableProperty] private int _gworldAobLen;
    [ObservableProperty] private string _moduleName = "";

    // --- AOBMaker CE Plugin bridge ---
    [ObservableProperty] private bool _isAobMakerAvailable;

    // --- Extra Scan state ---
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _scanStatusText = "";
    [ObservableProperty] private bool _scanComplete;
    [ObservableProperty] private string _scanResultText = "";

    // --- Cache management ---
    [ObservableProperty] private string _peHash = "";
    [ObservableProperty] private string _cacheStatusText = "";

    /// <summary>
    /// Legacy warning binding (kept for compatibility with existing axaml). True when
    /// version detection failed AND no user override is active. The new 3-state badge
    /// uses ShowLowConfidenceWarning / ShowUserOverrideBadge instead.
    /// </summary>
    public bool ShowVersionWarning => HasData && !VersionDetected && !IsUserOverride;

    /// <summary>True when ueVersion came from a user-set persistent override.</summary>
    public bool ShowUserOverrideBadge => HasData && IsUserOverride;

    /// <summary>True when detection succeeded but used the low-confidence Tier 3 / publisher-bias path.</summary>
    public bool ShowLowConfidenceWarning => HasData && IsLowConfidence && !IsUserOverride;

    /// <summary>True when version was detected with high confidence and no override is in effect.</summary>
    public bool ShowVersionDetectedBadge => HasData && VersionDetected && !IsUserOverride && !IsLowConfidence;

    /// <summary>True when a SquareEnix (or future) publisher thumbprint was matched.</summary>
    public bool ShowPublisherHint => HasData && !string.IsNullOrEmpty(PublisherThumbprint);

    /// <summary>Human-readable publisher label (e.g. "SQUARE_ENIX" → "Square Enix").</summary>
    public string PublisherLabel => PublisherThumbprint switch
    {
        "SQUARE_ENIX" => "Square Enix",
        _ => PublisherThumbprint,
    };

    /// <summary>List of override choices for the ComboBox (display strings).</summary>
    public static System.Collections.Generic.IReadOnlyList<string> UeVersionOverrideOptions { get; } = new[]
    {
        "Auto",
        "UE 4.18", "UE 4.19", "UE 4.20", "UE 4.21", "UE 4.22", "UE 4.23",
        "UE 4.24", "UE 4.25", "UE 4.26", "UE 4.27",
        "UE 5.0", "UE 5.1", "UE 5.2", "UE 5.3", "UE 5.4",
        "UE 5.5", "UE 5.6", "UE 5.7", "UE 5.8",
    };

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

    /// <summary>True when GObjects has a non-zero AOB scan address.</summary>
    public bool HasGObjectsScanAddr => HasData && IsNonZeroAddr(GObjectsScanAddr);
    /// <summary>True when GNames has a non-zero AOB scan address.</summary>
    public bool HasGNamesScanAddr => HasData && IsNonZeroAddr(GNamesScanAddr);
    /// <summary>True when GWorld has a non-zero AOB scan address.</summary>
    public bool HasGWorldScanAddr => HasData && IsNonZeroAddr(GWorldScanAddr);

    /// <summary>
    /// True when Extra Scan button should be visible:
    /// connected, not already scanning, and some pointer is missing.
    /// </summary>
    public bool CanExtraScan => HasData && !IsScanning
        && (IsPointerMissing(GObjectsAddress) || GWorldMethod == "not_found");

    // --- AOBMaker button enable state ---
    /// <summary>Can register GWorld address as CE symbol via CreateSymbolScript (requires AOB data).</summary>
    public bool CanRegisterGWorldSymbol => IsAobMakerAvailable
        && IsNonZeroAddr(GWorldAddress) && !string.IsNullOrEmpty(GworldAob);

    /// <summary>Can send GObjects pointer to CE hex view (data address).</summary>
    public bool CanHexGObjects => IsAobMakerAvailable && IsNonZeroAddr(GObjectsAddress);
    /// <summary>Can send GNames pointer to CE hex view (data address).</summary>
    public bool CanHexGNames => IsAobMakerAvailable && IsNonZeroAddr(GNamesAddress);
    /// <summary>Can send GWorld pointer to CE hex view (data address).</summary>
    public bool CanHexGWorld => IsAobMakerAvailable && IsNonZeroAddr(GWorldAddress);

    /// <summary>Can send GObjects AOB scan hit address to CE disassembler (code address).</summary>
    public bool CanAsmGObjectsScan => IsAobMakerAvailable && IsNonZeroAddr(GObjectsScanAddr);
    /// <summary>Can send GNames AOB scan hit address to CE disassembler (code address).</summary>
    public bool CanAsmGNamesScan => IsAobMakerAvailable && IsNonZeroAddr(GNamesScanAddr);
    /// <summary>Can send GWorld AOB scan hit address to CE disassembler (code address).</summary>
    public bool CanAsmGWorldScan => IsAobMakerAvailable && IsNonZeroAddr(GWorldScanAddr);

    /// <summary>True when cache management buttons should be shown (connected + has AobUsageService).</summary>
    public bool CanManageCache => HasData && _aobUsage != null;

    /// <summary>True when the clear-this-game button should be enabled (has PE hash).</summary>
    public bool CanClearGameCache => CanManageCache && !string.IsNullOrEmpty(PeHash);

    /// <summary>Fired when rescan results have been applied — MainWindowVM re-fetches state.</summary>
    public event Action? RescanApplied;

    public PointerPanelViewModel(IPlatformService platform, IDumpService? dump = null,
                                ILoggingService? log = null, IAobMakerBridge? aobMaker = null,
                                AobUsageService? aobUsage = null)
    {
        _platform = platform;
        _dump = dump;
        _log = log;
        _aobMaker = aobMaker;
        _aobUsage = aobUsage;
    }

    public void Update(EngineState state)
    {
        GObjectsAddress = state.GObjectsAddr;
        GNamesAddress = state.GNamesAddr;
        GWorldAddress = state.GWorldAddr;
        UeVersion = state.UEVersion;
        VersionDetected = state.VersionDetected;
        IsUserOverride = state.IsUserOverride;
        IsLowConfidence = state.IsLowConfidence;
        PublisherThumbprint = state.PublisherThumbprint;
        // Re-sync the ComboBox selection to whatever the DLL actually has (override or auto).
        // _suppressOverrideSelectionEvent gates the partial method so this assignment doesn't
        // re-trigger the apply path.
        _suppressOverrideSelectionEvent = true;
        SelectedUeVersionOverride = state.IsUserOverride ? VersionToLabel(state.UEVersion) : "Auto";
        _suppressOverrideSelectionEvent = false;
        TotalObjects = state.ObjectCount;
        GObjectsMethod = state.GObjectsMethod;
        GNamesMethod = state.GNamesMethod;
        GWorldMethod = state.GWorldMethod;
        GObjectsPatternId = state.GObjectsPatternId;
        GNamesPatternId = state.GNamesPatternId;
        GWorldPatternId = state.GWorldPatternId;
        GObjectsPatternsHit = state.GObjectsPatternsHit;
        GNamesPatternsHit = state.GNamesPatternsHit;
        GWorldPatternsHit = state.GWorldPatternsHit;
        GObjectsScanAddr = state.GObjectsScanAddr;
        GNamesScanAddr = state.GNamesScanAddr;
        GWorldScanAddr = state.GWorldScanAddr;
        GworldAob = state.GWorldAob;
        GworldAobPos = state.GWorldAobPos;
        GworldAobLen = state.GWorldAobLen;
        ModuleName = state.ModuleName;
        PeHash = state.PeHash;
        HasData = true;
        // Reset scan state on fresh update
        IsScanning = false;
        ScanComplete = false;
        ScanStatusText = "";
        ScanResultText = "";
        CacheStatusText = "";
        NotifyComputedProperties();
        // Check AOBMaker availability in background (fire-and-forget)
        _ = CheckAobMakerAsync();
    }

    /// <summary>Check AOBMaker availability (called after data load and on tab switch).</summary>
    public async Task CheckAobMakerAsync()
    {
        if (_aobMaker == null) return;
        try
        {
            IsAobMakerAvailable = await _aobMaker.CheckAvailabilityAsync();
            NotifyAobMakerProperties();
        }
        catch { IsAobMakerAvailable = false; }
    }

    private void NotifyComputedProperties()
    {
        OnPropertyChanged(nameof(ShowVersionWarning));
        OnPropertyChanged(nameof(ShowUserOverrideBadge));
        OnPropertyChanged(nameof(ShowLowConfidenceWarning));
        OnPropertyChanged(nameof(ShowVersionDetectedBadge));
        OnPropertyChanged(nameof(ShowPublisherHint));
        OnPropertyChanged(nameof(PublisherLabel));
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
        OnPropertyChanged(nameof(HasGObjectsScanAddr));
        OnPropertyChanged(nameof(HasGNamesScanAddr));
        OnPropertyChanged(nameof(HasGWorldScanAddr));
        OnPropertyChanged(nameof(CanExtraScan));
        OnPropertyChanged(nameof(CanManageCache));
        OnPropertyChanged(nameof(CanClearGameCache));
        NotifyAobMakerProperties();
    }

    private void NotifyAobMakerProperties()
    {
        OnPropertyChanged(nameof(CanHexGObjects));
        OnPropertyChanged(nameof(CanHexGNames));
        OnPropertyChanged(nameof(CanHexGWorld));
        OnPropertyChanged(nameof(CanAsmGObjectsScan));
        OnPropertyChanged(nameof(CanAsmGNamesScan));
        OnPropertyChanged(nameof(CanAsmGWorldScan));
        OnPropertyChanged(nameof(CanRegisterGWorldSymbol));
    }

    private bool _suppressOverrideSelectionEvent;

    /// <summary>
    /// Auto-fired by [ObservableProperty] when SelectedUeVersionOverride changes.
    /// Sends the new value over the pipe; on success, the DLL re-fetch updates everything.
    /// </summary>
    partial void OnSelectedUeVersionOverrideChanged(string value)
    {
        if (_suppressOverrideSelectionEvent) return;
        if (_dump == null) return;
        if (!HasData) return;
        // Fire-and-forget — the apply command itself awaits the pipe round-trip.
        _ = ApplyOverrideAsync(value);
    }

    private async Task ApplyOverrideAsync(string label)
    {
        if (_dump == null) return;
        try
        {
            ClearError();
            IsApplyingOverride = true;
            int version = LabelToVersion(label);  // 0 = Auto (clear)
            var newState = await _dump.SetUeVersionOverrideAsync(version, persist: true);
            Update(newState);
            RescanApplied?.Invoke();   // tell MainWindowVM to refresh other panels
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log?.Error(Constants.LogCatInit, "Failed to apply UE version override", ex);
        }
        finally
        {
            IsApplyingOverride = false;
        }
    }

    /// <summary>"UE 4.27" → 427, "UE 5.4" → 504, "Auto" → 0.</summary>
    private static int LabelToVersion(string label)
    {
        if (string.IsNullOrEmpty(label) || label == "Auto") return 0;
        // Format: "UE M.N"  (M = 4 or 5, N = 0..27)
        var span = label.AsSpan().TrimStart();
        if (span.StartsWith("UE ")) span = span.Slice(3);
        int dot = span.IndexOf('.');
        if (dot <= 0) return 0;
        if (!int.TryParse(span.Slice(0, dot), out int major)) return 0;
        if (!int.TryParse(span.Slice(dot + 1), out int minor)) return 0;
        return major * 100 + minor;
    }

    /// <summary>504 → "UE 5.4", 427 → "UE 4.27", 0 → "Auto".</summary>
    private static string VersionToLabel(int version)
    {
        if (version <= 0) return "Auto";
        int major = version / 100;
        int minor = version % 100;
        return $"UE {major}.{minor}";
    }

    private static string FormatMethodLabel(string method) => method switch
    {
        "data_scan" => "data scan",
        "data_heuristic" => "data heuristic",
        "instance_scan" => "instance scan",
        "string_ref" => "string ref",
        "pointer_scan" => "pointer scan",
        "not_found" => "not found",
        _ => method,
    };

    private static bool IsPointerMissing(string addr)
        => string.IsNullOrEmpty(addr) || addr == "0x0" || addr == "0x00000000" || addr == "0";

    /// <summary>True when the address string represents a non-zero value (not empty, "0", or "0x0").</summary>
    private static bool IsNonZeroAddr(string? addr)
        => !string.IsNullOrEmpty(addr) && addr != "0" && addr != "0x0" && addr != "0x00000000";

    /// <summary>Strip leading "0x" or "0X" prefix for clipboard copy.</summary>
    private static string StripHexPrefix(string addr)
        => addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? addr[2..] : addr;

    // --- Extra Scan ---

    [RelayCommand]
    private async Task ExtraScanAsync()
    {
        if (_dump == null) return;

        try
        {
            ClearError();
            IsScanning = true;
            ScanComplete = false;
            ScanStatusText = Res.Get("str.Pointers.Scan.Starting");
            ScanResultText = "";
            OnPropertyChanged(nameof(CanExtraScan));

            var startResult = await _dump.StartRescanAsync();

            if (!startResult.ScanningGObjects && !startResult.ScanningGWorld)
            {
                ScanStatusText = Res.Get("str.Pointers.Scan.NothingToScan");
                IsScanning = false;
                OnPropertyChanged(nameof(CanExtraScan));
                return;
            }

            _log?.Info(Constants.LogCatInit,
                $"Extra Scan started: GObjects={startResult.ScanningGObjects}, GWorld={startResult.ScanningGWorld}");

            // Poll status every 1.5 seconds
            while (true)
            {
                await Task.Delay(1500);

                var status = await _dump.GetRescanStatusAsync();
                ScanStatusText = status.StatusText;

                if (!status.Running && status.Phase >= 3)
                {
                    // Scan complete — apply if anything was found
                    ScanComplete = true;

                    if (status.FoundGObjects || status.FoundGWorld)
                    {
                        ScanStatusText = Res.Get("str.Pointers.Scan.Applying");
                        var newState = await _dump.ApplyRescanAsync();

                        var parts = new List<string>();
                        if (status.FoundGObjects) parts.Add($"GObjects: {status.GObjectsAddr}");
                        if (status.FoundGWorld) parts.Add($"GWorld: {status.GWorldAddr}");
                        ScanResultText = $"Found: {string.Join(", ", parts)}";
                        ScanStatusText = Res.Get("str.Pointers.Scan.Applied");

                        _log?.Info(Constants.LogCatInit, $"Extra Scan complete: {ScanResultText}");

                        // Notify MainWindowVM to refresh all panels
                        RescanApplied?.Invoke();
                    }
                    else
                    {
                        ScanResultText = Res.Get("str.Pointers.Scan.NoResults");
                        ScanStatusText = Res.Get("str.Pointers.Scan.CompleteNoResults");
                        _log?.Info(Constants.LogCatInit, "Extra Scan complete: no results");
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            ScanStatusText = Res.Format("str.Pointers.Scan.Error", ex.Message);
            SetError(ex);
            _log?.Error(Constants.LogCatInit, "Extra Scan failed", ex);
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(CanExtraScan));
        }
    }

    // --- Test button: simulate Extra Scan for development testing ---

    [RelayCommand]
    private async Task TestExtraScanAsync()
    {
        if (_dump == null) return;

        try
        {
            ClearError();
            IsScanning = true;
            ScanComplete = false;
            ScanStatusText = Res.Get("str.Pointers.TestScan.Starting");
            ScanResultText = "";
            OnPropertyChanged(nameof(CanExtraScan));

            // Simulate scan phases with delays
            ScanStatusText = Res.Get("str.Pointers.TestScan.GObjects");
            await Task.Delay(2000);

            ScanStatusText = Res.Get("str.Pointers.TestScan.GWorld");
            await Task.Delay(2000);

            // After simulation, do a real get_pointers to show current state
            ScanComplete = true;
            ScanStatusText = Res.Get("str.Pointers.TestScan.Complete");
            ScanResultText = Res.Get("str.Pointers.TestScan.Result");

            _log?.Info(Constants.LogCatInit, "Test Extra Scan simulation complete");
        }
        catch (Exception ex)
        {
            ScanStatusText = Res.Format("str.Pointers.TestScan.Error", ex.Message);
            SetError(ex);
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(CanExtraScan));
        }
    }

    // --- AOBMaker CE Plugin: data pointer → hex view (memory dump) ---

    [RelayCommand]
    private async Task HexGObjectsAsync()
    {
        if (_aobMaker == null || !IsNonZeroAddr(GObjectsAddress)) return;
        await _aobMaker.NavigateHexViewAsync(StripHexPrefix(GObjectsAddress));
    }

    [RelayCommand]
    private async Task HexGNamesAsync()
    {
        if (_aobMaker == null || !IsNonZeroAddr(GNamesAddress)) return;
        await _aobMaker.NavigateHexViewAsync(StripHexPrefix(GNamesAddress));
    }

    [RelayCommand]
    private async Task HexGWorldAsync()
    {
        if (_aobMaker == null || !IsNonZeroAddr(GWorldAddress)) return;
        await _aobMaker.NavigateHexViewAsync(StripHexPrefix(GWorldAddress));
    }

    // --- AOBMaker CE Plugin: scan address → disassembler (code) ---

    [RelayCommand]
    private async Task AsmGObjectsScanAsync()
    {
        if (_aobMaker == null || !IsNonZeroAddr(GObjectsScanAddr)) return;
        await _aobMaker.NavigateDisassemblerAsync(StripHexPrefix(GObjectsScanAddr));
    }

    [RelayCommand]
    private async Task AsmGNamesScanAsync()
    {
        if (_aobMaker == null || !IsNonZeroAddr(GNamesScanAddr)) return;
        await _aobMaker.NavigateDisassemblerAsync(StripHexPrefix(GNamesScanAddr));
    }

    [RelayCommand]
    private async Task AsmGWorldScanAsync()
    {
        if (_aobMaker == null || !IsNonZeroAddr(GWorldScanAddr)) return;
        await _aobMaker.NavigateDisassemblerAsync(StripHexPrefix(GWorldScanAddr));
    }

    // --- AOBMaker CE Plugin: register GWorld as AOB-scan-based CE symbol ---

    [RelayCommand]
    private async Task RegisterGWorldSymbolAsync()
    {
        if (_aobMaker == null || string.IsNullOrEmpty(GworldAob)) return;

        string symbolName = "gworld_addr";
        string module = !string.IsNullOrEmpty(ModuleName) ? ModuleName : "game.exe";

        // Send CreateSymbolScript — the CE Plugin's BuildSymbolScanScript() generates
        // a full AA script that: AOBScanModule for the pattern, reads the RIP-relative
        // displacement at 'pos', calculates final address using 'aoblen', and registers
        // it as a CE symbol. This survives game restarts (re-scans on enable).
        bool success = await _aobMaker.CreateSymbolScriptAsync(
            name: $"GWorld → {symbolName}",
            aob: GworldAob,
            pos: GworldAobPos,
            aoblen: GworldAobLen,
            symbol: symbolName,
            module: module,
            autoActivate: true);

        if (success)
            _log?.Info(Constants.LogCatInit,
                $"Created CE symbol script '{symbolName}' (AOB: {GworldAob}, pos={GworldAobPos}, len={GworldAobLen})");
        else
            _log?.Warn(Constants.LogCatInit,
                $"Failed to create CE symbol script '{symbolName}'");
    }

    // --- Cache management ---

    [RelayCommand]
    private async Task ClearGameCacheAsync()
    {
        if (_aobUsage == null || string.IsNullOrEmpty(PeHash)) return;

        try
        {
            var removed = await _aobUsage.DeleteGameAsync(PeHash);
            CacheStatusText = removed
                ? Res.Get("str.Pointers.Cache.GameCleared")
                : Res.Get("str.Pointers.Cache.GameNotFound");
            _log?.Info(Constants.LogCatInit, $"ClearGameCache: PE={PeHash}, removed={removed}");
        }
        catch (Exception ex)
        {
            CacheStatusText = Res.Format("str.Pointers.Cache.Error", ex.Message);
            _log?.Error(Constants.LogCatInit, "ClearGameCache failed", ex);
        }
    }

    [RelayCommand]
    private async Task ResetAllCacheAsync()
    {
        if (_aobUsage == null) return;

        try
        {
            var success = await _aobUsage.ResetAllAsync();
            CacheStatusText = success
                ? Res.Get("str.Pointers.Cache.AllReset")
                : Res.Get("str.Pointers.Cache.ResetFailed");
            _log?.Info(Constants.LogCatInit, $"ResetAllCache: success={success}");
        }
        catch (Exception ex)
        {
            CacheStatusText = Res.Format("str.Pointers.Cache.Error", ex.Message);
            _log?.Error(Constants.LogCatInit, "ResetAllCache failed", ex);
        }
    }

    // --- Clipboard copy commands ---

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

    [RelayCommand]
    private async Task CopyGObjectsScanAddrAsync()
    {
        if (!string.IsNullOrEmpty(GObjectsScanAddr))
            await _platform.CopyToClipboardAsync(StripHexPrefix(GObjectsScanAddr));
    }

    [RelayCommand]
    private async Task CopyGNamesScanAddrAsync()
    {
        if (!string.IsNullOrEmpty(GNamesScanAddr))
            await _platform.CopyToClipboardAsync(StripHexPrefix(GNamesScanAddr));
    }

    [RelayCommand]
    private async Task CopyGWorldScanAddrAsync()
    {
        if (!string.IsNullOrEmpty(GWorldScanAddr))
            await _platform.CopyToClipboardAsync(StripHexPrefix(GWorldScanAddr));
    }
}
