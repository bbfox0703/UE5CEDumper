#pragma once

// ============================================================
// Wirbel — 威亞貝爾 (北部魔法隊小隊長 — pragmatic soldier)
// Teleport: marker save/recall + cursor teleport (BugIt-style).
// Swift battlefield repositioning — the soldier who relocates first.
//
// All property offsets and UFunctions are resolved live from UE
// reflection (Ubel) — nothing hardcoded, UE4/UE5 version-agnostic.
// Full design contract: docs/teleport-spec.md.
// ============================================================

#include <cstdint>
#include "Grimoire.h"

namespace Wirbel {

// Result codes shared across exports, pipe, and mailbox
// (docs/teleport-spec.md §8).
enum TeleportResult : int32_t {
    TP_OK                = 0,
    TP_ERR_NOT_INIT      = -1,   // DLL not initialized / GWorld unavailable
    TP_ERR_NO_CONTROLLER = -2,   // local PlayerController not resolved
    TP_ERR_NO_PAWN       = -3,   // pawn is null (menu/cutscene/spectator)
    TP_ERR_REFLECTION    = -4,   // required property/UFunction not resolved
    TP_ERR_INVOKE        = -5,   // invoke failed or timed out
    TP_ERR_EMPTY_MARKER  = -6,   // marker slot empty / bad slot index
    TP_ERR_MAP_MISMATCH  = -7,   // recall refused: marker saved on another map
    TP_ERR_NO_HIT        = -8,   // trace found no blocking hit
    TP_ERR_NO_CURSOR     = -9,   // mouse position unavailable, center fallback off
    TP_ERR_WRITE_FAILED  = -10,  // tier-2 raw write also failed
};

// Pose always crosses this layer as doubles regardless of engine width
// (UE4 float FVector/FRotator values are widened at the read boundary).
struct Pose {
    double X = 0, Y = 0, Z = 0;
    double Pitch = 0, Yaw = 0, Roll = 0;
};

struct Marker {
    bool Valid = false;
    Pose P{};
    char MapName[Grimoire::TELEPORT_MAPNAME_CAP] = {};
    // [W2-MARKER-PARENTREL] The pose was saved from the raw parent-relative fallback (an attached pawn whose
    // world-space read failed): P is NOT world coordinates, and recalling it drives the pawn there as if it were.
    bool ParentRelative = false;
};

// Camera point-of-view (read-only). The on-screen view is produced by
// APlayerCameraManager.CameraCachePrivate.POV, recomputed every tick from the
// view target — there is NO universal way to SET it (a raw write is overwritten
// next frame; the real "move the camera" mechanisms are Debug Camera / moving
// the view-target pawn, which the teleport path already does). So this layer
// only READS the POV, which is genuinely new information: on games that drive
// the camera independently of the possessed pawn (HD-2D / fixed-view titles),
// the camera diverges from the pawn, and surfacing both makes that visible.
struct Pov {
    Pose Cam{};         // camera world location (X/Y/Z) + rotation (Pitch/Yaw/Roll)
    double Fov = 0;     // effective field-of-view angle, degrees (GetFOVAngle)
    bool HasPawn = false;
    Pose Pawn{};        // pawn world location (X/Y/Z) — for the camera-vs-pawn delta
    uint8_t Source = 0; // 0 = invoke getters (GetCameraLocation/Rotation/FOV)
    // Owner + intra-object offset of the cached POV Location FVector and FOV float
    // (APlayerCameraManager.CameraCachePrivate.POV.Location / .FOV) for the "Locate
    // in GWorld" handoff. Resolved by reflection whenever the camera manager
    // resolves (independent of which read path produced the live values). Offsets
    // are -1 / addr 0 when the cached-POV chain isn't reflected (rare).
    uintptr_t CamOwnerAddr      = 0;   // APlayerCameraManager object
    int32_t   CamLocFieldOffset = -1;  // POV.Location offset within the camera manager
    int32_t   CamRotFieldOffset = -1;  // POV.Rotation offset within the camera manager
    int32_t   CamFovFieldOffset = -1;  // POV.FOV offset within the camera manager
};

// Live movement state read alongside the pose in a single resolution pass.
// PawnAddr is the resolved pawn object (for a "Locate in GWorld" handoff — the
// pawn whose coordinates GetPose reports). Velocity/Acceleration come straight
// off the pawn's UCharacterMovementComponent (UMovementComponent::Velocity is
// the authoritative live velocity; UCharacterMovementComponent::Acceleration is
// CMC-only) — both reflected UPROPERTY FVectors, widened to double like Pose.
// HasMovement is false when the pawn has NO CharacterMovement (vehicle / custom
// movement framework): velocity/acceleration stay zero and the UI shows
// "unavailable" rather than a misleading 0. Speed = |Velocity| (cm/s).
struct MovementState {
    uintptr_t PawnAddr = 0;
    bool HasMovement = false;
    double VelX = 0, VelY = 0, VelZ = 0;
    double AccX = 0, AccY = 0, AccZ = 0;
    double Speed = 0;
    // Owning sub-object + intra-object offset of the location / velocity FVector
    // fields — for the Teleport "Locate value in GWorld" handoff (land the GWorld
    // path on the exact vector field, not just the pawn). Location lives on the
    // RootComponent (USceneComponent::RelativeLocation — parent-relative when the
    // pawn is attached, but it IS the stored location field); velocity on the
    // CharacterMovement (UMovementComponent::Velocity). LocOwnerAddr is set
    // whenever the pose resolves; VelOwnerAddr only when HasMovement. Offsets are
    // -1 / addresses 0 when unresolved.
    uintptr_t LocOwnerAddr   = 0;    // RootComponent (USceneComponent)
    int32_t   LocFieldOffset = -1;   // RelativeLocation offset within RootComponent
    uintptr_t VelOwnerAddr   = 0;    // CharacterMovement (UMovementComponent)
    int32_t   VelFieldOffset = -1;   // Velocity offset within CharacterMovement
    uintptr_t AccOwnerAddr   = 0;    // CharacterMovement (UCharacterMovementComponent)
    int32_t   AccFieldOffset = -1;   // Acceleration offset within CharacterMovement
    uintptr_t RotOwnerAddr   = 0;    // Controller (AController) — owns ControlRotation
    int32_t   RotFieldOffset = -1;   // ControlRotation FRotator offset within the controller
};

// One hop of the *GWorld -> Pawn pointer chain, decomposed into a bake-able
// (offset, deref) pair so a standalone CE-Lua trainer can re-walk it every tick
// (`addr = readQword(addr + Offset)` when Deref, else `addr = addr + Offset`).
struct TrainerChainHop {
    char    Field[48] = {};   // property name (for the emitted Lua comment)
    int32_t Offset = 0;
    bool    Deref = true;
};

// Everything a no-DLL standalone trainer needs baked as static numbers, resolved
// live from reflection during normal gameplay. The pawn chain is the FIXED
// engine-base-class spine (OwningGameInstance -> LocalPlayers[0] -> PlayerController
// -> Pawn); the per-field offsets hang off the pawn / its RootComponent / its
// CharacterMovement. Offsets are -1 when the field didn't resolve.
struct TrainerOffsets {
    int32_t         ChainCount = 0;
    TrainerChainHop Chain[16];      // *GWorld -> ... -> Pawn (pointer)
    int32_t PawnToRoot   = -1;      // APawn.RootComponent (deref)
    int32_t RootToRelLoc = -1;      // USceneComponent.RelativeLocation (inline FVector)
    int32_t FVectorWidth = 0;       // total FVector size: 12 (float) or 24 (double/LWC)
    int32_t PawnToCmc    = -1;      // APawn.CharacterMovement (deref)
    int32_t WalkSpeedOff = -1;      // UCharacterMovementComponent.MaxWalkSpeed (float)
    int32_t GravityOff   = -1;      // UCharacterMovementComponent.GravityScale (float)
    int32_t JumpOff      = -1;      // UCharacterMovementComponent.JumpZVelocity (float)
    int32_t CtrlRotOff   = -1;      // AController.ControlRotation (inline FRotator)
    int32_t CtrlRotSize  = 0;       // 12 (float) or 24 (double/LWC)
    int32_t PawnToController = -1;  // APawn.Controller (deref) — reach ControlRotation for the fly basis
    int32_t MoveModeOff  = -1;      // UCharacterMovementComponent.MovementMode (1 byte enum) — fly
    int32_t VelocityOff  = -1;      // UCharacterMovementComponent.Velocity (inline FVector) — fly
    int32_t VelocitySize = 0;       // 12 (float) or 24 (double/LWC)
};

// Resolve the standalone-trainer offset bundle (§ standalone-ce-lua-trainer).
// Reuses the teleport resolution chain; must be called during normal gameplay
// (a live pawn). Returns TP_OK, or a negative TeleportResult when the chain /
// pawn / RootComponent don't resolve.
int32_t GetTrainerOffsets(TrainerOffsets& out);

// Read the current pawn pose (location from RootComponent.RelativeLocation,
// rotation from Controller.ControlRotation). When the pawn's root is
// attached (vehicle/platform), falls back to invoking K2_GetActorLocation
// for world-space coordinates. outSource (optional): 0 = raw read,
// 1 = invoke path.
// outParentRelative (optional): TRUE when the pawn is ATTACHED (vehicle / mount /
// moving platform) and the world-space invoke failed, so X/Y/Z are the raw
// RelativeLocation -- parent-relative numbers, NOT world coordinates. Required by
// docs/teleport-spec.md:218-220 ("return the raw values anyway ... and a warning
// flag"); the fallback shipped in 2026-07 and the flag did not, so a degraded read
// was indistinguishable from a healthy one and got saved into markers and driven
// back as a world destination. [POSEATTACH-2026-09-10]
int32_t GetPose(Pose& out, char* mapName, int32_t mapNameCap, uint8_t* outSource,
                bool* outParentRelative = nullptr);

// Read the pose AND the live movement state (pawn address + velocity /
// acceleration off the CharacterMovement) in ONE resolution pass — used by the
// Teleport tab to surface current speed and to hand the pawn off to "Locate in
// GWorld". `move` is filled only when the pose read succeeds (TP_OK); its
// HasMovement flag tells the caller whether velocity/acceleration are valid.
int32_t GetPoseAndMovement(Pose& out, char* mapName, int32_t mapNameCap,
                           uint8_t* outSource, MovementState& move,
                           bool* outParentRelative = nullptr);

// Read the current camera POV (PlayerController.PlayerCameraManager): world
// location, rotation, and FOV via the engine's BlueprintCallable getters
// (GetCameraLocation / GetCameraRotation / GetFOVAngle, stable UE4.18→UE5.x).
// Distinct from GetPose (which reads the pawn). Best-effort pawn world location
// is filled for the delta display. Read-only — see the Pov struct note for why
// there is no Set POV. TP_ERR_INVOKE when the game thread is idle (menu/loading).
int32_t GetPov(Pov& out);

// Save the current pose + map name into a marker slot (0..TELEPORT_SLOTS-1).
int32_t SaveMarker(int32_t slot);

// Teleport back to a saved marker. Refuses with TP_ERR_MAP_MISMATCH when the
// current map differs from the marker's, unless force. tierOut (optional):
// 1 = invoke path (K2_SetActorLocation), 2 = raw-write fallback.
int32_t RecallMarker(int32_t slot, bool force, uint8_t* tierOut);

// Teleport to an explicit pose (BugItGo interop, pipe-only path). Bypasses
// the marker store and the map check. hasRot: also restore Pitch/Yaw/Roll.
int32_t RecallExplicit(const Pose& pose, bool hasRot, uint8_t* tierOut);

// Teleport the pawn to the world position under the mouse cursor (or the
// screen center when the cursor is unavailable and fallbackToCenter is set).
// traceChannel is the raw ETraceTypeQuery byte (0 ≈ Visibility by default
// engine mapping; games can remap). zOffset is added to the hit Z so the
// capsule doesn't spawn intersecting the ground. outHit (optional) receives
// the raw hit point (without zOffset) in X/Y/Z.
int32_t TeleportToCursor(double zOffset, int32_t traceChannel,
                         bool fallbackToCenter, Pose* outHit,
                         uint8_t* tierOut, bool* outUsedCenter);

// Read a marker slot. TP_ERR_EMPTY_MARKER when the slot is empty.
int32_t GetMarker(int32_t slot, Marker& out);

// Clear a marker slot.
int32_t ClearMarker(int32_t slot);

// Recall to the system "last" position — the pose captured automatically right
// before the most recent recall / force / BugItGo / cursor teleport, so a
// teleport that lands the pawn somewhere bad can be undone. One-way restore:
// this does NOT itself update the last slot (repeated calls always return to
// the same pre-teleport spot). System-managed — never user-saved. Map check is
// skipped (the last pose is always from moments ago on the current map).
// TP_ERR_EMPTY_MARKER when nothing has been auto-saved yet.
int32_t RecallLast(uint8_t* tierOut);

// Read the system "last" slot for display. TP_ERR_EMPTY_MARKER when empty.
int32_t GetLast(Marker& out);

// BugIt: capture the current pose into the dedicated BugIt slot (and return it
// so the caller can also copy a "BugItGo X Y Z" string). User-triggered single
// slot, distinct from the markers and the system "last" slot — it persists DLL
// side so a later BugItGo can teleport back without the caller holding the pose.
// outParentRelative (optional): the pose came from the raw parent-relative fallback. [W2-MARKER-PARENTREL]
int32_t BugItSave(Pose& out, char* mapName, int32_t mapNameCap, uint8_t* outSource,
                  bool* outParentRelative = nullptr);

// BugItGo: teleport to the pose stored by the most recent BugItSave (restores
// rotation, one-way like a marker recall). TP_ERR_EMPTY_MARKER (no-op) when no
// BugIt has been stored yet.
int32_t BugItGo(uint8_t* tierOut);

// Teleport along the pawn's facing direction by `distance` unreal units
// (negative = backward). horizontalOnly: move on the ground plane (forward from
// Yaw only, Z preserved) — the natural "walk toward the NW compass bearing".
// When false, the full 3D forward vector (including Pitch) is used, so looking
// up/down moves the pawn up/down (fly / noclip feel). The facing comes from the
// pawn's GetActorForwardVector when available, else the controller's
// ControlRotation. outNewPose receives the resulting pose (re-read after the
// move) so the caller can display the landed X/Y/Z/Pitch/Yaw. The pre-jump pose
// is auto-saved (RecallLast undoes it). tierOut: 1 invoke / 2 raw write.
// outLandingKnown (optional): FALSE when the post-move pose re-read FAILED, so
// outNewPose is untouched -- NOT a landing at the origin. The move itself still
// succeeded (that is what the return code says); publish nothing for the landing
// rather than zeros nobody measured. [TPREL-ZEROPOSE-2026-09-10]
int32_t TeleportRelative(double distance, bool horizontalOnly, Pose& outNewPose,
                         uint8_t* tierOut, bool* outLandingKnown = nullptr);

// Force the OS mouse cursor on (show=true) or off — writes the local
// PlayerController's bShowMouseCursor bitfield (a BlueprintReadWrite UPROPERTY;
// SetShowMouseCursor is not a UFUNCTION, so the bit is written directly via the
// reflected FBoolProperty ByteOffset + FieldMask). outState (optional) receives
// the resulting state. NOTE: games that re-set the flag every tick may revert it
// (one-shot write); a captured input mode can still hide the OS cursor. See
// teleport-spec §Cursor.
int32_t SetMouseCursor(bool show, bool* outState);

// Read the current bShowMouseCursor state on the local PlayerController.
int32_t GetMouseCursor(bool* outState);

// Current UWorld object name ("" when unavailable). Cheap — no chain walk.
bool GetCurrentMapName(char* buf, int32_t cap);

} // namespace Wirbel
