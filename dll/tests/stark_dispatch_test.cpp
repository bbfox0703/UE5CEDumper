// stark_dispatch_test.cpp
// UE5CEDumper — GameThreadDispatch (Stark.cpp), the invoke queue and its game-thread drain, in this process.
//
// WHAT IT DRIVES
//   EnqueueInvoke on caller threads and the drain on a fake game thread that enters the detour (HookedProcessEvent) as
//   the game would: a queued invoke run against its own copy of the parameters or the caller's buffer, a timeout that
//   leaves the request queued, a request its waiter abandons or the drain claims first, the if_free owner rule decided
//   under the queue's lock, the counts that last until a request is answered, a departed owner's drop, one request at
//   a time with no nested drain, a shutdown that cuts the wait on a stalled game, and the own-call mark. Then the game
//   thread's side: which entries count as its, the liveness they stamp, the own-call mark per thread, a fault
//   contained, the profiler fed and a throw contained, the worker-thread skip, and the pick and its validation.
//   Last, the hook itself: InstallHook and RemoveHook with real MinHook hooks on code pages of the test's making,
//   the trampoline block held at start on a main image the test builds, the header read, the window walk, and a
//   shutdown that leaves MinHook as it is.
//
// WHAT IS FAKED
//   Sein records every log line, so a check can read what was logged. ProcessEvent is FakePE, a function in this
//   executable that records each call and adds two numbers into an out-parameter; s_originalPE is set to it directly,
//   so no hook is installed here. With s_gameThreadId 0 every thread's entry counts as the game thread's.
//   CreateToolhelp32Snapshot inside Stark.cpp is StarkTestSnapshot, which a test can make fail; GetModuleHandleW,
//   VirtualQuery and GetTickCount64 are seams too -- the main image Stark reads, a page's answer, and its clock.
//
// WHY IT #includes THE .cpp
//   The queue, its lock, its counts and the detour are statics of Stark.cpp. Linie, which the detour feeds, is
//   ue5_testcore's; Falsch.cpp and MinHook are linked because the install path references them.

#include <windows.h>
#include <tlhelp32.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>   // getenv_s: the AddressSanitizer build's negative control
#include <stdexcept>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstring>
#include <functional>
#include <future>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <utility>
#include <vector>
#include <MinHook.h>

// ── the log recorder ────────────────────────────────────────────────────────────────────────────────────────────
struct LogLine { char lvl; std::string text; };
static std::mutex g_logMu;
static std::vector<LogLine> g_log;
static constexpr DWORD kLogClobber = 0xBADC0DE;   // what the real logger's file writes leave in LastError
static void LogV(char lvl, const char* fmt, va_list ap) {
    char b[8192];
    vsnprintf(b, sizeof b, fmt, ap);
    {
        std::lock_guard<std::mutex> lk(g_logMu);
        g_log.push_back({ lvl, b });
    }
    SetLastError(kLogClobber);
}
namespace Sein {
void Info(const char*, const char* fmt, ...) { va_list ap; va_start(ap, fmt); LogV('I', fmt, ap); va_end(ap); }
void Error(const char*, const char* fmt, ...) { va_list ap; va_start(ap, fmt); LogV('E', fmt, ap); va_end(ap); }
void Warn(const char*, const char* fmt, ...) { va_list ap; va_start(ap, fmt); LogV('W', fmt, ap); va_end(ap); }
void Debug(const char*, const char* fmt, ...) { va_list ap; va_start(ap, fmt); LogV('D', fmt, ap); va_end(ap); }
void Summary(const char*, ...) {}
}  // namespace Sein

HANDLE WINAPI StarkTestSnapshot(DWORD flags, DWORD pid);
#define CreateToolhelp32Snapshot StarkTestSnapshot
HMODULE WINAPI StarkGetModuleHandleW(LPCWSTR name);
SIZE_T WINAPI StarkVirtualQuery(LPCVOID addr, PMEMORY_BASIC_INFORMATION mbi, SIZE_T len);
ULONGLONG WINAPI StarkGetTickCount64();
#define GetModuleHandleW StarkGetModuleHandleW
#define VirtualQuery StarkVirtualQuery
#define GetTickCount64 StarkGetTickCount64
#include "../src/Stark.cpp"  // NOLINT -- see "WHY IT #includes THE .cpp" above
#undef CreateToolhelp32Snapshot
#undef GetModuleHandleW
#undef VirtualQuery
#undef GetTickCount64
// The main image Stark reads (0 = this executable), how VirtualQuery answers inside Stark.cpp, and its clock: always
// the test's, so a walk's time is what the test says. Each counts its calls.
static uintptr_t g_fakeMainImage = 0;
enum class VqMode { Real, Stripes, Lie };
static VqMode g_vqMode = VqMode::Real;
static uintptr_t g_vqLo = 0, g_vqHi = 0;   // Stripes: [lo, hi); Lie: the page at lo
static DWORD g_vqLieState = 0, g_vqLieProtect = 0;
static std::atomic<uint64_t> g_tick{ 1000000 };
static uint64_t g_tickStep = 0;
static std::atomic<int> g_tickReads{ 0 }, g_vqCalls{ 0 };
HMODULE WINAPI StarkGetModuleHandleW(LPCWSTR name) {
    if (!name && g_fakeMainImage) return reinterpret_cast<HMODULE>(g_fakeMainImage);
    return ::GetModuleHandleW(name);
}
SIZE_T WINAPI StarkVirtualQuery(LPCVOID addr, PMEMORY_BASIC_INFORMATION mbi, SIZE_T len) {
    g_vqCalls.fetch_add(1);
    const uintptr_t a = reinterpret_cast<uintptr_t>(addr);
    if (g_vqMode == VqMode::Stripes && a >= g_vqLo && a < g_vqHi) {
        const uintptr_t page = a & ~static_cast<uintptr_t>(0xFFF);
        const bool even = ((page - g_vqLo) / 0x1000) % 2 == 0;
        *mbi = MEMORY_BASIC_INFORMATION{};
        mbi->BaseAddress = reinterpret_cast<PVOID>(page);
        mbi->AllocationBase = reinterpret_cast<PVOID>(g_vqLo);
        mbi->RegionSize = 0x1000;
        mbi->State = even ? MEM_COMMIT : MEM_RESERVE;
        mbi->Protect = even ? PAGE_READWRITE : 0;
        mbi->Type = MEM_PRIVATE;
        return sizeof(MEMORY_BASIC_INFORMATION);
    }
    if (g_vqMode == VqMode::Lie && (a & ~static_cast<uintptr_t>(0xFFF)) == g_vqLo) {
        *mbi = MEMORY_BASIC_INFORMATION{};
        mbi->BaseAddress = reinterpret_cast<PVOID>(g_vqLo);
        mbi->AllocationBase = reinterpret_cast<PVOID>(g_vqLo);
        mbi->RegionSize = 0x1000;
        mbi->State = g_vqLieState;
        mbi->Protect = g_vqLieProtect;
        mbi->Type = MEM_PRIVATE;
        return sizeof(MEMORY_BASIC_INFORMATION);
    }
    return ::VirtualQuery(addr, mbi, len);
}
ULONGLONG WINAPI StarkGetTickCount64() {
    g_tickReads.fetch_add(1);
    const uint64_t v = g_tick.load();
    g_tick.store(v + g_tickStep);
    return v;
}
static std::atomic<bool> g_failSnapshot{false};
HANDLE WINAPI StarkTestSnapshot(DWORD flags, DWORD pid) {
    if (g_failSnapshot.load()) {
        SetLastError(ERROR_ACCESS_DENIED);
        return INVALID_HANDLE_VALUE;
    }
    return CreateToolhelp32Snapshot(flags, pid);
}

// ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────────
static int g_pass = 0, g_fail = 0;
static void check(const char* label, bool cond, const char* got = nullptr) {
    if (cond) { ++g_pass; }
    else { ++g_fail; printf("  FAIL  %s%s%s\n", label, got ? "   got: " : "", got ? got : ""); }
}
static void blk(const char* name) { printf("- %s\n", name); }
static std::string F(const char* fmt, ...) {
    char b[1024];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(b, sizeof b, fmt, ap);
    va_end(ap);
    return b;
}
static bool HasLog(char lvl, const std::string& exact) {
    std::lock_guard<std::mutex> lk(g_logMu);
    for (const auto& l : g_log) if (l.lvl == lvl && l.text == exact) return true;
    return false;
}
static int CountLog(char lvl, const std::string& prefix) {
    std::lock_guard<std::mutex> lk(g_logMu);
    int n = 0;
    for (const auto& l : g_log) if (l.lvl == lvl && l.text.rfind(prefix, 0) == 0) ++n;
    return n;
}
static std::string FirstLog(char lvl, const std::string& prefix) {
    std::lock_guard<std::mutex> lk(g_logMu);
    for (const auto& l : g_log) if (l.lvl == lvl && l.text.rfind(prefix, 0) == 0) return l.text;
    return std::string();
}
static bool WaitLog(char lvl, const std::string& prefix, int ms) {
    const uint64_t until = Stark::NowMs() + static_cast<uint64_t>(ms);
    while (CountLog(lvl, prefix) == 0 && Stark::NowMs() < until) Sleep(1);
    return CountLog(lvl, prefix) > 0;
}

// ── the fake ProcessEvent ───────────────────────────────────────────────────────────────────────────────────────
constexpr uintptr_t kTickObj = 0x7100, kTickFunc = 0x7101, kInst = 0x5000;
constexpr uintptr_t kFuncAdd = 0xF001, kFuncGate = 0xF002, kFuncRaise = 0xF003, kFuncNest = 0xF004, kFuncDirect = 0xF005;
constexpr uint64_t kOwnerA = 0x1111, kOwnerB = 0x2222;
constexpr uintptr_t kObj = 0x10000100, kObj2 = 0x10000200, kParams = 0x5150;
constexpr uintptr_t kPlain = 0xF100, kReenter = 0xF200, kNested = 0xF300, kQueued = 0xF400, kFault = 0xF500;
constexpr uintptr_t kSuper = 0xF600, kNestOwn = 0xF700, kBlock = 0xF800, kFa = 0xFA00, kFb = 0xFB00, kFc = 0xFC00;

struct AddParams { int32_t a, b, out; };
struct PeCall {
    char via;         // 'P' FakePE, 'V' FakeOverride, 'O' OtherPE
    uintptr_t obj, ufunc, params;
    DWORD tid;
    AddParams seen;   // the parameters as the call found them
    int depth;        // FakePE frames already on this thread
    bool own;         // inside one of Stark's own calls (InOwnPeCall)
    bool exited;
};
static std::mutex g_peMu;
static std::vector<PeCall> g_pe;
static std::vector<std::string> g_seq;   // "+F004@0" on entry (with the depth), "-F004" on return
static thread_local int t_peDepth = 0;
static HANDLE g_gateEntered = nullptr, g_gateRelease = nullptr;
static std::atomic<int> g_overrideCalls{0};
static std::atomic<bool> g_overrideSawOwn{false};
static HANDLE g_inside = nullptr, g_release = nullptr;
static std::atomic<int32_t> g_innerRc{-99};
static std::atomic<bool> g_afterInnerOwn{false};

static bool IsAddId(uintptr_t f) { return f >= kFuncAdd && f <= kFuncDirect; }
static void PushSeq(const char* fmt, uintptr_t f, int depth) {
    char b[48];
    snprintf(b, sizeof b, fmt, static_cast<unsigned long long>(f), depth);
    g_seq.emplace_back(b);
}
// Add / Direct add a + b into out. Gate parks until the test releases it. Nest dispatches a ProcessEvent of its own
// through the detour, as a UFunction calling another one does. Raise faults. The locks close before any branch that
// can raise or park.
static void __fastcall FakePE(void* obj, void* ufunc, void* params) {
    const uintptr_t f = reinterpret_cast<uintptr_t>(ufunc);
    size_t me = 0;
    {
        std::lock_guard<std::mutex> lk(g_peMu);
        PeCall c{};
        c.via = 'P';
        c.obj = reinterpret_cast<uintptr_t>(obj);
        c.ufunc = f;
        c.params = reinterpret_cast<uintptr_t>(params);
        c.tid = GetCurrentThreadId();
        if (params && IsAddId(f)) c.seen = *static_cast<const AddParams*>(params);
        c.depth = t_peDepth;
        c.own = Stark::InOwnPeCall();
        g_pe.push_back(c);
        me = g_pe.size() - 1;
        PushSeq("+%llX@%d", f, t_peDepth);
    }
    if (f == kFuncRaise) RaiseException(0xE0000F0D, 0, 0, nullptr);
    if (f == kFault) {
        const ULONG_PTR info[2] = { 0, 0x10 };
        RaiseException(EXCEPTION_ACCESS_VIOLATION, 0, 2, info);
    }
    ++t_peDepth;
    if (f == kFuncGate) {
        SetEvent(g_gateEntered);
        WaitForSingleObject(g_gateRelease, 10000);
    }
    if (f == kFuncNest) Stark::HookedProcessEvent(reinterpret_cast<void*>(kTickObj), reinterpret_cast<void*>(kTickFunc), nullptr);
    if (f == kReenter) Stark::HookedProcessEvent(obj, reinterpret_cast<void*>(kNested), params);
    if (params && IsAddId(f)) {
        auto* p = static_cast<AddParams*>(params);
        p->out = p->a + p->b;
    }
    --t_peDepth;
    std::lock_guard<std::mutex> lk(g_peMu);
    g_pe[me].exited = true;
    PushSeq("-%llX", f, 0);
}
static void Record(char via, void* obj, void* ufunc, void* params) {
    std::lock_guard<std::mutex> lk(g_peMu);
    PeCall c{};
    c.via = via;
    c.obj = reinterpret_cast<uintptr_t>(obj);
    c.ufunc = reinterpret_cast<uintptr_t>(ufunc);
    c.params = reinterpret_cast<uintptr_t>(params);
    c.tid = GetCurrentThreadId();
    c.own = Stark::InOwnPeCall();
    c.exited = true;
    g_pe.push_back(c);
}
// A class's own ProcessEvent override. Direct and Super call Super::ProcessEvent, the address the hook patched;
// NestOwn makes an own call of its own inside; Block parks until the test releases it.
static void __fastcall FakeOverride(void* obj, void* ufunc, void* params) {
    const uintptr_t f = reinterpret_cast<uintptr_t>(ufunc);
    g_overrideCalls.fetch_add(1);
    g_overrideSawOwn.store(Stark::InOwnPeCall());
    Record('V', obj, ufunc, params);
    if (f == kFuncDirect) Stark::HookedProcessEvent(obj, ufunc, params);
    else if (f == kSuper) Stark::HookedProcessEvent(obj, reinterpret_cast<void*>(kNested), params);
    else if (f == kNestOwn) {
        g_innerRc.store(Stark::CallOriginalSEH(reinterpret_cast<uintptr_t>(obj), kPlain,
                                               reinterpret_cast<uintptr_t>(params)));
        g_afterInnerOwn.store(Stark::InOwnPeCall());
    } else if (f == kBlock) {
        SetEvent(g_inside);
        WaitForSingleObject(g_release, 10000);
    }
}
// What the trampoline would reach: a call routed there instead of to the address given is seen as 'O'.
static void __fastcall OtherPE(void* obj, void* ufunc, void* params) { Record('O', obj, ufunc, params); }
static bool ThrowingReader(uintptr_t, Linie::FuncIdentity&) { throw std::runtime_error("reader boom"); }
static void __fastcall FakeOverrideRaise(void*, void*, void*) {
    g_overrideCalls.fetch_add(1);
    RaiseException(0xE0000F0E, 0, 0, nullptr);
}
static std::vector<PeCall> Calls(uintptr_t f) {
    std::lock_guard<std::mutex> lk(g_peMu);
    std::vector<PeCall> out;
    for (const auto& c : g_pe) if (c.via == 'P' && c.ufunc == f) out.push_back(c);
    return out;
}
static bool AnyCallWithA(int32_t a) {
    std::lock_guard<std::mutex> lk(g_peMu);
    for (const auto& c : g_pe) if (c.via == 'P' && IsAddId(c.ufunc) && c.seen.a == a) return true;
    return false;
}

// ── the game thread and the callers ─────────────────────────────────────────────────────────────────────────────
// One thread for the run. Each pump is one ProcessEvent the game makes, through the detour.
struct FakeGame {
    std::thread th;
    std::atomic<DWORD> tid{0};
    std::mutex mu;
    std::condition_variable cv;
    int asked = 0, done = 0;
    bool stop = false;
    void Start() {
        th = std::thread([this] {
            tid.store(GetCurrentThreadId());
            std::unique_lock<std::mutex> lk(mu);
            for (;;) {
                cv.wait(lk, [&] { return stop || asked > done; });
                if (asked > done) {
                    lk.unlock();
                    Stark::HookedProcessEvent(reinterpret_cast<void*>(kTickObj), reinterpret_cast<void*>(kTickFunc), nullptr);
                    lk.lock();
                    ++done;
                    cv.notify_all();
                    continue;
                }
                if (stop) break;
            }
        });
        while (!tid.load()) Sleep(1);
    }
    int PumpAsync() { std::lock_guard<std::mutex> lk(mu); ++asked; cv.notify_all(); return asked; }
    bool WaitPumped(int ticket, int ms) {
        std::unique_lock<std::mutex> lk(mu);
        return cv.wait_for(lk, std::chrono::milliseconds(ms), [&] { return done >= ticket; });
    }
    bool Pump(int ms = 5000) { return WaitPumped(PumpAsync(), ms); }
    void Stop() {
        SetEvent(g_gateRelease);
        { std::lock_guard<std::mutex> lk(mu); stop = true; cv.notify_all(); }
        if (th.joinable()) th.join();
    }
};
static FakeGame g_game;

