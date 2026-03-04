using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
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
    private AobUsageService? _aobUsage;
    private AobMakerBridgeService? _aobMakerBridge;
    private ProxyDeployService? _proxyDeploy;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Remove DataAnnotations validator to avoid duplicate validation with CommunityToolkit.Mvvm.
        // Safe because compiled bindings are enabled (AvaloniaUseCompiledBindingsByDefault=true).
        DisableAvaloniaDataAnnotationValidation();

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
            _aobUsage = new AobUsageService(_platform, _logging);
            _aobMakerBridge = new AobMakerBridgeService(_logging);
            _proxyDeploy = new ProxyDeployService(_logging);

            _logging.Info(Constants.LogCatInit, "UE5DumpUI starting...");
            _logging.Info(Constants.LogCatInit, $"Version:   {typeof(App).Assembly.GetName().Version}");
            _logging.Info(Constants.LogCatInit, $"OS:        {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            _logging.Info(Constants.LogCatInit, $"Runtime:   {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            _logging.Info(Constants.LogCatInit, $"Arch:      {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            _logging.Info(Constants.LogCatInit, $"Log dir:   {logDir}");

            // Create main window
            var mainVm = new MainWindowViewModel(
                _pipeClient, _dumpService, _logging, _platform, _aobUsage, _aobMakerBridge,
                _proxyDeploy);

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVm
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                _logging?.Info(Constants.LogCatInit, "UE5DumpUI shutting down...");
                _pipeClient?.Dispose();
                _aobMakerBridge?.Dispose();
                _platform?.Dispose();
                (_logging as IDisposable)?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // BindingPlugins.DataValidators carries [RequiresUnreferencedCode].
    // Compiled bindings are already enabled project-wide, so removing the
    // DataAnnotations plugin here is safe — the trimmer won't pull in extra reflection paths.
    [UnconditionalSuppressMessage(
        "TrimAnalysis", "IL2026",
        Justification = "AvaloniaUseCompiledBindingsByDefault=true; only removing unused DataAnnotations validators.")]
    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var toRemove = BindingPlugins.DataValidators
            .OfType<DataAnnotationsValidationPlugin>()
            .ToArray();
        foreach (var plugin in toRemove)
            BindingPlugins.DataValidators.Remove(plugin);
    }
}
