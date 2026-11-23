// mimic_mailbox_test.cpp
// UE5CEDumper — the Cheat Engine mailbox (Mimic.cpp), served one command at a time against fake modules.
//
// WHAT IT DRIVES
//   Mimic::ServeOne, the one pass the polling thread makes over a mailbox: the init gate, the dispatch, the error and
//   done bookkeeping, a handler that throws, a mailbox another poller is busy with, and an adopted mailbox answered in
//   place; and CMD_TELEPORT's marshalling, op by op. No polling thread is started, so every command is served
//   exactly when the test says.
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
#include <cstring>
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
// Wirbel: every call is logged with the arguments Mimic passed, and answers what the test chose. An unknown landing
// leaves outNewPose as the caller set it, as Wirbel.h says the real one does.
static int32_t g_tpRc = 0;
static uint8_t g_tpTier = 0, g_tpSource = 0;
static Wirbel::Pose g_tpPose{};
static const char* g_tpMap = "";
static bool g_tpParentRel = false, g_tpLandingKnown = true, g_tpUsedCenter = false, g_tpCursorState = false;
static Wirbel::Marker g_tpMarker{};
static Wirbel::Pov g_tpPov{};
static std::vector<std::string> g_tpCalls;
static void TpCall(const char* fmt, ...) {
    char line[160];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(line, sizeof line, fmt, ap);
    va_end(ap);
    g_tpCalls.emplace_back(line);
}
static int32_t TpPoseRead(const char* what, Wirbel::Pose& out, char* map, int32_t cap, uint8_t* src, bool* parentRel) {
    TpCall("%s", what);
    out = g_tpPose;
    snprintf(map, static_cast<size_t>(cap), "%s", g_tpMap);
    if (src) *src = g_tpSource;
    if (parentRel) *parentRel = g_tpParentRel;
    return g_tpRc;
}
static int32_t TpTier(uint8_t* tier) {
    if (tier) *tier = g_tpTier;
    return g_tpRc;
}
namespace Wirbel {
int32_t GetPose(Pose& out, char* map, int32_t cap, uint8_t* src, bool* parentRel) {
    return TpPoseRead("GetPose", out, map, cap, src, parentRel);
}
int32_t BugItSave(Pose& out, char* map, int32_t cap, uint8_t* src, bool* parentRel) {
    return TpPoseRead("BugItSave", out, map, cap, src, parentRel);
}
int32_t GetPov(Pov& out) { TpCall("GetPov"); out = g_tpPov; return g_tpRc; }
int32_t SaveMarker(int32_t slot) { TpCall("SaveMarker(%d)", slot); return g_tpRc; }
int32_t RecallMarker(int32_t slot, bool force, uint8_t* tier) {
    TpCall("RecallMarker(%d,%d)", slot, force ? 1 : 0);
    return TpTier(tier);
}
int32_t RecallExplicit(const Pose& p, bool hasRot, uint8_t* tier, bool keepUndo) {
    TpCall("RecallExplicit(%g,%g,%g,%g,%g,%g,%d,%d)", p.X, p.Y, p.Z, p.Pitch, p.Yaw, p.Roll, hasRot ? 1 : 0,
           keepUndo ? 1 : 0);
    return TpTier(tier);
}
int32_t GetMarker(int32_t slot, Marker& out) { TpCall("GetMarker(%d)", slot); out = g_tpMarker; return g_tpRc; }
int32_t ClearMarker(int32_t slot) { TpCall("ClearMarker(%d)", slot); return g_tpRc; }
int32_t RecallLast(uint8_t* tier) { TpCall("RecallLast"); return TpTier(tier); }
int32_t GetLast(Marker& out) { TpCall("GetLast"); out = g_tpMarker; return g_tpRc; }
int32_t BugItGo(uint8_t* tier) { TpCall("BugItGo"); return TpTier(tier); }
int32_t TeleportRelative(double distance, bool horizontalOnly, Pose& outNewPose, uint8_t* tier, bool* landingKnown) {
    TpCall("TeleportRelative(%g,%d)", distance, horizontalOnly ? 1 : 0);
    if (landingKnown) *landingKnown = g_tpLandingKnown;
    if (g_tpLandingKnown) outNewPose = g_tpPose;
    return TpTier(tier);
}
int32_t TeleportToCursor(double zOffset, int32_t channel, bool fallbackCenter, Pose* hit, uint8_t* tier, bool* usedCenter) {
    TpCall("TeleportToCursor(%g,%d,%d)", zOffset, channel, fallbackCenter ? 1 : 0);
    if (hit) *hit = g_tpPose;
    if (usedCenter) *usedCenter = g_tpUsedCenter;
    return TpTier(tier);
}
int32_t SetMouseCursor(bool show, bool* state) {
    TpCall("SetMouseCursor(%d)", show ? 1 : 0);
    if (state) *state = g_tpCursorState;
    return g_tpRc;
}
int32_t GetMouseCursor(bool* state) { TpCall("GetMouseCursor"); if (state) *state = g_tpCursorState; return g_tpRc; }
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

