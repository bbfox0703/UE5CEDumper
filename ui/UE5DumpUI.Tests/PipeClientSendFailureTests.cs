using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// audit #5 AC10 — a failed pipe WRITE has three causes and the old filters reported all
/// of them as cancellation. The classification is pure and lives on PipeClient because the
/// live path (a pipe dying between the IsConnected guard and the write) is a race that
/// cannot be driven deterministically from outside.
/// </summary>
public class PipeClientSendFailureTests
{
    [Fact]
    public void CallerCancel_OutranksEverything()
    {
        // A user pressing Cancel while the pipe also happens to be going down is still a
        // cancel, and must carry the caller's own token.
        Assert.Equal(PipeClient.SendFailure.CallerCancelled,
            PipeClient.ClassifySendFailure(callerCancelled: true, clientCancelled: true, connected: false));
        Assert.Equal(PipeClient.SendFailure.CallerCancelled,
            PipeClient.ClassifySendFailure(callerCancelled: true, clientCancelled: false, connected: true));
    }

    [Fact]
    public void DeliberateTeardown_IsStillCancellation()
    {
        // DisconnectAsync / Dispose cancel _cts BEFORE clearing IsConnected, so this is
        // the state a planned shutdown produces. Behaviour deliberately unchanged.
        Assert.Equal(PipeClient.SendFailure.Disconnecting,
            PipeClient.ClassifySendFailure(callerCancelled: false, clientCancelled: true, connected: false));
        Assert.Equal(PipeClient.SendFailure.Disconnecting,
            PipeClient.ClassifySendFailure(callerCancelled: false, clientCancelled: true, connected: true));
    }

    /// <summary>
    /// THE FINDING. Nobody cancelled anything and the connection is gone: ReadLoopAsync's
    /// finally cleared IsConnected on an unplanned exit. This must be a failure, not a
    /// cancel — it is the same event for which that finally faults every OTHER in-flight
    /// request with an IOException.
    ///
    /// NEGATIVE CONTROL: collapse the check back to `!connected || clientCancelled` and
    /// this is the one row that flips (to Disconnecting); every other row above and below
    /// keeps its answer, which is why the defect survived so long.
    /// </summary>
    [Fact]
    public void UnexpectedDeath_IsAFailureNotACancel()
    {
        Assert.Equal(PipeClient.SendFailure.PipeDied,
            PipeClient.ClassifySendFailure(callerCancelled: false, clientCancelled: false, connected: false));
    }

    [Fact]
    public void LivePipe_KeepsTheOriginalTransportError()
    {
        // Nothing cancelled, still connected: a genuine I/O error that must propagate
        // unwrapped rather than being relabelled as a disconnect.
        Assert.Equal(PipeClient.SendFailure.TransportError,
            PipeClient.ClassifySendFailure(callerCancelled: false, clientCancelled: false, connected: true));
    }
}
