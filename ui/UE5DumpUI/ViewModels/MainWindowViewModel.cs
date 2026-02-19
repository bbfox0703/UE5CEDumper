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
    private readonly LocalizationService _localization;

    [ObservableProperty] private string _statusText = "Disconnected";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _selectedLanguage = "en";

    // Child ViewModels
    public ObjectTreeViewModel ObjectTree { get; }
    public ClassStructViewModel ClassStruct { get; }
    public PointerPanelViewModel Pointers { get; }
    public HexViewViewModel HexView { get; }
    public LiveWalkerViewModel LiveWalker { get; }
    public InstanceFinderViewModel InstanceFinder { get; }

    public string[] AvailableLanguages { get; } = ["en", "zh-TW", "ja"];

    public MainWindowViewModel(
        IPipeClient pipeClient,
        IDumpService dump,
        ILoggingService log,
        IPlatformService platform,
        LocalizationService localization)
    {
        _pipeClient = pipeClient;
        _dump = dump;
        _log = log;
        _localization = localization;

        ObjectTree = new ObjectTreeViewModel(dump, log);
        ClassStruct = new ClassStructViewModel(dump, log);
        Pointers = new PointerPanelViewModel(platform);
        HexView = new HexViewViewModel(dump, pipeClient, log);
        LiveWalker = new LiveWalkerViewModel(dump, log);
        InstanceFinder = new InstanceFinderViewModel(dump, log);

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
            await _pipeClient.DisconnectAsync();
            StatusText = "Disconnected";
            IsConnected = false;
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        _localization.SwitchLanguage(value);
    }
}
