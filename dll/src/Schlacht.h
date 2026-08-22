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
// as the camera moves; everything is un-hidden on disable / disconnect.
//
// Stage 1: NEAREST single occluder (camera→pawn LineTraceSingle). A future
// Stage 2 can widen to all occluders (LineTraceMulti). Single-player only.
// Offsets resolved by FName (DynOff), UE4/UE5-agnostic; pawn re-resolved per tick.
// ============================================================
#pragma once
#include <cstdint>
#include <vector>

namespace Schlacht {

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
// direct-call-off-the-game-thread fallback, which the invoke path now refuses for
// repeating worker calls (it is the historic crash shape). Rather than run a
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

    // WHICH actors are hidden, not merely how many.
    //
    // hiddenCount alone is the DLL's own tally of what it BELIEVES it hid, and an
    // outside verifier had no way to audit it: a tick where SetActorHiddenInGame
    // was invoked but did not take looks identical to a tick where it worked.
    // Measured 2026-08-22 on DumperTest — hiddenCount reported 1 while not one of
    // the 33 candidate actors an independent walk could reach had bHidden set, and
    // there was no way to tell "my candidate set is wrong" from "the hide failed",
    // because the DLL would not say who. [SEETHRUSET-2026-08-22]
    //
    // The set already exists (s_state.hiddenActors); this only publishes it. Its
    // size is hiddenCount by construction, so the two cannot drift.
    std::vector<uintptr_t> hiddenActors;
};

// Enable / disable the see-through worker. Idempotent. enable=false un-hides any
// actor we hid and stops the worker. Returns 1 (on), 0 (off), or negative on error.
int32_t SetEnabled(bool enable);

// How many nearest occluders to hide along the view ray (clamped to
// [1, SCHLACHT_PIERCE_MAX]). Takes effect on the next tick; safe to call while
// active. Pawns/Characters on the ray are skipped (kept visible) and do NOT
// count toward the pierce depth.
void SetPierceCount(int32_t count);

// Poll the live status (safe to call from the pipe thread).
int32_t GetStatus(SeeThroughStatus& out);

// True while the worker is engaged.
bool IsActive();

// Stop + join the worker. Idempotent. Called by SetEnabled(false) and from
// UE5_Shutdown() before the DLL unloads.
void StopWorker();

} // namespace Schlacht
