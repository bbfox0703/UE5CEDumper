// ============================================================
// Stark — 修塔爾克 (勇者戰士 — Brave Warrior)
// GameThreadDispatch: MinHook ProcessEvent hook + game-thread queue
//
// Hooks UObject::ProcessEvent using MinHook. Every game-thread PE
// call first drains a lock-protected queue of pending invocations
// submitted from the pipe handler thread.
//
// Empty-queue fast path: one mutex lock/unlock per ProcessEvent
// call (negligible vs ProcessEvent's own cost).
// ============================================================

#define LOG_CAT "PIPE"
#include "Sein.h"
#include "Stark.h"
#include "Linie.h"   // Live PE profiler — opt-in per-UFunction fire counting
#include "Routine.h"   // Routine::RunThreadGuarded — a throw out of the PE hook fast-fails the GAME (B14)

#include <MinHook.h>
#include <Windows.h>

#include <atomic>
#include <chrono>
#include <cstring>
#include <future>
#include <memory>
#include <mutex>
#include <queue>
#include <vector>

namespace Stark {

// ---- Types ----

/// A single queued ProcessEvent invocation request.
/// Shared ownership: pipe thread holds shared_ptr while waiting on future,
/// game thread holds shared_ptr while executing.
///
/// CRITICAL: the shared_ptr keeps the REQUEST struct alive, but NOT the caller's
/// parameter buffer. If the caller passes a transient buffer (e.g. the pipe
/// handler's stack-local vector) and the invoke TIMES OUT, the request stays
/// queued while the caller's buffer is freed — the game thread then dereferences
/// freed memory (use-after-free). To make a timed-out-but-still-queued request
/// self-contained, EnqueueInvoke COPIES the param bytes into `ownedParams` (when
/// a size is given) and points `params` at that owned copy. Callers that pass a
/// persistent buffer (Mimic's mailbox global) may pass size 0 to skip the copy.
struct InvokeRequest {
    uintptr_t instance;
    uintptr_t ufunc;
    uintptr_t params;
    std::vector<uint8_t> ownedParams;   // owns the param bytes when copied (size>0)
    std::promise<int32_t> promise;
};

// ---- State ----

// Original ProcessEvent function pointer (set by MinHook)
typedef void(__fastcall* FnProcessEvent)(void* thisObj, void* ufunc, void* params);
static FnProcessEvent s_originalPE = nullptr;

// Pending invoke queue
static std::mutex s_queueMutex;
static std::queue<std::shared_ptr<InvokeRequest>> s_invokeQueue;
// Relaxed mirror of s_invokeQueue.size(), maintained under s_queueMutex. Lets the
// hot ProcessEvent hook skip taking the mutex on the overwhelmingly common path
// where no invoke is pending (ProcessEvent fires thousands of times per second).
static std::atomic<size_t> s_queueDepth{0};

// Hook state
static std::atomic<bool> s_hookActive{false};
static std::atomic<bool> s_mhInitialized{false};
static uintptr_t s_hookedAddr = 0;

// Timeout for waiting on game-thread execution. Atomic so the pipe thread
// (handling set_invoke_timeout) can update it while another pipe call is
// already blocked in EnqueueInvoke without locking. Re-read on each invoke;
// already-pending requests keep their original timeout (consistent with how
// future.wait_for is captured at call time).
static std::atomic<int32_t> s_invokeTimeoutMs{kDefaultInvokeTimeoutMs};

// Hook fire counter — incremented every time HookedProcessEvent runs.
// Used by post-install validation (Frieren::TryInstallGameThreadHook): a
// correctly-placed PE hook fires many times per second under normal
// gameplay, so a 0 count ~1.5s after install means we hooked the wrong
// vtable slot. relaxed memory order — readers just want a non-zero check.
static std::atomic<uint64_t> s_hookFireCount{0};

// steady_clock timestamp (ms) of the last HookedProcessEvent fire. 0 = never
// fired. A live game ticks ProcessEvent hundreds of times per frame, so this
// advances continuously during gameplay and FREEZES the instant the game
// thread stops (pause menu that halts the world, breakpoint, alt-tab throttle).
// MsSinceLastHookFire / IsGameThreadResponsive read it to fail-fast invokes and
// surface a "game paused" hint instead of blocking on a thread that won't run.
static std::atomic<uint64_t> s_lastHookFireMs{0};

// steady_clock timestamp (ms) when the hook last went active. Lets
// IsGameThreadResponsive distinguish "just installed, give it a beat to tick"
// from "installed a while ago and STILL never fired" — the latter means the game
// thread was already paused when we hooked (connected while paused), where
// s_lastHookFireMs alone stays 0 forever and can't reveal the stall.
static std::atomic<uint64_t> s_hookInstalledMs{0};

// Monotonic milliseconds since process start. steady_clock (not system_clock)
// so a wall-clock adjustment can't make an interval read negative/huge.
static uint64_t NowMs() {
    return (uint64_t)std::chrono::duration_cast<std::chrono::milliseconds>(
               std::chrono::steady_clock::now().time_since_epoch())
        .count();
}

// ---- SEH-isolated helper ----

/// Call ProcessEvent with SEH protection. Isolated into a separate function
/// because MSVC does not allow __try in functions with C++ objects that
/// require unwinding (shared_ptr, vector, promise, etc.).
/// Returns 0 on success, -3 if no original PE, -4 on SEH exception.
static int32_t CallProcessEventSEH(uintptr_t instance, uintptr_t ufunc, uintptr_t params) {
    if (!s_originalPE) return -3;
    __try {
        s_originalPE(
            reinterpret_cast<void*>(instance),
            reinterpret_cast<void*>(ufunc),
            reinterpret_cast<void*>(params));
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return -4;
    }
    return 0;
}

// True while THIS thread is executing inside Stark::CallOriginalSEH (audit #5
// ST1). Deliberately NOT set inside CallProcessEventSEH: that is what the
// legitimate game-thread drain calls, and a UFunction executing there routinely
// dispatches further ProcessEvent calls through the vtable -- i.e. back through
// our detour. Marking the thread there would make the game's own nested
// dispatches look like ours and suppress draining on the very thread that is
// supposed to drain.
static thread_local int t_inOwnPeCall = 0;

namespace {
struct OwnPeCallGuard {
    OwnPeCallGuard()  { ++t_inOwnPeCall; }
    ~OwnPeCallGuard() { --t_inOwnPeCall; }
};
}  // namespace

// ---- Hook function ----

/// Hooked ProcessEvent — called on the game thread for every UObject event.
/// Drains the invoke queue first, then calls the original PE for the game's own call.
// Everything OUR hook does before handing control to the game's ProcessEvent. Split out
// so the whole body sits inside one guard: this function is entered from GAME CODE on the
// GAME'S OWN THREAD, so an exception escaping it unwinds into a frame that has no handler
// for us and the process fast-fails (0xC0000409, FAST_FAIL_FATAL_APP_EXIT).
//
// That is not hypothetical — it is the DQ7R crash of 2026-08-04 (build 2622), captured in
// a WER dump whose entire stack was inside our module. It allocates in two places, both
// reachable while the game is tearing its object pool down: Linie's map insert, and the
// `pending` vector below. Zero cost on x64 when nothing throws (table-driven EH). (B14,
// scope corrected by in-game verification.)
static void HookedProcessEventBody(void* ufunc, uint64_t nowMs) {
    // Live PE profiler (Linie): opt-in per-UFunction fire counting. The
    // not-recording path pays exactly one relaxed atomic load + a
    // predicted-not-taken branch; the mutex + map touch happen ONLY inside a
    // Start/Stop window. Mirrors the s_queueDepth "skip work unless armed" gate
    // below. `ufunc` is the UFunction* for this dispatch — stored raw and
    // resolved to a name at pe_profile_get time, off the hot path.
    if (Linie::IsRecording()) {
        Linie::RecordCall(reinterpret_cast<uintptr_t>(ufunc), nowMs);
    }

    // Drain pending invocations from pipe thread. Fast path: skip the mutex
    // entirely unless the pipe thread has actually enqueued something. A stale
    // zero read just defers a freshly enqueued request to the next PE call
    // (microseconds away), which is harmless — the real happens-before for the
    // request data is the mutex taken below.
    // The gate itself lives in Stark.h as a pure predicate so it can be unit
    // tested -- no target compiles this file. Called, not mirrored: a mirrored
    // copy would prove only that the copy is right.
    if (ShouldDrainQueue(s_queueDepth.load(std::memory_order_acquire), InOwnPeCall())) {
        std::vector<std::shared_ptr<InvokeRequest>> pending;

        {
            std::lock_guard<std::mutex> lock(s_queueMutex);
            while (!s_invokeQueue.empty()) {
                pending.push_back(std::move(s_invokeQueue.front()));
                s_invokeQueue.pop();
            }
            s_queueDepth.store(0, std::memory_order_release);
        }

        // Execute all pending requests outside the lock
        for (auto& req : pending) {
            int32_t result = CallProcessEventSEH(req->instance, req->ufunc, req->params);

            if (result == -4) {
                LOG_ERROR("GameThreadDispatch: SEH exception during queued PE call "
                          "inst=0x%llX func=0x%llX",
                          (unsigned long long)req->instance,
                          (unsigned long long)req->ufunc);
            }

            // Fulfill the promise — unblocks the waiting pipe thread
            try {
                req->promise.set_value(result);
            } catch (...) {
                // Promise already satisfied (shouldn't happen, but be safe)
            }
        }
    }
}

static void __fastcall HookedProcessEvent(void* thisObj, void* ufunc, void* params) {
    // Tick the fire counter first thing. Even if the queue is empty and we
    // pass straight through to s_originalPE, this gives the post-install
    // validator (Frieren) ground truth that "we are sitting on the right
    // vtable slot." relaxed: a single non-zero observation by the validator
    // is enough; we never read this back inside the hot path.
    s_hookFireCount.fetch_add(1, std::memory_order_relaxed);
    // Stamp the fire time so IsGameThreadResponsive / MsSinceLastHookFire can
    // tell "ticking now" from "went quiet". One clock read on the hot path —
    // cheap next to ProcessEvent's own work, and the atomic store is relaxed.
    uint64_t nowMs = NowMs();
    s_lastHookFireMs.store(nowMs, std::memory_order_relaxed);

    // Our work, contained. See HookedProcessEventBody — a throw escaping here fast-fails
    // the game, because the caller is the game itself.
    Routine::RunThreadGuarded("GameThreadDispatch", [&] {
        HookedProcessEventBody(ufunc, nowMs);
    });

    // Now handle the game's own ProcessEvent call. OUTSIDE the guard on purpose: this is
    // the game's own code and its exceptions are its own business — swallowing one here
    // would silently change the game's behaviour, which is far worse than what the guard
    // above prevents.
    if (s_originalPE) {
        s_originalPE(thisObj, ufunc, params);
    }
}

// ---- Public API ----

bool InstallHook(uintptr_t processEventAddr) {
    // Serialize the whole check-then-MH_CreateHook/MH_EnableHook sequence. This is
    // a public API and the Frieren-side call_once only covers the game-thread
    // dispatch path — a concurrent (or future) caller must never race two
    // MH_CreateHook calls on the same target, which can corrupt MinHook's internal
    // tables (audit #3). A second caller blocks here, then hits the s_hookActive /
    // s_hookedAddr fast-paths below and no-ops.
    static std::mutex s_installMutex;
    std::lock_guard<std::mutex> lock(s_installMutex);

    if (s_hookActive.load()) {
        LOG_WARN("GameThreadDispatch: hook already active");
        return true;
    }

    if (!processEventAddr) {
        LOG_ERROR("GameThreadDispatch: null processEventAddr");
        return false;
    }

    // Audit fix #13: re-enable after soft disable. RemoveHook only flips
    // s_hookActive to false (the physical hook stays installed to avoid
    // an in-flight unhook race), so a second InstallHook on the same
    // address just flips the flag back on. No MinHook calls needed.
    if (s_hookedAddr == processEventAddr && s_originalPE != nullptr) {
        s_hookInstalledMs.store(NowMs(), std::memory_order_relaxed);
        s_hookActive.store(true);
        LOG_INFO("GameThreadDispatch: re-enabled existing hook at 0x%llX",
                 (unsigned long long)processEventAddr);
        return true;
    }

    // Initialize MinHook (once)
    if (!s_mhInitialized.load()) {
        MH_STATUS status = MH_Initialize();
        if (status != MH_OK && status != MH_ERROR_ALREADY_INITIALIZED) {
            LOG_ERROR("GameThreadDispatch: MH_Initialize failed: %s",
                      MH_StatusToString(status));
            return false;
        }
        s_mhInitialized.store(true);
    }

    // Create hook
    MH_STATUS status = MH_CreateHook(
        reinterpret_cast<LPVOID>(processEventAddr),
        reinterpret_cast<LPVOID>(&HookedProcessEvent),
        reinterpret_cast<LPVOID*>(&s_originalPE));

    if (status != MH_OK) {
        LOG_ERROR("GameThreadDispatch: MH_CreateHook failed: %s",
                  MH_StatusToString(status));
        return false;
    }

    // Enable hook
    status = MH_EnableHook(reinterpret_cast<LPVOID>(processEventAddr));
    if (status != MH_OK) {
        LOG_ERROR("GameThreadDispatch: MH_EnableHook failed: %s",
                  MH_StatusToString(status));
        MH_RemoveHook(reinterpret_cast<LPVOID>(processEventAddr));
        return false;
    }

    s_hookedAddr = processEventAddr;
    s_hookInstalledMs.store(NowMs(), std::memory_order_relaxed);
    s_hookActive.store(true);
    LOG_INFO("GameThreadDispatch: ProcessEvent hook installed at 0x%llX",
             (unsigned long long)processEventAddr);
    return true;
}

void RemoveHook() {
    if (!s_hookActive.load()) return;

    // Audit fix #13: do NOT call MH_DisableHook + MH_RemoveHook. They patch
    // the original code page back and free the trampoline — but a game
    // thread may be executing INSIDE the trampoline at this exact moment,
    // and MinHook does not synchronize with in-flight calls. Unhooking
    // under it is a guaranteed crash.
    //
    // Soft disable: just flip the active flag. EnqueueInvoke now returns
    // -7 immediately, so no new requests reach the queue. HookedProcessEvent
    // remains the entry point — with an empty queue (drained by Shutdown
    // for a clean stop), it just forwards to s_originalPE. The few KB of
    // trampoline memory persists until process exit, where the OS reclaims
    // it.
    //
    // s_originalPE / s_hookedAddr are intentionally NOT cleared so that
    // (a) HookedProcessEvent can still forward to the original PE for any
    //     game thread still inside our trampoline at this moment, and
    // (b) a subsequent InstallHook on the same address can fast-path
    //     re-enable via the s_hookedAddr / s_originalPE check above.
    s_hookActive.store(false);

    LOG_INFO("GameThreadDispatch: hook flag cleared (physical hook retained "
             "to avoid in-flight unhook race)");
}

void Shutdown() {
    // Soft-disable the hook (audit fix #13).
    RemoveHook();

    // Drop any pending queued invokes so waiting pipe threads get a result
    // instead of blocking on a promise no one will ever fulfill.
    {
        std::lock_guard<std::mutex> lock(s_queueMutex);
        while (!s_invokeQueue.empty()) {
            auto req = std::move(s_invokeQueue.front());
            s_invokeQueue.pop();
            try {
                req->promise.set_value(-7); // hook not active
            } catch (...) {
                // promise already satisfied — ignore
            }
        }
        s_queueDepth.store(0, std::memory_order_release);
    }

    // Audit fix #14: do NOT call MH_Uninitialize. It patches every hooked
    // module's code page back and frees all trampolines — same in-flight
    // crash risk as MH_RemoveHook. MinHook's tables stay in memory; the
    // OS reclaims them on process exit.
    //
    // s_mhInitialized intentionally remains true so that a future re-init
    // path (currently not exercised) would skip MH_Initialize and reuse
    // existing state.
}

bool IsHookActive() {
    return s_hookActive.load();
}

int32_t EnqueueInvoke(uintptr_t instance, uintptr_t ufunc, uintptr_t params, size_t paramsSize) {
    if (!s_hookActive.load()) {
        return -7; // Hook not active
    }

    auto req = std::make_shared<InvokeRequest>();
    req->instance = instance;
    req->ufunc = ufunc;
    // Own a copy of the param bytes so a timed-out-but-still-queued request can
    // never dereference a freed caller buffer (use-after-free). When the caller
    // passes size 0 (a persistent buffer like Mimic's global), use the pointer
    // as-is — that buffer outlives the request.
    if (paramsSize > 0 && params != 0) {
        const auto* src = reinterpret_cast<const uint8_t*>(params);
        req->ownedParams.assign(src, src + paramsSize);
        req->params = reinterpret_cast<uintptr_t>(req->ownedParams.data());
    } else {
        req->params = params;
    }

    auto future = req->promise.get_future();

    {
        std::lock_guard<std::mutex> lock(s_queueMutex);
        // Push a COPY of the shared_ptr (refcount stays >=1 locally) so `req`
        // remains valid below for the out-param copy-back even after the game
        // thread drains and pops the queue entry.
        s_invokeQueue.push(req);
        s_queueDepth.fetch_add(1, std::memory_order_release);  // wake the hook's fast-path gate
    }

    LOG_INFO("GameThreadDispatch: enqueued invoke inst=0x%llX func=0x%llX, waiting...",
             (unsigned long long)instance, (unsigned long long)ufunc);

    // Wait for game thread to execute the request
    int32_t timeoutMs = s_invokeTimeoutMs.load();
    auto status = future.wait_for(std::chrono::milliseconds(timeoutMs));
    if (status == std::future_status::timeout) {
        LOG_ERROR("GameThreadDispatch: invoke timeout (%dms) inst=0x%llX func=0x%llX",
                  timeoutMs,
                  (unsigned long long)instance, (unsigned long long)ufunc);
        // The request stays queued. When the caller supplied a size (>0) the
        // request owns a COPY of the param bytes, so the eventual game-thread
        // execution reads stable data even after the caller's buffer is reused/
        // freed — and no copy-back runs on this timeout path, so the caller's
        // buffer is never clobbered after we return. Size-0 callers (a persistent
        // buffer whose CONTENT must also stay valid until drained) get no such
        // protection. We just abandon the (now stale) result.
        return -5;
    }

    int32_t result = future.get();
    // Propagate out-params written by the game thread back to the caller's buffer
    // (only when we owned a copy; the size-0 path wrote the caller's buffer directly).
    if (!req->ownedParams.empty() && params != 0) {
        memcpy(reinterpret_cast<void*>(params), req->ownedParams.data(), req->ownedParams.size());
    }
    LOG_INFO("GameThreadDispatch: invoke completed result=%d", result);
    return result;
}

// Public setter/getter for the invoke timeout. Clamped to a sane band so
// a misbehaving UI can't accidentally hang every UFunction call forever
// (or, conversely, set such a tight value that everything always times out).
void SetInvokeTimeoutMs(int32_t timeoutMs) {
    if (timeoutMs <= 0) {
        s_invokeTimeoutMs.store(kDefaultInvokeTimeoutMs);
        return;
    }
    if (timeoutMs < kMinInvokeTimeoutMs) timeoutMs = kMinInvokeTimeoutMs;
    if (timeoutMs > kMaxInvokeTimeoutMs) timeoutMs = kMaxInvokeTimeoutMs;
    s_invokeTimeoutMs.store(timeoutMs);
}

int32_t GetInvokeTimeoutMs() {
    return s_invokeTimeoutMs.load();
}

uintptr_t HookedAddress() {
    return s_hookedAddr;
}

bool HasOriginal() {
    return s_originalPE != nullptr;
}

bool InOwnPeCall() {
    return t_inOwnPeCall != 0;
}

int32_t CallOriginalSEH(uintptr_t instance, uintptr_t ufunc, uintptr_t params) {
    // The guard must outlive the call and is a C++ object, so it cannot share a
    // frame with __try -- hence the separate SEH helper below it.
    OwnPeCallGuard guard;
    return CallProcessEventSEH(instance, ufunc, params);
}

uint64_t GetHookFireCount() {
    return s_hookFireCount.load(std::memory_order_relaxed);
}

uint64_t MsSinceLastHookFire() {
    uint64_t last = s_lastHookFireMs.load(std::memory_order_relaxed);
    if (last == 0) return UINT64_MAX;   // never fired — liveness unknown
    uint64_t now = NowMs();
    return (now > last) ? (now - last) : 0;
}

// One decision, one place. The branches used to live here and the report used to be
// `!IsGameThreadResponsive()`, which collapsed "measured responsive" and "no hook, so
// cannot tell" into the same wire value. The classifier is pure and lives in Stark.h
// so every branch is unit-testable; this reads the atomics and hands them over.
// (STALLDEFAULT-2026-08-26)
GameThreadLiveness GetGameThreadLiveness(int32_t thresholdMs) {
    const uint64_t thr = (uint64_t)(thresholdMs > 0 ? thresholdMs : kStallThresholdMs);
    // NowMs() into a local: C++ leaves argument evaluation order unspecified, and the
    // classifier's saturating guards should not have to cover a clock read that
    // happened after the atomics it is compared against.
    const uint64_t now = NowMs();
    return ClassifyGameThreadLiveness(
        s_hookActive.load(std::memory_order_relaxed),
        s_lastHookFireMs.load(std::memory_order_relaxed),
        s_hookInstalledMs.load(std::memory_order_relaxed),
        now, thr);
}

bool IsGameThreadResponsive(int32_t thresholdMs) {
    // Contract UNCHANGED: unknown counts as responsive, because the caller's invoke
    // is what lazily installs the hook. See IsResponsiveFromLiveness in Stark.h.
    return IsResponsiveFromLiveness(GetGameThreadLiveness(thresholdMs));
}

} // namespace Stark
