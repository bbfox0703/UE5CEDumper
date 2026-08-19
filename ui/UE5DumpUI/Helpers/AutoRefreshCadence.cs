namespace UE5DumpUI.Helpers;

/// <summary>
/// Why a scheduled auto-refresh tick did not perform a refresh.
/// </summary>
public enum AutoRefreshSkip
{
    /// <summary>Nothing in the way — run the refresh.</summary>
    None,
    /// <summary>The previous refresh is still awaiting a pipe round trip.</summary>
    InProgress,
    /// <summary>A grid cell is open for editing; refreshing would yank it away.</summary>
    Editing,
    /// <summary>Nothing is rooted in the panel (never walked, cleared, or disconnected).</summary>
    NoData,
}

/// <summary>The countdown state after one 1-second step: the value to carry, and the label to show.</summary>
public readonly record struct AutoRefreshCountdown(int Remaining, string Label);

/// <summary>
/// Pure cadence rules for the Live Walker's auto-refresh — extracted from
/// <c>LiveWalkerViewModel</c> because the shipped version could not be tested and
/// was wrong in a way that produced NO error of any kind.
///
/// <para><b>The defect this exists to make impossible</b> (reported by the maintainer
/// against 1.0.0.3262, tag <c>[AUTOREFRESH-2026-08-19]</c>): the countdown value was
/// reset in exactly one place — inside the refresh tick, <i>after</i> its early-return
/// guard. So any tick that was skipped (a cell being edited, no rooted object, a dead
/// pipe) left the counter to decrement to zero and clamp there **forever**, while the
/// Auto toggle still read ON and nothing was logged. A 21-minute session's pipe log
/// carried zero periodic walks and zero errors: the panel silently did nothing while
/// displaying a frozen "0s".</para>
///
/// <para>Two rules fix the class of defect rather than the one trigger:
/// <list type="number">
/// <item>the countdown <b>re-arms itself</b> at zero — it displays the timer's PERIOD,
///       which keeps elapsing whether or not the last tick did any work, so it must
///       never be able to stick; and</item>
/// <item>a skipped tick <b>says why</b> instead of showing a number that is not moving,
///       so "paused because you are editing" cannot be mistaken for "broken".</item>
/// </list></para>
/// </summary>
public static class AutoRefreshCadence
{
    /// <summary>Shown when auto-refresh is off — the bare unit label beside the interval box.</summary>
    public const string LabelIdle = "sec";
    /// <summary>Shown while a refresh is in flight (the countdown is meaningless until it lands).</summary>
    public const string LabelRefreshing = "sec · refreshing...";
    /// <summary>Shown while an open cell editor is suppressing refreshes.</summary>
    public const string LabelEditing = "sec · paused (editing)";
    /// <summary>Shown while the panel has nothing rooted to refresh.</summary>
    public const string LabelNoData = "sec · paused (no data)";

    /// <summary>The normal running label, e.g. <c>"sec · 7s"</c>.</summary>
    public static string LabelFor(int remaining) => $"sec · {remaining}s";

    /// <summary>
    /// Decide whether this tick may refresh. Order matters: an in-flight refresh is
    /// reported ahead of everything else because the panel's other state is mid-update
    /// while it runs.
    /// </summary>
    public static AutoRefreshSkip Classify(bool refreshInProgress, bool isEditing,
                                           bool hasData, string? currentAddress)
    {
        if (refreshInProgress) return AutoRefreshSkip.InProgress;
        if (isEditing) return AutoRefreshSkip.Editing;
        if (!hasData || string.IsNullOrEmpty(currentAddress)) return AutoRefreshSkip.NoData;
        return AutoRefreshSkip.None;
    }

    /// <summary>
    /// The effective period: the user's interval, floored by the benchmarked minimum,
    /// and never below 1 second (a zero-second DispatcherTimer spins the UI thread).
    /// </summary>
    public static int NormalizeInterval(int intervalSec, int minSec)
    {
        var n = intervalSec > minSec ? intervalSec : minSec;
        return n < 1 ? 1 : n;
    }

    /// <summary>
    /// One 1-second step of the countdown display.
    ///
    /// <para>The reset at zero is the fix: it lives HERE, on the path that always runs,
    /// instead of on the refresh's success path that may never run again.</para>
    /// </summary>
    public static AutoRefreshCountdown Step(int remaining, int intervalSec, AutoRefreshSkip skip)
    {
        if (intervalSec < 1) intervalSec = 1;

        // A refresh in flight freezes the number deliberately — it resumes from the
        // value the completing refresh writes back, and the label says so meanwhile.
        if (skip == AutoRefreshSkip.InProgress)
            return new AutoRefreshCountdown(remaining, LabelRefreshing);

        var next = remaining - 1;
        if (next <= 0) next = intervalSec;   // re-arm; the counter can never stick at 0

        return skip switch
        {
            AutoRefreshSkip.Editing => new AutoRefreshCountdown(next, LabelEditing),
            AutoRefreshSkip.NoData => new AutoRefreshCountdown(next, LabelNoData),
            _ => new AutoRefreshCountdown(next, LabelFor(next)),
        };
    }

    /// <summary>
    /// Should a suspended auto-refresh come back on?
    ///
    /// <para><paramref name="resumePending"/> is set ONLY when something outside the
    /// user's control stopped it — the pipe dropping, or switching away from the tab.
    /// A user untick and every navigation re-root deliberately do NOT set it, so those
    /// stay off until re-ticked. The data checks matter because resuming onto an empty
    /// panel would just re-arm a tick that skips, which is the state this whole file
    /// exists to prevent.</para>
    /// </summary>
    public static bool ShouldResume(bool resumePending, bool isRunning,
                                    bool hasData, string? currentAddress)
        => resumePending && !isRunning && hasData && !string.IsNullOrEmpty(currentAddress);
}
