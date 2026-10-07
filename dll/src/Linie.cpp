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

#include <Windows.h>
#include <algorithm>
#include <cmath>
#include <mutex>
#include <thread>
#include <unordered_map>
#include <unordered_set>

namespace Linie {

std::atomic<bool> g_recording{false};

// Per-function accumulators. count + firstSeq drive the ranked/causal view;
// lastMs + gaps + mean + m2 drive the CADENCE view (Welford running mean/variance
// of the inter-arrival gaps, so a regularly-firing timer callback shows a low cv).
struct Stat {
    uint64_t count    = 0;
    uint64_t firstSeq = 0;
    uint64_t firstMs  = 0;   // wall-clock of the first fire: with lastMs, the window the table covers
    uint64_t lastMs   = 0;   // wall-clock of the previous fire (inter-arrival base)
    uint64_t activeMs = 0;   // the sum of its gaps of kActiveGapMaxMs or less (IsPerFrame)
    uint64_t gaps     = 0;   // number of gaps measured (== count-1)
    double   mean     = 0.0; // Welford running mean of the gaps (ms)
    double   m2       = 0.0; // Welford running M2 (sum of squared deltas)
    FuncIdentity ident;      // read at the first call (TRACE-UNLOADED-NAMES)
    uint8_t  identTries = 0; // reads tried so far, up to kIdentityTries
    ArmHint  arm;            // what this address is armed for ([LIVEFUNCS-STEP2]); default: nothing
    int32_t  armSpec = -1;   // the followed name it last matched, so an address counts once per name
};
static std::mutex g_mu;
static std::unordered_map<uintptr_t, Stat> g_stats;
// The identity reader of the running recording, and its key check; nullptr reads nothing. Set and cleared under g_mu.
static FuncIdentityReader g_reader = nullptr;
static FuncKeyReader      g_keyReader = nullptr;
// The names the running recording follows; nullptr follows none. Set and cleared under g_mu.
static std::shared_ptr<ArmState> g_arms;

std::shared_ptr<ArmState> BuildArmState(std::vector<ArmSpec> specs, size_t logCapacity) {
    std::sort(specs.begin(), specs.end(), [](const ArmSpec& a, const ArmSpec& b) { return a.key < b.key; });
    auto st = std::make_shared<ArmState>();
    for (const ArmSpec& sp : specs) {
        if (!st->specs.empty() && st->specs.back().key == sp.key) {
            ArmSpec& m = st->specs.back();   // the same name ticked and chosen: one spec does both
            m.tick = m.tick || sp.tick;
            if (sp.ring >= 0) { m.ring = sp.ring; m.ringCap = sp.ringCap; }
            continue;
        }
        st->specs.push_back(sp);
    }
    st->counts.assign(st->specs.size(), ArmState::Count{});
    st->capacity = logCapacity;
    st->log.reserve(logCapacity);
    st->layouts.resize(logCapacity);
    return st;
}
// Monotonic call-stream position (1-based), reset per recording. A function's
// firstSeq is g_seq at the moment it was first seen — the causal ordering.
static uint64_t g_seq = 0;

// [LIVEFUNCS-STEP2] What `s`'s address is armed for, from the identity just read: a name the recording follows arms
// it -- to open a scope when ticked, with a new arm and its ring when chosen. Under g_mu; the log never grows past
// what BuildArmState reserved, so the hook never allocates for it.
static void ArmLocked(Stat& s, uintptr_t ufunc, uint64_t nowMs) {
    s.arm = ArmHint{};
    if (!g_arms || !s.ident.captured) return;
    const NameKey key{ s.ident.nameIndex, s.ident.nameNumber, s.ident.classIndex, s.ident.classNumber };
    const auto& specs = g_arms->specs;
    const auto it = std::lower_bound(specs.begin(), specs.end(), key,
                                     [](const ArmSpec& a, const NameKey& k) { return a.key < k; });
    if (it == specs.end() || !(it->key == key)) return;
    const size_t si = static_cast<size_t>(it - specs.begin());
    ArmState::Count& count = g_arms->counts[si];
    if (s.armSpec != static_cast<int32_t>(si)) {   // a new address for this name, not a reload at the same one
        ++count.addresses;
        s.armSpec = static_cast<int32_t>(si);
    }
    s.arm.gen = g_arms->gen;
    if (it->tick) s.arm.flags |= kArmTick;
    if (it->ring < 0) return;
    if (g_arms->stopped.load(std::memory_order_relaxed)) return;   // no call is traced any more: nothing to copy
    if (g_arms->log.size() >= g_arms->capacity) { ++count.armsFull; return; }
    ++count.arms;
    g_arms->log.push_back(ArmRecord{ ufunc, s.ident, static_cast<uint32_t>(si), it->ring, nowMs });
    g_arms->logCount.store(g_arms->log.size(), std::memory_order_release);
    s.arm.ring = it->ring;
    s.arm.arm  = static_cast<uint32_t>(g_arms->log.size() - 1);
    s.arm.copy = static_cast<uint16_t>(ArmCopyBytes(s.ident.parmsSize, s.ident.functionFlags, it->ringCap));
    if (ArmTakesAfter(s.ident.functionFlags)) s.arm.flags |= kArmAfter;
    if (ArmTruncated(s.ident.parmsSize, it->ringCap)) s.arm.flags |= kArmTruncated;
}

void RecordCall(uintptr_t ufunc, uint64_t nowMs, ArmHint* hint) {
    std::lock_guard<std::mutex> lk(g_mu);
    if (hint) *hint = ArmHint{};
    bool keyOk = true;   // this call's key check passed, or none ran
    uint64_t seq = ++g_seq;
    auto& s = g_stats[ufunc];       // default-constructs on first sight
    // [TRACE-UNLOADED-NAMES] The function is being dispatched, so it is alive: read what it is now, once. A later
    // call costs one test of `captured` (or of the try count, for a function whose reads keep failing).
    if (g_reader && !s.ident.captured && s.identTries < kIdentityTries) {
        ++s.identTries;
        FuncIdentity id{};
        if (g_reader(ufunc, id)) {
            id.captured = true;
            s.ident = id;
            ArmLocked(s, ufunc, nowMs);
        }
    } else if (g_reader && g_keyReader && s.ident.captured) {
        // Review DLL-3: the table is keyed by address, and a freed function's address can be taken by another that
        // fires in the same recording. Its key read now against the one read: a change is read again in full; the
        // same names under a new class object are the same function, its class reloaded; other names are another
        // function, and the entry says so from then on.
        int32_t idx = 0, num = 0;
        uint64_t outer = 0;
        if (!g_keyReader(ufunc, idx, num, outer)) {
            keyOk = false;   // [LIVEFUNCS-STEP2] unverified: this call is armed for nothing
        } else {
            bool changed = idx != s.ident.nameIndex || num != s.ident.nameNumber || outer != s.ident.outer;
            // [LIVEFUNCS-STEP2] An armed address's class is checked by its FName too; an unarmed one pays nothing.
            if (!changed && s.arm.gen != 0 && g_arms && g_arms->classNameReader) {
                int32_t ci = 0, cn = 0;
                if (!g_arms->classNameReader(outer, ci, cn)) keyOk = false;
                else changed = ci != s.ident.classIndex || cn != s.ident.classNumber;
            }
            if (changed) {
                FuncIdentity id{};
                if (g_reader(ufunc, id)) {
                    id.captured = true;
                    id.reused = s.ident.reused
                        || id.nameIndex != s.ident.nameIndex || id.nameNumber != s.ident.nameNumber
                        || id.classIndex != s.ident.classIndex || id.classNumber != s.ident.classNumber;
                    s.ident = id;
                    ArmLocked(s, ufunc, nowMs);   // what it is now: another name disarms, a reload is a new arm
                } else {
                    s.arm = ArmHint{};            // changed and unreadable: armed for nothing until read again
                }
            }
        }
    }
    if (s.count == 0) {
        s.firstSeq = seq;
        s.firstMs = nowMs;
        s.lastMs = nowMs;
    } else if (nowMs >= s.lastMs) {
        //
        // ⚠ THE COMPARISON IS >=, NOT >. A reorder is nowMs < s.lastMs. nowMs == s.lastMs is
        // NOT a reorder — it is two fires inside the same millisecond, which is ordinary
        // (nowMs is steady_clock truncated to ms, Stark.cpp `InvokeRequest`), and >= excludes the
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
        const uint64_t gapMs = nowMs - s.lastMs;
        if (gapMs <= kActiveGapMaxMs) s.activeMs += gapMs;   // still firing at frame cadence
        double gap = static_cast<double>(gapMs);
        s.gaps += 1;
        double delta = gap - s.mean;
        s.mean += delta / static_cast<double>(s.gaps);
        s.m2   += delta * (gap - s.mean);
        s.lastMs = nowMs;
    }
    ++s.count;
    if (hint) *hint = keyOk ? s.arm : ArmHint{};
}

void StartRecording(FuncIdentityReader reader, FuncKeyReader keyReader, std::shared_ptr<ArmState> arms) {
    std::shared_ptr<ArmState> drop;   // the last recording's, destroyed after the lock is left
    std::lock_guard<std::mutex> lk(g_mu);
    g_stats.clear();
    g_stats.reserve(4096);  // bound rehash churn over the recording window
    g_seq = 0;
    g_reader = reader;
    g_keyReader = keyReader;
    drop = std::move(g_arms);
    g_arms = std::move(arms);
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
    std::shared_ptr<ArmState> drop;   // destroyed after the table's lock is left: hooks may be waiting on it
    {
        std::lock_guard<std::mutex> lk(g_mu);
        g_recording.store(false, std::memory_order_relaxed);
        g_stats.clear();
        g_seq = 0;
        g_reader = nullptr;
        g_keyReader = nullptr;
        drop = std::move(g_arms);
    }
    // A client that left takes its trace with it: up to 512 MB of the game's memory, outside g_mu because the
    // trace has its own lock.
    FreeTrace();
}

void Snapshot(std::vector<FuncStat>& out, uint64_t& activityMs) {
    std::lock_guard<std::mutex> lk(g_mu);
    out.clear();
    out.reserve(g_stats.size());
    uint64_t earliest = UINT64_MAX, latest = 0;
    for (const auto& kv : g_stats) {
        const Stat& s = kv.second;
        double meanMs = (s.gaps > 0) ? s.mean : 0.0;
        double cv = 0.0;
        if (s.gaps > 0 && meanMs > 1e-6) {
            double variance = s.m2 / static_cast<double>(s.gaps);  // population variance
            cv = std::sqrt(variance) / meanMs;
        }
        out.push_back(FuncStat{ kv.first, s.count, s.firstSeq, meanMs, cv, s.gaps, s.firstMs, s.lastMs, s.activeMs,
                                s.ident });
        if (s.firstMs < earliest) earliest = s.firstMs;
        if (s.lastMs > latest) latest = s.lastMs;
    }
    activityMs = (latest > earliest && earliest != UINT64_MAX) ? latest - earliest : 0;
}

// The arms' handover to the background read. The log is the table's, under g_mu; the layouts are the reader's, under
// layoutMu; no function here holds both, and the hook takes neither of the second.
std::vector<PendingArm> TakePendingArms(ArmState& st, size_t max) {
    std::lock_guard<std::mutex> lk(g_mu);
    std::vector<PendingArm> out;
    const size_t end = std::min(st.log.size(), st.taken + max);
    for (size_t i = st.taken; i < end; ++i) out.push_back(PendingArm{ static_cast<uint32_t>(i), st.log[i] });
    st.taken = end;
    return out;
}

bool PublishArmLayout(ArmState& st, uint32_t index, ArmLayoutState state, std::shared_ptr<const void> layout,
                      std::string why, uint64_t readMs) {
    std::lock_guard<std::mutex> lk(st.layoutMu);
    if (index >= st.logCount.load(std::memory_order_acquire) || index >= st.layouts.size()) return false;
    ArmState::Layout& l = st.layouts[index];
    if (l.state != static_cast<uint8_t>(ArmLayoutState::Pending)) return false;
    l.state  = static_cast<uint8_t>(state);
    l.layout = std::move(layout);
    l.why    = std::move(why);
    l.readMs = readMs;
    return true;
}

void SealArms(ArmState& st) {
    std::lock_guard<std::mutex> lk(st.layoutMu);
    for (auto& l : st.layouts)   // every place, so an arm made after this is sealed too: a publish needs Pending
        if (l.state == static_cast<uint8_t>(ArmLayoutState::Pending))
            l.state = static_cast<uint8_t>(ArmLayoutState::NotReadBeforeStop);
}

std::vector<ArmView> CopyArms(ArmState& st) {
    std::vector<ArmRecord> recs;
    {
        std::lock_guard<std::mutex> lk(g_mu);
        recs = st.log;
    }
    std::vector<ArmView> out;
    out.reserve(recs.size());
    std::lock_guard<std::mutex> lk(st.layoutMu);
    for (size_t i = 0; i < recs.size() && i < st.layouts.size(); ++i) {
        const ArmState::Layout& l = st.layouts[i];
        out.push_back(ArmView{ static_cast<uint32_t>(i), recs[i], static_cast<ArmLayoutState>(l.state), l.layout,
                               l.why, l.readMs });
    }
    return out;
}

std::vector<ArmSummary> ArmsSummary() {
    std::lock_guard<std::mutex> lk(g_mu);
    std::vector<ArmSummary> out;
    if (!g_arms) return out;
    out.reserve(g_arms->specs.size());
    for (size_t i = 0; i < g_arms->specs.size(); ++i) {
        const ArmSpec& sp = g_arms->specs[i];
        const ArmState::Count& c = g_arms->counts[i];
        out.push_back(ArmSummary{ sp.key, sp.tick, sp.ring, c.addresses, c.arms, c.armsFull });
    }
    return out;
}

void IdentitiesOf(const std::vector<uintptr_t>& addrs, std::vector<FuncIdentity>& out) {
    std::lock_guard<std::mutex> lk(g_mu);
    out.assign(addrs.size(), FuncIdentity{});
    for (size_t i = 0; i < addrs.size(); ++i) {
        auto it = g_stats.find(addrs[i]);
        if (it != g_stats.end()) out[i] = it->second.ident;
    }
}

std::vector<uintptr_t> PerFrameFuncs() {
    std::vector<FuncStat> snap;
    uint64_t windowMs = 0;
    Snapshot(snap, windowMs);
    std::vector<uintptr_t> out;
    for (const auto& s : snap)
        if (IsPerFrame(s, windowMs)) out.push_back(s.func);
    return out;
}

// ---- [LIVEFUNCS-TIMELINE-2026-10-04] the call trace ----
//
// Lifetime (TR2). The hook never holds a pointer into the ring across the game's own ProcessEvent: every write sits
// inside a short section bracketed by g_inflight, and checks g_tracing INSIDE it. StopTrace clears g_tracing, then
// waits for g_inflight to reach 0. Both sides use seq_cst on the counter and the flag, so either the hook sees the
// flag cleared and touches nothing, or Stop sees the hook inside and waits for it -- after Stop returns no write
// lands in the ring, which is what lets Copy read it and Free release it. Every entry point a pipe thread calls takes
// g_traceMu; the hook never does.
//
// When a wait gives up (a hook stayed inside past kQuiesceTimeoutMs), nothing the hook reads may change until a later
// wait reaches zero: the ring, its capacity, the sets. A Free then leaves it all, and a Start is refused as Busy.

std::atomic<bool> g_tracing{false};

namespace {

// [LIVEFUNCS-STEP2] One snapshot ring: a run of fixed slots inside the trace's snapshot block, its own write index, on
// its own cache line so two busy choices do not share one.
struct alignas(64) SnapRing {
    uint8_t* base = nullptr;   // the ring's first slot, 64-aligned
    uint32_t cap  = 0;         // slot payload
    uint32_t slot = 0;         // kSnapHeaderBytes + cap
    std::atomic<uint64_t> next{ 0 };
    std::atomic<uint64_t> window{ 0 };          // the budget: the second in the high 40 bits, its count in the low 24
    std::atomic<uint64_t> skippedBudget{ 0 };
    std::atomic<uint64_t> droppedBudget{ 0 };
};

struct TraceState {
    TraceRecord* buf      = nullptr;
    uint64_t     bytes    = 0;
    uint64_t     cap      = 0;
    uint64_t     gen      = 0;
    bool         quiesced = true;
    std::atomic<uint64_t> next{0};
    AddrSet ticked;
    AddrSet exclude;
    bool distinctReady = false;
    std::vector<uintptr_t> distinctFuncs;
    std::vector<uintptr_t> distinctObjs;
    std::vector<FuncIdentity> distinctIdents;   // parallel to distinctFuncs (TRACE-UNLOADED-NAMES)
    std::shared_ptr<ArmState> arms;             // [LIVEFUNCS-STEP2] the names its recording follows
    bool   scoped      = false;                 // fixed at Start from what was asked (TraceConfig::scoped)
    size_t tickedNames = 0;
    bool   snapOnly    = false;
    uint8_t* snapBlock = nullptr;               // the rings' one allocation
    uint64_t snapBytes = 0;
    uint64_t snapK     = 0;                     // slots per ring
    uint32_t snapCount = 0;
    std::unique_ptr<SnapRing[]> snapRings;
    BytesCopier copier = nullptr;
    uint32_t snapPerRing = 0, snapTotal = 0;
    std::atomic<uint64_t> snapTotalWindow{ 0 };
};

TraceState            g_trace;
std::atomic<uint32_t> g_inflight{0};
std::mutex            g_traceMu;
uint64_t              g_traceGen = 0;

// The ticked scope of THIS thread: the stack address of the call that opened it, and the recording it belongs to.
thread_local uintptr_t t_scopeSp  = 0;
thread_local uint64_t  t_scopeGen = 0;

uint64_t QpcNow() {
    LARGE_INTEGER t;
    QueryPerformanceCounter(&t);
    return static_cast<uint64_t>(t.QuadPart);
}
uint64_t (*g_clock)() = QpcNow;

uint64_t QpcFreq() {
    static const uint64_t f = [] { LARGE_INTEGER q{}; QueryPerformanceFrequency(&q); return static_cast<uint64_t>(q.QuadPart); }();
    return f;
}

// How long StopTrace waits for a hook to leave its write. A write is a few stores; a hook still inside after this
// was suspended mid-write, and the ring is then neither read nor freed (a leak beats a use-after-free in the game).
constexpr uint64_t kQuiesceTimeoutMs = 2000;

// The in-flight count around one hook write. A destructor, so a fault inside the write (a C++ exception, or an SEH
// fault unwound under the DLL's /EHa) still takes the count down: a count left up would make every later Stop give
// up and every later ring unreadable.
struct InflightGuard {
    InflightGuard()  { g_inflight.fetch_add(1, std::memory_order_seq_cst); }
    ~InflightGuard() { g_inflight.fetch_sub(1, std::memory_order_release); }
    InflightGuard(const InflightGuard&) = delete;
    InflightGuard& operator=(const InflightGuard&) = delete;
};

// True when no hook is inside a write. A wait that reaches zero clears an earlier give-up: with the flag off and no
// hook inside, nothing can touch the ring again.
bool StopTraceLocked() {
    g_tracing.store(false, std::memory_order_seq_cst);
    if (g_trace.arms) g_trace.arms->stopped.store(true, std::memory_order_relaxed);
    const uint64_t deadline = GetTickCount64() + kQuiesceTimeoutMs;
    while (g_inflight.load(std::memory_order_seq_cst) != 0) {
        if (GetTickCount64() > deadline) { g_trace.quiesced = false; return false; }
        std::this_thread::yield();
    }
    g_trace.quiesced = true;
    return true;
}

// False, changing nothing, while a hook may still be inside (see the lifetime note above).
bool FreeTraceLocked() {
    if (!StopTraceLocked()) return false;
    if (g_trace.buf) VirtualFree(g_trace.buf, 0, MEM_RELEASE);
    g_trace.buf = nullptr;
    g_trace.bytes = g_trace.cap = 0;
    g_trace.next.store(0, std::memory_order_relaxed);
    g_trace.ticked.Build({});
    g_trace.exclude.Build({});
    g_trace.scoped = g_trace.snapOnly = false;
    g_trace.tickedNames = 0;
    if (g_trace.snapBlock) VirtualFree(g_trace.snapBlock, 0, MEM_RELEASE);
    g_trace.snapBlock = nullptr;
    g_trace.snapBytes = g_trace.snapK = 0;
    g_trace.snapCount = 0;
    g_trace.snapRings.reset();
    g_trace.distinctReady = false;
    std::vector<uintptr_t>().swap(g_trace.distinctFuncs);
    std::vector<uintptr_t>().swap(g_trace.distinctObjs);
    std::vector<FuncIdentity>().swap(g_trace.distinctIdents);
    // [LIVEFUNCS-STEP2] The names go with the trace: its own reference, and the recording's when it is the same one.
    // Both are destroyed when this returns, outside the table's lock, which game threads may be waiting on.
    std::shared_ptr<ArmState> dropTrace = std::move(g_trace.arms), dropRecording;
    if (dropTrace) {
        std::lock_guard<std::mutex> lk(g_mu);   // g_traceMu -> g_mu, the one nesting order
        if (g_arms == dropTrace) dropRecording = std::move(g_arms);
    }
    return true;
}

// [LIVEFUNCS-STEP2] One budget word: the second in the high 40 bits, the calls admitted in it in the low 24. A newer
// second starts again at one; the same or an older second (threads stamp out of order) counts up to the cap. The
// first calls of each second are the ones kept.
bool AdmitWord(std::atomic<uint64_t>& w, uint64_t sec, uint32_t cap) {
    uint64_t cur = w.load(std::memory_order_relaxed);
    for (;;) {
        uint64_t nxt;
        if (sec > (cur >> 24)) nxt = (sec << 24) | 1;
        else if ((cur & 0xFFFFFFull) < cap) nxt = cur + 1;
        else return false;
        if (w.compare_exchange_weak(cur, nxt, std::memory_order_relaxed)) return true;
    }
}

// The ring's budget, then the total. A call the total refuses has used one of its ring's places: approximate at the
// boundary, which a guard may be.
bool SnapAdmit(int32_t ring, uint64_t ticks) {
    const uint64_t sec = ticks / QpcFreq();
    return AdmitWord(g_trace.snapRings[ring].window, sec, g_trace.snapPerRing) &&
           AdmitWord(g_trace.snapTotalWindow, sec, g_trace.snapTotal);
}

// [LIVEFUNCS-STEP2] One slot of ring `ring`, inside the hook's in-flight section: the header, the copy, the slot's
// number last -- a reader keeps a slot only when its number is the one it expects. Never a write-back: the copy after
// the call takes a slot of its own (TR3).
void SnapWrite(int32_t ring, uint64_t entrySeq, bool after, uintptr_t params, const ArmHint& hint) {
    SnapRing& r = g_trace.snapRings[ring];
    const uint64_t k = r.next.fetch_add(1, std::memory_order_relaxed);
    uint8_t* slot = r.base + (k % g_trace.snapK) * r.slot;
    auto* hdr = reinterpret_cast<SnapSlotHeader*>(slot);
    hdr->entrySeq = entrySeq;
    hdr->arm = hint.arm;
    uint32_t n = hint.copy < r.cap ? hint.copy : r.cap;
    uint16_t flags = (hint.flags & kArmTruncated) ? kSnapTruncated : 0;
    if (params == 0) { flags |= kSnapNullParams; n = 0; }
    else if (n != 0 && !(g_trace.copier && g_trace.copier(params, slot + sizeof(SnapSlotHeader), n))) {
        flags |= kSnapCopyFault;
        n = 0;
    }
    hdr->len = static_cast<uint16_t>(n);
    hdr->flags = flags;
    hdr->seqKind = k | (after ? kSnapAfterBit : 0);
}

uint64_t FirstValidLocked() {
    const uint64_t w = g_trace.next.load(std::memory_order_relaxed);
    return w > g_trace.cap ? w - g_trace.cap : 0;
}

uint64_t HashAddr(uintptr_t a) {
    uint64_t h = static_cast<uint64_t>(a) * 0x9E3779B97F4A7C15ull;
    return h ^ (h >> 29);
}

}  // namespace

void AddrSet::Build(const std::vector<uintptr_t>& addrs) {
    size_t want = 8;
    while (want < addrs.size() * 2) want <<= 1;
    slots_.assign(addrs.empty() ? 0 : want, 0);
    mask_  = addrs.empty() ? 0 : want - 1;
    count_ = 0;
    for (uintptr_t a : addrs) {
        if (a == 0) continue;
        uint64_t i = HashAddr(a) & mask_;
        while (slots_[i] != 0 && slots_[i] != a) i = (i + 1) & mask_;
        if (slots_[i] == 0) { slots_[i] = a; ++count_; }
    }
}

bool AddrSet::Contains(uintptr_t addr) const {
    if (count_ == 0 || addr == 0) return false;
    uint64_t i = HashAddr(addr) & mask_;
    for (;;) {
        const uintptr_t s = slots_[i];
        if (s == addr) return true;
        if (s == 0) return false;
        i = (i + 1) & mask_;
    }
}

TraceStartStatus StartTrace(const TraceConfig& cfg) {
    std::lock_guard<std::mutex> lk(g_traceMu);
    if (!FreeTraceLocked()) return TraceStartStatus::Busy;
    const uint64_t cap = cfg.bytes / sizeof(TraceRecord);
    if (cap < 2) return TraceStartStatus::TooSmall;
    const SIZE_T size = static_cast<SIZE_T>(cap * sizeof(TraceRecord));
    void* p = VirtualAlloc(nullptr, size, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (!p) return TraceStartStatus::NoMemory;
    // Touch every page here, on the pipe thread: committed pages are demand-zero, and the first lap of the ring
    // would otherwise take one page fault per 4 KB on the game thread.
    SYSTEM_INFO si{};
    GetSystemInfo(&si);
    const size_t page = si.dwPageSize ? si.dwPageSize : 4096;
    for (size_t off = 0; off < size; off += page) static_cast<volatile uint8_t*>(p)[off] = 0;

    // [LIVEFUNCS-STEP2] The snapshot rings: K slots each, K the same for every ring, each ring 64-aligned (the 64 per
    // ring the formula sets aside). A buffer that cannot keep kSnapMinSlots calls a ring refuses the Start, the ring
    // just made freed with it: the trace never records without what was asked (T1).
    uint8_t* snapBlock = nullptr;
    uint64_t snapSize = 0, snapK = 0;
    std::unique_ptr<SnapRing[]> snapRings;
    const uint64_t nRings = cfg.snapRingCaps.size();
    if (nRings != 0) {
        uint64_t perCall = 0;
        for (uint32_t c : cfg.snapRingCaps) perCall += kSnapHeaderBytes + ((static_cast<uint64_t>(c) + 7) & ~7ull);
        if (cfg.snapBytes <= 64 * nRings || (snapK = (cfg.snapBytes - 64 * nRings) / perCall) < kSnapMinSlots) {
            VirtualFree(p, 0, MEM_RELEASE);
            return TraceStartStatus::SnapTooSmall;
        }
        snapRings = std::make_unique<SnapRing[]>(static_cast<size_t>(nRings));
        for (uint64_t r = 0; r < nRings; ++r) {
            snapRings[r].cap  = (cfg.snapRingCaps[r] + 7u) & ~7u;
            snapRings[r].slot = kSnapHeaderBytes + snapRings[r].cap;
            snapSize += (snapK * snapRings[r].slot + 63) & ~63ull;
        }
        snapBlock = static_cast<uint8_t*>(VirtualAlloc(nullptr, static_cast<SIZE_T>(snapSize), MEM_RESERVE | MEM_COMMIT,
                                                       PAGE_READWRITE));
        if (!snapBlock) {
            VirtualFree(p, 0, MEM_RELEASE);
            return TraceStartStatus::SnapNoMemory;
        }
        for (uint64_t off = 0; off < snapSize; off += page) static_cast<volatile uint8_t*>(snapBlock)[off] = 0;
        uint64_t at = 0;
        for (uint64_t r = 0; r < nRings; ++r) {
            snapRings[r].base = snapBlock + at;
            at += (snapK * snapRings[r].slot + 63) & ~63ull;
        }
    }
    g_trace.snapBlock = snapBlock;
    g_trace.snapBytes = snapSize;
    g_trace.snapK     = snapK;
    g_trace.snapCount = static_cast<uint32_t>(nRings);
    g_trace.snapRings = std::move(snapRings);
    g_trace.copier    = cfg.copier;
    // The budget's words hold a count in 24 bits; a cap of 0 would admit nothing ever.
    g_trace.snapPerRing = std::clamp<uint32_t>(cfg.snapPerRingPerSec, 1, 0xFFFFFF);
    g_trace.snapTotal   = std::clamp<uint32_t>(cfg.snapTotalPerSec, 1, 0xFFFFFF);
    g_trace.snapTotalWindow.store(0, std::memory_order_relaxed);

    g_trace.buf   = static_cast<TraceRecord*>(p);
    g_trace.bytes = size;
    g_trace.cap   = cap;
    g_trace.gen   = ++g_traceGen;
    g_trace.next.store(0, std::memory_order_relaxed);
    g_trace.ticked.Build(cfg.ticked);
    g_trace.exclude.Build(cfg.exclude);
    g_trace.distinctReady = false;
    g_trace.arms = cfg.arms;
    if (g_trace.arms) g_trace.arms->gen = g_trace.gen;   // before any hook can read a hint of this recording
    g_trace.scoped      = cfg.scoped || !cfg.ticked.empty();
    g_trace.tickedNames = cfg.tickedNames;
    g_trace.snapOnly    = cfg.snapOnly;
    // Publishes everything above to a hook that reads the flag inside its section (seq_cst is also a release).
    g_tracing.store(true, std::memory_order_seq_cst);
    return TraceStartStatus::Ok;
}

void StopTrace() {
    std::lock_guard<std::mutex> lk(g_traceMu);
    StopTraceLocked();
}

void FreeTrace() {
    std::lock_guard<std::mutex> lk(g_traceMu);
    FreeTraceLocked();
}

void TraceEnter(uintptr_t ufunc, uintptr_t obj, uintptr_t sp, uint32_t tid, TraceToken& tok, uintptr_t params,
                const ArmHint& hint) {
    tok = TraceToken{};
    InflightGuard inflight;
    if (!g_tracing.load(std::memory_order_seq_cst)) return;
    const uint64_t gen = g_trace.gen;
    // [LIVEFUNCS-STEP2] The table's read of this same call armed it, when the hint is this recording's (gens start at
    // 1, so a default hint never is); its ring only when this trace has it.
    const bool named = hint.gen == gen;
    const int32_t ring = (named && hint.ring >= 0 && static_cast<uint32_t>(hint.ring) < g_trace.snapCount) ? hint.ring : -1;
    bool open = false, lone = false, excluded = false;
    if (g_trace.scoped) {
        if (t_scopeGen != gen) {
            t_scopeSp = 0;          // a scope left open by an earlier recording is not this one's
            t_scopeGen = gen;
        } else if (t_scopeSp != 0 && sp >= t_scopeSp) {
            t_scopeSp = 0;          // called from at or above the root's frame: the root is gone (unwound)
        }
        if (t_scopeSp == 0) {
            if ((named && (hint.flags & kArmTick)) || g_trace.ticked.Contains(ufunc)) {
                t_scopeSp = sp;
                open = true;
            } else if (ring >= 0) {
                lone = true;        // [LIVEFUNCS-STEP2] T11: alone, for its parameters; it opens no scope
            } else {
                return;
            }
        }
    }
    if (!open && !lone && g_trace.exclude.Contains(ufunc)) {
        if (ring < 0) return;
        excluded = true;            // [LIVEFUNCS-STEP2] kept for its parameters; the scope around it stays as it was
    }
    const uint64_t ticks = g_clock();   // one read: the record's time and the budget's second
    // [LIVEFUNCS-STEP2] The budget. Over it, a call the scope records anyway keeps its record, flagged; one recorded
    // only for its parameters (lone, or kept from the exclusion) writes nothing -- no record only to say "skipped".
    bool taken = false;
    if (ring >= 0) {
        taken = SnapAdmit(ring, ticks);
        if (!taken) {
            if (lone || excluded) {
                g_trace.snapRings[ring].droppedBudget.fetch_add(1, std::memory_order_relaxed);
                return;
            }
            g_trace.snapRings[ring].skippedBudget.fetch_add(1, std::memory_order_relaxed);
        }
    }
    const uint64_t seq = g_trace.next.fetch_add(1, std::memory_order_relaxed);
    TraceRecord& r = g_trace.buf[seq % g_trace.cap];
    r.seqKind = seq;
    r.ticks   = ticks;
    r.a       = ufunc;
    r.b       = obj;
    r.tid     = tid;
    if (taken) SnapWrite(ring, seq, false, params, hint);
    r.flags   = (open ? kTraceScopeRoot : 0) | (taken ? kTraceSnapTaken : 0) | (lone ? kTraceSnapLone : 0) |
                (excluded ? kTraceSnapExcluded : 0) | ((ring >= 0 && !taken) ? kTraceSnapBudget : 0);
    tok.entrySeq = seq;
    tok.gen      = gen;
    tok.traced   = true;
    tok.opened   = open;
    if (taken && (hint.flags & kArmAfter)) {
        tok.after = hint;
        tok.after.ring = ring;
    }
}

void TraceReturn(const TraceToken& tok, uint32_t tid, uintptr_t params) {
    // Close this thread's scope first: it is thread state, not ring state, and must close even when nothing more
    // is written (the trace stopped while the root ran).
    if (tok.opened && t_scopeGen == tok.gen) t_scopeSp = 0;
    if (!tok.traced) return;
    InflightGuard inflight;
    if (!g_tracing.load(std::memory_order_seq_cst) || g_trace.gen != tok.gen) return;
    const uint64_t seq = g_trace.next.fetch_add(1, std::memory_order_relaxed);
    TraceRecord& r = g_trace.buf[seq % g_trace.cap];
    r.seqKind = seq | kTraceReturnBit;
    r.ticks   = g_clock();
    r.a       = tok.entrySeq;
    r.b       = 0;
    r.tid     = tid;
    r.flags   = 0;
    // [LIVEFUNCS-STEP2] The out parameters and the return value: a slot of its own, under the same gen check -- the
    // token's ring index means nothing to another recording.
    if (tok.after.ring >= 0) SnapWrite(tok.after.ring, tok.entrySeq, true, params, tok.after);
}

namespace {
TraceInfo InfoLocked() {
    TraceInfo i;
    i.allocated  = g_trace.buf != nullptr;
    i.tracing    = g_tracing.load(std::memory_order_seq_cst);
    i.quiesced   = g_trace.quiesced;
    i.bytes      = g_trace.bytes;
    i.capacity   = g_trace.cap;
    i.written    = g_trace.next.load(std::memory_order_relaxed);
    i.firstValid = FirstValidLocked();
    i.gen        = g_trace.gen;
    i.qpcFreq    = QpcFreq();
    i.ticked     = g_trace.ticked.Size();
    i.excluded   = g_trace.exclude.Size();
    i.scoped     = g_trace.scoped;
    i.tickedNames = g_trace.tickedNames;
    i.snapOnly   = g_trace.snapOnly;
    i.snap.allocated    = g_trace.snapBlock != nullptr;
    i.snap.bytes        = g_trace.snapBytes;
    i.snap.slotsPerRing = g_trace.snapK;
    i.snap.rings        = g_trace.snapCount;
    if (g_trace.snapBlock) {
        i.snap.perRingPerSec = g_trace.snapPerRing;
        i.snap.totalPerSec   = g_trace.snapTotal;
        for (uint32_t r = 0; r < g_trace.snapCount; ++r) {
            i.snap.skippedBudget += g_trace.snapRings[r].skippedBudget.load(std::memory_order_relaxed);
            i.snap.droppedBudget += g_trace.snapRings[r].droppedBudget.load(std::memory_order_relaxed);
        }
    }
    return i;
}
}  // namespace

TraceInfo GetTraceInfo() {
    std::lock_guard<std::mutex> lk(g_traceMu);
    return InfoLocked();
}

bool CopySnaps(uint32_t ring, uint64_t from, size_t maxSlots, std::vector<SnapCopy>& out, uint64_t* next) {
    std::lock_guard<std::mutex> lk(g_traceMu);
    if (!g_trace.snapBlock || ring >= g_trace.snapCount || g_tracing.load(std::memory_order_seq_cst) ||
        !g_trace.quiesced)
        return false;
    const SnapRing& r = g_trace.snapRings[ring];
    const uint64_t w = r.next.load(std::memory_order_relaxed);
    const uint64_t first = w > g_trace.snapK ? w - g_trace.snapK : 0;
    const uint64_t begin = from > first ? from : first;
    if (next) *next = begin;
    if (begin >= w) return true;
    const uint64_t want = (static_cast<uint64_t>(maxSlots) > UINT64_MAX - from) ? UINT64_MAX : from + maxSlots;
    const uint64_t end = want < w ? want : w;
    if (end <= begin) return true;
    if (next) *next = end;
    for (uint64_t k = begin; k < end; ++k) {
        const uint8_t* slot = r.base + (k % g_trace.snapK) * r.slot;
        const auto* hdr = reinterpret_cast<const SnapSlotHeader*>(slot);
        if ((hdr->seqKind & ~kSnapAfterBit) != k) continue;   // written out of turn when the ring lapped mid-write
        SnapCopy c;
        c.index    = k;
        c.entrySeq = hdr->entrySeq;
        c.after    = (hdr->seqKind & kSnapAfterBit) != 0;
        c.len      = hdr->len;
        c.flags    = hdr->flags;
        c.arm      = hdr->arm;
        const uint16_t n = hdr->len <= r.cap ? hdr->len : static_cast<uint16_t>(r.cap);
        c.bytes.assign(slot + sizeof(SnapSlotHeader), slot + sizeof(SnapSlotHeader) + n);
        out.push_back(std::move(c));
    }
    return true;
}

bool SnapRings(std::vector<SnapRingInfo>& out, uint64_t* gen) {
    std::lock_guard<std::mutex> lk(g_traceMu);
    if (gen) *gen = g_trace.gen;
    out.clear();
    if (!g_trace.snapBlock || g_tracing.load(std::memory_order_seq_cst) || !g_trace.quiesced) return false;
    for (uint32_t r = 0; r < g_trace.snapCount; ++r) {
        const SnapRing& ring = g_trace.snapRings[r];
        const uint64_t w = ring.next.load(std::memory_order_relaxed);
        out.push_back(SnapRingInfo{ r, ring.cap, w, w > g_trace.snapK ? w - g_trace.snapK : 0,
                                    ring.skippedBudget.load(std::memory_order_relaxed),
                                    ring.droppedBudget.load(std::memory_order_relaxed) });
    }
    return true;
}

bool CopyTrace(uint64_t from, size_t maxRecords, std::vector<TraceRecord>& out, uint64_t* next, TraceInfo* seen) {
    std::lock_guard<std::mutex> lk(g_traceMu);
    if (seen) *seen = InfoLocked();
    if (!g_trace.buf || g_tracing.load(std::memory_order_seq_cst) || !g_trace.quiesced) return false;
    const uint64_t w = g_trace.next.load(std::memory_order_relaxed);
    const uint64_t begin = from > FirstValidLocked() ? from : FirstValidLocked();
    if (next) *next = begin;
    if (begin >= w) return true;
    // [from, from + maxRecords), then clipped: a page asked from before the window does not reach further into it.
    const uint64_t want = (static_cast<uint64_t>(maxRecords) > UINT64_MAX - from) ? UINT64_MAX : from + maxRecords;
    const uint64_t end  = want < w ? want : w;
    if (end <= begin) return true;
    if (next) *next = end;
    out.reserve(out.size() + static_cast<size_t>(end - begin));
    for (uint64_t seq = begin; seq < end; ++seq) {
        const TraceRecord& r = g_trace.buf[seq % g_trace.cap];
        // A slot whose number is not `seq` was written out of turn when the ring lapped inside one write; it is
        // left out rather than shown under the wrong number.
        if ((r.seqKind & kTraceSeqMask) == seq) out.push_back(r);
    }
    return true;
}

bool TraceDistinct(std::vector<uintptr_t>& funcs, std::vector<uintptr_t>& objs, uint64_t* gen,
                   std::vector<FuncIdentity>* idents) {
    std::lock_guard<std::mutex> lk(g_traceMu);
    if (gen) *gen = g_trace.gen;
    if (!g_trace.buf || g_tracing.load(std::memory_order_seq_cst) || !g_trace.quiesced) return false;
    if (!g_trace.distinctReady) {
        // Sets, not a vector per record: this runs in the game's process, and a 512 MB ring holds about 6.7 million
        // entries but only thousands of distinct functions and objects (review DLL-6).
        std::unordered_set<uintptr_t> f, o;
        f.reserve(4096);
        o.reserve(16384);
        const uint64_t w = g_trace.next.load(std::memory_order_relaxed);
        for (uint64_t seq = FirstValidLocked(); seq < w; ++seq) {
            const TraceRecord& r = g_trace.buf[seq % g_trace.cap];
            if (r.seqKind != seq) continue;   // a return record, or a slot written out of turn
            f.insert(static_cast<uintptr_t>(r.a));
            if (r.b) o.insert(static_cast<uintptr_t>(r.b));
        }
        g_trace.distinctFuncs.assign(f.begin(), f.end());
        std::sort(g_trace.distinctFuncs.begin(), g_trace.distinctFuncs.end());
        g_trace.distinctObjs.assign(o.begin(), o.end());
        std::sort(g_trace.distinctObjs.begin(), g_trace.distinctObjs.end());
        // [TRACE-UNLOADED-NAMES] The identities the table read, frozen with the trace. g_mu inside g_traceMu: the
        // one place the two nest, and never the other way round (Reset leaves g_mu before FreeTrace; the hook never
        // takes g_traceMu). The table is this trace's recording's -- pe_profile_start replaces or frees the trace
        // before StartRecording clears the table -- so a later Start cannot hand these a newer recording's names.
        g_trace.distinctIdents.assign(g_trace.distinctFuncs.size(), FuncIdentity{});
        {
            std::lock_guard<std::mutex> lk2(g_mu);
            for (size_t i = 0; i < g_trace.distinctFuncs.size(); ++i) {
                auto it = g_stats.find(g_trace.distinctFuncs[i]);
                if (it != g_stats.end()) g_trace.distinctIdents[i] = it->second.ident;
            }
        }
        g_trace.distinctReady = true;
    }
    funcs = g_trace.distinctFuncs;
    objs  = g_trace.distinctObjs;
    if (idents) *idents = g_trace.distinctIdents;
    return true;
}

bool ReleaseIfEmpty() {
    std::lock_guard<std::mutex> lk(g_traceMu);
    if (!g_trace.buf || g_tracing.load(std::memory_order_seq_cst) || !g_trace.quiesced) return false;
    if (g_trace.next.load(std::memory_order_relaxed) != 0) return false;
    return FreeTraceLocked();
}

bool FreeTraceIfGen(uint64_t gen) {
    std::lock_guard<std::mutex> lk(g_traceMu);
    if (!g_trace.buf || g_trace.gen != gen || g_tracing.load(std::memory_order_seq_cst)) return false;
    return FreeTraceLocked();
}

std::string Base64Encode(const uint8_t* data, size_t len) {
    static const char kAlphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string out;
    out.resize(((len + 2) / 3) * 4);
    char* o = out.data();
    size_t i = 0;
    for (; i + 3 <= len; i += 3) {
        const uint32_t v = (uint32_t(data[i]) << 16) | (uint32_t(data[i + 1]) << 8) | data[i + 2];
        *o++ = kAlphabet[(v >> 18) & 63];
        *o++ = kAlphabet[(v >> 12) & 63];
        *o++ = kAlphabet[(v >> 6) & 63];
        *o++ = kAlphabet[v & 63];
    }
    if (const size_t rest = len - i) {
        const uint32_t v = (uint32_t(data[i]) << 16) | (rest == 2 ? uint32_t(data[i + 1]) << 8 : 0);
        *o++ = kAlphabet[(v >> 18) & 63];
        *o++ = kAlphabet[(v >> 12) & 63];
        *o++ = rest == 2 ? kAlphabet[(v >> 6) & 63] : '=';
        *o++ = '=';
    }
    return out;
}

void SetTraceClockForTest(uint64_t (*clock)()) {
    g_clock = clock ? clock : QpcNow;
}

} // namespace Linie
