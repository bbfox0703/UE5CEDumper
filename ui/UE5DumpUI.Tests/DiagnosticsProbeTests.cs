using System;
using System.Collections.Generic;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the shape of the automatic PERF log line written around every heavy
/// operation (Copy CE XML / Copy CE Field / Value Scan / Snapshot capture).
///
/// The point of recording automatically rather than in a measurement session is
/// that the evidence accumulates from real use. That only works if the line is
/// trustworthy, so the arithmetic is pinned here: deltas not absolutes, the
/// probe's own calls excluded, and no negative figures when the DLL's counters
/// are reset mid-operation.
/// </summary>
public class DiagnosticsProbeTests
{
    private static DiagnosticsResult R(long dispatches, double busyMs,
                                       params (string Cmd, long Count, double TotalMs, double MaxMs)[] cmds)
        => new()
        {
            TotalDispatches = dispatches,
            TotalBusyMs = busyMs,
            Commands = new List<DiagnosticsCommandEntry>(Array.ConvertAll(cmds,
                c => new DiagnosticsCommandEntry
                {
                    Cmd = c.Cmd, Count = c.Count, TotalMs = c.TotalMs, MaxMs = c.MaxMs,
                })),
        };

    // ── Deltas, not absolutes ────────────────────────────────────────

    [Fact]
    public void Deltas_subtract_the_opening_snapshot()
    {
        var before = R(100, 1000, ("value_scan_begin", 2, 800, 500));
        var after  = R(105, 5000, ("value_scan_begin", 3, 4800, 4000));

        var top = DiagnosticsProbe.TopDeltas(before, after, 3);
        Assert.Single(top);
        Assert.Equal("value_scan_begin", top[0].Cmd);
        Assert.Equal(1, top[0].Count);          // 3 - 2
        Assert.Equal(4000, top[0].TotalMs);     // 4800 - 800
    }

    [Fact]
    public void A_command_first_seen_during_the_operation_counts_in_full()
    {
        var before = R(10, 100);
        var after  = R(12, 900, ("search_properties", 2, 800, 600));

        var top = DiagnosticsProbe.TopDeltas(before, after, 3);
        Assert.Single(top);
        Assert.Equal(2, top[0].Count);
        Assert.Equal(800, top[0].TotalMs);
    }

    [Fact]
    public void Untouched_commands_are_omitted()
    {
        // Only what THIS operation did is interesting.
        var before = R(10, 100, ("init", 1, 5, 5), ("get_pointers", 1, 0, 0));
        var after  = R(11, 900, ("init", 1, 5, 5), ("get_pointers", 1, 0, 0),
                                ("value_scan_begin", 1, 800, 800));

        var top = DiagnosticsProbe.TopDeltas(before, after, 5);
        Assert.Single(top);
        Assert.Equal("value_scan_begin", top[0].Cmd);
    }

    [Fact]
    public void The_probes_own_calls_are_excluded()
    {
        // The opening snapshot is itself a dispatch that lands in the closing one.
        // Reporting it would make every measurement list the measurement.
        var before = R(10, 100);
        var after  = R(12, 400, ("get_diagnostics", 1, 125, 125),
                                ("value_scan_begin", 1, 175, 175));

        var top = DiagnosticsProbe.TopDeltas(before, after, 5);
        Assert.Single(top);
        Assert.Equal("value_scan_begin", top[0].Cmd);
    }

    [Fact]
    public void Deltas_are_ranked_by_total_time()
    {
        var before = R(0, 0);
        var after  = R(6, 600, ("a", 1, 100, 100), ("b", 1, 300, 300), ("c", 1, 200, 200));

        var top = DiagnosticsProbe.TopDeltas(before, after, 3);
        Assert.Equal(new[] { "b", "c", "a" }, Array.ConvertAll(top.ToArray(), t => t.Cmd));
    }

