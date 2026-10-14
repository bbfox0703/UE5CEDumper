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
#include "Lager.h"     // MinHook's trampoline-block search, modelled ([MH-PRERESERVE-PROXY])

#include <MinHook.h>
#include <Windows.h>
#include <intrin.h>   // _AddressOfReturnAddress -- the hook frame's stack position, for the trace's ticked scope

#include <atomic>
#include <chrono>
#include <cstring>
#include <future>
#include <memory>
#include <mutex>
#include <queue>
#include <string>
#include <vector>

// Lager models buffer.c without <Windows.h>; its constants must be the SDK's.
static_assert(Lager::kMemCommit == MEM_COMMIT && Lager::kMemReserve == MEM_RESERVE && Lager::kMemFree == MEM_FREE,
              "Lager's MEM_* states drifted from the SDK");
static_assert(Lager::kPageExecute == PAGE_EXECUTE && Lager::kPageExecuteRead == PAGE_EXECUTE_READ &&
              Lager::kPageExecuteReadWrite == PAGE_EXECUTE_READWRITE &&
              Lager::kPageExecuteWriteCopy == PAGE_EXECUTE_WRITECOPY && Lager::kPageGuard == PAGE_GUARD,
              "Lager's PAGE_* protections drifted from the SDK");
static_assert(Lager::kScnMemExecute == IMAGE_SCN_MEM_EXECUTE &&
              Lager::kDllCharDynamicBase == IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE,
              "Lager's PE constants drifted from the SDK");

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

// Every MinHook call Stark makes runs under this one mutex. It does NOT guard MinHook's own tables -- every MH_* call
// already holds MinHook's spin lock, and a second MH_CreateHook on one target simply returns MH_ERROR_ALREADY_CREATED.
// What it guards is STARK's state and its check-then-act: s_originalPE / s_hookedAddr / s_heldTarget / s_heldBlock and
// the "step the holder aside if ProcessEvent asks for its address" sequence, which InstallHook and the block holder
// must not interleave (audit #3).
static std::mutex s_mhMutex;

// [MH-PRERESERVE-PROXY] The held trampoline block (HoldTrampolineBlock). Written under s_mhMutex.
static uintptr_t s_heldTarget = 0;   // the holder hook's target: created, never enabled; 0 = nothing held
static uintptr_t s_heldBlock  = 0;   // the MinHook block the holder's slot sits in
static bool      s_holdTried  = false;

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

/// [A3-ST1-SUPER-DRAIN] The same SEH isolation for an arbitrary resolved ProcessEvent address
/// (a class's own override). Returns 0 on success, -3 on a null address, -4 on SEH exception.
static int32_t CallAddressSEH(uintptr_t peAddr, uintptr_t instance, uintptr_t ufunc, uintptr_t params) {
    if (!peAddr) return -3;
    typedef void (__fastcall *FnProcessEvent)(void*, void*, void*);
    __try {
        reinterpret_cast<FnProcessEvent>(peAddr)(
            reinterpret_cast<void*>(instance),
            reinterpret_cast<void*>(ufunc),
            reinterpret_cast<void*>(params));
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return -4;
    }
    return 0;
}

