// ============================================================
// Mimic — 寶箱怪 (經典梗 — The Classic Gag)
// Mailbox: CE Lua shared-memory command interface
//
// CE Lua uses readQword/writeQword (ReadProcessMemory/WriteProcessMemory)
// to communicate with the DLL without needing CreateRemoteThread.
//
// CE Lua workflow:
//   1. mb = getAddress("g_invokeMailbox")
//   2. Write command fields (class_name, func_name, params, etc.)
//   3. Write cmd field (trigger) — MUST be written LAST
//   4. Poll status field until == 1 (done)
//   5. Read result fields
// ============================================================

#pragma once

#include <cstdint>

namespace Mimic {

// Mailbox commands (CE writes to cmd field)
enum Cmd : int32_t {
    CMD_IDLE            = 0,
    CMD_INVOKE          = 1,  // Call ProcessEvent (instance_addr, ufunc_addr, params_data)
    CMD_FIND_INSTANCE   = 2,  // Find non-CDO instance by class name
    CMD_FIND_FUNCTION   = 3,  // Find UFunction by name on instance's class
    CMD_INVOKE_BY_NAME  = 4,  // Combined: find instance + find function + invoke
    CMD_LIST_FUNCTIONS  = 5,  // List all UFunctions on an instance's class (paginated)
    CMD_LIST_INSTANCES  = 6,  // List all live (non-CDO) instances of a class (paginated)
    CMD_SET_DEBUG_CAMERA = 7, // Robust Debug Camera force on/off / query.
                              //   Input:  instanceAddr = 0 (OFF) / 1 (ON) / 2 (query, no change)
                              //   Output: result = resulting state (1=ON, 0=OFF, -1=error)
    CMD_TELEPORT        = 8,  // Teleport (Wirbel): marker save/recall + cursor teleport.
                              //   Input:  instanceAddr = op (TeleportOp below)
                              //           ufuncAddr    = slot (0..2) for SAVE/RECALL/GET/CLEAR
                              //           paramsData (op CURSOR only, read BEFORE outputs):
                              //             [0..7] double zOffset, [8] u8 traceChannel,
                              //             [9] u8 fallbackToCenter
                              //   Output: result = Wirbel code (0 OK, negatives per
                              //           docs/teleport-spec.md §8)
                              //           paramsData pose block (GET_POSE/SAVE/GET_MARKER):
                              //             [0..47]   6 doubles X,Y,Z,Pitch,Yaw,Roll
                              //             [48..175] mapName (null-terminated)
                              //             [176]     u8 source (0 raw / 1 invoke)
                              //             [177]     u8 tier (1 invoke / 2 raw write)
                              //           op CURSOR output:
                              //             [0..23] 3 doubles hit point, [177] tier,
                              //             [178] u8 usedCenter
    CMD_PROTECT         = 9,  // GodMode (Solitar): force AActor::bCanBeDamaged off.
                              //   Input:  instanceAddr = op (ProtectOp below)
                              //           ufuncAddr    = value (0/1) for SET ops
                              //   Output: result = observed state (1 immune /
                              //           0 can-be-damaged) or negative
                              //           Solitar::ProtectResult (docs/godmode-spec.md §6.3)
                              //           paramsData (op GET_STATE):
                              //             [0] u8 want (desired toggle 1/0)
                              //             [1] u8 live (observed 1/0, 0xFF if no pawn)
                              //             [2] u8 resolvable (1/0)
    CMD_MOVEMENT        = 10, // Movement tuning (Laufen): set a CharacterMovement
                              //   float knob to a percent (100% = OFF), or the
                              //   gravity DIRECTION vector (knobId 3).
                              //   Input:  instanceAddr = knobId (0 MaxWalkSpeed /
                              //             1 GravityScale / 2 JumpZVelocity /
                              //             3 GravityDirection, UE5.4+)
                              //           knob 0-2: paramsData[0..7] = double percent
                              //             (user slider %; 100 = off. knob 2 = jump
                              //             HEIGHT %, DLL applies sqrt).
                              //           knob 3:   paramsData[0..23] = 3 doubles
                              //             x/y/z (DLL normalizes; (0,0,0) = off).
                              //   Output: result = 1 (active) / 0 (off) / negative
                              //             Laufen::MoveResult.
    CMD_FLY             = 11, // Fly (Dunste): no-gravity keyboard-driven 3D flight.
                              //   Input:  instanceAddr = op (FlyOp below).
                              //     SET_ENABLED: ufuncAddr = 1 (on) / 0 (off).
                              //     SET_SPEED:   paramsData[0..7] = double uu/s.
                              //     SET_PRESET:  ufuncAddr = preset (0/1/2).
                              //     GET_STATE:   (no input).
                              //   Output: result = 1 (active) / 0 (off) / negative
                              //     Dunste::FlyResult. GET_STATE writes paramsData:
                              //       [0] u8 active, [1] u8 preset, [8..15] double speed.
    CMD_FOREGROUND      = 12, // Keep-Foreground lock (Grausam): make the game
                              //   always believe it is the foreground app so it
                              //   never idles/pauses its game thread when unfocused.
                              //   Thread-agnostic (pure Win32 hook + WndProc
                              //   subclass) — processed on the mailbox polling
                              //   thread, so it works even while the game thread
                              //   is asleep.
                              //   Input:  instanceAddr = op (ForegroundOp below)
                              //           ufuncAddr    = value (1 on / 0 off) for SET
                              //   Output: result = 1 (on) / 0 (off) / <0 error
    CMD_QUERY_PTR       = 13, // Resolve a global pointer for CE Lua display —
                              //   GWorld / GameEngine (executeCodeEx can't read
                              //   export returns, so CE goes through the mailbox).
                              //   Read-only + thread-agnostic (reads a cached
                              //   global / iterates GObjects), so it runs on the
                              //   mailbox polling thread even while the game thread
                              //   is idle.
                              //   Input:  instanceAddr = op (QueryPtrOp below)
                              //   Output: result = 0 (found) / -1 (not resolved),
                              //           paramsData (op GWORLD):
                              //             [0..7]  uint64 &GWorld slot (the address
                              //                     of the global UWorld* pointer)
                              //             [8..15] uint64 UWorld* (slot deref, 0 if
                              //                     no world currently loaded)
                              //           paramsData (op GAME_ENGINE):
                              //             [0..7]  uint64 UEngine* instance
                              //             [8..15] uint64 UClass* (its class)
                              //             [16..143] class name (null-terminated)
    CMD_SEETHROUGH      = 14, // See-through occluders (Schlacht): toggle the worker
                              //   that hides the nearest N non-Pawn actors blocking the
                              //   camera→view ray. Self-contained (its own worker does
                              //   the game-thread invokes), so the mailbox handler just
                              //   flips it on the polling thread.
                              //   Input:  instanceAddr = pierce count (>=1)
                              //           ufuncAddr    = value (1 on / 0 off)
                              //   Output: result = 1 (on) / 0 (off) / <0 error
    CMD_TIME            = 15, // Time dilation (Hemmung): hold a reflected dilation
                              //   float at an absolute value (global slow-mo /
                              //   freeze / speed-up). Pure reflected memory write on
                              //   the polling thread — no game thread needed.
                              //   Input:  instanceAddr = op (TimeOp below)
                              //           ufuncAddr    = target (0 global / 1 pawn)
                              //           paramsData (op SET, read BEFORE outputs):
                              //             [0..7] double value (1.0 normal, 0 frozen)
                              //   Output: result = 1 (active) / 0 (off) / negative
                              //           Hemmung::TimeResult
};

// CMD_TELEPORT op codes (written into instanceAddr by CE Lua / pipe bridge)
enum TeleportOp : uint64_t {
    TP_OP_GET_POSE     = 0,
    TP_OP_SAVE         = 1,
    TP_OP_RECALL       = 2,
    TP_OP_RECALL_FORCE = 3,
    TP_OP_CURSOR       = 4,
    TP_OP_GET_MARKER   = 5,
    TP_OP_CLEAR_MARKER = 6,
    TP_OP_RECALL_LAST  = 7,  // recall the system "last" pose (auto-saved before
                             //   every recall/force/BugItGo/cursor jump). slot ignored.
    TP_OP_GET_LAST     = 8,  // read the system "last" slot (pose block output)
    TP_OP_BUGIT_SAVE   = 9,  // store current pose into the BugIt slot (pose block
                             //   output, like SAVE); user-triggered, slot ignored.
    TP_OP_BUGIT_GO     = 10, // teleport to the stored BugIt pose (no-op when empty)
    TP_OP_GET_POV      = 11, // read the camera POV (read-only). slot ignored.
                             //   Output POV block in paramsData:
                             //     [0..47]  6 doubles cam X,Y,Z,Pitch,Yaw,Roll
                             //     [48..55] double FOV
                             //     [56..79] 3 doubles pawn X,Y,Z (delta display)
                             //     [80]     u8 hasPawn   [81] u8 source
    TP_OP_RELATIVE     = 12, // teleport along the pawn's facing by a distance.
                             //   Input  paramsData: [0..7] double distance (uu;
                             //     negative = backward), [8] u8 mode (0 = horizontal
                             //     keep-Z, 1 = full 3D include pitch). slot ignored.
                             //   Output pose block (resulting pose, like GET_POSE) +
                             //     [177] tier.
    TP_OP_EXPLICIT     = 13, // teleport to explicit world coordinates (force; no
                             //   map check). Input paramsData: [0..47] 6 doubles
                             //   X,Y,Z,Pitch,Yaw,Roll, [48] u8 hasRot (restore
                             //   rotation). slot ignored. Output: [177] tier.
    TP_OP_SET_CURSOR   = 14, // force the mouse cursor on/off (writes
                             //   APlayerController.bShowMouseCursor). Input:
                             //   ufuncAddr (slot field) = 1 (show) / 0 (hide).
                             //   Output: result = Wirbel code, paramsData[0] =
                             //   resulting state (1/0).
    TP_OP_GET_CURSOR   = 15, // read the current bShowMouseCursor state. slot
                             //   ignored. Output: result = code, paramsData[0] =
                             //   state (1/0).
};

// CMD_FLY op codes (written into instanceAddr by CE Lua / pipe bridge).
enum FlyOp : uint64_t {
    FLY_OP_SET_ENABLED = 0, // ufuncAddr = 1 (on) / 0 (off). result = active/off/neg.
    FLY_OP_SET_SPEED   = 1, // paramsData[0..7] = double uu/s. result = FlyResult.
    FLY_OP_SET_PRESET  = 2, // ufuncAddr = preset (0/1/2). result = FlyResult.
    FLY_OP_GET_STATE   = 3, // result = FlyResult; paramsData[0] = active, [1] =
                            //   preset, [2] = noclip, [8..15] = speed double.
    FLY_OP_SET_NOCLIP  = 4, // ufuncAddr = 1 (noclip/through-walls) / 0 (collision).
};

// CMD_FOREGROUND op codes (written into instanceAddr by CE Lua / pipe bridge).
enum ForegroundOp : uint64_t {
    FG_OP_SET = 0, // ufuncAddr = 1 (on) / 0 (off). result = 1/0/negative.
    FG_OP_GET = 1, // no input. result = current state (1/0).
};

// CMD_QUERY_PTR op codes (written into instanceAddr by CE Lua / pipe bridge).
enum QueryPtrOp : uint64_t {
    QUERY_OP_GWORLD      = 0, // Output paramsData: [0..7] &GWorld slot,
                              //   [8..15] UWorld* (slot deref).
    QUERY_OP_GAME_ENGINE = 1, // Output paramsData: [0..7] UEngine* instance,
                              //   [8..15] UClass*, [16..143] class name.
    QUERY_OP_GENGINE_SLOT = 2, // Output paramsData: [0..7] &GEngine slot,
                              //   [8..15] UEngine* (slot deref). Same shape as
                              //   QUERY_OP_GWORLD, and preferred over op 1 for a CE
                              //   symbol: the SLOT is restart-stable, so the record
                              //   auto-follows engine recreation instead of freezing
                              //   a UEngine* snapshot. Errors when no GEngine AOB
                              //   validated — callers fall back to op 1.
};

// CMD_TIME op codes (written into instanceAddr by CE Lua / pipe bridge).
enum TimeOp : uint64_t {
    TIME_OP_SET   = 0, // ufuncAddr = target (0 global / 1 pawn),
                       //   paramsData[0..7] = double value. result = 1/0/negative.
    TIME_OP_RESET = 1, // ufuncAddr = target. result = Hemmung::TimeResult.
};

// CMD_PROTECT op codes (written into instanceAddr by CE Lua / pipe bridge).
enum ProtectOp : uint64_t {
    PROTECT_OP_SET_GODMODE = 0, // ufuncAddr = 1 (ON) / 0 (OFF). Output result =
                                //   observed state.
    PROTECT_OP_GET_GODMODE = 1, // Output result = observed state.
    PROTECT_OP_GET_STATE   = 2, // Output paramsData[0..2] = want / live / resolvable.
    // Reserved (v2 — docs/godmode-spec.md §5.4): PROTECT_OP_SET_ACTOR_BOOL = 3,
    // generic "force any reflected bool" using instanceAddr=obj + className=prop.
};

// Mailbox status (DLL writes to status field)
enum Status : int32_t {
    STATUS_IDLE         = 0,
    STATUS_DONE         = 1,
    STATUS_PROCESSING   = 0xFF,
};

// Auto-start readiness (DLL writes to the initState field).
//
// Why it lives in the mailbox instead of being its own exported global: CE Lua
// then reads it with one getAddress("g_invokeMailbox") + readInteger — a PURE
// MEMORY READ, no executeCodeEx and therefore no CreateRemoteThread, which
// games may block (the reason UE5CEDumper.CT deliberately avoids executeCodeEx
// during injection). It also reuses the former `reserved` alignment slot, so
// the struct layout and every later field offset are unchanged and the proxy
// .def files need no new DATA entry.
//
// Ordering contract: the DLL must publish READY/FAILED only AFTER the pipe
// server call has returned, so a poller that observes READY can connect
// immediately.
enum InitState : int32_t {
    INIT_IDLE    = 0, // DLL mapped; the auto-start thread has not begun work
    INIT_RUNNING = 1, // AOB scan / pipe-server start in progress
    INIT_READY   = 2, // init finished AND the pipe server is up
    INIT_FAILED  = 3, // init finished but the pipe server failed to start
    INIT_SKIPPED = 4, // auto-start deliberately skipped — CE plugin host, or
                      // another UE5CEDumper instance already owns the pipe
};

// Mailbox structure (exported as global variable)
// CE Lua accesses via getAddress("g_invokeMailbox") + offset reads/writes
//
// Total size: ~1848 bytes (fits in single page)
#pragma pack(push, 1)
struct MailboxData {
    volatile int32_t  cmd;              // 0x000: Cmd enum (CE writes LAST as trigger)
    volatile int32_t  status;           // 0x004: Status enum (DLL writes)
    volatile int32_t  result;           // 0x008: Return code (0=ok, negative=error)
    volatile int32_t  initState;        // 0x00C: InitState enum (DLL writes).
                                        //        CE Lua polls this to learn when
                                        //        auto-start finished, instead of
                                        //        sleeping a fixed budget.
                                        //        (Was `reserved` alignment padding
                                        //        — same type and offset, so the
                                        //        struct layout is unchanged.)

