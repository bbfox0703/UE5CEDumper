#pragma once

// ============================================================
// Frieren — 芙莉蓮, 葬送のフリーレン (主角 — Protagonist)
// ExportAPI: ~30 C ABI exports for CE Lua bridge
// ============================================================

#include <cstdint>
#include <Windows.h>

extern "C" {

// === Initialization ===
__declspec(dllexport) bool     UE5_Init();
__declspec(dllexport) void     UE5_Shutdown();
__declspec(dllexport) uint32_t UE5_GetVersion();

// Combined init + pipe server start — called by CEPlugin's InjectDLL
// so that a single entry point activates everything in the game process.
//
// ASYNCHRONOUS since build 2932 (audit #5 AB2): it SPAWNS the work and returns
// immediately, because one of its callers is Cheat Engine's injection stub, which
// frees the remote page after a hard 10 s (or 1 s on the APC path) whether we
// finished or not — and the `ret` onto that freed page crashes the GAME.
// The return value therefore means "an auto-start is running or was launched",
// NOT "init finished". Poll `Mimic::InitState` in the mailbox for readiness;
// every emitted script already does (CeReadinessLua::AppendPollLoop).
__declspec(dllexport) bool     UE5_AutoStart();

// Same work, run on the CALLING thread. Not exported — for callers that already
// own a dedicated thread and must not spawn another (DllMain's auto-start
// thread). Shares the one-at-a-time latch with UE5_AutoStart.
bool UE5_AutoStartBlocking();

// === Global Pointers ===
__declspec(dllexport) uintptr_t UE5_GetGObjectsAddr();
__declspec(dllexport) uintptr_t UE5_GetGNamesAddr();

// === Object Queries ===
__declspec(dllexport) int32_t   UE5_GetObjectCount();
__declspec(dllexport) uintptr_t UE5_GetObjectByIndex(int32_t index);
__declspec(dllexport) bool      UE5_GetObjectName(uintptr_t obj, char* buf, int32_t bufLen);
__declspec(dllexport) bool      UE5_GetObjectFullName(uintptr_t obj, char* buf, int32_t bufLen);
__declspec(dllexport) uintptr_t UE5_GetObjectClass(uintptr_t obj);
__declspec(dllexport) uintptr_t UE5_GetObjectOuter(uintptr_t obj);

// === Search ===
__declspec(dllexport) uintptr_t UE5_FindObject(const char* fullPath);
__declspec(dllexport) uintptr_t UE5_FindClass(const char* className);

// === WalkClass (batch mode) ===
__declspec(dllexport) int32_t   UE5_WalkClassBegin(uintptr_t uclassAddr);
__declspec(dllexport) bool      UE5_WalkClassGetField(int32_t index,
                                    uintptr_t* outAddr,
                                    char* nameOut, int32_t nameBufLen,
                                    char* typeOut, int32_t typeBufLen,
                                    int32_t* offsetOut,
                                    int32_t* sizeOut);
__declspec(dllexport) void      UE5_WalkClassEnd();

// === FName Resolution ===
__declspec(dllexport) bool      UE5_ResolveFName(uint64_t fname, char* buf, int32_t bufLen);

// === Object Decryption (GAP #1) ===
// Set a custom decryption function for encrypted GObjects pointers.
// Pass NULL to clear (revert to identity/no decryption).
// Must be called BEFORE UE5_Init() — decryption is needed during scanning.
// UE5_AutoStart() does NOT support decryption (use manual Lua flow).
__declspec(dllexport) void      UE5_SetObjectDecryption(uintptr_t (*decryptFunc)(uintptr_t));

// === Property Detail Queries (for CE Lua dissect) ===
// Returns the FieldMask byte for a BoolProperty field (0 if not a bool).
// fieldAddr: FProperty* address from UE5_WalkClassGetField.
__declspec(dllexport) int32_t   UE5_GetFieldBoolMask(uintptr_t fieldAddr);

// Returns the UScriptStruct* for a StructProperty (0 if not a struct).
// fieldAddr: FProperty* address from UE5_WalkClassGetField.
__declspec(dllexport) uintptr_t UE5_GetFieldStructClass(uintptr_t fieldAddr);

// Returns the PropertyClass (UClass*) for an ObjectProperty (0 if not an object prop).
// fieldAddr: FProperty* address from UE5_WalkClassGetField.
// Same offset as StructProperty::Struct — separate export for semantic clarity.
__declspec(dllexport) uintptr_t UE5_GetFieldPropertyClass(uintptr_t fieldAddr);

// Returns the PropertiesSize of a UClass/UStruct (total struct byte size).
__declspec(dllexport) int32_t   UE5_GetClassPropsSize(uintptr_t classAddr);

// === UFunction Invocation ===
// Find first non-CDO instance of a class by name. Returns UObject* address or 0.
__declspec(dllexport) uintptr_t UE5_FindInstanceOfClass(const char* className);

// Find a UFunction by name on a UClass. Returns UFunction* address or 0.
__declspec(dllexport) uintptr_t UE5_FindFunctionByName(uintptr_t classAddr, const char* funcName);

// Call UObject::ProcessEvent(ufunc, params). Returns 0 on success, negative on error.
// params must point to a buffer of at least UFunction::ParmsSize bytes.
// Error codes: -1=bad args, -2=vtable read fail, -3=ProcessEvent not found, -4=exception.
__declspec(dllexport) int32_t   UE5_CallProcessEvent(uintptr_t instance, uintptr_t ufunc, uintptr_t params);

// Direct ProcessEvent call from the calling thread, bypassing
// GameThreadDispatch. Safe for pure native helpers (FUNC_Native|FUNC_Static
// — KismetMathLibrary, KismetStringLibrary, BFLs without game-state side
// effects). NOT safe for instance methods that read/write actor state from
// off-thread; use UE5_CallProcessEvent for those.
// Error codes match UE5_CallProcessEvent: -1=bad args, -2=vtable, -3=PE
// vtable offset unresolved, -4=SEH exception.
__declspec(dllexport) int32_t   UE5_CallProcessEventDirect(uintptr_t instance, uintptr_t ufunc, uintptr_t params);

// Force the game-thread ProcessEvent hook to install immediately (race-safe /
// idempotent). Returns true if the hook is active afterward. The Live PE
// profiler (Linie) calls this at pe_profile_start so it can count the game's
// own PE calls without first issuing an invoke.
__declspec(dllexport) bool      UE5_EnsureGameThreadHook();

// ProcessEvent vtable offset once detected (>=0), else negative (-2 not attempted,
// -1 detection failed). Lets the profiler tell "ProcessEvent not found" apart from
// "found but the hook couldn't install" when reporting why recording is inactive.
__declspec(dllexport) int       UE5_GetProcessEventOffset();

// === Debug Camera (robust force on/off; shared by UI pipe + CE Lua) ===
// Read the live Debug Camera state. 1 = ON (a DebugCameraController is
// possessing the player), 0 = OFF, -1 = unknown / no live CheatManager.
// Two-hop reflection read of DebugCameraController.OriginalControllerRef.
__declspec(dllexport) int32_t   UE5_GetDebugCameraState();

// Force Debug Camera ON (enable!=0) or OFF (enable==0). Idempotent — no-op if
// already in the desired state. Fires ToggleDebugCamera only when needed; if a
// disable can't take (Shipping builds that strip DisableDebugCamera), switches
// the local player's controller back to the original PlayerController by hand.
// Returns the resulting state (1/0) or -1 on error. All offsets resolved live
// from reflection (UE4/UE5 version-agnostic).
__declspec(dllexport) int32_t   UE5_SetDebugCamera(int32_t enable);

// === Teleport (Wirbel: marker save/recall + cursor teleport) ===
// All return Wirbel result codes (0 = OK, negatives per docs/teleport-spec.md
// §8). Pose arrays are X,Y,Z,Pitch,Yaw,Roll as doubles regardless of the
// engine's FVector width (UE4 floats are widened at the boundary).
// NOTE for CE: executeCodeEx cannot retrieve these return values — CE Lua
// integration goes through the Mimic mailbox (CMD_TELEPORT=8) instead.
__declspec(dllexport) int32_t   UE5_TeleportGetPose(double* outPose6,
                                    char* outMapName, int32_t mapNameCap);
__declspec(dllexport) int32_t   UE5_TeleportSaveMarker(int32_t slot);
__declspec(dllexport) int32_t   UE5_TeleportRecallMarker(int32_t slot, int32_t force);
__declspec(dllexport) int32_t   UE5_TeleportToCursor(double zOffset,
                                    int32_t traceChannel, int32_t fallbackToCenter);
__declspec(dllexport) int32_t   UE5_TeleportGetMarker(int32_t slot, double* outPose6,
                                    char* outMapName, int32_t mapNameCap);
__declspec(dllexport) int32_t   UE5_TeleportClearMarker(int32_t slot);
// Recall the system "last" pose (auto-saved before every recall/force/BugItGo/
// cursor jump) — one-way restore so a bad teleport can be undone.
__declspec(dllexport) int32_t   UE5_TeleportRecallLast();
__declspec(dllexport) int32_t   UE5_TeleportGetLast(double* outPose6,
                                    char* outMapName, int32_t mapNameCap);
// Read the camera POV (read-only). outPov11 receives 11 doubles:
//   [0..5] camera X,Y,Z,Pitch,Yaw,Roll  [6] FOV
//   [7..9] pawn X,Y,Z (for the camera-vs-pawn delta)  [10] hasPawn (1/0)
__declspec(dllexport) int32_t   UE5_TeleportGetPov(double* outPov11);
// Teleport along the pawn's facing by `distance` uu (negative = backward).
// horizontalOnly!=0 keeps Z (ground-plane move); 0 uses the full 3D forward.
// outNewPose6 (nullable) receives the resulting X,Y,Z,Pitch,Yaw,Roll.
__declspec(dllexport) int32_t   UE5_TeleportRelative(double distance,
                                    int32_t horizontalOnly, double* outNewPose6);
// Teleport to explicit world coordinates (force — no map check). hasRot!=0 also
// restores Pitch/Yaw/Roll.
__declspec(dllexport) int32_t   UE5_TeleportRecallExplicit(double x, double y, double z,
                                    double pitch, double yaw, double roll, int32_t hasRot);
// Force the mouse cursor on (show!=0) / off — writes bShowMouseCursor on the
// local PlayerController. Returns a Wirbel code; *outState (nullable) = result.
__declspec(dllexport) int32_t   UE5_SetMouseCursor(int32_t show, int32_t* outState);
// Read the current bShowMouseCursor state. *outState (nullable) = 1/0.
__declspec(dllexport) int32_t   UE5_GetMouseCursor(int32_t* outState);

// === GodMode (Solitar: force AActor::bCanBeDamaged) ===
// Stateful toggle. GodMode ON ⇒ the local pawn's bCanBeDamaged is forced FALSE
// and re-asserted on a timer (survives respawns). Pure memory write — no invoke.
// Returns the OBSERVED live state (1 = immune, 0 = can be damaged) or a negative
// Solitar::ProtectResult (docs/godmode-spec.md §6.3). CE Lua uses the Mimic
// mailbox (CMD_PROTECT=9) — executeCodeEx cannot read these return values.
__declspec(dllexport) int32_t   UE5_SetGodMode(int32_t enable);
__declspec(dllexport) int32_t   UE5_GetGodMode();
// Combined state for the UI badge: *outWant = desired toggle (1/0, survives
// reconnect), *outLive = observed state (1/0, -1 if no pawn), *outResolvable =
// could the live pawn be read (1/0). All params nullable. Returns 0.
//
// Consumed by the UI via the `get_protect_state` pipe command since build 3203
// (audit #5 AD4). It had shipped with zero clients while the badge read the
// collapsed UE5_GetGodMode tri-state, which cannot tell "never enabled" from
// "enabled and waiting for a pawn" or from "engaged and drifted this instant".
// The wire name for *outLive is `godmode`, not `live` — see Fern.cpp.
__declspec(dllexport) int32_t   UE5_GetProtectState(int32_t* outWant,
                                    int32_t* outLive, int32_t* outResolvable);

// === Movement tuning (Laufen) ===
// Single-call "set percent" for the local pawn's CharacterMovement float knobs.
// knobId: 0 = MaxWalkSpeed, 1 = GravityScale, 2 = JumpZVelocity. `percent` is the
// user-facing slider value — 100 (±0.5) means OFF (restore base). For knobId 2
// (jump) `percent` is jump HEIGHT % (applied JumpZVelocity multiplier = sqrt).
// Returns 1 (active) / 0 (off) / negative Laufen::MoveResult. CE Lua uses the
// Mimic mailbox (CMD_MOVEMENT=10) — executeCodeEx cannot read return values.
__declspec(dllexport) int32_t   UE5_SetMovementPercent(int32_t knobId, double percent);

// Set the pawn's UCharacterMovementComponent::GravityDirection (UE5.4+) to the
// (x,y,z) vector (normalized DLL-side). Sentinel (0,0,0) = OFF (restore the
// captured default). Returns 1 (active) / 0 (off) / negative Laufen::MoveResult
// (MR_ERR_REFLECT when the field isn't reflected — pre-5.4). CE Lua uses the
// Mimic mailbox (CMD_MOVEMENT=10, knobId=3, paramsData = 3 doubles x/y/z).
__declspec(dllexport) int32_t   UE5_SetGravityDirection(double x, double y, double z);

// === Time dilation (Hemmung: global slow-mo / freeze / speed-up) ===
// Hold a reflected dilation float at an ABSOLUTE value against per-tick game
// overwrites. `target` = 0 (global AWorldSettings::TimeDilation, whole-world) /
// 1 (local pawn AActor::CustomTimeDilation, per-actor). `value` = dilation
// (1.0 = normal, 0.5 = half, 0 = frozen, 2.0 = double), clamped DLL-side to
// [TIME_DILATION_MIN, TIME_DILATION_MAX]. Returns 1 (active) or a negative
// Hemmung::TimeResult. CE Lua uses the Mimic mailbox (CMD_TIME=15) — executeCodeEx
// cannot read return values. Reset via UE5_ResetTimeDilation.
__declspec(dllexport) int32_t   UE5_SetTimeDilation(int32_t target, double value);

// Restore `target` to its captured natural value and stop holding it. Returns
// Hemmung::TimeResult (0 = ok / negative error).
__declspec(dllexport) int32_t   UE5_ResetTimeDilation(int32_t target);

// === Fly (Dunste) — no-gravity keyboard-driven 3D flight ===
// Toggle engine-flying (MOVE_Flying, collision preserved) on the local pawn,
// held by a re-assert worker that also samples the keyboard and drives the CMC
// Velocity each ~60 Hz tick. enable!=0 = on, 0 = off. Returns 1 (active) /
// 0 (off) / negative Dunste::FlyResult. Input is read DLL-side (no per-frame
// IPC); the UI/mailbox only toggle + set speed/preset. CE Lua uses the Mimic
// mailbox (CMD_FLY=11).
__declspec(dllexport) int32_t   UE5_SetFly(int32_t enable);
// Flight speed in uu/s (clamped to [FLY_SPEED_MIN, FLY_SPEED_MAX]). Returns FR_OK.
__declspec(dllexport) int32_t   UE5_SetFlySpeed(double uuPerSec);
// Keyboard preset: 0 = WASD, 1 = numpad, 2 = arrows (movement + up/down; turn is
// the mouse). Returns FR_OK, or negative Dunste::FlyResult for out-of-range.
__declspec(dllexport) int32_t   UE5_SetFlyPreset(int32_t preset);
// Noclip: enable!=0 = position-drive (fly through walls, works even where the
// game overwrites Velocity); 0 = velocity-drive (collision preserved). FR_OK.
__declspec(dllexport) int32_t   UE5_SetFlyNoclip(int32_t enable);

// === Mailbox (CE Lua shared memory interface) ===
// Returns the address of the g_invokeMailbox buffer.
// CE Lua can also use getAddress("g_invokeMailbox") directly.
__declspec(dllexport) uintptr_t UE5_GetMailboxAddr();

// === Pipe Server ===
__declspec(dllexport) bool      UE5_StartPipeServer();
__declspec(dllexport) void      UE5_StopPipeServer();
__declspec(dllexport) bool      UE5_IsPipeConnected();

} // extern "C"
