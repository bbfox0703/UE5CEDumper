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
    CMD_INVOKE          = 1,  // Call ProcessEvent (instance_addr, ufunc_addr, params_data). Output (contract 7+):
                              //   cmdOutFlags INV_OUT_NONSTATIC_ON_TEMPLATE when the function is not static and
                              //   the object is a template -- a warning beside `result`, never a refusal.
    CMD_FIND_INSTANCE   = 2,  // Find an object by class name (contract 6+): the first live object of that class
                              //   or a subclass (one a Debug Camera controller is or owns only when no other is
                              //   live), else the class's own default object; a name no class has is matched as
                              //   part of a class name. INVOKE_BY_NAME and LIST_FUNCTIONS' class-name branch
                              //   resolve through it. [FINDINSTANCE-SUBSTRING-USERFACING] Never a destroyed
                              //   object, at any of those steps (contract 9, [INVOKE-PICK-GARBAGE]).
    CMD_FIND_FUNCTION   = 3,  // Find UFunction by name on instance's class
    CMD_INVOKE_BY_NAME  = 4,  // Combined: find instance + find function + invoke
    CMD_LIST_FUNCTIONS  = 5,  // List all UFunctions on an instance's class (paginated)
    CMD_LIST_INSTANCES  = 6,  // List all live instances of a class (paginated) -- never a template
                              //   (Aura::TemplateOf: a class default object, its default subobjects, a
                              //   Blueprint component template; contract 8, [LISTINSTANCES-KEEPS-TEMPLATES]).
                              //   Scope is chosen by cmdFlags & LI_IN_DERIVED
                              //   (contract 3+): clear = the exact class only,
                              //   set = that class AND every subclass. See the
                              //   paramsData notes on MailboxData for the two
                              //   page formats — they differ, because a derived
                              //   sweep spans many concrete classes and the
                              //   identity witness has to travel per entry.
    CMD_SET_DEBUG_CAMERA = 7, // Robust Debug Camera force on/off / query.
                              //   Input:  instanceAddr = 0 (OFF) / 1 (ON) / 2 (query, no change)
                              //   Output: result = resulting state (1=ON, 0=OFF, -1=error,
                              //           -5 = the toggle is QUEUED: an ON will still run -- do not re-send;
                              //           an OFF did not complete -- send it again once the game responds.
                              //           -6 = not sent: an earlier toggle of the other direction is still
                              //           queued -- send again once the game responds.
                              //           [W3-DEBUGCAM-QUEUED]; MB3 in Mimic.cpp: no contract bump)
                              //           -17 = an ON not sent: no CheatManager is live, and the toggle does
                              //           nothing on its class default object (DBGCAM_ERR_NO_LIVE_CHEATMANAGER;
                              //           an OFF is never refused). [DEBUGCAM-FORCEON-CDO]
                              //           cmdOutFlags: builds 3676-3681 set INV_OUT_NONSTATIC_ON_TEMPLATE for a toggle
                              //           sent to that class default object [INVOKE-NONSTATIC-ON-CDO]; since -17 no
                              //           toggle goes there, so this command leaves the word 0.
    CMD_TELEPORT        = 8,  // Teleport (Wirbel): marker save/recall + cursor teleport.
                              //   Input:  instanceAddr = op (TeleportOp below)
                              //           ufuncAddr    = slot (0..2) for SAVE/RECALL/GET/CLEAR
                              //           paramsData (op CURSOR only, read BEFORE outputs):
                              //             [0..7] double zOffset, [8] u8 traceChannel,
                              //             [9] u8 fallbackToCenter
                              //   Output: result = Wirbel code (0 OK, negatives per
                              //           docs/teleport-spec.md §8)
                              //           paramsData pose block (GET_POSE/SAVE/GET_MARKER/
                              //           GET_LAST/BUGIT_SAVE/RELATIVE):
                              //             [0..47]   6 doubles X,Y,Z,Pitch,Yaw,Roll
                              //             [48..175] mapName (null-terminated)
                              //             [176]     u8 source (0 raw / 1 invoke)
                              //             [177]     u8 tier (1 invoke / 2 raw write)
                              //             [178]     u8 pose flags (contract 4+): bit0 = the
                              //                       pose came from the raw parent-relative
                              //                       fallback (NOT world coords); bit1 =
                              //                       RELATIVE's landing is unknown (the 6
                              //                       doubles are then NaN, never zeros)
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
                              //             3 GravityDirection, UE5.3+)
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
    CMD_OFFSETS_VERDICT = 16, // [W5-OFFSETS-MAILBOX] Were the DynOff offsets MEASURED? CE Lua builds
                              //   structures from them (ue5_dissect.lua), so a script that cannot ask
                              //   is building on fallbacks without knowing. Read-only, and
                              //   init-EXEMPT on purpose: when the probe never ran, "probe-not-run"
                              //   IS the answer the caller needs -- a -10 refusal would hide it.
                              //   Input:  none
                              //   Output: result = 1 (measured) / 0 (not measured)
                              //           paramsData[0..127] = reason, null-terminated ("" when
                              //             measured, else e.g. "probe-not-run")
};

