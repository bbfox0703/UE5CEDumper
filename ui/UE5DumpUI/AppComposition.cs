using Avalonia.Threading;
using UE5DumpUI.Core;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI;

/// <summary>
/// The composition root's one wiring step, extracted so it can be tested.
///
/// <para><b>Why this is not inline in <see cref="App"/>.</b> A service the call site forgets to
/// pass to an optional constructor parameter binds to its <c>null</c> default with no exception,
/// log line or compiler warning, and every feature behind that service's null guard goes quiet.
/// <see cref="App"/> only runs under a real Avalonia desktop lifetime, so no unit test reaches
/// its wiring.</para>
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
        ILogCompressionService? logCompression,
        IAobMakerUiClient? aobMakerUi,
        IAiWriteBlockGate? aiWriteBlock)
        => new MainWindowViewModel(
            pipeClient, dump, log, platform, aobUsage, aobMaker,
            proxyDeploy, experimentalGate, snapshotStore, globalHotkeys, bookmarks,
            coordLibrary, logCompression, aobMakerUi, aiWriteBlock);

    /// <summary>
    /// Build the dispatcher fault guard AND subscribe it, as one indivisible step.
    ///
    /// <para><b>Why it is here rather than two lines in <see cref="App"/>.</b> The
    /// guard is the only thing standing between a clipboard/IME fault and a killed
    /// process ([PASTECRASH-2026-08-18]), and a guard that is constructed but never
    /// attached is indistinguishable from one that works: no exception, no log line,
    /// no failing test — the app simply goes back to dying on a bad paste. Construction
    /// and subscription are the same call, so there is no state in which one happened
    /// without the other, and <c>CompositionRootWiringTests</c> exercises this exact
    /// method — the same shape as <see cref="BuildMainWindowViewModel"/> above.</para>
    /// </summary>
    internal static DispatcherFaultGuard AttachDispatcherFaultGuard(
        ILoggingService log, Dispatcher dispatcher)
    {
        var guard = new DispatcherFaultGuard(log);
        guard.Attach(dispatcher);
        return guard;
    }
}
