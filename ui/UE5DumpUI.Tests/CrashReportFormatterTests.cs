using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The crash.log headline names the real phase and the real uptime
/// (<c>[PASTECRASH-2026-08-18]</c>): a fixed phrase such as
/// <c>"UE5DumpUI startup crash"</c> for every crash would file a fault 31 minutes
/// into a live session as a startup failure. These pin that the headline names
/// both, and that a mid-session crash cannot be called a startup crash.
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
        // A crash 31 minutes in, phase Running.
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
    public void LongStartupPhase_ReportsBothReadingsWithoutAccusingTheMarker()
    {
        // A long-lived process still reading "Startup" is EITHER a stale marker or a
        // genuinely slow cold start (first launch after an update, AV scanning the
        // 54 MB AOT binary, a cold cache). The headline must surface both readings
        // and commit to neither: asserting "the marker was never advanced" would
        // print an accusation of a second defect on top of every slow-start crash —
        // the same "state a guess as a fact" mistake as a fixed "startup crash"
        // headline.
        var line = CrashReportFormatter.Headline(AppPhase.Startup, TimeSpan.FromMinutes(31));

        Assert.Contains("UNCERTAIN", line, StringComparison.Ordinal);
        Assert.Contains("31m 00s", line, StringComparison.Ordinal);
        // Both possibilities are named...
        Assert.Contains("genuinely that slow", line, StringComparison.Ordinal);
        Assert.Contains("never advanced", line, StringComparison.Ordinal);
        // ...and neither is asserted as the finding.
        Assert.Contains("EITHER", line, StringComparison.Ordinal);
        Assert.DoesNotContain("UNKNOWN", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortStartupCrash_DoesNotTripTheLongStartupBranch()
    {
        // Negative control for the branch above: a genuine startup crash inside the
        // window must read as an ordinary startup crash.
        var line = CrashReportFormatter.Headline(
            AppPhase.Startup, TimeSpan.FromSeconds(UE5DumpUI.Constants.CrashStartupPhaseMaxSeconds - 1));

        Assert.DoesNotContain("UNCERTAIN", line, StringComparison.Ordinal);
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
