#pragma once

// ============================================================
// Linie — 莉涅 (讀取魔力者 — the mage who reads an opponent's mana)
// Live ProcessEvent call profiler: per-UFunction* fire-count table,
// recorded from Stark's ProcessEvent hook during an opt-in Start/Stop
// window. Behaviour-based UFunction discovery — the user performs an
// in-game action (open shop / dash) and sees which UFunctions fired,
// ranked by count. Counting is gated by an atomic so the not-recording
// hot path pays only one relaxed load.
// ============================================================

#include <cstdint>
#include <atomic>
#include <vector>
#include <utility>

namespace Linie {

// One profiled function: how many times it fired + WHEN it first fired (its
// position in the recording's call stream, 1-based). firstSeq is the causal
// signal — an action's entry point (e.g. OpenShop) fires BEFORE the reactions
// it triggers (widget creation, On* notifications), so a smaller firstSeq among
// the diff's NEW rows ranks the true entry point above its downstream effects.
//
// CADENCE (Phase E): meanPeriodMs / cv summarise the wall-clock inter-arrival
// gaps between fires (Welford, computed from the timestamp Stark already reads —
// zero extra clock reads on the hot path). A steady TIMER callback fires at a
// regular period (low cv); Tick fires every frame (low cv too, but in the frame
// band ~5-40 ms); an input-driven function fires irregularly (high cv). The UI
// classifies "periodic" from these + gapSamples (it needs enough fires to trust
// the stats). meanPeriodMs/cv are 0 until at least one gap (2 fires) exists.
struct FuncStat {
    uintptr_t func         = 0;
    uint64_t  count        = 0;
    uint64_t  firstSeq     = 0;
    double    meanPeriodMs = 0.0;  // mean inter-arrival gap (ms); 0 with <2 fires
    double    cv           = 0.0;  // stddev/mean of the gaps; 0 with <2 gaps
    uint64_t  gapSamples   = 0;    // number of inter-arrival gaps measured (count-1)
    uint64_t  firstMs      = 0;    // wall-clock of the first and the latest fire, on the clock RecordCall gets
    uint64_t  lastMs       = 0;
    uint64_t  activeMs     = 0;    // the time it kept firing at frame cadence: the sum of its gaps up to kActiveGapMaxMs
};

// [LIVEFUNCS-HIDE-PERFRAME] A function that fires every frame through most of the recording: the per-frame noise
// pe_profile_get can leave out so it stops taking the fetch limit's rows from the low-count functions Live Funcs is
// for. A count cannot say it -- every frame for a minute is 3,600 fires at 60 fps and 8,640 at 144 -- so it is a
// cadence: a mean gap inside the frame band (the band the UI's periodic test excludes, FrameBandMaxMs) over enough
// gaps, KEPT UP for at least half of `windowMs`. Kept up is activeMs, the sum of its gaps of kActiveGapMaxMs or less,
// not the span from its first fire to its last: an action done twice six seconds apart spans the recording and
// fired for two seconds of it, and a broadcast to 80 instances in one frame adds nothing.
inline constexpr double   kPerFrameMaxMeanMs = 40.0;
inline constexpr uint64_t kPerFrameMinGaps   = 3;
inline constexpr uint64_t kActiveGapMaxMs    = 100;   // a frame at 10 fps; a longer gap is a pause, not a frame

inline bool IsPerFrame(const FuncStat& s, uint64_t windowMs) {
    if (windowMs == 0 || s.gapSamples < kPerFrameMinGaps || s.meanPeriodMs > kPerFrameMaxMeanMs) return false;
    return s.activeMs * 2 >= windowMs;
}

// Hot-path gate. Defined in Linie.cpp; declared extern so the check inlines at
// Stark's call site (one relaxed atomic load + predicted-not-taken branch when off).
extern std::atomic<bool> g_recording;
inline bool IsRecording() { return g_recording.load(std::memory_order_relaxed); }

// Record one PE fire for `ufunc` at wall-clock `nowMs` (Stark passes the same
// timestamp it already stamped for the responsiveness check — no extra clock
// read). ONLY call when IsRecording() is already true (Stark inlines that check).
// Takes the profile mutex; safe from any thread.
void RecordCall(uintptr_t ufunc, uint64_t nowMs);

// Clear the table (reserve to bound rehash churn), then flip recording on
// under the lock so there is never a half-cleared window.
void StartRecording();

// Flip recording off; the accumulated counts are retained for a later Snapshot.
void StopRecording();

// == IsRecording(). Named for readers at the pipe layer.
bool IsActive();

// Clear the table + force recording off. Called on client disconnect / shutdown
// so a stale recording never leaks across connections and the map is freed.
void Reset();

// Copy out one FuncStat per distinct function (addr / count / firstSeq / cadence / first and latest fire). Safe
// to call while recording. `activityMs` is the window the table was recorded over, from its earliest fire to its
// latest, taken under the same lock: the time the game was dispatching, not the wall clock between Start and Stop,
// which also holds the minutes a game that idles when not foreground spent behind the UI.
void Snapshot(std::vector<FuncStat>& out, uint64_t& activityMs);

} // namespace Linie