// CMD_TELEPORT op codes (written into instanceAddr by CE Lua / pipe bridge)
enum TeleportOp : uint64_t {
    TP_OP_GET_POSE     = 0,
    TP_OP_SAVE         = 1,
    TP_OP_RECALL       = 2,  // result -16 for a marker saved from a parent-relative read, nothing moved;
    TP_OP_RECALL_FORCE = 3,  //   RECALL_FORCE skips only the map check (-7), so it answers -16 too. A new
                             //   result on existing ops, as BUGIT_GO's below. [TELEPORT-RECALL-PARENTREL]
    TP_OP_CURSOR       = 4,
    TP_OP_GET_MARKER   = 5,
    TP_OP_CLEAR_MARKER = 6,
    TP_OP_RECALL_LAST  = 7,  // recall the system "last" pose (auto-saved before a
                             //   jump; a pose that is not world coordinates leaves the
                             //   previous one). slot ignored.
    TP_OP_GET_LAST     = 8,  // read the system "last" slot (pose block output)
    TP_OP_BUGIT_SAVE   = 9,  // store current pose into the BugIt slot (pose block
                             //   output, like SAVE); user-triggered, slot ignored.
    TP_OP_BUGIT_GO     = 10, // teleport to the stored BugIt pose (no-op when empty: result -6). result -16
                             //   when the stored BugIt came from a parent-relative read: nothing moved and the
                             //   "last" slot is untouched [TELEPORT-BUGITGO-PARENTREL]. A new result on an existing
                             //   op, not a contract change: every contract-checking teleport record already shows
                             //   "failed (code N)" for a negative result it has no message for, and keeps its window
                             //   open, and an older DLL answers it exactly as before. The BugIt record's "BugItGo will
                             //   refuse this pose" holds because it bakes contract 6 or later, which no DLL without the
                             //   refusal publishes.
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
                             //     [177] tier. result -16 when the current pose came from
                             //     a parent-relative read: nothing moved. [TELEPORT-RECALL-PARENTREL]
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

// CMD_LIST_INSTANCES flag bits (contract 3+). Two fields, one bit each so far —
// the IN bit is written by CE Lua before the trigger, the OUT bit by the DLL.
//
// LI_IN_DERIVED is what makes a class-wide freeze cover the pool the user is
// actually looking at. A Property Search row for an INHERITED field is keyed to
// the class that DECLARES it (`bCanBeDamaged` presents as `Actor`), so an
// exact-name pool held one incidental debug actor while every AActor subclass in
// the level — the player's pawn included — went untouched. Same failure Solide
// already fixed for Force (audit #5 A6); the two sit on the same panel row and
// must not scope oppositely. (`[FREEZESCOPE-2026-08-18]`)
//
// The bit DEFAULTS OFF, and that is the whole backward-compatibility story: a
// contract-1/2 script never writes cmdFlags, reads 0, and gets contract-2's exact
// match byte for byte. The handler clears cmdFlags after reading it, so a derived
// request cannot leak into the next caller's command.
enum ListInstancesFlag : uint32_t {
    LI_IN_DERIVED    = 0x1,  // cmdFlags    (IN) : include every SUBCLASS of className
    LI_OUT_TRUNCATED = 0x1,  // cmdOutFlags (OUT): the pool hit the result cap, so the
                             //   returned set is a PREFIX and more instances exist
                             //   unheld. A derived sweep off a broad base reaches this
                             //   routinely, so it is normal here, not exceptional.
};

// cmdOutFlags bits CMD_INVOKE (and CMD_INVOKE_BY_NAME, which ends in it) publishes (contract 7+). CMD_SET_DEBUG_CAMERA
// published the bit too in builds 3676-3681, for a toggle it sent to the CheatManager's class default object; it now
// refuses that toggle (-17, [DEBUGCAM-FORCEON-CDO]), so it never sets the bit -- what the bit means is unchanged.
// [INVOKE-NONSTATIC-ON-CDO] Never 0x1: a contract 3-6 DLL can leave LI_OUT_TRUNCATED behind after a LIST_INSTANCES,
// and it never writes 0x2. The dispatcher clears cmdOutFlags before every command, so the bit is this command's.
enum InvokeOutFlag : uint32_t {
    INV_OUT_NONSTATIC_ON_TEMPLATE = 0x2,  // the call reached dispatch with a function that is not static on a TEMPLATE
                                          //   (a class-default object, one of its default subobjects, or another
                                          //   archetype such as a Blueprint's component template): it may have done
                                          //   nothing there. A warning: `result` is the call's own answer.
};
// The first contract whose invoke family publishes InvokeOutFlag; a script claiming less reads 0x72C only from a DLL
// that publishes at least this (0x72C lies past the end of a contract 1-2 MailboxData).
constexpr int32_t MAILBOX_CONTRACT_INVOKE_OUT = 7;
static_assert((static_cast<uint32_t>(INV_OUT_NONSTATIC_ON_TEMPLATE) & static_cast<uint32_t>(LI_OUT_TRUNCATED)) == 0,
              "the invoke warning must never share the bit an older DLL leaves behind after LIST_INSTANCES");

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
                                        //           className field = class name (case-insensitive)
                                        //           cmdFlags       = LI_IN_DERIVED to include every
                                        //                            SUBCLASS (contract 3+; absent/0
                                        //                            keeps the exact match)
                                        //   Output: parmsSize = total live instance count (capped at 0xFFFF)
                                        //           numParms  = returned count this page
                                        //           functionFlags = total pages
                                        //           cmdOutFlags   = LI_OUT_TRUNCATED when the result cap
                                        //                           was reached (contract 3+)
                                        //           instanceAddr  = UClass* of the enumerated class
                                        //                           (0 = not resolved OR derived scope,
                                        //                           where there is no single class;
                                        //                           contract 2+)
                                        //           ufuncAddr     = byte offset of UObject::ClassPrivate,
                                        //                           i.e. Grimoire::OFF_UOBJECT_CLASS
                                        //                           (contract 2+)
                                        //   Entry layout depends on the scope — ListInstancesEntrySize():
                                        //     exact   8 bytes (max 128 per page):
                                        //       [0..7]   UObject* (uint64)
                                        //     derived 16 bytes (max 64 per page):
                                        //       [0..7]   UObject* (uint64)
                                        //       [8..15]  UClass*  (uint64) — the CONCRETE class of THAT
                                        //                object, i.e. its per-entry identity witness
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
                                        //
                                        //   ...and that class-wide shape is exactly why the DERIVED
                                        //   scope (contract 3) could not reuse it: the sweep returns
                                        //   instances of many concrete classes, so one page witness
                                        //   would refuse every write except the one class it named.
                                        //   Dropping the witness instead was not an option — that IS
                                        //   the AA2 defect — so the derived format carries the UClass*
                                        //   per entry and halves the page to 64.

