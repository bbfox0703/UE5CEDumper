// schlacht_seethrough_test.cpp
// UE5CEDumper — See-through (Schlacht.cpp) over a fake world, with a fake engine answering every game-thread invoke.
//
// WHAT IT DRIVES
//   Schlacht's whole surface -- SetEnabled, Release, SetPierceCount, GetStatus, IsActive, StopWorker, TakeLeftovers,
//   AdoptLeftovers -- and, through the #include, Tick itself: the resolution chain from GWorld to the camera, the
//   LineTraceSingle buffer and its pierce loop, the FHitResult read, the hide / un-hide diff, the deferred restore and
//   its give-up, the hand-off between copies, the owners, and the worker thread. Most ticks run synchronously on the
//   test's own thread (FxTickNow); the worker, the deferred restore and the lock-order checks run the real threads.
//
// WHAT IS FAKED
//   * The world: GWorld -> OwningGameInstance -> LocalPlayers[0] -> PlayerController -> Pawn / PlayerCameraManager,
//     KismetSystemLibrary with each LineTraceSingle shape, AActor::SetActorHiddenInGame, the FHitResult structs, and
//     an object array in the measured trap order (a class-name substring match for "actor" meets
//     Default__ActorChannel first). Reflection data built in this process and walked by the real Aura, Ubel and Serie.
//   * The engine: UE5_CallProcessEventEx records each call -- its parameters as they arrived, its thread, its place in
//     the run -- and answers what the test scripted: a camera pose, a scene of hits along +X, a hide code per actor.
//     Only a call answered 0 writes a result into the caller's buffer, as a call that ran would.
//   * The game thread's liveness (Stark::IsGameThreadResponsive), the hook (UE5_EnsureGameThreadHook), and Sein, which
//     records every log line under the same sequence stamp the calls carry, so a check can order the two.
//
// WHY IT #includes THE .cpp
//   Tick, CollectOccluders, the record (s_state) and both locks are statics of Schlacht.cpp. A synchronous tick needs no
//   100 ms wait, and a lock-order check asks the locks themselves. The core it walks is ue5_testcore, linked. Stark,
//   Frieren and MinHook are not: both invoke entry points are faked here, and the real queue is stark_dispatch_test's.
//
// THE SEAM
//   The deferred restore polls every 250 ms and gives up after five minutes. Inside Schlacht.cpp only, the two
//   constants resolve to this file's 50 ms and 1 s, so a give-up runs in a second; the static_assert under the include
//   proves the shipped values are untouched.

#include "../src/Grimoire.h"   // first: its #pragma once makes Schlacht.cpp's own include a no-op, so the two macros
                               // below rename only Schlacht.cpp's uses of the constants

namespace Grimoire {
constexpr int SCHLACHT_TEST_PENDING_TICK_MS = 50;     // a deferred-restore pass every 50 ms instead of 250
constexpr int SCHLACHT_TEST_PENDING_MAX_MS  = 1000;   // and a give-up after 1 s instead of five minutes
}  // namespace Grimoire
#define PENDING_RESTORE_TICK_MS SCHLACHT_TEST_PENDING_TICK_MS
#define PENDING_RESTORE_MAX_MS  SCHLACHT_TEST_PENDING_MAX_MS
#include "../src/Schlacht.cpp"  // NOLINT -- see "WHY IT #includes THE .cpp" above
#undef PENDING_RESTORE_TICK_MS
#undef PENDING_RESTORE_MAX_MS
static_assert(Grimoire::PENDING_RESTORE_MAX_MS == 300000 && Grimoire::PENDING_RESTORE_TICK_MS == 250,
              "the seam renames Schlacht.cpp's uses only: the shipped constants are untouched");

#include "../src/Serie.h"   // the fixture's name pool

#include <windows.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>   // getenv_s: the AddressSanitizer build's negative control; exit on a fixture overflow
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstring>
#include <deque>
#include <functional>
#include <initializer_list>
#include <map>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <tuple>      // std::ignore
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

// ══ the one clock every record is stamped with ═══════════════════════════════════════════════════════════════════
// A call and a log line written on different threads are ordered by this stamp, never by where they landed in a vector.
static std::atomic<uint64_t> g_seq{0};
static uint64_t FxStamp() { return g_seq.fetch_add(1) + 1; }

// ══ Sein: every log line recorded ════════════════════════════════════════════════════════════════════════════════
struct FxLogLine {
    uint64_t seq = 0;
    char lvl = 0;          // 'I' 'W' 'E' 'D'
    std::string cat;       // the writer's LOG_CAT: See-through's own lines are kSeeThruCat
    std::string text;
};
static std::mutex g_logMu;
static std::vector<FxLogLine> g_log;
// A test's hook into each line, called after the line is recorded and with neither recorder lock held, so it may block
// the writing thread (a deferred restore held on its "game thread resumed" line) without stalling every other writer.
// ⚠ It runs on the WRITER's thread, which may hold Schlacht's own locks: the enable refusals are logged under s_mutex
// and every line of SetEnabled and Release under s_workerMutex. A hook that blocks or calls into Schlacht filters to a
// line written with neither held, such as the deferred restore's.
static std::mutex g_logHookMu;
static std::function<void(const FxLogLine&)> g_onLog;
constexpr size_t kLogLineBytes = 8192;

static void FxLogV(char lvl, const char* cat, const char* fmt, va_list ap) {
    char b[kLogLineBytes];
    vsnprintf(b, sizeof b, fmt, ap);
    FxLogLine line;
    line.seq = FxStamp();
    line.lvl = lvl;
    line.cat = cat ? cat : "";
    line.text = b;
    {
        std::lock_guard<std::mutex> lk(g_logMu);
        g_log.push_back(line);
    }
    std::function<void(const FxLogLine&)> hook;
    {
        std::lock_guard<std::mutex> lk(g_logHookMu);
        hook = g_onLog;
    }
    if (hook) hook(line);
}
namespace Sein {
void Info(const char* cat, const char* fmt, ...) { va_list ap; va_start(ap, fmt); FxLogV('I', cat, fmt, ap); va_end(ap); }
void Error(const char* cat, const char* fmt, ...) { va_list ap; va_start(ap, fmt); FxLogV('E', cat, fmt, ap); va_end(ap); }
void Warn(const char* cat, const char* fmt, ...) { va_list ap; va_start(ap, fmt); FxLogV('W', cat, fmt, ap); va_end(ap); }
void Debug(const char* cat, const char* fmt, ...) { va_list ap; va_start(ap, fmt); FxLogV('D', cat, fmt, ap); va_end(ap); }
void Summary(const char*, ...) {}
}  // namespace Sein

// Schlacht.cpp #defines LOG_CAT before Sein.h and never undefines it, so this is the category its own lines carry.
static constexpr const char* kSeeThruCat = LOG_CAT;

// ══ Stark, the hook, Frieren's globals ═══════════════════════════════════════════════════════════════════════════
static std::atomic<bool> g_responsive{true};   // what IsGameThreadResponsive answers
namespace Stark {
bool IsGameThreadResponsive(int32_t) { return g_responsive.load(); }
void SetInvokeTimeoutMs(int32_t) {}
}  // namespace Stark

static std::atomic<bool> g_hookUp{true};       // what UE5_EnsureGameThreadHook answers
static std::atomic<int> g_ensureCalls{0};      // how often an enable asked for the hook
extern "C" bool UE5_EnsureGameThreadHook() {
    g_ensureCalls.fetch_add(1);
    return g_hookUp.load();
}

constexpr uint32_t kFixtureUEVersion = 504;    // UE 5.4: LWC FVectors, and FunctionTailShiftFor adds nothing
uint32_t g_cachedUEVersion = kFixtureUEVersion;
uintptr_t g_cachedGWorld = 0;
std::atomic<bool> g_initInProgress{false};

// ══ the fake engine's vocabulary ═════════════════════════════════════════════════════════════════════════════════
enum class FxKind { Other, Trace, CamLoc, CamRot, Hide };
constexpr int32_t kVecD = 24, kVecF = 12;   // an FVector under LWC (3 doubles) and before it (3 floats)

// Where one FHitResult shape keeps each member, relative to OutHit; -1 = the shape has no such member.
struct FxHitLayout {
    int32_t actor = -1, hoh = -1, comp = -1, impact = -1, location = -1;
    int32_t vec = kVecD;   // the width of ImpactPoint / Location
};
// What the fake engine knows about one UFunction blob, registered when the blob is built.
struct FxFnSpec {
    FxKind kind = FxKind::Other;
    int32_t vec = kVecD;   // Trace: Start / End width. Camera: the return's width.
    int32_t start = -1;    // Trace: Start's offset
    int32_t ret = -1;      // Trace: ReturnValue's offset. Camera: the return slot.
    int32_t outHit = -1;   // Trace: OutHit's offset; -1 = this variant has none
    int32_t flag = -1;     // Hide: bNewHidden's offset
    FxHitLayout hit;       // Trace: OutHit's struct
};
static std::unordered_map<uintptr_t, FxFnSpec> g_fnSpecs;   // written while the fixture is built, read-only after

// One hit along the camera's +X axis, `t` units past the camera. The indices are GObjects indices, written into
// whichever members the variant's FHitResult has (Actor on UE4, HitObjectHandle on UE5, Component on both).
struct FxSceneHit {
    double t = 0;
    int32_t actor = 0, hoh = 0, comp = 0;
};

// One call that reached the engine.
struct FxCall {
    uint64_t seq = 0;
    std::chrono::steady_clock::time_point t{};
    uintptr_t instance = 0, ufunc = 0;
    uint32_t size = 0;
    std::vector<uint8_t> params;   // as they arrived, before the fake wrote any result
    std::thread::id tid{};
    bool onMain = false;           // made on the thread that built the fixture
    bool background = false;       // Tot::IsBackgroundWorker() on the calling thread
    bool mutexFree = true;         // [INV.01] another thread could take s_mutex at the top of this call
    FxKind kind = FxKind::Other;
    int32_t rc = 0;                // what the engine answered
};

// ══ the fake engine's state: everything below is guarded by g_engMu ═════════════════════════════════════════════════
static std::mutex g_engMu;
static std::vector<FxCall> g_calls;
static std::deque<int32_t> g_traceRcs;                                    // popped per trace; empty = 0
static int32_t g_camLocRc = 0, g_camRotRc = 0;
static std::map<std::pair<uintptr_t, uint8_t>, std::deque<int32_t>> g_hideScript;   // {actor, bNewHidden} -> codes
static std::map<std::pair<uintptr_t, uint8_t>, int32_t> g_hideFallback;   // once that queue is empty; else 0
static std::vector<FxSceneHit> g_scene;
static bool g_hasSticky = false;                                          // every trace hits g_sticky
static FxSceneHit g_sticky;
constexpr double kCamLocDefault[3] = { 100.5, 200.25, 300.125 };          // exact in float and double alike
static double g_camLoc[3] = { kCamLocDefault[0], kCamLocDefault[1], kCamLocDefault[2] };
static double g_camRot[3] = { 0, 0, 0 };                                  // Pitch, Yaw, Roll: forward is +X
static std::unordered_map<uintptr_t, uint8_t> g_gameHidden;               // what each actor's hidden flag is now
static std::function<void(int)> g_onTrace;                                // argument: traces since it was armed
static int g_onTraceCalls = 0;
static std::function<void(uintptr_t, bool)> g_onHide;                     // arguments: the actor, bNewHidden
// Location sits this far before ImpactPoint along +X, so a ray start advanced from Location is told apart from one
// advanced from ImpactPoint. A shape with no ImpactPoint gets the hit point itself in Location.
constexpr double kLocationLag = 10.0;

static std::thread::id g_mainTid;
// [INV.01] Each call asks whether another thread can take s_mutex. The retry window keeps a GetStatus poll that holds
// it for a moment from reading as held; a caller that holds it across the call never lets go within it.
static std::atomic<bool> g_invProbe{true};
static std::atomic<int> g_invProbed{0}, g_invHeld{0};
constexpr int kInvProbeMs = 20;

// True when a thread other than the caller takes `m` within `ms`. Another thread, because a mutex the caller may own
// itself cannot be asked from the caller's own thread.
static bool FxFreeElsewhere(std::mutex& m, int ms = kInvProbeMs) {
    bool took = false;
    std::thread probe([&] {
        const auto until = std::chrono::steady_clock::now() + std::chrono::milliseconds(ms);
        do {
            if (m.try_lock()) {
                m.unlock();
                took = true;
                return;
            }
            std::this_thread::yield();
        } while (std::chrono::steady_clock::now() < until);
    });
    probe.join();
    return took;
}

static bool FxFits(uint32_t size, int32_t off, size_t len) {
    return off >= 0 && static_cast<size_t>(off) + len <= size;
}
static void FxPutVec(uint8_t* buf, uint32_t size, int32_t off, int32_t width, const double v[3]) {
    if (width >= kVecD) {
        if (!FxFits(size, off, static_cast<size_t>(kVecD))) return;
        for (int i = 0; i < 3; ++i) memcpy(buf + off + i * 8, &v[i], 8);
    } else {
        if (!FxFits(size, off, static_cast<size_t>(kVecF))) return;
        for (int i = 0; i < 3; ++i) {
            const float f = static_cast<float>(v[i]);
            memcpy(buf + off + i * 4, &f, 4);
        }
    }
}
static bool FxGetVec(const uint8_t* buf, uint32_t size, int32_t off, int32_t width, double out[3]) {
    if (width >= kVecD) {
        if (!FxFits(size, off, static_cast<size_t>(kVecD))) return false;
        for (int i = 0; i < 3; ++i) memcpy(&out[i], buf + off + i * 8, 8);
    } else {
        if (!FxFits(size, off, static_cast<size_t>(kVecF))) return false;
        for (int i = 0; i < 3; ++i) {
            float f = 0;
            memcpy(&f, buf + off + i * 4, 4);
            out[i] = f;
        }
    }
    return true;
}
static void FxPutI32(uint8_t* buf, uint32_t size, int32_t off, int32_t v) {
    if (FxFits(size, off, sizeof v)) memcpy(buf + off, &v, sizeof v);
}
static void FxPutByte(uint8_t* buf, uint32_t size, int32_t off, uint8_t v) {
    if (FxFits(size, off, 1)) buf[off] = v;
}

// g_engMu held. What LineTraceSingle does with a call that ran: the nearest scene hit at or past the ray's start.
static void FxRunTrace(const FxFnSpec& s, uint8_t* buf, uint32_t size) {
    double start[3] = { g_camLoc[0], g_camLoc[1], g_camLoc[2] };
    FxGetVec(buf, size, s.start, s.vec, start);
    const double tStart = start[0] - g_camLoc[0];
    const FxSceneHit* hit = nullptr;
    if (g_hasSticky) {
        hit = &g_sticky;
    } else {
        for (const auto& h : g_scene)
            if (h.t >= tStart && (!hit || h.t < hit->t)) hit = &h;
    }
    if (!hit) {
        FxPutByte(buf, size, s.ret, 0);
        return;
    }
    FxPutByte(buf, size, s.ret, 1);
    if (s.outHit < 0) return;
    const int32_t base = s.outHit;
    if (s.hit.actor >= 0) FxPutI32(buf, size, base + s.hit.actor, hit->actor);
    if (s.hit.hoh >= 0)   FxPutI32(buf, size, base + s.hit.hoh, hit->hoh);
    if (s.hit.comp >= 0)  FxPutI32(buf, size, base + s.hit.comp, hit->comp);
    const double point[3] = { g_camLoc[0] + hit->t, g_camLoc[1], g_camLoc[2] };
    if (s.hit.impact >= 0) {
        FxPutVec(buf, size, base + s.hit.impact, s.hit.vec, point);
        const double lagging[3] = { point[0] - kLocationLag, point[1], point[2] };
        if (s.hit.location >= 0) FxPutVec(buf, size, base + s.hit.location, s.hit.vec, lagging);
    } else if (s.hit.location >= 0) {
        FxPutVec(buf, size, base + s.hit.location, s.hit.vec, point);
    }
}

// The engine. The hooks run after the call is recorded and outside g_engMu: they may call AdoptLeftovers, store
// s_workerStop, block, or throw, and a throw leaves through Schlacht's own Invoke as a game's function would.
extern "C" int32_t UE5_CallProcessEventEx(uintptr_t instance, uintptr_t ufunc, uintptr_t params, uint32_t size) {
    FxCall c;
    c.seq = FxStamp();
    c.t = std::chrono::steady_clock::now();
    c.instance = instance;
    c.ufunc = ufunc;
    c.size = size;
    c.tid = std::this_thread::get_id();
    c.onMain = c.tid == g_mainTid;
    c.background = Tot::IsBackgroundWorker();
    if (g_invProbe.load()) {
        c.mutexFree = FxFreeElsewhere(s_mutex);
        g_invProbed.fetch_add(1);
        if (!c.mutexFree) g_invHeld.fetch_add(1);
    }
    uint8_t* buf = reinterpret_cast<uint8_t*>(params);
    if (buf && size) c.params.assign(buf, buf + size);
    const auto specIt = g_fnSpecs.find(ufunc);
    const FxFnSpec spec = specIt != g_fnSpecs.end() ? specIt->second : FxFnSpec{};
    c.kind = spec.kind;

    std::function<void(int)> onTrace;
    int traceIndex = 0;
    std::function<void(uintptr_t, bool)> onHide;
    bool hideArg = false;
    int32_t rc = 0;
    {
        std::lock_guard<std::mutex> lk(g_engMu);
        switch (spec.kind) {
        case FxKind::Trace:
            if (!g_traceRcs.empty()) {
                rc = g_traceRcs.front();
                g_traceRcs.pop_front();
            }
            if (rc == 0 && buf) FxRunTrace(spec, buf, size);
            if (g_onTrace) {
                onTrace = g_onTrace;
                traceIndex = g_onTraceCalls++;
            }
            break;
        case FxKind::CamLoc:
            rc = g_camLocRc;
            if (rc == 0 && buf) FxPutVec(buf, size, spec.ret, spec.vec, g_camLoc);
            break;
        case FxKind::CamRot:
            rc = g_camRotRc;
            if (rc == 0 && buf) FxPutVec(buf, size, spec.ret, spec.vec, g_camRot);
            break;
        case FxKind::Hide: {
            const uint8_t flag = (buf && FxFits(size, spec.flag, 1)) ? buf[spec.flag] : uint8_t{0};
            hideArg = flag != 0;
            const auto key = std::make_pair(instance, flag);
            auto q = g_hideScript.find(key);
            if (q != g_hideScript.end() && !q->second.empty()) {
                rc = q->second.front();
                q->second.pop_front();
            } else {
                const auto f = g_hideFallback.find(key);
                if (f != g_hideFallback.end()) rc = f->second;
            }
            if (rc == 0) g_gameHidden[instance] = flag;
            onHide = g_onHide;
            break;
        }
        case FxKind::Other:
            break;
        }
        c.rc = rc;
        g_calls.push_back(std::move(c));
    }
    if (onTrace) onTrace(traceIndex);
    if (onHide) onHide(instance, hideArg);
    return rc;
}

// ══ harness ══════════════════════════════════════════════════════════════════════════════════════════════════════
static std::atomic<int> g_pass{0}, g_fail{0};
static void check(const char* label, bool cond, const char* got = nullptr) {
    if (cond) { ++g_pass; }
    else { ++g_fail; printf("  FAIL  %s%s%s\n", label, got ? "   got: " : "", got ? got : ""); }
}
static void check(const char* label, bool cond, const std::string& got) { check(label, cond, got.c_str()); }
static void blk(const char* name) { printf("- %s\n", name); }

// Poll a condition for up to `ms`: the worker ticks every 100 ms, a deferred pass every 50, and a sleep on Windows is
// coarse.
constexpr DWORD kPollMs = 5;
static bool waitFor(const std::function<bool()>& cond, int ms) {
    const auto end = std::chrono::steady_clock::now() + std::chrono::milliseconds(ms);
    while (std::chrono::steady_clock::now() < end) {
        if (cond()) return true;
        Sleep(kPollMs);
    }
    return cond();
}

// The AddressSanitizer build only: the banner and negative control build_dll.py --asan --run needs (a deliberate heap
// overflow that must be reported). [CPP-HARDENING-2026-11-14]
static void FxAsanPreamble() {
#ifdef __SANITIZE_ADDRESS__
    printf("asan: on\n");
    size_t negLen = 0;
    if (getenv_s(&negLen, nullptr, 0, "UE5_ASAN_NEGATIVE_CONTROL") == 0 && negLen > 0) {
        constexpr size_t kNegBytes = 16;
        char* volatile p = new char[kNegBytes];
        p[kNegBytes] = 1;   // heap-buffer-overflow: ASan ends the process here
        delete[] p;
    }
#endif
}

// A run that checked nothing proves nothing, so it fails too.
static int FxExitCode() {
    const int pass = g_pass.load(), fail = g_fail.load();
    printf("\n%d checks, %d failure(s)\n", pass + fail, fail);
    if (pass == 0) printf("  FAIL  no check passed: a run that checked nothing is not a pass\n");
    return (fail > 0 || pass == 0) ? 1 : 0;
}

// ── formatting for `got` ──
static std::string FxS(long long v) { return std::to_string(v); }
static std::string FxHex(uintptr_t v) {
    char b[32];
    snprintf(b, sizeof b, "0x%llX", static_cast<unsigned long long>(v));
    return b;
}
static std::string FxFmt(const char* fmt, ...) {
    char b[kLogLineBytes];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(b, sizeof b, fmt, ap);
    va_end(ap);
    return b;
}
static std::string FxNameOf(uintptr_t obj) { return obj ? Ubel::GetName(obj) : std::string("(0)"); }
static long long FxMs(std::chrono::steady_clock::duration d) {
    return std::chrono::duration_cast<std::chrono::milliseconds>(d).count();
}
static std::string FxSetStr(const std::unordered_set<uintptr_t>& s) {
    std::vector<std::string> names;
    for (uintptr_t a : s) names.push_back(FxNameOf(a));
    std::sort(names.begin(), names.end());
    std::string out = "{";
    for (size_t i = 0; i < names.size(); ++i) out += (i ? ", " : "") + names[i];
    return out + "}";
}

// ══ the fixture: memory ══════════════════════════════════════════════════════════════════════════════════════════
constexpr size_t kObjBytes = 0x200;        // one UObject: clear of every UStruct and UFunction offset the walks read
constexpr size_t kFieldBytes = 0x100;      // one FField: clear of the FProperty subclass slot
constexpr size_t kFieldClassBytes = 0x20;  // one FFieldClass: only its name is read
constexpr int kMaxObjs = 192, kMaxFields = 256, kMaxFieldClasses = 16;
alignas(16) static uint8_t g_objArena[kMaxObjs][kObjBytes];
alignas(16) static uint8_t g_fieldArena[kMaxFields][kFieldBytes];
alignas(16) static uint8_t g_fclassArena[kMaxFieldClasses][kFieldClassBytes];
static int g_objUsed = 0, g_fieldUsed = 0, g_fclassUsed = 0;
static std::map<std::string, uintptr_t> g_fclassByName;

[[noreturn]] static void FxFixtureFull(const char* what) {
    printf("  FAIL  FIXTURE: %s is full -- raise its capacity\n", what);
    exit(2);
}
static uint8_t* FxBytes(uintptr_t a) { return reinterpret_cast<uint8_t*>(a); }
static uintptr_t FxAddr(const void* p) { return reinterpret_cast<uintptr_t>(p); }
static void FxPut64(uintptr_t at, int off, uint64_t v) { memcpy(FxBytes(at) + off, &v, sizeof v); }
static void FxPut32(uintptr_t at, int off, uint32_t v) { memcpy(FxBytes(at) + off, &v, sizeof v); }
static void FxPut16(uintptr_t at, int off, uint16_t v) { memcpy(FxBytes(at) + off, &v, sizeof v); }
static void FxSetPtr(uintptr_t obj, int off, uintptr_t v) { FxPut64(obj, off, v); }
static uintptr_t FxGetPtr(uintptr_t obj, int off) {
    uintptr_t v = 0;
    memcpy(&v, FxBytes(obj) + off, sizeof v);
    return v;
}
static void FxSetClass(uintptr_t obj, uintptr_t cls) { FxSetPtr(obj, Grimoire::OFF_UOBJECT_CLASS, cls); }
// A TArray header: { Data, Num, Max }.
constexpr int kTArrayNumAt = 0x08, kTArrayMaxAt = 0x0C;
constexpr int32_t kTArrayBytes = 0x10;
static void FxPutTArray(uintptr_t obj, int off, uintptr_t data, int32_t num, int32_t max) {
    FxPut64(obj, off, data);
    FxPut32(obj, off + kTArrayNumAt, static_cast<uint32_t>(num));
    FxPut32(obj, off + kTArrayMaxAt, static_cast<uint32_t>(max));
}
constexpr int32_t kPtrBytes = static_cast<int32_t>(sizeof(uintptr_t));

// ══ the fixture: names ═══════════════════════════════════════════════════════════════════════════════════════════
// A UE4 TNameEntryArray: one chunk of entry pointers, each entry 0x40 bytes with its text at +0x10, as dll_core_test's
// PROBECLASS lays it out. Spare entries after the last used one give the reader's look-ahead window real memory.
constexpr int kMaxNames = 256;
constexpr int kNameSpare = 8;
constexpr int kNameEntryBytes = 0x40;
constexpr int kNameTextAt = 0x10;
static uint8_t g_nameEntry[kMaxNames + kNameSpare][kNameEntryBytes];
static uintptr_t g_nameChunk[kMaxNames + 1];
static uintptr_t g_nameChunks[2];
static std::vector<std::string> g_nameText{ std::string() };   // [0] is FName None

static int32_t FxFName(const char* s) {
    for (size_t i = 1; i < g_nameText.size(); ++i)
        if (g_nameText[i] == s) return static_cast<int32_t>(i);
    const size_t i = g_nameText.size();
    const size_t len = strlen(s);
    if (i >= static_cast<size_t>(kMaxNames) || len + 1 > static_cast<size_t>(kNameEntryBytes - kNameTextAt))
        FxFixtureFull("the name table (or one entry's text)");
    memcpy(g_nameEntry[i] + kNameTextAt, s, len + 1);
    g_nameChunk[i] = FxAddr(g_nameEntry[i]);
    g_nameText.emplace_back(s);
    return static_cast<int32_t>(i);
}

// ══ the fixture: reflection builders ═════════════════════════════════════════════════════════════════════════════
constexpr const char* kObjectProperty = "ObjectProperty";
constexpr const char* kArrayProperty = "ArrayProperty";
constexpr const char* kBoolProperty = "BoolProperty";
constexpr const char* kByteProperty = "ByteProperty";
constexpr const char* kStructProperty = "StructProperty";
constexpr const char* kIntProperty = "IntProperty";
constexpr const char* kFloatProperty = "FloatProperty";
constexpr const char* kDoubleProperty = "DoubleProperty";
constexpr const char* kWeakObjectProperty = "WeakObjectProperty";
constexpr const char* kNameProperty = "NameProperty";

constexpr uint64_t kParm = 0x0080;               // CPF_Parm
constexpr uint64_t kOut = 0x0100;                // CPF_OutParm
constexpr uint64_t kRet = 0x0400;                // CPF_ReturnParm
constexpr uint64_t kPod = 0x0008001040000200;    // the plain-data flags a scalar parameter carries
constexpr uint8_t kNativeBoolByteMask = 0x01, kNativeBoolFieldMask = 0xFF;   // a native bool, not a bitfield

// The UFunction tail where the pinned FunctionFlags offset puts it: FunctionFlags, then NumParms +4, ParmsSize +6,
// ReturnValueOffset +8.
constexpr int kFnFlagsAt = 0xB0;
constexpr int kFnNumParmsAt = kFnFlagsAt + 4;
constexpr int kFnParmsSizeAt = kFnFlagsAt + 6;
constexpr int kFnRetOffAt = kFnFlagsAt + 8;
constexpr uint32_t kFnNative = 0x401;            // FUNC_Final | FUNC_Native
constexpr uint16_t kNoReturn = 0xFFFF;           // the ReturnValueOffset of a function that returns nothing
constexpr int kUClassInterfacesAt = 0xC0;        // UClass::Interfaces, measured: every class here reads none

// The metaclasses and packages every other object hangs off.
static uintptr_t g_metaClass, g_metaPackage, g_metaFunction, g_metaStruct, g_pkgEngine, g_pkgOther;

static uintptr_t FxNewObjBlob() {
    if (g_objUsed >= kMaxObjs) FxFixtureFull("the object arena");
    return FxAddr(g_objArena[g_objUsed++]);
}
static uintptr_t FxObjAt(uintptr_t at, uintptr_t cls, const char* name, uintptr_t outer) {
    FxPut64(at, Grimoire::OFF_UOBJECT_CLASS, cls);
    FxPut32(at, Grimoire::OFF_UOBJECT_NAME, static_cast<uint32_t>(FxFName(name)));
    FxPut64(at, DynOff::UOBJECT_OUTER, outer);
    return at;
}
static uintptr_t FxObj(uintptr_t cls, const char* name, uintptr_t outer) {
    return FxObjAt(FxNewObjBlob(), cls, name, outer);
}
// A native UClass: its class is the metaclass "Class", its outer a package.
static uintptr_t FxClass(const char* name, uintptr_t super, int32_t propsSize, uintptr_t outer = g_pkgEngine) {
    const uintptr_t c = FxObj(g_metaClass, name, outer);
    FxPut64(c, DynOff::USTRUCT_SUPER, super);
    FxPut32(c, DynOff::USTRUCT_PROPSSIZE, static_cast<uint32_t>(propsSize));
    return c;
}
static uintptr_t FxStruct(const char* name, int32_t size, uintptr_t firstField) {
    const uintptr_t s = FxObj(g_metaStruct, name, g_pkgEngine);
    FxPut32(s, DynOff::USTRUCT_PROPSSIZE, static_cast<uint32_t>(size));
    FxPut64(s, DynOff::USTRUCT_CHILDPROPS, firstField);
    return s;
}
static uintptr_t FxFieldClass(const char* type) {
    const auto it = g_fclassByName.find(type);
    if (it != g_fclassByName.end()) return it->second;
    if (g_fclassUsed >= kMaxFieldClasses) FxFixtureFull("the field-class arena");
    const uintptr_t fc = FxAddr(g_fclassArena[g_fclassUsed++]);
    FxPut32(fc, DynOff::FFIELDCLASS_NAME, static_cast<uint32_t>(FxFName(type)));
    g_fclassByName.emplace(type, fc);
    return fc;
}
// One FProperty. A StructProperty's struct and a BoolProperty's layout share the subclass slot after the base.
static uintptr_t FxField(const char* type, const char* name, int32_t off, int32_t size, uint64_t flags,
                         uintptr_t next = 0, uintptr_t structPtr = 0) {
    if (g_fieldUsed >= kMaxFields) FxFixtureFull("the field arena");
    const uintptr_t f = FxAddr(g_fieldArena[g_fieldUsed++]);
    FxPut64(f, DynOff::FFIELD_CLASS, FxFieldClass(type));
    FxPut32(f, DynOff::FFIELD_NAME, static_cast<uint32_t>(FxFName(name)));
    FxPut64(f, DynOff::FFIELD_NEXT, next);
    FxPut32(f, DynOff::FPROPERTY_ELEMSIZE - 4, 1);   // ArrayDim, the int32 just before ElementSize
    FxPut32(f, DynOff::FPROPERTY_ELEMSIZE, static_cast<uint32_t>(size));
    FxPut64(f, DynOff::FPROPERTY_FLAGS, flags);
    FxPut32(f, DynOff::FPROPERTY_OFFSET, static_cast<uint32_t>(off));
    if (strcmp(type, kStructProperty) == 0) FxPut64(f, DynOff::FSTRUCTPROP_STRUCT, structPtr);
    if (strcmp(type, kBoolProperty) == 0) {
        const int at = DynOff::FBOOLPROP_FIELDSIZE;   // FieldSize, ByteOffset, ByteMask, FieldMask
        uint8_t* p = FxBytes(f);
        p[at] = 1;
        p[at + 1] = 0;
        p[at + 2] = kNativeBoolByteMask;
        p[at + 3] = kNativeBoolFieldMask;
    }
    return f;
}
// A UFunction. The tail's fields are a uint8 and two uint16s; the arguments are ints so a call site needs no casts.
static uintptr_t FxFunc(const char* name, uintptr_t owner, uintptr_t firstParam, int32_t numParms, int32_t parmsSize,
                        int32_t retOff, uintptr_t next = 0) {
    const uintptr_t f = FxObj(g_metaFunction, name, owner);
    FxPut64(f, DynOff::USTRUCT_CHILDPROPS, firstParam);
    FxPut64(f, DynOff::UFIELD_NEXT, next);
    FxPut32(f, kFnFlagsAt, kFnNative);
    FxBytes(f)[kFnNumParmsAt] = static_cast<uint8_t>(numParms);
    FxPut16(f, kFnParmsSizeAt, static_cast<uint16_t>(parmsSize));
    FxPut16(f, kFnRetOffAt, static_cast<uint16_t>(retOff));
    return f;
}

// ══ the fixture: layouts ═════════════════════════════════════════════════════════════════════════════════════════
// KismetSystemLibrary::LineTraceSingle's parameter block on UE5 (FVector = 3 doubles): the default variant.
namespace FxLts {
constexpr int32_t kWorldContext = 0x00, kStart = 0x08, kEnd = 0x20, kChannel = 0x38, kComplex = 0x39, kIgnore = 0x40,
                  kDrawDebug = 0x50, kOutHit = 0x58, kOutHitBytes = 0xE8, kIgnoreSelf = 0x140, kReturn = 0x141,
                  kParmsSize = 0x148;
constexpr int32_t kChainEnd = kReturn + 1;        // where its own parameter chain ends
constexpr int32_t kShortParmsSize = 0x130;        // a tail read short of that end
constexpr uint8_t kNumParms = 10;
}  // namespace FxLts
// The same function on UE4 (FVector = 3 floats).
namespace FxLtsF {
constexpr int32_t kWorldContext = 0x00, kStart = 0x08, kEnd = 0x14, kChannel = 0x20, kComplex = 0x21, kIgnore = 0x28,
                  kDrawDebug = 0x38, kOutHit = 0x40, kOutHitBytes = 0x88, kIgnoreSelf = 0xC8, kReturn = 0xC9,
                  kParmsSize = 0xD0;
}  // namespace FxLtsF
// UE5's FHitResult (HitResult5) and UE4's (HitResult4), member offsets relative to OutHit.
namespace FxHit5 {
constexpr int32_t kFaceIndex = 0x00, kTime = 0x04, kLocation = 0x08, kImpact = 0x20, kHandle = 0x80,
                  kHandleBytes = 0x18, kComponent = 0x98, kBytes = 0xE8;
}  // namespace FxHit5
namespace FxHit4 {
constexpr int32_t kTime = 0x00, kDistance = 0x04, kLocation = 0x08, kImpact = 0x14, kActor = 0x60, kComponent = 0x68,
                  kBytes = 0x88;
}  // namespace FxHit4
constexpr int32_t kSaneParmsCeiling = 0x4000;    // the largest ParmsSize See-through must still accept (16 KB)
constexpr uint16_t kShortCamParms = 0x10;        // a camera getter whose tail reads short of its 24-byte return
// The world's reflected fields.
namespace FxOff {
constexpr int kOwningGameInstance = 0x30;       // World
constexpr int kSubsystems = 0x30;               // WorldFuzzy: an ArrayProperty whose name contains "GameInstance"
constexpr int kMyGameInstance = 0x40;           // WorldFuzzy: the ObjectProperty the fuzzy match must take
constexpr int kLocalPlayers = 0x38;             // GameInstance
constexpr int kPlayerController = 0x30;         // LocalPlayer
constexpr int kPawn = 0x30;                     // PlayerController
constexpr int kAckPawn = 0x38;                  // PlayerController
constexpr int kCameraManager = 0x40;            // PlayerController
constexpr int kOriginalController = 0x48;       // DebugCameraController
}  // namespace FxOff
constexpr int32_t kUObjectHeaderBytes = 0x28;   // a UObject's own header: vtable, flags, index, class, name, outer
constexpr int32_t kActorBytes = 0x30;           // where every subclass field here starts

// ══ the fixture: the object array ════════════════════════════════════════════════════════════════════════════════
// GObjects order matters: every lookup that a substring or a first-CDO rule would answer wrongly meets the wrong
// object first. Indices double as the FHitResult indices a scene names.
constexpr int32_t kIdxCdoActorChannel = 0, kIdxActorChannel = 1, kIdxCdoKslEx = 2, kIdxKsl = 3, kIdxCdoKsl = 4,
                  kIdxActor = 5, kIdxCdoActor = 6, kIdxWallA = 7, kIdxWallB = 8, kIdxWallC = 9, kIdxWallD = 10,
                  kIdxWall4 = 11, kIdxSmc0 = 19, kIdxSmc1 = 20, kIdxSub1 = 21, kIdxSub2 = 22, kIdxSub3 = 23,
                  kIdxSub4 = 24, kIdxHero = 25, kIdxEnemy = 26, kIdxNpc = 27, kIdxMaterial = 28, kIdxSpectator = 29,
                  kPoolSlots = 30;