// An invoke on a thread of its own, as a pipe or mailbox thread makes one. A fresh thread each time: the thread-local
// "last enqueued" and "last blocker" values must be this call's.
struct Caller {
    std::thread th;
    std::atomic<bool> done{false};
    int32_t rc = 0;
    uint64_t id = 0, blocker = 0, elapsed = 0;
    std::shared_future<int32_t> queued;
    uintptr_t inst = kInst;
    void Start(uint64_t owner, bool ifFree, uintptr_t func, AddParams* buf, size_t size, bool drop = false,
               bool wantQueued = false) {
        th = std::thread([this, owner, ifFree, func, buf, size, drop, wantQueued] {
            auto body = [&] {
                const uint64_t t0 = Stark::NowMs();
                rc = Stark::EnqueueInvoke(inst, func, reinterpret_cast<uintptr_t>(buf), size, drop,
                                          wantQueued ? &queued : nullptr);
                elapsed = Stark::NowMs() - t0;
                id = Stark::LastEnqueuedId();
                blocker = Stark::LastInvokeBlocker();
            };
            if (owner) {
                const Stark::InvokeOwnerScope scope(owner, ifFree);
                body();
            } else {
                body();
            }
            done.store(true, std::memory_order_release);
        });
    }
    bool Wait(int ms) {
        const uint64_t until = Stark::NowMs() + static_cast<uint64_t>(ms);
        while (!done.load(std::memory_order_acquire) && Stark::NowMs() < until) Sleep(1);
        return done.load(std::memory_order_acquire);
    }
};
// Callers live until the next reset joins them; one that never returns is left running (and leaked), and counted.
static std::vector<Caller*> g_callers;
static Caller& NewCaller() { g_callers.push_back(new Caller()); return *g_callers.back(); }
static void JoinCallers() {
    for (Caller* c : g_callers) {
        if (!c->th.joinable()) { delete c; continue; }
        if (c->Wait(15000)) { c->th.join(); delete c; }
        else { c->th.detach(); check("reset: a caller never returned", false); }
    }
    g_callers.clear();
}
static void RunOn(const std::function<void()>& fn) { std::thread t(fn); t.join(); }

// ── queue introspection ─────────────────────────────────────────────────────────────────────────────────────────
using Outs = std::vector<std::pair<uint64_t, int32_t>>;
static Outs Out() { return Stark::OutstandingInvokes(); }
static size_t QueueSize() { std::lock_guard<std::mutex> lk(Stark::s_queueMutex); return Stark::s_invokeQueue.size(); }
static std::shared_ptr<Stark::InvokeRequest> Front() {
    std::lock_guard<std::mutex> lk(Stark::s_queueMutex);
    return Stark::s_invokeQueue.empty() ? nullptr : Stark::s_invokeQueue.front();
}
static bool WaitDepth(size_t n, int ms = 2000) {
    const uint64_t until = Stark::NowMs() + static_cast<uint64_t>(ms);
    while (Stark::s_queueDepth.load() < n && Stark::NowMs() < until) Sleep(1);
    return Stark::s_queueDepth.load() >= n;
}
static bool Ready(const std::shared_future<int32_t>& f) {
    return f.valid() && f.wait_for(std::chrono::seconds(0)) == std::future_status::ready;
}
static int32_t Get(const std::shared_future<int32_t>& f) {
    return f.valid() && f.wait_for(std::chrono::seconds(2)) == std::future_status::ready ? f.get() : INT32_MIN;
}

// Before every check: whatever the last one left answered (-7) and joined, the counts empty, the fake installed.
static void ResetStark() {
    SetEvent(g_gateRelease);
    Stark::Shutdown();
    JoinCallers();
    Tot::ResetShutdown();
    {
        std::lock_guard<std::mutex> lk(Stark::s_queueMutex);
        if (!Stark::s_invokeQueue.empty() || !Stark::s_outstanding.empty()) {
            check("reset: the last check left requests or counts behind", false);
            Stark::s_invokeQueue.clear();
            Stark::s_outstanding.clear();
        }
        Stark::s_queueDepth.store(0);
    }
    Stark::s_originalPE = &FakePE;
    Stark::s_gameThreadId.store(0);
    Stark::s_lastHookFireMs.store(0);
    Stark::s_hookInstalledMs.store(0);
    Stark::s_hookActive.store(true);
    Stark::SetInvokeTimeoutMs(5000);
    ResetEvent(g_gateRelease);
    ResetEvent(g_gateEntered);
    { std::lock_guard<std::mutex> lk(g_peMu); g_pe.clear(); g_seq.clear(); }
    { std::lock_guard<std::mutex> lk(g_logMu); g_log.clear(); }
    g_overrideCalls.store(0);
    g_overrideSawOwn.store(false);
}

