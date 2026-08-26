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

#include <cstdint>

namespace Stark {

/// Install the ProcessEvent hook at the given address.
/// @param processEventAddr  Absolute address of UObject::ProcessEvent
/// @return true if hook installed successfully
bool InstallHook(uintptr_t processEventAddr);

/// Remove the hook and clean up. Safe to call even if not installed.
/// Does NOT call MH_Uninitialize — use Shutdown() at DLL unload instead.
void RemoveHook();

/// Full teardown: remove hook AND uninitialize MinHook globally.
/// Call ONLY from DLL_PROCESS_DETACH (via UE5_Shutdown). After this,
/// InstallHook() can no longer be used unless MinHook is re-initialized.
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
///                    the UFunction ParmsSize when the caller's buffer is
///                    transient (the common case — prevents a use-after-free if
///                    the invoke times out but is later drained by the game
///                    thread). Pass 0 only when `params` is a persistent buffer
///                    that outlives the request (e.g. Mimic's mailbox global).
/// @return 0 on success, -4 if SEH exception, -5 if timeout, -7 if hook not active
int32_t EnqueueInvoke(uintptr_t instance, uintptr_t ufunc, uintptr_t params, size_t paramsSize);

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
// The fix is NOT a thread-identity check. There is no IsInGameThread in this
// tree and nothing resolves GIsGameThreadId, so any gate would be guessing --
// and a gate that guesses wrong never drains, which times out every game-thread
// invoke and is strictly worse than the defect. Instead we do what Grausam
// already does for its own hooks: call the TRAMPOLINE we are holding, so our
// calls never enter the detour in the first place.

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

/// True while this thread is inside CallOriginalSEH.
bool InOwnPeCall();

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
/// `entryIsOurs` is true when this thread is inside our own CallOriginalSEH --
/// i.e. the detour was re-entered by a nested dispatch underneath a call WE
/// issued, which is not a game-thread tick and must not drain. This is the
/// shipped gate, not a mirror of it: HookedProcessEventBody calls exactly this.
inline bool ShouldDrainQueue(size_t queueDepth, bool entryIsOurs) {
    return queueDepth != 0 && !entryIsOurs;
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