constexpr int kWalls = 12;   // WallA..WallD, Wall4..Wall11: more than the pierce ceiling, so a clamp shows
static_assert(kIdxWallA + kWalls == kIdxSmc0, "the walls fill the slots between Default__Actor and the components");
static_assert(kIdxSpectator + 1 == kPoolSlots, "every slot is named");

constexpr int kItemSize = 24;           // the FUObjectItem stride InitWithExtendedLayout is told to expect
constexpr int kItemIndexAt = 12;        // InternalIndex within an item
namespace FxGObj {                      // the UE5 chunked FUObjectArray header
constexpr int kObjects = 0x10, kMaxElements = 0x20, kNumElements = 0x24, kMaxChunks = 0x28, kNumChunks = 0x2C,
              kBytes = 0x40;
}  // namespace FxGObj
static uint8_t g_poolItems[Grimoire::OBJECTS_PER_CHUNK * kItemSize];   // one whole chunk, as Aura reads one
static uintptr_t g_poolChunkTable[1];
alignas(16) static uint8_t g_poolHeader[FxGObj::kBytes];
static uintptr_t g_poolDefault[kPoolSlots];

enum class FxPoolVariant {
    Default,
    KslOnlyInOtherPackage,     // slots 3 and 4: a KismetSystemLibrary class and CDO under /Script/EngineOther
    ActorOnlyInOtherPackage,   // slots 5 and 6: an Actor class and CDO under /Script/EngineOther
    KslWithoutCdo,             // slot 4: Default__Actor2, so nothing derives from KismetSystemLibrary
    SecondKslCdo,              // slot 4: a second Default__KismetSystemLibrary blob, the first one gone
};

static void FxPoolPut(int32_t slot, uintptr_t obj) {
    uint8_t* item = g_poolItems + static_cast<size_t>(slot) * kItemSize;
    memcpy(item, &obj, sizeof obj);
    memcpy(item + kItemIndexAt, &slot, sizeof slot);
}
static uintptr_t FxSlot(int32_t slot) {
    uintptr_t obj = 0;
    memcpy(&obj, g_poolItems + static_cast<size_t>(slot) * kItemSize, sizeof obj);
    return obj;
}
static void FxPoolPublish() {
    g_poolChunkTable[0] = FxAddr(g_poolItems);
    const uintptr_t table = FxAddr(g_poolChunkTable);
    const int32_t count = kPoolSlots, chunks = 1;
    memcpy(g_poolHeader + FxGObj::kObjects, &table, sizeof table);
    memcpy(g_poolHeader + FxGObj::kMaxElements, &count, sizeof count);
    memcpy(g_poolHeader + FxGObj::kNumElements, &count, sizeof count);
    memcpy(g_poolHeader + FxGObj::kMaxChunks, &chunks, sizeof chunks);
    memcpy(g_poolHeader + FxGObj::kNumChunks, &chunks, sizeof chunks);
    Aura::InitWithExtendedLayout(FxAddr(g_poolHeader), kItemSize);
}

// ══ the fixture: its objects ═════════════════════════════════════════════════════════════════════════════════════
// Classes.
static uintptr_t g_cObject, g_cActor, g_cStaticMeshActor, g_cPawn, g_cCharacter, g_cHero, g_cEnemy, g_cNpc,
    g_cSpectator, g_cActorComponent, g_cSceneComponent, g_cPrimitiveComponent, g_cStaticMeshComponent,
    g_cMaterialInterface, g_cChannel, g_cActorChannel, g_cBpfl, g_cKsl, g_cKslEx, g_cKslOther, g_cActorOther, g_cWorld,
    g_cWorldFuzzy, g_cGameInstance, g_cLocalPlayer, g_cPlayerController, g_cDebugCamController, g_cBpDebugCam,
    g_cPcNoPawn, g_cPcm, g_cBpCam, g_cCamF, g_cCamNoLoc, g_cCamNoRet, g_cCamShort;
// ScriptStructs.
static uintptr_t g_sVector, g_sVectorF, g_sHandle, g_sHit5, g_sHit5NoImpact, g_sHit5NoPoint, g_sHit4;
// UFunctions. The LineTraceSingle variants are each their own blob, all named LineTraceSingle.
static uintptr_t g_fnSetHidden, g_fnSetHiddenShort, g_fnK2Loc;
static uintptr_t g_ltsD, g_ltsNoImpact, g_ltsNoPoint, g_ltsNoOutHit, g_ltsShort, g_ltsHuge, g_ltsMax, g_ltsF;
static uintptr_t g_fnLtsMulti;                          // NoLTS: KismetSystemLibrary with LineTraceMulti only
static uintptr_t g_fnLtsForObjects, g_fnLtsByProfile;   // ForObjects -> ByProfile -> LTS-D, as UE links them
static uintptr_t g_fnCamLoc, g_fnCamRot, g_fnCamLocF, g_fnCamRotF, g_fnCamRotNoLoc, g_fnCamLocNoRet, g_fnCamLocShort,
    g_fnCamRotShort;
// The object array's objects.
static uintptr_t g_cdoActorChannel, g_cdoKslEx, g_cdoKsl, g_cdoKsl2, g_cdoKslOther, g_cdoActor, g_cdoActorOther,
    g_cdoActor2;
static uintptr_t g_wall[kWalls];
static uintptr_t g_wallA, g_wallB, g_wallC, g_wallD;
static uintptr_t g_smc0, g_smc1;     // StaticMeshComponent_0 (outer WallC), _1 (outer WallB)
static uintptr_t g_sub[4];           // SubComp1 (outer WallD) .. SubComp4 (outer SubComp3)
static uintptr_t g_hero, g_enemy, g_npc, g_material, g_spectator;
// The world graph, outside the object array.
static uintptr_t g_world0, g_world2, g_gi0, g_lp0, g_pc0, g_dbgPc0, g_cam0, g_camDbg0, g_camF0, g_camNoLoc0,
    g_camNoRet0, g_subsystems;
static uintptr_t g_gworldSlot = 0;   // &GWorld: g_cachedGWorld points here
static uintptr_t g_lpArr[1];         // GameInstance.LocalPlayers' data
static uintptr_t g_unreadableWorld = 0;   // a reserved page, never committed
// PC_0 lives 0x10 into a larger zeroed blob, so the 8 bytes a read at PC-1 takes are memory the test owns.
constexpr int kPcInHost = 0x10;
alignas(16) static uint8_t g_pcHost[kPcInHost + kObjBytes];

static const char* const kWallNames[kWalls] = { "WallA", "WallB", "WallC", "WallD", "Wall4", "Wall5",
                                                "Wall6", "Wall7", "Wall8", "Wall9", "Wall10", "Wall11" };

// ── the fake engine's view of the functions ──
static void FxSpecTraceD(uintptr_t fn, bool hasOutHit, bool hasImpact, bool hasLocation) {
    FxFnSpec s;
    s.kind = FxKind::Trace;
    s.vec = kVecD;
    s.start = FxLts::kStart;
    s.ret = FxLts::kReturn;
    s.outHit = hasOutHit ? FxLts::kOutHit : -1;
    s.hit.hoh = FxHit5::kHandle;
    s.hit.comp = FxHit5::kComponent;
    s.hit.impact = hasImpact ? FxHit5::kImpact : -1;
    s.hit.location = hasLocation ? FxHit5::kLocation : -1;
    s.hit.vec = kVecD;
    g_fnSpecs[fn] = s;
}
static void FxSpecTraceF(uintptr_t fn) {
    FxFnSpec s;
    s.kind = FxKind::Trace;
    s.vec = kVecF;
    s.start = FxLtsF::kStart;
    s.ret = FxLtsF::kReturn;
    s.outHit = FxLtsF::kOutHit;
    s.hit.actor = FxHit4::kActor;
    s.hit.comp = FxHit4::kComponent;
    s.hit.impact = FxHit4::kImpact;
    s.hit.location = FxHit4::kLocation;
    s.hit.vec = kVecF;
    g_fnSpecs[fn] = s;
}

// ── builders for the structs and the parameter chains (each built back to front, so every Next is known) ──
static uintptr_t FxVectorStruct(const char* name, const char* type, int32_t member) {
    const uintptr_t z = FxField(type, "Z", 2 * member, member, 0);
    const uintptr_t y = FxField(type, "Y", member, member, 0, z);
    const uintptr_t x = FxField(type, "X", 0, member, 0, y);
    return FxStruct(name, 3 * member, x);
}
static uintptr_t FxHitResult5Struct(const char* name, bool withImpact, bool withLocation) {
    using namespace FxHit5;
    const uintptr_t comp = FxField(kWeakObjectProperty, "Component", kComponent, kPtrBytes, 0);
    uintptr_t next = FxField(kStructProperty, "HitObjectHandle", kHandle, kHandleBytes, 0, comp, g_sHandle);
    if (withImpact) next = FxField(kStructProperty, "ImpactPoint", kImpact, kVecD, 0, next, g_sVector);
    if (withLocation) next = FxField(kStructProperty, "Location", kLocation, kVecD, 0, next, g_sVector);
    const uintptr_t time = FxField(kFloatProperty, "Time", kTime, 4, 0, next);
    const uintptr_t face = FxField(kIntProperty, "FaceIndex", kFaceIndex, 4, 0, time);
    return FxStruct(name, kBytes, face);
}
static uintptr_t FxHitResult4Struct() {
    using namespace FxHit4;
    const uintptr_t comp = FxField(kWeakObjectProperty, "Component", kComponent, kPtrBytes, 0);
    const uintptr_t actor = FxField(kWeakObjectProperty, "Actor", kActor, kPtrBytes, 0, comp);
    const uintptr_t impact = FxField(kStructProperty, "ImpactPoint", kImpact, kVecF, 0, actor, g_sVectorF);
    const uintptr_t location = FxField(kStructProperty, "Location", kLocation, kVecF, 0, impact, g_sVectorF);
    const uintptr_t distance = FxField(kFloatProperty, "Distance", kDistance, 4, 0, location);
    const uintptr_t time = FxField(kFloatProperty, "Time", kTime, 4, 0, distance);
    return FxStruct("HitResult4", kBytes, time);
}
// LineTraceSingle's UE5 chain; hitStruct 0 leaves OutHit out.
static uintptr_t FxLtsDChain(uintptr_t hitStruct) {
    using namespace FxLts;
    const uintptr_t ret = FxField(kBoolProperty, "ReturnValue", kReturn, 1, kParm | kOut | kRet | kPod);
    const uintptr_t self = FxField(kBoolProperty, "bIgnoreSelf", kIgnoreSelf, 1, kParm | kPod, ret);
    const uintptr_t hit =
        hitStruct ? FxField(kStructProperty, "OutHit", kOutHit, kOutHitBytes, kParm | kOut, self, hitStruct) : self;
    const uintptr_t draw = FxField(kByteProperty, "DrawDebugType", kDrawDebug, 1, kParm | kPod, hit);
    const uintptr_t ignore = FxField(kArrayProperty, "ActorsToIgnore", kIgnore, kTArrayBytes, kParm, draw);
    const uintptr_t traceComplex = FxField(kBoolProperty, "bTraceComplex", kComplex, 1, kParm | kPod, ignore);
    const uintptr_t channel = FxField(kByteProperty, "TraceChannel", kChannel, 1, kParm | kPod, traceComplex);
    const uintptr_t end = FxField(kStructProperty, "End", kEnd, kVecD, kParm, channel, g_sVector);
    const uintptr_t start = FxField(kStructProperty, "Start", kStart, kVecD, kParm, end, g_sVector);
    return FxField(kObjectProperty, "WorldContextObject", kWorldContext, kPtrBytes, kParm, start);
}
static uintptr_t FxLtsFChain() {
    using namespace FxLtsF;
    const uintptr_t ret = FxField(kBoolProperty, "ReturnValue", kReturn, 1, kParm | kOut | kRet | kPod);
    // Lower-case, as an FName table that registered this spelling first holds it.
    const uintptr_t self = FxField(kBoolProperty, "bignoreself", kIgnoreSelf, 1, kParm | kPod, ret);
    const uintptr_t hit = FxField(kStructProperty, "OutHit", kOutHit, kOutHitBytes, kParm | kOut, self, g_sHit4);
    const uintptr_t draw = FxField(kByteProperty, "DrawDebugType", kDrawDebug, 1, kParm | kPod, hit);
    const uintptr_t ignore = FxField(kArrayProperty, "ActorsToIgnore", kIgnore, kTArrayBytes, kParm, draw);
    const uintptr_t traceComplex = FxField(kBoolProperty, "bTraceComplex", kComplex, 1, kParm | kPod, ignore);
    const uintptr_t channel = FxField(kByteProperty, "TraceChannel", kChannel, 1, kParm | kPod, traceComplex);
    const uintptr_t end = FxField(kStructProperty, "End", kEnd, kVecF, kParm, channel, g_sVectorF);
    const uintptr_t start = FxField(kStructProperty, "Start", kStart, kVecF, kParm, end, g_sVectorF);
    return FxField(kObjectProperty, "WorldContextObject", kWorldContext, kPtrBytes, kParm, start);
}
// A LineTraceSingle sibling (ForObjects / ByProfile): LineTraceSingle's members with `what` where TraceChannel sits
// and every later member moved past it, so its block is its own and not LineTraceSingle's.
static uintptr_t FxSiblingChain(const char* whatType, const char* whatName, int32_t whatBytes, int32_t& retOff,
                                int32_t& parmsSize) {
    const int32_t what = FxLts::kChannel;
    const int32_t traceComplex = what + whatBytes;
    const int32_t ignore = traceComplex + kPtrBytes;   // the TArray keeps its 8-byte alignment
    const int32_t draw = ignore + kTArrayBytes;
    const int32_t outHit = draw + kPtrBytes;
    const int32_t self = outHit + FxHit5::kBytes;
    retOff = self + 1;
    parmsSize = self + kPtrBytes;                       // the two bools, rounded up to the block's alignment
    const uintptr_t ret = FxField(kBoolProperty, "ReturnValue", retOff, 1, kParm | kOut | kRet | kPod);
    const uintptr_t selfF = FxField(kBoolProperty, "bIgnoreSelf", self, 1, kParm | kPod, ret);
    const uintptr_t hit = FxField(kStructProperty, "OutHit", outHit, FxLts::kOutHitBytes, kParm | kOut, selfF, g_sHit5);
    const uintptr_t drawF = FxField(kByteProperty, "DrawDebugType", draw, 1, kParm | kPod, hit);
    const uintptr_t ignoreF = FxField(kArrayProperty, "ActorsToIgnore", ignore, kTArrayBytes, kParm, drawF);
    const uintptr_t complexF = FxField(kBoolProperty, "bTraceComplex", traceComplex, 1, kParm | kPod, ignoreF);
    const uintptr_t whatF = FxField(whatType, whatName, what, whatBytes, kParm, complexF);
    const uintptr_t end = FxField(kStructProperty, "End", FxLts::kEnd, kVecD, kParm, whatF, g_sVector);
    const uintptr_t start = FxField(kStructProperty, "Start", FxLts::kStart, kVecD, kParm, end, g_sVector);
    return FxField(kObjectProperty, "WorldContextObject", FxLts::kWorldContext, kPtrBytes, kParm, start);
}
// A camera getter with one return: a StructProperty FVector at +0. retFlags without kRet makes it a getter whose
// return cannot be found.
static uintptr_t FxGetter(FxKind kind, const char* name, uintptr_t owner, uintptr_t vecStruct, int32_t retBytes,
                          uint64_t retFlags, int32_t parmsSize, uintptr_t next = 0) {
    const uintptr_t rv = FxField(kStructProperty, "ReturnValue", 0, retBytes, retFlags, 0, vecStruct);
    const int32_t retOff = (retFlags & kRet) ? 0 : static_cast<int32_t>(kNoReturn);
    const uintptr_t f = FxFunc(name, owner, rv, 1, parmsSize, retOff, next);
    FxFnSpec s;
    s.kind = kind;
    s.ret = 0;
    s.vec = retBytes >= kVecD ? kVecD : kVecF;
    g_fnSpecs[f] = s;
    return f;
}
static uintptr_t FxSetHiddenFn(int32_t parmsSize) {
    const uintptr_t p = FxField(kBoolProperty, "bNewHidden", 0, 1, kParm | kPod);
    const uintptr_t f = FxFunc("SetActorHiddenInGame", g_cActor, p, 1, parmsSize, kNoReturn);
    FxFnSpec s;
    s.kind = FxKind::Hide;
    s.flag = 0;
    g_fnSpecs[f] = s;
    return f;
}

// ══ variant swaps: each writes a fresh POINTER, never a walked field in place ══════════════════════════════════════
// Ubel memoizes a class walk keyed by witness pointers, not by field contents. A function swapped into
// KismetSystemLibrary also needs a new enable before a tick sees it: CollectOccluders resolves once per enable.
static void FxKsl(uintptr_t fn) { FxSetPtr(g_cKsl, DynOff::USTRUCT_CHILDREN, fn); }
static void FxActorFns(uintptr_t fn) { FxSetPtr(g_cActor, DynOff::USTRUCT_CHILDREN, fn); }
static void FxPool(FxPoolVariant v) {
    for (int32_t i = 0; i < kPoolSlots; ++i) FxPoolPut(i, g_poolDefault[i]);
    switch (v) {
    case FxPoolVariant::Default:
        break;
    case FxPoolVariant::KslOnlyInOtherPackage:
        FxPoolPut(kIdxKsl, g_cKslOther);
        FxPoolPut(kIdxCdoKsl, g_cdoKslOther);
        break;
    case FxPoolVariant::ActorOnlyInOtherPackage:
        FxPoolPut(kIdxActor, g_cActorOther);
        FxPoolPut(kIdxCdoActor, g_cdoActorOther);
        break;
    case FxPoolVariant::KslWithoutCdo:
        FxPoolPut(kIdxCdoKsl, g_cdoActor2);
        break;
    case FxPoolVariant::SecondKslCdo:
        FxPoolPut(kIdxCdoKsl, g_cdoKsl2);
        break;
    }
    FxPoolPublish();
}
// RES.09: PC_0 as a PlayerControllerNoPawn, and the 8 bytes a read at PC-1 would take spelling BP_Enemy_C_0 -- so a
// lookup that read at offset -1 would come back with a live pawn instead of nothing.
static void FxPcNoPawn(bool on) {
    const uintptr_t lead = on ? g_enemy : 0;
    memcpy(FxBytes(g_pc0) - 1, &lead, sizeof lead);
    FxSetClass(g_pc0, on ? g_cPcNoPawn : g_cPlayerController);
}
// The UE4 world: LineTraceSingle with 12-byte FVectors and HitResult4, and a camera whose getters return floats.
static void FxUE4World() {
    FxKsl(g_ltsF);
    FxSetPtr(g_pc0, FxOff::kCameraManager, g_camF0);
}
// Every pointer a block may swap, back to the good fixture.
static void FxWorldDefault() {
    g_gworldSlot = g_world0;
    g_cachedGWorld = FxAddr(&g_gworldSlot);
    FxSetPtr(g_world0, FxOff::kOwningGameInstance, g_gi0);
    g_lpArr[0] = g_lp0;
    FxPutTArray(g_gi0, FxOff::kLocalPlayers, FxAddr(g_lpArr), 1, 1);
    FxSetPtr(g_lp0, FxOff::kPlayerController, g_pc0);
    FxPcNoPawn(false);
    FxSetPtr(g_pc0, FxOff::kPawn, g_hero);
    FxSetPtr(g_pc0, FxOff::kAckPawn, g_hero);
    FxSetPtr(g_pc0, FxOff::kCameraManager, g_cam0);
    FxSetPtr(g_dbgPc0, FxOff::kPawn, g_spectator);
    FxSetPtr(g_dbgPc0, FxOff::kAckPawn, 0);
    FxSetPtr(g_dbgPc0, FxOff::kCameraManager, g_camDbg0);
    FxSetPtr(g_dbgPc0, FxOff::kOriginalController, g_pc0);
    FxSetClass(g_cam0, g_cBpCam);
    FxPutTArray(g_world2, FxOff::kSubsystems, g_subsystems, 1, 1);
    FxSetPtr(g_world2, FxOff::kMyGameInstance, g_gi0);
    FxKsl(g_ltsD);
    FxActorFns(g_fnSetHidden);
    FxPool(FxPoolVariant::Default);
}

// ══ the fake engine's script ═════════════════════════════════════════════════════════════════════════════════════
// An actor hit, named in whichever member the variant's FHitResult has: Actor on UE4, HitObjectHandle on UE5.
static FxSceneHit FxHitAt(int32_t idx, double t) { return FxSceneHit{ t, idx, idx, 0 }; }
static FxSceneHit FxHitHoh(int32_t idx, double t, int32_t comp = 0) { return FxSceneHit{ t, 0, idx, comp }; }
static FxSceneHit FxHitActor(int32_t idx, double t, int32_t comp = 0) { return FxSceneHit{ t, idx, 0, comp }; }
static FxSceneHit FxHitNone(double t) { return FxSceneHit{ t, 0, 0, 0 }; }   // a hit whose members name nothing

static void FxScene(const std::vector<FxSceneHit>& hits) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_scene = hits;
}
static void FxSticky(const FxSceneHit& hit) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_sticky = hit;
    g_hasSticky = true;
}
static void FxNoSticky() {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_hasSticky = false;
}
static void FxTraceRcs(std::initializer_list<int32_t> rcs) {
    std::lock_guard<std::mutex> lk(g_engMu);
    for (int32_t rc : rcs) g_traceRcs.push_back(rc);
}
static void FxCamRcs(int32_t locRc, int32_t rotRc) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_camLocRc = locRc;
    g_camRotRc = rotRc;
}
static void FxCamPose(const double loc[3], double pitch, double yaw) {
    std::lock_guard<std::mutex> lk(g_engMu);
    for (int i = 0; i < 3; ++i) g_camLoc[i] = loc[i];
    g_camRot[0] = pitch;
    g_camRot[1] = yaw;
    g_camRot[2] = 0;
}
static void FxCamRot(double pitch, double yaw) { FxCamPose(kCamLocDefault, pitch, yaw); }
static void FxScriptHide(uintptr_t actor, bool hidden, std::initializer_list<int32_t> rcs) {
    std::lock_guard<std::mutex> lk(g_engMu);
    auto& q = g_hideScript[std::make_pair(actor, static_cast<uint8_t>(hidden ? 1 : 0))];
    for (int32_t rc : rcs) q.push_back(rc);
}
static void FxScriptHideN(uintptr_t actor, bool hidden, int32_t rc, int count) {
    std::lock_guard<std::mutex> lk(g_engMu);
    auto& q = g_hideScript[std::make_pair(actor, static_cast<uint8_t>(hidden ? 1 : 0))];
    for (int i = 0; i < count; ++i) q.push_back(rc);
}
static void FxHideDefault(uintptr_t actor, bool hidden, int32_t rc) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_hideFallback[std::make_pair(actor, static_cast<uint8_t>(hidden ? 1 : 0))] = rc;
}
// -1 = no hide or un-hide of it ever ran; else its hidden flag now.
static int FxGameHidden(uintptr_t actor) {
    std::lock_guard<std::mutex> lk(g_engMu);
    const auto it = g_gameHidden.find(actor);
    return it == g_gameHidden.end() ? -1 : it->second;
}
static void FxOnTrace(std::function<void(int)> fn) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_onTrace = std::move(fn);
    g_onTraceCalls = 0;
}
static void FxOnHide(std::function<void(uintptr_t, bool)> fn) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_onHide = std::move(fn);
}
static void FxOnLog(std::function<void(const FxLogLine&)> fn) {
    std::lock_guard<std::mutex> lk(g_logHookMu);
    g_onLog = std::move(fn);
}
static void FxClearScripts() {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_traceRcs.clear();
    g_hideScript.clear();
    g_hideFallback.clear();
    g_scene.clear();
    g_hasSticky = false;
    g_camLocRc = g_camRotRc = 0;
    for (int i = 0; i < 3; ++i) g_camLoc[i] = kCamLocDefault[i];
    g_camRot[0] = g_camRot[1] = g_camRot[2] = 0;
    g_gameHidden.clear();
    g_onTraceCalls = 0;
}
static void FxClearRecorders() {
    {
        std::lock_guard<std::mutex> lk(g_engMu);
        g_calls.clear();
    }
    std::lock_guard<std::mutex> lk(g_logMu);
    g_log.clear();
}

// ══ reading the call log ═════════════════════════════════════════════════════════════════════════════════════════
static size_t FxCallMark() {
    std::lock_guard<std::mutex> lk(g_engMu);
    return g_calls.size();
}
static std::vector<FxCall> FxCalls(size_t from = 0) {
    std::lock_guard<std::mutex> lk(g_engMu);
    if (from >= g_calls.size()) return {};
    return std::vector<FxCall>(g_calls.begin() + static_cast<std::ptrdiff_t>(from), g_calls.end());
}
static std::vector<FxCall> FxCallsOf(FxKind kind, size_t from = 0) {
    std::vector<FxCall> out;
    for (auto& c : FxCalls(from))
        if (c.kind == kind) out.push_back(std::move(c));
    return out;
}
static int FxCount(FxKind kind, size_t from = 0) { return static_cast<int>(FxCallsOf(kind, from).size()); }
static int FxCountAll(size_t from = 0) { return static_cast<int>(FxCalls(from).size()); }
template <class T>
static T FxRd(const FxCall& c, size_t off) {
    T v{};
    if (off + sizeof(T) <= c.params.size()) memcpy(&v, c.params.data() + off, sizeof(T));
    return v;
}
static std::array<double, 3> FxVec(const FxCall& c, size_t off, int32_t width) {
    std::array<double, 3> v{};
    if (width >= kVecD) {
        for (size_t i = 0; i < 3; ++i) v[i] = FxRd<double>(c, off + i * 8);
    } else {
        for (size_t i = 0; i < 3; ++i) v[i] = FxRd<float>(c, off + i * 4);
    }
    return v;
}
constexpr int kNoHideByte = -1;   // a hide call that carried no parameter byte
static int FxHideByte(const FxCall& c) { return c.params.empty() ? kNoHideByte : c.params[0]; }
// SetActorHiddenInGame calls on `actor`; hidden -1 = either direction, else that bNewHidden.
static int FxHides(uintptr_t actor, int hidden = -1, size_t from = 0) {
    int n = 0;
    for (const auto& c : FxCallsOf(FxKind::Hide, from))
        if (c.instance == actor && (hidden < 0 || FxHideByte(c) == hidden)) ++n;
    return n;
}
// Every byte of the arriving parameters outside the [offset, offset + length) ranges is zero.
static bool FxZeroOutside(const FxCall& c, std::initializer_list<std::pair<size_t, size_t>> keep) {
    for (size_t i = 0; i < c.params.size(); ++i) {
        bool kept = false;
        for (const auto& r : keep) kept = kept || (i >= r.first && i < r.first + r.second);
        if (!kept && c.params[i] != 0) return false;
    }
    return true;
}

// ══ reading the log ══════════════════════════════════════════════════════════════════════════════════════════════
static size_t FxLogMark() {
    std::lock_guard<std::mutex> lk(g_logMu);
    return g_log.size();
}
static std::vector<FxLogLine> FxLogs(size_t from = 0) {
    std::lock_guard<std::mutex> lk(g_logMu);
    if (from >= g_log.size()) return {};
    return std::vector<FxLogLine>(g_log.begin() + static_cast<std::ptrdiff_t>(from), g_log.end());
}
static bool FxLogIs(const FxLogLine& l, char lvl, const char* cat) {
    return (!lvl || l.lvl == lvl) && (!cat || l.cat == cat);
}
// Lines whose text is exactly `text` (lvl 0 = any level, cat nullptr = any category).
static int FxLogCount(const std::string& text, char lvl = 0, const char* cat = kSeeThruCat, size_t from = 0) {
    int n = 0;
    for (const auto& l : FxLogs(from))
        if (FxLogIs(l, lvl, cat) && l.text == text) ++n;
    return n;
}
static int FxLogPrefix(const std::string& prefix, char lvl = 0, const char* cat = kSeeThruCat, size_t from = 0) {
    int n = 0;
    for (const auto& l : FxLogs(from))
        if (FxLogIs(l, lvl, cat) && l.text.compare(0, prefix.size(), prefix) == 0) ++n;
    return n;
}
// The stamp of the first such line, 0 when there is none.
static uint64_t FxLogSeq(const std::string& text, char lvl = 0, const char* cat = kSeeThruCat, size_t from = 0) {
    uint64_t best = 0;
    for (const auto& l : FxLogs(from))
        if (FxLogIs(l, lvl, cat) && l.text == text && (!best || l.seq < best)) best = l.seq;
    return best;
}
static uint64_t FxLogSeqPrefix(const std::string& prefix, char lvl = 0, const char* cat = kSeeThruCat,
                               size_t from = 0) {
    uint64_t best = 0;
    for (const auto& l : FxLogs(from))
        if (FxLogIs(l, lvl, cat) && l.text.compare(0, prefix.size(), prefix) == 0 && (!best || l.seq < best))
            best = l.seq;
    return best;
}
static int FxSeeThruLines(size_t from = 0) {
    int n = 0;
    for (const auto& l : FxLogs(from))
        if (l.cat == kSeeThruCat) ++n;
    return n;
}
static std::string FxLogDump(size_t from = 0, const char* cat = kSeeThruCat) {
    std::string out;
    for (const auto& l : FxLogs(from))
        if (!cat || l.cat == cat) out += std::string("\n      ") + l.lvl + " " + l.text;
    return out;
}

// ══ Schlacht's state ═════════════════════════════════════════════════════════════════════════════════════════════
static Schlacht::SeeThroughStatus FxStatus(uint64_t caller = Schlacht::kOwnerNone) {
    Schlacht::SeeThroughStatus s;
    std::ignore = Schlacht::GetStatus(s, caller);
    return s;
}
static std::unordered_set<uintptr_t> FxSet(std::initializer_list<uintptr_t> actors) {
    return std::unordered_set<uintptr_t>(actors);
}
static std::unordered_set<uintptr_t> FxSetOf(const std::vector<uintptr_t>& v) {
    return std::unordered_set<uintptr_t>(v.begin(), v.end());
}
static std::string FxStatusStr(const Schlacht::SeeThroughStatus& s) {
    return FxFmt("code=%d active=%d target=%d hidden=%d pierce=%d state=%d pending=%d abandoned=%d claims=%d ce=%d "
                 "caller=%d actors=", s.code, s.active ? 1 : 0, s.hasTarget ? 1 : 0, s.hiddenCount, s.pierceCount,
                 s.state, s.restorePending ? 1 : 0, s.restoreAbandoned ? 1 : 0, s.claims, s.ceClaimed ? 1 : 0,
                 s.callerClaimed ? 1 : 0) + FxSetStr(FxSetOf(s.hiddenActors));
}
static std::unordered_set<uintptr_t> FxRecordNow() {
    std::lock_guard<std::mutex> lk(s_mutex);
    return s_state.hiddenActors;
}
static std::unordered_set<uintptr_t> FxQueuedNow() {
    std::lock_guard<std::mutex> lk(s_mutex);
    return s_state.unhideQueued;
}
// "Off, with leftovers, and no restore waiting": the record written directly, nothing started.
static void FxRecord(const std::unordered_set<uintptr_t>& actors) {
    std::lock_guard<std::mutex> lk(s_mutex);
    s_state.hiddenActors = actors;
    s_state.hiddenCount = static_cast<int32_t>(actors.size());
}
static void FxSetQueued(const std::unordered_set<uintptr_t>& actors) {
    std::lock_guard<std::mutex> lk(s_mutex);
    s_state.unhideQueued = actors;
}
static void FxSetAbandoned(bool abandoned) {
    std::lock_guard<std::mutex> lk(s_mutex);
    s_state.restoreAbandoned = abandoned;
}
static void FxAdopt(std::initializer_list<uint64_t> actors) {
    const std::vector<uint64_t> v(actors);
    Schlacht::AdoptLeftovers(v.data(), static_cast<int32_t>(v.size()));
}
static bool FxWorkerJoinable() { return s_worker.joinable(); }
static bool FxPendingJoinable() { return s_pendingWorker.joinable(); }
static bool FxRestorePending() { return s_pendingRunning.load(); }

// ══ driving it ═══════════════════════════════════════════════════════════════════════════════════════════════════
// Client owner keys, as Voll::ClientOwnerKey would hand them out: any value but kOwnerNone and kOwnerCe.
constexpr uint64_t kClientA = 0x1111, kClientB = 0x2222, kClientC = 0x3333, kClientD = 0x5555, kClientNever = 0x9999;
constexpr int kQuietMs = 300;          // a window with nothing in it: three worker ticks, six deferred passes
constexpr int kHoldMs = 200;           // how long a thread must stay blocked to count as blocked
constexpr int kRestoreWithinMs = 1000; // a deferred restore lands within this once the game answers
constexpr int kGiveUpWithinMs = 3000;  // a seamed give-up (1 s) lands within this
constexpr int kResetWaitMs = 2000;
constexpr int kPendingTickMs = Grimoire::SCHLACHT_TEST_PENDING_TICK_MS;
constexpr int kPendingMaxMs = Grimoire::SCHLACHT_TEST_PENDING_MAX_MS;

// One tick on the test's own thread. Only on main, and only while no worker runs: the two would race on the record.
static void FxTickNow() {
    if (s_worker.joinable()) check("FIXTURE FxTickNow: no worker runs beside a synchronous tick", false);
    s_workerStop.store(false);
    Tick();
}
// On, with no worker left ticking: the init gate keeps the worker it starts from ticking, and StopWorker joins it.
static int32_t FxEnableSync(uint64_t owner = kClientA) {
    g_initInProgress.store(true);
    const int32_t rc = Schlacht::SetEnabled(true, owner);
    Schlacht::StopWorker();
    g_initInProgress.store(false);
    return rc;
}
// Back to a clean slate: hooks and scripts cleared, off, every thread joined, then (defaultWorld) every swapped
// pointer back, Schlacht's record at its declared defaults, and both recorders emptied. The world is restored after
// the threads stop, so no tick reads it half-swapped. A blocking hook must be released before this runs.
static void FxReset(bool defaultWorld = true) {
    FxOnTrace(nullptr);
    FxOnHide(nullptr);
    FxOnLog(nullptr);
    g_responsive.store(true);
    g_hookUp.store(true);
    g_initInProgress.store(false);
    FxClearScripts();
    try {
        std::ignore = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
    } catch (...) {
        // A hook cleared too late may still have thrown; the record is reset below either way.
    }
    waitFor([] { return !s_pendingRunning.load(); }, kResetWaitMs);
    Schlacht::StopWorker();
    {
        // The disable above un-hides the last scenario's record into the fake game; a scenario starts from a game no
        // call has touched, so "never ran" stays readable.
        std::lock_guard<std::mutex> lk(g_engMu);
        g_gameHidden.clear();
    }
    if (defaultWorld) FxWorldDefault();
    {
        std::lock_guard<std::mutex> lk(s_mutex);
        s_state = decltype(s_state){};   // the defaults Schlacht.cpp declares, every field at once
    }
    s_workerStop.store(false);
    Schlacht::SetPierceCount(Grimoire::SCHLACHT_PIERCE_DEFAULT);
    FxClearRecorders();
}

