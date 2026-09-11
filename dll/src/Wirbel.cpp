// ============================================================
// Wirbel — 威亞貝爾 (北部魔法隊小隊長 — pragmatic soldier)
// Teleport: marker save/recall + cursor teleport (BugIt-style).
//
// Tier model (docs/teleport-spec.md §4):
//   Tier 1 — invoke engine BlueprintCallable UFunctions through the game
//            thread (Stark queue via UE5_CallProcessEventEx).
//   Tier 2 — raw SEH property write fallback (RelativeLocation /
//            ControlRotation) when reflection/invoke fails.
//
// Resolution chain (§5): GWorld → OwningGameInstance → LocalPlayers[0]
// → PlayerController → Pawn → RootComponent. Every property name below
// is declared on an ENGINE base class (AController / AActor /
// USceneComponent), so the lookup is game- and version-agnostic.
// Markers store coordinates + map name only — never object pointers;
// the pawn is re-resolved on every operation (stale-pointer rule).
// ============================================================

#define LOG_CAT "WALK"
#include "Sein.h"
#include "Wirbel.h"
#include "Grimoire.h"
#include "Macht.h"
#include "Aura.h"
#include "Ubel.h"
#include "Stark.h"   // IsGameThreadResponsive — skip doomed POV invokes when paused

#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

// Frieren exports reused internally (extern "C" must live at global scope).
extern "C" int32_t   UE5_CallProcessEventEx(uintptr_t instance, uintptr_t ufunc,
                                            uintptr_t params, uint32_t paramsSize);
extern "C" uintptr_t UE5_FindInstanceOfClass(const char* className);
extern uintptr_t g_cachedGWorld;   // &GWorld — deref once for UWorld*

namespace {

using Wirbel::Pose;
using Wirbel::Marker;
using Wirbel::MovementState;
using namespace Wirbel;  // TeleportResult codes

// Serialize whole operations: handlers are reachable from BOTH the Fern
// pipe thread and the Mimic mailbox polling thread. One op at a time —
// hotkey spam must never interleave two multi-step teleports.
std::mutex s_opMutex;
Marker s_markers[Grimoire::TELEPORT_SLOTS];

// System "last" slot — the pose captured automatically right before every
// recall / force / BugItGo / cursor teleport (see SaveLastImpl). Never user
// saved; recalled one-way via Wirbel::RecallLast so a bad teleport is undoable.
Marker s_lastMarker;

// BugIt slot — a single user-triggered pose stored by Wirbel::BugItSave and
// recalled by Wirbel::BugItGo (UE BugIt/BugItGo semantics). Distinct from the
// markers and the "last" slot; lives DLL-side so the CE Lua hotkeys don't have
// to carry the coordinates between presses.
Marker s_bugItMarker;

bool IEquals(const std::string& a, const char* b) {
    size_t bl = std::strlen(b);
    if (a.size() != bl) return false;
    for (size_t i = 0; i < a.size(); ++i)
        if (std::tolower(static_cast<unsigned char>(a[i]))
            != std::tolower(static_cast<unsigned char>(b[i])))
            return false;
    return true;
}

// ---- low-level reads ----

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

// ---- FVector / FRotator width helpers ----
// LWC rule (§5.3): the property/param size decides the width — 24 bytes
// = 3×double (UE5.0+), 12 bytes = 3×float (UE4). Never keyed off version.

void WriteVec3(uint8_t* dst, int32_t size, const double v[3]) {
    if (size >= 24) {
        std::memcpy(dst, v, 24);
    } else {
        float f[3] = { static_cast<float>(v[0]), static_cast<float>(v[1]),
                       static_cast<float>(v[2]) };
        std::memcpy(dst, f, 12);
    }
}

void ReadVec3Buf(const uint8_t* src, int32_t size, double out[3]) {
    if (size >= 24) {
        std::memcpy(out, src, 24);
    } else {
        float f[3];
        std::memcpy(f, src, 12);
        out[0] = f[0]; out[1] = f[1]; out[2] = f[2];
    }
}

bool ReadVec3Mem(uintptr_t addr, int32_t size, double out[3]) {
    uint8_t raw[24] = {};
    int32_t need = (size >= 24) ? 24 : 12;
    if (!Macht::ReadBytesSafe(addr, raw, need)) return false;
    ReadVec3Buf(raw, size, out);
    return true;
}

// ---- UFunction lookup + param packing ----

// Find a UFunction by name, walking the WHOLE class hierarchy. UFunctions
// like K2_SetActorLocation / K2_TeleportTo / SetControlRotation are declared
// on engine base classes (AActor / AController), several levels above the
// game's concrete pawn/controller subclass — and Ubel::WalkFunctions only
// enumerates a single UClass's OWN Children chain. So walk class → Super →
// Super… until the function is found or the chain ends.
bool FindFunc(uintptr_t classAddr, const char* name, FunctionInfo& out) {
    if (!classAddr || !name) return false;
    uintptr_t cls = classAddr;
    for (int guard = 0; cls && guard < 64; ++guard) {
        auto funcs = Ubel::WalkFunctions(cls);
        for (const auto& f : funcs) {
            if (IEquals(f.name, name)) { out = f; return true; }
        }
        uintptr_t super = 0;
        if (!Macht::ReadSafe(cls + static_cast<uintptr_t>(DynOff::USTRUCT_SUPER), super)
            || super == cls)
            break;
        cls = super;
    }
    return false;
}

const FunctionParam* FindParam(const FunctionInfo& fi, const char* name) {
    for (const auto& p : fi.params)
        if (IEquals(p.name, name)) return &p;
    return nullptr;
}

const FunctionParam* FindReturnParam(const FunctionInfo& fi) {
    for (const auto& p : fi.params)
        if (p.isReturn) return &p;
    return nullptr;
}

bool ParamFits(const std::vector<uint8_t>& buf, const FunctionParam* p, int32_t need) {
    return p && p->offset >= 0
        && p->offset + need <= static_cast<int32_t>(buf.size());
}

void WriteBoolParam(std::vector<uint8_t>& buf, const FunctionInfo& fi,
                    const char* name, bool v) {
    const FunctionParam* p = FindParam(fi, name);
    if (ParamFits(buf, p, 1)) buf[p->offset] = v ? 1 : 0;
}

void WriteByteParam(std::vector<uint8_t>& buf, const FunctionInfo& fi,
                    const char* name, uint8_t v) {
    const FunctionParam* p = FindParam(fi, name);
    if (ParamFits(buf, p, 1)) buf[p->offset] = v;
}

bool WriteVecParam(std::vector<uint8_t>& buf, const FunctionInfo& fi,
                   const char* name, const double v[3]) {
    const FunctionParam* p = FindParam(fi, name);
    int32_t need = (p && p->size >= 24) ? 24 : 12;
    if (!ParamFits(buf, p, need)) return false;
    WriteVec3(buf.data() + p->offset, p->size, v);
    return true;
}

bool ReadVecParam(const std::vector<uint8_t>& buf, const FunctionInfo& fi,
                  const char* name, double out[3]) {
    const FunctionParam* p = FindParam(fi, name);
    int32_t need = (p && p->size >= 24) ? 24 : 12;
    if (!ParamFits(buf, p, need)) return false;
    ReadVec3Buf(buf.data() + p->offset, p->size, out);
    return true;
}

bool WriteFloatParam(std::vector<uint8_t>& buf, const FunctionInfo& fi,
                     const char* name, double v) {
    const FunctionParam* p = FindParam(fi, name);
    if (p && p->size == 8 && ParamFits(buf, p, 8)) {
        std::memcpy(buf.data() + p->offset, &v, 8);
        return true;
    }
    if (ParamFits(buf, p, 4)) {
        float f = static_cast<float>(v);
        std::memcpy(buf.data() + p->offset, &f, 4);
        return true;
    }
    return false;
}

bool ReadFloatParam(const std::vector<uint8_t>& buf, const FunctionInfo& fi,
                    const char* name, double& out) {
    const FunctionParam* p = FindParam(fi, name);
    if (p && p->size == 8 && ParamFits(buf, p, 8)) {
        std::memcpy(&out, buf.data() + p->offset, 8);
        return true;
    }
    if (ParamFits(buf, p, 4)) {
        float f = 0;
        std::memcpy(&f, buf.data() + p->offset, 4);
        out = f;
        return true;
    }
    return false;
}

bool ReadIntParam(const std::vector<uint8_t>& buf, const FunctionInfo& fi,
                  const char* name, int32_t& out) {
    const FunctionParam* p = FindParam(fi, name);
    if (!ParamFits(buf, p, 4)) return false;
    std::memcpy(&out, buf.data() + p->offset, 4);
    return true;
}

void WritePtrParam(std::vector<uint8_t>& buf, const FunctionInfo& fi,
                   const char* name, uintptr_t v) {
    const FunctionParam* p = FindParam(fi, name);
    if (ParamFits(buf, p, 8)) std::memcpy(buf.data() + p->offset, &v, 8);
}

// True when the function either has no boolean return or returned true.
bool ReturnedTrue(const FunctionInfo& fi, const std::vector<uint8_t>& buf) {
    const FunctionParam* rv = FindReturnParam(fi);
    if (!rv || rv->offset < 0 || rv->offset >= static_cast<int32_t>(buf.size()))
        return true;
    return buf[rv->offset] != 0;
}

// Invoke through the game thread (Stark). NEVER the static-native direct
// shortcut here — traces read the physics scene and teleports mutate actor
// state; both must run on the game thread (§11 caveat 1-2).
int32_t Invoke(uintptr_t instance, const FunctionInfo& fi, std::vector<uint8_t>& buf) {
    if (buf.empty()) buf.resize(1, 0);  // never hand ProcessEvent a null buffer
    return UE5_CallProcessEventEx(instance, fi.address,
                                  reinterpret_cast<uintptr_t>(buf.data()),
                                  static_cast<uint32_t>(buf.size()));
}

// Extract a world-space point from a packed FHitResult out-param using the
// param's reflected sub-field layout. Prefers ImpactPoint, falls back to
// Location. The leading bBlockingHit/bStartPenetrating bitfields make any
// assumed member offset wrong — reflection only (§11 caveat 3).
bool ExtractHitPoint(const std::vector<uint8_t>& buf, const FunctionParam& hr,
                     double out[3]) {
    const FunctionParam::StructSubField* best = nullptr;
    for (const auto& sf : hr.structFields)
        if (IEquals(sf.name, "ImpactPoint")) { best = &sf; break; }
    if (!best) {
        for (const auto& sf : hr.structFields)
            if (IEquals(sf.name, "Location")) { best = &sf; break; }
    }
    if (!best || best->offset < 0 || hr.offset < 0) return false;
    int32_t need = (best->size >= 24) ? 24 : 12;
    int32_t at = hr.offset + best->offset;
    if (at + need > static_cast<int32_t>(buf.size())) return false;
    ReadVec3Buf(buf.data() + at, best->size, out);
    return true;
}

// ---- resolution chain (§5) ----

struct Chain {
    uintptr_t world = 0, pc = 0, pawn = 0, root = 0;
    int32_t relLocOff = -1, relLocSize = 0;       // USceneComponent.RelativeLocation
    int32_t attachParentOff = -1;                  // USceneComponent.AttachParent
    int32_t ctrlRotOff = -1, ctrlRotSize = 0;      // AController.ControlRotation
};

// Fill `m` with the pawn address + live velocity/acceleration off the pawn's
// CharacterMovement (defined after GetCmc; forward-declared so GetPoseImpl can
// reuse the chain it already resolved).
void ReadMovementState(const Chain& c, MovementState& m);

// Resolve the LOCAL PlayerController: engine chain first, instance-scan
// fallback (prefer the PC whose UPlayer* Player field is non-null).
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
        if (pc) {
            LOG_INFO("Teleport: local PC 0x%llX via GWorld chain",
                     (unsigned long long)pc);
            return pc;
        }
    } while (false);

    auto rset = Aura::FindInstancesByClass("PlayerController", false, 100);
    uintptr_t firstNonCdo = 0;
    for (const auto& r : rset.results) {
        if (!r.addr || r.name.find("Default__") != std::string::npos) continue;
        if (!firstNonCdo) firstNonCdo = r.addr;
        uintptr_t cls = Ubel::GetClass(r.addr);
        int32_t playerOff = Ubel::FindFieldOffset(cls, "Player", "Player",
                                                  "Controller", "ObjectProperty");
        if (playerOff >= 0 && ReadPtrAt(r.addr, playerOff)) {
            LOG_INFO("Teleport: local PC 0x%llX via instance scan (Player set)",
                     (unsigned long long)r.addr);
            return r.addr;
        }
    }
    if (firstNonCdo)
        LOG_WARN("Teleport: PC 0x%llX via instance scan WITHOUT Player check",
                 (unsigned long long)firstNonCdo);
    return firstNonCdo;
}

