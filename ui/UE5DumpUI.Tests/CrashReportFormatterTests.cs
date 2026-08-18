using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The crash.log headline used to be the hard-coded phrase
/// <c>"UE5DumpUI startup crash"</c> for every crash, which filed a fault 31
/// minutes into a live session as a startup failure
/// (<c>[PASTECRASH-2026-08-18]</c>). These pin the replacement: the headline must
/// name the real phase, the real uptime, and must not be able to reproduce the
/// old lie.
/// </summary>
public class CrashReportFormatterTests
{
    [Fact]
    public void StartupCrash_StillSaysStartup()
    {
        var line = CrashReportFormatter.Headline(AppPhase.Startup, TimeSpan.FromSeconds(0.85));

        Assert.Contains("STARTUP", line, StringComparison.Ordinal);
        Assert.Contains("0.85s", line, StringComparison.Ordinal);
    }

    [Fact]
    public void MidSessionCrash_IsNotCalledAStartupCrash()
    {
        // The exact regression: 31 minutes in, phase Running.
        var line = CrashReportFormatter.Headline(
            AppPhase.Running, TimeSpan.FromMinutes(31) + TimeSpan.FromSeconds(12));

        Assert.DoesNotContain("STARTUP", line, StringComparison.Ordinal);
        Assert.Contains("RUNNING", line, StringComparison.Ordinal);
        Assert.Contains("31m 12s", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ShutdownCrash_SaysShutdown()
    {
        var line = CrashReportFormatter.Headline(AppPhase.ShuttingDown, TimeSpan.FromMinutes(5));

        Assert.Contains("SHUTDOWN", line, StringComparison.Ordinal);
    }

    [Fact]
    public void StalePhaseMarker_IsReportedAsStaleRatherThanRepeated()
    {
        // If the phase marker is never advanced, the honest answer is "unknown",
        // not "startup" — otherwise the fix just re-creates the original defect in
        // a new wording.
        var line = CrashReportFormatter.Headline(AppPhase.Startup, TimeSpan.FromMinutes(31));

        Assert.Contains("never advanced", line, StringComparison.Ordinal);
        Assert.Contains("UNKNOWN", line, StringComparison.Ordinal);
        Assert.Contains("31m 00s", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortStartupCrash_DoesNotTripTheStaleMarkerBranch()
    {
        // Negative control for the branch above: a genuine startup crash inside the
        // window must read as an ordinary startup crash.
        var line = CrashReportFormatter.Headline(
            AppPhase.Startup, TimeSpan.FromSeconds(UE5DumpUI.Constants.CrashStartupPhaseMaxSeconds - 1));

        Assert.DoesNotContain("never advanced", line, StringComparison.Ordinal);
        Assert.Contains("during STARTUP", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "0.00s")]
    [InlineData(0.85, "0.85s")]
    [InlineData(59.4, "59.40s")]
    [InlineData(60, "1m 00s")]
    [InlineData(1872, "31m 12s")]
    [InlineData(3600, "1h 00m 00s")]
    [InlineData(3865, "1h 04m 25s")]
    public void UptimeBands(double seconds, string expected)
    {
        Assert.Equal(expected, CrashReportFormatter.FormatUptime(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void NegativeUptime_ClampsInsteadOfPrintingNonsense()
    {
        // A clock adjustment must not produce "-3.00s" in a crash report.
        Assert.Equal("0.00s", CrashReportFormatter.FormatUptime(TimeSpan.FromSeconds(-3)));
    }

    [Fact]
    public void Format_CarriesTimestampHeadlineAndTheWholeException()
    {
        var when = new DateTimeOffset(2026, 8, 18, 21, 3, 11, TimeSpan.Zero);
        var ex = new InvalidOperationException("outer", new ArgumentException("inner"));

        var text = CrashReportFormatter.Format(when, AppPhase.Running, TimeSpan.FromMinutes(31), ex);

        Assert.StartsWith("[2026-08-18 21:03:11] ", text, StringComparison.Ordinal);
        Assert.Contains("while RUNNING", text, StringComparison.Ordinal);
        Assert.Contains("outer", text, StringComparison.Ordinal);
        Assert.Contains("inner", text, StringComparison.Ordinal);   // inner exceptions survive
    }
}