// ══ building it ══════════════════════════════════════════════════════════════════════════════════════════════════
static void FxBuildWorld() {
    g_mainTid = std::this_thread::get_id();

    // The offsets probe's decisions, made up front so no walk runs a GObjects vote: FName without the case-preserving
    // number, FProperty chains, the FunctionFlags offset decided at 0xB0 with the tail right after it, and
    // UClass::Interfaces measured.
    DynOff::bCasePreservingName = false;
    DynOff::bUseFProperty = true;
    g_cachedUEVersion = kFixtureUEVersion;
    DynOff::UFUNCTION_FLAGS = kFnFlagsAt;
    DynOff::UFUNCTION_TAIL_EXTRA = 0;
    DynOff::UFUNCTION_TAIL_MEASURED.store(-1);
    DynOff::bUFunctionFlagsDetected.store(true);
    DynOff::UCLASS_INTERFACES.store(kUClassInterfacesAt);

    // ── metaclasses and packages ──
    g_metaClass = FxNewObjBlob();
    FxObjAt(g_metaClass, g_metaClass, "Class", 0);
    g_metaPackage = FxObj(g_metaClass, "Package", 0);
    g_metaFunction = FxObj(g_metaClass, "Function", 0);
    g_metaStruct = FxObj(g_metaClass, "ScriptStruct", 0);
    g_pkgEngine = FxObj(g_metaPackage, "/Script/Engine", 0);
    g_pkgOther = FxObj(g_metaPackage, "/Script/EngineOther", 0);

    // ── ScriptStructs (each nested struct member points at a real ScriptStruct) ──
    g_sVector = FxVectorStruct("Vector", kDoubleProperty, kVecD / 3);
    g_sVectorF = FxVectorStruct("VectorF", kFloatProperty, kVecF / 3);
    g_sHandle = FxStruct("ActorInstanceHandle", FxHit5::kHandleBytes, 0);
    g_sHit5 = FxHitResult5Struct("HitResult5", true, true);
    g_sHit5NoImpact = FxHitResult5Struct("HitResult5NoImpact", false, true);
    g_sHit5NoPoint = FxHitResult5Struct("HitResult5NoPoint", false, false);
    g_sHit4 = FxHitResult4Struct();

    // ── classes ──
    g_cObject = FxClass("Object", 0, kUObjectHeaderBytes);
    g_cActor = FxClass("Actor", g_cObject, kActorBytes);
    g_cStaticMeshActor = FxClass("StaticMeshActor", g_cActor, kActorBytes);
    g_cPawn = FxClass("Pawn", g_cActor, kActorBytes);
    g_cCharacter = FxClass("Character", g_cPawn, kActorBytes);
    g_cHero = FxClass("BP_Hero_C", g_cCharacter, kActorBytes);
    g_cEnemy = FxClass("BP_Enemy_C", g_cPawn, kActorBytes);
    g_cNpc = FxClass("BP_Npc_C", g_cCharacter, kActorBytes);
    g_cSpectator = FxClass("SpectatorPawn", g_cPawn, kActorBytes);
    g_cActorComponent = FxClass("ActorComponent", g_cObject, kUObjectHeaderBytes);
    g_cSceneComponent = FxClass("SceneComponent", g_cActorComponent, kUObjectHeaderBytes);
    g_cPrimitiveComponent = FxClass("PrimitiveComponent", g_cSceneComponent, kUObjectHeaderBytes);
    g_cStaticMeshComponent = FxClass("StaticMeshComponent", g_cPrimitiveComponent, kUObjectHeaderBytes);
    g_cMaterialInterface = FxClass("MaterialInterface", g_cObject, kUObjectHeaderBytes);
    g_cChannel = FxClass("Channel", g_cObject, kUObjectHeaderBytes);
    g_cActorChannel = FxClass("ActorChannel", g_cChannel, kUObjectHeaderBytes);
    g_cBpfl = FxClass("BlueprintFunctionLibrary", g_cObject, kUObjectHeaderBytes);
    g_cKsl = FxClass("KismetSystemLibrary", g_cBpfl, kUObjectHeaderBytes);
    g_cKslEx = FxClass("KismetSystemLibraryEx", g_cObject, kUObjectHeaderBytes);   // the decoy: no LineTraceSingle
    g_cKslOther = FxClass("KismetSystemLibrary", g_cBpfl, kUObjectHeaderBytes, g_pkgOther);
    g_cActorOther = FxClass("Actor", g_cObject, kActorBytes, g_pkgOther);
    g_cWorld = FxClass("World", g_cObject, FxOff::kOwningGameInstance + kPtrBytes);
    g_cWorldFuzzy = FxClass("WorldFuzzy", g_cObject, FxOff::kMyGameInstance + kPtrBytes);
    g_cGameInstance = FxClass("GameInstance", g_cObject, FxOff::kLocalPlayers + kTArrayBytes);
    g_cLocalPlayer = FxClass("LocalPlayer", g_cObject, FxOff::kPlayerController + kPtrBytes);
    g_cPlayerController = FxClass("PlayerController", g_cActor, FxOff::kCameraManager + kPtrBytes);
    g_cDebugCamController = FxClass("DebugCameraController", g_cPlayerController,
                                    FxOff::kOriginalController + kPtrBytes);
    g_cBpDebugCam = FxClass("BP_DebugCam_C", g_cDebugCamController, FxOff::kOriginalController + kPtrBytes);
    g_cPcNoPawn = FxClass("PlayerControllerNoPawn", g_cActor, FxOff::kCameraManager + kPtrBytes);
    g_cPcm = FxClass("PlayerCameraManager", g_cActor, kActorBytes);
    g_cBpCam = FxClass("BP_CameraManager_C", g_cPcm, kActorBytes);
    g_cCamF = FxClass("CameraManagerF", g_cActor, kActorBytes);
    g_cCamNoLoc = FxClass("CameraNoLoc", g_cActor, kActorBytes);
    g_cCamNoRet = FxClass("CameraNoRet", g_cActor, kActorBytes);
    g_cCamShort = FxClass("CameraManagerShort", g_cActor, kActorBytes);

    // ── the world's reflected fields ──
    FxSetPtr(g_cWorld, DynOff::USTRUCT_CHILDPROPS,
             FxField(kObjectProperty, "OwningGameInstance", FxOff::kOwningGameInstance, kPtrBytes, 0));
    {
        const uintptr_t mine = FxField(kObjectProperty, "MyGameInstance", FxOff::kMyGameInstance, kPtrBytes, 0);
        FxSetPtr(g_cWorldFuzzy, DynOff::USTRUCT_CHILDPROPS,
                 FxField(kArrayProperty, "GameInstanceSubsystems", FxOff::kSubsystems, kTArrayBytes, 0, mine));
    }
    FxSetPtr(g_cGameInstance, DynOff::USTRUCT_CHILDPROPS,
             FxField(kArrayProperty, "LocalPlayers", FxOff::kLocalPlayers, kTArrayBytes, 0));
    FxSetPtr(g_cLocalPlayer, DynOff::USTRUCT_CHILDPROPS,
             FxField(kObjectProperty, "PlayerController", FxOff::kPlayerController, kPtrBytes, 0));
    {
        const uintptr_t cam = FxField(kObjectProperty, "PlayerCameraManager", FxOff::kCameraManager, kPtrBytes, 0);
        const uintptr_t ack = FxField(kObjectProperty, "AcknowledgedPawn", FxOff::kAckPawn, kPtrBytes, 0, cam);
        FxSetPtr(g_cPlayerController, DynOff::USTRUCT_CHILDPROPS,
                 FxField(kObjectProperty, "Pawn", FxOff::kPawn, kPtrBytes, 0, ack));
    }
    FxSetPtr(g_cDebugCamController, DynOff::USTRUCT_CHILDPROPS,
             FxField(kObjectProperty, "OriginalControllerRef", FxOff::kOriginalController, kPtrBytes, 0));
    FxSetPtr(g_cPcNoPawn, DynOff::USTRUCT_CHILDPROPS,
             FxField(kObjectProperty, "PlayerCameraManager", FxOff::kCameraManager, kPtrBytes, 0));

    // ── AActor's functions ──
    g_fnSetHidden = FxSetHiddenFn(1);
    g_fnSetHiddenShort = FxSetHiddenFn(0);   // ParmsSize 0, bNewHidden still at +0
    g_fnK2Loc = FxFunc("K2_GetActorLocation", g_cActor, 0, 0, 0, kNoReturn);   // NoSetter: the setter cooked out
    FxSetPtr(g_cActorOther, DynOff::USTRUCT_CHILDREN, g_fnSetHidden);

    // ── KismetSystemLibrary's functions ──
    const char* const kLineTraceSingle = "LineTraceSingle";
    g_ltsD = FxFunc(kLineTraceSingle, g_cKsl, FxLtsDChain(g_sHit5), FxLts::kNumParms, FxLts::kParmsSize,
                    FxLts::kReturn);
    g_ltsNoImpact = FxFunc(kLineTraceSingle, g_cKsl, FxLtsDChain(g_sHit5NoImpact), FxLts::kNumParms,
                           FxLts::kParmsSize, FxLts::kReturn);
    g_ltsNoPoint = FxFunc(kLineTraceSingle, g_cKsl, FxLtsDChain(g_sHit5NoPoint), FxLts::kNumParms, FxLts::kParmsSize,
                          FxLts::kReturn);
    g_ltsNoOutHit = FxFunc(kLineTraceSingle, g_cKsl, FxLtsDChain(0), FxLts::kNumParms - 1, FxLts::kParmsSize,
                           FxLts::kReturn);
    g_ltsShort = FxFunc(kLineTraceSingle, g_cKsl, FxLtsDChain(g_sHit5), FxLts::kNumParms, FxLts::kShortParmsSize,
                        FxLts::kReturn);
    g_ltsHuge = FxFunc(kLineTraceSingle, g_cKsl, FxLtsDChain(g_sHit5), FxLts::kNumParms, kSaneParmsCeiling + 1,
                       FxLts::kReturn);
    g_ltsMax = FxFunc(kLineTraceSingle, g_cKsl, FxLtsDChain(g_sHit5), FxLts::kNumParms, kSaneParmsCeiling,
                      FxLts::kReturn);
    g_ltsF = FxFunc(kLineTraceSingle, g_cKsl, FxLtsFChain(), FxLts::kNumParms, FxLtsF::kParmsSize, FxLtsF::kReturn);
    FxSpecTraceD(g_ltsD, true, true, true);
    FxSpecTraceD(g_ltsNoImpact, true, false, true);
    FxSpecTraceD(g_ltsNoPoint, true, false, false);
    FxSpecTraceD(g_ltsNoOutHit, false, false, false);
    FxSpecTraceD(g_ltsShort, true, true, true);
    FxSpecTraceD(g_ltsHuge, true, true, true);
    FxSpecTraceD(g_ltsMax, true, true, true);
    FxSpecTraceF(g_ltsF);
    g_fnLtsMulti = FxFunc("LineTraceMulti", g_cKsl, 0, 0, 0, kNoReturn);
    {
        // The siblings a cooked KismetSystemLibrary links into Children ahead of LineTraceSingle. The engine answers
        // neither (FxKind::Other): an ObjectTypes array left empty, or no profile, finds nothing.
        int32_t retP = 0, parmsP = 0, retO = 0, parmsO = 0;
        const uintptr_t chainP = FxSiblingChain(kNameProperty, "ProfileName", kPtrBytes, retP, parmsP);
        const uintptr_t chainO = FxSiblingChain(kArrayProperty, "ObjectTypes", kTArrayBytes, retO, parmsO);
        g_fnLtsByProfile = FxFunc("LineTraceSingleByProfile", g_cKsl, chainP, FxLts::kNumParms, parmsP, retP, g_ltsD);
        g_fnLtsForObjects = FxFunc("LineTraceSingleForObjects", g_cKsl, chainO, FxLts::kNumParms, parmsO, retO,
                                   g_fnLtsByProfile);
    }
    FxSetPtr(g_cKslOther, DynOff::USTRUCT_CHILDREN, g_ltsD);

    // ── the camera managers' getters ──
    const uint64_t kGetterRet = kParm | kOut | kRet;
    g_fnCamRot = FxGetter(FxKind::CamRot, "GetCameraRotation", g_cPcm, g_sVector, kVecD, kGetterRet, kVecD);
    g_fnCamLoc = FxGetter(FxKind::CamLoc, "GetCameraLocation", g_cPcm, g_sVector, kVecD, kGetterRet, kVecD, g_fnCamRot);
    FxSetPtr(g_cPcm, DynOff::USTRUCT_CHILDREN, g_fnCamLoc);
    g_fnCamRotF = FxGetter(FxKind::CamRot, "GetCameraRotation", g_cCamF, g_sVectorF, kVecF, kGetterRet, kVecF);
    g_fnCamLocF = FxGetter(FxKind::CamLoc, "GetCameraLocation", g_cCamF, g_sVectorF, kVecF, kGetterRet, kVecF,
                           g_fnCamRotF);
    FxSetPtr(g_cCamF, DynOff::USTRUCT_CHILDREN, g_fnCamLocF);
    g_fnCamRotNoLoc = FxGetter(FxKind::CamRot, "GetCameraRotation", g_cCamNoLoc, g_sVector, kVecD, kGetterRet, kVecD);
    FxSetPtr(g_cCamNoLoc, DynOff::USTRUCT_CHILDREN, g_fnCamRotNoLoc);
    g_fnCamLocNoRet = FxGetter(FxKind::CamLoc, "GetCameraLocation", g_cCamNoRet, g_sVector, kVecD, kParm | kOut, kVecD);
    FxSetPtr(g_cCamNoRet, DynOff::USTRUCT_CHILDREN, g_fnCamLocNoRet);
    g_fnCamRotShort = FxGetter(FxKind::CamRot, "GetCameraRotation", g_cCamShort, g_sVector, kVecD, kGetterRet,
                               kShortCamParms);
    g_fnCamLocShort = FxGetter(FxKind::CamLoc, "GetCameraLocation", g_cCamShort, g_sVector, kVecD, kGetterRet,
                               kShortCamParms, g_fnCamRotShort);
    FxSetPtr(g_cCamShort, DynOff::USTRUCT_CHILDREN, g_fnCamLocShort);

    // ── the object array's objects ──
    g_cdoActorChannel = FxObj(g_cActorChannel, "Default__ActorChannel", g_pkgEngine);
    g_cdoKslEx = FxObj(g_cKslEx, "Default__KismetSystemLibraryEx", g_pkgEngine);
    g_cdoKsl = FxObj(g_cKsl, "Default__KismetSystemLibrary", g_pkgEngine);
    g_cdoKsl2 = FxObj(g_cKsl, "Default__KismetSystemLibrary", g_pkgEngine);
    g_cdoKslOther = FxObj(g_cKslOther, "Default__KismetSystemLibrary", g_pkgOther);
    g_cdoActor = FxObj(g_cActor, "Default__Actor", g_pkgEngine);
    g_cdoActorOther = FxObj(g_cActorOther, "Default__Actor", g_pkgOther);
    g_cdoActor2 = FxObj(g_cActor, "Default__Actor2", g_pkgEngine);
    for (int i = 0; i < kWalls; ++i) g_wall[i] = FxObj(g_cStaticMeshActor, kWallNames[i], 0);
    g_wallA = g_wall[0];
    g_wallB = g_wall[1];
    g_wallC = g_wall[2];
    g_wallD = g_wall[3];
    g_smc0 = FxObj(g_cStaticMeshComponent, "StaticMeshComponent_0", g_wallC);
    g_smc1 = FxObj(g_cStaticMeshComponent, "StaticMeshComponent_1", g_wallB);
    g_sub[0] = FxObj(g_cStaticMeshComponent, "SubComp1", g_wallD);
    g_sub[1] = FxObj(g_cStaticMeshComponent, "SubComp2", g_sub[0]);
    g_sub[2] = FxObj(g_cStaticMeshComponent, "SubComp3", g_sub[1]);   // its actor is 3 hops up
    g_sub[3] = FxObj(g_cStaticMeshComponent, "SubComp4", g_sub[2]);   // its actor is 4 hops up
    g_hero = FxObj(g_cHero, "BP_Hero_C_0", 0);
    g_enemy = FxObj(g_cEnemy, "BP_Enemy_C_0", 0);
    g_npc = FxObj(g_cNpc, "BP_Npc_C_0", 0);
    g_material = FxObj(g_cMaterialInterface, "Material_0", g_pkgEngine);
    g_spectator = FxObj(g_cSpectator, "SpectatorPawn_0", 0);

    g_poolDefault[kIdxCdoActorChannel] = g_cdoActorChannel;
    g_poolDefault[kIdxActorChannel] = g_cActorChannel;
    g_poolDefault[kIdxCdoKslEx] = g_cdoKslEx;   // the decoy CDO, before the real one
    g_poolDefault[kIdxKsl] = g_cKsl;
    g_poolDefault[kIdxCdoKsl] = g_cdoKsl;
    g_poolDefault[kIdxActor] = g_cActor;
    g_poolDefault[kIdxCdoActor] = g_cdoActor;
    for (int i = 0; i < kWalls; ++i) g_poolDefault[kIdxWallA + i] = g_wall[i];
    g_poolDefault[kIdxSmc0] = g_smc0;
    g_poolDefault[kIdxSmc1] = g_smc1;
    g_poolDefault[kIdxSub1] = g_sub[0];
    g_poolDefault[kIdxSub2] = g_sub[1];
    g_poolDefault[kIdxSub3] = g_sub[2];
    g_poolDefault[kIdxSub4] = g_sub[3];
    g_poolDefault[kIdxHero] = g_hero;
    g_poolDefault[kIdxEnemy] = g_enemy;
    g_poolDefault[kIdxNpc] = g_npc;
    g_poolDefault[kIdxMaterial] = g_material;
    g_poolDefault[kIdxSpectator] = g_spectator;

    // ── the world graph ──
    g_world0 = FxObj(g_cWorld, "World_0", 0);
    g_world2 = FxObj(g_cWorldFuzzy, "World2_0", 0);
    g_gi0 = FxObj(g_cGameInstance, "GI_0", 0);
    g_lp0 = FxObj(g_cLocalPlayer, "LP_0", g_gi0);
    g_pc0 = FxObjAt(FxAddr(g_pcHost) + kPcInHost, g_cPlayerController, "PC_0", 0);
    g_dbgPc0 = FxObj(g_cBpDebugCam, "DbgPC_0", 0);
    g_cam0 = FxObj(g_cBpCam, "Cam_0", 0);
    g_camDbg0 = FxObj(g_cBpCam, "CamDbg_0", 0);
    g_camF0 = FxObj(g_cCamF, "CamF_0", 0);
    g_camNoLoc0 = FxObj(g_cCamNoLoc, "CamNoLoc_0", 0);
    g_camNoRet0 = FxObj(g_cCamNoRet, "CamNoRet_0", 0);
    g_subsystems = FxNewObjBlob();   // GameInstanceSubsystems' data: one zeroed element
    constexpr SIZE_T kPage = 0x1000;
    g_unreadableWorld = FxAddr(VirtualAlloc(nullptr, kPage, MEM_RESERVE, PAGE_NOACCESS));

    g_nameChunks[0] = FxAddr(g_nameChunk);
    Serie::InitUE4(FxAddr(g_nameChunks), kNameTextAt);

    FxClearScripts();
    FxWorldDefault();
    FxClearRecorders();
}

// Before main returns: every See-through thread stopped and joined. SafeThread detaches what is left at exit, and a
// detached thread would run on into destroyed statics.
static void FxCleanup() {
    FxOnTrace(nullptr);
    FxOnHide(nullptr);
    FxOnLog(nullptr);
    g_responsive.store(true);
    g_hookUp.store(true);
    g_initInProgress.store(false);
    Tot::ResetShutdown();
    Tot::ResetPerCommand();
    FxClearScripts();
    try {
        std::ignore = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
    } catch (...) {
        // Nothing to report here: the threads are what this step is for, and they are checked below.
    }
    waitFor([] { return !s_pendingRunning.load(); }, kResetWaitMs);
    Schlacht::StopWorker();
    check("CLEANUP.01 threads: no See-through thread outlives main -- no worker, no deferred restore, none running",
          !s_worker.joinable() && !s_pendingWorker.joinable() && !s_pendingRunning.load());
    g_cachedGWorld = 0;
    if (g_unreadableWorld) {
        VirtualFree(reinterpret_cast<void*>(g_unreadableWorld), 0, MEM_RELEASE);
        g_unreadableWorld = 0;
    }
}

// ══ group 1: STATUS0, REFUSE, PATH, TRACE ════════════════════════════════════════════════════════════════════════
// First in the run: ST.01 must meet Schlacht before any call reaches it, and the OutHit layout line PATH.02 reads is a
// once-per-process latch, so nothing in this group hits an occluder before PATH.01 does.

namespace G1 {

constexpr int32_t kRcOn = 1;               // SetEnabled: it is on
constexpr int32_t kStateOn = 1;            // status.state after an enable
constexpr int32_t kStatePollOnly = -1;     // status.state before any enable or disable
constexpr int32_t kPierceDefault = 1;      // SeeThroughStatus' declared pierceCount
constexpr int32_t kPierceTwo = 2;          // two walls deep: the second trace shows each buffer is built afresh
constexpr int kSearchCap = 100;            // more than the pool holds, so results[0] is the pool's first match
constexpr size_t kHit5Fields = 6;          // HitResult5's member count, as the fixture builds it
constexpr int32_t kBoolBytes = 1;          // a native bool parameter
constexpr int kHideTrue = 1;               // the bNewHidden byte of a hide
constexpr size_t kCamReads = 2;            // the two camera getters every tick calls ahead of its traces
constexpr size_t kEmptyTickCalls = kCamReads + 1;   // ...and the one trace an empty scene answers with a miss
constexpr double kTWallA = 500.0;          // WallA's distance along the ray
constexpr double kTWallB = 800.0;          // WallB's, behind it
constexpr const char* kLts = "LineTraceSingle";
constexpr const char* kOutHit = "OutHit";
constexpr const char* kKslName = "KismetSystemLibrary";
constexpr const char* kActorName = "Actor";
// Spelled out rather than taken from Grimoire: a path constant that drifted from the engine's would then refuse in
// PATH.01 instead of moving the fixture's expectation along with it.
constexpr const char* kActorClassPath = "/Script/Engine.Actor";
constexpr const char* kKslClassPath = "/Script/Engine.KismetSystemLibrary";

constexpr const char* kLineHookRefused =
    "SeeThrough: refusing to enable — game-thread hook unavailable (the tracing invokes would have to run off the "
    "game thread)";
constexpr const char* kLineNoTrace =
    "SeeThrough: refusing to enable — KismetSystemLibrary::LineTraceSingle not found (cooked out?), so nothing could "
    "be hidden";
constexpr const char* kLineNoSetter =
    "SeeThrough: refusing to enable — AActor::SetActorHiddenInGame not found (cooked out?), so nothing could be "
    "hidden";
constexpr const char* kLineTraceMissing = "SeeThrough: LineTraceSingle not found (cooked out?) — no occluder detection";
constexpr const char* kEnabledPrefix = "SeeThrough: enabled (";
std::string WorkerStartedLine() {
    return FxFmt("SeeThrough: worker started (%d ms tick)", Grimoire::SCHLACHT_TICK_MS);
}

// TR.03's expected ends: the camera (100.5, 200.25, 300.125) plus 1e5 along (cos p cos y, cos p sin y, sin p), worked
// out by hand so the check does not repeat the code's own arithmetic. tol 0 = exactly.
struct EndCase {
    double pitch, yaw, x, y, z, tol;
};
constexpr std::array<EndCase, 4> kEnds = { {
    { 0.0, 0.0, 100100.5, 200.25, 300.125, 0.0 },
    { 0.0, 90.0, 100.5, 100200.25, 300.125, 1e-6 },
    { 30.0, 90.0, 100.5, 86802.790378, 50300.125, 1e-5 },
    { 30.0, 0.0, 86703.040378, 200.25, 50300.125, 1e-5 },
} };
constexpr float kEndF0[3] = { 100100.5f, 200.25f, 300.125f };   // the UE4 world's end at (0,0): exact in float

const char* KindName(FxKind k) {
    switch (k) {
    case FxKind::Trace: return "Trace";
    case FxKind::CamLoc: return "CamLoc";
    case FxKind::CamRot: return "CamRot";
    case FxKind::Hide: return "Hide";
    case FxKind::Other: break;
    }
    return "Other";
}
std::string CallDesc(const FxCall& c) {
    return FxFmt("%s %s.%s/%u rc=%d", KindName(c.kind), FxNameOf(c.instance).c_str(), FxNameOf(c.ufunc).c_str(),
                 c.size, c.rc);
}
std::string CallsStr(const std::vector<FxCall>& calls) {
    std::string out = FxFmt("%zu call(s):", calls.size());
    for (const auto& c : calls) out += "\n      " + CallDesc(c);
    return out;
}
std::string VecStr(const std::array<double, 3>& v) { return FxFmt("(%.9f, %.9f, %.9f)", v[0], v[1], v[2]); }

// "Every call" is never true of no call at all: a check that saw nothing proves nothing.
template <class Pred>
bool Every(const std::vector<FxCall>& calls, Pred pred) {
    return !calls.empty() && std::all_of(calls.begin(), calls.end(), pred);
}
template <class Pred>
std::string FirstFail(const std::vector<FxCall>& calls, Pred pred) {
    if (calls.empty()) return "no call at all";
    for (size_t i = 0; i < calls.size(); ++i)
        if (!pred(calls[i])) return FxFmt("call %zu of %zu: ", i + 1, calls.size()) + CallDesc(calls[i]);
    return FxFmt("all %zu pass", calls.size());
}

bool CamCall(const FxCall& c, FxKind kind, uintptr_t cam, uintptr_t fn, int32_t bytes) {
    return c.kind == kind && c.instance == cam && c.ufunc == fn && c.size == static_cast<uint32_t>(bytes);
}
bool KslTrace(const FxCall& c, uintptr_t fn) {
    return c.kind == FxKind::Trace && c.instance == FxSlot(kIdxCdoKsl) && c.ufunc == fn;
}
// The two camera reads, then `traces` traces, then nothing but hides, one per actor of `hidden`.
bool HitTickShape(const std::vector<FxCall>& calls, uintptr_t cam, uintptr_t locFn, uintptr_t rotFn, int32_t vecBytes,
                  uintptr_t traceFn, size_t traces, const std::unordered_set<uintptr_t>& hidden) {
    if (calls.size() != kCamReads + traces + hidden.size()) return false;
    if (!CamCall(calls[0], FxKind::CamLoc, cam, locFn, vecBytes)
        || !CamCall(calls[1], FxKind::CamRot, cam, rotFn, vecBytes))
        return false;
    std::unordered_set<uintptr_t> seen;
    for (size_t i = kCamReads; i < calls.size(); ++i) {
        const bool ok = i < kCamReads + traces
            ? KslTrace(calls[i], traceFn)
            : calls[i].kind == FxKind::Hide && hidden.count(calls[i].instance) && seen.insert(calls[i].instance).second;
        if (!ok) return false;
    }
    return true;
}

std::pair<size_t, size_t> Span(int32_t off, int32_t len) {
    return { static_cast<size_t>(off), static_cast<size_t>(len) };
}
// An LTS-D trace that arrived carrying only its inputs: every byte but WorldContextObject, Start, End and bIgnoreSelf
// is zero (TraceChannel and bTraceComplex are written as 0).
bool OnlyInputs(const FxCall& c) {
    return FxZeroOutside(c, { Span(FxLts::kWorldContext, kPtrBytes), Span(FxLts::kStart, kVecD),
                              Span(FxLts::kEnd, kVecD), Span(FxLts::kIgnoreSelf, kBoolBytes) });
}

bool Near(const std::array<double, 3>& v, const EndCase& e) {
    return std::fabs(v[0] - e.x) <= e.tol && std::fabs(v[1] - e.y) <= e.tol && std::fabs(v[2] - e.z) <= e.tol;
}
bool Exactly(const std::array<double, 3>& v, const double want[3]) {
    return v[0] == want[0] && v[1] == want[1] && v[2] == want[2];
}
bool ExactlyF(const std::array<double, 3>& v, const float want[3]) {
    return v[0] == static_cast<double>(want[0]) && v[1] == static_cast<double>(want[1])
        && v[2] == static_cast<double>(want[2]);
}

// A class's OWN function by name, as Ubel walks it (no supers): what the fixture put on the class.
bool FindOwnFn(uintptr_t cls, const char* name, FunctionInfo& out) {
    for (const auto& f : Ubel::WalkFunctions(cls))
        if (f.name == name) {
            out = f;
            return true;
        }
    return false;
}
const FunctionParam* ParamNamed(const FunctionInfo& fi, const char* name) {
    for (const auto& p : fi.params)
        if (p.name == name) return &p;
    return nullptr;
}

// What every refused enable must leave: its answer, a status still off with nothing claimed, the one warning naming
// what is missing, and no worker -- no thread, no start line, not one invoke over a quiet window.
void Refused(const char* id, int32_t rc, int32_t want, const char* line, size_t logs, size_t calls) {
    const Schlacht::SeeThroughStatus s = FxStatus(kClientA);
    check(FxFmt("%s: SetEnabled answers %d", id, want).c_str(), rc == want, FxS(rc));
    check(FxFmt("%s: still off, code and state %d, nobody claims it", id, want).c_str(),
          !s.active && s.code == want && s.state == want && s.claims == 0 && !s.callerClaimed, FxStatusStr(s));
    check(FxFmt("%s: exactly one warning, naming what is missing", id).c_str(),
          FxLogCount(line, 'W', kSeeThruCat, logs) == 1, FxLogDump(logs));
    check(FxFmt("%s: no \"enabled\" line", id).c_str(), FxLogPrefix(kEnabledPrefix, 0, kSeeThruCat, logs) == 0,
          FxLogDump(logs));
    Sleep(static_cast<DWORD>(kQuietMs));
    check(FxFmt("%s: no worker -- no thread, no start line, not one invoke over %d ms", id, kQuietMs).c_str(),
          !FxWorkerJoinable() && FxLogCount(WorkerStartedLine(), 0, kSeeThruCat, logs) == 0 && FxCountAll(calls) == 0,
          CallsStr(FxCalls(calls)) + FxLogDump(logs));
}

}  // namespace G1

