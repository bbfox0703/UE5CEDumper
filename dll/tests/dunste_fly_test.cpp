// dunste_fly_test.cpp
// UE5CEDumper — Fly / Noclip (Dunste.cpp) over a fake world, with a fake game-thread dispatcher.
//
// WHY A TEST EXECUTABLE OF ITS OWN
//   Dunste.cpp keeps file-local copies of the pawn chain (DerefWorld, ResolvePawn, ...) that Solitar.cpp keeps too, so
//   it cannot join dll_core_test's single translation unit. It is compiled here as its own unit and linked against
//   ue5_testcore, the same core files built as a library.
//
// WHAT IS FAKED
//   * The world: GWorld -> OwningGameInstance -> LocalPlayers[0] -> PlayerController -> Pawn -> CharacterMovement,
//     built from reflection data in this process, as dll_core_test's GODMODE builds it.
//   * The game thread: Stark::IsGameThreadResponsive answers what the test says, and UE5_CallProcessEventEx records
//     each call and returns the code the test chose. On 0 it does what SetActorEnableCollision does in a game: it
//     sets the pawn's bActorEnableCollision to the call's argument, so the plugin's own read sees the result.
//   * No keys: the test process owns no window, so Fly never samples the keyboard and every tick writes a hover.

#include <windows.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>   // getenv_s: the AddressSanitizer build's negative control
#include <atomic>
#include <chrono>
#include <cstring>
#include <functional>
#include <mutex>
#include <string>
#include <thread>
#include <tuple>      // std::ignore
#include <vector>

#include "Dunste.h"
#include "Grimoire.h"
#include "Serie.h"
#include "Ubel.h"

// ── the externals ue5_testcore and Dunste.cpp leave to the test ─────────────────────────────────────────────────

namespace Sein {
void Info(const char*, const char*, ...) {}
void Error(const char*, const char*, ...) {}
void Warn(const char*, const char*, ...) {}
void Debug(const char*, const char*, ...) {}
void Summary(const char*, ...) {}
}  // namespace Sein

static std::atomic<bool> g_responsive{true};
namespace Stark {
void SetInvokeTimeoutMs(int32_t) {}
bool IsGameThreadResponsive(int32_t) { return g_responsive.load(); }
}  // namespace Stark

uint32_t g_cachedUEVersion = 0;
uintptr_t g_cachedGWorld = 0;
std::atomic<bool> g_initInProgress{false};

struct Invoke { uintptr_t instance; uintptr_t ufunc; std::vector<uint8_t> params; };
static std::mutex g_invokesMu;
static std::vector<Invoke> g_invokes;
static std::atomic<int32_t> g_invokeRc{0};
static uint8_t* g_collisionByte = nullptr;   // the pawn's bActorEnableCollision byte
static constexpr uint8_t kCollisionBit = 0x02;

extern "C" int32_t UE5_CallProcessEventEx(uintptr_t instance, uintptr_t ufunc, uintptr_t params, uint32_t size) {
    std::lock_guard<std::mutex> lk(g_invokesMu);
    Invoke rec{ instance, ufunc, {} };
    if (params && size) rec.params.assign(reinterpret_cast<const uint8_t*>(params),
                                          reinterpret_cast<const uint8_t*>(params) + size);
    g_invokes.push_back(rec);
    const int32_t rc = g_invokeRc.load();
    if (rc == 0 && g_collisionByte && !rec.params.empty())   // the game runs SetActorEnableCollision
        *g_collisionByte = static_cast<uint8_t>(rec.params[0] ? (*g_collisionByte | kCollisionBit)
                                                              : (*g_collisionByte & ~kCollisionBit));
    return rc;
}

// ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────────

static int g_pass = 0, g_fail = 0;
static void check(const char* label, bool cond, const char* got = nullptr) {
    if (cond) { ++g_pass; }
    else { ++g_fail; printf("  FAIL  %s%s%s\n", label, got ? "   got: " : "", got ? got : ""); }
}
static void blk(const char* name) { printf("- %s\n", name); }