static void QueueBlocks() {
    const std::string enqueued = "GameThreadDispatch: enqueued invoke";
    const std::string timeoutAdd100 = F("GameThreadDispatch: invoke timeout (100ms) inst=0x%llX func=0x%llX",
                                        (unsigned long long)kInst, (unsigned long long)kFuncAdd);
    const std::string droppedAbandoned = F("GameThreadDispatch: dropped an abandoned request func=0x%llX",
                                           (unsigned long long)kFuncAdd);
    const std::string droppedOwner = F("GameThreadDispatch: dropped a queued invoke func=0x%llX -- its client left "
                                       "before it ran", (unsigned long long)kFuncAdd);

    {   blk("QUEUE - a queued invoke runs on the game thread, and its answer and out-parameters come back");
        ResetStark();
        AddParams buf{ 2, 3, 0 };
        Caller& A = NewCaller();
        A.Start(kOwnerA, false, kFuncAdd, &buf, sizeof buf, false, true);
        WaitDepth(1);
        g_game.Pump();
        A.Wait(2000);
        const auto c = Calls(kFuncAdd);
        check("QUEUE owned copy ⭐: run on the game thread against its own copy, the out-parameter copied back",
              A.done && A.rc == 0 && buf.out == 5 && c.size() == 1 && c[0].obj == kInst
              && c[0].params != reinterpret_cast<uintptr_t>(&buf) && c[0].seen.a == 2 && c[0].seen.b == 3
              && c[0].seen.out == 0 && c[0].tid == g_game.tid.load() && !A.queued.valid());
        check("QUEUE owned copy: answered is counted off -- nothing outstanding or queued", Out().empty()
              && QueueSize() == 0 && Stark::s_queueDepth.load() == 0);
        check("QUEUE owned copy: queued and completed, in the log", HasLog('I', F("GameThreadDispatch: enqueued invoke "
              "inst=0x%llX func=0x%llX, waiting...", (unsigned long long)kInst, (unsigned long long)kFuncAdd))
              && HasLog('I', "GameThreadDispatch: invoke completed result=0") && CountLog('E', "") == 0);

        ResetStark();
        AddParams b0{ 2, 3, 0 };
        Caller& Z = NewCaller();
        Z.Start(kOwnerA, false, kFuncAdd, &b0, 0);
        WaitDepth(1);
        g_game.Pump();
        Z.Wait(2000);
        const auto z = Calls(kFuncAdd);
        check("QUEUE size 0: a persistent buffer is used as it is, no copy", Z.rc == 0 && z.size() == 1
              && z[0].params == reinterpret_cast<uintptr_t>(&b0) && b0.out == 5 && Out().empty());
    }

    {   blk("QUEUE TIMEOUT - a timed-out invoke stays queued, and runs later against its own parameters");
        ResetStark();
        Stark::SetInvokeTimeoutMs(100);
        AddParams buf{ 2, 3, 0 };
        Caller& A = NewCaller();
        A.Start(kOwnerA, false, kFuncAdd, &buf, sizeof buf, false, true);
        A.Wait(2000);
        Stark::QueuedInvoke rec;
        rec.Record(A.queued);
        const bool stillQueued = rec.StillQueued();
        check("QUEUE timeout: -5 after the timeout, logged, still queued and still counted", A.rc == -5
              && A.elapsed >= 100 && A.elapsed < 1000 && HasLog('E', timeoutAdd100) && QueueSize() == 1
              && Out() == Outs{ { kOwnerA, 1 } }, F("rc=%d elapsed=%llu", A.rc, (unsigned long long)A.elapsed).c_str());
        check("QUEUE timeout: the caller is handed the request's future, not yet answered", A.queued.valid()
              && !Ready(A.queued) && stillQueued);
        buf = AddParams{ 1000, 2000, -1 };
        g_game.Pump();
        const auto c = Calls(kFuncAdd);
        check("QUEUE timeout ⭐: the late run reads the ORIGINAL parameters and never writes the caller's buffer",
              c.size() == 1 && c[0].seen.a == 2 && c[0].seen.b == 3 && c[0].params != reinterpret_cast<uintptr_t>(&buf)
              && buf.a == 1000 && buf.b == 2000 && buf.out == -1);
        check("QUEUE timeout: the future completes, the record clears itself, nothing outstanding", Get(A.queued) == 0
              && !rec.StillQueued() && !rec.HasRecord() && Out().empty() && QueueSize() == 0);

        ResetStark();
        AddParams b1{ 1, 1, 0 }, b2{ 1, 1, 0 }, b3{ 1, 1, 0 };
        Caller& S = NewCaller();
        S.Start(kOwnerA, false, kFuncAdd, &b1, sizeof b1, false, true);
        WaitDepth(1);
        g_game.Pump();
        S.Wait(2000);
        const bool successUntouched = S.rc == 0 && !S.queued.valid();
        Stark::SetInvokeTimeoutMs(100);
        Caller& D = NewCaller();
        D.Start(kOwnerA, false, kFuncAdd, &b2, sizeof b2, true, true);
        D.Wait(2000);
        const bool dropUntouched = D.rc == -5 && !D.queued.valid();
        Caller& K = NewCaller();
        K.Start(kOwnerA, false, kFuncAdd, &b3, sizeof b3, false, true);
        K.Wait(2000);
        const bool keptFilled = K.rc == -5 && K.queued.valid() && !Ready(K.queued);
        g_game.Pump();
        check("QUEUE queuedOut: filled only on a timeout that stays queued -- not on success, not when abandoned",
              successUntouched && dropUntouched && keptFilled && Get(K.queued) == 0);
    }

    {   blk("QUEUE ABANDON - a drop-if-abandoned request its waiter gives up never runs; one the drain took is waited out");
        ResetStark();
        Stark::SetInvokeTimeoutMs(100);
        AddParams buf{ 2, 3, 0 };
        Caller& A = NewCaller();
        A.Start(kOwnerA, false, kFuncAdd, &buf, sizeof buf, true, true);
        A.Wait(2000);
        const auto r = Front();
        check("QUEUE abandoned ⭐: -5, no longer counted, marked abandoned and settled, but still in the queue",
              A.rc == -5 && A.elapsed >= 100 && HasLog('E', timeoutAdd100) && Out().empty() && QueueSize() == 1 && r
              && r->state.load() == Stark::kRequestAbandoned && r->settled && !A.queued.valid());
        g_game.Pump();
        check("QUEUE abandoned ⭐: the drain skips it -- never run -- and says so", Calls(kFuncAdd).empty()
              && HasLog('I', droppedAbandoned) && QueueSize() == 0 && Stark::s_queueDepth.load() == 0 && buf.out == 0);

        ResetStark();
        Stark::SetInvokeTimeoutMs(1000);
        AddParams g{ 2, 3, 0 };
        const uint64_t t0 = Stark::NowMs();
        Caller& G = NewCaller();
        G.Start(kOwnerA, false, kFuncGate, &g, sizeof g, true, false);
        WaitDepth(1);
        const auto req = Front();
        const int ticket = g_game.PumpAsync();
        const bool entered = WaitForSingleObject(g_gateEntered, 2000) == WAIT_OBJECT_0;
        const int32_t stateHeld = req ? req->state.load() : -1;
        while (Stark::NowMs() < t0 + 1500) Sleep(5);
        const bool doneEarly = G.done.load();
        SetEvent(g_gateRelease);
        G.Wait(2000);
        g_game.WaitPumped(ticket, 2000);
        check("QUEUE claimed ⭐: a request the drain is running is waited out past the timeout, and answered",
              entered && stateHeld == Stark::kRequestRunning && !doneEarly && G.rc == 0 && g.out == 5
              && Calls(kFuncGate).size() == 1 && CountLog('E', "GameThreadDispatch: invoke timeout") == 0
              && HasLog('I', "GameThreadDispatch: invoke completed result=0") && Out().empty(),
              F("entered=%d state=%d early=%d rc=%d", entered, stateHeld, doneEarly, G.rc).c_str());

        // The window between a waiter's claim and its settle: the drain answers the request and settles it itself.
        ResetStark();
        AddParams s{ 2, 3, 0 };
        auto skip = std::make_shared<Stark::InvokeRequest>();
        skip->instance = kInst;
        skip->ufunc = kFuncAdd;
        skip->params = reinterpret_cast<uintptr_t>(&s);
        skip->dropIfAbandoned = true;
        skip->owner = kOwnerA;
        skip->state.store(Stark::kRequestAbandoned);
        auto f = skip->promise.get_future();
        {
            std::lock_guard<std::mutex> lk(Stark::s_queueMutex);
            Stark::s_invokeQueue.push_back(skip);
            ++Stark::s_outstanding[kOwnerA];
            Stark::s_queueDepth.store(1);
        }
        g_game.Pump();
        const bool answered = f.wait_for(std::chrono::seconds(1)) == std::future_status::ready;
        check("QUEUE skipped: the drain answers an abandoned request -5 and settles it", answered && f.get() == -5
              && skip->settled && Out().empty() && Calls(kFuncAdd).empty() && s.out == 0 && HasLog('I', droppedAbandoned)
              && QueueSize() == 0);
    }

    {   blk("QUEUE IF_FREE - an if_free invoke is refused while another owner has one outstanding, decided under the lock");
        ResetStark();
        Stark::SetInvokeTimeoutMs(100);
        AddParams a{ 1, 1, 0 }, b{ 2, 2, 0 }, a2{ 3, 3, 0 };
        Caller& A = NewCaller();
        A.Start(kOwnerA, false, kFuncAdd, &a, sizeof a);
        A.Wait(2000);
        Stark::SetInvokeTimeoutMs(5000);
        Caller& B = NewCaller();
        B.Start(kOwnerB, true, kFuncAdd, &b, sizeof b);
        B.Wait(2000);
        check("QUEUE if_free ⭐: refused at once with the blocker named, nothing queued", A.rc == -5
              && B.rc == Stark::kInvokeHeldByOther && B.elapsed < 2500 && B.blocker == kOwnerA && B.id == 0
              && QueueSize() == 1 && Out() == Outs{ { kOwnerA, 1 } }
              && HasLog('I', "GameThreadDispatch: if_free invoke refused -- another client has 1 invoke(s) outstanding")
              && CountLog('I', enqueued) == 1, F("rc=%d blocker=%llx", B.rc, (unsigned long long)B.blocker).c_str());
        Stark::SetInvokeTimeoutMs(100);
        Caller& A2 = NewCaller();
        A2.Start(kOwnerA, true, kFuncAdd, &a2, sizeof a2);
        A2.Wait(2000);
        check("QUEUE if_free: an owner's own outstanding call does not block it", A2.rc == -5 && A2.blocker == 0
              && A2.id != 0 && Out() == Outs{ { kOwnerA, 2 } });

        ResetStark();
        Stark::SetInvokeTimeoutMs(100);
        AddParams c{ 1, 1, 0 }, d{ 2, 2, 0 };
        Caller& C = NewCaller();
        C.Start(kOwnerA, false, kFuncAdd, &c, sizeof c);
        C.Wait(2000);
        bool doneWhileHeld = true;
        Caller& E = NewCaller();
        {
            std::unique_lock<std::mutex> hold(Stark::s_queueMutex);
            E.Start(kOwnerB, true, kFuncAdd, &d, sizeof d);
            Sleep(300);
            doneWhileHeld = E.done.load();
        }
        E.Wait(2000);
        check("QUEUE if_free ⭐: the refusal waits for the queue's lock -- it is decided under it", !doneWhileHeld
              && E.rc == Stark::kInvokeHeldByOther && E.blocker == kOwnerA && QueueSize() == 1);
    }

    {   blk("QUEUE SETTLE - a request is counted off before its caller is woken, and only once");
        ResetStark();
        AddParams g{ 2, 3, 0 };
        Caller& A = NewCaller();
        A.Start(kOwnerA, false, kFuncGate, &g, sizeof g);
        WaitDepth(1);
        const int ticket = g_game.PumpAsync();
        WaitForSingleObject(g_gateEntered, 2000);
        bool doneWhileHeld = true, exited = false;
        {
            std::unique_lock<std::mutex> hold(Stark::s_queueMutex);
            SetEvent(g_gateRelease);
            const uint64_t until = Stark::NowMs() + 1000;
            while (!exited && Stark::NowMs() < until) {
                Sleep(1);
                const auto c = Calls(kFuncGate);
                exited = c.size() == 1 && c[0].exited;
            }
            Sleep(300);
            doneWhileHeld = A.done.load();
        }
        A.Wait(2000);
        const bool countedOff = Out().empty();
        g_game.WaitPumped(ticket, 2000);
        check("QUEUE settle ⭐: the caller is not woken while the count waits for the lock -- settled first",
              exited && !doneWhileHeld && A.rc == 0 && g.out == 5 && countedOff);

        ResetStark();
        Stark::SetInvokeTimeoutMs(100);
        AddParams b1{ 7, 0, 0 }, b2{ 2, 3, 0 };
        Caller& R1 = NewCaller();
        R1.Start(kOwnerA, false, kFuncAdd, &b1, sizeof b1, true);
        R1.Wait(2000);
        Stark::SetInvokeTimeoutMs(5000);
        Caller& R2 = NewCaller();
        R2.Start(kOwnerA, false, kFuncGate, &b2, sizeof b2);
        WaitDepth(2);
        const Outs before = Out();
        const int t2 = g_game.PumpAsync();
        WaitForSingleObject(g_gateEntered, 2000);
        const Outs during = Out();
        SetEvent(g_gateRelease);
        R2.Wait(2000);
        g_game.WaitPumped(t2, 2000);
        check("QUEUE settle once ⭐: a request settled by its waiter is not counted off again by the drain",
              R1.rc == -5 && before == Outs{ { kOwnerA, 1 } } && during == Outs{ { kOwnerA, 1 } }
              && HasLog('I', droppedAbandoned) && R2.rc == 0 && b2.out == 5 && Out().empty() && Calls(kFuncAdd).empty());
    }

    {   blk("QUEUE ONE AT A TIME - the drain takes one request at a time, up to what was queued when it began");
        ResetStark();
        AddParams g{ 2, 3, 0 }, x{ 21, 1, 0 };
        Caller& A = NewCaller();
        A.Start(kOwnerA, false, kFuncGate, &g, sizeof g);
        WaitDepth(1);
        Caller& B = NewCaller();
        B.Start(kOwnerB, false, kFuncAdd, &x, sizeof x);
        WaitDepth(2);
        const int ticket = g_game.PumpAsync();
        WaitForSingleObject(g_gateEntered, 2000);
        const int32_t n = Stark::DropQueuedInvokesOf(kOwnerB);
        const bool bAnswered = B.Wait(1000);
        const size_t qs = QueueSize();
        const Outs during = Out();
        SetEvent(g_gateRelease);
        A.Wait(2000);
        g_game.WaitPumped(ticket, 2000);
        check("QUEUE one at a time ⭐: the request behind the running one is still queued, so its owner's drop reaches it",
              n == 1 && bAnswered && B.rc == Stark::kInvokeDroppedOwnerLeft && HasLog('I', droppedOwner) && qs == 0
              && during == Outs{ { kOwnerA, 1 } } && A.rc == 0 && !AnyCallWithA(21),
              F("n=%d B.rc=%d qs=%zu", n, B.rc, qs).c_str());

        ResetStark();
        AddParams g2{ 2, 3, 0 }, y{ 31, 1, 0 };
        Caller& G = NewCaller();
        G.Start(kOwnerA, false, kFuncGate, &g2, sizeof g2);
        WaitDepth(1);
        const int t2 = g_game.PumpAsync();
        WaitForSingleObject(g_gateEntered, 2000);
        Caller& L = NewCaller();
        L.Start(kOwnerA, false, kFuncAdd, &y, sizeof y);
        WaitDepth(1);
        SetEvent(g_gateRelease);
        g_game.WaitPumped(t2, 2000);
        const bool lateWaits = !L.done.load() && QueueSize() == 1 && !AnyCallWithA(31);
        g_game.Pump();
        L.Wait(2000);
        check("QUEUE budget ⭐: a request queued during a drain waits for the next ProcessEvent", lateWaits
              && L.rc == 0 && y.out == 32 && G.rc == 0);

        ResetStark();
        AddParams nst{ 41, 1, 0 }, m{ 42, 1, 0 };
        Caller& N = NewCaller();
        N.Start(kOwnerA, false, kFuncNest, &nst, sizeof nst);
        WaitDepth(1);
        Caller& M = NewCaller();
        M.Start(kOwnerA, false, kFuncAdd, &m, sizeof m);
        WaitDepth(2);
        g_game.Pump();
        N.Wait(2000);
        M.Wait(2000);
        std::vector<std::string> seq;
        { std::lock_guard<std::mutex> lk(g_peMu); seq = g_seq; }
        const std::vector<std::string> want{ "+F004@0", "+7101@1", "-7101", "-F004", "+F001@0", "-F001", "+7101@0", "-7101" };
        std::string got;
        for (const auto& s : seq) got += s + " ";
        check("QUEUE no nesting ⭐: a request's own ProcessEvent forwards without draining the rest inside it",
              N.rc == 0 && M.rc == 0 && nst.out == 42 && m.out == 43 && seq == want, got.c_str());
    }

    {   blk("QUEUE OWNERS - a departed owner's queued invokes are dropped; retag and the if_free blocker");
        ResetStark();
        Stark::SetInvokeTimeoutMs(100);
        AddParams z{ 1, 0, 0 }, a1{ 11, 0, 0 }, a2{ 12, 0, 0 }, b1{ 21, 0, 0 }, a3{ 13, 0, 0 };
        Caller& Z = NewCaller();  Z.Start(0, false, kFuncAdd, &z, sizeof z, false, true);        Z.Wait(2000);
        Caller& A1 = NewCaller(); A1.Start(kOwnerA, false, kFuncAdd, &a1, sizeof a1, false, true); A1.Wait(2000);
        Caller& A2 = NewCaller(); A2.Start(kOwnerA, false, kFuncAdd, &a2, sizeof a2, false, true); A2.Wait(2000);
        Caller& B1 = NewCaller(); B1.Start(kOwnerB, false, kFuncAdd, &b1, sizeof b1, false, true); B1.Wait(2000);
        Caller& A3 = NewCaller(); A3.Start(kOwnerA, false, kFuncAdd, &a3, sizeof a3, true, false); A3.Wait(2000);
        const bool setup = Out() == Outs{ { 0, 1 }, { kOwnerA, 2 }, { kOwnerB, 1 } } && QueueSize() == 5;
        const int32_t n0 = Stark::DropQueuedInvokesOf(0);
        const size_t afterN0 = QueueSize();
        const int32_t nA = Stark::DropQueuedInvokesOf(kOwnerA);
        check("QUEUE drop ⭐: an owner's queued invokes are answered -26 and taken out; owner 0 is nobody's to drop",
              setup && n0 == 0 && afterN0 == 5 && nA == 2 && Get(A1.queued) == Stark::kInvokeDroppedOwnerLeft
              && Get(A2.queued) == Stark::kInvokeDroppedOwnerLeft && CountLog('I', droppedOwner) == 2
              && Out() == Outs{ { 0, 1 }, { kOwnerB, 1 } } && QueueSize() == 3 && Stark::s_queueDepth.load() == 3,
              F("n0=%d nA=%d qs=%zu", n0, nA, QueueSize()).c_str());
        g_game.Pump();
        bool onlyKept = true;
        for (const auto& c : Calls(kFuncAdd)) onlyKept = onlyKept && (c.seen.a == 1 || c.seen.a == 21);
        check("QUEUE drop: the rest run, the abandoned one is skipped", onlyKept && Calls(kFuncAdd).size() == 2
              && HasLog('I', droppedAbandoned) && Get(Z.queued) == 0 && Get(B1.queued) == 0 && Out().empty()
              && QueueSize() == 0);

        ResetStark();
        Stark::SetInvokeTimeoutMs(100);
        AddParams r{ 51, 0, 0 };
        Caller& R = NewCaller();
        R.Start(kOwnerA, false, kFuncAdd, &r, sizeof r, false, true);
        R.Wait(2000);
        const auto fr = Front();
        const bool idOk = R.id != 0 && fr && fr->id == R.id;
        const bool re1 = Stark::RetagQueued(R.id, kOwnerB);
        const bool movedOwner = fr && fr->owner == kOwnerB && Out() == Outs{ { kOwnerB, 1 } };
        const bool re2 = Stark::RetagQueued(R.id, kOwnerB);
        const bool sameAfter = Out() == Outs{ { kOwnerB, 1 } };
        const bool reZero = Stark::RetagQueued(0, kOwnerB);
        const uint64_t blockA = Stark::IfFreeBlocker(kOwnerA), blockB = Stark::IfFreeBlocker(kOwnerB);
        const int32_t dropA = Stark::DropQueuedInvokesOf(kOwnerA), dropB = Stark::DropQueuedInvokesOf(kOwnerB);
        const bool reAnswered = Stark::RetagQueued(R.id, kOwnerA);
        check("QUEUE retag ⭐: a queued request moves to its new owner, count and all, once", idOk && re1 && movedOwner
              && re2 && sameAfter && !reZero && blockA == kOwnerB && blockB == 0 && dropA == 0 && dropB == 1
              && Get(R.queued) == Stark::kInvokeDroppedOwnerLeft && !reAnswered && Out().empty());

        ResetStark();
        AddParams g{ 2, 3, 0 };
        Caller& G = NewCaller();
        G.Start(kOwnerA, false, kFuncGate, &g, sizeof g);
        WaitDepth(1);
        const uint64_t gid = Front() ? Front()->id : 0;
        const int ticket = g_game.PumpAsync();
        WaitForSingleObject(g_gateEntered, 2000);
        const bool reRunning = Stark::RetagQueued(gid, kOwnerB);
        const Outs during = Out();
        SetEvent(g_gateRelease);
        G.Wait(2000);
        g_game.WaitPumped(ticket, 2000);
        Stark::SetInvokeTimeoutMs(100);
        AddParams d{ 1, 1, 0 };
        Caller& D = NewCaller();
        D.Start(kOwnerA, false, kFuncAdd, &d, sizeof d, true);
        D.Wait(2000);
        const bool reAbandoned = Stark::RetagQueued(D.id, kOwnerB);
        check("QUEUE retag: not once the drain runs it, nor once its waiter abandoned it", gid != 0 && !reRunning
              && during == Outs{ { kOwnerA, 1 } } && G.rc == 0 && !reAbandoned && Out().empty() && QueueSize() == 1);
        g_game.Pump();
    }

    {   blk("QUEUE SHUTDOWN - a shutdown cuts the wait short only when the game thread is stalled");
        const std::string cutPrefix = "GameThreadDispatch: invoke wait cut short after ";
        ResetStark();
        Stark::SetInvokeTimeoutMs(3000);
        Stark::s_lastHookFireMs.store(1);
        Tot::RequestShutdown();
        AddParams b{ 2, 3, 0 };
        Caller& A = NewCaller();
        A.Start(kOwnerA, false, kFuncAdd, &b, sizeof b, false, true);
        const bool cutFast = A.Wait(1500);
        Tot::ResetShutdown();
        const std::string cut = FirstLog('W', cutPrefix);
        const std::string cutTail = F(" of 3000 ms -- a shutdown is under way and the game thread is stalled; the request "
                                      "stays queued inst=0x%llX func=0x%llX", (unsigned long long)kInst,
                                      (unsigned long long)kFuncAdd);
        check("QUEUE shutdown ⭐: shutdown and a stalled game cut a 3000 ms wait short; the request stays queued",
              cutFast && A.rc == -5 && A.elapsed < 1500 && cut.size() > cutTail.size()
              && cut.compare(cut.size() - cutTail.size(), cutTail.size(), cutTail) == 0
              && CountLog('E', "GameThreadDispatch: invoke timeout") == 0 && QueueSize() == 1
              && Out() == Outs{ { kOwnerA, 1 } } && A.queued.valid() && !Ready(A.queued),
              F("rc=%d elapsed=%llu", A.rc, (unsigned long long)A.elapsed).c_str());
        g_game.Pump();
        check("QUEUE shutdown: the cut request still runs on the next ProcessEvent, on its own copy",
              Get(A.queued) == 0 && Calls(kFuncAdd).size() == 1 && b.out == 0);

        ResetStark();
        Stark::SetInvokeTimeoutMs(200);
        std::atomic<bool> pin{ true };
        std::thread pinner([&] { while (pin.load()) { Stark::s_lastHookFireMs.store(Stark::NowMs() - 600); Sleep(5); } });
        Tot::RequestShutdown();
        AddParams b2{ 2, 3, 0 };
        Caller& Q = NewCaller();
        Q.Start(kOwnerA, false, kFuncAdd, &b2, sizeof b2);
        Q.Wait(1500);
        pin.store(false);
        pinner.join();
        Tot::ResetShutdown();
        const bool quietNotCut = Q.rc == -5 && Q.elapsed >= 200 && CountLog('W', cutPrefix) == 0
            && HasLog('E', F("GameThreadDispatch: invoke timeout (200ms) inst=0x%llX func=0x%llX", (unsigned long long)kInst,
                             (unsigned long long)kFuncAdd));
        ResetStark();
        Stark::SetInvokeTimeoutMs(200);
        Stark::s_lastHookFireMs.store(1);
        AddParams b3{ 2, 3, 0 };
        Caller& S = NewCaller();
        S.Start(kOwnerA, false, kFuncAdd, &b3, sizeof b3);
        S.Wait(1500);
        check("QUEUE shutdown: a 600 ms quiet game at shutdown, or a stall with no shutdown, waits out its timeout",
              quietNotCut && S.rc == -5 && S.elapsed >= 200 && CountLog('W', cutPrefix) == 0
              && CountLog('E', "GameThreadDispatch: invoke timeout (200ms)") == 1);

        ResetStark();
        Stark::SetInvokeTimeoutMs(3000);
        AddParams g{ 2, 3, 0 };
        Caller& G = NewCaller();
        G.Start(kOwnerA, false, kFuncGate, &g, sizeof g, true);
        WaitDepth(1);
        const int ticket = g_game.PumpAsync();
        WaitForSingleObject(g_gateEntered, 2000);
        Stark::s_lastHookFireMs.store(1);
        Tot::RequestShutdown();
        const bool cutLogged = WaitLog('W', cutPrefix, 1500);
        Sleep(200);
        const bool doneAfterCut = G.done.load();
        SetEvent(g_gateRelease);
        G.Wait(2000);
        Tot::ResetShutdown();
        g_game.WaitPumped(ticket, 2000);
        check("QUEUE shutdown: a cut wait on a request the drain already runs waits it out", cutLogged && !doneAfterCut
              && G.rc == 0 && g.out == 5);

        ResetStark();
        Stark::SetInvokeTimeoutMs(100);
        AddParams q1b{ 1, 1, 0 }, b4{ 2, 3, 0 }, b5{ 2, 3, 0 };
        Caller& Q1 = NewCaller();
        Q1.Start(kOwnerA, false, kFuncAdd, &q1b, sizeof q1b, false, true);
        Q1.Wait(2000);
        Stark::SetInvokeTimeoutMs(5000);
        Caller& W = NewCaller();
        W.Start(kOwnerA, false, kFuncAdd, &b4, sizeof b4);
        WaitDepth(2);
        Stark::Shutdown();
        W.Wait(2000);
        check("QUEUE shutdown ⭐: Shutdown answers every queued request -7 and clears the counts", Get(Q1.queued) == -7
              && W.rc == -7 && HasLog('I', "GameThreadDispatch: invoke completed result=-7")
              && HasLog('I', "GameThreadDispatch: hook flag cleared (physical hook retained to avoid in-flight unhook race)")
              && Out().empty() && QueueSize() == 0 && Stark::s_queueDepth.load() == 0 && !Stark::IsHookActive());
        const int enqueuedBefore = CountLog('I', enqueued);
        Caller& X = NewCaller();
        X.Start(kOwnerA, false, kFuncAdd, &b5, sizeof b5);
        X.Wait(2000);
        check("QUEUE inactive: with the hook off an invoke is refused -7 at once, nothing queued", X.rc == -7
              && X.elapsed < 1000 && X.id == 0 && QueueSize() == 0 && Out().empty()
              && CountLog('I', enqueued) == enqueuedBefore);
    }

    {   blk("QUEUE FAULT - a queued call that faults answers -4, and the drain goes on");
        ResetStark();
        AddParams r{ 61, 0, 0 }, s{ 62, 1, 0 };
        Caller& A1 = NewCaller();
        A1.Start(kOwnerA, false, kFuncRaise, &r, sizeof r);
        WaitDepth(1);
        Caller& A2 = NewCaller();
        A2.Start(kOwnerA, false, kFuncAdd, &s, sizeof s);
        WaitDepth(2);
        g_game.Pump();
        A1.Wait(2000);
        A2.Wait(2000);
        check("QUEUE fault ⭐: -4 for the faulting call, logged, and the next request still runs", A1.rc == -4
              && A2.rc == 0 && s.out == 63 && HasLog('E', F("GameThreadDispatch: SEH exception during queued PE call "
              "inst=0x%llX func=0x%llX", (unsigned long long)kInst, (unsigned long long)kFuncRaise)) && Out().empty()
              && g_game.Pump(), F("A1=%d A2=%d", A1.rc, A2.rc).c_str());
    }

    {   blk("QUEUE TIMEOUT SETTING - clamped, and the wait uses it");
        ResetStark();
        const int32_t in[] = { 0, -3, 1, 99, 100, 1234, 600000, 600001, INT32_MAX };
        const int32_t want[] = { 5000, 5000, 100, 100, 100, 1234, 600000, 600000, 600000 };
        bool clamps = true;
        for (size_t i = 0; i < sizeof in / sizeof in[0]; ++i) {
            Stark::SetInvokeTimeoutMs(in[i]);
            clamps = clamps && Stark::GetInvokeTimeoutMs() == want[i];
        }
        Stark::SetInvokeTimeoutMs(150);
        AddParams b{ 1, 1, 0 };
        Caller& A = NewCaller();
        A.Start(kOwnerA, false, kFuncAdd, &b, sizeof b);
        A.Wait(2000);
        check("QUEUE timeout setting: clamped to 100..600000, 0 or less the default; the wait honours it", clamps
              && A.rc == -5 && A.elapsed >= 150 && A.elapsed < 1000 && HasLog('E', F("GameThreadDispatch: invoke timeout "
              "(150ms) inst=0x%llX func=0x%llX", (unsigned long long)kInst, (unsigned long long)kFuncAdd)));
        g_game.Pump();
    }

    {   blk("QUEUE OWN CALL - an owner scope nests and restores; Stark's own calls are marked and never drain");
        ResetStark();
        bool scopes = false;
        RunOn([&] {
            const auto at = [] { return std::make_pair(Stark::CurrentInvokeOwner(), Stark::CurrentInvokeIfFree()); };
            const auto s0 = at();
            std::pair<uint64_t, bool> s1, s2, s3;
            {
                const Stark::InvokeOwnerScope a(kOwnerA, false);
                s1 = at();
                {
                    const Stark::InvokeOwnerScope b(kOwnerB, true);
                    s2 = at();
                }
                s3 = at();
            }
            const auto s4 = at();
            scopes = s0 == std::make_pair(uint64_t{ 0 }, false) && s1 == std::make_pair(kOwnerA, false)
                && s2 == std::make_pair(kOwnerB, true) && s3 == std::make_pair(kOwnerA, false)
                && s4 == std::make_pair(uint64_t{ 0 }, false);
        });
        check("QUEUE owner scope: nested scopes set the owner and if_free, and each restores the one before", scopes);

        ResetStark();
        AddParams q{ 71, 1, 0 };
        Caller& A = NewCaller();
        A.Start(kOwnerA, false, kFuncAdd, &q, sizeof q);
        WaitDepth(1);
        AddParams p{ 5, 6, 0 }, p2{ 8, 1, 0 };
        int32_t rc1 = 0, rc2 = 0, rc3 = 0, rc4 = 0;
        bool markedAfter = true;
        RunOn([&] {
            rc1 = Stark::CallAddressAsOwnSEH(reinterpret_cast<uintptr_t>(&FakeOverride), kInst, kFuncDirect,
                                             reinterpret_cast<uintptr_t>(&p));
            rc2 = Stark::CallOriginalSEH(kInst, kFuncNest, reinterpret_cast<uintptr_t>(&p2));
            rc3 = Stark::CallAddressAsOwnSEH(reinterpret_cast<uintptr_t>(&FakeOverrideRaise), kInst, kFuncDirect,
                                             reinterpret_cast<uintptr_t>(&p));
            rc4 = Stark::CallAddressAsOwnSEH(0, kInst, kFuncDirect, reinterpret_cast<uintptr_t>(&p));
            markedAfter = Stark::InOwnPeCall();
        });
        const auto direct = Calls(kFuncDirect);
        const auto nest = Calls(kFuncNest);
        const bool undrained = !AnyCallWithA(71) && QueueSize() == 1 && !A.done.load();
        check("QUEUE own call ⭐: an override called as our own sees the mark, and its Super call drains nothing",
              rc1 == 0 && g_overrideCalls.load() >= 1 && g_overrideSawOwn.load() && direct.size() == 1
              && direct[0].obj == kInst && direct[0].params == reinterpret_cast<uintptr_t>(&p) && p.out == 11 && undrained,
              F("rc1=%d overrides=%d sawOwn=%d", rc1, g_overrideCalls.load(), (int)g_overrideSawOwn.load()).c_str());
        check("QUEUE own call: the original's own call is marked too, and a nested ProcessEvent in it drains nothing",
              rc2 == 0 && nest.size() == 1 && nest[0].own && p2.out == 9 && undrained);
        check("QUEUE own call: a fault answers -4, no address -3, and the mark is released after", rc3 == -4 && rc4 == -3
              && !markedAfter, F("rc3=%d rc4=%d", rc3, rc4).c_str());
        g_game.Pump();
        A.Wait(2000);
        const auto q1 = Calls(kFuncAdd);
        check("QUEUE own call: the queued request runs on the game thread's next ProcessEvent", A.rc == 0 && q.out == 72
              && q1.size() == 1 && q1[0].tid == g_game.tid.load());
    }
}