static void Blocks_G1() {
    // ── STATUS0 ── no FxReset first: ST.01 is about a Schlacht nothing has called yet.
    {
        blk("STATUS0 - the declared defaults before any See-through call, and the fixture's own controls");
        Schlacht::SeeThroughStatus s;
        const int32_t rc = Schlacht::GetStatus(s, kClientA);
        check("ST.01 defaults: GetStatus answers 0 before any See-through call", rc == Schlacht::STR_OK, FxS(rc));
        check("ST.01 defaults: every field at its declared default -- off, code 0, no target, pierce 1, state -1, "
              "nothing hidden, pending or abandoned, nobody claims it",
              s.code == Schlacht::STR_OK && !s.active && !s.hasTarget && s.hiddenCount == 0
                  && s.pierceCount == G1::kPierceDefault && s.state == G1::kStatePollOnly && !s.restorePending
                  && !s.restoreAbandoned && s.hiddenActors.empty() && s.claims == 0 && !s.ceClaimed
                  && !s.callerClaimed,
              FxStatusStr(s));
        check("ST.01 defaults: IsActive is false", !Schlacht::IsActive());

        // SET.00: without these, every negative check below could pass for the wrong reason.
        const uintptr_t actorCls = Aura::FindClassByPath(G1::kActorClassPath);
        check("SET.00 fixture: /Script/Engine.Actor is the Actor class blob", actorCls == g_cActor,
              FxNameOf(actorCls) + " " + FxHex(actorCls));
        const uintptr_t kslCls = Aura::FindClassByPath(G1::kKslClassPath);
        check("SET.00 fixture: /Script/Engine.KismetSystemLibrary is the KismetSystemLibrary class blob",
              kslCls == g_cKsl, FxNameOf(kslCls) + " " + FxHex(kslCls));
        const uintptr_t kslCdo = Aura::FindLiveOrDefaultOf(G1::kKslName);
        check("SET.00 fixture: KismetSystemLibrary's default object is slot 4's, past the decoy in slot 2",
              kslCdo == FxSlot(kIdxCdoKsl) && kslCdo == g_cdoKsl, FxNameOf(kslCdo) + " " + FxHex(kslCdo));
        const auto subActor = Aura::FindInstancesByClass(G1::kActorName, false, G1::kSearchCap);
        check("SET.00 fixture: a class-name substring match for \"Actor\" meets Default__ActorChannel first (slot 0)",
              !subActor.results.empty() && subActor.results[0].addr == FxSlot(kIdxCdoActorChannel),
              subActor.results.empty() ? std::string("(none)") : subActor.results[0].name);
        const auto subKsl = Aura::FindInstancesByClass(G1::kKslName, false, G1::kSearchCap);
        check("SET.00 fixture: one for \"KismetSystemLibrary\" meets the KismetSystemLibraryEx decoy first (slot 2)",
              !subKsl.results.empty() && subKsl.results[0].addr == FxSlot(kIdxCdoKslEx),
              subKsl.results.empty() ? std::string("(none)") : subKsl.results[0].name);
        FunctionInfo lts;
        const bool haveLts = G1::FindOwnFn(g_cKsl, G1::kLts, lts);
        const FunctionParam* outHit = haveLts ? G1::ParamNamed(lts, G1::kOutHit) : nullptr;
        check("SET.00 fixture: KismetSystemLibrary walks to LineTraceSingle, whose OutHit has HitResult5's 6 members",
              outHit && outHit->structFields.size() == G1::kHit5Fields,
              outHit ? FxS(static_cast<long long>(outHit->structFields.size()))
                     : std::string("no LineTraceSingle or no OutHit"));
        check("SET.00 fixture: LTS-D's buffer is 0x148, its ParmsSize",
              haveLts && lts.address == g_ltsD
                  && Ubel::ParamBufferSize(lts) == static_cast<uint32_t>(FxLts::kParmsSize),
              haveLts ? FxHex(Ubel::ParamBufferSize(lts)) : std::string("no LineTraceSingle"));
        FxKsl(g_ltsShort);
        FunctionInfo ltsShort;
        const bool haveShort = G1::FindOwnFn(g_cKsl, G1::kLts, ltsShort);
        FxKsl(g_ltsD);
        check("SET.00 fixture: LTS-D-Short's buffer is 0x142, where its chain ends, past its 0x130 tail",
              haveShort && ltsShort.address == g_ltsShort
                  && Ubel::ParamBufferSize(ltsShort) == static_cast<uint32_t>(FxLts::kChainEnd),
              haveShort ? FxHex(Ubel::ParamBufferSize(ltsShort)) : std::string("no LineTraceSingle"));
        const uintptr_t at7 = Aura::GetByIndex(kIdxWallA);
        check("SET.00 fixture: GObjects index 7 is WallA, the index a scene names it by", at7 == g_wallA,
              FxNameOf(at7));
    }

    // ── REFUSE ──
    {
        blk("REFUSE - a build that cannot hide anything is refused at enable, before anything starts");

        FxReset();
        g_hookUp.store(false);
        {
            const int ensure0 = g_ensureCalls.load();
            const size_t logs = FxLogMark(), calls = FxCallMark();
            const int32_t rc = Schlacht::SetEnabled(true, kClientA);
            const int asked = g_ensureCalls.load() - ensure0;
            check("REF.01 hook down: the enable asked for the game-thread hook exactly once", asked == 1, FxS(asked));
            G1::Refused("REF.01 hook down", rc, Schlacht::STR_ERR_NO_HOOK, G1::kLineHookRefused, logs, calls);
        }
        g_hookUp.store(true);

        FxReset();
        FxPool(FxPoolVariant::KslOnlyInOtherPackage);
        {
            const size_t logs = FxLogMark(), calls = FxCallMark();
            const int32_t rc = Schlacht::SetEnabled(true, kClientA);
            G1::Refused("REF.02 KSL only in /Script/EngineOther", rc, Schlacht::STR_ERR_REFLECTION, G1::kLineNoTrace,
                        logs, calls);
        }

        FxReset();
        FxKsl(g_fnLtsMulti);
        {
            const size_t logs = FxLogMark(), calls = FxCallMark();
            const int32_t rc = Schlacht::SetEnabled(true, kClientA);
            G1::Refused("REF.03 LineTraceMulti only", rc, Schlacht::STR_ERR_REFLECTION, G1::kLineNoTrace, logs, calls);
        }

        FxReset();
        FxKsl(g_ltsNoOutHit);
        {
            const size_t logs = FxLogMark(), calls = FxCallMark();
            const int32_t rc = Schlacht::SetEnabled(true, kClientA);
            G1::Refused("REF.04 no OutHit", rc, Schlacht::STR_ERR_REFLECTION, G1::kLineNoTrace, logs, calls);
        }

        FxReset();
        FxActorFns(g_fnK2Loc);
        {
            const size_t logs = FxLogMark(), calls = FxCallMark();
            const int32_t rc = Schlacht::SetEnabled(true, kClientA);
            G1::Refused("REF.05 setter cooked out", rc, Schlacht::STR_ERR_REFLECTION, G1::kLineNoSetter, logs, calls);
        }

        FxReset();
        FxKsl(g_ltsHuge);
        {
            const size_t logs = FxLogMark(), calls = FxCallMark();
            const int32_t rc = Schlacht::SetEnabled(true, kClientA);
            G1::Refused("REF.06 ParmsSize 0x4001", rc, Schlacht::STR_ERR_REFLECTION, G1::kLineNoTrace, logs, calls);
        }

        FxReset();
        FxKsl(g_ltsMax);
        {
            const int32_t rc = FxEnableSync();
            const Schlacht::SeeThroughStatus s = FxStatus();
            check("SANE ParmsSize 0x4000: the ceiling itself is accepted -- SetEnabled answers 1, on, state 1",
                  rc == G1::kRcOn && s.active && s.state == G1::kStateOn, FxS(rc) + " " + FxStatusStr(s));
        }

        FxReset();
        FxPool(FxPoolVariant::ActorOnlyInOtherPackage);
        {
            const size_t logs = FxLogMark(), calls = FxCallMark();
            const int32_t rc = Schlacht::SetEnabled(true, kClientA);
            G1::Refused("REF.07 Actor only in /Script/EngineOther", rc, Schlacht::STR_ERR_REFLECTION, G1::kLineNoSetter,
                        logs, calls);
        }

        FxReset();
        FxRecord(FxSet({ g_wallA }));
        FxKsl(g_fnLtsMulti);
        {
            const size_t calls = FxCallMark();
            const int32_t rc = Schlacht::SetEnabled(true, kClientA);
            const Schlacht::SeeThroughStatus s = FxStatus();
            check("REF.08 leftover kept: SetEnabled answers -3", rc == Schlacht::STR_ERR_REFLECTION, FxS(rc));
            check("REF.08 leftover kept: the record is still {WallA}, hiddenCount 1",
                  FxRecordNow() == FxSet({ g_wallA }) && s.hiddenCount == 1, FxStatusStr(s));
            check("REF.08 leftover kept: not one SetActorHiddenInGame -- the refusal comes before any leftover is "
                  "un-hidden",
                  FxCount(FxKind::Hide, calls) == 0, G1::CallsStr(FxCalls(calls)));
            check("OBS-O01 refusal waits on nothing: off with WallA recorded, yet no restore waits and none gave up "
                  "(pinned; a fix flips it)",
                  !s.active && s.hiddenCount == 1 && !s.restorePending && !s.restoreAbandoned, FxStatusStr(s));

            FxSetAbandoned(true);
            const int32_t rcAgain = Schlacht::SetEnabled(true, kClientA);
            const Schlacht::SeeThroughStatus s2 = FxStatus();
            check("OBS-O01 refusal waits on nothing: a refused enable (-3) clears a \"gave up\" it did nothing about "
                  "(pinned)",
                  rcAgain == Schlacht::STR_ERR_REFLECTION && !s2.restoreAbandoned && !s2.restorePending,
                  FxS(rcAgain) + " " + FxStatusStr(s2));

            FxSetAbandoned(true);
            g_hookUp.store(false);
            const int32_t rcHook = Schlacht::SetEnabled(true, kClientA);
            g_hookUp.store(true);
            const Schlacht::SeeThroughStatus s3 = FxStatus();
            check("OBS-O01 refusal waits on nothing: so does one refused with the hook down (-5) (pinned)",
                  rcHook == Schlacht::STR_ERR_NO_HOOK && !s3.restoreAbandoned && !s3.restorePending
                      && FxRecordNow() == FxSet({ g_wallA }),
                  FxS(rcHook) + " " + FxStatusStr(s3));
        }
    }

    // ── PATH ──
    {
        blk("PATH - the producers are asked for by class path and exact name; the first hit of the process");
        FxReset();
        check("PATH.01 setup: Default__ActorChannel sits in slot 0 and the KismetSystemLibraryEx decoy in slot 2, "
              "ahead of the real classes and default objects",
              FxSlot(kIdxCdoActorChannel) == g_cdoActorChannel && FxSlot(kIdxCdoKslEx) == g_cdoKslEx
                  && FxSlot(kIdxCdoKsl) == g_cdoKsl,
              FxNameOf(FxSlot(kIdxCdoActorChannel)) + ", " + FxNameOf(FxSlot(kIdxCdoKslEx)));
        {
            const int32_t rc = FxEnableSync(kClientA);
            check("PATH.01 by path: the probe passes with both decoys first -- SetEnabled answers 1", rc == G1::kRcOn,
                  FxS(rc));
            FxScene({ FxHitHoh(kIdxWallA, G1::kTWallA) });
            const size_t logs = FxLogMark(), mark = FxCallMark();
            FxTickNow();
            const auto traces = FxCallsOf(FxKind::Trace, mark);
            const auto toCdo = [](const FxCall& c) { return c.instance == FxSlot(kIdxCdoKsl) && c.ufunc == g_ltsD; };
            check("PATH.01 by path: every trace goes to Default__KismetSystemLibrary (slot 4) with the LTS-D blob",
                  G1::Every(traces, toCdo), G1::FirstFail(traces, toCdo));
            check("PATH.01 by path: exactly one SetActorHiddenInGame(WallA, true), and no other hide",
                  FxHides(g_wallA, G1::kHideTrue, mark) == 1 && FxCount(FxKind::Hide, mark) == 1,
                  G1::CallsStr(FxCalls(mark)));
            check("PATH.01 by path: the record is {WallA}", FxRecordNow() == FxSet({ g_wallA }),
                  FxSetStr(FxRecordNow()));
            check("PATH.01 by path: no \"LineTraceSingle not found\" warning",
                  FxLogCount(G1::kLineTraceMissing, 0, kSeeThruCat, logs) == 0, FxLogDump(logs));
        }
        {
            const std::string layout = FxFmt(
                "SeeThrough: OutHit param off=%d size=%d, %zu struct field(s): FaceIndex@%d Time@%d Location@%d "
                "ImpactPoint@%d HitObjectHandle@%d Component@%d ",
                FxLts::kOutHit, FxLts::kOutHitBytes, G1::kHit5Fields, FxHit5::kFaceIndex, FxHit5::kTime,
                FxHit5::kLocation, FxHit5::kImpact, FxHit5::kHandle, FxHit5::kComponent);
            check("PATH.02 layout line: exactly one OutHit layout line, built from the fixture's own field table",
                  FxLogCount(layout, 'I') == 1, FxLogDump());
            const size_t mark = FxCallMark();
            FxTickNow();
            check("PATH.02 layout line: the second tick hits WallA again -- one trace, no hide or un-hide, record "
                  "{WallA}",
                  FxCount(FxKind::Trace, mark) == 1 && FxCount(FxKind::Hide, mark) == 0
                      && FxRecordNow() == FxSet({ g_wallA }),
                  G1::CallsStr(FxCalls(mark)));
            check("PATH.02 layout line: ...and there is still exactly one -- the dump is once per process",
                  FxLogCount(layout, 'I') == 1, FxLogDump());
        }

        FxReset();
        FxPool(FxPoolVariant::KslWithoutCdo);
        {
            check("OBS-O04a setup: slot 4 holds Default__Actor2, so nothing is KismetSystemLibrary's default object",
                  FxSlot(kIdxCdoKsl) == g_cdoActor2, FxNameOf(FxSlot(kIdxCdoKsl)));
            const int32_t rc = FxEnableSync();
            check("OBS-O04a no CDO: the probe asks the class, which is there -- SetEnabled answers 1",
                  rc == G1::kRcOn, FxS(rc));
            FxAdopt({ g_wallA });
            const size_t logs = FxLogMark(), mark = FxCallMark();
            FxTickNow();
            const Schlacht::SeeThroughStatus s = FxStatus();
            check("OBS-O04a no CDO: the tick traces nothing and warns nothing",
                  FxCount(FxKind::Trace, mark) == 0 && FxLogCount(G1::kLineTraceMissing, 0, kSeeThruCat, logs) == 0,
                  G1::CallsStr(FxCalls(mark)) + FxLogDump(logs));
            check("OBS-O04a no CDO: ...yet it reads as \"nothing occludes\" -- WallA un-hidden once, record {}, "
                  "code 0, a target (pinned; a fix flips it)",
                  FxHides(g_wallA, 0, mark) == 1 && FxCount(FxKind::Hide, mark) == 1 && s.hiddenActors.empty()
                      && s.hiddenCount == 0 && s.code == Schlacht::STR_OK && s.hasTarget,
                  FxStatusStr(s) + " " + G1::CallsStr(FxCalls(mark)));
        }

        FxReset();
        FxKsl(g_fnLtsForObjects);   // ForObjects -> ByProfile -> LTS-D, the order a cooked library links them in
        {
            const int32_t rc = FxEnableSync();
            check("PATH.03 exact name: with LineTraceSingleForObjects and ...ByProfile linked first, SetEnabled "
                  "answers 1",
                  rc == G1::kRcOn, FxS(rc));
            FxScene({ FxHitHoh(kIdxWallA, G1::kTWallA) });
            const size_t mark = FxCallMark();
            FxTickNow();
            const auto traces = FxCallsOf(FxKind::Trace, mark);
            const auto itself = [](const FxCall& c) {
                return c.ufunc == g_ltsD && c.size == static_cast<uint32_t>(FxLts::kParmsSize)
                    && FxRd<uint8_t>(c, FxLts::kChannel) == 0;
            };
            check("PATH.03 exact name: every trace is LineTraceSingle itself -- its blob, 0x148 bytes, TraceChannel "
                  "byte 0",
                  G1::Every(traces, itself), G1::FirstFail(traces, itself));
            int siblings = 0;
            for (const auto& c : FxCalls(mark))
                if (c.ufunc == g_fnLtsForObjects || c.ufunc == g_fnLtsByProfile) ++siblings;
            check("PATH.03 exact name: neither sibling whose name starts the same way is ever called", siblings == 0,
                  G1::CallsStr(FxCalls(mark)));
            check("PATH.03 exact name: WallA is hidden", FxHides(g_wallA, G1::kHideTrue, mark) == 1
                      && FxRecordNow() == FxSet({ g_wallA }),
                  FxSetStr(FxRecordNow()));
        }
    }

    // ── TRACE ──
    {
        blk("TRACE - the LineTraceSingle buffer: whose world, from where, which way, what else it carries, how big");

        // The LTS-D world, the default pose, an empty scene: the record stays empty, so a tick is only reads and
        // traces.
        FxReset();
        check("TRACE setup: the LTS-D world enables", FxEnableSync() == G1::kRcOn);
        {
            const size_t mark = FxCallMark();
            FxTickNow();
            const auto calls = FxCalls(mark);
            check("TR.08 call order: an empty-scene tick is exactly GetCameraLocation(Cam_0), "
                  "GetCameraRotation(Cam_0), LineTraceSingle(Default__KismetSystemLibrary) -- the camera calls "
                  "24 bytes",
                  calls.size() == G1::kEmptyTickCalls
                      && G1::CamCall(calls[0], FxKind::CamLoc, g_cam0, g_fnCamLoc, kVecD)
                      && G1::CamCall(calls[1], FxKind::CamRot, g_cam0, g_fnCamRot, kVecD)
                      && G1::KslTrace(calls[2], g_ltsD),
                  G1::CallsStr(calls));
            const auto traces = FxCallsOf(FxKind::Trace, mark);
            const auto start = traces.empty() ? std::array<double, 3>{} : FxVec(traces[0], FxLts::kStart, kVecD);
            check("TR.02 start: the first trace starts at the camera, exactly (3 doubles @0x08)",
                  !traces.empty() && G1::Exactly(start, kCamLocDefault), G1::VecStr(start));
            const auto end = traces.empty() ? std::array<double, 3>{} : FxVec(traces[0], FxLts::kEnd, kVecD);
            check("TR.03 end (0,0): 1e5 along +X from the camera, exactly",
                  !traces.empty() && G1::Near(end, G1::kEnds[0]), G1::VecStr(end));
        }
        for (size_t i = 1; i < G1::kEnds.size(); ++i) {
            const G1::EndCase& e = G1::kEnds[i];
            FxCamRot(e.pitch, e.yaw);
            const size_t mark = FxCallMark();
            FxTickNow();
            const auto traces = FxCallsOf(FxKind::Trace, mark);
            const auto end = traces.size() == 1 ? FxVec(traces[0], FxLts::kEnd, kVecD) : std::array<double, 3>{};
            check(FxFmt("TR.03 end (%g,%g): along the view, not toward the pawn -- within %g of the hand-worked end",
                        e.pitch, e.yaw, e.tol).c_str(),
                  traces.size() == 1 && G1::Near(end, e), G1::VecStr(end));
        }
        FxCamRot(0.0, 0.0);
        Schlacht::SetPierceCount(G1::kPierceTwo);
        FxScene({ FxHitAt(kIdxWallA, G1::kTWallA), FxHitAt(kIdxWallB, G1::kTWallB) });
        {
            const size_t mark = FxCallMark();
            FxTickNow();
            const auto calls = FxCalls(mark);
            const auto pierced = FxCallsOf(FxKind::Trace, mark);
            check("TRACE setup: a pierce-2 tick through WallA and WallB traces twice and hides both",
                  pierced.size() == static_cast<size_t>(G1::kPierceTwo) && FxRecordNow() == FxSet({ g_wallA, g_wallB }),
                  G1::CallsStr(calls));
            check("TR.08 call order: a hitting tick is the two camera reads, its traces, then nothing but the hides",
                  G1::HitTickShape(calls, g_cam0, g_fnCamLoc, g_fnCamRot, kVecD, g_ltsD, pierced.size(),
                                   FxSet({ g_wallA, g_wallB })),
                  G1::CallsStr(calls));
            const auto all = FxCallsOf(FxKind::Trace);   // since the reset: the empty, turned and pierced ticks
            const auto byPawn = [](const FxCall& c) { return FxRd<uintptr_t>(c, FxLts::kWorldContext) == g_hero; };
            check("TR.01 world context: every trace's WorldContextObject (@0x00) is BP_Hero_C_0, the resolved pawn",
                  G1::Every(all, byPawn), G1::FirstFail(all, byPawn));
            const auto channel0 = [](const FxCall& c) { return FxRd<uint8_t>(c, FxLts::kChannel) == 0; };
            check("TR.04 channel: the TraceChannel byte (@0x38) is 0 on every trace", G1::Every(all, channel0),
                  G1::FirstFail(all, channel0));
            const auto flags = [](const FxCall& c) {
                return FxRd<uint8_t>(c, FxLts::kComplex) == 0 && FxRd<uint8_t>(c, FxLts::kIgnoreSelf) == 1;
            };
            check("TR.05 flags: bTraceComplex (@0x39) is 0 and bIgnoreSelf (@0x140) is 1 on every trace",
                  G1::Every(all, flags), G1::FirstFail(all, flags));
            const auto inputsOnly = [](const FxCall& c) { return G1::OnlyInputs(c); };
            check("TR.06 fresh buffer: on every trace, each byte outside WorldContextObject, Start, End and "
                  "bIgnoreSelf is 0 -- ActorsToIgnore, DrawDebugType, the whole OutHit, ReturnValue",
                  G1::Every(all, inputsOnly), G1::FirstFail(all, inputsOnly));
            check("TR.06 fresh buffer: ...the pierce-2 tick's second trace included -- the first one's answer is not "
                  "carried into it",
                  pierced.size() == static_cast<size_t>(G1::kPierceTwo) && G1::OnlyInputs(pierced[1]),
                  pierced.size() > 1 ? G1::CallDesc(pierced[1]) : G1::CallsStr(pierced));
            const auto sized = [](const FxCall& c) { return c.size == static_cast<uint32_t>(FxLts::kParmsSize); };
            check("TR.07 size: every LTS-D trace is 0x148 bytes, its ParmsSize", G1::Every(all, sized),
                  G1::FirstFail(all, sized));
        }

        // The UE4 world: 12-byte FVectors in LineTraceSingle and from CamF_0's getters, re-enabled after the swap.
        FxReset();
        FxUE4World();
        check("TRACE setup: the UE4 world (LTS-F, CamF_0) enables", FxEnableSync() == G1::kRcOn);
        {
            const size_t mark = FxCallMark();
            FxTickNow();
            const auto calls = FxCalls(mark);
            check("TR.08 call order (UE4): exactly GetCameraLocation(CamF_0), GetCameraRotation(CamF_0), "
                  "LineTraceSingle -- the camera calls 12 bytes",
                  calls.size() == G1::kEmptyTickCalls
                      && G1::CamCall(calls[0], FxKind::CamLoc, g_camF0, g_fnCamLocF, kVecF)
                      && G1::CamCall(calls[1], FxKind::CamRot, g_camF0, g_fnCamRotF, kVecF)
                      && G1::KslTrace(calls[2], g_ltsF),
                  G1::CallsStr(calls));
            const auto traces = FxCallsOf(FxKind::Trace, mark);
            const float camF[3] = { static_cast<float>(kCamLocDefault[0]), static_cast<float>(kCamLocDefault[1]),
                                    static_cast<float>(kCamLocDefault[2]) };
            const auto start = traces.empty() ? std::array<double, 3>{} : FxVec(traces[0], FxLtsF::kStart, kVecF);
            check("TR.02 start (UE4): the first trace starts at the camera, exactly (3 floats @0x08)",
                  !traces.empty() && G1::ExactlyF(start, camF), G1::VecStr(start));
            const auto end = traces.empty() ? std::array<double, 3>{} : FxVec(traces[0], FxLtsF::kEnd, kVecF);
            check("TR.03 end (UE4, 0,0): floats (100100.5, 200.25, 300.125), exactly",
                  !traces.empty() && G1::ExactlyF(end, G1::kEndF0), G1::VecStr(end));
        }
        Schlacht::SetPierceCount(G1::kPierceTwo);
        FxScene({ FxHitAt(kIdxWallA, G1::kTWallA), FxHitAt(kIdxWallB, G1::kTWallB) });
        {
            const size_t mark = FxCallMark();
            FxTickNow();
            const auto pierced = FxCallsOf(FxKind::Trace, mark);
            const auto all = FxCallsOf(FxKind::Trace);
            const auto ignoreSelf = [](const FxCall& c) { return FxRd<uint8_t>(c, FxLtsF::kIgnoreSelf) == 1; };
            check("IEQ any case: the byte at 0xC8, the parameter spelled \"bignoreself\", is 1 on every trace -- a "
                  "parameter name matches regardless of case",
                  pierced.size() == static_cast<size_t>(G1::kPierceTwo) && G1::Every(all, ignoreSelf),
                  G1::FirstFail(all, ignoreSelf));
            const auto byPawn = [](const FxCall& c) { return FxRd<uintptr_t>(c, FxLtsF::kWorldContext) == g_hero; };
            check("TR.01 world context (UE4): every trace's WorldContextObject is BP_Hero_C_0",
                  G1::Every(all, byPawn), G1::FirstFail(all, byPawn));
        }

        // A tail read short of where the chain ends: the buffer must reach ReturnValue anyway.
        FxReset();
        FxKsl(g_ltsShort);
        check("TRACE setup: LTS-D-Short (tail 0x130, chain end 0x142) enables", FxEnableSync() == G1::kRcOn);
        FxScene({ FxHitAt(kIdxWallA, G1::kTWallA) });
        {
            const size_t mark = FxCallMark();
            FxTickNow();
            const auto traces = FxCallsOf(FxKind::Trace, mark);
            const auto chainEnd = [](const FxCall& c) {
                return c.ufunc == g_ltsShort && c.size == static_cast<uint32_t>(FxLts::kChainEnd);
            };
            check("TR.07 size (short tail): every trace is 0x142 bytes -- where the chain ends, not the 0x130 the tail "
                  "reads",
                  G1::Every(traces, chainEnd), G1::FirstFail(traces, chainEnd));
            check("TR.07 size (short tail): WallA is hidden -- ReturnValue @0x141 was read inside the buffer",
                  FxHides(g_wallA, G1::kHideTrue, mark) == 1 && FxRecordNow() == FxSet({ g_wallA }),
                  G1::CallsStr(FxCalls(mark)));
        }

        // CAM-SHORT: the getters' and the setter's buffers follow the same rule as the trace's.
        FxReset();
        FxSetClass(g_cam0, g_cCamShort);
        FxActorFns(g_fnSetHiddenShort);
        check("CAM-SHORT setup: Cam_0 as CameraManagerShort (tails 0x10) and Actor with the ParmsSize-0 setter enable",
              FxEnableSync() == G1::kRcOn);
        FxScene({ FxHitAt(kIdxWallA, G1::kTWallA) });
        {
            const size_t mark = FxCallMark();
            FxTickNow();
            const auto calls = FxCalls(mark);
            std::vector<FxCall> cams;
            for (const auto& c : calls)
                if (c.kind == FxKind::CamLoc || c.kind == FxKind::CamRot) cams.push_back(c);
            check("CAM-SHORT getters: both camera calls arrive 24 bytes -- the return's end, not the 0x10 tail",
                  cams.size() == G1::kCamReads
                      && G1::CamCall(cams[0], FxKind::CamLoc, g_cam0, g_fnCamLocShort, kVecD)
                      && G1::CamCall(cams[1], FxKind::CamRot, g_cam0, g_fnCamRotShort, kVecD),
                  G1::CallsStr(calls));
            const Schlacht::SeeThroughStatus s = FxStatus();
            check("CAM-SHORT getters: the trace runs, and the tick reports code 0 with a target",
                  FxCount(FxKind::Trace, mark) == 1 && s.code == Schlacht::STR_OK && s.hasTarget,
                  FxStatusStr(s) + " " + G1::CallsStr(calls));
            const auto hides = FxCallsOf(FxKind::Hide, mark);
            check("CAM-SHORT setter: the one hide arrives 1 byte -- bNewHidden's end, not the 0 tail -- carrying true",
                  hides.size() == 1 && hides[0].instance == g_wallA && hides[0].ufunc == g_fnSetHiddenShort
                      && hides[0].size == static_cast<uint32_t>(G1::kBoolBytes)
                      && FxHideByte(hides[0]) == G1::kHideTrue,
                  G1::CallsStr(hides));
            check("CAM-SHORT setter: WallA is hidden -- in the game and in the record",
                  FxGameHidden(g_wallA) == G1::kHideTrue && FxRecordNow() == FxSet({ g_wallA }),
                  FxS(FxGameHidden(g_wallA)) + " " + FxSetStr(FxRecordNow()));
        }
        FxReset();   // the next group starts from the default world whatever its first block does
    }
}

// ══ RESOLVE, HIT, DIFF ═══════════════════════════════════════════════════════════════════════════════════════════
// The chain a tick resolves before it traces, the FHitResult read, and the hide / un-hide diff with its pierce loop.
// Every tick here runs on main (FxTickNow) against a copy that was enabled and then had its worker stopped, so the
// calls and the record a check reads are that one tick's. In HIT, HIT.05 and HIT.06 run first: the unresolvable-hit
// dump they read fires once per process.

// Where a scene puts each hit along the camera's +X axis.
constexpr double kG2PawnT = 100.0;       // the local pawn itself, nearest of all
constexpr double kG2EnemyT = 300.0;      // a Pawn that is not the local one
constexpr double kG2NpcT = 400.0;        // a Character
constexpr double kG2NearT = 500.0;       // the first occluder
constexpr double kG2FarT = 800.0;        // the second
constexpr double kG2FarthestT = 900.0;   // the third
constexpr double kG2WallStep = 100.0;    // the twelve-wall scene: one wall every 100 units from kG2NearT on
// The second trace's Start after a hit at kG2NearT: that hit's point, one trace step further along +X.
constexpr double kG2SecondStartX = 602.5;
static_assert(kCamLocDefault[0] + kG2NearT + Grimoire::SCHLACHT_TRACE_STEP == kG2SecondStartX,
              "the camera's x, the first hit's distance and one step make the second Start");
// A tick whose only hit lies at kG2NearT traces twice: the hit, then a miss from just past it.
constexpr int kG2HitThenMiss = 2;
// Pierce depths: the floor a smaller ask is raised to, two in-range depths, and one far above the ceiling.
constexpr int32_t kG2PierceFloor = 1;
constexpr int32_t kG2PierceNegative = -5;
constexpr int32_t kG2PierceTwo = 2;
constexpr int32_t kG2PierceMid = 3;
constexpr int32_t kG2PierceFarAbove = 99;
static_assert(kWalls > Grimoire::SCHLACHT_PIERCE_MAX, "the wall scene holds more walls than one tick may hide");
// GameInstance.LocalPlayers as the good world has it: one element, LP_0.
constexpr int32_t kG2LocalPlayers = 1;

// ── helpers (G2-prefixed: every group's file shares one translation unit) ──
static const char* G2KindName(FxKind k) {
    switch (k) {
    case FxKind::Trace: return "Trace";
    case FxKind::CamLoc: return "CamLoc";
    case FxKind::CamRot: return "CamRot";
    case FxKind::Hide: return "Hide";
    case FxKind::Other: break;
    }
    return "Other";
}
// The calls since `from`, for a failing check's `got`.
static std::string G2Calls(size_t from) {
    std::string out;
    for (const auto& c : FxCalls(from)) {
        out += std::string(" ") + G2KindName(c.kind) + "(" + FxNameOf(c.instance);
        if (c.kind == FxKind::Hide) out += FxFmt(", %d", FxHideByte(c));
        out += FxFmt(")=%d", c.rc);
    }
    return out.empty() ? std::string(" (none)") : out;
}
static std::string G2Got(size_t from) { return FxStatusStr(FxStatus()) + "   calls:" + G2Calls(from); }
static std::string G2VecStr(const std::array<double, 3>& v) { return FxFmt("(%.6f, %.6f, %.6f) ", v[0], v[1], v[2]); }
static size_t G2Off(int32_t off) { return static_cast<size_t>(off); }
static int G2N(const std::vector<FxCall>& calls) { return static_cast<int>(calls.size()); }
// WorldContextObject sits at +0 in LineTraceSingle's UE5 and UE4 blocks alike.
static_assert(FxLts::kWorldContext == FxLtsF::kWorldContext, "one reader serves both worlds");
static uintptr_t G2Wco(const FxCall& c) { return FxRd<uintptr_t>(c, G2Off(FxLts::kWorldContext)); }
// The `index`th trace's Start at `width`; zeros when there is no such trace, which no expected Start equals.
static std::array<double, 3> G2StartOf(const std::vector<FxCall>& traces, size_t index, int32_t startOff,
                                       int32_t width) {
    return index < traces.size() ? FxVec(traces[index], G2Off(startOff), width) : std::array<double, 3>{};
}
// At least one trace since `from`, and every one satisfies `pred`: a check over no trace at all proves nothing.
static bool G2EveryTrace(size_t from, const std::function<bool(const FxCall&)>& pred) {
    const std::vector<FxCall> traces = FxCallsOf(FxKind::Trace, from);
    if (traces.empty()) return false;
    for (const auto& c : traces)
        if (!pred(c)) return false;
    return true;
}
// The record is exactly `want`, and hiddenCount agrees with it.
static bool G2RecordIs(const std::unordered_set<uintptr_t>& want) {
    return FxRecordNow() == want && FxStatus().hiddenCount == static_cast<int32_t>(want.size());
}
// One GetCameraLocation and one GetCameraRotation since `from`, both on `cam`.
static bool G2CamCallsOn(size_t from, uintptr_t cam) {
    const std::vector<FxCall> loc = FxCallsOf(FxKind::CamLoc, from);
    const std::vector<FxCall> rot = FxCallsOf(FxKind::CamRot, from);
    return loc.size() == 1 && rot.size() == 1 && loc[0].instance == cam && rot[0].instance == cam;
}

// On, with no worker ticking. An enable that did not return 1 is a fixture failure: every check after it would read a
// copy that is off.
static void G2Enable(const char* who) {
    const int32_t rc = FxEnableSync();
    if (rc != 1) check(FxFmt("FIXTURE %s: the enable returned 1", who).c_str(), false, FxS(rc));
}
// A clean slate in the default world, then on at `pierce`. A LineTraceSingle variant (`lts`) is swapped in between the
// reset and the enable, because CollectOccluders resolves the function once per enable.
static void G2Start(const char* who, int32_t pierce = Grimoire::SCHLACHT_PIERCE_DEFAULT, uintptr_t lts = 0) {
    FxReset();
    if (lts) FxKsl(lts);
    G2Enable(who);
    Schlacht::SetPierceCount(pierce);
}
// The same in the UE4 world: LineTraceSingle with float FVectors and HitResult4, and a camera that returns floats.
static void G2StartUE4(const char* who, int32_t pierce = Grimoire::SCHLACHT_PIERCE_DEFAULT) {
    FxReset();
    FxUE4World();
    G2Enable(who);
    Schlacht::SetPierceCount(pierce);
}
// RESOLVE's scene: WallA alone, in front. A good tick hides it, so a failing tick after it has a record to keep.
static void G2GoodTick(const char* who) {
    FxScene({ FxHitAt(kIdxWallA, kG2NearT) });
    FxTickNow();
    const Schlacht::SeeThroughStatus st = FxStatus();
    if (st.code != Schlacht::STR_OK || !st.hasTarget || FxRecordNow() != FxSet({ g_wallA }))
        check(FxFmt("FIXTURE %s: a good tick hid WallA", who).c_str(), false, FxStatusStr(st));
}
// One tick that must stop before its first call: `code`, no target, zero invokes, the record {WallA} unchanged.
static void G2StopsBeforeAnyCall(const std::string& label, int32_t code) {
    const size_t mark = FxCallMark();
    FxTickNow();
    const Schlacht::SeeThroughStatus st = FxStatus();
    check(label.c_str(),
          st.code == code && !st.hasTarget && FxCountAll(mark) == 0 && st.hiddenCount == 1
              && FxRecordNow() == FxSet({ g_wallA }),
          G2Got(mark));
}
// One world pointer a check breaks, and how to put it back.
struct G2Swap {
    const char* label;
    std::function<void()> apply, undo;
};

// The unresolvable-hit dump's sub-field lines, as HitResult5 lays its members out.
struct G2SubField {
    const char* name;
    const char* type;
    int32_t off, size;
};
static const G2SubField kG2Hit5Fields[] = {
    { "FaceIndex", kIntProperty, FxHit5::kFaceIndex, static_cast<int32_t>(sizeof(int32_t)) },
    { "Time", kFloatProperty, FxHit5::kTime, static_cast<int32_t>(sizeof(float)) },
    { "Location", kStructProperty, FxHit5::kLocation, kVecD },
    { "ImpactPoint", kStructProperty, FxHit5::kImpact, kVecD },
    { "HitObjectHandle", kStructProperty, FxHit5::kHandle, FxHit5::kHandleBytes },
    { "Component", kWeakObjectProperty, FxHit5::kComponent, kPtrBytes },
};
// From `from` on: the See-through lines right after the W `head` are exactly kG2Hit5Fields' lines, each an I, in field
// order, and no other sub-field line was written.
static bool G2DumpInOrder(size_t from, const std::string& head) {
    std::vector<FxLogLine> lines;
    for (const auto& l : FxLogs(from))
        if (l.cat == kSeeThruCat) lines.push_back(l);
    size_t at = 0;
    while (at < lines.size() && !(lines[at].lvl == 'W' && lines[at].text == head)) ++at;
    const size_t n = sizeof(kG2Hit5Fields) / sizeof(kG2Hit5Fields[0]);
    if (at + n >= lines.size()) return false;
    size_t i = 0;
    for (const G2SubField& f : kG2Hit5Fields) {
        const FxLogLine& l = lines[at + 1 + i++];
        if (l.lvl != 'I' || l.text != FxFmt("  FHitResult.%s : %s  @+%d sz=%d", f.name, f.type, f.off, f.size))
            return false;
    }
    return FxLogPrefix("  FHitResult.", 0, kSeeThruCat, from) == static_cast<int>(n);
}

