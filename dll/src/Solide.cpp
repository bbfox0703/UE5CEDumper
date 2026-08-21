// ============================================================
// Solide — ゾリーデ (剣士 — blindfold swordsman, "solid")
// ForceField / stealth-meter hold: hold a discovered reflected field
// (bool / ObjectProperty-null / numeric) at a value across all live instances of
// a class via a write-on-drift re-assert worker. Contract: Solide.h.
//
// The multi-instance sibling of Hemmung (absolute-value hold): same s_mutex +
// s_workerMutex two-lock split, same worker (write-on-drift, base capture), same
// "no cached instance pointers — re-resolve the pool every tick" discipline.
// Self-contained (Path B): only public Solitar/Ubel/Aura/Macht + DynOff.
// ============================================================

#define LOG_CAT "WALK"
#include "Sein.h"
#include "Solide.h"
#include "Solitar.h"   // SetActorBool / GetActorBool (bool kind reuse)
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
#include <vector>
#include <unordered_map>
#include <unordered_set>

// &GWorld — deref once for UWorld* (defined in Frieren.cpp; same as Hemmung/Solitar).
extern uintptr_t g_cachedGWorld;

namespace {

using namespace Solide;

// ---- active hold-jobs (survive UI reconnect; live for the game process) ----
struct Job {
    std::string className;
    std::string fieldName;
    int32_t     kind  = K_BOOL;
    double      value = 0.0;      // desired (bool: 1/0; numeric: absolute)
    // Restore base captured PER INSTANCE (owner addr → original value / bool bit) at
    // first force, so RemoveForce writes each instance's OWN base, never a foreign one.
    // Pruned to the live pool each re-assert tick to bound it. (L4)
    std::unordered_map<uintptr_t, double> baseByOwner;
    // Set when the field resolved on >=1 instance but was type-refused everywhere
    // (weak/soft/lazy ptr → GObjects[0] trap, or wrong numeric type). (L2)
    int32_t     lastRefusal = 0;
    // Last-tick stats (for the UI badge + Locate handoff).
    int32_t     held         = 0;
    uintptr_t   sampleOwner  = 0;
    int32_t     sampleOffset = -1;
    // Last tick's pool hit the cap → `held` is a floor. Also the single definition of
    // "capped" used by the base-prune guard below.
    bool        poolTruncated = false;
};
std::vector<Job> s_jobs;

std::mutex        s_mutex;        // serializes ops (pipe thread + worker)
Routine::SafeThread       s_worker;   // detaches at process exit, never terminates
std::mutex        s_workerMutex;  // guards start/stop join (never held with s_mutex)
std::atomic<bool> s_workerStop{false};

// ---- low-level reads (copied from Hemmung; public APIs only) ----

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

std::string ToLower(const std::string& s) {
    std::string r = s;
    for (char& c : r) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return r;
}

// LWC-width-aware float read/write (4B float / 8B double), keyed on FieldInfo.Size.
bool ReadFloatAt(uintptr_t addr, int32_t size, double& out) {
    if (size >= 8) {
        double d = 0;
        if (!Macht::ReadSafe(addr, d)) return false;
        out = d; return true;
    }
    float f = 0;
    if (!Macht::ReadSafe(addr, f)) return false;
    out = static_cast<double>(f); return true;
}
bool WriteFloatAt(uintptr_t addr, int32_t size, double v) {
    if (size >= 8) { double d = v; return Macht::WriteBytes(addr, &d, 8); }
    float f = static_cast<float>(v); return Macht::WriteBytes(addr, &f, 4);
}

// ---- numeric type classification (MVP: float/double + int32/int64/byte) ----

bool IsFloatType(const std::string& t) {
    return t == "FloatProperty" || t == "DoubleProperty";
}
// IsIntType now lives in Solide.h, derived from IntWidthOf so the gate and the
// width/sign table cannot drift apart (audit #5 AF8).
bool IsNumericType(const std::string& t) { return IsFloatType(t) || IsIntType(t); }

// Width + signedness come from Solide.h's IntWidthOf so the read and the write
// cannot disagree about a type (audit #5 AF8: Int8Property was written signed and
// read UNSIGNED, so a negative hold never converged and the worker rewrote the
// same byte forever while reporting drift).
bool ReadNumeric(uintptr_t addr, const FieldInfo& fi, double& out) {
    if (IsFloatType(fi.TypeName)) return ReadFloatAt(addr, fi.Size, out);
    const IntWidth w = IntWidthOf(fi.TypeName);
    switch (w.bytes) {
        case 4: { int32_t v = 0; if (!Macht::ReadSafe(addr, v)) return false;
                  out = static_cast<double>(v); return true; }
        case 8: { int64_t v = 0; if (!Macht::ReadSafe(addr, v)) return false;
                  out = static_cast<double>(v); return true; }
        case 1:
            if (w.isSigned) { int8_t v = 0; if (!Macht::ReadSafe(addr, v)) return false;
                              out = static_cast<double>(v); return true; }
            else            { uint8_t v = 0; if (!Macht::ReadSafe(addr, v)) return false;
                              out = static_cast<double>(v); return true; }
        default: return false;   // not an integer type we hold — never guess a width
    }
}
bool WriteNumeric(uintptr_t addr, const FieldInfo& fi, double value) {
    if (IsFloatType(fi.TypeName)) return WriteFloatAt(addr, fi.Size, value);
    const IntWidth w = IntWidthOf(fi.TypeName);
    if (w.bytes == 0) return false;

    // Refuse a target the field cannot represent instead of truncating into it. A
    // truncated write reads back as a different number, so the drift check can never
    // be satisfied — the same non-convergence AF8 caused, reached a different way.
    double lo = 0.0, hi = 0.0;
    IntRangeOf(w, lo, hi);
    if (!(value >= lo && value <= hi)) return false;

    const int64_t r = std::llround(value);
    switch (w.bytes) {
        case 4: { int32_t v = static_cast<int32_t>(r); return Macht::WriteBytes(addr, &v, 4); }
        case 8: { int64_t v = r;                       return Macht::WriteBytes(addr, &v, 8); }
        case 1:
            if (w.isSigned) { int8_t  v = static_cast<int8_t>(r);  return Macht::WriteBytes(addr, &v, 1); }
            else            { uint8_t v = static_cast<uint8_t>(r); return Macht::WriteBytes(addr, &v, 1); }
        default: return false;
    }
}

// ---- local-pawn resolution (for FindStealthMeter; copied from Hemmung) ----

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

// ---- per-instance apply ----

// Apply (or restore) one job on one live object. Returns true when the field
// resolved on this object (counts toward "N held"); fills sample owner/offset.
// `restore` writes the captured base instead of the desired value.
bool ApplyToInstance(Job& job, uintptr_t obj, uintptr_t cls, bool restore,
                     bool* drifted, uintptr_t& sampleOwner, int32_t& sampleOffset,
                     int32_t& refusal) {
    sampleOwner = 0; sampleOffset = -1; refusal = 0;

    if (job.kind == K_BOOL) {
        int32_t cur = Solitar::GetActorBool(obj, cls, job.fieldName.c_str());
        if (cur < 0) return false;   // bool not resolvable on this instance
        // Per-instance base (L4): capture this object's ORIGINAL bit on first force; on
        // restore, only rewrite instances we actually captured (never a foreign base).
        auto b = job.baseByOwner.find(obj);
        if (b == job.baseByOwner.end()) {
            if (restore) return false;
            b = job.baseByOwner.emplace(obj, static_cast<double>(cur)).first;
        }
        bool desired = restore ? (b->second != 0.0) : (job.value != 0.0);
        // Report drift (the game re-wrote the bit off our target) for telemetry
        // parity with the numeric/object-null branches — the "game keeps
        // re-writing it" LOG_WARN is the in-field "this lever is a no-op" signal.
        if (((cur != 0) != desired) && drifted) *drifted = true;
        int32_t rc = Solitar::SetActorBool(obj, cls, job.fieldName.c_str(), desired);
        if (rc < 0) return false;
        sampleOwner  = obj;
        sampleOffset = Ubel::FindFieldOffset(cls, job.fieldName.c_str(), nullptr,
                                             nullptr, "BoolProperty");   // exact-only (L3)
        return true;
    }

    // Exact field name only — the forced name comes from Property Search / the stealth
    // finder as an exact leaf name, so the fuzzy "contains" fallback (which could hit a
    // same-prefix field like HealthRegenRate) is refused. (L3)
    FieldInfo fi{};
    if (!Ubel::FindField(cls, job.fieldName.c_str(), /*contains=*/nullptr, nullptr, nullptr, fi)
        || fi.Offset < 0)
        return false;
    uintptr_t addr = obj + static_cast<uintptr_t>(fi.Offset);

    if (job.kind == K_OBJECT_NULL) {
        // Strong ObjectProperty only: writing 8 zero bytes into a Weak/Soft/Lazy
        // ptr sets ObjectIndex 0 = a VALID GObjects[0] slot, not null (crash trap).
        if (fi.TypeName != "ObjectProperty") { refusal = FR_ERR_WEAK_PTR; return false; }   // L2
        sampleOwner = obj; sampleOffset = fi.Offset;
        if (restore) return true;   // original ptr not saved (stale) — no restore
        uintptr_t cur = 0;
        if (!Macht::ReadSafe(addr, cur)) return false;
        if (cur != 0) {
            uintptr_t z = 0;
            // A FAILED write must not count as held. Discarding this result — using it only
            // to set `drifted` — is what let an unwritable instance be reported as one of
            // "N held". [SOLIDEHELD-2026-08-21]
            if (!Macht::WriteBytes(addr, &z, sizeof(uintptr_t))) {
                refusal = FR_ERR_WRITE;
                return false;
            }
            if (drifted) *drifted = true;
        }
        return true;
    }

    // K_NUMERIC
    if (!IsNumericType(fi.TypeName)) { refusal = FR_ERR_REFLECT; return false; }   // L2
    double cur = 0;
    if (!ReadNumeric(addr, fi, cur)) return false;
    auto b = job.baseByOwner.find(obj);   // per-instance base (L4)
    if (b == job.baseByOwner.end()) {
        if (restore) return false;
        b = job.baseByOwner.emplace(obj, cur).first;
    }
    double target = restore ? b->second : job.value;
    double eps = IsFloatType(fi.TypeName) ? (std::max)(1e-4, std::fabs(target) * 1e-5) : 0.5;
    if (std::fabs(cur - target) > eps) {
        // ⚠ The write RESULT decides whether this instance is held. `WriteNumeric` returns
        // false when the value does not fit the field's width — an int8 asked to hold 200 —
        // and the old code used that false only to suppress `drifted`, then returned true
        // anyway. The range check did its job (nothing was written, and 200 did NOT wrap to
        // -56), but the caller counted the instance and the UI reported "held on N instances,
        // value 200". Measured 2026-08-21: byte unchanged at 0x00 while the reply said
        // code=0 held=145 value=200.0. The report and the reality were computed by different
        // paths — audit #4's root cause, in a third place. [SOLIDEHELD-2026-08-21]
        //
        // K_BOOL already did this correctly (`if (rc < 0) return false;`); this brings the
        // other two branches into line with it.
        if (!WriteNumeric(addr, fi, target)) {
            refusal = FR_ERR_WRITE;
            return false;
        }
        if (drifted) *drifted = true;
    }
    sampleOwner = obj; sampleOffset = fi.Offset;
    return true;
}

// Apply (or restore) one job across the live instance pool (caller holds s_mutex).
void ApplyJobLocked(Job& job, bool restore, bool* drifted) {
    int32_t held = 0, refusal = 0;
    uintptr_t sampleOwner = 0; int32_t sampleOffset = -1;
    // The class AND every subclass of it (A6). A Property Search row for an
    // INHERITED field is keyed to the class that DECLARES it — `Aura` sets
    // match.className = definingName so the row can say "inherited by 4822"
    // instead of listing 4822 near-identical rows — so an exact-name pool for
    // e.g. "Actor" resolved essentially nothing and the hold silently held
    // nothing. Subclass semantics are what the row already claims.
    //
    // NOT exactMatch=false: that is a case-insensitive SUBSTRING match on the
    // class NAME, so "Enemy" would capture "EnemyProjectile" — the very thing
    // the old exact match existed to prevent. FindInstancesDerivedFrom walks
    // the super chain, so "EnemyProjectile" is captured only if it genuinely
    // derives from "Enemy", which is when it SHOULD be.
    auto rset = Aura::FindInstancesDerivedFrom(job.className, Grimoire::SOLIDE_MAX_INSTANCES);
    std::unordered_set<uintptr_t> seen;
    for (const auto& r : rset.results) {
        // Aura already drops CDOs (it has to, before its cap). Kept as the
        // local invariant: ApplyToInstance must never write a class default.
        if (!r.addr || r.name.find("Default__") != std::string::npos) continue;
        uintptr_t cls = r.classAddr ? r.classAddr : Ubel::GetClass(r.addr);
        if (!cls) continue;
        seen.insert(r.addr);
        uintptr_t so = 0; int32_t soff = -1, ref = 0;
        if (ApplyToInstance(job, r.addr, cls, restore, drifted, so, soff, ref)) {
            ++held;
            if (!sampleOwner && so) { sampleOwner = so; sampleOffset = soff; }
        } else if (ref != 0) {
            refusal = ref;   // field resolved but type-refused on this instance (L2)
        }
    }
    if (!restore) {
        job.held = held;
        job.sampleOwner = sampleOwner;
        job.sampleOffset = sampleOffset;
        job.lastRefusal = refusal;
        // Aura already computed this two lines up and we used to drop it on the floor,
        // leaving "held: 0" and "held: 256 of who-knows-how-many" indistinguishable.
        job.poolTruncated = rset.truncated;
        // Prune per-instance bases for owners no longer in the live pool — bounds the map
        // and avoids restoring a stale base to a GC-reused address. Only when the pool was
        // NOT capped: a capped result is a shifting first-N window, so an absent owner may
        // be live-but-past-cap (not gone) — dropping its true base then recapturing our
        // own forced value later would corrupt the restore. Below the cap the pool is
        // complete, so absent == genuinely gone. (L4)
        //
        // Uses Aura's own `truncated` rather than re-deriving "capped" from the result
        // size, so the prune guard and the UI badge can never disagree about whether the
        // pool was complete. Equivalent on this path by construction: FindInstancesByClass
        // is called with the default buildHistogram=false, where Aura sets
        // truncated = (results.size() >= maxResults) — exactly the old test, negated.
        if (!rset.truncated) {
            for (auto it = job.baseByOwner.begin(); it != job.baseByOwner.end(); )
                it = seen.count(it->first) ? std::next(it) : job.baseByOwner.erase(it);
        }
    }
}

bool AnyJobLocked() { return !s_jobs.empty(); }

std::vector<Job>::iterator FindJobLocked(const std::string& cls, const std::string& field) {
    return std::find_if(s_jobs.begin(), s_jobs.end(), [&](const Job& j) {
        return j.className == cls && j.fieldName == field;
    });
}

// ---- re-assert worker (identical discipline to Hemmung) ----

void WorkerLoop() {
    // Sliced sleep, cancel-immunity, per-tick exception guard and the shutdown break
    // all live in Routine::ReassertLoop (R5 / B14). Only the tick is ours.
    int driftCount = 0;
    Routine::ReassertLoop("Solide", Grimoire::SOLIDE_REASSERT_MS, s_workerStop, [&] {
        std::lock_guard<std::mutex> lk(s_mutex);
        if (!AnyJobLocked()) return;
        bool drifted = false;
        for (auto& job : s_jobs)
            ApplyJobLocked(job, /*restore=*/false, &drifted);
        if (drifted) {
            ++driftCount;
            if (driftCount <= 5 || driftCount % 100 == 0)
                LOG_WARN("Solide: re-asserted forced field(s) (drift #%d) — the game keeps "
                         "re-writing them; the hold is being maintained against it.", driftCount);
        }
    });
}

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

} // namespace

