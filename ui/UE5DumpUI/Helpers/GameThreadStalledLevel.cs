namespace UE5DumpUI.Helpers;

/// <summary>
/// The single combined "game thread paused" level for the two-lane pipe.
///
/// Both pipe lanes ride the same global <c>game_thread_stalled</c> flag on every
/// response, but each lane only sees it when IT gets a response. An independent
/// per-lane latch on each lane's <b>edge</b> transitions would stick the banner ON:
/// if the bulk lane observes the pause (mid-scan) and then goes idle while
/// the interactive lane carries the traffic after the game resumes, the bulk
/// lane's true→false edge never fires and the interactive lane's own latch is
/// already false, so neither raises the clear.
///
/// So there is one latch, not two: every per-response observation from EITHER lane
/// is fed in, and the actively-answering lane continuously overwrites a stale
/// value an idle lane left behind. <see cref="Observe"/> returns the value to
/// raise only when the combined level actually changes, so a busy paging burst
/// still does not spam the consumer.
///
/// Thread-safe: the two lanes' read loops call <see cref="Observe"/> from
/// different threads.
/// </summary>
internal sealed class GameThreadStalledLevel
{
    private readonly object _gate = new();
    private bool _level;

    /// <summary>Record one lane's per-response observation of the global stalled
    /// flag. Returns the new combined level when it changed (so the caller raises
    /// it), or <c>null</c> when it is unchanged.</summary>
    public bool? Observe(bool stalled)
    {
        lock (_gate)
        {
            if (stalled == _level) return null;
            _level = stalled;
            return stalled;
        }
    }

    /// <summary>Reset to "not stalled" on a (re)connect so a fresh session starts
    /// clean and the first observation after connect always raises.</summary>
    public void Reset()
    {
        lock (_gate) { _level = false; }
    }
}
