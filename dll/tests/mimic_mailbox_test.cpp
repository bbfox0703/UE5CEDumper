// mimic_mailbox_test.cpp
// UE5CEDumper — the Cheat Engine mailbox (Mimic.cpp), served one command at a time against fake modules.
//
// WHAT IT DRIVES
//   Mimic::ServeOne, the one pass the polling thread makes over a mailbox: the init gate, the dispatch, the error and
//   done bookkeeping, a handler that throws, a mailbox another poller is busy with, and an adopted mailbox answered in
//   place; CMD_TELEPORT's marshalling, op by op; and the lookups, listings and pointer queries. No polling thread
//   is started, so every command is served exactly when the test says.
//
// WHAT IS FAKED
//   Everything Mimic dispatches TO: the Frieren C ABI (UE5_*), Wirbel, Grausam, Schlacht, Dunste's status, Stark's
//   invoke-owner scope and Frieren's debug-camera pending state. Each fake records its arguments and returns what the
//   test chose. The hold ledger is the real one (Voll::Holds, header-inline), so "CE now holds it" is read back from it.
//   The core the lookups walk -- Aura, Ubel, Serie -- is the real one, over objects built in the test's own memory
//   (Fx); Sein::Info can carry a test's hook into one log line.
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

static std::function<void(const char* fmt)> g_onInfo;   // a test's hook, armed around one command
namespace Sein {
void Info(const char*, const char* fmt, ...) {
    if (g_onInfo) g_onInfo(fmt);
}
void Error(const char*, const char*, ...) {}
void Warn(const char*, const char*, ...) {}
void Debug(const char*, const char*, ...) {}
void Summary(const char*, ...) {}
}  // namespace Sein

#include "../src/Mimic.cpp"  // NOLINT -- see "WHY IT #includes THE .cpp" above
#include "../src/Serie.h"   // the fixture's name pool

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

