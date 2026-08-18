using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using UE5DumpUI.Core;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The guard itself: it must swallow exactly what the classifier accepts, count
/// it, and LOG it either way. A swallowed fault that leaves no trace would turn
/// "paste silently did nothing" into an unreportable ghost.
/// </summary>
public class DispatcherFaultGuardTests
{
    private const string ClipboardTrace =
        "   at Avalonia.Win32.ClipboardImpl.TryGetDataAsync()\n" +
        "   at Avalonia.Controls.TextBox.Paste()";

    private static COMException ClipboardFault()
    {
        var ex = new COMException("EnumFormatEtc failed", unchecked((int)0x8007000E));
        ExceptionDispatchInfo.SetRemoteStackTrace(ex, ClipboardTrace);
        return ex;
    }

    [Fact]
    public void ClipboardFault_IsHandledCountedAndLogged()
    {
        var log = new RecordingLog();
        var guard = new DispatcherFaultGuard(log);

        Assert.True(guard.ShouldHandle(ClipboardFault()));
        Assert.Equal(1, guard.SwallowedCount);

        var warn = Assert.Single(log.Warnings);
        Assert.Contains("Input-layer fault swallowed", warn, StringComparison.Ordinal);
        Assert.Contains("EnumFormatEtc failed", warn, StringComparison.Ordinal);
        Assert.Empty(log.Errors);
    }

    [Fact]
    public void BothOutcomes_GoToTheSameLogCategory()
    {
        // They used to split across "view" (swallowed) and "init" (refused), so the
        // guard's story needed two files to read and the documented verification step
        // — grep view-0.log for the guard's lines — could only ever see half of it.
        var log = new RecordingLog();
        var guard = new DispatcherFaultGuard(log);

        guard.ShouldHandle(ClipboardFault());

        var ours = new NullReferenceException("vm blew up");
        ExceptionDispatchInfo.SetRemoteStackTrace(ours,
            "   at UE5DumpUI.ViewModels.LiveWalkerViewModel.Refresh()\n" +
            "   at Avalonia.Controls.TextBox.Paste()");
        guard.ShouldHandle(ours);

        Assert.Equal(2, log.Categories.Count);
        Assert.All(log.Categories, c => Assert.Equal(UE5DumpUI.Constants.LogCatView, c));
    }

