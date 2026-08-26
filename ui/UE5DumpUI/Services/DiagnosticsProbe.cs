using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Brackets a heavy operation with two <c>get_diagnostics</c> snapshots and logs
/// the DELTA — what that one operation actually cost the DLL's dispatcher.
///
/// <para><b>Why automatic instead of a measurement session.</b> The numbers exist
/// to settle <c>docs/multipipe-eval.md</c>'s question about serial-dispatch
/// head-of-line blocking. A deliberate test run only ever covers the scenario
/// somebody thought to try, and only if they remembered to reset the counters
/// first. Recording every real Copy CE XML / Copy CE Field / Value Scan /
/// Snapshot capture instead means the evidence accumulates from actual use,
/// including the combinations nobody would think to test.</para>
///
/// <para><b>Deltas, not absolutes.</b> Absolute totals answer "what has this
/// session done"; the question here is "what did THIS operation cost", so every
/// figure is differenced against the opening snapshot. Wall-clock is measured
/// locally rather than from the DLL's uptime, so a <c>reset_diagnostics</c>
/// landing mid-operation cannot produce a negative duration.</para>
///
/// <para><b>Never affects the operation.</b> Every failure path is swallowed:
/// no connection, an older DLL that doesn't know the command, a mid-operation
/// disconnect. A diagnostic that breaks the thing it measures is worse than no
/// diagnostic. Cost is two pipe round-trips (~0-125 ms each) around operations
/// that run for seconds.</para>
///
/// Usage — the <c>await using</c> guarantees the closing sample even if the
/// operation throws:
/// <code>
/// await using var probe = await DiagnosticsProbe.BeginAsync(_dump, _log, "Value Scan (First)");
/// </code>
/// </summary>
internal sealed class DiagnosticsProbe : IAsyncDisposable
{
    /// <summary>The probe's own calls land in the table it reports. Excluding them
    /// keeps "what did this operation cost" honest — otherwise every measurement
    /// lists the measurement.</summary>
    private const string SelfCmd = "get_diagnostics";

    private readonly IDumpService? _dump;
    private readonly ILoggingService? _log;
    private readonly string _label;
    private readonly DiagnosticsResult? _before;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    /// <summary>Transport baseline, differenced at close. Monotonic snapshots
    /// rather than a reset, so two overlapping probes can't clobber each other.</summary>
    private readonly (double Ms, long Calls) _txBefore = PipeTransportStats.Snapshot();
    private bool _done;

    private DiagnosticsProbe(IDumpService? dump, ILoggingService? log, string label,
                             DiagnosticsResult? before)
    {
        _dump = dump;
        _log = log;
        _label = label;
        _before = before;
    }

    /// <summary>Open a probe. Returns a no-op probe (never null) when diagnostics
    /// are unavailable, so call sites need no null handling.</summary>
    public static async Task<DiagnosticsProbe> BeginAsync(
        IDumpService? dump, ILoggingService? log, string label,
        CancellationToken ct = default)
    {
        DiagnosticsResult? before = null;
        if (dump != null)
        {
            try { before = await dump.GetDiagnosticsAsync(limit: 0, ct); }
            catch { /* no connection / older DLL / cancelled — probe degrades to no-op */ }
        }
        return new DiagnosticsProbe(dump, log, label, before);
    }

