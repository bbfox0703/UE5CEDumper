// ============================================================
// Mimic — 寶箱怪 (經典梗 — The Classic Gag)
// Mailbox: CE Lua shared-memory command interface
//
// Polling thread checks the mailbox every ~1ms (see kPollIntervalMs).
// Uses existing public APIs (UE5_Init, UE5_CallProcessEvent, etc.)
// so no internal changes to GameThreadDispatch are needed.
//
// Thread safety model:
//   CE Lua writes all fields, then writes cmd (trigger) LAST.
//   Polling thread reads cmd, processes, writes status=1 then cmd=0.
//   WriteProcessMemory (CE side) is kernel-serializing.
// ============================================================

#define LOG_CAT "PIPE"
#include "Sein.h"
#include "Mimic.h"
#include "Frieren.h"
#include "Aura.h"
#include "Ubel.h"
#include "Wirbel.h"
#include "Dunste.h"
#include "Grausam.h"
#include "Schlacht.h"
#include "Genau.h"
#include "Macht.h"
#include "Grimoire.h"
#include "Tot.h"      // Tot::MarkCancelImmune — the poller must survive a pipe client's death (B4)
#include "Routine.h"   // Routine::RunThreadGuarded — a throw out of a thread proc is std::terminate (B14)

#include <Windows.h>
#include <timeapi.h>   // MMRESULT / TIMERR_NOERROR (types only — see WinmmTimer below)

#include <atomic>
#include <cstring>
#include <thread>
#include <algorithm>

// Forward declarations for ExportAPI symbols (must be outside namespace)
extern "C" bool     UE5_Init();
extern "C" int32_t  UE5_CallProcessEvent(uintptr_t, uintptr_t, uintptr_t);
extern "C" int32_t  UE5_CallProcessEventEx(uintptr_t, uintptr_t, uintptr_t, uint32_t);
extern "C" int32_t  UE5_CallProcessEventDirect(uintptr_t, uintptr_t, uintptr_t);
extern uintptr_t    g_cachedGObjects;
extern uintptr_t    g_cachedGNames;
extern uintptr_t    g_cachedGWorld;   // &GWorld (address of the global UWorld* pointer)
extern uintptr_t    g_cachedGEngine;  // &GEngine (the static slot holding UEngine*), 0 if unresolved

// UE FunctionFlags subset we care about for the static-native fast path.
// Pulled from Engine/Source/Runtime/CoreUObject/Public/UObject/Script.h.
// Only the two flags below are read; defining locally avoids dragging the
// full enum into Mimic just for two bit checks.
static constexpr uint32_t kFuncFlag_Native = 0x00000400u;
static constexpr uint32_t kFuncFlag_Static = 0x00002000u;

// The exported mailbox — zero-initialized by default
extern "C" __declspec(dllexport) Mimic::MailboxData g_invokeMailbox = {};

// Statically initialised: a script must be able to read this the instant the DLL
// is mapped, before StartThread and before any init has run. Nothing writes it at
// runtime — it is a constant published through the export table.
extern "C" __declspec(dllexport) Mimic::MailboxContract g_mailboxContract = {
    Mimic::MAILBOX_CONTRACT_MAGIC,
    Mimic::MAILBOX_CONTRACT,
    Mimic::MAILBOX_CONTRACT_MIN,
};

