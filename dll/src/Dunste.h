#pragma once

// ============================================================
// Dunste — ドゥンスト / Dünste (「蒸氣」 — vapors; Edel's teammate mage)
// Fly: no-gravity free 3D flight of the local pawn, driven by the keyboard.
// Vapors rise and drift weightlessly through the air, unbound by gravity —
// the module lifts the pawn off the ground and steers it in 3 dimensions.
//
// Mechanism: put the pawn's UCharacterMovementComponent into MOVE_Flying (raw
// enum-byte write, held against per-tick game overwrites by a re-assert worker —
// same write-on-drift pattern as Laufen/Solitar) so the engine suppresses
// gravity. A fast (~60 Hz) worker then samples the keyboard directly
// (GetAsyncKeyState — the DLL lives inside the game process, so no per-frame IPC).
//
// Control is VIEW-RELATIVE: movement is computed from the camera (ControlRotation)
// yaw — W flies where you look (ground plane), A/D strafe, Z/C move world-vertical.
// Turning is the mouse (we only READ the camera yaw; never write rotation). Two
// motion modes:
//   • Collision (default): drive the CMC Velocity FVector — the engine sweeps
//     world geometry (you can't pass through walls).
//   • Noclip: SAME velocity-drive, plus AActor::SetActorEnableCollision(false) so
//     the flying sweep passes through walls. No gravity in either mode. (FF7Rebirth
//     zeroes Velocity every frame → neither mode moves it.)
// All writes are pure reflected Macht (SEH) memory writes — NO UFunction invoke,
// NO game thread (Path B, like Laufen). All offsets resolved by FName (DynOff) →
// UE4/UE5-agnostic. No cached instance pointers: the pawn/CMC is re-resolved
// every worker tick (stale-pointer crash class).
//
// Input is captured only while the GAME window is foreground; the mouse is
// never touched (the game's own look input keeps working). Single-player only
// — same anti-cheat/desync caveat as Laufen/Wirbel.
// ============================================================

#include <cstdint>

namespace Dunste {

// Result codes (mirrors Laufen::MoveResult conventions). Non-negative returns
// from Set are the observed state (1 = flying active, 0 = inactive); negatives
// are errors.
enum FlyResult : int32_t {
    FR_OK           = 0,
    FR_ERR_NOT_INIT = -1,   // DLL not initialized / GWorld unavailable
    FR_ERR_NO_PAWN  = -3,   // local pawn null (menu / cutscene / spectator)
    FR_ERR_REFLECT  = -4,   // MovementMode / Velocity not resolved on the CMC
    FR_ERR_NO_CMC   = -5,   // pawn has no CharacterMovement (vehicle / custom framework)
    FR_ERR_WRITE    = -10,  // raw write failed
};

// What actually happened to ONE SetActorEnableCollision request.
//
// ⛔ `Absent` and `Refused` are NOT the same failure and must never be collapsed back into
// a bool. That collapse is the D1 defect: `InvokeSetCollision` returned "the setter was
// FOUND" and every caller read it as "collision CHANGED".
//   Absent  — the pawn's class has no SetActorEnableCollision (cooked out of a stripped
//             Shipping build). PERMANENT: retrying cannot conjure a setter, so the caller
//             COMMITS and stops re-emitting. That is audit #4 B8 and it must survive.
//   Refused — the dispatcher did not run it: -8 (an off-game-thread worker while the PE
//             hook is down, Frieren.cpp), -5 (the game thread did not drain in time),
//             -3 (no usable PE offset), -2/-4. TRANSIENT: the caller must NOT commit, and
//             must retry.
enum class CollisionApply : int32_t {
    Applied = 0,   // the dispatcher ran it and returned 0
    Absent  = 1,   // no setter on this pawn class
    Refused = 2,   // the dispatcher refused or failed
};

/// May the caller commit its collision record from this outcome? Pure, so
/// `dll_helpers_test` can pin the B8 rule without linking Dunste.cpp.
inline bool ShouldCommitCollision(CollisionApply a) {
    return a != CollisionApply::Refused;      // Applied AND Absent both commit (B8)
}

// Keyboard preset — which physical keys drive movement (forward/back, strafe
// L/R, up/down). Turning is view-relative (the mouse), so the yaw keys are no
// longer used. Wire value shared with the pipe/mailbox.
enum Preset : int32_t {
    PRESET_WASD   = 0,   // WASD move + Z/C up/down
    PRESET_NUMPAD = 1,   // Num 8/2/4/6 move + 1/3 up/down
    PRESET_ARROWS = 2,   // Arrows move + PgUp/PgDn up/down
    PRESET_COUNT
};

// Live snapshot for the UI badge / pipe readout.
struct FlyStatus {
    int32_t   code           = 0;      // FlyResult: FR_OK or a negative error
    bool      active         = false;  // fly override engaged (worker holding it)
    bool      noclip         = false;  // fly through walls (collision disabled) vs kept
    bool      hasCmc         = false;  // a CharacterMovement resolved on the pawn
    int32_t   preset         = PRESET_WASD;
    double    speed          = 0.0;    // uu/s
    bool      modeResolved   = false;  // MovementMode field found this instant
    int32_t   currentMode    = -1;     // live MovementMode enum byte (-1 = unknown)
    uintptr_t cmcAddr        = 0;
};

// Enable/disable fly. On enable: capture the pawn's current MovementMode (to
// restore later), force MOVE_Flying, start the worker. On disable: restore the
// captured MovementMode, zero velocity, stop the worker. Returns 1 (active),
// 0 (off), or a negative FlyResult.
int32_t SetEnabled(bool enable);

// Set the flight speed (uu/s), clamped to [FLY_SPEED_MIN, FLY_SPEED_MAX].
// Takes effect on the next worker tick. Returns FR_OK.
int32_t SetSpeed(double uuPerSec);

// Select the active keyboard preset (0 WASD / 1 numpad / 2 arrows). Returns
// FR_OK, or FR_ERR_REFLECT for an out-of-range value.
int32_t SetPreset(int32_t preset);

// Toggle Noclip: true = disable the actor's collision so flight passes through
// walls (velocity-drive is unchanged); false = keep collision. Takes effect on the
// next worker tick (a one-shot SetActorEnableCollision invoke). Returns FR_OK.
int32_t SetNoclip(bool enable);

// Read the live fly state (+ CMC owner for Locate-in-GWorld). Always fills
// FlyStatus; code carries why a read failed.
int32_t GetStatus(FlyStatus& out);

// True when fly is currently engaged (cheap, for the UI badge).
bool IsActive();

// Stop and join the fly worker. Idempotent. Called by SetEnabled(false) and
// from UE5_Shutdown() before the DLL unloads.
void StopWorker();

} // namespace Dunste
