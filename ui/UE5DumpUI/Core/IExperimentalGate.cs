using System;

namespace UE5DumpUI.Core;

/// <summary>
/// Shared opt-in gate for the experimental analysis tabs (Snapshot /
/// SPC Query / Class Pivot). A single instance is shared between
/// MainWindowViewModel (which gates tab visibility) and
/// PointerPanelViewModel (which owns the System-tab toggle checkbox), so
/// flipping it in one place updates the other via <see cref="Changed"/>.
/// The implementation persists the flag across restarts.
/// </summary>
public interface IExperimentalGate
{
    /// <summary>True when the experimental tabs should be shown.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Per-game snapshot DB size cap in MB (0 = unlimited). Persisted
    /// alongside <see cref="IsEnabled"/>; setting it does not raise
    /// <see cref="Changed"/> (the Snapshot tab owns its own quota UI).</summary>
    int SnapshotQuotaMb { get; set; }

    /// <summary>True once the user has opened, while enabled, an experimental tab
    /// that locks the opt-in (the main window's tab switch decides which).
    /// While locked the opt-in checkbox can no longer be unticked, and
    /// <see cref="IsEnabled"/> can no longer be set back to false.
    /// <b>Session-only</b> — NOT persisted; a restart clears it, so the user can
    /// untick again until they re-open such a tab.</summary>
    bool IsLocked { get; }

    /// <summary>Lock the opt-in for this session (idempotent). Raises
    /// <see cref="Changed"/> on the first call. Not persisted.</summary>
    void Lock();

    /// <summary>[EXPOFF-ROUND3-PAUSED] True from an untick until a teardown over a live connection has known what the
    /// features run in the game and turned all of it off: every reading it decided from a reply, every OFF it sent
    /// answered. An untick sets it and a tick clears it; such a teardown clears it, and one that could not tell or
    /// whose OFF failed leaves it to the next connect.
    /// Persisted with <see cref="IsEnabled"/>, so an untick made while disconnected is honoured at the next connect even
    /// across a restart -- and only that connect: with nothing owed, a connect with the features off leaves alone what a
    /// CE record may be running. Setting it does not raise <see cref="Changed"/>.</summary>
    bool TeardownOwed { get; set; }

    /// <summary>Raised whenever <see cref="IsEnabled"/> or <see cref="IsLocked"/>
    /// changes.</summary>
    event EventHandler? Changed;
}