// The game thread's side: which entries count as its, the liveness they stamp, the own-call mark, a fault contained,
// the profiler fed and a throw contained, the worker-thread skip, and the pick and its validation. Here the main
// thread plays the game thread wherever one is named; the fake game thread of the queue blocks has stopped.
constexpr uint32_t kNoThread = 1234;   // not a multiple of 4: never a real thread id, a pick that never fires
static DWORD g_mainTid = 0;

static void ResetThread(uint32_t gameTid, Stark::GameThreadIdSource src) {
    ResetStark();
    Linie::StopRecording();
    Linie::Reset();
    Stark::s_hookInstalledMs.store(Stark::NowMs());
    Stark::s_hookFireCount.store(0);
    Stark::s_gameThreadFireCount.store(0);
    Stark::s_otherThreadFireCount.store(0);
    Stark::s_workerSkipCount.store(0);
    Stark::s_gameThreadId.store(gameTid);
    Stark::s_gameThreadSource.store(static_cast<uint8_t>(src));
    Stark::s_gameThreadTried.store(true);
    Stark::SetInvokeTimeoutMs(10000);
    { std::lock_guard<std::mutex> lk(g_logMu); g_log.clear(); }
}
static void Fire(uintptr_t obj, uintptr_t f, int times = 1) {
    for (int i = 0; i < times; ++i)
        Stark::HookedProcessEvent(reinterpret_cast<void*>(obj), reinterpret_cast<void*>(f), reinterpret_cast<void*>(kParams));
}
static Caller& Pipe(uintptr_t inst, uintptr_t f) {
    Caller& c = NewCaller();
    c.inst = inst;
    c.Start(0, false, f, nullptr, 0);
    return c;
}
static std::vector<PeCall> Recs() { std::lock_guard<std::mutex> lk(g_peMu); return g_pe; }
static int IndexOf(const std::vector<PeCall>& r, char via, uintptr_t f, DWORD tid) {
    for (size_t i = 0; i < r.size(); ++i) if (r[i].via == via && r[i].ufunc == f && r[i].tid == tid) return static_cast<int>(i);
    return -1;
}
static std::string Tail(const std::string& s, size_t n) { return s.size() >= n ? s.substr(s.size() - n) : s; }

