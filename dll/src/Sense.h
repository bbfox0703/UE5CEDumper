#pragma once

// ============================================================
// Sense — 賽恩瑟, ゼンゼ (Second-Exam proctor, "scythe" — harvest / collection)
// Diagnostics: self-health telemetry for the DLL + this process.
//
// Measures what docs/multipipe-eval.md reasons about. That doc names DLL-side
// SERIAL-DISPATCH HEAD-OF-LINE BLOCKING as the root cause of UI lag and
// game-thread CPU starvation as the CE-mailbox risk. Sense records how long each
// pipe command actually occupies the dispatcher: what that blocking costs, read
// off a running game instead of guessed.
//
// Two tiers, both cheap:
//   Tier 1 — our own health: per-command dispatch time (count / total / max /
//            last), sampled at Fern's single dispatch chokepoint.
//   Tier 2 — process facts from Win32: working set, private bytes, CPU%,
//            handle and thread counts. No UE dependency at all.
//
// Cost when nobody is looking: one mutex + a map lookup + a few adds per pipe
// command. Commands are microseconds at best, so this is noise. The Tier 2
// sample is on demand only (it walks a thread snapshot) and never runs unless a
// client asks.
// ============================================================

#include <cstdint>
#include <string>
#include <vector>

namespace Sense {

/// Aggregated dispatch cost for one pipe command name.
///
/// Times are MICROSECONDS, accumulated from QueryPerformanceCounter. Milliseconds
/// off GetTickCount64 would mislead: its ~15.6 ms granularity floors every
/// sub-tick dispatch to 0, and its max is an artefact of which calls happen to
/// straddle a tick boundary, not a measurement. Most pipe commands are far below
/// that granularity, which is exactly the population this exists to measure.
struct CommandStat {
    std::string cmd;
    uint64_t    count   = 0;   // times dispatched since the last Reset
    uint64_t    totalUs = 0;   // summed time inside DispatchCommand
    uint64_t    maxUs   = 0;   // worst single dispatch — the head-of-line spike
    uint64_t    lastUs  = 0;   // most recent, so a live panel shows movement
};

/// Win32 process facts. Zeroes on any query failure — this is diagnostics, so a
/// missing number must never be worth an error path.
struct ProcessStat {
    uint64_t workingSetBytes  = 0;
    uint64_t privateBytes     = 0;
    uint64_t peakWorkingSet   = 0;
    uint32_t handleCount      = 0;
    uint32_t threadCount      = 0;
    /// Whole-process CPU% since the PREVIOUS SampleProcess call, normalised by
    /// core count (so 100 = one machine fully busy, not one core). -1 until a
    /// second sample exists to difference against.
    double   cpuPercent       = -1.0;
};

/// Record one completed dispatch, in MICROSECONDS. Called from Fern's chokepoint;
/// safe from any thread (the two-connection lane split means two can land at once).
void RecordDispatch(const std::string& cmd, uint64_t elapsedUs);

/// QueryPerformanceCounter ticks, for bracketing a dispatch.
uint64_t NowTicks();

/// Convert a tick delta to microseconds using the cached QPC frequency.
uint64_t TicksToUs(uint64_t deltaTicks);

/// Per-command stats, heaviest TOTAL time first — total, not max, because the
/// question is which command owns the dispatcher, not which one spiked once.
/// limit == 0 returns everything.
std::vector<CommandStat> TopCommands(size_t limit);

/// Totals across every command since the last Reset.
uint64_t TotalDispatches();
uint64_t TotalBusyUs();

/// Milliseconds since the DLL started collecting — the denominator for "what
/// fraction of wall-clock was the dispatcher busy?".
uint64_t UptimeMs();

/// Drop all command stats and restart the uptime clock, so a fresh session's
/// numbers aren't polluted by the last.
void Reset();

/// Sample Tier 2 process facts. On demand only; see cpuPercent for why two
/// calls are needed before CPU is meaningful.
ProcessStat SampleProcess();

} // namespace Sense
