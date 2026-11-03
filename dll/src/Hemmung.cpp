// ============================================================
// Hemmung — ヘムング ("inhibition", mist→energy mage)
// TimeDilation: hold AWorldSettings::TimeDilation (global) + per-pawn
// AActor::CustomTimeDilation at an absolute value via a write-on-drift
// re-assert worker. Contract: Hemmung.h.
//
// Structurally the absolute-value sibling of Laufen (MovementTuning): same
// s_mutex + s_workerMutex two-lock split, same LWC-width-aware reflected float
// read/write, same worker (write-on-drift, base capture, owner-change
// re-capture). Self-contained (Path B): only public Ubel/Aura/Macht + DynOff.
// ============================================================

#define LOG_CAT "WALK"
#include "Sein.h"
#include "Hemmung.h"
#include "Grimoire.h"
#include "Macht.h"
#include "Aura.h"
#include "Tot.h"     // Tot::MarkBackgroundWorker — re-assert worker ignores per-command cancel (M4)
#include "Routine.h"   // Routine::ReassertLoop — shared sliced-sleep + guarded tick (R5/B14)
#include "Ubel.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <mutex>
#include <string>
#include <thread>

// &GWorld — deref once for UWorld* (defined in Frieren.cpp; same as Laufen/Solitar).
extern uintptr_t g_cachedGWorld;

namespace {

using namespace Hemmung;  // TimeResult / DilTarget

// Per-target desired state — survives UI reconnects (the override lives in the
// DLL while the game process lives). Guarded by s_mutex.
struct DilState {
    bool      active        = false; // override engaged (worker holding it)
    double    value         = 1.0;   // desired ABSOLUTE dilation (1.0 = normal)
    double    base          = 1.0;   // captured natural value (restored on reset)
    uintptr_t capturedOwner = 0;     // owner the base was captured from (re-capture on change)
};
DilState s_dils[DIL_COUNT];

// Serializes whole operations: reachable from the Fern pipe thread, the Mimic
// mailbox thread, AND the re-assert worker.
std::mutex s_mutex;

// Re-assert worker — separate control mutex so StopWorker()'s join() never runs
// while s_mutex is held (the worker locks s_mutex per tick → would deadlock).
Routine::SafeThread       s_worker;   // detaches at process exit, never terminates
std::mutex        s_workerMutex;
std::atomic<bool> s_workerStop{false};

// ---- low-level reads (copied from Laufen; public APIs only) ----

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

// Read/write a scalar float field honoring the reflected width (4B float / 8B
// double under LWC). Width comes from FieldInfo.Size — never assumed.
bool ReadFloatAt(uintptr_t addr, int32_t size, double& out) {
    if (size >= 8) {
        double d = 0;
        if (!Macht::ReadSafe(addr, d)) return false;
        out = d;
        return true;
    }
    float f = 0;
    if (!Macht::ReadSafe(addr, f)) return false;
    out = static_cast<double>(f);
    return true;
}

bool WriteFloatAt(uintptr_t addr, int32_t size, double v) {
    if (size >= 8) {
        double d = v;
        return Macht::WriteBytes(addr, &d, 8);
    }
    float f = static_cast<float>(v);
    return Macht::WriteBytes(addr, &f, 4);
}

// ---- resolution chains ----

// Resolve the active AWorldSettings: reflected chain GWorld→PersistentLevel→
// WorldSettings first (gives EXACTLY the persistent level's settings, correct
// across subclasses), then an Aura::FindInstancesDerivedFrom("WorldSettings")
// instance-scan fallback (substring match catches game subclasses; first
// non-CDO).
uintptr_t ResolveWorldSettings(uintptr_t world) {
    if (world) {
        uintptr_t worldClass = Ubel::GetClass(world);
        int32_t levelOff = Ubel::FindFieldOffset(worldClass, "PersistentLevel",
                                                 "PersistentLevel", nullptr, "ObjectProperty");
        uintptr_t level = ReadPtrAt(world, levelOff);
        if (level) {
            uintptr_t levelClass = Ubel::GetClass(level);
            int32_t wsOff = Ubel::FindFieldOffset(levelClass, "WorldSettings",
                                                  "WorldSettings", nullptr, "ObjectProperty");
            uintptr_t ws = ReadPtrAt(level, wsOff);
            if (ws) return ws;
        }
    }
    // Fallback: the first live AWorldSettings, by derivation -- a game's subclass may be named without the word, and
    // a class merely named with it (PCG's node settings) has no TimeDilation. Never a template stand-in: the
    // re-assert worker would write TimeDilation into the default the next level's settings are built from, so not
    // FindLiveOrDefaultOf; the walk itself passes over templates. [FINDINSTANCE-INTERNAL-SUBSTRING-FALLBACKS]
    for (const auto& r : Aura::FindInstancesDerivedFrom(Grimoire::TIME_WORLDSETTINGS_CLASS,
                                                        Grimoire::TIME_WORLDSETTINGS_SCAN_MAX).results)
        if (r.addr) return r.addr;
    return 0;
}

// Resolve the local PlayerController: engine chain first, instance-scan fallback
// (prefer the PC whose UPlayer* Player field is non-null). Identical to
// Laufen/Solitar.
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

