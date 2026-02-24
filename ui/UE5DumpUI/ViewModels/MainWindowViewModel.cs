using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
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

    [ObservableProperty] private string _statusText = "Disconnected";
    [ObservableProperty] private string _windowTitle = "UE5 Dump UI";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private int _selectedAddressFormatIndex;
    [ObservableProperty] private bool _collapsePointerNodes;

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

    public MainWindowViewModel(
        IPipeClient pipeClient,
        IDumpService dump,
        ILoggingService log,
        IPlatformService platform)
    {
        _pipeClient = pipeClient;
        _dump = dump;
        _log = log;

        ObjectTree = new ObjectTreeViewModel(dump, log, platform);
        ClassStruct = new ClassStructViewModel(dump, log);
        Pointers = new PointerPanelViewModel(platform);
        HexView = new HexViewViewModel(dump, pipeClient, log);
        LiveWalker = new LiveWalkerViewModel(dump, log, platform);
        InstanceFinder = new InstanceFinderViewModel(dump, log, platform);

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

            Pointers.Update(
                state.GObjectsAddr,
                state.GNamesAddr,
                state.GWorldAddr,
                state.UEVersion,
                state.ObjectCount);

            ObjectTree.SetEngineState(state);
            LiveWalker.SetEngineState(state);
            InstanceFinder.SetEngineState(state);
            HexView.SetEngineState(state);

            IsConnected = true;
            StatusText = $"Connected — UE{state.UEVersion} ({state.ObjectCount} objects)";

            // Update window title with process name and start per-process mirror log
            if (!string.IsNullOrEmpty(state.ModuleName))
            {
                WindowTitle = $"UE5 Dump UI — {state.ModuleName}";
                _log.StartProcessMirror(state.ModuleName);
            }

            _log.Info(Constants.LogCatInit, $"Connected: UE{state.UEVersion}, {state.ObjectCount} objects, module={state.ModuleName}");
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
            _log.StopProcessMirror();
            await _pipeClient.DisconnectAsync();
            StatusText = "Disconnected";
            WindowTitle = "UE5 Dump UI";
            IsConnected = false;
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
}
