// ============================================================
// Dunste — ドゥンスト / Dünste (「蒸氣」 — vapors)
// Fly: no-gravity free 3D flight of the local pawn, keyboard-driven. Contract:
// Dunste.h.
//
// Force the CMC into MOVE_Flying via a raw enum-byte write held by a re-assert
// worker (write-on-drift, like Laufen), then drive the CMC Velocity FVector each
// ~60 Hz tick from the keyboard — the engine moves the WHOLE character with a
// proper transform refresh, so this is clean across games.
//
// VIEW-relative control: movement follows the camera (ControlRotation) yaw
// (read-only — the mouse owns the view). W flies where you look (ground plane),
// A/D strafe, Z/C move world-vertical.
//
// Two modes, IDENTICAL motion (velocity-drive); they differ only in collision:
//   • Collision (default): the CMC's flying sweep keeps world geometry — no
//     passing through walls. Pure raw Macht writes, no game thread (Path B).
//   • Noclip: additionally invoke AActor::SetActorEnableCollision(false) once (game
//     thread, Stark) so the sweep passes through walls; restored on off/disable.
//     (An earlier per-tick teleport noclip jittered / desynced the mesh / broke
//     input across games — velocity + collision-off is clean.) FF7Rebirth zeroes
//     Velocity every frame → neither mode moves it (authoritative movement system).
//
// Offsets resolved by FName (DynOff), UE4/UE5-agnostic; pawn re-resolved every tick.
// ============================================================

#define LOG_CAT "FLY"
#include "Sein.h"
#include "Dunste.h"
#include "Grimoire.h"
#include "Macht.h"
#include "Aura.h"
#include "Tot.h"     // Tot::MarkBackgroundWorker — fly worker ignores per-command cancel (M4)
#include "Routine.h"   // Routine::RunTickGuarded — a throw out of a thread proc is std::terminate (B14)
#include "Ubel.h"
#include "Stark.h"      // IsGameThreadResponsive — gate the Noclip teleport invoke

#include <algorithm>
#include <atomic>
#include <cctype>
#include <chrono>
#include <cmath>
#include <cstring>
#include <exception>
#include <mutex>
#include <thread>
#include <vector>

#include <windows.h>   // GetAsyncKeyState / GetForegroundWindow (foreground gate)
                       // (WIN32_LEAN_AND_MEAN / NOMINMAX come from the CMake defs)

// &GWorld — deref once for UWorld* (defined in Frieren.cpp; same as Laufen).
extern uintptr_t g_cachedGWorld;
// Game-thread ProcessEvent invoke (Stark). Only the Noclip path uses it — it must
// run on the game thread so UpdateComponentToWorld refreshes the render/physics
// transform (a raw RelativeLocation write does not → the pawn desyncs).
extern "C" int32_t UE5_CallProcessEventEx(uintptr_t instance, uintptr_t ufunc,
                                          uintptr_t params, uint32_t size);