// ── RESOLVE ──────────────────────────────────────────────────────────────────────────────────────────────────────
static void G2Resolve() {
    blk("RESOLVE - the chain a tick resolves before it traces: the game thread, GWorld, the controller, the pawn, "
        "the camera");

    {   // RES.01 (T1): an unresponsive game thread stops the tick before any call and keeps the record.
        G2Start("RES.01");
        FxScene({ FxHitAt(kIdxWallA, kG2NearT) });
        size_t mark = FxCallMark();
        FxTickNow();
        Schlacht::SeeThroughStatus st = FxStatus();
        check("RES.01 good tick: code 0 with a target, WallA hidden once and recorded",
              st.code == Schlacht::STR_OK && st.hasTarget && FxHides(g_wallA, 1, mark) == 1
                  && G2RecordIs(FxSet({ g_wallA })),
              G2Got(mark));
        g_responsive.store(false);
        mark = FxCallMark();
        FxTickNow();
        st = FxStatus();
        check("RES.01 unresponsive: code 0 and no target, hiddenCount 1, the record still {WallA}",
              st.code == Schlacht::STR_OK && !st.hasTarget && st.hiddenCount == 1
                  && FxRecordNow() == FxSet({ g_wallA }),
              FxStatusStr(st));
        check("RES.01 unresponsive: zero invokes of any kind", FxCountAll(mark) == 0, G2Calls(mark));
        g_responsive.store(true);
    }

    {   // RES.02 (T2): no GWorld.
        G2Start("RES.02");
        G2GoodTick("RES.02");
        g_cachedGWorld = 0;
        G2StopsBeforeAnyCall("RES.02 no GWorld: code -1 (not initialised), no target, zero invokes, the record "
                             "{WallA} unchanged",
                             Schlacht::STR_ERR_NOT_INIT);
        g_cachedGWorld = FxAddr(&g_gworldSlot);
    }

    {   // RES.03: a GWorld slot that cannot be read reads as no world, through the guarded read.
        G2Start("RES.03");
        G2GoodTick("RES.03");
        uintptr_t probe = 0;
        check("RES.03 fixture: the reserved page exists and cannot be read",
              g_unreadableWorld != 0 && !Macht::ReadSafe(g_unreadableWorld, probe), FxHex(g_unreadableWorld));
        g_cachedGWorld = g_unreadableWorld;
        G2StopsBeforeAnyCall("RES.03 unreadable GWorld: the tick returns without a fault -- code -1, no target, zero "
                             "invokes, the record {WallA} unchanged",
                             Schlacht::STR_ERR_NOT_INIT);
        g_cachedGWorld = FxAddr(&g_gworldSlot);
    }

    {   // RES.04 (T3): every link from the world to the controller.
        G2Start("RES.04");
        G2GoodTick("RES.04");
        const G2Swap links[] = {
            { "RES.04 no OwningGameInstance: code -2, no target, zero invokes, the record {WallA} unchanged",
              [] { FxSetPtr(g_world0, FxOff::kOwningGameInstance, 0); },
              [] { FxSetPtr(g_world0, FxOff::kOwningGameInstance, g_gi0); } },
            { "RES.04 LocalPlayers Num 0 with Data still set: code -2, no target, zero invokes, the record {WallA} "
              "unchanged",
              [] { FxPutTArray(g_gi0, FxOff::kLocalPlayers, FxAddr(g_lpArr), 0, kG2LocalPlayers); },
              [] { FxPutTArray(g_gi0, FxOff::kLocalPlayers, FxAddr(g_lpArr), kG2LocalPlayers, kG2LocalPlayers); } },
            { "RES.04 LocalPlayers[0] null: code -2, no target, zero invokes, the record {WallA} unchanged",
              [] { g_lpArr[0] = 0; },
              [] { g_lpArr[0] = g_lp0; } },
            { "RES.04 no PlayerController: code -2, no target, zero invokes, the record {WallA} unchanged",
              [] { FxSetPtr(g_lp0, FxOff::kPlayerController, 0); },
              [] { FxSetPtr(g_lp0, FxOff::kPlayerController, g_pc0); } },
        };
        for (const G2Swap& s : links) {
            s.apply();
            G2StopsBeforeAnyCall(s.label, Schlacht::STR_ERR_NO_PAWN);
            s.undo();
        }
    }

    {   // RES.05 (R1): a world with no OwningGameInstance. The "contains GameInstance" match must be an ObjectProperty.
        G2Start("RES.05");
        check("RES.05 fixture: without the type filter the fuzzy match would take the ArrayProperty "
              "GameInstanceSubsystems",
              Ubel::FindFieldOffset(g_cWorldFuzzy, "OwningGameInstance", "GameInstance") == FxOff::kSubsystems,
              FxS(Ubel::FindFieldOffset(g_cWorldFuzzy, "OwningGameInstance", "GameInstance")));
        FxScene({ FxHitAt(kIdxWallA, kG2NearT) });
        g_gworldSlot = g_world2;
        const size_t mark = FxCallMark();
        FxTickNow();
        const Schlacht::SeeThroughStatus st = FxStatus();
        check("RES.05 fuzzy GameInstance: code 0 with a target -- MyGameInstance, the ObjectProperty, was taken",
              st.code == Schlacht::STR_OK && st.hasTarget, G2Got(mark));
        check("RES.05 fuzzy GameInstance: the trace ran from BP_Hero_C_0 and WallA was hidden and recorded",
              G2EveryTrace(mark, [](const FxCall& c) { return G2Wco(c) == g_hero; })
                  && FxHides(g_wallA, 1, mark) == 1 && G2RecordIs(FxSet({ g_wallA })),
              G2Got(mark));
        g_gworldSlot = g_world0;
    }

    {   // RES.06 (R3): Pawn, else AcknowledgedPawn.
        G2Start("RES.06");
        FxScene({ FxHitAt(kIdxWallA, kG2NearT) });
        FxSetPtr(g_pc0, FxOff::kPawn, 0);   // AcknowledgedPawn still names BP_Hero_C_0
        const size_t mark = FxCallMark();
        FxTickNow();
        const Schlacht::SeeThroughStatus st = FxStatus();
        check("RES.06 Pawn null: code 0 -- AcknowledgedPawn taken, every trace's WorldContextObject is BP_Hero_C_0",
              st.code == Schlacht::STR_OK && st.hasTarget
                  && G2EveryTrace(mark, [](const FxCall& c) { return G2Wco(c) == g_hero; }),
              G2Got(mark));
        FxSetPtr(g_pc0, FxOff::kAckPawn, 0);
        G2StopsBeforeAnyCall("RES.06 Pawn and AcknowledgedPawn null: code -2, no target, zero invokes, the record "
                             "{WallA} unchanged",
                             Schlacht::STR_ERR_NO_PAWN);
        FxSetPtr(g_pc0, FxOff::kPawn, g_hero);
        FxSetPtr(g_pc0, FxOff::kAckPawn, g_hero);
    }

    {   // RES.07 (R2): a DebugCameraController hops to the controller it replaced, when it names one.
        G2Start("RES.07");
        FxScene({ FxHitAt(kIdxWallA, kG2NearT) });
        FxSetPtr(g_lp0, FxOff::kPlayerController, g_dbgPc0);   // its OriginalControllerRef names PC_0
        size_t mark = FxCallMark();
        FxTickNow();
        check("RES.07 debug camera: one GetCameraLocation and one GetCameraRotation, both on Cam_0 (PC_0's), not "
              "CamDbg_0",
              G2CamCallsOn(mark, g_cam0), G2Calls(mark));
        check("RES.07 debug camera: every trace's WorldContextObject is BP_Hero_C_0, PC_0's pawn",
              G2EveryTrace(mark, [](const FxCall& c) { return G2Wco(c) == g_hero; }), G2Calls(mark));
        FxSetPtr(g_dbgPc0, FxOff::kOriginalController, 0);
        mark = FxCallMark();
        FxTickNow();
        check("RES.07 no original controller: the debug controller is kept -- both camera calls on CamDbg_0",
              G2CamCallsOn(mark, g_camDbg0), G2Calls(mark));
        check("RES.07 no original controller: every trace's WorldContextObject is SpectatorPawn_0",
              G2EveryTrace(mark, [](const FxCall& c) { return G2Wco(c) == g_spectator; }), G2Calls(mark));
    }

    {   // RES.08 (T5): a camera that cannot be read stops the tick before it traces.
        G2Start("RES.08");
        G2GoodTick("RES.08");
        const G2Swap cams[] = {
            { "RES.08 no PlayerCameraManager: code -4, no target, zero invokes, the record {WallA} unchanged",
              [] { FxSetPtr(g_pc0, FxOff::kCameraManager, 0); },
              [] { FxSetPtr(g_pc0, FxOff::kCameraManager, g_cam0); } },
            { "RES.08 a camera without GetCameraLocation (CamNoLoc_0): code -4, no target, zero invokes, the record "
              "{WallA} unchanged",
              [] { FxSetPtr(g_pc0, FxOff::kCameraManager, g_camNoLoc0); },
              [] { FxSetPtr(g_pc0, FxOff::kCameraManager, g_cam0); } },
            { "RES.08 a GetCameraLocation with no return parameter (CamNoRet_0): code -4, no target, zero invokes, "
              "the record {WallA} unchanged",
              [] { FxSetPtr(g_pc0, FxOff::kCameraManager, g_camNoRet0); },
              [] { FxSetPtr(g_pc0, FxOff::kCameraManager, g_cam0); } },
        };
        for (const G2Swap& s : cams) {
            s.apply();
            G2StopsBeforeAnyCall(s.label, Schlacht::STR_ERR_NO_CAMERA);
            s.undo();
        }

        FxCamRcs(Stark::kInvokeTimedOutStillQueued, 0);
        size_t mark = FxCallMark();
        FxTickNow();
        Schlacht::SeeThroughStatus st = FxStatus();
        check("RES.08 GetCameraLocation did not run (-5): code -4, no target, exactly one camera call -- no rotation, "
              "no trace -- the record {WallA} unchanged",
              st.code == Schlacht::STR_ERR_NO_CAMERA && !st.hasTarget && FxCount(FxKind::CamLoc, mark) == 1
                  && FxCountAll(mark) == 1 && st.hiddenCount == 1 && FxRecordNow() == FxSet({ g_wallA }),
              G2Got(mark));
        FxCamRcs(0, Stark::kInvokeTimedOutStillQueued);
        mark = FxCallMark();
        FxTickNow();
        st = FxStatus();
        check("RES.08 GetCameraRotation did not run (-5): code -4, no target, exactly the two camera calls -- no "
              "trace -- the record {WallA} unchanged",
              st.code == Schlacht::STR_ERR_NO_CAMERA && !st.hasTarget && FxCount(FxKind::CamLoc, mark) == 1
                  && FxCount(FxKind::CamRot, mark) == 1 && FxCountAll(mark) == 2 && st.hiddenCount == 1
                  && FxRecordNow() == FxSet({ g_wallA }),
              G2Got(mark));
        FxCamRcs(0, 0);
    }

    {   // RES.09: a controller class with no Pawn field reads as no pawn. A field that cannot be found is never read
        // at offset -1, where these 8 bytes spell a live pawn.
        G2Start("RES.09");
        G2GoodTick("RES.09");
        FxPcNoPawn(true);
        uintptr_t lead = 0;
        memcpy(&lead, FxBytes(g_pc0) - 1, sizeof lead);
        check("RES.09 fixture: PlayerControllerNoPawn has neither Pawn nor AcknowledgedPawn but keeps "
              "PlayerCameraManager on Cam_0, and the 8 bytes at PC-1 spell BP_Enemy_C_0",
              Ubel::FindFieldOffset(g_cPcNoPawn, "Pawn") < 0
                  && Ubel::FindFieldOffset(g_cPcNoPawn, "AcknowledgedPawn") < 0
                  && Ubel::FindFieldOffset(g_cPcNoPawn, "PlayerCameraManager") == FxOff::kCameraManager
                  && FxGetPtr(g_pc0, FxOff::kCameraManager) == g_cam0 && lead == g_enemy,
              FxHex(lead));
        G2StopsBeforeAnyCall("RES.09 no Pawn field: code -2, no target, zero invokes, the record {WallA} unchanged "
                             "-- nothing read at PC-1",
                             Schlacht::STR_ERR_NO_PAWN);
        FxPcNoPawn(false);
    }
}

// ── HIT ──────────────────────────────────────────────────────────────────────────────────────────────────────────
static void G2Hit() {
    blk("HIT - reading the FHitResult: which member names the actor, the Outer hop, and the point the next trace "
        "starts from");

    {   // HIT.05, HIT.06 (L5): a hit whose members name nothing. First, because its dump fires once per process.
        G2Start("HIT.05");
        FxScene({ FxHitNone(kG2NearT) });
        const std::array<double, 3> past = { kG2SecondStartX, kCamLocDefault[1], kCamLocDefault[2] };
        const size_t logMark = FxLogMark();
        size_t mark = FxCallMark();
        FxTickNow();
        std::vector<FxCall> traces = FxCallsOf(FxKind::Trace, mark);
        const Schlacht::SeeThroughStatus st = FxStatus();
        check("HIT.05 unresolvable hit: exactly 2 traces -- the second starts one step past it and misses",
              G2N(traces) == kG2HitThenMiss && G2StartOf(traces, 1, FxLts::kStart, kVecD) == past,
              G2VecStr(G2StartOf(traces, 1, FxLts::kStart, kVecD)) + G2Calls(mark));
        check("HIT.05 unresolvable hit: zero SetActorHiddenInGame calls; code 0 with a target, hiddenCount 0",
              FxCount(FxKind::Hide, mark) == 0 && st.code == Schlacht::STR_OK && st.hasTarget && st.hiddenCount == 0
                  && FxRecordNow().empty(),
              G2Got(mark));

        const std::string dump = "SeeThrough: HIT but couldn't extract actor from FHitResult — sub-fields:";
        check("HIT.06 L5: exactly one W \"HIT but couldn't extract actor from FHitResult -- sub-fields:\"",
              FxLogCount(dump, 'W', kSeeThruCat, logMark) == 1, FxLogDump(logMark));
        check("HIT.06 L5: right after it, one I per HitResult5 field in field order, FaceIndex @+0 sz=4 through "
              "Component @+152 sz=8, and no other sub-field line",
              G2DumpInOrder(logMark, dump), FxLogDump(logMark));
        const size_t logMark2 = FxLogMark();
        mark = FxCallMark();
        FxTickNow();
        check("HIT.06 L5 once: a second unresolvable tick adds neither the W nor a sub-field line",
              FxCount(FxKind::Trace, mark) == kG2HitThenMiss
                  && FxLogPrefix("SeeThrough: HIT but", 0, kSeeThruCat, logMark2) == 0
                  && FxLogPrefix("  FHitResult.", 0, kSeeThruCat, logMark2) == 0,
              FxLogDump(logMark2) + G2Calls(mark));

        FxScene({ FxHitNone(kG2NearT), FxHitAt(kIdxWallB, kG2FarT) });
        mark = FxCallMark();
        FxTickNow();
        check("HIT.05 passed through: with WallB at 800 behind it, 2 traces, and WallB hidden and recorded",
              FxCount(FxKind::Trace, mark) == kG2HitThenMiss && FxHides(g_wallB, 1, mark) == 1
                  && FxCount(FxKind::Hide, mark) == 1 && G2RecordIs(FxSet({ g_wallB })),
              G2Got(mark));
    }

    {   // HIT.01 (UE 5.4): HitObjectHandle names a component; its Outer is the actor hidden.
        G2Start("HIT.01");
        FxScene({ FxHitHoh(kIdxSmc0, kG2NearT) });
        const size_t mark = FxCallMark();
        FxTickNow();
        check("HIT.01 component hit: SetActorHiddenInGame(WallC, 1) once -- StaticMeshComponent_0's Outer -- and no "
              "call on the component",
              FxHides(g_wallC, 1, mark) == 1 && FxCount(FxKind::Hide, mark) == 1 && FxHides(g_smc0, -1, mark) == 0,
              G2Calls(mark));
        check("HIT.01 component hit: the record {WallC}", G2RecordIs(FxSet({ g_wallC })), FxSetStr(FxRecordNow()));
    }

    {   // HIT.02: HitObjectHandle is tried before Component.
        G2Start("HIT.02");
        FxScene({ FxHitHoh(kIdxWallA, kG2NearT, kIdxSmc1) });
        const size_t mark = FxCallMark();
        FxTickNow();
        check("HIT.02 HitObjectHandle before Component: WallA hidden and recorded; WallB, Component's Outer, never "
              "called",
              FxHides(g_wallA, 1, mark) == 1 && FxHides(g_wallB, -1, mark) == 0 && G2RecordIs(FxSet({ g_wallA })),
              G2Got(mark));
    }

    {   // HIT.03: an empty HitObjectHandle falls through to Component, whose Outer is hidden.
        G2Start("HIT.03");
        FxScene({ FxHitHoh(0, kG2NearT, kIdxSmc1) });
        const size_t mark = FxCallMark();
        FxTickNow();
        check("HIT.03 Component fallback: HitObjectHandle 0, Component StaticMeshComponent_1 -- WallB hidden and "
              "recorded, no call on the component",
              FxHides(g_wallB, 1, mark) == 1 && FxCount(FxKind::Hide, mark) == 1 && G2RecordIs(FxSet({ g_wallB })),
              G2Got(mark));
    }

    {   // HIT.04 (UE4): HitResult4's Actor member, tried before Component.
        G2StartUE4("HIT.04");
        FxScene({ FxHitActor(kIdxWallD, kG2NearT) });
        size_t mark = FxCallMark();
        FxTickNow();
        check("HIT.04 UE4 world: every trace is LTS-F's, sized 0xD0",
              G2EveryTrace(mark, [](const FxCall& c) {
                  return c.ufunc == g_ltsF && c.size == static_cast<uint32_t>(FxLtsF::kParmsSize);
              }),
              G2Calls(mark));
        check("HIT.04 UE4 Actor member: Actor = 10 -- WallD hidden and recorded",
              FxHides(g_wallD, 1, mark) == 1 && FxCount(FxKind::Hide, mark) == 1 && G2RecordIs(FxSet({ g_wallD })),
              G2Got(mark));
        G2StartUE4("HIT.04");
        FxScene({ FxHitActor(kIdxWallA, kG2NearT, kIdxSmc1) });
        mark = FxCallMark();
        FxTickNow();
        check("HIT.04 UE4 Actor before Component: Actor = 7 with Component = 20 -- WallA hidden and recorded, WallB "
              "never called",
              FxHides(g_wallA, 1, mark) == 1 && FxHides(g_wallB, -1, mark) == 0 && G2RecordIs(FxSet({ g_wallA })),
              G2Got(mark));
    }

    {   // HIT.07: the next trace starts one step past ImpactPoint, else past Location.
        const std::array<double, 3> want = { kG2SecondStartX, kCamLocDefault[1], kCamLocDefault[2] };
        G2Start("HIT.07(a)", kG2PierceTwo);
        FxScene({ FxHitAt(kIdxWallA, kG2NearT), FxHitAt(kIdxWallB, kG2FarT) });
        size_t mark = FxCallMark();
        FxTickNow();
        std::vector<FxCall> traces = FxCallsOf(FxKind::Trace, mark);
        check("HIT.07(a) ImpactPoint: the second trace's Start is (602.5, 200.25, 300.125) -- ImpactPoint plus one "
              "step, not Location, which lies 10 short",
              G2N(traces) == kG2PierceTwo && G2StartOf(traces, 1, FxLts::kStart, kVecD) == want,
              G2VecStr(G2StartOf(traces, 1, FxLts::kStart, kVecD)) + G2Calls(mark));
        G2Start("HIT.07(b)", kG2PierceTwo, g_ltsNoImpact);
        FxScene({ FxHitAt(kIdxWallA, kG2NearT), FxHitAt(kIdxWallB, kG2FarT) });
        mark = FxCallMark();
        FxTickNow();
        traces = FxCallsOf(FxKind::Trace, mark);
        check("HIT.07(b) no ImpactPoint: every trace is LTS-D-NoImpact's, and the second Start is (602.5, 200.25, "
              "300.125) -- from Location",
              G2EveryTrace(mark, [](const FxCall& c) { return c.ufunc == g_ltsNoImpact; })
                  && G2N(traces) == kG2PierceTwo && G2StartOf(traces, 1, FxLts::kStart, kVecD) == want,
              G2VecStr(G2StartOf(traces, 1, FxLts::kStart, kVecD)) + G2Calls(mark));
        check("HIT.07(b) no ImpactPoint: the record {WallA, WallB}", G2RecordIs(FxSet({ g_wallA, g_wallB })),
              FxSetStr(FxRecordNow()));
    }

    {   // HIT.08: with no point to step from, the pierce loop stops.
        G2Start("HIT.08", kG2PierceMid, g_ltsNoPoint);
        FxScene({ FxHitAt(kIdxWallA, kG2NearT), FxHitAt(kIdxWallB, kG2FarT), FxHitAt(kIdxWallC, kG2FarthestT) });
        const size_t mark = FxCallMark();
        FxTickNow();
        check("HIT.08 neither ImpactPoint nor Location: exactly 1 trace at pierce 3 -- the loop cannot step, so it "
              "stops -- and the record {WallA}",
              G2EveryTrace(mark, [](const FxCall& c) { return c.ufunc == g_ltsNoPoint; })
                  && FxCount(FxKind::Trace, mark) == 1 && G2RecordIs(FxSet({ g_wallA })),
              G2Got(mark));
    }

    {   // HIT.09: the Outer hop reaches three objects past the hit, and no further.
        G2Start("HIT.09(a)");
        FxScene({ FxHitHoh(kIdxSub3, kG2NearT) });
        size_t mark = FxCallMark();
        FxTickNow();
        check("HIT.09(a) three hops: SubComp3 -> SubComp2 -> SubComp1 -> WallD -- WallD hidden and recorded, no "
              "component called",
              FxHides(g_wallD, 1, mark) == 1 && FxCount(FxKind::Hide, mark) == 1 && G2RecordIs(FxSet({ g_wallD })),
              G2Got(mark));
        G2Start("HIT.09(b)");
        FxScene({ FxHitHoh(kIdxSub4, kG2NearT) });
        mark = FxCallMark();
        FxTickNow();
        check("HIT.09(b) four hops: SubComp4's actor lies past the bound -- it traced, and made zero "
              "SetActorHiddenInGame calls; the record {}",
              FxCount(FxKind::Trace, mark) == kG2HitThenMiss && FxCount(FxKind::Hide, mark) == 0
                  && FxRecordNow().empty() && FxStatus().hiddenCount == 0,
              G2Got(mark));
    }

    {   // HIT.10 (UE4): the step is taken, and written, in float FVectors.
        G2StartUE4("HIT.10", kG2PierceTwo);
        FxScene({ FxHitAt(kIdxWallA, kG2NearT), FxHitAt(kIdxWallB, kG2FarT) });
        const std::array<double, 3> want = { kG2SecondStartX, kCamLocDefault[1], kCamLocDefault[2] };
        const size_t mark = FxCallMark();
        FxTickNow();
        const std::vector<FxCall> traces = FxCallsOf(FxKind::Trace, mark);
        check("HIT.10 UE4 pierce 2: the second trace's Start floats are (602.5f, 200.25f, 300.125f)",
              G2N(traces) == kG2PierceTwo && G2StartOf(traces, 1, FxLtsF::kStart, kVecF) == want,
              G2VecStr(G2StartOf(traces, 1, FxLtsF::kStart, kVecF)) + G2Calls(mark));
    }
}

// ── DIFF ─────────────────────────────────────────────────────────────────────────────────────────────────────────
static void G2Diff() {
    blk("DIFF - the hide / un-hide diff and the pierce loop: hidden once, kept without a call, un-hidden when gone, "
        "pawns passed through, the depth clamped");

    {   // DIFF.01 - DIFF.03: hide, keep, swap.
        G2Start("DIFF.01");
        FxScene({ FxHitAt(kIdxWallA, kG2NearT) });
        size_t mark = FxCallMark();
        FxTickNow();
        std::vector<FxCall> hides = FxCallsOf(FxKind::Hide, mark);
        check("DIFF.01 hide: exactly one SetActorHiddenInGame(WallA, 1) -- the setter found on Actor, up "
              "StaticMeshActor's chain",
              hides.size() == 1 && hides[0].instance == g_wallA && FxHideByte(hides[0]) == 1
                  && hides[0].ufunc == g_fnSetHidden,
              G2Calls(mark));
        check("DIFF.01 hide: the game's hidden flag for WallA is set", FxGameHidden(g_wallA) == 1,
              FxS(FxGameHidden(g_wallA)));
        Schlacht::SeeThroughStatus st = FxStatus();
        check("DIFF.01 status: code 0, a target, hiddenCount 1, hiddenActors {WallA}",
              st.code == Schlacht::STR_OK && st.hasTarget && st.hiddenCount == 1 && st.hiddenActors.size() == 1
                  && FxSetOf(st.hiddenActors) == FxSet({ g_wallA }),
              FxStatusStr(st));
        check("INV.01 tick hide (DIFF.01): the hide found s_mutex free -- no lock held across the invoke",
              hides.size() == 1 && hides[0].mutexFree, G2Calls(mark));

        mark = FxCallMark();
        FxTickNow();
        st = FxStatus();
        check("DIFF.02 still occluding: the tick traced, made zero SetActorHiddenInGame calls, and the record is "
              "still {WallA}",
              FxCount(FxKind::Trace, mark) == 1 && FxCount(FxKind::Hide, mark) == 0 && st.code == Schlacht::STR_OK
                  && st.hasTarget && G2RecordIs(FxSet({ g_wallA })),
              G2Got(mark));

        FxScene({ FxHitAt(kIdxWallB, kG2NearT) });   // the view moved: WallB in front, WallA out of the way
        mark = FxCallMark();
        FxTickNow();
        hides = FxCallsOf(FxKind::Hide, mark);
        check("DIFF.03 view changed: exactly two hide calls, SetActorHiddenInGame(WallA, 0) and (WallB, 1)",
              hides.size() == 2 && FxHides(g_wallA, 0, mark) == 1 && FxHides(g_wallB, 1, mark) == 1, G2Calls(mark));
        check("DIFF.03 view changed: the game's flags read WallA shown, WallB hidden",
              FxGameHidden(g_wallA) == 0 && FxGameHidden(g_wallB) == 1,
              FxFmt("A=%d B=%d", FxGameHidden(g_wallA), FxGameHidden(g_wallB)));
        check("DIFF.03 view changed: the record {WallB}, and nothing queued to un-hide",
              G2RecordIs(FxSet({ g_wallB })) && FxQueuedNow().empty(),
              FxSetStr(FxRecordNow()) + " queued " + FxSetStr(FxQueuedNow()));
        bool unhideFree = false, unhideSeen = false;
        for (const FxCall& c : hides)
            if (c.instance == g_wallA && FxHideByte(c) == 0) {
                unhideSeen = true;
                unhideFree = c.mutexFree;
            }
        check("INV.01 tick un-hide (DIFF.03): the un-hide found s_mutex free", unhideSeen && unhideFree,
              G2Calls(mark));
    }

    {   // DIFF.04: pawns on the ray are passed through, and none uses up the depth.
        G2Start("DIFF.04");
        FxScene({ FxHitAt(kIdxHero, kG2PawnT), FxHitAt(kIdxEnemy, kG2EnemyT), FxHitAt(kIdxNpc, kG2NpcT),
                  FxHitAt(kIdxWallA, kG2NearT) });
        const size_t mark = FxCallMark();
        FxTickNow();
        check("DIFF.04 pawns passed through: the record {WallA}", G2RecordIs(FxSet({ g_wallA })),
              FxSetStr(FxRecordNow()));
        check("DIFF.04 pawns passed through: no SetActorHiddenInGame on BP_Hero_C_0 (the pawn itself), BP_Enemy_C_0 "
              "(a Pawn) or BP_Npc_C_0 (a Character)",
              FxHides(g_hero, -1, mark) + FxHides(g_enemy, -1, mark) + FxHides(g_npc, -1, mark) == 0, G2Calls(mark));
        check("DIFF.04 pawns passed through: exactly 4 traces at pierce 1 -- one per pawn, then WallA",
              FxCount(FxKind::Trace, mark) == 4, G2Calls(mark));
    }

    {   // DIFF.05: a Pawn in front does not use up a depth of two.
        G2Start("DIFF.05", kG2PierceTwo);
        FxScene({ FxHitAt(kIdxEnemy, kG2EnemyT), FxHitAt(kIdxWallA, kG2NearT), FxHitAt(kIdxWallB, kG2FarT),
                  FxHitAt(kIdxWallC, kG2FarthestT) });
        const size_t mark = FxCallMark();
        FxTickNow();
        check("DIFF.05 pierce 2 past a Pawn: the record {WallA, WallB} after exactly 3 traces; WallC and the Pawn "
              "never called",
              G2RecordIs(FxSet({ g_wallA, g_wallB })) && FxCount(FxKind::Trace, mark) == 3
                  && FxHides(g_wallC, -1, mark) == 0 && FxHides(g_enemy, -1, mark) == 0,
              G2Got(mark));
    }

    {   // DIFF.06: pierce 2, two walls.
        G2Start("DIFF.06", kG2PierceTwo);
        FxScene({ FxHitAt(kIdxWallA, kG2NearT), FxHitAt(kIdxWallB, kG2FarT) });
        const size_t mark = FxCallMark();
        FxTickNow();
        const std::vector<FxCall> traces = FxCallsOf(FxKind::Trace, mark);
        check("DIFF.06 pierce 2: the record {WallA, WallB} after exactly 2 traces",
              G2RecordIs(FxSet({ g_wallA, g_wallB })) && G2N(traces) == kG2PierceTwo, G2Got(mark));
        const std::array<double, 3> second = G2StartOf(traces, 1, FxLts::kStart, kVecD);
        check("DIFF.06 pierce 2: the second trace's Start.x is 602.5 -- WallA's impact point plus one step",
              G2N(traces) == kG2PierceTwo && second[0] == kG2SecondStartX, G2VecStr(second));
    }

    {   // DIFF.07 (K1, T6): the depth is clamped to [1, the ceiling] and read on the next tick.
        G2Start("DIFF.07");
        struct Clamp {
            int32_t asked, want;
        };
        const Clamp clamps[] = {
            { 0, kG2PierceFloor },
            { kG2PierceNegative, kG2PierceFloor },
            { Grimoire::SCHLACHT_PIERCE_MAX + 1, Grimoire::SCHLACHT_PIERCE_MAX },
            { Grimoire::SCHLACHT_PIERCE_MAX, Grimoire::SCHLACHT_PIERCE_MAX },
            { kG2PierceMid, kG2PierceMid },
        };
        for (const Clamp& c : clamps) {
            Schlacht::SetPierceCount(c.asked);
            const int32_t got = FxStatus().pierceCount;
            check(FxFmt("DIFF.07 clamp: SetPierceCount(%d) reads back as pierceCount %d", c.asked, c.want).c_str(),
                  got == c.want, FxS(got));
        }

        std::vector<FxSceneHit> walls;
        for (int i = 0; i < kWalls; ++i) walls.push_back(FxHitAt(kIdxWallA + i, kG2NearT + kG2WallStep * i));
        FxScene(walls);
        std::unordered_set<uintptr_t> nearest;
        bool eachOnce = true;
        size_t mark = FxCallMark();
        FxTickNow();   // pierce is kG2PierceMid, the last clamp asked
        for (int i = 0; i < kG2PierceMid; ++i) {
            nearest.insert(g_wall[i]);
            eachOnce = eachOnce && FxHides(g_wall[i], 1, mark) == 1;
        }
        check("DIFF.07 pierce 3 over 12 walls: exactly the nearest 3 hidden (once each) and recorded, with 3 traces",
              G2RecordIs(nearest) && eachOnce && FxCount(FxKind::Hide, mark) == kG2PierceMid
                  && FxCount(FxKind::Trace, mark) == kG2PierceMid,
              G2Got(mark));

        Schlacht::SetPierceCount(kG2PierceFarAbove);
        const int32_t clamped = FxStatus().pierceCount;
        check("DIFF.07 clamp: SetPierceCount(99) reads back as pierceCount 10",
              clamped == Grimoire::SCHLACHT_PIERCE_MAX, FxS(clamped));
        for (int i = kG2PierceMid; i < Grimoire::SCHLACHT_PIERCE_MAX; ++i) nearest.insert(g_wall[i]);
        mark = FxCallMark();
        FxTickNow();
        bool farNever = true;
        for (int i = Grimoire::SCHLACHT_PIERCE_MAX; i < kWalls; ++i) farNever = farNever && FxHides(g_wall[i]) == 0;
        check("DIFF.07 pierce 10 over 12 walls: exactly the nearest 10 recorded after 10 traces; the two farthest "
              "never called",
              G2RecordIs(nearest) && FxCount(FxKind::Trace, mark) == Grimoire::SCHLACHT_PIERCE_MAX && farNever,
              G2Got(mark));
    }

    {   // DIFF.08 (C8): a hit that repeats forever ends after the depth plus the extra iterations.
        G2Start("DIFF.08");
        FxSticky(FxHitAt(kIdxEnemy, kG2EnemyT));
        size_t mark = FxCallMark();
        FxTickNow();
        const int pawnBound = Grimoire::SCHLACHT_PIERCE_DEFAULT + Grimoire::SCHLACHT_MAX_EXTRA_ITERS;
        check("DIFF.08 a Pawn on every trace at pierce 1: exactly 1 + 16 = 17 traces, then it stops; the record {}",
              FxCount(FxKind::Trace, mark) == pawnBound && FxCount(FxKind::Hide, mark) == 0 && FxRecordNow().empty(),
              G2Got(mark));
        FxSticky(FxHitAt(kIdxWallA, kG2NearT));
        Schlacht::SetPierceCount(kG2PierceTwo);
        mark = FxCallMark();
        FxTickNow();
        const int dupBound = kG2PierceTwo + Grimoire::SCHLACHT_MAX_EXTRA_ITERS;
        check("DIFF.08 WallA on every trace at pierce 2: exactly 2 + 16 = 18 traces -- a repeated hit uses an "
              "iteration and fills no depth; the record {WallA}",
              FxCount(FxKind::Trace, mark) == dupBound && G2RecordIs(FxSet({ g_wallA })), G2Got(mark));
    }

    {   // DIFF.09 (C5): a miss ends the loop, and what was hidden is un-hidden.
        G2Start("DIFF.09");
        G2GoodTick("DIFF.09");
        FxScene({});
        const size_t mark = FxCallMark();
        FxTickNow();
        const Schlacht::SeeThroughStatus st = FxStatus();
        check("DIFF.09 miss: exactly one trace -- ReturnValue 0 ends the loop", FxCount(FxKind::Trace, mark) == 1,
              G2Calls(mark));
        check("DIFF.09 miss: SetActorHiddenInGame(WallA, 0) once, the record {}, code 0 with a target",
              FxHides(g_wallA, 0, mark) == 1 && FxCount(FxKind::Hide, mark) == 1 && FxRecordNow().empty()
                  && st.hiddenCount == 0 && st.code == Schlacht::STR_OK && st.hasTarget,
              G2Got(mark));
    }
}

// RESOLVE, then HIT, then DIFF: the order main runs them in, after TRACE and before HIDECODES.
static void Blocks_G2() {
    G2Resolve();
    G2Hit();
    G2Diff();
}

// ══ G3: HIDECODES, DISPATCHFAIL, ENABLE, DISABLE, PENDING, GIVEUP ══════════════════════════════════════════════════
// What a hide or un-hide call comes to, an invoke that did not run, the enable and disable paths, the deferred restore
// and its give-up. These blocks run after HIT and DIFF, whose one-shot lines are spent by then, and they are where L2
// (not an actor) and L3 (no setter) fire for the first time in the process.

// ── G3's vocabulary ──
constexpr int kG3MsPerSecond = 1000;           // the give-up lines print whole seconds
// Distances along the camera's +X axis. The far one lies past the near one's ImpactPoint plus the trace step, so a
// pierce trace restarted there meets it.
constexpr double kG3NearT = 500.0, kG3FarT = 800.0;
constexpr int32_t kG3Pierce2 = 2;              // a second trace runs only after the first one hit
constexpr int32_t kG3EnablePierce = 4;         // EN.01: not the default, so L17 shows the depth the record holds
constexpr int kG3CachedTicks = 3;              // EN.04: more than one, so a warning per tick would show
constexpr int32_t kG3TakeCap = 8;              // TakeLeftovers' room: more than any record here holds
constexpr int kG3NeverRan = -1;                // FxGameHidden for an actor no call ever ran on
constexpr int kG3TickWithinMs = 1000;          // a real worker's first tick lands after 100 ms: ten ticks of slack
constexpr int kG3SpawnWindowMs = 100;          // PD.03: a second restore thread would log its L9 well within this
// Invoke codes beyond Stark::kInvokeTimedOutStillQueued (-5) and kHideFaultedInCall (-4). Neither call ran.
constexpr int32_t kG3RcHookNotActive = -7;     // the game-thread hook is not active
constexpr int32_t kG3RcDirectRefused = -8;     // a direct call refused on a background worker
// GU.01: well into the wait yet short of the 1 s give-up, which no pass reaches early because each sleeps a full tick.
constexpr int kG3MidWaitMs = kPendingMaxMs * 3 / 5;
// OBS-O08: a pass every 50 ms up to the 1 s give-up logs twenty such lines; fewer than this means passes went missing.
constexpr int kG3MinStalledPasses = 15;
// GU.04: every failing un-hide of WallA -- the disable's own call, then one per pass before the give-up's pass.
constexpr int kG3Gu04FailingCalls = kPendingMaxMs / kPendingTickMs;

// ── the lines G3 reads, spelled as Schlacht's format strings spell them ──
static const char* const kG3EnabledPrefix = "SeeThrough: enabled (pierce=";
static const char* const kG3RefusingPrefix = "SeeThrough: refusing to enable";
static const char* const kG3ResumedPrefix = "SeeThrough: game thread resumed after ";
static const char* const kG3GaveUpRestoringPrefix = "SeeThrough: gave up restoring ";
static const char* const kG3NoSetterLine =
    "SeeThrough: SetActorHiddenInGame NOT FOUND (cooked out?) — can't hide occluders";
static const char* const kG3NoLtsLine = "SeeThrough: LineTraceSingle not found (cooked out?) — no occluder detection";
static const char* const kG3WorkerStoppedLine = "SeeThrough: worker stopped";
static const char* const kG3NothingHiddenLine = "SeeThrough: disabled (nothing hidden)";
static const char* const kG3WaitingLine =
    "SeeThrough: waiting for the game thread to resume so the leftover hidden actor(s) can be restored";

static std::string G3NotActorLine(uintptr_t obj, const char* cls, const char* outer) {
    return FxFmt("SeeThrough: the traced hit is NOT an AActor — class '%s' at 0x%llX. Nothing can be hidden on this "
                 "build; the FHitResult read needs fixing [SEETHRUNOOP]. Owner hop: outer='%s'",
                 cls, static_cast<unsigned long long>(obj), outer);
}
static std::string G3WorkerStartedLine() {
    return FxFmt("SeeThrough: worker started (%d ms tick)", Grimoire::SCHLACHT_TICK_MS);
}
static std::string G3EnabledLine(int32_t pierce) { return FxFmt("SeeThrough: enabled (pierce=%d)", pierce); }
static std::string G3DisabledLine(int restored) { return FxFmt("SeeThrough: disabled (%d restored)", restored); }
static std::string G3DisabledWaitingLine(int restored, int notRun) {
    return FxFmt("SeeThrough: disabled (%d restored); %d un-hide(s) did not run — waiting for the game thread to "
                 "restore them", restored, notRun);
}
static std::string G3StalledDisableLine(int kept) {
    return FxFmt("SeeThrough: disabled but %d actor(s) remain hidden (game thread unresponsive) — waiting for it to "
                 "resume to restore them", kept);
}
static std::string G3ResumedSuffix(int restored, int left) {
    return FxFmt(" ms — restored %d leftover hidden actor(s), %d still hidden", restored, left);
}
static std::string G3ResumedLine(int waited, int restored, int left) {
    return kG3ResumedPrefix + FxS(waited) + G3ResumedSuffix(restored, left);
}
static std::string G3GaveUpWaitingLine() {
    return FxFmt("SeeThrough: gave up waiting for the game thread (%d s) — any leftover hidden actor(s) stay hidden "
                 "until See-through is enabled and disabled again with the game running",
                 kPendingMaxMs / kG3MsPerSecond);
}
static std::string G3GaveUpRestoringLine(int n) {
    return FxFmt("SeeThrough: gave up restoring %d hidden actor(s) after %d s — they stay hidden until See-through is "
                 "enabled and disabled again", n, kPendingMaxMs / kG3MsPerSecond);
}

// ── readers ──
// See-through's lines that start with `prefix` and end with `suffix` (lvl 0 = any level); `middle` gets the first
// one's text between the two -- the number an L11 line carries.
static int G3LogFramed(const std::string& prefix, const std::string& suffix, char lvl, size_t from,
                       std::string* middle = nullptr) {
    int n = 0;
    for (const auto& l : FxLogs(from)) {
        if (l.cat != kSeeThruCat || (lvl && l.lvl != lvl)) continue;
        const std::string& t = l.text;
        if (t.size() < prefix.size() + suffix.size() || t.compare(0, prefix.size(), prefix) != 0 ||
            t.compare(t.size() - suffix.size(), suffix.size(), suffix) != 0)
            continue;
        if (n == 0 && middle) *middle = t.substr(prefix.size(), t.size() - prefix.size() - suffix.size());
        ++n;
    }
    return n;
}
// SetActorHiddenInGame calls on `actor` since `from`, in arrival order; hidden -1 = either direction.
static std::vector<FxCall> G3HideCalls(uintptr_t actor, int hidden, size_t from) {
    std::vector<FxCall> out;
    for (auto& c : FxCallsOf(FxKind::Hide, from))
        if (c.instance == actor && (hidden < 0 || FxHideByte(c) == hidden)) out.push_back(std::move(c));
    return out;
}
// Every un-hide since `from`, on any actor.
static std::vector<FxCall> G3Unhides(size_t from) {
    std::vector<FxCall> out;
    for (auto& c : FxCallsOf(FxKind::Hide, from))
        if (FxHideByte(c) == 0) out.push_back(std::move(c));
    return out;
}
// [INV.01] Each of these calls found s_mutex free, and there was at least one: an empty list proves nothing.
static bool G3MutexFree(const std::vector<FxCall>& calls) {
    if (calls.empty()) return false;
    for (const auto& c : calls)
        if (!c.mutexFree) return false;
    return true;
}
// Every trace since `from` went to `instance` through `ufunc`, and there was at least one.
static bool G3AllTracesTo(size_t from, uintptr_t instance, uintptr_t ufunc) {
    const auto traces = FxCallsOf(FxKind::Trace, from);
    if (traces.empty()) return false;
    for (const auto& c : traces)
        if (c.instance != instance || c.ufunc != ufunc) return false;
    return true;
}
// The calls since `from`, one word each, for a check's `got`.
static std::string G3CallsStr(size_t from) {
    static const char* const kKindName[] = { "Other", "Trace", "CamLoc", "CamRot", "Hide" };
    std::string out;
    for (const auto& c : FxCalls(from)) {
        out += out.empty() ? "" : " ";
        out += kKindName[static_cast<size_t>(c.kind)];
        out += "(" + FxNameOf(c.instance);
        if (c.kind == FxKind::Hide) out += "," + FxS(FxHideByte(c));
        out += ")=" + FxS(c.rc);
        if (!c.onMain) out += c.background ? "@bg" : "@thread";
    }
    return out.empty() ? std::string("(no calls)") : out;
}