    [Fact]
    public void Limit_truncates_the_ranking()
    {
        var before = R(0, 0);
        var after  = R(6, 600, ("a", 1, 100, 100), ("b", 1, 300, 300), ("c", 1, 200, 200));
        Assert.Equal(2, DiagnosticsProbe.TopDeltas(before, after, 2).Count);
        Assert.Equal(3, DiagnosticsProbe.TopDeltas(before, after, 0).Count);   // 0 = no limit
    }

    // ── Resilience: a mid-operation reset must not produce nonsense ──

    [Fact]
    public void A_counter_reset_mid_operation_never_yields_negatives()
    {
        // reset_diagnostics between the two samples makes 'after' SMALLER.
        var before = R(500, 9000, ("value_scan_begin", 9, 8000, 4000));
        var after  = R(2, 30, ("value_scan_begin", 1, 20, 20));

        var line = DiagnosticsProbe.Format("Value Scan", TimeSpan.FromSeconds(2), before, after);
        Assert.DoesNotContain("-", line.Replace("·", "").Replace("—", ""), StringComparison.Ordinal);

        var top = DiagnosticsProbe.TopDeltas(before, after, 3);
        Assert.All(top, t => Assert.True(t.TotalMs >= 0 && t.Count >= 0));
    }

    // ── The log line ────────────────────────────────────────────────

