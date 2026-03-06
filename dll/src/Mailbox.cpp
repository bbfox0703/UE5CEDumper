// ============================================================
// Mailbox.cpp — Shared memory mailbox for CE Lua
//
// Polling thread checks the mailbox every ~10ms.
// Uses existing public APIs (UE5_Init, UE5_CallProcessEvent, etc.)
// so no internal changes to GameThreadDispatch are needed.
//
// Thread safety model:
//   CE Lua writes all fields, then writes cmd (trigger) LAST.
//   Polling thread reads cmd, processes, writes status=1 then cmd=0.
//   WriteProcessMemory (CE side) is kernel-serializing.
// ============================================================

#define LOG_CAT "PIPE"
#include "Logger.h"
#include "Mailbox.h"
#include "ExportAPI.h"
#include "ObjectArray.h"
#include "UStructWalker.h"

#include <Windows.h>

#include <atomic>
#include <cstring>
#include <thread>
#include <algorithm>

// Forward declarations for ExportAPI symbols (must be outside namespace)
extern "C" bool     UE5_Init();
extern "C" int32_t  UE5_CallProcessEvent(uintptr_t, uintptr_t, uintptr_t);
extern uintptr_t    g_cachedGObjects;
extern uintptr_t    g_cachedGNames;

// The exported mailbox — zero-initialized by default
extern "C" __declspec(dllexport) Mailbox::MailboxData g_invokeMailbox = {};

