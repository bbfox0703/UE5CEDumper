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
#include "Stark.h"      // Stark::kInvokeTimedOutStillQueued -- a queued Debug Camera toggle [W3-DEBUGCAM-QUEUED]
#include "Tot.h"      // Tot::MarkCancelImmune — the poller must survive a pipe client's death (B4)
#include "Routine.h"   // Routine::RunThreadGuarded — a throw out of a thread proc is std::terminate (B14)
#include "Voll.h"      // Voll::Holds -- who holds what (Stage D2-1)
#include "Land.h"      // Land::MirroredState / kMaxCopies -- an earlier copy's mailbox served here

#include <Windows.h>
#include <timeapi.h>   // MMRESULT / TIMERR_NOERROR (types only — see WinmmTimer below)

#include <atomic>
#include <cstring>
#include <exception>   // std::uncaught_exceptions — CompoundOpGuard's unwind branch (MB3)
#include <mutex>
#include <string>
#include <thread>
#include <algorithm>

// Forward declarations for ExportAPI symbols (must be outside namespace)
extern "C" bool     UE5_Init();
extern "C" int32_t  UE5_CallProcessEvent(uintptr_t, uintptr_t, uintptr_t);
extern "C" int32_t  UE5_CallProcessEventEx(uintptr_t, uintptr_t, uintptr_t, uint32_t);
extern "C" int32_t  UE5_CallProcessEventDirect(uintptr_t, uintptr_t, uintptr_t);
// [VND584-01] Frieren.cpp: a picked object in a weak / soft parameter becomes the engine's own value before the call.
namespace FrierenInvoke {
bool PrepareObjectParams(uintptr_t ufunc, uint8_t* params, size_t size, bool direct, std::string& why);
}
// [D2-3-INVOKE] Frieren.cpp: whether a debug-camera ON is pending, and which client sent it (ChangeHoldAsCe).
namespace FrierenDebugCamera {
bool PendingOn(uint64_t& owner);
}
extern uintptr_t    g_cachedGObjects;
extern uintptr_t    g_cachedGNames;
extern uintptr_t    g_cachedGWorld;   // &GWorld (address of the global UWorld* pointer)
extern uintptr_t    g_cachedGEngine;  // &GEngine (the static slot holding UEngine*), 0 if unresolved
extern std::atomic<bool> g_initInProgress;   // [A3-MIMIC-INIT-FASTPATH] Frieren.cpp: an init is scanning

// The UE FunctionFlags bits and the routing predicate that reads them now live in
// Mimic.h (FUNC_FLAG_NATIVE / FUNC_FLAG_STATIC / ShouldRouteDirectInvoke) so a test
// target can pin the rule — nothing compiles this .cpp. (audit #5 MB1)

// The exported mailbox — zero-initialized by default
extern "C" __declspec(dllexport) Mimic::MailboxData g_invokeMailbox = {};

// Statically initialised: a script must be able to read this the instant the DLL
// is mapped, before StartThread and before any init has run. This copy never writes it.
// [DUMPER-SECOND-COPY-AUTOSTART] A LATER copy of the dumper does, once: it zeroes the magic when
// it took this process over and cannot serve this mailbox (Land::TakeMailbox), so a script
// refuses at its contract check -- which is why this stays writable data, never const. A copy
// whose magic was zeroed is still recognised by DUMPER_LEGACY_IDENTITY_EXPORT.
// Its name and magic are also how one build of the dumper recognises another in the same
// process (Grimoire::DUMPER_IDENTITY_EXPORT, [AOB-SKIP-OWN-DLL]): rename it and that breaks too.
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

// [DUMPER-SECOND-COPY-AUTOSTART] The mailbox the command being handled came from. The poller serves this copy's own
// g_invokeMailbox and, once this copy has taken over from an earlier copy of the dumper in the process (Land), that
// copy's mailbox too: a CE script that resolved the earlier copy's export writes there. Handlers read and answer through
// Box(); only the poller sets it, and every handler runs on the poller's thread. Per thread: a copy can run two
// pollers for a while -- a stop that gave up waiting on a handler, then a restart -- and each must answer in the mailbox
// its own command came from.
static thread_local MailboxData* s_box = &g_invokeMailbox;
static MailboxData& Box() { return *s_box; }

// Earlier copies' mailboxes this copy serves (AdoptMailbox). A slot once set is never cleared: the earlier copy pins its
// image, so its mailbox stays mapped for the life of the process.
static std::atomic<MailboxData*> s_adopted[Land::kMaxCopies] = {};
// Whether the copy that owns each adopted mailbox forwards its exports to this one (Land::MirroredState).
static std::atomic<bool> s_adoptedForwards[Land::kMaxCopies] = {};

// Forward declarations
static void HandleFindInstance();
static void HandleFindFunction();
static void HandleInvoke();
static void HandleInvokeHeld();
static void HandleInvokeByName();
template <typename Change>
static bool TryChangeHoldAsCe(const std::string& hold, Change&& change);
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
static void HandleOffsetsVerdict();
static void SetError(int32_t code, const char* msg);
static void SetDone(int32_t resultCode);
static bool EnsureInitialized();

// Result code published when a command handler escapes with an exception. A new
// NEGATIVE result code is not a contract change: `MAILBOX_CONTRACT` versions the
// layout plus the Cmd/op numbering, and every generated script already treats any
// non-zero `result` as failure and renders `errorMsg` verbatim. Distinct from -1
// ("Unknown command") on purpose — this one means the command WAS recognised and
// dispatched, which is a different thing to tell the user.
static constexpr int32_t MB_ERR_HANDLER_THREW = -11;

// RAII guard for compound operations — increments s_compoundDepth on entry,
// decrements on exit. When the outermost guard destructs (depth back to 0)
// it publishes status=DONE / cmd=IDLE based on whatever `result` was last
// written. Catches all return paths (success, early error, exception).
//
// ⚠ "any return path" includes UNWINDING, and that path needed its own answer
// (audit #5 MB3, sibling of the per-command guard in ServeOne).
// `result` holds whatever the last COMPLETED sub-step wrote, and the sub-steps
// only write it on failure — HandleInvokeByName returns early when it is
// non-zero, so at the moment a later sub-step throws `result` is normally 0.
// Publishing DONE with that reports SUCCESS for a command that never ran to
// completion, and CE Lua reads `result == 0` as "the invoke worked": strictly
// worse than the hang it replaced, because the caller then trusts the params
// buffer. Comparing against the count captured at ENTRY (not `!= 0`) is what
// makes this correct inside a nested unwind.
struct CompoundOpGuard {
    CompoundOpGuard() : uncaughtAtEntry(std::uncaught_exceptions()) { ++s_compoundDepth; }
    ~CompoundOpGuard() {
        --s_compoundDepth;
        if (s_compoundDepth == 0) {
            if (std::uncaught_exceptions() > uncaughtAtEntry) {
                Box().result = MB_ERR_HANDLER_THREW;
                strncpy(Box().errorMsg,
                        "command handler threw — the operation did NOT complete",
                        sizeof(Box().errorMsg) - 1);
                Box().errorMsg[sizeof(Box().errorMsg) - 1] = '\0';
            }
            Box().status = STATUS_DONE;
            Box().cmd    = CMD_IDLE;
        }
    }