// Poll a condition for up to `ms`: the worker ticks every FLY_TICK_MS and a sleep on Windows is coarse.
static bool waitFor(const std::function<bool()>& cond, int ms) {
    const auto end = std::chrono::steady_clock::now() + std::chrono::milliseconds(ms);
    while (std::chrono::steady_clock::now() < end) {
        if (cond()) return true;
        Sleep(5);
    }
    return cond();
}
static size_t invokeCount() { std::lock_guard<std::mutex> lk(g_invokesMu); return g_invokes.size(); }
static Invoke lastInvoke() { std::lock_guard<std::mutex> lk(g_invokesMu); return g_invokes.empty() ? Invoke{} : g_invokes.back(); }

int main() {
    setvbuf(stdout, nullptr, _IONBF, 0);
    printf("dunste_fly_test — Fly / Noclip over a fake world and a fake game thread\n");
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

    // ── the fake world ──────────────────────────────────────────────────────────────────────────────────────────
    static uint8_t names[23][0x40] = {};
    const char* text[23] = { "", "ObjectProperty", "ArrayProperty", "BoolProperty", "ByteProperty", "StructProperty",
                             "OwningGameInstance", "LocalPlayers", "PlayerController", "Pawn", "CharacterMovement",
                             "MovementMode", "Velocity", "bActorEnableCollision", "FlyWorld", "FlyGameInstance",
                             "FlyLocalPlayer", "FlyPlayerController", "FlyPawn", "FlyMovement", "Function",
                             "SetActorEnableCollision", "bNewActorEnableCollision" };
    static uintptr_t chunk[24] = {};
    for (int i = 1; i < 23; ++i) {
        memcpy(names[i] + 0x10, text[i], strlen(text[i]) + 1);
        chunk[i] = reinterpret_cast<uintptr_t>(names[i]);
    }
    static uintptr_t chunks[2] = { reinterpret_cast<uintptr_t>(chunk), 0 };
    Serie::InitUE4(reinterpret_cast<uintptr_t>(chunks), 0x10);
    DynOff::bUseFProperty = true;

    auto A     = [](const void* p) { return reinterpret_cast<uintptr_t>(p); };
    auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
    auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
    auto put64 = [](uint8_t* b, int off, uint64_t v)  { memcpy(b + off, &v, sizeof(v)); };
    static uint8_t fc[6][0x20] = {};
    auto fclass = [&](int nameIdx) { put32(fc[nameIdx], DynOff::FFIELDCLASS_NAME, nameIdx); return A(fc[nameIdx]); };
    auto prop = [&](uint8_t* p, int fcIdx, int nameIdx, int32_t off, int32_t size, uint8_t* next) {
        putP(p, DynOff::FFIELD_CLASS, fclass(fcIdx));
        put32(p, DynOff::FFIELD_NAME, nameIdx);
        put32(p, DynOff::FPROPERTY_OFFSET, off);
        put32(p, DynOff::FPROPERTY_ELEMSIZE, size);
        put32(p, DynOff::FPROPERTY_ELEMSIZE - 4, 1);
        putP(p, DynOff::FFIELD_NEXT, A(next));
    };
    auto boolLayout = [](uint8_t* p, uint8_t byteMask, uint8_t fieldMask) {
        const int at = DynOff::FBOOLPROP_FIELDSIZE;
        p[at] = 1; p[at + 1] = 0; p[at + 2] = byteMask; p[at + 3] = fieldMask;
    };
    auto cls = [&](uint8_t* c, int nameIdx, uint8_t* firstProp, int32_t size) {
        put32(c, Grimoire::OFF_UOBJECT_NAME, nameIdx);
        put32(c, DynOff::USTRUCT_PROPSSIZE, size);
        putP(c, DynOff::USTRUCT_CHILDPROPS, A(firstProp));
    };

    static uint8_t pOgi[0x100] = {}, pLps[0x100] = {}, pPc[0x100] = {}, pPawn[0x100] = {};
    static uint8_t pCmc[0x100] = {}, pColl[0x100] = {}, pMode[0x100] = {}, pVel[0x100] = {};
    static uint8_t cWorld[0x100] = {}, cGi[0x100] = {}, cLp[0x100] = {}, cPc[0x100] = {}, cPawn[0x100] = {};
    static uint8_t cCmc[0x100] = {};
    prop(pOgi, 1, 6, 0x30, 8, nullptr);      cls(cWorld, 14, pOgi, 0x38);
    prop(pLps, 2, 7, 0x30, 16, nullptr);     cls(cGi, 15, pLps, 0x40);
    prop(pPc, 1, 8, 0x30, 8, nullptr);       cls(cLp, 16, pPc, 0x38);
    prop(pPawn, 1, 9, 0x30, 8, nullptr);     cls(cPc, 17, pPawn, 0x38);
    prop(pCmc, 1, 10, 0x30, 8, pColl);
    prop(pColl, 3, 13, 0x40, 1, nullptr);    boolLayout(pColl, kCollisionBit, kCollisionBit);
    cls(cPawn, 18, pCmc, 0x48);
    prop(pMode, 4, 11, 0x30, 1, pVel);
    prop(pVel, 5, 12, 0x40, 12, nullptr);    cls(cCmc, 19, pMode, 0x4C);

    // AActor::SetActorEnableCollision(bool bNewActorEnableCollision), in the pawn class's function list.
    static uint8_t fnClass[0x40] = {}, fn[0x100] = {}, fnParam[0x100] = {};
    put32(fnClass, Grimoire::OFF_UOBJECT_NAME, 20);                     // "Function"
    putP(fn, Grimoire::OFF_UOBJECT_CLASS, A(fnClass));
    put32(fn, Grimoire::OFF_UOBJECT_NAME, 21);
    putP(fn, DynOff::USTRUCT_CHILDPROPS, A(fnParam));
    prop(fnParam, 3, 22, 0, 1, nullptr);     boolLayout(fnParam, 0x01, 0xFF);
    put64(fnParam, DynOff::FPROPERTY_FLAGS, 0x0080ull | 0x0008001040000200ull);   // CPF_Parm, plain data
    putP(cPawn, DynOff::USTRUCT_CHILDREN, A(fn));

    static uint8_t world[0x60] = {}, gi[0x60] = {}, lp[0x60] = {}, pc[0x60] = {}, pawn[0x60] = {}, cmc[0x60] = {};
    static uintptr_t localPlayers[1] = {};
    putP(world, Grimoire::OFF_UOBJECT_CLASS, A(cWorld));
    putP(gi,    Grimoire::OFF_UOBJECT_CLASS, A(cGi));
    putP(lp,    Grimoire::OFF_UOBJECT_CLASS, A(cLp));
    putP(pc,    Grimoire::OFF_UOBJECT_CLASS, A(cPc));
    putP(pawn,  Grimoire::OFF_UOBJECT_CLASS, A(cPawn));
    putP(cmc,   Grimoire::OFF_UOBJECT_CLASS, A(cCmc));
    putP(world, 0x30, A(gi));
    localPlayers[0] = A(lp);
    putP(gi, 0x30, A(localPlayers)); put32(gi, 0x38, 1); put32(gi, 0x3C, 1);   // TArray {Data, Num, Max}
    putP(lp, 0x30, A(pc));
    putP(pc, 0x30, A(pawn));
    pawn[0x40] = 0xFF;                       // eight packed bools, bActorEnableCollision (0x02) among them: on
    g_collisionByte = &pawn[0x40];
    static uintptr_t gworldSlot = 0;
    gworldSlot = A(world);

    auto mode = [&]() { return cmc[0x30]; };
    auto velocity = [&](int i) { float f = 0; memcpy(&f, cmc + 0x40 + i * 4, 4); return f; };
    auto setVelocity = [&](float x, float y, float z) { const float v[3] = { x, y, z }; memcpy(cmc + 0x40, v, 12); };
    auto collisionOn = [&]() { return (pawn[0x40] & kCollisionBit) != 0; };
    char got[96];
    auto num = [&](long long v) { snprintf(got, sizeof got, "%lld", v); return got; };
    const int tick = Grimoire::FLY_TICK_MS, pendingTick = Grimoire::PENDING_RESTORE_TICK_MS;

    {   blk("REFUSE - no world, no CharacterMovement: Fly refuses and starts nothing");
        g_cachedGWorld = 0;
        int32_t rc = Dunste::SetEnabled(true);
        check("REFUSE: with no world, the DLL is not ready", rc == Dunste::FR_ERR_NOT_INIT, num(rc));
        check("REFUSE: ...and Fly is not active", !Dunste::IsActive());
        g_cachedGWorld = A(&gworldSlot);
        rc = Dunste::SetEnabled(true);
        check("REFUSE: a pawn with no CharacterMovement is refused", rc == Dunste::FR_ERR_NO_CMC, num(rc));
        check("REFUSE: ...and Fly is not active", !Dunste::IsActive());
        putP(pawn, 0x30, A(cmc));
    }

    {   blk("ENABLE - the pawn flies, the mode is held, and with no key held it hovers");
        cmc[0x30] = 1;                       // MOVE_Walking
        setVelocity(50.0f, 60.0f, 70.0f);
        const int32_t rc = Dunste::SetEnabled(true);
        check("ENABLE: returns 1", rc == 1, num(rc));
        check("ENABLE ⭐: MovementMode is MOVE_Flying", mode() == Grimoire::MOVE_FLYING, num(mode()));
        check("ENABLE: Fly is active", Dunste::IsActive());
        check("ENABLE ⭐: with no key held the worker writes a hover -- zero velocity",
              waitFor([&] { return velocity(0) == 0 && velocity(1) == 0 && velocity(2) == 0; }, 40 * tick));
        cmc[0x30] = 1;                       // the game puts the pawn back on its feet
        check("ENABLE ⭐: the worker flies it again", waitFor([&] { return mode() == Grimoire::MOVE_FLYING; }, 40 * tick),
              num(mode()));
        Dunste::FlyStatus st;
        check("ENABLE: the status resolves", Dunste::GetStatus(st) == Dunste::FR_OK);
        check("ENABLE: ...active, with a CharacterMovement, flying", st.active && st.hasCmc && st.currentMode == 5);
        check("ENABLE: ...and the pawn's collision is read as it is: on", st.collision == 1, num(st.collision));
    }

    {   blk("NOCLIP - collision goes off through SetActorEnableCollision on the game thread, once");
        g_invokeRc.store(0);
        Dunste::SetNoclip(true);
        check("NOCLIP: the worker asks the game thread", waitFor([&] { return invokeCount() >= 1; }, 40 * tick));
        const Invoke inv = lastInvoke();
        check("NOCLIP ⭐: SetActorEnableCollision(false) on the pawn",
              inv.instance == A(pawn) && inv.ufunc == A(fn) && !inv.params.empty() && inv.params[0] == 0);
        check("NOCLIP: the pawn's collision is off", !collisionOn());
        Dunste::FlyStatus st;
        std::ignore = Dunste::GetStatus(st);
        check("NOCLIP ⭐: the status reports what happened -- collision off", st.noclip && st.collision == 0,
              num(st.collision));
        const size_t n = invokeCount();
        Sleep(static_cast<DWORD>(20 * tick));
        check("NOCLIP: an applied change is recorded, not asked again every tick", invokeCount() == n,
              num(static_cast<long long>(invokeCount() - n)));
    }

    {   blk("DISABLE - the captured mode comes back, the velocity is zeroed, collision is restored");
        setVelocity(5.0f, 5.0f, 5.0f);
        const int32_t rc = Dunste::SetEnabled(false);
        check("DISABLE: returns 0", rc == 0, num(rc));
        check("DISABLE ⭐: MovementMode is the captured MOVE_Walking again", mode() == 1, num(mode()));
        check("DISABLE: the velocity is zeroed", velocity(0) == 0 && velocity(1) == 0 && velocity(2) == 0);
        const Invoke inv = lastInvoke();
        check("DISABLE ⭐: SetActorEnableCollision(true) on the pawn the collision was turned off on",
              inv.instance == A(pawn) && !inv.params.empty() && inv.params[0] == 1);
        check("DISABLE: the pawn collides again", collisionOn());
        check("DISABLE: Fly is not active", !Dunste::IsActive());
    }

    {   blk("DEFERRED - a disable while the game thread is stalled keeps the record and restores when it answers");
        check("setup: Fly on with Noclip", Dunste::SetEnabled(true) == 1);
        check("setup: collision off", waitFor([&] { return !collisionOn(); }, 40 * tick));
        g_responsive.store(false);
        const size_t n = invokeCount();
        const int32_t rc = Dunste::SetEnabled(false);
        check("DEFERRED: the disable returns 0", rc == 0, num(rc));
        check("DEFERRED: nothing is sent to a stalled game thread", invokeCount() == n);
        Sleep(static_cast<DWORD>(3 * pendingTick));
        check("DEFERRED ⭐: the pawn is still non-colliding -- and the record is kept", !collisionOn() && invokeCount() == n);
        g_responsive.store(true);
        check("DEFERRED ⭐: once the game thread answers, the collision is restored",
              waitFor([&] { return collisionOn(); }, 8 * pendingTick));
    }

    {   blk("REARM - an enable that is refused re-arms the restore it superseded");
        check("setup: Fly on with Noclip", Dunste::SetEnabled(true) == 1);
        check("setup: collision off", waitFor([&] { return !collisionOn(); }, 40 * tick));
        g_responsive.store(false);
        check("setup: the disable defers its restore", Dunste::SetEnabled(false) == 0 && !collisionOn());
        putP(pawn, 0x30, 0);                 // the CharacterMovement is gone: the next enable is refused
        const int32_t rc = Dunste::SetEnabled(true);
        check("REARM: the enable is refused", rc == Dunste::FR_ERR_NO_CMC, num(rc));
        g_responsive.store(true);
        check("REARM ⭐: the restore it stopped is armed again and lands once the game thread answers",
              waitFor([&] { return collisionOn(); }, 8 * pendingTick));
        putP(pawn, 0x30, A(cmc));
    }

    {   blk("QUEUED - a call the game thread queued is recorded, so the next toggle undoes it");
        g_invokeRc.store(-5);
        const size_t n = invokeCount();
        check("setup: Fly on with Noclip", Dunste::SetEnabled(true) == 1);
        check("QUEUED: the disable is sent", waitFor([&] { return invokeCount() > n; }, 40 * tick));
        const size_t sent = invokeCount();
        Sleep(static_cast<DWORD>(20 * tick));
        check("QUEUED ⭐: a queued call is not sent again -- it will run", invokeCount() == sent,
              num(static_cast<long long>(invokeCount() - sent)));
        g_invokeRc.store(0);
        pawn[0x40] = static_cast<uint8_t>(pawn[0x40] & ~kCollisionBit);   // the queued call ran
        check("QUEUED: the disable returns 0", Dunste::SetEnabled(false) == 0);
        const Invoke inv = lastInvoke();
        check("QUEUED ⭐: the disable undoes it -- SetActorEnableCollision(true)", !inv.params.empty() && inv.params[0] == 1);
        check("QUEUED: the pawn collides again", collisionOn());
    }

    {   blk("REFUSED - a refused call changes nothing and is retried, backing off");
        g_invokeRc.store(-8);
        const size_t n = invokeCount();
        check("setup: Fly on with Noclip", Dunste::SetEnabled(true) == 1);
        check("REFUSED: the disable is sent", waitFor([&] { return invokeCount() > n; }, 40 * tick));
        const size_t first = invokeCount();
        Sleep(static_cast<DWORD>(20 * tick));
        check("REFUSED ⭐: it backs off -- not asked again every tick", invokeCount() == first,
              num(static_cast<long long>(invokeCount() - first)));
        check("REFUSED: the pawn still collides", collisionOn());
        g_invokeRc.store(0);
        check("REFUSED ⭐: after the back-off it is asked again, and lands",
              waitFor([&] { return !collisionOn(); }, 200 * tick));
        check("REFUSED: the disable returns 0", Dunste::SetEnabled(false) == 0);
        check("REFUSED: the pawn collides again", collisionOn());
    }

    {   blk("KNOBS - speed is clamped to the slider's range, a preset must exist");
        std::ignore = Dunste::SetSpeed(1.0);
        Dunste::FlyStatus st;
        std::ignore = Dunste::GetStatus(st);
        check("KNOBS: a speed below the floor is the floor", st.speed == Grimoire::FLY_SPEED_MIN);
        std::ignore = Dunste::SetSpeed(1.0e9);
        std::ignore = Dunste::GetStatus(st);
        check("KNOBS: a speed above the ceiling is the ceiling", st.speed == Grimoire::FLY_SPEED_MAX);
        check("KNOBS: a preset that does not exist is refused", Dunste::SetPreset(99) == Dunste::FR_ERR_REFLECT);
        check("KNOBS: an existing one is taken", Dunste::SetPreset(Dunste::PRESET_NUMPAD) == Dunste::FR_OK);
    }

    Dunste::StopWorker();
    printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
    return g_fail == 0 ? 0 : 1;
}