    volatile uint32_t cmdFlags;         // 0x728: IN  — per-command input flags (contract 3+).
                                        //        Today only CMD_LIST_INSTANCES reads it
                                        //        (ListInstancesFlag). Its handler CLEARS it after
                                        //        reading, so a flag set by one caller can never widen
                                        //        the next caller's command — which is what keeps a
                                        //        contract-1/2 script (which never writes this field)
                                        //        on exactly its old behaviour.
    volatile uint32_t cmdOutFlags;      // 0x72C: OUT — per-command output flags (contract 3+).
                                        //        Each command publishes its own out-flag enum here
                                        //        (ListInstancesFlag, InvokeOutFlag -- see who sets each
                                        //        in its comment). The dispatcher clears it before every
                                        //        command (contract 7+), so a bit is always this
                                        //        command's, never accumulated.
};
#pragma pack(pop)

static_assert(sizeof(MailboxData) <= 4096, "MailboxData must fit in a single page");

// ---- CMD_LIST_INSTANCES page geometry + result caps (contract 3+) -----------
//
// The wire format depends on ONE input bit, so the DLL and the CE Lua helper must
// derive it from the same rule or they disagree about where entry N begins. Kept
// header-inline and pure precisely so `dll_helpers_test` can pin it: no test target
// compiles Mimic.cpp, but every test compiles this header.

constexpr uint32_t LIST_INSTANCES_ENTRY_EXACT   = 8;   // UObject*
constexpr uint32_t LIST_INSTANCES_ENTRY_DERIVED = 16;  // UObject* + its UClass*

constexpr uint32_t ListInstancesEntrySize(bool derived) {
    return derived ? LIST_INSTANCES_ENTRY_DERIVED : LIST_INSTANCES_ENTRY_EXACT;
}

