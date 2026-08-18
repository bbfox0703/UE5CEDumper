using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Composition-root wiring tests — the class of defect no other test can catch.
///
/// <para><b>Why this file exists.</b> Audit #4 B27: <c>App.axaml.cs</c> constructed a
/// <see cref="CoordinateLibraryStore"/> and then called
/// <c>new MainWindowViewModel(...)</c> with 11 positional arguments, while the
/// constructor's 12th parameter is <c>CoordinateLibraryStore? coordLibrary = null</c>.
/// The store bound to its default, was forwarded as null into
/// <see cref="TeleportViewModel"/>, and every Coordinate Library persistence path
/// silently early-returned on its null guard. The whole feature never persisted — no
/// exception, no log line, no compiler warning.</para>
///
/// <para><b>Why the existing tests missed it.</b> Every other test that builds
/// <c>MainWindowViewModel</c> passes <i>named</i> arguments for the handful of services
/// it cares about (see <c>MainWindowInjectHelperTests.BuildVm</c>, 7 named args). That
/// is the right thing for a unit test and structurally incapable of catching a
/// composition-root omission: the test supplies what it needs, so it can never notice
/// what <c>App</c> forgot.</para>
///
/// <para><b>Why the wiring moved to <see cref="AppComposition"/>.</b> A test that builds
/// the VM itself would have the same blind spot — it would assert that <i>the test</i>
/// passes the store. The construction is therefore behind one helper that both
/// <c>App</c> and this file call, whose parameters are all <b>required</b>, so the
/// compiler enforces what optional parameters cannot.</para>
///
/// <para><b>The rule this file enforces.</b> A service constructed at the composition
/// root must arrive at the object that uses it. Optional constructor parameters make
/// that a runtime property, not a compile-time one, so it needs both a test and a
/// required-parameter chokepoint.</para>
/// </summary>
public class CompositionRootWiringTests : IDisposable
{
    private readonly string _dir;

    public CompositionRootWiringTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ue5cd-wiring-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>
    /// Calls the SAME method <c>App.OnFrameworkInitializationCompleted</c> calls, so the
    /// wiring under test is the wiring the app runs. Building a
    /// <see cref="MainWindowViewModel"/> directly here would prove nothing: it would
    /// assert that <i>this test</i> passes the store, not that <c>App</c> does.
    /// </summary>
    private MainWindowViewModel BuildVmAsAppDoes()
    {
        var platform = new MockPlatformService(_dir);
        return AppComposition.BuildMainWindowViewModel(
            new NoopPipeClient(),
            new StubDumpService(),
            new NoopLog(),
            platform,
            aobUsage: null,
            aobMaker: null,
            proxyDeploy: null,
            experimentalGate: null,
            snapshotStore: null,
            globalHotkeys: null,
            bookmarks: null,
            coordLibrary: new CoordinateLibraryStore(platform),
            logCompression: null);
    }

    [Fact]
    public void Teleport_ReceivesTheCoordinateLibraryStore_AppBuilt()
    {
        var vm = BuildVmAsAppDoes();

        Assert.True(vm.Teleport.HasCoordStore,
            "TeleportViewModel was built without a CoordinateLibraryStore. Every Coordinate " +
            "Library save/load/delete/backup path guards on null and returns silently, so the " +
            "feature will appear to work in-session and lose everything on restart. Check that " +
            "App.axaml.cs still passes _coordLibraryStore to MainWindowViewModel — this is " +
            "audit #4 B27.");
    }

    [Fact]
    public void OmittingTheCoordStore_ReproducesB27()
    {
        // The negative control. Without it, the test above could pass for the wrong
        // reason (e.g. if HasCoordStore were hardcoded true) and would silently stop
        // guarding anything.
        var vm = new MainWindowViewModel(
            new NoopPipeClient(), new StubDumpService(), new NoopLog(), new MockPlatformService(_dir));

        Assert.False(vm.Teleport.HasCoordStore);
    }