// §5.6: when the Debug Camera is active the LocalPlayer's controller IS the
// DebugCameraController (pawnless). Hop to OriginalControllerRef so
// "fly somewhere with debug camera → save marker" works naturally.
uintptr_t HopThroughDebugCamera(uintptr_t pc) {
    if (!pc) return pc;
    uintptr_t cls = Ubel::GetClass(pc);
    std::string clsName = Ubel::GetName(cls);
    if (clsName.find("DebugCameraController") == std::string::npos) return pc;
    int32_t origOff = Ubel::FindFieldOffset(cls, "OriginalControllerRef",
                                            "OriginalController", nullptr, "ObjectProperty");
    uintptr_t orig = ReadPtrAt(pc, origOff);
    if (orig) {
        LOG_INFO("Teleport: hopped DebugCameraController 0x%llX -> original PC 0x%llX",
                 (unsigned long long)pc, (unsigned long long)orig);
        return orig;
    }
    return pc;
}

uintptr_t ResolvePawn(uintptr_t pc) {
    uintptr_t cls = Ubel::GetClass(pc);
    uintptr_t pawn = ReadPtrAt(pc, Ubel::FindFieldOffset(cls, "Pawn"));
    if (pawn) return pawn;
    return ReadPtrAt(pc, Ubel::FindFieldOffset(cls, "AcknowledgedPawn"));
}

int32_t ResolveChain(Chain& c) {
    c = Chain{};
    c.world = DerefWorld();
    if (!c.world) return TP_ERR_NOT_INIT;

    c.pc = ResolveLocalPC(c.world);
    if (!c.pc) return TP_ERR_NO_CONTROLLER;
    c.pc = HopThroughDebugCamera(c.pc);

    c.pawn = ResolvePawn(c.pc);
    if (!c.pawn) return TP_ERR_NO_PAWN;

    uintptr_t pawnClass = Ubel::GetClass(c.pawn);
    int32_t rootOff = Ubel::FindFieldOffset(pawnClass, "RootComponent",
                                            "RootComponent", nullptr, "ObjectProperty");
    c.root = ReadPtrAt(c.pawn, rootOff);
    if (!c.root) return TP_ERR_REFLECTION;

    uintptr_t rootClass = Ubel::GetClass(c.root);
    FieldInfo fi{};
    if (!Ubel::FindField(rootClass, "RelativeLocation", nullptr, nullptr, nullptr, fi))
        return TP_ERR_REFLECTION;
    c.relLocOff = fi.Offset;
    c.relLocSize = fi.Size;
    c.attachParentOff = Ubel::FindFieldOffset(rootClass, "AttachParent");

    uintptr_t pcClass = Ubel::GetClass(c.pc);
    if (Ubel::FindField(pcClass, "ControlRotation", nullptr, nullptr, nullptr, fi)) {
        c.ctrlRotOff = fi.Offset;
        c.ctrlRotSize = fi.Size;
    }
    return TP_OK;
}

void CopyMapName(uintptr_t world, char* buf, int32_t cap) {
    if (!buf || cap <= 0) return;
    std::string mn = world ? Ubel::GetName(world) : std::string();
    size_t n = (std::min)(mn.size(), static_cast<size_t>(cap - 1));
    std::memcpy(buf, mn.c_str(), n);
    buf[n] = '\0';
}

// ---- pose read (§5.4) ----

int32_t GetPoseImpl(Pose& out, char* mapName, int32_t mapNameCap, uint8_t* outSource,
                    MovementState* move = nullptr, bool* outParentRelative = nullptr) {
    Chain c;
    int32_t rc = ResolveChain(c);
    if (rc != TP_OK) return rc;

    if (outSource) *outSource = 0;
    if (outParentRelative) *outParentRelative = false;
    double xyz[3] = {};
    bool got = false;

    bool attached = (c.attachParentOff >= 0) && ReadPtrAt(c.root, c.attachParentOff) != 0;
    if (attached) {
        // RelativeLocation is parent-relative on attached pawns — ask the
        // engine for world space. Failure degrades to the raw read (better
        // an approximate display than an error).
        FunctionInfo fi;
        if (FindFunc(Ubel::GetClass(c.pawn), "K2_GetActorLocation", fi)
            && fi.parmsSize > 0) {
            std::vector<uint8_t> buf(fi.parmsSize, 0);
            const FunctionParam* rv = FindReturnParam(fi);
            if (rv && Invoke(c.pawn, fi, buf) == 0
                && ParamFits(buf, rv, (rv->size >= 24) ? 24 : 12)) {
                ReadVec3Buf(buf.data() + rv->offset, rv->size, xyz);
                got = true;
                if (outSource) *outSource = 1;
            }
        }
        if (!got) {
            // ⛔⛔ THE DEGRADATION HAS TO LEAVE THIS FUNCTION, not just this log.
            // docs/teleport-spec.md:218-220 says it in as many words: "If the invoke
            // fails (game-thread idle), return the raw values anyway with `source = raw`
            // AND A WARNING FLAG". The fallback shipped; the flag never did. So a healthy
            // unattached world-space read and a degraded PARENT-RELATIVE one both left
            // here as source=0 -- and the UI's own model documents "raw" as MEANING
            // not-attached, so those numbers were displayed as world coordinates, saved
            // into a marker that passes the map guard, and later driven back into the
            // pawn as a world-space destination. [POSEATTACH-2026-09-10].
            //
            // ⚠ It is a separate flag rather than a third `source` value on purpose: the
            // CE mailbox publishes that byte as `[176] source (0=raw, 1=invoke)`, and
            // adding a value would change the MEANING of a contract field -- exactly the
            // change Mimic.h says the surface hash cannot see. The mailbox is deliberately
            // left alone here; the pipe carries the flag.
            if (outParentRelative) *outParentRelative = true;
            LOG_WARN("Teleport: attached pawn but K2_GetActorLocation failed — "
                     "falling back to RelativeLocation (parent-relative!)");
        }
    }
    if (!got && !ReadVec3Mem(c.root + static_cast<uintptr_t>(c.relLocOff),
                             c.relLocSize, xyz))
        return TP_ERR_REFLECTION;

    double pyr[3] = {};
    if (c.ctrlRotOff >= 0)
        ReadVec3Mem(c.pc + static_cast<uintptr_t>(c.ctrlRotOff), c.ctrlRotSize, pyr);

    out.X = xyz[0]; out.Y = xyz[1]; out.Z = xyz[2];
    out.Pitch = pyr[0]; out.Yaw = pyr[1]; out.Roll = pyr[2];
    CopyMapName(c.world, mapName, mapNameCap);
    if (move) {
        ReadMovementState(c, *move);   // resets *move, then fills velocity/accel
        // The location FVector lives on the RootComponent (always resolved when
        // the pose read succeeded). Surface its owner + offset for the "Locate
        // position vector in GWorld" handoff. Set AFTER ReadMovementState since
        // that resets the struct.
        move->LocOwnerAddr   = c.root;
        move->LocFieldOffset = c.relLocOff;
        // The rotation FRotator (ControlRotation) lives on the Controller — for
        // the "Locate rotation in GWorld" handoff.
        if (c.ctrlRotOff >= 0) {
            move->RotOwnerAddr   = c.pc;
            move->RotFieldOffset = c.ctrlRotOff;
        }
    }
    return TP_OK;
}

uintptr_t GetCmc(const Chain& c) {
    int32_t cmOff = Ubel::FindFieldOffset(Ubel::GetClass(c.pawn), "CharacterMovement",
                                          "CharacterMovement", nullptr, "ObjectProperty");
    return ReadPtrAt(c.pawn, cmOff);
}

// Read the pawn's live velocity/acceleration off its CharacterMovement. Pure
// reflected-property memory reads (no invoke), so it is safe on the pipe thread
// exactly like the RelativeLocation read in GetPoseImpl. UMovementComponent::
// Velocity and UCharacterMovementComponent::Acceleration are FVectors whose
// width (12B float / 24B double LWC) is taken from the reflected field Size, so
// this stays UE4/UE5-agnostic. When the pawn has no CharacterMovement (vehicle /
// custom framework), HasMovement stays false and the values stay zero.
void ReadMovementState(const Chain& c, MovementState& m) {
    m = MovementState{};
    m.PawnAddr = c.pawn;

    uintptr_t cmc = GetCmc(c);
    if (!cmc) {
        LOG_INFO("Teleport: no CharacterMovement on pawn — velocity/acceleration unavailable");
        return;
    }
    uintptr_t cmcClass = Ubel::GetClass(cmc);

    FieldInfo vfi{};
    if (Ubel::FindField(cmcClass, "Velocity", nullptr, nullptr, nullptr, vfi)
        && vfi.Offset >= 0) {
        double vel[3] = {};
        if (ReadVec3Mem(cmc + static_cast<uintptr_t>(vfi.Offset), vfi.Size, vel)) {
            m.VelX = vel[0]; m.VelY = vel[1]; m.VelZ = vel[2];
            m.Speed = std::sqrt(vel[0] * vel[0] + vel[1] * vel[1] + vel[2] * vel[2]);
            m.HasMovement = true;
            // Owner + offset of the Velocity FVector — for the "Locate velocity
            // vector in GWorld" handoff (land the path on the CharacterMovement's
            // Velocity field).
            m.VelOwnerAddr   = cmc;
            m.VelFieldOffset = vfi.Offset;
        }
    }

    FieldInfo afi{};
    if (Ubel::FindField(cmcClass, "Acceleration", nullptr, nullptr, nullptr, afi)
        && afi.Offset >= 0) {
        double acc[3] = {};
        if (ReadVec3Mem(cmc + static_cast<uintptr_t>(afi.Offset), afi.Size, acc)) {
            m.AccX = acc[0]; m.AccY = acc[1]; m.AccZ = acc[2];
            // Owner + offset of the Acceleration FVector — for the "Locate
            // acceleration vector in GWorld" handoff (lands on CMC.Acceleration).
            m.AccOwnerAddr   = cmc;
            m.AccFieldOffset = afi.Offset;
        }
    }

    if (!m.HasMovement)
        LOG_INFO("Teleport: CharacterMovement 0x%llX has no reflected Velocity field",
                 (unsigned long long)cmc);
}

// Set UCharacterMovementComponent movement mode (MOVE_None=0 … MOVE_Falling=3).
// BlueprintCallable; "NewMovementMode" is a TEnumAsByte (1 byte). Best-effort.
void SetCmcMode(uintptr_t cmc, uint8_t mode) {
    if (!cmc) return;
    FunctionInfo fi;
    if (!FindFunc(Ubel::GetClass(cmc), "SetMovementMode", fi)) return;
    std::vector<uint8_t> buf((std::max<size_t>)(fi.parmsSize, 1), 0);
    WriteByteParam(buf, fi, "NewMovementMode", mode);
    Invoke(cmc, fi, buf);
}