    public async ValueTask DisposeAsync()
    {
        if (_done) return;
        _done = true;
        _sw.Stop();
        if (_dump == null || _before == null || _log == null) return;

        // Close the transport window HERE, before the closing get_diagnostics — the same
        // boundary _sw.Stop() above uses. The old code snapshotted AFTER that call while
        // the stopwatch had already stopped before it, so the probe's own round-trip
        // (93-125 ms in this repo's own measurements) landed in transportMs but not in
        // wallMs. On a 57.7 ms "Copy CE Field" that made transportMs > wallMs, uiMs clamp
        // to 0, and ipcMs absorb the probe. These lines are the evidence
        // docs/multipipe-eval.md reasons from, so the error was not cosmetic. (B35)
        var txAfter = PipeTransportStats.Snapshot();

        DiagnosticsResult after;
        try { after = await _dump.GetDiagnosticsAsync(limit: 0); }
        catch { return; }   // disconnected mid-operation: nothing to report

        // Nothing to subtract any more. _txBefore is a FIELD INITIALIZER, so it runs at
        // construction — i.e. AFTER BeginAsync's opening get_diagnostics — and txAfter is
        // now taken before the closing one. Neither probe call is inside the window, so
        // the old `- 2` was wrong twice over: it discounted calls that were never counted
        // and left in the milliseconds of one that was.
        double txMs = Math.Max(0, txAfter.Ms - _txBefore.Ms);
        long txCalls = Math.Max(0, txAfter.Calls - _txBefore.Calls);

        try { _log.Info(Constants.LogCatView,
                        Format(_label, _sw.Elapsed, _before, after, txMs, txCalls)); }
        catch { /* logging must never surface into the operation */ }
    }

    /// <summary>Build the log line. Pure + internal so the shape is unit-testable
    /// without a pipe.</summary>
    internal static string Format(string label, TimeSpan elapsed,
                                  DiagnosticsResult before, DiagnosticsResult after,
                                  double transportMs = -1, long transportCalls = 0)
    {
        var top = TopDeltas(before, after, 0);

        // Busy is summed from the per-command deltas, NOT from the global
        // total_busy_ms delta. The two disagreed on the first live run: the global
        // figure includes the probe's OWN opening get_diagnostics, so a 57.7 ms
        // Copy CE Field reported "busy 93 ms (161.2%)" — the probe measuring
        // itself. Deriving busy from the same rows the ranking shows makes the
        // percentage and the breakdown agree by construction.
        double busyMs = top.Sum(t => t.TotalMs);
        // ...and so is the COUNT, for the same reason and from the same rows. It used
        // to be `after.TotalDispatches - before.TotalDispatches`, the global counter,
        // which includes the probe's own get_diagnostics that `top` (and therefore
        // busyMs) deliberately skips. The line then printed three numbers over two
        // denominators: "11 dispatches" beside "busy 15.2 ms" beside "per call: dll
        // 1.524" — 15.2/11 = 1.38, not 1.524, because the per-call divisor is the
        // transport count, which excludes the probe too. Measured on a real P3R
        // session (2026-08-26) and on DumperTest: the divisor was always
        // dispatches-1, on two games and two builds. The busy fix directly above was
        // made and this one, two lines below it, was not. (audit #4's root cause: the
        // report and the reality computed by different code paths.)
        long dispatches = top.Sum(t => t.Count);
        double wallMs   = elapsed.TotalMilliseconds;

        // Share of the operation's own wall-clock spent inside the DLL dispatcher.
        // THE number: high means the DLL is the bottleneck and non-blocking dispatch
        // would help; low means the cost is elsewhere (UI thread, serialisation,
        // the game thread) and multipipe Phase 1 would not.
        double busyPct = wallMs > 0 ? busyMs * 100.0 / wallMs : 0.0;

        var sb = new StringBuilder();
        sb.Append("PERF ").Append(label)
          .Append(": wall ").Append(F(wallMs)).Append(" ms")
          .Append(" · dispatcher busy ").Append(F(busyMs)).Append(" ms (")
          .Append(F(busyPct)).Append("%)")
          .Append(" · ").Append(dispatches).Append(" dispatches");

        long objDelta = after.GObjectsCount - before.GObjectsCount;
        if (objDelta != 0)
            sb.Append(" · GObjects ").Append(objDelta > 0 ? "+" : "").Append(objDelta);

        long wsDelta = after.Process.WorkingSetBytes - before.Process.WorkingSetBytes;
        if (Math.Abs(wsDelta) > 1024 * 1024)
            sb.Append(" · game WS ").Append(wsDelta > 0 ? "+" : "")
              .Append(F(wsDelta / (1024.0 * 1024.0))).Append(" MiB");

        // The dll / ipc / ui split — the whole reason transport is measured.
        // Only emitted when transport was actually sampled (>= 0).
        if (transportMs >= 0 && wallMs > 0)
        {
            // transport INCLUDES the DLL's own dispatch time (SendAsync waits for
            // the response), so pure IPC is what's left after removing it.
            double ipcMs = Math.Max(0, transportMs - busyMs);
            double uiMs  = Math.Max(0, wallMs - transportMs);
            sb.Append(" · split dll ").Append(F(busyMs))
              .Append(" / ipc ").Append(F(ipcMs))
              .Append(" / ui ").Append(F(uiMs)).Append(" ms");
            if (transportCalls > 0)
            {
                // Per-call is the figure that decides whether batching is worth it:
                // ipc/call vanishes when N calls become one, ui/call does not.
                sb.Append(" (per call: dll ").Append(F3(busyMs / transportCalls))
                  .Append(" / ipc ").Append(F3(ipcMs / transportCalls))
                  .Append(" / ui ").Append(F3(uiMs / transportCalls)).Append(" ms)");
            }
        }

        if (top.Count > 0)
        {
            // "peak", and the qualifier is not decoration: MaxMs is a running
            // high-water mark that cannot be differenced (see TopDeltas), so it is the
            // worst call SINCE THE LAST RESET, not the worst call in this operation.
            // TopDeltas' own comment said "let the label carry the caveat" and the
            // label said "max". Two exports minutes apart printed a byte-identical
            // "max 3.5ms" while their totals moved, which is what that reads like.
            sb.Append(" · top (peak = session high-water, not this op): ");
            sb.Append(string.Join(", ", top.Take(3).Select(t =>
                $"{t.Cmd} {F(t.TotalMs)}ms/{t.Count}x peak {F(t.MaxMs)}ms")));
        }
        return sb.ToString();
    }