namespace Mimic {

// Polling thread state
static std::atomic<bool> s_running{false};
static HANDLE s_hThread = nullptr;

// Mailbox poll interval. CE Lua's invokeUFunction blocks waiting for the
// status flag to flip — every ms shaved off this interval is shaved off the
// per-invoke wall-clock latency. Was 10ms historically; lowered to 1ms so a
// tight Lua loop of N invokes doesn't accumulate (N × ~5ms) of pure idle
// time inside the polling loop. CPU cost at idle: one thread ticks 1000x/sec
// doing a handful of volatile reads — negligible. The polling thread bumps
// the Windows timer resolution to 1ms via timeBeginPeriod so Sleep(1) really
// delivers ~1ms on the few systems that still default to the legacy 15.6ms
// system tick (Win10 2004+ scopes this to our process; earlier systems pay
// a small global cost but the DLL is only loaded into game processes that
// almost always already request 1ms resolution for their own frame timing).
static constexpr UINT kPollIntervalMs = 1;

// Audit fix #10: depth counter for compound multi-step operations
// (HandleInvokeByName chains three sub-handlers). When > 0, sub-handlers'
// SetDone/SetError write `result` and `errorMsg` only — they do NOT touch
// `status` or `cmd`, which would prematurely signal completion to CE
// between sub-steps. The outer compound op publishes the final state via
// CompoundOpGuard's destructor.
static int s_compoundDepth = 0;

// Forward declarations
static void HandleFindInstance();
static void HandleFindFunction();
static void HandleInvoke();
static void HandleInvokeByName();
static void HandleListFunctions();
static void HandleListInstances();
static void HandleSetDebugCamera();
static void HandleTeleport();
static void HandleProtect();
static void HandleMovement();
static void HandleTime();
static void HandleFly();
static void HandleForeground();
static void HandleQueryPtr();
static void HandleSeeThrough();
static void SetError(int32_t code, const char* msg);
static void SetDone(int32_t resultCode);
static bool EnsureInitialized();

// RAII guard for compound operations — increments s_compoundDepth on entry,
// decrements on exit. When the outermost guard destructs (depth back to 0)
// it publishes status=DONE / cmd=IDLE based on whatever `result` was last
// written. Catches all return paths (success, early error, exception).
struct CompoundOpGuard {
    CompoundOpGuard() { ++s_compoundDepth; }
    ~CompoundOpGuard() {
        --s_compoundDepth;
        if (s_compoundDepth == 0) {
            g_invokeMailbox.status = STATUS_DONE;
            g_invokeMailbox.cmd    = CMD_IDLE;
        }
    }
};

// ---- winmm timer-resolution access ─────────────────────────────────────────
//
// timeBeginPeriod/timeEndPeriod are resolved from the SYSTEM32 copy of winmm at
// run time instead of being statically imported. That is not stylistic:
//
//   A static import of "winmm.dll!timeBeginPeriod" resolves against whichever
//   module named winmm.dll is loaded in the process. The moment we ship a
//   winmm.dll PROXY (see docs/todo.md, "4th proxy DLL"), that module is US, so
//   the call would land in our own forwarding stub. Before the stub has resolved
//   the real export it returns 0 — and 0 is TIMERR_NOERROR — so the call would
//   SILENTLY SUCCEED while doing nothing: Sleep(1) degrades to the 15.6ms tick
//   and CE-mailbox latency gets ~15x worse with no error anywhere. Delay-loading
//   does not help either, since a delay-load LoadLibrary("winmm.dll") from the
//   game directory finds us again.
//
// Loading by explicit System32 path sidesteps all of it: Windows keys loaded
// modules by full path, so this always yields the genuine OS winmm even when a
// same-named proxy of ours is already mapped.
//
// Deliberately proxy-AGNOSTIC (no UE5_PROXY_* test) so this file can stay in
// UE5DumperCommon, which the invariant in dll/CMakeLists.txt requires.
namespace {

using TimePeriodFn = MMRESULT (WINAPI*)(UINT);

TimePeriodFn g_timeBeginPeriod = nullptr;
TimePeriodFn g_timeEndPeriod   = nullptr;

// Resolve once. The module handle is intentionally never freed: its lifetime is
// the process, and winmm is almost always already loaded by the game anyway.
void EnsureWinmmResolved() {
    static bool s_tried = false;
    if (s_tried) return;
    s_tried = true;

    wchar_t path[MAX_PATH] = {};
    UINT n = GetSystemDirectoryW(path, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) {
        LOG_WARN("Mailbox: GetSystemDirectoryW failed (err=%lu) — timer resolution "
                 "stays at the system default", GetLastError());
        return;
    }
    wcsncat_s(path, L"\\winmm.dll", _TRUNCATE);

    HMODULE h = LoadLibraryW(path);
    if (!h) {
        LOG_WARN("Mailbox: LoadLibraryW(System32 winmm.dll) failed (err=%lu) — timer "
                 "resolution stays at the system default", GetLastError());
        return;
    }
    g_timeBeginPeriod = reinterpret_cast<TimePeriodFn>(GetProcAddress(h, "timeBeginPeriod"));
    g_timeEndPeriod   = reinterpret_cast<TimePeriodFn>(GetProcAddress(h, "timeEndPeriod"));
    if (!g_timeBeginPeriod || !g_timeEndPeriod) {
        LOG_WARN("Mailbox: winmm timeBeginPeriod/timeEndPeriod not found — timer "
                 "resolution stays at the system default");
        g_timeBeginPeriod = nullptr;   // never use one without the other
        g_timeEndPeriod   = nullptr;
    }
}

// Unresolvable → report a non-zero (failure) rc so the caller's existing
// "log and proceed" path runs and the paired EndPeriod is skipped.
MMRESULT BeginTimePeriod(UINT ms) {
    EnsureWinmmResolved();
    return g_timeBeginPeriod ? g_timeBeginPeriod(ms) : TIMERR_NOCANDO;
}

void EndTimePeriod(UINT ms) {
    if (g_timeEndPeriod) g_timeEndPeriod(ms);
}

} // namespace

// ---- Polling thread ----

static void PollingThreadBody();

// Guarded thread entry. The poller resolves names, walks reflection and builds result
// buffers straight out of a live game, so it allocates constantly — and it is a raw
// CreateThread proc, where a throw is std::terminate. (B14)
static DWORD WINAPI PollingThreadProc(LPVOID /*param*/) {
    Routine::RunThreadGuarded("Mailbox", [] { PollingThreadBody(); });
    return 0;
}

static void PollingThreadBody() {
    LOG_INFO("Mailbox: polling thread started (poll=%ums)", kPollIntervalMs);

    // This thread serves CE, not the pipe. Fern's disconnect monitor LATCHES the
    // per-command cancel when a UI client dies mid-command, and in a CE-only session
    // nothing ever clears it (only Fern::Start / AcceptLoop firstConn do). Without this
    // line every object lookup below would then bail at n==0 —
    // FIND_INSTANCE / LIST_INSTANCES / INVOKE_BY_NAME plus the class-scan fallback
    // resolvers in Wirbel/Solitar/Laufen/Hemmung — and still report scanned=<full pool>.
    // NOT MarkBackgroundWorker(): that also refuses (-8) the off-game-thread invoke
    // fallback, a policy scoped to repeating workers. These are the user's one-shot
    // CE invokes. (B4)
    Tot::MarkCancelImmune();

    // Bump Windows timer resolution to 1ms for the lifetime of this thread.
    // Without this, Sleep(1) on a host with the default 15.6ms tick (rare on
    // modern Win10/11 game processes, common on idle/server SKUs) would actually
    // sleep ~15.6ms — completely defeating the latency win. Paired with
    // timeEndPeriod below; balanced so we don't leak the request if the thread
    // exits cleanly. MMSYSERR_NOERROR == 0; non-zero just logs (caller decision
    // is to proceed: even on failure the worst case is Sleep granularity falls
    // back to the system default, not a correctness break).
    MMRESULT tbpRc = BeginTimePeriod(kPollIntervalMs);
    if (tbpRc != TIMERR_NOERROR) {
        LOG_WARN("Mailbox: timeBeginPeriod(%u) failed rc=%u — Sleep granularity "
                 "may fall back to system default", kPollIntervalMs, tbpRc);
    }

    // Audit doc #8: g_invokeMailbox is a plain struct (no atomics). Reads
    // are correct here under the assumption that the writer (CE Lua) uses
    // WriteProcessMemory, which kernel-serializes on x86/x64 with the
    // platform's strong memory model — so cross-process writes become
    // visible without explicit fences. `volatile`-style access prevents
    // compiler reordering. If we ever switch the writer to in-process,
    // the cmd/status fields must become std::atomic<int32_t>.
    while (s_running.load(std::memory_order_acquire)) {
        int32_t cmd = g_invokeMailbox.cmd;

        if (cmd != CMD_IDLE) {
            // Mark as processing
            g_invokeMailbox.status = STATUS_PROCESSING;

            LOG_INFO("Mailbox: received cmd=%d", cmd);

            // The state B4's immunity exists for is otherwise invisible: a UI client
            // that died mid-command leaves the per-command cancel latched forever in a
            // CE-only session, and the old code answered every lookup with an empty set
            // while reporting scanned=<full pool>. Log the FIRST command that runs under
            // a latched cancel — once per latch, so the whole session costs one line.
            {
                static bool s_loggedCancelLatch = false;
                const bool latched = Tot::g_perCommand.load(std::memory_order_relaxed);
                if (latched && !s_loggedCancelLatch) {
                    s_loggedCancelLatch = true;
                    LOG_WARN("Mailbox: cmd=%d runs while a pipe client's per-command cancel is "
                             "latched — this thread is cancel-immune, so lookups still scan (B4)",
                             cmd);
                } else if (!latched) {
                    s_loggedCancelLatch = false;
                }
            }

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
            case CMD_LIST_INSTANCES:
                HandleListInstances();
                break;
            case CMD_SET_DEBUG_CAMERA:
                HandleSetDebugCamera();
                break;
            case CMD_TELEPORT:
                HandleTeleport();
                break;
            case CMD_PROTECT:
                HandleProtect();
                break;
            case CMD_MOVEMENT:
                HandleMovement();
                break;
            case CMD_FLY:
                HandleFly();
                break;
            case CMD_FOREGROUND:
                HandleForeground();
                break;
            case CMD_QUERY_PTR:
                HandleQueryPtr();
                break;
            case CMD_SEETHROUGH:
                HandleSeeThrough();
                break;
            case CMD_TIME:
                HandleTime();
                break;
            default:
                SetError(-1, "Unknown command");
                break;
            }
        }

        Sleep(kPollIntervalMs);
    }