    int uncaughtAtEntry;
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

// One pass over one mailbox: the command waiting there, if any, run and answered in that mailbox. `warnedThrow` is the
// thread's one WARN for a throwing handler (Routine::RunTickGuarded).
static void ServeOne(MailboxData* box, bool& warnedThrow) {
    s_box = box;
    // [DUMPER-SECOND-COPY-AUTOSTART] Another poller is mid-command on this mailbox -- the earlier copy's, still in a
    // handler after its stop gave up waiting for it. It answers that command itself; running it here too would run it
    // twice.
    const int32_t seen = Box().status;
    if (seen == STATUS_PROCESSING) return;
    int32_t cmd = Box().cmd;
    // ...or finishing one: its SetDone writes DONE and then cmd = IDLE, and a pass between the two stores would run that
    // command again. A sender clears status before it writes cmd, so DONE beside a live cmd is that window or a sender
    // that did not; look again once the other poller has had its moment.
    if (cmd != CMD_IDLE && seen == STATUS_DONE) {
        Sleep(1);
        if (Box().cmd == CMD_IDLE) return;
    }

    if (cmd != CMD_IDLE) {
        // Mark as processing
        Box().status = STATUS_PROCESSING;

        // Clear the previous command's error text BEFORE dispatching this one.
        // SetError writes errorMsg; SetDone writes only `result` and never touches
        // it -- so without this a SUCCESSFUL command would inherit the last failure's
        // message, and a caller reading errorMsg without checking `result` first would
        // see a stale error on a command that worked. (MBERRSTALE-2026-08-23)
        //
        // Cleared HERE, not after the handler: a post-clear would wipe the message a
        // handler had just set via SetError. Pre-clearing leaves errorMsg empty on
        // success and populated on failure -- the only consistent pairing.
        Box().errorMsg[0] = '\0';
        // [INVOKE-NONSTATIC-ON-CDO] Same reasoning for the output flags (contract 7): an INVOKE_BY_NAME whose
        // lookup fails never reaches HandleInvoke, and must not carry the previous invoke's warning.
        Box().cmdOutFlags = 0;

        LOG_INFO("Mailbox: received cmd=%d", cmd);

        // The state this thread's cancel immunity exists for is otherwise invisible: a
        // UI client that died mid-command leaves the per-command cancel latched in a
        // CE-only session, and a thread that honoured it would answer every lookup with
        // an empty set while reporting scanned=<full pool>. Log the FIRST command that
        // runs under a latched cancel — once per latch, so the whole session costs one line.
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

        // Auto-init if needed (proxy DLL mode: UE5_Init not called yet).
        //
        // Skipped entirely for the commands CommandRequiresInit() exempts —
        // CMD_FOREGROUND and CMD_OFFSETS_VERDICT, for two different reasons.
        //
        // CMD_FOREGROUND: its handler is pure Win32 and the PIPE path services it
        // with no init gate at all. Gating it made the CE-Lua Keep-Foreground
        // toggle fail with -10 on any game whose AOB scan fails, and the generated
        // script renders that as "hook error -10", naming MinHook — a subsystem the
        // command never even reached. The exemption also skips the auto-init
        // ATTEMPT, so the toggle no longer pays for a whole-image sweep it does not
        // need. (audit #5 MB2)
        //
        // CMD_OFFSETS_VERDICT: its answer IS the init state — it reports whether the
        // offsets were measured, and "no probe has run" is one of the answers. Gating
        // it would return -10 in exactly the case the caller asked about, and
        // auto-init would run a whole-image sweep to answer a question about whether
        // that sweep has happened. ([W5-OFFSETS-MAILBOX]; Mimic.h carries the rule,
        // and this comment is checked against it by a source pin — [A1-REVIEW6-PINS])
        if (CommandRequiresInit(cmd) && !EnsureInitialized()) {
            // Init failed — these commands all walk UE reflection.
            if (cmd == CMD_INVOKE || cmd == CMD_INVOKE_BY_NAME) {
                SetError(-10, "DLL not initialized (GObjects/GNames not found)");
                return;
            }
            // FIND_INSTANCE/FIND_FUNCTION also need init
            SetError(-10, "DLL not initialized");
            return;
        }

        // ---- audit #5 MB3: guard the DISPATCH, not the whole thread ----
        //
        // PollingThreadProc's Routine::RunThreadGuarded is the outer net that
        // stops a throw reaching a frame with no handler, and it stays. But it
        // was ALSO the only guard, so one throwing handler unwound straight out
        // of this loop and the CE mailbox was dead for the rest of the session —
        // recoverable only by a CE Disable→Enable the user has no reason to try,
        // since every subsequent script just times out.
        //
        // This is now the per-iteration form the two siblings already use:
        // Routine::ReassertLoop guards per TICK, Stark guards per PE CALL. One
        // bad command loses that command, not the mailbox.
        //
        // `threw` is set false by the lambda's own last statement rather than by
        // a catch, so it stays true on ANY escape, including one the compiler
        // routes around a `break`.
        bool threw = true;
        Routine::RunTickGuarded("Mailbox", warnedThrow, [&] {
        switch (cmd) {
        case CMD_FIND_INSTANCE:
            HandleFindInstance();
            break;
        case CMD_FIND_FUNCTION:
            HandleFindFunction();
            break;
        case CMD_INVOKE:
            HandleInvokeHeld();
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
        case CMD_OFFSETS_VERDICT:
            HandleOffsetsVerdict();
            break;
        default:
            SetError(-1, "Unknown command");
            break;
        }
        threw = false;
        });

        // Publish a definite failure rather than leaving status=PROCESSING for
        // the script to time out on. CompoundOpGuard has already run for the one
        // compound handler and written the same code; SetError here is what
        // covers the other fourteen, which have no guard at all.
        if (threw) {
            SetError(MB_ERR_HANDLER_THREW,
                     "command handler threw — the operation did NOT complete");
        }
    }
}

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
    // per-command cancel when a UI client dies mid-command, and it stays set until that
    // connection has unregistered (Fern::ReevaluatePerCommandCancel) — and a pipe client's
    // death is no reason to cancel CE's work at all. Without this line every object
    // lookup below would bail at n==0 while it is set —
    // FIND_INSTANCE / LIST_INSTANCES / INVOKE_BY_NAME plus the class-scan fallback
    // resolvers in Wirbel/Solitar/Laufen/Hemmung — and still report scanned=<full pool>.
    // NOT MarkBackgroundWorker(): that also refuses (-8) the off-game-thread invoke
    // fallback, a policy scoped to repeating workers. These are the user's one-shot
    // CE invokes. (B4)
    Tot::MarkCancelImmune();
    // [D2-3-INVOKE] Every invoke this thread queues is CE's, for the queue's owner rule: an AI invoke reports a
    // conflict with one CE has outstanding. CE never sends if_free, so its own invokes always queue.
    const Stark::InvokeOwnerScope ceInvokes(Voll::kHoldOwnerCe, false);

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

    // One WARN per session for a throwing handler, matching Routine::ReassertLoop:
    // the poller runs at kPollIntervalMs, so an unlatched warning would fill the log.
    bool warnedThrow = false;

