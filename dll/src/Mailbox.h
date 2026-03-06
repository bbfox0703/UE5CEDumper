// ============================================================
// Mailbox.h — Shared memory mailbox for CE Lua UFunction invocation
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

namespace Mailbox {

// Mailbox commands (CE writes to cmd field)
enum Cmd : int32_t {
    CMD_IDLE            = 0,
    CMD_INVOKE          = 1,  // Call ProcessEvent (instance_addr, ufunc_addr, params_data)
    CMD_FIND_INSTANCE   = 2,  // Find non-CDO instance by class name
    CMD_FIND_FUNCTION   = 3,  // Find UFunction by name on instance's class
    CMD_INVOKE_BY_NAME  = 4,  // Combined: find instance + find function + invoke
    CMD_LIST_FUNCTIONS  = 5,  // List all UFunctions on an instance's class (paginated)
};

// Mailbox status (DLL writes to status field)
enum Status : int32_t {
    STATUS_IDLE         = 0,
    STATUS_DONE         = 1,
    STATUS_PROCESSING   = 0xFF,
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
    int32_t           reserved;         // 0x00C: Alignment padding

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

} // namespace Mailbox

// Exported global — CE Lua uses getAddress("g_invokeMailbox") to find it.
// No function call needed! CE resolves the symbol from the DLL export table.
extern "C" __declspec(dllexport) extern Mailbox::MailboxData g_invokeMailbox;