static void ThreadBlocks() {
    using Src = Stark::GameThreadIdSource;
    using Live = Stark::GameThreadLiveness;

    {   blk("THREAD FIRES - a ProcessEvent counts for the game thread, or against the pick, and our own calls not at all");
        ResetThread(g_mainTid, Src::Description);
        Fire(kObj, kPlain, 4);
        DWORD wtid = 0;
        int32_t rc = -99;
        RunOn([&] {
            wtid = GetCurrentThreadId();
            Fire(kObj, kPlain, 3);
            rc = Stark::CallOriginalSEH(kObj, kReenter, kParams);
        });
        const auto r = Recs();
        int mainPlain = 0, workerPlain = 0;
        bool shaped = r.size() == 9;
        for (const auto& c : r) {
            shaped = shaped && c.via == 'P' && c.obj == kObj && c.params == kParams;
            if (c.ufunc == kPlain && c.tid == g_mainTid) ++mainPlain;
            if (c.ufunc == kPlain && c.tid == wtid) ++workerPlain;
        }
        const int re = IndexOf(r, 'P', kReenter, wtid), ne = IndexOf(r, 'P', kNested, wtid);
        check("THREAD fires ⭐: the game thread's entries and the others' counted apart; our own nested call in neither",
              Stark::GetHookFireCount() == 8 && Stark::GetGameThreadFireCount() == 4 && Stark::GetOtherThreadFireCount() == 3
              && rc == 0, F("hook=%llu game=%llu other=%llu", (unsigned long long)Stark::GetHookFireCount(),
              (unsigned long long)Stark::GetGameThreadFireCount(), (unsigned long long)Stark::GetOtherThreadFireCount()).c_str());
        check("THREAD fires: every call forwarded with its object and parameters, on its own thread", shaped
              && mainPlain == 4 && workerPlain == 3 && re >= 0 && ne > re && r[re].own && r[ne].own
              && Stark::s_lastHookFireMs.load() != 0);

        ResetThread(0, Src::Unknown);
        Fire(kObj, kPlain, 2);
        RunOn([] { Fire(kObj, kPlain, 2); });
        check("THREAD fires: with no pick neither counter moves, and every entry stamps", Stark::GetHookFireCount() == 4
              && Stark::GetGameThreadFireCount() == 0 && Stark::GetOtherThreadFireCount() == 0
              && Stark::s_lastHookFireMs.load() != 0);
    }

    {   blk("THREAD LIVENESS - only the game thread's fires keep it looking alive; the thresholds and the grace after install");
        ResetThread(g_mainTid, Src::Description);
        Stark::s_lastHookFireMs.store(0);
        const uint64_t never = Stark::MsSinceLastHookFire();
        Stark::s_lastHookFireMs.store(Stark::NowMs() + 60000);
        const uint64_t ahead = Stark::MsSinceLastHookFire();
        Stark::s_lastHookFireMs.store(Stark::NowMs() - 1000);
        const uint64_t old = Stark::MsSinceLastHookFire();
        check("THREAD liveness: never fired is UINT64_MAX, a stamp ahead of the clock 0, an old one its age",
              never == UINT64_MAX && ahead == 0 && old >= 1000 && old < 3000);

        ResetThread(g_mainTid, Src::Description);
        Stark::s_hookInstalledMs.store(Stark::NowMs() - 10000);
        const uint64_t stale = Stark::NowMs() - 5000;
        Stark::s_lastHookFireMs.store(stale);
        RunOn([] { Fire(kObj, kPlain, 10); });
        const bool workerKeptStale = Stark::s_lastHookFireMs.load() == stale && Stark::MsSinceLastHookFire() >= 5000
            && Stark::GetGameThreadLiveness(0) == Live::Stalled && !Stark::IsGameThreadResponsive(0);
        const uint64_t t0 = Stark::NowMs();
        Fire(kObj, kPlain);
        const uint64_t t1 = Stark::NowMs();
        const uint64_t stamp = Stark::s_lastHookFireMs.load();
        check("THREAD liveness ⭐: a worker's fires do not keep a stalled game thread alive; the game thread's do",
              workerKeptStale && stamp >= t0 && stamp <= t1 && Stark::MsSinceLastHookFire() <= 1000
              && Stark::GetGameThreadLiveness(0) == Live::Responsive && Stark::IsGameThreadResponsive(0));

        ResetThread(0, Src::Unknown);
        Stark::s_lastHookFireMs.store(Stark::NowMs() - 5000);
        const uint64_t u0 = Stark::NowMs();
        RunOn([] { Fire(kObj, kPlain); });
        const uint64_t u1 = Stark::NowMs();
        const uint64_t us = Stark::s_lastHookFireMs.load();
        check("THREAD liveness: with no pick any thread's fire stamps", us >= u0 && us <= u1
              && Stark::GetGameThreadLiveness(0) == Live::Responsive);

        ResetThread(g_mainTid, Src::Description);
        Stark::s_hookInstalledMs.store(Stark::NowMs() - 10000);
        Stark::s_lastHookFireMs.store(Stark::NowMs() - 1000);
        const bool a = Stark::GetGameThreadLiveness(0) == Live::Stalled && Stark::GetGameThreadLiveness(-1) == Live::Stalled
            && Stark::GetGameThreadLiveness(2000) == Live::Responsive && !Stark::IsGameThreadResponsive(0)
            && Stark::IsGameThreadResponsive(2000);
        bool b = false, sampled = false;
        for (int i = 0; i < 5 && !sampled; ++i) {
            const uint64_t n0 = Stark::NowMs();
            Stark::s_lastHookFireMs.store(n0 - 100);
            const Live r0 = Stark::GetGameThreadLiveness(0), r50 = Stark::GetGameThreadLiveness(50);
            const bool resp50 = Stark::IsGameThreadResponsive(50);
            if (Stark::NowMs() - n0 > 200) continue;
            sampled = true;
            b = r0 == Live::Responsive && r50 == Live::Stalled && !resp50;
        }
        check("THREAD liveness: 0 or less means the default threshold, and a threshold given is honoured", a && sampled && b);

        ResetThread(g_mainTid, Src::Description);
        Stark::s_lastHookFireMs.store(0);
        Stark::s_hookInstalledMs.store(Stark::NowMs());
        const bool justInstalled = Stark::GetGameThreadLiveness(0) == Live::Unknown && Stark::IsGameThreadResponsive(0);
        Stark::s_hookInstalledMs.store(Stark::NowMs() - 2000);
        const bool neverFired = Stark::GetGameThreadLiveness(0) == Live::Stalled && !Stark::IsGameThreadResponsive(0);
        Stark::s_hookInstalledMs.store(0);
        const bool noInstallTime = Stark::GetGameThreadLiveness(0) == Live::Unknown && Stark::IsGameThreadResponsive(0);
        check("THREAD liveness: just installed is not yet known; installed 2 s ago and never fired is stalled",
              justInstalled && neverFired && noInstallTime && Stark::MsSinceLastHookFire() == UINT64_MAX);

        ResetThread(g_mainTid, Src::Description);
        Stark::s_lastHookFireMs.store(Stark::NowMs() - 5000);
        Stark::RemoveHook();
        Fire(kObj, kPlain);
        const int32_t rc = Stark::EnqueueInvoke(kObj, kQueued, 0, 0);
        check("THREAD soft disable ⭐: with the hook off it still forwards and stamps, liveness is not known, invokes -7",
              !Stark::IsHookActive() && CountLog('I', "GameThreadDispatch: hook flag cleared (physical hook retained to "
              "avoid in-flight unhook race)") == 1 && IndexOf(Recs(), 'P', kPlain, g_mainTid) >= 0
              && Stark::MsSinceLastHookFire() <= 1000 && Stark::GetGameThreadLiveness(0) == Live::Unknown
              && Stark::IsGameThreadResponsive(0) && Stark::HasOriginal() && rc == -7, F("rc=%d", rc).c_str());
        Stark::s_hookActive.store(true);
    }

    {   blk("THREAD OWN CALL - Stark's own calls are marked for their whole length, nest, and never drain");
        ResetThread(g_mainTid, Src::Description);
        bool before = true, mid = true, after = true;
        int32_t r1 = -99, r2 = -99, r3 = -99;
        RunOn([&] {
            before = Stark::InOwnPeCall();
            r1 = Stark::CallOriginalSEH(kObj, kPlain, kParams);
            mid = Stark::InOwnPeCall();
            r2 = Stark::CallAddressAsOwnSEH(reinterpret_cast<uintptr_t>(&FakeOverride), kObj, kPlain, kParams);
            r3 = Stark::CallAddressAsOwnSEH(reinterpret_cast<uintptr_t>(&FakeOverride), kObj, kNestOwn, kParams);
            after = Stark::InOwnPeCall();
        });
        const auto r = Recs();
        bool order = r.size() == 4 && r[0].via == 'P' && r[0].ufunc == kPlain && r[1].via == 'V' && r[1].ufunc == kPlain
            && r[2].via == 'V' && r[2].ufunc == kNestOwn && r[3].via == 'P' && r[3].ufunc == kPlain;
        for (const auto& c : r) order = order && c.own && c.obj == kObj && c.params == kParams;
        check("THREAD own call ⭐: marked inside, not outside; an inner own call returning leaves the outer one marked",
              !before && !mid && !after && r1 == 0 && r2 == 0 && r3 == 0 && g_innerRc.load() == 0 && order
              && g_afterInnerOwn.load());

        ResetThread(0, Src::Unknown);
        Caller& P = Pipe(kObj2, kQueued);
        WaitDepth(1);
        int32_t rA = -99, rB = -99;
        RunOn([&] {
            rA = Stark::CallAddressAsOwnSEH(reinterpret_cast<uintptr_t>(&FakeOverride), kObj, kSuper, kParams);
            rB = Stark::CallOriginalSEH(kObj, kReenter, kParams);
        });
        const auto m = Recs();
        const bool mailbox = m.size() == 4 && m[0].via == 'V' && m[0].ufunc == kSuper && m[1].via == 'P'
            && m[1].ufunc == kNested && m[2].via == 'P' && m[2].ufunc == kReenter && m[3].via == 'P'
            && m[3].ufunc == kNested && m[0].own && m[1].own && m[2].own && m[3].own;
        const bool held = rA == 0 && rB == 0 && Stark::GetHookFireCount() == 2 && Stark::s_queueDepth.load() == 1
            && !P.done.load();
        Fire(kObj, kPlain);
        P.Wait(2000);
        const auto g = Recs();
        const int q = IndexOf(g, 'P', kQueued, g_mainTid), pl = IndexOf(g, 'P', kPlain, g_mainTid);
        check("THREAD own call ⭐: Super::ProcessEvent re-entering the hook inside our own call drains nothing",
              mailbox && held, F("recs=%zu rA=%d rB=%d depth=%zu", m.size(), rA, rB, Stark::s_queueDepth.load()).c_str());
        check("THREAD own call: the game thread's next ProcessEvent drains it, before its own call", q >= 0 && pl > q
              && g[q].obj == kObj2 && !g[q].own && P.rc == 0 && Stark::s_queueDepth.load() == 0);

        ResetThread(0, Src::Unknown);
        ResetEvent(g_inside);
        ResetEvent(g_release);
        Caller& P2 = Pipe(kObj2, kQueued);
        WaitDepth(1);
        int32_t rc = -99;
        std::thread mb([&] {
            rc = Stark::CallAddressAsOwnSEH(reinterpret_cast<uintptr_t>(&FakeOverride), kObj, kBlock, kParams);
        });
        const bool inside = WaitForSingleObject(g_inside, 10000) == WAIT_OBJECT_0;
        const bool ownOnMain = Stark::InOwnPeCall();
        Fire(kObj, kPlain);
        SetEvent(g_release);
        mb.join();
        P2.Wait(2000);
        const auto t = Recs();
        const int tq = IndexOf(t, 'P', kQueued, g_mainTid), tp = IndexOf(t, 'P', kPlain, g_mainTid);
        int vb = -1;
        for (size_t i = 0; i < t.size(); ++i) if (t[i].via == 'V' && t[i].ufunc == kBlock) vb = static_cast<int>(i);
        check("THREAD own call ⭐: the mark is per thread -- the game thread drains while another is inside its own call",
              inside && !ownOnMain && tq >= 0 && tp > tq && P2.rc == 0 && vb >= 0 && t[vb].own && rc == 0);

        ResetThread(g_mainTid, Src::Description);
        Caller& P3 = Pipe(kObj2, kReenter);
        WaitDepth(1);
        Fire(kObj, kPlain);
        P3.Wait(2000);
        const auto d = Recs();
        const int dr = IndexOf(d, 'P', kReenter, g_mainTid), dn = IndexOf(d, 'P', kNested, g_mainTid);
        const int dp = IndexOf(d, 'P', kPlain, g_mainTid);
        check("THREAD own call: the drain is not our own call -- the game's nested dispatch in it counts as the game's",
              dr >= 0 && dn > dr && dp > dn && !d[dr].own && !d[dn].own && Stark::GetGameThreadFireCount() == 2
              && Stark::GetOtherThreadFireCount() == 0 && Stark::GetHookFireCount() == 2 && P3.rc == 0);
    }

    {   blk("THREAD FAULTS - a fault inside a call is contained, answered, and the mark released");
        ResetThread(g_mainTid, Src::Description);
        bool h0 = true, h1 = false, o1 = true, o2 = true;
        int32_t a = 0, b = 0, c = -99;
        RunOn([&] {
            Stark::s_originalPE = nullptr;
            h0 = Stark::HasOriginal();
            a = Stark::CallOriginalSEH(kObj, kPlain, kParams);
            o1 = Stark::InOwnPeCall();
            Stark::s_originalPE = &FakePE;
            h1 = Stark::HasOriginal();
            b = Stark::CallOriginalSEH(kObj, kFault, kParams);
            o2 = Stark::InOwnPeCall();
            c = Stark::CallOriginalSEH(kObj, kPlain, kParams);
        });
        const auto r = Recs();
        int faults = 0;
        for (const auto& x : r) if (x.ufunc == kFault && x.own) ++faults;
        check("THREAD fault ⭐: no original -3; a fault inside -4 with the process alive; the mark released after",
              !h0 && a == -3 && !o1 && h1 && b == -4 && faults == 1 && !o2 && c == 0 && r.size() == 2,
              F("a=%d b=%d c=%d recs=%zu", a, b, c, r.size()).c_str());

        ResetThread(g_mainTid, Src::Description);
        Stark::s_originalPE = &OtherPE;
        int32_t x1 = 0, x2 = 0, x3 = -99;
        bool ox = true;
        RunOn([&] {
            x1 = Stark::CallAddressAsOwnSEH(0, kObj, kPlain, kParams);
            x2 = Stark::CallAddressAsOwnSEH(reinterpret_cast<uintptr_t>(&FakePE), kObj, kFault, kParams);
            ox = Stark::InOwnPeCall();
            x3 = Stark::CallAddressAsOwnSEH(reinterpret_cast<uintptr_t>(&FakeOverride), kObj, kPlain, kParams);
        });
        Stark::s_originalPE = &FakePE;
        const auto s = Recs();
        int pf = 0, v = 0, o = 0;
        for (const auto& y : s) {
            if (y.via == 'P' && y.ufunc == kFault && y.own) ++pf;
            if (y.via == 'V' && y.ufunc == kPlain && y.own && y.obj == kObj && y.params == kParams) ++v;
            if (y.via == 'O') ++o;
        }
        check("THREAD fault: an address call answers -3 with no address, -4 on a fault; the address given is the one called",
              x1 == -3 && x2 == -4 && pf == 1 && !ox && x3 == 0 && v == 1 && o == 0);

        ResetThread(g_mainTid, Src::Description);
        Caller& P = Pipe(kObj2, kFault);
        WaitDepth(1);
        Fire(kObj, kPlain);
        P.Wait(2000);
        const auto q = Recs();
        const int qf = IndexOf(q, 'P', kFault, g_mainTid), qp = IndexOf(q, 'P', kPlain, g_mainTid);
        check("THREAD fault ⭐: a queued call that faults is answered -4 and logged, and the game's own call still runs",
              P.rc == -4 && CountLog('E', "GameThreadDispatch: SEH exception during queued PE call") == 1
              && HasLog('E', F("GameThreadDispatch: SEH exception during queued PE call inst=0x%llX func=0x%llX",
                               (unsigned long long)kObj2, (unsigned long long)kFault))
              && qf >= 0 && qp > qf && Stark::s_queueDepth.load() == 0 && Stark::GetHookFireCount() == 1);
    }

    {   blk("THREAD PROFILER AND GUARD - the hook feeds the profiler, and a throw in our work never reaches the game");
        ResetThread(g_mainTid, Src::Description);
        Linie::StartRecording();
        Caller& P = Pipe(kObj2, kQueued);
        WaitDepth(1);
        const uint64_t t0 = Stark::NowMs();
        Fire(kObj, kFa, 3);
        Fire(kObj, kFb, 1);
        RunOn([] { Fire(kObj, kFc, 2); });
        const uint64_t t1 = Stark::NowMs();
        Linie::StopRecording();
        Fire(kObj, kFa, 5);
        std::vector<Linie::FuncStat> rows;
        uint64_t act = 0;
        Linie::Snapshot(rows, act);
        P.Wait(2000);
        Linie::Reset();
        auto row = [&](uintptr_t f) -> const Linie::FuncStat* {
            for (const auto& x : rows) if (x.func == f) return &x;
            return nullptr;
        };
        const Linie::FuncStat *ra = row(kFa), *rb = row(kFb), *rc = row(kFc);
        bool inWindow = rows.size() == 3;
        for (const auto& x : rows) inWindow = inWindow && t0 <= x.firstMs && x.firstMs <= x.lastMs && x.lastMs <= t1;
        check("THREAD profiler ⭐: each function's fires counted in order, worker threads' too, none after the stop",
              ra && rb && rc && ra->count == 3 && ra->firstSeq == 1 && rb->count == 1 && rb->firstSeq == 4 && rc->count == 2
              && rc->firstSeq == 5 && inWindow && !row(kQueued) && P.rc == 0, F("rows=%zu", rows.size()).c_str());

        ResetThread(g_mainTid, Src::Description);
        Linie::StartRecording(&ThrowingReader);
        Caller& P2 = Pipe(kObj2, kQueued);
        WaitDepth(1);
        bool escaped = false;
        try { Fire(kObj, kFa); } catch (...) { escaped = true; }
        const bool contained = !escaped && CountLog('E', "") == 1
            && HasLog('E', "GameThreadDispatch: UNCAUGHT exception (reader boom) — contained. This would previously have "
                           "terminated the game (0xC0000409).")
            && IndexOf(Recs(), 'P', kFa, g_mainTid) >= 0 && Stark::GetHookFireCount() == 1
            && Stark::s_lastHookFireMs.load() != 0 && Stark::s_queueDepth.load() == 1
            && IndexOf(Recs(), 'P', kQueued, g_mainTid) < 0;
        Linie::StopRecording();
        Linie::Reset();
        Fire(kObj, kPlain);
        P2.Wait(2000);
        const auto r = Recs();
        const int gq = IndexOf(r, 'P', kQueued, g_mainTid), gp = IndexOf(r, 'P', kPlain, g_mainTid);
        check("THREAD guard ⭐: a throw in our work is contained and logged, and the game's own call still runs",
              contained, FirstLog('E', "").c_str());
        check("THREAD guard: the work it cut short runs on the next ProcessEvent", gq >= 0 && gp > gq && P2.rc == 0);
    }

    {   blk("THREAD WORKER SKIP - a worker thread's ProcessEvent leaves queued work for the game thread, and says so once");
        ResetThread(g_mainTid, Src::Description);
        Caller& P = Pipe(kObj2, kQueued);
        WaitDepth(1);
        DWORD wtid = 0;
        RunOn([&] {
            wtid = GetCurrentThreadId();
            Fire(kObj, kPlain, 3);
            (void)Stark::CallOriginalSEH(kObj, kReenter, kParams);
        });
        const bool skipped = Stark::s_workerSkipCount.load() == 3 && CountLog('I', "GameThreadDispatch: a ProcessEvent on "
            "thread ") == 1 && HasLog('I', F("GameThreadDispatch: a ProcessEvent on thread %lu (not the game thread %lu) "
            "left 1 queued request(s) for the game thread", (unsigned long)wtid, (unsigned long)g_mainTid))
            && IndexOf(Recs(), 'P', kQueued, wtid) < 0 && Stark::s_queueDepth.load() == 1;
        Fire(kObj, kPlain);
        P.Wait(2000);
        Stark::ApplyGameThreadValidation(Stark::GetGameThreadFireCount(), Stark::GetOtherThreadFireCount());
        check("THREAD worker skip ⭐: three worker entries left the request, one line said so; our own call is not one",
              skipped, F("skips=%llu", (unsigned long long)Stark::s_workerSkipCount.load()).c_str());
        check("THREAD worker skip: the game thread drains it, and the validation counts the skips", P.rc == 0
              && IndexOf(Recs(), 'P', kQueued, g_mainTid) >= 0
              && HasLog('I', F("GameThreadDispatch: game thread %lu (named GameThread by the engine) kept: 1 hook fires on "
                               "it in the window, 3 on other threads (our own calls not counted); worker-thread entries "
                               "that left queued work so far: 3", (unsigned long)g_mainTid)));
    }

    {   blk("THREAD PICK - the game thread is the one named GameThread, else the main thread; tried once");
        using FnSetDesc = HRESULT(WINAPI*)(HANDLE, PCWSTR);
        const auto setDesc = reinterpret_cast<FnSetDesc>(GetProcAddress(GetModuleHandleW(L"kernel32.dll"),
                                                                        "SetThreadDescription"));
        const std::string walkedTail = " threads walked); queued invokes drain on it only";
        auto named = [&](const wchar_t* desc, std::function<void(DWORD)> body) {
            HANDLE ready = CreateEventW(nullptr, TRUE, FALSE, nullptr), done = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            DWORD gtid = 0;
            std::thread g([&] {
                gtid = GetCurrentThreadId();
                setDesc(GetCurrentThread(), desc);
                SetEvent(ready);
                WaitForSingleObject(done, 10000);
            });
            WaitForSingleObject(ready, 5000);
            body(gtid);
            SetEvent(done);
            g.join();
            CloseHandle(ready);
            CloseHandle(done);
        };
        if (!setDesc) {
            printf("  SKIP  THREAD pick by name: SetThreadDescription is not available here\n");
        } else {
            ResetThread(0, Src::Unknown);
            Stark::s_gameThreadTried.store(false);
            named(L"GameThread", [&](DWORD gtid) {
                Stark::IdentifyGameThread();
                const std::string line = FirstLog('I', "GameThreadDispatch: game thread ");
                check("THREAD pick ⭐: the thread the engine named GameThread", Stark::GameThreadId() == gtid
                      && Stark::GameThreadSource() == Src::Description && Stark::s_gameThreadTried.load()
                      && line.rfind(F("GameThreadDispatch: game thread %lu, named GameThread by the engine (",
                                      (unsigned long)gtid), 0) == 0 && Tail(line, walkedTail.size()) == walkedTail,
                      line.c_str());
            });
            ResetThread(0, Src::Unknown);
            Stark::s_gameThreadTried.store(false);
            named(L"GameThreadPool", [&](DWORD) {
                Stark::IdentifyGameThread();
                check("THREAD pick: only the exact name counts -- GameThreadPool is not it", Stark::GameThreadId() == g_mainTid
                      && Stark::GameThreadSource() == Src::MainThread);
            });
        }
        ResetThread(0, Src::Unknown);
        Stark::s_gameThreadTried.store(false);
        Stark::IdentifyGameThread();
        const std::string line = FirstLog('I', "GameThreadDispatch: game thread ");
        check("THREAD pick: with no thread named GameThread, the process's first thread", Stark::GameThreadId() == g_mainTid
              && Stark::GameThreadSource() == Src::MainThread && line.rfind(F("GameThreadDispatch: game thread %lu, the "
              "main thread (no thread is named GameThread) (", (unsigned long)g_mainTid), 0) == 0
              && Tail(line, walkedTail.size()) == walkedTail, line.c_str());
        Stark::s_gameThreadId.store(0);
        { std::lock_guard<std::mutex> lk(g_logMu); g_log.clear(); }
        Stark::IdentifyGameThread();
        check("THREAD pick: once tried, never again -- a pick given up stays given up", Stark::GameThreadId() == 0
              && CountLog('I', "GameThreadDispatch: game thread") == 0);

        ResetThread(0, Src::Unknown);
        Stark::s_gameThreadTried.store(false);
        g_failSnapshot.store(true);
        Stark::IdentifyGameThread();
        g_failSnapshot.store(false);
        check("THREAD pick: no thread snapshot leaves the drain on every thread, and says why", CountLog('W', "") == 1
              && HasLog('W', "GameThreadDispatch: no thread snapshot (err 5) -- the drain stays on every thread, as before")
              && CountLog('I', "GameThreadDispatch: game thread") == 0 && Stark::GameThreadId() == 0
              && Stark::GameThreadSource() == Src::Unknown && Stark::s_gameThreadTried.load(), FirstLog('W', "").c_str());
    }

    {   blk("THREAD VALIDATION - a named pick is kept; a main-thread guess that never fires while others do is given up");
        const std::string giveUp = "GameThreadDispatch: the main-thread guess 1234 made no ProcessEvent call in the window "
            "while other threads made %llu: it is not this game's game thread, or it was held up for the whole window. The "
            "drain gate is given up for this session: queued invokes run on whichever thread enters ProcessEvent, as "
            "before [STARK-DRAIN-OFF-GAME-THREAD]";
        ResetThread(0, Src::Unknown);
        Stark::ApplyGameThreadValidation(0, 9);
        bool emptyLog;
        { std::lock_guard<std::mutex> lk(g_logMu); emptyLog = g_log.empty(); }
        check("THREAD validation: nothing picked, nothing to validate", emptyLog && Stark::GameThreadId() == 0
              && Stark::GameThreadSource() == Src::Unknown);

        ResetThread(kNoThread, Src::Description);
        Stark::s_workerSkipCount.store(7);
        Stark::ApplyGameThreadValidation(0, 9);
        check("THREAD validation ⭐: a thread the engine named is kept even when it never fired", Stark::GameThreadId() == kNoThread
              && Stark::GameThreadSource() == Src::Description && CountLog('W', "") == 0
              && HasLog('I', "GameThreadDispatch: game thread 1234 (named GameThread by the engine) kept: 0 hook fires on it "
                             "in the window, 9 on other threads (our own calls not counted); worker-thread entries that left "
                             "queued work so far: 7"));

        ResetThread(kNoThread, Src::MainThread);
        Stark::ApplyGameThreadValidation(0, 5);
        check("THREAD validation ⭐: a main-thread guess silent while others fire is given up, and the log says so",
              Stark::GameThreadId() == 0 && Stark::GameThreadSource() == Src::Unknown && CountLog('I', "") == 0
              && HasLog('W', F(giveUp.c_str(), 5ull)), FirstLog('W', "").c_str());

        ResetThread(kNoThread, Src::MainThread);
        Stark::ApplyGameThreadValidation(3, 5);
        const bool kept35 = Stark::GameThreadId() == kNoThread && HasLog('I', "GameThreadDispatch: game thread 1234 (the main "
            "thread (no thread is named GameThread)) kept: 3 hook fires on it in the window, 5 on other threads (our own "
            "calls not counted); worker-thread entries that left queued work so far: 0");
        ResetThread(kNoThread, Src::MainThread);
        Stark::ApplyGameThreadValidation(0, 0);
        check("THREAD validation: a guess that fired, or a window where nothing did, is kept", kept35
              && Stark::GameThreadId() == kNoThread && Stark::GameThreadSource() == Src::MainThread && CountLog('W', "") == 0
              && HasLog('I', "GameThreadDispatch: game thread 1234 (the main thread (no thread is named GameThread)) kept: 0 "
                             "hook fires on it in the window, 0 on other threads (our own calls not counted); worker-thread "
                             "entries that left queued work so far: 0"));

        ResetThread(kNoThread, Src::MainThread);
        RunOn([] {
            Fire(kObj, kPlain, 5);
            (void)Stark::CallOriginalSEH(kObj, kReenter, kParams);
            (void)Stark::CallOriginalSEH(kObj, kReenter, kParams);
        });
        const bool counted = Stark::GetGameThreadFireCount() == 0 && Stark::GetOtherThreadFireCount() == 5
            && Stark::GetHookFireCount() == 7;
        Stark::ApplyGameThreadValidation(Stark::GetGameThreadFireCount(), Stark::GetOtherThreadFireCount());
        const bool gaveUp = Stark::GameThreadId() == 0 && HasLog('W', F(giveUp.c_str(), 5ull));
        Stark::s_lastHookFireMs.store(Stark::NowMs() - 5000);
        const uint64_t t0 = Stark::NowMs();
        RunOn([] { Fire(kObj, kPlain); });
        const uint64_t t1 = Stark::NowMs();
        const uint64_t st = Stark::s_lastHookFireMs.load();
        check("THREAD validation ⭐: the counters feed it -- given up, and after that any thread's fire stamps again",
              counted && gaveUp && st >= t0 && st <= t1);

        ResetThread(kNoThread, Src::MainThread);
        RunOn([] { for (int i = 0; i < 3; ++i) (void)Stark::CallOriginalSEH(kObj, kReenter, kParams); });
        Stark::ApplyGameThreadValidation(Stark::GetGameThreadFireCount(), Stark::GetOtherThreadFireCount());
        check("THREAD validation: our own calls alone never give a guess up", Stark::GetHookFireCount() == 3
              && Stark::GetOtherThreadFireCount() == 0 && Stark::GameThreadId() == kNoThread
              && HasLog('I', "GameThreadDispatch: game thread 1234 (the main thread (no thread is named GameThread)) kept: 0 "
                             "hook fires on it in the window, 0 on other threads (our own calls not counted); worker-thread "
                             "entries that left queued work so far: 0"));
    }
}

