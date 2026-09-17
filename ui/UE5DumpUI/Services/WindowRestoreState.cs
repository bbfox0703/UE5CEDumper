using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace UE5DumpUI.Services;

/// <summary>
/// Pure, Window-agnostic state machine for the "restore to the rect it had before
/// maximize" behaviour. Avalonia 12 on Windows can land a restored (Maximized →
/// Normal) window stretched across monitors or at the wrong spot, so we snapshot the
/// last NORMAL geometry and re-apply it on the way back down. The decision logic lives
/// here — free of Avalonia <c>Window</c> coupling beyond the plain <see cref="PixelPoint"/>
/// struct and the <see cref="WindowState"/> enum — so it is unit-testable without a
/// headless UI. <see cref="Views.ManagedDialogWindow"/> drives it from the live window's
/// events; <see cref="Views.MainWindow"/> keeps its own equivalent (intertwined with
/// cross-restart persistence) and is intentionally not migrated.
///
/// The deferred-commit dance (Width/Height arrive BEFORE WindowState during a maximize
/// on Windows) is the caller's responsibility: it stashes geometry via
/// <see cref="NoteSize"/>/<see cref="NotePosition"/> while the window reads as Normal,
/// then calls <see cref="Commit"/> one dispatcher tick later passing the RE-READ state —
/// <see cref="Commit"/> abandons the stash if the window has since flipped to non-Normal
/// (the stashed values would be the maximized dimensions).
///
/// Two extra safeguards back the "second restore jumps to 0,0" fix:
/// <list type="bullet">
/// <item><b>Position acceptability guard</b> — <see cref="NotePosition"/> and
/// <see cref="Commit"/> reject a top-left that isn't visible-enough on any current
/// monitor (the position twin of <see cref="NoteSize"/>'s &gt;0 guard), so an off-screen
/// transition artifact can't latch into the snapshot. The caller pushes the current
/// monitors via <see cref="SetScreens"/>.</item>
/// <item><b>Restore re-seed</b> — the caller re-applies the restore rect on a deferred
/// (Background) dispatcher tick instead of synchronously mid-transition (which fought the
/// OS's own un-maximize placement). After re-applying it calls
/// <see cref="OnRestoreReapplied"/> so the Position/Size events that re-apply triggers are
/// re-seeded into the stash rather than mis-read as a fresh user move.</item>
/// </list>
/// </summary>
public sealed class WindowRestoreState
{
    private PixelPoint? _normalPos;
    private double _normalW;
    private double _normalH;

    private PixelPoint _pendPos;
    private double _pendW;
    private double _pendH;

    // Current monitors' working areas (physical px) used by the position acceptability
    // guard. Empty => accept everything (the headless / not-yet-shown path, and keeps the
    // pure tests screen-agnostic). The caller refreshes this from Screens.All.
    private IReadOnlyList<(int X, int Y, int W, int H)> _screens = Array.Empty<(int, int, int, int)>();

    // [W3-DIP-PIXELS] The window's RenderScaling. The stash's width/height are DIPs (Avalonia Width/Height), and
    // WindowPlacement works in PHYSICAL pixels -- the AF21 unit fix MainWindow got and this twin never did.
    private double _scale = 1.0;

    /// <summary>True once <see cref="Seed"/> has run (the window has opened in Normal
    /// state). Callers ignore size/position churn before this.</summary>
    public bool Seeded { get; private set; }

    /// <summary>Last committed normal-state top-left, or null before the first seed.</summary>
    public PixelPoint? NormalPosition => _normalPos;
    /// <summary>Last committed normal-state width.</summary>
    public double NormalWidth => _normalW;
    /// <summary>Last committed normal-state height.</summary>
    public double NormalHeight => _normalH;

    /// <summary>Refresh the monitor working areas (physical px) used by the position
    /// acceptability guard. The caller pushes <c>Screens.All</c> on open and on every
    /// window-state transition. Null is treated as "no screens" (accept everything).</summary>
    public void SetScreens(IReadOnlyList<(int X, int Y, int W, int H)> screens)
        => _screens = screens ?? Array.Empty<(int, int, int, int)>();

