// ============================================================
// Stark — 修塔爾克 (勇者戰士 — Brave Warrior)
// GameThreadDispatch: MinHook ProcessEvent hook + game-thread queue
//
// Architecture:
//   Pipe thread calls EnqueueInvoke() → pushes request to queue
//   Game thread's ProcessEvent hook drains queue → executes requests
//   Result returned via std::future (blocks pipe thread until done)
//
// This ensures ProcessEvent is always called from the game thread,
// which UE expects for state-changing operations (UI, rendering,
// spawning actors, etc.).
// ============================================================
#pragma once

#include <atomic>
#include <chrono>
#include <cstdint>
#include <future>
#include <string>

namespace Stark {

/// Install the ProcessEvent hook at the given address.
/// @param processEventAddr  Absolute address of UObject::ProcessEvent
/// @return true if hook installed successfully
bool InstallHook(uintptr_t processEventAddr);

/// [MH-PRERESERVE-PROXY] Take a MinHook trampoline block next to the main image now, and keep it for the process
/// lifetime. MinHook reuses a block that serves a new target before it allocates one, so the ProcessEvent hook later
/// lands in this block instead of needing a free block start within +-1 GB of ProcessEvent at install time -- which
/// a heap grown through that window can deny on an image without ASLR (FINAL FANTASY RESONANCE DEMO). The block is
/// held by a hook created on the image's entry point (else an executable section's start) and never enabled, so no
/// code of the game is written. Call from a thread, never from DllMain (MinHook under the loader lock). Once per
/// process; what was held and where is logged, and a failure is logged and changes nothing else.
void HoldTrampolineBlock();

/// Soft-disable the hook: clear the active flag and leave MinHook's patch and trampoline in place, because unhooking
/// under a game thread inside the trampoline crashes. Safe to call even if not installed.
void RemoveHook();

/// Teardown (UE5_Shutdown): RemoveHook, then answer every queued invoke with -7. MinHook is deliberately left
/// initialised: MH_Uninitialize would unpatch and free every trampoline under an in-flight call, and the held block
/// with them. InstallHook still works afterwards.
void Shutdown();

/// @return true if the ProcessEvent hook is active
bool IsHookActive();

/// Enqueue a ProcessEvent invocation for game-thread execution.
/// Blocks until the game thread executes it (or timeout).
///
/// @param instance    UObject instance pointer
/// @param ufunc       UFunction pointer
/// @param params      Parameter buffer pointer (already allocated/written by caller)
/// @param paramsSize  Bytes to COPY into the request so it owns its buffer. Pass
///                    the buffer's own size (Ubel::ParamBufferSize, never less than
///                    the UFunction's ParmsSize) when the caller's buffer is
///                    transient (the common case — prevents a use-after-free if
///                    the invoke times out but is later drained by the game
///                    thread). Pass 0 only when `params` outlives the request AND
///                    nothing rewrites it meanwhile: Mimic's mailbox does not
///                    qualify, because CE rewrites it with its next command.
/// @param dropIfAbandoned  [VND584-01] see ClaimRequest: on a timeout the request never runs later.
/// @param queuedOut  [DBGCAM-OFF-OVER-QUEUED-ON] on a timeout that leaves the request queued, receives its completion,
///                   for a caller that must know when it has run (QueuedInvoke). Untouched otherwise.
/// @return 0 on success, -4 if SEH exception, -5 if timeout, -7 if hook not active
int32_t EnqueueInvoke(uintptr_t instance, uintptr_t ufunc, uintptr_t params, size_t paramsSize,
                      bool dropIfAbandoned = false, std::shared_future<int32_t>* queuedOut = nullptr);

/// [W3-DEBUGCAM-QUEUED] EnqueueInvoke's -5: the game-thread dispatch TIMED OUT and the request STAYS QUEUED -- it still
/// runs when the game thread next drains. Mirrored UI-side as Constants.InvokeDispatchTimeoutResult.
constexpr int32_t kInvokeTimedOutStillQueued = -5;
/// [W3-DEBUGCAM-QUEUED] What a STATEFUL toggle export returns when its queued call did not return 0. A timed-out call
/// passes through as kInvokeTimedOutStillQueued: the toggle stays queued and runs when the game thread next drains, so
/// the caller reports it as queued, never as a failure that ran nothing. What that means for the user is the toggle's
/// own (UE5_SetDebugCamera's doc in Frieren.h). Every other failure ran nothing: -1.
constexpr int32_t StatefulToggleFailure(int32_t callResult) {
    return callResult == kInvokeTimedOutStillQueued ? kInvokeTimedOutStillQueued : -1;
}

/// Default invoke timeout (compile-time baseline, used when no override is set).
constexpr int32_t kDefaultInvokeTimeoutMs = 5000;
/// Clamp band for a user-supplied invoke timeout (100ms .. 10min). Enforced by
/// SetInvokeTimeoutMs and mirrored by Fern's set_invoke_timeout pipe validation.
constexpr int32_t kMinInvokeTimeoutMs = 100;
constexpr int32_t kMaxInvokeTimeoutMs = 600000;

/// Override the EnqueueInvoke timeout in milliseconds. Set to 0 to revert to
/// kDefaultInvokeTimeoutMs. Clamped to [100, 600000] (100ms .. 10min). Thread-safe.
void SetInvokeTimeoutMs(int32_t timeoutMs);

/// Read the current invoke timeout in milliseconds (post-clamping).
int32_t GetInvokeTimeoutMs();

/// Total number of times HookedProcessEvent has fired since this process
/// started. Used by post-install validation (build 648+) to confirm the
/// hook was actually placed on UObject::ProcessEvent and not on an
/// adjacent UObject virtual: a real PE hook fires many times per second
/// during normal gameplay, a wrong hook fires 0 times. Thread-safe.
uint64_t GetHookFireCount();

/// Gap (ms) beyond which — once the PE hook has fired at least once — the game
/// thread is considered "stalled": not ticking ProcessEvent (paused / suspended
/// / alt-tab-throttled). A live game fires PE hundreds of times per frame, so
/// even a few-FPS game stays far under this; only a genuine stop crosses it.
constexpr int32_t kStallThresholdMs = 500;

/// Milliseconds since HookedProcessEvent last fired, or UINT64_MAX when it has
/// never fired (hook not installed yet, or installed but the game thread has
/// not reached it — liveness unknown). Thread-safe.
uint64_t MsSinceLastHookFire();

/// @return true if the game thread appears to be ticking (draining our PE
/// hook): either no fire has been observed yet (unknown → give a fresh hook a
/// chance) or the last fire was within thresholdMs. False ONLY once the hook
/// has fired and then gone quiet past the threshold — i.e. the game is
/// paused/suspended. Callers use this to skip game-thread invokes that would
/// otherwise block for the full invoke timeout on a thread that will not run,
/// and to surface a "game paused" hint. thresholdMs<=0 uses kStallThresholdMs.
/// Thread-safe.
bool IsGameThreadResponsive(int32_t thresholdMs = kStallThresholdMs);

// ---- Three states, because "responsive" and "cannot tell" are different facts ----
//
// IsGameThreadResponsive above answers a GATE question ("should I attempt a
// game-thread invoke?") and deliberately maps the unknown case to true, because
// "no hook yet" means "try -- the attempt is what installs the hook". That is
// correct for the eight in-DLL gates and MUST NOT change.
//
// It is wrong as a REPORT. Every response envelope carried
// `game_thread_stalled: false`, which asserted a healthy game thread that nobody
// had measured -- the normal state of a fresh connection, because the hook
// installs lazily on the first invoke. (STALLDEFAULT-2026-08-26)
enum class GameThreadLiveness : uint8_t { Unknown, Responsive, Stalled };

/// Pure classifier -- state comes in as parameters so a header-only test target can
/// drive every branch; Stark.cpp supplies the atomics. All times are Stark's
/// steady-clock milliseconds; 0 means "never".
inline GameThreadLiveness ClassifyGameThreadLiveness(
        bool hookActive, uint64_t lastFireMs, uint64_t installedMs,
        uint64_t nowMs, uint64_t thresholdMs) {
    if (!hookActive) return GameThreadLiveness::Unknown;
    if (lastFireMs != 0)
        return ((nowMs > lastFireMs ? nowMs - lastFireMs : 0) <= thresholdMs)
                   ? GameThreadLiveness::Responsive : GameThreadLiveness::Stalled;
    // Hook active but never fired. Inside the grace window we still cannot tell;
    // past it, the game thread was already paused when we hooked.
    if (installedMs == 0) return GameThreadLiveness::Unknown;   // active, no install stamp
    return ((nowMs > installedMs ? nowMs - installedMs : 0) <= thresholdMs)
               ? GameThreadLiveness::Unknown
               : GameThreadLiveness::Stalled;
}

/// Unknown counts as RESPONSIVE. This is the gate contract, preserved exactly:
/// Dunste (noclip), Schlacht (see-through) and Wirbel all ask "should I try?", and
/// mapping Unknown to false would mean See-through never traces, the Noclip pawn
/// stays ghosted, and POV silently degrades -- forever, on a game whose hook has
/// not installed yet. DO NOT change this to `== Responsive`.
inline bool IsResponsiveFromLiveness(GameThreadLiveness l) {
    return l != GameThreadLiveness::Stalled;
}

/// The measured verdict, for REPORTS. thresholdMs<=0 uses kStallThresholdMs.
/// Thread-safe.
GameThreadLiveness GetGameThreadLiveness(int32_t thresholdMs = kStallThresholdMs);

// ============================================================
// Calling ProcessEvent OURSELVES without re-entering our own detour
// (audit #5 ST1)
// ============================================================
//
// MinHook patches the PROLOGUE of UObject::ProcessEvent. So any caller of ours
// that resolves the address out of the vtable and calls it lands in
// HookedProcessEvent -- on whatever thread it happened to be running. That is
// how a pipe lane or the Mimic polling thread ends up executing the drain, whose
// only gate is "is the queue non-empty". The requests it then runs are precisely
// the ones a caller judged UNSAFE off the game thread; that is why they were
// queued instead of called directly.
//
// The reachability is not a tight race. An invoke that times out stays queued
// with its own owned parameter copy, deliberately, expecting a later drain -- so
// after one timeout the window is open indefinitely, and the next self-issued
// direct call executes that abandoned stateful UFunction on the wrong thread.
//
// For OUR calls the fix is the TRAMPOLINE we are holding, as Grausam does for its
// own hooks: a call through it never enters the detour. A fail-open call to an
// OVERRIDE does (its Super::ProcessEvent is the patched address), so it runs under
// the own-PE-call mark instead: CallAddressAsOwnSEH ([A3-ST1-SUPER-DRAIN]).
//
// The ENGINE's worker threads call ProcessEvent too, and no trampoline covers
// them: on UE 5.8 a drain there ran a queued ToggleDebugCamera into an
// IsInGameThread check [STARK-DRAIN-OFF-GAME-THREAD]. That needs the game thread's
// identity after all. A guessed identity is the risk this note used to warn of --
// a gate on the wrong thread never drains, which times out every invoke and is
// worse than the defect -- so the identity is not a guess where UE states it (the
// thread it names "GameThread"), and where it is a guess (the main thread) the
// install validator gives the gate up unless that thread is seen firing
// (PickGameThread / KeepGameThreadGate below).

/// Address MinHook actually patched, or 0 if no hook is installed.
uintptr_t HookedAddress();

/// @return true when the original-function trampoline is available to call.
bool HasOriginal();

/// Call the ORIGINAL ProcessEvent through MinHook's trampoline, bypassing our
/// detour entirely, with the same SEH protection the queued path uses.
/// Returns 0 on success, -3 if no trampoline, -4 on an SEH exception.
///
/// Marks the calling thread as "inside our own PE call" for its duration, so a
/// nested dispatch that DOES re-enter the detour is recognised as ours.
int32_t CallOriginalSEH(uintptr_t instance, uintptr_t ufunc, uintptr_t params);

/// True while this thread is inside CallOriginalSEH or CallAddressAsOwnSEH.
bool InOwnPeCall();

/// [A3-ST1-SUPER-DRAIN] Call a resolved ProcessEvent address that is NOT the hooked one -- a class's own
/// override -- with the same SEH protection and the same "inside our own PE call" mark as CallOriginalSEH.
/// An override (every AActor's) calls Super::ProcessEvent, which IS the patched address, so an unmarked
/// call re-entered our detour off the game thread and drained the invoke queue there.
/// Returns 0 on success, -3 on a null address, -4 on an SEH exception.
int32_t CallAddressAsOwnSEH(uintptr_t peAddr, uintptr_t instance, uintptr_t ufunc, uintptr_t params);

/// Should a caller that resolved `resolvedPeAddr` out of an instance's vtable
/// route through the trampoline instead of calling that address directly?
///
/// The `resolvedPeAddr == hookedAddr` term is load-bearing, not belt-and-braces:
/// a class that genuinely OVERRIDES ProcessEvent has a different slot, that slot
/// was never patched, and calling the trampoline for it would silently run the
/// BASE implementation instead of the override. When the two differ we fail open
/// to the caller's own address, which is the correct one.
inline bool ShouldUseTrampoline(uintptr_t resolvedPeAddr,
                                uintptr_t hookedAddr,
                                bool haveOriginal) {
    return haveOriginal && hookedAddr != 0 && resolvedPeAddr == hookedAddr;
}

/// Should HookedProcessEvent drain the queue on this entry?
///
/// `entryIsOurs` is true when this thread is inside our own CallOriginalSEH or
/// CallAddressAsOwnSEH --
/// i.e. the detour was re-entered by a nested dispatch underneath a call WE
/// issued, which is not a game-thread tick and must not drain. This is the
/// shipped gate, not a mirror of it: HookedProcessEventBody calls exactly this.
inline bool ShouldDrainQueue(size_t queueDepth, bool entryIsOurs) {
    return queueDepth != 0 && !entryIsOurs;
}

// ---- [STARK-DRAIN-OFF-GAME-THREAD] Which thread is the game thread ----

/// How the game thread was identified.
enum class GameThreadIdSource : uint8_t { Unknown, Description, MainThread };

/// One of the process's threads, as the identification sees it.
struct ThreadInfo {
    uint32_t tid;
    bool     namedGameThread;   // its OS thread description is "GameThread"
    uint64_t creationTime;      // FILETIME as a 64-bit count; earlier is smaller
};

struct GameThreadPick {
    uint32_t           tid;
    GameThreadIdSource source;
};

/// The game thread among `threads`: the one UE named "GameThread" -- FPlatformProcess::SetupGameThread names the
/// thread FEngineLoop::PreInit has just stored as GGameThreadId -- and failing a name (an engine or a Windows that
/// does not set thread descriptions), the earliest-created thread: GuardedMain's, the main thread PreInit records.
/// Among several named ones (not expected), the earliest-created of them.
inline GameThreadPick PickGameThread(const ThreadInfo* threads, size_t n) {
    GameThreadPick named{ 0, GameThreadIdSource::Unknown }, first{ 0, GameThreadIdSource::Unknown };
    uint64_t namedTime = 0, firstTime = 0;
    for (size_t i = 0; i < n; ++i) {
        const ThreadInfo& t = threads[i];
        if (t.namedGameThread && (named.tid == 0 || t.creationTime < namedTime)) {
            named = { t.tid, GameThreadIdSource::Description };
            namedTime = t.creationTime;
        }
        if (first.tid == 0 || t.creationTime < firstTime) {
            first = { t.tid, GameThreadIdSource::MainThread };
            firstTime = t.creationTime;
        }
    }
    return named.tid != 0 ? named : first;
}

/// Is a hook entry on thread `tid` a game-thread entry, given the identified `gameThreadId`? 0 -- nothing identified,
/// or the gate given up -- counts every entry, as before the gate existed.
inline bool IsGameThreadEntry(uint32_t tid, uint32_t gameThreadId) {
    return gameThreadId == 0 || tid == gameThreadId;
}

/// Should HookedProcessEvent drain the queue on this entry? An engine worker thread calls ProcessEvent too, and a
/// request queued for the game thread must not run there: it leaves the work for the next game-thread entry.
inline bool ShouldDrainQueue(size_t queueDepth, bool entryIsOurs, bool onGameThread) {
    return onGameThread && ShouldDrainQueue(queueDepth, entryIsOurs);
}

/// After the install validation window: keep the game-thread gate, or give it up? A NAMED game thread is the engine's
/// own GGameThreadId and is kept even when silent -- a paused game thread, where invokes wait as on any paused game.
/// The main-thread GUESS is given up when it stayed silent while other threads fired: a gate on the wrong thread
/// never drains and times out every invoke, which is worse than the defect it fixes. Nothing fired at all is an idle
/// game, not evidence against the guess.
inline bool KeepGameThreadGate(GameThreadIdSource source, uint64_t gameThreadFires, uint64_t allFires) {
    switch (source) {
        case GameThreadIdSource::Description: return true;
        case GameThreadIdSource::MainThread:  return gameThreadFires > 0 || allFires == 0;
        default:                              return false;
    }
}

/// The identified game thread's id; 0 when none is, or the gate was given up. Thread-safe.
uint32_t GameThreadId();

/// How the game thread was identified (Unknown when it is not). Thread-safe.
GameThreadIdSource GameThreadSource();

/// Hook fires on the identified game thread only (GetHookFireCount counts every thread). Thread-safe.
uint64_t GetGameThreadFireCount();

/// The install validator's verdict for the window it just measured: gives the gate up as KeepGameThreadGate says.
void ApplyGameThreadValidation(uint64_t gameThreadFires, uint64_t allFires);

/// [VND584-01] A queued request its waiter may abandon. A timed-out request normally stays queued and runs later from
/// its own copy of the parameters; for a helper call made from a value that may not outlive the wait -- the object an
/// invoke's caller picked, which the engine call dereferences -- a late run could touch a freed object. Such a request
/// either is already running when its waiter gives up (the waiter then waits it out and takes its result) or never
/// runs. The drain and the waiter race on one compare-exchange out of Queued; whichever moves it first decides.
/// [VND584-05] clang narrows `test dword ptr [reg+disp32], imm32` whose immediate has a single set byte to
/// `test byte ptr [reg+disp32+k], imm8`. Avowed (clang, no Rich header) tests FUNC_Native as `F6 82 B1 00 00 00 04`
/// and the 0x400000 flag as `F6 83 B2 00 00 00 40` -- FunctionFlags at +0xB0 both times -- so the dword patterns
/// never match and every run fell back to the stock version table. The FunctionFlags offset such a body tests, when
/// a FUNC_Native byte test (imm8 0x04 at FunctionFlags+1, within `p1Limit`) and a 0x400000 byte test (imm8 0x40 at
/// FunctionFlags+2, anywhere) both imply the SAME offset; -1 otherwise. One lone `test byte [reg+disp],4` is too
/// common to mean anything; two agreeing on one field are the fingerprint.
inline int NarrowedFlagTestsFunctionFlags(const uint8_t* body, size_t size, size_t p1Limit) {
    // One `test byte ptr [reg+disp32], imm8` ending by `limit`: F6, ModRM mod=10 reg=000 (/0 is TEST), a SIB byte when
    // rm=100, a disp32 whose upper three bytes are 0 (FunctionFlags sits below +0x100), then the imm8. Its disp's low
    // byte, or -1.
    auto testAt = [&](size_t i, uint8_t imm, size_t limit) -> int {
        if (i + 7 > limit || body[i] != 0xF6) return -1;
        const uint8_t modrm = body[i + 1];
        if ((modrm & 0xF8) != 0x80) return -1;
        const size_t d = ((modrm & 7) == 4) ? i + 3 : i + 2;
        if (d + 5 > limit) return -1;
        if (body[d + 1] != 0 || body[d + 2] != 0 || body[d + 3] != 0 || body[d + 4] != imm) return -1;
        return body[d];
    };
    bool highTest[256] = {};
    bool anyHigh = false;
    for (size_t j = 0; j < size; ++j) {
        const int d2 = testAt(j, 0x40, size);
        if (d2 >= 2) { highTest[d2 - 2] = true; anyHigh = true; }
    }
    if (!anyHigh) return -1;
    const size_t lim1 = p1Limit < size ? p1Limit : size;
    for (size_t i = 0; i < lim1; ++i) {
        const int d1 = testAt(i, 0x04, lim1);
        if (d1 >= 1 && highTest[d1 - 1]) return d1 - 1;
    }
    return -1;
}

/// [DBGCAM-OFF-OVER-QUEUED-ON] A request EnqueueInvoke stopped waiting on and left queued, kept by a caller that must
/// know whether it has run since: a STATEFUL toggle reads its state before it fires, and a toggle still queued will
/// flip whatever that read said. Answered from the request's own promise: the drain fulfils it when it runs the
/// request, and Shutdown answers it -7 without running it -- either way nothing is left to flip the state.
/// Not thread-safe: the caller holds its own lock.
class QueuedInvoke {
public:
    void Record(std::shared_future<int32_t> done) { m_done = std::move(done); }
    /// True while a recorded request has not completed. Once it has -- run with any result, or answered without
    /// running -- the record clears itself.
    bool StillQueued() {
        if (!m_done.valid()) return false;
        if (m_done.wait_for(std::chrono::seconds(0)) != std::future_status::ready) return true;
        m_done = {};
        return false;
    }
    /// Whether a request is recorded at all (a completed one is cleared by the next StillQueued).
    bool HasRecord() const { return m_done.valid(); }
private:
    std::shared_future<int32_t> m_done;
};

/// [DBGCAM-OFF-OVER-QUEUED-ON] What a stateful toggle Set answers, sending nothing, while an earlier toggle is still
/// queued or being dispatched. The same direction IS the caller's toggle, already queued: -5, do not re-send. The
/// opposite direction was not sent, and the state it would read predates the queued toggle: -6, send it again once the
/// game responds. A code of its own, so no caller describes it as a queued toggle of its own direction.
constexpr int32_t kToggleBlockedByQueued = -6;
inline int32_t QueuedToggleAnswer(bool queuedWant, bool want) {
    return queuedWant == want ? kInvokeTimedOutStillQueued : kToggleBlockedByQueued;
}

constexpr int32_t kRequestQueued = 0, kRequestRunning = 1, kRequestAbandoned = 2;
inline bool ClaimRequest(std::atomic<int32_t>& state, int32_t to) {
    int32_t expected = kRequestQueued;
    return state.compare_exchange_strong(expected, to, std::memory_order_acq_rel);
}

// ============================================================
// ProcessEvent DETECTION lifecycle — pure decision rules
// ([PEHOOKONCE-2026-08-18] / [PEHOOK-2026-08-17])
// ============================================================
//
// The detector itself lives in Frieren.cpp (it needs Aura + Macht). Only the
// DECISIONS live here, header-inline, for one reason: no test target compiles
// Frieren.cpp, and this header is already in dll_helpers_test's include list.
// Moving a rule into a header is how this repo pins one (working-lessons §2.2).
//
// THREE failure classes, deliberately NOT merged — each has a different remedy
// and merging them is what produced the field defect:
//
//   1. NOT READY  — no UObject vtable exists to read yet. In proxy mode the DLL
//      starts the pipe server only, so GObjects is unset until a scan runs. This
//      is an EXPECTED state, not an error, and a later scan fixes it by itself.
//      It must therefore leave detection ARMED. Before the fix it stored the same
//      -1 as a hard failure and every retry path was gated against -1, so one
//      early `pe_profile_start` poisoned the hook for the whole process life.
//   2. DETECTION FAILED — candidate vtables existed and neither the pattern scan
//      nor the version table could name a slot. Terminal: retrying reads the same
//      bytes and reaches the same answer.
//   3. INSTALL FAILED — the offset is known and MinHook could not place a
//      trampoline (MH_ERROR_MEMORY_ALLOC). Already retryable on its OWN budget in
//      Frieren (kMaxHookAttempts / kHookRetryCooldownMs) and forced past by
//      UE5_EnsureGameThreadHook. Nothing here touches it.

/// How TryInstallGameThreadHook names an install attempt in its log lines ([MH-PRERESERVE-PROXY]). A forced attempt
/// (a feature being switched on) is not counted against the automatic budget (B24), so printing the automatic counter
/// beside it as "attempt N/M" read as a second attempt with the same number.
inline std::string DescribeHookAttempt(bool forced, int automaticAttempts, int maxAttempts) {
    const std::string budget = std::to_string(automaticAttempts) + "/" + std::to_string(maxAttempts);
    return forced ? "forced attempt, not counted -- " + budget + " automatic attempt(s) used"
                  : "attempt " + budget;
}

/// Detected ProcessEvent vtable offset sentinels. >=0 is a real byte offset.
/// Exported to the UI through UE5_GetProcessEventOffset, so the two negatives
/// are a wire contract, not private state.
constexpr int kPeOffsetNotDetected = -2;   ///< class 1 — re-armable, retry after a scan
constexpr int kPeOffsetFailed      = -1;   ///< class 2 — terminal for this process

inline bool PeOffsetUsable(int offset)     { return offset >= 0; }
inline bool PeOffsetRetryable(int offset)  { return offset == kPeOffsetNotDetected; }

/// Bound on REAL detection runs (ones that had candidate vtables to look at).
/// A run that found no candidates costs an Aura::GetCount() and returns, so it
/// deliberately spends no budget — otherwise a user who pokes an unscanned
/// process a dozen times would exhaust the budget and re-create the very
/// permanent-failure bug this replaces.
///
/// ⚠ THE COOLDOWN IS WHAT ACTUALLY THROTTLES; this cap is a backstop and is not
/// reachable by today's wiring. Worth stating plainly, because the obvious
/// reading — "without the cap a never-detectable game re-scans forever" — is
/// FALSE: the only outcome that leaves the sentinel re-armed is "no candidate
/// vtables", and that path returns before scanning anything. Every path that does
/// run the expensive scan ends at a usable offset or at kPeOffsetFailed, and both
/// fast-out. So the counter only advances on a validation re-arm, which is itself
/// capped at kMaxPeValidationFailures. Keep the cap anyway: it costs one compare
/// and it is the guard that holds if a future outcome ever leaves the sentinel
/// armed after a scan.
constexpr int      kMaxPeDetectAttempts = 8;
constexpr uint64_t kPeDetectCooldownMs  = 1000;

/// Should a re-detection run right now?
///
/// This is the anti-storm rule. Detection is re-armed on the ordinary invoke
/// path, which a 10 Hz feature worker walks, so without the cooldown an
/// undetectable game would re-probe on every single invoke, forever.
///
/// @param force  a user-initiated attempt (a feature being switched on). Skips
///               the cap and the cooldown for the same reason
///               TryInstallGameThreadHook's `force` does: the user is waiting,
///               and when the offset is already usable this returns false long
///               before any work happens.
inline bool ShouldRetryPeDetection(int currentOffset,
                                   int attemptsSpent,
                                   uint64_t nowMs,
                                   uint64_t lastAttemptMs,
                                   bool force,
                                   int maxAttempts = kMaxPeDetectAttempts,
                                   uint64_t cooldownMs = kPeDetectCooldownMs) {
    if (!PeOffsetRetryable(currentOffset)) return false;   // usable, or terminally failed
    if (force) return true;
    if (attemptsSpent >= maxAttempts) return false;
    if (lastAttemptMs != 0 && nowMs - lastAttemptMs < cooldownMs) return false;
    return true;
}

/// How many post-install validation failures (hook fired 0 times) are absorbed
/// before the offset is declared terminally wrong.
constexpr int kMaxPeValidationFailures = 3;

/// Act on a VALIDATION FAILED verdict, or only report it?
///
/// Only the version-TABLE guess is acted on. That asymmetry is the whole point:
///
///  * A zero fire count has two causes — a mis-detected slot, or a game thread
///    that genuinely did not tick during the window (paused, loading screen,
///    minimised with t.IdleWhenNotForeground). The count alone cannot separate
///    them, so acting on every zero would disable a CORRECT hook on an idle game.
///  * The two causes are not equally likely per detector. The pattern scan
///    fingerprints ProcessEvent's own body and has never been observed wrong
///    (Lushfoil UE 5.6: pattern hit at vtable+0x260, validator OK). The version
///    table is a per-version GUESS with no evidence from the binary at all, and
///    it is what produced the one measured mis-detection (DumperTest UE 5.4
///    Development: fallback primary=0x220, 0 fires).
///
/// So a zero on the pattern path reads as "the game was idle" and the hook is
/// kept; a zero on the table path reads as "the guess was wrong" and is acted on.
inline bool ShouldActOnValidationFailure(bool offsetFromVersionTable) {
    return offsetFromVersionTable;
}

/// What the stored offset must become after a validation failure.
///
/// Re-arming (rather than failing outright) is what lets the idle-game false
/// positive recover by itself: the next invoke re-detects, re-installs — MinHook
/// fast-paths a re-enable at the same address — and re-arms the validator, which
/// passes as soon as the game is ticking. A genuinely wrong slot fails that same
/// loop, which is why the loop is counted and terminal at kMaxPeValidationFailures.
///
/// @param failureCount 1-based count of validation failures seen so far.
/// @return kPeOffsetNotDetected (re-arm), kPeOffsetFailed (give up), or
///         currentOffset unchanged (verdict reported but not acted on).
inline int PeOffsetAfterValidationFailure(bool offsetFromVersionTable,
                                          int currentOffset,
                                          int failureCount,
                                          int maxFailures = kMaxPeValidationFailures) {
    if (!ShouldActOnValidationFailure(offsetFromVersionTable)) return currentOffset;
    if (failureCount >= maxFailures) return kPeOffsetFailed;
    return kPeOffsetNotDetected;
}

} // namespace Stark
