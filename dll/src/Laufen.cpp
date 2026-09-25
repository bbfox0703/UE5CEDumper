// ============================================================
// Laufen — 拉歐芬 / 走る (高速移動の魔法 — "to run", high-speed-movement mage)
// MovementTuning: force per-pawn UCharacterMovementComponent float knobs
// (MaxWalkSpeed / GravityScale / JumpZVelocity) by a multiplier of their
// captured base, held by a re-assert worker (write-on-drift). Contract: Laufen.h.
//
// This is the float analogue of Solitar (GodMode): same local-pawn resolution
// chain, same s_mutex + s_workerMutex two-lock split, same write-on-drift worker
// — but the target is a FloatProperty scalar on the CharacterMovement sub-object
// instead of a single FBoolProperty bit on the AActor. Self-contained (Path B):
// only public Ubel/Aura/Macht + DynOff, no Wirbel coupling.
// ============================================================

#define LOG_CAT "WALK"
#include "Sein.h"
#include "Laufen.h"
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

// &GWorld — deref once for UWorld* (defined in Frieren.cpp; same as Solitar/Wirbel).
extern uintptr_t g_cachedGWorld;

namespace {

using namespace Laufen;  // MoveResult codes

// ---- knob definitions (reflected float property names on the CMC) ----
struct KnobDef {
    const char* exact;     // exact FName match wins
    const char* contains;  // fuzzy fallback if a game renamed it slightly
};
const KnobDef kKnobs[KNOB_COUNT] = {
    { "MaxWalkSpeed",  "WalkSpeed"     },  // KNOB_WALK_SPEED
    { "GravityScale",  "GravityScale"  },  // KNOB_GRAVITY (P2)
    { "JumpZVelocity", "JumpZVelocity" },  // KNOB_JUMP    (P3)
};

// Per-knob desired state — survives UI reconnects (the override lives in the
// DLL while the game process lives). Guarded by s_mutex.
struct KnobState {
    bool      active       = false;  // override engaged (worker holding it)
    double    multiplier   = 1.0;    // desired multiplier of base
    double    base         = 0.0;    // captured untouched base value
    uintptr_t capturedPawn = 0;      // pawn the base was captured from (respawn re-capture)
};
KnobState s_knobs[KNOB_COUNT];

// Gravity-DIRECTION override (UE5.3+ FVector GravityDirection; stock 5.3 honours it too -- [R7-X7]). Parallel to the
// scalar knobs: same base-capture / re-assert discipline. Guarded by s_mutex.
struct GravDirState {
    bool      active       = false;
    double    x = 0, y = 0, z = -1;   // desired (normalized) unit vector
    double    baseX = 0, baseY = 0, baseZ = -1;   // captured default
    uintptr_t capturedPawn = 0;
};
GravDirState s_gravDir;

// Serializes whole operations: reachable from the Fern pipe thread AND the
// re-assert worker (and, in P4, the Mimic mailbox thread).
std::mutex s_mutex;

// Re-assert worker — separate control mutex so StopWorker()'s join() never runs
// while s_mutex is held (the worker locks s_mutex per tick → would deadlock).
Routine::SafeThread       s_worker;   // detaches at process exit, never terminates
std::mutex        s_workerMutex;
std::atomic<bool> s_workerStop{false};

// ---- low-level reads (copied from Solitar; public APIs only) ----

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

// Read/write a 3-component FVector honoring the reflected struct width (12B =
// 3×float / 24B = 3×double under LWC). structSize is the StructProperty's
// ElementSize (sizeof FVector); component width = structSize/3.
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

// Normalize a direction to a unit vector; degenerate (near-zero) → straight down.
void Normalize3(double v[3]) {
    double len = std::sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
    if (len < 1e-6) { v[0] = 0; v[1] = 0; v[2] = -1; }
    else            { v[0] /= len; v[1] /= len; v[2] /= len; }
}

// ---- resolution chain (identical to Solitar — local pawn → CMC) ----

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

// Resolved local pawn + its CharacterMovement sub-object for this instant.
struct Ctx {
    uintptr_t world = 0, pc = 0, pawn = 0, pawnClass = 0, cmc = 0, cmcClass = 0;
};

int32_t ResolveCtx(Ctx& c) {
    c = Ctx{};
    c.world = DerefWorld();
    if (!c.world) return MR_ERR_NOT_INIT;
    c.pc = ResolveLocalPC(c.world);
    if (!c.pc) return MR_ERR_NO_PAWN;
    c.pc = HopThroughDebugCamera(c.pc);
    c.pawn = ResolvePawn(c.pc);
    if (!c.pawn) return MR_ERR_NO_PAWN;
    c.pawnClass = Ubel::GetClass(c.pawn);
    if (!c.pawnClass) return MR_ERR_REFLECT;
    int32_t cmOff = Ubel::FindFieldOffset(c.pawnClass, "CharacterMovement",
                                          "CharacterMovement", nullptr, "ObjectProperty");
    c.cmc = ReadPtrAt(c.pawn, cmOff);
    if (!c.cmc) return MR_ERR_NO_CMC;
    c.cmcClass = Ubel::GetClass(c.cmc);
    if (!c.cmcClass) return MR_ERR_REFLECT;
    return MR_OK;
}

// Resolved float field location on the live CMC.
struct FieldLoc {
    uintptr_t   addr   = 0;
    int32_t     offset = -1;
    int32_t     size   = 4;
    std::string name;
};

bool ResolveField(const Ctx& c, int knobId, FieldLoc& f) {
    if (knobId < 0 || knobId >= KNOB_COUNT) return false;
    FieldInfo fi{};
    const KnobDef& d = kKnobs[knobId];
    if (!Ubel::FindField(c.cmcClass, d.exact, d.contains, nullptr, "FloatProperty", fi)
        || fi.Offset < 0)
        return false;
    f.addr   = c.cmc + static_cast<uintptr_t>(fi.Offset);
    f.offset = fi.Offset;
    f.size   = fi.Size;
    f.name   = fi.Name;
    return true;
}

// Resolve the GravityDirection FVector field on the live CMC (UE5.3+).
bool ResolveGravDirField(const Ctx& c, uintptr_t& addr, int32_t& size) {
    FieldInfo fi{};
    if (!Ubel::FindField(c.cmcClass, "GravityDirection", "GravityDirection",
                         nullptr, "StructProperty", fi)
        || fi.Offset < 0)
        return false;
    addr = c.cmc + static_cast<uintptr_t>(fi.Offset);
    size = fi.Size;
    return true;
}

bool AnyActiveLocked() {
    for (int i = 0; i < KNOB_COUNT; ++i)
        if (s_knobs[i].active) return true;
    return s_gravDir.active;
}

// Apply one knob's desired value to the live CMC (caller holds s_mutex).
// Write-on-drift: only writes when the live value differs from base*multiplier.
void ApplyKnobLocked(const Ctx& c, int knobId, bool* drifted) {
    KnobState& k = s_knobs[knobId];
    if (!k.active) return;
    FieldLoc f;
    if (!ResolveField(c, knobId, f)) return;   // field gone this tick
    double current = 0;
    if (!ReadFloatAt(f.addr, f.size, current)) return;
    // Re-capture base on pawn change (respawn): the new CMC's value is untouched
    // by us, so it is the genuine base to scale. But KEEP the previous base if the
    // fresh pawn reads 0 / non-finite — otherwise a respawn that lands mid-cutscene
    // silently converts the hold into "pin at zero" (B22). The pawn is still adopted,
    // so a later tick with a sane value is not re-captured over our own write.
    if (c.pawn != k.capturedPawn) {
        if (current > 0.0 && std::isfinite(current)) k.base = current;
        k.capturedPawn = c.pawn;
    }
    double target = k.base * k.multiplier;
    double eps = (std::max)(1e-3, std::fabs(target) * 1e-5);
    if (std::fabs(current - target) > eps) {
        if (WriteFloatAt(f.addr, f.size, target) && drifted) *drifted = true;
    }
}

// Apply the gravity-direction override to the live CMC (caller holds s_mutex).
// Write-on-drift; re-capture the base default on pawn change (respawn).
void ApplyGravDirLocked(const Ctx& c, bool* drifted) {
    if (!s_gravDir.active) return;
    uintptr_t addr = 0; int32_t size = 0;
    if (!ResolveGravDirField(c, addr, size)) return;
    double cur[3] = {};
    if (!ReadVec3At(addr, size, cur)) return;
    if (c.pawn != s_gravDir.capturedPawn) {
        s_gravDir.baseX = cur[0]; s_gravDir.baseY = cur[1]; s_gravDir.baseZ = cur[2];
        s_gravDir.capturedPawn = c.pawn;
    }
    double target[3] = { s_gravDir.x, s_gravDir.y, s_gravDir.z };
    double dx = cur[0] - target[0], dy = cur[1] - target[1], dz = cur[2] - target[2];
    if (dx * dx + dy * dy + dz * dz > 1e-6) {
        if (WriteVec3At(addr, size, target) && drifted) *drifted = true;
    }
}

// Fill a GravDirInfo for the UI from the live CMC + stored desired state.
void FillGravDirLocked(const Ctx& c, GravDirInfo& info) {
    info.active = s_gravDir.active;
    uintptr_t addr = 0; int32_t size = 0;
    if (!ResolveGravDirField(c, addr, size)) return;   // pre-5.3 / not reflected
    double cur[3] = {};
    if (!ReadVec3At(addr, size, cur)) return;
    info.resolved    = true;
    info.x = cur[0]; info.y = cur[1]; info.z = cur[2];
    info.ownerAddr   = c.cmc;
    info.fieldOffset = static_cast<int32_t>(addr - c.cmc);
    info.fieldName   = "GravityDirection";
}

// Fill a KnobInfo for the UI from the live CMC + stored desired state.
void FillKnobLocked(const Ctx& c, int knobId, KnobInfo& info) {
    const KnobState& k = s_knobs[knobId];
    info.multiplier = k.multiplier;
    info.active     = k.active;
    info.base       = k.base;
    FieldLoc f;
    if (ResolveField(c, knobId, f)) {
        double cur = 0;
        if (ReadFloatAt(f.addr, f.size, cur)) {
            info.resolved    = true;
            info.current     = cur;
            info.ownerAddr   = c.cmc;
            info.fieldOffset = f.offset;
            info.fieldName   = f.name;
        }
    }
}

// ---- re-assert worker ----

void WorkerLoop() {
    // Sliced sleep, cancel-immunity, per-tick exception guard and the shutdown break
    // all live in Routine::ReassertLoop (R5 / B14). Only the tick is ours.
    int driftCount = 0;
    Routine::ReassertLoop("Movement", Grimoire::MOVE_REASSERT_MS, s_workerStop, [&] {
        std::lock_guard<std::mutex> lk(s_mutex);
        if (!AnyActiveLocked()) return;      // all knobs reset between ticks
        Ctx c;
        if (ResolveCtx(c) != MR_OK) return;  // pawn/CMC gone (menu) — retry next tick
        bool drifted = false;
        for (int i = 0; i < KNOB_COUNT; ++i)
            ApplyKnobLocked(c, i, &drifted);
        ApplyGravDirLocked(c, &drifted);
        if (drifted) {
            ++driftCount;
            if (driftCount <= 5 || driftCount % 100 == 0)
                LOG_WARN("Movement: re-asserted knob(s) (drift #%d) — the game keeps "
                         "recomputing the value each tick (sprint/ability system); the "
                         "override is being held against it.", driftCount);
        }
    });
}

// Worker start/stop split into "Locked" cores (caller ALREADY holds s_workerMutex)
// plus a public StopWorker() wrapper. The knob mutators hold s_workerMutex ACROSS
// the whole "mutate desired state + decide start/stop" sequence (audit #8), so a
// concurrent mutator on a different knob can't slip a StartWorker between one op's
// state-clear and its stop decision (which would join a freshly-started worker and
// leave a knob active with no re-assert). Lock order is always s_workerMutex
// (outer) → s_mutex (inner); the join in StopWorkerLocked runs with s_mutex
// RELEASED because WorkerLoop takes s_mutex every tick (joining under it deadlocks).
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

namespace Laufen {

int32_t GetSnapshot(Snapshot& out) {
    out = Snapshot{};
    std::lock_guard<std::mutex> lk(s_mutex);
    Ctx c;
    int32_t rc = ResolveCtx(c);
    out.code = rc;
    // Always surface the stored desired state so the UI can render "active" even
    // when the pawn momentarily doesn't resolve (menu / loading).
    for (int i = 0; i < KNOB_COUNT; ++i) {
        out.knobs[i].multiplier = s_knobs[i].multiplier;
        out.knobs[i].active     = s_knobs[i].active;
        out.knobs[i].base       = s_knobs[i].base;
    }
    if (rc != MR_OK) return rc;
    out.hasCmc  = true;
    out.cmcAddr = c.cmc;
    for (int i = 0; i < KNOB_COUNT; ++i)
        FillKnobLocked(c, i, out.knobs[i]);
    FillGravDirLocked(c, out.gravDir);
    return MR_OK;
}

int32_t GetKnob(int32_t knobId, KnobInfo& out) {
    out = KnobInfo{};
    if (knobId < 0 || knobId >= KNOB_COUNT) return MR_ERR_REFLECT;
    std::lock_guard<std::mutex> lk(s_mutex);
    Ctx c;
    int32_t rc = ResolveCtx(c);
    if (rc != MR_OK) {
        out.multiplier = s_knobs[knobId].multiplier;
        out.active     = s_knobs[knobId].active;
        out.base       = s_knobs[knobId].base;
        return rc;
    }
    FillKnobLocked(c, knobId, out);
    return MR_OK;
}

int32_t SetMultiplier(int32_t knobId, double multiplier) {
    if (knobId < 0 || knobId >= KNOB_COUNT) return MR_ERR_REFLECT;
    multiplier = (std::max)(Grimoire::MOVE_MULT_MIN,
                            (std::min)(Grimoire::MOVE_MULT_MAX, multiplier));
    // Hold s_workerMutex across the whole mutate+StartWorker decision (audit #8):
    // lock order s_workerMutex (outer) → s_mutex (inner).
    std::lock_guard<std::mutex> wlk(s_workerMutex);
    int32_t rc = MR_OK;
    {
        std::lock_guard<std::mutex> lk(s_mutex);
        Ctx c;
        rc = ResolveCtx(c);
        if (rc != MR_OK) return rc;
        FieldLoc f;
        if (!ResolveField(c, knobId, f)) return MR_ERR_REFLECT;
        double current = 0;
        if (!ReadFloatAt(f.addr, f.size, current)) return MR_ERR_REFLECT;
        KnobState& k = s_knobs[knobId];
        // A base of 0 (or NaN/inf) makes every target 0 too, and the worker then writes
        // that over the game's own value every 250 ms while the panel reads "300%,
        // active" — the knob is pinning the value AT ZERO. Games really do park these
        // at 0 (cutscene, swim, mount, ragdoll), so refuse the capture rather than the
        // command: the pawn/field are fine, just not sampleable right now. (B22)
        if (!(current > 0.0) || !std::isfinite(current)) {
            LOG_WARN("Movement: knob %d ('%s') base reads %.3f — not a usable base "
                     "(cutscene / swim / mount?). Not engaging; try again in normal play.",
                     knobId, f.name.c_str(), current);
            return MR_ERR_REFLECT;
        }
        // Capture an untouched base only when (re)activating or the pawn changed —
        // NOT when merely changing the multiplier on the same active pawn, or we
        // would fold our own write into the base and compound.
        if (!k.active || c.pawn != k.capturedPawn) {
            k.base = current;
            k.capturedPawn = c.pawn;
        }
        k.multiplier = multiplier;
        k.active = true;
        double target = k.base * multiplier;
        if (!WriteFloatAt(f.addr, f.size, target))
            rc = MR_ERR_WRITE;   // leave active; the worker will retry next tick
        LOG_INFO("Movement: knob %d ('%s') base=%.3f x%.3f -> %.3f (rc=%d)",
                 knobId, f.name.c_str(), k.base, multiplier, target, rc);
    }
    StartWorkerLocked();   // s_workerMutex held, s_mutex released
    return (rc < 0) ? rc : 1;   // 1 = override active
}

int32_t ResetKnob(int32_t knobId) {
    if (knobId < 0 || knobId >= KNOB_COUNT) return MR_ERR_REFLECT;
    // Hold s_workerMutex across the clear + stop decision so a concurrent
    // SetMultiplier on another knob can't StartWorker between them and then get
    // joined away, leaving a knob active with no re-assert worker (audit #8).
    std::lock_guard<std::mutex> wlk(s_workerMutex);
    bool anyActive;
    {
        std::lock_guard<std::mutex> lk(s_mutex);
        KnobState& k = s_knobs[knobId];
        if (k.active) {
            Ctx c;
            if (ResolveCtx(c) == MR_OK) {
                FieldLoc f;
                if (ResolveField(c, knobId, f))
                    WriteFloatAt(f.addr, f.size, k.base);   // best-effort restore
            }
        }
        k.active = false;
        k.multiplier = 1.0;
        k.capturedPawn = 0;
        // Decide under the SAME s_mutex hold as the clear — a stale re-read in a
        // second scope was the race window.
        anyActive = AnyActiveLocked();
    }
    // Join with s_mutex RELEASED (WorkerLoop takes s_mutex per tick) but
    // s_workerMutex still held (serializes against a racing StartWorkerLocked).
    if (!anyActive) StopWorkerLocked();
    LOG_INFO("Movement: reset knob %d (any active left=%d)", knobId, anyActive ? 1 : 0);
    return MR_OK;
}

int32_t SetKnobPercent(int32_t knobId, double percent) {
    if (knobId < 0 || knobId >= KNOB_COUNT) return MR_ERR_REFLECT;
    // 100% (±0.5) means "off" — the single-call API the CE-Lua/mailbox path uses.
    if (std::fabs(percent - 100.0) < 0.5) {
        int32_t rc = ResetKnob(knobId);
        return (rc < 0) ? rc : 0;   // 0 = off
    }
    // Jump: percent is HEIGHT %, apply velocity multiplier = sqrt(height) (h ∝ v²).
    // Other knobs: percent is the multiplier directly.
    double mult = (knobId == KNOB_JUMP) ? std::sqrt(percent / 100.0)
                                        : (percent / 100.0);
    return SetMultiplier(knobId, mult);   // 1 active / negative MoveResult
}

int32_t SetGravityDirection(double x, double y, double z) {
    // (0,0,0) sentinel = OFF (a zero vector is not a valid direction). Handled
    // BEFORE taking s_workerMutex — ResetGravityDirection() acquires it itself.
    if (std::fabs(x) < 1e-9 && std::fabs(y) < 1e-9 && std::fabs(z) < 1e-9)
        return ResetGravityDirection();
    // Hold s_workerMutex across the mutate+StartWorker decision (audit #8):
    // lock order s_workerMutex (outer) → s_mutex (inner).
    std::lock_guard<std::mutex> wlk(s_workerMutex);
    int32_t rc = MR_OK;
    {
        std::lock_guard<std::mutex> lk(s_mutex);
        Ctx c;
        rc = ResolveCtx(c);
        if (rc != MR_OK) return rc;
        uintptr_t addr = 0; int32_t size = 0;
        if (!ResolveGravDirField(c, addr, size)) return MR_ERR_REFLECT;  // pre-5.3
        double cur[3] = {};
        if (!ReadVec3At(addr, size, cur)) return MR_ERR_REFLECT;
        // Capture the untouched default only on (re)activation or pawn change.
        if (!s_gravDir.active || c.pawn != s_gravDir.capturedPawn) {
            s_gravDir.baseX = cur[0]; s_gravDir.baseY = cur[1]; s_gravDir.baseZ = cur[2];
            s_gravDir.capturedPawn = c.pawn;
        }
        double v[3] = { x, y, z };
        Normalize3(v);
        s_gravDir.x = v[0]; s_gravDir.y = v[1]; s_gravDir.z = v[2];
        s_gravDir.active = true;
        if (!WriteVec3At(addr, size, v)) rc = MR_ERR_WRITE;
        LOG_INFO("Movement: gravity dir -> (%.3f, %.3f, %.3f) (rc=%d)", v[0], v[1], v[2], rc);
    }
    StartWorkerLocked();   // s_workerMutex held, s_mutex released
    return (rc < 0) ? rc : 1;
}

int32_t ResetGravityDirection() {
    // Hold s_workerMutex across the clear + stop decision (audit #8): lock order
    // s_workerMutex (outer) → s_mutex (inner); join with s_mutex released.
    std::lock_guard<std::mutex> wlk(s_workerMutex);
    bool anyActive;
    {
        std::lock_guard<std::mutex> lk(s_mutex);
        if (s_gravDir.active) {
            Ctx c;
            if (ResolveCtx(c) == MR_OK) {
                uintptr_t addr = 0; int32_t size = 0;
                if (ResolveGravDirField(c, addr, size)) {
                    double base[3] = { s_gravDir.baseX, s_gravDir.baseY, s_gravDir.baseZ };
                    WriteVec3At(addr, size, base);   // best-effort restore
                }
            }
        }
        s_gravDir.active = false;
        s_gravDir.capturedPawn = 0;
        anyActive = AnyActiveLocked();   // decide under the same s_mutex hold
    }
    if (!anyActive) StopWorkerLocked();
    LOG_INFO("Movement: gravity dir reset (any active left=%d)", anyActive ? 1 : 0);
    return MR_OK;
}

int32_t GetGravityDirection(GravDirInfo& out) {
    out = GravDirInfo{};
    std::lock_guard<std::mutex> lk(s_mutex);
    Ctx c;
    int32_t rc = ResolveCtx(c);
    if (rc != MR_OK) {
        out.active = s_gravDir.active;
        return rc;
    }
    FillGravDirLocked(c, out);
    return MR_OK;
}

void StopWorker() {
    // Public wrapper (exported; called on DLL unload). Takes s_workerMutex, then
    // joins with s_mutex not held — same discipline the mutators use.
    std::lock_guard<std::mutex> lk(s_workerMutex);
    StopWorkerLocked();
}

} // namespace Laufen