    [Fact]
    public void Format_leads_with_the_label_and_the_busy_share()
    {
        var before = R(0, 0);
        var after  = R(3, 500, ("value_scan_begin", 1, 500, 500));

        var line = DiagnosticsProbe.Format("Value Scan (First)", TimeSpan.FromSeconds(2), before, after);
        Assert.StartsWith("PERF Value Scan (First):", line, StringComparison.Ordinal);
        Assert.Contains("wall 2,000.0 ms", line, StringComparison.Ordinal);
        Assert.Contains("dispatcher busy 500.0 ms (25.0%)", line, StringComparison.Ordinal);
        // ONE dispatch, not the three the global counter saw. This assertion used to
        // say "3 dispatches" and was pinning the defect: the fixture's own numbers
        // (TotalDispatches=3, one command call) are the inconsistency, and the test
        // asserted the inconsistent half. See the sibling test below, which made
        // exactly this correction for busyMs and stopped one field short.
        Assert.Contains("1 dispatches", line, StringComparison.Ordinal);
        Assert.Contains("value_scan_begin 500.0ms/1x", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The line must never invite arithmetic that does not work out. Every number on it
    /// is over ONE denominator: the per-command rows. Reported from a live P3R session
    /// (2026-08-26) where the line read "11 dispatches ... busy 15.2 ms ... per call:
    /// dll 1.524" — and 15.2/11 is 1.38, because the divisor was really 10.
    /// </summary>
    [Fact]
    public void Dispatch_count_shares_the_denominator_with_busy_and_per_call()
    {
        // The probe's own get_diagnostics is in the global counter and in the command
        // table; TopDeltas skips it, so busy and the count must both skip it too.
        var before = R(0, 0);
        var after  = R(11, 200,
                       ("get_diagnostics", 1, 93, 93),
                       ("walk_instance_batch", 7, 12.3, 3.9),
                       ("walk_instance", 3, 2.9, 3.1));

        var line = DiagnosticsProbe.Format("Copy CE XML", TimeSpan.FromMilliseconds(46.1),
                                           before, after, transportMs: 30.0, transportCalls: 10);

        // 7 + 3 = 10 real calls, and 10 is also the transport call count the per-call
        // figures divide by. The global 11 must not appear as the dispatch count.
        Assert.Contains("10 dispatches", line, StringComparison.Ordinal);
        Assert.DoesNotContain("11 dispatches", line, StringComparison.Ordinal);

        // busy is 12.3 + 2.9 = 15.2, and 15.2 / 10 = 1.520 -- the arithmetic on the
        // line now closes.
        Assert.Contains("dispatcher busy 15.2 ms", line, StringComparison.Ordinal);
        Assert.Contains("dll 1.520", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// MaxMs is a running high-water mark that cannot be differenced, so it is the worst
    /// call SINCE THE LAST RESET, not the worst call in this operation. TopDeltas' own
    /// comment said "let the label carry the caveat"; the label said "max". Two exports
    /// minutes apart printed a byte-identical max while their totals moved.
    /// </summary>
    [Fact]
    public void The_high_water_mark_is_labelled_as_one()
    {
        var before = R(0, 0);
        var after  = R(2, 10, ("walk_instance", 2, 10, 3.5));

        var line = DiagnosticsProbe.Format("Copy CE XML", TimeSpan.FromMilliseconds(50), before, after);

        Assert.Contains("peak 3.5ms", line, StringComparison.Ordinal);
        Assert.Contains("session high-water", line, StringComparison.Ordinal);
        // The bare word that made it read as this operation's maximum.
        Assert.DoesNotContain("max 3.5ms", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Busy_excludes_the_probes_own_call_so_short_ops_cannot_exceed_100pct()
    {
        // The exact first-live-run failure: a 57.7 ms Copy CE Field reported
        // "busy 93 ms (161.2%)" because the global total included the probe's own
        // 93 ms opening get_diagnostics. Busy is now summed from the per-command
        // deltas, which exclude it.
        var before = R(0, 0);
        var after  = R(130, 93, ("get_diagnostics", 1, 93, 93), ("walk_instance", 128, 20, 15));

        var line = DiagnosticsProbe.Format("Copy CE Field",
            TimeSpan.FromMilliseconds(57.7), before, after);

        Assert.Contains("dispatcher busy 20.0 ms", line, StringComparison.Ordinal);
        Assert.DoesNotContain("161", line, StringComparison.Ordinal);
        Assert.DoesNotContain("get_diagnostics", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Busy_always_equals_the_sum_of_the_reported_rows()
    {
        // The percentage and the breakdown must agree by construction — they
        // disagreed on the live run (93 ms busy vs a top row showing 0 ms).
        var before = R(0, 0);
        var after  = R(9, 999, ("a", 1, 100.5, 100.5), ("b", 2, 40.25, 30),
                               ("get_diagnostics", 1, 500, 500));

        var line = DiagnosticsProbe.Format("X", TimeSpan.FromSeconds(1), before, after);
        var rows = DiagnosticsProbe.TopDeltas(before, after, 0);
        Assert.Equal(140.75, rows.Sum(r => r.TotalMs), 3);
        Assert.Contains("dispatcher busy 140.8 ms", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Sub_millisecond_commands_survive_as_fractions()
    {
        // GetTickCount64's 15.6 ms granularity floored these to zero; QPC
        // microseconds keep them. 1397 walk_instance calls at ~0.11 ms each.
        var before = R(0, 0);
        var after  = R(1397, 155.2, ("walk_instance", 1397, 155.2, 0.9));

        var rows = DiagnosticsProbe.TopDeltas(before, after, 1);
        Assert.Equal(155.2, rows[0].TotalMs, 3);
        Assert.True(rows[0].AvgMs > 0.0 && rows[0].AvgMs < 1.0,
                    $"avg should be sub-ms, was {rows[0].AvgMs}");
    }

    // ── The dll / ipc / ui split (the batching decision) ────────────

    [Fact]
    public void Split_separates_dll_work_from_ipc_latency_from_ui_work()
    {
        // Shaped after the real Copy CE XML: 20,357 calls, 5.36 s wall,
        // 1.65 s in the DLL. Transport (which INCLUDES the DLL time, since
        // SendAsync waits for the response) is the new measurement.
        var before = R(0, 0);
        var after  = R(20357, 1651.3, ("walk_instance", 20357, 1651.3, 14.3));

        var line = DiagnosticsProbe.Format("Copy CE XML", TimeSpan.FromMilliseconds(5362.7),
                                           before, after, transportMs: 4000.0, transportCalls: 20357);

        // dll 1651.3 · ipc = 4000 - 1651.3 = 2348.7 · ui = 5362.7 - 4000 = 1362.7
        Assert.Contains("split dll 1,651.3 / ipc 2,348.7 / ui 1,362.7 ms", line, StringComparison.Ordinal);
        Assert.Contains("per call:", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Split_is_omitted_when_transport_was_not_sampled()
    {
        // A probe from before the transport counter existed, or a run where the
        // snapshot failed — the line must simply not claim a split.
        var line = DiagnosticsProbe.Format("X", TimeSpan.FromSeconds(1), R(0, 0), R(1, 10, ("a", 1, 10, 10)));
        Assert.DoesNotContain("split dll", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Split_never_goes_negative_when_transport_undershoots_dll_time()
    {
        // Concurrency or clock skew can make transport look smaller than the DLL
        // time it contains; a negative "ipc" would be nonsense in a log.
        var line = DiagnosticsProbe.Format("X", TimeSpan.FromSeconds(1), R(0, 0),
                                           R(1, 900, ("a", 1, 900, 900)),
                                           transportMs: 100.0, transportCalls: 1);
        Assert.Contains("ipc 0.0", line, StringComparison.Ordinal);
        // The property is "no negative NUMBER", not "no hyphen". The old assertion used
        // the second as a proxy for the first and the proxy broke the moment the line
        // gained a hyphenated word ("session high-water") -- a test failing on prose is
        // a test measuring the wrong thing. Check for a minus sign that actually
        // precedes a digit.
        for (int i = 0; i < line.Length - 1; i++)
            Assert.False(line[i] == '-' && char.IsDigit(line[i + 1]),
                         $"negative number at index {i}: {line}");
    }

    [Fact]
    public void Per_call_figures_are_reported_to_microsecond_resolution()
    {
        // The whole decision hinges on sub-millisecond per-call costs, so N1
        // would round them all to 0.1/0.2 and hide the comparison.
        var before = R(0, 0);
        var after  = R(1000, 88.0, ("walk_instance", 1000, 88.0, 1.0));
        var line = DiagnosticsProbe.Format("X", TimeSpan.FromMilliseconds(296.0),
                                           before, after, transportMs: 208.0, transportCalls: 1000);
        Assert.Contains("dll 0.088", line, StringComparison.Ordinal);
        Assert.Contains("ipc 0.120", line, StringComparison.Ordinal);
        Assert.Contains("ui 0.088", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_survives_a_zero_length_operation()
    {
        // Guards the wall-clock divisor.
        var line = DiagnosticsProbe.Format("X", TimeSpan.Zero, R(0, 0), R(0, 0));
        Assert.Contains("(0.0%)", line, StringComparison.Ordinal);
        Assert.DoesNotContain("NaN", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_reports_gobjects_growth_only_when_it_moved()
    {
        var before = new DiagnosticsResult { GObjectsCount = 1000 };
        var same   = new DiagnosticsResult { GObjectsCount = 1000 };
        var grown  = new DiagnosticsResult { GObjectsCount = 1500 };

        Assert.DoesNotContain("GObjects", DiagnosticsProbe.Format("X", TimeSpan.FromSeconds(1), before, same),
                              StringComparison.Ordinal);
        Assert.Contains("GObjects +500", DiagnosticsProbe.Format("X", TimeSpan.FromSeconds(1), before, grown),
                        StringComparison.Ordinal);
    }

    [Fact]
    public void Format_reports_game_memory_only_on_a_meaningful_move()
    {
        // Sub-MiB jitter is noise, and the figure is the GAME's memory, not ours.
        var before = new DiagnosticsResult { Process = new DiagnosticsProcess { WorkingSetBytes = 1_000_000_000 } };
        var jitter = new DiagnosticsResult { Process = new DiagnosticsProcess { WorkingSetBytes = 1_000_100_000 } };
        var real   = new DiagnosticsResult { Process = new DiagnosticsProcess { WorkingSetBytes = 1_500_000_000 } };

        Assert.DoesNotContain("game WS", DiagnosticsProbe.Format("X", TimeSpan.FromSeconds(1), before, jitter),
                              StringComparison.Ordinal);
        Assert.Contains("game WS +", DiagnosticsProbe.Format("X", TimeSpan.FromSeconds(1), before, real),
                        StringComparison.Ordinal);
    }
}