    // [FINDINSTANCE-INTERNAL-SUBSTRING-FALLBACKS] By derivation, the local player's controller first.
    return Aura::ScanForLocalPlayerController().pc;
}

uintptr_t HopThroughDebugCamera(uintptr_t pc) {
    if (!pc) return pc;
    uintptr_t cls = Ubel::GetClass(pc);
    // [FINDINSTANCE-INTERNAL-SUBSTRING-FALLBACKS] Derivation: a game's debug camera may be named without the word.
    if (!Aura::ClassDerivesFromAny(cls, {Grimoire::AURA_USERPICK_DCC_CLASS})) return pc;
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

// Resolved float field location for one dilation target on the live owner.
struct DilLoc {
    uintptr_t   owner  = 0;
    uintptr_t   addr   = 0;
    int32_t     offset = -1;
    int32_t     size   = 4;
    std::string name;
};

// Resolve a target's owner + reflected float field this instant. Returns a
// TimeResult (TR_OK / TR_ERR_NOT_INIT / TR_ERR_NO_TARGET / TR_ERR_REFLECT).
int32_t ResolveDilField(int32_t target, DilLoc& f) {
    f = DilLoc{};
    uintptr_t world = DerefWorld();
    if (!world) return TR_ERR_NOT_INIT;

    uintptr_t owner = 0, ownerClass = 0;
    const char* fieldName = nullptr;
    if (target == DIL_GLOBAL) {
        owner = ResolveWorldSettings(world);
        if (!owner) return TR_ERR_NO_TARGET;
        ownerClass = Ubel::GetClass(owner);
        fieldName = "TimeDilation";           // exact wins over MatineeTimeDilation
    } else if (target == DIL_PAWN) {
        uintptr_t pc = ResolveLocalPC(world);
        if (!pc) return TR_ERR_NO_TARGET;
        pc = HopThroughDebugCamera(pc);
        owner = ResolvePawn(pc);
        if (!owner) return TR_ERR_NO_TARGET;
        ownerClass = Ubel::GetClass(owner);
        fieldName = "CustomTimeDilation";
    } else {
        return TR_ERR_REFLECT;
    }
    if (!ownerClass) return TR_ERR_REFLECT;

    FieldInfo fi{};
    if (!Ubel::FindField(ownerClass, fieldName, fieldName, nullptr, "FloatProperty", fi)
        || fi.Offset < 0)
        return TR_ERR_REFLECT;
    f.owner  = owner;
    f.addr   = owner + static_cast<uintptr_t>(fi.Offset);
    f.offset = fi.Offset;
    f.size   = fi.Size;
    f.name   = fi.Name;
    return TR_OK;
}

bool AnyActiveLocked() {
    for (int i = 0; i < DIL_COUNT; ++i)
        if (s_dils[i].active) return true;
    return false;
}

// Apply one target's desired ABSOLUTE value to its live owner (caller holds
// s_mutex). Write-on-drift: only writes when the live value differs from the
// held value. Re-captures the restore base on owner change (level/respawn).
void ApplyDilLocked(int32_t target, bool* drifted) {
    DilState& d = s_dils[target];
    if (!d.active) return;
    DilLoc f;
    if (ResolveDilField(target, f) != TR_OK) return;   // owner/field gone this tick
    double current = 0;
    if (!ReadFloatAt(f.addr, f.size, current)) return;
    // Re-capture the natural base when the owner changes (a new WorldSettings on
    // level load, or a respawned pawn): that owner's value is untouched by us.
    // Unlike Laufen the base is not a multiplicand here — it is what Reset RESTORES,
    // so capturing 0 means Reset leaves the game permanently frozen. Fall back to the
    // engine default rather than store a value we know is unusable. (B22)
    if (f.owner != d.capturedOwner) {
        d.base = (current > 0.0 && std::isfinite(current)) ? current : 1.0;
        d.capturedOwner = f.owner;
    }
    double eps = (std::max)(1e-4, std::fabs(d.value) * 1e-5);
    if (std::fabs(current - d.value) > eps) {
        if (WriteFloatAt(f.addr, f.size, d.value) && drifted) *drifted = true;
    }
}

// Fill a DilationInfo for the UI from the live owner + stored desired state.
void FillDilLocked(int32_t target, DilationInfo& info) {
    const DilState& d = s_dils[target];
    info.value  = d.value;
    info.active = d.active;
    info.base   = d.base;
    DilLoc f;
    if (ResolveDilField(target, f) == TR_OK) {
        double cur = 0;
        if (ReadFloatAt(f.addr, f.size, cur)) {
            info.resolved    = true;
            info.current     = cur;
            info.ownerAddr   = f.owner;
            info.fieldOffset = f.offset;
            info.fieldName   = f.name;
        }
    }
}

// ---- re-assert worker (identical discipline to Laufen) ----

void WorkerLoop() {
    // Sliced sleep, cancel-immunity, per-tick exception guard and the shutdown break
    // all live in Routine::ReassertLoop (R5 / B14). Only the tick is ours.
    int driftCount = 0;
    Routine::ReassertLoop("Time", Grimoire::TIME_REASSERT_MS, s_workerStop, [&] {
        std::lock_guard<std::mutex> lk(s_mutex);
        if (!AnyActiveLocked()) return;   // all levers reset between ticks
        bool drifted = false;
        for (int i = 0; i < DIL_COUNT; ++i)
            ApplyDilLocked(i, &drifted);
        if (drifted) {
            ++driftCount;
            if (driftCount <= 5 || driftCount % 100 == 0)
                LOG_WARN("Time: re-asserted dilation (drift #%d) — the game keeps "
                         "recomputing it (slow-mo ability / Sequencer time track); the "
                         "override is being held against it.", driftCount);
        }
    });
}

// Worker start/stop "Locked" cores (caller ALREADY holds s_workerMutex). Lock
// order is always s_workerMutex (outer) → s_mutex (inner); the join in
// StopWorkerLocked runs with s_mutex RELEASED because WorkerLoop takes s_mutex
// every tick (joining under it deadlocks). Identical to Laufen.
void StartWorkerLocked() {
    if (Tot::ShutdownRequested()) return;   // don't (re)spawn during the shutdown window (M5)
    if (s_worker.joinable()) return;   // already running
    s_workerStop.store(false);
    s_worker = std::thread(WorkerLoop);
}

void StopWorkerLocked() {
    if (!s_worker.joinable()) return;
    s_workerStop.store(true);
    s_worker.join();
}

} // namespace