// Pack + invoke AActor::K2_SetActorLocation(target, bSweep=false, bTeleport=true).
// Returns true when the invoke ran and reported success.
bool InvokeSetActorLocation(uintptr_t pawn, const double xyz[3]) {
    FunctionInfo fi;
    if (!FindFunc(Ubel::GetClass(pawn), "K2_SetActorLocation", fi) || fi.parmsSize <= 0)
        return false;
    std::vector<uint8_t> buf(fi.parmsSize, 0);
    if (!WriteVecParam(buf, fi, "NewLocation", xyz)) return false;
    WriteBoolParam(buf, fi, "bSweep", false);
    WriteBoolParam(buf, fi, "bTeleport", true);
    return Invoke(pawn, fi, buf) == 0 && ReturnedTrue(fi, buf);
}

// Teleport via the ROOT COMPONENT's K2_SetWorldLocation (or K2_SetRelativeLocation
// when not attached). Unlike a raw RelativeLocation write these run
// UpdateComponentToWorld, so the cached world transform the renderer uses is
// refreshed — needed for games (e.g. Octopath Traveler / SE HD-2D) that cook
// AActor::K2_SetActorLocation OUT of reflection, leaving only the component-level
// setters. Returns true if invoked OK.
bool TeleportViaComponent(const Chain& c, const double xyz[3]) {
    if (!c.root) return false;
    uintptr_t rootClass = Ubel::GetClass(c.root);
    bool attached = (c.attachParentOff >= 0) && ReadPtrAt(c.root, c.attachParentOff) != 0;
    const char* names[] = { "K2_SetWorldLocation", "K2_SetRelativeLocation" };
    for (const char* n : names) {
        // K2_SetRelativeLocation only equals world space when not attached.
        if (!attached || std::strcmp(n, "K2_SetWorldLocation") == 0) {
            FunctionInfo fi;
            if (!FindFunc(rootClass, n, fi) || fi.parmsSize <= 0) continue;
            std::vector<uint8_t> buf(fi.parmsSize, 0);
            if (!WriteVecParam(buf, fi, "NewLocation", xyz)) continue;
            WriteBoolParam(buf, fi, "bSweep", false);
            WriteBoolParam(buf, fi, "bTeleport", true);
            if (Invoke(c.root, fi, buf) == 0 && ReturnedTrue(fi, buf)) {
                LOG_INFO("Teleport: %s invoked OK on RootComponent 0x%llX -> (%.1f, %.1f, %.1f)",
                         n, (unsigned long long)c.root, xyz[0], xyz[1], xyz[2]);
                return true;
            }
        }
    }
    return false;
}

// Last-resort "deep force" for games that cook out EVERY K2 location setter
// (SE HD-2D / Octopath): the renderer + CharacterMovement read the cached world
// transform USceneComponent::ComponentToWorld, which a raw RelativeLocation
// write does NOT refresh — and we can't reach UpdateComponentToWorld by
// reflection. So scan a bounded window of the RootComponent for FVector(s)
// holding the OLD world position and overwrite them with the target. The match
// (3 consecutive floats/doubles all within `eps` of oldPos) is extremely
// specific for large world coords, so false hits are near-impossible; the
// component is the only object scanned. Only valid when not attached
// (world == relative). Returns the number of vectors rewritten.
int DeepForceWorldPos(const Chain& c, const double oldPos[3], const double target[3]) {
    if (!c.root || c.relLocSize < 12) return 0;
    // Skip if the old position is degenerate (origin) — would over-match.
    if (std::fabs(oldPos[0]) + std::fabs(oldPos[1]) + std::fabs(oldPos[2]) < 1.0) return 0;

    const bool isDouble = c.relLocSize >= 24;
    const int  vsz  = isDouble ? 24 : 12;
    const int  step = isDouble ? 8  : 4;
    constexpr int kWindow = 0x400;             // USceneComponent/CapsuleComponent ≫ 1KB
    constexpr double kEps2 = 100.0;            // (10 uu)² — tolerate ~1 frame of drift
    uint8_t buf[kWindow];
    if (!Macht::ReadBytesSafe(c.root, buf, kWindow)) return 0;

    int rewrote = 0;
    uint8_t rawTarget[24] = {};
    WriteVec3(rawTarget, c.relLocSize, target);
    for (int o = 0; o + vsz <= kWindow; o += step) {
        double v[3];
        ReadVec3Buf(buf + o, c.relLocSize, v);
        double dx = v[0]-oldPos[0], dy = v[1]-oldPos[1], dz = v[2]-oldPos[2];
        if (dx*dx + dy*dy + dz*dz <= kEps2) {
            // ⛔ COUNT WHAT LANDED, NOT WHAT WAS ATTEMPTED. `rewrote` used to increment
            // whether or not the write succeeded, and it is the function's whole output: it
            // is returned, and it selects between "deep-force rewrote N vector(s)" and the
            // "visual may not move" WARNING below. A run where every write was refused
            // therefore logged a confident N and SUPPRESSED the one warning an operator has.
            // Adjudicated 2026-09-09 as slice B of the unadjudicated sweep claims.
            if (Macht::WriteBytes(c.root + static_cast<uintptr_t>(o), rawTarget, vsz))
                rewrote++;
        }
    }
    if (rewrote)
        LOG_INFO("Teleport: deep-force rewrote %d world-transform vector(s) on "
                 "RootComponent 0x%llX (no K2 setter available)",
                 rewrote, (unsigned long long)c.root);
    else
        LOG_WARN("Teleport: deep-force found no ComponentToWorld vector matching "
                 "the old position — visual may not move");
    return rewrote;
}

// Invoke AActor::K2_GetActorLocation → world-space FVector. Returns false when
// the function / return param can't be resolved.
bool GetActorWorld(uintptr_t pawn, double out[3]) {
    FunctionInfo fi;
    if (!FindFunc(Ubel::GetClass(pawn), "K2_GetActorLocation", fi) || fi.parmsSize <= 0)
        return false;
    std::vector<uint8_t> buf(fi.parmsSize, 0);
    const FunctionParam* rv = FindReturnParam(fi);
    if (!rv || Invoke(pawn, fi, buf) != 0) return false;
    int32_t need = (rv->size >= 24) ? 24 : 12;
    if (rv->offset < 0 || rv->offset + need > static_cast<int32_t>(buf.size())) return false;
    ReadVec3Buf(buf.data() + rv->offset, rv->size, out);
    return true;
}

// ---- camera POV read ----

// Invoke a no-arg getter whose return value is an FVector/FRotator (3×float or
// 3×double per LWC). Returns false when the function / return param can't be
// resolved or the invoke fails (game thread idle).
bool InvokeRetVec(uintptr_t instance, const char* fn, double out[3]) {
    FunctionInfo fi;
    if (!FindFunc(Ubel::GetClass(instance), fn, fi) || fi.parmsSize <= 0) return false;
    std::vector<uint8_t> buf(fi.parmsSize, 0);
    const FunctionParam* rv = FindReturnParam(fi);
    if (!rv || Invoke(instance, fi, buf) != 0) return false;
    int32_t need = (rv->size >= 24) ? 24 : 12;
    if (rv->offset < 0 || rv->offset + need > static_cast<int32_t>(buf.size())) return false;
    ReadVec3Buf(buf.data() + rv->offset, rv->size, out);
    return true;
}

// Invoke a no-arg getter returning a float/double scalar (e.g. GetFOVAngle).
bool InvokeRetFloat(uintptr_t instance, const char* fn, double& out) {
    FunctionInfo fi;
    if (!FindFunc(Ubel::GetClass(instance), fn, fi) || fi.parmsSize <= 0) return false;
    std::vector<uint8_t> buf(fi.parmsSize, 0);
    const FunctionParam* rv = FindReturnParam(fi);
    if (!rv || rv->offset < 0 || Invoke(instance, fi, buf) != 0) return false;
    if (rv->size == 8 && rv->offset + 8 <= static_cast<int32_t>(buf.size())) {
        std::memcpy(&out, buf.data() + rv->offset, 8);
        return true;
    }
    if (rv->offset + 4 <= static_cast<int32_t>(buf.size())) {
        float f = 0;
        std::memcpy(&f, buf.data() + rv->offset, 4);
        out = f;
        return true;
    }
    return false;
}

// Best-effort pawn world location for the camera-vs-pawn delta. Reads the root
// RelativeLocation (== world when not attached); leaves out untouched on failure.
bool ReadPawnWorld(uintptr_t pawn, double out[3]) {
    if (!pawn) return false;
    uintptr_t pawnClass = Ubel::GetClass(pawn);
    int32_t rootOff = Ubel::FindFieldOffset(pawnClass, "RootComponent",
                                            "RootComponent", nullptr, "ObjectProperty");
    uintptr_t root = ReadPtrAt(pawn, rootOff);
    if (!root) return false;
    FieldInfo rfi{};
    if (!Ubel::FindField(Ubel::GetClass(root), "RelativeLocation",
                         nullptr, nullptr, nullptr, rfi))
        return false;
    return ReadVec3Mem(root + static_cast<uintptr_t>(rfi.Offset), rfi.Size, out);
}

// Read FStructProperty::Struct (the inner UScriptStruct*) from a StructProperty
// FieldInfo, so a nested struct can be walked. Mirrors the probe Ubel uses in
// its value walk (DynOff::FSTRUCTPROP_STRUCT ± a few slots, validated by a
// readable UScriptStruct name). Returns 0 when not a struct field / unresolved.
uintptr_t StructInner(const FieldInfo& fi) {
    if (fi.TypeName != "StructProperty" || !fi.Address) return 0;
    static const int kProbes[] = { 0, 4, -4, 8, -8, 0x10, -0x10 };
    for (int d : kProbes) {
        int off = DynOff::FSTRUCTPROP_STRUCT + d;
        if (off < 0) continue;
        uintptr_t cand = 0;
        if (!Macht::ReadSafe(fi.Address + static_cast<uintptr_t>(off), cand) || !cand)
            continue;
        if (!Grimoire::IsUserspacePointer(cand)) continue;
        std::string sn = Ubel::GetName(cand);
        if (!sn.empty() && static_cast<unsigned char>(sn[0]) >= 0x20
            && static_cast<unsigned char>(sn[0]) < 0x7F)
            return cand;
    }
    return 0;
}

// Raw-read fallback for the camera POV: when the BlueprintCallable getters exist
// but ProcessEvent yields nothing (observed on TQ2 / Octopath — getters found
// but the invoke returns no value), read the cached POV directly. Chain (every
// offset/size resolved by reflection, no hardcoded struct layout):
//   APlayerCameraManager.CameraCachePrivate (FCameraCacheEntry, StructProperty)
//     -> POV (FMinimalViewInfo, StructProperty)
//        -> Location (FVector) / Rotation (FRotator) / FOV (float).
// CameraCachePrivate is private but still reflected on the titles seen so far;
// older UE4 may expose it as the public "CameraCache" (contains-match covers it).
bool ReadPovRaw(uintptr_t cam, double loc[3], double rot[3], double& fov) {
    uintptr_t camClass = Ubel::GetClass(cam);
    FieldInfo fiCache{};
    if (!Ubel::FindField(camClass, "CameraCachePrivate", "CameraCache",
                         nullptr, "StructProperty", fiCache))
        return false;
    uintptr_t cacheStruct = StructInner(fiCache);
    if (!cacheStruct) return false;

    FieldInfo fiPov{};
    if (!Ubel::FindField(cacheStruct, "POV", "POV", nullptr, "StructProperty", fiPov))
        return false;
    uintptr_t minView = StructInner(fiPov);
    if (!minView) return false;

    FieldInfo fiLoc{}, fiRot{};
    if (!Ubel::FindField(minView, "Location", nullptr, nullptr, nullptr, fiLoc)
        || !Ubel::FindField(minView, "Rotation", nullptr, nullptr, nullptr, fiRot))
        return false;

    uintptr_t povBase = cam + static_cast<uintptr_t>(fiCache.Offset)
                            + static_cast<uintptr_t>(fiPov.Offset);
    if (!ReadVec3Mem(povBase + static_cast<uintptr_t>(fiLoc.Offset), fiLoc.Size, loc))
        return false;
    ReadVec3Mem(povBase + static_cast<uintptr_t>(fiRot.Offset), fiRot.Size, rot);

    FieldInfo fiFov{};
    if (Ubel::FindField(minView, "FOV", nullptr, nullptr, nullptr, fiFov)) {
        float f = 0;
        if (Macht::ReadSafe(povBase + static_cast<uintptr_t>(fiFov.Offset), f)) fov = f;
    }
    return true;
}

