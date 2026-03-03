using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// Main window ViewModel — orchestrates connection and child ViewModels.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IPipeClient _pipeClient;
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService _platform;
    private readonly AobUsageService? _aobUsage;
    private EngineState? _engineState;

    [ObservableProperty] private string _statusText = "Disconnected";
    [ObservableProperty] private string _windowTitle = "UE5 Dump UI";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _needsScan;       // True when connected but scan not yet done (proxy DLL mode)
    [ObservableProperty] private bool _isScanning;      // True while trigger_scan is in progress
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private int _selectedAddressFormatIndex;
    [ObservableProperty] private bool _collapsePointerNodes;
    [ObservableProperty] private int _arrayLimitExponent = 6; // 2^6 = 64
    [ObservableProperty] private int _dropDownLimitExponent = 9; // 2^9 = 512
    [ObservableProperty] private int _csxDrilldownDepth; // 0 = flat (dummy), 1+ = real child structures

    /// <summary>Computed array element limit: 2^ArrayLimitExponent (2..16384).</summary>
    public int ArrayLimit => 1 << ArrayLimitExponent;

    /// <summary>Computed CE DropDownList max entries: 2^DropDownLimitExponent (64..8192).</summary>
    public int DropDownLimit => 1 << DropDownLimitExponent;

    /// <summary>Show warning when array limit &gt;= 256 (high memory usage).</summary>
    public bool ShowArrayLimitWarning => ArrayLimitExponent >= 8;

    /// <summary>Address format options for toolbar ComboBox.</summary>
    public string[] AddressFormatOptions { get; } =
    [
        "Hex (no prefix)",
        "Hex (0x prefix)",
        "Module+Offset",
    ];

    /// <summary>
    /// Application version string read from assembly metadata (e.g. "v1.0.0.37").
    /// </summary>
    public string AppVersion { get; } = GetAppVersion();

    private static string GetAppVersion()
    {
        var ver = Assembly.GetEntryAssembly()?.GetName().Version;
        return ver != null ? $"v{ver}" : "";
    }

    // Child ViewModels
    public ObjectTreeViewModel ObjectTree { get; }
    public ClassStructViewModel ClassStruct { get; }
    public PointerPanelViewModel Pointers { get; }
    public HexViewViewModel HexView { get; }
    public LiveWalkerViewModel LiveWalker { get; }
    public InstanceFinderViewModel InstanceFinder { get; }
    public PropertySearchViewModel PropertySearch { get; }
    public GameClassFilterViewModel GameClassFilter { get; }

    partial void OnSelectedAddressFormatIndexChanged(int value)
    {
        ObjectTree.SelectedAddressFormatIndex = value;
        LiveWalker.SelectedAddressFormatIndex = value;
        InstanceFinder.SelectedAddressFormatIndex = value;
    }

    partial void OnCollapsePointerNodesChanged(bool value)
    {
        LiveWalker.CollapsePointerNodes = value;
        InstanceFinder.CollapsePointerNodes = value;
    }

    partial void OnArrayLimitExponentChanged(int value)
    {
        OnPropertyChanged(nameof(ArrayLimit));
        OnPropertyChanged(nameof(ShowArrayLimitWarning));
        LiveWalker.ArrayLimit = ArrayLimit;
        InstanceFinder.ArrayLimit = ArrayLimit;
    }

    partial void OnDropDownLimitExponentChanged(int value)
    {
        OnPropertyChanged(nameof(DropDownLimit));
        LiveWalker.DropDownLimit = DropDownLimit;
        InstanceFinder.DropDownLimit = DropDownLimit;
    }

    partial void OnCsxDrilldownDepthChanged(int value)
    {
        LiveWalker.CsxDrilldownDepth = value;
    }

    public MainWindowViewModel(
        IPipeClient pipeClient,
        IDumpService dump,
        ILoggingService log,
        IPlatformService platform,
        AobUsageService? aobUsage = null,
        IAobMakerBridge? aobMaker = null)
    {
        _pipeClient = pipeClient;
        _dump = dump;
        _log = log;
        _platform = platform;
        _aobUsage = aobUsage;

        ObjectTree = new ObjectTreeViewModel(dump, log, platform);
        ClassStruct = new ClassStructViewModel(dump, log);
        Pointers = new PointerPanelViewModel(platform, dump, log, aobMaker);
        HexView = new HexViewViewModel(dump, pipeClient, log);
        LiveWalker = new LiveWalkerViewModel(dump, log, platform);
        InstanceFinder = new InstanceFinderViewModel(dump, log, platform);
        PropertySearch = new PropertySearchViewModel(dump, log);
        GameClassFilter = new GameClassFilterViewModel(dump, log);

        // Wire Pointers Extra Scan -> refresh all panels after rescan results applied
        Pointers.RescanApplied += async () =>
        {
            try
            {
                var state = await _dump.GetPointersAsync();
                _engineState = state;

                Pointers.Update(
                    state.GObjectsAddr, state.GNamesAddr, state.GWorldAddr,
                    state.UEVersion, state.VersionDetected, state.ObjectCount,
                    state.GObjectsMethod, state.GNamesMethod, state.GWorldMethod,
                    state.GObjectsPatternId, state.GNamesPatternId, state.GWorldPatternId,
                    state.GObjectsPatternsHit, state.GNamesPatternsHit, state.GWorldPatternsHit,
                    state.GObjectsScanAddr, state.GNamesScanAddr, state.GWorldScanAddr);

                ObjectTree.SetEngineState(state);
                LiveWalker.SetEngineState(state);
                InstanceFinder.SetEngineState(state);
                HexView.SetEngineState(state);

                StatusText = $"Connected — UE{state.UEVersion} ({state.ObjectCount} objects)";

                // Re-load objects if tree was empty
                if (ObjectTree.ObjectCount == 0 && state.ObjectCount > 0)
                    _ = ObjectTree.LoadCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                _log.Error("RescanApplied refresh error", ex);
            }
        };

        // Wire cross-VM communication
        // Wrap async lambdas in try/catch to prevent async void from crashing the app
        ObjectTree.SelectionChanged += async (node) =>
        {
            try
            {
                await ClassStruct.OnObjectSelected(node);
                if (node != null)
                {
                    HexView.SetAddress(node.Address);
                }
            }
            catch (Exception ex)
            {
                _log.Error("SelectionChanged handler error", ex);
            }
        };

        // Wire InstanceFinder -> LiveWalker navigation + tab switch
        InstanceFinder.NavigateToLiveWalker += async (addr) =>
        {
            try
            {
                SelectedTabIndex = 0; // Switch to Live Walker tab
                await LiveWalker.NavigateToAddressCommand.ExecuteAsync(addr);
            }
            catch (Exception ex)
            {
                _log.Error("NavigateToLiveWalker handler error", ex);
            }
        };

        // Wire PropertySearch -> InstanceFinder (pre-fill class name + switch tab)
        PropertySearch.NavigateToInstanceFinder += (className) =>
        {
            try
            {
                SelectedTabIndex = 1; // Switch to Instance Finder tab
                InstanceFinder.SearchClassName = className;
            }
            catch (Exception ex)
            {
                _log.Error("NavigateToInstanceFinder handler error", ex);
            }
        };

        // Wire PropertySearch -> LiveWalker navigation + tab switch
        PropertySearch.NavigateToLiveWalker += async (addr) =>
        {
            try
            {
                SelectedTabIndex = 0; // Switch to Live Walker tab
                await LiveWalker.NavigateToAddressCommand.ExecuteAsync(addr);
            }
            catch (Exception ex)
            {
                _log.Error("PropertySearch NavigateToLiveWalker handler error", ex);
            }
        };

        // Wire GameClassFilter -> InstanceFinder (pre-fill class name + switch tab)
        GameClassFilter.NavigateToInstanceFinder += (className) =>
        {
            try
            {
                SelectedTabIndex = 1; // Switch to Instance Finder tab
                InstanceFinder.SearchClassName = className;
            }
            catch (Exception ex)
            {
                _log.Error("GameClassFilter NavigateToInstanceFinder handler error", ex);
            }
        };

        // Wire GameClassFilter -> LiveWalker navigation + tab switch
        GameClassFilter.NavigateToLiveWalker += async (addr) =>
        {
            try
            {
                SelectedTabIndex = 0; // Switch to Live Walker tab
                await LiveWalker.NavigateToAddressCommand.ExecuteAsync(addr);
            }
            catch (Exception ex)
            {
                _log.Error("GameClassFilter NavigateToLiveWalker handler error", ex);
            }
        };

        // Wire GameClassFilter -> ClassStruct (walk class schema + switch tab)
        GameClassFilter.NavigateToClassStruct += async (classAddr) =>
        {
            try
            {
                SelectedTabIndex = 4; // Switch to ClassStruct tab (index shifted by GameClassFilter)
                await ClassStruct.LoadClassCommand.ExecuteAsync(classAddr);
            }
            catch (Exception ex)
            {
                _log.Error("GameClassFilter NavigateToClassStruct handler error", ex);
            }
        };

        _pipeClient.ConnectionStateChanged += (connected) =>
        {
            if (!connected) _log.StopProcessMirror();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsConnected = connected;
                StatusText = connected ? "Connected" : "Disconnected";
                if (!connected) WindowTitle = "UE5 Dump UI";
            });
        };
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            ClearError();
            StatusText = "Connecting...";

            await _pipeClient.ConnectAsync();

            var state = await _dump.InitAsync();
            _engineState = state;
            IsConnected = true;

            // Detect proxy DLL mode: connected but scan not yet done
            // (UE version 0 and all pointers at 0x0 / empty)
            bool notScanned = state.UEVersion == 0
                && state.ObjectCount == 0
                && (string.IsNullOrEmpty(state.GObjectsAddr) || state.GObjectsAddr == "0x0");

            if (notScanned)
            {
                NeedsScan = true;
                StatusText = "Connected — waiting for scan (load a save first, then click Start Scan)";

                if (!string.IsNullOrEmpty(state.ModuleName))
                {
                    WindowTitle = $"UE5 Dump UI — {state.ModuleName}";
                    _log.StartProcessMirror(state.ModuleName);
                }

                _log.Info(Constants.LogCatInit, "Connected (proxy mode — scan not yet triggered)");
            }
            else
            {
                NeedsScan = false;
                ApplyEngineState(state);
            }
        }
        catch (Exception ex)
        {
            StatusText = "Connection Error";
            SetError(ex);
            _log.Error(Constants.LogCatInit, "Connection failed", ex);
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        try
        {
            ClearError();
            ObjectTree.CancelLoadCommand.Execute(null);
            _log.StopProcessMirror();
            await _pipeClient.DisconnectAsync();
            StatusText = "Disconnected";
            WindowTitle = "UE5 Dump UI";
            IsConnected = false;
            NeedsScan = false;
            IsScanning = false;
        }
        catch (OperationCanceledException)
        {
            // Expected during disconnect
            StatusText = "Disconnected";
            IsConnected = false;
        }
        catch (Exception ex)
        {
            // Suppress pipe-related errors during disconnect
            if (ex is IOException or ObjectDisposedException)
            {
                StatusText = "Disconnected";
                IsConnected = false;
            }
            else
            {
                SetError(ex);
            }
        }
    }

    /// <summary>
    /// Apply a fully-scanned engine state to all child ViewModels.
    /// Shared between ConnectAsync (normal mode) and TriggerScanAsync (proxy mode).
    /// </summary>
    private void ApplyEngineState(EngineState state)
    {
        Pointers.Update(
            state.GObjectsAddr, state.GNamesAddr, state.GWorldAddr,
            state.UEVersion, state.VersionDetected, state.ObjectCount,
            state.GObjectsMethod, state.GNamesMethod, state.GWorldMethod,
            state.GObjectsPatternId, state.GNamesPatternId, state.GWorldPatternId,
            state.GObjectsPatternsHit, state.GNamesPatternsHit, state.GWorldPatternsHit,
            state.GObjectsScanAddr, state.GNamesScanAddr, state.GWorldScanAddr);

        ObjectTree.SetEngineState(state);
        LiveWalker.SetEngineState(state);
        InstanceFinder.SetEngineState(state);
        HexView.SetEngineState(state);

        // Fire-and-forget: persist AOB usage data (failure must not block UI)
        if (_aobUsage != null)
            _ = _aobUsage.RecordScanAsync(state);

        StatusText = $"Connected — UE{state.UEVersion} ({state.ObjectCount} objects)";

        if (!string.IsNullOrEmpty(state.ModuleName))
        {
            WindowTitle = $"UE5 Dump UI — {state.ModuleName}";
            _log.StartProcessMirror(state.ModuleName);
        }

        _log.Info(Constants.LogCatInit, $"Connected: UE{state.UEVersion}, {state.ObjectCount} objects, module={state.ModuleName}");

        // Auto-load objects
        _ = ObjectTree.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Trigger AOB scan from the UI. Used in proxy DLL mode where the DLL starts
    /// the pipe server without scanning. The user clicks "Start Scan" after the
    /// game has loaded a save / reached the main world.
    /// </summary>
    [RelayCommand]
    private async Task TriggerScanAsync()
    {
        try
        {
            ClearError();
            IsScanning = true;
            StatusText = "Scanning...";

            var state = await _dump.TriggerScanAsync();
            _engineState = state;
            NeedsScan = false;
            IsScanning = false;
            ApplyEngineState(state);
        }
        catch (Exception ex)
        {
            IsScanning = false;
            StatusText = "Scan failed";
            SetError(ex);
            _log.Error(Constants.LogCatInit, "TriggerScan failed", ex);
        }
    }

    // --- Export Commands ---

    [RelayCommand]
    private async Task ExportSymbolsX64dbgAsync()
    {
        await ExportSymbolsAsync("x64dbg Database (*.dd64)", ".dd64",
            (symbols, moduleName) => SymbolExportService.GenerateX64dbgDatabase(symbols, moduleName));
    }

    [RelayCommand]
    private async Task ExportSymbolsGhidraAsync()
    {
        await ExportSymbolsAsync("Ghidra Symbols (*.txt)", ".txt",
            (symbols, _) => SymbolExportService.GenerateGhidraSymbols(symbols));
    }

    [RelayCommand]
    private async Task ExportSymbolsIdaAsync()
    {
        await ExportSymbolsAsync("IDA Script (*.idc)", ".idc",
            (symbols, _) => SymbolExportService.GenerateIdaScript(symbols));
    }

    private async Task ExportSymbolsAsync(
        string filterName, string filterExtension,
        Func<IReadOnlyList<SymbolEntry>, string, string> generator)
    {
        if (_engineState == null) return;

        try
        {
            ClearError();
            var moduleName = _engineState.ModuleName;
            if (string.IsNullOrEmpty(moduleName)) moduleName = "game.exe";
            var safeModule = Path.GetFileNameWithoutExtension(moduleName);

            var filePath = await _platform.ShowSaveFileDialogAsync(
                $"{safeModule}_symbols", filterName, filterExtension);
            if (string.IsNullOrEmpty(filePath)) return;

            StatusText = "Collecting symbols...";

            var progress = new Progress<string>(msg =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText = msg));

            var symbols = await SymbolExportService.CollectSymbolsAsync(
                _dump, moduleName, _engineState.ModuleBase, progress);

            StatusText = "Writing file...";
            var content = generator(symbols, moduleName);
            await File.WriteAllTextAsync(filePath, content);

            StatusText = $"Exported {symbols.Count} symbols";
            _log.Info($"Symbols exported to {filePath} ({symbols.Count} entries)");
        }
        catch (Exception ex)
        {
            StatusText = "Export failed";
            SetError(ex);
            _log.Error("Symbol export failed", ex);
        }
    }

    [RelayCommand]
    private async Task ExportFullSdkAsync()
    {
        if (_engineState == null) return;

        try
        {
            ClearError();
            var moduleName = _engineState.ModuleName;
            if (string.IsNullOrEmpty(moduleName)) moduleName = "game";
            var safeModule = Path.GetFileNameWithoutExtension(moduleName);

            var filePath = await _platform.ShowSaveFileDialogAsync(
                $"{safeModule}_SDK", "C++ Header (*.h)", ".h");
            if (string.IsNullOrEmpty(filePath)) return;

            StatusText = "Generating SDK...";
            var progress = new Progress<string>(msg =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText = msg));

            var content = await SdkExportService.GenerateFullSdkAsync(_dump, progress);
            await File.WriteAllTextAsync(filePath, content);

            StatusText = "SDK exported";
            _log.Info($"Full SDK exported to {filePath}");
        }
        catch (Exception ex)
        {
            StatusText = "Export failed";
            SetError(ex);
            _log.Error("Full SDK export failed", ex);
        }
    }

    [RelayCommand]
    private async Task ExportUsmapAsync()
    {
        if (_engineState == null) return;

        try
        {
            ClearError();
            var moduleName = _engineState.ModuleName;
            if (string.IsNullOrEmpty(moduleName)) moduleName = "game";
            var safeModule = Path.GetFileNameWithoutExtension(moduleName);

            var filePath = await _platform.ShowSaveFileDialogAsync(
                $"{safeModule}", "USMAP (*.usmap)", ".usmap");
            if (string.IsNullOrEmpty(filePath)) return;

            StatusText = "Generating USMAP...";
            var progress = new Progress<string>(msg =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText = msg));

            var bytes = await UsmapExportService.GenerateUsmapAsync(_dump, progress);
            await File.WriteAllBytesAsync(filePath, bytes);

            StatusText = "USMAP exported";
            _log.Info($"USMAP exported to {filePath} ({bytes.Length} bytes)");
        }
        catch (Exception ex)
        {
            StatusText = "Export failed";
            SetError(ex);
            _log.Error("USMAP export failed", ex);
        }
    }
}