    /// <summary>Per-command deltas, heaviest total first. Commands absent from the
    /// opening snapshot count in full (they ran for the first time during the
    /// operation).</summary>
    internal static List<DiagnosticsCommandEntry> TopDeltas(
        DiagnosticsResult before, DiagnosticsResult after, int limit)
    {
        var baseline = before.Commands.ToDictionary(c => c.Cmd, StringComparer.Ordinal);
        var deltas = new List<DiagnosticsCommandEntry>();
        foreach (var a in after.Commands)
        {
            if (string.Equals(a.Cmd, SelfCmd, StringComparison.Ordinal)) continue;
            baseline.TryGetValue(a.Cmd, out var b);
            double total = a.TotalMs - (b?.TotalMs ?? 0);
            long count = a.Count - (b?.Count ?? 0);
            if (count <= 0 && total <= 0) continue;   // untouched by this operation
            deltas.Add(new DiagnosticsCommandEntry
            {
                Cmd     = a.Cmd,
                Count   = Math.Max(0, count),
                TotalMs = Math.Max(0, total),
                // Max is a running high-water mark, so it cannot be differenced —
                // report the observed peak and let the label carry the caveat.
                MaxMs   = a.MaxMs,
                AvgMs   = count > 0 ? total / count : 0.0,
            });
        }
        deltas.Sort((x, y) => y.TotalMs.CompareTo(x.TotalMs) != 0
            ? y.TotalMs.CompareTo(x.TotalMs)
            : string.CompareOrdinal(x.Cmd, y.Cmd));
        if (limit > 0 && deltas.Count > limit) deltas.RemoveRange(limit, deltas.Count - limit);
        return deltas;
    }

    private static string F(double v) => v.ToString("N1", CultureInfo.InvariantCulture);
    private static string F3(double v) => v.ToString("N3", CultureInfo.InvariantCulture);
}
