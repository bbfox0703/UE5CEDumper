using System.Diagnostics;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Coarse "where in its life was the app?" marker. Only three states, because the
/// point is to keep <c>crash.log</c> honest, not to build a state machine: the
/// file used to hard-code the phrase "startup crash" for every crash, so a fault
/// 31 minutes into a live session was filed as a startup failure and triaged as
/// one (field report <c>[PASTECRASH-2026-08-18]</c>).
/// </summary>
internal enum AppPhase
{
    /// <summary>Process start until the main window has been created and wired.</summary>
    Startup,

    /// <summary>Normal operation — the window exists and the message loop is running.</summary>
    Running,

    /// <summary>A shutdown was requested; services are being torn down.</summary>
    ShuttingDown,
}

/// <summary>
/// Process-wide lifecycle state, read by the top-level crash handler in
/// <c>Program.Main</c>.
///
/// <para>A mutable static is the right shape here and not laziness: the handler
/// that needs these two facts is the <c>catch</c> around
/// <c>StartWithClassicDesktopLifetime</c>, which by definition runs when the
/// object graph that would otherwise carry them has already come apart. The
/// phase is written from exactly three places in <c>App.axaml.cs</c>.</para>
/// </summary>
internal static class AppLifecycle
{
    private static long _startTimestamp = Stopwatch.GetTimestamp();
    private static int _phase = (int)AppPhase.Startup;

    /// <summary>
    /// Anchor t0 at the real entry point. Called first thing in <c>Main</c> so the
    /// uptime is measured from process start rather than from whenever this class
    /// happened to be initialised.
    /// </summary>
    internal static void Begin()
    {
        Interlocked.Exchange(ref _startTimestamp, Stopwatch.GetTimestamp());
        Volatile.Write(ref _phase, (int)AppPhase.Startup);
    }

    /// <summary>Current phase. Written on the UI thread, read from the crash handler
    /// (which may be a different thread), hence the volatile access.</summary>
    internal static AppPhase Phase
    {
        get => (AppPhase)Volatile.Read(ref _phase);
        set => Volatile.Write(ref _phase, (int)value);
    }

    /// <summary>Wall-clock time since <see cref="Begin"/>.</summary>
    internal static TimeSpan Uptime =>
        Stopwatch.GetElapsedTime(Interlocked.Read(ref _startTimestamp));
}