namespace Solide {

int32_t AddForce(const char* className, const char* fieldName, int32_t kind, double value) {
    if (!className || !*className || !fieldName || !*fieldName) return FR_ERR_BAD_ARGS;
    if (kind < K_BOOL || kind > K_NUMERIC) return FR_ERR_BAD_KIND;
    if (!g_cachedGWorld) return FR_ERR_NOT_INIT;

    std::lock_guard<std::mutex> wlk(s_workerMutex);   // outer (audit #8 discipline)
    int32_t held = 0;
    {
        std::lock_guard<std::mutex> lk(s_mutex);
        auto it = FindJobLocked(className, fieldName);
        bool newlyAdded = false;
        if (it == s_jobs.end()) {
            Job j;
            j.className = className;
            j.fieldName = fieldName;
            j.kind = kind;
            j.value = value;
            s_jobs.push_back(std::move(j));
            it = s_jobs.end() - 1;
            newlyAdded = true;
        } else {
            // Re-arm an existing hold with a new value/kind; keep the captured bases
            // when the kind is unchanged so a re-apply doesn't fold our own write in.
            if (it->kind != kind) { it->kind = kind; it->baseByOwner.clear(); }
            it->value = value;
        }
        bool drifted = false;
        ApplyJobLocked(*it, /*restore=*/false, &drifted);
        held = it->held;
        // Field resolved on >=1 instance but was type-refused everywhere (weak/soft/lazy
        // ptr → GObjects[0] trap, or wrong numeric type) → a futile hold. Don't persist a
        // newly-added job or start the worker; surface the reason instead of a silent
        // held=0 (Fern maps a negative return to `code`). (L2)
        if (held == 0 && it->lastRefusal != 0 && newlyAdded) {
            int32_t refusal = it->lastRefusal;
            s_jobs.erase(it);
            return refusal;
        }
    }
    StartWorkerLocked();   // s_workerMutex held, s_mutex released
    return held;           // >= 0 : live "N held" count (0 = matched nothing)
}

int32_t RemoveForce(const char* className, const char* fieldName) {
    if (!className || !fieldName) return FR_ERR_BAD_ARGS;
    std::lock_guard<std::mutex> wlk(s_workerMutex);
    bool anyLeft;
    {
        std::lock_guard<std::mutex> lk(s_mutex);
        auto it = FindJobLocked(className, fieldName);
        if (it == s_jobs.end()) return FR_OK;   // already gone
        bool drifted = false;
        ApplyJobLocked(*it, /*restore=*/true, &drifted);   // best-effort restore
        s_jobs.erase(it);
        anyLeft = AnyJobLocked();
    }
    if (!anyLeft) StopWorkerLocked();
    return FR_OK;
}

int32_t ClearAll() {
    std::lock_guard<std::mutex> wlk(s_workerMutex);
    {
        std::lock_guard<std::mutex> lk(s_mutex);
        bool drifted = false;
        for (auto& job : s_jobs)
            ApplyJobLocked(job, /*restore=*/true, &drifted);
        s_jobs.clear();
    }
    StopWorkerLocked();
    return FR_OK;
}

int32_t GetState(std::vector<ForcedFieldInfo>& out) {
    out.clear();
    std::lock_guard<std::mutex> lk(s_mutex);
    out.reserve(s_jobs.size());
    for (const auto& j : s_jobs) {
        ForcedFieldInfo fi;
        fi.className   = j.className;
        fi.fieldName   = j.fieldName;
        fi.kind        = j.kind;
        fi.value       = j.value;
        fi.held        = j.held;
        fi.sampleOwner = j.sampleOwner;
        fi.sampleOffset= j.sampleOffset;
        fi.poolTruncated = j.poolTruncated;
        out.push_back(std::move(fi));
    }
    return FR_OK;
}

int32_t FindStealthMeter(std::vector<StealthCandidate>& out, int32_t maxResults) {
    out.clear();
    uintptr_t world = DerefWorld();
    if (!world) return FR_ERR_NOT_INIT;
    uintptr_t pc = ResolveLocalPC(world);
    if (!pc) return FR_ERR_NO_TARGET;
    pc = HopThroughDebugCamera(pc);
    uintptr_t pawn = ResolvePawn(pc);
    if (!pawn) return FR_ERR_NO_TARGET;

    // Scan the pawn + its owned components/counterparts for numeric stealth fields.
    std::vector<Aura::RelatedObject> related = Aura::GetRelatedObjects(pawn);
    std::vector<StealthCandidate> cands;
    std::vector<std::pair<std::string, std::string>> seen; // (className, fieldName) dedupe
    for (const auto& ro : related) {
        if (!ro.addr) continue;
        if (ro.relation == "Class" || ro.relation == "Outer") continue; // UClass / level — no meter
        uintptr_t cls = Ubel::GetClass(ro.addr);
        if (!cls) continue;
        std::string clsName = Ubel::GetName(cls);
        const ClassInfo& ci = Ubel::WalkClassEx(cls);
        for (const auto& f : ci.Fields) {
            if (!IsFloatType(f.TypeName)) continue;   // meters are floats
            int32_t score = MatchStealthField(ToLower(f.Name));
            if (score <= 0) continue;
            auto key = std::make_pair(clsName, f.Name);
            if (std::find(seen.begin(), seen.end(), key) != seen.end()) continue;
            seen.push_back(key);
            StealthCandidate c;
            c.className = clsName;
            c.classAddr = cls;
            c.fieldName = f.Name;
            c.typeName  = f.TypeName;
            c.ownerAddr = ro.addr;
            c.score     = score;
            double cur = 0;
            if (ReadFloatAt(ro.addr + static_cast<uintptr_t>(f.Offset), f.Size, cur)) c.current = cur;
            cands.push_back(std::move(c));
        }
    }
    std::sort(cands.begin(), cands.end(), [](const StealthCandidate& a, const StealthCandidate& b) {
        return a.score > b.score;
    });
    if (maxResults > 0 && static_cast<int32_t>(cands.size()) > maxResults)
        cands.resize(maxResults);
    out = std::move(cands);
    return FR_OK;
}

void StopWorker() {
    std::lock_guard<std::mutex> lk(s_workerMutex);
    StopWorkerLocked();
}

} // namespace Solide
