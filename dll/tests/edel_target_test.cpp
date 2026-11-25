// edel_target_test.cpp
// UE5CEDumper — CurrentTarget (Edel.cpp) over a fake world.
//
// WHAT IT DRIVES
//   Edel::DetectCurrentTarget end to end -- the GWorld chain and its instance-scan fallback, the debug-camera hop, the
//   pawn read, the self-exclusion, the scoring of every object the controller, the pawn and their owned components
//   point at, the sort, the cap and the auto-pick -- and, through the #include, the pieces it is built from: the
//   super-chain classifier, the ownership test, the keyword tables and the two scan budgets.
//
// WHAT IS FAKED
//   * The world: GWorld -> OwningGameInstance -> LocalPlayers[0] -> PlayerController -> Pawn, with a decoy ahead of
//     every by-name lookup a fuzzy or a first-match rule would answer wrongly; one controller and pawn per scenario,
//     with the components they own and some they only point at; a UE4 (UProperty) twin of the chain; and an object
//     array that puts a template, a widget, an idle debug camera and two lesser controllers ahead of the local one.
//     Reflection data built in this process and walked by the real Aura, Ubel and Serie (ue5_testcore, linked).
//   * Memory that refuses: a reserved page, and a second one whose first touch raises Tot's per-command cancel from a
//     vectored exception handler, so a cancel lands in the middle of a run.
//   * Sein, which records every line with its level, its category and a stamp.
//
// WHY IT #includes THE .cpp
//   ClassifyBySuperChain, OuterChainsBackTo, the keyword tables and both budgets have internal linkage. Edel keeps no
//   state, so there is nothing else to isolate. UE5_CallProcessEventEx, Stark::IsGameThreadResponsive and
//   UE5_FindInstanceOfClass are left undefined: Edel invokes nothing, takes no liveness gate and makes no user-facing
//   lookup, so an edit that adds one fails the link.
//
// WHY AN EXECUTABLE OF ITS OWN
//   Edel's anonymous-namespace helpers share their names with Solitar.cpp's, which dll_core_test compiles into its one
//   translation unit, and with Wirbel.cpp's, which wirbel_teleport_test #includes.

#include "../src/Edel.cpp"  // NOLINT -- see "WHY IT #includes THE .cpp" above

// Edel.cpp #defines LOG_CAT before Sein.h and never undefines it, so this is the category its own lines carry. The
// readers below compare against the literal "WALK" instead (kEdelCat), so a changed define fails FIX.08 rather than
// moving the expectation along with it.
static constexpr const char* kWalkCat = LOG_CAT;

#include "../src/Serie.h"   // the fixture's name pool
#include "../src/Tot.h"     // the cancel flags Edel and the instance scan poll

#include <windows.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>   // getenv_s: the AddressSanitizer build's negative control; exit on a fixture failure
#include <io.h>      // _dup / _dup2 / _close: FIX.08 sends FxExitCode's own printout to NUL while it probes the verdict
#include <algorithm>
#include <array>
#include <atomic>
#include <climits>
#include <cstring>
#include <functional>
#include <initializer_list>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

// Edel's notes carry em dashes and are compared byte for byte: a source not read as UTF-8 would compare its own
// mojibake against Edel's.
constexpr bool FxIsEmDash(const char* s) { return s[0] == '\xE2' && s[1] == '\x80' && s[2] == '\x94' && s[3] == '\0'; }
static_assert(FxIsEmDash("—"), "compile with /utf-8: the em dash must be U+2014, E2 80 94");
constexpr const char kEmDash[] = "\xE2\x80\x94";

// ══ the one clock every record is stamped with ══════════════════════════════════════════════════════════════════
// Lines written on different threads are ordered by this stamp, never by where they landed in a vector.
static std::atomic<uint64_t> g_seq{0};
static uint64_t FxStamp() { return g_seq.fetch_add(1) + 1; }

// ══ Sein: every log line recorded ═══════════════════════════════════════════════════════════════════════════════
struct FxLogLine {
    uint64_t seq = 0;
    char lvl = 0;          // 'I' 'W' 'E' 'D'
    std::string cat;       // the writer's LOG_CAT
    std::string text;
};
static std::mutex g_logMu;
static std::vector<FxLogLine> g_log;
// A test's hook into each line, called on the writer's thread after the line is recorded, with no recorder lock held.
static std::mutex g_logHookMu;
static std::function<void(const FxLogLine&)> g_onLog;
constexpr size_t kLogLineBytes = 8192;

// Edel's own lines: category WALK and the "Edel:" prefix, because Ubel writes WALK lines too ("WalkClass: ...").
constexpr const char* kEdelCat = "WALK";
constexpr const char* kEdelPrefix = "Edel:";
// LOG.07 asks about the whole run and a scenario clears g_log, so these two counters are never cleared.
static std::atomic<int> g_edelLinesRun{0}, g_edelErrorOrDebugRun{0};

static bool FxStartsWith(const std::string& s, const char* prefix) { return s.compare(0, strlen(prefix), prefix) == 0; }

static void FxLogV(char lvl, const char* cat, const char* fmt, va_list ap) {
    char b[kLogLineBytes];
    vsnprintf(b, sizeof b, fmt, ap);
    FxLogLine line;
    line.seq = FxStamp();
    line.lvl = lvl;
    line.cat = cat ? cat : "";
    line.text = b;
    if (line.cat == kEdelCat && FxStartsWith(line.text, kEdelPrefix)) {
        ++g_edelLinesRun;
        if (lvl == 'E' || lvl == 'D') ++g_edelErrorOrDebugRun;
    }
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
void Info(const char* cat, const char* fmt, ...) {
    va_list ap;
    va_start(ap, fmt);
    FxLogV('I', cat, fmt, ap);
    va_end(ap);
}
void Error(const char* cat, const char* fmt, ...) {
    va_list ap;
    va_start(ap, fmt);
    FxLogV('E', cat, fmt, ap);
    va_end(ap);
}
void Warn(const char* cat, const char* fmt, ...) {
    va_list ap;
    va_start(ap, fmt);
    FxLogV('W', cat, fmt, ap);
    va_end(ap);
}
void Debug(const char* cat, const char* fmt, ...) {
    va_list ap;
    va_start(ap, fmt);
    FxLogV('D', cat, fmt, ap);
    va_end(ap);
}
void Summary(const char*, ...) {}
}  // namespace Sein

// ══ Stark, Frieren's globals ════════════════════════════════════════════════════════════════════════════════════
// Genau.obj, which Aura pulls in, names this one; Edel never does.
namespace Stark {
void SetInvokeTimeoutMs(int32_t) {}
}  // namespace Stark

constexpr uint32_t kFixtureUEVersion = 504;    // UE5: the instance scan applies UE5's destroyed-object rule
uint32_t g_cachedUEVersion = kFixtureUEVersion;
uintptr_t g_cachedGWorld = 0;
std::atomic<bool> g_initInProgress{false};

// ══ harness ═════════════════════════════════════════════════════════════════════════════════════════════════════
static std::atomic<int> g_pass{0}, g_fail{0};
static void check(const char* label, bool cond, const char* got = nullptr) {
    if (cond) { ++g_pass; }
    else { ++g_fail; printf("  FAIL  %s%s%s\n", label, got ? "   got: " : "", got ? got : ""); }
}
static void check(const char* label, bool cond, const std::string& got) { check(label, cond, got.c_str()); }
static void check(const std::string& label, bool cond, const std::string& got = std::string()) {
    check(label.c_str(), cond, got.empty() ? nullptr : got.c_str());
}
static void blk(const char* name) { printf("- %s\n", name); }

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

// ══ the fixture's numbers ═══════════════════════════════════════════════════════════════════════════════════════
constexpr int32_t kPtrBytes = static_cast<int32_t>(sizeof(uintptr_t));
constexpr int32_t kTArrayBytes = 0x10;         // { Data, Num, Max }
constexpr int kTArrayNumAt = 0x08, kTArrayMaxAt = 0x0C;
constexpr int32_t kWeakBytes = 8;              // FWeakObjectPtr { ObjectIndex, SerialNumber }
constexpr int kWeakSerialAt = 4;
constexpr int32_t kIntBytes = 4;
constexpr int32_t kUObjectHeaderBytes = 0x28;  // vtable, flags, InternalIndex, class, name, outer
// Every class keeps its super here for the whole run -- the UE4 shape without FStructBaseChain, not the 0x40 default
// -- so a walk that took a fixed offset would read the zero at 0x40 (FIX.02, CLASS.08).
constexpr int kStructSuperAt = 0x30;
constexpr int32_t kIndexBase = 0x100;          // a non-pooled object's InternalIndex: this plus its creation order
// A readable negative InternalIndex: earns no +5, and Edel reports it as it is. -7 rather than -1 tells a `> 0` test
// and a `!= -1` test apart from the real `>= 0`.
constexpr int32_t kUntrackedIndex = -7;
constexpr int32_t kTcFIndex = 4;               // PICK.07's component: a tracked one, so it scores +5
constexpr int32_t kDefaultMaxCandidates = 8;   // Edel.h's documented default
// The bounds Edel promises, written here rather than taken from Edel.cpp: a fixture sized from Edel's own constants
// would follow a changed bound and still pass.
constexpr int kEdgeCap = 1024;                 // edges collected per root and per component
constexpr int kComponentBudget = 16;           // owned components scanned per root
constexpr int kSuperChainNodes = 64;           // class nodes ClassifyBySuperChain visits
constexpr int kSelfEdges = 2;                  // a scenario controller's Pawn and AcknowledgedPawn, both emitted

namespace FxOff {
// Engine fields. Every field Edel or the instance scan reads BY NAME has an offset no other named read uses.
constexpr int32_t kRootComponent = 0x60;            // Actor (an edge only)
constexpr int32_t kOwningGameInstance = 0x70;       // World
constexpr int32_t kCachedGameInstance = 0x78;       // WorldShadow: the fuzzy decoy, an ObjectProperty
constexpr int32_t kShadowGameInstance = 0x80;       // WorldShadow.OwningGameInstance
constexpr int32_t kSubsystems = 0x88;               // WorldFuzzy.GameInstanceSubsystems: an ArrayProperty decoy
constexpr int32_t kMyGameInstance = 0x98;           // WorldFuzzy
constexpr int32_t kLocalPlayers = 0xA0;             // GameInstance
constexpr int32_t kLocalPlayersCount = 0xB0;        // GameInstanceFuzzy: an IntProperty decoy holding 1
constexpr int32_t kLocalPlayersArray = 0xB8;        // GameInstanceFuzzy
constexpr int32_t kPlayerController = 0xC8;         // LocalPlayer
constexpr int32_t kPlayerControllerClass = 0xD0;    // LocalPlayerFuzzy: a ClassProperty decoy
constexpr int32_t kPlayerControllerRef = 0xD8;      // LocalPlayerFuzzy
constexpr int32_t kPawn = 0xE0;                     // Controller
constexpr int32_t kAckPawn = 0xE8;                  // PlayerController
constexpr int32_t kPlayer = 0xF0;                   // PlayerController
constexpr int32_t kCameraManager = 0xF8;            // PlayerController
constexpr int32_t kOriginalController = 0x100;      // DebugCameraController.OriginalControllerRef
constexpr int32_t kOriginalControllerClass = 0x110; // BP_DccFuzzy_C: a ClassProperty decoy
constexpr int32_t kOriginalControllerPtr = 0x118;   // BP_DccFuzzy_C
constexpr int32_t kPawnClassOverride = 0x120;       // BP_PcAckOnly_C: a ClassProperty holding class BP_Enemy_C
constexpr int32_t kAckOnlyAckPawn = 0x128;          // BP_PcAckOnly_C.AcknowledgedPawn
constexpr int32_t kPawnController = 0x130;          // Pawn.Controller (an edge only)
constexpr int32_t kCharacterMovement = 0x138;       // Character (an edge only)
constexpr int32_t kMesh = 0x140;                    // Character (an edge only)
constexpr int32_t kOwningGameInstanceU = 0x148;     // WorldU
constexpr int32_t kLocalPlayersU = 0x150;           // GameInstanceU
constexpr int32_t kPlayerControllerU = 0x160;       // LocalPlayerU
constexpr int32_t kPawnU = 0x168;                   // PlayerControllerU
constexpr int32_t kAckPawnU = 0x170;                // PlayerControllerU
constexpr int32_t kPlayerU = 0x178;                 // PlayerControllerU
constexpr int32_t kHudOriginalController = 0x1E0;   // DebugCameraControllerHUD_C.OriginalControllerRef
constexpr int32_t kWidgetPlayer = 0x1E8;            // WBP_PlayerControllerHUD_C.Player
constexpr int32_t kExclAckPawn = 0x1F8;             // PcExcl.AcknowledgedPawn: not the 0xE8 a debug camera inherits

// Scenario and target fields: from kSlot0, one pointer apart, in the order each class lists them.
constexpr int32_t kSlot0 = 0x200;
constexpr int32_t Slot(int i) { return kSlot0 + i * kPtrBytes; }
// Where that order does not hold.
constexpr int32_t kLockOnInfoBytes = 0x10;          // ScriptStruct LockOnInfo
constexpr int32_t kLockOnInfoTarget = 0x08;         // LockOnInfo.CurrentTarget, within the struct
constexpr int32_t kLockOnTarget = kSlot0 + kLockOnInfoTarget;   // PawnCand: LockOn.CurrentTarget, absolute
constexpr int32_t kLockedOnTarget = 0x210;          // PawnCand: a WeakObjectProperty
constexpr int32_t kGhost = 0x218;                   // PawnCand
constexpr int32_t kOrphan = 0x220;                  // PawnCand
// PcDedup: a TArray at 0x248 spans 0x248..0x257, so Wingman sits after it -- the array keeps the LOWER offset, which
// is what makes ORDER.01d about emission order and not offset order.
constexpr int32_t kDedupSquad = 0x248;
constexpr int32_t kDedupWingman = 0x258;
constexpr int32_t kDedupCompD1 = 0x260;
constexpr int32_t kCancel4PawnComp = 0x208;         // PawnCancel4.PawnComp
constexpr int32_t kHeroUEnemies = 0x208;            // BP_HeroU_C: a UArrayProperty
constexpr int32_t kHeroULocked = 0x218;             // BP_HeroU_C: a UWeakObjectProperty
}  // namespace FxOff

constexpr int kKwNegCount = 23;
static_assert(FxOff::Slot(kKwNegCount - 1) == 0x2B0, "PcKwNeg's ViewTarget follows its 22 negatives");

// ══ the fixture: memory ═════════════════════════════════════════════════════════════════════════════════════════
constexpr size_t kObjBytes = 0x200;        // a UObject whose fields all lie below the scenario slots
constexpr size_t kBigBytes = 0x800;        // one with fields at kSlot0 or above
static_assert(static_cast<size_t>(FxOff::Slot(kKwNegCount - 1) + kPtrBytes) <= kBigBytes,
              "the furthest scenario field lies inside a big object");
constexpr size_t kFieldBytes = 0x100;      // one FField: clear of the FProperty subclass slot
constexpr size_t kFieldClassBytes = 0x20;  // one FFieldClass: only its name is read
constexpr int kMaxObjs = 1024, kMaxBig = 192, kMaxFields = 512, kMaxFieldClasses = 16;
alignas(16) static uint8_t g_objArena[kMaxObjs][kObjBytes];
alignas(16) static uint8_t g_bigArena[kMaxBig][kBigBytes];
alignas(16) static uint8_t g_fieldArena[kMaxFields][kFieldBytes];
alignas(16) static uint8_t g_fclassArena[kMaxFieldClasses][kFieldClassBytes];
static int g_objUsed = 0, g_bigUsed = 0, g_fieldUsed = 0, g_fclassUsed = 0;
static std::map<std::string, uintptr_t> g_fclassByName;

static uint8_t* FxBytes(uintptr_t a) { return reinterpret_cast<uint8_t*>(a); }
static uintptr_t FxAddr(const void* p) { return reinterpret_cast<uintptr_t>(p); }
static void FxPut64(uintptr_t at, int off, uint64_t v) { memcpy(FxBytes(at) + off, &v, sizeof v); }
static void FxPut32(uintptr_t at, int off, uint32_t v) { memcpy(FxBytes(at) + off, &v, sizeof v); }
static void FxSetPtr(uintptr_t obj, int off, uintptr_t v) { FxPut64(obj, off, v); }
static uintptr_t FxGetPtr(uintptr_t obj, int off) {
    uintptr_t v = 0;
    memcpy(&v, FxBytes(obj) + off, sizeof v);
    return v;
}
static int32_t FxGet32(uintptr_t obj, int off) {
    int32_t v = 0;
    memcpy(&v, FxBytes(obj) + off, sizeof v);
    return v;
}
static void FxSetClass(uintptr_t obj, uintptr_t cls) { FxSetPtr(obj, Grimoire::OFF_UOBJECT_CLASS, cls); }
static void FxSetOuter(uintptr_t obj, uintptr_t outer) { FxSetPtr(obj, DynOff::UOBJECT_OUTER, outer); }
static void FxSetIndex(uintptr_t obj, int32_t index) {
    FxPut32(obj, Grimoire::OFF_UOBJECT_INDEX, static_cast<uint32_t>(index));
}
static void FxSetFlags(uintptr_t obj, uint32_t flags) { FxPut32(obj, Grimoire::OFF_UOBJECT_FLAGS, flags); }
static void FxPutTArray(uintptr_t obj, int off, uintptr_t data, int32_t num, int32_t max) {
    FxPut64(obj, off, data);
    FxPut32(obj, off + kTArrayNumAt, static_cast<uint32_t>(num));
    FxPut32(obj, off + kTArrayMaxAt, static_cast<uint32_t>(max));
}
static void FxPutWeak(uintptr_t obj, int off, int32_t objectIndex, int32_t serial) {
    FxPut32(obj, off, static_cast<uint32_t>(objectIndex));
    FxPut32(obj, off + kWeakSerialAt, static_cast<uint32_t>(serial));
}

// ══ the fixture: names ══════════════════════════════════════════════════════════════════════════════════════════
// A UE4 TNameEntryArray: one chunk of entry pointers, each entry 0x100 bytes with its text at +0x10. Spare entries
// after the last used one give the reader's look-ahead window real memory.
constexpr int kMaxNames = 2048;
constexpr int kNameSpare = 8;
constexpr int kNameEntryBytes = 0x100;
constexpr int kNameTextAt = 0x10;
static uint8_t g_nameEntry[kMaxNames + kNameSpare][kNameEntryBytes];
static uintptr_t g_nameChunk[kMaxNames + 1];
static uintptr_t g_nameChunks[2];
static std::vector<std::string> g_nameText{ std::string() };   // [0] is FName None
static std::unordered_map<std::string, int32_t> g_nameIndex;

static int32_t FxFName(const char* s) {
    const auto it = g_nameIndex.find(s);
    if (it != g_nameIndex.end()) return it->second;
    const size_t i = g_nameText.size();
    const size_t len = strlen(s);
    if (i >= static_cast<size_t>(kMaxNames) || len + 1 > static_cast<size_t>(kNameEntryBytes - kNameTextAt))
        FxFixtureFull("the name table (or one entry's text)");
    memcpy(g_nameEntry[i] + kNameTextAt, s, len + 1);
    g_nameChunk[i] = FxAddr(g_nameEntry[i]);
    g_nameText.emplace_back(s);
    const int32_t index = static_cast<int32_t>(i);
    g_nameIndex.emplace(s, index);
    return index;
}

// ══ the fixture: every UObject it built, and what a reset puts back ════════════════════════════════════════════
struct FxObjRec {
    size_t bytes = 0;      // the blob's size: what a reset clears past the header
    bool world = false;    // a world object a reset re-plants; reflection data (classes, properties) is never cleared
    std::array<uint8_t, static_cast<size_t>(kUObjectHeaderBytes)> header{};   // as built: FxSnapshotHeaders
};
static std::unordered_map<uintptr_t, FxObjRec> g_objRec;
static std::vector<uintptr_t> g_allObjs;     // build order
static std::vector<uintptr_t> g_worldObjs;   // build order
static int32_t g_created = 0;

static bool FxIsFixtureObj(uintptr_t a) { return a && g_objRec.find(a) != g_objRec.end(); }
// The name the fixture planted, read through its own table -- never through Ubel. "" for anything it did not build.
static std::string FxNameText(uintptr_t obj) {
    if (!FxIsFixtureObj(obj)) return std::string();
    const int32_t index = FxGet32(obj, Grimoire::OFF_UOBJECT_NAME);
    if (index < 0 || static_cast<size_t>(index) >= g_nameText.size()) return std::string();
    return g_nameText[static_cast<size_t>(index)];
}
// The planted name of obj's class as it stands now; "" when the class is 0 or not a fixture object.
static std::string FxClassNameText(uintptr_t obj) {
    if (!FxIsFixtureObj(obj)) return std::string();
    return FxNameText(FxGetPtr(obj, Grimoire::OFF_UOBJECT_CLASS));
}
// obj's InternalIndex as it stands now; -1 for anything the fixture did not build (Edel's "unreadable").
static int32_t FxIndexOf(uintptr_t obj) {
    return FxIsFixtureObj(obj) ? FxGet32(obj, Grimoire::OFF_UOBJECT_INDEX) : -1;
}
static const std::vector<uintptr_t>& FxAllObjects() { return g_allObjs; }

static uintptr_t FxNewObjBlob() {
    if (g_objUsed >= kMaxObjs) FxFixtureFull("the object arena");
    return FxAddr(g_objArena[g_objUsed++]);
}
static uintptr_t FxNewBigBlob() {
    if (g_bigUsed >= kMaxBig) FxFixtureFull("the big-object arena");
    return FxAddr(g_bigArena[g_bigUsed++]);
}
// One UObject's header at `at`: class, name, outer and an InternalIndex of kIndexBase plus its creation order.
static uintptr_t FxUObjectAt(uintptr_t at, uintptr_t cls, const char* name, uintptr_t outer, size_t bytes,
                             bool world) {
    FxPut64(at, Grimoire::OFF_UOBJECT_CLASS, cls);
    FxPut32(at, Grimoire::OFF_UOBJECT_NAME, static_cast<uint32_t>(FxFName(name)));
    FxPut64(at, DynOff::UOBJECT_OUTER, outer);
    FxSetIndex(at, kIndexBase + g_created++);
    FxObjRec rec;
    rec.bytes = bytes;
    rec.world = world;
    g_objRec[at] = rec;
    g_allObjs.push_back(at);
    if (world) g_worldObjs.push_back(at);
    return at;
}
static uintptr_t FxObj(uintptr_t cls, const char* name, uintptr_t outer = 0) {
    return FxUObjectAt(FxNewObjBlob(), cls, name, outer, kObjBytes, true);
}
static uintptr_t FxObjBig(uintptr_t cls, const char* name, uintptr_t outer = 0) {
    return FxUObjectAt(FxNewBigBlob(), cls, name, outer, kBigBytes, true);
}
static uintptr_t FxReflObj(uintptr_t cls, const char* name, uintptr_t outer) {
    return FxUObjectAt(FxNewObjBlob(), cls, name, outer, kObjBytes, false);
}
// A component: its fields start at kSlot0, and its InternalIndex is kUntrackedIndex so that, met through a
// controller's field, it scores -60 and is dropped -- only its own edges reach the list, once it is scanned.
static uintptr_t FxComp(uintptr_t cls, const char* name, uintptr_t outer) {
    const uintptr_t c = FxObjBig(cls, name, outer);
    FxSetIndex(c, kUntrackedIndex);
    return c;
}
static void FxSnapshotHeaders() {
    for (auto& kv : g_objRec) memcpy(kv.second.header.data(), FxBytes(kv.first), kv.second.header.size());
}

// ── default contents: recorded once at build, replayed by every reset after the bodies are cleared ──
struct FxPatch {
    uintptr_t at;
    std::vector<uint8_t> bytes;
};
static std::vector<FxPatch> g_patches;
struct FxBufDef {
    uintptr_t* buf;
    size_t count;
    std::vector<uintptr_t> init;   // the rest of the buffer is zero
};
static std::vector<FxBufDef> g_bufDefs;

static void FxDefRaw(uintptr_t at, const void* p, size_t n) {
    memcpy(FxBytes(at), p, n);
    const uint8_t* b = static_cast<const uint8_t*>(p);
    g_patches.push_back(FxPatch{ at, std::vector<uint8_t>(b, b + n) });
}
static void FxDefPtr(uintptr_t obj, int off, uintptr_t v) { FxDefRaw(obj + static_cast<uintptr_t>(off), &v, sizeof v); }
static void FxDefI32(uintptr_t obj, int off, int32_t v) { FxDefRaw(obj + static_cast<uintptr_t>(off), &v, sizeof v); }
static void FxDefTArray(uintptr_t obj, int off, uintptr_t data, int32_t num, int32_t max) {
    uint8_t image[kTArrayBytes] = {};
    memcpy(image, &data, sizeof data);
    memcpy(image + kTArrayNumAt, &num, sizeof num);
    memcpy(image + kTArrayMaxAt, &max, sizeof max);
    FxDefRaw(obj + static_cast<uintptr_t>(off), image, sizeof image);
}
static void FxDefWeak(uintptr_t obj, int off, int32_t objectIndex, int32_t serial) {
    const int32_t image[2] = { objectIndex, serial };
    FxDefRaw(obj + static_cast<uintptr_t>(off), image, sizeof image);
}
static void FxDefBuf(uintptr_t* buf, size_t count, std::vector<uintptr_t> init) {
    if (init.size() > count) FxFixtureFail("a buffer's default is longer than the buffer");
    memset(buf, 0, count * sizeof(uintptr_t));
    memcpy(buf, init.data(), init.size() * sizeof(uintptr_t));
    g_bufDefs.push_back(FxBufDef{ buf, count, std::move(init) });
}

// ══ the fixture: reflection builders ════════════════════════════════════════════════════════════════════════════
constexpr const char* kObjectProperty = "ObjectProperty";
constexpr const char* kClassProperty = "ClassProperty";
constexpr const char* kArrayProperty = "ArrayProperty";
constexpr const char* kStructProperty = "StructProperty";
constexpr const char* kIntProperty = "IntProperty";
constexpr const char* kWeakObjectProperty = "WeakObjectProperty";

// The FunctionFlags latch and UClass::Interfaces as Wirbel's fixture measures them: Edel walks no function, but a walk
// that found them undecided would run a GObjects vote.
constexpr int kFnFlagsAt = 0xB0;
constexpr int kUClassInterfacesAt = 0xC0;

// The metaclasses and packages every other object hangs off. "ScriptStruct" is the name Ubel::IsScriptStructObject
// accepts.
static uintptr_t g_metaClass, g_metaPackage, g_metaStruct, g_pkgEngine, g_pkgOther, g_pkgEngineU;

// Every class the fixture builds, so a reset can put back any head a scenario swapped (FxRestoreClasses).
static std::vector<uintptr_t> g_allClasses;
static uintptr_t FxClass(const char* name, uintptr_t super, int32_t propsSize, uintptr_t outer = 0) {
    const uintptr_t c = FxReflObj(g_metaClass, name, outer ? outer : g_pkgEngine);
    FxPut64(c, DynOff::USTRUCT_SUPER, super);
    FxPut32(c, DynOff::USTRUCT_PROPSSIZE, static_cast<uint32_t>(propsSize));
    g_allClasses.push_back(c);
    return c;
}
static uintptr_t FxStruct(const char* name, int32_t size, uintptr_t firstField) {
    const uintptr_t s = FxReflObj(g_metaStruct, name, g_pkgEngine);
    FxPut32(s, DynOff::USTRUCT_PROPSSIZE, static_cast<uint32_t>(size));
    FxSetPtr(s, DynOff::USTRUCT_CHILDPROPS, firstField);
    return s;
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
// One FProperty. A StructProperty's struct and an ArrayProperty's Inner share the subclass slot after the base: without
// the Inner, Ubel types the array's elements as nothing and Aura emits no element edge.
static uintptr_t FxField(const char* type, const char* name, int32_t off, int32_t size, uintptr_t next = 0,
                         uintptr_t subclassSlot = 0) {
    if (g_fieldUsed >= kMaxFields) FxFixtureFull("the field arena");
    const uintptr_t f = FxAddr(g_fieldArena[g_fieldUsed++]);
    FxPut64(f, DynOff::FFIELD_CLASS, FxFieldClass(type));
    FxPut32(f, DynOff::FFIELD_NAME, static_cast<uint32_t>(FxFName(name)));
    FxPut64(f, DynOff::FFIELD_NEXT, next);
    FxPut32(f, DynOff::FPROPERTY_ELEMSIZE - 4, 1);   // ArrayDim, the int32 just before ElementSize
    FxPut32(f, DynOff::FPROPERTY_ELEMSIZE, static_cast<uint32_t>(size));
    FxPut32(f, DynOff::FPROPERTY_OFFSET, static_cast<uint32_t>(off));
    if (strcmp(type, kStructProperty) == 0) FxPut64(f, DynOff::FSTRUCTPROP_STRUCT, subclassSlot);
    if (strcmp(type, kArrayProperty) == 0) FxPut64(f, DynOff::FARRAYPROP_INNER, subclassSlot);
    return f;
}
static uintptr_t FxObjF(const char* name, int32_t off) { return FxField(kObjectProperty, name, off, kPtrBytes); }
static uintptr_t FxClassPropF(const char* name, int32_t off) { return FxField(kClassProperty, name, off, kPtrBytes); }
static uintptr_t FxIntF(const char* name, int32_t off) { return FxField(kIntProperty, name, off, kIntBytes); }
static uintptr_t FxWeakF(const char* name, int32_t off) { return FxField(kWeakObjectProperty, name, off, kWeakBytes); }
static uintptr_t FxStructF(const char* name, int32_t off, int32_t size, uintptr_t s) {
    return FxField(kStructProperty, name, off, size, 0, s);
}
// An ArrayProperty with its Inner (named as the array, as UE names it) planted at DynOff::FARRAYPROP_INNER.
static uintptr_t FxArrayF(const char* name, int32_t off, const char* innerType = kObjectProperty) {
    const uintptr_t inner = FxField(innerType, name, 0, kPtrBytes);
    return FxField(kArrayProperty, name, off, kTArrayBytes, 0, inner);
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
// ObjectProperty fields named in order, one pointer apart from `first` (kSlot0 unless given).
static uintptr_t FxObjFieldsAt(const std::vector<std::string>& names, int32_t first) {
    uintptr_t head = 0, tail = 0;
    for (size_t i = 0; i < names.size(); ++i) {
        const uintptr_t f = FxObjF(names[i].c_str(), first + static_cast<int32_t>(i) * kPtrBytes);
        if (tail) FxSetPtr(tail, DynOff::FFIELD_NEXT, f);
        else head = f;
        tail = f;
    }
    return head;
}
static uintptr_t FxObjFields(const std::vector<std::string>& names) { return FxObjFieldsAt(names, FxOff::kSlot0); }
// stem01, stem02 ... (two digits).
static std::vector<std::string> FxNumbered(const char* stem, int first, int count) {
    std::vector<std::string> out;
    for (int i = 0; i < count; ++i) out.push_back(FxFmt("%s%02d", stem, first + i));
    return out;
}
static std::vector<std::string> FxCat(std::vector<std::string> a, const std::vector<std::string>& b) {
    a.insert(a.end(), b.begin(), b.end());
    return a;
}
// Where an FField chain ends: the class's PropertiesSize must reach at least this far.
static int32_t FxChainEnd(uintptr_t head) {
    int32_t end = 0;
    for (uintptr_t f = head; f; f = FxGetPtr(f, DynOff::FFIELD_NEXT)) {
        const int32_t e = FxGet32(f, DynOff::FPROPERTY_OFFSET) + FxGet32(f, DynOff::FPROPERTY_ELEMSIZE);
        if (e > end) end = e;
    }
    return end;
}
static void FxSetProps(uintptr_t cls, uintptr_t head) { FxSetPtr(cls, DynOff::USTRUCT_CHILDPROPS, head); }
static void FxSetChildren(uintptr_t cls, uintptr_t head) { FxSetPtr(cls, DynOff::USTRUCT_CHILDREN, head); }
// A class whose own fields are `head` (0 for none), sized to reach past both its super's and its own last field.
static uintptr_t FxClassF(const char* name, uintptr_t super, uintptr_t head, uintptr_t outer = 0) {
    int32_t size = kUObjectHeaderBytes;
    if (FxIsFixtureObj(super)) {
        const int32_t superSize = FxGet32(super, DynOff::USTRUCT_PROPSSIZE);
        if (superSize > size) size = superSize;
    }
    const int32_t end = FxChainEnd(head);
    if (end > size) size = end;
    const uintptr_t c = FxClass(name, super, size, outer);
    FxSetProps(c, head);
    return c;
}

// A UE4-era UProperty (a UObject): ArrayDim, ElementSize, Offset and UField::Next at the UProperty offsets, as
// dll_core_test's UPROPINNER lays them out.
static uintptr_t g_cUpObjectProperty, g_cUpArrayProperty, g_cUpWeakProperty;
static uintptr_t FxUProp(uintptr_t propClass, const char* name, int32_t off, int32_t size, uintptr_t next = 0) {
    const uintptr_t p = FxReflObj(propClass, name, 0);
    FxPut32(p, DynOff::UPROPERTY_ELEMSIZE - 4, 1);
    FxPut32(p, DynOff::UPROPERTY_ELEMSIZE, static_cast<uint32_t>(size));
    FxPut32(p, DynOff::UPROPERTY_OFFSET, static_cast<uint32_t>(off));
    FxPut64(p, DynOff::UFIELD_NEXT, next);
    return p;
}
// A UArrayProperty whose Inner UObjectProperty sits in the subclass slot the family's array probe reads.
static uintptr_t FxUArrayProp(const char* name, int32_t off, uintptr_t next = 0) {
    const uintptr_t inner = FxUProp(g_cUpObjectProperty, name, 0, kPtrBytes);
    const uintptr_t a = FxUProp(g_cUpArrayProperty, name, off, kTArrayBytes, next);
    FxPut64(a, DynOff::FARRAYPROP_INNER, inner);
    return a;
}
static int32_t FxUChainEnd(uintptr_t head) {
    int32_t end = 0;
    for (uintptr_t p = head; p; p = FxGetPtr(p, DynOff::UFIELD_NEXT)) {
        const int32_t e = FxGet32(p, DynOff::UPROPERTY_OFFSET) + FxGet32(p, DynOff::UPROPERTY_ELEMSIZE);
        if (e > end) end = e;
    }
    return end;
}
// A UE4 twin class: its properties are UObjects on its Children chain, in package EngineU.
static uintptr_t FxUClass(const char* name, uintptr_t super, uintptr_t head) {
    int32_t size = FxIsFixtureObj(super) ? FxGet32(super, DynOff::USTRUCT_PROPSSIZE) : kUObjectHeaderBytes;
    const int32_t end = FxUChainEnd(head);
    if (end > size) size = end;
    const uintptr_t c = FxClass(name, super, size, g_pkgEngineU);
    FxSetChildren(c, head);
    return c;
}

// ══ the fixture: its classes (built once in FxBuildClasses; a scenario never edits one) ═════════════════════════
// Aura's reference-metadata cache is permanent per DynOff::FamilyCacheKey and has no witness: a field list edited after
// its first walk would read one way through FindFieldOffset and another through the edge walk. A scenario changes
// shape by pointing an object at another class.
static uintptr_t g_cObject, g_cActor, g_cController, g_cPlayerController, g_cDcc, g_cPawn, g_cCharacter,
    g_cActorComponent, g_cSceneComponent, g_cPawnSensing, g_cCmc, g_cPlayer, g_cLocalPlayer, g_cNetConnection,
    g_cGameInstance, g_cWorld, g_cUserWidget;
static uintptr_t g_sLockOnInfo;
// /Script/EngineOther: engine names with other shapes, and the chain's decoy classes.
static uintptr_t g_cCharacterOther, g_cDefaultPawnOther, g_cDccOther, g_cWorldShadow, g_cWorldFuzzy, g_cWorldBare,
    g_cGameInstanceFuzzy, g_cGameInstanceBare, g_cLocalPlayerFuzzy;
// Game classes.
static uintptr_t g_cGameCtrl, g_cFreeCam, g_cDccHud, g_cDccFuzzy, g_cPcAckOnly, g_cPcExcl, g_cWidgetHud, g_cHero,
    g_cHeroBare, g_cEnemy, g_cParty, g_cBrute, g_cBot, g_cProp, g_cWorldBoss, g_cHolder, g_cData, g_cActorProxy,
    g_cLockOnData, g_cTargetHolder, g_cCompEscort, g_cCompHorde, g_cCompTrap, g_cCycA, g_cCycB, g_cSelfLoop,
    g_cBadSuper;
// Filler_00 .. Filler_63: each Filler_k's super is Filler_(k+1), Filler_63's is Actor -- so Actor is the 64th class
// node above Filler_01 and the 65th above Filler_00.
constexpr int kFillerClasses = kSuperChainNodes;
static uintptr_t g_cFiller[kFillerClasses];
// Scenario classes: PlayerController subclasses (Pc*) and Character subclasses (Pawn*).
static uintptr_t g_cPcParty, g_cPcScore, g_cPawnScore, g_cPcKwPos, g_cPcKwNeg, g_cPcCand, g_cPawnCand, g_cPcDedup,
    g_cPawnDedup, g_cPcSort, g_cPcHorde, g_cPcHorde2, g_cPcHorde3, g_cPcHorde3b, g_cPcComp1, g_cPcComp17,
    g_cPawnComp1, g_cPcComp16, g_cPawnComp16, g_cPcComp3, g_cPawnComp3, g_cPcOuter, g_cPcClass, g_cPcTrap,
    g_cPcCancel3, g_cPawnCancel3, g_cPcCancel4, g_cPawnCancel4, g_cPawnMargin, g_cPawnZero, g_cPawnFind1,
    g_cPcBareTargets, g_cPawnExcl, g_cPcCompQ, g_cPawnCompQ;
// The UE4 (UProperty) twins: distinct class objects, because Aura's reference metadata ignores bUseFProperty.
static uintptr_t g_cWorldU, g_cGameInstanceU, g_cLocalPlayerU, g_cPcU, g_cHeroU;

// PcKwPos / PcKwNeg: one field per keyword-table entry that can be told apart, each to its own target.
static const std::vector<std::string> kKwPosFields = {
    "TargetActor", "FocusActor", "LockOnActor", "LockedOnActor", "EnemyRef", "SelectedUnit", "SelectedActor",
    "NearestHostile", "BestInteractable", "LastHitActor", "LastDamageCauser",
};
static const std::vector<std::string> kKwNegFields = {
    "OwnerRef", "InstigatorRef", "AIControllerRef", "PlayerStateRef", "CameraActor", "SpectatorRef", "InputRef",
    "HudRef", "LocalPlayerRef", "DefaultPawnRef", "CheatManagerRef", "GameModeRef", "GameStateRef", "GameInstanceRef",
    "WorldRef", "LevelRef", "MeshRef", "CapsuleRef", "MovementRef", "AnimRef", "AudioRef", "WidgetRef", "ViewTarget",
};
constexpr int kKwPosCount = 11;

// ══ the fixture: its objects (built in FxBuildObjects; each FName is the handle's design name) ═════════════════
// The default world.
static uintptr_t g_world0, g_gi0, g_lp0, g_lp1, g_pc0, g_hero0, g_enemy0, g_pcAlt, g_heroAlt, g_giAlt, g_lpAlt;
static uintptr_t g_gworldSlot = 0;   // &GWorld: g_cachedGWorld points here, and it holds the world a scenario uses
// The chain's variants.
static uintptr_t g_worldShadow, g_worldFuzzy, g_worldBare, g_sub0, g_giFz, g_giT, g_lpT, g_pcT, g_lpFz;
// The object array's own, and the "Dead" variant's controller.
static uintptr_t g_cdoPc, g_widget0, g_dbgIdle, g_pcNoPlayer, g_pawnN, g_pcRemote, g_pawnR, g_netConn0, g_dbgPc0,
    g_enemyW, g_enemyU3, g_pcU0, g_lpU0, g_pcDead, g_heroDead;
// The UE4 chain.
static uintptr_t g_worldU0, g_giU0, g_heroU0, g_enemyU1, g_enemyU2;
// HOP.
static uintptr_t g_freeCam0, g_dcHud0, g_pawnD, g_dccFz0, g_dbgNoOrig0;
// PAWN.
static uintptr_t g_heroAck, g_pcAck0, g_ackPawn0, g_trap0;
// EXCL.
static uintptr_t g_freeCamX, g_pcEx, g_heroEx1, g_heroEx2, g_enemyE;
// CLASS.
static uintptr_t g_brute0, g_bot0, g_prop0, g_sense0, g_cmc0, g_data0, g_proxy0, g_deep64, g_deep65, g_cyc0, g_self0,
    g_bad0, g_badCls0, g_pcCls, g_heroCls;
// OUTER ("PC_U" / "Hero_U").
static uintptr_t g_pcOuter, g_heroOuter, g_k1, g_k2, g_k3, g_k0, g_subU, g_subU2, g_t1, g_t2, g_t3, g_t0;
// SCORE.
static uintptr_t g_pcS, g_heroS, g_aT, g_pS, g_aP, g_aO, g_aB, g_nD, g_nL, g_aV, g_nJ, g_nK, g_aN, g_aP2, g_pT, g_pO,
    g_nG;
// KWPOS / KWNEG: g_kp[i] is "KP_" + kKwPosFields[i], g_kn[i] is "KN_" + kKwNegFields[i].
static uintptr_t g_pcKp, g_heroKp, g_pcKn, g_heroKn;
static uintptr_t g_kp[kKwPosCount], g_kn[kKwNegCount];
// CAND.
static uintptr_t g_pcC, g_heroC, g_enemyA0, g_enemyA1, g_enemyN, g_orphan0;
// DEDUP / ORDER ("A" "B" "C" "N" "X" "Y" "Z" "W").
static uintptr_t g_pcD, g_heroD, g_cD1, g_cD2, g_dA, g_dB, g_dC, g_dN, g_dX, g_dY, g_dZ, g_dW;
// SORT.
static uintptr_t g_pcO, g_heroO, g_a1, g_a2, g_a3, g_a4;
// EDGE.
static uintptr_t g_filler, g_pcH, g_heroH, g_pawnIn, g_pawnOut, g_pcH2, g_heroH2, g_cIn, g_cOut, g_tIn, g_tOut, g_pcH3,
    g_heroH3, g_cBig, g_pawnCIn, g_pawnCOut, g_pcH3b, g_heroH3b, g_cBig2, g_cAfter, g_aDec, g_tAfter;
// COMP.
constexpr int kComp17 = kComponentBudget + 1;
static uintptr_t g_pcK, g_heroK, g_uComp, g_oa, g_c1K, g_tU, g_tOa, g_t1K;
static uintptr_t g_pcB, g_heroB, g_uComp2, g_aX, g_pc1, g_tp1, g_tU2;
static uintptr_t g_c17[kComp17], g_t17[kComp17];
static uintptr_t g_pcB2, g_heroB2, g_tp16b;
static uintptr_t g_cb2[kComponentBudget], g_pb2[kComponentBudget];
constexpr int kComp3Pc = kComponentBudget - 1;   // C3_01 .. C3_15: with C_sh, exactly the PC's budget
static uintptr_t g_pc3, g_hero3, g_cSh, g_tSh, g_tp16;
static uintptr_t g_c3[kComp3Pc], g_p3[kComponentBudget];
static uintptr_t g_pcQ, g_heroQ, g_cQ, g_tQ;
// CANCEL.
static uintptr_t g_pcX, g_heroX, g_pcY, g_heroY, g_cY, g_pY, g_tY, g_tpY, g_enemyY, g_pcZ, g_heroZ, g_cZa, g_cZb, g_pZ,
    g_tZb, g_tpZ;
// PICK.
static uintptr_t g_pcM, g_heroM, g_enemyM, g_aM, g_pcZ0, g_heroZ0, g_pZ0, g_pcE, g_heroE, g_pcF, g_heroF, g_tcF;
// CLAMP: more distinct pawns than the 64 cap, in ascending address order.
constexpr int kPartySize = 70;
static uintptr_t g_pcParty, g_heroParty;
static uintptr_t g_party[kPartySize];

// ══ memory that refuses, trap hosts, static buffers ═════════════════════════════════════════════════════════════
constexpr SIZE_T kPage = 0x1000;
constexpr uintptr_t kPageInterior = 0x100;   // a pointer into a refused page points past where its header would be
static uintptr_t g_unreadablePage = 0;       // reserved, never committed
static uintptr_t g_cancelPage = 0;           // reserved, never committed: its first touch raises the cancel
static uintptr_t FxGhost() { return g_unreadablePage + kPageInterior; }
static uintptr_t FxCancelTarget() { return g_cancelPage + kPageInterior; }
// PcAck_0 and GI_T each start kHostLead into a zeroed host, so the byte a read at offset -1 takes is the host's.
constexpr int32_t kHostLead = 0x40;
constexpr int32_t kTrapLead = 1;             // a read at offset -1 starts this far before its object
constexpr int32_t kTrapMax = 256;            // GI_T's trap array: a Max above its Num, so ReadTArray accepts it
alignas(16) static uint8_t g_giTrapHost[kHostLead + kObjBytes];
alignas(16) static uint8_t g_pcAckHost[kHostLead + kObjBytes];
// RF_MirroredGarbage: UE5 mirrors EInternalObjectFlags::Garbage into UObject::ObjectFlags here.
constexpr uint32_t kRfMirroredGarbage = 0x40000000u;

// Static buffers: a container's Data. A reset refills each from its default.
constexpr int32_t kLpArrCount = 2;
static uintptr_t g_lpArr[kLpArrCount]; // GI_0.LocalPlayers: [0] LocalPlayer_0, [1] LocalPlayer_1 (a different PC)
static uintptr_t g_lpAltArr[1];        // GI_Alt.LocalPlayers and GI_Fz.LocalPlayersArray: LocalPlayer_Alt
// World_Fuzzy.GameInstanceSubsystems: only [0] is an element; [2] is 0 too, so a misread "GameInstance" (the buffer
// itself) lands on something whose class -- the pointer at +0x10 -- is 0.
constexpr int32_t kSubBufCount = 4;
static uintptr_t g_subBuf[kSubBufCount];
static uintptr_t g_giTrapBuf[1];       // GI_T's trap array: LocalPlayer_T
static uintptr_t g_lpUArr[1];          // GIU_0.LocalPlayers: LocalPlayerU_0
static uintptr_t g_enemiesUBuf[1];     // HeroU_0.Enemies: Enemy_U2
static uintptr_t g_partyBuf[kPartySize];
constexpr int32_t kSortCount = 2, kCandCount = 2;
static uintptr_t g_sortBuf[kSortCount];   // PC_O.Targets: A4, A3 -- emission order opposite to address order
static uintptr_t g_candBuf[kCandCount];   // PC_C.Enemies: Enemy_A0, Enemy_A1
static uintptr_t g_squadBuf[1];        // PC_D.Squad: W
// PC_H.Crowd and PC_H2.Crowd: behind two self edges, entry kHordeFillers is edge kEdgeCap and the next one edge
// kEdgeCap + 1. C_big.Crowd has no self edge ahead of it.
constexpr int kHordeFillers = kEdgeCap - kSelfEdges - 1;   // 1021
constexpr int kHordeEntries = kHordeFillers + 2;           // 1023
constexpr int kHorde3Fillers = kEdgeCap - 1;               // 1023
constexpr int kHorde3Entries = kHorde3Fillers + 2;         // 1025
static uintptr_t g_hordeBuf[kHordeEntries];
static uintptr_t g_hordeBuf2[kHordeEntries];
static uintptr_t g_hordeBuf3[kHorde3Entries];

// ══ the fixture: classes ════════════════════════════════════════════════════════════════════════════════════════
static void FxBuildClasses() {
    using namespace FxOff;
    // ── engine (/Script/Engine) ──
    g_cObject = FxClass("Object", 0, kUObjectHeaderBytes);
    g_cActor = FxClassF("Actor", g_cObject, FxObjF("RootComponent", kRootComponent));
    g_cController = FxClassF("Controller", g_cActor, FxObjF("Pawn", kPawn));
    g_cPlayerController = FxClassF("PlayerController", g_cController,
                                   FxChainFields({ FxObjF("AcknowledgedPawn", kAckPawn), FxObjF("Player", kPlayer),
                                                   FxObjF("PlayerCameraManager", kCameraManager) }));
    g_cDcc = FxClassF("DebugCameraController", g_cPlayerController,
                      FxObjF("OriginalControllerRef", kOriginalController));
    g_cPawn = FxClassF("Pawn", g_cActor, FxObjF("Controller", kPawnController));
    g_cCharacter = FxClassF("Character", g_cPawn,
                            FxChainFields({ FxObjF("CharacterMovement", kCharacterMovement), FxObjF("Mesh", kMesh) }));
    g_cActorComponent = FxClassF("ActorComponent", g_cObject, 0);
    g_cSceneComponent = FxClassF("SceneComponent", g_cActorComponent, 0);
    g_cPawnSensing = FxClassF("PawnSensingComponent", g_cActorComponent, 0);   // "Pawn" in its name, a component
    g_cCmc = FxClassF("CharacterMovementComponent", g_cActorComponent, 0);     // "Character" in its name, a component
    g_cPlayer = FxClassF("Player", g_cObject, 0);
    g_cLocalPlayer = FxClassF("LocalPlayer", g_cPlayer, FxObjF("PlayerController", kPlayerController));
    g_cNetConnection = FxClassF("NetConnection", g_cPlayer, 0);
    g_cGameInstance = FxClassF("GameInstance", g_cObject, FxArrayF("LocalPlayers", kLocalPlayers));
    g_cWorld = FxClassF("World", g_cObject, FxObjF("OwningGameInstance", kOwningGameInstance));
    g_cUserWidget = FxClassF("UserWidget", g_cObject, 0);
    g_sLockOnInfo = FxStruct("LockOnInfo", kLockOnInfoBytes, FxObjF("CurrentTarget", kLockOnInfoTarget));

    // ── /Script/EngineOther: "Character" and "DefaultPawn" straight off Actor, so each name is the only route by
    //    which its subclass classifies as a pawn; a debug camera with no OriginalControllerRef; the chain's decoys ──
    g_cCharacterOther = FxClassF("Character", g_cActor, 0, g_pkgOther);
    g_cDefaultPawnOther = FxClassF("DefaultPawn", g_cActor, 0, g_pkgOther);
    g_cDccOther = FxClassF("DebugCameraController", g_cPlayerController, 0, g_pkgOther);
    g_cWorldShadow = FxClassF("WorldShadow", g_cObject,
                              FxChainFields({ FxObjF("CachedGameInstance", kCachedGameInstance),
                                              FxObjF("OwningGameInstance", kShadowGameInstance) }),
                              g_pkgOther);
    g_cWorldFuzzy = FxClassF("WorldFuzzy", g_cObject,
                             FxChainFields({ FxArrayF("GameInstanceSubsystems", kSubsystems),
                                             FxObjF("MyGameInstance", kMyGameInstance) }),
                             g_pkgOther);
    g_cWorldBare = FxClassF("WorldBare", g_cObject, 0, g_pkgOther);
    g_cGameInstanceFuzzy = FxClassF("GameInstanceFuzzy", g_cObject,
                                    FxChainFields({ FxIntF("LocalPlayersCount", kLocalPlayersCount),
                                                    FxArrayF("LocalPlayersArray", kLocalPlayersArray) }),
                                    g_pkgOther);
    g_cGameInstanceBare = FxClassF("GameInstanceBare", g_cObject, 0, g_pkgOther);
    g_cLocalPlayerFuzzy = FxClassF("LocalPlayerFuzzy", g_cPlayer,
                                   FxChainFields({ FxClassPropF("PlayerControllerClass", kPlayerControllerClass),
                                                   FxObjF("PlayerControllerRef", kPlayerControllerRef) }),
                                   g_pkgOther);

    // ── game classes ──
    g_cGameCtrl = FxClassF("BP_GameCtrl_C", g_cPlayerController, 0);   // named without "PlayerController"
    g_cFreeCam = FxClassF("BP_FreeCam_C", g_cDcc, 0);                  // named without "Debug"
    // The word in its name, a PlayerController by derivation, never a debug camera.
    g_cDccHud = FxClassF("DebugCameraControllerHUD_C", g_cPlayerController,
                         FxObjF("OriginalControllerRef", kHudOriginalController));
    g_cDccFuzzy = FxClassF("BP_DccFuzzy_C", g_cDccOther,
                           FxChainFields({ FxClassPropF("OriginalControllerClass", kOriginalControllerClass),
                                           FxObjF("OriginalControllerPtr", kOriginalControllerPtr) }));
    // No "Pawn" field: only AcknowledgedPawn names a pawn exactly, and PawnClassOverride, first by offset, contains it.
    g_cPcAckOnly = FxClassF("BP_PcAckOnly_C", g_cActor,
                            FxChainFields({ FxClassPropF("PawnClassOverride", kPawnClassOverride),
                                            FxObjF("AcknowledgedPawn", kAckOnlyAckPawn) }));
    g_cPcExcl = FxClassF("PcExcl", g_cController, FxObjF("AcknowledgedPawn", kExclAckPawn));
    g_cWidgetHud = FxClassF("WBP_PlayerControllerHUD_C", g_cUserWidget, FxObjF("Player", kWidgetPlayer));
    g_cHero = FxClassF("BP_Hero_C", g_cCharacter, FxObjF("CurrentTarget", kSlot0));
    g_cHeroBare = FxClassF("BP_HeroBare_C", g_cCharacter, 0);
    g_cEnemy = FxClassF("BP_Enemy_C", g_cCharacter, 0);
    g_cParty = FxClassF("BP_Party_C", g_cPawn, 0);
    g_cBrute = FxClassF("BP_Brute_C", g_cCharacterOther, 0);
    g_cBot = FxClassF("BP_Bot_C", g_cDefaultPawnOther, 0);
    g_cProp = FxClassF("BP_Prop_C", g_cActor, 0);
    g_cWorldBoss = FxClassF("BP_WorldBoss_C", g_cActor, 0);   // a negative keyword in its CLASS name only
    g_cHolder = FxClassF("BP_Holder_C", g_cActor, FxObjF("TargetActor", kSlot0));
    g_cData = FxClassF("BP_Data_C", g_cObject, 0);
    g_cActorProxy = FxClassF("BP_ActorProxy_C", g_cObject, 0);   // "Actor" in its name, an Object
    g_cLockOnData = FxClassF("BP_LockOnData_C", g_cObject, 0);
    g_cTargetHolder = FxClassF("UTargetHolderComponent", g_cActorComponent, FxObjF("TargetActor", kSlot0));
    g_cCompEscort = FxClassF("CompEscort", g_cActorComponent, FxObjFields({ "Escort", "Escort2" }));
    g_cCompHorde = FxClassF("CompHorde", g_cActorComponent, FxArrayF("Crowd", kSlot0));
    g_cCompTrap = FxClassF("CompTrap", g_cActorComponent, FxObjF("Trap", kSlot0));
    for (int k = kFillerClasses - 1; k >= 0; --k)
        g_cFiller[k] = FxClassF(FxFmt("Filler_%02d", k).c_str(),
                                k == kFillerClasses - 1 ? g_cActor : g_cFiller[k + 1], 0);
    g_cCycA = FxClassF("Cyc_A", 0, 0);
    g_cCycB = FxClassF("Cyc_B", g_cCycA, 0);
    FxSetPtr(g_cCycA, DynOff::USTRUCT_SUPER, g_cCycB);
    g_cSelfLoop = FxClassF("SelfLoop_C", 0, 0);
    FxSetPtr(g_cSelfLoop, DynOff::USTRUCT_SUPER, g_cSelfLoop);
    g_cBadSuper = FxClassF("BadSuper_C", g_unreadablePage, 0);

    // ── scenario classes ──
    auto pcClass = [](const char* name, uintptr_t head) { return FxClassF(name, g_cPlayerController, head); };
    auto pawnClass = [](const char* name, uintptr_t head) { return FxClassF(name, g_cCharacter, head); };
    g_cPcParty = pcClass("PcParty", FxArrayF("Party", kSlot0));
    g_cPcScore = pcClass("PcScore", FxObjFields({ "TargetActor", "Squadmate", "Prop", "OwnerRef", "Boss", "DataAsset",
                                                  "LockOnComponent", "ViewTarget", "Junk", "Junk2", "Stray" }));
    g_cPawnScore = pawnClass("PawnScore", FxObjFields({ "Prop2", "TargetPawn", "OwnerPawn", "Gadget" }));
    g_cPcKwPos = pcClass("PcKwPos", FxObjFields(kKwPosFields));
    g_cPcKwNeg = pcClass("PcKwNeg", FxObjFields(kKwNegFields));
    g_cPcCand = pcClass("PcCand", FxArrayF("Enemies", kSlot0));
    g_cPawnCand = pawnClass("PawnCand", FxChainFields({ FxStructF("LockOn", kSlot0, kLockOnInfoBytes, g_sLockOnInfo),
                                                        FxWeakF("LockedOnTarget", kLockedOnTarget),
                                                        FxObjF("Ghost", kGhost), FxObjF("Orphan", kOrphan) }));
    g_cPcDedup = pcClass("PcDedup", FxChainFields({ FxObjFields({ "FocusA", "TargetA", "Hostile1", "OwnerB", "NeutralC",
                                                                  "EnemyC", "LockOnD", "JunkD", "TargetOwner" }),
                                                    FxArrayF("Squad", kDedupSquad), FxObjF("Wingman", kDedupWingman),
                                                    FxObjF("CompD1", kDedupCompD1) }));
    g_cPawnDedup = pawnClass("PawnDedup", FxObjFields({ "Buddy", "Ally", "CompD2" }));
    g_cPcSort = pcClass("PcSort",
                        FxChainFields({ FxObjFields({ "TargetA1", "TargetA2" }), FxArrayF("Targets", Slot(2)) }));
    g_cPcHorde = pcClass("PcHorde", FxArrayF("Crowd", kSlot0));
    g_cPcHorde2 = pcClass("PcHorde2", FxArrayF("Crowd", kSlot0));
    g_cPcHorde3 = pcClass("PcHorde3", FxObjF("HordeComp", kSlot0));
    g_cPcHorde3b = pcClass("PcHorde3b", FxObjFields({ "HordeComp", "HolderAfter" }));
    g_cPcComp1 = pcClass("PcComp1", FxObjFields({ "OtherComp", "OwnedActor", "Comp1" }));
    g_cPcComp17 = pcClass("PcComp17", FxObjFields(FxCat({ "OtherComp", "Prop" }, FxNumbered("Comp", 1, kComp17))));
    g_cPawnComp1 = pawnClass("PawnComp1", FxObjF("PawnComp", kSlot0));
    g_cPcComp16 = pcClass("PcComp16", FxObjFields(FxNumbered("Comp", 1, kComponentBudget)));
    g_cPawnComp16 = pawnClass("PawnComp16", FxObjFields(FxNumbered("PawnComp", 1, kComponentBudget)));
    // Dup1 and Dup2 (both to C3_01), Comp02 .. Comp15, then Shared.
    const std::vector<std::string> comp3 = FxCat(FxCat({ "Dup1", "Dup2" }, FxNumbered("Comp", 2, kComp3Pc - 1)),
                                                 { "Shared" });
    g_cPcComp3 = pcClass("PcComp3", FxObjFields(comp3));
    g_cPawnComp3 = pawnClass("PawnComp3",
                             FxObjFields(FxCat({ "Shared" }, FxNumbered("PawnComp", 1, kComponentBudget))));
    g_cPcOuter = pcClass("PcOuter", FxObjFields({ "Comp1Hop", "Comp2Hop", "Comp3Hop", "CompNull" }));
    g_cPcClass = pcClass("PcClass", FxObjFields({ "Brute", "Bot", "Deep64", "Sense", "Deep65", "Cycle" }));
    g_cPcTrap = pcClass("PcTrap", FxObjF("Trap", kSlot0));
    g_cPcCancel3 = pcClass("PcCancel3", FxObjF("CompRef", kSlot0));
    g_cPawnCancel3 = pawnClass("PawnCancel3", FxObjFields({ "Trap", "PawnComp", "CurrentTarget" }));
    g_cPcCancel4 = pcClass("PcCancel4", FxObjFields({ "CompA", "CompB" }));
    g_cPawnCancel4 = pawnClass("PawnCancel4", FxObjF("PawnComp", kCancel4PawnComp));
    g_cPawnMargin = pawnClass("PawnMargin", FxObjFields({ "CurrentTarget", "TargetProp" }));
    g_cPawnZero = pawnClass("PawnZero", FxObjF("OwnerPawn", kSlot0));
    g_cPawnFind1 = pawnClass("PawnFind1", FxObjF("TargetingComponent", kSlot0));
    g_cPcBareTargets = pcClass("PcBareTargets", 0);
    g_cPawnExcl = pawnClass("PawnExcl",
                            FxObjFields({ "LockOnSelf", "FocusPC", "TargetAck", "HostileRaw", "EnemyRef" }));
    g_cPcCompQ = pcClass("PcCompQ", FxObjF("CachedLock", kSlot0));
    g_cPawnCompQ = pawnClass("PawnCompQ", FxObjF("LockComp", kSlot0));

    // ── the UE4 (UProperty) twins, /Script/EngineU: the property classes are UClasses named as UE4 names them ──
    g_cUpObjectProperty = FxReflObj(g_metaClass, kObjectProperty, g_pkgEngineU);
    g_cUpArrayProperty = FxReflObj(g_metaClass, kArrayProperty, g_pkgEngineU);
    g_cUpWeakProperty = FxReflObj(g_metaClass, kWeakObjectProperty, g_pkgEngineU);
    g_cWorldU = FxUClass("WorldU", g_cObject,
                         FxUProp(g_cUpObjectProperty, "OwningGameInstance", kOwningGameInstanceU, kPtrBytes));
    g_cGameInstanceU = FxUClass("GameInstanceU", g_cObject, FxUArrayProp("LocalPlayers", kLocalPlayersU));
    g_cLocalPlayerU = FxUClass("LocalPlayerU", g_cLocalPlayer,
                               FxUProp(g_cUpObjectProperty, "PlayerController", kPlayerControllerU, kPtrBytes));
    {
        const uintptr_t player = FxUProp(g_cUpObjectProperty, "Player", kPlayerU, kPtrBytes);
        const uintptr_t ack = FxUProp(g_cUpObjectProperty, "AcknowledgedPawn", kAckPawnU, kPtrBytes, player);
        const uintptr_t pawn = FxUProp(g_cUpObjectProperty, "Pawn", kPawnU, kPtrBytes, ack);
        g_cPcU = FxUClass("PlayerControllerU", g_cPlayerController, pawn);
    }
    {
        const uintptr_t locked = FxUProp(g_cUpWeakProperty, "LockedOnTarget", kHeroULocked, kWeakBytes);
        const uintptr_t enemies = FxUArrayProp("Enemies", kHeroUEnemies, locked);
        const uintptr_t target = FxUProp(g_cUpObjectProperty, "TargetActor", kSlot0, kPtrBytes, enemies);
        g_cHeroU = FxUClass("BP_HeroU_C", g_cPawn, target);
    }
}

// ══ the fixture: the object array ═══════════════════════════════════════════════════════════════════════════════
// GObjects order matters: the template, a widget holding LocalPlayer_0, an idle debug camera, a Player-less and a
// remote controller all come before the local one.
constexpr int32_t kSlotCdo = 0, kSlotWidget = 1, kSlotDbgIdle = 2, kSlotPcNoPlayer = 3, kSlotPcRemote = 4,
                  kSlotPc0 = 5, kSlotLp0 = 6, kSlotNetConn = 7, kSlotDbgPc0 = 8, kSlotEnemyW = 9, kSlotEnemyU3 = 10,
                  kSlotPcU = 11, kSlotLpU = 12, kPoolSlots = 13;
constexpr int32_t kSlotPcDead = kSlotPcNoPlayer;   // the "Dead" variant puts PC_Dead where PC_NoPlayer was
// The weak targets' serial numbers. Enemy_U3's differs from its slot, so a reader that took the index for the serial
// resolves nothing.
constexpr int32_t kSerialEnemyW = 9, kSerialEnemyU3 = 11;
constexpr int kItemSize = 24;           // the FUObjectItem stride InitWithExtendedLayout is told to expect
constexpr int kItemIndexAt = 12;        // InternalIndex within an item
constexpr int kItemSerialAt = 0x10;     // SerialNumber within a classic 24-byte item (Lineal::SerialOffsetForLayout)
namespace FxGObj {                      // the UE5 chunked FUObjectArray header
constexpr int kObjects = 0x10, kMaxElements = 0x20, kNumElements = 0x24, kMaxChunks = 0x28, kNumChunks = 0x2C,
              kBytes = 0x40;
}  // namespace FxGObj
static uint8_t g_poolItems[Grimoire::OBJECTS_PER_CHUNK * kItemSize];   // one whole chunk, as Aura reads one
static uintptr_t g_poolChunkTable[1];
alignas(16) static uint8_t g_poolHeader[FxGObj::kBytes];
static uintptr_t g_poolDefault[kPoolSlots];
static int32_t g_poolSerial[kPoolSlots];

enum class FxPoolVariant {
    Default,
    NoPc0,           // slot 5 empty: the scan's best is the remote controller
    NoPc0NoRemote,   // slots 4 and 5 empty: the scan's best has no Player
    IdleOnly,        // slots 3-5 empty: nothing but a template, a widget and idle debug cameras
    ActiveDcc,       // DbgPC_0 holds LocalPlayer_0 and PC_0 holds none: the scan takes the debug camera
    Uprop,           // only slots 10-12: Enemy_U3, PCU_0 (holding LocalPlayerU_0) and LocalPlayerU_0
    Dead,            // slot 3 is PC_Dead: destroyed, still holding LocalPlayer_0
};

static void FxPoolPut(int32_t slot, uintptr_t obj, int32_t serial) {
    uint8_t* item = g_poolItems + static_cast<size_t>(slot) * kItemSize;
    memset(item, 0, kItemSize);
    memcpy(item, &obj, sizeof obj);
    memcpy(item + kItemIndexAt, &slot, sizeof slot);
    memcpy(item + kItemSerialAt, &serial, sizeof serial);
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
// The pool as the variant says. It first undoes the field writes ActiveDcc and Uprop make, so call it BEFORE a
// scenario's own writes to PC_0 / DbgPC_0 / PCU_0's Player.
static void FxPool(FxPoolVariant v) {
    FxSetPtr(g_dbgPc0, FxOff::kPlayer, 0);
    FxSetPtr(g_pc0, FxOff::kPlayer, g_lp0);
    FxSetPtr(g_pcU0, FxOff::kPlayerU, 0);
    for (int32_t i = 0; i < kPoolSlots; ++i) FxPoolPut(i, g_poolDefault[i], g_poolSerial[i]);
    switch (v) {
    case FxPoolVariant::Default:
        break;
    case FxPoolVariant::NoPc0:
        FxPoolPut(kSlotPc0, 0, 0);
        break;
    case FxPoolVariant::NoPc0NoRemote:
        FxPoolPut(kSlotPc0, 0, 0);
        FxPoolPut(kSlotPcRemote, 0, 0);
        break;
    case FxPoolVariant::IdleOnly:
        FxPoolPut(kSlotPcNoPlayer, 0, 0);
        FxPoolPut(kSlotPcRemote, 0, 0);
        FxPoolPut(kSlotPc0, 0, 0);
        break;
    case FxPoolVariant::ActiveDcc:
        FxSetPtr(g_dbgPc0, FxOff::kPlayer, g_lp0);
        FxSetPtr(g_pc0, FxOff::kPlayer, 0);
        break;
    case FxPoolVariant::Uprop:
        for (int32_t i = 0; i < kPoolSlots; ++i) FxPoolPut(i, 0, 0);
        FxPoolPut(kSlotEnemyU3, g_enemyU3, kSerialEnemyU3);
        FxPoolPut(kSlotPcU, g_pcU0, 0);
        FxPoolPut(kSlotLpU, g_lpU0, 0);
        FxSetPtr(g_pcU0, FxOff::kPlayerU, g_lpU0);
        break;
    case FxPoolVariant::Dead:
        FxPoolPut(kSlotPcDead, g_pcDead, 0);
        break;
    }
    FxPoolPublish();
}

// ══ the fixture: objects ════════════════════════════════════════════════════════════════════════════════════════
static uintptr_t FxObjInHost(uint8_t* host, uintptr_t cls, const char* name) {
    return FxUObjectAt(FxAddr(host) + kHostLead, cls, name, 0, kObjBytes, true);
}
// The 16 bytes a TArray read at obj-1 takes -- the host's last byte, then obj's vtable, flags and InternalIndex --
// spelling {data, num, max}: a lookup that read a missing field at offset -1 would come back with a real array.
static void FxPlantTArrayTrap(uintptr_t obj, uintptr_t data, int32_t num, int32_t max) {
    uint8_t image[kTArrayBytes] = {};
    memcpy(image, &data, sizeof data);
    memcpy(image + kTArrayNumAt, &num, sizeof num);
    memcpy(image + kTArrayMaxAt, &max, sizeof max);
    memcpy(FxBytes(obj - kTrapLead), image, sizeof image);
    FxDefRaw(obj - kTrapLead, image, kTrapLead);   // the host's byte; obj's own bytes come back with its header
}
// The 8 bytes a pointer read at obj-1 takes spelling `target`.
static void FxPlantPtrTrap(uintptr_t obj, uintptr_t target) {
    memcpy(FxBytes(obj - kTrapLead), &target, sizeof target);
    FxDefRaw(obj - kTrapLead, &target, kTrapLead);
}

static void FxBuildObjects() {
    // ── the default world ──
    g_world0 = FxObj(g_cWorld, "World_0");
    g_gi0 = FxObj(g_cGameInstance, "GI_0");
    g_lp0 = FxObj(g_cLocalPlayer, "LocalPlayer_0", g_gi0);
    g_lp1 = FxObj(g_cLocalPlayer, "LocalPlayer_1", g_gi0);
    g_pc0 = FxObj(g_cGameCtrl, "PC_0");
    g_hero0 = FxObjBig(g_cHero, "Hero_0");
    g_enemy0 = FxObj(g_cEnemy, "Enemy_0");
    g_pcAlt = FxObj(g_cGameCtrl, "PC_Alt");   // never pooled: the scan cannot pick it
    g_heroAlt = FxObj(g_cHeroBare, "Hero_Alt");
    g_giAlt = FxObj(g_cGameInstance, "GI_Alt");
    g_lpAlt = FxObj(g_cLocalPlayer, "LocalPlayer_Alt", g_giAlt);

    // ── the chain's variants ──
    g_worldShadow = FxObj(g_cWorldShadow, "World_Shadow");
    g_worldFuzzy = FxObj(g_cWorldFuzzy, "World_Fuzzy");
    g_worldBare = FxObj(g_cWorldBare, "World_Bare");
    g_sub0 = FxObj(g_cData, "Sub_0");
    g_giFz = FxObj(g_cGameInstanceFuzzy, "GI_Fz");
    g_giT = FxObjInHost(g_giTrapHost, g_cGameInstanceBare, "GI_T");
    g_lpT = FxObj(g_cLocalPlayer, "LocalPlayer_T");
    g_pcT = FxObj(g_cGameCtrl, "PC_T");       // never pooled
    g_lpFz = FxObj(g_cLocalPlayerFuzzy, "LocalPlayer_Fz");

    // ── the object array's own ──
    g_cdoPc = FxObj(g_cPlayerController, "Default__PlayerController", g_pkgEngine);
    g_widget0 = FxObj(g_cWidgetHud, "WBP_PlayerControllerHUD_C_0");
    g_dbgIdle = FxObj(g_cDcc, "DbgIdle_0");
    g_pcNoPlayer = FxObj(g_cGameCtrl, "PC_NoPlayer");
    g_pawnN = FxObj(g_cHeroBare, "Pawn_N");
    g_pcRemote = FxObj(g_cGameCtrl, "PC_Remote");
    g_pawnR = FxObj(g_cHeroBare, "Pawn_R");
    g_netConn0 = FxObj(g_cNetConnection, "NetConnection_0");
    g_dbgPc0 = FxObj(g_cDcc, "DbgPC_0");
    g_enemyW = FxObj(g_cEnemy, "Enemy_W");
    g_enemyU3 = FxObj(g_cEnemy, "Enemy_U3");
    g_pcU0 = FxObj(g_cPcU, "PCU_0");
    g_giU0 = FxObj(g_cGameInstanceU, "GIU_0");
    g_lpU0 = FxObj(g_cLocalPlayerU, "LocalPlayerU_0", g_giU0);
    g_pcDead = FxObj(g_cGameCtrl, "PC_Dead");
    g_heroDead = FxObj(g_cHeroBare, "Hero_Dead");

    // ── the UE4 chain ──
    g_worldU0 = FxObj(g_cWorldU, "WorldU_0");
    g_heroU0 = FxObjBig(g_cHeroU, "HeroU_0");
    g_enemyU1 = FxObj(g_cEnemy, "Enemy_U1");
    g_enemyU2 = FxObj(g_cEnemy, "Enemy_U2");

    // ── HOP ──
    g_freeCam0 = FxObj(g_cFreeCam, "FreeCam_0");
    g_dcHud0 = FxObj(g_cDccHud, "DcHud_0");
    g_pawnD = FxObj(g_cHeroBare, "Pawn_D");
    g_dccFz0 = FxObj(g_cDccFuzzy, "DccFz_0");
    g_dbgNoOrig0 = FxObj(g_cDcc, "DbgNoOrig_0");

    // ── PAWN ──
    g_heroAck = FxObj(g_cHeroBare, "Hero_Ack");
    g_pcAck0 = FxObjInHost(g_pcAckHost, g_cPcAckOnly, "PcAck_0");
    g_ackPawn0 = FxObj(g_cHeroBare, "AckPawn_0");
    g_trap0 = FxObj(g_cEnemy, "Trap_0");

    // ── EXCL ──
    g_freeCamX = FxObj(g_cFreeCam, "FreeCam_X");
    g_pcEx = FxObj(g_cPcExcl, "PC_Ex");
    g_heroEx1 = FxObjBig(g_cPawnExcl, "Hero_Ex1");
    g_heroEx2 = FxObj(g_cHeroBare, "Hero_Ex2");
    g_enemyE = FxObj(g_cEnemy, "Enemy_E");

    // ── CLASS ──
    g_brute0 = FxObj(g_cBrute, "Brute_0");
    g_bot0 = FxObj(g_cBot, "Bot_0");
    g_prop0 = FxObj(g_cProp, "Prop_0");
    g_sense0 = FxObj(g_cPawnSensing, "Sense_0", g_enemy0);
    g_cmc0 = FxObj(g_cCmc, "Cmc_0", g_enemy0);
    g_data0 = FxObj(g_cData, "Data_0");
    g_proxy0 = FxObj(g_cActorProxy, "Proxy_0");
    g_deep64 = FxObj(g_cFiller[1], "Deep64_0");
    g_deep65 = FxObj(g_cFiller[0], "Deep65_0");
    g_cyc0 = FxObj(g_cCycA, "Cyc_0");
    g_self0 = FxObj(g_cSelfLoop, "Self_0");
    g_bad0 = FxObj(g_cBadSuper, "Bad_0");
    g_badCls0 = FxObj(g_unreadablePage, "BadCls_0");   // its class pointer cannot be read
    g_pcCls = FxObjBig(g_cPcClass, "PC_Cls");
    g_heroCls = FxObj(g_cHeroBare, "Hero_Cls");

    // ── OUTER ──
    g_pcOuter = FxObjBig(g_cPcOuter, "PC_U");
    g_heroOuter = FxObj(g_cHeroBare, "Hero_U");
    g_subU = FxObj(g_cData, "Sub_U", g_pcOuter);
    g_subU2 = FxObj(g_cData, "Sub_U2", g_subU);
    g_k1 = FxComp(g_cTargetHolder, "K1", g_pcOuter);   // one hop
    g_k2 = FxComp(g_cTargetHolder, "K2", g_subU);      // two hops
    g_k3 = FxComp(g_cTargetHolder, "K3", g_subU2);     // three hops
    g_k0 = FxComp(g_cTargetHolder, "K0", 0);           // no outer
    g_t1 = FxObj(g_cProp, "T1");
    g_t2 = FxObj(g_cProp, "T2");
    g_t3 = FxObj(g_cProp, "T3");
    g_t0 = FxObj(g_cProp, "T0");

    // ── SCORE ──
    g_pcS = FxObjBig(g_cPcScore, "PC_S");
    g_heroS = FxObjBig(g_cPawnScore, "Hero_S");
    g_aT = FxObj(g_cProp, "A_t");
    g_pS = FxObj(g_cParty, "P_s");
    g_aP = FxObj(g_cProp, "A_p");
    g_aO = FxObj(g_cProp, "A_o");
    g_aB = FxObj(g_cWorldBoss, "A_b");
    g_nD = FxObj(g_cData, "N_d");
    g_nL = FxObj(g_cLockOnData, "N_l");
    g_aV = FxObj(g_cProp, "A_v");
    g_nJ = FxObj(g_cData, "N_j");
    g_nK = FxObj(g_cData, "N_k");
    g_aN = FxObj(g_cProp, "A_n");
    g_aP2 = FxObj(g_cProp, "A_p2");
    g_pT = FxObj(g_cParty, "P_t");
    g_pO = FxObj(g_cParty, "P_o");
    g_nG = FxObj(g_cData, "N_g");

    // ── KWPOS / KWNEG ──
    g_pcKp = FxObjBig(g_cPcKwPos, "PC_KP");
    g_heroKp = FxObj(g_cHeroBare, "Hero_KP");
    for (int i = 0; i < kKwPosCount; ++i) g_kp[i] = FxObj(g_cProp, ("KP_" + kKwPosFields[i]).c_str());
    g_pcKn = FxObjBig(g_cPcKwNeg, "PC_KN");
    g_heroKn = FxObj(g_cHeroBare, "Hero_KN");
    for (int i = 0; i < kKwNegCount; ++i) g_kn[i] = FxObj(g_cProp, ("KN_" + kKwNegFields[i]).c_str());

    // ── CAND: Enemy_A0 below Enemy_A1 by address ──
    g_pcC = FxObjBig(g_cPcCand, "PC_C");
    g_heroC = FxObjBig(g_cPawnCand, "Hero_C");
    g_enemyA0 = FxObj(g_cEnemy, "Enemy_A0");
    g_enemyA1 = FxObj(g_cEnemy, "Enemy_A1");
    g_enemyN = FxObj(g_cEnemy, "Enemy_N");
    g_orphan0 = FxObj(0, "Orphan_0");   // its class pointer is 0

    // ── DEDUP / ORDER ──
    g_pcD = FxObjBig(g_cPcDedup, "PC_D");
    g_heroD = FxObjBig(g_cPawnDedup, "Hero_D");
    g_cD1 = FxComp(g_cCompEscort, "C_D1", g_pcD);
    g_cD2 = FxComp(g_cCompEscort, "C_D2", g_heroD);
    g_dA = FxObj(g_cProp, "A");
    g_dB = FxObj(g_cProp, "B");
    g_dC = FxObj(g_cProp, "C");
    g_dN = FxObj(g_cData, "N");
    g_dX = FxObj(g_cProp, "X");
    g_dY = FxObj(g_cProp, "Y");
    g_dZ = FxObj(g_cProp, "Z");
    g_dW = FxObj(g_cProp, "W");

    // ── SORT: A3 < A4 < A2 < A1 by address ──
    g_pcO = FxObjBig(g_cPcSort, "PC_O");
    g_heroO = FxObj(g_cHeroBare, "Hero_O");
    g_a3 = FxObj(g_cProp, "A3");
    g_a4 = FxObj(g_cProp, "A4");
    g_a2 = FxObj(g_cProp, "A2");
    g_a1 = FxObj(g_cProp, "A1");

    // ── EDGE ──
    g_filler = FxObj(g_cData, "Filler");
    g_pcH = FxObjBig(g_cPcHorde, "PC_H");
    g_heroH = FxObj(g_cHeroBare, "Hero_H");
    g_pawnIn = FxObj(g_cParty, "Pawn_In");
    g_pawnOut = FxObj(g_cParty, "Pawn_Out");
    g_pcH2 = FxObjBig(g_cPcHorde2, "PC_H2");
    g_heroH2 = FxObj(g_cHeroBare, "Hero_H2");
    g_cIn = FxComp(g_cTargetHolder, "C_in", g_pcH2);
    g_cOut = FxComp(g_cTargetHolder, "C_out", g_pcH2);
    g_tIn = FxObj(g_cProp, "T_in");
    g_tOut = FxObj(g_cProp, "T_out");
    g_pcH3 = FxObjBig(g_cPcHorde3, "PC_H3");
    g_heroH3 = FxObj(g_cHeroBare, "Hero_H3");
    g_cBig = FxComp(g_cCompHorde, "C_big", g_pcH3);
    g_pawnCIn = FxObj(g_cParty, "Pawn_cIn");
    g_pawnCOut = FxObj(g_cParty, "Pawn_cOut");
    g_pcH3b = FxObjBig(g_cPcHorde3b, "PC_H3b");
    g_heroH3b = FxObj(g_cHeroBare, "Hero_H3b");
    g_cBig2 = FxComp(g_cCompHorde, "C_big2", g_pcH3b);
    g_cAfter = FxComp(g_cCompEscort, "C_after", g_pcH3b);
    g_aDec = FxObj(g_cProp, "A_dec");
    g_tAfter = FxObj(g_cProp, "T_after");

    // ── COMP ──
    g_pcK = FxObjBig(g_cPcComp1, "PC_K");
    g_heroK = FxObj(g_cHeroBare, "Hero_K");
    g_uComp = FxComp(g_cTargetHolder, "U_comp", g_enemy0);    // owned by another actor
    g_oa = FxObjBig(g_cHolder, "OA", g_pcK);                  // owned, but an actor
    g_c1K = FxComp(g_cTargetHolder, "C1", g_pcK);
    g_tU = FxObj(g_cProp, "T_u");
    g_tOa = FxObj(g_cProp, "T_oa");
    g_t1K = FxObj(g_cProp, "T_1");
    g_pcB = FxObjBig(g_cPcComp17, "PC_B");
    g_heroB = FxObjBig(g_cPawnComp1, "Hero_B");
    g_uComp2 = FxComp(g_cTargetHolder, "U_comp2", g_enemy0);
    g_aX = FxObj(g_cProp, "A_x");
    for (int i = 0; i < kComp17; ++i) {
        g_c17[i] = FxComp(g_cTargetHolder, FxFmt("C%02d", i + 1).c_str(), g_pcB);
        g_t17[i] = FxObj(g_cProp, FxFmt("T%02d", i + 1).c_str());
    }
    g_pc1 = FxComp(g_cTargetHolder, "PC1", g_heroB);
    g_tp1 = FxObj(g_cProp, "Tp1");
    g_tU2 = FxObj(g_cProp, "T_u2");
    g_pcB2 = FxObjBig(g_cPcComp16, "PC_B2");
    g_heroB2 = FxObjBig(g_cPawnComp16, "Hero_B2");
    for (int i = 0; i < kComponentBudget; ++i) {
        g_cb2[i] = FxComp(g_cTargetHolder, FxFmt("CB2_%02d", i + 1).c_str(), g_pcB2);
        g_pb2[i] = FxComp(g_cTargetHolder, FxFmt("PB2_%02d", i + 1).c_str(), g_heroB2);
    }
    g_tp16b = FxObj(g_cProp, "Tp16b");
    g_pc3 = FxObjBig(g_cPcComp3, "PC_3");
    g_hero3 = FxObjBig(g_cPawnComp3, "Hero_3", g_pc3);   // so C_sh is owned by PC_3 within two hops
    for (int i = 0; i < kComp3Pc; ++i) g_c3[i] = FxComp(g_cTargetHolder, FxFmt("C3_%02d", i + 1).c_str(), g_pc3);
    g_cSh = FxComp(g_cTargetHolder, "C_sh", g_hero3);
    g_tSh = FxObj(g_cProp, "T_sh");
    for (int i = 0; i < kComponentBudget; ++i)
        g_p3[i] = FxComp(g_cTargetHolder, FxFmt("P3_%02d", i + 1).c_str(), g_hero3);
    g_tp16 = FxObj(g_cProp, "Tp16");
    g_pcQ = FxObjBig(g_cPcCompQ, "PC_Q");
    g_heroQ = FxObjBig(g_cPawnCompQ, "Hero_Q");          // its outer is not PC_Q
    g_cQ = FxComp(g_cTargetHolder, "C_q", g_heroQ);
    g_tQ = FxObj(g_cProp, "T_q");

    // ── CANCEL ──
    g_pcX = FxObjBig(g_cPcTrap, "PC_X");
    g_heroX = FxObjBig(g_cHero, "Hero_X");
    g_pcY = FxObjBig(g_cPcCancel3, "PC_Y");
    g_heroY = FxObjBig(g_cPawnCancel3, "Hero_Y");
    g_cY = FxComp(g_cTargetHolder, "C_y", g_pcY);
    g_pY = FxComp(g_cTargetHolder, "P_y", g_heroY);
    g_tY = FxObj(g_cProp, "T_y");
    g_tpY = FxObj(g_cProp, "Tp_y");
    g_enemyY = FxObj(g_cEnemy, "Enemy_Y");
    g_pcZ = FxObjBig(g_cPcCancel4, "PC_Z");
    g_heroZ = FxObjBig(g_cPawnCancel4, "Hero_Z");
    g_cZa = FxComp(g_cCompTrap, "C_za", g_pcZ);
    g_cZb = FxComp(g_cTargetHolder, "C_zb", g_pcZ);
    g_pZ = FxComp(g_cTargetHolder, "P_z", g_heroZ);
    g_tZb = FxObj(g_cProp, "T_zb");
    g_tpZ = FxObj(g_cProp, "Tp_z");

    // ── PICK ──
    g_pcM = FxObj(g_cGameCtrl, "PC_M");
    g_heroM = FxObjBig(g_cPawnMargin, "Hero_M");
    g_enemyM = FxObj(g_cEnemy, "Enemy_M");
    g_aM = FxObj(g_cProp, "A_m");
    g_pcZ0 = FxObj(g_cGameCtrl, "PC_Z0");
    g_heroZ0 = FxObjBig(g_cPawnZero, "Hero_Z0");
    g_pZ0 = FxObj(g_cParty, "P_z0");
    g_pcE = FxObj(g_cPcBareTargets, "PC_E");
    g_heroE = FxObj(g_cHeroBare, "Hero_E");
    g_pcF = FxObj(g_cGameCtrl, "PC_F");
    g_heroF = FxObjBig(g_cPawnFind1, "Hero_F");
    g_tcF = FxComp(g_cTargetHolder, "TC_f", g_heroF);

    // ── CLAMP: Party_00 .. Party_69 in ascending address order ──
    g_pcParty = FxObjBig(g_cPcParty, "PC_Party");
    g_heroParty = FxObj(g_cHeroBare, "Hero_Party");
    for (int i = 0; i < kPartySize; ++i) g_party[i] = FxObj(g_cParty, FxFmt("Party_%02d", i).c_str());

    // ── header-level state, before FxSnapshotHeaders: InternalIndex, flags, the traps ──
    const std::pair<uintptr_t, int32_t> pooled[] = {
        { g_cdoPc, kSlotCdo }, { g_widget0, kSlotWidget }, { g_dbgIdle, kSlotDbgIdle },
        { g_pcNoPlayer, kSlotPcNoPlayer }, { g_pcRemote, kSlotPcRemote }, { g_pc0, kSlotPc0 }, { g_lp0, kSlotLp0 },
        { g_netConn0, kSlotNetConn }, { g_dbgPc0, kSlotDbgPc0 }, { g_enemyW, kSlotEnemyW },
        { g_enemyU3, kSlotEnemyU3 }, { g_pcU0, kSlotPcU }, { g_lpU0, kSlotLpU }, { g_pcDead, kSlotPcDead },
    };
    for (const auto& p : pooled) FxSetIndex(p.first, p.second);
    for (uintptr_t o : { g_filler, g_dN, g_aM, g_pZ0, g_nJ, g_aN }) FxSetIndex(o, kUntrackedIndex);
    FxSetIndex(g_nK, 0);            // the `>= 0` boundary: index 0 earns the +5
    FxSetIndex(g_tcF, kTcFIndex);
    FxSetFlags(g_cdoPc, Grimoire::RF_CLASS_DEFAULT_OBJECT);
    FxSetFlags(g_pcDead, kRfMirroredGarbage);
    FxPlantTArrayTrap(g_giT, FxAddr(g_giTrapBuf), 1, kTrapMax);
    FxPlantPtrTrap(g_pcAck0, g_trap0);

    g_poolDefault[kSlotCdo] = g_cdoPc;            // a template: the scan passes over it
    g_poolDefault[kSlotWidget] = g_widget0;       // holds LocalPlayer_0, but is no PlayerController
    g_poolDefault[kSlotDbgIdle] = g_dbgIdle;      // a debug camera with no Player: never taken
    g_poolDefault[kSlotPcNoPlayer] = g_pcNoPlayer;
    g_poolDefault[kSlotPcRemote] = g_pcRemote;
    g_poolDefault[kSlotPc0] = g_pc0;              // the local one, after every controller a first match would take
    g_poolDefault[kSlotLp0] = g_lp0;
    g_poolDefault[kSlotNetConn] = g_netConn0;
    g_poolDefault[kSlotDbgPc0] = g_dbgPc0;
    g_poolDefault[kSlotEnemyW] = g_enemyW;
    g_poolDefault[kSlotEnemyU3] = g_enemyU3;
    g_poolSerial[kSlotEnemyW] = kSerialEnemyW;
    g_poolSerial[kSlotEnemyU3] = kSerialEnemyU3;
}

// ══ the fixture: default contents (recorded once; every reset replays them) ═════════════════════════════════════
// A scenario controller's Pawn and AcknowledgedPawn both point at its pawn, and the pawn's Controller points back.
static void FxDefPair(uintptr_t pc, uintptr_t pawn) {
    FxDefPtr(pc, FxOff::kPawn, pawn);
    FxDefPtr(pc, FxOff::kAckPawn, pawn);
    FxDefPtr(pawn, FxOff::kPawnController, pc);
}
// obj's scenario fields, from kSlot0 on, pointing at `targets` in order.
static void FxDefSlots(uintptr_t obj, std::initializer_list<uintptr_t> targets) {
    int i = 0;
    for (uintptr_t t : targets) FxDefPtr(obj, FxOff::Slot(i++), t);
}
static void FxPlantDefaults() {
    using namespace FxOff;
    // ── the default world ──
    FxDefPtr(g_world0, kOwningGameInstance, g_gi0);
    FxDefBuf(g_lpArr, kLpArrCount, { g_lp0, g_lp1 });
    FxDefTArray(g_gi0, kLocalPlayers, FxAddr(g_lpArr), kLpArrCount, kLpArrCount);
    FxDefPtr(g_lp0, kPlayerController, g_pc0);
    FxDefPtr(g_lp1, kPlayerController, g_pcAlt);
    FxDefPair(g_pc0, g_hero0);
    FxDefPtr(g_pc0, kPlayer, g_lp0);
    FxDefPtr(g_hero0, kSlot0, g_enemy0);   // CurrentTarget
    FxDefPair(g_pcAlt, g_heroAlt);
    FxDefBuf(g_lpAltArr, 1, { g_lpAlt });
    FxDefTArray(g_giAlt, kLocalPlayers, FxAddr(g_lpAltArr), 1, 1);
    FxDefPtr(g_lpAlt, kPlayerController, g_pcAlt);

    // ── the chain's variants: each arm a broken or decoyed lookup would take ends somewhere else ──
    FxDefPtr(g_worldShadow, kCachedGameInstance, g_gi0);
    FxDefPtr(g_worldShadow, kShadowGameInstance, g_giAlt);
    FxDefBuf(g_subBuf, kSubBufCount, { g_sub0 });
    FxDefTArray(g_worldFuzzy, kSubsystems, FxAddr(g_subBuf), 1, 1);
    FxDefPtr(g_worldFuzzy, kMyGameInstance, g_giAlt);
    FxDefI32(g_giFz, kLocalPlayersCount, 1);
    FxDefTArray(g_giFz, kLocalPlayersArray, FxAddr(g_lpAltArr), 1, 1);
    FxDefBuf(g_giTrapBuf, 1, { g_lpT });
    FxDefPtr(g_lpT, kPlayerController, g_pcT);
    FxDefPtr(g_lpFz, kPlayerControllerClass, g_cGameCtrl);
    FxDefPtr(g_lpFz, kPlayerControllerRef, g_pcAlt);

    // ── the object array's own. The template holds what would make it the winner if it were taken ──
    FxDefPtr(g_cdoPc, kPawn, g_enemy0);
    FxDefPtr(g_cdoPc, kAckPawn, g_enemy0);
    FxDefPtr(g_cdoPc, kPlayer, g_lp0);
    FxDefPtr(g_widget0, kWidgetPlayer, g_lp0);
    FxDefPair(g_pcNoPlayer, g_pawnN);
    FxDefPair(g_pcRemote, g_pawnR);
    FxDefPtr(g_pcRemote, kPlayer, g_netConn0);
    FxDefPtr(g_dbgPc0, kOriginalController, g_pc0);
    FxDefPair(g_pcDead, g_heroDead);
    FxDefPtr(g_pcDead, kPlayer, g_lp0);

    // ── the UE4 chain (PCU_0's Player is the Uprop pool variant's to set) ──
    FxDefPtr(g_worldU0, kOwningGameInstanceU, g_giU0);
    FxDefBuf(g_lpUArr, 1, { g_lpU0 });
    FxDefTArray(g_giU0, kLocalPlayersU, FxAddr(g_lpUArr), 1, 1);
    FxDefPtr(g_lpU0, kPlayerControllerU, g_pcU0);
    FxDefPtr(g_pcU0, kPawnU, g_heroU0);
    FxDefPtr(g_pcU0, kAckPawnU, g_heroU0);
    FxDefPtr(g_heroU0, kSlot0, g_enemyU1);
    FxDefBuf(g_enemiesUBuf, 1, { g_enemyU2 });
    FxDefTArray(g_heroU0, kHeroUEnemies, FxAddr(g_enemiesUBuf), 1, 1);
    FxDefWeak(g_heroU0, kHeroULocked, kSlotEnemyU3, kSerialEnemyU3);

    // ── HOP ──
    FxDefPtr(g_freeCam0, kOriginalController, g_pc0);
    FxDefPtr(g_dcHud0, kHudOriginalController, g_pc0);
    FxDefPair(g_dcHud0, g_pawnD);
    FxDefPtr(g_dccFz0, kOriginalControllerClass, g_cGameCtrl);
    FxDefPtr(g_dccFz0, kOriginalControllerPtr, g_pc0);

    // ── PAWN ──
    FxDefPtr(g_heroAck, kPawnController, g_pc0);
    FxDefPtr(g_pcAck0, kPawnClassOverride, g_cEnemy);
    FxDefPtr(g_pcAck0, kAckOnlyAckPawn, g_ackPawn0);
    FxDefPtr(g_ackPawn0, kPawnController, g_pcAck0);

    // ── EXCL: Hero_Ex1 points at every object the exclusion set holds, and at one real target ──
    FxDefPtr(g_freeCamX, kOriginalController, g_pcEx);
    FxDefPtr(g_pcEx, kPawn, g_heroEx1);
    FxDefPtr(g_pcEx, kExclAckPawn, g_heroEx2);
    FxDefPtr(g_heroEx1, kPawnController, g_pcEx);
    FxDefPtr(g_heroEx1, Slot(0), g_heroEx1);    // LockOnSelf
    FxDefPtr(g_heroEx1, Slot(1), g_pcEx);       // FocusPC
    FxDefPtr(g_heroEx1, Slot(2), g_heroEx2);    // TargetAck
    FxDefPtr(g_heroEx1, Slot(3), g_freeCamX);   // HostileRaw
    FxDefPtr(g_heroEx1, Slot(4), g_enemyE);     // EnemyRef
    FxDefPtr(g_heroEx2, kPawnController, g_pcEx);

    // ── CLASS ──
    FxDefPair(g_pcCls, g_heroCls);
    FxDefSlots(g_pcCls, { g_brute0, g_bot0, g_deep64, g_sense0, g_deep65, g_cyc0 });

    // ── OUTER ──
    FxDefPair(g_pcOuter, g_heroOuter);
    FxDefSlots(g_pcOuter, { g_k1, g_k2, g_k3, g_k0 });
    FxDefPtr(g_k1, kSlot0, g_t1);   // TargetActor
    FxDefPtr(g_k2, kSlot0, g_t2);
    FxDefPtr(g_k3, kSlot0, g_t3);
    FxDefPtr(g_k0, kSlot0, g_t0);

    // ── SCORE ──
    FxDefPair(g_pcS, g_heroS);
    FxDefSlots(g_pcS, { g_aT, g_pS, g_aP, g_aO, g_aB, g_nD, g_nL, g_aV, g_nJ, g_nK, g_aN });
    FxDefSlots(g_heroS, { g_aP2, g_pT, g_pO, g_nG });

    // ── KWPOS / KWNEG ──
    FxDefPair(g_pcKp, g_heroKp);
    for (int i = 0; i < kKwPosCount; ++i) FxDefPtr(g_pcKp, Slot(i), g_kp[i]);
    FxDefPair(g_pcKn, g_heroKn);
    for (int i = 0; i < kKwNegCount; ++i) FxDefPtr(g_pcKn, Slot(i), g_kn[i]);

    // ── CAND ──
    FxDefPair(g_pcC, g_heroC);
    FxDefBuf(g_candBuf, kCandCount, { g_enemyA0, g_enemyA1 });
    FxDefTArray(g_pcC, kSlot0, FxAddr(g_candBuf), kCandCount, kCandCount);
    FxDefPtr(g_heroC, kLockOnTarget, g_enemyN);
    FxDefWeak(g_heroC, kLockedOnTarget, kSlotEnemyW, kSerialEnemyW);
    FxDefPtr(g_heroC, kGhost, FxGhost());
    FxDefPtr(g_heroC, kOrphan, g_orphan0);

    // ── DEDUP / ORDER ──
    FxDefPair(g_pcD, g_heroD);
    // FocusA, TargetA -> A; Hostile1, OwnerB -> B; NeutralC, EnemyC -> C; LockOnD, JunkD -> N; TargetOwner -> X.
    FxDefSlots(g_pcD, { g_dA, g_dA, g_dB, g_dB, g_dC, g_dC, g_dN, g_dN, g_dX });
    FxDefBuf(g_squadBuf, 1, { g_dW });
    FxDefTArray(g_pcD, kDedupSquad, FxAddr(g_squadBuf), 1, 1);
    FxDefPtr(g_pcD, kDedupWingman, g_dW);
    FxDefPtr(g_pcD, kDedupCompD1, g_cD1);
    FxDefPtr(g_heroD, Slot(0), g_dX);    // Buddy
    FxDefPtr(g_heroD, Slot(1), g_dY);    // Ally
    FxDefPtr(g_heroD, Slot(2), g_cD2);   // CompD2
    FxDefPtr(g_cD1, Slot(0), g_dY);      // Escort
    FxDefPtr(g_cD1, Slot(1), g_dZ);      // Escort2
    FxDefPtr(g_cD2, Slot(0), g_dZ);      // Escort

    // ── SORT ──
    FxDefPair(g_pcO, g_heroO);
    FxDefPtr(g_pcO, Slot(0), g_a1);
    FxDefPtr(g_pcO, Slot(1), g_a2);
    FxDefBuf(g_sortBuf, kSortCount, { g_a4, g_a3 });
    FxDefTArray(g_pcO, Slot(2), FxAddr(g_sortBuf), kSortCount, kSortCount);

    // ── EDGE: one Filler object repeated -- every emitted edge counts towards a cap, a repeat included ──
    {
        std::vector<uintptr_t> horde(static_cast<size_t>(kHordeFillers), g_filler), horde2 = horde;
        horde.push_back(g_pawnIn);
        horde.push_back(g_pawnOut);
        horde2.push_back(g_cIn);
        horde2.push_back(g_cOut);
        std::vector<uintptr_t> horde3(static_cast<size_t>(kHorde3Fillers), g_filler);
        horde3.push_back(g_pawnCIn);
        horde3.push_back(g_pawnCOut);
        FxDefBuf(g_hordeBuf, kHordeEntries, std::move(horde));
        FxDefBuf(g_hordeBuf2, kHordeEntries, std::move(horde2));
        FxDefBuf(g_hordeBuf3, kHorde3Entries, std::move(horde3));
    }
    FxDefPair(g_pcH, g_heroH);
    FxDefTArray(g_pcH, kSlot0, FxAddr(g_hordeBuf), kHordeEntries, kHordeEntries);
    FxDefPair(g_pcH2, g_heroH2);
    FxDefTArray(g_pcH2, kSlot0, FxAddr(g_hordeBuf2), kHordeEntries, kHordeEntries);
    FxDefPtr(g_cIn, kSlot0, g_tIn);
    FxDefPtr(g_cOut, kSlot0, g_tOut);
    FxDefPair(g_pcH3, g_heroH3);
    FxDefPtr(g_pcH3, kSlot0, g_cBig);
    FxDefTArray(g_cBig, kSlot0, FxAddr(g_hordeBuf3), kHorde3Entries, kHorde3Entries);
    FxDefPair(g_pcH3b, g_heroH3b);
    FxDefPtr(g_pcH3b, Slot(0), g_cBig2);
    FxDefPtr(g_pcH3b, Slot(1), g_cAfter);
    FxDefTArray(g_cBig2, kSlot0, FxAddr(g_hordeBuf3), kHorde3Entries, kHorde3Entries);
    FxDefPtr(g_cAfter, Slot(0), g_aDec);      // Escort
    FxDefPtr(g_cAfter, Slot(1), g_tAfter);    // Escort2: the second edge, which a full reused vector would drop

    // ── COMP ──
    FxDefPair(g_pcK, g_heroK);
    FxDefPtr(g_pcK, Slot(0), g_uComp);   // OtherComp
    FxDefPtr(g_pcK, Slot(1), g_oa);      // OwnedActor
    FxDefPtr(g_pcK, Slot(2), g_c1K);     // Comp1
    FxDefPtr(g_uComp, kSlot0, g_tU);
    FxDefPtr(g_oa, kSlot0, g_tOa);
    FxDefPtr(g_c1K, kSlot0, g_t1K);
    FxDefPair(g_pcB, g_heroB);
    FxDefPtr(g_pcB, Slot(0), g_uComp2);  // OtherComp: unowned, ahead of the owned ones
    FxDefPtr(g_pcB, Slot(1), g_aX);      // Prop: an actor, ahead of the owned ones
    for (int i = 0; i < kComp17; ++i) {
        FxDefPtr(g_pcB, Slot(2 + i), g_c17[i]);
        FxDefPtr(g_c17[i], kSlot0, g_t17[i]);
    }
    FxDefPtr(g_uComp2, kSlot0, g_tU2);
    FxDefPtr(g_heroB, Slot(0), g_pc1);   // PawnComp
    FxDefPtr(g_pc1, kSlot0, g_tp1);
    FxDefPair(g_pcB2, g_heroB2);
    for (int i = 0; i < kComponentBudget; ++i) {
        FxDefPtr(g_pcB2, Slot(i), g_cb2[i]);
        FxDefPtr(g_heroB2, Slot(i), g_pb2[i]);
    }
    FxDefPtr(g_pb2[kComponentBudget - 1], kSlot0, g_tp16b);
    FxDefPair(g_pc3, g_hero3);
    FxDefPtr(g_pc3, Slot(0), g_c3[0]);   // Dup1
    FxDefPtr(g_pc3, Slot(1), g_c3[0]);   // Dup2: the same component again
    for (int i = 1; i < kComp3Pc; ++i) FxDefPtr(g_pc3, Slot(1 + i), g_c3[i]);   // Comp02 .. Comp15
    FxDefPtr(g_pc3, Slot(kComp3Pc + 1), g_cSh);                                 // Shared
    FxDefPtr(g_cSh, kSlot0, g_tSh);
    FxDefPtr(g_hero3, Slot(0), g_cSh);                                          // Shared, first
    for (int i = 0; i < kComponentBudget; ++i) FxDefPtr(g_hero3, Slot(1 + i), g_p3[i]);
    FxDefPtr(g_p3[kComponentBudget - 1], kSlot0, g_tp16);
    FxDefPair(g_pcQ, g_heroQ);
    FxDefPtr(g_pcQ, kSlot0, g_cQ);       // CachedLock: reached from the PC, which does not own it
    FxDefPtr(g_heroQ, kSlot0, g_cQ);     // LockComp: reached from the pawn, which does
    FxDefPtr(g_cQ, kSlot0, g_tQ);

    // ── CANCEL: each Trap field points into the cancel page ──
    FxDefPair(g_pcX, g_heroX);
    FxDefPtr(g_pcX, kSlot0, FxCancelTarget());
    FxDefPtr(g_heroX, kSlot0, g_enemy0);          // CurrentTarget
    FxDefPair(g_pcY, g_heroY);
    FxDefPtr(g_pcY, kSlot0, g_cY);                // CompRef
    FxDefPtr(g_heroY, Slot(0), FxCancelTarget()); // Trap
    FxDefPtr(g_heroY, Slot(1), g_pY);             // PawnComp
    FxDefPtr(g_heroY, Slot(2), g_enemyY);         // CurrentTarget
    FxDefPtr(g_cY, kSlot0, g_tY);
    FxDefPtr(g_pY, kSlot0, g_tpY);
    FxDefPair(g_pcZ, g_heroZ);
    FxDefPtr(g_pcZ, Slot(0), g_cZa);              // CompA
    FxDefPtr(g_pcZ, Slot(1), g_cZb);              // CompB
    FxDefPtr(g_heroZ, kCancel4PawnComp, g_pZ);
    FxDefPtr(g_cZa, kSlot0, FxCancelTarget());    // CompTrap.Trap
    FxDefPtr(g_cZb, kSlot0, g_tZb);
    FxDefPtr(g_pZ, kSlot0, g_tpZ);

    // ── PICK ──
    FxDefPair(g_pcM, g_heroM);
    FxDefPtr(g_heroM, Slot(0), g_enemyM);   // CurrentTarget
    FxDefPtr(g_heroM, Slot(1), g_aM);       // TargetProp
    FxDefPair(g_pcZ0, g_heroZ0);
    FxDefPtr(g_heroZ0, kSlot0, g_pZ0);      // OwnerPawn
    FxDefPair(g_pcE, g_heroE);
    FxDefPair(g_pcF, g_heroF);
    FxDefPtr(g_heroF, kSlot0, g_tcF);       // TargetingComponent

    // ── CLAMP ──
    FxDefPair(g_pcParty, g_heroParty);
    FxDefBuf(g_partyBuf, kPartySize, std::vector<uintptr_t>(g_party, g_party + kPartySize));
    FxDefTArray(g_pcParty, kSlot0, FxAddr(g_partyBuf), kPartySize, kPartySize);
}

// ══ reset pieces ════════════════════════════════════════════════════════════════════════════════════════════════
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
// Every world object's header back as built -- a trap's bytes included, since they live in it -- its body cleared,
// then every default content and buffer replayed.
static void FxWorldDefault() {
    for (uintptr_t o : g_worldObjs) {
        const auto it = g_objRec.find(o);
        const FxObjRec& rec = it->second;
        memcpy(FxBytes(o), rec.header.data(), rec.header.size());
        memset(FxBytes(o) + kUObjectHeaderBytes, 0, rec.bytes - static_cast<size_t>(kUObjectHeaderBytes));
    }
    for (const FxPatch& p : g_patches) memcpy(FxBytes(p.at), p.bytes.data(), p.bytes.size());
    for (const FxBufDef& b : g_bufDefs) {
        memset(b.buf, 0, b.count * sizeof(uintptr_t));
        memcpy(b.buf, b.init.data(), b.init.size() * sizeof(uintptr_t));
    }
}

// ══ the fixture: swaps a scenario makes (FxReset undoes every one) ══════════════════════════════════════════════
static void FxWorld(uintptr_t world) { g_gworldSlot = world; }               // 0 = a GWorld slot that holds nothing
static void FxNoGWorld() { g_cachedGWorld = 0; }
static void FxGWorldAt(uintptr_t slotAddr) { g_cachedGWorld = slotAddr; }   // e.g. g_unreadablePage
// The chain's controller: LocalPlayer_0.PlayerController. Every scenario controller is reached this way.
static void FxLpController(uintptr_t pc) { FxSetPtr(g_lp0, FxOff::kPlayerController, pc); }
static void FxSetLocalPlayers(uintptr_t data, int32_t num, int32_t max) {
    FxPutTArray(g_gi0, FxOff::kLocalPlayers, data, num, max);
}
// One link of the default chain broken; ResolveLocalPC then falls to the instance scan.
enum class FxBreak { NoGameInstance, MaxBelowNum, NegativeNum, EmptyLocalPlayers, NullLocalPlayer, NoController };
constexpr int32_t kNegativeNumMax = 4;   // the Max a Num of -1 is paired with: above it, so only the sign can fail
static void FxBreakChain(FxBreak b) {
    switch (b) {
    case FxBreak::NoGameInstance:
        FxSetPtr(g_world0, FxOff::kOwningGameInstance, 0);
        break;
    case FxBreak::MaxBelowNum:
        FxSetLocalPlayers(FxAddr(g_lpArr), 1, 0);
        break;
    case FxBreak::NegativeNum:
        FxSetLocalPlayers(FxAddr(g_lpArr), -1, kNegativeNumMax);
        break;
    case FxBreak::EmptyLocalPlayers:
        FxSetLocalPlayers(FxAddr(g_lpArr), 0, kLpArrCount);
        break;
    case FxBreak::NullLocalPlayer:
        g_lpArr[0] = 0;
        break;
    case FxBreak::NoController:
        FxSetPtr(g_lp0, FxOff::kPlayerController, 0);
        break;
    }
}
// UProperty reflection on (UE4) or off. Every family-keyed memo -- WalkClass, WalkClassEx, Aura's reference metadata --
// is re-keyed both ways: a class with no FField chain of its own reads the same witness in either mode, so without the
// retire a walk made in one mode would be served in the other.
static void FxUpropMode(bool on) {
    DynOff::bUseFProperty = !on;
    DynOff::RetireFamilyEpoch();
}

// ══ the cancel seam ═════════════════════════════════════════════════════════════════════════════════════════════
// A first-in-line vectored handler: the first touch of g_cancelPage -- consider -> ClassifyBySuperChain -> GetClass of
// an edge's target -- raises the per-command cancel in the middle of a run. It returns CONTINUE_SEARCH, so Macht's own
// __except still handles the fault and the read fails as any refused page's does.
constexpr ULONG kFirstInLine = 1;   // AddVectoredExceptionHandler: ahead of every other handler
constexpr DWORD kAvParams = 2;      // an access violation's record: the access, then its address
static std::atomic<int> g_cancelHits{0};
static PVOID g_cancelVeh = nullptr;
static LONG CALLBACK FxCancelHandler(EXCEPTION_POINTERS* ep) {
    const EXCEPTION_RECORD* er = ep->ExceptionRecord;
    if (er->ExceptionCode != EXCEPTION_ACCESS_VIOLATION || er->NumberParameters < kAvParams)
        return EXCEPTION_CONTINUE_SEARCH;
    const uintptr_t at = static_cast<uintptr_t>(er->ExceptionInformation[1]);
    const uintptr_t page = g_cancelPage;
    if (!page || at < page || at - page >= kPage) return EXCEPTION_CONTINUE_SEARCH;
    g_cancelHits.fetch_add(1);
    Tot::RequestPerCommand();
    return EXCEPTION_CONTINUE_SEARCH;
}
static void FxCancelTrap(bool on) {
    if (on && !g_cancelVeh) {
        g_cancelVeh = AddVectoredExceptionHandler(kFirstInLine, FxCancelHandler);
        if (!g_cancelVeh) FxFixtureFail("the cancel seam's exception handler could not be added");
    } else if (!on && g_cancelVeh) {
        RemoveVectoredExceptionHandler(g_cancelVeh);
        g_cancelVeh = nullptr;
    }
}
static int FxCancelHits() { return g_cancelHits.load(); }

// This thread bound to a connection's cancel flag, as Fern binds every handler thread: Tot::Requested() then reads
// that flag (and shutdown) and no longer the process-wide per-command one. Thread-local: bind, detect and unbind on
// the same thread.
static std::atomic<bool> g_connCancel{false};
static std::unique_ptr<Tot::ConnectionCancelScope> g_connScope;
static void FxBindConnection(bool raised) {
    g_connScope.reset();
    g_connCancel.store(raised);
    g_connScope = std::make_unique<Tot::ConnectionCancelScope>(&g_connCancel);
}
static void FxUnbindConnection() {
    g_connScope.reset();
    g_connCancel.store(false);
}

// ══ the recorders, and reading the log ══════════════════════════════════════════════════════════════════════════
// Aura's instance scan writes one INFO under PIPE:find per walk it finishes, and this WARN when Tot cut it short.
constexpr const char* kScanCat = "PIPE:find";
constexpr const char* kScanPrefix = "FindInstancesDerivedFrom base='PlayerController'";
constexpr const char* kScanAborted = "FindInstancesDerivedFrom: aborted (client gone / shutdown)";

static void FxOnLog(std::function<void(const FxLogLine&)> fn) {
    std::lock_guard<std::mutex> lk(g_logHookMu);
    g_onLog = std::move(fn);
}
// A scenario clears the log again after its last setup call: a lookup it made itself writes WalkClass lines.
static void FxClearLog() {
    std::lock_guard<std::mutex> lk(g_logMu);
    g_log.clear();
}
static void FxClearRecorders() {
    g_cancelHits.store(0);
    FxClearLog();
}
static size_t FxLogMark() {
    std::lock_guard<std::mutex> lk(g_logMu);
    return g_log.size();
}
static std::vector<FxLogLine> FxLogs(size_t from = 0) {
    std::lock_guard<std::mutex> lk(g_logMu);
    if (from >= g_log.size()) return {};
    return std::vector<FxLogLine>(g_log.begin() + static_cast<std::ptrdiff_t>(from), g_log.end());
}
// lvl 0 = any level; cat nullptr = any category.
static bool FxLogIs(const FxLogLine& l, char lvl, const char* cat) {
    return (!lvl || l.lvl == lvl) && (!cat || l.cat == cat);
}
static bool FxIsEdelLine(const FxLogLine& l) { return l.cat == kEdelCat && FxStartsWith(l.text, kEdelPrefix); }
static std::vector<FxLogLine> FxEdelLines(size_t from = 0) {
    std::vector<FxLogLine> out;
    for (auto& l : FxLogs(from))
        if (FxIsEdelLine(l)) out.push_back(std::move(l));
    return out;
}
static std::vector<FxLogLine> FxScanLines(size_t from = 0) {
    std::vector<FxLogLine> out;
    for (auto& l : FxLogs(from))
        if (l.cat == kScanCat && FxStartsWith(l.text, kScanPrefix)) out.push_back(std::move(l));
    return out;
}
// Lines whose text is exactly `text` / starts with `prefix`, at a level and in a category.
static int FxLogCount(const std::string& text, char lvl = 0, const char* cat = nullptr, size_t from = 0) {
    int n = 0;
    for (const auto& l : FxLogs(from))
        if (FxLogIs(l, lvl, cat) && l.text == text) ++n;
    return n;
}
static int FxLogPrefix(const std::string& prefix, char lvl = 0, const char* cat = nullptr, size_t from = 0) {
    int n = 0;
    for (const auto& l : FxLogs(from))
        if (FxLogIs(l, lvl, cat) && FxStartsWith(l.text, prefix.c_str())) ++n;
    return n;
}
// Edel's own lines only (category WALK, "Edel:").
static int FxEdelCount(const std::string& text, char lvl = 0, size_t from = 0) {
    int n = 0;
    for (const auto& l : FxEdelLines(from))
        if ((!lvl || l.lvl == lvl) && l.text == text) ++n;
    return n;
}
static int FxEdelPrefixCount(const std::string& prefix, char lvl = 0, size_t from = 0) {
    int n = 0;
    for (const auto& l : FxEdelLines(from))
        if ((!lvl || l.lvl == lvl) && FxStartsWith(l.text, prefix.c_str())) ++n;
    return n;
}
// The Edel lines, exactly these, in this order: level and text each, all under category WALK.
struct FxExpLine {
    char lvl;
    std::string text;
};
static bool FxEdelLinesAre(const std::vector<FxExpLine>& want, size_t from = 0) {
    const std::vector<FxLogLine> got = FxEdelLines(from);
    if (got.size() != want.size()) return false;
    for (size_t i = 0; i < got.size(); ++i)
        if (got[i].lvl != want[i].lvl || got[i].text != want[i].text || got[i].cat != kEdelCat) return false;
    return true;
}
static std::string FxLinesStr(const std::vector<FxLogLine>& lines) {
    std::string out = FxFmt("%zu line(s)", lines.size());
    for (const auto& l : lines) out += std::string("\n      ") + l.lvl + " [" + l.cat + "] " + l.text;
    return out;
}
static std::string FxLogDump(size_t from = 0, const char* cat = nullptr) {
    std::string out;
    for (const auto& l : FxLogs(from))
        if (!cat || l.cat == cat) out += std::string("\n      ") + l.lvl + " [" + l.cat + "] " + l.text;
    return out;
}

// ── Edel's lines, from its own format strings (%llX: uppercase hex, unpadded) ──
namespace FxLine {
static unsigned long long U(uintptr_t v) { return static_cast<unsigned long long>(v); }
// L1: the chain's controller, BEFORE the hop.
static std::string L1(uintptr_t pc) { return FxFmt("Edel: local PC 0x%llX via GWorld chain", U(pc)); }
// L2 .. L4: the instance scan's pick, by the Player it holds.
static std::string L2(uintptr_t pc) { return FxFmt("Edel: local PC 0x%llX via instance scan (Player set)", U(pc)); }
static std::string L3(uintptr_t pc) {
    return FxFmt("Edel: PC 0x%llX via instance scan (a remote Player; no local one found)", U(pc));
}
static std::string L4(uintptr_t pc) { return FxFmt("Edel: PC 0x%llX via instance scan WITHOUT a Player", U(pc)); }
// L5: once per completed detection -- the controller AFTER the hop, and the count after the cap.
static std::string L5(bool resolved, uintptr_t pc, uintptr_t pawn, size_t count) {
    return FxFmt("Edel: DetectCurrentTarget resolved=%d pc=0x%llX pawn=0x%llX candidates=%zu", resolved ? 1 : 0,
                 U(pc), U(pawn), count);
}
constexpr const char* kL5Prefix = "Edel: DetectCurrentTarget ";
}  // namespace FxLine

// ── Edel's notes, verbatim (the em dash is U+2014) ──
namespace FxNote {
constexpr const char* kN1 = "No local PlayerController found (not in gameplay?).";
constexpr const char* kN2 = "GWorld not available and no PlayerController found — open a level / let the game finish "
                            "loading, then retry.";
constexpr const char* kN3 = "PlayerController has no Pawn (menu / spectator?). No target to detect.";
constexpr const char* kN5 = "No clear current target (multiple / weak candidates). Showing best guesses — click one to "
                            "inspect; verify before trusting.";
constexpr const char* kN6 = "Player resolved but no target-like reference found. Lock onto / click an enemy, then "
                            "Detect again — or search the Instances tab for the enemy's class.";
constexpr const char* kCancelled = "Cancelled.";
// N4: the auto-pick.
static std::string N4(const std::string& name, const std::string& cls, int32_t score, const std::string& field) {
    return "Detected target: " + name + " (" + cls + ") — score " + std::to_string(score) + " via " + field + ".";
}
}  // namespace FxNote

// ── a candidate's reason, in Edel's words: field / is-X / infra-field / not-Actor, joined by ", " ──
namespace FxWhy {
constexpr const char* kIsPawn = "is-Pawn";
constexpr const char* kIsActor = "is-Actor";
constexpr const char* kInfra = "infra-field";
constexpr const char* kNotActor = "not-Actor";
static std::string Field(const std::string& name) { return "field '" + name + "'"; }
static std::string Join(std::initializer_list<std::string> parts) {
    std::string out;
    for (const auto& p : parts) {
        if (!out.empty()) out += ", ";
        out += p;
    }
    return out;
}
}  // namespace FxWhy

// ══ running Edel, and reading its candidates ════════════════════════════════════════════════════════════════════
using FxCands = std::vector<Edel::TargetCandidate>;

static Edel::TargetCandidate FxCand(uintptr_t addr, int32_t index, const std::string& name, const std::string& cls,
                                    int32_t score, uintptr_t src, const std::string& srcClass,
                                    const std::string& field, int32_t off, const std::string& reason) {
    Edel::TargetCandidate c;
    c.addr = addr;
    c.index = index;
    c.name = name;
    c.className = cls;
    c.score = score;
    c.sourceObject = src;
    c.sourceClass = srcClass;
    c.fieldName = field;
    c.fieldOffset = off;
    c.reason = reason;
    return c;
}
// The same, with the index, name and class of the target and the class of the source as the fixture planted them --
// read from its own table, never through Ubel -- so a check states only what Edel decides.
static Edel::TargetCandidate FxCandOf(uintptr_t addr, int32_t score, uintptr_t src, const std::string& field,
                                      int32_t off, const std::string& reason) {
    return FxCand(addr, FxIndexOf(addr), FxNameText(addr), FxClassNameText(addr), score, src, FxClassNameText(src),
                  field, off, reason);
}
static bool FxCandEq(const Edel::TargetCandidate& a, const Edel::TargetCandidate& b) {
    return a.addr == b.addr && a.index == b.index && a.name == b.name && a.className == b.className &&
           a.score == b.score && a.sourceObject == b.sourceObject && a.sourceClass == b.sourceClass &&
           a.fieldName == b.fieldName && a.fieldOffset == b.fieldOffset && a.reason == b.reason;
}
static std::string FxCandStr(const Edel::TargetCandidate& c) {
    const std::string off = c.fieldOffset < 0 ? FxS(c.fieldOffset) : FxHex(static_cast<uintptr_t>(c.fieldOffset));
    return FxFmt("{%s idx=%d name='%s' class='%s' score=%d src=%s srcClass='%s' field='%s' off=%s reason='%s'}",
                 FxHex(c.addr).c_str(), c.index, c.name.c_str(), c.className.c_str(), c.score,
                 FxHex(c.sourceObject).c_str(), c.sourceClass.c_str(), c.fieldName.c_str(), off.c_str(),
                 c.reason.c_str());
}
static std::string FxCandsStr(const FxCands& cs) {
    std::string out = FxFmt("%zu candidate(s)", cs.size());
    for (const auto& c : cs) out += "\n      " + FxCandStr(c);
    return out;
}
// Exactly these candidates, in this order, every field equal.
static bool FxCandsAre(const FxCands& got, const FxCands& want) {
    if (got.size() != want.size()) return false;
    for (size_t i = 0; i < got.size(); ++i)
        if (!FxCandEq(got[i], want[i])) return false;
    return true;
}
static const Edel::TargetCandidate* FxFindCand(const Edel::CurrentTargetResult& r, uintptr_t addr) {
    for (const auto& c : r.candidates)
        if (c.addr == addr) return &c;
    return nullptr;
}
static std::vector<uintptr_t> FxCandAddrs(const Edel::CurrentTargetResult& r) {
    std::vector<uintptr_t> out;
    for (const auto& c : r.candidates) out.push_back(c.addr);
    return out;
}
static bool FxAddrsAre(const Edel::CurrentTargetResult& r, const std::vector<uintptr_t>& want) {
    return FxCandAddrs(r) == want;
}
static std::string FxAddrsStr(const std::vector<uintptr_t>& addrs) {
    std::string out = "[";
    for (size_t i = 0; i < addrs.size(); ++i) {
        const std::string name = FxNameText(addrs[i]);
        out += (i ? ", " : "") + (name.empty() ? FxHex(addrs[i]) : name);
    }
    return out + "]";
}
static bool FxResultEq(const Edel::CurrentTargetResult& a, const Edel::CurrentTargetResult& b) {
    return a.resolved == b.resolved && a.world == b.world && a.playerController == b.playerController &&
           a.playerPawn == b.playerPawn && a.note == b.note && FxCandsAre(a.candidates, b.candidates);
}
static std::string FxResultStr(const Edel::CurrentTargetResult& r) {
    return FxFmt("resolved=%d world=%s pc=%s(%s) pawn=%s(%s) note='%s' ", r.resolved ? 1 : 0, FxHex(r.world).c_str(),
                 FxHex(r.playerController).c_str(), FxNameText(r.playerController).c_str(),
                 FxHex(r.playerPawn).c_str(), FxNameText(r.playerPawn).c_str(), r.note.c_str()) +
           FxCandsStr(r.candidates);
}

// The last result, kept for a block's checks to read. Main thread only: a concurrent caller keeps its own.
static Edel::CurrentTargetResult g_r;
static const Edel::CurrentTargetResult& FxDetect(int32_t maxCandidates = kDefaultMaxCandidates) {
    g_r = Edel::DetectCurrentTarget(maxCandidates);
    return g_r;
}
// The call with no argument at all: what Edel.h's default gives.
static const Edel::CurrentTargetResult& FxDetectNoArg() {
    g_r = Edel::DetectCurrentTarget();
    return g_r;
}
static std::vector<uintptr_t> FxFirstParty(int n) { return std::vector<uintptr_t>(g_party, g_party + n); }

// ══ RO.01: every byte Edel could reach ══════════════════════════════════════════════════════════════════════════
static void FxAppend(std::vector<uint8_t>& out, const void* p, size_t n) {
    const uint8_t* b = static_cast<const uint8_t*>(p);
    out.insert(out.end(), b, b + n);
}
// Every byte the fixture planted that Edel could read: a run that wrote anywhere in it shows as a difference.
static std::vector<uint8_t> FxMemSnapshot() {
    std::vector<uint8_t> s;
    FxAppend(s, g_objArena, sizeof g_objArena);
    FxAppend(s, g_bigArena, sizeof g_bigArena);
    FxAppend(s, g_fieldArena, sizeof g_fieldArena);
    FxAppend(s, g_fclassArena, sizeof g_fclassArena);
    FxAppend(s, g_nameEntry, sizeof g_nameEntry);
    FxAppend(s, g_nameChunk, sizeof g_nameChunk);
    FxAppend(s, g_nameChunks, sizeof g_nameChunks);
    FxAppend(s, g_poolItems, sizeof g_poolItems);
    FxAppend(s, g_poolChunkTable, sizeof g_poolChunkTable);
    FxAppend(s, g_poolHeader, sizeof g_poolHeader);
    FxAppend(s, &g_gworldSlot, sizeof g_gworldSlot);
    FxAppend(s, g_giTrapHost, sizeof g_giTrapHost);
    FxAppend(s, g_pcAckHost, sizeof g_pcAckHost);
    for (const FxBufDef& b : g_bufDefs) FxAppend(s, b.buf, b.count * sizeof(uintptr_t));
    return s;
}

// ══ PICK.05 / NOTE.06: the label N6 sends the user to ═══════════════════════════════════════════════════════════
// N6 names the Instances tab by the UI's current label, so the expectation is built from the label itself: a rename
// on either side then fails here. en.axaml is read as data, from the repository this file sits in. "" when the file
// or the key cannot be found -- a check built on it then fails, naming the path.
constexpr const char* kStringsRel = "../../ui/UE5DumpUI/Resources/Strings/en.axaml";
constexpr const char* kInstancesTabKey = "x:Key=\"str.Tab.InstanceFinder\">";
static std::string FxStringsPath() {
    const std::string self = __FILE__;
    const size_t cut = self.find_last_of("/\\");
    return (cut == std::string::npos ? std::string(".") : self.substr(0, cut)) + "/" + kStringsRel;
}
static std::string FxInstancesTabLabel() {
    FILE* f = nullptr;
    if (fopen_s(&f, FxStringsPath().c_str(), "rb") != 0 || !f) return std::string();
    std::string text;
    char buf[4096];
    size_t n = 0;
    while ((n = fread(buf, 1, sizeof buf, f)) > 0) text.append(buf, n);
    fclose(f);
    const size_t at = text.find(kInstancesTabKey);
    if (at == std::string::npos) return std::string();
    const size_t start = at + strlen(kInstancesTabKey);
    const size_t end = text.find('<', start);
    return end == std::string::npos ? std::string() : text.substr(start, end - start);
}

// ══ reset, build, cleanup ═══════════════════════════════════════════════════════════════════════════════════════
// Back to a clean slate, each undo after the call it undoes: the hook; the cancel seam and the bound connection, before
// Tot is lowered, so neither a late fault nor a bound flag can raise it again; Tot; FProperty mode; the class heads;
// every world object, buffer and trap; the pool; GWorld -> World_0; and the recorders LAST. A scenario does its setup
// after this, clears the log again after its last setup call, and only then runs Edel.
static void FxReset() {
    FxOnLog(nullptr);
    FxCancelTrap(false);
    FxUnbindConnection();
    Tot::ResetPerCommand();
    Tot::ResetShutdown();
    if (!DynOff::bUseFProperty) FxUpropMode(false);
    g_cachedUEVersion = kFixtureUEVersion;
    FxRestoreClasses();
    FxWorldDefault();
    FxPool(FxPoolVariant::Default);
    g_gworldSlot = g_world0;
    g_cachedGWorld = FxAddr(&g_gworldSlot);
    FxClearRecorders();
}

// Built once, before any Edel call.
static void FxBuildWorld() {
    // The offsets probe's decisions, made up front so no walk runs a GObjects vote: FName without the case-preserving
    // number, FProperty chains, the FunctionFlags offset decided with its tail right after it, UClass::Interfaces
    // measured -- and USTRUCT_SUPER moved off its default before the first class is built.
    DynOff::bCasePreservingName = false;
    DynOff::bUseFProperty = true;
    g_cachedUEVersion = kFixtureUEVersion;
    DynOff::UFUNCTION_FLAGS = kFnFlagsAt;
    DynOff::UFUNCTION_TAIL_EXTRA = 0;
    DynOff::UFUNCTION_TAIL_MEASURED.store(-1);
    DynOff::bUFunctionFlagsDetected.store(true);
    DynOff::UCLASS_INTERFACES.store(kUClassInterfacesAt);
    DynOff::USTRUCT_SUPER = kStructSuperAt;

    g_unreadablePage = FxAddr(VirtualAlloc(nullptr, kPage, MEM_RESERVE, PAGE_NOACCESS));
    g_cancelPage = FxAddr(VirtualAlloc(nullptr, kPage, MEM_RESERVE, PAGE_NOACCESS));
    if (!g_unreadablePage || !g_cancelPage) FxFixtureFail("a refused page could not be reserved");

    // ── metaclasses and packages ──
    g_metaClass = FxNewObjBlob();
    FxUObjectAt(g_metaClass, g_metaClass, "Class", 0, kObjBytes, false);
    g_metaPackage = FxReflObj(g_metaClass, "Package", 0);
    g_metaStruct = FxReflObj(g_metaClass, "ScriptStruct", 0);
    g_pkgEngine = FxReflObj(g_metaPackage, "/Script/Engine", 0);
    g_pkgOther = FxReflObj(g_metaPackage, "/Script/EngineOther", 0);
    g_pkgEngineU = FxReflObj(g_metaPackage, "/Script/EngineU", 0);

    if (kKwPosFields.size() != static_cast<size_t>(kKwPosCount) ||
        kKwNegFields.size() != static_cast<size_t>(kKwNegCount))
        FxFixtureFail("a keyword field list and its count disagree");
    FxBuildClasses();
    FxBuildObjects();
    FxPlantDefaults();
    FxSnapshotHeaders();
    FxSnapshotClasses();

    g_nameChunks[0] = FxAddr(g_nameChunk);
    Serie::InitUE4(FxAddr(g_nameChunks), kNameTextAt);

    FxReset();
}

// Before main returns: the clean slate, GWorld unset, and both refused pages released.
static void FxCleanup() {
    FxReset();
    g_cachedGWorld = 0;
    if (g_cancelPage) VirtualFree(reinterpret_cast<void*>(g_cancelPage), 0, MEM_RELEASE);
    g_cancelPage = 0;
    if (g_unreadablePage) VirtualFree(reinterpret_cast<void*>(g_unreadablePage), 0, MEM_RELEASE);
    g_unreadablePage = 0;
}

// ══ group 1: FIX, DEF, CLAMP, WORLD, CHAIN, SCAN ════════════════════════════════════════════════════════════════

namespace G1 {

// Edel's weights, as this group's expectations add them up: a positive field +50, is-Pawn +30, near combat +10 (the
// pawn root), a tracked InternalIndex +5, not-Actor -60.
constexpr int32_t kScoreDefTarget = 95;          // Enemy_0 through Hero_0's CurrentTarget: 50 + 30 + 10 + 5
constexpr int32_t kScoreNotActorTracked = -55;   // a UPlayer through a controller's neutral field: -60 + 5
constexpr int32_t kScorePartyPawn = 35;          // a party pawn through PC_Party's neutral Party: 30 + 5
constexpr int32_t kElementOffset = -1;           // the fieldOffset Edel gives a container element
constexpr int32_t kNoField = -1;                 // FindFieldOffset's answer for a field the class does not have
constexpr int32_t kNoElement = -1;               // an OutgoingPtr that is not a container element
// maxCandidates' ceiling as Edel.h documents it, written here rather than read from Edel.cpp.
constexpr int32_t kCandidateCeiling = 64;
constexpr size_t kDefCandidates = 2;             // the default world: Enemy_0, then LocalPlayer_0
// USTRUCT_SUPER's UE4.25+ default: a walk that hard-coded it would read the zero every fixture class holds there.
constexpr int kDefaultStructSuperAt = 0x40;
// The engine class every fixture debug camera derives from -- or, for DebugCameraControllerHUD_C, only names.
constexpr const char* kDebugCamClass = "DebugCameraController";
constexpr int32_t kTrapNum = 1;                  // GI_T's trap array holds one LocalPlayer
constexpr size_t kShownMismatches = 5;           // FIX.07 names this many mismatches in its `got`, then counts
// FIX.08's probes: a category no real writer uses, and a number that shows the format arguments were applied.
constexpr const char* kProbeCat = "FIX:probe";
constexpr int kProbeNumber = 7;

// ── formatting ──
static std::string OffStr(int32_t off) { return off < 0 ? FxS(off) : FxHex(static_cast<uintptr_t>(off)); }
static std::string NameOr0(uintptr_t obj) { return obj ? FxNameText(obj) : std::string("0"); }
// A result and the Edel lines behind it: what every end-to-end check prints when it fails.
static std::string Got(const Edel::CurrentTargetResult& r) {
    return FxResultStr(r) + "\n      Edel lines: " + FxLinesStr(FxEdelLines());
}
static std::string ScanLinesStr() { return FxLinesStr(FxScanLines()); }

// ── DEF.02's records and DEF.03's note, spelled out: the strings the C# stubs imitate are pinned here, never derived
//    from the fixture's own tables ──
static Edel::TargetCandidate DefEnemy() {
    return FxCand(g_enemy0, FxIndexOf(g_enemy0), "Enemy_0", "BP_Enemy_C", kScoreDefTarget, g_hero0, "BP_Hero_C",
                  "CurrentTarget", FxOff::kSlot0, "field 'CurrentTarget', is-Pawn");
}
static Edel::TargetCandidate DefPlayer() {
    return FxCand(g_lp0, kSlotLp0, "LocalPlayer_0", "LocalPlayer", kScoreNotActorTracked, g_pc0, "BP_GameCtrl_C",
                  "Player", FxOff::kPlayer, "not-Actor");
}
static FxCands DefRecords() { return { DefEnemy(), DefPlayer() }; }
// The dash built from its bytes, so the comparison does not lean on how this source was read.
static std::string DefNote() {
    return std::string("Detected target: Enemy_0 (BP_Enemy_C) ") + kEmDash + " score 95 via CurrentTarget.";
}

// ── FIX.01: one by-name read, with the arguments Edel (or Aura's scan) passes ──
struct NamedRead {
    const char* what;       // the read, as the label names it
    uintptr_t cls;
    const char* exact;
    const char* contains;   // nullptr: the exact name only
    const char* type;       // nullptr: any type
    int32_t want;           // kNoField: the class has no such field
};
static void CheckNamedRead(const NamedRead& n, std::unordered_set<int32_t>& offsets, size_t& named) {
    const int32_t got = Ubel::FindFieldOffset(n.cls, n.exact, n.contains, nullptr, n.type);
    const std::string want = n.want < 0 ? std::string("-1 (no such field)") : FxHex(static_cast<uintptr_t>(n.want));
    check(FxFmt("FIX.01 named offsets: %s reads %s", n.what, want.c_str()), got == n.want, OffStr(got));
    if (n.want < 0) return;
    ++named;
    offsets.insert(got);
}

// ── FIX.03: the edges Aura hands Edel ──
static bool EdgeIs(const Aura::OutgoingPtr& e, uintptr_t target, int32_t off, const char* field, int32_t elem) {
    return e.target == target && e.fieldOffset == off && e.fieldName == field && e.elementIndex == elem;
}
static const Aura::OutgoingPtr* FindEdge(const std::vector<Aura::OutgoingPtr>& es, uintptr_t target) {
    for (const auto& e : es)
        if (e.target == target) return &e;
    return nullptr;
}
static std::string EdgeStr(const Aura::OutgoingPtr& e) {
    return FxFmt("{%s %s off=%s field='%s' elem=%d}", FxNameText(e.target).c_str(), FxHex(e.target).c_str(),
                 OffStr(e.fieldOffset).c_str(), e.fieldName.c_str(), e.elementIndex);
}
// The first `head` edges and the last one: a 1024-edge list printed whole would bury the line that matters.
static std::string EdgesStr(const std::vector<Aura::OutgoingPtr>& es, size_t head) {
    std::string out = FxFmt("%zu edge(s)", es.size());
    for (size_t i = 0; i < es.size() && i < head; ++i) out += FxFmt("\n      [%zu] ", i) + EdgeStr(es[i]);
    if (es.size() > head) out += FxFmt("\n      [%zu] ", es.size() - 1) + EdgeStr(es.back());
    return out;
}

// ── FIX.04: Aura's scan, called directly ──
static const char* PickName(Aura::LocalPcPick p) {
    switch (p) {
    case Aura::LocalPcPick::None: return "None";
    case Aura::LocalPcPick::Cancelled: return "Cancelled";
    case Aura::LocalPcPick::LocalPlayer: return "LocalPlayer";
    case Aura::LocalPcPick::OtherPlayer: return "OtherPlayer";
    case Aura::LocalPcPick::NoPlayer: return "NoPlayer";
    }
    return "?";
}
static void ExpectLocalPcScan(const char* how, uintptr_t wantPc, Aura::LocalPcPick wantPick) {
    const Aura::LocalPcScan s = Aura::ScanForLocalPlayerController();
    check(FxFmt("FIX.04 scan: %s answers {%s, %s}", how, NameOr0(wantPc).c_str(), PickName(wantPick)),
          s.pc == wantPc && s.pick == wantPick, NameOr0(s.pc) + " " + FxHex(s.pc) + " " + PickName(s.pick));
}

// ── FIX.08: FxExitCode's verdict for counters reading `pass` and `fail`. Its printout goes to NUL, so a probe of the
//    "nothing passed" rule does not print the FAIL line a real empty run prints; the run's counters go back after ──
static int ExitCodeFor(int pass, int fail) {
    const int runPass = g_pass.exchange(pass), runFail = g_fail.exchange(fail);
    fflush(stdout);
    const int out = _fileno(stdout);
    const int saved = _dup(out);
    FILE* nul = nullptr;
    if (saved >= 0 && fopen_s(&nul, "NUL", "w") == 0 && nul) _dup2(_fileno(nul), out);
    const int code = FxExitCode();
    fflush(stdout);
    if (saved >= 0) {
        _dup2(saved, out);
        _close(saved);
    }
    if (nul) fclose(nul);
    g_pass.store(runPass);
    g_fail.store(runFail);
    return code;
}

// ── CLAMP: the first n party pawns as Edel lists them -- each an element of PC_Party's Party, bare is-Pawn ──
static FxCands PartyRecords(size_t n) {
    FxCands out;
    for (size_t i = 0; i < n && i < static_cast<size_t>(kPartySize); ++i)
        out.push_back(FxCandOf(g_party[i], kScorePartyPawn, g_pcParty, "Party", kElementOffset, FxWhy::kIsPawn));
    return out;
}
// One call over the party: exactly the first `want` pawns in address order; CLAMP.06 when two or more come back.
static void ExpectParty(const char* id, const char* arg, const Edel::CurrentTargetResult& r, size_t want) {
    check(FxFmt("%s: DetectCurrentTarget(%s) lists exactly Party_00 .. Party_%02zu, each {35, PC_Party 'PcParty', "
                "'Party' -1, 'is-Pawn'}", id, arg, want - 1),
          FxCandsAre(r.candidates, PartyRecords(want)), FxCandsStr(r.candidates));
    if (want < kDefCandidates) return;
    check(FxFmt("CLAMP.06 notes: DetectCurrentTarget(%s), %zu equal tops, gives resolved false and N5", arg, want),
          !r.resolved && r.note == FxNote::kN5, Got(r));
}

// ── WORLD: no world Edel can read ──
// WORLD.01a's expectations: the scan finds PC_0 and detection runs to the end with world 0.
static Edel::CurrentTargetResult ExpectNoWorldScan(const char* prefix) {
    const Edel::CurrentTargetResult r = FxDetect();
    check(FxFmt("%s: world 0, playerController PC_0, playerPawn Hero_0", prefix),
          r.world == 0 && r.playerController == g_pc0 && r.playerPawn == g_hero0, Got(r));
    check(FxFmt("%s: resolved, with DEF.02's two records", prefix),
          r.resolved && FxCandsAre(r.candidates, DefRecords()), Got(r));
    check(FxFmt("%s: the Edel lines are INFO L2 naming PC_0, then L5", prefix),
          FxEdelLinesAre({ { 'I', FxLine::L2(g_pc0) }, { 'I', FxLine::L5(true, g_pc0, g_hero0, kDefCandidates) } }),
          FxLinesStr(FxEdelLines()));
    check(FxFmt("%s: the instance scan ran once", prefix), FxScanLines().size() == 1u, ScanLinesStr());
    return r;
}
// The same with nothing for the scan to take: N2, and every chain field of the result left 0.
static void ExpectNoWorldNoPc(const char* prefix) {
    const Edel::CurrentTargetResult r = FxDetect();
    check(FxFmt("%s: note N2 verbatim", prefix), r.note == FxNote::kN2, r.note);
    check(FxFmt("%s: world, playerController and playerPawn all 0; no candidates; unresolved", prefix),
          r.world == 0 && r.playerController == 0 && r.playerPawn == 0 && r.candidates.empty() && !r.resolved,
          Got(r));
    check(FxFmt("%s: no Edel line", prefix), FxEdelLines().empty(), FxLinesStr(FxEdelLines()));
    check(FxFmt("%s: the instance scan ran once, and found nothing", prefix), FxScanLines().size() == 1u,
          ScanLinesStr());
}

// ── CHAIN ──
// A broken link: Edel falls to the instance scan, which takes PC_0 -- never the PC_Alt every variant ends at.
static Edel::CurrentTargetResult ExpectFallsToScan(const char* prefix) {
    const Edel::CurrentTargetResult r = FxDetect();
    check(FxFmt("%s: the instance scan ran once", prefix), FxScanLines().size() == 1u, ScanLinesStr());
    check(FxFmt("%s: the Edel lines are INFO L2 naming PC_0, then L5", prefix),
          FxEdelLinesAre({ { 'I', FxLine::L2(g_pc0) }, { 'I', FxLine::L5(true, g_pc0, g_hero0, kDefCandidates) } }),
          FxLinesStr(FxEdelLines()));
    check(FxFmt("%s: playerController PC_0, playerPawn Hero_0", prefix),
          r.playerController == g_pc0 && r.playerPawn == g_hero0, Got(r));
    return r;
}
// A lookup's arm that only a correct rule takes: the chain itself reaches PC_Alt, which the scan could never give.
static void ExpectChainReachesAlt(const char* prefix) {
    const Edel::CurrentTargetResult r = FxDetect();
    check(FxFmt("%s: the Edel lines are INFO L1 naming PC_Alt, then L5 (unresolved, no candidates)", prefix),
          FxEdelLinesAre({ { 'I', FxLine::L1(g_pcAlt) }, { 'I', FxLine::L5(false, g_pcAlt, g_heroAlt, 0) } }),
          FxLinesStr(FxEdelLines()));
    check(FxFmt("%s: playerController PC_Alt, playerPawn Hero_Alt", prefix),
          r.playerController == g_pcAlt && r.playerPawn == g_heroAlt, Got(r));
    check(FxFmt("%s: the instance scan never ran", prefix), FxScanLines().empty(), ScanLinesStr());
}

// ── SCAN.05: the first line of a category and level whose text starts with `prefix`; 0 when there is none ──
static uint64_t FirstSeq(const char* cat, char lvl, const std::string& prefix) {
    for (const auto& l : FxLogs())
        if (l.cat == cat && (!lvl || l.lvl == lvl) && FxStartsWith(l.text, prefix.c_str())) return l.seq;
    return 0;
}

// ══ FIX ═════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockFix() {
    blk("FIX - fixture self-checks: the named offsets, the moved super, the edges, the scan, the traps, the names, "
        "the recorders");
    FxReset();

    // FIX.01: each by-name read at its planned offset, every decoy passed over, no two reads sharing an offset.
    {
        const NamedRead fprop[] = {
            { "World.OwningGameInstance", g_cWorld, "OwningGameInstance", "GameInstance", kObjectProperty,
              FxOff::kOwningGameInstance },
            { "WorldShadow.OwningGameInstance (exact, past the decoy at 0x78)", g_cWorldShadow, "OwningGameInstance",
              "GameInstance", kObjectProperty, FxOff::kShadowGameInstance },
            { "WorldFuzzy's fuzzy GameInstance (past the array decoy at 0x88)", g_cWorldFuzzy, "OwningGameInstance",
              "GameInstance", kObjectProperty, FxOff::kMyGameInstance },
            { "GameInstance.LocalPlayers", g_cGameInstance, "LocalPlayers", "LocalPlayers", kArrayProperty,
              FxOff::kLocalPlayers },
            { "GameInstanceFuzzy's fuzzy LocalPlayers (past the int decoy at 0xB0)", g_cGameInstanceFuzzy,
              "LocalPlayers", "LocalPlayers", kArrayProperty, FxOff::kLocalPlayersArray },
            { "LocalPlayer.PlayerController", g_cLocalPlayer, "PlayerController", "PlayerController", kObjectProperty,
              FxOff::kPlayerController },
            { "LocalPlayerFuzzy's fuzzy PlayerController (past the class decoy at 0xD0)", g_cLocalPlayerFuzzy,
              "PlayerController", "PlayerController", kObjectProperty, FxOff::kPlayerControllerRef },
            { "BP_GameCtrl_C.Pawn", g_cGameCtrl, "Pawn", nullptr, nullptr, FxOff::kPawn },
            { "BP_GameCtrl_C.AcknowledgedPawn", g_cGameCtrl, "AcknowledgedPawn", nullptr, nullptr, FxOff::kAckPawn },
            { "BP_GameCtrl_C.Player", g_cGameCtrl, "Player", nullptr, nullptr, FxOff::kPlayer },
            { "DebugCameraController.OriginalControllerRef", g_cDcc, "OriginalControllerRef", "OriginalController",
              kObjectProperty, FxOff::kOriginalController },
            { "BP_DccFuzzy_C's fuzzy OriginalController (past the class decoy at 0x110)", g_cDccFuzzy,
              "OriginalControllerRef", "OriginalController", kObjectProperty, FxOff::kOriginalControllerPtr },
            { "DebugCameraControllerHUD_C.OriginalControllerRef", g_cDccHud, "OriginalControllerRef",
              "OriginalController", kObjectProperty, FxOff::kHudOriginalController },
            { "BP_PcAckOnly_C.Pawn", g_cPcAckOnly, "Pawn", nullptr, nullptr, kNoField },
            { "BP_PcAckOnly_C.AcknowledgedPawn", g_cPcAckOnly, "AcknowledgedPawn", nullptr, nullptr,
              FxOff::kAckOnlyAckPawn },
            { "PcExcl.AcknowledgedPawn", g_cPcExcl, "AcknowledgedPawn", nullptr, nullptr, FxOff::kExclAckPawn },
        };
        const NamedRead uprop[] = {
            { "WorldU.OwningGameInstance (UProperty)", g_cWorldU, "OwningGameInstance", "GameInstance",
              kObjectProperty, FxOff::kOwningGameInstanceU },
            { "GameInstanceU.LocalPlayers (UProperty)", g_cGameInstanceU, "LocalPlayers", "LocalPlayers",
              kArrayProperty, FxOff::kLocalPlayersU },
            { "LocalPlayerU.PlayerController (UProperty)", g_cLocalPlayerU, "PlayerController", "PlayerController",
              kObjectProperty, FxOff::kPlayerControllerU },
            { "PlayerControllerU.Pawn (UProperty)", g_cPcU, "Pawn", nullptr, nullptr, FxOff::kPawnU },
            { "PlayerControllerU.AcknowledgedPawn (UProperty)", g_cPcU, "AcknowledgedPawn", nullptr, nullptr,
              FxOff::kAckPawnU },
            { "PlayerControllerU.Player (UProperty)", g_cPcU, "Player", nullptr, nullptr, FxOff::kPlayerU },
        };
        std::unordered_set<int32_t> offsets;
        size_t named = 0;
        for (const auto& n : fprop) CheckNamedRead(n, offsets, named);
        FxUpropMode(true);
        for (const auto& n : uprop) CheckNamedRead(n, offsets, named);
        FxUpropMode(false);
        check("FIX.01 named offsets: no two named reads share an offset", offsets.size() == named,
              FxFmt("%zu distinct offset(s) over %zu read(s)", offsets.size(), named));
    }

    // FIX.02: every class keeps its super at 0x30, so a walk that took the 0x40 default would read a zero.
    {
        check("FIX.02 moved super: DynOff::USTRUCT_SUPER is 0x30, off its 0x40 default",
              DynOff::USTRUCT_SUPER == kStructSuperAt && kStructSuperAt != kDefaultStructSuperAt,
              FxHex(static_cast<uintptr_t>(DynOff::USTRUCT_SUPER)));
        std::string nonZero;
        for (uintptr_t c : g_allClasses)
            if (FxGetPtr(c, kDefaultStructSuperAt) != 0) nonZero += " " + FxNameText(c);
        check("FIX.02 moved super: every fixture class reads 0 at +0x40", !g_allClasses.empty() && nonZero.empty(),
              nonZero);
        const ClassInfo ci = Ubel::WalkClass(g_cGameCtrl);
        const uintptr_t controllersPawn = FxGetPtr(g_cController, DynOff::USTRUCT_CHILDPROPS);
        const FieldInfo* pawn = nullptr;
        for (const auto& f : ci.Fields)
            if (f.Name == "Pawn") pawn = &f;
        check("FIX.02 moved super: WalkClass(BP_GameCtrl_C) lists Controller's own Pawn field at 0xE0, two supers up",
              ci.SuperClass == g_cPlayerController && pawn && pawn->Offset == FxOff::kPawn &&
                  pawn->Address == controllersPawn,
              pawn ? FxFmt("Pawn at %s from field %s; super %s", OffStr(pawn->Offset).c_str(),
                           FxHex(pawn->Address).c_str(), FxNameText(ci.SuperClass).c_str())
                   : FxFmt("no Pawn among %zu field(s)", ci.Fields.size()));
    }

    // FIX.03: Aura's edges in the order Edel's first-wins and cap rules depend on.
    {
        std::vector<Aura::OutgoingPtr> hero;
        Aura::CollectOutgoingObjectPtrs(g_hero0, hero, kEdgeCap);
        check("FIX.03 edges: Hero_0 gives exactly [{PC_0, 0x130, 'Controller'}, {Enemy_0, 0x200, 'CurrentTarget'}]",
              hero.size() == 2u && EdgeIs(hero[0], g_pc0, FxOff::kPawnController, "Controller", kNoElement) &&
                  EdgeIs(hero[1], g_enemy0, FxOff::kSlot0, "CurrentTarget", kNoElement),
              EdgesStr(hero, hero.size()));
        constexpr size_t kHeadShown = 3;
        std::vector<Aura::OutgoingPtr> horde;
        Aura::CollectOutgoingObjectPtrs(g_pcH, horde, kEdgeCap);
        const size_t cap = static_cast<size_t>(kEdgeCap);
        check("FIX.03 edges: PC_H gives exactly 1024 edges", horde.size() == cap, EdgesStr(horde, kHeadShown));
        check("FIX.03 edges: PC_H's first two are Hero_H through Pawn (0xE0), then AcknowledgedPawn (0xE8)",
              horde.size() >= 2u && EdgeIs(horde[0], g_heroH, FxOff::kPawn, "Pawn", kNoElement) &&
                  EdgeIs(horde[1], g_heroH, FxOff::kAckPawn, "AcknowledgedPawn", kNoElement),
              EdgesStr(horde, kHeadShown));
        check("FIX.03 edges: PC_H's 1024th edge is Pawn_In, element 1021 of Crowd (header 0x200)",
              horde.size() == cap && EdgeIs(horde[cap - 1], g_pawnIn, FxOff::kSlot0, "Crowd", kHordeFillers),
              EdgesStr(horde, kHeadShown));
        std::vector<Aura::OutgoingPtr> cand;
        Aura::CollectOutgoingObjectPtrs(g_heroC, cand, kEdgeCap);
        const Aura::OutgoingPtr* nested = FindEdge(cand, g_enemyN);
        const Aura::OutgoingPtr* weak = FindEdge(cand, g_enemyW);
        check("FIX.03 edges: Hero_C gives Enemy_N through 'LockOn.CurrentTarget' at 0x208",
              nested && EdgeIs(*nested, g_enemyN, FxOff::kLockOnTarget, "LockOn.CurrentTarget", kNoElement),
              EdgesStr(cand, cand.size()));
        check("FIX.03 edges: Hero_C gives Enemy_W through the weak 'LockedOnTarget' at 0x210",
              weak && EdgeIs(*weak, g_enemyW, FxOff::kLockedOnTarget, "LockedOnTarget", kNoElement),
              EdgesStr(cand, cand.size()));
    }

    // FIX.04: the scan's pick for each pool variant; each one, and Tot, put back after.
    {
        FxReset();
        ExpectLocalPcScan("pool Default", g_pc0, Aura::LocalPcPick::LocalPlayer);
        FxReset();
        FxPool(FxPoolVariant::NoPc0);
        ExpectLocalPcScan("pool NoPc0", g_pcRemote, Aura::LocalPcPick::OtherPlayer);
        FxReset();
        FxPool(FxPoolVariant::NoPc0NoRemote);
        ExpectLocalPcScan("pool NoPc0NoRemote", g_pcNoPlayer, Aura::LocalPcPick::NoPlayer);
        FxReset();
        FxPool(FxPoolVariant::IdleOnly);
        ExpectLocalPcScan("pool IdleOnly", 0, Aura::LocalPcPick::None);
        FxReset();
        FxPool(FxPoolVariant::ActiveDcc);
        ExpectLocalPcScan("pool ActiveDcc", g_dbgPc0, Aura::LocalPcPick::LocalPlayer);
        FxReset();
        Tot::RequestPerCommand();
        ExpectLocalPcScan("pool Default with Tot raised", 0, Aura::LocalPcPick::Cancelled);
        Tot::ResetPerCommand();
        FxReset();
    }

    // FIX.05: derivation, never the name.
    {
        const std::unordered_set<std::string> dcc = { kDebugCamClass };
        check("FIX.05 derivation: BP_FreeCam_C, BP_DccFuzzy_C and DebugCameraController derive from "
              "DebugCameraController",
              Aura::ClassDerivesFromAny(g_cFreeCam, dcc) && Aura::ClassDerivesFromAny(g_cDccFuzzy, dcc) &&
                  Aura::ClassDerivesFromAny(g_cDcc, dcc));
        check("FIX.05 derivation: DebugCameraControllerHUD_C and BP_GameCtrl_C do not",
              !Aura::ClassDerivesFromAny(g_cDccHud, dcc) && !Aura::ClassDerivesFromAny(g_cGameCtrl, dcc));
    }

    // FIX.06: the offset -1 traps hold what a read there would take; both reserved pages refuse.
    {
        uintptr_t trapPtr = 0;
        const bool ptrRead = Macht::ReadSafe(g_pcAck0 - static_cast<uintptr_t>(kTrapLead), trapPtr);
        check("FIX.06 traps: the 8 bytes at PcAck_0-1 read Trap_0", ptrRead && trapPtr == g_trap0,
              FxHex(trapPtr) + " " + FxNameText(trapPtr));
        Macht::TArrayView arr;
        const bool arrRead = Macht::ReadTArray(g_giT - static_cast<uintptr_t>(kTrapLead), arr);
        check("FIX.06 traps: a TArray read at GI_T-1 gives {GiTrapBuf, 1, 256}, and GiTrapBuf[0] is LocalPlayer_T",
              arrRead && arr.Data == FxAddr(g_giTrapBuf) && arr.Count == kTrapNum && arr.Max == kTrapMax &&
                  g_giTrapBuf[0] == g_lpT,
              FxFmt("read=%d {%s, %d, %d}", arrRead ? 1 : 0, FxHex(arr.Data).c_str(), arr.Count, arr.Max));
        uintptr_t probe = 0;
        const bool unreadableRead = Macht::ReadSafe(g_unreadablePage, probe);
        const bool cancelRead = Macht::ReadSafe(g_cancelPage, probe);
        check("FIX.06 traps: ReadSafe refuses both reserved pages, and they are two pages",
              !unreadableRead && !cancelRead && g_unreadablePage && g_cancelPage && g_unreadablePage != g_cancelPage,
              FxFmt("unreadable=%s read=%d cancel=%s read=%d", FxHex(g_unreadablePage).c_str(), unreadableRead ? 1 : 0,
                    FxHex(g_cancelPage).c_str(), cancelRead ? 1 : 0));
        check("FIX.06 traps: with the seam off, a touch of the cancel page counts no hit and raises no cancel",
              FxCancelHits() == 0 && !Tot::Requested(), FxS(FxCancelHits()));
    }

    // FIX.07: Ubel reads back every planted name, and the names this group quotes are the planted ones.
    {
        size_t bad = 0;
        std::string shown;
        for (uintptr_t o : FxAllObjects()) {
            const std::string readBack = Ubel::GetName(o);
            if (readBack == FxNameText(o)) continue;
            if (++bad <= kShownMismatches)
                shown += FxFmt(" %s:'%s'/'%s'", FxHex(o).c_str(), readBack.c_str(), FxNameText(o).c_str());
        }
        check("FIX.07 names: Ubel::GetName answers the planted FName of every object and class the fixture built",
              !FxAllObjects().empty() && bad == 0, FxFmt("%zu mismatch(es):", bad) + shown);
        const std::pair<uintptr_t, const char*> quoted[] = {
            { g_enemy0, "Enemy_0" }, { g_cEnemy, "BP_Enemy_C" }, { g_hero0, "Hero_0" }, { g_cHero, "BP_Hero_C" },
            { g_lp0, "LocalPlayer_0" }, { g_cLocalPlayer, "LocalPlayer" }, { g_pc0, "PC_0" },
            { g_cGameCtrl, "BP_GameCtrl_C" }, { g_cPcParty, "PcParty" }, { g_cParty, "BP_Party_C" },
            { g_party[0], "Party_00" }, { g_netConn0, "NetConnection_0" }, { g_cNetConnection, "NetConnection" },
            { g_widget0, "WBP_PlayerControllerHUD_C_0" }, { g_cWidgetHud, "WBP_PlayerControllerHUD_C" },
        };
        std::string wrong;
        for (const auto& q : quoted) {
            const std::string readBack = Ubel::GetName(q.first);
            if (readBack != q.second) wrong += FxFmt(" '%s' read '%s'", q.second, readBack.c_str());
        }
        check("FIX.07 names: every name this group's checks quote reads back as quoted", wrong.empty(), wrong);
    }

    // FIX.08: the recorders and the verdict every other check relies on.
    {
        check("FIX.08 harness: Edel.cpp's LOG_CAT is \"WALK\", the literal every Edel-line reader compares against",
              strcmp(kWalkCat, "WALK") == 0 && strcmp(kEdelCat, "WALK") == 0, kWalkCat);
        const size_t warnMark = FxLogMark();
        Sein::Warn(kProbeCat, "FIX.08 probe %s %d", "warn", kProbeNumber);
        const std::vector<FxLogLine> warn = FxLogs(warnMark);
        check("FIX.08 harness: one Sein::Warn records one line at level 'W' with its category and formatted text",
              warn.size() == 1u && warn[0].lvl == 'W' && warn[0].cat == kProbeCat &&
                  warn[0].text == FxFmt("FIX.08 probe warn %d", kProbeNumber),
              FxLinesStr(warn));

        // The probes below are the harness's, not Edel's: the run-wide counters LOG.07 reads go back after them.
        const int linesRun = g_edelLinesRun.load(), errorOrDebugRun = g_edelErrorOrDebugRun.load();
        const size_t edelMark = FxLogMark();
        Sein::Info(kEdelCat, "Edel: FIX.08 probe");
        Sein::Info(kEdelCat, "WalkClass: FIX.08 probe");
        Sein::Info(kProbeCat, "Edel: FIX.08 probe");
        Sein::Debug(kEdelCat, "Edel: FIX.08 debug probe");
        const std::vector<FxLogLine> edel = FxEdelLines(edelMark);
        check("FIX.08 harness: FxEdelLines keeps the WALK lines that start \"Edel:\", and no WalkClass line nor an "
              "\"Edel:\" line of another category",
              edel.size() == 2u && edel[0].lvl == 'I' && edel[0].text == "Edel: FIX.08 probe" && edel[1].lvl == 'D',
              FxLinesStr(edel));
        const int linesDelta = g_edelLinesRun.load() - linesRun;
        const int errorOrDebugDelta = g_edelErrorOrDebugRun.load() - errorOrDebugRun;
        check("FIX.08 harness: the run-wide counters count both Edel lines, and the DEBUG one as an E/D line",
              linesDelta == 2 && errorOrDebugDelta == 1, FxFmt("lines +%d, E/D +%d", linesDelta, errorOrDebugDelta));
        g_edelLinesRun.store(linesRun);
        g_edelErrorOrDebugRun.store(errorOrDebugRun);

        const int none = ExitCodeFor(0, 0), clean = ExitCodeFor(1, 0), failed = ExitCodeFor(1, 1);
        check("FIX.08 harness: FxExitCode is non-zero for a run in which no check passed", none != 0, FxS(none));
        check("FIX.08 harness: FxExitCode is 0 for passes with no failure, and non-zero with one",
              clean == 0 && failed != 0, FxFmt("clean=%d failed=%d", clean, failed));
    }
}

// ══ DEF ═════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockDef() {
    blk("DEF - the default world: the result, both records, the note and the lines the C# stubs imitate");
    FxReset();
    FxClearLog();
    const Edel::CurrentTargetResult r = FxDetect(kDefaultMaxCandidates);
    check("DEF.01 result: resolved", r.resolved, Got(r));
    check("DEF.01 result: world World_0, playerController PC_0, playerPawn Hero_0",
          r.world == g_world0 && r.playerController == g_pc0 && r.playerPawn == g_hero0, Got(r));
    check("DEF.01 result: two candidates", r.candidates.size() == kDefCandidates, Got(r));
    check("DEF.02 records: [0] is Enemy_0 {its index, 'BP_Enemy_C', 95, Hero_0 'BP_Hero_C', 'CurrentTarget' 0x200, "
          "\"field 'CurrentTarget', is-Pawn\"}",
          !r.candidates.empty() && FxCandEq(r.candidates[0], DefEnemy()), FxCandsStr(r.candidates));
    check("DEF.02 records: [1] is LocalPlayer_0 {6, 'LocalPlayer', -55, PC_0 'BP_GameCtrl_C', 'Player' 0xF0, "
          "'not-Actor'}",
          r.candidates.size() >= kDefCandidates && FxCandEq(r.candidates[1], DefPlayer()), FxCandsStr(r.candidates));
    check("DEF.03 note: \"Detected target: Enemy_0 (BP_Enemy_C) <U+2014> score 95 via CurrentTarget.\" byte for byte",
          r.note == DefNote(), r.note);
    check("DEF.04 log: the Edel lines are INFO/WALK L1 naming PC_0, then INFO/WALK L5 (resolved=1, 2 candidates)",
          FxEdelLinesAre({ { 'I', FxLine::L1(g_pc0) }, { 'I', FxLine::L5(true, g_pc0, g_hero0, kDefCandidates) } }),
          FxLinesStr(FxEdelLines()));
    check("DEF.04 log: no instance-scan line", FxScanLines().empty(), ScanLinesStr());

    const Edel::CurrentTargetResult noArg = FxDetectNoArg();
    check("DEF.05 default argument: DetectCurrentTarget() equals DetectCurrentTarget(8) field for field",
          FxResultEq(noArg, r), FxResultStr(noArg) + "\n    against " + FxResultStr(r));
}

// ══ CLAMP ═══════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockClamp() {
    blk("CLAMP - maxCandidates: 0 or less means 8, 1 to 64 kept, above 64 is 64, the count logged after the cut");
    FxReset();
    FxLpController(g_pcParty);
    const size_t def = static_cast<size_t>(kDefaultMaxCandidates);
    const size_t ceiling = static_cast<size_t>(kCandidateCeiling);

    FxClearLog();
    {
        const Edel::CurrentTargetResult r = FxDetect(0);
        ExpectParty("CLAMP.01a 0 -> 8", "0", r, def);
        check("CLAMP.01a 0 -> 8: the first record reads 'Party_00', 'BP_Party_C', from 'PcParty' through 'Party'",
              !r.candidates.empty() && r.candidates[0].name == "Party_00" &&
                  r.candidates[0].className == "BP_Party_C" && r.candidates[0].sourceClass == "PcParty" &&
                  r.candidates[0].fieldName == "Party" && r.candidates[0].reason == "is-Pawn",
              FxCandsStr(r.candidates));
        check("CLAMP.05 post-truncation count: the 0 call's lines are L1 naming PC_Party, then L5 ending "
              "'candidates=8'",
              FxEdelLinesAre({ { 'I', FxLine::L1(g_pcParty) },
                               { 'I', FxLine::L5(false, g_pcParty, g_heroParty, def) } }),
              FxLinesStr(FxEdelLines()));
    }
    ExpectParty("CLAMP.01b -1 -> 8", "-1", FxDetect(-1), def);
    ExpectParty("CLAMP.01b INT_MIN -> 8", "INT_MIN", FxDetect(INT_MIN), def);
    ExpectParty("CLAMP.02a 1 stays 1", "1", FxDetect(1), 1u);
    ExpectParty("CLAMP.02b 64 stays 64", "64", FxDetect(kCandidateCeiling), ceiling);

    FxClearLog();
    {
        const Edel::CurrentTargetResult r = FxDetect(kCandidateCeiling + 1);
        ExpectParty("CLAMP.03 cap", "65", r, ceiling);
        check("CLAMP.05 post-truncation count: the 65 call's lines are L1 naming PC_Party, then L5 ending "
              "'candidates=64'",
              FxEdelLinesAre({ { 'I', FxLine::L1(g_pcParty) },
                               { 'I', FxLine::L5(false, g_pcParty, g_heroParty, ceiling) } }),
              FxLinesStr(FxEdelLines()));
    }
    ExpectParty("CLAMP.03 cap", "INT_MAX", FxDetect(INT_MAX), ceiling);
    ExpectParty("CLAMP.04 default", "", FxDetectNoArg(), def);
}

// ══ WORLD ═══════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockWorld() {
    blk("WORLD - GWorld unset, unreadable or empty: the scan still resolves; the world kept on every later failure");

    FxReset();
    FxNoGWorld();
    FxClearLog();
    {
        const Edel::CurrentTargetResult r = ExpectNoWorldScan("WORLD.01a no GWorld, scan resolves");
        check("CHAIN.10 world 0 continues: with world 0 detection runs to its end -- one L5, resolved",
              r.world == 0 && r.resolved && FxEdelPrefixCount(FxLine::kL5Prefix, 'I') == 1, Got(r));
    }
    FxReset();
    FxNoGWorld();
    FxPool(FxPoolVariant::IdleOnly);
    FxClearLog();
    ExpectNoWorldNoPc("WORLD.01b no GWorld, nothing to scan");

    FxReset();
    FxGWorldAt(g_unreadablePage);
    FxClearLog();
    ExpectNoWorldScan("WORLD.02 unreadable slot");
    FxReset();
    FxGWorldAt(g_unreadablePage);
    FxPool(FxPoolVariant::IdleOnly);
    FxClearLog();
    ExpectNoWorldNoPc("WORLD.02 unreadable slot, pool IdleOnly");

    FxReset();
    FxWorld(0);
    FxClearLog();
    ExpectNoWorldScan("WORLD.03 empty slot");
    FxReset();
    FxWorld(0);
    FxPool(FxPoolVariant::IdleOnly);
    FxClearLog();
    ExpectNoWorldNoPc("WORLD.03 empty slot, pool IdleOnly");

    // WORLD.04: each failure path after the world was read still reports it.
    FxReset();
    FxWorld(g_worldBare);
    FxPool(FxPoolVariant::IdleOnly);
    FxClearLog();
    {
        const Edel::CurrentTargetResult r = FxDetect();
        check("WORLD.04 world survives failure paths: under N1 (World_Bare, pool IdleOnly) world is World_Bare",
              r.note == FxNote::kN1 && r.world == g_worldBare, Got(r));
    }
    FxReset();
    FxSetPtr(g_pc0, FxOff::kPawn, 0);
    FxSetPtr(g_pc0, FxOff::kAckPawn, 0);
    FxClearLog();
    {
        const Edel::CurrentTargetResult r = FxDetect();
        check("WORLD.04 world survives failure paths: under N3 (PC_0 with no pawn) world is World_0",
              r.note == FxNote::kN3 && r.world == g_world0, Got(r));
    }
    FxReset();
    FxClearLog();
    {
        Tot::RequestPerCommand();
        const Edel::CurrentTargetResult r = FxDetect();
        Tot::ResetPerCommand();
        check("WORLD.04 world survives failure paths: under 'Cancelled.' (Tot raised before the call) world is "
              "World_0",
              r.note == FxNote::kCancelled && r.world == g_world0, Got(r));
    }
    FxReset();
    FxLpController(g_pcE);
    FxClearLog();
    {
        const Edel::CurrentTargetResult r = FxDetect();
        check("WORLD.04 world survives failure paths: under N6 (PC_E, nothing target-like) world is World_0",
              r.note == FxNote::kN6 && r.world == g_world0, Got(r));
    }
}

// ══ CHAIN ═══════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockChain() {
    blk("CHAIN - the GWorld chain: each lookup's exact and fuzzy arms, element 0, and each broken link falling to the "
        "scan");

    FxReset();
    FxClearLog();
    {
        const Edel::CurrentTargetResult r = FxDetect();
        check("CHAIN.01 happy chain: one INFO L1 naming PC_0", FxEdelCount(FxLine::L1(g_pc0), 'I') == 1,
              FxLinesStr(FxEdelLines()));
        check("CHAIN.01 happy chain: the instance scan never ran", FxScanLines().empty(), ScanLinesStr());
        check("CHAIN.01 happy chain: playerController PC_0", r.playerController == g_pc0, Got(r));
    }

    FxReset();
    FxBreakChain(FxBreak::NoGameInstance);
    FxClearLog();
    ExpectFallsToScan("CHAIN.02 no game instance");

    FxReset();
    FxWorld(g_worldFuzzy);
    FxClearLog();
    ExpectChainReachesAlt("CHAIN.03a world fuzzy arm (World_Fuzzy: an array decoy at 0x88, MyGameInstance at 0x98)");

    FxReset();
    FxWorld(g_worldShadow);
    FxClearLog();
    ExpectChainReachesAlt("CHAIN.03b exact beats fuzzy (World_Shadow: CachedGameInstance at 0x78 -> GI_0, "
                          "OwningGameInstance at 0x80 -> GI_Alt)");

    FxReset();
    FxSetPtr(g_world0, FxOff::kOwningGameInstance, g_giFz);
    FxClearLog();
    ExpectChainReachesAlt("CHAIN.04a LocalPlayers fuzzy arm (GI_Fz: an int decoy at 0xB0, LocalPlayersArray at "
                          "0xB8)");

    FxReset();
    FxSetPtr(g_world0, FxOff::kOwningGameInstance, g_giT);
    FxClearLog();
    {
        const Edel::CurrentTargetResult r = ExpectFallsToScan("CHAIN.04b no LocalPlayers field [trap]");
        check("CHAIN.04b no LocalPlayers field [trap]: no read at GI_T-1 -- never PC_T, and no line names it",
              r.playerController != g_pcT && FxEdelCount(FxLine::L1(g_pcT)) == 0, Got(r));
    }

    FxReset();
    FxBreakChain(FxBreak::MaxBelowNum);
    FxClearLog();
    ExpectFallsToScan("CHAIN.05 sanity failure ({LpArr, Num 1, Max 0})");
    FxReset();
    FxBreakChain(FxBreak::NegativeNum);
    FxClearLog();
    ExpectFallsToScan("CHAIN.05 sanity failure ({LpArr, Num -1, Max 4})");

    FxReset();
    FxBreakChain(FxBreak::EmptyLocalPlayers);
    FxClearLog();
    ExpectFallsToScan("CHAIN.06 empty LocalPlayers ({LpArr, 0, 2})");

    FxReset();
    FxBreakChain(FxBreak::NullLocalPlayer);
    FxClearLog();
    {
        const Edel::CurrentTargetResult r = ExpectFallsToScan("CHAIN.07a element 0 null (LpArr = {0, LocalPlayer_1})");
        check("CHAIN.07a element 0 null: element 1 is never taken -- not PC_Alt, and no line names it",
              r.playerController != g_pcAlt && FxEdelCount(FxLine::L1(g_pcAlt)) == 0, Got(r));
    }

    FxReset();
    FxClearLog();
    {
        const Edel::CurrentTargetResult r = FxDetect();
        check("CHAIN.07b element 0 is used: playerController PC_0", r.playerController == g_pc0, Got(r));
        check("CHAIN.07b element 0 is used: L1 names PC_0, and no line names PC_Alt",
              FxEdelCount(FxLine::L1(g_pc0), 'I') == 1 && FxEdelCount(FxLine::L1(g_pcAlt)) == 0,
              FxLinesStr(FxEdelLines()));
    }

    FxReset();
    g_lpArr[0] = g_lpFz;
    FxClearLog();
    ExpectChainReachesAlt("CHAIN.08 PlayerController fuzzy arm (LocalPlayer_Fz: a class decoy at 0xD0, "
                          "PlayerControllerRef at 0xD8)");

    FxReset();
    FxBreakChain(FxBreak::NoController);
    FxClearLog();
    {
        const Edel::CurrentTargetResult r = ExpectFallsToScan("CHAIN.09 null controller");
        check("CHAIN.09 null controller: resolved", r.resolved, Got(r));
    }
}

// ══ SCAN ════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockScan() {
    blk("SCAN - the instance-scan fallback: its picks, a cancelled walk, an active debug camera, a destroyed "
        "controller");

    FxReset();
    FxBreakChain(FxBreak::NoGameInstance);
    FxClearLog();
    {
        check("SCAN.01 premise: the template, the widget, an idle debug camera, PC_NoPlayer and PC_Remote sit in "
              "slots 0-4, ahead of PC_0 in slot 5",
              FxSlot(kSlotCdo) == g_cdoPc && FxSlot(kSlotWidget) == g_widget0 && FxSlot(kSlotDbgIdle) == g_dbgIdle &&
                  FxSlot(kSlotPcNoPlayer) == g_pcNoPlayer && FxSlot(kSlotPcRemote) == g_pcRemote &&
                  FxSlot(kSlotPc0) == g_pc0);
        check("SCAN.01 premise: the widget's class is named with 'PlayerController' and holds LocalPlayer_0; "
              "PC_0's class is named without it",
              FxClassNameText(g_widget0).find("PlayerController") != std::string::npos &&
                  FxGetPtr(g_widget0, FxOff::kWidgetPlayer) == g_lp0 &&
                  FxClassNameText(g_pc0).find("PlayerController") == std::string::npos,
              FxClassNameText(g_widget0) + " / " + FxClassNameText(g_pc0));
        const Edel::CurrentTargetResult r = FxDetect();
        check("SCAN.01 local player wins: playerController PC_0", r.playerController == g_pc0, Got(r));
        check("SCAN.01 local player wins: the Edel lines are INFO/WALK L2 naming PC_0, then L5",
              FxEdelLinesAre({ { 'I', FxLine::L2(g_pc0) },
                               { 'I', FxLine::L5(true, g_pc0, g_hero0, kDefCandidates) } }),
              FxLinesStr(FxEdelLines()));
    }

    FxReset();
    FxBreakChain(FxBreak::NoGameInstance);
    FxPool(FxPoolVariant::NoPc0);
    FxClearLog();
    {
        const Edel::CurrentTargetResult r = FxDetect();
        check("SCAN.02 remote player: playerController PC_Remote, playerPawn Pawn_R",
              r.playerController == g_pcRemote && r.playerPawn == g_pawnR, Got(r));
        check("SCAN.02 remote player: the Edel lines are WARN/WALK L3 naming PC_Remote, then L5 (unresolved, 1 "
              "candidate)",
              FxEdelLinesAre({ { 'W', FxLine::L3(g_pcRemote) },
                               { 'I', FxLine::L5(false, g_pcRemote, g_pawnR, 1u) } }),
              FxLinesStr(FxEdelLines()));
        const FxCands want = { FxCand(g_netConn0, kSlotNetConn, "NetConnection_0", "NetConnection",
                                      kScoreNotActorTracked, g_pcRemote, "BP_GameCtrl_C", "Player", FxOff::kPlayer,
                                      "not-Actor") };
        check("SCAN.02 remote player: the candidates are exactly [NetConnection_0 {7, 'NetConnection', -55, "
              "PC_Remote 'BP_GameCtrl_C', 'Player' 0xF0, 'not-Actor'}]",
              FxCandsAre(r.candidates, want), FxCandsStr(r.candidates));
        check("SCAN.02 remote player: note N5", r.note == FxNote::kN5, r.note);
    }

    FxReset();
    FxBreakChain(FxBreak::NoGameInstance);
    FxPool(FxPoolVariant::NoPc0NoRemote);
    FxClearLog();
    {
        const Edel::CurrentTargetResult r = FxDetect();
        check("SCAN.03 no player: playerController PC_NoPlayer, playerPawn Pawn_N",
              r.playerController == g_pcNoPlayer && r.playerPawn == g_pawnN, Got(r));
        check("SCAN.03 no player: the Edel lines are WARN/WALK L4 naming PC_NoPlayer, then L5 (unresolved, 0 "
              "candidates)",
              FxEdelLinesAre({ { 'W', FxLine::L4(g_pcNoPlayer) },
                               { 'I', FxLine::L5(false, g_pcNoPlayer, g_pawnN, 0u) } }),
              FxLinesStr(FxEdelLines()));
        check("SCAN.03 no player: no candidates, note N6", r.candidates.empty() && r.note == FxNote::kN6, Got(r));
    }

    FxReset();
    FxBreakChain(FxBreak::NoGameInstance);
    FxPool(FxPoolVariant::IdleOnly);
    FxClearLog();
    {
        const Edel::CurrentTargetResult r = FxDetect();
        check("SCAN.04 none: playerController and playerPawn 0, no candidates, unresolved",
              r.playerController == 0 && r.playerPawn == 0 && r.candidates.empty() && !r.resolved, Got(r));
        check("SCAN.04 none: note N1 (the world was read), world World_0",
              r.note == FxNote::kN1 && r.world == g_world0, r.note);
        check("SCAN.04 none: no Edel line", FxEdelLines().empty(), FxLinesStr(FxEdelLines()));
        check("SCAN.04 none: the instance scan ran once", FxScanLines().size() == 1u, ScanLinesStr());
    }

    // SCAN.05: the walk sees the cancel first; Aura still writes its summary after the abort WARN, reading 0 found.
    FxReset();
    FxBreakChain(FxBreak::NoGameInstance);
    FxClearLog();
    {
        Tot::RequestPerCommand();
        const Edel::CurrentTargetResult r = FxDetect();
        Tot::ResetPerCommand();
        check("SCAN.05 cancelled scan: playerController 0", r.playerController == 0 && r.playerPawn == 0, Got(r));
        check("SCAN.05 cancelled scan: note N1, not 'Cancelled.' (FIND-3, pinned as today)",
              r.note == FxNote::kN1, r.note);
        check("SCAN.05 cancelled scan: no Edel line", FxEdelLines().empty(), FxLinesStr(FxEdelLines()));
        check("SCAN.05 cancelled scan: exactly one PIPE:find WARN, the walk's own abort line",
              FxLogCount(kScanAborted, 'W', kScanCat) == 1 && FxLogPrefix("", 'W', kScanCat) == 1,
              FxLogDump(0, kScanCat));
        const std::string emptySummary = std::string(kScanPrefix) + ": 0 live instance(s)";
        const uint64_t abortSeq = FirstSeq(kScanCat, 'W', kScanAborted);
        const uint64_t summarySeq = FirstSeq(kScanCat, 'I', kScanPrefix);
        check("SCAN.05 cancelled scan: the walk's one summary line follows the abort and reads 0 live instances",
              FxScanLines().size() == 1u && FxStartsWith(FxScanLines()[0].text, emptySummary.c_str()) &&
                  abortSeq != 0 && summarySeq > abortSeq,
              FxLogDump(0, kScanCat));
    }

    FxReset();
    FxBreakChain(FxBreak::NoGameInstance);
    FxPool(FxPoolVariant::ActiveDcc);
    FxClearLog();
    {
        const Edel::CurrentTargetResult r = FxDetect();
        check("SCAN.06 active debug camera from the scan: playerController PC_0, playerPawn Hero_0",
              r.playerController == g_pc0 && r.playerPawn == g_hero0, Got(r));
        check("SCAN.06 active debug camera from the scan: the Edel lines are INFO L2 naming DbgPC_0 (before the "
              "hop), then L5 naming PC_0 and Hero_0",
              FxEdelLinesAre({ { 'I', FxLine::L2(g_dbgPc0) }, { 'I', FxLine::L5(true, g_pc0, g_hero0, 1u) } }),
              FxLinesStr(FxEdelLines()));
        check("SCAN.06 active debug camera from the scan: the candidates are exactly [DEF.02's Enemy_0 record]",
              FxCandsAre(r.candidates, { DefEnemy() }), FxCandsStr(r.candidates));
        check("SCAN.06 active debug camera from the scan: DbgPC_0 is not a candidate",
              FxFindCand(r, g_dbgPc0) == nullptr, FxCandsStr(r.candidates));
    }

    // SCAN.07: a destroyed controller still holding LocalPlayer_0 sits ahead of PC_0; Edel must take PC_0.
    FxReset();
    FxBreakChain(FxBreak::NoGameInstance);
    FxPool(FxPoolVariant::Dead);
    FxClearLog();
    {
        check("SCAN.07 premise: slot 3 holds PC_Dead, flagged garbage and holding LocalPlayer_0",
              FxSlot(kSlotPcDead) == g_pcDead && FxGet32(g_pcDead, Grimoire::OFF_UOBJECT_FLAGS) ==
                  static_cast<int32_t>(kRfMirroredGarbage) && FxGetPtr(g_pcDead, FxOff::kPlayer) == g_lp0,
              NameOr0(FxSlot(kSlotPcDead)));
        const Edel::CurrentTargetResult r = FxDetect();
        check("SCAN.07 destroyed controller first: playerController PC_0, playerPawn Hero_0",
              r.playerController == g_pc0 && r.playerPawn == g_hero0, Got(r));
        check("SCAN.07 destroyed controller first: the Edel lines are INFO L2 naming PC_0, then L5",
              FxEdelLinesAre({ { 'I', FxLine::L2(g_pc0) },
                               { 'I', FxLine::L5(true, g_pc0, g_hero0, kDefCandidates) } }),
              FxLinesStr(FxEdelLines()));
    }
    // The same pool with PC_Dead's flag cleared: it is taken, so the check above turns on the destroyed-object rule.
    FxReset();
    FxBreakChain(FxBreak::NoGameInstance);
    FxPool(FxPoolVariant::Dead);
    FxSetFlags(g_pcDead, 0);
    FxClearLog();
    {
        const Edel::CurrentTargetResult r = FxDetect();
        check("SCAN.07 premise: with its garbage flag cleared, PC_Dead is the scan's pick",
              r.playerController == g_pcDead && FxEdelCount(FxLine::L2(g_pcDead), 'I') == 1, Got(r));
    }
}

}  // namespace G1

static void Blocks_G1() {
    G1::BlockFix();
    G1::BlockDef();
    G1::BlockClamp();
    G1::BlockWorld();
    G1::BlockChain();
    G1::BlockScan();
}

// ══ G2: HOP / PAWN / EXCL / CLASS / OUTER ═══════════════════════════════════════════════════════════════════════
// Who the player is, and what can never be its target. HOP and PAWN follow the controller the chain hands over
// through the debug-camera hop and the pawn read; EXCL plants the player's own objects behind positive fields, so a
// missed exclusion would surface as a high-scoring candidate. CLASS and OUTER call the two structural tests directly
// through the #include, because their edges (the 64-node bound, the two-hop ownership bound, an unreadable class) are
// properties of the functions alone, and then run once end to end, so the direct answers are shown to be the ones the
// scoring uses.

namespace G2 {

// Edel's score terms. Every expected score below is built from them, so each number says which signals it is made of.
constexpr int32_t kPtsField = 50;       // the field's name matches a positive keyword
constexpr int32_t kPtsPawn = 30;        // the target classifies as a pawn
constexpr int32_t kPtsActor = 15;       // ... as any other actor
constexpr int32_t kPtsNotActor = -60;   // ... as neither
constexpr int32_t kPtsNear = 10;        // read from the pawn or a component, never from the controller
constexpr int32_t kPtsTracked = 5;      // a readable InternalIndex of 0 or more

constexpr int32_t kTargetPawnNear = kPtsField + kPtsPawn + kPtsNear + kPtsTracked;     // a pawn behind a target field
constexpr int32_t kTargetActorNear = kPtsField + kPtsActor + kPtsNear + kPtsTracked;   // a component's TargetActor
constexpr int32_t kBarePawn = kPtsPawn + kPtsTracked;            // a pawn behind a neutral controller field
constexpr int32_t kBareActor = kPtsActor + kPtsTracked;          // an actor behind a neutral controller field
constexpr int32_t kBareNonActor = kPtsNotActor + kPtsTracked;    // a tracked non-actor: kept, above the -60 cut

// OuterChainsBackTo's bound as scanOwnedComponents passes it: a component's outer, or its outer's outer.
constexpr int kOwnerHops = 2;
// DynOff::USTRUCT_SUPER's built-in default. No fixture class keeps its super there, so a walk that used it would stop
// at the zero it reads.
constexpr int kDefaultSuperAt = 0x40;
// A fixture-side walk's bound: past the longest super chain the fixture builds (64 fillers, then Actor and Object).
constexpr size_t kClassWalkCap = 2 * static_cast<size_t>(kSuperChainNodes);

constexpr int kExclEnemyRef = 4;   // PawnExcl: EnemyRef, the field that holds Enemy_E
// PcClass's fields, in its order.
constexpr int kClsBrute = 0, kClsBot = 1, kClsDeep64 = 2, kClsSense = 3, kClsDeep65 = 4, kClsCycle = 5;

// DEF.02's two records: what the default player yields once the hop or the pawn read has landed on PC_0 and Hero_0.
static FxCands Def02() {
    return { FxCandOf(g_enemy0, kTargetPawnNear, g_hero0, "CurrentTarget", FxOff::kSlot0,
                      FxWhy::Join({ FxWhy::Field("CurrentTarget"), FxWhy::kIsPawn })),
             FxCandOf(g_lp0, kBareNonActor, g_pc0, "Player", FxOff::kPlayer, FxWhy::kNotActor) };
}
static bool IsDef02(const Edel::CurrentTargetResult& r) { return FxCandsAre(r.candidates, Def02()); }

// `want` is among the candidates, every field equal.
static bool HasCand(const Edel::CurrentTargetResult& r, const Edel::TargetCandidate& want) {
    const Edel::TargetCandidate* got = FxFindCand(r, want.addr);
    return got && FxCandEq(*got, want);
}
static std::string WantGot(const Edel::TargetCandidate& want, const Edel::CurrentTargetResult& r) {
    return "want " + FxCandStr(want) + "\n      got " + FxCandsStr(r.candidates);
}
static bool Absent(const Edel::CurrentTargetResult& r, uintptr_t addr) { return FxFindCand(r, addr) == nullptr; }

// The chain handed `raw` over: one L1 naming it. Without this a hop check could pass through a broken chain, because
// the instance scan picks PC_0 by itself.
static bool ChainNamed(uintptr_t raw) { return FxEdelCount(FxLine::L1(raw), 'I') == 1; }
static std::string EdelLinesStr() { return FxLinesStr(FxEdelLines()); }

static std::string Kind(uintptr_t obj) { return ClassifyBySuperChain(obj); }
static std::string KindStr(uintptr_t obj) { return "'" + Kind(obj) + "'"; }

// The class nodes from cls up, read through the fixture's own super slot -- never through Ubel or Edel -- stopping
// at 0, at an object the fixture did not build, at a repeat, or at the cap.
static std::vector<uintptr_t> ClassNodes(uintptr_t cls) {
    std::vector<uintptr_t> nodes;
    std::unordered_set<uintptr_t> seen;
    for (uintptr_t c = cls; c && FxIsFixtureObj(c) && seen.insert(c).second && nodes.size() < kClassWalkCap;
         c = FxGetPtr(c, kStructSuperAt))
        nodes.push_back(c);
    return nodes;
}
// Where `target` sits on cls's super chain, counting cls itself as node 1; 0 when it is not on it.
static size_t NodeOf(uintptr_t cls, uintptr_t target) {
    const std::vector<uintptr_t> nodes = ClassNodes(cls);
    for (size_t i = 0; i < nodes.size(); ++i)
        if (nodes[i] == target) return i + 1;
    return 0;
}

static std::string OuterName(uintptr_t obj) {
    const uintptr_t outer = FxGetPtr(obj, DynOff::UOBJECT_OUTER);
    return outer ? FxNameText(outer) : std::string("(0)");
}

// ── HOP ──
static void HopBlock() {
    blk("HOP - the debug-camera hop on the chain path: by derivation, never by name; its fuzzy arm; no original");

    // HOP.01: the engine's own debug camera hands over the controller it replaced. L1 names the controller before
    // the hop and L5 the one after it, so the two lines tell them apart.
    FxReset();
    FxLpController(g_dbgPc0);
    FxClearLog();
    {
        const auto& r = FxDetect();
        check("HOP.01 engine debug camera: playerController is PC_0, the controller OriginalControllerRef holds",
              r.playerController == g_pc0, FxResultStr(r));
        check("HOP.01 engine debug camera: playerPawn is Hero_0, read from PC_0", r.playerPawn == g_hero0,
              FxResultStr(r));
        check("HOP.01 engine debug camera: the Edel lines are L1 naming DbgPC_0 (before the hop), then L5 naming "
              "PC_0 and Hero_0",
              FxEdelLinesAre({ { 'I', FxLine::L1(g_dbgPc0) },
                               { 'I', FxLine::L5(true, g_pc0, g_hero0, Def02().size()) } }),
              EdelLinesStr());
        check("HOP.01 engine debug camera: the candidates are DEF.02's", IsDef02(r), FxCandsStr(r.candidates));
    }

    // HOP.02: a debug camera whose class is named without the word still hops: the test is derivation.
    FxReset();
    FxLpController(g_freeCam0);
    FxClearLog();
    {
        const auto& r = FxDetect();
        check("HOP.02 subclass named without the word: the chain handed over FreeCam_0 (BP_FreeCam_C)",
              ChainNamed(g_freeCam0), EdelLinesStr());
        check("HOP.02 subclass named without the word: playerController is PC_0", r.playerController == g_pc0,
              FxResultStr(r));
        check("HOP.02 subclass named without the word: playerPawn is Hero_0", r.playerPawn == g_hero0,
              FxResultStr(r));
    }

    // HOP.03: the word in a class name is not derivation. DcHud_0's own OriginalControllerRef holds PC_0, so a hop by
    // name would land there; it is kept, and its own pawn is read.
    FxReset();
    FxLpController(g_dcHud0);
    FxClearLog();
    {
        const auto& r = FxDetect();
        check("HOP.03 name without derivation: the chain handed over DcHud_0 (DebugCameraControllerHUD_C)",
              ChainNamed(g_dcHud0), EdelLinesStr());
        check("HOP.03 name without derivation: playerController stays DcHud_0", r.playerController == g_dcHud0,
              FxResultStr(r));
        check("HOP.03 name without derivation: playerPawn is Pawn_D, DcHud_0's own", r.playerPawn == g_pawnD,
              FxResultStr(r));
    }

    // HOP.04: a debug camera with no OriginalControllerRef. The fuzzy arm takes the first ObjectProperty whose name
    // holds "OriginalController", past the ClassProperty ahead of it, which holds class BP_GameCtrl_C.
    FxReset();
    FxLpController(g_dccFz0);
    FxClearLog();
    {
        const auto& r = FxDetect();
        check("HOP.04 OriginalController fuzzy arm: the chain handed over DccFz_0 (BP_DccFuzzy_C)",
              ChainNamed(g_dccFz0), EdelLinesStr());
        check("HOP.04 OriginalController fuzzy arm: playerController is PC_0 (OriginalControllerPtr 0x118), not the "
              "class at 0x110",
              r.playerController == g_pc0, FxResultStr(r));
        check("HOP.04 OriginalController fuzzy arm: playerPawn is Hero_0", r.playerPawn == g_hero0, FxResultStr(r));
    }

    // HOP.05: a debug camera with no original stays the controller. It has no pawn, so detection stops at N3 before
    // any scan and writes no L5.
    FxReset();
    FxLpController(g_dbgNoOrig0);
    FxClearLog();
    {
        const auto& r = FxDetect();
        check("HOP.05 no original: playerController stays DbgNoOrig_0", r.playerController == g_dbgNoOrig0,
              FxResultStr(r));
        check("HOP.05 no original: playerPawn is 0", r.playerPawn == 0, FxResultStr(r));
        check("HOP.05 no original: the note is N3 verbatim", r.note == FxNote::kN3, r.note);
        check("HOP.05 no original: no candidates", r.candidates.empty(), FxCandsStr(r.candidates));
        check("HOP.05 no original: the Edel lines are L1 naming DbgNoOrig_0 and nothing else (no L5)",
              FxEdelLinesAre({ { 'I', FxLine::L1(g_dbgNoOrig0) } }), EdelLinesStr());
    }
    // HOP.06, the raw controller excluded, is checked in EXCL.01: FreeCam_X behind Hero_Ex1's HostileRaw.
}

// ── PAWN ──
static void PawnBlock() {
    blk("PAWN - ResolvePawn: Pawn first, AcknowledgedPawn behind it, exact names only, and no read at offset -1");

    // PAWN.01: Pawn and AcknowledgedPawn differ, as on a networked client. Pawn is taken, and the other one is still
    // excluded although PC_0's own AcknowledgedPawn edge reaches it.
    FxReset();
    FxSetPtr(g_pc0, FxOff::kAckPawn, g_heroAck);
    FxClearLog();
    {
        const auto& r = FxDetect();
        check("PAWN.01 Pawn first: playerPawn is Hero_0 (Pawn), not Hero_Ack (AcknowledgedPawn)",
              r.playerPawn == g_hero0, FxResultStr(r));
        check("PAWN.01 Pawn first: the candidates are DEF.02's; Hero_Ack, behind PC_0's AcknowledgedPawn, is excluded",
              IsDef02(r), FxCandsStr(r.candidates));
    }

    // PAWN.02: no Pawn, so AcknowledgedPawn is read.
    FxReset();
    FxSetPtr(g_pc0, FxOff::kPawn, 0);
    FxSetPtr(g_pc0, FxOff::kAckPawn, g_hero0);
    FxClearLog();
    {
        const auto& r = FxDetect();
        check("PAWN.02 AcknowledgedPawn fallback: playerPawn is Hero_0, read from AcknowledgedPawn",
              r.playerPawn == g_hero0, FxResultStr(r));
        check("PAWN.02 AcknowledgedPawn fallback: the candidates are DEF.02's", IsDef02(r), FxCandsStr(r.candidates));
        check("PAWN.02 AcknowledgedPawn fallback: one L5 reading pc=0x<PC_0> pawn=0x<Hero_0>",
              FxEdelCount(FxLine::L5(true, g_pc0, g_hero0, Def02().size()), 'I') == 1, EdelLinesStr());
    }

    // PAWN.03: neither, so detection stops at N3 with the chain's facts still reported and no L5.
    FxReset();
    FxSetPtr(g_pc0, FxOff::kPawn, 0);
    FxSetPtr(g_pc0, FxOff::kAckPawn, 0);
    FxClearLog();
    {
        const auto& r = FxDetect();
        check("PAWN.03 no pawn: the note is N3 verbatim", r.note == FxNote::kN3, r.note);
        check("PAWN.03 no pawn: playerController is PC_0", r.playerController == g_pc0, FxResultStr(r));
        check("PAWN.03 no pawn: playerPawn is 0", r.playerPawn == 0, FxResultStr(r));
        check("PAWN.03 no pawn: no candidates", r.candidates.empty(), FxCandsStr(r.candidates));
        check("PAWN.03 no pawn: not resolved", !r.resolved, FxResultStr(r));
        check("PAWN.03 no pawn: world is World_0", r.world == g_world0, FxResultStr(r));
        check("PAWN.03 no pawn: the Edel lines are L1 naming PC_0 and nothing else (no L5)",
              FxEdelLinesAre({ { 'I', FxLine::L1(g_pc0) } }), EdelLinesStr());
    }

    // PAWN.04: BP_PcAckOnly_C has no Pawn field. A lookup that misses answers -1, and the 8 bytes a read at -1 takes
    // spell Trap_0. PawnClassOverride, ahead of AcknowledgedPawn by offset, has "Pawn" in its name and holds a class.
    FxReset();
    FxLpController(g_pcAck0);
    FxClearLog();
    {
        const auto& r = FxDetect();
        const uintptr_t trapRead = FxGetPtr(g_pcAck0, -kTrapLead);
        check("PAWN.04a no read at offset -1: (fixture) the 8 bytes at PcAck_0-1 read Trap_0, a pawn",
              trapRead == g_trap0, FxNameText(trapRead) + " " + FxHex(trapRead));
        check("PAWN.04a no read at offset -1: the chain handed over PcAck_0", ChainNamed(g_pcAck0), EdelLinesStr());
        check("PAWN.04a no read at offset -1: playerController stays PcAck_0", r.playerController == g_pcAck0,
              FxResultStr(r));
        check("PAWN.04a no read at offset -1: playerPawn is AckPawn_0 (AcknowledgedPawn 0x128), not Trap_0",
              r.playerPawn == g_ackPawn0, FxResultStr(r));
        check("PAWN.04b exact names only: (fixture) PawnClassOverride 0x120 holds class BP_Enemy_C",
              FxGetPtr(g_pcAck0, FxOff::kPawnClassOverride) == g_cEnemy,
              FxNameOf(FxGetPtr(g_pcAck0, FxOff::kPawnClassOverride)));
        check("PAWN.04b exact names only: playerPawn is not class BP_Enemy_C, the object PawnClassOverride holds",
              r.playerPawn != g_cEnemy, FxResultStr(r));
    }
}

// ── EXCL ──
static void ExclBlock() {
    blk("EXCL - the player's own objects are never candidates: the controller after and before the hop, the pawn, "
        "and the AcknowledgedPawn ResolvePawn did not return");

    // EXCL.01 / EXCL.02: Hero_Ex1 points at every object the exclusion set holds, each through a positive field, and
    // at one real target. PC_Ex's AcknowledgedPawn sits at 0x1F8, not at the 0xE8 a PlayerController keeps it at, so
    // the extra read must go through PC_Ex's own class after the hop.
    FxReset();
    FxLpController(g_freeCamX);
    FxClearLog();
    {
        const auto& r = FxDetect();
        const FxCands want = { FxCandOf(g_enemyE, kTargetPawnNear, g_heroEx1, "EnemyRef", FxOff::Slot(kExclEnemyRef),
                                        FxWhy::Join({ FxWhy::Field("EnemyRef"), FxWhy::kIsPawn })) };
        check("EXCL.01 self pointers: playerController is PC_Ex, after the hop from FreeCam_X",
              r.playerController == g_pcEx, FxResultStr(r));
        check("EXCL.01 self pointers: playerPawn is Hero_Ex1", r.playerPawn == g_heroEx1, FxResultStr(r));
        check("EXCL.01 self pointers: the candidates are exactly [Enemy_E 95 from Hero_Ex1 via EnemyRef 0x220]",
              FxCandsAre(r.candidates, want), FxCandsStr(r.candidates));
        check("EXCL.01 self pointers: resolved", r.resolved, FxResultStr(r));
        check("EXCL.01 self pointers: Hero_Ex1, the pawn (behind LockOnSelf), is no candidate", Absent(r, g_heroEx1),
              FxCandsStr(r.candidates));
        check("EXCL.01 self pointers: PC_Ex, the controller after the hop (behind FocusPC), is no candidate",
              Absent(r, g_pcEx), FxCandsStr(r.candidates));
        check("HOP.06 raw controller excluded: FreeCam_X, the controller before the hop (behind HostileRaw), is no "
              "candidate",
              Absent(r, g_freeCamX), FxCandsStr(r.candidates));
        check("EXCL.02 the other pawn: Hero_Ex2, PC_Ex's AcknowledgedPawn (also behind TargetAck), is no candidate",
              Absent(r, g_heroEx2), FxCandsStr(r.candidates));
        check("EXCL.02 the other pawn: the note is N4 naming Enemy_E (BP_Enemy_C), score 95, via EnemyRef",
              r.note == FxNote::N4("Enemy_E", "BP_Enemy_C", kTargetPawnNear, "EnemyRef"), r.note);
    }
}

// ── CLASS ──
static void ClassBlock() {
    blk("CLASS - ClassifyBySuperChain called directly: the three pawn names, Actor, ActorComponent, the 64-node bound, "
        "cycles, unreadable classes and the moved super; then once end to end");
    FxReset();
    const int failsBeforeClass = g_fail.load();

    // CLASS.01: each pawn name is a route of its own. BP_Brute_C and BP_Bot_C sit on EngineOther's "Character" and
    // "DefaultPawn", which derive straight from Actor, so no "Pawn" node lies above them.
    check("CLASS.01a Pawn: Party_00 (BP_Party_C : Pawn) is 'Pawn'", Kind(g_party[0]) == "Pawn", KindStr(g_party[0]));
    check("CLASS.01b Character: Brute_0 (BP_Brute_C : 'Character' : Actor) is 'Pawn'", Kind(g_brute0) == "Pawn",
          KindStr(g_brute0));
    check("CLASS.01c DefaultPawn: Bot_0 (BP_Bot_C : 'DefaultPawn' : Actor) is 'Pawn'", Kind(g_bot0) == "Pawn",
          KindStr(g_bot0));
    check("CLASS.01d Character : Pawn: Enemy_0 (BP_Enemy_C) is 'Pawn'", Kind(g_enemy0) == "Pawn", KindStr(g_enemy0));

    // CLASS.02: any other actor.
    check("CLASS.02 actor: Prop_0 (BP_Prop_C : Actor) is 'Actor'", Kind(g_prop0) == "Actor", KindStr(g_prop0));
    check("CLASS.02 actor: PC_0 (BP_GameCtrl_C, Actor three supers up) is 'Actor'", Kind(g_pc0) == "Actor",
          KindStr(g_pc0));

    // CLASS.03: components, including two whose class names carry "Pawn" and "Character".
    check("CLASS.03a component: C01 (UTargetHolderComponent : ActorComponent) is 'ActorComponent'",
          Kind(g_c17[0]) == "ActorComponent", KindStr(g_c17[0]));
    check("CLASS.03b name decoys: Sense_0 (PawnSensingComponent) is 'ActorComponent'",
          Kind(g_sense0) == "ActorComponent", KindStr(g_sense0));
    check("CLASS.03b name decoys: Cmc_0 (CharacterMovementComponent) is 'ActorComponent'",
          Kind(g_cmc0) == "ActorComponent", KindStr(g_cmc0));

    // CLASS.04: neither, including a class with "Actor" in its name.
    check("CLASS.04 neither: LocalPlayer_0 (LocalPlayer : Player : Object) is ''", Kind(g_lp0).empty(),
          KindStr(g_lp0));
    check("CLASS.04 neither: Data_0 (BP_Data_C : Object) is ''", Kind(g_data0).empty(), KindStr(g_data0));
    check("CLASS.04 neither: Proxy_0 (BP_ActorProxy_C : Object) is ''", Kind(g_proxy0).empty(), KindStr(g_proxy0));

    // CLASS.05: the walk visits 64 class nodes. The fixture's own count of the chain is checked first, so the bound is
    // what decides the answer.
    const size_t actorAbove01 = NodeOf(g_cFiller[1], g_cActor);
    const size_t actorAbove00 = NodeOf(g_cFiller[0], g_cActor);
    check("CLASS.05a Actor as the 64th node: (fixture) Actor is node 64 counting from Filler_01",
          actorAbove01 == static_cast<size_t>(kSuperChainNodes), FxS(static_cast<long long>(actorAbove01)));
    check("CLASS.05a Actor as the 64th node: Deep64_0 (class Filler_01) is 'Actor'", Kind(g_deep64) == "Actor",
          KindStr(g_deep64));
    check("CLASS.05b Actor as the 65th node: (fixture) Actor is node 65 counting from Filler_00",
          actorAbove00 == static_cast<size_t>(kSuperChainNodes) + 1, FxS(static_cast<long long>(actorAbove00)));
    check("CLASS.05b Actor as the 65th node: Deep65_0 (class Filler_00) is ''", Kind(g_deep65).empty(),
          KindStr(g_deep65));
    const int failsThroughClass05 = g_fail.load();

    // CLASS.06: a loop of supers ends.
    check("CLASS.06 cycles terminate: Cyc_0 (Cyc_A <-> Cyc_B) is ''", Kind(g_cyc0).empty(), KindStr(g_cyc0));
    check("CLASS.06 cycles terminate: Self_0 (SelfLoop_C, its own super) is ''", Kind(g_self0).empty(),
          KindStr(g_self0));

    // CLASS.07: a refused page anywhere on the way ends the walk with nothing.
    check("CLASS.07 unreadable: Bad_0 (BadSuper_C, whose super is a refused page) is ''", Kind(g_bad0).empty(),
          KindStr(g_bad0));
    check("CLASS.07 unreadable: BadCls_0, whose class pointer is a refused page, is ''", Kind(g_badCls0).empty(),
          KindStr(g_badCls0));
    check("CLASS.07 unreadable: the refused page itself is ''", Kind(g_unreadablePage).empty(),
          KindStr(g_unreadablePage));

    // CLASS.08: every answer above came through the super slot the run moved to 0x30, with 0 where the default
    // offset would look.
    check("CLASS.08 moved super: DynOff::USTRUCT_SUPER is 0x30", DynOff::USTRUCT_SUPER == kStructSuperAt,
          FxHex(static_cast<uintptr_t>(DynOff::USTRUCT_SUPER)));
    {
        std::string nonZero;
        for (uintptr_t o : { g_party[0], g_brute0, g_bot0, g_enemy0, g_prop0, g_pc0, g_c17[0], g_sense0, g_cmc0, g_lp0,
                             g_data0, g_proxy0, g_deep64, g_deep65 })
            for (uintptr_t c : ClassNodes(FxGetPtr(o, Grimoire::OFF_UOBJECT_CLASS)))
                if (FxGetPtr(c, kDefaultSuperAt) != 0) nonZero += " " + FxNameText(c);
        check("CLASS.08 moved super: every class node CLASS.01-05 walk reads 0 at +0x40, the default super offset",
              nonZero.empty(), nonZero);
    }
    check("CLASS.08 moved super: CLASS.01-05 passed under it", failsThroughClass05 == failsBeforeClass,
          FxFmt("%d failure(s) in CLASS.01-05", failsThroughClass05 - failsBeforeClass));

    // CLASS.09: the same answers decide the scores end to end. Sense_0 is a component owned by Enemy_0, so it is
    // scored as a target and never scanned as one of PC_Cls's components.
    FxReset();
    FxLpController(g_pcCls);
    FxClearLog();
    {
        const auto& r = FxDetect();
        const FxCands want = {
            FxCandOf(g_brute0, kBarePawn, g_pcCls, "Brute", FxOff::Slot(kClsBrute), FxWhy::kIsPawn),
            FxCandOf(g_bot0, kBarePawn, g_pcCls, "Bot", FxOff::Slot(kClsBot), FxWhy::kIsPawn),
            FxCandOf(g_deep64, kBareActor, g_pcCls, "Deep64", FxOff::Slot(kClsDeep64), FxWhy::kIsActor),
            FxCandOf(g_sense0, kBareNonActor, g_pcCls, "Sense", FxOff::Slot(kClsSense), FxWhy::kNotActor),
            FxCandOf(g_deep65, kBareNonActor, g_pcCls, "Deep65", FxOff::Slot(kClsDeep65), FxWhy::kNotActor),
            FxCandOf(g_cyc0, kBareNonActor, g_pcCls, "Cycle", FxOff::Slot(kClsCycle), FxWhy::kNotActor),
        };
        check("CLASS.09 end to end: exactly Brute_0 35, Bot_0 35 (is-Pawn), Deep64_0 20 (is-Actor), Sense_0, "
              "Deep65_0, Cyc_0 -55 (not-Actor), each from PC_Cls via its own field",
              FxCandsAre(r.candidates, want), FxCandsStr(r.candidates));
    }
}

// ── OUTER ──
static void OuterBlock() {
    blk("OUTER - ownership: an outer chain that reaches the root within two hops; then the PC's components end to end");
    FxReset();

    check("OUTER.01 one hop: (fixture) K1's outer is PC_U", FxGetPtr(g_k1, DynOff::UOBJECT_OUTER) == g_pcOuter,
          OuterName(g_k1));
    check("OUTER.01 one hop: OuterChainsBackTo(K1, PC_U, 2) is true", OuterChainsBackTo(g_k1, g_pcOuter, kOwnerHops));

    check("OUTER.02 two hops: (fixture) K2's outer is Sub_U, and Sub_U's outer is PC_U",
          FxGetPtr(g_k2, DynOff::UOBJECT_OUTER) == g_subU && FxGetPtr(g_subU, DynOff::UOBJECT_OUTER) == g_pcOuter,
          OuterName(g_k2) + " / " + OuterName(g_subU));
    check("OUTER.02 two hops: OuterChainsBackTo(K2, PC_U, 2) is true", OuterChainsBackTo(g_k2, g_pcOuter, kOwnerHops));

    // The chain does reach PC_U at the third hop, so the false below is the bound's answer, not a broken chain's.
    check("OUTER.03 three hops: (fixture) K3's outer is Sub_U2, and Sub_U2's outer is Sub_U",
          FxGetPtr(g_k3, DynOff::UOBJECT_OUTER) == g_subU2 && FxGetPtr(g_subU2, DynOff::UOBJECT_OUTER) == g_subU,
          OuterName(g_k3) + " / " + OuterName(g_subU2));
    check("OUTER.03 three hops: OuterChainsBackTo(K3, PC_U, 3) is true",
          OuterChainsBackTo(g_k3, g_pcOuter, kOwnerHops + 1));
    check("OUTER.03 three hops: OuterChainsBackTo(K3, PC_U, 2) is false",
          !OuterChainsBackTo(g_k3, g_pcOuter, kOwnerHops));

    check("OUTER.04 null outer: (fixture) K0's outer is 0", FxGetPtr(g_k0, DynOff::UOBJECT_OUTER) == 0,
          OuterName(g_k0));
    check("OUTER.04 null outer: OuterChainsBackTo(K0, PC_U, 2) is false",
          !OuterChainsBackTo(g_k0, g_pcOuter, kOwnerHops));

    // OUTER.05-08: PC_U's four components carry InternalIndex -7, so each is dropped as a candidate of its own; a
    // TargetActor reaches the list only if its component was scanned.
    FxReset();
    FxLpController(g_pcOuter);
    FxClearLog();
    {
        const auto& r = FxDetect();
        const std::string reason = FxWhy::Join({ FxWhy::Field("TargetActor"), FxWhy::kIsActor });
        const Edel::TargetCandidate t1 = FxCandOf(g_t1, kTargetActorNear, g_k1, "TargetActor", FxOff::kSlot0, reason);
        const Edel::TargetCandidate t2 = FxCandOf(g_t2, kTargetActorNear, g_k2, "TargetActor", FxOff::kSlot0, reason);
        check("OUTER.05 end to end: playerController is PC_U", r.playerController == g_pcOuter, FxResultStr(r));
        check("OUTER.05 one hop, end to end: T1 is a candidate {80, K1, 'UTargetHolderComponent', 'TargetActor', "
              "0x200, \"field 'TargetActor', is-Actor\"}",
              HasCand(r, t1), WantGot(t1, r));
        check("OUTER.06 two hops, end to end: T2 is a candidate, read from K2", HasCand(r, t2), WantGot(t2, r));
        check("OUTER.07 three hops, end to end: T3 is no candidate", Absent(r, g_t3), FxCandsStr(r.candidates));
        check("OUTER.08 null outer, end to end: T0 is no candidate", Absent(r, g_t0), FxCandsStr(r.candidates));
    }
}

}  // namespace G2

static void Blocks_G2() {
    G2::HopBlock();
    G2::PawnBlock();
    G2::ExclBlock();
    G2::ClassBlock();
    G2::OuterBlock();
}

// ══ G3: SCORE, KW, CAND, DEDUP / ORDER, EDGE ════════════════════════════════════════════════════════════════════
// What Edel makes of the edges it collects: each scoring signal on its own, every keyword-table entry that can be told
// apart, the candidate record field by field, which edge a target's single record keeps, and the 1024-edge cap at the
// three places Edel collects edges. Every scenario reaches its controller through the GWorld chain, so a run writes
// exactly two Edel lines, L1 and L5.

namespace G3 {

// Edel's ceiling: no list in these blocks is long enough to be cut, so every candidate a check names is in it.
constexpr int32_t kMaxList = 64;
// A container element's fieldOffset: the element lives in a heap buffer, not inside the source object.
constexpr int32_t kElement = -1;
// The index Edel reports for a child whose InternalIndex cannot be read.
constexpr int32_t kUnreadable = -1;
// CAND.07: bit 15 and bit 16 both set. Read through 16 bits it comes back negative, so it loses the +5 as well as its
// value; any read narrower than 17 bits drops the top bit.
constexpr int32_t kWideIndex = 0x18000;

static std::string Got(const Edel::TargetCandidate* c) { return c ? FxCandStr(*c) : std::string("absent"); }
static std::string GotWant(const Edel::CurrentTargetResult& r, const Edel::TargetCandidate& want) {
    return "got " + Got(FxFindCand(r, want.addr)) + "\n      want " + FxCandStr(want);
}
static std::string ListGot(const Edel::CurrentTargetResult& r, const FxCands& want) {
    return "got " + FxCandsStr(r.candidates) + "\n      want " + FxCandsStr(want);
}
// `want` is among the candidates, every field equal.
static bool Has(const Edel::CurrentTargetResult& r, const Edel::TargetCandidate& want) {
    const Edel::TargetCandidate* c = FxFindCand(r, want.addr);
    return c && FxCandEq(*c, want);
}
static bool Absent(const Edel::CurrentTargetResult& r, uintptr_t addr) { return FxFindCand(r, addr) == nullptr; }
static int CountOf(const Edel::CurrentTargetResult& r, uintptr_t addr) {
    int n = 0;
    for (const auto& c : r.candidates) n += (c.addr == addr) ? 1 : 0;
    return n;
}
// What a detection whose controller came through the GWorld chain writes: L1 naming it, then L5.
static bool ChainLines(uintptr_t pc, uintptr_t pawn, bool resolved, size_t count) {
    return FxEdelLinesAre({ { 'I', FxLine::L1(pc) }, { 'I', FxLine::L5(resolved, pc, pawn, count) } });
}
static std::string EdelGot() { return FxLinesStr(FxEdelLines()); }
// A fresh world whose chain leads to `pc`, the log cleared after the last setup write.
static void Scenario(uintptr_t pc) {
    FxReset();
    FxLpController(pc);
    FxClearLog();
}

// ══ SCORE ═══════════════════════════════════════════════════════════════════════════════════════════════════════
// One target per signal, through PC_S and its pawn Hero_S. The expected scores are built from Edel's weights: +50 a
// positive keyword in the field name, +30 a pawn or else +15 an actor, -40 a negative keyword in the field name, -60 a
// non-actor, +10 on the pawn root, +5 a readable InternalIndex of 0 or more; -60 or less is dropped.
static void Score() {
    using namespace FxWhy;
    using FxOff::Slot;
    blk("SCORE - one target per signal: keyword, kind, infra field, non-actor, near combat, index and the drop line");
    Scenario(g_pcS);
    const Edel::CurrentTargetResult& r = FxDetect(kMaxList);

    const auto pT = FxCandOf(g_pT, 95, g_heroS, "TargetPawn", Slot(1), Join({ Field("TargetPawn"), kIsPawn }));
    const auto aT = FxCandOf(g_aT, 70, g_pcS, "TargetActor", Slot(0), Join({ Field("TargetActor"), kIsActor }));
    const auto pS = FxCandOf(g_pS, 35, g_pcS, "Squadmate", Slot(1), kIsPawn);
    const auto aP2 = FxCandOf(g_aP2, 30, g_heroS, "Prop2", Slot(0), kIsActor);
    const auto aV = FxCandOf(g_aV, 30, g_pcS, "ViewTarget", Slot(7),
                             Join({ Field("ViewTarget"), kIsActor, kInfra }));
    const auto aP = FxCandOf(g_aP, 20, g_pcS, "Prop", Slot(2), kIsActor);
    const auto aB = FxCandOf(g_aB, 20, g_pcS, "Boss", Slot(4), kIsActor);
    const auto aN = FxCandOf(g_aN, 15, g_pcS, "Stray", Slot(10), kIsActor);
    const auto pO = FxCandOf(g_pO, 5, g_heroS, "OwnerPawn", Slot(2), Join({ kIsPawn, kInfra }));
    const auto nL = FxCandOf(g_nL, -5, g_pcS, "LockOnComponent", Slot(6),
                             Join({ Field("LockOnComponent"), kNotActor }));
    const auto aO = FxCandOf(g_aO, -20, g_pcS, "OwnerRef", Slot(3), Join({ kIsActor, kInfra }));
    const auto nG = FxCandOf(g_nG, -45, g_heroS, "Gadget", Slot(3), kNotActor);
    const auto nD = FxCandOf(g_nD, -55, g_pcS, "DataAsset", Slot(5), kNotActor);
    const auto nK = FxCandOf(g_nK, -55, g_pcS, "Junk2", Slot(9), kNotActor);
    const FxCands want = { pT, aT, pS, aP2, aV, aP, aB, aN, pO, nL, aO, nG, nD, nK };

    check("SCORE.00 list: the chain reaches PC_S and its pawn Hero_S through World_0",
          r.world == g_world0 && r.playerController == g_pcS && r.playerPawn == g_heroS, FxResultStr(r));
    check("SCORE.00 list: exactly 14 candidates in this order, every field as derived -- P_t 95, A_t 70, P_s 35, "
          "A_p2 30, A_v 30, A_p 20, A_b 20, A_n 15, P_o 5, N_l -5, A_o -20, N_g -45, N_d -55, N_k -55",
          FxCandsAre(r.candidates, want), ListGot(r, want));

    check("SCORE.01 positive field: A_t == {70, PC_S, 'PcScore', 'TargetActor', 0x200, \"field 'TargetActor', "
          "is-Actor\"}",
          Has(r, aT), GotWant(r, aT));
    check("SCORE.01 positive field: P_t == {95, Hero_S, 'PawnScore', 'TargetPawn', 0x208, \"field 'TargetPawn', "
          "is-Pawn\"}",
          Has(r, pT), GotWant(r, pT));

    check("SCORE.02 kind: the pawn P_s scores 35 with reason 'is-Pawn'", Has(r, pS), GotWant(r, pS));
    check("SCORE.02 kind: the actor A_p scores 20 with reason 'is-Actor'", Has(r, aP), GotWant(r, aP));

    check("SCORE.03a negative field: A_o through 'OwnerRef' scores -20, reason 'is-Actor, infra-field'", Has(r, aO),
          GotWant(r, aO));
    check("SCORE.03a negative field: P_o through 'OwnerPawn' scores 5, reason 'is-Pawn, infra-field'", Has(r, pO),
          GotWant(r, pO));
    check("SCORE.03b class name: A_b, a BP_WorldBoss_C behind the neutral field 'Boss', keeps 20 and 'is-Actor'",
          Has(r, aB), GotWant(r, aB));

    check("SCORE.04 non-actor: N_d (a BP_Data_C) scores -55, reason 'not-Actor'", Has(r, nD), GotWant(r, nD));
    check("SCORE.04 non-actor: N_l through 'LockOnComponent' scores -5, reason \"field 'LockOnComponent', not-Actor\"",
          Has(r, nL), GotWant(r, nL));

    const Edel::TargetCandidate* cAP2 = FxFindCand(r, g_aP2);
    check("SCORE.05 near combat: +10 on the pawn root only -- A_p2 30 against A_p 20, N_g -45 against N_d -55",
          Has(r, aP2) && Has(r, aP) && Has(r, nG) && Has(r, nD), ListGot(r, want));
    check("SCORE.05 near combat: no reason text of its own -- A_p2's reason is exactly 'is-Actor'",
          cAP2 && cAP2->reason == "is-Actor", Got(cAP2));

    const Edel::TargetCandidate* cNK = FxFindCand(r, g_nK);
    check("SCORE.06a index: InternalIndex 0 earns the +5 -- N_k is kept at -55 with index 0",
          cNK && cNK->score == -55 && cNK->index == 0, Got(cNK));
    const Edel::TargetCandidate* cAN = FxFindCand(r, g_aN);
    check("SCORE.06b index: a negative InternalIndex earns nothing -- A_n scores 15, reason 'is-Actor'",
          cAN && cAN->score == 15 && cAN->reason == "is-Actor", Got(cAN));
    check("SCORE.06c index: the raw readable index is reported -- A_n.index == -7 (FIND-4, pinned as today)",
          cAN && cAN->index == kUntrackedIndex, Got(cAN));

    const Edel::TargetCandidate* cAV = FxFindCand(r, g_aV);
    const Edel::TargetCandidate* cNL = FxFindCand(r, g_nL);
    check("SCORE.07 reason order: A_v scores 30 with reason exactly \"field 'ViewTarget', is-Actor, infra-field\"",
          cAV && cAV->score == 30 && cAV->reason == "field 'ViewTarget', is-Actor, infra-field", Got(cAV));
    check("SCORE.07 reason order: N_l's reason is exactly \"field 'LockOnComponent', not-Actor\"",
          cNL && cNL->reason == "field 'LockOnComponent', not-Actor", Got(cNL));

    check("SCORE.08a drop line: exactly -60 is dropped -- N_j (a non-actor at InternalIndex -7) is absent",
          Absent(r, g_nJ), FxCandsStr(r.candidates));
    check("SCORE.08b drop line: -55 is kept -- N_d and N_k are both present",
          !Absent(r, g_nD) && !Absent(r, g_nK), FxCandsStr(r.candidates));

    check("SCORE.09 note: 95 clears 70 by 25 -- resolved, \"Detected target: P_t (BP_Party_C) — score 95 via "
          "TargetPawn.\"",
          r.resolved && r.note == "Detected target: P_t (BP_Party_C) — score 95 via TargetPawn.", FxResultStr(r));
    check("SCORE.09 log: L1 names PC_S, then L5 reads resolved=1 pawn=Hero_S candidates=14",
          ChainLines(g_pcS, g_heroS, true, want.size()), EdelGot());
}

// ══ KW ══════════════════════════════════════════════════════════════════════════════════════════════════════════
// One field per keyword-table entry that can be told apart, each to its own BP_Prop_C with a tracked index. A positive
// alone is 50 + 15 + 5 = 70; a negative alone is 15 - 40 + 5 = -20; ViewTarget, both at once, is 30.
static void Kw() {
    using namespace FxWhy;
    using FxOff::Slot;
    blk("KW - every keyword-table entry that can be told apart, one field and one target each");

    Scenario(g_pcKp);
    {
        const Edel::CurrentTargetResult& r = FxDetect(kMaxList);
        FxCands want;
        for (int i = 0; i < kKwPosCount; ++i)
            want.push_back(FxCandOf(g_kp[i], 70, g_pcKp, kKwPosFields[i], Slot(i),
                                    Join({ Field(kKwPosFields[i]), kIsActor })));
        check("KWPOS.01 stems: 11 candidates, each 70 from PC_KP's own field with \"field '<field>', is-Actor\", in "
              "offset order",
              FxCandsAre(r.candidates, want), ListGot(r, want));
        for (int i = 0; i < kKwPosCount; ++i)
            check(FxFmt("KWPOS.01 stems: field '%s' alone earns the +50 -- KP_%s scores 70 from 0x%X",
                        kKwPosFields[i].c_str(), kKwPosFields[i].c_str(), static_cast<unsigned>(Slot(i))),
                  Has(r, want[static_cast<size_t>(i)]), GotWant(r, want[static_cast<size_t>(i)]));
        check("KWPOS.01 note: eleven tops tied at 70 resolve nothing -- N5", !r.resolved && r.note == FxNote::kN5,
              FxResultStr(r));
        check("KWPOS.01 log: L1 names PC_KP, then L5 reads resolved=0 pawn=Hero_KP candidates=11",
              ChainLines(g_pcKp, g_heroKp, false, want.size()), EdelGot());
    }

    Scenario(g_pcKn);
    {
        const Edel::CurrentTargetResult& r = FxDetect(kMaxList);
        const int viewTarget = kKwNegCount - 1;   // the one field that is both positive and negative, placed last
        FxCands want;
        want.push_back(FxCandOf(g_kn[viewTarget], 30, g_pcKn, "ViewTarget", Slot(viewTarget),
                                Join({ Field("ViewTarget"), kIsActor, kInfra })));
        for (int i = 0; i < viewTarget; ++i)
            want.push_back(FxCandOf(g_kn[i], -20, g_pcKn, kKwNegFields[i], Slot(i), Join({ kIsActor, kInfra })));
        check("KWNEG.01 entries: 23 candidates -- KN_ViewTarget first at 30, then the other 22 in offset order, each "
              "-20 with 'is-Actor, infra-field'",
              FxCandsAre(r.candidates, want), ListGot(r, want));
        check("KWNEG.01 entries: KN_ViewTarget == {30, PC_KN, 'PcKwNeg', 'ViewTarget', 0x2B0, \"field 'ViewTarget', "
              "is-Actor, infra-field\"}",
              Has(r, want[0]), GotWant(r, want[0]));
        for (int i = 0; i < viewTarget; ++i)
            check(FxFmt("KWNEG.01 entries: field '%s' alone costs the -40 -- KN_%s scores -20 from 0x%X",
                        kKwNegFields[i].c_str(), kKwNegFields[i].c_str(), static_cast<unsigned>(Slot(i))),
                  Has(r, want[static_cast<size_t>(i) + 1]), GotWant(r, want[static_cast<size_t>(i) + 1]));
        check("KWNEG.01 note: 30 clears -20 -- resolved, \"Detected target: KN_ViewTarget (BP_Prop_C) — score 30 via "
              "ViewTarget.\"",
              r.resolved && r.note == "Detected target: KN_ViewTarget (BP_Prop_C) — score 30 via ViewTarget.",
              FxResultStr(r));
        check("KWNEG.01 log: L1 names PC_KN, then L5 reads resolved=1 pawn=Hero_KN candidates=23",
              ChainLines(g_pcKn, g_heroKn, true, want.size()), EdelGot());
    }
}

// ══ CAND ════════════════════════════════════════════════════════════════════════════════════════════════════════
// The record Edel builds for each kept edge. PC_C's Enemies array and Hero_C's nested struct, weak field, unreadable
// pointer and class-less object give every shape of name, class, field name and offset one candidate each.
static void Cand() {
    using namespace FxWhy;
    blk("CAND - the candidate record: name, class, source, raw field name, element offset, unreadable child, index");
    if (g_enemyA0 >= g_enemyA1) FxFixtureFail("CAND.03 needs Enemy_A0 below Enemy_A1 by address");
    // The records below read indices and names from the fixture, so they are taken from a reset world.
    FxReset();

    const auto eN = FxCandOf(g_enemyN, 95, g_heroC, "LockOn.CurrentTarget", FxOff::kLockOnTarget,
                             Join({ Field("LockOn.CurrentTarget"), kIsPawn }));
    const auto eW = FxCandOf(g_enemyW, 95, g_heroC, "LockedOnTarget", FxOff::kLockedOnTarget,
                             Join({ Field("LockedOnTarget"), kIsPawn }));
    const auto eA0 = FxCandOf(g_enemyA0, 85, g_pcC, "Enemies", kElement, Join({ Field("Enemies"), kIsPawn }));
    const auto eA1 = FxCandOf(g_enemyA1, 85, g_pcC, "Enemies", kElement, Join({ Field("Enemies"), kIsPawn }));
    const auto orphan = FxCandOf(g_orphan0, -45, g_heroC, "Orphan", FxOff::kOrphan, kNotActor);
    // Written out rather than taken from the fixture: an unreadable child has no name, class or index to look up.
    const auto ghost = FxCand(FxGhost(), kUnreadable, "", "", -50, g_heroC, "PawnCand", "Ghost", FxOff::kGhost,
                              "not-Actor");

    Scenario(g_pcC);
    {
        const Edel::CurrentTargetResult& r = FxDetect(kMaxList);
        const FxCands want = { eN, eW, eA0, eA1, orphan, ghost };
        check("CAND.03 raw field names: the list is exactly Enemy_N 95, Enemy_W 95, Enemy_A0 85, Enemy_A1 85, "
              "Orphan_0 -45, the ghost -50, every field as derived",
              FxCandsAre(r.candidates, want), ListGot(r, want));
        const Edel::TargetCandidate* cN = FxFindCand(r, g_enemyN);
        const Edel::TargetCandidate* cW = FxFindCand(r, g_enemyW);
        const Edel::TargetCandidate* cA0 = FxFindCand(r, g_enemyA0);
        const Edel::TargetCandidate* cA1 = FxFindCand(r, g_enemyA1);
        const Edel::TargetCandidate* cO = FxFindCand(r, g_orphan0);
        check("CAND.03 raw field names: a nested struct member keeps Aura's dotted path and its absolute offset -- "
              "'LockOn.CurrentTarget' at 0x208",
              cN && cN->fieldName == "LockOn.CurrentTarget" && cN->fieldOffset == FxOff::kLockOnTarget, Got(cN));
        check("CAND.03 raw field names: the case is kept -- 'LockedOnTarget' and 'Enemies' exactly as reflected",
              cW && cW->fieldName == "LockedOnTarget" && cA0 && cA0->fieldName == "Enemies",
              Got(cW) + "\n      " + Got(cA0));
        check("CAND.03 note: two tops tied at 95 resolve nothing -- N5", !r.resolved && r.note == FxNote::kN5,
              FxResultStr(r));
        check("CAND.03 log: L1 names PC_C, then L5 reads resolved=0 pawn=Hero_C candidates=6",
              ChainLines(g_pcC, g_heroC, false, want.size()), EdelGot());

        check("CAND.01 name and class: Enemy_N carries its own FName 'Enemy_N' and its class's 'BP_Enemy_C'",
              cN && cN->name == "Enemy_N" && cN->className == "BP_Enemy_C", Got(cN));
        check("CAND.01 name and class: Orphan_0, whose class pointer is 0, keeps its name and has className ''",
              cO && cO->name == "Orphan_0" && cO->className.empty(), Got(cO));

        check("CAND.02 source: a root edge carries its root and the root's class -- Enemy_N from Hero_C 'PawnCand', "
              "Enemy_A0 from PC_C 'PcCand'",
              cN && cN->sourceObject == g_heroC && cN->sourceClass == "PawnCand" && cA0 && cA0->sourceObject == g_pcC &&
                  cA0->sourceClass == "PcCand",
              Got(cN) + "\n      " + Got(cA0));

        check("CAND.04 element offset: Enemy_A0 (element 0) and Enemy_A1 (element 1) both carry fieldOffset -1",
              cA0 && cA0->fieldOffset == kElement && cA1 && cA1->fieldOffset == kElement,
              Got(cA0) + "\n      " + Got(cA1));
        check("CAND.04 element offset: Enemy_W, a weak single edge, carries its field's 0x210",
              cW && cW->fieldOffset == FxOff::kLockedOnTarget, Got(cW));

        check("CAND.05 unreadable child: the ghost == {g_unreadablePage+0x100, -1, '', '', -50, Hero_C, 'PawnCand', "
              "'Ghost', 0x218, 'not-Actor'}",
              Has(r, ghost), GotWant(r, ghost));
    }

    // CAND.03b: with the weak pointer null (serial 0) and the array empty, Enemy_N is alone at the top.
    FxReset();
    FxLpController(g_pcC);
    FxPutWeak(g_heroC, FxOff::kLockedOnTarget, kSlotEnemyW, 0);
    FxPutTArray(g_pcC, FxOff::kSlot0, FxAddr(g_candBuf), 0, kCandCount);
    FxClearLog();
    {
        const Edel::CurrentTargetResult& r = FxDetect(kMaxList);
        const FxCands want = { eN, orphan, ghost };
        check("CAND.03b dotted path in the note: with Enemy_W and the array gone the list is Enemy_N, Orphan_0, the "
              "ghost",
              FxCandsAre(r.candidates, want), ListGot(r, want));
        check("CAND.03b dotted path in the note: \"Detected target: Enemy_N (BP_Enemy_C) — score 95 via "
              "LockOn.CurrentTarget.\"",
              r.resolved && r.note == "Detected target: Enemy_N (BP_Enemy_C) — score 95 via LockOn.CurrentTarget.",
              FxResultStr(r));
    }

    // CAND.02, a component edge: T01 reached through C01, an owned UTargetHolderComponent of PC_B.
    Scenario(g_pcB);
    {
        const Edel::CurrentTargetResult& r = FxDetect(kMaxList);
        const Edel::TargetCandidate* c = FxFindCand(r, g_t17[0]);
        check("CAND.02 source: a component edge carries the component C01 as sourceObject and "
              "'UTargetHolderComponent' as sourceClass",
              c && c->sourceObject == g_c17[0] && c->sourceClass == "UTargetHolderComponent", Got(c));
        const auto t01 = FxCandOf(g_t17[0], 80, g_c17[0], "TargetActor", FxOff::kSlot0,
                                  Join({ Field("TargetActor"), kIsActor }));
        check("CAND.02 source: T01 == {80, C01, 'UTargetHolderComponent', 'TargetActor', 0x200, \"field "
              "'TargetActor', is-Actor\"}",
              Has(r, t01), GotWant(r, t01));
    }

    // CAND.02, root edges in the default world: both roots, each with its own class.
    Scenario(g_pc0);
    {
        const Edel::CurrentTargetResult& r = FxDetect(kMaxList);
        const Edel::TargetCandidate* e = FxFindCand(r, g_enemy0);
        const Edel::TargetCandidate* lp = FxFindCand(r, g_lp0);
        check("CAND.02 source: Enemy_0 comes from Hero_0 'BP_Hero_C' and LocalPlayer_0 from PC_0 'BP_GameCtrl_C'",
              e && e->sourceObject == g_hero0 && e->sourceClass == "BP_Hero_C" && lp && lp->sourceObject == g_pc0 &&
                  lp->sourceClass == "BP_GameCtrl_C",
              Got(e) + "\n      " + Got(lp));
    }

    // CAND.07: a live game's InternalIndex runs far past 16 bits; the record and the +5 must take all 32.
    FxReset();
    FxSetIndex(g_enemy0, kWideIndex);
    FxClearLog();
    {
        const Edel::CurrentTargetResult& r = FxDetect();
        const Edel::TargetCandidate* c = r.candidates.empty() ? nullptr : &r.candidates[0];
        check("CAND.07 wide index: candidates[0] is Enemy_0 with index 0x18000, reported whole",
              c && c->addr == g_enemy0 && c->index == kWideIndex, FxResultStr(r));
        check("CAND.07 wide index: it still earns the +5 -- score 95, resolved true",
              c && c->score == 95 && r.resolved, FxResultStr(r));
        const auto want = FxCandOf(g_enemy0, 95, g_hero0, "CurrentTarget", FxOff::kSlot0,
                                   Join({ Field("CurrentTarget"), kIsPawn }));
        check("CAND.07 wide index: the rest of the record is DEF.02's -- 'CurrentTarget', 0x200, from Hero_0",
              c && FxCandEq(*c, want), Got(c) + "\n      want " + FxCandStr(want));
    }
}

// ══ DEDUP / ORDER ═══════════════════════════════════════════════════════════════════════════════════════════════
// One record per target: a higher score replaces, a tie keeps the edge seen first. "First" is the scan order -- the
// PC's edges, the pawn's, the PC's components', then the pawn's -- and, within one source, Aura's emission order:
// direct fields before array elements.
static void Dedup() {
    using namespace FxWhy;
    using FxOff::Slot;
    blk("DEDUP / ORDER - one record per target: which edge it keeps, and the order roots and component passes run");
    if (g_dY >= g_dZ) FxFixtureFail("DEDUP.00 needs Y below Z by address");
    Scenario(g_pcD);
    const Edel::CurrentTargetResult& r = FxDetect(kMaxList);

    const auto a = FxCandOf(g_dA, 70, g_pcD, "FocusA", Slot(0), Join({ Field("FocusA"), kIsActor }));
    const auto b = FxCandOf(g_dB, 70, g_pcD, "Hostile1", Slot(2), Join({ Field("Hostile1"), kIsActor }));
    const auto c = FxCandOf(g_dC, 70, g_pcD, "EnemyC", Slot(5), Join({ Field("EnemyC"), kIsActor }));
    const auto n = FxCandOf(g_dN, -10, g_pcD, "LockOnD", Slot(6), Join({ Field("LockOnD"), kNotActor }));
    const auto x = FxCandOf(g_dX, 30, g_pcD, "TargetOwner", Slot(8), Join({ Field("TargetOwner"), kIsActor, kInfra }));
    const auto y = FxCandOf(g_dY, 30, g_heroD, "Ally", Slot(1), kIsActor);
    const auto z = FxCandOf(g_dZ, 30, g_cD1, "Escort2", Slot(1), kIsActor);
    const auto w = FxCandOf(g_dW, 20, g_pcD, "Wingman", FxOff::kDedupWingman, kIsActor);
    // The pawn's own component, met first as one of the pawn's fields: a non-actor at -60 + 10, untracked.
    const auto cD2 = FxCandOf(g_cD2, -50, g_heroD, "CompD2", Slot(2), kNotActor);
    // Y and Z tie on score and offset, so the address puts Y first.
    const FxCands want = { a, b, c, y, z, x, w, n, cD2 };

    check("DEDUP.00 list: exactly A 70, B 70, C 70, Y 30, Z 30, X 30, W 20, N -10, C_D2 -50, every field as derived",
          FxCandsAre(r.candidates, want), ListGot(r, want));
    check("DEDUP.00 note: three tops tied at 70 resolve nothing -- N5", !r.resolved && r.note == FxNote::kN5,
          FxResultStr(r));
    check("DEDUP.00 log: L1 names PC_D, then L5 reads resolved=0 pawn=Hero_D candidates=9",
          ChainLines(g_pcD, g_heroD, false, want.size()), EdelGot());

    check("DEDUP.01 tie keeps the first: FocusA and TargetA both score 70 for A -- A == {70, PC_D, 'PcDedup', "
          "'FocusA', 0x200, \"field 'FocusA', is-Actor\"}",
          Has(r, a), GotWant(r, a));
    check("DEDUP.02a lower never replaces: OwnerB's -20 for B leaves Hostile1's record -- 'Hostile1', 0x210, 70",
          Has(r, b), GotWant(r, b));
    check("DEDUP.02b higher replaces: EnemyC's 70 for C replaces NeutralC's 20 -- 'EnemyC', 0x228, 70", Has(r, c),
          GotWant(r, c));
    check("DEDUP.02c dropped never erases: JunkD's -60 for N leaves LockOnD's record -- 'LockOnD', -10", Has(r, n),
          GotWant(r, n));

    check("ORDER.01a PC root before pawn root: X == {30, PC_D, 'PcDedup', 'TargetOwner', 0x240, \"field "
          "'TargetOwner', is-Actor, infra-field\"}, not the pawn's Buddy",
          Has(r, x), GotWant(r, x));
    check("ORDER.01b pawn root before PC components: Y's source is Hero_D, field 'Ally', not C_D1's Escort",
          Has(r, y), GotWant(r, y));
    check("ORDER.01c PC components before pawn components: Z == {30, C_D1, 'CompEscort', 'Escort2', 0x208, "
          "'is-Actor'}, not C_D2's Escort",
          Has(r, z), GotWant(r, z));
    check("ORDER.01d direct before array: W's record is 'Wingman' at 0x258, not the Squad element at 0x248",
          Has(r, w), GotWant(r, w));
}

// ══ EDGE ════════════════════════════════════════════════════════════════════════════════════════════════════════
// Aura stops collecting once 1024 edges are in the vector, and every emitted edge counts -- the excluded self edges
// and repeats of one object included. The fixture puts one target at edge 1024 and another at edge 1025 behind each of
// Edel's three collections: a root's edges, a root's edges walked for components, and one component's edges.
static void Edge() {
    using namespace FxWhy;
    using FxOff::Slot;
    blk("EDGE - the 1024-edge cap on a root's edges, on its component search and on each component, a fresh one each");

    // PC_H: two self edges, then Crowd -- 1021 Fillers, Pawn_In as edge 1024, Pawn_Out as edge 1025.
    Scenario(g_pcH);
    {
        const Edel::CurrentTargetResult& r = FxDetect(kMaxList);
        const FxCands want = { FxCandOf(g_pawnIn, 35, g_pcH, "Crowd", kElement, kIsPawn) };
        check("EDGE.01a root cap: edge 1024 is considered -- the list is exactly [{Pawn_In, 35, PC_H, 'PcHorde', "
              "'Crowd', -1, 'is-Pawn'}], every Filler dropped at -60",
              FxCandsAre(r.candidates, want), ListGot(r, want));
        check("EDGE.01a root cap: the lone positive candidate resolves -- \"Detected target: Pawn_In (BP_Party_C) — "
              "score 35 via Crowd.\"",
              r.resolved && r.note == "Detected target: Pawn_In (BP_Party_C) — score 35 via Crowd.", FxResultStr(r));
        check("EDGE.01a root cap: L1 names PC_H, then L5 reads resolved=1 pawn=Hero_H candidates=1",
              ChainLines(g_pcH, g_heroH, true, want.size()), EdelGot());
        check("EDGE.01b root cap: edge 1025 is never collected -- Pawn_Out is absent", Absent(r, g_pawnOut),
              FxCandsStr(r.candidates));
    }

    // PC_H2: the same Crowd shape, with the owned components C_in at edge 1024 and C_out at edge 1025.
    Scenario(g_pcH2);
    {
        const Edel::CurrentTargetResult& r = FxDetect(kMaxList);
        const auto tIn = FxCandOf(g_tIn, 80, g_cIn, "TargetActor", FxOff::kSlot0,
                                  Join({ Field("TargetActor"), kIsActor }));
        check("EDGE.02a component search cap: C_in at edge 1024 is found and scanned -- T_in == {80, C_in, "
              "'UTargetHolderComponent', 'TargetActor', 0x200, \"field 'TargetActor', is-Actor\"}",
              Has(r, tIn), GotWant(r, tIn));
        check("EDGE.02a component search cap: the list is exactly [T_in] -- C_in itself drops at -60 as a field",
              FxCandsAre(r.candidates, { tIn }), ListGot(r, { tIn }));
        check("EDGE.02b component search cap: C_out at edge 1025 is never found -- T_out is absent",
              Absent(r, g_tOut), FxCandsStr(r.candidates));
    }

    // PC_H3: C_big's own Crowd -- 1023 Fillers, Pawn_cIn as edge 1024, Pawn_cOut as edge 1025, no self edge ahead.
    Scenario(g_pcH3);
    {
        const Edel::CurrentTargetResult& r = FxDetect(kMaxList);
        const auto pIn = FxCandOf(g_pawnCIn, 45, g_cBig, "Crowd", kElement, kIsPawn);
        const auto filler = FxCandOf(g_filler, -50, g_cBig, "Crowd", kElement, kNotActor);
        check("EDGE.03a component cap: edge 1024 of C_big is considered -- Pawn_cIn == {45, C_big, 'CompHorde', "
              "'Crowd', -1, 'is-Pawn'}",
              Has(r, pIn), GotWant(r, pIn));
        check("EDGE.03a component cap: the Filler is present once, at -50 from C_big",
              CountOf(r, g_filler) == 1 && Has(r, filler), GotWant(r, filler));
        const FxCands want = { pIn, filler };
        check("EDGE.03a component cap: the list is exactly [Pawn_cIn 45, Filler -50]", FxCandsAre(r.candidates, want),
              ListGot(r, want));
        check("EDGE.03a component cap: L1 names PC_H3, then L5 reads resolved=1 pawn=Hero_H3 candidates=2",
              ChainLines(g_pcH3, g_heroH3, true, want.size()), EdelGot());
        check("EDGE.03b component cap: edge 1025 of C_big is never collected -- Pawn_cOut is absent",
              Absent(r, g_pawnCOut), FxCandsStr(r.candidates));
    }

    // PC_H3b: C_big2 fills a vector to the cap; C_after, the next component, holds its target in its SECOND edge,
    // because a full vector reused without a clear still accepts one push.
    Scenario(g_pcH3b);
    {
        const Edel::CurrentTargetResult& r = FxDetect(kMaxList);
        const auto pIn = FxCandOf(g_pawnCIn, 45, g_cBig2, "Crowd", kElement, kIsPawn);
        const auto aDec = FxCandOf(g_aDec, 30, g_cAfter, "Escort", Slot(0), kIsActor);
        const auto tAfter = FxCandOf(g_tAfter, 30, g_cAfter, "Escort2", Slot(1), kIsActor);
        const auto filler = FxCandOf(g_filler, -50, g_cBig2, "Crowd", kElement, kNotActor);
        check("EDGE.03c fresh budget per component: T_after == {30, C_after, 'CompEscort', 'Escort2', 0x208, "
              "'is-Actor'}, C_after's second edge after C_big2 filled 1024",
              Has(r, tAfter), GotWant(r, tAfter));
        check("EDGE.03c fresh budget per component: C_after's first edge stands too -- A_dec == {30, C_after, "
              "'CompEscort', 'Escort', 0x200, 'is-Actor'}",
              Has(r, aDec), GotWant(r, aDec));
        check("EDGE.03c fresh budget per component: the list is exactly [Pawn_cIn 45, A_dec 30, T_after 30, "
              "Filler -50]",
              FxCandsAre(r.candidates, { pIn, aDec, tAfter, filler }), ListGot(r, { pIn, aDec, tAfter, filler }));
        check("EDGE.03c fresh budget per component: C_big2's edge 1025 is still never collected -- Pawn_cOut is absent",
              Absent(r, g_pawnCOut), FxCandsStr(r.candidates));
    }
}

}  // namespace G3

static void Blocks_G3() {
    G3::Score();
    G3::Kw();
    G3::Cand();
    G3::Dedup();
    G3::Edge();
}

// ══ G4: components, cancellation, the sort, the auto-pick, the log and the notes, UE4, read-only and two callers ═══
// main runs Blocks_G4 last of the groups: LOG.07 reads counters the whole run kept, so it closes this group.

namespace G4 {

// ── Edel's score terms: each expected score below is the sum of the signals the design says it earns ──
constexpr int32_t kTermKeyword = 50;     // a kPositive stem in the field name
constexpr int32_t kTermPawn = 30;        // the target classifies as a pawn
constexpr int32_t kTermActor = 15;       // ... or as any other actor
constexpr int32_t kTermInfra = -40;      // a kNegative entry in the field name
constexpr int32_t kTermNotActor = -60;   // neither a pawn nor an actor
constexpr int32_t kTermNear = 10;        // read from the pawn or from a component, not from the controller
constexpr int32_t kTermTracked = 5;      // a readable InternalIndex of 0 or more

constexpr int32_t kScorePawnTarget = kTermKeyword + kTermPawn + kTermNear + kTermTracked;
constexpr int32_t kScoreHeldTarget = kTermKeyword + kTermActor + kTermNear + kTermTracked;   // a component's target
constexpr int32_t kScoreRootKwActor = kTermKeyword + kTermActor + kTermTracked;
constexpr int32_t kScoreRootActor = kTermActor + kTermTracked;
constexpr int32_t kScoreRootPawn = kTermPawn + kTermTracked;
constexpr int32_t kScoreRootNonActor = kTermNotActor + kTermTracked;
constexpr int32_t kScoreNearUntracked = kTermNotActor + kTermNear;   // a component a pawn field holds, or a ghost
constexpr int32_t kScoreNearKwActorUntracked = kTermKeyword + kTermActor + kTermNear;
constexpr int32_t kScoreNearOwnerPawnUntracked = kTermPawn + kTermInfra + kTermNear;
constexpr int32_t kScoreNearKwNonActor = kTermKeyword + kTermNotActor + kTermNear + kTermTracked;
// The numbers the design states: a term above that drifted from them stops the build, not a check.
static_assert(kScorePawnTarget == 95 && kScoreHeldTarget == 80 && kScoreRootKwActor == 70 && kScoreRootActor == 20 &&
                  kScoreRootPawn == 35 && kScoreRootNonActor == -55 && kScoreNearUntracked == -50 &&
                  kScoreNearKwActorUntracked == 75 && kScoreNearOwnerPawnUntracked == 0 && kScoreNearKwNonActor == 5,
              "the design's scores");

// ── the calls' arguments and the scenarios' tweaks ──
constexpr int32_t kSeeAll = 64;                    // Edel's ceiling: every list a check compares whole is shorter
constexpr int32_t kAboveCeiling = kSeeAll + 1;     // LOG.05: CLAMP.05's call that is clamped
constexpr int32_t kClampZero = 0;                  // LOG.05: CLAMP.05's call that falls back to the default
constexpr int32_t kTopOnly = 1;                    // PICK.06: a list cut to one
constexpr int32_t kCutTwo = 2, kCutThree = 3;      // SORT.04: cuts that end inside the four-way tie
constexpr int32_t kElement = -1;                   // a container element's fieldOffset: it lives in a heap buffer
constexpr int32_t kAmTrackedIndex = 3;             // PICK.02b: A_m tracked, so the +5 narrows the margin to 15
constexpr int32_t kPz0TrackedIndex = 2;            // PICK.03b: P_z0 tracked, so the top rises from 0 to 5
constexpr int32_t kNullSerial = 0;                 // UE reads a weak pointer with serial 0 as null
constexpr int32_t kEmptyNum = 0;                   // a TArray with nothing in it
constexpr int kThreads = 2, kCallsPerThread = 25;  // CONC.01, as the design sizes it
constexpr const char* kHolderClass = "UTargetHolderComponent";
constexpr const char* kTargetField = "TargetActor";   // UTargetHolderComponent's one field

// ── helpers ──
static std::string Why(const std::string& field, const char* kind) {
    return FxWhy::Join({ FxWhy::Field(field), kind });
}

// A scenario's start: the clean slate, then the chain's controller.
static void Begin(uintptr_t pc) {
    FxReset();
    FxLpController(pc);
}
// The run itself, after the scenario's last setup call: the log cleared, so it holds this run's lines only.
static const Edel::CurrentTargetResult& Detect(int32_t maxCandidates = kDefaultMaxCandidates) {
    FxClearLog();
    return FxDetect(maxCandidates);
}

// A UTargetHolderComponent's TargetActor as Edel records it: read near combat, from the component that holds it.
static Edel::TargetCandidate HeldTarget(uintptr_t target, uintptr_t holder) {
    return FxCandOf(target, kScoreHeldTarget, holder, kTargetField, FxOff::kSlot0, Why(kTargetField, FxWhy::kIsActor));
}
// A non-actor met near combat with no index bonus: a component a pawn field holds, or a target that cannot be read.
static Edel::TargetCandidate NearNonActor(uintptr_t target, uintptr_t src, const std::string& field, int32_t off) {
    return FxCandOf(target, kScoreNearUntracked, src, field, off, FxWhy::kNotActor);
}

// DEF.02's records, DEF.03's note typed out (the string the C# stubs imitate), and DEF.04's lines.
static FxCands DefaultCands() {
    return { FxCandOf(g_enemy0, kScorePawnTarget, g_hero0, "CurrentTarget", FxOff::kSlot0,
                      Why("CurrentTarget", FxWhy::kIsPawn)),
             FxCandOf(g_lp0, kScoreRootNonActor, g_pc0, "Player", FxOff::kPlayer, FxWhy::kNotActor) };
}
static std::string DefNote() {
    return std::string("Detected target: Enemy_0 (BP_Enemy_C) ") + kEmDash + " score 95 via CurrentTarget.";
}
static Edel::CurrentTargetResult DefaultResult() {
    Edel::CurrentTargetResult r;
    r.resolved = true;
    r.world = g_world0;
    r.playerController = g_pc0;
    r.playerPawn = g_hero0;
    r.candidates = DefaultCands();
    r.note = DefNote();
    return r;
}
static std::vector<FxExpLine> DefaultLines() {
    return { { 'I', FxLine::L1(g_pc0) }, { 'I', FxLine::L5(true, g_pc0, g_hero0, DefaultCands().size()) } };
}

// The first n party pawns as CLAMP records them: bare pawns, array elements of PC_Party's Party.
static FxCands PartyRecords(int n) {
    FxCands out;
    for (uintptr_t p : FxFirstParty(n))
        out.push_back(FxCandOf(p, kScoreRootPawn, g_pcParty, "Party", kElement, FxWhy::kIsPawn));
    return out;
}
// N4 for a target the fixture built, its name and class read from the fixture's own table.
static std::string PickNote(uintptr_t target, int32_t score, const std::string& field) {
    return FxNote::N4(FxNameText(target), FxClassNameText(target), score, field);
}

static bool ChainIs(const Edel::CurrentTargetResult& r, uintptr_t world, uintptr_t pc, uintptr_t pawn) {
    return r.world == world && r.playerController == pc && r.playerPawn == pawn;
}
static bool Descending(const Edel::CurrentTargetResult& r) {
    for (size_t i = 1; i < r.candidates.size(); ++i)
        if (r.candidates[i - 1].score < r.candidates[i].score) return false;
    return true;
}
// Where addr sits in the list; -1 when it is absent.
static int Position(const Edel::CurrentTargetResult& r, uintptr_t addr) {
    for (size_t i = 0; i < r.candidates.size(); ++i)
        if (r.candidates[i].addr == addr) return static_cast<int>(i);
    return -1;
}
// "" when every wanted record is in the list exactly as wanted; otherwise each one that is absent or differs.
static std::string Missing(const Edel::CurrentTargetResult& r, const FxCands& want) {
    std::string out;
    for (const auto& w : want) {
        const Edel::TargetCandidate* c = FxFindCand(r, w.addr);
        if (!c) out += "\n      absent: " + FxCandStr(w);
        else if (!FxCandEq(*c, w)) out += "\n      differs: " + FxCandStr(*c) + "\n         want: " + FxCandStr(w);
    }
    return out;
}

static void CandsAre(const std::string& label, const Edel::CurrentTargetResult& r, const FxCands& want) {
    check(label, FxCandsAre(r.candidates, want), FxCandsStr(r.candidates) + "\n    want " + FxCandsStr(want));
}
static void HasCand(const std::string& label, const Edel::CurrentTargetResult& r, const Edel::TargetCandidate& want) {
    const Edel::TargetCandidate* c = FxFindCand(r, want.addr);
    check(label, c != nullptr && FxCandEq(*c, want),
          (c ? FxCandStr(*c) : std::string("absent")) + "\n    want " + FxCandStr(want));
}
static void Lacks(const std::string& label, const Edel::CurrentTargetResult& r, uintptr_t addr) {
    check(label, FxFindCand(r, addr) == nullptr, FxCandsStr(r.candidates));
}
static void NoteIs(const std::string& label, const Edel::CurrentTargetResult& r, const std::string& want) {
    check(label, r.note == want, "'" + r.note + "'\n    want '" + want + "'");
}
static void LinesAre(const std::string& label, const std::vector<FxExpLine>& want) {
    check(label, FxEdelLinesAre(want), FxLinesStr(FxEdelLines()));
}

// The digits %llX prints, built without printf: uppercase, no leading zeros.
static std::string UpperHexDigits(uintptr_t v) {
    static const char kDigits[] = "0123456789ABCDEF";
    constexpr unsigned kNibbleBits = 4;
    constexpr uintptr_t kNibbleMask = 0xF;
    std::string s;
    do {
        s.insert(s.begin(), kDigits[v & kNibbleMask]);
        v >>= kNibbleBits;
    } while (v);
    return s;
}
static bool HasHexLetter(uintptr_t v) { return UpperHexDigits(v).find_first_of("ABCDEF") != std::string::npos; }
// Every "Edel:" line in any category: LOG.01 asks which category they land in, so it cannot read through the WALK
// filter FxEdelLines applies.
static std::vector<FxLogLine> EdelAnyCategory() {
    std::vector<FxLogLine> out;
    for (auto& l : FxLogs())
        if (FxStartsWith(l.text, kEdelPrefix)) out.push_back(std::move(l));
    return out;
}
static bool AllInWalk(const std::vector<FxLogLine>& lines) {
    for (const auto& l : lines)
        if (l.cat != kEdelCat) return false;
    return true;
}
static std::string FirstDiff(const std::vector<uint8_t>& a, const std::vector<uint8_t>& b) {
    if (a.size() != b.size()) return FxFmt("the snapshot changed size: %zu -> %zu", a.size(), b.size());
    for (size_t i = 0; i < a.size(); ++i)
        if (a[i] != b[i]) return FxFmt("byte %zu of the snapshot changed: 0x%02X -> 0x%02X", i, a[i], b[i]);
    return std::string();
}

// CANCEL.04: once a reset has lowered the cancel, the default world resolves exactly as DEF.01-DEF.04. The seam is
// armed for this run, so a silent seam shows the default world never touches the cancel page.
static void Relowered(const char* after) {
    FxReset();
    FxCancelTrap(true);
    const Edel::CurrentTargetResult r = Detect();
    const std::string id = std::string("CANCEL.04 after ") + after;
    check(id + ": the default world resolves exactly as DEF.01-DEF.03", FxResultEq(r, DefaultResult()), FxResultStr(r));
    check(id + ": DEF.04's lines -- L1 then L5 -- and no scan", FxEdelLinesAre(DefaultLines()) && FxScanLines().empty(),
          FxLinesStr(FxEdelLines()) + FxLogDump(0, kScanCat));
    check(id + ": the seam stays silent", FxCancelHits() == 0, FxS(FxCancelHits()));
}

// ══ COMP ════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockComp() {
    blk("COMP - owned components: the ownership and kind tests, 16 per root, one scan per component, near combat");
    FxReset();

    // ── COMP.01: PC_K holds an unowned component, an owned actor and an owned component ──
    {
        Begin(g_pcK);
        const auto& r = Detect();
        const auto t1 = HeldTarget(g_t1K, g_c1K);
        const auto oa = FxCandOf(g_oa, kScoreRootActor, g_pcK, "OwnedActor", FxOff::Slot(1), FxWhy::kIsActor);
        HasCand("COMP.01a owned only: T_1 is read through C1, which PC_K owns, with C1 as its source", r, t1);
        Lacks("COMP.01a owned only: T_u is absent -- U_comp belongs to Enemy_0, so it is never scanned", r, g_tU);
        Lacks("COMP.01b components only: T_oa is absent -- OA is an owned actor, never descended into", r, g_tOa);
        HasCand("COMP.01b components only: OA itself is a controller candidate, an actor through a neutral field", r,
                oa);
        CandsAre("COMP.01 the whole list: [T_1, OA] -- the components the controller points at fall at -60", r,
                 { t1, oa });
    }

    // ── COMP.02a: 17 owned components on PC_B behind an unowned one and an actor; one owned one on Hero_B ──
    {
        Begin(g_pcB);
        const auto& r = Detect(kSeeAll);
        FxCands held;
        for (int i = 0; i < kComponentBudget; ++i) held.push_back(HeldTarget(g_t17[i], g_c17[i]));
        const auto tp1 = HeldTarget(g_tp1, g_pc1);
        const auto ax = FxCandOf(g_aX, kScoreRootActor, g_pcB, "Prop", FxOff::Slot(1), FxWhy::kIsActor);
        const auto pc1 = NearNonActor(g_pc1, g_heroB, "PawnComp", FxOff::kSlot0);
        const std::string missing = Missing(r, held);
        check("COMP.02a 16 per root: T01..T16 are each read through their own component, C01..C16", missing.empty(),
              missing);
        Lacks("COMP.02a 16 per root: T17 is absent -- C17 is PC_B's 17th owned component", r, g_t17[kComp17 - 1]);
        HasCand("COMP.02a 16 per root: Tp1 is present -- the pawn's component has a budget of its own", r, tp1);
        Lacks("COMP.02a 16 per root: T_u2 is absent -- U_comp2 is not PC_B's, so it is skipped", r, g_tU2);
        FxCands all = held;
        all.push_back(tp1);
        all.push_back(ax);
        all.push_back(pc1);
        CandsAre("COMP.02a the whole list: T01..T16 and Tp1 (tied at 80, by address), then A_x, then PC1", r, all);
        const Edel::TargetCandidate* t01 = FxFindCand(r, g_t17[0]);
        check("COMP.04 near combat: T01 scores 80 = 50 + 15 + 10 + 5, with C01, a UTargetHolderComponent, as source",
              t01 && t01->score == kScoreHeldTarget && t01->sourceObject == g_c17[0] &&
                  t01->sourceClass == kHolderClass,
              t01 ? FxCandStr(*t01) : std::string("absent"));
        HasCand("COMP.06 also a root candidate: PC1, scanned as Hero_B's component, is scored through PawnComp too", r,
                pc1);
    }

    // ── COMP.02b: 16 empty owned components on PC_B2; 16 on Hero_B2, of which only the last holds a target ──
    {
        Begin(g_pcB2);
        const auto& r = Detect(kSeeAll);
        const auto tp16b = HeldTarget(g_tp16b, g_pb2[kComponentBudget - 1]);
        HasCand("COMP.02b independent budgets: Tp16b is read through PB2_16, the pawn's 16th component, after the "
                "controller spent all 16 of its own",
                r, tp16b);
        FxCands all{ tp16b };
        for (int i = 0; i < kComponentBudget; ++i)
            all.push_back(NearNonActor(g_pb2[i], g_heroB2, FxFmt("PawnComp%02d", i + 1), FxOff::Slot(i)));
        CandsAre("COMP.02b the whole list: Tp16b, then PB2_01..PB2_16 by offset, each held by a pawn field", r, all);
    }

    // ── COMP.03: C3_01 twice, C3_02..C3_15, then C_sh on PC_3; C_sh again first on Hero_3, then P3_01..P3_16 ──
    {
        Begin(g_pc3);
        const auto& r = Detect(kSeeAll);
        const auto tp16 = HeldTarget(g_tp16, g_p3[kComponentBudget - 1]);
        const auto tsh = HeldTarget(g_tSh, g_cSh);
        const auto csh = NearNonActor(g_cSh, g_hero3, "Shared", FxOff::kSlot0);
        HasCand("COMP.03 one scan each: Tp16 is present -- neither C3_01's repeat nor C_sh met again on the pawn spent "
                "a budget slot",
                r, tp16);
        HasCand("COMP.03 one scan each: T_sh is present, with C_sh as its source", r, tsh);
        FxCands all{ tsh, tp16, csh };
        for (int i = 0; i < kComponentBudget; ++i)
            all.push_back(NearNonActor(g_p3[i], g_hero3, FxFmt("PawnComp%02d", i + 1), FxOff::Slot(1 + i)));
        CandsAre("COMP.03 the whole list: T_sh, Tp16, then C_sh and P3_01..P3_16 by offset", r, all);
        HasCand("COMP.06 also a root candidate: C_sh, scanned under PC_3, is scored through Hero_3's Shared too", r,
                csh);
    }

    // ── COMP.07: PC_Q caches the pawn's component, which PC_Q does not own and Hero_Q does ──
    {
        Begin(g_pcQ);
        const auto& r = Detect();
        const auto tq = HeldTarget(g_tQ, g_cQ);
        HasCand("COMP.07 turned away, then owned: T_q is read through C_q -- the controller's pass rejected C_q, and "
                "the pawn's pass still scans it",
                r, tq);
        CandsAre("COMP.07 the whole list: [T_q, C_q], C_q scored through Hero_Q's LockComp", r,
                 { tq, NearNonActor(g_cQ, g_heroQ, "LockComp", FxOff::kSlot0) });
    }
}

// ══ CANCEL ══════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockCancel() {
    blk("CANCEL - Tot polled after the controller's scan and on entry to each component pass; a bound connection");
    FxReset();

    // ── CANCEL.01: raised before the call, the chain intact ──
    {
        FxReset();
        Tot::RequestPerCommand();
        const auto& r = Detect();
        NoteIs("CANCEL.01 raised before: the note is 'Cancelled.'", r, FxNote::kCancelled);
        check("CANCEL.01 raised before: unresolved, the chain still standing -- World_0, PC_0, Hero_0",
              !r.resolved && ChainIs(r, g_world0, g_pc0, g_hero0), FxResultStr(r));
        check("CANCEL.01 raised before: the controller's scored edges are discarded -- no candidate",
              r.candidates.empty(), FxCandsStr(r.candidates));
        LinesAre("CANCEL.01 raised before: L1 naming PC_0, and no L5", { { 'I', FxLine::L1(g_pc0) } });
        check("CANCEL.01 raised before: no PIPE:find line at all -- the chain held, so no scan ran",
              FxLogPrefix(std::string(), 0, kScanCat) == 0, FxLogDump(0, kScanCat));
    }
    Relowered("CANCEL.01");

    // ── CANCEL.02: raised inside the controller's scan -- PC_X's Trap points into the cancel page ──
    {
        Begin(g_pcX);
        FxCancelTrap(true);
        const auto& r = Detect();
        check("CANCEL.02 inside the PC scan: the seam fired", FxCancelHits() >= 1, FxS(FxCancelHits()));
        NoteIs("CANCEL.02 inside the PC scan: the note is 'Cancelled.'", r, FxNote::kCancelled);
        check("CANCEL.02 inside the PC scan: no candidate and unresolved, with PC_X and Hero_X resolved",
              r.candidates.empty() && !r.resolved && ChainIs(r, g_world0, g_pcX, g_heroX), FxResultStr(r));
        LinesAre("CANCEL.02 inside the PC scan: L1 naming PC_X, and no L5", { { 'I', FxLine::L1(g_pcX) } });
    }
    Relowered("CANCEL.02");

    // ── CANCEL.03: raised inside the pawn's scan -- Hero_Y's Trap, ahead of its component and its CurrentTarget ──
    {
        Begin(g_pcY);
        FxCancelTrap(true);
        const auto& r = Detect();
        const auto enemy = FxCandOf(g_enemyY, kScorePawnTarget, g_heroY, "CurrentTarget", FxOff::Slot(2),
                                    Why("CurrentTarget", FxWhy::kIsPawn));
        const auto ghost = NearNonActor(FxCancelTarget(), g_heroY, "Trap", FxOff::Slot(0));
        const auto py = NearNonActor(g_pY, g_heroY, "PawnComp", FxOff::Slot(1));
        check("CANCEL.03 inside the pawn scan: the seam fired", FxCancelHits() >= 1, FxS(FxCancelHits()));
        CandsAre("CANCEL.03 inside the pawn scan: [Enemy_Y, the cancel-page ghost, P_y] -- the pawn's scan runs on "
                 "to its end",
                 r, { enemy, ghost, py });
        Lacks("CANCEL.03 inside the pawn scan: T_y is absent -- the controller's component pass polls Tot on entry "
              "(COMP.05)",
              r, g_tY);
        Lacks("CANCEL.03 inside the pawn scan: Tp_y is absent -- so does the pawn's (COMP.05)", r, g_tpY);
        check("CANCEL.03 inside the pawn scan: resolved, with the ordinary pick note (FIND-3, pinned as today)",
              r.resolved && r.note == PickNote(g_enemyY, kScorePawnTarget, "CurrentTarget"), FxResultStr(r));
        LinesAre("CANCEL.03 inside the pawn scan: L1 naming PC_Y, then L5 ending candidates=3",
                 { { 'I', FxLine::L1(g_pcY) }, { 'I', FxLine::L5(true, g_pcY, g_heroY, 3) } });
    }
    Relowered("CANCEL.03");

    // ── CANCEL.03b: raised inside the controller's component loop -- C_za's Trap, ahead of C_zb ──
    {
        Begin(g_pcZ);
        FxCancelTrap(true);
        const auto& r = Detect();
        const auto tzb = HeldTarget(g_tZb, g_cZb);
        const auto ghost = NearNonActor(FxCancelTarget(), g_cZa, "Trap", FxOff::kSlot0);
        const auto pz = NearNonActor(g_pZ, g_heroZ, "PawnComp", FxOff::kCancel4PawnComp);
        check("CANCEL.03b inside the component loop: the seam fired", FxCancelHits() >= 1, FxS(FxCancelHits()));
        HasCand("CANCEL.03b inside the component loop: T_zb is present -- the loop itself does not poll", r, tzb);
        HasCand("CANCEL.03b inside the component loop: the ghost is present, read from C_za's Trap", r, ghost);
        Lacks("CANCEL.03b inside the component loop: Tp_z is absent -- the pawn's pass polls on entry (COMP.05)", r,
              g_tpZ);
        CandsAre("CANCEL.03b the whole list: [T_zb, the ghost (0x200), P_z (0x208)]", r, { tzb, ghost, pz });
        LinesAre("CANCEL.03b inside the component loop: L1 naming PC_Z, then L5",
                 { { 'I', FxLine::L1(g_pcZ) }, { 'I', FxLine::L5(true, g_pcZ, g_heroZ, 3) } });
    }
    Relowered("CANCEL.03b");

    // ── CANCEL.05: the cancelled instance scan (SCAN.05's setup) ──
    {
        FxReset();
        FxBreakChain(FxBreak::NoGameInstance);
        Tot::RequestPerCommand();
        const auto& r = Detect();
        check("CANCEL.05 cancelled scan: no controller, and the note is N1, not 'Cancelled.' (FIND-3, pinned as today)",
              r.playerController == 0 && r.note == FxNote::kN1, FxResultStr(r));
        check("CANCEL.05 cancelled scan: Aura's own abort WARN once, then the walk's one summary line (0 found)",
              FxLogCount(kScanAborted, 'W', kScanCat) == 1 && FxScanLines().size() == 1u, FxLogDump(0, kScanCat));
        check("CANCEL.05 cancelled scan: no Edel line", FxEdelLines().empty(), FxLinesStr(FxEdelLines()));
    }

    // ── CANCEL.06: shutdown, with the per-command flag low ──
    {
        FxReset();
        Tot::RequestShutdown();
        const auto& r = Detect();
        check("CANCEL.06 shutdown: precondition -- the per-command flag is low", !Tot::g_perCommand.load());
        NoteIs("CANCEL.06 shutdown: the note is 'Cancelled.'", r, FxNote::kCancelled);
        check("CANCEL.06 shutdown: no candidate", r.candidates.empty(), FxCandsStr(r.candidates));
        LinesAre("CANCEL.06 shutdown: L1 naming PC_0, and no L5", { { 'I', FxLine::L1(g_pc0) } });
    }
    Relowered("CANCEL.06");

    // ── CANCEL.07: a bound connection's own flag decides, not the process-wide one ──
    {
        FxReset();
        FxBindConnection(true);
        const auto& r = Detect();
        check("CANCEL.07a bound and raised: precondition -- the process-wide per-command flag is low",
              !Tot::g_perCommand.load());
        NoteIs("CANCEL.07a bound and raised: this connection's own cancel stops the run -- 'Cancelled.'", r,
               FxNote::kCancelled);
        check("CANCEL.07a bound and raised: no candidate", r.candidates.empty(), FxCandsStr(r.candidates));
        LinesAre("CANCEL.07a bound and raised: L1 naming PC_0, and no L5", { { 'I', FxLine::L1(g_pc0) } });
    }
    Relowered("CANCEL.07a");
    {
        FxReset();
        FxBindConnection(false);
        Tot::RequestPerCommand();
        const auto& r = Detect();
        check("CANCEL.07b bound and low, another client gone: the default world resolves exactly as DEF.01-DEF.03",
              FxResultEq(r, DefaultResult()), FxResultStr(r));
        LinesAre("CANCEL.07b bound and low, another client gone: DEF.04's lines -- L1 then L5", DefaultLines());
    }
    {
        Begin(g_pcB);
        FxBindConnection(false);
        Tot::RequestPerCommand();
        const auto& r = Detect(kSeeAll);
        FxCands want;
        for (int i = 0; i < kComponentBudget; ++i) want.push_back(HeldTarget(g_t17[i], g_c17[i]));
        want.push_back(HeldTarget(g_tp1, g_pc1));
        const std::string missing = Missing(r, want);
        check("CANCEL.07b bound and low, another client gone: both component passes still run -- T01..T16 and Tp1 "
              "all present",
              missing.empty(), missing);
    }
    Relowered("CANCEL.07b");
}

// ══ SORT ════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockSort() {
    blk("SORT - score descending, a direct field before an element on a tie, then the address; the cut after the sort");
    FxReset();

    // ── SORT.01: the default world and the SCORE world, each best-first ──
    {
        FxReset();
        const Edel::CurrentTargetResult def = Detect();
        check("SORT.01 score descending: the default world lists Enemy_0 (95) before LocalPlayer_0 (-55)",
              FxAddrsAre(def, { g_enemy0, g_lp0 }) && Descending(def), FxCandsStr(def.candidates));
        Begin(g_pcS);
        const auto& r = Detect(kSeeAll);
        check("SORT.01 score descending: SCORE.00's 14 in its order, each score at least the next",
              FxAddrsAre(r, { g_pT, g_aT, g_pS, g_aP2, g_aV, g_aP, g_aB, g_aN, g_pO, g_nL, g_aO, g_nG, g_nD, g_nK }) &&
                  Descending(r),
              FxCandsStr(r.candidates));
    }

    // ── SORT.02 / SORT.03: four ties at 70 -- two direct fields, then an array emitted A4 first ──
    const FxCands sorted = {
        FxCandOf(g_a1, kScoreRootKwActor, g_pcO, "TargetA1", FxOff::Slot(0), Why("TargetA1", FxWhy::kIsActor)),
        FxCandOf(g_a2, kScoreRootKwActor, g_pcO, "TargetA2", FxOff::Slot(1), Why("TargetA2", FxWhy::kIsActor)),
        FxCandOf(g_a3, kScoreRootKwActor, g_pcO, "Targets", kElement, Why("Targets", FxWhy::kIsActor)),
        FxCandOf(g_a4, kScoreRootKwActor, g_pcO, "Targets", kElement, Why("Targets", FxWhy::kIsActor)),
    };
    {
        Begin(g_pcO);
        const auto& r = Detect(kSeeAll);
        CandsAre("SORT.02 field before element: exactly [A1 at 0x200, A2 at 0x208, A3 at -1, A4 at -1]", r, sorted);
        check("SORT.03 address breaks the last tie: precondition -- A4 is emitted first, and A3 lies below it",
              g_sortBuf[0] == g_a4 && g_a3 < g_a4, FxAddrsStr({ g_sortBuf[0], g_sortBuf[1] }));
        const int p3 = Position(r, g_a3), p4 = Position(r, g_a4);
        check("SORT.03 address breaks the last tie: A3 comes before A4", p3 >= 0 && p4 >= 0 && p3 < p4,
              FxAddrsStr(FxCandAddrs(r)));
    }

    // ── SORT.04: the cut comes after the sort ──
    {
        Begin(g_pcO);
        const Edel::CurrentTargetResult two = Detect(kCutTwo);
        CandsAre("SORT.04 cut after sort: 2 gives [A1, A2]", two, { sorted[0], sorted[1] });
        const auto& three = Detect(kCutThree);
        CandsAre("SORT.04 cut after sort: 3 gives [A1, A2, A3]", three, { sorted[0], sorted[1], sorted[2] });
    }
}

// ══ PICK ════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockPick() {
    blk("PICK - the auto-pick: a lone positive, the margin of 20, a top of 0, ties, nothing, the cut, a component");
    FxReset();

    // ── PICK.01: the default world with PC_0's Player cleared (the pool already reset it) ──
    {
        FxReset();
        FxSetPtr(g_pc0, FxOff::kPlayer, 0);
        const auto& r = Detect();
        CandsAre("PICK.01 lone positive: the list is DEF.02's Enemy_0 record alone", r, { DefaultCands()[0] });
        check("PICK.01 lone positive: resolved", r.resolved, FxResultStr(r));
        NoteIs("PICK.01 lone positive: DEF.03's note", r, DefNote());
        check("PICK.01 lone positive: L5 ends candidates=1",
              FxEdelCount(FxLine::L5(true, g_pc0, g_hero0, 1), 'I') == 1, FxLinesStr(FxEdelLines()));
    }

    // ── PICK.02: Enemy_M 95 against A_m, 75 untracked and 80 tracked ──
    const auto enemyM = FxCandOf(g_enemyM, kScorePawnTarget, g_heroM, "CurrentTarget", FxOff::Slot(0),
                                 Why("CurrentTarget", FxWhy::kIsPawn));
    {
        Begin(g_pcM);
        const auto& r = Detect();
        CandsAre("PICK.02a margin 20: [Enemy_M 95, A_m 75]", r,
                 { enemyM, FxCandOf(g_aM, kScoreNearKwActorUntracked, g_heroM, "TargetProp", FxOff::Slot(1),
                                    Why("TargetProp", FxWhy::kIsActor)) });
        check("PICK.02a margin 20: exactly 20 resolves", r.resolved, FxResultStr(r));
        NoteIs("PICK.02a margin 20: the pick note names Enemy_M", r,
               PickNote(g_enemyM, kScorePawnTarget, "CurrentTarget"));
    }
    {
        Begin(g_pcM);
        FxSetIndex(g_aM, kAmTrackedIndex);
        const auto& r = Detect();
        CandsAre("PICK.02b margin 15: [Enemy_M 95, A_m 80 with index 3]", r,
                 { enemyM, FxCandOf(g_aM, kScoreNearKwActorUntracked + kTermTracked, g_heroM, "TargetProp",
                                    FxOff::Slot(1), Why("TargetProp", FxWhy::kIsActor)) });
        check("PICK.02b margin 15: does not resolve", !r.resolved, FxResultStr(r));
        NoteIs("PICK.02b margin 15: N5", r, FxNote::kN5);
    }

    // ── PICK.03: a lone pawn through an infra field, top 0 untracked and 5 tracked ──
    const std::string ownerWhy = FxWhy::Join({ FxWhy::kIsPawn, FxWhy::kInfra });
    {
        Begin(g_pcZ0);
        const auto& r = Detect();
        CandsAre("PICK.03a top of 0: [P_z0 0, index -7, 'is-Pawn, infra-field']", r,
                 { FxCandOf(g_pZ0, kScoreNearOwnerPawnUntracked, g_heroZ0, "OwnerPawn", FxOff::kSlot0, ownerWhy) });
        check("PICK.03a top of 0: does not resolve", !r.resolved, FxResultStr(r));
        NoteIs("PICK.03a top of 0: N5 -- one weak candidate, not N6", r, FxNote::kN5);
    }
    {
        Begin(g_pcZ0);
        FxSetIndex(g_pZ0, kPz0TrackedIndex);
        const auto& r = Detect();
        CandsAre("PICK.03b top of 5: [P_z0 5, index 2]", r,
                 { FxCandOf(g_pZ0, kScoreNearOwnerPawnUntracked + kTermTracked, g_heroZ0, "OwnerPawn", FxOff::kSlot0,
                            ownerWhy) });
        check("PICK.03b top of 5: resolves", r.resolved, FxResultStr(r));
        NoteIs("PICK.03b top of 5: the pick note names P_z0, score 5", r,
               PickNote(g_pZ0, kScoreNearOwnerPawnUntracked + kTermTracked, "OwnerPawn"));
    }

    // ── PICK.04 / PICK.06: the party, cut to 8 and to 1 ──
    {
        Begin(g_pcParty);
        const auto& r = Detect(kDefaultMaxCandidates);
        CandsAre("PICK.04 equal tops: Party_00..Party_07, all 35", r, PartyRecords(kDefaultMaxCandidates));
        check("PICK.04 equal tops: does not resolve", !r.resolved, FxResultStr(r));
        NoteIs("PICK.04 equal tops: N5", r, FxNote::kN5);
    }
    {
        Begin(g_pcParty);
        const auto& r = Detect(kTopOnly);
        CandsAre("PICK.06 margin after the cut: one record, Party_00", r, PartyRecords(kTopOnly));
        check("PICK.06 margin after the cut: a lone survivor resolves (FIND-2, pinned as today)", r.resolved,
              FxResultStr(r));
        NoteIs("PICK.06 margin after the cut: the pick note names Party_00", r,
               PickNote(g_party[0], kScoreRootPawn, "Party"));
    }

    // ── PICK.05: nothing target-like; N6 names the Instances tab by its UI label ──
    {
        Begin(g_pcE);
        const auto& r = Detect();
        const std::string label = FxInstancesTabLabel();
        check("PICK.05 nothing target-like: the Instances tab's label was read from en.axaml", !label.empty(),
              FxStringsPath());
        const std::string want = std::string("Player resolved but no target-like reference found. Lock onto / click "
                                             "an enemy, then Detect again ") +
                                 kEmDash + " or search the " + label + " tab for the enemy's class.";
        NoteIs("PICK.05 nothing target-like: N6 sends the user to the tab by its current UI label", r, want);
        check("PICK.05 nothing target-like: no candidate, unresolved", r.candidates.empty() && !r.resolved,
              FxResultStr(r));
        check("PICK.05 nothing target-like: L5 ends candidates=0",
              FxEdelCount(FxLine::L5(false, g_pcE, g_heroE, 0), 'I') == 1, FxLinesStr(FxEdelLines()));
    }

    // ── PICK.07: a component through a positive pawn field, alone ──
    {
        Begin(g_pcF);
        const auto& r = Detect();
        const auto tcf = FxCandOf(g_tcF, kScoreNearKwNonActor, g_heroF, "TargetingComponent", FxOff::kSlot0,
                                  Why("TargetingComponent", FxWhy::kNotActor));
        CandsAre("PICK.07 a component auto-picks: [TC_f 5, index 4, through the pawn] (COMP.06)", r, { tcf });
        check("PICK.07 a component auto-picks: resolved (FIND-1, pinned as today)", r.resolved, FxResultStr(r));
    }
}

// ══ LOG ═════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockLog() {
    blk("LOG - Edel's five lines word for word, in category WALK, one L5 per completed run, none on an early return");
    FxReset();

    // ── LOG.01: L1 in the default world, and under HOP.01 ──
    {
        FxReset();
        const auto& r = Detect();
        const std::vector<FxLogLine> any = EdelAnyCategory();
        const std::string want = "Edel: local PC 0x" + UpperHexDigits(g_pc0) + " via GWorld chain";
        check("LOG.01 L1: the run is DEF's", r.resolved && r.playerController == g_pc0, FxResultStr(r));
        check("LOG.01 L1: the first Edel line is INFO under the literal category WALK, naming PC_0 in %llX digits",
              !any.empty() && any[0].lvl == 'I' && any[0].cat == kEdelCat && any[0].text == want,
              FxLinesStr(any) + "\n    want " + want);
        check("LOG.01 L1: every 'Edel:' line, whatever its category, lands in WALK -- both of DEF's",
              any.size() == DefaultLines().size() && AllInWalk(any), FxLinesStr(any));
        check("LOG.01 L1: PC_0's address spells a hex letter, so the case of the digits is under test",
              HasHexLetter(g_pc0), UpperHexDigits(g_pc0));
    }
    {
        Begin(g_dbgPc0);
        const auto& r = Detect();
        const std::vector<FxLogLine> any = EdelAnyCategory();
        const std::string want = "Edel: local PC 0x" + UpperHexDigits(g_dbgPc0) + " via GWorld chain";
        check("LOG.01 L1 under HOP.01: the first line names DbgPC_0, the controller before the hop",
              !any.empty() && any[0].lvl == 'I' && any[0].cat == kEdelCat && any[0].text == want,
              FxLinesStr(any) + "\n    want " + want);
        check("LOG.05 L5 under HOP.01: exactly one, naming PC_0 after the hop and Hero_0",
              r.playerController == g_pc0 && FxEdelPrefixCount(FxLine::kL5Prefix) == 1 &&
                  FxEdelCount(FxLine::L5(true, g_pc0, g_hero0, DefaultCands().size()), 'I') == 1,
              FxLinesStr(FxEdelLines()));
    }

    // ── LOG.02 .. LOG.04: the instance scan's three picks (the chain broken at the game instance) ──
    {
        FxReset();
        FxBreakChain(FxBreak::NoGameInstance);
        Detect();
        const std::vector<FxLogLine> lines = FxEdelLines();
        check("LOG.02 L2: SCAN.01's first Edel line is INFO 'Edel: local PC 0x<PC_0> via instance scan (Player set)'",
              !lines.empty() && lines[0].lvl == 'I' && lines[0].text == FxLine::L2(g_pc0), FxLinesStr(lines));
    }
    {
        FxReset();
        FxPool(FxPoolVariant::NoPc0);
        FxBreakChain(FxBreak::NoGameInstance);
        Detect();
        const std::vector<FxLogLine> lines = FxEdelLines();
        check("LOG.03 L3: SCAN.02's first Edel line is the WARN naming PC_Remote, a remote Player",
              !lines.empty() && lines[0].lvl == 'W' && lines[0].text == FxLine::L3(g_pcRemote), FxLinesStr(lines));
    }
    {
        FxReset();
        FxPool(FxPoolVariant::NoPc0NoRemote);
        FxBreakChain(FxBreak::NoGameInstance);
        Detect();
        const std::vector<FxLogLine> lines = FxEdelLines();
        check("LOG.04 L4: SCAN.03's first Edel line is the WARN naming PC_NoPlayer, WITHOUT a Player",
              !lines.empty() && lines[0].lvl == 'W' && lines[0].text == FxLine::L4(g_pcNoPlayer), FxLinesStr(lines));
    }

    // ── LOG.05: one L5 per completed run, its count after the cut (CLAMP.05) and its resolved flag (PICK.04) ──
    {
        Begin(g_pcParty);
        const struct {
            const char* what;
            int32_t max;
            size_t count;
        } runs[] = {
            { "LOG.05 L5 after a call with 0: exactly one, ending candidates=8", kClampZero,
              static_cast<size_t>(kDefaultMaxCandidates) },
            { "LOG.05 L5 after a call with 65: exactly one, ending candidates=64 -- the count after the cut",
              kAboveCeiling, static_cast<size_t>(kSeeAll) },
            { "LOG.05 L5 under PICK.04: exactly one, reading resolved=0", kDefaultMaxCandidates,
              static_cast<size_t>(kDefaultMaxCandidates) },
        };
        for (const auto& run : runs) {
            const auto& r = Detect(run.max);
            check(run.what,
                  !r.resolved && FxEdelPrefixCount(FxLine::kL5Prefix) == 1 &&
                      FxEdelCount(FxLine::L5(false, g_pcParty, g_heroParty, run.count), 'I') == 1,
                  FxLinesStr(FxEdelLines()));
        }
    }

    // ── LOG.06: silent early returns ──
    {
        FxReset();
        FxPool(FxPoolVariant::IdleOnly);
        FxBreakChain(FxBreak::NoGameInstance);
        const auto& r = Detect();
        check("LOG.06 silent under SCAN.04: no controller found (N1), and no Edel line",
              r.note == FxNote::kN1 && FxEdelLines().empty(), r.note + FxLinesStr(FxEdelLines()));
    }
    {
        FxReset();
        FxBreakChain(FxBreak::NoGameInstance);
        Tot::RequestPerCommand();
        const auto& r = Detect();
        check("LOG.06 silent under SCAN.05: the cancelled scan ends in N1, and no Edel line",
              r.note == FxNote::kN1 && FxEdelLines().empty(), r.note + FxLinesStr(FxEdelLines()));
    }
    {
        FxReset();
        FxNoGWorld();
        FxPool(FxPoolVariant::IdleOnly);
        const auto& r = Detect();
        check("LOG.06 silent under WORLD.01b: no world and no controller (N2), and no Edel line",
              r.note == FxNote::kN2 && FxEdelLines().empty(), r.note + FxLinesStr(FxEdelLines()));
    }
    {
        FxReset();
        FxSetPtr(g_pc0, FxOff::kPawn, 0);
        FxSetPtr(g_pc0, FxOff::kAckPawn, 0);
        const auto& r = Detect();
        check("LOG.06 no L5 under PAWN.03: the pawnless return (N3) leaves L1 alone",
              r.note == FxNote::kN3 && FxEdelLinesAre({ { 'I', FxLine::L1(g_pc0) } }),
              r.note + FxLinesStr(FxEdelLines()));
    }
    {
        Begin(g_dbgNoOrig0);
        const auto& r = Detect();
        check("LOG.06 no L5 under HOP.05: the debug camera with no original (N3) leaves L1 alone",
              r.note == FxNote::kN3 && FxEdelLinesAre({ { 'I', FxLine::L1(g_dbgNoOrig0) } }),
              r.note + FxLinesStr(FxEdelLines()));
    }
    {
        FxReset();
        Tot::RequestPerCommand();
        const auto& r = Detect();
        check("LOG.06 no L5 under CANCEL.01: 'Cancelled.' leaves L1 alone",
              r.note == FxNote::kCancelled && FxEdelPrefixCount(FxLine::kL5Prefix) == 0,
              r.note + FxLinesStr(FxEdelLines()));
    }
    {
        Begin(g_pcX);
        FxCancelTrap(true);
        const auto& r = Detect();
        check("LOG.06 no L5 under CANCEL.02: 'Cancelled.' from inside the PC scan leaves L1 alone",
              r.note == FxNote::kCancelled && FxEdelPrefixCount(FxLine::kL5Prefix) == 0,
              r.note + FxLinesStr(FxEdelLines()));
    }
}

// ══ NOTE ════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockNote() {
    blk("NOTE - every note verbatim, the em dash as U+2014");
    FxReset();

    {
        FxReset();
        FxPool(FxPoolVariant::IdleOnly);
        FxBreakChain(FxBreak::NoGameInstance);
        NoteIs("NOTE.01 N1 under SCAN.04: 'No local PlayerController found (not in gameplay?).'", Detect(),
               FxNote::kN1);
    }
    {
        FxReset();
        FxNoGWorld();
        FxPool(FxPoolVariant::IdleOnly);
        const auto& r = Detect();
        NoteIs("NOTE.02 N2 under WORLD.01b: verbatim", r, FxNote::kN2);
        check("NOTE.02 N2 under WORLD.01b: its dash is U+2014, E2 80 94", r.note.find(kEmDash) != std::string::npos,
              r.note);
    }
    {
        FxReset();
        FxSetPtr(g_pc0, FxOff::kPawn, 0);
        FxSetPtr(g_pc0, FxOff::kAckPawn, 0);
        NoteIs("NOTE.03 N3 under PAWN.03: 'PlayerController has no Pawn (menu / spectator?). No target to detect.'",
               Detect(), FxNote::kN3);
    }

    // ── NOTE.04: N4, typed out byte for byte -- a plain field, a dotted path, a score of 5 ──
    {
        FxReset();
        NoteIs("NOTE.04 N4 under DEF.03: 'Detected target: Enemy_0 (BP_Enemy_C) <U+2014> score 95 via CurrentTarget.'",
               Detect(), DefNote());
    }
    {
        Begin(g_pcC);
        FxPutWeak(g_heroC, FxOff::kLockedOnTarget, kSlotEnemyW, kNullSerial);
        FxPutTArray(g_pcC, FxOff::kSlot0, FxAddr(g_candBuf), kEmptyNum, kCandCount);
        NoteIs("NOTE.04 N4 under CAND.03b: the dotted path LockOn.CurrentTarget is the note's field", Detect(),
               std::string("Detected target: Enemy_N (BP_Enemy_C) ") + kEmDash + " score 95 via LockOn.CurrentTarget.");
    }
    {
        Begin(g_pcZ0);
        FxSetIndex(g_pZ0, kPz0TrackedIndex);
        NoteIs("NOTE.04 N4 under PICK.03b: 'Detected target: P_z0 (BP_Party_C) <U+2014> score 5 via OwnerPawn.'",
               Detect(), std::string("Detected target: P_z0 (BP_Party_C) ") + kEmDash + " score 5 via OwnerPawn.");
    }

    {
        Begin(g_pcParty);
        NoteIs("NOTE.05 N5 under PICK.04: verbatim", Detect(kDefaultMaxCandidates), FxNote::kN5);
    }
    {
        Begin(g_pcE);
        const auto& r = Detect();
        NoteIs("NOTE.06 N6 under PICK.05: verbatim", r, FxNote::kN6);
        check("NOTE.06 N6 under PICK.05: it names 'the Instances tab'",
              r.note.find("the Instances tab") != std::string::npos, r.note);
    }
    {
        FxReset();
        Tot::RequestPerCommand();
        NoteIs("NOTE.07 the cancel note under CANCEL.01: 'Cancelled.'", Detect(), "Cancelled.");
    }
}

// ══ UE4 ═════════════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockUe4() {
    blk("UE4 - the UProperty twin: the chain, the edges and the instance scan read UE4 reflection");
    FxReset();

    // ── UE4.01: GWorld -> WorldU_0, every link a UProperty ──
    {
        FxReset();
        FxUpropMode(true);
        FxWorld(g_worldU0);
        const auto& r = Detect();
        const FxCands want = {
            FxCandOf(g_enemyU1, kScorePawnTarget, g_heroU0, "TargetActor", FxOff::kSlot0,
                     Why("TargetActor", FxWhy::kIsPawn)),
            FxCandOf(g_enemyU3, kScorePawnTarget, g_heroU0, "LockedOnTarget", FxOff::kHeroULocked,
                     Why("LockedOnTarget", FxWhy::kIsPawn)),
            FxCandOf(g_enemyU2, kScorePawnTarget, g_heroU0, "Enemies", kElement, Why("Enemies", FxWhy::kIsPawn)),
        };
        LinesAre("UE4.01 UProperty chain: L1 names PCU_0, then L5 with HeroU_0 and three candidates",
                 { { 'I', FxLine::L1(g_pcU0) }, { 'I', FxLine::L5(false, g_pcU0, g_heroU0, want.size()) } });
        check("UE4.01 UProperty chain: WorldU_0, PCU_0, HeroU_0", ChainIs(r, g_worldU0, g_pcU0, g_heroU0),
              FxResultStr(r));
        CandsAre("UE4.01 UProperty edges: [Enemy_U1 at 0x200, Enemy_U3 at 0x218 (weak), Enemy_U2 at -1 (array)], each "
                 "95 from HeroU_0",
                 r, want);
        check("UE4.01 UProperty edges: three tied tops -- unresolved, N5", !r.resolved && r.note == FxNote::kN5,
              FxResultStr(r));
    }

    // ── UE4.02: the chain cut at World_Bare, the scan over the UProperty pool ──
    {
        FxReset();
        FxUpropMode(true);
        FxWorld(g_worldBare);
        FxPool(FxPoolVariant::Uprop);
        const auto& r = Detect();
        const std::vector<FxLogLine> lines = FxEdelLines();
        check("UE4.02 UProperty scan: the first Edel line is L2 naming PCU_0, its Player read through a UProperty",
              !lines.empty() && lines[0].lvl == 'I' && lines[0].text == FxLine::L2(g_pcU0), FxLinesStr(lines));
        check("UE4.02 UProperty scan: the scan ran once and picked PCU_0, whose pawn is HeroU_0",
              FxScanLines().size() == 1 && r.playerController == g_pcU0 && r.playerPawn == g_heroU0,
              FxResultStr(r) + FxLogDump(0, kScanCat));
    }
    FxReset();
}

// ══ RO / CONC ═══════════════════════════════════════════════════════════════════════════════════════════════════
static void BlockRoConc() {
    blk("RO / CONC - Edel writes nothing it reads, and two callers at once agree with one");
    FxReset();

    // ── RO.01: every planted byte, before and after four runs ──
    {
        const struct {
            const char* id;
            uintptr_t pc;
            int32_t max;
        } runs[] = {
            { "DEF", g_pc0, kDefaultMaxCandidates },
            { "SCORE.00", g_pcS, kSeeAll },
            { "CAND.03", g_pcC, kDefaultMaxCandidates },
            { "COMP.03", g_pc3, kSeeAll },
        };
        for (const auto& run : runs) {
            Begin(run.pc);
            FxClearLog();
            const std::vector<uint8_t> before = FxMemSnapshot();
            const Edel::CurrentTargetResult r = FxDetect(run.max);
            const std::vector<uint8_t> after = FxMemSnapshot();
            check(std::string("RO.01 read-only: ") + run.id + " ran to a list of candidates", !r.candidates.empty(),
                  FxResultStr(r));
            check(std::string("RO.01 read-only: ") + run.id + " left every planted byte as it was", before == after,
                  FirstDiff(before, after));
        }
    }

    // ── CONC.01: a warm-up, then two threads of 25 calls each ──
    {
        FxReset();
        FxClearLog();
        const Edel::CurrentTargetResult warm = FxDetect();
        check("CONC.01 two callers: the warm-up is DEF's result", FxResultEq(warm, DefaultResult()), FxResultStr(warm));
        std::atomic<int> calls{ 0 }, mismatches{ 0 };
        std::mutex firstMu;
        std::string firstBad;
        auto caller = [&]() {
            for (int i = 0; i < kCallsPerThread; ++i) {
                const Edel::CurrentTargetResult r = Edel::DetectCurrentTarget(kDefaultMaxCandidates);
                ++calls;
                if (FxResultEq(r, warm)) continue;
                ++mismatches;
                std::lock_guard<std::mutex> lk(firstMu);
                if (firstBad.empty()) firstBad = FxResultStr(r);
            }
        };
        std::vector<std::thread> threads;
        for (int t = 0; t < kThreads; ++t) threads.emplace_back(caller);
        for (auto& t : threads) t.join();
        const int threaded = kThreads * kCallsPerThread;
        const int runsLogged = 1 + threaded;   // the warm-up, then every threaded call
        check("CONC.01 two callers: every threaded call returned", calls.load() == threaded, FxS(calls.load()));
        check("CONC.01 two callers: every result equals the warm-up's field for field", mismatches.load() == 0,
              FxS(mismatches.load()) + " differ; first: " + firstBad);
        check("CONC.01 two callers: the log holds exactly 51 L5 lines, each DEF's",
              FxEdelPrefixCount(FxLine::kL5Prefix) == runsLogged &&
                  FxEdelCount(FxLine::L5(true, g_pc0, g_hero0, DefaultCands().size()), 'I') == runsLogged,
              FxS(FxEdelPrefixCount(FxLine::kL5Prefix)));
        check("CONC.01 two callers: and as many L1 lines, and no other Edel line",
              FxEdelCount(FxLine::L1(g_pc0), 'I') == runsLogged &&
                  FxEdelLines().size() == static_cast<size_t>(2 * runsLogged),
              FxS(static_cast<long long>(FxEdelLines().size())));
    }
}

// ══ LOG.07: the whole run ═══════════════════════════════════════════════════════════════════════════════════════
// Last of all: the two counters are never cleared, so they cover every block of every group before this one.
static void BlockLogRun() {
    blk("LOG.07 - the level of every Edel line the whole run wrote");
    FxReset();
    check("LOG.07 levels: no Edel line in the whole run was written at level E or D", g_edelErrorOrDebugRun.load() == 0,
          FxS(g_edelErrorOrDebugRun.load()));
    check("LOG.07 levels: the run wrote Edel lines, so the count above is not vacuous", g_edelLinesRun.load() > 0,
          FxS(g_edelLinesRun.load()));
}

}  // namespace G4

static void Blocks_G4() {
    G4::BlockComp();
    G4::BlockCancel();
    G4::BlockSort();
    G4::BlockPick();
    G4::BlockLog();
    G4::BlockNote();
    G4::BlockUe4();
    G4::BlockRoConc();
    G4::BlockLogRun();
}

// ══ H1: every keyword-table entry by its bare name, and counts inside the clamp range ═══════════════════════════
// Its fixtures are built on first use, after FxBuildWorld, with the harness's builders, and never edited afterwards.

namespace H1 {

// ── the keyword tables, one row per entry ──
// Each row spells one entry of Edel's tables as an engine would name such a field, and the field is exactly the entry.
// Only such a field tells an entry apart from a longer word that contains it: make the entry longer and its own row
// stops matching, where a field that merely contains the entry ("<Entry>Ref") would still hold the longer word too.
// Written out here, never read from Edel's tables, so a changed entry shows as a changed score instead of moving its
// field along with it. A row whose entry contains another entry of the same table scores the same without its own
// entry: that row pins the score, and no field can test such an entry on its own.
struct Row {
    const char* field;   // the entry, capitalised
    bool keyword;        // the field name holds a kPositive entry: +50, and the reason opens "field '<name>'"
    bool infra;          // the field name holds a kNegative entry: -40, and "infra-field"
};
// kPositive, entry for entry and in its order.
static const std::vector<Row> kPosRows = {
    { "Target", true, false },         { "Focus", true, false },         { "LockOn", true, false },
    { "LockedOn", true, false },       { "LockedTarget", true, false },  { "Enem", true, false },
    { "AimTarget", true, false },      { "SelectedUnit", true, false },  { "SelectedActor", true, false },
    { "SelectedTarget", true, false }, { "CombatTarget", true, false },  { "Hostile", true, false },
    { "CurrentTarget", true, false },  { "Interactable", true, false },  { "LastHitActor", true, false },
    { "LastDamage", true, false },     { "AttackTarget", true, false },  { "EngagedTarget", true, false },
    { "MarkedTarget", true, false },
};
// kNegative, entry for entry and in its order. ViewTarget holds kPositive's "target" as well.
static const std::vector<Row> kNegRows = {
    { "Owner", false, true },          { "Instigator", false, true },    { "Controller", false, true },
    { "PlayerState", false, true },    { "Camera", false, true },        { "CameraManager", false, true },
    { "Spectator", false, true },      { "ViewTarget", true, true },     { "Input", false, true },
    { "HUD", false, true },            { "LocalPlayer", false, true },   { "DefaultPawn", false, true },
    { "CheatManager", false, true },   { "GameMode", false, true },      { "GameState", false, true },
    { "GameInstance", false, true },   { "World", false, true },         { "Level", false, true },
    { "Mesh", false, true },           { "Capsule", false, true },       { "Movement", false, true },
    { "Anim", false, true },           { "Audio", false, true },         { "Widget", false, true },
};
// A row's target is named for its field, with a prefix of its own so no KWPOS / KWNEG target shares its name.
constexpr const char* kTargetPrefix = "KB_";

// A row's target is a tracked BP_Prop_C read from the controller root: the actor's term and the index's, then the table
// terms its field name earns.
static int32_t ScoreOf(const Row& row) {
    return G4::kTermActor + G4::kTermTracked + (row.keyword ? G4::kTermKeyword : 0) + (row.infra ? G4::kTermInfra : 0);
}
constexpr int32_t kScoreKeywordOnly = G4::kTermKeyword + G4::kTermActor + G4::kTermTracked;   // every kPositive row
// Edel's reason, in its order: the field, the kind, then infra-field.
static std::string WhyOf(const Row& row) {
    using namespace FxWhy;
    if (row.keyword && row.infra) return Join({ Field(row.field), kIsActor, kInfra });
    if (row.keyword) return Join({ Field(row.field), kIsActor });
    return Join({ kIsActor, kInfra });
}
static const char* EffectOf(const Row& row) {
    if (row.keyword && row.infra) return "earns the +50 and costs the -40";
    return row.keyword ? "earns the +50" : "costs the -40";
}

// ── the bare-name scenarios' world ──
// A PlayerController subclass whose own fields are one table's rows, in order from kSlot0, each holding a target of
// its own, and a pawn with nothing of its own to point at.
struct BareWorld {
    uintptr_t pc = 0, pawn = 0;
    std::vector<uintptr_t> targets;   // targets[i]: what rows[i]'s field holds
};

// FxReset puts a world object's header back from the record FxSnapshotHeaders filled at build. An object built later
// starts with an empty record, and the first reset would zero its class, name and index, so it is filled here.
static uintptr_t Keep(uintptr_t obj) {
    const auto it = g_objRec.find(obj);
    if (it == g_objRec.end()) FxFixtureFail("Keep: an object the fixture did not build");
    memcpy(it->second.header.data(), FxBytes(obj), it->second.header.size());
    return obj;
}

// Built once per table, on first use: Aura keeps a class's reference metadata from its first walk, so the class is
// complete before any walk and never edited after one.
static BareWorld BuildBare(const char* clsName, const char* pcName, const char* pawnName,
                           const std::vector<Row>& rows) {
    if (static_cast<size_t>(FxOff::Slot(static_cast<int>(rows.size()))) > kBigBytes)
        FxFixtureFail("a keyword table has more rows than a big object has slots");
    std::vector<std::string> names;
    for (const Row& row : rows) {
        if (!row.keyword && !row.infra) FxFixtureFail("a keyword row belongs to neither table");
        names.push_back(row.field);
    }
    BareWorld w;
    w.pc = Keep(FxObjBig(FxClassF(clsName, g_cPlayerController, FxObjFields(names)), pcName));
    w.pawn = Keep(FxObj(g_cHeroBare, pawnName));
    for (const Row& row : rows)
        w.targets.push_back(Keep(FxObj(g_cProp, (std::string(kTargetPrefix) + row.field).c_str())));
    return w;
}
static const BareWorld& PosWorld() {
    static const BareWorld w = BuildBare("PcKwBarePos", "PC_KBP", "Hero_KBP", kPosRows);
    return w;
}
static const BareWorld& NegWorld() {
    static const BareWorld w = BuildBare("PcKwBareNeg", "PC_KBN", "Hero_KBN", kNegRows);
    return w;
}
// The setup after FxReset, which clears every world object's body: the pair as FxDefPair plants one, then each row's
// field pointed at its target.
static void Plant(const BareWorld& w) {
    FxSetPtr(w.pc, FxOff::kPawn, w.pawn);
    FxSetPtr(w.pc, FxOff::kAckPawn, w.pawn);
    FxSetPtr(w.pawn, FxOff::kPawnController, w.pc);
    for (size_t i = 0; i < w.targets.size(); ++i) FxSetPtr(w.pc, FxOff::Slot(static_cast<int>(i)), w.targets[i]);
}

// One table's run, checked record by record: the chain, the whole list, each row, and that every entry has its row.
// Returns the list as Edel should give it.
static FxCands ExpectRows(const char* block, const char* table, size_t tableEntries, const BareWorld& w,
                          const std::vector<Row>& rows, const Edel::CurrentTargetResult& r) {
    FxCands byRow;
    for (size_t i = 0; i < rows.size(); ++i)
        byRow.push_back(FxCandOf(w.targets[i], ScoreOf(rows[i]), w.pc, rows[i].field,
                                 FxOff::Slot(static_cast<int>(i)), WhyOf(rows[i])));
    // Edel's order: the higher score first, then the smaller offset -- and the rows are in offset order already.
    FxCands want = byRow;
    std::stable_sort(want.begin(), want.end(),
                     [](const Edel::TargetCandidate& a, const Edel::TargetCandidate& b) { return a.score > b.score; });
    const std::string pcName = FxNameText(w.pc), pawnName = FxNameText(w.pawn);

    check(FxFmt("%s bare names: the chain reaches %s and its pawn %s through World_0", block, pcName.c_str(),
                pawnName.c_str()),
          r.world == g_world0 && r.playerController == w.pc && r.playerPawn == w.pawn, FxResultStr(r));
    check(FxFmt("%s bare names: exactly %zu candidates, one per %s entry, each from %s's field named the entry "
                "itself; the higher score first, then the smaller offset", block, want.size(), table, pcName.c_str()),
          FxCandsAre(r.candidates, want), G3::ListGot(r, want));
    for (size_t i = 0; i < rows.size(); ++i) {
        const Edel::TargetCandidate& c = byRow[i];
        check(FxFmt("%s bare names: field '%s', the entry itself, %s -- %s scores %d from 0x%X", block, rows[i].field,
                    EffectOf(rows[i]), c.name.c_str(), c.score, static_cast<unsigned>(c.fieldOffset)),
              G3::Has(r, c), G3::GotWant(r, c));
    }
    // Edel's table is read for its size alone: an entry added there with no row here would go untested.
    check(FxFmt("%s bare names: a row for every %s entry -- %zu rows here, as many as the table holds", block, table,
                rows.size()),
          tableEntries == rows.size(), FxFmt("%s holds %zu", table, tableEntries));
    return want;
}

// ══ KWPOS.02 / KWNEG.02 ═════════════════════════════════════════════════════════════════════════════════════════
static void KwBarePos() {
    const BareWorld& w = PosWorld();
    G4::Begin(w.pc);
    Plant(w);
    const Edel::CurrentTargetResult& r = G4::Detect(G4::kSeeAll);
    const FxCands want = ExpectRows("KWPOS.02", "kPositive", kPositive.size(), w, kPosRows, r);
    check(FxFmt("KWPOS.02 bare names: %zu tops tied at %d resolve nothing -- N5", want.size(), kScoreKeywordOnly),
          !r.resolved && r.note == FxNote::kN5, FxResultStr(r));
    check(FxFmt("KWPOS.02 bare names: L1 names %s, then L5 reads resolved=0 pawn=%s candidates=%zu",
                FxNameText(w.pc).c_str(), FxNameText(w.pawn).c_str(), want.size()),
          G3::ChainLines(w.pc, w.pawn, false, want.size()), G3::EdelGot());
}

static void KwBareNeg() {
    const BareWorld& w = NegWorld();
    G4::Begin(w.pc);
    Plant(w);
    const Edel::CurrentTargetResult& r = G4::Detect(G4::kSeeAll);
    const FxCands want = ExpectRows("KWNEG.02", "kNegative", kNegative.size(), w, kNegRows, r);
    if (want.size() < 2) FxFixtureFail("KWNEG.02 needs a runner-up");
    const Edel::TargetCandidate& top = want[0];
    check(FxFmt("KWNEG.02 bare names: %s at %d clears the runner-up at %d -- resolved, its N4 naming %s (%s) via %s",
                top.name.c_str(), top.score, want[1].score, top.name.c_str(), top.className.c_str(),
                top.fieldName.c_str()),
          r.resolved && r.note == G4::PickNote(top.addr, top.score, top.fieldName), FxResultStr(r));
    check(FxFmt("KWNEG.02 bare names: L1 names %s, then L5 reads resolved=1 pawn=%s candidates=%zu",
                FxNameText(w.pc).c_str(), FxNameText(w.pawn).c_str(), want.size()),
          G3::ChainLines(w.pc, w.pawn, true, want.size()), G3::EdelGot());
}

// ══ CLAMP.07 ════════════════════════════════════════════════════════════════════════════════════════════════════
// A count strictly inside 1..64 comes back as asked, cut from PC_Party's equal pawns. A ceiling test that fired below
// 64 would raise every count above the point where it fires, so the largest count below the ceiling catches it
// wherever it fires; the other two are counts callers pass.
constexpr int32_t kInsideLow = kDefaultMaxCandidates + 1;   // the smallest count above the default
constexpr int32_t kMcpMax = 32;                              // the most the MCP read_current_target tool forwards
constexpr int32_t kBelowCeiling = G4::kSeeAll - 1;           // the largest count below Edel's ceiling
static_assert(kBelowCeiling < kPartySize, "every count asked for is below the party's size, so each list is cut");

static void ClampInside() {
    for (const int32_t n : { kInsideLow, kMcpMax, kBelowCeiling }) {
        G4::Begin(g_pcParty);
        const Edel::CurrentTargetResult& r = G4::Detect(n);
        const size_t want = static_cast<size_t>(n);
        G1::ExpectParty("CLAMP.07 inside the range", FxS(n).c_str(), r, want);
        check(FxFmt("CLAMP.07 inside the range: the %d call's lines are L1 naming PC_Party, then L5 ending "
                    "'candidates=%d'", n, n),
              FxEdelLinesAre({ { 'I', FxLine::L1(g_pcParty) },
                               { 'I', FxLine::L5(false, g_pcParty, g_heroParty, want) } }),
              FxLinesStr(FxEdelLines()));
    }
}

}  // namespace H1

static void Blocks_H1() {
    blk("KW+ / CLAMP+ - every keyword by its bare name, and counts inside the clamp range");
    H1::KwBarePos();
    H1::KwBareNeg();
    H1::ClampInside();
}

// ══ H2: COMP+ / SCORE+ / SORT+ ══════════════════════════════════════════════════════════════════════════════════
// What the groups above do not reach in the scan, the scores and the order: the controller's component pass after a
// debug-camera hop, and the ownership test that keeps a pawn's components out of the controller's pass and budget;
// the infra-field penalty and the drop line on non-actors, near combat included; the element offset of a TSet or TMap
// entry; and the sort's last tie-break. Every scenario starts from FxReset and writes only object fields, a static
// buffer or an InternalIndex, all of which the next reset puts back. The one new world, CAND.08's, is built on first
// use and recorded as a default, so every reset after it replays it.

namespace H2 {

// ── shared ──
// Edel drops a candidate at this score or below: the design's number, written here rather than read from Edel.cpp.
constexpr int32_t kDropLine = -60;
// A tracked non-actor behind an infra field: near combat (the pawn root, a component) and on the controller root.
constexpr int32_t kNearInfraNonActor = G4::kTermNotActor + G4::kTermInfra + G4::kTermNear + G4::kTermTracked;
constexpr int32_t kRootInfraNonActor = G4::kTermNotActor + G4::kTermInfra + G4::kTermTracked;
static_assert(kNearInfraNonActor == -85 && kRootInfraNonActor == -95, "the design's scores");
static_assert(kNearInfraNonActor <= kDropLine && kRootInfraNonActor <= kDropLine,
              "a tracked non-actor behind an infra field falls at or below the drop line on either root");
constexpr int32_t kNextIndex = 1;   // one InternalIndex above another
constexpr int kPcBFirstComp = 2;    // PC_B's Comp01: after OtherComp and Prop

static uintptr_t OuterOf(uintptr_t obj) { return FxGetPtr(obj, DynOff::UOBJECT_OUTER); }

// ══ COMP.08: the controller's component pass after a debug-camera hop ═══════════════════════════════════════════
// With the Debug Camera active the chain hands over the DebugCameraController, and the hop names the controller it
// replaced. That controller's owned components often hold the lock-on state, so its component pass must walk the
// controller the hop landed on -- the debug camera owns none -- and the pawn's pass the pawn read from that controller.
static void CompHop() {
    // COMP.08a: DbgPC_0 hands over PC_K, COMP.01's controller.
    {
        G4::Begin(g_dbgPc0);
        FxSetPtr(g_dbgPc0, FxOff::kOriginalController, g_pcK);
        check("COMP.08a debug camera, then PC_K's components: (fixture) C1's outer is PC_K, and DbgPC_0 holds no pawn",
              OuterOf(g_c1K) == g_pcK && FxGetPtr(g_dbgPc0, FxOff::kPawn) == 0 &&
                  FxGetPtr(g_dbgPc0, FxOff::kAckPawn) == 0,
              FxNameText(OuterOf(g_c1K)));
        const auto& r = G4::Detect();
        const auto t1 = G4::HeldTarget(g_t1K, g_c1K);
        const auto oa = FxCandOf(g_oa, G4::kScoreRootActor, g_pcK, "OwnedActor", FxOff::Slot(1), FxWhy::kIsActor);
        const FxCands want = { t1, oa };
        check("COMP.08a debug camera, then PC_K's components: playerController PC_K, after the hop; playerPawn Hero_K",
              G4::ChainIs(r, g_world0, g_pcK, g_heroK), FxResultStr(r));
        G4::HasCand("COMP.08a debug camera, then PC_K's components: T_1 is read through C1, the component PC_K owns",
                    r, t1);
        G4::CandsAre("COMP.08a debug camera, then PC_K's components: the whole list is COMP.01's [T_1, OA]", r, want);
        G4::NoteIs("COMP.08a debug camera, then PC_K's components: the pick note names T_1, score 80, via TargetActor",
                   r, G4::PickNote(g_t1K, G4::kScoreHeldTarget, G4::kTargetField));
        G4::LinesAre("COMP.08a debug camera, then PC_K's components: L1 names DbgPC_0, then L5 names PC_K and Hero_K",
                     { { 'I', FxLine::L1(g_dbgPc0) }, { 'I', FxLine::L5(true, g_pcK, g_heroK, want.size()) } });
    }

    // COMP.08b: DbgPC_0 hands over PC_B, whose pawn Hero_B owns PC1: both passes must follow the hop.
    {
        G4::Begin(g_dbgPc0);
        FxSetPtr(g_dbgPc0, FxOff::kOriginalController, g_pcB);
        const auto& r = G4::Detect(G4::kSeeAll);
        FxCands held;
        for (int i = 0; i < kComponentBudget; ++i) held.push_back(G4::HeldTarget(g_t17[i], g_c17[i]));
        const auto tp1 = G4::HeldTarget(g_tp1, g_pc1);
        const auto ax = FxCandOf(g_aX, G4::kScoreRootActor, g_pcB, "Prop", FxOff::Slot(1), FxWhy::kIsActor);
        const auto pc1 = G4::NearNonActor(g_pc1, g_heroB, "PawnComp", FxOff::kSlot0);
        check("COMP.08b debug camera, then both passes: playerController PC_B, after the hop; playerPawn Hero_B",
              G4::ChainIs(r, g_world0, g_pcB, g_heroB), FxResultStr(r));
        const std::string missing = G4::Missing(r, held);
        check("COMP.08b debug camera, then both passes: T01..T16 are each read through C01..C16, which PC_B owns",
              missing.empty(), missing);
        G4::HasCand("COMP.08b debug camera, then both passes: Tp1 is read through PC1, the component of Hero_B -- the "
                    "pawn PC_B holds, where the debug camera holds none",
                    r, tp1);
        FxCands all = held;
        all.push_back(tp1);
        all.push_back(ax);
        all.push_back(pc1);
        G4::CandsAre("COMP.08b debug camera, then both passes: the whole list is COMP.02a's -- T01..T16, Tp1, A_x, PC1",
                     r, all);
        G4::LinesAre("COMP.08b debug camera, then both passes: L1 names DbgPC_0, then L5 names PC_B and Hero_B",
                     { { 'I', FxLine::L1(g_dbgPc0) }, { 'I', FxLine::L5(false, g_pcB, g_heroB, all.size()) } });
    }
}

// ══ COMP.09: the controller's pass takes the components the controller owns, and no others ══════════════════════
// A component the pawn owns belongs to the pawn's pass and the pawn's budget. A controller that caches references to
// its pawn's components must neither scan them itself nor spend its own 16 slots on them, or the controller's own
// targeting component can go unscanned.
static void CompOwner() {
    // COMP.09a: PC_Q caches C_q, which Hero_Q owns, and Hero_Q no longer points at it.
    {
        G4::Begin(g_pcQ);
        FxSetPtr(g_heroQ, FxOff::kSlot0, 0);   // Hero_Q.LockComp
        check("COMP.09a pawn-owned, controller-cached: (fixture) C_q's outer is Hero_Q, which has no outer of its own",
              OuterOf(g_cQ) == g_heroQ && OuterOf(g_heroQ) == 0, FxNameText(OuterOf(g_cQ)));
        const auto& r = G4::Detect();
        G4::Lacks("COMP.09a pawn-owned, controller-cached: T_q is absent -- the controller's pass turns C_q away, and "
                  "the pawn's pass never meets it",
                  r, g_tQ);
        check("COMP.09a pawn-owned, controller-cached: no candidate at all (C_q drops at -60 as a controller field) -- "
              "unresolved, N6",
              r.candidates.empty() && !r.resolved && r.note == FxNote::kN6, FxResultStr(r));
        G4::LinesAre("COMP.09a pawn-owned, controller-cached: L1 names PC_Q, then L5 ends candidates=0",
                     { { 'I', FxLine::L1(g_pcQ) }, { 'I', FxLine::L5(false, g_pcQ, g_heroQ, 0) } });
    }

    // COMP.09b: PC_B's pawn becomes Hero_B2, and PC_B's Comp01..Comp16 point at Hero_B2's PB2_01..PB2_16. PC_B's own
    // C17 comes after them, so it is the first component PC_B owns -- and needs the controller's budget untouched.
    {
        G4::Begin(g_pcB);
        FxSetPtr(g_pcB, FxOff::kPawn, g_heroB2);
        FxSetPtr(g_pcB, FxOff::kAckPawn, g_heroB2);
        FxSetPtr(g_heroB2, FxOff::kPawnController, g_pcB);   // back at its controller, so no stray actor is scored
        for (int i = 0; i < kComponentBudget; ++i) FxSetPtr(g_pcB, FxOff::Slot(kPcBFirstComp + i), g_pb2[i]);
        const uintptr_t c17 = g_c17[kComp17 - 1], t17 = g_t17[kComp17 - 1];
        const uintptr_t pb16 = g_pb2[kComponentBudget - 1];
        check("COMP.09b controller budget: (fixture) PB2_01's outer is Hero_B2, which has no outer; C17's is PC_B; T17 "
              "lies below Tp16b",
              OuterOf(g_pb2[0]) == g_heroB2 && OuterOf(g_heroB2) == 0 && OuterOf(c17) == g_pcB && t17 < g_tp16b,
              FxNameText(OuterOf(g_pb2[0])) + " / " + FxNameText(OuterOf(c17)));
        const auto& r = G4::Detect(G4::kSeeAll);
        const auto t17Rec = G4::HeldTarget(t17, c17);
        const auto tp16b = G4::HeldTarget(g_tp16b, pb16);
        G4::HasCand("COMP.09b controller budget: T17 is read through C17 -- PB2_01..PB2_16, owned by the pawn, spend "
                    "none of the controller's 16 slots",
                    r, t17Rec);
        G4::HasCand("COMP.09b controller budget: Tp16b is read through PB2_16 in the pawn's own pass", r, tp16b);
        FxCands all = { t17Rec, tp16b,
                        FxCandOf(g_aX, G4::kScoreRootActor, g_pcB, "Prop", FxOff::Slot(1), FxWhy::kIsActor) };
        for (int i = 0; i < kComponentBudget; ++i)
            all.push_back(G4::NearNonActor(g_pb2[i], g_heroB2, FxFmt("PawnComp%02d", i + 1), FxOff::Slot(i)));
        G4::CandsAre("COMP.09b controller budget: the whole list is [T17, Tp16b (tied at 80, by address), A_x, then "
                     "PB2_01..PB2_16 by offset]",
                     r, all);
        G4::LinesAre("COMP.09b controller budget: L1 names PC_B, then L5 names Hero_B2",
                     { { 'I', FxLine::L1(g_pcB) }, { 'I', FxLine::L5(false, g_pcB, g_heroB2, all.size()) } });
    }
}

// ══ SCORE.10 / SCORE.11: the infra penalty on non-actors, and the drop line near combat ═════════════════════════
// Every Character exposes CharacterMovement and Mesh, and every PlayerController its PlayerCameraManager: non-actors
// behind infra field names. The -40 must still apply to them, and -60 or less must drop them on the pawn root as on
// the controller's, or every detection lists the player's own components -- and a world with no target reads N5,
// "best guesses", instead of N6.
static void ScoreInfra() {
    FxReset();
    check("SCORE.10 infra field on a non-actor: (fixture) Cmc_0 and Sense_0 are tracked components owned by Enemy_0, "
          "and Data_0 is a tracked non-actor",
          G2::Kind(g_cmc0) == "ActorComponent" && G2::Kind(g_sense0) == "ActorComponent" && G2::Kind(g_data0).empty() &&
              OuterOf(g_cmc0) == g_enemy0 && OuterOf(g_sense0) == g_enemy0 && FxIndexOf(g_cmc0) >= 0 &&
              FxIndexOf(g_sense0) >= 0 && FxIndexOf(g_data0) >= 0,
          G2::KindStr(g_cmc0) + " " + G2::KindStr(g_sense0) + " " + G2::KindStr(g_data0));

    // SCORE.10a / SCORE.11a: the default world, with the engine's infra fields planted.
    {
        FxReset();
        FxSetPtr(g_hero0, FxOff::kCharacterMovement, g_cmc0);
        FxSetPtr(g_hero0, FxOff::kMesh, g_sense0);
        FxSetPtr(g_pc0, FxOff::kCameraManager, g_data0);
        const auto& r = G4::Detect();
        check("SCORE.10a infra field on a non-actor: with Hero_0's CharacterMovement and Mesh and PC_0's "
              "PlayerCameraManager planted, the default world resolves exactly as DEF.01-DEF.03",
              FxResultEq(r, G4::DefaultResult()), FxResultStr(r));
        G4::LinesAre("SCORE.10a infra field on a non-actor: DEF.04's lines -- L5 still ends candidates=2",
                     G4::DefaultLines());
        G4::Lacks(FxFmt("SCORE.10a infra field on a non-actor: Data_0 behind PC_0's PlayerCameraManager is absent -- "
                        "%d, dropped",
                        kRootInfraNonActor),
                  r, g_data0);
        check(FxFmt("SCORE.11a drop line near combat: Cmc_0 and Sense_0 behind Hero_0's CharacterMovement and Mesh are "
                    "absent -- %d is dropped on the pawn root too",
                    kNearInfraNonActor),
              G3::Absent(r, g_cmc0) && G3::Absent(r, g_sense0), FxCandsStr(r.candidates));
    }

    // SCORE.10b / SCORE.11b: PICK.05's world, nothing target-like, with the same infra fields planted.
    {
        G4::Begin(g_pcE);
        FxSetPtr(g_heroE, FxOff::kCharacterMovement, g_cmc0);
        FxSetPtr(g_heroE, FxOff::kMesh, g_sense0);
        FxSetPtr(g_pcE, FxOff::kCameraManager, g_data0);
        const auto& r = G4::Detect();
        G4::Lacks("SCORE.10b infra field on a non-actor: Data_0 behind PC_E's PlayerCameraManager is absent", r,
                  g_data0);
        check("SCORE.11b drop line near combat: Cmc_0 and Sense_0 behind Hero_E's CharacterMovement and Mesh are "
              "absent",
              G3::Absent(r, g_cmc0) && G3::Absent(r, g_sense0), FxCandsStr(r.candidates));
        check("SCORE.11b drop line near combat: the player's own components leave the list empty -- unresolved, N6, "
              "not N5's best guesses",
              r.candidates.empty() && !r.resolved && r.note == FxNote::kN6, FxResultStr(r));
        G4::LinesAre("SCORE.11b drop line near combat: L1 names PC_E, then L5 ends candidates=0",
                     { { 'I', FxLine::L1(g_pcE) }, { 'I', FxLine::L5(false, g_pcE, g_heroE, 0) } });
    }
}

// ══ CAND.08 / SORT.05: a TSet element and a TMap value carry fieldOffset -1 ═════════════════════════════════════
// Every container element lives in a heap buffer, so its record carries -1 -- the documented "container element"
// offset -- whatever the container: a TSet's or a TMap's header offset is no address of the actor, and a consumer that
// added it to the source would dereference the sparse array's Data pointer. The same -1 keys the element after every
// direct field on a score tie.
constexpr const char* kSetProperty = "SetProperty";
constexpr const char* kMapProperty = "MapProperty";
// UE's sparse TSet / TMap: a TSparseArray -- its element TArray, then the allocation TBitArray -- and the hash. Of the
// header Aura reads the TArray, the first inline allocation word and NumBits; nothing else is planted.
constexpr int32_t kSparseSetBytes = 0x50;      // the sparse layout: a 16-byte set is the compact one, never decoded
static_assert(kSparseSetBytes == DynOff::SPARSE_SET_ELEMENT_SIZE, "the size the walker reads as a sparse set");
constexpr int kSparseAllocWordAt = 0x10;       // TBitArray's first inline word: bit i set means slot i is allocated
constexpr int kSparseNumBitsAt = 0x28;         // TBitArray::NumBits: a slot at or past it reads as free
constexpr int32_t kOneSlot = 1;                // one allocated slot, index 0
constexpr int32_t kSlot0Allocated = 1;         // the allocation word with bit 0 set
constexpr int32_t kSetStride = 0x10;           // TSetElement<AActor*>: the pointer, then HashNextId and HashIndex
constexpr int32_t kMapStride = 0x18;           // TSetElement<TPair<int32, AActor*>>: key, pad, value, hash ids
constexpr int32_t kMapValueAt = 0x08;          // the value in a pair: an int32 key padded to the pointer's alignment
constexpr uintptr_t kMapKey = 7;               // the pair's key: any value, since only the value is an object
constexpr size_t kSetWords = static_cast<size_t>(kSetStride) / sizeof(uintptr_t);
constexpr size_t kMapWords = static_cast<size_t>(kMapStride) / sizeof(uintptr_t);
static_assert(kMapValueAt == kPtrBytes, "the value is the map buffer's second word");
// PcSetMap's fields: TargetSet, TargetMap, then TargetAfter -- a direct field at a higher offset than both.
constexpr int32_t kSetAt = FxOff::kSlot0;
constexpr int32_t kMapAt = kSetAt + kSparseSetBytes;
constexpr int32_t kAfterAt = kMapAt + kSparseSetBytes;
static_assert(static_cast<size_t>(kAfterAt + kPtrBytes) <= kBigBytes, "PcSetMap's last field lies inside a big object");
constexpr int32_t kSetElement = 0, kMapElement = 0;   // the sparse index of the one entry each container holds

static uintptr_t g_setBuf[kSetWords];   // PC_SM.TargetSet's Data: InSet_0
static uintptr_t g_mapBuf[kMapWords];   // PC_SM.TargetMap's Data: { kMapKey, InMap_0 }
static uintptr_t g_pcSm = 0, g_heroSm = 0, g_inSet = 0, g_inMap = 0, g_after = 0;

// A sparse container at `at` in obj holding one allocated slot, in `buf`, recorded as a default.
static void DefSparseOne(uintptr_t obj, int32_t at, uintptr_t* buf) {
    FxDefTArray(obj, at, FxAddr(buf), kOneSlot, kOneSlot);
    FxDefI32(obj, at + kSparseAllocWordAt, kSlot0Allocated);
    FxDefI32(obj, at + kSparseNumBitsAt, kOneSlot);
}

// PC_SM and its pawn, built once. The reset first puts every existing header and class head back as built, so the
// snapshots taken at the end record the defaults, and the new objects', for every reset after this one.
static void BuildSetMapWorld() {
    if (g_pcSm) return;
    FxReset();
    // UE names a TSet's element property and a TMap's value property after the container, its key "<name>_Key".
    const uintptr_t setField = FxField(kSetProperty, "TargetSet", kSetAt, kSparseSetBytes);
    FxPut64(setField, DynOff::FARRAYPROP_INNER, FxObjF("TargetSet", 0));
    const uintptr_t mapField = FxField(kMapProperty, "TargetMap", kMapAt, kSparseSetBytes);
    FxPut64(mapField, DynOff::FSTRUCTPROP_STRUCT, FxIntF("TargetMap_Key", 0));
    FxPut64(mapField, DynOff::FSTRUCTPROP_STRUCT + kPtrBytes, FxObjF("TargetMap", 0));
    const uintptr_t cls = FxClassF("PcSetMap", g_cPlayerController,
                                   FxChainFields({ setField, mapField, FxObjF("TargetAfter", kAfterAt) }));
    g_pcSm = FxObjBig(cls, "PC_SM");
    g_heroSm = FxObj(g_cHeroBare, "Hero_SM");
    g_inSet = FxObj(g_cProp, "InSet_0");   // below InMap_0 by address
    g_inMap = FxObj(g_cProp, "InMap_0");
    g_after = FxObj(g_cProp, "After_0");
    FxDefPair(g_pcSm, g_heroSm);
    FxDefBuf(g_setBuf, kSetWords, { g_inSet });
    FxDefBuf(g_mapBuf, kMapWords, { kMapKey, g_inMap });
    DefSparseOne(g_pcSm, kSetAt, g_setBuf);
    DefSparseOne(g_pcSm, kMapAt, g_mapBuf);
    FxDefPtr(g_pcSm, kAfterAt, g_after);
    FxSnapshotHeaders();
    FxSnapshotClasses();
}

static std::string EdgeOr(const Aura::OutgoingPtr* e) {
    return e ? G1::EdgeStr(*e) + " type='" + e->fieldType + "'" : std::string("absent");
}

static void SetMapElements() {
    BuildSetMapWorld();
    G4::Begin(g_pcSm);
    {
        std::vector<Aura::OutgoingPtr> edges;
        Aura::CollectOutgoingObjectPtrs(g_pcSm, edges);
        const Aura::OutgoingPtr* es = G1::FindEdge(edges, g_inSet);
        const Aura::OutgoingPtr* em = G1::FindEdge(edges, g_inMap);
        check("CAND.08 set and map elements: (fixture) Aura emits InSet_0 as element 0 of the SetProperty 'TargetSet', "
              "at its header 0x200",
              es && G1::EdgeIs(*es, g_inSet, kSetAt, "TargetSet", kSetElement) && es->fieldType == kSetProperty,
              EdgeOr(es));
        check("CAND.08 set and map elements: (fixture) Aura emits InMap_0 as the value of element 0 of the MapProperty "
              "'TargetMap', at its header 0x250",
              em && G1::EdgeIs(*em, g_inMap, kMapAt, "TargetMap.Value", kMapElement) && em->fieldType == kMapProperty &&
                  em->elemStride == kMapStride && em->elemValueOffset == kMapValueAt,
              EdgeOr(em));
        check("CAND.08 set and map elements: (fixture) InSet_0 lies below InMap_0 by address", g_inSet < g_inMap,
              FxHex(g_inSet) + " " + FxHex(g_inMap));
    }
    const auto& r = G4::Detect(G4::kSeeAll);
    const auto after = FxCandOf(g_after, G4::kScoreRootKwActor, g_pcSm, "TargetAfter", kAfterAt,
                                G4::Why("TargetAfter", FxWhy::kIsActor));
    const auto inSet = FxCandOf(g_inSet, G4::kScoreRootKwActor, g_pcSm, "TargetSet", G4::kElement,
                                G4::Why("TargetSet", FxWhy::kIsActor));
    const auto inMap = FxCandOf(g_inMap, G4::kScoreRootKwActor, g_pcSm, "TargetMap.Value", G4::kElement,
                                G4::Why("TargetMap.Value", FxWhy::kIsActor));
    const FxCands want = { after, inSet, inMap };
    const Edel::TargetCandidate* cs = FxFindCand(r, g_inSet);
    const Edel::TargetCandidate* cm = FxFindCand(r, g_inMap);
    check("CAND.08 set and map elements: InSet_0 (a TSet element) and InMap_0 (a TMap value) both carry fieldOffset "
          "-1, never their headers' 0x200 / 0x250",
          cs && cs->fieldOffset == G4::kElement && cm && cm->fieldOffset == G4::kElement,
          G3::Got(cs) + "\n      " + G3::Got(cm));
    G4::CandsAre("CAND.08 set and map elements: the whole list is [After_0 0x2A0, InSet_0 'TargetSet' -1, InMap_0 "
                 "'TargetMap.Value' -1], each 70",
                 r, want);
    const int pAfter = G4::Position(r, g_after), pSet = G4::Position(r, g_inSet), pMap = G4::Position(r, g_inMap);
    check("SORT.05 field before element: tied at 70, the direct field at 0x2A0 sorts ahead of the set element and the "
          "map value, which key as -1 and then by address",
          pAfter >= 0 && pSet >= 0 && pMap >= 0 && pAfter < pSet && pSet < pMap, FxAddrsStr(FxCandAddrs(r)));
    G4::NoteIs("CAND.08 set and map elements: three tops tied at 70 resolve nothing -- N5", r, FxNote::kN5);
    G4::LinesAre("CAND.08 set and map elements: L1 names PC_SM, then L5 names Hero_SM, candidates=3",
                 { { 'I', FxLine::L1(g_pcSm) }, { 'I', FxLine::L5(false, g_pcSm, g_heroSm, want.size()) } });
}

// ══ SORT.06: the address breaks the last tie, never the InternalIndex or the name ═══════════════════════════════
// In a live game an object's address, its GUObjectArray index and its name are unrelated, and the cut to maxCandidates
// keeps the first entries of the sorted list: the documented order is the address. Every tie elsewhere in this file
// has address, index and name all in one order, so these pairs set the index -- and, in SORT.06b, the name too -- in
// the order opposite to the addresses.
static void SortTieBreak() {
    // SORT.06a: SORT.03's world, with A3 (below A4 by address) re-indexed above A4.
    {
        G4::Begin(g_pcO);
        FxSetIndex(g_a3, FxIndexOf(g_a4) + kNextIndex);
        check("SORT.06a address, not index: (fixture) A3 lies below A4 by address and now above it by InternalIndex",
              g_a3 < g_a4 && FxIndexOf(g_a3) > FxIndexOf(g_a4),
              FxFmt("A3 index %d, A4 index %d", FxIndexOf(g_a3), FxIndexOf(g_a4)));
        const auto& r = G4::Detect(G4::kSeeAll);
        const FxCands want = {
            FxCandOf(g_a1, G4::kScoreRootKwActor, g_pcO, "TargetA1", FxOff::Slot(0),
                     G4::Why("TargetA1", FxWhy::kIsActor)),
            FxCandOf(g_a2, G4::kScoreRootKwActor, g_pcO, "TargetA2", FxOff::Slot(1),
                     G4::Why("TargetA2", FxWhy::kIsActor)),
            FxCandOf(g_a3, G4::kScoreRootKwActor, g_pcO, "Targets", G4::kElement, G4::Why("Targets", FxWhy::kIsActor)),
            FxCandOf(g_a4, G4::kScoreRootKwActor, g_pcO, "Targets", G4::kElement, G4::Why("Targets", FxWhy::kIsActor)),
        };
        G4::CandsAre("SORT.06a address, not index: SORT.02's list [A1, A2, A3, A4], A3 still before A4 and carrying "
                     "its new index",
                     r, want);
    }

    // SORT.06b: A1 and A2 only as Targets elements, emitted A1 first. A2 lies below A1 by address, while its name and
    // -- re-indexed -- its InternalIndex both sort after A1's.
    {
        G4::Begin(g_pcO);
        FxSetPtr(g_pcO, FxOff::Slot(0), 0);   // TargetA1
        FxSetPtr(g_pcO, FxOff::Slot(1), 0);   // TargetA2
        g_sortBuf[0] = g_a1;
        g_sortBuf[1] = g_a2;
        FxSetIndex(g_a2, FxIndexOf(g_a1) + kNextIndex);
        check("SORT.06b address, not name or index: (fixture) A2 lies below A1 by address, and its name and its "
              "InternalIndex both sort after A1's",
              g_a2 < g_a1 && FxNameText(g_a1) < FxNameText(g_a2) && FxIndexOf(g_a2) > FxIndexOf(g_a1),
              FxFmt("A1 index %d, A2 index %d", FxIndexOf(g_a1), FxIndexOf(g_a2)));
        const auto& r = G4::Detect(G4::kSeeAll);
        const FxCands want = {
            FxCandOf(g_a2, G4::kScoreRootKwActor, g_pcO, "Targets", G4::kElement, G4::Why("Targets", FxWhy::kIsActor)),
            FxCandOf(g_a1, G4::kScoreRootKwActor, g_pcO, "Targets", G4::kElement, G4::Why("Targets", FxWhy::kIsActor)),
        };
        G4::CandsAre("SORT.06b address, not name or index: exactly [A2, A1], both 'Targets' elements at 70", r, want);
        G4::NoteIs("SORT.06b address, not name or index: two tops tied at 70 resolve nothing -- N5", r, FxNote::kN5);
        G4::LinesAre("SORT.06b address, not name or index: L1 names PC_O, then L5 ends candidates=2",
                     { { 'I', FxLine::L1(g_pcO) }, { 'I', FxLine::L5(false, g_pcO, g_heroO, want.size()) } });
    }
}

}  // namespace H2

static void Blocks_H2() {
    blk("COMP+ / SCORE+ / SORT+ - what the survivor hunt found in the scan, the scores and the order");
    H2::CompHop();
    H2::CompOwner();
    H2::ScoreInfra();
    H2::SetMapElements();
    H2::SortTieBreak();
}

int main() {
    setvbuf(stdout, nullptr, _IONBF, 0);
    printf("edel_target_test -- Current Target (Edel.cpp) over a fake world, in this process\n");
    FxAsanPreamble();
    FxBuildWorld();
    Blocks_G1();
    Blocks_G2();
    Blocks_G3();
    Blocks_G4();
    Blocks_H1();
    Blocks_H2();
    FxCleanup();
    return FxExitCode();
}
