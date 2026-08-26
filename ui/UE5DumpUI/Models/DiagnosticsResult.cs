using System.Collections.Generic;

namespace UE5DumpUI.Models;

/// <summary>
/// Dispatch cost of one pipe command since the last diagnostics reset
/// (<c>get_diagnostics</c>). Ranked by <see cref="TotalMs"/> — the question this
/// answers is which command OWNS the dispatcher, not which one spiked once.
/// </summary>
public sealed class DiagnosticsCommandEntry
{
    public string Cmd     { get; init; } = "";
    public long   Count   { get; init; }
    /// <summary>Fractional milliseconds. The DLL times dispatches with QPC and
    /// reports microseconds scaled to ms, because most pipe commands are
    /// SUB-millisecond — an integer here would round the interesting ones to
    /// zero, which is exactly the bug the first live run exposed.</summary>
    public double TotalMs { get; init; }
    /// <summary>Worst single dispatch — the head-of-line spike a user feels as a
    /// frozen UI.</summary>
    public double MaxMs   { get; init; }
    public double LastMs  { get; init; }
    public double AvgMs   { get; init; }

    /// <summary>Share of all dispatcher-busy time owned by this command. Set by the
    /// service once the total is known, so the grid can rank by impact rather than
    /// by raw milliseconds.</summary>
    public double SharePercent { get; set; }
}

/// <summary>
/// Win32 process facts (Tier 2) — no UE dependency at all.
///
/// <para><b>These are the GAME process's numbers, not the DLL's.</b> We are
/// injected into the game, so the OS reports one process and there is no
/// supported way to attribute a working set to a single loaded module. A
/// multi-GB figure here is the game's memory; only the dispatch table is purely
/// our cost. Any UI surfacing these must say so — an unlabelled "7,453 MiB"
/// next to our diagnostics reads as ours.</para>
/// </summary>
public sealed class DiagnosticsProcess
{
    public long   WorkingSetBytes { get; init; }
    public long   PrivateBytes    { get; init; }
    public long   PeakWorkingSet  { get; init; }
    public int    HandleCount     { get; init; }
    public int    ThreadCount     { get; init; }
    /// <summary>Whole-process CPU% since the PREVIOUS sample, normalised by core
    /// count (100 = one machine fully busy). <b>-1 until a second sample exists</b>
    /// to difference against — show "—", not "0%".</summary>
    public double CpuPercent      { get; init; } = -1.0;

    public bool HasCpu => CpuPercent >= 0.0;
}

/// <summary>Game-thread health, from Stark's ProcessEvent hook.</summary>
public sealed class DiagnosticsGameThread
{
    public bool HookActive        { get; init; }
    public long HookFireCount     { get; init; }
    /// <summary>Age of the last ProcessEvent fire. <b>-1 = never fired</b>
    /// (liveness unknown) — the DLL's <c>UINT64_MAX</c> sentinel, mapped to a
    /// value that fits a signed 64-bit integer at the wire boundary. A very large
    /// positive value means the same thing and came from a pre-fix DLL.</summary>
    public long MsSinceLastFire   { get; init; } = -1;
    /// <summary>The GATE predicate, not a measurement: "unknown" counts as
    /// responsive, because with no hook installed the right answer to "should I
    /// attempt an invoke?" is yes. Kept for older DLLs; prefer
    /// <see cref="Liveness"/> for anything shown to a user.</summary>
    public bool Responsive        { get; init; }
    /// <summary>The honest three-state answer: <c>"responsive"</c>, <c>"stalled"</c>
    /// or <c>"unknown"</c> (no PE hook yet, so nothing was measured). Empty from a
    /// DLL that predates it — fall back to <see cref="Responsive"/> then.
    /// (STALLDEFAULT-2026-08-26)</summary>
    public string Liveness        { get; init; } = "";
    public int  InvokeTimeoutMs   { get; init; }

    /// <summary>True when the hook has actually fired at least once, so
    /// <see cref="MsSinceLastFire"/> is a real age worth showing.</summary>
    public bool HasFired => MsSinceLastFire >= 0 && HookFireCount > 0;
}

/// <summary>
/// One <c>get_diagnostics</c> snapshot.
///
/// <para><b>Why this exists.</b> <c>docs/multipipe-eval.md</c> names DLL-side
/// serial-dispatch head-of-line blocking as the root cause of UI lag, and
/// game-thread CPU starvation as the CE-mailbox risk — but nothing measured
/// either, so "should Phase 1 (non-blocking dispatch) be built?" was a blind
/// decision. <see cref="BusyPercent"/> plus the per-command ranking is the
/// evidence that settles it.</para>
/// </summary>
public sealed class DiagnosticsResult
{
    public long   UptimeMs        { get; init; }
    public long   TotalDispatches { get; init; }
    /// <summary>Fractional milliseconds — see <see cref="DiagnosticsCommandEntry.TotalMs"/>.</summary>
    public double TotalBusyMs     { get; init; }
    /// <summary>Fraction of wall-clock a dispatcher was occupied. High here plus a
    /// lagging UI is the case FOR non-blocking dispatch; low here says the lag is
    /// somewhere else and Phase 1 would not help.</summary>
    public double BusyPercent     { get; init; }
    public int    GObjectsCount   { get; init; }

    public DiagnosticsProcess    Process    { get; init; } = new();
    public DiagnosticsGameThread GameThread { get; init; } = new();
    public IReadOnlyList<DiagnosticsCommandEntry> Commands { get; init; }
        = new List<DiagnosticsCommandEntry>();
}