    /// <summary>[W3-DIP-PIXELS] The window's current RenderScaling, pushed with <see cref="SetScreens"/>. The stashed size
    /// stays in DIPs -- it is re-applied as Width/Height -- and only the visibility test converts it. A non-positive or
    /// non-finite scale reads as 1.</summary>
    public void SetScale(double scale) => _scale = scale > 0 && double.IsFinite(scale) ? scale : 1.0;

    /// <summary>Capture the initial normal-state rect (call once, after the window is
    /// open and still Normal). Both the committed snapshot and the pending stash start
    /// here.</summary>
    public void Seed(PixelPoint pos, double w, double h)
    {
        _normalPos = pos;
        _normalW = w;
        _normalH = h;
        _pendPos = pos;
        _pendW = w;
        _pendH = h;
        Seeded = true;
    }

    /// <summary>Stash a new top-left seen while the window is Normal (commit later).
    /// A position that isn't visible-enough on any current monitor — the maximized-origin
    /// transient or an off-screen park — is rejected so it can't poison the restore
    /// snapshot (the position twin of <see cref="NoteSize"/>'s &gt;0 guard).</summary>
    public void NotePosition(PixelPoint pos)
    {
        if (PositionAcceptable(pos, _pendW, _pendH)) _pendPos = pos;
    }

    /// <summary>Stash a new size seen while the window is Normal (commit later).
    /// Non-positive values (transient layout noise) are ignored.</summary>
    public void NoteSize(double w, double h)
    {
        if (w > 0) _pendW = w;
        if (h > 0) _pendH = h;
    }

    /// <summary>Promote the pending stash into the committed snapshot — but only if the
    /// window is still Normal (<paramref name="isNormalNow"/>). When it has flipped to
    /// maximized/minimized since the stash, the pending values are the maximized
    /// dimensions, so the commit is abandoned and the prior snapshot kept.</summary>
    public void Commit(bool isNormalNow)
    {
        if (!isNormalNow) return;
        if (_pendW > 0) _normalW = _pendW;
        if (_pendH > 0) _normalH = _pendH;
        // Position promotion is gated the same way the stash is (was unconditional):
        // an off-screen / maximized-origin top-left must never latch into the snapshot.
        if (PositionAcceptable(_pendPos, _pendW, _pendH)) _normalPos = _pendPos;
    }

    /// <summary>Re-seed the pending stash from the committed snapshot. The caller invokes
    /// this immediately after re-applying the restore rect to the live window, so the
    /// Position/Size change events that re-apply triggers are not mis-read as a fresh user
    /// move (which would thrash, or re-poison, the snapshot).</summary>
    public void OnRestoreReapplied()
    {
        if (_normalPos is { } p) _pendPos = p;
        _pendW = _normalW;
        _pendH = _normalH;
    }

    /// <summary>The rect to re-apply when returning to Normal from a non-Normal state.
    /// False when there is no usable snapshot yet (not seeded / degenerate size).</summary>
    public bool TryGetRestoreRect(out PixelPoint pos, out double w, out double h)
    {
        pos = _normalPos ?? default;
        w = _normalW;
        h = _normalH;
        return _normalPos.HasValue && _normalW > 0 && _normalH > 0;
    }

    /// <summary>True when the (pos, size) rect shows a grabbable chunk on some current
    /// monitor. No screens (headless / pre-show) => accept, so the pure tests and the
    /// not-yet-shown window keep their existing behaviour. Size is in DIPs and is converted with the pushed
    /// <see cref="SetScale"/> scale: WindowPlacement works in PHYSICAL pixels, and no min-visible tolerance absorbs a
    /// 225% factor (it used to claim one did) [W3-DIP-PIXELS].</summary>
    private bool PositionAcceptable(PixelPoint pos, double w, double h)
        => _screens.Count == 0
           || WindowPlacement.IsVisibleEnough(
                pos.X, pos.Y, (int)Math.Round(w * _scale), (int)Math.Round(h * _scale), _screens);
}