    while (s_running.load(std::memory_order_acquire)) {
        // [DUMPER-SECOND-COPY-AUTOSTART] This copy's mailbox first, then each earlier copy's it took over, one command
        // each per pass. An adopted mailbox says what this copy's says, or for a copy that cannot forward only READY or
        // SKIPPED (Land::MirroredState): a CE inject script that resolved the earlier copy reads it as this copy.
        ServeOne(&g_invokeMailbox, warnedThrow);
        for (int slot = 0; slot < Land::kMaxCopies; ++slot) {
            MailboxData* box = s_adopted[slot].load(std::memory_order_acquire);
            if (!box) continue;
            box->initState = Land::MirroredState(g_invokeMailbox.initState,
                                                 s_adoptedForwards[slot].load(std::memory_order_acquire));
            ServeOne(box, warnedThrow);
        }
        s_box = &g_invokeMailbox;
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

void StopThread(bool clearMailbox) {
    if (!s_running.load()) return;

    s_running.store(false, std::memory_order_release);

    if (s_hThread) {
        WaitForSingleObject(s_hThread, 3000);
        CloseHandle(s_hThread);
        s_hThread = nullptr;
    }

    // Clear mailbox -- unless a later copy of the dumper is about to serve it (the hand-off's own shutdown).
    if (clearMailbox) memset(&g_invokeMailbox, 0, sizeof(g_invokeMailbox));
    // [DUMPER-SECOND-COPY-AUTOSTART] Nobody serves the earlier copies' mailboxes now. Only initState is touched: a
    // command a script wrote there is its own. An untick says IDLE where the owner forwards (a revival reaches this
    // copy); a hand-off says SKIPPED, so the copy taking over from this one retires that owner too and takes its
    // mailbox; an owner that cannot forward says SKIPPED either way.
    for (int slot = 0; slot < Land::kMaxCopies; ++slot)
        if (MailboxData* box = s_adopted[slot].load(std::memory_order_acquire))
            box->initState = Land::MirroredState(clearMailbox ? INIT_IDLE : INIT_SKIPPED,
                                                 s_adoptedForwards[slot].load(std::memory_order_acquire));
}

bool AdoptMailbox(MailboxData* earlier, bool earlierForwards) {
    if (!earlier || earlier == &g_invokeMailbox) return false;
    for (auto& slot : s_adopted)
        if (slot.load(std::memory_order_acquire) == earlier) return true;
    for (int slot = 0; slot < Land::kMaxCopies; ++slot) {
        MailboxData* expected = nullptr;
        // The flag lands just after the claim: a pass that sees the slot before it reads false, the stricter mirror
        // (READY or SKIPPED), never a laxer one.
        if (s_adopted[slot].compare_exchange_strong(expected, earlier, std::memory_order_acq_rel)) {
            s_adoptedForwards[slot].store(earlierForwards, std::memory_order_release);
            earlier->initState = Land::MirroredState(g_invokeMailbox.initState, earlierForwards);
            LOG_INFO("Mailbox: serving an earlier copy's mailbox at %p beside this copy's own",
                     static_cast<void*>(earlier));
            return true;
        }
    }
    LOG_WARN("Mailbox: no slot left to serve the earlier copy's mailbox at %p (%d in use)",
             static_cast<void*>(earlier), Land::kMaxCopies);
    return false;
}

uintptr_t GetAddress() {
    return reinterpret_cast<uintptr_t>(&g_invokeMailbox);
}

// ---- Auto-initialization ----

// [R7-S9] The globals are published INSIDE the fence (UE5_Init after FindAll, apply_rescan after BeginApply), so read
// them FIRST and the flag LAST: seeing a published GObjects then guarantees seeing the flag that preceded it. As
// arguments to one call the reads had no order at all -- C++ leaves argument evaluation unspecified.
static bool InitSettled() {
    const bool haveGObjects = g_cachedGObjects != 0;
    const bool haveGNames   = g_cachedGNames != 0;
    std::atomic_thread_fence(std::memory_order_acquire);
    const bool inProgress   = g_initInProgress.load(std::memory_order_acquire);
    return Mimic::InitFastPathOk(haveGObjects, haveGNames, inProgress);
}

static bool EnsureInitialized() {
    // UE5_Init is idempotent (checks internal s_initialized flag)
    // Note: extern declarations are at file scope (above namespace)
    // [A3-MIMIC-INIT-FASTPATH] The globals alone are not "initialized": UE5_Init publishes them right after FindAll,
    // 190-445 ms before Serie / Aura init and ValidateAndFixOffsets finish. While an init is scanning, fall through to
    // UE5_Init, which waits on s_initMutex (and logs that it is waiting) and returns the first caller's result.
    if (InitSettled()) {
        return true;  // Already initialized
    }

    LOG_INFO("Mailbox: auto-initializing (UE5_Init)...");
    UE5_Init();
    // [R7-S9] An initialised process returns from UE5_Init at once when no fence was up at ITS check, and an
    // apply_rescan may raise one right after. Judge the result the same way, and while a fence is up let UE5_Init wait
    // it out (its already-initialised branch takes s_initMutex). Bounded: a transient "not ready" beats a hang.
    for (int i = 0; i < 4 && !InitSettled() && g_initInProgress.load(std::memory_order_acquire); ++i)
        UE5_Init();

    return InitSettled();
}

// ---- Command handlers ----

static void HandleFindInstance() {
    // Read class name from mailbox
    char className[256];
    memcpy(className, Box().className, sizeof(className));
    className[255] = '\0';

    if (className[0] == '\0') {
        SetError(-1, "Empty class name");
        return;
    }

    LOG_INFO("Mailbox: FIND_INSTANCE class='%s'", className);

    // [FINDINSTANCE-SUBSTRING-USERFACING] The user-facing resolver (Aura.h); its rule is contract 9 (6, + templates in
    // 7, + destroyed objects in 9).
    const Aura::UserInstance found = Aura::ResolveUserInstance(className);
    Box().instanceAddr = found.obj;
    char msg[256];
    if (found.pick == Aura::UserPick::Cancelled) {
        // This thread is cancel-immune (Tot::MarkCancelImmune), so only a shutdown cuts the walk short.
        snprintf(msg, sizeof(msg), "Instance lookup for '%s' was cut short: the DLL is shutting down", className);
        SetError(-2, msg);
    } else if (found.obj) {
        LOG_INFO("Mailbox: FIND_INSTANCE '%s' -> 0x%llX (%s)", className, (unsigned long long)found.obj,
                 Aura::UserPickWire(found.pick));
        SetDone(0);
    } else {
        snprintf(msg, sizeof(msg), "No instance of '%s' found (scanned=%d)", className, found.scanned);
        SetError(-2, msg);
    }
}

// Adapters for Ubel::ResolveFunctionInChain — the live-memory half the header's
// traversal deliberately does not contain. [INVOKEINHERIT-2026-08-20]
static inline std::vector<FunctionInfo> ChainListFuncs(uintptr_t cls) {
    return Ubel::WalkFunctions(cls);
}
static inline bool ChainReadSuper(uintptr_t cls, uintptr_t& super) {
    return Macht::ReadSafe(cls + static_cast<uintptr_t>(DynOff::USTRUCT_SUPER), super);
}
// [VND584-04] A class's own implemented interfaces (empty while UClass::Interfaces is unmeasured).
static inline std::vector<Ubel::ImplementedInterface> ChainListInterfaces(uintptr_t cls) {
    return Ubel::ClassInterfaces(cls);
}

static void HandleFindFunction() {
    uintptr_t instanceAddr = Box().instanceAddr;

    char funcName[256];
    memcpy(funcName, Box().funcName, sizeof(funcName));
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

    // Search the class AND ITS BASES, so an inherited UFunction is reachable from CE Lua by
    // name [INVOKEINHERIT-2026-08-20]. Same resolver as UE5_FindFunctionByName, so the pipe
    // and the mailbox cannot answer the same question differently.
    FunctionInfo hit;
    int levels = 0;
    // [VND584-04] ...and then their implemented interfaces, as UClass::FindFunctionByName does.
    const bool found = Ubel::ResolveFunctionInChainOrInterfaces(
        classAddr, funcName, ChainListFuncs, ChainReadSuper, ChainListInterfaces, hit, &levels);

    uintptr_t ufuncAddr = found ? hit.address : 0;
    uint16_t parmsSize = found ? hit.parmsSize : 0;
    uint16_t numParms = found ? hit.numParms : 0;
    uint32_t funcFlags = found ? hit.functionFlags : 0;

    if (ufuncAddr) {
        Box().ufuncAddr = ufuncAddr;
        Box().parmsSize = parmsSize;
        Box().numParms = numParms;
        Box().functionFlags = funcFlags;
        LOG_INFO("Mailbox: FIND_FUNCTION '%s' -> 0x%llX (parmsSize=%u numParms=%u flags=0x%X)",
                 funcName, (unsigned long long)ufuncAddr,
                 parmsSize, numParms, funcFlags);
        SetDone(0);
    } else {
        Box().ufuncAddr = 0;
        char msg[256];
        snprintf(msg, sizeof(msg),
                 "Function '%s' not found (%d class level(s) searched, own class + supers)",
                 funcName, levels);
        SetError(-3, msg);
    }
}

static void HandleInvoke() {
    uintptr_t instanceAddr = Box().instanceAddr;
    uintptr_t ufuncAddr = Box().ufuncAddr;

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
    //
    // The flags come from the UFunction that `ufuncAddr` NAMES, re-read here, and
    // NOT from g_invokeMailbox.functionFlags. That field is a DLL-filled output —
    // CMD_INVOKE's documented inputs are instanceAddr / ufuncAddr / paramsData —
    // so it holds whatever the previous command left at offset 0x024:
    //   * CMD_FIND_FUNCTION leaves the flags of the function IT resolved, which for
    //     a bare re-FIRE (the generated form's FIRE button re-issues CMD_INVOKE
    //     without re-running CMD_FIND_FUNCTION) can be a DIFFERENT function — a
    //     stale Native|Static then sends a stateful actor UFunction off the game
    //     thread, exactly what the comment above forbids;
    //   * CMD_LIST_FUNCTIONS and CMD_LIST_INSTANCES overwrite it with a PAGE COUNT.
    // ResolveFunctionInfo also validates the meta-class is "Function", so a stale or
    // recycled ufuncAddr fails safe instead of routing on garbage: it is refused just
    // below, because a function that does not resolve cannot be sized either. (audit #5 MB1)
    FunctionInfo fi{};   // global scope, like Fern's Linie query path
    const bool flagsResolved = Ubel::ResolveFunctionInfo(ufuncAddr, fi);

    // [UE-OVERRIDE-411] review 2: ProcessEvent writes into this fixed slab (or into the queued
    // path's owned copy of it), so the function's parameter block has to fit it -- measured by
    // its own chain, since a ParmsSize read under a wrong UE version is another field.
    // Mimic::InvokeSlabRefusal says why each case is refused.
    const uint32_t needBytes = flagsResolved ? Ubel::ParamBufferSize(ufuncAddr, fi.parmsSize) : 0;
    const int32_t refusal = InvokeSlabRefusal(flagsResolved, needBytes,
                                              static_cast<uint32_t>(sizeof(Box().paramsData)));
    if (refusal != 0) {
        char msg[256];
        if (!flagsResolved)
            snprintf(msg, sizeof(msg), "0x%llX is not a live UFunction -- not invoked (its parameters "
                     "cannot be sized)", (unsigned long long)ufuncAddr);
        else
            snprintf(msg, sizeof(msg), "'%s' needs %u bytes of parameters (where its parameter chain ends), "
                     "the mailbox holds %zu -- not invoked", fi.name.c_str(), needBytes,
                     sizeof(Box().paramsData));
        SetError(refusal, msg);
        return;
    }
    // [VND584-06] CMD_INVOKE takes ufuncAddr on trust: a CE form resolves it once and FIRE sends it back later, and a
    // Blueprint class reloaded in between can leave that address holding another class's UFunction -- which still
    // passes ResolveFunctionInfo's meta-class check. A static function ignores `this`, so any instance may call it.
    if (!(fi.functionFlags & FUNC_FLAG_STATIC)) {
        const uintptr_t owner = Ubel::GetOuter(ufuncAddr);
        const uintptr_t cls   = Ubel::GetClass(instanceAddr);
        // [VND584-04] An interface's function belongs to every class that implements the interface.
        Ubel::ImplementedInterface viaInterface;
        const bool owned = Ubel::IsClassInChain(cls, owner, ChainReadSuper)
            || Ubel::FindImplementedInterface(cls, owner, false, ChainReadSuper, ChainListInterfaces, viaInterface);
        if (!owned) {
            char msg[256];
            snprintf(msg, sizeof(msg), "'%s' (0x%llX) is not a function of this instance's class -- it may have been "
                     "reloaded; re-open the invoke form so it finds the function again", fi.name.c_str(),
                     (unsigned long long)ufuncAddr);
            SetError(MB_ERR_INVOKE_NOT_OWNED, msg);
            return;
        }
    }

    const bool isStaticNative = ShouldRouteDirectInvoke(fi.functionFlags, flagsResolved);

    if (fi.functionFlags != Box().functionFlags) {
        // Made greppable: the mailbox field disagrees with the function actually being
        // invoked, and the route follows the re-read value, never the mailbox's.
        LOG_WARN("Mailbox: INVOKE mailbox functionFlags=0x%08X is STALE — '%s' "
                 "really has 0x%08X; routing on the re-read value (MB1)",
                 Box().functionFlags, fi.name.c_str(), fi.functionFlags);
    }

    // Clear the ReturnValue slot before dispatching, so the After dump cannot be
    // mistaken for a result.
    //
    // WHY: paramsData is a PERSISTENT global. Whatever the caller wrote for the INPUTS
    // is still sitting in it, so an unwritten return slot can hold a copy of an input. A
    // slot that MIRRORS THE INPUT makes "the function ran and wrote this" indistinguishable
    // from "the function never executed". [B636-NOACCIDENT-2026-08-23]
    // PointerPanelViewModel's own docstring states the same hazard.
    //
    // ZERO rather than a 0xCD-style sentinel, deliberately: a struct return that the
    // callee only partially fills reads sanely when the remainder is zero and reads as
    // garbage when it is 0xCD, and zeroing is what UE itself does to a param buffer
    // before ProcessEvent.
    //
    // ⚠ RESIDUAL, stated so nobody over-trusts this: a function that legitimately
    // returns 0 is STILL indistinguishable by slot contents alone. The slot answers
    // "was it written", not "did it run" -- for that, read the ProcessEvent return code
    // and the route line logged just below. Pick a fixture with a non-zero expected
    // return when the question is whether the call happened.
    //
    // The slot comes from the function's own chain [FUNCPARM-CONSUMERS]: fi is
    // ResolveFunctionInfo's, which reads the tail only, so its params are empty.
    int32_t retOff = -1, retSize = 0;
    if (flagsResolved && fi.returnValueOffset != 0xFFFF && Ubel::ReadReturnSlot(ufuncAddr, retOff, retSize)) {
        const size_t off = static_cast<size_t>(retOff);
        const size_t len = static_cast<size_t>(retSize);
        // Bounds are checked against the real buffer, not against ParmsSize: a
        // forked layout can report a ParmsSize larger than the mailbox slab, and a
        // memset past the end would corrupt whatever follows it in the struct.
        if (off + len > sizeof(Box().paramsData)) {
            LOG_WARN("Mailbox: INVOKE return slot +%zu size %zu exceeds paramsData "
                     "(%zu) — not clearing", off, len,
                     sizeof(Box().paramsData));
        } else {
            memset(Box().paramsData + off, 0, len);
            LOG_DEBUG("Mailbox: INVOKE cleared ReturnValue slot +%zu (%zu bytes)", off, len);
        }
    }

    {
        std::string objWhy;
        if (!FrierenInvoke::PrepareObjectParams(ufuncAddr, Box().paramsData,
                                                sizeof(Box().paramsData), isStaticNative, objWhy)) {
            SetError(MB_ERR_INVOKE_OBJECT_PARAM, ("Not invoked: " + objWhy).c_str());
            return;
        }
    }

    // [INVOKE-NONSTATIC-ON-CDO] After the last refusal, before the call (which may destroy the object): a function that
    // is not static on a template may do nothing there. Warned through cmdOutFlags (contract 7), never refused.
    {
        const Aura::TemplateInfo tpl = Aura::TemplateOf(instanceAddr);
        if (WarnNonStaticOnTemplate(tpl.kind != Aura::TemplateKind::None, flagsResolved, fi.functionFlags)) {
            Box().cmdOutFlags |= INV_OUT_NONSTATIC_ON_TEMPLATE;
            LOG_WARN("Mailbox: INVOKE '%s' is not static and 0x%llX is a %s -- it may do nothing there",
                     fi.name.c_str(), (unsigned long long)instanceAddr, Aura::TemplateKindWire(tpl.kind));
        }
    }

    int32_t result;
    if (isStaticNative) {
        LOG_INFO("Mailbox: INVOKE -> static-native fast path "
                 "(flags=0x%08X, bypassing GameThreadDispatch)",
                 fi.functionFlags);
        result = UE5_CallProcessEventDirect(
            instanceAddr, ufuncAddr,
            reinterpret_cast<uintptr_t>(Box().paramsData));
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
            reinterpret_cast<uintptr_t>(Box().paramsData),
            static_cast<uint32_t>(sizeof(Box().paramsData)));
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
        strncpy(Box().errorMsg, msg, sizeof(Box().errorMsg) - 1);
        Box().errorMsg[sizeof(Box().errorMsg) - 1] = '\0';
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
    if (Box().result != 0) return;

    HandleFindFunction();
    if (Box().result != 0) return;

    HandleInvokeHeld();

    LOG_INFO("Mailbox: INVOKE_BY_NAME complete, result=%d", Box().result);
}

// [MCP-WRITE-GRANTS-2026-11-14] CMD_INVOKE and INVOKE_BY_NAME. A record that runs the debug camera's own functions (a
// Console row baked into a CE record) changes the camera as CMD_SET_DEBUG_CAMERA does: under the hold's lock, and CE's
// afterwards. Unrecorded, the camera it turned on stayed named for whoever held it before -- an AI session, whose end
// then turned it off. The function is named by the UFunction itself, as the pipe's invoke_function names it. It never
// waits for the lock (TryChangeHoldAsCe): a pipe writer holds it across its own game-thread wait, and this poller is the
// mailbox's only one.
static void HandleInvokeHeld() {
    char name[128] = {};
    const uintptr_t ufuncAddr = Box().ufuncAddr;
    if (ufuncAddr && UE5_GetObjectName(ufuncAddr, name, static_cast<int32_t>(sizeof(name)))
        && Voll::IsDebugCameraFunction(name)) {
        if (!TryChangeHoldAsCe(Grimoire::HOLD_DEBUG_CAMERA, [] { HandleInvoke(); }))
            LOG_INFO("Mailbox: %s ran unrecorded -- another writer was changing the debug camera", name);
        return;
    }
    HandleInvoke();
}

static void HandleListFunctions() {
    // Resolve instance: use instanceAddr if set, otherwise find by className
    uintptr_t instanceAddr = Box().instanceAddr;

    if (!instanceAddr) {
        char className[256];
        memcpy(className, Box().className, sizeof(className));
        className[255] = '\0';

        if (className[0] == '\0') {
            SetError(-1, "No instance address or class name provided");
            return;
        }

        LOG_INFO("Mailbox: LIST_FUNCTIONS finding instance of '%s'...", className);

        // [FINDINSTANCE-SUBSTRING-USERFACING] FIND_INSTANCE's resolver. The list is the picked object's class's own
        // functions, so a base-class name lists the subclass the pick landed on.
        const Aura::UserInstance found = Aura::ResolveUserInstance(className);
        instanceAddr = found.obj;
        if (!instanceAddr) {
            char msg[256];
            if (found.pick == Aura::UserPick::Cancelled)
                snprintf(msg, sizeof(msg), "Instance lookup for '%s' was cut short: the DLL is shutting down", className);
            else
                snprintf(msg, sizeof(msg), "No instance of '%s' found", className);
            SetError(-2, msg);
            return;
        }
        Box().instanceAddr = instanceAddr;
    }

    // Get page index from params_data[0..3]
    uint32_t pageIndex = 0;
    memcpy(&pageIndex, Box().paramsData, sizeof(uint32_t));

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
    Box().parmsSize = static_cast<uint16_t>(totalCount);
    Box().numParms = static_cast<uint16_t>(returnedCount);
    Box().functionFlags = totalPages;

    // Zero out params_data
    memset(Box().paramsData, 0, sizeof(Box().paramsData));

    // Write entries: each 64 bytes
    for (uint32_t i = 0; i < returnedCount; ++i) {
        const auto& f = funcs[startIdx + i];
        uint8_t* entry = Box().paramsData + (i * ENTRY_SIZE);

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

// Enumerate all live instances of a class -- never a template -- for the property-freeze
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
    memcpy(className, Box().className, sizeof(className));
    className[255] = '\0';

    // Read the scope flag and CLEAR it in the same breath, BEFORE any early return.
    //
    // cmdFlags is the one input that would otherwise survive between commands
    // (className and paramsData are both rewritten by the next caller), so a
    // derived request left standing would silently widen the pool of whoever asks
    // next — including a contract-1/2 script that never writes the field and is
    // entitled to exact-match behaviour. Clearing here is what makes the flag
    // genuinely additive rather than sticky.
    const uint32_t inFlags = Box().cmdFlags;
    Box().cmdFlags = 0;
    const bool derived = (inFlags & LI_IN_DERIVED) != 0;

    // Rewritten, never accumulated: a stale truncation bit from a previous page
    // would tell the caller its pool was capped when it was not.
    Box().cmdOutFlags = 0;

    if (className[0] == '\0') {
        SetError(-1, "Empty class name");
        return;
    }

    // Read page index from paramsData[0..3] BEFORE we overwrite the buffer.
    uint32_t pageIndex = 0;
    memcpy(&pageIndex, Box().paramsData, sizeof(uint32_t));

    LOG_INFO("Mailbox: LIST_INSTANCES class='%s' page=%u scope=%s",
             className, pageIndex, derived ? "derived" : "exact");

    // Caps live in Mimic.h beside the page geometry they have to agree with.
    const int cap = derived ? LIST_INSTANCES_MAX_DERIVED : LIST_INSTANCES_MAX_EXACT;
    // [LISTINSTANCES-KEEPS-TEMPLATES] Both walks skip templates (TemplateOf) INSIDE the walk, before the cap: they are
    // built at class load and sit at the low indices a walk reaches first, so filtering only afterwards could return
    // a page of nothing but templates.
    Aura::SearchResultSet rset = derived
        ? Aura::FindInstancesDerivedFrom(className, cap)
        : Aura::FindInstancesByClass(className, /*exactMatch=*/true, /*maxResults=*/cap, /*newestFirst=*/false,
                                     /*nameFilter=*/"", /*excludeClasses=*/{}, /*buildHistogram=*/false, /*skipTemplates=*/true);

    // Every entry published here is an object the freeze helper WRITES to, and never restores: a template among them
    // -- a class default object, one of its default subobjects, a Blueprint component template -- would pass the
    // frozen value to every later spawn. The walks already passed them over; this states the rule where the pool is
    // published, so it does not rest on either walk alone.
    struct LiveEntry { uint64_t obj; uint64_t cls; };
    std::vector<LiveEntry> live;
    live.reserve(rset.results.size());
    Aura::TemplateOuterMemo outerMemo;
    for (const auto& r : rset.results) {
        if (!r.addr || Aura::TemplateOf(r.addr, outerMemo).kind != Aura::TemplateKind::None) continue;
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
    Box().parmsSize = static_cast<uint16_t>(totalCount > 0xFFFFu ? 0xFFFFu : totalCount);
    Box().numParms = static_cast<uint16_t>(returnedCount);
    Box().functionFlags = totalPages;

    // "The pool was capped" comes from the walk that did the capping, not from a
    // second test over the page — that is audit #4's own named root cause (report
    // and reality computed by different code paths). A derived sweep off a broad
    // base reaches this routinely, so the caller must be TOLD rather than left to
    // read a truthful-but-partial count as a total.
    if (rset.truncated || rset.aborted)
        Box().cmdOutFlags |= LI_OUT_TRUNCATED;

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
    Box().instanceAddr = 0;
    Box().ufuncAddr    = 0;
    if (derived) {
        // The offset is a property of the ENGINE, not of the page, so it is
        // published whenever the scope needs it — the caller treats a zero here as
        // a hard error rather than as a reason to write unguarded.
        Box().ufuncAddr = static_cast<uint64_t>(Grimoire::OFF_UOBJECT_CLASS);
    } else if (returnedCount > 0) {
        uintptr_t cls = Ubel::GetClass(static_cast<uintptr_t>(live[startIdx].obj));
        if (cls) {
            Box().instanceAddr = static_cast<uint64_t>(cls);
            Box().ufuncAddr    = static_cast<uint64_t>(Grimoire::OFF_UOBJECT_CLASS);
        }
    }

    memset(Box().paramsData, 0, sizeof(Box().paramsData));

    for (uint32_t i = 0; i < returnedCount; ++i) {
        const LiveEntry& e = live[startIdx + i];
        uint8_t* slot = Box().paramsData + (i * entrySize);
        memcpy(slot, &e.obj, sizeof(uint64_t));
        if (derived) memcpy(slot + sizeof(uint64_t), &e.cls, sizeof(uint64_t));
    }

    LOG_INFO("Mailbox: LIST_INSTANCES returned %u/%u (page %u/%u) scope=%s%s classWitness=0x%llX",
             returnedCount, totalCount, pageIndex + 1, totalPages,
             derived ? "derived" : "exact",
             (Box().cmdOutFlags & LI_OUT_TRUNCATED) ? " CAPPED" : "",
             (unsigned long long)Box().instanceAddr);
    SetDone(0);
}

// ---- Helpers ----

// CMD_SET_DEBUG_CAMERA: robust Debug Camera force on/off, shared with the UI
// pipe (set_debug_camera). instanceAddr carries the request: 0=OFF, 1=ON,
// 2=query (read state, no change). result gets the resulting state (1/0/-1), -5 when the toggle is QUEUED (errorMsg
// says what to do, which depends on the direction), -6 when an opposite toggle is still queued, or -17 for an ON
// with no live CheatManager.
// Delegates entirely to the Frieren exports, which own the toggle +
// controller-swap fallback.
// [PIPE-PER-PROCESS-2026-11-06] Stage D2-1: a hold CE changes is changed under the ledger lock the pipe's writers take
// too, and is CE's afterwards (Voll::HoldLedger). An OFF leaves CE named on a hold that is off, which reads as free.
// Called with the hold's lock held.
static void RecordCeChange(const std::string& hold) {
    // [D2-3-INVOKE] A debug-camera ON still pending is its sender's, as the pipe's hold recorder has it: CE's answer
    // that sent nothing (a toggle already pending, the other way) never takes another client's.
    uint64_t sender = 0;
    if (hold == Grimoire::HOLD_DEBUG_CAMERA && UE5_GetDebugCameraState() != 1
        && FrierenDebugCamera::PendingOn(sender) && sender != Voll::kHoldOwnerCe)
        return;
    Voll::Holds().SetOwner(hold, Voll::kHoldOwnerCe);
}

template <typename Change>
static int32_t ChangeHoldAsCe(const std::string& hold, Change&& change) {
    std::lock_guard<std::mutex> lock(Voll::Holds().LockFor(hold));
    const int32_t rc = change();
    RecordCeChange(hold);
    return rc;
}

// As ChangeHoldAsCe, but never waits for the lock: busy, the change runs unrecorded. Returns whether it was recorded.
template <typename Change>
static bool TryChangeHoldAsCe(const std::string& hold, Change&& change) {
    std::unique_lock<std::mutex> lock(Voll::Holds().LockFor(hold), std::try_to_lock);
    if (!lock.owns_lock()) {
        change();
        return false;
    }
    change();
    RecordCeChange(hold);
    return true;
}

static void HandleSetDebugCamera() {
    uint64_t req = Box().instanceAddr;
    int32_t state = (req == 2)
        ? UE5_GetDebugCameraState()
        : ChangeHoldAsCe(Grimoire::HOLD_DEBUG_CAMERA, [&] { return UE5_SetDebugCamera(req != 0 ? 1 : 0); });
    // [DEBUGCAM-FORCEON-CDO] No toggle reaches the CheatManager's class default object (an ON there is -17), so this
    // command leaves cmdOutFlags as the dispatcher cleared it.
    if (state == -1) {
        // -1 is a state that did not read or a toggle that failed (-4 faulted inside it). Never the no-live-CheatManager
        // case: its read lands on the class default object and answers 0 or 1, and an ON there answers -17.
        strncpy(Box().errorMsg,
                "Debug Camera: unreadable state, or the toggle failed (see the DLL log)",
                sizeof(Box().errorMsg) - 1);
        Box().errorMsg[sizeof(Box().errorMsg) - 1] = '\0';
    } else if (state == DBGCAM_ERR_NO_LIVE_CHEATMANAGER) {
        // [DEBUGCAM-FORCEON-CDO] A new negative code, only for an ON: every [ENABLE] reports it (Mimic.h, WHAT IS NOT
        // A CHANGE AT ALL).
        strncpy(Box().errorMsg,
                "Debug Camera: ON not sent -- no live CheatManager (UE 5 leaves it out of Shipping by default); "
                "ToggleDebugCamera does nothing on its class default object",
                sizeof(Box().errorMsg) - 1);
        Box().errorMsg[sizeof(Box().errorMsg) - 1] = '\0';
    } else if (state == Stark::kInvokeTimedOutStillQueued) {
        // [W3-DEBUGCAM-QUEUED] The toggle timed out on the game thread and STAYS QUEUED. Not a contract change (MB3
        // above: a new negative result code; every script treats non-zero as failure and renders errorMsg verbatim) --
        // so errorMsg carries what to do. A queued ON will run, and a second one is not wanted. A queued OFF drains on
        // the original controller's CheatManager and leaves the camera on (stock UE 4.18-5.7), so the user sends it
        // again [DEBUGCAM-QUEUED-OFF-NOESCALATE].
        const char* queued = (req == 0)
            ? "Debug Camera: OFF did not complete -- the game thread is busy; send OFF again once the game responds"
            : "Debug Camera: ON queued -- it will run when the game thread is free; do not re-send";
        strncpy(Box().errorMsg, queued, sizeof(Box().errorMsg) - 1);
        Box().errorMsg[sizeof(Box().errorMsg) - 1] = '\0';
    } else if (state == Stark::kToggleBlockedByQueued) {
        // [DBGCAM-OFF-OVER-QUEUED-ON] An earlier toggle of the OTHER direction is still queued: this one sent nothing,
        // and the state it would have read predates that toggle. A new negative code, the same MB3 reasoning as -5.
        const char* blocked = (req == 0)
            ? "Debug Camera: OFF not sent -- an earlier ON is still queued; send OFF again once the game responds"
            : "Debug Camera: ON not sent -- an earlier OFF is still queued; send ON again once the game responds";
        strncpy(Box().errorMsg, blocked, sizeof(Box().errorMsg) - 1);
        Box().errorMsg[sizeof(Box().errorMsg) - 1] = '\0';
    }
    LOG_INFO("Mailbox: SET_DEBUG_CAMERA req=%llu -> state=%d",
             (unsigned long long)req, state);
    SetDone(state);
}

// CMD_OFFSETS_VERDICT: were the DynOff offsets MEASURED? [W5-OFFSETS-MAILBOX]
// The pipe (get_offsets) and the C ABI (UE5_GetOffsetsVerdict) both carry this verdict; the mailbox --
// the path a CE script takes when executeCodeEx is unavailable -- did not, while ue5_dissect.lua builds
// CE structures out of those very offsets. No input. result = 1 measured / 0 not; paramsData[0..127]
// carries the reason ("" when measured, "probe-not-run" before any detection).
// Init-EXEMPT (Mimic.h CommandRequiresInit): "no probe has run" is an ANSWER here, not a failure.
static void HandleOffsetsVerdict() {
    memset(Box().paramsData, 0, sizeof(Box().paramsData));
    char reason[128] = {};
    const int32_t measured = UE5_GetOffsetsVerdict(reason, static_cast<int32_t>(sizeof(reason)));
    memcpy(Box().paramsData, reason, sizeof(reason));
    LOG_INFO("Mailbox: OFFSETS_VERDICT -> measured=%d reason='%s'", measured, reason);
    SetDone(measured);
}

// CMD_TELEPORT: marker save/recall + cursor teleport (Wirbel). Field usage
// documented in Mimic.h (TeleportOp enum + CMD_TELEPORT comment block) and
// docs/teleport-spec.md §8. Delegates entirely to Wirbel — this handler only
// marshals the mailbox fields.
static void HandleTeleport() {
    const uint64_t op  = Box().instanceAddr;
    const int32_t slot = static_cast<int32_t>(Box().ufuncAddr);

    // Pose block writer: [0..47] 6 doubles, [48..175] mapName,
    // [176] source, [177] tier, [178] pose flags (contract 4: bit0 parent-relative,
    // bit1 landing unknown). Zeroes the whole buffer first.
    auto writePoseBlock = [](const Wirbel::Pose& p, const char* mapName,
                             uint8_t source, uint8_t tier, uint8_t flags = 0) {
        memset(Box().paramsData, 0, sizeof(Box().paramsData));
        double v[6] = { p.X, p.Y, p.Z, p.Pitch, p.Yaw, p.Roll };
        memcpy(Box().paramsData, v, sizeof(v));
        if (mapName) {
            size_t cap = static_cast<size_t>(Grimoire::TELEPORT_MAPNAME_CAP);
            size_t n = strnlen(mapName, cap - 1);
            memcpy(Box().paramsData + 48, mapName, n);
            Box().paramsData[48 + n] = '\0';
        }
        Box().paramsData[176] = source;
        Box().paramsData[177] = tier;
        Box().paramsData[178] = flags;   // [W2-MARKER-PARENTREL] / [W2-TPREL-TRANSPORTS]
    };

    int32_t rc;
    switch (op) {
    case TP_OP_GET_POSE: {
        Wirbel::Pose p{};
        char map[Grimoire::TELEPORT_MAPNAME_CAP] = {};
        uint8_t source = 0;
        bool parentRel = false;   // [W2-MARKER-PARENTREL]
        rc = Wirbel::GetPose(p, map, sizeof(map), &source, &parentRel);
        if (rc == 0) writePoseBlock(p, map, source, 0, parentRel ? 0x01 : 0);
        break;
    }
    case TP_OP_SAVE: {
        rc = Wirbel::SaveMarker(slot);
        if (rc == 0) {
            Wirbel::Marker m{};
            if (Wirbel::GetMarker(slot, m) == 0)
                writePoseBlock(m.P, m.MapName, 0, 0, m.ParentRelative ? 0x01 : 0);
        }
        break;
    }
    case TP_OP_RECALL:
    case TP_OP_RECALL_FORCE: {
        uint8_t tier = 0;
        rc = Wirbel::RecallMarker(slot, op == TP_OP_RECALL_FORCE, &tier);
        memset(Box().paramsData, 0, sizeof(Box().paramsData));
        Box().paramsData[177] = tier;
        break;
    }
    case TP_OP_CURSOR: {
        // Read inputs BEFORE outputs overwrite the shared buffer.
        double zOffset = Grimoire::TELEPORT_DEFAULT_ZOFFSET;
        memcpy(&zOffset, Box().paramsData, sizeof(double));
        uint8_t channel = Box().paramsData[8];
        bool fallbackCenter = Box().paramsData[9] != 0;

        Wirbel::Pose hit{};
        uint8_t tier = 0;
        bool usedCenter = false;
        rc = Wirbel::TeleportToCursor(zOffset, channel, fallbackCenter,
                                      &hit, &tier, &usedCenter);
        memset(Box().paramsData, 0, sizeof(Box().paramsData));
        if (rc == 0) {
            double v[3] = { hit.X, hit.Y, hit.Z };
            memcpy(Box().paramsData, v, sizeof(v));
        }
        Box().paramsData[177] = tier;
        Box().paramsData[178] = usedCenter ? 1 : 0;
        break;
    }
    case TP_OP_GET_MARKER: {
        Wirbel::Marker m{};
        rc = Wirbel::GetMarker(slot, m);
        if (rc == 0) writePoseBlock(m.P, m.MapName, 0, 0, m.ParentRelative ? 0x01 : 0);
        break;
    }
    case TP_OP_CLEAR_MARKER:
        rc = Wirbel::ClearMarker(slot);
        break;
    case TP_OP_RECALL_LAST: {
        uint8_t tier = 0;
        rc = Wirbel::RecallLast(&tier);
        memset(Box().paramsData, 0, sizeof(Box().paramsData));
        Box().paramsData[177] = tier;
        break;
    }
    case TP_OP_GET_LAST: {
        Wirbel::Marker m{};
        rc = Wirbel::GetLast(m);
        if (rc == 0) writePoseBlock(m.P, m.MapName, 0, 0, m.ParentRelative ? 0x01 : 0);
        break;
    }
    case TP_OP_BUGIT_SAVE: {
        Wirbel::Pose p{};
        char map[Grimoire::TELEPORT_MAPNAME_CAP] = {};
        uint8_t source = 0;
        bool parentRel = false;   // [W2-MARKER-PARENTREL]
        rc = Wirbel::BugItSave(p, map, sizeof(map), &source, &parentRel);
        if (rc == 0) writePoseBlock(p, map, source, 0, parentRel ? 0x01 : 0);
        break;
    }
    case TP_OP_BUGIT_GO: {
        uint8_t tier = 0;
        rc = Wirbel::BugItGo(&tier);
        memset(Box().paramsData, 0, sizeof(Box().paramsData));
        Box().paramsData[177] = tier;
        break;
    }
    case TP_OP_GET_POV: {
        Wirbel::Pov pov{};
        rc = Wirbel::GetPov(pov);
        memset(Box().paramsData, 0, sizeof(Box().paramsData));
        if (rc == 0) {
            double cam[6] = { pov.Cam.X, pov.Cam.Y, pov.Cam.Z,
                              pov.Cam.Pitch, pov.Cam.Yaw, pov.Cam.Roll };
            memcpy(Box().paramsData, cam, sizeof(cam));
            memcpy(Box().paramsData + 48, &pov.Fov, sizeof(double));
            double pawn[3] = { pov.Pawn.X, pov.Pawn.Y, pov.Pawn.Z };
            memcpy(Box().paramsData + 56, pawn, sizeof(pawn));
            Box().paramsData[80] = pov.HasPawn ? 1 : 0;
            Box().paramsData[81] = pov.Source;
        }
        break;
    }
    case TP_OP_RELATIVE: {
        // Read inputs BEFORE outputs overwrite the shared buffer.
        double distance = 0;
        memcpy(&distance, Box().paramsData, sizeof(double));
        bool horizontalOnly = Box().paramsData[8] == 0;  // mode 0 = horizontal
        Wirbel::Pose p{};
        uint8_t tier = 0;
        bool landingKnown = true;   // [W2-TPREL-TRANSPORTS]
        rc = Wirbel::TeleportRelative(distance, horizontalOnly, p, &tier, &landingKnown);
        if (rc == 0 && !landingKnown) {
            // The move succeeded but its re-read failed: publish NaN (nobody measured it) and say so --
            // never the zero-initialised Pose, a landing at the world origin. [TPREL-ZEROPOSE-2026-09-10]
            const uint64_t nanBits = 0x7FF8000000000000ull;
            double nan = 0;
            memcpy(&nan, &nanBits, sizeof(nan));
            p.X = p.Y = p.Z = p.Pitch = p.Yaw = p.Roll = nan;
        }
        if (rc == 0) writePoseBlock(p, nullptr, 0, tier, landingKnown ? 0 : 0x02);
        break;
    }
    case TP_OP_EXPLICIT: {
        // Read inputs BEFORE outputs overwrite the shared buffer.
        double v[6] = {};
        memcpy(v, Box().paramsData, sizeof(v));
        bool hasRot = Box().paramsData[48] != 0;
        Wirbel::Pose p{};
        p.X = v[0]; p.Y = v[1]; p.Z = v[2];
        p.Pitch = v[3]; p.Yaw = v[4]; p.Roll = v[5];
        uint8_t tier = 0;
        rc = Wirbel::RecallExplicit(p, hasRot, &tier);
        memset(Box().paramsData, 0, sizeof(Box().paramsData));
        Box().paramsData[177] = tier;
        break;
    }
    case TP_OP_SET_CURSOR: {
        bool state = false;
        rc = Wirbel::SetMouseCursor(slot != 0, &state);   // ufuncAddr(slot) = 1/0
        memset(Box().paramsData, 0, sizeof(Box().paramsData));
        Box().paramsData[0] = state ? 1 : 0;
        break;
    }
    case TP_OP_GET_CURSOR: {
        bool state = false;
        rc = Wirbel::GetMouseCursor(&state);
        memset(Box().paramsData, 0, sizeof(Box().paramsData));
        if (rc == 0) Box().paramsData[0] = state ? 1 : 0;
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
        strncpy(Box().errorMsg, msg, sizeof(Box().errorMsg) - 1);
        Box().errorMsg[sizeof(Box().errorMsg) - 1] = '\0';
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
    const uint64_t op = Box().instanceAddr;
    int32_t rc;
    switch (op) {
    case PROTECT_OP_SET_GODMODE:
        rc = ChangeHoldAsCe(Grimoire::HOLD_GOD_MODE,
                            [] { return UE5_SetGodMode(Box().ufuncAddr != 0 ? 1 : 0); });
        break;
    case PROTECT_OP_GET_GODMODE:
        rc = UE5_GetGodMode();
        break;
    case PROTECT_OP_GET_STATE: {
        int32_t want = 0, live = -1, resolvable = 0;
        rc = UE5_GetProtectState(&want, &live, &resolvable);
        memset(Box().paramsData, 0, sizeof(Box().paramsData));
        Box().paramsData[0] = static_cast<uint8_t>(want ? 1 : 0);
        Box().paramsData[1] = (live < 0)
            ? 0xFF : static_cast<uint8_t>(live ? 1 : 0);
        Box().paramsData[2] = static_cast<uint8_t>(resolvable ? 1 : 0);
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
        strncpy(Box().errorMsg, msg, sizeof(Box().errorMsg) - 1);
        Box().errorMsg[sizeof(Box().errorMsg) - 1] = '\0';
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
    const uint64_t op = Box().instanceAddr;
    int32_t rc;
    switch (op) {
    case FG_OP_SET:
        rc = ChangeHoldAsCe(Grimoire::HOLD_FOREGROUND_LOCK,
                            [] { return Grausam::SetForegroundLock(Box().ufuncAddr != 0); });
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
// handler just flips the flag on the mailbox polling thread. CE is one owner of
// it ([PIPE-PER-PROCESS-2026-11-06] Stage C): a pipe client that leaves no longer
// turns off what CE's record turned on. Nothing in the mailbox changed, so the
// contract version did not either.
static void HandleSeeThrough() {
    if (Box().instanceAddr >= 1)
        Schlacht::SetPierceCount(static_cast<int32_t>(Box().instanceAddr));
    int32_t rc = Schlacht::SetEnabled(Box().ufuncAddr != 0, Schlacht::kOwnerCe);
    LOG_INFO("Mailbox: SEETHROUGH count=%llu value=%llu -> rc=%d",
             (unsigned long long)Box().instanceAddr,
             (unsigned long long)Box().ufuncAddr, rc);
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
    const uint64_t op = Box().instanceAddr;
    memset(Box().paramsData, 0, sizeof(Box().paramsData));

    switch (op) {
    case QUERY_OP_GWORLD: {
        // g_cachedGWorld is &GWorld (the address of the global UWorld* pointer);
        // deref once for the live UWorld*. A recovery slot is a live heap field
        // that still derefs to the current world (docs: RecoverGWorldViaEngine).
        uintptr_t slot  = g_cachedGWorld;
        uintptr_t world = 0;
        if (slot) Macht::ReadSafe(slot, world);
        uint64_t out[2] = { static_cast<uint64_t>(slot), static_cast<uint64_t>(world) };
        memcpy(Box().paramsData, out, sizeof(out));
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
        memcpy(Box().paramsData, out, sizeof(out));
        if (!info.className.empty()) {
            // paramsData[16..143] — 128-byte class-name field (leave a null).
            size_t n = (std::min)(info.className.size(), static_cast<size_t>(127));
            memcpy(Box().paramsData + 16, info.className.c_str(), n);
            Box().paramsData[16 + n] = '\0';
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
        memcpy(Box().paramsData, out, sizeof(out));
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
    const uint64_t knobId = Box().instanceAddr;
    int32_t rc;
    if (knobId == 3) {
        // Gravity direction (UE5.3+): 3 doubles x/y/z in paramsData. (0,0,0) = off.
        double v[3] = {};
        memcpy(v, Box().paramsData, sizeof(v));
        rc = ChangeHoldAsCe(Grimoire::HOLD_GRAVITY_DIRECTION, [&] { return UE5_SetGravityDirection(v[0], v[1], v[2]); });
        LOG_INFO("Mailbox: MOVEMENT gravdir -> (%.3f, %.3f, %.3f) rc=%d",
                 v[0], v[1], v[2], rc);
    } else {
        double percent = 0.0;
        memcpy(&percent, Box().paramsData, sizeof(double));
        static const char* const kKnobs[] = {"walk_speed", "gravity", "jump"};   // Laufen's KNOB_* order
        if (knobId < 3)
            rc = ChangeHoldAsCe(std::string(Grimoire::HOLD_MOVEMENT_PREFIX) + kKnobs[knobId],
                                [&] { return UE5_SetMovementPercent(static_cast<int32_t>(knobId), percent); });
        else
            rc = UE5_SetMovementPercent(static_cast<int32_t>(knobId), percent);   // refused by the module
        LOG_INFO("Mailbox: MOVEMENT knob=%llu pct=%.1f -> rc=%d",
                 (unsigned long long)knobId, percent, rc);
    }
    if (rc < 0) {
        char msg[128];
        snprintf(msg, sizeof(msg), "Movement: knob=%llu failed code=%d",
                 (unsigned long long)knobId, rc);
        strncpy(Box().errorMsg, msg, sizeof(Box().errorMsg) - 1);
        Box().errorMsg[sizeof(Box().errorMsg) - 1] = '\0';
    }
    SetDone(rc);
}

// CMD_TIME (Hemmung): hold a reflected dilation float at an absolute value, or
// reset it. instanceAddr = op (TimeOp); ufuncAddr = target (0 global / 1 pawn);
// paramsData[0..7] = double value for SET (read BEFORE SetDone). Returns
// 1 (active) / 0 (off) / negative Hemmung::TimeResult.
static void HandleTime() {
    const uint64_t op = Box().instanceAddr;
    const int32_t  target = static_cast<int32_t>(Box().ufuncAddr);
    // Hemmung's DIL_GLOBAL / DIL_PAWN; any other target is the module's to refuse, and names no hold.
    const std::string hold = (target == 0 || target == 1)
        ? std::string(Grimoire::HOLD_TIME_PREFIX) + (target == 0 ? "global" : "pawn") : std::string();
    auto asCe = [&](auto&& change) { return hold.empty() ? change() : ChangeHoldAsCe(hold, change); };
    int32_t rc;
    switch (op) {
    case TIME_OP_SET: {
        double value = 0.0;
        memcpy(&value, Box().paramsData, sizeof(double));
        rc = asCe([&] { return UE5_SetTimeDilation(target, value); });
        LOG_INFO("Mailbox: TIME set target=%d value=%.4f -> rc=%d", target, value, rc);
        break;
    }
    case TIME_OP_RESET:
        rc = asCe([&] { return UE5_ResetTimeDilation(target); });
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
        strncpy(Box().errorMsg, msg, sizeof(Box().errorMsg) - 1);
        Box().errorMsg[sizeof(Box().errorMsg) - 1] = '\0';
    }
    SetDone(rc);
}

// CMD_FLY (Dunste): toggle no-gravity flight + set speed/preset, shared with the
// UI pipe (fly_set). instanceAddr = op (FlyOp); ufuncAddr / paramsData carry the
// operand (read BEFORE SetDone). GET_STATE writes the live status into paramsData.
static void HandleFly() {
    const uint64_t op = Box().instanceAddr;
    int32_t rc;
    switch (op) {
    case FLY_OP_SET_ENABLED:
        rc = ChangeHoldAsCe(Grimoire::HOLD_FLY,
                            [] { return UE5_SetFly(static_cast<int32_t>(Box().ufuncAddr) != 0 ? 1 : 0); });
        LOG_INFO("Mailbox: FLY enable=%llu -> rc=%d",
                 (unsigned long long)Box().ufuncAddr, rc);
        break;
    case FLY_OP_SET_SPEED: {
        double speed = 0.0;
        memcpy(&speed, Box().paramsData, sizeof(double));
        rc = ChangeHoldAsCe(Grimoire::HOLD_FLY, [&] { return UE5_SetFlySpeed(speed); });
        LOG_INFO("Mailbox: FLY speed=%.0f -> rc=%d", speed, rc);
        break;
    }
    case FLY_OP_SET_PRESET:
        rc = ChangeHoldAsCe(Grimoire::HOLD_FLY,
                            [] { return UE5_SetFlyPreset(static_cast<int32_t>(Box().ufuncAddr)); });
        LOG_INFO("Mailbox: FLY preset=%llu -> rc=%d",
                 (unsigned long long)Box().ufuncAddr, rc);
        break;
    case FLY_OP_SET_NOCLIP:
        rc = ChangeHoldAsCe(Grimoire::HOLD_FLY,
                            [] { return UE5_SetFlyNoclip(static_cast<int32_t>(Box().ufuncAddr) != 0 ? 1 : 0); });
        LOG_INFO("Mailbox: FLY noclip=%llu -> rc=%d",
                 (unsigned long long)Box().ufuncAddr, rc);
        break;
    case FLY_OP_GET_STATE: {
        Dunste::FlyStatus st{};
        rc = Dunste::GetStatus(st);
        Box().paramsData[0] = st.active ? 1 : 0;
        Box().paramsData[1] = static_cast<uint8_t>(st.preset);
        Box().paramsData[2] = st.noclip ? 1 : 0;
        memcpy(Box().paramsData + 8, &st.speed, sizeof(double));
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
        strncpy(Box().errorMsg, msg, sizeof(Box().errorMsg) - 1);
        Box().errorMsg[sizeof(Box().errorMsg) - 1] = '\0';
    }
    SetDone(rc);
}

static void SetError(int32_t code, const char* msg) {
    Box().result = code;
    if (msg) {
        strncpy(Box().errorMsg, msg, sizeof(Box().errorMsg) - 1);
        Box().errorMsg[sizeof(Box().errorMsg) - 1] = '\0';
    }
    LOG_WARN("Mailbox: error=%d msg='%s'", code, msg ? msg : "");

    // Audit fix #10: inside a compound op (HandleInvokeByName), the outer
    // CompoundOpGuard publishes status/cmd; suppress the intermediate
    // signal here so CE doesn't see a transient DONE between sub-steps.
    if (s_compoundDepth > 0) return;

    // Signal completion — MUST write status BEFORE clearing cmd
    Box().status = STATUS_DONE;
    Box().cmd = CMD_IDLE;
}

static void SetDone(int32_t resultCode) {
    Box().result = resultCode;

    // Audit fix #10: see SetError comment.
    if (s_compoundDepth > 0) return;

    // Signal completion — MUST write status BEFORE clearing cmd
    Box().status = STATUS_DONE;
    Box().cmd = CMD_IDLE;
}

} // namespace Mimic