    volatile uint64_t instanceAddr;     // 0x010: UObject* instance
    volatile uint64_t ufuncAddr;        // 0x018: UFunction* address

    uint16_t          parmsSize;        // 0x020: UFunction::ParmsSize (DLL fills)
    uint16_t          numParms;         // 0x022: UFunction::NumParms (DLL fills)
    uint32_t          functionFlags;    // 0x024: EFunctionFlags (DLL fills)

    char              className[256];   // 0x028: Input: class name (null-terminated)
    char              funcName[256];    // 0x128: Input: function name (null-terminated)
    char              errorMsg[256];    // 0x228: Output: error description

    uint8_t           paramsData[1024]; // 0x328: In/Out: inline parameter buffer
                                        //        Covers 99%+ of UFunctions
                                        //
                                        // CMD_LIST_FUNCTIONS uses this buffer for paginated results:
                                        //   Input:  paramsData[0..3] = page index (uint32, 0-based)
                                        //   Output: parmsSize = total function count
                                        //           numParms  = returned count this page
                                        //           functionFlags = total pages
                                        //   Each entry is 64 bytes (max 15 per page):
                                        //     [0..7]   addr (uint64)
                                        //     [8..9]   parmsSize (uint16)
                                        //     [10..11] numParms (uint16)
                                        //     [12..15] flags (uint32)
                                        //     [16..63] name (48 chars, null-terminated)
                                        //
                                        // CMD_LIST_INSTANCES uses this buffer for paginated results:
                                        //   Input:  paramsData[0..3] = page index (uint32, 0-based)
                                        //           className field = exact class name (case-insensitive)
                                        //   Output: parmsSize = total live instance count (capped at 0xFFFF)
                                        //           numParms  = returned count this page
                                        //           functionFlags = total pages
                                        //           instanceAddr  = UClass* of the enumerated class
                                        //                           (0 = not resolved; contract 2+)
                                        //           ufuncAddr     = byte offset of UObject::ClassPrivate,
                                        //                           i.e. Grimoire::OFF_UOBJECT_CLASS
                                        //                           (contract 2+)
                                        //   Each entry is 8 bytes (max 128 per page):
                                        //     [0..7]   UObject* (uint64)
                                        //
                                        // WHY the two extra outputs (contract 2, audit #5 AA2/AA3):
                                        //   A caller that CACHES these pointers and writes to them on a
                                        //   timer cannot tell a still-live instance from a recycled slot.
                                        //   With (UClass*, classOffset) it can re-read obj->ClassPrivate
                                        //   before each write and refuse when the slot now holds a
                                        //   DIFFERENT class — which is the write that actually corrupts,
                                        //   since the frozen offset means something else there.
                                        //   A slot recycled by ANOTHER INSTANCE OF THE SAME CLASS is not
                                        //   a hazard for a class-wide freeze: that instance is a target
                                        //   too, and the next rescan would enumerate it anyway.
                                        //   Both are class-wide, not per-entry, so the entries stay
                                        //   8 bytes and the page size is unchanged — this is additive.
};
#pragma pack(pop)

static_assert(sizeof(MailboxData) <= 4096, "MailboxData must fit in a single page");

/// Start the mailbox polling thread.
/// Called from dllmain.cpp DLL_PROCESS_ATTACH.
void StartThread();

/// Stop the mailbox polling thread and clean up.
/// Called from UE5_Shutdown().
void StopThread();

/// Returns the address of the mailbox buffer.
uintptr_t GetAddress();

} // namespace Mimic