// Resolve the intra-camera-manager offsets of the cached POV Location FVector and
// FOV float (CameraCachePrivate.POV.Location / .FOV) — for the "Locate in GWorld"
// handoff. Same reflection chain as ReadPovRaw, but returns offsets instead of
// reading. Works regardless of which read path produced the live POV values.
// outLocOff / outFovOff are -1 when unresolved.
void ResolvePovOffsets(uintptr_t cam, int32_t& outLocOff, int32_t& outRotOff,
                       int32_t& outFovOff) {
    outLocOff = -1;
    outRotOff = -1;
    outFovOff = -1;
    if (!cam) return;
    uintptr_t camClass = Ubel::GetClass(cam);
    FieldInfo fiCache{};
    if (!Ubel::FindField(camClass, "CameraCachePrivate", "CameraCache",
                         nullptr, "StructProperty", fiCache))
        return;
    uintptr_t cacheStruct = StructInner(fiCache);
    if (!cacheStruct) return;
    FieldInfo fiPov{};
    if (!Ubel::FindField(cacheStruct, "POV", "POV", nullptr, "StructProperty", fiPov))
        return;
    uintptr_t minView = StructInner(fiPov);
    if (!minView) return;
    int32_t povBaseOff = fiCache.Offset + fiPov.Offset;
    FieldInfo fiLoc{};
    if (Ubel::FindField(minView, "Location", nullptr, nullptr, nullptr, fiLoc)
        && fiLoc.Offset >= 0)
        outLocOff = povBaseOff + fiLoc.Offset;
    FieldInfo fiRot{};
    if (Ubel::FindField(minView, "Rotation", nullptr, nullptr, nullptr, fiRot)
        && fiRot.Offset >= 0)
        outRotOff = povBaseOff + fiRot.Offset;
    FieldInfo fiFov{};
    if (Ubel::FindField(minView, "FOV", nullptr, nullptr, nullptr, fiFov)
        && fiFov.Offset >= 0)
        outFovOff = povBaseOff + fiFov.Offset;
}

int32_t GetPovImpl(Pov& out) {
    out = Pov{};
    uintptr_t world = DerefWorld();
    if (!world) return TP_ERR_NOT_INIT;
    uintptr_t pc = ResolveLocalPC(world);
    if (!pc) return TP_ERR_NO_CONTROLLER;
    // NOTE: deliberately NO HopThroughDebugCamera here — the POV should reflect
    // the ACTIVE on-screen view. When the Debug Camera is on, the LocalPlayer's
    // controller IS the DebugCameraController and ITS camera manager produces the
    // free-fly view the user is looking at, which is what we want to report.

    uintptr_t pcClass = Ubel::GetClass(pc);
    int32_t camOff = Ubel::FindFieldOffset(pcClass, "PlayerCameraManager",
                                           "PlayerCameraManager", nullptr, "ObjectProperty");
    uintptr_t cam = ReadPtrAt(pc, camOff);
    if (!cam) {
        LOG_WARN("Teleport: POV failed — PlayerCameraManager unresolved (camOff=%d) on "
                 "PC class '%s'", camOff, Ubel::GetName(pcClass).c_str());
        return TP_ERR_REFLECTION;
    }

    double loc[3] = {}, rot[3] = {}, fov = 0;
    uint8_t source = 0;   // 0 = invoke getters, 1 = raw cached-POV read
    // Skip the game-thread getters entirely when the game thread is not ticking
    // (paused / suspended). Each InvokeRetVec would otherwise enqueue a
    // ProcessEvent call and block for the full invoke timeout (default 5s) that a
    // paused thread can never service — two per poll = a ~10s stall that
    // serializes behind every other pipe command (this is exactly what starved
    // the object-list load when a user paused mid-scan). When stalled, fall
    // straight through to the raw cached-POV read below, which needs no game
    // thread; it resumes invoking automatically once the thread ticks again.
    bool gotLoc = false, gotRot = false;
    if (Stark::IsGameThreadResponsive()) {
        gotLoc = InvokeRetVec(cam, "GetCameraLocation", loc);
        gotRot = InvokeRetVec(cam, "GetCameraRotation", rot);
    } else {
        LOG_INFO("Teleport: POV getters skipped — game thread not ticking "
                 "(paused/suspended), using raw cached POV");
    }

    if (gotLoc || gotRot) {
        InvokeRetFloat(cam, "GetFOVAngle", fov);   // best-effort bonus
        // Rare partial case: backfill a missing component from the cached POV.
        if (!gotLoc || !gotRot) {
            double rl[3] = {}, rr[3] = {}, rf = 0;
            if (ReadPovRaw(cam, rl, rr, rf)) {
                if (!gotLoc) { loc[0] = rl[0]; loc[1] = rl[1]; loc[2] = rl[2]; }
                if (!gotRot) { rot[0] = rr[0]; rot[1] = rr[1]; rot[2] = rr[2]; }
            }
        }
    } else {
        // Both invoke getters yielded nothing. On hard-stripped Shipping builds
        // (TQ2 / Octopath) the getters are present in reflection but ProcessEvent
        // returns no value — read the reflected cached POV directly instead.
        if (ReadPovRaw(cam, loc, rot, fov)) {
            source = 1;
            LOG_INFO("Teleport: POV via raw cached-POV read (invoke getters yielded nothing)");
        } else {
            uintptr_t camClass = Ubel::GetClass(cam);
            FunctionInfo probe;
            bool hasLocFn = FindFunc(camClass, "GetCameraLocation", probe);
            bool hasRotFn = FindFunc(camClass, "GetCameraRotation", probe);
            int32_t cacheOff = Ubel::FindFieldOffset(camClass, "CameraCachePrivate",
                                                     "CameraCache", nullptr, nullptr);
            LOG_WARN("Teleport: POV failed on '%s' (cam=0x%llX) — getters found loc=%d "
                     "rot=%d, raw cached-POV read also failed. CameraCache off=%d.",
                     Ubel::GetName(camClass).c_str(), (unsigned long long)cam,
                     hasLocFn ? 1 : 0, hasRotFn ? 1 : 0, cacheOff);
            return TP_ERR_INVOKE;
        }
    }

    out.Cam.X = loc[0]; out.Cam.Y = loc[1]; out.Cam.Z = loc[2];
    out.Cam.Pitch = rot[0]; out.Cam.Yaw = rot[1]; out.Cam.Roll = rot[2];
    out.Fov = fov;
    out.Source = source;

    // Surface the cached-POV field addresses for "Locate in GWorld" (best-effort;
    // independent of whether the live values came from getters or the raw read).
    int32_t locOff = -1, rotOff = -1, fovOff = -1;
    ResolvePovOffsets(cam, locOff, rotOff, fovOff);
    out.CamOwnerAddr      = cam;
    out.CamLocFieldOffset = locOff;
    out.CamRotFieldOffset = rotOff;
    out.CamFovFieldOffset = fovOff;

    // Best-effort pawn world position for the delta display (resolve the pawn
    // via the REAL controller, hopping past the debug camera if it's active).
    double pxyz[3] = {};
    if (ReadPawnWorld(ResolvePawn(HopThroughDebugCamera(pc)), pxyz)) {
        out.HasPawn = true;
        out.Pawn.X = pxyz[0]; out.Pawn.Y = pxyz[1]; out.Pawn.Z = pxyz[2];
    }
    LOG_INFO("Teleport: POV cam=(%.1f, %.1f, %.1f) rot=(%.1f, %.1f, %.1f) fov=%.1f hasPawn=%d src=%s",
             out.Cam.X, out.Cam.Y, out.Cam.Z, out.Cam.Pitch, out.Cam.Yaw, out.Cam.Roll,
             out.Fov, out.HasPawn ? 1 : 0, out.Source == 1 ? "raw" : "invoke");
    return TP_OK;
}

// ---- teleport write (§5.5) ----

// DIAGNOSTIC (build 1113, gated): a permanent low-noise "separate visible actor"
// detector. On normal games the possessed pawn IS the camera's view target, so this
// stays SILENT. It logs ONLY when APlayerCameraManager.ViewTarget.Target is a
// DIFFERENT actor than the possessed pawn (a genuine separate-actor game — none seen
// yet; TQ2 / SEED / Octopath all resolve view-target == pawn) or when ViewTarget
// can't be resolved at all. ViewTarget is read raw via the StructInner /
// FStructProperty walk (the camera-manager invoke getters return nothing on some
// titles). Fires at most once per teleport (a deliberate user action).
//
// Build 1114 found the corollary: when the pawn IS the view target yet the on-screen
// body doesn't move (TQ2), the cause is NOT a separate actor but a stale cached world
// transform after a raw write — fixed in the CMC-freeze path below by always running
// the component setter (K2_SetWorldLocation) + deep-force. So the once-off Root/Mesh
// dump that proved this is removed; this detector remains only for the genuinely
// different-view-target case it was built to catch.
void DiagVisibleActor(const Chain& c) {
    int32_t camOff = Ubel::FindFieldOffset(Ubel::GetClass(c.pc), "PlayerCameraManager",
                                           "PlayerCameraManager", nullptr, "ObjectProperty");
    uintptr_t cam = ReadPtrAt(c.pc, camOff);
    if (!cam) return;   // no camera manager to compare against — stay silent

    // ViewTarget.Target (raw): ViewTarget (FTViewTarget StructProperty, exact match
    // excludes PendingViewTarget) -> Target (ObjectProperty -> AActor*).
    uintptr_t camClass = Ubel::GetClass(cam);
    FieldInfo fiVT{};
    uintptr_t vtActor = 0;
    if (Ubel::FindField(camClass, "ViewTarget", nullptr, nullptr, "StructProperty", fiVT)) {
        uintptr_t vtStruct = StructInner(fiVT);   // FTViewTarget UScriptStruct
        if (vtStruct) {
            FieldInfo fiTgt{};
            if (Ubel::FindField(vtStruct, "Target", "Target", nullptr, nullptr, fiTgt))
                vtActor = ReadPtrAt(cam, static_cast<uintptr_t>(fiVT.Offset + fiTgt.Offset));
        }
    }

    if (vtActor == c.pawn) return;   // normal: camera follows the possessed pawn — silent

    if (!vtActor) {
        LOG_WARN("Teleport diag: ViewTarget.Target UNRESOLVED on camera class '%s' — can't "
                 "check for a separate visible actor (ViewTarget not reflected?)",
                 Ubel::GetName(camClass).c_str());
        return;
    }
    uintptr_t vtClass = Ubel::GetClass(vtActor);
    double vtLoc[3] = {}, pawnLoc[3] = {};
    ReadPawnWorld(vtActor, vtLoc);
    ReadVec3Mem(c.root + static_cast<uintptr_t>(c.relLocOff), c.relLocSize, pawnLoc);
    int32_t meshOff = Ubel::FindFieldOffset(vtClass, "Mesh", "Mesh", nullptr, "ObjectProperty");
    int32_t cmcOff  = Ubel::FindFieldOffset(vtClass, "CharacterMovement",
                                            "CharacterMovement", nullptr, "ObjectProperty");
    LOG_WARN("Teleport diag: SEPARATE visible actor — camera follows 0x%llX '%s' "
             "loc=(%.1f, %.1f, %.1f) but possessed pawn is 0x%llX '%s' loc=(%.1f, %.1f, %.1f) "
             "(view-target hasMesh=%d hasCMC=%d). Candidate actor to teleport instead.",
             (unsigned long long)vtActor, Ubel::GetName(vtClass).c_str(),
             vtLoc[0], vtLoc[1], vtLoc[2],
             (unsigned long long)c.pawn, Ubel::GetName(Ubel::GetClass(c.pawn)).c_str(),
             pawnLoc[0], pawnLoc[1], pawnLoc[2], meshOff >= 0 ? 1 : 0, cmcOff >= 0 ? 1 : 0);
}

