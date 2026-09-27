namespace UE5DumpUI.Helpers;

/// <summary>
/// One list control's queued view restore [KEYWORD-BOX-VIEW-KEEP]: carries the selection the user
/// had across a burst of rebuilds, and lets only the newest restore of the burst run.
/// </summary>
/// <remarks>
/// <para>A rebuild detaches the selection, and its restore is posted at Background priority, which
/// is BELOW input: keys that arrive before it runs rebuild again, and each of those captures read
/// the detached, empty selection. Measured on build 3574 (Interesting Funcs, one row picked, "spawn
/// act" typed and Ctrl+A, Delete sent at once): the first restore put the row back but only
/// scrolled it into view; the clear's restore held an empty capture and did nothing, so the row
/// sat at the bottom edge instead of coming to the top.</para>
/// <para>So while a restore is queued, a capture returns the state it carries — unless the control
/// has a selection again, which the user made after the rebuild and which wins — and every
/// restore but the newest is dropped: the newest carries the original selection and the mode of
/// the last edit.</para>
/// </remarks>
public sealed class FilterViewRestoreQueue
{
    private FilterViewState? _carried;
    private int _ticket;

    /// <summary>The state to hand the keeper before a rebuild. <paramref name="live"/> reads the
    /// control as it is now.</summary>
    public FilterViewState Capture(Func<FilterViewState> live)
    {
        var now = live();
        return now.Selected.Count > 0 || _carried == null ? now : _carried;
    }

    /// <summary>Queue a restore of <paramref name="state"/>; returns its ticket.</summary>
    public int Schedule(FilterViewState state)
    {
        _carried = state;
        return ++_ticket;
    }

    /// <summary>True when <paramref name="ticket"/> is the newest restore, which then runs; captures
    /// read the control again from here on. False for a restore a newer one replaced.</summary>
    public bool Begin(int ticket)
    {
        if (ticket != _ticket) return false;
        _carried = null;
        return true;
    }

    /// <summary>False once a newer restore was queued: a later step of an older restore (a posted
    /// scroll) must not move the view the newer one is about to set.</summary>
    public bool IsNewest(int ticket) => ticket == _ticket;
}
