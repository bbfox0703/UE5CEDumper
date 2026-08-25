using Avalonia;
using Avalonia.Win32;
using UE5DumpUI.Helpers;

namespace UE5DumpUI;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Anchor the uptime clock at the real entry point, so crash.log can say how
        // long the process had been alive. Cheap (one Stopwatch timestamp) and it
        // must precede the elevated-inject branch below, which is also a process
        // that can crash.
        AppLifecycle.Begin();

        // Headless elevated inject helper: when the main (non-elevated) UI hits an
        // Access-Denied inject (the game runs as Administrator), it relaunches THIS
        // exe with `--inject-elevated <pid> <dll> <resultFile>` via UAC. We do just
        // the injection, write the result to resultFile, and exit — no GUI, no
        // single-instance mutex. Must run BEFORE BuildAvaloniaApp().
        if (args.Length >= 4 && args[0] == "--inject-elevated")
            return RunElevatedInject(args[1], args[2], args[3]);

        // AOT publish gives gutted stack traces (no PDB lookup at runtime,
        // inlining opaque). A crash in the compositor thread would otherwise
        // leave the user with "the exe just closed" and no signal. This
        // top-level catch writes the full exception to
        // %LOCALAPPDATA%\UE5CEDumper\crash.log — the only diagnostic surface
        // for AOT failures (aot-pitfalls.md §0.17 / §2 / §8.3).
        //
        // It catches far more than STARTUP: anything the dispatcher rethrows
        // during the message loop unwinds through StartWithClassicDesktopLifetime
        // and lands here. The report used to hard-code the phrase "startup crash"
        // anyway, which mislabelled a fault 31 minutes into a live session
        // ([PASTECRASH-2026-08-18]); CrashReportFormatter now states the real
        // phase and uptime.
        try
        {
            // RETURN the lifetime's exit code, don't discard it (audit #5 AF10).
            // StartWithClassicDesktopLifetime hands back whatever `desktop.Shutdown(n)`
            // was given, and App.axaml.cs deliberately passes 1 on the second-instance
            // path — which this method then reported to the shell as 0. Anything
            // scripting the exe (a launcher, a CI step, `Start-Process -Wait`) was told
            // the launch succeeded when it had refused to start.
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Constants.LogFolderName);
                Directory.CreateDirectory(logDir);
                File.WriteAllText(
                    Path.Combine(logDir, Constants.CrashLogFileName),
                    CrashReportFormatter.Format(
                        DateTimeOffset.Now, AppLifecycle.Phase, AppLifecycle.Uptime, ex));
            }
            catch { /* best effort — nothing more we can do if even this fails */ }
            return 1;
        }
    }

    /// <summary>Headless elevated inject: inject the DLL and write "OK &lt;hex&gt;" or the
    /// error to <paramref name="resultFile"/>. Returns 0 on success. No GUI.</summary>
    private static int RunElevatedInject(string pidArg, string dllPath, string resultFile)
    {
        try
        {
            if (!int.TryParse(pidArg, out int pid))
            {
                File.WriteAllText(resultFile, "Invalid PID.");
                return 2;
            }
            var result = new Services.WindowsPlatformService().InjectDll(pid, dllPath);
            File.WriteAllText(resultFile,
                result.Ok ? $"OK {result.HModule:X}" : (result.ErrorMessage ?? "inject failed"));
            return result.Ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(resultFile, "Elevated inject error: " + ex.Message); } catch { }
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            // Windows-only tool (injects into Windows games), so wire the Win32 +
            // Skia backends EXPLICITLY instead of UsePlatformDetect(). This drops
            // the Avalonia.Desktop meta-package (which dragged in the X11 / macOS
            // Native / FreeDesktop backends + Tmds.DBus), eliminating their
            // "will always throw" ILC AOT warnings — those code paths can never
            // run on Windows. UsePlatformDetect() itself lives in Avalonia.Desktop,
            // so it's gone too.
            .UseWin32()
            .UseSkia()
            // Text shaping — UsePlatformDetect() wired this for us; the explicit
            // backend must call it or AppBuilder.Setup() throws "No text shaping
            // system configured".
            .UseHarfBuzz()
            // AOT: WinUI Composition via MicroCom COM interop crashes on Native AOT.
            // Force software redirection surface to bypass the compositor COM path.
            .With(new Win32PlatformOptions
            {
                CompositionMode = [Win32CompositionMode.RedirectionSurface]
            })
            .WithInterFont();
}