// Move the pawn to xyz. Tier 1 = invoke (preferTeleportTo: K2_TeleportTo with
// FindTeleportSpot adjust; else K2_SetActorLocation bSweep=false bTeleport=true
// for an exact landing). Tier 2 = raw RelativeLocation write.
// destPyr (nullable) is only consumed by the K2_TeleportTo DestRotation param;
// rotation restore for the recall path happens separately in SetRotation.
int32_t TeleportPawnTo(const Chain& c, const double xyz[3], const double* destPyr,
                       bool preferTeleportTo, uint8_t* tierOut) {
    uintptr_t pawnClass = Ubel::GetClass(c.pawn);
    bool moved = false;

    DiagVisibleActor(c);   // build 1113: log pawn vs camera ViewTarget (see above)

    // Capture the pre-move world position (RelativeLocation == world when the
    // root isn't attached) so the deep-force fallback can locate the stale
    // ComponentToWorld vector if every K2 setter turns out to be cooked out.
    double oldPos[3] = {};
    ReadVec3Mem(c.root + static_cast<uintptr_t>(c.relLocOff), c.relLocSize, oldPos);

    FunctionInfo fi;
    bool haveFn = FindFunc(pawnClass,
                           preferTeleportTo ? "K2_TeleportTo" : "K2_SetActorLocation", fi);
    if (!haveFn)
        haveFn = FindFunc(pawnClass,
                          preferTeleportTo ? "K2_SetActorLocation" : "K2_TeleportTo", fi);

    if (haveFn && fi.parmsSize > 0) {
        std::vector<uint8_t> buf(fi.parmsSize, 0);
        bool isTeleportTo = IEquals(fi.name, "K2_TeleportTo");
        bool packed;
        if (isTeleportTo) {
            packed = WriteVecParam(buf, fi, "DestLocation", xyz);
            double pyr[3] = {};
            if (destPyr) {
                std::memcpy(pyr, destPyr, sizeof(pyr));
            } else if (c.ctrlRotOff >= 0) {
                // keep current facing
                ReadVec3Mem(c.pc + static_cast<uintptr_t>(c.ctrlRotOff),
                            c.ctrlRotSize, pyr);
            }
            WriteVecParam(buf, fi, "DestRotation", pyr);
        } else {
            packed = WriteVecParam(buf, fi, "NewLocation", xyz);
            WriteBoolParam(buf, fi, "bSweep", false);
            WriteBoolParam(buf, fi, "bTeleport", true);
        }
        if (packed) {
            int32_t r = Invoke(c.pawn, fi, buf);
            if (r == 0 && ReturnedTrue(fi, buf)) {
                moved = true;
                LOG_INFO("Teleport: %s invoked OK on pawn 0x%llX -> (%.1f, %.1f, %.1f)",
                         fi.name.c_str(), (unsigned long long)c.pawn,
                         xyz[0], xyz[1], xyz[2]);
            } else {
                LOG_WARN("Teleport: %s %s (r=%d, parmsSize=%u) — trying raw-write fallback",
                         fi.name.c_str(),
                         r == 0 ? "returned false" : "invoke failed", r, fi.parmsSize);
            }
        } else {
            LOG_WARN("Teleport: %s param-pack failed (couldn't locate location param) "
                     "— raw-write fallback", fi.name.c_str());
        }
    } else {
        LOG_WARN("Teleport: no K2_TeleportTo / K2_SetActorLocation on pawn class "
                 "(likely cooked out) — trying RootComponent setters");
    }

    // Tier 1b: component-level K2_SetWorldLocation on the root. Runs
    // UpdateComponentToWorld (the raw write below does NOT), so it actually
    // moves what's rendered when the actor-level setters were cooked out.
    uint8_t tier = 1;
    if (!moved && TeleportViaComponent(c, xyz))
        moved = true;

    bool attachedForWrite = (c.attachParentOff >= 0)
                            && ReadPtrAt(c.root, c.attachParentOff) != 0;
    if (!moved) {
        tier = 2;
        uint8_t raw[24] = {};
        WriteVec3(raw, c.relLocSize, xyz);
        int32_t sz = (c.relLocSize >= 24) ? 24 : 12;
        if (!Macht::WriteBytes(c.root + static_cast<uintptr_t>(c.relLocOff), raw, sz))
            return TP_ERR_WRITE_FAILED;
        LOG_WARN("Teleport: tier-2 raw RelativeLocation write (no transform update "
                 "— may not move the visual)");
        // Deep force: with no K2 setter, also rewrite the cached world transform
        // (ComponentToWorld) so the renderer/CMC actually move. World==relative
        // only when not attached.
        if (!attachedForWrite)
            DeepForceWorldPos(c, oldPos, xyz);
    }
    if (tierOut) *tierOut = tier;

    auto dist3 = [](const double a[3], const double b[3]) {
        double dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
        return std::sqrt(dx * dx + dy * dy + dz * dz);
    };
    bool attached = (c.attachParentOff >= 0)
                    && ReadPtrAt(c.root, c.attachParentOff) != 0;

    // Estimate the actor's CURRENT world position to verify the move.
    // When NOT attached, raw RelativeLocation IS world space — and
    // K2_GetActorLocation has proven unreliable on some games (TQ2 returns
    // (0,0,0)), so trust rawRel in that case. Only when attached do we need
    // K2_GetActorLocation (rawRel would be parent-relative then).
    auto currentWorld = [&](double out[3]) {
        double rawRel[3] = {};
        ReadVec3Mem(c.root + static_cast<uintptr_t>(c.relLocOff), c.relLocSize, rawRel);
        double world[3] = {};
        bool gotWorld = GetActorWorld(c.pawn, world);
        if (attached && gotWorld) { out[0]=world[0]; out[1]=world[1]; out[2]=world[2]; }
        else { out[0]=rawRel[0]; out[1]=rawRel[1]; out[2]=rawRel[2]; }
    };

    constexpr double kReachedEps = 60.0;   // ~ capsule radius
    double cur[3] = {};
    currentWorld(cur);

    // Gated retry: a tier-1 invoke that "succeeded" but left the actor far from
    // the target means the game's CharacterMovement is overriding
    // SetActorLocation (TQ2-class). Freeze the CMC (MOVE_None), force the
    // location BOTH ways (engine invoke + raw RelativeLocation write while
    // frozen — valid since world==relative when not attached), then thaw to
    // Falling so it re-grounds. GATED on "first attempt didn't reach target", so
    // games where the plain move already works (SEED) never enter this path.
    if (moved && dist3(cur, xyz) > kReachedEps) {
        uintptr_t cmc = GetCmc(c);
        LOG_WARN("Teleport: actor didn't reach target (cur=(%.1f,%.1f,%.1f)) — "
                 "forcing via CMC freeze%s", cur[0], cur[1], cur[2],
                 cmc ? "" : " (no CMC found)");
        SetCmcMode(cmc, 0);                          // MOVE_None — freeze physics
        // Do NOT trust K2_SetActorLocation's return here. On some titles (TQ2 —
        // standard Character, capsule root + child mesh) it reports success but is
        // a no-op (CMC reverts), and a raw RelativeLocation write alone leaves the
        // cached ComponentToWorld stale so the rendered mesh stays at the old spot.
        // So ALWAYS also run the component setter — K2_SetWorldLocation runs
        // UpdateComponentToWorld and propagates the refresh to the child mesh — and
        // deep-force the cached world transform as a fallback. (Previously the
        // component setter was gated on the actor setter returning false, so a
        // false-success skipped the one call that refreshes the visual.)
        InvokeSetActorLocation(c.pawn, xyz);
        TeleportViaComponent(c, xyz);                // refresh ComponentToWorld + children
        if (!attached) {                              // raw write (world==relative)
            uint8_t raw[24] = {};
            WriteVec3(raw, c.relLocSize, xyz);
            Macht::WriteBytes(c.root + static_cast<uintptr_t>(c.relLocOff), raw,
                              (c.relLocSize >= 24) ? 24 : 12);
            // Belt-and-suspenders: rewrite any still-stale cached world-transform
            // vectors (catches the case where K2_SetWorldLocation is also a no-op).
            DeepForceWorldPos(c, oldPos, xyz);
        }
        SetCmcMode(cmc, 3);                          // MOVE_Falling — re-ground
        InvokeSetActorLocation(c.pawn, xyz);         // re-assert after thaw…
        TeleportViaComponent(c, xyz);                // …and re-refresh the transform
        currentWorld(cur);
        if (dist3(cur, xyz) <= kReachedEps)
            LOG_INFO("Teleport: CMC-freeze force succeeded -> cur=(%.1f,%.1f,%.1f)",
                     cur[0], cur[1], cur[2]);
        else
            LOG_WARN("Teleport: CMC-freeze force STILL didn't stick "
                     "(cur=(%.1f,%.1f,%.1f)) — the memory write didn't hold; the game "
                     "may re-derive position from a separate source", cur[0], cur[1], cur[2]);
    }

    LOG_INFO("Teleport: post-move cur=(%.1f, %.1f, %.1f) target=(%.1f, %.1f, %.1f) attached=%d",
             cur[0], cur[1], cur[2], xyz[0], xyz[1], xyz[2], attached ? 1 : 0);
    return TP_OK;
}

// Restore Pitch/Yaw/Roll. Invoke AController::SetControlRotation; raw-write
// ControlRotation as fallback (known-safe — the PC re-consumes it per frame).
void SetRotation(const Chain& c, const double pyr[3]) {
    FunctionInfo fi;
    if (FindFunc(Ubel::GetClass(c.pc), "SetControlRotation", fi) && fi.parmsSize > 0) {
        std::vector<uint8_t> buf(fi.parmsSize, 0);
        if (WriteVecParam(buf, fi, "NewRotation", pyr)
            && Invoke(c.pc, fi, buf) == 0)
            return;
    }
    if (c.ctrlRotOff >= 0) {
        uint8_t raw[24] = {};
        WriteVec3(raw, c.ctrlRotSize, pyr);
        Macht::WriteBytes(c.pc + static_cast<uintptr_t>(c.ctrlRotOff), raw,
                          (c.ctrlRotSize >= 24) ? 24 : 12);
    }
}

// Settle the pawn after a teleport so it actually stays put. Two best-effort
// steps, each independently skipped when unavailable:
//   (1) AController::StopMovement — aborts an active move order / path
//       following. Without this, click-to-move ARPGs (Titan Quest-likes)
//       immediately walk the pawn back to its commanded destination, so the
//       teleport "does nothing" even though K2_SetActorLocation succeeded.
//   (2) UCharacterMovementComponent::StopMovementImmediately — zeroes
//       conserved velocity (e.g. fall speed) on Character pawns.
void StopMovement(const Chain& c) {
    FunctionInfo pcStop;
    if (FindFunc(Ubel::GetClass(c.pc), "StopMovement", pcStop)) {
        std::vector<uint8_t> b((std::max<size_t>)(pcStop.parmsSize, 1), 0);
        Invoke(c.pc, pcStop, b);
        LOG_INFO("Teleport: AController::StopMovement invoked on PC 0x%llX",
                 (unsigned long long)c.pc);
    }

    uintptr_t pawnClass = Ubel::GetClass(c.pawn);
    int32_t cmOff = Ubel::FindFieldOffset(pawnClass, "CharacterMovement",
                                          "CharacterMovement", nullptr, "ObjectProperty");
    uintptr_t cm = ReadPtrAt(c.pawn, cmOff);
    if (!cm) {
        LOG_INFO("Teleport: no CharacterMovement on pawn (skip velocity reset)");
        return;
    }
    FunctionInfo fi;
    if (!FindFunc(Ubel::GetClass(cm), "StopMovementImmediately", fi)) return;
    std::vector<uint8_t> buf((std::max<size_t>)(fi.parmsSize, 1), 0);
    Invoke(cm, fi, buf);
    LOG_INFO("Teleport: StopMovementImmediately invoked on CMC 0x%llX",
             (unsigned long long)cm);
}