constexpr uint32_t ListInstancesPerPage(bool derived) {
    return static_cast<uint32_t>(sizeof(MailboxData::paramsData))
         / ListInstancesEntrySize(derived);
}

// Result caps. TWO of them, because the pools are not the same size and the cost
// that matters is cap ÷ page size: the CE Lua caller re-runs a full GObjects walk
// for EVERY page, on EVERY rescan (5 s by default). Both caps land on the same
// 16-page ceiling the helper enforces, so widening the scope did not double the
// per-rescan walk count.
//
// The derived cap is also a fact about the consumer, not just about the walk: the
// freeze tick issues one ReadProcessMemory + one WriteProcessMemory per instance
// at 20 Hz from CE's Lua, so a four-figure pool is already past what that loop can
// service. Truncation is reported (LI_OUT_TRUNCATED) rather than hidden.
constexpr int      LIST_INSTANCES_MAX_EXACT   = 2000;
constexpr int      LIST_INSTANCES_MAX_DERIVED = 1024;
constexpr uint32_t LIST_INSTANCES_MAX_PAGES   = 16;

static_assert(LIST_INSTANCES_MAX_EXACT
                  <= static_cast<int>(LIST_INSTANCES_MAX_PAGES * ListInstancesPerPage(false)),
              "the exact cap must fit inside the page budget the helper walks");
static_assert(LIST_INSTANCES_MAX_DERIVED
                  <= static_cast<int>(LIST_INSTANCES_MAX_PAGES * ListInstancesPerPage(true)),
              "the derived cap must fit inside the page budget the helper walks");

// ---- Pure decision rules (header-inline so a test target can pin them) -------
//
// Neither of these is contract SURFACE — tools/check_mailbox_contract.py hashes
// `MailboxData`'s fields and the CONTRACT_ENUMS values, nothing else — but both
// decide how a command behaves, and `Mimic.cpp` is compiled by no test target.
// Keeping them here is what makes them assertable at all.

// UE FunctionFlags subset the invoke route depends on. Pulled from
// Engine/Source/Runtime/CoreUObject/Public/UObject/Script.h. Only these two bits
// are ever read; declaring them locally avoids dragging the full enum in.
constexpr uint32_t FUNC_FLAG_NATIVE = 0x00000400u;
constexpr uint32_t FUNC_FLAG_STATIC = 0x00002000u;

/// Should CMD_INVOKE bypass GameThreadDispatch and call ProcessEvent directly?
///
/// TRUE only for a UFunction tagged Native+Static — a C++ helper with no implicit
/// `this` and no dependency on game state (KismetMathLibrary and friends). Those
/// are safe off-thread and, crucially, do not need the game thread to fire
/// ProcessEvent ever again, so an idle game (menu / loading screen) can still
/// service them instead of timing out at the invoke deadline.
///
/// `flagsResolved` is not decoration. The two directions are NOT symmetric:
///   * a false NEGATIVE routes a pure helper through the queue — slower, and on an
///     idle game it can time out, but nothing is corrupted;
///   * a false POSITIVE runs a STATEFUL actor UFunction off the game thread, which
///     is the hazard GameThreadDispatch exists to prevent.
/// So an unresolved read must answer false. Callers must resolve the flags from
/// the UFunction named by `ufuncAddr`, never from `MailboxData::functionFlags` —
/// that field is a DLL-filled OUTPUT (`CMD_INVOKE`'s documented inputs are
/// instanceAddr / ufuncAddr / paramsData), so a bare re-FIRE of CMD_INVOKE reads
/// whatever the PREVIOUS command left at offset 0x024. Two commands overwrite it
/// with a page count, and CMD_FIND_FUNCTION leaves the flags of a DIFFERENT
/// function — which is the false-positive above. (audit #5 MB1)
constexpr bool ShouldRouteDirectInvoke(uint32_t functionFlags, bool flagsResolved) {
    return flagsResolved
        && (functionFlags & (FUNC_FLAG_NATIVE | FUNC_FLAG_STATIC))
               == (FUNC_FLAG_NATIVE | FUNC_FLAG_STATIC);
}