    // [W3-DEBUGCAM-QUEUED] [DBGCAM-OFF-OVER-QUEUED-ON] A queued toggle runs later and a blocked one never ran: the text
    // CE shows verbatim says which, in each direction.
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

    // [W2-MARKER-PARENTREL] [W2-TPREL-TRANSPORTS] [TPREL-ZEROPOSE-2026-09-10] The mailbox only marshals: inputs read
    // before the shared buffer is overwritten, Wirbel's code passed through, and the pose block's flags byte saying
    // what kind of pose each op read. paramsData is filled with 0xCC before each send, so a byte the handler did not
    // write is visible.
    {   blk("TELEPORT - each op's inputs reach Wirbel, and its pose, tier and flags come back");
        using Calls = std::vector<std::string>;
        const Wirbel::Pose kPose{ 1, 2, 3, 4, 5, 6 };
        const Wirbel::Pose kLanding{ 10, 20, 30, 0, 90, 0 };
        auto reset = [&] {
            g_tpRc = 0; g_tpTier = 0; g_tpSource = 0; g_tpPose = {}; g_tpMap = "";
            g_tpParentRel = false; g_tpLandingKnown = true; g_tpUsedCenter = false; g_tpCursorState = false;
            g_tpMarker = {}; g_tpPov = {};
            g_tpCalls.clear();
            memset(mb.paramsData, 0xCC, sizeof mb.paramsData);
        };
        auto dbl = [&](size_t at) { double d; memcpy(&d, mb.paramsData + at, sizeof d); return d; };
        auto bits = [&](size_t at) { uint64_t u; memcpy(&u, mb.paramsData + at, sizeof u); return u; };
        auto pose = [&](const Wirbel::Pose& p) {
            return dbl(0) == p.X && dbl(8) == p.Y && dbl(16) == p.Z && dbl(24) == p.Pitch && dbl(32) == p.Yaw
                && dbl(40) == p.Roll;
        };
        auto mapName = [&] { return std::string(reinterpret_cast<const char*>(mb.paramsData + 48)); };
        auto marker = [&](bool parentRel) {
            g_tpMarker.Valid = true;
            g_tpMarker.P = kPose;
            snprintf(g_tpMarker.MapName, sizeof g_tpMarker.MapName, "%s", "Map_B");
            g_tpMarker.ParentRelative = parentRel;
        };
        auto input = [&](double d, uint8_t b8, uint8_t b9) {
            memcpy(mb.paramsData, &d, sizeof d);
            mb.paramsData[8] = b8;
            mb.paramsData[9] = b9;
        };
        const uint64_t kNan = 0x7FF8000000000000ull;

        reset(); g_tpPose = kPose; g_tpMap = "Map_A"; g_tpSource = 1; g_tpParentRel = true;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_GET_POSE);
        check("TELEPORT GET_POSE ⭐: the pose, its map and source, and bit0 for a parent-relative read",
              g_tpCalls == Calls{ "GetPose" } && Answered(mb) && mb.result == 0 && Msg(mb).empty() && pose(kPose)
              && mapName() == "Map_A" && mb.paramsData[176] == 1 && mb.paramsData[177] == 0 && mb.paramsData[178] == 0x01);
        reset(); g_tpPose = kPose; g_tpMap = "Map_A";
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_GET_POSE);
        check("TELEPORT GET_POSE world: a world-space read publishes flags 0", mb.result == 0 && mb.paramsData[178] == 0);
        reset(); g_tpPose = kPose; g_tpRc = Wirbel::TP_ERR_NO_PAWN;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_GET_POSE);
        check("TELEPORT GET_POSE failed: the code and its text, and no pose published",
              Answered(mb) && mb.result == -3 && Msg(mb) == "Teleport: op=0 slot=0 failed code=-3"
              && mb.paramsData[0] == 0xCC, Msg(mb).c_str());

        reset(); g_tpPose = Wirbel::Pose{ 7, 8, 9, 10, 11, 12 }; g_tpMap = "Map_A"; g_tpSource = 1; g_tpParentRel = true;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_BUGIT_SAVE);
        check("TELEPORT BUGIT_SAVE ⭐: the BugIt pose, its source, and bit0 for a parent-relative read",
              g_tpCalls == Calls{ "BugItSave" } && mb.result == 0 && pose(Wirbel::Pose{ 7, 8, 9, 10, 11, 12 })
              && mapName() == "Map_A" && mb.paramsData[176] == 1 && mb.paramsData[178] == 0x01);

        reset(); marker(true);
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_SAVE, 2);
        check("TELEPORT SAVE ⭐: saved, then the same slot read back -- its pose, map and bit0",
              g_tpCalls == (Calls{ "SaveMarker(2)", "GetMarker(2)" }) && mb.result == 0 && pose(kPose)
              && mapName() == "Map_B" && mb.paramsData[176] == 0 && mb.paramsData[177] == 0 && mb.paramsData[178] == 0x01);
        reset(); marker(true); g_tpRc = Wirbel::TP_ERR_NO_PAWN;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_SAVE, 2);
        check("TELEPORT SAVE failed: nothing is read back and nothing published",
              g_tpCalls == Calls{ "SaveMarker(2)" } && mb.result == -3 && mb.paramsData[0] == 0xCC);
        reset(); marker(true);
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_GET_MARKER, 1);
        check("TELEPORT GET_MARKER: the slot's pose, map and bit0", g_tpCalls == Calls{ "GetMarker(1)" }
              && mb.result == 0 && pose(kPose) && mapName() == "Map_B" && mb.paramsData[178] == 0x01);
        reset(); marker(true);
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_GET_LAST);
        check("TELEPORT GET_LAST: the last slot's pose and bit0", g_tpCalls == Calls{ "GetLast" } && mb.result == 0
              && pose(kPose) && mb.paramsData[178] == 0x01);
        reset();
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_CLEAR_MARKER, 4);
        check("TELEPORT CLEAR_MARKER: the slot reaches Wirbel", g_tpCalls == Calls{ "ClearMarker(4)" } && mb.result == 0);

        reset(); g_tpTier = 2;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_RECALL, 2);
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_RECALL_FORCE, 1);
        check("TELEPORT RECALL: the slot reaches Wirbel, and force only for RECALL_FORCE",
              g_tpCalls == (Calls{ "RecallMarker(2,0)", "RecallMarker(1,1)" }) && mb.result == 0);
        check("TELEPORT RECALL tier: the tier published over a cleared buffer",
              mb.paramsData[177] == 2 && mb.paramsData[0] == 0 && mb.paramsData[178] == 0);
        reset(); g_tpRc = Wirbel::TP_ERR_PARENT_RELATIVE;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_RECALL_FORCE, 1);
        check("TELEPORT RECALL_FORCE ⭐: a parent-relative refusal reaches CE as -16, with its text",
              Answered(mb) && mb.result == -16 && Msg(mb) == "Teleport: op=3 slot=1 failed code=-16", Msg(mb).c_str());
        reset(); g_tpTier = 1;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_RECALL_LAST);
        check("TELEPORT RECALL_LAST: the undo recall, its tier published", g_tpCalls == Calls{ "RecallLast" }
              && mb.result == 0 && mb.paramsData[177] == 1 && mb.paramsData[0] == 0);
        reset(); g_tpRc = Wirbel::TP_ERR_PARENT_RELATIVE;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_BUGIT_GO);
        check("TELEPORT BUGIT_GO ⭐: a parent-relative refusal reaches CE as -16, over a cleared buffer",
              g_tpCalls == Calls{ "BugItGo" } && Answered(mb) && mb.result == -16
              && Msg(mb) == "Teleport: op=10 slot=0 failed code=-16" && mb.paramsData[177] == 0 && mb.paramsData[0] == 0,
              Msg(mb).c_str());

        reset(); input(50.0, 3, 0); g_tpPose = kLanding; g_tpTier = 2;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_CURSOR);
        check("TELEPORT CURSOR ⭐: the Z offset, trace channel and fallback are read before the buffer is reused",
              g_tpCalls == Calls{ "TeleportToCursor(50,3,0)" } && mb.result == 0, g_tpCalls.empty() ? "" : g_tpCalls[0].c_str());
        check("TELEPORT CURSOR: the hit published, the rest cleared, the tier, and no center fallback",
              dbl(0) == 10 && dbl(8) == 20 && dbl(16) == 30 && bits(24) == 0 && mb.paramsData[177] == 2
              && mb.paramsData[178] == 0);
        reset(); input(50.0, 3, 1); g_tpUsedCenter = true;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_CURSOR);
        check("TELEPORT CURSOR center: the fallback asked for, and the screen center used is published",
              g_tpCalls == Calls{ "TeleportToCursor(50,3,1)" } && mb.paramsData[178] == 1);
        reset(); input(50.0, 3, 0); g_tpPose = kLanding; g_tpTier = 1; g_tpRc = Wirbel::TP_ERR_NO_HIT;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_CURSOR);
        check("TELEPORT CURSOR failed: no hit published, the tier still is",
              mb.result == -8 && bits(0) == 0 && bits(8) == 0 && bits(16) == 0 && mb.paramsData[177] == 1);

        reset();
        g_tpPov.Cam = Wirbel::Pose{ 1, 2, 3, 4, 5, 6 };
        g_tpPov.Fov = 90.0;
        g_tpPov.HasPawn = true;
        g_tpPov.Pawn = Wirbel::Pose{ 7, 8, 9, 0, 0, 0 };
        g_tpPov.Source = 2;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_GET_POV);
        check("TELEPORT GET_POV ⭐: camera pose, FOV, pawn location, has-pawn and source at their offsets",
              g_tpCalls == Calls{ "GetPov" } && mb.result == 0 && pose(kPose) && dbl(48) == 90.0 && dbl(56) == 7
              && dbl(64) == 8 && dbl(72) == 9 && mb.paramsData[80] == 1 && mb.paramsData[81] == 2);
        reset(); g_tpPov.Fov = 90.0; g_tpRc = Wirbel::TP_ERR_INVOKE;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_GET_POV);
        check("TELEPORT GET_POV failed: a cleared buffer", mb.result == -5 && bits(48) == 0 && mb.paramsData[0] == 0);

        reset(); input(250.0, 1, 0xCC); g_tpPose = kLanding; g_tpTier = 1;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_RELATIVE);
        check("TELEPORT RELATIVE: distance and mode reach Wirbel, the landing and tier published, flags 0",
              g_tpCalls == Calls{ "TeleportRelative(250,0)" } && mb.result == 0 && pose(kLanding)
              && mb.paramsData[48] == 0 && mb.paramsData[176] == 0 && mb.paramsData[177] == 1 && mb.paramsData[178] == 0,
              g_tpCalls.empty() ? "" : g_tpCalls[0].c_str());
        reset(); input(250.0, 0, 0xCC); g_tpPose = kLanding; g_tpTier = 2; g_tpLandingKnown = false;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_RELATIVE);
        check("TELEPORT RELATIVE unknown ⭐: an unknown landing publishes NaN and bit1, never a landing at the origin",
              g_tpCalls == Calls{ "TeleportRelative(250,1)" } && mb.result == 0 && bits(0) == kNan && bits(8) == kNan
              && bits(16) == kNan && bits(24) == kNan && bits(32) == kNan && bits(40) == kNan
              && mb.paramsData[177] == 2 && mb.paramsData[178] == 0x02);
        reset(); input(250.0, 0, 0xCC); g_tpRc = Wirbel::TP_ERR_PARENT_RELATIVE;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_RELATIVE);
        check("TELEPORT RELATIVE refused: a parent-relative start's refusal reaches CE as -16",
              mb.result == -16 && Msg(mb) == "Teleport: op=12 slot=0 failed code=-16", Msg(mb).c_str());

        reset();
        {
            const double v[6] = { 100, 200, 300, 10, 20, 30 };
            memcpy(mb.paramsData, v, sizeof v);
            mb.paramsData[48] = 1;
        }
        g_tpTier = 2;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_EXPLICIT);
        check("TELEPORT EXPLICIT ⭐: the six coordinates and the rotation flag reach Wirbel, the undo point kept",
              g_tpCalls == Calls{ "RecallExplicit(100,200,300,10,20,30,1,0)" } && mb.result == 0,
              g_tpCalls.empty() ? "" : g_tpCalls[0].c_str());
        check("TELEPORT EXPLICIT tier: published over a cleared buffer", mb.paramsData[177] == 2 && mb.paramsData[0] == 0);
        reset();
        mb.paramsData[48] = 0;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_EXPLICIT);
        check("TELEPORT EXPLICIT no rotation: a zero flag keeps the pawn's rotation",
              g_tpCalls.size() == 1 && g_tpCalls[0].find(",0,0)") != std::string::npos,
              g_tpCalls.empty() ? "" : g_tpCalls[0].c_str());

        reset(); g_tpCursorState = true;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_SET_CURSOR, 1);
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_SET_CURSOR, 0);
        check("TELEPORT SET_CURSOR: the slot is the wanted state", g_tpCalls == (Calls{ "SetMouseCursor(1)", "SetMouseCursor(0)" }));
        check("TELEPORT SET_CURSOR state: the state that resulted is published", mb.paramsData[0] == 1 && mb.paramsData[1] == 0);
        reset(); g_tpCursorState = true;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_GET_CURSOR);
        check("TELEPORT GET_CURSOR: the state published", g_tpCalls == Calls{ "GetMouseCursor" } && mb.paramsData[0] == 1);
        reset(); g_tpCursorState = true; g_tpRc = Wirbel::TP_ERR_NO_CONTROLLER;
        Send(mb, Mimic::CMD_TELEPORT, Mimic::TP_OP_GET_CURSOR);
        check("TELEPORT GET_CURSOR failed: no state published", mb.result == -2 && mb.paramsData[0] == 0);

        reset();
        Send(mb, Mimic::CMD_TELEPORT, 99);
        check("TELEPORT unknown op: refused -1 with its own text, and Wirbel never asked",
              Answered(mb) && mb.result == -1 && Msg(mb) == "Teleport: unknown op" && g_tpCalls.empty(), Msg(mb).c_str());
    }

    printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
    return g_fail == 0 ? 0 : 1;
}
