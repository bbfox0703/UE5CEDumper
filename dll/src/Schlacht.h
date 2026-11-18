// ============================================================
// Schlacht — シュラハト / Schlacht (「全知者」 — "The Omniscient")
// See-through occluders: let an omniscient view pass through the world so the
// wall between the camera and your character never blocks it.
//
// Each worker tick traces a ray from the CAMERA to the local PAWN
// (UKismetSystemLibrary::LineTraceSingle, game thread via Stark), takes the
// NEAREST blocking hit, and — only if that actor is NOT a Pawn/Character (so
// enemies / NPCs / the player itself are never touched) — hides it via
// AActor::SetActorHiddenInGame(true). The previously-hidden actor is restored
// as the camera moves; everything is un-hidden on disable, or when the last
// client that turned it on disconnects.
//
// Stage 1: NEAREST single occluder (camera→pawn LineTraceSingle). A future
// Stage 2 can widen to all occluders (LineTraceMulti). Single-player only.
// Offsets resolved by FName (DynOff), UE4/UE5-agnostic; pawn re-resolved per tick.
// ============================================================
#pragma once
#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <vector>

namespace Schlacht {

// ── Who holds See-through ([PIPE-PER-PROCESS-2026-11-06] Stage C) ─────────────
// One switch, two kinds of client turn it on: a pipe client (the UI's card, a rig, MCP) and CE's mailbox record. Each
// enable records who asked; a departing client process releases only its own claim, and See-through goes off with the
// last claim. So a departure never turns it off under a ticked CE record (whose untick would then do nothing), and a
// departed client's See-through never stays on with nobody left to turn it off. An explicit OFF from anyone still turns
// it off and drops every claim: that is a user's untick or a card's OFF, which means "off", not "I no longer need it".
//
// An owner is a client process (Voll::ClientOwnerKey) or CE (kOwnerCe).
constexpr uint64_t kOwnerNone = 0;      // names no owner: an explicit OFF, and a claim that records nothing
constexpr uint64_t kOwnerCe   = ~0ull;  // the CE mailbox record

enum class OwnerRelease {
    NotHeld,     // the owner never claimed it: nothing changes
    StillHeld,   // it stays on: the owner is still connected, or another owner holds it
    TurnOff,     // the last claim went: turn it off
};

// Records `owner` once. kOwnerNone is never recorded.
inline void ClaimOwner(std::vector<uint64_t>& owners, uint64_t owner) {
    if (owner == kOwnerNone) return;
    for (uint64_t o : owners)
        if (o == owner) return;
    owners.push_back(owner);
}

// Whether `owner` holds a claim. kOwnerNone is nobody's claim.
inline bool HasOwner(const std::vector<uint64_t>& owners, uint64_t owner) {
    return owner != kOwnerNone && std::find(owners.begin(), owners.end(), owner) != owners.end();
}

// A departing owner's release. `stillLive` = another connection of the same client process is still registered, and
// then its claim is kept.
inline OwnerRelease ReleaseOwner(std::vector<uint64_t>& owners, uint64_t owner, bool stillLive) {
    for (size_t i = 0; i < owners.size(); ++i) {
        if (owners[i] != owner) continue;
        if (stillLive) return OwnerRelease::StillHeld;
        owners.erase(owners.begin() + static_cast<std::ptrdiff_t>(i));
        return owners.empty() ? OwnerRelease::TurnOff : OwnerRelease::StillHeld;
    }
    return OwnerRelease::NotHeld;
}

// ── The hidden record across a failed dispatch ([SHUTDOWN-SEETHRU-WAIT]) ──────
// A game-thread invoke that did not run (a timeout, a wait cut by a shutdown on a stalled game thread, a hook that is
// down) is not a clean answer. A LineTrace that did not run does not mean "nothing occludes", and an un-hide that did
// not run leaves its actor hidden; dropping the actor from the record on either would leave it hidden with nothing
// tracking it, out of the next enable's or disable's reach. A shutdown cutting such waits makes both likely, so the
// record keeps what a failed dispatch may have left hidden.

// Apply a tick's hide/un-hide diff only when every trace ran and no stop is under way: a trace that did not run says
// nothing about what occludes.
inline bool ShouldApplyOccluderDiff(bool dispatchFailed, bool stopRequested) {
    return !dispatchFailed && !stopRequested;
}

// What one SetActorHiddenInGame call came to: never attempted (not an actor, or the setter is cooked out), or the
// invoke's own result (0 = it ran).
constexpr int32_t kHideNotAttempted = INT32_MIN;

// The invoke's code for a call that entered the engine and faulted there (CallProcessEventSEH): it ran.
constexpr int32_t kHideFaultedInCall = -4;

// After an un-hide, does the actor stay in the record? Only when the call was attempted and did not run: it may still be
// queued, and Stark::Shutdown answers a queued request without running it. Un-hiding twice is harmless, losing the
// record is not. A call never attempted drops the actor (it died, or the setter went away), and so does one
// that faulted inside the engine: kept, it would fault again on every 100 ms tick.
inline bool UnhideLeftItHidden(int32_t code) {
    return code != 0 && code != kHideNotAttempted && code != kHideFaultedInCall;
}

// ── The record across a hand-off ([SEETHRU-HANDOFF-LEFTOVER]) ────────────────
// A copy that hands over to a later copy (Land) turns See-through off in its shutdown. On a game thread that is not
// firing then, the un-hides cannot run and the record is kept "for the next See-through ON" -- but a copy that handed
// over never gets another ON: its exports run in the later copy, whose own record does not know these actors. The later
// copy therefore takes the record over (UE5_HandOverHiddenActors), and its next ON or OFF restores them.

// Moves up to `cap` actors out of `hidden` -- and out of `queued`, the un-hides that may still run -- into `out`;
// returns how many. What does not fit stays for the next call.
template <class Set>
inline int32_t TakeHiddenRecord(Set& hidden, Set& queued, uint64_t* out, int32_t cap) {
    int32_t n = 0;
    for (auto it = hidden.begin(); it != hidden.end() && n < cap;) {
        out[n++] = static_cast<uint64_t>(*it);
        queued.erase(*it);
        it = hidden.erase(it);
    }
    return n;
}

// A recorded actor that still occludes counts as hidden with no call -- unless its last un-hide did not run: that
// un-hide may still be queued, and when it runs the actor shows while the record says hidden. Such an actor is hidden
// again (the queue runs the hide after the un-hide). Hiding twice is harmless.
inline bool RehideOnTick(bool stillOccluding, bool unhideMayStillRun) {
    return stillOccluding && unhideMayStillRun;
}

// Result / status code (0 = OK, negative = why the tick could not act).
enum SeeThroughResult {
    STR_OK             =  0,
    STR_ERR_NOT_INIT   = -1,   // GWorld not resolved (Connect & scan first)
    STR_ERR_NO_PAWN    = -2,   // no local player pawn (menu / loading / cutscene)
    STR_ERR_REFLECTION = -3,   // LineTraceSingle / SetActorHiddenInGame cooked out
    STR_ERR_NO_CAMERA  = -4,   // PlayerCameraManager / GetCameraLocation unavailable
    STR_ERR_NO_HOOK    = -5,   // game-thread ProcessEvent hook unavailable — see below
};

// STR_ERR_NO_HOOK: the worker traces the world by INVOKING LineTraceSingle, ~10x
// a second. With the game-thread hook down those invokes would have to take the
// direct-call-off-the-game-thread fallback, which the invoke path refuses for
// repeating worker calls (a known crash shape). Rather than run a
// worker whose every tick is refused, See-Through declines to enable and says so.
// A later enable retries the hook first — a MinHook trampoline-allocation failure
// is a VM-layout accident, not a permanent property of the game.

struct SeeThroughStatus {
    int32_t code        = 0;      // last SeeThroughResult (0 = OK on the last tick)
    bool    active      = false;  // the see-through worker is engaged
    bool    hasTarget   = false;  // camera + pawn resolved on the last tick
    int32_t hiddenCount = 0;      // occluders currently hidden
    int32_t pierceCount = 1;      // how many nearest occluders to hide along the ray
    int32_t state       = -1;     // last enable/disable result (1/0/neg); -1 = poll-only
    // [P1-SEETHRU-GIVEUP] The leftover-hidden record, split: a restore still WAITING for the game thread, and one that
    // GAVE UP after PENDING_RESTORE_MAX_MS. A record taken over from an earlier copy at a hand-off gets the same deferred
    // restore ([SEETHRU-HANDOFF-LEFTOVER]). hiddenCount > 0 with active == false alone cannot tell the two apart, and
    // only the waiting one reappears by itself.
    bool    restorePending   = false;
    bool    restoreAbandoned = false;

