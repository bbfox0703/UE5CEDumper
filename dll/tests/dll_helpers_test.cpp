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
#include "../src/Solitar.h"     // GodMode FBoolProperty bit write (ApplyBoolBit, header-inline)
#include "../src/Grimoire.h"    // DynOff FFieldClass::Name probe (UE5.8 virtual-dtor shift)
#include "../src/Solide.h"      // Force-field / stealth-meter matcher (MatchStealthField, header-inline)
#include "../src/Orden.h"       // Multi-value group scan: source-agnostic SDR matcher (MatchGroup)
#include "../src/Ubel.h"        // Native-C scan P0: ComputeHoles / ComputeClassHoles / NormalizeGuessedTypeToProperty (inline, pure)
#include "../src/Aura.h"        // IsEnginePackage (header-inline, pure) — engine/game package gate
#include "../src/Tot.h"         // Cancellation flags: cancel-immunity vs background-worker (B4)
#include "../src/Routine.h"    // SafeThread — detaching-on-destroy thread wrapper

#include <Windows.h>
#include <timeapi.h>   // timeBeginPeriod — exercised by the poll-latency check

#include <thread>

#include <algorithm>
#include <cstdio>
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

// ----- Mimic poll-latency micro-benchmark ------------------------------------
//
// Mimic.cpp's polling thread does `Sleep(kPollIntervalMs)` (=1) every iteration
// and bumps timer resolution via timeBeginPeriod(1) so Sleep(1) actually
// delivers ~1ms latency. This test reproduces the same setup in the test
// process and asserts that 100 × Sleep(1) takes < 200ms wall-clock.
//
// Without timeBeginPeriod, Sleep(1) on a system with the default 15.6ms tick
// rounds up to 15.6ms per call → 100 calls = ~1560ms, which would fail this
// assertion. So a green test confirms the Mimic-side latency reduction is
// actually achievable on this host's OS configuration.
//
// 300ms threshold (vs. ideal ~100ms): generous to account for CI scheduler
// jitter, thread contention, and the kernel's discretion on tick rounding.
// Idle baseline on a quiet machine landed at 193ms (≈1.94ms/sleep) — Windows
// commonly rounds Sleep(1) up to the next 1-2ms tick boundary, so anything
// under ~250ms confirms timeBeginPeriod is in effect. The 5× headroom keeps
// the test from flaking under heavy load while still catching the legacy-
// tick regression cleanly (which would land near 1560ms).