/// [INVOKE-NONSTATIC-ON-CDO] Warn -- never refuse -- when a user-facing invoke runs a function that is not static on a
/// TEMPLATE (a class-default object, one of its default subobjects, or another archetype such as a Blueprint's
/// component template): it may do nothing there, or fault (a CheatManager command that dereferences its owner, such as
/// God's GetOuterAPlayerController()->GetPawn(), meets null on Default__CheatManager from 4.20 on), and a game's own
/// command may still work.
/// `isUFunction` is ResolveFunctionInfo's answer, so the flags carry the same trust as ShouldRouteDirectInvoke's: an
/// object that is not a UFunction warns, and only a FUNC_Static read clears it.
constexpr bool WarnNonStaticOnTemplate(bool onTemplate, bool isUFunction, uint32_t functionFlags) {
    return onTemplate && !(isUFunction && (functionFlags & FUNC_FLAG_STATIC) != 0);
}

/// [DEBUGCAM-FORCEON-CDO] CMD_SET_DEBUG_CAMERA / set_debug_camera / UE5_SetDebugCamera: a Force ON not sent, because the
/// CheatManager the state read found is a template -- its class default object, so none is live. ToggleDebugCamera does
/// nothing there on 4.20+ (DECLARE_WITHIN's GetOuterAPlayerController() answers null on a CDO, and every toggle path
/// guards it) and is undefined behaviour on 4.18/4.19 (a raw cast of the /Script/Engine package). UE 5 compiles the
/// CheatManager out of Shipping by default (UE_WITH_CHEAT_MANAGER), so this is the usual UE 5 Shipping answer; a UE 4
/// standalone game creates one unless it turns cheats off (AGameModeBase::AllowCheats).
constexpr int32_t DBGCAM_ERR_NO_LIVE_CHEATMANAGER = -17;

/// What a Debug Camera Force does once it has read the state and found the camera in the other state. No live
/// CheatManager: the state read found only the class default object, or no CheatManager at all.
enum class DbgCamForceStep : uint8_t {
    Toggle,                    // a live CheatManager: send ToggleDebugCamera
    SwapBackOnly,              // OFF with no live CheatManager: the toggle would do nothing, so only switch back
    RefuseNoLiveCheatManager,  // ON with no live CheatManager: nothing can turn the camera on
};

/// An ON is refused rather than sent, so no toggle on a template is ever dispatched or left queued, and a later -5 / -6
/// always refers to a toggle on a live CheatManager. An OFF is never refused: a camera that is on while only the class
/// default object stands in (the player's CheatManager is gone) still turns off through the swap-back, which is where a
/// no-op toggle there ended up anyway.
constexpr DbgCamForceStep DebugCameraForceStep(bool want, bool noLiveCheatManager) {
    return !noLiveCheatManager ? DbgCamForceStep::Toggle
         : want ? DbgCamForceStep::RefuseNoLiveCheatManager : DbgCamForceStep::SwapBackOnly;
}

/// [UE-OVERRIDE-411] review 2: what CMD_INVOKE publishes instead of handing ProcessEvent the paramsData slab.
/// Negative and apart from ProcessEvent's own codes (Frieren.h lists them) and the mailbox's -10 / -11; like
/// MB_ERR_HANDLER_THREW in Mimic.cpp, not a contract change, because every script treats a non-zero result as a
/// failure and shows errorMsg.
constexpr int32_t MB_ERR_INVOKE_UNRESOLVED = -12;   // ufuncAddr is not a live UFunction
constexpr int32_t MB_ERR_INVOKE_TOO_LARGE  = -13;   // its parameter block ends past the slab
/// [VND584-01] A weak / soft / lazy parameter holds an object the engine could not turn into one (errorMsg names it).
/// What paramsData MEANS did move, additively: a picked object's address in such a slot is now swapped for the
/// engine's own value before ProcessEvent, where it used to reach the engine raw and resolve to null. No bump, on
/// purpose: no generated script changed -- each writes exactly the bytes it always wrote -- and an older DLL answers
/// them exactly as before the fix, so a script on an older DLL is no worse off than it already was, while a bump would
/// refuse every new script there, the many with no such slot included. Like -12 / -13, the code itself is additive.
constexpr int32_t MB_ERR_INVOKE_OBJECT_PARAM = -14;
/// [VND584-06] ufuncAddr is a live UFunction, but not one of the instance's class chain: a cached address a reloaded
/// Blueprint class left to another class's function. Additive like -12 / -13: a script on an older DLL never gets it,
/// and every script shows errorMsg for a non-zero result -- which says to re-open the form, whose creation re-runs FIND.
constexpr int32_t MB_ERR_INVOKE_NOT_OWNED = -15;
/// ⚠ -16 is taken: Wirbel::TP_ERR_PARENT_RELATIVE reaches CE in the same `result` field on CMD_TELEPORT
/// ([TELEPORT-BUGITGO-PARENTREL], [TELEPORT-RECALL-PARENTREL]). ⚠ -17 is taken too: DBGCAM_ERR_NO_LIVE_CHEATMANAGER on
/// CMD_SET_DEBUG_CAMERA ([DEBUGCAM-FORCEON-CDO]). The next mailbox-wide code takes -18.

