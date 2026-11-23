// stark_dispatch_test.cpp
// UE5CEDumper — GameThreadDispatch (Stark.cpp), the invoke queue and its game-thread drain, in this process.
//
// WHAT IT DRIVES
//   EnqueueInvoke on caller threads and the drain on a fake game thread that enters the detour (HookedProcessEvent) as
//   the game would: a queued invoke run against its own copy of the parameters or the caller's buffer, a timeout that
//   leaves the request queued, a request its waiter abandons or the drain claims first, the if_free owner rule decided
//   under the queue's lock, the counts that last until a request is answered, a departed owner's drop, one request at
//   a time with no nested drain, a shutdown that cuts the wait on a stalled game, and the own-call mark.
//
// WHAT IS FAKED
//   Sein records every log line, so a check can read what was logged. ProcessEvent is FakePE, a function in this
//   executable that records each call and adds two numbers into an out-parameter; s_originalPE is set to it directly,
//   so no hook is installed here. With s_gameThreadId 0 every thread's entry counts as the game thread's.
//
// WHY IT #includes THE .cpp
//   The queue, its lock, its counts and the detour are statics of Stark.cpp. Linie, which the detour feeds, is
//   ue5_testcore's; Falsch.cpp and MinHook are linked because the install path references them.

#include <windows.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>   // getenv_s: the AddressSanitizer build's negative control
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

// ── the log recorder ────────────────────────────────────────────────────────────────────────────────────────────
struct LogLine { char lvl; std::string text; };
static std::mutex g_logMu;
static std::vector<LogLine> g_log;
static void LogV(char lvl, const char* fmt, va_list ap) {
    char b[8192];
    vsnprintf(b, sizeof b, fmt, ap);
    std::lock_guard<std::mutex> lk(g_logMu);
    g_log.push_back({ lvl, b });
}
namespace Sein {
void Info(const char*, const char* fmt, ...) { va_list ap; va_start(ap, fmt); LogV('I', fmt, ap); va_end(ap); }
void Error(const char*, const char* fmt, ...) { va_list ap; va_start(ap, fmt); LogV('E', fmt, ap); va_end(ap); }
void Warn(const char*, const char* fmt, ...) { va_list ap; va_start(ap, fmt); LogV('W', fmt, ap); va_end(ap); }
void Debug(const char*, const char* fmt, ...) { va_list ap; va_start(ap, fmt); LogV('D', fmt, ap); va_end(ap); }
void Summary(const char*, ...) {}
}  // namespace Sein

#include "../src/Stark.cpp"  // NOLINT -- see "WHY IT #includes THE .cpp" above

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

struct AddParams { int32_t a, b, out; };
struct PeCall {
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
    ++t_peDepth;
    if (f == kFuncGate) {
        SetEvent(g_gateEntered);
        WaitForSingleObject(g_gateRelease, 10000);
    }
    if (f == kFuncNest) Stark::HookedProcessEvent(reinterpret_cast<void*>(kTickObj), reinterpret_cast<void*>(kTickFunc), nullptr);
    if (params && IsAddId(f)) {
        auto* p = static_cast<AddParams*>(params);
        p->out = p->a + p->b;
    }
    --t_peDepth;
    std::lock_guard<std::mutex> lk(g_peMu);
    g_pe[me].exited = true;
    PushSeq("-%llX", f, 0);
}
// A class's own ProcessEvent override: it calls Super::ProcessEvent, the address the hook patched.
static void __fastcall FakeOverride(void* obj, void* ufunc, void* params) {
    g_overrideCalls.fetch_add(1);
    g_overrideSawOwn.store(Stark::InOwnPeCall());
    Stark::HookedProcessEvent(obj, ufunc, params);
}
static void __fastcall FakeOverrideRaise(void*, void*, void*) {
    g_overrideCalls.fetch_add(1);
    RaiseException(0xE0000F0E, 0, 0, nullptr);
}
static std::vector<PeCall> Calls(uintptr_t f) {
    std::lock_guard<std::mutex> lk(g_peMu);
    std::vector<PeCall> out;
    for (const auto& c : g_pe) if (c.ufunc == f) out.push_back(c);
    return out;
}
static bool AnyCallWithA(int32_t a) {
    std::lock_guard<std::mutex> lk(g_peMu);
    for (const auto& c : g_pe) if (IsAddId(c.ufunc) && c.seen.a == a) return true;
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
    void Start(uint64_t owner, bool ifFree, uintptr_t func, AddParams* buf, size_t size, bool drop = false,
               bool wantQueued = false) {
        th = std::thread([this, owner, ifFree, func, buf, size, drop, wantQueued] {
            auto body = [&] {
                const uint64_t t0 = Stark::NowMs();
                rc = Stark::EnqueueInvoke(kInst, func, reinterpret_cast<uintptr_t>(buf), size, drop,
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
    g_gateEntered = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    g_gateRelease = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    g_game.Start();

    QueueBlocks();

    ResetStark();
    g_game.Stop();
    CloseHandle(g_gateEntered);
    CloseHandle(g_gateRelease);
    printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
    return (g_fail == 0 && g_pass > 0) ? 0 : 1;
}