namespace Mailbox {

// Polling thread state
static std::atomic<bool> s_running{false};
static HANDLE s_hThread = nullptr;

// Forward declarations
static void HandleFindInstance();
static void HandleFindFunction();
static void HandleInvoke();
static void HandleInvokeByName();
static void HandleListFunctions();
static void SetError(int32_t code, const char* msg);
static void SetDone(int32_t resultCode);
static bool EnsureInitialized();

// ---- Polling thread ----

static DWORD WINAPI PollingThreadProc(LPVOID /*param*/) {
    LOG_INFO("Mailbox: polling thread started");

    while (s_running.load(std::memory_order_acquire)) {
        int32_t cmd = g_invokeMailbox.cmd;

        if (cmd != CMD_IDLE) {
            // Mark as processing
            g_invokeMailbox.status = STATUS_PROCESSING;

            LOG_INFO("Mailbox: received cmd=%d", cmd);

            // Auto-init if needed (proxy DLL mode: UE5_Init not called yet)
            if (!EnsureInitialized() && cmd != CMD_IDLE) {
                // Init failed — most commands won't work
                if (cmd == CMD_INVOKE || cmd == CMD_INVOKE_BY_NAME) {
                    SetError(-10, "DLL not initialized (GObjects/GNames not found)");
                    continue;
                }
                // FIND_INSTANCE/FIND_FUNCTION also need init
                SetError(-10, "DLL not initialized");
                continue;
            }

            switch (cmd) {
            case CMD_FIND_INSTANCE:
                HandleFindInstance();
                break;
            case CMD_FIND_FUNCTION:
                HandleFindFunction();
                break;
            case CMD_INVOKE:
                HandleInvoke();
                break;
            case CMD_INVOKE_BY_NAME:
                HandleInvokeByName();
                break;
            case CMD_LIST_FUNCTIONS:
                HandleListFunctions();
                break;
            default:
                SetError(-1, "Unknown command");
                break;
            }
        }

        Sleep(10);
    }

    LOG_INFO("Mailbox: polling thread stopped");
    return 0;
}

// ---- Public API ----

void StartThread() {
    if (s_running.load()) return;

    s_running.store(true, std::memory_order_release);
    s_hThread = CreateThread(nullptr, 0, PollingThreadProc, nullptr, 0, nullptr);
    if (!s_hThread) {
        s_running.store(false);
        LOG_ERROR("Mailbox: failed to create polling thread (err=%lu)", GetLastError());
    }
}

void StopThread() {
    if (!s_running.load()) return;

    s_running.store(false, std::memory_order_release);

    if (s_hThread) {
        WaitForSingleObject(s_hThread, 3000);
        CloseHandle(s_hThread);
        s_hThread = nullptr;
    }

    // Clear mailbox
    memset(&g_invokeMailbox, 0, sizeof(g_invokeMailbox));
}

uintptr_t GetAddress() {
    return reinterpret_cast<uintptr_t>(&g_invokeMailbox);
}

// ---- Auto-initialization ----

static bool EnsureInitialized() {
    // UE5_Init is idempotent (checks internal s_initialized flag)
    // Note: extern declarations are at file scope (above namespace)
    if (g_cachedGObjects && g_cachedGNames) {
        return true;  // Already initialized
    }

    LOG_INFO("Mailbox: auto-initializing (UE5_Init)...");
    UE5_Init();

    return (g_cachedGObjects != 0 && g_cachedGNames != 0);
}

// ---- Command handlers ----

static void HandleFindInstance() {
    // Read class name from mailbox
    char className[256];
    memcpy(className, g_invokeMailbox.className, sizeof(className));
    className[255] = '\0';

    if (className[0] == '\0') {
        SetError(-1, "Empty class name");
        return;
    }

    LOG_INFO("Mailbox: FIND_INSTANCE class='%s'", className);

    // Reuse existing logic from UE5_FindInstanceOfClass
    auto rset = ObjectArray::FindInstancesByClass(className, false, 100);

    // Prefer non-CDO instance
    uintptr_t found = 0;
    for (const auto& r : rset.results) {
        if (r.addr && r.name.find("Default__") == std::string::npos) {
            found = r.addr;
            break;
        }
    }

    // Fallback: first result even if CDO
    if (!found && !rset.results.empty() && rset.results[0].addr) {
        found = rset.results[0].addr;
        LOG_WARN("Mailbox: FIND_INSTANCE only CDO found for '%s'", className);
    }

    if (found) {
        g_invokeMailbox.instanceAddr = found;
        LOG_INFO("Mailbox: FIND_INSTANCE '%s' -> 0x%llX",
                 className, (unsigned long long)found);
        SetDone(0);
    } else {
        g_invokeMailbox.instanceAddr = 0;
        char msg[256];
        snprintf(msg, sizeof(msg), "No instance of '%s' found (scanned=%d)",
                 className, rset.scanned);
        SetError(-2, msg);
    }
}

static void HandleFindFunction() {
    uintptr_t instanceAddr = g_invokeMailbox.instanceAddr;

    char funcName[256];
    memcpy(funcName, g_invokeMailbox.funcName, sizeof(funcName));
    funcName[255] = '\0';

    if (!instanceAddr) {
        SetError(-1, "Instance address is null");
        return;
    }
    if (funcName[0] == '\0') {
        SetError(-1, "Empty function name");
        return;
    }

    LOG_INFO("Mailbox: FIND_FUNCTION inst=0x%llX func='%s'",
             (unsigned long long)instanceAddr, funcName);

    // Get UClass from instance
    uintptr_t classAddr = UStructWalker::GetClass(instanceAddr);
    if (!classAddr) {
        SetError(-2, "Cannot read UClass from instance");
        return;
    }

    // Walk functions
    auto funcs = UStructWalker::WalkFunctions(classAddr);

    // Exact match
    uintptr_t ufuncAddr = 0;
    uint16_t parmsSize = 0;
    uint16_t numParms = 0;
    uint32_t funcFlags = 0;

    for (const auto& f : funcs) {
        if (f.name == funcName) {
            ufuncAddr = f.address;
            parmsSize = f.parmsSize;
            numParms = f.numParms;
            funcFlags = f.functionFlags;
            break;
        }
    }

    // Case-insensitive fallback
    if (!ufuncAddr) {
        std::string lower(funcName);
        std::transform(lower.begin(), lower.end(), lower.begin(),
                       [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        for (const auto& f : funcs) {
            std::string fl = f.name;
            std::transform(fl.begin(), fl.end(), fl.begin(),
                           [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
            if (fl == lower) {
                ufuncAddr = f.address;
                parmsSize = f.parmsSize;
                numParms = f.numParms;
                funcFlags = f.functionFlags;
                break;
            }
        }
    }

    if (ufuncAddr) {
        g_invokeMailbox.ufuncAddr = ufuncAddr;
        g_invokeMailbox.parmsSize = parmsSize;
        g_invokeMailbox.numParms = numParms;
        g_invokeMailbox.functionFlags = funcFlags;
        LOG_INFO("Mailbox: FIND_FUNCTION '%s' -> 0x%llX (parmsSize=%u numParms=%u flags=0x%X)",
                 funcName, (unsigned long long)ufuncAddr,
                 parmsSize, numParms, funcFlags);
        SetDone(0);
    } else {
        g_invokeMailbox.ufuncAddr = 0;
        char msg[256];
        snprintf(msg, sizeof(msg), "Function '%s' not found (%d functions walked)",
                 funcName, (int)funcs.size());
        SetError(-3, msg);
    }
}

static void HandleInvoke() {
    uintptr_t instanceAddr = g_invokeMailbox.instanceAddr;
    uintptr_t ufuncAddr = g_invokeMailbox.ufuncAddr;

    if (!instanceAddr || !ufuncAddr) {
        SetError(-1, "Instance or UFunction address is null");
        return;
    }

    LOG_INFO("Mailbox: INVOKE inst=0x%llX func=0x%llX",
             (unsigned long long)instanceAddr, (unsigned long long)ufuncAddr);

    // Call ProcessEvent using the existing public API.
    // UE5_CallProcessEvent handles:
    //   - Lazy ProcessEvent vtable detection
    //   - Lazy MinHook installation (GameThreadDispatch)
    //   - EnqueueInvoke (blocks until game thread executes)
    //   - Fallback direct call if hook not active
    // Note: extern declaration is at file scope (above namespace)
    int32_t result = UE5_CallProcessEvent(
        instanceAddr, ufuncAddr,
        reinterpret_cast<uintptr_t>(g_invokeMailbox.paramsData));

    if (result != 0) {
        char msg[256];
        snprintf(msg, sizeof(msg), "ProcessEvent returned %d", result);
        // Still mark as done (not error) — the result code tells the story
        strncpy(g_invokeMailbox.errorMsg, msg, sizeof(g_invokeMailbox.errorMsg) - 1);
        g_invokeMailbox.errorMsg[sizeof(g_invokeMailbox.errorMsg) - 1] = '\0';
    }

    LOG_INFO("Mailbox: INVOKE result=%d", result);
    SetDone(result);
}

static void HandleInvokeByName() {
    LOG_INFO("Mailbox: INVOKE_BY_NAME starting...");

    // Step 1: Find instance
    HandleFindInstance();
    if (g_invokeMailbox.result != 0) return;  // Error already set

    // Reset for next step
    g_invokeMailbox.cmd = CMD_FIND_FUNCTION;  // Prevent re-trigger
    g_invokeMailbox.status = STATUS_PROCESSING;

    // Step 2: Find function
    HandleFindFunction();
    if (g_invokeMailbox.result != 0) return;  // Error already set

    // Reset for invoke
    g_invokeMailbox.cmd = CMD_INVOKE;
    g_invokeMailbox.status = STATUS_PROCESSING;

    // Step 3: Invoke
    HandleInvoke();

    LOG_INFO("Mailbox: INVOKE_BY_NAME complete, result=%d", g_invokeMailbox.result);
}

static void HandleListFunctions() {
    // Resolve instance: use instanceAddr if set, otherwise find by className
    uintptr_t instanceAddr = g_invokeMailbox.instanceAddr;

    if (!instanceAddr) {
        char className[256];
        memcpy(className, g_invokeMailbox.className, sizeof(className));
        className[255] = '\0';

        if (className[0] == '\0') {
            SetError(-1, "No instance address or class name provided");
            return;
        }

        LOG_INFO("Mailbox: LIST_FUNCTIONS finding instance of '%s'...", className);

        auto rset = ObjectArray::FindInstancesByClass(className, false, 100);
        for (const auto& r : rset.results) {
            if (r.addr && r.name.find("Default__") == std::string::npos) {
                instanceAddr = r.addr;
                break;
            }
        }
        if (!instanceAddr && !rset.results.empty() && rset.results[0].addr) {
            instanceAddr = rset.results[0].addr;
        }
        if (!instanceAddr) {
            char msg[256];
            snprintf(msg, sizeof(msg), "No instance of '%s' found", className);
            SetError(-2, msg);
            return;
        }
        g_invokeMailbox.instanceAddr = instanceAddr;
    }

    // Get page index from params_data[0..3]
    uint32_t pageIndex = 0;
    memcpy(&pageIndex, g_invokeMailbox.paramsData, sizeof(uint32_t));

    // Get UClass
    uintptr_t classAddr = UStructWalker::GetClass(instanceAddr);
    if (!classAddr) {
        SetError(-2, "Cannot read UClass from instance");
        return;
    }

    // Walk all functions
    auto funcs = UStructWalker::WalkFunctions(classAddr);

    LOG_INFO("Mailbox: LIST_FUNCTIONS inst=0x%llX class=0x%llX total=%d page=%u",
             (unsigned long long)instanceAddr, (unsigned long long)classAddr,
             (int)funcs.size(), pageIndex);

    // Pagination: 15 entries per page (64 bytes each, 15*64=960 < 1024)
    // First 4 bytes of paramsData used for page header
    constexpr uint32_t ENTRY_SIZE = 64;
    constexpr uint32_t NAME_SIZE = 48;
    constexpr uint32_t ENTRIES_PER_PAGE = 15;

    uint32_t totalCount = static_cast<uint32_t>(funcs.size());
    uint32_t totalPages = (totalCount + ENTRIES_PER_PAGE - 1) / ENTRIES_PER_PAGE;
    if (totalPages == 0) totalPages = 1;

    uint32_t startIdx = pageIndex * ENTRIES_PER_PAGE;
    uint32_t endIdx = (std::min)(startIdx + ENTRIES_PER_PAGE, totalCount);
    uint32_t returnedCount = (startIdx < totalCount) ? (endIdx - startIdx) : 0;

    // Write metadata to header fields (repurposed)
    g_invokeMailbox.parmsSize = static_cast<uint16_t>(totalCount);
    g_invokeMailbox.numParms = static_cast<uint16_t>(returnedCount);
    g_invokeMailbox.functionFlags = totalPages;

    // Zero out params_data
    memset(g_invokeMailbox.paramsData, 0, sizeof(g_invokeMailbox.paramsData));

    // Write entries: each 64 bytes
    for (uint32_t i = 0; i < returnedCount; ++i) {
        const auto& f = funcs[startIdx + i];
        uint8_t* entry = g_invokeMailbox.paramsData + (i * ENTRY_SIZE);

        // [0..7] addr
        uint64_t addr64 = f.address;
        memcpy(entry + 0, &addr64, 8);

        // [8..9] parmsSize
        uint16_t ps = f.parmsSize;
        memcpy(entry + 8, &ps, 2);

        // [10..11] numParms
        uint16_t np = f.numParms;
        memcpy(entry + 10, &np, 2);

        // [12..15] flags
        uint32_t fl = f.functionFlags;
        memcpy(entry + 12, &fl, 4);

        // [16..63] name (null-terminated, max 47 chars + null)
        size_t nameLen = (std::min)(f.name.size(), static_cast<size_t>(NAME_SIZE - 1));
        memcpy(entry + 16, f.name.c_str(), nameLen);
        entry[16 + nameLen] = '\0';
    }

    LOG_INFO("Mailbox: LIST_FUNCTIONS returned %u/%u functions (page %u/%u)",
             returnedCount, totalCount, pageIndex + 1, totalPages);
    SetDone(0);
}

// ---- Helpers ----

static void SetError(int32_t code, const char* msg) {
    g_invokeMailbox.result = code;
    if (msg) {
        strncpy(g_invokeMailbox.errorMsg, msg, sizeof(g_invokeMailbox.errorMsg) - 1);
        g_invokeMailbox.errorMsg[sizeof(g_invokeMailbox.errorMsg) - 1] = '\0';
    }
    LOG_WARN("Mailbox: error=%d msg='%s'", code, msg ? msg : "");

    // Signal completion — MUST write status BEFORE clearing cmd
    g_invokeMailbox.status = STATUS_DONE;
    g_invokeMailbox.cmd = CMD_IDLE;
}

static void SetDone(int32_t resultCode) {
    g_invokeMailbox.result = resultCode;

    // Signal completion — MUST write status BEFORE clearing cmd
    g_invokeMailbox.status = STATUS_DONE;
    g_invokeMailbox.cmd = CMD_IDLE;
}

} // namespace Mailbox
