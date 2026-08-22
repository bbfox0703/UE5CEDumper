// ============================================================
// Linie — 莉涅 (讀取魔力者 — the mage who reads an opponent's mana)
// Live ProcessEvent call profiler — implementation. See Linie.h.
//
// Data model: unordered_map<UFunction* addr, fire count> guarded by a
// DEDICATED mutex (never Stark's queue mutex — that would drag profiler
// contention into the invoke-drain critical section). Recording gated by
// a single atomic<bool> so Stark's not-recording hot path never touches
// the mutex or the map.
// ============================================================

#include "Linie.h"

#include <cmath>
#include <mutex>
#include <unordered_map>

namespace Linie {

std::atomic<bool> g_recording{false};

// Per-function accumulators. count + firstSeq drive the ranked/causal view;
// lastMs + gaps + mean + m2 drive the CADENCE view (Welford running mean/variance
// of the inter-arrival gaps, so a regularly-firing timer callback shows a low cv).
struct Stat {
    uint64_t count    = 0;
    uint64_t firstSeq = 0;
    uint64_t lastMs   = 0;   // wall-clock of the previous fire (inter-arrival base)
    uint64_t gaps     = 0;   // number of gaps measured (== count-1)
    double   mean     = 0.0; // Welford running mean of the gaps (ms)
    double   m2       = 0.0; // Welford running M2 (sum of squared deltas)
};
static std::mutex g_mu;
static std::unordered_map<uintptr_t, Stat> g_stats;
// Monotonic call-stream position (1-based), reset per recording. A function's
// firstSeq is g_seq at the moment it was first seen — the causal ordering.
static uint64_t g_seq = 0;

void RecordCall(uintptr_t ufunc, uint64_t nowMs) {
    std::lock_guard<std::mutex> lk(g_mu);
    uint64_t seq = ++g_seq;
    auto& s = g_stats[ufunc];       // default-constructs on first sight
    if (s.count == 0) {
        s.firstSeq = seq;
        s.lastMs = nowMs;
    } else if (nowMs >= s.lastMs) {
        //
        // ⚠ THE COMPARISON IS >=, NOT >. A reorder is nowMs < s.lastMs. nowMs == s.lastMs is
        // NOT a reorder — it is two fires inside the same millisecond, which is ordinary
        // (nowMs is steady_clock truncated to ms, Stark.cpp:103-107), and >= excludes the
        // underflow just as completely while keeping the sample. L5 (below) prescribed
        // "strictly greater" because it was reasoning about REORDERING only; dropping the
        // equal case was collateral. Measured 2026-08-22 on DumperTest at t.MaxFPS 60 over
        // 10.00 s: CameraModifier::BlueprintModifyCamera fires twice per frame, count=1202
        // but gap_samples=602 instead of 1201, and mean_period_ms read 16.61 ms against a
        // true 8.33 ms — because the dropped sample ALSO left s.lastMs pointing at the first
        // of the pair, so the NEXT gap spanned both fires and read as a whole frame. The
        // four once-per-frame functions in the same window were exact, which is the control:
        // a clock or window error would have moved all six together.
        //
        // The wrong number was the smaller half. Dropping the ~0 ms gaps also MANUFACTURED
        // the regularity the "Timer" badge keys on — the surviving gaps were all exactly one
        // frame apart, so cv collapsed to ~0.01 and a twice-per-frame render callback scored
        // as a textbook periodic timer, the precise distinction this cadence phase exists to
        // draw. Rig: tools/verify/linie_cadence_gap.py. [CADENCEGAP-2026-08-22]
        //
        // Inter-arrival gap since this function's previous fire — Welford update.
        // Guarded on nowMs >= lastMs: multi-thread PE can deliver two fires of the SAME
        // ufunc out of timestamp order (nowMs is stamped before the g_mu lock), and an
        // unsigned nowMs - s.lastMs would underflow to a ~1.8e19 gap that poisons the
        // Welford mean/cv for the rest of the window. Skip the reordered sample and don't
        // let it lower the base. (L5)
        double gap = static_cast<double>(nowMs - s.lastMs);
        s.gaps += 1;
        double delta = gap - s.mean;
        s.mean += delta / static_cast<double>(s.gaps);
        s.m2   += delta * (gap - s.mean);
        s.lastMs = nowMs;
    }
    ++s.count;
}

void StartRecording() {
    std::lock_guard<std::mutex> lk(g_mu);
    g_stats.clear();
    g_stats.reserve(4096);  // bound rehash churn over the recording window
    g_seq = 0;
    // Flip on UNDER the lock so a concurrent RecordCall can never observe
    // recording==true against a half-cleared table.
    g_recording.store(true, std::memory_order_relaxed);
}

void StopRecording() {
    g_recording.store(false, std::memory_order_relaxed);
}

bool IsActive() {
    return g_recording.load(std::memory_order_relaxed);
}

void Reset() {
    std::lock_guard<std::mutex> lk(g_mu);
    g_recording.store(false, std::memory_order_relaxed);
    g_stats.clear();
    g_seq = 0;
}

void Snapshot(std::vector<FuncStat>& out) {
    std::lock_guard<std::mutex> lk(g_mu);
    out.clear();
    out.reserve(g_stats.size());
    for (const auto& kv : g_stats) {
        const Stat& s = kv.second;
        double meanMs = (s.gaps > 0) ? s.mean : 0.0;
        double cv = 0.0;
        if (s.gaps > 0 && meanMs > 1e-6) {
            double variance = s.m2 / static_cast<double>(s.gaps);  // population variance
            cv = std::sqrt(variance) / meanMs;
        }
        out.push_back(FuncStat{ kv.first, s.count, s.firstSeq, meanMs, cv, s.gaps });
    }
}

} // namespace Linie
