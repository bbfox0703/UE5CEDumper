namespace UE5DumpUI.Helpers;

/// <summary>
/// One list control's queued view restore [KEYWORD-BOX-VIEW-KEEP]: carries the selection the user
/// had across a burst of rebuilds, lets only the newest restore of the burst run, and parks a
/// selection the filter hid entirely until its rows are shown again.
/// </summary>
/// <remarks>
/// <para>A rebuild detaches the selection, and its restore is posted at Background priority, which
/// is BELOW input: keys that arrive before it runs rebuild again, and each of those captures read
/// the detached, empty selection. Measured on build 3574 (Interesting Funcs, one row picked, keys
/// sent at once): the clear left the row at the bottom edge instead of bringing it to the top. So
/// while a restore is queued, a capture returns the state it carries — unless the control has a
/// selection again, which the user made after the rebuild and which wins — and every restore but
/// the newest is dropped: the newest carries the original selection and the mode of the last edit.</para>
/// <para>A keyword that hides EVERY selected row (a typo, zero rows) used to drop the selection for
/// good, so the clear that fixed the typo had nothing to bring back (measured on build 3576). The
/// maintainer's call, 2026-09-27: keep the last non-empty selection. A restore that finds none of
/// its rows parks them; a later capture with nothing selected and nothing queued hands them back,
/// so they return as soon as an edit shows them again. A partly hidden selection is not parked:
/// the rows still shown are the selection now. The View drops the parked rows when the USER
/// changes the selection, and the keeper when the program resets the box.</para>
/// </remarks>
public sealed class FilterViewRestoreQueue
{
    private FilterViewState? _carried;
    private IReadOnlyList<object>? _parked;
    private int _ticket;

    /// <summary>True while a hidden selection is parked.</summary>
    public bool HasParked => _parked != null;

    /// <summary>The state to hand the keeper before a rebuild. <paramref name="live"/> reads the
    /// control as it is now.</summary>
    public FilterViewState Capture(Func<FilterViewState> live)
    {
        var now = live();
        if (now.Selected.Count > 0) return now;
        if (_carried != null) return _carried;
        return _parked != null ? new FilterViewState(_parked, now.TopRow) : now;
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

    /// <summary>Record what a running restore found: none of a non-empty selection parks it; any of
    /// it makes what is shown the selection again.</summary>
    public void Settle(FilterViewState state, int found)
    {
        if (found > 0) _parked = null;
        else if (state.Selected.Count > 0) _parked = state.Selected;
    }

    /// <summary>Drop the parked selection: the user picked (or cleared) the selection themselves, or
    /// the program reset the box.</summary>
    public void Unpark() => _parked = null;
}
