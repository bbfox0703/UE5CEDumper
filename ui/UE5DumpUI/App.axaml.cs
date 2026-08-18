using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using UE5DumpUI.Views;

namespace UE5DumpUI;

public class App : Application
{
    // Service instances (simple composition root — no DI container for AOT compatibility)
    private WindowsPlatformService? _platform;
    private LoggingService? _logging;
    private IPipeClient? _pipeClient;
    private DumpService? _dumpService;
    private AobUsageService? _aobUsage;
    private AobMakerBridgeService? _aobMakerBridge;
    private ProxyDeployService? _proxyDeploy;
    private ExperimentalGate? _experimentalGate;
    private SnapshotStore? _snapshotStore;
    private UiOptionsStore? _uiOptions;
    private BookmarkStore? _bookmarkStore;
    private CoordinateLibraryStore? _coordLibraryStore;
    private WindowsLogCompressionService? _logCompression;
    private DispatcherFaultGuard? _faultGuard;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Single instance check
            _platform = new WindowsPlatformService();
            if (!_platform.TryAcquireSingleInstance())
            {
                // Another instance is running. Raise ITS window before exiting — this
                // runs before the logger exists, so a silent Shutdown(1) gives the user
                // no window, no dialog and no log line, and a double-click reads as
                // "the app did nothing". Bringing the first instance forward is both the
                // useful outcome and the visible one. (B42)
                _platform.ActivateExistingInstance();
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                desktop.Shutdown(1);
                return;
            }

            // Opt into Windows "restartable apps": if we're open when the user
            // reboots / installs an update, Windows relaunches us on next
            // sign-in (and the window-state restore below puts us back in place).
            _platform.RegisterForRestart();

            // Initialize services
            var logDir = _platform.GetLogDirectoryPath();
            _logging = new LoggingService(logDir);

            // Attach the dispatcher fault guard as soon as there is somewhere to
            // log to, and before any window exists — a clipboard/IME fault must
            // never again take the process down with it ([PASTECRASH-2026-08-18]).
            // The guard swallows ONLY faults the classifier positively identifies
            // as platform input-layer ones; everything else still crashes and still
            // reaches crash.log.
            _faultGuard = new DispatcherFaultGuard(_logging);
            _faultGuard.Attach(Dispatcher.UIThread);

            // Two-connection lane router (interactive + bulk) — see
            // LaneRoutingPipeClient / docs/multipipe-eval.md §9.
            _pipeClient = new LaneRoutingPipeClient(_logging);
            _dumpService = new DumpService(_pipeClient, _logging);
            _aobUsage = new AobUsageService(_platform, _logging);
            _aobMakerBridge = new AobMakerBridgeService(_logging);
            _proxyDeploy = new ProxyDeployService(_logging, _platform);
            _experimentalGate = new ExperimentalGate(_platform, _logging);
            _snapshotStore = new SnapshotStore(_platform, _logging);
            _uiOptions = new UiOptionsStore(_platform, _logging);
            _bookmarkStore = new BookmarkStore(_platform, _logging);
            _coordLibraryStore = new CoordinateLibraryStore(_platform, _logging);
            _logCompression = new WindowsLogCompressionService(_logging);

            _logging.Info(Constants.LogCatInit, "UE5DumpUI starting...");
            _logging.Info(Constants.LogCatInit, $"Version:   {typeof(App).Assembly.GetName().Version}");
            _logging.Info(Constants.LogCatInit, $"OS:        {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            _logging.Info(Constants.LogCatInit, $"Runtime:   {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            _logging.Info(Constants.LogCatInit, $"Arch:      {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            _logging.Info(Constants.LogCatInit, $"Log dir:   {logDir}");

            // Leave a breadcrumb for scripts/UE5CEDumper.CT. A CE table script cannot
            // read its own .CT path, so it infers the folder from CE's Open/Save
            // dialogs — which a double-click in Explorer never fills in, leaving the
            // DLL undiscoverable even when it sits right beside the .CT. We know
            // exactly where it is, so record it. Only when it really is there: a
            // breadcrumb pointing at nothing would just add a dead search slot.
            try
            {
                var exeDir = AppContext.BaseDirectory;
                if (File.Exists(Path.Combine(exeDir, "UE5Dumper.dll")))
                    new DumperDllPathStore(_platform).Record(exeDir);
            }
            catch (Exception ex)
            {
                _logging.Warn(Constants.LogCatInit, $"DLL path breadcrumb not written: {ex.Message}");
            }

            // Create main window
            var globalHotkeys = new WindowsGlobalHotkeyService();
            // Wiring lives in AppComposition so a test can exercise this exact call.
            // Do NOT inline it back: MainWindowViewModel's service parameters are all
            // optional, so an omitted argument compiles and silently disables a whole
            // feature (audit #4 B27). AppComposition's parameters are required.
            var mainVm = AppComposition.BuildMainWindowViewModel(
                _pipeClient, _dumpService, _logging, _platform, _aobUsage, _aobMakerBridge,
                _proxyDeploy, _experimentalGate, _snapshotStore, globalHotkeys, _bookmarkStore,
                _coordLibraryStore, _logCompression);

            // Load + apply persisted panel options, then track changes for
            // debounced save-on-change. Done before the window is shown so the
            // restored values are in place for the first render.
            mainVm.InitializeOptionsPersistence(_uiOptions);

            // Opt-in startup log compression (default OFF). Read from the VM rather than
            // re-loading ui-options.json, so what runs is exactly what the checkbox shows.
            //
            // Fire-and-forget on a thread-pool thread: it must not delay the window, and
            // nothing downstream depends on it. LoggingService's constructor has already
            // rotated and age-pruned by now, so this never compresses a file that is about
            // to be deleted. Errors are the service's own to log — a failed sweep must not
            // surface as a startup exception.
            if (mainVm.Pointers.AutoCompressLogs)
            {
                var svc = _logCompression;
                var dir = logDir;
                var logger = _logging;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var r = await svc.CompressAsync(
                            dir,
                            TimeSpan.FromDays(Constants.LogAutoCompressMinAgeDays),
                            Constants.LogCompressMinSizeBytes);
                        logger.Info(Constants.LogCatInit,
                            $"Auto log compression: {r.Compressed} file(s), " +
                            $"{r.BytesSaved / 1024}KB saved, {r.Failed} failed");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(Constants.LogCatInit, $"Auto log compression failed: {ex.Message}");
                    }
                });
            }

            // Restore last-session window placement (position / size / maximized,
            // validated against the monitors present this session). Attached
            // before the window is shown so there's no visible reposition.
            var windowStateStore = new WindowStateStore(_platform);
            var mainWindow = new MainWindow { DataContext = mainVm };
            mainWindow.AttachWindowState(windowStateStore);
            desktop.MainWindow = mainWindow;

            desktop.ShutdownRequested += (_, _) =>
            {
                AppLifecycle.Phase = AppPhase.ShuttingDown;
                _logging?.Info(Constants.LogCatInit, "UE5DumpUI shutting down...");
                // Flush any pending debounced option change before teardown.
                mainVm.FlushOptions();
                _pipeClient?.Dispose();
                _aobMakerBridge?.Dispose();
                _platform?.Dispose();
                (_logging as IDisposable)?.Dispose();
            };

            // Everything the window needs is wired; from here a crash is NOT a
            // startup crash, and crash.log must stop calling it one.
            AppLifecycle.Phase = AppPhase.Running;
        }

        base.OnFrameworkInitializationCompleted();
    }

}
