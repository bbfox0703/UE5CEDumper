// ============================================================
// Solitar — 索莉塔 (研究人類的大魔族 — Greater demon studying humanity)
// GodMode: force the local player pawn's AActor::bCanBeDamaged bitfield off.
//
// Mechanism (docs/godmode-spec.md §5.2): resolve the local pawn via the engine
// chain (GWorld → OwningGameInstance → LocalPlayers[0] → PlayerController →
// Pawn), then read-modify-write the single FBoolProperty bit of bCanBeDamaged —
// the exact technique Wirbel::ResolveCursorBit/SetMouseCursor uses for
// bShowMouseCursor, retargeted here. GodMode ON ⇒ bCanBeDamaged = FALSE, so all
// damage routed through UGameplayStatics::ApplyDamage (which gates on
// AActor::CanBeDamaged()) is dropped before TakeDamage runs.
//
// Pure memory write — NO UFunction invoke, NO game thread, so it works in menus
// too. A re-assert worker re-applies the bit on a short timer (write-on-drift)
// so it survives respawns / level changes. Self-contained (Path B): uses only
// public Ubel / Aura / Macht APIs + DynOff — no coupling to Wirbel.
//
// Every property name is on an engine base class (AActor / AController /
// UPlayer), so the lookup is game- and UE-version-agnostic. No cached instance
// pointers — the pawn is re-resolved on every operation and every re-assert tick
// (the stale-pointer crash class from the 2026-06-10 audit).
// ============================================================

#define LOG_CAT "WALK"
#include "Sein.h"
#include "Solitar.h"
#include "Grimoire.h"
#include "Macht.h"
#include "Aura.h"
#include "Tot.h"     // Tot::MarkBackgroundWorker — re-assert worker ignores per-command cancel (M4)
#include "Routine.h"   // Routine::ReassertLoop — shared sliced-sleep + guarded tick (R5/B14)
#include "Ubel.h"

#include <atomic>
#include <cctype>
#include <chrono>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

// &GWorld — deref once for UWorld* (defined in Frieren.cpp; same as Wirbel).
extern uintptr_t g_cachedGWorld;