// ---- feature helpers: directional move + mouse cursor ----

// Normalize a 3-vector in place. Returns false (leaving v untouched) for a
// near-zero vector so a degenerate facing can't produce NaNs.
bool Normalize3(double v[3]) {
    double len = std::sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
    if (len < 1e-6) return false;
    v[0] /= len; v[1] /= len; v[2] /= len;
    return true;
}

// Pawn facing as a unit forward vector. Prefers AActor::GetActorForwardVector
// (BlueprintCallable — the actor's exact orientation); falls back to the
// controller's ControlRotation Yaw/Pitch trig (UE convention:
// X = cos(P)cos(Y), Y = cos(P)sin(Y), Z = sin(P)). horizontalOnly drops Z so the
// move stays on the ground plane. Returns false when neither source is available
// (or the facing is straight up under horizontalOnly).
bool GetForward(const Chain& c, bool horizontalOnly, double out[3]) {
    double f[3] = {};
    bool got = InvokeRetVec(c.pawn, "GetActorForwardVector", f);
    if (!got && c.ctrlRotOff >= 0) {
        double pyr[3] = {};
        if (ReadVec3Mem(c.pc + static_cast<uintptr_t>(c.ctrlRotOff), c.ctrlRotSize, pyr)) {
            constexpr double kDeg2Rad = 3.14159265358979323846 / 180.0;
            double yaw = pyr[1] * kDeg2Rad, pitch = pyr[0] * kDeg2Rad;
            f[0] = std::cos(pitch) * std::cos(yaw);
            f[1] = std::cos(pitch) * std::sin(yaw);
            f[2] = std::sin(pitch);
            got = true;
        }
    }
    if (!got) return false;
    if (horizontalOnly) f[2] = 0.0;     // ground-plane move, preserve Z
    if (!Normalize3(f)) return false;   // facing straight up + horizontalOnly
    out[0] = f[0]; out[1] = f[1]; out[2] = f[2];
    return true;
}

// Resolve APlayerController.bShowMouseCursor down to a single byte address + bit
// mask from the reflected FBoolProperty layout
// ([FieldSize=1, ByteOffset, ByteMask, FieldMask] at FBOOLPROP_FIELDSIZE,
// probed ±). value byte = pc + Property.Offset + ByteOffset; bit = & FieldMask.
// FindField only surfaces FieldMask (not ByteOffset), so the layout is read here
// directly. outPc receives the resolved gameplay PlayerController (for the input-
// mode call). Returns a TeleportResult code (TP_OK on success).
int32_t ResolveCursorBit(uintptr_t& outPc, uintptr_t& byteAddr, uint8_t& mask) {
    uintptr_t world = DerefWorld();
    if (!world) return TP_ERR_NOT_INIT;
    uintptr_t pc = ResolveLocalPC(world);
    if (!pc) return TP_ERR_NO_CONTROLLER;
    pc = HopThroughDebugCamera(pc);   // target the gameplay PC, not a debug cam
    outPc = pc;
    uintptr_t cls = Ubel::GetClass(pc);
    FieldInfo fi{};
    if (!Ubel::FindField(cls, "bShowMouseCursor", nullptr, nullptr, "BoolProperty", fi)
        || !fi.Address)
        return TP_ERR_REFLECTION;
    int baseOff = DynOff::bUseFProperty ? DynOff::FBOOLPROP_FIELDSIZE
                                        : DynOff::UBOOLPROP_FIELDSIZE;
    for (int tryOff : { baseOff, baseOff - 4, baseOff + 4, baseOff + 8, baseOff - 8 }) {
        if (tryOff < 0) continue;
        uint8_t b[4] = {};
        if (!Macht::ReadBytesSafe(fi.Address + tryOff, b, 4)) continue;
        uint8_t fieldSize = b[0], byteOff = b[1], byteMask = b[2], fieldMask = b[3];
        if (fieldSize == 1 && fieldMask && (fieldMask & (fieldMask - 1)) == 0
            && byteOff <= 7 && byteMask && (byteMask & (byteMask - 1)) == 0) {
            byteAddr = pc + static_cast<uintptr_t>(fi.Offset) + byteOff;
            mask = fieldMask;
            return TP_OK;
        }
    }
    LOG_WARN("Teleport: cursor — bShowMouseCursor FBoolProperty layout unresolved");
    return TP_ERR_REFLECTION;
}

// Invoke one UWidgetBlueprintLibrary input-mode setter on the game thread.
// gameAndUI packs the GameAndUI-only params (InWidgetToFocus=null,
// MouseLock=DoNotLock, bHideCursorDuringCapture=FALSE — the false keeps the OS
// cursor visible; the param DEFAULTS to true). Returns false if the function
// isn't reflected.
bool InvokeWblInputMode(uintptr_t wbl, uintptr_t wblClass, const char* fn,
                        uintptr_t pc, bool gameAndUI) {
    FunctionInfo fi;
    if (!FindFunc(wblClass, fn, fi) || fi.parmsSize <= 0) {
        LOG_WARN("Teleport: cursor — %s not found on WidgetBlueprintLibrary", fn);
        return false;
    }
    std::vector<uint8_t> buf(fi.parmsSize, 0);
    WritePtrParam(buf, fi, "PlayerController", pc);
    if (gameAndUI) {
        WriteByteParam(buf, fi, "InMouseLockMode", 0);              // DoNotLock
        WriteBoolParam(buf, fi, "bHideCursorDuringCapture", false);  // keep cursor visible
    }
    Invoke(wbl, fi, buf);
    LOG_INFO("Teleport: cursor — %s invoked on PC 0x%llX", fn, (unsigned long long)pc);
    return true;
}

// Drive the player input mode so the cursor isn't recaptured/hidden by a GameOnly
// viewport. Best-effort: titles without UMG keep just the bShowMouseCursor write.
// Game thread (touches slate/viewport) — invoked through Stark.
//
// show=true forces a GameOnly→GameAndUI TRANSITION: on some titles a single
// GameAndUI call from the game's running input state doesn't release the mouse
// capture / show the cursor until the mode actually CHANGES (observed live: the
// first force shows nothing, but OFF-then-ON works). Cycling through GameOnly
// first reproduces that transition in one action.
void ApplyCursorInputMode(uintptr_t pc, bool show) {
    if (!pc) return;
    uintptr_t wbl = UE5_FindInstanceOfClass("WidgetBlueprintLibrary");
    if (!wbl) {
        LOG_INFO("Teleport: cursor — no WidgetBlueprintLibrary (UMG cooked out?); "
                 "bShowMouseCursor write only (input mode may still hide it)");
        return;
    }
    uintptr_t wblClass = Ubel::GetClass(wbl);
    if (show) {
        InvokeWblInputMode(wbl, wblClass, "SetInputMode_GameOnly", pc, false);
        InvokeWblInputMode(wbl, wblClass, "SetInputMode_GameAndUIEx", pc, true);
    } else {
        InvokeWblInputMode(wbl, wblClass, "SetInputMode_GameOnly", pc, false);
    }
}

int32_t RecallTo(const Pose& p, bool restoreRot, uint8_t* tierOut) {
    Chain c;
    int32_t rc = ResolveChain(c);
    if (rc != TP_OK) return rc;

    double xyz[3] = { p.X, p.Y, p.Z };
    double pyr[3] = { p.Pitch, p.Yaw, p.Roll };
    rc = TeleportPawnTo(c, xyz, restoreRot ? pyr : nullptr,
                        /*preferTeleportTo=*/false, tierOut);
    if (rc != TP_OK) return rc;
    if (restoreRot) SetRotation(c, pyr);
    StopMovement(c);
    return TP_OK;
}

// Capture the current pose into the system "last" slot just before a jump, so a
// failed/unwanted teleport can be undone via Wirbel::RecallLast. Best-effort: an
// unreadable pose (menu / no pawn) leaves the previous last position intact, and
// the about-to-run teleport will fail on its own. Caller MUST already hold
// s_opMutex (uses the lock-free GetPoseImpl, not the public GetPose export).
void SaveLastImpl() {
    Marker m{};
    if (GetPoseImpl(m.P, m.MapName, sizeof(m.MapName), nullptr, nullptr, &m.ParentRelative) != TP_OK) return;
    m.Valid = true;
    s_lastMarker = m;
    LOG_INFO("Teleport: last position auto-saved (%.1f, %.1f, %.1f) map='%s'",
             m.P.X, m.P.Y, m.P.Z, m.MapName);
}

} // namespace

namespace Wirbel {

int32_t GetPose(Pose& out, char* mapName, int32_t mapNameCap, uint8_t* outSource,
                bool* outParentRelative) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    return GetPoseImpl(out, mapName, mapNameCap, outSource, nullptr, outParentRelative);
}

int32_t GetPoseAndMovement(Pose& out, char* mapName, int32_t mapNameCap,
                           uint8_t* outSource, MovementState& move,
                           bool* outParentRelative) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    move = MovementState{};
    return GetPoseImpl(out, mapName, mapNameCap, outSource, &move, outParentRelative);
}

int32_t GetPov(Pov& out) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    return GetPovImpl(out);
}

