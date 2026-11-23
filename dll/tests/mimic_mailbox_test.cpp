// mimic_mailbox_test.cpp
// UE5CEDumper — the Cheat Engine mailbox (Mimic.cpp), served one command at a time against fake modules.
//
// WHAT IT DRIVES
//   Mimic::ServeOne, the one pass the polling thread makes over a mailbox: the init gate, the dispatch, the error and
//   done bookkeeping, a handler that throws, a mailbox another poller is busy with, and an adopted mailbox answered in
//   place. No polling thread is started, so every command is served exactly when the test says.
//
// WHAT IS FAKED
//   Everything Mimic dispatches TO: the Frieren C ABI (UE5_*), Wirbel, Grausam, Schlacht, Dunste's status, Stark's
//   invoke-owner scope and Frieren's debug-camera pending state. Each fake records its arguments and returns what the
//   test chose. The hold ledger is the real one (Voll::Holds, header-inline), so "CE now holds it" is read back from it.
//
// WHY IT #includes THE .cpp
//   ServeOne, the handlers and the mailbox pointer are statics of Mimic.cpp. The core it reaches (Aura, Ubel, ...) is
//   ue5_testcore, linked.

#include <windows.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>   // getenv_s: the AddressSanitizer build's negative control
#include <atomic>
#include <functional>
#include <stdexcept>
#include <string>
#include <vector>

namespace Sein {
void Info(const char*, const char*, ...) {}
void Error(const char*, const char*, ...) {}
void Warn(const char*, const char*, ...) {}
void Debug(const char*, const char*, ...) {}
void Summary(const char*, ...) {}
}  // namespace Sein

#include "../src/Mimic.cpp"  // NOLINT -- see "WHY IT #includes THE .cpp" above

// ── Frieren's globals ───────────────────────────────────────────────────────────────────────────────────────────
uintptr_t g_cachedGObjects = 0;
uintptr_t g_cachedGNames = 0;
uintptr_t g_cachedGWorld = 0;
uintptr_t g_cachedGEngine = 0;
uint32_t g_cachedUEVersion = 0;
std::atomic<bool> g_initInProgress{false};

// ── the fakes ───────────────────────────────────────────────────────────────────────────────────────────────────
static int g_initCalls = 0;
static std::function<void()> g_onInit;          // what a UE5_Init call does to the globals
static std::vector<int32_t> g_godModeCalls, g_debugCameraCalls;
static int32_t g_godModeRc = 1, g_debugCameraRc = 1, g_debugCameraState = 0;
static int32_t g_protectWant = 1, g_protectLive = -1, g_protectResolvable = 1;
static bool g_pendingOn = false;
static uint64_t g_pendingOwner = 0;
static std::vector<bool> g_foregroundCalls;
static bool g_foregroundThrows = false;
static std::vector<std::pair<bool, uint64_t>> g_seeThroughCalls;
static std::vector<int32_t> g_pierceCalls;

