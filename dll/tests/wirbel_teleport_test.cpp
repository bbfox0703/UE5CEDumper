// wirbel_teleport_test.cpp
// UE5CEDumper — Teleport (Wirbel.cpp) over a fake world, with a fake engine answering every game-thread invoke.
//
// WHAT IT DRIVES
//   Every Wirbel export -- the pose, movement and trainer reads, the camera POV, the marker / last / BugIt slots, the
//   three teleports and the mouse cursor -- and, through the #include, the helpers they share: the GWorld chain and its
//   instance-scan fallback, the debug-camera hop, parameter packing at both FVector widths, the CMC freeze and the
//   deep force.
//
// WHAT IS FAKED
//   * The world: GWorld -> OwningGameInstance -> LocalPlayers[0] -> PlayerController -> Pawn -> RootComponent, the
//     camera manager and its cached POV, the character movement, KismetSystemLibrary and WidgetBlueprintLibrary each
//     behind a decoy "...Ex" CDO, a UE4 (float) twin of the controller, pawn, root, movement and camera, and an object
//     array whose order puts a decoy in front of every lookup a substring or a first-match rule would answer wrongly.
//     Reflection data built in this process and walked by the real Aura, Ubel and Serie.
//   * The engine: UE5_CallProcessEventEx records each call -- its parameters as they arrived, its thread, whether the
//     op lock was held, its place in the run -- and answers what the test scripted. Only a call answered 0 writes a
//     result, and a move writes the root's RelativeLocation (or the attached pawn's world position) as a game would.
//   * Memory that cannot be read (reserved pages) or written (the read-only view of a section), so Macht's own SEH and
//     VirtualProtect paths fail for real.
//   * Stark's liveness gate, which records the threshold each call asked for, and Sein, which records every line
//     under the stamp the calls carry, so a check can order the two.
//
// WHY IT #includes THE .cpp
//   The marker slots, the "last" (undo) slot, the BugIt slot and the op lock are file statics of Wirbel.cpp, and "last"
//   and BugIt have no public reset: a linked copy would carry one scenario's slots into the next, and a lock check
//   could not ask the lock itself. DeepForceWorldPos and the Chain it takes are file-local too. The core it walks is
//   ue5_testcore, linked. Stark, Frieren and MinHook are not: the one invoke entry point is faked here, and
//   UE5_FindInstanceOfClass is left undefined, so a Wirbel call to the user-facing lookup would fail the link.
//
// WHY AN EXECUTABLE OF ITS OWN
//   mimic_mailbox_test defines a fake namespace Wirbel.

#include "../src/Wirbel.cpp"  // NOLINT -- see "WHY IT #includes THE .cpp" above

// Wirbel.cpp #defines LOG_CAT before Sein.h and never undefines it, so this is the category its own lines carry. Ubel
// and Solitar log under the same category, which is why a Teleport line is also told by its "Teleport" prefix.
static constexpr const char* kWalkCat = LOG_CAT;

#include "../src/Serie.h"   // the fixture's name pool
#include "../src/Tot.h"     // the per-command cancel flag the instance scan polls

#include <windows.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>   // getenv_s: the AddressSanitizer build's negative control; exit on a fixture overflow
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstring>
#include <cwchar>
#include <deque>
#include <functional>
#include <initializer_list>
#include <map>
#include <memory>
#include <mutex>
#include <set>
#include <stdexcept>
#include <string>
#include <thread>
#include <tuple>      // std::ignore
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

// FIX.04: the codes the pipe, the mailbox and the C# TeleportCodes publish, pinned where the enum lives.
static_assert(Wirbel::TP_ERR_PARENT_RELATIVE == -16 && Wirbel::TP_ERR_WRITE_FAILED == -10 &&
                  Wirbel::TP_ERR_NO_CURSOR == -9 && Wirbel::TP_ERR_NO_HIT == -8 &&
                  Wirbel::TP_ERR_MAP_MISMATCH == -7 && Wirbel::TP_ERR_EMPTY_MARKER == -6,
              "FIX.04 codes: the TeleportResult values every transport and the C# side agree on");

// ══ the one clock every record is stamped with ═══════════════════════════════════════════════════════════════════
// A call and a log line written on different threads are ordered by this stamp, never by where they landed in a vector.
static std::atomic<uint64_t> g_seq{0};
static uint64_t FxStamp() { return g_seq.fetch_add(1) + 1; }

// ══ Sein: every log line recorded ════════════════════════════════════════════════════════════════════════════════
struct FxLogLine {
    uint64_t seq = 0;
    char lvl = 0;          // 'I' 'W' 'E' 'D'
    std::string cat;       // the writer's LOG_CAT: Wirbel's own lines are kWalkCat and start with "Teleport"
    std::string text;
};
static std::mutex g_logMu;
static std::vector<FxLogLine> g_log;
// A test's hook into each line, called after the line is recorded and with neither recorder lock held.
// ⚠ It runs on the WRITER's thread, and every Teleport line is written with s_opMutex held: a hook that blocks holds
// the op lock, and one that calls a Wirbel export other than GetCurrentMapName deadlocks.
static std::mutex g_logHookMu;
static std::function<void(const FxLogLine&)> g_onLog;
constexpr size_t kLogLineBytes = 8192;

static void FxLogV(char lvl, const char* cat, const char* fmt, va_list ap) {
    char b[kLogLineBytes];
    vsnprintf(b, sizeof b, fmt, ap);
    FxLogLine line;
    line.seq = FxStamp();
    line.lvl = lvl;
    line.cat = cat ? cat : "";
    line.text = b;
    {
        std::lock_guard<std::mutex> lk(g_logMu);
        g_log.push_back(line);
    }
    std::function<void(const FxLogLine&)> hook;
    {
        std::lock_guard<std::mutex> lk(g_logHookMu);
        hook = g_onLog;
    }
    if (hook) hook(line);
}
namespace Sein {
void Info(const char* cat, const char* fmt, ...) { va_list ap; va_start(ap, fmt); FxLogV('I', cat, fmt, ap); va_end(ap); }
void Error(const char* cat, const char* fmt, ...) { va_list ap; va_start(ap, fmt); FxLogV('E', cat, fmt, ap); va_end(ap); }
void Warn(const char* cat, const char* fmt, ...) { va_list ap; va_start(ap, fmt); FxLogV('W', cat, fmt, ap); va_end(ap); }
void Debug(const char* cat, const char* fmt, ...) { va_list ap; va_start(ap, fmt); FxLogV('D', cat, fmt, ap); va_end(ap); }
void Summary(const char*, ...) {}
}  // namespace Sein

// ══ Stark, Frieren's globals ═════════════════════════════════════════════════════════════════════════════════════
// The gate records the threshold of every call: a caller that passes its own -- one long enough to treat a paused game
// as ticking -- answers the same as one that takes Stark's default, so only the argument tells them apart.
static std::atomic<bool> g_responsive{true};   // what IsGameThreadResponsive answers
static std::mutex g_respMu;
static std::vector<int32_t> g_respArgs;
namespace Stark {
bool IsGameThreadResponsive(int32_t thresholdMs) {
    {
        std::lock_guard<std::mutex> lk(g_respMu);
        g_respArgs.push_back(thresholdMs);
    }
    return g_responsive.load();
}
void SetInvokeTimeoutMs(int32_t) {}
}  // namespace Stark

constexpr uint32_t kFixtureUEVersion = 504;    // Wirbel never reads it; Ubel keys the UFunction tail read on it
uint32_t g_cachedUEVersion = kFixtureUEVersion;
uintptr_t g_cachedGWorld = 0;
std::atomic<bool> g_initInProgress{false};

// ══ harness ══════════════════════════════════════════════════════════════════════════════════════════════════════
static std::atomic<int> g_pass{0}, g_fail{0};
static void check(const char* label, bool cond, const char* got = nullptr) {
    if (cond) { ++g_pass; }
    else { ++g_fail; printf("  FAIL  %s%s%s\n", label, got ? "   got: " : "", got ? got : ""); }
}
static void check(const char* label, bool cond, const std::string& got) { check(label, cond, got.c_str()); }
static void blk(const char* name) { printf("- %s\n", name); }

// Poll a condition for up to `ms`: a thread another one releases gets there in its own time, and a sleep on Windows is
// coarse.
constexpr DWORD kPollMs = 5;
static bool waitFor(const std::function<bool()>& cond, int ms) {
    const auto end = std::chrono::steady_clock::now() + std::chrono::milliseconds(ms);
    while (std::chrono::steady_clock::now() < end) {
        if (cond()) return true;
        Sleep(kPollMs);
    }
    return cond();
}

// The AddressSanitizer build only: the banner and negative control build_dll.py --asan --run needs (a deliberate heap
// overflow that must be reported). [CPP-HARDENING-2026-11-14]
static void FxAsanPreamble() {
#ifdef __SANITIZE_ADDRESS__
    printf("asan: on\n");
    size_t negLen = 0;
    if (getenv_s(&negLen, nullptr, 0, "UE5_ASAN_NEGATIVE_CONTROL") == 0 && negLen > 0) {
        constexpr size_t kNegBytes = 16;
        char* volatile p = new char[kNegBytes];
        p[kNegBytes] = 1;   // heap-buffer-overflow: ASan ends the process here
        delete[] p;
    }
#endif
}

// A run that checked nothing proves nothing, so it fails too.
static int FxExitCode() {
    const int pass = g_pass.load(), fail = g_fail.load();
    printf("\n%d checks, %d failure(s)\n", pass + fail, fail);
    if (pass == 0) printf("  FAIL  no check passed: a run that checked nothing is not a pass\n");
    return (fail > 0 || pass == 0) ? 1 : 0;
}

// A fixture that cannot be built tests nothing: stop rather than run every block against half a world.
[[noreturn]] static void FxFixtureFail(const char* what) {
    printf("  FAIL  FIXTURE: %s\n", what);
    exit(2);
}
[[noreturn]] static void FxFixtureFull(const char* what) {
    printf("  FAIL  FIXTURE: %s is full -- raise its capacity\n", what);
    exit(2);
}

// ── formatting for `got` ──
static std::string FxS(long long v) { return std::to_string(v); }
static std::string FxHex(uintptr_t v) {
    char b[32];
    snprintf(b, sizeof b, "0x%llX", static_cast<unsigned long long>(v));
    return b;
}
static std::string FxFmt(const char* fmt, ...) {
    char b[kLogLineBytes];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(b, sizeof b, fmt, ap);
    va_end(ap);
    return b;
}
static std::string FxNameOf(uintptr_t obj) { return obj ? Ubel::GetName(obj) : std::string("(0)"); }
static long long FxMs(std::chrono::steady_clock::duration d) {
    return std::chrono::duration_cast<std::chrono::milliseconds>(d).count();
}

// ══ the fixture's numbers ════════════════════════════════════════════════════════════════════════════════════════
constexpr int32_t kVecD = 24, kVecF = 12;   // an FVector / FRotator under LWC (3 doubles) and before it (3 floats)
constexpr int32_t kPtrBytes = static_cast<int32_t>(sizeof(uintptr_t));

// Every coordinate is exact in a float as well as a double, so a UE4 read widens to the very double a check compares
// against, and a log line formats it the same way.
constexpr double kStart[3] = { 1000.5, -2000.5, 300.5 };          // Root_0.RelativeLocation: where the hero stands
constexpr double kRot[3] = { -10.5, 90.5, 0.0 };                  // PC_0.ControlRotation: Pitch, Yaw, Roll
constexpr Wirbel::Pose kStartPose{ 1000.5, -2000.5, 300.5, -10.5, 90.5, 0.0 };   // what a default GetPose answers
constexpr Wirbel::Pose kDest{ 1500.5, -2500.5, 350.5, 5.5, -30.5, 0.0 };         // a jump target with a facing
constexpr double kDestXyz[3] = { kDest.X, kDest.Y, kDest.Z };
constexpr double kDestPyr[3] = { kDest.Pitch, kDest.Yaw, kDest.Roll };
constexpr double kFullTurn = 360.0;
constexpr double kDestYawStored = 329.5;   // Yaw -30.5 as SetControlRotation keeps it, normalised to [0, 360)
constexpr double kAttachedWorld[3] = { 5000.5, 6000.5, 700.5 };   // the world position of a pawn riding its parent
constexpr double kEnemyPos[3] = { -700.5, 800.5, 90.5 };
constexpr double kNpcPos[3] = { 4000.5, 4100.5, 120.5 };
constexpr double kSpecPos[3] = { 11.5, 22.5, 33.5 };              // the debug camera's spectator pawn
constexpr double kPropPos[3] = { -5.5, -6.5, -7.5 };              // the prop a camera may follow instead of the pawn
constexpr double kOther[3] = { 2222.5, 3333.5, 444.5 };           // a third place, for slots and mid-op moves
constexpr double kDbgRot[3] = { 77.5, 77.5, 0.0 };                // the debug camera's own facing: never reported
constexpr Wirbel::Pose kPlanted{ -1234.5, 2345.5, -345.5, 1.5, 2.5, 0.0 };   // a pose no op produces, for planted slots
constexpr double kVel[3] = { 0.0, 300.0, 400.0 };
constexpr double kSpeed = 500.0;                                   // |kVel|
constexpr double kAcc[3] = { 1.5, 2.5, 3.5 };
// The CMC's float knobs. Named ...Value: FxOff names the fields themselves, and a block that opens FxOff sees both.
constexpr float kWalkSpeedValue = 600.0f, kGravityValue = 1.0f, kJumpZValue = 420.0f;
constexpr uint8_t kModeWalking = 1;                                // EMovementMode MOVE_Walking

// The camera: what its getters answer, and what its cached POV holds -- different, so a check tells the two paths apart.
constexpr double kCamLoc[3] = { 100.5, 200.5, 300.5 };
constexpr double kCamRot[3] = { -5.5, 45.5, 0.0 };
constexpr double kFovGetter = 87.5;
constexpr double kFovGetterDouble = 87.25;                         // a GetFOVAngle that returns a double
constexpr double kCamDbgLoc[3] = { 900.5, 901.5, 902.5 };          // the debug camera's own camera manager
constexpr double kCamDbgRot[3] = { -7.5, 47.5, 0.0 };
constexpr double kRawLoc[3] = { 110.5, 210.5, 310.5 };
constexpr double kRawRot[3] = { -6.5, 46.5, 0.0 };
constexpr double kRawFov = 75.5;
constexpr double kRawFovDouble = 90.5;                             // CamFovDouble's FOV: its low 4 bytes read as 0.0f

// The cursor: a mouse position, a viewport whose centre is not a whole pixel, and what the traces hit.
constexpr double kMouse[2] = { 640.25, 360.75 };
constexpr int32_t kViewport[2] = { 1921, 1081 };
constexpr double kCenter[2] = { 960.5, 540.5 };                    // kViewport / 2.0
constexpr double kHit[3] = { 3000.5, 3100.5, 50.5 };               // a hit's ImpactPoint
constexpr double kLocationLag = 10.0;                              // a hit's Location trails ImpactPoint by this in X
constexpr double kZOffset = 90.5;
constexpr double kDeprojLoc[3] = { 10.5, 20.5, 30.5 };
constexpr double kDeprojDir[3] = { 0.0, 0.0, -1.0 };
// Unit length, every component non-zero and distinct: an End built from the wrong component of dir shows.
constexpr double kDeprojDirOblique[3] = { 0.48, 0.6, -0.64 };
constexpr double kFwd[3] = { 0.6, 0.8, 0.5 };                      // GetActorForwardVector's answer: not unit

// Sentinels a check expects to find untouched.
constexpr double kPoseSentinel = 7777.5;
constexpr uint8_t kByteSentinel = 0xAB;
constexpr uint8_t kTierSentinel = 0xEE;
constexpr char kMapFill = '#';
constexpr uint8_t kGuardByte = 0xA5;            // planted after a float vector: a 24-byte write over 12 floats shows
constexpr uint8_t kCursorSiblingBit = 0x01;     // the other bit in bShowMouseCursor's byte: only the mask may change
constexpr uint8_t kCursorBit = 0x02;            // bShowMouseCursor's FieldMask

constexpr int kSlots = Grimoire::TELEPORT_SLOTS;
constexpr int kMapCap = Grimoire::TELEPORT_MAPNAME_CAP;
constexpr int kLongNameChars = 140;             // past TELEPORT_MAPNAME_CAP, so every copy of it truncates
constexpr const char* kName20 = "Lvl_Persistent_Main_";   // a 20-character map name, as real ones run
constexpr int kHoldMs = 200;                    // how long a thread must stay blocked to count as blocked
constexpr int kJoinWaitMs = 5000;

// Where each reflected field lives. Every offset is the reflection the fixture builds, so a check that names one names
// what Wirbel was told, not a guess about an engine.
namespace FxOff {
constexpr int kRootComponent = 0x28;       // Actor
constexpr int kOwningGameInstance = 0x30;  // World
constexpr int kSubsystems = 0x30;          // WorldFuzzy: an ArrayProperty whose name contains "GameInstance"
constexpr int kMyGameInstance = 0x40;      // WorldFuzzy: the ObjectProperty the fuzzy match must take
constexpr int kLocalPlayers = 0x38;        // GameInstance
constexpr int kPlayerController = 0x30;    // LocalPlayer
constexpr int kPawn = 0x30;                // Controller (BP_PcAckOnly_C: PawnClassOverride)
constexpr int kControlRotation = 0x38;     // Controller
constexpr int kCtrlRotGuard = 0x44;        // UE4: the 12 bytes after a float ControlRotation
constexpr int kAckPawn = 0x50;             // PlayerController
constexpr int kCameraManager = 0x58;       // PlayerController
constexpr int kPlayer = 0x60;              // PlayerController
constexpr int kShowCursor = 0x68;          // PlayerController.bShowMouseCursor's byte
constexpr int kOriginalController = 0x70;  // DebugCameraController (DccFuzzy: OriginalControllerPtr)
constexpr int kPawnController = 0x30;      // Pawn.Controller
constexpr int kCharacterMovement = 0x38;   // Character
constexpr int kMesh = 0x40;                // Character
constexpr int kPropMesh = 0x30;            // BP_Prop_C
constexpr int kAttachParent = 0x30;        // SceneComponent
constexpr int kRelativeLocation = 0x40;    // SceneComponent
constexpr int kRelLocGuard = 0x4C;         // UE4: the 12 bytes after a float RelativeLocation (RelativeRotation's)
constexpr int kGuardBytes = 12;
constexpr int kComponentLocationCache = 0x40;   // RootNoRelLoc: a vector that is not RelativeLocation
constexpr int kC2W = 0x100;                // Root_0: an unreflected ComponentToWorld translation, doubles
constexpr int kC2WF = 0x104;               // RootF_0: the same in floats, 4-aligned but not 8
constexpr int kVelocity = 0x30;            // MovementComponent
constexpr int kAcceleration = 0x48;        // CharacterMovementComponent
constexpr int kMaxWalkSpeed = 0x60, kGravityScale = 0x64, kJumpZVelocity = 0x68, kCustomMode = 0x6C,
              kMovementMode = 0x6D;
// CmcFuzzy: each name a fuzzy arm must reach, beside a decoy of the wrong type or with the excluded word.
constexpr int kWalkSpeedCurve = 0x30, kBaseWalkSpeed = 0x38, kFuzzyGravity = 0x3C, kFuzzyJump = 0x40,
              kFuzzyCustomMode = 0x44, kGroundMovementMode = 0x45, kVelocityCurve = 0x48, kLastVelocity = 0x50;
constexpr int kPendingViewTarget = 0x30;   // PlayerCameraManager: its exact name is not "ViewTarget"
constexpr int kViewTarget = 0x40;
constexpr int kCacheHistory = 0xF0;        // CamFuzzy: an ArrayProperty whose name contains "CameraCache"
constexpr int kCameraCache = 0x100;        // PlayerCameraManager.CameraCachePrivate
constexpr int kPov = 0x10;                 // CameraCacheEntry.POV
constexpr int kPovBase = kCameraCache + kPov;   // the cached POV within a camera manager
constexpr int kPovRot = 0x18, kPovFov = 0x30;   // MinimalViewInfo
constexpr int kPovRotF = 0x0C, kPovFovF = 0x18; // MinimalViewInfoF
constexpr int kWidgetPlayer = 0x60;        // WBP_PlayerControllerHUD_C
}  // namespace FxOff
constexpr int32_t kUObjectHeaderBytes = 0x28;   // a UObject's own header: vtable, flags, index, class, name, outer
constexpr int32_t kPcBytes = FxOff::kShowCursor + 8;           // every controller class's PropertiesSize
constexpr int32_t kDccBytes = FxOff::kOriginalController + 8;
constexpr int32_t kSceneBytes = 0x58;                          // USceneComponent's slice the fixture reflects
constexpr int32_t kCmcBytes = 0x70;
constexpr int32_t kViewTargetBytes = 0x10;                     // FTViewTarget
constexpr int32_t kMviBytes = 0x40, kMviBytesF = 0x20;         // FMinimalViewInfo, double / float
constexpr int32_t kCacheBytes = FxOff::kPov + kMviBytes, kCacheBytesF = FxOff::kPov + kMviBytesF;
constexpr int32_t kCamBytes = FxOff::kCameraCache + kCacheBytes;

// ── the parameter blocks the fake engine and the checks read, per shape ──
// K2_SetActorLocation / K2_SetWorldLocation / K2_SetRelativeLocation: NewLocation, bSweep, SweepHitResult, bTeleport,
// and (on K2_SetActorLocation) the bool ReturnValue.
struct FxSetLocLayout { int32_t loc, sweep, sweepHit, hitBytes, teleport, ret, parmsSize, vec; };
constexpr FxSetLocLayout kSal5{ 0x00, 0x18, 0x20, 0xE8, 0x108, 0x109, 0x110, kVecD };
constexpr FxSetLocLayout kSal4{ 0x00, 0x0C, 0x10, 0x88, 0x98, 0x99, 0x9C, kVecF };
struct FxTeleportToLayout { int32_t loc, rot, ret, parmsSize, vec; };
constexpr FxTeleportToLayout kTp5{ 0x00, 0x18, 0x30, 0x38, kVecD };
constexpr FxTeleportToLayout kTp4{ 0x00, 0x0C, 0x18, 0x1C, kVecF };
// GetHitResultUnderCursorByChannel (screen -1) and GetHitResultAtScreenPosition. "Short" carries a HitResult of 0x20
// bytes, so ImpactPoint at +0x20 lies past the buffer while Location does not.
struct FxHitCallLayout { int32_t screen, screenBytes, channel, complex, hit, hitBytes, ret, parmsSize; };
constexpr FxHitCallLayout kHc5{ -1, 0, 0x00, 0x01, 0x08, 0xE8, 0xF0, 0xF8 };
constexpr FxHitCallLayout kHc4{ -1, 0, 0x00, 0x01, 0x04, 0x88, 0x8C, 0x90 };
constexpr FxHitCallLayout kHcShort{ -1, 0, 0x00, 0x01, 0x08, 0x20, 0x28, 0x30 };
constexpr FxHitCallLayout kHs5{ 0x00, 16, 0x10, 0x11, 0x18, 0xE8, 0x100, 0x108 };
constexpr FxHitCallLayout kHs4{ 0x00, 8, 0x08, 0x09, 0x0C, 0x88, 0x94, 0x98 };
constexpr FxHitCallLayout kHsShort{ 0x00, 16, 0x10, 0x11, 0x18, 0x20, 0x38, 0x40 };
// DeprojectScreenPositionToWorld: float screen coordinates (UE5 and UE4), and the double twin.
struct FxDeprojectLayout { int32_t sx, sy, screenBytes, worldLoc, worldDir, ret, parmsSize, vec; };
constexpr FxDeprojectLayout kDp5{ 0x00, 0x04, 4, 0x08, 0x20, 0x38, 0x40, kVecD };
constexpr FxDeprojectLayout kDp5D{ 0x00, 0x08, 8, 0x10, 0x28, 0x40, 0x48, kVecD };
constexpr FxDeprojectLayout kDp4{ 0x00, 0x04, 4, 0x08, 0x14, 0x20, 0x24, kVecF };
// GetMousePosition: float LocationX/Y and the double twin.
struct FxMouseLayout { int32_t x, y, bytes, ret, parmsSize; };
constexpr FxMouseLayout kMp{ 0x00, 0x04, 4, 0x08, 0x0C };
constexpr FxMouseLayout kMpD{ 0x00, 0x08, 8, 0x10, 0x18 };
namespace FxVp {                       // GetViewportSize: SizeY ends exactly at the end of its 8-byte block
constexpr int32_t kSizeX = 0x00, kSizeY = 0x04, kParmsSize = 0x08;
}  // namespace FxVp
namespace FxIm {                       // SetInputMode_GameOnly / SetInputMode_GameAndUIEx
constexpr int32_t kPc = 0x00, kFlush = 0x08, kGameOnlyParms = 0x10;
constexpr int32_t kWidget = 0x08, kLockMode = 0x10, kHideCursor = 0x11, kFlushUi = 0x12, kGameAndUiParms = 0x18;
}  // namespace FxIm
namespace FxSmm {                      // SetMovementMode
constexpr int32_t kMode = 0x00, kCustom = 0x01, kParmsSize = 0x02;
constexpr uint8_t kModeNone = 0, kModeFalling = 3;   // the two modes the CMC freeze sets, in that order
}  // namespace FxSmm

// KismetSystemLibrary::LineTraceSingle's parameter block on UE5 (FVector = 3 doubles): the default variant.
namespace FxLts {
constexpr int32_t kWorldContext = 0x00, kStart = 0x08, kEnd = 0x20, kChannel = 0x38, kComplex = 0x39, kIgnore = 0x40,
                  kDrawDebug = 0x50, kOutHit = 0x58, kOutHitBytes = 0xE8, kIgnoreSelf = 0x140, kReturn = 0x141,
                  kParmsSize = 0x148;
constexpr uint8_t kNumParms = 10;
}  // namespace FxLts
// The same function on UE4 (FVector = 3 floats).
namespace FxLtsF {
constexpr int32_t kWorldContext = 0x00, kStart = 0x08, kEnd = 0x14, kChannel = 0x20, kComplex = 0x21, kIgnore = 0x28,
                  kDrawDebug = 0x38, kOutHit = 0x40, kOutHitBytes = 0x88, kIgnoreSelf = 0xC8, kReturn = 0xC9,
                  kParmsSize = 0xD0;
}  // namespace FxLtsF
// UE5's FHitResult (HitResult5) and UE4's (HitResult4), member offsets relative to the parameter.
namespace FxHit5 {
constexpr int32_t kFaceIndex = 0x00, kTime = 0x04, kLocation = 0x08, kImpact = 0x20, kHandle = 0x80,
                  kHandleBytes = 0x18, kComponent = 0x98, kBytes = 0xE8;
}  // namespace FxHit5
namespace FxHit4 {
constexpr int32_t kTime = 0x00, kDistance = 0x04, kLocation = 0x08, kImpact = 0x14, kActor = 0x60, kComponent = 0x68,
                  kBytes = 0x88;
}  // namespace FxHit4

// ══ the fixture: memory ══════════════════════════════════════════════════════════════════════════════════════════
constexpr size_t kObjBytes = 0x200;        // one UObject: clear of every UStruct and UFunction offset the walks read
constexpr size_t kBigBytes = 0x800;        // a root or a camera manager
constexpr int32_t kDeepWindow = 0x400;     // DeepForceWorldPos reads this much from a root, whatever its class's size
static_assert(kBigBytes > static_cast<size_t>(kDeepWindow) + 0x10 + kVecD,
              "a copy planted just past the deep-force window still lies inside the root's own blob");
constexpr size_t kFieldBytes = 0x100;      // one FField: clear of the FProperty subclass slot and the bool probes
constexpr size_t kFieldClassBytes = 0x20;  // one FFieldClass: only its name is read
constexpr int kMaxObjs = 512, kMaxBig = 24, kMaxFields = 1536, kMaxFieldClasses = 16;
alignas(16) static uint8_t g_objArena[kMaxObjs][kObjBytes];
alignas(16) static uint8_t g_bigArena[kMaxBig][kBigBytes];
alignas(16) static uint8_t g_fieldArena[kMaxFields][kFieldBytes];
alignas(16) static uint8_t g_fclassArena[kMaxFieldClasses][kFieldClassBytes];
static int g_objUsed = 0, g_bigUsed = 0, g_fieldUsed = 0, g_fclassUsed = 0;
static std::map<std::string, uintptr_t> g_fclassByName;
// Each world object's size, so a reset clears all of what a scenario may have written into it.
static std::unordered_map<uintptr_t, size_t> g_bodyBytes;

static uint8_t* FxBytes(uintptr_t a) { return reinterpret_cast<uint8_t*>(a); }
static uintptr_t FxAddr(const void* p) { return reinterpret_cast<uintptr_t>(p); }
static void FxPut64(uintptr_t at, int off, uint64_t v) { memcpy(FxBytes(at) + off, &v, sizeof v); }
static void FxPut32(uintptr_t at, int off, uint32_t v) { memcpy(FxBytes(at) + off, &v, sizeof v); }
static void FxPut16(uintptr_t at, int off, uint16_t v) { memcpy(FxBytes(at) + off, &v, sizeof v); }
static void FxSetPtr(uintptr_t obj, int off, uintptr_t v) { FxPut64(obj, off, v); }
static uintptr_t FxGetPtr(uintptr_t obj, int off) {
    uintptr_t v = 0;
    memcpy(&v, FxBytes(obj) + off, sizeof v);
    return v;
}
static void FxSetClass(uintptr_t obj, uintptr_t cls) { FxSetPtr(obj, Grimoire::OFF_UOBJECT_CLASS, cls); }
// A TArray header: { Data, Num, Max }.
constexpr int kTArrayNumAt = 0x08, kTArrayMaxAt = 0x0C;
constexpr int32_t kTArrayBytes = 0x10;
static void FxPutTArray(uintptr_t obj, int off, uintptr_t data, int32_t num, int32_t max) {
    FxPut64(obj, off, data);
    FxPut32(obj, off + kTArrayNumAt, static_cast<uint32_t>(num));
    FxPut32(obj, off + kTArrayMaxAt, static_cast<uint32_t>(max));
}
// A vector the test plants or reads itself, at a reflected width: 3 doubles, or 3 floats.
static void FxSetVecAt(uintptr_t at, int32_t width, const double v[3]) {
    if (width >= kVecD) {
        memcpy(FxBytes(at), v, kVecD);
        return;
    }
    const float f[3] = { static_cast<float>(v[0]), static_cast<float>(v[1]), static_cast<float>(v[2]) };
    memcpy(FxBytes(at), f, sizeof f);
}
static std::array<double, 3> FxVecAt(uintptr_t at, int32_t width) {
    std::array<double, 3> v{};
    if (width >= kVecD) {
        memcpy(v.data(), FxBytes(at), kVecD);
        return v;
    }
    float f[3] = {};
    memcpy(f, FxBytes(at), sizeof f);
    v = { f[0], f[1], f[2] };
    return v;
}
static void FxClearBody(uintptr_t obj) {
    const auto it = g_bodyBytes.find(obj);
    const size_t bytes = it != g_bodyBytes.end() ? it->second : kObjBytes;
    memset(FxBytes(obj) + kUObjectHeaderBytes, 0, bytes - static_cast<size_t>(kUObjectHeaderBytes));
}

// ══ the fixture: names ═══════════════════════════════════════════════════════════════════════════════════════════
// A UE4 TNameEntryArray: one chunk of entry pointers, each entry 0x100 bytes with its text at +0x10 -- room for a map
// name longer than TELEPORT_MAPNAME_CAP. Spare entries after the last used one give the reader's look-ahead window
// real memory.
constexpr int kMaxNames = 1024;
constexpr int kNameSpare = 8;
constexpr int kNameEntryBytes = 0x100;
constexpr int kNameTextAt = 0x10;
static uint8_t g_nameEntry[kMaxNames + kNameSpare][kNameEntryBytes];
static uintptr_t g_nameChunk[kMaxNames + 1];
static uintptr_t g_nameChunks[2];
static std::vector<std::string> g_nameText{ std::string() };   // [0] is FName None
// A wide entry's key in g_nameText: it can never equal a narrow name, so the two never share an index.
constexpr const char* kWideNameTag = "\x01wide:";
static_assert(kLongNameChars + 1 <= kNameEntryBytes - kNameTextAt, "the long map name fits one name entry");

static int32_t FxFName(const char* s) {
    for (size_t i = 1; i < g_nameText.size(); ++i)
        if (g_nameText[i] == s) return static_cast<int32_t>(i);
    const size_t i = g_nameText.size();
    const size_t len = strlen(s);
    if (i >= static_cast<size_t>(kMaxNames) || len + 1 > static_cast<size_t>(kNameEntryBytes - kNameTextAt))
        FxFixtureFull("the name table (or one entry's text)");
    memcpy(g_nameEntry[i] + kNameTextAt, s, len + 1);
    g_nameChunk[i] = FxAddr(g_nameEntry[i]);
    g_nameText.emplace_back(s);
    return static_cast<int32_t>(i);
}
// A wide (UTF-16) entry: its first word carries the wide bit and its own index, as a pre-4.23 FNameEntry does.
constexpr int32_t FxWideIndexWord(int32_t index) { return (index << 1) | 1; }
static_assert(Serie::UE4EntryIsWide(FxWideIndexWord(5), 5) && !Serie::UE4EntryIsWide(0, 5),
              "the fixture's wide entries are the ones Serie decodes as wide, and its narrow ones are not");
static int32_t FxFNameWide(const wchar_t* text, const char* key) {
    const std::string tag = std::string(kWideNameTag) + key;
    for (size_t i = 1; i < g_nameText.size(); ++i)
        if (g_nameText[i] == tag) return static_cast<int32_t>(i);
    const size_t i = g_nameText.size();
    const size_t units = std::wcslen(text);
    if (i >= static_cast<size_t>(kMaxNames) ||
        (units + 1) * sizeof(wchar_t) > static_cast<size_t>(kNameEntryBytes - kNameTextAt))
        FxFixtureFull("the name table (or one wide entry's text)");
    const int32_t index = static_cast<int32_t>(i);
    const int32_t word = FxWideIndexWord(index);
    memcpy(g_nameEntry[i], &word, sizeof word);
    memcpy(g_nameEntry[i] + kNameTextAt, text, (units + 1) * sizeof(wchar_t));
    g_nameChunk[i] = FxAddr(g_nameEntry[i]);
    g_nameText.push_back(tag);
    return index;
}
// The camera-cache struct copy's name: 相機快取, a CJK first character the struct probe must accept.
constexpr wchar_t kCjkCacheName[] = L"相機快取";

// ══ the fixture: reflection builders ═════════════════════════════════════════════════════════════════════════════
constexpr const char* kObjectProperty = "ObjectProperty";
constexpr const char* kArrayProperty = "ArrayProperty";
constexpr const char* kBoolProperty = "BoolProperty";
constexpr const char* kByteProperty = "ByteProperty";
constexpr const char* kStructProperty = "StructProperty";
constexpr const char* kIntProperty = "IntProperty";
constexpr const char* kFloatProperty = "FloatProperty";
constexpr const char* kDoubleProperty = "DoubleProperty";
constexpr const char* kWeakObjectProperty = "WeakObjectProperty";
constexpr const char* kNameProperty = "NameProperty";

constexpr uint64_t kParm = 0x0080;               // CPF_Parm
constexpr uint64_t kOut = 0x0100;                // CPF_OutParm
constexpr uint64_t kRet = 0x0400;                // CPF_ReturnParm
constexpr uint64_t kPod = 0x0008001040000200;    // the plain-data flags a scalar parameter carries
constexpr uint64_t kInPod = kParm | kPod;        // a scalar passed in
constexpr uint64_t kOutPod = kParm | kOut | kPod;          // a scalar out-parameter
constexpr uint64_t kRetPod = kParm | kOut | kRet | kPod;   // a scalar return value
constexpr uint64_t kOutStruct = kParm | kOut;              // a struct out-parameter
constexpr uint64_t kRetStruct = kParm | kOut | kRet;       // a struct return value
constexpr uint8_t kNativeBoolByteMask = 0x01, kNativeBoolFieldMask = 0xFF;   // a native bool, not a bitfield

// The UFunction tail where the pinned FunctionFlags offset puts it: FunctionFlags, then NumParms +4, ParmsSize +6,
// ReturnValueOffset +8.
constexpr int kFnFlagsAt = 0xB0;
constexpr int kFnNumParmsAt = kFnFlagsAt + 4;
constexpr int kFnParmsSizeAt = kFnFlagsAt + 6;
constexpr int kFnRetOffAt = kFnFlagsAt + 8;
constexpr uint32_t kFnNative = 0x401;            // FUNC_Final | FUNC_Native
constexpr uint16_t kNoReturn = 0xFFFF;           // the ReturnValueOffset of a function that returns nothing
constexpr int kUClassInterfacesAt = 0xC0;        // UClass::Interfaces, measured: every class here reads none

// The metaclasses and packages every other object hangs off.
static uintptr_t g_metaClass, g_metaPackage, g_metaFunction, g_metaStruct, g_pkgEngine, g_pkgOther, g_pkgEngineU;

static uintptr_t FxNewObjBlob() {
    if (g_objUsed >= kMaxObjs) FxFixtureFull("the object arena");
    return FxAddr(g_objArena[g_objUsed++]);
}
static uintptr_t FxNewBigBlob() {
    if (g_bigUsed >= kMaxBig) FxFixtureFull("the big-object arena");
    return FxAddr(g_bigArena[g_bigUsed++]);
}
static uintptr_t FxObjAtIdx(uintptr_t at, uintptr_t cls, int32_t nameIdx, uintptr_t outer) {
    FxPut64(at, Grimoire::OFF_UOBJECT_CLASS, cls);
    FxPut32(at, Grimoire::OFF_UOBJECT_NAME, static_cast<uint32_t>(nameIdx));
    FxPut64(at, DynOff::UOBJECT_OUTER, outer);
    return at;
}
static uintptr_t FxObjAt(uintptr_t at, uintptr_t cls, const char* name, uintptr_t outer) {
    return FxObjAtIdx(at, cls, FxFName(name), outer);
}
static uintptr_t FxObj(uintptr_t cls, const char* name, uintptr_t outer) {
    const uintptr_t o = FxObjAt(FxNewObjBlob(), cls, name, outer);
    g_bodyBytes[o] = kObjBytes;
    return o;
}
static uintptr_t FxObjBig(uintptr_t cls, const char* name, uintptr_t outer) {
    const uintptr_t o = FxObjAt(FxNewBigBlob(), cls, name, outer);
    g_bodyBytes[o] = kBigBytes;
    return o;
}

// Every class the fixture builds, so a reset can put back any head a scenario swapped.
static std::vector<uintptr_t> g_allClasses;
// A native UClass: its class is the metaclass "Class", its outer a package.
static uintptr_t FxClass(const char* name, uintptr_t super, int32_t propsSize, uintptr_t outer = 0) {
    const uintptr_t c = FxObj(g_metaClass, name, outer ? outer : g_pkgEngine);
    FxPut64(c, DynOff::USTRUCT_SUPER, super);
    FxPut32(c, DynOff::USTRUCT_PROPSSIZE, static_cast<uint32_t>(propsSize));
    g_allClasses.push_back(c);
    return c;
}
static uintptr_t FxStructIdx(int32_t nameIdx, int32_t size, uintptr_t firstField) {
    const uintptr_t s = FxObjAtIdx(FxNewObjBlob(), g_metaStruct, nameIdx, g_pkgEngine);
    FxPut32(s, DynOff::USTRUCT_PROPSSIZE, static_cast<uint32_t>(size));
    FxPut64(s, DynOff::USTRUCT_CHILDPROPS, firstField);
    return s;
}
static uintptr_t FxStruct(const char* name, int32_t size, uintptr_t firstField) {
    return FxStructIdx(FxFName(name), size, firstField);
}
static uintptr_t FxFieldClass(const char* type) {
    const auto it = g_fclassByName.find(type);
    if (it != g_fclassByName.end()) return it->second;
    if (g_fclassUsed >= kMaxFieldClasses) FxFixtureFull("the field-class arena");
    const uintptr_t fc = FxAddr(g_fclassArena[g_fclassUsed++]);
    FxPut32(fc, DynOff::FFIELDCLASS_NAME, static_cast<uint32_t>(FxFName(type)));
    g_fclassByName.emplace(type, fc);
    return fc;
}
// One FProperty. A StructProperty's struct and a BoolProperty's layout share the subclass slot after the base; a
// BoolProperty built here is a native bool, which is what every parameter is.
static uintptr_t FxField(const char* type, const char* name, int32_t off, int32_t size, uint64_t flags,
                         uintptr_t next = 0, uintptr_t structPtr = 0) {
    if (g_fieldUsed >= kMaxFields) FxFixtureFull("the field arena");
    const uintptr_t f = FxAddr(g_fieldArena[g_fieldUsed++]);
    FxPut64(f, DynOff::FFIELD_CLASS, FxFieldClass(type));
    FxPut32(f, DynOff::FFIELD_NAME, static_cast<uint32_t>(FxFName(name)));
    FxPut64(f, DynOff::FFIELD_NEXT, next);
    FxPut32(f, DynOff::FPROPERTY_ELEMSIZE - 4, 1);   // ArrayDim, the int32 just before ElementSize
    FxPut32(f, DynOff::FPROPERTY_ELEMSIZE, static_cast<uint32_t>(size));
    FxPut64(f, DynOff::FPROPERTY_FLAGS, flags);
    FxPut32(f, DynOff::FPROPERTY_OFFSET, static_cast<uint32_t>(off));
    if (strcmp(type, kStructProperty) == 0) FxPut64(f, DynOff::FSTRUCTPROP_STRUCT, structPtr);
    if (strcmp(type, kBoolProperty) == 0) {
        const int at = DynOff::FBOOLPROP_FIELDSIZE;   // FieldSize, ByteOffset, ByteMask, FieldMask
        uint8_t* p = FxBytes(f);
        p[at] = 1;
        p[at + 1] = 0;
        p[at + 2] = kNativeBoolByteMask;
        p[at + 3] = kNativeBoolFieldMask;
    }
    return f;
}
// A packed bool (uint8 bFlag:1): [FieldSize, ByteOffset, ByteMask, FieldMask] at FBOOLPROP_FIELDSIZE + delta. The
// native layout FxField writes there is cleared first across the whole probe reach, so a probe that misses every
// planted layout finds nothing.
struct FxBoolLayout { int delta; uint8_t fieldSize, byteOffset, byteMask, fieldMask; };
constexpr int kBoolProbeReach = 8;   // Wirbel and Ubel read the layout this far either side of FBOOLPROP_FIELDSIZE
static uintptr_t FxBoolField(const char* name, int32_t off, const std::vector<FxBoolLayout>& layouts) {
    const uintptr_t f = FxField(kBoolProperty, name, off, 1, 0);
    const int base = DynOff::FBOOLPROP_FIELDSIZE;
    memset(FxBytes(f) + base - kBoolProbeReach, 0, 2 * kBoolProbeReach + 4);
    for (const auto& l : layouts) {
        uint8_t* p = FxBytes(f) + base + l.delta;
        p[0] = l.fieldSize;
        p[1] = l.byteOffset;
        p[2] = l.byteMask;
        p[3] = l.fieldMask;
    }
    return f;
}
// bShowMouseCursor as the engine declares it (uint32 bShowMouseCursor:1): a bitfield whose sibling bit is planted set.
static const std::vector<FxBoolLayout> kCursorPacked = { { 0, 1, 0, kCursorBit, kCursorBit } };

// A UFunction. The tail's fields are a uint8 and two uint16s; the arguments are ints so a call site needs no casts.
static std::unordered_map<uintptr_t, std::string> g_fnName;   // each blob's name, so a list can be searched by it
static uintptr_t FxFunc(const char* name, uintptr_t owner, uintptr_t firstParam, int32_t numParms, int32_t parmsSize,
                        int32_t retOff, uintptr_t next = 0) {
    const uintptr_t f = FxObj(g_metaFunction, name, owner);
    FxPut64(f, DynOff::USTRUCT_CHILDPROPS, firstParam);
    FxPut64(f, DynOff::UFIELD_NEXT, next);
    FxPut32(f, kFnFlagsAt, kFnNative);
    FxBytes(f)[kFnNumParmsAt] = static_cast<uint8_t>(numParms);
    FxPut16(f, kFnParmsSizeAt, static_cast<uint16_t>(parmsSize));
    FxPut16(f, kFnRetOffAt, static_cast<uint16_t>(retOff));
    g_fnName[f] = name;
    return f;
}
// Links FFields in the order given, skipping zeros. An element may already be a chain: the next one hangs off its tail.
static uintptr_t FxChainFields(std::initializer_list<uintptr_t> fields) {
    uintptr_t head = 0, tail = 0;
    for (uintptr_t f : fields) {
        if (!f) continue;
        if (tail) FxSetPtr(tail, DynOff::FFIELD_NEXT, f);
        else head = f;
        tail = f;
        for (uintptr_t n = FxGetPtr(tail, DynOff::FFIELD_NEXT); n; n = FxGetPtr(tail, DynOff::FFIELD_NEXT)) tail = n;
    }
    return head;
}
static uintptr_t FxObjF(const char* name, int32_t off) { return FxField(kObjectProperty, name, off, kPtrBytes, 0); }
static uintptr_t FxStructF(const char* name, int32_t off, int32_t size, uintptr_t s) {
    return FxField(kStructProperty, name, off, size, 0, 0, s);
}
static uintptr_t FxScalarF(const char* type, const char* name, int32_t off, int32_t size) {
    return FxField(type, name, off, size, 0);
}
static void FxSetProps(uintptr_t cls, uintptr_t head) { FxSetPtr(cls, DynOff::USTRUCT_CHILDPROPS, head); }
// A class's function list. Ubel memoizes a class walk by witness pointers, and Children is one: a swap is a fresh head,
// never a function edited in place.
static void FxSetFns(uintptr_t cls, uintptr_t head) { FxSetPtr(cls, DynOff::USTRUCT_CHILDREN, head); }

// A UE4-era UProperty (a UObject): class, name, ArrayDim, ElementSize, PropertyFlags, Offset and UField::Next at the
// UProperty offsets, as dll_core_test's UPROPINNER lays them out.
static uintptr_t FxUProp(uintptr_t propClass, const char* name, int32_t off, int32_t size, uint64_t flags,
                         uintptr_t next) {
    const uintptr_t p = FxObj(propClass, name, 0);
    FxPut32(p, DynOff::UPROPERTY_ELEMSIZE - 4, 1);
    FxPut32(p, DynOff::UPROPERTY_ELEMSIZE, static_cast<uint32_t>(size));
    FxPut64(p, DynOff::UPROPERTY_FLAGS, flags);
    FxPut32(p, DynOff::UPROPERTY_OFFSET, static_cast<uint32_t>(off));
    FxPut64(p, DynOff::UFIELD_NEXT, next);
    return p;
}
// The UProperty bool's layout sits 4 below UBOOLPROP_FIELDSIZE: inside the UProperty probe, outside the FProperty one,
// so a cursor read that took the FProperty base on a UProperty engine finds no layout.
constexpr int kUpropLayoutDelta = -4;

// ══ the fixture: its objects (built in FxBuildWorld) ═════════════════════════════════════════════════════════════
// Classes, UE5.
static uintptr_t g_cObject, g_cActor, g_cController, g_cPlayerController, g_cGameCtrl, g_cDcc, g_cBpDebugCam,
    g_cDccFuzzy, g_cBpDebugCamFuzzy, g_cDccHud, g_cPcAckOnly, g_cPcNoPawn, g_cPcNoRot, g_cPcNoCursor, g_cPcSelfSuper,
    g_cPcBadSuper, g_cPawn, g_cCharacter, g_cHero, g_cNpc, g_cEnemy, g_cSpectator, g_cProp, g_cActorComponent,
    g_cSceneComponent, g_cPrimitive, g_cCapsule, g_cSkeletalMesh, g_cRootNoRelLoc, g_cMoveComp, g_cCmc, g_cCmcNoVel,
    g_cCmcFuzzy, g_cPcm, g_cBpCam, g_cCamNoCache, g_cCamFuzzy, g_cCamNoViewTarget, g_cCamProbe8, g_cCamProbeClass,
    g_cCamCjk, g_cCamEmptyName, g_cCamCacheNotStruct, g_cCamNoRot, g_cCamFovDouble, g_cCamFovDoubleGetter,
    g_cCamLocScalar, g_cBpfl, g_cKsl, g_cKslEx, g_cWbl, g_cWblEx, g_cWorld, g_cWorldFuzzy, g_cWorldBare,
    g_cGameInstance, g_cGameInstanceBare, g_cPlayer, g_cLocalPlayer, g_cLocalPlayerBare, g_cNetConnection,
    g_cUserWidget, g_cWidgetHud;
// bShowMouseCursor layout variants, each its own PlayerController-shaped class.
enum class FxBoolVariant { At80, FieldSize4, ByteMask3, ByteOffset1, ByteMask8, Native, ByteOffset8, Count };
constexpr int kBoolVariants = static_cast<int>(FxBoolVariant::Count);
static uintptr_t g_cPcBool[kBoolVariants];
// Classes, the UE4 (float) twins.
static uintptr_t g_cActorF, g_cCharacterF, g_cHeroF, g_cControllerF, g_cPlayerControllerF, g_cGameCtrlF,
    g_cSceneCompF, g_cCapsuleF, g_cCmcF, g_cPcmF;
// The UProperty mini-world.
static uintptr_t g_cUpObjectProperty, g_cUpBoolProperty, g_cPcU, g_upPlayer, g_upCursor;
// ScriptStructs.
static uintptr_t g_sVector, g_sVectorF, g_sRotator, g_sRotatorF, g_sVector2D, g_sVector2DF, g_sHandle, g_sHit5,
    g_sHit5NoImpact, g_sHit5NoPoint, g_sHit4, g_sViewTarget, g_sMvi, g_sMviF, g_sMviNoRot, g_sMviFovD, g_sCache,
    g_sCacheF, g_sCacheNoRot, g_sCacheFovD, g_sCacheCjk, g_sEmptyName;
// The default function lists (each class's Children head) and the blobs in them.
static uintptr_t g_hdActor, g_hdCtrl, g_hdPc, g_hdScene, g_hdMoveComp, g_hdCmc, g_hdCam, g_hdKsl, g_hdWbl, g_hdActorF,
    g_hdCtrlF, g_hdPcF, g_hdSceneF, g_hdCmcF, g_hdCamF;
static uintptr_t g_fnGetLoc, g_fnForward, g_fnSetActorLoc, g_fnTeleportTo, g_fnSetCtrlRot, g_fnStopMove, g_fnMousePos,
    g_fnViewport, g_fnHitCursor, g_fnHitScreen, g_fnDeproject, g_fnSetWorldLoc, g_fnSetRelLoc, g_fnStopMoveNow,
    g_fnSetMoveMode, g_fnCamLoc, g_fnCamRot, g_fnFov, g_fnGameOnly, g_fnGameAndUiEx;
static uintptr_t g_fnGetLocF, g_fnForwardF, g_fnSetActorLocF, g_fnTeleportToF, g_fnSetCtrlRotF, g_fnMousePosF,
    g_fnHitCursorF, g_fnHitScreenF, g_fnDeprojectF, g_fnSetWorldLocF, g_fnSetRelLocF, g_fnSetMoveModeF,
    g_fnStopMoveNowF, g_fnCamLocF, g_fnCamRotF, g_fnFovF;
// KismetSystemLibrary's LineTraceSingle variants (each its own blob, all named LineTraceSingle), its siblings, the
// decoy library's own blob.
static uintptr_t g_ltsD, g_ltsF, g_ltsNoOutHit, g_ltsNoImpact, g_ltsNoPoint, g_fnLtsMulti, g_fnLtsForObjects,
    g_fnLtsByProfile, g_ltsDecoy;
// The object array's objects.
static uintptr_t g_cdoPc, g_cdoWblEx, g_cdoWbl, g_cdoKslEx, g_cdoKsl;
// The world graph.
static uintptr_t g_worldA, g_worldB, g_worldCase, g_worldFuzzy, g_worldLong, g_world20, g_worldEmpty, g_worldBare;
static uintptr_t g_gi0, g_giBare, g_lp0, g_lpBare, g_netConn0, g_widget0, g_subsystems;
static uintptr_t g_pc0, g_dbgPc0, g_dbgIdle, g_pcNoPlayer, g_pcRemote, g_dbgFuzzy0, g_decoyPc0, g_pcAckOnly0, g_pcU0;
static uintptr_t g_hero0, g_npc0, g_enemy0, g_spectator0, g_prop0;
static uintptr_t g_root0, g_rootNpc, g_rootEnemy, g_rootSpec, g_rootProp, g_parent0, g_mesh0;
static uintptr_t g_cmc0, g_cmcNoVel0, g_cmcFuzzy0;
static uintptr_t g_cam0, g_camDbg0;
static uintptr_t g_pcF0, g_heroF0, g_rootF0, g_cmcF0, g_camF0;
static uintptr_t g_gworldSlot = 0;   // &GWorld: g_cachedGWorld points here
static uintptr_t g_lpArr[1];         // GameInstance.LocalPlayers' data
// PC_0 lives 0x10 into a larger zeroed blob, so the 8 bytes a read at PC-1 takes are memory the test owns.
constexpr int kPcInHost = 0x10;
constexpr int kPcTrapLead = 1;       // the read a missing field's offset -1 would make starts this far before PC_0
alignas(16) static uint8_t g_pcHost[kPcInHost + kObjBytes];

// ══ memory that refuses ══════════════════════════════════════════════════════════════════════════════════════════
constexpr SIZE_T kPage = 0x1000;
// The write-fail section: a root copy in one page, a controller copy in the next. Reads through the read-only view
// work; Macht::WriteBytes cannot raise a FILE_MAP_READ view's protection, so every raw write there fails.
constexpr size_t kWfBytes = 2 * kPage;
constexpr size_t kWfRootAt = 0, kWfPcAt = kPage;
static HANDLE g_wfSection = nullptr;
static uintptr_t g_wfRw = 0, g_wfRo = 0;
static uintptr_t g_unreadablePage = 0;   // reserved, never committed
// Objects that end where a committed page meets a reserved one: whatever lies past the page end cannot be read.
enum FxEdge { kEdgeRoot, kEdgeRootNear, kEdgeCamRot, kEdgeCamLoc, kEdgeCount };
static void* g_edgeBase[kEdgeCount] = {};
static uintptr_t g_rootEdge, g_rootNearEdge, g_camEdgeRot, g_camEdgeLoc;
constexpr size_t kRootEdgeBytes = FxOff::kRelativeLocation;                 // RelativeLocation is the first byte lost
constexpr size_t kRootNearEdgeBytes = 0x200;                                // readable, but not a whole deep-force window
constexpr size_t kCamEdgeRotBytes = FxOff::kPovBase + FxOff::kPovRot;       // POV.Location readable, Rotation not
constexpr size_t kCamEdgeLocBytes = FxOff::kPovBase;                        // POV.Location already lost

// ══ the fake engine's vocabulary ═════════════════════════════════════════════════════════════════════════════════
enum class FxKind {
    Other, GetLoc, SetActorLoc, TeleportTo, SetWorldLoc, SetRelLoc, SetCtrlRot, SetMoveMode, StopMove, StopMoveNow,
    CamLoc, CamRot, Fov, Forward, MousePos, Viewport, HitCursor, HitScreen, Deproject, Trace, InputGameOnly,
    InputGameAndUI,
};
static const char* FxKindName(FxKind k) {
    switch (k) {
    case FxKind::GetLoc: return "GetLoc";
    case FxKind::SetActorLoc: return "SetActorLoc";
    case FxKind::TeleportTo: return "TeleportTo";
    case FxKind::SetWorldLoc: return "SetWorldLoc";
    case FxKind::SetRelLoc: return "SetRelLoc";
    case FxKind::SetCtrlRot: return "SetCtrlRot";
    case FxKind::SetMoveMode: return "SetMoveMode";
    case FxKind::StopMove: return "StopMove";
    case FxKind::StopMoveNow: return "StopMoveNow";
    case FxKind::CamLoc: return "CamLoc";
    case FxKind::CamRot: return "CamRot";
    case FxKind::Fov: return "Fov";
    case FxKind::Forward: return "Forward";
    case FxKind::MousePos: return "MousePos";
    case FxKind::Viewport: return "Viewport";
    case FxKind::HitCursor: return "HitCursor";
    case FxKind::HitScreen: return "HitScreen";
    case FxKind::Deproject: return "Deproject";
    case FxKind::Trace: return "Trace";
    case FxKind::InputGameOnly: return "InputGameOnly";
    case FxKind::InputGameAndUI: return "InputGameAndUI";
    case FxKind::Other: break;
    }
    return "Other";
}

// How a move answers: it moves the pawn (Moves), reports success and leaves it where it was (Lies -- a
// CharacterMovement that reverts the write), moves it short of the target along -X (Short), or answers false and
// leaves it (RetFalse).
enum class FxMove { Moves, Lies, Short, RetFalse };

// Where one FHitResult shape keeps ImpactPoint and Location, relative to the parameter; -1 = the shape has none.
struct FxHitLayout {
    int32_t impact = -1, location = -1;
    int32_t vec = kVecD;
};
// What the fake engine knows about one UFunction blob, registered when the blob is built.
struct FxFnSpec {
    FxKind kind = FxKind::Other;
    int32_t vec = kVecD;     // the width of the vector the call carries
    int32_t loc = -1;        // the location or rotation parameter, a getter's return, Deproject's WorldLocation, a
                             //   trace's Start, a screen trace's ScreenPosition
    int32_t rot = -1;        // K2_TeleportTo's DestRotation
    int32_t ret = -1;        // the bool ReturnValue; -1 = none
    int32_t scalar = 4;      // a float-or-double's width: GetFOVAngle's return, the mouse and screen coordinates
    int32_t x = -1, y = -1;  // LocationX/Y, SizeX/Y, ScreenX/Y
    int32_t dir = -1;        // Deproject's WorldDirection
    int32_t channel = -1;    // the TraceChannel byte
    int32_t hitAt = -1;      // the HitResult / OutHit parameter; -1 = none
    FxHitLayout hit;
    int32_t pc = -1;         // the input-mode calls' PlayerController
    int32_t flag = -1;       // the move setters' bTeleport
    double dflt[3] = {};     // a getter's answer when no script and no per-object default names one
};
static std::mutex g_specMu;
static std::unordered_map<uintptr_t, FxFnSpec> g_fnSpecs;
static void FxRegister(uintptr_t fn, const FxFnSpec& s) {
    std::lock_guard<std::mutex> lk(g_specMu);
    g_fnSpecs[fn] = s;
}
static FxFnSpec FxSpecOf(uintptr_t fn) {
    std::lock_guard<std::mutex> lk(g_specMu);
    const auto it = g_fnSpecs.find(fn);
    return it != g_fnSpecs.end() ? it->second : FxFnSpec{};
}

// One call that reached the engine.
struct FxCall {
    uint64_t seq = 0;
    std::chrono::steady_clock::time_point t{};
    uintptr_t instance = 0, ufunc = 0;
    uint32_t size = 0;
    std::vector<uint8_t> params;   // as they arrived, before the fake wrote any result
    std::thread::id tid{};
    bool onMain = false;           // made on the thread that built the fixture
    bool opHeld = false;           // [LOCK.01] no other thread could take s_opMutex during the call (probe on only)
    FxKind kind = FxKind::Other;
    int32_t rc = 0;                // what the engine answered
};

// ══ the fake engine's state: everything below is guarded by g_engMu ═════════════════════════════════════════════════
static std::mutex g_engMu;
static std::vector<FxCall> g_calls;
static std::map<FxKind, std::deque<int32_t>> g_rcQueue;   // popped per call of that kind
static std::map<FxKind, int32_t> g_rcAlways;              // once the queue is empty; absent = 0
static std::map<FxKind, uint8_t> g_retByte;               // the ReturnValue a call that ran writes; absent = 1
static std::map<FxKind, FxMove> g_moveMode;               // absent = Moves
static std::map<FxKind, double> g_shortBy;
using FxAnswerKey = std::pair<FxKind, uintptr_t>;         // instance 0 = any instance
static std::map<FxAnswerKey, std::array<double, 3>> g_answer;          // a scenario's getter answers
static std::map<FxAnswerKey, std::array<double, 3>> g_answerDefault;   // the fixture's own, kept across resets
static std::set<FxKind> g_silent;                         // answers 0 and writes nothing
static double g_mouse[2] = { kMouse[0], kMouse[1] };
static int32_t g_viewport[2] = { kViewport[0], kViewport[1] };
static double g_deprojLoc[3] = { kDeprojLoc[0], kDeprojLoc[1], kDeprojLoc[2] };
static double g_deprojDir[3] = { kDeprojDir[0], kDeprojDir[1], kDeprojDir[2] };
static double g_attachedWorld[3] = { kAttachedWorld[0], kAttachedWorld[1], kAttachedWorld[2] };
static std::map<FxKind, std::map<uint8_t, std::array<double, 3>>> g_hits;   // per trace kind: channel -> hit point
static std::function<void(FxKind, uintptr_t, int)> g_onCall;              // kind, instance, nth call of that kind
static std::map<FxKind, int> g_onCallSeen;

static std::thread::id g_mainTid;
// [LOCK.01] Each call asks whether another thread can take s_opMutex. Off unless a block turns it on: a held lock costs
// the whole probe window per call, and a cursor teleport makes over twenty.
static std::atomic<bool> g_invProbe{false};
static std::atomic<int> g_invProbed{0}, g_invHeld{0};
constexpr int kInvProbeMs = 20;

// True when a thread other than the caller takes `m` within `ms`. Another thread, because a mutex the caller may own
// itself cannot be asked from the caller's own thread.
static bool FxFreeElsewhere(std::mutex& m, int ms = kInvProbeMs) {
    bool took = false;
    std::thread probe([&] {
        const auto until = std::chrono::steady_clock::now() + std::chrono::milliseconds(ms);
        do {
            if (m.try_lock()) {
                m.unlock();
                took = true;
                return;
            }
            std::this_thread::yield();
        } while (std::chrono::steady_clock::now() < until);
    });
    probe.join();
    return took;
}

static bool FxFits(uint32_t size, int32_t off, size_t len) {
    return off >= 0 && static_cast<size_t>(off) + len <= size;
}
static void FxPutVec(uint8_t* buf, uint32_t size, int32_t off, int32_t width, const double v[3]) {
    if (width >= kVecD) {
        if (!FxFits(size, off, static_cast<size_t>(kVecD))) return;
        for (int i = 0; i < 3; ++i) memcpy(buf + off + i * 8, &v[i], 8);
    } else {
        if (!FxFits(size, off, static_cast<size_t>(kVecF))) return;
        for (int i = 0; i < 3; ++i) {
            const float f = static_cast<float>(v[i]);
            memcpy(buf + off + i * 4, &f, 4);
        }
    }
}
static bool FxGetVec(const uint8_t* buf, uint32_t size, int32_t off, int32_t width, double out[3]) {
    if (width >= kVecD) {
        if (!FxFits(size, off, static_cast<size_t>(kVecD))) return false;
        for (int i = 0; i < 3; ++i) memcpy(&out[i], buf + off + i * 8, 8);
    } else {
        if (!FxFits(size, off, static_cast<size_t>(kVecF))) return false;
        for (int i = 0; i < 3; ++i) {
            float f = 0;
            memcpy(&f, buf + off + i * 4, 4);
            out[i] = f;
        }
    }
    return true;
}
static void FxPutI32(uint8_t* buf, uint32_t size, int32_t off, int32_t v) {
    if (FxFits(size, off, sizeof v)) memcpy(buf + off, &v, sizeof v);
}
static void FxPutByte(uint8_t* buf, uint32_t size, int32_t off, uint8_t v) {
    if (FxFits(size, off, 1)) buf[off] = v;
}
static void FxPutScalar(uint8_t* buf, uint32_t size, int32_t off, int32_t width, double v) {
    if (width == static_cast<int32_t>(sizeof(double))) {
        if (FxFits(size, off, sizeof v)) memcpy(buf + off, &v, sizeof v);
        return;
    }
    const float f = static_cast<float>(v);
    if (FxFits(size, off, sizeof f)) memcpy(buf + off, &f, sizeof f);
}

// ── the game's own memory, as the fake engine touches it ──
// A root's RelativeLocation width, by its class: the UE4 twins carry floats.
static int32_t FxRootWidth(uintptr_t root) {
    uintptr_t cls = 0;
    if (!Macht::ReadSafe(root + Grimoire::OFF_UOBJECT_CLASS, cls)) return kVecD;
    return (cls == g_cCapsuleF || cls == g_cSceneCompF) ? kVecF : kVecD;
}
static bool FxReadVecMem(uintptr_t at, int32_t width, double out[3]) {
    uint8_t raw[kVecD] = {};
    if (!Macht::ReadBytesSafe(at, raw, width >= kVecD ? kVecD : kVecF)) return false;
    if (width >= kVecD) {
        memcpy(out, raw, kVecD);
        return true;
    }
    float f[3] = {};
    memcpy(f, raw, sizeof f);
    for (int i = 0; i < 3; ++i) out[i] = f[i];
    return true;
}
// A write the GAME makes: it lands on an object a scenario aliased read-only too, because only the DLL's own raw writes
// are refused there. One past a committed page is dropped rather than faulting the test.
static void FxGameWrite(uintptr_t addr, const void* data, size_t n) {
    if (g_wfRo && addr >= g_wfRo && addr + n <= g_wfRo + kWfBytes) addr = g_wfRw + (addr - g_wfRo);
    std::ignore = Macht::WriteBytes(addr, data, n);
}
static void FxGameWriteVec(uintptr_t addr, int32_t width, const double v[3]) {
    uint8_t raw[kVecD] = {};
    FxPutVec(raw, sizeof raw, 0, width, v);
    FxGameWrite(addr, raw, width >= kVecD ? kVecD : kVecF);
}
// g_engMu held. The pawn's world position as the engine knows it: where the last move put an attached pawn, else its
// root's RelativeLocation, which is world space for an unattached root. False when the root or its location cannot be
// read.
static bool FxPawnWorld(uintptr_t pawn, double out[3]) {
    uintptr_t root = 0;
    if (!Macht::ReadSafe(pawn + FxOff::kRootComponent, root) || !root) return false;
    uintptr_t parent = 0;
    if (Macht::ReadSafe(root + FxOff::kAttachParent, parent) && parent) {
        for (int i = 0; i < 3; ++i) out[i] = g_attachedWorld[i];
        return true;
    }
    return FxReadVecMem(root + FxOff::kRelativeLocation, FxRootWidth(root), out);
}
// g_engMu held. A move landing: the attached pawn's world position, or the root's RelativeLocation. The cached
// ComponentToWorld copy is left as it was, as a CharacterMovement-driven game leaves it until its next tick -- which is
// what deep force exists to rewrite.
static void FxMoveRoot(uintptr_t root, const double t[3]) {
    if (!root) return;
    uintptr_t parent = 0;
    if (Macht::ReadSafe(root + FxOff::kAttachParent, parent) && parent) {
        for (int i = 0; i < 3; ++i) g_attachedWorld[i] = t[i];
        return;
    }
    FxGameWriteVec(root + FxOff::kRelativeLocation, FxRootWidth(root), t);
}

// g_engMu held. The lookups behind each script.
static int32_t FxNextRc(FxKind kind) {
    auto q = g_rcQueue.find(kind);
    if (q != g_rcQueue.end() && !q->second.empty()) {
        const int32_t rc = q->second.front();
        q->second.pop_front();
        return rc;
    }
    const auto a = g_rcAlways.find(kind);
    return a != g_rcAlways.end() ? a->second : 0;
}
static uint8_t FxRetByteOf(FxKind kind) {
    const auto it = g_retByte.find(kind);
    return it != g_retByte.end() ? it->second : uint8_t{1};
}
static bool FxAnswerOf(const FxFnSpec& s, uintptr_t instance, double out[3]) {
    auto pick = [&](const std::map<FxAnswerKey, std::array<double, 3>>& m, uintptr_t inst) {
        const auto it = m.find(FxAnswerKey{ s.kind, inst });
        if (it == m.end()) return false;
        for (int i = 0; i < 3; ++i) out[i] = it->second[static_cast<size_t>(i)];
        return true;
    };
    if (pick(g_answer, instance) || pick(g_answer, 0)) return true;
    if (s.kind == FxKind::GetLoc) return FxPawnWorld(instance, out);
    if (pick(g_answerDefault, instance)) return true;
    for (int i = 0; i < 3; ++i) out[i] = s.dflt[i];
    return true;
}
static void FxRunMove(const FxFnSpec& s, uintptr_t instance, uint8_t* buf, uint32_t size) {
    const auto modeIt = g_moveMode.find(s.kind);
    const FxMove mode = modeIt != g_moveMode.end() ? modeIt->second : FxMove::Moves;
    FxPutByte(buf, size, s.ret, mode == FxMove::RetFalse ? uint8_t{0} : FxRetByteOf(s.kind));
    if (mode == FxMove::Lies || mode == FxMove::RetFalse) return;
    double t[3] = {};
    if (!FxGetVec(buf, size, s.loc, s.vec, t)) return;
    if (mode == FxMove::Short) {
        const auto by = g_shortBy.find(s.kind);
        t[0] -= by != g_shortBy.end() ? by->second : 0.0;
    }
    const bool onPawn = s.kind == FxKind::SetActorLoc || s.kind == FxKind::TeleportTo;
    uintptr_t root = instance;
    if (onPawn && !Macht::ReadSafe(instance + FxOff::kRootComponent, root)) return;
    FxMoveRoot(root, t);
}
// A trace that ran: the point scripted for the channel it carries is hit (ImpactPoint, with Location trailing it by
// kLocationLag in X whether ImpactPoint exists or not), anything else misses. Path 1 and 2 scan channels; the line
// trace carries the requested one only.
static void FxRunHit(const FxFnSpec& s, uint8_t* buf, uint32_t size) {
    const uint8_t chan = FxFits(size, s.channel, 1) ? buf[s.channel] : uint8_t{0};
    const auto& byChannel = g_hits[s.kind];
    const auto it = byChannel.find(chan);
    if (it == byChannel.end()) {
        FxPutByte(buf, size, s.ret, 0);
        return;
    }
    const double point[3] = { it->second[0], it->second[1], it->second[2] };
    const double lagging[3] = { point[0] - kLocationLag, point[1], point[2] };
    if (s.hitAt >= 0 && s.hit.impact >= 0) FxPutVec(buf, size, s.hitAt + s.hit.impact, s.hit.vec, point);
    if (s.hitAt >= 0 && s.hit.location >= 0) FxPutVec(buf, size, s.hitAt + s.hit.location, s.hit.vec, lagging);
    FxPutByte(buf, size, s.ret, 1);
}
// g_engMu held. What a call that ran (answered 0) does.
static void FxRun(const FxFnSpec& s, uintptr_t instance, uint8_t* buf, uint32_t size) {
    const bool silent = g_silent.count(s.kind) != 0;
    switch (s.kind) {
    case FxKind::GetLoc:
    case FxKind::Forward:
    case FxKind::CamLoc:
    case FxKind::CamRot: {
        double v[3] = {};
        if (!silent && FxAnswerOf(s, instance, v)) FxPutVec(buf, size, s.loc, s.vec, v);
        return;
    }
    case FxKind::Fov: {
        double v[3] = {};
        if (!silent && FxAnswerOf(s, instance, v)) FxPutScalar(buf, size, s.loc, s.scalar, v[0]);
        return;
    }
    case FxKind::SetActorLoc:
    case FxKind::TeleportTo:
    case FxKind::SetWorldLoc:
    case FxKind::SetRelLoc:
        FxRunMove(s, instance, buf, size);
        return;
    case FxKind::SetCtrlRot: {
        double r[3] = {};
        if (!FxGetVec(buf, size, s.loc, s.vec, r)) return;
        r[1] = std::fmod(r[1], kFullTurn);
        if (r[1] < 0) r[1] += kFullTurn;
        FxGameWriteVec(instance + FxOff::kControlRotation, s.vec, r);
        return;
    }
    case FxKind::MousePos:
        if (silent) return;
        FxPutScalar(buf, size, s.x, s.scalar, g_mouse[0]);
        FxPutScalar(buf, size, s.y, s.scalar, g_mouse[1]);
        FxPutByte(buf, size, s.ret, FxRetByteOf(s.kind));
        return;
    case FxKind::Viewport:
        if (silent) return;
        FxPutI32(buf, size, s.x, g_viewport[0]);
        FxPutI32(buf, size, s.y, g_viewport[1]);
        return;
    case FxKind::HitCursor:
    case FxKind::HitScreen:
    case FxKind::Trace:
        FxRunHit(s, buf, size);
        return;
    case FxKind::Deproject:
        if (silent) return;
        FxPutVec(buf, size, s.loc, s.vec, g_deprojLoc);
        FxPutVec(buf, size, s.dir, s.vec, g_deprojDir);
        FxPutByte(buf, size, s.ret, FxRetByteOf(s.kind));
        return;
    case FxKind::SetMoveMode:
    case FxKind::StopMove:
    case FxKind::StopMoveNow:
    case FxKind::InputGameOnly:
    case FxKind::InputGameAndUI:
    case FxKind::Other:
        return;
    }
}

// The engine. The hook runs after the call is recorded and outside g_engMu, on the calling thread -- which holds
// s_opMutex, so a hook may block (the caller stays blocked with the lock) but may call no Wirbel export other than
// GetCurrentMapName.
extern "C" int32_t UE5_CallProcessEventEx(uintptr_t instance, uintptr_t ufunc, uintptr_t params, uint32_t size) {
    FxCall c;
    c.seq = FxStamp();
    c.t = std::chrono::steady_clock::now();
    c.instance = instance;
    c.ufunc = ufunc;
    c.size = size;
    c.tid = std::this_thread::get_id();
    c.onMain = c.tid == g_mainTid;
    if (g_invProbe.load()) {
        c.opHeld = !FxFreeElsewhere(s_opMutex);
        g_invProbed.fetch_add(1);
        if (c.opHeld) g_invHeld.fetch_add(1);
    }
    uint8_t* buf = reinterpret_cast<uint8_t*>(params);
    if (buf && size) c.params.assign(buf, buf + size);
    const FxFnSpec spec = FxSpecOf(ufunc);
    c.kind = spec.kind;

    std::function<void(FxKind, uintptr_t, int)> hook;
    int nth = 0;
    int32_t rc = 0;
    {
        std::lock_guard<std::mutex> lk(g_engMu);
        rc = FxNextRc(spec.kind);
        if (rc == 0 && buf) FxRun(spec, instance, buf, size);
        c.rc = rc;
        g_calls.push_back(std::move(c));
        if (g_onCall) {
            hook = g_onCall;
            nth = g_onCallSeen[spec.kind]++;
        }
    }
    if (hook) hook(spec.kind, instance, nth);
    return rc;
}

// ══ the fixture: function blobs (each registers what the fake engine reads and writes in it) ══════════════════════
static FxHitLayout FxHitLayoutOf(uintptr_t hitStruct) {
    FxHitLayout h;
    if (hitStruct == g_sHit4) {
        h.impact = FxHit4::kImpact;
        h.location = FxHit4::kLocation;
        h.vec = kVecF;
    } else if (hitStruct == g_sHit5) {
        h.impact = FxHit5::kImpact;
        h.location = FxHit5::kLocation;
    } else if (hitStruct == g_sHit5NoImpact) {
        h.location = FxHit5::kLocation;
    }
    return h;
}
// A getter with one return: a StructProperty FVector / FRotator at +0. Without the return flag it is a getter whose
// return cannot be found.
static uintptr_t FxFnVecGetter(FxKind kind, const char* name, uintptr_t owner, uintptr_t vecStruct, int32_t width,
                               bool withReturn, const double* dflt) {
    const uintptr_t rv =
        FxField(kStructProperty, "ReturnValue", 0, width, withReturn ? kRetStruct : kOutStruct, 0, vecStruct);
    const uintptr_t f = FxFunc(name, owner, rv, 1, width, withReturn ? 0 : kNoReturn);
    FxFnSpec s;
    s.kind = kind;
    s.loc = 0;
    s.vec = width;
    if (dflt)
        for (int i = 0; i < 3; ++i) s.dflt[i] = dflt[i];
    FxRegister(f, s);
    return f;
}
// A getter returning one float or double at +0. As the return of a vector getter, its 4 bytes are too few for a
// vector.
static uintptr_t FxFnScalarGetter(FxKind kind, const char* name, uintptr_t owner, const char* type, int32_t bytes,
                                  double dflt) {
    const uintptr_t rv = FxField(type, "ReturnValue", 0, bytes, kRetPod);
    const uintptr_t f = FxFunc(name, owner, rv, 1, bytes, 0);
    FxFnSpec s;
    s.kind = kind;
    s.loc = 0;
    s.vec = bytes;
    s.scalar = bytes;
    s.dflt[0] = dflt;
    FxRegister(f, s);
    return f;
}
// K2_SetActorLocation (withRet) and the component setters (no return), at either width.
static uintptr_t FxFnSetLoc(FxKind kind, const char* name, uintptr_t owner, const FxSetLocLayout& L, bool withRet,
                            const char* locName = "NewLocation") {
    const bool d = L.vec >= kVecD;
    const uintptr_t head = FxChainFields({
        FxField(kStructProperty, locName, L.loc, L.vec, kParm, 0, d ? g_sVector : g_sVectorF),
        FxField(kBoolProperty, "bSweep", L.sweep, 1, kInPod),
        FxField(kStructProperty, "SweepHitResult", L.sweepHit, L.hitBytes, kOutStruct, 0, d ? g_sHit5 : g_sHit4),
        FxField(kBoolProperty, "bTeleport", L.teleport, 1, kInPod),
        withRet ? FxField(kBoolProperty, "ReturnValue", L.ret, 1, kRetPod) : uintptr_t{0},
    });
    const uintptr_t f = FxFunc(name, owner, head, withRet ? 5 : 4, L.parmsSize, withRet ? L.ret : kNoReturn);
    FxFnSpec s;
    s.kind = kind;
    s.loc = L.loc;
    s.vec = L.vec;
    s.ret = withRet ? L.ret : -1;
    s.flag = L.teleport;
    FxRegister(f, s);
    return f;
}
// A function present in reflection with no parameters and ParmsSize 0: what a cooked-out setter leaves.
static uintptr_t FxFnNoParams(FxKind kind, const char* name, uintptr_t owner) {
    const uintptr_t f = FxFunc(name, owner, 0, 0, 0, kNoReturn);
    FxFnSpec s;
    s.kind = kind;
    FxRegister(f, s);
    return f;
}
static uintptr_t FxFnTeleportTo(uintptr_t owner, const FxTeleportToLayout& L) {
    const bool d = L.vec >= kVecD;
    const uintptr_t head = FxChainFields({
        FxField(kStructProperty, "DestLocation", L.loc, L.vec, kParm, 0, d ? g_sVector : g_sVectorF),
        FxField(kStructProperty, "DestRotation", L.rot, L.vec, kParm, 0, d ? g_sRotator : g_sRotatorF),
        FxField(kBoolProperty, "ReturnValue", L.ret, 1, kRetPod),
    });
    const uintptr_t f = FxFunc("K2_TeleportTo", owner, head, 3, L.parmsSize, L.ret);
    FxFnSpec s;
    s.kind = FxKind::TeleportTo;
    s.loc = L.loc;
    s.rot = L.rot;
    s.vec = L.vec;
    s.ret = L.ret;
    FxRegister(f, s);
    return f;
}
static uintptr_t FxFnSetCtrlRot(uintptr_t owner, int32_t vec) {
    const uintptr_t p =
        FxField(kStructProperty, "NewRotation", 0, vec, kParm, 0, vec >= kVecD ? g_sRotator : g_sRotatorF);
    const uintptr_t f = FxFunc("SetControlRotation", owner, p, 1, vec, kNoReturn);
    FxFnSpec s;
    s.kind = FxKind::SetCtrlRot;
    s.loc = 0;
    s.vec = vec;
    FxRegister(f, s);
    return f;
}
static uintptr_t FxFnSetMoveMode(uintptr_t owner) {
    const uintptr_t head = FxChainFields({
        FxField(kByteProperty, "NewMovementMode", FxSmm::kMode, 1, kInPod),
        FxField(kByteProperty, "NewCustomMode", FxSmm::kCustom, 1, kInPod),
    });
    const uintptr_t f = FxFunc("SetMovementMode", owner, head, 2, FxSmm::kParmsSize, kNoReturn);
    FxFnSpec s;
    s.kind = FxKind::SetMoveMode;
    FxRegister(f, s);
    return f;
}
static uintptr_t FxFnMouse(uintptr_t owner, const FxMouseLayout& L) {
    const char* type = L.bytes == 8 ? kDoubleProperty : kFloatProperty;
    const uintptr_t head = FxChainFields({
        FxField(type, "LocationX", L.x, L.bytes, kOutPod),
        FxField(type, "LocationY", L.y, L.bytes, kOutPod),
        FxField(kBoolProperty, "ReturnValue", L.ret, 1, kRetPod),
    });
    const uintptr_t f = FxFunc("GetMousePosition", owner, head, 3, L.parmsSize, L.ret);
    FxFnSpec s;
    s.kind = FxKind::MousePos;
    s.x = L.x;
    s.y = L.y;
    s.scalar = L.bytes;
    s.ret = L.ret;
    FxRegister(f, s);
    return f;
}
static uintptr_t FxFnViewport(uintptr_t owner) {
    const uintptr_t head = FxChainFields({
        FxField(kIntProperty, "SizeX", FxVp::kSizeX, 4, kOutPod),
        FxField(kIntProperty, "SizeY", FxVp::kSizeY, 4, kOutPod),
    });
    const uintptr_t f = FxFunc("GetViewportSize", owner, head, 2, FxVp::kParmsSize, kNoReturn);
    FxFnSpec s;
    s.kind = FxKind::Viewport;
    s.x = FxVp::kSizeX;
    s.y = FxVp::kSizeY;
    FxRegister(f, s);
    return f;
}
// GetHitResultUnderCursorByChannel / GetHitResultAtScreenPosition at one layout; withHitResult false leaves the
// HitResult parameter out.
static uintptr_t FxFnHit(FxKind kind, const char* name, uintptr_t owner, const FxHitCallLayout& L, uintptr_t hitStruct,
                         bool withHitResult) {
    const uintptr_t screenStruct = L.screenBytes >= 16 ? g_sVector2D : g_sVector2DF;
    const uintptr_t head = FxChainFields({
        L.screen >= 0 ? FxField(kStructProperty, "ScreenPosition", L.screen, L.screenBytes, kParm, 0, screenStruct)
                      : uintptr_t{0},
        FxField(kByteProperty, "TraceChannel", L.channel, 1, kInPod),
        FxField(kBoolProperty, "bTraceComplex", L.complex, 1, kInPod),
        withHitResult ? FxField(kStructProperty, "HitResult", L.hit, L.hitBytes, kOutStruct, 0, hitStruct)
                      : uintptr_t{0},
        FxField(kBoolProperty, "ReturnValue", L.ret, 1, kRetPod),
    });
    const int32_t numParms = (L.screen >= 0 ? 1 : 0) + 3 + (withHitResult ? 1 : 0);
    const uintptr_t f = FxFunc(name, owner, head, numParms, L.parmsSize, L.ret);
    FxFnSpec s;
    s.kind = kind;
    s.loc = L.screen;
    s.channel = L.channel;
    s.hitAt = withHitResult ? L.hit : -1;
    s.hit = FxHitLayoutOf(hitStruct);
    s.ret = L.ret;
    FxRegister(f, s);
    return f;
}
static uintptr_t FxFnDeproject(uintptr_t owner, const FxDeprojectLayout& L) {
    const char* screenType = L.screenBytes == 8 ? kDoubleProperty : kFloatProperty;
    const uintptr_t vs = L.vec >= kVecD ? g_sVector : g_sVectorF;
    const uintptr_t head = FxChainFields({
        FxField(screenType, "ScreenX", L.sx, L.screenBytes, kInPod),
        FxField(screenType, "ScreenY", L.sy, L.screenBytes, kInPod),
        FxField(kStructProperty, "WorldLocation", L.worldLoc, L.vec, kOutStruct, 0, vs),
        FxField(kStructProperty, "WorldDirection", L.worldDir, L.vec, kOutStruct, 0, vs),
        FxField(kBoolProperty, "ReturnValue", L.ret, 1, kRetPod),
    });
    const uintptr_t f = FxFunc("DeprojectScreenPositionToWorld", owner, head, 5, L.parmsSize, L.ret);
    FxFnSpec s;
    s.kind = FxKind::Deproject;
    s.x = L.sx;
    s.y = L.sy;
    s.scalar = L.screenBytes;
    s.loc = L.worldLoc;
    s.dir = L.worldDir;
    s.vec = L.vec;
    s.ret = L.ret;
    FxRegister(f, s);
    return f;
}
// SetInputMode_GameOnly (PlayerController, bFlushInput) or a GameAndUI shape (PlayerController, InWidgetToFocus,
// InMouseLockMode, bHideCursorDuringCapture, bFlushInput) under the name given.
static uintptr_t FxFnInputMode(FxKind kind, const char* name, uintptr_t owner, bool gameAndUi) {
    const uintptr_t head = gameAndUi
        ? FxChainFields({
              FxField(kObjectProperty, "PlayerController", FxIm::kPc, kPtrBytes, kParm),
              FxField(kObjectProperty, "InWidgetToFocus", FxIm::kWidget, kPtrBytes, kParm),
              FxField(kByteProperty, "InMouseLockMode", FxIm::kLockMode, 1, kInPod),
              FxField(kBoolProperty, "bHideCursorDuringCapture", FxIm::kHideCursor, 1, kInPod),
              FxField(kBoolProperty, "bFlushInput", FxIm::kFlushUi, 1, kInPod),
          })
        : FxChainFields({
              FxField(kObjectProperty, "PlayerController", FxIm::kPc, kPtrBytes, kParm),
              FxField(kBoolProperty, "bFlushInput", FxIm::kFlush, 1, kInPod),
          });
    const uintptr_t f = FxFunc(name, owner, head, gameAndUi ? 5 : 2,
                               gameAndUi ? FxIm::kGameAndUiParms : FxIm::kGameOnlyParms, kNoReturn);
    FxFnSpec s;
    s.kind = kind;
    s.pc = FxIm::kPc;
    FxRegister(f, s);
    return f;
}
// LineTraceSingle's view for the fake engine; hitStruct 0 = this variant has no OutHit.
static void FxSpecTrace(uintptr_t fn, bool ue4, uintptr_t hitStruct) {
    FxFnSpec s;
    s.kind = FxKind::Trace;
    if (ue4) {
        s.vec = kVecF;
        s.loc = FxLtsF::kStart;
        s.channel = FxLtsF::kChannel;
        s.ret = FxLtsF::kReturn;
        s.hitAt = FxLtsF::kOutHit;
        s.hit = FxHitLayoutOf(g_sHit4);
    } else {
        s.vec = kVecD;
        s.loc = FxLts::kStart;
        s.channel = FxLts::kChannel;
        s.ret = FxLts::kReturn;
        s.hitAt = hitStruct ? FxLts::kOutHit : -1;
        s.hit = FxHitLayoutOf(hitStruct);
    }
    FxRegister(fn, s);
}

// ── the struct and parameter-chain builders carried from schlacht_seethrough_test ──
static uintptr_t FxTripleStruct(const char* name, const char* type, int32_t member, const char* a, const char* b,
                                const char* c) {
    return FxStruct(name, 3 * member,
                    FxChainFields({ FxField(type, a, 0, member, 0), FxField(type, b, member, member, 0),
                                    FxField(type, c, 2 * member, member, 0) }));
}
static uintptr_t FxPairStruct(const char* name, const char* type, int32_t member) {
    return FxStruct(name, 2 * member,
                    FxChainFields({ FxField(type, "X", 0, member, 0), FxField(type, "Y", member, member, 0) }));
}
static uintptr_t FxHitResult5Struct(const char* name, bool withImpact, bool withLocation) {
    using namespace FxHit5;
    const uintptr_t comp = FxField(kWeakObjectProperty, "Component", kComponent, kPtrBytes, 0);
    uintptr_t next = FxField(kStructProperty, "HitObjectHandle", kHandle, kHandleBytes, 0, comp, g_sHandle);
    if (withImpact) next = FxField(kStructProperty, "ImpactPoint", kImpact, kVecD, 0, next, g_sVector);
    if (withLocation) next = FxField(kStructProperty, "Location", kLocation, kVecD, 0, next, g_sVector);
    const uintptr_t time = FxField(kFloatProperty, "Time", kTime, 4, 0, next);
    const uintptr_t face = FxField(kIntProperty, "FaceIndex", kFaceIndex, 4, 0, time);
    return FxStruct(name, kBytes, face);
}
static uintptr_t FxHitResult4Struct() {
    using namespace FxHit4;
    const uintptr_t comp = FxField(kWeakObjectProperty, "Component", kComponent, kPtrBytes, 0);
    const uintptr_t actor = FxField(kWeakObjectProperty, "Actor", kActor, kPtrBytes, 0, comp);
    const uintptr_t impact = FxField(kStructProperty, "ImpactPoint", kImpact, kVecF, 0, actor, g_sVectorF);
    const uintptr_t location = FxField(kStructProperty, "Location", kLocation, kVecF, 0, impact, g_sVectorF);
    const uintptr_t distance = FxField(kFloatProperty, "Distance", kDistance, 4, 0, location);
    const uintptr_t time = FxField(kFloatProperty, "Time", kTime, 4, 0, distance);
    return FxStruct("HitResult4", kBytes, time);
}
// LineTraceSingle's UE5 chain; hitStruct 0 leaves OutHit out.
static uintptr_t FxLtsDChain(uintptr_t hitStruct) {
    using namespace FxLts;
    const uintptr_t ret = FxField(kBoolProperty, "ReturnValue", kReturn, 1, kParm | kOut | kRet | kPod);
    const uintptr_t self = FxField(kBoolProperty, "bIgnoreSelf", kIgnoreSelf, 1, kParm | kPod, ret);
    const uintptr_t hit =
        hitStruct ? FxField(kStructProperty, "OutHit", kOutHit, kOutHitBytes, kParm | kOut, self, hitStruct) : self;
    const uintptr_t draw = FxField(kByteProperty, "DrawDebugType", kDrawDebug, 1, kParm | kPod, hit);
    const uintptr_t ignore = FxField(kArrayProperty, "ActorsToIgnore", kIgnore, kTArrayBytes, kParm, draw);
    const uintptr_t traceComplex = FxField(kBoolProperty, "bTraceComplex", kComplex, 1, kParm | kPod, ignore);
    const uintptr_t channel = FxField(kByteProperty, "TraceChannel", kChannel, 1, kParm | kPod, traceComplex);
    const uintptr_t end = FxField(kStructProperty, "End", kEnd, kVecD, kParm, channel, g_sVector);
    // Qualified: the global kStart (the hero's position) is visible here too.
    const uintptr_t start = FxField(kStructProperty, "Start", FxLts::kStart, kVecD, kParm, end, g_sVector);
    return FxField(kObjectProperty, "WorldContextObject", kWorldContext, kPtrBytes, kParm, start);
}
static uintptr_t FxLtsFChain() {
    using namespace FxLtsF;
    const uintptr_t ret = FxField(kBoolProperty, "ReturnValue", kReturn, 1, kParm | kOut | kRet | kPod);
    // Lower-case, as an FName table that registered this spelling first holds it.
    const uintptr_t self = FxField(kBoolProperty, "bignoreself", kIgnoreSelf, 1, kParm | kPod, ret);
    const uintptr_t hit = FxField(kStructProperty, "OutHit", kOutHit, kOutHitBytes, kParm | kOut, self, g_sHit4);
    const uintptr_t draw = FxField(kByteProperty, "DrawDebugType", kDrawDebug, 1, kParm | kPod, hit);
    const uintptr_t ignore = FxField(kArrayProperty, "ActorsToIgnore", kIgnore, kTArrayBytes, kParm, draw);
    const uintptr_t traceComplex = FxField(kBoolProperty, "bTraceComplex", kComplex, 1, kParm | kPod, ignore);
    const uintptr_t channel = FxField(kByteProperty, "TraceChannel", kChannel, 1, kParm | kPod, traceComplex);
    const uintptr_t end = FxField(kStructProperty, "End", kEnd, kVecF, kParm, channel, g_sVectorF);
    const uintptr_t start = FxField(kStructProperty, "Start", FxLtsF::kStart, kVecF, kParm, end, g_sVectorF);
    return FxField(kObjectProperty, "WorldContextObject", kWorldContext, kPtrBytes, kParm, start);
}
// A LineTraceSingle sibling (ForObjects / ByProfile): LineTraceSingle's members with `what` where TraceChannel sits
// and every later member moved past it, so its block is its own and not LineTraceSingle's.
static uintptr_t FxSiblingChain(const char* whatType, const char* whatName, int32_t whatBytes, int32_t& retOff,
                                int32_t& parmsSize) {
    const int32_t what = FxLts::kChannel;
    const int32_t traceComplex = what + whatBytes;
    const int32_t ignore = traceComplex + kPtrBytes;   // the TArray keeps its 8-byte alignment
    const int32_t draw = ignore + kTArrayBytes;
    const int32_t outHit = draw + kPtrBytes;
    const int32_t self = outHit + FxHit5::kBytes;
    retOff = self + 1;
    parmsSize = self + kPtrBytes;                       // the two bools, rounded up to the block's alignment
    const uintptr_t ret = FxField(kBoolProperty, "ReturnValue", retOff, 1, kParm | kOut | kRet | kPod);
    const uintptr_t selfF = FxField(kBoolProperty, "bIgnoreSelf", self, 1, kParm | kPod, ret);
    const uintptr_t hit = FxField(kStructProperty, "OutHit", outHit, FxLts::kOutHitBytes, kParm | kOut, selfF, g_sHit5);
    const uintptr_t drawF = FxField(kByteProperty, "DrawDebugType", draw, 1, kParm | kPod, hit);
    const uintptr_t ignoreF = FxField(kArrayProperty, "ActorsToIgnore", ignore, kTArrayBytes, kParm, drawF);
    const uintptr_t complexF = FxField(kBoolProperty, "bTraceComplex", traceComplex, 1, kParm | kPod, ignoreF);
    const uintptr_t whatF = FxField(whatType, whatName, what, whatBytes, kParm, complexF);
    const uintptr_t end = FxField(kStructProperty, "End", FxLts::kEnd, kVecD, kParm, whatF, g_sVector);
    const uintptr_t start = FxField(kStructProperty, "Start", FxLts::kStart, kVecD, kParm, end, g_sVector);
    return FxField(kObjectProperty, "WorldContextObject", FxLts::kWorldContext, kPtrBytes, kParm, start);
}

// ══ function lists: built on demand, one fresh chain per shape, memoized by shape ═════════════════════════════════
// Main thread only, between ops. Each list is its own blobs: UField::Next lives in the blob, so no blob is in two lists.
static std::map<std::string, uintptr_t> g_listMemo;
static std::map<uintptr_t, std::map<std::string, uintptr_t>> g_listBlobs;   // head -> name -> blob
static uintptr_t FxList(const std::string& key, const std::function<std::vector<uintptr_t>()>& make) {
    const auto it = g_listMemo.find(key);
    if (it != g_listMemo.end()) return it->second;
    uintptr_t head = 0, prev = 0;
    std::map<std::string, uintptr_t> names;
    for (uintptr_t f : make()) {
        if (!f) continue;
        if (prev) FxSetPtr(prev, DynOff::UFIELD_NEXT, f);
        else head = f;
        prev = f;
        names[g_fnName[f]] = f;
    }
    g_listMemo[key] = head;
    if (head) g_listBlobs[head] = names;
    return head;
}
// The blob named `name` (exact case) in the list `head` starts; 0 when it has none.
static uintptr_t FxFnIn(uintptr_t head, const char* name) {
    const auto l = g_listBlobs.find(head);
    if (l == g_listBlobs.end()) return 0;
    const auto f = l->second.find(name);
    return f != l->second.end() ? f->second : 0;
}

// AActor: K2_GetActorLocation, GetActorForwardVector, K2_SetActorLocation, K2_TeleportTo (ActorF's float twins on ue4).
enum class FxGetLocShape { Vec, NoReturn, Scalar, Missing };
enum class FxSalShape { Ok, Missing, Cooked, Lowercase, LocationParam };
struct FxActorFnOpts {
    FxGetLocShape getLoc = FxGetLocShape::Vec;
    bool forward = true;
    FxSalShape setActorLoc = FxSalShape::Ok;
    bool teleportTo = true;
    bool ue4 = false;
};
static uintptr_t FxActorFnList(const FxActorFnOpts& o) {
    const std::string key = FxFmt("actor/%d/%d/%d/%d/%d", static_cast<int>(o.getLoc), o.forward ? 1 : 0,
                                  static_cast<int>(o.setActorLoc), o.teleportTo ? 1 : 0, o.ue4 ? 1 : 0);
    return FxList(key, [&] {
        const uintptr_t owner = o.ue4 ? g_cActorF : g_cActor;
        const int32_t w = o.ue4 ? kVecF : kVecD;
        const uintptr_t vs = o.ue4 ? g_sVectorF : g_sVector;
        const FxSetLocLayout& sal = o.ue4 ? kSal4 : kSal5;
        const char* const kGetLoc = "K2_GetActorLocation";
        const char* const kSetLoc = "K2_SetActorLocation";
        uintptr_t getLoc = 0, setLoc = 0;
        switch (o.getLoc) {
        case FxGetLocShape::Vec:
            getLoc = FxFnVecGetter(FxKind::GetLoc, kGetLoc, owner, vs, w, true, nullptr);
            break;
        case FxGetLocShape::NoReturn:
            getLoc = FxFnVecGetter(FxKind::GetLoc, kGetLoc, owner, vs, w, false, nullptr);
            break;
        case FxGetLocShape::Scalar:
            getLoc = FxFnScalarGetter(FxKind::GetLoc, kGetLoc, owner, kFloatProperty, 4, 0.0);
            break;
        case FxGetLocShape::Missing:
            break;
        }
        switch (o.setActorLoc) {
        case FxSalShape::Ok:
            setLoc = FxFnSetLoc(FxKind::SetActorLoc, kSetLoc, owner, sal, true);
            break;
        case FxSalShape::Missing:
            break;
        case FxSalShape::Cooked:
            setLoc = FxFnNoParams(FxKind::SetActorLoc, kSetLoc, owner);
            break;
        case FxSalShape::Lowercase:   // FindFunc compares case-insensitively
            setLoc = FxFnSetLoc(FxKind::SetActorLoc, "k2_setactorlocation", owner, sal, true);
            break;
        case FxSalShape::LocationParam:   // the location parameter under a name Wirbel does not pack
            setLoc = FxFnSetLoc(FxKind::SetActorLoc, kSetLoc, owner, sal, true, "Location");
            break;
        }
        return std::vector<uintptr_t>{
            getLoc,
            o.forward ? FxFnVecGetter(FxKind::Forward, "GetActorForwardVector", owner, vs, w, true, kFwd) : 0,
            setLoc,
            o.teleportTo ? FxFnTeleportTo(owner, o.ue4 ? kTp4 : kTp5) : 0,
        };
    });
}
// USceneComponent: K2_SetWorldLocation (no return by default) and K2_SetRelativeLocation.
enum class FxCompShape { NoReturn, BoolReturn, Missing };
struct FxSceneFnOpts {
    FxCompShape world = FxCompShape::NoReturn;
    bool relative = true;
    bool ue4 = false;
};
static uintptr_t FxSceneFnList(const FxSceneFnOpts& o) {
    const std::string key = FxFmt("scene/%d/%d/%d", static_cast<int>(o.world), o.relative ? 1 : 0, o.ue4 ? 1 : 0);
    return FxList(key, [&] {
        const uintptr_t owner = o.ue4 ? g_cSceneCompF : g_cSceneComponent;
        const FxSetLocLayout& L = o.ue4 ? kSal4 : kSal5;
        return std::vector<uintptr_t>{
            o.world == FxCompShape::Missing
                ? 0
                : FxFnSetLoc(FxKind::SetWorldLoc, "K2_SetWorldLocation", owner, L, o.world == FxCompShape::BoolReturn),
            o.relative ? FxFnSetLoc(FxKind::SetRelLoc, "K2_SetRelativeLocation", owner, L, false) : 0,
        };
    });
}
// AController: SetControlRotation and StopMovement.
struct FxCtrlFnOpts {
    bool setRot = true;
    bool stop = true;
    bool ue4 = false;
};
static uintptr_t FxCtrlFnList(const FxCtrlFnOpts& o) {
    const std::string key = FxFmt("ctrl/%d/%d/%d", o.setRot ? 1 : 0, o.stop ? 1 : 0, o.ue4 ? 1 : 0);
    return FxList(key, [&] {
        const uintptr_t owner = o.ue4 ? g_cControllerF : g_cController;
        return std::vector<uintptr_t>{
            o.setRot ? FxFnSetCtrlRot(owner, o.ue4 ? kVecF : kVecD) : 0,
            o.stop ? FxFnNoParams(FxKind::StopMove, "StopMovement", owner) : 0,
        };
    });
}
// APlayerController: the mouse, the viewport, both hit tests and the deprojection. On ue4 every shape but Missing is the
// UE4 one (HitResult4, an 8-byte ScreenPosition, float vectors).
enum class FxMouseShape { Float, Double, Missing };
enum class FxHitShape { Hit5, NoImpact, NoPoint, Short, NoHitResult, Missing };
enum class FxDeprojShape { Float, Double, Missing };
struct FxPcFnOpts {
    FxMouseShape mouse = FxMouseShape::Float;
    bool viewport = true;
    FxHitShape cursor = FxHitShape::Hit5;
    FxHitShape screen = FxHitShape::Hit5;
    FxDeprojShape deproject = FxDeprojShape::Float;
    bool ue4 = false;
};
static uintptr_t FxFnHitOf(FxKind kind, uintptr_t owner, FxHitShape shape, bool ue4) {
    if (shape == FxHitShape::Missing) return 0;
    const bool screen = kind == FxKind::HitScreen;
    const char* name = screen ? "GetHitResultAtScreenPosition" : "GetHitResultUnderCursorByChannel";
    if (ue4) return FxFnHit(kind, name, owner, screen ? kHs4 : kHc4, g_sHit4, true);
    const FxHitCallLayout& L = shape == FxHitShape::Short ? (screen ? kHsShort : kHcShort) : (screen ? kHs5 : kHc5);
    const uintptr_t st = shape == FxHitShape::NoImpact  ? g_sHit5NoImpact
                         : shape == FxHitShape::NoPoint ? g_sHit5NoPoint
                                                        : g_sHit5;
    return FxFnHit(kind, name, owner, L, st, shape != FxHitShape::NoHitResult);
}
static uintptr_t FxPcFnList(const FxPcFnOpts& o) {
    const std::string key = FxFmt("pc/%d/%d/%d/%d/%d/%d", static_cast<int>(o.mouse), o.viewport ? 1 : 0,
                                  static_cast<int>(o.cursor), static_cast<int>(o.screen), static_cast<int>(o.deproject),
                                  o.ue4 ? 1 : 0);
    return FxList(key, [&] {
        const uintptr_t owner = o.ue4 ? g_cPlayerControllerF : g_cPlayerController;
        uintptr_t deproject = 0;
        if (o.deproject != FxDeprojShape::Missing)
            deproject = FxFnDeproject(owner, o.ue4 ? kDp4 : (o.deproject == FxDeprojShape::Double ? kDp5D : kDp5));
        return std::vector<uintptr_t>{
            o.mouse == FxMouseShape::Missing ? 0 : FxFnMouse(owner, o.mouse == FxMouseShape::Double ? kMpD : kMp),
            o.viewport ? FxFnViewport(owner) : 0,
            FxFnHitOf(FxKind::HitCursor, owner, o.cursor, o.ue4),
            FxFnHitOf(FxKind::HitScreen, owner, o.screen, o.ue4),
            deproject,
        };
    });
}
// UMovementComponent: StopMovementImmediately. UCharacterMovementComponent: SetMovementMode. The UE4 twin carries both.
static uintptr_t FxMoveCompFnList(bool stopNow) {
    return FxList(FxFmt("movecomp/%d", stopNow ? 1 : 0), [&] {
        return std::vector<uintptr_t>{
            stopNow ? FxFnNoParams(FxKind::StopMoveNow, "StopMovementImmediately", g_cMoveComp) : 0 };
    });
}
static uintptr_t FxCmcFnList(bool setMode) {
    return FxList(FxFmt("cmc/%d", setMode ? 1 : 0),
                  [&] { return std::vector<uintptr_t>{ setMode ? FxFnSetMoveMode(g_cCmc) : 0 }; });
}
static uintptr_t FxCmcFFnList() {
    return FxList("cmcF", [] {
        return std::vector<uintptr_t>{ FxFnSetMoveMode(g_cCmcF),
                                       FxFnNoParams(FxKind::StopMoveNow, "StopMovementImmediately", g_cCmcF) };
    });
}
// APlayerCameraManager: GetCameraLocation, GetCameraRotation, GetFOVAngle.
enum class FxCamLocShape { Vec, Scalar, Missing };
enum class FxFovShape { Float, Double, Missing };
struct FxCamFnOpts {
    FxCamLocShape loc = FxCamLocShape::Vec;
    bool rot = true;
    FxFovShape fov = FxFovShape::Float;
    bool ue4 = false;
};
static uintptr_t FxCamFnList(const FxCamFnOpts& o) {
    const std::string key = FxFmt("cam/%d/%d/%d/%d", static_cast<int>(o.loc), o.rot ? 1 : 0, static_cast<int>(o.fov),
                                  o.ue4 ? 1 : 0);
    return FxList(key, [&] {
        const uintptr_t owner = o.ue4 ? g_cPcmF : g_cPcm;
        const int32_t w = o.ue4 ? kVecF : kVecD;
        uintptr_t loc = 0, fov = 0;
        if (o.loc == FxCamLocShape::Vec)
            loc = FxFnVecGetter(FxKind::CamLoc, "GetCameraLocation", owner, o.ue4 ? g_sVectorF : g_sVector, w, true,
                                kCamLoc);
        else if (o.loc == FxCamLocShape::Scalar)
            loc = FxFnScalarGetter(FxKind::CamLoc, "GetCameraLocation", owner, kFloatProperty, 4, kCamLoc[0]);
        if (o.fov == FxFovShape::Float)
            fov = FxFnScalarGetter(FxKind::Fov, "GetFOVAngle", owner, kFloatProperty, 4, kFovGetter);
        else if (o.fov == FxFovShape::Double)
            fov = FxFnScalarGetter(FxKind::Fov, "GetFOVAngle", owner, kDoubleProperty, 8, kFovGetterDouble);
        return std::vector<uintptr_t>{
            loc,
            o.rot ? FxFnVecGetter(FxKind::CamRot, "GetCameraRotation", owner, o.ue4 ? g_sRotatorF : g_sRotator, w,
                                  true, kCamRot)
                  : 0,
            fov,
        };
    });
}
// UWidgetBlueprintLibrary: SetInputMode_GameOnly and SetInputMode_GameAndUIEx (or the non-Ex name, which Wirbel must not
// fall back to). decoy builds the same names for WidgetBlueprintLibraryEx.
enum class FxUiShape { Ex, NonEx, Missing };
struct FxWblFnOpts {
    bool gameOnly = true;
    FxUiShape ui = FxUiShape::Ex;
    bool decoy = false;
};
static uintptr_t FxWblFnList(const FxWblFnOpts& o) {
    const std::string key = FxFmt("wbl/%d/%d/%d", o.gameOnly ? 1 : 0, static_cast<int>(o.ui), o.decoy ? 1 : 0);
    return FxList(key, [&] {
        const uintptr_t owner = o.decoy ? g_cWblEx : g_cWbl;
        uintptr_t ui = 0;
        if (o.ui != FxUiShape::Missing)
            ui = FxFnInputMode(FxKind::InputGameAndUI,
                               o.ui == FxUiShape::Ex ? "SetInputMode_GameAndUIEx" : "SetInputMode_GameAndUI", owner,
                               true);
        return std::vector<uintptr_t>{
            o.gameOnly ? FxFnInputMode(FxKind::InputGameOnly, "SetInputMode_GameOnly", owner, false) : 0, ui };
    });
}

// ══ variant swaps: each writes a fresh POINTER, never a walked field in place ══════════════════════════════════════
// Ubel memoizes a class walk keyed by its OWN witness pointers: a field list swapped on a base class is not seen by a
// subclass walked before. So a field variant is a fresh class the object is switched to (FxSetClass), and only a
// function list (WalkFunctions, unmemoized) is swapped on the class that declares it.
static void FxActorFns(uintptr_t head) { FxSetFns(g_cActor, head); }
static void FxCtrlFns(uintptr_t head) { FxSetFns(g_cController, head); }
static void FxPcFns(uintptr_t head) { FxSetFns(g_cPlayerController, head); }
static void FxSceneFns(uintptr_t head) { FxSetFns(g_cSceneComponent, head); }
static void FxMoveCompFns(uintptr_t head) { FxSetFns(g_cMoveComp, head); }
static void FxCmcFns(uintptr_t head) { FxSetFns(g_cCmc, head); }
static void FxCamFns(uintptr_t head) { FxSetFns(g_cPcm, head); }   // BP_CameraManager_C inherits it; variants do not
static void FxKsl(uintptr_t head) { FxSetFns(g_cKsl, head); }
static void FxWbl(uintptr_t head) { FxSetFns(g_cWbl, head); }
static void FxActorFFns(uintptr_t head) { FxSetFns(g_cActorF, head); }
static void FxCtrlFFns(uintptr_t head) { FxSetFns(g_cControllerF, head); }
static void FxPcFFns(uintptr_t head) { FxSetFns(g_cPlayerControllerF, head); }
static void FxSceneFFns(uintptr_t head) { FxSetFns(g_cSceneCompF, head); }
static void FxCamFFns(uintptr_t head) { FxSetFns(g_cPcmF, head); }

// Every class's heads and super as built, so a reset puts back whatever a scenario swapped.
struct FxClassDefaults { uintptr_t cls, children, childProps, super; };
static std::vector<FxClassDefaults> g_classDefaults;
static void FxSnapshotClasses() {
    g_classDefaults.clear();
    for (uintptr_t c : g_allClasses)
        g_classDefaults.push_back({ c, FxGetPtr(c, DynOff::USTRUCT_CHILDREN), FxGetPtr(c, DynOff::USTRUCT_CHILDPROPS),
                                    FxGetPtr(c, DynOff::USTRUCT_SUPER) });
}
static void FxRestoreClasses() {
    for (const auto& d : g_classDefaults) {
        FxSetPtr(d.cls, DynOff::USTRUCT_CHILDREN, d.children);
        FxSetPtr(d.cls, DynOff::USTRUCT_CHILDPROPS, d.childProps);
        FxSetPtr(d.cls, DynOff::USTRUCT_SUPER, d.super);
    }
}

// ══ the fixture: classes and structs ═════════════════════════════════════════════════════════════════════════════
// A controller's own PlayerController fields: AcknowledgedPawn, PlayerCameraManager, Player and (cursor) a
// bShowMouseCursor bitfield at the layout given.
static uintptr_t FxPcOwnFields(bool cursor, const std::vector<FxBoolLayout>& layout) {
    return FxChainFields({ FxObjF("AcknowledgedPawn", FxOff::kAckPawn),
                           FxObjF("PlayerCameraManager", FxOff::kCameraManager), FxObjF("Player", FxOff::kPlayer),
                           cursor ? FxBoolField("bShowMouseCursor", FxOff::kShowCursor, layout) : uintptr_t{0} });
}
static uintptr_t FxCtrlRotField(int32_t width) {
    return FxStructF("ControlRotation", FxOff::kControlRotation, width, width >= kVecD ? g_sRotator : g_sRotatorF);
}
// A camera manager's fields: PendingViewTarget first (its name contains the word, and a fuzzy read would take it),
// ViewTarget unless left out, and the cache field(s) given.
static uintptr_t FxCamFields(bool viewTarget, uintptr_t cacheA, uintptr_t cacheB = 0) {
    return FxChainFields({ FxStructF("PendingViewTarget", FxOff::kPendingViewTarget, kViewTargetBytes, g_sViewTarget),
                           viewTarget ? FxStructF("ViewTarget", FxOff::kViewTarget, kViewTargetBytes, g_sViewTarget)
                                      : uintptr_t{0},
                           cacheA, cacheB });
}
static uintptr_t FxCacheField(uintptr_t cacheStruct, int32_t bytes = kCacheBytes) {
    return FxStructF("CameraCachePrivate", FxOff::kCameraCache, bytes, cacheStruct);
}
// A cache field whose struct sits at FSTRUCTPROP_STRUCT + 8, with `atBase` (0, a class, an empty-named struct) where
// the struct pointer normally is.
static uintptr_t FxCacheFieldProbed(uintptr_t atBase) {
    const uintptr_t f = FxCacheField(atBase);
    FxPut64(f, static_cast<int>(DynOff::FSTRUCTPROP_STRUCT) + kPtrBytes, g_sCache);
    return f;
}
static uintptr_t FxCmcOwnFields(int32_t width, bool withVelocity) {
    const uintptr_t vs = width >= kVecD ? g_sVector : g_sVectorF;
    return FxChainFields({
        withVelocity ? FxStructF("Velocity", FxOff::kVelocity, width, vs) : uintptr_t{0},
        FxStructF("Acceleration", FxOff::kAcceleration, width, vs),
        FxScalarF(kFloatProperty, "MaxWalkSpeed", FxOff::kMaxWalkSpeed, 4),
        FxScalarF(kFloatProperty, "GravityScale", FxOff::kGravityScale, 4),
        FxScalarF(kFloatProperty, "JumpZVelocity", FxOff::kJumpZVelocity, 4),
        FxScalarF(kByteProperty, "CustomMovementMode", FxOff::kCustomMode, 1),
        FxScalarF(kByteProperty, "MovementMode", FxOff::kMovementMode, 1),
    });
}
static const char* FxBoolVariantName(FxBoolVariant v) {
    switch (v) {
    case FxBoolVariant::At80: return "PcBoolAt80";
    case FxBoolVariant::FieldSize4: return "PcBoolFieldSize4";
    case FxBoolVariant::ByteMask3: return "PcBoolByteMask3";
    case FxBoolVariant::ByteOffset1: return "PcBoolByteOffset1";
    case FxBoolVariant::ByteMask8: return "PcBoolByteMask8";
    case FxBoolVariant::Native: return "PcBoolNative";
    case FxBoolVariant::ByteOffset8: return "PcBoolByteOffset8";
    case FxBoolVariant::Count: break;
    }
    return "PcBool?";
}
// The bShowMouseCursor layouts the cursor probe must accept, skip to a valid neighbour, or refuse. Deltas are relative
// to FBOOLPROP_FIELDSIZE; Wirbel reads +0, -4, +4, +8, -8 in that order.
static std::vector<FxBoolLayout> FxBoolVariantLayout(FxBoolVariant v) {
    switch (v) {
    case FxBoolVariant::At80: return { { 8, 1, 0, kCursorBit, kCursorBit } };
    case FxBoolVariant::FieldSize4: return { { 0, 4, 0, kCursorBit, kCursorBit }, { 4, 1, 0, 0x04, 0x04 } };
    case FxBoolVariant::ByteMask3: return { { 0, 1, 0, 0x03, kCursorBit }, { 4, 1, 0, 0x04, 0x04 } };
    case FxBoolVariant::ByteOffset1: return { { 0, 1, 1, 0x04, 0x04 } };
    case FxBoolVariant::ByteMask8: return { { 0, 1, 0, 0x08, 0x04 } };
    case FxBoolVariant::Native: return { { 0, 1, 0, kNativeBoolByteMask, kNativeBoolFieldMask } };
    case FxBoolVariant::ByteOffset8: return { { 0, 1, 8, kCursorBit, kCursorBit } };
    case FxBoolVariant::Count: break;
    }
    return {};
}
static uintptr_t FxPcBoolClass(FxBoolVariant v) { return g_cPcBool[static_cast<int>(v)]; }

static void FxBuildStructs() {
    g_sVector = FxTripleStruct("Vector", kDoubleProperty, kVecD / 3, "X", "Y", "Z");
    g_sVectorF = FxTripleStruct("VectorF", kFloatProperty, kVecF / 3, "X", "Y", "Z");
    g_sRotator = FxTripleStruct("Rotator", kDoubleProperty, kVecD / 3, "Pitch", "Yaw", "Roll");
    g_sRotatorF = FxTripleStruct("RotatorF", kFloatProperty, kVecF / 3, "Pitch", "Yaw", "Roll");
    g_sVector2D = FxPairStruct("Vector2D", kDoubleProperty, 8);
    g_sVector2DF = FxPairStruct("Vector2DF", kFloatProperty, 4);
    g_sHandle = FxStruct("ActorInstanceHandle", FxHit5::kHandleBytes, 0);
    g_sHit5 = FxHitResult5Struct("HitResult5", true, true);
    g_sHit5NoImpact = FxHitResult5Struct("HitResult5NoImpact", false, true);
    g_sHit5NoPoint = FxHitResult5Struct("HitResult5NoPoint", false, false);
    g_sHit4 = FxHitResult4Struct();
    g_sViewTarget = FxStruct("TViewTarget", kViewTargetBytes, FxObjF("Target", 0));
    using namespace FxOff;
    g_sMvi = FxStruct("MinimalViewInfo", kMviBytes,
                      FxChainFields({ FxStructF("Location", 0, kVecD, g_sVector),
                                      FxStructF("Rotation", kPovRot, kVecD, g_sRotator),
                                      FxScalarF(kFloatProperty, "FOV", kPovFov, 4) }));
    g_sMviF = FxStruct("MinimalViewInfoF", kMviBytesF,
                       FxChainFields({ FxStructF("Location", 0, kVecF, g_sVectorF),
                                       FxStructF("Rotation", kPovRotF, kVecF, g_sRotatorF),
                                       FxScalarF(kFloatProperty, "FOV", kPovFovF, 4) }));
    g_sMviNoRot = FxStruct("MinimalViewInfoNoRot", kMviBytes,
                           FxChainFields({ FxStructF("Location", 0, kVecD, g_sVector),
                                           FxScalarF(kFloatProperty, "FOV", kPovFov, 4) }));
    g_sMviFovD = FxStruct("MinimalViewInfoFovD", kMviBytes,
                          FxChainFields({ FxStructF("Location", 0, kVecD, g_sVector),
                                          FxStructF("Rotation", kPovRot, kVecD, g_sRotator),
                                          FxScalarF(kDoubleProperty, "FOV", kPovFov, 8) }));
    auto cache = [](const char* name, int32_t size, uintptr_t mvi, int32_t mviBytes) {
        return FxStruct(name, size, FxChainFields({ FxScalarF(kFloatProperty, "Timestamp", 0, 4),
                                                    FxStructF("POV", kPov, mviBytes, mvi) }));
    };
    g_sCache = cache("CameraCacheEntry", kCacheBytes, g_sMvi, kMviBytes);
    g_sCacheF = cache("CameraCacheEntryF", kCacheBytesF, g_sMviF, kMviBytesF);
    g_sCacheNoRot = cache("CameraCacheEntryNoRot", kCacheBytes, g_sMviNoRot, kMviBytes);
    g_sCacheFovD = cache("CameraCacheEntryFovD", kCacheBytes, g_sMviFovD, kMviBytes);
    // Two more structs with CameraCacheEntry's own members, under names the struct probe must judge: one in CJK it
    // must accept, one empty it must pass over.
    const uintptr_t cacheMembers = FxGetPtr(g_sCache, DynOff::USTRUCT_CHILDPROPS);
    g_sCacheCjk = FxStructIdx(FxFNameWide(kCjkCacheName, "CameraCacheEntry"), kCacheBytes, cacheMembers);
    g_sEmptyName = FxStructIdx(FxFName(""), kCacheBytes, cacheMembers);
}

static void FxBuildClasses() {
    using namespace FxOff;
    g_cObject = FxClass("Object", 0, kUObjectHeaderBytes);
    g_cActor = FxClass("Actor", g_cObject, kRootComponent + kPtrBytes);
    FxSetProps(g_cActor, FxObjF("RootComponent", kRootComponent));

    // ── controllers ──
    g_cController = FxClass("Controller", g_cActor, kControlRotation + kVecD);
    FxSetProps(g_cController, FxChainFields({ FxObjF("Pawn", kPawn), FxCtrlRotField(kVecD) }));
    g_cPlayerController = FxClass("PlayerController", g_cController, kPcBytes);
    FxSetProps(g_cPlayerController, FxPcOwnFields(true, kCursorPacked));
    g_cGameCtrl = FxClass("BP_GameCtrl_C", g_cPlayerController, kPcBytes);   // named without "PlayerController"
    g_cDcc = FxClass("DebugCameraController", g_cPlayerController, kDccBytes);
    FxSetProps(g_cDcc, FxObjF("OriginalControllerRef", kOriginalController));
    g_cBpDebugCam = FxClass("BP_DebugCam_C", g_cDcc, kDccBytes);
    // A second DebugCameraController (another package) whose original-controller field only CONTAINS the name, and
    // which reflects no pawn at all.
    g_cDccFuzzy = FxClass("DebugCameraController", g_cActor, kDccBytes, g_pkgOther);
    FxSetProps(g_cDccFuzzy, FxObjF("OriginalControllerPtr", kOriginalController));
    g_cBpDebugCamFuzzy = FxClass("BP_DebugCamFuzzy_C", g_cDccFuzzy, kDccBytes);
    // The decoy: the word in its name, a PlayerController by derivation, never a debug camera.
    g_cDccHud = FxClass("DebugCameraControllerHUD_C", g_cPlayerController, kDccBytes);
    FxSetProps(g_cDccHud, FxObjF("OriginalControllerRef", kOriginalController));
    // Only AcknowledgedPawn names a pawn exactly; PawnClassOverride, first by offset, contains the word.
    g_cPcAckOnly = FxClass("BP_PcAckOnly_C", g_cActor, kPcBytes);
    FxSetProps(g_cPcAckOnly, FxChainFields({ FxObjF("PawnClassOverride", kPawn), FxCtrlRotField(kVecD),
                                             FxPcOwnFields(true, kCursorPacked) }));
    g_cPcNoPawn = FxClass("PlayerControllerNoPawn", g_cActor, kPcBytes);
    FxSetProps(g_cPcNoPawn, FxObjF("PlayerCameraManager", kCameraManager));
    g_cPcNoRot = FxClass("PcNoRot", g_cActor, kPcBytes);
    FxSetProps(g_cPcNoRot, FxChainFields({ FxObjF("Pawn", kPawn), FxPcOwnFields(true, kCursorPacked) }));
    g_cPcNoCursor = FxClass("PcNoCursor", g_cController, kPcBytes);
    FxSetProps(g_cPcNoCursor, FxPcOwnFields(false, {}));
    for (int v = 0; v < kBoolVariants; ++v) {
        const auto variant = static_cast<FxBoolVariant>(v);
        g_cPcBool[v] = FxClass(FxBoolVariantName(variant), g_cController, kPcBytes);
        FxSetProps(g_cPcBool[v], FxPcOwnFields(true, FxBoolVariantLayout(variant)));
    }
    // Function walks up a class's supers must stop at one whose super is itself, and at one they cannot read.
    g_cPcSelfSuper = FxClass("PcSelfSuper", 0, kPcBytes);
    FxSetPtr(g_cPcSelfSuper, DynOff::USTRUCT_SUPER, g_cPcSelfSuper);
    FxSetProps(g_cPcSelfSuper,
               FxChainFields({ FxObjF("Pawn", kPawn), FxCtrlRotField(kVecD), FxPcOwnFields(true, kCursorPacked) }));
    g_cPcBadSuper = FxClass("PcBadSuper", g_unreadablePage, kPcBytes);
    FxSetProps(g_cPcBadSuper,
               FxChainFields({ FxObjF("Pawn", kPawn), FxCtrlRotField(kVecD), FxPcOwnFields(true, kCursorPacked) }));

    // ── pawns ──
    g_cPawn = FxClass("Pawn", g_cActor, kPawnController + kPtrBytes);
    FxSetProps(g_cPawn, FxObjF("Controller", kPawnController));
    g_cCharacter = FxClass("Character", g_cPawn, kMesh + kPtrBytes);
    FxSetProps(g_cCharacter, FxChainFields({ FxObjF("CharacterMovement", kCharacterMovement), FxObjF("Mesh", kMesh) }));
    g_cHero = FxClass("BP_Hero_C", g_cCharacter, kMesh + kPtrBytes);   // AActor's K2 functions sit 3 supers up
    g_cNpc = FxClass("BP_Npc_C", g_cCharacter, kMesh + kPtrBytes);
    g_cEnemy = FxClass("BP_Enemy_C", g_cPawn, kPawnController + kPtrBytes);
    g_cSpectator = FxClass("SpectatorPawn", g_cPawn, kPawnController + kPtrBytes);
    g_cProp = FxClass("BP_Prop_C", g_cActor, kPropMesh + kPtrBytes);
    FxSetProps(g_cProp, FxObjF("Mesh", kPropMesh));

    // ── components ──
    g_cActorComponent = FxClass("ActorComponent", g_cObject, kUObjectHeaderBytes);
    g_cSceneComponent = FxClass("SceneComponent", g_cActorComponent, kSceneBytes);
    FxSetProps(g_cSceneComponent, FxChainFields({ FxObjF("AttachParent", kAttachParent),
                                                  FxStructF("RelativeLocation", kRelativeLocation, kVecD, g_sVector) }));
    g_cPrimitive = FxClass("PrimitiveComponent", g_cSceneComponent, kSceneBytes);
    g_cCapsule = FxClass("CapsuleComponent", g_cPrimitive, kSceneBytes);
    g_cSkeletalMesh = FxClass("SkeletalMeshComponent", g_cPrimitive, kSceneBytes);
    g_cRootNoRelLoc = FxClass("RootNoRelLoc", g_cActorComponent, kSceneBytes);
    FxSetProps(g_cRootNoRelLoc,
               FxChainFields({ FxObjF("AttachParent", kAttachParent),
                               FxStructF("ComponentLocationCache", kComponentLocationCache, kVecD, g_sVector) }));

    // ── movement ──
    g_cMoveComp = FxClass("MovementComponent", g_cActorComponent, kVelocity + kVecD);
    FxSetProps(g_cMoveComp, FxStructF("Velocity", kVelocity, kVecD, g_sVector));
    g_cCmc = FxClass("CharacterMovementComponent", g_cMoveComp, kCmcBytes);
    FxSetProps(g_cCmc, FxCmcOwnFields(kVecD, false));
    g_cCmcNoVel = FxClass("CmcNoVel", g_cActorComponent, kAcceleration + kVecD);
    FxSetProps(g_cCmcNoVel, FxStructF("Acceleration", kAcceleration, kVecD, g_sVector));
    g_cCmcFuzzy = FxClass("CmcFuzzy", g_cActorComponent, kLastVelocity + kVecD);
    FxSetProps(g_cCmcFuzzy, FxChainFields({
                                FxObjF("WalkSpeedCurve", kWalkSpeedCurve),
                                FxScalarF(kFloatProperty, "BaseWalkSpeed", kBaseWalkSpeed, 4),
                                FxScalarF(kFloatProperty, "GravityScale", kFuzzyGravity, 4),
                                FxScalarF(kFloatProperty, "JumpZVelocity", kFuzzyJump, 4),
                                FxScalarF(kByteProperty, "CustomMovementMode", kFuzzyCustomMode, 1),
                                FxScalarF(kByteProperty, "GroundMovementMode", kGroundMovementMode, 1),
                                FxObjF("VelocityCurve", kVelocityCurve),
                                FxStructF("LastVelocity", kLastVelocity, kVecD, g_sVector),
                            }));

    // ── camera managers: the default, and one class per shape of the cached POV the raw read must judge ──
    g_cPcm = FxClass("PlayerCameraManager", g_cActor, kCamBytes);
    FxSetProps(g_cPcm, FxCamFields(true, FxCacheField(g_sCache)));
    g_cBpCam = FxClass("BP_CameraManager_C", g_cPcm, kCamBytes);
    g_cCamNoCache = FxClass("CamNoCache", g_cActor, kCamBytes);
    FxSetProps(g_cCamNoCache, FxCamFields(true, 0));
    g_cCamFuzzy = FxClass("CamFuzzy", g_cActor, kCamBytes);
    FxSetProps(g_cCamFuzzy, FxCamFields(true, FxField(kArrayProperty, "CameraCacheHistory", kCacheHistory,
                                                      kTArrayBytes, 0),
                                        FxStructF("CameraCache", kCameraCache, kCacheBytes, g_sCache)));
    g_cCamNoViewTarget = FxClass("CamNoViewTarget", g_cActor, kCamBytes);
    FxSetProps(g_cCamNoViewTarget, FxCamFields(false, FxCacheField(g_sCache)));
    g_cCamProbe8 = FxClass("CamProbe8", g_cActor, kCamBytes);
    FxSetProps(g_cCamProbe8, FxCamFields(true, FxCacheFieldProbed(0)));
    g_cCamProbeClass = FxClass("CamProbeClass", g_cActor, kCamBytes);
    FxSetProps(g_cCamProbeClass, FxCamFields(true, FxCacheFieldProbed(g_cActor)));
    g_cCamCjk = FxClass("CamCjk", g_cActor, kCamBytes);
    FxSetProps(g_cCamCjk, FxCamFields(true, FxCacheField(g_sCacheCjk)));
    g_cCamEmptyName = FxClass("CamEmptyName", g_cActor, kCamBytes);
    FxSetProps(g_cCamEmptyName, FxCamFields(true, FxCacheFieldProbed(g_sEmptyName)));
    g_cCamCacheNotStruct = FxClass("CamCacheNotStruct", g_cActor, kCamBytes);
    {
        // An IntProperty named CameraCachePrivate whose subclass slot nonetheless holds the cache struct.
        const uintptr_t f = FxScalarF(kIntProperty, "CameraCachePrivate", kCameraCache, 4);
        FxPut64(f, DynOff::FSTRUCTPROP_STRUCT, g_sCache);
        FxSetProps(g_cCamCacheNotStruct, FxCamFields(true, f));
    }
    g_cCamNoRot = FxClass("CamNoRot", g_cActor, kCamBytes);
    FxSetProps(g_cCamNoRot, FxCamFields(true, FxCacheField(g_sCacheNoRot)));
    g_cCamFovDouble = FxClass("CamFovDouble", g_cActor, kCamBytes);
    FxSetProps(g_cCamFovDouble, FxCamFields(true, FxCacheField(g_sCacheFovD)));
    g_cCamFovDoubleGetter = FxClass("CamFovDoubleGetter", g_cActor, kCamBytes);
    FxSetProps(g_cCamFovDoubleGetter, FxCamFields(true, FxCacheField(g_sCache)));
    g_cCamLocScalar = FxClass("CamLocScalar", g_cActor, kCamBytes);
    FxSetProps(g_cCamLocScalar, FxCamFields(true, FxCacheField(g_sCache)));

    // ── libraries, each behind a decoy whose name contains it and which does not derive from it ──
    g_cBpfl = FxClass("BlueprintFunctionLibrary", g_cObject, kUObjectHeaderBytes);
    g_cKsl = FxClass("KismetSystemLibrary", g_cBpfl, kUObjectHeaderBytes);
    g_cKslEx = FxClass("KismetSystemLibraryEx", g_cObject, kUObjectHeaderBytes);
    g_cWbl = FxClass("WidgetBlueprintLibrary", g_cBpfl, kUObjectHeaderBytes);
    g_cWblEx = FxClass("WidgetBlueprintLibraryEx", g_cObject, kUObjectHeaderBytes);

    // ── the world and its players ──
    g_cWorld = FxClass("World", g_cObject, kOwningGameInstance + kPtrBytes);
    FxSetProps(g_cWorld, FxObjF("OwningGameInstance", kOwningGameInstance));
    g_cWorldFuzzy = FxClass("WorldFuzzy", g_cObject, kMyGameInstance + kPtrBytes);
    FxSetProps(g_cWorldFuzzy, FxChainFields({ FxField(kArrayProperty, "GameInstanceSubsystems", kSubsystems,
                                                      kTArrayBytes, 0),
                                              FxObjF("MyGameInstance", kMyGameInstance) }));
    g_cWorldBare = FxClass("WorldBare", g_cObject, kUObjectHeaderBytes);
    g_cGameInstance = FxClass("GameInstance", g_cObject, kLocalPlayers + kTArrayBytes);
    FxSetProps(g_cGameInstance, FxField(kArrayProperty, "LocalPlayers", kLocalPlayers, kTArrayBytes, 0));
    g_cGameInstanceBare = FxClass("GameInstanceBare", g_cObject, kUObjectHeaderBytes);
    g_cPlayer = FxClass("Player", g_cObject, kUObjectHeaderBytes);
    g_cLocalPlayer = FxClass("LocalPlayer", g_cPlayer, kPlayerController + kPtrBytes);
    FxSetProps(g_cLocalPlayer, FxObjF("PlayerController", kPlayerController));
    // A LocalPlayer by name (another package) that reflects no PlayerController: the chain breaks there, the scan
    // still ranks a controller holding it as local.
    g_cLocalPlayerBare = FxClass("LocalPlayer", g_cPlayer, kUObjectHeaderBytes, g_pkgOther);
    g_cNetConnection = FxClass("NetConnection", g_cPlayer, kUObjectHeaderBytes);
    g_cUserWidget = FxClass("UserWidget", g_cObject, kUObjectHeaderBytes);
    g_cWidgetHud = FxClass("WBP_PlayerControllerHUD_C", g_cUserWidget, kWidgetPlayer + kPtrBytes);
    FxSetProps(g_cWidgetHud, FxObjF("Player", kWidgetPlayer));

    // ── the UE4 (float) twins ──
    g_cActorF = FxClass("ActorF", g_cObject, kRootComponent + kPtrBytes);
    FxSetProps(g_cActorF, FxObjF("RootComponent", kRootComponent));
    g_cCharacterF = FxClass("CharacterF", g_cActorF, kMesh + kPtrBytes);
    FxSetProps(g_cCharacterF, FxChainFields({ FxObjF("Controller", kPawnController),
                                              FxObjF("CharacterMovement", kCharacterMovement), FxObjF("Mesh", kMesh) }));
    g_cHeroF = FxClass("BP_HeroF_C", g_cCharacterF, kMesh + kPtrBytes);
    g_cControllerF = FxClass("ControllerF", g_cActor, kControlRotation + kVecF);
    FxSetProps(g_cControllerF, FxChainFields({ FxObjF("Pawn", kPawn), FxCtrlRotField(kVecF) }));
    g_cPlayerControllerF = FxClass("PlayerControllerF", g_cControllerF, kPcBytes);
    FxSetProps(g_cPlayerControllerF, FxPcOwnFields(true, kCursorPacked));
    g_cGameCtrlF = FxClass("BP_GameCtrlF_C", g_cPlayerControllerF, kPcBytes);
    g_cSceneCompF = FxClass("SceneComponentF", g_cActorComponent, kSceneBytes);
    FxSetProps(g_cSceneCompF, FxChainFields({ FxObjF("AttachParent", kAttachParent),
                                              FxStructF("RelativeLocation", kRelativeLocation, kVecF, g_sVectorF) }));
    g_cCapsuleF = FxClass("CapsuleComponentF", g_cSceneCompF, kSceneBytes);
    g_cCmcF = FxClass("CharacterMovementComponentF", g_cActorComponent, kCmcBytes);
    FxSetProps(g_cCmcF, FxCmcOwnFields(kVecF, true));
    g_cPcmF = FxClass("PlayerCameraManagerF", g_cActor, FxOff::kCameraCache + kCacheBytesF);
    FxSetProps(g_cPcmF, FxCamFields(true, FxCacheField(g_sCacheF, kCacheBytesF)));
}

static void FxBuildFunctions() {
    // ── the default lists ──
    g_hdActor = FxActorFnList({});
    g_fnGetLoc = FxFnIn(g_hdActor, "K2_GetActorLocation");
    g_fnForward = FxFnIn(g_hdActor, "GetActorForwardVector");
    g_fnSetActorLoc = FxFnIn(g_hdActor, "K2_SetActorLocation");
    g_fnTeleportTo = FxFnIn(g_hdActor, "K2_TeleportTo");
    FxSetFns(g_cActor, g_hdActor);
    g_hdCtrl = FxCtrlFnList({});
    g_fnSetCtrlRot = FxFnIn(g_hdCtrl, "SetControlRotation");
    g_fnStopMove = FxFnIn(g_hdCtrl, "StopMovement");
    FxSetFns(g_cController, g_hdCtrl);
    g_hdPc = FxPcFnList({});
    g_fnMousePos = FxFnIn(g_hdPc, "GetMousePosition");
    g_fnViewport = FxFnIn(g_hdPc, "GetViewportSize");
    g_fnHitCursor = FxFnIn(g_hdPc, "GetHitResultUnderCursorByChannel");
    g_fnHitScreen = FxFnIn(g_hdPc, "GetHitResultAtScreenPosition");
    g_fnDeproject = FxFnIn(g_hdPc, "DeprojectScreenPositionToWorld");
    FxSetFns(g_cPlayerController, g_hdPc);
    g_hdScene = FxSceneFnList({});
    g_fnSetWorldLoc = FxFnIn(g_hdScene, "K2_SetWorldLocation");
    g_fnSetRelLoc = FxFnIn(g_hdScene, "K2_SetRelativeLocation");
    FxSetFns(g_cSceneComponent, g_hdScene);
    g_hdMoveComp = FxMoveCompFnList(true);
    g_fnStopMoveNow = FxFnIn(g_hdMoveComp, "StopMovementImmediately");
    FxSetFns(g_cMoveComp, g_hdMoveComp);
    g_hdCmc = FxCmcFnList(true);
    g_fnSetMoveMode = FxFnIn(g_hdCmc, "SetMovementMode");
    FxSetFns(g_cCmc, g_hdCmc);
    g_hdCam = FxCamFnList({});
    g_fnCamLoc = FxFnIn(g_hdCam, "GetCameraLocation");
    g_fnCamRot = FxFnIn(g_hdCam, "GetCameraRotation");
    g_fnFov = FxFnIn(g_hdCam, "GetFOVAngle");
    FxSetFns(g_cPcm, g_hdCam);

    // ── the variant classes carry the lists their shape needs ──
    FxSetFns(g_cPcNoRot, FxCtrlFnList({ .setRot = false }));
    const uintptr_t mouseOnly = FxPcFnList({ .viewport = false,
                                             .cursor = FxHitShape::Missing,
                                             .screen = FxHitShape::Missing,
                                             .deproject = FxDeprojShape::Missing });
    FxSetFns(g_cPcSelfSuper, mouseOnly);
    FxSetFns(g_cPcBadSuper, mouseOnly);
    for (uintptr_t c : { g_cCamNoCache, g_cCamFuzzy, g_cCamNoViewTarget, g_cCamProbe8, g_cCamProbeClass, g_cCamCjk,
                         g_cCamEmptyName, g_cCamCacheNotStruct, g_cCamNoRot, g_cCamFovDouble })
        FxSetFns(c, g_hdCam);
    FxSetFns(g_cCamFovDoubleGetter, FxCamFnList({ .fov = FxFovShape::Double }));
    FxSetFns(g_cCamLocScalar, FxCamFnList({ .loc = FxCamLocShape::Scalar }));

    // ── the UE4 twins' lists ──
    g_hdActorF = FxActorFnList({ .ue4 = true });
    g_fnGetLocF = FxFnIn(g_hdActorF, "K2_GetActorLocation");
    g_fnForwardF = FxFnIn(g_hdActorF, "GetActorForwardVector");
    g_fnSetActorLocF = FxFnIn(g_hdActorF, "K2_SetActorLocation");
    g_fnTeleportToF = FxFnIn(g_hdActorF, "K2_TeleportTo");
    FxSetFns(g_cActorF, g_hdActorF);
    g_hdCtrlF = FxCtrlFnList({ .ue4 = true });
    g_fnSetCtrlRotF = FxFnIn(g_hdCtrlF, "SetControlRotation");
    FxSetFns(g_cControllerF, g_hdCtrlF);
    g_hdPcF = FxPcFnList({ .ue4 = true });
    g_fnMousePosF = FxFnIn(g_hdPcF, "GetMousePosition");
    g_fnHitCursorF = FxFnIn(g_hdPcF, "GetHitResultUnderCursorByChannel");
    g_fnHitScreenF = FxFnIn(g_hdPcF, "GetHitResultAtScreenPosition");
    g_fnDeprojectF = FxFnIn(g_hdPcF, "DeprojectScreenPositionToWorld");
    FxSetFns(g_cPlayerControllerF, g_hdPcF);
    g_hdSceneF = FxSceneFnList({ .ue4 = true });
    g_fnSetWorldLocF = FxFnIn(g_hdSceneF, "K2_SetWorldLocation");
    g_fnSetRelLocF = FxFnIn(g_hdSceneF, "K2_SetRelativeLocation");
    FxSetFns(g_cSceneCompF, g_hdSceneF);
    g_hdCmcF = FxCmcFFnList();
    g_fnSetMoveModeF = FxFnIn(g_hdCmcF, "SetMovementMode");
    g_fnStopMoveNowF = FxFnIn(g_hdCmcF, "StopMovementImmediately");
    FxSetFns(g_cCmcF, g_hdCmcF);
    g_hdCamF = FxCamFnList({ .ue4 = true });
    g_fnCamLocF = FxFnIn(g_hdCamF, "GetCameraLocation");
    g_fnCamRotF = FxFnIn(g_hdCamF, "GetCameraRotation");
    g_fnFovF = FxFnIn(g_hdCamF, "GetFOVAngle");
    FxSetFns(g_cPcmF, g_hdCamF);

    // ── KismetSystemLibrary: LineTraceSingle behind its ForObjects / ByProfile siblings, as UE links them ──
    const char* const kLineTraceSingle = "LineTraceSingle";
    g_ltsD = FxFunc(kLineTraceSingle, g_cKsl, FxLtsDChain(g_sHit5), FxLts::kNumParms, FxLts::kParmsSize,
                    FxLts::kReturn);
    g_ltsNoImpact = FxFunc(kLineTraceSingle, g_cKsl, FxLtsDChain(g_sHit5NoImpact), FxLts::kNumParms, FxLts::kParmsSize,
                           FxLts::kReturn);
    g_ltsNoPoint = FxFunc(kLineTraceSingle, g_cKsl, FxLtsDChain(g_sHit5NoPoint), FxLts::kNumParms, FxLts::kParmsSize,
                          FxLts::kReturn);
    g_ltsNoOutHit = FxFunc(kLineTraceSingle, g_cKsl, FxLtsDChain(0), FxLts::kNumParms - 1, FxLts::kParmsSize,
                           FxLts::kReturn);
    g_ltsF = FxFunc(kLineTraceSingle, g_cKsl, FxLtsFChain(), FxLts::kNumParms, FxLtsF::kParmsSize, FxLtsF::kReturn);
    FxSpecTrace(g_ltsD, false, g_sHit5);
    FxSpecTrace(g_ltsNoImpact, false, g_sHit5NoImpact);
    FxSpecTrace(g_ltsNoPoint, false, g_sHit5NoPoint);
    FxSpecTrace(g_ltsNoOutHit, false, 0);
    FxSpecTrace(g_ltsF, true, g_sHit4);
    g_fnLtsMulti = FxFunc("LineTraceMulti", g_cKsl, 0, 0, 0, kNoReturn);
    {
        // The engine answers neither sibling (FxKind::Other): an ObjectTypes array left empty, or no profile, finds
        // nothing.
        int32_t retP = 0, parmsP = 0, retO = 0, parmsO = 0;
        const uintptr_t chainP = FxSiblingChain(kNameProperty, "ProfileName", kPtrBytes, retP, parmsP);
        const uintptr_t chainO = FxSiblingChain(kArrayProperty, "ObjectTypes", kTArrayBytes, retO, parmsO);
        g_fnLtsByProfile = FxFunc("LineTraceSingleByProfile", g_cKsl, chainP, FxLts::kNumParms, parmsP, retP, g_ltsD);
        g_fnLtsForObjects = FxFunc("LineTraceSingleForObjects", g_cKsl, chainO, FxLts::kNumParms, parmsO, retO,
                                   g_fnLtsByProfile);
    }
    g_hdKsl = g_fnLtsForObjects;
    FxSetFns(g_cKsl, g_hdKsl);
    g_ltsDecoy = FxFunc(kLineTraceSingle, g_cKslEx, FxLtsDChain(g_sHit5), FxLts::kNumParms, FxLts::kParmsSize,
                        FxLts::kReturn);
    FxSpecTrace(g_ltsDecoy, false, g_sHit5);
    FxSetFns(g_cKslEx, g_ltsDecoy);

    // ── WidgetBlueprintLibrary, and its decoy carrying the same two names ──
    g_hdWbl = FxWblFnList({});
    g_fnGameOnly = FxFnIn(g_hdWbl, "SetInputMode_GameOnly");
    g_fnGameAndUiEx = FxFnIn(g_hdWbl, "SetInputMode_GameAndUIEx");
    FxSetFns(g_cWbl, g_hdWbl);
    FxSetFns(g_cWblEx, FxWblFnList({ .decoy = true }));
}

// ══ the fixture: the object array ════════════════════════════════════════════════════════════════════════════════
// GObjects order matters: every lookup that a substring, a template or a first-match rule would answer wrongly meets
// the wrong object first, and the local controller comes last of the controllers.
constexpr int32_t kIdxCdoPc = 0, kIdxWidget = 1, kIdxDbgIdle = 2, kIdxPcNoPlayer = 3, kIdxPcRemote = 4, kIdxPc0 = 5,
                  kIdxCdoWblEx = 6, kIdxWbl = 7, kIdxCdoWbl = 8, kIdxCdoKslEx = 9, kIdxKsl = 10, kIdxCdoKsl = 11,
                  kIdxLp0 = 12, kIdxNetConn = 13, kIdxDbgPc0 = 14, kPoolSlots = 15;
constexpr int kItemSize = 24;           // the FUObjectItem stride InitWithExtendedLayout is told to expect
constexpr int kItemIndexAt = 12;        // InternalIndex within an item
namespace FxGObj {                      // the UE5 chunked FUObjectArray header
constexpr int kObjects = 0x10, kMaxElements = 0x20, kNumElements = 0x24, kMaxChunks = 0x28, kNumChunks = 0x2C,
              kBytes = 0x40;
}  // namespace FxGObj
static uint8_t g_poolItems[Grimoire::OBJECTS_PER_CHUNK * kItemSize];   // one whole chunk, as Aura reads one
static uintptr_t g_poolChunkTable[1];
alignas(16) static uint8_t g_poolHeader[FxGObj::kBytes];
static uintptr_t g_poolDefault[kPoolSlots];

enum class FxPoolVariant {
    Default,
    NoPc0,           // slot 5 empty: the scan's best is the remote controller
    NoPc0NoRemote,   // slots 4 and 5 empty: the scan's best has no Player
    IdleOnly,        // slots 3-5 empty: nothing but templates and idle debug cameras
    ActiveDcc,       // DbgPC_0 holds LP_0 and PC_0 holds none: the scan takes the debug camera, the hop the original
    NoWbl,           // slots 6-8 empty: no WidgetBlueprintLibrary at all
    KslWithoutCdo,   // slot 11 empty: nothing derives from KismetSystemLibrary
    Uprop,           // PCU_0 is the only controller (the UProperty mini-world)
};

static void FxPoolPut(int32_t slot, uintptr_t obj) {
    uint8_t* item = g_poolItems + static_cast<size_t>(slot) * kItemSize;
    memcpy(item, &obj, sizeof obj);
    memcpy(item + kItemIndexAt, &slot, sizeof slot);
}
static uintptr_t FxSlot(int32_t slot) {
    uintptr_t obj = 0;
    memcpy(&obj, g_poolItems + static_cast<size_t>(slot) * kItemSize, sizeof obj);
    return obj;
}
static void FxPoolPublish() {
    g_poolChunkTable[0] = FxAddr(g_poolItems);
    const uintptr_t table = FxAddr(g_poolChunkTable);
    const int32_t count = kPoolSlots, chunks = 1;
    memcpy(g_poolHeader + FxGObj::kObjects, &table, sizeof table);
    memcpy(g_poolHeader + FxGObj::kMaxElements, &count, sizeof count);
    memcpy(g_poolHeader + FxGObj::kNumElements, &count, sizeof count);
    memcpy(g_poolHeader + FxGObj::kMaxChunks, &chunks, sizeof chunks);
    memcpy(g_poolHeader + FxGObj::kNumChunks, &chunks, sizeof chunks);
    Aura::InitWithExtendedLayout(FxAddr(g_poolHeader), kItemSize);
}
static void FxPool(FxPoolVariant v) {
    // ActiveDcc's two field writes, undone first so every other variant starts from the default controllers.
    FxSetPtr(g_dbgPc0, FxOff::kPlayer, 0);
    FxSetPtr(g_pc0, FxOff::kPlayer, g_lp0);
    for (int32_t i = 0; i < kPoolSlots; ++i) FxPoolPut(i, g_poolDefault[i]);
    switch (v) {
    case FxPoolVariant::Default:
        break;
    case FxPoolVariant::NoPc0:
        FxPoolPut(kIdxPc0, 0);
        break;
    case FxPoolVariant::NoPc0NoRemote:
        FxPoolPut(kIdxPc0, 0);
        FxPoolPut(kIdxPcRemote, 0);
        break;
    case FxPoolVariant::IdleOnly:
        FxPoolPut(kIdxPcNoPlayer, 0);
        FxPoolPut(kIdxPcRemote, 0);
        FxPoolPut(kIdxPc0, 0);
        break;
    case FxPoolVariant::ActiveDcc:
        FxSetPtr(g_dbgPc0, FxOff::kPlayer, g_lp0);
        FxSetPtr(g_pc0, FxOff::kPlayer, 0);
        break;
    case FxPoolVariant::NoWbl:
        FxPoolPut(kIdxCdoWblEx, 0);
        FxPoolPut(kIdxWbl, 0);
        FxPoolPut(kIdxCdoWbl, 0);
        break;
    case FxPoolVariant::KslWithoutCdo:
        FxPoolPut(kIdxCdoKsl, 0);
        break;
    case FxPoolVariant::Uprop:
        for (int32_t i = 0; i < kPoolSlots; ++i) FxPoolPut(i, 0);
        FxPoolPut(kIdxPc0, g_pcU0);
        FxPoolPut(kIdxLp0, g_lp0);
        FxPoolPut(kIdxNetConn, g_netConn0);
        break;
    }
    FxPoolPublish();
}

// ══ the fixture: objects and their default state ═════════════════════════════════════════════════════════════════
static uintptr_t FxEdgeObj(FxEdge which, uintptr_t cls, const char* name, size_t bytes) {
    void* base = VirtualAlloc(nullptr, 2 * kPage, MEM_RESERVE, PAGE_NOACCESS);
    if (!base || !VirtualAlloc(base, kPage, MEM_COMMIT, PAGE_READWRITE))
        FxFixtureFail("an edge page could not be made");
    g_edgeBase[which] = base;
    const uintptr_t obj = FxAddr(base) + kPage - bytes;
    FxObjAt(obj, cls, name, 0);
    g_bodyBytes[obj] = bytes;
    return obj;
}

static void FxBuildObjects() {
    // ── the object array's own ──
    g_cdoPc = FxObj(g_cPlayerController, "Default__PlayerController", g_pkgEngine);
    g_widget0 = FxObj(g_cWidgetHud, "WBP_PlayerControllerHUD_C_0", 0);
    g_dbgIdle = FxObj(g_cBpDebugCam, "DbgPC_Idle", 0);
    g_pcNoPlayer = FxObj(g_cGameCtrl, "PC_NoPlayer", 0);
    g_pcRemote = FxObj(g_cGameCtrl, "PC_Remote", 0);
    g_pc0 = FxObjAt(FxAddr(g_pcHost) + kPcInHost, g_cGameCtrl, "PC_0", 0);
    g_bodyBytes[g_pc0] = kObjBytes;
    g_cdoWblEx = FxObj(g_cWblEx, "Default__WidgetBlueprintLibraryEx", g_pkgEngine);
    g_cdoWbl = FxObj(g_cWbl, "Default__WidgetBlueprintLibrary", g_pkgEngine);
    g_cdoKslEx = FxObj(g_cKslEx, "Default__KismetSystemLibraryEx", g_pkgEngine);
    g_cdoKsl = FxObj(g_cKsl, "Default__KismetSystemLibrary", g_pkgEngine);
    g_gi0 = FxObj(g_cGameInstance, "GI_0", 0);
    g_lp0 = FxObj(g_cLocalPlayer, "LP_0", g_gi0);
    g_netConn0 = FxObj(g_cNetConnection, "NetConnection_0", 0);
    g_dbgPc0 = FxObj(g_cBpDebugCam, "DbgPC_0", 0);

    // ── the world graph ──
    g_worldA = FxObj(g_cWorld, "Map_A", 0);
    g_worldB = FxObj(g_cWorld, "Map_B", 0);
    g_worldCase = FxObj(g_cWorld, "map_a", 0);
    g_worldFuzzy = FxObj(g_cWorldFuzzy, "Map_A", 0);
    g_worldLong = FxObj(g_cWorld, std::string(static_cast<size_t>(kLongNameChars), 'L').c_str(), 0);
    g_world20 = FxObj(g_cWorld, kName20, 0);
    g_worldEmpty = FxObj(g_cWorld, "", 0);
    g_worldBare = FxObj(g_cWorldBare, "Map_A", 0);
    g_giBare = FxObj(g_cGameInstanceBare, "GI_Bare", 0);
    g_lpBare = FxObj(g_cLocalPlayerBare, "LP_Bare", g_gi0);
    g_subsystems = FxNewObjBlob();   // GameInstanceSubsystems' data: one zeroed element
    g_dbgFuzzy0 = FxObj(g_cBpDebugCamFuzzy, "DbgFuzzy_0", 0);
    g_decoyPc0 = FxObj(g_cDccHud, "DecoyPC_0", 0);
    g_pcAckOnly0 = FxObj(g_cPcAckOnly, "PcAckOnly_0", 0);
    g_hero0 = FxObj(g_cHero, "BP_Hero_C_0", 0);
    g_npc0 = FxObj(g_cNpc, "BP_Npc_C_0", 0);
    g_enemy0 = FxObj(g_cEnemy, "BP_Enemy_C_0", 0);
    g_spectator0 = FxObj(g_cSpectator, "SpectatorPawn_0", 0);
    g_prop0 = FxObj(g_cProp, "BP_Prop_C_0", 0);
    g_root0 = FxObjBig(g_cCapsule, "Root_0", g_hero0);
    g_rootNpc = FxObjBig(g_cCapsule, "RootNpc_0", g_npc0);
    g_rootEnemy = FxObjBig(g_cCapsule, "RootEnemy_0", g_enemy0);
    g_rootSpec = FxObjBig(g_cCapsule, "RootSpec_0", g_spectator0);
    g_rootProp = FxObjBig(g_cCapsule, "RootProp_0", g_prop0);
    g_parent0 = FxObj(g_cSceneComponent, "Parent_0", 0);
    g_mesh0 = FxObj(g_cSkeletalMesh, "Mesh_0", g_hero0);
    g_cmc0 = FxObj(g_cCmc, "CMC_0", g_hero0);
    g_cmcNoVel0 = FxObj(g_cCmcNoVel, "CmcNoVel_0", g_hero0);
    g_cmcFuzzy0 = FxObj(g_cCmcFuzzy, "CmcFuzzy_0", g_hero0);
    g_cam0 = FxObjBig(g_cBpCam, "Cam_0", 0);
    g_camDbg0 = FxObjBig(g_cBpCam, "CamDbg_0", 0);

    // ── the UE4 twins ──
    g_pcF0 = FxObj(g_cGameCtrlF, "PCF_0", 0);
    g_heroF0 = FxObj(g_cHeroF, "BP_HeroF_C_0", 0);
    g_rootF0 = FxObjBig(g_cCapsuleF, "RootF_0", g_heroF0);
    g_cmcF0 = FxObj(g_cCmcF, "CMCF_0", g_heroF0);
    g_camF0 = FxObjBig(g_cPcmF, "CamF_0", 0);

    // ── the UProperty mini-world: a PlayerController whose properties are UObjects on its Children chain ──
    g_cUpObjectProperty = FxObj(g_metaClass, "ObjectProperty", g_pkgEngineU);
    g_cUpBoolProperty = FxObj(g_metaClass, "BoolProperty", g_pkgEngineU);
    g_upCursor = FxUProp(g_cUpBoolProperty, "bShowMouseCursor", FxOff::kShowCursor, 1, 0, 0);
    {
        uint8_t* p = FxBytes(g_upCursor) + DynOff::UBOOLPROP_FIELDSIZE + kUpropLayoutDelta;
        p[0] = 1;
        p[1] = 0;
        p[2] = kCursorBit;
        p[3] = kCursorBit;
    }
    g_upPlayer = FxUProp(g_cUpObjectProperty, "Player", FxOff::kPlayer, kPtrBytes, 0, g_upCursor);
    g_cPcU = FxClass("PlayerController", 0, kPcBytes, g_pkgEngineU);
    FxSetFns(g_cPcU, g_upPlayer);
    g_pcU0 = FxObj(g_cPcU, "PCU_0", 0);

    // ── the edge pages ──
    g_rootEdge = FxEdgeObj(kEdgeRoot, g_cCapsule, "RootEdge", kRootEdgeBytes);
    g_rootNearEdge = FxEdgeObj(kEdgeRootNear, g_cCapsule, "RootNearEdge", kRootNearEdgeBytes);
    g_camEdgeRot = FxEdgeObj(kEdgeCamRot, g_cBpCam, "CamEdgeRot", kCamEdgeRotBytes);
    g_camEdgeLoc = FxEdgeObj(kEdgeCamLoc, g_cBpCam, "CamEdgeLoc", kCamEdgeLocBytes);

    // ── the write-fail section: one object, a writable view and a read-only one ──
    g_wfSection = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, static_cast<DWORD>(kWfBytes),
                                     nullptr);
    if (!g_wfSection) FxFixtureFail("the write-fail section could not be made");
    g_wfRw = FxAddr(MapViewOfFile(g_wfSection, FILE_MAP_WRITE, 0, 0, kWfBytes));
    g_wfRo = FxAddr(MapViewOfFile(g_wfSection, FILE_MAP_READ, 0, 0, kWfBytes));
    if (!g_wfRw || !g_wfRo) FxFixtureFail("the write-fail section's views could not be mapped");

    g_poolDefault[kIdxCdoPc] = g_cdoPc;            // a template: a scan must pass over it
    g_poolDefault[kIdxWidget] = g_widget0;         // holds LP_0, but is no PlayerController
    g_poolDefault[kIdxDbgIdle] = g_dbgIdle;        // a debug camera with no Player: never taken
    g_poolDefault[kIdxPcNoPlayer] = g_pcNoPlayer;
    g_poolDefault[kIdxPcRemote] = g_pcRemote;
    g_poolDefault[kIdxPc0] = g_pc0;                // the local one, after every controller a first match would take
    g_poolDefault[kIdxCdoWblEx] = g_cdoWblEx;      // the decoy CDOs, before the real ones
    g_poolDefault[kIdxWbl] = g_cWbl;
    g_poolDefault[kIdxCdoWbl] = g_cdoWbl;
    g_poolDefault[kIdxCdoKslEx] = g_cdoKslEx;
    g_poolDefault[kIdxKsl] = g_cKsl;
    g_poolDefault[kIdxCdoKsl] = g_cdoKsl;
    g_poolDefault[kIdxLp0] = g_lp0;
    g_poolDefault[kIdxNetConn] = g_netConn0;
    g_poolDefault[kIdxDbgPc0] = g_dbgPc0;

    // The debug camera's own camera manager answers its own getters.
    std::lock_guard<std::mutex> lk(g_engMu);
    g_answerDefault[FxAnswerKey{ FxKind::CamLoc, g_camDbg0 }] = { kCamDbgLoc[0], kCamDbgLoc[1], kCamDbgLoc[2] };
    g_answerDefault[FxAnswerKey{ FxKind::CamRot, g_camDbg0 }] = { kCamDbgRot[0], kCamDbgRot[1], kCamDbgRot[2] };
}

// ── one object's default state ──
// rot nullptr leaves ControlRotation zero. The cursor byte holds only the sibling bit, so a write that flips anything
// but its own mask shows.
static void FxPlantPc(uintptr_t pc, uintptr_t cls, uintptr_t pawn, uintptr_t ack, uintptr_t cam, uintptr_t player,
                      const double* rot, int32_t rotWidth) {
    FxClearBody(pc);
    FxSetClass(pc, cls);
    FxSetPtr(pc, FxOff::kPawn, pawn);
    if (rot) FxSetVecAt(pc + FxOff::kControlRotation, rotWidth, rot);
    FxSetPtr(pc, FxOff::kAckPawn, ack);
    FxSetPtr(pc, FxOff::kCameraManager, cam);
    FxSetPtr(pc, FxOff::kPlayer, player);
    FxBytes(pc)[FxOff::kShowCursor] = kCursorSiblingBit;
}
static void FxPlantPawn(uintptr_t pawn, uintptr_t cls, uintptr_t root, uintptr_t controller, uintptr_t cmc,
                        uintptr_t mesh) {
    FxClearBody(pawn);
    FxSetClass(pawn, cls);
    FxSetPtr(pawn, FxOff::kRootComponent, root);
    FxSetPtr(pawn, FxOff::kPawnController, controller);
    FxSetPtr(pawn, FxOff::kCharacterMovement, cmc);
    FxSetPtr(pawn, FxOff::kMesh, mesh);
}
static void FxPlantRoot(uintptr_t root, uintptr_t cls, const double pos[3], int32_t width) {
    FxClearBody(root);
    FxSetClass(root, cls);
    FxSetPtr(root, FxOff::kAttachParent, 0);
    FxSetVecAt(root + FxOff::kRelativeLocation, width, pos);
}
// The cached POV a raw read takes: Location, Rotation, and a float FOV whose next 4 bytes stay zero.
static void FxPlantRawPov(uintptr_t cam, int32_t width) {
    const uintptr_t pov = cam + FxOff::kPovBase;
    const bool d = width >= kVecD;
    FxSetVecAt(pov, width, kRawLoc);
    FxSetVecAt(pov + (d ? FxOff::kPovRot : FxOff::kPovRotF), width, kRawRot);
    const float fov[2] = { static_cast<float>(kRawFov), 0.0f };
    memcpy(FxBytes(pov + (d ? FxOff::kPovFov : FxOff::kPovFovF)), fov, sizeof fov);
}
// CamFovDouble's cached FOV: a double where the float normally is.
static void FxPlantFovDouble(uintptr_t cam) {
    const double v = kRawFovDouble;
    memcpy(FxBytes(cam + FxOff::kPovBase + FxOff::kPovFov), &v, sizeof v);
}
static void FxPlantCam(uintptr_t cam, uintptr_t cls, uintptr_t target, uintptr_t pending, int32_t width) {
    FxClearBody(cam);
    FxSetClass(cam, cls);
    FxSetPtr(cam, FxOff::kPendingViewTarget, pending);
    FxSetPtr(cam, FxOff::kViewTarget, target);
    FxPlantRawPov(cam, width);
}
static void FxPlantCmc(uintptr_t cmc, uintptr_t cls, int32_t width) {
    FxClearBody(cmc);
    FxSetClass(cmc, cls);
    FxSetVecAt(cmc + FxOff::kVelocity, width, kVel);
    FxSetVecAt(cmc + FxOff::kAcceleration, width, kAcc);
    memcpy(FxBytes(cmc + FxOff::kMaxWalkSpeed), &kWalkSpeedValue, sizeof kWalkSpeedValue);
    memcpy(FxBytes(cmc + FxOff::kGravityScale), &kGravityValue, sizeof kGravityValue);
    memcpy(FxBytes(cmc + FxOff::kJumpZVelocity), &kJumpZValue, sizeof kJumpZValue);
    FxBytes(cmc)[FxOff::kMovementMode] = kModeWalking;
}

// Every world object back to its default state, every class's heads back, the default pool and GWorld -> Map_A. What a
// scenario wrote anywhere in an object it may touch is cleared with the object's whole body.
static void FxWorldDefault() {
    using namespace FxOff;
    DynOff::bUseFProperty = true;
    FxRestoreClasses();

    for (uintptr_t w : { g_worldA, g_worldB, g_worldCase, g_worldLong, g_world20, g_worldEmpty }) {
        FxClearBody(w);
        FxSetClass(w, g_cWorld);
        FxSetPtr(w, kOwningGameInstance, g_gi0);
    }
    FxClearBody(g_worldFuzzy);
    FxSetClass(g_worldFuzzy, g_cWorldFuzzy);
    FxPutTArray(g_worldFuzzy, kSubsystems, g_subsystems, 1, 1);
    FxSetPtr(g_worldFuzzy, kMyGameInstance, g_gi0);
    FxClearBody(g_worldBare);
    FxSetClass(g_worldBare, g_cWorldBare);
    FxClearBody(g_gi0);
    FxSetClass(g_gi0, g_cGameInstance);
    g_lpArr[0] = g_lp0;
    FxPutTArray(g_gi0, kLocalPlayers, FxAddr(g_lpArr), 1, 1);
    FxClearBody(g_giBare);
    FxSetClass(g_giBare, g_cGameInstanceBare);
    FxClearBody(g_lp0);
    FxSetClass(g_lp0, g_cLocalPlayer);
    FxSetPtr(g_lp0, kPlayerController, g_pc0);
    FxClearBody(g_lpBare);
    FxSetClass(g_lpBare, g_cLocalPlayerBare);
    FxClearBody(g_netConn0);
    FxSetClass(g_netConn0, g_cNetConnection);
    FxClearBody(g_widget0);
    FxSetClass(g_widget0, g_cWidgetHud);
    FxSetPtr(g_widget0, kWidgetPlayer, g_lp0);

    // ── controllers ──
    memset(FxBytes(g_pc0) - kPcTrapLead, 0, kPtrBytes);   // the PC-1 trap's bytes
    FxPlantPc(g_pc0, g_cGameCtrl, g_hero0, g_hero0, g_cam0, g_lp0, kRot, kVecD);
    FxPlantPc(g_dbgPc0, g_cBpDebugCam, g_spectator0, 0, g_camDbg0, 0, kDbgRot, kVecD);
    FxSetPtr(g_dbgPc0, kOriginalController, g_pc0);
    FxPlantPc(g_dbgIdle, g_cBpDebugCam, 0, 0, 0, 0, nullptr, kVecD);
    FxPlantPc(g_pcNoPlayer, g_cGameCtrl, g_npc0, g_npc0, g_cam0, 0, nullptr, kVecD);
    FxPlantPc(g_pcRemote, g_cGameCtrl, g_enemy0, g_enemy0, g_cam0, g_netConn0, nullptr, kVecD);
    // The template a scan must pass over holds what would make it the winner if it were taken.
    FxPlantPc(g_cdoPc, g_cPlayerController, g_enemy0, g_enemy0, g_cam0, g_lp0, nullptr, kVecD);
    FxPlantPc(g_decoyPc0, g_cDccHud, g_npc0, g_npc0, g_cam0, 0, nullptr, kVecD);
    FxSetPtr(g_decoyPc0, kOriginalController, g_pc0);
    FxPlantPc(g_pcAckOnly0, g_cPcAckOnly, g_enemy0, g_hero0, g_cam0, 0, kRot, kVecD);   // +0x30 is PawnClassOverride
    FxClearBody(g_dbgFuzzy0);
    FxSetClass(g_dbgFuzzy0, g_cBpDebugCamFuzzy);
    FxSetPtr(g_dbgFuzzy0, kOriginalController, g_pc0);
    FxClearBody(g_pcU0);
    FxSetClass(g_pcU0, g_cPcU);
    FxSetPtr(g_pcU0, kPlayer, g_lp0);
    FxBytes(g_pcU0)[kShowCursor] = kCursorSiblingBit;

    // ── pawns, roots, movement ──
    FxPlantPawn(g_hero0, g_cHero, g_root0, g_pc0, g_cmc0, g_mesh0);
    FxPlantPawn(g_npc0, g_cNpc, g_rootNpc, g_pcNoPlayer, 0, 0);
    FxPlantPawn(g_enemy0, g_cEnemy, g_rootEnemy, g_pcRemote, 0, 0);
    FxPlantPawn(g_spectator0, g_cSpectator, g_rootSpec, g_dbgPc0, 0, 0);
    FxClearBody(g_prop0);
    FxSetClass(g_prop0, g_cProp);
    FxSetPtr(g_prop0, kRootComponent, g_rootProp);
    FxSetPtr(g_prop0, kPropMesh, g_mesh0);
    FxPlantRoot(g_root0, g_cCapsule, kStart, kVecD);
    FxSetVecAt(g_root0 + kC2W, kVecD, kStart);   // the cached world transform deep force looks for
    FxPlantRoot(g_rootNpc, g_cCapsule, kNpcPos, kVecD);
    FxPlantRoot(g_rootEnemy, g_cCapsule, kEnemyPos, kVecD);
    FxPlantRoot(g_rootSpec, g_cCapsule, kSpecPos, kVecD);
    FxPlantRoot(g_rootProp, g_cCapsule, kPropPos, kVecD);
    FxClearBody(g_parent0);
    FxSetClass(g_parent0, g_cSceneComponent);
    FxClearBody(g_mesh0);
    FxSetClass(g_mesh0, g_cSkeletalMesh);
    FxPlantCmc(g_cmc0, g_cCmc, kVecD);
    FxClearBody(g_cmcNoVel0);
    FxSetClass(g_cmcNoVel0, g_cCmcNoVel);
    FxSetVecAt(g_cmcNoVel0 + kAcceleration, kVecD, kAcc);
    FxClearBody(g_cmcFuzzy0);
    FxSetClass(g_cmcFuzzy0, g_cCmcFuzzy);
    memcpy(FxBytes(g_cmcFuzzy0 + kBaseWalkSpeed), &kWalkSpeedValue, sizeof kWalkSpeedValue);
    memcpy(FxBytes(g_cmcFuzzy0 + kFuzzyGravity), &kGravityValue, sizeof kGravityValue);
    memcpy(FxBytes(g_cmcFuzzy0 + kFuzzyJump), &kJumpZValue, sizeof kJumpZValue);
    FxBytes(g_cmcFuzzy0)[kGroundMovementMode] = kModeWalking;
    FxSetVecAt(g_cmcFuzzy0 + kLastVelocity, kVecD, kVel);

    // ── cameras: the view target is the pawn (so the visible-actor diagnostic stays silent), the pending one is not ──
    FxPlantCam(g_cam0, g_cBpCam, g_hero0, g_enemy0, kVecD);
    FxPlantCam(g_camDbg0, g_cBpCam, g_hero0, g_enemy0, kVecD);

    // ── the UE4 twins, each float vector followed by guard bytes a 24-byte write would overrun ──
    FxPlantPc(g_pcF0, g_cGameCtrlF, g_heroF0, g_heroF0, g_camF0, g_lp0, kRot, kVecF);
    memset(FxBytes(g_pcF0 + kCtrlRotGuard), kGuardByte, kGuardBytes);
    FxPlantPawn(g_heroF0, g_cHeroF, g_rootF0, g_pcF0, g_cmcF0, g_mesh0);
    FxPlantRoot(g_rootF0, g_cCapsuleF, kStart, kVecF);
    memset(FxBytes(g_rootF0 + kRelLocGuard), kGuardByte, kGuardBytes);
    FxSetVecAt(g_rootF0 + kC2WF, kVecF, kStart);
    FxPlantCmc(g_cmcF0, g_cCmcF, kVecF);
    FxPlantCam(g_camF0, g_cPcmF, g_heroF0, g_enemy0, kVecF);

    // ── the edge objects ──
    FxClearBody(g_rootEdge);
    FxSetClass(g_rootEdge, g_cCapsule);
    FxClearBody(g_rootNearEdge);
    FxSetClass(g_rootNearEdge, g_cCapsule);
    FxSetVecAt(g_rootNearEdge + kRelativeLocation, kVecD, kStart);
    FxSetVecAt(g_rootNearEdge + kC2W, kVecD, kStart);
    for (uintptr_t cam : { g_camEdgeRot, g_camEdgeLoc }) {
        FxClearBody(cam);
        FxSetClass(cam, g_cBpCam);
        FxSetPtr(cam, kPendingViewTarget, g_enemy0);
        FxSetPtr(cam, kViewTarget, g_hero0);
    }
    FxSetVecAt(g_camEdgeRot + kPovBase, kVecD, kRawLoc);   // the last readable bytes of its page

    FxPool(FxPoolVariant::Default);
    g_gworldSlot = g_worldA;
    g_cachedGWorld = FxAddr(&g_gworldSlot);
}

// ══ the fixture: swaps a scenario makes (FxReset undoes every one) ═══════════════════════════════════════════════
static void FxWorld(uintptr_t world) { g_gworldSlot = world; }               // 0 = a GWorld slot that holds nothing
static void FxNoGWorld() { g_cachedGWorld = 0; }
static void FxGWorldAt(uintptr_t slotAddr) { g_cachedGWorld = slotAddr; }   // e.g. g_unreadablePage
// One link of the GWorld chain broken; ResolveLocalPC then falls to the instance scan.
enum class FxBreak { NoGameInstance, NoLocalPlayers, MaxBelowNum, NullLocalPlayer, NoController };
static void FxBreakChain(FxBreak b) {
    switch (b) {
    case FxBreak::NoGameInstance:
        if (g_gworldSlot) FxSetPtr(g_gworldSlot, FxOff::kOwningGameInstance, 0);
        break;
    case FxBreak::NoLocalPlayers:
        FxPutTArray(g_gi0, FxOff::kLocalPlayers, FxAddr(g_lpArr), 0, 1);
        break;
    case FxBreak::MaxBelowNum:
        FxPutTArray(g_gi0, FxOff::kLocalPlayers, FxAddr(g_lpArr), 1, 0);
        break;
    case FxBreak::NullLocalPlayer:
        g_lpArr[0] = 0;
        break;
    case FxBreak::NoController:
        FxSetPtr(g_lp0, FxOff::kPlayerController, 0);
        break;
    }
}
static void FxLpController(uintptr_t pc) { FxSetPtr(g_lp0, FxOff::kPlayerController, pc); }
static void FxDebugCam(uintptr_t dcc = 0) { FxLpController(dcc ? dcc : g_dbgPc0); }   // the debug camera is active
static void FxAttach(bool on, uintptr_t root = 0) {
    FxSetPtr(root ? root : g_root0, FxOff::kAttachParent, on ? g_parent0 : 0);
}
// A root's RelativeLocation at its own width (Root_0 by default).
static void FxSetPos(const double v[3], uintptr_t root = 0) {
    const uintptr_t r = root ? root : g_root0;
    FxSetVecAt(r + FxOff::kRelativeLocation, FxRootWidth(r), v);
}
static std::array<double, 3> FxPos(uintptr_t root = 0) {
    const uintptr_t r = root ? root : g_root0;
    return FxVecAt(r + FxOff::kRelativeLocation, FxRootWidth(r));
}
// A controller's ControlRotation at a width (PC_0, doubles, by default).
static void FxSetCtrlRot(const double pyr[3], uintptr_t pc = 0, int32_t width = kVecD) {
    FxSetVecAt((pc ? pc : g_pc0) + FxOff::kControlRotation, width, pyr);
}
static std::array<double, 3> FxCtrlRot(uintptr_t pc = 0, int32_t width = kVecD) {
    return FxVecAt((pc ? pc : g_pc0) + FxOff::kControlRotation, width);
}
static uint8_t FxCursorByte(uintptr_t pc = 0) { return FxBytes(pc ? pc : g_pc0)[FxOff::kShowCursor]; }
static void FxSetCursorByte(uint8_t b, uintptr_t pc = 0) { FxBytes(pc ? pc : g_pc0)[FxOff::kShowCursor] = b; }
// PC_0 as a PlayerControllerNoPawn, and the 8 bytes a read at PC-1 would take spelling BP_Enemy_C_0 -- so a lookup that
// read at offset -1 would come back with a live pawn instead of nothing.
static void FxPcNoPawn(bool on) {
    const uintptr_t lead = on ? g_enemy0 : 0;
    memcpy(FxBytes(g_pc0) - kPcTrapLead, &lead, sizeof lead);
    FxSetClass(g_pc0, on ? g_cPcNoPawn : g_cGameCtrl);
}
// The UE4 world: LP_0's controller is PCF_0 (whose pawn, root, movement and camera are the float twins), and the line
// trace is LTS-F.
static void FxUE4World() {
    FxLpController(g_pcF0);
    FxKsl(g_ltsF);
}
// The write-fail aliases: a copy of Root_0 (or PC_0) as it is now, which the world then reaches only through the
// read-only view. Returns the read-only address.
static uintptr_t FxUseRoRoot() {
    memcpy(FxBytes(g_wfRw + kWfRootAt), FxBytes(g_root0), kBigBytes);
    const uintptr_t ro = g_wfRo + kWfRootAt;
    FxSetPtr(g_hero0, FxOff::kRootComponent, ro);
    return ro;
}
static uintptr_t FxUseRoPc() {
    memcpy(FxBytes(g_wfRw + kWfPcAt), FxBytes(g_pc0), kObjBytes);
    const uintptr_t ro = g_wfRo + kWfPcAt;
    FxLpController(ro);
    return ro;
}
static uintptr_t FxRwOf(uintptr_t ro) { return g_wfRw + (ro - g_wfRo); }   // the same bytes, through the writable view
// The UProperty mini-world: UProperty mode, PCU_0 the only pooled controller, GWorld -> WorldBare so the chain breaks
// and the scan finds it. Off restores FProperty mode, the default pool and Map_A.
static void FxUpropWorld(bool on) {
    DynOff::bUseFProperty = !on;
    FxPool(on ? FxPoolVariant::Uprop : FxPoolVariant::Default);
    g_gworldSlot = on ? g_worldBare : g_worldA;
}

// ══ the fake engine's script ═════════════════════════════════════════════════════════════════════════════════════
static void FxRcs(FxKind kind, std::initializer_list<int32_t> rcs) {
    std::lock_guard<std::mutex> lk(g_engMu);
    for (int32_t rc : rcs) g_rcQueue[kind].push_back(rc);
}
static void FxRcAlways(FxKind kind, int32_t rc) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_rcAlways[kind] = rc;
}
static void FxRetByte(FxKind kind, uint8_t b) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_retByte[kind] = b;
}
static void FxMoveMode(FxKind kind, FxMove mode, double shortBy = 0.0) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_moveMode[kind] = mode;
    g_shortBy[kind] = shortBy;
}
// A getter's answer (GetLoc, Forward, CamLoc, CamRot; Fov takes v[0]) for one instance, or for any (instance 0).
static void FxAnswer(FxKind kind, const double v[3], uintptr_t instance = 0) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_answer[FxAnswerKey{ kind, instance }] = { v[0], v[1], v[2] };
}
static void FxAnswerScalar(FxKind kind, double v, uintptr_t instance = 0) {
    const double a[3] = { v, 0.0, 0.0 };
    FxAnswer(kind, a, instance);
}
static void FxSilent(FxKind kind, bool on = true) {
    std::lock_guard<std::mutex> lk(g_engMu);
    if (on) g_silent.insert(kind);
    else g_silent.erase(kind);
}
static void FxMouse(double x, double y) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_mouse[0] = x;
    g_mouse[1] = y;
}
static void FxViewportSize(int32_t sx, int32_t sy) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_viewport[0] = sx;
    g_viewport[1] = sy;
}
static void FxDeproject(const double loc[3], const double dir[3]) {
    std::lock_guard<std::mutex> lk(g_engMu);
    for (int i = 0; i < 3; ++i) {
        g_deprojLoc[i] = loc[i];
        g_deprojDir[i] = dir[i];
    }
}
static void FxDeprojectDir(const double dir[3]) {
    std::lock_guard<std::mutex> lk(g_engMu);
    for (int i = 0; i < 3; ++i) g_deprojDir[i] = dir[i];
}
// A blocking hit for one channel of one trace kind (HitCursor, HitScreen, Trace); every other channel misses.
static void FxHit(FxKind kind, int channel, const double* point = kHit) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_hits[kind][static_cast<uint8_t>(channel)] = { point[0], point[1], point[2] };
}
static void FxAttachedWorld(const double v[3]) {
    std::lock_guard<std::mutex> lk(g_engMu);
    for (int i = 0; i < 3; ++i) g_attachedWorld[i] = v[i];
}
static std::array<double, 3> FxAttachedWorldNow() {
    std::lock_guard<std::mutex> lk(g_engMu);
    return { g_attachedWorld[0], g_attachedWorld[1], g_attachedWorld[2] };
}
// The hook every call runs after it is recorded: (kind, instance, nth call of that kind since this was armed).
static void FxOnCall(std::function<void(FxKind, uintptr_t, int)> fn) {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_onCall = std::move(fn);
    g_onCallSeen.clear();
}
static void FxOnLog(std::function<void(const FxLogLine&)> fn) {
    std::lock_guard<std::mutex> lk(g_logHookMu);
    g_onLog = std::move(fn);
}
static void FxResponsive(bool on) { g_responsive.store(on); }
// [LOCK.01] The op-lock probe, with its counters zeroed.
static void FxProbe(bool on) {
    g_invProbed.store(0);
    g_invHeld.store(0);
    g_invProbe.store(on);
}
static void FxClearScripts() {
    std::lock_guard<std::mutex> lk(g_engMu);
    g_rcQueue.clear();
    g_rcAlways.clear();
    g_retByte.clear();
    g_moveMode.clear();
    g_shortBy.clear();
    g_answer.clear();
    g_silent.clear();
    g_hits.clear();
    g_mouse[0] = kMouse[0];
    g_mouse[1] = kMouse[1];
    g_viewport[0] = kViewport[0];
    g_viewport[1] = kViewport[1];
    for (int i = 0; i < 3; ++i) {
        g_deprojLoc[i] = kDeprojLoc[i];
        g_deprojDir[i] = kDeprojDir[i];
        g_attachedWorld[i] = kAttachedWorld[i];
    }
    g_onCall = nullptr;
    g_onCallSeen.clear();
}
static void FxClearRecorders() {
    {
        std::lock_guard<std::mutex> lk(g_engMu);
        g_calls.clear();
    }
    {
        std::lock_guard<std::mutex> lk(g_respMu);
        g_respArgs.clear();
    }
    std::lock_guard<std::mutex> lk(g_logMu);
    g_log.clear();
}
static std::vector<int32_t> FxRespArgs() {
    std::lock_guard<std::mutex> lk(g_respMu);
    return g_respArgs;
}

// ══ reading the call log ═════════════════════════════════════════════════════════════════════════════════════════
static size_t FxCallMark() {
    std::lock_guard<std::mutex> lk(g_engMu);
    return g_calls.size();
}
static std::vector<FxCall> FxCalls(size_t from = 0) {
    std::lock_guard<std::mutex> lk(g_engMu);
    if (from >= g_calls.size()) return {};
    return std::vector<FxCall>(g_calls.begin() + static_cast<std::ptrdiff_t>(from), g_calls.end());
}
static std::vector<FxCall> FxCallsOf(FxKind kind, size_t from = 0) {
    std::vector<FxCall> out;
    for (auto& c : FxCalls(from))
        if (c.kind == kind) out.push_back(std::move(c));
    return out;
}
static int FxCount(FxKind kind, size_t from = 0) { return static_cast<int>(FxCallsOf(kind, from).size()); }
static int FxCountAll(size_t from = 0) { return static_cast<int>(FxCalls(from).size()); }
static int FxCountOn(FxKind kind, uintptr_t instance, size_t from = 0) {
    int n = 0;
    for (const auto& c : FxCallsOf(kind, from))
        if (c.instance == instance) ++n;
    return n;
}
// The nth call of `kind` (0-based); seq 0 when there is none.
static FxCall FxNth(FxKind kind, int n = 0, size_t from = 0) {
    const auto calls = FxCallsOf(kind, from);
    return n >= 0 && static_cast<size_t>(n) < calls.size() ? calls[static_cast<size_t>(n)] : FxCall{};
}
static std::vector<FxKind> FxKinds(size_t from = 0) {
    std::vector<FxKind> out;
    for (const auto& c : FxCalls(from)) out.push_back(c.kind);
    return out;
}
static bool FxKindsAre(std::initializer_list<FxKind> expected, size_t from = 0) {
    const auto got = FxKinds(from);
    return got.size() == expected.size() && std::equal(got.begin(), got.end(), expected.begin());
}
template <class T>
static T FxRd(const FxCall& c, size_t off) {
    T v{};
    if (off + sizeof(T) <= c.params.size()) memcpy(&v, c.params.data() + off, sizeof(T));
    return v;
}
static std::array<double, 3> FxVec(const FxCall& c, size_t off, int32_t width) {
    std::array<double, 3> v{};
    if (width >= kVecD) {
        for (size_t i = 0; i < 3; ++i) v[i] = FxRd<double>(c, off + i * 8);
    } else {
        for (size_t i = 0; i < 3; ++i) v[i] = FxRd<float>(c, off + i * 4);
    }
    return v;
}
static double FxScalar(const FxCall& c, size_t off, int32_t width) {
    return width == static_cast<int32_t>(sizeof(double)) ? FxRd<double>(c, off) : FxRd<float>(c, off);
}
// Every byte of the arriving parameters outside the [offset, offset + length) ranges is zero.
static bool FxZeroOutside(const FxCall& c, std::initializer_list<std::pair<size_t, size_t>> keep) {
    for (size_t i = 0; i < c.params.size(); ++i) {
        bool kept = false;
        for (const auto& r : keep) kept = kept || (i >= r.first && i < r.first + r.second);
        if (!kept && c.params[i] != 0) return false;
    }
    return true;
}
static std::string FxCallStr(const FxCall& c) {
    return FxFmt("%s %s.%s/%u rc=%d", FxKindName(c.kind), FxNameOf(c.instance).c_str(), FxNameOf(c.ufunc).c_str(),
                 c.size, c.rc);
}
static std::string FxCallsStr(const std::vector<FxCall>& calls) {
    std::string out = FxFmt("%zu call(s):", calls.size());
    for (const auto& c : calls) out += "\n      " + FxCallStr(c);
    return out;
}
static std::string FxKindsStr(const std::vector<FxKind>& kinds) {
    std::string out = "[";
    for (size_t i = 0; i < kinds.size(); ++i) out += std::string(i ? ", " : "") + FxKindName(kinds[i]);
    return out + "]";
}

// ══ reading the log ══════════════════════════════════════════════════════════════════════════════════════════════
// By default a reader counts Teleport lines only: category WALK and text starting "Teleport" ("Teleport: ",
// "Teleport diag: "), since Ubel and Solitar write WALK too. The ...Cat forms take any category (nullptr = every line).
constexpr const char* kTeleportPrefix = "Teleport";
static size_t FxLogMark() {
    std::lock_guard<std::mutex> lk(g_logMu);
    return g_log.size();
}
static std::vector<FxLogLine> FxLogs(size_t from = 0) {
    std::lock_guard<std::mutex> lk(g_logMu);
    if (from >= g_log.size()) return {};
    return std::vector<FxLogLine>(g_log.begin() + static_cast<std::ptrdiff_t>(from), g_log.end());
}
static bool FxIsTeleportLine(const FxLogLine& l) {
    return l.cat == kWalkCat && l.text.compare(0, strlen(kTeleportPrefix), kTeleportPrefix) == 0;
}
static bool FxLogIs(const FxLogLine& l, char lvl, const char* cat) {
    return (!lvl || l.lvl == lvl) && (!cat || l.cat == cat);
}
static std::vector<FxLogLine> FxTeleportLog(size_t from = 0) {
    std::vector<FxLogLine> out;
    for (auto& l : FxLogs(from))
        if (FxIsTeleportLine(l)) out.push_back(std::move(l));
    return out;
}
// Teleport lines whose text is exactly `text` (lvl 0 = any level).
static int FxLogCount(const std::string& text, char lvl = 0, size_t from = 0) {
    int n = 0;
    for (const auto& l : FxTeleportLog(from))
        if ((!lvl || l.lvl == lvl) && l.text == text) ++n;
    return n;
}
static int FxLogPrefix(const std::string& prefix, char lvl = 0, size_t from = 0) {
    int n = 0;
    for (const auto& l : FxTeleportLog(from))
        if ((!lvl || l.lvl == lvl) && l.text.compare(0, prefix.size(), prefix) == 0) ++n;
    return n;
}
static int FxTeleportLines(char lvl = 0, size_t from = 0) {
    int n = 0;
    for (const auto& l : FxTeleportLog(from))
        if (!lvl || l.lvl == lvl) ++n;
    return n;
}
// The stamp of the first (or last) such Teleport line, 0 when there is none.
static uint64_t FxLogSeq(const std::string& text, char lvl = 0, size_t from = 0) {
    for (const auto& l : FxTeleportLog(from))
        if ((!lvl || l.lvl == lvl) && l.text == text) return l.seq;
    return 0;
}
static uint64_t FxLogSeqPrefix(const std::string& prefix, char lvl = 0, size_t from = 0) {
    for (const auto& l : FxTeleportLog(from))
        if ((!lvl || l.lvl == lvl) && l.text.compare(0, prefix.size(), prefix) == 0) return l.seq;
    return 0;
}
static uint64_t FxLogLastSeqPrefix(const std::string& prefix, char lvl = 0, size_t from = 0) {
    uint64_t last = 0;
    for (const auto& l : FxTeleportLog(from))
        if ((!lvl || l.lvl == lvl) && l.text.compare(0, prefix.size(), prefix) == 0) last = l.seq;
    return last;
}
static int FxLogCountCat(const std::string& text, char lvl, const char* cat, size_t from = 0) {
    int n = 0;
    for (const auto& l : FxLogs(from))
        if (FxLogIs(l, lvl, cat) && l.text == text) ++n;
    return n;
}
static int FxLogPrefixCat(const std::string& prefix, char lvl, const char* cat, size_t from = 0) {
    int n = 0;
    for (const auto& l : FxLogs(from))
        if (FxLogIs(l, lvl, cat) && l.text.compare(0, prefix.size(), prefix) == 0) ++n;
    return n;
}
static std::string FxLogDump(size_t from = 0) {
    std::string out;
    for (const auto& l : FxTeleportLog(from)) out += std::string("\n      ") + l.lvl + " " + l.text;
    return out;
}
static std::string FxLogDumpCat(size_t from, const char* cat) {
    std::string out;
    for (const auto& l : FxLogs(from))
        if (!cat || l.cat == cat) out += std::string("\n      ") + l.lvl + " [" + l.cat + "] " + l.text;
    return out;
}

// ── the fixed Teleport lines, verbatim from Wirbel.cpp ──
namespace FxText {
constexpr const char* kAttachedFail =
    "Teleport: attached pawn but K2_GetActorLocation failed — falling back to RelativeLocation (parent-relative!)";
constexpr const char* kNoCmcPose = "Teleport: no CharacterMovement on pawn — velocity/acceleration unavailable";
constexpr const char* kNoCmcStop = "Teleport: no CharacterMovement on pawn (skip velocity reset)";
constexpr const char* kDeepNone =
    "Teleport: deep-force found no ComponentToWorld vector matching the old position — visual may not move";
constexpr const char* kPovRaw = "Teleport: POV via raw cached-POV read (invoke getters yielded nothing)";
constexpr const char* kPovSkipped =
    "Teleport: POV getters skipped — game thread not ticking (paused/suspended), using raw cached POV";
constexpr const char* kNoSetters =
    "Teleport: no K2_TeleportTo / K2_SetActorLocation on pawn class (likely cooked out) — trying RootComponent setters";
constexpr const char* kTier2 =
    "Teleport: tier-2 raw RelativeLocation write (no transform update — may not move the visual)";
constexpr const char* kLayoutUnresolved = "Teleport: cursor — bShowMouseCursor FBoolProperty layout unresolved";
constexpr const char* kNoWbl = "Teleport: cursor — no WidgetBlueprintLibrary (UMG cooked out?); bShowMouseCursor write "
                               "only (input mode may still hide it)";
constexpr const char* kCursorZero = "Teleport: cursor — GetMousePosition returned (0,0) (cursor captured/hidden) — "
                                    "falling back to screen center";
constexpr const char* kDeprojFailed = "Teleport: cursor — DeprojectScreenPositionToWorld invoke failed/empty";
constexpr const char* kKslMissing = "Teleport: cursor — KismetSystemLibrary instance/CDO not found";
constexpr const char* kLtsMissing =
    "Teleport: cursor — LineTraceSingle not found on KismetSystemLibrary (cooked out?) — no trace fallback";
constexpr const char* kNoOutHit = "Teleport: cursor — LineTraceSingle has no OutHit param (layout?)";
constexpr const char* kNoFacing = "Teleport: relative move — could not resolve a facing direction";
constexpr const char* kBugItPr =
    "Teleport: BugIt stored from a PARENT-RELATIVE read -- not world coordinates; BugItGo will refuse it";
constexpr const char* kDiagPrefix = "Teleport diag:";
constexpr const char* kStillQueued = " (did not complete; -5 = still queued)";   // StopMovement's suffix on rc != 0
}  // namespace FxText

// ── the formatted Teleport lines a check compares most often, from Wirbel.cpp's own format strings ──
namespace FxLine {
static unsigned long long U(uintptr_t v) { return static_cast<unsigned long long>(v); }
static std::string Chain(uintptr_t pc) { return FxFmt("Teleport: local PC 0x%llX via GWorld chain", U(pc)); }
static std::string ScanLocal(uintptr_t pc) {
    return FxFmt("Teleport: local PC 0x%llX via instance scan (Player set)", U(pc));
}
static std::string ScanRemote(uintptr_t pc) {
    return FxFmt("Teleport: PC 0x%llX via instance scan (a remote Player; no local one found)", U(pc));
}
static std::string ScanNoPlayer(uintptr_t pc) {
    return FxFmt("Teleport: PC 0x%llX via instance scan WITHOUT a Player", U(pc));
}
static std::string Hop(uintptr_t dcc, uintptr_t orig) {
    return FxFmt("Teleport: hopped DebugCameraController 0x%llX -> original PC 0x%llX", U(dcc), U(orig));
}
static std::string PostMove(const double cur[3], const double target[3], bool attached) {
    return FxFmt("Teleport: post-move cur=(%.1f, %.1f, %.1f) target=(%.1f, %.1f, %.1f) attached=%d", cur[0], cur[1],
                 cur[2], target[0], target[1], target[2], attached ? 1 : 0);
}
static std::string PawnSetterOk(const char* fn, uintptr_t pawn, const double xyz[3]) {
    return FxFmt("Teleport: %s invoked OK on pawn 0x%llX -> (%.1f, %.1f, %.1f)", fn, U(pawn), xyz[0], xyz[1], xyz[2]);
}
static std::string CompSetterOk(const char* fn, uintptr_t root, const double xyz[3]) {
    return FxFmt("Teleport: %s invoked OK on RootComponent 0x%llX -> (%.1f, %.1f, %.1f)", fn, U(root), xyz[0], xyz[1],
                 xyz[2]);
}
static std::string StopMove(uintptr_t pc, int32_t rc) {
    return FxFmt("Teleport: AController::StopMovement on PC 0x%llX: rc=%d%s", U(pc), rc,
                 rc == 0 ? "" : FxText::kStillQueued);
}
static std::string StopMoveNow(uintptr_t cmc, int32_t rc) {
    return FxFmt("Teleport: StopMovementImmediately on CMC 0x%llX: rc=%d%s", U(cmc), rc,
                 rc == 0 ? "" : FxText::kStillQueued);
}
static std::string DeepRewrote(int n, uintptr_t root) {
    return FxFmt("Teleport: deep-force rewrote %d world-transform vector(s) on RootComponent 0x%llX (no K2 setter "
                 "available)", n, U(root));
}
static std::string LastSaved(const double xyz[3], const char* map) {
    return FxFmt("Teleport: last position auto-saved (%.1f, %.1f, %.1f) map='%s'", xyz[0], xyz[1], xyz[2], map);
}
static std::string MarkerSaved(int slot, const double xyz[3], const char* map) {
    return FxFmt("Teleport: marker %d saved (%.1f, %.1f, %.1f) map='%s'", slot, xyz[0], xyz[1], xyz[2], map);
}
static std::string Recall(int slot, int32_t rc) { return FxFmt("Teleport: recall %d -> rc=%d", slot, rc); }
static std::string Explicit(const double xyz[3], bool rot, bool keepUndo, int32_t rc) {
    return FxFmt("Teleport: explicit recall (%.1f, %.1f, %.1f) rot=%d keep_undo=%d -> rc=%d", xyz[0], xyz[1], xyz[2],
                 rot ? 1 : 0, keepUndo ? 1 : 0, rc);
}
static std::string DiagUnresolved(const char* camClass) {
    return FxFmt("Teleport diag: ViewTarget.Target UNRESOLVED on camera class '%s' — can't check for a separate visible "
                 "actor (ViewTarget not reflected?)", camClass);
}
}  // namespace FxLine

// ══ poses, slots, sentinels ══════════════════════════════════════════════════════════════════════════════════════
static Wirbel::Pose FxPose(const double xyz[3], const double* pyr = nullptr) {
    Wirbel::Pose p{};
    p.X = xyz[0];
    p.Y = xyz[1];
    p.Z = xyz[2];
    if (pyr) {
        p.Pitch = pyr[0];
        p.Yaw = pyr[1];
        p.Roll = pyr[2];
    }
    return p;
}
static Wirbel::Pose FxSentinelPose() {
    return Wirbel::Pose{ kPoseSentinel, kPoseSentinel, kPoseSentinel, kPoseSentinel, kPoseSentinel, kPoseSentinel };
}
static bool FxIsSentinel(const Wirbel::Pose& p) {
    return p.X == kPoseSentinel && p.Y == kPoseSentinel && p.Z == kPoseSentinel && p.Pitch == kPoseSentinel &&
           p.Yaw == kPoseSentinel && p.Roll == kPoseSentinel;
}
static std::array<double, 3> FxArr(const double v[3]) { return { v[0], v[1], v[2] }; }
static std::array<double, 3> FxXyzOf(const Wirbel::Pose& p) { return { p.X, p.Y, p.Z }; }
static std::array<double, 3> FxPyrOf(const Wirbel::Pose& p) { return { p.Pitch, p.Yaw, p.Roll }; }
// Componentwise within tol (0 = exactly).
static bool FxEq3(const std::array<double, 3>& a, const double b[3], double tol = 0.0) {
    for (size_t i = 0; i < 3; ++i)
        if (std::fabs(a[i] - b[i]) > tol) return false;
    return true;
}
static bool FxPoseIs(const Wirbel::Pose& p, const double xyz[3], const double* pyr = nullptr, double tol = 0.0) {
    static constexpr double kZero[3] = { 0.0, 0.0, 0.0 };
    return FxEq3(FxXyzOf(p), xyz, tol) && FxEq3(FxPyrOf(p), pyr ? pyr : kZero, tol);
}
static bool FxPoseEq(const Wirbel::Pose& a, const Wirbel::Pose& b) {
    return a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.Pitch == b.Pitch && a.Yaw == b.Yaw && a.Roll == b.Roll;
}
static std::string FxArrStr(const std::array<double, 3>& v) { return FxFmt("(%.9g, %.9g, %.9g)", v[0], v[1], v[2]); }
static std::string FxPoseStr(const Wirbel::Pose& p) {
    return FxFmt("(%.9g, %.9g, %.9g | %.9g, %.9g, %.9g)", p.X, p.Y, p.Z, p.Pitch, p.Yaw, p.Roll);
}
static std::string FxMarkerStr(const Wirbel::Marker& m) {
    return FxFmt("valid=%d pr=%d map='%s' ", m.Valid ? 1 : 0, m.ParentRelative ? 1 : 0, m.MapName) + FxPoseStr(m.P);
}
static void FxFillMap(char* buf, size_t n) { memset(buf, kMapFill, n); }

// Wirbel's slots, read and planted directly under its own lock -- "last" and BugIt have no public setter.
static Wirbel::Marker FxMarker(const Wirbel::Pose& p, const char* map, bool parentRelative = false) {
    Wirbel::Marker m{};
    m.Valid = true;
    m.P = p;
    snprintf(m.MapName, sizeof m.MapName, "%s", map ? map : "");
    m.ParentRelative = parentRelative;
    return m;
}
static void FxPlantMarker(int slot, const Wirbel::Marker& m) {
    std::lock_guard<std::mutex> lk(s_opMutex);
    s_markers[slot] = m;
}
static void FxPlantLast(const Wirbel::Marker& m) {
    std::lock_guard<std::mutex> lk(s_opMutex);
    s_lastMarker = m;
}
static void FxPlantBugIt(const Wirbel::Marker& m) {
    std::lock_guard<std::mutex> lk(s_opMutex);
    s_bugItMarker = m;
}
static Wirbel::Marker FxMarkerNow(int slot) {
    std::lock_guard<std::mutex> lk(s_opMutex);
    return s_markers[slot];
}
static Wirbel::Marker FxLastNow() {
    std::lock_guard<std::mutex> lk(s_opMutex);
    return s_lastMarker;
}
static Wirbel::Marker FxBugItNow() {
    std::lock_guard<std::mutex> lk(s_opMutex);
    return s_bugItMarker;
}
static bool FxMarkerEq(const Wirbel::Marker& a, const Wirbel::Marker& b) {
    return a.Valid == b.Valid && a.ParentRelative == b.ParentRelative && FxPoseEq(a.P, b.P) &&
           strncmp(a.MapName, b.MapName, sizeof a.MapName) == 0;
}
static void FxClearSlots() {
    std::lock_guard<std::mutex> lk(s_opMutex);
    for (auto& m : s_markers) m = Wirbel::Marker{};
    s_lastMarker = Wirbel::Marker{};
    s_bugItMarker = Wirbel::Marker{};
}

// ══ reset, build, cleanup ════════════════════════════════════════════════════════════════════════════════════════
// Back to a clean slate, in this order: hooks and scripts cleared, the gate ticking, the probe off and Tot's per-command
// cancel lowered; then every world object, class head and the pool back to default and GWorld -> Map_A; then Wirbel's
// slots emptied under its own lock; then both recorders emptied, last. A scenario plants a slot AFTER this, and clears
// the recorders again after its last setup call. A blocking hook must be released before this runs.
static void FxReset() {
    FxOnLog(nullptr);
    FxClearScripts();
    g_responsive.store(true);
    FxProbe(false);
    Tot::ResetPerCommand();
    FxWorldDefault();
    FxClearSlots();
    FxClearRecorders();
}

// Built once, before any Wirbel call. It never touches Wirbel's slots: LAST.05 reads "last" as the process starts.
static void FxBuildWorld() {
    g_mainTid = std::this_thread::get_id();

    // The offsets probe's decisions, made up front so no walk runs a GObjects vote: FName without the case-preserving
    // number, FProperty chains, the FunctionFlags offset decided at 0xB0 with the tail right after it, and
    // UClass::Interfaces measured.
    DynOff::bCasePreservingName = false;
    DynOff::bUseFProperty = true;
    g_cachedUEVersion = kFixtureUEVersion;
    DynOff::UFUNCTION_FLAGS = kFnFlagsAt;
    DynOff::UFUNCTION_TAIL_EXTRA = 0;
    DynOff::UFUNCTION_TAIL_MEASURED.store(-1);
    DynOff::bUFunctionFlagsDetected.store(true);
    DynOff::UCLASS_INTERFACES.store(kUClassInterfacesAt);

    g_unreadablePage = FxAddr(VirtualAlloc(nullptr, kPage, MEM_RESERVE, PAGE_NOACCESS));
    if (!g_unreadablePage) FxFixtureFail("the unreadable page could not be reserved");

    // ── metaclasses and packages. "ScriptStruct" is the name Ubel::IsScriptStructObject accepts ──
    g_metaClass = FxNewObjBlob();
    FxObjAt(g_metaClass, g_metaClass, "Class", 0);
    g_metaPackage = FxObj(g_metaClass, "Package", 0);
    g_metaFunction = FxObj(g_metaClass, "Function", 0);
    g_metaStruct = FxObj(g_metaClass, "ScriptStruct", 0);
    g_pkgEngine = FxObj(g_metaPackage, "/Script/Engine", 0);
    g_pkgOther = FxObj(g_metaPackage, "/Script/EngineOther", 0);
    g_pkgEngineU = FxObj(g_metaPackage, "/Script/EngineU", 0);

    FxBuildStructs();
    FxBuildClasses();
    FxBuildFunctions();
    FxBuildObjects();
    FxSnapshotClasses();

    g_nameChunks[0] = FxAddr(g_nameChunk);
    Serie::InitUE4(FxAddr(g_nameChunks), kNameTextAt);

    FxClearScripts();
    FxWorldDefault();
    FxClearRecorders();
}

// Before main returns: the hooks gone, the gate and Tot at rest, GWorld unset, and every page and view released.
static void FxCleanup() {
    FxOnLog(nullptr);
    FxClearScripts();
    g_responsive.store(true);
    FxProbe(false);
    Tot::ResetPerCommand();
    DynOff::bUseFProperty = true;
    g_cachedGWorld = 0;
    if (g_wfRo) UnmapViewOfFile(reinterpret_cast<void*>(g_wfRo));
    if (g_wfRw) UnmapViewOfFile(reinterpret_cast<void*>(g_wfRw));
    if (g_wfSection) CloseHandle(g_wfSection);
    g_wfRo = g_wfRw = 0;
    g_wfSection = nullptr;
    for (void*& base : g_edgeBase) {
        if (base) VirtualFree(base, 0, MEM_RELEASE);
        base = nullptr;
    }
    if (g_unreadablePage) VirtualFree(reinterpret_cast<void*>(g_unreadablePage), 0, MEM_RELEASE);
    g_unreadablePage = 0;
}

// ══ Blocks_G1: FIX, RES, POSE / MVS, TRN ═══════════════════════════════════════════════════════════════════════════
// The reads every later block stands on: the fixture's own controls, the GWorld chain with its scan fallback and the
// debug-camera hop, the pose and movement read, and the standalone-trainer bundle. Each scenario starts from FxReset
// and changes one thing, so a check that fails names the one link it was about.

namespace G1 {

// A value no resolution produces, primed where a reset is owed: a field the op should have cleared keeps it.
constexpr uintptr_t kJunkAddr = 0x7777;
constexpr int32_t kJunkOff = 0x777;
constexpr int32_t kJunkCount = 7;
// A map buffer longer than any cap a call is given, so a byte written past the cap shows.
constexpr size_t kMapGuard = 16;
constexpr size_t kMapBuf = static_cast<size_t>(kMapCap) + kMapGuard;
constexpr int32_t kShortCap = 4;              // room for "Map" and its terminator, not for "Map_A"
constexpr const char* kMapA = "Map_A";
constexpr const char* kMapAShort = "Map";
// The trainer chain: OwningGameInstance, LocalPlayers, LocalPlayers[0], PlayerController, then the pawn.
constexpr int32_t kChainHops = 5;
constexpr int32_t kPawnHop = kChainHops - 1;
constexpr int32_t kGiHops = 1;                // only OwningGameInstance pushed
constexpr int32_t kLpHops = 3;                // up to LocalPlayers[0]
constexpr int32_t kPcHops = 4;                // up to PlayerController
constexpr size_t kHopSlots = sizeof(Wirbel::TrainerOffsets::Chain) / sizeof(Wirbel::TrainerChainHop);
constexpr const char* kHopLinePrefix = "Teleport: hopped";
constexpr const char* kViaScan = "via instance scan";
constexpr const char* kViaChain = "via GWorld chain";
constexpr double kStepUu = 100.0;             // a relative move's length
constexpr int32_t kChannel = 0;               // the trace channel a cursor scenario asks for and the fake hits
constexpr const char* kWblName = "WidgetBlueprintLibrary";
constexpr const char* kKslName = "KismetSystemLibrary";
// Aura's own line when a scan sees the per-command cancel: the proof the scan, not something else, gave up.
constexpr const char* kScanAborted = "FindInstancesDerivedFrom: aborted (client gone / shutdown)";
constexpr const char* kFindCat = "PIPE:find";

static bool MapIs(const char* buf, size_t n, const char* want) {
    return memchr(buf, 0, n) != nullptr && strcmp(buf, want) == 0;
}
static bool MapUntouched(const char* buf, size_t n) {
    for (size_t i = 0; i < n; ++i)
        if (buf[i] != kMapFill) return false;
    return true;
}
static std::string MapStr(const char* buf, size_t n) { return "'" + std::string(buf, strnlen(buf, n)) + "'"; }

// One GetPose with every output primed, so a check tells "written" from "left alone".
struct PoseRead {
    int32_t rc = 0;
    Wirbel::Pose p{};
    uint8_t src = kByteSentinel;
    bool pr = true;
    char map[kMapBuf] = {};
};
static PoseRead ReadPose(int32_t cap = kMapCap) {
    PoseRead r;
    r.p = FxSentinelPose();
    FxFillMap(r.map, sizeof r.map);
    r.rc = Wirbel::GetPose(r.p, r.map, cap, &r.src, &r.pr);
    return r;
}
static std::string OutStr(const PoseRead& r) {
    return FxFmt("rc=%d src=0x%02X pr=%d ", r.rc, static_cast<unsigned>(r.src), r.pr ? 1 : 0) + FxPoseStr(r.p) +
           " map=" + MapStr(r.map, sizeof r.map);
}

// Teleport lines whose text contains `part` (lvl 0 = any level).
static int TeleportLinesWith(const char* part, char lvl = 0, size_t from = 0) {
    int n = 0;
    for (const auto& l : FxTeleportLog(from))
        if ((!lvl || l.lvl == lvl) && l.text.find(part) != std::string::npos) ++n;
    return n;
}

// ── the movement read ──
static Wirbel::MovementState JunkMove() {
    Wirbel::MovementState m;
    m.PawnAddr = m.LocOwnerAddr = m.VelOwnerAddr = m.AccOwnerAddr = m.RotOwnerAddr = kJunkAddr;
    m.HasMovement = true;
    m.VelX = m.VelY = m.VelZ = m.AccX = m.AccY = m.AccZ = m.Speed = kPoseSentinel;
    m.LocFieldOffset = m.VelFieldOffset = m.AccFieldOffset = m.RotFieldOffset = kJunkOff;
    return m;
}
static bool MoveEq(const Wirbel::MovementState& a, const Wirbel::MovementState& b) {
    return a.PawnAddr == b.PawnAddr && a.HasMovement == b.HasMovement && a.VelX == b.VelX && a.VelY == b.VelY &&
           a.VelZ == b.VelZ && a.AccX == b.AccX && a.AccY == b.AccY && a.AccZ == b.AccZ && a.Speed == b.Speed &&
           a.LocOwnerAddr == b.LocOwnerAddr && a.LocFieldOffset == b.LocFieldOffset &&
           a.VelOwnerAddr == b.VelOwnerAddr && a.VelFieldOffset == b.VelFieldOffset &&
           a.AccOwnerAddr == b.AccOwnerAddr && a.AccFieldOffset == b.AccFieldOffset &&
           a.RotOwnerAddr == b.RotOwnerAddr && a.RotFieldOffset == b.RotFieldOffset;
}
static std::string MoveStr(const Wirbel::MovementState& m) {
    return FxFmt("pawn=%s has=%d vel=(%.9g, %.9g, %.9g) speed=%.9g acc=(%.9g, %.9g, %.9g) loc=%s/%d vel=%s/%d "
                 "acc=%s/%d rot=%s/%d",
                 FxHex(m.PawnAddr).c_str(), m.HasMovement ? 1 : 0, m.VelX, m.VelY, m.VelZ, m.Speed, m.AccX, m.AccY,
                 m.AccZ, FxHex(m.LocOwnerAddr).c_str(), m.LocFieldOffset, FxHex(m.VelOwnerAddr).c_str(),
                 m.VelFieldOffset, FxHex(m.AccOwnerAddr).c_str(), m.AccFieldOffset, FxHex(m.RotOwnerAddr).c_str(),
                 m.RotFieldOffset);
}
static std::array<double, 3> VelOf(const Wirbel::MovementState& m) { return { m.VelX, m.VelY, m.VelZ }; }
static std::array<double, 3> AccOf(const Wirbel::MovementState& m) { return { m.AccX, m.AccY, m.AccZ }; }

// One GetPoseAndMovement with the pose, the flags, the map and every MovementState field primed.
struct MoveRead {
    int32_t rc = 0;
    Wirbel::Pose p{};
    uint8_t src = kByteSentinel;
    bool pr = true;
    char map[kMapBuf] = {};
    Wirbel::MovementState m{};
};
static MoveRead ReadMove() {
    MoveRead r;
    r.p = FxSentinelPose();
    FxFillMap(r.map, sizeof r.map);
    r.m = JunkMove();
    r.rc = Wirbel::GetPoseAndMovement(r.p, r.map, kMapCap, &r.src, r.m, &r.pr);
    return r;
}

// ── the trainer bundle ──
static Wirbel::TrainerOffsets JunkTrainer() {
    Wirbel::TrainerOffsets t;
    t.ChainCount = kJunkCount;
    for (auto& h : t.Chain) {
        snprintf(h.Field, sizeof h.Field, "%s", "junk");
        h.Offset = kJunkOff;
        h.Deref = false;
    }
    t.PawnToRoot = t.RootToRelLoc = t.FVectorWidth = t.PawnToCmc = t.WalkSpeedOff = t.GravityOff = t.JumpOff =
        t.CtrlRotOff = t.CtrlRotSize = t.PawnToController = t.MoveModeOff = t.VelocityOff = t.VelocitySize = kJunkOff;
    return t;
}
static bool TrainerEq(const Wirbel::TrainerOffsets& a, const Wirbel::TrainerOffsets& b) {
    if (a.ChainCount != b.ChainCount) return false;
    for (size_t i = 0; i < kHopSlots; ++i) {
        const auto& x = a.Chain[i];
        const auto& y = b.Chain[i];
        if (memcmp(x.Field, y.Field, sizeof x.Field) != 0 || x.Offset != y.Offset || x.Deref != y.Deref) return false;
    }
    return a.PawnToRoot == b.PawnToRoot && a.RootToRelLoc == b.RootToRelLoc && a.FVectorWidth == b.FVectorWidth &&
           a.PawnToCmc == b.PawnToCmc && a.WalkSpeedOff == b.WalkSpeedOff && a.GravityOff == b.GravityOff &&
           a.JumpOff == b.JumpOff && a.CtrlRotOff == b.CtrlRotOff && a.CtrlRotSize == b.CtrlRotSize &&
           a.PawnToController == b.PawnToController && a.MoveModeOff == b.MoveModeOff &&
           a.VelocityOff == b.VelocityOff && a.VelocitySize == b.VelocitySize;
}
static std::string TrainerStr(const Wirbel::TrainerOffsets& t) {
    std::string s = FxFmt("chain=%d:", t.ChainCount);
    for (int32_t i = 0; i < t.ChainCount && static_cast<size_t>(i) < kHopSlots; ++i) {
        const auto& h = t.Chain[i];
        s += FxFmt(" (%.*s,0x%X,%d)", static_cast<int>(sizeof h.Field), h.Field, static_cast<unsigned>(h.Offset),
                   h.Deref ? 1 : 0);
    }
    s += FxFmt(" root=%d relLoc=%d vec=%d ctrlRot=%d/%d ctrl=%d cmc=%d walk=%d grav=%d jump=%d mode=%d vel=%d/%d",
               t.PawnToRoot, t.RootToRelLoc, t.FVectorWidth, t.CtrlRotOff, t.CtrlRotSize, t.PawnToController,
               t.PawnToCmc, t.WalkSpeedOff, t.GravityOff, t.JumpOff, t.MoveModeOff, t.VelocityOff, t.VelocitySize);
    return s;
}
static bool HopIs(const Wirbel::TrainerChainHop& h, const char* field, int32_t off) {
    return strncmp(h.Field, field, sizeof h.Field) == 0 && h.Offset == off && h.Deref;
}

// An object's body as it is now, for a "nothing was written" comparison.
static std::vector<uint8_t> BodyOf(uintptr_t obj) {
    const auto it = g_bodyBytes.find(obj);
    const size_t n = it != g_bodyBytes.end() ? it->second : kObjBytes;
    return std::vector<uint8_t>(FxBytes(obj), FxBytes(obj) + n);
}

// The blob FindFunc resolves `name` to from `cls`, and its ProcessEvent buffer size; zeros when it finds nothing.
struct FnFound {
    bool found = false;
    uintptr_t blob = 0;
    uint32_t bytes = 0;
};
static FnFound FnOf(uintptr_t cls, const char* name) {
    FunctionInfo fi;
    if (!FindFunc(cls, name, fi)) return {};
    return { true, fi.address, Ubel::ParamBufferSize(fi) };
}
static std::string FnStr(const FnFound& f) {
    return f.found ? FxNameOf(f.blob) + " " + FxHex(f.blob) + " buffer " + FxHex(f.bytes) : std::string("not found");
}

// RES.10: one op from a fresh world and empty recorders; each GWorld-chain line it writes is one resolution.
static void Resolutions(const char* op, int want, int32_t wantRc, const std::function<void()>& setup,
                        const std::function<int32_t()>& run) {
    FxReset();
    if (setup) setup();
    FxClearRecorders();
    const int32_t rc = run();
    const int got = FxLogCount(FxLine::Chain(g_pc0), 'I');
    check(FxFmt("RES.10 resolutions: %s answers %d", op, wantRc).c_str(), rc == wantRc, FxS(rc) + FxLogDump());
    check(FxFmt("RES.10 resolutions: %s walks the GWorld chain %d time(s)", op, want).c_str(), got == want,
          FxS(got) + FxLogDump());
}

}  // namespace G1

static void Blocks_G1() {
    // ── FIX ──
    {
        blk("FIX - fixture self-checks: the pool's decoys, the reflection Wirbel reads, the baseline pose");
        FxReset();
        // FIX.04 (the TeleportResult codes) is the static_assert under the include, and FIX.LINK is the link itself:
        // UE5_FindInstanceOfClass is left undefined, so a Wirbel call to the user-facing lookup would not build.

        check("FIX.01 pool order: slot 6 holds the WidgetBlueprintLibraryEx decoy CDO, ahead of the real one in slot 8",
              FxSlot(kIdxCdoWblEx) == g_cdoWblEx && FxSlot(kIdxCdoWbl) == g_cdoWbl &&
                  FxNameOf(g_cdoWblEx) == "Default__WidgetBlueprintLibraryEx",
              FxNameOf(FxSlot(kIdxCdoWblEx)) + " / " + FxNameOf(FxSlot(kIdxCdoWbl)));
        check("FIX.01 pool order: slot 9 holds the KismetSystemLibraryEx decoy CDO, ahead of the real one in slot 11",
              FxSlot(kIdxCdoKslEx) == g_cdoKslEx && FxSlot(kIdxCdoKsl) == g_cdoKsl &&
                  FxNameOf(g_cdoKslEx) == "Default__KismetSystemLibraryEx",
              FxNameOf(FxSlot(kIdxCdoKslEx)) + " / " + FxNameOf(FxSlot(kIdxCdoKsl)));
        const uintptr_t wbl = Aura::FindLiveOrDefaultOf(G1::kWblName);
        check("FIX.01 pool order: WidgetBlueprintLibrary's default object is slot 8's, past the decoy in slot 6",
              wbl == FxSlot(kIdxCdoWbl) && wbl == g_cdoWbl, FxNameOf(wbl) + " " + FxHex(wbl));
        const uintptr_t ksl = Aura::FindLiveOrDefaultOf(G1::kKslName);
        check("FIX.01 pool order: KismetSystemLibrary's default object is slot 11's, past the decoy in slot 9",
              ksl == FxSlot(kIdxCdoKsl) && ksl == g_cdoKsl, FxNameOf(ksl) + " " + FxHex(ksl));

        FieldInfo cursor{};
        const bool haveCursor = Ubel::FindField(g_cGameCtrl, "bShowMouseCursor", nullptr, nullptr, nullptr, cursor);
        check("FIX.02 reflection: BP_GameCtrl_C reaches bShowMouseCursor at 0x68 through PlayerController",
              haveCursor && cursor.Offset == FxOff::kShowCursor,
              haveCursor ? FxS(cursor.Offset) : std::string("not found"));
        const int32_t rootOff = Ubel::FindFieldOffset(g_cHero, "RootComponent");
        check("FIX.02 reflection: BP_Hero_C reaches RootComponent at 0x28 through Actor",
              rootOff == FxOff::kRootComponent, FxS(rootOff));
        const G1::FnFound sal = G1::FnOf(g_cHero, "K2_SetActorLocation");
        check("FIX.02 reflection: Wirbel's FindFunc walks BP_Hero_C's supers to Actor's K2_SetActorLocation blob",
              sal.found && sal.blob == g_fnSetActorLoc, G1::FnStr(sal));
        check("FIX.02 reflection: K2_SetActorLocation's buffer is 0x110, its ParmsSize",
              sal.found && sal.bytes == static_cast<uint32_t>(kSal5.parmsSize), G1::FnStr(sal));
        const G1::FnFound lts = G1::FnOf(g_cKsl, "LineTraceSingle");
        check("FIX.02 reflection: KismetSystemLibrary's LineTraceSingle is LTS-D, past its ForObjects / ByProfile "
              "siblings, with a 0x148 buffer",
              lts.found && lts.blob == g_ltsD && lts.bytes == static_cast<uint32_t>(FxLts::kParmsSize),
              G1::FnStr(lts));
        const G1::FnFound stop = G1::FnOf(g_cGameCtrl, "StopMovement");
        check("FIX.02 reflection: StopMovement is Controller's blob, reached from BP_GameCtrl_C, with a 0-byte buffer",
              stop.found && stop.blob == g_fnStopMove && stop.bytes == 0, G1::FnStr(stop));
        const G1::FnFound mouse = G1::FnOf(g_cGameCtrl, "GetMousePosition");
        check("FIX.02 reflection: GetMousePosition is PlayerController's blob with a 0xC buffer",
              mouse.found && mouse.blob == g_fnMousePos && mouse.bytes == static_cast<uint32_t>(kMp.parmsSize),
              G1::FnStr(mouse));
        const G1::FnFound viewport = G1::FnOf(g_cGameCtrl, "GetViewportSize");
        check("FIX.02 reflection: GetViewportSize is PlayerController's blob with an 8-byte buffer",
              viewport.found && viewport.blob == g_fnViewport &&
                  viewport.bytes == static_cast<uint32_t>(FxVp::kParmsSize),
              G1::FnStr(viewport));

        FxClearRecorders();
        const G1::PoseRead r = G1::ReadPose();
        check("FIX.03 baseline: GetPose answers 0", r.rc == Wirbel::TP_OK, G1::OutStr(r));
        check("FIX.03 baseline: the pose is kStart with kRot", FxPoseEq(r.p, kStartPose), G1::OutStr(r));
        check("FIX.03 baseline: the map is \"Map_A\"", G1::MapIs(r.map, sizeof r.map, G1::kMapA), G1::OutStr(r));
        check("FIX.03 baseline: no engine call", FxCountAll() == 0, FxCallsStr(FxCalls()));
    }

    // ── RES ──
    {
        blk("RES - the GWorld chain, the instance-scan fallback, the debug-camera hop, the pawn and its root");

        // RES.01: every op that resolves stops at a GWorld it cannot read, before any call or line.
        FxReset();
        FxNoGWorld();
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.01 no GWorld: GetPose answers -1", r.rc == Wirbel::TP_ERR_NOT_INIT, G1::OutStr(r));
            check("RES.01 no GWorld: the pose, *src 0xAB and *pr true are left as primed",
                  FxIsSentinel(r.p) && r.src == kByteSentinel && r.pr, G1::OutStr(r));
            Wirbel::Pov pov;
            const int32_t povRc = Wirbel::GetPov(pov);
            Wirbel::TrainerOffsets trn;
            const int32_t trnRc = Wirbel::GetTrainerOffsets(trn);
            bool shown = false;
            const int32_t setRc = Wirbel::SetMouseCursor(true, &shown);
            bool state = false;
            const int32_t getRc = Wirbel::GetMouseCursor(&state);
            Wirbel::Pose hit = FxSentinelPose();
            uint8_t tier = kTierSentinel;
            bool usedCenter = false;
            const int32_t curRc = Wirbel::TeleportToCursor(kZOffset, G1::kChannel, true, &hit, &tier, &usedCenter);
            Wirbel::Pose landed = FxSentinelPose();
            bool known = false;
            const int32_t relRc = Wirbel::TeleportRelative(G1::kStepUu, true, landed, &tier, &known);
            check("RES.01 no GWorld: GetPov, GetTrainerOffsets, SetMouseCursor, GetMouseCursor, TeleportToCursor and "
                  "TeleportRelative each answer -1",
                  povRc == Wirbel::TP_ERR_NOT_INIT && trnRc == Wirbel::TP_ERR_NOT_INIT &&
                      setRc == Wirbel::TP_ERR_NOT_INIT && getRc == Wirbel::TP_ERR_NOT_INIT &&
                      curRc == Wirbel::TP_ERR_NOT_INIT && relRc == Wirbel::TP_ERR_NOT_INIT,
                  FxFmt("pov=%d trainer=%d set=%d get=%d cursor=%d relative=%d", povRc, trnRc, setRc, getRc, curRc,
                        relRc));
            check("RES.01 no GWorld: not one engine call across the seven ops", FxCountAll() == 0,
                  FxCallsStr(FxCalls()));
            check("RES.01 no GWorld: not one Teleport line across the seven ops", FxTeleportLines() == 0, FxLogDump());
        }
        FxReset();
        FxGWorldAt(g_unreadablePage);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.01 unreadable GWorld slot: GetPose answers -1, outputs left as primed",
                  r.rc == Wirbel::TP_ERR_NOT_INIT && FxIsSentinel(r.p) && r.src == kByteSentinel && r.pr,
                  G1::OutStr(r));
            check("RES.01 unreadable GWorld slot: no call, no Teleport line",
                  FxCountAll() == 0 && FxTeleportLines() == 0, FxCallsStr(FxCalls()) + FxLogDump());
        }
        FxReset();
        FxWorld(0);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.01 empty GWorld slot: GetPose answers -1, outputs left as primed",
                  r.rc == Wirbel::TP_ERR_NOT_INIT && FxIsSentinel(r.p) && r.src == kByteSentinel && r.pr,
                  G1::OutStr(r));
            check("RES.01 empty GWorld slot: no call, no Teleport line", FxCountAll() == 0 && FxTeleportLines() == 0,
                  FxCallsStr(FxCalls()) + FxLogDump());
        }

        // RES.02: the chain names the controller it took, once, at INFO.
        FxReset();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.02 chain: GetPose answers 0 with the hero's pose",
                  r.rc == Wirbel::TP_OK && FxPoseEq(r.p, kStartPose), G1::OutStr(r));
            check("RES.02 chain: exactly one INFO \"local PC <PC_0> via GWorld chain\"",
                  FxLogCount(FxLine::Chain(g_pc0), 'I') == 1 && FxLogCount(FxLine::Chain(g_pc0)) == 1, FxLogDump());
            check("RES.02 chain: no instance-scan line", G1::TeleportLinesWith(G1::kViaScan) == 0, FxLogDump());
        }
        FxReset();
        FxWorld(g_worldFuzzy);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.02 fuzzy GameInstance: a world with no OwningGameInstance reaches GI_0 through MyGameInstance, "
                  "past the GameInstanceSubsystems array",
                  r.rc == Wirbel::TP_OK && FxPoseEq(r.p, kStartPose), G1::OutStr(r));
            check("RES.02 fuzzy GameInstance: the chain line, not a scan line",
                  FxLogCount(FxLine::Chain(g_pc0), 'I') == 1 && G1::TeleportLinesWith(G1::kViaScan) == 0, FxLogDump());
        }

        // RES.03: each broken link falls silently to the scan, which ranks PC_0 first by its LocalPlayer.
        {
            struct Broken {
                const char* what;
                FxBreak link;
            };
            const Broken broken[] = {
                { "OwningGameInstance=0", FxBreak::NoGameInstance },
                { "LocalPlayers Num 0", FxBreak::NoLocalPlayers },
                { "LocalPlayers Max below Num", FxBreak::MaxBelowNum },
                { "LocalPlayers[0]=0", FxBreak::NullLocalPlayer },
                { "LocalPlayer.PlayerController=0", FxBreak::NoController },
            };
            for (const auto& b : broken) {
                FxReset();
                FxBreakChain(b.link);
                FxClearRecorders();
                const G1::PoseRead r = G1::ReadPose();
                check(FxFmt("RES.03 %s: GetPose answers 0 with the hero's pose", b.what).c_str(),
                      r.rc == Wirbel::TP_OK && FxPoseEq(r.p, kStartPose), G1::OutStr(r));
                check(FxFmt("RES.03 %s: exactly one INFO \"local PC <PC_0> via instance scan (Player set)\" -- past "
                            "Widget_0 holding LP_0, DbgPC_Idle, PC_NoPlayer and PC_Remote",
                            b.what)
                          .c_str(),
                      FxLogCount(FxLine::ScanLocal(g_pc0), 'I') == 1 && FxLogCount(FxLine::ScanLocal(g_pc0)) == 1,
                      FxLogDump());
                check(FxFmt("RES.03 %s: no GWorld-chain line", b.what).c_str(),
                      G1::TeleportLinesWith(G1::kViaChain) == 0, FxLogDump());
            }
        }

        // RES.04: what the scan settles for when no local controller exists, and when it is cancelled.
        FxReset();
        FxBreakChain(FxBreak::NoController);
        FxPool(FxPoolVariant::NoPc0);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.04 OtherPlayer: GetPose answers 0 with PC_Remote's pawn, Enemy_0 at kEnemyPos",
                  r.rc == Wirbel::TP_OK && FxPoseIs(r.p, kEnemyPos), G1::OutStr(r));
            check("RES.04 OtherPlayer: exactly one WARN \"PC <PC_Remote> via instance scan (a remote Player; no local "
                  "one found)\"",
                  FxLogCount(FxLine::ScanRemote(g_pcRemote), 'W') == 1 &&
                      FxLogCount(FxLine::ScanRemote(g_pcRemote)) == 1,
                  FxLogDump());
        }
        FxReset();
        FxBreakChain(FxBreak::NoController);
        FxPool(FxPoolVariant::NoPc0NoRemote);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.04 NoPlayer: GetPose answers 0 with PC_NoPlayer's pawn, Npc_0 at kNpcPos -- not the idle debug "
                  "camera ahead of it",
                  r.rc == Wirbel::TP_OK && FxPoseIs(r.p, kNpcPos), G1::OutStr(r));
            check("RES.04 NoPlayer: exactly one WARN \"PC <PC_NoPlayer> via instance scan WITHOUT a Player\"",
                  FxLogCount(FxLine::ScanNoPlayer(g_pcNoPlayer), 'W') == 1 &&
                      FxLogCount(FxLine::ScanNoPlayer(g_pcNoPlayer)) == 1,
                  FxLogDump());
        }
        FxReset();
        FxBreakChain(FxBreak::NoController);
        FxPool(FxPoolVariant::IdleOnly);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.04 None: GetPose answers -2, outputs left as primed",
                  r.rc == Wirbel::TP_ERR_NO_CONTROLLER && FxIsSentinel(r.p) && r.src == kByteSentinel && r.pr,
                  G1::OutStr(r));
            check("RES.04 None: GetPose writes no Teleport line and makes no call",
                  FxTeleportLines() == 0 && FxCountAll() == 0, FxCallsStr(FxCalls()) + FxLogDump());
            // The POV and cursor ops resolve the controller on their own: without their own -2 they would go on to a
            // null controller's class and report a missing camera manager or cursor field instead.
            const size_t logs = FxLogMark(), calls = FxCallMark();
            Wirbel::Pov pov;
            const int32_t povRc = Wirbel::GetPov(pov);
            bool shown = false;
            const int32_t setRc = Wirbel::SetMouseCursor(true, &shown);
            bool state = false;
            const int32_t getRc = Wirbel::GetMouseCursor(&state);
            check("RES.04 None: GetPov, SetMouseCursor and GetMouseCursor each answer -2",
                  povRc == Wirbel::TP_ERR_NO_CONTROLLER && setRc == Wirbel::TP_ERR_NO_CONTROLLER &&
                      getRc == Wirbel::TP_ERR_NO_CONTROLLER,
                  FxFmt("pov=%d set=%d get=%d", povRc, setRc, getRc));
            check("RES.04 None: those three write no WARN (nor any Teleport line) and make no call",
                  FxTeleportLines('W', logs) == 0 && FxTeleportLines(0, logs) == 0 && FxCountAll(calls) == 0,
                  FxCallsStr(FxCalls(calls)) + FxLogDump(logs));
            check("RES.04 None: PC_0's cursor byte is left alone", FxCursorByte() == kCursorSiblingBit,
                  FxHex(FxCursorByte()));
        }
        FxReset();
        FxBreakChain(FxBreak::NoController);
        FxClearRecorders();
        {
            Tot::RequestPerCommand();
            const G1::PoseRead r = G1::ReadPose();
            Tot::ResetPerCommand();
            check("RES.04 Cancelled: GetPose answers -2 when the scan is cancelled, outputs left as primed",
                  r.rc == Wirbel::TP_ERR_NO_CONTROLLER && FxIsSentinel(r.p) && r.src == kByteSentinel && r.pr,
                  G1::OutStr(r));
            check("RES.04 Cancelled: the scan saw the cancel -- one PIPE:find abort line",
                  FxLogCountCat(G1::kScanAborted, 'W', G1::kFindCat) == 1, FxLogDumpCat(0, nullptr));
            check("RES.04 Cancelled: no Teleport line and no call", FxTeleportLines() == 0 && FxCountAll() == 0,
                  FxCallsStr(FxCalls()) + FxLogDump());
            const G1::PoseRead again = G1::ReadPose();
            check("RES.04 Cancelled: with the cancel lowered the same world resolves again",
                  again.rc == Wirbel::TP_OK && FxPoseEq(again.p, kStartPose), G1::OutStr(again));
        }

        // RES.05: a DebugCameraController by derivation hops to its original controller; a name alone does not.
        FxReset();
        FxDebugCam();
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.05 hop: GetPose answers 0 with the hero's position, not the spectator's",
                  r.rc == Wirbel::TP_OK && FxEq3(FxXyzOf(r.p), kStart), G1::OutStr(r));
            const uint64_t chainSeq = FxLogSeq(FxLine::Chain(g_dbgPc0), 'I');
            const uint64_t hopSeq = FxLogSeq(FxLine::Hop(g_dbgPc0, g_pc0), 'I');
            check("RES.05 hop: one INFO \"hopped DebugCameraController <DbgPC_0> -> original PC <PC_0>\"",
                  FxLogCount(FxLine::Hop(g_dbgPc0, g_pc0), 'I') == 1 && FxLogPrefix(G1::kHopLinePrefix) == 1,
                  FxLogDump());
            check("RES.05 hop: the hop line is stamped after the chain line that named DbgPC_0",
                  chainSeq != 0 && hopSeq > chainSeq, FxFmt("chain=%llu hop=%llu", chainSeq, hopSeq) + FxLogDump());
            check("RES.08 hopped rotation: the rotation is PC_0's kRot, not the debug camera's (77.5, 77.5, 0)",
                  FxEq3(FxPyrOf(r.p), kRot), G1::OutStr(r));
        }
        FxReset();
        FxDebugCam(g_dbgFuzzy0);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.05 fuzzy field: a DebugCameraController in another package hops through OriginalControllerPtr",
                  r.rc == Wirbel::TP_OK && FxPoseEq(r.p, kStartPose), G1::OutStr(r));
            check("RES.05 fuzzy field: one hop line, DbgFuzzy_0 -> PC_0",
                  FxLogCount(FxLine::Hop(g_dbgFuzzy0, g_pc0), 'I') == 1 && FxLogPrefix(G1::kHopLinePrefix) == 1,
                  FxLogDump());
        }
        FxReset();
        FxDebugCam(g_decoyPc0);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.05 decoy does not hop: DebugCameraControllerHUD_C is taken as it is -- Npc_0 at kNpcPos",
                  r.rc == Wirbel::TP_OK && FxPoseIs(r.p, kNpcPos), G1::OutStr(r));
            check("RES.05 decoy does not hop: no hop line", FxLogPrefix(G1::kHopLinePrefix) == 0, FxLogDump());
        }
        FxReset();
        FxSetPtr(g_dbgPc0, FxOff::kOriginalController, 0);
        FxDebugCam();
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.05 no original (D12 pin): a debug camera with no original keeps itself -- Spectator_0 at "
                  "kSpecPos, facing the debug camera's own rotation",
                  r.rc == Wirbel::TP_OK && FxPoseIs(r.p, kSpecPos, kDbgRot), G1::OutStr(r));
            check("RES.05 no original (D12 pin): no hop line", FxLogPrefix(G1::kHopLinePrefix) == 0, FxLogDump());
        }
        FxReset();
        FxBreakChain(FxBreak::NoController);
        FxPool(FxPoolVariant::ActiveDcc);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.05 active DCC from the scan: GetPose answers 0 with the hero's pose",
                  r.rc == Wirbel::TP_OK && FxPoseEq(r.p, kStartPose), G1::OutStr(r));
            check("RES.05 active DCC from the scan: the scan line names DbgPC_0, the controller holding LP_0",
                  FxLogCount(FxLine::ScanLocal(g_dbgPc0), 'I') == 1, FxLogDump());
            check("RES.05 active DCC from the scan: one hop line, DbgPC_0 -> PC_0",
                  FxLogCount(FxLine::Hop(g_dbgPc0, g_pc0), 'I') == 1 && FxLogPrefix(G1::kHopLinePrefix) == 1,
                  FxLogDump());
        }

        // RES.06: the pawn is "Pawn" exactly, else "AcknowledgedPawn" exactly; a missing field reads nothing.
        FxReset();
        FxSetPtr(g_pc0, FxOff::kPawn, 0);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.06 Ack fallback: Pawn 0 falls to AcknowledgedPawn, the hero",
                  r.rc == Wirbel::TP_OK && FxPoseEq(r.p, kStartPose), G1::OutStr(r));
        }
        FxReset();
        FxSetPtr(g_pc0, FxOff::kPawn, 0);
        FxSetPtr(g_pc0, FxOff::kAckPawn, 0);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.06 no pawn: Pawn and AcknowledgedPawn 0 answer -3, outputs left as primed",
                  r.rc == Wirbel::TP_ERR_NO_PAWN && FxIsSentinel(r.p) && r.src == kByteSentinel && r.pr &&
                      G1::MapUntouched(r.map, sizeof r.map),
                  G1::OutStr(r));
        }
        FxReset();
        FxPcNoPawn(true);
        FxClearRecorders();
        {
            check("RES.06 PC-1 trap: fixture -- the 8 bytes at PC_0-1 spell Enemy_0",
                  FxGetPtr(g_pc0 - kPcTrapLead, 0) == g_enemy0, FxHex(FxGetPtr(g_pc0 - kPcTrapLead, 0)));
            const G1::PoseRead r = G1::ReadPose();
            check("RES.06 PC-1 trap: a controller with neither pawn field answers -3, never a read at offset -1",
                  r.rc == Wirbel::TP_ERR_NO_PAWN && FxIsSentinel(r.p), G1::OutStr(r));
        }
        FxReset();
        FxLpController(g_pcAckOnly0);
        FxClearRecorders();
        {
            check("RES.06 exact only: fixture -- PcAckOnly_0's PawnClassOverride (first by offset) holds Enemy_0",
                  FxGetPtr(g_pcAckOnly0, FxOff::kPawn) == g_enemy0, FxNameOf(FxGetPtr(g_pcAckOnly0, FxOff::kPawn)));
            const G1::PoseRead r = G1::ReadPose();
            check("RES.06 exact only: PawnClassOverride is passed over -- AcknowledgedPawn's hero, not kEnemyPos",
                  r.rc == Wirbel::TP_OK && FxPoseEq(r.p, kStartPose), G1::OutStr(r));
        }

        // RES.07 / RES.08: the root and its RelativeLocation are required, ControlRotation is not.
        FxReset();
        FxSetPtr(g_hero0, FxOff::kRootComponent, 0);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.07 no root: RootComponent 0 answers -4, outputs left as primed",
                  r.rc == Wirbel::TP_ERR_REFLECTION && FxIsSentinel(r.p) && r.src == kByteSentinel && r.pr,
                  G1::OutStr(r));
        }
        FxReset();
        FxSetClass(g_root0, g_cRootNoRelLoc);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.08 no RelativeLocation: a root with only ComponentLocationCache answers -4",
                  r.rc == Wirbel::TP_ERR_REFLECTION && FxIsSentinel(r.p), G1::OutStr(r));
        }
        FxReset();
        FxSetClass(g_pc0, g_cPcNoRot);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("RES.08 no ControlRotation: GetPose still answers 0, with Pitch/Yaw/Roll 0",
                  r.rc == Wirbel::TP_OK && FxPoseIs(r.p, kStart), G1::OutStr(r));
        }

        // RES.09: nothing is cached between ops.
        FxReset();
        {
            const G1::PoseRead first = G1::ReadPose();
            FxSetPtr(g_pc0, FxOff::kPawn, g_npc0);
            const G1::PoseRead second = G1::ReadPose();
            check("RES.09 re-resolved: the first read is the hero's", first.rc == Wirbel::TP_OK &&
                  FxEq3(FxXyzOf(first.p), kStart), G1::OutStr(first));
            check("RES.09 re-resolved: after PC_0.Pawn becomes Npc_0 the next read is Npc_0's",
                  second.rc == Wirbel::TP_OK && FxEq3(FxXyzOf(second.p), kNpcPos), G1::OutStr(second));
        }

        // RES.10: how many times each op walks the chain -- one per pose it reads.
        const Wirbel::Marker atDest = FxMarker(kDest, G1::kMapA);
        G1::Resolutions("GetPose", 1, Wirbel::TP_OK, nullptr, [] {
            Wirbel::Pose p{};
            char map[G1::kMapBuf] = {};
            uint8_t src = 0;
            bool pr = false;
            return Wirbel::GetPose(p, map, kMapCap, &src, &pr);
        });
        G1::Resolutions("GetPoseAndMovement", 1, Wirbel::TP_OK, nullptr, [] {
            Wirbel::Pose p{};
            char map[G1::kMapBuf] = {};
            uint8_t src = 0;
            Wirbel::MovementState m{};
            return Wirbel::GetPoseAndMovement(p, map, kMapCap, &src, m);
        });
        G1::Resolutions("SaveMarker", 1, Wirbel::TP_OK, nullptr, [] { return Wirbel::SaveMarker(0); });
        G1::Resolutions("BugItSave", 1, Wirbel::TP_OK, nullptr, [] {
            Wirbel::Pose p{};
            char map[G1::kMapBuf] = {};
            uint8_t src = 0;
            bool pr = false;
            return Wirbel::BugItSave(p, map, kMapCap, &src, &pr);
        });
        G1::Resolutions("GetTrainerOffsets", 1, Wirbel::TP_OK, nullptr, [] {
            Wirbel::TrainerOffsets t;
            return Wirbel::GetTrainerOffsets(t);
        });
        G1::Resolutions("SetMouseCursor", 1, Wirbel::TP_OK, nullptr, [] {
            bool shown = false;
            return Wirbel::SetMouseCursor(true, &shown);
        });
        G1::Resolutions("GetMouseCursor", 1, Wirbel::TP_OK, nullptr, [] {
            bool shown = false;
            return Wirbel::GetMouseCursor(&shown);
        });
        G1::Resolutions("RecallLast", 1, Wirbel::TP_OK, [&] { FxPlantLast(atDest); }, [] {
            uint8_t tier = 0;
            return Wirbel::RecallLast(&tier);
        });
        G1::Resolutions("GetPov", 1, Wirbel::TP_OK, nullptr, [] {
            Wirbel::Pov pov;
            return Wirbel::GetPov(pov);
        });
        G1::Resolutions("RecallMarker accepted", 2, Wirbel::TP_OK, [&] { FxPlantMarker(0, atDest); }, [] {
            uint8_t tier = 0;
            return Wirbel::RecallMarker(0, false, &tier);
        });
        G1::Resolutions("RecallExplicit", 2, Wirbel::TP_OK, nullptr, [] {
            uint8_t tier = 0;
            return Wirbel::RecallExplicit(kDest, true, &tier, false);
        });
        G1::Resolutions("BugItGo", 2, Wirbel::TP_OK, [&] { FxPlantBugIt(atDest); }, [] {
            uint8_t tier = 0;
            return Wirbel::BugItGo(&tier);
        });
        G1::Resolutions("TeleportToCursor", 2, Wirbel::TP_OK, [] { FxHit(FxKind::HitCursor, G1::kChannel); }, [] {
            Wirbel::Pose hit{};
            uint8_t tier = 0;
            bool usedCenter = false;
            return Wirbel::TeleportToCursor(kZOffset, G1::kChannel, true, &hit, &tier, &usedCenter);
        });
        G1::Resolutions("RecallExplicit keepUndo", 1, Wirbel::TP_OK, nullptr, [] {
            uint8_t tier = 0;
            return Wirbel::RecallExplicit(kDest, true, &tier, true);
        });
        G1::Resolutions("TeleportRelative", 4, Wirbel::TP_OK, nullptr, [] {
            Wirbel::Pose landed{};
            uint8_t tier = 0;
            bool known = false;
            return Wirbel::TeleportRelative(G1::kStepUu, true, landed, &tier, &known);
        });
        G1::Resolutions("RecallMarker on an empty slot", 0, Wirbel::TP_ERR_EMPTY_MARKER, nullptr, [] {
            uint8_t tier = 0;
            return Wirbel::RecallMarker(0, false, &tier);
        });
        G1::Resolutions("RecallMarker refused for another map", 0, Wirbel::TP_ERR_MAP_MISMATCH,
                        [] { FxPlantMarker(0, FxMarker(kDest, "Map_B")); }, [] {
                            uint8_t tier = 0;
                            return Wirbel::RecallMarker(0, false, &tier);
                        });
        G1::Resolutions("RecallMarker refused as parent-relative", 0, Wirbel::TP_ERR_PARENT_RELATIVE,
                        [] { FxPlantMarker(0, FxMarker(kDest, G1::kMapA, true)); }, [] {
                            uint8_t tier = 0;
                            return Wirbel::RecallMarker(0, false, &tier);
                        });
        G1::Resolutions("BugItGo with no BugIt", 0, Wirbel::TP_ERR_EMPTY_MARKER, nullptr, [] {
            uint8_t tier = 0;
            return Wirbel::BugItGo(&tier);
        });
        G1::Resolutions("BugItGo refused as parent-relative", 0, Wirbel::TP_ERR_PARENT_RELATIVE,
                        [] { FxPlantBugIt(FxMarker(kDest, G1::kMapA, true)); }, [] {
                            uint8_t tier = 0;
                            return Wirbel::BugItGo(&tier);
                        });
        G1::Resolutions("RecallLast with no last", 0, Wirbel::TP_ERR_EMPTY_MARKER, nullptr, [] {
            uint8_t tier = 0;
            return Wirbel::RecallLast(&tier);
        });
        G1::Resolutions("SaveMarker on a bad slot", 0, Wirbel::TP_ERR_EMPTY_MARKER, nullptr,
                        [] { return Wirbel::SaveMarker(-1); });
        G1::Resolutions("GetMarker", 0, Wirbel::TP_OK, [&] { FxPlantMarker(0, atDest); }, [] {
            Wirbel::Marker m{};
            return Wirbel::GetMarker(0, m);
        });
        G1::Resolutions("ClearMarker", 0, Wirbel::TP_OK, [&] { FxPlantMarker(0, atDest); },
                        [] { return Wirbel::ClearMarker(0); });
        G1::Resolutions("GetLast", 0, Wirbel::TP_OK, [&] { FxPlantLast(atDest); }, [] {
            Wirbel::Marker m{};
            return Wirbel::GetLast(m);
        });
        G1::Resolutions("GetCurrentMapName (0 = it answered true)", 0, Wirbel::TP_OK, nullptr, [] {
            char map[G1::kMapBuf] = {};
            return Wirbel::GetCurrentMapName(map, kMapCap) ? Wirbel::TP_OK : Wirbel::TP_ERR_NOT_INIT;
        });
    }

    // ── POSE / MVS ──
    {
        blk("POSE / MVS - the pose read (attached or not), its outputs on failure, the movement state");

        FxReset();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("POSE.01 unattached: GetPose answers 0", r.rc == Wirbel::TP_OK, G1::OutStr(r));
            check("POSE.01 unattached: the pose is RelativeLocation kStart with ControlRotation kRot",
                  FxPoseEq(r.p, kStartPose), G1::OutStr(r));
            check("POSE.01 unattached: *src 0 (raw) and *pr false, both written over their primed values",
                  r.src == 0 && !r.pr, G1::OutStr(r));
            check("POSE.01 unattached: the map is \"Map_A\"", G1::MapIs(r.map, sizeof r.map, G1::kMapA),
                  G1::OutStr(r));
            check("POSE.01 unattached: no engine call -- an unattached root's RelativeLocation is world space",
                  FxCountAll() == 0, FxCallsStr(FxCalls()));
            check("POSE.05 rotation: Pitch/Yaw/Roll are PC_0's ControlRotation at +0x38",
                  FxEq3(FxPyrOf(r.p), kRot), G1::OutStr(r));
        }

        FxReset();
        FxAttach(true);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            const FxCall get = FxNth(FxKind::GetLoc);
            check("POSE.02 attached: GetPose answers 0 with K2_GetActorLocation's world position and kRot",
                  r.rc == Wirbel::TP_OK && FxPoseIs(r.p, kAttachedWorld, kRot), G1::OutStr(r));
            check("POSE.02 attached: *src 1 (invoke) and *pr false", r.src == 1 && !r.pr, G1::OutStr(r));
            check("POSE.02 attached: exactly one call, K2_GetActorLocation on Hero_0 with a 24-byte buffer",
                  FxCountAll() == 1 && FxCountOn(FxKind::GetLoc, g_hero0) == 1 &&
                      get.size == static_cast<uint32_t>(kVecD),
                  FxCallsStr(FxCalls()));
            check("POSE.02 attached: no WARN", FxTeleportLines('W') == 0, FxLogDump());
        }
        for (const int32_t failRc : { static_cast<int32_t>(Wirbel::TP_ERR_INVOKE),
                                      static_cast<int32_t>(Wirbel::TP_ERR_NOT_INIT) }) {
            FxReset();
            FxAttach(true);
            FxRcs(FxKind::GetLoc, { failRc });
            FxClearRecorders();
            const G1::PoseRead r = G1::ReadPose();
            check(FxFmt("POSE.03 attached, getter rc %d: GetPose still answers 0 with the raw kStart", failRc).c_str(),
                  r.rc == Wirbel::TP_OK && FxPoseEq(r.p, kStartPose), G1::OutStr(r));
            check(FxFmt("POSE.03 attached, getter rc %d: *src 0 and *pr true -- the numbers are parent-relative",
                        failRc)
                      .c_str(),
                  r.src == 0 && r.pr, G1::OutStr(r));
            check(FxFmt("POSE.03 attached, getter rc %d: exactly one WARN \"attached pawn but K2_GetActorLocation "
                        "failed ...\"",
                        failRc)
                      .c_str(),
                  FxLogCount(FxText::kAttachedFail, 'W') == 1 && FxLogCount(FxText::kAttachedFail) == 1, FxLogDump());
            check(FxFmt("POSE.03 attached, getter rc %d: one K2_GetActorLocation call", failRc).c_str(),
                  FxCount(FxKind::GetLoc) == 1 && FxCountAll() == 1, FxCallsStr(FxCalls()));
        }
        {
            struct Shape {
                const char* what;
                FxGetLocShape shape;
                int calls;   // a getter with no return is never invoked; one whose return is too short is
            };
            const Shape shapes[] = {
                { "getter missing", FxGetLocShape::Missing, 0 },
                { "getter with no return", FxGetLocShape::NoReturn, 0 },
                { "getter returning 4 bytes", FxGetLocShape::Scalar, 1 },
            };
            for (const auto& s : shapes) {
                FxReset();
                FxAttach(true);
                FxActorFns(FxActorFnList(FxActorFnOpts{ .getLoc = s.shape }));
                FxClearRecorders();
                const G1::PoseRead r = G1::ReadPose();
                check(FxFmt("POSE.03 attached, %s: GetPose answers 0 with the raw kStart, *src 0, *pr true", s.what)
                          .c_str(),
                      r.rc == Wirbel::TP_OK && FxPoseEq(r.p, kStartPose) && r.src == 0 && r.pr, G1::OutStr(r));
                check(FxFmt("POSE.03 attached, %s: exactly one WARN \"attached pawn but K2_GetActorLocation failed "
                            "...\"",
                            s.what)
                          .c_str(),
                      FxLogCount(FxText::kAttachedFail, 'W') == 1, FxLogDump());
                check(FxFmt("POSE.03 attached, %s: %d K2_GetActorLocation call(s)", s.what, s.calls).c_str(),
                      FxCount(FxKind::GetLoc) == s.calls && FxCountAll() == s.calls, FxCallsStr(FxCalls()));
            }
        }

        // POSE.04: RootEdge's RelativeLocation lies past its committed page; its AttachParent does not.
        FxReset();
        FxSetPtr(g_hero0, FxOff::kRootComponent, g_rootEdge);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("POSE.04 raw read fails: an unreadable RelativeLocation answers -4",
                  r.rc == Wirbel::TP_ERR_REFLECTION, G1::OutStr(r));
            check("POSE.04 raw read fails: *src 0 and *pr false were already written over the primed 0xAB / true",
                  r.src == 0 && !r.pr, G1::OutStr(r));
            check("POSE.04 raw read fails: the pose and the map are left as primed",
                  FxIsSentinel(r.p) && G1::MapUntouched(r.map, sizeof r.map), G1::OutStr(r));
        }
        FxReset();
        FxSetPtr(g_hero0, FxOff::kRootComponent, g_rootEdge);
        FxAttach(true, g_rootEdge);
        FxRcs(FxKind::GetLoc, { Wirbel::TP_ERR_INVOKE });
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("POSE.04 D3 pin: attached, getter failed, raw read failed -- -4 with *pr already true",
                  r.rc == Wirbel::TP_ERR_REFLECTION && r.pr && FxIsSentinel(r.p), G1::OutStr(r));
            check("POSE.04 D3 pin: the parent-relative WARN came before the failure",
                  FxLogCount(FxText::kAttachedFail, 'W') == 1, FxLogDump());
        }
        FxReset();
        FxSetPtr(g_hero0, FxOff::kRootComponent, g_rootEdge);
        FxAttach(true, g_rootEdge);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("POSE.04 attached ok skips the raw read: 0 with kAttachedWorld though RelativeLocation is unreadable",
                  r.rc == Wirbel::TP_OK && FxPoseIs(r.p, kAttachedWorld, kRot) && r.src == 1 && !r.pr, G1::OutStr(r));
        }

        // POSE.06: the map name is truncated to cap-1 and terminated, and never written without room.
        FxReset();
        {
            const G1::PoseRead r = G1::ReadPose(G1::kShortCap);
            check("POSE.06 map cap 4: GetPose answers 0 with \"Map\", terminated inside the cap",
                  r.rc == Wirbel::TP_OK && G1::MapIs(r.map, sizeof r.map, G1::kMapAShort), G1::OutStr(r));
            check("POSE.06 map cap 4: the byte at the cap is untouched", r.map[G1::kShortCap] == kMapFill,
                  G1::OutStr(r));
            Wirbel::Pose p = FxSentinelPose();
            const int32_t rc = Wirbel::GetPose(p, nullptr, kMapCap, nullptr);
            check("POSE.06 map null: GetPose answers 0 with the pose", rc == Wirbel::TP_OK && FxPoseEq(p, kStartPose),
                  FxS(rc) + " " + FxPoseStr(p));
            const G1::PoseRead zero = G1::ReadPose(0);
            check("POSE.06 map cap 0: GetPose answers 0 and leaves the buffer untouched",
                  zero.rc == Wirbel::TP_OK && G1::MapUntouched(zero.map, sizeof zero.map), G1::OutStr(zero));
        }
        FxReset();
        FxWorld(g_worldLong);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose(kMapCap);
            const size_t len = strnlen(r.map, sizeof r.map);
            bool allL = len == static_cast<size_t>(kMapCap - 1);
            for (size_t i = 0; allL && i < len; ++i) allL = r.map[i] == 'L';
            bool guardIntact = true;
            for (size_t i = static_cast<size_t>(kMapCap); i < sizeof r.map; ++i)
                guardIntact = guardIntact && r.map[i] == kMapFill;
            check("POSE.06 a 140-character map at cap 128: 127 'L' and the terminator",
                  r.rc == Wirbel::TP_OK && allL, FxS(static_cast<long long>(len)) + " " + G1::OutStr(r));
            check("POSE.06 a 140-character map at cap 128: nothing written past the cap", guardIntact, G1::OutStr(r));
        }

        // POSE.07: a failed resolution writes none of the outputs.
        FxReset();
        FxNoGWorld();
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("POSE.07 failure leaves outputs, GWorld 0: -1 with pose, *src 0xAB, *pr true and map all as primed",
                  r.rc == Wirbel::TP_ERR_NOT_INIT && FxIsSentinel(r.p) && r.src == kByteSentinel && r.pr &&
                      G1::MapUntouched(r.map, sizeof r.map),
                  G1::OutStr(r));
        }
        FxReset();
        FxSetPtr(g_pc0, FxOff::kPawn, 0);
        FxSetPtr(g_pc0, FxOff::kAckPawn, 0);
        FxClearRecorders();
        {
            const G1::PoseRead r = G1::ReadPose();
            check("POSE.07 failure leaves outputs, no pawn: -3 with pose, *src 0xAB, *pr true and map all as primed",
                  r.rc == Wirbel::TP_ERR_NO_PAWN && FxIsSentinel(r.p) && r.src == kByteSentinel && r.pr &&
                      G1::MapUntouched(r.map, sizeof r.map),
                  G1::OutStr(r));
        }

        // POSE.08 / MVS: the movement read, over a MovementState primed with junk in every field.
        FxReset();
        {
            const G1::MoveRead r = G1::ReadMove();
            const Wirbel::MovementState& m = r.m;
            check("POSE.08 GetPoseAndMovement: answers 0 with the pose kStart / kRot",
                  r.rc == Wirbel::TP_OK && FxPoseEq(r.p, kStartPose), FxS(r.rc) + " " + FxPoseStr(r.p));
            check("MVS.01 pawn: PawnAddr is Hero_0", m.PawnAddr == g_hero0, G1::MoveStr(m));
            check("MVS.03 velocity: HasMovement, Velocity kVel (0, 300, 400) and Speed exactly 500",
                  m.HasMovement && FxEq3(G1::VelOf(m), kVel) && m.Speed == kSpeed, G1::MoveStr(m));
            check("POSE.08 GetPoseAndMovement: the Velocity owner is CMC_0, at 0x30",
                  m.VelOwnerAddr == g_cmc0 && m.VelFieldOffset == FxOff::kVelocity, G1::MoveStr(m));
            check("POSE.08 GetPoseAndMovement: Acceleration kAcc, owned by CMC_0 at 0x48",
                  FxEq3(G1::AccOf(m), kAcc) && m.AccOwnerAddr == g_cmc0 && m.AccFieldOffset == FxOff::kAcceleration,
                  G1::MoveStr(m));
            check("POSE.08 GetPoseAndMovement: the location owner is Root_0, RelativeLocation at 0x40",
                  m.LocOwnerAddr == g_root0 && m.LocFieldOffset == FxOff::kRelativeLocation, G1::MoveStr(m));
            check("POSE.08 GetPoseAndMovement: the rotation owner is PC_0, ControlRotation at 0x38",
                  m.RotOwnerAddr == g_pc0 && m.RotFieldOffset == FxOff::kControlRotation, G1::MoveStr(m));
            check("POSE.08 GetPoseAndMovement: no engine call and no WARN",
                  FxCountAll() == 0 && FxTeleportLines('W') == 0, FxCallsStr(FxCalls()) + FxLogDump());
        }
        FxReset();
        FxDebugCam();
        FxClearRecorders();
        {
            const G1::MoveRead r = G1::ReadMove();
            check("POSE.08 debug cam active: the rotation owner is the hopped PC_0, not DbgPC_0",
                  r.rc == Wirbel::TP_OK && r.m.RotOwnerAddr == g_pc0 && r.m.RotFieldOffset == FxOff::kControlRotation,
                  FxS(r.rc) + " " + G1::MoveStr(r.m));
            check("POSE.08 debug cam active: the pawn is still the hero", r.m.PawnAddr == g_hero0, G1::MoveStr(r.m));
        }
        FxReset();
        FxNoGWorld();
        FxClearRecorders();
        {
            const G1::MoveRead r = G1::ReadMove();
            check("POSE.08 GWorld 0: GetPoseAndMovement answers -1", r.rc == Wirbel::TP_ERR_NOT_INIT, FxS(r.rc));
            check("POSE.08 GWorld 0: every MovementState field is back at its default, none left as primed",
                  G1::MoveEq(r.m, Wirbel::MovementState{}), G1::MoveStr(r.m));
        }
        FxReset();
        FxSetPtr(g_hero0, FxOff::kCharacterMovement, 0);
        FxClearRecorders();
        {
            const G1::MoveRead r = G1::ReadMove();
            const Wirbel::MovementState& m = r.m;
            check("POSE.08 LocOwner without CMC: answers 0, the location owner is still Root_0 at 0x40",
                  r.rc == Wirbel::TP_OK && m.LocOwnerAddr == g_root0 && m.LocFieldOffset == FxOff::kRelativeLocation,
                  FxS(r.rc) + " " + G1::MoveStr(m));
            check("POSE.08 LocOwner without CMC: HasMovement false", !m.HasMovement, G1::MoveStr(m));
            check("MVS.02 no CMC: Velocity and Acceleration owners 0, offsets -1, values 0",
                  m.VelOwnerAddr == 0 && m.VelFieldOffset == -1 && m.AccOwnerAddr == 0 && m.AccFieldOffset == -1 &&
                      m.VelX == 0 && m.VelY == 0 && m.VelZ == 0 && m.AccX == 0 && m.AccY == 0 && m.AccZ == 0 &&
                      m.Speed == 0,
                  G1::MoveStr(m));
            check("MVS.02 no CMC: the pawn is still reported", m.PawnAddr == g_hero0, G1::MoveStr(m));
            check("MVS.02 no CMC: exactly one INFO \"no CharacterMovement on pawn — velocity/acceleration "
                  "unavailable\"",
                  FxLogCount(FxText::kNoCmcPose, 'I') == 1 && FxLogCount(FxText::kNoCmcPose) == 1, FxLogDump());
        }
        FxReset();
        FxSetPtr(g_hero0, FxOff::kCharacterMovement, g_cmcNoVel0);
        FxClearRecorders();
        {
            const G1::MoveRead r = G1::ReadMove();
            const Wirbel::MovementState& m = r.m;
            const std::string noVel =
                FxFmt("Teleport: CharacterMovement 0x%llX has no reflected Velocity field",
                      static_cast<unsigned long long>(g_cmcNoVel0));
            check("MVS.04 acceleration alone: answers 0 with HasMovement false -- Acceleration does not set it",
                  r.rc == Wirbel::TP_OK && !m.HasMovement, FxS(r.rc) + " " + G1::MoveStr(m));
            check("MVS.04 acceleration alone: Acceleration kAcc owned by CmcNoVel_0 at 0x48",
                  FxEq3(G1::AccOf(m), kAcc) && m.AccOwnerAddr == g_cmcNoVel0 &&
                      m.AccFieldOffset == FxOff::kAcceleration,
                  G1::MoveStr(m));
            check("MVS.04 acceleration alone: no Velocity owner, offset -1, speed 0",
                  m.VelOwnerAddr == 0 && m.VelFieldOffset == -1 && m.Speed == 0, G1::MoveStr(m));
            check("MVS.05 no Velocity line: exactly one INFO \"CharacterMovement <CmcNoVel_0> has no reflected "
                  "Velocity field\"",
                  FxLogCount(noVel, 'I') == 1 && FxLogCount(noVel) == 1, FxLogDump());
            check("MVS.05 no Velocity line: not the no-CMC line", FxLogCount(FxText::kNoCmcPose) == 0, FxLogDump());
        }
    }

    // ── TRN ──
    {
        blk("TRN - the standalone-trainer bundle: the baked chain, its partial failures, the offsets off the pawn");

        FxReset();
        FxNoGWorld();
        FxClearRecorders();
        {
            Wirbel::TrainerOffsets t = G1::JunkTrainer();
            const int32_t rc = Wirbel::GetTrainerOffsets(t);
            check("TRN.01 reset: GWorld 0 answers -1", rc == Wirbel::TP_ERR_NOT_INIT, FxS(rc));
            check("TRN.01 reset: every field is back at its default, none left as primed",
                  G1::TrainerEq(t, Wirbel::TrainerOffsets{}), G1::TrainerStr(t));
        }

        FxReset();
        {
            const uintptr_t watched[] = { g_worldA, g_gi0, g_lp0, g_pc0, g_hero0, g_root0, g_cmc0, g_cam0 };
            std::vector<std::vector<uint8_t>> before;
            for (const uintptr_t o : watched) before.push_back(G1::BodyOf(o));
            FxClearRecorders();
            Wirbel::TrainerOffsets t;
            const int32_t rc = Wirbel::GetTrainerOffsets(t);
            bool unchanged = true;
            std::string changed;
            for (size_t i = 0; i < std::size(watched); ++i) {
                if (G1::BodyOf(watched[i]) != before[i]) {
                    unchanged = false;
                    changed += " " + FxNameOf(watched[i]);
                }
            }
            check("TRN.02 chain: answers 0 with five hops", rc == Wirbel::TP_OK && t.ChainCount == G1::kChainHops,
                  FxS(rc) + " " + G1::TrainerStr(t));
            check("TRN.02 chain: the hops are (OwningGameInstance 0x30) (LocalPlayers 0x38) (LocalPlayers[0] 0) "
                  "(PlayerController 0x30) (Pawn 0x30), each dereferenced",
                  G1::HopIs(t.Chain[0], "OwningGameInstance", FxOff::kOwningGameInstance) &&
                      G1::HopIs(t.Chain[1], "LocalPlayers", FxOff::kLocalPlayers) &&
                      G1::HopIs(t.Chain[2], "LocalPlayers[0]", 0) &&
                      G1::HopIs(t.Chain[3], "PlayerController", FxOff::kPlayerController) &&
                      G1::HopIs(t.Chain[G1::kPawnHop], "Pawn", FxOff::kPawn),
                  G1::TrainerStr(t));
            check("TRN.02 chain: no engine call", FxCountAll() == 0, FxCallsStr(FxCalls()));
            check("TRN.02 chain: nothing written to the world, controller, pawn, root, movement or camera", unchanged,
                  changed);

            check("TRN.05 offsets: PawnToRoot 0x28, RootToRelLoc 0x40, FVectorWidth 24",
                  t.PawnToRoot == FxOff::kRootComponent && t.RootToRelLoc == FxOff::kRelativeLocation &&
                      t.FVectorWidth == kVecD,
                  G1::TrainerStr(t));
            check("TRN.05 offsets: CtrlRotOff 0x38, CtrlRotSize 24",
                  t.CtrlRotOff == FxOff::kControlRotation && t.CtrlRotSize == kVecD, G1::TrainerStr(t));
            check("TRN.05 offsets: PawnToController 0x30, PawnToCmc 0x38",
                  t.PawnToController == FxOff::kPawnController && t.PawnToCmc == FxOff::kCharacterMovement,
                  G1::TrainerStr(t));
            check("TRN.05 offsets: MaxWalkSpeed 0x60, GravityScale 0x64, JumpZVelocity 0x68",
                  t.WalkSpeedOff == FxOff::kMaxWalkSpeed && t.GravityOff == FxOff::kGravityScale &&
                      t.JumpOff == FxOff::kJumpZVelocity,
                  G1::TrainerStr(t));
            check("TRN.05 offsets: MovementMode 0x6D, not CustomMovementMode at 0x6C",
                  t.MoveModeOff == FxOff::kMovementMode, G1::TrainerStr(t));
            check("TRN.05 offsets: Velocity 0x30, 24 bytes",
                  t.VelocityOff == FxOff::kVelocity && t.VelocitySize == kVecD, G1::TrainerStr(t));
        }

        // TRN.03: each later failure returns -4 with the hops pushed so far, though the pose still resolves.
        {
            struct Row {
                const char* what;
                int32_t hops;
                std::function<void()> setup;
            };
            const Row rows[] = {
                { "World_Bare (no OwningGameInstance field)", 0, [] { FxWorld(g_worldBare); } },
                { "OwningGameInstance=0", G1::kGiHops, [] { FxBreakChain(FxBreak::NoGameInstance); } },
                { "GI_Bare (no LocalPlayers field)", G1::kGiHops,
                  [] { FxSetPtr(g_worldA, FxOff::kOwningGameInstance, g_giBare); } },
                { "LocalPlayers Num 0", G1::kLpHops, [] { FxBreakChain(FxBreak::NoLocalPlayers); } },
                { "LocalPlayers[0]=0", G1::kLpHops, [] { FxBreakChain(FxBreak::NullLocalPlayer); } },
                { "LP_0 a LocalPlayerBare (no PlayerController field)", G1::kLpHops,
                  [] { FxSetClass(g_lp0, g_cLocalPlayerBare); } },
                { "LP.PlayerController=DbgFuzzy_0 (no Pawn field)", G1::kPcHops, [] { FxDebugCam(g_dbgFuzzy0); } },
            };
            for (const auto& row : rows) {
                FxReset();
                row.setup();
                FxClearRecorders();
                Wirbel::TrainerOffsets t;
                const int32_t rc = Wirbel::GetTrainerOffsets(t);
                const G1::PoseRead r = G1::ReadPose();
                check(FxFmt("TRN.03 %s: GetTrainerOffsets answers -4 with %d hop(s) left in out", row.what, row.hops)
                          .c_str(),
                      rc == Wirbel::TP_ERR_REFLECTION && t.ChainCount == row.hops, FxS(rc) + " " + G1::TrainerStr(t));
                check(FxFmt("TRN.03 %s: GetPose in the same state answers 0 with the hero's pose", row.what).c_str(),
                      r.rc == Wirbel::TP_OK && FxPoseEq(r.p, kStartPose), G1::OutStr(r));
            }
        }
        FxReset();
        FxBreakChain(FxBreak::NoController);
        FxClearRecorders();
        {
            Wirbel::TrainerOffsets t;
            const int32_t rc = Wirbel::GetTrainerOffsets(t);
            check("TRN.03 scan-only PC (pin): LP.PlayerController 0 still answers 0 with five hops",
                  rc == Wirbel::TP_OK && t.ChainCount == G1::kChainHops, FxS(rc) + " " + G1::TrainerStr(t));
            check("TRN.03 scan-only PC (pin): the pawn hop is (Pawn 0x30), from the scanned PC's class",
                  G1::HopIs(t.Chain[G1::kPawnHop], "Pawn", FxOff::kPawn), G1::TrainerStr(t));
        }

        FxReset();
        FxDebugCam();
        FxSetPtr(g_dbgPc0, FxOff::kOriginalController, g_pcAckOnly0);
        FxClearRecorders();
        {
            Wirbel::TrainerOffsets t;
            const int32_t rc = Wirbel::GetTrainerOffsets(t);
            check("TRN.04 raw PC class: answers 0 with five hops",
                  rc == Wirbel::TP_OK && t.ChainCount == G1::kChainHops, FxS(rc) + " " + G1::TrainerStr(t));
            check("TRN.04 raw PC class: the pawn hop comes from DbgPC_0's class (Pawn 0x30), not the hopped "
                  "PcAckOnly_0's (AcknowledgedPawn 0x50)",
                  G1::HopIs(t.Chain[G1::kPawnHop], "Pawn", FxOff::kPawn), G1::TrainerStr(t));
        }

        FxReset();
        FxSetPtr(g_hero0, FxOff::kCharacterMovement, g_cmcFuzzy0);
        FxClearRecorders();
        {
            Wirbel::TrainerOffsets t;
            const int32_t rc = Wirbel::GetTrainerOffsets(t);
            check("TRN.05 fuzzy CMC: answers 0", rc == Wirbel::TP_OK, FxS(rc));
            check("TRN.05 fuzzy CMC: BaseWalkSpeed 0x38 past the WalkSpeedCurve object, GravityScale 0x3C, "
                  "JumpZVelocity 0x40",
                  t.WalkSpeedOff == FxOff::kBaseWalkSpeed && t.GravityOff == FxOff::kFuzzyGravity &&
                      t.JumpOff == FxOff::kFuzzyJump,
                  G1::TrainerStr(t));
            check("TRN.05 fuzzy CMC: GroundMovementMode 0x45, past CustomMovementMode",
                  t.MoveModeOff == FxOff::kGroundMovementMode, G1::TrainerStr(t));
            check("TRN.05 fuzzy CMC: LastVelocity 0x50 / 24, past the VelocityCurve object",
                  t.VelocityOff == FxOff::kLastVelocity && t.VelocitySize == kVecD, G1::TrainerStr(t));
        }

        FxReset();
        FxSetPtr(g_hero0, FxOff::kCharacterMovement, 0);
        FxClearRecorders();
        {
            Wirbel::TrainerOffsets t;
            const int32_t rc = Wirbel::GetTrainerOffsets(t);
            check("TRN.05 no CMC: answers 0 with PawnToCmc still 0x38",
                  rc == Wirbel::TP_OK && t.PawnToCmc == FxOff::kCharacterMovement, FxS(rc) + " " + G1::TrainerStr(t));
            check("TRN.05 no CMC: every movement field -1, VelocitySize 0",
                  t.WalkSpeedOff == -1 && t.GravityOff == -1 && t.JumpOff == -1 && t.MoveModeOff == -1 &&
                      t.VelocityOff == -1 && t.VelocitySize == 0,
                  G1::TrainerStr(t));
        }

        FxReset();
        FxUE4World();
        FxClearRecorders();
        {
            Wirbel::TrainerOffsets t;
            const int32_t rc = Wirbel::GetTrainerOffsets(t);
            check("TRN.05 UE4: answers 0 with five hops, the pawn hop (Pawn 0x30) from PCF_0's class",
                  rc == Wirbel::TP_OK && t.ChainCount == G1::kChainHops &&
                      G1::HopIs(t.Chain[G1::kPawnHop], "Pawn", FxOff::kPawn),
                  FxS(rc) + " " + G1::TrainerStr(t));
            check("TRN.05 UE4: FVectorWidth 12 and CtrlRotSize 12, from the float fields' own sizes",
                  t.FVectorWidth == kVecF && t.CtrlRotSize == kVecF, G1::TrainerStr(t));
            // The float CMC: a UE4 trainer's fly writes Velocity at this width.
            check("TRN.05 UE4: the float CMC's Velocity at 0x30 is 12 bytes",
                  t.VelocityOff == FxOff::kVelocity && t.VelocitySize == kVecF, G1::TrainerStr(t));
            check("TRN.05 UE4: the other offsets are the same fields' -- root 0x28, RelativeLocation 0x40, "
                  "ControlRotation 0x38, CMC 0x38, MovementMode 0x6D",
                  t.PawnToRoot == FxOff::kRootComponent && t.RootToRelLoc == FxOff::kRelativeLocation &&
                      t.CtrlRotOff == FxOff::kControlRotation && t.PawnToCmc == FxOff::kCharacterMovement &&
                      t.MoveModeOff == FxOff::kMovementMode,
                  G1::TrainerStr(t));
        }
    }
}

// ══ Blocks_G2: the camera POV (POV.01-13, with the liveness threshold the gate is asked at) and the mouse cursor
// (MCUR.01-06, UPROP.01) ══════════════════════════════════════════════════════════════════════════════════════════
// Every helper sits in namespace FxG2: the blocks share one translation unit with Wirbel.cpp and the other block
// groups, and Wirbel's unnamed namespace already spends most plain names at global scope.

namespace FxG2 {

// The code a game-thread invoke answers when it did not complete (still queued at its timeout).
constexpr int32_t kInvokeFail = Wirbel::TP_ERR_INVOKE;
constexpr int32_t kUnresolved = -1;   // an offset reflection did not resolve
constexpr double kOrigin[3] = { 0.0, 0.0, 0.0 };

// ── POV ──────────────────────────────────────────────────────────────────────────────────────────────────────────
// Prefilled into every Pov field, so a check sees which ones GetPov wrote: no resolve produces these.
constexpr uintptr_t kJunkOwner = 0x7777;
constexpr int32_t kJunkOff = 0x7777;
// The cached POV's offsets within a camera manager, as the fixture reflects them, at both FVector widths.
constexpr int32_t kLocOff = FxOff::kPovBase;
constexpr int32_t kRotOff = FxOff::kPovBase + FxOff::kPovRot;
constexpr int32_t kFovOff = FxOff::kPovBase + FxOff::kPovFov;
constexpr int32_t kRotOffF = FxOff::kPovBase + FxOff::kPovRotF;
constexpr int32_t kFovOffF = FxOff::kPovBase + FxOff::kPovFovF;
constexpr const char* kGameCtrlClass = "BP_GameCtrl_C";   // PC_0's class, as the no-camera WARN names it

static Wirbel::Pov JunkPov() {
    Wirbel::Pov p;
    p.Cam = FxSentinelPose();
    p.Fov = kPoseSentinel;
    p.HasPawn = true;
    p.Pawn = FxSentinelPose();
    p.Source = kByteSentinel;
    p.CamOwnerAddr = kJunkOwner;
    p.CamLocFieldOffset = kJunkOff;
    p.CamRotFieldOffset = kJunkOff;
    p.CamFovFieldOffset = kJunkOff;
    return p;
}
static bool PovIsDefault(const Wirbel::Pov& p) {
    const Wirbel::Pov d{};
    return FxPoseEq(p.Cam, d.Cam) && p.Fov == d.Fov && p.HasPawn == d.HasPawn && FxPoseEq(p.Pawn, d.Pawn) &&
           p.Source == d.Source && p.CamOwnerAddr == d.CamOwnerAddr && p.CamLocFieldOffset == d.CamLocFieldOffset &&
           p.CamRotFieldOffset == d.CamRotFieldOffset && p.CamFovFieldOffset == d.CamFovFieldOffset;
}
static std::string PovStr(const Wirbel::Pov& p) {
    return FxFmt("cam=%s fov=%.9g hasPawn=%d pawn=%s src=%u owner=%s offs=%d/%d/%d", FxPoseStr(p.Cam).c_str(), p.Fov,
                 p.HasPawn ? 1 : 0, FxPoseStr(p.Pawn).c_str(), static_cast<unsigned>(p.Source),
                 FxHex(p.CamOwnerAddr).c_str(), p.CamLocFieldOffset, p.CamRotFieldOffset, p.CamFovFieldOffset);
}
static std::string RcStr(int32_t rc) { return FxFmt("rc=%d", rc); }
static bool CamIs(const Wirbel::Pov& p, const double loc[3], const double rot[3]) { return FxPoseIs(p.Cam, loc, rot); }
static bool OffsetsAre(const Wirbel::Pov& p, int32_t loc, int32_t rot, int32_t fov) {
    return p.CamLocFieldOffset == loc && p.CamRotFieldOffset == rot && p.CamFovFieldOffset == fov;
}
// The liveness gate was asked exactly once, at Stark's own default threshold: a caller passing a longer one would treat
// a paused game as ticking and answer the same, so only the recorded argument tells the two apart.
static bool GateAtDefault() {
    const auto a = FxRespArgs();
    return a.size() == 1 && a[0] == Stark::kStallThresholdMs;
}
static std::string GateStr() {
    std::string out = "thresholds:";
    for (int32_t v : FxRespArgs()) out += " " + FxS(v);
    return out;
}
// Wirbel.cpp's own POV lines, from its format strings.
static std::string PovLine(const double loc[3], const double rot[3], double fov, bool hasPawn, bool raw) {
    return FxFmt("Teleport: POV cam=(%.1f, %.1f, %.1f) rot=(%.1f, %.1f, %.1f) fov=%.1f hasPawn=%d src=%s", loc[0],
                 loc[1], loc[2], rot[0], rot[1], rot[2], fov, hasPawn ? 1 : 0, raw ? "raw" : "invoke");
}
static std::string PovNoCam(int camOff, const char* pcClass) {
    return FxFmt("Teleport: POV failed — PlayerCameraManager unresolved (camOff=%d) on PC class '%s'", camOff, pcClass);
}
static std::string PovFailed(const char* camClass, uintptr_t cam, int hasLoc, int hasRot, int cacheOff) {
    return FxFmt("Teleport: POV failed on '%s' (cam=0x%llX) — getters found loc=%d rot=%d, raw cached-POV read also "
                 "failed. CameraCache off=%d.",
                 camClass, static_cast<unsigned long long>(cam), hasLoc, hasRot, cacheOff);
}

// One GetPov from a junk-filled Pov.
struct PovRun {
    int32_t rc = 0;
    Wirbel::Pov p{};
};
static PovRun RunPov() {
    PovRun r;
    r.p = JunkPov();
    r.rc = Wirbel::GetPov(r.p);
    return r;
}

static void PovBlock() {
    blk("POV - camera point of view: the getters, the raw cached POV, its offsets and the pawn delta");

    // POV.01: an active debug camera's own camera manager is what is on screen, so the camera is NOT hopped; only the
    // pawn delta goes through to the gameplay controller's pawn.
    {
        FxReset();
        FxDebugCam();
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.01 no hop for the camera: GetPov answers 0", r.rc == Wirbel::TP_OK, RcStr(r.rc) + FxLogDump());
        check("POV.01 no hop for the camera: CamOwnerAddr is the debug camera's own manager CamDbg_0",
              r.p.CamOwnerAddr == g_camDbg0, FxHex(r.p.CamOwnerAddr));
        check("POV.01 no hop for the camera: Cam is CamDbg_0's getters (kCamDbgLoc, kCamDbgRot)",
              CamIs(r.p, kCamDbgLoc, kCamDbgRot), PovStr(r.p));
        check("POV.01 no hop for the camera: the three getters ran on CamDbg_0 and none on Cam_0",
              FxCountOn(FxKind::CamLoc, g_camDbg0) == 1 && FxCountOn(FxKind::CamRot, g_camDbg0) == 1 &&
                  FxCountOn(FxKind::Fov, g_camDbg0) == 1 && FxCountOn(FxKind::CamLoc, g_cam0) == 0 &&
                  FxCountOn(FxKind::CamRot, g_cam0) == 0 && FxCountOn(FxKind::Fov, g_cam0) == 0,
              FxCallsStr(FxCalls()));
        check("POV.01 no hop for the camera: the pawn delta hops to PC_0's pawn (HasPawn, Pawn == kStart)",
              r.p.HasPawn && FxPoseIs(r.p.Pawn, kStart), PovStr(r.p));
        check("POV.01 no hop for the camera: exactly one hopped line (the pawn delta's)",
              FxLogCount(FxLine::Hop(g_dbgPc0, g_pc0), 'I') == 1, FxLogDump());
    }

    // POV.02: a controller without a camera manager fails before any getter, with the reset Pov.
    {
        FxReset();
        FxSetPtr(g_pc0, FxOff::kCameraManager, 0);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.02 no camera manager: -4", r.rc == Wirbel::TP_ERR_REFLECTION, RcStr(r.rc));
        check("POV.02 no camera manager: one 'W' naming camOff 88 and PC class 'BP_GameCtrl_C'",
              FxLogCount(PovNoCam(FxOff::kCameraManager, kGameCtrlClass), 'W') == 1, FxLogDump());
        check("POV.02 no camera manager: out == Pov{}", PovIsDefault(r.p), PovStr(r.p));
        check("POV.02 no camera manager: no getter ran", FxCountAll() == 0, FxCallsStr(FxCalls()));
    }

    // POV.03: a ticking game answers through the three getters, in order, on the camera manager.
    {
        FxReset();
        const PovRun r = RunPov();
        check("POV.03 getters: GetPov answers 0", r.rc == Wirbel::TP_OK, RcStr(r.rc) + FxLogDump());
        check("POV.03 getters: calls are [CamLoc, CamRot, Fov] in that order",
              FxKindsAre({ FxKind::CamLoc, FxKind::CamRot, FxKind::Fov }), FxKindsStr(FxKinds()));
        check("POV.03 getters: every getter ran on Cam_0",
              FxCountOn(FxKind::CamLoc, g_cam0) == 1 && FxCountOn(FxKind::CamRot, g_cam0) == 1 &&
                  FxCountOn(FxKind::Fov, g_cam0) == 1,
              FxCallsStr(FxCalls()));
        check("POV.03 getters: Cam == kCamLoc + kCamRot", CamIs(r.p, kCamLoc, kCamRot), PovStr(r.p));
        check("POV.03 getters: Fov == 87.5 (GetFOVAngle's float)", r.p.Fov == kFovGetter, PovStr(r.p));
        check("POV.03 getters: Source 0 (invoke)", r.p.Source == 0, PovStr(r.p));
        check("POV.03 getters: the liveness gate was asked once, at Stark's default threshold",
              GateAtDefault(), GateStr());
    }
    // A GetFOVAngle that returns a double is read as one.
    {
        FxReset();
        FxSetClass(g_cam0, g_cCamFovDoubleGetter);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.03 FOV double: GetPov answers 0", r.rc == Wirbel::TP_OK, RcStr(r.rc) + FxLogDump());
        check("POV.03 FOV double: Fov == 87.25 (the getter's 8-byte return read as a double)",
              r.p.Fov == kFovGetterDouble, PovStr(r.p));
        check("POV.03 FOV double: Cam still from the getters, Source 0",
              CamIs(r.p, kCamLoc, kCamRot) && r.p.Source == 0, PovStr(r.p));
    }
    // GetFOVAngle is a bonus: its failure leaves Fov 0 and the read succeeds.
    {
        FxReset();
        FxRcs(FxKind::Fov, { kInvokeFail });
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.03 FOV fails: GetPov still answers 0", r.rc == Wirbel::TP_OK, RcStr(r.rc) + FxLogDump());
        check("POV.03 FOV fails: the one Fov call answered -5", FxCount(FxKind::Fov) == 1 &&
                                                                   FxNth(FxKind::Fov).rc == kInvokeFail,
              FxCallsStr(FxCalls()));
        check("POV.03 FOV fails: Fov 0, Cam from the getters, Source 0",
              r.p.Fov == 0.0 && CamIs(r.p, kCamLoc, kCamRot) && r.p.Source == 0, PovStr(r.p));
    }

    // POV.04: one getter failing backfills only its own vector from the cached POV.
    {
        FxReset();
        FxRcs(FxKind::CamRot, { kInvokeFail });
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.04 partial, rotation fails: GetPov answers 0", r.rc == Wirbel::TP_OK, RcStr(r.rc) + FxLogDump());
        check("POV.04 partial, rotation fails: loc kCamLoc (getter), rot kRawRot (backfilled)",
              CamIs(r.p, kCamLoc, kRawRot), PovStr(r.p));
        check("POV.04 partial, rotation fails: GetFOVAngle still ran (Fov 87.5), Source 0",
              FxCount(FxKind::Fov) == 1 && r.p.Fov == kFovGetter && r.p.Source == 0, PovStr(r.p));
    }
    {
        FxReset();
        FxRcs(FxKind::CamLoc, { kInvokeFail });
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.04 partial, location fails: GetPov answers 0", r.rc == Wirbel::TP_OK, RcStr(r.rc) + FxLogDump());
        check("POV.04 partial, location fails: loc kRawLoc (backfilled), rot kCamRot (getter)",
              CamIs(r.p, kRawLoc, kCamRot), PovStr(r.p));
        check("POV.04 partial, location fails: Fov 87.5 from the getter, Source 0",
              r.p.Fov == kFovGetter && r.p.Source == 0, PovStr(r.p));
    }
    // D10 pin: the backfill takes the vector only, never the cached FOV.
    {
        FxReset();
        FxRcs(FxKind::CamRot, { kInvokeFail });
        FxRcs(FxKind::Fov, { kInvokeFail });
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.04 D10 pin: rot backfilled with kRawRot", r.rc == Wirbel::TP_OK && CamIs(r.p, kCamLoc, kRawRot),
              RcStr(r.rc) + " " + PovStr(r.p));
        check("POV.04 D10 pin: Fov stays 0 (the cached 75.5 is not backfilled)", r.p.Fov == 0.0, PovStr(r.p));
    }

    // POV.05: both getters failing falls to the cached POV, FOV included, and skips GetFOVAngle.
    {
        FxReset();
        FxRcs(FxKind::CamLoc, { kInvokeFail });
        FxRcs(FxKind::CamRot, { kInvokeFail });
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.05 both fail: GetPov answers 0", r.rc == Wirbel::TP_OK, RcStr(r.rc) + FxLogDump());
        check("POV.05 both fail: calls are [CamLoc, CamRot] and no Fov",
              FxKindsAre({ FxKind::CamLoc, FxKind::CamRot }), FxKindsStr(FxKinds()));
        check("POV.05 both fail: Cam == kRawLoc + kRawRot", CamIs(r.p, kRawLoc, kRawRot), PovStr(r.p));
        check("POV.05 both fail: Fov == 75.5 (the cached float)", r.p.Fov == kRawFov, PovStr(r.p));
        check("POV.05 both fail: Source 1 (raw)", r.p.Source == 1, PovStr(r.p));
        check("POV.05 both fail: one 'I' \"POV via raw cached-POV read\"", FxLogCount(FxText::kPovRaw, 'I') == 1,
              FxLogDump());
    }

    // POV.06: a game thread that is not ticking makes no invoke at all, and the gate is asked at the default threshold.
    {
        FxReset();
        FxResponsive(false);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.06 not responsive: GetPov answers 0", r.rc == Wirbel::TP_OK, RcStr(r.rc) + FxLogDump());
        check("POV.06 not responsive: no engine call, Fov included", FxCountAll() == 0, FxCallsStr(FxCalls()));
        check("POV.06 not responsive: one 'I' \"POV getters skipped\"", FxLogCount(FxText::kPovSkipped, 'I') == 1,
              FxLogDump());
        check("POV.06 not responsive: Source 1 with the raw values (kRawLoc, kRawRot, 75.5)",
              r.p.Source == 1 && CamIs(r.p, kRawLoc, kRawRot) && r.p.Fov == kRawFov, PovStr(r.p));
        check("POV.06 not responsive: the liveness gate was asked once, at Stark's default threshold",
              GateAtDefault(), GateStr());
    }

    // POV.07: getters failing on a camera manager with no cache: -5, the reset Pov, and a WARN that says why.
    {
        FxReset();
        FxSetClass(g_cam0, g_cCamNoCache);
        FxRcAlways(FxKind::CamLoc, kInvokeFail);
        FxRcAlways(FxKind::CamRot, kInvokeFail);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.07 raw also fails: -5", r.rc == Wirbel::TP_ERR_INVOKE, RcStr(r.rc));
        check("POV.07 raw also fails: out == Pov{}", PovIsDefault(r.p), PovStr(r.p));
        check("POV.07 raw also fails: one 'W' naming CamNoCache, getters loc=1 rot=1, CameraCache off=-1",
              FxLogCount(PovFailed("CamNoCache", g_cam0, 1, 1, kUnresolved), 'W') == 1, FxLogDump());
        check("POV.07 raw also fails: no Fov call", FxCount(FxKind::Fov) == 0, FxCallsStr(FxCalls()));
    }

    // POV.08: the raw chain's own rules, all on the raw path (not responsive).
    {
        FxReset();
        FxSetClass(g_cam0, g_cCamFuzzy);
        FxResponsive(false);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.08 fuzzy cache: the struct-typed \"CameraCache\" is taken past the array named like it",
              r.rc == Wirbel::TP_OK && r.p.Source == 1 && CamIs(r.p, kRawLoc, kRawRot) && r.p.Fov == kRawFov,
              RcStr(r.rc) + " " + PovStr(r.p));
    }
    {
        FxReset();
        FxSetClass(g_cam0, g_cCamNoRot);
        FxResponsive(false);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.08 Rotation required: a MinimalViewInfo without Rotation fails the raw read (-5)",
              r.rc == Wirbel::TP_ERR_INVOKE, RcStr(r.rc) + " " + PovStr(r.p));
    }
    {
        FxReset();
        FxSetPtr(g_pc0, FxOff::kCameraManager, g_camEdgeRot);
        FxResponsive(false);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.08 rotation unreadable: GetPov still answers 0", r.rc == Wirbel::TP_OK, RcStr(r.rc) + FxLogDump());
        check("POV.08 rotation unreadable: Cam loc kRawLoc, rot (0,0,0), Fov 0, Source 1",
              CamIs(r.p, kRawLoc, kOrigin) && r.p.Fov == 0.0 && r.p.Source == 1, PovStr(r.p));
    }
    {
        FxReset();
        FxSetPtr(g_pc0, FxOff::kCameraManager, g_camEdgeLoc);
        FxResponsive(false);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.08 location unreadable: -5", r.rc == Wirbel::TP_ERR_INVOKE, RcStr(r.rc) + " " + PovStr(r.p));
    }
    {
        FxReset();
        FxSetClass(g_cam0, g_cCamFovDouble);
        FxPlantFovDouble(g_cam0);
        FxResponsive(false);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.08 FOV is a 4-byte float (pin): GetPov answers 0 on the raw path",
              r.rc == Wirbel::TP_OK && r.p.Source == 1, RcStr(r.rc) + " " + PovStr(r.p));
        check("POV.08 FOV is a 4-byte float (pin): Fov == 0.0, the float read of the double's low half",
              r.p.Fov == 0.0, PovStr(r.p));
    }

    // POV.09: the struct probe behind the raw read accepts the struct wherever its slot puts it, and only a struct.
    {
        struct ProbeCase { const char* what; uintptr_t cls; };
        const ProbeCase cases[] = {
            { "probe +8", g_cCamProbe8 },
            { "class at +0", g_cCamProbeClass },
            { "CJK name", g_cCamCjk },
            { "empty name", g_cCamEmptyName },
        };
        for (const ProbeCase& pcase : cases) {
            FxReset();
            FxSetClass(g_cam0, pcase.cls);
            FxResponsive(false);
            FxClearRecorders();
            const PovRun r = RunPov();
            check(FxFmt("POV.09 %s: GetPov answers 0 on the raw path (Source 1)", pcase.what).c_str(),
                  r.rc == Wirbel::TP_OK && r.p.Source == 1, RcStr(r.rc) + " " + PovStr(r.p) + FxLogDump());
            check(FxFmt("POV.09 %s: the raw values (kRawLoc, kRawRot, 75.5)", pcase.what).c_str(),
                  CamIs(r.p, kRawLoc, kRawRot) && r.p.Fov == kRawFov, PovStr(r.p));
        }
    }
    {
        FxReset();
        FxSetClass(g_cam0, g_cCamCacheNotStruct);
        FxResponsive(false);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.09 kind gate: an IntProperty holding the cache struct is not walked into (-5)",
              r.rc == Wirbel::TP_ERR_INVOKE, RcStr(r.rc) + " " + PovStr(r.p));
    }

    // POV.10: the Locate offsets come from reflection whichever path produced the values.
    {
        FxReset();
        const PovRun r = RunPov();
        check("POV.10 offsets, invoke path: 0x110 / 0x128 / 0x140 on owner Cam_0",
              r.rc == Wirbel::TP_OK && r.p.Source == 0 && OffsetsAre(r.p, kLocOff, kRotOff, kFovOff) &&
                  r.p.CamOwnerAddr == g_cam0,
              RcStr(r.rc) + " " + PovStr(r.p));
    }
    {
        FxReset();
        FxResponsive(false);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.10 offsets, raw path: 0x110 / 0x128 / 0x140 on owner Cam_0",
              r.rc == Wirbel::TP_OK && r.p.Source == 1 && OffsetsAre(r.p, kLocOff, kRotOff, kFovOff) &&
                  r.p.CamOwnerAddr == g_cam0,
              RcStr(r.rc) + " " + PovStr(r.p));
    }
    {
        FxReset();
        FxUE4World();
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.10 offsets, UE4: 0x110 / 0x11C / 0x128 on owner CamF_0",
              r.rc == Wirbel::TP_OK && OffsetsAre(r.p, kLocOff, kRotOffF, kFovOffF) && r.p.CamOwnerAddr == g_camF0,
              RcStr(r.rc) + " " + PovStr(r.p) + FxLogDump());
    }
    {
        FxReset();
        FxSetClass(g_cam0, g_cCamNoCache);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.10 no cache, getters ok: GetPov answers 0 from the getters",
              r.rc == Wirbel::TP_OK && r.p.Source == 0 && CamIs(r.p, kCamLoc, kCamRot),
              RcStr(r.rc) + " " + PovStr(r.p));
        check("POV.10 no cache, getters ok: offsets -1/-1/-1 and the owner still Cam_0",
              OffsetsAre(r.p, kUnresolved, kUnresolved, kUnresolved) && r.p.CamOwnerAddr == g_cam0, PovStr(r.p));
    }

    // POV.11: the pawn delta is the raw RelativeLocation, even on an attached pawn whose world getter would differ.
    {
        FxReset();
        FxAttach(true);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.11 pawn delta: HasPawn with Pawn == kStart (raw), not kAttachedWorld",
              r.rc == Wirbel::TP_OK && r.p.HasPawn && FxPoseIs(r.p.Pawn, kStart), RcStr(r.rc) + " " + PovStr(r.p));
        check("POV.11 pawn delta: K2_GetActorLocation was not invoked", FxCount(FxKind::GetLoc) == 0,
              FxCallsStr(FxCalls()));
    }

    // POV.12: the summary line, both sources.
    {
        FxReset();
        const PovRun r = RunPov();
        check("POV.12 log, invoke: one 'I' POV line with the getters' values and src=invoke",
              r.rc == Wirbel::TP_OK && FxLogCount(PovLine(kCamLoc, kCamRot, kFovGetter, true, false), 'I') == 1,
              FxLogDump());
    }
    {
        FxReset();
        FxResponsive(false);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.12 log, raw: one 'I' POV line with the cached values and src=raw",
              r.rc == Wirbel::TP_OK && FxLogCount(PovLine(kRawLoc, kRawRot, kRawFov, true, true), 'I') == 1,
              FxLogDump());
    }

    // POV.13: InvokeRetVec checks its return's fit only after the call, and trusts an rc 0 that wrote nothing.
    {
        FxReset();
        FxSetClass(g_cam0, g_cCamLocScalar);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.13 scalar GetCameraLocation: the call is still made once", FxCount(FxKind::CamLoc) == 1,
              FxCallsStr(FxCalls()));
        check("POV.13 scalar GetCameraLocation: loc backfilled kRawLoc, rot kCamRot, Source 0",
              r.rc == Wirbel::TP_OK && CamIs(r.p, kRawLoc, kCamRot) && r.p.Source == 0,
              RcStr(r.rc) + " " + PovStr(r.p));
    }
    {
        FxReset();
        FxSilent(FxKind::CamLoc);
        FxClearRecorders();
        const PovRun r = RunPov();
        check("POV.13 rc 0 writing nothing: Cam location (0,0,0) counted as read, rot kCamRot, Source 0",
              r.rc == Wirbel::TP_OK && CamIs(r.p, kOrigin, kCamRot) && r.p.Source == 0,
              RcStr(r.rc) + " " + PovStr(r.p));
        check("POV.13 rc 0 writing nothing: one CamLoc call, and the read did not fall to the raw path",
              FxCount(FxKind::CamLoc) == 1 && FxLogCount(FxText::kPovRaw) == 0, FxLogDump());
    }
}

// ── MCUR ─────────────────────────────────────────────────────────────────────────────────────────────────────────
constexpr uint8_t kCursorOn = kCursorSiblingBit | kCursorBit;   // PC_0's cursor byte with the bit set
constexpr uint8_t kAltBit = 0x04;          // the FieldMask of the bool variants' alternative (valid) layout
constexpr int32_t kNextByte = 1;           // PcBoolByteOffset1's ByteOffset: the byte after bShowMouseCursor's own
constexpr int32_t kPastByteRange = 8;      // PcBoolByteOffset8's ByteOffset: one past the 0..7 a bitfield can use
constexpr const char* kGameOnly = "SetInputMode_GameOnly";
constexpr const char* kGameAndUiEx = "SetInputMode_GameAndUIEx";

static uintptr_t CursorAddr(uintptr_t pc, int32_t byteOff = 0) {
    return pc + static_cast<uintptr_t>(FxOff::kShowCursor) + static_cast<uintptr_t>(byteOff);
}
static uint8_t ByteAt(uintptr_t a) { return FxBytes(a)[0]; }
static std::string ByteStr(uintptr_t a) { return FxFmt("byte=0x%02X", static_cast<unsigned>(ByteAt(a))); }
static std::pair<size_t, size_t> Span(int32_t off, int32_t len) {
    return { static_cast<size_t>(off), static_cast<size_t>(len) };
}
// Wirbel.cpp's own cursor lines, from its format strings.
static std::string Forced(bool on, uintptr_t addr, uint8_t mask, bool bit) {
    return FxFmt("Teleport: bShowMouseCursor forced %s (addr=0x%llX mask=0x%02X) — bit now %d", on ? "ON" : "OFF",
                 static_cast<unsigned long long>(addr), static_cast<unsigned>(mask), bit ? 1 : 0);
}
static std::string InputOk(const char* fn, uintptr_t pc) {
    return FxFmt("Teleport: cursor — %s invoked on PC 0x%llX", fn, static_cast<unsigned long long>(pc));
}
static std::string InputFail(const char* fn, uintptr_t pc, int32_t rc) {
    return FxFmt("Teleport: cursor — %s on PC 0x%llX did not complete (rc=%d; -5 = still queued)", fn,
                 static_cast<unsigned long long>(pc), rc);
}
static std::string InputMissing(const char* fn) {
    return FxFmt("Teleport: cursor — %s not found on WidgetBlueprintLibrary", fn);
}

// One SetMouseCursor / GetMouseCursor with the out flag prefilled, so a check sees whether it was written.
struct CursorRun {
    int32_t rc = 0;
    bool state = false;
};
static CursorRun RunSetCursor(bool show, bool prefill) {
    CursorRun r;
    r.state = prefill;
    r.rc = Wirbel::SetMouseCursor(show, &r.state);
    return r;
}
static CursorRun RunGetCursor(bool prefill) {
    CursorRun r;
    r.state = prefill;
    r.rc = Wirbel::GetMouseCursor(&r.state);
    return r;
}
static std::string CursorStr(const CursorRun& r) { return FxFmt("rc=%d state=%d", r.rc, r.state ? 1 : 0); }

static void McurBlock() {
    blk("MCUR - mouse cursor: the bShowMouseCursor bit, its layout probe and the input mode around it");

    // MCUR.01: the gameplay controller's bit, past an active debug camera.
    {
        FxReset();
        FxDebugCam();
        FxClearRecorders();
        const CursorRun s = RunSetCursor(true, false);
        check("MCUR.01 hop: SetMouseCursor(true) answers 0 with the bit on", s.rc == Wirbel::TP_OK && s.state,
              CursorStr(s) + FxLogDump());
        check("MCUR.01 hop: PC_0's byte was written (0x03)", FxCursorByte() == kCursorOn, ByteStr(CursorAddr(g_pc0)));
        check("MCUR.01 hop: the debug camera's byte is untouched (0x01)", FxCursorByte(g_dbgPc0) == kCursorSiblingBit,
              ByteStr(CursorAddr(g_dbgPc0)));
        check("MCUR.01 hop: exactly one hopped line", FxLogCount(FxLine::Hop(g_dbgPc0, g_pc0), 'I') == 1, FxLogDump());
        check("MCUR.01 hop: the input mode is given PC_0, not the debug camera",
              FxRd<uintptr_t>(FxNth(FxKind::InputGameOnly), FxIm::kPc) == g_pc0, FxCallsStr(FxCalls()));
        // PC_0's bit off and the debug camera's on: a read of the wrong controller answers true.
        FxSetCursorByte(kCursorSiblingBit);
        FxSetCursorByte(kCursorOn, g_dbgPc0);
        const CursorRun g = RunGetCursor(true);
        check("MCUR.01 hop: GetMouseCursor reads PC_0's bit (off), not the debug camera's (on)",
              g.rc == Wirbel::TP_OK && !g.state, CursorStr(g));
    }
    // A controller class with no bShowMouseCursor: -4, quietly, with nothing written.
    {
        FxReset();
        FxSetClass(g_pc0, g_cPcNoCursor);
        FxClearRecorders();
        const CursorRun s = RunSetCursor(true, true);
        check("MCUR.01 no field: SetMouseCursor answers -4 and leaves *out",
              s.rc == Wirbel::TP_ERR_REFLECTION && s.state, CursorStr(s));
        check("MCUR.01 no field: the byte is untouched", FxCursorByte() == kCursorSiblingBit,
              ByteStr(CursorAddr(g_pc0)));
        check("MCUR.01 no field: no engine call and no cursor line or WARN",
              FxCountAll() == 0 && FxTeleportLines('W') == 0 && FxLogPrefix("Teleport: bShowMouseCursor") == 0 &&
                  FxLogPrefix("Teleport: cursor") == 0,
              FxCallsStr(FxCalls()) + FxLogDump());
        const CursorRun g = RunGetCursor(true);
        check("MCUR.01 no field: GetMouseCursor answers -4 and leaves *out",
              g.rc == Wirbel::TP_ERR_REFLECTION && g.state, CursorStr(g));
    }

    // MCUR.02: the FBoolProperty layout probe -- which layouts it takes, where, and with which bit.
    {
        struct Found { const char* what; uintptr_t cls; int32_t byteOff; uint8_t mask; };
        const Found found[] = {
            { "default", 0, 0, kCursorBit },
            { "+0x80", FxPcBoolClass(FxBoolVariant::At80), 0, kCursorBit },
            { "FieldSize 4 then valid @0x7C", FxPcBoolClass(FxBoolVariant::FieldSize4), 0, kAltBit },
            { "ByteMask 0x03 then valid @0x7C", FxPcBoolClass(FxBoolVariant::ByteMask3), 0, kAltBit },
            { "ByteOffset 1", FxPcBoolClass(FxBoolVariant::ByteOffset1), kNextByte, kAltBit },
            { "ByteMask 0x08 / FieldMask 0x04", FxPcBoolClass(FxBoolVariant::ByteMask8), 0, kAltBit },
        };
        for (const Found& f : found) {
            FxReset();
            if (f.cls) FxSetClass(g_pc0, f.cls);
            FxClearRecorders();
            const uintptr_t at = CursorAddr(g_pc0, f.byteOff);
            const uint8_t before = ByteAt(at);
            const CursorRun s = RunSetCursor(true, false);
            check(FxFmt("MCUR.02 layout %s: answers 0 with the bit on", f.what).c_str(),
                  s.rc == Wirbel::TP_OK && s.state, CursorStr(s) + FxLogDump());
            check(FxFmt("MCUR.02 layout %s: pc+0x%X gains mask 0x%02X and keeps its other bits", f.what,
                        static_cast<unsigned>(FxOff::kShowCursor + f.byteOff), static_cast<unsigned>(f.mask))
                      .c_str(),
                  ByteAt(at) == static_cast<uint8_t>(before | f.mask), ByteStr(at));
            check(FxFmt("MCUR.02 layout %s: the forced line names that byte and mask", f.what).c_str(),
                  FxLogCount(Forced(true, at, f.mask, true), 'I') == 1, FxLogDump());
            if (f.byteOff != 0)
                check(FxFmt("MCUR.02 layout %s: bShowMouseCursor's own byte is untouched", f.what).c_str(),
                      FxCursorByte() == kCursorSiblingBit, ByteStr(CursorAddr(g_pc0)));
        }
    }
    {
        struct Refused { const char* what; uintptr_t cls; };
        const Refused refused[] = {
            { "native bool (D5 pin)", FxPcBoolClass(FxBoolVariant::Native) },
            { "ByteOffset 8", FxPcBoolClass(FxBoolVariant::ByteOffset8) },
        };
        for (const Refused& f : refused) {
            FxReset();
            FxSetClass(g_pc0, f.cls);
            FxClearRecorders();
            const CursorRun s = RunSetCursor(true, true);
            check(FxFmt("MCUR.02 layout %s: refused with -4, *out untouched", f.what).c_str(),
                  s.rc == Wirbel::TP_ERR_REFLECTION && s.state, CursorStr(s));
            check(FxFmt("MCUR.02 layout %s: one 'W' \"layout unresolved\"", f.what).c_str(),
                  FxLogCount(FxText::kLayoutUnresolved, 'W') == 1, FxLogDump());
            check(FxFmt("MCUR.02 layout %s: nothing written (pc+0x68 and pc+0x70 as planted), no engine call", f.what)
                      .c_str(),
                  FxCursorByte() == kCursorSiblingBit && ByteAt(CursorAddr(g_pc0, kPastByteRange)) == 0 &&
                      FxCountAll() == 0,
                  ByteStr(CursorAddr(g_pc0)) + " " + ByteStr(CursorAddr(g_pc0, kPastByteRange)));
        }
    }

    // MCUR.03: the write keeps the sibling bit, the reported state is re-read after the input-mode calls, and a write
    // that fails stops before them.
    {
        FxReset();
        const CursorRun s = RunSetCursor(true, false);
        check("MCUR.03 show: 0 with *out true", s.rc == Wirbel::TP_OK && s.state, CursorStr(s));
        check("MCUR.03 show: the byte goes 0x01 -> 0x03", FxCursorByte() == kCursorOn, ByteStr(CursorAddr(g_pc0)));
        check("MCUR.03 show: one 'I' \"forced ON (addr=PC_0+0x68 mask=0x02) - bit now 1\"",
              FxLogCount(Forced(true, CursorAddr(g_pc0), kCursorBit, true), 'I') == 1, FxLogDump());
        check("MCUR.03 show: no WARN", FxTeleportLines('W') == 0, FxLogDump());
    }
    {
        FxReset();
        FxSetCursorByte(kCursorOn);
        FxClearRecorders();
        const CursorRun s = RunSetCursor(false, true);
        check("MCUR.03 hide: 0 with *out false", s.rc == Wirbel::TP_OK && !s.state, CursorStr(s));
        check("MCUR.03 hide: the byte goes 0x03 -> 0x01 (sibling kept)", FxCursorByte() == kCursorSiblingBit,
              ByteStr(CursorAddr(g_pc0)));
        check("MCUR.03 hide: one 'I' \"forced OFF ... bit now 0\"",
              FxLogCount(Forced(false, CursorAddr(g_pc0), kCursorBit, false), 'I') == 1, FxLogDump());
    }
    {
        FxReset();
        // The game reverts the bit during the last input-mode call: what is reported must be what is there after it.
        FxOnCall([](FxKind kind, uintptr_t, int) {
            if (kind == FxKind::InputGameAndUI) FxSetCursorByte(kCursorSiblingBit);
        });
        FxClearRecorders();
        const CursorRun s = RunSetCursor(true, true);
        check("MCUR.03 reverted: still 0, with *out false", s.rc == Wirbel::TP_OK && !s.state, CursorStr(s));
        check("MCUR.03 reverted: the line says \"bit now 0\"",
              FxLogCount(Forced(true, CursorAddr(g_pc0), kCursorBit, false), 'I') == 1, FxLogDump());
    }
    {
        FxReset();
        const uintptr_t ro = FxUseRoPc();
        FxClearRecorders();
        const CursorRun s = RunSetCursor(true, true);
        check("MCUR.03 write fails: -10 with *out untouched", s.rc == Wirbel::TP_ERR_WRITE_FAILED && s.state,
              CursorStr(s));
        check("MCUR.03 write fails: no WidgetBlueprintLibrary call (no engine call at all)", FxCountAll() == 0,
              FxCallsStr(FxCalls()));
        check("MCUR.03 write fails: the byte is unchanged", ByteAt(CursorAddr(ro)) == kCursorSiblingBit,
              ByteStr(CursorAddr(ro)));
        check("MCUR.03 write fails: no \"forced\" line", FxLogPrefix("Teleport: bShowMouseCursor forced") == 0,
              FxLogDump());
    }

    // MCUR.04: the input mode, driven on WidgetBlueprintLibrary's own CDO behind its decoy.
    {
        FxReset();
        const CursorRun s = RunSetCursor(true, false);
        const FxCall go = FxNth(FxKind::InputGameOnly), ui = FxNth(FxKind::InputGameAndUI);
        check("MCUR.04 show: calls are [GameOnly, GameAndUIEx]",
              s.rc == Wirbel::TP_OK && FxKindsAre({ FxKind::InputGameOnly, FxKind::InputGameAndUI }),
              CursorStr(s) + " " + FxKindsStr(FxKinds()));
        check("MCUR.04 show: both on Default__WidgetBlueprintLibrary (slot 8), not the decoy at slot 6",
              FxSlot(kIdxCdoWbl) == g_cdoWbl && go.instance == g_cdoWbl && ui.instance == g_cdoWbl &&
                  go.instance != FxSlot(kIdxCdoWblEx),
              FxCallsStr(FxCalls()));
        check("MCUR.04 show: the library's own SetInputMode_GameOnly and SetInputMode_GameAndUIEx",
              go.ufunc == g_fnGameOnly && ui.ufunc == g_fnGameAndUiEx, FxCallsStr(FxCalls()));
    }
    {
        FxReset();
        FxSetCursorByte(kCursorOn);
        FxClearRecorders();
        const CursorRun s = RunSetCursor(false, true);
        check("MCUR.04 hide: calls are [GameOnly] only",
              s.rc == Wirbel::TP_OK && FxKindsAre({ FxKind::InputGameOnly }),
              CursorStr(s) + " " + FxKindsStr(FxKinds()));
        check("MCUR.04 hide: on Default__WidgetBlueprintLibrary",
              FxNth(FxKind::InputGameOnly).instance == g_cdoWbl, FxCallsStr(FxCalls()));
    }
    {
        FxReset();
        FxRcs(FxKind::InputGameOnly, { kInvokeFail });
        FxClearRecorders();
        const CursorRun s = RunSetCursor(true, false);
        check("MCUR.04 GameOnly fails: SetMouseCursor still 0 with the bit on", s.rc == Wirbel::TP_OK && s.state,
              CursorStr(s));
        check("MCUR.04 GameOnly fails: GameAndUIEx is still called after it",
              FxKindsAre({ FxKind::InputGameOnly, FxKind::InputGameAndUI }) &&
                  FxNth(FxKind::InputGameOnly).rc == kInvokeFail,
              FxCallsStr(FxCalls()));
        check("MCUR.04 GameOnly fails: one 'W' \"SetInputMode_GameOnly ... did not complete (rc=-5 ...)\"",
              FxLogCount(InputFail(kGameOnly, g_pc0, kInvokeFail), 'W') == 1, FxLogDump());
        check("MCUR.04 GameOnly fails: GameAndUIEx's own 'I' still follows",
              FxLogCount(InputOk(kGameAndUiEx, g_pc0), 'I') == 1, FxLogDump());
    }
    {
        FxReset();
        FxPool(FxPoolVariant::NoWbl);
        FxClearRecorders();
        const CursorRun s = RunSetCursor(true, false);
        check("MCUR.04 no WidgetBlueprintLibrary: 0 with the bit written", s.rc == Wirbel::TP_OK && s.state &&
                                                                              FxCursorByte() == kCursorOn,
              CursorStr(s) + " " + ByteStr(CursorAddr(g_pc0)));
        check("MCUR.04 no WidgetBlueprintLibrary: one 'I' \"no WidgetBlueprintLibrary (UMG cooked out?)\"",
              FxLogCount(FxText::kNoWbl, 'I') == 1, FxLogDump());
        check("MCUR.04 no WidgetBlueprintLibrary: no engine call", FxCountAll() == 0, FxCallsStr(FxCalls()));
    }
    {
        FxReset();
        FxWbl(FxWblFnList({ .ui = FxUiShape::NonEx }));
        FxClearRecorders();
        const CursorRun s = RunSetCursor(true, false);
        check("MCUR.04 non-Ex library: SetMouseCursor still 0", s.rc == Wirbel::TP_OK && s.state, CursorStr(s));
        check("MCUR.04 non-Ex library: one 'W' \"SetInputMode_GameAndUIEx not found on WidgetBlueprintLibrary\"",
              FxLogCount(InputMissing(kGameAndUiEx), 'W') == 1, FxLogDump());
        check("MCUR.04 non-Ex library: no call to the non-Ex SetInputMode_GameAndUI, GameOnly still made",
              FxCount(FxKind::InputGameAndUI) == 0 && FxCount(FxKind::InputGameOnly) == 1, FxCallsStr(FxCalls()));
    }

    // MCUR.05: the input-mode parameter blocks, and the level each outcome logs at.
    {
        FxReset();
        const CursorRun s = RunSetCursor(true, false);
        const FxCall go = FxNth(FxKind::InputGameOnly), ui = FxNth(FxKind::InputGameAndUI);
        check("MCUR.05 buffers: GameOnly carries PlayerController == PC_0 @0 in its 0x10 bytes",
              s.rc == Wirbel::TP_OK && go.size == static_cast<uint32_t>(FxIm::kGameOnlyParms) &&
                  FxRd<uintptr_t>(go, FxIm::kPc) == g_pc0,
              FxCallStr(go));
        check("MCUR.05 buffers: GameOnly is zero outside PlayerController (bFlushInput false)",
              FxZeroOutside(go, { Span(FxIm::kPc, kPtrBytes) }), FxCallStr(go));
        check("MCUR.05 buffers: GameAndUIEx carries PlayerController == PC_0 @0 in its 0x18 bytes",
              ui.size == static_cast<uint32_t>(FxIm::kGameAndUiParms) && FxRd<uintptr_t>(ui, FxIm::kPc) == g_pc0,
              FxCallStr(ui));
        check("MCUR.05 buffers: InMouseLockMode @0x10 == 0 (DoNotLock), bHideCursorDuringCapture @0x11 == 0",
              FxRd<uint8_t>(ui, FxIm::kLockMode) == 0 && FxRd<uint8_t>(ui, FxIm::kHideCursor) == 0, FxCallStr(ui));
        check("MCUR.05 buffers: GameAndUIEx is zero outside PlayerController",
              FxZeroOutside(ui, { Span(FxIm::kPc, kPtrBytes) }), FxCallStr(ui));
        check("MCUR.05 buffers: one 'I' \"SetInputMode_GameOnly invoked on PC 0x<PC_0>\"",
              FxLogCount(InputOk(kGameOnly, g_pc0), 'I') == 1, FxLogDump());
        check("MCUR.05 buffers: one 'I' \"SetInputMode_GameAndUIEx invoked on PC 0x<PC_0>\"",
              FxLogCount(InputOk(kGameAndUiEx, g_pc0), 'I') == 1, FxLogDump());
    }
    {
        FxReset();
        FxRcs(FxKind::InputGameAndUI, { kInvokeFail });
        FxClearRecorders();
        const CursorRun s = RunSetCursor(true, false);
        check("MCUR.05 rc -5: SetMouseCursor still 0", s.rc == Wirbel::TP_OK && s.state, CursorStr(s));
        check("MCUR.05 rc -5: one 'W' \"SetInputMode_GameAndUIEx on PC 0x<PC_0> did not complete (rc=-5; ...)\"",
              FxLogCount(InputFail(kGameAndUiEx, g_pc0, kInvokeFail), 'W') == 1, FxLogDump());
        check("MCUR.05 rc -5: no \"invoked\" line for the call that did not complete",
              FxLogCount(InputOk(kGameAndUiEx, g_pc0)) == 0, FxLogDump());
    }

    // MCUR.06: GetMouseCursor reads the bit under its mask, and only reads.
    {
        FxReset();
        const CursorRun off = RunGetCursor(true);
        check("MCUR.06 read, byte 0x01: 0 with false (the sibling bit is not the cursor's)",
              off.rc == Wirbel::TP_OK && !off.state, CursorStr(off));
        FxSetCursorByte(kCursorBit);
        const CursorRun on = RunGetCursor(false);
        check("MCUR.06 read, byte 0x02: 0 with true", on.rc == Wirbel::TP_OK && on.state, CursorStr(on));
        check("MCUR.06 read: no engine call and the byte unchanged",
              FxCountAll() == 0 && FxCursorByte() == kCursorBit, ByteStr(CursorAddr(g_pc0)));
        check("MCUR.06 read: no \"forced\" line", FxLogPrefix("Teleport: bShowMouseCursor forced") == 0, FxLogDump());
    }
}

// UPROP.01: on a UProperty engine the bool layout is probed from UBOOLPROP_FIELDSIZE, where the FProperty base would
// find nothing.
static void UpropBlock() {
    blk("UPROP - the cursor bit through UProperty reflection");
    FxReset();
    FxUpropWorld(true);
    FxClearRecorders();
    const CursorRun g = RunGetCursor(true);
    check("UPROP.01 UProperty arm: GetMouseCursor answers 0 with false (PCU_0's byte 0x01)",
          g.rc == Wirbel::TP_OK && !g.state, CursorStr(g) + FxLogDumpCat(0, nullptr));
    check("UPROP.01 UProperty arm: the scan found PCU_0 as the local controller",
          FxLogCount(FxLine::ScanLocal(g_pcU0), 'I') == 1, FxLogDump());
    const CursorRun s = RunSetCursor(true, false);
    check("UPROP.01 UProperty arm: SetMouseCursor(true) answers 0 with the bit on", s.rc == Wirbel::TP_OK && s.state,
          CursorStr(s) + FxLogDumpCat(0, nullptr));
    check("UPROP.01 UProperty arm: PCU_0+0x68 gains bit 0x02 (0x01 -> 0x03)", ByteAt(CursorAddr(g_pcU0)) == kCursorOn,
          ByteStr(CursorAddr(g_pcU0)));
    check("UPROP.01 UProperty arm: the forced line names PCU_0+0x68 and mask 0x02",
          FxLogCount(Forced(true, CursorAddr(g_pcU0), kCursorBit, true), 'I') == 1, FxLogDump());
    FxUpropWorld(false);
}

}  // namespace FxG2

static void Blocks_G2() {
    FxG2::PovBlock();
    FxG2::McurBlock();
    FxG2::UpropBlock();
}

// ══ G3: MOVE / COMP / DEEP, FRZ / DIAG / ROT / STOP / RT ═══════════════════════════════════════════════════════════
// The move itself. It is reached through RecallExplicit with keepUndo set, so the "last" slot writes no line of its own
// and every call and line a check counts belongs to the move; through TeleportToCursor where a check needs
// K2_TeleportTo asked first; and through RecallLast for RecallTo's own chain. DeepForceWorldPos is also called
// directly: its skips and its window edges are properties of the function alone, and a move reaches it with only the
// one old position it read.

namespace g3 {

// Stark's "timed out, still queued": an invoke that did not run, so the fake engine writes nothing for it.
constexpr int32_t kDidNotRun = Stark::kInvokeTimedOutStillQueued;
// The channel the cursor hit is scripted on; a cursor jump asks for the requested channel first, so it hits at once.
constexpr int32_t kTraceChannel = 0;
constexpr double kZero[3] = { 0.0, 0.0, 0.0 };
// Where a cursor jump lands: the scripted hit point raised by the z offset.
constexpr double kCursorDest[3] = { kHit[0], kHit[1], kHit[2] + kZOffset };
// The facing SetControlRotation leaves after a recall to kDest: the fake engine keeps yaw in [0, 360).
constexpr double kDestPyrStored[3] = { kDest.Pitch, kDestYawStored, kDest.Roll };
// Wirbel's "reached" radius for the post-move check: a landing exactly this short counts as arrived, one a half unit
// further does not.
constexpr double kReachedEps = 60.0;
constexpr double kJustPastEps = 60.5;
constexpr int32_t kBoolBytes = 1;
constexpr int32_t kNewRotationAt = 0;     // SetControlRotation's only parameter
// A parameterless function still gets a one-byte buffer: ProcessEvent is never handed a null one.
constexpr uint32_t kNoParamsBuffer = 1;
constexpr int kThawCall = 1;              // the freeze's second SetMovementMode: MOVE_Falling, after the raw write

// Deep force, called directly.
constexpr int32_t kNarrowRelLoc = 8;      // a RelativeLocation narrower than even a float vector
constexpr int32_t kCopyAt = 0x200;        // where a direct call on the scratch blob finds its one planted copy
// The degenerate-origin guard measures the old position's L1 size against 1.0: one under it, one exactly at it.
constexpr double kBelowL1[3] = { 0.25, 0.25, 0.25 };
constexpr double kAtL1[3] = { 0.5, 0.25, 0.25 };
// A 6-8-10 triangle: a copy this far from the old position sits exactly on the match radius (squared distance 100),
// and one half a unit higher lies just past it.
constexpr double kEdgeDx = 6.0, kEdgeDy = 8.0, kPastDz = 0.5;
constexpr int32_t kEdgeCopyAt = 0x200, kPastCopyAt = 0x260;
static_assert(kPastCopyAt >= kEdgeCopyAt + kVecD, "the two DEEP.02 copies do not overlap");
// The window's edges, for a double vector: the last one that ends inside it, one step later (straddling its end), one
// wholly past it; and one inside the window but past the root class's reflected size.
constexpr int32_t kDoubleStep = 8;
constexpr int32_t kLastInsideAt = kDeepWindow - kVecD;
constexpr int32_t kStraddleAt = kLastInsideAt + kDoubleStep;
constexpr int32_t kPastWindowAt = kDeepWindow + kDoubleStep;
constexpr int32_t kPastClassAt = 0x300;
static_assert(kPastClassAt >= kSceneBytes && kPastClassAt + kVecD <= kDeepWindow,
              "DEEP.04's small-class copy lies past CapsuleComponent's PropertiesSize and inside the window");
static_assert(static_cast<size_t>(kPastWindowAt + kVecD) <= kBigBytes, "every DEEP.04 copy lies inside Root_0's blob");
// Read as doubles or as floats, every vector of these bytes is astronomically far from any position here, so the only
// match a direct deep-force call on the scratch blob can find is the copy a check plants.
constexpr uint8_t kScratchFill = 0x7F;

constexpr const char* kFreezePrefix = "Teleport: actor didn't reach target";
constexpr const char* kDeepPrefix = "Teleport: deep-force";   // both of deep force's lines: the INFO and the WARN

static std::string PackFailed(const char* fn) {
    return FxFmt("Teleport: %s param-pack failed (couldn't locate location param) — raw-write fallback", fn);
}
static std::string SetterFailed(const char* fn, const char* how, int32_t r, int32_t parmsSize) {
    return FxFmt("Teleport: %s %s (r=%d, parmsSize=%u) — trying raw-write fallback", fn, how, r,
                 static_cast<unsigned>(parmsSize));
}
static std::string FreezeEnter(const double cur[3], bool haveCmc) {
    return FxFmt("Teleport: actor didn't reach target (cur=(%.1f,%.1f,%.1f)) — forcing via CMC freeze%s", cur[0],
                 cur[1], cur[2], haveCmc ? "" : " (no CMC found)");
}
static std::string FreezeOk(const double cur[3]) {
    return FxFmt("Teleport: CMC-freeze force succeeded -> cur=(%.1f,%.1f,%.1f)", cur[0], cur[1], cur[2]);
}
static std::string FreezeStill(const double cur[3]) {
    return FxFmt("Teleport: CMC-freeze force STILL didn't stick (cur=(%.1f,%.1f,%.1f)) — the memory write didn't "
                 "hold; the game may re-derive position from a separate source", cur[0], cur[1], cur[2]);
}
static std::string ModeNotDone(uint8_t mode, uintptr_t cmc, int32_t rc) {
    return FxFmt("Teleport: SetMovementMode(%u) on CMC 0x%llX did not complete (rc=%d; -5 = still queued)",
                 static_cast<unsigned>(mode), static_cast<unsigned long long>(cmc), rc);
}
static std::string DiagSeparate(uintptr_t vt, const char* vtClass, const double vtLoc[3], uintptr_t pawn,
                                const char* pawnClass, const double pawnLoc[3], int hasMesh, int hasCmc) {
    return FxFmt("Teleport diag: SEPARATE visible actor — camera follows 0x%llX '%s' loc=(%.1f, %.1f, %.1f) but "
                 "possessed pawn is 0x%llX '%s' loc=(%.1f, %.1f, %.1f) (view-target hasMesh=%d hasCMC=%d). Candidate "
                 "actor to teleport instead.",
                 static_cast<unsigned long long>(vt), vtClass, vtLoc[0], vtLoc[1], vtLoc[2],
                 static_cast<unsigned long long>(pawn), pawnClass, pawnLoc[0], pawnLoc[1], pawnLoc[2], hasMesh, hasCmc);
}
static std::string RotFailed(uintptr_t at) {
    return FxFmt("Teleport: the ControlRotation fallback write FAILED at 0x%llX -- the rotation was not restored",
                 static_cast<unsigned long long>(at));
}
static std::string RecallLastLine(const Wirbel::Pose& p, int32_t rc) {
    return FxFmt("Teleport: recall-last (%.1f, %.1f, %.1f) -> rc=%d", p.X, p.Y, p.Z, rc);
}

}  // namespace g3

// What one move op answered.
struct G3Out {
    int32_t rc = INT32_MIN;
    uint8_t tier = kTierSentinel;
};
// A recall to kDest, keepUndo set: the "last" slot is left alone, so it adds neither a read nor a line.
static G3Out G3Recall(bool hasRot = false) {
    G3Out o;
    o.rc = Wirbel::RecallExplicit(kDest, hasRot, &o.tier, /*keepUndo=*/true);
    return o;
}
// A cursor jump whose first trace (path 1, the requested channel) hits kHit, so the move lands on kCursorDest.
static G3Out G3Cursor() {
    FxHit(FxKind::HitCursor, g3::kTraceChannel);
    G3Out o;
    Wirbel::Pose hit = FxSentinelPose();
    bool usedCenter = true;
    o.rc = Wirbel::TeleportToCursor(kZOffset, g3::kTraceChannel, /*fallbackToCenter=*/true, &hit, &o.tier,
                                    &usedCenter);
    return o;
}
static std::string G3Got(const G3Out& o) {
    return FxFmt("rc=%d tier=0x%02X calls=", o.rc, static_cast<unsigned>(o.tier)) + FxKindsStr(FxKinds()) +
           FxLogDump();
}
static std::pair<size_t, size_t> G3Span(int32_t off, int32_t len) {
    return { static_cast<size_t>(off), static_cast<size_t>(len) };
}
// The calls since `from` are exactly these, kind and instance, in this order.
static bool G3CallsAre(std::initializer_list<std::pair<FxKind, uintptr_t>> want, size_t from = 0) {
    const auto calls = FxCalls(from);
    if (calls.size() != want.size()) return false;
    size_t i = 0;
    for (const auto& w : want) {
        if (calls[i].kind != w.first || calls[i].instance != w.second) return false;
        ++i;
    }
    return true;
}
// The 12 guard bytes planted after a float vector all still hold kGuardByte.
static bool G3Guarded(uintptr_t at) {
    for (int i = 0; i < FxOff::kGuardBytes; ++i)
        if (FxBytes(at)[i] != kGuardByte) return false;
    return true;
}
static std::string G3BytesStr(uintptr_t at, int n) {
    std::string s;
    for (int i = 0; i < n; ++i) s += FxFmt("%02X ", static_cast<unsigned>(FxBytes(at)[i]));
    return s;
}
static std::string G3VecStr(uintptr_t at, int32_t width) { return FxArrStr(FxVecAt(at, width)); }
// The Chain a direct deep-force call needs: the root, and RelativeLocation's offset and width.
static Chain G3DeepChain(uintptr_t root, int32_t relLocSize) {
    Chain c{};
    c.root = root;
    c.relLocOff = FxOff::kRelativeLocation;
    c.relLocSize = relLocSize;
    return c;
}
// A root-sized blob that is no fixture object, so a direct call that should not write anything can be compared whole
// before and after without touching the world.
alignas(16) static uint8_t g3_scratch[kBigBytes];
static uintptr_t G3Scratch() {
    memset(g3_scratch, g3::kScratchFill, sizeof g3_scratch);
    return FxAddr(g3_scratch);
}
static std::vector<uint8_t> G3ScratchSnap() { return std::vector<uint8_t>(g3_scratch, g3_scratch + sizeof g3_scratch); }
static bool G3ScratchSame(const std::vector<uint8_t>& snap) {
    return snap.size() == sizeof g3_scratch && memcmp(snap.data(), g3_scratch, sizeof g3_scratch) == 0;
}

// ══ MOVE ══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void G3Move() {
    using K = FxKind;
    blk("MOVE - TeleportPawnTo: the actor setter it picks and packs, the outcome it logs, tier 2, the verification "
        "read and the freeze gate");

    // MOVE.01 / .03 / .06 / .10 / .11 / .12: the default recall -- unattached, every setter present and answering.
    FxReset();
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("MOVE.01 order: a recall calls K2_SetActorLocation on Hero_0, K2_GetActorLocation on Hero_0, "
              "StopMovement on PC_0 and StopMovementImmediately on CMC_0 -- nothing else, in that order",
              G3CallsAre({ { K::SetActorLoc, g_hero0 }, { K::GetLoc, g_hero0 }, { K::StopMove, g_pc0 },
                           { K::StopMoveNow, g_cmc0 } }),
              FxCallsStr(FxCalls()));
        check("MOVE.01 order: it answers 0 at tier 1, and Root_0's RelativeLocation holds kDest",
              o.rc == Wirbel::TP_OK && o.tier == 1 && FxEq3(FxPos(), kDestXyz), G3Got(o) + " pos " + FxArrStr(FxPos()));
        check("MOVE.01 order: the camera follows the pawn, so no 'Teleport diag' line",
              FxLogPrefix(FxText::kDiagPrefix) == 0, FxLogDump());
        const FxCall sal = FxNth(K::SetActorLoc);
        check("MOVE.03 buffer: K2_SetActorLocation's buffer is its ParmsSize, 0x110",
              sal.size == static_cast<uint32_t>(kSal5.parmsSize) && sal.params.size() == sal.size, FxCallStr(sal));
        check("MOVE.03 buffer: NewLocation @0 arrives as kDest in 3 doubles",
              FxEq3(FxVec(sal, kSal5.loc, kVecD), kDestXyz), FxArrStr(FxVec(sal, kSal5.loc, kVecD)));
        check("MOVE.03 buffer: bTeleport @0x108 arrives 1 and bSweep @0x18 arrives 0",
              FxRd<uint8_t>(sal, kSal5.teleport) == 1 && FxRd<uint8_t>(sal, kSal5.sweep) == 0,
              FxFmt("bTeleport=%u bSweep=%u", static_cast<unsigned>(FxRd<uint8_t>(sal, kSal5.teleport)),
                    static_cast<unsigned>(FxRd<uint8_t>(sal, kSal5.sweep))));
        check("MOVE.03 buffer: every byte but NewLocation and bTeleport arrives zero",
              FxZeroOutside(sal, { G3Span(kSal5.loc, kVecD), G3Span(kSal5.teleport, g3::kBoolBytes) }));
        check("MOVE.06 outcome: one INFO 'K2_SetActorLocation invoked OK on pawn' naming Hero_0 and kDest",
              FxLogCount(FxLine::PawnSetterOk("K2_SetActorLocation", g_hero0, kDestXyz), 'I') == 1, FxLogDump());
        const FxCall get = FxNth(K::GetLoc);
        check("MOVE.10 verification: unattached, exactly one K2_GetActorLocation, on Hero_0, after the setter (D7 pin)",
              FxCount(K::GetLoc) == 1 && get.instance == g_hero0 && get.seq > sal.seq, FxCallsStr(FxCalls()));
        check("MOVE.11 threshold: a move that landed enters no CMC freeze -- no SetMovementMode, no freeze WARN",
              FxCount(K::SetMoveMode) == 0 && FxLogPrefix(g3::kFreezePrefix) == 0, G3Got(o));
        check("MOVE.12 post-move: one INFO 'post-move cur=kDest target=kDest attached=0'",
              FxLogCount(FxLine::PostMove(kDestXyz, kDestXyz, false), 'I') == 1, FxLogDump());
    }

    // MOVE.02: a recall asks K2_SetActorLocation first, a cursor jump K2_TeleportTo; each takes the other name only
    // when its own is not reflected at all.
    FxReset();
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("MOVE.02 name pick: a recall moves with K2_SetActorLocation and never asks K2_TeleportTo",
              o.rc == Wirbel::TP_OK && FxCount(K::SetActorLoc) == 1 && FxCount(K::TeleportTo) == 0, G3Got(o));
    }
    FxReset();
    FxClearRecorders();
    {
        const G3Out o = G3Cursor();
        check("MOVE.02 name pick: a cursor jump moves with K2_TeleportTo on Hero_0 and never K2_SetActorLocation",
              o.rc == Wirbel::TP_OK && FxCountOn(K::TeleportTo, g_hero0) == 1 && FxCount(K::TeleportTo) == 1 &&
                  FxCount(K::SetActorLoc) == 0,
              G3Got(o));
    }
    const uintptr_t tpOnly = FxActorFnList({ .setActorLoc = FxSalShape::Missing });
    FxReset();
    FxActorFns(tpOnly);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("MOVE.02 name pick: a recall on a pawn without K2_SetActorLocation takes K2_TeleportTo on Hero_0, tier 1",
              o.rc == Wirbel::TP_OK && o.tier == 1 && FxCountOn(K::TeleportTo, g_hero0) == 1 &&
                  FxCount(K::SetWorldLoc) == 0 && FxEq3(FxPos(), kDestXyz),
              G3Got(o));
    }
    FxReset();
    FxActorFns(FxActorFnList({ .teleportTo = false }));
    FxClearRecorders();
    {
        const G3Out o = G3Cursor();
        const FxCall sal = FxNth(K::SetActorLoc);
        check("MOVE.02 name pick: a cursor jump on a pawn without K2_TeleportTo takes K2_SetActorLocation, to the hit "
              "raised by zOffset",
              o.rc == Wirbel::TP_OK && FxCount(K::SetActorLoc) == 1 && FxCount(K::TeleportTo) == 0 &&
                  FxEq3(FxVec(sal, kSal5.loc, kVecD), g3::kCursorDest),
              G3Got(o) + " NewLocation " + FxArrStr(FxVec(sal, kSal5.loc, kVecD)));
    }
    // D11: a K2_SetActorLocation that is reflected but cooked (ParmsSize 0) is found, so K2_TeleportTo is never asked,
    // and the move goes to the root's setters.
    FxReset();
    FxActorFns(FxActorFnList({ .setActorLoc = FxSalShape::Cooked }));
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("MOVE.02 D11 pin: the cooked K2_SetActorLocation is not called and K2_TeleportTo is not tried",
              FxCount(K::SetActorLoc) == 0 && FxCount(K::TeleportTo) == 0, G3Got(o));
        check("MOVE.02 D11 pin: one WARN 'no K2_TeleportTo / K2_SetActorLocation on pawn class ... RootComponent "
              "setters'",
              FxLogCount(FxText::kNoSetters, 'W') == 1, FxLogDump());
        check("MOVE.02 D11 pin: the move goes to K2_SetWorldLocation on Root_0 and lands at tier 1",
              o.rc == Wirbel::TP_OK && o.tier == 1 &&
                  G3CallsAre({ { K::SetWorldLoc, g_root0 }, { K::GetLoc, g_hero0 }, { K::StopMove, g_pc0 },
                               { K::StopMoveNow, g_cmc0 } }) &&
                  FxEq3(FxPos(), kDestXyz),
              G3Got(o));
    }

    // MOVE.04: K2_TeleportTo's DestRotation -- the recall's own rotation, else the current facing, else zeros.
    FxReset();
    FxActorFns(tpOnly);
    FxClearRecorders();
    {
        const G3Out o = G3Recall(/*hasRot=*/true);
        const FxCall tp = FxNth(K::TeleportTo);
        check("MOVE.04 TeleportTo rotation: a recall with a rotation sends DestRotation = kDest's (5.5, -30.5, 0) and "
              "DestLocation = kDest",
              o.rc == Wirbel::TP_OK && tp.seq != 0 && FxEq3(FxVec(tp, kTp5.rot, kVecD), kDestPyr) &&
                  FxEq3(FxVec(tp, kTp5.loc, kVecD), kDestXyz),
              FxCallStr(tp) + " rot " + FxArrStr(FxVec(tp, kTp5.rot, kVecD)) + " loc " +
                  FxArrStr(FxVec(tp, kTp5.loc, kVecD)));
    }
    FxReset();
    FxActorFns(tpOnly);
    FxClearRecorders();
    {
        const G3Out o = G3Recall(/*hasRot=*/false);
        const FxCall tp = FxNth(K::TeleportTo);
        check("MOVE.04 TeleportTo rotation: a recall without one keeps the current facing -- DestRotation = PC_0's "
              "ControlRotation kRot",
              o.rc == Wirbel::TP_OK && tp.seq != 0 && FxEq3(FxVec(tp, kTp5.rot, kVecD), kRot) &&
                  FxEq3(FxVec(tp, kTp5.loc, kVecD), kDestXyz),
              FxCallStr(tp) + " rot " + FxArrStr(FxVec(tp, kTp5.rot, kVecD)));
    }
    FxReset();
    FxClearRecorders();
    {
        const G3Out o = G3Cursor();
        const FxCall tp = FxNth(K::TeleportTo);
        check("MOVE.04 TeleportTo rotation: a cursor jump keeps the current facing -- DestRotation kRot, DestLocation "
              "the hit raised by zOffset",
              o.rc == Wirbel::TP_OK && tp.seq != 0 && FxEq3(FxVec(tp, kTp5.rot, kVecD), kRot) &&
                  FxEq3(FxVec(tp, kTp5.loc, kVecD), g3::kCursorDest),
              FxCallStr(tp) + " rot " + FxArrStr(FxVec(tp, kTp5.rot, kVecD)) + " loc " +
                  FxArrStr(FxVec(tp, kTp5.loc, kVecD)));
    }
    FxReset();
    FxActorFns(tpOnly);
    FxSetClass(g_pc0, g_cPcNoRot);
    FxClearRecorders();
    {
        const G3Out o = G3Recall(/*hasRot=*/false);
        const FxCall tp = FxNth(K::TeleportTo);
        check("MOVE.04 TeleportTo rotation: a controller with no reflected ControlRotation sends DestRotation zeros",
              o.rc == Wirbel::TP_OK && tp.seq != 0 && FxEq3(FxVec(tp, kTp5.rot, kVecD), g3::kZero) &&
                  FxEq3(FxVec(tp, kTp5.loc, kVecD), kDestXyz),
              FxCallStr(tp) + " rot " + FxArrStr(FxVec(tp, kTp5.rot, kVecD)));
    }

    // MOVE.05: a K2_SetActorLocation whose location parameter is not named NewLocation is never invoked.
    FxReset();
    FxActorFns(FxActorFnList({ .setActorLoc = FxSalShape::LocationParam }));
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("MOVE.05 pack fails: one WARN 'K2_SetActorLocation param-pack failed (couldn't locate location param) "
              "— raw-write fallback'",
              FxLogCount(g3::PackFailed("K2_SetActorLocation"), 'W') == 1, FxLogDump());
        check("MOVE.05 pack fails: K2_SetActorLocation is never called; K2_SetWorldLocation on Root_0 moves the pawn "
              "at tier 1",
              o.rc == Wirbel::TP_OK && o.tier == 1 && FxCount(K::SetActorLoc) == 0 &&
                  FxCountOn(K::SetWorldLoc, g_root0) == 1 && FxEq3(FxPos(), kDestXyz),
              G3Got(o));
    }

    // MOVE.06 / MOVE.07: the actor setter answered false, or did not run.
    FxReset();
    FxRetByte(K::SetActorLoc, 0);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("MOVE.06 outcome: K2_SetActorLocation answering false logs one WARN 'returned false (r=0, "
              "parmsSize=272) — trying raw-write fallback' and no OK line",
              FxLogCount(g3::SetterFailed("K2_SetActorLocation", "returned false", 0, kSal5.parmsSize), 'W') == 1 &&
                  FxLogPrefix("Teleport: K2_SetActorLocation invoked OK") == 0,
              FxLogDump());
        check("MOVE.06 outcome: ... and the move falls to K2_SetWorldLocation on Root_0",
              o.rc == Wirbel::TP_OK && o.tier == 1 && FxCountOn(K::SetWorldLoc, g_root0) == 1, G3Got(o));
    }
    FxReset();
    FxRcs(K::SetActorLoc, { g3::kDidNotRun });
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("MOVE.06 outcome: K2_SetActorLocation that did not run logs one WARN 'invoke failed (r=-5, "
              "parmsSize=272) — trying raw-write fallback'",
              FxLogCount(g3::SetterFailed("K2_SetActorLocation", "invoke failed", g3::kDidNotRun, kSal5.parmsSize),
                         'W') == 1,
              FxLogDump());
        check("MOVE.07 component tier 1: K2_SetWorldLocation on Root_0 then moves the pawn to kDest at tier 1",
              o.rc == Wirbel::TP_OK && o.tier == 1 && FxCountOn(K::SetWorldLoc, g_root0) == 1 &&
                  FxEq3(FxPos(), kDestXyz),
              G3Got(o) + " pos " + FxArrStr(FxPos()));
        check("MOVE.07 component tier 1: K2_SetRelativeLocation is not tried after it, and no tier-2 line",
              FxCount(K::SetRelLoc) == 0 && FxLogCount(FxText::kTier2) == 0, G3Got(o));
    }

    // MOVE.08: tier 2 -- no setter on the pawn or on its root.
    const uintptr_t noActorSetters = FxActorFnList({ .setActorLoc = FxSalShape::Missing, .teleportTo = false });
    const uintptr_t noSceneSetters = FxSceneFnList({ .world = FxCompShape::Missing, .relative = false });
    FxReset();
    FxActorFns(noActorSetters);
    FxSceneFns(noSceneSetters);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("MOVE.08 tier 2: with no setter on the pawn or its root the recall answers 0 at tier 2, RelativeLocation "
              "kDest",
              o.rc == Wirbel::TP_OK && o.tier == 2 && FxEq3(FxPos(), kDestXyz), G3Got(o) + " pos " + FxArrStr(FxPos()));
        check("MOVE.08 tier 2: the only calls are the verification read and the settle",
              G3CallsAre({ { K::GetLoc, g_hero0 }, { K::StopMove, g_pc0 }, { K::StopMoveNow, g_cmc0 } }),
              FxCallsStr(FxCalls()));
        const uint64_t tier2 = FxLogSeq(FxText::kTier2, 'W');
        const uint64_t deep = FxLogSeq(FxLine::DeepRewrote(1, g_root0), 'I');
        check("MOVE.08 tier 2: one WARN 'tier-2 raw RelativeLocation write', then the INFO deep-force line",
              FxLogCount(FxText::kTier2, 'W') == 1 && tier2 != 0 && deep > tier2, FxLogDump());
        check("DEEP.02 snapshot after the raw write: exactly 1 vector rewritten on Root_0 -- RelativeLocation already "
              "held kDest, so only the ComponentToWorld copy at +0x100 matched, and it now holds kDest",
              FxLogCount(FxLine::DeepRewrote(1, g_root0), 'I') == 1 &&
                  FxEq3(FxVecAt(g_root0 + FxOff::kC2W, kVecD), kDestXyz),
              FxLogDump() + " C2W " + G3VecStr(g_root0 + FxOff::kC2W, kVecD));
        check("MOVE.12 post-move: tier 2 still ends with one INFO post-move line, cur kDest",
              FxLogCount(FxLine::PostMove(kDestXyz, kDestXyz, false), 'I') == 1, FxLogDump());
    }
    FxReset();
    {
        const uintptr_t ro = FxUseRoRoot();
        FxRcAlways(K::SetActorLoc, g3::kDidNotRun);
        FxRcAlways(K::SetWorldLoc, g3::kDidNotRun);
        FxRcAlways(K::SetRelLoc, g3::kDidNotRun);
        FxClearRecorders();
        const G3Out o = G3Recall(/*hasRot=*/true);
        check("MOVE.08 write fails: a refused tier-2 write answers -10 and leaves *tierOut at its sentinel",
              o.rc == Wirbel::TP_ERR_WRITE_FAILED && o.tier == kTierSentinel, G3Got(o));
        check("MOVE.08 write fails: nothing runs after the write -- only the three setters that did not run, no "
              "GetLoc, no SetControlRotation, no StopMovement",
              G3CallsAre({ { K::SetActorLoc, g_hero0 }, { K::SetWorldLoc, ro }, { K::SetRelLoc, ro } }),
              FxCallsStr(FxCalls()));
        check("MOVE.08 write fails: no tier-2 line, and the read-only RelativeLocation still holds kStart",
              FxLogCount(FxText::kTier2) == 0 && FxEq3(FxVecAt(ro + FxOff::kRelativeLocation, kVecD), kStart),
              FxLogDump());
    }
    FxReset();
    FxActorFns(noActorSetters);
    FxSceneFns(noSceneSetters);
    FxAttach(true);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("MOVE.08 attached tier 2: tier 2 and no deep-force line of either kind -- an attached root's world is "
              "not its RelativeLocation",
              o.rc == Wirbel::TP_OK && o.tier == 2 && FxLogPrefix(g3::kDeepPrefix) == 0, G3Got(o));
        check("MOVE.08 attached tier 2: Root_0's ComponentToWorld copy is left at kStart",
              FxEq3(FxVecAt(g_root0 + FxOff::kC2W, kVecD), kStart), G3VecStr(g_root0 + FxOff::kC2W, kVecD));
        check("MOVE.11 threshold: tier 2 far from the target (GetLoc answers kAttachedWorld) enters no freeze (D9 pin)",
              FxCount(K::SetMoveMode) == 0 && FxLogPrefix(g3::kFreezePrefix) == 0 &&
                  FxLogCount(FxLine::PostMove(kAttachedWorld, kDestXyz, true), 'I') == 1,
              G3Got(o));
    }
    FxReset();
    FxUE4World();
    FxActorFFns(FxActorFnList({ .setActorLoc = FxSalShape::Missing, .teleportTo = false, .ue4 = true }));
    FxSceneFFns(FxSceneFnList({ .world = FxCompShape::Missing, .relative = false, .ue4 = true }));
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("MOVE.08 UE4 sentinel: tier 2 writes kDest as 3 floats into RootF_0's RelativeLocation",
              o.rc == Wirbel::TP_OK && o.tier == 2 &&
                  FxEq3(FxVecAt(g_rootF0 + FxOff::kRelativeLocation, kVecF), kDestXyz),
              G3Got(o) + " pos " + G3VecStr(g_rootF0 + FxOff::kRelativeLocation, kVecF));
        check("MOVE.08 UE4 sentinel: the 12 bytes after it (0x4C..0x57) still hold 0xA5 -- 12 bytes written, not 24",
              G3Guarded(g_rootF0 + FxOff::kRelLocGuard),
              G3BytesStr(g_rootF0 + FxOff::kRelLocGuard, FxOff::kGuardBytes));
        check("DEEP.02 matching: on a float root the copy at +0x104 is rewritten as 3 floats -- one INFO 'rewrote 1' "
              "on RootF_0",
              FxLogCount(FxLine::DeepRewrote(1, g_rootF0), 'I') == 1 &&
                  FxEq3(FxVecAt(g_rootF0 + FxOff::kC2WF, kVecF), kDestXyz),
              FxLogDump() + " C2WF " + G3VecStr(g_rootF0 + FxOff::kC2WF, kVecF));
    }

    // MOVE.10: what the verification read trusts.
    FxReset();
    FxAnswer(K::GetLoc, g3::kZero);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("MOVE.10 verification: unattached, a K2_GetActorLocation answering (0,0,0) is asked but not trusted -- "
              "cur is RelativeLocation (kDest) and no freeze",
              o.rc == Wirbel::TP_OK && FxCount(K::GetLoc) == 1 && FxCount(K::SetMoveMode) == 0 &&
                  FxLogCount(FxLine::PostMove(kDestXyz, kDestXyz, false), 'I') == 1,
              G3Got(o));
    }
    FxReset();
    FxAttach(true);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("MOVE.10 attached: the setter moved the pawn's world position to kDest, RelativeLocation stays kStart",
              o.rc == Wirbel::TP_OK && FxEq3(FxAttachedWorldNow(), kDestXyz) && FxEq3(FxPos(), kStart),
              G3Got(o) + " world " + FxArrStr(FxAttachedWorldNow()) + " rel " + FxArrStr(FxPos()));
        check("MOVE.10 attached: cur comes from K2_GetActorLocation -- 'post-move cur=kDest ... attached=1', no freeze",
              FxLogCount(FxLine::PostMove(kDestXyz, kDestXyz, true), 'I') == 1 && FxCount(K::SetMoveMode) == 0,
              G3Got(o));
    }
    FxReset();
    FxAttach(true);
    FxRcAlways(K::GetLoc, g3::kDidNotRun);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("MOVE.10 attached getter fails: cur falls back to the raw RelativeLocation -- 'post-move cur=kStart "
              "target=kDest attached=1', and the op still answers 0",
              o.rc == Wirbel::TP_OK && FxLogCount(FxLine::PostMove(kStart, kDestXyz, true), 'I') == 1, G3Got(o));
    }

    // MOVE.11: the freeze gate is "more than 60 units off", strictly.
    FxReset();
    FxMoveMode(K::SetActorLoc, FxMove::Short, g3::kReachedEps);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        const double landed[3] = { kDest.X - g3::kReachedEps, kDest.Y, kDest.Z };
        check("MOVE.11 threshold: a landing exactly 60 short counts as reached -- no freeze WARN, no SetMovementMode",
              o.rc == Wirbel::TP_OK && FxCount(K::SetMoveMode) == 0 && FxLogPrefix(g3::kFreezePrefix) == 0 &&
                  FxLogCount(FxLine::PostMove(landed, kDestXyz, false), 'I') == 1,
              G3Got(o));
    }
    FxReset();
    FxMoveMode(K::SetActorLoc, FxMove::Short, g3::kJustPastEps);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        const double landed[3] = { kDest.X - g3::kJustPastEps, kDest.Y, kDest.Z };
        check("MOVE.11 threshold: a landing 60.5 short enters the freeze, its WARN naming where the pawn stopped",
              FxLogCount(g3::FreezeEnter(landed, true), 'W') == 1, FxLogDump());
        check("MOVE.11 threshold: the freeze sequence -- [SetActorLoc, GetLoc, SetMoveMode, SetActorLoc, SetWorldLoc, "
              "SetMoveMode, SetActorLoc, SetWorldLoc, GetLoc, StopMove, StopMoveNow]",
              FxKindsAre({ K::SetActorLoc, K::GetLoc, K::SetMoveMode, K::SetActorLoc, K::SetWorldLoc, K::SetMoveMode,
                           K::SetActorLoc, K::SetWorldLoc, K::GetLoc, K::StopMove, K::StopMoveNow }) &&
                  o.rc == Wirbel::TP_OK,
              G3Got(o));
        check("MOVE.11 oldPos before the setter: exactly one INFO 'deep-force rewrote 1' on Root_0 and no 'found no "
              "ComponentToWorld' WARN -- it searched kStart, not where the short setter left the pawn",
              FxLogCount(FxLine::DeepRewrote(1, g_root0), 'I') == 1 && FxLogCount(FxText::kDeepNone) == 0,
              FxLogDump());
        check("MOVE.11 oldPos before the setter: Root_0's ComponentToWorld copy at +0x100 holds kDest afterwards",
              FxEq3(FxVecAt(g_root0 + FxOff::kC2W, kVecD), kDestXyz), G3VecStr(g_root0 + FxOff::kC2W, kVecD));
    }
}

// ══ COMP ══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void G3Comp() {
    using K = FxKind;
    blk("COMP - TeleportViaComponent: K2_SetWorldLocation first, K2_SetRelativeLocation only on an unattached root, a "
        "false answer falls through");

    FxReset();
    FxRcs(K::SetActorLoc, { g3::kDidNotRun });
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        const FxCall swl = FxNth(K::SetWorldLoc);
        check("COMP.01 component setters: K2_SetWorldLocation is called on Root_0 with NewLocation kDest in 3 doubles",
              swl.seq != 0 && swl.instance == g_root0 && FxEq3(FxVec(swl, kSal5.loc, kVecD), kDestXyz),
              FxCallStr(swl) + " " + FxArrStr(FxVec(swl, kSal5.loc, kVecD)));
        check("COMP.01 component setters: bTeleport @0x108 arrives 1, bSweep 0, every other byte zero",
              FxRd<uint8_t>(swl, kSal5.teleport) == 1 && FxRd<uint8_t>(swl, kSal5.sweep) == 0 &&
                  FxZeroOutside(swl, { G3Span(kSal5.loc, kVecD), G3Span(kSal5.teleport, g3::kBoolBytes) }),
              FxCallStr(swl));
        check("COMP.01 component setters: one INFO 'K2_SetWorldLocation invoked OK on RootComponent' naming Root_0 and "
              "kDest",
              FxLogCount(FxLine::CompSetterOk("K2_SetWorldLocation", g_root0, kDestXyz), 'I') == 1, FxLogDump());
        check("COMP.01 component setters: a setter without a return value counts as done -- K2_SetRelativeLocation is "
              "not tried, tier 1",
              FxCount(K::SetRelLoc) == 0 && o.tier == 1 && o.rc == Wirbel::TP_OK, G3Got(o));
    }
    FxReset();
    FxSceneFns(FxSceneFnList({ .world = FxCompShape::BoolReturn }));
    FxRetByte(K::SetWorldLoc, 0);
    FxRcs(K::SetActorLoc, { g3::kDidNotRun });
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("COMP.01 component setters: a K2_SetWorldLocation answering false falls through to "
              "K2_SetRelativeLocation on Root_0, tier 1",
              o.rc == Wirbel::TP_OK && o.tier == 1 &&
                  G3CallsAre({ { K::SetActorLoc, g_hero0 }, { K::SetWorldLoc, g_root0 }, { K::SetRelLoc, g_root0 },
                               { K::GetLoc, g_hero0 }, { K::StopMove, g_pc0 }, { K::StopMoveNow, g_cmc0 } }),
              G3Got(o));
        check("COMP.01 component setters: ... K2_SetRelativeLocation logs the OK line and K2_SetWorldLocation does not",
              FxLogCount(FxLine::CompSetterOk("K2_SetRelativeLocation", g_root0, kDestXyz), 'I') == 1 &&
                  FxLogPrefix("Teleport: K2_SetWorldLocation invoked OK") == 0,
              FxLogDump());
    }
    FxReset();
    FxAttach(true);
    FxRcs(K::SetActorLoc, { g3::kDidNotRun });
    FxRcAlways(K::SetWorldLoc, g3::kDidNotRun);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("COMP.01 component setters: on an attached root K2_SetRelativeLocation is never tried -- "
              "K2_SetWorldLocation failing goes straight to tier 2",
              o.rc == Wirbel::TP_OK && o.tier == 2 && FxCount(K::SetRelLoc) == 0 && FxCount(K::SetWorldLoc) == 1,
              G3Got(o));
    }
    FxReset();
    FxSceneFns(FxSceneFnList({ .world = FxCompShape::Missing }));
    FxRcs(K::SetActorLoc, { g3::kDidNotRun });
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        const FxCall srl = FxNth(K::SetRelLoc);
        check("COMP.01 component setters: a root with only K2_SetRelativeLocation moves through it -- on Root_0, "
              "NewLocation kDest, bTeleport 1, tier 1",
              o.rc == Wirbel::TP_OK && o.tier == 1 && srl.seq != 0 && srl.instance == g_root0 &&
                  FxEq3(FxVec(srl, kSal5.loc, kVecD), kDestXyz) && FxRd<uint8_t>(srl, kSal5.teleport) == 1 &&
                  FxEq3(FxPos(), kDestXyz),
              G3Got(o));
        check("COMP.01 component setters: ... with one INFO 'K2_SetRelativeLocation invoked OK on RootComponent'",
              FxLogCount(FxLine::CompSetterOk("K2_SetRelativeLocation", g_root0, kDestXyz), 'I') == 1, FxLogDump());
    }
}

// ══ DEEP ══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void G3Deep() {
    blk("DEEP - DeepForceWorldPos called directly: when it skips, which copies match, what it counts, its window, its "
        "two lines");

    // DEEP.01: the skips -- each answers 0, writes nothing and logs nothing.
    FxReset();
    {
        const size_t lm = FxLogMark();
        const int n = DeepForceWorldPos(G3DeepChain(0, kVecD), kStart, kDestXyz);
        check("DEEP.01 skips: a root of 0 answers 0 with no line", n == 0 && FxTeleportLines(0, lm) == 0,
              FxS(n) + FxLogDump(lm));
    }
    {
        const uintptr_t blob = G3Scratch();
        FxSetVecAt(blob + g3::kCopyAt, kVecD, kStart);
        const auto snap = G3ScratchSnap();
        const size_t lm = FxLogMark();
        const int n = DeepForceWorldPos(G3DeepChain(blob, g3::kNarrowRelLoc), kStart, kDestXyz);
        check("DEEP.01 skips: a RelativeLocation narrower than 12 bytes answers 0, writes nothing, logs nothing",
              n == 0 && G3ScratchSame(snap) && FxTeleportLines(0, lm) == 0, FxS(n) + FxLogDump(lm));
    }
    {
        const uintptr_t blob = G3Scratch();
        FxSetVecAt(blob + g3::kCopyAt, kVecD, g3::kBelowL1);
        const auto snap = G3ScratchSnap();
        const size_t lm = FxLogMark();
        const int n = DeepForceWorldPos(G3DeepChain(blob, kVecD), g3::kBelowL1, kDestXyz);
        check("DEEP.01 skips: an old position of L1 size 0.75 is the degenerate origin -- 0, its planted copy kept, "
              "no line",
              n == 0 && G3ScratchSame(snap) && FxTeleportLines(0, lm) == 0,
              FxS(n) + " copy " + G3VecStr(blob + g3::kCopyAt, kVecD) + FxLogDump(lm));
    }
    {
        const uintptr_t blob = G3Scratch();
        FxSetVecAt(blob + g3::kCopyAt, kVecD, g3::kAtL1);
        const size_t lm = FxLogMark();
        const int n = DeepForceWorldPos(G3DeepChain(blob, kVecD), g3::kAtL1, kDestXyz);
        check("DEEP.01 skips: L1 size exactly 1.0 is not skipped -- its planted copy is rewritten to the target and "
              "one INFO 'rewrote 1' names the blob",
              n == 1 && FxEq3(FxVecAt(blob + g3::kCopyAt, kVecD), kDestXyz) &&
                  FxLogCount(FxLine::DeepRewrote(1, blob), 'I', lm) == 1,
              FxS(n) + " copy " + G3VecStr(blob + g3::kCopyAt, kVecD) + FxLogDump(lm));
    }
    {
        const size_t lm = FxLogMark();
        const int n = DeepForceWorldPos(G3DeepChain(g_rootNearEdge, kVecD), kStart, kDestXyz);
        check("DEEP.01 skips: a root whose 0x400-byte window runs off its page answers 0 -- both kStart copies kept, "
              "no line",
              n == 0 && FxEq3(FxVecAt(g_rootNearEdge + FxOff::kRelativeLocation, kVecD), kStart) &&
                  FxEq3(FxVecAt(g_rootNearEdge + FxOff::kC2W, kVecD), kStart) && FxTeleportLines(0, lm) == 0,
              FxS(n) + FxLogDump(lm));
    }

    // DEEP.02 / DEEP.03: which copies match (squared distance <= 100, inclusive), at both widths, and what is counted.
    FxReset();
    {
        const double edge[3] = { kStart[0] + g3::kEdgeDx, kStart[1] + g3::kEdgeDy, kStart[2] };
        const double past[3] = { kStart[0] + g3::kEdgeDx, kStart[1] + g3::kEdgeDy, kStart[2] + g3::kPastDz };
        FxSetPos(kDestXyz);   // as after the raw write: RelativeLocation no longer holds the old position
        FxSetVecAt(g_root0 + g3::kEdgeCopyAt, kVecD, edge);
        FxSetVecAt(g_root0 + g3::kPastCopyAt, kVecD, past);
        FxClearRecorders();
        const int n = DeepForceWorldPos(G3DeepChain(g_root0, kVecD), kStart, kOther);
        check("DEEP.02 matching: the copy of kStart at +0x100 and the one exactly 10 away at +0x200 are rewritten -- "
              "2",
              n == 2 && FxEq3(FxVecAt(g_root0 + FxOff::kC2W, kVecD), kOther) &&
                  FxEq3(FxVecAt(g_root0 + g3::kEdgeCopyAt, kVecD), kOther),
              FxS(n) + " +0x100 " + G3VecStr(g_root0 + FxOff::kC2W, kVecD) + " +0x200 " +
                  G3VecStr(g_root0 + g3::kEdgeCopyAt, kVecD));
        check("DEEP.02 matching: the one just past 10 away (+0x260) and RelativeLocation (kDest) are kept",
              FxEq3(FxVecAt(g_root0 + g3::kPastCopyAt, kVecD), past) && FxEq3(FxPos(), kDestXyz),
              G3VecStr(g_root0 + g3::kPastCopyAt, kVecD) + " rel " + FxArrStr(FxPos()));
        check("DEEP.03 logs: what was rewritten is one INFO 'deep-force rewrote 2 world-transform vector(s) on "
              "RootComponent Root_0'",
              FxLogCount(FxLine::DeepRewrote(2, g_root0), 'I') == 1 && FxTeleportLines() == 1, FxLogDump());
    }
    FxReset();
    {
        FxSetPos(kDestXyz, g_rootF0);
        FxClearRecorders();
        const int n = DeepForceWorldPos(G3DeepChain(g_rootF0, kVecF), kStart, kOther);
        check("DEEP.02 matching: on a float root the copy at +0x104 is found in float steps and rewritten as 3 floats",
              n == 1 && FxEq3(FxVecAt(g_rootF0 + FxOff::kC2WF, kVecF), kOther),
              FxS(n) + " +0x104 " + G3VecStr(g_rootF0 + FxOff::kC2WF, kVecF));
        check("DEEP.02 matching: ... and nothing else on it moves -- RelativeLocation kDest, its 12 guard bytes 0xA5",
              FxEq3(FxVecAt(g_rootF0 + FxOff::kRelativeLocation, kVecF), kDestXyz) &&
                  G3Guarded(g_rootF0 + FxOff::kRelLocGuard),
              G3VecStr(g_rootF0 + FxOff::kRelativeLocation, kVecF) + " guard " +
                  G3BytesStr(g_rootF0 + FxOff::kRelLocGuard, FxOff::kGuardBytes));
    }
    FxReset();
    {
        const uintptr_t ro = FxUseRoRoot();
        FxClearRecorders();
        const int n = DeepForceWorldPos(G3DeepChain(ro, kVecD), kStart, kDestXyz);
        check("DEEP.02 counts what landed: two matches on a read-only root, neither written -- 0",
              n == 0 && FxEq3(FxVecAt(ro + FxOff::kRelativeLocation, kVecD), kStart) &&
                  FxEq3(FxVecAt(ro + FxOff::kC2W, kVecD), kStart),
              FxS(n));
        check("DEEP.03 logs: nothing landed is one WARN 'deep-force found no ComponentToWorld vector matching the old "
              "position', and no INFO",
              FxLogCount(FxText::kDeepNone, 'W') == 1 && FxLogPrefix(g3::kDeepPrefix, 'I') == 0, FxLogDump());
    }

    // DEEP.04: the window is a fixed 0x400 bytes from the root, whatever its class's size. Each copy alone in Root_0,
    // with RelativeLocation and the default ComponentToWorld copy moved off the old position.
    struct Case {
        int32_t at;
        bool rewritten;
        const char* label;
    };
    const Case cases[] = {
        { g3::kLastInsideAt, true, "DEEP.04 window: a copy at +0x3E8, the last vector ending inside the window, is "
                                   "rewritten" },
        { g3::kStraddleAt, false, "DEEP.04 window: a copy at +0x3F0, which would end past the window, is kept" },
        { g3::kPastWindowAt, false, "DEEP.04 window: a copy at +0x408, past the window, is kept" },
        { g3::kPastClassAt, true, "DEEP.04 window: a copy at +0x300, past CapsuleComponent's 0x58 PropertiesSize but "
                                  "inside the window, is rewritten (D6 pin)" },
    };
    for (const Case& k : cases) {
        FxReset();
        FxSetPos(kDestXyz);
        FxSetVecAt(g_root0 + FxOff::kC2W, kVecD, kOther);
        FxSetVecAt(g_root0 + k.at, kVecD, kStart);
        FxClearRecorders();
        const int n = DeepForceWorldPos(G3DeepChain(g_root0, kVecD), kStart, kDestXyz);
        const bool ok = k.rewritten
            ? n == 1 && FxEq3(FxVecAt(g_root0 + k.at, kVecD), kDestXyz) &&
                  FxLogCount(FxLine::DeepRewrote(1, g_root0), 'I') == 1
            : n == 0 && FxEq3(FxVecAt(g_root0 + k.at, kVecD), kStart) && FxLogCount(FxText::kDeepNone, 'W') == 1;
        check(k.label, ok, FxS(n) + " copy " + G3VecStr(g_root0 + k.at, kVecD) + FxLogDump());
    }
}

// ══ FRZ ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void G3Frz() {
    using K = FxKind;
    blk("FRZ - the CMC freeze: when it starts, its call sequence and modes, the raw write inside it, and how it ends");

    // FRZ.01 / .02 / .05: both setters report success and move nothing -- a CharacterMovement that reverts them.
    FxReset();
    FxMoveMode(K::SetActorLoc, FxMove::Lies);
    FxMoveMode(K::SetWorldLoc, FxMove::Lies);
    {
        std::array<double, 3> atThaw{};
        bool sawThaw = false;
        FxOnCall([&](FxKind k, uintptr_t, int nth) {
            if (k == FxKind::SetMoveMode && nth == g3::kThawCall) {
                atThaw = FxPos();
                sawThaw = true;
            }
        });
        FxClearRecorders();
        const G3Out o = G3Recall();
        FxOnCall(nullptr);
        check("FRZ.01 enter: one WARN 'actor didn't reach target (cur=(1000.5,-2000.5,300.5)) — forcing via CMC "
              "freeze', no suffix",
              FxLogCount(g3::FreezeEnter(kStart, true), 'W') == 1, FxLogDump());
        check("FRZ.02 sequence: [SetActorLoc, GetLoc, SetMoveMode CMC_0, SetActorLoc, SetWorldLoc Root_0, SetMoveMode "
              "CMC_0, SetActorLoc, SetWorldLoc Root_0, GetLoc, StopMove, StopMoveNow]",
              G3CallsAre({ { K::SetActorLoc, g_hero0 }, { K::GetLoc, g_hero0 }, { K::SetMoveMode, g_cmc0 },
                           { K::SetActorLoc, g_hero0 }, { K::SetWorldLoc, g_root0 }, { K::SetMoveMode, g_cmc0 },
                           { K::SetActorLoc, g_hero0 }, { K::SetWorldLoc, g_root0 }, { K::GetLoc, g_hero0 },
                           { K::StopMove, g_pc0 }, { K::StopMoveNow, g_cmc0 } }),
              FxCallsStr(FxCalls()));
        const FxCall freeze = FxNth(K::SetMoveMode, 0), thaw = FxNth(K::SetMoveMode, 1);
        check("FRZ.02 sequence: the first SetMovementMode sends NewMovementMode 0 (MOVE_None), the second 3 "
              "(MOVE_Falling), NewCustomMode 0 in both",
              FxRd<uint8_t>(freeze, FxSmm::kMode) == FxSmm::kModeNone &&
                  FxRd<uint8_t>(thaw, FxSmm::kMode) == FxSmm::kModeFalling &&
                  FxRd<uint8_t>(freeze, FxSmm::kCustom) == 0 && FxRd<uint8_t>(thaw, FxSmm::kCustom) == 0,
              FxFmt("modes %u/%u custom %u/%u", static_cast<unsigned>(FxRd<uint8_t>(freeze, FxSmm::kMode)),
                    static_cast<unsigned>(FxRd<uint8_t>(thaw, FxSmm::kMode)),
                    static_cast<unsigned>(FxRd<uint8_t>(freeze, FxSmm::kCustom)),
                    static_cast<unsigned>(FxRd<uint8_t>(thaw, FxSmm::kCustom))));
        const FxCall sal1 = FxNth(K::SetActorLoc, 1), sal2 = FxNth(K::SetActorLoc, 2);
        check("FRZ.02 sequence: both of the freeze's K2_SetActorLocation calls send kDest with bTeleport 1",
              FxEq3(FxVec(sal1, kSal5.loc, kVecD), kDestXyz) && FxEq3(FxVec(sal2, kSal5.loc, kVecD), kDestXyz) &&
                  FxRd<uint8_t>(sal1, kSal5.teleport) == 1 && FxRd<uint8_t>(sal2, kSal5.teleport) == 1,
              FxCallStr(sal1) + " / " + FxCallStr(sal2));
        check("FRZ.02 sequence: the raw RelativeLocation write lands before the thaw -- RelativeLocation is kDest when "
              "SetMovementMode(3) arrives",
              sawThaw && FxEq3(atThaw, kDestXyz), FxArrStr(atThaw));
        check("FRZ.02 sequence: deep force rewrites the stale ComponentToWorld copy -- one INFO 'rewrote 1' on Root_0, "
              "+0x100 now kDest",
              FxLogCount(FxLine::DeepRewrote(1, g_root0), 'I') == 1 &&
                  FxEq3(FxVecAt(g_root0 + FxOff::kC2W, kVecD), kDestXyz),
              FxLogDump());
        check("FRZ.05 result: one INFO 'CMC-freeze force succeeded -> cur=(1500.5,-2500.5,350.5)', no STILL WARN",
              FxLogCount(g3::FreezeOk(kDestXyz), 'I') == 1 && FxLogPrefix("Teleport: CMC-freeze force STILL") == 0,
              FxLogDump());
        check("MOVE.12 post-move: after the freeze the post-move line says cur kDest, and the op answers 0",
              o.rc == Wirbel::TP_OK && FxLogCount(FxLine::PostMove(kDestXyz, kDestXyz, false), 'I') == 1, G3Got(o));
    }

    // FRZ.01: no CharacterMovement -- the freeze still runs, without the mode calls.
    FxReset();
    FxMoveMode(K::SetActorLoc, FxMove::Lies);
    FxMoveMode(K::SetWorldLoc, FxMove::Lies);
    FxSetPtr(g_hero0, FxOff::kCharacterMovement, 0);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("FRZ.01 enter: without a CharacterMovement the WARN ends ' (no CMC found)'",
              FxLogCount(g3::FreezeEnter(kStart, false), 'W') == 1, FxLogDump());
        check("FRZ.01 enter: ... and no SetMovementMode is called, while both setters are still re-asserted",
              FxCount(K::SetMoveMode) == 0 && FxCount(K::SetActorLoc) == 3 && FxCount(K::SetWorldLoc) == 2 &&
                  o.rc == Wirbel::TP_OK,
              G3Got(o));
    }

    // FRZ.02 UE4: the freeze's own raw write is 12 bytes on a float RelativeLocation.
    FxReset();
    FxUE4World();
    FxMoveMode(K::SetActorLoc, FxMove::Lies);
    FxMoveMode(K::SetWorldLoc, FxMove::Lies);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("FRZ.02 UE4: the freeze runs on the float twins -- SetMovementMode twice on CMCF_0, setters on "
              "BP_HeroF_C_0 and RootF_0",
              G3CallsAre({ { K::SetActorLoc, g_heroF0 }, { K::GetLoc, g_heroF0 }, { K::SetMoveMode, g_cmcF0 },
                           { K::SetActorLoc, g_heroF0 }, { K::SetWorldLoc, g_rootF0 }, { K::SetMoveMode, g_cmcF0 },
                           { K::SetActorLoc, g_heroF0 }, { K::SetWorldLoc, g_rootF0 }, { K::GetLoc, g_heroF0 },
                           { K::StopMove, g_pcF0 }, { K::StopMoveNow, g_cmcF0 } }) &&
                  o.rc == Wirbel::TP_OK,
              G3Got(o));
        check("FRZ.02 UE4: RelativeLocation holds kDest as 3 floats, and the 12 bytes after it still hold 0xA5",
              FxEq3(FxVecAt(g_rootF0 + FxOff::kRelativeLocation, kVecF), kDestXyz) &&
                  G3Guarded(g_rootF0 + FxOff::kRelLocGuard),
              G3VecStr(g_rootF0 + FxOff::kRelativeLocation, kVecF) + " guard " +
                  G3BytesStr(g_rootF0 + FxOff::kRelLocGuard, FxOff::kGuardBytes));
        check("FRZ.02 UE4: deep force rewrites the float ComponentToWorld copy -- one INFO 'rewrote 1' on RootF_0",
              FxLogCount(FxLine::DeepRewrote(1, g_rootF0), 'I') == 1 &&
                  FxEq3(FxVecAt(g_rootF0 + FxOff::kC2WF, kVecF), kDestXyz) &&
                  FxLogCount(g3::FreezeOk(kDestXyz), 'I') == 1,
              FxLogDump());
    }

    // FRZ.03: a SetMovementMode that did not run is logged at INFO, once per mode, and changes nothing else.
    FxReset();
    FxMoveMode(K::SetActorLoc, FxMove::Lies);
    FxMoveMode(K::SetWorldLoc, FxMove::Lies);
    FxRcAlways(K::SetMoveMode, g3::kDidNotRun);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("FRZ.03 mode rc: one INFO 'SetMovementMode(0) on CMC CMC_0 did not complete (rc=-5; -5 = still "
              "queued)'",
              FxLogCount(g3::ModeNotDone(FxSmm::kModeNone, g_cmc0, g3::kDidNotRun), 'I') == 1, FxLogDump());
        check("FRZ.03 mode rc: ... and one for SetMovementMode(3); the op still answers 0",
              FxLogCount(g3::ModeNotDone(FxSmm::kModeFalling, g_cmc0, g3::kDidNotRun), 'I') == 1 &&
                  o.rc == Wirbel::TP_OK,
              G3Got(o));
    }

    // FRZ.04: the freeze re-asserts with K2_SetActorLocation even when the move itself was K2_TeleportTo.
    FxReset();
    FxMoveMode(K::TeleportTo, FxMove::Lies);
    FxClearRecorders();
    {
        const G3Out o = G3Cursor();
        const FxCall sal1 = FxNth(K::SetActorLoc, 0), sal2 = FxNth(K::SetActorLoc, 1);
        check("FRZ.04 always K2_SetActorLocation: a cursor jump whose K2_TeleportTo did not stick enters the freeze",
              FxLogCount(g3::FreezeEnter(kStart, true), 'W') == 1 && o.rc == Wirbel::TP_OK, G3Got(o));
        check("FRZ.04 always K2_SetActorLocation: the freeze calls K2_SetActorLocation twice, K2_TeleportTo is not "
              "called again",
              FxCount(K::TeleportTo) == 1 && FxCount(K::SetActorLoc) == 2, G3Got(o));
        check("FRZ.04 always K2_SetActorLocation: both send the cursor target with bTeleport 1",
              FxEq3(FxVec(sal1, kSal5.loc, kVecD), g3::kCursorDest) &&
                  FxEq3(FxVec(sal2, kSal5.loc, kVecD), g3::kCursorDest) && FxRd<uint8_t>(sal1, kSal5.teleport) == 1 &&
                  FxRd<uint8_t>(sal2, kSal5.teleport) == 1,
              FxCallStr(sal1) + " / " + FxCallStr(sal2));
    }

    // FRZ.05: the game puts the pawn back after the thaw, so the freeze does not stick -- still TP_OK.
    FxReset();
    FxMoveMode(K::SetActorLoc, FxMove::Lies);
    FxMoveMode(K::SetWorldLoc, FxMove::Lies);
    FxOnCall([](FxKind k, uintptr_t, int nth) {
        if (k == FxKind::SetMoveMode && nth == g3::kThawCall) FxSetPos(kStart);
    });
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        FxOnCall(nullptr);
        check("FRZ.05 result: one WARN 'CMC-freeze force STILL didn't stick (cur=(1000.5,-2000.5,300.5)) — the memory "
              "write didn't hold; ...', no succeeded line",
              FxLogCount(g3::FreezeStill(kStart), 'W') == 1 && FxLogPrefix("Teleport: CMC-freeze force succeeded") == 0,
              FxLogDump());
        check("MOVE.12 post-move: TP_OK even after STILL, and the post-move line says cur kStart (D9 pin)",
              o.rc == Wirbel::TP_OK && FxLogCount(FxLine::PostMove(kStart, kDestXyz, false), 'I') == 1, G3Got(o));
    }
}

// ══ DIAG ══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void G3Diag() {
    using K = FxKind;
    blk("DIAG - DiagVisibleActor: silent without a camera or when it follows the pawn, one WARN per teleport when it "
        "cannot tell or follows another actor");

    FxReset();
    FxSetPtr(g_pc0, FxOff::kCameraManager, 0);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("DIAG.01 no camera: a controller with no PlayerCameraManager gives no 'Teleport diag' line, and the move "
              "lands",
              FxLogPrefix(FxText::kDiagPrefix) == 0 && o.rc == Wirbel::TP_OK && FxEq3(FxPos(), kDestXyz), G3Got(o));
    }
    FxReset();
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("DIAG.02 follows the pawn: ViewTarget.Target is Hero_0 (PendingViewTarget, reflected first, names "
              "Enemy_0) -- no 'Teleport diag' line",
              FxLogPrefix(FxText::kDiagPrefix) == 0 && o.rc == Wirbel::TP_OK, FxLogDump());
    }
    FxReset();
    FxSetPtr(g_cam0, FxOff::kViewTarget, 0);
    FxClearRecorders();
    {
        const G3Out first = G3Recall();
        const int afterFirst = FxLogCount(FxLine::DiagUnresolved("BP_CameraManager_C"), 'W');
        const G3Out second = G3Recall();
        check("DIAG.03 unresolved: ViewTarget.Target 0 gives one WARN 'ViewTarget.Target UNRESOLVED on camera class "
              "'BP_CameraManager_C'' per teleport -- 1, then 2 (D14 pin)",
              afterFirst == 1 && FxLogCount(FxLine::DiagUnresolved("BP_CameraManager_C"), 'W') == 2 &&
                  FxLogPrefix(FxText::kDiagPrefix) == 2,
              FxLogDump());
        check("DIAG.03 unresolved: the warning changes nothing -- both recalls answer 0",
              first.rc == Wirbel::TP_OK && second.rc == Wirbel::TP_OK, G3Got(second));
    }
    FxReset();
    FxSetClass(g_cam0, g_cCamNoViewTarget);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("DIAG.03 unresolved: a camera class with no field named exactly ViewTarget gives one WARN naming "
              "'CamNoViewTarget'",
              FxLogCount(FxLine::DiagUnresolved("CamNoViewTarget"), 'W') == 1 &&
                  FxLogPrefix(FxText::kDiagPrefix) == 1 && o.rc == Wirbel::TP_OK,
              FxLogDump());
    }
    FxReset();
    FxSetPtr(g_cam0, FxOff::kViewTarget, g_prop0);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        const std::string want =
            g3::DiagSeparate(g_prop0, "BP_Prop_C", kPropPos, g_hero0, "BP_Hero_C", kStart, /*hasMesh=*/1, /*hasCmc=*/0);
        check("DIAG.04 separate actor: one WARN 'SEPARATE visible actor' naming Prop_0 at kPropPos and Hero_0 at "
              "kStart, hasMesh=1 hasCMC=0",
              FxLogCount(want, 'W') == 1 && FxLogPrefix(FxText::kDiagPrefix) == 1, FxLogDump() + "\n  want " + want);
        bool touched = false;
        for (const auto& c : FxCalls()) touched = touched || c.instance == g_prop0 || c.instance == g_rootProp;
        check("DIAG.04 separate actor: the move still lands on Hero_0, and nothing is called on Prop_0 or its root",
              o.rc == Wirbel::TP_OK && FxEq3(FxPos(), kDestXyz) && !touched &&
                  FxCountOn(K::SetActorLoc, g_hero0) == 1,
              G3Got(o));
    }
}

// ══ ROT ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void G3Rot() {
    using K = FxKind;
    blk("ROT - SetRotation: SetControlRotation when it runs, else a raw ControlRotation write at its own width");

    FxReset();
    FxClearRecorders();
    {
        const G3Out o = G3Recall(/*hasRot=*/true);
        const FxCall rot = FxNth(K::SetCtrlRot);
        check("ROT.01 setter: one SetControlRotation, on PC_0, NewRotation kDest's (5.5, -30.5, 0) in 3 doubles",
              FxCount(K::SetCtrlRot) == 1 && rot.instance == g_pc0 &&
                  FxEq3(FxVec(rot, g3::kNewRotationAt, kVecD), kDestPyr),
              FxCallStr(rot) + " " + FxArrStr(FxVec(rot, g3::kNewRotationAt, kVecD)));
        check("ROT.01 setter: no raw write after it -- ControlRotation is what the setter left, (5.5, 329.5, 0)",
              FxEq3(FxCtrlRot(), g3::kDestPyrStored), FxArrStr(FxCtrlRot()));
        check("ROT.01 setter: no WARN, and the recall answers 0", FxTeleportLines('W') == 0 && o.rc == Wirbel::TP_OK,
              G3Got(o));
    }

    const uintptr_t ctrlNoRot = FxCtrlFnList({ .setRot = false });
    FxReset();
    FxCtrlFns(ctrlNoRot);
    FxClearRecorders();
    {
        const G3Out o = G3Recall(/*hasRot=*/true);
        check("ROT.02 raw fallback: without SetControlRotation the raw write puts (5.5, -30.5, 0) in ControlRotation",
              FxCount(K::SetCtrlRot) == 0 && FxEq3(FxCtrlRot(), kDestPyr) && o.rc == Wirbel::TP_OK &&
                  FxTeleportLines('W') == 0,
              G3Got(o) + " rot " + FxArrStr(FxCtrlRot()));
    }
    FxReset();
    FxRcs(K::SetCtrlRot, { g3::kDidNotRun });
    FxClearRecorders();
    {
        const G3Out o = G3Recall(/*hasRot=*/true);
        check("ROT.02 raw fallback: a SetControlRotation that did not run is followed by the raw write, "
              "(5.5, -30.5, 0)",
              FxCount(K::SetCtrlRot) == 1 && FxNth(K::SetCtrlRot).rc == g3::kDidNotRun &&
                  FxEq3(FxCtrlRot(), kDestPyr) && o.rc == Wirbel::TP_OK,
              G3Got(o) + " rot " + FxArrStr(FxCtrlRot()));
    }
    FxReset();
    FxCtrlFns(ctrlNoRot);
    {
        const uintptr_t ro = FxUseRoPc();
        FxClearRecorders();
        const G3Out o = G3Recall(/*hasRot=*/true);
        check("ROT.02 raw fallback: a refused raw write logs one WARN 'the ControlRotation fallback write FAILED at "
              "0x<pc+0x38>'",
              FxLogCount(g3::RotFailed(ro + FxOff::kControlRotation), 'W') == 1, FxLogDump());
        check("ROT.02 raw fallback: ... the read-only ControlRotation still holds kRot, and the recall still answers 0",
              FxEq3(FxCtrlRot(ro), kRot) && o.rc == Wirbel::TP_OK, G3Got(o) + " rot " + FxArrStr(FxCtrlRot(ro)));
    }
    FxReset();
    FxUE4World();
    FxCtrlFFns(FxCtrlFnList({ .setRot = false, .ue4 = true }));
    FxClearRecorders();
    {
        const G3Out o = G3Recall(/*hasRot=*/true);
        check("ROT.02 UE4: the raw write puts (5.5, -30.5, 0) in PCF_0's ControlRotation as 3 floats",
              FxCount(K::SetCtrlRot) == 0 && FxEq3(FxCtrlRot(g_pcF0, kVecF), kDestPyr) && o.rc == Wirbel::TP_OK,
              G3Got(o) + " rot " + FxArrStr(FxCtrlRot(g_pcF0, kVecF)));
        check("ROT.02 UE4 sentinel: the 12 bytes after it (PCF_0+0x44..0x4F) still hold 0xA5 -- 12 bytes written, "
              "not 24",
              G3Guarded(g_pcF0 + FxOff::kCtrlRotGuard), G3BytesStr(g_pcF0 + FxOff::kCtrlRotGuard, FxOff::kGuardBytes));
    }
    FxReset();
    FxSetClass(g_pc0, g_cPcNoRot);
    FxClearRecorders();
    {
        const G3Out o = G3Recall(/*hasRot=*/true);
        check("ROT.02 raw fallback: a controller with neither SetControlRotation nor a reflected ControlRotation gets "
              "nothing written -- +0x38 still kRot, no WARN",
              FxCount(K::SetCtrlRot) == 0 && FxEq3(FxCtrlRot(), kRot) && FxTeleportLines('W') == 0 &&
                  o.rc == Wirbel::TP_OK,
              G3Got(o) + " rot " + FxArrStr(FxCtrlRot()));
    }
}

// ══ STOP ══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void G3Stop() {
    using K = FxKind;
    blk("STOP - StopMovement: AController::StopMovement on the controller, StopMovementImmediately on the CMC, each "
        "skipped alone, neither changing the code");

    FxReset();
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        const FxCall stop = FxNth(K::StopMove), now = FxNth(K::StopMoveNow);
        check("STOP.01 controller: StopMovement is called on PC_0 with a one-byte buffer",
              FxCount(K::StopMove) == 1 && stop.instance == g_pc0 && stop.size == g3::kNoParamsBuffer &&
                  stop.params.size() == g3::kNoParamsBuffer,
              FxCallStr(stop));
        check("STOP.01 controller: one INFO 'AController::StopMovement on PC PC_0: rc=0'",
              FxLogCount(FxLine::StopMove(g_pc0, 0), 'I') == 1, FxLogDump());
        check("STOP.02 CMC: StopMovementImmediately is called on CMC_0 with a one-byte buffer",
              FxCount(K::StopMoveNow) == 1 && now.instance == g_cmc0 && now.size == g3::kNoParamsBuffer &&
                  now.params.size() == g3::kNoParamsBuffer,
              FxCallStr(now));
        check("STOP.02 CMC: one INFO 'StopMovementImmediately on CMC CMC_0: rc=0'",
              FxLogCount(FxLine::StopMoveNow(g_cmc0, 0), 'I') == 1 && o.rc == Wirbel::TP_OK, FxLogDump());
    }
    FxReset();
    FxRcs(K::StopMove, { g3::kDidNotRun });
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("STOP.01 controller: a StopMovement that did not run logs ': rc=-5 (did not complete; -5 = still "
              "queued)' at INFO",
              FxLogCount(FxLine::StopMove(g_pc0, g3::kDidNotRun), 'I') == 1 && o.rc == Wirbel::TP_OK, FxLogDump());
    }
    FxReset();
    FxCtrlFns(FxCtrlFnList({ .stop = false }));
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("STOP.01 controller: a controller without StopMovement gets no call and no line",
              FxCount(K::StopMove) == 0 && FxLogPrefix("Teleport: AController::StopMovement") == 0 &&
                  o.rc == Wirbel::TP_OK,
              G3Got(o));
    }
    FxReset();
    FxSetPtr(g_hero0, FxOff::kCharacterMovement, 0);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("STOP.02 CMC: a pawn without a CharacterMovement logs one INFO 'no CharacterMovement on pawn (skip "
              "velocity reset)' and calls nothing",
              FxLogCount(FxText::kNoCmcStop, 'I') == 1 && FxCount(K::StopMoveNow) == 0 && o.rc == Wirbel::TP_OK,
              G3Got(o));
    }
    FxReset();
    FxMoveCompFns(FxMoveCompFnList(false));
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("STOP.02 CMC: a CMC class without StopMovementImmediately is silent -- no call, no line of either kind",
              FxCount(K::StopMoveNow) == 0 && FxLogPrefix("Teleport: StopMovementImmediately") == 0 &&
                  FxLogCount(FxText::kNoCmcStop) == 0 && o.rc == Wirbel::TP_OK,
              G3Got(o));
    }
    FxReset();
    FxRcAlways(K::StopMove, g3::kDidNotRun);
    FxRcAlways(K::StopMoveNow, g3::kDidNotRun);
    FxClearRecorders();
    {
        const G3Out o = G3Recall();
        check("STOP.03 codes ignored: both settle calls answering -5 leave the recall at 0, tier 1",
              o.rc == Wirbel::TP_OK && o.tier == 1 && FxCount(K::StopMove) == 1 && FxCount(K::StopMoveNow) == 1 &&
                  FxLogCount(FxLine::StopMoveNow(g_cmc0, g3::kDidNotRun), 'I') == 1,
              G3Got(o));
    }
}

// ══ RT ════════════════════════════════════════════════════════════════════════════════════════════════════════════
static void G3Rt() {
    using K = FxKind;
    blk("RT - RecallTo: its own chain code returned as it is; a failed move stops before the rotation and the settle");

    FxReset();
    FxPlantLast(FxMarker(kPlanted, "Map_A"));
    FxNoGWorld();
    FxClearRecorders();
    {
        uint8_t tier = kTierSentinel;
        const int32_t rc = Wirbel::RecallLast(&tier);
        check("RT.01 chain failure: RecallLast with no GWorld answers -1, the chain's own code, calls nothing and "
              "leaves *tierOut",
              rc == Wirbel::TP_ERR_NOT_INIT && FxCountAll() == 0 && tier == kTierSentinel,
              FxFmt("rc=%d tier=0x%02X ", rc, static_cast<unsigned>(tier)) + FxCallsStr(FxCalls()));
        check("RT.01 chain failure: one INFO 'recall-last (-1234.5, 2345.5, -345.5) -> rc=-1'",
              FxLogCount(g3::RecallLastLine(kPlanted, Wirbel::TP_ERR_NOT_INIT), 'I') == 1, FxLogDump());
    }
    FxReset();
    FxPlantLast(FxMarker(kPlanted, "Map_A"));
    {
        const uintptr_t ro = FxUseRoRoot();
        FxRcAlways(K::SetActorLoc, g3::kDidNotRun);
        FxRcAlways(K::SetWorldLoc, g3::kDidNotRun);
        FxRcAlways(K::SetRelLoc, g3::kDidNotRun);
        FxClearRecorders();
        uint8_t tier = kTierSentinel;
        const int32_t rc = Wirbel::RecallLast(&tier);
        check("RT.01 move failure: a refused write answers -10 -- no SetControlRotation, no StopMovement, no "
              "StopMovementImmediately",
              rc == Wirbel::TP_ERR_WRITE_FAILED && tier == kTierSentinel &&
                  G3CallsAre({ { K::SetActorLoc, g_hero0 }, { K::SetWorldLoc, ro }, { K::SetRelLoc, ro } }),
              FxFmt("rc=%d ", rc) + FxCallsStr(FxCalls()));
        check("RT.01 move failure: one INFO 'recall-last (-1234.5, 2345.5, -345.5) -> rc=-10'",
              FxLogCount(g3::RecallLastLine(kPlanted, Wirbel::TP_ERR_WRITE_FAILED), 'I') == 1, FxLogDump());
    }
}

static void Blocks_G3() {
    G3Move();
    G3Comp();
    G3Deep();
    G3Frz();
    G3Diag();
    G3Rot();
    G3Stop();
    G3Rt();
}

// ══ G4: the marker, undo ("last") and BugIt slots, RecallExplicit and TeleportRelative ═════════════════════════════
// Blocks MRK / LAST / EXP / BUG and REL. LAST.05@start is a function of its own because main runs it before any other
// block: "last" has no public reset and FxReset empties it, so only a read made before every other op can show the
// slot empty as the process starts.

namespace G4 {

// ── the Teleport lines these blocks compare whole: Wirbel.cpp's own format strings, verbatim (em dashes as UTF-8) ──
constexpr const char* kFmtMarkerPr =
    "Teleport: marker %d saved from a PARENT-RELATIVE read -- its pose is not world coordinates";
constexpr const char* kFmtRecallMap = "Teleport: recall %d refused — map '%s' != marker map '%s'";
constexpr const char* kFmtRecallPr = "Teleport: recall %d refused -- the marker came from a PARENT-RELATIVE read "
                                     "(%.1f, %.1f, %.1f), not world coordinates; nothing moved%s";
constexpr const char* kForceToo = " (Force too)";
constexpr const char* kRecallRefusedPrefix = "Teleport: recall %d refused";   // both refusal WARNs start so
constexpr const char* kFmtLastPr = "Teleport: last position NOT updated -- the pose before this jump came from a "
                                   "PARENT-RELATIVE read (%.1f, %.1f, %.1f), not world coordinates; Recall last keeps "
                                   "the previous one";
constexpr const char* kLastPrefix = "Teleport: last position";   // the auto-saved INFO and the NOT-updated WARN alike
constexpr const char* kLastSavedPrefix = "Teleport: last position auto-saved";
constexpr const char* kMarkerPrefix = "Teleport: marker";
constexpr const char* kFmtRecallLast = "Teleport: recall-last (%.1f, %.1f, %.1f) -> rc=%d";
constexpr const char* kFmtBugItStored = "Teleport: BugIt stored (%.1f, %.1f, %.1f) map='%s'";
constexpr const char* kBugItStoredPrefix = "Teleport: BugIt stored";
constexpr const char* kFmtBugItRefused = "Teleport: BugItGo refused -- the stored BugIt (%.1f, %.1f, %.1f) came from a "
                                         "PARENT-RELATIVE read, not world coordinates; nothing moved";
constexpr const char* kFmtBugItGo = "Teleport: BugItGo (%.1f, %.1f, %.1f) -> rc=%d";
constexpr const char* kFmtRelRefused = "Teleport: relative move refused -- the current pose came from a "
                                       "PARENT-RELATIVE read (%.1f, %.1f, %.1f), not world coordinates; nothing moved";
constexpr const char* kFmtRelative = "Teleport: relative %.1f uu (%s) fwd=(%.3f, %.3f, %.3f) -> (%.1f, %.1f, %.1f)";
constexpr const char* kRel3D = "3D";
// REL.07's horizontal line typed out rather than formatted here: its rounding is part of what is pinned.
constexpr const char* kRelLineHorizontal =
    "Teleport: relative 100.0 uu (horizontal) fwd=(0.600, 0.800, 0.000) -> (1060.5, -1920.5, 300.5)";

// ── numbers ──
static_assert(kSlots == 3, "MRK plants and recalls slots 0, 1 and 2: the middle one and the last valid one");
constexpr int kSlotFirst = 0, kSlotMiddle = 1, kSlotLast = kSlots - 1;
constexpr int32_t kRcNotRun = -5;        // what Stark answers for an invoke the game thread did not run
constexpr int32_t kVisibility = 0;       // the trace channel a default engine maps to Visibility
constexpr uint8_t kTierInvoke = 1;       // TeleportPawnTo's tier when an engine setter moved the pawn
constexpr uint8_t kSourceRaw = 0, kSourceInvoke = 1;   // GetPose's outSource: the raw read, or K2_GetActorLocation
constexpr size_t kNewRotationAt = 0;     // SetControlRotation's NewRotation, its only parameter
constexpr double kStep = 100.0;          // every REL jump's distance: the design's destinations assume it
constexpr double kTol = 1e-9;            // a destination computed through a normalisation agrees to the last ulps only
constexpr double kHalfTurnDeg = 180.0;
constexpr double kStraightUp[3] = { 90.0, 0.0, 0.0 };   // Pitch 90: nothing is left of the facing once Z is dropped
constexpr double kZero3[3] = { 0.0, 0.0, 0.0 };
// kStart stepped kStep (and -kStep) along kFwd with Z dropped: (0.6, 0.8, 0) is already unit length.
constexpr double kRelDestFwd[3] = { 1060.5, -1920.5, 300.5 };
constexpr double kRelDestBack[3] = { 940.5, -2080.5, 300.5 };
constexpr int kShortCap = 4;             // a map buffer with room for "Map" and its terminator only
constexpr const char* kMapA = "Map_A";
constexpr const char* kMapB = "Map_B";
constexpr const char* kMapAShort = "Map"; // "Map_A" as a kShortCap copy keeps it
constexpr const char* kSentinelMap = "XX";
constexpr int kLongCopyChars = kMapCap - 1;   // what a TELEPORT_MAPNAME_CAP copy of World_Long keeps

// ── helpers ──
static std::string Lbl(const std::string& id, const char* what) { return id + ": " + what; }

// A Marker no Wirbel call produces -- valid, parent-relative, a sentinel pose and map -- handed to a read that must not
// touch its output.
static Wirbel::Marker SentinelMarker() { return FxMarker(FxSentinelPose(), kSentinelMap, true); }
// What "last" holds before an op that must or must not replace it: a pose no op produces, on another map.
static Wirbel::Marker PlantedLast() { return FxMarker(kPlanted, kMapB); }
// What a read of the default world saves: the hero's pose, world coordinates.
static Wirbel::Marker StartMarker(const char* map = kMapA) { return FxMarker(kStartPose, map); }
// Three valid markers on Map_A, each at its own place, so a recall that took another slot's pose shows.
static std::array<Wirbel::Marker, kSlots> ThreeMarkers() {
    return { { FxMarker(kDest, kMapA), FxMarker(FxPose(kOther, kDestPyr), kMapA),
               FxMarker(FxPose(kSpecPos, kDestPyr), kMapA) } };
}

static bool Within(const std::array<double, 3>& a, const std::array<double, 3>& b, double tol = kTol) {
    return FxEq3(a, b.data(), tol);
}
static double Rad(double deg) { return deg * std::acos(-1.0) / kHalfTurnDeg; }
static std::array<double, 3> Horizontal(const std::array<double, 3>& v) { return { v[0], v[1], 0.0 }; }
// The facing a ControlRotation describes, in UE's convention: X = cos(P)cos(Y), Y = cos(P)sin(Y), Z = sin(P).
static std::array<double, 3> Facing(const double pyr[3]) {
    const double p = Rad(pyr[0]), y = Rad(pyr[1]);
    return { std::cos(p) * std::cos(y), std::cos(p) * std::sin(y), std::sin(p) };
}
static std::array<double, 3> Unit(const std::array<double, 3>& v) {
    const double len = std::sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
    return { v[0] / len, v[1] / len, v[2] / len };
}
// Where a relative jump lands: `from` plus `dist` along `dir` made unit length.
static std::array<double, 3> Stepped(const double from[3], const std::array<double, 3>& dir, double dist) {
    const auto u = Unit(dir);
    return { from[0] + u[0] * dist, from[1] + u[1] * dist, from[2] + u[2] * dist };
}
// The first K2_SetActorLocation since `from`: its NewLocation as it arrived.
static std::array<double, 3> FirstNewLocation(size_t from) {
    return FxVec(FxNth(FxKind::SetActorLoc, 0, from), static_cast<size_t>(kSal5.loc), kSal5.vec);
}
static std::array<double, 3> FirstNewRotation(size_t from) {
    return FxVec(FxNth(FxKind::SetCtrlRot, 0, from), kNewRotationAt, kVecD);
}

// An attached pawn whose world-space read the game thread never runs: every pose read falls back to the
// parent-relative RelativeLocation and is flagged so.
static void DegradeToParentRelative() {
    FxAttach(true);
    FxRcAlways(FxKind::GetLoc, kRcNotRun);
}
// PC_0 with neither Pawn nor AcknowledgedPawn: every pose read stops at -3.
static void NoPawn() {
    FxSetPtr(g_pc0, FxOff::kPawn, 0);
    FxSetPtr(g_pc0, FxOff::kAckPawn, 0);
}

// A refused jump: its code, exactly one Teleport line (its own WARN; none when `warn` is empty), no call, the pawn
// where it stood, and neither the undo slot nor the tier byte touched.
static void Refused(const std::string& id, int32_t rc, int32_t want, const std::string& warn, size_t logs,
                    size_t calls, const Wirbel::Marker& last, uint8_t tier) {
    check(Lbl(id, "the refusal's code").c_str(), rc == want, FxS(rc) + " (want " + FxS(want) + ")");
    if (warn.empty())
        check(Lbl(id, "refused silently: no Teleport line").c_str(), FxTeleportLines(0, logs) == 0, FxLogDump(logs));
    else
        check(Lbl(id, "exactly one Teleport line, the refusal's own WARN").c_str(),
              FxTeleportLines(0, logs) == 1 && FxLogCount(warn, 'W', logs) == 1, "want " + warn + FxLogDump(logs));
    check(Lbl(id, "nothing reaches the engine").c_str(), FxCountAll(calls) == 0, FxCallsStr(FxCalls(calls)));
    check(Lbl(id, "the pawn stands where it stood").c_str(), FxEq3(FxPos(), kStart), FxArrStr(FxPos()));
    const Wirbel::Marker now = FxLastNow();
    check(Lbl(id, "the undo slot is untouched").c_str(), FxMarkerEq(now, last), FxMarkerStr(now));
    check(Lbl(id, "the tier byte is untouched").c_str(), tier == kTierSentinel, FxS(tier));
}

// The ops the undo-slot table runs, each with throwaway outputs.
static int32_t RunRecallMarker(bool force) {
    uint8_t t = kTierSentinel;
    return Wirbel::RecallMarker(kSlotFirst, force, &t);
}
static int32_t RunExplicit() {
    uint8_t t = kTierSentinel;
    return Wirbel::RecallExplicit(kDest, true, &t, false);
}
static int32_t RunCursor(bool fallbackToCenter) {
    Wirbel::Pose hit = FxSentinelPose();
    uint8_t t = kTierSentinel;
    bool usedCenter = false;
    return Wirbel::TeleportToCursor(kZOffset, kVisibility, fallbackToCenter, &hit, &t, &usedCenter);
}
static int32_t RunBugItGo() {
    uint8_t t = kTierSentinel;
    return Wirbel::BugItGo(&t);
}
static int32_t RunBugItSave() {
    Wirbel::Pose p = FxSentinelPose();
    char map[kMapCap] = {};
    uint8_t src = kByteSentinel;
    bool pr = false;
    return Wirbel::BugItSave(p, map, kMapCap, &src, &pr);
}
static int32_t RunRelative() {
    Wirbel::Pose p = FxSentinelPose();
    uint8_t t = kTierSentinel;
    bool known = false;
    return Wirbel::TeleportRelative(kStep, true, p, &t, &known);
}

// One row of the undo-slot table: the op, what it must answer (so the check runs on the path it names), and the setup
// that puts it there, run after FxReset and before "last" is planted.
struct SlotOp {
    const char* what;
    std::function<void()> setup;
    std::function<int32_t()> run;
    int32_t want;
};

// ══ MRK ══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockMrk() {
    blk("MRK - the marker slots: bounds, save, copy, clear, the map and parent-relative refusals, the accepted recall");

    // ── MRK.01: every slot export refuses -1 and kSlots before it reads or writes anything ──
    {
        FxReset();
        const auto planted = ThreeMarkers();
        for (int s = 0; s < kSlots; ++s) FxPlantMarker(s, planted[static_cast<size_t>(s)]);
        FxPlantLast(PlantedLast());
        for (const int slot : { -1, kSlots }) {
            const std::string id = FxFmt("MRK.01 bad slot %d", slot);
            uint8_t tier = kTierSentinel;
            const size_t logs = FxLogMark(), calls = FxCallMark();
            const int32_t save = Wirbel::SaveMarker(slot);
            const int32_t recall = Wirbel::RecallMarker(slot, false, &tier);
            const int32_t forced = Wirbel::RecallMarker(slot, true, &tier);
            const int32_t clear = Wirbel::ClearMarker(slot);
            Wirbel::Marker out = SentinelMarker();
            const int32_t get = Wirbel::GetMarker(slot, out);
            check(Lbl(id, "SaveMarker answers -6").c_str(), save == Wirbel::TP_ERR_EMPTY_MARKER, FxS(save));
            check(Lbl(id, "RecallMarker answers -6, with Force and without").c_str(),
                  recall == Wirbel::TP_ERR_EMPTY_MARKER && forced == Wirbel::TP_ERR_EMPTY_MARKER,
                  FxS(recall) + " / " + FxS(forced));
            check(Lbl(id, "ClearMarker answers -6").c_str(), clear == Wirbel::TP_ERR_EMPTY_MARKER, FxS(clear));
            check(Lbl(id, "GetMarker answers -6").c_str(), get == Wirbel::TP_ERR_EMPTY_MARKER, FxS(get));
            check(Lbl(id, "GetMarker leaves its output as it was handed in").c_str(),
                  FxMarkerEq(out, SentinelMarker()), FxMarkerStr(out));
            check(Lbl(id, "no chain resolved, no line, no call").c_str(),
                  FxTeleportLines(0, logs) == 0 && FxCountAll(calls) == 0,
                  FxLogDump(logs) + " | " + FxCallsStr(FxCalls(calls)));
            check(Lbl(id, "the tier byte is untouched").c_str(), tier == kTierSentinel, FxS(tier));
        }
        bool kept = true;
        std::string got;
        for (int s = 0; s < kSlots; ++s) {
            const Wirbel::Marker m = FxMarkerNow(s);
            kept = kept && FxMarkerEq(m, planted[static_cast<size_t>(s)]);
            got += FxMarkerStr(m) + "; ";
        }
        check("MRK.01 bad slot: slots 0..2 are as planted -- no out-of-range save or clear reached them", kept, got);
        check("MRK.01 bad slot: the undo slot is as planted", FxMarkerEq(FxLastNow(), PlantedLast()),
              FxMarkerStr(FxLastNow()));
    }

    // ── MRK.02: a save, and a failed save that leaves the slot ──
    {
        FxReset();
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::SaveMarker(kSlotMiddle);
        Wirbel::Marker out = SentinelMarker();
        const int32_t get = Wirbel::GetMarker(kSlotMiddle, out);
        check("MRK.02 save: SaveMarker(1) answers 0", rc == Wirbel::TP_OK, FxS(rc));
        check("MRK.02 save: GetMarker(1) answers 0 with {Valid, kStart + kRot, \"Map_A\", world coordinates}",
              get == Wirbel::TP_OK && FxMarkerEq(out, StartMarker()), FxS(get) + " " + FxMarkerStr(out));
        check("MRK.02 save: INFO \"marker 1 saved (1000.5, -2000.5, 300.5) map='Map_A'\" once",
              FxLogCount(FxLine::MarkerSaved(kSlotMiddle, kStart, kMapA), 'I', logs) == 1, FxLogDump(logs));
        check("MRK.02 save: no WARN -- a world read is not flagged", FxTeleportLines('W', logs) == 0, FxLogDump(logs));
        check("MRK.02 save: an unattached save reads memory only -- no call", FxCountAll(calls) == 0,
              FxCallsStr(FxCalls(calls)));

        const Wirbel::Marker saved = FxMarkerNow(kSlotMiddle);
        FxNoGWorld();
        const size_t logs2 = FxLogMark();
        const int32_t failed = Wirbel::SaveMarker(kSlotMiddle);
        check("MRK.02 save, no GWorld: SaveMarker(1) answers -1", failed == Wirbel::TP_ERR_NOT_INIT, FxS(failed));
        check("MRK.02 save, no GWorld: slot 1 still holds the earlier save",
              FxMarkerEq(FxMarkerNow(kSlotMiddle), saved), FxMarkerStr(FxMarkerNow(kSlotMiddle)));
        check("MRK.02 save, no GWorld: no marker line", FxLogPrefix(kMarkerPrefix, 0, logs2) == 0, FxLogDump(logs2));
    }

    // ── MRK.02: a parent-relative read is saved, flagged, and WARNed before the saved INFO ──
    {
        FxReset();
        DegradeToParentRelative();
        const size_t logs = FxLogMark();
        const int32_t rc = Wirbel::SaveMarker(kSlotMiddle);
        const Wirbel::Marker slot = FxMarkerNow(kSlotMiddle);
        const std::string warn = FxFmt(kFmtMarkerPr, kSlotMiddle);
        const uint64_t warnSeq = FxLogSeq(warn, 'W', logs);
        const uint64_t infoSeq = FxLogSeq(FxLine::MarkerSaved(kSlotMiddle, kStart, kMapA), 'I', logs);
        check("MRK.02 parent-relative: SaveMarker(1) still saves -- 0", rc == Wirbel::TP_OK, FxS(rc));
        check("MRK.02 parent-relative: slot 1 holds the raw RelativeLocation flagged parent-relative",
              FxMarkerEq(slot, FxMarker(kStartPose, kMapA, true)), FxMarkerStr(slot));
        check("MRK.02 parent-relative: WARN \"marker 1 saved from a PARENT-RELATIVE read -- ...\" once",
              FxLogCount(warn, 'W', logs) == 1, FxLogDump(logs));
        check("MRK.02 parent-relative: the WARN is stamped before the saved INFO",
              warnSeq != 0 && infoSeq != 0 && warnSeq < infoSeq, FxS(static_cast<long long>(warnSeq)) + " / " +
                  FxS(static_cast<long long>(infoSeq)));
    }

    // ── MRK.03: GetMarker copies an empty slot out too ──
    {
        FxReset();
        Wirbel::Marker out = SentinelMarker();
        const int32_t rc = Wirbel::GetMarker(kSlotFirst, out);
        check("MRK.03 copy of empty: GetMarker(0) on an empty slot answers -6", rc == Wirbel::TP_ERR_EMPTY_MARKER,
              FxS(rc));
        check("MRK.03 copy of empty: the empty slot is copied out -- Valid false, map \"\", a zero pose, unflagged",
              FxMarkerEq(out, Wirbel::Marker{}), FxMarkerStr(out));
    }

    // ── MRK.04: ClearMarker empties the whole slot, twice over, silently, and only its own ──
    {
        FxReset();
        const Wirbel::Marker kept = FxMarker(FxPose(kOther, kDestPyr), kMapA);
        FxPlantMarker(kSlotFirst, FxMarker(kDest, kMapB, true));
        FxPlantMarker(kSlotMiddle, kept);
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t first = Wirbel::ClearMarker(kSlotFirst);
        const Wirbel::Marker after = FxMarkerNow(kSlotFirst);
        const int32_t second = Wirbel::ClearMarker(kSlotFirst);
        check("MRK.04 clear: ClearMarker(0) answers 0, and again on the slot it just emptied",
              first == Wirbel::TP_OK && second == Wirbel::TP_OK, FxS(first) + " / " + FxS(second));
        check("MRK.04 clear: slot 0 is Marker{} -- pose, map and flag gone with Valid",
              FxMarkerEq(after, Wirbel::Marker{}) && FxMarkerEq(FxMarkerNow(kSlotFirst), Wirbel::Marker{}),
              FxMarkerStr(after));
        check("MRK.04 clear: slot 1 is untouched", FxMarkerEq(FxMarkerNow(kSlotMiddle), kept),
              FxMarkerStr(FxMarkerNow(kSlotMiddle)));
        check("MRK.04 clear: no Teleport line, no call", FxTeleportLines(0, logs) == 0 && FxCountAll(calls) == 0,
              FxLogDump(logs) + " | " + FxCallsStr(FxCalls(calls)));
    }

    // ── MRK.05: an empty slot recalls nothing, with or without Force ──
    {
        FxReset();
        FxPlantLast(PlantedLast());
        for (const bool force : { false, true }) {
            uint8_t tier = kTierSentinel;
            const size_t logs = FxLogMark(), calls = FxCallMark();
            const int32_t rc = Wirbel::RecallMarker(kSlotFirst, force, &tier);
            Refused(force ? "MRK.05 empty recall, Force" : "MRK.05 empty recall", rc, Wirbel::TP_ERR_EMPTY_MARKER, "",
                    logs, calls, PlantedLast(), tier);
        }
    }

    // ── MRK.06: no world: the map check reads "" and refuses; Force gets past it to the chain's own -1 ──
    {
        FxReset();
        FxPlantMarker(kSlotFirst, FxMarker(kDest, kMapA));
        FxPlantLast(PlantedLast());
        FxNoGWorld();
        {
            uint8_t tier = kTierSentinel;
            const size_t logs = FxLogMark(), calls = FxCallMark();
            const int32_t rc = Wirbel::RecallMarker(kSlotFirst, false, &tier);
            Refused("MRK.06 no world", rc, Wirbel::TP_ERR_MAP_MISMATCH, FxFmt(kFmtRecallMap, kSlotFirst, "", kMapA),
                    logs, calls, PlantedLast(), tier);
        }
        {
            uint8_t tier = kTierSentinel;
            const size_t logs = FxLogMark(), calls = FxCallMark();
            const int32_t rc = Wirbel::RecallMarker(kSlotFirst, true, &tier);
            check("MRK.06 no world, Force: past the map check, the chain's own -1 comes back", rc ==
                  Wirbel::TP_ERR_NOT_INIT, FxS(rc));
            check("MRK.06 no world, Force: INFO \"recall 0 -> rc=-1\" and no other Teleport line",
                  FxLogCount(FxLine::Recall(kSlotFirst, Wirbel::TP_ERR_NOT_INIT), 'I', logs) == 1 &&
                      FxTeleportLines(0, logs) == 1,
                  FxLogDump(logs));
            check("MRK.06 no world, Force: no call, and the undo slot is untouched -- its own read failed too",
                  FxCountAll(calls) == 0 && FxMarkerEq(FxLastNow(), PlantedLast()), FxMarkerStr(FxLastNow()));
        }
    }

    // ── MRK.07: the map compare ignores case ──
    {
        FxReset();
        FxPlantMarker(kSlotFirst, FxMarker(kDest, kMapA));
        FxWorld(g_worldCase);
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::RecallMarker(kSlotFirst, false, &tier);
        check("MRK.07 case-insensitive: a \"Map_A\" marker on world \"map_a\" is recalled without Force -- 0",
              rc == Wirbel::TP_OK, FxS(rc) + FxLogDump(logs));
        check("MRK.07 case-insensitive: no refusal, and the pawn is sent to the marker",
              FxLogPrefix(FxFmt(kRecallRefusedPrefix, kSlotFirst), 0, logs) == 0 &&
                  FxEq3(FirstNewLocation(calls), kDestXyz),
              FxArrStr(FirstNewLocation(calls)) + FxLogDump(logs));
    }

    // ── MRK.07b: map names as long as real ones round-trip through save and recall without Force ──
    for (const uintptr_t world : { g_worldLong, g_world20 }) {
        FxReset();
        FxWorld(world);
        const bool isLong = world == g_worldLong;
        const std::string id = isLong ? "MRK.07b 140-char map name" : "MRK.07b 20-char map name";
        const std::string want =
            isLong ? std::string(static_cast<size_t>(kLongCopyChars), 'L') : std::string(kName20);
        const int32_t save = Wirbel::SaveMarker(kSlotFirst);
        const Wirbel::Marker saved = FxMarkerNow(kSlotFirst);
        check(Lbl(id, "SaveMarker(0) answers 0, and the slot keeps the name as far as the slot holds it").c_str(),
              save == Wirbel::TP_OK && saved.Valid && want == saved.MapName,
              FxS(save) + " '" + std::string(saved.MapName) + "'");
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark();
        const int32_t rc = Wirbel::RecallMarker(kSlotFirst, false, &tier);
        check(Lbl(id, "recalled on the same map without Force -- 0, no refusal WARN").c_str(),
              rc == Wirbel::TP_OK && FxLogPrefix(FxFmt(kRecallRefusedPrefix, kSlotFirst), 0, logs) == 0,
              FxS(rc) + FxLogDump(logs));
    }

    // ── MRK.08: the refusals: parent-relative first (Force or not, any map), then the map without Force ──
    {
        struct Case {
            const char* id;
            bool parentRelative;
            uintptr_t world;
            bool force;
            int32_t want;
        };
        const Case cases[] = {
            { "MRK.08 parent-relative marker", true, g_worldA, false, Wirbel::TP_ERR_PARENT_RELATIVE },
            { "MRK.08 parent-relative marker, Force", true, g_worldA, true, Wirbel::TP_ERR_PARENT_RELATIVE },
            { "MRK.08 parent-relative marker on Map_B", true, g_worldB, false, Wirbel::TP_ERR_PARENT_RELATIVE },
            { "MRK.08 parent-relative marker on Map_B, Force", true, g_worldB, true, Wirbel::TP_ERR_PARENT_RELATIVE },
            { "MRK.08 world marker on Map_B", false, g_worldB, false, Wirbel::TP_ERR_MAP_MISMATCH },
        };
        for (const Case& c : cases) {
            FxReset();
            FxPlantMarker(kSlotFirst, FxMarker(kDest, kMapA, c.parentRelative));
            FxPlantLast(PlantedLast());
            FxWorld(c.world);
            uint8_t tier = kTierSentinel;
            const size_t logs = FxLogMark(), calls = FxCallMark();
            const int32_t rc = Wirbel::RecallMarker(kSlotFirst, c.force, &tier);
            const std::string warn =
                c.parentRelative
                    ? FxFmt(kFmtRecallPr, kSlotFirst, kDest.X, kDest.Y, kDest.Z, c.force ? kForceToo : "")
                    : FxFmt(kFmtRecallMap, kSlotFirst, kMapB, kMapA);
            Refused(c.id, rc, c.want, warn, logs, calls, PlantedLast(), tier);
        }
    }
    // The flag the refusal reads is the one SaveMarker stored from the read itself.
    {
        FxReset();
        DegradeToParentRelative();
        const int32_t save = Wirbel::SaveMarker(kSlotFirst);
        check("MRK.08 saved from a parent-relative read: the save itself answers 0 (setup)", save == Wirbel::TP_OK,
              FxS(save));
        FxPlantLast(PlantedLast());
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::RecallMarker(kSlotFirst, true, &tier);
        Refused("MRK.08 saved from a parent-relative read, Force", rc, Wirbel::TP_ERR_PARENT_RELATIVE,
                FxFmt(kFmtRecallPr, kSlotFirst, kStart[0], kStart[1], kStart[2], kForceToo), logs, calls,
                PlantedLast(), tier);
    }

    // ── MRK.09: the accepted recall: undo point, move, rotation, settle ──
    {
        FxReset();
        FxPlantMarker(kSlotFirst, FxMarker(kDest, kMapA));
        FxPlantLast(PlantedLast());
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::RecallMarker(kSlotFirst, false, &tier);
        Wirbel::Marker last = SentinelMarker();
        const int32_t lastRc = Wirbel::GetLast(last);
        const FxCall setLoc = FxNth(FxKind::SetActorLoc, 0, calls);
        const FxCall rot = FxNth(FxKind::SetCtrlRot, 0, calls);
        check("MRK.09 accepted: RecallMarker(0) answers 0", rc == Wirbel::TP_OK, FxS(rc) + FxLogDump(logs));
        check("MRK.09 accepted: the undo slot holds the pose before the jump -- kStart + kRot on Map_A",
              lastRc == Wirbel::TP_OK && FxMarkerEq(last, StartMarker()), FxMarkerStr(last));
        check("MRK.09 accepted: the calls are [SetActorLoc, GetLoc, SetCtrlRot, StopMove, StopMoveNow]",
              FxKindsAre({ FxKind::SetActorLoc, FxKind::GetLoc, FxKind::SetCtrlRot, FxKind::StopMove,
                           FxKind::StopMoveNow },
                         calls),
              FxKindsStr(FxKinds(calls)));
        check("MRK.09 accepted: K2_SetActorLocation on the hero carries the marker's position",
              setLoc.instance == g_hero0 && FxEq3(FirstNewLocation(calls), kDestXyz),
              FxNameOf(setLoc.instance) + " " + FxArrStr(FirstNewLocation(calls)));
        check("MRK.09 accepted: SetControlRotation on PC_0 carries the marker's rotation (5.5, -30.5, 0)",
              rot.instance == g_pc0 && FxEq3(FirstNewRotation(calls), kDestPyr),
              FxNameOf(rot.instance) + " " + FxArrStr(FirstNewRotation(calls)));
        check("MRK.09 accepted: tier 1", tier == kTierInvoke, FxS(tier));
        check("MRK.09 accepted: INFO \"recall 0 -> rc=0\" once",
              FxLogCount(FxLine::Recall(kSlotFirst, Wirbel::TP_OK), 'I', logs) == 1, FxLogDump(logs));
        check("MRK.09 accepted: the hero stands at the marker", FxEq3(FxPos(), kDestXyz), FxArrStr(FxPos()));
    }
    {
        FxReset();
        FxPlantMarker(kSlotFirst, FxMarker(kDest, kMapA));
        FxWorld(g_worldB);
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::RecallMarker(kSlotFirst, true, &tier);
        check("MRK.09 Force on Map_B: a Map_A marker is recalled -- 0 and INFO \"recall 0 -> rc=0\"",
              rc == Wirbel::TP_OK && FxLogCount(FxLine::Recall(kSlotFirst, Wirbel::TP_OK), 'I', logs) == 1,
              FxS(rc) + FxLogDump(logs));
        check("MRK.09 Force on Map_B: the pawn is sent to the marker, and the undo slot names the map it left",
              FxEq3(FirstNewLocation(calls), kDestXyz) && FxMarkerEq(FxLastNow(), StartMarker(kMapB)),
              FxArrStr(FirstNewLocation(calls)) + " " + FxMarkerStr(FxLastNow()));
    }

    // ── MRK.09b: every slot recalls its own pose; ClearMarker clears only its own; the last slot is usable ──
    {
        FxReset();
        const auto planted = ThreeMarkers();
        for (int s = 0; s < kSlots; ++s) FxPlantMarker(s, planted[static_cast<size_t>(s)]);
        for (const int slot : { kSlotMiddle, kSlotLast }) {
            const std::string id = FxFmt("MRK.09b recall slot %d", slot);
            const auto own = FxXyzOf(planted[static_cast<size_t>(slot)].P);
            uint8_t tier = kTierSentinel;
            const size_t logs = FxLogMark(), calls = FxCallMark();
            const int32_t rc = Wirbel::RecallMarker(slot, false, &tier);
            check(Lbl(id, "answers 0 and logs INFO \"recall <slot> -> rc=0\"").c_str(),
                  rc == Wirbel::TP_OK && FxLogCount(FxLine::Recall(slot, Wirbel::TP_OK), 'I', logs) == 1,
                  FxS(rc) + FxLogDump(logs));
            check(Lbl(id, "K2_SetActorLocation carries that slot's own position, not slot 0's").c_str(),
                  FxEq3(FirstNewLocation(calls), own.data()),
                  FxArrStr(FirstNewLocation(calls)) + " want " + FxArrStr(own));
        }
        const int32_t clear = Wirbel::ClearMarker(kSlotLast);
        check("MRK.09b ClearMarker(2): answers 0 and empties slot 2 only -- slots 0 and 1 are as planted",
              clear == Wirbel::TP_OK && FxMarkerEq(FxMarkerNow(kSlotLast), Wirbel::Marker{}) &&
                  FxMarkerEq(FxMarkerNow(kSlotFirst), planted[kSlotFirst]) &&
                  FxMarkerEq(FxMarkerNow(kSlotMiddle), planted[kSlotMiddle]),
              FxS(clear) + " " + FxMarkerStr(FxMarkerNow(kSlotFirst)) + "; " + FxMarkerStr(FxMarkerNow(kSlotMiddle)) +
                  "; " + FxMarkerStr(FxMarkerNow(kSlotLast)));
        // The recalls moved and turned the hero; put the default pose back so the save below has a known answer.
        FxSetPos(kStart);
        FxSetCtrlRot(kRot);
        const int32_t save = Wirbel::SaveMarker(kSlotLast);
        Wirbel::Marker out = SentinelMarker();
        const int32_t get = Wirbel::GetMarker(kSlotLast, out);
        check("MRK.09b the last slot: SaveMarker(2) and GetMarker(2) answer 0 with the pose saved",
              save == Wirbel::TP_OK && get == Wirbel::TP_OK && FxMarkerEq(out, StartMarker()),
              FxS(save) + " / " + FxS(get) + " " + FxMarkerStr(out));
    }
}

// ══ LAST ═════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockLast() {
    blk("LAST - the undo slot: what replaces it and when, and RecallLast");

    // ── LAST.01: a read that fails keeps the slot, silently ──
    {
        FxReset();
        FxPlantLast(PlantedLast());
        NoPawn();
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::RecallExplicit(kDest, true, &tier, false);
        check("LAST.01 read fails: no pawn -- RecallExplicit answers -3", rc == Wirbel::TP_ERR_NO_PAWN, FxS(rc));
        check("LAST.01 read fails: the undo slot keeps its previous pose", FxMarkerEq(FxLastNow(), PlantedLast()),
              FxMarkerStr(FxLastNow()));
        check("LAST.01 read fails: silent -- no \"last position\" line of either kind",
              FxLogPrefix(kLastPrefix, 0, logs) == 0, FxLogDump(logs));
        check("LAST.01 read fails: no call", FxCountAll(calls) == 0, FxCallsStr(FxCalls(calls)));
    }

    // ── LAST.01: a parent-relative read keeps the slot, says so, and the jump still runs ──
    {
        FxReset();
        FxPlantLast(PlantedLast());
        DegradeToParentRelative();
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::RecallExplicit(kDest, true, &tier, false);
        check("LAST.01 parent-relative: the undo slot keeps its previous pose", FxMarkerEq(FxLastNow(), PlantedLast()),
              FxMarkerStr(FxLastNow()));
        check("LAST.01 parent-relative: WARN \"last position NOT updated -- ... (1000.5, -2000.5, 300.5) ...\" once",
              FxLogCount(FxFmt(kFmtLastPr, kStart[0], kStart[1], kStart[2]), 'W', logs) == 1, FxLogDump(logs));
        check("LAST.01 parent-relative: no auto-saved line", FxLogPrefix(kLastSavedPrefix, 0, logs) == 0,
              FxLogDump(logs));
        check("LAST.01 parent-relative: the jump still runs -- K2_SetActorLocation carries kDest, and it answers 0",
              rc == Wirbel::TP_OK && FxCount(FxKind::SetActorLoc, calls) >= 1 &&
                  FxEq3(FirstNewLocation(calls), kDestXyz),
              FxS(rc) + " " + FxCallsStr(FxCalls(calls)));
    }

    // ── LAST.01 / LAST.05: a world read replaces the slot, says so, and GetLast reads it back ──
    {
        FxReset();
        FxPlantLast(FxMarker(kPlanted, kMapB, true));
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark();
        const int32_t rc = Wirbel::RecallExplicit(kDest, true, &tier, false);
        Wirbel::Marker out = SentinelMarker();
        const int32_t get = Wirbel::GetLast(out);
        check("LAST.01 stored: the recall answers 0", rc == Wirbel::TP_OK, FxS(rc));
        check("LAST.01 stored: INFO \"last position auto-saved (1000.5, -2000.5, 300.5) map='Map_A'\" once",
              FxLogCount(FxLine::LastSaved(kStart, kMapA), 'I', logs) == 1, FxLogDump(logs));
        check("LAST.01 stored: GetLast is the pose before the jump, world coordinates (no flag)",
              FxMarkerEq(out, StartMarker()), FxMarkerStr(out));
        check("LAST.05 GetLast: 0 once a jump has saved, and the copy is the slot itself",
              get == Wirbel::TP_OK && FxMarkerEq(out, FxLastNow()), FxS(get) + " " + FxMarkerStr(FxLastNow()));
    }

    // ── LAST.02: every jump that runs replaces the slot; nothing else does ──
    {
        const std::vector<SlotOp> updaters = {
            { "RecallMarker", [] { FxPlantMarker(kSlotFirst, FxMarker(kDest, kMapA)); },
              [] { return RunRecallMarker(false); }, Wirbel::TP_OK },
            { "RecallExplicit", nullptr, [] { return RunExplicit(); }, Wirbel::TP_OK },
            { "cursor hit", [] { FxHit(FxKind::HitCursor, kVisibility); }, [] { return RunCursor(true); },
              Wirbel::TP_OK },
            { "BugItGo", [] { FxPlantBugIt(FxMarker(kDest, kMapA)); }, [] { return RunBugItGo(); }, Wirbel::TP_OK },
            { "TeleportRelative", nullptr, [] { return RunRelative(); }, Wirbel::TP_OK },
        };
        for (const SlotOp& op : updaters) {
            FxReset();
            if (op.setup) op.setup();
            FxPlantLast(PlantedLast());
            const int32_t rc = op.run();
            const std::string id = std::string("LAST.02 updater ") + op.what;
            check(Lbl(id, "the jump answers 0").c_str(), rc == op.want, FxS(rc));
            check(Lbl(id, "the undo slot now holds the pose before the jump -- kStart on Map_A").c_str(),
                  FxMarkerEq(FxLastNow(), StartMarker()), FxMarkerStr(FxLastNow()));
        }

        const std::vector<SlotOp> others = {
            { "RecallLast", nullptr,
              [] {
                  uint8_t t = kTierSentinel;
                  return Wirbel::RecallLast(&t);
              },
              Wirbel::TP_OK },
            { "SaveMarker", nullptr, [] { return Wirbel::SaveMarker(kSlotFirst); }, Wirbel::TP_OK },
            { "BugItSave", nullptr, [] { return RunBugItSave(); }, Wirbel::TP_OK },
            { "GetPose", nullptr,
              [] {
                  Wirbel::Pose p{};
                  char map[kMapCap] = {};
                  uint8_t src = kByteSentinel;
                  return Wirbel::GetPose(p, map, kMapCap, &src);
              },
              Wirbel::TP_OK },
            { "GetPoseAndMovement", nullptr,
              [] {
                  Wirbel::Pose p{};
                  char map[kMapCap] = {};
                  uint8_t src = kByteSentinel;
                  Wirbel::MovementState mv{};
                  return Wirbel::GetPoseAndMovement(p, map, kMapCap, &src, mv);
              },
              Wirbel::TP_OK },
            { "GetPov", nullptr,
              [] {
                  Wirbel::Pov pov{};
                  return Wirbel::GetPov(pov);
              },
              Wirbel::TP_OK },
            { "GetTrainerOffsets", nullptr,
              [] {
                  Wirbel::TrainerOffsets t{};
                  return Wirbel::GetTrainerOffsets(t);
              },
              Wirbel::TP_OK },
            { "RecallMarker -16", [] { FxPlantMarker(kSlotFirst, FxMarker(kDest, kMapA, true)); },
              [] { return RunRecallMarker(false); }, Wirbel::TP_ERR_PARENT_RELATIVE },
            { "RecallMarker -7", [] { FxPlantMarker(kSlotFirst, FxMarker(kDest, kMapB)); },
              [] { return RunRecallMarker(false); }, Wirbel::TP_ERR_MAP_MISMATCH },
            { "RecallMarker -6", nullptr, [] { return RunRecallMarker(false); }, Wirbel::TP_ERR_EMPTY_MARKER },
            { "BugItGo -16", [] { FxPlantBugIt(FxMarker(kDest, kMapA, true)); }, [] { return RunBugItGo(); },
              Wirbel::TP_ERR_PARENT_RELATIVE },
            { "BugItGo -6", nullptr, [] { return RunBugItGo(); }, Wirbel::TP_ERR_EMPTY_MARKER },
            { "cursor -9", [] { FxRetByte(FxKind::MousePos, 0); }, [] { return RunCursor(false); },
              Wirbel::TP_ERR_NO_CURSOR },
            { "cursor -8", nullptr, [] { return RunCursor(true); }, Wirbel::TP_ERR_NO_HIT },
            { "cursor -4",
              [] { FxPcFns(FxPcFnList({ .deproject = FxDeprojShape::Missing })); },
              [] { return RunCursor(true); }, Wirbel::TP_ERR_REFLECTION },
            { "cursor -5", [] { FxRcAlways(FxKind::Deproject, kRcNotRun); }, [] { return RunCursor(true); },
              Wirbel::TP_ERR_INVOKE },
            { "relative -16", [] { DegradeToParentRelative(); }, [] { return RunRelative(); },
              Wirbel::TP_ERR_PARENT_RELATIVE },
            { "relative -4", [] { FxAnswer(FxKind::Forward, kZero3); }, [] { return RunRelative(); },
              Wirbel::TP_ERR_REFLECTION },
        };
        for (const SlotOp& op : others) {
            FxReset();
            if (op.setup) op.setup();
            FxPlantLast(PlantedLast());
            const int32_t rc = op.run();
            const std::string id = std::string("LAST.02 non-updater ") + op.what;
            check(Lbl(id, "the op answers the code that names its path").c_str(), rc == op.want,
                  FxS(rc) + " (want " + FxS(op.want) + ")");
            check(Lbl(id, "the undo slot keeps the planted pose").c_str(), FxMarkerEq(FxLastNow(), PlantedLast()),
                  FxMarkerStr(FxLastNow()));
        }
    }

    // ── LAST.03: the slot is replaced BEFORE the move, so a move that then fails has already replaced it ──
    {
        FxReset();
        FxPlantLast(PlantedLast());
        FxUseRoRoot();
        for (const FxKind k : { FxKind::SetActorLoc, FxKind::SetWorldLoc, FxKind::SetRelLoc }) FxRcAlways(k, kRcNotRun);
        uint8_t tier = kTierSentinel;
        const int32_t rc = Wirbel::RecallExplicit(kDest, true, &tier, false);
        check("LAST.03 undo point first: every setter refused and the raw write fails -- -10",
              rc == Wirbel::TP_ERR_WRITE_FAILED, FxS(rc));
        check("LAST.03 undo point first: the undo slot was already replaced with the pose before the jump",
              FxMarkerEq(FxLastNow(), StartMarker()), FxMarkerStr(FxLastNow()));
    }

    // ── LAST.04: RecallLast: refuses an empty slot; otherwise goes there and leaves the slot as it was ──
    {
        FxReset();
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::RecallLast(&tier);
        Refused("LAST.04 recall-last, empty", rc, Wirbel::TP_ERR_EMPTY_MARKER, "", logs, calls, Wirbel::Marker{},
                tier);
    }
    {
        FxReset();
        // Parent-relative and on another map: RecallLast checks neither.
        const Wirbel::Marker planted = FxMarker(kPlanted, kMapB, true);
        FxPlantLast(planted);
        const auto plantedXyz = FxXyzOf(planted.P), plantedPyr = FxPyrOf(planted.P);
        constexpr int kRounds = 2;   // the second proves the first left the slot to return to
        for (int round = 1; round <= kRounds; ++round) {
            const std::string id = FxFmt("LAST.04 recall-last, round %d", round);
            FxSetPos(kStart);
            uint8_t tier = kTierSentinel;
            const size_t logs = FxLogMark(), calls = FxCallMark();
            const int32_t rc = Wirbel::RecallLast(&tier);
            check(Lbl(id, "answers 0 -- no map or parent-relative check").c_str(), rc == Wirbel::TP_OK, FxS(rc));
            check(Lbl(id, "K2_SetActorLocation carries the planted pose's position").c_str(),
                  FxEq3(FirstNewLocation(calls), plantedXyz.data()), FxArrStr(FirstNewLocation(calls)));
            check(Lbl(id, "SetControlRotation carries the planted rotation").c_str(),
                  FxEq3(FirstNewRotation(calls), plantedPyr.data()), FxArrStr(FirstNewRotation(calls)));
            check(Lbl(id, "the slot is left as it was -- not replaced by the pose it jumped from").c_str(),
                  FxMarkerEq(FxLastNow(), planted), FxMarkerStr(FxLastNow()));
            check(Lbl(id, "INFO \"recall-last (-1234.5, 2345.5, -345.5) -> rc=0\" once").c_str(),
                  FxLogCount(FxFmt(kFmtRecallLast, kPlanted.X, kPlanted.Y, kPlanted.Z, Wirbel::TP_OK), 'I', logs) == 1,
                  FxLogDump(logs));
        }
    }
}

// ══ EXP ══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockExp() {
    blk("EXP - RecallExplicit: keepUndo, hasRot, and no map check");

    // ── EXP.01: keepUndo leaves the undo slot and skips its read ──
    {
        FxReset();
        FxPlantLast(PlantedLast());
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark();
        const int32_t rc = Wirbel::RecallExplicit(kDest, true, &tier, true);
        check("EXP.01 keepUndo: answers 0", rc == Wirbel::TP_OK, FxS(rc));
        check("EXP.01 keepUndo: the undo slot is left as planted", FxMarkerEq(FxLastNow(), PlantedLast()),
              FxMarkerStr(FxLastNow()));
        check("EXP.01 keepUndo: no \"last position\" line", FxLogPrefix(kLastPrefix, 0, logs) == 0, FxLogDump(logs));
        check("EXP.01 keepUndo: one chain line -- RecallTo's own resolution, no undo-point read",
              FxLogCount(FxLine::Chain(g_pc0), 'I', logs) == 1, FxLogDump(logs));
        check("EXP.01 keepUndo: INFO \"explicit recall (1500.5, -2500.5, 350.5) rot=1 keep_undo=1 -> rc=0\" once",
              FxLogCount(FxLine::Explicit(kDestXyz, true, true, Wirbel::TP_OK), 'I', logs) == 1, FxLogDump(logs));
    }

    // ── EXP.01: without keepUndo the slot takes the pose before the jump ──
    {
        FxReset();
        FxPlantLast(PlantedLast());
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark();
        const int32_t rc = Wirbel::RecallExplicit(kDest, true, &tier, false);
        check("EXP.01 !keepUndo: answers 0", rc == Wirbel::TP_OK, FxS(rc));
        check("EXP.01 !keepUndo: the undo slot holds the pose before the jump (kStart), not the destination",
              FxMarkerEq(FxLastNow(), StartMarker()), FxMarkerStr(FxLastNow()));
        check("EXP.01 !keepUndo: two chain lines -- the undo-point read and RecallTo",
              FxLogCount(FxLine::Chain(g_pc0), 'I', logs) == 2, FxLogDump(logs));
        check("EXP.01 !keepUndo: INFO \"explicit recall (1500.5, -2500.5, 350.5) rot=1 keep_undo=0 -> rc=0\" once",
              FxLogCount(FxLine::Explicit(kDestXyz, true, false, Wirbel::TP_OK), 'I', logs) == 1, FxLogDump(logs));
    }

    // ── EXP.01: a failing recall still logs its line, with the code ──
    {
        FxReset();
        NoPawn();
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark();
        const int32_t rc = Wirbel::RecallExplicit(kDest, true, &tier, false);
        check("EXP.01 failing: no pawn -- -3, and the INFO line still comes, with rc=-3",
              rc == Wirbel::TP_ERR_NO_PAWN &&
                  FxLogCount(FxLine::Explicit(kDestXyz, true, false, Wirbel::TP_ERR_NO_PAWN), 'I', logs) == 1,
              FxS(rc) + FxLogDump(logs));
    }

    // ── EXP.01: no map check ──
    {
        FxReset();
        FxWorld(g_worldB);
        uint8_t tier = kTierSentinel;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::RecallExplicit(kDest, true, &tier, false);
        check("EXP.01 on Map_B: no map check -- 0, and the pawn is sent to the pose",
              rc == Wirbel::TP_OK && FxEq3(FirstNewLocation(calls), kDestXyz),
              FxS(rc) + " " + FxArrStr(FirstNewLocation(calls)));
    }

    // ── EXP.02: hasRot false: no SetControlRotation; a K2_TeleportTo keeps the current facing ──
    {
        FxReset();
        uint8_t tier = kTierSentinel;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::RecallExplicit(kDest, false, &tier, false);
        check("EXP.02 hasRot false: answers 0", rc == Wirbel::TP_OK, FxS(rc));
        check("EXP.02 hasRot false: no SetControlRotation -- calls [SetActorLoc, GetLoc, StopMove, StopMoveNow]",
              FxKindsAre({ FxKind::SetActorLoc, FxKind::GetLoc, FxKind::StopMove, FxKind::StopMoveNow }, calls),
              FxKindsStr(FxKinds(calls)));
        check("EXP.02 hasRot false: ControlRotation is still kRot", FxEq3(FxCtrlRot(), kRot), FxArrStr(FxCtrlRot()));
    }
    {
        FxReset();
        FxActorFns(FxActorFnList({ .setActorLoc = FxSalShape::Missing }));
        uint8_t tier = kTierSentinel;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::RecallExplicit(kDest, false, &tier, false);
        const FxCall tp = FxNth(FxKind::TeleportTo, 0, calls);
        const auto destLoc = FxVec(tp, static_cast<size_t>(kTp5.loc), kTp5.vec);
        const auto destRot = FxVec(tp, static_cast<size_t>(kTp5.rot), kTp5.vec);
        check("EXP.02 TeleportTo-only pawn: answers 0, K2_TeleportTo on the hero carrying DestLocation kDest",
              rc == Wirbel::TP_OK && tp.seq != 0 && tp.instance == g_hero0 && FxEq3(destLoc, kDestXyz),
              FxS(rc) + " " + FxCallsStr(FxCalls(calls)));
        check("EXP.02 TeleportTo-only pawn: DestRotation is the current ControlRotation kRot, not zeros",
              FxEq3(destRot, kRot), FxArrStr(destRot));
        check("EXP.02 TeleportTo-only pawn: still no SetControlRotation", FxCount(FxKind::SetCtrlRot, calls) == 0,
              FxCallsStr(FxCalls(calls)));
    }
}

// ══ BUG ══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockBug() {
    blk("BUG - BugItSave and BugItGo: the outputs, the parent-relative flag, the refusals and the jump");

    // ── BUG.01: a save: every output written, the slot replaced, one INFO ──
    {
        FxReset();
        Wirbel::Pose out = FxSentinelPose();
        char map[kMapCap];
        FxFillMap(map, sizeof map);
        uint8_t src = kByteSentinel;
        bool pr = true;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::BugItSave(out, map, kMapCap, &src, &pr);
        const std::string mapGot(map, strnlen(map, sizeof map));
        check("BUG.01 save: answers 0", rc == Wirbel::TP_OK, FxS(rc));
        check("BUG.01 save: out is the pose, kStart + kRot", FxPoseEq(out, kStartPose), FxPoseStr(out));
        check("BUG.01 save: the map buffer holds \"Map_A\"", mapGot == kMapA, mapGot);
        check("BUG.01 save: *outSource is written -- 0 (raw read), not the 0xAB it was handed", src == kSourceRaw,
              FxS(src));
        check("BUG.01 save: *outParentRelative is written -- false", !pr);
        check("BUG.01 save: the slot is {Valid, kStart + kRot, \"Map_A\", world coordinates}",
              FxMarkerEq(FxBugItNow(), StartMarker()), FxMarkerStr(FxBugItNow()));
        check("BUG.01 save: INFO \"BugIt stored (1000.5, -2000.5, 300.5) map='Map_A'\" once, and no WARN",
              FxLogCount(FxFmt(kFmtBugItStored, kStart[0], kStart[1], kStart[2], kMapA), 'I', logs) == 1 &&
                  FxTeleportLines('W', logs) == 0,
              FxLogDump(logs));
        check("BUG.01 save: an unattached read is memory only -- no call", FxCountAll(calls) == 0,
              FxCallsStr(FxCalls(calls)));
    }
    {
        FxReset();
        Wirbel::Pose out = FxSentinelPose();
        char map[kMapCap];
        FxFillMap(map, sizeof map);
        uint8_t src = kByteSentinel;
        bool pr = true;
        const int32_t rc = Wirbel::BugItSave(out, map, kShortCap, &src, &pr);
        const std::string mapGot(map, strnlen(map, sizeof map));
        check("BUG.01 cap 4: answers 0 with \"Map\" -- cap-1 characters and the terminator",
              rc == Wirbel::TP_OK && mapGot == kMapAShort, FxS(rc) + " '" + mapGot + "'");
        check("BUG.01 cap 4: the byte past the cap is untouched", map[kShortCap] == kMapFill, FxS(map[kShortCap]));
        check("BUG.01 cap 4: the slot keeps the whole name -- only the caller's copy is cut",
              std::string(FxBugItNow().MapName) == kMapA, FxMarkerStr(FxBugItNow()));
    }
    {
        FxReset();
        Wirbel::Pose out = FxSentinelPose();
        char map[kMapCap];
        FxFillMap(map, sizeof map);
        uint8_t src = kByteSentinel;
        const int32_t rc = Wirbel::BugItSave(out, map, kMapCap, &src, nullptr);
        check("BUG.01 outParentRelative nullptr: tolerated -- 0, and the pose still comes back",
              rc == Wirbel::TP_OK && FxPoseEq(out, kStartPose), FxS(rc) + " " + FxPoseStr(out));
    }

    // ── BUG.01: attached, with a world read that answers: the source byte says so ──
    {
        FxReset();
        FxAttach(true);
        Wirbel::Pose out = FxSentinelPose();
        char map[kMapCap];
        FxFillMap(map, sizeof map);
        uint8_t src = kByteSentinel;
        bool pr = true;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::BugItSave(out, map, kMapCap, &src, &pr);
        check("BUG.01 attached, world read OK: answers 0 with the world position kAttachedWorld and rotation kRot",
              rc == Wirbel::TP_OK && FxPoseIs(out, kAttachedWorld, kRot), FxS(rc) + " " + FxPoseStr(out));
        check("BUG.01 attached, world read OK: *outSource is 1 -- K2_GetActorLocation answered", src == kSourceInvoke,
              FxS(src));
        check("BUG.01 attached, world read OK: *outParentRelative is false", !pr);
        check("BUG.01 attached, world read OK: one call, K2_GetActorLocation on the hero",
              FxCountOn(FxKind::GetLoc, g_hero0, calls) == 1 && FxCountAll(calls) == 1, FxCallsStr(FxCalls(calls)));
    }

    // ── BUG.01: a parent-relative read is stored, flagged, and WARNed before the stored INFO ──
    {
        FxReset();
        DegradeToParentRelative();
        Wirbel::Pose out = FxSentinelPose();
        char map[kMapCap];
        FxFillMap(map, sizeof map);
        uint8_t src = kByteSentinel;
        bool pr = false;
        const size_t logs = FxLogMark();
        const int32_t rc = Wirbel::BugItSave(out, map, kMapCap, &src, &pr);
        const uint64_t warnSeq = FxLogSeq(FxText::kBugItPr, 'W', logs);
        const uint64_t infoSeq = FxLogSeq(FxFmt(kFmtBugItStored, kStart[0], kStart[1], kStart[2], kMapA), 'I', logs);
        check("BUG.01 parent-relative: answers 0 with the raw RelativeLocation",
              rc == Wirbel::TP_OK && FxPoseEq(out, kStartPose), FxS(rc) + " " + FxPoseStr(out));
        check("BUG.01 parent-relative: *outParentRelative is true", pr);
        check("BUG.01 parent-relative: *outSource is 0 -- the raw read", src == kSourceRaw, FxS(src));
        check("BUG.01 parent-relative: the slot is flagged parent-relative",
              FxMarkerEq(FxBugItNow(), FxMarker(kStartPose, kMapA, true)), FxMarkerStr(FxBugItNow()));
        check("BUG.01 parent-relative: WARN \"BugIt stored from a PARENT-RELATIVE read ...\" once, before the INFO",
              FxLogCount(FxText::kBugItPr, 'W', logs) == 1 && warnSeq != 0 && infoSeq != 0 && warnSeq < infoSeq,
              FxLogDump(logs));
    }

    // ── BUG.01: a failed save touches nothing: not the slot, not one output ──
    {
        FxReset();
        const Wirbel::Marker stored = FxMarker(kDest, kMapA);
        FxPlantBugIt(stored);
        FxNoGWorld();
        Wirbel::Pose out = FxSentinelPose();
        char map[kMapCap];
        FxFillMap(map, sizeof map);
        uint8_t src = kByteSentinel;
        bool pr = true;
        const size_t logs = FxLogMark();
        const int32_t rc = Wirbel::BugItSave(out, map, kMapCap, &src, &pr);
        const bool mapUntouched = std::all_of(map, map + kMapCap, [](char ch) { return ch == kMapFill; });
        check("BUG.01 failure: no GWorld -- -1", rc == Wirbel::TP_ERR_NOT_INIT, FxS(rc));
        check("BUG.01 failure: the stored BugIt is untouched", FxMarkerEq(FxBugItNow(), stored),
              FxMarkerStr(FxBugItNow()));
        check("BUG.01 failure: out, *outParentRelative, *outSource and the map buffer are as they were handed in",
              FxIsSentinel(out) && pr && src == kByteSentinel && mapUntouched,
              FxPoseStr(out) + FxFmt(" pr=%d src=%d mapUntouched=%d", pr ? 1 : 0, src, mapUntouched ? 1 : 0));
        check("BUG.01 failure: no stored line", FxLogPrefix(kBugItStoredPrefix, 0, logs) == 0, FxLogDump(logs));
        FxGWorldAt(FxAddr(&g_gworldSlot));
        uint8_t tier = kTierSentinel;
        const size_t calls = FxCallMark();
        const int32_t go = Wirbel::BugItGo(&tier);
        check("BUG.01 failure: BugItGo still goes to the stored pose afterwards",
              go == Wirbel::TP_OK && FxEq3(FirstNewLocation(calls), kDestXyz),
              FxS(go) + " " + FxArrStr(FirstNewLocation(calls)));
    }

    // ── BUG.02: the refusals, before the undo point ──
    {
        FxReset();
        FxPlantLast(PlantedLast());
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::BugItGo(&tier);
        Refused("BUG.02 empty", rc, Wirbel::TP_ERR_EMPTY_MARKER, "", logs, calls, PlantedLast(), tier);
    }
    {
        FxReset();
        FxPlantLast(PlantedLast());
        FxPlantBugIt(FxMarker(kDest, kMapA, true));
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::BugItGo(&tier);
        Refused("BUG.02 parent-relative", rc, Wirbel::TP_ERR_PARENT_RELATIVE,
                FxFmt(kFmtBugItRefused, kDest.X, kDest.Y, kDest.Z), logs, calls, PlantedLast(), tier);
    }
    // The flag BugItGo reads is the one BugItSave stored from the read itself.
    {
        FxReset();
        DegradeToParentRelative();
        const int32_t save = RunBugItSave();
        check("BUG.02 saved from a parent-relative read: the save itself answers 0 (setup)", save == Wirbel::TP_OK,
              FxS(save));
        FxPlantLast(PlantedLast());
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::BugItGo(&tier);
        Refused("BUG.02 saved from a parent-relative read", rc, Wirbel::TP_ERR_PARENT_RELATIVE,
                FxFmt(kFmtBugItRefused, kStart[0], kStart[1], kStart[2]), logs, calls, PlantedLast(), tier);
    }

    // ── BUG.03: the accepted jump: undo point, move, rotation, settle; no map check ──
    {
        FxReset();
        FxPlantLast(PlantedLast());
        FxPlantBugIt(FxMarker(kDest, kMapA));
        uint8_t tier = kTierSentinel;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::BugItGo(&tier);
        check("BUG.03 accepted: answers 0, tier 1", rc == Wirbel::TP_OK && tier == kTierInvoke,
              FxS(rc) + " tier " + FxS(tier));
        check("BUG.03 accepted: the undo slot holds the pose before the jump (kStart on Map_A)",
              FxMarkerEq(FxLastNow(), StartMarker()), FxMarkerStr(FxLastNow()));
        check("BUG.03 accepted: the calls are [SetActorLoc, GetLoc, SetCtrlRot, StopMove, StopMoveNow]",
              FxKindsAre({ FxKind::SetActorLoc, FxKind::GetLoc, FxKind::SetCtrlRot, FxKind::StopMove,
                           FxKind::StopMoveNow },
                         calls),
              FxKindsStr(FxKinds(calls)));
        check("BUG.03 accepted: K2_SetActorLocation carries the BugIt position, SetControlRotation its rotation",
              FxEq3(FirstNewLocation(calls), kDestXyz) && FxEq3(FirstNewRotation(calls), kDestPyr),
              FxArrStr(FirstNewLocation(calls)) + " / " + FxArrStr(FirstNewRotation(calls)));
        check("BUG.03 accepted: INFO \"BugItGo (1500.5, -2500.5, 350.5) -> rc=0\" once",
              FxLogCount(FxFmt(kFmtBugItGo, kDest.X, kDest.Y, kDest.Z, Wirbel::TP_OK), 'I', logs) == 1,
              FxLogDump(logs));
    }
    {
        FxReset();
        FxPlantBugIt(FxMarker(kDest, kMapA));
        FxWorld(g_worldB);
        uint8_t tier = kTierSentinel;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::BugItGo(&tier);
        check("BUG.03 on Map_B: no map check -- a Map_A BugIt goes, 0",
              rc == Wirbel::TP_OK && FxEq3(FirstNewLocation(calls), kDestXyz),
              FxS(rc) + " " + FxArrStr(FirstNewLocation(calls)));
    }
    // A healthy save replaces a flagged one, and BugItGo then goes.
    {
        FxReset();
        FxAttach(true);
        FxRcs(FxKind::GetLoc, { kRcNotRun });   // the first save's world read only
        Wirbel::Pose out = FxSentinelPose();
        char map[kMapCap] = {};
        uint8_t src = kByteSentinel;
        bool pr1 = false, pr2 = true;
        const int32_t save1 = Wirbel::BugItSave(out, map, kMapCap, &src, &pr1);
        check("BUG.03 healthy save after a flagged one: the first save is flagged (setup)",
              save1 == Wirbel::TP_OK && pr1, FxS(save1) + FxFmt(" pr=%d", pr1 ? 1 : 0));
        FxAttach(false);
        const int32_t save2 = Wirbel::BugItSave(out, map, kMapCap, &src, &pr2);
        check("BUG.03 healthy save after a flagged one: the second replaces it, unflagged",
              save2 == Wirbel::TP_OK && !pr2 && FxMarkerEq(FxBugItNow(), StartMarker()), FxMarkerStr(FxBugItNow()));
        uint8_t tier = kTierSentinel;
        const size_t calls = FxCallMark();
        const int32_t go = Wirbel::BugItGo(&tier);
        check("BUG.03 healthy save after a flagged one: BugItGo goes -- 0, one K2_SetActorLocation to kStart",
              go == Wirbel::TP_OK && FxCount(FxKind::SetActorLoc, calls) == 1 &&
                  FxEq3(FirstNewLocation(calls), kStart),
              FxS(go) + " " + FxCallsStr(FxCalls(calls)));
    }
}

// ══ REL ══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockRel() {
    blk("REL - TeleportRelative: the start pose, the facing, the step, the undo point and the landing");

    // ── REL.01: a chain failure touches no output ──
    {
        FxReset();
        FxNoGWorld();
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = true;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::TeleportRelative(kStep, true, out, &tier, &known);
        check("REL.01 chain failure: no GWorld -- -1", rc == Wirbel::TP_ERR_NOT_INIT, FxS(rc));
        check("REL.01 chain failure: outNewPose, *outLandingKnown and the tier are as handed in",
              FxIsSentinel(out) && known && tier == kTierSentinel, FxPoseStr(out));
        check("REL.01 chain failure: no call", FxCountAll(calls) == 0, FxCallsStr(FxCalls(calls)));
    }

    // ── REL.02: an attached start steps from the world position the engine answered ──
    {
        FxReset();
        FxAttach(true);
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = false;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::TeleportRelative(kStep, true, out, &tier, &known);
        const auto want = Stepped(kAttachedWorld, Horizontal(FxArr(kFwd)), kStep);
        check("REL.02 attached start: answers 0", rc == Wirbel::TP_OK, FxS(rc));
        check("REL.02 attached start: the step starts from kAttachedWorld (K2_GetActorLocation), not RelativeLocation",
              Within(FirstNewLocation(calls), want), FxArrStr(FirstNewLocation(calls)) + " want " + FxArrStr(want));
    }

    // ── REL.03: a parent-relative start is refused before the facing and the undo point ──
    {
        FxReset();
        FxPlantLast(PlantedLast());
        DegradeToParentRelative();
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = true;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::TeleportRelative(kStep, true, out, &tier, &known);
        check("REL.03 parent-relative start: -16", rc == Wirbel::TP_ERR_PARENT_RELATIVE, FxS(rc));
        check("REL.03 parent-relative start: WARN \"relative move refused -- ... (1000.5, -2000.5, 300.5) ...\" once",
              FxLogCount(FxFmt(kFmtRelRefused, kStart[0], kStart[1], kStart[2]), 'W', logs) == 1, FxLogDump(logs));
        check("REL.03 parent-relative start: no GetActorForwardVector and no K2_SetActorLocation",
              FxCount(FxKind::Forward, calls) == 0 && FxCount(FxKind::SetActorLoc, calls) == 0,
              FxCallsStr(FxCalls(calls)));
        check("REL.03 parent-relative start: the undo slot, outNewPose, *outLandingKnown and the tier are untouched",
              FxMarkerEq(FxLastNow(), PlantedLast()) && FxIsSentinel(out) && known && tier == kTierSentinel,
              FxMarkerStr(FxLastNow()) + " " + FxPoseStr(out));
    }

    // ── REL.04: the facing: the forward getter, then ControlRotation, horizontal or 3D ──
    {
        FxReset();
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = false;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::TeleportRelative(kStep, true, out, &tier, &known);
        check("REL.04 forward vector, horizontal: fwd (0.6, 0.8, 0) -- Z dropped, then unit length",
              rc == Wirbel::TP_OK && FxEq3(FirstNewLocation(calls), kRelDestFwd, kTol),
              FxS(rc) + " " + FxArrStr(FirstNewLocation(calls)));
    }
    {
        FxReset();
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = false;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::TeleportRelative(kStep, false, out, &tier, &known);
        const auto want = Stepped(kStart, FxArr(kFwd), kStep);
        check("REL.04 forward vector, 3D: fwd = (0.6, 0.8, 0.5) / |v| -- NewLocation.Z is 300.5 + 44.72135955",
              rc == Wirbel::TP_OK && Within(FirstNewLocation(calls), want),
              FxS(rc) + " " + FxArrStr(FirstNewLocation(calls)) + " want " + FxArrStr(want));
    }
    {
        FxReset();
        FxRcAlways(FxKind::Forward, kRcNotRun);
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = false;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::TeleportRelative(kStep, true, out, &tier, &known);
        const auto want = Stepped(kStart, Horizontal(Facing(kRot)), kStep);
        check("REL.04 ControlRotation fallback, horizontal: the forward getter failed -- fwd (cos 90.5, sin 90.5, 0)",
              rc == Wirbel::TP_OK && Within(FirstNewLocation(calls), want),
              FxS(rc) + " " + FxArrStr(FirstNewLocation(calls)) + " want " + FxArrStr(want));
        check("REL.04 ControlRotation fallback, horizontal: GetActorForwardVector was asked first, once",
              FxCount(FxKind::Forward, calls) == 1 &&
                  FxNth(FxKind::Forward, 0, calls).seq < FxNth(FxKind::SetActorLoc, 0, calls).seq,
              FxCallsStr(FxCalls(calls)));
    }
    {
        FxReset();
        FxRcAlways(FxKind::Forward, kRcNotRun);
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = false;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::TeleportRelative(kStep, false, out, &tier, &known);
        const auto got = FirstNewLocation(calls);
        const auto want = Stepped(kStart, Facing(kRot), kStep);
        const double wantZ = kStart[2] + kStep * std::sin(Rad(kRot[0]));
        check("REL.04 ControlRotation fallback, 3D: NewLocation.Z is 300.5 + 100 sin(Pitch -10.5)",
              rc == Wirbel::TP_OK && std::fabs(got[2] - wantZ) <= kTol, FxS(rc) + " " + FxArrStr(got));
        check("REL.04 ControlRotation fallback, 3D: X and Y follow cos(P)cos(Y) and cos(P)sin(Y)", Within(got, want),
              FxArrStr(got) + " want " + FxArrStr(want));
    }
    {
        FxReset();
        FxPlantLast(PlantedLast());
        FxRcAlways(FxKind::Forward, kRcNotRun);
        FxSetCtrlRot(kStraightUp);
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = false;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::TeleportRelative(kStep, true, out, &tier, &known);
        check("REL.04 pitch 90, horizontal: nothing is left of the facing -- -4", rc == Wirbel::TP_ERR_REFLECTION,
              FxS(rc));
        check("REL.04 pitch 90, horizontal: WARN \"relative move -- could not resolve a facing direction\" once",
              FxLogCount(FxText::kNoFacing, 'W', logs) == 1, FxLogDump(logs));
        check("REL.04 pitch 90, horizontal: no K2_SetActorLocation, and the undo slot is untouched",
              FxCount(FxKind::SetActorLoc, calls) == 0 && FxMarkerEq(FxLastNow(), PlantedLast()),
              FxCallsStr(FxCalls(calls)) + " " + FxMarkerStr(FxLastNow()));
    }
    {
        FxReset();
        FxAnswer(FxKind::Forward, kZero3);
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = false;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::TeleportRelative(kStep, true, out, &tier, &known);
        check("REL.04 zero forward vector: -4 with the facing WARN, though ControlRotation exists -- no fallback",
              rc == Wirbel::TP_ERR_REFLECTION && FxLogCount(FxText::kNoFacing, 'W', logs) == 1 &&
                  FxCount(FxKind::SetActorLoc, calls) == 0,
              FxS(rc) + FxLogDump(logs));
    }
    {
        FxReset();
        FxSetClass(g_pc0, g_cPcNoRot);
        FxRcAlways(FxKind::Forward, kRcNotRun);
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = false;
        const size_t logs = FxLogMark(), calls = FxCallMark();
        const int32_t rc = Wirbel::TeleportRelative(kStep, true, out, &tier, &known);
        check("REL.04 no forward answer, no ControlRotation: -4 with the facing WARN, no move",
              rc == Wirbel::TP_ERR_REFLECTION && FxLogCount(FxText::kNoFacing, 'W', logs) == 1 &&
                  FxCount(FxKind::SetActorLoc, calls) == 0,
              FxS(rc) + FxLogDump(logs));
    }

    // ── REL.05: the destination is start + fwd * distance, the move is K2_SetActorLocation, the undo read is fresh ──
    for (const double dist : { kStep, -kStep }) {
        FxReset();
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = false;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::TeleportRelative(dist, true, out, &tier, &known);
        const std::string id = FxFmt("REL.05 step %.0f", dist);
        const double* want = dist > 0 ? kRelDestFwd : kRelDestBack;
        check(Lbl(id, "answers 0, K2_SetActorLocation carrying kStart + fwd * distance").c_str(),
              rc == Wirbel::TP_OK && FxEq3(FirstNewLocation(calls), want, kTol),
              FxS(rc) + " " + FxArrStr(FirstNewLocation(calls)));
        check(Lbl(id, "the move is K2_SetActorLocation, not K2_TeleportTo: [Forward, SetActorLoc, GetLoc, StopMove, "
                      "StopMoveNow]").c_str(),
              FxKindsAre({ FxKind::Forward, FxKind::SetActorLoc, FxKind::GetLoc, FxKind::StopMove,
                           FxKind::StopMoveNow },
                         calls),
              FxKindsStr(FxKinds(calls)));
    }
    {
        FxReset();
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = false;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::TeleportRelative(-kStep, false, out, &tier, &known);
        const auto want = Stepped(kStart, FxArr(kFwd), -kStep);
        check("REL.05 step -100, 3D: Z steps too -- NewLocation is kStart - 100 * kFwd / |kFwd|",
              rc == Wirbel::TP_OK && Within(FirstNewLocation(calls), want),
              FxS(rc) + " " + FxArrStr(FirstNewLocation(calls)) + " want " + FxArrStr(want));
    }
    {
        FxReset();
        // The pawn moves between the start read and the undo read, as a game ticking under the op would.
        FxOnCall([](FxKind k, uintptr_t, int nth) {
            if (k == FxKind::Forward && nth == 0) FxSetPos(kOther);
        });
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = false;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::TeleportRelative(kStep, true, out, &tier, &known);
        check("REL.05 pawn moved mid-op: the destination is stepped from the start read (kStart)",
              rc == Wirbel::TP_OK && FxEq3(FirstNewLocation(calls), kRelDestFwd, kTol),
              FxS(rc) + " " + FxArrStr(FirstNewLocation(calls)));
        check("REL.05 pawn moved mid-op: the undo slot holds a fresh read taken after the facing -- kOther",
              FxMarkerEq(FxLastNow(), FxMarker(FxPose(kOther, kRot), kMapA)), FxMarkerStr(FxLastNow()));
    }

    // ── REL.06: the landing re-read, after StopMovement's invokes ──
    {
        FxReset();
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = false;
        const int32_t rc = Wirbel::TeleportRelative(kStep, true, out, &tier, &known);
        check("REL.06 landing read: answers 0, tier 1", rc == Wirbel::TP_OK && tier == kTierInvoke,
              FxS(rc) + " tier " + FxS(tier));
        check("REL.06 landing read: outNewPose is the landing -- the destination, rotation kRot",
              FxEq3(FxXyzOf(out), kRelDestFwd, kTol) && FxEq3(FxPyrOf(out), kRot), FxPoseStr(out));
        check("REL.06 landing read: *outLandingKnown is true", known);
    }
    {
        FxReset();
        // GWorld goes away once the settle's last invoke has run: only the landing read comes after it.
        FxOnCall([](FxKind k, uintptr_t, int nth) {
            if (k == FxKind::StopMoveNow && nth == 0) FxWorld(0);
        });
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = true;
        const size_t calls = FxCallMark();
        const int32_t rc = Wirbel::TeleportRelative(kStep, true, out, &tier, &known);
        check("REL.06 landing read fails: the move ran -- one K2_SetActorLocation, then StopMovementImmediately",
              FxCount(FxKind::SetActorLoc, calls) == 1 && FxCount(FxKind::StopMoveNow, calls) == 1,
              FxCallsStr(FxCalls(calls)));
        check("REL.06 landing read fails: the move's 0 stands", rc == Wirbel::TP_OK, FxS(rc));
        check("REL.06 landing read fails: outNewPose is untouched -- no landing at the origin", FxIsSentinel(out),
              FxPoseStr(out));
        check("REL.06 landing read fails: *outLandingKnown is false", !known);
    }
    {
        FxReset();
        // The pawn is attached, and its world read stops answering, once the settle's last invoke has run.
        FxOnCall([](FxKind k, uintptr_t, int nth) {
            if (k == FxKind::StopMoveNow && nth == 0) {
                FxAttach(true);
                FxRcAlways(FxKind::GetLoc, kRcNotRun);
            }
        });
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = true;
        const int32_t rc = Wirbel::TeleportRelative(kStep, true, out, &tier, &known);
        check("REL.06 landing parent-relative: the move's 0 stands", rc == Wirbel::TP_OK, FxS(rc));
        check("REL.06 landing parent-relative: outNewPose IS overwritten -- with the raw RelativeLocation (the dest)",
              FxEq3(FxXyzOf(out), kRelDestFwd, kTol) && FxEq3(FxPyrOf(out), kRot), FxPoseStr(out));
        check("REL.06 landing parent-relative: *outLandingKnown is false", !known);
    }
    {
        FxReset();
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        const int32_t rc = Wirbel::TeleportRelative(kStep, true, out, &tier, nullptr);
        check("REL.06 outLandingKnown nullptr: tolerated -- 0, and the landing is still written",
              rc == Wirbel::TP_OK && FxEq3(FxXyzOf(out), kRelDestFwd, kTol), FxS(rc) + " " + FxPoseStr(out));
    }

    // ── REL.07: the INFO line, horizontal and 3D ──
    {
        FxReset();
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = false;
        const size_t logs = FxLogMark();
        const int32_t rc = Wirbel::TeleportRelative(kStep, true, out, &tier, &known);
        check("REL.07 log, horizontal: INFO \"relative 100.0 uu (horizontal) fwd=(0.600, 0.800, 0.000) -> "
              "(1060.5, -1920.5, 300.5)\" once",
              rc == Wirbel::TP_OK && FxLogCount(kRelLineHorizontal, 'I', logs) == 1, FxLogDump(logs));
    }
    {
        FxReset();
        Wirbel::Pose out = FxSentinelPose();
        uint8_t tier = kTierSentinel;
        bool known = false;
        const size_t logs = FxLogMark();
        const int32_t rc = Wirbel::TeleportRelative(kStep, false, out, &tier, &known);
        const auto f = Unit(FxArr(kFwd));
        const auto d = Stepped(kStart, FxArr(kFwd), kStep);
        const std::string line = FxFmt(kFmtRelative, kStep, kRel3D, f[0], f[1], f[2], d[0], d[1], d[2]);
        check("REL.07 log, 3D: the INFO says (3D) with the unit 3D facing and its destination",
              rc == Wirbel::TP_OK && FxLogCount(line, 'I', logs) == 1, "want " + line + FxLogDump(logs));
    }
}

}  // namespace G4

// LAST.05@start. main runs this before every other block and before any FxReset: FxReset empties "last" itself, so
// only here can an empty slot be the process's own starting state.
static void Block_LastAtStart() {
    blk("LAST.05@start - the undo slot as the process started, before any op");
    const size_t logs = FxLogMark(), calls = FxCallMark();
    Wirbel::Marker out = G4::SentinelMarker();
    const int32_t rc = Wirbel::GetLast(out);
    check("LAST.05@start GetLast: -6 before any jump has saved an undo point", rc == Wirbel::TP_ERR_EMPTY_MARKER,
          FxS(rc));
    check("LAST.05@start GetLast: the empty slot is copied out -- Valid false, no map, a zero pose, unflagged",
          FxMarkerEq(out, Wirbel::Marker{}), FxMarkerStr(out));
    check("LAST.05@start GetLast: a read of the slot calls nothing and logs nothing",
          FxCountAll(calls) == 0 && FxTeleportLines(0, logs) == 0, FxCallsStr(FxCalls(calls)) + FxLogDump(logs));
}

static void Blocks_G4() {
    G4::BlockMrk();
    G4::BlockLast();
    G4::BlockExp();
    G4::BlockBug();
    G4::BlockRel();
}

// ══ G5: the cursor teleport, the map name, parameter packing and the op lock ═════════════════════════════════════
// main runs Blocks_G5 last of the groups: the design puts LOCK after every other block, and its probe and its held
// threads are the slowest checks in the run.

namespace G5 {

constexpr int32_t kRcQueued = -5;     // what an invoke the game thread never ran answers ("-5 = still queued")
constexpr int32_t kNotReturned = 1;   // no Wirbel export answers a positive code: a thread that never got one
constexpr uint8_t kTierInvoke = 1, kTierRaw = 2;   // TeleportPawnTo's tiers: an engine setter, the raw write

// ── trace channels ──
constexpr int32_t kChanZero = 0;     // the first call of a scan already asks for it
constexpr int32_t kChan = 3;         // not 0, so "the requested one first, then 0..9 without it" differs from 0..9
constexpr int32_t kChanPast9 = 12;   // outside 0..9: the scan skips nothing and makes one call more
constexpr int32_t kChanWide = 256;   // wider than the TraceChannel byte: the engine sees 0
constexpr int32_t kChanLate = 5;     // a channel the channel-3 scan reaches only after four others
constexpr int kScanCalls = 10;       // a scan for a channel in 0..9: that channel, then the other nine
// The scan orders the design spells out, as the TraceChannel bytes the engine receives.
static const std::vector<int> kOrder3 = { 3, 0, 1, 2, 4, 5, 6, 7, 8, 9 };
static const std::vector<int> kOrder3To0 = { 3, 0 };
static const std::vector<int> kOrder3ToLate = { 3, 0, 1, 2, 4, 5 };
static const std::vector<int> kOrder12 = { 12, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
static const std::vector<int> kOrder256 = { 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

// ── where things land ──
constexpr double kCursorDest[3] = { kHit[0], kHit[1], kHit[2] + kZOffset };          // (3000.5, 3100.5, 141.0)
constexpr double kHitLocation[3] = { kHit[0] - kLocationLag, kHit[1], kHit[2] };     // the hit's Location member
constexpr double kLocationDest[3] = { kHitLocation[0], kHitLocation[1], kHitLocation[2] + kZOffset };
// LineTraceSingle's End, worked by hand as kDeprojLoc + dir * 1e5, for the default and the oblique direction.
constexpr double kTraceEnd[3] = { 10.5, 20.5, -99969.5 };
constexpr double kTraceEndOblique[3] = { 48010.5, 60020.5, -63969.5 };
// 0.48, 0.6 and 0.64 are not exact in binary; each times 1e5 is within an ulp of the whole number.
constexpr double kObliqueTol = 1e-6;
constexpr double kDestPyrStored[3] = { kDest.Pitch, kDestYawStored, kDest.Roll };   // after SetControlRotation
constexpr int32_t kViewportFlat[2] = { 1920, 0 };   // a viewport with no height: no centre to take
constexpr double kStep = 100.0;                     // a relative move's distance
constexpr int kShortCap = 4;                        // a map-name buffer shorter than "Map_A"
constexpr int kTrainerHops = 5;                     // the trainer chain's hops from GWorld to the pawn
constexpr int32_t kCoordD = static_cast<int32_t>(sizeof(double)), kCoordF = static_cast<int32_t>(sizeof(float));

// ── Wirbel's own format strings and fixed texts, verbatim ──
constexpr const char* kCursorLineFmt = "Teleport: cursor mouse=(%.0f, %.0f) haveMouse=%d usedCenter=%d channel=%d";
constexpr const char* kPath1Fmt = "Teleport: cursor GetHitResultUnderCursorByChannel hit=%d hitChannel=%d";
constexpr const char* kPath1Prefix = "Teleport: cursor GetHitResultUnderCursorByChannel";
constexpr const char* kPath2Fmt =
    "Teleport: cursor GetHitResultAtScreenPosition screen=(%.0f, %.0f) hit=%d hitChannel=%d";
constexpr const char* kPath2Prefix = "Teleport: cursor GetHitResultAtScreenPosition";
constexpr const char* kDeprojMissingFmt =
    "Teleport: cursor — DeprojectScreenPositionToWorld not found on PC class '%s' (cooked out?) — can't trace";
constexpr const char* kDeprojFmt = "Teleport: cursor deproject loc=(%.1f, %.1f, %.1f) dir=(%.3f, %.3f, %.3f)";
constexpr const char* kDeprojPrefix = "Teleport: cursor deproject loc=";
constexpr const char* kTraceFmt = "Teleport: cursor LineTraceSingle r=%d returnedHit=%d haveHit=%d";
constexpr const char* kNoHitFmt = "Teleport: cursor — no blocking hit (channel=%d). Try a different trace channel.";
constexpr const char* kJumpFmt = "Teleport: cursor -> (%.1f, %.1f, %.1f) center=%d";
constexpr const char* kJumpPrefix = "Teleport: cursor -> ";
constexpr const char* kSetterFailFmt = "Teleport: %s %s (r=%d, parmsSize=%u) — trying raw-write fallback";
constexpr const char* kNotReachedPrefix = "Teleport: actor didn't reach target";
constexpr const char* kSalName = "K2_SetActorLocation";
constexpr const char* kSwlName = "K2_SetWorldLocation";
constexpr const char* kMapA = "Map_A";
constexpr const char* kMapB = "Map_B";

// TeleportToCursor's outputs, each planted first so "untouched" is seen: a sentinel pose, the tier sentinel, and
// usedCenter preset to the value the path under test would NOT write.
struct CursorRun {
    int32_t rc = kNotReturned;
    Wirbel::Pose hit = FxSentinelPose();
    uint8_t tier = kTierSentinel;
    bool usedCenter = true;
};
static CursorRun Cursor(int32_t channel, bool fallback = true, bool usedCenterPreset = true) {
    CursorRun r;
    r.usedCenter = usedCenterPreset;
    r.rc = Wirbel::TeleportToCursor(kZOffset, channel, fallback, &r.hit, &r.tier, &r.usedCenter);
    return r;
}
static std::string RunStr(const CursorRun& r) {
    return FxFmt("rc=%d tier=0x%02X usedCenter=%d hit=", r.rc, static_cast<unsigned>(r.tier), r.usedCenter ? 1 : 0) +
           FxPoseStr(r.hit);
}
static bool HitIs(const CursorRun& r, const double v[3]) { return FxEq3(FxXyzOf(r.hit), v); }

// A range FxZeroOutside keeps, from the int offsets the layouts carry.
static std::pair<size_t, size_t> Keep(int32_t off, int32_t len) {
    return { static_cast<size_t>(off), static_cast<size_t>(len) };
}
// The TraceChannel byte of every call of `kind` since `from`, in call order.
static std::vector<int> Channels(FxKind kind, int32_t at, size_t from = 0) {
    std::vector<int> out;
    for (const auto& c : FxCallsOf(kind, from)) out.push_back(FxRd<uint8_t>(c, static_cast<size_t>(at)));
    return out;
}
static std::string IntsStr(const std::vector<int>& v) {
    std::string out = "[";
    for (size_t i = 0; i < v.size(); ++i) out += (i ? ", " : "") + std::to_string(v[i]);
    return out + "]";
}
static bool Every(const std::vector<FxCall>& calls, const std::function<bool(const FxCall&)>& pred) {
    for (const auto& c : calls)
        if (!pred(c)) return false;
    return true;
}
static std::string FirstFail(const std::vector<FxCall>& calls, const std::function<bool(const FxCall&)>& pred) {
    for (const auto& c : calls)
        if (!pred(c)) return FxCallStr(c);
    return FxCallsStr(calls);
}
// The ScreenPosition a GetHitResultAtScreenPosition call carried: two doubles (UE5) or two floats (UE4).
static bool ScreenIs(const FxCall& c, const FxHitCallLayout& L, int32_t coordBytes, const double v[2]) {
    if (!c.seq || L.screen < 0) return false;
    const size_t at = static_cast<size_t>(L.screen);
    if (coordBytes == kCoordD) return FxRd<double>(c, at) == v[0] && FxRd<double>(c, at + sizeof(double)) == v[1];
    return FxRd<float>(c, at) == static_cast<float>(v[0]) && FxRd<float>(c, at + sizeof(float)) == static_cast<float>(v[1]);
}
static std::string ScreenStr(const FxCall& c, const FxHitCallLayout& L, int32_t coordBytes) {
    const size_t at = static_cast<size_t>(L.screen < 0 ? 0 : L.screen);
    if (coordBytes == kCoordD)
        return FxFmt("screen=(%.9g, %.9g) ", FxRd<double>(c, at), FxRd<double>(c, at + sizeof(double)));
    return FxFmt("screen=(%.9g, %.9g) ", FxRd<float>(c, at), FxRd<float>(c, at + sizeof(float)));
}
static int Moves() {
    return FxCount(FxKind::SetActorLoc) + FxCount(FxKind::TeleportTo) + FxCount(FxKind::SetWorldLoc) +
           FxCount(FxKind::SetRelLoc);
}
static bool GuardIntact(uintptr_t at) {
    for (int i = 0; i < FxOff::kGuardBytes; ++i)
        if (FxBytes(at)[i] != kGuardByte) return false;
    return true;
}
static bool AllFill(const char* buf, size_t n) {
    for (size_t i = 0; i < n; ++i)
        if (buf[i] != kMapFill) return false;
    return true;
}
// A NUL-terminated `want` within the first n bytes; read without running past the buffer.
static bool BufIs(const char* buf, size_t n, const char* want) {
    const size_t len = strnlen(buf, n);
    return len < n && len == strlen(want) && memcmp(buf, want, len) == 0;
}
static std::string BufStr(const char* buf, size_t n) { return std::string(buf, strnlen(buf, n)); }

// [PACK.03] What Wirbel hands ProcessEvent for one call: ParamBufferSize of the function as its declaring class lists
// it -- the same walk FindFunc takes -- and at least 1 byte, since a parameterless call still gets a buffer. The
// buffer must arrive whole: the recorder copies it only when it is non-null.
static bool SizeOk(const FxCall& c, std::string& why) {
    const uintptr_t owner = FxGetPtr(c.ufunc, DynOff::UOBJECT_OUTER);
    for (const FunctionInfo& fi : Ubel::WalkFunctions(owner)) {
        if (fi.address != c.ufunc) continue;
        const uint32_t want = (std::max)(Ubel::ParamBufferSize(fi), 1u);
        if (c.size == want && c.params.size() == static_cast<size_t>(c.size)) return true;
        why = FxCallStr(c) + FxFmt(" -- want %u bytes, %zu arrived", want, c.params.size());
        return false;
    }
    why = FxCallStr(c) + " -- not listed by the class that owns it";
    return false;
}

// ══ CUR ══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockCur() {
    blk("CUR - TeleportToCursor: the mouse or the screen centre, three hit paths in order, the trace, and the jump");

    // ── CUR.01 ──
    {
        FxReset();
        FxNoGWorld();
        FxClearRecorders();
        const CursorRun r = Cursor(kChan);
        check("CUR.01 chain failure: no GWorld answers -1", r.rc == Wirbel::TP_ERR_NOT_INIT, RunStr(r));
        check("CUR.01 chain failure: outHit, the tier and usedCenter keep what was planted before the call",
              FxIsSentinel(r.hit) && r.tier == kTierSentinel && r.usedCenter, RunStr(r));
        check("CUR.01 chain failure: no engine call and no Teleport line", FxCountAll() == 0 && FxTeleportLines() == 0,
              FxCallsStr(FxCalls()) + FxLogDump());
    }

    // ── CUR.02: the mouse is used only when GetMousePosition ran and answered true ──
    {
        FxReset();
        FxHit(FxKind::HitCursor, kChanZero);
        FxClearRecorders();
        const CursorRun r = Cursor(kChanZero);
        check("CUR.02 mouse: GetMousePosition answering true is the cursor -- usedCenter written false, no "
              "GetViewportSize, path 1 asked once and hit",
              r.rc == Wirbel::TP_OK && !r.usedCenter && FxCount(FxKind::MousePos) == 1 &&
                  FxCount(FxKind::Viewport) == 0 && FxCount(FxKind::HitCursor) == 1,
              RunStr(r) + FxCallsStr(FxCalls()));
        const FxCall mp = FxNth(FxKind::MousePos);
        check("CUR.02 mouse: asked of PC_0 with a fresh 0xC-byte buffer",
              mp.instance == g_pc0 && mp.ufunc == g_fnMousePos && mp.size == static_cast<uint32_t>(kMp.parmsSize) &&
                  FxZeroOutside(mp, {}),
              FxCallStr(mp));
    }
    {
        struct MouseFail { const char* label; std::function<void()> arm; };
        const MouseFail fails[] = {
            { "CUR.02 mouse ReturnValue false: GetMousePosition answering false is no cursor -- the centre is used "
              "(usedCenter true, one GetViewportSize, no path 1, path 2 at (960.5, 540.5))",
              [] { FxRetByte(FxKind::MousePos, 0); } },
            { "CUR.02 mouse invoke failed: GetMousePosition answering rc -5 is no cursor -- the centre is used "
              "(usedCenter true, one GetViewportSize, no path 1, path 2 at (960.5, 540.5))",
              [] { FxRcs(FxKind::MousePos, { kRcQueued }); } },
        };
        for (const auto& f : fails) {
            FxReset();
            f.arm();
            FxHit(FxKind::HitScreen, kChanZero);
            FxClearRecorders();
            const CursorRun r = Cursor(kChanZero, true, false);
            const FxCall hs = FxNth(FxKind::HitScreen);
            check(f.label,
                  r.rc == Wirbel::TP_OK && r.usedCenter && FxCount(FxKind::Viewport) == 1 &&
                      FxCount(FxKind::HitCursor) == 0 && ScreenIs(hs, kHs5, kCoordD, kCenter),
                  RunStr(r) + " " + ScreenStr(hs, kHs5, kCoordD) + FxCallsStr(FxCalls()));
        }
    }
    {
        FxReset();
        FxPcFns(FxPcFnList({ .mouse = FxMouseShape::Double }));
        FxHit(FxKind::HitScreen, kChanZero);
        FxClearRecorders();
        const CursorRun r = Cursor(kChanZero);
        const FxCall mp = FxNth(FxKind::MousePos);
        const FxCall hs = FxNth(FxKind::HitScreen);
        check("CUR.02 double mouse: double LocationX / LocationY (a 0x18-byte GetMousePosition) are read as doubles -- "
              "the cursor is kept and path 2 traces from (640.25, 360.75), not the centre",
              r.rc == Wirbel::TP_OK && !r.usedCenter && mp.size == static_cast<uint32_t>(kMpD.parmsSize) &&
                  ScreenIs(hs, kHs5, kCoordD, kMouse),
              RunStr(r) + " " + ScreenStr(hs, kHs5, kCoordD) + FxCallStr(mp));
    }

    // ── CUR.03: a cursor near (0, 0) on BOTH axes is a captured one ──
    {
        struct ZeroCase { double x, y; bool zero; const char* label; };
        const ZeroCase cases[] = {
            { 0.5, 0.25, true,
              "CUR.03 zero cursor (0.5, 0.25): 'I' \"...GetMousePosition returned (0,0)...falling back to screen "
              "center\", and the centre is used" },
            { 1.0, 0.0, false,
              "CUR.03 cursor kept (1.0, 0): |x| is not below 1, so the mouse stands -- no fallback line, usedCenter "
              "false, path 1 asked" },
            { 0.99, 0.5, true,
              "CUR.03 zero cursor (0.99, 0.5): both below 1 -- the fallback line, and the centre is used" },
        };
        for (const auto& z : cases) {
            FxReset();
            FxMouse(z.x, z.y);
            FxHit(FxKind::HitCursor, kChanZero);
            FxHit(FxKind::HitScreen, kChanZero);
            FxClearRecorders();
            const CursorRun r = Cursor(kChanZero, true, !z.zero);
            const int zeroLines = FxLogCount(FxText::kCursorZero, 'I');
            const int anyZeroLines = FxLogCount(FxText::kCursorZero);
            const bool ok = z.zero ? (r.rc == Wirbel::TP_OK && r.usedCenter && zeroLines == 1 &&
                                      FxCount(FxKind::HitCursor) == 0 &&
                                      ScreenIs(FxNth(FxKind::HitScreen), kHs5, kCoordD, kCenter))
                                   : (r.rc == Wirbel::TP_OK && !r.usedCenter && anyZeroLines == 0 &&
                                      FxCount(FxKind::HitCursor) == 1);
            check(z.label, ok, RunStr(r) + FxLogDump() + FxCallsStr(FxCalls()));
        }
    }

    // ── CUR.04 ──
    {
        FxReset();
        FxRetByte(FxKind::MousePos, 0);
        FxClearRecorders();
        const CursorRun r = Cursor(kChan, false);
        check("CUR.04 no fallback: no mouse with fallbackToCenter false answers -9 after the one GetMousePosition",
              r.rc == Wirbel::TP_ERR_NO_CURSOR && FxKindsAre({ FxKind::MousePos }),
              RunStr(r) + " " + FxKindsStr(FxKinds()));
        check("CUR.04 no fallback: no line past the chain's, and outHit, the tier and usedCenter untouched",
              FxTeleportLines() == 1 && FxLogCount(FxLine::Chain(g_pc0), 'I') == 1 && FxIsSentinel(r.hit) &&
                  r.tier == kTierSentinel && r.usedCenter,
              RunStr(r) + FxLogDump());
    }

    // ── CUR.05: the centre is half the viewport, and only a viewport with both sides counts ──
    {
        FxReset();
        FxRetByte(FxKind::MousePos, 0);
        FxHit(FxKind::HitScreen, kChanZero);
        FxClearRecorders();
        const CursorRun r = Cursor(kChanZero, true, false);
        const FxCall vp = FxNth(FxKind::Viewport);
        const FxCall hs = FxNth(FxKind::HitScreen);
        check("CUR.05 centre: a 1921x1081 viewport puts the trace at (960.5, 540.5) -- halves, not whole pixels -- "
              "asked of PC_0",
              r.rc == Wirbel::TP_OK && r.usedCenter && vp.instance == g_pc0 && ScreenIs(hs, kHs5, kCoordD, kCenter),
              RunStr(r) + " " + ScreenStr(hs, kHs5, kCoordD));
    }
    {
        struct NoCentre { const char* label; std::function<void()> arm; bool viewportAsked; };
        const NoCentre cases[] = {
            { "CUR.05 no centre (1920 x 0): a viewport with no height answers -9 -- no trace",
              [] { FxViewportSize(kViewportFlat[0], kViewportFlat[1]); }, true },
            { "CUR.05 no centre (GetViewportSize rc -5): answers -9 -- no trace",
              [] { FxRcs(FxKind::Viewport, { kRcQueued }); }, true },
            { "CUR.05 no centre (GetViewportSize missing): answers -9 -- no trace",
              [] { FxPcFns(FxPcFnList({ .viewport = false })); }, false },
        };
        for (const auto& n : cases) {
            FxReset();
            FxRetByte(FxKind::MousePos, 0);
            n.arm();
            FxClearRecorders();
            const CursorRun r = Cursor(kChanZero, true, false);
            const bool kinds = n.viewportAsked ? FxKindsAre({ FxKind::MousePos, FxKind::Viewport })
                                               : FxKindsAre({ FxKind::MousePos });
            check(n.label,
                  r.rc == Wirbel::TP_ERR_NO_CURSOR && kinds && !r.usedCenter && FxIsSentinel(r.hit) &&
                      r.tier == kTierSentinel,
                  RunStr(r) + " " + FxKindsStr(FxKinds()));
        }
    }

    // ── CUR.06 ──
    {
        FxReset();
        FxHit(FxKind::HitCursor, kChan);
        FxClearRecorders();
        const CursorRun r = Cursor(kChan, true, true);
        check("CUR.06 usedCenter: a mouse read writes false over the caller's true", r.rc == Wirbel::TP_OK && !r.usedCenter,
              RunStr(r));
        check("CUR.06 cursor line: 'I' \"Teleport: cursor mouse=(640, 361) haveMouse=1 usedCenter=0 channel=3\"",
              FxLogCount(FxFmt(kCursorLineFmt, kMouse[0], kMouse[1], 1, 0, kChan), 'I') == 1, FxLogDump());
    }
    {
        FxReset();
        FxRetByte(FxKind::MousePos, 0);
        FxHit(FxKind::HitScreen, kChan);
        FxClearRecorders();
        const CursorRun r = Cursor(kChan, true, false);
        check("CUR.06 usedCenter: the centre writes true over the caller's false", r.rc == Wirbel::TP_OK && r.usedCenter,
              RunStr(r));
        check("CUR.06 cursor line: 'I' with the centre as the line prints it, haveMouse=0 usedCenter=1 channel=3",
              FxLogCount(FxFmt(kCursorLineFmt, kCenter[0], kCenter[1], 0, 1, kChan), 'I') == 1, FxLogDump());
    }

    // ── CUR.07 / CUR.08: the two channel scans, nothing to hit ──
    {
        FxReset();
        FxClearRecorders();
        const CursorRun r = Cursor(kChan);
        const auto hc = FxCallsOf(FxKind::HitCursor);
        const auto hs = FxCallsOf(FxKind::HitScreen);
        const auto hcChans = Channels(FxKind::HitCursor, kHc5.channel);
        check("CUR.07 path 1 order: channel 3 with nothing to hit asks 3 first, then 0..9 without 3 -- 10 calls, all "
              "on PC_0",
              hcChans == kOrder3 && Every(hc, [](const FxCall& c) { return c.instance == g_pc0; }),
              IntsStr(hcChans) + " " + RunStr(r));
        const auto p1Buf = [](const FxCall& c) {
            return FxRd<uint8_t>(c, kHc5.complex) == 1 &&
                   FxZeroOutside(c, { Keep(kHc5.channel, 1), Keep(kHc5.complex, 1) });
        };
        check("CUR.07 path 1 buffer: every call is a fresh buffer carrying its TraceChannel and bTraceComplex 1, "
              "nothing else",
              !hc.empty() && Every(hc, p1Buf), FirstFail(hc, p1Buf));
        check("CUR.07 path 1 line: 'I' \"...GetHitResultUnderCursorByChannel hit=0 hitChannel=-1\"",
              FxLogCount(FxFmt(kPath1Fmt, 0, -1), 'I') == 1, FxLogDump());
        const auto hsChans = Channels(FxKind::HitScreen, kHs5.channel);
        check("CUR.08 path 2 order: still no hit, path 2 asks the same order -- 3 once, 10 calls",
              hsChans == kOrder3, IntsStr(hsChans));
        const auto p2Buf = [](const FxCall& c) {
            return ScreenIs(c, kHs5, kCoordD, kMouse) && FxRd<uint8_t>(c, kHs5.complex) == 1 &&
                   FxZeroOutside(c, { Keep(kHs5.screen, kHs5.screenBytes), Keep(kHs5.channel, 1),
                                      Keep(kHs5.complex, 1) });
        };
        check("CUR.08 path 2 buffer: every call carries ScreenPosition (640.25, 360.75) as two doubles and "
              "bTraceComplex 1, nothing else",
              !hs.empty() && Every(hs, p2Buf), FirstFail(hs, p2Buf));
        check("CUR.08 path 2 line: 'I' \"...GetHitResultAtScreenPosition screen=(640, 361) hit=0 hitChannel=-1\"",
              FxLogCount(FxFmt(kPath2Fmt, kMouse[0], kMouse[1], 0, -1), 'I') == 1, FxLogDump());
    }
    {
        struct Order { int32_t chan; const std::vector<int>* order; const char* label; };
        const Order orders[] = {
            { kChanPast9, &kOrder12,
              "CUR.07 path 1 order: channel 12, outside 0..9, skips nothing -- 12, then 0..9: 11 calls" },
            { kChanWide, &kOrder256,
              "CUR.07 path 1 order (pin): channel 256 reaches the engine as the byte 0, and the scan asks 0 again -- "
              "0, then 0..9: 11 calls" },
        };
        for (const auto& o : orders) {
            FxReset();
            FxClearRecorders();
            const CursorRun r = Cursor(o.chan);
            const auto chans = Channels(FxKind::HitCursor, kHc5.channel);
            check(o.label, r.rc == Wirbel::TP_ERR_NO_HIT && chans == *o.order, IntsStr(chans) + " " + RunStr(r));
        }
    }
    {
        FxReset();
        FxHit(FxKind::HitCursor, kChanLate);
        FxClearRecorders();
        const CursorRun r = Cursor(kChan);
        const auto chans = Channels(FxKind::HitCursor, kHc5.channel);
        check("CUR.07 path 1 stops at the hit: channel 3 with a hit on 5 asks 3, 0, 1, 2, 4, 5 and no more",
              r.rc == Wirbel::TP_OK && chans == kOrder3ToLate && HitIs(r, kHit), IntsStr(chans) + " " + RunStr(r));
        check("CUR.07 path 1 hit line: 'I' \"...hit=1 hitChannel=5\", and path 2 is never asked",
              FxLogCount(FxFmt(kPath1Fmt, 1, kChanLate), 'I') == 1 && FxCount(FxKind::HitScreen) == 0 &&
                  FxLogPrefix(kPath2Prefix) == 0,
              FxLogDump());
    }
    {
        FxReset();
        FxRetByte(FxKind::MousePos, 0);
        FxHit(FxKind::HitScreen, kChanZero);
        FxClearRecorders();
        const CursorRun r = Cursor(kChanZero, true, false);
        check("CUR.07 path 1 needs a real cursor: on the screen centre GetHitResultUnderCursorByChannel is never asked "
              "and logs nothing",
              r.rc == Wirbel::TP_OK && r.usedCenter && FxCount(FxKind::HitCursor) == 0 && FxLogPrefix(kPath1Prefix) == 0,
              RunStr(r) + FxLogDump());
    }
    {
        FxReset();
        FxPcFns(FxPcFnList({ .cursor = FxHitShape::NoHitResult }));
        FxHit(FxKind::HitScreen, kChanZero);
        FxClearRecorders();
        const CursorRun r = Cursor(kChanZero);
        check("CUR.07 no HitResult parameter: path 1 makes no call and logs hit=0 hitChannel=-1; path 2 hits instead",
              r.rc == Wirbel::TP_OK && FxCount(FxKind::HitCursor) == 0 && FxLogCount(FxFmt(kPath1Fmt, 0, -1), 'I') == 1 &&
                  FxCount(FxKind::HitScreen) == 1 && HitIs(r, kHit),
              RunStr(r) + FxLogDump());
    }

    // ── CUR.08: path 2 hits ──
    {
        FxReset();
        FxHit(FxKind::HitScreen, kChanZero);
        FxClearRecorders();
        const CursorRun r = Cursor(kChan);
        const auto chans = Channels(FxKind::HitScreen, kHs5.channel);
        check("CUR.08 path 2 hit: path 1 misses on all 10, path 2 asks 3 then 0 and stops at the hit on 0",
              r.rc == Wirbel::TP_OK && FxCount(FxKind::HitCursor) == kScanCalls && chans == kOrder3To0 &&
                  HitIs(r, kHit),
              IntsStr(chans) + " " + RunStr(r));
        check("CUR.08 path 2 line: 'I' \"...GetHitResultAtScreenPosition screen=(640, 361) hit=1 hitChannel=0\"",
              FxLogCount(FxFmt(kPath2Fmt, kMouse[0], kMouse[1], 1, kChanZero), 'I') == 1, FxLogDump());
    }
    {
        // The UE4 world: an 8-byte ScreenPosition, a float FHitResult, the float K2_TeleportTo twin.
        FxReset();
        FxUE4World();
        FxHit(FxKind::HitScreen, kChanZero);
        FxClearRecorders();
        const CursorRun r = Cursor(kChanZero);
        const FxCall hs = FxNth(FxKind::HitScreen);
        check("CUR.08 UE4 screen position: an 8-byte ScreenPosition on PCF_0 gets two floats (640.25, 360.75)",
              hs.instance == g_pcF0 && hs.ufunc == g_fnHitScreenF && ScreenIs(hs, kHs4, kCoordF, kMouse),
              FxCallStr(hs) + " " + ScreenStr(hs, kHs4, kCoordF));
        const FxCall tp = FxNth(FxKind::TeleportTo);
        const auto dest = FxVec(tp, kTp4.loc, kVecF);
        check("CUR.08 UE4 hit point: the float ImpactPoint is read at its own 12 bytes -- outHit (3000.5, 3100.5, "
              "50.5), and the K2_TeleportTo twin on BP_HeroF_C_0 gets DestLocation floats (3000.5, 3100.5, 141.0)",
              r.rc == Wirbel::TP_OK && HitIs(r, kHit) && tp.instance == g_heroF0 && tp.ufunc == g_fnTeleportToF &&
                  FxEq3(dest, kCursorDest),
              RunStr(r) + " dest=" + FxArrStr(dest) + " " + FxCallStr(tp));
        const auto rot = FxVec(tp, kTp4.rot, kVecF);
        check("CUR.14 UE4 facing: with no explicit rotation the twin's DestRotation floats @0x0C are PCF_0's float "
              "ControlRotation (-10.5, 90.5, 0)",
              tp.seq != 0 && FxEq3(rot, kRot), FxArrStr(rot));
    }
    {
        FxReset();
        FxHit(FxKind::HitCursor, kChanZero);
        FxHit(FxKind::HitScreen, kChanZero);
        FxClearRecorders();
        const CursorRun r = Cursor(kChanZero);
        check("CUR.08 path 2 after a path-1 hit: never asked, and no path-2 line",
              r.rc == Wirbel::TP_OK && FxCount(FxKind::HitScreen) == 0 && FxLogPrefix(kPath2Prefix) == 0,
              RunStr(r) + FxLogDump());
    }

    // ── CUR.09: which member of the FHitResult is the point ──
    {
        FxReset();
        FxHit(FxKind::HitCursor, kChanZero);
        FxClearRecorders();
        const CursorRun r = Cursor(kChanZero);
        check("CUR.09 hit point: ImpactPoint is preferred -- outHit is the point (3000.5, ...), not Location 10 "
              "behind it in X",
              r.rc == Wirbel::TP_OK && HitIs(r, kHit), RunStr(r));
    }
    {
        FxReset();
        FxPcFns(FxPcFnList({ .cursor = FxHitShape::NoImpact }));
        FxHit(FxKind::HitCursor, kChanZero);
        FxClearRecorders();
        const CursorRun r = Cursor(kChanZero);
        const auto dest = FxVec(FxNth(FxKind::TeleportTo), kTp5.loc, kVecD);
        check("CUR.09 no ImpactPoint: the HitResult's Location is used -- outHit (2990.5, 3100.5, 50.5), the jump "
              "raised from there",
              r.rc == Wirbel::TP_OK && HitIs(r, kHitLocation) && FxEq3(dest, kLocationDest),
              RunStr(r) + " dest=" + FxArrStr(dest));
    }
    {
        struct Miss { FxHitShape shape; int32_t parmsSize; const char* label; };
        const Miss misses[] = {
            { FxHitShape::NoPoint, kHc5.parmsSize,
              "CUR.09 no point member: a hit whose HitResult has neither ImpactPoint nor Location is a miss -- path 1 "
              "goes on through all 10 and logs hit=0, path 2 hits instead" },
            { FxHitShape::Short, kHcShort.parmsSize,
              "CUR.09 a point past the buffer: ImpactPoint at 0x28 of a 0x30-byte call is a miss, not a read past the "
              "end -- path 1 goes through all 10 and logs hit=0, path 2 hits instead" },
        };
        for (const auto& m : misses) {
            FxReset();
            FxPcFns(FxPcFnList({ .cursor = m.shape }));
            FxHit(FxKind::HitCursor, kChanZero);
            FxHit(FxKind::HitScreen, kChanZero);
            FxClearRecorders();
            const CursorRun r = Cursor(kChanZero);
            check(m.label,
                  r.rc == Wirbel::TP_OK && FxCount(FxKind::HitCursor) == kScanCalls &&
                      FxNth(FxKind::HitCursor).size == static_cast<uint32_t>(m.parmsSize) &&
                      FxLogCount(FxFmt(kPath1Fmt, 0, -1), 'I') == 1 && FxCount(FxKind::HitScreen) == 1 &&
                      HitIs(r, kHit),
                  RunStr(r) + FxCallsStr(FxCallsOf(FxKind::HitCursor)));
        }
    }

    // ── CUR.10: the deprojection, past two missing paths ──
    {
        FxReset();
        FxPcFns(FxPcFnList({ .deproject = FxDeprojShape::Missing }));
        FxClearRecorders();
        const CursorRun r = Cursor(kChan);
        check("CUR.10 no deprojection: DeprojectScreenPositionToWorld missing answers -4 with the 'W' naming "
              "BP_GameCtrl_C, and no trace",
              r.rc == Wirbel::TP_ERR_REFLECTION &&
                  FxLogCount(FxFmt(kDeprojMissingFmt, "BP_GameCtrl_C"), 'W') == 1 && FxCount(FxKind::Deproject) == 0 &&
                  FxCount(FxKind::Trace) == 0,
              RunStr(r) + FxLogDump());
    }
    {
        struct DeprojFail { const char* label; std::function<void()> arm; };
        const DeprojFail fails[] = {
            { "CUR.10 deprojection rc -5: answers -5 with 'W' \"...invoke failed/empty\", no trace",
              [] { FxRcs(FxKind::Deproject, { kRcQueued }); } },
            { "CUR.10 deprojection ReturnValue false: answers -5 with 'W' \"...invoke failed/empty\", no trace",
              [] { FxRetByte(FxKind::Deproject, 0); } },
        };
        for (const auto& f : fails) {
            FxReset();
            f.arm();
            FxClearRecorders();
            const CursorRun r = Cursor(kChan);
            check(f.label,
                  r.rc == Wirbel::TP_ERR_INVOKE && FxLogCount(FxText::kDeprojFailed, 'W') == 1 &&
                      FxCount(FxKind::Deproject) == 1 && FxCount(FxKind::Trace) == 0 && FxLogPrefix(kDeprojPrefix) == 0,
                  RunStr(r) + FxLogDump());
        }
    }
    {
        FxReset();
        FxHit(FxKind::Trace, kChan);
        FxClearRecorders();
        const CursorRun r = Cursor(kChan);
        const FxCall dp = FxNth(FxKind::Deproject);
        check("CUR.10 deprojection: on PC_0, ScreenX / ScreenY carry the mouse as floats (640.25 @0, 360.75 @4) in a "
              "fresh 0x40-byte buffer",
              dp.instance == g_pc0 && dp.size == static_cast<uint32_t>(kDp5.parmsSize) &&
                  FxRd<float>(dp, kDp5.sx) == static_cast<float>(kMouse[0]) &&
                  FxRd<float>(dp, kDp5.sy) == static_cast<float>(kMouse[1]) &&
                  FxZeroOutside(dp, { Keep(kDp5.sx, kDp5.screenBytes), Keep(kDp5.sy, kDp5.screenBytes) }),
              FxCallStr(dp) + FxFmt(" x=%.9g y=%.9g", FxRd<float>(dp, kDp5.sx), FxRd<float>(dp, kDp5.sy)));
        check("CUR.10 deprojection line: 'I' \"...deproject loc=(10.5, 20.5, 30.5) dir=(0.000, 0.000, -1.000)\"",
              r.rc == Wirbel::TP_OK &&
                  FxLogCount(FxFmt(kDeprojFmt, kDeprojLoc[0], kDeprojLoc[1], kDeprojLoc[2], kDeprojDir[0],
                                   kDeprojDir[1], kDeprojDir[2]),
                             'I') == 1,
              RunStr(r) + FxLogDump());
    }
    {
        FxReset();
        FxPcFns(FxPcFnList({ .deproject = FxDeprojShape::Double }));
        FxHit(FxKind::Trace, kChan);
        FxClearRecorders();
        const CursorRun r = Cursor(kChan);
        const FxCall dp = FxNth(FxKind::Deproject);
        const auto start = FxVec(FxNth(FxKind::Trace), FxLts::kStart, kVecD);
        check("CUR.10 double deprojection: double ScreenX / ScreenY get the mouse as doubles (@0, @8), and the twin's "
              "WorldLocation (@0x10) is the trace's Start",
              r.rc == Wirbel::TP_OK && dp.size == static_cast<uint32_t>(kDp5D.parmsSize) &&
                  FxRd<double>(dp, kDp5D.sx) == kMouse[0] && FxRd<double>(dp, kDp5D.sy) == kMouse[1] &&
                  FxEq3(start, kDeprojLoc),
              RunStr(r) + FxFmt(" x=%.9g y=%.9g start=", FxRd<double>(dp, kDp5D.sx), FxRd<double>(dp, kDp5D.sy)) +
                  FxArrStr(start));
    }

    // ── CUR.11: which library object, which function ──
    {
        FxReset();
        FxHit(FxKind::Trace, kChan);
        FxClearRecorders();
        const CursorRun r = Cursor(kChan);
        const auto traces = FxCallsOf(FxKind::Trace);
        check("CUR.11 trace target: one LineTraceSingle (the LTS-D blob) on Default__KismetSystemLibrary in slot 11 -- "
              "not the Ex decoy CDO in slot 9, not a ForObjects / ByProfile sibling",
              r.rc == Wirbel::TP_OK && FxSlot(kIdxCdoKslEx) == g_cdoKslEx && FxSlot(kIdxCdoKsl) == g_cdoKsl &&
                  traces.size() == 1 && traces[0].instance == g_cdoKsl && traces[0].ufunc == g_ltsD &&
                  FxCount(FxKind::Other) == 0,
              RunStr(r) + FxCallsStr(traces));
    }
    {
        struct KslFail { const char* label; std::function<void()> arm; const char* text; };
        const KslFail fails[] = {
            { "CUR.11 no KismetSystemLibrary object: only the Ex decoy CDO left -- -4 with its 'W', no trace",
              [] { FxPool(FxPoolVariant::KslWithoutCdo); }, FxText::kKslMissing },
            { "CUR.11 no LineTraceSingle: a library with LineTraceMulti only -- -4 with its 'W', no trace",
              [] { FxKsl(g_fnLtsMulti); }, FxText::kLtsMissing },
            { "CUR.11 no OutHit: a LineTraceSingle without it -- -4 with its 'W' before any trace call",
              [] { FxKsl(g_ltsNoOutHit); }, FxText::kNoOutHit },
        };
        for (const auto& f : fails) {
            FxReset();
            f.arm();
            FxClearRecorders();
            const CursorRun r = Cursor(kChan);
            check(f.label,
                  r.rc == Wirbel::TP_ERR_REFLECTION && FxLogCount(f.text, 'W') == 1 && FxCount(FxKind::Deproject) == 1 &&
                      FxCount(FxKind::Trace) == 0 && FxCount(FxKind::Other) == 0 && Moves() == 0,
                  RunStr(r) + FxLogDump() + FxCallsStr(FxCalls()));
        }
    }

    // ── CUR.12: the LineTraceSingle buffer ──
    {
        FxReset();
        FxHit(FxKind::Trace, kChan);
        FxClearRecorders();
        const CursorRun r = Cursor(kChan);
        const auto traces = FxCallsOf(FxKind::Trace);
        const FxCall tr = traces.empty() ? FxCall{} : traces[0];
        const auto start = FxVec(tr, FxLts::kStart, kVecD);
        const auto end = FxVec(tr, FxLts::kEnd, kVecD);
        check("CUR.12 trace buffer: exactly one trace -- WorldContextObject BP_Hero_C_0, Start the deprojected "
              "(10.5, 20.5, 30.5), End 1e5 along (0, 0, -1): (10.5, 20.5, -99969.5)",
              traces.size() == 1 && FxRd<uintptr_t>(tr, FxLts::kWorldContext) == g_hero0 && FxEq3(start, kDeprojLoc) &&
                  FxEq3(end, kTraceEnd),
              FxCallsStr(traces) + " start=" + FxArrStr(start) + " end=" + FxArrStr(end));
        check("CUR.12 trace buffer: TraceChannel 3 (the requested one, no scan), bTraceComplex 1, bIgnoreSelf 1, every "
              "other byte 0, 0x148 bytes",
              tr.size == static_cast<uint32_t>(FxLts::kParmsSize) && FxRd<uint8_t>(tr, FxLts::kChannel) == kChan &&
                  FxRd<uint8_t>(tr, FxLts::kComplex) == 1 && FxRd<uint8_t>(tr, FxLts::kIgnoreSelf) == 1 &&
                  FxZeroOutside(tr, { Keep(FxLts::kWorldContext, kPtrBytes), Keep(FxLts::kStart, kVecD),
                                      Keep(FxLts::kEnd, kVecD), Keep(FxLts::kChannel, 1), Keep(FxLts::kComplex, 1),
                                      Keep(FxLts::kIgnoreSelf, 1) }),
              FxCallStr(tr));
        check("CUR.12 trace line: 'I' \"...LineTraceSingle r=0 returnedHit=1 haveHit=1\", and the jump takes its hit",
              r.rc == Wirbel::TP_OK && HitIs(r, kHit) && FxLogCount(FxFmt(kTraceFmt, 0, 1, 1), 'I') == 1,
              RunStr(r) + FxLogDump());
    }
    {
        FxReset();
        FxDeprojectDir(kDeprojDirOblique);
        FxHit(FxKind::Trace, kChan);
        FxClearRecorders();
        const CursorRun r = Cursor(kChan);
        const auto traces = FxCallsOf(FxKind::Trace);
        const FxCall tr = traces.empty() ? FxCall{} : traces[0];
        const auto start = FxVec(tr, FxLts::kStart, kVecD);
        const auto end = FxVec(tr, FxLts::kEnd, kVecD);
        check("CUR.12 oblique ray: dir (0.48, 0.6, -0.64) gives End (48010.5, 60020.5, -63969.5), each component from "
              "its own, and Start exactly the deprojected location",
              r.rc == Wirbel::TP_OK && traces.size() == 1 && FxEq3(start, kDeprojLoc) &&
                  FxEq3(end, kTraceEndOblique, kObliqueTol),
              "start=" + FxArrStr(start) + " end=" + FxArrStr(end));
    }
    {
        // LTS-F: float Start / End, and the self-ignore flag registered under a lower-case spelling.
        FxReset();
        FxUE4World();
        FxHit(FxKind::Trace, kChan);
        FxClearRecorders();
        const CursorRun r = Cursor(kChan);
        const auto traces = FxCallsOf(FxKind::Trace);
        const FxCall tr = traces.empty() ? FxCall{} : traces[0];
        check("CUR.12 LTS-F: the parameter spelled \"bignoreself\" gets 1 @0xC8 -- a parameter name matches in any case",
              r.rc == Wirbel::TP_OK && traces.size() == 1 && tr.ufunc == g_ltsF &&
                  FxRd<uint8_t>(tr, FxLtsF::kIgnoreSelf) == 1,
              RunStr(r) + FxCallsStr(traces));
        const auto start = FxVec(tr, FxLtsF::kStart, kVecF);
        const auto end = FxVec(tr, FxLtsF::kEnd, kVecF);
        check("CUR.12 LTS-F buffer: WorldContextObject BP_HeroF_C_0, Start / End as floats (10.5, 20.5, 30.5) / "
              "(10.5, 20.5, -99969.5), TraceChannel 3, bTraceComplex 1, every other byte 0, 0xD0 bytes",
              tr.size == static_cast<uint32_t>(FxLtsF::kParmsSize) &&
                  FxRd<uintptr_t>(tr, FxLtsF::kWorldContext) == g_heroF0 && FxEq3(start, kDeprojLoc) &&
                  FxEq3(end, kTraceEnd) && FxRd<uint8_t>(tr, FxLtsF::kChannel) == kChan &&
                  FxRd<uint8_t>(tr, FxLtsF::kComplex) == 1 &&
                  FxZeroOutside(tr, { Keep(FxLtsF::kWorldContext, kPtrBytes), Keep(FxLtsF::kStart, kVecF),
                                      Keep(FxLtsF::kEnd, kVecF), Keep(FxLtsF::kChannel, 1),
                                      Keep(FxLtsF::kComplex, 1), Keep(FxLtsF::kIgnoreSelf, 1) }),
              FxCallStr(tr) + " start=" + FxArrStr(start) + " end=" + FxArrStr(end));
    }

    // ── CUR.13 ──
    {
        FxReset();
        const Wirbel::Marker planted = FxMarker(kPlanted, kMapB);
        FxPlantLast(planted);
        FxClearRecorders();
        const CursorRun r = Cursor(kChan);
        check("CUR.13 no hit: every path missing answers -8 with 'W' \"...no blocking hit (channel=3). Try a different "
              "trace channel.\"",
              r.rc == Wirbel::TP_ERR_NO_HIT && FxLogCount(FxFmt(kNoHitFmt, kChan), 'W') == 1 &&
                  FxLogCount(FxFmt(kTraceFmt, 0, 0, 0), 'I') == 1,
              RunStr(r) + FxLogDump());
        const Wirbel::Marker last = FxLastNow();
        check("CUR.13 no hit: no move call, outHit and the tier untouched, the last slot as planted",
              Moves() == 0 && FxIsSentinel(r.hit) && r.tier == kTierSentinel && FxMarkerEq(last, planted),
              RunStr(r) + " last: " + FxMarkerStr(last));
    }

    // ── CUR.14: the jump ──
    {
        FxReset();
        FxPlantLast(FxMarker(kPlanted, kMapB));
        FxHit(FxKind::HitCursor, kChanZero);
        FxClearRecorders();
        const CursorRun r = Cursor(kChanZero);
        check("CUR.14 jump: outHit is the raw hit (3000.5, 3100.5, 50.5), without zOffset; tier 1",
              r.rc == Wirbel::TP_OK && HitIs(r, kHit) && r.tier == kTierInvoke, RunStr(r));
        const FxCall tp = FxNth(FxKind::TeleportTo);
        const auto dest = FxVec(tp, kTp5.loc, kVecD);
        const auto rot = FxVec(tp, kTp5.rot, kVecD);
        check("CUR.14 jump: K2_TeleportTo on BP_Hero_C_0 -- DestLocation the hit raised by 90.5 (3000.5, 3100.5, "
              "141.0), DestRotation the current ControlRotation (-10.5, 90.5, 0)",
              tp.instance == g_hero0 && tp.ufunc == g_fnTeleportTo && FxEq3(dest, kCursorDest) && FxEq3(rot, kRot),
              FxCallStr(tp) + " dest=" + FxArrStr(dest) + " rot=" + FxArrStr(rot));
        check("CUR.14 jump: the calls are exactly GetMousePosition, the hit, K2_TeleportTo, K2_GetActorLocation, "
              "StopMovement, StopMovementImmediately -- no K2_SetActorLocation, no SetControlRotation",
              FxKindsAre({ FxKind::MousePos, FxKind::HitCursor, FxKind::TeleportTo, FxKind::GetLoc, FxKind::StopMove,
                           FxKind::StopMoveNow }),
              FxKindsStr(FxKinds()));
        const Wirbel::Marker last = FxLastNow();
        check("CUR.14 jump: the last slot holds the pose before the jump -- kStart, kRot, Map_A, world",
              FxMarkerEq(last, FxMarker(kStartPose, kMapA)), FxMarkerStr(last));
        check("CUR.14 jump line: 'I' \"Teleport: cursor -> (3000.5, 3100.5, 141.0) center=0\"",
              FxLogCount(FxFmt(kJumpFmt, kCursorDest[0], kCursorDest[1], kCursorDest[2], 0), 'I') == 1, FxLogDump());
    }
    {
        FxReset();
        std::ignore = FxUseRoRoot();
        // The fake's own moves reach the copy through the writable view, so every setter is made to fail: only the
        // DLL's raw write is left, and the read-only view refuses it.
        for (FxKind k : { FxKind::TeleportTo, FxKind::SetActorLoc, FxKind::SetWorldLoc, FxKind::SetRelLoc })
            FxRcAlways(k, kRcQueued);
        FxHit(FxKind::HitCursor, kChanZero);
        FxClearRecorders();
        const CursorRun r = Cursor(kChanZero);
        check("CUR.14 write fails: every setter failing on a read-only root -- -10, outHit already the hit, the tier "
              "untouched, no settle and no jump line",
              r.rc == Wirbel::TP_ERR_WRITE_FAILED && HitIs(r, kHit) && r.tier == kTierSentinel &&
                  FxCount(FxKind::StopMove) == 0 && FxCount(FxKind::StopMoveNow) == 0 && FxLogPrefix(kJumpPrefix) == 0,
              RunStr(r) + FxLogDump());
    }

    // ── CUR.15: the jump uses the chain resolved at entry ──
    {
        FxReset();
        FxHit(FxKind::HitCursor, kChanZero);
        FxOnCall([](FxKind k, uintptr_t, int) {
            if (k == FxKind::HitCursor) FxSetPtr(g_pc0, FxOff::kPawn, g_npc0);
        });
        FxClearRecorders();
        const CursorRun r = Cursor(kChanZero);
        FxOnCall(nullptr);
        const FxCall tp = FxNth(FxKind::TeleportTo);
        const Wirbel::Marker last = FxLastNow();
        check("CUR.15 entry chain: PC_0's pawn switched to BP_Npc_C_0 during the hit test -- the jump still moves "
              "BP_Hero_C_0, while the undo point, resolved anew, is the Npc's (4000.5, 4100.5, 120.5)",
              r.rc == Wirbel::TP_OK && tp.instance == g_hero0 && last.Valid && FxEq3(FxXyzOf(last.P), kNpcPos),
              RunStr(r) + " " + FxCallStr(tp) + " last: " + FxMarkerStr(last));
    }
}

// ══ MAP ══════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockMap() {
    blk("MAP - GetCurrentMapName: no lock and no chain walk; what it answers and writes for each buffer and world");
    FxReset();
    char buf[kMapCap];
    check("MAP.01 null buffer: answers false", !Wirbel::GetCurrentMapName(nullptr, kMapCap));
    FxFillMap(buf, sizeof buf);
    const bool cap0 = Wirbel::GetCurrentMapName(buf, 0);
    const bool capNeg = Wirbel::GetCurrentMapName(buf, -1);
    check("MAP.01 cap 0 or below: answers false and writes nothing", !cap0 && !capNeg && AllFill(buf, sizeof buf));
    FxFillMap(buf, sizeof buf);
    const bool full = Wirbel::GetCurrentMapName(buf, kMapCap);
    check("MAP.01 the default world: true and \"Map_A\"", full && BufIs(buf, sizeof buf, kMapA),
          BufStr(buf, sizeof buf));
    FxFillMap(buf, sizeof buf);
    const bool cut = Wirbel::GetCurrentMapName(buf, kShortCap);
    check("MAP.01 cap 4: true, \"Map\" terminated at [3], and [4] still the fill",
          cut && BufIs(buf, kShortCap, "Map") && buf[kShortCap] == kMapFill, BufStr(buf, sizeof buf));
    check("MAP.01 no chain walk: none of the reads above made an engine call or logged a Teleport line",
          FxCountAll() == 0 && FxTeleportLines() == 0, FxCallsStr(FxCalls()) + FxLogDump());
    FxNoGWorld();
    FxFillMap(buf, sizeof buf);
    buf[0] = 'X';
    const bool noWorld = Wirbel::GetCurrentMapName(buf, kMapCap);
    check("MAP.01 no GWorld: false, with buf[0] cleared to NUL", !noWorld && buf[0] == '\0', BufStr(buf, sizeof buf));
    FxReset();
    FxWorld(g_worldEmpty);
    FxFillMap(buf, sizeof buf);
    const bool empty = Wirbel::GetCurrentMapName(buf, kMapCap);
    check("MAP.01 an empty world name: false, with an empty string", !empty && buf[0] == '\0', BufStr(buf, sizeof buf));
}

// ══ PACK ═════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockPack() {
    blk("PACK - parameter packing: the FVector width rule, a parameter ending at the buffer end, buffer sizes, the "
        "function walk, ReturnedTrue");

    // ── PACK.01: the UE4 world, every vector at 12 bytes ──
    {
        FxReset();
        FxUE4World();
        FxClearRecorders();
        Wirbel::Pose p = FxSentinelPose();
        char map[kMapCap];
        FxFillMap(map, sizeof map);
        uint8_t src = kByteSentinel;
        bool pr = true;
        const int32_t rc = Wirbel::GetPose(p, map, kMapCap, &src, &pr);
        check("PACK.01 UE4 GetPose: a 12-byte RelativeLocation and ControlRotation are read as floats and widened -- "
              "(1000.5, -2000.5, 300.5 | -10.5, 90.5, 0), Map_A, raw, world",
              rc == Wirbel::TP_OK && FxPoseIs(p, kStart, kRot) && BufIs(map, sizeof map, kMapA) && src == 0 && !pr,
              FxS(rc) + " " + FxPoseStr(p));
        Wirbel::MovementState m{};
        const int32_t rcM = Wirbel::GetPoseAndMovement(p, map, kMapCap, &src, m, &pr);
        check("PACK.01 UE4 movement: CMCF_0's 12-byte Velocity @0x30 is read as floats -- (0, 300, 400), speed 500 -- "
              "and its Acceleration (1.5, 2.5, 3.5)",
              rcM == Wirbel::TP_OK && m.HasMovement && m.VelOwnerAddr == g_cmcF0 &&
                  m.VelFieldOffset == FxOff::kVelocity && m.VelX == kVel[0] && m.VelY == kVel[1] &&
                  m.VelZ == kVel[2] && m.Speed == kSpeed && m.AccOwnerAddr == g_cmcF0 && m.AccX == kAcc[0] &&
                  m.AccY == kAcc[1] && m.AccZ == kAcc[2],
              FxFmt("rc=%d has=%d vel=(%.9g, %.9g, %.9g) speed=%.9g acc=(%.9g, %.9g, %.9g)", rcM, m.HasMovement ? 1 : 0,
                    m.VelX, m.VelY, m.VelZ, m.Speed, m.AccX, m.AccY, m.AccZ));
    }
    {
        FxReset();
        FxUE4World();
        FxClearRecorders();
        uint8_t tier = kTierSentinel;
        const int32_t rc = Wirbel::RecallExplicit(kDest, true, &tier, true);
        const FxCall sal = FxNth(FxKind::SetActorLoc);
        const auto loc = FxVec(sal, kSal4.loc, kVecF);
        check("PACK.01 UE4 recall: the 0x9C-byte K2_SetActorLocation twin on BP_HeroF_C_0 carries NewLocation as 3 "
              "floats @0 and bTeleport 1 @0x98, nothing else",
              rc == Wirbel::TP_OK && tier == kTierInvoke && sal.instance == g_heroF0 && sal.ufunc == g_fnSetActorLocF &&
                  sal.size == static_cast<uint32_t>(kSal4.parmsSize) && FxEq3(loc, kDestXyz) &&
                  FxRd<uint8_t>(sal, kSal4.teleport) == 1 &&
                  FxZeroOutside(sal, { Keep(kSal4.loc, kVecF), Keep(kSal4.teleport, 1) }),
              FxFmt("rc=%d tier=%u ", rc, static_cast<unsigned>(tier)) + FxCallStr(sal) + " loc=" + FxArrStr(loc));
        check("PACK.01 UE4 recall verified at 12 bytes: no SetMovementMode, no 'didn't reach target' WARN, and the "
              "post-move line has cur == target",
              FxCount(FxKind::SetMoveMode) == 0 && FxLogPrefix(kNotReachedPrefix) == 0 &&
                  FxLogCount(FxLine::PostMove(kDestXyz, kDestXyz, false), 'I') == 1,
              FxLogDump());
        const FxCall rot = FxNth(FxKind::SetCtrlRot);
        const auto pyr = FxVec(rot, 0, kVecF);
        check("PACK.01 UE4 rotation: the 12-byte SetControlRotation twin on PCF_0 gets NewRotation as floats "
              "(5.5, -30.5, 0)",
              rot.instance == g_pcF0 && rot.ufunc == g_fnSetCtrlRotF && rot.size == static_cast<uint32_t>(kVecF) &&
                  FxEq3(pyr, kDestPyr),
              FxCallStr(rot) + " " + FxArrStr(pyr));
    }
    {
        FxReset();
        FxUE4World();
        FxClearRecorders();
        Wirbel::Pov pov{};
        const int32_t rc = Wirbel::GetPov(pov);
        check("PACK.01 UE4 POV (getters): CamF_0's 12-byte getter returns are read as floats -- (100.5, 200.5, 300.5 | "
              "-5.5, 45.5, 0), FOV 87.5, and the pawn at (1000.5, -2000.5, 300.5)",
              rc == Wirbel::TP_OK && pov.Source == 0 && FxPoseIs(pov.Cam, kCamLoc, kCamRot) && pov.Fov == kFovGetter &&
                  pov.HasPawn && FxEq3(FxXyzOf(pov.Pawn), kStart),
              FxS(rc) + " cam=" + FxPoseStr(pov.Cam) + FxFmt(" fov=%.9g", pov.Fov));
        FxResponsive(false);
        Wirbel::Pov raw{};
        const int32_t rcRaw = Wirbel::GetPov(raw);
        check("PACK.01 UE4 POV (raw): the float MinimalViewInfo is read at its own widths -- (110.5, 210.5, 310.5 | "
              "-6.5, 46.5, 0), FOV 75.5",
              rcRaw == Wirbel::TP_OK && raw.Source == 1 && FxPoseIs(raw.Cam, kRawLoc, kRawRot) && raw.Fov == kRawFov,
              FxS(rcRaw) + " cam=" + FxPoseStr(raw.Cam) + FxFmt(" fov=%.9g", raw.Fov));
    }
    {
        FxReset();
        FxUE4World();
        FxCtrlFFns(FxCtrlFnList({ .setRot = false, .ue4 = true }));
        FxClearRecorders();
        uint8_t tier = kTierSentinel;
        const int32_t rc = Wirbel::RecallExplicit(kDest, true, &tier, true);
        const auto pyr = FxCtrlRot(g_pcF0, kVecF);
        check("PACK.01 UE4 rotation fallback: with no SetControlRotation the raw write puts 3 floats (5.5, -30.5, 0) "
              "at PCF_0+0x38 and leaves the 12 bytes after them 0xA5",
              rc == Wirbel::TP_OK && FxCount(FxKind::SetCtrlRot) == 0 && FxEq3(pyr, kDestPyr) &&
                  GuardIntact(g_pcF0 + FxOff::kCtrlRotGuard),
              FxS(rc) + " " + FxArrStr(pyr));
    }
    {
        FxReset();
        FxUE4World();
        FxActorFFns(FxActorFnList({ .setActorLoc = FxSalShape::Missing, .teleportTo = false, .ue4 = true }));
        FxSceneFFns(FxSceneFnList({ .world = FxCompShape::Missing, .relative = false, .ue4 = true }));
        FxClearRecorders();
        uint8_t tier = kTierSentinel;
        const int32_t rc = Wirbel::RecallExplicit(kDest, false, &tier, true);
        check("PACK.01 UE4 tier 2: with no setter anywhere, 12 bytes land at RootF_0+0x40 -- floats (1500.5, -2500.5, "
              "350.5), the 0xA5 after them intact -- tier 2",
              rc == Wirbel::TP_OK && tier == kTierRaw && FxEq3(FxPos(g_rootF0), kDestXyz) &&
                  GuardIntact(g_rootF0 + FxOff::kRelLocGuard),
              FxFmt("rc=%d tier=%u pos=", rc, static_cast<unsigned>(tier)) + FxArrStr(FxPos(g_rootF0)));
        const auto c2w = FxVecAt(g_rootF0 + FxOff::kC2WF, kVecF);
        check("PACK.01 UE4 deep force: the float ComponentToWorld copy @0x104 is found by the float old position and "
              "rewritten -- 'I' \"deep-force rewrote 1 ...\" once, no 'found no' WARN",
              FxEq3(c2w, kDestXyz) && FxLogCount(FxLine::DeepRewrote(1, g_rootF0), 'I') == 1 &&
                  FxLogCount(FxText::kDeepNone) == 0,
              FxArrStr(c2w) + FxLogDump());
    }

    // ── PACK.02 ──
    {
        FxReset();
        FxRetByte(FxKind::MousePos, 0);
        FxHit(FxKind::HitScreen, kChanZero);
        FxClearRecorders();
        const CursorRun r = Cursor(kChanZero, true, false);
        const FxCall vp = FxNth(FxKind::Viewport);
        const FxCall hs = FxNth(FxKind::HitScreen);
        check("PACK.02 a parameter ending at the buffer end: SizeY @4 of the 8-byte GetViewportSize is read -- the "
              "centre's Y is 540.5",
              r.rc == Wirbel::TP_OK && r.usedCenter && vp.size == static_cast<uint32_t>(FxVp::kParmsSize) &&
                  ScreenIs(hs, kHs5, kCoordD, kCenter),
              RunStr(r) + " " + FxCallStr(vp) + " " + ScreenStr(hs, kHs5, kCoordD));
    }

    // ── PACK.03: every call's buffer, over one scenario per kind of op ──
    {
        struct Sized { const char* name; std::function<void()> arm; std::function<int32_t()> op; };
        const std::vector<Sized> scenarios = {
            { "a recall with rotation and an undo point", [] {},
              [] {
                  uint8_t t = kTierSentinel;
                  return Wirbel::RecallExplicit(kDest, true, &t, false);
              } },
            { "a recall the movement component reverts (the CMC freeze)",
              [] {
                  FxMoveMode(FxKind::SetActorLoc, FxMove::Lies);
                  FxMoveMode(FxKind::SetWorldLoc, FxMove::Lies);
              },
              [] {
                  uint8_t t = kTierSentinel;
                  return Wirbel::RecallExplicit(kDest, false, &t, true);
              } },
            { "a cursor jump through path 1, path 2 and the line trace", [] { FxHit(FxKind::Trace, kChan); },
              [] { return Cursor(kChan).rc; } },
            { "a cursor jump from the screen centre",
              [] {
                  FxRetByte(FxKind::MousePos, 0);
                  FxHit(FxKind::HitScreen, kChan);
              },
              [] { return Cursor(kChan).rc; } },
            { "a camera POV read", [] {},
              [] {
                  Wirbel::Pov p{};
                  return Wirbel::GetPov(p);
              } },
            { "a mouse-cursor show", [] {},
              [] {
                  bool s = false;
                  return Wirbel::SetMouseCursor(true, &s);
              } },
            { "a relative move", [] {},
              [] {
                  Wirbel::Pose p{};
                  uint8_t t = kTierSentinel;
                  bool known = false;
                  return Wirbel::TeleportRelative(kStep, true, p, &t, &known);
              } },
            { "an attached pose read", [] { FxAttach(true); },
              [] {
                  Wirbel::Pose p{};
                  return Wirbel::GetPose(p, nullptr, 0, nullptr);
              } },
            { "a UE4 recall", [] { FxUE4World(); },
              [] {
                  uint8_t t = kTierSentinel;
                  return Wirbel::RecallExplicit(kDest, true, &t, true);
              } },
        };
        bool sawStop = false, sawStopNow = false, sawSetMode = false, parameterlessOk = true;
        std::string parameterlessWhy;
        for (const auto& s : scenarios) {
            FxReset();
            s.arm();
            FxClearRecorders();
            const int32_t rc = s.op();
            const auto calls = FxCalls();
            std::string why;
            bool ok = rc == Wirbel::TP_OK && !calls.empty();
            for (const auto& c : calls) {
                if (ok && !SizeOk(c, why)) ok = false;
                const bool parameterless =
                    c.kind == FxKind::StopMove || c.kind == FxKind::StopMoveNow || c.kind == FxKind::SetMoveMode;
                if (!parameterless) continue;
                sawStop = sawStop || c.kind == FxKind::StopMove;
                sawStopNow = sawStopNow || c.kind == FxKind::StopMoveNow;
                sawSetMode = sawSetMode || c.kind == FxKind::SetMoveMode;
                if (c.size < 1 || c.params.size() != static_cast<size_t>(c.size)) {
                    parameterlessOk = false;
                    parameterlessWhy += " " + FxCallStr(c);
                }
            }
            check(FxFmt("PACK.03 sizes, %s: every call is handed ParamBufferSize of its function, at least 1 byte, whole",
                        s.name)
                      .c_str(),
                  ok, FxFmt("rc=%d ", rc) + why + " " + FxCallsStr(calls));
        }
        check("PACK.03 parameterless calls: StopMovement, StopMovementImmediately and SetMovementMode were each made, "
              "every one with a real buffer of at least 1 byte",
              sawStop && sawStopNow && sawSetMode && parameterlessOk,
              FxFmt("stop=%d stopNow=%d setMode=%d", sawStop ? 1 : 0, sawStopNow ? 1 : 0, sawSetMode ? 1 : 0) +
                  parameterlessWhy);
    }

    // ── PACK.04: the function walk ──
    {
        FxReset();
        FunctionInfo fi;
        const bool found = FindFunc(g_cHero, kSalName, fi);
        check("PACK.04 FindFunc walks the supers: K2_SetActorLocation, declared on Actor, is found from BP_Hero_C as "
              "Actor's blob",
              found && fi.address == g_fnSetActorLoc, FxHex(fi.address));
        FunctionInfo none;
        check("PACK.04 FindFunc: a name no class in the chain declares is not found",
              !FindFunc(g_cHero, "K2_NoSuchFunction", none));
    }
    {
        FxReset();
        const uintptr_t head = FxActorFnList({ .setActorLoc = FxSalShape::Lowercase });
        FxActorFns(head);
        const uintptr_t lower = FxFnIn(head, "k2_setactorlocation");
        FunctionInfo fi;
        const bool found = FindFunc(g_cHero, kSalName, fi);
        FxClearRecorders();
        uint8_t tier = kTierSentinel;
        const int32_t rc = Wirbel::RecallExplicit(kDest, false, &tier, true);
        check("PACK.04 any case: \"K2_SetActorLocation\" finds the function reflected as \"k2_setactorlocation\", and "
              "the recall invokes it rather than K2_TeleportTo",
              lower != 0 && found && fi.address == lower && rc == Wirbel::TP_OK && FxCount(FxKind::SetActorLoc) == 1 &&
                  FxNth(FxKind::SetActorLoc).ufunc == lower && FxCount(FxKind::TeleportTo) == 0,
              FxFmt("found=%d rc=%d ", found ? 1 : 0, rc) + FxCallsStr(FxCalls()));
    }
    {
        struct SuperCase { uintptr_t cls; const char* name; const char* label; };
        const SuperCase cases[] = {
            { g_cPcSelfSuper, "PcSelfSuper",
              "PACK.04 a class that is its own super: the walk stops -- the cursor teleport finds GetMousePosition and "
              "nothing past it, and answers -4 naming PcSelfSuper" },
            { g_cPcBadSuper, "PcBadSuper",
              "PACK.04 an unreadable super: the walk stops at it -- GetMousePosition and nothing past it, -4 naming "
              "PcBadSuper" },
        };
        for (const auto& s : cases) {
            FxReset();
            FxSetClass(g_pc0, s.cls);
            FxClearRecorders();
            const CursorRun r = Cursor(kChan);
            check(s.label,
                  r.rc == Wirbel::TP_ERR_REFLECTION && FxKindsAre({ FxKind::MousePos }) &&
                      FxLogCount(FxFmt(kDeprojMissingFmt, s.name), 'W') == 1,
                  RunStr(r) + " " + FxKindsStr(FxKinds()) + FxLogDump());
        }
    }

    // ── PACK.05: ReturnedTrue ──
    {
        FxReset();
        FxMoveMode(FxKind::SetActorLoc, FxMove::RetFalse);
        FxClearRecorders();
        uint8_t tier = kTierSentinel;
        const int32_t rc = Wirbel::RecallExplicit(kDest, false, &tier, true);
        const FxCall sal = FxNth(FxKind::SetActorLoc);
        const FxCall swl = FxNth(FxKind::SetWorldLoc);
        check("PACK.05 ReturnValue false: K2_SetActorLocation answering false is not a move -- its 'W' \"returned false "
              "(r=0, parmsSize=272)\", then K2_SetWorldLocation moves the root; tier 1",
              rc == Wirbel::TP_OK && tier == kTierInvoke &&
                  FxLogCount(FxFmt(kSetterFailFmt, kSalName, "returned false", 0,
                                   static_cast<unsigned>(kSal5.parmsSize)),
                             'W') == 1 &&
                  FxCount(FxKind::SetWorldLoc) == 1 && swl.seq > sal.seq && FxEq3(FxPos(), kDestXyz),
              FxFmt("rc=%d tier=%u ", rc, static_cast<unsigned>(tier)) + FxCallsStr(FxCalls()) + FxLogDump());
    }
    {
        FxReset();
        FxRcAlways(FxKind::SetActorLoc, kRcQueued);
        FxClearRecorders();
        uint8_t tier = kTierSentinel;
        const int32_t rc = Wirbel::RecallExplicit(kDest, false, &tier, true);
        check("PACK.05 no return value: K2_SetWorldLocation, which returns nothing, counts as done -- its 'I' line, no "
              "K2_SetRelativeLocation, tier 1, the root on kDest",
              rc == Wirbel::TP_OK && tier == kTierInvoke &&
                  FxLogCount(FxLine::CompSetterOk(kSwlName, g_root0, kDestXyz), 'I') == 1 &&
                  FxCount(FxKind::SetRelLoc) == 0 && FxEq3(FxPos(), kDestXyz),
              FxFmt("rc=%d tier=%u ", rc, static_cast<unsigned>(tier)) + FxCallsStr(FxCalls()) + FxLogDump());
    }
}

// ══ LOCK ═════════════════════════════════════════════════════════════════════════════════════════════════════════
// A: a RecallExplicit held inside its first K2_SetActorLocation, the op lock in hand, until `release`. The hook spins
// on the caller's thread, so the lock stays held exactly as long as the check needs.
static void HoldInSetActorLocation(std::atomic<bool>& inHook, std::atomic<bool>& release) {
    FxOnCall([&inHook, &release](FxKind k, uintptr_t, int nth) {
        if (k != FxKind::SetActorLoc || nth != 0) return;
        inHook.store(true);
        while (!release.load()) Sleep(kPollMs);
    });
}

static void BlockLock() {
    blk("LOCK - the op lock: held over every invoke of every export, serialising two callers; the map name lock-free");

    // ── LOCK.01 ──
    {
        FxReset();
        bool seesHeld = false;
        {
            std::lock_guard<std::mutex> lk(s_opMutex);
            seesHeld = !FxFreeElsewhere(s_opMutex);
        }
        const bool seesFree = FxFreeElsewhere(s_opMutex);
        check("LOCK.01 the probe: another thread cannot take s_opMutex while this one holds it, and can once it is let "
              "go -- so 'held' below is measured",
              seesHeld && seesFree);
    }
    {
        struct Probed { const char* name; std::function<void()> arm; std::function<int32_t()> op; };
        const std::vector<Probed> ops = {
            { "RecallMarker (accepted)", [] { FxPlantMarker(0, FxMarker(kDest, kMapA)); },
              [] {
                  uint8_t t = kTierSentinel;
                  return Wirbel::RecallMarker(0, false, &t);
              } },
            { "RecallExplicit", [] {},
              [] {
                  uint8_t t = kTierSentinel;
                  return Wirbel::RecallExplicit(kDest, true, &t, false);
              } },
            { "TeleportToCursor", [] { FxHit(FxKind::HitCursor, kChanZero); }, [] { return Cursor(kChanZero).rc; } },
            { "TeleportRelative", [] {},
              [] {
                  Wirbel::Pose p{};
                  uint8_t t = kTierSentinel;
                  bool known = false;
                  return Wirbel::TeleportRelative(kStep, true, p, &t, &known);
              } },
            { "SetMouseCursor", [] {},
              [] {
                  bool s = false;
                  return Wirbel::SetMouseCursor(true, &s);
              } },
            { "GetPov", [] {},
              [] {
                  Wirbel::Pov p{};
                  return Wirbel::GetPov(p);
              } },
            { "GetPose (attached)", [] { FxAttach(true); },
              [] {
                  Wirbel::Pose p{};
                  return Wirbel::GetPose(p, nullptr, 0, nullptr);
              } },
            // The CE mailbox's hotkeys and the reads that share the undo and BugIt slots with them.
            { "BugItGo (accepted)", [] { FxPlantBugIt(FxMarker(kDest, kMapA)); },
              [] {
                  uint8_t t = kTierSentinel;
                  return Wirbel::BugItGo(&t);
              } },
            { "RecallLast", [] { FxPlantLast(FxMarker(kPlanted, kMapB)); },
              [] {
                  uint8_t t = kTierSentinel;
                  return Wirbel::RecallLast(&t);
              } },
            { "GetPoseAndMovement (attached)", [] { FxAttach(true); },
              [] {
                  Wirbel::Pose p{};
                  Wirbel::MovementState m{};
                  return Wirbel::GetPoseAndMovement(p, nullptr, 0, nullptr, m);
              } },
            { "SaveMarker (attached)", [] { FxAttach(true); }, [] { return Wirbel::SaveMarker(0); } },
            { "BugItSave (attached)", [] { FxAttach(true); },
              [] {
                  Wirbel::Pose p{};
                  char map[kMapCap];
                  uint8_t src = 0;
                  bool pr = false;
                  return Wirbel::BugItSave(p, map, kMapCap, &src, &pr);
              } },
        };
        for (const auto& o : ops) {
            FxReset();
            o.arm();
            FxClearRecorders();
            FxProbe(true);
            const int32_t rc = o.op();
            const int probed = g_invProbed.load(), held = g_invHeld.load();
            FxProbe(false);
            const auto calls = FxCalls();
            const bool everyHeld = Every(calls, [](const FxCall& c) { return c.opHeld; });
            check(FxFmt("LOCK.01 op lock held: %s succeeds, and every invoke it makes finds s_opMutex held", o.name)
                      .c_str(),
                  rc == Wirbel::TP_OK && probed > 0 && held == probed && static_cast<int>(calls.size()) == probed &&
                      everyHeld,
                  FxFmt("rc=%d probed=%d held=%d ", rc, probed, held) + FxCallsStr(calls));
        }
    }

    // ── LOCK.02 / LOCK.03: A held inside its move; B a pose read, C the map name ──
    {
        FxReset();
        std::atomic<bool> inHook{ false }, release{ false };
        HoldInSetActorLocation(inHook, release);
        FxClearRecorders();
        std::atomic<bool> doneA{ false }, doneB{ false }, doneC{ false }, mapOk{ false };
        std::atomic<int32_t> rcA{ kNotReturned }, rcB{ kNotReturned };
        uint8_t tierA = kTierSentinel;
        Wirbel::Pose poseB = FxSentinelPose();
        char mapB[kMapCap] = {};
        char mapC[kMapCap] = {};
        std::thread a([&] {
            rcA.store(Wirbel::RecallExplicit(kDest, true, &tierA, true));
            doneA.store(true);
        });
        const bool blocked = waitFor([&] { return inHook.load(); }, kJoinWaitMs);
        std::thread b([&] {
            rcB.store(Wirbel::GetPose(poseB, mapB, kMapCap, nullptr));
            doneB.store(true);
        });
        std::thread c([&] {
            mapOk.store(Wirbel::GetCurrentMapName(mapC, kMapCap));
            doneC.store(true);
        });
        const bool cInTime = waitFor([&] { return doneC.load(); }, kHoldMs);
        const bool bEarly = waitFor([&] { return doneB.load(); }, kHoldMs);
        release.store(true);
        if (!waitFor([&] { return doneA.load() && doneB.load() && doneC.load(); }, kJoinWaitMs))
            FxFixtureFail("LOCK.02: a thread never returned after the hook let A go");
        a.join();
        b.join();
        c.join();
        FxOnCall(nullptr);

        check("LOCK.02 setup: A (RecallExplicit) is held inside its K2_SetActorLocation call", blocked);
        check("LOCK.03 lock-free map name: while A holds the op lock, GetCurrentMapName on another thread returns true "
              "\"Map_A\" within 200 ms",
              cInTime && mapOk.load() && BufIs(mapC, sizeof mapC, kMapA), BufStr(mapC, sizeof mapC));
        check("LOCK.02 serialised: while A holds the op lock, GetPose on thread B does not return within 200 ms",
              blocked && !bEarly);
        check("LOCK.02 serialised: released, A answers 0 and B answers 0 with A's whole recall in its pose -- kDest, "
              "and the yaw 329.5 A's SetControlRotation wrote after the hook",
              rcA.load() == Wirbel::TP_OK && rcB.load() == Wirbel::TP_OK && FxPoseIs(poseB, kDestXyz, kDestPyrStored),
              FxFmt("rcA=%d rcB=%d ", rcA.load(), rcB.load()) + FxPoseStr(poseB));
        const auto calls = FxCalls();
        std::vector<uint64_t> chainSeqs;
        for (const auto& l : FxTeleportLog())
            if (l.text == FxLine::Chain(g_pc0)) chainSeqs.push_back(l.seq);
        const bool ordered = !calls.empty() && chainSeqs.size() == 2 && chainSeqs[0] < calls.front().seq &&
                             chainSeqs[1] > calls.back().seq;
        check("LOCK.02 serialised: B's chain line is stamped after A's last call -- two chain lines, A's before its "
              "first call",
              ordered, FxFmt("chain lines=%zu ", chainSeqs.size()) + FxCallsStr(calls) + FxLogDump());
    }
    {
        // The slot reads and the cursor and trainer reads wait on the same lock: each one started while A is held must
        // still be waiting 200 ms later.
        FxReset();
        const Wirbel::Marker m1 = FxMarker(FxPose(kOther), kMapA);
        const Wirbel::Marker m2 = FxMarker(FxPose(kSpecPos), kMapA);
        const Wirbel::Marker planted = FxMarker(kPlanted, kMapB);
        FxPlantMarker(1, m1);
        FxPlantMarker(2, m2);
        FxPlantLast(planted);
        std::atomic<bool> inHook{ false }, release{ false };
        HoldInSetActorLocation(inHook, release);
        FxClearRecorders();
        std::atomic<bool> doneA{ false };
        std::atomic<int32_t> rcA{ kNotReturned };
        uint8_t tierA = kTierSentinel;
        std::thread a([&] {
            rcA.store(Wirbel::RecallExplicit(kDest, true, &tierA, true));
            doneA.store(true);
        });
        const bool blocked = waitFor([&] { return inHook.load(); }, kJoinWaitMs);

        Wirbel::Marker gotLast{}, gotMarker{};
        bool cursorState = true;
        Wirbel::TrainerOffsets trainer{};
        struct Waiter {
            const char* name;
            std::function<int32_t()> run;
            std::atomic<bool> done{ false };
            std::atomic<int32_t> rc{ kNotReturned };
        };
        Waiter waiters[] = {
            { "GetLast", [&] { return Wirbel::GetLast(gotLast); } },
            { "GetMarker(1)", [&] { return Wirbel::GetMarker(1, gotMarker); } },
            { "ClearMarker(2)", [] { return Wirbel::ClearMarker(2); } },
            { "GetMouseCursor", [&] { return Wirbel::GetMouseCursor(&cursorState); } },
            { "GetTrainerOffsets", [&] { return Wirbel::GetTrainerOffsets(trainer); } },
        };
        std::vector<std::thread> threads;
        for (auto& w : waiters)
            threads.emplace_back([&w] {
                w.rc.store(w.run());
                w.done.store(true);
            });
        const auto anyDone = [&waiters] {
            for (const auto& w : waiters)
                if (w.done.load()) return true;
            return false;
        };
        std::ignore = waitFor(anyDone, kHoldMs);
        std::vector<bool> early;
        for (const auto& w : waiters) early.push_back(w.done.load());
        release.store(true);
        const auto allDone = [&] {
            if (!doneA.load()) return false;
            for (const auto& w : waiters)
                if (!w.done.load()) return false;
            return true;
        };
        if (!waitFor(allDone, kJoinWaitMs)) FxFixtureFail("LOCK.02: a thread never returned after the hook let A go");
        a.join();
        for (auto& t : threads) t.join();
        FxOnCall(nullptr);

        check("LOCK.02 setup: A (RecallExplicit) is held inside its K2_SetActorLocation call, and answers 0 once let go",
              blocked && rcA.load() == Wirbel::TP_OK, FxS(rcA.load()));
        for (size_t i = 0; i < std::size(waiters); ++i)
            check(FxFmt("LOCK.02 serialised: %s on another thread does not return within 200 ms while A holds the op "
                        "lock",
                        waiters[i].name)
                      .c_str(),
                  blocked && !early[i]);
        check("LOCK.02 after the release: GetLast answers 0 with the planted last slot",
              waiters[0].rc.load() == Wirbel::TP_OK && FxMarkerEq(gotLast, planted), FxMarkerStr(gotLast));
        check("LOCK.02 after the release: GetMarker(1) answers 0 with slot 1's marker",
              waiters[1].rc.load() == Wirbel::TP_OK && FxMarkerEq(gotMarker, m1), FxMarkerStr(gotMarker));
        check("LOCK.02 after the release: ClearMarker(2) answers 0 and empties slot 2 only",
              waiters[2].rc.load() == Wirbel::TP_OK && FxMarkerEq(FxMarkerNow(2), Wirbel::Marker{}) &&
                  FxMarkerEq(FxMarkerNow(1), m1),
              FxMarkerStr(FxMarkerNow(2)));
        check("LOCK.02 after the release: GetMouseCursor answers 0 with the bit clear",
              waiters[3].rc.load() == Wirbel::TP_OK && !cursorState, FxS(waiters[3].rc.load()));
        check("LOCK.02 after the release: GetTrainerOffsets answers 0 with the five-hop chain",
              waiters[4].rc.load() == Wirbel::TP_OK && trainer.ChainCount == kTrainerHops,
              FxFmt("rc=%d hops=%d", waiters[4].rc.load(), trainer.ChainCount));
    }
}

}  // namespace G5

static void Blocks_G5() {
    G5::BlockCur();
    G5::BlockMap();
    G5::BlockPack();
    G5::BlockLock();
}

int main() {
    setvbuf(stdout, nullptr, _IONBF, 0);
    printf("wirbel_teleport_test -- Teleport (Wirbel.cpp) over a fake world and a fake engine, in this process\n");
    FxAsanPreamble();
    FxBuildWorld();
    Block_LastAtStart();
    Blocks_G1();
    Blocks_G2();
    Blocks_G3();
    Blocks_G4();
    Blocks_G5();
    FxCleanup();
    return FxExitCode();
}