// Exported global — CE Lua uses getAddress("g_invokeMailbox") to find it.
// No function call needed! CE resolves the symbol from the DLL export table.
extern "C" __declspec(dllexport) extern Mimic::MailboxData g_invokeMailbox;

// ============================================================
// CE Lua ↔ DLL contract version
// ============================================================
//
// WHY THIS IS NOT THE BUILD NUMBER. A .CT the user saved months ago is still
// perfectly valid against a newer DLL as long as nothing it depends on moved.
// Versioning on the build would condemn every old script on every release; what
// actually has to match is the CONTRACT — and it changes rarely. Same shape as
// Genau::kVersionDetectLogicRev, which bumps only when the detection LOGIC
// changes rather than on every build.
//
// WHAT IS IN THE CONTRACT (change any of these ⇒ bump):
//   1. MailboxData field offsets, or its size
//   2. Cmd values
//   3. Op values inside a command (TeleportOp / FlyOp / ProtectOp / QueryPtrOp /
//      TimeOp / ForegroundOp, and Laufen's knobIds)
//   4. Status / InitState values, or result-code meanings
//   5. The meaning of an EXISTING field at an existing offset
//
// WHAT IS NOT (additive ⇒ bump MAILBOX_CONTRACT only, leave the MIN alone):
//   a new Cmd, a new op at a previously unused number, a new knobId. Old scripts
//   never referenced them, so they stay valid.
//
// TWO numbers, because the answer is a RANGE and the two failure directions need
// different advice:
//   script <  MAILBOX_CONTRACT_MIN  → the script is too old   → regenerate the .CT
//   script >  MAILBOX_CONTRACT      → the DLL is too old       → update the DLL
//   otherwise                        → compatible, however many builds apart
// Today's "new .CT + old DLL" case is silent corruption; the second line is the
// whole reason the check reads a range instead of testing equality.
namespace Mimic {

/// Current contract revision. Bump when any item 1-5 above changes.
/// 2 (build 2926, audit #5 AA2/AA3): CMD_LIST_INSTANCES additionally publishes the
///   enumerated UClass* in `instanceAddr` and the UObject::ClassPrivate byte offset
///   in `ufuncAddr`. ADDITIVE — both fields were unused outputs for this command, so
///   no contract-1 script referenced them and MAILBOX_CONTRACT_MIN stays at 1.
constexpr int32_t MAILBOX_CONTRACT = 2;

/// Oldest script contract still accepted. Bump ONLY when a change actually
/// invalidates older scripts — an additive change must not move this.
constexpr int32_t MAILBOX_CONTRACT_MIN = 1;

/// 'UE5C' — proves the symbol resolved to OUR data rather than to a stale
/// address left behind by a previous injection. That is not hypothetical: a
/// 2026-08-06 session showed CE holding a mailbox address the DLL no longer
/// owned, and the script wrote into it for ~155 s before giving up.
constexpr uint32_t MAILBOX_CONTRACT_MAGIC = 0x43354555u;

/// Published for CE Lua as its own exported symbol, NOT as a field inside
/// MailboxData — reading the layout-version out of the struct whose layout is in
/// question is circular, and this must be readable BEFORE anything is written.
#pragma pack(push, 1)
struct MailboxContract {
    uint32_t magic;     // 0x00: MAILBOX_CONTRACT_MAGIC
    int32_t  current;   // 0x04: MAILBOX_CONTRACT
    int32_t  minimum;   // 0x08: MAILBOX_CONTRACT_MIN
};
#pragma pack(pop)

static_assert(sizeof(MailboxContract) == 12, "CE Lua reads these at fixed offsets");
static_assert(MAILBOX_CONTRACT_MIN <= MAILBOX_CONTRACT,
              "the accepted range cannot be empty");

} // namespace Mimic

extern "C" __declspec(dllexport) extern Mimic::MailboxContract g_mailboxContract;
