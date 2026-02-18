using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using UE5DumpUI.Core;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using UE5DumpUI.Views;

namespace UE5DumpUI;

public class App : Application
{
    // Service instances (simple composition root — no DI container for AOT compatibility)
    private WindowsPlatformService? _platform;
    private LoggingService? _logging;
    private PipeClient? _pipeClient;
    private DumpService? _dumpService;
    private LocalizationService? _localization;

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
                // Another instance is running
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                desktop.Shutdown(1);
                return;
            }

            // Initialize services
            var logDir = _platform.GetLogDirectoryPath();
            _logging = new LoggingService(logDir);
            _pipeClient = new PipeClient(_logging);
            _dumpService = new DumpService(_pipeClient, _logging);
            _localization = new LocalizationService();

            _logging.Info("UE5DumpUI starting...");
            _logging.Info($"Version:   {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            _logging.Info($"OS:        {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            _logging.Info($"Runtime:   {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            _logging.Info($"Arch:      {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            _logging.Info($"Log dir:   {logDir}");

            // Create main window
            var mainVm = new MainWindowViewModel(
                _pipeClient, _dumpService, _logging, _platform, _localization);

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVm
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                _logging?.Info("UE5DumpUI shutting down...");
                _pipeClient?.Dispose();
                _platform?.Dispose();
                (_logging as IDisposable)?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