// Standalone-trainer offset bundle (no-DLL trainer export). Reuses the teleport
// resolution chain, then decomposes *GWorld->Pawn into bake-able (offset, deref)
// hops and gathers the pawn/root/CMC field offsets a standalone CE-Lua trainer
// needs. Read-only reflection — no writes, no invokes.
int32_t GetTrainerOffsets(TrainerOffsets& out) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    out = TrainerOffsets{};

    Chain c;
    int32_t rc = ResolveChain(c);
    if (rc != TP_OK) return rc;

    auto push = [&](const char* name, int32_t off, bool deref) {
        if (off < 0 || out.ChainCount >= 16) return;
        TrainerChainHop& h = out.Chain[out.ChainCount++];
        size_t n = 0;
        while (name[n] && n + 1 < sizeof(h.Field)) { h.Field[n] = name[n]; ++n; }
        h.Field[n] = '\0';
        h.Offset = off;
        h.Deref = deref;
    };

    // *GWorld -> OwningGameInstance
    uintptr_t worldClass = Ubel::GetClass(c.world);
    int32_t giOff = Ubel::FindFieldOffset(worldClass, "OwningGameInstance",
                                          "GameInstance", nullptr, "ObjectProperty");
    if (giOff < 0) return TP_ERR_REFLECTION;
    push("OwningGameInstance", giOff, true);
    uintptr_t gi = ReadPtrAt(c.world, giOff);
    if (!gi) return TP_ERR_REFLECTION;

    // GameInstance -> LocalPlayers (TArray Data ptr) -> [0]
    uintptr_t giClass = Ubel::GetClass(gi);
    int32_t lpOff = Ubel::FindFieldOffset(giClass, "LocalPlayers", "LocalPlayers",
                                          nullptr, "ArrayProperty");
    if (lpOff < 0) return TP_ERR_REFLECTION;
    push("LocalPlayers", lpOff, true);   // -> TArray Data pointer
    push("LocalPlayers[0]", 0, true);    // -> element[0] (ULocalPlayer*)
    Macht::TArrayView arr;
    if (!Macht::ReadTArray(gi + static_cast<uintptr_t>(lpOff), arr) || arr.Count <= 0)
        return TP_ERR_REFLECTION;
    uintptr_t lp = Macht::ReadTArrayElement(arr, 0);
    if (!lp) return TP_ERR_REFLECTION;

    // LocalPlayer -> PlayerController
    uintptr_t lpClass = Ubel::GetClass(lp);
    int32_t pcOff = Ubel::FindFieldOffset(lpClass, "PlayerController",
                                          "PlayerController", nullptr, "ObjectProperty");
    if (pcOff < 0) return TP_ERR_REFLECTION;
    push("PlayerController", pcOff, true);

    // PlayerController -> Pawn (resolve the offset on the RAW chain PC's class, not
    // a debug-camera-hopped controller — the baked chain is the normal one).
    uintptr_t rawPc = ReadPtrAt(lp, pcOff);
    uintptr_t pcChainClass = Ubel::GetClass(rawPc ? rawPc : c.pc);
    const char* pawnField = "Pawn";
    int32_t pawnOff = Ubel::FindFieldOffset(pcChainClass, "Pawn");
    if (pawnOff < 0) {
        pawnField = "AcknowledgedPawn";
        pawnOff = Ubel::FindFieldOffset(pcChainClass, "AcknowledgedPawn");
    }
    if (pawnOff < 0) return TP_ERR_REFLECTION;
    push(pawnField, pawnOff, true);

    // Offsets hanging off the pawn (reuse what ResolveChain already resolved).
    uintptr_t pawnClass = Ubel::GetClass(c.pawn);
    out.PawnToRoot   = Ubel::FindFieldOffset(pawnClass, "RootComponent",
                                             "RootComponent", nullptr, "ObjectProperty");
    out.RootToRelLoc = c.relLocOff;
    out.FVectorWidth = c.relLocSize;
    out.CtrlRotOff   = c.ctrlRotOff;
    out.CtrlRotSize  = c.ctrlRotSize;
    // Pawn -> Controller back-ref (the pure-Lua fly reads ControlRotation off it).
    out.PawnToController = Ubel::FindFieldOffset(pawnClass, "Controller",
                                                 "Controller", nullptr, "ObjectProperty");

    // CharacterMovement + float knobs (MaxWalkSpeed / GravityScale / JumpZVelocity)
    // + the fly fields (MovementMode / Velocity), mirroring Dunste::ResolveCtx.
    out.PawnToCmc = Ubel::FindFieldOffset(pawnClass, "CharacterMovement",
                                          "CharacterMovement", nullptr, "ObjectProperty");
    if (out.PawnToCmc >= 0) {
        uintptr_t cmc = ReadPtrAt(c.pawn, out.PawnToCmc);
        if (cmc) {
            uintptr_t cmcClass = Ubel::GetClass(cmc);
            out.WalkSpeedOff = Ubel::FindFieldOffset(cmcClass, "MaxWalkSpeed", "WalkSpeed",
                                                     nullptr, "FloatProperty");
            out.GravityOff   = Ubel::FindFieldOffset(cmcClass, "GravityScale", "GravityScale",
                                                     nullptr, "FloatProperty");
            out.JumpOff      = Ubel::FindFieldOffset(cmcClass, "JumpZVelocity", "JumpZVelocity",
                                                     nullptr, "FloatProperty");
            // MovementMode: exclude the sibling CustomMovementMode byte (matches Dunste).
            out.MoveModeOff  = Ubel::FindFieldOffset(cmcClass, "MovementMode", "MovementMode",
                                                     "Custom", nullptr);
            FieldInfo vf{};
            if (Ubel::FindField(cmcClass, "Velocity", "Velocity", nullptr, "StructProperty", vf)
                && vf.Offset >= 0) {
                out.VelocityOff  = vf.Offset;
                out.VelocitySize = vf.Size;
            }
        }
    }
    return TP_OK;
}

int32_t SaveMarker(int32_t slot) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    if (slot < 0 || slot >= Grimoire::TELEPORT_SLOTS) return TP_ERR_EMPTY_MARKER;
    Marker m{};
    // [W2-MARKER-PARENTREL] Capture the read's parent-relative flag with the pose: the pose card warns "do not
    // save these as a marker", and the save path used to have no way to know.
    int32_t rc = GetPoseImpl(m.P, m.MapName, sizeof(m.MapName), nullptr, nullptr, &m.ParentRelative);
    if (rc != TP_OK) return rc;
    m.Valid = true;
    s_markers[slot] = m;
    if (m.ParentRelative)
        LOG_WARN("Teleport: marker %d saved from a PARENT-RELATIVE read -- its pose is not world coordinates",
                 slot);
    LOG_INFO("Teleport: marker %d saved (%.1f, %.1f, %.1f) map='%s'",
             slot, m.P.X, m.P.Y, m.P.Z, m.MapName);
    return TP_OK;
}

int32_t RecallMarker(int32_t slot, bool force, uint8_t* tierOut) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    if (slot < 0 || slot >= Grimoire::TELEPORT_SLOTS) return TP_ERR_EMPTY_MARKER;
    if (!s_markers[slot].Valid) return TP_ERR_EMPTY_MARKER;
    Marker m = s_markers[slot];

    if (!force) {
        // Cross-map recall = void fall / unloaded streaming cell (§11 caveat 5).
        char cur[Grimoire::TELEPORT_MAPNAME_CAP] = {};
        CopyMapName(DerefWorld(), cur, sizeof(cur));
        if (!IEquals(std::string(cur), m.MapName)) {
            LOG_WARN("Teleport: recall %d refused — map '%s' != marker map '%s'",
                     slot, cur, m.MapName);
            return TP_ERR_MAP_MISMATCH;
        }
    }
    SaveLastImpl();   // remember where we were, in case this recall goes wrong
    int32_t rc = RecallTo(m.P, /*restoreRot=*/true, tierOut);
    LOG_INFO("Teleport: recall %d -> rc=%d", slot, rc);
    return rc;
}

int32_t RecallExplicit(const Pose& pose, bool hasRot, uint8_t* tierOut) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    SaveLastImpl();   // remember where we were, in case this BugItGo goes wrong
    int32_t rc = RecallTo(pose, hasRot, tierOut);
    LOG_INFO("Teleport: explicit recall (%.1f, %.1f, %.1f) rot=%d -> rc=%d",
             pose.X, pose.Y, pose.Z, hasRot ? 1 : 0, rc);
    return rc;
}

int32_t TeleportToCursor(double zOffset, int32_t traceChannel,
                         bool fallbackToCenter, Pose* outHit,
                         uint8_t* tierOut, bool* outUsedCenter) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    Chain c;
    int32_t rc = ResolveChain(c);
    if (rc != TP_OK) return rc;

    uintptr_t pcClass = Ubel::GetClass(c.pc);
    FunctionInfo fi;

    // 1. Mouse position (screen-space floats in every UE version).
    double mx = 0, my = 0;
    bool haveMouse = false;
    if (FindFunc(pcClass, "GetMousePosition", fi) && fi.parmsSize > 0) {
        std::vector<uint8_t> buf(fi.parmsSize, 0);
        if (Invoke(c.pc, fi, buf) == 0 && ReturnedTrue(fi, buf)) {
            haveMouse = ReadFloatParam(buf, fi, "LocationX", mx)
                     && ReadFloatParam(buf, fi, "LocationY", my);
        }
    }
    // A captured / hidden cursor (controller play or mouse-look games — e.g. TQ2)
    // reports success with position (0,0); treat that as "no cursor" so the
    // screen-center fallback kicks in instead of tracing from the screen corner.
    if (haveMouse && std::fabs(mx) < 1.0 && std::fabs(my) < 1.0) {
        haveMouse = false;
        LOG_INFO("Teleport: cursor — GetMousePosition returned (0,0) (cursor "
                 "captured/hidden) — falling back to screen center");
    }
    bool usedCenter = false;
    if (!haveMouse) {
        if (!fallbackToCenter) return TP_ERR_NO_CURSOR;
        if (FindFunc(pcClass, "GetViewportSize", fi) && fi.parmsSize > 0) {
            std::vector<uint8_t> buf(fi.parmsSize, 0);
            int32_t sx = 0, sy = 0;
            if (Invoke(c.pc, fi, buf) == 0
                && ReadIntParam(buf, fi, "SizeX", sx)
                && ReadIntParam(buf, fi, "SizeY", sy)
                && sx > 0 && sy > 0) {
                mx = sx / 2.0;
                my = sy / 2.0;
                usedCenter = true;
            }
        }
        if (!usedCenter) return TP_ERR_NO_CURSOR;
    }
    if (outUsedCenter) *outUsedCenter = usedCenter;
    LOG_INFO("Teleport: cursor mouse=(%.0f, %.0f) haveMouse=%d usedCenter=%d channel=%d",
             mx, my, haveMouse ? 1 : 0, usedCenter ? 1 : 0, traceChannel);

    // 2. Hit test. Two trace methods, each scanned across trace channels (the
    //    requested one first, then 0..9) — click-to-move ARPGs (TQ2) put the
    //    ground on a CUSTOM ETraceTypeQuery, not Visibility(0), so we auto-find it.
    //    Deproject + KismetSystemLibrary line trace is the last resort.
    //    Channel order: loop index 0 = requested channel; index 1..10 = channels
    //    0..9 (skipping the requested one).
    double impact[3] = {};
    bool haveHit = false;
    int32_t hitChan = -1;

    // Path 1: GetHitResultUnderCursorByChannel — uses the game's OWN internal
    // cursor position (works even when our GetMousePosition read (0,0)). Cursor
    // games only; skipped for the screen-center fallback.
    if (!usedCenter && FindFunc(pcClass, "GetHitResultUnderCursorByChannel", fi)
        && fi.parmsSize > 0) {
        const FunctionParam* hr = FindParam(fi, "HitResult");
        for (int32_t i = 0; !haveHit && i <= 10; ++i) {
            int32_t chan = (i == 0) ? traceChannel : (i - 1);
            if (i > 0 && chan == traceChannel) continue;
            std::vector<uint8_t> buf(fi.parmsSize, 0);
            WriteByteParam(buf, fi, "TraceChannel", static_cast<uint8_t>(chan));
            WriteBoolParam(buf, fi, "bTraceComplex", true);
            if (hr && Invoke(c.pc, fi, buf) == 0 && ReturnedTrue(fi, buf)
                && ExtractHitPoint(buf, *hr, impact)) {
                haveHit = true; hitChan = chan;
            }
        }
        LOG_INFO("Teleport: cursor GetHitResultUnderCursorByChannel hit=%d hitChannel=%d",
                 haveHit ? 1 : 0, hitChan);
    }

    // Path 2: GetHitResultAtScreenPosition — traces from the SCREEN POSITION
    // (a real cursor OR the screen center), needs NO cursor and NO
    // KismetSystemLibrary (which TQ2 doesn't expose).
    if (!haveHit && FindFunc(pcClass, "GetHitResultAtScreenPosition", fi)
        && fi.parmsSize > 0) {
        const FunctionParam* sp = FindParam(fi, "ScreenPosition");  // FVector2D
        const FunctionParam* hr = FindParam(fi, "HitResult");
        for (int32_t i = 0; !haveHit && i <= 10; ++i) {
            int32_t chan = (i == 0) ? traceChannel : (i - 1);
            if (i > 0 && chan == traceChannel) continue;
            std::vector<uint8_t> buf(fi.parmsSize, 0);
            if (sp && sp->offset >= 0) {
                if (sp->size >= 16) {
                    double v[2] = { mx, my };
                    std::memcpy(buf.data() + sp->offset, v, sizeof(v));
                } else {
                    float v[2] = { static_cast<float>(mx), static_cast<float>(my) };
                    std::memcpy(buf.data() + sp->offset, v, sizeof(v));
                }
            }
            WriteByteParam(buf, fi, "TraceChannel", static_cast<uint8_t>(chan));
            WriteBoolParam(buf, fi, "bTraceComplex", true);
            if (hr && Invoke(c.pc, fi, buf) == 0 && ReturnedTrue(fi, buf)
                && ExtractHitPoint(buf, *hr, impact)) {
                haveHit = true; hitChan = chan;
            }
        }
        LOG_INFO("Teleport: cursor GetHitResultAtScreenPosition screen=(%.0f, %.0f) "
                 "hit=%d hitChannel=%d", mx, my, haveHit ? 1 : 0, hitChan);
    }
    if (!haveHit) {
        if (!FindFunc(pcClass, "DeprojectScreenPositionToWorld", fi)
            || fi.parmsSize <= 0) {
            LOG_WARN("Teleport: cursor — DeprojectScreenPositionToWorld not found on PC "
                     "class '%s' (cooked out?) — can't trace", Ubel::GetName(pcClass).c_str());
            return TP_ERR_REFLECTION;
        }
        std::vector<uint8_t> buf(fi.parmsSize, 0);
        WriteFloatParam(buf, fi, "ScreenX", mx);
        WriteFloatParam(buf, fi, "ScreenY", my);
        double loc[3] = {}, dir[3] = {};
        if (Invoke(c.pc, fi, buf) != 0 || !ReturnedTrue(fi, buf)
            || !ReadVecParam(buf, fi, "WorldLocation", loc)
            || !ReadVecParam(buf, fi, "WorldDirection", dir)) {
            LOG_WARN("Teleport: cursor — DeprojectScreenPositionToWorld invoke failed/empty");
            return TP_ERR_INVOKE;
        }
        LOG_INFO("Teleport: cursor deproject loc=(%.1f, %.1f, %.1f) dir=(%.3f, %.3f, %.3f)",
                 loc[0], loc[1], loc[2], dir[0], dir[1], dir[2]);

        double end[3] = {
            loc[0] + dir[0] * Grimoire::TELEPORT_TRACE_DIST,
            loc[1] + dir[1] * Grimoire::TELEPORT_TRACE_DIST,
            loc[2] + dir[2] * Grimoire::TELEPORT_TRACE_DIST,
        };

        // UKismetSystemLibrary::LineTraceSingle via the library CDO. Static-
        // native, but it reads the physics scene — game thread ONLY (the
        // Invoke helper guarantees that; never reuse Mimic's direct path).
        uintptr_t ksl = UE5_FindInstanceOfClass("KismetSystemLibrary");
        if (!ksl) {
            LOG_WARN("Teleport: cursor — KismetSystemLibrary instance/CDO not found");
            return TP_ERR_REFLECTION;
        }
        FunctionInfo lt;
        if (!FindFunc(Ubel::GetClass(ksl), "LineTraceSingle", lt) || lt.parmsSize <= 0) {
            LOG_WARN("Teleport: cursor — LineTraceSingle not found on KismetSystemLibrary "
                     "(cooked out?) — no trace fallback");
            return TP_ERR_REFLECTION;
        }
        std::vector<uint8_t> buf2(lt.parmsSize, 0);
        WritePtrParam(buf2, lt, "WorldContextObject", c.pawn);
        WriteVecParam(buf2, lt, "Start", loc);
        WriteVecParam(buf2, lt, "End", end);
        WriteByteParam(buf2, lt, "TraceChannel", static_cast<uint8_t>(traceChannel));
        WriteBoolParam(buf2, lt, "bTraceComplex", true);
        WriteBoolParam(buf2, lt, "bIgnoreSelf", true);
        // ActorsToIgnore (TArray), DrawDebugType, colors, DrawTime stay zeroed.
        const FunctionParam* hr = FindParam(lt, "OutHit");
        if (!hr) {
            LOG_WARN("Teleport: cursor — LineTraceSingle has no OutHit param (layout?)");
            return TP_ERR_REFLECTION;
        }
        int32_t ltr = Invoke(ksl, lt, buf2);
        bool ltHit = (ltr == 0) && ReturnedTrue(lt, buf2);
        if (ltHit) haveHit = ExtractHitPoint(buf2, *hr, impact);
        LOG_INFO("Teleport: cursor LineTraceSingle r=%d returnedHit=%d haveHit=%d",
                 ltr, ltHit ? 1 : 0, haveHit ? 1 : 0);
    }
    if (!haveHit) {
        LOG_WARN("Teleport: cursor — no blocking hit (channel=%d). Try a different "
                 "trace channel.", traceChannel);
        return TP_ERR_NO_HIT;
    }

    if (outHit) {
        outHit->X = impact[0];
        outHit->Y = impact[1];
        outHit->Z = impact[2];
    }
    double dest[3] = { impact[0], impact[1], impact[2] + zOffset };
    SaveLastImpl();   // remember where we were, in case this cursor jump goes wrong
    rc = TeleportPawnTo(c, dest, nullptr, /*preferTeleportTo=*/true, tierOut);
    if (rc != TP_OK) return rc;
    StopMovement(c);
    LOG_INFO("Teleport: cursor -> (%.1f, %.1f, %.1f) center=%d",
             dest[0], dest[1], dest[2], usedCenter ? 1 : 0);
    return TP_OK;
}

