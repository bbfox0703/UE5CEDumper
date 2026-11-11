using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Xunit;

namespace UE5DumpUI.HeadlessTests;

/// <summary>The application the headless tests run in: the app's theme, nothing else — none of
/// UE5DumpUI's own startup (services, pipes, the single-instance mutex).</summary>
public sealed class HeadlessTestApp : Application
{
    public override void Initialize()
    {
        // A TextBox without its template has no presenter and ignores every key, so the theme
        // is what makes the controls under test real.
        Styles.Add(new FluentTheme());
        BindAppAssembly();
    }

    /// <summary>
    /// [HEADLESS-AVARES-2026-11-11] Avalonia answers "which assembly is avares://UE5DumpUI/..." with the shortest
    /// LOADED module whose file name STARTS with "UE5DumpUI", and keeps that answer for the session. Until UE5DumpUI.dll
    /// is loaded, the module that matches is this test assembly, which has no compiled XAML, so every avares://UE5DumpUI
    /// load for the rest of the run threw "No precompiled XAML found". Whichever test body reached the dispatcher first
    /// decided it: one that loads en.axaml before touching any UE5DumpUI type failed the run (build 3705: 49 of 71).
    /// So the app's assembly is loaded, and the answer settled and checked, before any test body runs.
    /// </summary>
    private static void BindAppAssembly()
    {
        var app = typeof(global::UE5DumpUI.App).Assembly;
        var resolved = AssetLoader.GetAssembly(new Uri("avares://UE5DumpUI/"));
        if (resolved != app)
            throw new InvalidOperationException(
                $"avares://UE5DumpUI resolves to {resolved?.GetName().Name ?? "nothing"}, not {app.GetName().Name}: "
                + "every test that loads the app's XAML by URI would fail with 'No precompiled XAML found'.");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<HeadlessTestApp>()
                     .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>
/// One headless session for the whole assembly. Every test body runs as ONE job on its
/// dispatcher thread (<see cref="Run"/>), so tests never interleave on the UI thread even
/// though xunit runs classes in parallel — which is what lets a test install and remove a
/// global class handler inside its own body.
/// </summary>
internal static class Headless
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(
        () => HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp), AvaloniaTestIsolationLevel.PerAssembly));

    public static Task Run(Action body) => Session.Value.Dispatch(body, TestContext.Current.CancellationToken);
}
