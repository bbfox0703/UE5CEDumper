using System.Globalization;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Builds the one-line headline that opens <c>%LOCALAPPDATA%\UE5CEDumper\crash.log</c>.
///
/// <para><b>Why this is not a string interpolation at the call site.</b> The
/// headline is the first thing a triage pass sees, so a fixed phrase such as
/// <c>"UE5DumpUI startup crash"</c> would mislabel every fault past startup in the
/// most misleading direction available (<c>[PASTECRASH-2026-08-18]</c>).</para>
///
/// <para>The headline carries both facts the reader actually needs — WHICH
/// phase and HOW LONG the process had been alive — and it cross-checks them
/// against each other, because a phase marker is only as honest as the code that
/// advances it. If the phase still says <see cref="AppPhase.Startup"/> after
/// minutes of uptime, that is itself a defect (the marker was never advanced),
/// and the headline says so.</para>
/// </summary>
internal static class CrashReportFormatter
{
    /// <summary>Timestamp shape, the one existing crash.log entries carry, so
    /// old and new entries read the same way.</summary>
    internal const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// Human-readable uptime. Three bands, because the useful precision differs:
    /// sub-minute crashes are startup-shaped and want milliseconds, hour-plus
    /// sessions want hours.
    /// </summary>
    internal static string FormatUptime(TimeSpan uptime)
    {
        if (uptime < TimeSpan.Zero)
            uptime = TimeSpan.Zero;

        if (uptime.TotalMinutes < 1)
            return uptime.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + "s";

        if (uptime.TotalHours < 1)
            return string.Format(CultureInfo.InvariantCulture, "{0}m {1:00}s",
                (int)uptime.TotalMinutes, uptime.Seconds);

        return string.Format(CultureInfo.InvariantCulture, "{0}h {1:00}m {2:00}s",
            (int)uptime.TotalHours, uptime.Minutes, uptime.Seconds);
    }

    /// <summary>
    /// The headline line, without the timestamp. Always names the app, the phase
    /// and the uptime.
    /// </summary>
    internal static string Headline(AppPhase phase, TimeSpan uptime)
    {
        var when = FormatUptime(uptime);

        // The phase still says Startup, yet the process had been alive longer than a
        // startup is expected to take. Report BOTH readings rather than trusting the
        // one that might be stale.
        //
        // Hedged on purpose. This is a HEURISTIC over one threshold, and it has a
        // legitimate false positive: a genuinely slow cold start — first launch after
        // an update, a cold NTFS cache, AV scanning a 54 MB AOT binary, a network
        // drive in the path — really can spend minutes still IN startup, and then the
        // phase marker is correct and the crash really is a startup crash. Wording it
        // as a finding ("the marker was never advanced") would put an accusation of a
        // second defect at the top of every such report, which is the same category
        // of mistake as a hard-coded "startup crash" label, the one this whole formatter
        // exists to avoid: stating a guess as a fact.
        if (phase == AppPhase.Startup &&
            uptime.TotalSeconds > Constants.CrashStartupPhaseMaxSeconds)
        {
            return $"{Constants.AppName} crash — phase still says STARTUP after {when}, which is " +
                   "longer than startup normally takes; EITHER the start was genuinely that slow " +
                   "OR the phase marker never advanced, so treat the phase as UNCERTAIN";
        }

        var phaseText = phase switch
        {
            AppPhase.Startup => "during STARTUP",
            AppPhase.Running => "while RUNNING",
            AppPhase.ShuttingDown => "during SHUTDOWN",
            _ => "in an UNKNOWN phase",
        };

        return $"{Constants.AppName} crash {phaseText} (uptime {when})";
    }

    /// <summary>The full <c>crash.log</c> body: timestamped headline, then the
    /// exception exactly as <c>ToString()</c> renders it (inner exceptions and
    /// stack traces included).</summary>
    internal static string Format(DateTimeOffset when, AppPhase phase, TimeSpan uptime, Exception ex)
    {
        var stamp = when.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        return $"[{stamp}] {Headline(phase, uptime)}\n{ex}\n";
    }
}