/// May CMD_INVOKE hand ProcessEvent the slab? 0 = yes, otherwise the refusal to publish.
///
/// ProcessEvent writes each parameter where the function's own chain puts it, out parameters and the return
/// value included, so the slab must reach the chain's END (`bufferBytes`, Ubel::ParamBufferSize), never just the
/// ParmsSize the tail read gave: under a wrong UE version that read is another field (NumParms, ReturnValueOffset),
/// and a small one let an out parameter past 1 KB run through cmdFlags / cmdOutFlags and out of g_invokeMailbox.
/// A function that does not resolve cannot be sized, and the queued path's owned copy is only the slab's size, so
/// routing it anyway risks the same overrun.
constexpr int32_t InvokeSlabRefusal(bool resolved, uint32_t bufferBytes, uint32_t slabBytes) {
    if (!resolved) return MB_ERR_INVOKE_UNRESOLVED;
    return bufferBytes > slabBytes ? MB_ERR_INVOKE_TOO_LARGE : 0;
}

/// [A3-MIMIC-INIT-FASTPATH] May a mailbox command skip EnsureInitialized's call into UE5_Init? UE5_Init publishes
/// g_cachedGObjects / g_cachedGNames right after FindAll -- BEFORE Serie / Aura init, decoy recovery and
/// ValidateAndFixOffsets -- so "both are set" is NOT "initialized" while an init is still scanning. Pure, so
/// dll_helpers_test pins it.
constexpr bool InitFastPathOk(bool haveGObjects, bool haveGNames, bool initInProgress) {
    // While an init is scanning, the caller goes through UE5_Init, which waits on s_initMutex -- and says so -- and
    // returns the first caller's result (Frieren.cpp's promise). Never "always call UE5_Init": a pathological rescan can
    // pass the 10 s mailbox timeout.
    return haveGObjects && haveGNames && !initInProgress;
}

