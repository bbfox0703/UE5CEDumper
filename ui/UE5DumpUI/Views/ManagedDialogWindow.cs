using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using UE5DumpUI.Services;

namespace UE5DumpUI.Views;

/// <summary>
/// Base class for the resizable, <see cref="WindowStartupLocation.CenterOwner"/> code-behind
/// dialogs (PropertyXrefDialog / FunctionPropsDialog / ObjectInstancePickerDialog). It fixes
/// the same maximize→restore placement bug the main window already handles: on Avalonia 12
/// (Windows), restoring a maximized dialog can land it stretched across monitors or at the
/// wrong spot instead of where it was before the maximize. We snapshot the last NORMAL
/// geometry and re-apply it on the way back to Normal.
///
/// The snapshot/decision logic lives in the pure, unit-tested
/// <see cref="WindowRestoreState"/>; this class only wires it to the live window's events.
/// Seeding happens on <see cref="Window.Opened"/> (CenterOwner sets the position only after
/// the window is shown, so the ctor can't read it). The deferred commit handles the
/// Windows quirk where Width/Height change BEFORE WindowState during a maximize: stash on
/// the size/position change, then commit one Background-priority dispatcher tick later and
/// re-check WindowState — abandoning the stash if it has flipped to maximized.
///
/// The re-apply on the way back to Normal is ALSO deferred to a Background tick rather than
/// done synchronously inside the WindowState change: a synchronous set mid-un-maximize
/// emitted a maximized-origin (~0,0) position transient that latched into the snapshot and
/// surfaced as the window jumping to 0,0 on the SECOND restore. Running after the burst
/// settles also lets the re-apply override the OS's placement instead of racing it.
///
/// MainWindow keeps its own equivalent (entangled with cross-restart persistence + screen
/// validation) and is intentionally not migrated onto this base.
/// </summary>
public abstract class ManagedDialogWindow : Window
{
    private readonly WindowRestoreState _restore = new();
    private WindowState _previousWindowState = WindowState.Normal;
    private bool _commitScheduled;
    // Set once the window has closed so a deferred re-apply that fires afterwards can't
    // touch a torn-down window (Position/Size on a closed handle). See OnClosedTeardown.
    private bool _closed;

    protected ManagedDialogWindow()
    {
        // Position isn't an AvaloniaProperty in 12 — listen via the explicit event.
        PositionChanged += OnPositionChanged;
        // Seed once shown: CenterOwner positions the window at Opened, not in the ctor.
        Opened += OnOpenedSeed;
        Closed += OnClosedTeardown;
    }

    private void OnClosedTeardown(object? sender, EventArgs e)
    {
        _closed = true;
        Closed -= OnClosedTeardown;
    }

    /// <summary>Current monitors' working areas as plain physical-pixel rects, for the
    /// restore-state position guard. Mirrors MainWindow.CurrentScreenWorkingAreas.</summary>
    private IReadOnlyList<(int X, int Y, int W, int H)> CurrentScreens()
    {
        var list = new List<(int, int, int, int)>();
        var all = Screens?.All;
        if (all == null) return list;
        foreach (var s in all)
        {
            var wa = s.WorkingArea;
            list.Add((wa.X, wa.Y, wa.Width, wa.Height));
        }
        return list;
    }

    private void OnOpenedSeed(object? sender, EventArgs e)
    {
        // Unsubscribe in finally so a (vanishingly unlikely) throw while seeding can't
        // leave the handler attached and re-seed on a later Opened.
        try
        {
            _restore.SetScreens(CurrentScreens());
            _restore.SetScale(RenderScaling);   // [W3-DIP-PIXELS] the stash is DIPs; the guard is physical px
            _previousWindowState = WindowState;
            if (WindowState == WindowState.Normal)
                _restore.Seed(Position, Width, Height);
        }
        finally
        {
            Opened -= OnOpenedSeed;
        }
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (!_restore.Seeded) return;
        if (WindowState == WindowState.Normal)
        {
            _restore.NotePosition(Position);
            ScheduleSnapshotCommit();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            var newState = change.GetNewValue<WindowState>();
            HandleWindowStateTransition(_previousWindowState, newState);
            _previousWindowState = newState;
        }
        else if (change.Property == WidthProperty || change.Property == HeightProperty)
        {
            // Reading WindowState here is unreliable on the to-maximized transition
            // (Width/Height flip first), so stash now and commit deferred (see below).
            if (_restore.Seeded && WindowState == WindowState.Normal)
            {
                _restore.NoteSize(Width, Height);
                ScheduleSnapshotCommit();
            }
        }
    }

    // Coalesced deferred commit at Background priority so any WindowState change in the
    // same Win32 message has propagated before we promote the stash.
    private void ScheduleSnapshotCommit()
    {
        if (_commitScheduled) return;
        _commitScheduled = true;
        Dispatcher.UIThread.Post(CommitSnapshot, DispatcherPriority.Background);
    }

    private void CommitSnapshot()
    {
        _commitScheduled = false;
        _restore.Commit(WindowState == WindowState.Normal);
    }

    private void HandleWindowStateTransition(WindowState oldState, WindowState newState)
    {
        // Refresh the monitors backing the restore-state position guard — the window may
        // have been dragged to another screen before this transition.
        _restore.SetScreens(CurrentScreens());
        _restore.SetScale(RenderScaling);   // [W3-DIP-PIXELS] ...and the scale of the monitor it is on now

        // Leaving Normal: the snapshot is already kept fresh by the change handlers above.
        if (oldState == WindowState.Normal && newState != WindowState.Normal)
            return;
        // Returning to Normal: re-apply the snapshot so the dialog lands where it was.
        // DEFER the re-apply to a Background dispatcher tick (after the OS's own un-maximize
        // placement has settled) rather than fighting it synchronously mid-transition. The
        // synchronous set used to emit a maximized-origin position transient that latched
        // into the snapshot — the "second restore jumps to 0,0" bug. Background priority
        // runs FIFO after the current Win32 message burst, so it lands after any stray
        // transient and OnRestoreReapplied re-seeds the stash to the rect we just applied.
        if (newState == WindowState.Normal && oldState != WindowState.Normal)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_closed || WindowState != WindowState.Normal) return;
                if (!_restore.TryGetRestoreRect(out var pos, out var w, out var h)) return;
                // Size before position so final DPI scaling resolves against the target monitor.
                Width = w;
                Height = h;
                Position = pos;
                _restore.OnRestoreReapplied();
            }, DispatcherPriority.Background);
        }
    }
}
