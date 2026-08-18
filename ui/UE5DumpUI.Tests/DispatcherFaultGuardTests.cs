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
        // secondary exception. Failing closed (do not swallow) is the safe answer.
        var guard = new DispatcherFaultGuard(new ThrowingLog());

        Assert.False(guard.ShouldHandle(ClipboardFault()));
    }

    [Fact]
    public void RepeatedFaults_KeepCounting()
    {
        var guard = new DispatcherFaultGuard(new RecordingLog());

        Assert.True(guard.ShouldHandle(ClipboardFault()));
        Assert.True(guard.ShouldHandle(ClipboardFault()));
        Assert.Equal(2, guard.SwallowedCount);
    }

    private sealed class RecordingLog : ILoggingService
    {
        public List<string> Warnings { get; } = [];
        public List<string> Errors { get; } = [];

        public void Info(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message) => Errors.Add(message);
        public void Error(string message, Exception ex) => Errors.Add(message);
        public void Debug(string message) { }
        public void Info(string category, string message) { }
        public void Warn(string category, string message) => Warnings.Add(message);
        public void Error(string category, string message) => Errors.Add(message);
        public void Error(string category, string message, Exception ex) => Errors.Add(message);
        public void Debug(string category, string message) { }
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