// ── drivers ──
// On with no worker left ticking. A step that depends on it says so when it did not go through.
static void G3On(const char* where, uint64_t owner = kClientA) {
    const int32_t rc = FxEnableSync(owner);
    if (rc != 1)
        check(FxFmt("FIXTURE %s: the enable this step starts from went through", where).c_str(), false, FxS(rc));
}
static void G3Off(const char* where) {
    const int32_t rc = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
    if (rc != 0) check(FxFmt("FIXTURE %s: the disable this step relies on returned 0", where).c_str(), false, FxS(rc));
}

// One enabled tick whose only hide (WallA) the engine answers `rc`.
struct G3HideOutcome {
    Schlacht::SeeThroughStatus st;
    int hideCalls = 0;
    int32_t rc = 0;
    int gameHidden = kG3NeverRan;
};
static G3HideOutcome G3TickHidingWallA(int32_t rc) {
    FxReset();
    G3On("HIDECODES");
    FxScene({ FxHitAt(kIdxWallA, kG3NearT) });
    FxScriptHide(g_wallA, true, { rc });
    const size_t from = FxCallMark();
    FxTickNow();
    G3HideOutcome o;
    o.st = FxStatus();
    const auto hides = G3HideCalls(g_wallA, 1, from);
    o.hideCalls = static_cast<int>(hides.size());
    if (!hides.empty()) o.rc = hides[0].rc;
    o.gameHidden = FxGameHidden(g_wallA);
    return o;
}
static std::string G3OutcomeStr(const G3HideOutcome& o) {
    return FxStatusStr(o.st) + FxFmt(" hideCalls=%d rc=%d gameHidden=%d", o.hideCalls, o.rc, o.gameHidden);
}

// ── hook state: file-scope, because an engine or log hook copied before it was cleared may still run after a block
//    ends, and it must not reach a block's dead locals ──
static std::atomic<bool> g3_pd5Blocked{false}, g3_pd5Release{false}, g3_pd5AdoptDone{false};
static std::atomic<int> g3_gu04Unhides{0};
static std::atomic<int> g3_gu04PendingAtLine{kG3NeverRan};   // 1 / 0 = restorePending when that L11 was written
static std::atomic<bool> g3_gu04Adopted{false};
static std::string g3_gu04Line;   // written before its hook is armed, read only while it is

// ══ HIDECODES ═══════════════════════════════════════════════════════════════════════════════════════════════════════
static void G3HideCodes() {
    blk("HIDECODES - what a hide or un-hide call comes to");

    // XL2: a recorded object that is not an actor leaves the record without a call, and the build is told once.
    FxReset();
    G3On("XL2");
    FxAdopt({ g_material });
    {
        const size_t calls = FxCallMark();
        FxTickNow();   // empty scene: the trace misses, so the record's only member is un-hidden
        const auto st = FxStatus();
        check("XL2.01 not an actor: Material_0 leaves the record with no SetActorHiddenInGame call on it",
              FxHides(g_material, -1, calls) == 0 && FxRecordNow().empty() && st.hiddenCount == 0,
              FxStatusStr(st) + " calls: " + G3CallsStr(calls));
        const std::string l2 = G3NotActorLine(g_material, "MaterialInterface", "/Script/Engine");
        check("XL2.02 not an actor: one warning names its class, its address and its outer",
              FxLogCount(l2, 'W') == 1, FxLogDump());
        FxAdopt({ g_material });
        FxTickNow();
        check("XL2.03 not an actor: the warning is once per process -- a second drop adds none",
              FxLogCount(l2, 'W') == 1 && FxHides(g_material, -1, calls) == 0 && FxRecordNow().empty(), FxLogDump());
    }

    // XL3: the setter goes away after the probe passed; a hit then hides nothing and records nothing.
    FxReset();
    G3On("XL3");
    FxActorFns(g_fnK2Loc);
    FxScene({ FxHitAt(kIdxWallA, kG3NearT) });
    {
        const size_t calls = FxCallMark();
        FxTickNow();
        const auto st = FxStatus();
        check("XL3.01 no setter: the hit on WallA makes no SetActorHiddenInGame call, records nothing [SEETHRUTALLY]",
              FxCount(FxKind::Trace, calls) >= 1 && FxCount(FxKind::Hide, calls) == 0 && st.hiddenCount == 0 &&
                  FxRecordNow().empty(),
              FxStatusStr(st) + " calls: " + G3CallsStr(calls));
        check("XL3.02 no setter: one warning says SetActorHiddenInGame is missing",
              FxLogCount(kG3NoSetterLine, 'W') == 1, FxLogDump());
        FxTickNow();
        check("XL3.03 no setter: the warning is once per process -- a second tick adds none",
              FxLogCount(kG3NoSetterLine, 'W') == 1 && FxCount(FxKind::Hide, calls) == 0, FxLogDump());
    }
    FxActorFns(g_fnSetHidden);

    // HIDE.01 / HIDE.02 / OBS-O12: what the hide's own answer does to the record.
    {
        const G3HideOutcome ran = G3TickHidingWallA(0);
        check("HIDE.01 ran: a hide answered 0 is recorded -- hiddenCount 1, WallA hidden in the game",
              ran.hideCalls == 1 && ran.rc == 0 && ran.st.hiddenCount == 1 &&
                  FxSetOf(ran.st.hiddenActors) == FxSet({ g_wallA }) && ran.gameHidden == 1,
              G3OutcomeStr(ran));
        const G3HideOutcome queued = G3TickHidingWallA(Stark::kInvokeTimedOutStillQueued);
        check("HIDE.02 timed out, still queued: a hide answered -5 is recorded hidden -- hiddenCount 1, WallA listed",
              queued.hideCalls == 1 && queued.rc == Stark::kInvokeTimedOutStillQueued && queued.st.hiddenCount == 1 &&
                  FxSetOf(queued.st.hiddenActors) == FxSet({ g_wallA }) && queued.gameHidden == kG3NeverRan,
              G3OutcomeStr(queued));
        const G3HideOutcome hookDown = G3TickHidingWallA(kG3RcHookNotActive);
        check("OBS-O12 never ran, still recorded: a hide answered -7 counts WallA hidden (hiddenCount 1)",
              hookDown.hideCalls == 1 && hookDown.rc == kG3RcHookNotActive && hookDown.st.hiddenCount == 1 &&
                  FxSetOf(hookDown.st.hiddenActors) == FxSet({ g_wallA }) && hookDown.gameHidden == kG3NeverRan,
              G3OutcomeStr(hookDown));
        const G3HideOutcome faulted = G3TickHidingWallA(Schlacht::kHideFaultedInCall);
        check("OBS-O12 faulted, still recorded: a hide answered -4 counts WallA hidden (hiddenCount 1)",
              faulted.hideCalls == 1 && faulted.rc == Schlacht::kHideFaultedInCall && faulted.st.hiddenCount == 1 &&
                  FxSetOf(faulted.st.hiddenActors) == FxSet({ g_wallA }),
              G3OutcomeStr(faulted));
    }
}

// ══ DISPATCHFAIL ════════════════════════════════════════════════════════════════════════════════════════════════════
static void G3DispatchFail() {
    blk("DISPATCHFAIL - an invoke that did not run never costs the record");

    // DF.01 .. DF.03 share one record, {WallA}, written by a good tick.
    FxReset();
    G3On("DF.01");
    FxScene({ FxHitAt(kIdxWallA, kG3NearT) });
    FxTickNow();
    if (FxRecordNow() != FxSet({ g_wallA }))
        check("FIXTURE DF.01: the good tick recorded WallA", false, FxSetStr(FxRecordNow()));
    {
        const size_t calls = FxCallMark();
        FxTraceRcs({ Stark::kInvokeTimedOutStillQueued });
        FxTickNow();
        const auto st = FxStatus();
        check("DF.01 trace did not run: one trace, no hide or un-hide, the record {WallA} kept, code 0 with no target",
              FxCount(FxKind::Trace, calls) == 1 && FxCount(FxKind::Hide, calls) == 0 &&
                  FxRecordNow() == FxSet({ g_wallA }) && st.code == 0 && !st.hasTarget,
              FxStatusStr(st) + " calls: " + G3CallsStr(calls));
    }
    {
        Schlacht::SetPierceCount(kG3Pierce2);
        FxScene({ FxHitAt(kIdxWallB, kG3FarT) });
        FxTraceRcs({ 0, kG3RcDirectRefused });
        const size_t calls = FxCallMark();
        FxTickNow();
        check("DF.02 second trace did not run: WallB is not hidden and WallA is not un-hidden -- the record {WallA}",
              FxCount(FxKind::Trace, calls) == 2 && FxCount(FxKind::Hide, calls) == 0 &&
                  FxRecordNow() == FxSet({ g_wallA }),
              FxSetStr(FxRecordNow()) + " calls: " + G3CallsStr(calls));
    }
    {
        // The hit on WallB would un-hide WallA and hide WallB, were it applied.
        Schlacht::SetPierceCount(Grimoire::SCHLACHT_PIERCE_DEFAULT);
        FxOnTrace([](int index) {
            if (index == 0) s_workerStop.store(true);   // a stop arrives while the first trace runs
        });
        const size_t calls = FxCallMark();
        FxTickNow();
        FxOnTrace(nullptr);
        const bool stopSeen = s_workerStop.load();
        s_workerStop.store(false);
        const auto st = FxStatus();
        check("DF.03 stop under way: the hit on WallB is not applied -- no call, the record {WallA}, code 0, no target",
              stopSeen && FxCount(FxKind::Trace, calls) >= 1 && FxCount(FxKind::Hide, calls) == 0 &&
                  FxRecordNow() == FxSet({ g_wallA }) && st.code == 0 && !st.hasTarget,
              FxStatusStr(st) + " calls: " + G3CallsStr(calls));
    }

    // DF.04 / DF.05: un-hides answered every way, then the tick after.
    FxReset();
    G3On("DF.04");
    FxAdopt({ g_wallA, g_wallB, g_wallC, g_wallD, g_wall[4], g_material });
    FxScriptHide(g_wallB, false, { Schlacht::kHideFaultedInCall });
    FxScriptHide(g_wallC, false, { Stark::kInvokeTimedOutStillQueued });
    FxScriptHide(g_wallD, false, { kG3RcHookNotActive });
    FxScriptHide(g_wall[4], false, { kG3RcDirectRefused });
    {
        const size_t calls = FxCallMark();
        FxTickNow();   // empty scene: every recorded actor is un-hidden
        const auto notRun = FxSet({ g_wallC, g_wallD, g_wall[4] });
        const auto st = FxStatus();
        check("DF.04 un-hides that did not run stay: C (-5), D (-7), E (-8) recorded; A (ran) and B (-4) gone",
              FxRecordNow() == notRun && st.hiddenCount == 3, FxStatusStr(st));
        check("DF.04 queued: those three are marked as un-hides that may still run",
              FxQueuedNow() == notRun, FxSetStr(FxQueuedNow()));
        bool oncePerActor = true;
        for (uintptr_t a : { g_wallA, g_wallB, g_wallC, g_wallD, g_wall[4] })
            oncePerActor = oncePerActor && FxHides(a, 0, calls) == 1 && FxHides(a, 1, calls) == 0;
        check("DF.04 calls: exactly one un-hide on each of A..E, none on Material_0",
              oncePerActor && FxHides(g_material, -1, calls) == 0, G3CallsStr(calls));
    }
    {
        FxScene({ FxHitAt(kIdxWallC, kG3NearT) });   // all un-hides answer 0 from here on
        const size_t calls = FxCallMark();
        FxTickNow();
        const auto rehide = G3HideCalls(g_wallC, 1, calls);
        check("DF.05 re-hide: C still occludes with its un-hide queued, so it is hidden again; D and E un-hidden again",
              rehide.size() == 1 && FxHides(g_wallC, 0, calls) == 0 && FxHides(g_wallD, 0, calls) == 1 &&
                  FxHides(g_wall[4], 0, calls) == 1 && FxCount(FxKind::Hide, calls) == 3,
              G3CallsStr(calls));
        check("DF.05 record: {C}, and nothing is queued any more",
              FxRecordNow() == FxSet({ g_wallC }) && FxQueuedNow().empty(),
              FxSetStr(FxRecordNow()) + " queued " + FxSetStr(FxQueuedNow()));
        check("INV.01 re-hide (DF.05): s_mutex was free across the tick's re-hide", G3MutexFree(rehide),
              G3CallsStr(calls));
        const size_t third = FxCallMark();
        FxTickNow();
        check("DF.05 settled: a third tick with C still in the way makes no call",
              FxCount(FxKind::Hide, third) == 0 && FxRecordNow() == FxSet({ g_wallC }), G3CallsStr(third));
    }

    // DF.06: a disable whose un-hide did not run hands it to the deferred restore.
    FxReset();
    G3On("DF.06");
    FxRecord(FxSet({ g_wallA, g_wallB }));
    FxScriptHide(g_wallA, false, { Stark::kInvokeTimedOutStillQueued, 0 });
    {
        const size_t calls = FxCallMark(), logs = FxLogMark();
        const int32_t rc = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const auto st = FxStatus();
        const auto queued = FxQueuedNow();
        check("DF.06 off: SetEnabled(false) returns 0", rc == 0, FxS(rc));
        check("DF.06 at return: off with no target, A kept and queued, a restore waits, nothing given up",
              !st.active && st.state == 0 && !st.hasTarget && FxSetOf(st.hiddenActors) == FxSet({ g_wallA }) &&
                  queued == FxSet({ g_wallA }) && st.hiddenCount == 1 && st.restorePending && !st.restoreAbandoned,
              FxStatusStr(st));
        check("DF.06 L19: one warning says one was restored and one waits for the game thread",
              FxLogCount(G3DisabledWaitingLine(1, 1), 'W', kSeeThruCat, logs) == 1, FxLogDump(logs));
        const std::string resumed = G3ResumedLine(kPendingTickMs, 1, 0);
        const bool restored = waitFor([&] {
            return !FxRestorePending() && FxLogCount(resumed, 'I', kSeeThruCat, logs) >= 1;
        }, kRestoreWithinMs);
        check("DF.06 restored: within 1 s no restore waits, the record and the queue are empty and A shows in the game "
              "again",
              restored && FxRecordNow().empty() && FxQueuedNow().empty() && FxGameHidden(g_wallA) == 0,
              FxStatusStr(FxStatus()));
        check("DF.06 L11: the restore says it ran at its first 50 ms pass, 1 restored, 0 still hidden",
              FxLogCount(resumed, 'I', kSeeThruCat, logs) == 1, FxLogDump(logs));
        const auto unhidesA = G3HideCalls(g_wallA, 0, calls);
        check("DF.06 thread: A's first un-hide ran on the disabling thread, its retry on the restore's background one",
              unhidesA.size() == 2 && unhidesA[0].onMain && !unhidesA[0].background && !unhidesA[1].onMain &&
                  unhidesA[1].background,
              G3CallsStr(calls));
    }
}

// ══ ENABLE ══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void G3Enable() {
    blk("ENABLE - the enable path");

    // EN.01 / EN.02 / OBS-O02a: on after a refusal, then asked again while on.
    FxReset();
    {
        const int ensure0 = g_ensureCalls.load();
        g_hookUp.store(false);
        const int32_t refused = Schlacht::SetEnabled(true, kClientA);
        g_hookUp.store(true);
        if (refused != Schlacht::STR_ERR_NO_HOOK || FxStatus().code != Schlacht::STR_ERR_NO_HOOK)
            check("FIXTURE EN.01: the hook-down enable is refused with -5", false, FxS(refused));
        Schlacht::SetPierceCount(kG3EnablePierce);
        const size_t logs = FxLogMark();
        const int32_t rc = FxEnableSync(kClientA);
        const auto st = FxStatus();
        check("EN.01 on after a refusal: 1, active, state 1, code 0",
              rc == 1 && st.active && st.state == 1 && st.code == 0, FxStatusStr(st));
        check("EN.01 L17: one line, naming the pierce depth the record holds",
              FxLogCount(G3EnabledLine(kG3EnablePierce), 'I', kSeeThruCat, logs) == 1 &&
                  FxLogPrefix(kG3EnabledPrefix, 0, kSeeThruCat, logs) == 1,
              FxLogDump(logs));
        check("EN.01 L6: one worker started", FxLogCount(G3WorkerStartedLine(), 'I', kSeeThruCat, logs) == 1,
              FxLogDump(logs));

        const int ensure1 = g_ensureCalls.load();
        const size_t logs2 = FxLogMark();
        const int32_t again = Schlacht::SetEnabled(true, kClientB);
        const int ensure2 = g_ensureCalls.load();
        check("EN.02 already on: 1, with no second L17 and no worker started",
              again == 1 && FxLogPrefix(kG3EnabledPrefix, 0, kSeeThruCat, logs2) == 0 &&
                  FxLogCount(G3WorkerStartedLine(), 'I', kSeeThruCat, logs2) == 0 && !FxWorkerJoinable(),
              FxS(again) + FxLogDump(logs2));
        FxActorFns(g_fnK2Loc);
        const int32_t noProbe = Schlacht::SetEnabled(true, kClientB);
        const int ensure3 = g_ensureCalls.load();
        const auto st2 = FxStatus();
        FxActorFns(g_fnSetHidden);
        check("EN.02 no probe while on: with the setter gone it still returns 1, state stays 1, nothing is refused",
              noProbe == 1 && st2.active && st2.state == 1 &&
                  FxLogPrefix(kG3RefusingPrefix, 0, kSeeThruCat, logs2) == 0,
              FxStatusStr(st2) + FxLogDump(logs2));
        check("OBS-O02a hook asked: every enable asks for the hook, the already-on ones included",
              ensure1 - ensure0 == 2 && ensure2 - ensure1 == 1 && ensure3 - ensure2 == 1,
              FxFmt("deltas %d %d %d", ensure1 - ensure0, ensure2 - ensure1, ensure3 - ensure2));
    }

    // EN.03: each enable re-resolves the KismetSystemLibrary CDO.
    FxReset();
    {
        G3On("EN.03");
        FxScene({ FxHitAt(kIdxWallA, kG3NearT) });
        const size_t first = FxCallMark();
        FxTickNow();
        const bool toCdo1 = G3AllTracesTo(first, g_cdoKsl, g_ltsD);
        G3Off("EN.03");
        FxPool(FxPoolVariant::SecondKslCdo);   // no thread runs: the disable un-hid WallA with nothing left over
        G3On("EN.03 (second CDO)");
        const size_t second = FxCallMark();
        FxTickNow();
        check("EN.03 first enable: its traces go to slot 4's Default__KismetSystemLibrary", toCdo1, G3CallsStr(first));
        check("EN.03 re-enabled: with a new CDO in slot 4, every trace goes to the new one",
              FxSlot(kIdxCdoKsl) == g_cdoKsl2 && G3AllTracesTo(second, g_cdoKsl2, g_ltsD), G3CallsStr(second));
    }

    // EN.04: LineTraceSingle gone after the enable resolved nothing; the failure holds for that enable.
    FxReset();
    {
        G3On("EN.04");
        FxKsl(g_fnLtsMulti);
        FxScene({ FxHitAt(kIdxWallA, kG3NearT) });
        const size_t calls = FxCallMark(), logs = FxLogMark();
        for (int i = 0; i < kG3CachedTicks; ++i) FxTickNow();
        check("EN.04 cooked out: three ticks reach the trace step yet make no trace, and one warning says so",
              FxCount(FxKind::Trace, calls) == 0 && FxCount(FxKind::CamLoc, calls) == kG3CachedTicks &&
                  FxLogCount(kG3NoLtsLine, 'W', kSeeThruCat, logs) == 1,
              G3CallsStr(calls) + FxLogDump(logs));
        FxKsl(g_ltsD);
        const size_t back = FxCallMark();
        FxTickNow();
        check("EN.04 cached: LineTraceSingle back, the same enable still traces nothing and warns no more",
              FxCount(FxKind::Trace, back) == 0 && FxLogCount(kG3NoLtsLine, 'W', kSeeThruCat, logs) == 1,
              G3CallsStr(back));
        G3Off("EN.04");
        G3On("EN.04 (again)");
        const size_t resumed = FxCallMark();
        FxTickNow();
        check("EN.04 re-enabled: traces resume, on LineTraceSingle", G3AllTracesTo(resumed, g_cdoKsl, g_ltsD),
              G3CallsStr(resumed));
    }

    // EN.05 / OBS-O04b: LineTraceSingle without OutHit after the enable reads as "nothing occludes".
    FxReset();
    {
        G3On("EN.05");
        FxKsl(g_ltsNoOutHit);
        FxRecord(FxSet({ g_wallA }));
        FxScene({ FxHitAt(kIdxWallA, kG3NearT) });   // a hit, had a trace run
        const size_t calls = FxCallMark(), logs = FxLogMark();
        FxTickNow();
        const auto st = FxStatus();
        check("EN.05 no OutHit: no trace runs, and no L4 -- LineTraceSingle itself was found",
              FxCount(FxKind::Trace, calls) == 0 && FxLogCount(kG3NoLtsLine, 0, kSeeThruCat, logs) == 0,
              G3CallsStr(calls) + FxLogDump(logs));
        check("OBS-O04b nothing occludes: WallA is un-hidden and dropped, code 0 with a target",
              FxHides(g_wallA, 0, calls) == 1 && FxRecordNow().empty() && st.code == 0 && st.hasTarget,
              FxStatusStr(st));
    }

    // EN.06: a leftover is un-hidden by the enable itself, on the enabling thread.
    FxReset();
    {
        FxRecord(FxSet({ g_wallA }));
        FxSetQueued(FxSet({ g_wallA }));
        const size_t calls = FxCallMark(), logs = FxLogMark();
        const int32_t rc = FxEnableSync(kClientA);
        const auto un = G3HideCalls(g_wallA, 0, calls);
        const uint64_t enabledAt = FxLogSeqPrefix(kG3EnabledPrefix, 'I', kSeeThruCat, logs);
        const auto st = FxStatus();
        check("EN.06 leftover: 1, and WallA's un-hide ran on the enabling thread, unmarked, before L17",
              rc == 1 && un.size() == 1 && un[0].onMain && !un[0].background && enabledAt != 0 &&
                  un[0].seq < enabledAt,
              G3CallsStr(calls) + FxLogDump(logs));
        check("EN.06 record: emptied, nothing queued",
              st.hiddenCount == 0 && FxRecordNow().empty() && FxQueuedNow().empty(), FxStatusStr(st));
        check("INV.01 enable leftover (EN.06): s_mutex was free across the un-hide", G3MutexFree(un),
              G3CallsStr(calls));
    }

    // EN.07: an enable while the game is stalled joins the waiting restore and leaves the record to the worker.
    FxReset();
    {
        G3On("EN.07");
        FxRecord(FxSet({ g_wallA }));
        g_responsive.store(false);
        G3Off("EN.07");
        if (!FxRestorePending()) check("FIXTURE EN.07: the stalled disable left a restore waiting", false);
        const size_t calls = FxCallMark();
        g_initInProgress.store(true);   // the worker this enable starts must not tick before the state is read
        const int32_t rc = Schlacht::SetEnabled(true, kClientA);
        const bool pending = FxRestorePending(), pendingThread = FxPendingJoinable();
        const auto st = FxStatus();
        const int hides = FxCount(FxKind::Hide, calls);
        Schlacht::StopWorker();
        g_initInProgress.store(false);
        check("EN.07 enable while stalled: 1, and the waiting restore was joined before it returned",
              rc == 1 && !pending && !pendingThread, FxS(rc) + " " + FxStatusStr(st));
        check("EN.07 leftover kept: still stalled, the record {WallA} stays with no call during the enable",
              FxSetOf(st.hiddenActors) == FxSet({ g_wallA }) && st.hiddenCount == 1 && hides == 0,
              FxStatusStr(st) + " calls: " + G3CallsStr(calls));
        g_responsive.store(true);
        const size_t tick = FxCallMark();
        FxTickNow();   // empty scene: WallA no longer occludes
        check("EN.07 inherited: the first responsive tick un-hides WallA and empties the record",
              FxHides(g_wallA, 0, tick) == 1 && FxRecordNow().empty(), G3CallsStr(tick));
    }

    // EN.08: a leftover whose un-hide did not run stays queued, and the next tick hides it again.
    FxReset();
    {
        FxRecord(FxSet({ g_wallA }));
        FxScriptHide(g_wallA, false, { Stark::kInvokeTimedOutStillQueued });
        const int32_t rc = FxEnableSync(kClientA);
        check("EN.08 leftover un-hide did not run: 1, WallA stays recorded and queued",
              rc == 1 && FxRecordNow() == FxSet({ g_wallA }) && FxQueuedNow() == FxSet({ g_wallA }),
              FxS(rc) + " record " + FxSetStr(FxRecordNow()) + " queued " + FxSetStr(FxQueuedNow()));
        FxScene({ FxHitAt(kIdxWallA, kG3NearT) });
        const size_t calls = FxCallMark();
        FxTickNow();
        check("EN.08 re-hide: WallA still in the way, so the next tick hides it again -- exactly one call",
              G3HideCalls(g_wallA, 1, calls).size() == 1 && FxCount(FxKind::Hide, calls) == 1 &&
                  FxRecordNow() == FxSet({ g_wallA }) && FxQueuedNow().empty(),
              G3CallsStr(calls));
    }
}

// ══ DISABLE ═════════════════════════════════════════════════════════════════════════════════════════════════════════
static void G3Disable() {
    blk("DISABLE - the disable path");

    // DS.01: nothing to undo after a refusal.
    FxReset();
    {
        FxKsl(g_fnLtsMulti);
        const int32_t refused = Schlacht::SetEnabled(true, kClientA);
        FxKsl(g_ltsD);
        if (refused != Schlacht::STR_ERR_REFLECTION)
            check("FIXTURE DS.01: the enable without LineTraceSingle is refused with -3", false, FxS(refused));
        const size_t logs = FxLogMark();
        const int32_t rc = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const auto st = FxStatus();
        check("DS.01 nothing to undo: 0, no See-through line, state and code still -3",
              rc == 0 && FxSeeThruLines(logs) == 0 && st.state == Schlacht::STR_ERR_REFLECTION &&
                  st.code == Schlacht::STR_ERR_REFLECTION,
              FxStatusStr(st) + FxLogDump(logs));
    }

    // DS.02: the disable leaves the last tick's code alone.
    FxReset();
    {
        G3On("DS.02");
        FxSetPtr(g_lp0, FxOff::kPlayerController, 0);
        FxTickNow();
        const int32_t tickCode = FxStatus().code;
        const int32_t rc = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const auto st = FxStatus();
        check("DS.02 the tick's code survives: 0, off, state 0, no target, code still -2",
              tickCode == Schlacht::STR_ERR_NO_PAWN && rc == 0 && !st.active && st.state == 0 && !st.hasTarget &&
                  st.code == Schlacht::STR_ERR_NO_PAWN,
              FxS(tickCode) + " " + FxStatusStr(st));
    }

    // DS.03: a real worker is stopped before the record is restored.
    FxReset();
    {
        Schlacht::SetPierceCount(kG3Pierce2);
        FxScene({ FxHitAt(kIdxWallA, kG3NearT), FxHitAt(kIdxWallB, kG3FarT) });
        const int32_t on = Schlacht::SetEnabled(true, kClientA);
        const bool ticked = waitFor([] { return FxRecordNow() == FxSet({ g_wallA, g_wallB }); }, kG3TickWithinMs);
        if (on != 1 || !ticked)
            check("FIXTURE DS.03: the real worker hid WallA and WallB", false, FxS(on) + " " + FxSetStr(FxRecordNow()));
        const size_t calls = FxCallMark(), logs = FxLogMark();
        const int32_t rc = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const auto un = G3Unhides(calls);
        uint64_t firstUnhide = 0;
        for (const auto& c : un)
            if (!firstUnhide || c.seq < firstUnhide) firstUnhide = c.seq;
        int tracesAfter = 0;
        for (const auto& c : FxCallsOf(FxKind::Trace, calls))
            if (c.seq > firstUnhide) ++tracesAfter;
        const uint64_t stoppedAt = FxLogSeq(kG3WorkerStoppedLine, 'I', kSeeThruCat, logs);
        check("DS.03 order: the worker stopped before the first un-hide, and no trace followed it",
              firstUnhide != 0 && stoppedAt != 0 && stoppedAt < firstUnhide && tracesAfter == 0,
              G3CallsStr(calls) + FxLogDump(logs));
        check("DS.03 un-hides: exactly one each for WallA and WallB, and no hide",
              FxHides(g_wallA, 0, calls) == 1 && FxHides(g_wallB, 0, calls) == 1 && FxHides(g_wallA, 1, calls) == 0 &&
                  FxHides(g_wallB, 1, calls) == 0,
              G3CallsStr(calls));
        check("DS.03 L18: 0, one line says two were restored, no restore waits, the record is empty",
              rc == 0 && FxLogCount(G3DisabledLine(2), 'I', kSeeThruCat, logs) == 1 && !FxRestorePending() &&
                  FxRecordNow().empty(),
              FxStatusStr(FxStatus()) + FxLogDump(logs));
        check("INV.01 disable (DS.03): s_mutex was free across both un-hides", G3MutexFree(un) && un.size() == 2,
              G3CallsStr(calls));
    }

    // OBS-O06: L18 counts as "restored" what was dropped without running.
    FxReset();
    {
        G3On("OBS-O06");
        FxRecord(FxSet({ g_wallA, g_material }));
        FxScriptHide(g_wallA, false, { Schlacht::kHideFaultedInCall });
        const size_t logs = FxLogMark();
        const int32_t rc = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        check("OBS-O06 counted as restored: a faulted un-hide and a non-actor both count in L18; the record empties",
              rc == 0 && FxLogCount(G3DisabledLine(2), 'I', kSeeThruCat, logs) == 1 && FxRecordNow().empty() &&
                  !FxRestorePending(),
              FxStatusStr(FxStatus()) + FxLogDump(logs));
    }

    // DS.05: stalled with nothing hidden.
    FxReset();
    {
        G3On("DS.05");
        g_responsive.store(false);
        const size_t logs = FxLogMark();
        const int32_t rc = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        check("DS.05 stalled with nothing hidden: 0, one line says so, and no restore thread starts",
              rc == 0 && FxLogCount(kG3NothingHiddenLine, 'I', kSeeThruCat, logs) == 1 && !FxRestorePending() &&
                  !FxPendingJoinable(),
              FxStatusStr(FxStatus()) + FxLogDump(logs));
    }

    // DS.06: off already, yet a leftover is restored -- the early-out does not swallow it.
    FxReset();
    {
        FxRecord(FxSet({ g_wallA }));
        FxSetQueued(FxSet({ g_wallA }));
        const size_t calls = FxCallMark(), logs = FxLogMark();
        const int32_t rc = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        check("DS.06 off with a queued leftover: WallA is un-hidden, the record and the queue empty, and L18 says one "
              "was restored",
              rc == 0 && FxHides(g_wallA, 0, calls) == 1 && FxRecordNow().empty() && FxQueuedNow().empty() &&
                  FxLogCount(G3DisabledLine(1), 'I', kSeeThruCat, logs) == 1,
              G3CallsStr(calls) + FxLogDump(logs));
    }
}

// ══ PENDING ═════════════════════════════════════════════════════════════════════════════════════════════════════════
static void G3Pending() {
    blk("PENDING - the deferred restore");

    // PD.01 / PD.02: a stalled disable waits, then restores the moment the game answers.
    FxReset();
    {
        G3On("PD.01");
        FxRecord(FxSet({ g_wallA }));
        g_responsive.store(false);
        const size_t calls = FxCallMark(), logs = FxLogMark();
        const int32_t rc = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const bool waiting = waitFor([&] { return FxLogCount(kG3WaitingLine, 'I', kSeeThruCat, logs) >= 1; },
                                     kRestoreWithinMs);
        check("PD.01 stalled disable: 0, one warning says one actor waits, and the restore says it is waiting",
              rc == 0 && FxLogCount(G3StalledDisableLine(1), 'W', kSeeThruCat, logs) == 1 && waiting &&
                  FxLogCount(kG3WaitingLine, 'I', kSeeThruCat, logs) == 1,
              FxLogDump(logs));
        std::this_thread::sleep_for(std::chrono::milliseconds(kHoldMs));   // several passes while stalled
        const int whileStalled = FxHides(g_wallA, -1, calls);
        const bool stillWaiting = FxRestorePending();
        g_responsive.store(true);
        check("PD.02 no call while stalled: the passes before the game answers make no un-hide",
              whileStalled == 0 && stillWaiting, G3CallsStr(calls));
        const std::string suffix = G3ResumedSuffix(1, 0);
        const bool done = waitFor([&] {
            return !FxRestorePending() && G3LogFramed(kG3ResumedPrefix, suffix, 'I', logs) >= 1;
        }, kRestoreWithinMs);
        const auto un = G3HideCalls(g_wallA, 0, calls);
        check("PD.01 resumed: within 1 s WallA is un-hidden once, from the restore's background thread; none waits",
              done && un.size() == 1 && !un[0].onMain && un[0].background && FxRecordNow().empty(),
              G3CallsStr(calls) + " " + FxStatusStr(FxStatus()));
        std::string waitedText;
        const int lines = G3LogFramed(kG3ResumedPrefix, suffix, 'I', logs, &waitedText);
        const int waited = atoi(waitedText.c_str());
        check("PD.01 L11: one line says 1 restored, 0 still hidden", lines == 1, FxLogDump(logs));
        check("PD.01 L11 waited: the ms it names is a whole number of 50 ms passes, short of the give-up",
              waitedText == FxS(waited) && waited > 0 && waited % kPendingTickMs == 0 && waited < kPendingMaxMs,
              waitedText);
        check("INV.01 deferred pass (PD.01): s_mutex was free across the restore's un-hide", G3MutexFree(un),
              G3CallsStr(calls));
    }

    // PD.03 / PD.04: an adoption reuses the waiting restore; a later disable starts a new one.
    FxReset();
    {
        G3On("PD.03");
        FxRecord(FxSet({ g_wallA }));
        g_responsive.store(false);
        const size_t calls = FxCallMark(), logs = FxLogMark();
        G3Off("PD.03");
        waitFor([&] { return FxLogCount(kG3WaitingLine, 'I', kSeeThruCat, logs) >= 1; }, kRestoreWithinMs);
        FxAdopt({ g_wallB });
        const bool secondStarted = waitFor([&] {
            return FxLogCount(kG3WaitingLine, 'I', kSeeThruCat, logs) >= 2;
        }, kG3SpawnWindowMs);
        const auto st = FxStatus();
        check("PD.03 reused: adopting while a restore waits joins it -- one L9, the record {A, B}, a restore waiting",
              !secondStarted && FxLogCount(kG3WaitingLine, 'I', kSeeThruCat, logs) == 1 &&
                  FxSetOf(st.hiddenActors) == FxSet({ g_wallA, g_wallB }) && st.restorePending,
              FxStatusStr(st) + FxLogDump(logs));
        g_responsive.store(true);
        const std::string bothLine = G3ResumedSuffix(2, 0);
        const bool both = waitFor([&] {
            return !FxRestorePending() && FxRecordNow().empty() &&
                   G3LogFramed(kG3ResumedPrefix, bothLine, 'I', logs) >= 1;
        }, kRestoreWithinMs);
        check("PD.03 both restored: once the game answers, A and B are each un-hidden within 1 s",
              both && FxHides(g_wallA, 0, calls) == 1 && FxHides(g_wallB, 0, calls) == 1,
              G3CallsStr(calls) + " " + FxStatusStr(FxStatus()));

        // That restore has decided and logged its last line, so the record is no longer its own.
        FxRecord(FxSet({ g_wallC }));
        g_responsive.store(false);
        G3Off("PD.04");
        const bool restarted = waitFor([&] {
            return FxLogCount(kG3WaitingLine, 'I', kSeeThruCat, logs) >= 2;
        }, kRestoreWithinMs);
        check("PD.04 reap and restart: a stalled disable after that restore ended starts a new one -- L9 twice",
              restarted && FxLogCount(kG3WaitingLine, 'I', kSeeThruCat, logs) == 2 && FxRestorePending(),
              FxStatusStr(FxStatus()) + FxLogDump(logs));
    }

    // PD.05: an adoption that lands while the old restore thread is still running after its decision waits for that
    // thread to end, so only one restore thread exists and its final flag store cannot clear the new restore's.
    FxReset();
    {
        g3_pd5Blocked.store(false);
        g3_pd5Release.store(false);
        g3_pd5AdoptDone.store(false);
        // The restore's L11 is written with neither of Schlacht's locks held, so holding its thread there blocks
        // nothing but that thread.
        FxOnLog([](const FxLogLine& l) {
            if (l.lvl != 'I' || l.cat != kSeeThruCat ||
                l.text.compare(0, strlen(kG3ResumedPrefix), kG3ResumedPrefix) != 0)
                return;
            bool first = false;
            if (!g3_pd5Blocked.compare_exchange_strong(first, true)) return;
            while (!g3_pd5Release.load()) Sleep(kPollMs);
        });
        FxAdopt({ g_wallA });   // off and responsive: a restore starts, and its first pass restores A
        const bool held = waitFor([] { return g3_pd5Blocked.load(); }, kRestoreWithinMs);
        if (!held) {
            g3_pd5Release.store(true);
            check("FIXTURE PD.05: the restore reached its L11 line and was held there", false, FxLogDump());
        } else {
            g_responsive.store(false);   // the restore the adoption starts must wait, not finish at its first pass
            const size_t logs = FxLogMark();
            std::thread adopter([] {
                FxAdopt({ g_wallB });
                g3_pd5AdoptDone.store(true);
            });
            std::this_thread::sleep_for(std::chrono::milliseconds(kHoldMs));
            const bool returnedWhileHeld = g3_pd5AdoptDone.load();
            check("PD.05 reap: an adoption while the old restore thread still runs waits for it to end",
                  !returnedWhileHeld);
            g3_pd5Release.store(true);
            const bool returned = waitFor([] { return g3_pd5AdoptDone.load(); }, kResetWaitMs);
            if (returned) adopter.join();
            else adopter.detach();
            check("PD.05 released: the adoption returns once the old restore thread ends", returned);
            const bool flagCleared = waitFor([] { return !FxRestorePending(); }, kQuietMs);
            check("PD.05 one restore: the new restore stays pending for 300 ms -- no old thread clears its flag",
                  !flagCleared && FxRecordNow() == FxSet({ g_wallB }), FxStatusStr(FxStatus()));
            check("PD.05 L9: exactly one new restore started", FxLogCount(kG3WaitingLine, 'I', kSeeThruCat, logs) == 1,
                  FxLogDump(logs));
        }
        FxOnLog(nullptr);
    }
}