namespace {

using namespace Dunste;  // FlyResult / Preset codes

// ---- desired state (guarded by s_mutex) ----
struct FlyState {
    bool      active       = false;
    bool      noclip       = false;  // fly through walls: velocity-drive + the actor's
                                     //   collision disabled (vs collision kept)
    double    speed        = Grimoire::FLY_SPEED_DEFAULT;
    int32_t   preset       = PRESET_WASD;
    uint8_t   baseMode     = 1;      // captured MovementMode to restore (default MOVE_Walking)
    bool      baseCaptured = false;
    uintptr_t capturedPawn = 0;      // pawn the base mode was captured from (respawn re-capture)
    bool      collisionOff = false;  // WE disabled the actor's collision (noclip)
    uintptr_t collisionPawn = 0;     // pawn the collision was disabled on (respawn re-apply)
    uint64_t  tick         = 0;      // worker tick counter (sampled diagnostics)
    int       driftCount   = 0;      // times we re-asserted MOVE_Flying (game fought us)
};
FlyState s_state;

// Serializes whole operations: reachable from the Fern pipe thread, the Mimic
// mailbox thread, AND the fly worker.
std::mutex s_mutex;

// Fly worker — separate control mutex so StopWorker()'s join() never runs while
// s_mutex is held (the worker locks s_mutex per tick → would deadlock).
Routine::SafeThread       s_worker;   // detaches at process exit, never terminates
std::mutex        s_workerMutex;
std::atomic<bool> s_workerStop{false};

// ---- key preset table: [preset][logical action] = Win32 virtual-key ----
enum Act { A_FWD, A_BACK, A_LEFT, A_RIGHT, A_YAW_L, A_YAW_R, A_UP, A_DOWN, A_COUNT };
const uint8_t kPresetKeys[PRESET_COUNT][A_COUNT] = {
    // FWD    BACK   LEFT   RIGHT  YAW_L  YAW_R  UP     DOWN
    { 'W',   'S',   'A',   'D',   'Q',   'E',   'Z',   'C'    },              // PRESET_WASD
    { VK_NUMPAD8, VK_NUMPAD2, VK_NUMPAD4, VK_NUMPAD6,
      VK_NUMPAD7, VK_NUMPAD9, VK_NUMPAD1, VK_NUMPAD3 },                       // PRESET_NUMPAD
    { VK_UP, VK_DOWN, VK_LEFT, VK_RIGHT,
      VK_INSERT, VK_DELETE, VK_PRIOR, VK_NEXT },                             // PRESET_ARROWS (PgUp/PgDn)
};

// ---- low-level reads (public Macht APIs only) ----

uintptr_t DerefWorld() {
    if (!g_cachedGWorld) return 0;
    uintptr_t w = 0;
    if (!Macht::ReadSafe(g_cachedGWorld, w)) return 0;
    return w;
}

uintptr_t ReadPtrAt(uintptr_t obj, int32_t off) {
    if (!obj || off < 0) return 0;
    uintptr_t v = 0;
    Macht::ReadSafe(obj + static_cast<uintptr_t>(off), v);
    return v;
}

// Read/write a 3-component FVector/FRotator honoring the reflected struct width
// (12B = 3×float / 24B = 3×double under LWC).
bool ReadVec3At(uintptr_t addr, int32_t structSize, double out[3]) {
    int cw = (structSize >= 24) ? 8 : 4;
    for (int i = 0; i < 3; ++i) {
        if (cw == 8) {
            double d = 0;
            if (!Macht::ReadSafe(addr + static_cast<uintptr_t>(i * 8), d)) return false;
            out[i] = d;
        } else {
            float f = 0;
            if (!Macht::ReadSafe(addr + static_cast<uintptr_t>(i * 4), f)) return false;
            out[i] = static_cast<double>(f);
        }
    }
    return true;
}

bool WriteVec3At(uintptr_t addr, int32_t structSize, const double v[3]) {
    int cw = (structSize >= 24) ? 8 : 4;
    for (int i = 0; i < 3; ++i) {
        if (cw == 8) {
            double d = v[i];
            if (!Macht::WriteBytes(addr + static_cast<uintptr_t>(i * 8), &d, 8)) return false;
        } else {
            float f = static_cast<float>(v[i]);
            if (!Macht::WriteBytes(addr + static_cast<uintptr_t>(i * 4), &f, 4)) return false;
        }
    }
    return true;
}

// ---- minimal game-thread UFunction invoke (Noclip teleport only) ----

// Case-insensitive name compare (UFunction / param names).
bool IEq(const std::string& a, const char* b) {
    size_t n = std::strlen(b);
    if (a.size() != n) return false;
    for (size_t i = 0; i < n; ++i)
        if (std::tolower(static_cast<unsigned char>(a[i])) !=
            std::tolower(static_cast<unsigned char>(b[i]))) return false;
    return true;
}

// Sane upper bound on a UFunction's ParmsSize — see Schlacht.cpp's note. A freed/
// reused UFunction during game teardown can read a garbage-huge parmsSize; sizing a
// std::vector by it then throws bad_alloc → from the worker thread, std::terminate
// (0xC0000409). Reject it here so InvokeSetCollision is a clean no-op instead.
constexpr int32_t kMaxSaneParmsSize = 0x4000;

// Find a UFunction by name, walking class → Super (engine setters live on base classes
// above the concrete pawn).
//
// This file had the ONLY correct super-chain climb in the tree; the two general resolvers
// were built straight on the per-class walker and could not see an inherited function at
// all ([INVOKEINHERIT-2026-08-20]). The climb has moved to Ubel::ResolveFunctionInChain so
// there is one copy rather than three. ⚠ One deliberate semantic change here: that helper
// prefers an EXACT name match anywhere in the chain over a case-insensitive one, where this
// used to take the first case-insensitive hit. It differs only when two functions in one
// chain have names differing purely in case, and preferring the exact one is the better
// answer. The parmsSize sanity gate stays — it is this caller's, not the resolver's.
bool FindFuncByName(uintptr_t classAddr, const char* name, FunctionInfo& out) {
    FunctionInfo hit;
    if (!Ubel::ResolveFunctionInChain(
            classAddr, name,
            [](uintptr_t cls) { return Ubel::WalkFunctions(cls); },
            [](uintptr_t cls, uintptr_t& super) {
                return Macht::ReadSafe(cls + static_cast<uintptr_t>(DynOff::USTRUCT_SUPER), super);
            },
            hit))
        return false;

    // A freed/reused UFunction during teardown can read a garbage-huge parmsSize; sizing a
    // std::vector by it then throws bad_alloc → from the worker thread, std::terminate.
    if (hit.parmsSize > kMaxSaneParmsSize) return false;
    out = hit;
    return true;
}

// Invoke AActor::SetActorEnableCollision(enable) on the game thread (Stark). Noclip
// = disable the actor's collision so the CMC's flying sweep passes through walls,
// while the (unchanged) velocity-drive moves the WHOLE character with a proper
// transform refresh — unlike a per-tick teleport, which only moved the root and
// jittered / desynced the mesh across games. Returns true if the setter was found.
bool InvokeSetCollision(uintptr_t pawn, bool enable) {
    if (!pawn) return false;
    FunctionInfo fi;
    if (!FindFuncByName(Ubel::GetClass(pawn), "SetActorEnableCollision", fi)) {
        // The setter is cooked out (heavily-stripped Shipping builds, e.g. TQ2) →
        // noclip can't disable collision here. Flight still works; it just can't
        // pass through walls. Surface it so a "noclip 無效" report is explained.
        LOG_WARN("Fly: SetActorEnableCollision NOT FOUND on pawn class — Noclip can't "
                 "disable collision on this game (flight still works, walls still block)");
        return false;
    }
    std::vector<uint8_t> buf((std::max<size_t>)(static_cast<size_t>(fi.parmsSize), size_t{1}), 0);
    for (const auto& p : fi.params)
        if (IEq(p.name, "bNewActorEnableCollision") && p.offset >= 0 && p.offset < (int)buf.size())
            buf[p.offset] = enable ? 1 : 0;
    UE5_CallProcessEventEx(pawn, fi.address,
                           reinterpret_cast<uintptr_t>(buf.data()),
                           static_cast<uint32_t>(buf.size()));
    LOG_INFO("Fly: SetActorEnableCollision(%d) invoked", enable ? 1 : 0);
    return true;
}

// ---- resolution chain (local pawn → CMC → PC; same shape as Laufen) ----
// allowScan gates the expensive FindInstancesByClass fallback: the one-shot
// enable path passes true; the ~60 Hz worker passes false (the pointer walk is
// cheap and the fallback would be a per-tick full-pool scan).

uintptr_t ResolveLocalPC(uintptr_t world, bool allowScan) {
    do {
        if (!world) break;
        uintptr_t worldClass = Ubel::GetClass(world);
        int32_t giOff = Ubel::FindFieldOffset(worldClass, "OwningGameInstance",
                                              "GameInstance", nullptr, "ObjectProperty");
        uintptr_t gi = ReadPtrAt(world, giOff);
        if (!gi) break;
        uintptr_t giClass = Ubel::GetClass(gi);
        int32_t lpOff = Ubel::FindFieldOffset(giClass, "LocalPlayers", "LocalPlayers",
                                              nullptr, "ArrayProperty");
        if (lpOff < 0) break;
        Macht::TArrayView arr;
        if (!Macht::ReadTArray(gi + static_cast<uintptr_t>(lpOff), arr) || arr.Count <= 0)
            break;
        uintptr_t lp = Macht::ReadTArrayElement(arr, 0);
        if (!lp) break;
        uintptr_t lpClass = Ubel::GetClass(lp);
        int32_t pcOff = Ubel::FindFieldOffset(lpClass, "PlayerController",
                                              "PlayerController", nullptr, "ObjectProperty");
        uintptr_t pc = ReadPtrAt(lp, pcOff);
        if (pc) return pc;
    } while (false);

    if (!allowScan) return 0;
    auto rset = Aura::FindInstancesByClass("PlayerController", false, 100);
    uintptr_t firstNonCdo = 0;
    for (const auto& r : rset.results) {
        if (!r.addr || r.name.find("Default__") != std::string::npos) continue;
        if (!firstNonCdo) firstNonCdo = r.addr;
        uintptr_t cls = Ubel::GetClass(r.addr);
        int32_t playerOff = Ubel::FindFieldOffset(cls, "Player", "Player",
                                                  "Controller", "ObjectProperty");
        if (playerOff >= 0 && ReadPtrAt(r.addr, playerOff))
            return r.addr;
    }
    return firstNonCdo;
}

uintptr_t HopThroughDebugCamera(uintptr_t pc) {
    if (!pc) return pc;
    uintptr_t cls = Ubel::GetClass(pc);
    std::string clsName = Ubel::GetName(cls);
    if (clsName.find("DebugCameraController") == std::string::npos) return pc;
    int32_t origOff = Ubel::FindFieldOffset(cls, "OriginalControllerRef",
                                            "OriginalController", nullptr, "ObjectProperty");
    uintptr_t orig = ReadPtrAt(pc, origOff);
    return orig ? orig : pc;
}

uintptr_t ResolvePawn(uintptr_t pc) {
    uintptr_t cls = Ubel::GetClass(pc);
    uintptr_t pawn = ReadPtrAt(pc, Ubel::FindFieldOffset(cls, "Pawn"));
    if (pawn) return pawn;
    return ReadPtrAt(pc, Ubel::FindFieldOffset(cls, "AcknowledgedPawn"));
}

// Resolved local pawn + CharacterMovement + RootComponent + the field locations
// the fly tick reads/writes. Re-resolved every tick (no cached pointers).
struct Ctx {
    uintptr_t world = 0, pc = 0, pawn = 0, cmc = 0, cmcClass = 0;
    int32_t   modeOff = -1;                 // CMC.MovementMode (1 byte)
    uintptr_t velAddr  = 0; int32_t velSize = 12;   // CMC.Velocity FVector
    uintptr_t root     = 0;                          // pawn.RootComponent (USceneComponent)
    uintptr_t relLocAddr = 0; int32_t relLocSize = 12; // RootComponent.RelativeLocation (noclip pos-drive)
    uintptr_t ctrlRotAddr = 0; int32_t ctrlRotSize = 12; // Controller.ControlRotation (camera-yaw basis)
    bool      attached  = false;            // root has an AttachParent (rel != world)
};

int32_t ResolveCtx(Ctx& c, bool allowScan) {
    c = Ctx{};
    c.world = DerefWorld();
    if (!c.world) return FR_ERR_NOT_INIT;
    c.pc = ResolveLocalPC(c.world, allowScan);
    if (!c.pc) return FR_ERR_NO_PAWN;
    c.pc = HopThroughDebugCamera(c.pc);
    c.pawn = ResolvePawn(c.pc);
    if (!c.pawn) return FR_ERR_NO_PAWN;
    uintptr_t pawnClass = Ubel::GetClass(c.pawn);
    if (!pawnClass) return FR_ERR_REFLECT;
    int32_t cmOff = Ubel::FindFieldOffset(pawnClass, "CharacterMovement",
                                          "CharacterMovement", nullptr, "ObjectProperty");
    c.cmc = ReadPtrAt(c.pawn, cmOff);
    if (!c.cmc) return FR_ERR_NO_CMC;
    c.cmcClass = Ubel::GetClass(c.cmc);
    if (!c.cmcClass) return FR_ERR_REFLECT;

    // MovementMode: TEnumAsByte<EMovementMode> (FByteProperty). Match by name
    // (any type) — the enum byte lives at the property offset.
    c.modeOff = Ubel::FindFieldOffset(c.cmcClass, "MovementMode",
                                      "MovementMode", "Custom", nullptr);
    // Velocity FVector on UMovementComponent (base of the CMC).
    FieldInfo fi{};
    if (Ubel::FindField(c.cmcClass, "Velocity", "Velocity", nullptr, "StructProperty", fi)
        && fi.Offset >= 0) {
        c.velAddr = c.cmc + static_cast<uintptr_t>(fi.Offset);
        c.velSize = fi.Size;
    }
    // RootComponent + its RelativeLocation FVector — where noclip position-drive
    // writes (== world position only when not attached).
    int32_t rootOff = Ubel::FindFieldOffset(pawnClass, "RootComponent",
                                            "RootComponent", nullptr, "ObjectProperty");
    c.root = ReadPtrAt(c.pawn, rootOff);
    if (c.root) {
        uintptr_t rootClass = Ubel::GetClass(c.root);
        FieldInfo rl{};
        if (Ubel::FindField(rootClass, "RelativeLocation", "RelativeLocation",
                            nullptr, "StructProperty", rl) && rl.Offset >= 0) {
            c.relLocAddr = c.root + static_cast<uintptr_t>(rl.Offset);
            c.relLocSize = rl.Size;
        }
        int32_t apOff = Ubel::FindFieldOffset(rootClass, "AttachParent", "AttachParent",
                                              nullptr, "ObjectProperty");
        c.attached = (apOff >= 0) && ReadPtrAt(c.root, apOff) != 0;
    }
    // ControlRotation FRotator on the Controller — the camera aim; its yaw is the
    // basis for view-relative movement (we only READ it; the mouse owns the view).
    FieldInfo cr{};
    if (Ubel::FindField(Ubel::GetClass(c.pc), "ControlRotation", nullptr, nullptr, nullptr, cr)
        && cr.Offset >= 0) {
        c.ctrlRotAddr = c.pc + static_cast<uintptr_t>(cr.Offset);
        c.ctrlRotSize = cr.Size;
    }
    if (c.modeOff < 0 || !c.velAddr) return FR_ERR_REFLECT;
    return FR_OK;
}

// True when the GAME window (any top-level window owned by this process) is
// foreground — so fly keys never fire while the user is tabbed to the dumper UI
// or another app.
bool GameIsForeground() {
    HWND fg = GetForegroundWindow();
    if (!fg) return false;
    DWORD pid = 0;
    GetWindowThreadProcessId(fg, &pid);
    return pid == GetCurrentProcessId();
}

bool KeyDown(uint8_t vk) {
    return (GetAsyncKeyState(vk) & 0x8000) != 0;
}

// Horizontal forward + right basis from the CAMERA yaw (degrees). View-relative:
// W flies where the camera looks (on the ground plane), Z/C are world vertical.
// UE convention: forward = (cosY, sinY, 0); right = (-sinY, cosY, 0).
void CameraBasis(double yawDeg, double fwd[3], double right[3]) {
    constexpr double kDeg2Rad = 3.14159265358979323846 / 180.0;
    double yaw = yawDeg * kDeg2Rad;
    double cy = std::cos(yaw), sy = std::sin(yaw);
    fwd[0]   = cy;  fwd[1]   = sy;  fwd[2]   = 0.0;
    right[0] = -sy; right[1] = cy;  right[2] = 0.0;
}

// ---- one fly tick (caller holds s_mutex; c already resolved MR_OK) ----
// When the actor's collision must change (noclip on/off, or a new pawn after
// respawn), sets outCollPawn + outCollEnable; the worker invokes
// SetActorEnableCollision OUTSIDE the lock (game thread).
void FlyTickLocked(const Ctx& c, double dtSec, uintptr_t& outCollPawn, int& outCollEnable) {
    (void)dtSec;
    outCollPawn = 0;
    ++s_state.tick;
    // Read the game's LIVE mode + velocity at the top of the tick — i.e. the state
    // the game left after processing our PREVIOUS write. If velNow stays ~0 while
    // we keep writing a big velocity, the game is overwriting us (FF7R class — its
    // movement is authoritative, so neither mode moves it).
    uint8_t liveMode = 0;
    bool haveMode = (c.modeOff >= 0) &&
                    Macht::ReadSafe(c.cmc + static_cast<uintptr_t>(c.modeOff), liveMode);
    double velNow[3] = {0, 0, 0};
    if (c.velAddr) ReadVec3At(c.velAddr, c.velSize, velNow);
    // Re-capture base MovementMode on pawn change (respawn).
    if (c.pawn != s_state.capturedPawn) {
        if (haveMode)
            s_state.baseMode = (liveMode == Grimoire::MOVE_FLYING) ? 1 /*Walking*/ : liveMode;
        s_state.baseCaptured = haveMode;
        s_state.capturedPawn = c.pawn;
    }
    // Hold MOVE_Flying against the game re-asserting its own mode (write-on-drift).
    if (haveMode && liveMode != Grimoire::MOVE_FLYING) {
        uint8_t fly = Grimoire::MOVE_FLYING;
        if (Macht::WriteBytes(c.cmc + static_cast<uintptr_t>(c.modeOff), &fly, 1))
            ++s_state.driftCount;
    }

    // View-relative basis from the camera (ControlRotation) yaw. The mouse owns
    // the view; we only READ it. Falls back to world axes if unresolved.
    double cr[3] = {0, 0, 0};
    double camYaw = (c.ctrlRotAddr && ReadVec3At(c.ctrlRotAddr, c.ctrlRotSize, cr)) ? cr[1] : 0.0;
    double fwd[3], right[3];
    CameraBasis(camYaw, fwd, right);

    // Sample keys only while the game is foreground; otherwise hover (no move).
    uint32_t keymask = 0;
    double move[3] = {0, 0, 0};
    if (GameIsForeground()) {
        const uint8_t* k = kPresetKeys[s_state.preset];
        auto held = [&](int a) {
            bool d = KeyDown(k[a]);
            if (d) keymask |= (1u << a);
            return d;
        };
        auto add = [&](const double v[3], double s) {
            move[0] += v[0] * s; move[1] += v[1] * s; move[2] += v[2] * s;
        };
        if (held(A_FWD))   add(fwd, 1.0);
        if (held(A_BACK))  add(fwd, -1.0);
        if (held(A_RIGHT)) add(right, 1.0);
        if (held(A_LEFT))  add(right, -1.0);
        if (held(A_UP))    move[2] += 1.0;   // world up
        if (held(A_DOWN))  move[2] -= 1.0;
        // Turn is the mouse (view-relative) — the yaw keys are intentionally unused.
    }

    // Normalize the combined direction so diagonals aren't faster.
    double len = std::sqrt(move[0]*move[0] + move[1]*move[1] + move[2]*move[2]);
    double dir[3] = {0, 0, 0};
    if (len > 1e-6) { dir[0] = move[0]/len; dir[1] = move[1]/len; dir[2] = move[2]/len; }

    // Motion is IDENTICAL in both modes: drive the CMC Velocity (the engine moves
    // the WHOLE character with a proper transform refresh — the reason velocity mode
    // is clean everywhere). No keys → zero velocity (hover, no gravity). Noclip only
    // additionally turns OFF the actor's collision (below) so the flying sweep passes
    // through walls.
    double vel[3] = { dir[0]*s_state.speed, dir[1]*s_state.speed, dir[2]*s_state.speed };
    WriteVec3At(c.velAddr, c.velSize, vel);

    // Collision should be OFF iff noclip is on. Emit an invoke request when the
    // desired state differs, or when the pawn changed while it should stay off
    // (respawn → the fresh pawn has collision on again).
    bool wantOff = s_state.noclip;
    if (wantOff != s_state.collisionOff ||
        (wantOff && c.pawn != s_state.collisionPawn)) {
        outCollPawn   = c.pawn;
        outCollEnable = wantOff ? 0 : 1;      // 0 = disable collision, 1 = restore
        // The state is committed by the WORKER, from the invoke's actual result — not
        // here. Committing optimistically made the record say "collision is off" even
        // when the invoke never ran, and the re-emit condition above reads that same
        // record, so nothing ever retried. Leaving it unchanged means a skipped or
        // failed invoke simply re-emits on the next tick. (B8)
    }

    // Sampled diagnostics: first tick after enable, then ~every 2s.
    if (s_state.tick == 1 || s_state.tick % 120 == 0) {
        LOG_INFO("Fly tick %llu: noclip=%d collOff=%d mode=%d(want 5) keys=0x%03X camYaw=%.0f "
                 "drift=%d velGameLeft=(%.0f,%.0f,%.0f) velWeSet=(%.0f,%.0f,%.0f)",
                 (unsigned long long)s_state.tick, s_state.noclip ? 1 : 0,
                 s_state.collisionOff ? 1 : 0, haveMode ? liveMode : -1, keymask, camYaw,
                 s_state.driftCount, velNow[0], velNow[1], velNow[2], vel[0], vel[1], vel[2]);
    }
}

// ---- fly worker ----
void WorkerLoop() {
    Tot::MarkBackgroundWorker();   // ignore per-command cancel; abort only on shutdown (M4)
    LOG_INFO("Fly: worker started (%d ms tick)", Grimoire::FLY_TICK_MS);
    const double dtSec = Grimoire::FLY_TICK_MS / 1000.0;
    bool warnedThrow = false;
    bool warnedCollDefer = false;   // rate-limit the "game thread not responding" line (B8)
    while (!s_workerStop.load()) {
        std::this_thread::sleep_for(std::chrono::milliseconds(Grimoire::FLY_TICK_MS));
        if (s_workerStop.load()) break;
        // Safety net (see Schlacht): a tick reads live game state + dispatches a
        // game-thread invoke; during the game's own shutdown that churns/frees, so a
        // read or allocation can throw. An exception escaping the thread entry is
        // std::terminate (0xC0000409) — the feature-on-then-close-game crash.
        try {
            Ctx c;
            uintptr_t collPawn = 0;
            int collEnable = 0;
            {
                std::lock_guard<std::mutex> lk(s_mutex);
                if (!s_state.active) continue;             // disabled between ticks
                if (ResolveCtx(c, false) != FR_OK) continue;  // pawn/CMC gone (menu) — retry
                FlyTickLocked(c, dtSec, collPawn, collEnable);
            }
            // The SetActorEnableCollision invoke (noclip on/off, or respawn re-apply)
            // runs OUTSIDE s_mutex (it blocks on the game thread) and only when the game
            // thread is live — else it would stall the worker for the full timeout on a
            // paused game.
            //
            // Commit the record from what ACTUALLY happened. A skipped invoke (paused /
            // backgrounded game thread) or a game with no SetActorEnableCollision leaves
            // the record alone, so the next tick re-emits — the retry the optimistic
            // commit silently removed. The "not found" case can never succeed, so it is
            // logged once by InvokeSetCollision and then rate-limited here rather than
            // re-invoked ~60x/s. (B8)
            if (collPawn) {
                const bool wantOff = (collEnable == 0);
                if (!Stark::IsGameThreadResponsive()) {
                    if (!warnedCollDefer) {
                        warnedCollDefer = true;
                        LOG_WARN("Fly: collision %s deferred — game thread not responding; "
                                 "retrying every tick until it does",
                                 wantOff ? "disable" : "restore");
                    }
                } else {
                    // Either the setter ran, or it is absent on this pawn class — and no
                    // amount of retrying fixes absent (InvokeSetCollision says so once).
                    // Commit in both cases so the tick stops re-emitting; the ONLY
                    // non-committing path is the deferred one above, which is exactly the
                    // one that must retry.
                    InvokeSetCollision(collPawn, !wantOff);
                    warnedCollDefer = false;
                    std::lock_guard<std::mutex> lk(s_mutex);
                    s_state.collisionOff  = wantOff;
                    s_state.collisionPawn = wantOff ? collPawn : 0;
                }
            }
        } catch (const std::exception& e) {
            if (!warnedThrow) { warnedThrow = true; LOG_WARN("Fly: tick threw (%s) — skipping (game tearing down?)", e.what()); }
        } catch (...) {
            if (!warnedThrow) { warnedThrow = true; LOG_WARN("Fly: tick threw (unknown) — skipping (game tearing down?)"); }
        }
    }
    LOG_INFO("Fly: worker stopped");
}

// Worker start/stop split (same discipline as Laufen): the join in
// StopWorkerLocked runs with s_mutex RELEASED (WorkerLoop takes s_mutex every
// tick → joining under it deadlocks); s_workerMutex serializes start vs stop.
void StartWorkerLocked() {
    if (Tot::ShutdownRequested()) return;   // don't (re)spawn during the shutdown window (M5)
    if (s_worker.joinable()) return;
    s_workerStop.store(false);
    s_worker = std::thread(WorkerLoop);
}

void StopWorkerLocked() {
    if (!s_worker.joinable()) return;
    s_workerStop.store(true);
    s_worker.join();
}

// ---- deferred collision restore (B8) ----
//
// Disabling Fly while the game thread is paused cannot re-enable the pawn's collision,
// and on an idle-when-unfocused title that is the COMMON case, not an edge one: the
// click that turns Fly off is in OUR window, which is what backgrounds the game and
// stops ProcessEvent — so IsGameThreadResponsive is false at exactly the moment we need
// it. A ghosted pawn then falls through the world with nothing tracking it. Poll for the
// thread to come back and restore the moment it does, i.e. the instant the user clicks
// back into the game. Same shape as Schlacht::PendingRestoreLoop.
Routine::SafeThread       s_pendingWorker;   // detaches at process exit, never terminates
std::atomic<bool> s_pendingStop{false};
std::atomic<bool> s_pendingRunning{false};

void PendingRestoreLoop() {
    Tot::MarkBackgroundWorker();   // shutdown-only cancel, like every re-assert worker (M4)
    LOG_INFO("Fly: waiting for the game thread to resume so the pawn's collision can be "
             "restored");
    int waited = 0;
    bool warnedThrow = false;
    bool done = false;
    while (!done) {
        std::this_thread::sleep_for(
            std::chrono::milliseconds(Grimoire::PENDING_RESTORE_TICK_MS));
        waited += Grimoire::PENDING_RESTORE_TICK_MS;
        if (s_pendingStop.load() || Tot::ShutdownRequested()) break;

        // Guarded for the same reason as Schlacht's twin: this can poll for 5 minutes
        // against a game the user simply closed (Tot::ShutdownRequested() is not set on
        // a plain exit), and a throw out of a thread entry is std::terminate. (B14)
        Routine::RunTickGuarded("Fly", warnedThrow, [&] {
            // Re-enabled, or someone else already restored: nothing left to do.
            uintptr_t pawn = 0;
            {
                std::lock_guard<std::mutex> lk(s_mutex);
                if (s_state.active || !s_state.collisionOff) { done = true; return; }
                pawn = s_state.collisionPawn;
            }
            if (!pawn) { done = true; return; }

            if (!Stark::IsGameThreadResponsive()) {
                if (waited >= Grimoire::PENDING_RESTORE_MAX_MS) {
                    LOG_WARN("Fly: gave up waiting for the game thread (%d s) — the pawn's "
                             "collision stays OFF until Fly is enabled and disabled again "
                             "with the game running",
                             Grimoire::PENDING_RESTORE_MAX_MS / 1000);
                    done = true;
                }
                return;
            }

            InvokeSetCollision(pawn, true);
            {
                std::lock_guard<std::mutex> lk(s_mutex);
                s_state.collisionOff  = false;
                s_state.collisionPawn = 0;
            }
            LOG_INFO("Fly: game thread resumed after %d ms — pawn collision restored", waited);
            done = true;
        });
    }
    s_pendingRunning.store(false);
}

void StartPendingLocked() {   // s_workerMutex held
    if (s_pendingRunning.load()) return;                     // already waiting
    if (s_pendingWorker.joinable()) s_pendingWorker.join();  // reap a finished one
    if (Tot::ShutdownRequested()) return;                    // M5 spawn chokepoint
    s_pendingStop.store(false);
    s_pendingRunning.store(true);
    s_pendingWorker = std::thread(PendingRestoreLoop);
}

void StopPendingLocked() {    // s_workerMutex held; never called FROM the worker
    if (!s_pendingWorker.joinable()) return;
    s_pendingStop.store(true);
    s_pendingWorker.join();
}

} // namespace

