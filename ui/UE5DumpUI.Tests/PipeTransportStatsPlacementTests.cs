using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// audit #5 AC13 — `PipeTransportStats` had NO test of any kind. This adds the half that
/// can be tested deterministically, and records why the other half still cannot.
///
/// <para><b>The defect AC13 fixed.</b> The `finally` that records a request's transport
/// time used to sit around `await tcs.Task` ALONE, so the write-lock wait,
/// `WriteLineAsync`, and every throw out of the IOException classifier bypassed it: a
/// request that DIED IN THE WRITE contributed exactly 0 ms, and the IPC average flattered
/// itself precisely when the pipe was misbehaving. The fix moved the body, not the
/// comment.</para>
///
/// <para><b>What this test pins.</b> The other half of the same intent, which is a real
/// invariant and is deterministic: the not-connected guard sits ABOVE the timer on
/// purpose — nothing was sent, so there is no transport time to attribute, and a 0 ms
/// sample would deflate the average just as the missing write-failures inflated it. If
/// the timer ever drifts above the guard, every refusal starts logging a 0 ms call and
/// this test fails.</para>
///
/// <para><b>⚠ What is NOT covered, and the attempt is recorded so it is not re-spent.</b>
/// Testing the positive half — a request that fails BEFORE the response is still counted
/// — needs `SendAsync` to get past `IsConnected` and a live `_writer`, i.e. a real
/// connected pipe. That route was built and abandoned on 2026-08-23:</para>
/// <list type="bullet">
///   <item>`Constants.PipeName` is a hardcoded `const`, so a test server would bind the
///     name a running game's DLL also serves — named pipes allow several server instances
///     per name, so the test's client can reach the DLL or the UI's client can reach the
///     test. That is the hazard behind CLAUDE.md's "never run pipe_client.py while the UI
///     is connected". An injectable name was prototyped and reverted with the test.</item>
///   <item>With an injectable name, `PipeClient.ConnectAsync` **reproducibly never
///     completes** against an in-process `NamedPipeServerStream`, while a raw
///     `NamedPipeClientStream` built with identical arguments connects in 0.15 s. Measured
///     across four variations — `maxNumberOfServerInstances` 1 and 4, on and off the xUnit
///     synchronization context — every one timed out at the harness bound with the
///     server's own `WaitForConnectionAsync` reporting completed. Not diagnosed; it is not
///     an AC13 defect, and it is not worth more time than it already cost.</item>
/// </list>
///
/// <para>So AC13's placement remains covered only by the guard side. The honest next step
/// is the one `[AC13-2026-08-22]` already recommends: surface the transport figure where a
/// disconnect cannot destroy the observable, which makes the live row runnable again.</para>
/// </summary>
public class PipeTransportStatsPlacementTests
{
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

    [Fact]
    public async Task ARefusalWithNothingSent_IsNotCountedAsTransport()
    {
        // Never connected, so SendAsync throws from the guard ABOVE the timer.
        using var client = new PipeClient(new NoopLog(), "T");
        var before = PipeTransportStats.Snapshot();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(new JsonObject { ["cmd"] = "ping" },
                                   TestContext.Current.CancellationToken));

        var after = PipeTransportStats.Snapshot();
        Assert.Equal(before.Calls, after.Calls);
    }

    [Fact]
    public void Snapshot_IsMonotonicAndConvertsTicksToMilliseconds()
    {
        // Callers DIFFERENCE two snapshots rather than resetting, so a later snapshot may
        // never report fewer calls or less time than an earlier one — that is what makes
        // concurrent DiagnosticsProbe windows safe. Record a known tick count and check
        // both the monotonicity and the tick -> ms conversion.
        var before = PipeTransportStats.Snapshot();
        long ticks = System.Diagnostics.Stopwatch.Frequency / 100;   // exactly 10 ms
        PipeTransportStats.Record(ticks);
        var after = PipeTransportStats.Snapshot();

        Assert.Equal(before.Calls + 1, after.Calls);
        Assert.True(after.Ms >= before.Ms, "the accumulator must never go backwards");
        Assert.InRange(after.Ms - before.Ms, 9.0, 11.0);
    }
}