// The hook: real MinHook hooks on code pages of the test's own making, and a main image it builds, found through the
// GetModuleHandleW seam. Each check starts from MinHook uninitialised -- MH_Uninitialize restores every enabled target
// and frees every block -- and its pages are freed only after that.
static const uint8_t kSafePE[8] = { 0x41, 0xC7, 0x00, 0x78, 0x56, 0x34, 0x12, 0xC3 };   // mov dword [r8], 0x12345678; ret
static const uint8_t kRetRet[8] = { 0xC3, 0xC3, 0xC3, 0xC3, 0xC3, 0xC3, 0xC3, 0xC3 };   // ret, then no padding
static const uint8_t kUnsafe[8] = { 0x48, 0x8B, 0x05, 0x00, 0x00, 0x00, 0x50, 0xC3 };   // a RIP-relative load farBlock away
static const uint8_t kHdeError[8] = { 0x48, 0x40, 0x90, 0x90, 0x90, 0x90, 0x90, 0xC3 }; // REX after REX: hde64 refuses it
constexpr DWORD kExecChars = IMAGE_SCN_CNT_CODE | IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ;

static std::vector<void*> g_fixtures;
static void FreeFixtures() {
    for (void* p : g_fixtures) VirtualFree(p, 0, MEM_RELEASE);
    g_fixtures.clear();
}
static void HookReset() {
    ResetStark();
    MH_Uninitialize();
    FreeFixtures();
    Stark::s_mhInitialized.store(false);
    Stark::s_holdTried = false;
    Stark::s_heldTarget = 0;
    Stark::s_heldBlock = 0;
    Stark::s_hookActive.store(false);
    Stark::s_hookedAddr = 0;
    Stark::s_originalPE = nullptr;
    Stark::s_hookFireCount.store(0);
    Stark::s_lastHookFireMs.store(0);
    Stark::s_gameThreadId.store(0);
    Stark::s_gameThreadTried.store(true);
    g_fakeMainImage = 0;
    g_vqMode = VqMode::Real;
    g_tick.store(g_tick.load() + 1000000);   // past the window log's throttle, whatever the last check left in it
    g_tickStep = 0;
    { std::lock_guard<std::mutex> lk(g_logMu); g_log.clear(); }
}
static uintptr_t CodePage(const uint8_t (&code)[8]) {
    auto* p = static_cast<uint8_t*>(VirtualAlloc(nullptr, 0x10000, MEM_RESERVE, PAGE_NOACCESS));
    VirtualAlloc(p, 0x1000, MEM_COMMIT, PAGE_READWRITE);
    memset(p, 0xCC, 0x1000);
    memcpy(p, code, sizeof code);
    DWORD old = 0;
    VirtualProtect(p, 0x1000, PAGE_EXECUTE_READ, &old);
    FlushInstructionCache(GetCurrentProcess(), p, 0x1000);
    g_fixtures.push_back(p);
    return reinterpret_cast<uintptr_t>(p);
}
// Calls a target as the game calls ProcessEvent; kSafePE writes 0x12345678 through its third argument.
static uint32_t CallTarget(uintptr_t t) {
    using Pe = void (*)(void*, void*, void*);
    int o = 0, f = 0;
    uint32_t v = 0;
    reinterpret_cast<Pe>(t)(&o, &f, &v);
    return v;
}
static std::string Hex(uint64_t v) { return F("0x%llX", static_cast<unsigned long long>(v)); }
static MEMORY_BASIC_INFORMATION RealQuery(uintptr_t a) {
    MEMORY_BASIC_INFORMATION m{};
    ::VirtualQuery(reinterpret_cast<LPCVOID>(a), &m, sizeof m);
    return m;
}
static int IndexOfLog(char lvl, const std::string& exact) {
    std::lock_guard<std::mutex> lk(g_logMu);
    for (size_t i = 0; i < g_log.size(); ++i) if (g_log[i].lvl == lvl && g_log[i].text == exact) return static_cast<int>(i);
    return -1;
}
static int CountContaining(const std::string& part) {
    std::lock_guard<std::mutex> lk(g_logMu);
    int n = 0;
    for (const auto& l : g_log) if (l.text.find(part) != std::string::npos) ++n;
    return n;
}
static size_t LogSize() { std::lock_guard<std::mutex> lk(g_logMu); return g_log.size(); }

// A main image: a PE header of the test's writing at I, and below it, at H = I - 0x10000, the free granule MinHook's
// first block for a target in the image goes to. Page 0 read-only, pages 1-2 execute-read, the rest no access, unless
// the spec says otherwise.
struct Sec { uint32_t rva, vsize, chars; };
struct ImgSpec {
    uint32_t entryRva = 0x1000, sizeOfImage = 0x10000;
    bool dynBase = true;
    std::vector<Sec> secs{ { 0x1000, 0x2000, kExecChars } };
    DWORD prot[16] = {};
};
static uintptr_t MakeImageAt(uintptr_t I, const ImgSpec& spec, const std::function<void(uint8_t*)>& code) {
    {
        auto* b = static_cast<uint8_t*>(VirtualAlloc(reinterpret_cast<void*>(I), 0x10000, MEM_RESERVE | MEM_COMMIT,
                                                     PAGE_READWRITE));
        if (reinterpret_cast<uintptr_t>(b) != I) { if (b) VirtualFree(b, 0, MEM_RELEASE); return 0; }
        memset(b, 0xCC, 0x10000);
        memset(b, 0, 0x1000);
        auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(b);
        dos->e_magic = IMAGE_DOS_SIGNATURE;
        dos->e_lfanew = 0x40;
        auto* nt = reinterpret_cast<IMAGE_NT_HEADERS64*>(b + 0x40);
        nt->Signature = IMAGE_NT_SIGNATURE;
        nt->FileHeader.Machine = IMAGE_FILE_MACHINE_AMD64;
        nt->FileHeader.NumberOfSections = static_cast<WORD>(spec.secs.size());
        nt->FileHeader.SizeOfOptionalHeader = sizeof(IMAGE_OPTIONAL_HEADER64);
        nt->OptionalHeader.Magic = IMAGE_NT_OPTIONAL_HDR64_MAGIC;
        nt->OptionalHeader.AddressOfEntryPoint = spec.entryRva;
        nt->OptionalHeader.SizeOfImage = spec.sizeOfImage;
        nt->OptionalHeader.DllCharacteristics = spec.dynBase ? IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE : 0;
        IMAGE_SECTION_HEADER* sec = IMAGE_FIRST_SECTION(nt);
        for (size_t i = 0; i < spec.secs.size(); ++i) {
            memcpy(sec[i].Name, ".text", 5);
            sec[i].VirtualAddress = spec.secs[i].rva;
            sec[i].Misc.VirtualSize = spec.secs[i].vsize;
            sec[i].Characteristics = spec.secs[i].chars;
        }
        code(b);
        for (int pg = 0; pg < 16; ++pg) {
            const DWORD pr = spec.prot[pg] ? spec.prot[pg]
                : (pg == 0 ? PAGE_READONLY : pg <= 2 ? PAGE_EXECUTE_READ : PAGE_NOACCESS);
            DWORD old = 0;
            VirtualProtect(b + pg * 0x1000, 0x1000, pr, &old);
        }
        FlushInstructionCache(GetCurrentProcess(), b, 0x10000);
        g_fixtures.push_back(b);
        g_fakeMainImage = I;
        return I;
    }
}
// MinHook's heap first: an MH_Initialize after the hole is open can place its heap in it. Stark's own flag stays
// unset, so its EnsureMinHookLocked still runs and meets MH_ERROR_ALREADY_INITIALIZED.
static uintptr_t MakeImage(const ImgSpec& spec, const std::function<void(uint8_t*)>& code) {
    (void)MH_Initialize();
    for (int attempt = 0; attempt < 8; ++attempt) {
        void* probe = VirtualAlloc(nullptr, 0x40000, MEM_RESERVE, PAGE_NOACCESS);
        if (!probe) continue;
        VirtualFree(probe, 0, MEM_RELEASE);
        const uintptr_t I = reinterpret_cast<uintptr_t>(probe) + 0x20000;
        if (RealQuery(I - 0x10000).State != MEM_FREE) continue;
        if (MakeImageAt(I, spec, code)) return I;
    }
    return 0;
}

// Address-space fixtures for MinHook's +-1 GB window. Each probes for a free span, then takes exact pieces of it; a
// piece another allocation took meanwhile drops the attempt and tries again.
static bool ReserveAt(uintptr_t a, size_t n) {
    void* r = VirtualAlloc(reinterpret_cast<void*>(a), n, MEM_RESERVE, PAGE_NOACCESS);
    if (reinterpret_cast<uintptr_t>(r) != a) { if (r) VirtualFree(r, 0, MEM_RELEASE); return false; }
    g_fixtures.push_back(r);
    return true;
}
static uintptr_t ProbeSpan(size_t n) {
    void* r = VirtualAlloc(nullptr, n, MEM_RESERVE, PAGE_NOACCESS);
    if (!r) return 0;
    VirtualFree(r, 0, MEM_RELEASE);
    return reinterpret_cast<uintptr_t>(r);
}
static void DropFrom(size_t mark) {
    while (g_fixtures.size() > mark) { VirtualFree(g_fixtures.back(), 0, MEM_RELEASE); g_fixtures.pop_back(); }
}
// Every block start within +-1 GB of T taken: [T-1GB, T) and [T, T+1GB) reserved, T's first page the safe
// target, and another reservation right above.
static uintptr_t Starve(uintptr_t& P) {
    for (int attempt = 0; attempt < 8; ++attempt) {
        const size_t mark = g_fixtures.size();
        P = ProbeSpan(0x80020000);
        if (!P) continue;
        const uintptr_t T = P + 0x40010000;
        if (!ReserveAt(P, 0x40010000) || !ReserveAt(T, 0x40000000) || !ReserveAt(T + 0x40000000, 0x10000)) {
            DropFrom(mark);
            continue;
        }
        auto* t = static_cast<uint8_t*>(VirtualAlloc(reinterpret_cast<void*>(T), 0x1000, MEM_COMMIT, PAGE_READWRITE));
        memset(t, 0xCC, 0x1000);
        memcpy(t, kSafePE, sizeof kSafePE);
        DWORD old = 0;
        VirtualProtect(t, 0x1000, PAGE_EXECUTE_READ, &old);
        FlushInstructionCache(GetCurrentProcess(), t, 0x1000);
        return T;
    }
    return 0;
}
// A window with one free granule each side of T: [T-1GB, T-0x20000) reserved, T's own 0x10000, the rest free.
static uintptr_t LivePick(uintptr_t& P) {
    for (int attempt = 0; attempt < 8; ++attempt) {
        const size_t mark = g_fixtures.size();
        P = ProbeSpan(0x80030000);
        if (!P) continue;
        const uintptr_t T = P + 0x40010000;
        if (!ReserveAt(P, 0x3FFF0000) || !ReserveAt(T, 0x10000)) { DropFrom(mark); continue; }
        VirtualAlloc(reinterpret_cast<void*>(T), 0x1000, MEM_COMMIT, PAGE_READWRITE);
        return T;
    }
    return 0;
}
// An image at I whose window's one free granule is H = I - 0x10000, the holder's block; with `high`, everything
// above the image is taken too, so once the holder has H no fresh block fits anywhere.
static uintptr_t HeldWindow(bool high) {
    (void)MH_Initialize();
    for (int attempt = 0; attempt < 8; ++attempt) {
        const size_t mark = g_fixtures.size();
        const uintptr_t P = ProbeSpan(0x80040000);
        if (!P) continue;
        const uintptr_t I = P + 0x40020000;
        if (!ReserveAt(P, 0x40010000)
            || !MakeImageAt(I, ImgSpec{}, [](uint8_t* b) { memcpy(b + 0x1000, kSafePE, 8); memcpy(b + 0x2000, kSafePE, 8); })
            || (high && !ReserveAt(I + 0x10000, 0x40010000))) {
            DropFrom(mark);
            continue;
        }
        return I;
    }
    return 0;
}
static std::string HeldLine(uintptr_t H, uintptr_t target, const char* kind, const char* verdict, uint64_t codeLo,
                            uint64_t codeHi, uintptr_t I, uint32_t size, bool dyn) {
    SYSTEM_INFO si{};
    GetSystemInfo(&si);
    const Lager::Span s = Lager::OriginsServedBy(H, reinterpret_cast<uint64_t>(si.lpMinimumApplicationAddress),
                                                 reinterpret_cast<uint64_t>(si.lpMaximumApplicationAddress));
    return F("GameThreadDispatch: held a MinHook trampoline block for the process lifetime: block 0x%llX, taken by a hook "
             "created (never enabled) on 0x%llX, %s; it serves hook targets [0x%llX, 0x%llX], %s the image's executable "
             "range [0x%llX, 0x%llX) (image 0x%llX + 0x%X, DYNAMICBASE %s)", (unsigned long long)H,
             (unsigned long long)target, kind, (unsigned long long)s.lo, (unsigned long long)s.hi, verdict,
             (unsigned long long)codeLo, (unsigned long long)codeHi, (unsigned long long)I, size, dyn ? "yes" : "no");
}
static const char* kNoBlock = "GameThreadDispatch: no MinHook trampoline block held (%s) -- nothing else changes: a hook "
                              "allocates its own block as before";
static const char* kInstalled = "GameThreadDispatch: ProcessEvent hook installed at 0x%llX (trampoline pre-flight: 1 "
                                "instruction(s) copied, Zydis agrees)";

// Faults a vectored handler sees inside [lo, hi): it only counts, and lets the exception go on.
static std::atomic<uintptr_t> g_watchLo{ 0 }, g_watchHi{ 0 };
static std::atomic<int> g_watchFaults{ 0 };
static LONG CALLBACK WatchFaults(EXCEPTION_POINTERS* ep) {
    const DWORD code = ep->ExceptionRecord->ExceptionCode;
    if ((code == EXCEPTION_ACCESS_VIOLATION || code == STATUS_GUARD_PAGE_VIOLATION)
        && ep->ExceptionRecord->NumberParameters >= 2) {
        const uintptr_t a = static_cast<uintptr_t>(ep->ExceptionRecord->ExceptionInformation[1]);
        if (a >= g_watchLo.load() && a < g_watchHi.load()) g_watchFaults.fetch_add(1);
    }
    return EXCEPTION_CONTINUE_SEARCH;
}
static Lager::ImageLayout g_layout;
static void ReadLayoutIntoGlobal() { g_layout = Stark::ReadMainImageLayout(); }
static bool RanWithoutEscapingSEH(void (*fn)()) {
    __try { fn(); } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    return true;
}