int32_t GetMarker(int32_t slot, Marker& out) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    if (slot < 0 || slot >= Grimoire::TELEPORT_SLOTS) return TP_ERR_EMPTY_MARKER;
    out = s_markers[slot];
    return out.Valid ? TP_OK : TP_ERR_EMPTY_MARKER;
}

int32_t ClearMarker(int32_t slot) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    if (slot < 0 || slot >= Grimoire::TELEPORT_SLOTS) return TP_ERR_EMPTY_MARKER;
    s_markers[slot] = Marker{};
    return TP_OK;
}

int32_t RecallLast(uint8_t* tierOut) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    if (!s_lastMarker.Valid) return TP_ERR_EMPTY_MARKER;
    Marker m = s_lastMarker;
    // One-way restore: deliberately does NOT SaveLastImpl() here, so the last
    // slot stays pinned to the pre-teleport pose and repeated recalls always
    // return to the same spot (recovery, not a toggle).
    int32_t rc = RecallTo(m.P, /*restoreRot=*/true, tierOut);
    LOG_INFO("Teleport: recall-last (%.1f, %.1f, %.1f) -> rc=%d",
             m.P.X, m.P.Y, m.P.Z, rc);
    return rc;
}

int32_t GetLast(Marker& out) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    out = s_lastMarker;
    return out.Valid ? TP_OK : TP_ERR_EMPTY_MARKER;
}

int32_t BugItSave(Pose& out, char* mapName, int32_t mapNameCap, uint8_t* outSource) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    Marker m{};
    int32_t rc = GetPoseImpl(m.P, m.MapName, sizeof(m.MapName), outSource, nullptr,
                             &m.ParentRelative);   // [W2-MARKER-PARENTREL]
    if (rc != TP_OK) return rc;
    m.Valid = true;
    s_bugItMarker = m;
    out = m.P;
    if (mapName && mapNameCap > 0) {
        int32_t n = 0;
        for (; n < mapNameCap - 1 && m.MapName[n]; ++n) mapName[n] = m.MapName[n];
        mapName[n] = '\0';
    }
    LOG_INFO("Teleport: BugIt stored (%.1f, %.1f, %.1f) map='%s'",
             m.P.X, m.P.Y, m.P.Z, m.MapName);
    return TP_OK;
}

int32_t BugItGo(uint8_t* tierOut) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    if (!s_bugItMarker.Valid) return TP_ERR_EMPTY_MARKER;   // no BugIt yet -> no-op
    Marker m = s_bugItMarker;
    SaveLastImpl();   // BugItGo is a jump -> record the undo point first
    int32_t rc = RecallTo(m.P, /*restoreRot=*/true, tierOut);
    LOG_INFO("Teleport: BugItGo (%.1f, %.1f, %.1f) -> rc=%d",
             m.P.X, m.P.Y, m.P.Z, rc);
    return rc;
}

int32_t TeleportRelative(double distance, bool horizontalOnly, Pose& outNewPose,
                         uint8_t* tierOut, bool* outLandingKnown) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    Chain c;
    int32_t rc = ResolveChain(c);
    if (rc != TP_OK) return rc;

    // Current world position (GetPoseImpl handles attached pawns via
    // K2_GetActorLocation; RelativeLocation == world otherwise).
    Pose cur{};
    rc = GetPoseImpl(cur, nullptr, 0, nullptr);
    if (rc != TP_OK) return rc;

    double fwd[3] = {};
    if (!GetForward(c, horizontalOnly, fwd)) {
        LOG_WARN("Teleport: relative move — could not resolve a facing direction");
        return TP_ERR_REFLECTION;
    }
    double dest[3] = { cur.X + fwd[0] * distance,
                       cur.Y + fwd[1] * distance,
                       cur.Z + fwd[2] * distance };
    SaveLastImpl();   // remember the pre-jump pose so RecallLast can undo it
    rc = TeleportPawnTo(c, dest, nullptr, /*preferTeleportTo=*/false, tierOut);
    if (rc != TP_OK) return rc;
    StopMovement(c);
    // ⛔ THE RE-READ CAN FAIL, AND SILENCE MADE THAT LOOK LIKE ARRIVING AT THE ORIGIN.
    // GetPoseImpl leaves `out` untouched on every failure path, and all three transports
    // zero-initialise the Pose and publish it whenever code == 0 -- so a failed re-read
    // was emitted as a landing at exactly (0,0,0,0,0,0), indistinguishable from really
    // standing at the world origin, and the panel overwrote its live X/Y/Z with those
    // zeros for the user to copy or save. The move itself SUCCEEDED, so this is not an
    // error code -- the caller is told the landing is unknown and publishes nothing
    // rather than a number nobody measured. [TPREL-ZEROPOSE-2026-09-10]
    if (outLandingKnown)
        *outLandingKnown = (GetPoseImpl(outNewPose, nullptr, 0, nullptr) == TP_OK);
    else
        GetPoseImpl(outNewPose, nullptr, 0, nullptr);   // best-effort re-read
    LOG_INFO("Teleport: relative %.1f uu (%s) fwd=(%.3f, %.3f, %.3f) -> (%.1f, %.1f, %.1f)",
             distance, horizontalOnly ? "horizontal" : "3D",
             fwd[0], fwd[1], fwd[2], dest[0], dest[1], dest[2]);
    return TP_OK;
}

int32_t SetMouseCursor(bool show, bool* outState) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    uintptr_t pc = 0, byteAddr = 0;
    uint8_t mask = 0;
    int32_t rc = ResolveCursorBit(pc, byteAddr, mask);
    if (rc != TP_OK) return rc;
    uint8_t b = 0;
    if (!Macht::ReadSafe(byteAddr, b)) return TP_ERR_REFLECTION;
    if (show) b |= mask;
    else      b = static_cast<uint8_t>(b & ~mask);
    if (!Macht::WriteBytes(byteAddr, &b, 1)) return TP_ERR_WRITE_FAILED;

    // The bShowMouseCursor flag alone often isn't enough — a GameOnly input mode
    // recaptures/hides the OS cursor (observed on TQ2 / DQIII HD-2D). Also drive
    // the input mode so the cursor actually shows/hides.
    ApplyCursorInputMode(pc, show);

    // Re-read the bit so the reported state reflects reality (a game that re-sets
    // it every tick may already have reverted our write between the lines above).
    uint8_t after = 0;
    bool stuck = Macht::ReadSafe(byteAddr, after) ? ((after & mask) != 0) : show;
    if (outState) *outState = stuck;
    LOG_INFO("Teleport: bShowMouseCursor forced %s (addr=0x%llX mask=0x%02X) — bit now %d",
             show ? "ON" : "OFF", (unsigned long long)byteAddr, mask, stuck ? 1 : 0);
    return TP_OK;
}

int32_t GetMouseCursor(bool* outState) {
    std::lock_guard<std::mutex> lock(s_opMutex);
    uintptr_t pc = 0, byteAddr = 0;
    uint8_t mask = 0;
    int32_t rc = ResolveCursorBit(pc, byteAddr, mask);
    if (rc != TP_OK) return rc;
    uint8_t b = 0;
    if (!Macht::ReadSafe(byteAddr, b)) return TP_ERR_REFLECTION;
    if (outState) *outState = (b & mask) != 0;
    return TP_OK;
}

bool GetCurrentMapName(char* buf, int32_t cap) {
    if (!buf || cap <= 0) return false;
    buf[0] = '\0';
    uintptr_t world = DerefWorld();
    if (!world) return false;
    CopyMapName(world, buf, cap);
    return buf[0] != '\0';
}

} // namespace Wirbel