    [Fact]
    public void OurOwnFault_IsNotHandled_AndIsStillLogged()
    {
        var log = new RecordingLog();
        var guard = new DispatcherFaultGuard(log);

        var ours = new NullReferenceException("vm blew up");
        ExceptionDispatchInfo.SetRemoteStackTrace(ours,
            "   at UE5DumpUI.ViewModels.LiveWalkerViewModel.Refresh()\n" +
            "   at Avalonia.Controls.TextBox.Paste()");

        Assert.False(guard.ShouldHandle(ours));
        Assert.Equal(0, guard.SwallowedCount);
        Assert.Empty(log.Warnings);

        var err = Assert.Single(log.Errors);
        Assert.Contains("NOT swallowed", err, StringComparison.Ordinal);
        Assert.Contains("OurCodeOnStack", err, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_IsNotHandled()
    {
        var guard = new DispatcherFaultGuard(new RecordingLog());
        Assert.False(guard.ShouldHandle(null));
    }

    [Fact]
    public void ALoggerThatThrows_DoesNotTurnOneCrashIntoTwo()
    {
        // Avalonia's own guidance: a handler for this event must not raise a
        // secondary exception. Nothing may escape ShouldHandle, whatever the logger
        // does.
        var guard = new DispatcherFaultGuard(new ThrowingLog());

        var ex = Record.Exception(() => guard.ShouldHandle(ClipboardFault()));

        Assert.Null(ex);
    }

    [Fact]
    public void TheVerdictDoesNotDependOnTheLoggerBeingAlive()
    {
        // The guard OUTLIVES the logger: ShutdownRequested disposes the logging
        // service, and a dispatcher fault can arrive after that. When classification
        // and logging shared one try/catch, a dead logger silently flipped every
        // swallow into a crash — whether a clipboard fault killed the app depended on
        // how far through teardown the process happened to be. Same input, same
        // answer, live logger or dead one.
        var live = new DispatcherFaultGuard(new RecordingLog());
        var dead = new DispatcherFaultGuard(new ThrowingLog());

        Assert.Equal(live.ShouldHandle(ClipboardFault()), dead.ShouldHandle(ClipboardFault()));
        Assert.True(dead.ShouldHandle(ClipboardFault()));

        // ...and the refusing direction stays identical too, so this is not just
        // "everything is swallowed now".
        var ours = new NullReferenceException("vm blew up");
        ExceptionDispatchInfo.SetRemoteStackTrace(ours,
            "   at UE5DumpUI.ViewModels.LiveWalkerViewModel.Refresh()");
        Assert.False(dead.ShouldHandle(ours));
    }

    [Fact]
    public void TheCounterNeverRunsAheadOfTheSwallowsThatHappened()
    {
        // It used to increment BEFORE logging, inside the try — so a throwing logger
        // left the count claiming a swallow that the catch then turned into a crash.
        var dead = new DispatcherFaultGuard(new ThrowingLog());
        bool swallowed = dead.ShouldHandle(ClipboardFault());

        Assert.Equal(swallowed ? 1 : 0, dead.SwallowedCount);

        var refusing = new DispatcherFaultGuard(new ThrowingLog());
        refusing.ShouldHandle(new NullReferenceException("no stack, no evidence"));
        Assert.Equal(0, refusing.SwallowedCount);
    }

    [Fact]
    public void RepeatedFaults_KeepCounting()
    {
        var guard = new DispatcherFaultGuard(new RecordingLog());

        Assert.True(guard.ShouldHandle(ClipboardFault()));
        Assert.True(guard.ShouldHandle(ClipboardFault()));
        Assert.Equal(2, guard.SwallowedCount);
    }

    // ------------------------------------------------------- set-only-true rule

    [Fact]
    public void AnAlreadyHandledFault_IsNeverUnHandled()
    {
        // The regression this pins: `e.Handled = ShouldHandle(...)` writes the result
        // unconditionally, so a fault another subscriber had already claimed would be
        // silently un-claimed by us. The event is multicast and its invocation order
        // is nobody's contract, so this guard may only ever ADD a reason to swallow.
        var guard = new DispatcherFaultGuard(new RecordingLog());

        var ours = new NullReferenceException("vm blew up");
        ExceptionDispatchInfo.SetRemoteStackTrace(ours,
            "   at UE5DumpUI.ViewModels.LiveWalkerViewModel.Refresh()");

        // On its own merits this fault is refused...
        Assert.False(guard.NextHandledFlag(alreadyHandled: false, ours));
        // ...but somebody else's true survives.
        Assert.True(guard.NextHandledFlag(alreadyHandled: true, ours));
    }

    [Fact]
    public void AnAlreadyHandledFault_IsNotCountedAsOurSwallow()
    {
        // SwallowedCount is the guard's own tally, and it feeds the log line the
        // verification step greps for. Counting somebody else's decision would make
        // that number a lie.
        var guard = new DispatcherFaultGuard(new RecordingLog());

        Assert.True(guard.NextHandledFlag(alreadyHandled: true, ClipboardFault()));

        Assert.Equal(0, guard.SwallowedCount);
    }

    [Fact]
    public void AnUnhandledClipboardFault_IsStillClaimed()
    {
        // Negative control for the two above: the short-circuit must not have turned
        // the guard off.
        var guard = new DispatcherFaultGuard(new RecordingLog());

        Assert.True(guard.NextHandledFlag(alreadyHandled: false, ClipboardFault()));
        Assert.Equal(1, guard.SwallowedCount);
    }

    private sealed class RecordingLog : ILoggingService
    {
        public List<string> Warnings { get; } = [];
        public List<string> Errors { get; } = [];

        /// <summary>Every category the guard routed a line to, in order.</summary>
        public List<string> Categories { get; } = [];

        public void Info(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message) => Errors.Add(message);
        public void Error(string message, Exception ex) => Errors.Add(message);
        public void Debug(string message) { }
        public void Info(string category, string message) => Categories.Add(category);
        public void Warn(string category, string message)
        {
            Categories.Add(category);
            Warnings.Add(message);
        }
        public void Error(string category, string message)
        {
            Categories.Add(category);
            Errors.Add(message);
        }
        public void Error(string category, string message, Exception ex)
        {
            Categories.Add(category);
            Errors.Add(message);
        }
        public void Debug(string category, string message) => Categories.Add(category);
        public void StartProcessMirror(string processName) { }
        public void StopProcessMirror() { }
    }

    private sealed class ThrowingLog : ILoggingService
    {
        public void Info(string message) => throw new InvalidOperationException("log is gone");
        public void Warn(string message) => throw new InvalidOperationException("log is gone");
        public void Error(string message) => throw new InvalidOperationException("log is gone");
        public void Error(string message, Exception ex) => throw new InvalidOperationException("log is gone");
        public void Debug(string message) => throw new InvalidOperationException("log is gone");
        public void Info(string category, string message) => throw new InvalidOperationException("log is gone");
        public void Warn(string category, string message) => throw new InvalidOperationException("log is gone");
        public void Error(string category, string message) => throw new InvalidOperationException("log is gone");
        public void Error(string category, string message, Exception ex) => throw new InvalidOperationException("log is gone");
        public void Debug(string category, string message) => throw new InvalidOperationException("log is gone");
        public void StartProcessMirror(string processName) => throw new InvalidOperationException("log is gone");
        public void StopProcessMirror() => throw new InvalidOperationException("log is gone");
    }
}