// ══ GIVEUP ══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void G3GiveUp() {
    blk("GIVEUP - the seamed give-up paths (1 s, a pass every 50 ms)");

    // GU.01 / GU.02 / OBS-O01b share one record, {A, B}, through three give-ups.
    FxReset();
    {
        G3On("GU.01");
        FxRecord(FxSet({ g_wallA, g_wallB }));
        g_responsive.store(false);
        const size_t calls = FxCallMark(), logs = FxLogMark();
        const int32_t rc = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const auto st = FxStatus();
        check("GU.01 stalled disable: 0, a restore waits, nothing given up, the record {A, B} kept, off at state 0",
              rc == 0 && st.restorePending && !st.restoreAbandoned && st.hiddenCount == 2 &&
                  FxSetOf(st.hiddenActors) == FxSet({ g_wallA, g_wallB }) && !st.active && st.state == 0,
              FxStatusStr(st));
        std::this_thread::sleep_for(std::chrono::milliseconds(kG3MidWaitMs));
        check("GU.01 still waiting: about 600 ms in, the restore has not given up",
              FxRestorePending() && FxLogCount(G3GaveUpWaitingLine(), 'W', kSeeThruCat, logs) == 0,
              FxStatusStr(FxStatus()));
        const bool gaveUp = waitFor([] { return !FxRestorePending(); }, kGiveUpWithinMs);
        const auto st2 = FxStatus();
        check("GU.01 gave up: within 3 s no restore waits, it is marked abandoned, the record {A, B} kept",
              gaveUp && !st2.restorePending && st2.restoreAbandoned &&
                  FxSetOf(st2.hiddenActors) == FxSet({ g_wallA, g_wallB }) && st2.hiddenCount == 2,
              FxStatusStr(st2));
        check("GU.01 L10: one warning says it gave up waiting after 1 s",
              FxLogCount(G3GaveUpWaitingLine(), 'W', kSeeThruCat, logs) == 1, FxLogDump(logs));
        check("GU.01 no call: nothing was un-hidden", FxCount(FxKind::Hide, calls) == 0, G3CallsStr(calls));

        const size_t logs2 = FxLogMark();
        const int32_t again = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const auto st3 = FxStatus();
        check("GU.02 off again, still stalled: a fresh restore waits and the give-up no longer holds",
              again == 0 && !st3.restoreAbandoned && st3.restorePending &&
                  FxLogCount(G3StalledDisableLine(2), 'W', kSeeThruCat, logs2) == 1,
              FxStatusStr(st3) + FxLogDump(logs2));
        const bool gaveUpAgain = waitFor([] { return !FxRestorePending(); }, kGiveUpWithinMs);
        check("GU.02 gave up again: abandoned once more", gaveUpAgain && FxStatus().restoreAbandoned,
              FxStatusStr(FxStatus()));
        g_responsive.store(true);
        const int32_t offLive = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        check("GU.02 off with the game answering: 0, both restored, and the give-up no longer holds",
              offLive == 0 && !FxStatus().restoreAbandoned && FxRecordNow().empty() && !FxRestorePending(),
              FxS(offLive) + " " + FxStatusStr(FxStatus()));
        const int32_t on = FxEnableSync(kClientA);
        check("GU.02 on with the game answering: 1, and the give-up no longer holds",
              on == 1 && !FxStatus().restoreAbandoned, FxS(on) + " " + FxStatusStr(FxStatus()));

        FxRecord(FxSet({ g_wallA, g_wallB }));
        g_responsive.store(false);
        G3Off("OBS-O01b");
        const bool gaveUpThird = waitFor([] { return !FxRestorePending(); }, kGiveUpWithinMs);
        const bool abandonedBefore = FxStatus().restoreAbandoned;
        g_hookUp.store(false);
        const int32_t refused = Schlacht::SetEnabled(true, kClientA);
        g_hookUp.store(true);
        const auto st4 = FxStatus();
        check("OBS-O01b refused after a give-up: -5 clears the give-up, starts no restore, keeps the record {A, B}",
              gaveUpThird && abandonedBefore && refused == Schlacht::STR_ERR_NO_HOOK && !st4.restoreAbandoned &&
                  !st4.restorePending && FxSetOf(st4.hiddenActors) == FxSet({ g_wallA, g_wallB }) &&
                  st4.hiddenCount == 2,
              FxS(refused) + " " + FxStatusStr(st4));
    }

    // GU.03 / OBS-O08: the game answers, but the un-hide never runs.
    FxReset();
    {
        G3On("GU.03");
        FxRecord(FxSet({ g_wallA }));
        FxHideDefault(g_wallA, false, Stark::kInvokeTimedOutStillQueued);
        const size_t logs = FxLogMark();
        const int32_t rc = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        check("GU.03 L19: 0, and the disable says its un-hide did not run and a restore waits for it",
              rc == 0 && FxLogCount(G3DisabledWaitingLine(0, 1), 'W', kSeeThruCat, logs) == 1, FxLogDump(logs));
        const bool gaveUp = waitFor([] { return !FxRestorePending(); }, kGiveUpWithinMs);
        const auto st = FxStatus();
        check("GU.03 gave up restoring: within 3 s no restore waits, abandoned, WallA recorded and queued",
              gaveUp && !st.restorePending && st.restoreAbandoned && FxSetOf(st.hiddenActors) == FxSet({ g_wallA }) &&
                  FxQueuedNow() == FxSet({ g_wallA }),
              FxStatusStr(st) + " queued " + FxSetStr(FxQueuedNow()));
        check("GU.03 L12: one warning says one actor stays hidden after 1 s",
              FxLogCount(G3GaveUpRestoringLine(1), 'W', kSeeThruCat, logs) == 1, FxLogDump(logs));
        const int passes = G3LogFramed(kG3ResumedPrefix, G3ResumedSuffix(0, 1), 'I', logs);
        check("OBS-O08 a line per pass: at least 15 say 0 restored, 1 still hidden", passes >= kG3MinStalledPasses,
              FxS(passes));
    }

    // GU.04: the give-up counts only the pass's own failures, and the end is decided from the record.
    FxReset();
    {
        G3On("GU.04");
        FxRecord(FxSet({ g_wallA }));
        FxScriptHideN(g_wallA, false, Stark::kInvokeTimedOutStillQueued, kG3Gu04FailingCalls);
        g3_gu04Unhides.store(0);
        g3_gu04PendingAtLine.store(kG3NeverRan);
        g3_gu04Adopted.store(false);
        g3_gu04Line = G3ResumedLine(kPendingMaxMs, 1, 1);
        FxOnHide([](uintptr_t actor, bool hidden) {
            if (actor != g_wallA || hidden) return;
            const int n = g3_gu04Unhides.fetch_add(1) + 1;
            // Only the restore's own pass may adopt: the disable's call runs with s_workerMutex held.
            if (n == kG3Gu04FailingCalls + 1 && Tot::IsBackgroundWorker()) {
                FxAdopt({ g_wallB });
                g3_gu04Adopted.store(true);
            }
        });
        // That pass's L11 is written after it decided, with neither lock held: the flag read there is its decision.
        FxOnLog([](const FxLogLine& l) {
            if (l.lvl == 'I' && l.cat == kSeeThruCat && l.text == g3_gu04Line)
                g3_gu04PendingAtLine.store(FxRestorePending() ? 1 : 0);
        });
        const size_t calls = FxCallMark(), logs = FxLogMark();
        const int32_t rc = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const std::string lastLine = G3ResumedLine(kPendingMaxMs + kPendingTickMs, 1, 0);
        const bool ended = waitFor([&] {
            return !FxRestorePending() && FxRecordNow().empty() && FxLogCount(lastLine, 'I', kSeeThruCat, logs) >= 1;
        }, kGiveUpWithinMs);
        FxOnHide(nullptr);
        FxOnLog(nullptr);
        const auto st = FxStatus();
        const auto unA = G3HideCalls(g_wallA, 0, calls);
        const auto unB = G3HideCalls(g_wallB, 0, calls);
        check("GU.04 fixture: WallA's un-hide failed 20 times, then ran, and that pass adopted WallB",
              rc == 0 && static_cast<int>(unA.size()) == kG3Gu04FailingCalls + 1 && g3_gu04Adopted.load(),
              FxS(static_cast<long long>(unA.size())));
        check("GU.04 no give-up: the pass that adopted B gave nothing up -- its own un-hide ran",
              FxLogPrefix(kG3GaveUpRestoringPrefix, 0, kSeeThruCat, logs) == 0, FxLogDump(logs));
        check("GU.04 decided from the record: the restore was still pending after that pass, with B left in it",
              g3_gu04PendingAtLine.load() == 1, FxS(g3_gu04PendingAtLine.load()) + FxLogDump(logs));
        check("GU.04 B restored: within 1 s of its adoption B is un-hidden, the record empty, none waits or gave up",
              ended && unB.size() == 1 && !unA.empty() && FxMs(unB[0].t - unA.back().t) <= kRestoreWithinMs &&
                  st.hiddenCount == 0 && !st.restorePending && !st.restoreAbandoned,
              FxStatusStr(st) + " calls: " + G3CallsStr(calls));
    }

    // GU.05: a waiting restore whose record was taken away ends at its next pass without a give-up.
    FxReset();
    {
        G3On("GU.05");
        FxRecord(FxSet({ g_wallA }));
        g_responsive.store(false);
        const size_t calls = FxCallMark(), logs = FxLogMark();
        G3Off("GU.05");
        uint64_t taken[kG3TakeCap] = {};
        const int32_t n = Schlacht::TakeLeftovers(taken, kG3TakeCap);
        const bool ended = waitFor([] { return !FxRestorePending(); }, kQuietMs);
        const auto st = FxStatus();
        check("GU.05 taken while a restore waits: 1 (WallA), and the restore ends within 300 ms, nothing abandoned",
              n == 1 && taken[0] == g_wallA && ended && !st.restoreAbandoned && FxRecordNow().empty(),
              FxS(n) + " " + FxStatusStr(st));
        check("GU.05 quiet: no give-up line and no call",
              FxLogCount(G3GaveUpWaitingLine(), 0, kSeeThruCat, logs) == 0 && FxCount(FxKind::Hide, calls) == 0,
              G3CallsStr(calls) + FxLogDump(logs));
    }
}

static void Blocks_G3() {
    G3HideCodes();
    G3DispatchFail();
    G3Enable();
    G3Disable();
    G3Pending();
    G3GiveUp();
}

// ══ G4: HANDOFF, OWNERS, STATUS, WORKER, INV, SHUTDOWN ═══════════════════════════════════════════════════════════
// These run after every block that trips a one-shot latch, and none of them depends on one. SHUTDOWN is last: Tot's
// shutdown flag is process-wide, and under it an enable is refused and no deferred restore can start.


namespace g4 {

constexpr int32_t kTakeCap = 8;   // a TakeLeftovers buffer with room past every record built here
// The worker sleeps one whole tick before its first; the floor leaves the timer 10 ms of slack.
constexpr int kFirstTickFloorMs = Grimoire::SCHLACHT_TICK_MS - 10;
constexpr int kInitHoldMs = 5 * Grimoire::SCHLACHT_TICK_MS;     // five ticks the init gate must hold back
constexpr int kThrowWindowMs = 6 * Grimoire::SCHLACHT_TICK_MS;  // six ticks, for at least three that throw
constexpr int kTicksWanted = 3;   // enough ticks to tell a once-per-run line from a once-per-tick one
constexpr int kWorkerWithinMs = 1000;    // a real worker's first ticks land within this
// Past the moment a waiting restore would have given up (1 s under the seam), with half as much again to spare.
constexpr int kAfterGiveUpWatchMs = kPendingMaxMs + kPendingMaxMs / 2;
constexpr int32_t kNotReturned = INT32_MIN;   // no SetEnabled answer is this
// Stark's "timed out, still queued": an invoke that did not run, so the fake engine writes nothing for it.
constexpr int32_t kDidNotRun = -5;
constexpr double kNearT = 500, kFarT = 800;   // distances along the ray: WallA in front of WallB

constexpr const char* kWorkerStopped = "SeeThrough: worker stopped";
constexpr const char* kRestoreWaits =
    "SeeThrough: waiting for the game thread to resume so the leftover hidden actor(s) can be restored";
constexpr const char* kResumedPrefix = "SeeThrough: game thread resumed after";
constexpr const char* kGaveUpPrefix = "SeeThrough: gave up";   // both give-ups: waiting, and restoring
constexpr const char* kShutdownEndedPrefix = "SeeThrough: a shutdown ended the deferred restore";
constexpr const char* kEnabledPrefix = "SeeThrough: enabled (pierce=";
constexpr const char* kTickThrewPrefix = "SeeThrough: tick threw";
constexpr const char* kLastHolderLeft = "SeeThrough: its last holder left -- turning it off";
constexpr const char* kRefusedNoTrace = "SeeThrough: refusing to enable — KismetSystemLibrary::LineTraceSingle not "
                                        "found (cooked out?), so nothing could be hidden";

static std::string WorkerStarted() {
    return FxFmt("SeeThrough: worker started (%d ms tick)", Grimoire::SCHLACHT_TICK_MS);
}
static std::string TickThrew(const char* what) {
    return FxFmt("SeeThrough: tick threw (%s) — skipping (game tearing down?)", what);
}
static std::string Disabled(int restored) { return FxFmt("SeeThrough: disabled (%d restored)", restored); }
static std::string HolderLeft(int left) { return FxFmt("SeeThrough: a holder left; %d still hold it on", left); }
static std::string ShutdownEnded(int left) {
    return FxFmt("SeeThrough: a shutdown ended the deferred restore with %d actor(s) still hidden — the next "
                 "See-through ON restores them", left);
}
static std::string OffUnresponsiveNoWait(int kept) {
    return FxFmt("SeeThrough: disabled but %d actor(s) remain hidden (game thread unresponsive) and a shutdown is "
                 "under way, so no restore waits — the next See-through ON restores them", kept);
}
static std::string OffKeptNoWait(int restored, int kept) {
    return FxFmt("SeeThrough: disabled (%d restored); %d un-hide(s) did not run and a shutdown is under way, so no "
                 "restore waits — the next See-through ON restores them", restored, kept);
}
static std::string OffUnresponsiveWaits(int kept) {
    return FxFmt("SeeThrough: disabled but %d actor(s) remain hidden (game thread unresponsive) — waiting for it to "
                 "resume to restore them", kept);
}

// The fake game's hidden flag, set as an earlier copy's hide would have left it: no hide of this run made it so.
static void GameHiddenSet(uintptr_t actor, bool hidden) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_gameHidden[actor] = static_cast<uint8_t>(hidden ? 1 : 0);
}

// For `got`: the SetActorHiddenInGame calls since `from`, as Name:bNewHidden=rc, marked when made on main.
static std::string HidesStr(size_t from) {
    std::string out;
    for (const auto& c : FxCallsOf(FxKind::Hide, from))
        out += FxFmt(" %s:%d=%d%s", FxNameOf(c.instance).c_str(), FxHideByte(c), c.rc, c.onMain ? "(main)" : "");
    return out.empty() ? std::string(" hides: none") : " hides:" + out;
}

// Non-empty, and every call made on one thread that is not main and is marked a background worker.
static bool AllBackground(const std::vector<FxCall>& calls) {
    if (calls.empty()) return false;
    for (const auto& c : calls)
        if (c.onMain || !c.background || c.tid != calls.front().tid) return false;
    return true;
}
// Non-empty, and every call found s_mutex free while it ran.
static bool AllMutexFree(const std::vector<FxCall>& calls) {
    if (calls.empty()) return false;
    for (const auto& c : calls)
        if (!c.mutexFree) return false;
    return true;
}

// The record and the restore flag as they stood when a line starting with `prefix` was written -- taken on the
// writer's own thread, which holds neither of Schlacht's locks for that line, so the state cannot move under it.
struct PassSnap {
    std::atomic<bool> taken{false};
    std::unordered_set<uintptr_t> record;
    bool pending = false;
};
static void SnapAtFirst(const std::shared_ptr<PassSnap>& snap, const char* prefix) {
    const std::string want(prefix);
    FxOnLog([snap, want](const FxLogLine& l) {
        if (l.cat != kSeeThruCat || l.text.compare(0, want.size(), want) != 0 || snap->taken.load()) return;
        snap->record = FxRecordNow();
        snap->pending = FxRestorePending();
        snap->taken.store(true);   // published last: a reader that sees it sees the two fields above
    });
}

}  // namespace g4

