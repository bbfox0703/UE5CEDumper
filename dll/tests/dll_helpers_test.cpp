// ============================================================
// dll_helpers_test
//
// Stand-alone executable (no GoogleTest / Catch2 dependency) covering pure
// helpers in Renge.h (TryStrToAddr) and Scharf.h (IsAlignmentSuspicious).
// Same EXPECT-style harness as utf8_helpers_test.cpp; exit code = failure count.
//
// Why a separate exe? Both helpers used to be inlined into hot-path code
// (Fern.cpp pipe handler and Ubel.cpp WalkInstance) where they couldn't be
// exercised without a real game process. Extracting them to small headers
// makes regressions catchable at build time.
//
// Real-world cases driving these tests come from cross-game logs:
//   - Renge: Squirrel With A Gun sent {"addr":"0x[ply_base]"} (unsubstituted
//     CE placeholder), throwing std::invalid_argument and crashing the pipe
//     command. TryStrToAddr now returns false on any non-hex input.
//   - Scharf: Meltopia (UE 5.0.5) emitted ~75 "Misaligned field"
//     warnings per session for legitimate uint8 EnumProperty / FName layouts.
//     RequiredAlignment now consults ElemSize and CasePreservingName mode.
// ============================================================

#include "../src/Himmel.h"  // AobResolve + IsCeReplayableAob + the shipped pattern tables
#include "../src/Renge.h"
#include "../src/Scharf.h"
#include "../src/Radar.h"
#include "../src/Macht.h"   // ComputeSetElementStride / ComputeMapValueOffset (V1a geometry)
#include "../src/Denken.h"
#include "../src/Lineal.h"  // UE5.7+ packed FUObjectItem reconstruction (Reconstruct/Encode)
#include "../src/Neu.h"     // UEnum::Names layout parse (legacy TArray vs UE5.6+ FNameData)
#include "../src/GraphPath.h"   // Pure BFS shortest-path core ("Locate in GWorld")
#include "../src/VersionNeedleScan.h"  // Gated UE-version needle sweep (audit #5 G2)
#include "../src/Solitar.h"     // GodMode FBoolProperty bit write (ApplyBoolBit, header-inline)
#include "../src/Grimoire.h"    // DynOff FFieldClass::Name probe (UE5.8 virtual-dtor shift)
#include "../src/Solide.h"      // Force-field / stealth-meter matcher (MatchStealthField, header-inline)
#include "../src/Orden.h"       // Multi-value group scan: source-agnostic SDR matcher (MatchGroup)
#include "../src/Ubel.h"        // Native-C scan P0: ComputeHoles / ComputeClassHoles / NormalizeGuessedTypeToProperty (inline, pure)
#include "../src/Serie.h"       // FNamePool index geometry: ReadEnumRawValue is in Ubel; BlockBits/UE4 bounds are here (audit #5 G4/G5)
#include "../src/Aura.h"        // IsEnginePackage (header-inline, pure) — engine/game package gate
#include "../src/Tot.h"         // Cancellation flags: cancel-immunity vs background-worker (B4)
#include "../src/Routine.h"    // SafeThread — detaching-on-destroy thread wrapper
#include "../src/Mimic.h"      // CE Lua <-> DLL mailbox LAYOUT (pure data; Mimic.cpp is not compiled here)
#include "../src/Stark.h"      // ShouldUseTrampoline / ShouldDrainQueue (header-inline, pure)
#include "../src/Genau.h"      // AdmitMultiModuleCandidate (constexpr, pure) — Pass-2 scan admission
#include "../src/Voll.h"       // Pipe-accept capacity logging policy (OnCreateFailure/Success, [PIPEBUSY])
#include "../src/Flamme.h"     // ShouldPublishAtomicWrite (constexpr, pure) — hint-cache publish gate

#include <Windows.h>

#include <thread>

#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <memory>   // getenv for DLL_TEST_TRACE
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

static int g_pass = 0;
static int g_fail = 0;

#define EXPECT(label, cond) do { \
    if (cond) { ++g_pass; } \
    else { ++g_fail; std::printf("  FAIL: %s\n    at %s:%d\n", label, __FILE__, __LINE__); } \
} while (0)

#define EXPECT_EQ_U64(label, actual, expected) do { \
    uint64_t _a = static_cast<uint64_t>(actual); \
    uint64_t _e = static_cast<uint64_t>(expected); \
    if (_a == _e) { ++g_pass; } \
    else { \
        ++g_fail; \
        std::printf("  FAIL: %s\n    actual=0x%llX expected=0x%llX\n    at %s:%d\n", \
            label, (unsigned long long)_a, (unsigned long long)_e, __FILE__, __LINE__); \
    } \
} while (0)

#define EXPECT_EQ_STR(label, actual, expected) do { \
    const std::string _a = (actual); \
    const std::string _e = (expected); \
    if (_a == _e) { ++g_pass; } \
    else { \
        ++g_fail; \
        std::printf("  FAIL: %s\n    actual=\"%s\" expected=\"%s\"\n    at %s:%d\n", \
            label, _a.c_str(), _e.c_str(), __FILE__, __LINE__); \
    } \
} while (0)

// ----- TryStrToAddr ----------------------------------------------------------

static void Test_TryStrToAddr_AcceptsValidHex() {
    uintptr_t v = 0;
    EXPECT("0x prefix",       Renge::TryStrToAddr("0x1F809E08FB0", v));
    EXPECT_EQ_U64("0x1F809E08FB0", v, 0x1F809E08FB0ULL);

    v = 0;
    EXPECT("0X prefix uppercase", Renge::TryStrToAddr("0X1f809e08fb0", v));
    EXPECT_EQ_U64("0X1f809e08fb0", v, 0x1F809E08FB0ULL);

    v = 0;
    EXPECT("no prefix",       Renge::TryStrToAddr("1A2B3C", v));
    EXPECT_EQ_U64("1A2B3C", v, 0x1A2B3CULL);

    v = 0;
    EXPECT("zero",            Renge::TryStrToAddr("0x0", v));
    EXPECT_EQ_U64("zero=0", v, 0ULL);

    v = 0;
    EXPECT("max 64-bit",      Renge::TryStrToAddr("0xFFFFFFFFFFFFFFFF", v));
    EXPECT_EQ_U64("max 64-bit", v, 0xFFFFFFFFFFFFFFFFULL);

    v = 0;
    EXPECT("trailing whitespace tolerated", Renge::TryStrToAddr("0x1234 ", v));
    EXPECT_EQ_U64("trailing space", v, 0x1234ULL);
}

static void Test_TryStrToAddr_RejectsCePlaceholder() {
    // The Squirrel With A Gun crash: UI sent unsubstituted "0x[ply_base]"
    uintptr_t v = 0xDEADBEEF;
    EXPECT("rejects 0x[ply_base]", !Renge::TryStrToAddr("0x[ply_base]", v));
    EXPECT("outAddr untouched on failure", v == 0xDEADBEEF);
}

static void Test_TryStrToAddr_RejectsTrailingGarbage() {
    uintptr_t v = 0;
    EXPECT("rejects 0x123junk",   !Renge::TryStrToAddr("0x123junk", v));
    EXPECT("rejects 0xABC]",      !Renge::TryStrToAddr("0xABC]", v));
    EXPECT("rejects 0x12 0x34",   !Renge::TryStrToAddr("0x12 0x34", v));
}

static void Test_TryStrToAddr_RejectsEmpty() {
    uintptr_t v = 0;
    EXPECT("rejects empty",       !Renge::TryStrToAddr("", v));
    EXPECT("rejects whitespace",  !Renge::TryStrToAddr("   ", v));
    EXPECT("rejects 0x alone",    !Renge::TryStrToAddr("0x", v));
}

static void Test_TryStrToAddr_RejectsNonHex() {
    uintptr_t v = 0;
    EXPECT("rejects ply_base",    !Renge::TryStrToAddr("ply_base", v));
    EXPECT("rejects -1",          !Renge::TryStrToAddr("-1", v));
    EXPECT("rejects negative hex",!Renge::TryStrToAddr("-0x1", v));
    EXPECT("rejects null literal",!Renge::TryStrToAddr("null", v));
}

static void Test_StrToAddr_NoexceptZeroOnFailure() {
    // Legacy convenience wrapper must not throw on any input.
    EXPECT_EQ_U64("malformed → 0",         Renge::StrToAddr("0x[ply_base]"), 0ULL);
    EXPECT_EQ_U64("empty → 0",             Renge::StrToAddr(""), 0ULL);
    EXPECT_EQ_U64("ply_base → 0",          Renge::StrToAddr("ply_base"), 0ULL);
    EXPECT_EQ_U64("valid still parses",    Renge::StrToAddr("0xCAFE"), 0xCAFEULL);
}

// ----- Scharf::IsAlignmentSuspicious --------------------------------

static void Test_Alignment_PointerProperties_Need8() {
    // Pointer-shaped fields at 8-aligned offsets — never suspicious.
    EXPECT("ObjectProperty @ 0x10 OK",      !Scharf::IsAlignmentSuspicious("ObjectProperty", 0x10, 8, false));
    EXPECT("ClassProperty @ 0x40 OK",       !Scharf::IsAlignmentSuspicious("ClassProperty",  0x40, 8, false));
    EXPECT("InterfaceProperty @ 0x18 OK",   !Scharf::IsAlignmentSuspicious("InterfaceProperty", 0x18, 16, false));

    // Misaligned pointer — real concern.
    EXPECT("ObjectProperty @ 0x4 BAD",       Scharf::IsAlignmentSuspicious("ObjectProperty", 0x4, 8, false));
    EXPECT("ArrayProperty @ 0x14 BAD",       Scharf::IsAlignmentSuspicious("ArrayProperty",  0x14, 16, false));
}

static void Test_Alignment_EnumProperty_RespectsElemSize() {
    // Real-world Meltopia / CaravanSandWitch case:
    //   "DefaultUpdateOverlapsMethodDuringLevelStreaming" (EnumProperty) at offset 0x5F
    //   ElemSize = 1 (uint8 enum) — 0x5F % 1 == 0 → not suspicious
    EXPECT("uint8 enum @ 0x5F OK",  !Scharf::IsAlignmentSuspicious("EnumProperty", 0x5F, 1, false));
    EXPECT("uint8 enum @ 0x16A OK", !Scharf::IsAlignmentSuspicious("EnumProperty", 0x16A, 1, false));
    EXPECT("uint8 enum @ 0x99A OK", !Scharf::IsAlignmentSuspicious("EnumProperty", 0x99A, 1, false));
    EXPECT("uint8 enum @ 0x5E OK",  !Scharf::IsAlignmentSuspicious("EnumProperty", 0x5E, 1, false));

    // uint16 enum: alignment 2.
    EXPECT("uint16 enum @ 0x6 OK",  !Scharf::IsAlignmentSuspicious("EnumProperty", 0x6, 2, false));
    EXPECT("uint16 enum @ 0x5 BAD",  Scharf::IsAlignmentSuspicious("EnumProperty", 0x5, 2, false));

    // uint32 enum: alignment 4.
    EXPECT("uint32 enum @ 0xC OK",  !Scharf::IsAlignmentSuspicious("EnumProperty", 0xC, 4, false));
    EXPECT("uint32 enum @ 0xA BAD",  Scharf::IsAlignmentSuspicious("EnumProperty", 0xA, 4, false));
}

static void Test_Alignment_NameProperty_RespectsCpnMode() {
    // Non-CPN: FName = 8 bytes (int32 + int32), aligned to 4.
    //   CaravanSandWitch case: "MipFilter" (NameProperty) at offset 0x3C, ElemSize=8
    //   0x3C % 4 == 0 → not suspicious
    EXPECT("non-CPN FName @ 0x3C OK", !Scharf::IsAlignmentSuspicious("NameProperty", 0x3C, 8, false));
    EXPECT("non-CPN FName @ 0x4 OK",  !Scharf::IsAlignmentSuspicious("NameProperty", 0x4, 8, false));
    EXPECT("non-CPN FName @ 0x3 BAD",  Scharf::IsAlignmentSuspicious("NameProperty", 0x3, 8, false));

    // CPN (Titan Quest II): FName = 16 bytes, aligned to 8.
    EXPECT("CPN FName @ 0x10 OK", !Scharf::IsAlignmentSuspicious("NameProperty", 0x10, 16, true));
    EXPECT("CPN FName @ 0xC BAD",  Scharf::IsAlignmentSuspicious("NameProperty", 0xC, 16, true));
}

static void Test_Alignment_ScalarPrimitives() {
    // BoolProperty / ByteProperty: 1-byte aligned, never suspicious.
    EXPECT("Bool @ 0x1 OK",  !Scharf::IsAlignmentSuspicious("BoolProperty", 0x1, 1, false));
    EXPECT("Byte @ 0x7 OK",  !Scharf::IsAlignmentSuspicious("ByteProperty", 0x7, 1, false));

    // IntProperty / FloatProperty: 4-byte aligned.
    EXPECT("Int @ 0x4 OK",   !Scharf::IsAlignmentSuspicious("IntProperty", 0x4, 4, false));
    EXPECT("Int @ 0x6 BAD",   Scharf::IsAlignmentSuspicious("IntProperty", 0x6, 4, false));

    // Int64Property: 8-byte aligned.
    EXPECT("Int64 @ 0x8 OK", !Scharf::IsAlignmentSuspicious("Int64Property", 0x8, 8, false));
    EXPECT("Int64 @ 0xC BAD", Scharf::IsAlignmentSuspicious("Int64Property", 0xC, 8, false));
}

static void Test_Alignment_OffsetZeroNeverSuspicious() {
    EXPECT("Object @ 0 OK",     !Scharf::IsAlignmentSuspicious("ObjectProperty", 0, 8, false));
    EXPECT("Enum @ 0 OK",       !Scharf::IsAlignmentSuspicious("EnumProperty", 0, 1, false));
    EXPECT("Name CPN @ 0 OK",   !Scharf::IsAlignmentSuspicious("NameProperty", 0, 16, true));
}

static void Test_Alignment_UnknownTypesNotValidated() {
    // StructProperty layout depends on the script struct — skip alignment check.
    EXPECT("Struct @ 0x3 not flagged",
           !Scharf::IsAlignmentSuspicious("StructProperty", 0x3, 32, false));
    // FieldPathProperty / OptionalProperty / unknown types: skip.
    EXPECT("FieldPath @ 0x5 not flagged",
           !Scharf::IsAlignmentSuspicious("FieldPathProperty", 0x5, 16, false));
    EXPECT("OptionalProperty @ 0x9 not flagged",
           !Scharf::IsAlignmentSuspicious("OptionalProperty", 0x9, 8, false));
    EXPECT("garbage type not flagged",
           !Scharf::IsAlignmentSuspicious("GarbageProperty", 0x1, 4, false));
}

static void Test_Alignment_WeakAndSparseDelegate() {
    // FWeakObjectPtr: 2x int32, 4-byte aligned.
    EXPECT("Weak @ 0x4 OK",    !Scharf::IsAlignmentSuspicious("WeakObjectProperty", 0x4, 8, false));
    EXPECT("Weak @ 0x2 BAD",    Scharf::IsAlignmentSuspicious("WeakObjectProperty", 0x2, 8, false));

    // MulticastSparseDelegateProperty: only 1 byte stored on the field.
    EXPECT("SparseDelegate @ 0x5 OK",
           !Scharf::IsAlignmentSuspicious("MulticastSparseDelegateProperty", 0x5, 1, false));
}

// ----- Tot: when the per-command cancel is still owed ------------------------
//
// Audit #5 F2. The cancel latch cleared only when a session connected into an
// EMPTY connection registry ("firstConn"). That was right when there was one
// connection. With the two-lane split it is not: if the BULK lane drops
// mid-scan while the LIGHT lane stays up, the registry is never empty, so the
// latch is never cleared and every subsequent scan on the surviving lane aborts
// instantly -- for the life of the process.
//
// Ownership is the right question, not emptiness.

static void Test_Tot_PerCommandStillOwed() {
    // The one that was broken. Lane 1 (bulk) raised the cancel and is gone;
    // lane 2 (light) is still connected, so the registry is NOT empty -- but the
    // raiser is not in it, so the cancel is no longer owed.
    {
        const uint64_t owners[] = { 1 };
        const uint64_t live[]   = { 2 };
        EXPECT("raiser gone, sibling lane still up -> NOT owed",
               !Tot::PerCommandStillOwed(owners, 1, live, 1));
    }

    // Must NOT clear while the raiser is still registered: its orphaned scan has
    // to keep seeing the cancel until it unwinds.
    {
        const uint64_t owners[] = { 1 };
        const uint64_t live[]   = { 1, 2 };
        EXPECT("raiser still registered -> still owed",
               Tot::PerCommandStillOwed(owners, 1, live, 2));
    }

    // BOTH lanes dropped mid-command. Clearing on the first one's exit would free
    // a cancel the second still needs -- which is why the monitor records owners
    // unconditionally rather than only on the first raise.
    {
        const uint64_t owners[] = { 1, 2 };
        const uint64_t live[]   = { 2 };
        EXPECT("second raiser still live -> still owed",
               Tot::PerCommandStillOwed(owners, 2, live, 1));
        const uint64_t none[] = { 9 };
        EXPECT("neither raiser live -> NOT owed",
               !Tot::PerCommandStillOwed(owners, 2, none, 1));
    }

    // The case the old firstConn rule DID handle -- an empty registry. The new
    // rule must still catch it, or the fix is a regression on the path that worked.
    {
        const uint64_t owners[] = { 1, 2, 3 };
        EXPECT("empty registry -> NOT owed (subsumes the old firstConn rule)",
               !Tot::PerCommandStillOwed(owners, 3, nullptr, 0));
    }

    // Nobody raised anything: never owed, whatever is connected.
    {
        const uint64_t live[] = { 1, 2, 3 };
        EXPECT("no owners -> never owed",
               !Tot::PerCommandStillOwed(nullptr, 0, live, 3));
    }

    // Ids are monotonic and never reused, so a NEW connection that happens to sit
    // where an old one did must not inherit its cancel. (This is why the owner set
    // stores a seq and not a pointer -- the allocator reuses addresses.)
    {
        const uint64_t owners[] = { 7 };
        const uint64_t live[]   = { 8 };
        EXPECT("a later connection does not inherit an earlier one's cancel",
               !Tot::PerCommandStillOwed(owners, 1, live, 1));
    }
}

// ----- Aura: which deep leaves the static scan index already covers -----------
//
// Audit #5 A4. Value Search's deep pass skipped on `depth < 2` alone -- "depth 1
// is covered by the static paths". That is true only for ARRAYS:
// collectStructArrayInner is reached solely from the ArrayProperty branch, so a
// struct-sided TSet<FStruct> or TMap<K, FStruct> element was covered by NEITHER
// the static index nor the deep pass. An everyday TMap<FName, FItemData>
// inventory count was unfindable with Deep ON as well as OFF.
//
// The two sibling consumers of the same walker already had the right shape, and
// that asymmetry is what made this visible: the snapshot path tests
// `leafName.empty() && depth < 2`, the group scan uses `depth < 1`.
//
// NOTE the rule is pinnable here; the WIRING is not -- no target compiles
// Aura.cpp. The wiring is instead made a COMPILE error by placing
// ContainerLeaf::kind immediately before `depth`, so an un-threaded aggregate
// init binds an int to a scoped enum.

static void Test_Aura_DeepLeafCoverage() {
    using Aura::DeepLeafCoveredByStaticScanIndex;
    using K = Aura::ContainerKind;

    // depth 0 -- the object's own direct fields; always statically indexed.
    EXPECT("depth 0 is covered", DeepLeafCoveredByStaticScanIndex(0, K::Direct, false));
    EXPECT("depth 0 is covered whatever the kind",
           DeepLeafCoveredByStaticScanIndex(0, K::Map, false));

    // depth 1, whole element -- a leaf container's element or a scalar map side.
    // The static paths cover these for EVERY kind.
    EXPECT("array leaf-element covered",  DeepLeafCoveredByStaticScanIndex(1, K::Array, true));
    EXPECT("set leaf-element covered",    DeepLeafCoveredByStaticScanIndex(1, K::Set,   true));
    EXPECT("map scalar side covered",     DeepLeafCoveredByStaticScanIndex(1, K::Map,   true));

    // depth 1, a NAMED field inside a struct element -- covered only for arrays.
    EXPECT("array struct-element field covered (collectStructArrayInner)",
           DeepLeafCoveredByStaticScanIndex(1, K::Array, false));

    // THE DEFECT, both halves.
    EXPECT("set struct-element field is NOT covered",
           !DeepLeafCoveredByStaticScanIndex(1, K::Set, false));
    EXPECT("map struct-side field is NOT covered",
           !DeepLeafCoveredByStaticScanIndex(1, K::Map, false));

    // Nothing static reaches past one level.
    EXPECT("depth 2 array not covered",   !DeepLeafCoveredByStaticScanIndex(2, K::Array, false));
    EXPECT("depth 2 whole-element not covered",
           !DeepLeafCoveredByStaticScanIndex(2, K::Array, true));
    EXPECT("depth 4 not covered",         !DeepLeafCoveredByStaticScanIndex(4, K::Map, false));

    // Unknown is the zero enumerator and must never read as "covered" -- a leaf
    // struct that forgets to set `kind` value-initialises to it, and answering
    // "covered" there would silently reproduce A4.
    EXPECT("Unknown at depth 1 is NOT covered",
           !DeepLeafCoveredByStaticScanIndex(1, K::Unknown, false));
    EXPECT("Unknown is the zero enumerator",
           static_cast<int>(K::Unknown) == 0);
    EXPECT("Array is NOT the zero enumerator",
           static_cast<int>(K::Array) != 0);
}

// ----- Ubel: the class-walk cache bound -------------------------------------
//
// Audit #5 U5. The finding was re-filed FOUR times saying eviction is illegal
// until WalkClassEx stops returning `const ClassInfo&`. That is right about the
// ENRICHED cache and wrong about the plain one: WalkClass returns BY VALUE
// (Ubel.h) and every s_walkClassCache touch copies under the mutex, so bounding
// it is legal today with zero call-site change. Half the bytes were reclaimable
// the whole time.
//
// The LRU itself lives in Ubel.cpp, which no target compiles. What IS pinnable
// is the sizing rule and the cap, and the cap is the number most likely to be
// "tuned" later by someone reading the wrong denominator.

static void Test_Ubel_ClassCacheBound() {
    // 512 was the first proposal, sized against "~50x the deepest super chain".
    // That is the wrong denominator: the working set is the classes touched in
    // ONE scan pass, and the reference log touches 10,046 distinct classes.
    EXPECT("cap is not the 512 that the wrong denominator produced",
           Ubel::kMaxWalkClassCacheEntries != 512);
    EXPECT("cap is 2048", Ubel::kMaxWalkClassCacheEntries == 2048);

    // A bound that cannot bind is the failure mode worth guarding: it would make
    // get_diagnostics report a cap while the map grew forever.
    EXPECT("cap is a real bound", Ubel::kMaxWalkClassCacheEntries > 0);
    EXPECT("cap is below the reference working set, i.e. it actually evicts",
           Ubel::kMaxWalkClassCacheEntries < 10046);
}

static void Test_Ubel_EstimateClassInfoBytes() {
    const size_t base = Ubel::EstimateClassInfoBytes(0, 0, 0, 0, 0);
    EXPECT("an empty ClassInfo still costs its own struct", base == sizeof(ClassInfo));

    // Every input must be additive -- a term dropped from the sum is exactly how
    // a memory counter under-reports and a bound looks unnecessary.
    EXPECT("name length counts",
           Ubel::EstimateClassInfoBytes(10, 0, 0, 0, 0) == base + 10);
    EXPECT("path length counts",
           Ubel::EstimateClassInfoBytes(0, 20, 0, 0, 0) == base + 20);
    EXPECT("super name counts",
           Ubel::EstimateClassInfoBytes(0, 0, 30, 0, 0) == base + 30);
    EXPECT("fields count, scaled by their width",
           Ubel::EstimateClassInfoBytes(0, 0, 0, 7, 40) == base + 280);

    // The dominant term on a real class is the field vector, not the strings.
    const size_t realistic =
        Ubel::EstimateClassInfoBytes(24, 64, 16, 182, sizeof(FieldInfo));
    EXPECT("a 182-field class is dominated by its fields",
           realistic > 182 * sizeof(FieldInfo));
}

// ----- Stark: not re-entering our own ProcessEvent detour --------------------
//
// Audit #5 ST1. MinHook patches UObject::ProcessEvent's PROLOGUE, so a caller of
// ours that resolves the address out of an instance's vtable and calls it lands
// in HookedProcessEvent -- on whatever thread it happened to be on. The drain
// there is gated only on "is the queue non-empty", so a pipe lane or the Mimic
// polling thread would execute requests that were queued PRECISELY because a
// caller judged them unsafe off the game thread.
//
// It is not a tight race: a timed-out invoke stays queued deliberately, with its
// own parameter copy, expecting a later drain -- so after one timeout the window
// is open indefinitely.
//
// The repair is not a thread-identity check. Nothing in this tree resolves the
// game thread id, so any gate would be guessing, and a gate that guesses wrong
// never drains -- which times out every game-thread invoke and is strictly worse
// than the defect. We call the trampoline instead, as Grausam already does.

static void Test_Stark_ShouldUseTrampoline() {
    const uintptr_t kHooked = 0x7FF6'0000'1000ull;
    const uintptr_t kOther  = 0x7FF6'0000'2000ull;

    // The ordinary case: this instance's PE slot IS the patched address.
    EXPECT("trampoline when the slot is the patched address",
           Stark::ShouldUseTrampoline(kHooked, kHooked, true));

    // THE LOAD-BEARING CASE. A class that genuinely OVERRIDES ProcessEvent has a
    // different slot; that slot was never patched, so calling the trampoline for
    // it would silently run the BASE implementation instead of the override.
    // Fail open to the caller's own address.
    EXPECT("NOT the trampoline when the class overrides ProcessEvent",
           !Stark::ShouldUseTrampoline(kOther, kHooked, true));

    // No trampoline to call -- hook never installed, or install failed.
    EXPECT("no trampoline available -> call directly",
           !Stark::ShouldUseTrampoline(kHooked, kHooked, false));

    // Nothing patched yet: there is no address to match against, and a 0 ==  0
    // comparison must not read as agreement.
    EXPECT("nothing hooked -> call directly",
           !Stark::ShouldUseTrampoline(kHooked, 0, true));
    EXPECT("nothing hooked, and the resolved address is 0 too",
           !Stark::ShouldUseTrampoline(0, 0, true));
}

static void Test_Stark_ShouldDrainQueue() {
    // The pre-existing fast path: nothing enqueued, nothing to do.
    EXPECT("empty queue never drains",         !Stark::ShouldDrainQueue(0, false));
    EXPECT("empty queue never drains (ours)",  !Stark::ShouldDrainQueue(0, true));

    // A genuine game-thread tick with work pending.
    EXPECT("game entry with work drains",       Stark::ShouldDrainQueue(1, false));
    EXPECT("game entry with lots of work drains", Stark::ShouldDrainQueue(64, false));

    // THE REGRESSION GUARD. The detour was re-entered by a nested dispatch
    // underneath a call WE issued -- that is not a game-thread tick, and draining
    // there runs queued work on our own caller's thread.
    EXPECT("our own re-entry does NOT drain",  !Stark::ShouldDrainQueue(1, true));
    EXPECT("our own re-entry does NOT drain, whatever the depth",
           !Stark::ShouldDrainQueue(64, true));
}

// ----- Stark: ProcessEvent detection lifecycle ---------------------------------
//
// [PEHOOKONCE-2026-08-18]. The field defect: a detection that failed because there
// was nothing to detect YET (proxy mode starts the pipe server only, so GObjects is
// unset until a scan) stored the same -1 as a hard failure, and every retry path in
// Frieren was gated against -1. One `pe_profile_start` before the scan therefore
// poisoned the ProcessEvent hook for the entire process -- no invoke, no click and
// no later scan could ever install it, and the message told the user to retry the
// one thing that could not work.
//
// These pin the SEPARATION. Frieren.cpp is not compiled by any test target, so the
// rules live in Stark.h precisely so that this file can hold them still.

static void Test_Stark_PeOffsetSentinels() {
    // The two negatives are NOT interchangeable, which is the entire finding.
    EXPECT("a real offset is usable",        Stark::PeOffsetUsable(0x220));
    EXPECT("a real offset is not retryable", !Stark::PeOffsetRetryable(0x220));

    EXPECT("not-detected is NOT usable",     !Stark::PeOffsetUsable(Stark::kPeOffsetNotDetected));
    EXPECT("not-detected IS retryable",      Stark::PeOffsetRetryable(Stark::kPeOffsetNotDetected));

    EXPECT("hard failure is NOT usable",     !Stark::PeOffsetUsable(Stark::kPeOffsetFailed));
    EXPECT("hard failure is NOT retryable",  !Stark::PeOffsetRetryable(Stark::kPeOffsetFailed));

    // Offset 0 is a legal vtable offset in principle and must not read as a sentinel.
    EXPECT("offset 0 is usable",             Stark::PeOffsetUsable(0));
    EXPECT("offset 0 is not retryable",      !Stark::PeOffsetRetryable(0));
}

static void Test_Stark_ShouldRetryPeDetection() {
    constexpr int kNotDetected = Stark::kPeOffsetNotDetected;

    // THE FIX. An armed sentinel retries -- this is the path that was unreachable.
    EXPECT("armed sentinel, first attempt -> retry",
           Stark::ShouldRetryPeDetection(kNotDetected, 0, 10'000, 0, false));

    // Already answered, or answered "never": no work either way.
    EXPECT("a usable offset never re-detects",
           !Stark::ShouldRetryPeDetection(0x220, 0, 10'000, 0, false));
    EXPECT("a hard failure never re-detects",
           !Stark::ShouldRetryPeDetection(Stark::kPeOffsetFailed, 0, 10'000, 0, false));
    EXPECT("a hard failure does not re-detect even when FORCED",
           !Stark::ShouldRetryPeDetection(Stark::kPeOffsetFailed, 0, 10'000, 0, true));

    // THE ANTI-STORM HALF. Re-arming without these turns the ordinary invoke path
    // -- which a 10 Hz feature worker walks -- into a re-scan of up to 12 vtables x
    // 0x2000 bytes, ten times a second, forever.
    EXPECT("inside the cooldown -> no retry",
           !Stark::ShouldRetryPeDetection(kNotDetected, 1, 10'500, 10'000, false));
    EXPECT("cooldown just elapsed -> retry",
           Stark::ShouldRetryPeDetection(kNotDetected, 1, 11'000, 10'000, false));
    EXPECT("budget spent -> no retry",
           !Stark::ShouldRetryPeDetection(kNotDetected, Stark::kMaxPeDetectAttempts,
                                          99'000, 10'000, false));
    EXPECT("one attempt left -> retry",
           Stark::ShouldRetryPeDetection(kNotDetected, Stark::kMaxPeDetectAttempts - 1,
                                         99'000, 10'000, false));

    // A user-initiated attempt (a feature switching on) skips cooldown and cap for
    // the same reason TryInstallGameThreadHook's `force` does: the user is waiting.
    EXPECT("force beats the cooldown",
           Stark::ShouldRetryPeDetection(kNotDetected, 1, 10'001, 10'000, true));
    EXPECT("force beats the budget",
           Stark::ShouldRetryPeDetection(kNotDetected, Stark::kMaxPeDetectAttempts,
                                         99'000, 10'000, true));

    // lastAttemptMs == 0 means "never attempted", not "attempted at tick 0" -- a
    // cooldown measured from it would suppress the very first retry.
    EXPECT("never attempted -> not throttled",
           Stark::ShouldRetryPeDetection(kNotDetected, 1, 5, 0, false));
}

static void Test_Stark_PeValidationFailureVerdict() {
    constexpr int kOffset = 0x220;

    // [PEHOOK-2026-08-17]: the version TABLE is a per-version guess with no evidence
    // from the binary, and it is what produced the one measured mis-detection
    // (DumperTest UE 5.4 Development, primary=0x220, hook fired 0 times).
    EXPECT("version-table zero-fires is acted on",
           Stark::ShouldActOnValidationFailure(true));
    EXPECT("version-table failure re-arms detection",
           Stark::PeOffsetAfterValidationFailure(true, kOffset, 1) == Stark::kPeOffsetNotDetected);
    EXPECT("second failure still re-arms",
           Stark::PeOffsetAfterValidationFailure(true, kOffset, 2) == Stark::kPeOffsetNotDetected);

    // Bounded: a genuinely wrong slot re-detects to the same wrong slot, so the
    // loop must terminate rather than reinstall forever.
    EXPECT("the last allowed failure gives up",
           Stark::PeOffsetAfterValidationFailure(true, kOffset,
                                                 Stark::kMaxPeValidationFailures)
               == Stark::kPeOffsetFailed);
    EXPECT("past the cap stays given up",
           Stark::PeOffsetAfterValidationFailure(true, kOffset,
                                                 Stark::kMaxPeValidationFailures + 5)
               == Stark::kPeOffsetFailed);

    // THE FALSE-POSITIVE GUARD, and the reason this is not "act on every zero".
    // A zero fire count also describes an idle game thread (paused / loading /
    // minimised under t.IdleWhenNotForeground). The pattern scan fingerprints
    // ProcessEvent's own body and has never been observed wrong, so a zero there
    // reads as "the game was idle" and the correct hook must survive it.
    EXPECT("pattern-scan zero-fires is NOT acted on",
           !Stark::ShouldActOnValidationFailure(false));
    EXPECT("a pattern-scan offset is left untouched",
           Stark::PeOffsetAfterValidationFailure(false, kOffset, 1) == kOffset);
    EXPECT("a pattern-scan offset survives repeated zeroes",
           Stark::PeOffsetAfterValidationFailure(false, kOffset, 99) == kOffset);
}

// ----- Lineal: FUObjectItem SerialNumber offset --------------------------------
//
// Audit #5 A1. Aura::GetSerialNumber used to compute this inline as
// `s_itemSize >= 24 ? 0x10 : 0x0C` -- a two-way split covering only strides 16
// and 24. The reachable stride set is {16, 20, 24, 32}: Aura's auto-probe tries
// {16, 24, 32, 20} and UE5_InitWithExtendedLayout forces any of
// {0x14, 0x18, 0x10, 0x20}.
//
// At stride 20 the old expression returned 0x0C, which is ClusterRootIndex.
// Ubel::ResolveWeakObjectPtr then compares that against the stored serial with a
// bare `if (actualSerial != serialNumber) return 0;` -- no fallback, no retry,
// no log -- so EVERY weak reference reads as stale. That empties
// WeakObjectProperty and the whole delegate family, and costs the Soft/Lazy
// handlers their resolved live object.
//
// The rule lives in Lineal.h precisely so it can be tested: no target compiles
// Aura.cpp, but this file already includes Lineal.h.

static void Test_Lineal_SerialOffsetForLayout() {
    using Lineal::SerialOffsetForLayout;
    using M = Lineal::ItemLayoutMode;

    // --- Classic: the offset is decided by whether ClusterRootIndex precedes
    // the serial, which it does for every stride above 16.
    EXPECT("classic 16 -> 0x0C", SerialOffsetForLayout(M::Classic, 16, 0, 0x0C) == 0x0C);

    // THE REGRESSION ROW. Avowed's packed 20-byte FUObjectItem:
    // {Object@+0x00, Flags@+0x08, ClusterRoot@+0x0C, Serial@+0x10} -- from the
    // Ghidra decompilation of AllocateUObjectIndex in docs/avowed-gobjects-fix.md.
    EXPECT("classic 20 -> 0x10 (Avowed)", SerialOffsetForLayout(M::Classic, 20, 0, 0x0C) == 0x10);

    EXPECT("classic 24 -> 0x10", SerialOffsetForLayout(M::Classic, 24, 0, 0x0C) == 0x10);
    EXPECT("classic 32 -> 0x10", SerialOffsetForLayout(M::Classic, 32, 0, 0x0C) == 0x10);

    // --- UE5.7+ unpacked: FlagsAndRefCount(8) + Object(8) + SerialNumber(4),
    // so the serial sits immediately after the object wherever that landed.
    EXPECT("unpacked57 objOff 8 -> 0x10",
           SerialOffsetForLayout(M::Unpacked57, 24, 0x08, 0x0C) == 0x10);
    EXPECT("unpacked57 objOff 16 -> 0x18",
           SerialOffsetForLayout(M::Unpacked57, 32, 0x10, 0x0C) == 0x18);

    // --- Packed UE5.7+: layout is UNVERIFIED, so the value is whatever
    // set_packed_consts calibrated. It must pass through untouched -- including
    // when the stride would otherwise imply something else.
    EXPECT("packed57 passes the calibrated value through",
           SerialOffsetForLayout(M::Packed57, 24, 0x08, 0x0C) == 0x0C);
    EXPECT("packed57 honours a recalibration",
           SerialOffsetForLayout(M::Packed57, 24, 0x08, 0x14) == 0x14);

    // --- The function is pure: same inputs, same answer, no hidden state.
    EXPECT("pure / repeatable",
           SerialOffsetForLayout(M::Classic, 20, 0, 0x0C) ==
           SerialOffsetForLayout(M::Classic, 20, 0, 0x0C));
}

// ----- Mimic: the CE Lua <-> DLL mailbox LAYOUT --------------------------------
//
// What used to sit here was a poll-latency micro-benchmark that touched no Mimic
// code at all (audit #5 AD6). It re-implemented EnsureWinmmResolved inside the
// test process and then asserted a fact about THIS HOST -- that
// timeBeginPeriod(1) makes 100 x Sleep(1) finish under 300 ms. True, occasionally
// useful, and invariant to every line in dll/src: it passed with Mimic.cpp
// deleted from the tree. Its own comment, and the CMakeLists comment justifying
// why winmm is not linked, both claimed it "covers the actual mechanism".
//
// What replaces it is the thing that genuinely cannot be checked anywhere else.
// `Mimic.cpp` is not compiled by any target, but `Mimic.h` is pure data, so its
// layout IS reachable from here. And that layout is a published cross-language
// contract: every offset below is baked as a literal into the emitted CE Lua
// (`Services/CeMailboxLayout.cs`, whose comment reads "must match Mimic.h
// MailboxData") and into scripts/UE5CEDumper.CT. Until now nothing enforced it
// on either side -- `tools/check_mailbox_contract.py` hashes the comment-stripped
// surface but never computes an offset, so it cannot tell a moved field from a
// renamed one. A silent shift here does not fail a build; it makes every saved
// .CT write to the wrong address.
//
// These numbers are deliberately spelled as literals rather than derived from
// the struct: deriving them from the same declaration they are checking would
// assert only that C++ agrees with itself.

static void Test_Mimic_MailboxLayout() {
    // Command/status header.
    EXPECT("mailbox cmd @ 0x00",          offsetof(Mimic::MailboxData, cmd)           == 0x00);
    EXPECT("mailbox status @ 0x04",       offsetof(Mimic::MailboxData, status)        == 0x04);
    EXPECT("mailbox result @ 0x08",       offsetof(Mimic::MailboxData, result)        == 0x08);
    EXPECT("mailbox initState @ 0x0C",    offsetof(Mimic::MailboxData, initState)     == 0x0C);

    // Operand slots -- reused per command as op / knobId / value / slot.
    EXPECT("mailbox instanceAddr @ 0x10", offsetof(Mimic::MailboxData, instanceAddr)  == 0x10);
    EXPECT("mailbox ufuncAddr @ 0x18",    offsetof(Mimic::MailboxData, ufuncAddr)     == 0x18);

    // UFunction metadata the DLL fills in.
    EXPECT("mailbox parmsSize @ 0x20",    offsetof(Mimic::MailboxData, parmsSize)     == 0x20);
    EXPECT("mailbox numParms @ 0x22",     offsetof(Mimic::MailboxData, numParms)      == 0x22);
    EXPECT("mailbox functionFlags @ 0x24",offsetof(Mimic::MailboxData, functionFlags) == 0x24);

    // Fixed-width string blocks. A size change here silently shifts everything
    // after it, which is the failure this test exists to make loud.
    EXPECT("mailbox className @ 0x28",    offsetof(Mimic::MailboxData, className)     == 0x28);
    EXPECT("mailbox funcName @ 0x128",    offsetof(Mimic::MailboxData, funcName)      == 0x128);
    EXPECT("mailbox errorMsg @ 0x228",    offsetof(Mimic::MailboxData, errorMsg)      == 0x228);

    // The paged in/out buffer -- CeMailboxLayout.OffParamsData is this number.
    EXPECT("mailbox paramsData @ 0x328",  offsetof(Mimic::MailboxData, paramsData)    == 0x328);

    EXPECT("mailbox className is 256",    sizeof(Mimic::MailboxData::className)  == 256);
    EXPECT("mailbox funcName is 256",     sizeof(Mimic::MailboxData::funcName)   == 256);
    EXPECT("mailbox errorMsg is 256",     sizeof(Mimic::MailboxData::errorMsg)   == 256);
    EXPECT("mailbox paramsData is 1024",  sizeof(Mimic::MailboxData::paramsData) == 1024);

    // Contract 3 grew the struct at the TAIL — which is the only place it can grow
    // without invalidating every saved .CT, so pinning that these two sit AFTER
    // paramsData (and that nothing above them moved) is the whole compatibility
    // claim, checked rather than asserted in prose.
    EXPECT("mailbox cmdFlags @ 0x728",    offsetof(Mimic::MailboxData, cmdFlags)    == 0x728);
    EXPECT("mailbox cmdOutFlags @ 0x72C", offsetof(Mimic::MailboxData, cmdOutFlags) == 0x72C);

    // Whole-struct size: 0x328 + 1024 + 8 = 1840. The header comment says "~1848",
    // which is what an unchecked number drifts into.
    EXPECT("mailbox total size 1840",     sizeof(Mimic::MailboxData) == 1840);
    EXPECT("mailbox fits one page",       sizeof(Mimic::MailboxData) <= 4096);
}

// ----- Mimic: CMD_LIST_INSTANCES page geometry ---------------------------------
//
// The wire format of one LIST_INSTANCES page depends on a single input bit, and
// the two sides that have to agree about it are written in different languages:
// Mimic.cpp packs the entries, scripts/ue5_freeze_helper.lua unpacks them. Neither
// can see the other, so the rule lives in the header both are documented against
// and is pinned here. Getting the stride wrong does not crash — it reads the high
// half of one pointer as the next object and freezes an address that is not one.
static void Test_Mimic_ListInstancesGeometry() {
    EXPECT("exact entry is 8 bytes",    Mimic::ListInstancesEntrySize(false) == 8);
    EXPECT("derived entry is 16 bytes", Mimic::ListInstancesEntrySize(true)  == 16);

    // Derived carries the per-entry UClass* witness, so it fits half as many.
    EXPECT("exact page holds 128",   Mimic::ListInstancesPerPage(false) == 128);
    EXPECT("derived page holds 64",  Mimic::ListInstancesPerPage(true)  == 64);

    // A page must never overrun paramsData — the derived format halved the count
    // rather than spilling, and this is what says so.
    EXPECT("exact page fits paramsData",
           Mimic::ListInstancesPerPage(false) * Mimic::ListInstancesEntrySize(false)
               <= sizeof(Mimic::MailboxData::paramsData));
    EXPECT("derived page fits paramsData",
           Mimic::ListInstancesPerPage(true) * Mimic::ListInstancesEntrySize(true)
               <= sizeof(Mimic::MailboxData::paramsData));

    // Both caps must be reachable inside the page budget the helper walks, or the
    // last instances are enumerated by the DLL and never fetched — a freeze that
    // silently drops its tail.
    EXPECT("exact cap fits 16 pages",
           Mimic::LIST_INSTANCES_MAX_EXACT
               <= static_cast<int>(Mimic::LIST_INSTANCES_MAX_PAGES
                                   * Mimic::ListInstancesPerPage(false)));
    EXPECT("derived cap fits 16 pages",
           Mimic::LIST_INSTANCES_MAX_DERIVED
               <= static_cast<int>(Mimic::LIST_INSTANCES_MAX_PAGES
                                   * Mimic::ListInstancesPerPage(true)));

    // The flag bits the CE Lua side writes/reads as literals.
    EXPECT("LI_IN_DERIVED = 1",    static_cast<uint32_t>(Mimic::LI_IN_DERIVED)    == 1u);
    EXPECT("LI_OUT_TRUNCATED = 1", static_cast<uint32_t>(Mimic::LI_OUT_TRUNCATED) == 1u);
}

// The Cmd / op enumerators CE Lua writes into the mailbox. Renumbering one is a
// breaking contract change (see MAILBOX_CONTRACT_MIN in Mimic.h); these values
// are duplicated in Services/CeMailboxLayout.cs and in scripts/UE5CEDumper.CT,
// neither of which the compiler can see.
static void Test_Mimic_CommandNumbering() {
    EXPECT("CMD_SET_DEBUG_CAMERA = 7", static_cast<int>(Mimic::CMD_SET_DEBUG_CAMERA) == 7);
    EXPECT("CMD_TELEPORT = 8",         static_cast<int>(Mimic::CMD_TELEPORT)         == 8);
    EXPECT("CMD_PROTECT = 9",          static_cast<int>(Mimic::CMD_PROTECT)          == 9);
    EXPECT("CMD_MOVEMENT = 10",        static_cast<int>(Mimic::CMD_MOVEMENT)         == 10);
    EXPECT("CMD_FLY = 11",             static_cast<int>(Mimic::CMD_FLY)              == 11);
    EXPECT("CMD_FOREGROUND = 12",      static_cast<int>(Mimic::CMD_FOREGROUND)       == 12);
    EXPECT("CMD_QUERY_PTR = 13",       static_cast<int>(Mimic::CMD_QUERY_PTR)        == 13);
    EXPECT("CMD_SEETHROUGH = 14",      static_cast<int>(Mimic::CMD_SEETHROUGH)       == 14);
    EXPECT("CMD_TIME = 15",            static_cast<int>(Mimic::CMD_TIME)             == 15);

    // InitState -- polled as a bare memory read by the CE bootstrap, so these
    // are as load-bearing as the offsets above.
    EXPECT("INIT_IDLE = 0",    static_cast<int>(Mimic::INIT_IDLE)    == 0);
    EXPECT("INIT_RUNNING = 1", static_cast<int>(Mimic::INIT_RUNNING) == 1);
    EXPECT("INIT_READY = 2",   static_cast<int>(Mimic::INIT_READY)   == 2);
    EXPECT("INIT_FAILED = 3",  static_cast<int>(Mimic::INIT_FAILED)  == 3);
    EXPECT("INIT_SKIPPED = 4", static_cast<int>(Mimic::INIT_SKIPPED) == 4);

    // The published compatibility RANGE. A script checks MIN <= its baked
    // version <= CONTRACT before its first write.
    EXPECT("contract range is sane", Mimic::MAILBOX_CONTRACT_MIN <= Mimic::MAILBOX_CONTRACT);
    EXPECT("contract is 3",          Mimic::MAILBOX_CONTRACT     == 3);
    EXPECT("contract min is 1",      Mimic::MAILBOX_CONTRACT_MIN == 1);

    // g_mailboxContract is a SEPARATE exported symbol, read before anything is
    // written -- so CE Lua reads these three at fixed offsets too, and they are
    // the one thing a script consults to decide whether the rest of the layout
    // can be trusted. If they move, the version check itself reads garbage.
    EXPECT("contract magic @ 0x00",   offsetof(Mimic::MailboxContract, magic)   == 0x00);
    EXPECT("contract current @ 0x04", offsetof(Mimic::MailboxContract, current) == 0x04);
    EXPECT("contract minimum @ 0x08", offsetof(Mimic::MailboxContract, minimum) == 0x08);
    EXPECT("contract struct is 12",   sizeof(Mimic::MailboxContract) == 12);
    EXPECT("contract magic value",    Mimic::MAILBOX_CONTRACT_MAGIC == 0x43354555u);
}

// ----- Mimic: CMD_INVOKE routing + the init gate (audit #5 MB1 / MB2) ----------
//
// Both rules used to live inline in Mimic.cpp, which no target compiles — so the
// only way to state them checkably was to move them into the header. They are the
// two places a mailbox command can pick the WRONG behaviour without failing:
// MB1 runs a stateful UFunction on the wrong thread, MB2 refuses a command that
// would have worked.

static void Test_Mimic_InvokeRouting() {
    constexpr uint32_t N = Mimic::FUNC_FLAG_NATIVE;   // 0x0400
    constexpr uint32_t S = Mimic::FUNC_FLAG_STATIC;   // 0x2000

    // The UE bit values themselves — a generated script never sees these, but
    // getting one wrong silently reclassifies every function.
    EXPECT("FUNC_FLAG_NATIVE = 0x400",  N == 0x00000400u);
    EXPECT("FUNC_FLAG_STATIC = 0x2000", S == 0x00002000u);

    // Only Native AND Static together take the direct path.
    EXPECT("native+static routes direct", Mimic::ShouldRouteDirectInvoke(N | S, true));
    EXPECT("native alone queues",         !Mimic::ShouldRouteDirectInvoke(N, true));
    EXPECT("static alone queues",         !Mimic::ShouldRouteDirectInvoke(S, true));
    EXPECT("no flags queues",             !Mimic::ShouldRouteDirectInvoke(0, true));

    // Unrelated bits must not disturb the verdict either way.
    EXPECT("extra flags keep direct",
           Mimic::ShouldRouteDirectInvoke(N | S | 0x00000001u | 0x04000000u, true));
    EXPECT("extra flags keep queued",
           !Mimic::ShouldRouteDirectInvoke(N | 0x00000001u | 0x04000000u, true));

    // THE MB1 RULE. `flagsResolved=false` is what the caller passes when it could
    // not re-read the UFunction — and it must beat any flag value, because the
    // alternative is trusting a mailbox field the previous command wrote. If this
    // assertion is dropped, a stale Native|Static routes a stateful actor
    // UFunction off the game thread and nothing reports it.
    EXPECT("unresolved never routes direct",
           !Mimic::ShouldRouteDirectInvoke(N | S, false));
    EXPECT("unresolved with 0xFFFFFFFF never routes direct",
           !Mimic::ShouldRouteDirectInvoke(0xFFFFFFFFu, false));

    // The two commands that REPURPOSE functionFlags as a page count. Their values
    // are small, so they can never satisfy the mask — the stale-field hazard is a
    // false NEGATIVE from these, and a false POSITIVE only from CMD_FIND_FUNCTION
    // having resolved a different function. Pinned so a future page-count encoding
    // that happens to set both bits cannot arrive unnoticed.
    for (uint32_t pages = 1; pages <= 64; ++pages) {
        if (Mimic::ShouldRouteDirectInvoke(pages, true)) {
            EXPECT("a LIST_* page count must never look static-native", false);
            break;
        }
    }
    EXPECT("page counts never look static-native", true);
}

static void Test_Mimic_CommandRequiresInit() {
    // The ONE exemption, and the reason it is safe: Grausam touches no UObject and
    // the pipe path gates it on nothing.
    EXPECT("CMD_FOREGROUND is exempt", !Mimic::CommandRequiresInit(Mimic::CMD_FOREGROUND));

    // Negative control — everything else must still be gated. This is the half
    // that matters: over-exempting turns "-10 DLL not initialized" into a handler
    // dereferencing caches the scan never filled. Enumerated explicitly rather
    // than looped so adding a Cmd forces a decision here.
    EXPECT("CMD_INVOKE needs init",           Mimic::CommandRequiresInit(Mimic::CMD_INVOKE));
    EXPECT("CMD_FIND_INSTANCE needs init",    Mimic::CommandRequiresInit(Mimic::CMD_FIND_INSTANCE));
    EXPECT("CMD_FIND_FUNCTION needs init",    Mimic::CommandRequiresInit(Mimic::CMD_FIND_FUNCTION));
    EXPECT("CMD_INVOKE_BY_NAME needs init",   Mimic::CommandRequiresInit(Mimic::CMD_INVOKE_BY_NAME));
    EXPECT("CMD_LIST_FUNCTIONS needs init",   Mimic::CommandRequiresInit(Mimic::CMD_LIST_FUNCTIONS));
    EXPECT("CMD_LIST_INSTANCES needs init",   Mimic::CommandRequiresInit(Mimic::CMD_LIST_INSTANCES));
    EXPECT("CMD_SET_DEBUG_CAMERA needs init", Mimic::CommandRequiresInit(Mimic::CMD_SET_DEBUG_CAMERA));
    EXPECT("CMD_TELEPORT needs init",         Mimic::CommandRequiresInit(Mimic::CMD_TELEPORT));
    EXPECT("CMD_PROTECT needs init",          Mimic::CommandRequiresInit(Mimic::CMD_PROTECT));
    EXPECT("CMD_MOVEMENT needs init",         Mimic::CommandRequiresInit(Mimic::CMD_MOVEMENT));
    EXPECT("CMD_FLY needs init",              Mimic::CommandRequiresInit(Mimic::CMD_FLY));
    // Documented as "read-only + thread-agnostic", which is NOT the same claim:
    // it reads the caches the scan fills and iterates GObjects.
    EXPECT("CMD_QUERY_PTR needs init",        Mimic::CommandRequiresInit(Mimic::CMD_QUERY_PTR));
    EXPECT("CMD_SEETHROUGH needs init",       Mimic::CommandRequiresInit(Mimic::CMD_SEETHROUGH));
    // "Pure reflected memory write" — reflected means GObjects.
    EXPECT("CMD_TIME needs init",             Mimic::CommandRequiresInit(Mimic::CMD_TIME));

    // An unknown command must be gated too: it falls through to "Unknown command",
    // and the gate is the cheaper refusal.
    EXPECT("an unknown cmd is gated", Mimic::CommandRequiresInit(9999));

    // Exactly ONE exemption across the whole declared command space. A future
    // handler that quietly adds itself to the exemption list trips this.
    int exempt = 0;
    for (int32_t c = 0; c <= Mimic::CMD_TIME; ++c)
        if (!Mimic::CommandRequiresInit(c)) ++exempt;
    EXPECT("exactly one command is init-exempt", exempt == 1);
}

// ----- Flamme: the hint-cache publish gate (audit #5 FL1) ----------------------
//
// The rule that decides whether a staged temp may be renamed over the real cache.
// Getting it wrong in the permissive direction publishes a TRUNCATED JSON document
// over the only copy, and LoadHints then returns empty for every game at once —
// pattern IDs, ueVersion, the user's version override and the invoke timeout.
// Nothing compiles Flamme.cpp, so this predicate is the only assertable part.
static void Test_Flamme_AtomicPublishGate() {
    using Flamme::ShouldPublishAtomicWrite;

    // The one passing shape: stream clean, size readable, size exact.
    EXPECT("clean write publishes",  ShouldPublishAtomicWrite(true, true, 4096, 4096));

    // Each detector alone must be able to veto — if either could not, it would be
    // decoration rather than a second detector.
    EXPECT("stream failure refuses", !ShouldPublishAtomicWrite(false, true, 4096, 4096));
    EXPECT("short write refuses",    !ShouldPublishAtomicWrite(true, true, 4095, 4096));

    // The direction that actually happens on a full volume: the stream reports fine
    // (the failure surfaced only at flush on some CRTs) but the bytes are not there.
    EXPECT("silent truncation refuses", !ShouldPublishAtomicWrite(true, true, 0, 4096));

    // trunc failed / something appended — longer is as wrong as shorter.
    EXPECT("over-long file refuses", !ShouldPublishAtomicWrite(true, true, 4097, 4096));

    // An unmeasurable file is not a verified one. This is the branch a "size == 0
    // means we could not read it" shortcut would get backwards.
    EXPECT("unknown size refuses",   !ShouldPublishAtomicWrite(true, false, 0, 4096));
    EXPECT("unknown size refuses even when the numbers would match",
           !ShouldPublishAtomicWrite(true, false, 4096, 4096));

    // Large files must not overflow or wrap the comparison — the cache grows one
    // record per game, forever.
    EXPECT("large exact match publishes",
           ShouldPublishAtomicWrite(true, true, 3000000000ull, 3000000000ull));
    EXPECT("large off-by-one refuses",
           !ShouldPublishAtomicWrite(true, true, 3000000000ull, 3000000001ull));

    // Degenerate but well-defined: no special-casing of an empty document.
    EXPECT("empty exact match publishes", ShouldPublishAtomicWrite(true, true, 0, 0));
}

// ----- Radar: SizeOf + NameOf + parsers ---------------------------------

static void Test_ValueScan_DataTypeSizes() {
    EXPECT("SizeOf Int8 = 1",   Radar::SizeOf(Radar::DataType::Int8)   == 1);
    EXPECT("SizeOf Int16 = 2",  Radar::SizeOf(Radar::DataType::Int16)  == 2);
    EXPECT("SizeOf Int32 = 4",  Radar::SizeOf(Radar::DataType::Int32)  == 4);
    EXPECT("SizeOf Int64 = 8",  Radar::SizeOf(Radar::DataType::Int64)  == 8);
    EXPECT("SizeOf UInt8 = 1",  Radar::SizeOf(Radar::DataType::UInt8)  == 1);
    EXPECT("SizeOf UInt16 = 2", Radar::SizeOf(Radar::DataType::UInt16) == 2);
    EXPECT("SizeOf UInt32 = 4", Radar::SizeOf(Radar::DataType::UInt32) == 4);
    EXPECT("SizeOf UInt64 = 8", Radar::SizeOf(Radar::DataType::UInt64) == 8);
    EXPECT("SizeOf Float = 4",  Radar::SizeOf(Radar::DataType::Float)  == 4);
    EXPECT("SizeOf Double = 8", Radar::SizeOf(Radar::DataType::Double) == 8);
    EXPECT("SizeOf Bool = 1",   Radar::SizeOf(Radar::DataType::Bool)   == 1);
    // Phase 2A: string types — variable length, signalled by SizeOf = 0.
    EXPECT("SizeOf FString = 0", Radar::SizeOf(Radar::DataType::FString) == 0);
    EXPECT("SizeOf FName = 0",   Radar::SizeOf(Radar::DataType::FName)   == 0);
    EXPECT("SizeOf FText = 0",   Radar::SizeOf(Radar::DataType::FText)   == 0);
    // Phase 2B: vector types — VARIABLE width, signalled by SizeOf = 0 like the
    // string + multi-numeric families. UE5's LWC made "FVector" 3xdouble (24B)
    // while "FVector3f" stayed 3xfloat (12B), so no single constant is true;
    // the per-field width lives in FieldDescriptor::vectorWidth. (audit #5 AB5)
    EXPECT("SizeOf FVector = 0 (variable)",    Radar::SizeOf(Radar::DataType::FVector)    == 0);
    EXPECT("SizeOf FRotator = 0 (variable)",   Radar::SizeOf(Radar::DataType::FRotator)   == 0);
    EXPECT("SizeOf FTransform = 0 (variable)", Radar::SizeOf(Radar::DataType::FTransform) == 0);
    // Multi-numeric meta types — variable width, signalled by SizeOf = 0.
    EXPECT("SizeOf NumericNoByte = 0", Radar::SizeOf(Radar::DataType::NumericNoByte) == 0);
    EXPECT("SizeOf NumericAll = 0",    Radar::SizeOf(Radar::DataType::NumericAll)    == 0);
}

static void Test_ValueScan_ParseDataTypeRoundTrip() {
    using DT = Radar::DataType;
    DT got;
    EXPECT("parse Int32",   Radar::TryParseDataType("Int32",  got) && got == DT::Int32);
    EXPECT("parse Float",   Radar::TryParseDataType("Float",  got) && got == DT::Float);
    EXPECT("parse Bool",    Radar::TryParseDataType("Bool",   got) && got == DT::Bool);
    EXPECT("parse UInt64",  Radar::TryParseDataType("UInt64", got) && got == DT::UInt64);
    // Phase 2 DataTypes — locks the wire-protocol shape.
    EXPECT("parse FString", Radar::TryParseDataType("FString", got) && got == DT::FString);
    EXPECT("parse FName",   Radar::TryParseDataType("FName",   got) && got == DT::FName);
    EXPECT("parse FText",   Radar::TryParseDataType("FText",   got) && got == DT::FText);
    EXPECT("parse FVector",  Radar::TryParseDataType("FVector",  got) && got == DT::FVector);
    EXPECT("parse FRotator", Radar::TryParseDataType("FRotator", got) && got == DT::FRotator);
    EXPECT("parse FTransform", Radar::TryParseDataType("FTransform", got) && got == DT::FTransform);
    // Multi-numeric meta DataTypes — locks the wire-protocol shape.
    EXPECT("parse NumericNoByte", Radar::TryParseDataType("NumericNoByte", got) && got == DT::NumericNoByte);
    EXPECT("parse NumericAll",    Radar::TryParseDataType("NumericAll",    got) && got == DT::NumericAll);
    EXPECT("parse rejects unknown", !Radar::TryParseDataType("TArray<Int32>", got));
    EXPECT("parse rejects empty",   !Radar::TryParseDataType("",              got));
}

static void Test_ValueScan_ScanTypePartitioning() {
    using ST = Radar::ScanType;
    EXPECT("Exact is first-scan",      Radar::IsFirstScanType(ST::Exact));
    EXPECT("Bigger is first-scan",     Radar::IsFirstScanType(ST::Bigger));
    EXPECT("Smaller is first-scan",    Radar::IsFirstScanType(ST::Smaller));
    EXPECT("Between is first-scan",    Radar::IsFirstScanType(ST::Between));
    EXPECT("Changed is prev-value",    Radar::IsPrevValueScanType(ST::Changed));
    EXPECT("Unchanged is prev-value",  Radar::IsPrevValueScanType(ST::Unchanged));
    EXPECT("Increased is prev-value",  Radar::IsPrevValueScanType(ST::Increased));
    EXPECT("Decreased is prev-value",  Radar::IsPrevValueScanType(ST::Decreased));
    // No overlap between first-scan and prev-value partitions:
    EXPECT("Exact is NOT prev-value",  !Radar::IsPrevValueScanType(ST::Exact));
    EXPECT("Changed is NOT first-scan", !Radar::IsFirstScanType(ST::Changed));
    // Phase 2A: substring predicates are first-scan eligible.
    EXPECT("Contains is first-scan",   Radar::IsFirstScanType(ST::Contains));
    EXPECT("StartsWith is first-scan", Radar::IsFirstScanType(ST::StartsWith));
    EXPECT("EndsWith is first-scan",   Radar::IsFirstScanType(ST::EndsWith));
    EXPECT("Contains is NOT prev-value",   !Radar::IsPrevValueScanType(ST::Contains));
}

static void Test_ValueScan_TypeFamilyPredicates() {
    using DT = Radar::DataType;
    using ST = Radar::ScanType;
    // IsStringDataType: only the three string types.
    EXPECT("FString isString",  Radar::IsStringDataType(DT::FString));
    EXPECT("FName isString",    Radar::IsStringDataType(DT::FName));
    EXPECT("FText isString",    Radar::IsStringDataType(DT::FText));
    EXPECT("Int32 NOT isString", !Radar::IsStringDataType(DT::Int32));
    EXPECT("Float NOT isString", !Radar::IsStringDataType(DT::Float));
    EXPECT("FVector NOT isString", !Radar::IsStringDataType(DT::FVector));
    // IsVectorDataType: only the three vector types.
    EXPECT("FVector isVector",    Radar::IsVectorDataType(DT::FVector));
    EXPECT("FRotator isVector",   Radar::IsVectorDataType(DT::FRotator));
    EXPECT("FTransform isVector", Radar::IsVectorDataType(DT::FTransform));
    EXPECT("Int32 NOT isVector",  !Radar::IsVectorDataType(DT::Int32));
    EXPECT("FString NOT isVector", !Radar::IsVectorDataType(DT::FString));
    // IsSubstringScanType: only Contains/StartsWith/EndsWith.
    EXPECT("Contains is substring",   Radar::IsSubstringScanType(ST::Contains));
    EXPECT("StartsWith is substring", Radar::IsSubstringScanType(ST::StartsWith));
    EXPECT("EndsWith is substring",   Radar::IsSubstringScanType(ST::EndsWith));
    EXPECT("Exact NOT substring",   !Radar::IsSubstringScanType(ST::Exact));
    EXPECT("Bigger NOT substring",  !Radar::IsSubstringScanType(ST::Bigger));
    EXPECT("Changed NOT substring", !Radar::IsSubstringScanType(ST::Changed));
}

static void Test_ValueScan_IsScanTypeValidFor() {
    using DT = Radar::DataType;
    using ST = Radar::ScanType;
    // Numerics: substring predicates reject, ordering predicates accept.
    EXPECT("Int32 Exact valid",    Radar::IsScanTypeValidFor(DT::Int32, ST::Exact));
    EXPECT("Int32 Bigger valid",   Radar::IsScanTypeValidFor(DT::Int32, ST::Bigger));
    EXPECT("Int32 Changed valid",  Radar::IsScanTypeValidFor(DT::Int32, ST::Changed));
    EXPECT("Int32 Contains REJ",   !Radar::IsScanTypeValidFor(DT::Int32, ST::Contains));
    EXPECT("Int32 StartsWith REJ", !Radar::IsScanTypeValidFor(DT::Int32, ST::StartsWith));
    EXPECT("Float EndsWith REJ",   !Radar::IsScanTypeValidFor(DT::Float, ST::EndsWith));
    // Strings: ordering predicates reject, substring + Exact + Changed/Unchanged accept.
    EXPECT("FString Exact valid",     Radar::IsScanTypeValidFor(DT::FString, ST::Exact));
    EXPECT("FString Contains valid",  Radar::IsScanTypeValidFor(DT::FString, ST::Contains));
    EXPECT("FString StartsWith valid", Radar::IsScanTypeValidFor(DT::FString, ST::StartsWith));
    EXPECT("FName EndsWith valid",    Radar::IsScanTypeValidFor(DT::FName,   ST::EndsWith));
    EXPECT("FText Changed valid",     Radar::IsScanTypeValidFor(DT::FText,   ST::Changed));
    EXPECT("FText Unchanged valid",   Radar::IsScanTypeValidFor(DT::FText,   ST::Unchanged));
    EXPECT("FString Bigger REJ",   !Radar::IsScanTypeValidFor(DT::FString, ST::Bigger));
    EXPECT("FString Smaller REJ",  !Radar::IsScanTypeValidFor(DT::FString, ST::Smaller));
    EXPECT("FString Between REJ",  !Radar::IsScanTypeValidFor(DT::FString, ST::Between));
    EXPECT("FString Increased REJ", !Radar::IsScanTypeValidFor(DT::FString, ST::Increased));
    EXPECT("FString Decreased REJ", !Radar::IsScanTypeValidFor(DT::FString, ST::Decreased));
    // Vectors: substring predicates reject; ordering predicates accept.
    EXPECT("FVector Exact valid",    Radar::IsScanTypeValidFor(DT::FVector, ST::Exact));
    EXPECT("FVector Bigger valid",   Radar::IsScanTypeValidFor(DT::FVector, ST::Bigger));
    EXPECT("FVector Between valid",  Radar::IsScanTypeValidFor(DT::FVector, ST::Between));
    EXPECT("FVector Changed valid",  Radar::IsScanTypeValidFor(DT::FVector, ST::Changed));
    EXPECT("FRotator Contains REJ", !Radar::IsScanTypeValidFor(DT::FRotator, ST::Contains));
    // Multi-numeric meta type behaves like a numeric: ordering accept,
    // substring reject.
    EXPECT("NumericNoByte Exact valid",   Radar::IsScanTypeValidFor(DT::NumericNoByte, ST::Exact));
    EXPECT("NumericNoByte Bigger valid",  Radar::IsScanTypeValidFor(DT::NumericNoByte, ST::Bigger));
    EXPECT("NumericNoByte Between valid", Radar::IsScanTypeValidFor(DT::NumericNoByte, ST::Between));
    EXPECT("NumericNoByte Changed valid", Radar::IsScanTypeValidFor(DT::NumericNoByte, ST::Changed));
    EXPECT("NumericNoByte Contains REJ", !Radar::IsScanTypeValidFor(DT::NumericNoByte, ST::Contains));
    EXPECT("NumericAll Exact valid",   Radar::IsScanTypeValidFor(DT::NumericAll, ST::Exact));
    EXPECT("NumericAll Bigger valid",  Radar::IsScanTypeValidFor(DT::NumericAll, ST::Bigger));
    EXPECT("NumericAll Contains REJ", !Radar::IsScanTypeValidFor(DT::NumericAll, ST::Contains));
}

// ----- Radar: multi-numeric meta type -----------------------------------

static void Test_ValueScan_MultiNumericMembers() {
    using DT = Radar::DataType;
    EXPECT("NumericNoByte is multi-numeric",  Radar::IsMultiNumericDataType(DT::NumericNoByte));
    EXPECT("NumericAll is multi-numeric",     Radar::IsMultiNumericDataType(DT::NumericAll));
    EXPECT("Int32 is NOT multi-numeric",     !Radar::IsMultiNumericDataType(DT::Int32));
    EXPECT("FString is NOT multi-numeric",   !Radar::IsMultiNumericDataType(DT::FString));

    const auto& m = Radar::MultiNumericMembers(DT::NumericNoByte);
    EXPECT("NumericNoByte has 8 members", m.size() == 8);
    auto has = [](const std::vector<DT>& v, DT d) {
        for (auto x : v) if (x == d) return true;
        return false;
    };
    EXPECT("members include Int16",  has(m, DT::Int16));
    EXPECT("members include UInt16", has(m, DT::UInt16));
    EXPECT("members include Int32",  has(m, DT::Int32));
    EXPECT("members include UInt32", has(m, DT::UInt32));
    EXPECT("members include Int64",  has(m, DT::Int64));
    EXPECT("members include UInt64", has(m, DT::UInt64));
    EXPECT("members include Float",  has(m, DT::Float));
    EXPECT("members include Double", has(m, DT::Double));
    // The "no byte" contract: no 1-byte or bool members.
    EXPECT("members exclude Int8",  !has(m, DT::Int8));
    EXPECT("members exclude UInt8", !has(m, DT::UInt8));
    EXPECT("members exclude Bool",  !has(m, DT::Bool));

    // NumericAll = NumericNoByte + { Int8, UInt8 } (10 members), still no Bool.
    const auto& ma = Radar::MultiNumericMembers(DT::NumericAll);
    EXPECT("NumericAll has 10 members", ma.size() == 10);
    EXPECT("NumericAll includes Int8",  has(ma, DT::Int8));
    EXPECT("NumericAll includes UInt8", has(ma, DT::UInt8));
    EXPECT("NumericAll includes Int32", has(ma, DT::Int32));
    EXPECT("NumericAll includes Double",has(ma, DT::Double));
    EXPECT("NumericAll excludes Bool", !has(ma, DT::Bool));
    // Non-meta types yield an empty member set.
    EXPECT("Int32 members empty", Radar::MultiNumericMembers(DT::Int32).empty());
}

static void Test_ValueScan_DataTypeFromPropertyTypeName() {
    using DT = Radar::DataType;
    DT got;
    EXPECT("IntProperty -> Int32",     Radar::TryDataTypeFromPropertyTypeName("IntProperty", got)    && got == DT::Int32);
    EXPECT("Int16Property -> Int16",   Radar::TryDataTypeFromPropertyTypeName("Int16Property", got)  && got == DT::Int16);
    EXPECT("Int64Property -> Int64",   Radar::TryDataTypeFromPropertyTypeName("Int64Property", got)  && got == DT::Int64);
    EXPECT("UInt16Property -> UInt16", Radar::TryDataTypeFromPropertyTypeName("UInt16Property", got) && got == DT::UInt16);
    EXPECT("UInt32Property -> UInt32", Radar::TryDataTypeFromPropertyTypeName("UInt32Property", got) && got == DT::UInt32);
    EXPECT("UInt64Property -> UInt64", Radar::TryDataTypeFromPropertyTypeName("UInt64Property", got) && got == DT::UInt64);
    EXPECT("FloatProperty -> Float",   Radar::TryDataTypeFromPropertyTypeName("FloatProperty", got)  && got == DT::Float);
    EXPECT("DoubleProperty -> Double", Radar::TryDataTypeFromPropertyTypeName("DoubleProperty", got) && got == DT::Double);
    // 1-byte families resolve too (NumericAll includes them; NumericNoByte
    // simply never feeds them in via its PropertyTypeNames union).
    EXPECT("ByteProperty -> UInt8",  Radar::TryDataTypeFromPropertyTypeName("ByteProperty", got) && got == DT::UInt8);
    EXPECT("Int8Property -> Int8",   Radar::TryDataTypeFromPropertyTypeName("Int8Property", got)  && got == DT::Int8);
    // audit #5 AB14 — EnumProperty resolves to UInt8 (enums are 1-byte in the
    // overwhelming majority). Before the fix this returned false and enum-backed
    // state fields were invisible to every meta value scan.
    EXPECT("EnumProperty -> UInt8",  Radar::TryDataTypeFromPropertyTypeName("EnumProperty", got) && got == DT::UInt8);
    // Bool + non-numeric still reject.
    EXPECT("BoolProperty rejected",  !Radar::TryDataTypeFromPropertyTypeName("BoolProperty", got));
    EXPECT("StrProperty rejected",   !Radar::TryDataTypeFromPropertyTypeName("StrProperty", got));
    EXPECT("StructProperty rejected",!Radar::TryDataTypeFromPropertyTypeName("StructProperty", got));

    // PropertyTypeNames(meta) MUST be exactly the set that
    // TryDataTypeFromPropertyTypeName resolves — otherwise a field could
    // be accepted into the scan index yet fail per-field resolution.
    auto allResolve = [](const std::vector<std::string>& names) {
        for (const auto& n : names) {
            DT d;
            if (!Radar::TryDataTypeFromPropertyTypeName(n, d)) return false;
        }
        return true;
    };
    auto hasName = [](const std::vector<std::string>& v, const char* n) {
        for (const auto& s : v) if (s == n) return true;
        return false;
    };
    const auto& noByteNames = Radar::PropertyTypeNames(DT::NumericNoByte);
    EXPECT("NumericNoByte has 8 property names", noByteNames.size() == 8);
    EXPECT("every NumericNoByte property name resolves", allResolve(noByteNames));
    // audit #5 AB14 — EnumProperty joined NumericAll (11 now, was 10) but NOT
    // NumericNoByte: it resolves to UInt8, a 1-byte width NumericNoByte excludes.
    const auto& allNames = Radar::PropertyTypeNames(DT::NumericAll);
    EXPECT("NumericAll has 11 property names", allNames.size() == 11);
    EXPECT("every NumericAll property name resolves", allResolve(allNames));
    EXPECT("NumericAll includes EnumProperty", hasName(allNames, "EnumProperty"));
    EXPECT("NumericNoByte excludes EnumProperty", !hasName(noByteNames, "EnumProperty"));
}

static void Test_ValueScan_PropertyTypeNameOf_Inverse() {
    using DT = Radar::DataType;
    // PropertyTypeNameOf must be the exact inverse of
    // TryDataTypeFromPropertyTypeName for every concrete numeric width — the
    // Native-C scan stamps raw descriptors with PropertyTypeNameOf(dt) and refine
    // re-resolves them via TryDataTypeFromPropertyTypeName, so a mismatch would
    // silently drop native candidates on the first Next Scan.
    const DT widths[] = {
        DT::Int8, DT::UInt8, DT::Int16, DT::UInt16, DT::Int32,
        DT::UInt32, DT::Int64, DT::UInt64, DT::Float, DT::Double,
    };
    for (DT w : widths) {
        const char* name = Radar::PropertyTypeNameOf(w);
        EXPECT("PropertyTypeNameOf non-empty", name[0] != '\0');
        DT back;
        EXPECT("PropertyTypeNameOf round-trips",
               Radar::TryDataTypeFromPropertyTypeName(name, back) && back == w);
    }
    // Non-numeric / meta / bool have no property-type name.
    EXPECT("Bool -> empty",         Radar::PropertyTypeNameOf(DT::Bool)[0]        == '\0');
    EXPECT("FString -> empty",      Radar::PropertyTypeNameOf(DT::FString)[0]     == '\0');
    EXPECT("FVector -> empty",      Radar::PropertyTypeNameOf(DT::FVector)[0]     == '\0');
    EXPECT("NumericNoByte -> empty",Radar::PropertyTypeNameOf(DT::NumericNoByte)[0] == '\0');
}

// Helper: does the set contain an entry for `dt`, and (optionally) does
// it decode to the expected scalar value?
// ============================================================
// Macht::PatternScanRange — audit #5 MA2
// ============================================================
// The batch scanner guarded its scalar loops with `regionSize - patLen` as an
// unsigned max-start, checked only against the batch's SHORTEST pattern. For any
// pattern LONGER than the region that subtraction underflows to ~1.8e19 and the
// `pos <= maxStart` loop walks off the end — in a function with no SEH.
//
// The bound now comes from one shared predicate, so the underflow is structurally
// impossible in all four scan loops rather than guarded in three of them.
static void Test_Macht_PatternScanRange() {
    size_t maxStart = 0;

    // Ordinary case: 10-byte pattern in a 100-byte region starts anywhere in 0..90.
    maxStart = 12345;
    EXPECT("MA2 ordinary fit is allowed", Macht::PatternScanRange(100, 10, maxStart));
    EXPECT("MA2 ordinary maxStart is regionSize - patLen", maxStart == 90);

    // Exactly-fits: one valid start position, offset 0.
    maxStart = 12345;
    EXPECT("MA2 exact fit is allowed", Macht::PatternScanRange(10, 10, maxStart));
    EXPECT("MA2 exact fit maxStart is 0", maxStart == 0);

    // THE DEFECT: a pattern longer than the region must be refused, not wrapped.
    maxStart = 12345;
    EXPECT("MA2 over-long pattern is refused", !Macht::PatternScanRange(100, 200, maxStart));
    EXPECT("MA2 refusal leaves maxStart untouched (no underflow leaks out)",
           maxStart == 12345);

    // One byte too long — the boundary the old arithmetic turned into SIZE_MAX.
    EXPECT("MA2 one-byte-too-long is refused", !Macht::PatternScanRange(10, 11, maxStart));

    // Empty pattern: the old arithmetic gave maxStart == regionSize, i.e. a
    // reported match one past the end of the region.
    EXPECT("MA2 empty pattern is refused", !Macht::PatternScanRange(100, 0, maxStart));

    // The batch scenario in full: the whole-batch guard compares the region against
    // the SHORTEST pattern, so it clears — and the long one must still be refused
    // individually. This is the combination that made it a latent crash.
    const size_t regionSize = 100, shortPat = 60, longPat = 200;
    const size_t minPatLen = shortPat < longPat ? shortPat : longPat;
    EXPECT("MA2 batch guard passes on the shortest pattern", regionSize >= minPatLen);
    EXPECT("MA2 ...but the short pattern is still individually allowed",
           Macht::PatternScanRange(regionSize, shortPat, maxStart));
    EXPECT("MA2 ...and the long one is individually refused",
           !Macht::PatternScanRange(regionSize, longPat, maxStart));
}

static void Test_ValueScan_BuildNumericTargets() {
    using DT = Radar::DataType;

    // "100" fits every member width.
    {
        Radar::NumericTargetSet ts;
        EXPECT("BuildNumericTargets(100) ok", Radar::BuildNumericTargets(DT::NumericNoByte, "100", ts));
        EXPECT("100 fits all 8 widths", ts.entries.size() == 8);
        const uint8_t* i32 = ts.Find(DT::Int32);
        EXPECT("100 Int32 entry present", i32 != nullptr);
        if (i32) { int32_t v; std::memcpy(&v, i32, 4); EXPECT("100 Int32 decodes", v == 100); }
        const uint8_t* f = ts.Find(DT::Float);
        EXPECT("100 Float entry present", f != nullptr);
        if (f) { float v; std::memcpy(&v, f, 4); EXPECT("100 Float decodes", v == 100.0f); }
    }
    // "70000" overflows 16-bit widths — no Int16/UInt16 entries.
    {
        Radar::NumericTargetSet ts;
        EXPECT("BuildNumericTargets(70000) ok", Radar::BuildNumericTargets(DT::NumericNoByte, "70000", ts));
        EXPECT("70000 has no Int16",  ts.Find(DT::Int16)  == nullptr);
        EXPECT("70000 has no UInt16", ts.Find(DT::UInt16) == nullptr);
        EXPECT("70000 has Int32",     ts.Find(DT::Int32)  != nullptr);
        EXPECT("70000 has UInt32",    ts.Find(DT::UInt32) != nullptr);
        EXPECT("70000 has Float",     ts.Find(DT::Float)  != nullptr);
    }
    // "-5" can't be unsigned — signed + float members only.
    {
        Radar::NumericTargetSet ts;
        EXPECT("BuildNumericTargets(-5) ok", Radar::BuildNumericTargets(DT::NumericNoByte, "-5", ts));
        EXPECT("-5 has Int16",   ts.Find(DT::Int16)  != nullptr);
        EXPECT("-5 has Int32",   ts.Find(DT::Int32)  != nullptr);
        EXPECT("-5 has Float",   ts.Find(DT::Float)  != nullptr);
        EXPECT("-5 has NO UInt16", ts.Find(DT::UInt16) == nullptr);
        EXPECT("-5 has NO UInt32", ts.Find(DT::UInt32) == nullptr);
        EXPECT("-5 has NO UInt64", ts.Find(DT::UInt64) == nullptr);
    }
    // ---- audit #5 AB4 -------------------------------------------------------
    // "Does the target fit this width" is right for Exact and WRONG for the
    // ordered predicates. Every Int16 field is smaller than 70000, but 70000 has
    // no int16 encoding, so the old set emitted no Int16 entry and the engines
    // skipped every 2-byte field. The four directions are NOT symmetric.
    {
        using ST = Radar::ScanType;
        using Fit = Radar::NumericTargetSet::Fit;

        // Smaller: a target ABOVE the width's max matches every value of it.
        Radar::NumericTargetSet lo;
        EXPECT("AB4 Smaller(70000) ok",
               Radar::BuildNumericTargets(DT::NumericNoByte, "70000", lo,
                                          Radar::RoundMode::Round, ST::Smaller));
        const auto* i16 = lo.FindEntry(DT::Int16);
        EXPECT("AB4 Smaller(70000) keeps Int16", i16 != nullptr);
        EXPECT("AB4 Smaller(70000) Int16 is AlwaysTrue",
               i16 && i16->fit == Fit::AlwaysTrue);
        EXPECT("AB4 Smaller(70000) UInt16 is AlwaysTrue",
               lo.FindEntry(DT::UInt16) &&
               lo.FindEntry(DT::UInt16)->fit == Fit::AlwaysTrue);
        // An in-range width is untouched — still a real encoded target.
        EXPECT("AB4 Smaller(70000) Int32 still Encoded",
               lo.FindEntry(DT::Int32) &&
               lo.FindEntry(DT::Int32)->fit == Fit::Encoded);
        // Find() must NOT hand out the zeroed buffer of an AlwaysTrue entry:
        // comparing against 0 would be wrong in a quieter way than the bug.
        EXPECT("AB4 Find() hides an AlwaysTrue entry", lo.Find(DT::Int16) == nullptr);

        // Bigger with the SAME value is the opposite answer: nothing 16-bit
        // exceeds 70000, so skipping is correct and must be preserved.
        Radar::NumericTargetSet hi;
        Radar::BuildNumericTargets(DT::NumericNoByte, "70000", hi,
                                   Radar::RoundMode::Round, ST::Bigger);
        EXPECT("AB4 Bigger(70000) still drops Int16", hi.FindEntry(DT::Int16) == nullptr);
        EXPECT("AB4 Bigger(70000) still drops UInt16", hi.FindEntry(DT::UInt16) == nullptr);

        // The sign leak the finding did not mention: a negative string suppresses
        // the unsigned parse entirely, so `Bigger -5` used to drop every unsigned
        // width. Every unsigned value IS bigger than -5.
        Radar::NumericTargetSet neg;
        Radar::BuildNumericTargets(DT::NumericNoByte, "-5", neg,
                                   Radar::RoundMode::Round, ST::Bigger);
        EXPECT("AB4 Bigger(-5) keeps UInt16 as AlwaysTrue",
               neg.FindEntry(DT::UInt16) &&
               neg.FindEntry(DT::UInt16)->fit == Fit::AlwaysTrue);
        EXPECT("AB4 Bigger(-5) keeps UInt32 as AlwaysTrue",
               neg.FindEntry(DT::UInt32) &&
               neg.FindEntry(DT::UInt32)->fit == Fit::AlwaysTrue);
        // ...and Smaller(-5) is the opposite: no unsigned value is below -5.
        Radar::NumericTargetSet neg2;
        Radar::BuildNumericTargets(DT::NumericNoByte, "-5", neg2,
                                   Radar::RoundMode::Round, ST::Smaller);
        EXPECT("AB4 Smaller(-5) still drops UInt16", neg2.FindEntry(DT::UInt16) == nullptr);

        // Exact is the default and must be byte-identical to before: no verdicts.
        Radar::NumericTargetSet ex;
        Radar::BuildNumericTargets(DT::NumericNoByte, "70000", ex);
        EXPECT("AB4 Exact(70000) unchanged: no Int16", ex.FindEntry(DT::Int16) == nullptr);
        EXPECT("AB4 Exact(70000) unchanged: Int32 Encoded",
               ex.FindEntry(DT::Int32) &&
               ex.FindEntry(DT::Int32)->fit == Fit::Encoded);

        // The predicate honours the verdict without reading a target, and the
        // boundary value that CLAMPING would have dropped is kept: an int16 of
        // exactly 32767 is smaller than 70000.
        int16_t edge = 32767;
        EXPECT("AB4 predicate: 32767 < 70000 via AlwaysTrue",
               Radar::ComparePredicate(DT::Int16, ST::Smaller,
                                       reinterpret_cast<const uint8_t*>(&edge),
                                       lo.FindEntry(DT::Int16)));
        // A null entry stays false rather than matching everything.
        EXPECT("AB4 predicate: null entry is false",
               !Radar::ComparePredicate(DT::Int16, ST::Bigger,
                                        reinterpret_cast<const uint8_t*>(&edge),
                                        hi.FindEntry(DT::Int16)));
        // An Encoded entry still compares normally through the same overload.
        int32_t v32 = 69999;
        EXPECT("AB4 predicate: Encoded path still compares",
               Radar::ComparePredicate(DT::Int32, ST::Smaller,
                                       reinterpret_cast<const uint8_t*>(&v32),
                                       lo.FindEntry(DT::Int32)));
    }

    // "100.5" is non-integral. Float/Double keep the exact 100.5; integer widths
    // are COERCED to the displayed integer via the rounding mode (build 1672) —
    // default Round: round(100.5)=101 (half-away). So it now fits all 8 widths.
    {
        Radar::NumericTargetSet ts;
        EXPECT("BuildNumericTargets(100.5) ok", Radar::BuildNumericTargets(DT::NumericNoByte, "100.5", ts));
        EXPECT("100.5 (Round) fits all 8 widths", ts.entries.size() == 8);
        const uint8_t* d = ts.Find(DT::Double);
        if (d) { double v; std::memcpy(&v, d, 8); EXPECT("100.5 Double keeps exact 100.5", v == 100.5); }
        const uint8_t* i32 = ts.Find(DT::Int32);
        EXPECT("100.5 Int32 entry present (coerced)", i32 != nullptr);
        if (i32) { int32_t v; std::memcpy(&v, i32, 4); EXPECT("100.5 Round -> Int32 == 101", v == 101); }
    }
    // Rounding-mode integer coercion: "10.9" reduces to 11 (Round), 10 (Trunc),
    // 11 (Ceil) for integer widths; Float always keeps 10.9. (The 10~11 / 11~11
    // Between-on-integer behavior the user asked about.)
    {
        Radar::NumericTargetSet tr, tt, tc;
        Radar::BuildNumericTargets(DT::NumericNoByte, "10.9", tr, Radar::RoundMode::Round);
        Radar::BuildNumericTargets(DT::NumericNoByte, "10.9", tt, Radar::RoundMode::Trunc);
        Radar::BuildNumericTargets(DT::NumericNoByte, "10.9", tc, Radar::RoundMode::Ceil);
        const uint8_t* r = tr.Find(DT::Int32);
        const uint8_t* t = tt.Find(DT::Int32);
        const uint8_t* c = tc.Find(DT::Int32);
        EXPECT("10.9 coerced entries present", r && t && c);
        if (r) { int32_t v; std::memcpy(&v, r, 4); EXPECT("10.9 Round -> 11", v == 11); }
        if (t) { int32_t v; std::memcpy(&v, t, 4); EXPECT("10.9 Trunc -> 10", v == 10); }
        if (c) { int32_t v; std::memcpy(&v, c, 4); EXPECT("10.9 Ceil  -> 11", v == 11); }
        const uint8_t* f = tr.Find(DT::Float);
        if (f) { float v; std::memcpy(&v, f, 4); EXPECT("10.9 Float keeps 10.9", v == 10.9f); }
    }
    // Hex "0x10" → integer widths only (no float reinterpret).
    {
        Radar::NumericTargetSet ts;
        EXPECT("BuildNumericTargets(0x10) ok", Radar::BuildNumericTargets(DT::NumericNoByte, "0x10", ts));
        EXPECT("0x10 has Int32",    ts.Find(DT::Int32) != nullptr);
        EXPECT("0x10 has NO Float", ts.Find(DT::Float) == nullptr);
        const uint8_t* i = ts.Find(DT::Int32);
        if (i) { int32_t v; std::memcpy(&v, i, 4); EXPECT("0x10 Int32 == 16", v == 16); }
    }
    // audit #5 AB15 — a LEADING ZERO must mean DECIMAL for every width, not octal
    // for the integers and decimal for the floats. Before the fix, base-0 parsing
    // read "010" as octal 8 for Int32/Int64/... while std::stod read it as 10.0 for
    // Float/Double, so one meta scan gave the same string two different numbers.
    {
        Radar::NumericTargetSet ts;
        EXPECT("BuildNumericTargets(010) ok", Radar::BuildNumericTargets(DT::NumericAll, "010", ts));
        const uint8_t* i32 = ts.Find(DT::Int32);
        EXPECT("010 Int32 present", i32 != nullptr);
        if (i32) { int32_t v; std::memcpy(&v, i32, 4); EXPECT("010 Int32 == 10 (decimal, not octal 8)", v == 10); }
        const uint8_t* u8 = ts.Find(DT::UInt8);
        if (u8) EXPECT("010 UInt8 == 10", *u8 == 10);
        const uint8_t* f = ts.Find(DT::Float);
        EXPECT("010 Float present", f != nullptr);
        if (f) { float v; std::memcpy(&v, f, 4); EXPECT("010 Float == 10.0", v == 10.0f); }
        // The integer and float interpretations now AGREE — the whole point.
        if (i32 && f) {
            int32_t iv; std::memcpy(&iv, i32, 4);
            float   fv; std::memcpy(&fv, f, 4);
            EXPECT("010: integer and float widths agree", static_cast<float>(iv) == fv);
        }
    }
    // ...and a 0x prefix still parses as hex even with a sign in front.
    {
        Radar::NumericTargetSet ts;
        EXPECT("BuildNumericTargets(-0x10) ok", Radar::BuildNumericTargets(DT::NumericNoByte, "-0x10", ts));
        const uint8_t* i32 = ts.Find(DT::Int32);
        EXPECT("-0x10 Int32 present", i32 != nullptr);
        if (i32) { int32_t v; std::memcpy(&v, i32, 4); EXPECT("-0x10 Int32 == -16", v == -16); }
        EXPECT("-0x10 has NO Float (hex is integer-only)", ts.Find(DT::Float) == nullptr);
    }
    // Empty / whitespace / garbage → false, no entries.
    {
        Radar::NumericTargetSet ts;
        EXPECT("BuildNumericTargets('') false", !Radar::BuildNumericTargets(DT::NumericNoByte, "", ts));
        EXPECT("empty leaves no entries", ts.entries.empty());
        EXPECT("BuildNumericTargets('  ') false", !Radar::BuildNumericTargets(DT::NumericNoByte, "   ", ts));
        EXPECT("BuildNumericTargets('abc') false", !Radar::BuildNumericTargets(DT::NumericNoByte, "abc", ts));
    }
    // Non-meta data type yields no targets.
    {
        Radar::NumericTargetSet ts;
        EXPECT("BuildNumericTargets(Int32 meta) false", !Radar::BuildNumericTargets(DT::Int32, "100", ts));
    }
    // NumericAll: "100" fits all 10 widths (incl. Int8/UInt8).
    {
        Radar::NumericTargetSet ts;
        EXPECT("BuildNumericTargets(All,100) ok", Radar::BuildNumericTargets(DT::NumericAll, "100", ts));
        EXPECT("All 100 fits 10 widths", ts.entries.size() == 10);
        EXPECT("All 100 has Int8",  ts.Find(DT::Int8)  != nullptr);
        EXPECT("All 100 has UInt8", ts.Find(DT::UInt8) != nullptr);
        const uint8_t* i8 = ts.Find(DT::Int8);
        if (i8) { int8_t v; std::memcpy(&v, i8, 1); EXPECT("All 100 Int8 decodes", v == 100); }
    }
    // NumericAll: "300" overflows 8-bit widths — no Int8/UInt8 entries.
    {
        Radar::NumericTargetSet ts;
        EXPECT("BuildNumericTargets(All,300) ok", Radar::BuildNumericTargets(DT::NumericAll, "300", ts));
        EXPECT("All 300 has NO Int8",  ts.Find(DT::Int8)  == nullptr);
        EXPECT("All 300 has NO UInt8", ts.Find(DT::UInt8) == nullptr);
        EXPECT("All 300 has Int16",    ts.Find(DT::Int16) != nullptr);
        EXPECT("All 300 has UInt16",   ts.Find(DT::UInt16)!= nullptr);
    }
    // NumericAll: "-5" → Int8 yes (signed), UInt8 no (negative).
    {
        Radar::NumericTargetSet ts;
        EXPECT("BuildNumericTargets(All,-5) ok", Radar::BuildNumericTargets(DT::NumericAll, "-5", ts));
        EXPECT("All -5 has Int8",     ts.Find(DT::Int8)  != nullptr);
        EXPECT("All -5 has NO UInt8", ts.Find(DT::UInt8) == nullptr);
    }
    // NumericAll: "200" → UInt8 yes (<=255), Int8 no (>127).
    {
        Radar::NumericTargetSet ts;
        EXPECT("BuildNumericTargets(All,200) ok", Radar::BuildNumericTargets(DT::NumericAll, "200", ts));
        EXPECT("All 200 has UInt8",   ts.Find(DT::UInt8) != nullptr);
        EXPECT("All 200 has NO Int8", ts.Find(DT::Int8)  == nullptr);
    }
}

// ----- Radar: ComparePredicate per DataType -----------------------------
//
// Each test seeds two byte buffers as if they were the raw memory of a
// real UProperty, then exercises every ScanType predicate. Prev-value
// scan types reuse `target` as the candidate's stored prevValue, so the
// same buffer layout works for both flavours.

template <typename T>
static void WriteLE(uint8_t buf[8], T val) {
    std::memset(buf, 0, 8);
    std::memcpy(buf, &val, sizeof(T));
}

static void Test_ValueScan_Predicate_Int32() {
    using DT = Radar::DataType;
    using ST = Radar::ScanType;
    uint8_t cur[8], tgt[8], tgt2[8];
    WriteLE<int32_t>(cur, 100);
    WriteLE<int32_t>(tgt, 100);
    EXPECT("Int32 Exact (100==100)",      Radar::ComparePredicate(DT::Int32, ST::Exact,   cur, tgt));
    WriteLE<int32_t>(tgt, 50);
    EXPECT("Int32 Bigger (100>50)",       Radar::ComparePredicate(DT::Int32, ST::Bigger,  cur, tgt));
    EXPECT("Int32 Smaller false",        !Radar::ComparePredicate(DT::Int32, ST::Smaller, cur, tgt));
    WriteLE<int32_t>(tgt, 200);
    EXPECT("Int32 Smaller (100<200)",     Radar::ComparePredicate(DT::Int32, ST::Smaller, cur, tgt));
    WriteLE<int32_t>(tgt, 50);
    WriteLE<int32_t>(tgt2, 150);
    EXPECT("Int32 Between (100 in [50,150])", Radar::ComparePredicate(DT::Int32, ST::Between, cur, tgt, tgt2));
    WriteLE<int32_t>(tgt, 150);
    WriteLE<int32_t>(tgt2, 200);
    EXPECT("Int32 Between rejects (100 not in [150,200])",
           !Radar::ComparePredicate(DT::Int32, ST::Between, cur, tgt, tgt2));

    // Changed / Unchanged compare against prev (passed as `target`)
    WriteLE<int32_t>(tgt, 100);
    EXPECT("Int32 Unchanged (100==prev100)",  Radar::ComparePredicate(DT::Int32, ST::Unchanged, cur, tgt));
    EXPECT("Int32 Changed rejects same",     !Radar::ComparePredicate(DT::Int32, ST::Changed,   cur, tgt));
    WriteLE<int32_t>(tgt, 99);
    EXPECT("Int32 Changed (100!=prev99)",     Radar::ComparePredicate(DT::Int32, ST::Changed,   cur, tgt));
    EXPECT("Int32 Increased (100>prev99)",    Radar::ComparePredicate(DT::Int32, ST::Increased, cur, tgt));
    WriteLE<int32_t>(tgt, 101);
    EXPECT("Int32 Decreased (100<prev101)",   Radar::ComparePredicate(DT::Int32, ST::Decreased, cur, tgt));
}

static void Test_ValueScan_Predicate_Int8Negative() {
    // Regression for sign extension: Int8 must compare as signed even
    // when the raw byte is 0xFF (which would be 255 as unsigned).
    using DT = Radar::DataType;
    using ST = Radar::ScanType;
    uint8_t cur[8] = {}, tgt[8] = {};
    int8_t minusOne = -1;
    int8_t zero = 0;
    std::memcpy(cur, &minusOne, 1);
    std::memcpy(tgt, &zero, 1);
    EXPECT("Int8 (-1 < 0) Smaller",   Radar::ComparePredicate(DT::Int8, ST::Smaller, cur, tgt));
    EXPECT("Int8 (-1 < 0) Bigger NO", !Radar::ComparePredicate(DT::Int8, ST::Bigger,  cur, tgt));
}

static void Test_ValueScan_Predicate_Float() {
    using DT = Radar::DataType;
    using ST = Radar::ScanType;
    uint8_t cur[8], tgt[8];
    WriteLE<float>(cur, 3.14f);
    WriteLE<float>(tgt, 3.14f);
    EXPECT("Float Exact (3.14==3.14)",  Radar::ComparePredicate(DT::Float, ST::Exact,  cur, tgt));
    WriteLE<float>(tgt, 1.0f);
    EXPECT("Float Bigger (3.14>1)",     Radar::ComparePredicate(DT::Float, ST::Bigger, cur, tgt));
    WriteLE<float>(cur, -2.5f);
    WriteLE<float>(tgt, -1.0f);
    EXPECT("Float Smaller (-2.5<-1)",   Radar::ComparePredicate(DT::Float, ST::Smaller, cur, tgt));
}

static void Test_ValueScan_Predicate_Double() {
    using DT = Radar::DataType;
    using ST = Radar::ScanType;
    uint8_t cur[8], tgt[8];
    WriteLE<double>(cur, 1.0 / 3.0);
    WriteLE<double>(tgt, 1.0 / 3.0);
    EXPECT("Double Exact (1/3==1/3, fractional literal)", Radar::ComparePredicate(DT::Double, ST::Exact, cur, tgt));
    // Increased (default Round) compares DISPLAYED values: cur 2.6 -> 3 > prev 0.
    WriteLE<double>(cur, 2.6);
    WriteLE<double>(tgt, 0.0);
    EXPECT("Double Increased (2.6 displays as 3 > prev 0)", Radar::ComparePredicate(DT::Double, ST::Increased, cur, tgt));
}

static void Test_ValueScan_Predicate_Bool() {
    using DT = Radar::DataType;
    using ST = Radar::ScanType;
    uint8_t cur[8] = { 1 }, tgt[8] = { 1 };
    EXPECT("Bool true==true Exact",       Radar::ComparePredicate(DT::Bool, ST::Exact, cur, tgt));
    tgt[0] = 0;
    EXPECT("Bool true!=false Changed",    Radar::ComparePredicate(DT::Bool, ST::Changed, cur, tgt));
    EXPECT("Bool true!=false Unchanged NO", !Radar::ComparePredicate(DT::Bool, ST::Unchanged, cur, tgt));
}

static void Test_ValueScan_Predicate_UInt64_RangeBoundary() {
    using DT = Radar::DataType;
    using ST = Radar::ScanType;
    uint8_t cur[8], tgt[8];
    // Values that would be NEGATIVE if mis-read as signed: ensures
    // unsigned path is taken for UInt64.
    WriteLE<uint64_t>(cur, 0xFFFFFFFFFFFFFFFFULL);
    WriteLE<uint64_t>(tgt, 0x8000000000000000ULL);
    EXPECT("UInt64 (~0 > 0x8000...) Bigger", Radar::ComparePredicate(DT::UInt64, ST::Bigger, cur, tgt));
    EXPECT("UInt64 (~0 < 0x8000...) Smaller NO",
           !Radar::ComparePredicate(DT::UInt64, ST::Smaller, cur, tgt));
}

// ----- Radar: SessionManager lifecycle ----------------------------------

// ----- Radar: Float/Double rounding mode (displayed-integer scan) -------
//
// Build 1672 replaced the implicit half-away round + the ± tolerance band with
// an explicit RoundMode (Round/Trunc/Ceil): every Float/Double operand is
// reduced to the integer the game DISPLAYS before comparing. GAS use case: a
// stored 513.36 shows as 513 (Round/Trunc) — scanning "513" Exact finds it; a
// stored 99.6 shows as 100 (Round/Ceil). A FRACTIONAL target keeps exact-literal
// compare (precise intent). Prev-value predicates compare reduced cur vs reduced
// prev. Integer types are reduce-invariant (the mode is a no-op there).

static void Test_ValueScan_FloatRoundMode_Exact() {
    using DT = Radar::DataType;
    using ST = Radar::ScanType;
    using RM = Radar::RoundMode;
    uint8_t cur[8], tgt[8];

    // --- Round (default, half-away-from-zero). target=338 (whole) ---
    WriteLE<float>(tgt, 338.0f);
    WriteLE<float>(cur, 337.5f);   // round -> 338
    EXPECT("Float Exact Round matches 337.5 vs 338 (rounds to 338)",
           Radar::ComparePredicate(DT::Float, ST::Exact, cur, tgt, nullptr, RM::Round));
    WriteLE<float>(cur, 338.49f);  // round -> 338
    EXPECT("Float Exact Round matches 338.49 vs 338",
           Radar::ComparePredicate(DT::Float, ST::Exact, cur, tgt, nullptr, RM::Round));
    WriteLE<float>(cur, 337.4f);   // round -> 337
    EXPECT("Float Exact Round rejects 337.4 vs 338 (rounds to 337)",
           !Radar::ComparePredicate(DT::Float, ST::Exact, cur, tgt, nullptr, RM::Round));

    // --- Trunc (toward zero): only the integer part counts ---
    WriteLE<float>(cur, 338.9f);   // trunc -> 338
    EXPECT("Float Exact Trunc matches 338.9 vs 338 (truncates to 338)",
           Radar::ComparePredicate(DT::Float, ST::Exact, cur, tgt, nullptr, RM::Trunc));
    WriteLE<float>(cur, 339.0f);   // trunc -> 339
    EXPECT("Float Exact Trunc rejects 339.0 vs 338",
           !Radar::ComparePredicate(DT::Float, ST::Exact, cur, tgt, nullptr, RM::Trunc));
    WriteLE<float>(cur, 337.5f);   // trunc -> 337 (NOT 338, unlike Round)
    EXPECT("Float Exact Trunc rejects 337.5 vs 338 (truncates to 337)",
           !Radar::ComparePredicate(DT::Float, ST::Exact, cur, tgt, nullptr, RM::Trunc));

    // --- Ceil (toward +inf): the displayed 99.6 -> 100 case ---
    WriteLE<float>(cur, 337.1f);   // ceil -> 338
    EXPECT("Float Exact Ceil matches 337.1 vs 338 (ceils to 338)",
           Radar::ComparePredicate(DT::Float, ST::Exact, cur, tgt, nullptr, RM::Ceil));
    WriteLE<float>(cur, 338.0f);   // ceil -> 338
    EXPECT("Float Exact Ceil matches 338.0 vs 338",
           Radar::ComparePredicate(DT::Float, ST::Exact, cur, tgt, nullptr, RM::Ceil));
    WriteLE<float>(cur, 338.5f);   // ceil -> 339
    EXPECT("Float Exact Ceil rejects 338.5 vs 338 (ceils to 339)",
           !Radar::ComparePredicate(DT::Float, ST::Exact, cur, tgt, nullptr, RM::Ceil));

    // --- A NON-whole target keeps exact-literal equality (no reduce) in ANY mode ---
    WriteLE<float>(tgt, 338.25f);
    WriteLE<float>(cur, 338.0f);
    EXPECT("Float Exact rejects 338.0 vs 338.25 (non-whole target, no reduce)",
           !Radar::ComparePredicate(DT::Float, ST::Exact, cur, tgt, nullptr, RM::Round));
    WriteLE<float>(cur, 338.25f);
    EXPECT("Float Exact matches 338.25 vs 338.25 (exact literal, any mode)",
           Radar::ComparePredicate(DT::Float, ST::Exact, cur, tgt, nullptr, RM::Ceil));

    // --- DOUBLE shares the same path (IsFloatType covers Float + Double) ---
    WriteLE<double>(tgt, 513.0);
    WriteLE<double>(cur, 513.3599853516);   // round/trunc -> 513, ceil -> 514
    EXPECT("Double Exact Round matches 513.36 vs 513",
           Radar::ComparePredicate(DT::Double, ST::Exact, cur, tgt, nullptr, RM::Round));
    EXPECT("Double Exact Trunc matches 513.36 vs 513",
           Radar::ComparePredicate(DT::Double, ST::Exact, cur, tgt, nullptr, RM::Trunc));
    EXPECT("Double Exact Ceil rejects 513.36 vs 513 (ceils to 514)",
           !Radar::ComparePredicate(DT::Double, ST::Exact, cur, tgt, nullptr, RM::Ceil));
    WriteLE<double>(tgt, 514.0);
    EXPECT("Double Exact Ceil matches 513.36 vs 514",
           Radar::ComparePredicate(DT::Double, ST::Exact, cur, tgt, nullptr, RM::Ceil));
}

static void Test_ValueScan_FloatRoundMode_Ordered() {
    using DT = Radar::DataType;
    using ST = Radar::ScanType;
    using RM = Radar::RoundMode;
    uint8_t cur[8], tgt[8];

    // Bigger: reduce(cur) > whole target.
    WriteLE<float>(tgt, 338.0f);
    WriteLE<float>(cur, 339.0f);
    EXPECT("Float Bigger Round (round 339 > 338)",
           Radar::ComparePredicate(DT::Float, ST::Bigger, cur, tgt, nullptr, RM::Round));
    WriteLE<float>(cur, 338.4f);   // round -> 338, not > 338
    EXPECT("Float Bigger Round rejects 338.4 (rounds to 338)",
           !Radar::ComparePredicate(DT::Float, ST::Bigger, cur, tgt, nullptr, RM::Round));
    WriteLE<float>(cur, 338.6f);   // round -> 339 > 338
    EXPECT("Float Bigger Round 338.6 (rounds to 339)",
           Radar::ComparePredicate(DT::Float, ST::Bigger, cur, tgt, nullptr, RM::Round));

    // Smaller: reduce(cur) < whole target.
    WriteLE<float>(cur, 337.4f);   // round -> 337 < 338
    EXPECT("Float Smaller Round (round 337 < 338)",
           Radar::ComparePredicate(DT::Float, ST::Smaller, cur, tgt, nullptr, RM::Round));

    // Fractional target: literal compare (no reduce) in any mode.
    WriteLE<float>(tgt, 338.25f);
    WriteLE<float>(cur, 338.3f);
    EXPECT("Float Bigger fractional target literal (338.3 > 338.25)",
           Radar::ComparePredicate(DT::Float, ST::Bigger, cur, tgt, nullptr, RM::Round));
}

static void Test_ValueScan_FloatRoundMode_PrevValue() {
    using DT = Radar::DataType;
    using ST = Radar::ScanType;
    using RM = Radar::RoundMode;
    uint8_t cur[8], prev[8];

    // Unchanged/Changed compare the DISPLAYED (reduced) values, not raw bytes.
    WriteLE<float>(prev, 100.0f);
    WriteLE<float>(cur,  100.3f);   // round -> 100 == round(100.0)=100
    EXPECT("Float Unchanged Round (100.3 displays as 100 == 100)",
           Radar::ComparePredicate(DT::Float, ST::Unchanged, cur, prev, nullptr, RM::Round));
    EXPECT("Float Changed Round rejects 100.3 (same display)",
           !Radar::ComparePredicate(DT::Float, ST::Changed, cur, prev, nullptr, RM::Round));
    WriteLE<float>(cur, 100.6f);    // round -> 101 != 100
    EXPECT("Float Changed Round (100.6 displays as 101 != 100)",
           Radar::ComparePredicate(DT::Float, ST::Changed, cur, prev, nullptr, RM::Round));

    // Increased: reduce(cur) > reduce(prev).
    WriteLE<float>(prev, 50.0f);
    WriteLE<float>(cur,  50.6f);    // round -> 51 > 50
    EXPECT("Float Increased Round (51 > 50)",
           Radar::ComparePredicate(DT::Float, ST::Increased, cur, prev, nullptr, RM::Round));
    WriteLE<float>(cur, 50.4f);     // round -> 50, not > 50
    EXPECT("Float Increased Round rejects 50.4 (rounds to 50)",
           !Radar::ComparePredicate(DT::Float, ST::Increased, cur, prev, nullptr, RM::Round));

    // Trunc lens: 100.9 and 100.2 both display as 100 -> Unchanged.
    WriteLE<float>(prev, 100.9f);
    WriteLE<float>(cur,  100.2f);
    EXPECT("Float Unchanged Trunc (100.9 & 100.2 both truncate to 100)",
           Radar::ComparePredicate(DT::Float, ST::Unchanged, cur, prev, nullptr, RM::Trunc));
}

static void Test_ValueScan_FloatRoundMode_Between() {
    using DT = Radar::DataType;
    using ST = Radar::ScanType;
    using RM = Radar::RoundMode;
    uint8_t cur[8], lo[8], hi[8];

    // Whole bounds [10,20]: reduce(cur) within [10,20].
    WriteLE<float>(lo, 10.0f);
    WriteLE<float>(hi, 20.0f);
    WriteLE<float>(cur, 9.8f);    // round -> 10, in range
    EXPECT("Float Between Round includes 9.8 (rounds to 10)",
           Radar::ComparePredicate(DT::Float, ST::Between, cur, lo, hi, RM::Round));
    WriteLE<float>(cur, 20.3f);   // round -> 20, in range
    EXPECT("Float Between Round includes 20.3 (rounds to 20)",
           Radar::ComparePredicate(DT::Float, ST::Between, cur, lo, hi, RM::Round));
    WriteLE<float>(cur, 20.6f);   // round -> 21, out
    EXPECT("Float Between Round rejects 20.6 (rounds to 21)",
           !Radar::ComparePredicate(DT::Float, ST::Between, cur, lo, hi, RM::Round));
    WriteLE<float>(cur, 9.4f);    // round -> 9, out
    EXPECT("Float Between Round rejects 9.4 (rounds to 9)",
           !Radar::ComparePredicate(DT::Float, ST::Between, cur, lo, hi, RM::Round));

    // Fractional bounds [10.5, 20.5]: literal range (no reduce).
    WriteLE<float>(lo, 10.5f);
    WriteLE<float>(hi, 20.5f);
    WriteLE<float>(cur, 10.5f);
    EXPECT("Float Between fractional includes 10.5 (literal)",
           Radar::ComparePredicate(DT::Float, ST::Between, cur, lo, hi, RM::Round));
    WriteLE<float>(cur, 9.8f);
    EXPECT("Float Between fractional rejects 9.8 (literal, < 10.5)",
           !Radar::ComparePredicate(DT::Float, ST::Between, cur, lo, hi, RM::Round));

    // Reversed bounds normalize (parity with C# Min/Max): Between 20..10 matches 15.
    WriteLE<float>(lo, 20.0f);   // lo > hi on purpose
    WriteLE<float>(hi, 10.0f);
    WriteLE<float>(cur, 15.0f);
    EXPECT("Float Between reversed (20..10) still includes 15",
           Radar::ComparePredicate(DT::Float, ST::Between, cur, lo, hi, RM::Round));
    // Same for an integer field (ApplyOrdered path).
    uint8_t ilo[8], ihi[8], icur[8];
    WriteLE<int32_t>(ilo, 100);
    WriteLE<int32_t>(ihi, 50);
    WriteLE<int32_t>(icur, 75);
    EXPECT("Int32 Between reversed (100..50) still includes 75",
           Radar::ComparePredicate(DT::Int32, ST::Between, icur, ilo, ihi, RM::Round));
}

static void Test_ValueScan_RoundMode_IntegerNoOp() {
    using DT = Radar::DataType;
    using ST = Radar::ScanType;
    using RM = Radar::RoundMode;
    uint8_t cur[8], tgt[8];
    // The rounding mode is a no-op for integer ComparePredicate — an integer
    // reduces to itself, so Int32 Exact stays strict regardless of the mode.
    // (Fractional-target coercion happens upstream in BuildNumericTargets /
    // ParseValueBytes, not here.)
    WriteLE<int32_t>(cur, 99);
    WriteLE<int32_t>(tgt, 100);
    EXPECT("Int32 Exact Ceil rejects 99 vs 100 (mode no-op)",
           !Radar::ComparePredicate(DT::Int32, ST::Exact, cur, tgt, nullptr, RM::Ceil));
    WriteLE<int32_t>(cur, 100);
    EXPECT("Int32 Exact Ceil accepts 100 vs 100",
           Radar::ComparePredicate(DT::Int32, ST::Exact, cur, tgt, nullptr, RM::Ceil));

    // Same for UInt64
    WriteLE<uint64_t>(cur, 999);
    WriteLE<uint64_t>(tgt, 1000);
    EXPECT("UInt64 Exact Trunc still rejects 999 vs 1000",
           !Radar::ComparePredicate(DT::UInt64, ST::Exact, cur, tgt, nullptr, RM::Trunc));
}

// ----- Radar: CompareStringPredicate (Phase 2A) -------------------------

static void Test_ValueScan_StringPredicate_Exact() {
    using ST = Radar::ScanType;
    EXPECT("Exact case-insensitive match",
           Radar::CompareStringPredicate(ST::Exact, "PlayerName", "playername", false));
    EXPECT("Exact case-sensitive rejects",
           !Radar::CompareStringPredicate(ST::Exact, "PlayerName", "playername", true));
    EXPECT("Exact case-sensitive accepts",
           Radar::CompareStringPredicate(ST::Exact, "PlayerName", "PlayerName", true));
    EXPECT("Exact rejects different length",
           !Radar::CompareStringPredicate(ST::Exact, "PlayerName", "Player", false));
    EXPECT("Exact accepts empty == empty",
           Radar::CompareStringPredicate(ST::Exact, "", "", false));
}

static void Test_ValueScan_StringPredicate_Substring() {
    using ST = Radar::ScanType;
    EXPECT("Contains case-insensitive: 'Health' in 'PlayerHealth'",
           Radar::CompareStringPredicate(ST::Contains, "PlayerHealth", "Health", false));
    EXPECT("Contains case-insensitive lowercase: 'health' in 'PlayerHealth'",
           Radar::CompareStringPredicate(ST::Contains, "PlayerHealth", "health", false));
    EXPECT("Contains case-sensitive rejects case mismatch",
           !Radar::CompareStringPredicate(ST::Contains, "PlayerHealth", "health", true));
    EXPECT("Contains rejects missing substring",
           !Radar::CompareStringPredicate(ST::Contains, "PlayerHealth", "Mana", false));
    EXPECT("Contains empty needle always true",
           Radar::CompareStringPredicate(ST::Contains, "PlayerHealth", "", false));
    EXPECT("Contains rejects longer-than-haystack",
           !Radar::CompareStringPredicate(ST::Contains, "Hi", "Player", false));

    EXPECT("StartsWith: 'Player' starts 'PlayerHealth'",
           Radar::CompareStringPredicate(ST::StartsWith, "PlayerHealth", "Player", false));
    EXPECT("StartsWith rejects suffix",
           !Radar::CompareStringPredicate(ST::StartsWith, "PlayerHealth", "Health", false));
    EXPECT("StartsWith case-insensitive 'player'",
           Radar::CompareStringPredicate(ST::StartsWith, "PlayerHealth", "player", false));

    EXPECT("EndsWith: 'Health' ends 'PlayerHealth'",
           Radar::CompareStringPredicate(ST::EndsWith, "PlayerHealth", "Health", false));
    EXPECT("EndsWith rejects prefix",
           !Radar::CompareStringPredicate(ST::EndsWith, "PlayerHealth", "Player", false));
    EXPECT("EndsWith case-sensitive rejects",
           !Radar::CompareStringPredicate(ST::EndsWith, "PlayerHealth", "HEALTH", true));
}

static void Test_ValueScan_StringPredicate_PrevValue() {
    using ST = Radar::ScanType;
    EXPECT("Changed: different strings",
           Radar::CompareStringPredicate(ST::Changed, "NewName", "OldName", false));
    EXPECT("Changed rejects identical",
           !Radar::CompareStringPredicate(ST::Changed, "Same", "Same", false));
    EXPECT("Unchanged: identical strings",
           Radar::CompareStringPredicate(ST::Unchanged, "Same", "Same", false));
    EXPECT("Unchanged: case-insensitive identical",
           Radar::CompareStringPredicate(ST::Unchanged, "SAME", "same", false));
    EXPECT("Unchanged case-sensitive rejects case-diff",
           !Radar::CompareStringPredicate(ST::Unchanged, "SAME", "same", true));
}

static void Test_ValueScan_StringPredicate_RejectsNumericOrdering() {
    using ST = Radar::ScanType;
    // Numeric predicates have no meaning for strings — return false
    // unconditionally so the pipe handler's IsScanTypeValidFor guard
    // is belt-and-braces.
    EXPECT("Bigger rejects",
           !Radar::CompareStringPredicate(ST::Bigger, "B", "A", false));
    EXPECT("Smaller rejects",
           !Radar::CompareStringPredicate(ST::Smaller, "A", "B", false));
    EXPECT("Between rejects",
           !Radar::CompareStringPredicate(ST::Between, "M", "A", false));
    EXPECT("Increased rejects",
           !Radar::CompareStringPredicate(ST::Increased, "B", "A", false));
    EXPECT("Decreased rejects",
           !Radar::CompareStringPredicate(ST::Decreased, "A", "B", false));
}

// ----- Radar: CompareVectorPredicate (Phase 2B) -------------------------

// CompareVectorPredicate now takes DECODED triples (see Radar.h) — the raw
// bytes no longer share a width between the field and the target once LWC is
// in play, so the decode happens at the call site.
static void WriteVector(double v[3], double x, double y, double z) {
    v[0] = x; v[1] = y; v[2] = z;
}

// Raw source bytes as a game would hold them, at each of the two real widths.
static void WriteVectorBytesFloat(uint8_t buf[12], float x, float y, float z) {
    std::memcpy(buf + 0, &x, 4);
    std::memcpy(buf + 4, &y, 4);
    std::memcpy(buf + 8, &z, 4);
}
static void WriteVectorBytesDouble(uint8_t buf[24], double x, double y, double z) {
    std::memcpy(buf +  0, &x, 8);
    std::memcpy(buf +  8, &y, 8);
    std::memcpy(buf + 16, &z, 8);
}

static void Test_ValueScan_VectorPredicate_Exact() {
    using ST = Radar::ScanType;
    using RM = Radar::RoundMode;
    double cur[3], tgt[3];
    WriteVector(cur, 100.0, 200.0, 300.0);
    WriteVector(tgt, 100.0, 200.0, 300.0);
    EXPECT("Vec Exact all match", Radar::CompareVectorPredicate(ST::Exact, cur, tgt));
    // Per-axis displayed-integer reduce: X=100.6 rounds to 101, so a whole target
    // axis of 100 no longer matches that axis.
    WriteVector(cur, 100.6, 200.0, 300.0);
    EXPECT("Vec Exact rejects axis that rounds away (100.6 -> 101 vs 100)",
           !Radar::CompareVectorPredicate(ST::Exact, cur, tgt, nullptr, RM::Round));
    // X=100.4 rounds back to 100 -> all axes match the displayed integer.
    WriteVector(cur, 100.4, 200.0, 300.0);
    EXPECT("Vec Exact Round accepts axis that rounds back (100.4 -> 100)",
           Radar::CompareVectorPredicate(ST::Exact, cur, tgt, nullptr, RM::Round));
}

static void Test_ValueScan_VectorPredicate_Ordering() {
    using ST = Radar::ScanType;
    double cur[3], tgt[3];
    WriteVector(cur, 10.0, 20.0, 30.0);
    WriteVector(tgt, 5.0,  10.0, 15.0);
    EXPECT("Vec Bigger: all axes above", Radar::CompareVectorPredicate(ST::Bigger, cur, tgt));
    EXPECT("Vec Smaller (10,20,30) NOT < (5,10,15)",
           !Radar::CompareVectorPredicate(ST::Smaller, cur, tgt));

    // One axis equal kills Bigger
    WriteVector(cur, 10.0, 10.0, 30.0);
    EXPECT("Vec Bigger fails when one axis equals",
           !Radar::CompareVectorPredicate(ST::Bigger, cur, tgt));
}

static void Test_ValueScan_VectorPredicate_Between() {
    using ST = Radar::ScanType;
    double cur[3], lo[3], hi[3];
    WriteVector(lo, 0.0,   0.0,   0.0);
    WriteVector(hi, 100.0, 100.0, 100.0);
    WriteVector(cur, 50.0, 50.0, 50.0);
    EXPECT("Vec Between: (50,50,50) in [(0,0,0),(100,100,100)]",
           Radar::CompareVectorPredicate(ST::Between, cur, lo, hi));
    WriteVector(cur, 50.0, 150.0, 50.0);
    EXPECT("Vec Between rejects Y outside",
           !Radar::CompareVectorPredicate(ST::Between, cur, lo, hi));
}

static void Test_ValueScan_VectorPredicate_PrevValue() {
    using ST = Radar::ScanType;
    double cur[3], prev[3];
    WriteVector(prev, 100.0, 100.0, 100.0);

    // Movement on any single axis = Changed
    WriteVector(cur, 100.0, 100.0, 105.0);
    EXPECT("Vec Changed: one axis moved",
           Radar::CompareVectorPredicate(ST::Changed, cur, prev));
    EXPECT("Vec Unchanged rejects when axis differs",
           !Radar::CompareVectorPredicate(ST::Unchanged, cur, prev));

    // No movement
    WriteVector(cur, 100.0, 100.0, 100.0);
    EXPECT("Vec Unchanged accepts identical",
           Radar::CompareVectorPredicate(ST::Unchanged, cur, prev));
    EXPECT("Vec Changed rejects identical",
           !Radar::CompareVectorPredicate(ST::Changed, cur, prev));

    // Increased: ANY axis moved up beyond tolerance
    WriteVector(cur, 100.0, 100.0, 110.0);
    EXPECT("Vec Increased: Z went up",
           Radar::CompareVectorPredicate(ST::Increased, cur, prev));
    // All went down — Increased rejects
    WriteVector(cur, 90.0, 90.0, 90.0);
    EXPECT("Vec Increased rejects when all axes down",
           !Radar::CompareVectorPredicate(ST::Increased, cur, prev));
    EXPECT("Vec Decreased: all axes down",
           Radar::CompareVectorPredicate(ST::Decreased, cur, prev));
}

static void Test_ValueScan_VectorPredicate_RejectsSubstring() {
    using ST = Radar::ScanType;
    double cur[3], tgt[3];
    WriteVector(cur, 0,0,0); WriteVector(tgt, 0,0,0);
    EXPECT("Vec Contains rejects",
           !Radar::CompareVectorPredicate(ST::Contains, cur, tgt));
    EXPECT("Vec StartsWith rejects",
           !Radar::CompareVectorPredicate(ST::StartsWith, cur, tgt));
    EXPECT("Vec EndsWith rejects",
           !Radar::CompareVectorPredicate(ST::EndsWith, cur, tgt));
}

// ----- Radar: LWC vector width (audit #5 AB3 / AB5) ------------------------

static void Test_ValueScan_VectorWidth_Accepted() {
    EXPECT("width 12 (3xfloat, UE4 / FVector3f) supported",
           Radar::IsSupportedVectorWidth(Radar::VECTOR_WIDTH_FLOAT));
    EXPECT("width 24 (3xdouble, UE5 LWC FVector) supported",
           Radar::IsSupportedVectorWidth(Radar::VECTOR_WIDTH_DOUBLE));
    // A struct that passes the NAME gate but is not an X/Y/Z triple must be
    // refused, not read at a guessed width: FVector2D is 8 (float) or 16
    // (double), FVector4 is 16 or 32.
    EXPECT("width 8 (FVector2D float) refused",  !Radar::IsSupportedVectorWidth(8));
    EXPECT("width 16 (FVector2D LWC / FVector4 float) refused",
           !Radar::IsSupportedVectorWidth(16));
    EXPECT("width 32 (FVector4 LWC) refused",    !Radar::IsSupportedVectorWidth(32));
    EXPECT("width 0 (unresolved) refused",       !Radar::IsSupportedVectorWidth(0));
    EXPECT("negative width refused",             !Radar::IsSupportedVectorWidth(-12));
}

static void Test_ValueScan_DecodeVectorBytes() {
    uint8_t f12[12] = {};
    WriteVectorBytesFloat(f12, 1.5f, -2.5f, 3.25f);
    double out[3] = { 9, 9, 9 };
    EXPECT("decode 12B succeeds", Radar::DecodeVectorBytes(f12, 12, out));
    EXPECT("decode 12B X", out[0] == 1.5);
    EXPECT("decode 12B Y", out[1] == -2.5);
    EXPECT("decode 12B Z", out[2] == 3.25);

    uint8_t d24[24] = {};
    WriteVectorBytesDouble(d24, 1.5, -2.5, 3.25);
    double out2[3] = { 9, 9, 9 };
    EXPECT("decode 24B succeeds", Radar::DecodeVectorBytes(d24, 24, out2));
    EXPECT("decode 24B X", out2[0] == 1.5);
    EXPECT("decode 24B Y", out2[1] == -2.5);
    EXPECT("decode 24B Z", out2[2] == 3.25);

    // Unsupported widths refuse AND leave the caller's buffer untouched, so a
    // caller that ignores the bool cannot silently compare stale values.
    double untouched[3] = { 7, 7, 7 };
    EXPECT("decode width 16 refuses", !Radar::DecodeVectorBytes(d24, 16, untouched));
    EXPECT("decode width 16 leaves out untouched",
           untouched[0] == 7 && untouched[1] == 7 && untouched[2] == 7);
    EXPECT("decode width 0 refuses", !Radar::DecodeVectorBytes(d24, 0, untouched));
    EXPECT("decode null src refuses", !Radar::DecodeVectorBytes(nullptr, 24, untouched));
}

static void Test_ValueScan_StoreVectorCanonical() {
    double v[3] = { 10.25, -20.5, 30.75 };
    uint8_t canon[Radar::VECTOR_CANON_BYTES] = {};
    Radar::StoreVectorCanonical(v, canon);
    EXPECT("canonical form is 24 bytes", Radar::VECTOR_CANON_BYTES == 24);
    double back[3] = {};
    EXPECT("canonical round-trips through the 24B decoder",
           Radar::DecodeVectorBytes(canon, Radar::VECTOR_CANON_BYTES, back));
    EXPECT("canonical round-trip X", back[0] == 10.25);
    EXPECT("canonical round-trip Y", back[1] == -20.5);
    EXPECT("canonical round-trip Z", back[2] == 30.75);
    // The candidate snapshot buffer has to hold one.
    EXPECT("Candidate::prevValue fits a canonical vector",
           sizeof(Radar::Candidate::prevValue) >= Radar::VECTOR_CANON_BYTES);
}

// THE negative control for AB5. A UE5 LWC field holds three DOUBLES; the scan
// used to read a flat 12 bytes and reinterpret them as three floats, which is
// the low half of X plus the low half of Y — a bit pattern that can never equal
// the value the user typed. Reverting DecodeVectorBytes to a fixed 12-byte
// float read turns the second half of this test red.
static void Test_ValueScan_LwcVectorIsNotReadAsFloats() {
    using ST = Radar::ScanType;
    // A plausible world position in a UE5 (LWC, double) game.
    const double X = 1024.5, Y = -2048.25, Z = 512.125;
    uint8_t lwcField[24] = {};
    WriteVectorBytesDouble(lwcField, X, Y, Z);

    double target[3];
    WriteVector(target, X, Y, Z);

    // Read at the field's REAL reflected width -> exact match.
    double cur[3] = {};
    EXPECT("LWC field decodes at its reflected width 24",
           Radar::DecodeVectorBytes(lwcField, Radar::VECTOR_WIDTH_DOUBLE, cur));
    EXPECT("LWC field Exact-matches the typed target",
           Radar::CompareVectorPredicate(ST::Exact, cur, target));

    // The pre-fix behaviour: same bytes, read as 3 floats. Whatever that
    // produces, it is not the value the user typed.
    double asFloats[3] = {};
    EXPECT("same bytes also decode at width 12 (the old, wrong path)",
           Radar::DecodeVectorBytes(lwcField, Radar::VECTOR_WIDTH_FLOAT, asFloats));
    EXPECT("reading an LWC field as 3 floats does NOT match the target",
           !Radar::CompareVectorPredicate(ST::Exact, asFloats, target));

    // And the converse still holds: a genuine 12-byte float field must match.
    uint8_t floatField[12] = {};
    WriteVectorBytesFloat(floatField, 1024.5f, -2048.25f, 512.125f);
    double curF[3] = {};
    EXPECT("float field decodes at its reflected width 12",
           Radar::DecodeVectorBytes(floatField, Radar::VECTOR_WIDTH_FLOAT, curF));
    EXPECT("float field Exact-matches the same typed target",
           Radar::CompareVectorPredicate(ST::Exact, curF, target));
}

// ----- VectorStructNames (Phase 2B) ----------------------------------------

static void Test_ValueScan_VectorStructNames() {
    using DT = Radar::DataType;
    const auto& vec = Radar::VectorStructNames(DT::FVector);
    EXPECT("FVector accepts 'Vector'",
           std::find(vec.begin(), vec.end(), std::string("Vector")) != vec.end());
    EXPECT("FVector accepts 'Vector3f'",
           std::find(vec.begin(), vec.end(), std::string("Vector3f")) != vec.end());
    const auto& rot = Radar::VectorStructNames(DT::FRotator);
    EXPECT("FRotator accepts 'Rotator'",
           std::find(rot.begin(), rot.end(), std::string("Rotator")) != rot.end());
    // FTransform is intentionally empty until per-version Translation
    // offset detection ships.
    const auto& xfm = Radar::VectorStructNames(DT::FTransform);
    EXPECT("FTransform empty (deferred)", xfm.empty());
    // Non-vector dt returns empty.
    const auto& none = Radar::VectorStructNames(DT::Int32);
    EXPECT("Int32 has no vector struct names", none.empty());
}

static void Test_ValueScan_SessionLifecycle() {
    using namespace Radar;
    auto& mgr = SessionManager::Instance();

    // Seed two candidates.
    std::vector<Candidate> seed;
    seed.resize(2);
    seed[0].addr = 0x1000;
    WriteLE<int32_t>(seed[0].prevValue, 100);
    seed[1].addr = 0x2000;
    WriteLE<int32_t>(seed[1].prevValue, 200);

    // Shared metadata pools the candidates index into (V3-A). Both
    // candidates reference one descriptor + one instance to exercise the
    // dedup path.
    std::vector<FieldDescriptor> descriptors(1);
    descriptors[0].className     = "AActor";
    descriptors[0].fieldName     = "Health";
    descriptors[0].fieldType     = "IntProperty";
    std::vector<InstanceRecord> instances(1);
    instances[0].instanceAddr = 0x4000;
    instances[0].instanceName = "Actor_0";

    uint64_t sid = mgr.Begin(DataType::Int32, std::move(seed),
                             std::move(descriptors), std::move(instances));
    EXPECT("Begin returns non-zero session id", sid != 0);

    bool viewed = mgr.ViewWith(sid, [&](const Session& sess) {
        EXPECT("ViewWith sees correct dataType", sess.dt == DataType::Int32);
        EXPECT("ViewWith sees 2 candidates",     sess.candidates.size() == 2);
        EXPECT("ViewWith preserves descriptor pool", sess.descriptors.size() == 1);
        EXPECT("ViewWith preserves instance pool",   sess.instances.size() == 1);
        EXPECT("Descriptor field name interned",
               sess.descriptors[0].fieldName == "Health");
    });
    EXPECT("ViewWith returns true for live session", viewed);

    // RefineWith may mutate the candidates vector.
    bool refined = mgr.RefineWith(sid, [](Session& sess) {
        sess.candidates.pop_back();  // drop one
    });
    EXPECT("RefineWith returns true for live session", refined);

    size_t remaining = 0;
    mgr.ViewWith(sid, [&](const Session& sess) {
        remaining = sess.candidates.size();
    });
    EXPECT("Refine pruned candidate count", remaining == 1);

    EXPECT("End returns true on first call",  mgr.End(sid));
    EXPECT("End returns false on second call",!mgr.End(sid));

    // Lookups on a missing session id return false WITHOUT invoking
    // the callback -- caller maps to wire error "session_not_found".
    bool callbackRan = false;
    bool missingOk = mgr.RefineWith(sid, [&](Session&) {
        callbackRan = true;
    });
    EXPECT("RefineWith on missing returns false", !missingOk);
    EXPECT("RefineWith on missing does NOT invoke callback", !callbackRan);
}

// V3-A — FieldDisplayName reconstructs the candidate display name from the
// interned descriptor + the candidate's element index: the base name for a
// direct field (-1), and "base[idx]" for a TArray/container element.
static void Test_ValueScan_FieldDisplayName() {
    using namespace Radar;
    FieldDescriptor desc;
    desc.fieldName = "Items";

    EXPECT("Direct field uses base name (-1)",
           FieldDisplayName(desc, -1) == "Items");
    EXPECT("Element 0 renders [0]",
           FieldDisplayName(desc, 0) == "Items[0]");
    EXPECT("Element 42 renders [42]",
           FieldDisplayName(desc, 42) == "Items[42]");

    FieldDescriptor nested;
    nested.fieldName = "MaximumHealth.CurrentValue";
    EXPECT("Dotted nested base name preserved (-1)",
           FieldDisplayName(nested, -1) == "MaximumHealth.CurrentValue");

    // V1a — TMap key/value scan fields carry a "Map.Key" / "Map.Value" base
    // name (the per-pair half), so element rendering reads "Map.Key[idx]".
    FieldDescriptor mapKey;
    mapKey.fieldName = "Inventory.Key";
    EXPECT("Map key element renders Map.Key[idx]",
           FieldDisplayName(mapKey, 2) == "Inventory.Key[2]");
    FieldDescriptor mapVal;
    mapVal.fieldName = "Inventory.Value";
    EXPECT("Map value element renders Map.Value[idx]",
           FieldDisplayName(mapVal, 5) == "Inventory.Value[5]");

    // build 1201 — struct-array-inner descriptors carry a "[]" placeholder so
    // the element index lands after the ARRAY name, not at the very end:
    // "SaveSlotList[].GP" -> "SaveSlotList[3].GP".
    FieldDescriptor structArr;
    structArr.fieldName = "SaveSlotList[].GP";
    EXPECT("Struct-array-inner inserts index at placeholder",
           FieldDisplayName(structArr, 3) == "SaveSlotList[3].GP");
    EXPECT("Struct-array-inner drops empty placeholder when no index",
           FieldDisplayName(structArr, -1) == "SaveSlotList.GP");
    FieldDescriptor structArrNested;
    structArrNested.fieldName = "SaveSlotList[].MsTuneData.GP2";
    EXPECT("Struct-array-inner nested direct-struct path",
           FieldDisplayName(structArrNested, 1) == "SaveSlotList[1].MsTuneData.GP2");
}

// V1c — TOptional<T> bIsSet flag offset. A non-intrusive optional is laid out
// { T value; bool bIsSet; } padded to alignof(T), so the flag sits at
// offset == sizeof(T). OptionalFlagOffset returns that offset when the optional
// is larger than its value (room for the bool), else -1 (intrusive / unknown).
static void Test_ValueScan_OptionalFlagOffset() {
    using namespace Radar;
    // Non-intrusive numerics: flag at sizeof(T).
    EXPECT("TOptional<int8>  -> flag at 1", OptionalFlagOffset(2, 1)  == 1);
    EXPECT("TOptional<int16> -> flag at 2", OptionalFlagOffset(4, 2)  == 2);
    EXPECT("TOptional<int32> -> flag at 4", OptionalFlagOffset(8, 4)  == 4);
    EXPECT("TOptional<int64> -> flag at 8", OptionalFlagOffset(16, 8) == 8);
    EXPECT("TOptional<float> -> flag at 4", OptionalFlagOffset(8, 4)  == 4);
    EXPECT("TOptional<double>-> flag at 8", OptionalFlagOffset(16, 8) == 8);
    // FVector (double, 24B) -> 24 value + bool padded to 32.
    EXPECT("TOptional<FVector>-> flag at 24", OptionalFlagOffset(32, 24) == 24);
    // FString (16B) -> 16 value + bool padded to 24.
    EXPECT("TOptional<FString>-> flag at 16", OptionalFlagOffset(24, 16) == 16);
    // Intrusive / pointer-shaped: optional size == value size, no flag.
    EXPECT("Intrusive (size==inner) -> -1", OptionalFlagOffset(8, 8) == -1);
    // Unknown / unresolved inner size -> no gate.
    EXPECT("Zero inner size -> -1",     OptionalFlagOffset(8, 0)  == -1);
    EXPECT("Negative inner size -> -1", OptionalFlagOffset(8, -1) == -1);
    // Defensive: a value somehow larger than the optional -> no gate.
    EXPECT("inner > optional -> -1", OptionalFlagOffset(4, 8) == -1);
}

// V3-C — server-side ordered view (filter + sort + window) over a candidate
// pool. The DLL owns the full set; the UI is a window. These pure helpers run
// over the DLL's own pools (no game memory), so filter/sort never touch the
// game thread. Builds a tiny synthetic pool and checks filter / sort / format.
static void Test_ValueScan_OrderedView() {
    using namespace Radar;

    std::vector<FieldDescriptor> descs(2);
    descs[0].className = "BP_Player_C"; descs[0].definingClassName = "ACharacter";
    descs[0].fieldName = "Health"; descs[0].fieldType = "IntProperty"; descs[0].fieldOffset = 0x1C;
    descs[1].className = "BP_Enemy_C"; descs[1].definingClassName = "BP_Enemy_C";
    descs[1].fieldName = "Mana"; descs[1].fieldType = "IntProperty"; descs[1].fieldOffset = 0x40;

    std::vector<InstanceRecord> insts(2);
    insts[0].instanceAddr = 0x1000; insts[0].instanceIndex = 5; insts[0].instanceName = "Player_0";
    insts[1].instanceAddr = 0x2000; insts[1].instanceIndex = 9; insts[1].instanceName = "Enemy_3";

    auto mk = [](int32_t v, uintptr_t addr, uint32_t d, uint32_t inst) {
        Candidate c;
        std::memcpy(c.prevValue, &v, 4);
        c.addr = addr; c.descriptorIdx = d; c.instanceIdx = inst; c.elementIndex = -1;
        return c;
    };
    // Addresses chosen with no decimal-digit overlap with the test values /
    // offsets so a value/offset filter doesn't also match an address.
    std::vector<Candidate> cands = {
        mk(100, 0xAAAA, 0, 0),   // c0: Player.Health = 100
        mk(50,  0xBBBB, 1, 1),   // c1: Enemy.Mana    = 50
        mk(30,  0xCCCC, 0, 1),   // c2: Enemy.Health  = 30
        mk(200, 0xDDDD, 1, 0),   // c3: Player.Mana   = 200
    };
    const DataType dt = DataType::Int32;

    auto view = [&](const std::string& f, SortKey k, bool desc) {
        return BuildOrderedView(cands, descs, insts, dt, f, k, desc);
    };

    // --- no filter, ordering ---
    auto o = view("", SortKey::ScanOrder, false);
    EXPECT("ScanOrder keeps all in order", o.size() == 4 && o[0] == 0 && o[3] == 3);
    o = view("", SortKey::ScanOrder, true);
    EXPECT("ScanOrder desc reverses", o.size() == 4 && o[0] == 3 && o[3] == 0);

    o = view("", SortKey::Value, false);
    EXPECT("Value asc 30,50,100,200", o.size() == 4 && o[0] == 2 && o[1] == 1 && o[2] == 0 && o[3] == 3);
    o = view("", SortKey::Value, true);
    EXPECT("Value desc 200..30", o[0] == 3 && o[1] == 0 && o[2] == 1 && o[3] == 2);

    o = view("", SortKey::Offset, false);
    EXPECT("Offset asc stable (Health 0x1C then Mana 0x40)",
           o.size() == 4 && o[0] == 0 && o[1] == 2 && o[2] == 1 && o[3] == 3);

    o = view("", SortKey::ClassName, false);
    EXPECT("ClassName asc stable (Enemy then Player)",
           o[0] == 1 && o[1] == 3 && o[2] == 0 && o[3] == 2);

    o = view("", SortKey::Address, false);
    EXPECT("Address asc by pointer", o[0] == 0 && o[1] == 1 && o[2] == 2 && o[3] == 3);

    o = view("", SortKey::InstanceIndex, false);
    EXPECT("InstanceIndex asc (5 then 9)", o[0] == 0 && o[1] == 3 && o[2] == 1 && o[3] == 2);

    // --- filtering (case-insensitive substring across displayed columns) ---
    o = view("mana", SortKey::ScanOrder, false);
    EXPECT("filter field name 'mana'", o.size() == 2 && o[0] == 1 && o[1] == 3);
    o = view("enemy", SortKey::ScanOrder, false);
    EXPECT("filter class/instance 'enemy'", o.size() == 3 && o[0] == 1 && o[1] == 2 && o[2] == 3);
    o = view("100", SortKey::ScanOrder, false);
    EXPECT("filter by value '100'", o.size() == 1 && o[0] == 0);
    o = view("0x40", SortKey::ScanOrder, false);
    EXPECT("filter by offset hex '0x40'", o.size() == 2 && o[0] == 1 && o[1] == 3);
    // 'player' matches Player_0 instance (c0, c3) AND BP_Player_C class (c0, c2).
    o = view("PLAYER", SortKey::ScanOrder, false);
    EXPECT("filter case-insensitive 'PLAYER'", o.size() == 3 && o[0] == 0 && o[1] == 2 && o[2] == 3);
    o = view("zzz", SortKey::ScanOrder, false);
    EXPECT("filter no match -> empty", o.empty());

    // filter + sort compose (Health rows, by value asc): c2(30) then c0(100)
    o = view("health", SortKey::Value, false);
    EXPECT("filter 'health' + Value asc", o.size() == 2 && o[0] == 2 && o[1] == 0);

    // --- value formatting / decode ---
    EXPECT("FormatCandidateValue Int32 100", FormatCandidateValue(cands[0], dt, descs[0]) == "100");
    EXPECT("DecodeNumericToDouble Int32 100", DecodeNumericToDouble(DataType::Int32, cands[0].prevValue) == 100.0);
    Candidate bc; bc.prevValue[0] = 1;
    EXPECT("FormatCandidateValue Bool true", FormatCandidateValue(bc, DataType::Bool, descs[0]) == "true");
    EXPECT("DecodeNumericToDouble Bool true", DecodeNumericToDouble(DataType::Bool, bc.prevValue) == 1.0);

    // --- float/double: fixed-point, never scientific notation (Value Search +
    // Group Scan share FormatCandidateValue). In-range values keep the same 6
    // significant figures as the historical default; huge/garbage floats render
    // as plain digits instead of "5.73356e+17". ---
    {
        auto fcand = [](float f) { Candidate c; std::memcpy(c.prevValue, &f, 4); return c; };
        auto dcand = [](double d) { Candidate c; std::memcpy(c.prevValue, &d, 8); return c; };
        auto noExp = [](const std::string& s) {
            return s.find('e') == std::string::npos && s.find('E') == std::string::npos;
        };

        EXPECT("Float normal keeps 6 sig figs",
               FormatCandidateValue(fcand(1.39391f), DataType::Float, descs[0]) == "1.39391");
        EXPECT("Float whole -> integer (no trailing .0)",
               FormatCandidateValue(fcand(100.0f), DataType::Float, descs[0]) == "100");
        EXPECT("Float huge -> fixed-point, no exponent",
               noExp(FormatCandidateValue(fcand(5.73356e17f), DataType::Float, descs[0])));
        EXPECT("Double huge -> fixed-point, no exponent",
               noExp(FormatCandidateValue(dcand(1.0e20), DataType::Double, descs[0])));
        EXPECT("Float tiny -> fixed-point, no exponent",
               noExp(FormatCandidateValue(fcand(1.0e-7f), DataType::Float, descs[0])));
    }

    // --- sort key parsing ---
    SortKey k;
    EXPECT("parse 'value'", TryParseSortKey("value", k) && k == SortKey::Value);
    EXPECT("parse '' -> ScanOrder", TryParseSortKey("", k) && k == SortKey::ScanOrder);
    EXPECT("parse 'offset'", TryParseSortKey("offset", k) && k == SortKey::Offset);
    EXPECT("parse unknown -> false", !TryParseSortKey("bogus", k));

    // --- class-noise exclude (server-side, P2) ---
    {
        std::unordered_set<std::string> excl = { "BP_Enemy_C" };
        auto e = BuildOrderedView(cands, descs, insts, dt, "", SortKey::ScanOrder, false, excl);
        EXPECT("exclude BP_Enemy_C drops c1,c3", e.size() == 2 && e[0] == 0 && e[1] == 2);
        // exclude composes with the keyword filter: Health rows are all on the
        // excluded Player class -> empty.
        std::unordered_set<std::string> exclP = { "BP_Player_C" };
        auto e2 = BuildOrderedView(cands, descs, insts, dt, "health", SortKey::ScanOrder, false, exclP);
        EXPECT("exclude Player + filter 'health' -> empty", e2.empty());
        // empty exclude set == no exclusion (default arg path).
        auto e3 = BuildOrderedView(cands, descs, insts, dt, "", SortKey::ScanOrder, false, {});
        EXPECT("empty exclude keeps all 4", e3.size() == 4);
    }

    // --- class histogram (count desc, name asc; over the WHOLE pool) ---
    {
        auto h = BuildClassHistogram(cands, descs);
        EXPECT("histogram 2 classes", h.size() == 2);
        // tie at count 2 -> name ascending: BP_Enemy_C before BP_Player_C.
        EXPECT("histogram[0] BP_Enemy_C:2",  h.size() == 2 && h[0].first == "BP_Enemy_C" && h[0].second == 2);
        EXPECT("histogram[1] BP_Player_C:2", h.size() == 2 && h[1].first == "BP_Player_C" && h[1].second == 2);
    }

    // histogram count-desc ordering (distinct counts) over a separate pool.
    {
        std::vector<FieldDescriptor> hd(2);
        hd[0].className = "WidgetBlueprintGeneratedClass";
        hd[1].className = "BP_Pawn_C";
        std::vector<Candidate> hc(5);
        for (int i = 0; i < 5; ++i) hc[i].descriptorIdx = (i < 3) ? 0u : 1u;  // Widget x3, Pawn x2
        auto h = BuildClassHistogram(hc, hd);
        EXPECT("histogram count-desc Widget(3) then Pawn(2)",
               h.size() == 2 && h[0].first == "WidgetBlueprintGeneratedClass" && h[0].second == 3
               && h[1].first == "BP_Pawn_C" && h[1].second == 2);
    }

    // --- CanonicalExcludeKey: order-insensitive cache key ---
    EXPECT("CanonicalExcludeKey order-insensitive",
           CanonicalExcludeKey({ "B", "A" }) == CanonicalExcludeKey({ "A", "B" }));
    EXPECT("CanonicalExcludeKey empty -> empty", CanonicalExcludeKey({}).empty());
}

// IsEnginePackage: the "Game classes only" / auto-detect package gate. The
// critical case is GetFullName's "//Script/Engine/Class" double-leading-slash
// '/'-separator format — a strict prefix compare misses it, which silently made
// gameOnly a no-op for every engine class.
static void Test_IsEnginePackage() {
    using Aura::IsEnginePackage;

    // The real-world format GetFullName emits: double leading slash, '/' sep.
    EXPECT("engine: //Script/Engine/AnimSequence",        IsEnginePackage("//Script/Engine/AnimSequence"));
    EXPECT("engine: //Script/Engine/StaticMeshComponent", IsEnginePackage("//Script/Engine/StaticMeshComponent"));
    EXPECT("engine: //Script/CoreUObject/Object",         IsEnginePackage("//Script/CoreUObject/Object"));
    EXPECT("engine: //Script/Niagara/NiagaraScript",      IsEnginePackage("//Script/Niagara/NiagaraScript"));
    EXPECT("engine: //Script/Paper2D/PaperFlipbook",      IsEnginePackage("//Script/Paper2D/PaperFlipbook"));
    EXPECT("engine: //Script/CinematicCamera/CineCameraComponent",
           IsEnginePackage("//Script/CinematicCamera/CineCameraComponent"));

    // Canonical single-slash + '.'-separator format also matches.
    EXPECT("engine: /Script/Engine.Actor (canonical)", IsEnginePackage("/Script/Engine.Actor"));

    // Game classes are NOT engine — must survive gameOnly.
    EXPECT("game: //Game/BP/BP_Enemy_C",        !IsEnginePackage("//Game/BP/BP_Enemy_C"));
    EXPECT("game: //Script/MyGame/MyCharacter", !IsEnginePackage("//Script/MyGame/MyCharacter"));

    // Boundary: a game module whose name merely STARTS WITH an engine prefix
    // must not be mistaken for the engine module.
    EXPECT("boundary: //Script/EngineGameplay/Foo not engine",
           !IsEnginePackage("//Script/EngineGameplay/Foo"));

    // Degenerate inputs.
    EXPECT("empty -> not engine", !IsEnginePackage(""));
    EXPECT("all slashes -> not engine", !IsEnginePackage("///"));
}

// CanonicalizeObjectPath / LooksLikeObjectPath / PathLeafName: the pure half of
// find-object-by-path. THE case that matters is the cross-convention one — our own
// Ubel::GetFullName emits "//Script/Engine/Actor" while every caller, doc and .CT
// writes "/Script/Engine.Actor". Those two are the SAME object and compared unequal,
// which is why find-by-path resolved nothing at all before build 3157 (measured on
// Elliot: find_object "Actor" -> 0x7FF4DDE12068, find_object "/Script/Engine.Actor"
// -> "Object not found").
static void Test_CanonicalizeObjectPath() {
    using Aura::CanonicalizeObjectPath;
    using Aura::LooksLikeObjectPath;
    using Aura::PathLeafName;

    const std::string kActor = "/Script/Engine/Actor";

    // THE regression this exists for: all three spellings must agree.
    EXPECT_EQ_STR("canon: GetFullName form", CanonicalizeObjectPath("//Script/Engine/Actor"), kActor);
    EXPECT_EQ_STR("canon: UE canonical form", CanonicalizeObjectPath("/Script/Engine.Actor"), kActor);
    EXPECT_EQ_STR("canon: class-qualified form",
                  CanonicalizeObjectPath("Class /Script/Engine.Actor"), kActor);

    // Subobject separator ':' is a path separator too.
    EXPECT_EQ_STR("canon: subobject ':'",
                  CanonicalizeObjectPath("/Game/Maps/Foo.Foo:PersistentLevel"),
                  "/Game/Maps/Foo/Foo/PersistentLevel");

    // Case is PRESERVED — every sibling name compare in Aura is exact-cased.
    EXPECT_EQ_STR("canon: case preserved",
                  CanonicalizeObjectPath("/Script/Engine.actor"), "/Script/Engine/actor");

    // Degenerate inputs must not crash or produce a bare "/".
    EXPECT("canon: empty", CanonicalizeObjectPath("").empty());
    EXPECT("canon: all slashes", CanonicalizeObjectPath("///").empty());

    // NEGATIVE CONTROL: two different objects that share a leaf name must NOT
    // canonicalize equal. If this ever passes, the leaf pre-filter in
    // FindByFullName would be answering with whichever object it reached first.
    EXPECT("canon: same leaf, different package differs",
           CanonicalizeObjectPath("/Game/A.Foo") != CanonicalizeObjectPath("/Game/B.Foo"));

    // LooksLikeObjectPath decides whether the expensive resolve is attempted at all;
    // a bare FName must stay on the cheap single-pass route.
    EXPECT("looks: bare name is not a path", !LooksLikeObjectPath("Actor"));
    EXPECT("looks: slash form is a path",     LooksLikeObjectPath("/Script/Engine/Actor"));
    EXPECT("looks: dot form is a path",       LooksLikeObjectPath("/Script/Engine.Actor"));
    EXPECT("looks: colon form is a path",     LooksLikeObjectPath("Foo:Sub"));

    // The pre-filter's leaf must be the FName, or the cheap gate rejects every
    // candidate and the resolve silently finds nothing.
    EXPECT_EQ_STR("leaf: canonical form",  PathLeafName("/Script/Engine.Actor"), "Actor");
    EXPECT_EQ_STR("leaf: GetFullName form", PathLeafName("//Script/Engine/Actor"), "Actor");
    EXPECT_EQ_STR("leaf: class-qualified",  PathLeafName("Class /Script/Engine.Actor"), "Actor");
    EXPECT_EQ_STR("leaf: bare name is its own leaf", PathLeafName("Actor"), "Actor");
}

// IsReflectionMetaClass: the Object Tree "Instances only" server-side gate. MUST match
// the C# Helpers/ReflectionMetaClassifier — excludes the FULL reflection/type layer, not
// just class-like metas (else UFunction/UScriptStruct/UPackage/UEnum leak through).
static void Test_IsReflectionMetaClass() {
    using Aura::IsReflectionMetaClass;

    // Class family
    EXPECT("meta: Class",                     IsReflectionMetaClass("Class"));
    EXPECT("meta: BlueprintGeneratedClass",   IsReflectionMetaClass("BlueprintGeneratedClass"));
    EXPECT("meta: WidgetBlueprintGeneratedClass", IsReflectionMetaClass("WidgetBlueprintGeneratedClass"));
    EXPECT("meta: DynamicClass",              IsReflectionMetaClass("DynamicClass"));
    // Function family — the headline gap a class-only filter would miss
    EXPECT("meta: Function",                  IsReflectionMetaClass("Function"));
    EXPECT("meta: DelegateFunction",          IsReflectionMetaClass("DelegateFunction"));
    EXPECT("meta: SparseDelegateFunction",    IsReflectionMetaClass("SparseDelegateFunction"));
    // Struct / enum descriptors + package
    EXPECT("meta: ScriptStruct",              IsReflectionMetaClass("ScriptStruct"));
    EXPECT("meta: UserDefinedStruct",         IsReflectionMetaClass("UserDefinedStruct"));
    EXPECT("meta: Enum",                      IsReflectionMetaClass("Enum"));
    EXPECT("meta: UserDefinedEnum",           IsReflectionMetaClass("UserDefinedEnum"));
    EXPECT("meta: Package",                   IsReflectionMetaClass("Package"));
    // UE4 UProperty descriptors — caught by the "…Property" suffix
    EXPECT("meta: IntProperty",               IsReflectionMetaClass("IntProperty"));
    EXPECT("meta: ObjectProperty",            IsReflectionMetaClass("ObjectProperty"));
    EXPECT("meta: StructProperty",            IsReflectionMetaClass("StructProperty"));
    EXPECT("meta: MulticastInlineDelegateProperty", IsReflectionMetaClass("MulticastInlineDelegateProperty"));

    // Live instances are kept
    EXPECT("instance: BP_Enemy_C",            !IsReflectionMetaClass("BP_Enemy_C"));
    EXPECT("instance: Character",             !IsReflectionMetaClass("Character"));
    EXPECT("instance: PlayerController",      !IsReflectionMetaClass("PlayerController"));
    EXPECT("instance: StaticMeshComponent",   !IsReflectionMetaClass("StaticMeshComponent"));
    EXPECT("instance: MyDataManager (not …Property)", !IsReflectionMetaClass("MyDataManager"));

    // Contract: empty kept; exact-cased (UE never emits lowercase "class")
    EXPECT("empty -> instance",               !IsReflectionMetaClass(""));
    EXPECT("case-sensitive: 'class' kept",    !IsReflectionMetaClass("class"));
}

// Keyword tokenize + space=AND matcher: the server-side twin of the C#
// ObjectTreeFilter (term-level AND, field-level OR over obj+class name).
static void Test_KeywordMatch() {
    using Aura::SplitLowerKeywords;
    using Aura::MatchesAllKeywords;

    // Tokenize: whitespace-split + lowercase; blanks collapse.
    auto t0 = SplitLowerKeywords("  BP_   Char ");
    EXPECT("split count", t0.size() == 2);
    EXPECT("split lower[0]", t0.size() == 2 && t0[0] == "bp_");
    EXPECT("split lower[1]", t0.size() == 2 && t0[1] == "char");
    EXPECT("split blank -> empty", SplitLowerKeywords("   ").empty());

    // Empty terms match everything.
    EXPECT("empty terms match", MatchesAllKeywords(SplitLowerKeywords(""), "Anything", "AnyClass"));

    // AND across fields: "bp_" hits class, "char" hits name.
    auto t1 = SplitLowerKeywords("bp_ char");
    EXPECT("AND across name+class", MatchesAllKeywords(t1, "MyCharacter", "BP_MyCharacter_C"));
    // A term matching neither field fails the AND.
    auto t2 = SplitLowerKeywords("bp_ player");
    EXPECT("missing term -> no match", !MatchesAllKeywords(t2, "MyCharacter", "BP_MyCharacter_C"));
    // Case-insensitive; single term may hit the class only.
    auto t3 = SplitLowerKeywords("PAWN");
    EXPECT("term hits class only (ci)", MatchesAllKeywords(t3, "Default__Thing", "APawn"));
    EXPECT("term hits neither", !MatchesAllKeywords(t3, "Default__Thing", "AActor"));
}

// Snapshot "Auto detect Engine/System noise" source-level skip: the PURE
// precedence (DecideSnapshotNoise) + the keep/noise base sets. The live
// super-chain / package predicates that FEED the booleans read game memory and
// can't run here, so we lock down (a) that the gameplay guardrail always wins —
// the Pawn pitfall: a player Pawn's X/Y/Z must never be source-skipped — and (b)
// that the keep set holds the gameplay carriers while the noise set holds only
// engine leaves (and NO gameplay carrier).
static void Test_SnapshotNoise_GuardrailAndSets() {
    using namespace Aura;

    // Guardrail wins: a keep-base-derived class is NEVER noise, no matter what
    // the package/noise-base predicates say (the irreversible-skip safety net).
    EXPECT("keep beats engine package", DecideSnapshotNoise(true,  true,  false) == false);
    EXPECT("keep beats noise base",     DecideSnapshotNoise(true,  false, true)  == false);
    EXPECT("keep beats both",           DecideSnapshotNoise(true,  true,  true)  == false);

    // Non-keep classes: engine /Script package OR engine leaf base => noise.
    EXPECT("engine package is noise",   DecideSnapshotNoise(false, true,  false) == true);
    EXPECT("engine leaf base is noise", DecideSnapshotNoise(false, false, true)  == true);
    EXPECT("both rules => noise",       DecideSnapshotNoise(false, true,  true)  == true);

    // Plain game class (not keep, not engine, not a noise leaf) is kept.
    EXPECT("plain game class kept",     DecideSnapshotNoise(false, false, false) == false);

    // Keep set must contain the gameplay value carriers (Pawn pitfall guard).
    const auto& keep = SnapshotGameplayKeepBases();
    EXPECT("Actor kept",          keep.count("Actor") == 1);
    EXPECT("ActorComponent kept", keep.count("ActorComponent") == 1);  // HP/MP in components
    EXPECT("Pawn kept",           keep.count("Pawn") == 1);
    EXPECT("Character kept",      keep.count("Character") == 1);
    EXPECT("Controller kept",     keep.count("Controller") == 1);
    EXPECT("PlayerState kept",    keep.count("PlayerState") == 1);

    // Noise set must hold engine leaves but NO gameplay carrier (so a class that
    // derives from Pawn/Actor/component can never be flagged via the noise rule).
    const auto& noise = SnapshotEngineNoiseBases();
    EXPECT("Widget is noise base",        noise.count("Widget") == 1);
    EXPECT("UserWidget is noise base",    noise.count("UserWidget") == 1);
    EXPECT("NiagaraSystem is noise base", noise.count("NiagaraSystem") == 1);
    EXPECT("Actor NOT a noise base",          noise.count("Actor") == 0);
    EXPECT("Pawn NOT a noise base",           noise.count("Pawn") == 0);
    EXPECT("ActorComponent NOT a noise base", noise.count("ActorComponent") == 0);
}

// Snapshot type-family narrowing (NumericDataTypeInFamily): the orthogonal
// integer-vs-float filter applied on top of the numeric scope. Any keeps all;
// IntegersOnly drops Float/Double; FloatsOnly keeps only Float/Double. Locks the
// per-width verdicts + the wire-string parse so a UI pick maps to the right cut.
static void Test_NumericFamily_Filter() {
    using namespace Aura;
    using Radar::DataType;

    // Any keeps every numeric width (the prior, no-narrowing behaviour).
    EXPECT("Any keeps Int32",  NumericDataTypeInFamily(DataType::Int32,  NumericFamily::Any));
    EXPECT("Any keeps Float",  NumericDataTypeInFamily(DataType::Float,  NumericFamily::Any));
    EXPECT("Any keeps Double", NumericDataTypeInFamily(DataType::Double, NumericFamily::Any));
    EXPECT("Any keeps UInt8",  NumericDataTypeInFamily(DataType::UInt8,  NumericFamily::Any));

    // IntegersOnly keeps every integer width, drops Float/Double.
    EXPECT("Int keeps Int8",    NumericDataTypeInFamily(DataType::Int8,   NumericFamily::IntegersOnly));
    EXPECT("Int keeps Int64",   NumericDataTypeInFamily(DataType::Int64,  NumericFamily::IntegersOnly));
    EXPECT("Int keeps UInt32",  NumericDataTypeInFamily(DataType::UInt32, NumericFamily::IntegersOnly));
    EXPECT("Int drops Float",  !NumericDataTypeInFamily(DataType::Float,  NumericFamily::IntegersOnly));
    EXPECT("Int drops Double", !NumericDataTypeInFamily(DataType::Double, NumericFamily::IntegersOnly));

    // FloatsOnly keeps Float/Double, drops every integer width.
    EXPECT("Float keeps Float",   NumericDataTypeInFamily(DataType::Float,  NumericFamily::FloatsOnly));
    EXPECT("Float keeps Double",  NumericDataTypeInFamily(DataType::Double, NumericFamily::FloatsOnly));
    EXPECT("Float drops Int32",  !NumericDataTypeInFamily(DataType::Int32,  NumericFamily::FloatsOnly));
    EXPECT("Float drops Int64",  !NumericDataTypeInFamily(DataType::Int64,  NumericFamily::FloatsOnly));
    EXPECT("Float drops UInt8",  !NumericDataTypeInFamily(DataType::UInt8,  NumericFamily::FloatsOnly));

    // Wire-string parse (unknown -> Any, the safe back-compat default).
    EXPECT("parse IntegersOnly", ParseNumericFamily("IntegersOnly") == NumericFamily::IntegersOnly);
    EXPECT("parse FloatsOnly",   ParseNumericFamily("FloatsOnly")   == NumericFamily::FloatsOnly);
    EXPECT("parse Any",          ParseNumericFamily("Any")          == NumericFamily::Any);
    EXPECT("parse unknown->Any", ParseNumericFamily("garbage")      == NumericFamily::Any);
    EXPECT("parse empty->Any",   ParseNumericFamily("")             == NumericFamily::Any);
}

// Which leaf each slot DISPLAYS. Reproduces the DumperTest sample exactly: a
// Changed + Unchanged refine where slot 0 kept {Health.CurrentValue, TickCount}
// and slot 1 kept {PrimaryActorTick.TickInterval, Health.BaseValue, FrozenInt}.
// Two valid assignments, one row — and for four separate reports the pair the
// row did not pick was read as a missed match.
//
// The rule used to live inside Fern.cpp's JSON builder where no test could reach
// it, which is how it kept drifting away from the filter it must agree with.
static void Test_Radar_PickGroupWitnessAssignment() {
    using namespace Radar;

    auto desc = [](const char* defining, const char* field) {
        FieldDescriptor d;
        d.className = "DumperTestActor";
        d.definingClassName = defining;
        d.fieldName = field;
        d.fieldType = "IntProperty";
        return d;
    };
    std::vector<FieldDescriptor> descs = {
        desc("Actor",                "PrimaryActorTick.TickInterval"),  // 0
        desc("DumperTestAttribute",  "Health.BaseValue"),               // 1
        desc("DumperTestAttribute",  "Health.CurrentValue"),            // 2
        desc("DumperTestActor",      "TickCount"),                      // 3
        desc("DumperTestActor",      "FrozenInt"),                      // 4
    };

    auto leaf = [](uint32_t descIdx, uintptr_t addr, int32_t value) {
        GroupSlotMatch m;
        m.descriptorIdx = descIdx;
        m.leafAddr      = addr;
        m.offset        = static_cast<int32_t>(addr);
        std::memcpy(m.prevValue, &value, sizeof(value));
        return m;
    };

    std::vector<SlotSpec> slots(2);
    slots[0].dt = DataType::Int32; slots[0].st = ScanType::Changed;
    slots[1].dt = DataType::Int32; slots[1].st = ScanType::Unchanged;

    // The real offsets from the 2026-08-05 session: FrozenInt @0x51C == 1308.
    std::vector<std::vector<GroupSlotMatch>> sm = {
        { leaf(2, 1288, 19),  leaf(3, 1304, 101) },
        { leaf(0,   52,  0),  leaf(1, 1284, 100), leaf(4, 1308, 424242) },
    };

    // A zero is almost never the value being hunted (engine bookkeeping sits at 0
    // by default), so `TickInterval=0, InitialLifeSpan=0` is the least useful pair
    // a candidate can show. Non-zero wins INSIDE each rule.
    EXPECT("zero: plain / decimal / signed / vector forms",
           IsZeroValueText("0") && IsZeroValueText("0.0") && IsZeroValueText("-0.00") &&
           IsZeroValueText("0, 0, 0") && IsZeroValueText(" 0 "));
    EXPECT("zero: any non-zero digit disqualifies",
           !IsZeroValueText("1") && !IsZeroValueText("0.5") && !IsZeroValueText("100") &&
           !IsZeroValueText("0, 0, 1"));
    EXPECT("zero: non-numeric text is not a zero",
           !IsZeroValueText("") && !IsZeroValueText("-") && !IsZeroValueText("Elite"));

    // No filter: slot 0 takes its first free leaf, and slot 1 then prefers a
    // SIBLING from the same struct — Health.BaseValue, not the engine tick field
    // that happens to come first.
    auto plain = PickGroupWitnessAssignment(sm, slots, descs, {});
    EXPECT("witness: slot0 -> Health.CurrentValue", plain.size() == 2 && plain[0] == 0);
    EXPECT("witness: slot1 prefers the same struct over the first free leaf", plain[1] == 1);

    // Pass 3 skips a leading zero. Reproduces the default first-scan row that read
    // `PrimaryActorTick.TickInterval=0, InitialLifeSpan=0` — a valid pairing and a
    // useless one, while the same object's real fields had matched too.
    std::vector<std::vector<GroupSlotMatch>> zeroFirst = {
        { leaf(0,   52,      0), leaf(3, 1304,    183) },   // TickInterval=0, TickCount=183
        { leaf(0, 1216,      0), leaf(4, 1308, 424242) },   // (zero), FrozenInt=424242
    };
    auto nonZero = PickGroupWitnessAssignment(zeroFirst, slots, descs, {});
    EXPECT("zero: slot0 skips the 0 and shows TickCount", nonZero[0] == 1);
    EXPECT("zero: slot1 skips the 0 too", nonZero[1] == 1);

    // But a slot with NOTHING but zeros must still show one — the fallback is a
    // tie-break, not a filter, and an empty cell would be a worse lie.
    std::vector<std::vector<GroupSlotMatch>> allZero = {
        { leaf(3, 1304, 0) },
        { leaf(4, 1308, 0) },
    };
    auto zeros = PickGroupWitnessAssignment(allZero, slots, descs, {});
    EXPECT("zero: an all-zero slot still reports its leaf",
           zeros.size() == 2 && zeros[0] == 0 && zeros[1] == 0);

    // And an explicitly filtered-for zero still wins: the tie-break lives INSIDE
    // each rule, so it can never outrank what the user actually asked for.
    std::vector<std::vector<GroupSlotMatch>> askedForZero = {
        { leaf(3, 1304, 183), leaf(0, 52, 0) },   // TickCount first, the 0 second
        { leaf(4, 1308, 424242) },
    };
    auto asked = PickGroupWitnessAssignment(
        askedForZero, slots, descs, SplitFilterTerms("tickinterval"));
    EXPECT("zero: a filtered-for zero still wins its slot", asked[0] == 1);

    // Filtering by NAME must bring that field to the front of its own slot...
    auto byName = PickGroupWitnessAssignment(sm, slots, descs, SplitFilterTerms("tickcount"));
    EXPECT("witness: filter 'tickcount' -> slot0 shows TickCount", byName[0] == 1);
    // ...and the sibling rule then pairs it with the other field of the SAME
    // class, which is exactly the TickCount/FrozenInt pairing that was reported
    // missing while both had matched all along.
    EXPECT("witness: and slot1 pairs it with FrozenInt (same defining class)", byName[1] == 2);

    // Filtering by VALUE goes through GroupSlotValueString — the same call the
    // server-side filter matches on, so a row can never show a leaf the filter
    // did not match.
    auto byValue = PickGroupWitnessAssignment(sm, slots, descs, SplitFilterTerms("424242"));
    EXPECT("witness: filter '424242' -> slot1 shows FrozenInt", byValue[1] == 2);

    // TWO TERMS = the only way to ask for a SPECIFIC pairing. With 2 x 3 kept
    // leaves (2 x 36 in the real case) nothing in the data says which of the many
    // valid pairings you meant, so naming both fields is the request. Each term
    // must land in its OWN slot — one greedy slot taking both would put the second
    // named field nowhere, which is the whole defect.
    auto twoTerms = PickGroupWitnessAssignment(
        sm, slots, descs, SplitFilterTerms("tickcount frozenint"));
    EXPECT("witness: 'tickcount frozenint' -> slot0 = TickCount", twoTerms[0] == 1);
    EXPECT("witness: 'tickcount frozenint' -> slot1 = FrozenInt", twoTerms[1] == 2);

    // Order-independent: the user should not have to know which value is slot 0.
    auto reversed = PickGroupWitnessAssignment(
        sm, slots, descs, SplitFilterTerms("frozenint tickcount"));
    EXPECT("witness: term order does not decide slot order",
           reversed[0] == 1 && reversed[1] == 2);

    // Extra whitespace must not create empty terms (an empty term matches every
    // leaf and would silently disable the pairing).
    EXPECT("terms: whitespace collapsed, lower-cased",
           SplitFilterTerms("  TickCount\t frozenINT  ").size() == 2 &&
           SplitFilterTerms("  TickCount\t frozenINT  ")[0] == "tickcount" &&
           SplitFilterTerms("  TickCount\t frozenINT  ")[1] == "frozenint");
    EXPECT("terms: empty filter yields no terms", SplitFilterTerms("   ").empty());

    // Distinctness: when both slots keep the SAME leaf first, they must not both
    // display it. "PrimaryActorTick.TickInterval=0, PrimaryActorTick.TickInterval=0"
    // is a value apparently paired with itself, which MatchGroup forbids outright.
    std::vector<std::vector<GroupSlotMatch>> shared = {
        { leaf(0, 52, 0), leaf(1, 1284, 100) },
        { leaf(0, 52, 0), leaf(1, 1284, 100) },
    };
    auto distinct = PickGroupWitnessAssignment(shared, slots, descs, {});
    EXPECT("witness: no leaf is displayed by two slots",
           shared[0][distinct[0]].leafAddr != shared[1][distinct[1]].leafAddr);

    // audit #5 AB18 — the case GREEDY ALONE gets wrong. slot0 has two options and
    // slot1 has only the shared one; greedy seats slot0 on the shared leaf first and
    // leaves slot1 nothing free, so it duplicated it — even though a distinct
    // assignment plainly exists (slot0 -> unique, slot1 -> shared). The augmenting-
    // path repair must recover it. This FAILS before the fix and passes after.
    std::vector<std::vector<GroupSlotMatch>> forcedSwap = {
        { leaf(1, 1284, 100), leaf(2, 1288, 19) },   // slot0: two options
        { leaf(1, 1284, 100) },                       // slot1: only the shared leaf
    };
    auto swapped = PickGroupWitnessAssignment(forcedSwap, slots, descs, {});
    EXPECT("witness: augmenting path yields a distinct assignment greedy would miss",
           swapped.size() == 2 &&
           forcedSwap[0][swapped[0]].leafAddr != forcedSwap[1][swapped[1]].leafAddr);
    EXPECT("witness: the forced-unique slot keeps its only (shared) leaf",
           forcedSwap[1][swapped[1]].leafAddr == 1284);
    EXPECT("witness: and the flexible slot moved to its other leaf",
           forcedSwap[0][swapped[0]].leafAddr == 1288);

    // An empty slot claims nothing and must not disturb the later slots. The
    // filter picks the SECOND leaf, so an implementation that lost its place
    // (or just returned zeros) fails here rather than passing by construction.
    std::vector<std::vector<GroupSlotMatch>> withEmpty = {
        {},
        { leaf(1, 1284, 100), leaf(4, 1308, 424242) },
    };
    auto emptied = PickGroupWitnessAssignment(withEmpty, slots, descs, SplitFilterTerms("frozen"));
    EXPECT("witness: an empty slot is skipped and later slots still filter",
           emptied.size() == 2 && emptied[0] == 0 && emptied[1] == 1);

    // A descriptorIdx past the pool must never index it. Slot 0 leads with a
    // corrupt entry and the filter matches only the SECOND leaf, so the guard has
    // to skip rather than match-or-crash — asserting index 1 is what makes this
    // test fail on a missing bounds check instead of passing on a default 0.
    std::vector<std::vector<GroupSlotMatch>> bad = {
        { leaf(99, 4096, 7), leaf(1, 1284, 100) },
        { leaf(4, 1308, 424242) },
    };
    auto guarded = PickGroupWitnessAssignment(bad, slots, descs, SplitFilterTerms("health"));
    EXPECT("witness: an out-of-range descriptor is skipped, not indexed",
           guarded.size() == 2 && guarded[0] == 1 && guarded[1] == 0);

    // ---- leaf-list display order ----
    // Leaves arrive base-class-first, so an actor's list opens with engine fields
    // and the ones the user came for are thirty rows down a 220px scrolling box.
    // The object's OWN declared fields come first. A leaf nested in a STRUCT
    // reports the struct as its defining class, so it stays in the second tier —
    // it is not a field of this class either.
    auto ordered = OrderGroupSlotLeaves(sm[1], descs);
    EXPECT("order: 3 leaves in, 3 out", ordered.size() == 3);
    EXPECT("order: FrozenInt (declared by DumperTestActor) comes first",
           descs[sm[1][ordered[0]].descriptorIdx].fieldName == "FrozenInt");
    // ...and the two it jumped are still in their original relative order.
    EXPECT("order: the inherited/nested tier keeps scan order",
           descs[sm[1][ordered[1]].descriptorIdx].fieldName == "PrimaryActorTick.TickInterval" &&
           descs[sm[1][ordered[2]].descriptorIdx].fieldName == "Health.BaseValue");
    // Ordering must not drop or duplicate anything — it decides what is VISIBLE.
    std::vector<size_t> seen = ordered;
    std::sort(seen.begin(), seen.end());
    EXPECT("order: a permutation, nothing lost or repeated",
           seen.size() == 3 && seen[0] == 0 && seen[1] == 1 && seen[2] == 2);
}

// audit #5 AB16 — the server-side Value Search filter must cover the displayed
// "Origin" column, so filtering for "native" hits rows that visibly read "Native-C".
static void Test_Radar_FormatCandidateOrigin() {
    using namespace Radar;
    FieldDescriptor reflected;      // isNativeC = false by default
    EXPECT("origin: reflected field", FormatCandidateOrigin(reflected) == "Reflected");

    FieldDescriptor nativeWidth;
    nativeWidth.isNativeC   = true;
    nativeWidth.guessedType = "Int32";
    EXPECT("origin: native with width", FormatCandidateOrigin(nativeWidth) == "Native-C (Int32)");

    FieldDescriptor nativeNoWidth;
    nativeNoWidth.isNativeC = true;   // guessedType empty
    EXPECT("origin: native without width", FormatCandidateOrigin(nativeNoWidth) == "Native-C");

    // The string exactly mirrors the C# ScanCandidate.Origin, so the server-side
    // keyword filter now matches a column the user can see (the whole finding).
}

// audit #5 AB19 — a group session's leaf memory is candidates x slots x perSlotCap
// GroupSlotMatch objects; only the last two are clamped. The pure helpers below let
// GroupSessionManager::Begin bound the retained session by total leaves.
static void Test_Radar_GroupLeafBudget() {
    using namespace Radar;
    auto candWith = [](size_t slotCount, size_t leavesPerSlot) {
        GroupCandidate gc;
        gc.slotMatches.resize(slotCount);
        for (auto& sl : gc.slotMatches) sl.resize(leavesPerSlot);
        return gc;
    };

    // Leaf count is the plain product summed over candidates.
    std::vector<GroupCandidate> pool = {
        candWith(2, 3),   // 6 leaves
        candWith(2, 4),   // 8 leaves
        candWith(2, 5),   // 10 leaves
    };
    EXPECT("leafcount: sum over all slots of all candidates",
           GroupSessionLeafCount(pool) == 24);

    // Empty pool -> 0 kept, 0 leaves.
    EXPECT("budget: empty pool keeps nothing",
           GroupCandidatesWithinLeafBudget({}, 1000) == 0);
    EXPECT("leafcount: empty pool is 0", GroupSessionLeafCount({}) == 0);

    // Whole pool fits under a generous budget.
    EXPECT("budget: whole pool fits", GroupCandidatesWithinLeafBudget(pool, 1000) == 3);

    // A tight budget keeps the LEADING candidates (scan order) up to the cap: the
    // first (6) fits under 10, adding the second (8 -> 14) would exceed it, so 1 kept.
    EXPECT("budget: trims the tail at the leaf cap",
           GroupCandidatesWithinLeafBudget(pool, 10) == 1);
    // Exactly the first two (6 + 8 = 14) at a budget of 14.
    EXPECT("budget: boundary keeps as many as fit", GroupCandidatesWithinLeafBudget(pool, 14) == 2);

    // Never drop the whole scan on the backstop: a single over-budget candidate is
    // still kept (it is itself bounded by slots x perSlotCap).
    std::vector<GroupCandidate> oneBig = { candWith(4, 4096) };  // 16384 leaves
    EXPECT("budget: one over-budget candidate is still kept",
           GroupCandidatesWithinLeafBudget(oneBig, 100) == 1);
}

// audit #5 AE13 — the per-slot cap verdict has to SURVIVE Begin.
//
// Orden::MatchGroup reports truncation (Test_Orden_PerSlotCap above), and until this
// fix the whole of its onward journey was a LOG_WARN inside ScanForValueGroup: no wire
// key existed, so no DTO could carry it and the UI presented a capped witness list as
// the complete set of matching fields. Refine cannot recompute it — it prunes the
// stored pool and never calls the matcher again — and query is a pure window, so the
// verdict has to be carried on the SESSION or it is lost after the first response.
//
// NEGATIVE CONTROL: drop the two assignments from GroupSessionManager::Begin and both
// EXPECTs below fail with the default-constructed false/0.
static void Test_Radar_GroupSessionCarriesPerSlotCap() {
    using namespace Radar;
    auto& mgr = GroupSessionManager::Instance();

    std::vector<GroupCandidate> pool(1);
    pool[0].slotMatches.resize(2);
    for (auto& sl : pool[0].slotMatches) sl.resize(1);

    uint64_t hitId = mgr.Begin({}, pool, {}, {}, /*perSlotCapHit=*/true, /*perSlotCap=*/256);
    bool sawHit = false; int sawCap = -1;
    EXPECT("session: the capped session is retrievable",
           mgr.WithSession(hitId, [&](const GroupSession& s) {
               sawHit = s.perSlotCapHit; sawCap = s.perSlotCap;
           }));
    EXPECT("session: a Begin-time cap hit survives into the session", sawHit);
    EXPECT("session: and so does the EFFECTIVE cap the DLL clamped to", sawCap == 256);

    // Control: an uncapped scan must not report one, or every session would warn.
    uint64_t cleanId = mgr.Begin({}, pool, {}, {}, /*perSlotCapHit=*/false, /*perSlotCap=*/256);
    bool cleanHit = true;
    mgr.WithSession(cleanId, [&](const GroupSession& s) { cleanHit = s.perSlotCapHit; });
    EXPECT("session: an uncapped scan reports no truncation", !cleanHit);

    mgr.End(hitId);
    mgr.End(cleanId);
}

// The grid must order by the leaf the ROW SHOWS — audit #5 AB6.
//
// BuildGroupOrderedView's Value/Offset/ClassName keys read slotMatches[0][0], the first
// leaf the scan happened to keep, while Fern renders slotMatches[0][picks[0]] from
// PickGroupWitnessAssignment. With per_slot_cap at 256 a slot routinely keeps dozens, so
// "sort by Value" produced an order with no visible relationship to the Value column.
//
// The fixture is built so the two disagree: in each candidate the leaf at index 0 is
// deliberately ranked in the OPPOSITE order to the leaf the witness picker chooses, so a
// view still keyed on [0] returns the reverse of the expected order rather than
// coincidentally agreeing.
static void Test_Radar_GroupSortUsesTheDisplayedLeaf() {
    using namespace Radar;

    auto desc = [](const char* field, const char* cls) {
        FieldDescriptor d;
        d.className = cls;
        d.definingClassName = cls;   // own-class => the picker's preferred tier
        d.fieldName = field;
        d.fieldType = "IntProperty";
        return d;
    };
    std::vector<FieldDescriptor> descs = {
        desc("Bookkeeping", "Actor"),        // 0 — inherited noise, value 0
        desc("Health",      "BP_Enemy_C"),   // 1
        desc("Health",      "BP_Boss_C"),    // 2
    };
    descs[0].definingClassName = "Actor";
    descs[0].className         = "BP_Enemy_C";   // inherited => second tier for the picker

    auto leaf = [](uint32_t descIdx, int32_t offset, int32_t value) {
        GroupSlotMatch m;
        m.descriptorIdx = descIdx;
        m.leafAddr      = static_cast<uintptr_t>(offset);
        m.offset        = offset;
        std::memcpy(m.prevValue, &value, sizeof(value));
        return m;
    };

    std::vector<SlotSpec> slots(1);
    slots[0].dt = DataType::Int32;
    slots[0].st = ScanType::Changed;

    std::vector<InstanceRecord> instances(2);
    instances[0].instanceName = "Enemy_0"; instances[0].instanceIndex = 10;
    instances[1].instanceName = "Enemy_1"; instances[1].instanceIndex = 11;

    // Candidate A: leaf[0] is the inherited zero (offset 8), the DISPLAYED leaf is
    // Health=900 at offset 0x200. Candidate B: leaf[0] is the inherited zero too, and
    // its displayed leaf is Health=100 at offset 0x100.
    //
    // Keyed on [0] both candidates tie at value 0 / offset 8 and keep scan order (A, B).
    // Keyed on the DISPLAYED leaf, ascending Value and ascending Offset both put B first.
    std::vector<GroupCandidate> candidates(2);
    candidates[0].instanceIdx = 0;
    candidates[0].slotMatches = { { leaf(0, 8, 0), leaf(1, 0x200, 900) } };
    candidates[1].instanceIdx = 1;
    candidates[1].slotMatches = { { leaf(0, 8, 0), leaf(2, 0x100, 100) } };

    auto view = [&](SortKey k) {
        return BuildGroupOrderedView(candidates, slots, descs, instances,
                                     /*filter=*/"", k, /*sortDesc=*/false, {});
    };

    // Guard the premise: keyed on [0] these tie, so a passing result below cannot be
    // an accident of the fixture already being in the right order.
    auto byScan = view(SortKey::ScanOrder);
    EXPECT("groupsort: fixture starts in scan order A,B",
           byScan.size() == 2 && byScan[0] == 0 && byScan[1] == 1);

    auto byValue = view(SortKey::Value);
    EXPECT("groupsort: Value orders by the DISPLAYED leaf (100 before 900)",
           byValue.size() == 2 && byValue[0] == 1 && byValue[1] == 0);

    auto byOffset = view(SortKey::Offset);
    EXPECT("groupsort: Offset orders by the DISPLAYED leaf (0x100 before 0x200)",
           byOffset.size() == 2 && byOffset[0] == 1 && byOffset[1] == 0);

    // ClassName reads the descriptor of the displayed leaf too: BP_Boss_C < BP_Enemy_C.
    auto byClass = view(SortKey::ClassName);
    EXPECT("groupsort: ClassName reads the DISPLAYED leaf's descriptor",
           byClass.size() == 2 && byClass[0] == 1 && byClass[1] == 0);

    // Descending must be the exact reverse — the flip is applied to the same key.
    auto desc2 = BuildGroupOrderedView(candidates, slots, descs, instances,
                                       "", SortKey::Value, /*sortDesc=*/true, {});
    EXPECT("groupsort: descending reverses the same key",
           desc2.size() == 2 && desc2[0] == 0 && desc2[1] == 1);

    // A slot that kept nothing must not index anything: keys fall back to 0/"" and the
    // view still returns every candidate.
    std::vector<GroupCandidate> withEmpty(2);
    withEmpty[0].instanceIdx = 0;
    withEmpty[0].slotMatches = { {} };
    withEmpty[1].instanceIdx = 1;
    withEmpty[1].slotMatches = { { leaf(1, 0x100, 100) } };
    auto emptied = BuildGroupOrderedView(withEmpty, slots, descs, instances,
                                         "", SortKey::Value, false, {});
    EXPECT("groupsort: an empty slot is survivable and drops nobody", emptied.size() == 2);
}

// Group-scan server-side class filter: exclude skip + histogram bucket on the
// candidate's OBJECT-level class (first non-empty slot's match), including the
// defensive case where slot 0 is empty so the class comes from a later slot.
static void Test_GroupScan_ExcludeAndHistogram() {
    using namespace Radar;

    std::vector<FieldDescriptor> descs(2);
    descs[0].className = "BP_Enemy_C";
    descs[0].fieldName = "HP"; descs[0].fieldType = "IntProperty";
    descs[1].className = "WidgetBlueprintGeneratedClass";
    descs[1].fieldName = "Opacity"; descs[1].fieldType = "FloatProperty";

    std::vector<InstanceRecord> insts(3);
    for (int i = 0; i < 3; ++i) {
        insts[i].instanceAddr  = 0x1000 + (uintptr_t)i * 0x100;
        insts[i].instanceIndex = i;
        insts[i].instanceName  = "Obj_" + std::to_string(i);
    }

    // Two slots (invariant: slotMatches.size() == slots.size()).
    std::vector<SlotSpec> slots(2);
    slots[0].dt = DataType::Int32;
    slots[1].dt = DataType::Int32;

    auto sm = [](uint32_t desc) { GroupSlotMatch m; m.descriptorIdx = desc; return m; };
    auto mk = [&](uint32_t inst, bool emptyLeadingSlot, uint32_t classDesc) {
        GroupCandidate gc; gc.instanceIdx = inst;
        if (emptyLeadingSlot) {
            gc.slotMatches.push_back({});            // slot 0 empty (defensive)
            gc.slotMatches.push_back({ sm(classDesc) });  // class resolved from slot 1
        } else {
            gc.slotMatches.push_back({ sm(classDesc) });
            gc.slotMatches.push_back({ sm(classDesc) });
        }
        return gc;
    };
    std::vector<GroupCandidate> cands = {
        mk(0, false, 0),  // c0: BP_Enemy_C
        mk(1, false, 1),  // c1: WidgetBlueprintGeneratedClass
        mk(2, true,  0),  // c2: BP_Enemy_C via the 2nd slot (empty leading slot)
    };

    // Histogram buckets on object-level class: Enemy=2 (c0,c2), Widget=1 (c1).
    auto h = BuildGroupClassHistogram(cands, descs);
    EXPECT("group histogram Enemy(2) then Widget(1)",
           h.size() == 2 && h[0].first == "BP_Enemy_C" && h[0].second == 2
           && h[1].first == "WidgetBlueprintGeneratedClass" && h[1].second == 1);

    // Exclude the widget class -> drops only c1.
    std::unordered_set<std::string> exclW = { "WidgetBlueprintGeneratedClass" };
    auto vW = BuildGroupOrderedView(cands, slots, descs, insts, "", SortKey::ScanOrder, false, exclW);
    EXPECT("group exclude Widget drops c1", vW.size() == 2 && vW[0] == 0 && vW[1] == 2);

    // Exclude the enemy class -> drops c0 AND c2 (incl. the empty-leading-slot one).
    std::unordered_set<std::string> exclE = { "BP_Enemy_C" };
    auto vE = BuildGroupOrderedView(cands, slots, descs, insts, "", SortKey::ScanOrder, false, exclE);
    EXPECT("group exclude Enemy drops c0,c2", vE.size() == 1 && vE[0] == 1);

    // Empty exclude keeps all three.
    auto vAll = BuildGroupOrderedView(cands, slots, descs, insts, "", SortKey::ScanOrder, false, {});
    EXPECT("group empty exclude keeps all", vAll.size() == 3);
}

// V2 (build 950) — scaling smoke for the server-side ordered view. The cap
// ceiling was raised to 1,000,000 now that the UI windows server-side (V3-C);
// confirm a full filter + sort over a set that size stays well under a second
// (it runs on every filter/sort change, debounced 250ms in the UI). Uses
// QueryPerformanceCounter to match the poll-latency bench above.
static void Test_ValueScan_OrderedViewScale() {
    using namespace Radar;
    const int N = 1'000'000;

    std::vector<FieldDescriptor> descs(10);
    for (int i = 0; i < 10; ++i) {
        descs[i].className   = "Class_" + std::to_string(i);
        descs[i].fieldName   = "Field_" + std::to_string(i);
        descs[i].fieldType   = "IntProperty";
        descs[i].fieldOffset = i * 4;
    }
    std::vector<InstanceRecord> insts(1000);
    for (int i = 0; i < 1000; ++i) {
        insts[i].instanceAddr  = 0x10000 + (uintptr_t)i * 0x100;
        insts[i].instanceIndex = i;
        insts[i].instanceName  = "Obj_" + std::to_string(i);
    }
    std::vector<Candidate> cands(N);
    for (int i = 0; i < N; ++i) {
        int32_t v = (int32_t)(((uint32_t)i * 2654435761u) & 0x7FFFFFFF);  // scattered
        std::memcpy(cands[i].prevValue, &v, 4);
        cands[i].addr          = 0x100000 + (uintptr_t)i * 8;
        cands[i].descriptorIdx = (uint32_t)(i % 10);
        cands[i].instanceIdx   = (uint32_t)(i % 1000);
        cands[i].elementIndex  = -1;
    }

    LARGE_INTEGER freq, t0, t1;
    QueryPerformanceFrequency(&freq);

    QueryPerformanceCounter(&t0);
    auto sorted = BuildOrderedView(cands, descs, insts, DataType::Int32, "", SortKey::Value, false);
    QueryPerformanceCounter(&t1);
    double sortMs = (double)(t1.QuadPart - t0.QuadPart) * 1000.0 / freq.QuadPart;
    std::printf("  [bench] BuildOrderedView sort-by-value %d candidates: %.1f ms\n", N, sortMs);
    EXPECT("scale: sort retains all", sorted.size() == (size_t)N);
    bool asc = true;
    for (size_t i = 1; i < sorted.size(); i += 40000) {
        if (DecodeNumericToDouble(DataType::Int32, cands[sorted[i - 1]].prevValue) >
            DecodeNumericToDouble(DataType::Int32, cands[sorted[i]].prevValue)) { asc = false; break; }
    }
    EXPECT("scale: sorted ascending", asc);

    QueryPerformanceCounter(&t0);
    auto filtered = BuildOrderedView(cands, descs, insts, DataType::Int32, "class_3", SortKey::ScanOrder, false);
    QueryPerformanceCounter(&t1);
    double filtMs = (double)(t1.QuadPart - t0.QuadPart) * 1000.0 / freq.QuadPart;
    std::printf("  [bench] BuildOrderedView filter 'class_3' over %d: %.1f ms -> %zu rows\n",
                N, filtMs, filtered.size());
    EXPECT("scale: filter 'class_3' = N/10", filtered.size() == (size_t)(N / 10));

    // Generous bounds catch an O(n^2) regression; the printed numbers are far
    // lower. The filter is allocation-heavier than the sort (lowercases each
    // displayed column) — if it ever creeps past this, the follow-up is the
    // incremental/top-k path noted in todo V2.
    EXPECT("scale: sort under 5s",   sortMs < 5000.0);
    EXPECT("scale: filter under 5s", filtMs < 5000.0);
}

// Macht::IsRipRelativeModRM — the x64 `[rip+disp32]` ModR/M test.
//
// Three of Genau's five hand-rolled RIP decode loops tested `(b & 0x07) == 0x05` only,
// omitting the mod=00 half, so ordinary RBP/R13 addressing was decoded as RIP-relative and
// the int32 read at instr+3 was a disp8 plus the NEXT instruction's bytes. The bytes below
// are real encodings, and the REJECT cases are exactly the ones that used to slip through —
// a test that only asserts the accept case would have passed before the fix too.
static void Test_Macht_IsRipRelativeModRM() {
    // ACCEPT — mod=00, r/m=101. reg field (bits 5:3) is the destination and must not matter.
    EXPECT("48 8D 0D  lea rcx,[rip+d32]  modrm=0x0D", Macht::IsRipRelativeModRM(0x0D));
    EXPECT("48 8B 05  mov rax,[rip+d32]  modrm=0x05", Macht::IsRipRelativeModRM(0x05));
    EXPECT("4C 8B 25  mov r12,[rip+d32]  modrm=0x25", Macht::IsRipRelativeModRM(0x25));
    EXPECT("reg=111 still accepted        modrm=0x3D", Macht::IsRipRelativeModRM(0x3D));

    // REJECT — r/m=101 but mod!=00. These are the regression cases.
    EXPECT("48 8B 4D F8  mov rcx,[rbp-8]   mod=01", !Macht::IsRipRelativeModRM(0x4D));
    EXPECT("48 8D 45 20  lea rax,[rbp+20]  mod=01", !Macht::IsRipRelativeModRM(0x45));
    EXPECT("mov rcx,[rbp+disp32]           mod=10", !Macht::IsRipRelativeModRM(0x8D));
    EXPECT("48 8B C5  mov rax,rbp (reg dir) mod=11", !Macht::IsRipRelativeModRM(0xC5));
    EXPECT("mod=11 r/m=101 high reg        modrm=0xFD", !Macht::IsRipRelativeModRM(0xFD));

    // REJECT — mod=00 but r/m!=101 (never RIP-relative).
    EXPECT("mod=00 r/m=100 -> SIB, [rsp+X] modrm=0x04", !Macht::IsRipRelativeModRM(0x04));
    EXPECT("mod=00 r/m=000 -> [rax]        modrm=0x00", !Macht::IsRipRelativeModRM(0x00));
    EXPECT("mod=00 r/m=011 -> [rbx]        modrm=0x03", !Macht::IsRipRelativeModRM(0x03));

    // Exhaustive: exactly 8 of the 256 ModR/M bytes are RIP-relative — mod=00, r/m=101,
    // reg free across its 8 values. A count pins the predicate against any future rewrite
    // far better than the hand-picked cases above.
    int n = 0;
    for (int b = 0; b < 256; ++b)
        if (Macht::IsRipRelativeModRM(static_cast<uint8_t>(b))) ++n;
    EXPECT("exactly 8/256 ModR/M bytes are RIP-relative", n == 8);
}

// V1a — TSet / TMap sparse-container element geometry. ComputeSetElementStride
// accounts for the TSetElement hash overhead (HashNextId + HashIndex, value
// aligned to 4); ComputeMapValueOffset aligns the TPair value to its natural
// alignment. These drive the slot addresses the container scan reads, so lock
// the math the Address Finder + Value Search both depend on.
static void Test_ValueScan_SparseContainerGeometry() {
    // TSetElement<T> = { T value; int32 HashNextId; int32 HashIndex; }, with
    // value padded up to 4-byte alignment before the two hash ints (+8).
    EXPECT("Set<int32> stride = 4 + 8",        Macht::ComputeSetElementStride(4)  == 12);
    EXPECT("Set<int64> stride = 8 + 8",        Macht::ComputeSetElementStride(8)  == 16);
    EXPECT("Set<uint8> stride pads to 4 + 8",  Macht::ComputeSetElementStride(1)  == 12);
    EXPECT("Set<3-byte> stride pads to 4 + 8", Macht::ComputeSetElementStride(3)  == 12);
    EXPECT("Set<FVector 12> stride = 12 + 8",  Macht::ComputeSetElementStride(12) == 20);

    // TPair<K,V> value offset = K size aligned up to V's natural alignment
    // (guessed from V size: >=8 -> 8, >=4 -> 4, >=2 -> 2, else 1).
    EXPECT("Map<int32,int32> value at +4",    Macht::ComputeMapValueOffset(4, 4)  == 4);
    EXPECT("Map<uint8,int32> value aligns +4", Macht::ComputeMapValueOffset(1, 4) == 4);
    EXPECT("Map<uint8,struct80> value at +8",  Macht::ComputeMapValueOffset(1, 80) == 8);
    EXPECT("Map<int32,uint8> value at +4",     Macht::ComputeMapValueOffset(4, 1) == 4);
    EXPECT("Map<int64,int64> value at +8",     Macht::ComputeMapValueOffset(8, 8) == 8);

    // Explicit value alignment overrides the size guess — REQUIRED for FName
    // (8 bytes but 4-aligned) and FWeakObjectPtr. Map<Enum, FName>: value at +4,
    // NOT +8 (the size guess would corrupt every element). Align comes from
    // Scharf::RequiredAlignment("NameProperty", 8, false) == 4.
    EXPECT("Map<uint8,FName> value at +4 (align 4)",  Macht::ComputeMapValueOffset(1, 8, 4) == 4);
    EXPECT("Map<uint8,FName> WOULD be +8 w/o align",  Macht::ComputeMapValueOffset(1, 8)    == 8);
    EXPECT("Map<uint8,ptr> value at +8 (align 8)",    Macht::ComputeMapValueOffset(1, 8, 8) == 8);

    // ---- Audit #5 D4a/M1 — the COMPOSITE recipe, not the two halves ----
    // Real stride = Align(Align(unpaddedPair, alignof(TPair)) + 8, alignof(TPair)),
    // where alignof(TPair) == max(alignof(Key), alignof(Value)). Every case above
    // happens to land on a multiple of 8, which is exactly why this survived: both
    // helpers were tested separately and the composition never was.
    EXPECT("SanitizeAlign rejects 0",          Macht::SanitizeAlign(0)  == 0);
    EXPECT("SanitizeAlign rejects negative",   Macht::SanitizeAlign(-8) == 0);
    EXPECT("SanitizeAlign rejects non-pow2",   Macht::SanitizeAlign(12) == 0);
    EXPECT("SanitizeAlign rejects > 32",       Macht::SanitizeAlign(64) == 0);
    EXPECT("SanitizeAlign accepts 8",          Macht::SanitizeAlign(8)  == 8);

    // TMap<AActor*,float>: key 8/align 8, value 4/align 4 -> pairAlign 8.
    // Unpadded pair 12; the one-arg form returned 20, the engine strides 24.
    EXPECT("Map<ptr,float> value at +8",
           Macht::ComputeMapValueOffset(8, 4, 4) == 8);
    EXPECT("Map<ptr,float> stride 24 (one-arg form gave 20)",
           Macht::ComputeSetElementStride(8 + 4, 8) == 24);
    EXPECT("Map<ptr,float> one-arg form is still 20",
           Macht::ComputeSetElementStride(8 + 4) == 20);

    // TMap<FString,int32>: unpadded pair 20 -> 32, not 28.
    EXPECT("Map<FString,int32> stride 32",
           Macht::ComputeSetElementStride(16 + 4, 8) == 32);
    // TMap<UObject*,uint8>: unpadded pair 9 -> 24, not 20.
    EXPECT("Map<ptr,uint8> stride 24",
           Macht::ComputeSetElementStride(8 + 1, 8) == 24);

    // ---- Audit #5 D4a/M3 — struct values must use MinAlignment, not a size guess ----
    // TMap<int32,FVector>: FVector is 12 bytes but 4-ALIGNED, so the value really
    // sits at +4 and the pair is 16. The size guess ("8 or more => align 8") put it
    // at +8 with a stride of 28, so even element 0 displayed a wrong vector.
    EXPECT("Map<int32,FVector> value at +4 with real align",
           Macht::ComputeMapValueOffset(4, 12, 4) == 4);
    EXPECT("Map<int32,FVector> stride 24 with real align",
           Macht::ComputeSetElementStride(4 + 12, 4) == 24);
    EXPECT("Map<int32,FVector> size guess WOULD give +8",
           Macht::ComputeMapValueOffset(4, 12) == 8);
    // TMap<int32,FGuid>: 16 bytes, 4-aligned -> value at +4, pair 20, stride 28.
    EXPECT("Map<int32,FGuid> value at +4 with real align",
           Macht::ComputeMapValueOffset(4, 16, 4) == 4);
    EXPECT("Map<int32,FGuid> stride 28 with real align",
           Macht::ComputeSetElementStride(4 + 16, 4) == 28);

    // ---- Regression guard: every PDB-verified geometry in the tree still holds ----
    // Everspace 2 UE 5.4, FSparseDelegateStorage::SparseDelegates (Aura.cpp).
    EXPECT("ES2 outer TSetElement stride 0x60",
           Macht::ComputeSetElementStride(8 + 0x50, 8) == 0x60);
    EXPECT("ES2 inner Map<FName,TSharedPtr> stride 0x20 (non-CPN)",
           Macht::ComputeSetElementStride(Macht::ComputeMapValueOffset(8, 16, 8) + 16, 8) == 0x20);
    EXPECT("ES2 inner Map<FName,TSharedPtr> stride 0x28 (CPN)",
           Macht::ComputeSetElementStride(Macht::ComputeMapValueOffset(16, 16, 8) + 16, 8) == 0x28);
    // UDataTable RowMap TMap<FName, uint8*> — both CPN states.
    EXPECT("DataTable RowMap stride 24 (non-CPN)",
           Macht::ComputeSetElementStride(0x08 + 8, 8) == 24);
    EXPECT("DataTable RowMap stride 32 (CPN)",
           Macht::ComputeSetElementStride(0x10 + 8, 8) == 32);
    // TSet<T> must be byte-identical to the pre-fix behaviour (elemSize is already
    // a multiple of alignof(T), so the default align of 4 is correct there).
    EXPECT("Set<FVector 12> unchanged at 20",  Macht::ComputeSetElementStride(12) == 20);
    EXPECT("Set<ptr 8> align 8 == align 4",
           Macht::ComputeSetElementStride(8, 8) == Macht::ComputeSetElementStride(8));
    EXPECT("Scharf NameProperty(8) align = 4",        Scharf::RequiredAlignment("NameProperty", 8, false) == 4);
    EXPECT("Scharf WeakObjectProperty align = 4",     Scharf::RequiredAlignment("WeakObjectProperty", 8, false) == 4);
}

// ----- main ------------------------------------------------------------------

// Phase A1a — snapshot field selection: pick numeric scalar fields by scope,
// preserving original field indices and resolving each to its concrete width.
static void Test_ValueScan_SelectSnapshotNumericFields() {
    using DT = Radar::DataType;
    // Mixed class layout (field order matters — indices must be preserved).
    const std::vector<std::string> fields = {
        "FloatProperty",   // 0  -> captured (Float) in both scopes
        "BoolProperty",    // 1  -> never (bool excluded)
        "IntProperty",     // 2  -> captured (Int32) in both
        "StrProperty",     // 3  -> never (non-numeric)
        "Int8Property",    // 4  -> NumericAll only (Int8)
        "ByteProperty",    // 5  -> NumericAll only (UInt8)
        "StructProperty",  // 6  -> never
        "Int16Property",   // 7  -> captured (Int16) in both
        "DoubleProperty",  // 8  -> captured (Double) in both
    };

    auto noByte = Radar::SelectSnapshotNumericFields(fields, DT::NumericNoByte);
    EXPECT("NoByte picks 4 fields", noByte.size() == 4);
    if (noByte.size() == 4) {
        EXPECT("NoByte[0] = field 0 Float",  noByte[0].fieldIndex == 0 && noByte[0].dt == DT::Float);
        EXPECT("NoByte[1] = field 2 Int32",  noByte[1].fieldIndex == 2 && noByte[1].dt == DT::Int32);
        EXPECT("NoByte[2] = field 7 Int16",  noByte[2].fieldIndex == 7 && noByte[2].dt == DT::Int16);
        EXPECT("NoByte[3] = field 8 Double", noByte[3].fieldIndex == 8 && noByte[3].dt == DT::Double);
    }

    auto all = Radar::SelectSnapshotNumericFields(fields, DT::NumericAll);
    EXPECT("All picks 6 fields", all.size() == 6);
    if (all.size() == 6) {
        EXPECT("All includes field 4 Int8",  all[2].fieldIndex == 4 && all[2].dt == DT::Int8);
        EXPECT("All includes field 5 UInt8", all[3].fieldIndex == 5 && all[3].dt == DT::UInt8);
    }

    // Non-meta scope captures nothing (snapshot only runs with meta types).
    auto none = Radar::SelectSnapshotNumericFields(fields, DT::Int32);
    EXPECT("Int32 scope captures nothing", none.empty());

    // Empty input is fine.
    auto empty = Radar::SelectSnapshotNumericFields({}, DT::NumericNoByte);
    EXPECT("empty field list -> empty picks", empty.empty());

    // Every captured field must have a non-zero fixed width (SizeOf invariant).
    for (const auto& p : all) {
        EXPECT("captured dt has 1..8 byte width",
               Radar::SizeOf(p.dt) >= 1 && Radar::SizeOf(p.dt) <= 8);
    }
}

// Phase A1b — struct-array inner-key selection.
static void Test_ValueScan_SelectArrayInnerKey() {
    // FCargoSlot { FName ItemID; int32 Quantity; } -> key = ItemID (index 0).
    EXPECT("FName ItemID is the key",
        Radar::SelectArrayInnerKey({"NameProperty", "IntProperty"}, {"ItemID", "Quantity"}) == 0);
    // A plain (keyword-less) FName still beats an integer.
    EXPECT("plain FName beats int",
        Radar::SelectArrayInnerKey({"IntProperty", "NameProperty"}, {"Count", "Slot"}) == 1);
    // No FName -> first integer field.
    EXPECT("first int when no FName",
        Radar::SelectArrayInnerKey({"FloatProperty", "IntProperty", "Int64Property"}, {"X", "Qty", "Big"}) == 1);
    // A keyworded FName is preferred over an earlier plain FName.
    EXPECT("keyworded FName preferred",
        Radar::SelectArrayInnerKey({"NameProperty", "NameProperty"}, {"Display", "RowName"}) == 1);
    // Neither FName nor integer -> -1 (caller uses the element index).
    EXPECT("no key field -> -1",
        Radar::SelectArrayInnerKey({"FloatProperty", "BoolProperty"}, {"X", "Flag"}) == -1);
}

// ----- Denken: native x64 disassembly (Path 2) -------------------------------
//
// Hand-assembled x64 byte buffers exercise the decoder core through a buffer-
// backed MemReader (no live process). The encodings below are standard MS x64;
// `this` is assumed in RCX at entry (Denken's seed), matching the UE exec-thunk
// signature Func(UObject* Context, FFrame&, void*).

namespace {

struct DenkenRegion { uintptr_t base; std::vector<uint8_t> bytes; };

static Denken::MemReader MakeReader(const std::vector<DenkenRegion>* regions) {
    return [regions](uintptr_t addr, uint8_t* out, size_t maxLen) -> size_t {
        for (const auto& r : *regions) {
            if (addr >= r.base && addr < r.base + r.bytes.size()) {
                size_t avail = r.base + r.bytes.size() - addr;
                size_t n = avail < maxLen ? avail : maxLen;
                std::memcpy(out, r.bytes.data() + (addr - r.base), n);
                return n;
            }
        }
        

    return 0;
    };
}

static const Denken::NativeFieldAccess* FindAccess(
    const Denken::NativeAnalysisResult& r, uint32_t off) {
    for (const auto& a : r.accesses) if (a.offset == off) return &a;
    return nullptr;
}

} // namespace

static void Test_Denken_BasicAccesses() {
    // mov [rcx+0x10], eax   89 41 10   write @0x10, base=this -> high-conf
    // mov eax, [rdx+0x20]   8B 42 20   read  @0x20, base=rdx  -> low-conf
    // mov rbx, rcx          48 89 CB   rbx becomes a this-alias
    // mov eax, [rbx+0x08]   8B 43 08   read  @0x08 via alias  -> high-conf
    // ret                   C3
    std::vector<DenkenRegion> regions = {{ 0x140000000ULL, {
        0x89, 0x41, 0x10,
        0x8B, 0x42, 0x20,
        0x48, 0x89, 0xCB,
        0x8B, 0x43, 0x08,
        0xC3,
    }}};
    auto r = Denken::Analyze(0x140000000ULL, MakeReader(&regions));
    EXPECT("basic: ran", r.ok);

    const auto* w10 = FindAccess(r, 0x10);
    EXPECT("basic: @0x10 present",   w10 != nullptr);
    if (w10) {
        EXPECT("basic: @0x10 write",     w10->writeCount == 1);
        EXPECT("basic: @0x10 high-conf", w10->highConfidence);
        EXPECT("basic: @0x10 size 4",    w10->accessSize == 4);
    }
    const auto* r20 = FindAccess(r, 0x20);
    EXPECT("basic: @0x20 present",   r20 != nullptr);
    if (r20) {
        EXPECT("basic: @0x20 read",      r20->writeCount == 0);
        EXPECT("basic: @0x20 low-conf",  !r20->highConfidence);
    }
    const auto* r08 = FindAccess(r, 0x08);
    EXPECT("basic: @0x08 present (alias)", r08 != nullptr);
    if (r08) EXPECT("basic: @0x08 high-conf via rbx alias", r08->highConfidence);
}

static void Test_Denken_ExcludesStackAndZeroDisp() {
    // mov eax, [rbp+0x10]   8B 45 10        rbp-relative (local) -> excluded
    // mov eax, [rsp+0x10]   8B 44 24 10     rsp-relative (local) -> excluded
    // mov [rcx+0x04], eax   89 41 04        valid this write     -> recorded
    // ret                   C3
    std::vector<DenkenRegion> regions = {{ 0x140000000ULL, {
        0x8B, 0x45, 0x10,
        0x8B, 0x44, 0x24, 0x10,
        0x89, 0x41, 0x04,
        0xC3,
    }}};
    auto r = Denken::Analyze(0x140000000ULL, MakeReader(&regions));
    EXPECT("stack: ran", r.ok);
    EXPECT("stack: rbp excluded", FindAccess(r, 0x10) == nullptr);
    const auto* a = FindAccess(r, 0x04);
    EXPECT("stack: this write recorded", a != nullptr && a->writeCount == 1 && a->highConfidence);
}

static void Test_Denken_FollowsCallHandoff() {
    // Thunk @ B0: save this, restore to rcx, call impl, ret.
    //   mov rbx, rcx     48 89 CB
    //   mov rcx, rbx     48 89 D9
    //   call rel32       E8 <rel>      (instr at B0+6, next = B0+11)
    //   ret              C3
    // Impl  @ B1: mov [rcx+0x40], eax ; ret  (write @0x40, this in rcx)
    const uintptr_t B0 = 0x140000000ULL;
    const uintptr_t B1 = 0x140001000ULL;
    const int32_t rel = static_cast<int32_t>(B1 - (B0 + 11));
    std::vector<uint8_t> thunk = {
        0x48, 0x89, 0xCB,
        0x48, 0x89, 0xD9,
        0xE8,
        static_cast<uint8_t>(rel & 0xFF),
        static_cast<uint8_t>((rel >> 8) & 0xFF),
        static_cast<uint8_t>((rel >> 16) & 0xFF),
        static_cast<uint8_t>((rel >> 24) & 0xFF),
        0xC3,
    };
    std::vector<DenkenRegion> regions = {
        { B0, thunk },
        { B1, { 0x89, 0x41, 0x40, 0xC3 } },
    };
    auto r = Denken::Analyze(B0, MakeReader(&regions));
    EXPECT("follow: ran", r.ok);
    EXPECT("follow: followed 1 call", r.callsFollowed == 1);
    const auto* a = FindAccess(r, 0x40);
    EXPECT("follow: impl write @0x40 found", a != nullptr);
    if (a) EXPECT("follow: @0x40 high-conf in impl", a->highConfidence && a->writeCount == 1);
}

static void Test_Denken_DoesNotFollowNonThisCall() {
    // call rel32 with a NON-this rcx (rcx was clobbered by a load) must NOT
    // follow. Sequence: mov rcx, [rdx] (clobbers rcx) ; call impl ; ret.
    //   mov rcx, [rdx]   48 8B 0A
    //   call rel32       E8 <rel>     (instr at B0+3, next = B0+8)
    //   ret              C3
    const uintptr_t B0 = 0x140000000ULL;
    const uintptr_t B1 = 0x140001000ULL;
    const int32_t rel = static_cast<int32_t>(B1 - (B0 + 8));
    std::vector<uint8_t> thunk = {
        0x48, 0x8B, 0x0A,
        0xE8,
        static_cast<uint8_t>(rel & 0xFF),
        static_cast<uint8_t>((rel >> 8) & 0xFF),
        static_cast<uint8_t>((rel >> 16) & 0xFF),
        static_cast<uint8_t>((rel >> 24) & 0xFF),
        0xC3,
    };
    std::vector<DenkenRegion> regions = {
        { B0, thunk },
        { B1, { 0x89, 0x41, 0x40, 0xC3 } },   // would write @0x40 if (wrongly) followed
    };
    auto r = Denken::Analyze(B0, MakeReader(&regions));
    EXPECT("no-follow: ran", r.ok);
    EXPECT("no-follow: did not follow", r.callsFollowed == 0);
    EXPECT("no-follow: impl access not recorded", FindAccess(r, 0x40) == nullptr);
}

static void Test_Denken_TerminatesAndGuards() {
    // Bare ret -> ok, zero accesses, no crash.
    std::vector<DenkenRegion> ret = {{ 0x140000000ULL, { 0xC3 } }};
    auto r0 = Denken::Analyze(0x140000000ULL, MakeReader(&ret));
    EXPECT("guard: bare ret ok", r0.ok && r0.accesses.empty());

    // Unreadable start address -> not ok (reader returns 0).
    std::vector<DenkenRegion> empty;
    auto r1 = Denken::Analyze(0x140000000ULL, MakeReader(&empty));
    EXPECT("guard: unreadable start -> !ok", !r1.ok);

    // Null start / null reader -> not ok.
    auto r2 = Denken::Analyze(0, MakeReader(&ret));
    EXPECT("guard: null addr -> !ok", !r2.ok);
}

// ----- Lineal (UE5.7+ packed FUObjectItem reconstruction) ----------------
//
// No live game uses this layout yet, so the reconstruction MATH is the only
// thing verifiable today. These tests assert the Encode/Reconstruct round trip
// (any 8-aligned pointer survives a split-and-rebuild regardless of flag bits)
// plus the calibration-knob edges (alignBits / ptrMaskBits actually matter).

static void Test_Packed_RoundTrip_Basic() {
    Lineal::PackedConsts c;  // defaults: alignBits=3, ptrMask=0x3FFF
    const uintptr_t ptrs[] = {
        0x0000000140001000ULL,   // typical module-region pointer
        0x000001F809E08FB0ULL,   // typical heap pointer (8-aligned)
        0x0000700000000008ULL,   // high heap, minimal low bits
        0x0000000000000008ULL,   // smallest non-null aligned value
    };
    for (uintptr_t obj : ptrs) {
        uint64_t flags = 0; uint32_t low = 0;
        Lineal::Encode(obj, c, flags, low);
        EXPECT_EQ_U64("round-trip default consts", Lineal::Reconstruct(flags, low, c), obj);
    }
}

static void Test_Packed_RoundTrip_HighBits() {
    Lineal::PackedConsts c;
    // Top of the 47-bit x64 user-mode range, 8-aligned — proves the 14-bit
    // ptrMask captures every high pointer bit a real UObject* can carry.
    const uintptr_t obj = 0x00007FFFFFFFFFF8ULL;
    uint64_t flags = 0; uint32_t low = 0;
    Lineal::Encode(obj, c, flags, low);
    EXPECT_EQ_U64("round-trip 47-bit high pointer", Lineal::Reconstruct(flags, low, c), obj);
}

static void Test_Packed_ZeroAndNull() {
    Lineal::PackedConsts c;
    // ptrLow == 0 must reconstruct to 0 (the "empty/null slot" contract the
    // object walk relies on), regardless of any flag bits sitting in the high dword.
    EXPECT_EQ_U64("ptrLow=0 -> null", Lineal::Reconstruct(0xFFFFFFFF00000000ULL, 0, c), 0ULL);
    EXPECT_EQ_U64("all-zero -> null", Lineal::Reconstruct(0, 0, c), 0ULL);
}

static void Test_Packed_FlagsDoNotLeak() {
    Lineal::PackedConsts c;
    const uintptr_t obj = 0x000001F809E08FB0ULL;
    uint64_t flags = 0; uint32_t low = 0;
    // Seed the low 32 bits (real flags/refcount) with noise — they must NOT
    // bleed into the reconstructed pointer.
    Lineal::Encode(obj, c, flags, low, /*flagsExtra=*/0xDEADBEEFull);
    EXPECT_EQ_U64("flags in low dword do not corrupt ptr",
                  Lineal::Reconstruct(flags, low, c), obj);
    // And confirm the low dword actually carried the seeded flags (so the test
    // proves isolation, not that flagsExtra was silently dropped).
    EXPECT_EQ_U64("flagsExtra preserved in low dword", flags & 0xFFFFFFFFull, 0xDEADBEEFull);
}

static void Test_Packed_AlignBitsKnob() {
    // A non-default alignBits changes the encoding; round trip must still hold
    // when Encode and Reconstruct share the same consts.
    Lineal::PackedConsts c4; c4.alignBits = 4;  // hypothetical 16-byte alignment
    const uintptr_t obj = 0x000001F809E08F00ULL;     // 16-aligned
    uint64_t flags = 0; uint32_t low = 0;
    Lineal::Encode(obj, c4, flags, low);
    EXPECT_EQ_U64("round-trip alignBits=4", Lineal::Reconstruct(flags, low, c4), obj);

    // Decoding the SAME fields with the default alignBits=3 must yield a
    // DIFFERENT pointer — i.e. the knob is load-bearing, not ignored.
    Lineal::PackedConsts c3;
    EXPECT("alignBits mismatch diverges",
           Lineal::Reconstruct(flags, low, c3) != obj);
}

static void Test_Packed_PtrMaskKnob() {
    // Widening ptrMask must not break a pointer whose high bits already fit the
    // narrower mask (round trip stable), but a deliberately-too-narrow mask must
    // drop high bits — guarding against the constant being ignored.
    const uintptr_t obj = 0x00007FFFFFFFFFF8ULL;

    Lineal::PackedConsts wide; wide.ptrMaskBits = 0x7FFFull;  // 15 bits
    uint64_t f = 0; uint32_t l = 0;
    Lineal::Encode(obj, wide, f, l);
    EXPECT_EQ_U64("round-trip wider mask", Lineal::Reconstruct(f, l, wide), obj);

    Lineal::PackedConsts narrow; narrow.ptrMaskBits = 0x00FFull;  // 8 bits — too narrow
    EXPECT("too-narrow mask loses high bits",
           Lineal::Reconstruct(f, l, narrow) != obj);
}

// ----- GraphPath::BfsShortestObjectPath (Locate in GWorld) -----------------
//
// The BFS core is pure (no live memory) so the search invariants — shortest
// path, cycle safety, depth bound, abort, visited cap, reconstruction — are
// exercised here against an in-memory mock graph. The live adjacency adapter
// (EnumerateOutgoingObjectPtrs over real GObjects) is integration-only.

namespace {

struct MockEdge {
    uintptr_t   to;
    int32_t     off;
    std::string name;
    std::string type;
    std::string inner;
    int32_t     elem;
    int32_t     stride;
    int32_t     valOff;
};

struct MockGraph {
    std::unordered_map<uintptr_t, std::vector<MockEdge>> adj;
    void add(uintptr_t from, uintptr_t to, int32_t off = 0,
             std::string name = "f", std::string type = "ObjectProperty",
             std::string inner = "", int32_t elem = -1,
             int32_t stride = 0, int32_t valOff = 0) {
        adj[from].push_back({to, off, std::move(name), std::move(type), std::move(inner),
                             elem, stride, valOff});
    }
};

// Build a neighbor functor over a mock graph (generic-lambda compatible).
#define MOCK_NB(g) [&](uintptr_t node, auto&& emit) {                                       \
        auto it = (g).adj.find(node);                                                       \
        if (it == (g).adj.end()) return;                                                    \
        for (const auto& e : it->second)                                                    \
            if (emit(e.to, e.off, e.name, e.type, e.inner, e.elem, e.stride, e.valOff)) return; \
    }

static auto kNeverAbort = [] { return false; };

} // namespace

static void Test_GraphPath_DirectChild() {
    MockGraph g;
    g.add(0x1000, 0x2000, 0x40, "Target");
    auto r = Aura::BfsShortestObjectPath(0x1000ull, 0x2000ull, 5, 1000000,
                                         MOCK_NB(g), kNeverAbort);
    EXPECT("direct child found", r.found);
    EXPECT("direct child status ok", r.status == "ok");
    EXPECT("direct child 1 hop", r.depthReached == 1);
    EXPECT("direct child step toObj", r.steps.size() == 1 && r.steps[0].toObj == 0x2000ull);
    EXPECT("direct child step offset", r.steps.size() == 1 && r.steps[0].fieldOffset == 0x40);
    EXPECT("direct child step name", r.steps.size() == 1 && r.steps[0].fieldName == "Target");
}

static void Test_GraphPath_RootEqualsTarget() {
    MockGraph g;
    auto r = Aura::BfsShortestObjectPath(0x1000ull, 0x1000ull, 5, 1000000,
                                         MOCK_NB(g), kNeverAbort);
    EXPECT("root==target found", r.found);
    EXPECT("root==target no steps", r.steps.empty());
    EXPECT("root==target status ok", r.status == "ok");
}

static void Test_GraphPath_ShortestAmongTwo() {
    // root -> A -> B -> target  (3 hops)   and   root -> C -> target (2 hops)
    // BFS must return the 2-hop path regardless of edge insertion order.
    MockGraph g;
    g.add(0x1000, 0x2000, 1, "A");      // long branch first
    g.add(0x2000, 0x3000, 2, "B");
    g.add(0x3000, 0x9000, 3, "target_via_B");
    g.add(0x1000, 0x4000, 4, "C");      // short branch
    g.add(0x4000, 0x9000, 5, "target_via_C");
    auto r = Aura::BfsShortestObjectPath(0x1000ull, 0x9000ull, 5, 1000000,
                                         MOCK_NB(g), kNeverAbort);
    EXPECT("shortest found", r.found);
    EXPECT("shortest is 2 hops", r.depthReached == 2);
    EXPECT("shortest goes via C", r.steps.size() == 2 && r.steps[0].toObj == 0x4000ull);
    EXPECT("shortest last edge name", r.steps.size() == 2 && r.steps[1].fieldName == "target_via_C");
}

static void Test_GraphPath_Cycle() {
    // root -> A -> B -> A (cycle), B -> target. Must terminate + find target.
    MockGraph g;
    g.add(0x1000, 0x2000, 1, "A");
    g.add(0x2000, 0x3000, 2, "B");
    g.add(0x3000, 0x2000, 3, "back_to_A");   // cycle edge
    g.add(0x3000, 0x9000, 4, "target");
    auto r = Aura::BfsShortestObjectPath(0x1000ull, 0x9000ull, 10, 1000000,
                                         MOCK_NB(g), kNeverAbort);
    EXPECT("cycle terminates + finds", r.found);
    EXPECT("cycle path 3 hops", r.depthReached == 3);
    EXPECT("cycle visited bounded", r.visited == 4);  // root, A, B, target
}

static void Test_GraphPath_DepthBound() {
    // Linear chain root(0) -> n1 -> n2 -> n3 -> n4 -> n5 -> n6(target at depth 6)
    MockGraph g;
    uintptr_t prev = 0x1000;
    for (int i = 1; i <= 6; ++i) {
        uintptr_t cur = 0x1000 + static_cast<uintptr_t>(i) * 0x1000;
        g.add(prev, cur, i, "n" + std::to_string(i));
        prev = cur;
    }
    uintptr_t target = 0x1000 + 6 * 0x1000;

    auto tooShallow = Aura::BfsShortestObjectPath(0x1000ull, target, 5, 1000000,
                                                  MOCK_NB(g), kNeverAbort);
    EXPECT("depth 5 cannot reach depth-6 target", !tooShallow.found);
    EXPECT("depth 5 status not_reachable", tooShallow.status == "not_reachable");

    auto deepEnough = Aura::BfsShortestObjectPath(0x1000ull, target, 6, 1000000,
                                                  MOCK_NB(g), kNeverAbort);
    EXPECT("depth 6 reaches depth-6 target", deepEnough.found);
    EXPECT("depth 6 path is 6 hops", deepEnough.depthReached == 6);
}

static void Test_GraphPath_Unreachable() {
    MockGraph g;
    g.add(0x1000, 0x2000, 1, "A");   // target 0x9000 not in graph
    auto r = Aura::BfsShortestObjectPath(0x1000ull, 0x9000ull, 5, 1000000,
                                         MOCK_NB(g), kNeverAbort);
    EXPECT("unreachable not found", !r.found);
    EXPECT("unreachable status", r.status == "not_reachable");
}

static void Test_GraphPath_Abort() {
    MockGraph g;
    g.add(0x1000, 0x2000, 1, "A");
    g.add(0x2000, 0x9000, 2, "target");
    auto alwaysAbort = [] { return true; };
    auto r = Aura::BfsShortestObjectPath(0x1000ull, 0x9000ull, 5, 1000000,
                                         MOCK_NB(g), alwaysAbort);
    EXPECT("abort not found", !r.found);
    EXPECT("abort flag set", r.aborted);
    EXPECT("abort status", r.status == "aborted");
}

static void Test_GraphPath_VisitedCap() {
    // root -> A -> B -> target, cap visited at 2 → cannot discover B/target.
    MockGraph g;
    g.add(0x1000, 0x2000, 1, "A");
    g.add(0x2000, 0x3000, 2, "B");
    g.add(0x3000, 0x9000, 3, "target");
    auto r = Aura::BfsShortestObjectPath(0x1000ull, 0x9000ull, 10, /*maxVisited=*/2,
                                         MOCK_NB(g), kNeverAbort);
    EXPECT("cap not found", !r.found);
    EXPECT("cap status", r.status == "visited_cap");
}

static void Test_GraphPath_ContainerEdgePreserved() {
    // An array-element edge must round-trip its type + element index into the step.
    MockGraph g;
    g.add(0x1000, 0x2000, 0x80, "Actors", "ArrayProperty", "ObjectProperty", 5234);
    auto r = Aura::BfsShortestObjectPath(0x1000ull, 0x2000ull, 5, 1000000,
                                         MOCK_NB(g), kNeverAbort);
    EXPECT("container edge found", r.found && r.steps.size() == 1);
    EXPECT("container edge type", r.steps.size() == 1 && r.steps[0].fieldType == "ArrayProperty");
    EXPECT("container edge inner", r.steps.size() == 1 && r.steps[0].innerType == "ObjectProperty");
    EXPECT("container edge element index", r.steps.size() == 1 && r.steps[0].elementIndex == 5234);
}

static void Test_GraphPath_MapSetElementGeometryRoundTrip() {
    // A Map-value element edge must round-trip its element stride + within-pair value
    // offset into the step so the UI can split it into container + element CE derefs.
    MockGraph g;
    // MapProperty, sparse slot 3, pairStride=0x18, valueOffset=0x10 (the .Value edge).
    g.add(0x1000, 0x2000, 0xC0, "Attrs.Value", "MapProperty", "ObjectProperty", 3, 0x18, 0x10);
    auto r = Aura::BfsShortestObjectPath(0x1000ull, 0x2000ull, 5, 1000000,
                                         MOCK_NB(g), kNeverAbort);
    EXPECT("map edge found", r.found && r.steps.size() == 1);
    EXPECT("map edge type", r.steps.size() == 1 && r.steps[0].fieldType == "MapProperty");
    EXPECT("map edge element index", r.steps.size() == 1 && r.steps[0].elementIndex == 3);
    EXPECT("map edge stride", r.steps.size() == 1 && r.steps[0].elemStride == 0x18);
    EXPECT("map edge value offset", r.steps.size() == 1 && r.steps[0].elemValueOffset == 0x10);
    // A direct (non-element) edge leaves the geometry zeroed.
    MockGraph g2;
    g2.add(0x1000, 0x2000, 0x40, "Direct");
    auto r2 = Aura::BfsShortestObjectPath(0x1000ull, 0x2000ull, 5, 1000000,
                                          MOCK_NB(g2), kNeverAbort);
    EXPECT("direct edge zero stride", r2.steps.size() == 1 && r2.steps[0].elemStride == 0
                                       && r2.steps[0].elemValueOffset == 0);
}

static void Test_GraphPath_Reconstruction() {
    // GWorld(0x1000) -> Level(0x2000) -> Actor(0x3000) -> Comp(0x4000=target)
    MockGraph g;
    g.add(0x1000, 0x2000, 0x30, "PersistentLevel");
    g.add(0x2000, 0x3000, 0x98, "Actors", "ArrayProperty", "ObjectProperty", 12);
    g.add(0x3000, 0x4000, 0x140, "RootComponent");
    auto r = Aura::BfsShortestObjectPath(0x1000ull, 0x4000ull, 5, 1000000,
                                         MOCK_NB(g), kNeverAbort);
    EXPECT("reconstruct found", r.found && r.steps.size() == 3);
    if (r.steps.size() == 3) {
        EXPECT("step0 from=root", r.steps[0].fromObj == 0x1000ull);
        EXPECT("step0 to=Level",  r.steps[0].toObj == 0x2000ull && r.steps[0].fieldName == "PersistentLevel");
        EXPECT("step1 to=Actor",  r.steps[1].toObj == 0x3000ull && r.steps[1].elementIndex == 12);
        EXPECT("step2 to=target", r.steps[2].toObj == 0x4000ull && r.steps[2].fieldName == "RootComponent");
        EXPECT("steps are ordered root->target",
               r.steps[0].fromObj == 0x1000ull &&
               r.steps[1].fromObj == r.steps[0].toObj &&
               r.steps[2].fromObj == r.steps[1].toObj);
    }
}

// ----- Solitar::ApplyBoolBit (GodMode FBoolProperty bit write) ----------------
// The critical correctness property: a single-bit read-modify-write must leave
// the other 7 bitfields packed in the same byte untouched (GodMode ON clears
// bCanBeDamaged; OFF restores it).

static void Test_Solitar_ApplyBoolBit() {
    using Solitar::ApplyBoolBit;
    // Set a bit.
    EXPECT_EQ_U64("set bit into 0x00",          ApplyBoolBit(0x00, 0x04, true),  0x04);
    EXPECT_EQ_U64("set already-set bit",        ApplyBoolBit(0x04, 0x04, true),  0x04);
    EXPECT_EQ_U64("set bit preserves others",   ApplyBoolBit(0xFB, 0x04, true),  0xFF);
    // Clear a bit (GodMode ON ⇒ bCanBeDamaged FALSE).
    EXPECT_EQ_U64("clear bit from 0xFF",        ApplyBoolBit(0xFF, 0x04, false), 0xFB);
    EXPECT_EQ_U64("clear already-clear bit",    ApplyBoolBit(0x00, 0x04, false), 0x00);
    EXPECT_EQ_U64("clear bit preserves others", ApplyBoolBit(0x05, 0x04, false), 0x01);
    // Every single-bit mask: set/clear touches only that bit; idempotent.
    for (int i = 0; i < 8; ++i) {
        uint8_t mask = static_cast<uint8_t>(1u << i);
        EXPECT_EQ_U64("clear one bit of 0xFF leaves ~mask",
                      ApplyBoolBit(0xFF, mask, false), static_cast<uint8_t>(0xFF & ~mask));
        EXPECT_EQ_U64("set one bit of 0x00 leaves mask",
                      ApplyBoolBit(0x00, mask, true), mask);
        uint8_t setOnce = ApplyBoolBit(0xA5, mask, true);
        EXPECT_EQ_U64("idempotent set",   ApplyBoolBit(setOnce, mask, true),  setOnce);
        uint8_t clrOnce = ApplyBoolBit(0xA5, mask, false);
        EXPECT_EQ_U64("idempotent clear", ApplyBoolBit(clrOnce, mask, false), clrOnce);
    }
}

// ----- Solitar::MatchProtectionBool (T2 generic invincibility-flag matcher) ---
// Polarity is the bug-prone part: a wrong value would ENABLE damage. Lock the
// keyword set + protect-value for each known flag, and confirm unrelated /
// ambiguous names (deal-damage, visibility) are NOT matched.

static void Test_Solitar_MatchProtectionBool() {
    bool p = false;
    // Positive (protect = true): set the flag ON for godmode.
    EXPECT("binvincible matched",  Solitar::MatchProtectionBool("binvincible", p));
    EXPECT("binvincible protect=true", p == true);
    EXPECT("bisinvulnerable matched", Solitar::MatchProtectionBool("bisinvulnerable", p));
    EXPECT("invulnerable protect=true (NOT read as vulnerable)", p == true);
    EXPECT("bisimmortal matched",  Solitar::MatchProtectionBool("bisimmortal", p));
    EXPECT("immortal protect=true", p == true);
    EXPECT("bmuteki matched",      Solitar::MatchProtectionBool("bmuteki", p));
    EXPECT("muteki protect=true",  p == true);
    EXPECT("bdamageimmune matched", Solitar::MatchProtectionBool("bdamageimmune", p));
    EXPECT("damageimmune protect=true", p == true);
    // Negative (protect = false): clear the flag for godmode.
    EXPECT("bcanbedamaged matched", Solitar::MatchProtectionBool("bcanbedamaged", p));
    EXPECT("canbedamaged protect=false", p == false);
    EXPECT("bcantakedamage matched", Solitar::MatchProtectionBool("bcantakedamage", p));
    EXPECT("cantakedamage protect=false", p == false);
    // Must NOT match: ambiguous deal-damage flags + unrelated bools.
    EXPECT("bcandamage NOT matched (deal-damage)", !Solitar::MatchProtectionBool("bcandamage", p));
    EXPECT("bnodamage NOT matched (ambiguous)",    !Solitar::MatchProtectionBool("bnodamage", p));
    EXPECT("bhidden NOT matched",   !Solitar::MatchProtectionBool("bhidden", p));
    EXPECT("bvisible NOT matched",  !Solitar::MatchProtectionBool("bvisible", p));
    EXPECT("breplicates NOT matched", !Solitar::MatchProtectionBool("breplicates", p));
}

// ----- Solide::MatchStealthField (stealth/detection-meter field scorer) --------
// A positive stem must score > 0; a config/limit name that merely CONTAINS a stem
// must be demoted; unrelated names must score 0.

static void Test_Solide_MatchStealthField() {
    using Solide::MatchStealthField;
    // Positive: detection/stealth vocabulary scores > 0.
    EXPECT("visibility scores",   MatchStealthField("visibility") > 0);
    EXPECT("detectionlevel scores", MatchStealthField("detectionlevel") > 0);
    EXPECT("noiselevel scores",   MatchStealthField("noiselevel") > 0);
    EXPECT("awareness scores",    MatchStealthField("awareness") > 0);
    EXPECT("stealthmeter scores", MatchStealthField("stealthmeter") > 0);
    EXPECT("concealment scores",  MatchStealthField("concealment") > 0);
    EXPECT("suspicion scores",    MatchStealthField("suspicion") > 0);
    // Negative demotion: a config/limit name ranks below the bare meter.
    EXPECT("maxvisibility demoted below visibility",
           MatchStealthField("maxvisibility") < MatchStealthField("visibility"));
    EXPECT("detectionradius demoted below detectionlevel",
           MatchStealthField("detectionradius") < MatchStealthField("detectionlevel"));
    // Unrelated names: no positive signal.
    EXPECT("health scores 0",   MatchStealthField("health") == 0);
    EXPECT("velocity scores 0", MatchStealthField("velocity") == 0);
    EXPECT("position scores 0", MatchStealthField("position") == 0);
}

// ----- Solide::IntWidthOf / IntRangeOf (held-integer width + SIGN, AF8) -------
// The bug this pins: Solide read Int8Property as UNSIGNED while writing it as
// signed, so the re-assert worker's `read != target` drift check could never be
// satisfied for a negative hold — it rewrote the same byte every tick, forever,
// and told the user the game was fighting it. Nothing compiles Solide.cpp, so the
// rule lives in Solide.h and is tested here.
static void Test_Solide_IntWidthAndRange() {
    using Solide::IntWidthOf;
    using Solide::IntRangeOf;

    // THE defect: Int8Property is SIGNED and one byte wide.
    EXPECT("Int8Property is 1 byte",  IntWidthOf("Int8Property").bytes == 1);
    EXPECT("Int8Property is SIGNED",  IntWidthOf("Int8Property").isSigned);
    // Its unsigned same-width siblings must NOT be dragged along by the fix.
    EXPECT("ByteProperty is 1 byte",  IntWidthOf("ByteProperty").bytes == 1);
    EXPECT("ByteProperty is unsigned", !IntWidthOf("ByteProperty").isSigned);
    EXPECT("UInt8Property is unsigned", !IntWidthOf("UInt8Property").isSigned);

    EXPECT("IntProperty is 4 signed",
           IntWidthOf("IntProperty").bytes == 4 && IntWidthOf("IntProperty").isSigned);
    EXPECT("Int64Property is 8 signed",
           IntWidthOf("Int64Property").bytes == 8 && IntWidthOf("Int64Property").isSigned);

    // Anything else must report "not mine" rather than defaulting to a byte — the
    // old code's fall-through is what made an unknown type a silent 1-byte write.
    EXPECT("FloatProperty is not an int",  IntWidthOf("FloatProperty").bytes == 0);
    EXPECT("UInt32Property is not held",   IntWidthOf("UInt32Property").bytes == 0);
    EXPECT("empty type is not held",       IntWidthOf("").bytes == 0);

    double lo = 0, hi = 0;
    IntRangeOf(IntWidthOf("Int8Property"), lo, hi);
    EXPECT("int8 range is -128..127", lo == -128.0 && hi == 127.0);
    // -5 must be INSIDE the signed range (the value that never converged) and
    // 200 must be OUTSIDE it (the mirror case: it stored as -56 and the unsigned
    // read agreed with the target, so the hold looked converged while the game
    // saw a different number).
    EXPECT("-5 holdable in int8", -5.0 >= lo && -5.0 <= hi);
    EXPECT("200 NOT holdable in int8", !(200.0 >= lo && 200.0 <= hi));

    IntRangeOf(IntWidthOf("ByteProperty"), lo, hi);
    EXPECT("uint8 range is 0..255", lo == 0.0 && hi == 255.0);
    EXPECT("-5 NOT holdable in uint8", !(-5.0 >= lo && -5.0 <= hi));
    EXPECT("200 holdable in uint8", 200.0 >= lo && 200.0 <= hi);

    IntRangeOf(IntWidthOf("IntProperty"), lo, hi);
    EXPECT("int32 range ends", lo == -2147483648.0 && hi == 2147483647.0);

    // A non-held type must produce an EMPTY range, so a caller that forgets to
    // check `bytes` rejects rather than writing a wrong width.
    IntRangeOf(IntWidthOf("FloatProperty"), lo, hi);
    EXPECT("non-int range is empty", lo == 0.0 && hi == 0.0);

    // The TYPE GATE and the width table are one list, not two that must agree — the
    // shape of the original defect. A type IsIntType admits but IntWidthOf does not
    // know would be accepted by Force and then fail every read and write silently.
    EXPECT("gate admits Int8Property",    Solide::IsIntType("Int8Property"));
    EXPECT("gate admits ByteProperty",    Solide::IsIntType("ByteProperty"));
    EXPECT("gate admits UInt8Property",   Solide::IsIntType("UInt8Property"));
    EXPECT("gate admits IntProperty",     Solide::IsIntType("IntProperty"));
    EXPECT("gate admits Int64Property",   Solide::IsIntType("Int64Property"));
    EXPECT("gate rejects FloatProperty",  !Solide::IsIntType("FloatProperty"));
    EXPECT("gate rejects UInt32Property", !Solide::IsIntType("UInt32Property"));
    EXPECT("gate rejects StrProperty",    !Solide::IsIntType("StrProperty"));
}

// ----- Neu: UEnum::Names layout (legacy TArray vs UE5.6+ FNameData) -----------
// Synthetic memory: register buffers at chosen virtual addresses; the read
// callback serves bytes from registered ranges and FAILS for any unmapped
// address — exactly mirroring Macht::ReadSafe on game memory, so the parser's
// pointer-readability checks (the format disambiguator) are exercised WITHOUT a
// live process or FNamePool. Names are stored as raw int32 FName comparison
// indices (string resolution is Serie's job, not Neu's).
struct NeuFakeMem {
    std::vector<std::pair<uintptr_t, std::vector<uint8_t>>> regions;
    void Put(uintptr_t addr, const void* data, size_t n) {
        std::vector<uint8_t> b(n);
        std::memcpy(b.data(), data, n);
        regions.emplace_back(addr, std::move(b));
    }
    bool Read(uintptr_t a, void* o, size_t n) const {
        for (const auto& r : regions) {
            if (a >= r.first && (a - r.first) + n <= r.second.size()) {
                std::memcpy(o, r.second.data() + (a - r.first), n);
                return true;
            }
        }
        return false;
    }
};

// Legacy TArray<TPair<FName,int64>> header (padded to 0x20 so +0x10 is readable,
// like a real UEnum where CppForm/EnumFlags follow the array) + interleaved data.
static void NeuPutLegacy(NeuFakeMem& fm, uintptr_t region, uintptr_t dataAddr,
                         const std::vector<std::pair<int32_t,int64_t>>& es, int fnameStride) {
    const size_t entryStride = static_cast<size_t>(fnameStride) + 8;
    std::vector<uint8_t> data(es.size() * entryStride, 0);
    for (size_t i = 0; i < es.size(); ++i) {
        std::memcpy(&data[i*entryStride], &es[i].first, 4);                            // FName idx @ +0
        std::memcpy(&data[i*entryStride + fnameStride], &es[i].second, 8);             // int64 value @ +stride
    }
    fm.Put(dataAddr, data.data(), data.size());
    uint8_t hdr[0x20] = {};
    uint64_t dataU = dataAddr;       std::memcpy(hdr + 0, &dataU, 8);
    int32_t num = (int32_t)es.size(); std::memcpy(hdr + 8, &num, 4);
    int32_t maxN = (int32_t)es.size(); std::memcpy(hdr + 12, &maxN, 4);  // ArrayMax
    fm.Put(region, hdr, sizeof(hdr));
}

// UE5.6+ FNameData {tagged FName*, tagged int64*, int32 NumValues} + parallel arrays.
static void NeuPutFNameData(NeuFakeMem& fm, uintptr_t region, uintptr_t namesAddr,
                            uintptr_t valuesAddr, const std::vector<std::pair<int32_t,int64_t>>& es,
                            int fnameStride, bool tagged) {
    std::vector<uint8_t> names(es.size() * fnameStride, 0);
    std::vector<uint8_t> vals(es.size() * 8, 0);
    for (size_t i = 0; i < es.size(); ++i) {
        std::memcpy(&names[i*fnameStride], &es[i].first, 4);  // FName idx at start of each FName slot
        std::memcpy(&vals[i*8], &es[i].second, 8);
    }
    fm.Put(namesAddr, names.data(), names.size());
    fm.Put(valuesAddr, vals.data(), vals.size());
    uint8_t hdr[0x18] = {};
    uint64_t tn = static_cast<uint64_t>(namesAddr)  | (tagged ? 1ull : 0ull);
    uint64_t tv = static_cast<uint64_t>(valuesAddr) | (tagged ? 1ull : 0ull);
    int32_t num = (int32_t)es.size();
    std::memcpy(hdr + 0,  &tn, 8);
    std::memcpy(hdr + 8,  &tv, 8);
    std::memcpy(hdr + 16, &num, 4);
    fm.Put(region, hdr, sizeof(hdr));
}

static void Test_Neu_Legacy_Basic() {
    NeuFakeMem fm;
    std::vector<std::pair<int32_t,int64_t>> es = {{10,0},{20,1},{30,2},{40,3}};
    NeuPutLegacy(fm, 0x10000000, 0x20000000, es, 8);
    auto rd = [&](uintptr_t a, void* o, size_t n){ return fm.Read(a, o, n); };

    Neu::EnumNamesLayout L;
    EXPECT("legacy detect",  Neu::DetectLayout(rd, 0x10000000, 8, 16384, L));
    EXPECT("legacy format",  L.format == Neu::EnumNamesFormat::Legacy);
    EXPECT_EQ_U64("legacy count", L.count, 4);
    int32_t idx = 0; int64_t v = 0;
    EXPECT("legacy entry0",  Neu::ReadEntry(rd, L, 0, idx, v));
    EXPECT_EQ_U64("legacy idx0", idx, 10);  EXPECT_EQ_U64("legacy val0", v, 0);
    Neu::ReadEntry(rd, L, 3, idx, v);
    EXPECT_EQ_U64("legacy idx3", idx, 40);  EXPECT_EQ_U64("legacy val3", v, 3);
    // BuildLayout with the known format (what the live reader uses) matches.
    Neu::EnumNamesLayout L2;
    EXPECT("legacy build", Neu::BuildLayout(rd, 0x10000000, Neu::EnumNamesFormat::Legacy, 8, 16384, L2));
    EXPECT_EQ_U64("legacy build count", L2.count, 4);
}

static void Test_Neu_Legacy_CasePreserving() {
    NeuFakeMem fm;
    std::vector<std::pair<int32_t,int64_t>> es = {{5,0},{6,1},{7,2}};
    NeuPutLegacy(fm, 0x10000000, 0x20000000, es, 0x10);  // FName=16 -> stride 24, value @ +16
    auto rd = [&](uintptr_t a, void* o, size_t n){ return fm.Read(a, o, n); };
    Neu::EnumNamesLayout L;
    EXPECT("legacy CPN detect", Neu::DetectLayout(rd, 0x10000000, 0x10, 16384, L));
    int32_t idx = 0; int64_t v = 0;
    Neu::ReadEntry(rd, L, 2, idx, v);
    EXPECT_EQ_U64("legacy CPN idx2", idx, 7);  EXPECT_EQ_U64("legacy CPN val2", v, 2);
}

static void Test_Neu_FNameData_Basic() {
    NeuFakeMem fm;
    std::vector<std::pair<int32_t,int64_t>> es = {{100,0},{200,1},{300,2},{400,3},{500,4}};
    NeuPutFNameData(fm, 0x10000000, 0x30000000, 0x40000000, es, 8, /*tagged*/true);
    auto rd = [&](uintptr_t a, void* o, size_t n){ return fm.Read(a, o, n); };
    Neu::EnumNamesLayout L;
    EXPECT("fnd detect", Neu::DetectLayout(rd, 0x10000000, 8, 16384, L));
    EXPECT("fnd format", L.format == Neu::EnumNamesFormat::FNameData57);
    EXPECT_EQ_U64("fnd count", L.count, 5);
    EXPECT_EQ_U64("fnd namesPtr masked",  L.namesPtr,  0x30000000);  // tag bit stripped
    EXPECT_EQ_U64("fnd valuesPtr masked", L.valuesPtr, 0x40000000);
    int32_t idx = 0; int64_t v = 0;
    Neu::ReadEntry(rd, L, 0, idx, v);  EXPECT_EQ_U64("fnd idx0", idx, 100);  EXPECT_EQ_U64("fnd val0", v, 0);
    Neu::ReadEntry(rd, L, 4, idx, v);  EXPECT_EQ_U64("fnd idx4", idx, 500);  EXPECT_EQ_U64("fnd val4", v, 4);
}

static void Test_Neu_FNameData_CasePreserving() {
    NeuFakeMem fm;
    std::vector<std::pair<int32_t,int64_t>> es = {{11,0},{22,1},{33,2}};
    NeuPutFNameData(fm, 0x10000000, 0x30000000, 0x40000000, es, 0x10, true);  // FName=16 stride
    auto rd = [&](uintptr_t a, void* o, size_t n){ return fm.Read(a, o, n); };
    Neu::EnumNamesLayout L;
    EXPECT("fnd CPN detect", Neu::DetectLayout(rd, 0x10000000, 0x10, 16384, L));
    int32_t idx = 0; int64_t v = 0;
    Neu::ReadEntry(rd, L, 1, idx, v);
    EXPECT_EQ_U64("fnd CPN idx1", idx, 22);  EXPECT_EQ_U64("fnd CPN val1", v, 1);
}

static void Test_Neu_FNameData_SparseValues() {
    // Proves we read the ACTUAL values array, not assume sequential [0,1,2,...].
    NeuFakeMem fm;
    std::vector<std::pair<int32_t,int64_t>> es = {{1,0},{2,1},{3,2},{4,4},{5,8},{6,255}};
    NeuPutFNameData(fm, 0x10000000, 0x30000000, 0x40000000, es, 8, true);
    auto rd = [&](uintptr_t a, void* o, size_t n){ return fm.Read(a, o, n); };
    Neu::EnumNamesLayout L;
    EXPECT("fnd sparse detect", Neu::DetectLayout(rd, 0x10000000, 8, 16384, L));
    int32_t idx = 0; int64_t v = 0;
    Neu::ReadEntry(rd, L, 3, idx, v);  EXPECT_EQ_U64("fnd sparse val3", v, 4);
    Neu::ReadEntry(rd, L, 4, idx, v);  EXPECT_EQ_U64("fnd sparse val4", v, 8);
    Neu::ReadEntry(rd, L, 5, idx, v);  EXPECT_EQ_U64("fnd sparse val5", v, 255);
}

static void Test_Neu_TagBitMasked() {
    // Untagged (low bit 0) name/value pointers must still mask to the same base.
    NeuFakeMem fm;
    std::vector<std::pair<int32_t,int64_t>> es = {{7,0},{8,1}};
    NeuPutFNameData(fm, 0x10000000, 0x30000000, 0x40000000, es, 8, /*tagged*/false);
    auto rd = [&](uintptr_t a, void* o, size_t n){ return fm.Read(a, o, n); };
    Neu::EnumNamesLayout L;
    EXPECT("fnd untagged detect", Neu::DetectLayout(rd, 0x10000000, 8, 16384, L));
    EXPECT_EQ_U64("fnd untagged namesPtr", L.namesPtr, 0x30000000);
    int32_t idx = 0; int64_t v = 0;
    Neu::ReadEntry(rd, L, 1, idx, v);  EXPECT_EQ_U64("fnd untagged idx1", idx, 8);
}

static void Test_Neu_Disambiguation() {
    // A legacy header whose Num|Max 8-byte word masks into the pointer numeric
    // range but at UNMAPPED memory must still be read as Legacy — the FNameData
    // hypothesis is rejected because its "values pointer" won't dereference.
    NeuFakeMem fm;
    std::vector<std::pair<int32_t,int64_t>> es = {{10,0},{20,1}};
    const int stride = 8; const size_t entryStride = (size_t)stride + 8;
    std::vector<uint8_t> data(es.size() * entryStride, 0);
    for (size_t i = 0; i < es.size(); ++i) {
        std::memcpy(&data[i*entryStride], &es[i].first, 4);
        std::memcpy(&data[i*entryStride + stride], &es[i].second, 8);
    }
    fm.Put(0x20000000, data.data(), data.size());
    // Num=2, Max=0x55 -> w1 = 0x0000005500000002; (&~1) ~= 0x5500000002 is in the
    // pointer numeric range yet unmapped. Bait +0x10 with a plausible "NumValues".
    uint8_t hdr[0x18] = {};
    uint64_t dataU = 0x20000000;  std::memcpy(hdr + 0, &dataU, 8);
    int32_t num = 2, maxN = 0x55;  std::memcpy(hdr + 8, &num, 4);  std::memcpy(hdr + 12, &maxN, 4);
    int32_t bait = 2;              std::memcpy(hdr + 16, &bait, 4);
    fm.Put(0x10000000, hdr, sizeof(hdr));
    auto rd = [&](uintptr_t a, void* o, size_t n){ return fm.Read(a, o, n); };
    Neu::EnumNamesLayout L;
    EXPECT("disambig detect",       Neu::DetectLayout(rd, 0x10000000, 8, 16384, L));
    EXPECT("disambig picks Legacy", L.format == Neu::EnumNamesFormat::Legacy);
    EXPECT_EQ_U64("disambig count", L.count, 2);
}

static void Test_Neu_Edge() {
    Neu::EnumNamesLayout L;
    auto rd_none = [](uintptr_t, void*, size_t){ return false; };
    EXPECT("edge all-fault -> false", !Neu::DetectLayout(rd_none, 0x10000000, 8, 16384, L));

    {   // count over the cap -> rejected
        NeuFakeMem fm;
        std::vector<std::pair<int32_t,int64_t>> es = {{1,0},{2,1},{3,2},{4,3},{5,4}};
        NeuPutLegacy(fm, 0x10000000, 0x20000000, es, 8);
        auto rd = [&](uintptr_t a, void* o, size_t n){ return fm.Read(a, o, n); };
        Neu::EnumNamesLayout L2;
        EXPECT("edge over-cap -> false", !Neu::DetectLayout(rd, 0x10000000, 8, /*maxCount*/3, L2));
    }
    {   // FNameData header present but the arrays are unmapped -> rejected
        NeuFakeMem fm;
        uint8_t hdr[0x18] = {};
        uint64_t tn = 0x30000000ull | 1, tv = 0x40000000ull | 1;  int32_t num = 3;
        std::memcpy(hdr + 0, &tn, 8);  std::memcpy(hdr + 8, &tv, 8);  std::memcpy(hdr + 16, &num, 4);
        fm.Put(0x10000000, hdr, sizeof(hdr));   // arrays intentionally NOT registered
        auto rd = [&](uintptr_t a, void* o, size_t n){ return fm.Read(a, o, n); };
        Neu::EnumNamesLayout L3;
        EXPECT("edge unmapped arrays -> false", !Neu::DetectLayout(rd, 0x10000000, 8, 16384, L3));
    }
    {   // audit #5 AF1 — the count is range-checked BEFORE the narrowing cast.
        //
        // The FNameData57 branch tested `static_cast<int32_t>(numNew) > maxCount`, so the
        // entire upper half of the uint32 range slipped past: 0x80000000 casts to
        // -2147483648, which is not greater than maxCount. `out.count` then came back
        // NEGATIVE from a function whose contract is "parses sanely".
        //
        // The arrays ARE mapped here, so the only thing that can reject these headers is
        // the count check itself — a fixture with unmapped arrays would pass for the
        // wrong reason and prove nothing.
        NeuFakeMem fm;
        uint8_t names[64] = {}, vals[64] = {};
        fm.Put(0x30000000, names, sizeof(names));
        fm.Put(0x40000000, vals,  sizeof(vals));

        auto putHdr = [&](uintptr_t region, uint32_t rawCount) {
            uint8_t hdr[0x18] = {};
            uint64_t tn = 0x30000000ull | 1, tv = 0x40000000ull | 1;
            std::memcpy(hdr + 0,  &tn, 8);
            std::memcpy(hdr + 8,  &tv, 8);
            std::memcpy(hdr + 16, &rawCount, 4);
            fm.Put(region, hdr, sizeof(hdr));
        };
        auto rd = [&](uintptr_t a, void* o, size_t n){ return fm.Read(a, o, n); };

        // Sanity: the same fixture with a PLAUSIBLE count is accepted, so a rejection
        // below is about the count and not about the fixture being unreadable.
        putHdr(0x10000000, 4);
        Neu::EnumNamesLayout ok;
        EXPECT("AF1 control: a sane count on this fixture is accepted",
               Neu::BuildLayout(rd, 0x10000000, Neu::EnumNamesFormat::FNameData57, 8, 16384, ok)
               && ok.count == 4);

        // The sign-bit case, and the two neighbours that bracket it.
        //
        // A DISTINCT region per case, deliberately: NeuFakeMem::Put appends and Read
        // returns the FIRST region that covers the address, so re-using one address
        // would have silently re-read case 1 three times — a loop that reports three
        // passes while testing one value.
        const uint32_t cases[] = { 0x80000000u, 0xFFFFFFFFu, 0x7FFFFFFFu };
        uintptr_t region = 0x11000000;
        for (uint32_t raw : cases) {
            region += 0x1000;
            putHdr(region, raw);
            Neu::EnumNamesLayout bad;
            bool built = Neu::BuildLayout(rd, region,
                                          Neu::EnumNamesFormat::FNameData57, 8, 16384, bad);
            EXPECT("AF1: an implausible uint32 count is rejected", !built);
            // Belt and braces: even if some future change accepts one, it must never
            // publish a negative count — that is the value that reaches the callers.
            EXPECT("AF1: count is never negative", !built || bad.count > 0);
        }
    }
}

// ----- Orden::MatchGroup — multi-value group scan SDR matcher --------------
//
// Orden works on already-read Leaf structs (no memory functor) — the scanner
// produces the leaves, Orden does the pure combinatorial match. Helpers build
// synthetic leaves + multi-width slot targets via the SAME BuildNumericTargets
// machinery the live scan uses.

static Orden::Leaf OrdenLeaf(Radar::DataType width, int32_t pos,
                             const void* raw, size_t n, uint32_t descIdx = 0) {
    Orden::Leaf lf;
    lf.position      = pos;
    lf.width         = width;
    lf.descriptorIdx = descIdx;
    lf.elementIndex  = -1;
    std::memcpy(lf.bytes, raw, n);
    return lf;
}
static Orden::Leaf OrdenLeafI32(int32_t pos, int32_t v, uint32_t descIdx = 0) {
    return OrdenLeaf(Radar::DataType::Int32, pos, &v, 4, descIdx);
}
static Orden::Leaf OrdenLeafI16(int32_t pos, int16_t v) {
    return OrdenLeaf(Radar::DataType::Int16, pos, &v, 2);
}
static Orden::Leaf OrdenLeafFloat(int32_t pos, float v) {
    return OrdenLeaf(Radar::DataType::Float, pos, &v, 4);
}

static void Test_Orden_PerSlotCap() {
    // The cap decides what a LATER refine can re-read, and leaves arrive in field
    // declaration order (base class first). A cap of 8 therefore stored only AActor's
    // early fields for every DumperTestActor candidate, and a Changed refine pruned all
    // 618 of them to zero -- which read as "group scan cannot see my property".
    // Measured 2026-08-05; the diagnostic said `leaves entered=8 ... predicate-said-no=8`.
    std::vector<Orden::Leaf> leaves;
    for (int i = 0; i < 40; ++i) leaves.push_back(OrdenLeafI32(i * 4, 100 + i));

    Radar::NumericTargetSet lo, hi;
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "0",      lo);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "100000", hi);
    Orden::SlotTarget st{};
    st.targets = &lo; st.st = Radar::ScanType::Between; st.targets2 = &hi;
    std::vector<Orden::SlotTarget> slots = { st, st };

    std::vector<Orden::SlotMatches> out;
    bool truncated = false;
    EXPECT("cap: 40 leaves still match",
           Orden::MatchGroup(leaves, slots, out, Orden::kDefaultPerSlotCap, &truncated));
    EXPECT("cap: every satisfying leaf kept (the refine re-reads this list)",
           out[0].leafIdx.size() == 40);
    EXPECT("cap: nothing dropped under the default", !truncated);

    std::vector<Orden::SlotMatches> capped;
    bool tr2 = false;
    Orden::MatchGroup(leaves, slots, capped, 8, &tr2);
    EXPECT("cap: an explicit small cap still bounds the list", capped[0].leafIdx.size() == 8);
    EXPECT("cap: and truncation is REPORTED, not silent", tr2);
}

static void Test_Orden_DistinctValues() {
    // Four numeric leaves at scattered offsets; four slots in a DIFFERENT order.
    // Mirrors the spec example: Str 24, Def 10, Dex 14, Int 8.
    std::vector<Orden::Leaf> leaves = {
        OrdenLeafI32(0x18, 8),    // Int  (smallest offset, last input slot)
        OrdenLeafI32(0x1C, 14),   // Dex
        OrdenLeafI32(0x20, 24),   // Str
        OrdenLeafI32(0x24, 10),   // Def
        OrdenLeafI32(0x2C, 99),   // unrelated leaf
    };
    Radar::NumericTargetSet t0, t1, t2, t3;
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "24", t0);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "10", t1);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "14", t2);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "8",  t3);
    std::vector<Orden::SlotTarget> slots = {{&t0},{&t1},{&t2},{&t3}};

    std::vector<Orden::SlotMatches> out;
    EXPECT("group distinct match", Orden::MatchGroup(leaves, slots, out));
    EXPECT("group 4 slots", out.size() == 4);
    // Each value is unique -> each slot resolves to exactly one leaf (locked).
    EXPECT("slot0 (24) singleton", out[0].leafIdx.size() == 1);
    EXPECT("slot0 -> pos 0x20", leaves[out[0].leafIdx[0]].position == 0x20);
    EXPECT("slot3 (8) -> pos 0x18 (order-independent)",
           out[3].leafIdx.size() == 1 && leaves[out[3].leafIdx[0]].position == 0x18);
}

static void Test_Orden_MissingValueRejected() {
    // No leaf holds 10 -> the Def slot has zero matches -> reject whole block.
    std::vector<Orden::Leaf> leaves = {
        OrdenLeafI32(0x10, 24), OrdenLeafI32(0x14, 14), OrdenLeafI32(0x18, 8),
    };
    Radar::NumericTargetSet t0, t1, t2, t3;
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "24", t0);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "10", t1);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "14", t2);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "8",  t3);
    std::vector<Orden::SlotTarget> slots = {{&t0},{&t1},{&t2},{&t3}};
    std::vector<Orden::SlotMatches> out;
    EXPECT("group missing value rejected", !Orden::MatchGroup(leaves, slots, out));
}

static void Test_Orden_DuplicateValuesSDR() {
    // Two slots want 24, one wants 10. Needs TWO distinct leaves holding 24.
    Radar::NumericTargetSet t24a, t24b, t10;
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "24", t24a);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "24", t24b);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "10", t10);
    std::vector<Orden::SlotTarget> slots = {{&t24a},{&t24b},{&t10}};

    {   // two leaves hold 24 -> distinct assignment exists
        std::vector<Orden::Leaf> leaves = {
            OrdenLeafI32(0x10, 24), OrdenLeafI32(0x14, 24), OrdenLeafI32(0x18, 10),
        };
        std::vector<Orden::SlotMatches> out;
        EXPECT("dup-value SDR ok (two 24s)", Orden::MatchGroup(leaves, slots, out));
    }
    {   // only ONE leaf holds 24 -> cannot satisfy both 24 slots
        std::vector<Orden::Leaf> leaves = {
            OrdenLeafI32(0x10, 24), OrdenLeafI32(0x18, 10),
        };
        std::vector<Orden::SlotMatches> out;
        EXPECT("dup-value SDR fail (one 24)", !Orden::MatchGroup(leaves, slots, out));
    }
}

static void Test_Orden_MultiWidthMatch() {
    // "24" must match the same value stored as Int16, Int32, or Float; "25" must not.
    Radar::NumericTargetSet t24, t25;
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "24", t24);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "25", t25);

    std::vector<Orden::Leaf> leaves = {
        OrdenLeafI16(0x10, 24), OrdenLeafFloat(0x14, 24.0f), OrdenLeafI32(0x18, 24),
    };
    {   // two slots both want 24 -> match against the int16 + float + int32 pool
        std::vector<Orden::SlotTarget> slots = {{&t24},{&t24}};
        std::vector<Orden::SlotMatches> out;
        EXPECT("multi-width 24 matches", Orden::MatchGroup(leaves, slots, out));
        EXPECT("multi-width slot0 has >=2 leaves", out[0].leafIdx.size() >= 2);
    }
    {   // 25 is absent at every width
        std::vector<Orden::SlotTarget> slots = {{&t25},{&t24}};
        std::vector<Orden::SlotMatches> out;
        EXPECT("multi-width 25 absent rejected", !Orden::MatchGroup(leaves, slots, out));
    }
}

static void Test_Orden_ConvergenceAndAssignment() {
    // HasDistinctAssignment directly models refine convergence: as per-slot lists
    // shrink they stay feasible until one empties.
    std::vector<Orden::SlotMatches> m(2);
    m[0].leafIdx = {0, 1};
    m[1].leafIdx = {1};
    EXPECT("SDR feasible before convergence", Orden::HasDistinctAssignment(m, 2));
    // Refine locks slot1->leaf1, forcing slot0->leaf0 (still feasible).
    m[0].leafIdx = {0};
    EXPECT("SDR feasible after lock", Orden::HasDistinctAssignment(m, 2));
    // Both collapse onto the same single leaf -> no distinct assignment.
    m[0].leafIdx = {1};
    EXPECT("SDR infeasible on collision", !Orden::HasDistinctAssignment(m, 2));
    // An emptied slot (its value vanished on refine) -> reject.
    m[0].leafIdx.clear();
    EXPECT("SDR infeasible on empty slot", !Orden::HasDistinctAssignment(m, 2));
}

static void Test_Orden_OrderedFirstScan() {
    // P2: per-slot ordered predicates on the FIRST scan (Bigger / Smaller),
    // routed through Radar::ComparePredicate by LeafSatisfiesSlot. Leaves at
    // Str 24, Def 10.
    std::vector<Orden::Leaf> leaves = {
        OrdenLeafI32(0x10, 24), OrdenLeafI32(0x14, 10),
    };
    Radar::NumericTargetSet t20, t15, t30;
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "20", t20);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "15", t15);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "30", t30);
    {   // slot0: > 20 (24 ok), slot1: < 15 (10 ok) -> distinct match
        std::vector<Orden::SlotTarget> slots = {
            { &t20, Radar::ScanType::Bigger,  Radar::RoundMode::Round },
            { &t15, Radar::ScanType::Smaller, Radar::RoundMode::Round },
        };
        std::vector<Orden::SlotMatches> out;
        EXPECT("group ordered first-scan match", Orden::MatchGroup(leaves, slots, out));
    }
    {   // slot0: > 30 -> no leaf qualifies -> reject the whole block
        std::vector<Orden::SlotTarget> slots = {
            { &t30, Radar::ScanType::Bigger,  Radar::RoundMode::Round },
            { &t15, Radar::ScanType::Smaller, Radar::RoundMode::Round },
        };
        std::vector<Orden::SlotMatches> out;
        EXPECT("group ordered first-scan reject (no leaf > 30)",
               !Orden::MatchGroup(leaves, slots, out));
    }
}

static void Test_Orden_BetweenFirstScan() {
    // P2: per-slot Between (inclusive range) on the first scan — needs both the
    // lower (`targets`) and upper (`targets2`) bound. Leaves Str 24, Def 10.
    std::vector<Orden::Leaf> leaves = {
        OrdenLeafI32(0x10, 24), OrdenLeafI32(0x14, 10),
    };
    Radar::NumericTargetSet lo20, hi30, lo5, hi12, hi8;
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "20", lo20);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "30", hi30);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "5",  lo5);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "12", hi12);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "8",  hi8);
    {   // slot0 in [20,30] (24 ok), slot1 in [5,12] (10 ok) -> distinct match
        std::vector<Orden::SlotTarget> slots = {
            { &lo20, Radar::ScanType::Between, Radar::RoundMode::Round,&hi30 },
            { &lo5,  Radar::ScanType::Between, Radar::RoundMode::Round,&hi12 },
        };
        std::vector<Orden::SlotMatches> out;
        EXPECT("group Between first-scan match", Orden::MatchGroup(leaves, slots, out));
    }
    {   // slot1 in [5,8] -> 10 is out of range -> reject the block
        std::vector<Orden::SlotTarget> slots = {
            { &lo20, Radar::ScanType::Between, Radar::RoundMode::Round,&hi30 },
            { &lo5,  Radar::ScanType::Between, Radar::RoundMode::Round,&hi8 },
        };
        std::vector<Orden::SlotMatches> out;
        EXPECT("group Between first-scan reject (10 not in [5,8])",
               !Orden::MatchGroup(leaves, slots, out));
    }
    {   // missing upper bound -> Between can't evaluate -> no match
        std::vector<Orden::SlotTarget> slots = {
            { &lo20, Radar::ScanType::Between, Radar::RoundMode::Round,nullptr },
            { &lo5,  Radar::ScanType::Between, Radar::RoundMode::Round,&hi12 },
        };
        std::vector<Orden::SlotMatches> out;
        EXPECT("group Between missing upper bound rejected",
               !Orden::MatchGroup(leaves, slots, out));
    }
}

static void Test_Orden_RoundedFloatExact() {
    // Multi-value (group / "multi value search") scan inherits the CE-style rounded
    // float Exact through LeafSatisfiesSlot -> Radar::ComparePredicate: a whole-number
    // target matches a float that ROUNDS to it, so a GAS Health.BaseValue=513.36 is
    // found by Exact "513" (the same gap the snapshot fix closed for Group Match).
    std::vector<Orden::Leaf> leaves = {
        OrdenLeafFloat(0x10, 513.36f),   // e.g. AttributeSet "Health.BaseValue"
        OrdenLeafFloat(0x14, 99.6f),     // e.g. AttributeSet "Mana.BaseValue"
    };
    Radar::NumericTargetSet t513, t100, t98;
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "513", t513);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "100", t100);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "98",  t98);
    {   // slot0 Exact 513 (513.36->513), slot1 Exact 100 (99.6->100) -> distinct match
        std::vector<Orden::SlotTarget> slots = {
            { &t513, Radar::ScanType::Exact, Radar::RoundMode::Round },
            { &t100, Radar::ScanType::Exact, Radar::RoundMode::Round },
        };
        std::vector<Orden::SlotMatches> out;
        EXPECT("group rounded float Exact match (513.36~513 + 99.6~100)",
               Orden::MatchGroup(leaves, slots, out));
    }
    {   // slot1 Exact 98 -> neither leaf rounds to 98 -> reject the whole block
        std::vector<Orden::SlotTarget> slots = {
            { &t513, Radar::ScanType::Exact, Radar::RoundMode::Round },
            { &t98,  Radar::ScanType::Exact, Radar::RoundMode::Round },
        };
        std::vector<Orden::SlotMatches> out;
        EXPECT("group rounded float Exact reject (nothing rounds to 98)",
               !Orden::MatchGroup(leaves, slots, out));
    }
    {   // DOUBLE leaves inherit the same rounded Exact (Radar::IsFloatType covers Double).
        double hp = 7421.6, mp = 49.5;   // round half-away-from-zero -> 7422, 50
        std::vector<Orden::Leaf> dleaves = {
            OrdenLeaf(Radar::DataType::Double, 0x20, &hp, 8),
            OrdenLeaf(Radar::DataType::Double, 0x28, &mp, 8),
        };
        Radar::NumericTargetSet t7422, t50;
        Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "7422", t7422);
        Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "50",   t50);
        std::vector<Orden::SlotTarget> slots = {
            { &t7422, Radar::ScanType::Exact, Radar::RoundMode::Round },
            { &t50,   Radar::ScanType::Exact, Radar::RoundMode::Round },
        };
        std::vector<Orden::SlotMatches> out;
        EXPECT("group rounded DOUBLE Exact match (7421.6~7422 + 49.5~50)",
               Orden::MatchGroup(dleaves, slots, out));
    }
}

static void Test_Orden_PrevValueRejectedOnFirstScan() {
    // Prev-value predicates (Increased / ...) have no baseline on the first scan,
    // so LeafSatisfiesSlot — and thus MatchGroup — must never match them,
    // regardless of the leaf value. (The refine path is what honours them.)
    std::vector<Orden::Leaf> leaves = {
        OrdenLeafI32(0x10, 24), OrdenLeafI32(0x14, 10),
    };
    Radar::NumericTargetSet t24, t10;
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "24", t24);
    Radar::BuildNumericTargets(Radar::DataType::NumericNoByte, "10", t10);
    std::vector<Orden::SlotTarget> slots = {
        { &t24, Radar::ScanType::Increased, Radar::RoundMode::Round },   // prev-value: never matches here
        { &t10, Radar::ScanType::Exact,     Radar::RoundMode::Round },
    };
    std::vector<Orden::SlotMatches> out;
    EXPECT("group prev-value slot never matches on first scan",
           !Orden::MatchGroup(leaves, slots, out));
    EXPECT("group prev-value slot0 collected zero leaves", out[0].leafIdx.empty());
}

// ----- Ubel: Native-C scan P0 — hole computation + type normalization --------
//
// Pure helpers (no game memory). ComputeHoles is the interval-complement core
// shared by the Guess-What gap pass (WalkInstance) and the Native-C value scan;
// ComputeClassHoles is the ArrayDim-aware class-level builder the scan will use;
// NormalizeGuessedTypeToProperty maps Guess labels to canonical property strings.

static void Test_Holes_ComputeHoles_Basic() {
    // Two occupied fields in [0x28, 0x40): a gap before, between, and after.
    std::vector<Ubel::Interval> occ = { {0x30, 0x34}, {0x38, 0x3C} };
    auto holes = Ubel::ComputeHoles(occ, 0x28, 0x40);
    EXPECT("3 holes", holes.size() == 3);
    if (holes.size() == 3) {
        EXPECT_EQ_U64("hole0 start", holes[0].start, 0x28); EXPECT_EQ_U64("hole0 end", holes[0].end, 0x30);
        EXPECT_EQ_U64("hole1 start", holes[1].start, 0x34); EXPECT_EQ_U64("hole1 end", holes[1].end, 0x38);
        EXPECT_EQ_U64("hole2 start", holes[2].start, 0x3C); EXPECT_EQ_U64("hole2 end", holes[2].end, 0x40);
    }
}

static void Test_Holes_LeadingGapSurvives() {
    // Regression for commit 75ea723: a field at/after the first real offset must
    // NOT swallow the [windowStart, firstField) leading region.
    std::vector<Ubel::Interval> occ = { {0x40, 0x44} };
    auto holes = Ubel::ComputeHoles(occ, 0x28, 0x80);
    EXPECT("leading + trailing = 2 holes", holes.size() == 2);
    if (holes.size() == 2) {
        EXPECT_EQ_U64("leading hole start", holes[0].start, 0x28);
        EXPECT_EQ_U64("leading hole end",   holes[0].end,   0x40);
        EXPECT_EQ_U64("trailing hole start", holes[1].start, 0x44);
        EXPECT_EQ_U64("trailing hole end",   holes[1].end,   0x80);
    }
}

static void Test_Holes_FullyCovered() {
    std::vector<Ubel::Interval> occ = { {0x28, 0x40} };
    EXPECT("fully covered -> no holes", Ubel::ComputeHoles(occ, 0x28, 0x40).empty());
    // Overlapping + adjacent intervals merge to full coverage.
    std::vector<Ubel::Interval> occ2 = { {0x28, 0x34}, {0x30, 0x3A}, {0x3A, 0x40} };
    EXPECT("merged coverage -> no holes", Ubel::ComputeHoles(occ2, 0x28, 0x40).empty());
}

static void Test_Holes_ClampsOutOfWindow() {
    // A field reaching below windowStart is trimmed (header bytes excluded), and
    // a field with a garbage-huge end is trimmed to windowEnd — neither drops the
    // surrounding holes.
    std::vector<Ubel::Interval> occ = { {0x00, 0x2C}, {0x30, 0x7FFFFFF0} };
    auto holes = Ubel::ComputeHoles(occ, 0x28, 0x40);
    EXPECT("one middle hole", holes.size() == 1);
    if (holes.size() == 1) {
        EXPECT_EQ_U64("hole start (after clamped header field)", holes[0].start, 0x2C);
        EXPECT_EQ_U64("hole end (before clamped huge field)",   holes[0].end,   0x30);
    }
    // Empty / inverted window yields nothing.
    EXPECT("empty window -> no holes", Ubel::ComputeHoles(occ, 0x40, 0x40).empty());
    EXPECT("inverted window -> no holes", Ubel::ComputeHoles(occ, 0x40, 0x28).empty());
}

static void Test_Holes_ComputeClassHoles_ArrayDim() {
    // A static C-array UPROPERTY int Foo[10] at 0x40 (ElementSize 4, ArrayDim 10)
    // occupies [0x40, 0x68) — its tail must NOT be reported as a hole (the
    // phantom-hole bug the ArrayDim read fixes). A scalar int at 0x68 follows.
    ClassInfo ci;
    ci.PropertiesSize = 0x80;
    FieldInfo arr; arr.Offset = 0x40; arr.Size = 4; arr.ArrayDim = 10; ci.Fields.push_back(arr);
    FieldInfo sc;  sc.Offset = 0x68;  sc.Size = 4;  sc.ArrayDim = 1;  ci.Fields.push_back(sc);

    auto holes = Ubel::ComputeClassHoles(ci, 0x28, 0x80);
    // Expect: [0x28,0x40) leading, [0x6C,0x80) trailing. NO [0x44,0x68) phantom.
    EXPECT("array-dim: 2 holes (no phantom)", holes.size() == 2);
    if (holes.size() == 2) {
        EXPECT_EQ_U64("leading hole end == array start", holes[0].end, 0x40);
        EXPECT_EQ_U64("trailing hole start == after scalar", holes[1].start, 0x6C);
    }
    // Sanity: if ArrayDim were ignored (==1) a phantom [0x44,0x68) would appear.
    FieldInfo arrBad = arr; arrBad.ArrayDim = 1;
    ClassInfo ciBad; ciBad.PropertiesSize = 0x80; ciBad.Fields = { arrBad, sc };
    EXPECT("array-dim=1 control yields a phantom hole",
           Ubel::ComputeClassHoles(ciBad, 0x28, 0x80).size() == 3);
}

static void Test_IsSanePropertiesSize() {
    // The bound that stops a recycled-object walk from wedging the pipe: a real
    // UStruct::PropertiesSize is non-negative and at most kMaxSanePropertiesSize.
    // Real-world trigger (Elliot, 2026-06-27 log): returning to Live Walker on a
    // freed instance read class PropertiesSize=867763776 (~827 MB) → one giant
    // gap → GuessGapTypes spun ~8e8 SEH reads and blocked the single-threaded pipe.
    EXPECT("UWorld real size (2536) is sane", Ubel::IsSanePropertiesSize(2536));
    EXPECT("zero is sane (unusual, not garbage)", Ubel::IsSanePropertiesSize(0));
    EXPECT("exactly the cap is sane", Ubel::IsSanePropertiesSize(Ubel::kMaxSanePropertiesSize));
    EXPECT("cap+1 is NOT sane", !Ubel::IsSanePropertiesSize(Ubel::kMaxSanePropertiesSize + 1));
    EXPECT("negative is NOT sane", !Ubel::IsSanePropertiesSize(-1));
    EXPECT("the 827 MB garbage value is NOT sane", !Ubel::IsSanePropertiesSize(867763776));
}

static void Test_ShouldPublishClassWalk() {
    // audit #5 U4: the class caches are keyed by a raw UClass* the engine recycles
    // and nothing in dll/src ever erases them, so one bad walk is served for the rest
    // of the process. WalkInstance owned the identical predicate but ran it AFTER
    // WalkClass had already published — and two shipped call sites hand in addresses
    // that are not UStructs at all (WalkInstance's own pre-gate walk, and
    // UE5_WalkClassBegin, which ue5_dissect.lua uses as its is-this-an-instance probe).

    // The read-ok term is the half IsSanePropertiesSize cannot express: Macht::ReadSafe
    // ZEROES its out-param on an access violation, and 0 is a legitimate
    // PropertiesSize, so an unmapped address is indistinguishable from a UCLASS that
    // declares no own UPROPERTYs unless the return value is carried.
    EXPECT("failed read is never published, even with a sane-looking 0",
           !Ubel::ShouldPublishClassWalk(false, 0));
    EXPECT("failed read is never published, even with a plausible size",
           !Ubel::ShouldPublishClassWalk(false, 512));

    // With a successful read the bound is exactly IsSanePropertiesSize's.
    EXPECT("read ok + real UWorld size is published", Ubel::ShouldPublishClassWalk(true, 2536));
    EXPECT("read ok + zero is published (a field-less UCLASS is legitimate)",
           Ubel::ShouldPublishClassWalk(true, 0));
    EXPECT("read ok + exactly the cap is published",
           Ubel::ShouldPublishClassWalk(true, Ubel::kMaxSanePropertiesSize));
    EXPECT("read ok + negative is NOT published", !Ubel::ShouldPublishClassWalk(true, -1));
    EXPECT("read ok + the 827 MB garbage value is NOT published",
           !Ubel::ShouldPublishClassWalk(true, 867763776));
}

// ── audit #5 G2: the gated version sweep must return EXACTLY what the naive one did ──
//
// The naive references below are transcribed from Genau.cpp as it stood BEFORE the gate
// (pattern-major, unconditional memcmp at every offset, per-pattern break, no first-byte
// reject). They are the oracle: the rewrite's whole safety argument is "same answers,
// 29 s less work", and nothing else in this repo can check the first half — no test
// target compiles Genau.cpp.

struct NaiveResult { uint32_t value = 0; int tier = 0; };

// Updated for G8 + G9 (build 3099). It still models the NAIVE shape — pattern-major, no
// first-byte gate, unconditional memcmp at every offset — so it remains an independent
// oracle for the gated implementation. What changed is the RULES it encodes:
//   * Tier 2's context is a raw 16-byte clamped search, not an 8-byte strstr (G8), and it
//     now requires the same UE anchor Tier 3 always did.
//   * a Tier 3 candidate records the pattern's first-T3 fact but does NOT retire it, so a
//     later Tier 2 hit on the same needle is still found (G9).
// The pre-fix reference is preserved in git history; the cases below pin the DIFFERENCE.
static NaiveResult NaiveTier23Reference(const uint8_t* scan, size_t size) {
    bool     hasT2[Genau::kVersionNeedleCount] = {};
    bool     hasT3[Genau::kVersionNeedleCount] = {};
    for (size_t k = 0; k < Genau::kVersionNeedleCount; ++k) {
        const char* needle = Genau::kVersionNeedles[k].needle;
        size_t needleLen = strlen(needle);
        for (size_t off = 0; off + needleLen + 10 < size; ++off) {
            const size_t bareLen = needleLen - 1;             // G11: match the BARE needle
            if (memcmp(scan + off, needle, bareLen) != 0) continue;
            if (off > 0) {                                    // G11: guard hoisted out of Tier 3
                uint8_t prev = scan[off - 1];
                if ((prev >= '0' && prev <= '9') || prev == '.') continue;
            }
            const uint8_t afterBare = scan[off + bareLen];
            const bool whole = !(afterBare >= '0' && afterBare <= '9');
            if (whole && Genau::HasReleaseBefore(scan, off, 16) &&
                Genau::HasUEAnchorNearby(scan, size, off, 256)) {
                hasT2[k] = true;
                break;                                        // best this pattern can do
            }
            if (scan[off + bareLen] == '.' &&
                scan[off + needleLen] >= '0' && scan[off + needleLen] <= '9') {
                if (!Genau::HasUEAnchorNearby(scan, size, off, 256)) continue;
                hasT3[k] = true;                              // recorded, NOT retired (G9)
            }
        }
    }
    for (size_t k = 0; k < Genau::kVersionNeedleCount; ++k)
        if (hasT2[k]) return NaiveResult{ Genau::kVersionNeedles[k].value, 2 };
    for (size_t k = 0; k < Genau::kVersionNeedleCount; ++k)
        if (hasT3[k]) return NaiveResult{ Genau::kVersionNeedles[k].value, 3 };
    return NaiveResult{};
}

static NaiveResult NaiveTier1Reference(const uint8_t* scan, size_t size) {
    const char* prefixes[] = { "++UE5+Release-", "++UE4+Release-" };
    auto widen = [](const std::string& s) {
        std::vector<uint8_t> w;
        for (char c : s) { w.push_back(static_cast<uint8_t>(c)); w.push_back(0); }
        return w;
    };
    for (int wide = 0; wide <= 1; ++wide) {
        for (const char* prefix : prefixes) {
            std::vector<uint8_t> pre = wide ? widen(prefix)
                : std::vector<uint8_t>(prefix, prefix + strlen(prefix));
            if (pre.empty() || size <= pre.size() + 8) continue;
            for (size_t off = 0; off + pre.size() + 8 < size; ++off) {
                if (memcmp(scan + off, pre.data(), pre.size()) != 0) continue;
                for (size_t k = 0; k < Genau::kVersionNeedleCount; ++k) {
                    std::string bare = Genau::kVersionNeedles[k].needle;
                    if (!bare.empty() && bare.back() == '.') bare.pop_back();
                    std::vector<uint8_t> nd = wide ? widen(bare)
                        : std::vector<uint8_t>(bare.begin(), bare.end());
                    if (off + pre.size() + nd.size() <= size &&
                        memcmp(scan + off + pre.size(), nd.data(), nd.size()) == 0)
                        return NaiveResult{ Genau::kVersionNeedles[k].value, 1 };
                }
            }
        }
    }
    return NaiveResult{};
}

static uint32_t GatedValue(const Genau::NeedleScanResult& r) {
    return r.found() ? Genau::kVersionNeedles[r.index].value : 0u;
}

// Build a buffer of `size` filler bytes with `what` planted at `at`.
static void Plant(std::vector<uint8_t>& buf, size_t at, const char* what) {
    size_t n = strlen(what);
    for (size_t i = 0; i < n; ++i) buf[at + i] = static_cast<uint8_t>(what[i]);
}

static void ExpectEquivalent23(const char* label, std::vector<uint8_t>& buf) {
    NaiveResult naive = NaiveTier23Reference(buf.data(), buf.size());
    Genau::NeedleScanResult gated = Genau::ScanVersionTier23(buf.data(), buf.size());
    bool ok = (naive.value == GatedValue(gated)) && (naive.tier == gated.tier);
    EXPECT(label, ok);
}

static void Test_VersionNeedleScan_Equivalence() {
    // Filler is 'x': not '4', not '5', no '.', no anchor — so nothing matches by accident.
    auto fresh = [](size_t n) { return std::vector<uint8_t>(n, 'x'); };

    {   // plain Tier 2
        auto b = fresh(4096); Plant(b, 1000, "Release-5.4.0");
        ExpectEquivalent23("G2 equiv: plain Tier 2 Release-5.4.", b);
    }
    {   // ⚠ THE CASE THAT DISCRIMINATES. Two needles in the SAME first-byte group, the
        // table-LATER one ("5.4.", index 4) at a LOWER address than the table-EARLIER one
        // ("5.8.", index 0). Table order must win -> 508.
        //
        // A cross-group case (4.27 low vs 5.8 high) CANNOT catch an address-order
        // regression, because two separate first-byte walks preserve UE5-before-UE4
        // ordering by construction. Only this intra-group shape fires. Do not "simplify"
        // it away — see the note in VersionNeedleScan.h.
        auto b = fresh(8192);
        Plant(b, 1000, "Release-5.4.0");
        Plant(b, 5000, "Release-5.8.0");
        ExpectEquivalent23("G2 equiv: intra-group table order (5.4@low vs 5.8@high) -> 508", b);
    }
    {   // cross-group, kept as the weaker companion so the contrast is on record
        auto b = fresh(8192);
        Plant(b, 1000, "Release-4.27.0");
        Plant(b, 5000, "Release-5.8.0");
        ExpectEquivalent23("G2 equiv: cross-group table order -> 508", b);
    }
    {   // bare Tier 3 with an anchor in the window
        auto b = fresh(4096);
        Plant(b, 900, "Unreal");
        Plant(b, 1000, " 5.4.0 ");
        ExpectEquivalent23("G2 equiv: Tier 3 bare + anchor", b);
    }
    {   // Tier 3 with NO anchor -> rejected entirely
        auto b = fresh(4096); Plant(b, 1000, " 5.4.0 ");
        ExpectEquivalent23("G2 equiv: Tier 3 without anchor is rejected", b);
    }
    {   // preceding digit -> a game version, not an engine version
        auto b = fresh(4096);
        Plant(b, 900, "Unreal");
        Plant(b, 1000, "15.4.0 ");
        ExpectEquivalent23("G2 equiv: preceding digit rejected (game version)", b);
    }
    {   // a Tier 2 hit must beat a deferred Tier 3 from an EARLIER table entry
        auto b = fresh(8192);
        Plant(b, 900, "Unreal");
        Plant(b, 1000, " 5.8.0 ");        // Tier 3 for index 0
        Plant(b, 5000, "Release-4.27.0"); // Tier 2 for index 9
        ExpectEquivalent23("G2 equiv: Tier 2 beats a deferred Tier 3 -> 427/t2", b);
    }
    {   // same-pattern retirement: a Tier 3 at a LOW offset hides a Tier 2 at a HIGH one.
        // Reproduces a real (separately filed) quirk of the original `break`; the point
        // here is only that the rewrite reproduces it rather than silently "fixing" it.
        auto b = fresh(8192);
        Plant(b, 900, "Unreal");
        Plant(b, 1000, " 4.27.0 ");
        Plant(b, 5000, "Release-4.27.0");
        ExpectEquivalent23("G2 equiv: same-pattern Tier 3 retires before a later Tier 2", b);
    }
    {   // ⚠ Trailing bound edge. The needle must actually QUALIFY (digit after it, anchor
        // in window, no digit/dot before) or the bound difference is INVISIBLE and this
        // case silently proves nothing — which is exactly what a first version of it did:
        // tightening the walk bound to `off + 16` left it green.
        //
        // The naive 4-char bound is `off + 14 < size`; offsets size-16 and size-15 are
        // inside it and outside a `off + 16` bound, so those are the discriminating spots.
        for (size_t back = 14; back <= 17; ++back) {
            const size_t S = 4096;
            auto b = fresh(S);
            // The anchor must be within 256 bytes OF THE NEEDLE, not merely somewhere in
            // the buffer — planting it far away made this case qualify as nothing at all
            // and silently stopped it discriminating.
            Plant(b, S - back - 100, "Unreal");
            Plant(b, S - back, "5.4.0");       // qualifies as Tier 3 when in bounds
            ExpectEquivalent23("G2 equiv: trailing bound edge, qualifying needle", b);
        }
        // 5-char needles have a tighter bound than 4-char ones by exactly one offset;
        // a shared bound would change which trailing offsets are examined.
        for (size_t back = 14; back <= 17; ++back) {
            const size_t S = 4096;
            auto b = fresh(S);
            Plant(b, S - back - 100, "Unreal");
            Plant(b, S - back, "4.27.0");
            ExpectEquivalent23("G2 equiv: trailing bound edge, 5-char needle", b);
        }
    }
    {   // needle at offset 0 (the `off > 0` / `off >= 8` guards)
        auto b = fresh(4096);
        Plant(b, 0, "5.4.0");
        Plant(b, 100, "Unreal");
        ExpectEquivalent23("G2 equiv: needle at offset 0", b);
    }
    {   // Tier 1, narrow and wide
        auto b = fresh(4096); Plant(b, 1500, "++UE5+Release-5.4");
        NaiveResult naive = NaiveTier1Reference(b.data(), b.size());
        Genau::NeedleScanResult g = Genau::ScanVersionTier1(b.data(), b.size());
        EXPECT("G2 equiv: Tier 1 ascii", naive.value == GatedValue(g) && naive.tier == g.tier);
        EXPECT("G2 equiv: Tier 1 ascii finds 504", GatedValue(g) == 504 && g.tier == 1);
    }
    {
        auto b = fresh(4096);
        const char* tag = "++UE4+Release-4.27";
        for (size_t i = 0; tag[i]; ++i) { b[1500 + i * 2] = (uint8_t)tag[i]; b[1500 + i * 2 + 1] = 0; }
        NaiveResult naive = NaiveTier1Reference(b.data(), b.size());
        Genau::NeedleScanResult g = Genau::ScanVersionTier1(b.data(), b.size());
        EXPECT("G2 equiv: Tier 1 utf16", naive.value == GatedValue(g) && naive.tier == g.tier);
        EXPECT("G2 equiv: Tier 1 utf16 finds 427", GatedValue(g) == 427 && g.tier == 1);
    }
    {   // deterministic pseudo-random fuzz over a biased alphabet that makes hits likely.
        // NOTE: fuzz alone did NOT catch the address-order regression — the intra-group
        // case above is what does. Kept for coverage breadth, not as the control.
        uint32_t seed = 0x5EED1234u;
        auto next = [&seed]() { seed = seed * 1664525u + 1013904223u; return seed >> 16; };
        static const char alphabet[] = "45.0123RelaseUnrx ";
        for (int iter = 0; iter < 400; ++iter) {
            std::vector<uint8_t> b(512);
            for (auto& c : b) c = (uint8_t)alphabet[next() % (sizeof(alphabet) - 1)];
            NaiveResult naive = NaiveTier23Reference(b.data(), b.size());
            Genau::NeedleScanResult g = Genau::ScanVersionTier23(b.data(), b.size());
            if (naive.value != GatedValue(g) || naive.tier != g.tier) {
                EXPECT("G2 equiv: fuzz buffer mismatch", false);
                break;
            }
        }
        EXPECT("G2 equiv: 400 fuzz buffers agree", true);
    }
}

static void Test_VersionNeedleScan_GateStillGates() {
    // The PERFORMANCE property, made deterministic — no wall clock, so no CI flake.
    // A buffer with no '4' and no '5' byte anywhere must cost ZERO needle compares.
    // Delete the first-byte reject in ScanVersionTier23 and this jumps to ~19x size.
    std::vector<uint8_t> b(1u << 20, 'x');
    Genau::NeedleScanResult r = Genau::ScanVersionTier23(b.data(), b.size());
    EXPECT("G2 gate: no '4'/'5' byte -> zero needle compares", r.needlesCompared == 0);
    EXPECT("G2 gate: and no detection", !r.found());

    // ⚠ The FIRST-byte gate alone satisfies the buffer above, so that case cannot see the
    // SECOND-byte ('.') gate at all — deleting it left the suite green. This buffer is all
    // '5' with no '.' anywhere: the first-byte gate passes at every offset and only the
    // '.' gate can keep the compare count at zero.
    std::vector<uint8_t> allFives(1u << 20, '5');
    Genau::NeedleScanResult f = Genau::ScanVersionTier23(allFives.data(), allFives.size());
    EXPECT("G2 gate: all-'5' but no '.' -> zero needle compares", f.needlesCompared == 0);
    EXPECT("G2 gate: all-'5' finds nothing", !f.found());

    // Same for Tier 1's '+' gate.
    Genau::NeedleScanResult t1 = Genau::ScanVersionTier1(b.data(), b.size());
    EXPECT("G2 gate: no '+' byte -> zero prefix compares", t1.needlesCompared == 0);

    // And the gate must not be so aggressive that a real hit costs nothing to find:
    // a planted needle MUST produce at least one compare, or the counter is measuring
    // a code path that never runs.
    std::vector<uint8_t> hit(4096, 'x');
    Plant(hit, 900, "Unreal");                 // Tier 2 now requires an anchor too (G8)
    Plant(hit, 1000, "Release-5.4.0");
    Genau::NeedleScanResult h = Genau::ScanVersionTier23(hit.data(), hit.size());
    EXPECT("G2 gate: a real hit still issues compares", h.needlesCompared > 0);
    EXPECT("G2 gate: a real hit is still found", h.found() && h.tier == 2);
}

// ── audit #5 G8 + G9: the two Tier 2/3 rule defects, and what changed ────────
//
// These assert ABSOLUTE answers, not just naive/gated agreement — the equivalence oracle
// alone cannot see these fixes, because BOTH sides moved. Each case states what the
// pre-fix code returned.
static void Test_VersionTierRules_G8_G9() {
    auto fresh = [](size_t n) { return std::vector<uint8_t>(n, 'x'); };
    auto tier23 = [](std::vector<uint8_t>& b) { return Genau::ScanVersionTier23(b.data(), b.size()); };
    auto val = [](const Genau::NeedleScanResult& r) {
        return r.found() ? Genau::kVersionNeedles[r.index].value : 0u;
    };

    // ── G8: the window was 8 bytes while its comment and buffer both said 16 ──
    {   // "Release" separated by more than one byte. Pre-fix: Tier 3 (the "Release" ended
        // more than 8 bytes before the needle, so the context test never saw it).
        auto b = fresh(4096);
        Plant(b, 900, "Unreal");
        Plant(b, 1000, "Release v5.4.0");
        auto r = tier23(b);
        EXPECT("G8: 'Release v5.4.0' is now Tier 2", r.tier == 2 && val(r) == 504);
    }
    {   auto b = fresh(4096);
        Plant(b, 900, "Unreal");
        Plant(b, 1000, "Release_Build_5.4.0");
        auto r = tier23(b);
        EXPECT("G8: 'Release_Build_5.4.0' is now Tier 2", r.tier == 2 && val(r) == 504);
    }
    {   // ⚠ NUL-IMMUNITY — the case that makes the naive `memcpy(...,16)+strstr` repair WRONG.
        // A neighbouring string's terminator sits inside the wider window; strstr would stop
        // there and LOSE a match the 8-byte window found. The raw search must still find it.
        auto b = fresh(4096);
        Plant(b, 900, "Unreal");
        Plant(b, 1000, "AAAAAAA");
        b[1007] = 0x00;                       // NUL inside the 16-byte window
        Plant(b, 1008, "Release-5.4.0");
        auto r = tier23(b);
        EXPECT("G8: a NUL in the wider window does not lose the hit", r.tier == 2 && val(r) == 504);
    }
    {   // ⚠ THE SAFETY HALF. Widening without an anchor gate manufactures a confident
        // detection from ordinary text. Tier 2 had NO anchor requirement; it does now.
        // Pre-fix (and with a naive widening): Tier 2 / 504 from a release-notes heading.
        auto b = fresh(4096);
        Plant(b, 1000, "Release Notes 5.4.0");   // no Engine/Unreal/UE anywhere
        auto r = tier23(b);
        EXPECT("G8: anchorless 'Release Notes 5.4.0' is NOT detected", !r.found());
    }

    // ── G9: a Tier 3 candidate retired the pattern before a later Tier 2 was seen ──
    {   // Same needle: Tier 3 at a low offset, Tier 2 later. Pre-fix: 427 / Tier 3.
        auto b = fresh(8192);
        Plant(b, 900, "Unreal");
        Plant(b, 1000, " 4.27.0 ");
        Plant(b, 5000, "Unreal");
        Plant(b, 5100, "Release-4.27.2");
        auto r = tier23(b);
        EXPECT("G9: a later Tier 2 on the same needle now wins", r.tier == 2 && val(r) == 427);
    }
    {   // ⚠ THE ONE THE DESIGN COMMENT PROMISES AND THE CODE DID NOT DELIVER: a stray SDK
        // "5.5.0" near the start out-racing a real "Release-4.27" later in the module.
        // Pre-fix: 505 / Tier 3 — the wrong VERSION, not merely the wrong confidence.
        auto b = fresh(8192);
        Plant(b, 900, "Engine");
        Plant(b, 1000, " 5.5.0 ");            // bundled PhysX-style banner -> Tier 3
        Plant(b, 5000, "Unreal Engine");
        Plant(b, 5100, "Release-4.27.2");     // the real tag -> Tier 2
        auto r = tier23(b);
        EXPECT("G9: a real Release-4.27 beats a stray 5.5.0 SDK string", r.tier == 2 && val(r) == 427);
    }
    {   // REGRESSION RAIL: the canonical shape must be unaffected by either change.
        auto b = fresh(4096);
        Plant(b, 900, "Unreal");
        Plant(b, 1000, "Release-5.4.0");
        auto r = tier23(b);
        EXPECT("G8/G9 rail: canonical 'Release-5.4.0' is still Tier 2", r.tier == 2 && val(r) == 504);
    }
}

// ── audit #5 G11: Tier 2 required a THREE-component version, so it never fired ──
//
// MEASURED before and after over the 170 PE images in the local analyze corpus:
// Tier 2 fired 0/170 before and 6/170 after, and on all six its answer AGREES EXACTLY
// with the version Tier 1 independently reports (418/420/418/420/420/418) — two
// detectors cross-validating. Tier 1 returns first on all six, so no effective verdict
// changed on any binary we own; what changed is that Tier 2 now works as a fallback for
// images where the full "++UEx+Release-" tag is stripped but "Release-X.Y" survives.
static void Test_VersionTier2_BareNeedle_G11() {
    auto fresh = [](size_t n) { return std::vector<uint8_t>(n, 'x'); };
    auto tier23 = [](std::vector<uint8_t>& b) { return Genau::ScanVersionTier23(b.data(), b.size()); };
    auto val = [](const Genau::NeedleScanResult& r) {
        return r.found() ? Genau::kVersionNeedles[r.index].value : 0u;
    };

    {   // THE WHOLE FINDING. UE's real tag is TWO-component: no trailing dot, so the
        // needle "4.27." could never match it. Pre-G11 this was NOT detected at all.
        auto b = fresh(4096);
        Plant(b, 900, "Unreal Engine");
        Plant(b, 1000, "Release-4.27");        // nothing after — two-component
        auto r = tier23(b);
        EXPECT("G11: two-component 'Release-4.27' is now Tier 2", r.tier == 2 && val(r) == 427);
    }
    {   // REGRESSION RAIL: the three-component form must still work, unchanged.
        auto b = fresh(4096);
        Plant(b, 900, "Unreal Engine");
        Plant(b, 1000, "Release-5.4.2");
        auto r = tier23(b);
        EXPECT("G11 rail: three-component 'Release-5.4.2' is still Tier 2", r.tier == 2 && val(r) == 504);
    }
    {   // ⚠ THE GUARD THAT MAKES THE BARE MATCH SAFE. Without the "next byte is not a
        // digit" test, "5.4" matches inside "5.40" and reports a UE version from a GAME
        // version string. UE 5.40 does not exist; "Release 5.40" in a shipped binary does.
        auto b = fresh(4096);
        Plant(b, 900, "Unreal Engine");
        Plant(b, 1000, "Release 5.40 build");
        auto r = tier23(b);
        EXPECT("G11: 'Release 5.40' does NOT match the bare 5.4 needle", !r.found());
    }
    {   // ⚠ THE HOISTED GUARD. Tier 3 always rejected a preceding digit; Tier 2 never did,
        // and needs it far more now that it matches the shorter bare form.
        auto b = fresh(4096);
        Plant(b, 900, "Unreal Engine");
        Plant(b, 1000, "Release 15.4 patch");
        auto r = tier23(b);
        EXPECT("G11: a preceding digit ('15.4') is rejected by Tier 2 too", !r.found());
    }
    {   // and the dot form of the same trap
        auto b = fresh(4096);
        Plant(b, 900, "Unreal Engine");
        Plant(b, 1000, "Release 1.5.4 patch");
        auto r = tier23(b);
        EXPECT("G11: a preceding dot ('1.5.4') is rejected by Tier 2 too", !r.found());
    }
    {   // Tier 3 is untouched: it still demands the full three-component form.
        auto b = fresh(4096);
        Plant(b, 900, "Unreal");
        Plant(b, 1000, " 5.4.0 ");             // no "Release" -> Tier 3 only
        auto r = tier23(b);
        EXPECT("G11 rail: bare three-component is still Tier 3", r.tier == 3 && val(r) == 504);
    }
    {   // ...and a two-component token with NO "Release" must not become Tier 3, because
        // Tier 3's whole point is the three-component shape.
        auto b = fresh(4096);
        Plant(b, 900, "Unreal");
        Plant(b, 1000, " 5.4 ");
        auto r = tier23(b);
        EXPECT("G11 rail: two-component without 'Release' is NOT Tier 3", !r.found());
    }
}

// ── audit #5 G12: the sizeof(FProperty) family must move as ONE ─────────────
//
// FSTRUCTPROP_STRUCT / FARRAYPROP_INNER / FBOOLPROP_FIELDSIZE / FBYTEPROP_ENUM all name the
// same slot (the first subclass field, == sizeof(FProperty)); FENUMPROP_ENUM sits 8 bytes
// later because FEnumProperty declares FNumericProperty* UnderlyingProp first.
//
// They had FOUR independent writers and one of them — Genau's Step 2.5 default block — set
// only TWO members, leaving the other three at the UE5.0-era 0x78/0x78/0x80. Any run taking
// a "keeping defaults" exit then shipped a SPLIT family for the whole session: TArray
// element descriptors and every enum-name read 8 bytes off while struct reads were correct.
// Deterministic, first run, no concurrency. docs/test-games.md records Solarpunk resolving
// through exactly that heuristic fallback.
//
// This pins the INVARIANT. It cannot pin the wiring — no test target compiles Genau.cpp or
// Ubel.cpp — but making the helper the only sane way to express the family is the
// structural half, and this is the half a build can check.
static void Test_PropertyFamilyIsCoherent() {
    // Every real Offset_Internal this repo has measured, plus the neighbours a future
    // engine could plausibly land on.
    for (int propOff : { 0x40, 0x44, 0x48, 0x4C, 0x50 }) {
        DynOff::PropertyFamily f = DynOff::PropertyFamilyFor(propOff);
        EXPECT("G12: ArrayProperty::Inner shares the struct slot",     f.arrayInner    == f.structProp);
        EXPECT("G12: BoolProperty::FieldSize shares the struct slot",  f.boolFieldSize == f.structProp);
        EXPECT("G12: ByteProperty::Enum shares the struct slot",       f.byteEnum      == f.structProp);
        EXPECT("G12: EnumProperty::Enum is exactly 8 past ByteProperty's",
               f.enumEnum == f.byteEnum + 8);
    }

    // The two concrete layouts this repo actually ships against. 0x44 is the UE5.1.1+
    // default Step 2.5 writes; 0x48 is TQ2's measured value.
    DynOff::PropertyFamily ue5 = DynOff::PropertyFamilyFor(0x44);
    EXPECT("G12: Offset_Internal 0x44 -> family base 0x70", ue5.structProp == 0x70);
    EXPECT("G12: Offset_Internal 0x44 -> EnumProperty 0x78", ue5.enumEnum == 0x78);

    DynOff::PropertyFamily tq2 = DynOff::PropertyFamilyFor(0x48);
    EXPECT("G12: Offset_Internal 0x48 -> family base 0x74", tq2.structProp == 0x74);

    // The base-taking overload must agree with the offset-taking one — Ubel's corrector has
    // the base in hand, Genau has Offset_Internal, and the two must not diverge.
    EXPECT("G12: the two spellings agree",
           DynOff::PropertyFamilyAtBase(0x70).enumEnum == DynOff::PropertyFamilyFor(0x44).enumEnum);

    // ⚠ THE HISTORICAL SPLIT, asserted as a shape that must never be producible: the shipped
    // defect was STRUCT/BOOLSIZE at 0x70 with INNER/BYTEENUM at 0x78 and ENUMENUM at 0x80.
    // No input may yield a family whose members disagree like that.
    for (int propOff : { 0x40, 0x44, 0x48, 0x4C, 0x50 }) {
        DynOff::PropertyFamily f = DynOff::PropertyFamilyFor(propOff);
        const bool splitShape = (f.arrayInner == f.structProp + 8) || (f.byteEnum == f.structProp + 8);
        EXPECT("G12: the historical split shape is unreachable", !splitShape);
    }
}

static void Test_ShouldPublishEnumTable() {
    // audit #5, found while fixing U4: ResolveEnumValue's entry loop `break`s on a
    // mid-table read failure and the PARTIAL vector was published unconditionally
    // into a cache nothing in dll/src ever erases — permanently splitting one UEnum
    // so values below the break point resolve and values above render as raw ints.

    // The distinction this predicate exists to hold: a FAILED BuildLayout is a
    // complete answer, a BROKEN loop is not. Getting these backwards is the easy
    // mistake, and it is wrong in both directions — refusing to cache a member-less
    // UEnum re-probes on every lookup; caching a truncated one is the original bug.
    EXPECT("BuildLayout failed -> publish (member-less UEnum / not a UEnum at all)",
           Ubel::ShouldPublishEnumTable(false, 0, 0));
    EXPECT("BuildLayout failed -> publish, whatever the count args say",
           Ubel::ShouldPublishEnumTable(false, 12, 0));

    EXPECT("full table is published", Ubel::ShouldPublishEnumTable(true, 12, 12));
    EXPECT("a single-member table is published", Ubel::ShouldPublishEnumTable(true, 1, 1));
    EXPECT("truncated at the last entry is NOT published",
           !Ubel::ShouldPublishEnumTable(true, 12, 11));
    EXPECT("truncated at the first entry is NOT published",
           !Ubel::ShouldPublishEnumTable(true, 12, 0));

    // Neu range-checks count before the narrowing cast (its own AF1 fix), but this
    // predicate must not sign-convert a negative into a huge size_t and call it equal.
    EXPECT("a negative intended count is never published",
           !Ubel::ShouldPublishEnumTable(true, -1, 0));
}

static void Test_NameWitness() {
    // audit #5 U6 + F3's in-session half: the per-UObject name cache is keyed by an
    // address UE recycles, so after a level change every name-bearing reply served the
    // DESTROYED object's name while the class was read fresh — the two disagreed with
    // no error anywhere, and only restarting the game cleared it.
    using Ubel::NameWitness;

    EXPECT("identical witnesses match", (NameWitness{1234, 0} == NameWitness{1234, 0}));

    // `number` is load-bearing. Dropping it is exactly audit #5 U8, which shipped once
    // and made Slot_1 / Slot_2 / Slot_3 all render as "Slot" — so a witness that
    // ignored it would serve Slot_1's cached string for Slot_2 at a recycled address.
    EXPECT("differing only in Number is NOT a match",
           (NameWitness{1234, 1} != NameWitness{1234, 2}));
    EXPECT("differing only in ComparisonIndex is NOT a match",
           (NameWitness{1234, 7} != NameWitness{5678, 7}));
    EXPECT("differing in both is NOT a match", (NameWitness{1, 1} != NameWitness{2, 2}));

    // NAME_None is {0,0} and is a REAL name, not an "unset" sentinel — a witness that
    // treated zero as "no witness" would refuse to ever serve it from cache.
    EXPECT("NAME_None {0,0} is a legitimate matching witness",
           (NameWitness{0, 0} == NameWitness{0, 0}));
    EXPECT("a default-constructed witness equals NAME_None", (NameWitness{} == NameWitness{0, 0}));

    // Number is int32 and UE uses the full range; no sign-collapse in the compare.
    EXPECT("negative Number is distinguished from its positive twin",
           (NameWitness{9, -1} != NameWitness{9, 1}));
}

static void Test_Holes_NormalizeGuessedType() {
    using DT = Radar::DataType;
    // Every label GuessGapTypes can emit must normalize to a canonical property
    // string that BOTH Radar::TryDataTypeFromPropertyTypeName (DLL) and (verified
    // separately, C# side) SnapshotNumeric.TryFromHex accept — or to "" (drop).
    struct Case { const char* guess; const char* canon; };
    const Case cases[] = {
        {"Float",  "FloatProperty"},  {"Float?",  "FloatProperty"},
        {"Double", "DoubleProperty"}, {"Double?", "DoubleProperty"},
        {"Int32?", "IntProperty"},    {"Int16?",  "Int16Property"},
        {"Byte?",  "ByteProperty"},
    };
    for (const auto& c : cases) {
        std::string canon = Ubel::NormalizeGuessedTypeToProperty(c.guess);
        EXPECT(c.guess, canon == c.canon);
        DT dt;
        EXPECT("normalized resolves in Radar", Radar::TryDataTypeFromPropertyTypeName(canon, dt));
    }
    // Padding / Pointer? have no gameplay-numeric meaning -> dropped.
    EXPECT("Padding -> drop",  Ubel::NormalizeGuessedTypeToProperty("Padding").empty());
    EXPECT("Pointer? -> drop", Ubel::NormalizeGuessedTypeToProperty("Pointer?").empty());
    EXPECT("unknown -> drop",  Ubel::NormalizeGuessedTypeToProperty("Mystery").empty());
}

// ----- Macht::ParsePattern nibble wildcards ----------------------------------
// Mirrors the scanner's per-byte compare so a nibble pattern can be matched
// against a synthetic buffer without linking Macht.cpp (Win32/AVX2).
static bool PatMatchAt(const Macht::ParsedPattern& pat,
                       const uint8_t* mem, size_t memLen, size_t at) {
    if (at + pat.bytes.size() > memLen) return false;
    for (size_t j = 0; j < pat.bytes.size(); ++j)
        if ((mem[at + j] & pat.mask[j]) != pat.bytes[j]) return false;
    return true;
}

// Routine::SafeThread — the fix for the fast-fail that TWO generations of exception
// guards could not touch, because there was never an exception: `~std::thread()` on a
// still-joinable thread calls std::terminate() DIRECTLY.
//
// This test is its own negative control. Swap SafeThread back to std::thread and the
// test EXECUTABLE dies with 0xC0000409 instead of failing an assertion — the same
// fast-fail the game was taking. Verified that way before shipping.
static void Test_Routine_SafeThread() {
    std::printf("Test_Routine_SafeThread\n");

    // shared_ptr, captured BY VALUE, because the worker MUST outlive this frame.
    //
    // SafeThread's destructor DETACHES (Routine.h:82) — that is the behaviour under
    // test — so the thread below keeps running after its scope ends AND after this
    // whole function returns. Its loop is 50 x sleep_for(1ms), but Windows quantises
    // that to the ~15.6 ms timer tick, so it lives ~780 ms while the function returns
    // in ~25 ms.
    //
    // A by-reference capture of a plain local therefore left it doing `lock xadd`
    // into a RECLAIMED STACK FRAME for ~750 ms — across the frames of every later
    // test and then the CRT exit path. When one of those increments lands on a /GS
    // cookie the process dies with STATUS_STACK_BUFFER_OVERRUN (0xC0000409), with no
    // output and no assertion. That is exactly what PR #503's CI hit, twice
    // unreproducible locally: which byte gets hit depends on stack layout and timing,
    // so it is intermittent by construction. Adding unrelated tests moved the layout
    // enough to surface a defect that had been latent.
    //
    // ⚠ ASAN does NOT catch this by default — `detect_stack_use_after_return` is off
    // unless asked for. A clean ASAN run is not evidence against a stack UAF.
    auto ran = std::make_shared<std::atomic<int>>(0);

    // 1. Destructed while STILL RUNNING. This is the game-close case: the worker is
    //    mid-tick, nothing joined it, and the static destructor runs.
    {
        Routine::SafeThread t;
        t = std::thread([ran] {
            for (int i = 0; i < 50 && ran->load() < 1000; ++i) {
                ran->fetch_add(1);
                std::this_thread::sleep_for(std::chrono::milliseconds(1));
            }
        });
        EXPECT("running thread is joinable", t.joinable());
    }   // <-- std::thread would terminate() here
    EXPECT("survived destruction of a running thread", true);

    // 2. Destructed after the thread FINISHED but was never joined. Still joinable —
    //    the object holds an id — so std::thread terminates here too. This is the more
    //    common shape at process exit, where Windows has already killed the thread.
    {
        Routine::SafeThread t;
        t = std::thread([] {});
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
        EXPECT("finished-but-unjoined is still joinable", t.joinable());
    }
    EXPECT("survived destruction of a finished thread", true);

    // 3. Move-assigned OVER a live thread. std::thread's own move-assign terminates
    //    when the target is joinable — the exact shape Fern's m_stopping flag was
    //    added to dodge by hand.
    {
        Routine::SafeThread t;
        t = std::thread([] { std::this_thread::sleep_for(std::chrono::milliseconds(30)); });
        t = std::thread([] {});   // <-- std::thread would terminate() here
        EXPECT("survived move-assign over a live thread", true);
    }

    // 4. The ordinary path still works: joinable, join, then no longer joinable.
    {
        Routine::SafeThread t;
        std::atomic<bool> done{false};
        t = std::thread([&] { done.store(true); });
        t.join();
        EXPECT("join() ran the body", done.load());
        EXPECT("not joinable after join", !t.joinable());
    }   // destructor on a joined thread is a no-op for both types

    EXPECT("the running thread actually started", ran->load() > 0);
}

// B34 — Cheat Engine must never be auto-scanned as if it were the game.
//
// The first version of this guard was an exact-name list and shipped "verified". A live
// capture then showed the DLL scanning CE for 5.8 s and opening the game pipe inside it,
// because the real executable is cheatengine-x86_64-SSE4-AVX2.exe — a CPU-feature variant
// none of the three listed names matched. That exact string is the first assertion here.
static void Test_Grimoire_IsCheatEngineExeName() {
    std::printf("Test_Grimoire_IsCheatEngineExeName\n");

    // The name from the failing capture. If only one line of this test survives, this one.
    EXPECT("SSE4-AVX2 variant (the live miss)",
           IsCheatEngineExeName(L"cheatengine-x86_64-SSE4-AVX2.exe"));

    EXPECT("plain x86_64",   IsCheatEngineExeName(L"cheatengine-x86_64.exe"));
    EXPECT("i386",           IsCheatEngineExeName(L"cheatengine-i386.exe"));
    EXPECT("launcher shim",  IsCheatEngineExeName(L"Cheat Engine.exe"));
    EXPECT("case-insensitive", IsCheatEngineExeName(L"CHEATENGINE-X86_64.EXE"));
    // A variant that does not exist yet must also match — that is the point of a prefix.
    EXPECT("hypothetical future variant",
           IsCheatEngineExeName(L"cheatengine-x86_64-AVX512.exe"));

    // Anchored at the start, so a game that merely CONTAINS the words is not refused.
    EXPECT("substring is not enough",  !IsCheatEngineExeName(L"MyCheatEngineClone.exe"));
    EXPECT("unrelated game",           !IsCheatEngineExeName(L"DQ7R-Win64-Shipping.exe"));
    EXPECT("empty",                    !IsCheatEngineExeName(L""));
    EXPECT("null",                     !IsCheatEngineExeName(nullptr));
}

// AB1 (audit #5, HIGH) — DllMain started a 1 ms-poll thread in EVERY host including Cheat
// Engine, and CE FreeLibrary's plugin DLLs (Settings->Plugins->Add does LoadLibrary ->
// CEPlugin_GetVersion -> FreeLibrary; every CE exit unloads every plugin). A thread left
// running in an unmapped image crashes CE.
//
// The guard EXISTED — IsCheatEngineExeName — and had exactly one call site, INSIDE the
// thread it should have prevented from being created. The fix hoists the decision into
// DllMain, and this test exists because DllMain itself is unreachable from any test: the
// decision had to move somewhere a test can reach, taking the FULL path exactly as
// GetModuleFileNameW(nullptr, ...) hands it over.
static void Test_Grimoire_HostAllowsBackgroundThreads() {
    std::printf("Test_Grimoire_HostAllowsBackgroundThreads\n");

    // Refused for CE — full paths, because that is what the caller actually passes.
    EXPECT("CE full path (the live-miss variant)",
           !HostAllowsBackgroundThreads(
               L"C:\\Program Files\\Cheat Engine 7.7\\cheatengine-x86_64-SSE4-AVX2.exe"));
    EXPECT("CE plain x86_64 full path",
           !HostAllowsBackgroundThreads(L"D:\\CE\\cheatengine-x86_64.exe"));
    EXPECT("CE launcher shim full path",
           !HostAllowsBackgroundThreads(L"C:\\Tools\\Cheat Engine.exe"));
    EXPECT("bare leaf, no directory at all",
           !HostAllowsBackgroundThreads(L"cheatengine-i386.exe"));
    // A path can reach us with forward slashes; the leaf must still be found.
    EXPECT("forward slashes",
           !HostAllowsBackgroundThreads(L"C:/CE/cheatengine-x86_64.exe"));
    EXPECT("mixed separators, last one wins",
           !HostAllowsBackgroundThreads(L"C:\\Tools/CE\\cheatengine-x86_64.exe"));

    // Allowed for anything else — the ONLY host we refuse is CE.
    EXPECT("a real game",
           HostAllowsBackgroundThreads(
               L"D:\\Steam\\steamapps\\common\\DQ7R\\DQ7R-Win64-Shipping.exe"));
    // The directory may say Cheat Engine while the executable does not: only the leaf counts.
    EXPECT("CE in the DIRECTORY name, game as the leaf",
           HostAllowsBackgroundThreads(L"C:\\Cheat Engine 7.7\\SomeGame.exe"));
    EXPECT("substring is not enough",
           HostAllowsBackgroundThreads(L"C:\\Games\\MyCheatEngineClone.exe"));

    // FAILS OPEN. An unreadable host path must not silently disable the DLL for every
    // game; the shipped behaviour for a non-CE host is what we preserve.
    EXPECT("empty path fails open", HostAllowsBackgroundThreads(L""));
    EXPECT("null path fails open",  HostAllowsBackgroundThreads(nullptr));
}

// B46 — HexToBytes could not fail, so write_mem could not report a bad pattern.
static void Test_Renge_TryHexToBytes() {
    std::printf("Test_Renge_TryHexToBytes\n");
    std::vector<uint8_t> out;

    EXPECT("plain hex parses", Renge::TryHexToBytes("DEADBEEF", out));
    EXPECT("plain hex length", out.size() == 4);
    EXPECT("plain hex bytes",
           out.size() == 4 && out[0] == 0xDE && out[1] == 0xAD &&
           out[2] == 0xBE && out[3] == 0xEF);

    EXPECT("lowercase parses", Renge::TryHexToBytes("deadbeef", out));
    EXPECT("lowercase bytes", out.size() == 4 && out[0] == 0xDE && out[3] == 0xEF);

    // The exact string from the finding. strtoul turned every non-hex char into 0x00,
    // so this was WRITTEN INTO THE GAME as {DE,0A,0D,BE,0E} and answered ok:true.
    out.assign(1, 0xAA);
    EXPECT("spaced hex is REJECTED (not silently mangled)",
           !Renge::TryHexToBytes("DE AD BE EF", out));
    EXPECT("rejected input leaves out untouched", out.size() == 1 && out[0] == 0xAA);

    EXPECT("odd length rejected",     !Renge::TryHexToBytes("ABC", out));
    EXPECT("0x prefix rejected",      !Renge::TryHexToBytes("0xAB", out));
    EXPECT("non-hex letter rejected", !Renge::TryHexToBytes("ZZ", out));
    EXPECT("empty rejected",          !Renge::TryHexToBytes("", out));
}

// F5 — the response/event envelope must survive its payload.
//
// MakeResponse / MakeEvent used to splice the handler payload on with nlohmann's
// merge_patch (RFC 7386). Two of its behaviours are wrong for an envelope and neither is
// what any call site meant: a NULL value in the patch DELETES the key rather than setting
// it, and a NON-OBJECT patch replaces the whole target — which would drop "id"/"ok"
// outright. Assignment has neither.
//
// The negative control is the merge_patch line itself: put it back in ApplyPayload and
// the null case loses "value" while the non-object case loses "id" AND "ok".
//
// MakeResponse itself is not called here — it reads Stark::IsGameThreadResponsive, whose
// definition lives in Stark.cpp and is not linked into this target. ApplyPayload is the
// piece that decides, so it is the piece pinned (working-lessons §2.3: put the rule in a
// header and the header IS testable).
static void Test_Renge_ApplyPayloadKeepsEnvelope() {
    std::printf("Test_Renge_ApplyPayloadKeepsEnvelope\n");

    auto envelope = [] {
        nlohmann::json e;
        e["id"] = 7;
        e["ok"] = true;
        e["game_thread_stalled"] = false;
        return e;
    };

    // 1. Ordinary payload: every envelope key survives, every payload key lands.
    {
        nlohmann::json res = envelope();
        nlohmann::json data;
        data["total"] = 42;
        data["name"]  = "Actor";
        Renge::ApplyPayload(res, std::move(data));
        EXPECT("plain payload keeps id",      res.contains("id") && res["id"] == 7);
        EXPECT("plain payload keeps ok",      res.contains("ok") && res["ok"] == true);
        EXPECT("plain payload keeps stalled", res.contains("game_thread_stalled"));
        EXPECT("plain payload adds total",    res.contains("total") && res["total"] == 42);
        EXPECT_EQ_STR("plain payload adds name", res["name"].get<std::string>(), "Actor");
    }

    // 2. A NULL value must be SET, not delete the key. merge_patch removed it, so a
    //    handler answering {"value": null} shipped a response with no "value" at all.
    {
        nlohmann::json res = envelope();
        nlohmann::json data;
        data["value"] = nullptr;
        Renge::ApplyPayload(res, std::move(data));
        EXPECT("null payload value is PRESENT", res.contains("value"));
        EXPECT("null payload value is null",    res.contains("value") && res["value"].is_null());
    }

    // 3. A null that COLLIDES with an envelope key must not delete it. This is the one
    //    that turns a success into an unparseable response on the UI side.
    {
        nlohmann::json res = envelope();
        nlohmann::json data;
        data["ok"] = nullptr;
        Renge::ApplyPayload(res, std::move(data));
        EXPECT("colliding null keeps the key", res.contains("ok"));
        EXPECT("id survives a colliding null", res.contains("id") && res["id"] == 7);
    }

    // 4. A handler that deliberately overrides an envelope key still wins (the
    //    game_thread_stalled contract MakeResponse documents).
    {
        nlohmann::json res = envelope();
        nlohmann::json data;
        data["game_thread_stalled"] = true;
        Renge::ApplyPayload(res, std::move(data));
        EXPECT("handler override wins", res["game_thread_stalled"] == true);
    }

    // 5. A non-object payload cannot destroy the envelope. merge_patch REPLACED the
    //    target with it, i.e. "id" and "ok" simply ceased to exist.
    {
        nlohmann::json res = envelope();
        nlohmann::json arr = nlohmann::json::array({1, 2, 3});
        Renge::ApplyPayload(res, std::move(arr));
        EXPECT("non-object payload leaves an object", res.is_object());
        EXPECT("non-object payload keeps id", res.contains("id") && res["id"] == 7);
        EXPECT("non-object payload keeps ok", res.contains("ok") && res["ok"] == true);
    }

    // 6. Nested objects are REPLACED, not recursively merged. merge_patch would have
    //    kept `a` from the envelope's stale sub-object and produced a hybrid neither
    //    side wrote.
    {
        nlohmann::json res = envelope();
        res["inner"] = nlohmann::json{{"a", 1}};
        nlohmann::json data;
        data["inner"] = nlohmann::json{{"b", 2}};
        Renge::ApplyPayload(res, std::move(data));
        EXPECT("nested object replaced, not merged",
               res["inner"].is_object() && !res["inner"].contains("a")
               && res["inner"].contains("b"));
    }

    // 7. The rvalue overload actually MOVES: the source array is left empty rather than
    //    deep-copied. This is the half F5 was filed for (8192-object snapshot chunks
    //    copied a second time inside the game process's heap).
    {
        nlohmann::json res = envelope();
        nlohmann::json data;
        data["objects"] = nlohmann::json::array({1, 2, 3, 4});
        Renge::ApplyPayload(res, std::move(data));
        EXPECT("payload landed", res["objects"].size() == 4);
        // Deliberately NOT `is_array() && empty()`: a moved-from nlohmann value is left
        // as `null`, not as an empty array of its old type. Asserting "no longer holds
        // the four elements" detects the copy either way without pinning nlohmann's
        // moved-from representation — the first draft of this line asserted the
        // representation and failed against a working move.
        EXPECT("payload was moved out of, not copied",
               !data["objects"].is_array() || data["objects"].empty());
    }
}

// audit #5 AD24 — the three ENVELOPE BUILDERS themselves had no test, and the reason
// was real: "structurally UNLINKABLE from the only test target that includes it".
//
// My first pass called that premise false, reasoning that everything in Renge.h is
// `inline` so there is nothing to link. THE LINKER DISAGREED, and it was right:
// `MakeResponse` calls `Stark::IsGameThreadResponsive`, which is DECLARED in Stark.h but
// DEFINED in Stark.cpp — and no test target compiles Stark.cpp. `inline` makes the
// builder itself linkable; it does nothing for what the builder calls. MakeError and
// MakeEvent were always testable (neither touches Stark); MakeResponse was not.
//
// The one dependency is supplied below rather than by adding Stark.cpp to the target,
// which would drag in MinHook and the Win32 hook machinery for a JSON test. The stub is
// safe against drift in the way that matters: it has to match the declaration in Stark.h
// that Renge.h already pulls in, so a signature change is a compile error here, not a
// silently diverging fixture. It returns a FIXED value, which is why the assertions
// below pin the presence and type of `game_thread_stalled` and only assert its value in
// the one case that is about precedence rather than about liveness.
namespace Stark {
bool IsGameThreadResponsive(int32_t /*thresholdMs*/) { return true; }
}   // namespace Stark

static void Test_Renge_EnvelopeBuilders() {
    std::printf("Test_Renge_EnvelopeBuilders\n");

    // MakeResponse: id + ok=true + the liveness hint, with no payload at all.
    {
        nlohmann::json res = Renge::MakeResponse(11);
        EXPECT("MakeResponse sets id", res.contains("id") && res["id"] == 11);
        EXPECT("MakeResponse sets ok=true", res.contains("ok") && res["ok"] == true);
        EXPECT("MakeResponse always carries the liveness hint",
               res.contains("game_thread_stalled") && res["game_thread_stalled"].is_boolean());
        EXPECT("MakeResponse with no payload has exactly the envelope", res.size() == 3);
    }

    // A payload splices in WITHOUT displacing any envelope key.
    {
        nlohmann::json data;
        data["total"] = 3;
        data["items"] = nlohmann::json::array({1, 2, 3});
        nlohmann::json res = Renge::MakeResponse(12, std::move(data));
        EXPECT("payload keeps id", res["id"] == 12);
        EXPECT("payload keeps ok", res["ok"] == true);
        EXPECT("payload keeps liveness hint", res.contains("game_thread_stalled"));
        EXPECT("payload lands", res["total"] == 3 && res["items"].size() == 3);
    }

    // The documented precedence: a handler that sets game_thread_stalled itself WINS,
    // because ApplyPayload runs after the envelope is stamped. This is the one envelope
    // key a payload is allowed to overwrite, and the comment on MakeResponse says so.
    {
        nlohmann::json data;
        data["game_thread_stalled"] = true;
        nlohmann::json res = Renge::MakeResponse(13, std::move(data));
        EXPECT("handler's own liveness value wins over the envelope's",
               res["game_thread_stalled"] == true);
    }

    // A non-object payload is refused rather than allowed to replace the envelope.
    {
        nlohmann::json res = Renge::MakeResponse(14, nlohmann::json::array({9}));
        EXPECT("array payload cannot destroy the response envelope",
               res.is_object() && res["id"] == 14 && res["ok"] == true);
    }

    // MakeError: ok=false and an error string, and NO liveness hint — it does not go
    // through MakeResponse, which is easy to "tidy up" into a shared path by accident.
    {
        nlohmann::json err = Renge::MakeError(15, "boom");
        EXPECT("MakeError sets id", err["id"] == 15);
        EXPECT("MakeError sets ok=false", err.contains("ok") && err["ok"] == false);
        EXPECT_EQ_STR("MakeError carries the message", err["error"].get<std::string>(), "boom");
        EXPECT("MakeError is exactly id+ok+error", err.size() == 3);
    }

    // MakeEvent: an "event" key, NO id (the UI routes on the absence of one), and the
    // same payload rule as MakeResponse.
    {
        nlohmann::json evt = Renge::MakeEvent("scan_progress");
        EXPECT_EQ_STR("MakeEvent names the event", evt["event"].get<std::string>(), "scan_progress");
        EXPECT("MakeEvent carries no id — that is how a push is told from a response",
               !evt.contains("id"));

        nlohmann::json data;
        data["pct"] = 40;
        nlohmann::json evt2 = Renge::MakeEvent("scan_progress", std::move(data));
        EXPECT("event payload lands", evt2["pct"] == 40);
        EXPECT_EQ_STR("event key survives its payload",
                      evt2["event"].get<std::string>(), "scan_progress");

        // The envelope-destroying case for events, same as response #4 above.
        nlohmann::json evt3 = Renge::MakeEvent("tick", nlohmann::json::array({1}));
        EXPECT("array payload cannot destroy the event envelope",
               evt3.is_object() && evt3.contains("event"));
    }
}

// B4 — the mailbox poller needs immunity from the PER-COMMAND cancel WITHOUT being
// classified a background worker. One flag used to answer both questions; this asserts
// they are now genuinely independent. The second EXPECT in the poller block is the
// negative control for the tempting one-line "fix": calling MarkBackgroundWorker() on
// the poller would satisfy the first assertion and break this one, which is what would
// make UE5_CallProcessEventEx refuse (-8) the user's one-shot CE invokes.
//
// Each role runs on its own thread because the flags are thread_local; the threads are
// joined one at a time, so the shared g_pass/g_fail counters are never raced.
static void Test_Tot_CancelImmunityVsBackgroundWorker() {
    std::printf("Test_Tot_CancelImmunityVsBackgroundWorker\n");

    Tot::ResetPerCommand();
    Tot::ResetShutdown();

    // A fresh thread starts unmarked — the pipe-command case.
    std::thread([] {
        EXPECT("unmarked thread: no cancel pending",   !Tot::Requested());
        EXPECT("unmarked thread: not a bg worker",     !Tot::IsBackgroundWorker());
        Tot::RequestPerCommand();
        EXPECT("unmarked thread HONOURS per-command",   Tot::Requested());
    }).join();
    // g_perCommand stays latched from here on — exactly the state a CE-only session is
    // stuck in after a UI client dies mid-command (nothing clears it without Fern::Start).

    // The Mimic poller: immune to the pipe's cancel, but NOT a background worker.
    std::thread([] {
        Tot::MarkCancelImmune();
        EXPECT("poller IGNORES the latched per-command", !Tot::Requested());
        EXPECT("poller is NOT a background worker",      !Tot::IsBackgroundWorker());
        Tot::RequestShutdown();
        EXPECT("poller still aborts on real shutdown",    Tot::Requested());
        Tot::ResetShutdown();
    }).join();

    // A re-assert worker: unchanged on both axes (MarkBackgroundWorker sets both flags).
    std::thread([] {
        Tot::MarkBackgroundWorker();
        EXPECT("bg worker ignores per-command",  !Tot::Requested());
        EXPECT("bg worker reports as bg worker",  Tot::IsBackgroundWorker());
        Tot::RequestShutdown();
        EXPECT("bg worker aborts on shutdown",    Tot::Requested());
        Tot::ResetShutdown();
    }).join();

    Tot::ResetPerCommand();
    Tot::ResetShutdown();
}

static void Test_Sig_IsCeReplayableAob() {
    std::printf("Test_Sig_IsCeReplayableAob\n");

    // CE replays the triple as exactly ONE step: addr = match + len + i32[match + pos],
    // which yields the RIP TARGET. So the question is not "is `pattern` bytes?" but "is
    // the answer the RIP target?".
    EXPECT("RipDirect replayable",        IsCeReplayableAob(AobResolve::RipDirect));
    EXPECT("RipBoth replayable (form)",   IsCeReplayableAob(AobResolve::RipBoth));

    // ⚠ RipDeref is NOT replayable, and this assertion was INVERTED until build 3262
    // (audit #5 AD10) — the test pinned the defect, which is why nothing caught it.
    // RipDeref's answer is one further load THROUGH the RIP target, and the triple has
    // no field in which to say so; a CE script built from it registers the
    // pointer-to-pointer slot as though it were the pointer.
    EXPECT("RipDeref NOT replayable",    !IsCeReplayableAob(AobResolve::RipDeref));

    // ⛔ RipBoth passing above is NECESSARY, NOT SUFFICIENT, and no publish site may use
    // this predicate alone. Which of RipBoth's two arms won is a RUNTIME fact an enum
    // cannot carry, and the deref arm has RipDeref's exact problem — as does a non-zero
    // `adjustment`, which the triple also cannot express. Genau::CeReplayMatchesResolved
    // settles both by replaying the triple and comparing it to the address published.
    // Every GWorld entry is RipBoth, so that is the live path, not a hypothetical.

    // SymbolExport / SymbolCallFollow keep an MSVC MANGLED NAME in `pattern`. Publishing
    // one made the UI's "an AOB is available" test (non-empty string) true, and every
    // address in the exported CE table then resolved to `??` (audit #4 B2).
    EXPECT("SymbolExport NOT replayable", !IsCeReplayableAob(AobResolve::SymbolExport));
    EXPECT("SymbolCallFollow NOT repl.",  !IsCeReplayableAob(AobResolve::SymbolCallFollow));

    // CallFollow's pattern IS a byte string, but the address comes from following the
    // CALL and scanning the callee — no fixed offset into the match can express that.
    EXPECT("CallFollow NOT replayable",   !IsCeReplayableAob(AobResolve::CallFollow));

    // The structural reason the gate is needed at all: every non-replayable form also
    // carries instrOffset/opcodeLen/totalLen = 0, so the published range would be the
    // degenerate [0,0) even if the pattern itself were scannable. Assert that over the
    // REAL shipped tables rather than trusting the macros.
    int checkedExports = 0, checkedCallFollow = 0;
    auto sweep = [&](const AobSignature* tbl, size_t n) {
        for (size_t i = 0; i < n; ++i) {
            const auto& sig = tbl[i];
            if (IsCeReplayableAob(sig.resolve)) continue;
            // RipDeref is non-replayable but its geometry is REAL and non-zero — it needs
            // instrOffset to reach the RIP target before the extra load. The zero-geometry
            // property below belongs to the forms whose `pattern` is not bytes at all, so
            // asserting it over RipDeref would demand a broken entry. Vacuous today (no
            // table has a RipDeref entry); stated so that adding one is not a false alarm.
            if (sig.resolve == AobResolve::RipDeref) continue;
            EXPECT("non-replayable has zero instrOffset", sig.instrOffset == 0);
            EXPECT("non-replayable has zero opcodeLen",   sig.opcodeLen   == 0);
            EXPECT("non-replayable has zero totalLen",    sig.totalLen    == 0);
            if (sig.resolve == AobResolve::SymbolExport ||
                sig.resolve == AobResolve::SymbolCallFollow) ++checkedExports;
            if (sig.resolve == AobResolve::CallFollow) ++checkedCallFollow;
        }
    };
    sweep(Sig::GOBJECTS_PATTERNS, std::size(Sig::GOBJECTS_PATTERNS));
    sweep(Sig::GNAMES_PATTERNS,   std::size(Sig::GNAMES_PATTERNS));
    sweep(Sig::GWORLD_PATTERNS,   std::size(Sig::GWORLD_PATTERNS));
    sweep(Sig::GENGINE_PATTERNS,  std::size(Sig::GENGINE_PATTERNS));
    // A gate nothing exercises is a gate that silently stops guarding.
    EXPECT("tables still carry symbol entries",     checkedExports > 0);
    EXPECT("tables still carry a CallFollow entry", checkedCallFollow > 0);
}

// audit #5 AA38 — the Pass-2 (multi-module) admission rule as a truth table.
//
// The whole legal space is 3 anchor states x 2 candidate placements x 2 producer
// flags = 12 rows, and every one is asserted so the table cannot drift silently.
// (The predicate deliberately does NOT take two booleans for the anchor: that shape
// has 4 combinations of which 1 is unreachable, and the unreachable one is exactly
// the "meaningless, pass false here" parameter the audit's own lesson forbids.)
// audit #5 A11 — how refine treats a container-element candidate whose stored
// ABSOLUTE address may no longer denote the element it was scanned from.
//
// Every case below maps to a mechanism in the vendored 5.8 engine source, and the
// non-regression cases matter as much as the drops: a container-wide "did anything
// change" rule would drop every candidate in an array that merely APPENDED, and
// those candidates are correct today.
static void Test_Radar_RefineContainerAnchor() {
    std::printf("Test_Radar_RefineContainerAnchor\n");
    using Radar::ValueAnchor;
    using Radar::RefineAnchorVerdict;
    const auto V = &Radar::RefineContainerAnchor;

    constexpr uintptr_t kData = 0x2000;
    // anchor, idx, numAtScan, dataAtScan, nowData, nowNum, slotAllocated

    // --- non-container anchors are untouched ---------------------------------
    EXPECT("direct field is never re-anchored",
           V(ValueAnchor::Direct, -1, -1, 0, 0, 0, true) == RefineAnchorVerdict::KeepAddress);
    EXPECT("an UNSTAMPED anchor keeps the pre-A11 behaviour",
           V(ValueAnchor::Unknown, 3, 8, kData, 0x9000, 9, true)
               == RefineAnchorVerdict::KeepAddress);

    // --- the NON-REGRESSION cases (these are why the rule is index-aware) -----
    // Array.h AddUninitialized: `if (ArrayNum == ArrayMax) grow; else ArrayNum++`.
    // Appending into slack relocates NOTHING, so every existing element address is
    // still correct and must survive.
    EXPECT("append with slack does NOT drop",
           V(ValueAnchor::ArrayElement, 3, 8, kData, kData, 9, true)
               == RefineAnchorVerdict::KeepAddress);
    EXPECT("an unchanged container keeps its address",
           V(ValueAnchor::ArrayElement, 3, 8, kData, kData, 8, true)
               == RefineAnchorVerdict::KeepAddress);
    // A sparse Add into a FREE slot elsewhere leaves our slot exactly where it was.
    EXPECT("sparse add elsewhere does NOT drop our slot",
           V(ValueAnchor::SparseElement, 3, 8, kData, kData, 8, /*slotAllocated=*/true)
               == RefineAnchorVerdict::KeepAddress);

    // --- the GAIN: a grown container is recovered, not lost ------------------
    // Today a growth realloc leaves every element address stale and the candidates
    // are simply gone. Slot order is preserved, so they are recomputable.
    EXPECT("growth realloc REPOINTS instead of losing the candidate",
           V(ValueAnchor::ArrayElement, 3, 8, kData, 0x9000, 12, true)
               == RefineAnchorVerdict::Repoint);
    EXPECT("sparse realloc repoints too",
           V(ValueAnchor::SparseElement, 3, 8, kData, 0x9000, 12, true)
               == RefineAnchorVerdict::Repoint);

    // --- the DROPS, each an actual silent-wrong-value today ------------------
    // Array.h RemoveAtImpl -> RelocateConstructItems: the tail shifts DOWN one slot,
    // so the pinned address stays mapped and now holds the NEIGHBOUR's value.
    EXPECT("array shrank (RemoveAt shifted the tail) drops",
           V(ValueAnchor::ArrayElement, 3, 8, kData, kData, 7, true)
               == RefineAnchorVerdict::Drop);
    EXPECT("element index past the end drops",
           V(ValueAnchor::ArrayElement, 9, 8, kData, kData, 5, true)
               == RefineAnchorVerdict::Drop);
    // SparseArray.h AddUninitialized reuses FirstFreeIndex: a removed slot is
    // REFILLED in place, so the address is identical and reads as a live value.
    // The allocation bit is the only exact witness.
    EXPECT("sparse slot freed drops (address is identical — only the bit knows)",
           V(ValueAnchor::SparseElement, 3, 8, kData, kData, 8, /*slotAllocated=*/false)
               == RefineAnchorVerdict::Drop);
    // A sparse MaxIndex shrink means Compact() ran, which DOES relocate.
    EXPECT("sparse compact drops",
           V(ValueAnchor::SparseElement, 3, 8, kData, kData, 6, true)
               == RefineAnchorVerdict::Drop);

    // --- missing bookkeeping degrades, it does not destroy -------------------
    EXPECT("a container anchor with no element index falls back, not drops",
           V(ValueAnchor::ArrayElement, -1, 8, kData, 0x9000, 9, true)
               == RefineAnchorVerdict::KeepAddress);
    EXPECT("a container anchor with no scan-time count falls back, not drops",
           V(ValueAnchor::ArrayElement, 3, -1, kData, 0x9000, 9, true)
               == RefineAnchorVerdict::KeepAddress);

    // --- the address recomputation ------------------------------------------
    EXPECT("element address = data + idx*stride + intra",
           Radar::ContainerElemAddr(0x1000, 3, 16, 4) == 0x1000u + 3u * 16u + 4u);
    EXPECT("element 0 with no intra offset is the buffer base",
           Radar::ContainerElemAddr(0x1000, 0, 16, 0) == 0x1000u);

    static_assert(Radar::RefineContainerAnchor(ValueAnchor::ArrayElement, 3, 8,
                                               0x2000, 0x2000, 7, true)
                      == RefineAnchorVerdict::Drop,
                  "RefineContainerAnchor must be constexpr-evaluable");
}

// audit #5 A12 — the group-scan half. The RULE is the same one above; what is new is
// the anchor a deep leaf carries to reach it, and the two ways that wiring can be
// silently wrong (the sparse count's UNIT, and the leaf-depth off-by-one).
static void Test_Radar_LeafAnchor() {
    std::printf("Test_Radar_LeafAnchor\n");
    using Radar::ValueAnchor;
    using Radar::RefineAnchorVerdict;

    // --- the depth rule, in the one place it lives -----------------------------
    // Leaves are emitted with `depth + 1`, so the container header is a direct field
    // of the scanned object exactly when the LEAF's depth is 1. Writing this test at
    // the call site with the walker's own `depth` is an off-by-one no target can catch,
    // because no test target compiles Aura.cpp.
    EXPECT("depth-1 array leaf is anchorable",
           Radar::AnchorKindForLeaf(/*isSparse=*/false, 1) == ValueAnchor::ArrayElement);
    EXPECT("depth-1 sparse leaf is anchorable",
           Radar::AnchorKindForLeaf(/*isSparse=*/true, 1) == ValueAnchor::SparseElement);
    EXPECT("depth-2 leaf is NOT anchorable (its header is inside an outer buffer)",
           Radar::AnchorKindForLeaf(false, 2) == ValueAnchor::UnverifiableNested);
    EXPECT("depth-0 is not a container leaf at all",
           Radar::AnchorKindForLeaf(false, 0) == ValueAnchor::UnverifiableNested);

    // --- UnverifiableNested must behave EXACTLY like Unknown -------------------
    // Its whole reason to exist is to be distinguishable from a dropped stamp while
    // changing nothing, so the "changes nothing" half needs pinning too.
    EXPECT("nested leaf does not Repoint on a moved buffer",
           Radar::RefineContainerAnchor(ValueAnchor::UnverifiableNested, 3, 8,
                                        0x2000, 0x9000, 9, true)
               == RefineAnchorVerdict::KeepAddress);
    EXPECT("nested leaf does not Drop on an out-of-range index",
           Radar::RefineContainerAnchor(ValueAnchor::UnverifiableNested, 9, 8,
                                        0x2000, 0x2000, 5, true)
               == RefineAnchorVerdict::KeepAddress);

    // --- the factories: a defaulted / half-wired anchor must DEGRADE -----------
    // Both downstream hops default-construct their struct, so a dropped assignment has
    // to land on values the rule refuses to act on. `num` defaulting to 0 instead of -1
    // would pass the `numAtScan < 0` guard and then pass the shrink test.
    const Radar::LeafAnchor defaulted{};
    EXPECT("a defaulted anchor is Unknown", defaulted.kind == ValueAnchor::Unknown);
    EXPECT("a defaulted anchor's count is -1, not 0", defaulted.num == -1);
    EXPECT("a defaulted anchor cannot act",
           Radar::RefineContainerAnchor(defaulted.kind, 3, defaulted.num, defaulted.data,
                                        0x9000, 9, true) == RefineAnchorVerdict::KeepAddress);
    const Radar::LeafAnchor direct = Radar::MakeDirectLeafAnchor();
    EXPECT("a direct anchor is Direct, not Unknown", direct.kind == ValueAnchor::Direct);
    // THE half-wired shape that actually matters: a hop copies `kind` faithfully and
    // drops the value fields. The zero `data` is what stops the rule acting — without
    // that guard this Repoints to the buffer base, collapsing every leaf onto element 0.
    EXPECT("a stamped kind with no buffer base cannot act",
           Radar::RefineContainerAnchor(ValueAnchor::ArrayElement, 3, /*numAtScan=*/0,
                                        /*dataAtScan=*/0, 0x9000, 9, true)
               == RefineAnchorVerdict::KeepAddress);

    // --- the sparse UNIT trap -------------------------------------------------
    // The walker's own loop bound is TSparseArray::MaxCapacity; refine re-reads
    // MaxIndex, and MaxCapacity >= MaxIndex is enforced by ReadTSparseArray. Stamping
    // the local that happens to be in hand would make numAtScan exceed nowNum for every
    // TSet/TMap with a spare slot, so the shrink rule would DROP them all on the first
    // Next Scan. The factory takes `maxIndex` by name; this asserts what that buys.
    const auto sparse = Radar::MakeSparseLeafAnchor(0x1000, 0x2000, /*maxIndex=*/8, 1);
    EXPECT("a sparse container with spare capacity is NOT dropped",
           Radar::RefineContainerAnchor(sparse.kind, 3, sparse.num, sparse.data,
                                        /*nowData=*/0x2000, /*nowNum=*/8, true)
               == RefineAnchorVerdict::KeepAddress);
    EXPECT("...but stamping the CAPACITY instead would drop it",
           Radar::RefineContainerAnchor(sparse.kind, 3, /*numAtScan=*/64, sparse.data,
                                        0x2000, /*nowNum=*/8, true)
               == RefineAnchorVerdict::Drop);

    // --- the repoint, which is exact by construction ---------------------------
    // Every leaf in a moved buffer shifts by the same delta, whatever its element
    // index, stride or intra-element offset were — so a wrong intra can only fail to
    // help, and can never relocate a candidate onto a neighbouring field.
    EXPECT("a moved buffer shifts a leaf by exactly the buffer delta",
           Radar::RepointByBufferMove(0x2044, 0x2000, 0x9000) == 0x9044u);
    EXPECT("an unmoved buffer leaves the leaf alone",
           Radar::RepointByBufferMove(0x2044, 0x2000, 0x2000) == 0x2044u);

    // The array factory keeps the logical count (slack slots hold stale data).
    const auto arr = Radar::MakeArrayLeafAnchor(0x1000, 0x2000, /*count=*/8, 1);
    EXPECT("an appended-into-slack array is not dropped",
           Radar::RefineContainerAnchor(arr.kind, 3, arr.num, arr.data, 0x2000, 9, true)
               == RefineAnchorVerdict::KeepAddress);
    EXPECT("a grown+realloc'd array repoints",
           Radar::RefineContainerAnchor(arr.kind, 3, arr.num, arr.data, 0x9000, 12, true)
               == RefineAnchorVerdict::Repoint);

    // Deliberately asserts a DEEP depth, not depth 1: the depth-1 rule is what the
    // negative control reverts, and a static_assert over the controlled line turns that
    // control into a compile abort that structurally cannot show its own
    // "everything else stayed green" half. This still proves constexpr-evaluability.
    static_assert(Radar::AnchorKindForLeaf(true, 5) == ValueAnchor::UnverifiableNested,
                  "AnchorKindForLeaf must be constexpr-evaluable");
    static_assert(Radar::MakeDirectLeafAnchor().num == -1,
                  "a direct anchor must carry the no-count sentinel");
}

static void Test_Genau_AdmitMultiModuleCandidate() {
    std::printf("Test_Genau_AdmitMultiModuleCandidate\n");
    using Genau::AnchorState;
    using Genau::ModuleAdmission;
    const auto admit = &Genau::AdmitMultiModuleCandidate;

    // --- THE DEFECT: unanchored + foreign + not the anchor producer -----------
    // python.exe / Solarpunk published a GWorld out of an arbitrary loaded module on
    // a run where GObjects never validated at all.
    EXPECT("AA38: unanchored foreign candidate is refused",
           admit(AnchorState::None, /*candIsMainExe=*/false, /*producesAnchor=*/false)
               == ModuleAdmission::RefuseUnanchored);

    // --- GObjects' OWN Pass 2 must still be admitted --------------------------
    // It runs before any anchor can exist. Refusing it would break the modular
    // builds (Satisfactory 4.25) that multi-module scanning was added for.
    EXPECT("AA38: the anchor producer is exempt while unanchored",
           admit(AnchorState::None, false, /*producesAnchor=*/true)
               == ModuleAdmission::Accept);

    // --- today's EOSSDK behaviour must survive unchanged ----------------------
    EXPECT("monolithic anchor + foreign candidate is refused",
           admit(AnchorState::MainExe, false, false) == ModuleAdmission::RefuseForeignMonolithic);
    EXPECT("monolithic anchor refuses the producer too",
           admit(AnchorState::MainExe, false, true) == ModuleAdmission::RefuseForeignMonolithic);

    // --- a genuinely modular build is untouched -------------------------------
    EXPECT("modular anchor admits a foreign candidate",
           admit(AnchorState::ForeignDll, false, false) == ModuleAdmission::Accept);
    EXPECT("modular anchor admits the producer",
           admit(AnchorState::ForeignDll, false, true) == ModuleAdmission::Accept);

    // --- a MAIN-MODULE candidate is admitted in every state -------------------
    // This is what keeps the FORBIDDEN fix forbidden. GWLD_DI427_1/2 are UE4.27
    // write-site patterns that resolve &GWorld from a `GWorld = nullptr` store before
    // any world exists, so a main-module hit whose *GWorld reads 0 MUST be accepted —
    // tightening `world == 0` globally would delete two Tier-1 patterns.
    for (auto st : { AnchorState::None, AnchorState::MainExe, AnchorState::ForeignDll }) {
        for (bool prod : { false, true }) {
            EXPECT("main-module candidate is always admitted",
                   admit(st, /*candIsMainExe=*/true, prod) == ModuleAdmission::Accept);
        }
    }

    // The rule must be pure — it reads no global state and cannot be made to depend on
    // one, which is the only reason a table this small is worth anything.
    static_assert(Genau::AdmitMultiModuleCandidate(AnchorState::MainExe, false, false)
                      == ModuleAdmission::RefuseForeignMonolithic,
                  "AdmitMultiModuleCandidate must be constexpr-evaluable");
}

static void Test_Macht_ParsePattern_Nibble() {
    std::printf("Test_Macht_ParsePattern_Nibble\n");
    Macht::ParsedPattern p;

    // Classic full-byte + full-wildcard still parses correctly.
    EXPECT("parse classic",           Macht::ParsePattern("48 8B 05 ?? ?? ?? ??", p));
    EXPECT("classic size 7",          p.bytes.size() == 7);
    EXPECT("classic mask[0]=0xFF",    p.mask[0]  == 0xFF);
    EXPECT("classic byte[0]=0x48",    p.bytes[0] == 0x48);
    EXPECT("classic mask[3]=0x00",    p.mask[3]  == 0x00);   // wildcard
    EXPECT("classic byte[3]=0x00",    p.bytes[3] == 0x00);
    EXPECT("classic anchor at 0",     p.anchorOffset == 0);
    EXPECT("classic anchorByte 0x48", p.anchorByte == 0x48);

    // High-nibble fixed: "4?" matches 0x40-0x4F only (any REX.WRXB).
    EXPECT("parse 4?",       Macht::ParsePattern("4?", p));
    EXPECT("4? mask 0xF0",   p.mask[0]  == 0xF0);
    EXPECT("4? byte 0x40",   p.bytes[0] == 0x40);
    { uint8_t m40[1]={0x40}, m4f[1]={0x4F}, m50[1]={0x50}, m3f[1]={0x3F};
      EXPECT("4? matches 0x40",  PatMatchAt(p, m40,1,0));
      EXPECT("4? matches 0x4F",  PatMatchAt(p, m4f,1,0));
      EXPECT("4? rejects 0x50", !PatMatchAt(p, m50,1,0));
      EXPECT("4? rejects 0x3F", !PatMatchAt(p, m3f,1,0)); }
    EXPECT("4? never anchors", p.anchorOffset == -1);   // nibble can't AVX2-broadcast

    // Low-nibble fixed: "?5" matches low nibble 5.
    EXPECT("parse ?5",     Macht::ParsePattern("?5", p));
    EXPECT("?5 mask 0x0F", p.mask[0]  == 0x0F);
    EXPECT("?5 byte 0x05", p.bytes[0] == 0x05);
    { uint8_t a[1]={0x35}, b[1]={0xF5}, c[1]={0x34};
      EXPECT("?5 matches 0x35",  PatMatchAt(p, a,1,0));
      EXPECT("?5 matches 0xF5",  PatMatchAt(p, b,1,0));
      EXPECT("?5 rejects 0x34", !PatMatchAt(p, c,1,0)); }

    // Anchor selection skips nibble bytes, picks the first FULL literal.
    EXPECT("parse ?? 4? 8B",       Macht::ParsePattern("?? 4? 8B", p));
    EXPECT("anchor at full lit 2", p.anchorOffset == 2);
    EXPECT("anchorByte 0x8B",      p.anchorByte == 0x8B);

    // Real tightening use: "4? 8B" accepts a REX.W mov, rejects a non-REX byte.
    EXPECT("parse 4? 8B", Macht::ParsePattern("4? 8B", p));
    { uint8_t good[2]={0x4C,0x8B}, bad[2]={0x3C,0x8B};
      EXPECT("4? 8B matches 4C 8B",  PatMatchAt(p, good,2,0));
      EXPECT("4? 8B rejects 3C 8B", !PatMatchAt(p, bad,2,0)); }

    // Malformed / empty tokens rejected.
    EXPECT("rejects G5",    !Macht::ParsePattern("G5", p));
    EXPECT("rejects empty", !Macht::ParsePattern("", p));
}


// ============================================================
// DynOff — FFieldClass::Name offset probe (the UE 5.8 drill-down fix)
//
// UE 5.8 made ~FFieldClass() virtual, inserting a vfptr at +0x00 and moving
// FName Name from +0x00 to +0x08. This was the ONLY UE offset never probed, and
// when wrong the damage is total but silent: the ChildProperties probe matches
// nothing at any offset, validation gives up on defaults, and the walker types
// every property as Scalar -> no drill-down in the Live Walker.
//
// The picker is reader-templated exactly so it can be tested here with no game.
// ============================================================
// ================================================================
// Ubel::PreviewScalarValue — audit U17 (the layout half of U3)
//
// The byte-blind decoder cannot tell 3 doubles from 6 floats, because size does
// not determine member width and neither does the engine version — one UE5 game
// holds fields of both. The only thing that settles it is the property's OWN
// declared width, which is what this reads. Everything below is the case U3's
// test 6 had to leave asserted-as-broken.
// ================================================================
static void Test_Ubel_PreviewScalarValue() {
    std::printf("Test_Ubel_PreviewScalarValue\n");

    // --- The LWC case, member by member. 8 bytes read as a double, not two floats.
    {
        double d = 1234.5;
        uint8_t b[8]; memcpy(b, &d, 8);
        EXPECT("DoubleProperty reads all 8 bytes",
               Ubel::PreviewScalarValue("DoubleProperty", b, 8) == "1234.5");
        // The SAME bytes as a float are the garbage the old path printed. Asserting
        // this pins that width comes from the property, never from the buffer.
        EXPECT("FloatProperty at width 4 is a different, wrong reading",
               Ubel::PreviewScalarValue("FloatProperty", b, 4) != "1234.5");
    }
    {
        double d = -678.25;
        uint8_t b[8]; memcpy(b, &d, 8);
        EXPECT("negative double", Ubel::PreviewScalarValue("DoubleProperty", b, 8) == "-678.25");
    }

    // --- Float stays float.
    {
        float f = 90.0f;
        uint8_t b[4]; memcpy(b, &f, 4);
        EXPECT("FloatProperty", Ubel::PreviewScalarValue("FloatProperty", b, 4) == "90");
    }

    // --- Integers, signed and unsigned, at their real widths. A UInt32 holding
    //     0xFFFFFFFF must not print as -1: that is the sign-leak family AB4 was.
    {
        uint8_t b[8] = {};
        int32_t i = -42; memcpy(b, &i, 4);
        EXPECT("IntProperty signed", Ubel::PreviewScalarValue("IntProperty", b, 4) == "-42");
        uint32_t u = 0xFFFFFFFFu; memcpy(b, &u, 4);
        EXPECT("UInt32Property is NOT sign-extended",
               Ubel::PreviewScalarValue("UInt32Property", b, 4) == "4294967295");
        int64_t i64 = -5000000000LL; memcpy(b, &i64, 8);
        EXPECT("Int64Property", Ubel::PreviewScalarValue("Int64Property", b, 8) == "-5000000000");
        int16_t i16 = -300; memcpy(b, &i16, 2);
        EXPECT("Int16Property", Ubel::PreviewScalarValue("Int16Property", b, 2) == "-300");
    }

    // --- Bool / byte.
    {
        uint8_t b[1] = { 0 };
        EXPECT("BoolProperty false", Ubel::PreviewScalarValue("BoolProperty", b, 1) == "false");
        b[0] = 1;
        EXPECT("BoolProperty true",  Ubel::PreviewScalarValue("BoolProperty", b, 1) == "true");
        b[0] = 200;
        EXPECT("ByteProperty",       Ubel::PreviewScalarValue("ByteProperty", b, 1) == "200");
    }

    // --- Impure types return "" so the caller supplies them. This is the seam that
    //     keeps the function pure and therefore testable at all.
    {
        uint8_t b[16] = {};
        EXPECT("NameProperty is deferred",   Ubel::PreviewScalarValue("NameProperty", b, 8).empty());
        EXPECT("ObjectProperty is deferred", Ubel::PreviewScalarValue("ObjectProperty", b, 8).empty());
        EXPECT("unknown type is deferred",   Ubel::PreviewScalarValue("SomeFutureProperty", b, 8).empty());
    }

    // --- A width the property does not have must NOT be guessed at.
    {
        uint8_t b[8] = {};
        EXPECT("DoubleProperty at width 4 is refused",
               Ubel::PreviewScalarValue("DoubleProperty", b, 4).empty());
        EXPECT("null buffer is refused", Ubel::PreviewScalarValue("FloatProperty", nullptr, 4).empty());
    }

    // --- FormatPreviewNumber: readable over the human range, %g only outside it.
    EXPECT("zero",             Ubel::FormatPreviewNumber(0.0) == "0");
    EXPECT("trailing zeros trimmed", Ubel::FormatPreviewNumber(2245.0) == "2245");
    EXPECT("one decimal kept", Ubel::FormatPreviewNumber(129.7) == "129.7");
    EXPECT("no scientific in the human range",
           Ubel::FormatPreviewNumber(18328.64).find('e') == std::string::npos);
    EXPECT("scientific past the human range",
           Ubel::FormatPreviewNumber(1e20).find('e') != std::string::npos);
}

// ================================================================
// Ubel::InterpretStructBytes / LooksLikeVtablePointer — audit U3
//
// The old branch skipped 8 bytes whenever size > 8, on the theory that structs
// start with a vtable. True for FGameplayAttributeData (GAS declares a virtual
// destructor), false for nearly every other USTRUCT — and the cost was a SILENT
// DROP of leading members, which is worse than garbage because it looks right.
//
// The four cases below each fail the OLD code differently, and that is the
// point: a repair aimed at only one of them regresses another. In particular
// "just delete the 8-byte skip" — which the filed finding's parenthetical
// invites — passes the first two and BREAKS the third.
// ================================================================
static void Test_Ubel_InterpretStructBytes() {
    std::printf("Test_Ubel_InterpretStructBytes\n");

    auto putF = [](uint8_t* p, int i, float v) { memcpy(p + i * 4, &v, 4); };

    // --- 1. FVector3f: 12 B, 3 floats. The live-confirmed failure.
    //     Old code: floatStart=8 -> ONE number, the LAST component.
    {
        uint8_t b[12];
        putF(b, 0, 1.0f); putF(b, 1, 2.0f); putF(b, 2, 6203.0f);
        EXPECT("FVector3f keeps all three components",
               Ubel::InterpretStructBytes(b, 12) == "f:[1.0000, 2.0000, 6203.0000]");
    }

    // --- 2. FLinearColor: 16 B, 4 floats. Old code dropped R and G entirely.
    {
        uint8_t b[16];
        putF(b, 0, 0.25f); putF(b, 1, 0.5f); putF(b, 2, 0.75f); putF(b, 3, 1.0f);
        EXPECT("FLinearColor keeps R and G",
               Ubel::InterpretStructBytes(b, 16) == "f:[0.2500, 0.5000, 0.7500, 1.0000]");
    }

    // --- 3. REGRESSION GUARD: FGameplayAttributeData really does carry a vtable.
    //     If the skip is deleted outright this yields the two pointer halves
    //     followed by the values, which is the fix that "looks" right on cases
    //     1 and 2 and quietly ruins every GAS attribute preview.
    {
        uint8_t b[16];
        uint64_t vt = 0x00007FF6A1B2C3D0ULL;   // module-range, 8-aligned
        memcpy(b, &vt, 8);
        putF(b, 2, 100.0f); putF(b, 3, 75.0f);
        EXPECT("GAS attribute still skips its real vtable",
               Ubel::InterpretStructBytes(b, 16) == "f:[100.0000, 75.0000]");
        EXPECT("...and the pointer is recognised as one",
               Ubel::LooksLikeVtablePointer(b, 16));
    }

    // --- 4. The gate must REJECT non-pointers, or case 3 is passing by luck.
    //     Each of these is the first 8 bytes of a real struct that has no vtable.
    {
        uint8_t b[8];
        putF(b, 0, 1.0f); putF(b, 1, 2.0f);
        EXPECT("two floats are not a vtable pointer", !Ubel::LooksLikeVtablePointer(b, 8));

        double d = 1234.5;                       // UE5 LWC FVector's X
        memcpy(b, &d, 8);
        EXPECT("a double is not a vtable pointer", !Ubel::LooksLikeVtablePointer(b, 8));

        uint64_t tiny = 0x1234ULL;   // small integer member ("small" is a Win32 macro: rpcndr.h #define small char)
        memcpy(b, &tiny, 8);
        EXPECT("a small integer is not a vtable pointer", !Ubel::LooksLikeVtablePointer(b, 8));

        uint64_t unaligned = 0x00007FF6A1B2C3D1ULL;
        memcpy(b, &unaligned, 8);
        EXPECT("an unaligned address is not a vtable pointer",
               !Ubel::LooksLikeVtablePointer(b, 8));

        uint64_t kernel = 0xFFFF800000000000ULL;
        memcpy(b, &kernel, 8);
        EXPECT("a kernel-range address is not a vtable pointer",
               !Ubel::LooksLikeVtablePointer(b, 8));
    }

    // --- 5. Honest fallback: nothing decodable must yield "", not a number.
    {
        uint8_t zeros[16] = {};
        EXPECT("all-zero struct yields no hint", Ubel::InterpretStructBytes(zeros, 16).empty());
        EXPECT("too small yields no hint", Ubel::InterpretStructBytes(zeros, 2).empty());
        uint8_t big[128] = {};
        big[0] = 1;
        EXPECT("beyond 16 floats yields no hint", Ubel::InterpretStructBytes(big, 128).empty());
    }

    // --- 6. DOCUMENTED REMAINING GAP (U3 half 2), asserted so it cannot be
    //     mistaken for fixed: a 24-byte LWC FVector is 3 doubles, but the bytes
    //     cannot say that, so this still decodes 6 floats. Only the reflected
    //     layout can settle 3-doubles vs 6-floats.
    {
        uint8_t b[24];
        double xyz[3] = { 1234.5, -678.25, 90.0 };
        memcpy(b, xyz, 24);
        std::string got = Ubel::InterpretStructBytes(b, 24);
        EXPECT("LWC vector no longer eats X (skip is gated)", !got.empty());
        // Six values, not four: the leading double is no longer swallowed. Still
        // wrong values — that is the layout half, not this one.
        EXPECT("LWC still decodes as 6 floats (layout half outstanding)",
               std::count(got.begin(), got.end(), ',') == 5);
    }
}

// ================================================================
// Aura::StructPathGuard — audit A3
//
// ScanForValue's index builder threaded ONE unordered_set through the whole
// per-class struct walk and never erased. That silently turned the cycle guard
// into a global dedupe: only the FIRST field of a given UScriptStruct type in a
// class contributed leaves, and every later one was dropped subtree and all,
// ACROSS UNRELATED BRANCHES. An ordinary actor indexed `Location` but never
// `Velocity` / `Scale3D` / `Extent`; inside one FTransform, `Translation`
// blocked `Scale3D`. Value Search then reported "no match" for a field that was
// right there — and Group Scan / Property-Search-Deep, which scope to the path,
// found it, which is the observable tell.
//
// No target compiles Aura.cpp, so the semantics live in a header-inline RAII
// type and are pinned here. The two cases below are the entire contract, and
// they pull in opposite directions: a *sibling* re-entry MUST be allowed, a
// re-entry *along the active path* MUST NOT.
// ================================================================
static void Test_Aura_StructPathGuard() {
    std::printf("Test_Aura_StructPathGuard\n");

    const uintptr_t kVector    = 0x1000;   // pretend UScriptStruct addresses
    const uintptr_t kTransform = 0x2000;

    std::unordered_set<uintptr_t> path;

    // --- 1. SIBLINGS: the same struct type twice, sequentially. Both must enter.
    //     This is the A3 defect: with a whole-walk set the second one is refused.
    {
        Aura::StructPathGuard g1(path, kVector);
        EXPECT("first FVector enters", g1.Entered());
    }
    EXPECT("path empty after scope exit", path.empty());
    {
        Aura::StructPathGuard g2(path, kVector);
        EXPECT("SIBLING FVector also enters (A3)", g2.Entered());
    }

    // --- 2. NESTED, DIFFERENT TYPES: FTransform { FVector Translation, ... }.
    //     The inner FVector must enter, and on leaving it the *sibling*
    //     FVector Scale3D must still be able to enter while FTransform is held.
    {
        Aura::StructPathGuard t(path, kTransform);
        EXPECT("FTransform enters", t.Entered());
        {
            Aura::StructPathGuard v1(path, kVector);
            EXPECT("Translation enters inside FTransform", v1.Entered());
        }
        {
            Aura::StructPathGuard v2(path, kVector);
            EXPECT("Scale3D enters inside the SAME FTransform (A3)", v2.Entered());
        }
        EXPECT("FTransform still held while siblings come and go",
               path.count(kTransform) == 1);
    }
    EXPECT("path empty after FTransform scope", path.empty());

    // --- 3. TRUE CYCLE: re-entering a struct already ON the path is refused.
    //     Negative control for case 1 — if the guard allowed this, a
    //     self-referential USTRUCT would recurse until the stack died, and
    //     "siblings work" would be passing for the wrong reason.
    {
        Aura::StructPathGuard outer(path, kVector);
        EXPECT("outer FVector enters", outer.Entered());
        {
            Aura::StructPathGuard inner(path, kVector);
            EXPECT("re-entry ALONG THE PATH is refused", !inner.Entered());
        }
        // The refused guard must not have erased the outer one's entry on
        // destruction — that would reopen the cycle one level up.
        EXPECT("refused guard did not release the outer entry",
               path.count(kVector) == 1);
    }
    EXPECT("path empty at the end", path.empty());
}

static void Test_FFieldClassName_Probe() {
    std::printf("\n[DynOff::PickFFieldClassNameOffset]\n");

    // <=5.7: Name at +0x00. Must pick 0x00 — this is the no-regression case, and
    // 0x08 must never even be consulted (on 5.7 it holds EClassFlags).
    {
        int consulted = 0;
        auto r = DynOff::PickFFieldClassNameOffset([&](int off) -> std::string {
            ++consulted;
            return off == 0x00 ? "IntProperty" : "SHOULD-NOT-BE-READ";
        });
        EXPECT("5.7 layout picks +0x00", r == 0x00);
        EXPECT("5.7 layout short-circuits", consulted == 1);
    }

    // 5.8: +0x00 is the low dword of a vtable pointer, which decodes to junk or
    // an empty string; the real name is at +0x08.
    {
        auto r = DynOff::PickFFieldClassNameOffset([](int off) -> std::string {
            return off == 0x08 ? "ObjectProperty" : std::string();
        });
        EXPECT("5.8 layout picks +0x08", r == 0x08);
    }

    // The exact 5.8 observation: FName index taken from vtable bits resolved to a
    // real but wrong name. It must not contain "Property" or the 0x00 arm wins.
    {
        auto r = DynOff::PickFFieldClassNameOffset([](int off) -> std::string {
            return off == 0x08 ? "BoolProperty" : "None";
        });
        EXPECT("junk 'None' at +0x00 rejected", r == 0x08);
    }

    // Neither candidate plausible -> -1, so the caller keeps scanning other
    // struct offsets rather than latching a wrong pair.
    {
        auto r = DynOff::PickFFieldClassNameOffset([](int) { return std::string("Actor"); });
        EXPECT("no candidate -> -1", r == -1);
    }

    // Suffix, not substring, on the 0x08 arm. "PropertyBag" contains "Property"
    // but is not a field-class name; accepting it would let garbage latch +0x08
    // on a <=5.7 build, which is the one way this change could regress.
    {
        auto r = DynOff::PickFFieldClassNameOffset([](int off) -> std::string {
            return off == 0x08 ? "PropertyBag" : std::string();
        });
        EXPECT("0x08 arm requires the SUFFIX", r == -1);
    }

    EXPECT("suffix: IntProperty",   DynOff::LooksLikeFieldClassName("IntProperty"));
    EXPECT("suffix: ObjectProperty", DynOff::LooksLikeFieldClassName("ObjectProperty"));
    EXPECT("suffix: bare 'Property' too short", !DynOff::LooksLikeFieldClassName("Property"));
    EXPECT("suffix: prefix-only rejected", !DynOff::LooksLikeFieldClassName("PropertyBag"));
    EXPECT("suffix: empty rejected",  !DynOff::LooksLikeFieldClassName(""));
    EXPECT("suffix: overlong rejected",
           !DynOff::LooksLikeFieldClassName(std::string(60, 'x') + "Property"));
}

// Voll — pipe-accept capacity logging policy ([PIPEBUSY-2026-08-18]). The accept loop
// runs on one thread; the policy is pure so it can be pinned here (no target compiles
// Fern.cpp). The load-bearing invariant is the state machine: ERROR_PIPE_BUSY logs ONCE
// on entry and ONCE on recovery, everything else ALWAYS logs ERROR, and the latch never
// hides a different-errno failure.
static void Test_Voll_CapacityLoggingPolicy() {
    // A different, unexpected errno ALWAYS logs ERROR — regardless of the latch state.
    {
        bool at = false;
        EXPECT("access-denied -> Error", Voll::OnCreateFailure(ERROR_ACCESS_DENIED, at) == Voll::AcceptLog::Error);
        EXPECT("Error leaves latch clear", at == false);
    }

    // ERROR_PIPE_BUSY: announce once, then stay silent while it holds.
    {
        bool at = false;
        EXPECT("first busy -> EnterCapacity",  Voll::OnCreateFailure(ERROR_PIPE_BUSY, at) == Voll::AcceptLog::EnterCapacity);
        EXPECT("latch now set",                at == true);
        EXPECT("second busy -> None",          Voll::OnCreateFailure(ERROR_PIPE_BUSY, at) == Voll::AcceptLog::None);
        EXPECT("third busy -> None",           Voll::OnCreateFailure(ERROR_PIPE_BUSY, at) == Voll::AcceptLog::None);
        EXPECT("still at capacity",            at == true);

        // Recovery: exactly one line, then quiet.
        EXPECT("success -> RecoverCapacity",   Voll::OnCreateSuccess(at) == Voll::AcceptLog::RecoverCapacity);
        EXPECT("latch cleared on recovery",    at == false);
        EXPECT("next success -> None",         Voll::OnCreateSuccess(at) == Voll::AcceptLog::None);
    }

    // Ordinary success while NOT at capacity is silent (no spurious "slot freed").
    {
        bool at = false;
        EXPECT("plain success -> None", Voll::OnCreateSuccess(at) == Voll::AcceptLog::None);
        EXPECT("latch stays clear",     at == false);
    }

    // ADVERSARIAL: a different-errno failure DURING the at-capacity state still logs ERROR
    // and does NOT clear the latch — so the eventual recovery line still fires exactly once
    // and the genuine error is never suppressed.
    {
        bool at = false;
        EXPECT("busy -> EnterCapacity", Voll::OnCreateFailure(ERROR_PIPE_BUSY, at) == Voll::AcceptLog::EnterCapacity);
        EXPECT("other errno mid-capacity -> Error",
               Voll::OnCreateFailure(ERROR_INVALID_PARAMETER, at) == Voll::AcceptLog::Error);
        EXPECT("latch survives the unrelated error", at == true);
        EXPECT("recovery still fires once", Voll::OnCreateSuccess(at) == Voll::AcceptLog::RecoverCapacity);
    }
}

// audit #5 U9 — a byte-width enum member must read UNSIGNED so the UHT MAX=255 sentinel
// (and any enumerator >= 128) matches the UEnum table instead of sign-extending to a
// negative int. Wider widths keep their natural signedness (matching the array-enum
// sibling). Negative control: reverting ReadEnumRawValue's `case 1` to int8_t fails the
// 0xFF / 0x80 rows.
static void Test_Ubel_ReadEnumRawValue() {
    uint8_t b_ff = 0xFF, b_80 = 0x80, b_7f = 0x7F;
    EXPECT_EQ_U64("byte 0xFF unsigned -> 255", Ubel::ReadEnumRawValue(&b_ff, 1), 255);
    EXPECT_EQ_U64("byte 0x80 unsigned -> 128", Ubel::ReadEnumRawValue(&b_80, 1), 128);
    EXPECT_EQ_U64("byte 0x7F -> 127",          Ubel::ReadEnumRawValue(&b_7f, 1), 127);

    // Wider widths: natural signedness — a negative int16 stays negative.
    uint8_t neg16[2] = { 0xFF, 0xFF };            // int16 -1
    EXPECT("int16 0xFFFF -> -1", Ubel::ReadEnumRawValue(neg16, 2) == -1);
    uint8_t v16[2] = { 0x00, 0x01 };              // int16 256
    EXPECT_EQ_U64("int16 0x0100 -> 256", Ubel::ReadEnumRawValue(v16, 2), 256);
    uint8_t v32[4] = { 0x00, 0x00, 0x00, 0x01 };  // int32 0x01000000
    EXPECT_EQ_U64("int32 -> 0x01000000", Ubel::ReadEnumRawValue(v32, 4), 0x01000000);
    uint8_t v64[8] = { 0,0,0,0,0,0,0,1 };         // int64 0x0100000000000000
    EXPECT_EQ_U64("int64 -> high byte", Ubel::ReadEnumRawValue(v64, 8), 0x0100000000000000ULL);

    // Guards: null and unusual size are 0, never a crash.
    EXPECT_EQ_U64("null -> 0",   Ubel::ReadEnumRawValue(nullptr, 1), 0);
    EXPECT_EQ_U64("size 3 -> 0", Ubel::ReadEnumRawValue(&b_ff, 3), 0);
}

// audit #5 U10 — the FString/FUtf8String count cap bounds a GARBAGE Count, not display
// length: a realistic long string (a 400-char description that used to render as
// "(empty)") is accepted, while empty/negative and garbage-huge counts are rejected.
// Negative control: dropping kMaxFStringChars back to 256 fails the 400 / cap rows.
static void Test_Ubel_IsPlausibleStringCount() {
    EXPECT("1 accepted",             Ubel::IsPlausibleStringCount(1));
    EXPECT("256 accepted",           Ubel::IsPlausibleStringCount(256));
    EXPECT("400 accepted (was empty)", Ubel::IsPlausibleStringCount(400));
    EXPECT("cap accepted",           Ubel::IsPlausibleStringCount(Ubel::kMaxFStringChars));
    EXPECT("0 rejected",             !Ubel::IsPlausibleStringCount(0));
    EXPECT("negative rejected",      !Ubel::IsPlausibleStringCount(-1));
    EXPECT("garbage count rejected", !Ubel::IsPlausibleStringCount(0x7FFFFFFF));
    EXPECT("just over cap rejected", !Ubel::IsPlausibleStringCount(Ubel::kMaxFStringChars + 1));
}

// audit #5 G4 — the FNamePool block-offset-bits probe at testIdx=1 CANNOT distinguish 14
// from 16 (both address chunk 0, offset 1*stride), which is why the old detector's
// 14-bit arm was structurally unreachable while it logged the outcome as a measurement.
// A block-boundary index DOES differ, but is unreliable for other reasons (see
// DetectBlockOffsetBits) — so the honest fix keeps the stock width and this pins the
// impossibility. Negative control: if ComputeBlockProbe stopped masking, the idx-1
// indistinguishable assertion flips.
static void Test_Serie_BlockBitsProbe() {
    const int stride = 2;
    EXPECT("idx1 16 vs 14 indistinguishable",
           Serie::BlockBitsAreIndistinguishable(1, 16, 14, stride));
    Serie::BlockProbe p16 = Serie::ComputeBlockProbe(1, 16, stride);
    Serie::BlockProbe p14 = Serie::ComputeBlockProbe(1, 14, stride);
    EXPECT("idx1 chunk 0 both",          p16.chunkIndex == 0 && p14.chunkIndex == 0);
    EXPECT("idx1 offset 1*stride both",  p16.chunkOffset == stride && p14.chunkOffset == stride);

    // At the 14-bit block boundary the widths DO diverge: 16 keeps it in chunk 0 at a
    // large offset, 14 rolls it into chunk 1 offset 0.
    const int32_t boundary = 1 << 14;  // 0x4000
    EXPECT("idx 0x4000 16 vs 14 distinguishes",
           !Serie::BlockBitsAreIndistinguishable(boundary, 16, 14, stride));
    Serie::BlockProbe b16 = Serie::ComputeBlockProbe(boundary, 16, stride);
    Serie::BlockProbe b14 = Serie::ComputeBlockProbe(boundary, 14, stride);
    EXPECT("idx 0x4000 @16 chunk 0",     b16.chunkIndex == 0);
    EXPECT_EQ_U64("idx 0x4000 @16 offset", b16.chunkOffset, (int64_t)boundary * stride);
    EXPECT("idx 0x4000 @14 chunk 1",     b14.chunkIndex == 1);
    EXPECT_EQ_U64("idx 0x4000 @14 offset 0", b14.chunkOffset, 0);
}

// audit #5 G5 — the UE4 TNameEntryArray index guard must reject a NEGATIVE nameIndex
// (a poison 0xFFFFFFFF read into an int32 as -1), which under truncating division gives
// chunkIndex 0 / elemIndex -1 and derefs chunk + (-1)*8. Negative control: a guard that
// only bounds chunkIndex (the old code) accepts -1 (chunkIndex 0).
static void Test_Serie_UE4NameIndexInBounds() {
    const int32_t chunkSize = 0x4000;   // UE4_CHUNK_SIZE
    const int32_t maxChunks = 256;      // UE4_NAME_MAX_CHUNKS
    EXPECT("index 0 (None) ok", Serie::UE4NameIndexInBounds(0, chunkSize, maxChunks));
    EXPECT("index 1 ok",        Serie::UE4NameIndexInBounds(1, chunkSize, maxChunks));
    EXPECT("index 16383 ok",    Serie::UE4NameIndexInBounds(16383, chunkSize, maxChunks));
    EXPECT("last chunk ok",     Serie::UE4NameIndexInBounds(maxChunks * chunkSize, chunkSize, maxChunks));
    // The defect: a negative index MUST be rejected (the old chunkIndex-only guard did not).
    EXPECT("index -1 REJECTED",     !Serie::UE4NameIndexInBounds(-1, chunkSize, maxChunks));
    EXPECT("large negative rejected", !Serie::UE4NameIndexInBounds(-100000, chunkSize, maxChunks));
    EXPECT("past max chunks rejected",
           !Serie::UE4NameIndexInBounds((maxChunks + 1) * chunkSize, chunkSize, maxChunks));
}

// Print the test about to run, when DLL_TEST_TRACE is set. build.ps1 sets it under
// CI. Off locally so the ordinary run stays two lines.
static bool g_trace = false;
#define RUN(fn) do { if (g_trace) std::printf("[run] %s\n", #fn); fn(); } while (0)

int main() {
    // UNBUFFERED, and this is not a style choice. When this exe died on CI with
    // 0xC0000409 (STATUS_STACK_BUFFER_OVERRUN) the log contained NOT ONE LINE of its
    // output -- not even the banner below -- because stdout to a pipe is fully
    // buffered and the buffer dies with the process. A harness whose output vanishes
    // exactly when something goes wrong tells you nothing at the only moment it
    // matters, and it passed locally, so there was nothing else to go on.
    std::setvbuf(stdout, nullptr, _IONBF, 0);
    g_trace = std::getenv("DLL_TEST_TRACE") != nullptr;

    std::printf("dll_helpers_test (Renge + Scharf + Radar)\n");
    std::printf("------------------------------------------\n");

    RUN(Test_TryStrToAddr_AcceptsValidHex);
    RUN(Test_TryStrToAddr_RejectsCePlaceholder);
    RUN(Test_TryStrToAddr_RejectsTrailingGarbage);
    RUN(Test_TryStrToAddr_RejectsEmpty);
    RUN(Test_TryStrToAddr_RejectsNonHex);
    RUN(Test_StrToAddr_NoexceptZeroOnFailure);

    RUN(Test_Alignment_PointerProperties_Need8);
    RUN(Test_Alignment_EnumProperty_RespectsElemSize);
    RUN(Test_Alignment_NameProperty_RespectsCpnMode);
    RUN(Test_Alignment_ScalarPrimitives);
    RUN(Test_Alignment_OffsetZeroNeverSuspicious);
    RUN(Test_Alignment_UnknownTypesNotValidated);
    RUN(Test_Alignment_WeakAndSparseDelegate);

    RUN(Test_Tot_PerCommandStillOwed);
    RUN(Test_Aura_DeepLeafCoverage);
    RUN(Test_Ubel_ClassCacheBound);
    RUN(Test_Ubel_EstimateClassInfoBytes);
    RUN(Test_Stark_ShouldUseTrampoline);
    RUN(Test_Stark_ShouldDrainQueue);
    RUN(Test_Stark_PeOffsetSentinels);
    RUN(Test_Stark_ShouldRetryPeDetection);
    RUN(Test_Stark_PeValidationFailureVerdict);
    RUN(Test_Lineal_SerialOffsetForLayout);
    RUN(Test_Mimic_MailboxLayout);
    RUN(Test_Mimic_ListInstancesGeometry);
    RUN(Test_Mimic_CommandNumbering);
    RUN(Test_Mimic_InvokeRouting);
    RUN(Test_Mimic_CommandRequiresInit);
    RUN(Test_Flamme_AtomicPublishGate);

    RUN(Test_ValueScan_DataTypeSizes);
    RUN(Test_ValueScan_ParseDataTypeRoundTrip);
    RUN(Test_ValueScan_ScanTypePartitioning);
    RUN(Test_ValueScan_Predicate_Int32);
    RUN(Test_ValueScan_Predicate_Int8Negative);
    RUN(Test_ValueScan_Predicate_Float);
    RUN(Test_ValueScan_Predicate_Double);
    RUN(Test_ValueScan_Predicate_Bool);
    RUN(Test_ValueScan_Predicate_UInt64_RangeBoundary);
    RUN(Test_ValueScan_FloatRoundMode_Exact);
    RUN(Test_ValueScan_FloatRoundMode_Ordered);
    RUN(Test_ValueScan_FloatRoundMode_PrevValue);
    RUN(Test_ValueScan_FloatRoundMode_Between);
    RUN(Test_ValueScan_RoundMode_IntegerNoOp);

    // Phase 2A — string predicates + family predicates
    RUN(Test_ValueScan_TypeFamilyPredicates);
    RUN(Test_ValueScan_IsScanTypeValidFor);
    RUN(Test_ValueScan_StringPredicate_Exact);
    RUN(Test_ValueScan_StringPredicate_Substring);
    RUN(Test_ValueScan_StringPredicate_PrevValue);
    RUN(Test_ValueScan_StringPredicate_RejectsNumericOrdering);
    // Phase 2B — vector predicates
    RUN(Test_ValueScan_VectorPredicate_Exact);
    RUN(Test_ValueScan_VectorPredicate_Ordering);
    RUN(Test_ValueScan_VectorPredicate_Between);
    RUN(Test_ValueScan_VectorPredicate_PrevValue);
    RUN(Test_ValueScan_VectorPredicate_RejectsSubstring);
    RUN(Test_ValueScan_VectorWidth_Accepted);
    RUN(Test_ValueScan_DecodeVectorBytes);
    RUN(Test_ValueScan_StoreVectorCanonical);
    RUN(Test_ValueScan_LwcVectorIsNotReadAsFloats);
    RUN(Test_ValueScan_VectorStructNames);
    // build 794 — multi-numeric (NumericNoByte) meta type
    RUN(Test_ValueScan_MultiNumericMembers);
    RUN(Test_ValueScan_DataTypeFromPropertyTypeName);
    RUN(Test_ValueScan_PropertyTypeNameOf_Inverse);
    RUN(Test_Macht_PatternScanRange);
    RUN(Test_ValueScan_BuildNumericTargets);
    // Phase A1a — snapshot field selection
    RUN(Test_ValueScan_SelectSnapshotNumericFields);
    // Phase A1b — struct-array inner-key selection
    RUN(Test_ValueScan_SelectArrayInnerKey);

    RUN(Test_ValueScan_SessionLifecycle);
    RUN(Test_ValueScan_FieldDisplayName);
    RUN(Test_ValueScan_OptionalFlagOffset);
    RUN(Test_ValueScan_OrderedView);
    RUN(Test_IsEnginePackage);
    RUN(Test_CanonicalizeObjectPath);
    RUN(Test_IsReflectionMetaClass);
    RUN(Test_KeywordMatch);
    RUN(Test_SnapshotNoise_GuardrailAndSets);
    RUN(Test_NumericFamily_Filter);
    RUN(Test_GroupScan_ExcludeAndHistogram);
    RUN(Test_Radar_PickGroupWitnessAssignment);
    RUN(Test_Radar_FormatCandidateOrigin);
    RUN(Test_Radar_GroupLeafBudget);
    RUN(Test_Radar_GroupSessionCarriesPerSlotCap);
    RUN(Test_Radar_GroupSortUsesTheDisplayedLeaf);
    RUN(Test_ValueScan_OrderedViewScale);
    RUN(Test_Macht_IsRipRelativeModRM);
    RUN(Test_ValueScan_SparseContainerGeometry);

    // Path 2 — native x64 disassembly (Denken decoder core)
    RUN(Test_Denken_BasicAccesses);
    RUN(Test_Denken_ExcludesStackAndZeroDisp);
    RUN(Test_Denken_FollowsCallHandoff);
    RUN(Test_Denken_DoesNotFollowNonThisCall);
    RUN(Test_Denken_TerminatesAndGuards);

    // UE5.7+ packed FUObjectItem reconstruction (math-only; no live game exists)
    RUN(Test_Packed_RoundTrip_Basic);
    RUN(Test_Packed_RoundTrip_HighBits);
    RUN(Test_Packed_ZeroAndNull);
    RUN(Test_Packed_FlagsDoNotLeak);
    RUN(Test_Packed_AlignBitsKnob);
    RUN(Test_Packed_PtrMaskKnob);

    // GraphPath BFS core — "Locate in GWorld" shortest-path search (mock graph)
    RUN(Test_GraphPath_DirectChild);
    RUN(Test_GraphPath_RootEqualsTarget);
    RUN(Test_GraphPath_ShortestAmongTwo);
    RUN(Test_GraphPath_Cycle);
    RUN(Test_GraphPath_DepthBound);
    RUN(Test_GraphPath_Unreachable);
    RUN(Test_GraphPath_Abort);
    RUN(Test_GraphPath_VisitedCap);
    RUN(Test_GraphPath_ContainerEdgePreserved);
    RUN(Test_GraphPath_MapSetElementGeometryRoundTrip);
    RUN(Test_GraphPath_Reconstruction);

    // Solitar GodMode — FBoolProperty single-bit read-modify-write
    RUN(Test_Solitar_ApplyBoolBit);
    RUN(Test_Solitar_MatchProtectionBool);
    RUN(Test_Solide_MatchStealthField);
    RUN(Test_Solide_IntWidthAndRange);

    // Neu — UEnum::Names layout: legacy TArray vs UE5.6+ FNameData (synthetic memory)
    RUN(Test_Neu_Legacy_Basic);
    RUN(Test_Neu_Legacy_CasePreserving);
    RUN(Test_Neu_FNameData_Basic);
    RUN(Test_Neu_FNameData_CasePreserving);
    RUN(Test_Neu_FNameData_SparseValues);
    RUN(Test_Neu_TagBitMasked);
    RUN(Test_Neu_Disambiguation);
    RUN(Test_Neu_Edge);

    // Orden — multi-value group scan SDR matcher (synthetic leaves, no game)
    RUN(Test_Orden_PerSlotCap);
    RUN(Test_Orden_DistinctValues);
    RUN(Test_Orden_MissingValueRejected);
    RUN(Test_Orden_DuplicateValuesSDR);
    RUN(Test_Orden_MultiWidthMatch);
    RUN(Test_Orden_ConvergenceAndAssignment);
    RUN(Test_Orden_OrderedFirstScan);
    RUN(Test_Orden_BetweenFirstScan);
    RUN(Test_Orden_RoundedFloatExact);
    RUN(Test_Orden_PrevValueRejectedOnFirstScan);

    // Ubel — Native-C scan P0: hole computation + Guess-type normalization (pure)
    RUN(Test_Holes_ComputeHoles_Basic);
    RUN(Test_Holes_LeadingGapSurvives);
    RUN(Test_Holes_FullyCovered);
    RUN(Test_Holes_ClampsOutOfWindow);
    RUN(Test_Holes_ComputeClassHoles_ArrayDim);
    RUN(Test_IsSanePropertiesSize);
    RUN(Test_ShouldPublishClassWalk);
    RUN(Test_ShouldPublishEnumTable);
    RUN(Test_VersionNeedleScan_Equivalence);
    RUN(Test_VersionNeedleScan_GateStillGates);
    RUN(Test_VersionTierRules_G8_G9);
    RUN(Test_VersionTier2_BareNeedle_G11);
    RUN(Test_PropertyFamilyIsCoherent);
    RUN(Test_NameWitness);
    RUN(Test_Holes_NormalizeGuessedType);

    // Macht — AOB pattern parser: nibble wildcards (4? / ?5) + anchor selection
    RUN(Test_Sig_IsCeReplayableAob);
    RUN(Test_Genau_AdmitMultiModuleCandidate);
    RUN(Test_Radar_RefineContainerAnchor);
    RUN(Test_Radar_LeafAnchor);
    RUN(Test_Macht_ParsePattern_Nibble);

    // Tot — per-command cancel immunity is independent of "is a background worker"
    RUN(Test_Tot_CancelImmunityVsBackgroundWorker);

    // Routine — SafeThread: ~std::thread on a joinable thread terminates the process
    RUN(Test_Routine_SafeThread);

    // Grimoire — Cheat Engine host detection (prefix, not an exact-name list)
    RUN(Test_Grimoire_IsCheatEngineExeName);
    RUN(Test_Grimoire_HostAllowsBackgroundThreads);

    // Renge — hex parsing has a failure channel (write_mem can refuse a bad pattern)
    RUN(Test_Renge_TryHexToBytes);
    RUN(Test_Renge_ApplyPayloadKeepsEnvelope);   // F5 — envelope survives its payload
    RUN(Test_Renge_EnvelopeBuilders);            // AD24 — MakeResponse / MakeError / MakeEvent

    // DynOff — FFieldClass::Name probe (UE 5.8 virtual-dtor member shift)
    RUN(Test_FFieldClassName_Probe);

    // Ubel — reflected struct preview: member width comes from the property (U17)
    RUN(Test_Ubel_PreviewScalarValue);

    // Ubel — byte-blind struct preview: gate the vtable skip on evidence (U3)
    RUN(Test_Ubel_InterpretStructBytes);

    // Aura — the struct-walk cycle guard is scoped to the PATH, not the whole walk
    RUN(Test_Aura_StructPathGuard);

    // Voll — pipe-accept capacity logging: ERROR_PIPE_BUSY once, not 1/s ([PIPEBUSY])
    RUN(Test_Voll_CapacityLoggingPolicy);

    // audit #5 L1 (D1/D2 DLL engine decode + safety)
    RUN(Test_Ubel_ReadEnumRawValue);          // U9  — byte enums read unsigned
    RUN(Test_Ubel_IsPlausibleStringCount);    // U10 — FString cap bounds a garbage Count
    RUN(Test_Serie_BlockBitsProbe);           // G4  — idx-1 probe cannot distinguish 14 vs 16
    RUN(Test_Serie_UE4NameIndexInBounds);     // G5  — reject negative UE4 name index

    std::printf("------------------------------------------\n");
    std::printf("Pass: %d   Fail: %d\n", g_pass, g_fail);
    return g_fail;
}