    /// <summary>
    /// The structural guard. <see cref="MainWindowViewModel"/>'s service parameters are
    /// optional (that is the hazard), so the composition helper's must not be — otherwise
    /// a future service can be dropped at the call site and compile, exactly as B27 did.
    /// </summary>
    [Fact]
    public void AppComposition_TakesEveryServiceAsRequired()
    {
        var build = typeof(AppComposition).GetMethod(
            "BuildMainWindowViewModel", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(build);

        var vmCtor = typeof(MainWindowViewModel).GetConstructors(
            BindingFlags.Public | BindingFlags.Instance).Single();

        // Same arity: the helper forwards the whole list, it does not swallow one.
        Assert.Equal(vmCtor.GetParameters().Length, build!.GetParameters().Length);

        // The VM's are optional past the 4 infrastructure services — which is precisely
        // why an omission at the call site compiles silently...
        Assert.All(vmCtor.GetParameters().Skip(4), p => Assert.True(p.IsOptional));

        // ...and the helper's are not, which is what makes the omission a build error.
        Assert.All(build.GetParameters(), p => Assert.False(p.IsOptional,
            $"AppComposition.BuildMainWindowViewModel parameter '{p.Name}' has a default. " +
            "Every parameter here must be required — a defaulted one lets App drop the " +
            "argument and silently disable a feature (audit #4 B27)."));
    }

    // ------------------------------------------------------------------
    // The dispatcher fault guard's attachment. Same class of defect as B27 above:
    // a service that is constructed and then not connected to anything.
    // ------------------------------------------------------------------

    /// <summary>
    /// A guard that is never attached is indistinguishable from one that works —
    /// no exception, no log line, nothing to notice — and the app quietly goes back
    /// to dying on a bad paste ([PASTECRASH-2026-08-18]). Deleting the
    /// <c>Attach</c> call used to fail no test at all.
    ///
    /// <para>Construction and subscription are now one call, and this exercises that
    /// call — so the only way to lose the subscription is to delete the whole thing
    /// from <c>App</c>, which is a visible edit rather than a silent one.</para>
    /// </summary>
    [Fact]
    public void AttachDispatcherFaultGuard_ActuallySubscribes()
    {
        var dispatcher = Avalonia.Threading.Dispatcher.UIThread;

        int before = FaultGuardSubscriberCount(dispatcher);
        var guard = AppComposition.AttachDispatcherFaultGuard(new NoopLog(), dispatcher);
        try
        {
            Assert.Equal(before + 1, FaultGuardSubscriberCount(dispatcher));
        }
        finally
        {
            // Dispatcher.UIThread is process-wide; leaving this attached would leak
            // into every later test in this assembly.
            guard.Detach(dispatcher);
        }

        // Detach is half of the same contract: the guard must be removable before the
        // logger is disposed at shutdown, or it can be invoked with nowhere to report.
        Assert.Equal(before, FaultGuardSubscriberCount(dispatcher));
    }

    /// <summary>
    /// How many <see cref="DispatcherFaultGuard"/> handlers are on the dispatcher's
    /// <c>UnhandledException</c> event. Counted by delegate TARGET rather than by
    /// total length so an unrelated subscriber cannot make this pass or fail.
    /// </summary>
    private static int FaultGuardSubscriberCount(Avalonia.Threading.Dispatcher dispatcher)
    {
        var field = typeof(Avalonia.Threading.Dispatcher).GetField(
            "UnhandledException",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.True(field is not null,
            "Avalonia's Dispatcher no longer exposes an 'UnhandledException' backing field, so " +
            "this test can no longer see whether the fault guard is attached. Re-derive it before " +
            "assuming the wiring is fine.");

        var handler = field!.GetValue(dispatcher) as Delegate;
        return handler is null
            ? 0
            : handler.GetInvocationList().Count(d => d.Target is DispatcherFaultGuard);
    }

    // ------------------------------------------------------------------
    // Local no-ops. The equivalents in MainWindowInjectHelperTests are private
    // nested types, so they cannot be shared without widening them.
    // ------------------------------------------------------------------

    private sealed class NoopPipeClient : IPipeClient
    {
        public bool IsConnected => false;
        public event Action<bool>? ConnectionStateChanged { add { } remove { } }
        public event Action<System.Text.Json.Nodes.JsonObject>? EventReceived { add { } remove { } }
        public event Action<UE5DumpUI.Models.PipeLogEntry>? Activity { add { } remove { } }
        public event Action<bool>? GameThreadStalledChanged { add { } remove { } }
        public Task ConnectAsync(System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<System.Text.Json.Nodes.JsonObject> SendAsync(
            System.Text.Json.Nodes.JsonObject request,
            System.Threading.CancellationToken ct = default)
            => Task.FromResult(new System.Text.Json.Nodes.JsonObject());
        public void Dispose() { }
    }

    private sealed class NoopLog : ILoggingService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Error(string message, Exception ex) { }
        public void Debug(string message) { }
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message) { }
        public void Error(string category, string message, Exception ex) { }
        public void Debug(string category, string message) { }
        public void StartProcessMirror(string processName) { }
        public void StopProcessMirror() { }
    }
}