static void Blocks_G4() {
    const uintptr_t A = g_wallA, B = g_wallB, C = g_wallC, D = g_wallD;
    const uintptr_t W4 = g_wall[kIdxWall4 - kIdxWallA];
    const char* const cat = kSeeThruCat;

    // ══ HANDOFF ══════════════════════════════════════════════════════════════════════════════════════════════════
    blk("HANDOFF - TakeLeftovers and AdoptLeftovers: what moves, what a restore does with it, and the lock order");
    {
        FxReset();
        const int32_t on = FxEnableSync();
        FxRecord(FxSet({A}));   // written while on: an enable un-hides a record written before it
        uint64_t out[g4::kTakeCap] = {};
        const int32_t n = Schlacht::TakeLeftovers(out, g4::kTakeCap);
        const auto st = FxStatus();
        check("HO.01 take while on: TakeLeftovers moves nothing while See-through is on -- 0, the record {WallA} and "
              "hiddenCount 1 kept",
              on == 1 && n == 0 && out[0] == 0 && FxRecordNow() == FxSet({A}) && st.hiddenCount == 1 && st.active,
              "n=" + FxS(n) + " " + FxStatusStr(st));
    }
    {
        FxReset();
        FxRecord(FxSet({A}));   // off, with a leftover: the one case where a take would move something
        uint64_t out[g4::kTakeCap] = {};
        const int32_t nNull = Schlacht::TakeLeftovers(nullptr, g4::kTakeCap);
        const int32_t nZero = Schlacht::TakeLeftovers(out, 0);
        const int32_t nNeg = Schlacht::TakeLeftovers(out, -1);
        const auto st = FxStatus();
        check("HO.02 take refuses: a null buffer, a cap of 0 and a cap of -1 each take 0 -- the record {WallA} unchanged",
              nNull == 0 && nZero == 0 && nNeg == 0 && out[0] == 0 && FxRecordNow() == FxSet({A}) && st.hiddenCount == 1,
              "null=" + FxS(nNull) + " zero=" + FxS(nZero) + " neg=" + FxS(nNeg) + " " + FxStatusStr(st));
    }
    {
        FxReset();
        FxRecord(FxSet({A, B, C}));
        FxSetQueued(FxSet({B}));
        FxSetAbandoned(true);
        uint64_t out[g4::kTakeCap] = {};
        const int32_t n2 = Schlacht::TakeLeftovers(out, 2);
        const auto rec = FxRecordNow();
        const auto queued = FxQueuedNow();
        const auto st = FxStatus();
        const auto abc = FxSet({A, B, C});
        const uintptr_t t0 = static_cast<uintptr_t>(out[0]), t1 = static_cast<uintptr_t>(out[1]);
        const bool takenOk = n2 == 2 && t0 != t1 && abc.count(t0) != 0 && abc.count(t1) != 0 && out[2] == 0;
        const bool restOk = rec.size() == 1 && abc.count(*rec.begin()) != 0 && rec.count(t0) == 0 && rec.count(t1) == 0;
        const bool bTaken = t0 == B || t1 == B;
        const auto wantQueued = bTaken ? std::unordered_set<uintptr_t>{} : FxSet({B});
        const std::string got = "took " + FxNameOf(t0) + "," + FxNameOf(t1) + " left " + FxSetStr(rec) + " queued " +
                                FxSetStr(queued) + " " + FxStatusStr(st);
        check("HO.03 take two of three: 2 distinct members of {WallA, WallB, WallC} come out, the third stays, "
              "hiddenCount 1",
              takenOk && restOk && st.hiddenCount == 1, got);
        check("HO.03 take two of three: a partial take keeps restoreAbandoned, and the queue loses the taken actors only",
              st.restoreAbandoned && queued.count(t0) == 0 && queued.count(t1) == 0 && queued == wantQueued, got);
        uint64_t rest[g4::kTakeCap] = {};
        const int32_t n1 = Schlacht::TakeLeftovers(rest, g4::kTakeCap);
        const auto st2 = FxStatus();
        check("HO.03 take the rest: 1, the last actor -- the record and the queue empty, hiddenCount 0, "
              "restoreAbandoned cleared",
              n1 == 1 && restOk && static_cast<uintptr_t>(rest[0]) == *rec.begin() && rest[1] == 0 &&
                  FxRecordNow().empty() && FxQueuedNow().empty() && st2.hiddenCount == 0 && !st2.restoreAbandoned,
              "n=" + FxS(n1) + " took " + FxNameOf(static_cast<uintptr_t>(rest[0])) + " " + FxStatusStr(st2));
    }
    {
        FxReset();
        const size_t calls0 = FxCallMark();
        FxAdopt({A, 0, B});
        const auto st = FxStatus();
        check("HO.04 adopt while off: the zero entry is skipped -- the record {WallA, WallB}, hiddenCount 2, and a "
              "restore waits",
              FxRecordNow() == FxSet({A, B}) && st.hiddenCount == 2 && st.restorePending && !st.active,
              FxStatusStr(st));
        const bool restored = waitFor([] { return !FxRestorePending() && FxRecordNow().empty(); }, kRestoreWithinMs);
        const auto hides = FxCallsOf(FxKind::Hide, calls0);
        check("HO.04 adopt while off: within 1 s the deferred restore un-hides WallA and WallB once each, on one "
              "background thread off main -- the record empties",
              restored && FxHides(A, 0, calls0) == 1 && FxHides(B, 0, calls0) == 1 && hides.size() == 2 &&
                  g4::AllBackground(hides),
              g4::HidesStr(calls0) + " " + FxStatusStr(FxStatus()));
        check("HO.04 INV.01: the deferred restore's un-hides ran with s_mutex free", g4::AllMutexFree(hides),
              g4::HidesStr(calls0));
    }
    {
        FxReset();
        FxRecord(FxSet({A}));
        const size_t log0 = FxLogMark();
        const uint64_t one[1] = { B };
        Schlacht::AdoptLeftovers(nullptr, 3);
        Schlacht::AdoptLeftovers(one, 0);
        Schlacht::AdoptLeftovers(one, -1);
        const auto st = FxStatus();
        check("HO.05 adopt refuses: a null list, n 0 and n -1 change nothing -- the record {WallA}, no restore started, "
              "no 'waiting for the game thread' line",
              FxRecordNow() == FxSet({A}) && st.hiddenCount == 1 && !st.restorePending && !FxPendingJoinable() &&
                  FxLogCount(g4::kRestoreWaits, 'I', cat, log0) == 0,
              FxStatusStr(st) + FxLogDump(log0));
    }
    {
        FxReset();
        const size_t calls0 = FxCallMark();
        FxAdopt({0, 0});
        const bool ended = waitFor([] { return !FxRestorePending(); }, kQuietMs);
        check("OBS-ADOPT0 (pin) adopt zeros while off: the record stays {} with no SetActorHiddenInGame, and no restore "
              "waits within 300 ms",
              ended && FxRecordNow().empty() && FxStatus().hiddenCount == 0 && FxCount(FxKind::Hide, calls0) == 0,
              FxStatusStr(FxStatus()) + g4::HidesStr(calls0));
    }
    {
        FxReset();
        const int32_t on = FxEnableSync();
        FxRecord(FxSet({A}));
        FxAdopt({C, B});
        const auto st = FxStatus();
        check("HO.07 adopt while on: (a) the record {WallA, WallB, WallC}, hiddenCount 3, and no restore starts while on",
              on == 1 && FxRecordNow() == FxSet({A, B, C}) && st.hiddenCount == 3 && !st.restorePending &&
                  !FxPendingJoinable(),
              FxStatusStr(st));
        Schlacht::SetPierceCount(2);
        FxScene({ FxHitAt(kIdxWallA, g4::kNearT), FxHitAt(kIdxWallB, g4::kFarT) });
        const size_t calls0 = FxCallMark();
        FxTickNow();
        check("HO.07 adopt while on: (b) the next tick, WallA and WallB still occluding at pierce 2, calls nothing on "
              "either -- the adopted WallB counts hidden with no call",
              FxHides(A, -1, calls0) == 0 && FxHides(B, -1, calls0) == 0, g4::HidesStr(calls0));
        const auto unhides = FxCallsOf(FxKind::Hide, calls0);
        check("HO.07 adopt while on: (c) WallC, no longer occluding, is un-hidden exactly once and dropped -- the record "
              "{WallA, WallB}",
              FxHides(C, 0, calls0) == 1 && unhides.size() == 1 && FxRecordNow() == FxSet({A, B}) &&
                  FxStatus().hiddenCount == 2,
              g4::HidesStr(calls0) + " " + FxStatusStr(FxStatus()));
        check("HO.07 INV.01: the tick's un-hide ran with s_mutex free", g4::AllMutexFree(unhides), g4::HidesStr(calls0));
    }
    {
        // Adopted before the tick reads the record: the tick diffs it with the rest of the record.
        FxReset();
        const int32_t on = FxEnableSync();
        FxRecord(FxSet({A}));
        FxScene({ FxHitAt(kIdxWallA, g4::kNearT) });
        // On main inside FxTickNow, which holds neither of Schlacht's locks, so the hook may adopt.
        FxOnTrace([D](int index) {
            if (index == 0) FxAdopt({D});
        });
        const size_t calls0 = FxCallMark();
        FxTickNow();
        FxOnTrace(nullptr);
        check("HO.08 adopt during a trace: the tick diffs WallD with the record it joined -- not occluding, so un-hidden "
              "at once and dropped, WallA kept",
              on == 1 && FxCount(FxKind::Trace, calls0) >= 1 && FxHides(D, 0, calls0) == 1 &&
                  FxHides(A, -1, calls0) == 0 && FxRecordNow() == FxSet({A}),
              FxSetStr(FxRecordNow()) + g4::HidesStr(calls0));
    }
    {
        // Adopted after the tick read the record, while its diff runs: the write-back keeps it (T9).
        FxReset();
        const int32_t on = FxEnableSync();
        FxScene({ FxHitAt(kIdxWallA, g4::kNearT) });
        // The diff's hide of WallA runs on main inside FxTickNow with neither of Schlacht's locks held.
        FxOnHide([A, D](uintptr_t actor, bool hidden) {
            if (actor == A && hidden) FxAdopt({D});
        });
        const size_t calls0 = FxCallMark();
        FxTickNow();
        FxOnHide(nullptr);
        const auto rec = FxRecordNow();
        check("HO.08 adopt during the diff (T9): an actor adopted while the tick hid an occluder survives its "
              "write-back -- the record {WallA, WallD}",
              on == 1 && FxHides(A, 1, calls0) == 1 && FxHides(D, -1, calls0) == 0 && rec == FxSet({A, D}) &&
                  FxStatus().hiddenCount == 2,
              FxSetStr(rec) + g4::HidesStr(calls0));
        const size_t calls1 = FxCallMark();
        FxTickNow();
        check("HO.08 adopt during the diff: the next tick (scene {WallA}) un-hides WallD and leaves WallA -- the record "
              "{WallA}",
              FxHides(D, 0, calls1) == 1 && FxHides(A, -1, calls1) == 0 && FxRecordNow() == FxSet({A}),
              g4::HidesStr(calls1) + " " + FxSetStr(FxRecordNow()));
    }
    {
        // Adopted while the write-back waits for s_mutex: the merge reads the record under that lock, so it sees what an
        // adoption put there just before.
        FxReset();
        const int32_t on = FxEnableSync();
        FxScene({ FxHitAt(kIdxWallA, g4::kNearT) });
        auto locked = std::make_shared<std::atomic<bool>>(false);
        std::thread holder;
        // The diff's hide of WallA is the tick's last invoke; a helper takes s_mutex before the hook returns, so the
        // write-back that follows has to wait for it.
        FxOnHide([&holder, locked, A, D](uintptr_t actor, bool hidden) {
            if (actor != A || !hidden) return;
            holder = std::thread([locked, D] {
                std::lock_guard<std::mutex> lk(s_mutex);
                locked->store(true);
                Sleep(kHoldMs);
                s_state.hiddenActors.insert(D);
                s_state.hiddenCount = static_cast<int32_t>(s_state.hiddenActors.size());
            });
            while (!locked->load()) Sleep(1);
        });
        FxTickNow();
        FxOnHide(nullptr);
        if (holder.joinable()) holder.join();
        check("HO.08 adopt while the write-back waits: the merge reads the record under s_mutex -- the record "
              "{WallA, WallD}",
              on == 1 && locked->load() && FxRecordNow() == FxSet({A, D}) && FxStatus().hiddenCount == 2,
              FxSetStr(FxRecordNow()));
    }
    {
        FxReset();
        // On the deferred restore's own thread, which holds neither lock during its un-hides, so the hook may adopt.
        auto fired = std::make_shared<std::atomic<bool>>(false);
        FxOnHide([fired, A, B](uintptr_t actor, bool hidden) {
            if (actor == A && !hidden && !fired->exchange(true)) FxAdopt({B});
        });
        auto snap = std::make_shared<g4::PassSnap>();
        g4::SnapAtFirst(snap, g4::kResumedPrefix);
        const size_t calls0 = FxCallMark();
        const size_t log0 = FxLogMark();
        FxAdopt({A});
        const bool snapped = waitFor([snap] { return snap->taken.load(); }, kRestoreWithinMs);
        check("HO.09 adopt mid-pass (Q3): an actor adopted during the pass's un-hide of WallA is kept -- after that "
              "pass the record is {WallB} and the restore still waits",
              snapped && fired->load() && snap->record == FxSet({B}) && snap->pending,
              snapped ? "record " + FxSetStr(snap->record) + " pending=" + FxS(snap->pending ? 1 : 0)
                      : "no 'game thread resumed' line" + FxLogDump(log0));
        const bool restored = waitFor([] { return !FxRestorePending() && FxRecordNow().empty(); }, kRestoreWithinMs);
        FxOnLog(nullptr);
        FxOnHide(nullptr);
        check("HO.09 adopt mid-pass: within 1 s the next pass un-hides WallB -- the record empty, no restore waiting, "
              "none abandoned",
              restored && FxHides(A, 0, calls0) == 1 && FxHides(B, 0, calls0) == 1 && !FxStatus().restoreAbandoned,
              g4::HidesStr(calls0) + " " + FxStatusStr(FxStatus()));
    }
    {
        FxReset();
        const int32_t on = FxEnableSync();
        FxRecord(FxSet({A, B}));
        g_responsive.store(false);
        const size_t log0 = FxLogMark();
        const int32_t off = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const bool gaveUp = waitFor([] {
            const auto s = FxStatus();
            return s.restoreAbandoned && !s.restorePending;
        }, kGiveUpWithinMs);
        FxAdopt({C});
        const auto st = FxStatus();
        check("HO.10 adopt after a give-up: restoreAbandoned clears and a new restore waits -- the record "
              "{WallA, WallB, WallC}",
              on == 1 && off == 0 && gaveUp && !st.restoreAbandoned && st.restorePending &&
                  FxRecordNow() == FxSet({A, B, C}) && st.hiddenCount == 3 &&
                  FxLogCount(g4::OffUnresponsiveWaits(2), 'W', cat, log0) == 1,
              FxStatusStr(st) + FxLogDump(log0));
    }
    {
        FxReset();
        g_responsive.store(false);   // the restore the adoption starts then waits, so Wall4 is still recorded to be read
        std::atomic<bool> returned{false};
        s_workerMutex.lock();
        std::thread adopter([&returned, W4] {
            FxAdopt({W4});
            returned.store(true);
        });
        const bool early = waitFor([&returned] { return returned.load(); }, kHoldMs);
        const bool innerFree = FxFreeElsewhere(s_mutex);
        const bool untouched = FxRecordNow().count(W4) == 0;
        s_workerMutex.unlock();
        adopter.join();
        check("HO.11 lock order: AdoptLeftovers takes s_workerMutex first -- held off 200 ms with the record untouched, "
              "while s_mutex stays free",
              !early && untouched && innerFree,
              "returned=" + FxS(early ? 1 : 0) + " untouched=" + FxS(untouched ? 1 : 0) + " s_mutex free=" +
                  FxS(innerFree ? 1 : 0));
        check("HO.11 lock order: released, it returns and Wall4 is in the record",
              returned.load() && FxRecordNow().count(W4) == 1, FxSetStr(FxRecordNow()));
    }
    {
        // TakeLeftovers moves the record under s_mutex: held off while another thread holds it, then it takes WallA.
        FxReset();
        FxRecord(FxSet({A}));
        std::atomic<bool> returned{false};
        int32_t taken = -1;
        uint64_t out[g4::kTakeCap] = {};
        s_mutex.lock();
        std::thread taker([&returned, &taken, &out] {
            taken = Schlacht::TakeLeftovers(out, g4::kTakeCap);
            returned.store(true);
        });
        const bool early = waitFor([&returned] { return returned.load(); }, kHoldMs);
        s_mutex.unlock();
        taker.join();
        check("HO.12 take under s_mutex: held off while s_mutex is held, then it takes WallA and the record empties",
              !early && taken == 1 && out[0] == A && FxRecordNow().empty(),
              "returned early=" + FxS(early ? 1 : 0) + " taken=" + FxS(taken) + " " + FxSetStr(FxRecordNow()));
    }

    // ══ OWNERS ═══════════════════════════════════════════════════════════════════════════════════════════════════
    blk("OWNERS - who holds See-through on: claims, Release, an explicit OFF, and the locks stillLive is asked under");
    {
        FxReset();
        const int32_t on = FxEnableSync(kClientA);
        const auto a = FxStatus(kClientA), b = FxStatus(kClientB), none = FxStatus(Schlacht::kOwnerNone);
        check("OW.01 claim: SetEnabled(true, 0x1111) == 1 records one claim -- callerClaimed for 0x1111, not for "
              "0x2222 or kOwnerNone; ceClaimed F",
              on == 1 && a.claims == 1 && a.callerClaimed && !a.ceClaimed && !b.callerClaimed && !none.callerClaimed,
              FxStatusStr(a));
        const int32_t ce = Schlacht::SetEnabled(true, Schlacht::kOwnerCe);
        const auto s2 = FxStatus(kClientA);
        check("OW.02 CE claim: SetEnabled(true, kOwnerCe) == 1 adds CE's claim -- claims 2, ceClaimed T",
              ce == 1 && s2.claims == 2 && s2.ceClaimed && s2.callerClaimed, FxStatusStr(s2));
        const int32_t again = Schlacht::SetEnabled(true, kClientA);
        const auto s3 = FxStatus(kClientA);
        check("OW.02 claimed once: a second SetEnabled(true, 0x1111) == 1 records nothing new -- claims stay 2",
              again == 1 && s3.claims == 2, FxStatusStr(s3));
    }
    {
        FxReset();
        const int32_t on = FxEnableSync(Schlacht::kOwnerNone);
        const auto st = FxStatus();
        check("OBS-O10 (pin) no owner: from off, SetEnabled(true, kOwnerNone) == 1 turns it on with no claim -- "
              "active T, claims 0",
              on == 1 && st.active && st.claims == 0 && !st.ceClaimed, FxStatusStr(st));
    }
    {
        FxReset();
        g_hookUp.store(false);
        const int32_t rc = Schlacht::SetEnabled(true, kClientA);
        g_hookUp.store(true);
        const auto st = FxStatus(kClientA);
        check("OW.04 a refusal claims nothing: a hook-down enable from off == -5 and records no claim",
              rc == Schlacht::STR_ERR_NO_HOOK && st.claims == 0 && !st.callerClaimed && !st.active,
              "rc=" + FxS(rc) + " " + FxStatusStr(st));
    }
    {
        FxReset();
        const int32_t on = FxEnableSync(kClientA);
        g_hookUp.store(false);
        const int32_t refused = Schlacht::SetEnabled(true, kClientC);
        const auto st = FxStatus(kClientC);
        check("OBS-O02b (pin) hook down while on: SetEnabled(true, 0x3333) == -5 writes code and state -5, stays on, "
              "and does not record 0x3333",
              on == 1 && refused == Schlacht::STR_ERR_NO_HOOK && st.active && st.state == Schlacht::STR_ERR_NO_HOOK &&
                  st.code == Schlacht::STR_ERR_NO_HOOK && st.claims == 1 && !st.callerClaimed &&
                  FxStatus(kClientA).callerClaimed,
              "rc=" + FxS(refused) + " " + FxStatusStr(st));
        FxScene({ FxHitAt(kIdxWallA, g4::kNearT) });
        const size_t calls0 = FxCallMark();
        FxTickNow();
        g_hookUp.store(true);
        check("OBS-O02b (pin) hook down while on: a tick still traces", FxCount(FxKind::Trace, calls0) >= 1,
              "traces=" + FxS(FxCount(FxKind::Trace, calls0)));
    }
    {
        FxReset();
        const int32_t on = FxEnableSync(kClientA);
        int asked = 0;
        const std::function<bool(uint64_t)> counting = [&asked](uint64_t) {
            ++asked;
            return true;
        };
        size_t log0 = FxLogMark();
        const auto r5 = Schlacht::Release(kClientNever, counting);
        const auto s5 = FxStatus();
        check("OW.05 not held: Release(0x9999) == NotHeld -- stillLive never asked, no SEETHRU line, claims unchanged",
              on == 1 && r5 == Schlacht::OwnerRelease::NotHeld && asked == 0 && FxSeeThruLines(log0) == 0 &&
                  s5.claims == 1 && s5.active,
              "asked=" + FxS(asked) + " " + FxStatusStr(s5) + FxLogDump(log0));
        log0 = FxLogMark();
        const auto r6 = Schlacht::Release(Schlacht::kOwnerNone, counting);
        const auto s6 = FxStatus();
        check("OW.06 kOwnerNone: Release(kOwnerNone) == NotHeld -- stillLive never asked, nothing changes",
              r6 == Schlacht::OwnerRelease::NotHeld && asked == 0 && FxSeeThruLines(log0) == 0 && s6.claims == 1 &&
                  s6.active,
              "asked=" + FxS(asked) + " " + FxStatusStr(s6) + FxLogDump(log0));
    }
    {
        // OW.07 to OW.09 run on, holder by holder: claims {0x1111, CE}, then {CE}, then none.
        FxReset();
        const int32_t on = FxEnableSync(kClientA);
        const int32_t ce = Schlacht::SetEnabled(true, Schlacht::kOwnerCe);
        int asked = 0;
        uint64_t askedFor = 0;
        size_t log0 = FxLogMark();
        const auto r7 = Schlacht::Release(kClientA, [&asked, &askedFor](uint64_t o) {
            ++asked;
            askedFor = o;
            return true;
        });
        const auto s7 = FxStatus(kClientA);
        check("OW.07 still live: Release(0x1111, live) == StillHeld -- stillLive asked once, for 0x1111; claims 2; "
              "neither the last-holder nor the holder-left line",
              on == 1 && ce == 1 && r7 == Schlacht::OwnerRelease::StillHeld && asked == 1 && askedFor == kClientA &&
                  s7.claims == 2 && s7.callerClaimed && FxSeeThruLines(log0) == 0,
              "asked=" + FxS(asked) + " for " + FxHex(static_cast<uintptr_t>(askedFor)) + " " + FxStatusStr(s7) +
                  FxLogDump(log0));
        log0 = FxLogMark();
        const auto r8 = Schlacht::Release(kClientA, [](uint64_t) { return false; });
        const auto s8 = FxStatus(kClientA);
        check("OW.08 a holder left: Release(0x1111, gone) == StillHeld with CE still holding -- claims 1, one I "
              "'a holder left; 1 still hold it on', still on",
              r8 == Schlacht::OwnerRelease::StillHeld && s8.claims == 1 && s8.ceClaimed && !s8.callerClaimed &&
                  s8.active && FxLogCount(g4::HolderLeft(1), 'I', cat, log0) == 1 && FxSeeThruLines(log0) == 1,
              FxStatusStr(s8) + FxLogDump(log0));
        FxRecord(FxSet({A}));
        log0 = FxLogMark();
        const size_t calls0 = FxCallMark();
        const auto r9 = Schlacht::Release(Schlacht::kOwnerCe, nullptr);
        const auto s9 = FxStatus();
        check("OW.09 the last holder left: Release(kOwnerCe, no stillLive) == TurnOff -- I 'its last holder left -- "
              "turning it off', off with state 0, claims 0",
              r9 == Schlacht::OwnerRelease::TurnOff && FxLogCount(g4::kLastHolderLeft, 'I', cat, log0) == 1 &&
                  !s9.active && s9.state == 0 && s9.claims == 0 && !s9.ceClaimed,
              FxStatusStr(s9) + FxLogDump(log0));
        const auto unhides = FxCallsOf(FxKind::Hide, calls0);
        const uint64_t left = FxLogSeq(g4::kLastHolderLeft, 'I', cat, log0);
        check("OW.09 the last holder left: the OFF that follows the line un-hides WallA as an explicit OFF would -- "
              "the record empty, 'disabled (1 restored)'",
              FxHides(A, 0, calls0) == 1 && unhides.size() == 1 && left != 0 && left < unhides.front().seq &&
                  FxRecordNow().empty() && FxLogCount(g4::Disabled(1), 'I', cat, log0) == 1,
              g4::HidesStr(calls0) + FxLogDump(log0));
        check("OW.09 INV.01: Release's un-hide ran with s_mutex free", g4::AllMutexFree(unhides), g4::HidesStr(calls0));
    }
    {
        FxReset();
        const int32_t on = FxEnableSync(kClientA);
        const int32_t ce = Schlacht::SetEnabled(true, Schlacht::kOwnerCe);
        const int32_t off = Schlacht::SetEnabled(false, kClientD);
        const auto st = FxStatus(kClientA);
        check("OW.10 an explicit OFF: SetEnabled(false, 0x5555) == 0 from a client that never claimed drops every "
              "claim -- claims 0, ceClaimed F, off",
              on == 1 && ce == 1 && off == 0 && st.claims == 0 && !st.ceClaimed && !st.callerClaimed && !st.active,
              FxStatusStr(st));
    }
    {
        FxReset();
        const int32_t on = FxEnableSync(kClientA);
        bool asked = false, outerHeld = false, innerFree = false;
        const auto r = Schlacht::Release(kClientA, [&asked, &outerHeld, &innerFree](uint64_t) {
            asked = true;
            outerHeld = !FxFreeElsewhere(s_workerMutex);
            innerFree = FxFreeElsewhere(s_mutex);
            return true;
        });
        check("OW.11 stillLive's locks: it is asked with s_workerMutex held and s_mutex free",
              on == 1 && r == Schlacht::OwnerRelease::StillHeld && asked && outerHeld && innerFree,
              "asked=" + FxS(asked ? 1 : 0) + " s_workerMutex held=" + FxS(outerHeld ? 1 : 0) + " s_mutex free=" +
                  FxS(innerFree ? 1 : 0));
    }

    // ══ STATUS ═══════════════════════════════════════════════════════════════════════════════════════════════════
    blk("STATUS - GetStatus, IsActive and StopWorker");
    {
        FxReset();
        const int32_t on = FxEnableSync();
        FxSetPtr(g_lp0, FxOff::kPlayerController, 0);
        FxTickNow();
        Schlacht::SeeThroughStatus st;
        const int32_t ret = Schlacht::GetStatus(st, Schlacht::kOwnerNone);
        check("ST.02 GetStatus returns the code: -2 after a tick with no PlayerController, while status.state stays 1",
              on == 1 && ret == Schlacht::STR_ERR_NO_PAWN && st.code == Schlacht::STR_ERR_NO_PAWN && st.state == 1 &&
                  st.active && !st.hasTarget,
              "ret=" + FxS(ret) + " " + FxStatusStr(st));
    }
    {
        FxReset();
        FxRecord(FxSet({A, B, C}));
        const auto st = FxStatus();
        check("ST.03 the list: status.hiddenActors as a set is {WallA, WallB, WallC}, with no duplicate, and its size "
              "is hiddenCount",
              FxSetOf(st.hiddenActors) == FxSet({A, B, C}) && st.hiddenActors.size() == 3 && st.hiddenCount == 3,
              FxStatusStr(st));
    }
    {
        FxReset();
        const bool off0 = Schlacht::IsActive();
        const int32_t on = Schlacht::SetEnabled(true, kClientA);
        const bool started = FxWorkerJoinable();
        const bool on1 = Schlacht::IsActive();
        Schlacht::StopWorker();
        const bool on2 = Schlacht::IsActive();
        const bool stopped = !FxWorkerJoinable();
        const int32_t off = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const bool off3 = Schlacht::IsActive();
        check("ST.04 IsActive: F while off, T after a real SetEnabled(true) that started a worker, still T after "
              "StopWorker, F after SetEnabled(false)",
              !off0 && on == 1 && started && on1 && on2 && stopped && off == 0 && !off3,
              FxFmt("off=%d on=%d worker=%d after-stop=%d stopped=%d after-off=%d", off0 ? 1 : 0, on1 ? 1 : 0,
                    started ? 1 : 0, on2 ? 1 : 0, stopped ? 1 : 0, off3 ? 1 : 0));
    }
    {
        FxReset();
        FxScene({ FxHitAt(kIdxWallA, g4::kNearT) });
        const int32_t on = Schlacht::SetEnabled(true, kClientA);
        const bool ticking = waitFor([A] { return FxRecordNow() == FxSet({A}); }, g4::kWorkerWithinMs);
        const size_t log0 = FxLogMark();
        Schlacht::StopWorker();
        const uint64_t returnedAt = FxStamp();
        const size_t calls0 = FxCallMark();
        Sleep(static_cast<DWORD>(kQuietMs));
        const uint64_t stopped = FxLogSeq(g4::kWorkerStopped, 'I', cat, log0);
        check("ST.05 StopWorker (S3): the worker stops -- 'worker stopped' logged before it returns, then no invoke for "
              "300 ms",
              on == 1 && ticking && stopped != 0 && stopped < returnedAt && FxCountAll(calls0) == 0 &&
                  !FxWorkerJoinable(),
              "ticking=" + FxS(ticking ? 1 : 0) + " invokes after=" + FxS(FxCountAll(calls0)) + FxLogDump(log0));
        check("ST.05 StopWorker leaves the rest: still active, the record {WallA} unchanged",
              Schlacht::IsActive() && FxRecordNow() == FxSet({A}) && FxStatus().hiddenCount == 1,
              FxStatusStr(FxStatus()));
        const size_t log1 = FxLogMark();
        const size_t calls1 = FxCallMark();
        const int32_t again = Schlacht::SetEnabled(true, kClientA);
        Sleep(static_cast<DWORD>(kQuietMs));
        check("OBS-O09 (pin) no restart: SetEnabled(true) after StopWorker == 1 with no new 'worker started' and no "
              "invoke for 300 ms",
              again == 1 && FxLogCount(g4::WorkerStarted(), 'I', cat, log1) == 0 && FxCountAll(calls1) == 0 &&
                  !FxWorkerJoinable(),
              "rc=" + FxS(again) + " invokes=" + FxS(FxCountAll(calls1)) + FxLogDump(log1));
        const int32_t off = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        check("OBS-O09 (pin) no restart: SetEnabled(false) then turns it off", off == 0 && !Schlacht::IsActive(),
              FxStatusStr(FxStatus()));
    }
    {
        FxReset();
        const int32_t on = FxEnableSync();
        FxRecord(FxSet({A}));
        g_responsive.store(false);
        const int32_t off = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const bool waiting = FxRestorePending();
        const size_t log0 = FxLogMark();
        const size_t calls0 = FxCallMark();
        Schlacht::StopWorker();
        const auto st = FxStatus();
        check("ST.06 StopWorker ends a waiting restore: at return restorePending F and restoreAbandoned F, its thread "
              "joined, the record {WallA} kept",
              on == 1 && off == 0 && waiting && !st.restorePending && !st.restoreAbandoned && !FxPendingJoinable() &&
                  FxRecordNow() == FxSet({A}) && st.hiddenCount == 1,
              "waiting=" + FxS(waiting ? 1 : 0) + " " + FxStatusStr(st));
        Sleep(static_cast<DWORD>(g4::kAfterGiveUpWatchMs));
        check("ST.06 StopWorker ends a waiting restore: nothing follows in 1.5 s -- no give-up line, no shutdown line, "
              "no un-hide",
              FxLogPrefix(g4::kGaveUpPrefix, 0, cat, log0) == 0 && FxLogPrefix(g4::kShutdownEndedPrefix, 0, cat, log0) == 0 &&
                  FxCount(FxKind::Hide, calls0) == 0 && FxRecordNow() == FxSet({A}),
              g4::HidesStr(calls0) + FxLogDump(log0));
    }

    // ══ WORKER ═══════════════════════════════════════════════════════════════════════════════════════════════════
    blk("WORKER - the real worker thread: the init gate, its first tick, its start and stop lines, a tick that throws");
    {
        FxReset();
        FxScene({ FxHitAt(kIdxWallA, g4::kNearT) });
        g_initInProgress.store(true);
        const size_t calls0 = FxCallMark();
        const int32_t on = Schlacht::SetEnabled(true, kClientA);
        Sleep(static_cast<DWORD>(g4::kInitHoldMs));
        const int held = FxCountAll(calls0);
        g_initInProgress.store(false);
        const bool traced = waitFor([calls0] { return FxCount(FxKind::Trace, calls0) >= 1; }, g4::kWorkerWithinMs);
        const auto calls = FxCalls(calls0);   // read before the reset's OFF, whose un-hides run on main
        check("IG.01 init gate (W1): no invoke of any kind for 500 ms while an init runs", on == 1 && held == 0,
              "invokes=" + FxS(held));
        check("IG.01 init gate: once it ends, a LineTraceSingle within 1 s, and every worker invoke on one thread, not "
              "main, marked background",
              traced && g4::AllBackground(calls), "invokes=" + FxS(static_cast<long long>(calls.size())));
    }
    {
        FxReset();
        const size_t calls0 = FxCallMark();
        const int32_t on = Schlacht::SetEnabled(true, kClientA);
        const auto returnedAt = std::chrono::steady_clock::now();
        const bool ticked = waitFor([calls0] { return FxCount(FxKind::CamLoc, calls0) >= 1; }, g4::kWorkerWithinMs);
        const auto cams = FxCallsOf(FxKind::CamLoc, calls0);
        const auto all = FxCalls(calls0);
        const long long gap = cams.empty() ? -1 : FxMs(cams.front().t - returnedAt);
        check("IG.02 the first tick (W3): the first camera invoke comes at least 90 ms after SetEnabled returned, and "
              "nothing comes before it",
              on == 1 && ticked && gap >= g4::kFirstTickFloorMs && !all.empty() && all.front().kind == FxKind::CamLoc,
              "gap=" + FxS(gap) + " ms");
    }
    {
        FxReset();
        const std::string started = g4::WorkerStarted();
        const size_t log0 = FxLogMark();
        const size_t calls0 = FxCallMark();
        const int32_t on = Schlacht::SetEnabled(true, kClientA);
        const bool up = waitFor([&] {
            return FxLogCount(started, 'I', cat, log0) >= 1 && FxCount(FxKind::Trace, calls0) >= g4::kTicksWanted;
        }, g4::kWorkerWithinMs);
        const int32_t again = Schlacht::SetEnabled(true, kClientB);   // already on: starts nothing
        const int32_t off = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const uint64_t offReturned = FxStamp();
        const size_t calls1 = FxCallMark();
        Sleep(static_cast<DWORD>(kQuietMs));
        const uint64_t stopped = FxLogSeq(g4::kWorkerStopped, 'I', cat, log0);
        check("IG.04 one start line (W5): an enable that starts a worker logs exactly one I 'worker started (100 ms "
              "tick)', and an already-on enable adds none",
              on == 1 && up && again == 1 && FxLogCount(started, 'I', cat, log0) == 1, FxLogDump(log0));
        check("IG.04 the stop line: SetEnabled(false) logs I 'worker stopped' before it returns, and no invoke follows "
              "in 300 ms",
              off == 0 && stopped != 0 && stopped < offReturned && FxLogCount(g4::kWorkerStopped, 'I', cat, log0) == 1 &&
                  FxCountAll(calls1) == 0,
              "invokes after=" + FxS(FxCountAll(calls1)) + FxLogDump(log0));
        const size_t log1 = FxLogMark();
        const int32_t on2 = Schlacht::SetEnabled(true, kClientA);
        const bool up2 = waitFor([&] { return FxLogCount(started, 'I', cat, log1) >= 1; }, g4::kWorkerWithinMs);
        const int32_t off2 = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        check("IG.04 one line per start: a second enable after an OFF logs one more 'worker started', and its OFF one "
              "more 'worker stopped'",
              on2 == 1 && up2 && off2 == 0 && FxLogCount(started, 'I', cat, log1) == 1 &&
                  FxLogCount(g4::kWorkerStopped, 'I', cat, log1) == 1,
              FxLogDump(log1));
    }
    {
        FxReset();
        // Thrown on the worker's own invoke: the worker catches it, and no lock of Schlacht's is held across an invoke.
        FxOnTrace([](int) { throw std::runtime_error("boom"); });
        const size_t log0 = FxLogMark();
        const size_t calls0 = FxCallMark();
        const int32_t on = Schlacht::SetEnabled(true, kClientA);
        const bool ticks = waitFor([calls0] { return FxCount(FxKind::Trace, calls0) >= g4::kTicksWanted; },
                                   g4::kThrowWindowMs);
        const int32_t off = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        check("IG.05 a tick that throws (W4, L7): the worker lives on -- at least 3 throwing traces in 600 ms -- and "
              "warns once: 'tick threw (boom)'",
              on == 1 && ticks && off == 0 && FxLogCount(g4::TickThrew("boom"), 'W', cat, log0) == 1 &&
                  FxLogPrefix(g4::kTickThrewPrefix, 0, cat, log0) == 1,
              "traces=" + FxS(FxCount(FxKind::Trace, calls0)) + FxLogDump(log0));
        FxOnTrace([](int) { throw 42; });
        const size_t log1 = FxLogMark();
        const size_t calls1 = FxCallMark();
        const int32_t on2 = Schlacht::SetEnabled(true, kClientA);
        const bool ticks2 = waitFor([calls1] { return FxCount(FxKind::Trace, calls1) >= g4::kTicksWanted; },
                                    g4::kThrowWindowMs);
        const int32_t off2 = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        FxOnTrace(nullptr);
        check("IG.05 a throw that is not a std::exception: the new worker run warns once more, 'tick threw (unknown)', "
              "over at least 3 throwing ticks",
              on2 == 1 && ticks2 && off2 == 0 && FxLogCount(g4::TickThrew("unknown"), 'W', cat, log1) == 1 &&
                  FxLogPrefix(g4::kTickThrewPrefix, 0, cat, log1) == 1,
              "traces=" + FxS(FxCount(FxKind::Trace, calls1)) + FxLogDump(log1));
    }
    {
        FxReset();
        // WallA hidden in the game, as an earlier copy left it. A call that throws did not run, so its code is one the
        // fake answers without writing; without the throw that code would keep WallA recorded.
        g4::GameHiddenSet(A, true);
        FxScriptHide(A, false, { g4::kDidNotRun });
        auto thrown = std::make_shared<std::atomic<bool>>(false);
        FxOnHide([thrown, A](uintptr_t actor, bool hidden) {
            if (actor == A && !hidden && !thrown->exchange(true)) throw std::runtime_error("gone");
        });
        const size_t log0 = FxLogMark();
        const size_t calls0 = FxCallMark();
        FxAdopt({A});
        const bool ended = waitFor([] { return !FxRestorePending(); }, kRestoreWithinMs);
        FxOnHide(nullptr);
        check("IG.06 a deferred pass that throws (L14): exactly one W 'tick threw (gone)'",
              thrown->load() && FxLogCount(g4::TickThrew("gone"), 'W', cat, log0) == 1 &&
                  FxLogPrefix(g4::kTickThrewPrefix, 0, cat, log0) == 1,
              FxLogDump(log0));
        check("OBS-THROW (pin) a throwing pass loses the actor: WallA is gone from the record while the game keeps it "
              "hidden, and the restore ends at the next pass -- no retry, no 'game thread resumed', none abandoned",
              ended && FxRecordNow().empty() && FxGameHidden(A) == 1 && FxHides(A, 0, calls0) == 1 &&
                  FxLogPrefix(g4::kResumedPrefix, 0, cat, log0) == 0 && !FxStatus().restoreAbandoned,
              "game hidden=" + FxS(FxGameHidden(A)) + g4::HidesStr(calls0) + " " + FxStatusStr(FxStatus()));
    }
    {
        FxReset();
        const int32_t on = FxEnableSync();
        FxRecord(FxSet({A}));
        g4::GameHiddenSet(A, true);
        FxScriptHide(A, false, { g4::kDidNotRun });
        // Thrown from an invoke SetEnabled makes, which holds s_workerMutex: the throw unwinds it, nothing re-enters.
        FxOnHide([A](uintptr_t actor, bool hidden) {
            if (actor == A && !hidden) throw std::runtime_error("gone");
        });
        bool threw = false;
        std::string what;
        int32_t off = g4::kNotReturned;
        try {
            off = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        } catch (const std::exception& e) {
            threw = true;
            what = e.what();
        }
        FxOnHide(nullptr);
        const auto st = FxStatus();
        check("OBS-THROW (pin) the disable path: an un-hide that throws leaves SetEnabled(false) as the exception -- "
              "the record ends empty, WallA stays hidden in the game, no restore waits",
              on == 1 && threw && what == "gone" && FxRecordNow().empty() && st.hiddenCount == 0 &&
                  FxGameHidden(A) == 1 && !st.active && !st.restorePending,
              "rc=" + FxS(off) + " threw=" + FxS(threw ? 1 : 0) + " what=" + what + " game hidden=" +
                  FxS(FxGameHidden(A)) + " " + FxStatusStr(st));
        check("OBS-THROW (pin) the disable path: the exception released s_workerMutex on its way out",
              FxFreeElsewhere(s_workerMutex));
    }

    blk("WORKER LK - SetEnabled serialises on s_workerMutex: one toggle at a time joins and starts the threads");
    {
        FxReset();
        g_initInProgress.store(true);   // the worker the enable starts stays quiet: this check is about the lock
        const int ensure0 = g_ensureCalls.load();
        const size_t log0 = FxLogMark();
        std::atomic<bool> done{false};
        std::atomic<int32_t> rc{g4::kNotReturned};
        s_workerMutex.lock();
        std::thread b([&done, &rc] {
            rc.store(Schlacht::SetEnabled(true, kClientA));
            done.store(true);
        });
        const bool early = waitFor([&done] { return done.load(); }, kHoldMs);
        const bool noHook = g_ensureCalls.load() == ensure0;
        const bool noLine = FxLogPrefix(g4::kEnabledPrefix, 0, cat, log0) == 0;
        const bool stillOff = !Schlacht::IsActive();
        const bool innerFree = FxFreeElsewhere(s_mutex);
        s_workerMutex.unlock();
        b.join();
        const bool onAfter = Schlacht::IsActive();
        Schlacht::StopWorker();
        g_initInProgress.store(false);
        check("LK.01 an enable waits on s_workerMutex: held off 200 ms -- no hook asked, no 'enabled' line, still off "
              "-- while s_mutex stays free",
              !early && noHook && noLine && stillOff && innerFree,
              FxFmt("returned=%d hook asked=%d line=%d on=%d s_mutex free=%d", early ? 1 : 0, noHook ? 0 : 1,
                    noLine ? 0 : 1, stillOff ? 0 : 1, innerFree ? 1 : 0));
        check("LK.01 an enable waits on s_workerMutex: released, it returns 1, asks for the hook once and logs "
              "'enabled'",
              done.load() && rc.load() == 1 && g_ensureCalls.load() == ensure0 + 1 &&
                  FxLogPrefix(g4::kEnabledPrefix, 'I', cat, log0) == 1 && onAfter,
              "rc=" + FxS(rc.load()) + FxLogDump(log0));
    }
    {
        FxReset();
        const int32_t on = FxEnableSync(kClientA);
        FxRecord(FxSet({A}));
        const size_t calls0 = FxCallMark();
        std::atomic<bool> done{false};
        std::atomic<int32_t> rc{g4::kNotReturned};
        s_workerMutex.lock();
        std::thread b([&done, &rc] {
            rc.store(Schlacht::SetEnabled(false, Schlacht::kOwnerNone));
            done.store(true);
        });
        const bool early = waitFor([&done] { return done.load(); }, kHoldMs);
        const int hidesHeld = FxCount(FxKind::Hide, calls0);
        const auto held = FxStatus(kClientA);
        const bool innerFree = FxFreeElsewhere(s_mutex);
        s_workerMutex.unlock();
        b.join();
        check("LK.01 a disable waits on s_workerMutex: held off 200 ms -- no SetActorHiddenInGame, still on, the "
              "claim still recorded -- while s_mutex stays free",
              on == 1 && !early && hidesHeld == 0 && held.active && held.callerClaimed && innerFree,
              "returned=" + FxS(early ? 1 : 0) + " s_mutex free=" + FxS(innerFree ? 1 : 0) + g4::HidesStr(calls0) +
                  " " + FxStatusStr(held));
        check("LK.01 a disable waits: released, it returns 0 and un-hides WallA -- the record empty, claims 0",
              done.load() && rc.load() == 0 && FxHides(A, 0, calls0) == 1 && FxRecordNow().empty() &&
                  FxStatus().claims == 0,
              "rc=" + FxS(rc.load()) + g4::HidesStr(calls0) + " " + FxStatusStr(FxStatus()));
    }

    // ══ INV ══════════════════════════════════════════════════════════════════════════════════════════════════════
    blk("INV - no game-thread invoke ran with s_mutex held, over the whole run so far (critic INV.01)");
    {
        FxReset();
        // The probe must see a held lock, or a zero count below would prove nothing.
        bool seesHeld = false;
        {
            std::lock_guard<std::mutex> lk(s_mutex);
            seesHeld = !FxFreeElsewhere(s_mutex);
        }
        const bool seesFree = FxFreeElsewhere(s_mutex);
        check("INV.00 the probe: another thread cannot take s_mutex while this one holds it, and can once it is let go",
              seesHeld && seesFree);
        check("INV.01 no invoke under s_mutex: every invoke of the run so far found s_mutex free -- probed > 0, held 0, "
              "the probe left on",
              g_invProbe.load() && g_invProbed.load() > 0 && g_invHeld.load() == 0,
              "probed=" + FxS(g_invProbed.load()) + " held=" + FxS(g_invHeld.load()) + " probe on=" +
                  FxS(g_invProbe.load() ? 1 : 0));
    }

    // ══ SHUTDOWN ═════════════════════════════════════════════════════════════════════════════════════════════════
    blk("SHUTDOWN - a shutdown ends a waiting restore and forbids a new one; last, as Tot's flag is process-wide");
    {
        FxReset();
        const int32_t on = FxEnableSync();
        FxRecord(FxSet({A, B}));
        g_responsive.store(false);
        const size_t calls0 = FxCallMark();
        const size_t log0 = FxLogMark();
        const int32_t off = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const bool waiting = FxRestorePending();
        Tot::RequestShutdown();
        const bool ended = waitFor([] { return !FxRestorePending(); }, kQuietMs);
        const auto st = FxStatus();
        check("SD.01 a shutdown ends a waiting restore (Q5): within 300 ms restorePending F and restoreAbandoned T, the "
              "record {WallA, WallB} kept, no un-hide",
              on == 1 && off == 0 && waiting && ended && st.restoreAbandoned && FxRecordNow() == FxSet({A, B}) &&
                  st.hiddenCount == 2 && FxCount(FxKind::Hide, calls0) == 0,
              FxStatusStr(st) + g4::HidesStr(calls0));
        check("SD.01 a shutdown ends a waiting restore (L13): exactly one W 'a shutdown ended the deferred restore with "
              "2 actor(s) still hidden'",
              FxLogCount(g4::ShutdownEnded(2), 'W', cat, log0) == 1 &&
                  FxLogPrefix(g4::kShutdownEndedPrefix, 0, cat, log0) == 1,
              FxLogDump(log0));
    }
    {
        Tot::ResetShutdown();
        FxReset();   // its OFF also joins SD.01's finished restore thread, so a joinable one below would be new
        const int32_t on = FxEnableSync();
        FxRecord(FxSet({C}));
        Tot::RequestShutdown();
        g_responsive.store(false);
        const size_t log0 = FxLogMark();
        const int32_t off = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const auto st = FxStatus();
        check("SD.02 an unresponsive OFF under a shutdown (D4): 0, restoreAbandoned T, restorePending F, the record "
              "{WallC}, no restore thread",
              on == 1 && off == 0 && st.restoreAbandoned && !st.restorePending && FxRecordNow() == FxSet({C}) &&
                  st.hiddenCount == 1 && !FxPendingJoinable(),
              FxStatusStr(st));
        check("SD.02 an unresponsive OFF under a shutdown: exactly one W '... and a shutdown is under way, so no "
              "restore waits ...', and no 'waiting for the game thread' line",
              FxLogCount(g4::OffUnresponsiveNoWait(1), 'W', cat, log0) == 1 &&
                  FxLogCount(g4::kRestoreWaits, 'I', cat, log0) == 0,
              FxLogDump(log0));
    }
    {
        Tot::ResetShutdown();
        FxReset();
        const int32_t on = FxEnableSync();
        FxRecord(FxSet({C}));
        FxScriptHide(C, false, { g4::kDidNotRun });
        Tot::RequestShutdown();
        const size_t log0 = FxLogMark();
        const size_t calls0 = FxCallMark();
        const int32_t off = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const auto st = FxStatus();
        check("SD.03 a responsive OFF under a shutdown (D3): WallC's un-hide did not run -- restoreAbandoned T, "
              "restorePending F, the record {WallC} still queued, no restore thread",
              on == 1 && off == 0 && FxHides(C, 0, calls0) == 1 && st.restoreAbandoned && !st.restorePending &&
                  FxRecordNow() == FxSet({C}) && FxQueuedNow() == FxSet({C}) && !FxPendingJoinable(),
              FxStatusStr(st) + g4::HidesStr(calls0));
        check("SD.03 a responsive OFF under a shutdown: exactly one W 'disabled (0 restored); 1 un-hide(s) did not run "
              "and a shutdown is under way ...', and no 'waiting for the game thread' line",
              FxLogCount(g4::OffKeptNoWait(0, 1), 'W', cat, log0) == 1 &&
                  FxLogCount(g4::kRestoreWaits, 'I', cat, log0) == 0,
              FxLogDump(log0));
    }
    {
        Tot::ResetShutdown();
        FxReset();
        Tot::RequestShutdown();
        const size_t log0 = FxLogMark();
        FxAdopt({D});
        const auto st = FxStatus();
        check("SD.04 adopt under a shutdown: no restore can wait -- restoreAbandoned T, restorePending F, WallD in the "
              "record, no 'waiting for the game thread' line, no restore thread",
              st.restoreAbandoned && !st.restorePending && FxRecordNow() == FxSet({D}) && !FxPendingJoinable() &&
                  FxLogCount(g4::kRestoreWaits, 'I', cat, log0) == 0,
              FxStatusStr(st) + FxLogDump(log0));
        const size_t log1 = FxLogMark();
        const int32_t rc = Schlacht::SetEnabled(true, kClientA);
        const auto s5 = FxStatus(kClientA);
        check("OBS-O05 (pin) an enable under a shutdown: -3 as 'KismetSystemLibrary::LineTraceSingle not found' (the "
              "object walk aborts) -- restoreAbandoned cleared, still off, nothing claimed, the record {WallD} kept",
              rc == Schlacht::STR_ERR_REFLECTION && FxLogCount(g4::kRefusedNoTrace, 'W', cat, log1) == 1 &&
                  !s5.restoreAbandoned && !s5.active && s5.state == Schlacht::STR_ERR_REFLECTION && s5.claims == 0 &&
                  FxRecordNow() == FxSet({D}),
              "rc=" + FxS(rc) + " " + FxStatusStr(s5) + FxLogDump(log1));
    }
    {
        // A restore stopped by its stopper while a shutdown is up: the stopper re-decides, so the restore adds no warning
        // and no abandon of its own.
        Tot::ResetShutdown();
        FxReset();
        const int32_t on = FxEnableSync();
        FxRecord(FxSet({A}));
        g_responsive.store(false);
        // Held on its first line, which it writes with no lock held, until the stop below is requested.
        auto gate = std::make_shared<std::atomic<int>>(0);   // 0 armed, 1 holding, 2 released
        FxOnLog([gate](const FxLogLine& line) {
            if (line.text != g4::kRestoreWaits) return;
            int armed = 0;
            if (!gate->compare_exchange_strong(armed, 1)) return;
            while (gate->load() == 1) Sleep(1);
        });
        const size_t log0 = FxLogMark();
        const int32_t off = Schlacht::SetEnabled(false, Schlacht::kOwnerNone);
        const bool held = waitFor([gate] { return gate->load() == 1; }, kQuietMs);
        Tot::RequestShutdown();
        std::thread stopper([] { Schlacht::StopWorker(); });
        const bool stopAsked = waitFor([] { return s_pendingStop.load(); }, kQuietMs);
        gate->store(2);
        stopper.join();
        FxOnLog(nullptr);
        const auto st = FxStatus();
        check("SD.05 a restore stopped during a shutdown: its stopper re-decides -- no 'a shutdown ended' warning, nothing "
              "abandoned, the record {WallA} kept",
              on == 1 && off == 0 && held && stopAsked && FxLogPrefix(g4::kShutdownEndedPrefix, 0, cat, log0) == 0 &&
                  !st.restoreAbandoned && !st.restorePending && FxRecordNow() == FxSet({A}),
              FxStatusStr(st) + FxLogDump(log0));
    }
    Tot::ResetShutdown();
}

int main() {
    setvbuf(stdout, nullptr, _IONBF, 0);
    printf("schlacht_seethrough_test -- See-through (Schlacht.cpp) over a fake world and a fake engine, "
           "in this process\n");
    FxAsanPreamble();
    FxBuildWorld();
    Blocks_G1();
    Blocks_G2();
    Blocks_G3();
    Blocks_G4();
    FxCleanup();
    return FxExitCode();
}
