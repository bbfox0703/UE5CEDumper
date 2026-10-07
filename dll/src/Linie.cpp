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
};
static std::mutex g_mu;
static std::unordered_map<uintptr_t, Stat> g_stats;
// The identity reader of the running recording, and its key check; nullptr reads nothing. Set and cleared under g_mu.
static FuncIdentityReader g_reader = nullptr;
static FuncKeyReader      g_keyReader = nullptr;
// Monotonic call-stream position (1-based), reset per recording. A function's
// firstSeq is g_seq at the moment it was first seen — the causal ordering.
static uint64_t g_seq = 0;

void RecordCall(uintptr_t ufunc, uint64_t nowMs) {
    std::lock_guard<std::mutex> lk(g_mu);
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
        }
    } else if (g_reader && g_keyReader && s.ident.captured) {
        // Review DLL-3: the table is keyed by address, and a freed function's address can be taken by another that
        // fires in the same recording. Its key read now against the one read: a change is read again in full; the
        // same names under a new class object are the same function, its class reloaded; other names are another
        // function, and the entry says so from then on.
        int32_t idx = 0, num = 0;
        uint64_t outer = 0;
        if (g_keyReader(ufunc, idx, num, outer)
            && (idx != s.ident.nameIndex || num != s.ident.nameNumber || outer != s.ident.outer)) {
            FuncIdentity id{};
            if (g_reader(ufunc, id)) {
                id.captured = true;
                id.reused = s.ident.reused
                    || id.nameIndex != s.ident.nameIndex || id.nameNumber != s.ident.nameNumber
                    || id.classIndex != s.ident.classIndex || id.classNumber != s.ident.classNumber;
                s.ident = id;
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
}

void StartRecording(FuncIdentityReader reader, FuncKeyReader keyReader) {
    std::lock_guard<std::mutex> lk(g_mu);
    g_stats.clear();
    g_stats.reserve(4096);  // bound rehash churn over the recording window
    g_seq = 0;
    g_reader = reader;
    g_keyReader = keyReader;
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
    {
        std::lock_guard<std::mutex> lk(g_mu);
        g_recording.store(false, std::memory_order_relaxed);
        g_stats.clear();
        g_seq = 0;
        g_reader = nullptr;
        g_keyReader = nullptr;
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
    g_trace.distinctReady = false;
    std::vector<uintptr_t>().swap(g_trace.distinctFuncs);
    std::vector<uintptr_t>().swap(g_trace.distinctObjs);
    std::vector<FuncIdentity>().swap(g_trace.distinctIdents);
    return true;
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

    g_trace.buf   = static_cast<TraceRecord*>(p);
    g_trace.bytes = size;
    g_trace.cap   = cap;
    g_trace.gen   = ++g_traceGen;
    g_trace.next.store(0, std::memory_order_relaxed);
    g_trace.ticked.Build(cfg.ticked);
    g_trace.exclude.Build(cfg.exclude);
    g_trace.distinctReady = false;
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

void TraceEnter(uintptr_t ufunc, uintptr_t obj, uintptr_t sp, uint32_t tid, TraceToken& tok) {
    tok = TraceToken{};
    InflightGuard inflight;
    if (!g_tracing.load(std::memory_order_seq_cst)) return;
    const uint64_t gen = g_trace.gen;
    bool open = false;
    if (!g_trace.ticked.Empty()) {
        if (t_scopeGen != gen) {
            t_scopeSp = 0;          // a scope left open by an earlier recording is not this one's
            t_scopeGen = gen;
        } else if (t_scopeSp != 0 && sp >= t_scopeSp) {
            t_scopeSp = 0;          // called from at or above the root's frame: the root is gone (unwound)
        }
        if (t_scopeSp == 0) {
            if (!g_trace.ticked.Contains(ufunc)) return;
            t_scopeSp = sp;
            open = true;
        }
    }
    if (!open && g_trace.exclude.Contains(ufunc)) return;
    const uint64_t seq = g_trace.next.fetch_add(1, std::memory_order_relaxed);
    TraceRecord& r = g_trace.buf[seq % g_trace.cap];
    r.seqKind = seq;
    r.ticks   = g_clock();
    r.a       = ufunc;
    r.b       = obj;
    r.tid     = tid;
    r.flags   = open ? kTraceScopeRoot : 0;
    tok.entrySeq = seq;
    tok.gen      = gen;
    tok.traced   = true;
    tok.opened   = open;
}

void TraceReturn(const TraceToken& tok, uint32_t tid) {
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
    return i;
}
}  // namespace

TraceInfo GetTraceInfo() {
    std::lock_guard<std::mutex> lk(g_traceMu);
    return InfoLocked();
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