// ── the reflection fixture ──────────────────────────────────────────────────────────────────────────────────────
// One name pool, one object array and one set of DynOff values for every block that walks reflection: Serie, Aura and
// the DynOff offsets are process globals, so a second fixture would overwrite the first. Built once and static, so a
// polling thread can read it too. The shapes are dll_core_test's USERPICK: a UE4 name table with the string at +0x10,
// 24-byte FUObjectItems, FProperty mode. Every wrong answer sits BELOW the right one in the array.
namespace Fx {
enum : int32_t {
    nClass = 1, nFunction, nObject, nChannel, nActorChannel, nActor, nHero, nSidekick, nHeroComp, nSwarm, nGameEngine,
    nMany, nCdoActorChannel, nCdoActor, nCdoHero, nHeroCompTpl, nHeroCompScs, nHero0, nHero1, nSidekick0, nHeroComp0,
    nHeroComp1, nCdoGameEngine, nGameEngine0, nSwarm0, nGameViewport, nChannelOnly, nK2Loc, nHeroOnly, nFn00,
    nLong = nFn00 + 15, nMany0, nNames };
enum {
    bClass, bFunction, bObject, bChannel, bActorChannel, bActor, bHero, bSidekick, bHeroComp, bSwarm, bGameEngine, bMany,
    bCdoActorChannel, bCdoActor, bCdoHero, bHeroCompTpl, bHeroCompScs, bHero0, bHero1, bSidekick0, bHeroComp0,
    bHeroComp1, bCdoGameEngine, bGameEngine0, bMany0, bNoClass, bChannelOnly, bK2Loc, bHeroOnly, bFn00,
    bLong = bFn00 + 15, bGameViewport, kBlobs };
constexpr int kFlood = 2000;   // archetypes met before the live HeroComps: more than either LIST_INSTANCES cap
constexpr int kSwarm = 2100;   // live Swarm_C objects: past the exact-scope cap

static const std::string kLong = "LongFunctionName_" + std::string(43, 'x');   // 60 characters
static uint8_t s_entry[nNames][0x80];
static uintptr_t s_chunk[nNames + 1];
static uintptr_t s_chunks[2];
alignas(16) static uint8_t s_b[kBlobs][0x200];
alignas(16) static uint8_t s_flood[kFlood][0x40];
alignas(16) static uint8_t s_swarm[kSwarm][0x40];
static uint8_t s_items[Grimoire::OBJECTS_PER_CHUNK * 24];
static uintptr_t s_table[1];
alignas(16) static uint8_t s_header[0x40];
static int32_t s_count = 0;
static int32_t s_hero0Index = -1;

static uintptr_t A(int b) { return b < 0 ? uintptr_t{0} : reinterpret_cast<uintptr_t>(s_b[b]); }
static void P64(uint8_t* b, int off, uint64_t v) { memcpy(b + off, &v, sizeof v); }
static void P32(uint8_t* b, int off, uint32_t v) { memcpy(b + off, &v, sizeof v); }
static void Obj(uint8_t* b, uintptr_t cls, int32_t name, uintptr_t outer, uint32_t flags = 0) {
    P64(b, Grimoire::OFF_UOBJECT_CLASS, cls);
    P32(b, Grimoire::OFF_UOBJECT_NAME, static_cast<uint32_t>(name));
    P64(b, DynOff::UOBJECT_OUTER, outer);
    P32(b, Grimoire::OFF_UOBJECT_FLAGS, flags);
}
static void Klass(int b, int32_t name, int super, int children = -1, int childProps = -1) {
    Obj(s_b[b], A(bClass), name, 0);
    P64(s_b[b], DynOff::USTRUCT_SUPER, A(super));
    P64(s_b[b], DynOff::USTRUCT_CHILDREN, A(children));
    P64(s_b[b], DynOff::USTRUCT_CHILDPROPS, A(childProps));
}
// A UFunction with no parameters. FunctionFlags, NumParms and ParmsSize read as 0x03 bytes wherever the reader looks;
// the checks take the expected values from Ubel::WalkFunctions, never from a literal.
static void Fn(int b, int32_t name, int outerCls, int next) {
    memset(s_b[b] + 0x60, 0x03, sizeof s_b[b] - 0x60);
    Obj(s_b[b], A(bFunction), name, A(outerCls));
    P64(s_b[b], DynOff::UFIELD_NEXT, A(next));
}
static void Add(uintptr_t obj) {
    memcpy(s_items + static_cast<size_t>(s_count) * 24, &obj, sizeof obj);
    memcpy(s_items + static_cast<size_t>(s_count) * 24 + 12, &s_count, sizeof s_count);
    ++s_count;
}
static void Publish() {
    s_table[0] = reinterpret_cast<uintptr_t>(s_items);
    const uintptr_t tbl = reinterpret_cast<uintptr_t>(s_table);
    const int32_t one = 1;
    memcpy(s_header + 0x10, &tbl, sizeof tbl);
    memcpy(s_header + 0x20, &s_count, sizeof s_count);
    memcpy(s_header + 0x24, &s_count, sizeof s_count);
    memcpy(s_header + 0x28, &one, sizeof one);
    memcpy(s_header + 0x2C, &one, sizeof one);
    Aura::InitWithExtendedLayout(reinterpret_cast<uintptr_t>(s_header), 24);
}

static void Build() {
    static bool built = false;
    if (built) return;
    built = true;

    std::string nm[nNames];
    nm[nClass] = "Class"; nm[nFunction] = "Function"; nm[nObject] = "Object"; nm[nChannel] = "Channel";
    nm[nActorChannel] = "ActorChannel"; nm[nActor] = "Actor"; nm[nHero] = "BP_Hero_C"; nm[nSidekick] = "BP_Sidekick_C";
    nm[nHeroComp] = "HeroComp"; nm[nSwarm] = "Swarm_C"; nm[nGameEngine] = "GameEngine"; nm[nMany] = "BP_Many_C";
    nm[nCdoActorChannel] = "Default__ActorChannel"; nm[nCdoActor] = "Default__Actor"; nm[nCdoHero] = "Default__BP_Hero_C";
    nm[nHeroCompTpl] = "HeroComp_Tpl"; nm[nHeroCompScs] = "HeroComp_SCS"; nm[nHero0] = "BP_Hero_C_0";
    nm[nHero1] = "BP_Hero_C_1"; nm[nSidekick0] = "BP_Sidekick_C_0"; nm[nHeroComp0] = "HeroComp_0";
    nm[nHeroComp1] = "HeroComp_1"; nm[nCdoGameEngine] = "Default__GameEngine"; nm[nGameEngine0] = "GameEngine_0";
    nm[nSwarm0] = "Swarm_C_0"; nm[nGameViewport] = "GameViewport"; nm[nChannelOnly] = "ChannelOnly";
    nm[nK2Loc] = "K2_GetActorLocation"; nm[nHeroOnly] = "HeroOnly"; nm[nLong] = kLong; nm[nMany0] = "Many_0";
    for (int i = 0; i < 15; ++i) {
        char b[8];
        snprintf(b, sizeof b, "Fn%02d", i);
        nm[nFn00 + i] = b;
    }
    for (int i = 1; i < nNames; ++i) {
        memcpy(s_entry[i] + 0x10, nm[i].c_str(), nm[i].size() + 1);
        s_chunk[i] = reinterpret_cast<uintptr_t>(s_entry[i]);
    }
    s_chunks[0] = reinterpret_cast<uintptr_t>(s_chunk);
    Serie::InitUE4(reinterpret_cast<uintptr_t>(s_chunks), 0x10);

    DynOff::bCasePreservingName = false;
    DynOff::bUseFProperty = true;
    g_cachedUEVersion = 508;
    // The function-flags reader decided once, as the offsets probe would: FunctionFlags at +0xB0, the tail after it.
    DynOff::UFUNCTION_FLAGS = 0xB0;
    DynOff::UFUNCTION_TAIL_EXTRA = 0;
    DynOff::UFUNCTION_TAIL_MEASURED.store(-1);
    DynOff::bUFunctionFlagsDetected.store(true);
    // UClass::Interfaces measured at +0xC0, so no lookup probes for it: every class here reads none unless it says so.
    DynOff::UCLASS_INTERFACES.store(0xC0);

    Obj(s_b[bClass], A(bClass), nClass, 0);
    Obj(s_b[bFunction], A(bClass), nFunction, 0);
    Klass(bObject, nObject, -1);
    Klass(bChannel, nChannel, bObject);
    Klass(bActorChannel, nActorChannel, bChannel, bChannelOnly);
    Klass(bActor, nActor, bObject, bK2Loc);
    Klass(bHero, nHero, bActor, bHeroOnly);
    Klass(bSidekick, nSidekick, bHero);
    Klass(bHeroComp, nHeroComp, bObject);
    Klass(bSwarm, nSwarm, bObject);
    Klass(bGameEngine, nGameEngine, bObject, -1, bGameViewport);
    Klass(bMany, nMany, bObject, bFn00);
    Fn(bChannelOnly, nChannelOnly, bActorChannel, -1);
    Fn(bK2Loc, nK2Loc, bActor, -1);
    Fn(bHeroOnly, nHeroOnly, bHero, -1);
    for (int i = 0; i < 15; ++i) Fn(bFn00 + i, nFn00 + i, bMany, bFn00 + i + 1);   // Fn14 -> the long name
    Fn(bLong, nLong, bMany, -1);
    P32(s_b[bGameViewport], DynOff::FFIELD_NAME, nGameViewport);
    P32(s_b[bGameViewport], DynOff::FPROPERTY_OFFSET, 0x100);

    const uint32_t cdo = Grimoire::RF_CLASS_DEFAULT_OBJECT, arch = Grimoire::RF_ARCHETYPE_OBJECT;
    Obj(s_b[bCdoActorChannel], A(bActorChannel), nCdoActorChannel, 0, cdo);
    Obj(s_b[bCdoActor], A(bActor), nCdoActor, 0, cdo);
    Obj(s_b[bCdoHero], A(bHero), nCdoHero, 0, cdo);
    Obj(s_b[bHeroCompTpl], A(bHeroComp), nHeroCompTpl, A(bCdoHero));        // a default subobject: a CDO is its outer
    Obj(s_b[bHeroCompScs], A(bHeroComp), nHeroCompScs, A(bHero), arch);     // a Blueprint component template
    Obj(s_b[bHero0], A(bHero), nHero0, 0);
    Obj(s_b[bHero1], A(bHero), nHero1, 0);
    Obj(s_b[bSidekick0], A(bSidekick), nSidekick0, 0);
    Obj(s_b[bHeroComp0], A(bHeroComp), nHeroComp0, A(bHero0));
    Obj(s_b[bHeroComp1], A(bHeroComp), nHeroComp1, A(bHero1));
    Obj(s_b[bCdoGameEngine], A(bGameEngine), nCdoGameEngine, 0, cdo);
    P64(s_b[bCdoGameEngine], 0x100, 0x7FF000001000ull);                      // a viewport, but a class default object
    Obj(s_b[bGameEngine0], A(bGameEngine), nGameEngine0, 0);
    P64(s_b[bGameEngine0], 0x100, 0x10000000ull);
    Obj(s_b[bMany0], A(bMany), nMany0, 0);
    for (int i = 0; i < kFlood; ++i) Obj(s_flood[i], A(bHeroComp), nHeroCompScs, A(bHero), arch);
    for (int i = 0; i < kSwarm; ++i) Obj(s_swarm[i], A(bSwarm), nSwarm0, 0);

    for (int b : { bCdoActorChannel, bCdoActor, bCdoHero, bHeroCompTpl, bHeroCompScs }) Add(A(b));
    for (int i = 0; i < kFlood; ++i) Add(reinterpret_cast<uintptr_t>(s_flood[i]));
    s_hero0Index = s_count;
    for (int b : { bHero0, bHero1, bSidekick0, bHeroComp0, bHeroComp1, bCdoGameEngine, bGameEngine0 }) Add(A(b));
    for (int i = 0; i < kSwarm; ++i) Add(reinterpret_cast<uintptr_t>(s_swarm[i]));
    Publish();
}
}  // namespace Fx

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

        // [R7-S9] Globals published under a fence that never drops are not "initialised": the gate asks UE5_Init
        // again while the fence is up -- bounded -- and then refuses, rather than run the command on a half-applied pool.
        g_cachedGObjects = g_cachedGNames = 0;
        g_initCalls = 0;
        g_godModeCalls.clear();
        g_initInProgress.store(true);
        g_onInit = [] { g_cachedGObjects = g_cachedGNames = 1; };
        Send(mb, Mimic::CMD_PROTECT, Mimic::PROTECT_OP_SET_GODMODE, 1);
        check("INITGATE ⭐: globals published while the fence stays up are not an init -- four more asks, then -10",
              g_initCalls == 5 && mb.result == -10 && g_godModeCalls.empty(), num(g_initCalls));
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
        // [DEBUGCAM-FORCEON-CDO] A toggle never goes to the CheatManager's class default object, so the command raises
        // no template warning; the dispatcher's clear is what leaves cmdOutFlags at 0.
        g_debugCameraRc = Mimic::DBGCAM_ERR_NO_LIVE_CHEATMANAGER;
        mb.cmdOutFlags = 0xFFFFFFFF;
        Send(mb, Mimic::CMD_SET_DEBUG_CAMERA, 1);
        check("DEBUGCAM -17: no live CheatManager says so, and no template bit", mb.result == -17
              && Msg(mb).find("no live CheatManager") != std::string::npos && mb.cmdOutFlags == 0, Msg(mb).c_str());
        g_debugCameraRc = -1;
        mb.cmdOutFlags = 0xFFFFFFFF;
        Send(mb, Mimic::CMD_SET_DEBUG_CAMERA, 1);
        check("DEBUGCAM -1: an unreadable state says so -- never that the CheatManager is missing", mb.result == -1
              && Msg(mb).find("unreadable state") != std::string::npos
              && Msg(mb).find("no live CheatManager") == std::string::npos && mb.cmdOutFlags == 0, Msg(mb).c_str());
        g_debugCameraRc = 1;
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

    // [FINDINSTANCE-SUBSTRING-USERFACING] [INVOKEINHERIT-2026-08-20] [LISTINSTANCES-KEEPS-TEMPLATES]
    // [FREEZESCOPE-2026-08-18] Lookups, listings and pointer queries over the reflection fixture: a user-facing class
    // name picks by derivation, a function is found on the class or its bases, and a pool a freeze writes to never
    // holds a template. paramsData is filled with 0xCC past the page index, so every byte a handler publishes is seen
    // written.
    Fx::Build();
    {   blk("FINDLIST - lookups pick by derivation, listings page, and a pool never holds a template");
        using namespace Fx;
        auto named = [&](int32_t cmd, const char* cls, const char* fn, uint64_t inst, uint64_t ufunc = 0,
                         uint32_t page = 0, uint32_t flags = 0) {
            snprintf(mb.className, sizeof mb.className, "%s", cls);
            snprintf(mb.funcName, sizeof mb.funcName, "%s", fn);
            memset(mb.paramsData, 0xCC, sizeof mb.paramsData);
            memcpy(mb.paramsData, &page, sizeof page);
            mb.cmdFlags = flags;
            Send(mb, cmd, inst, ufunc);
        };
        auto q = [&](size_t at) { uint64_t v; memcpy(&v, mb.paramsData + at, sizeof v); return v; };
        auto u16 = [&](size_t at) { uint16_t v; memcpy(&v, mb.paramsData + at, sizeof v); return v; };
        auto u32 = [&](size_t at) { uint32_t v; memcpy(&v, mb.paramsData + at, sizeof v); return v; };
        auto str = [&](size_t at) { return std::string(reinterpret_cast<const char*>(mb.paramsData + at)); };
        auto zeroFrom = [&](size_t from) {
            for (size_t i = from; i < sizeof mb.paramsData; ++i) if (mb.paramsData[i]) return false;
            return true;
        };
        auto nameOf = [](uint64_t o) { return o ? Ubel::GetName(static_cast<uintptr_t>(o)) : std::string("(0)"); };
        std::string shown;

        const auto w = Ubel::WalkFunctions(A(bHero));
        check("FINDLIST setup: the long name resolves, the array holds every object, and the class's one function reads "
              "non-zero metadata", Serie::GetString(nLong) == kLong && Aura::GetCount() == s_count && w.size() == 1
              && w[0].name == "HeroOnly" && w[0].parmsSize != 0 && w[0].numParms != 0 && w[0].functionFlags != 0);

        named(Mimic::CMD_FIND_INSTANCE, "Actor", "", 0xDEAD);
        shown = nameOf(mb.instanceAddr);
        check("FIND_INSTANCE ⭐: a base-class name finds a live object of a subclass, not a class default object whose "
              "name holds it", Answered(mb) && mb.result == 0 && mb.instanceAddr == A(bHero0) && Msg(mb).empty(),
              shown.c_str());
        named(Mimic::CMD_FIND_INSTANCE, "NoSuchClass_Xyz", "", 0xDEAD);
        char want[192];
        snprintf(want, sizeof want, "No instance of 'NoSuchClass_Xyz' found (scanned=%d)", Aura::GetCount());
        check("FIND_INSTANCE not found: -2, the address cleared, and how many objects were looked at",
              mb.result == -2 && mb.instanceAddr == 0 && Msg(mb) == want, Msg(mb).c_str());
        Tot::RequestShutdown();
        named(Mimic::CMD_FIND_INSTANCE, "Actor", "", 0xDEAD);
        Tot::ResetShutdown();
        check("FIND_INSTANCE cancelled: a shutdown cuts the walk short, and the answer says so", Answered(mb)
              && mb.result == -2 && mb.instanceAddr == 0
              && Msg(mb) == "Instance lookup for 'Actor' was cut short: the DLL is shutting down", Msg(mb).c_str());
        named(Mimic::CMD_FIND_INSTANCE, "", "", 0);
        check("FIND_INSTANCE empty name: refused -1", mb.result == -1 && Msg(mb) == "Empty class name", Msg(mb).c_str());

        mb.parmsSize = 0; mb.numParms = 0; mb.functionFlags = 0;
        named(Mimic::CMD_FIND_FUNCTION, "", "HeroOnly", A(bHero0));
        check("FIND_FUNCTION ⭐: the class's own function, with the metadata its walk reads", mb.result == 0
              && mb.ufuncAddr == A(bHeroOnly) && mb.parmsSize == w[0].parmsSize && mb.numParms == w[0].numParms
              && mb.functionFlags == w[0].functionFlags);
        named(Mimic::CMD_FIND_FUNCTION, "", "K2_GetActorLocation", A(bHero0));
        check("FIND_FUNCTION inherited ⭐: a function its base class declares is found through the super chain",
              mb.result == 0 && mb.ufuncAddr == A(bK2Loc), Msg(mb).c_str());
        named(Mimic::CMD_FIND_FUNCTION, "", "Nope", A(bHero0), 0xBEEF);
        check("FIND_FUNCTION not found: -3, a stale address cleared, and the class levels searched", mb.result == -3
              && mb.ufuncAddr == 0
              && Msg(mb) == "Function 'Nope' not found (3 class level(s) searched, own class + supers)", Msg(mb).c_str());
        named(Mimic::CMD_FIND_FUNCTION, "", "HeroOnly", 0);
        const bool nullInst = mb.result == -1 && Msg(mb) == "Instance address is null";
        named(Mimic::CMD_FIND_FUNCTION, "", "", A(bHero0));
        const bool emptyFn = mb.result == -1 && Msg(mb) == "Empty function name";
        named(Mimic::CMD_FIND_FUNCTION, "", "HeroOnly", A(bNoClass));
        check("FIND_FUNCTION refusals: no instance, no name, an object with no class", nullInst && emptyFn
              && mb.result == -2 && Msg(mb) == "Cannot read UClass from instance", Msg(mb).c_str());

        named(Mimic::CMD_LIST_FUNCTIONS, "", "", A(bHero0));
        check("LIST_FUNCTIONS by instance: the header, and the entry's address, metadata and name", mb.result == 0
              && mb.parmsSize == 1 && mb.numParms == 1 && mb.functionFlags == 1 && q(0) == A(bHeroOnly)
              && u16(8) == w[0].parmsSize && u16(10) == w[0].numParms && u32(12) == w[0].functionFlags
              && str(16) == "HeroOnly" && zeroFrom(64));
        named(Mimic::CMD_LIST_FUNCTIONS, "Actor", "", 0);
        shown = nameOf(mb.instanceAddr) + " / " + str(16);
        check("LIST_FUNCTIONS by class ⭐: the derivation pick is published, and its own class's functions listed",
              mb.result == 0 && mb.instanceAddr == A(bHero0) && mb.parmsSize == 1 && str(16) == "HeroOnly", shown.c_str());
        named(Mimic::CMD_LIST_FUNCTIONS, "", "", A(bMany0), 0, 0);
        bool page0 = mb.result == 0 && mb.parmsSize == 16 && mb.numParms == 15 && mb.functionFlags == 2;
        for (int i = 0; i < 15; ++i) {
            char fn[8];
            snprintf(fn, sizeof fn, "Fn%02d", i);
            page0 = page0 && q(static_cast<size_t>(i) * 64) == A(bFn00 + i) && str(static_cast<size_t>(i) * 64 + 16) == fn;
        }
        check("LIST_FUNCTIONS page 0: 15 entries of 64 bytes, 16 in all over 2 pages", page0 && zeroFrom(15 * 64));
        named(Mimic::CMD_LIST_FUNCTIONS, "", "", A(bMany0), 0, 1);
        check("LIST_FUNCTIONS page 1: the rest, a long name cut to 47 characters, and nothing after it",
              mb.numParms == 1 && mb.parmsSize == 16 && mb.functionFlags == 2 && q(0) == A(bLong)
              && str(16) == kLong.substr(0, 47) && zeroFrom(64), str(16).c_str());
        named(Mimic::CMD_LIST_FUNCTIONS, "", "", A(bMany0), 0, 2);
        check("LIST_FUNCTIONS past the end: no entries and a cleared buffer", Answered(mb) && mb.result == 0
              && mb.numParms == 0 && mb.parmsSize == 16 && mb.functionFlags == 2 && zeroFrom(0));
        named(Mimic::CMD_LIST_FUNCTIONS, "", "", 0);
        const bool noInput = mb.result == -1 && Msg(mb) == "No instance address or class name provided";
        named(Mimic::CMD_LIST_FUNCTIONS, "NoSuchClass_Xyz", "", 0);
        const bool noClass = mb.result == -2 && Msg(mb) == "No instance of 'NoSuchClass_Xyz' found";
        named(Mimic::CMD_LIST_FUNCTIONS, "", "", A(bNoClass));
        check("LIST_FUNCTIONS refusals: nothing named, a class with no instance, an object with no class", noInput
              && noClass && mb.result == -2 && Msg(mb) == "Cannot read UClass from instance", Msg(mb).c_str());
        Tot::RequestShutdown();
        named(Mimic::CMD_LIST_FUNCTIONS, "Actor", "", 0);
        Tot::ResetShutdown();
        check("LIST_FUNCTIONS cancelled: a shutdown cuts the lookup short, and the answer says so", mb.result == -2
              && Msg(mb) == "Instance lookup for 'Actor' was cut short: the DLL is shutting down", Msg(mb).c_str());

        named(Mimic::CMD_LIST_INSTANCES, "BP_Hero_C", "", 0xDEAD, 0xBEEF);
        check("LIST_INSTANCES exact: the class's live objects only -- no subclass, no class default object -- and the "
              "class witness", mb.result == 0 && mb.parmsSize == 2 && mb.numParms == 2 && mb.functionFlags == 1
              && mb.cmdOutFlags == 0 && q(0) == A(bHero0) && q(8) == A(bHero1) && zeroFrom(16)
              && mb.instanceAddr == A(bHero) && mb.ufuncAddr == static_cast<uint64_t>(Grimoire::OFF_UOBJECT_CLASS));
        named(Mimic::CMD_LIST_INSTANCES, "BP_Hero_C", "", 0xDEAD, 0xBEEF, 0, Mimic::LI_IN_DERIVED);
        check("LIST_INSTANCES derived ⭐: subclasses too, each entry with its own class, no page witness, the flag "
              "consumed", mb.result == 0 && mb.parmsSize == 3 && mb.numParms == 3 && q(0) == A(bHero0)
              && q(8) == A(bHero) && q(16) == A(bHero1) && q(24) == A(bHero) && q(32) == A(bSidekick0)
              && q(40) == A(bSidekick) && mb.instanceAddr == 0
              && mb.ufuncAddr == static_cast<uint64_t>(Grimoire::OFF_UOBJECT_CLASS) && mb.cmdFlags == 0);
        named(Mimic::CMD_LIST_INSTANCES, "HeroComp", "", 0, 0, 0);
        check("LIST_INSTANCES exact ⭐: the templates met first take no slot under the cap", mb.result == 0
              && mb.parmsSize == 2 && q(0) == A(bHeroComp0) && q(8) == A(bHeroComp1)
              && (mb.cmdOutFlags & Mimic::LI_OUT_TRUNCATED) == 0, nameOf(q(0)).c_str());
        named(Mimic::CMD_LIST_INSTANCES, "HeroComp", "", 0, 0, 0, Mimic::LI_IN_DERIVED);
        check("LIST_INSTANCES derived ⭐: ...nor under the derived cap", mb.result == 0 && mb.parmsSize == 2
              && q(0) == A(bHeroComp0) && q(16) == A(bHeroComp1) && (mb.cmdOutFlags & Mimic::LI_OUT_TRUNCATED) == 0,
              nameOf(q(0)).c_str());

        // A walk that let a template through: the hook turns an object the walk already took into an archetype, on the
        // walk's closing log line -- after it collected its results, before the pool is published.
        int fired = 0;
        g_onInfo = [&](const char* fmt) {
            if (std::string(fmt).rfind("FindInstancesByClass class=", 0) == 0) {
                P32(s_b[bHero1], Grimoire::OFF_UOBJECT_FLAGS, Grimoire::RF_ARCHETYPE_OBJECT);
                ++fired;
            }
        };
        named(Mimic::CMD_LIST_INSTANCES, "BP_Hero_C", "", 0, 0);
        g_onInfo = nullptr;
        P32(s_b[bHero1], Grimoire::OFF_UOBJECT_FLAGS, 0);
        check("LIST_INSTANCES post-filter ⭐: an object that is a template when the pool is published is left out",
              fired == 1 && mb.parmsSize == 1 && q(0) == A(bHero0), nameOf(q(8)).c_str());
        fired = 0;
        g_onInfo = [&](const char* fmt) {
            if (std::string(fmt).rfind("FindInstancesDerivedFrom base=", 0) == 0) {
                P32(s_b[bSidekick0], Grimoire::OFF_UOBJECT_FLAGS, Grimoire::RF_ARCHETYPE_OBJECT);
                ++fired;
            }
        };
        named(Mimic::CMD_LIST_INSTANCES, "BP_Hero_C", "", 0, 0, 0, Mimic::LI_IN_DERIVED);
        g_onInfo = nullptr;
        P32(s_b[bSidekick0], Grimoire::OFF_UOBJECT_FLAGS, 0);
        check("LIST_INSTANCES post-filter derived: ...in the derived scope too", fired == 1 && mb.parmsSize == 2
              && q(0) == A(bHero0) && q(16) == A(bHero1));

        named(Mimic::CMD_LIST_INSTANCES, "ActorChannel", "", 0xDEAD, 0xBEEF);
        check("LIST_INSTANCES nothing live: no witness -- a previous command's pointers are cleared", mb.result == 0
              && mb.parmsSize == 0 && mb.numParms == 0 && mb.functionFlags == 1 && mb.instanceAddr == 0
              && mb.ufuncAddr == 0);
        const uint32_t per = Mimic::ListInstancesPerPage(false);
        const uint32_t cap = static_cast<uint32_t>(Mimic::LIST_INSTANCES_MAX_EXACT);
        const uint32_t pages = (cap + per - 1) / per;
        named(Mimic::CMD_LIST_INSTANCES, "Swarm_C", "", 0, 0, 0);
        check("LIST_INSTANCES capped ⭐: the pool stops at the cap and says so", mb.parmsSize == cap
              && mb.numParms == per && mb.functionFlags == pages && (mb.cmdOutFlags & Mimic::LI_OUT_TRUNCATED) != 0);
        named(Mimic::CMD_LIST_INSTANCES, "Swarm_C", "", 0, 0, pages - 1);
        check("LIST_INSTANCES last page: what is left of the cap, its witness, and the cap still reported",
              mb.numParms == cap - (pages - 1) * per && q(0) == reinterpret_cast<uintptr_t>(s_swarm[(pages - 1) * per])
              && mb.instanceAddr == A(bSwarm) && (mb.cmdOutFlags & Mimic::LI_OUT_TRUNCATED) != 0);
        named(Mimic::CMD_LIST_INSTANCES, "", "", 0, 0, 0, Mimic::LI_IN_DERIVED);
        check("LIST_INSTANCES empty name: refused, and the scope flag consumed anyway", mb.result == -1
              && Msg(mb) == "Empty class name" && mb.cmdFlags == 0, Msg(mb).c_str());

        static uintptr_t s_worldSlot = 0x0000123456789AB0ull, s_engineSlot = 0x00004567890ABCD0ull;
        const uint64_t worldSlot = reinterpret_cast<uintptr_t>(&s_worldSlot);
        const uint64_t engineSlot = reinterpret_cast<uintptr_t>(&s_engineSlot);
        g_cachedGWorld = static_cast<uintptr_t>(worldSlot);
        g_cachedGEngine = static_cast<uintptr_t>(engineSlot);
        memset(mb.paramsData, 0xCC, sizeof mb.paramsData);
        Send(mb, Mimic::CMD_QUERY_PTR, Mimic::QUERY_OP_GWORLD);
        check("QUERY_PTR GWorld ⭐: the slot and the world it holds, the rest cleared", Answered(mb) && mb.result == 0
              && q(0) == worldSlot && q(8) == s_worldSlot && zeroFrom(16));
        g_cachedGWorld = 0;
        memset(mb.paramsData, 0xCC, sizeof mb.paramsData);
        Send(mb, Mimic::CMD_QUERY_PTR, Mimic::QUERY_OP_GWORLD);
        check("QUERY_PTR GWorld unresolved: -1 with its text, nothing published", mb.result == -1
              && Msg(mb) == "GWorld not resolved (no static slot / recovery failed)" && q(0) == 0 && q(8) == 0,
              Msg(mb).c_str());
        g_cachedGWorld = static_cast<uintptr_t>(worldSlot);
        Send(mb, Mimic::CMD_QUERY_PTR, Mimic::QUERY_OP_GENGINE_SLOT);
        const bool slotOk = mb.result == 0 && q(0) == engineSlot && q(8) == s_engineSlot;
        g_cachedGEngine = 0;
        Send(mb, Mimic::CMD_QUERY_PTR, Mimic::QUERY_OP_GENGINE_SLOT);
        check("QUERY_PTR GEngine slot: the slot and the engine it holds; unresolved, -1 with its text", slotOk
              && mb.result == -1 && Msg(mb) == "GEngine slot not resolved (no AOB validated)" && q(0) == 0,
              Msg(mb).c_str());
        Send(mb, Mimic::CMD_QUERY_PTR, Mimic::QUERY_OP_GAME_ENGINE);
        shown = nameOf(q(0));
        check("QUERY_PTR GameEngine ⭐: the live engine, not its class default object, with its class and its name",
              mb.result == 0 && q(0) == A(bGameEngine0) && q(8) == A(bGameEngine) && str(16) == "GameEngine",
              shown.c_str());
        P64(s_b[bGameEngine0], 0x100, 0);
        Send(mb, Mimic::CMD_QUERY_PTR, Mimic::QUERY_OP_GAME_ENGINE);
        P64(s_b[bGameEngine0], 0x100, 0x10000000ull);
        check("QUERY_PTR GameEngine, none live: -1 with its text", mb.result == -1
              && Msg(mb) == "GameEngine not found (no live UEngine in GObjects)" && q(0) == 0, Msg(mb).c_str());
        Send(mb, Mimic::CMD_QUERY_PTR, 7);
        check("QUERY_PTR unknown op: -1", Answered(mb) && mb.result == -1 && Msg(mb) == "QueryPtr: unknown op");
        g_cachedGObjects = g_cachedGNames = 0;
        Send(mb, Mimic::CMD_QUERY_PTR, Mimic::QUERY_OP_GWORLD);
        g_cachedGObjects = g_cachedGNames = 1;
        check("QUERY_PTR needs an init: it reads what a scan fills, so it is refused -10 without one",
              mb.result == -10 && Msg(mb) == "DLL not initialized", Msg(mb).c_str());
        g_cachedGWorld = 0;
        g_cachedGEngine = 0;
    }

    printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
    return g_fail == 0 ? 0 : 1;
}