static void Test_Mimic_PollLatency_OneMillisecond() {
    // Mirror the DLL polling thread's timer-resolution request — INCLUDING how it
    // reaches winmm. Mimic no longer statically imports winmm: it resolves
    // timeBeginPeriod/timeEndPeriod from the System32 copy by explicit path, so
    // that a future winmm.dll PROXY build cannot have the call resolve back into
    // its own forwarding stub (which returns 0 == TIMERR_NOERROR before it has
    // resolved the real export, silently no-opping the 1ms tick). Resolving the
    // same way here means this test covers the actual mechanism, and lets the test
    // exe drop its winmm import too — nothing in the tree statically imports it.
    using TimePeriodFn = MMRESULT (WINAPI*)(UINT);
    wchar_t winmmPath[MAX_PATH] = {};
    UINT n = GetSystemDirectoryW(winmmPath, MAX_PATH);
    EXPECT("GetSystemDirectoryW ok", n != 0 && n < MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return;
    wcsncat_s(winmmPath, L"\\winmm.dll", _TRUNCATE);

    HMODULE hWinmm = LoadLibraryW(winmmPath);
    EXPECT("LoadLibraryW(System32 winmm.dll) ok", hWinmm != nullptr);
    if (!hWinmm) return;

    auto pBegin = reinterpret_cast<TimePeriodFn>(GetProcAddress(hWinmm, "timeBeginPeriod"));
    auto pEnd   = reinterpret_cast<TimePeriodFn>(GetProcAddress(hWinmm, "timeEndPeriod"));
    EXPECT("timeBeginPeriod resolved", pBegin != nullptr);
    EXPECT("timeEndPeriod resolved", pEnd != nullptr);
    if (!pBegin || !pEnd) return;

    MMRESULT rc = pBegin(1);
    EXPECT("timeBeginPeriod(1) ok", rc == TIMERR_NOERROR);
    if (rc != TIMERR_NOERROR) {
        std::printf("  [warn] timeBeginPeriod failed rc=%u — skipping latency assert\n", rc);
        return;
    }

    LARGE_INTEGER freq{}, start{}, end{};
    QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&start);

    constexpr int kIters = 100;
    for (int i = 0; i < kIters; ++i) {
        Sleep(1);
    }

    QueryPerformanceCounter(&end);
    pEnd(1);

    double elapsedMs = double(end.QuadPart - start.QuadPart) * 1000.0 / double(freq.QuadPart);
    std::printf("  [info] 100 x Sleep(1) under timeBeginPeriod(1) = %.1f ms "
                "(avg %.2f ms/sleep)\n",
                elapsedMs, elapsedMs / kIters);

    // Hard ceiling: if a sleep really cost the legacy 15.6ms tick, this would
    // be ~1560ms. 300ms catches that regression cleanly while tolerating noise.
    if (elapsedMs >= 300.0) {
        ++g_fail;
        std::printf("  FAIL: poll-latency over threshold\n"
                    "    actual=%.1f ms expected<200 ms\n"
                    "    at %s:%d\n", elapsedMs, __FILE__, __LINE__);
    } else {
        ++g_pass;
    }
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
    // Phase 2B: vector types — three floats = 12 bytes.
    EXPECT("SizeOf FVector = 12",    Radar::SizeOf(Radar::DataType::FVector)    == 12);
    EXPECT("SizeOf FRotator = 12",   Radar::SizeOf(Radar::DataType::FRotator)   == 12);
    EXPECT("SizeOf FTransform = 12", Radar::SizeOf(Radar::DataType::FTransform) == 12);
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
    const auto& noByteNames = Radar::PropertyTypeNames(DT::NumericNoByte);
    EXPECT("NumericNoByte has 8 property names", noByteNames.size() == 8);
    EXPECT("every NumericNoByte property name resolves", allResolve(noByteNames));
    const auto& allNames = Radar::PropertyTypeNames(DT::NumericAll);
    EXPECT("NumericAll has 10 property names", allNames.size() == 10);
    EXPECT("every NumericAll property name resolves", allResolve(allNames));
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

static void WriteVector(uint8_t buf[12], float x, float y, float z) {
    std::memcpy(buf + 0, &x, 4);
    std::memcpy(buf + 4, &y, 4);
    std::memcpy(buf + 8, &z, 4);
}

static void Test_ValueScan_VectorPredicate_Exact() {
    using ST = Radar::ScanType;
    using RM = Radar::RoundMode;
    uint8_t cur[12], tgt[12];
    WriteVector(cur, 100.0f, 200.0f, 300.0f);
    WriteVector(tgt, 100.0f, 200.0f, 300.0f);
    EXPECT("Vec Exact all match", Radar::CompareVectorPredicate(ST::Exact, cur, tgt));
    // Per-axis displayed-integer reduce: X=100.6 rounds to 101, so a whole target
    // axis of 100 no longer matches that axis.
    WriteVector(cur, 100.6f, 200.0f, 300.0f);
    EXPECT("Vec Exact rejects axis that rounds away (100.6 -> 101 vs 100)",
           !Radar::CompareVectorPredicate(ST::Exact, cur, tgt, nullptr, RM::Round));
    // X=100.4 rounds back to 100 -> all axes match the displayed integer.
    WriteVector(cur, 100.4f, 200.0f, 300.0f);
    EXPECT("Vec Exact Round accepts axis that rounds back (100.4 -> 100)",
           Radar::CompareVectorPredicate(ST::Exact, cur, tgt, nullptr, RM::Round));
}

static void Test_ValueScan_VectorPredicate_Ordering() {
    using ST = Radar::ScanType;
    uint8_t cur[12], tgt[12];
    WriteVector(cur, 10.0f, 20.0f, 30.0f);
    WriteVector(tgt, 5.0f,  10.0f, 15.0f);
    EXPECT("Vec Bigger: all axes above", Radar::CompareVectorPredicate(ST::Bigger, cur, tgt));
    EXPECT("Vec Smaller (10,20,30) NOT < (5,10,15)",
           !Radar::CompareVectorPredicate(ST::Smaller, cur, tgt));

    // One axis equal kills Bigger
    WriteVector(cur, 10.0f, 10.0f, 30.0f);
    EXPECT("Vec Bigger fails when one axis equals",
           !Radar::CompareVectorPredicate(ST::Bigger, cur, tgt));
}

static void Test_ValueScan_VectorPredicate_Between() {
    using ST = Radar::ScanType;
    uint8_t cur[12], lo[12], hi[12];
    WriteVector(lo, 0.0f,   0.0f,   0.0f);
    WriteVector(hi, 100.0f, 100.0f, 100.0f);
    WriteVector(cur, 50.0f, 50.0f, 50.0f);
    EXPECT("Vec Between: (50,50,50) in [(0,0,0),(100,100,100)]",
           Radar::CompareVectorPredicate(ST::Between, cur, lo, hi));
    WriteVector(cur, 50.0f, 150.0f, 50.0f);
    EXPECT("Vec Between rejects Y outside",
           !Radar::CompareVectorPredicate(ST::Between, cur, lo, hi));
}

static void Test_ValueScan_VectorPredicate_PrevValue() {
    using ST = Radar::ScanType;
    uint8_t cur[12], prev[12];
    WriteVector(prev, 100.0f, 100.0f, 100.0f);

    // Movement on any single axis = Changed
    WriteVector(cur, 100.0f, 100.0f, 105.0f);
    EXPECT("Vec Changed: one axis moved",
           Radar::CompareVectorPredicate(ST::Changed, cur, prev));
    EXPECT("Vec Unchanged rejects when axis differs",
           !Radar::CompareVectorPredicate(ST::Unchanged, cur, prev));

    // No movement
    WriteVector(cur, 100.0f, 100.0f, 100.0f);
    EXPECT("Vec Unchanged accepts identical",
           Radar::CompareVectorPredicate(ST::Unchanged, cur, prev));
    EXPECT("Vec Changed rejects identical",
           !Radar::CompareVectorPredicate(ST::Changed, cur, prev));

    // Increased: ANY axis moved up beyond tolerance
    WriteVector(cur, 100.0f, 100.0f, 110.0f);
    EXPECT("Vec Increased: Z went up",
           Radar::CompareVectorPredicate(ST::Increased, cur, prev));
    // All went down — Increased rejects
    WriteVector(cur, 90.0f, 90.0f, 90.0f);
    EXPECT("Vec Increased rejects when all axes down",
           !Radar::CompareVectorPredicate(ST::Increased, cur, prev));
    EXPECT("Vec Decreased: all axes down",
           Radar::CompareVectorPredicate(ST::Decreased, cur, prev));
}

static void Test_ValueScan_VectorPredicate_RejectsSubstring() {
    using ST = Radar::ScanType;
    uint8_t cur[12], tgt[12];
    WriteVector(cur, 0,0,0); WriteVector(tgt, 0,0,0);
    EXPECT("Vec Contains rejects",
           !Radar::CompareVectorPredicate(ST::Contains, cur, tgt));
    EXPECT("Vec StartsWith rejects",
           !Radar::CompareVectorPredicate(ST::StartsWith, cur, tgt));
    EXPECT("Vec EndsWith rejects",
           !Radar::CompareVectorPredicate(ST::EndsWith, cur, tgt));
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

    std::atomic<int> ran{0};

    // 1. Destructed while STILL RUNNING. This is the game-close case: the worker is
    //    mid-tick, nothing joined it, and the static destructor runs.
    {
        Routine::SafeThread t;
        t = std::thread([&] {
            for (int i = 0; i < 50 && ran.load() < 1000; ++i) {
                ran.fetch_add(1);
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

    EXPECT("the running thread actually started", ran.load() > 0);
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

    // Only the RIP forms give CE a (pattern, pos, len) triple it can replay with
    // AOBScanModuleUE + a fixed offset into the match.
    EXPECT("RipDirect replayable",        IsCeReplayableAob(AobResolve::RipDirect));
    EXPECT("RipDeref replayable",         IsCeReplayableAob(AobResolve::RipDeref));
    EXPECT("RipBoth replayable",          IsCeReplayableAob(AobResolve::RipBoth));

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

int main() {
    std::printf("dll_helpers_test (Renge + Scharf + Radar)\n");
    std::printf("------------------------------------------\n");

    Test_TryStrToAddr_AcceptsValidHex();
    Test_TryStrToAddr_RejectsCePlaceholder();
    Test_TryStrToAddr_RejectsTrailingGarbage();
    Test_TryStrToAddr_RejectsEmpty();
    Test_TryStrToAddr_RejectsNonHex();
    Test_StrToAddr_NoexceptZeroOnFailure();

    Test_Alignment_PointerProperties_Need8();
    Test_Alignment_EnumProperty_RespectsElemSize();
    Test_Alignment_NameProperty_RespectsCpnMode();
    Test_Alignment_ScalarPrimitives();
    Test_Alignment_OffsetZeroNeverSuspicious();
    Test_Alignment_UnknownTypesNotValidated();
    Test_Alignment_WeakAndSparseDelegate();

    Test_Mimic_PollLatency_OneMillisecond();

    Test_ValueScan_DataTypeSizes();
    Test_ValueScan_ParseDataTypeRoundTrip();
    Test_ValueScan_ScanTypePartitioning();
    Test_ValueScan_Predicate_Int32();
    Test_ValueScan_Predicate_Int8Negative();
    Test_ValueScan_Predicate_Float();
    Test_ValueScan_Predicate_Double();
    Test_ValueScan_Predicate_Bool();
    Test_ValueScan_Predicate_UInt64_RangeBoundary();
    Test_ValueScan_FloatRoundMode_Exact();
    Test_ValueScan_FloatRoundMode_Ordered();
    Test_ValueScan_FloatRoundMode_PrevValue();
    Test_ValueScan_FloatRoundMode_Between();
    Test_ValueScan_RoundMode_IntegerNoOp();

    // Phase 2A — string predicates + family predicates
    Test_ValueScan_TypeFamilyPredicates();
    Test_ValueScan_IsScanTypeValidFor();
    Test_ValueScan_StringPredicate_Exact();
    Test_ValueScan_StringPredicate_Substring();
    Test_ValueScan_StringPredicate_PrevValue();
    Test_ValueScan_StringPredicate_RejectsNumericOrdering();
    // Phase 2B — vector predicates
    Test_ValueScan_VectorPredicate_Exact();
    Test_ValueScan_VectorPredicate_Ordering();
    Test_ValueScan_VectorPredicate_Between();
    Test_ValueScan_VectorPredicate_PrevValue();
    Test_ValueScan_VectorPredicate_RejectsSubstring();
    Test_ValueScan_VectorStructNames();
    // build 794 — multi-numeric (NumericNoByte) meta type
    Test_ValueScan_MultiNumericMembers();
    Test_ValueScan_DataTypeFromPropertyTypeName();
    Test_ValueScan_PropertyTypeNameOf_Inverse();
    Test_ValueScan_BuildNumericTargets();
    // Phase A1a — snapshot field selection
    Test_ValueScan_SelectSnapshotNumericFields();
    // Phase A1b — struct-array inner-key selection
    Test_ValueScan_SelectArrayInnerKey();

    Test_ValueScan_SessionLifecycle();
    Test_ValueScan_FieldDisplayName();
    Test_ValueScan_OptionalFlagOffset();
    Test_ValueScan_OrderedView();
    Test_IsEnginePackage();
    Test_IsReflectionMetaClass();
    Test_KeywordMatch();
    Test_SnapshotNoise_GuardrailAndSets();
    Test_NumericFamily_Filter();
    Test_GroupScan_ExcludeAndHistogram();
    Test_Radar_PickGroupWitnessAssignment();
    Test_ValueScan_OrderedViewScale();
    Test_Macht_IsRipRelativeModRM();
    Test_ValueScan_SparseContainerGeometry();

    // Path 2 — native x64 disassembly (Denken decoder core)
    Test_Denken_BasicAccesses();
    Test_Denken_ExcludesStackAndZeroDisp();
    Test_Denken_FollowsCallHandoff();
    Test_Denken_DoesNotFollowNonThisCall();
    Test_Denken_TerminatesAndGuards();

    // UE5.7+ packed FUObjectItem reconstruction (math-only; no live game exists)
    Test_Packed_RoundTrip_Basic();
    Test_Packed_RoundTrip_HighBits();
    Test_Packed_ZeroAndNull();
    Test_Packed_FlagsDoNotLeak();
    Test_Packed_AlignBitsKnob();
    Test_Packed_PtrMaskKnob();

    // GraphPath BFS core — "Locate in GWorld" shortest-path search (mock graph)
    Test_GraphPath_DirectChild();
    Test_GraphPath_RootEqualsTarget();
    Test_GraphPath_ShortestAmongTwo();
    Test_GraphPath_Cycle();
    Test_GraphPath_DepthBound();
    Test_GraphPath_Unreachable();
    Test_GraphPath_Abort();
    Test_GraphPath_VisitedCap();
    Test_GraphPath_ContainerEdgePreserved();
    Test_GraphPath_MapSetElementGeometryRoundTrip();
    Test_GraphPath_Reconstruction();

    // Solitar GodMode — FBoolProperty single-bit read-modify-write
    Test_Solitar_ApplyBoolBit();
    Test_Solitar_MatchProtectionBool();
    Test_Solide_MatchStealthField();

    // Neu — UEnum::Names layout: legacy TArray vs UE5.6+ FNameData (synthetic memory)
    Test_Neu_Legacy_Basic();
    Test_Neu_Legacy_CasePreserving();
    Test_Neu_FNameData_Basic();
    Test_Neu_FNameData_CasePreserving();
    Test_Neu_FNameData_SparseValues();
    Test_Neu_TagBitMasked();
    Test_Neu_Disambiguation();
    Test_Neu_Edge();

    // Orden — multi-value group scan SDR matcher (synthetic leaves, no game)
    Test_Orden_PerSlotCap();
    Test_Orden_DistinctValues();
    Test_Orden_MissingValueRejected();
    Test_Orden_DuplicateValuesSDR();
    Test_Orden_MultiWidthMatch();
    Test_Orden_ConvergenceAndAssignment();
    Test_Orden_OrderedFirstScan();
    Test_Orden_BetweenFirstScan();
    Test_Orden_RoundedFloatExact();
    Test_Orden_PrevValueRejectedOnFirstScan();

    // Ubel — Native-C scan P0: hole computation + Guess-type normalization (pure)
    Test_Holes_ComputeHoles_Basic();
    Test_Holes_LeadingGapSurvives();
    Test_Holes_FullyCovered();
    Test_Holes_ClampsOutOfWindow();
    Test_Holes_ComputeClassHoles_ArrayDim();
    Test_IsSanePropertiesSize();
    Test_Holes_NormalizeGuessedType();

    // Macht — AOB pattern parser: nibble wildcards (4? / ?5) + anchor selection
    Test_Sig_IsCeReplayableAob();
    Test_Macht_ParsePattern_Nibble();

    // Tot — per-command cancel immunity is independent of "is a background worker"
    Test_Tot_CancelImmunityVsBackgroundWorker();

    // Routine — SafeThread: ~std::thread on a joinable thread terminates the process
    Test_Routine_SafeThread();

    // Grimoire — Cheat Engine host detection (prefix, not an exact-name list)
    Test_Grimoire_IsCheatEngineExeName();
    Test_Grimoire_HostAllowsBackgroundThreads();

    // Renge — hex parsing has a failure channel (write_mem can refuse a bad pattern)
    Test_Renge_TryHexToBytes();

    // DynOff — FFieldClass::Name probe (UE 5.8 virtual-dtor member shift)
    Test_FFieldClassName_Probe();

    std::printf("------------------------------------------\n");
    std::printf("Pass: %d   Fail: %d\n", g_pass, g_fail);
    return g_fail;
}