static void HookBlocks() {
    {   blk("HOOK INSTALL - ProcessEvent hooked for real, vetted first, re-enabled after a soft removal");
        HookReset();
        const uintptr_t T = CodePage(kSafePE);
        const Falsch::Report pre = Falsch::VetHookTarget(T);
        check("HOOK fixture: the safe target is Safe, one instruction copied", pre.verdict == Falsch::Verdict::Safe
              && pre.copied == 1);
        const bool ok = Stark::InstallHook(T);
        const uintptr_t tr = reinterpret_cast<uintptr_t>(Stark::s_originalPE);
        const uintptr_t blkBase = reinterpret_cast<uintptr_t>(RealQuery(tr).AllocationBase);
        check("HOOK install ⭐: installed and enabled -- the target patched, the hook active, the trampoline's block named",
              ok && Stark::IsHookActive() && Stark::s_hookedAddr == T && *reinterpret_cast<const uint8_t*>(T) == 0xE9
              && HasLog('I', F(kInstalled, (unsigned long long)T)) && blkBase == (tr & ~static_cast<uintptr_t>(0xFFF))
              && HasLog('I', F("GameThreadDispatch: ProcessEvent's trampoline 0x%llX is in block 0x%llX (no block was "
                               "held)", (unsigned long long)tr, (unsigned long long)blkBase)), FirstLog('I', "").c_str());
        const uint32_t v = CallTarget(T);
        check("HOOK install: a call goes through the detour and on to the original", v == 0x12345678
              && Stark::GetHookFireCount() == 1);
        const uintptr_t T2 = CodePage(kSafePE);
        const bool ok2 = Stark::InstallHook(T2);
        check("HOOK install: a second install while one is active patches nothing", ok2
              && HasLog('W', "GameThreadDispatch: hook already active") && memcmp(reinterpret_cast<const void*>(T2), kSafePE, 8) == 0
              && Stark::s_hookedAddr == T);

        Stark::RemoveHook();
        const bool inactive = !Stark::IsHookActive();
        const size_t afterFirst = LogSize();
        const uint32_t v2 = CallTarget(T);
        Stark::RemoveHook();
        const bool secondSilent = LogSize() == afterFirst;
        const auto pe = Stark::s_originalPE;
        const bool re = Stark::InstallHook(T);
        check("HOOK soft removal ⭐: the flag cleared, the patch kept and still forwarding; re-enabled without MinHook",
              inactive && CountLog('I', "GameThreadDispatch: hook flag cleared (physical hook retained to avoid in-flight "
              "unhook race)") == 1 && *reinterpret_cast<const uint8_t*>(T) == 0xE9 && v2 == 0x12345678
              && Stark::GetHookFireCount() == 2 && secondSilent && re
              && HasLog('I', F("GameThreadDispatch: re-enabled existing hook at 0x%llX", (unsigned long long)T))
              && CountLog('I', "GameThreadDispatch: ProcessEvent hook installed") == 1 && Stark::s_originalPE == pe
              && Stark::IsHookActive());
    }

    {   blk("HOOK REFUSALS - a null address, an unsafe target, MinHook's own refusal; one install at a time");
        HookReset();
        std::atomic<int> r{ -1 };
        bool silent = true;
        {
            std::unique_lock<std::mutex> hold(Stark::s_mhMutex);
            std::thread t([&] { r.store(Stark::InstallHook(0) ? 1 : 0); });
            const uint64_t until = Stark::NowMs() + 500;
            while (Stark::NowMs() < until) { if (LogSize() != 0 || r.load() != -1) silent = false; Sleep(5); }
            hold.unlock();
            t.join();
        }
        check("HOOK lock: an install waits for MinHook's lock; then a null address is refused", silent && r.load() == 0
              && HasLog('E', "GameThreadDispatch: null processEventAddr"));

        HookReset();
        const uintptr_t U = CodePage(kUnsafe);
        const Falsch::Report rep = Falsch::VetHookTarget(U);
        const bool okU = Stark::InstallHook(U);
        check("HOOK unsafe ⭐: the pre-flight refuses it before MinHook reads it -- nothing created or patched",
              rep.verdict == Falsch::Verdict::Unsafe && !okU && HasLog('E', F("GameThreadDispatch: ProcessEvent hook NOT "
              "installed at 0x%llX -- the trampoline pre-flight refused it at +%d: %s", (unsigned long long)U, rep.offset,
              rep.why.c_str())) && memcmp(reinterpret_cast<const void*>(U), kUnsafe, 8) == 0
              && Stark::s_originalPE == nullptr && !Stark::IsHookActive() && CountContaining("MH_CreateHook failed") == 0,
              FirstLog('E', "").c_str());

        HookReset();
        const uintptr_t Hd = CodePage(kHdeError);
        const Falsch::Report hr = Falsch::VetHookTarget(Hd);
        const bool okH = Stark::InstallHook(Hd);
        check("HOOK MinHook refuses: warned, MinHook's own failure reported, and no window walk for it",
              hr.verdict == Falsch::Verdict::MinHookRefuses && !okH && HasLog('W', F("GameThreadDispatch: ProcessEvent "
              "0x%llX: %s", (unsigned long long)Hd, hr.why.c_str()))
              && HasLog('E', "GameThreadDispatch: MH_CreateHook failed: MH_ERROR_UNSUPPORTED_FUNCTION")
              && CountContaining("MinHook block window") == 0);

        HookReset();
        const uintptr_t T = CodePage(kSafePE);
        SYSTEM_INFO si{};
        GetSystemInfo(&si);
        const uintptr_t farBlock = (T + 0x100000000ull < reinterpret_cast<uintptr_t>(si.lpMaximumApplicationAddress))
            ? T + 0x100000000ull : T - 0x100000000ull;
        Stark::s_heldBlock = farBlock;
        const bool okR = Stark::InstallHook(T);
        const std::string reach = F("GameThreadDispatch: the held block 0x%llX is out of reach of ProcessEvent 0x%llX "
                                    "(MinHook reuses a block only within +-1 GB) -- it must find a block of its own",
                                    (unsigned long long)farBlock, (unsigned long long)T);
        const int iReach = IndexOfLog('W', reach), iInst = IndexOfLog('I', F(kInstalled, (unsigned long long)T));
        check("HOOK reach: a held block out of ProcessEvent's reach is said so before the attempt, and not used", okR
              && iReach >= 0 && iInst > iReach && HasLog('I', F("GameThreadDispatch: ProcessEvent's trampoline 0x%llX is "
              "NOT in the held block 0x%llX", (unsigned long long)reinterpret_cast<uintptr_t>(Stark::s_originalPE),
              (unsigned long long)farBlock)) && CountContaining("NEEDED") == 0 && CountContaining("was reused") == 0);
    }

    {   blk("HOOK HOLD - a trampoline block held at start, on a hook created and never enabled");
        SYSTEM_INFO si{};
        GetSystemInfo(&si);
        const auto safeAt = [](uint32_t rva) { return [rva](uint8_t* b) { memcpy(b + rva, kSafePE, 8); }; };

        HookReset();
        const uintptr_t I = MakeImage(ImgSpec{}, [](uint8_t* b) { memcpy(b + 0x1000, kSafePE, 8); memcpy(b + 0x2000, kSafePE, 8); });
        const uintptr_t e = I + 0x1000, H = I - 0x10000;
        Stark::HoldTrampolineBlock();
        const size_t afterHold = LogSize();
        Stark::HoldTrampolineBlock();
        const std::string held = HeldLine(H, e, "the image's entry point", "which covers", I + 0x1000, I + 0x3000, I,
                                          0x10000, true);
        check("HOOK hold ⭐: a block held for the process, on the image's entry point, covering its code", I != 0
              && Stark::s_heldTarget == e && Stark::s_heldBlock == H && CountLog('I', "GameThreadDispatch: held a MinHook")
              == 1 && HasLog('I', held), FirstLog('I', "GameThreadDispatch: held").c_str());
        const uint32_t ve = CallTarget(e);
        check("HOOK hold ⭐: the holder is never enabled -- the entry point untouched, MinHook says disabled", memcmp(
              reinterpret_cast<const void*>(e), kSafePE, 8) == 0 && MH_DisableHook(reinterpret_cast<LPVOID>(e)) ==
              MH_ERROR_DISABLED && MH_CreateHook(reinterpret_cast<LPVOID>(e), reinterpret_cast<LPVOID>(&Stark::HolderDetour),
              nullptr) == MH_ERROR_ALREADY_CREATED && ve == 0x12345678 && Stark::GetHookFireCount() == 0);
        check("HOOK hold: once per process -- a second call does nothing", LogSize() == afterHold && Stark::s_heldBlock == H);

        HookReset();
        const MH_STATUS pre = MH_Initialize();
        const uintptr_t I2 = MakeImage(ImgSpec{}, safeAt(0x1000));
        Stark::HoldTrampolineBlock();
        check("HOOK hold: MinHook already initialised is no failure", pre == MH_OK && HasLog('I', HeldLine(I2 - 0x10000,
              I2 + 0x1000, "the image's entry point", "which covers", I2 + 0x1000, I2 + 0x3000, I2, 0x10000, true))
              && CountContaining("MH_Initialize failed") == 0 && CountContaining("MinHook did not initialise") == 0
              && Stark::s_mhInitialized.load());

        HookReset();
        const uintptr_t I2f = MakeImage(ImgSpec{}, safeAt(0x1000));
        const MH_STATUS down = MH_Uninitialize();   // the hold is MinHook's first caller, as at proxy start
        Stark::HoldTrampolineBlock();
        check("HOOK hold first caller: the hold initialises MinHook itself, then holds", I2f != 0 && down == MH_OK
              && Stark::s_heldTarget == I2f + 0x1000 && Stark::s_heldBlock != 0
              && CountLog('I', "GameThreadDispatch: held a MinHook") == 1 && Stark::s_mhInitialized.load()
              && MH_CreateHook(reinterpret_cast<LPVOID>(I2f + 0x1000), reinterpret_cast<LPVOID>(&Stark::HolderDetour),
                               nullptr) == MH_ERROR_ALREADY_CREATED,
              FirstLog('W', "GameThreadDispatch: no MinHook").c_str());

        HookReset();
        ImgSpec none;
        none.entryRva = 0;
        none.secs.clear();
        MakeImage(none, [](uint8_t*) {});
        bool silent = true;
        {
            std::unique_lock<std::mutex> hold(Stark::s_mhMutex);
            std::thread t([] { Stark::HoldTrampolineBlock(); });
            const uint64_t until = Stark::NowMs() + 500;
            while (Stark::NowMs() < until) { if (LogSize() != 0) silent = false; Sleep(5); }
            hold.unlock();
            t.join();
        }
        check("HOOK hold: it waits for MinHook's lock; headers that name nothing hold nothing, and say so", silent
              && HasLog('W', F(kNoBlock, "the main image's headers named no candidate")));

        HookReset();
        ImgSpec fb;
        fb.entryRva = 0x1010;
        const uintptr_t I3 = MakeImage(fb, [](uint8_t* b) { memcpy(b + 0x1000, kSafePE, 8); memcpy(b + 0x1010, kRetRet, 8); });
        Stark::HoldTrampolineBlock();
        check("HOOK hold fallback ⭐: an entry point MinHook cannot take falls back to a section's start",
              Stark::s_heldTarget == I3 + 0x1000 && Stark::s_heldBlock == I3 - 0x10000
              && HasLog('I', HeldLine(I3 - 0x10000, I3 + 0x1000, "the start of an executable section", "which covers",
                                      I3 + 0x1000, I3 + 0x3000, I3, 0x10000, true))
              && MH_DisableHook(reinterpret_cast<LPVOID>(I3 + 0x1010)) == MH_ERROR_NOT_CREATED,
              FirstLog('I', "GameThreadDispatch: held").c_str());

        HookReset();
        ImgSpec all;
        all.entryRva = 0x1010;
        all.secs = { { 0x1000, 0x1000, kExecChars }, { 0x2000, 0x1000, kExecChars }, { 0x3000, 0x1000, kExecChars } };
        const uintptr_t I4 = MakeImage(all, [](uint8_t* b) {
            memcpy(b + 0x1000, kSafePE, 8); memcpy(b + 0x1010, kRetRet, 8); memcpy(b + 0x2000, kRetRet, 8);
        });
        Stark::s_hookedAddr = I4 + 0x1000;
        Stark::HoldTrampolineBlock();
        const std::string refused = F("0x%llX: MH_ERROR_UNSUPPORTED_FUNCTION; 0x%llX: a real hook's target; 0x%llX: "
            "MH_ERROR_UNSUPPORTED_FUNCTION; 0x%llX: not readable code; ", (unsigned long long)(I4 + 0x1010),
            (unsigned long long)(I4 + 0x1000), (unsigned long long)(I4 + 0x2000), (unsigned long long)(I4 + 0x3000));
        bool noneCreated = true;
        for (uintptr_t a : { I4 + 0x1010, I4 + 0x1000, I4 + 0x2000, I4 + 0x3000 })
            noneCreated = noneCreated && MH_DisableHook(reinterpret_cast<LPVOID>(a)) == MH_ERROR_NOT_CREATED;
        check("HOOK hold refused ⭐: every candidate refused, each with its reason, and nothing held", Stark::s_heldTarget == 0
              && Stark::s_heldBlock == 0 && HasLog('W', F(kNoBlock, refused.c_str())) && noneCreated,
              FirstLog('W', "").c_str());

        HookReset();
        ImgSpec pe;
        pe.entryRva = 0x2FCD;
        const uintptr_t I5 = MakeImage(pe, [](uint8_t* b) { memcpy(b + 0x2FCD, kSafePE, 8); memcpy(b + 0x1000, kSafePE, 8); });
        Stark::s_hookedAddr = I5 + 0x1000;
        Stark::HoldTrampolineBlock();
        check("HOOK hold page end ⭐: a candidate whose span runs into a page that cannot be read is refused, not hooked",
              Stark::s_heldBlock == 0 && HasLog('W', F(kNoBlock, F("0x%llX: not readable code; 0x%llX: a real hook's "
              "target; ", (unsigned long long)(I5 + 0x2FCD), (unsigned long long)(I5 + 0x1000)).c_str()))
              && MH_DisableHook(reinterpret_cast<LPVOID>(I5 + 0x2FCD)) == MH_ERROR_NOT_CREATED, FirstLog('W', "").c_str());

        HookReset();
        ImgSpec ur;
        ur.dynBase = false;
        ur.secs = { { 0x1000, 0x2000, IMAGE_SCN_CNT_CODE | IMAGE_SCN_MEM_READ } };
        const uintptr_t I6 = MakeImage(ur, safeAt(0x1000));
        Stark::HoldTrampolineBlock();
        const Lager::Span s6 = Lager::OriginsServedBy(I6 - 0x10000, reinterpret_cast<uint64_t>(si.lpMinimumApplicationAddress),
                                                      reinterpret_cast<uint64_t>(si.lpMaximumApplicationAddress));
        check("HOOK hold unknown range: no executable section named -- held, coverage not checked, and said so",
              Stark::s_heldBlock == I6 - 0x10000 && HasLog('W', F("GameThreadDispatch: held a MinHook trampoline block "
              "for the process lifetime: block 0x%llX, taken by a hook created (never enabled) on 0x%llX, the image's "
              "entry point; it serves hook targets [0x%llX, 0x%llX]; the image's executable range is unknown (its headers "
              "named no executable section), so coverage was not checked (image 0x%llX + 0x10000, DYNAMICBASE no)",
              (unsigned long long)(I6 - 0x10000), (unsigned long long)(I6 + 0x1000), (unsigned long long)s6.lo,
              (unsigned long long)s6.hi, (unsigned long long)I6)) && CountContaining("which covers") == 0
              && CountContaining("does NOT cover") == 0, FirstLog('W', "").c_str());

        HookReset();
        ImgSpec big;
        big.sizeOfImage = 0xC0000000;
        big.secs = { { 0x1000, 0xBFFFF000, kExecChars } };
        const uintptr_t I7 = MakeImage(big, safeAt(0x1000));
        Stark::HoldTrampolineBlock();
        check("HOOK hold not covering: code beyond the block's reach is warned about", Stark::s_heldBlock == I7 - 0x10000
              && HasLog('W', HeldLine(I7 - 0x10000, I7 + 0x1000, "the image's entry point", "which does NOT cover -- a "
              "hook outside it needs a block of its own --", I7 + 0x1000, I7 + 0xC0000000, I7, 0xC0000000, true)),
              FirstLog('W', "").c_str());

        HookReset();
        const uintptr_t I8 = MakeImage(ImgSpec{}, safeAt(0x1000));
        Stark::HoldTrampolineBlock();
        const bool heldFirst = Stark::s_heldTarget == I8 + 0x1000;
        const bool okS = Stark::InstallHook(I8 + 0x1000);
        const uintptr_t tr = reinterpret_cast<uintptr_t>(Stark::s_originalPE);
        const uint32_t vs = CallTarget(I8 + 0x1000);
        check("HOOK step aside ⭐: ProcessEvent at the holder's address takes it -- the holder removed, the hook real",
              heldFirst && okS && HasLog('W', F("GameThreadDispatch: ProcessEvent 0x%llX is the held block's holder target "
              "-- holder removed (MH_OK) so the real hook can take the address", (unsigned long long)(I8 + 0x1000)))
              && Stark::s_heldTarget == 0 && Stark::s_heldBlock == 0
              && HasLog('I', F(kInstalled, (unsigned long long)(I8 + 0x1000)))
              && HasLog('I', F("GameThreadDispatch: ProcessEvent's trampoline 0x%llX is in block 0x%llX (no block was "
                               "held)", (unsigned long long)tr, (unsigned long long)(I8 - 0x10000)))
              && vs == 0x12345678 && Stark::GetHookFireCount() == 1);

        HookReset();
        const uintptr_t I9 = MakeImage(ImgSpec{}, [](uint8_t* b) { memcpy(b + 0x1000, kUnsafe, 8); });
        Stark::HoldTrampolineBlock();
        const bool heldU = Stark::s_heldTarget == I9 + 0x1000 && Stark::s_heldBlock == I9 - 0x10000;
        const Falsch::Report ru = Falsch::VetHookTarget(I9 + 0x1000);
        const bool okV = Stark::InstallHook(I9 + 0x1000);
        check("HOOK vet before step aside ⭐: a refused ProcessEvent does not cost the held block", heldU
              && ru.verdict == Falsch::Verdict::Unsafe && !okV && CountContaining("holder removed") == 0
              && Stark::s_heldTarget == I9 + 0x1000 && Stark::s_heldBlock == I9 - 0x10000
              && MH_CreateHook(reinterpret_cast<LPVOID>(I9 + 0x1000), reinterpret_cast<LPVOID>(&Stark::HolderDetour),
                               nullptr) == MH_ERROR_ALREADY_CREATED);
    }

    {   blk("HOOK HEADER - the main image's header is queried before it is read, and a fault reading it is contained");
        HookReset();
        const uintptr_t I = MakeImage(ImgSpec{}, [](uint8_t* b) { memcpy(b + 0x1000, kSafePE, 8); });
        DWORD old = 0;
        VirtualProtect(reinterpret_cast<void*>(I), 0x1000, PAGE_READONLY | PAGE_GUARD, &old);
        g_watchLo.store(I);
        g_watchHi.store(I + 0x1000);
        g_watchFaults.store(0);
        const Lager::ImageLayout lay = Stark::ReadMainImageLayout();
        Stark::HoldTrampolineBlock();
        check("HOOK header guard ⭐: a guarded header is left alone -- unknown layout, the guard still armed, no fault",
              !lay.valid && lay.base == I && HasLog('W', F(kNoBlock, "the main image's headers named no candidate"))
              && (RealQuery(I).Protect & PAGE_GUARD) != 0 && g_watchFaults.load() == 0);

        HookReset();
        const uintptr_t J = MakeImage(ImgSpec{}, [](uint8_t*) {});
        VirtualProtect(reinterpret_cast<void*>(J), 0x1000, PAGE_NOACCESS, &old);
        g_watchLo.store(J);
        g_watchHi.store(J + 0x1000);
        g_watchFaults.store(0);
        const Lager::ImageLayout lj = Stark::ReadMainImageLayout();
        check("HOOK header no access: unknown layout, never read", !lj.valid && lj.base == J && g_watchFaults.load() == 0);

        HookReset();
        const uintptr_t K = MakeImage(ImgSpec{}, [](uint8_t*) {});
        g_vqMode = VqMode::Lie;
        g_vqLo = K;
        g_vqLieState = MEM_RESERVE;
        g_vqLieProtect = PAGE_READONLY;
        const Lager::ImageLayout lk = Stark::ReadMainImageLayout();
        g_vqMode = VqMode::Real;
        check("HOOK header state: a page that is not committed is refused, whatever its protection claims", !lk.valid);

        HookReset();
        const uintptr_t L = MakeImage(ImgSpec{}, [](uint8_t*) {});
        VirtualProtect(reinterpret_cast<void*>(L), 0x1000, PAGE_NOACCESS, &old);
        g_vqMode = VqMode::Lie;
        g_vqLo = L;
        g_vqLieState = MEM_COMMIT;
        g_vqLieProtect = PAGE_READONLY;
        g_watchLo.store(L);
        g_watchHi.store(L + 0x1000);
        g_watchFaults.store(0);
        const bool ran = RanWithoutEscapingSEH(&ReadLayoutIntoGlobal);
        g_vqMode = VqMode::Real;
        check("HOOK header fault ⭐: a read that faults is contained inside Stark, with an unknown layout", ran
              && !g_layout.valid && g_layout.base == L && g_watchFaults.load() == 1,
              F("ran=%d faults=%d", (int)ran, g_watchFaults.load()).c_str());
        g_watchLo.store(0);
        g_watchHi.store(0);
    }

    {   blk("HOOK WALK - the window walk stops on a failed query and on its region cap");
        HookReset();
        SYSTEM_INFO si{};
        GetSystemInfo(&si);
        const uint64_t appMax = reinterpret_cast<uint64_t>(si.lpMaximumApplicationAddress);
        MEMORY_BASIC_INFORMATION probe{};
        const bool pastTopFails = ::VirtualQuery(reinterpret_cast<LPCVOID>(appMax + 1), &probe, sizeof probe) == 0;
        Lager::WalkStop st = Lager::WalkStop::Complete;
        const auto m = Stark::WalkRegions(appMax + 1 - 0x20000, appMax + 1 + 0x20000, st);
        check("HOOK walk: a query that fails ends the walk and says so", pastTopFails && st == Lager::WalkStop::QueryFailed
              && !m.empty());

        HookReset();
        const uintptr_t S = 0x100000000000ull;
        g_vqMode = VqMode::Stripes;
        g_vqLo = S;
        g_vqHi = S + 0x40000000;
        Lager::WalkStop sc = Lager::WalkStop::Complete;
        const auto c = Stark::WalkRegions(S, S + 0x40000000, sc);
        g_vqMode = VqMode::Real;
        bool fields = !c.empty();
        for (const auto& r : c) fields = fields && r.type == MEM_PRIVATE && r.allocationBase == S;
        check("HOOK walk cap ⭐: the walk stops at 200000 regions, each recorded with its state, type and allocation",
              sc == Lager::WalkStop::RegionCap && c.size() == 200000 && c.back().base == S + 199999ull * 0x1000
              && c[0].state == MEM_COMMIT && c[1].state == MEM_RESERVE && fields, F("size=%zu", c.size()).c_str());
    }

    {   blk("HOOK WINDOW - a MinHook allocation failure walks the window it searched and says what took it");
        SYSTEM_INFO si{};
        GetSystemInfo(&si);
        const auto* exe = reinterpret_cast<const uint8_t*>(::GetModuleHandleW(nullptr));
        const auto* ent = reinterpret_cast<const IMAGE_NT_HEADERS64*>(
            exe + reinterpret_cast<const IMAGE_DOS_HEADER*>(exe)->e_lfanew);
        const std::string mainImage = F("main image 0x%llX + 0x%X, DYNAMICBASE %s", (unsigned long long)(uintptr_t)exe,
            ent->OptionalHeader.SizeOfImage,
            (ent->OptionalHeader.DllCharacteristics & IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE) ? "yes" : "no");
        const std::string nb = " -- NB a block start WAS free, so the window is not the cause: VirtualAlloc refused the "
                               "page (commit limit / ACG) or MinHook's hook table could not grow (LastError %u)";
        auto starvedLine = [&](uintptr_t T, uintptr_t P, const std::string& held, const std::string& tail) {
            return F("GameThreadDispatch: MinHook block window for 0x%llX: block starts [0x%llX, 0x%llX] -- 0 free "
                     "region(s), 0x0 bytes free, largest 0x0 at 0x0, 0 can start a block; reserved 0x7FFFF001, committed "
                     "0x1000; taken by private 0x80000001, image 0x0, mapped 0x0, largest occupant 0x%llX x 0x40000000 "
                     "(private/heap); FindPrevFreeRegion -> none, FindNextFreeRegion -> none; held block %s; %s%s (4 "
                     "region(s) walked in 0 ms)", (unsigned long long)T, (unsigned long long)(T - 0x40000000),
                     (unsigned long long)(T + 0x3FFFF001), (unsigned long long)P, held.c_str(), mainImage.c_str(),
                     tail.c_str());
        };

        HookReset();
        uintptr_t P = 0;
        const uintptr_t T = Starve(P);
        const bool okA = T && Stark::InstallHook(T);
        const std::string line = starvedLine(T, P, "none", "");
        check("HOOK window ⭐: a full window -- the allocation failure, then the map of what took the window",
              T != 0 && !okA && !Stark::IsHookActive()
              && HasLog('E', "GameThreadDispatch: MH_CreateHook failed: MH_ERROR_MEMORY_ALLOC") && HasLog('E', line),
              FirstLog('E', "GameThreadDispatch: MinHook block window").c_str());

        HookReset();
        uintptr_t P2 = 0;
        const uintptr_t T2 = Starve(P2);
        Stark::s_heldBlock = T2 - 0x10000;
        SetLastError(kLogClobber);
        const bool okL = T2 && Stark::InstallHook(T2);
        check("HOOK window last error ⭐: the LastError read straight after MinHook's call -- 0, not what the log left",
              T2 != 0 && !okL && CountContaining("out of reach") == 0 && HasLog('E', starvedLine(T2, P2,
              F("0x%llX (serves this target)", (unsigned long long)(T2 - 0x10000)), F(nb.c_str(), 0u))),
              FirstLog('E', "GameThreadDispatch: MinHook block window").c_str());

        HookReset();
        uintptr_t P6 = 0;
        const uintptr_t T6 = Starve(P6);
        const uintptr_t farBlock = (T6 + 0x100000000ull < reinterpret_cast<uintptr_t>(si.lpMaximumApplicationAddress))
            ? T6 + 0x100000000ull : T6 - 0x100000000ull;
        Stark::s_heldBlock = farBlock;
        const bool okF = T6 && Stark::InstallHook(T6);
        const int iW = IndexOfLog('W', F("GameThreadDispatch: the held block 0x%llX is out of reach of ProcessEvent 0x%llX "
            "(MinHook reuses a block only within +-1 GB) -- it must find a block of its own", (unsigned long long)farBlock,
            (unsigned long long)T6));
        const int iE = IndexOfLog('E', "GameThreadDispatch: MH_CreateHook failed: MH_ERROR_MEMORY_ALLOC");
        check("HOOK reach before the attempt: an unreachable held block is said so before MinHook tries, not after",
              T6 != 0 && !okF && iW >= 0 && iE > iW);

        HookReset();
        uintptr_t P3 = 0;
        const uintptr_t T1 = Starve(P3), Tb = T1 + 0x10000;
        const uint64_t B = g_tick.load();
        g_tick.store(B);
        const bool c1 = Stark::InstallHook(T1);
        g_tick.store(B + 4999);
        const int reads0 = g_tickReads.load(), vq0 = g_vqCalls.load();
        const bool c2 = Stark::InstallHook(T1);
        const int reads1 = g_tickReads.load(), vq1 = g_vqCalls.load();
        g_tick.store(B + 5000);
        const bool c3 = Stark::InstallHook(T1);
        g_tick.store(B + 5001);
        Stark::LogTrampolineWindow(Tb, 0);
        g_tick.store(B + 5002);
        const bool c5 = Stark::InstallHook(T1);
        const std::string w1 = F("GameThreadDispatch: MinHook block window for 0x%llX:", (unsigned long long)T1);
        const std::string w2 = F("GameThreadDispatch: MinHook block window for 0x%llX:", (unsigned long long)Tb);
        check("HOOK window throttle ⭐: the same target is not walked again within 5 s -- not even its clock or a query",
              T1 != 0 && !c1 && !c2 && !c3 && !c5 && CountLog('E', w1) == 3 && CountLog('E', w2) == 1
              && HasLog('W', F("GameThreadDispatch: MinHook block window for 0x%llX not re-walked -- its map was logged "
                               "4999 ms ago", (unsigned long long)T1))
              && CountLog('E', "GameThreadDispatch: MH_CreateHook failed: MH_ERROR_MEMORY_ALLOC") == 4
              && reads1 - reads0 == 1 && vq1 - vq0 == 0, F("walks=%d reads=%d vq=%d", CountLog('E', w1),
              reads1 - reads0, vq1 - vq0).c_str());

        HookReset();
        uintptr_t P4 = 0;
        const uintptr_t T4 = LivePick(P4);
        g_tickStep = 3000;
        Stark::LogTrampolineWindow(T4, 1234);
        g_tickStep = 0;
        const std::string live = F("GameThreadDispatch: MinHook block window for 0x%llX: block starts [0x%llX, 0x%llX] "
            "(map INCOMPLETE: the walk's time budget was spent before the window was fully mapped -- the byte totals "
            "cover only the part walked) -- 0 free region(s), 0x0 bytes free, largest 0x0 at 0x0, 0 can start a block; "
            "reserved 0x3FFE0000, committed 0x0; taken by private 0x3FFE0000, image 0x0, mapped 0x0, largest occupant "
            "0x%llX x 0x3FFE0000 (private/heap); FindPrevFreeRegion -> 0x%llX, FindNextFreeRegion -> 0x%llX; held block "
            "none; %s%s (1 region(s) walked in 9000 ms)", (unsigned long long)T4, (unsigned long long)(T4 - 0x40000000),
            (unsigned long long)(T4 + 0x3FFFF001), (unsigned long long)P4, (unsigned long long)(T4 - 0x10000),
            (unsigned long long)(T4 + 0x10000), mainImage.c_str(), F(nb.c_str(), 1234u).c_str());
        check("HOOK window live picks ⭐: a walk cut by its time budget says so, and the picks are measured anyway",
              T4 != 0 && CountLog('E', "") == 1 && HasLog('E', live), FirstLog('E', "").c_str());

        HookReset();
        uintptr_t P5 = 0;
        const uintptr_t T5 = LivePick(P5);
        g_tickStep = 1000;
        Lager::WalkStop st = Lager::WalkStop::Complete;
        const auto m = Stark::WalkRegions(T5 - 0x40000000, T5 + 0x40000001, st);
        g_tickStep = 0;
        check("HOOK walk budget: the walk stops once its 2000 ms are spent -- not before", T5 != 0
              && st == Lager::WalkStop::Budget && m.size() == 3 && m[2].base == T5, F("size=%zu", m.size()).c_str());
    }

    {   blk("HOOK HELD BLOCK - ProcessEvent's trampoline lands in the held block; the log says whether it was needed");
        HookReset();
        const uintptr_t I = HeldWindow(true);
        Stark::HoldTrampolineBlock();
        const bool heldH = I && Stark::s_heldBlock == I - 0x10000;
        const bool okN = I && Stark::InstallHook(I + 0x2000);
        const uintptr_t tr = reinterpret_cast<uintptr_t>(Stark::s_originalPE);
        const uint32_t v = okN ? CallTarget(I + 0x2000) : 0;
        check("HOOK held needed ⭐: a starved window -- the trampoline in the held block, which the log calls NEEDED",
              heldH && okN && CountContaining("out of reach") == 0 && (tr & ~static_cast<uintptr_t>(0xFFF)) == I - 0x10000
              && HasLog('I', F("GameThreadDispatch: ProcessEvent's trampoline 0x%llX is in the held block 0x%llX",
                               (unsigned long long)tr, (unsigned long long)(I - 0x10000)))
              && HasLog('I', "GameThreadDispatch: the held block was NEEDED -- MinHook could not have placed a fresh "
                             "block (FindPrevFreeRegion -> none, FindNextFreeRegion -> none)")
              && memcmp(reinterpret_cast<const void*>(I + 0x1000), kSafePE, 8) == 0
              && MH_DisableHook(reinterpret_cast<LPVOID>(I + 0x1000)) == MH_ERROR_DISABLED
              && v == 0x12345678 && Stark::GetHookFireCount() == 1, FirstLog('I', "GameThreadDispatch: ProcessEvent's").c_str());

        HookReset();
        const uintptr_t J = HeldWindow(false);
        Stark::HoldTrampolineBlock();
        const bool freeAbove = J && RealQuery(J + 0x10000).State == MEM_FREE;
        const bool okR = J && Stark::InstallHook(J + 0x2000);
        check("HOOK held reused: reused, but a fresh block was free too -- the log says the window was not starved",
              freeAbove && okR && HasLog('I', F("GameThreadDispatch: ProcessEvent's trampoline 0x%llX is in the held "
              "block 0x%llX", (unsigned long long)reinterpret_cast<uintptr_t>(Stark::s_originalPE),
              (unsigned long long)(J - 0x10000))) && HasLog('I', F("GameThreadDispatch: the held block was reused, but a "
              "fresh block was also available this time (FindPrevFreeRegion -> 0x0, FindNextFreeRegion -> 0x%llX) -- the "
              "window was not starved", (unsigned long long)(J + 0x10000)))
              && CountContaining("NEEDED") == 0 && CountContaining("out of reach") == 0
              && MH_DisableHook(reinterpret_cast<LPVOID>(J + 0x1000)) == MH_ERROR_DISABLED,
              FirstLog('I', "GameThreadDispatch: the held").c_str());
    }

    {   blk("HOOK SHUTDOWN - queued requests answered -7, MinHook left as it is, the hook still forwarding");
        HookReset();
        const uintptr_t T = CodePage(kSafePE);
        const bool ok = Stark::InstallHook(T);
        auto r1 = std::make_shared<Stark::InvokeRequest>(), r2 = std::make_shared<Stark::InvokeRequest>(),
             r3 = std::make_shared<Stark::InvokeRequest>();
        r1->owner = 7; r2->owner = 7; r3->owner = 9;
        auto f1 = r1->promise.get_future(), f2 = r2->promise.get_future(), f3 = r3->promise.get_future();
        r3->promise.set_value(5);
        {
            std::lock_guard<std::mutex> lk(Stark::s_queueMutex);
            Stark::s_invokeQueue.push_back(r1);
            Stark::s_invokeQueue.push_back(r2);
            Stark::s_invokeQueue.push_back(r3);
            Stark::s_outstanding[7] = 2;
            Stark::s_outstanding[9] = 1;
            Stark::s_queueDepth.store(3);
        }
        bool escaped = false;
        try { Stark::Shutdown(); } catch (...) { escaped = true; }
        bool cleared = false;
        {
            std::lock_guard<std::mutex> lk(Stark::s_queueMutex);
            cleared = Stark::s_invokeQueue.empty() && Stark::s_queueDepth.load() == 0
                && Stark::s_outstanding.find(7) == Stark::s_outstanding.end()
                && Stark::s_outstanding.find(9) == Stark::s_outstanding.end();
        }
        const MH_STATUS again = MH_Initialize();
        const uint32_t v = CallTarget(T);
        check("HOOK shutdown ⭐: every queued request answered -7 (one already answered left alone), the counts cleared",
              ok && !escaped && f1.get() == -7 && f2.get() == -7 && f3.get() == 5 && cleared);
        check("HOOK shutdown ⭐: MinHook is not torn down -- the patch stays and still forwards", !Stark::IsHookActive()
              && HasLog('I', "GameThreadDispatch: hook flag cleared (physical hook retained to avoid in-flight unhook "
              "race)") && again == MH_ERROR_ALREADY_INITIALIZED && *reinterpret_cast<const uint8_t*>(T) == 0xE9
              && v == 0x12345678 && Stark::GetHookFireCount() == 1);
    }

    HookReset();
    MH_Uninitialize();
    FreeFixtures();
}

