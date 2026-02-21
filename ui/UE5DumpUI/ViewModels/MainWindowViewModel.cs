using System.IO;
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
    [ObservableProperty] private bool _isConnected;

    // Child ViewModels
    public ObjectTreeViewModel ObjectTree { get; }
    public ClassStructViewModel ClassStruct { get; }
    public PointerPanelViewModel Pointers { get; }
    public HexViewViewModel HexView { get; }
    public LiveWalkerViewModel LiveWalker { get; }
    public InstanceFinderViewModel InstanceFinder { get; }

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
        ObjectTree.SelectionChanged += async (node) =>
        {
            await ClassStruct.OnObjectSelected(node);
            if (node != null)
            {
                HexView.SetAddress(node.Address);
            }
        };

        // Wire InstanceFinder -> LiveWalker navigation
        InstanceFinder.NavigateToLiveWalker += async (addr) =>
        {
            await LiveWalker.NavigateToAddressCommand.ExecuteAsync(addr);
        };

        _pipeClient.ConnectionStateChanged += (connected) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsConnected = connected;
                StatusText = connected ? "Connected" : "Disconnected";
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

            LiveWalker.SetEngineState(state);
            InstanceFinder.SetEngineState(state);

            IsConnected = true;
            StatusText = $"Connected — UE{state.UEVersion} ({state.ObjectCount} objects)";

            _log.Info($"Connected: UE{state.UEVersion}, {state.ObjectCount} objects");
        }
        catch (Exception ex)
        {
            StatusText = "Connection Error";
            SetError(ex);
            _log.Error("Connection failed", ex);
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        try
        {
            ClearError();
            await _pipeClient.DisconnectAsync();
            StatusText = "Disconnected";
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
