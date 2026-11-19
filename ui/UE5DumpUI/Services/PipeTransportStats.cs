using System.Threading;

namespace UE5DumpUI.Services;

/// <summary>
/// Process-wide accumulator for time spent inside <see cref="PipeClient.SendAsync"/> —
/// the ONE extra measurement needed to decompose a heavy operation's cost.
///
/// <para><b>Why.</b> A heavy export's per-call cost outside the DLL is part pipe
/// latency and part UI-side work, and batching <c>walk_instance</c> recovers only
/// the first. Pipe latency disappears when 200 calls become one; UI-side per-result
/// work does not. Promising a speed-up without that split would be guessing.</para>
///
/// <para><b>The split this enables.</b> Transport is measured here; the DLL's own
/// dispatch time already comes from <c>Sense</c>; the operation's wall-clock is
/// measured by <see cref="DiagnosticsProbe"/>. Three numbers give all four parts:</para>
/// <list type="bullet">
///   <item><c>dll</c> = Sense dispatcher busy — the actual work.</item>
///   <item><c>ipc</c> = transport − dll — pure pipe/IPC latency. <b>This is what
///     batching removes.</b></item>
///   <item><c>ui</c> = wall − transport — deserialise + whatever the caller does
///     with each result. <b>Batching does NOT remove this.</b></item>
/// </list>
///
/// <para><b>Caveat the numbers carry.</b> Transport is summed per call, so with
/// two lanes sending concurrently the sum can exceed wall-clock. The heavy exports
/// this exists to measure are sequential, but a reader must not treat the sum as
/// exclusive time.</para>
///
/// Cost: one <see cref="Stopwatch.GetTimestamp"/> pair and two interlocked adds per
/// pipe call — negligible against a round-trip of hundreds of microseconds.
/// </summary>
internal static class PipeTransportStats
{
    private static long _ticks;   // Stopwatch ticks, summed across all calls
    private static long _calls;

    /// <summary>Record one completed <c>SendAsync</c> (write + wait + response).</summary>
    public static void Record(long elapsedStopwatchTicks)
    {
        Interlocked.Add(ref _ticks, elapsedStopwatchTicks);
        Interlocked.Increment(ref _calls);
    }

    /// <summary>Monotonic snapshot: (total transport ms, call count). Callers
    /// difference two snapshots rather than resetting, so concurrent operations
    /// cannot clobber each other's baseline.</summary>
    public static (double Ms, long Calls) Snapshot()
    {
        long ticks = Interlocked.Read(ref _ticks);
        long calls = Interlocked.Read(ref _calls);
        return (ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency, calls);
    }
}
