using System.Runtime.InteropServices;
using UE5DumpUI.Core;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The WRITE half of <c>[PASTECRASH-2026-08-18]</c>.
///
/// <para>The original fix guarded the clipboard READ (Ctrl+V, which surfaces as a
/// dispatcher fault with no frame of ours on it). The write was left unguarded, and
/// it fails in the OPPOSITE way: <c>CopyToClipboardAsync</c> is awaited from ~60
/// <c>[RelayCommand]</c> Copy buttons, CommunityToolkit's <c>AsyncRelayCommand</c>
/// rethrows a faulted command onto the dispatcher via
/// <c>AwaitAndThrowIfFailed</c>, and that exception DOES carry our frames — so the
/// classifier correctly returns <c>OurCodeOnStack</c>, refuses to swallow, and the
/// app dies. The read guard did not merely miss this case; it is structurally
/// obliged to refuse it. The only place it can be stopped is at the write.</para>
///
/// <para>There is no clipboard in a headless test run, so the guarded write takes
/// the clipboard call as a delegate and these tests supply ones that fail on
/// purpose. Asserting over the source text instead would pass just as happily
/// against a <c>try</c> block that rethrew.</para>
/// </summary>
public class ClipboardWriteGuardTests
{
    private static COMException ClipboardBusy() =>
        new("OpenClipboard failed", unchecked((int)0x800401D0));   // CLIPBRD_E_CANT_OPEN

    [Fact]
    public async Task ASuccessfulWrite_ReportsTrueAndSaysNothing()
    {
        var log = new RecordingLog();

        bool ok = await WindowsPlatformService.CopyGuardedAsync(() => Task.CompletedTask, log);

        Assert.True(ok);
        Assert.Empty(log.Warnings);
    }

    [Fact]
    public async Task AFailedWrite_DegradesToFalseInsteadOfThrowing()
    {
        // THE test. Before the guard this exception escaped into the command's
        // continuation and took the process with it.
        var log = new RecordingLog();

        bool ok = await WindowsPlatformService.CopyGuardedAsync(
            () => Task.FromException(ClipboardBusy()), log);

        Assert.False(ok);
        var warn = Assert.Single(log.Warnings);
        Assert.Contains("Clipboard copy FAILED", warn, StringComparison.Ordinal);
        Assert.Contains("OpenClipboard failed", warn, StringComparison.Ordinal);
        Assert.Equal(UE5DumpUI.Constants.LogCatView, Assert.Single(log.Categories));
    }

    [Fact]
    public async Task AWriteThatThrowsSynchronously_IsGuardedToo()
    {
        // A delegate can fault before it ever returns a Task — an argument check, or
        // a disposed clipboard. `await` never sees a Task at all in that case, so a
        // guard placed only around the await would miss it.
        var log = new RecordingLog();

        bool ok = await WindowsPlatformService.CopyGuardedAsync(
            () => throw ClipboardBusy(), log);

        Assert.False(ok);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public async Task NoClipboardAtAll_IsFalseAndSaidOutLoud()
    {
        var log = new RecordingLog();

        bool ok = await WindowsPlatformService.CopyGuardedAsync(null, log);

        Assert.False(ok);
        Assert.Contains("no main window / no clipboard",
            Assert.Single(log.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedWriteBeforeTheLoggerIsWired_StillDoesNotThrow()
    {
        // The platform service is constructed BEFORE the logging service (the log
        // directory comes from it), so Logger is null for the first moments of
        // startup. Silent is acceptable there; throwing is not.
        bool ok = await WindowsPlatformService.CopyGuardedAsync(
            () => Task.FromException(ClipboardBusy()), log: null);

        Assert.False(ok);
    }

    [Theory]
    // The never-swallow set stays loud at BOTH ends of the clipboard. One list —
    // InputLayerFaultClassifier.IsNeverSwallowable — so the read guard and the write
    // guard cannot drift into disagreeing about what a crash is.
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(AccessViolationException))]
    [InlineData(typeof(TypeLoadException))]
    [InlineData(typeof(BadImageFormatException))]
    [InlineData(typeof(InvalidProgramException))]
    public async Task ProcessLevelFailures_AreNotDegradedIntoASilentNoOp(Type exceptionType)
    {
        var log = new RecordingLog();
        var fatal = (Exception)Activator.CreateInstance(exceptionType)!;

        await Assert.ThrowsAsync(exceptionType, () =>
            WindowsPlatformService.CopyGuardedAsync(() => Task.FromException(fatal), log));

        Assert.Empty(log.Warnings);
    }

    [Fact]
    public async Task AnOrdinaryFailureIsStillDegraded()
    {
        // Negative control for the theory above: the never-swallow filter must not
        // have grown into "rethrow everything", which would restore the crash.
        bool ok = await WindowsPlatformService.CopyGuardedAsync(
            () => Task.FromException(new InvalidOperationException("clipboard is busy")), null);

        Assert.False(ok);
    }

    [Fact]
    public void TheInterfaceStillReportsWhetherTheCopyHappened()
    {
        // Structural. A refactor back to a bare Task would compile at all ~60 call
        // sites — `await x;` is legal either way — and silently discard the one
        // signal that lets a caller say "the copy did nothing".
        var method = typeof(IPlatformService).GetMethod(nameof(IPlatformService.CopyToClipboardAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<bool>), method!.ReturnType);
    }

    private sealed class RecordingLog : ILoggingService
    {
        public List<string> Warnings { get; } = [];
        public List<string> Categories { get; } = [];

        public void Info(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message) { }
        public void Error(string message, Exception ex) { }
        public void Debug(string message) { }
        public void Info(string category, string message) { }
        public void Warn(string category, string message)
        {
            Categories.Add(category);
            Warnings.Add(message);
        }
        public void Error(string category, string message) { }
        public void Error(string category, string message, Exception ex) { }
        public void Debug(string category, string message) { }
        public void StartProcessMirror(string processName) { }
        public void StopProcessMirror() { }
    }
}