    // WHICH actors are hidden, not merely how many.
    //
    // hiddenCount alone is the DLL's own tally of what it BELIEVES it hid, and an
    // outside verifier cannot audit it: a tick where SetActorHiddenInGame was
    // invoked but did not take looks identical to a tick where it worked. Naming
    // the actors lets a verifier tell "my candidate set is wrong" from "the hide
    // failed". [SEETHRUSET-2026-08-22]
    //
    // The set already exists (s_state.hiddenActors); this only publishes it. Its
    // size is hiddenCount by construction, so the two cannot drift.
    std::vector<uintptr_t> hiddenActors;

    // Who holds it on ([PIPE-PER-PROCESS-2026-11-06] Stage C): how many owners, and whether CE's record is one.
    int32_t claims    = 0;
    bool    ceClaimed = false;
    // [MCP-WRITE-GRANTS-2026-11-14] Whether the client that asked holds one of those claims, so a client can tell its
    // own claim from another's without remembering what it did (a UI OFF then ON in between would fool that memory).
    bool    callerClaimed = false;
};

// Enable / disable the see-through worker. Idempotent. enable=true records `owner`'s claim when it turns on or was
// already on; a refused enable records nothing. enable=false is an explicit OFF: every claim goes, any actor we hid is
// un-hidden and the worker stops, and `owner` is not consulted. Returns 1 (on), 0 (off), or negative on error.
[[nodiscard]] int32_t SetEnabled(bool enable, uint64_t owner);

// A departed owner's release. `stillLive(owner)` is asked under Schlacht's own lock, so a client process that
// reconnects and turns See-through on in between is not turned off under it. When the last claim goes it turns off,
// exactly as an explicit OFF would.
OwnerRelease Release(uint64_t owner, const std::function<bool(uint64_t)>& stillLive);

// How many nearest occluders to hide along the view ray (clamped to
// [1, SCHLACHT_PIERCE_MAX]). Takes effect on the next tick; safe to call while
// active. Pawns/Characters on the ray are skipped (kept visible) and do NOT
// count toward the pierce depth.
void SetPierceCount(int32_t count);

// Poll the live status (safe to call from the pipe thread). `caller` fills callerClaimed.
int32_t GetStatus(SeeThroughStatus& out, uint64_t caller = kOwnerNone);

// True while the worker is engaged.
bool IsActive();

// Stop + join the worker, leaving the hidden record as it is. Idempotent. UE5_Shutdown does not use it: it turns
// See-through off, so the occluders are un-hidden while the hook can still dispatch.
void StopWorker();

// [SEETHRU-HANDOFF-LEFTOVER] A tick judges a snapshot of the record and writes its result back; what was added while it
// ran -- actors an earlier copy handed over (AdoptLeftovers) -- is kept for the next tick to judge, not overwritten.
template <class Set>
inline void KeepAddedDuringTick(Set& applied, const Set& now, const Set& snapshot) {
    for (const auto& a : now)
        if (!snapshot.count(a)) applied.insert(a);
}

// [SEETHRU-HANDOFF-LEFTOVER] This copy's leftover record, moved into `out` (at most `cap`; see TakeHiddenRecord). Nothing
// while See-through is on: a running worker owns its record.
int32_t TakeLeftovers(uint64_t* out, int32_t cap);
// [SEETHRU-HANDOFF-LEFTOVER] Actors an earlier copy's See-through left hidden, recorded as this copy's. Off: this copy's
// deferred restore un-hides them when the game thread answers (as after an OFF that could not finish), and its next ON
// or OFF does too. On: a running worker's next tick un-hides those no longer in the way.
void AdoptLeftovers(const uint64_t* actors, int32_t n);

} // namespace Schlacht
