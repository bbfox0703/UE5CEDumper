using Avalonia;
using Avalonia.Win32;

namespace UE5DumpUI;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // AOT: WinUI Composition via MicroCom COM interop crashes on Native AOT.
            // Force software redirection surface to bypass the compositor COM path.
            .With(new Win32PlatformOptions
            {
                CompositionMode = [Win32CompositionMode.RedirectionSurface]
            })
            .WithInterFont();
}