namespace {

using namespace Solitar;  // ProtectResult codes

// Desired toggle — the source of truth, survives UI reconnects (markers/state
// live in the DLL while the game process lives). Atomic so the badge getters can
// read it without contending on s_mutex.
std::atomic<bool> s_wantGod{false};

// Serializes whole operations: handlers are reachable from BOTH the Fern pipe
// thread and the Mimic mailbox polling thread, plus the re-assert worker.
std::mutex s_mutex;

// Re-assert worker — separate control mutex so StopWorker()'s join() never runs
// while s_mutex is held (the worker locks s_mutex per tick → would deadlock).
Routine::SafeThread       s_worker;   // detaches at process exit, never terminates
std::mutex        s_workerMutex;
std::atomic<bool> s_workerStop{false};

// T2 generic scan: cached protection-bool targets for the current pawn class —
// bCanBeDamaged + every reflected invincibility bool matched by
// MatchProtectionBool. Rebuilt only when the resolved pawn class changes (the
// offsets/masks are class-stable, so the worker reuses them across ticks).
// Guarded by s_mutex (only touched inside ApplyGodNowLocked).
struct BoolTarget {
    int32_t     fieldOffset = 0;        // Offset_Internal of the property in the object
    BoolBits    bits;                   // where the value sits in its byte, and how it is read and written
    bool        protect = false;       // value written when GodMode is ON
    bool        isCanBeDamaged = false; // the canonical badge indicator
    std::string name;
};
std::vector<BoolTarget> s_targets;
uintptr_t               s_targetsClass = 0;

// ---- low-level reads (copied from Wirbel; public APIs only) ----

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

// ---- resolution chain (docs/godmode-spec.md §4-5.2) ----

// Resolve the LOCAL PlayerController: engine chain first, instance-scan fallback
// (prefer the PC whose UPlayer* Player field is non-null).
uintptr_t ResolveLocalPC(uintptr_t world) {
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

// When the project's Debug Camera is ON the LocalPlayer's controller IS the
// DebugCameraController (pawnless). Hop to OriginalControllerRef so GodMode
// targets the real gameplay pawn.
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

struct PawnRef {
    uintptr_t world = 0, pc = 0, pawn = 0, pawnClass = 0;
};

int32_t ResolvePawnRef(PawnRef& out) {
    out = PawnRef{};
    out.world = DerefWorld();
    if (!out.world) return PR_ERR_NOT_INIT;
    out.pc = ResolveLocalPC(out.world);
    if (!out.pc) return PR_ERR_NO_PAWN;
    out.pc = HopThroughDebugCamera(out.pc);
    out.pawn = ResolvePawn(out.pc);
    if (!out.pawn) return PR_ERR_NO_PAWN;
    out.pawnClass = Ubel::GetClass(out.pawn);
    if (!out.pawnClass) return PR_ERR_REFLECT;
    return PR_OK;
}

// Read an FBoolProperty's [FieldSize, ByteOffset, ByteMask, FieldMask] layout from
// the FProperty address (probed ±4/±8, same as Wirbel::ResolveCursorBit). FindField
// only surfaces FieldMask, so the within-byte offset must be read here directly.
// Which probed layout counts is Solitar::PickBoolBits.
bool ReadBoolLayout(uintptr_t fpropAddr, BoolBits& bits) {
    if (!fpropAddr) return false;
    int baseOff = DynOff::bUseFProperty ? DynOff::FBOOLPROP_FIELDSIZE
                                        : DynOff::UBOOLPROP_FIELDSIZE;
    const int tryOffs[] = { baseOff, baseOff - 4, baseOff + 4, baseOff + 8, baseOff - 8 };
    constexpr int kTries = static_cast<int>(sizeof(tryOffs) / sizeof(tryOffs[0]));
    uint8_t quads[kTries][4] = {};
    bool readOk[kTries] = {};
    for (int i = 0; i < kTries; ++i)
        readOk[i] = tryOffs[i] >= 0 && Macht::ReadBytesSafe(fpropAddr + tryOffs[i], quads[i], 4);
    return PickBoolBits(quads, readOk, kTries, bits);
}

// Resolve a reflected FBoolProperty (by name) down to a single byte address + its
// read/write masks on a live object. Generalized from Wirbel::ResolveCursorBit.
int32_t ResolveActorBoolBit(uintptr_t obj, uintptr_t classAddr,
                            const char* exact, const char* contains,
                            uintptr_t& byteAddr, BoolBits& bits) {
    if (!obj || !classAddr) return PR_ERR_REFLECT;
    FieldInfo fi{};
    if (!Ubel::FindField(classAddr, exact, contains, nullptr, "BoolProperty", fi)
        || !fi.Address)
        return PR_ERR_REFLECT;
    if (!ReadBoolLayout(fi.Address, bits)) return PR_ERR_REFLECT;
    byteAddr = obj + static_cast<uintptr_t>(fi.Offset) + bits.byteOff;
    return PR_OK;
}

std::string ToLower(const std::string& s) {
    std::string r = s;
    for (char& c : r) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return r;
}

// T2 generic scan: walk the pawn class (incl. inherited fields) for damage /
// invincibility FBoolProperty fields and cache them as protection targets.
// Universal keyword table (MatchProtectionBool) — no per-game config. Rebuilt
// only when the pawn class changes. Logs the discovered flags when verbose
// (so the user sees exactly what generic analysis found on their game).
void BuildTargets(uintptr_t pawnClass, bool verbose) {
    s_targets.clear();
    s_targetsClass = pawnClass;
    if (!pawnClass) return;
    const ClassInfo& ci = Ubel::WalkClassEx(pawnClass);
    for (const auto& f : ci.Fields) {
        if (f.TypeName != "BoolProperty" || !f.Address) continue;
        std::string lower = ToLower(f.Name);
        bool protect = false;
        if (!MatchProtectionBool(lower, protect)) continue;
        BoolBits bits;
        if (!ReadBoolLayout(f.Address, bits)) continue;
        BoolTarget t;
        t.fieldOffset = f.Offset;
        t.bits = bits;
        t.protect = protect;
        t.isCanBeDamaged = (lower.find("canbedamaged") != std::string::npos);
        t.name = f.Name;
        s_targets.push_back(std::move(t));
        if (s_targets.size() >= 16) break;   // sanity cap
    }
    if (verbose) {
        LOG_INFO("GodMode: scanned class 0x%llX ('%s') -> %zu protection bool(s)",
                 (unsigned long long)pawnClass, Ubel::GetName(pawnClass).c_str(),
                 s_targets.size());
        for (const auto& t : s_targets)
            LOG_INFO("GodMode:   '%s' @+0x%X mask=0x%02X protect=%d%s",
                     t.name.c_str(), t.fieldOffset, t.bits.fieldMask, t.protect ? 1 : 0,
                     t.isCanBeDamaged ? " [canonical]" : "");
    }
}

// Apply the desired GodMode state to the live pawn. Caller MUST hold s_mutex.
// Writes EVERY cached protection target (bCanBeDamaged + any invincibility bool
// the T2 scan matched on the pawn class): ON ⇒ each bool's "protect" value, OFF ⇒
// the inverse (its normal-gameplay value). Returns the OBSERVED godmode state
// (1 = immune, 0 = can be damaged) from the canonical bCanBeDamaged target, or a
// negative ProtectResult. verbose: log the scan + summary (once per explicit
// toggle). outDrifted (opt): set true when any target had drifted from the
// desired value on entry (the game reverted our write — a hint it uses
// value-based health, not these flags).
int32_t ApplyGodNowLocked(bool verbose, bool* outDrifted = nullptr) {
    if (outDrifted) *outDrifted = false;
    PawnRef p;
    int32_t rc = ResolvePawnRef(p);
    if (rc != PR_OK) return rc;

    // Rebuild the protection-target set when the pawn class changes (T2 scan).
    if (p.pawnClass != s_targetsClass)
        BuildTargets(p.pawnClass, verbose);
    if (s_targets.empty())
        return PR_ERR_REFLECT;   // not even bCanBeDamaged resolved

    bool desiredOn = s_wantGod.load();
    bool anyDrift = false;
    int32_t observed = desiredOn ? 1 : 0;   // fallback if no canonical target

    for (const auto& t : s_targets) {
        uintptr_t byteAddr = p.pawn + static_cast<uintptr_t>(t.fieldOffset) + t.bits.byteOff;
        uint8_t b = 0;
        if (!Macht::ReadSafe(byteAddr, b)) continue;
        bool want = desiredOn ? t.protect : !t.protect;   // ON = protect, OFF = normal
        if (ReadBoolValue(b, t.bits) != want) {
            anyDrift = true;
            uint8_t nb = WriteBoolValue(b, t.bits, want);
            if (!Macht::WriteBytes(byteAddr, &nb, 1)) continue;
        }
        if (t.isCanBeDamaged) {
            uint8_t after = 0;
            bool canDmg = Macht::ReadSafe(byteAddr, after) ? ReadBoolValue(after, t.bits)
                                                           : !desiredOn;
            observed = canDmg ? 0 : 1;   // godmode = NOT can-be-damaged
        }
    }
    if (outDrifted) *outDrifted = anyDrift;
    if (verbose)
        LOG_INFO("GodMode: applied %zu protection bool(s) on pawn=0x%llX class='%s' "
                 "(want=%d -> godmode=%d). If damage still applies, the game uses "
                 "value-based health — freeze HP in Value Search instead.",
                 s_targets.size(), (unsigned long long)p.pawn,
                 Ubel::GetName(p.pawnClass).c_str(), desiredOn ? 1 : 0, observed);
    return observed;
}

// ---- re-assert worker ----

void WorkerLoop() {
    // Sliced sleep, cancel-immunity, per-tick exception guard and the shutdown break
    // all live in Routine::ReassertLoop (R5 / B14). Only the tick is ours.
    int driftCount = 0;
    Routine::ReassertLoop("GodMode", Grimoire::PROTECT_REASSERT_MS, s_workerStop, [&] {
        std::lock_guard<std::mutex> lk(s_mutex);
        if (!s_wantGod.load()) return;     // toggled off between ticks
        bool drifted = false;
        ApplyGodNowLocked(false, &drifted);   // write-on-drift handled inside
        if (drifted) {
            ++driftCount;
            // Rate-limited: a game reverting the bit every frame would spam.
            // Drift = the game re-set bCanBeDamaged since our last tick → the
            // flag is almost certainly NOT what gates its damage.
            if (driftCount <= 5 || driftCount % 100 == 0)
                LOG_WARN("GodMode: re-asserted protection flag(s) (drift #%d) — the "
                         "game keeps re-setting them; it likely uses value-based "
                         "health, not these flags (use Value Search + Freeze on HP).",
                         driftCount);
        }
    });
}

// Start/stop the worker. The *Locked cores assume the caller already holds
// s_workerMutex, so SetGodMode can decide start-vs-stop under the SAME hold as the
// state mutate — concurrent ON/OFF (pipe SetGodMode + CE-mailbox CMD_PROTECT) then
// can't join a wanted worker or leave an orphan (audit #8 discipline, retrofitted). (L1)
void StartWorkerLocked() {
    if (Tot::ShutdownRequested()) return;   // don't (re)spawn during the shutdown window (M5)
    if (s_worker.joinable()) return;   // already running
    s_workerStop.store(false);
    s_worker = std::thread(WorkerLoop);
}
void StopWorkerLocked() {
    if (!s_worker.joinable()) return;
    s_workerStop.store(true);
    s_worker.join();   // s_workerMutex held; s_mutex must NOT be (worker locks it per tick)
}

} // namespace

namespace Solitar {

// Read-only companion to BuildTargets: resolve every matched protection bool on
// the current pawn as pawn-relative offsets, WITHOUT mutating s_targets or the
// toggle — so exporting a standalone trainer never disturbs a live GodMode.
int32_t ResolveProtectBits(std::vector<ProtectBit>& out) {
    out.clear();
    PawnRef p;
    int32_t rc = ResolvePawnRef(p);
    if (rc != PR_OK) return rc;
    const ClassInfo& ci = Ubel::WalkClassEx(p.pawnClass);
    for (const auto& f : ci.Fields) {
        if (f.TypeName != "BoolProperty" || !f.Address) continue;
        std::string lower = ToLower(f.Name);
        bool protect = false;
        if (!MatchProtectionBool(lower, protect)) continue;
        BoolBits bits;
        if (!ReadBoolLayout(f.Address, bits)) continue;
        ProtectBit b;
        b.name = f.Name;
        b.byteOffset = f.Offset + static_cast<int32_t>(bits.byteOff);
        b.mask = TrainerMaskFor(bits);
        b.protect = protect ? 1 : 0;
        out.push_back(std::move(b));
        if (out.size() >= 16) break;   // mirror BuildTargets sanity cap
    }
    return PR_OK;
}

int32_t SetGodMode(bool on) {
    // Hold s_workerMutex (outer) across the state mutate AND the start/stop decision, and
    // decide on the FINAL s_wantGod (not the `on` arg), so a concurrent ON/OFF from the
    // pipe + CE mailbox can't join a wanted worker or leave an orphan. s_mutex (inner) is
    // released before the start/stop — the worker locks s_mutex per tick, so joining
    // under it would deadlock. (L1)
    std::lock_guard<std::mutex> wlk(s_workerMutex);
    int32_t rc;
    {
        std::lock_guard<std::mutex> lk(s_mutex);
        s_wantGod.store(on);
        rc = ApplyGodNowLocked(/*verbose=*/true);
    }
    if (s_wantGod.load()) StartWorkerLocked();
    else                  StopWorkerLocked();
    LOG_INFO("GodMode: set %s -> rc=%d (want=%d)", on ? "ON" : "OFF", rc,
             s_wantGod.load() ? 1 : 0);
    return rc;
}

int32_t GetGodMode() {
    std::lock_guard<std::mutex> lk(s_mutex);
    PawnRef p;
    int32_t rc = ResolvePawnRef(p);
    if (rc != PR_OK) return rc;
    uintptr_t byteAddr = 0;
    BoolBits bits;
    rc = ResolveActorBoolBit(p.pawn, p.pawnClass, "bCanBeDamaged", "CanBeDamaged",
                             byteAddr, bits);
    if (rc != PR_OK) return rc;
    uint8_t b = 0;
    if (!Macht::ReadSafe(byteAddr, b)) return PR_ERR_REFLECT;
    return ReadBoolValue(b, bits) ? 0 : 1;   // set ⇒ can be damaged ⇒ godmode off
}

bool IsGodModeWanted() {
    return s_wantGod.load();
}

int32_t GetState(State& out) {
    out = State{};
    out.want = s_wantGod.load() ? 1 : 0;
    int32_t live = GetGodMode();   // locks s_mutex internally
    if (live < 0) {
        out.live = -1;
        out.resolvable = false;
    } else {
        out.live = static_cast<int8_t>(live);
        out.resolvable = true;
    }
    return PR_OK;
}

int32_t SetActorBool(uintptr_t obj, uintptr_t classAddr, const char* propName, bool on) {
    if (!obj || !classAddr || !propName) return PR_ERR_REFLECT;
    std::lock_guard<std::mutex> lk(s_mutex);
    uintptr_t byteAddr = 0;
    BoolBits bits;
    int32_t rc = ResolveActorBoolBit(obj, classAddr, propName, propName, byteAddr, bits);
    if (rc != PR_OK) return rc;
    uint8_t b = 0;
    if (!Macht::ReadSafe(byteAddr, b)) return PR_ERR_REFLECT;
    uint8_t nb = WriteBoolValue(b, bits, on);
    if (nb != b && !Macht::WriteBytes(byteAddr, &nb, 1)) return PR_ERR_WRITE;
    uint8_t after = 0;
    bool v = Macht::ReadSafe(byteAddr, after) ? ReadBoolValue(after, bits) : on;
    return v ? 1 : 0;
}

int32_t GetActorBool(uintptr_t obj, uintptr_t classAddr, const char* propName) {
    if (!obj || !classAddr || !propName) return PR_ERR_REFLECT;
    std::lock_guard<std::mutex> lk(s_mutex);
    uintptr_t byteAddr = 0;
    BoolBits bits;
    int32_t rc = ResolveActorBoolBit(obj, classAddr, propName, propName, byteAddr, bits);
    if (rc != PR_OK) return rc;
    uint8_t b = 0;
    if (!Macht::ReadSafe(byteAddr, b)) return PR_ERR_REFLECT;
    return ReadBoolValue(b, bits) ? 1 : 0;
}

void StopWorker() {
    std::lock_guard<std::mutex> lk(s_workerMutex);
    StopWorkerLocked();
}

} // namespace Solitar
