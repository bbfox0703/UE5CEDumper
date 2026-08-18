using System.Globalization;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Builds the one-line headline that opens <c>%LOCALAPPDATA%\UE5CEDumper\crash.log</c>.
///
/// <para><b>Why this is not a string interpolation at the call site.</b> The
/// handler used to write the literal phrase <c>"UE5DumpUI startup crash"</c> for
/// EVERY crash. A clipboard fault 31 minutes into a connected session was filed
/// as a startup crash and read as one — the phrase is the first thing a triage
/// pass sees, and it was wrong in the most misleading direction available
/// (field report <c>[PASTECRASH-2026-08-18]</c>).</para>
///
/// <para>The headline now carries both facts the reader actually needs — WHICH
/// phase and HOW LONG the process had been alive — and it cross-checks them
/// against each other, because a phase marker is only as honest as the code that
/// advances it. If the phase still says <see cref="AppPhase.Startup"/> after
/// minutes of uptime, that is itself a defect (the marker was never advanced),
/// and the headline says so rather than repeating the old lie in a new
/// format.</para>
/// </summary>
internal static class CrashReportFormatter
{
    /// <summary>Timestamp shape, unchanged from the original handler so existing
    /// logs and this one read the same way.</summary>
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

        // The phase marker never advanced past Startup, yet the process had been
        // alive far longer than any startup takes. Report BOTH readings instead of
        // trusting the stale one — this is the exact failure the honest-phase fix
        // exists to prevent, so it must not be reproducible through the new path.
        if (phase == AppPhase.Startup &&
            uptime.TotalSeconds > Constants.CrashStartupPhaseMaxSeconds)
        {
            return $"{Constants.AppName} crash — phase marker still says STARTUP after {when}; " +
                   "the marker was never advanced, so treat the phase as UNKNOWN";
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
