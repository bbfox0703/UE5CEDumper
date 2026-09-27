using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Xunit;

namespace UE5DumpUI.HeadlessTests;

/// <summary>The application the headless tests run in: the app's theme, nothing else — none of
/// UE5DumpUI's own startup (services, pipes, the single-instance mutex).</summary>
public sealed class HeadlessTestApp : Application
{
    // A TextBox without its template has no presenter and ignores every key, so the theme
    // is what makes the controls under test real.
    public override void Initialize() => Styles.Add(new FluentTheme());

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