    if (tbpRc == TIMERR_NOERROR) {
        EndTimePeriod(kPollIntervalMs);
    }

    LOG_INFO("Mailbox: polling thread stopped");
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
    auto rset = Aura::FindInstancesByClass(className, false, 100);

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
    uintptr_t classAddr = Ubel::GetClass(instanceAddr);
    if (!classAddr) {
        SetError(-2, "Cannot read UClass from instance");
        return;
    }

    // Walk functions
    auto funcs = Ubel::WalkFunctions(classAddr);

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

    // Pure-helper fast path: a UFunction tagged Native+Static is a C++
    // implementation that takes no implicit `this` and has no hidden
    // dependency on game state. KismetMathLibrary, KismetStringLibrary,
    // KismetArrayLibrary etc. all live here. These are safe to call
    // directly from the pipe thread and -- importantly -- DO NOT need
    // the game thread to ever fire ProcessEvent again.
    //
    // Without this short-circuit, an idle game (main menu / loading
    // screen) leaves the GameThreadDispatch queue undrained and every
    // pure-helper invoke times out at the configured deadline (default
    // 5s, observed in ES2 logs even at 7s). The user has no clue why a
    // simple `exp(8)` math helper would time out -- the call doesn't
    // need a game thread at all.
    //
    // Stateful instance methods (FUNC_Net, FUNC_Event, BlueprintEvent,
    // anything touching actor mutable state from off-thread) still
    // route through GameThreadDispatch via UE5_CallProcessEvent.
    const bool isStaticNative =
        (g_invokeMailbox.functionFlags & (kFuncFlag_Native | kFuncFlag_Static))
            == (kFuncFlag_Native | kFuncFlag_Static);

    int32_t result;
    if (isStaticNative) {
        LOG_INFO("Mailbox: INVOKE -> static-native fast path "
                 "(flags=0x%08X, bypassing GameThreadDispatch)",
                 g_invokeMailbox.functionFlags);
        result = UE5_CallProcessEventDirect(
            instanceAddr, ufuncAddr,
            reinterpret_cast<uintptr_t>(g_invokeMailbox.paramsData));
    } else {
        // Call ProcessEvent via the SIZE-AWARE entry so the queued
        // GameThreadDispatch request OWNS a copy of the param bytes.
        //
        // Audit #2: paramsData is a PERSISTENT global that CE overwrites with its
        // next command the instant we SetDone below. On an idle game (menu /
        // loading) the invoke times out (-5) while STILL queued; the old 3-arg
        // path passed size 0, leaving the request aliasing this shared buffer, so
        // the game thread would later run the ORIGINAL ufunc against the NEXT
        // command's params — silent state corruption (CallProcessEventSEH only
        // masks a wild deref, not a plausible-but-wrong call). Passing the buffer
        // size makes EnqueueInvoke copy into req->ownedParams; a late drain then
        // reads the original params, and the timeout path performs no copy-back so
        // the mailbox is never clobbered after we return.
        // UE5_CallProcessEventEx handles: lazy PE vtable detection, lazy MinHook
        // install, EnqueueInvoke (blocks until game-thread exec / timeout), and
        // the direct-call fallback when the hook isn't active.
        result = UE5_CallProcessEventEx(
            instanceAddr, ufuncAddr,
            reinterpret_cast<uintptr_t>(g_invokeMailbox.paramsData),
            static_cast<uint32_t>(sizeof(g_invokeMailbox.paramsData)));
        if (result == -5) {
            LOG_WARN("Mailbox: INVOKE timed out (game thread idle?). The queued "
                     "request keeps its OWN param copy, so a later drain runs "
                     "safely against the original params (stale result abandoned).");
        }
    }

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

    // Audit fix #10: chain three sub-handlers without leaking intermediate
    // status=DONE / cmd=IDLE writes to CE. The guard suppresses those
    // publishes inside the inner SetDone/SetError calls; on destruction
    // (any return path) it publishes the final state once.
    CompoundOpGuard guard;

    HandleFindInstance();
    if (g_invokeMailbox.result != 0) return;

    HandleFindFunction();
    if (g_invokeMailbox.result != 0) return;

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

        auto rset = Aura::FindInstancesByClass(className, false, 100);
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
    uintptr_t classAddr = Ubel::GetClass(instanceAddr);
    if (!classAddr) {
        SetError(-2, "Cannot read UClass from instance");
        return;
    }

