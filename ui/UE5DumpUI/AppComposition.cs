using Avalonia.Threading;
using UE5DumpUI.Core;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI;

/// <summary>
/// The composition root's one wiring step, extracted so it can be tested.
///
/// <para><b>Why this is not inline in <see cref="App"/>.</b> Audit #4 B27:
/// <c>App.OnFrameworkInitializationCompleted</c> constructed a
/// <see cref="CoordinateLibraryStore"/> and then called <c>new MainWindowViewModel(...)</c>
/// with 11 positional arguments against a 12-parameter constructor. The store bound to its
/// optional <c>null</c> default, was forwarded into <see cref="TeleportViewModel"/>, and
/// every Coordinate Library persistence path early-returned on its null guard — the feature
/// worked in-session and lost everything on restart, with no exception, log line or compiler
/// warning. Nothing could catch it, because <see cref="App"/> only runs under a real Avalonia
/// desktop lifetime and every unit test that built the VM passed its own named arguments.</para>
///
/// <para>Keeping the argument list here — behind a signature whose parameters are all
/// <b>required</b> — means the compiler enforces what optional parameters cannot, and
/// <c>CompositionRootWiringTests</c> exercises the exact code the app runs.</para>
/// </summary>
internal static class AppComposition
{
    /// <summary>
    /// Build the main window's ViewModel graph. Every parameter is required on purpose:
    /// adding a service here must break the call site rather than silently default.
    /// </summary>
    internal static MainWindowViewModel BuildMainWindowViewModel(
        IPipeClient pipeClient,
        IDumpService dump,
        ILoggingService log,
        IPlatformService platform,
        AobUsageService? aobUsage,
        IAobMakerBridge? aobMaker,
        IProxyDeployService? proxyDeploy,
        IExperimentalGate? experimentalGate,
        ISnapshotStore? snapshotStore,
        IGlobalHotkeyService? globalHotkeys,
        BookmarkStore? bookmarks,
        CoordinateLibraryStore? coordLibrary,
        ILogCompressionService? logCompression)
        => new MainWindowViewModel(
            pipeClient, dump, log, platform, aobUsage, aobMaker,
            proxyDeploy, experimentalGate, snapshotStore, globalHotkeys, bookmarks,
            coordLibrary, logCompression);

    /// <summary>
    /// Build the dispatcher fault guard AND subscribe it, as one indivisible step.
    ///
    /// <para><b>Why it is here rather than two lines in <see cref="App"/>.</b> The
    /// guard is the only thing standing between a clipboard/IME fault and a killed
    /// process ([PASTECRASH-2026-08-18]), and a guard that is constructed but never
    /// attached is indistinguishable from one that works: no exception, no log line,
    /// no failing test — the app simply goes back to dying on a bad paste. Deleting
    /// the <c>Attach</c> call used to break nothing. Construction and subscription
    /// are now the same call, so there is no state in which one happened without the
    /// other, and <c>CompositionRootWiringTests</c> exercises this exact method — the
    /// same shape as the B27 chokepoint above.</para>
    /// </summary>
    internal static DispatcherFaultGuard AttachDispatcherFaultGuard(
        ILoggingService log, Dispatcher dispatcher)
    {
        var guard = new DispatcherFaultGuard(log);
        guard.Attach(dispatcher);
        return guard;
    }
}