namespace Dunste {

int32_t SetEnabled(bool enable) {
    // Hold s_workerMutex across the whole mutate + start/stop decision (Laufen
    // audit #8): lock order s_workerMutex (outer) → s_mutex (inner).
    std::lock_guard<std::mutex> wlk(s_workerMutex);
    // Either direction supersedes a deferred collision restore: enable takes the pawn
    // over itself, and a fresh disable re-decides. Joined here so exactly one path ever
    // owns collisionOff/collisionPawn. (B8, Schlacht shape)
    StopPendingLocked();
    if (enable) {
        int32_t rc;
        {
            std::lock_guard<std::mutex> lk(s_mutex);
            Ctx c;
            rc = ResolveCtx(c, true);   // one-shot: allow the scan fallback
            if (rc != FR_OK) return rc;
            uint8_t liveMode = 0;
            if (c.modeOff >= 0 &&
                Macht::ReadSafe(c.cmc + static_cast<uintptr_t>(c.modeOff), liveMode)) {
                s_state.baseMode = (liveMode == Grimoire::MOVE_FLYING) ? 1 : liveMode;
                s_state.baseCaptured = true;
            }
            s_state.capturedPawn = c.pawn;
            s_state.active = true;
            s_state.tick = 0;
            s_state.driftCount = 0;
            uint8_t fly = Grimoire::MOVE_FLYING;
            if (c.modeOff >= 0)
                Macht::WriteBytes(c.cmc + static_cast<uintptr_t>(c.modeOff), &fly, 1);
            LOG_INFO("Fly: ENABLED (baseMode=%u, speed=%.0f, preset=%d)",
                     s_state.baseMode, s_state.speed, s_state.preset);
        }
        StartWorkerLocked();   // s_workerMutex held, s_mutex released
        return 1;
    }

    // Disable: restore the captured MovementMode + zero velocity, stop worker.
    // If we disabled the actor's collision (noclip), restore it — do the invoke
    // OUTSIDE s_mutex (game thread).
    //
    // Clear `active` FIRST, then join, THEN decide about collision. The worker's tick
    // early-outs on !active, so after the join nothing can re-disable collision behind
    // the restore below. The old order (decide → restore → join) let an in-flight tick
    // turn collision back off after we had just turned it on. (B8, Schlacht M1 shape.)
    uintptr_t restoreCollPawn = 0;
    {
        std::lock_guard<std::mutex> lk(s_mutex);
        if (s_state.active) {
            Ctx c;
            if (ResolveCtx(c, false) == FR_OK) {
                if (c.modeOff >= 0 && s_state.baseCaptured) {
                    uint8_t base = s_state.baseMode;
                    Macht::WriteBytes(c.cmc + static_cast<uintptr_t>(c.modeOff), &base, 1);
                }
                double zero[3] = {0, 0, 0};
                if (c.velAddr) WriteVec3At(c.velAddr, c.velSize, zero);
            }
        }
        s_state.active = false;
        s_state.baseCaptured = false;
        s_state.capturedPawn = 0;
    }
    StopWorkerLocked();   // join with s_mutex released — no tick can race the restore
    {
        std::lock_guard<std::mutex> lk(s_mutex);
        // Prefer the pawn the collision was actually disabled ON. Re-resolving can hand
        // back a *different* (respawned) pawn, and re-enabling collision on that one
        // leaves the original permanently ghosted.
        if (s_state.collisionOff)
            restoreCollPawn = s_state.collisionPawn;
    }
    if (restoreCollPawn) {
        if (Stark::IsGameThreadResponsive()) {
            InvokeSetCollision(restoreCollPawn, true);   // re-enable collision
            std::lock_guard<std::mutex> lk(s_mutex);
            s_state.collisionOff  = false;
            s_state.collisionPawn = 0;
        } else {
            // The pawn is non-colliding and we cannot fix that right now. Wiping the
            // record here is what made the pawn fall through the world: nothing tracked
            // it, and re-enabling Fly without Noclip never restored it either. KEEP the
            // record and poll for the game thread — the restore then lands the instant
            // the user clicks back into the game. (B8; Schlacht's PendingRestoreLoop is
            // the shipped precedent for exactly this.)
            LOG_WARN("Fly: DISABLED but the pawn's collision is still OFF (game thread "
                     "unresponsive) — waiting for it to resume to restore it");
            StartPendingLocked();
            return 0;
        }
    }
    LOG_INFO("Fly: DISABLED");
    return 0;
}

int32_t SetSpeed(double uuPerSec) {
    std::lock_guard<std::mutex> lk(s_mutex);
    s_state.speed = (std::max)(Grimoire::FLY_SPEED_MIN,
                               (std::min)(Grimoire::FLY_SPEED_MAX, uuPerSec));
    return FR_OK;
}

int32_t SetPreset(int32_t preset) {
    if (preset < 0 || preset >= PRESET_COUNT) return FR_ERR_REFLECT;
    std::lock_guard<std::mutex> lk(s_mutex);
    s_state.preset = preset;
    return FR_OK;
}

int32_t SetNoclip(bool enable) {
    std::lock_guard<std::mutex> lk(s_mutex);
    s_state.noclip = enable;
    LOG_INFO("Fly: noclip = %d", enable ? 1 : 0);
    return FR_OK;
}

int32_t GetStatus(FlyStatus& out) {
    out = FlyStatus{};
    std::lock_guard<std::mutex> lk(s_mutex);
    out.active = s_state.active;
    out.noclip = s_state.noclip;
    out.preset = s_state.preset;
    out.speed  = s_state.speed;
    Ctx c;
    int32_t rc = ResolveCtx(c, false);
    out.code = rc;
    if (rc != FR_OK) return rc;
    out.hasCmc  = true;
    out.cmcAddr = c.cmc;
    uint8_t liveMode = 0;
    if (c.modeOff >= 0 &&
        Macht::ReadSafe(c.cmc + static_cast<uintptr_t>(c.modeOff), liveMode)) {
        out.modeResolved = true;
        out.currentMode  = liveMode;
    }
    return FR_OK;
}

bool IsActive() {
    std::lock_guard<std::mutex> lk(s_mutex);
    return s_state.active;
}

void StopWorker() {
    std::lock_guard<std::mutex> lk(s_workerMutex);
    StopWorkerLocked();
    // UE5_Shutdown joins every worker before unload; a still-waiting deferred restore
    // would outlive the DLL. Tot::RequestShutdown() is already latched by then, so the
    // loop is on its way out anyway — this just makes the join deterministic. (B8)
    StopPendingLocked();
}

} // namespace Dunste