    // Walk all functions
    auto funcs = Ubel::WalkFunctions(classAddr);

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

// Enumerate all live (non-CDO) instances of a class for the property-freeze
// helper. Paginated identically to LIST_FUNCTIONS so the CE Lua side can pull
// multi-page result sets through the single-slot mailbox.
//
// Match policy: chosen by the caller, and the DEFAULT is the exact class.
//
// exactMatch=true was the only policy until contract 3, on the reasoning that a
// freeze wants precise class identity. That reasoning holds for the class the user
// NAMED and fails for the class the UI actually hands over: a Property Search row
// for an inherited field is keyed to the class that DECLARES it, so freezing a
// pawn's `bCanBeDamaged` submits "Actor" and an exact pool returns whichever stray
// AActor the level happens to hold — never the pawn. Solide hit the identical wall
// and fixed it with Aura::FindInstancesDerivedFrom (audit #5 A6); the Freeze button
// and the Force submenu sit on the same row, so they must scope the same way.
// (`[FREEZESCOPE-2026-08-18]`)
//
// The scope is an opt-in bit rather than a policy change, so a script generated
// before contract 3 keeps the pool it was written against.
static void HandleListInstances() {
    char className[256];
    memcpy(className, g_invokeMailbox.className, sizeof(className));
    className[255] = '\0';

    // Read the scope flag and CLEAR it in the same breath, BEFORE any early return.
    //
    // cmdFlags is the one input that would otherwise survive between commands
    // (className and paramsData are both rewritten by the next caller), so a
    // derived request left standing would silently widen the pool of whoever asks
    // next — including a contract-1/2 script that never writes the field and is
    // entitled to exact-match behaviour. Clearing here is what makes the flag
    // genuinely additive rather than sticky.
    const uint32_t inFlags = g_invokeMailbox.cmdFlags;
    g_invokeMailbox.cmdFlags = 0;
    const bool derived = (inFlags & LI_IN_DERIVED) != 0;

    // Rewritten, never accumulated: a stale truncation bit from a previous page
    // would tell the caller its pool was capped when it was not.
    g_invokeMailbox.cmdOutFlags = 0;

    if (className[0] == '\0') {
        SetError(-1, "Empty class name");
        return;
    }

    // Read page index from paramsData[0..3] BEFORE we overwrite the buffer.
    uint32_t pageIndex = 0;
    memcpy(&pageIndex, g_invokeMailbox.paramsData, sizeof(uint32_t));

    LOG_INFO("Mailbox: LIST_INSTANCES class='%s' page=%u scope=%s",
             className, pageIndex, derived ? "derived" : "exact");

    // Caps live in Mimic.h beside the page geometry they have to agree with.
    const int cap = derived ? LIST_INSTANCES_MAX_DERIVED : LIST_INSTANCES_MAX_EXACT;
    Aura::SearchResultSet rset = derived
        ? Aura::FindInstancesDerivedFrom(className, cap)
        : Aura::FindInstancesByClass(className, /*exactMatch=*/true, /*maxResults=*/cap);

    // CDO filter: the class default object (Default__BP_Foo_C) is the
    // template, not a live instance. Freezing its property would touch the
    // template state — never what the user wants.
    //
    // The derived walk already drops CDOs INSIDE its loop (it has to — a base like
    // AActor has thousands of them at the low GObjects indices, and filtering after
    // the cap would return a page of nothing but templates). This second pass is
    // therefore a no-op there and the real filter on the exact path; both are kept
    // so neither path depends on the other's behaviour.
    struct LiveEntry { uint64_t obj; uint64_t cls; };
    std::vector<LiveEntry> live;
    live.reserve(rset.results.size());
    for (const auto& r : rset.results) {
        if (!r.addr || r.name.find("Default__") != std::string::npos) continue;
        live.push_back({ static_cast<uint64_t>(r.addr), static_cast<uint64_t>(r.classAddr) });
    }

    const uint32_t entrySize = ListInstancesEntrySize(derived);
    const uint32_t entriesPerPage = ListInstancesPerPage(derived);

    uint32_t totalCount = static_cast<uint32_t>(live.size());
    uint32_t totalPages = (totalCount + entriesPerPage - 1) / entriesPerPage;
    if (totalPages == 0) totalPages = 1;

    uint32_t startIdx = pageIndex * entriesPerPage;
    uint32_t endIdx = (std::min)(startIdx + entriesPerPage, totalCount);
    uint32_t returnedCount = (startIdx < totalCount) ? (endIdx - startIdx) : 0;

    // parmsSize is uint16 — saturate if a class somehow has >65535 instances.
    g_invokeMailbox.parmsSize = static_cast<uint16_t>(totalCount > 0xFFFFu ? 0xFFFFu : totalCount);
    g_invokeMailbox.numParms = static_cast<uint16_t>(returnedCount);
    g_invokeMailbox.functionFlags = totalPages;

    // "The pool was capped" comes from the walk that did the capping, not from a
    // second test over the page — that is audit #4's own named root cause (report
    // and reality computed by different code paths). A derived sweep off a broad
    // base reaches this routinely, so the caller must be TOLD rather than left to
    // read a truthful-but-partial count as a total.
    if (rset.truncated || rset.aborted)
        g_invokeMailbox.cmdOutFlags |= LI_OUT_TRUNCATED;

    // Contract 2: publish the identity witness a caching caller needs to tell a
    // live instance from a recycled slot (audit #5 AA2/AA3).
    //
    // Read from an actual enumerated instance rather than resolved by name a
    // second time: the caller compares against what it will read out of THESE
    // objects, so the witness has to come from the same place.
    //
    // EXACT scope: every entry shares one class, so one pointer covers the page —
    //   and the page the caller is holding, not just page 0.
    // DERIVED scope (contract 3): there IS no single class, so instanceAddr stays
    //   0 and the witness travels per entry inside paramsData. Publishing the base
    //   class here instead would be worse than useless: the caller would compare
    //   each object's ClassPrivate against a class none of them IS, and refuse
    //   every write while reporting a healthy freeze.
    //
    // Both are cleared first: a previous command may have left an unrelated
    // UObject*/UFunction* here, and a caller must never mistake that for a
    // witness. Zero means "not available" and the caller falls back.
    g_invokeMailbox.instanceAddr = 0;
    g_invokeMailbox.ufuncAddr    = 0;
    if (derived) {
        // The offset is a property of the ENGINE, not of the page, so it is
        // published whenever the scope needs it — the caller treats a zero here as
        // a hard error rather than as a reason to write unguarded.
        g_invokeMailbox.ufuncAddr = static_cast<uint64_t>(Grimoire::OFF_UOBJECT_CLASS);
    } else if (returnedCount > 0) {
        uintptr_t cls = Ubel::GetClass(static_cast<uintptr_t>(live[startIdx].obj));
        if (cls) {
            g_invokeMailbox.instanceAddr = static_cast<uint64_t>(cls);
            g_invokeMailbox.ufuncAddr    = static_cast<uint64_t>(Grimoire::OFF_UOBJECT_CLASS);
        }
    }

    memset(g_invokeMailbox.paramsData, 0, sizeof(g_invokeMailbox.paramsData));

    for (uint32_t i = 0; i < returnedCount; ++i) {
        const LiveEntry& e = live[startIdx + i];
        uint8_t* slot = g_invokeMailbox.paramsData + (i * entrySize);
        memcpy(slot, &e.obj, sizeof(uint64_t));
        if (derived) memcpy(slot + sizeof(uint64_t), &e.cls, sizeof(uint64_t));
    }

    LOG_INFO("Mailbox: LIST_INSTANCES returned %u/%u (page %u/%u) scope=%s%s classWitness=0x%llX",
             returnedCount, totalCount, pageIndex + 1, totalPages,
             derived ? "derived" : "exact",
             (g_invokeMailbox.cmdOutFlags & LI_OUT_TRUNCATED) ? " CAPPED" : "",
             (unsigned long long)g_invokeMailbox.instanceAddr);
    SetDone(0);
}

// ---- Helpers ----

// CMD_SET_DEBUG_CAMERA: robust Debug Camera force on/off, shared with the UI
// pipe (set_debug_camera). instanceAddr carries the request: 0=OFF, 1=ON,
// 2=query (read state, no change). result gets the resulting state (1/0/-1).
// Delegates entirely to the Frieren exports, which own the toggle +
// controller-swap fallback.
static void HandleSetDebugCamera() {
    uint64_t req = g_invokeMailbox.instanceAddr;
    int32_t state = (req == 2)
        ? UE5_GetDebugCameraState()
        : UE5_SetDebugCamera(req != 0 ? 1 : 0);
    if (state == -1) {
        strncpy(g_invokeMailbox.errorMsg,
                "Debug Camera: no live CheatManager / unreadable state",
                sizeof(g_invokeMailbox.errorMsg) - 1);
        g_invokeMailbox.errorMsg[sizeof(g_invokeMailbox.errorMsg) - 1] = '\0';
    }
    LOG_INFO("Mailbox: SET_DEBUG_CAMERA req=%llu -> state=%d",
             (unsigned long long)req, state);
    SetDone(state);
}

// CMD_TELEPORT: marker save/recall + cursor teleport (Wirbel). Field usage
// documented in Mimic.h (TeleportOp enum + CMD_TELEPORT comment block) and
// docs/teleport-spec.md §8. Delegates entirely to Wirbel — this handler only
// marshals the mailbox fields.
static void HandleTeleport() {
    const uint64_t op  = g_invokeMailbox.instanceAddr;
    const int32_t slot = static_cast<int32_t>(g_invokeMailbox.ufuncAddr);

    // Pose block writer: [0..47] 6 doubles, [48..175] mapName,
    // [176] source, [177] tier. Zeroes the whole buffer first.
    auto writePoseBlock = [](const Wirbel::Pose& p, const char* mapName,
                             uint8_t source, uint8_t tier) {
        memset(g_invokeMailbox.paramsData, 0, sizeof(g_invokeMailbox.paramsData));
        double v[6] = { p.X, p.Y, p.Z, p.Pitch, p.Yaw, p.Roll };
        memcpy(g_invokeMailbox.paramsData, v, sizeof(v));
        if (mapName) {
            size_t cap = static_cast<size_t>(Grimoire::TELEPORT_MAPNAME_CAP);
            size_t n = strnlen(mapName, cap - 1);
            memcpy(g_invokeMailbox.paramsData + 48, mapName, n);
            g_invokeMailbox.paramsData[48 + n] = '\0';
        }
        g_invokeMailbox.paramsData[176] = source;
        g_invokeMailbox.paramsData[177] = tier;
    };

    int32_t rc;
    switch (op) {
    case TP_OP_GET_POSE: {
        Wirbel::Pose p{};
        char map[Grimoire::TELEPORT_MAPNAME_CAP] = {};
        uint8_t source = 0;
        rc = Wirbel::GetPose(p, map, sizeof(map), &source);
        if (rc == 0) writePoseBlock(p, map, source, 0);
        break;
    }
    case TP_OP_SAVE: {
        rc = Wirbel::SaveMarker(slot);
        if (rc == 0) {
            Wirbel::Marker m{};
            if (Wirbel::GetMarker(slot, m) == 0)
                writePoseBlock(m.P, m.MapName, 0, 0);
        }
        break;
    }
    case TP_OP_RECALL:
    case TP_OP_RECALL_FORCE: {
        uint8_t tier = 0;
        rc = Wirbel::RecallMarker(slot, op == TP_OP_RECALL_FORCE, &tier);
        memset(g_invokeMailbox.paramsData, 0, sizeof(g_invokeMailbox.paramsData));
        g_invokeMailbox.paramsData[177] = tier;
        break;
    }
    case TP_OP_CURSOR: {
        // Read inputs BEFORE outputs overwrite the shared buffer.
        double zOffset = Grimoire::TELEPORT_DEFAULT_ZOFFSET;
        memcpy(&zOffset, g_invokeMailbox.paramsData, sizeof(double));
        uint8_t channel = g_invokeMailbox.paramsData[8];
        bool fallbackCenter = g_invokeMailbox.paramsData[9] != 0;

        Wirbel::Pose hit{};
        uint8_t tier = 0;
        bool usedCenter = false;
        rc = Wirbel::TeleportToCursor(zOffset, channel, fallbackCenter,
                                      &hit, &tier, &usedCenter);
        memset(g_invokeMailbox.paramsData, 0, sizeof(g_invokeMailbox.paramsData));
        if (rc == 0) {
            double v[3] = { hit.X, hit.Y, hit.Z };
            memcpy(g_invokeMailbox.paramsData, v, sizeof(v));
        }
        g_invokeMailbox.paramsData[177] = tier;
        g_invokeMailbox.paramsData[178] = usedCenter ? 1 : 0;
        break;
    }
    case TP_OP_GET_MARKER: {
        Wirbel::Marker m{};
        rc = Wirbel::GetMarker(slot, m);
        if (rc == 0) writePoseBlock(m.P, m.MapName, 0, 0);
        break;
    }
    case TP_OP_CLEAR_MARKER:
        rc = Wirbel::ClearMarker(slot);
        break;
    case TP_OP_RECALL_LAST: {
        uint8_t tier = 0;
        rc = Wirbel::RecallLast(&tier);
        memset(g_invokeMailbox.paramsData, 0, sizeof(g_invokeMailbox.paramsData));
        g_invokeMailbox.paramsData[177] = tier;
        break;
    }
    case TP_OP_GET_LAST: {
        Wirbel::Marker m{};
        rc = Wirbel::GetLast(m);
        if (rc == 0) writePoseBlock(m.P, m.MapName, 0, 0);
        break;
    }
    case TP_OP_BUGIT_SAVE: {
        Wirbel::Pose p{};
        char map[Grimoire::TELEPORT_MAPNAME_CAP] = {};
        uint8_t source = 0;
        rc = Wirbel::BugItSave(p, map, sizeof(map), &source);
        if (rc == 0) writePoseBlock(p, map, source, 0);
        break;
    }
    case TP_OP_BUGIT_GO: {
        uint8_t tier = 0;
        rc = Wirbel::BugItGo(&tier);
        memset(g_invokeMailbox.paramsData, 0, sizeof(g_invokeMailbox.paramsData));
        g_invokeMailbox.paramsData[177] = tier;
        break;
    }
    case TP_OP_GET_POV: {
        Wirbel::Pov pov{};
        rc = Wirbel::GetPov(pov);
        memset(g_invokeMailbox.paramsData, 0, sizeof(g_invokeMailbox.paramsData));
        if (rc == 0) {
            double cam[6] = { pov.Cam.X, pov.Cam.Y, pov.Cam.Z,
                              pov.Cam.Pitch, pov.Cam.Yaw, pov.Cam.Roll };
            memcpy(g_invokeMailbox.paramsData, cam, sizeof(cam));
            memcpy(g_invokeMailbox.paramsData + 48, &pov.Fov, sizeof(double));
            double pawn[3] = { pov.Pawn.X, pov.Pawn.Y, pov.Pawn.Z };
            memcpy(g_invokeMailbox.paramsData + 56, pawn, sizeof(pawn));
            g_invokeMailbox.paramsData[80] = pov.HasPawn ? 1 : 0;
            g_invokeMailbox.paramsData[81] = pov.Source;
        }
        break;
    }
    case TP_OP_RELATIVE: {
        // Read inputs BEFORE outputs overwrite the shared buffer.
        double distance = 0;
        memcpy(&distance, g_invokeMailbox.paramsData, sizeof(double));
        bool horizontalOnly = g_invokeMailbox.paramsData[8] == 0;  // mode 0 = horizontal
        Wirbel::Pose p{};
        uint8_t tier = 0;
        rc = Wirbel::TeleportRelative(distance, horizontalOnly, p, &tier);
        if (rc == 0) writePoseBlock(p, nullptr, 0, tier);
        break;
    }
    case TP_OP_EXPLICIT: {
        // Read inputs BEFORE outputs overwrite the shared buffer.
        double v[6] = {};
        memcpy(v, g_invokeMailbox.paramsData, sizeof(v));
        bool hasRot = g_invokeMailbox.paramsData[48] != 0;
        Wirbel::Pose p{};
        p.X = v[0]; p.Y = v[1]; p.Z = v[2];
        p.Pitch = v[3]; p.Yaw = v[4]; p.Roll = v[5];
        uint8_t tier = 0;
        rc = Wirbel::RecallExplicit(p, hasRot, &tier);
        memset(g_invokeMailbox.paramsData, 0, sizeof(g_invokeMailbox.paramsData));
        g_invokeMailbox.paramsData[177] = tier;
        break;
    }
    case TP_OP_SET_CURSOR: {
        bool state = false;
        rc = Wirbel::SetMouseCursor(slot != 0, &state);   // ufuncAddr(slot) = 1/0
        memset(g_invokeMailbox.paramsData, 0, sizeof(g_invokeMailbox.paramsData));
        g_invokeMailbox.paramsData[0] = state ? 1 : 0;
        break;
    }
    case TP_OP_GET_CURSOR: {
        bool state = false;
        rc = Wirbel::GetMouseCursor(&state);
        memset(g_invokeMailbox.paramsData, 0, sizeof(g_invokeMailbox.paramsData));
        if (rc == 0) g_invokeMailbox.paramsData[0] = state ? 1 : 0;
        break;
    }
    default:
        SetError(-1, "Teleport: unknown op");
        return;
    }

    if (rc != 0) {
        char msg[256];
        snprintf(msg, sizeof(msg), "Teleport: op=%llu slot=%d failed code=%d",
                 (unsigned long long)op, slot, rc);
        strncpy(g_invokeMailbox.errorMsg, msg, sizeof(g_invokeMailbox.errorMsg) - 1);
        g_invokeMailbox.errorMsg[sizeof(g_invokeMailbox.errorMsg) - 1] = '\0';
    }
    LOG_INFO("Mailbox: TELEPORT op=%llu slot=%d -> rc=%d",
             (unsigned long long)op, slot, rc);
    SetDone(rc);
}

// CMD_PROTECT: GodMode force on/off (Solitar), shared with the UI pipe
// (set_god_mode). instanceAddr = op (ProtectOp), ufuncAddr = value (0/1) for the
// SET op. Delegates entirely to the Frieren exports; this handler only marshals
// the mailbox fields (docs/godmode-spec.md §6.2).
static void HandleProtect() {
    const uint64_t op = g_invokeMailbox.instanceAddr;
    int32_t rc;
    switch (op) {
    case PROTECT_OP_SET_GODMODE:
        rc = UE5_SetGodMode(g_invokeMailbox.ufuncAddr != 0 ? 1 : 0);
        break;
    case PROTECT_OP_GET_GODMODE:
        rc = UE5_GetGodMode();
        break;
    case PROTECT_OP_GET_STATE: {
        int32_t want = 0, live = -1, resolvable = 0;
        rc = UE5_GetProtectState(&want, &live, &resolvable);
        memset(g_invokeMailbox.paramsData, 0, sizeof(g_invokeMailbox.paramsData));
        g_invokeMailbox.paramsData[0] = static_cast<uint8_t>(want ? 1 : 0);
        g_invokeMailbox.paramsData[1] = (live < 0)
            ? 0xFF : static_cast<uint8_t>(live ? 1 : 0);
        g_invokeMailbox.paramsData[2] = static_cast<uint8_t>(resolvable ? 1 : 0);
        break;
    }
    default:
        SetError(-1, "Protect: unknown op");
        return;
    }
    if (rc < 0) {
        char msg[128];
        snprintf(msg, sizeof(msg), "GodMode: op=%llu failed code=%d",
                 (unsigned long long)op, rc);
        strncpy(g_invokeMailbox.errorMsg, msg, sizeof(g_invokeMailbox.errorMsg) - 1);
        g_invokeMailbox.errorMsg[sizeof(g_invokeMailbox.errorMsg) - 1] = '\0';
    }
    LOG_INFO("Mailbox: PROTECT op=%llu -> rc=%d", (unsigned long long)op, rc);
    SetDone(rc);
}

// CMD_FOREGROUND: Keep-Foreground lock (Grausam), shared with the UI pipe
// (set_foreground_lock). instanceAddr = op (ForegroundOp), ufuncAddr = value
// (0/1) for the SET op. Thread-agnostic — Grausam is a pure Win32 hook/subclass,
// so this runs entirely on the mailbox polling thread (no game-thread dispatch),
// and therefore works even while the game thread is idle/paused.
static void HandleForeground() {
    const uint64_t op = g_invokeMailbox.instanceAddr;
    int32_t rc;
    switch (op) {
    case FG_OP_SET:
        rc = Grausam::SetForegroundLock(g_invokeMailbox.ufuncAddr != 0);
        break;
    case FG_OP_GET:
        rc = Grausam::IsForegroundLockEnabled();
        break;
    default:
        SetError(-1, "Foreground: unknown op");
        return;
    }
    LOG_INFO("Mailbox: FOREGROUND op=%llu -> rc=%d", (unsigned long long)op, rc);
    SetDone(rc);
}

// CMD_SEETHROUGH: toggle See-through occluders (Schlacht). instanceAddr = pierce
// count (>=1, how many nearest occluders to hide), ufuncAddr = value (0/1 on/off).
// Schlacht owns its own worker thread that does the game-thread invokes, so this
// handler just flips the flag on the mailbox polling thread.
static void HandleSeeThrough() {
    if (g_invokeMailbox.instanceAddr >= 1)
        Schlacht::SetPierceCount(static_cast<int32_t>(g_invokeMailbox.instanceAddr));
    int32_t rc = Schlacht::SetEnabled(g_invokeMailbox.ufuncAddr != 0);
    LOG_INFO("Mailbox: SEETHROUGH count=%llu value=%llu -> rc=%d",
             (unsigned long long)g_invokeMailbox.instanceAddr,
             (unsigned long long)g_invokeMailbox.ufuncAddr, rc);
    SetDone(rc);
}

// CMD_QUERY_PTR: resolve a global pointer for CE Lua display. instanceAddr = op
// (QueryPtrOp). Output addresses go in paramsData qwords (result is int32, too
// narrow for a 64-bit address). Read-only — reads the cached &GWorld global or
// iterates GObjects for the live UEngine — so it is safe on the polling thread
// even while the game thread is idle (no ProcessEvent dispatch). This exists
// because CE Lua's executeCodeEx can't reliably read an export's return value
// (see docs/godmode-spec.md §10 + docs/lessons-learned.md).
static void HandleQueryPtr() {
    const uint64_t op = g_invokeMailbox.instanceAddr;
    memset(g_invokeMailbox.paramsData, 0, sizeof(g_invokeMailbox.paramsData));

    switch (op) {
    case QUERY_OP_GWORLD: {
        // g_cachedGWorld is &GWorld (the address of the global UWorld* pointer);
        // deref once for the live UWorld*. A recovery slot is a live heap field
        // that still derefs to the current world (docs: RecoverGWorldViaEngine).
        uintptr_t slot  = g_cachedGWorld;
        uintptr_t world = 0;
        if (slot) Macht::ReadSafe(slot, world);
        uint64_t out[2] = { static_cast<uint64_t>(slot), static_cast<uint64_t>(world) };
        memcpy(g_invokeMailbox.paramsData, out, sizeof(out));
        LOG_INFO("Mailbox: QUERY_PTR GWorld -> &GWorld=0x%llX UWorld*=0x%llX",
                 (unsigned long long)slot, (unsigned long long)world);
        if (!slot) {
            SetError(-1, "GWorld not resolved (no static slot / recovery failed)");
            return;
        }
        SetDone(0);
        return;
    }
    case QUERY_OP_GAME_ENGINE: {
        Genau::GameEngineInfo info = Genau::FindGameEngine();
        uint64_t out[2] = { static_cast<uint64_t>(info.engineAddr),
                            static_cast<uint64_t>(info.classAddr) };
        memcpy(g_invokeMailbox.paramsData, out, sizeof(out));
        if (!info.className.empty()) {
            // paramsData[16..143] — 128-byte class-name field (leave a null).
            size_t n = (std::min)(info.className.size(), static_cast<size_t>(127));
            memcpy(g_invokeMailbox.paramsData + 16, info.className.c_str(), n);
            g_invokeMailbox.paramsData[16 + n] = '\0';
        }
        LOG_INFO("Mailbox: QUERY_PTR GameEngine -> 0x%llX class='%s'",
                 (unsigned long long)info.engineAddr, info.className.c_str());
        if (!info.engineAddr) {
            SetError(-1, "GameEngine not found (no live UEngine in GObjects)");
            return;
        }
        SetDone(0);
        return;
    }
    case QUERY_OP_GENGINE_SLOT: {
        // The SLOT, not the object — same shape as QUERY_OP_GWORLD. A CE symbol bound
        // here survives engine recreation because the game keeps writing the current
        // UEngine* into this address; a symbol bound to the object freezes at whatever
        // was live when the record was ticked.
        uintptr_t slot   = g_cachedGEngine;
        uintptr_t engine = 0;
        if (slot) Macht::ReadSafe(slot, engine);
        uint64_t out[2] = { static_cast<uint64_t>(slot), static_cast<uint64_t>(engine) };
        memcpy(g_invokeMailbox.paramsData, out, sizeof(out));
        LOG_INFO("Mailbox: QUERY_PTR GEngineSlot -> &GEngine=0x%llX UEngine*=0x%llX",
                 (unsigned long long)slot, (unsigned long long)engine);
        if (!slot) {
            // Not fatal for the caller: the CE script falls back to QUERY_OP_GAME_ENGINE
            // and publishes a snapshot instead.
            SetError(-1, "GEngine slot not resolved (no AOB validated)");
            return;
        }
        SetDone(0);
        return;
    }
    default:
        SetError(-1, "QueryPtr: unknown op");
        return;
    }
}

// CMD_MOVEMENT (Laufen): set one CharacterMovement float knob to a percent.
// instanceAddr = knobId (0/1/2); paramsData[0..7] = double percent (100 = off,
// knob 2 = jump height %). The percent is read BEFORE SetDone (paramsData is not
// overwritten with outputs here). Returns 1 (active) / 0 (off) / negative.
static void HandleMovement() {
    const uint64_t knobId = g_invokeMailbox.instanceAddr;
    int32_t rc;
    if (knobId == 3) {
        // Gravity direction (UE5.4+): 3 doubles x/y/z in paramsData. (0,0,0) = off.
        double v[3] = {};
        memcpy(v, g_invokeMailbox.paramsData, sizeof(v));
        rc = UE5_SetGravityDirection(v[0], v[1], v[2]);
        LOG_INFO("Mailbox: MOVEMENT gravdir -> (%.3f, %.3f, %.3f) rc=%d",
                 v[0], v[1], v[2], rc);
    } else {
        double percent = 0.0;
        memcpy(&percent, g_invokeMailbox.paramsData, sizeof(double));
        rc = UE5_SetMovementPercent(static_cast<int32_t>(knobId), percent);
        LOG_INFO("Mailbox: MOVEMENT knob=%llu pct=%.1f -> rc=%d",
                 (unsigned long long)knobId, percent, rc);
    }
    if (rc < 0) {
        char msg[128];
        snprintf(msg, sizeof(msg), "Movement: knob=%llu failed code=%d",
                 (unsigned long long)knobId, rc);
        strncpy(g_invokeMailbox.errorMsg, msg, sizeof(g_invokeMailbox.errorMsg) - 1);
        g_invokeMailbox.errorMsg[sizeof(g_invokeMailbox.errorMsg) - 1] = '\0';
    }
    SetDone(rc);
}

// CMD_TIME (Hemmung): hold a reflected dilation float at an absolute value, or
// reset it. instanceAddr = op (TimeOp); ufuncAddr = target (0 global / 1 pawn);
// paramsData[0..7] = double value for SET (read BEFORE SetDone). Returns
// 1 (active) / 0 (off) / negative Hemmung::TimeResult.
static void HandleTime() {
    const uint64_t op = g_invokeMailbox.instanceAddr;
    const int32_t  target = static_cast<int32_t>(g_invokeMailbox.ufuncAddr);
    int32_t rc;
    switch (op) {
    case TIME_OP_SET: {
        double value = 0.0;
        memcpy(&value, g_invokeMailbox.paramsData, sizeof(double));
        rc = UE5_SetTimeDilation(target, value);
        LOG_INFO("Mailbox: TIME set target=%d value=%.4f -> rc=%d", target, value, rc);
        break;
    }
    case TIME_OP_RESET:
        rc = UE5_ResetTimeDilation(target);
        LOG_INFO("Mailbox: TIME reset target=%d -> rc=%d", target, rc);
        break;
    default:
        rc = -1;
        LOG_WARN("Mailbox: TIME unknown op=%llu", (unsigned long long)op);
        break;
    }
    if (rc < 0) {
        char msg[128];
        snprintf(msg, sizeof(msg), "Time: op=%llu target=%d failed code=%d",
                 (unsigned long long)op, target, rc);
        strncpy(g_invokeMailbox.errorMsg, msg, sizeof(g_invokeMailbox.errorMsg) - 1);
        g_invokeMailbox.errorMsg[sizeof(g_invokeMailbox.errorMsg) - 1] = '\0';
    }
    SetDone(rc);
}

// CMD_FLY (Dunste): toggle no-gravity flight + set speed/preset, shared with the
// UI pipe (fly_set). instanceAddr = op (FlyOp); ufuncAddr / paramsData carry the
// operand (read BEFORE SetDone). GET_STATE writes the live status into paramsData.
static void HandleFly() {
    const uint64_t op = g_invokeMailbox.instanceAddr;
    int32_t rc;
    switch (op) {
    case FLY_OP_SET_ENABLED:
        rc = UE5_SetFly(static_cast<int32_t>(g_invokeMailbox.ufuncAddr) != 0 ? 1 : 0);
        LOG_INFO("Mailbox: FLY enable=%llu -> rc=%d",
                 (unsigned long long)g_invokeMailbox.ufuncAddr, rc);
        break;
    case FLY_OP_SET_SPEED: {
        double speed = 0.0;
        memcpy(&speed, g_invokeMailbox.paramsData, sizeof(double));
        rc = UE5_SetFlySpeed(speed);
        LOG_INFO("Mailbox: FLY speed=%.0f -> rc=%d", speed, rc);
        break;
    }
    case FLY_OP_SET_PRESET:
        rc = UE5_SetFlyPreset(static_cast<int32_t>(g_invokeMailbox.ufuncAddr));
        LOG_INFO("Mailbox: FLY preset=%llu -> rc=%d",
                 (unsigned long long)g_invokeMailbox.ufuncAddr, rc);
        break;
    case FLY_OP_SET_NOCLIP:
        rc = UE5_SetFlyNoclip(static_cast<int32_t>(g_invokeMailbox.ufuncAddr) != 0 ? 1 : 0);
        LOG_INFO("Mailbox: FLY noclip=%llu -> rc=%d",
                 (unsigned long long)g_invokeMailbox.ufuncAddr, rc);
        break;
    case FLY_OP_GET_STATE: {
        Dunste::FlyStatus st{};
        rc = Dunste::GetStatus(st);
        g_invokeMailbox.paramsData[0] = st.active ? 1 : 0;
        g_invokeMailbox.paramsData[1] = static_cast<uint8_t>(st.preset);
        g_invokeMailbox.paramsData[2] = st.noclip ? 1 : 0;
        memcpy(g_invokeMailbox.paramsData + 8, &st.speed, sizeof(double));
        break;
    }
    default:
        rc = Dunste::FR_ERR_REFLECT;
        break;
    }
    if (rc < 0) {
        char msg[128];
        snprintf(msg, sizeof(msg), "Fly: op=%llu failed code=%d",
                 (unsigned long long)op, rc);
        strncpy(g_invokeMailbox.errorMsg, msg, sizeof(g_invokeMailbox.errorMsg) - 1);
        g_invokeMailbox.errorMsg[sizeof(g_invokeMailbox.errorMsg) - 1] = '\0';
    }
    SetDone(rc);
}

static void SetError(int32_t code, const char* msg) {
    g_invokeMailbox.result = code;
    if (msg) {
        strncpy(g_invokeMailbox.errorMsg, msg, sizeof(g_invokeMailbox.errorMsg) - 1);
        g_invokeMailbox.errorMsg[sizeof(g_invokeMailbox.errorMsg) - 1] = '\0';
    }
    LOG_WARN("Mailbox: error=%d msg='%s'", code, msg ? msg : "");

    // Audit fix #10: inside a compound op (HandleInvokeByName), the outer
    // CompoundOpGuard publishes status/cmd; suppress the intermediate
    // signal here so CE doesn't see a transient DONE between sub-steps.
    if (s_compoundDepth > 0) return;

    // Signal completion — MUST write status BEFORE clearing cmd
    g_invokeMailbox.status = STATUS_DONE;
    g_invokeMailbox.cmd = CMD_IDLE;
}

static void SetDone(int32_t resultCode) {
    g_invokeMailbox.result = resultCode;

    // Audit fix #10: see SetError comment.
    if (s_compoundDepth > 0) return;

    // Signal completion — MUST write status BEFORE clearing cmd
    g_invokeMailbox.status = STATUS_DONE;
    g_invokeMailbox.cmd = CMD_IDLE;
}

} // namespace Mimic
