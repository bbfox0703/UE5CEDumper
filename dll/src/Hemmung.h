#pragma once

// ============================================================
// Hemmung — ヘムング (霧を魔力に変える — "inhibition", mist→energy mage)
// TimeDilation: force the game's reflected time-dilation floats — global
// AWorldSettings::TimeDilation (whole-world slow-mo / freeze / fast-forward)
// and per-pawn AActor::CustomTimeDilation — to a held ABSOLUTE value against
// per-tick game/Sequencer overwrites, via a write-on-drift re-assert worker.
// Hemmung *inhibits* the game's clock: it holds the dilation where the user
// pinned it and re-writes it whenever the game (a slow-mo ability, a Sequencer
// track) tries to move it.
//
// This is a close sibling of Laufen (MovementTuning): same s_mutex +
// s_workerMutex two-lock split, same LWC-width-aware reflected float read/write,
// same re-assert worker (write-on-drift, base capture, owner-change re-capture).
// The differences: (1) the target is an ABSOLUTE value, not a multiplier of a
// base (dilation is naturally absolute — 1.0 = normal, 0.5 = half speed, 0 =
// frozen; the captured base is used only to RESTORE on reset); (2) the two
// targets live on DIFFERENT objects (the WorldSettings singleton vs the local
// pawn) rather than on one re-resolved pawn.
//
// Reach: DIL_GLOBAL resolves AWorldSettings via GWorld→PersistentLevel→
// WorldSettings (reflected ObjectProperty chain) with an
// Aura::FindInstancesDerivedFrom("WorldSettings") fallback; DIL_PAWN reuses the
// local-pawn chain (GWorld→GameInstance→LocalPlayers[0]→PC→Pawn). All offsets
// resolved by FName (DynOff rule) → UE4/UE5-agnostic. Pure reflected memory
// read/write via Macht (SEH) — NO UFunction invoke, NO game thread. No cached
// instance pointers — the WorldSettings / pawn is re-resolved every operation
// and every worker tick (the stale-pointer crash class).
//
// NOTE: writing TimeDilation directly bypasses SetGlobalTimeDilation's
// [MinGlobalTimeDilation, MaxGlobalTimeDilation] clamp. The effective world
// dilation is TimeDilation × MatineeTimeDilation × DemoPlayTimeDilation ×
// CustomTimeDilation — Hemmung holds the first (global) and the last (per-pawn);
// a paused world (bGamePaused) can't be stepped via dilation, and an active
// Sequencer time-track will flicker for up to one re-assert tick.
// ============================================================

#include <cstdint>
#include <string>

namespace Hemmung {

// Result codes (mirrors Laufen::MoveResult conventions). Non-negative returns
// from Set/Reset are the observed active state (1 = override active, 0 =
// inactive); negative values are errors.
enum TimeResult : int32_t {
    TR_OK           = 0,
    TR_ERR_NOT_INIT = -1,   // DLL not initialized / GWorld unavailable
    TR_ERR_NO_TARGET= -6,   // WorldSettings / local pawn not found this instant
    TR_ERR_REFLECT  = -4,   // TimeDilation / CustomTimeDilation float not reflected
    TR_ERR_WRITE    = -10,  // raw float write failed
};

// The two dilation levers. Stable order — also the wire value used by the pipe
// "target" string mapping + the CE mailbox.
enum DilTarget : int32_t {
    DIL_GLOBAL = 0,   // AWorldSettings::TimeDilation — whole-world speed
    DIL_PAWN   = 1,   // local pawn AActor::CustomTimeDilation — per-actor speed
    DIL_COUNT
};

// Live snapshot of one dilation lever, for the UI readout + Locate-in-GWorld
// handoff (owner+offset).
struct DilationInfo {
    bool        resolved   = false; // field found on the live owner this instant
    double      current    = 0.0;   // live value read from memory
    double      base        = 1.0;  // captured natural value (restored on reset)
    double      value       = 1.0;  // desired held value (1.0 = normal)
    bool        active      = false;// override engaged (worker holding it)
    uintptr_t   ownerAddr   = 0;    // WorldSettings / pawn address — Locate owner
    int32_t     fieldOffset = -1;   // float offset within the owner
    std::string fieldName;          // reflected property name
};

struct Snapshot {
    int32_t      code = 0;              // TimeResult: TR_OK or a negative error
    DilationInfo dils[DIL_COUNT];
};

// Read both dilation levers (current/base/value/active + owner+offset). Always
// fills Snapshot; code carries why a read failed.
int32_t GetSnapshot(Snapshot& out);

// Read a single lever (for the UI badge / connect-time query / CE readout).
int32_t GetDilation(int32_t target, DilationInfo& out);

// Hold `target` at an ABSOLUTE dilation `value` (clamped to
// [TIME_DILATION_MIN, TIME_DILATION_MAX]). Captures the game's natural value as
// the restore base on first activation (and re-captures when the owner changes —
// level transition / respawn). Starts the re-assert worker. Returns 1 (active)
// or a negative TimeResult. value = 1.0 is a valid state ("lock at normal speed"
// — blocks the game from slowing time); use ResetDilation to turn it off.
int32_t SetDilation(int32_t target, double value);

// Disable a lever: best-effort restore the captured natural value, clear the
// override. Stops the worker when no lever remains active. Returns TR_OK or a
// negative TimeResult.
int32_t ResetDilation(int32_t target);

// Stop and join the re-assert worker. Idempotent. Called by ResetDilation (when
// nothing else is active) and from UE5_Shutdown() before the DLL unloads.
void StopWorker();

} // namespace Hemmung