// True while THIS thread is executing inside Stark::CallOriginalSEH (audit #5
// ST1) or Stark::CallAddressAsOwnSEH ([A3-ST1-SUPER-DRAIN]). Deliberately NOT set inside CallProcessEventSEH: that is what the
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
static void HookedProcessEventBody(void* ufunc, uint64_t nowMs, Linie::ArmHint& hint) {
    // Live PE profiler (Linie): opt-in per-UFunction fire counting. The
    // not-recording path pays exactly one relaxed atomic load + a
    // predicted-not-taken branch; the mutex + map touch happen ONLY inside a
    // Start/Stop window. Mirrors the s_queueDepth "skip work unless armed" gate
    // below. `ufunc` is the UFunction* for this dispatch — stored raw; on its
    // first call of a recording Linie also reads its FName ints (loads only, no
    // string), and the names are decoded at read time, off the hot path.
    // [TRACE-UNLOADED-NAMES]
    if (Linie::IsRecording()) {
        // [LIVEFUNCS-STEP2] Its read of this call says what the address is armed for -- a tick or a snapshot
        // choice by name -- and the trace gets that on the same call.
        Linie::RecordCall(reinterpret_cast<uintptr_t>(ufunc), nowMs, &hint);
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

    // [LIVEFUNCS-TIMELINE-2026-10-04] The call trace's entry record is written after the body. The invokes the body
    // drained call the original ProcessEvent through the trampoline, not this hook, so they are not in the trace
    // themselves; the calls they make come back through the hook and are, ahead of this call, as they ran (review
    // DLL-7). The token stays in this frame across the game's call; this frame's stack address is how a ticked scope
    // tells a nested call from a later one.
    Linie::TraceToken traceTok;
    Linie::ArmHint armHint;   // [LIVEFUNCS-STEP2] integers only, in this frame
    const uintptr_t traceSp = reinterpret_cast<uintptr_t>(_AddressOfReturnAddress());

    // Our work, contained. See HookedProcessEventBody — a throw escaping here fast-fails
    // the game, because the caller is the game itself.
    Routine::RunThreadGuarded("GameThreadDispatch", [&] {
        HookedProcessEventBody(ufunc, nowMs, armHint);
        if (Linie::IsTracing()) {
            Linie::TraceEnter(reinterpret_cast<uintptr_t>(ufunc), reinterpret_cast<uintptr_t>(thisObj), traceSp,
                              GetCurrentThreadId(), traceTok, reinterpret_cast<uintptr_t>(params), armHint);
        }
    });

    // Now handle the game's own ProcessEvent call. OUTSIDE the guard on purpose: this is
    // the game's own code and its exceptions are its own business — swallowing one here
    // would silently change the game's behaviour, which is far worse than what the guard
    // above prevents.
    if (s_originalPE) {
        s_originalPE(thisObj, ufunc, params);
    }

    // The return record: our work again, so guarded again (TR3). A game exception that unwinds past this frame skips
    // it, and the trace shows that call as never returning; Linie closes a ticked scope left open that way itself.
    if (traceTok.traced) {
        Routine::RunThreadGuarded("GameThreadDispatch", [&] {
            // The same block again: the copy after the call reads the out parameters and the return value there.
            Linie::TraceReturn(traceTok, GetCurrentThreadId(), reinterpret_cast<uintptr_t>(params));
        });
    }
}

// ---- MinHook's trampoline window ([MH-PRERESERVE-PROXY]) ----

// The copy faults when a page under it cannot be read; SEH needs a frame with no C++ objects (C2712).
static bool CopyBytesSEH(const void* src, void* dst, size_t len) {
    __try {
        memcpy(dst, src, len);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
    return true;
}

// The main image's PE layout, read from its mapped headers. A header page that cannot be read gives an invalid
// layout, which the summary reports as unknown rather than guessing.
static Lager::ImageLayout ReadMainImageLayout() {
    const auto* base = reinterpret_cast<const uint8_t*>(GetModuleHandleW(nullptr));
    uint8_t hdr[0x1000];
    if (!base || !CopyBytesSEH(base, hdr, sizeof(hdr))) {
        Lager::ImageLayout img;
        img.base = reinterpret_cast<uint64_t>(base);
        return img;
    }
    return Lager::ParseImageLayout(hdr, sizeof(hdr), reinterpret_cast<uint64_t>(base));
}

// A VirtualQuery was measured at about 300 µs on a game running uncapped (Aura.h, CodeRangeCache), and a heap grown
// through the window can be thousands of regions. The walk runs on a pipe thread the user is waiting on, so it stops
// at this budget, and the summary then says its map is incomplete.
static constexpr uint64_t kWindowWalkBudgetMs = 2000;

// One failing Start fails twice (the automatic attempt, then the forced one), each walking the same window. Skip a
// re-walk for the same target within this window so one Start costs at most one walk, not two held under s_mhMutex.
static constexpr uint64_t kWindowLogThrottleMs = 5000;

// The regions VirtualQuery reports over [lo, endExclusive), in address order: the map Lager models MinHook's walks on.
static std::vector<Lager::Region> WalkRegions(uint64_t lo, uint64_t endExclusive) {
    std::vector<Lager::Region> map;
    const uint64_t deadline = GetTickCount64() + kWindowWalkBudgetMs;
    uint64_t addr = lo & ~static_cast<uint64_t>(0xFFF);
    while (addr < endExclusive) {
        MEMORY_BASIC_INFORMATION mbi{};
        if (VirtualQuery(reinterpret_cast<LPCVOID>(addr), &mbi, sizeof(mbi)) == 0) break;
        Lager::Region r;
        r.base           = reinterpret_cast<uint64_t>(mbi.BaseAddress);
        r.size           = mbi.RegionSize;
        r.state          = mbi.State;
        r.allocationBase = reinterpret_cast<uint64_t>(mbi.AllocationBase);
        map.push_back(r);
        const uint64_t next = r.base + r.size;
        if (next <= addr || GetTickCount64() > deadline) break;
        addr = next;
    }
    return map;
}

// MH_CreateHook answers MH_ERROR_MEMORY_ALLOC and nothing more. Walk the window GetMemoryBlock searched for `target`
// and log what held it, so a failure on a live game says whether the space was taken, by what, and where the main
// image sits -- the evidence [MH-PRERESERVE-PROXY] was proposed without. `lastError` is GetLastError() captured right
// after MH_CreateHook: when the window still had room, it is what distinguishes a VirtualAlloc/heap refusal from a
// full window.
static void LogTrampolineWindow(uintptr_t target, uint32_t lastError) {
    // Throttle: a Start fails twice and would walk twice. Called only from InstallHook, which holds s_mhMutex, so
    // this static state needs no lock of its own.
    static uintptr_t s_lastWindowLogTarget = 0;
    static uint64_t  s_lastWindowLogMs     = 0;
    const uint64_t nowMs = GetTickCount64();
    if (target == s_lastWindowLogTarget && nowMs - s_lastWindowLogMs < kWindowLogThrottleMs) {
        LOG_WARN("GameThreadDispatch: MinHook block window for 0x%llX not re-walked -- its map was logged %llu ms ago",
                 (unsigned long long)target, (unsigned long long)(nowMs - s_lastWindowLogMs));
        return;
    }
    s_lastWindowLogTarget = target;
    s_lastWindowLogMs     = nowMs;

    SYSTEM_INFO si{};
    GetSystemInfo(&si);
    const uint64_t appMin = reinterpret_cast<uint64_t>(si.lpMinimumApplicationAddress);
    const uint64_t appMax = reinterpret_cast<uint64_t>(si.lpMaximumApplicationAddress);
    const Lager::Window w = Lager::SearchWindow(target, appMin, appMax);
    const uint64_t t0 = GetTickCount64();
    const std::vector<Lager::Region> map = WalkRegions(w.minAddr, w.maxAddr + Lager::kMemoryBlockSize);
    const uint64_t walkMs = GetTickCount64() - t0;
    const Lager::WindowSummary summary =
        Lager::SummarizeWindow(map, target, appMin, appMax, si.dwAllocationGranularity);
    const std::string line = Lager::FormatWindowSummary(summary, target, ReadMainImageLayout(), s_heldBlock, lastError);
    LOG_ERROR("GameThreadDispatch: %s (%zu region(s) walked in %llu ms)", line.c_str(), map.size(),
              (unsigned long long)walkMs);
}

// MinHook's own state (its private heap) is the one record of whether it is initialised, shared by every module in
// this DLL that hooks: each accepts MH_ERROR_ALREADY_INITIALIZED (the holder is usually the first initialiser now, so
// Grausam's later call almost always lands on that branch -- InvokeScriptTests pins that every call site ORs it in),
// and none calls MH_Uninitialize, which would free the held block and every live trampoline with it (pinned too).
// Call under s_mhMutex.
static bool EnsureMinHookLocked() {
    if (s_mhInitialized.load()) return true;
    const MH_STATUS status = MH_Initialize();
    if (status != MH_OK && status != MH_ERROR_ALREADY_INITIALIZED) {
        LOG_ERROR("GameThreadDispatch: MH_Initialize failed: %s", MH_StatusToString(status));
        return false;
    }
    s_mhInitialized.store(true);
    return true;
}

// MH_CreateHook refuses a detour that is not executable (IsExecutableAddress), so the holder needs one. It never
// runs: the holder is created and never enabled, and nothing in this DLL enables every hook at once
// (InvokeScriptTests pins both).
static void HolderDetour() {}

// May MinHook read `target`? It reads a hook target with no SEH, inside its spin lock, so a fault there would end
// the game and a caught one would leave the lock taken for every later MinHook call. It decodes past the prologue,
// so the guard covers target .. target + kHolderReadSpan - 1 (Lager derives the span). Lager decides from the page
// protections; the SEH copy then proves the bytes are really there.
static bool HolderCandidateSafe(uintptr_t target) {
    MEMORY_BASIC_INFORMATION at{}, end{}, before{};
    if (!VirtualQuery(reinterpret_cast<LPCVOID>(target), &at, sizeof(at)) ||
        !VirtualQuery(reinterpret_cast<LPCVOID>(target + Lager::kHolderReadSpan - 1), &end, sizeof(end)) ||
        !VirtualQuery(reinterpret_cast<LPCVOID>(target - 5), &before, sizeof(before))) {
        return false;
    }
    if (!Lager::HolderCandidateReadable(at.State, at.Protect, end.State, end.Protect, before.State, before.Protect))
        return false;
    uint8_t probe[Lager::kHolderReadSpan];
    return CopyBytesSEH(reinterpret_cast<const void*>(target), probe, sizeof(probe));
}

// ---- Public API ----

void HoldTrampolineBlock() {
    std::lock_guard<std::mutex> lock(s_mhMutex);
    if (s_holdTried) return;   // once per process (per copy of this DLL: each copy links its own MinHook)
    s_holdTried = true;
    if (!EnsureMinHookLocked()) {
        LOG_WARN("GameThreadDispatch: no MinHook trampoline block held -- MinHook did not initialise; "
                 "nothing else changes");
        return;
    }

    SYSTEM_INFO si{};
    GetSystemInfo(&si);
    const uint64_t appMin = reinterpret_cast<uint64_t>(si.lpMinimumApplicationAddress);
    const uint64_t appMax = reinterpret_cast<uint64_t>(si.lpMaximumApplicationAddress);
    const Lager::ImageLayout img = ReadMainImageLayout();
    std::string refused;
    for (const Lager::HolderCandidate& c : Lager::ChooseHolderTargets(img)) {
        const uintptr_t target = static_cast<uintptr_t>(c.address);
        char why[160];
        if (target == s_hookedAddr) {   // a real hook already owns this address
            std::snprintf(why, sizeof(why), "0x%llX: a real hook's target; ", (unsigned long long)target);
            refused += why;
            continue;
        }
        if (!HolderCandidateSafe(target)) {
            std::snprintf(why, sizeof(why), "0x%llX: not readable code; ", (unsigned long long)target);
            refused += why;
            continue;
        }
        LPVOID trampoline = nullptr;
        const MH_STATUS status =
            MH_CreateHook(reinterpret_cast<LPVOID>(target), reinterpret_cast<LPVOID>(&HolderDetour),
                          &trampoline);
        if (status != MH_OK) {
            std::snprintf(why, sizeof(why), "0x%llX: %s; ", (unsigned long long)target, MH_StatusToString(status));
            refused += why;
            continue;
        }
        // FreeBuffer finds a slot's block the same way: the slot address rounded down to MEMORY_BLOCK_SIZE.
        s_heldTarget = target;
        s_heldBlock  = static_cast<uintptr_t>(Lager::BlockOfSlot(reinterpret_cast<uintptr_t>(trampoline)));
        const Lager::Span served = Lager::OriginsServedBy(s_heldBlock, appMin, appMax);
        const Lager::ExecRange code = Lager::ExecutableRange(img);
        const bool covers = Lager::SpanCovers(served, code);
        const char* fmt =
            "GameThreadDispatch: held a MinHook trampoline block for the process lifetime: block 0x%llX, taken "
            "by a hook created (never enabled) on 0x%llX, %s; it serves hook targets [0x%llX, 0x%llX], %s the "
            "image's executable range [0x%llX, 0x%llX) (image 0x%llX + 0x%X, DYNAMICBASE %s)";
        const char* verdict = covers ? "which covers" : "which does NOT cover -- a hook outside it needs a block of "
                                                        "its own --";
        if (covers) {
            LOG_INFO(fmt, (unsigned long long)s_heldBlock, (unsigned long long)target, Lager::HolderKindName(c.kind),
                     (unsigned long long)served.lo, (unsigned long long)served.hi, verdict,
                     (unsigned long long)code.lo, (unsigned long long)code.hi, (unsigned long long)img.base,
                     img.sizeOfImage, img.dynamicBase ? "yes" : "no");
        } else {
            LOG_WARN(fmt, (unsigned long long)s_heldBlock, (unsigned long long)target, Lager::HolderKindName(c.kind),
                     (unsigned long long)served.lo, (unsigned long long)served.hi, verdict,
                     (unsigned long long)code.lo, (unsigned long long)code.hi, (unsigned long long)img.base,
                     img.sizeOfImage, img.dynamicBase ? "yes" : "no");
        }
        return;
    }
    LOG_WARN("GameThreadDispatch: no MinHook trampoline block held (%s) -- nothing else changes: a hook allocates "
             "its own block as before",
             refused.empty() ? "the main image's headers named no candidate" : refused.c_str());
}

bool InstallHook(uintptr_t processEventAddr) {
    // Serialize the whole check-then-MH_CreateHook/MH_EnableHook sequence. Not for MinHook's
    // sake -- its spin lock guards its own tables and a duplicate create returns
    // MH_ERROR_ALREADY_CREATED -- but because this reads and writes Stark's own state
    // (s_originalPE / s_hookedAddr / s_heldTarget / s_heldBlock) and the holder step-aside,
    // and the Frieren-side call_once only covers the game-thread dispatch path, so a concurrent
    // (or future) public caller must still see a consistent view (audit #3). A second caller
    // blocks here, then hits the s_hookActive / s_hookedAddr fast-paths below and no-ops. The
    // block holder takes the same mutex.
    std::lock_guard<std::mutex> lock(s_mhMutex);

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

    if (!EnsureMinHookLocked()) return false;

    // [MH-PRERESERVE-PROXY] A real hook outranks the holder. MinHook keys hooks by target, so ProcessEvent at the
    // holder's address would fail MH_ERROR_ALREADY_CREATED. The holder was never enabled, so removing it writes
    // nothing to the target (MH_RemoveHook restores code only for an enabled hook); its block goes with it when it
    // was the last slot in use.
    if (s_heldTarget != 0 && s_heldTarget == processEventAddr) {
        const MH_STATUS removed = MH_RemoveHook(reinterpret_cast<LPVOID>(s_heldTarget));
        LOG_WARN("GameThreadDispatch: ProcessEvent 0x%llX is the held block's holder target -- holder removed (%s) "
                 "so the real hook can take the address", (unsigned long long)processEventAddr,
                 MH_StatusToString(removed));
        s_heldTarget = 0;
        s_heldBlock  = 0;
    }
    // Say before the attempt when the held block cannot serve ProcessEvent: MinHook then needs a block of its own.
    if (s_heldBlock != 0) {
        SYSTEM_INFO si{};
        GetSystemInfo(&si);
        const uint64_t appMin = reinterpret_cast<uint64_t>(si.lpMinimumApplicationAddress);
        const uint64_t appMax = reinterpret_cast<uint64_t>(si.lpMaximumApplicationAddress);
        if (!Lager::BlockServes(s_heldBlock, Lager::SearchWindow(processEventAddr, appMin, appMax))) {
            LOG_WARN("GameThreadDispatch: the held block 0x%llX is out of reach of ProcessEvent 0x%llX "
                     "(MinHook reuses a block only within +-1 GB) -- it must find a block of its own",
                     (unsigned long long)s_heldBlock, (unsigned long long)processEventAddr);
        }
    }

    // Create hook. Clear LastError first and read it back immediately: on MH_ERROR_MEMORY_ALLOC it is the only thing
    // that separates a full window from a VirtualAlloc/heap refusal, and the LOG_ERROR below would overwrite it.
    SetLastError(0);
    MH_STATUS status = MH_CreateHook(
        reinterpret_cast<LPVOID>(processEventAddr),
        reinterpret_cast<LPVOID>(&HookedProcessEvent),
        reinterpret_cast<LPVOID*>(&s_originalPE));
    const DWORD mhLastError = GetLastError();

    if (status != MH_OK) {
        LOG_ERROR("GameThreadDispatch: MH_CreateHook failed: %s",
                  MH_StatusToString(status));
        if (status == MH_ERROR_MEMORY_ALLOC) LogTrampolineWindow(processEventAddr, mhLastError);
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
    // [MH-PRERESERVE-PROXY] Where its trampoline went: the evidence that the held block did its job.
    const uintptr_t peBlock = static_cast<uintptr_t>(Lager::BlockOfSlot(reinterpret_cast<uintptr_t>(s_originalPE)));
    if (s_heldBlock != 0) {
        LOG_INFO("GameThreadDispatch: ProcessEvent's trampoline 0x%llX is %s the held block 0x%llX",
                 (unsigned long long)reinterpret_cast<uintptr_t>(s_originalPE),
                 peBlock == s_heldBlock ? "in" : "NOT in", (unsigned long long)s_heldBlock);
    } else {
        LOG_INFO("GameThreadDispatch: ProcessEvent's trampoline 0x%llX is in block 0x%llX (no block was held)",
                 (unsigned long long)reinterpret_cast<uintptr_t>(s_originalPE), (unsigned long long)peBlock);
    }
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

// [A3-ST1-SUPER-DRAIN] A class's own ProcessEvent override, called under the same "inside our own PE
// call" mark. The override is not the end of the call: AActor::ProcessEvent calls Super::ProcessEvent,
// which IS the address MinHook patched, so an unmarked call re-entered our detour off the game thread
// and its drain ran every queued request there.
int32_t CallAddressAsOwnSEH(uintptr_t peAddr, uintptr_t instance, uintptr_t ufunc, uintptr_t params) {
    // The guard in THIS (outer) frame, the __try in CallAddressSEH's (MSVC C2712), as CallOriginalSEH.
    OwnPeCallGuard guard;
    return CallAddressSEH(peAddr, instance, ufunc, params);
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