/// Does `cmd` need the AOB scan (GObjects/GNames) to have succeeded?
///
/// Everything that touches UE reflection does, so this is TRUE by default and the
/// exemption list is deliberately tiny — an over-broad exemption turns a clean
/// "-10 DLL not initialized" into a null deref inside a handler.
///
/// CMD_FOREGROUND is the only exemption the header's own command documentation
/// justifies: Grausam is a pure Win32 MinHook + WndProc subclass that never reads
/// a UObject, and the pipe path (`Fern`, `set_foreground_lock`) already services
/// it with no init gate at all. Gating it meant that on a game whose scan fails,
/// the CE-Lua Keep-Foreground toggle was refused with -10 while the UI's identical
/// button worked. (audit #5 MB2)
///
/// Counter-examples that look exempt and are NOT: CMD_QUERY_PTR is "read-only and
/// thread-agnostic" but reads the caches the scan fills and iterates GObjects;
/// CMD_TIME is a "pure reflected memory write" — reflected means GObjects.
///
/// [W5-OFFSETS-MAILBOX] CMD_OFFSETS_VERDICT is the SECOND exemption, and for a reason the first does not
/// share: its answer IS the init state. It reports whether the offsets were measured, and "no probe has
/// run" is one of the answers; gating it would return -10 ("DLL not initialized") in exactly the case
/// the caller asked about, and auto-init would run a whole-image sweep to answer a question about
/// whether that sweep has happened. It reads two atomics and a fixed string — no UObject, no cache.
constexpr bool CommandRequiresInit(int32_t cmd) {
    return cmd != CMD_FOREGROUND && cmd != CMD_OFFSETS_VERDICT;
}

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
//      TimeOp / ForegroundOp, and Laufen's knobIds), and the flag enums a command reads or
//      publishes (ListInstancesFlag, InvokeOutFlag)
//   4. Status / InitState values, or result-code meanings
//   5. The meaning of an EXISTING field at an existing offset
//
// WHAT IS NOT (additive ⇒ bump MAILBOX_CONTRACT only, leave the MIN alone):
//   a new Cmd, a new op at a previously unused number, a new knobId. Old scripts
//   never referenced them, so they stay valid.
//   ALSO a meaning change (item 5) whose old behaviour a script could only have
//   relied on by ACCIDENT (version 6 is the case). It is still a meaning change --
//   a script that did rely on it may now get a different answer, in some cases a
//   silent one -- but moving MIN would refuse every saved .CT, the ones the change
//   fixes included. Write down which accidents end, as version 6 does.
//
// WHAT IS NOT A CHANGE AT ALL: a new negative result code on a command or op whose
//   every generated block reports a negative result it has no message for and keeps
//   its window open -- today CMD_TELEPORT (the -16 of BUGIT_GO, RECALL, RECALL_FORCE and RELATIVE), the invoke
//   family (MB_ERR_INVOKE_* -12..-15) and CMD_SET_DEBUG_CAMERA's -17, which only an ON gets: every [ENABLE] reports
//   a state it has no branch for and keeps its window open (records since 2026-08-21 also untick; older ones stay
//   ticked, as they did for an answer of 0), and the [DISABLE] blocks that report a negative only through dbg() never
//   receive it, because an OFF is never refused. Check the generators first: some [DISABLE]
//   blocks report a negative state only through dbg(), and the GodMode [ENABLE]
//   reads any negative state as "armed", so a new code there IS a change.
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
/// 3 (`[FREEZESCOPE-2026-08-18]`): two new fields at the TAIL of MailboxData —
///   `cmdFlags` (IN) and `cmdOutFlags` (OUT) — plus CMD_LIST_INSTANCES' derived
///   scope, which they select and report truncation for. ADDITIVE in both halves,
///   so MAILBOX_CONTRACT_MIN stays at 1:
///     * every pre-existing field keeps its offset (the struct only grew at the
///       end), so an old script's reads and writes land exactly where they did;
///     * the new IN flag defaults to 0 = the old exact-match behaviour, and the
///       handler clears it after every use so it cannot be inherited;
///     * the 16-byte derived page format is reachable ONLY with that flag set, so
///       no old script can encounter it.
///   Note this bump DOES move the surface hash (the struct gained fields), unlike
///   version 2 which moved on meaning alone — see tools/check_mailbox_contract.py.
/// 4 ([W2-MARKER-PARENTREL] / [W2-TPREL-TRANSPORTS]): CMD_TELEPORT's pose block gains paramsData[178], pose
///   flags -- bit0 the pose came from the raw parent-relative fallback, bit1 TP_OP_RELATIVE's landing is unknown
///   (its 6 doubles are then NaN, never the zeros of a landing at the world origin). ADDITIVE: [178] was an
///   unused output for every pose-block op (only CURSOR writes it, as its own usedCenter), so no contract-1..3
///   script reads it, and MAILBOX_CONTRACT_MIN stays at 1. Like version 2 this moves on MEANING alone -- the
///   surface hash does not change, and tools/check_mailbox_contract.py records why.
/// 5 ([W5-OFFSETS-MAILBOX]): CMD_OFFSETS_VERDICT = 16 -- "were the DynOff offsets measured?", answered with
///   result 1/0 plus the reason in paramsData[0..127]. ADDITIVE in the plainest sense: a NEW Cmd at a
///   previously unused number, so no older script can send it or read its output, and
///   MAILBOX_CONTRACT_MIN stays at 1. Unlike versions 2 and 4 this DOES move the surface hash -- a new
///   enum member is layout -- so tools/check_mailbox_contract.py takes a new golden pair.
/// 6 ([FINDINSTANCE-SUBSTRING-USERFACING]): CMD_FIND_INSTANCE -- and CMD_INVOKE_BY_NAME and CMD_LIST_FUNCTIONS'
///   class-name branch, which resolve through it -- picks by DERIVATION: the first live object of the class named in
///   className or of a subclass (one a Debug Camera controller is or owns only when no other is live), else that
///   class's own default object. The class-name SUBSTRING match it used before now answers only a name no object's
///   class chain has, without the old 100-match cap. A MEANING change (item 5); no field moved and no result code
///   changed (a lookup cut short by shutdown answers -2 with its own errorMsg). MAILBOX_CONTRACT_MIN stays at 1, but
///   two accidents end, and a script that relied on one gets a different answer:
///     * it sent a name some class HAS and relied on reaching an UNRELATED class whose name merely contains it. It
///       now gets that class's object -- or its default object -- and either -3 "function not found" or, for a
///       function the two classes share, result 0 from an object that may do nothing;
///     * it relied on reaching the Debug Camera's own CheatManager (a ToggleDebugCamera OFF on 4.18-5.7). It now
///       reaches the player's, where that toggle is a no-op: result 0, the camera stays on, visible only in the game.
///       CMD_SET_DEBUG_CAMERA is the supported OFF.
///   Both relied on an accident, and moving MIN would refuse every saved .CT, the ones this fixes included. Like
///   versions 2 and 4 it moves on meaning alone -- the surface hash does not change. The bump makes a script generated
///   now refuse a DLL that would still guess. It does not reach baked scripts outside Verify Return Value mode: their
///   embedded invoke helper deliberately claims contract 1 (verify mode bakes the generator's own contract check).
/// 7 ([INVOKE-NONSTATIC-ON-CDO]): two changes, one bump. (a) CMD_INVOKE -- and CMD_INVOKE_BY_NAME, which ends in it --
///   publishes InvokeOutFlag in cmdOutFlags: INV_OUT_NONSTATIC_ON_TEMPLATE when it calls a function that is not static on
///   a template (a class-default object, one of its default subobjects, or another archetype), where it may have done
///   nothing -- and so does CMD_SET_DEBUG_CAMERA when its toggle targets the CheatManager's class default object (none
///   was live). A warning beside `result`, never a refusal. cmdOutFlags was an unused output for both, and the
///   dispatcher now clears it before every command, so a set bit is always this command's. The new enum is layout,
///   so the surface hash moves. (b) CMD_FIND_INSTANCE never takes a TEMPLATE for a live object: one flagged
///   RF_ArchetypeObject -- a
///   class-default object's default subobject, which every component-class lookup landed on (measured on DumperTest
///   5.4) -- is passed over like a class-default object. Both ADDITIVE in the sense version 6 set out: no field moved,
///   no result code changed, and the old pick a script could have relied on was an accident (a template taken for a
///   live object). MAILBOX_CONTRACT_MIN stays at 1. A script claiming less than MAILBOX_CONTRACT_INVOKE_OUT reads
///   0x72C only from a DLL that publishes at least that; the embedded invoke helper keeps claiming 1 and gates its read
///   on the DLL's own number.
///   2026-11-03 ([DEBUGCAM-FORCEON-CDO], still 7): CMD_SET_DEBUG_CAMERA refuses an ON whose CheatManager is a template
///   with -17 and sends an OFF there straight to the swap-back, so it no longer sets INV_OUT_NONSTATIC_ON_TEMPLATE. The
///   bit's meaning did not move (it still names a call that reached a template, which this command can no longer
///   make), and -17 is a new negative code only an ON gets (WHAT IS NOT A CHANGE AT ALL above). No bump, and the surface
///   hash does not change.
/// 8 ([LISTINSTANCES-KEEPS-TEMPLATES]): CMD_LIST_INSTANCES never lists a TEMPLATE -- a class default object's default
///   subobjects and a Blueprint's component templates join the class default objects it already skipped, in both
///   scopes, before the cap. A MEANING change (item 5); no field moved and no result code changed, so the surface hash
///   does not move. The total (parmsSize), the page count and LI_OUT_TRUNCATED now count live objects only.
///   MAILBOX_CONTRACT_MIN stays at 1, by version 6's rule: the command has always promised live instances, and since
///   version 7 (b) a template is not one, so a pool holding templates was a defect even though the old "(non-CDO)"
///   gloss named only the class default object. Two accidents end: a freeze that also reached a template (so later
///   spawns inherited the value) now holds live objects only; and on GAS 5.3+ a GameplayEffect class default's
///   component subobjects -- templates, usually the only objects of their classes, read by every application rather
///   than copied by spawns -- are no longer frozen at all. The fix is DLL-side, so an older script gets the clean
///   pool from this DLL with nothing to regenerate; the bump makes a script generated now refuse a contract-7 DLL,
///   which would still hand it templates to write.
/// 9 ([INVOKE-PICK-GARBAGE]): CMD_FIND_INSTANCE -- with CMD_INVOKE_BY_NAME and CMD_LIST_FUNCTIONS' class-name branch,
///   which resolve through it -- never picks a DESTROYED object (UE5 Garbage, UE 5.0-5.3 RF_PendingKill, UE4 item
///   PendingKill), at any step: the live pick, the Debug Camera's last resort, and a partial class-name match. An actor
///   destroyed in play stays in GObjects until the next collection (about a minute on a stock engine), and a low index
///   made it the first object met: an invoke reached an object that had left its level, and one the engine may free
///   under it. A MEANING change (item 5); no field moved and no result code changed, so the surface hash does not move.
///   MAILBOX_CONTRACT_MIN stays at 1, by version 6's rule: the command promises a live object, and a destroyed one was
///   an accident no script could rely on. The fix is DLL-side, so an older script gets the live object from this DLL
///   with nothing to regenerate; the bump makes a script generated now refuse a contract-8 DLL, which would still hand
///   it a destroyed one.
constexpr int32_t MAILBOX_CONTRACT = 9;

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