extern "C" {
__declspec(dllexport) bool UE5_Init() {
    ++g_initCalls;
    if (g_onInit) g_onInit();
    return g_cachedGObjects != 0;
}
__declspec(dllexport) int32_t UE5_SetGodMode(int32_t enable) { g_godModeCalls.push_back(enable); return g_godModeRc; }
__declspec(dllexport) int32_t UE5_GetGodMode() { return 1; }
__declspec(dllexport) int32_t UE5_GetProtectState(int32_t* outWant, int32_t* outLive, int32_t* outResolvable) {
    *outWant = g_protectWant; *outLive = g_protectLive; *outResolvable = g_protectResolvable;
    return 0;
}
__declspec(dllexport) int32_t UE5_SetDebugCamera(int32_t enable) { g_debugCameraCalls.push_back(enable); return g_debugCameraRc; }
__declspec(dllexport) int32_t UE5_GetDebugCameraState() { return g_debugCameraState; }
__declspec(dllexport) int32_t UE5_GetOffsetsVerdict(char* reasonBuf, int32_t bufLen) {
    snprintf(reasonBuf, static_cast<size_t>(bufLen), "%s", "measured by the probe");
    return 1;
}
__declspec(dllexport) int32_t UE5_SetMovementPercent(int32_t, double) { return 0; }
__declspec(dllexport) int32_t UE5_SetTimeDilation(int32_t, double) { return 0; }
__declspec(dllexport) int32_t UE5_ResetTimeDilation(int32_t) { return 0; }
__declspec(dllexport) int32_t UE5_SetGravityDirection(double, double, double) { return 0; }
__declspec(dllexport) int32_t UE5_SetFly(int32_t) { return 0; }
__declspec(dllexport) int32_t UE5_SetFlySpeed(double) { return 0; }
__declspec(dllexport) int32_t UE5_SetFlyPreset(int32_t) { return 0; }
__declspec(dllexport) int32_t UE5_SetFlyNoclip(int32_t) { return 0; }
__declspec(dllexport) bool UE5_GetObjectName(uintptr_t, char* buf, int32_t bufLen) {
    if (buf && bufLen > 0) buf[0] = '\0';
    return false;
}
__declspec(dllexport) int32_t UE5_CallProcessEvent(uintptr_t, uintptr_t, uintptr_t) { return 0; }
int32_t UE5_CallProcessEventEx(uintptr_t, uintptr_t, uintptr_t, uint32_t) { return 0; }   // Mimic declares it plain
__declspec(dllexport) int32_t UE5_CallProcessEventDirect(uintptr_t, uintptr_t, uintptr_t) { return 0; }
}  // extern "C"

namespace FrierenInvoke {
bool PrepareObjectParams(uintptr_t, uint8_t*, size_t, bool, std::string&) { return true; }
}
namespace FrierenDebugCamera {
bool PendingOn(uint64_t& owner) { owner = g_pendingOwner; return g_pendingOn; }
}
namespace Stark {
void SetInvokeTimeoutMs(int32_t) {}
InvokeOwnerScope::InvokeOwnerScope(uint64_t, bool) : m_prevOwner(0), m_prevIfFree(false) {}
InvokeOwnerScope::~InvokeOwnerScope() {}
}
namespace Grausam {
int SetForegroundLock(bool enable) {
    g_foregroundCalls.push_back(enable);
    if (g_foregroundThrows) throw std::runtime_error("a handler that throws");
    return enable ? 1 : 0;
}
int IsForegroundLockEnabled() { return g_foregroundCalls.empty() ? 0 : (g_foregroundCalls.back() ? 1 : 0); }
}
namespace Schlacht {
int32_t SetEnabled(bool enable, uint64_t owner) { g_seeThroughCalls.emplace_back(enable, owner); return enable ? 1 : 0; }
void SetPierceCount(int32_t count) { g_pierceCalls.push_back(count); }
}
namespace Dunste {
int32_t GetStatus(FlyStatus& out) { out = FlyStatus{}; return FR_OK; }
}
namespace Wirbel {
int32_t GetPose(Pose&, char*, int32_t, uint8_t*, bool*) { return 0; }
int32_t GetPov(Pov&) { return 0; }
int32_t SaveMarker(int32_t) { return 0; }
int32_t RecallMarker(int32_t, bool, uint8_t*) { return 0; }
int32_t RecallExplicit(const Pose&, bool, uint8_t*, bool) { return 0; }
int32_t GetMarker(int32_t, Marker&) { return 0; }
int32_t ClearMarker(int32_t) { return 0; }
int32_t RecallLast(uint8_t*) { return 0; }
int32_t GetLast(Marker&) { return 0; }
int32_t BugItSave(Pose&, char*, int32_t, uint8_t*, bool*) { return 0; }
int32_t BugItGo(uint8_t*) { return 0; }
int32_t TeleportRelative(double, bool, Pose&, uint8_t*, bool*) { return 0; }
int32_t TeleportToCursor(double, int32_t, bool, Pose*, uint8_t*, bool*) { return 0; }
int32_t SetMouseCursor(bool, bool*) { return 0; }
int32_t GetMouseCursor(bool*) { return 0; }
}

// ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────────

static int g_pass = 0, g_fail = 0;
static void check(const char* label, bool cond, const char* got = nullptr) {
    if (cond) { ++g_pass; }
    else { ++g_fail; printf("  FAIL  %s%s%s\n", label, got ? "   got: " : "", got ? got : ""); }
}
static void blk(const char* name) { printf("- %s\n", name); }

// What CE does: clear the status, fill the inputs, write cmd last. Then one poller pass over that mailbox.
static void Send(Mimic::MailboxData& box, int32_t cmd, uint64_t instanceAddr = 0, uint64_t ufuncAddr = 0) {
    box.status = Mimic::STATUS_IDLE;
    box.result = 0x5A5A;
    box.instanceAddr = instanceAddr;
    box.ufuncAddr = ufuncAddr;
    box.cmd = cmd;
    bool warned = false;
    Mimic::ServeOne(&box, warned);
}
static std::string Msg(const Mimic::MailboxData& box) { return std::string(box.errorMsg); }
static bool Answered(const Mimic::MailboxData& box) { return box.status == Mimic::STATUS_DONE && box.cmd == Mimic::CMD_IDLE; }

int main() {
    setvbuf(stdout, nullptr, _IONBF, 0);
    printf("mimic_mailbox_test — the Cheat Engine mailbox, one command at a time against fake modules\n");
#ifdef __SANITIZE_ADDRESS__
    // [CPP-HARDENING-2026-11-14] The AddressSanitizer build only: the banner and negative control
    // build_dll.py --asan --run needs (a deliberate heap overflow that must be reported).
    printf("asan: on\n");
    {
        size_t negLen = 0;
        if (getenv_s(&negLen, nullptr, 0, "UE5_ASAN_NEGATIVE_CONTROL") == 0 && negLen > 0) {
            char* volatile p = new char[16];
            p[16] = 1;   // heap-buffer-overflow: ASan ends the process here
            delete[] p;
        }
    }
#endif
    Mimic::MailboxData& mb = g_invokeMailbox;
    char got[160];
    auto num = [&](long long v) { snprintf(got, sizeof got, "%lld", v); return got; };

    {   blk("UNKNOWN - a command nobody handles is answered, never left processing");
        g_cachedGObjects = g_cachedGNames = 1;
        Send(mb, 99);
        check("UNKNOWN: answered -- status DONE, cmd back to IDLE", Answered(mb));
        check("UNKNOWN: result -1, 'Unknown command'", mb.result == -1 && Msg(mb) == "Unknown command", Msg(mb).c_str());
    }

    {   blk("OFFSETS - the offsets verdict needs no init, and a success carries no earlier error text");
        g_cachedGObjects = g_cachedGNames = 0;
        g_initCalls = 0;
        Send(mb, Mimic::CMD_OFFSETS_VERDICT);
        check("OFFSETS ⭐: answered with nothing initialised -- its answer IS the init state", Answered(mb) && mb.result == 1,
              num(mb.result));
        check("OFFSETS ⭐: ...and it ran no auto-init to answer", g_initCalls == 0, num(g_initCalls));
        check("OFFSETS: the reason reaches the mailbox",
              std::string(reinterpret_cast<const char*>(mb.paramsData)) == "measured by the probe");
        check("OFFSETS ⭐: the previous command's error text is gone", Msg(mb).empty(), Msg(mb).c_str());
    }

    {   blk("INITGATE - a command that walks reflection waits for an init, and refuses without one");
        g_cachedGObjects = g_cachedGNames = 0;
        g_initCalls = 0;
        g_onInit = nullptr;
        g_godModeCalls.clear();
        Send(mb, Mimic::CMD_PROTECT, Mimic::PROTECT_OP_SET_GODMODE, 1);
        check("INITGATE: an auto-init is attempted", g_initCalls >= 1, num(g_initCalls));
        check("INITGATE ⭐: it fails, so the command is refused -10 and never reaches God Mode",
              Answered(mb) && mb.result == -10 && g_godModeCalls.empty(), num(mb.result));
        g_foregroundCalls.clear();
        g_initCalls = 0;
        Send(mb, Mimic::CMD_FOREGROUND, Mimic::FG_OP_SET, 1);
        check("INITGATE ⭐: Keep Foreground is pure Win32 -- served with no init, and none attempted",
              mb.result == 1 && g_foregroundCalls.size() == 1 && g_initCalls == 0, num(mb.result));

        // An init that is still scanning: UE5_Init returns while another thread holds the fence. The gate asks again
        // while the fence is up, and the third call finds the globals.
        g_initCalls = 0;
        g_initInProgress.store(true);
        g_onInit = [] {
            if (g_initCalls >= 3) { g_cachedGObjects = g_cachedGNames = 1; g_initInProgress.store(false); }
        };
        Send(mb, Mimic::CMD_PROTECT, Mimic::PROTECT_OP_SET_GODMODE, 1);
        check("INITGATE ⭐: while an init is scanning, the gate calls UE5_Init again until it settles",
              g_initCalls == 3 && mb.result == g_godModeRc && g_godModeCalls.size() == 1, num(g_initCalls));
        g_onInit = nullptr;
        g_initInProgress.store(false);
    }

    g_cachedGObjects = g_cachedGNames = 1;

    {   blk("PROTECT - God Mode through the mailbox, and CE recorded as its holder");
        Voll::Holds().SetOwner(Grimoire::HOLD_GOD_MODE, 0);
        g_godModeCalls.clear();
        Send(mb, Mimic::CMD_PROTECT, Mimic::PROTECT_OP_SET_GODMODE, 1);
        check("PROTECT: UE5_SetGodMode(1), its result published", g_godModeCalls == std::vector<int32_t>{ 1 }
              && mb.result == g_godModeRc && Answered(mb));
        check("PROTECT ⭐: the hold is CE's now", Voll::Holds().Owner(Grimoire::HOLD_GOD_MODE) == Voll::kHoldOwnerCe);
        g_protectWant = 1; g_protectLive = -1; g_protectResolvable = 1;
        Send(mb, Mimic::CMD_PROTECT, Mimic::PROTECT_OP_GET_STATE);
        check("PROTECT: the state is want / live / resolvable, an unreadable live as 0xFF",
              mb.paramsData[0] == 1 && mb.paramsData[1] == 0xFF && mb.paramsData[2] == 1);
        Send(mb, Mimic::CMD_PROTECT, 7);
        check("PROTECT: an unknown op is refused", mb.result == -1 && Msg(mb) == "Protect: unknown op", Msg(mb).c_str());
    }

    // [MCP-WRITE-GRANTS-2026-11-14] A change CE makes through the mailbox makes CE the holder, so an AI session never
    // replaces or ends it. Each handler that changes a hold, and the hold it names.
    {   blk("HOLDS - every handler that changes a hold records CE as its holder");
        struct Case { const char* what; int32_t cmd; uint64_t inst; uint64_t ufunc; std::string hold; };
        const Case cases[] = {
            { "Keep Foreground",   Mimic::CMD_FOREGROUND, Mimic::FG_OP_SET, 1, Grimoire::HOLD_FOREGROUND_LOCK },
            { "walk speed",        Mimic::CMD_MOVEMENT, 0, 0, std::string(Grimoire::HOLD_MOVEMENT_PREFIX) + "walk_speed" },
            { "gravity scale",     Mimic::CMD_MOVEMENT, 1, 0, std::string(Grimoire::HOLD_MOVEMENT_PREFIX) + "gravity" },
            { "jump",              Mimic::CMD_MOVEMENT, 2, 0, std::string(Grimoire::HOLD_MOVEMENT_PREFIX) + "jump" },
            { "gravity direction", Mimic::CMD_MOVEMENT, 3, 0, Grimoire::HOLD_GRAVITY_DIRECTION },
            { "world time",        Mimic::CMD_TIME, Mimic::TIME_OP_SET, 0, std::string(Grimoire::HOLD_TIME_PREFIX) + "global" },
            { "pawn time reset",   Mimic::CMD_TIME, Mimic::TIME_OP_RESET, 1, std::string(Grimoire::HOLD_TIME_PREFIX) + "pawn" },
            { "Fly on",            Mimic::CMD_FLY, Mimic::FLY_OP_SET_ENABLED, 1, Grimoire::HOLD_FLY },
            { "Fly speed",         Mimic::CMD_FLY, Mimic::FLY_OP_SET_SPEED, 0, Grimoire::HOLD_FLY },
            { "Fly preset",        Mimic::CMD_FLY, Mimic::FLY_OP_SET_PRESET, 1, Grimoire::HOLD_FLY },
            { "Fly noclip",        Mimic::CMD_FLY, Mimic::FLY_OP_SET_NOCLIP, 1, Grimoire::HOLD_FLY },
        };
        for (const Case& c : cases) {
            Voll::Holds().SetOwner(c.hold, 0);
            Send(mb, c.cmd, c.inst, c.ufunc);
            check((std::string("HOLDS ⭐: ") + c.what + " -- CE holds '" + c.hold + "'").c_str(),
                  Answered(mb) && Voll::Holds().Owner(c.hold) == Voll::kHoldOwnerCe);
        }
        const std::string worldTime = std::string(Grimoire::HOLD_TIME_PREFIX) + "global";
        Voll::Holds().SetOwner(worldTime, 0);
        Send(mb, Mimic::CMD_TIME, Mimic::TIME_OP_SET, 5);
        check("HOLDS control: a time target that is neither world nor pawn takes no hold",
              Voll::Holds().Owner(worldTime) == 0);
    }

    {   blk("DEBUGCAM - a queued or blocked toggle says what to do next, and a pending ON stays its sender's");
        g_debugCameraRc = Stark::kInvokeTimedOutStillQueued;
        Send(mb, Mimic::CMD_SET_DEBUG_CAMERA, 1);
        check("DEBUGCAM ⭐: a queued ON passes the code through and says not to re-send",
              mb.result == Stark::kInvokeTimedOutStillQueued && Msg(mb).find("ON queued") != std::string::npos
              && Msg(mb).find("do not re-send") != std::string::npos, Msg(mb).c_str());
        Send(mb, Mimic::CMD_SET_DEBUG_CAMERA, 0);
        check("DEBUGCAM ⭐: a queued OFF says to send OFF again -- never that a second toggle undoes the first",
              Msg(mb).find("send OFF again") != std::string::npos && Msg(mb).find("undo") == std::string::npos,
              Msg(mb).c_str());
        g_debugCameraRc = Stark::kToggleBlockedByQueued;
        Send(mb, Mimic::CMD_SET_DEBUG_CAMERA, 1);
        check("DEBUGCAM ⭐: an ON refused over a queued OFF says it was not sent",
              mb.result == Stark::kToggleBlockedByQueued
              && Msg(mb).find("ON not sent -- an earlier OFF is still queued") != std::string::npos, Msg(mb).c_str());
        Send(mb, Mimic::CMD_SET_DEBUG_CAMERA, 0);
        check("DEBUGCAM ⭐: an OFF refused over a queued ON says it was not sent",
              Msg(mb).find("OFF not sent -- an earlier ON is still queued") != std::string::npos, Msg(mb).c_str());

        // An AI's ON is still pending: CE's toggle does not take the hold from its sender.
        g_debugCameraRc = 1;
        g_debugCameraState = 0;
        g_pendingOn = true;
        g_pendingOwner = 0x1234;
        Voll::Holds().SetOwner(Grimoire::HOLD_DEBUG_CAMERA, 0x1234);
        Send(mb, Mimic::CMD_SET_DEBUG_CAMERA, 1);
        check("DEBUGCAM ⭐: a pending ON is its sender's -- CE's toggle does not record CE as the holder",
              Voll::Holds().Owner(Grimoire::HOLD_DEBUG_CAMERA) == 0x1234);
        g_pendingOn = false;
        Send(mb, Mimic::CMD_SET_DEBUG_CAMERA, 1);
        check("DEBUGCAM control: with nothing pending, CE's toggle makes CE the holder",
              Voll::Holds().Owner(Grimoire::HOLD_DEBUG_CAMERA) == Voll::kHoldOwnerCe);
        g_debugCameraState = 7;
        Send(mb, Mimic::CMD_SET_DEBUG_CAMERA, 2);
        check("DEBUGCAM: request 2 is a query -- the state, and no toggle", mb.result == 7, num(mb.result));
    }

    {   blk("THREW - a handler that throws loses its command, not the mailbox");
        g_foregroundThrows = true;
        Send(mb, Mimic::CMD_FOREGROUND, Mimic::FG_OP_SET, 1);
        check("THREW ⭐: answered with -11 and says the operation did not complete",
              Answered(mb) && mb.result == Mimic::MB_ERR_HANDLER_THREW && Msg(mb).find("did NOT complete") != std::string::npos,
              Msg(mb).c_str());
        g_foregroundThrows = false;
        Send(mb, Mimic::CMD_FOREGROUND, Mimic::FG_OP_GET);
        check("THREW ⭐: the next command is served", Answered(mb) && mb.result >= 0 && Msg(mb).empty(), num(mb.result));
    }

    {   blk("ADOPTED - a mailbox taken over from an earlier copy is answered in that mailbox");
        static Mimic::MailboxData other = {};
        mb.result = 0x1111;
        Send(other, Mimic::CMD_OFFSETS_VERDICT);
        check("ADOPTED ⭐: the answer lands in the adopted mailbox", Answered(other) && other.result == 1, num(other.result));
        check("ADOPTED ⭐: ...and this copy's own mailbox is untouched", mb.result == 0x1111, num(mb.result));
    }

    {   blk("BUSY - a mailbox another poller is mid-command on is left to it");
        g_godModeCalls.clear();
        mb.instanceAddr = Mimic::PROTECT_OP_SET_GODMODE;
        mb.ufuncAddr = 1;
        mb.cmd = Mimic::CMD_PROTECT;
        mb.status = Mimic::STATUS_PROCESSING;
        bool warned = false;
        Mimic::ServeOne(&mb, warned);
        check("BUSY ⭐: not run a second time -- the other poller answers it", g_godModeCalls.empty()
              && mb.status == Mimic::STATUS_PROCESSING && mb.cmd == Mimic::CMD_PROTECT);
        mb.cmd = Mimic::CMD_IDLE;
        mb.status = Mimic::STATUS_IDLE;
    }

    {   blk("SEETHROUGH - the count and the toggle reach Schlacht, as CE's");
        g_seeThroughCalls.clear();
        g_pierceCalls.clear();
        Send(mb, Mimic::CMD_SEETHROUGH, 3, 1);
        check("SEETHROUGH: the pierce count is set first", g_pierceCalls == std::vector<int32_t>{ 3 });
        check("SEETHROUGH ⭐: enabled, owned by CE", g_seeThroughCalls.size() == 1 && g_seeThroughCalls[0].first
              && g_seeThroughCalls[0].second == Schlacht::kOwnerCe);
        check("SEETHROUGH: its result published", mb.result == 1 && Answered(mb), num(mb.result));
    }

    printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
    return g_fail == 0 ? 0 : 1;
}