int main() {
    setvbuf(stdout, nullptr, _IONBF, 0);
    printf("stark_dispatch_test — GameThreadDispatch: the invoke queue and its game-thread drain, in this process\n");
#ifdef __SANITIZE_ADDRESS__
    // [CPP-HARDENING-2026-11-14] The AddressSanitizer build only: the banner and negative control
    // build_dll.py --asan --run needs (a deliberate heap overflow that must be reported).
    printf("asan: on\n");
    {
        size_t negLen = 0;
        if (getenv_s(&negLen, nullptr, 0, "UE5_ASAN_NEGATIVE_CONTROL") == 0 && negLen > 0) {
            char* volatile p = new char[16];
            p[16] = 1;   // heap-buffer-overflow: ASan ends the process here
            delete[] p;
        }
    }
#endif
    g_mainTid = GetCurrentThreadId();
    AddVectoredExceptionHandler(1, WatchFaults);
    g_gateEntered = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    g_inside = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    g_release = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    g_gateRelease = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    g_game.Start();

    QueueBlocks();
    ResetStark();
    g_game.Stop();

    ThreadBlocks();
    SetEvent(g_release);
    ResetStark();

    HookBlocks();
    CloseHandle(g_gateEntered);
    CloseHandle(g_gateRelease);
    CloseHandle(g_inside);
    CloseHandle(g_release);
    printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
    return (g_fail == 0 && g_pass > 0) ? 0 : 1;
}
