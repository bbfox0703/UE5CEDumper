#pragma once

// ============================================================
// Laufen — 拉歐芬 / 走る (高速移動の魔法 — "to run", high-speed-movement mage)
// MovementTuning: force per-pawn UCharacterMovementComponent float knobs
// (MaxWalkSpeed / GravityScale / JumpZVelocity) by a multiplier of their
// captured base value, held against per-tick game overwrites by a re-assert
// worker — the same write-on-drift pattern Solitar (GodMode) uses, retargeted
// from a single FBoolProperty bit to FloatProperty scalars on the CMC. The
// UE5.3+ GravityDirection FVector is held by the same capture / re-assert
// discipline, but set outright to a unit vector rather than scaled.
//
// Mechanism: resolve the local pawn (GWorld → OwningGameInstance →
// LocalPlayers[0] → PlayerController → Pawn), hop to its CharacterMovement
// sub-object (reflected "CharacterMovement" ObjectProperty), FindField the
// named float, capture its untouched base once, then write base*multiplier.
// Pure reflected memory read/write via Macht (SEH) — NO UFunction invoke, NO
// game thread. Self-contained (Path B): only the public APIs of non-gameplay
// modules + DynOff — no coupling to Wirbel or any other gameplay feature. All
// offsets resolved by FName (DynOff rule) → UE4/UE5-agnostic. No cached
// instance pointers — the pawn/CMC is re-resolved every operation and every
// worker tick (stale-pointer crash class).
//
// The capture / re-assert engine is knob-agnostic: a knob is only the reflected
// name it resolves (kKnobs, Laufen.cpp); SetKnobPercent owns any per-knob
// percent mapping.
// ============================================================

#include <cstdint>
#include <string>

namespace Laufen {

// Result codes (mirrors Solitar::ProtectResult conventions). Non-negative
// returns from Set/Reset are the observed live state (1 = override active,
// 0 = inactive); negative values are errors.
enum MoveResult : int32_t {
    MR_OK           = 0,
    MR_ERR_NOT_INIT = -1,   // DLL not initialized / GWorld unavailable
    MR_ERR_REFLECT  = -4,   // target float property not resolved on the CMC
    MR_ERR_NO_PAWN  = -3,   // local pawn null (menu / cutscene / spectator)
    MR_ERR_NO_CMC   = -5,   // pawn has no CharacterMovement (vehicle / custom framework)
    MR_ERR_WRITE    = -10,  // raw float write failed
};

// One tunable CMC float. Stable order: the values are the knobId of the C ABI
// and of Mimic's CMD_MOVEMENT, baked into generated CE scripts (the pipe sends
// "knob" names, mapped onto these in Fern). CMD_MOVEMENT already spends the
// next knobId on GravityDirection, so appending a knob is a mailbox-contract
// change, not just an enum edit.
enum KnobId : int32_t {
    KNOB_WALK_SPEED = 0,    // UCharacterMovementComponent::MaxWalkSpeed
    KNOB_GRAVITY    = 1,    // UCharacterMovementComponent::GravityScale
    KNOB_JUMP       = 2,    // UCharacterMovementComponent::JumpZVelocity
    KNOB_COUNT
};

// Live snapshot of one knob, for the UI readout + Locate-in-GWorld handoff.
struct KnobInfo {
    bool        resolved   = false; // field found on the live CMC this instant
    double      current    = 0.0;   // live value read from memory
    double      base       = 0.0;   // captured base (valid only when active)
    double      multiplier = 1.0;   // desired multiplier (1.0 = off)
    bool        active     = false; // override engaged (worker holding it)
    uintptr_t   ownerAddr  = 0;     // CMC address — Locate-in-GWorld owner
    int32_t     fieldOffset = -1;   // float offset within the CMC
    std::string fieldName;          // reflected property name
};

// Live snapshot of the gravity-DIRECTION vector (UE5.3+ only — the reflected
// UCharacterMovementComponent::GravityDirection FVector). resolved=false on
// pre-5.3 / games where the field isn't reflected.
struct GravDirInfo {
    bool        resolved   = false;
    double      x = 0, y = 0, z = -1;   // live unit vector (default (0,0,-1) = down)
    bool        active     = false;     // override engaged (worker holding it)
    uintptr_t   ownerAddr  = 0;         // CMC — Locate-in-GWorld owner
    int32_t     fieldOffset = -1;       // GravityDirection offset within the CMC
    std::string fieldName;
};

struct Snapshot {
    int32_t   code    = 0;      // MoveResult: MR_OK or a negative error
    bool      hasCmc  = false;  // a CharacterMovement resolved on the pawn
    uintptr_t cmcAddr = 0;
    KnobInfo  knobs[KNOB_COUNT];
    GravDirInfo gravDir;        // UE5.3+ arbitrary gravity direction
};

// Read every knob on the current pawn's CMC (current/base/multiplier/active +
// owner+offset). Always fills Snapshot; code carries why a read failed.
int32_t GetSnapshot(Snapshot& out);

// Read a single knob (for the UI badge / connect-time query).
int32_t GetKnob(int32_t knobId, KnobInfo& out);

// Apply a multiplier (of the captured base) to a knob. Captures the base on
// first activation (and re-captures when the pawn changes — respawn). Starts
// the re-assert worker. Returns the observed active state (1) or a negative
// MoveResult. multiplier is clamped to [MOVE_MULT_MIN, MOVE_MULT_MAX].
int32_t SetMultiplier(int32_t knobId, double multiplier);

// Disable a knob: best-effort restore the captured base, clear the override.
// Stops the worker when no knob remains active. Returns MR_OK or a negative
// MoveResult.
int32_t ResetKnob(int32_t knobId);

// One-call "set percent" entry point for the CE-Lua / mailbox path (and the
// UI). `percent` is the USER-FACING slider value: 100 (±0.5) means OFF and calls
// ResetKnob. For KNOB_JUMP `percent` is jump HEIGHT % and the applied
// JumpZVelocity multiplier is sqrt(percent/100) (apex height ∝ v²); for the
// other knobs the multiplier is percent/100. Returns 1 (active), 0 (off via
// 100%), or a negative MoveResult. This keeps the height↔velocity + "100% = off"
// logic in ONE place (the DLL), so Lua just passes a percentage.
int32_t SetKnobPercent(int32_t knobId, double percent);

// === Gravity DIRECTION (UE5.3+ arbitrary gravity) ===

// Set the pawn's UCharacterMovementComponent::GravityDirection to the (x,y,z)
// vector (normalized DLL-side; the engine expects a unit vector). Captures the
// game's default on first activation, holds the value with the re-assert worker.
// Sentinel: (0,0,0) means OFF — restore the captured default (a zero vector is
// not a valid direction). Returns 1 (active), 0 (off via (0,0,0)), or a negative
// MoveResult (MR_ERR_REFLECT when GravityDirection isn't reflected — pre-5.3).
int32_t SetGravityDirection(double x, double y, double z);

// Restore GravityDirection to its captured default and stop holding it.
int32_t ResetGravityDirection();

// Read the live gravity direction (+ owner/offset for Locate-in-GWorld).
int32_t GetGravityDirection(GravDirInfo& out);

// Stop and join the re-assert worker. Idempotent. Called by ResetKnob (when
// nothing else is active) and from UE5_Shutdown() before the DLL unloads.
void StopWorker();

} // namespace Laufen