namespace Hemmung {

int32_t GetSnapshot(Snapshot& out) {
    out = Snapshot{};
    std::lock_guard<std::mutex> lk(s_mutex);
    // Always surface the stored desired state so the UI can render "active" even
    // when the owner momentarily doesn't resolve (menu / loading).
    for (int i = 0; i < DIL_COUNT; ++i) {
        out.dils[i].value  = s_dils[i].value;
        out.dils[i].active = s_dils[i].active;
        out.dils[i].base   = s_dils[i].base;
    }
    // Resolve each target independently — a failure on one (no pawn) shouldn't
    // blank the other (global). code reflects the global-target resolution.
    int32_t firstCode = TR_OK;
    for (int i = 0; i < DIL_COUNT; ++i) {
        DilLoc probe;
        int32_t rc = ResolveDilField(i, probe);
        if (i == DIL_GLOBAL) firstCode = rc;
        if (rc == TR_OK) FillDilLocked(i, out.dils[i]);
    }
    out.code = firstCode;
    return firstCode;
}

int32_t GetDilation(int32_t target, DilationInfo& out) {
    out = DilationInfo{};
    if (target < 0 || target >= DIL_COUNT) return TR_ERR_REFLECT;
    std::lock_guard<std::mutex> lk(s_mutex);
    out.value  = s_dils[target].value;
    out.active = s_dils[target].active;
    out.base   = s_dils[target].base;
    DilLoc f;
    int32_t rc = ResolveDilField(target, f);
    if (rc != TR_OK) return rc;
    FillDilLocked(target, out);
    return TR_OK;
}

int32_t SetDilation(int32_t target, double value) {
    if (target < 0 || target >= DIL_COUNT) return TR_ERR_REFLECT;
    value = (std::max)(Grimoire::TIME_DILATION_MIN,
                       (std::min)(Grimoire::TIME_DILATION_MAX, value));
    // Hold s_workerMutex across the whole mutate+StartWorker decision (audit #8
    // discipline from Laufen): lock order s_workerMutex (outer) → s_mutex (inner).
    std::lock_guard<std::mutex> wlk(s_workerMutex);
    int32_t rc = TR_OK;
    {
        std::lock_guard<std::mutex> lk(s_mutex);
        DilLoc f;
        rc = ResolveDilField(target, f);
        if (rc != TR_OK) return rc;
        double current = 0;
        if (!ReadFloatAt(f.addr, f.size, current)) return TR_ERR_REFLECT;
        DilState& d = s_dils[target];
        // Capture the untouched natural base only when (re)activating or the owner
        // changed — NOT when merely changing the value on the same active owner, or
        // we would fold our own write into the restore base.
        if (!d.active || f.owner != d.capturedOwner) {
            // See the worker's note: base is the RESTORE value, so 0 / NaN would make
            // Reset freeze the game forever. 1.0 is the engine default for both
            // AWorldSettings::TimeDilation and AActor::CustomTimeDilation. (B22)
            if (current > 0.0 && std::isfinite(current)) {
                d.base = current;
            } else {
                LOG_WARN("Time: target %d ('%s') reads %.4f — not a usable base; "
                         "Reset will restore the engine default 1.0 instead",
                         target, f.name.c_str(), current);
                d.base = 1.0;
            }
            d.capturedOwner = f.owner;
        }
        d.value = value;
        d.active = true;
        if (!WriteFloatAt(f.addr, f.size, value))
            rc = TR_ERR_WRITE;   // leave active; the worker will retry next tick
        LOG_INFO("Time: target %d ('%s') base=%.4f -> hold %.4f (rc=%d)",
                 target, f.name.c_str(), d.base, value, rc);
    }
    StartWorkerLocked();   // s_workerMutex held, s_mutex released
    return (rc < 0) ? rc : 1;   // 1 = override active
}

int32_t ResetDilation(int32_t target) {
    if (target < 0 || target >= DIL_COUNT) return TR_ERR_REFLECT;
    // Hold s_workerMutex across the clear + stop decision so a concurrent
    // SetDilation on the other target can't StartWorker between them and then get
    // joined away, leaving a lever active with no re-assert worker (Laufen audit #8).
    std::lock_guard<std::mutex> wlk(s_workerMutex);
    bool anyActive;
    {
        std::lock_guard<std::mutex> lk(s_mutex);
        DilState& d = s_dils[target];
        if (d.active) {
            DilLoc f;
            if (ResolveDilField(target, f) == TR_OK)
                WriteFloatAt(f.addr, f.size, d.base);   // best-effort restore
        }
        d.active = false;
        d.value = 1.0;
        d.base = 1.0;
        d.capturedOwner = 0;
        anyActive = AnyActiveLocked();   // decide under the SAME s_mutex hold
    }
    // Join with s_mutex RELEASED (WorkerLoop takes s_mutex per tick) but
    // s_workerMutex still held (serializes against a racing StartWorkerLocked).
    if (!anyActive) StopWorkerLocked();
    LOG_INFO("Time: reset target %d (any active left=%d)", target, anyActive ? 1 : 0);
    return TR_OK;
}

void StopWorker() {
    // Public wrapper (called on DLL unload). Takes s_workerMutex, then joins with
    // s_mutex not held — same discipline the mutators use.
    std::lock_guard<std::mutex> lk(s_workerMutex);
    StopWorkerLocked();
}

} // namespace Hemmung
