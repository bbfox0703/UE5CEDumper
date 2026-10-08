// dll_core_test.cpp
// UE5CEDumper — the first test target that compiles the DLL's CORE.
//
// WHY THIS EXISTS
//   Until now no test target compiled Aura.cpp, Ubel.cpp, Genau.cpp, Macht.cpp or
//   Serie.cpp — 23,000 lines holding every GObjects walk, the class walker and the
//   offset finder. Findings were shipped unverifiable for exactly that reason, and A10
//   was declined partly on it. `dll_helpers_test` deliberately stays a LEAF target
//   (Radar + Denken only); this is the heavyweight sibling.
//
// HOW IT WORKS, and why it needs no game
//   Macht reads the CURRENT PROCESS. So a fake FUObjectArray built in this test's own
//   memory is, to Aura, indistinguishable from a real one — Aura::Init points it at the
//   fixture and every read lands on our bytes. That is the fixture the repo has wanted
//   ("C1"): object-pool-shaped state that can be created on demand.
//
// ⚠ THE LAYOUT IS FORCED, NOT DETECTED
//   InitWithExtendedLayout pins {Objects@+0x10, MaxElements@+0x20, NumElements@+0x24,
//   MaxChunks@+0x28, NumChunks@+0x2C} instead of letting DetectLayout guess from
//   content. A fixture whose layout was auto-detected would be testing the detector,
//   and a detector that guessed WRONG would silently give a pool of zero objects — which
//   reads exactly like "the walk was cancelled". The positive control below exists to
//   separate those two.
//
// THE EXTERNAL SURFACE (measured by an incremental link probe, not guessed):
//   Sein x5, Stark::SetInvokeTimeoutMs, g_cachedUEVersion — stubbed here; Zydis and
//   version.lib — linked by CMake.

#include <windows.h>
#include <psapi.h>    // K32GetProcessMemoryInfo (kernel32 on Windows 7+): the step-2 tests measure a ring freed
#include <intrin.h>   // __cpuid: the benchmarks name the CPU they ran on
#include <stdio.h>
#include <cstdint>
#include <cstring>
#include <vector>
#include <atomic>
#include <thread>

namespace Sein {
void Info(const char*, const char*, ...) {}
void Error(const char*, const char*, ...) {}
void Warn(const char*, const char*, ...) {}
void Debug(const char*, const char*, ...) {}
void Summary(const char*, ...) {}
}  // namespace Sein

// Stark.cpp is the MinHook ProcessEvent hook — no place in a headless test.
namespace Stark { void SetInvokeTimeoutMs(int) {} }

// Defined in Frieren.cpp (the C ABI layer) in the DLL. Defining it here is a FEATURE:
// the test chooses the UE version the core code branches on.
uint32_t g_cachedUEVersion = 0;

// ⚠ #undef between every include, and it is not just warning silencing.
// Each of these .cpp files is its own translation unit in the real DLL, and each either
// #defines LOG_CAT itself or inherits Sein.h's `#ifndef LOG_CAT -> ""` default. Concatenated
// into ONE TU here, the second and later #defines are redefinitions (C4005) -- and worse, the
// two files that define NOTHING (Radar.cpp, Denken.cpp) silently inherited whatever the
// PREVIOUS include happened to leave behind, so their log lines were attributed to another
// module's category in a way the shipping build never does. Undef-ing makes this harness
// match the real build instead of merely compiling quietly.
#undef LOG_CAT
#include "../src/Macht.cpp"      // NOLINT
#undef LOG_CAT
#include "../src/Serie.cpp"      // NOLINT
#undef LOG_CAT
#include "../src/Ubel.cpp"       // NOLINT
#undef LOG_CAT
#include "../src/Radar.cpp"      // NOLINT
#undef LOG_CAT
#include "../src/Denken.cpp"     // NOLINT
#undef LOG_CAT
#include "../src/Flamme.cpp"     // NOLINT
#undef LOG_CAT
#include "../src/Aura.cpp"       // NOLINT
#undef LOG_CAT
#include "../src/Genau.cpp"      // NOLINT
#undef LOG_CAT
#include "../src/Linie.cpp"      // NOLINT -- the profiler table: RecordCall's accumulation behind IsPerFrame

// ── harness ──────────────────────────────────────────────────────────

static int g_pass = 0, g_fail = 0;
static void check(const char* label, bool cond, const char* got = nullptr) {
    if (cond) { ++g_pass; }
    else { ++g_fail; printf("  FAIL  %s%s%s\n", label, got ? "   got: " : "", got ? got : ""); }
}
static void blk(const char* name) { printf("- %s\n", name); }

// ── the fake object pool ─────────────────────────────────────────────
//
// Chunked layout: Objects points at a table of chunk pointers, each chunk holding
// Grimoire::OBJECTS_PER_CHUNK items of s_itemSize bytes. We use the 24-byte
// FUObjectItem shape (UObject* at +0, flags, index, serial) that InitWithExtendedLayout
// is told to expect.

struct FakePool {
    static constexpr int kItemSize = 24;

    std::vector<uint8_t>            header;     // the FUObjectArray struct itself
    std::vector<uintptr_t>          chunkTable;
    std::vector<std::vector<uint8_t>> chunks;
    std::vector<uint8_t>            objects;    // backing bytes the UObject* values point into

    // Build a pool of `count` objects. Every object pointer is non-null and distinct, so
    // ForEach's `if (obj != 0)` filter passes for all of them and the count it yields is
    // exactly `count` — which is what makes the positive control meaningful.
    void Build(int32_t count) {
        const int perChunk = Grimoire::OBJECTS_PER_CHUNK;
        const int nChunks  = (count + perChunk - 1) / perChunk;

        objects.assign(static_cast<size_t>(count) * 64, 0);   // 64 B of room per "object"
        chunks.resize(nChunks);
        chunkTable.resize(nChunks);
        for (int c = 0; c < nChunks; ++c) {
            chunks[c].assign(static_cast<size_t>(perChunk) * kItemSize, 0);
            chunkTable[c] = reinterpret_cast<uintptr_t>(chunks[c].data());
        }
        for (int32_t i = 0; i < count; ++i) {
            uint8_t* item = chunks[i / perChunk].data() + static_cast<size_t>(i % perChunk) * kItemSize;
            uintptr_t obj = reinterpret_cast<uintptr_t>(objects.data() + static_cast<size_t>(i) * 64);
            memcpy(item, &obj, sizeof(obj));               // UObject* at +0
            int32_t idx = i;
            memcpy(item + 12, &idx, sizeof(idx));          // InternalIndex-ish
        }

        header.assign(0x40, 0);
        uintptr_t tbl = reinterpret_cast<uintptr_t>(chunkTable.data());
        memcpy(header.data() + 0x10, &tbl,   sizeof(tbl));    // Objects
        memcpy(header.data() + 0x20, &count, sizeof(count));  // MaxElements
        memcpy(header.data() + 0x24, &count, sizeof(count));  // NumElements
        int32_t mc = nChunks;
        memcpy(header.data() + 0x28, &mc, sizeof(mc));        // MaxChunks
        memcpy(header.data() + 0x2C, &mc, sizeof(mc));        // NumChunks
    }

    uintptr_t Addr() const { return reinterpret_cast<uintptr_t>(header.data()); }
};

// [SCAN-EARLY-TRIGGER-CONTAINED] AOBScanAll on a module base, run under SEH so a fault is a FAIL line, not a dead
// test process. The vector lives in its own function: a __try frame cannot hold objects that need unwinding.
static size_t ScanCountAt(const char* pattern, uintptr_t base) {
    return Macht::AOBScanAll(pattern, base).size();
}
static bool ScanFaultsAt(const char* pattern, uintptr_t base, size_t* count) {
    __try { *count = ScanCountAt(pattern, base); return false; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return true; }
}

// Tot's cancel flag is a per-command atomic in a header. Reset it explicitly between
// cases: it is process-global and a leaked `true` would make every later walk look
// cancelled — the exact shape that would turn this whole file into a false pass.
static void ResetCancel() {
    Tot::g_perCommand.store(false);
    Tot::g_shutdown.store(false);
}

// [LIVEFUNCS-STEP3] S3-M1: a stack captured from a known frame. Not inlined, and every one stores its callee's result
// to a volatile after the call, so no call is a tail call that the compiler could turn into a jump (the design review's
// M5). Arithmetic is not enough: `n + 0 * x` folds to `n`, and the first green run lost S3Outer's frame to exactly that.
static volatile uint32_t g_s3Sink = 0;
static __declspec(noinline) uint32_t S3CaptureFromHere(uint64_t* out, uint32_t max, uint16_t& fl, uint64_t& myRet) {
    myRet = reinterpret_cast<uint64_t>(_ReturnAddress());
    const uint32_t n = Macht::CaptureCallerStack(reinterpret_cast<uintptr_t>(_AddressOfReturnAddress()), out, max, fl);
    g_s3Sink = n;
    return n;
}
static __declspec(noinline) uint32_t S3Outer(uint64_t* out, uint32_t max, uint16_t& fl, uint64_t& myRet, uint64_t& outerRet) {
    outerRet = reinterpret_cast<uint64_t>(_ReturnAddress());
    const uint32_t n = S3CaptureFromHere(out, max, fl, myRet);
    g_s3Sink = n;
    return n;
}
static __declspec(noinline) uint32_t S3Deep(int depth, uint64_t* out, uint32_t max, uint16_t& fl) {
    if (depth <= 0) {
        uint64_t r = 0;
        return S3CaptureFromHere(out, max, fl, r);
    }
    const uint32_t n = S3Deep(depth - 1, out, max, fl);
    g_s3Sink = n;   // work after the call: the recursion stays a recursion
    return n;
}
static WORD NTAPI S3WalkerNoAnchor(DWORD, DWORD count, PVOID* frames, PDWORD) {
    const DWORD n = count < 3 ? count : 3;
    for (DWORD i = 0; i < n; ++i) frames[i] = reinterpret_cast<PVOID>(static_cast<uintptr_t>(0x5000 + i));
    return static_cast<WORD>(n);
}
static WORD NTAPI S3WalkerFaults(DWORD, DWORD, PVOID*, PDWORD) {
    volatile int* p = nullptr;
    *p = 1;   // the walk itself faults: the capture must survive it
    return 0;
}
// [LIVEFUNCS-STEP3] S3-L1: a stand-in for Macht's capturer in Linie's stack slots. It writes the frames sp, sp + 1,
// sp + 2 (at most `max`) and keeps the `max` it was handed, so a test sees what Linie asked for; mode 1 reads nothing
// and says BadSp, mode 2 throws as a walk that faults would unwind under the DLL's /EHa.
static uint32_t g_s3StubMax  = 0;
static int      g_s3StubMode = 0;
static uint32_t S3StubCap(uintptr_t sp, uint64_t* out, uint32_t max, uint16_t& fl) {
    g_s3StubMax = max;
    if (g_s3StubMode == 1) { fl = Macht::kStackBadSp; return 0; }
    if (g_s3StubMode == 2) throw std::runtime_error("walk fault");
    const uint32_t n = max < 3 ? max : 3;
    for (uint32_t j = 0; j < n; ++j) out[j] = sp + j;
    fl = 0;
    return n;
}
// S3-M1 case 8: TraceEnter from a frame whose own return-address slot is the `sp` it passes, as Stark's hook does.
static __declspec(noinline) void S3TraceAt(uint64_t hintGen, uint64_t& myRet, Linie::TraceToken& tok) {
    myRet = reinterpret_cast<uint64_t>(_ReturnAddress());
    Linie::ArmHint h{};
    h.gen = hintGen;
    h.stackRing = 0;
    Linie::TraceEnter(0xF5, 0, reinterpret_cast<uintptr_t>(_AddressOfReturnAddress()), 1, tok, 0, h);
    g_s3Sink = 1;   // a store after the call: never a tail call
}
static __declspec(noinline) void S3TraceDeep(int depth, uint64_t hintGen, uint64_t& myRet, Linie::TraceToken& tok) {
    if (depth <= 0) S3TraceAt(hintGen, myRet, tok);
    else S3TraceDeep(depth - 1, hintGen, myRet, tok);
    g_s3Sink = static_cast<uint32_t>(depth);
}

int main() {
    setvbuf(stdout, nullptr, _IONBF, 0);
    printf("dll_core_test — the DLL core, against a fake object pool in this process\n");

    // The poll is `(i & 0xFFF) == 0`, i.e. every 4096. A pool must be several multiples
    // of that or "it stopped early" cannot be told from "it ran to the end".
    constexpr int32_t kCount = 4096 * 4;   // 16,384

    FakePool pool;
    pool.Build(kCount);
    Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);

    {   blk("the fixture itself — a positive control, because a broken pool reads as 'cancelled'");
        ResetCancel();
        check("GetCount reports the pool size", Aura::GetCount() == kCount);

        int seen = 0;
        Aura::ForEach([&](int32_t, uintptr_t obj) { if (obj) ++seen; return true; });
        check("ForEach visits EVERY object when not cancelled", seen == kCount);
        // ⚠ Without this, every assertion below would pass against a pool of zero objects.
    }

    // [VER-410-GATE] What the resource readings decide before any memory scan. Tier 1 below the floor is a refusal of
    // the whole scan, so each way to reach it is pinned, and so is each way that must NOT reach it.
    {   blk("VER-410-GATE - a reading below the floor is taken at tier 1 only when the resources corroborate it");
        using Genau::DecideResourceVersion;
        const char* isDefense = "4.10.2-0+++depot+UE4-Releases+4.10";

        auto v = DecideResourceVersion(410, true, isDefense, 0);
        check("VER-410-GATE ⭐: IS Defense (fixed 4.10.2, its engine build string) is tier 1",
              v.version == 410 && v.tier == 1 && v.byBuildString && !v.byCrc);
        v = DecideResourceVersion(410, true, "", 410);
        check("VER-410-GATE ⭐: 410 with a CrashReportClient agreeing is tier 1",
              v.version == 410 && v.tier == 1 && v.byCrc && !v.byBuildString);
        v = DecideResourceVersion(410, true, isDefense, 410);
        check("VER-410-GATE: both signals at once say so", v.tier == 1 && v.byBuildString && v.byCrc);

        v = DecideResourceVersion(0, false, "", 410);
        check("VER-410-GATE: a CrashReportClient alone (the exe carries no engine version) stays tier 3",
              v.version == 410 && v.tier == 3);
        v = DecideResourceVersion(427, true, "++UE4+Release-4.27-CL-0", 410);
        check("VER-410-GATE: a CrashReportClient saying 410 against an exe saying 427 stays tier 3",
              v.version == 410 && v.tier == 3);
        v = DecideResourceVersion(410, true, isDefense, 409);
        check("VER-410-GATE: the exe's corroboration does not carry over to a CrashReportClient overriding it (409)",
              v.version == 409 && v.tier == 3 && !v.byBuildString);
        v = DecideResourceVersion(410, false, "++UE4+Release-4.10-CL-0", 0);
        check("VER-410-GATE: a code read out of the string cannot corroborate itself -- tier 3", v.tier == 3);
        v = DecideResourceVersion(410, true, "4.10.3", 0);
        check("VER-410-GATE: a bare 4.10.3 beside a fixed 4.10 stays tier 3", v.version == 410 && v.tier == 3);
        v = DecideResourceVersion(405, true, "4.5.0.0", 0);
        check("VER-410-GATE: the b25a marker (4.5.0.0) stays tier 3", v.version == 405 && v.tier == 3);

        v = DecideResourceVersion(411, true, "4.11.0-0+UE4", 0);
        check("VER-410-GATE: the floor itself is tier 1, with nothing to corroborate",
              v.version == 411 && v.tier == 1 && !v.byBuildString && !v.byCrc);
        v = DecideResourceVersion(504, true, "", 0);
        check("VER-410-GATE: a supported reading needs no second signal", v.version == 504 && v.tier == 1);
        v = DecideResourceVersion(0, false, "", 0);
        check("VER-410-GATE: no reading at all is tier 0", v.version == 0 && v.tier == 0);
    }

    {   blk("A7 — ForEach honours Tot::Requested() and stops");
        ResetCancel();
        Tot::g_perCommand.store(true);          // cancel BEFORE the walk starts

        int seen = 0;
        Aura::ForEach([&](int32_t, uintptr_t) { ++seen; return true; });

        // The poll is at i == 0, so a cancel already pending stops it immediately.
        check("a pre-set cancel stops the walk at the first poll", seen == 0, std::to_string(seen).c_str());
        ResetCancel();
    }

    {   blk("A7 — cancelling MID-WALK stops it at the next poll boundary, not at the end");
        ResetCancel();
        int seen = 0;
        Aura::ForEach([&](int32_t i, uintptr_t) {
            ++seen;
            if (i == 100) Tot::g_perCommand.store(true);   // cancel from inside the walk
            return true;
        });
        ResetCancel();

        // Cancelled at i=100; the next poll is i=4096, so the walk must stop there --
        // strictly before the end, and strictly after the cancel. Asserting the BOUNDARY
        // rather than "less than kCount" is what makes this about the poll and not about
        // the callback returning false.
        check("stopped before the end", seen < kCount, std::to_string(seen).c_str());
        check("stopped at the 4096 poll boundary, not earlier or later",
              seen == 4096, std::to_string(seen).c_str());
    }

    {   blk("...and the cancel is not sticky — the next walk runs in full");
        ResetCancel();
        int seen = 0;
        Aura::ForEach([&](int32_t, uintptr_t obj) { if (obj) ++seen; return true; });
        check("a fresh walk after a cancelled one is complete", seen == kCount,
              std::to_string(seen).c_str());
    }

    {   blk("A7 (the REAL one) — FindByAddress honours the poll; the two blocks above do NOT cover it");
        // ⛔ WHY THIS BLOCK EXISTS: A7 WAS RECORDED AS VERIFIED BY TESTS THAT DO NOT TOUCH IT.
        // The two blocks above are labelled "A7" and drive `Aura::ForEach`. But ForEach ALREADY
        // had its poll -- it is one of the SIBLINGS A7 was made to match. The audit row says so
        // exactly (docs/audit-2026-08-13-early-code-findings.md:278): "FindByAddress is the ONLY
        // full-GObjects walk in the file with neither a Tot::Requested() poll nor a deadline".
        // And FindByAddress (Aura.cpp) hand-rolls `for (int32_t i = 0; i < count; ++i)` --
        // it never calls ForEach. Measured 2026-09-06: `grep FindByAddress dll/tests/ tools/verify/`
        // returned ZERO hits, so deleting A7's poll reddened nothing, while both
        // verification-register.md and todo.md `[A7-CORETEST-2026-08-25]` said it was verified.
        // A green claim computed by a different code path than the thing it claims about.
        //
        // ⚠ ANTI-VACUITY, and it is why this is four checks and not one: an UNCANCELLED lookup of
        // an address that is not in the pool ALSO returns found == false. "Not found" therefore
        // proves nothing on its own. The assertion is the FLIP of ONE FIXED ADDRESS under ONE
        // CHANGED FLAG -- which is only meaningful because (a) establishes it is findable first.
        const int32_t kIdx = 8000;      // past the i==4096 poll boundary, so a stop precedes the hit
        const uintptr_t objAddr =
            reinterpret_cast<uintptr_t>(pool.objects.data() + static_cast<size_t>(kIdx) * 64);

        // (a) POSITIVE CONTROL -- the address really is findable, and by the EXACT path.
        // Exactness matters: an exact hit returns at Aura.cpp `FindByAddress` and never enters the backward
        // module scan, which the audit deliberately left unpolled.
        ResetCancel();
        auto hit = Aura::FindByAddress(objAddr);
        check("FindByAddress finds an in-pool object when not cancelled", hit.found == true);
        check("...as an EXACT match, so the backward scan is never entered", hit.exactMatch == true);
        check("...at the index we planted it", hit.index == kIdx,
              std::to_string(hit.index).c_str());

        // (b) THE CASE A7 FIXED. The exact-match return is unconditional, so the ONLY thing that
        // can turn this same address into a miss is the poll at Aura.cpp `FindByAddress`.
        ResetCancel();
        Tot::g_perCommand.store(true);
        auto cancelled = Aura::FindByAddress(objAddr);
        check("A7: a cancelled FindByAddress abandons the walk", cancelled.found == false);
        check("...and reports no index rather than a stale one", cancelled.index == -1,
              std::to_string(cancelled.index).c_str());

        // (c) NOT STICKY -- guards against a leaked global cancel turning every later block in
        // this file into a false pass, which is the hazard ResetCancel's own comment describes.
        ResetCancel();
        check("...and the cancel is not sticky: the same address is found again",
              Aura::FindByAddress(objAddr).found == true);
    }

    // ── B18 — Extra Scan must bail on cancel, and say its results are partial ──────
    //
    // B18 as filed: "Extra Scan is uncancellable under an unbounded join => CE UI freezes".
    // Fixed in build 2603; VERIFYING it stayed blocked because on every title here the scan
    // finishes faster than a person can cancel it.
    //
    // The mechanism is a poll at a BATCH BOUNDARY inside Genau::ScanForTarget, and the
    // (MA1) comment beside it explains why it is there and not inside Macht: the largest
    // indivisible unit is one AOBScanBatch, measured at most 0.64 s on a 213 MB .text. So
    // what to test is not a DURATION -- it is that the poll is consulted and that the
    // report declares the results partial.
    //
    // ScanForTarget is `static` in Genau.cpp; this TU includes that file, which is the only
    // way to reach it without widening the header for a test.
    //
    // The scan runs against THIS PROCESS's modules -- which is what makes it work headlessly
    // -- and the pattern below cannot match anything, so the uncancelled run is a full scan
    // that finds nothing rather than an early success.
    {
        blk("B18 - the Extra Scan cancellation poll");

        static const AobSignature kNoMatch[] = {
            { "TEST_NOMATCH_1",
              "CC CC CC CC DE AD BE EF CA FE BA BE CC CC CC CC DE AD BE EF",
              AobTarget::GObjects, AobResolve::RipDirect,
              0, 3, 7, 0, 50, 0 },
        };
        auto neverValid = [](uintptr_t) -> bool { return false; };

        {   // POSITIVE CONTROL. Without it, "cancelled == true" below is equally consistent
            // with a scan that bails for some unrelated reason on every call.
            Genau::ScanReport rep;
            rep.targetName = "B18-control";
            ResetCancel();
            const uintptr_t got = Genau::ScanForTarget(
                kNoMatch, 1, neverValid, rep, false, false);

            check("control: an uncancelled scan runs to completion", rep.cancelled == false);
            check("control: and finds nothing, so it was not a lucky early exit", got == 0);

            // ⚠ WHY THIS CONTROL IS NOT VACUOUS, and it is worth writing down because the
            // whole run takes 0.08 s and that LOOKS like the scan never happened. It did:
            // the test process simply has few, small modules. The proof is the negative
            // control, not the duration — replacing the poll with `if (false)` reddens the
            // cancelled case below, which can only happen if the loop REACHES that line.
            // Same path up to the poll in both cases, so a control that passes here is a
            // control that ran.
        }

        {   // The case. The poll sits at the TOP of the batch loop, so a cancel already
            // pending stops it on batch 0 -- deterministic, with no thread race to lose.
            Genau::ScanReport rep;
            rep.targetName = "B18-cancelled";
            ResetCancel();
            Tot::g_perCommand.store(true);
            const uintptr_t got = Genau::ScanForTarget(
                kNoMatch, 1, neverValid, rep, false, false);
            ResetCancel();

            check("cancelled: the scan reports itself CANCELLED", rep.cancelled == true);
            // The half that matters operationally: a cancelled scan must not hand back an
            // address, because its own log line says partial results MUST NOT be published.
            check("cancelled: and returns no address", got == 0);
        }
    }

    // ── A10 — IS THE RECYCLED-UClass* DEFECT REPRODUCIBLE AT ALL? ─────────────────
    //
    // A10 was declined on 2026-08-24 with four reasons, and one of them was that nothing
    // in the tree can recycle a UClass* on demand, so no fix could be shown to work and no
    // test could be shown to fail. That reason was true when it was written. This target
    // makes it checkable: the fixture's memory is OURS, so an address can be made to hold
    // a different class simply by overwriting the bytes.
    //
    // This case does NOT fix A10 and does not assert the code is correct. It answers the
    // prior question the decision turned on: does the defect actually reproduce?
    {
        blk("A10 - can a recycled class address be made to serve STALE metadata?");

        // A minimal walkable "UClass": WalkClassEx caches whenever
        // ShouldPublishClassWalk(true, PropertiesSize) holds, and that is only
        // IsPlausiblePropertiesSize -- a range check. Name/super reads may fail harmlessly.
        // STATIC, never a heap buffer: this class stays in s_walkClassExCache for the rest of the
        // run, and a freed heap block's address is handed to the next same-sized allocation -- a
        // later test's class then read THIS cached answer (SANEPROPS failed that way on 2026-09-27).
        static uint8_t blob[0x200] = {};
        const uintptr_t X = reinterpret_cast<uintptr_t>(blob);

        auto setPropsSize = [&](int32_t v) {
            memcpy(blob + DynOff::USTRUCT_PROPSSIZE, &v, sizeof(v));
        };

        setPropsSize(100);
        const int32_t first = Ubel::WalkClassEx(X).PropertiesSize;

        // The recycle: same address, different class.
        setPropsSize(200);
        const int32_t second = Ubel::WalkClassEx(X).PropertiesSize;

        check("A10 fixture: the first walk was cached at all (else this proves nothing)",
              first == 100, std::to_string(first).c_str());

        // ⚠ NOT an assertion that the code is right. Whichever way this lands it is
        // information the A10 decision did not have:
        //   second == 100 -> the defect REPRODUCES: a recycled address serves the old class
        //   second == 200 -> it does not reproduce here, and the fixture is too weak to
        //                    settle A10 -- which is itself worth knowing before anyone
        //                    spends the ~36-call-site refactor.
        printf("    [A10] after overwriting the class at the same address: "
               "PropertiesSize %d -> %d  (%s)\n",
               first, second,
               second == first ? "STALE — the defect reproduces"
                               : "re-read — not reproduced by this fixture");
    }

    // ⚠ THE SUMMARY USED TO BE PRINTED HERE, mid-main, and it UNDERCOUNTED SILENTLY.
    // `check()` prints nothing on success, so the only visible evidence a block ran is its
    // `blk()` label and the final tally. Every block appended after this point — SANEPROPS,
    // and later the A1 lazy-stride block — executed and passed while the printed line still
    // said "11 checks", i.e. the report and the reality were computed at different points in
    // the same function. Moved to just before `return`, where it counts everything.
    // (Found 2026-09-05 while adding the A1 block: its six checks were invisible.)

    // -- SANEPROPS-2026-08-26 -- a big class is not a recycled one -----------------
    //
    // One constant answered two questions. P3R has two REAL classes at ~3.67 MB
    // (XRD777SaveGame / AstreaSaveGame) that walk their fields cleanly; the old 1 MB
    // bound declared them recycled, so WalkInstance zeroed propsSize and skipped the
    // walk, and the UI told the user a live object had been freed.
    //
    // Decided by ARITHMETIC on a class blob we own, so none of these can go green for
    // a heap-layout reason.
    {
        blk("SANEPROPS - a real 3.6 MB class must walk; a garbage one must still bail");

        // ONE CLASS BLOB PER CASE, and it is not tidiness: s_walkClassExCache is keyed
        // by the class address (per family epoch) and NOTHING erases it (that unboundedness is exactly
        // why the plausibility ceiling has to stay a bound). Reusing one address makes
        // every case after the first read the FIRST case's memoised answer -- the A10
        // defect the fixture above demonstrates, hit here by accident while writing
        // this test.
        // STATIC blobs, for the same reason one step further: a heap buffer can be handed the
        // address of a class an EARLIER test walked and freed, and then reads that test's memoised
        // answer. 2026-09-27: A10's 0x200-byte class vector is freed right before this block; in one
        // build.ps1 run "garbage 827 MB is STILL judged stale" failed -- what reading A10's cached
        // small PropertiesSize would produce -- and it passed in three standalone reruns. The address
        // reuse was inferred from that, not logged; static storage removes it either way.
        static uint8_t objBlob[0x200] = {};
        const uintptr_t O = reinterpret_cast<uintptr_t>(objBlob);
        static uint8_t clsGarbage[0x200] = {}, clsEmpty[0x200] = {}, clsBig[0x200] = {};
        auto setPropsSize = [](uint8_t* b, int32_t v) {
            memcpy(b + DynOff::USTRUCT_PROPSSIZE, &v, sizeof(v));
            return reinterpret_cast<uintptr_t>(b);
        };

        // Order matters: prove the wedge case still bails BEFORE asking the walker to
        // accept a multi-megabyte size, so a regression shows up as a failing check
        // rather than as a hung test run.
        const uintptr_t Cg = setPropsSize(clsGarbage, 867763776);  // Elliot, ~827 MB
        auto garbage = Ubel::WalkInstance(O, Cg, 64, 2, /*fillGaps=*/true);
        check("garbage 827 MB is STILL judged stale", garbage.isStale == true);
        check("...and its propsSize is zeroed", garbage.propsSize == 0);

        const uintptr_t Ce = setPropsSize(clsEmpty, 0);
        auto empty = Ubel::WalkInstance(O, Ce, 64, 2, /*fillGaps=*/true);
        check("a field-less class (propsSize 0) is NOT stale", empty.isStale == false);

        const uintptr_t Cb = setPropsSize(clsBig, 3671816);        // P3R AstreaSaveGame
        auto big = Ubel::WalkInstance(O, Cb, 64, 2, /*fillGaps=*/true);
        check("P3R's real 3.6 MB class is NOT stale (THE bug)", big.isStale == false);
        check("...and its propsSize survives", big.propsSize == 3671816);
        check("...and the gap pass says it SKIPPED rather than saying nothing",
              big.gapFillSkipped == true);

        // The control for the flag itself: a default walk never asked for gap-fill, so
        // it must not claim a skip. Setting the flag beside the stale gate instead of
        // beside the gap pass would make this true on every ordinary walk. Same address
        // as the case above ON PURPOSE -- the class answer is meant to be memoised; it
        // is the per-WALK flag that must differ.
        auto bigNoFill = Ubel::WalkInstance(O, Cb, 64, 2, /*fillGaps=*/false);
        check("a walk that did not ask for gap-fill does not claim a skip",
              bigNoFill.gapFillSkipped == false);
    }

    {   blk("A1 follow-up — sizeof(TLazyObjectPtr) is DERIVED, and is 0x20 in NO era");
        // ⭐ WHY THIS TEST EXISTS, AND WHY IT IS OFFLINE. `ReadLazyObjectArrayElements` forced
        // `elemSize = 0x20` — the FWeakObjectPtr(8)+Tag(4)+pad(4)+FGuid(16) model audit A1 was
        // written to delete — and `InferScalarSize` returned the same constant, while
        // `ResolveInnerSize` consults InferScalarSize BEFORE it ever asks the engine, so nothing
        // downstream could correct it. Two costs: every array element from index 1 drifted, and
        // LazyGuidOffset(0x20) computes 0x10, which PersistentPtrEnvelopeFor REJECTS — so the
        // `payload envelope measured` line could never be emitted from an array walk, and an
        // operator reaching lazy that way would score a CORRECT fix as FAILED.
        //
        // It is offline because no installed title has a TArray<TLazyObjectPtr> (OCTOPATH has 5
        // scalar lazy properties and zero arrays), and because the 2026-09-05 batch spent itself
        // believing "Ubel.cpp is in no test target" — it has been in THIS one since 2026-08-25.
        //
        // The truth being pinned: FUniqueObjectGuid is a bare FGuid (4×uint32, alignof 4), so
        // there is no pad after the tag. 0x1C up to 5.2; 0x18 from 5.3, where TagAtLastTest was
        // deleted. OCTOPATH reported ElementSize 0x1C live on 2026-09-05 — the same number from
        // the engine's own side, which is what makes these constants a measurement and not a guess.
        const uint32_t savedVer   = g_cachedUEVersion;
        const int      savedLatch = DynOff::LAZYPTR_GUID;
        DynOff::LAZYPTR_GUID = -1;          // no latch, so the version fallback is what answers

        char buf[96];
        g_cachedUEVersion = 502;
        const int32_t at502 = Ubel::InferScalarSize("LazyObjectProperty");
        snprintf(buf, sizeof(buf), "0x%X", at502);
        check("UE 5.2 -> 0x1C  (FWeakObjectPtr 8 + Tag 4, no pad)", at502 == 0x1C, buf);

        g_cachedUEVersion = 418;
        const int32_t at418 = Ubel::InferScalarSize("LazyObjectProperty");
        snprintf(buf, sizeof(buf), "0x%X", at418);
        check("UE 4.18 -> 0x1C, matching OCTOPATH's measured ElementSize", at418 == 0x1C, buf);

        g_cachedUEVersion = 503;
        const int32_t at503 = Ubel::InferScalarSize("LazyObjectProperty");
        snprintf(buf, sizeof(buf), "0x%X", at503);
        check("UE 5.3 -> 0x18  (TagAtLastTest deleted)", at503 == 0x18, buf);

        g_cachedUEVersion = 508;
        const int32_t at508 = Ubel::InferScalarSize("LazyObjectProperty");
        check("UE 5.8 -> 0x18 as well", at508 == 0x18);

        // THE REGRESSION GUARD, and it is the whole point: the old value must be unreachable.
        check("...and 0x20 is returned by NO era",
              at502 != 0x20 && at418 != 0x20 && at503 != 0x20 && at508 != 0x20);

        // The boundary is 5.3 exactly, not "somewhere in 5.x" — 5.2 and 5.3 must differ by the
        // 4-byte tag and nothing else.
        check("...and the 5.2/5.3 step is exactly the 4-byte tag", at502 - at503 == 4);

        g_cachedUEVersion    = savedVer;
        DynOff::LAZYPTR_GUID = savedLatch;
    }

    {   blk("A1 follow-up (2) — ReadLazyObjectArrayElements itself, which the block above does NOT cover");
        // ⛔ WHY THE BLOCK ABOVE IS NOT ENOUGH, and I am recording this because it is my own
        // overstatement from earlier today. That block pins `InferScalarSize` — an arithmetic
        // helper. The function the `elemSize = 0x20` bug actually lived in is
        // `ReadLazyObjectArrayElements`, and it had ZERO coverage: grepping dll/tests/ and
        // tools/verify/ for it returned nothing. Pinning the helper and calling the fix verified is
        // the same mistake audit A7 made — a test that names a NEIGHBOUR of its subject (§1.12).
        //
        // Offline because `Macht::ReadTArray` (Macht.h) only sanity-checks Count/Max, so a
        // { Data, Num, Max } triple in this process's own memory is a real TArray to it. No
        // installed title has a TArray<TLazyObjectPtr> — OCTOPATH has 5 scalar lazy properties and
        // zero arrays, ES2 has 3 and zero — so a live row for this would be unfalsifiable.
        const uint32_t savedVerArr   = g_cachedUEVersion;
        const int      savedLatchArr = DynOff::LAZYPTR_GUID;

        constexpr int32_t kN = 6, kStride = 0x18;   // UE >= 5.3: FWeakObjectPtr(8) + FGuid(16)
        // ⭐ POISON EVERYWHERE, and the index ENCODED IN THE VALUE. A wrong stride then does not
        // merely read "a different element" — it reads 0xEE, and the failing check names which
        // element drifted. A "the count came back right" assertion would pass at 0x20 and prove
        // nothing, because the bug reads element 0 correctly and only drifts from index 1.
        std::vector<uint8_t> elems(static_cast<size_t>(kN) * 0x20 + 0x40, 0xEE);
        for (int32_t i = 0; i < kN; ++i) {
            uint8_t* e = elems.data() + static_cast<size_t>(i) * kStride;
            int32_t objIdx = -1, serial = -1;        // unresolvable, so `value` is the GUID alone
            memcpy(e + 0, &objIdx, sizeof(objIdx));
            memcpy(e + 4, &serial, sizeof(serial));
            uint32_t a = 0xA0000000u | i, b = 0xB0000000u | i,
                     c = 0xC0000000u | i, d = 0xD0000000u | i;
            memcpy(e + 0x08 +  0, &a, sizeof(a));    // FGuid at +0x08 for UE >= 5.3
            memcpy(e + 0x08 +  4, &b, sizeof(b));
            memcpy(e + 0x08 +  8, &c, sizeof(c));
            memcpy(e + 0x08 + 12, &d, sizeof(d));
        }
        struct FakeTArray { uintptr_t Data; int32_t Num; int32_t Max; };
        FakeTArray arr{ reinterpret_cast<uintptr_t>(elems.data()), kN, kN };

        g_cachedUEVersion    = 506;                  // untagged era -> envelope 0x08, stride 0x18
        DynOff::LAZYPTR_GUID = -1;                   // no latch: make the run derive it
        auto lr = Ubel::ReadLazyObjectArrayElements(
            reinterpret_cast<uintptr_t>(&arr), 0, kStride, 0, 64);

        check("lazy array: the read succeeds against an in-process TArray", lr.ok == true);
        check("...and yields every element", static_cast<int32_t>(lr.elements.size()) == kN,
              std::to_string(lr.elements.size()).c_str());

        bool allRight = (static_cast<int32_t>(lr.elements.size()) == kN);
        int firstWrong = -1;
        for (int32_t i = 0; allRight && i < kN; ++i) {
            char want[48];
            snprintf(want, sizeof(want), "{A000000%X-B000000%X-C000000%X-D000000%X}", i, i, i, i);
            if (lr.elements[i].value.find(want) == std::string::npos) {
                allRight = false;
                firstWrong = i;
            }
        }
        check("A1 ⭐: EVERY element decodes at its own stride — index encoded in the GUID, so a "
              "drift reads poison", allRight,
              firstWrong >= 0 ? ("first wrong element: " + std::to_string(firstWrong) + " got "
                                 + lr.elements[firstWrong].value).c_str() : nullptr);
        // The one that fails first under the old bug: element 0 is read correctly by BOTH strides,
        // so it is element 1 that discriminates. Asserted separately so the failure says so.
        if (lr.elements.size() > 1) {
            check("A1: ...element 1 specifically — the first index the 0x20 stride gets wrong",
                  lr.elements[1].value.find("{A0000001-B0000001-C0000001-D0000001}")
                      != std::string::npos,
                  lr.elements[1].value.c_str());
        }

        g_cachedUEVersion    = savedVerArr;
        DynOff::LAZYPTR_GUID = savedLatchArr;
    }

    {   blk("G6 — the fork's tag→key table is TRI-state: a transient miss must not be cached");
        // ⛔ WHAT G6 ACTUALLY ASSERTS, and why one bool cannot express it. The old code cached a
        // permanent TAGKEY_MISS, so a single unlucky read — the fork grows this table WHILE the
        // game runs — blanked every FName carrying that tag for the rest of the process, even
        // though the fork's own lookup would have succeeded a millisecond later. The fix splits
        // "could not determine" from "determined: absent":
        //
        //   readError=true   no ctx / count==sentinel / idx>=count / a failed read
        //                      -> GetTagKey returns false and CACHES NOTHING; the next name retries
        //   readError=false  the chain ended cleanly at idx<0, i.e. genuinely ABSENT
        //                      -> the fork stores that block unXOR'd, so key 0 IS the answer.
        //                         Resolve to 0 and cache it.
        //
        // The two misses must get OPPOSITE treatment. That opposition is the whole finding, so
        // every check below is a PAIR: the same tag before and after the table settles.
        //
        // Offline because `Macht::ReadSafe` reads THIS process, so a std::vector shaped like the
        // fork's hash table is indistinguishable from the fork's own — the same trick the fake
        // FUObjectArray above uses. G6's live host (MindsEye) is not installed and is not coming.
        // ⚠ SCOPE: this pins the TRI-STATE LOGIC, not that the ctx offsets match MindsEye's real
        // table. That half came from RE and can only ever be confirmed on the fork. G6 was filed
        // as a logic defect, so the logic is the finding.
        constexpr int32_t kCap = 16, kCountLow = 2, kCountAll = 8;
        std::vector<uint8_t> ctx(0x80, 0), entries(static_cast<size_t>(kCap) * 24, 0);
        std::vector<int32_t> buckets(kCap, -1);

        auto putEntry = [&](int i, uint16_t tag, uint8_t k) {
            uint8_t* e = entries.data() + static_cast<size_t>(i) * 24;
            uint64_t val = k;            // the key is the LOW BYTE of the u64 value
            int32_t  next = -1;
            memcpy(e + 0x00, &tag, sizeof(tag));
            memcpy(e + 0x08, &val, sizeof(val));
            memcpy(e + 0x10, &next, sizeof(next));
        };
        auto setCount    = [&](int32_t c) { memcpy(ctx.data() + 0x18, &c, sizeof(c)); };
        auto setSentinel = [&](int32_t s) { memcpy(ctx.data() + 0x44, &s, sizeof(s)); };

        // Bucket index is `tag & (capacity-1)`, so the low nibbles must differ or one tag's
        // chain walks into another's and the arms stop being independent.
        constexpr uint16_t T_FOUND = 0x0101, T_ABSENT = 0x0202, T_TORN = 0x0303, T_EMPTY = 0x0404;
        putEntry(0, T_FOUND, 0x5A);
        putEntry(1, T_EMPTY, 0x3C);
        putEntry(5, T_TORN,  0x7E);      // index 5 is PAST kCountLow — the torn-read case
        buckets[1] = 0; buckets[2] = -1; buckets[3] = 5; buckets[4] = 1;

        const uintptr_t entriesAddr = reinterpret_cast<uintptr_t>(entries.data());
        const uintptr_t bucketsAddr = reinterpret_cast<uintptr_t>(buckets.data());
        memcpy(ctx.data() + 0x10, &entriesAddr, sizeof(entriesAddr));
        memcpy(ctx.data() + 0x50, &bucketsAddr, sizeof(bucketsAddr));
        memcpy(ctx.data() + 0x58, &kCap, sizeof(kCap));
        setCount(kCountLow);
        setSentinel(0x7FFFFFFF);          // never equal to count -> the "empty" guard stays open

        // ⚠ The pool must be all zeroes. InitObfuscated ends by calling FirstEntrySampleText(),
        // which READS an entry to log a sample; a non-zero header there would decode a name and
        // seed s_tagKey before a single assertion runs. A zero header is length 0, which GetString
        // rejects before it ever looks at a tag.
        std::vector<uint8_t> chunk(0x400, 0), poolHdr(0x20, 0);
        const uintptr_t chunkAddr = reinterpret_cast<uintptr_t>(chunk.data());
        memcpy(poolHdr.data() + 0x10, &chunkAddr, sizeof(chunkAddr));
        Serie::InitObfuscated(reinterpret_cast<uintptr_t>(poolHdr.data()), 0x10, 2,
                              reinterpret_cast<uintptr_t>(ctx.data()));

        uint8_t key = 0xEE;
        check("G6 control: a tag PRESENT in the table resolves to its key",
              Serie::GetTagKey(T_FOUND, key) && key == 0x5A);

        // --- ARM 1: a torn read is transient, so it must NOT be cached ---------------------
        key = 0xEE;
        check("G6: a link past the published count does NOT resolve (torn read)",
              Serie::GetTagKey(T_TORN, key) == false);
        setCount(kCountAll);                       // the fork's table finishes growing
        key = 0xEE;
        check("G6 ⭐: after the table settles the SAME tag resolves — no permanent blanking",
              Serie::GetTagKey(T_TORN, key) && key == 0x7E);

        // The same arm through the OTHER transient door, because `count == sentinel` and
        // `idx >= count` are different guards and a fix could restore one and not the other.
        setSentinel(kCountAll);                    // table reports itself empty
        key = 0xEE;
        check("G6: a table reporting itself empty does NOT resolve",
              Serie::GetTagKey(T_EMPTY, key) == false);
        setSentinel(0x7FFFFFFF);
        key = 0xEE;
        check("G6 ⭐: ...and that tag recovers too once the table settles",
              Serie::GetTagKey(T_EMPTY, key) && key == 0x3C);

        // --- ARM 2: a clean chain end is DETERMINED, so it must be cached ------------------
        key = 0xEE;
        check("G6: a genuinely ABSENT tag RESOLVES rather than failing",
              Serie::GetTagKey(T_ABSENT, key) == true);
        check("...to key 0 — the fork stores that block unXOR'd, so plaintext is the answer",
              key == 0x00);

        // The discriminator between the two arms: pull the table away and ask again. A cached
        // answer survives; an uncached one cannot, because LookupTagKey needs s_keyTableCtx.
        const uintptr_t savedCtx = Serie::s_keyTableCtx;
        Serie::s_keyTableCtx = 0;
        key = 0xEE;
        check("G6 ⭐: the ABSENT answer was CACHED (still answers with the table gone)",
              Serie::GetTagKey(T_ABSENT, key) && key == 0x00);
        key = 0xEE;
        check("G6 ⭐: ...while a transient miss was NOT (this tag now fails, having never cached)",
              Serie::GetTagKey(0x0505, key) == false);
        Serie::s_keyTableCtx = savedCtx;

        // Leave no global state behind: every later block in this file would inherit it.
        Serie::s_obfuscated = false;
        Serie::s_keyTableCtx = 0;
        Serie::s_poolAddr = 0;
        Serie::s_payloadGap = 0;
        Serie::s_tagKey.reset();
        Serie::s_initialized.store(false, std::memory_order_release);
    }


    {   blk("D3/D5 -- an UNREADABLE array element must not be published as a VALUE");
        // Blind-spot sweep, 2026-09-08. Two array readers fabricated a plausible answer
        // when the element read faulted: the multicast-delegate one published the
        // affirmative "(0 bindings)" and the TLazyObjectPtr one an all-zero FGuid, both
        // counted in readCount. Macht::ReadTArray (Macht.h) validates only Count
        // and Max and NEVER probes Data, so a freed buffer reaches the element loop with
        // the header looking perfectly sane -- that is why the fault arm is reachable at
        // all, and it is what these cases pin.
        ResetCancel();

        // A fake instance holding one TArray header { Data, Count, Max } at +0x00.
        struct FakeTArrayField { uintptr_t Data; int32_t Count; int32_t Max; };

        // 0x1000 is never mapped in a Windows user process, so every element read faults
        // while the HEADER itself reads fine out of our own stack.
        FakeTArrayField dead{ 0x1000, 3, 4 };
        const uintptr_t deadAddr = reinterpret_cast<uintptr_t>(&dead);

        auto mc = Ubel::ReadMulticastDelegateArrayElements(deadAddr, 0, 16, 0, 8);
        check("D3: an unreadable multicast element reports 3 elements", mc.elements.size() == 3,
              std::to_string(mc.elements.size()).c_str());
        bool mcHonest = !mc.elements.empty();
        for (const auto& e : mc.elements) mcHonest = mcHonest && e.value == "???";
        check("D3 ⭐: it renders as the unread sentinel, NOT \"(0 bindings)\"", mcHonest,
              mc.elements.empty() ? "(none)" : mc.elements[0].value.c_str());

        auto lz = Ubel::ReadLazyObjectArrayElements(deadAddr, 0, 0x18, 0, 8);
        bool lzHonest = !lz.elements.empty();
        for (const auto& e : lz.elements) lzHonest = lzHonest && e.value == "???";
        check("D5 ⭐: an unreadable TLazyObjectPtr renders \"???\", not a fabricated GUID",
              lzHonest, lz.elements.empty() ? "(none)" : lz.elements[0].value.c_str());

        // ⭐ THE CONTROL THAT MAKES D5 MEAN ANYTHING. An all-zero FGuid is the LEGITIMATE
        // value of an UNSET TLazyObjectPtr, so "print zeros differently" was never
        // available -- the fix has to separate UNREAD from READ-AS-ZERO. Read a REAL
        // buffer of zeroes and require the zeros back.
        alignas(16) uint8_t zeros[0x18 * 2] = {};
        FakeTArrayField live{ reinterpret_cast<uintptr_t>(zeros), 2, 2 };
        auto lz0 = Ubel::ReadLazyObjectArrayElements(
            reinterpret_cast<uintptr_t>(&live), 0, 0x18, 0, 8);
        bool zerosKept = lz0.elements.size() == 2;
        for (const auto& e : lz0.elements) zerosKept = zerosKept && e.value != "???";
        check("D5 ⭐ control: a genuinely all-zero FGuid still reads as a VALUE, not \"???\"",
              zerosKept, lz0.elements.empty() ? "(none)" : lz0.elements[0].value.c_str());

        // And the multicast twin: a readable, genuinely-empty inner TArray must still say
        // "(0 bindings)" -- otherwise the fix would have replaced one wrong answer with
        // another.
        alignas(16) uint8_t emptyInner[16 * 2] = {};
        FakeTArrayField liveMc{ reinterpret_cast<uintptr_t>(emptyInner), 2, 2 };
        auto mc0 = Ubel::ReadMulticastDelegateArrayElements(
            reinterpret_cast<uintptr_t>(&liveMc), 0, 16, 0, 8);
        bool emptyKept = mc0.elements.size() == 2;
        for (const auto& e : mc0.elements) emptyKept = emptyKept && e.value == "(0 bindings)";
        check("D3 ⭐ control: a readable EMPTY delegate still says \"(0 bindings)\"",
              emptyKept, mc0.elements.empty() ? "(none)" : mc0.elements[0].value.c_str());
    }


    {   blk("W3-BATCH-METHOD review -- an unreadable Script buffer is not \"bytecode\"");
        // WalkFunctionPropertyRefs tagged a function "bytecode" BEFORE reading its Script buffer and
        // returned empty refs when that read failed -- which the UI's batch rendered as a real "0"
        // ("analysed, touches no class fields") for a function nobody scanned. Review of 0de62ec1.
        alignas(16) static uint8_t fn[0x400] = {};
        const uintptr_t fnAddr = reinterpret_cast<uintptr_t>(fn);
        *reinterpret_cast<uintptr_t*>(fn + DynOff::USTRUCT_SCRIPT)      = 0x1000;   // never mapped
        *reinterpret_cast<int32_t*>(fn + DynOff::USTRUCT_SCRIPT + 0x08) = 16;       // a plausible Num
        auto dead = Aura::WalkFunctionPropertyRefs(fnAddr);
        check("W3-BATCH-METHOD ⭐: an unreadable Script buffer is tagged bytecode_unreadable, not bytecode",
              dead.method == "bytecode_unreadable", dead.method.c_str());
        check("...with no refs", dead.refs.empty());

        // ⭐ The control: a READABLE Script with no property opcode is genuinely analysed, and stays
        // "bytecode" -- otherwise the fix would have relabelled every empty scan as unread.
        alignas(16) static uint8_t script[16];
        memset(script, 0x0B, sizeof(script));   // 0x0B is none of the anchor opcodes
        *reinterpret_cast<uintptr_t*>(fn + DynOff::USTRUCT_SCRIPT) = reinterpret_cast<uintptr_t>(script);
        auto live = Aura::WalkFunctionPropertyRefs(fnAddr);
        check("W3-BATCH-METHOD control: a readable Script is still bytecode",
              live.method == "bytecode", live.method.c_str());
    }


    {   blk("W5-STRARRAY-ELEMENTS -- a string array's elements are read and decoded");
        // IsScalarArrayType admits no string type and no other phase read one, so the walk sent a
        // TArray<FString> with its count and NO elements. ⭐ The index is encoded in each string, so a
        // stride drift reads the wrong text rather than merely "a" text.
        struct FakeFString { uintptr_t Data; int32_t Num; int32_t Max; };
        struct FakeTArray  { uintptr_t Data; int32_t Num; int32_t Max; };
        static_assert(sizeof(FakeFString) == 16, "an FString header is 16 bytes on x64");

        static const wchar_t* kWide[3] = { L"alpha0", L"beta1", L"gamma2" };
        static FakeFString wide[3];
        for (int i = 0; i < 3; ++i) {
            const int32_t n = static_cast<int32_t>(wcslen(kWide[i])) + 1;   // UE counts the terminator
            wide[i] = { reinterpret_cast<uintptr_t>(kWide[i]), n, n };
        }
        static FakeTArray wArr{ reinterpret_cast<uintptr_t>(wide), 3, 3 };
        const uintptr_t wAddr = reinterpret_cast<uintptr_t>(&wArr);

        auto ws = Ubel::ReadStringArrayElements(wAddr, 0, "StrProperty", 16, 0, 64);
        check("STRARRAY: a TArray<FString> is read", ws.ok && ws.elements.size() == 3,
              std::to_string(ws.elements.size()).c_str());
        const bool wideRight = ws.elements.size() == 3 && ws.elements[0].value == "alpha0"
                            && ws.elements[1].value == "beta1" && ws.elements[2].value == "gamma2";
        check("STRARRAY ⭐: each FString element decodes at its own 16-byte stride (UTF-16 -> UTF-8)",
              wideRight, ws.elements.empty() ? "(none)" : ws.elements[1].value.c_str());
        check("STRARRAY: ...with its index", ws.elements.size() == 3 && ws.elements[2].index == 2);

        // A garbage element size (a bad FPROPERTY_ELEMSIZE read) must not move the stride.
        auto wg = Ubel::ReadStringArrayElements(wAddr, 0, "StrProperty", 524808, 0, 64);
        check("STRARRAY ⭐: the stride is the header's, not a garbage element size",
              wg.elements.size() == 3 && wg.elements[2].value == "gamma2",
              wg.elements.size() == 3 ? wg.elements[2].value.c_str() : "(count)");

        // The limit caps, and the total is still the true count.
        auto wl = Ubel::ReadStringArrayElements(wAddr, 0, "StrProperty", 16, 0, 2);
        check("STRARRAY: the limit caps the read, and the total stays the true count",
              wl.elements.size() == 2 && wl.totalCount == 3);

        static const char* kNarrow[2] = { "delta0", "eps1" };
        static FakeFString narrow[2] = {
            { reinterpret_cast<uintptr_t>(kNarrow[0]), 7, 7 },
            { reinterpret_cast<uintptr_t>(kNarrow[1]), 5, 5 },
        };
        static FakeTArray nArr{ reinterpret_cast<uintptr_t>(narrow), 2, 2 };
        for (const char* t : { "Utf8StrProperty", "AnsiStrProperty" }) {
            auto ns = Ubel::ReadStringArrayElements(reinterpret_cast<uintptr_t>(&nArr), 0, t, 16, 0, 64);
            check((std::string("STRARRAY ⭐: a TArray of ") + t + " decodes its 1-byte text").c_str(),
                  ns.elements.size() == 2 && ns.elements[0].value == "delta0" && ns.elements[1].value == "eps1",
                  ns.elements.size() == 2 ? ns.elements[1].value.c_str() : "(count)");
        }

        // Headers that cannot be read are UNREAD, not empty strings.
        static FakeTArray dead{ 0x1000, 2, 2 };   // 0x1000 is never mapped
        auto ds = Ubel::ReadStringArrayElements(reinterpret_cast<uintptr_t>(&dead), 0, "StrProperty", 16, 0, 64);
        bool deadHonest = ds.elements.size() == 2;
        for (const auto& e : ds.elements) deadHonest = deadHonest && e.value == "???";
        check("STRARRAY ⭐: an unreadable element header renders \"???\", not \"\"", deadHonest,
              ds.elements.empty() ? "(none)" : ds.elements[0].value.c_str());

        check("STRARRAY: IsStringArrayType admits the whole family",
              Ubel::IsStringArrayType("StrProperty") && Ubel::IsStringArrayType("Utf8StrProperty")
              && Ubel::IsStringArrayType("AnsiStrProperty"));
        check("STRARRAY control: ...and nothing else (FText has no string header to decode here)",
              !Ubel::IsStringArrayType("TextProperty") && !Ubel::IsStringArrayType("NameProperty"));
    }


    {   blk("W3-XREF-CAP -- MergedScanCapHit: is a capped parallel scan's merged result a prefix?");
        // Each worker stops at maxResults, leaving the rest of its index range UNSCANNED, and ConcatTruncate then
        // cuts the merge to maxResults. Neither was published: a capped page read as a complete answer.
        struct CapTR { std::vector<int> items; };
        std::vector<CapTR> fin(2);  fin[0].items.assign(100, 0);  fin[1].items.assign(100, 0);
        check("XREFCAP control: two workers that each finished, exactly at the cap, are NOT capped",
              !Aura::MergedScanCapHit(fin, &CapTR::items, 200));
        std::vector<CapTR> one(2);  one[0].items.assign(200, 0);
        check("XREFCAP ⭐: a worker that reached the cap stopped early -- capped",
              Aura::MergedScanCapHit(one, &CapTR::items, 200));
        std::vector<CapTR> sum(2);  sum[0].items.assign(150, 0);  sum[1].items.assign(100, 0);
        check("XREFCAP ⭐: workers that together passed the cap -- capped",
              Aura::MergedScanCapHit(sum, &CapTR::items, 200));
        std::vector<CapTR> none(3);
        check("XREFCAP control: an empty scan is not capped", !Aura::MergedScanCapHit(none, &CapTR::items, 200));
    }


    {   blk("D2 -- a scan worker that THROWS must not report the run as COMPLETE");
        // Blind-spot sweep, 2026-09-08. ParallelIndexRanges' catch(...) is the
        // terminate-guard (an exception escaping a std::thread callable calls
        // std::terminate in the host game) and has to stay -- but it swallowed the chunk
        // while its comment claimed the outcome was "exactly like a deadline hit". It set
        // no flag, so ScanForValue folded deadlineHit=false and Fern published
        // data["deadline_hit"]=false: a value scan missing a whole index range rendered as
        // a complete one, and the UI's truncation notice never fired.
        ResetCancel();

        // maxThreads=1 is what makes this DETERMINISTIC. ScanThreadCount picks the worker
        // count from the machine, so a test that let it choose could pass by never running
        // the throwing chunk at all -- and a case that cannot fail proves nothing. With 1,
        // chunk 0 runs INLINE on this thread, so the throw is guaranteed.
        int ran = 0;
        auto faulted = Aura::ParallelGObjectsScan<int>(
            1024,
            [&](int& tr, int32_t b, int32_t e, std::atomic<bool>&) {
                (void)tr; (void)b; (void)e;
                ++ran;
                throw std::runtime_error("simulated worker fault");
            },
            /*maxThreads=*/1);

        check("D2: the throwing chunk really ran (else this case proves nothing)", ran == 1,
              std::to_string(ran).c_str());
        check("D2: the fault did not escape - the terminate-guard still holds", true);
        check("D2 * : it is recorded as a worker fault", faulted.workerFaulted);
        check("D2 * : so the run reports INCOMPLETE, not complete", faulted.incomplete());
        check("D2: and it is NOT mislabelled as a deadline - the workers' stop signal is "
              "left alone so siblings keep working", faulted.deadlineHit == false);

        // THE CONTROL. A clean run must still report complete, or "incomplete" would be
        // satisfied by a scanner that always says so.
        ResetCancel();
        int cleanRan = 0;
        auto clean = Aura::ParallelGObjectsScan<int>(
            1024,
            [&](int& tr, int32_t b, int32_t e, std::atomic<bool>&) {
                (void)tr; (void)b; (void)e; ++cleanRan;
            },
            /*maxThreads=*/1);
        check("D2 control: a clean run ran", cleanRan == 1);
        check("D2 * control: a clean run reports COMPLETE", !clean.incomplete());
    }

    // -- BOOLLAYOUT-2026-09-11 -- the bool layout probe, and the struct preview's mask -------
    //
    // [A3-BOOL-NATIVE-NOWRITE] / [A2-STRUCT-PREVIEW-BOOLMASK], the B05 review follow-up. B05's
    // classifier required ByteMask 0xFF for a native bool, but every engine's SetBoolSize writes
    // `ByteMask = true` (0x01) -- so on a real game NO bool classified native, every native-bool
    // edit was refused, and the only test fed the classifier the same wrong tuple it was written
    // from. These drive the PROBE with the bytes a real FBoolProperty presents at
    // FBOOLPROP_FIELDSIZE, not the classifier's arguments. Pure (no name pool), so it sits above
    // the pool-faking tail.
    {
        blk("BOOLLAYOUT - the probe reads SetBoolSize's real bytes; the preview honours each mask");

        const bool savedFProp = DynOff::bUseFProperty;
        DynOff::bUseFProperty = true;
        auto probe = [](uint8_t fs, uint8_t bo, uint8_t bm, uint8_t fm) {
            static uint8_t blob[0x100];
            memset(blob, 0, sizeof(blob));
            uint8_t* b = blob + DynOff::FBOOLPROP_FIELDSIZE;
            b[0] = fs; b[1] = bo; b[2] = bm; b[3] = fm;
            FieldInfo fi{};   // GLOBAL: Ubel.h declares FieldInfo / ClassInfo above `namespace Ubel`
            Ubel::ProbeBoolLayout(reinterpret_cast<uintptr_t>(blob), fi);
            return fi;
        };
        const auto nat = probe(1, 0, 0x01, 0xFF);
        check("BOOLLAYOUT ⭐: SetBoolSize's native bytes {1,0,01,FF} probe NATIVE", nat.boolNative);
        check("BOOLLAYOUT: ...and carry no mask", nat.boolFieldMask == 0);
        const auto packed = probe(1, 0, 0x04, 0x04);
        check("BOOLLAYOUT control: a packed bit probes its own mask, not native",
              !packed.boolNative && packed.boolFieldMask == 0x04);
        const auto miss = probe(0, 0, 0, 0);
        check("BOOLLAYOUT control: all-zero (a missed probe) is neither",
              !miss.boolNative && miss.boolFieldMask == 0);
        const auto allFF = probe(1, 0, 0xFF, 0xFF);
        check("BOOLLAYOUT: {1,0,FF,FF} is not native -- no engine writes it", !allFF.boolNative);
        DynOff::bUseFProperty = savedFProp;

        // The struct preview passes EACH field's own mask; mask 0 (unresolved) reads the byte.
        ClassInfo si{};
        const char* bNames[3] = { "bA", "bB", "bU" };
        const uint8_t bMasks[3] = { 0x01, 0x02, 0x00 };
        for (int k = 0; k < 3; ++k) {
            FieldInfo bf{};
            bf.Name = bNames[k]; bf.TypeName = "BoolProperty"; bf.Offset = 0; bf.Size = 1;
            bf.boolFieldMask = bMasks[k];
            si.Fields.push_back(bf);
        }
        const uint8_t sbuf[1] = { 0x02 };
        const std::string pv = Ubel::InterpretStructByLayout(sbuf, 1, si, 8);
        check("BOOLLAYOUT ⭐: two packed bits in one byte preview separately; mask 0 reads the byte",
              pv == "{bA=false, bB=true, bU=true}", pv.c_str());
    }

    // -- UFUNCTAIL-2026-09-11 -- NumParms / ParmsSize / ReturnValueOffset on UE 4.11-4.17 ----
    //
    // [A2-UFUNC-TAIL-4X]. FunctionFlags is measured per version, but the three fields behind it
    // were read at a flat +4/+6/+8, under a comment calling that "stable across all UE versions".
    // It is not: RE-UE4SS's MemberVariableLayout templates put a uint16 RepOffset first on every
    // version from 4.11 to 4.17 (4.11: FunctionFlags 0x88, RepOffset 0x8C, NumParms 0x8E) and drop
    // it at 4.18 (NumParms 0x8C). So on 4.11-4.17 `parmsSize` was really NumParms, and every
    // invoke buffer sized from it was undersized inside the game. Pure (no name pool), so it sits
    // above the pool-faking tail.
    {
        blk("UFUNCTAIL - the UFunction tail follows the version's RepOffset");

        const uint32_t savedVer = g_cachedUEVersion;
        const bool     savedCpn = DynOff::bCasePreservingName;
        DynOff::bCasePreservingName = false;
        static uint8_t fn[0x100];
        auto put16 = [](uint8_t* p, uint16_t v) { memcpy(p, &v, 2); };
        auto readAt = [&](unsigned ver) {
            g_cachedUEVersion = ver;
            FunctionInfo fi{};   // GLOBAL, like FieldInfo
            Ubel::ReadFuncFlagsAndParams(reinterpret_cast<uintptr_t>(fn), fi);
            return fi;
        };
        const uint32_t flags = 0x00080401;
        char buf[96];

        // 4.15 (4.11-4.17): FunctionFlags 0x88, RepOffset 0x8C, NumParms 0x8E, ParmsSize 0x90, RVO 0x92.
        memset(fn, 0, sizeof(fn));
        memcpy(fn + 0x88, &flags, 4);
        put16(fn + 0x8C, 0x1234);   // RepOffset
        fn[0x8E] = 3;               // NumParms
        put16(fn + 0x90, 0x30);     // ParmsSize
        put16(fn + 0x92, 0x28);     // ReturnValueOffset
        const auto f415 = readAt(415);
        snprintf(buf, sizeof(buf), "numParms %u parmsSize 0x%X rvo 0x%X",
                 f415.numParms, f415.parmsSize, f415.returnValueOffset);
        check("UFUNCTAIL ⭐: 4.15 reads the real ParmsSize (0x30), not NumParms", f415.parmsSize == 0x30, buf);
        check("UFUNCTAIL ⭐: ...the real NumParms (3), not RepOffset's low byte", f415.numParms == 3, buf);
        check("UFUNCTAIL ⭐: ...and the real ReturnValueOffset (0x28)", f415.returnValueOffset == 0x28, buf);

        // 4.18: no RepOffset -- NumParms 0x8C, ParmsSize 0x8E, RVO 0x90. The boundary's other side.
        memset(fn, 0, sizeof(fn));
        memcpy(fn + 0x88, &flags, 4);
        fn[0x8C] = 2;
        put16(fn + 0x8E, 0x18);
        put16(fn + 0x90, 0x10);
        const auto f418 = readAt(418);
        snprintf(buf, sizeof(buf), "numParms %u parmsSize 0x%X rvo 0x%X",
                 f418.numParms, f418.parmsSize, f418.returnValueOffset);
        check("UFUNCTAIL control: 4.18 has no RepOffset and reads as before",
              f418.numParms == 2 && f418.parmsSize == 0x18 && f418.returnValueOffset == 0x10, buf);

        // UE 5.5: FunctionFlags 0xB0 with the tail right behind it. Unchanged.
        memset(fn, 0, sizeof(fn));
        memcpy(fn + 0xB0, &flags, 4);
        fn[0xB4] = 4;
        put16(fn + 0xB6, 0x40);
        put16(fn + 0xB8, 0x38);
        const auto f505 = readAt(505);
        check("UFUNCTAIL control: UE 5.5 reads as before",
              f505.numParms == 4 && f505.parmsSize == 0x40 && f505.returnValueOffset == 0x38);

        // [UE-OVERRIDE-411] review. The version decides where ParmsSize is read, so a wrong one -- an override of 4.17
        // on a 4.18 title, which the override now reaches -- reads the field next door. A K2_SetActorLocation-shaped
        // 4.18 tail: NumParms 5, ParmsSize 0x9A, ReturnValueOffset 0x99 (bTeleport +0x98, the bool return +0x99).
        memset(fn, 0, sizeof(fn));
        memcpy(fn + 0x88, &flags, 4);
        fn[0x8C] = 5;
        put16(fn + 0x8E, 0x9A);
        put16(fn + 0x90, 0x99);
        const auto wrong = readAt(417);
        snprintf(buf, sizeof(buf), "parmsSize 0x%X", wrong.parmsSize);
        check("PEBUF the hazard: a 4.18 tail read as 4.17 takes ReturnValueOffset for ParmsSize",
              wrong.parmsSize == 0x99, buf);

        // The chain WalkFunctions reads does not depend on the tail: each entry carries its own offset and size.
        auto parm = [](int32_t off, int32_t size, bool isReturn) {
            FunctionParam p{};
            p.name = "p"; p.offset = off; p.size = size; p.isParm = true; p.isReturn = isReturn;
            return p;
        };
        FunctionInfo shifted = wrong;
        shifted.params = { parm(0, 12, false), parm(0xC, 1, false), parm(0x10, 0x88, false),
                           parm(0x98, 1, false), parm(0x99, 1, true) };
        snprintf(buf, sizeof(buf), "%u", Ubel::ParamBufferSize(shifted));
        check("PEBUF ⭐: the buffer still covers the return value ProcessEvent writes at +0x99",
              Ubel::ParamBufferSize(shifted) == 0x9A, buf);
        FunctionParam local = parm(0x200, 8, false);   // a Blueprint function's local: in the chain, not CPF_Parm
        local.isParm = false;
        shifted.params.push_back(local);
        snprintf(buf, sizeof(buf), "%u", Ubel::ParamBufferSize(shifted));
        check("PEBUF: a local past the parameters does not grow the buffer", Ubel::ParamBufferSize(shifted) == 0x9A, buf);
        // Both kinds of entry count on their own: a function with no return value (the reverse misread, a 4.15
        // tail read as 4.18, takes NumParms 1 for ParmsSize), and a return entry whose CPF_Parm bit did not read.
        FunctionInfo noReturn{};
        noReturn.parmsSize = 1;
        noReturn.params = { parm(0, 12, false) };
        check("PEBUF ⭐: a function with no return value gets its whole chain (12), not NumParms (1)",
              Ubel::ParamBufferSize(noReturn) == 12, std::to_string(Ubel::ParamBufferSize(noReturn)).c_str());
        FunctionInfo onlyReturn{};
        onlyReturn.parmsSize = 0;
        onlyReturn.params = { parm(0, 12, true) };
        onlyReturn.params[0].isParm = false;
        check("PEBUF: a return entry counts even when its CPF_Parm bit did not read -- ProcessEvent writes it",
              Ubel::ParamBufferSize(onlyReturn) == 12, std::to_string(Ubel::ParamBufferSize(onlyReturn)).c_str());

        // invoke_function holds only ResolveFunctionInfo's tail read, so its form reads the chain at the address.
        // UProperty mode, as on 4.18: Children -> UProperty entries with PropertyFlags / Offset_Internal / ElementSize.
        const bool savedFPropT = DynOff::bUseFProperty;
        DynOff::bUseFProperty = false;
        static uint8_t tailProps[5][0x100];
        memset(tailProps, 0, sizeof(tailProps));
        const int32_t pOff[5]  = { 0, 0xC, 0x10, 0x98, 0x99 };
        const int32_t pSize[5] = { 12, 1, 0x88, 1, 1 };
        auto putP = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        putP(fn, DynOff::USTRUCT_CHILDREN, reinterpret_cast<uintptr_t>(tailProps[0]));
        for (int i = 0; i < 5; ++i) {
            const uint64_t pf = 0x0080 | (i == 4 ? 0x0400 : 0);   // CPF_Parm, and CPF_ReturnParm on the last
            memcpy(tailProps[i] + DynOff::UPROPERTY_FLAGS, &pf, sizeof(pf));
            memcpy(tailProps[i] + DynOff::UPROPERTY_OFFSET, &pOff[i], 4);
            memcpy(tailProps[i] + DynOff::UPROPERTY_ELEMSIZE, &pSize[i], 4);
            if (i < 4) putP(tailProps[i], DynOff::UFIELD_NEXT, reinterpret_cast<uintptr_t>(tailProps[i + 1]));
        }
        const uintptr_t fnAddr = reinterpret_cast<uintptr_t>(fn);
        snprintf(buf, sizeof(buf), "%u", Ubel::ParamBufferSize(fnAddr, wrong.parmsSize));
        check("PEBUF ⭐: read at the address, the chain gives 0x9A over the misread 0x99",
              Ubel::ParamBufferSize(fnAddr, wrong.parmsSize) == 0x9A, buf);
        check("PEBUF control: a correct ParmsSize is kept", Ubel::ParamBufferSize(fnAddr, 0x9A) == 0x9A);
        // One implausible entry makes the shape read give up on the whole chain; the return slot still counts.
        const int32_t bogus = 0x20000;
        memcpy(tailProps[2] + DynOff::UPROPERTY_ELEMSIZE, &bogus, 4);
        snprintf(buf, sizeof(buf), "%u", Ubel::ParamBufferSize(fnAddr, wrong.parmsSize));
        check("PEBUF ⭐: an unreadable chain still gives the return's end (0x9A)",
              Ubel::ParamBufferSize(fnAddr, wrong.parmsSize) == 0x9A, buf);
        memcpy(tailProps[2] + DynOff::UPROPERTY_ELEMSIZE, &pSize[2], 4);
        // No return value at all: only the shape read can answer. One 12-byte parameter, NumParms 1 read as ParmsSize.
        static uint8_t oneParmFn[0x100] = {};
        putP(oneParmFn, DynOff::USTRUCT_CHILDREN, reinterpret_cast<uintptr_t>(tailProps[0]));
        const uintptr_t savedNext0 = *reinterpret_cast<uintptr_t*>(tailProps[0] + DynOff::UFIELD_NEXT);
        putP(tailProps[0], DynOff::UFIELD_NEXT, 0);
        snprintf(buf, sizeof(buf), "%u", Ubel::ParamBufferSize(reinterpret_cast<uintptr_t>(oneParmFn), 1));
        check("PEBUF ⭐: with no return value the chain's own end (12) answers, not NumParms (1)",
              Ubel::ParamBufferSize(reinterpret_cast<uintptr_t>(oneParmFn), 1) == 12, buf);
        putP(tailProps[0], DynOff::UFIELD_NEXT, savedNext0);
        static uint8_t noChain[0x100] = {};
        check("PEBUF control: a function with no chain keeps its ParmsSize",
              Ubel::ParamBufferSize(reinterpret_cast<uintptr_t>(noChain), 0x20) == 0x20);
        DynOff::bUseFProperty = savedFPropT;

        g_cachedUEVersion           = savedVer;
        DynOff::bCasePreservingName = savedCpn;
    }

    // -- GROUPREFINE-2026-09-11 -- a group refine keeps a width whose every value satisfies ----
    //
    // [W2-ORDEN-FINDENTRY]. RefineGroupCandidates looked each targeted slot up with Find(), which
    // hides audit #5 AB4's AlwaysTrue verdict, so a `Bigger -5` refine pruned every unsigned leaf
    // -- and with it every candidate whose slot needed one. The single-value refine has used
    // FindEntry since AB4. Pure: the leaves are this block's own statics, read through the same
    // SEH-safe path as game memory, so it sits above the pool-faking tail.
    {
        blk("GROUPREFINE - a group refine honours the AlwaysTrue verdict");
        using ST = Radar::ScanType;
        static uint16_t leafU16 = 3;
        static int16_t  leafI16 = 5;
        static int32_t  leafI32 = 24;
        std::vector<Radar::FieldDescriptor> descs(3);
        descs[0].fieldType = "UInt16Property"; descs[0].fieldName = "Counter";
        descs[1].fieldType = "IntProperty";    descs[1].fieldName = "Level";
        descs[2].fieldType = "Int16Property";  descs[2].fieldName = "Small";
        std::vector<Radar::InstanceRecord> insts(1);
        insts[0].instanceAddr = 0x1000;
        insts[0].instanceName = "Fake";
        auto match = [](uint32_t desc, const void* at) {
            Radar::GroupSlotMatch m;
            m.descriptorIdx = desc;
            m.leafAddr      = reinterpret_cast<uintptr_t>(at);
            return m;
        };
        auto slot = [](ST st, const char* v) {
            Radar::SlotSpec sp;
            sp.st    = st;
            sp.value = v;
            Radar::BuildNumericTargets(sp.dt, v, sp.targets, sp.roundMode, st);   // as Fern does
            return sp;
        };
        char buf[96];

        // Slot 0 holds the UInt16 leaf and the Int32 one; slot 1 (Exact 24) needs the Int32 one.
        const std::vector<Radar::SlotSpec> slots = { slot(ST::Bigger, "-5"), slot(ST::Exact, "24") };
        std::vector<Radar::GroupCandidate> cands(1);
        cands[0].slotMatches = { { match(0, &leafU16), match(1, &leafI32) }, { match(1, &leafI32) } };
        Aura::RefineGroupCandidates(slots, cands, descs, insts);
        snprintf(buf, sizeof(buf), "candidates %zu, slot0 leaves %zu", cands.size(),
                 cands.empty() ? size_t{0} : cands[0].slotMatches[0].size());
        check("GROUPREFINE ⭐: Refine(Bigger -5) keeps the candidate", cands.size() == 1, buf);
        check("GROUPREFINE ⭐: ...and its unsigned leaf (3 > -5)",
              cands.size() == 1 && cands[0].slotMatches[0].size() == 2, buf);

        // Control: nothing 16-bit exceeds 70000, so an Int16-only slot empties and the candidate goes.
        const std::vector<Radar::SlotSpec> slots2 = { slot(ST::Bigger, "70000"), slot(ST::Exact, "24") };
        std::vector<Radar::GroupCandidate> c2(1);
        c2[0].slotMatches = { { match(2, &leafI16) }, { match(1, &leafI32) } };
        Aura::RefineGroupCandidates(slots2, c2, descs, insts);
        check("GROUPREFINE control: Refine(Bigger 70000) still drops an Int16-only slot", c2.empty());

        // (review of aaf6a022) A refine that compared an AlwaysTrue entry's ZEROED bytes (the raw
        // overload) keeps the 3 above -- 3 > 0 -- so these two pin the verdict itself: an unsigned 0
        // under Bigger -5, and an Int16 32767 under Smaller 70000.
        static uint16_t zeroU16 = 0;
        static int16_t  maxI16  = 32767;
        const std::vector<Radar::SlotSpec> slots3 = { slot(ST::Bigger, "-5"), slot(ST::Exact, "24") };
        std::vector<Radar::GroupCandidate> c3(1);
        c3[0].slotMatches = { { match(0, &zeroU16) }, { match(1, &leafI32) } };
        Aura::RefineGroupCandidates(slots3, c3, descs, insts);
        check("GROUPREFINE ⭐: an unsigned 0 survives Refine(Bigger -5) -- the verdict, not a zeroed target",
              c3.size() == 1);
        const std::vector<Radar::SlotSpec> slots4 = { slot(ST::Smaller, "70000"), slot(ST::Exact, "24") };
        std::vector<Radar::GroupCandidate> c4(1);
        c4[0].slotMatches = { { match(2, &maxI16) }, { match(1, &leafI32) } };
        Aura::RefineGroupCandidates(slots4, c4, descs, insts);
        check("GROUPREFINE ⭐: an Int16 32767 survives Refine(Smaller 70000)", c4.size() == 1);
    }

    // -- TMAPGEOM-2026-09-09 -- a faulted FStructProperty::Struct must REFUSE ----------
    //
    // ⛔ MUST STAY IN THE POOL-FAKING TAIL OF THIS FUNCTION, with IFACEREAD and
    // UNREADVAL, BOOLNATIVE, UFUNCWALK, OPTLAYOUT, PROBECLASS, UFIELDNEXT, FNAMEMEASURE, ENUMU8, FNAMENUMBER, FNAMESIZE, CALLFOLLOW, SOFTPATH, COMPACTSET, STATICGOBJ, WEAKLABEL and the LIVEFUNCS-STEP2 name / layout blocks below it and NOTHING ELSE after any of them. It calls Serie::InitUE4,
    // and Serie's pool state (s_poolAddr / s_isUE4Mode / s_initialized) lives in
    // file-statics that no header exposes -- so it CANNOT be restored. Anything appended
    // after this block would run against a fake UE4 name pool and could pass or fail for
    // that reason. IFACEREAD, UNREADVAL, BOOLNATIVE, UFUNCWALK, OPTLAYOUT, PROBECLASS, UFIELDNEXT, FNAMEMEASURE, ENUMU8, FNAMENUMBER, FNAMESIZE, CALLFOLLOW, SOFTPATH, COMPACTSET, STATICGOBJ, WEAKLABEL and the LIVEFUNCS-STEP2 name / layout blocks are the legal exceptions: each installs its OWN
    // pool first and depends on nothing the block above it leaves behind.
    //
    // THE DEFECT. `GetMapPairLayout` dropped both `FStructProperty::Struct` reads. On a
    // faulted read the addr stays 0, `ResolveElementAlignment` is then asked to align a
    // struct it cannot see, and whatever it guesses flows into pairAlign -> pairStride --
    // while the comment two lines under that block says out loud that "the stride must be a
    // multiple of it, or every element after index 0 lands at a wrong address". The sweep
    // filed this site as "the same question unanswered" and never answered it.
    //
    // ⭐ WHY THIS NEEDS A PAGE BOUNDARY AND NOT A DEAD POINTER. The other fault fixtures in
    // this file point a Data pointer at 0x1000, so the whole target is unreadable. That does
    // NOT reach this arm: a wholly-unreadable property makes GetFieldTypeName return
    // "Unknown" and the function returns false long before the struct read -- the same
    // outcome as the fix, for a different reason. The repair only matters for a PARTIAL
    // failure: the property readable at +0x08 and +0x3C, unreadable at +0x78. That is
    // manufactured here by committing ONE page of a two-page reservation and laying the
    // property across the edge.
    {
        blk("TMAPGEOM - a faulted FStructProperty::Struct refuses, it does not guess a stride");

        // 1. A fake UE4 name pool, so GetFieldTypeName can answer "StructProperty".
        //    Chunks[0] -> chunk -> entry, string at entry + 0x10.
        static uint8_t entryBlob[0x40] = {};
        memcpy(entryBlob + 0x10, "StructProperty", sizeof("StructProperty"));
        static uintptr_t chunk[4] = {};
        chunk[1] = reinterpret_cast<uintptr_t>(entryBlob);   // comparison index 1
        static uintptr_t chunks[2] = { reinterpret_cast<uintptr_t>(chunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(chunks), 0x10);
        check("TMAPGEOM setup: the fake pool resolves index 1",
              Serie::GetString(1) == "StructProperty", Serie::GetString(1).c_str());

        // 2. A fake FFieldClass whose Name is that index.
        static uint8_t fclass[0x20] = {};
        *reinterpret_cast<int32_t*>(fclass + DynOff::FFIELDCLASS_NAME) = 1;

        // 3. Two pages RESERVED, one COMMITTED. A read that crosses the edge faults.
        uint8_t* page = static_cast<uint8_t*>(
            VirtualAlloc(nullptr, 0x2000, MEM_RESERVE, PAGE_READWRITE));
        check("TMAPGEOM setup: reserved two pages", page != nullptr);
        if (!page) { printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
                     return g_fail == 0 ? 0 : 1; }
        VirtualAlloc(page, 0x1000, MEM_COMMIT, PAGE_READWRITE);

        auto makeProp = [&](uint8_t* at) {
            memset(at, 0, 0x40);
            *reinterpret_cast<uintptr_t*>(at + DynOff::FFIELD_CLASS) =
                reinterpret_cast<uintptr_t>(fclass);
            // ELEMSIZE is what ResolveInnerSize uses FIRST, so it returns before ever
            // touching FSTRUCTPROP_STRUCT -- which is why the ONLY faulting read in the
            // straddled case is the one under test.
            *reinterpret_cast<int32_t*>(at + DynOff::FPROPERTY_ELEMSIZE) = 12;
            return reinterpret_cast<uintptr_t>(at);
        };

        auto makeMap = [&](std::vector<uint8_t>& blob, uintptr_t prop) {
            *reinterpret_cast<uintptr_t*>(blob.data() + DynOff::FARRAYPROP_INNER)     = prop;
            *reinterpret_cast<uintptr_t*>(blob.data() + DynOff::FARRAYPROP_INNER + 8) = prop;
            return reinterpret_cast<uintptr_t>(blob.data());
        };

        // ⭐ THE CONTROL FIRST. The same fake, wholly inside the committed page, MUST lay
        // out -- otherwise a refusal below would prove only that the fake is broken.
        std::vector<uint8_t> mapOk(0x100, 0);
        Ubel::MapPairLayout okLayout{};
        const bool okRes = Ubel::GetMapPairLayout(makeMap(mapOk, makeProp(page + 0x100)),
                                                  okLayout);
        check("TMAPGEOM control: a READABLE struct property lays out", okRes);
        check("TMAPGEOM control: and it produced a stride", okLayout.pairStride > 0,
              std::to_string(okLayout.pairStride).c_str());

        // Now the same property laid across the page edge: +0x08 and +0x3C are the last
        // readable bytes, +0x78 is in the uncommitted page.
        uint8_t* edge = page + 0x1000 - 0x40;
        std::vector<uint8_t> mapBad(0x100, 0);
        Ubel::MapPairLayout badLayout{};
        const bool badRes = Ubel::GetMapPairLayout(makeMap(mapBad, makeProp(edge)), badLayout);

        check("TMAPGEOM ⭐: a faulted FStructProperty::Struct REFUSES the layout",
              badRes == false);
        check("TMAPGEOM ⭐: and no stride was published from an unread struct pointer",
              badLayout.pairStride == 0,
              std::to_string(badLayout.pairStride).c_str());
        check("TMAPGEOM: the struct addr really did stay unread",
              badLayout.valueStructAddr == 0);

        VirtualFree(page, 0, MEM_RELEASE);
    }

    // -- IFACEREAD-2026-09-09 -- an UNREADABLE FScriptInterface must REFUSE -----------
    //
    // ⛔ ALSO AFTER Serie::InitUE4, for the reason the TMAPGEOM header gives: it needs a
    // fake UE4 name pool to make GetFieldTypeName answer "InterfaceProperty". It re-inits
    // the pool with its OWN chunks, so it does not depend on TMAPGEOM's leftovers — but
    // nothing that needs the real (uninitialised) pool may be appended after it either.
    //
    // THE DEFECT (slice C of the unadjudicated sweep claims, Ubel.cpp InterfaceProperty).
    // Both FScriptInterface reads were discarded and the hex column was then built
    // unconditionally, so an UNREADABLE interface rendered
    // "0000000000000000 0000000000000000" — an affirmative claim about memory nobody
    // could read, byte-identical to a genuinely NULL interface.
    //
    // ⭐ WHY THE LIVE ARM COULD NOT SETTLE IT, WHICH IS WHY THIS FIXTURE EXISTS. The
    // regression half ran on DumperTest and found GeometryCollectionComponent
    // .CustomRenderer — a real, genuinely null interface — printing exactly those zeros.
    // That shows the fix did not break the readable path, and NOTHING more: readable-null
    // and unreadable are the two states the defect confuses, and a live game hands you
    // only the first. Separating them takes a manufactured fault.
    //
    // ⭐ AND IT MUST BE A PARTIAL ONE. A wholly-unmapped instance never reaches this
    // branch — WalkInstance's IsAddrReadable gate bails first — so a dead-pointer fake
    // would go green for the wrong reason, the same trap TMAPGEOM documents. The arm is
    // built its way: one committed page of a two-page reservation, with the 16-byte
    // FScriptInterface laid ACROSS the edge, so ObjectPointer reads and InterfacePointer
    // faults. That is the case the old code got wrong in its worst form — a REAL pointer
    // followed by eight zero bytes it had never read.
    {
        blk("IFACEREAD - a faulted FScriptInterface refuses, it does not publish zeros");

        // 1. The fake pool: index 1 = the TYPE name, index 2 = the FIELD name.
        static uint8_t ifTypeEntry[0x40] = {};
        static uint8_t ifNameEntry[0x40] = {};
        memcpy(ifTypeEntry + 0x10, "InterfaceProperty", sizeof("InterfaceProperty"));
        memcpy(ifNameEntry + 0x10, "Iface", sizeof("Iface"));
        static uintptr_t ifChunk[4] = {};
        ifChunk[1] = reinterpret_cast<uintptr_t>(ifTypeEntry);
        ifChunk[2] = reinterpret_cast<uintptr_t>(ifNameEntry);
        static uintptr_t ifChunks[2] = { reinterpret_cast<uintptr_t>(ifChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(ifChunks), 0x10);
        check("IFACEREAD setup: the pool resolves the type name",
              Serie::GetString(1) == "InterfaceProperty", Serie::GetString(1).c_str());
        check("IFACEREAD setup: the pool resolves the field name",
              Serie::GetString(2) == "Iface", Serie::GetString(2).c_str());

        // 2. An FFieldClass whose Name is that type index.
        static uint8_t ifFieldClass[0x20] = {};
        *reinterpret_cast<int32_t*>(ifFieldClass + DynOff::FFIELDCLASS_NAME) = 1;

        // 3. Two pages reserved, one committed. The instance sits at the base so the
        //    UObject-header readability gate passes and ONLY the field read can fault.
        uint8_t* ipage = static_cast<uint8_t*>(
            VirtualAlloc(nullptr, 0x2000, MEM_RESERVE, PAGE_READWRITE));
        check("IFACEREAD setup: reserved two pages", ipage != nullptr);
        if (!ipage) { printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
                      return g_fail == 0 ? 0 : 1; }
        VirtualAlloc(ipage, 0x1000, MEM_COMMIT, PAGE_READWRITE);
        const uintptr_t inst = reinterpret_cast<uintptr_t>(ipage);

        // 4. ONE CLASS BLOB PER CASE — not tidiness. s_walkClassCache is keyed by the
        //    class address and nothing erases it, so a shared blob would serve case 1's
        //    memoised field list to cases 2 and 3 (the A10 defect this file demonstrates
        //    further up, hit by accident while writing the SANEPROPS block).
        static uint8_t ifProp[4][0x80] = {};
        static uint8_t ifCls[4][0x100] = {};
        auto makeClass = [&](int i, int32_t fieldOffset) {
            *reinterpret_cast<uintptr_t*>(ifProp[i] + DynOff::FFIELD_CLASS) =
                reinterpret_cast<uintptr_t>(ifFieldClass);
            *reinterpret_cast<int32_t*>(ifProp[i] + DynOff::FFIELD_NAME)         = 2;
            *reinterpret_cast<int32_t*>(ifProp[i] + DynOff::FPROPERTY_OFFSET)    = fieldOffset;
            *reinterpret_cast<int32_t*>(ifProp[i] + DynOff::FPROPERTY_ELEMSIZE)  = 16;
            *reinterpret_cast<int32_t*>(ifProp[i] + DynOff::FPROPERTY_ELEMSIZE - 4) = 1;
            *reinterpret_cast<int32_t*>(ifCls[i] + DynOff::USTRUCT_PROPSSIZE)    = 0x2000;
            *reinterpret_cast<uintptr_t*>(ifCls[i] + DynOff::USTRUCT_CHILDPROPS) =
                reinterpret_cast<uintptr_t>(ifProp[i]);
            return reinterpret_cast<uintptr_t>(ifCls[i]);
        };
        // ⛔ THE ANTI-VACUITY GUARD, and it is not decoration. Every ⭐ assertion below is
        // of the form "hexValue is EMPTY" -- which a walk that produced NO FIELDS AT ALL
        // satisfies for free. A seed that silently failed to take would then read as a
        // green refusal. So the extractor asserts the field arrived before returning it,
        // and every case pays that check.
        auto ifaceField = [&](const char* who,
                              const Ubel::InstanceWalkResult& r) -> Ubel::LiveFieldValue {
            check((std::string("IFACEREAD control: ") + who
                   + " -- the fake class produced exactly one field").c_str(),
                  r.fields.size() == 1, std::to_string(r.fields.size()).c_str());
            for (const auto& f : r.fields)
                if (f.typeName == "InterfaceProperty") return f;
            return Ubel::LiveFieldValue{};
        };

        // ⭐ THE CONTROL FIRST, so a refusal below cannot be satisfied by a broken fake.
        *reinterpret_cast<uintptr_t*>(ipage + 0x100) = 0x1122334455667788ull;
        *reinterpret_cast<uintptr_t*>(ipage + 0x108) = 0x99AABBCCDDEEFF00ull;
        const auto okF = ifaceField("bound",
            Ubel::WalkInstance(inst, makeClass(0, 0x100), 64, 2, false));
        check("IFACEREAD control: the fake class produced the interface field",
              okF.name == "Iface", okF.name.c_str());
        check("IFACEREAD control: a READABLE interface still publishes both halves",
              okF.hexValue == "1122334455667788 99AABBCCDDEEFF00", okF.hexValue.c_str());
        check("IFACEREAD control: and carries no refusal string",
              okF.typedValue.find("unreadable") == std::string::npos,
              okF.typedValue.c_str());

        // ⭐ THE READABLE-NULL CONTROL -- the state the LIVE arm actually found, and the
        // one the whole defect is about. GeometryCollectionComponent.CustomRenderer on
        // DumperTest is a genuinely null interface, so it printed sixteen zero bytes; the
        // pre-fix walker printed the SAME sixteen zero bytes for memory it could not read.
        // A fix that suppressed the hex on both would "pass" every refusal check above
        // while destroying real information, so this case must keep its zeros.
        const auto nullF = ifaceField("readable-null",
            Ubel::WalkInstance(inst, makeClass(3, 0x200), 64, 2, false));
        check("IFACEREAD control: a readable NULL interface still reports its zeros",
              nullF.hexValue == "0000000000000000 0000000000000000", nullF.hexValue.c_str());
        check("IFACEREAD control: ...and is NOT slandered as unreadable",
              nullF.typedValue.find("unreadable") == std::string::npos,
              nullF.typedValue.c_str());

        // ⭐ THE PARTIAL FAULT. ObjectPointer is the page's last 8 bytes; InterfacePointer
        // is in the uncommitted page. Old behaviour: "1122334455667788 0000000000000000".
        *reinterpret_cast<uintptr_t*>(ipage + 0xFF8) = 0x1122334455667788ull;
        const auto edgeF = ifaceField("half-unread",
            Ubel::WalkInstance(inst, makeClass(1, 0x0FF8), 64, 2, false));
        check("IFACEREAD ⭐: a HALF-readable FScriptInterface publishes NO hex",
              edgeF.hexValue.empty(), edgeF.hexValue.c_str());
        check("IFACEREAD ⭐: and says so, naming the offset in the base it prints",
              edgeF.typedValue.find("unreadable at +0xFF8") != std::string::npos,
              edgeF.typedValue.c_str());

        // The wholly-unreadable field, one page further out. Same refusal.
        const auto deadF = ifaceField("both-unread",
            Ubel::WalkInstance(inst, makeClass(2, 0x1000), 64, 2, false));
        check("IFACEREAD: a wholly unreadable FScriptInterface refuses too",
              deadF.hexValue.empty(), deadF.hexValue.c_str());
        check("IFACEREAD: ...and names ITS offset, not the previous case's",
              deadF.typedValue.find("unreadable at +0x1000") != std::string::npos,
              deadF.typedValue.c_str());

        // ⭐⭐ THE DISCRIMINATOR, and the single check that states the defect itself.
        // Pre-fix, these two rendered BYTE-IDENTICALLY: a genuinely null interface and
        // memory nobody could read both came out "0000000000000000 0000000000000000".
        // Every other check here can be satisfied by some partial fix; this one cannot,
        // because it asserts the two states are now TELLABLE APART -- which is the entire
        // user-visible claim, and precisely what the live DumperTest run could not decide.
        check("IFACEREAD ⭐⭐: readable-NULL and UNREADABLE are no longer the same output",
              nullF.hexValue != deadF.hexValue,
              (nullF.hexValue + " vs " + deadF.hexValue).c_str());

        VirtualFree(ipage, 0, MEM_RELEASE);
    }

    // -- UNREADVAL-2026-09-09 -- the OTHER handlers that publish 0 as a VALUE ----------
    //
    // ⛔ ALSO IN THE POOL-FAKING TAIL, and for the third time the same reason: these three
    // handlers are selected by `fi.TypeName`, so the walker must be able to answer
    // "EnumProperty" / "ByteProperty" / "OptionalProperty" out of a fake pool. Own chunks,
    // like IFACEREAD; nothing needing the real pool may follow.
    //
    // THE DEFECT, and it is IFACEREAD's exactly, in three more places. Slice C of the
    // unadjudicated sweep claims ranked `Ubel.cpp` OptionalProperty's isObjectLike read as
    // PUBLISHED-tier; reading it found the same discarded-read shape in the two enum
    // handlers beside it. On a faulted read the out-param keeps its initialised 0, and 0
    // is never "unknown" downstream:
    //   * EnumProperty  -> hexValue "00" and typedValue "0" (or, with a UEnum, the NAME of
    //                      enumerator 0) for a byte nobody could read;
    //   * ByteProperty  -> the same, one handler down;
    //   * TOptional     -> "(unset)", an AFFIRMATIVE claim that the option provably holds
    //                      no value -- the identical claim "(unbound)" made for delegates
    //                      in D3/D5. And its FString/FName arms fail the OTHER way: their
    //                      sentinels are -1 / 0xFFFFFFFF, so a faulted 0 reads as SET.
    // `BoolProperty`, three blocks above the enum handlers, has always had it right.
    //
    // ⭐ THE DISCRIMINATOR IS AGAIN WHAT MATTERS. Every refusal check below can be passed
    // by a fix that simply blanks the field; what cannot is "a byte that genuinely holds 0
    // still reports 0, and is no longer the same output as a byte we could not read". Each
    // family therefore gets a readable-ZERO case as well as a readable-nonzero one.
    {
        blk("UNREADVAL - a faulted enum / byte-enum / TOptional refuses, it does not publish 0");

        // 1. The pool. 1..6 are the type names, the field name, and the UEnum's class name.
        static uint8_t uvEntry[7][0x40] = {};
        const char* uvNames[7] = { "", "EnumProperty", "Val", "ByteProperty",
                                   "OptionalProperty", "Enum", "ObjectProperty" };
        static uintptr_t uvChunk[8] = {};
        for (int i = 1; i <= 6; ++i) {
            memcpy(uvEntry[i] + 0x10, uvNames[i], strlen(uvNames[i]) + 1);
            uvChunk[i] = reinterpret_cast<uintptr_t>(uvEntry[i]);
        }
        static uintptr_t uvChunks[2] = { reinterpret_cast<uintptr_t>(uvChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(uvChunks), 0x10);
        check("UNREADVAL setup: the pool resolves EnumProperty",
              Serie::GetString(1) == "EnumProperty", Serie::GetString(1).c_str());
        check("UNREADVAL setup: the pool resolves OptionalProperty",
              Serie::GetString(4) == "OptionalProperty", Serie::GetString(4).c_str());

        // 2. One FFieldClass per type name.
        static uint8_t uvFC[7][0x20] = {};
        auto fieldClassFor = [&](int nameIdx) {
            *reinterpret_cast<int32_t*>(uvFC[nameIdx] + DynOff::FFIELDCLASS_NAME) = nameIdx;
            return reinterpret_cast<uintptr_t>(uvFC[nameIdx]);
        };

        // 3. A UEnum the ByteProperty handler will ACCEPT: its UClass must be named "Enum".
        //    Without this the handler falls through to the generic scalar path and the
        //    ByteProperty cases below would measure nothing -- an anti-vacuity concern in
        //    its own right, which is why the control asserts it resolved.
        static uint8_t uvEnumCls[0x40] = {};
        static uint8_t uvEnumObj[0x40] = {};
        *reinterpret_cast<int32_t*>(uvEnumCls + Grimoire::OFF_UOBJECT_NAME) = 5;   // "Enum"
        *reinterpret_cast<uintptr_t*>(uvEnumObj + Grimoire::OFF_UOBJECT_CLASS) =
            reinterpret_cast<uintptr_t>(uvEnumCls);

        // 4. The inner ObjectProperty a TOptional wraps (ProbeInnerProperty finds it at
        //    FARRAYPROP_INNER -- TOptional and TArray are both FProperty + FProperty*).
        static uint8_t uvInner[0x100] = {};
        *reinterpret_cast<uintptr_t*>(uvInner + DynOff::FFIELD_CLASS) = fieldClassFor(6);
        *reinterpret_cast<int32_t*>(uvInner + DynOff::FFIELD_NAME) = 2;

        // 5. Two pages reserved, one committed -- the IFACEREAD manufacture. A wholly dead
        //    instance bails at WalkInstance's readability gate for a DIFFERENT reason, so
        //    only a partial fault reaches these handlers.
        uint8_t* upage = static_cast<uint8_t*>(
            VirtualAlloc(nullptr, 0x2000, MEM_RESERVE, PAGE_READWRITE));
        check("UNREADVAL setup: reserved two pages", upage != nullptr);
        if (!upage) { printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
                      return g_fail == 0 ? 0 : 1; }
        VirtualAlloc(upage, 0x1000, MEM_COMMIT, PAGE_READWRITE);
        const uintptr_t uinst = reinterpret_cast<uintptr_t>(upage);

        // 6. ONE CLASS BLOB PER CASE. s_walkClassCache is keyed by class address and
        //    nothing erases it; a shared blob serves case 1's memoised fields to case 2.
        static uint8_t uvProp[12][0x100] = {};   // one blob per case -- keep >= the number of makeCase calls
        static uint8_t uvCls[12][0x100] = {};
        int uvNext = 0;
        auto makeCase = [&](int typeIdx, int32_t fieldOffset, int32_t elemSize,
                            uintptr_t enumPtr, uintptr_t innerPtr) {
            const int i = uvNext++;
            *reinterpret_cast<uintptr_t*>(uvProp[i] + DynOff::FFIELD_CLASS) =
                fieldClassFor(typeIdx);
            *reinterpret_cast<int32_t*>(uvProp[i] + DynOff::FFIELD_NAME)        = 2;
            *reinterpret_cast<int32_t*>(uvProp[i] + DynOff::FPROPERTY_OFFSET)   = fieldOffset;
            *reinterpret_cast<int32_t*>(uvProp[i] + DynOff::FPROPERTY_ELEMSIZE) = elemSize;
            *reinterpret_cast<int32_t*>(uvProp[i] + DynOff::FPROPERTY_ELEMSIZE - 4) = 1;
            if (enumPtr) {
                *reinterpret_cast<uintptr_t*>(uvProp[i] + DynOff::FENUMPROP_ENUM) = enumPtr;
                *reinterpret_cast<uintptr_t*>(uvProp[i] + DynOff::FBYTEPROP_ENUM) = enumPtr;
            }
            if (innerPtr)
                *reinterpret_cast<uintptr_t*>(uvProp[i] + DynOff::FARRAYPROP_INNER) = innerPtr;
            *reinterpret_cast<int32_t*>(uvCls[i] + DynOff::USTRUCT_PROPSSIZE)    = 0x2000;
            *reinterpret_cast<uintptr_t*>(uvCls[i] + DynOff::USTRUCT_CHILDPROPS) =
                reinterpret_cast<uintptr_t>(uvProp[i]);
            return reinterpret_cast<uintptr_t>(uvCls[i]);
        };
        // ⛔ THE ANTI-VACUITY GUARD, same as IFACEREAD's and needed for the same reason:
        //    every ⭐ assertion is "hexValue is EMPTY" or "typedValue says unreadable", and
        //    a walk that produced NO FIELDS satisfies the first for free.
        auto oneField = [&](const char* who, const char* wantType,
                            const Ubel::InstanceWalkResult& r) -> Ubel::LiveFieldValue {
            check((std::string("UNREADVAL control: ") + who
                   + " -- the fake class produced exactly one field").c_str(),
                  r.fields.size() == 1, std::to_string(r.fields.size()).c_str());
            for (const auto& f : r.fields)
                if (f.typeName == wantType) return f;
            return Ubel::LiveFieldValue{};
        };

        // ---- EnumProperty ------------------------------------------------------------
        upage[0x100] = 0x07;
        const auto enSeven = oneField("enum readable", "EnumProperty",
            Ubel::WalkInstance(uinst, makeCase(1, 0x100, 1, 0, 0), 64, 2, false));
        check("UNREADVAL control: a READABLE enum byte still publishes its value",
              enSeven.typedValue == "7", enSeven.typedValue.c_str());
        check("UNREADVAL control: ...and its hex",
              enSeven.hexValue == "07", enSeven.hexValue.c_str());

        // The readable ZERO -- what the refusal must not look like.
        const auto enZero = oneField("enum readable-zero", "EnumProperty",
            Ubel::WalkInstance(uinst, makeCase(1, 0x200, 1, 0, 0), 64, 2, false));
        check("UNREADVAL control: a byte that genuinely holds 0 still reports 0",
              enZero.typedValue == "0" && enZero.hexValue == "00",
              (enZero.typedValue + " / " + enZero.hexValue).c_str());

        const auto enDead = oneField("enum unreadable", "EnumProperty",
            Ubel::WalkInstance(uinst, makeCase(1, 0x1000, 1, 0, 0), 64, 2, false));
        check("UNREADVAL ⭐: an UNREADABLE enum byte publishes NO hex",
              enDead.hexValue.empty(), enDead.hexValue.c_str());
        check("UNREADVAL ⭐: ...and says so, naming its own offset",
              enDead.typedValue.find("unreadable at +0x1000") != std::string::npos,
              enDead.typedValue.c_str());
        check("UNREADVAL ⭐⭐: a real 0 and an unreadable byte are no longer the same value",
              enZero.typedValue != enDead.typedValue,
              (enZero.typedValue + " vs " + enDead.typedValue).c_str());

        // ---- ByteProperty with a UEnum -----------------------------------------------
        const uintptr_t uvEnum = reinterpret_cast<uintptr_t>(uvEnumObj);
        upage[0x300] = 0x05;
        const auto byFive = oneField("byte-enum readable", "ByteProperty",
            Ubel::WalkInstance(uinst, makeCase(3, 0x300, 1, uvEnum, 0), 64, 2, false));
        // ⛔ NOT DECORATION: if the fake UEnum failed to validate, the handler falls
        //    through to the generic scalar path and every ByteProperty check below would
        //    be measuring that path instead. enumAddr is set ONLY inside the enum arm.
        check("UNREADVAL control: the fake UEnum validated, so the enum arm really ran",
              byFive.enumAddr == uvEnum, byFive.typedValue.c_str());
        check("UNREADVAL control: a READABLE byte-enum publishes its value and hex",
              byFive.typedValue == "5" && byFive.hexValue == "05",
              (byFive.typedValue + " / " + byFive.hexValue).c_str());

        const auto byZero = oneField("byte-enum readable-zero", "ByteProperty",
            Ubel::WalkInstance(uinst, makeCase(3, 0x400, 1, uvEnum, 0), 64, 2, false));
        check("UNREADVAL control: a byte-enum that genuinely holds 0 still reports 0",
              byZero.typedValue == "0" && byZero.hexValue == "00",
              (byZero.typedValue + " / " + byZero.hexValue).c_str());

        const auto byDead = oneField("byte-enum unreadable", "ByteProperty",
            Ubel::WalkInstance(uinst, makeCase(3, 0x1000, 1, uvEnum, 0), 64, 2, false));
        check("UNREADVAL ⭐: an UNREADABLE byte-enum publishes NO hex",
              byDead.hexValue.empty(), byDead.hexValue.c_str());
        check("UNREADVAL ⭐: ...and says so",
              byDead.typedValue.find("unreadable at +0x1000") != std::string::npos,
              byDead.typedValue.c_str());
        check("UNREADVAL ⭐: ...while KEEPING the UEnum metadata, which came from the FField",
              byDead.enumAddr == uvEnum, "enumAddr lost");
        check("UNREADVAL ⭐⭐: a real 0 and an unreadable byte-enum differ",
              byZero.typedValue != byDead.typedValue,
              (byZero.typedValue + " vs " + byDead.typedValue).c_str());

        // ---- TOptional<UObject*> -----------------------------------------------------
        const uintptr_t uvInnerAddr = reinterpret_cast<uintptr_t>(uvInner);
        *reinterpret_cast<uintptr_t*>(upage + 0x500) = reinterpret_cast<uintptr_t>(uvEnumObj);
        const auto opSet = oneField("optional set", "OptionalProperty",
            Ubel::WalkInstance(uinst, makeCase(4, 0x500, 8, 0, uvInnerAddr), 64, 2, false));
        check("UNREADVAL control: a SET TOptional does not read as unset",
              opSet.typedValue != "(unset)"
              && opSet.typedValue.find("unreadable") == std::string::npos,
              opSet.typedValue.c_str());

        // ⭐ The readable NULL -- "(unset)" is CORRECT here, and must stay.
        const auto opNull = oneField("optional readable-null", "OptionalProperty",
            Ubel::WalkInstance(uinst, makeCase(4, 0x600, 8, 0, uvInnerAddr), 64, 2, false));
        check("UNREADVAL control: a readable NULL TOptional is still (unset)",
              opNull.typedValue == "(unset)", opNull.typedValue.c_str());

        const auto opDead = oneField("optional unreadable", "OptionalProperty",
            Ubel::WalkInstance(uinst, makeCase(4, 0x1000, 8, 0, uvInnerAddr), 64, 2, false));
        check("UNREADVAL ⭐: an UNREADABLE TOptional refuses instead of claiming (unset)",
              opDead.typedValue.find("unreadable at +0x1000") != std::string::npos,
              opDead.typedValue.c_str());
        check("UNREADVAL ⭐⭐: readable-null and unreadable are no longer the same claim",
              opNull.typedValue != opDead.typedValue,
              (opNull.typedValue + " vs " + opDead.typedValue).c_str());

        // Review of cc430176: the cases above are the INTRUSIVE shape (an 8-byte optional of an
        // 8-byte object). Real object optionals are TRAILING-FLAG (16 bytes); their discriminator is
        // the flag byte at +8. Here the pointer is readable and the flag sits across the page edge.
        const auto opFlagDead = oneField("trailing-flag optional, flag unreadable", "OptionalProperty",
            Ubel::WalkInstance(uinst, makeCase(4, 0x0FF8, 16, 0, uvInnerAddr), 64, 2, false));
        check("UNREADVAL: a trailing-flag optional whose FLAG is unreadable refuses, not (unset)",
              opFlagDead.typedValue.find("unreadable") != std::string::npos && opFlagDead.typedValue != "(unset)",
              opFlagDead.typedValue.c_str());

        VirtualFree(upage, 0, MEM_RELEASE);
    }

    // -- BOOLNATIVE-2026-09-11 -- WalkInstance publishes a native bool as native -------------
    //
    // ⛔ POOL-FAKING, like IFACEREAD and UNREADVAL: WalkInstance picks the bool handler by
    // `fi.TypeName`, so the walker must answer "BoolProperty" out of a fake pool. Own chunks and
    // own pool, installed first; nothing needing the real pool may follow.
    //
    // [A3-BOOL-NATIVE-NOWRITE], the B05 review follow-up. Live Walker's rows come from
    // WalkInstance's OWN probe loop, not ProbeBoolLayout, so BOOLLAYOUT above does not reach
    // them. B05 shipped with every UI test injecting BoolNative directly and nothing asking the
    // DLL whether it detects one -- which is how a classifier that recognised no real engine's
    // native bool went green. One class blob per case (s_walkClassCache is keyed by address).
    {
        blk("BOOLNATIVE - WalkInstance marks SetBoolSize's native layout native, and only it");

        static uint8_t bnEntry[3][0x40] = {};
        const char* bnNames[3] = { "", "BoolProperty", "bFlag" };
        static uintptr_t bnChunk[4] = {};
        for (int i = 1; i <= 2; ++i) {
            memcpy(bnEntry[i] + 0x10, bnNames[i], strlen(bnNames[i]) + 1);
            bnChunk[i] = reinterpret_cast<uintptr_t>(bnEntry[i]);
        }
        static uintptr_t bnChunks[2] = { reinterpret_cast<uintptr_t>(bnChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(bnChunks), 0x10);
        check("BOOLNATIVE setup: the pool resolves BoolProperty",
              Serie::GetString(1) == "BoolProperty", Serie::GetString(1).c_str());

        static uint8_t bnFC[0x20] = {};
        *reinterpret_cast<int32_t*>(bnFC + DynOff::FFIELDCLASS_NAME) = 1;

        const bool savedFProp = DynOff::bUseFProperty;
        DynOff::bUseFProperty = true;

        static uint8_t bnInst[0x200] = {};
        static uint8_t bnProp[3][0x100] = {};
        static uint8_t bnCls[3][0x100] = {};
        int bnNext = 0;
        auto walkOne = [&](uint8_t fs, uint8_t bo, uint8_t bm, uint8_t fm, int32_t fieldOffset) {
            const int i = bnNext++;
            *reinterpret_cast<uintptr_t*>(bnProp[i] + DynOff::FFIELD_CLASS) =
                reinterpret_cast<uintptr_t>(bnFC);
            *reinterpret_cast<int32_t*>(bnProp[i] + DynOff::FFIELD_NAME)        = 2;
            *reinterpret_cast<int32_t*>(bnProp[i] + DynOff::FPROPERTY_OFFSET)   = fieldOffset;
            *reinterpret_cast<int32_t*>(bnProp[i] + DynOff::FPROPERTY_ELEMSIZE) = 1;
            *reinterpret_cast<int32_t*>(bnProp[i] + DynOff::FPROPERTY_ELEMSIZE - 4) = 1;
            uint8_t* b = bnProp[i] + DynOff::FBOOLPROP_FIELDSIZE;
            b[0] = fs; b[1] = bo; b[2] = bm; b[3] = fm;
            *reinterpret_cast<int32_t*>(bnCls[i] + DynOff::USTRUCT_PROPSSIZE)    = 0x200;
            *reinterpret_cast<uintptr_t*>(bnCls[i] + DynOff::USTRUCT_CHILDPROPS) =
                reinterpret_cast<uintptr_t>(bnProp[i]);
            const auto r = Ubel::WalkInstance(reinterpret_cast<uintptr_t>(bnInst),
                                              reinterpret_cast<uintptr_t>(bnCls[i]), 64, 2, false);
            // Anti-vacuity: every ⭐ below is a boolean a missing field would satisfy for free.
            check("BOOLNATIVE control: the fake class produced exactly one BoolProperty field",
                  r.fields.size() == 1 && r.fields[0].typeName == "BoolProperty",
                  std::to_string(r.fields.size()).c_str());
            return r.fields.empty() ? Ubel::LiveFieldValue{} : r.fields[0];
        };

        bnInst[0x10] = 0x01;
        const auto nat = walkOne(1, 0, 0x01, 0xFF, 0x10);
        check("BOOLNATIVE ⭐: SetBoolSize's native bytes {1,0,01,FF} publish boolNative", nat.boolNative);
        check("BOOLNATIVE: ...with no mask (Fern sends bool_native, not bool_mask)",
              nat.boolFieldMask == 0);
        check("BOOLNATIVE: ...and read as the whole byte", nat.typedValue == "true",
              nat.typedValue.c_str());

        bnInst[0x20] = 0x04;
        const auto packed = walkOne(1, 0, 0x04, 0x04, 0x20);
        check("BOOLNATIVE control: a packed bit is NOT native", !packed.boolNative);
        check("BOOLNATIVE control: ...and carries its own mask", packed.boolFieldMask == 0x04);

        const auto miss = walkOne(0, 0, 0, 0, 0x30);
        check("BOOLNATIVE control: an unresolved probe is neither -- the UI then refuses",
              !miss.boolNative && miss.boolFieldMask == 0);

        DynOff::bUseFProperty = savedFProp;
    }

    // -- UFUNCWALK-2026-09-11 -- WalkFunctions' UProperty-mode subclass reads on 4.11-4.17 ----
    //
    // ⛔ POOL-FAKING, like IFACEREAD / UNREADVAL / BOOLNATIVE: WalkFunctions keeps a child only if
    // its class is NAMED "Function", and types each param by its class's NAME. Own pool, first.
    //
    // [A2-UFUNC-TAIL-4X]'s lead. In UProperty mode (UE4 < 4.25) WalkFunctions read a param's
    // UStructProperty::Struct / UObjectPropertyBase::PropertyClass at a flat
    // UPROPERTY_OFFSET + 0x2C. That is the 4.18+ delta: on 4.11-4.17 Offset_Internal and
    // RepNotifyFunc sit the other way round and the first subclass field is at +0x28 (the measured
    // table on DynOff::UBoolPropFieldSizeFor). The pointer is planted ONLY at the real slot, so a
    // read at the other one gets a misaligned half-pointer that names nothing.
    {
        blk("UFUNCWALK - WalkFunctions reads a UProperty param's subclass field at the version's delta");

        static uint8_t wfEntry[14][0x40] = {};
        const char* wfNames[14] = { "", "Function", "ObjectProperty", "Target", "Actor",
                                    "StructProperty", "Hit", "HitResult", "DoIt",
                                    "Class", "ScriptStruct", "Decoy", "Thing", "K2Node_DynamicCast_AsActor" };
        static uintptr_t wfChunk[15] = {};
        for (int i = 1; i <= 13; ++i) {
            memcpy(wfEntry[i] + 0x10, wfNames[i], strlen(wfNames[i]) + 1);
            wfChunk[i] = reinterpret_cast<uintptr_t>(wfEntry[i]);
        }
        static uintptr_t wfChunks[2] = { reinterpret_cast<uintptr_t>(wfChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(wfChunks), 0x10);
        check("UFUNCWALK setup: the pool resolves Function", Serie::GetString(1) == "Function",
              Serie::GetString(1).c_str());

        const bool     savedFPropW = DynOff::bUseFProperty;
        const bool     savedCpnW   = DynOff::bCasePreservingName;
        const int      savedOffW   = DynOff::UPROPERTY_OFFSET;
        const uint32_t savedVerW   = g_cachedUEVersion;
        const int      savedStartW = DynOff::UPROPERTY_SUBCLASS_START;
        DynOff::UPROPERTY_SUBCLASS_START = 0;   // no Genau run here: the version's start
        DynOff::bUseFProperty       = false;
        DynOff::bCasePreservingName = false;

        // Named objects: a zeroed UObject whose FName is the given pool index. 0x100 bytes, so a
        // WalkClass of the fake struct reads zeros, not a neighbour.
        static uint8_t wfNamed[13][0x100] = {};
        auto named = [&](int idx) {
            *reinterpret_cast<int32_t*>(wfNamed[idx] + Grimoire::OFF_UOBJECT_NAME) = idx;
            return reinterpret_cast<uintptr_t>(wfNamed[idx]);
        };
        auto put   = [](uint8_t* base, int off, uintptr_t v) { memcpy(base + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* base, int off, int32_t v)   { memcpy(base + off, &v, sizeof(v)); };
        constexpr uintptr_t kWfParm = 0x0080;   // CPF_Parm
        // Actor is a UClass and HitResult a UScriptStruct -- a param's slot is read only when it holds that kind
        // ([STRUCTPROBE-ANY-NAME]); Decoy is an instance of a class called Thing, neither.
        put(wfNamed[9], Grimoire::OFF_UOBJECT_CLASS, named(9));     // Class : Class
        put(wfNamed[4], Grimoire::OFF_UOBJECT_CLASS, named(9));     // Actor : Class
        put(wfNamed[7], Grimoire::OFF_UOBJECT_CLASS, named(10));    // HitResult : ScriptStruct
        put(wfNamed[11], Grimoire::OFF_UOBJECT_CLASS, named(12));   // Decoy : Thing
        named(4); named(7); named(11);

        // ONE set of blobs per case: a class, its UFunction, two UProperty params.
        static uint8_t wfCls[4][0x100] = {}, wfFn[4][0x100] = {};
        static uint8_t wfObjP[4][0x100] = {}, wfStrP[4][0x100] = {};
        auto walkAt = [&](int c, unsigned ver, int offsetInternal, int subclassStart,
                          uintptr_t objTarget = 0, uintptr_t structTarget = 0) {
            g_cachedUEVersion        = ver;
            DynOff::UPROPERTY_OFFSET = offsetInternal;
            put(wfCls[c], DynOff::USTRUCT_CHILDREN, reinterpret_cast<uintptr_t>(wfFn[c]));
            put(wfFn[c], Grimoire::OFF_UOBJECT_CLASS, named(1));                 // "Function"
            put32(wfFn[c], Grimoire::OFF_UOBJECT_NAME, 8);                       // "DoIt"
            put32(wfFn[c], DynOff::FunctionFlagsOffsetFor(ver, false), 0x00080401);
            put(wfFn[c], DynOff::USTRUCT_CHILDREN, reinterpret_cast<uintptr_t>(wfObjP[c]));
            // param 1: ObjectProperty "Target" -> PropertyClass "Actor" at the REAL subclass start. Both params carry
            // CPF_Parm, as every parameter UE lays out does.
            put(wfObjP[c], Grimoire::OFF_UOBJECT_CLASS, named(2));
            put32(wfObjP[c], Grimoire::OFF_UOBJECT_NAME, 3);
            put32(wfObjP[c], DynOff::UPROPERTY_ELEMSIZE, 8);
            put(wfObjP[c], DynOff::UPROPERTY_FLAGS, kWfParm);
            put32(wfObjP[c], offsetInternal, 0);
            put(wfObjP[c], subclassStart, objTarget ? objTarget : named(4));
            put(wfObjP[c], DynOff::UFIELD_NEXT, reinterpret_cast<uintptr_t>(wfStrP[c]));
            // param 2: StructProperty "Hit" -> Struct "HitResult" at the REAL subclass start
            put(wfStrP[c], Grimoire::OFF_UOBJECT_CLASS, named(5));
            put32(wfStrP[c], Grimoire::OFF_UOBJECT_NAME, 6);
            put32(wfStrP[c], DynOff::UPROPERTY_ELEMSIZE, 0x88);
            put(wfStrP[c], DynOff::UPROPERTY_FLAGS, kWfParm);
            put32(wfStrP[c], offsetInternal, 8);
            put(wfStrP[c], subclassStart, structTarget ? structTarget : named(7));
            return Ubel::WalkFunctions(reinterpret_cast<uintptr_t>(wfCls[c]));
        };
        // Anti-vacuity: every ⭐ is a string compare an empty walk would fail -- but say which.
        auto paramsOf = [&](const char* who, const std::vector<FunctionInfo>& fs) {
            const bool shaped = fs.size() == 1 && fs[0].params.size() == 2;
            check((std::string("UFUNCWALK control: ") + who + " -- one function, two params").c_str(),
                  shaped, std::to_string(fs.size()).c_str());
            return shaped ? fs[0].params : std::vector<FunctionParam>{};
        };

        // 4.15 (4.11-4.17): Offset_Internal 0x50 -> first subclass field 0x78 (+0x28).
        const auto p415 = paramsOf("4.15", walkAt(0, 415, 0x50, 0x78));
        if (p415.size() == 2) {
            check("UFUNCWALK ⭐: 4.15 reads the ObjectProperty's PropertyClass at +0x28",
                  p415[0].objClassName == "Actor", p415[0].objClassName.c_str());
            check("UFUNCWALK ⭐: ...and the StructProperty's Struct at +0x28",
                  p415[1].structType == "HitResult", p415[1].structType.c_str());
        }
        // 4.18: Offset_Internal 0x44 -> first subclass field 0x70 (+0x2C). The control.
        const auto p418 = paramsOf("4.18", walkAt(1, 418, 0x44, 0x70));
        if (p418.size() == 2) {
            check("UFUNCWALK control: 4.18 reads PropertyClass at +0x2C as before",
                  p418[0].objClassName == "Actor", p418[0].objClassName.c_str());
            check("UFUNCWALK control: ...and Struct at +0x2C as before",
                  p418[1].structType == "HitResult", p418[1].structType.c_str());
        }

        // Review of 9abc03c8: FindFunctionsByClassParam's ParamTargetType is WalkFunctions' mirror
        // ("Mirrors the param-type enrichment in Ubel::WalkFunctions") and kept the flat +0x2C, so
        // at 4.15 it could not match a param's PropertyClass. The same fakes, asked through it.
        bool retMatch = false;
        g_cachedUEVersion = 415; DynOff::UPROPERTY_OFFSET = 0x50;
        check("UFUNCWALK ⭐: FindFunctionsByClassParam matches the 4.15 ObjectProperty param",
              Aura::CountClassParams(reinterpret_cast<uintptr_t>(wfFn[0]), named(4), retMatch) == 1);
        g_cachedUEVersion = 418; DynOff::UPROPERTY_OFFSET = 0x44;
        check("UFUNCWALK control: ...and the 4.18 one, as before",
              Aura::CountClassParams(reinterpret_cast<uintptr_t>(wfFn[1]), named(4), retMatch) == 1);

        // [FUNCPARM-CONSUMERS] A Blueprint function's chain holds its locals after the parameters, and a local typed
        // with the class (K2Node_DynamicCast_AsActor, the cast node's output) is not the function taking it. The
        // 4.18 function again, with such a local after its two params. Its flags word carries non-Parm bits, so
        // "any bit set" cannot pass for CPF_Parm.
        static uint8_t wfLocal[0x100] = {};
        put(wfLocal, Grimoire::OFF_UOBJECT_CLASS, named(2));                  // ObjectProperty
        put32(wfLocal, Grimoire::OFF_UOBJECT_NAME, 13);
        put32(wfLocal, DynOff::UPROPERTY_ELEMSIZE, 8);
        put(wfLocal, DynOff::UPROPERTY_FLAGS, 0x0008001040000200ull);
        put32(wfLocal, DynOff::UPROPERTY_OFFSET, 0x90);                       // past the parameter block
        put(wfLocal, 0x70, named(4));                                         // PropertyClass Actor, 4.18's slot
        put(wfStrP[1], DynOff::UFIELD_NEXT, reinterpret_cast<uintptr_t>(wfLocal));
        // Anti-vacuity (review of 987a0dab): the same entry flagged CPF_Parm IS counted, so the star check below
        // fails for the flag and not because the local is unreachable.
        put(wfLocal, DynOff::UPROPERTY_FLAGS, 0x0008001040000280ull);
        check("UFUNCWALK control: the same entry, flagged CPF_Parm, is counted",
              Aura::CountClassParams(reinterpret_cast<uintptr_t>(wfFn[1]), named(4), retMatch) == 2);
        put(wfLocal, DynOff::UPROPERTY_FLAGS, 0x0008001040000200ull);
        check("UFUNCWALK ⭐: FindFunctionsByClassParam does not count a Blueprint local typed with the class",
              Aura::CountClassParams(reinterpret_cast<uintptr_t>(wfFn[1]), named(4), retMatch) == 1);
        put(wfStrP[1], DynOff::UFIELD_NEXT, 0);

        // [STRUCTPROBE-ANY-NAME] (review of build 3596): this UProperty param path took ANY named object as the
        // param's struct / class -- and walked it as a struct. A named non-struct, non-class object in the slot:
        const auto pDecoy = paramsOf("a decoy in the slot", walkAt(2, 418, 0x44, 0x70, named(11), named(11)));
        if (pDecoy.size() == 2) {
            check("UFUNCWALK ⭐: a UProperty param's slot holding a named NON-class names no PropertyClass",
                  pDecoy[0].objClassName.empty(), pDecoy[0].objClassName.c_str());
            check("UFUNCWALK ⭐: ...nor a named NON-struct a Struct", pDecoy[1].structType.empty(),
                  pDecoy[1].structType.c_str());
        }

        // [UPROP-SUBCLASS-SLOT] (review of build 3596): the start Genau RECORDED, not the version's -- a 4.15 layout
        // labelled 4.22 (the version formula says 0x7C, the layout 0x78). Pins the WalkFunctions and the
        // ParamTargetType sites, which a revert to the version formula left green.
        DynOff::UPROPERTY_SUBCLASS_START = 0x78;
        const auto pMis = paramsOf("4.15 labelled 4.22", walkAt(3, 422, 0x50, 0x78));
        if (pMis.size() == 2) {
            check("UFUNCWALK ⭐: WalkFunctions reads a param at the recorded start, not the version's",
                  pMis[0].objClassName == "Actor" && pMis[1].structType == "HitResult",
                  (pMis[0].objClassName + "/" + pMis[1].structType).c_str());
        }
        g_cachedUEVersion = 422; DynOff::UPROPERTY_OFFSET = 0x50;
        check("UFUNCWALK ⭐: ...and so does FindFunctionsByClassParam's matcher",
              Aura::CountClassParams(reinterpret_cast<uintptr_t>(wfFn[3]), named(4), retMatch) == 1);
        DynOff::UPROPERTY_SUBCLASS_START = savedStartW;

        g_cachedUEVersion           = savedVerW;
        DynOff::UPROPERTY_OFFSET    = savedOffW;
        DynOff::bCasePreservingName = savedCpnW;
        DynOff::bUseFProperty       = savedFPropW;
    }

    // -- UFUNCPARM-2026-10-06 -- WalkFunctions says which chain entries are PARAMETERS ----------------
    //
    // ⛔ POOL-FAKING, like UFUNCWALK: WalkFunctions keeps a child only if its class is NAMED "Function", and
    // types each entry by its class's NAME. Own pool, first.
    //
    // [EXTPR-539-540-2026-10-02] D3. A UFunction's property chain holds its parameters (CPF_Parm, the return
    // included) and, on a Blueprint function, its locals after them (CallFunc_*_ReturnValue, K2Node_*,
    // Temp_*): the 2026-08 Y1 trap, where the Invoke form offered two frame locals past parmsSize as
    // arguments. WalkFunctions still lists every entry; it now also says which ones are parameters, from the
    // PropertyFlags word it already reads for out / ret. Both property models: FField and UProperty.
    {
        blk("UFUNCPARM - WalkFunctions flags the CPF_Parm entries, so a Blueprint local is not a parameter");

        static uint8_t upEntry[9][0x40] = {};
        const char* upNames[9] = { "", "Function", "IntProperty", "BoolProperty", "Count",
                                   "ReturnValue", "Temp_int_Variable", "DoIt", "Class" };
        static uintptr_t upChunk[10] = {};
        for (int i = 1; i <= 8; ++i) {
            memcpy(upEntry[i] + 0x10, upNames[i], strlen(upNames[i]) + 1);
            upChunk[i] = reinterpret_cast<uintptr_t>(upEntry[i]);
        }
        static uintptr_t upChunks[2] = { reinterpret_cast<uintptr_t>(upChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(upChunks), 0x10);
        check("UFUNCPARM setup: the pool resolves Function", Serie::GetString(1) == "Function",
              Serie::GetString(1).c_str());

        const bool savedFPropP = DynOff::bUseFProperty;
        const bool savedCpnP   = DynOff::bCasePreservingName;
        DynOff::bCasePreservingName = false;

        constexpr uint64_t kParm = 0x0080, kOut = 0x0100, kRet = 0x0400;
        // What a numeric property carries anyway: ZeroConstructor | IsPlainOldData | NoDestructor |
        // HasGetValueTypeHash. On every entry, so a local is never flags 0 and "any bit set" cannot pass for
        // CPF_Parm (review of 0fa23e3f).
        constexpr uint64_t kPod = 0x0008001040000200ull;
        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto put64 = [](uint8_t* b, int off, uint64_t v)  { memcpy(b + off, &v, sizeof(v)); };

        // UProperty mode types an entry by its UClass's name; FField mode by its FFieldClass's.
        static uint8_t upNamed[9][0x100] = {};
        auto named = [&](int idx) {
            *reinterpret_cast<int32_t*>(upNamed[idx] + Grimoire::OFF_UOBJECT_NAME) = idx;
            return reinterpret_cast<uintptr_t>(upNamed[idx]);
        };
        static uint8_t upFC[9][0x20] = {};
        auto fclass = [&](int idx) {
            *reinterpret_cast<int32_t*>(upFC[idx] + DynOff::FFIELDCLASS_NAME) = idx;
            return reinterpret_cast<uintptr_t>(upFC[idx]);
        };

        // The chain in UE's order: Count (an int32 parameter), ReturnValue (the return, which UE marks
        // CPF_Parm | CPF_OutParm | CPF_ReturnParm), then Temp_int_Variable, a local past the parameter block.
        struct Entry { int name, type; int32_t size, offset; uint64_t flags; };
        const Entry chain[3] = { { 4, 2, 4, 0, kParm | kPod },
                                 { 5, 3, 1, 4, kParm | kOut | kRet | kPod },
                                 { 6, 2, 4, 8, kPod } };

        // ONE set of blobs per property model.
        static uint8_t upCls[2][0x100] = {}, upFn[2][0x100] = {}, upProp[2][3][0x100] = {};
        auto walk = [&](int m, bool fprop) {
            DynOff::bUseFProperty = fprop;
            putP(upCls[m], DynOff::USTRUCT_CHILDREN, reinterpret_cast<uintptr_t>(upFn[m]));
            putP(upFn[m], Grimoire::OFF_UOBJECT_CLASS, named(1));                 // "Function"
            put32(upFn[m], Grimoire::OFF_UOBJECT_NAME, 7);                        // "DoIt"
            putP(upFn[m], fprop ? DynOff::USTRUCT_CHILDPROPS : DynOff::USTRUCT_CHILDREN,
                 reinterpret_cast<uintptr_t>(upProp[m][0]));
            for (int i = 0; i < 3; ++i) {
                uint8_t* pr = upProp[m][i];
                const Entry& e = chain[i];
                const uintptr_t next = i < 2 ? reinterpret_cast<uintptr_t>(upProp[m][i + 1]) : 0;
                if (fprop) {
                    putP(pr, DynOff::FFIELD_CLASS, fclass(e.type));
                    put32(pr, DynOff::FFIELD_NAME, e.name);
                    put32(pr, DynOff::FPROPERTY_ELEMSIZE, e.size);
                    put32(pr, DynOff::FPROPERTY_OFFSET, e.offset);
                    put64(pr, DynOff::FPROPERTY_FLAGS, e.flags);
                    putP(pr, DynOff::FFIELD_NEXT, next);
                } else {
                    putP(pr, Grimoire::OFF_UOBJECT_CLASS, named(e.type));
                    put32(pr, Grimoire::OFF_UOBJECT_NAME, e.name);
                    put32(pr, DynOff::UPROPERTY_ELEMSIZE, e.size);
                    put32(pr, DynOff::UPROPERTY_OFFSET, e.offset);
                    put64(pr, DynOff::UPROPERTY_FLAGS, e.flags);
                    putP(pr, DynOff::UFIELD_NEXT, next);
                }
            }
            return Ubel::WalkFunctions(reinterpret_cast<uintptr_t>(upCls[m]));
        };

        for (int m = 0; m < 2; ++m) {
            const bool fprop = m == 0;
            const std::string who = fprop ? "FField" : "UProperty";
            const auto fs = walk(m, fprop);
            // Anti-vacuity: "not a parameter" would hold for an entry that was never read.
            const bool shaped = fs.size() == 1 && fs[0].params.size() == 3
                && fs[0].params[2].name == "Temp_int_Variable";
            check(("UFUNCPARM control: " + who + " -- one function, every chain entry listed, the local too").c_str(),
                  shaped, std::to_string(fs.empty() ? 0 : fs[0].params.size()).c_str());
            if (!shaped) continue;
            const auto& ps = fs[0].params;
            check(("UFUNCPARM control: " + who + " -- out / ret read from the same flags as before").c_str(),
                  ps[1].isReturn && ps[1].isOut && !ps[0].isReturn && !ps[2].isReturn);
            check(("UFUNCPARM ⭐: " + who + " -- the parameter and the return are parameters").c_str(),
                  ps[0].isParm && ps[1].isParm);
            check(("UFUNCPARM ⭐: " + who + " -- the local after them is not").c_str(), !ps[2].isParm);

            // [FUNCPARM-CONSUMERS] review: Mimic clears the return slot before an invoke, and the slot comes from
            // the chain's CPF_ReturnParm entry (ResolveFunctionInfo reads only the tail, which has no size).
            int32_t retOff = -1, retSize = 0;
            const bool hasRet = Ubel::ReadReturnSlot(reinterpret_cast<uintptr_t>(upFn[m]), retOff, retSize);
            check(("UFUNCPARM ⭐: " + who + " -- the return slot is the CPF_ReturnParm entry's").c_str(),
                  hasRet && retOff == 4 && retSize == 1,
                  (std::to_string(retOff) + "/" + std::to_string(retSize)).c_str());
            // No return: drop the ReturnValue entry from the chain (Count links straight to the local).
            const uintptr_t savedNext = *reinterpret_cast<uintptr_t*>(upProp[m][0] +
                (fprop ? DynOff::FFIELD_NEXT : DynOff::UFIELD_NEXT));
            putP(upProp[m][0], fprop ? DynOff::FFIELD_NEXT : DynOff::UFIELD_NEXT,
                 reinterpret_cast<uintptr_t>(upProp[m][2]));
            check(("UFUNCPARM ⭐: " + who + " -- a function without a return has no return slot").c_str(),
                  !Ubel::ReadReturnSlot(reinterpret_cast<uintptr_t>(upFn[m]), retOff, retSize));
            putP(upProp[m][0], fprop ? DynOff::FFIELD_NEXT : DynOff::UFIELD_NEXT, savedNext);
        }

        DynOff::bCasePreservingName = savedCpnP;
        DynOff::bUseFProperty       = savedFPropP;
    }

    // -- OPTLAYOUT-2026-09-11 -- TOptional set/unset follows the LAYOUT, not the inner type's name --
    //
    // ⛔ POOL-FAKING, like IFACEREAD / UNREADVAL / BOOLNATIVE / UFUNCWALK: the walker and Find Refs
    // pick their TOptional arm by type NAME out of the pool. Own pool, installed first.
    //
    // [A2-TOPTIONAL-INTRUSIVE]. UE decides "set" through ValueProperty->HasIntrusiveUnsetOptionalState()
    // and sizes the property by FOptionalPropertyLayout::CalcSize (UE 5.8 PropertyOptional.h):
    // intrusive -> sizeof(T); otherwise Align(sizeof(T) + 1, alignof(T)) with the bIsSet byte at
    // +sizeof(T). The walker instead decided by the inner type's NAME: an object optional was
    // "unset" when its pointer was null (a TOptional<AActor*> set to null showed (unset); after
    // Reset(), which writes no bytes, the stale pointer was published), and a container optional read
    // its bIsSet at field + sizeof(T) -- the NEXT property's first byte. Find Refs held the same belief.
    {
        blk("OPTLAYOUT - TOptional set/unset follows UE's CalcSize layout, and Find Refs agrees");

        static uint8_t olEntry[13][0x40] = {};
        const char* olNames[13] = { "", "OptionalProperty", "ObjectProperty", "ArrayProperty",
                                    "StrProperty", "Opt", "Inner", "NameProperty", "TextProperty",
                                    "StructProperty", "MyStruct", "LazyObjectProperty", "ScriptStruct" };
        static uintptr_t olChunk[14] = {};
        for (int i = 1; i <= 12; ++i) {
            memcpy(olEntry[i] + 0x10, olNames[i], strlen(olNames[i]) + 1);
            olChunk[i] = reinterpret_cast<uintptr_t>(olEntry[i]);
        }
        static uintptr_t olChunks[2] = { reinterpret_cast<uintptr_t>(olChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(olChunks), 0x10);
        check("OPTLAYOUT setup: the pool resolves OptionalProperty",
              Serie::GetString(1) == "OptionalProperty", Serie::GetString(1).c_str());

        const bool     savedFPropO = DynOff::bUseFProperty;
        const bool     savedCpnO   = DynOff::bCasePreservingName;
        const uint32_t savedVerO   = g_cachedUEVersion;
        DynOff::bUseFProperty       = true;
        DynOff::bCasePreservingName = false;
        g_cachedUEVersion           = 505;

        static uint8_t olFC[12][0x20] = {};
        auto fclass = [&](int nameIdx) {
            *reinterpret_cast<int32_t*>(olFC[nameIdx] + DynOff::FFIELDCLASS_NAME) = nameIdx;
            return reinterpret_cast<uintptr_t>(olFC[nameIdx]);
        };
        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };

        // The wrapped value properties: ObjectProperty (8), ArrayProperty (16), StrProperty (16).
        static uint8_t olInner[8][0x100] = {};
        auto inner = [&](int k, int typeIdx, int32_t size) {
            putP(olInner[k], DynOff::FFIELD_CLASS, fclass(typeIdx));
            put32(olInner[k], DynOff::FFIELD_NAME, 6);
            put32(olInner[k], DynOff::FPROPERTY_ELEMSIZE, size);
            return reinterpret_cast<uintptr_t>(olInner[k]);
        };
        const uintptr_t innerObj = inner(0, 2, 8);
        const uintptr_t innerArr = inner(1, 3, 16);
        const uintptr_t innerStr = inner(2, 4, 16);

        // ONE class + property + object per case (s_walkClassCache and the ref-meta cache are
        // keyed by class address). The optional field sits at +0x40 of its object; the object's
        // UClass pointer is at OFF_UOBJECT_CLASS so Find Refs can reach the class.
        constexpr int32_t kField = 0x40;
        static uint8_t olProp[24][0x100] = {}, olCls[24][0x100] = {}, olObj[24][0x200] = {};
        static uint8_t olStale[0x100] = {};                 // a "UObject" a reset optional still points at
        const uintptr_t stale = reinterpret_cast<uintptr_t>(olStale);
        int olNext = 0;
        auto makeCase = [&](int32_t optSize, uintptr_t innerProp) {
            const int i = olNext++;
            putP(olProp[i], DynOff::FFIELD_CLASS, fclass(1));
            put32(olProp[i], DynOff::FFIELD_NAME, 5);
            put32(olProp[i], DynOff::FPROPERTY_OFFSET, kField);
            put32(olProp[i], DynOff::FPROPERTY_ELEMSIZE, optSize);
            put32(olProp[i], DynOff::FPROPERTY_ELEMSIZE - 4, 1);
            putP(olProp[i], DynOff::FARRAYPROP_INNER, innerProp);
            put32(olCls[i], DynOff::USTRUCT_PROPSSIZE, 0x200);
            putP(olCls[i], DynOff::USTRUCT_CHILDPROPS, reinterpret_cast<uintptr_t>(olProp[i]));
            putP(olObj[i], Grimoire::OFF_UOBJECT_CLASS, reinterpret_cast<uintptr_t>(olCls[i]));
            return i;
        };
        auto walk = [&](const char* who, int i) -> Ubel::LiveFieldValue {
            const auto r = Ubel::WalkInstance(reinterpret_cast<uintptr_t>(olObj[i]),
                                              reinterpret_cast<uintptr_t>(olCls[i]), 64, 2, false);
            // Anti-vacuity: "(unset)" / "!= (unset)" would both be satisfied by a missing field.
            check((std::string("OPTLAYOUT control: ") + who + " -- exactly one OptionalProperty field").c_str(),
                  r.fields.size() == 1 && r.fields[0].typeName == "OptionalProperty",
                  std::to_string(r.fields.size()).c_str());
            return r.fields.empty() ? Ubel::LiveFieldValue{} : r.fields[0];
        };
        auto isRefusal = [](const std::string& s) {
            return s.find("unreadable") != std::string::npos || s.find("not recognised") != std::string::npos
                || s.find("not decoded") != std::string::npos;
        };

        // A. TOptional<UObject*>, NON-intrusive (16 = Align(8+1, 8)), after Reset(): the pointer bytes
        //    are still there, bIsSet (+8) is 0. UE says UNSET, and the stale pointer must not escape.
        const int cA = makeCase(16, innerObj);
        putP(olObj[cA], kField, stale);
        olObj[cA][kField + 8] = 0;
        const auto fA = walk("reset object optional", cA);
        check("OPTLAYOUT ⭐: a RESET non-intrusive object optional reads (unset)",
              fA.typedValue == "(unset)", fA.typedValue.c_str());
        check("OPTLAYOUT ⭐: ...and publishes NO stale, drillable pointer", fA.ptrValue == 0);

        // B. The same optional SET to nullptr: bIsSet 1, pointer 0. UE says SET.
        const int cB = makeCase(16, innerObj);
        olObj[cB][kField + 8] = 1;
        const auto fB = walk("object optional set to null", cB);
        check("OPTLAYOUT ⭐: an object optional SET to null is not (unset)",
              fB.typedValue != "(unset)" && !isRefusal(fB.typedValue), fB.typedValue.c_str());
        check("OPTLAYOUT: ...it renders exactly (set: null), with no drillable pointer",
              fB.typedValue == "(set: null)" && fB.ptrValue == 0, fB.typedValue.c_str());

        // A2 (control, green both ways). Set, pointing at a live object.
        const int cA2 = makeCase(16, innerObj);
        putP(olObj[cA2], kField, stale);
        olObj[cA2][kField + 8] = 1;
        const auto fA2 = walk("set object optional", cA2);
        check("OPTLAYOUT control: a SET object optional publishes its pointer", fA2.ptrValue == stale);

        // C. TOptional<TArray>, INTRUSIVE (UE 5.5+: 16 == sizeof(TArray)): unset is ArrayMax == -1 at
        //    +12. The byte at +16 belongs to the NEXT property and must decide nothing.
        const int cC = makeCase(16, innerArr);
        put32(olObj[cC], kField + 12, -1);
        olObj[cC][kField + 16] = 1;
        const auto fC = walk("unset intrusive array optional", cC);
        check("OPTLAYOUT ⭐: an intrusive TArray optional with ArrayMax -1 reads (unset)",
              fC.typedValue == "(unset)", fC.typedValue.c_str());

        // D. The same, SET (ArrayMax 4), with a zero neighbour byte.
        const int cD = makeCase(16, innerArr);
        put32(olObj[cD], kField + 8, 0);
        put32(olObj[cD], kField + 12, 4);
        olObj[cD][kField + 16] = 0;
        const auto fD = walk("set intrusive array optional", cD);
        check("OPTLAYOUT ⭐: an intrusive TArray optional with a real ArrayMax is SET",
              fD.typedValue != "(unset)" && !isRefusal(fD.typedValue), fD.typedValue.c_str());

        // E. TOptional<FString>, NON-intrusive -- the UE 5.3 / 5.4 shape (24 = Align(16+1, 8)), bIsSet
        //    at +16. Unset with an all-zero FString, whose ArrayMax is 0, not the 5.5+ sentinel -1.
        const int cE = makeCase(24, innerStr);
        olObj[cE][kField + 16] = 0;
        const auto fE = walk("unset non-intrusive string optional", cE);
        check("OPTLAYOUT ⭐: a 5.4-shape unset FString optional reads (unset), not set-and-empty",
              fE.typedValue == "(unset)", fE.typedValue.c_str());

        // E2 (control, green both ways). Set, empty.
        const int cE2 = makeCase(24, innerStr);
        olObj[cE2][kField + 16] = 1;
        const auto fE2 = walk("set empty string optional", cE2);
        check("OPTLAYOUT control: a set, empty FString optional reads \"\"",
              fE2.typedValue == "\"\"", fE2.typedValue.c_str());

        // F. A size that matches NEITHER layout (20 for an 8-byte pointer): refuse, never guess.
        const int cF = makeCase(20, innerObj);
        putP(olObj[cF], kField, stale);
        const auto fF = walk("unrecognised optional layout", cF);
        check("OPTLAYOUT ⭐: an optional whose size fits neither layout is REFUSED",
              isRefusal(fF.typedValue), fF.typedValue.c_str());
        check("OPTLAYOUT ⭐: ...and publishes no pointer", fF.ptrValue == 0);

        // G. Find Refs' twin: the outgoing-pointer enumerator (the per-object read Find Refs mirrors)
        //    must not report a RESET optional's stale pointer as a live reference.
        auto hitsOf = [&](int i) {
            int hits = 0;
            Aura::EnumerateOutgoingObjectPtrs(reinterpret_cast<uintptr_t>(olObj[i]),
                [&](uintptr_t child, auto&&...) -> bool { if (child == stale) ++hits; return false; });
            return hits;
        };
        check("OPTLAYOUT control: Find Refs sees a SET optional's pointer", hitsOf(cA2) == 1);
        check("OPTLAYOUT ⭐: Find Refs does NOT report a RESET optional's stale pointer", hitsOf(cA) == 0);
        // Review of cc430176: the "unprovable layout is not bucketed" rule. Both candidate flag bytes
        // are set, so a mutant treating Unknown as a trailing flag would find its gate open.
        olObj[cF][kField + 8] = 1;
        olObj[cF][kField + 16] = 1;
        check("OPTLAYOUT: Find Refs does not bucket an optional whose layout it cannot prove",
              hitsOf(cF) == 0);

        // (#7) The intrusive sentinels beyond TArray: FString ArrayMax -1 @+12, FName ~0u @+0,
        // FText TextData null @+0 -- each unset and set.
        const uintptr_t innerName = inner(3, 7, DynOff::SizeofFName());
        const uintptr_t innerText = inner(4, 8, 16);
        const int nameSize = DynOff::SizeofFName();
        const int cSu = makeCase(16, innerStr);   put32(olObj[cSu], kField + 12, -1);
        const int cSs = makeCase(16, innerStr);   put32(olObj[cSs], kField + 12, 0); olObj[cSs][kField + 16] = 1;
        const int cNu = makeCase(nameSize, innerName); put32(olObj[cNu], kField, -1);
        const int cNs = makeCase(nameSize, innerName); put32(olObj[cNs], kField, 5);
        static uint8_t olTextData[0x100] = {};
        const int cTu = makeCase(16, innerText);
        const int cTs = makeCase(16, innerText);  putP(olObj[cTs], kField, reinterpret_cast<uintptr_t>(olTextData));
        const auto fSu = walk("intrusive FString, unset", cSu);
        const auto fSs = walk("intrusive FString, set", cSs);
        const auto fNu = walk("intrusive FName, unset", cNu);
        const auto fNs = walk("intrusive FName, set", cNs);
        const auto fTu = walk("intrusive FText, unset", cTu);
        const auto fTs = walk("intrusive FText, set", cTs);
        check("OPTLAYOUT: intrusive FString with ArrayMax -1 reads (unset)", fSu.typedValue == "(unset)", fSu.typedValue.c_str());
        check("OPTLAYOUT: intrusive FString with a real ArrayMax is SET (a set neighbour byte decides nothing)",
              fSs.typedValue != "(unset)" && !isRefusal(fSs.typedValue), fSs.typedValue.c_str());
        check("OPTLAYOUT: intrusive FName with ComparisonIndex ~0u reads (unset)", fNu.typedValue == "(unset)", fNu.typedValue.c_str());
        check("OPTLAYOUT: intrusive FName with a real index is SET",
              fNs.typedValue != "(unset)" && !isRefusal(fNs.typedValue), fNs.typedValue.c_str());
        check("OPTLAYOUT: intrusive FText with null TextData reads (unset)", fTu.typedValue == "(unset)", fTu.typedValue.c_str());
        check("OPTLAYOUT: intrusive FText with TextData is SET",
              fTs.typedValue != "(unset)" && !isRefusal(fTs.typedValue), fTs.typedValue.c_str());

        // (#8) A struct optional: the struct probe + UScriptStruct::MinAlignment decide the layout.
        static uint8_t olStruct[2][0x100] = {};
        // MyStruct is a UScriptStruct: a struct slot is read only when it holds one ([OPTSTRUCT-ANY-NAME]).
        static uint8_t olSsCls[0x100] = {};
        *reinterpret_cast<int32_t*>(olSsCls + Grimoire::OFF_UOBJECT_NAME) = 12;              // "ScriptStruct"
        auto structInner = [&](int k, int s, int16_t minAlign) {
            *reinterpret_cast<int32_t*>(olStruct[s] + Grimoire::OFF_UOBJECT_NAME) = 10;      // "MyStruct"
            putP(olStruct[s], Grimoire::OFF_UOBJECT_CLASS, reinterpret_cast<uintptr_t>(olSsCls));
            put32(olStruct[s], DynOff::USTRUCT_PROPSSIZE, 24);
            memcpy(olStruct[s] + DynOff::USTRUCT_PROPSSIZE + 4, &minAlign, sizeof(minAlign));
            const uintptr_t p = inner(k, 9, 24);                                              // "StructProperty"
            putP(olInner[k], DynOff::FSTRUCTPROP_STRUCT, reinterpret_cast<uintptr_t>(olStruct[s]));
            return p;
        };
        const uintptr_t innerStruct8 = structInner(5, 0, 8);
        const uintptr_t innerStruct4 = structInner(6, 1, 4);
        const int cVs = makeCase(32, innerStruct8); olObj[cVs][kField + 24] = 1;             // Align(25,8) = 32
        const int cVu = makeCase(32, innerStruct8); olObj[cVu][kField + 24] = 0;
        const int cVw = makeCase(32, innerStruct4);                                          // Align(25,4) = 28 != 32
        const auto fVs = walk("set struct optional", cVs);
        const auto fVu = walk("unset struct optional", cVu);
        const auto fVw = walk("struct optional, wrong MinAlignment", cVw);
        check("OPTLAYOUT: a SET struct optional (MinAlignment 8, 32 bytes) is not refused and not (unset)",
              fVs.typedValue != "(unset)" && !isRefusal(fVs.typedValue), fVs.typedValue.c_str());
        check("OPTLAYOUT: ...the same optional with its flag clear reads (unset)", fVu.typedValue == "(unset)", fVu.typedValue.c_str());
        check("OPTLAYOUT control: a MinAlignment that does not produce the size is refused",
              isRefusal(fVw.typedValue), fVw.typedValue.c_str());

        // Review of cc430176: TOptional<TLazyObjectPtr> on 5.3+ is Align(0x18 + 1, 4) = 0x1C. With
        // Scharf's old alignment of 8 it was "not recognised", and dropped from Find Refs.
        const uintptr_t innerLazy = inner(7, 11, 0x18);
        const int cLu = makeCase(0x1C, innerLazy); olObj[cLu][kField + 0x18] = 0;
        const auto fLu = walk("unset lazy optional", cLu);
        check("OPTLAYOUT ⭐: a 5.3+ TOptional<TLazyObjectPtr> (0x1C) is recognised -- an unset one reads (unset)",
              fLu.typedValue == "(unset)", fLu.typedValue.c_str());

        // (#9) [A2-TOPTIONAL-STRUCT-DESCENT] A TOptional<FStruct> after Reset(): UE's MarkUnset destroys the value and
        // clears bIsSet, and zeroes NO bytes -- a TArray even keeps its Data / Num. Find Refs and the Address Finder
        // descended into the struct anyway ("an unset slot is zero"), so a reset optional's stale pointer, or its stale
        // array, read as live. Two structs, each 24 bytes with MinAlignment 8 -> Align(25, 8) = 32, bIsSet at +24:
        // { UObject* Inner; } and { TArray<UObject*> Inner; }.
        static uint8_t olSR[2][0x100] = {}, olSRChild[2][0x100] = {}, olSRInner[2][0x100] = {};
        auto structWithChild = [&](int k, int typeIdx, int32_t childSize, uintptr_t childInner) {
            putP(olSRChild[k], DynOff::FFIELD_CLASS, fclass(typeIdx));
            put32(olSRChild[k], DynOff::FFIELD_NAME, 6);
            put32(olSRChild[k], DynOff::FPROPERTY_OFFSET, 0);
            put32(olSRChild[k], DynOff::FPROPERTY_ELEMSIZE, childSize);
            put32(olSRChild[k], DynOff::FPROPERTY_ELEMSIZE - 4, 1);
            if (childInner) putP(olSRChild[k], DynOff::FARRAYPROP_INNER, childInner);
            *reinterpret_cast<int32_t*>(olSR[k] + Grimoire::OFF_UOBJECT_NAME) = 10;          // "MyStruct"
            putP(olSR[k], Grimoire::OFF_UOBJECT_CLASS, reinterpret_cast<uintptr_t>(olSsCls));
            put32(olSR[k], DynOff::USTRUCT_PROPSSIZE, 24);
            const int16_t align8 = 8;
            memcpy(olSR[k] + DynOff::USTRUCT_PROPSSIZE + 4, &align8, sizeof(align8));
            putP(olSR[k], DynOff::USTRUCT_CHILDPROPS, reinterpret_cast<uintptr_t>(olSRChild[k]));
            putP(olSRInner[k], DynOff::FFIELD_CLASS, fclass(9));                              // "StructProperty"
            put32(olSRInner[k], DynOff::FFIELD_NAME, 6);
            put32(olSRInner[k], DynOff::FPROPERTY_ELEMSIZE, 24);
            putP(olSRInner[k], DynOff::FSTRUCTPROP_STRUCT, reinterpret_cast<uintptr_t>(olSR[k]));
            return reinterpret_cast<uintptr_t>(olSRInner[k]);
        };
        const uintptr_t optPtrStruct = structWithChild(0, 2, 8, 0);          // { UObject* }
        const uintptr_t optArrStruct = structWithChild(1, 3, 16, innerObj);  // { TArray<UObject*> }

        const int cSPu = makeCase(32, optPtrStruct); putP(olObj[cSPu], kField, stale); olObj[cSPu][kField + 24] = 0;
        const int cSPs = makeCase(32, optPtrStruct); putP(olObj[cSPs], kField, stale); olObj[cSPs][kField + 24] = 1;
        // 28 fits neither Align(25, 8) = 32 nor 24: a layout nobody can prove. Its would-be flag byte is SET, so a
        // mutant treating Unknown as a trailing flag would find its gate open.
        const int cSPk = makeCase(28, optPtrStruct); putP(olObj[cSPk], kField, stale); olObj[cSPk][kField + 24] = 1;
        static uintptr_t olStaleArr[1] = {};
        olStaleArr[0] = stale;    // the destroyed array's buffer still holds its old element
        auto putArr = [&](int c) {
            putP(olObj[c], kField, reinterpret_cast<uintptr_t>(olStaleArr));
            put32(olObj[c], kField + 8, 1);
            put32(olObj[c], kField + 12, 1);
        };
        const int cSAu = makeCase(32, optArrStruct); putArr(cSAu); olObj[cSAu][kField + 24] = 0;
        const int cSAs = makeCase(32, optArrStruct); putArr(cSAs); olObj[cSAs][kField + 24] = 1;

        check("OPTLAYOUT control: Find Refs descends into a SET struct optional", hitsOf(cSPs) == 1);
        check("OPTLAYOUT ⭐: Find Refs does NOT report a RESET struct optional's stale pointer", hitsOf(cSPu) == 0);
        check("OPTLAYOUT ⭐: Find Refs does not descend into a struct optional whose layout it cannot prove",
              hitsOf(cSPk) == 0);
        check("OPTLAYOUT control: Find Refs reads a SET struct optional's object array", hitsOf(cSAs) == 1);
        check("OPTLAYOUT ⭐: Find Refs does NOT report a RESET struct optional's stale object array",
              hitsOf(cSAu) == 0);
        // The Address Finder half: the container cache carries the same gate, relative to the entry.
        const auto& olConts = Aura::GetClassContainers(reinterpret_cast<uintptr_t>(olCls[cSAu]));
        check("OPTLAYOUT control: the container cache sees the struct optional's array",
              olConts.size() == 1 && olConts[0].offset == kField, std::to_string(olConts.size()).c_str());
        check("OPTLAYOUT ⭐: ...and gates it on the optional's bIsSet (+24 from the array)",
              olConts.size() == 1 && olConts[0].setFlagOffset == 24);

        g_cachedUEVersion           = savedVerO;
        DynOff::bCasePreservingName = savedCpnO;
        DynOff::bUseFProperty       = savedFPropO;
    }

    // -- FIELDPATHARR-2026-09-16 -- a TArray<TFieldPath> publishes its elements ---------------------
    //
    // ⛔ POOL-FAKING BLOCK: it installs its OWN UE4 name pool, like IFACEREAD / UNREADVAL / OPTLAYOUT
    // above, and depends on nothing they leave behind.
    //
    // [WALK-FIELDPATH-ARRAY-NOELEMS]. No phase claimed `FieldPathProperty`, so the walk sent such an
    // array with its count and NOT ONE element -- the same hole `[W5-STRARRAY-ELEMENTS]` closed for
    // strings. Found live 2026-09-16: the fixture's `Arr_FieldPath` read `num=2` on the wire with
    // zero elements rendered.
    //
    // ⭐ THE STRIDE IS THE ENGINE'S, AND `Path` IS FOUND FROM IT. `InitialFieldClass` and
    // `FieldPathSerialNumber` are WITH_EDITORONLY_DATA (UE 5.4 FieldPath.h:56-63), so the same struct
    // is 32 bytes in a game build and 48 in an editor build -- and `Path` is the LAST member in both.
    // Both shapes are asserted here with the SAME expected values, which is what pins
    // `pathOffset = elemSize - 16` rather than a constant.
    {
        blk("FIELDPATHARR - a TArray<TFieldPath> reads each element's path name");

        static uint8_t fpEntry[3][0x40] = {};
        const char* fpNames[3] = { "", "TickCount", "FrozenInt" };
        static uintptr_t fpChunk[4] = {};
        for (int i = 1; i <= 2; ++i) {
            memcpy(fpEntry[i] + 0x10, fpNames[i], strlen(fpNames[i]) + 1);
            fpChunk[i] = reinterpret_cast<uintptr_t>(fpEntry[i]);
        }
        static uintptr_t fpChunks[2] = { reinterpret_cast<uintptr_t>(fpChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(fpChunks), 0x10);
        check("FIELDPATHARR setup: the pool resolves TickCount",
              Serie::GetString(1) == "TickCount", Serie::GetString(1).c_str());

        const bool savedCpnF = DynOff::bCasePreservingName;
        DynOff::bCasePreservingName = false;          // sizeof(FName) == 8

        struct FakeName  { int32_t Comparison; int32_t Number; };
        struct FakeTArr  { uintptr_t Data; int32_t Num; int32_t Max; };
        static_assert(sizeof(FakeName) == 8, "FName is two int32 without case preservation");

        static FakeName nTick { 1, 0 };
        static FakeName nFroz { 2, 0 };
        static FakeTArr pTick { reinterpret_cast<uintptr_t>(&nTick), 1, 1 };
        static FakeTArr pFroz { reinterpret_cast<uintptr_t>(&nFroz), 1, 1 };
        static FakeTArr pNone { 0, 0, 0 };            // a path that was never set

        // The GAME shape: 32 bytes, Path last.
        static uint8_t game[3][32] = {};
        memcpy(game[0] + 16, &pTick, sizeof(pTick));
        memcpy(game[1] + 16, &pFroz, sizeof(pFroz));
        memcpy(game[2] + 16, &pNone, sizeof(pNone));
        static FakeTArr gameArr { reinterpret_cast<uintptr_t>(game), 3, 3 };

        auto g = Ubel::ReadFieldPathArrayElements(
            reinterpret_cast<uintptr_t>(&gameArr), 0, 32, 0, 64);
        check("FIELDPATHARR: a TArray<TFieldPath> is read at all",
              g.ok && g.elements.size() == 3, std::to_string(g.elements.size()).c_str());
        check("FIELDPATHARR ⭐: each element resolves its own path name",
              g.elements.size() == 3 && g.elements[0].value == "TickCount"
                                     && g.elements[1].value == "FrozenInt",
              g.elements.size() == 3 ? g.elements[1].value.c_str() : "(count)");
        check("FIELDPATHARR ⭐: an EMPTY path is (unset), not a name and not a refusal",
              g.elements.size() == 3 && g.elements[2].value == "(unset)",
              g.elements.size() == 3 ? g.elements[2].value.c_str() : "(count)");
        check("FIELDPATHARR: ...each with its index and its raw bytes",
              g.elements.size() == 3 && g.elements[2].index == 2 && g.elements[0].hex.size() == 64);

        // The EDITOR shape: 48 bytes, the same Path at the same distance from the END.
        static uint8_t edit[2][48] = {};
        memcpy(edit[0] + 32, &pTick, sizeof(pTick));
        memcpy(edit[1] + 32, &pFroz, sizeof(pFroz));
        static FakeTArr editArr { reinterpret_cast<uintptr_t>(edit), 2, 2 };
        auto e = Ubel::ReadFieldPathArrayElements(
            reinterpret_cast<uintptr_t>(&editArr), 0, 48, 0, 64);
        check("FIELDPATHARR ⭐: the 48-byte editor shape reads the same names "
              "(Path is found at elemSize-16, not at a pinned offset)",
              e.ok && e.elements.size() == 2 && e.elements[0].value == "TickCount"
                   && e.elements[1].value == "FrozenInt",
              e.elements.size() == 2 ? e.elements[1].value.c_str() : "(count)");

        // A garbage ElementSize must REFUSE, not step by it.
        auto bad = Ubel::ReadFieldPathArrayElements(
            reinterpret_cast<uintptr_t>(&gameArr), 0, 8, 0, 64);
        check("FIELDPATHARR ⭐: an ElementSize too small to hold the path is refused, not walked",
              !bad.ok && bad.elements.empty() && bad.error.find("too small") != std::string::npos,
              bad.error.c_str());

        // An unreadable element is "???" -- never an affirmative "(unset)".
        static FakeTArr deadArr { 0x1000, 2, 2 };     // 0x1000 is never mapped
        auto d = Ubel::ReadFieldPathArrayElements(
            reinterpret_cast<uintptr_t>(&deadArr), 0, 32, 0, 64);
        check("FIELDPATHARR ⭐: an unreadable element is ???, not (unset)",
              d.ok && d.elements.size() == 2 && d.elements[0].value == "???",
              d.elements.empty() ? "(none)" : d.elements[0].value.c_str());

        // A readable element whose Path POINTER is dead: the element is not a name either.
        static FakeTArr pDead { 0x1000, 1, 1 };
        static uint8_t half[32] = {};
        memcpy(half + 16, &pDead, sizeof(pDead));
        static FakeTArr halfArr { reinterpret_cast<uintptr_t>(half), 1, 1 };
        auto h = Ubel::ReadFieldPathArrayElements(
            reinterpret_cast<uintptr_t>(&halfArr), 0, 32, 0, 64);
        check("FIELDPATHARR ⭐: a readable element with an unreadable path is ???, not a name",
              h.ok && h.elements.size() == 1 && h.elements[0].value == "???",
              h.elements.empty() ? "(none)" : h.elements[0].value.c_str());

        check("FIELDPATHARR: the type predicate admits FieldPathProperty and nothing else",
              Ubel::IsFieldPathArrayType("FieldPathProperty")
              && !Ubel::IsFieldPathArrayType("StrProperty")
              && !Ubel::IsFieldPathArrayType("DelegateProperty"));

        DynOff::bCasePreservingName = savedCpnF;
    }

    // -- REFINEOPT-2026-09-12 -- a refine applies the same TOptional gate the first scan did -------
    //
    // [A2-TOPTIONAL-REFINE] (adversarial review 6). The first scan skips a TOptional whose bIsSet byte is 0,
    // but RefineCandidates re-reads every candidate by its stored ABSOLUTE address. UE's MarkUnset clears the
    // flag and zeroes no bytes, so the value is still there: an Unchanged refine kept the row forever and the
    // panel showed a value for a slot the engine considers empty.
    //
    // No pool faking here: refine reads plain process memory through Macht's SEH-guarded readers, so a static
    // buffer IS the object. The session pools are built by hand, which is also the point -- it pins that the
    // gate lives on the DESCRIPTOR, where a refine can still see it.
    {
        blk("REFINEOPT - a refine drops a TOptional that is no longer set");

        static uint8_t roObj[64] = {};
        const uintptr_t roAddr = reinterpret_cast<uintptr_t>(roObj);
        auto putI32 = [](uint8_t* b, int off, int32_t v) { memcpy(b + off, &v, sizeof(v)); };

        // One session: a direct Int32 leaf at the object's start, whose optional flag byte sits at +4.
        std::vector<Radar::InstanceRecord>  roInst(1);
        roInst[0].instanceAddr = roAddr;
        roInst[0].instanceName = "Obj";

        auto refineOnce = [&](int32_t flagOffset, int8_t sentinel, int32_t stored) {
            std::vector<Radar::FieldDescriptor> descs(1);
            descs[0].className          = "C";
            descs[0].fieldName          = "Opt";
            descs[0].fieldType          = "IntProperty";
            descs[0].fieldOffset        = 0;
            descs[0].anchor             = Radar::ValueAnchor::Direct;
            descs[0].optionalFlagOffset = flagOffset;
            descs[0].optionalSentinel   = sentinel;

            std::vector<Radar::Candidate> cands(1);
            cands[0].addr          = roAddr;
            cands[0].descriptorIdx = 0;
            cands[0].instanceIdx   = 0;
            memcpy(cands[0].prevValue, &stored, sizeof(stored));

            Aura::RefineCandidates(Radar::DataType::Int32, Radar::ScanType::Unchanged,
                                   nullptr, nullptr, cands, descs, roInst);
            return cands.size();
        };

        // A. Trailing-flag optional, still SET: the value is unchanged, so the row survives (control).
        putI32(roObj, 0, 100);
        roObj[4] = 1;
        check("REFINEOPT control: a SET optional survives an Unchanged refine", refineOnce(4, 0, 100) == 1);

        // B. The same optional after Reset(): bIsSet cleared, value bytes untouched -- the row must go.
        roObj[4] = 0;
        check("REFINEOPT ⭐: a reset optional is dropped by a refine, not kept on its stale value",
              refineOnce(4, 0, 100) == 0);

        // C. An INTRUSIVE optional has no flag byte. The gate is type-agnostic (it runs before the value is
        //    read), so an FName sentinel over these bytes pins it: ComparisonIndex ~0u means unset.
        putI32(roObj, 0, -1);
        check("REFINEOPT ⭐: an intrusive unset optional is dropped by a refine",
              refineOnce(-1, static_cast<int8_t>(Ubel::OptionalUnsetSentinel::FNameIndexNone), -1) == 0);
        putI32(roObj, 0, 7);
        check("REFINEOPT control: an intrusive SET optional survives",
              refineOnce(-1, static_cast<int8_t>(Ubel::OptionalUnsetSentinel::FNameIndexNone), 7) == 1);

        // D. An ordinary leaf carries no gate at all, and must be untouched by any of this.
        putI32(roObj, 0, 42);
        roObj[4] = 0;
        check("REFINEOPT control: an ordinary leaf refines exactly as before", refineOnce(-1, 0, 42) == 1);
    }

    // -- STRARRAYWALK-2026-09-11 -- WalkInstance hands a string array its elements -------------
    //
    // ⛔ POOL-FAKING, like BOOLNATIVE: the walker picks the ArrayProperty handler by `fi.TypeName` and
    // the inner's type by its FFieldClass name, so both come out of a fake pool. Own pool, last.
    // [W5-STRARRAY-ELEMENTS] The reader above is pinned directly; this pins that the FProperty-mode
    // walk actually CALLS it.
    {
        blk("STRARRAYWALK - WalkInstance reads a TArray<FString>'s elements");

        static uint8_t saEntry[4][0x40] = {};
        const char* saNames[4] = { "", "ArrayProperty", "Names", "StrProperty" };
        static uintptr_t saChunk[5] = {};
        for (int i = 1; i <= 3; ++i) {
            memcpy(saEntry[i] + 0x10, saNames[i], strlen(saNames[i]) + 1);
            saChunk[i] = reinterpret_cast<uintptr_t>(saEntry[i]);
        }
        static uintptr_t saChunks[2] = { reinterpret_cast<uintptr_t>(saChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(saChunks), 0x10);
        check("STRARRAYWALK setup: the pool resolves StrProperty",
              Serie::GetString(3) == "StrProperty", Serie::GetString(3).c_str());

        const bool savedFPropS = DynOff::bUseFProperty;
        DynOff::bUseFProperty = true;

        static uint8_t saArrFC[0x20] = {}, saStrFC[0x20] = {};
        *reinterpret_cast<int32_t*>(saArrFC + DynOff::FFIELDCLASS_NAME) = 1;   // "ArrayProperty"
        *reinterpret_cast<int32_t*>(saStrFC + DynOff::FFIELDCLASS_NAME) = 3;   // "StrProperty"

        static uint8_t saInner[0x100] = {};   // the Inner FProperty
        *reinterpret_cast<uintptr_t*>(saInner + DynOff::FFIELD_CLASS) = reinterpret_cast<uintptr_t>(saStrFC);
        *reinterpret_cast<int32_t*>(saInner + DynOff::FPROPERTY_ELEMSIZE) = 16;

        static uint8_t saProp[0x100] = {};    // the ArrayProperty itself
        *reinterpret_cast<uintptr_t*>(saProp + DynOff::FFIELD_CLASS) = reinterpret_cast<uintptr_t>(saArrFC);
        *reinterpret_cast<int32_t*>(saProp + DynOff::FFIELD_NAME)        = 2;   // "Names"
        *reinterpret_cast<int32_t*>(saProp + DynOff::FPROPERTY_OFFSET)   = 0x40;
        *reinterpret_cast<int32_t*>(saProp + DynOff::FPROPERTY_ELEMSIZE) = 16;
        *reinterpret_cast<int32_t*>(saProp + DynOff::FPROPERTY_ELEMSIZE - 4) = 1;
        *reinterpret_cast<uintptr_t*>(saProp + DynOff::FARRAYPROP_INNER) = reinterpret_cast<uintptr_t>(saInner);

        static uint8_t saCls[0x100] = {};
        *reinterpret_cast<int32_t*>(saCls + DynOff::USTRUCT_PROPSSIZE)    = 0x100;
        *reinterpret_cast<uintptr_t*>(saCls + DynOff::USTRUCT_CHILDPROPS) = reinterpret_cast<uintptr_t>(saProp);

        struct FakeFStringW { uintptr_t Data; int32_t Num; int32_t Max; };
        static const wchar_t* kW[2] = { L"one0", L"two1" };
        static FakeFStringW hdrs[2] = {
            { reinterpret_cast<uintptr_t>(kW[0]), 5, 5 },
            { reinterpret_cast<uintptr_t>(kW[1]), 5, 5 },
        };
        static uint8_t saInst[0x100] = {};
        *reinterpret_cast<uintptr_t*>(saInst + 0x40) = reinterpret_cast<uintptr_t>(hdrs);   // TArray.Data
        *reinterpret_cast<int32_t*>(saInst + 0x48)   = 2;                                    // Num
        *reinterpret_cast<int32_t*>(saInst + 0x4C)   = 2;                                    // Max

        const auto r = Ubel::WalkInstance(reinterpret_cast<uintptr_t>(saInst),
                                          reinterpret_cast<uintptr_t>(saCls), 64, 2, false);
        const Ubel::LiveFieldValue* f = r.fields.size() == 1 ? &r.fields[0] : nullptr;
        check("STRARRAYWALK control: the fake class produced exactly one ArrayProperty of StrProperty",
              f && f->typeName == "ArrayProperty" && f->arrayInnerType == "StrProperty",
              f ? f->arrayInnerType.c_str() : std::to_string(r.fields.size()).c_str());
        check("STRARRAYWALK ⭐: the walk hands the string array its elements, decoded",
              f && f->arrayElements.size() == 2 && f->arrayElements[0].value == "one0"
                && f->arrayElements[1].value == "two1",
              f ? std::to_string(f->arrayElements.size()).c_str() : "(no field)");

        DynOff::bUseFProperty = savedFPropS;
    }

    // -- XREFCAPWALK-2026-09-11 -- FindPropertyXrefs publishes the cap it hit -------------------------------
    //
    // ⛔ POOL-FAKING, like the blocks above: FindPropertyXrefs keeps an object only if its class is NAMED
    // "Function". Own name pool, last. The five fake UFunctions live in the MAIN object pool (Aura was initialised
    // on it at the top), at indices 0..4 -- all inside worker 0's contiguous range, so cap 3 stops that worker
    // early. [W3-XREF-CAP]
    {
        blk("XREFCAPWALK - FindPropertyXrefs publishes the cap it hit, and only when it hit it");
        ResetCancel();

        static uint8_t xcEntry[3][0x40] = {};
        const char* xcNames[3] = { "", "Function", "Fn" };
        static uintptr_t xcChunk[4] = {};
        for (int i = 1; i <= 2; ++i) {
            memcpy(xcEntry[i] + 0x10, xcNames[i], strlen(xcNames[i]) + 1);
            xcChunk[i] = reinterpret_cast<uintptr_t>(xcEntry[i]);
        }
        static uintptr_t xcChunks[2] = { reinterpret_cast<uintptr_t>(xcChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(xcChunks), 0x10);
        check("XREFCAPWALK setup: the pool resolves Function", Serie::GetString(1) == "Function",
              Serie::GetString(1).c_str());

        const int savedScript = DynOff::USTRUCT_SCRIPT;
        DynOff::USTRUCT_SCRIPT = 0x30;   // the pool's objects are 64 bytes: clear of class, name and outer

        constexpr uintptr_t kNeedle = 0x1122334455667788ull;   // the "FProperty*" searched for; never dereferenced
        alignas(8) static uint8_t xcScript[16];
        memset(xcScript, 0x0B, sizeof(xcScript));
        xcScript[0] = 0x01;                                    // EX_InstanceVariable, then the pointer
        memcpy(xcScript + 1, &kNeedle, sizeof(kNeedle));
        static uint8_t xcFnClass[0x40] = {};
        *reinterpret_cast<int32_t*>(xcFnClass + Grimoire::OFF_UOBJECT_NAME) = 1;   // "Function"

        for (int i = 0; i < 5; ++i) {
            uint8_t* o = pool.objects.data() + static_cast<size_t>(i) * 64;
            memset(o, 0, 64);   // no earlier block's leftovers (an Outer, a class) in the scan
            *reinterpret_cast<uintptr_t*>(o + Grimoire::OFF_UOBJECT_CLASS) = reinterpret_cast<uintptr_t>(xcFnClass);
            *reinterpret_cast<int32_t*>(o + Grimoire::OFF_UOBJECT_NAME)    = 2;   // "Fn"
            *reinterpret_cast<uintptr_t*>(o + 0x30) = reinterpret_cast<uintptr_t>(xcScript);
            *reinterpret_cast<int32_t*>(o + 0x38)   = static_cast<int32_t>(sizeof(xcScript));
        }

        const auto whole  = Aura::FindPropertyXrefs(kNeedle, false, 10);
        const auto capped = Aura::FindPropertyXrefs(kNeedle, false, 3);
        check("XREFCAPWALK control: all five fake UFunctions are found under a cap they fit in",
              whole.xrefs.size() == 5, std::to_string(whole.xrefs.size()).c_str());
        check("XREFCAPWALK control: ...and that scan is not capped", !whole.stats.capHit);
        check("XREFCAPWALK: cap 3 returns the first three", capped.xrefs.size() == 3,
              std::to_string(capped.xrefs.size()).c_str());
        check("XREFCAPWALK ⭐: FindPropertyXrefs publishes the cap it hit, and the cap itself",
              capped.stats.capHit && capped.stats.cap == 3);
        check("XREFCAPWALK control: a cap is not a deadline", !capped.stats.deadlineHit);

        for (int i = 0; i < 5; ++i) memset(pool.objects.data() + static_cast<size_t>(i) * 64, 0, 64);
        DynOff::USTRUCT_SCRIPT = savedScript;
    }

    // -- RELSTOPS-2026-09-11 -- GetRelatedObjects says why it stopped short -----------------------------------
    //
    // ⛔ POOL-FAKING, like OPTLAYOUT: the owned walk reaches children through EnumerateOutgoingObjectPtrs, whose
    // ref-meta comes from the class walker, which names property types out of a fake name pool. Own pool, last.
    // One target whose class holds a TArray<UObject*> "Parts" of four children; each child is Outer'd to the
    // target (so IsOwnedBy keeps it) and has a field-less class. [W4-RELATED-STOPS]
    {
        blk("RELSTOPS - GetRelatedObjects records each cause it stopped for, and only when it refused something");
        ResetCancel();

        static uint8_t rsEntry[7][0x40] = {};
        const char* rsNames[7] = { "", "ArrayProperty", "ObjectProperty", "Parts", "Inner",
                                   "BP_Holder_C", "PartComponent" };
        static uintptr_t rsChunk[8] = {};
        for (int i = 1; i <= 6; ++i) {
            memcpy(rsEntry[i] + 0x10, rsNames[i], strlen(rsNames[i]) + 1);
            rsChunk[i] = reinterpret_cast<uintptr_t>(rsEntry[i]);
        }
        static uintptr_t rsChunks[2] = { reinterpret_cast<uintptr_t>(rsChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(rsChunks), 0x10);
        check("RELSTOPS setup: the pool resolves ObjectProperty", Serie::GetString(2) == "ObjectProperty",
              Serie::GetString(2).c_str());

        const bool     savedFPropR = DynOff::bUseFProperty;
        const bool     savedCpnR   = DynOff::bCasePreservingName;
        const uint32_t savedVerR   = g_cachedUEVersion;
        DynOff::bUseFProperty       = true;
        DynOff::bCasePreservingName = false;
        g_cachedUEVersion           = 505;

        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };

        static uint8_t rsArrFC[0x20] = {}, rsObjFC[0x20] = {};
        put32(rsArrFC, DynOff::FFIELDCLASS_NAME, 1);   // "ArrayProperty"
        put32(rsObjFC, DynOff::FFIELDCLASS_NAME, 2);   // "ObjectProperty"

        static uint8_t rsInner[0x100] = {};            // the array's Inner: an 8-byte ObjectProperty
        putP(rsInner, DynOff::FFIELD_CLASS, reinterpret_cast<uintptr_t>(rsObjFC));
        put32(rsInner, DynOff::FFIELD_NAME, 4);
        put32(rsInner, DynOff::FPROPERTY_ELEMSIZE, 8);

        constexpr int32_t kParts = 0x40;
        static uint8_t rsProp[0x100] = {};             // TArray<UObject*> Parts @ +0x40
        putP(rsProp, DynOff::FFIELD_CLASS, reinterpret_cast<uintptr_t>(rsArrFC));
        put32(rsProp, DynOff::FFIELD_NAME, 3);
        put32(rsProp, DynOff::FPROPERTY_OFFSET, kParts);
        put32(rsProp, DynOff::FPROPERTY_ELEMSIZE, 16);
        put32(rsProp, DynOff::FPROPERTY_ELEMSIZE - 4, 1);
        putP(rsProp, DynOff::FARRAYPROP_INNER, reinterpret_cast<uintptr_t>(rsInner));

        static uint8_t rsHolderCls[0x100] = {}, rsPartCls[0x100] = {};
        put32(rsHolderCls, DynOff::USTRUCT_PROPSSIZE, 0x100);
        putP(rsHolderCls, DynOff::USTRUCT_CHILDPROPS, reinterpret_cast<uintptr_t>(rsProp));
        put32(rsHolderCls, Grimoire::OFF_UOBJECT_NAME, 5);   // "BP_Holder_C"
        put32(rsPartCls, Grimoire::OFF_UOBJECT_NAME, 6);     // "PartComponent"

        static uint8_t rsTarget[0x100] = {};
        static uint8_t rsPart[4][0x100] = {};
        static uintptr_t rsParts[4] = {};
        putP(rsTarget, Grimoire::OFF_UOBJECT_CLASS, reinterpret_cast<uintptr_t>(rsHolderCls));
        for (int i = 0; i < 4; ++i) {
            putP(rsPart[i], Grimoire::OFF_UOBJECT_CLASS, reinterpret_cast<uintptr_t>(rsPartCls));
            putP(rsPart[i], DynOff::UOBJECT_OUTER, reinterpret_cast<uintptr_t>(rsTarget));
            rsParts[i] = reinterpret_cast<uintptr_t>(rsPart[i]);
        }
        putP(rsTarget, kParts, reinterpret_cast<uintptr_t>(rsParts));   // TArray.Data
        put32(rsTarget, kParts + 8, 4);                                   // Num
        put32(rsTarget, kParts + 12, 4);                                  // Max
        const uintptr_t rsT = reinterpret_cast<uintptr_t>(rsTarget);
        const Aura::RelatedObjectsLimits dflt;

        Aura::RelatedObjectsStats s0;
        const auto all = Aura::GetRelatedObjects(rsT, 128, &s0, dflt);
        check("RELSTOPS control: Self, Class and the four owned parts", all.size() == 6,
              std::to_string(all.size()).c_str());
        check("RELSTOPS control: a walk that finished records no stop",
              !s0.resultCapHit && !s0.ownedCapHit && !s0.visitCapHit && !s0.deadlineHit && !s0.cancelled);

        Aura::RelatedObjectsStats sFit;
        const auto fit = Aura::GetRelatedObjects(rsT, 6, &sFit, dflt);
        check("RELSTOPS control: a list that exactly fills its cap is complete, not cut off",
              fit.size() == 6 && !sFit.resultCapHit, std::to_string(fit.size()).c_str());

        Aura::RelatedObjectsStats sCap;
        const auto cap = Aura::GetRelatedObjects(rsT, 3, &sCap, dflt);
        check("RELSTOPS: cap 3 keeps Self, Class and one part", cap.size() == 3,
              std::to_string(cap.size()).c_str());
        check("RELSTOPS ⭐: a refused part records the row cap, and the cap itself",
              sCap.resultCapHit && sCap.maxResults == 3);
        check("RELSTOPS control: ...and no other cause",
              !sCap.ownedCapHit && !sCap.visitCapHit && !sCap.deadlineHit && !sCap.cancelled);

        // A PART as the target: its class has no fields, so it owns nothing, and the Class row refused at cap 1
        // is the ONLY refusal -- add() alone must record it. (With the Parts target the owned walk refuses a
        // part too and sets the same flag, which hid a broken add(): the review mutant survived that way.)
        Aura::RelatedObjectsStats sOne;
        const auto one = Aura::GetRelatedObjects(reinterpret_cast<uintptr_t>(rsPart[0]), 1, &sOne, dflt);
        check("RELSTOPS ⭐: a refused hierarchy row (the Class) records the row cap too",
              one.size() == 1 && sOne.resultCapHit, std::to_string(one.size()).c_str());

        Aura::RelatedObjectsLimits ownLim;  ownLim.maxOwnedSubs = 2;
        Aura::RelatedObjectsStats sOwn;
        const auto own = Aura::GetRelatedObjects(rsT, 128, &sOwn, ownLim);
        check("RELSTOPS: two parts under an owned cap of 2", own.size() == 4, std::to_string(own.size()).c_str());
        check("RELSTOPS ⭐: the owned-object cap is its own cause",
              sOwn.ownedCapHit && !sOwn.resultCapHit && sOwn.maxOwnedSubs == 2);

        Aura::RelatedObjectsLimits visLim;  visLim.maxVisited = 2;
        Aura::RelatedObjectsStats sVis;
        const auto vis = Aura::GetRelatedObjects(rsT, 128, &sVis, visLim);
        check("RELSTOPS: two pointers followed under a visit budget of 2", vis.size() == 4,
              std::to_string(vis.size()).c_str());
        check("RELSTOPS ⭐: the pointer budget is its own cause",
              sVis.visitCapHit && !sVis.ownedCapHit && !sVis.resultCapHit);

        Aura::RelatedObjectsLimits dlLim;  dlLim.deadlineMs = -1;   // already past it at the first check
        Aura::RelatedObjectsStats sDl;
        const auto dl = Aura::GetRelatedObjects(rsT, 128, &sDl, dlLim);
        check("RELSTOPS: a spent deadline keeps only the hierarchy rows", dl.size() == 2,
              std::to_string(dl.size()).c_str());
        check("RELSTOPS ⭐: the deadline is its own cause, not a cancel", sDl.deadlineHit && !sDl.cancelled);

        Aura::RelatedObjectsStats sCx;
        Tot::g_perCommand.store(true);
        const auto cx = Aura::GetRelatedObjects(rsT, 128, &sCx, dflt);
        ResetCancel();
        check("RELSTOPS: a cancelled walk keeps only the hierarchy rows", cx.size() == 2,
              std::to_string(cx.size()).c_str());
        check("RELSTOPS ⭐: a cancel is its own cause, not a deadline", sCx.cancelled && !sCx.deadlineHit);

        // Review 4 of d6a3af46: the caps are asked only of an object that QUALIFIES, so an edge that fails the filters
        // AFTER the list is full is no refusal. The target above never produces one -- its parts own nothing and each
        // of its edges qualifies -- so the old order passed every check here. A second holder's Parts array ends with
        // a fifth element it does NOT own (no Outer), enumerated after the four parts that fill the list. (Not a
        // separate pointer field: the enumerator emits direct pointers before array elements, so it came first.)
        static uint8_t rsHolder2Cls[0x100] = {};
        put32(rsHolder2Cls, DynOff::USTRUCT_PROPSSIZE, 0x100);
        putP(rsHolder2Cls, DynOff::USTRUCT_CHILDPROPS, reinterpret_cast<uintptr_t>(rsProp));
        put32(rsHolder2Cls, Grimoire::OFF_UOBJECT_NAME, 5);
        static uint8_t rsTarget2[0x100] = {}, rsStranger[0x100] = {};
        static uint8_t rsPart2[4][0x100] = {};
        static uintptr_t rsParts2[5] = {};
        putP(rsTarget2, Grimoire::OFF_UOBJECT_CLASS, reinterpret_cast<uintptr_t>(rsHolder2Cls));
        for (int i = 0; i < 4; ++i) {
            putP(rsPart2[i], Grimoire::OFF_UOBJECT_CLASS, reinterpret_cast<uintptr_t>(rsPartCls));
            putP(rsPart2[i], DynOff::UOBJECT_OUTER, reinterpret_cast<uintptr_t>(rsTarget2));
            rsParts2[i] = reinterpret_cast<uintptr_t>(rsPart2[i]);
        }
        putP(rsStranger, Grimoire::OFF_UOBJECT_CLASS, reinterpret_cast<uintptr_t>(rsPartCls));   // no Outer: not owned
        rsParts2[4] = reinterpret_cast<uintptr_t>(rsStranger);
        putP(rsTarget2, kParts, reinterpret_cast<uintptr_t>(rsParts2));   // TArray.Data
        put32(rsTarget2, kParts + 8, 5);                                   // Num
        put32(rsTarget2, kParts + 12, 5);                                  // Max
        const uintptr_t rsT2 = reinterpret_cast<uintptr_t>(rsTarget2);

        Aura::RelatedObjectsStats sAll2;
        const auto all2 = Aura::GetRelatedObjects(rsT2, 128, &sAll2, dflt);
        check("RELSTOPS setup: the second holder relates Self, Class and its four parts, never the stranger",
              all2.size() == 6 && !sAll2.resultCapHit && !sAll2.ownedCapHit, std::to_string(all2.size()).c_str());
        Aura::RelatedObjectsStats sFit2;
        const auto fit2 = Aura::GetRelatedObjects(rsT2, 6, &sFit2, dflt);
        check("RELSTOPS ⭐: a non-qualifying edge after the list is full is not a refusal (no row cap)",
              fit2.size() == 6 && !sFit2.resultCapHit, std::to_string(fit2.size()).c_str());
        Aura::RelatedObjectsLimits own4;  own4.maxOwnedSubs = 4;
        Aura::RelatedObjectsStats sOwn4;
        const auto ownFit = Aura::GetRelatedObjects(rsT2, 128, &sOwn4, own4);
        check("RELSTOPS ⭐: ...nor after the owned budget is spent (no owned cap)",
              ownFit.size() == 6 && !sOwn4.ownedCapHit, std::to_string(ownFit.size()).c_str());

        DynOff::bUseFProperty       = savedFPropR;
        DynOff::bCasePreservingName = savedCpnR;
        g_cachedUEVersion           = savedVerR;
    }

    // -- STRIDEVERDICT-2026-09-12 -- DetectItemSize publishes its verdict, and resets at entry --------------
    //
    // ⛔ RE-INITIALISES AURA on throwaway arrays, so it runs LAST and puts the main pool back at the end. The
    // "named" probe needs a live name pool: this relies on RELSTOPS' (index 2 resolves). [W4-STRIDE-TENTATIVE]
    {
        blk("STRIDEVERDICT - detected / tentative / undetected / forced reach the accessors; a re-init resets first");
        ResetCancel();
        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };

        Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);
        check("STRIDEVERDICT ⭐: the fixture's forced stride reads \"forced\"",
              strcmp(Aura::GetItemDetect(), "forced") == 0, Aura::GetItemDetect());

        // A class whose class-of-class is itself, so LooksLikeUObject accepts an object of it.
        static uint8_t svCls[0x40] = {};
        putP(svCls, Grimoire::OFF_UOBJECT_CLASS, reinterpret_cast<uintptr_t>(svCls));
        const uintptr_t svClsP = reinterpret_cast<uintptr_t>(svCls);
        // A one-chunk array header in the forced extended shape: Objects@+0x10, Max/Num@+0x20/+0x24, chunks.
        auto header = [&](uint8_t* hdr, uintptr_t* table, uint8_t* chunk) {
            table[0] = reinterpret_cast<uintptr_t>(chunk);
            putP(hdr, 0x10, reinterpret_cast<uintptr_t>(table));
            put32(hdr, 0x20, 64);  put32(hdr, 0x24, 64);
            put32(hdr, 0x28, 1);   put32(hdr, 0x2C, 1);
            return reinterpret_cast<uintptr_t>(hdr);
        };

        // DETECTED: five named objects at the first five slots of a 16-byte-stride chunk, the rest null.
        static uint8_t svObjs[5][0x40] = {};
        static uint8_t svDetChunk[0x4000] = {};                 // 200 probes x the widest stride (40) fit
        static uintptr_t svDetTable[2] = {};
        static uint8_t svDetHdr[0x40] = {};
        for (int i = 0; i < 5; ++i) {
            putP(svObjs[i], Grimoire::OFF_UOBJECT_CLASS, svClsP);
            put32(svObjs[i], Grimoire::OFF_UOBJECT_NAME, 2);
            putP(svDetChunk, i * 16, reinterpret_cast<uintptr_t>(svObjs[i]));
        }
        Aura::InitWithExtendedLayout(header(svDetHdr, svDetTable, svDetChunk), 0);
        check("STRIDEVERDICT ⭐: five clean items at stride 16 are a DETECTED stride, with all five validated",
              strcmp(Aura::GetItemDetect(), "detected") == 0 && Aura::GetItemDetectValidated() == 5,
              Aura::GetItemDetect());
        check("STRIDEVERDICT control: ...found by P1, so of its 200 probes",
              Aura::GetItemDetectProbes() == 200, std::to_string(Aura::GetItemDetectProbes()).c_str());

        // TENTATIVE: ONE valid object among nulls. Every stride validates exactly it, which clears no gate.
        static uint8_t svTenChunk[0x4000] = {};
        static uintptr_t svTenTable[2] = {};
        static uint8_t svTenHdr[0x40] = {};
        putP(svTenChunk, 0, reinterpret_cast<uintptr_t>(svObjs[0]));
        Aura::InitWithExtendedLayout(header(svTenHdr, svTenTable, svTenChunk), 0);
        check("STRIDEVERDICT ⭐: one validated item of 200 is a TENTATIVE stride, and says so with its count",
              strcmp(Aura::GetItemDetect(), "tentative") == 0 && Aura::GetItemDetectValidated() == 1,
              Aura::GetItemDetect());

        // DEEP ([W4-STRIDE-TENTATIVE] review 4): the first 200 slots are null at every stride, so P1 finds nothing
        // and P2-deep probes 100 items from item 1000 (x24). A pass won there must publish ITS probe count -- the
        // badge divides by it -- not P1's 200. Chunk and table are both 0x7000 bytes, so no phase reads past them.
        static uint8_t svDeepChunk[0x7000] = {};
        static uintptr_t svDeepTable[0x7000 / 8] = {};
        static uint8_t svDeepHdr[0x40] = {};
        for (int i = 0; i < 5; ++i)
            putP(svDeepChunk, 1000 * 24 + i * 16, reinterpret_cast<uintptr_t>(svObjs[i]));
        Aura::InitWithExtendedLayout(header(svDeepHdr, svDeepTable, svDeepChunk), 0);
        check("STRIDEVERDICT setup: five items deep in the chunk are DETECTED, all five validated",
              strcmp(Aura::GetItemDetect(), "detected") == 0 && Aura::GetItemDetectValidated() == 5,
              Aura::GetItemDetect());
        check("STRIDEVERDICT ⭐: a pass won in a deep phase publishes ITS 100 probes, not P1's 200",
              Aura::GetItemDetectProbes() == 100, std::to_string(Aura::GetItemDetectProbes()).c_str());

        // FORCED right after it: the caller fixed the stride, so nothing was probed.
        Aura::InitWithExtendedLayout(header(svDeepHdr, svDeepTable, svDeepChunk), 16);
        check("STRIDEVERDICT ⭐: a FORCED stride probed nothing: 0 probes, not the previous run's 100",
              strcmp(Aura::GetItemDetect(), "forced") == 0 && Aura::GetItemDetectProbes() == 0,
              std::to_string(Aura::GetItemDetectProbes()).c_str());

        // TENTATIVE-DEEP: one object deep in the chunk clears no gate, so the weak fallback reports it -- and its
        // badge must read "1 of 100", not "1 of 200".
        static uint8_t svDeepTenChunk[0x7000] = {};
        static uintptr_t svDeepTenTable[0x7000 / 8] = {};
        static uint8_t svDeepTenHdr[0x40] = {};
        putP(svDeepTenChunk, 1000 * 24, reinterpret_cast<uintptr_t>(svObjs[0]));
        Aura::InitWithExtendedLayout(header(svDeepTenHdr, svDeepTenTable, svDeepTenChunk), 0);
        check("STRIDEVERDICT ⭐: a tentative stride from a deep phase reads 1 of 100 probes",
              strcmp(Aura::GetItemDetect(), "tentative") == 0 && Aura::GetItemDetectValidated() == 1
              && Aura::GetItemDetectProbes() == 100,
              (std::string(Aura::GetItemDetect()) + " " + std::to_string(Aura::GetItemDetectValidated()) + " of "
               + std::to_string(Aura::GetItemDetectProbes())).c_str());

        // UNDETECTED, and the reset: a previous run left packed mode on; this re-init cannot even read its
        // chunk table (Objects == 0), so it returns at the first early return.
        Aura::SetPackedConsts(0, 0, true, -1);
        check("STRIDEVERDICT setup: packed mode is on before the re-init", Aura::IsPacked());
        static uint8_t svBadHdr[0x40] = {};
        Aura::InitWithExtendedLayout(reinterpret_cast<uintptr_t>(svBadHdr), 0);
        check("STRIDEVERDICT ⭐: a re-init that cannot read its chunk table resets the layout (reset at ENTRY)",
              !Aura::IsPacked());
        check("STRIDEVERDICT ⭐: ...and reads \"undetected\", not the previous run's verdict",
              strcmp(Aura::GetItemDetect(), "undetected") == 0, Aura::GetItemDetect());

        check("STRIDEVERDICT ⭐: ...and publishes 0 probes, not the previous run's 100",
              Aura::GetItemDetectProbes() == 0, std::to_string(Aura::GetItemDetectProbes()).c_str());

        // The rest of the reset ([W4-STRIDE-TENTATIVE] review 4). The runs above leave the stride at 16, the object
        // offset at +0x00 and a validated count nothing reads -- the very values the reset writes -- so dropping any of
        // those three resets passed. This fixture leaves all three unlike the defaults: five objects at stride 20
        // with the pointer at +0x08, which the classic pass cannot read and the +0x08 pass detects. Chunk and table
        // are both 0x7000 bytes, so no deep or flat phase of either pass reads past them.
        static uint8_t svOffChunk[0x7000] = {};
        static uintptr_t svOffTable[0x7000 / 8] = {};
        static uint8_t svOffHdr[0x40] = {};
        for (int i = 0; i < 5; ++i)
            putP(svOffChunk, 8 + i * 20, reinterpret_cast<uintptr_t>(svObjs[i]));
        Aura::InitWithExtendedLayout(header(svOffHdr, svOffTable, svOffChunk), 0);
        check("STRIDEVERDICT setup: five items at stride 20, object at +0x08, are DETECTED as exactly that",
              strcmp(Aura::GetItemDetect(), "detected") == 0 && Aura::GetItemSize() == 20
              && Aura::GetItemObjOffset() == 8 && Aura::GetItemDetectValidated() == 5,
              (std::string(Aura::GetItemDetect()) + " stride " + std::to_string(Aura::GetItemSize()) + " off "
               + std::to_string(Aura::GetItemObjOffset()) + " n " + std::to_string(Aura::GetItemDetectValidated())).c_str());
        Aura::InitWithExtendedLayout(reinterpret_cast<uintptr_t>(svBadHdr), 0);
        check("STRIDEVERDICT ⭐: the unreadable re-init puts the stride back to the default 16",
              Aura::GetItemSize() == 16, std::to_string(Aura::GetItemSize()).c_str());
        check("STRIDEVERDICT ⭐: ...the object-pointer offset back to +0x00",
              Aura::GetItemObjOffset() == 0, std::to_string(Aura::GetItemObjOffset()).c_str());
        check("STRIDEVERDICT ⭐: ...and the validated count back to 0",
              Aura::GetItemDetectValidated() == 0, std::to_string(Aura::GetItemDetectValidated()).c_str());

        Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);
        check("STRIDEVERDICT control: the main pool is back, classic", !Aura::IsPacked() && Aura::GetCount() == kCount);
    }

    // -- STRARRAYSTRIDE-2026-09-12 -- a string array PUBLISHES the 16-byte stride its reader uses --------------
    //
    // ⛔ POOL-FAKING, like STRARRAYWALK: own pool, last. Review 3 of 6b48e776: the reader pinned the header
    // stride, but the walk still published the inner's raw ELEMSIZE -- and the UI lays element rows out by
    // that (Offset = i * ArrayElemSize) and hands it back to read_array_elements. [W5-STRARRAY-ELEMENTS]
    {
        blk("STRARRAYSTRIDE - WalkInstance publishes a string array's 16-byte stride, whatever the inner says");

        static uint8_t ssEntry[4][0x40] = {};
        const char* ssNames[4] = { "", "ArrayProperty", "Names", "StrProperty" };
        static uintptr_t ssChunk[5] = {};
        for (int i = 1; i <= 3; ++i) {
            memcpy(ssEntry[i] + 0x10, ssNames[i], strlen(ssNames[i]) + 1);
            ssChunk[i] = reinterpret_cast<uintptr_t>(ssEntry[i]);
        }
        static uintptr_t ssChunks[2] = { reinterpret_cast<uintptr_t>(ssChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(ssChunks), 0x10);

        const bool savedFPropSS = DynOff::bUseFProperty;
        DynOff::bUseFProperty = true;

        static uint8_t ssArrFC[0x20] = {}, ssStrFC[0x20] = {};
        *reinterpret_cast<int32_t*>(ssArrFC + DynOff::FFIELDCLASS_NAME) = 1;   // "ArrayProperty"
        *reinterpret_cast<int32_t*>(ssStrFC + DynOff::FFIELDCLASS_NAME) = 3;   // "StrProperty"

        static uint8_t ssInner[0x100] = {};   // the Inner FProperty, whose ELEMSIZE reads as garbage
        *reinterpret_cast<uintptr_t*>(ssInner + DynOff::FFIELD_CLASS) = reinterpret_cast<uintptr_t>(ssStrFC);
        *reinterpret_cast<int32_t*>(ssInner + DynOff::FPROPERTY_ELEMSIZE) = 0x18;   // in range, so not zeroed

        static uint8_t ssProp[0x100] = {};    // the ArrayProperty itself
        *reinterpret_cast<uintptr_t*>(ssProp + DynOff::FFIELD_CLASS) = reinterpret_cast<uintptr_t>(ssArrFC);
        *reinterpret_cast<int32_t*>(ssProp + DynOff::FFIELD_NAME)        = 2;   // "Names"
        *reinterpret_cast<int32_t*>(ssProp + DynOff::FPROPERTY_OFFSET)   = 0x40;
        *reinterpret_cast<int32_t*>(ssProp + DynOff::FPROPERTY_ELEMSIZE) = 16;
        *reinterpret_cast<int32_t*>(ssProp + DynOff::FPROPERTY_ELEMSIZE - 4) = 1;
        *reinterpret_cast<uintptr_t*>(ssProp + DynOff::FARRAYPROP_INNER) = reinterpret_cast<uintptr_t>(ssInner);

        static uint8_t ssCls[0x100] = {};
        *reinterpret_cast<int32_t*>(ssCls + DynOff::USTRUCT_PROPSSIZE)    = 0x100;
        *reinterpret_cast<uintptr_t*>(ssCls + DynOff::USTRUCT_CHILDPROPS) = reinterpret_cast<uintptr_t>(ssProp);

        struct FakeFStringW { uintptr_t Data; int32_t Num; int32_t Max; };
        static const wchar_t* kSW[2] = { L"one0", L"two1" };
        static FakeFStringW ssHdrs[2] = {
            { reinterpret_cast<uintptr_t>(kSW[0]), 5, 5 },
            { reinterpret_cast<uintptr_t>(kSW[1]), 5, 5 },
        };
        static uint8_t ssInst[0x100] = {};
        *reinterpret_cast<uintptr_t*>(ssInst + 0x40) = reinterpret_cast<uintptr_t>(ssHdrs);   // TArray.Data
        *reinterpret_cast<int32_t*>(ssInst + 0x48)   = 2;                                      // Num
        *reinterpret_cast<int32_t*>(ssInst + 0x4C)   = 2;                                      // Max

        const auto r = Ubel::WalkInstance(reinterpret_cast<uintptr_t>(ssInst),
                                          reinterpret_cast<uintptr_t>(ssCls), 64, 2, false);
        const Ubel::LiveFieldValue* f = r.fields.size() == 1 ? &r.fields[0] : nullptr;
        check("STRARRAYSTRIDE control: the pinned reader still decodes both elements",
              f && f->arrayElements.size() == 2 && f->arrayElements[1].value == "two1",
              f ? std::to_string(f->arrayElements.size()).c_str() : "(no field)");
        check("STRARRAYSTRIDE ⭐: the published element size is the 16-byte header, not the inner's 0x18",
              f && f->arrayElemSize == 16, f ? std::to_string(f->arrayElemSize).c_str() : "(no field)");

        DynOff::bUseFProperty = savedFPropSS;
    }

    // -- CONTAINERENUM-2026-09-12 -- a container inner's UEnum reaches FieldInfo -------------------------------
    //
    // ⛔ POOL-FAKING (own pool, last). [A4-USMAP-CONTAINER-ENUM] The walker named a container inner's type, struct and
    // object class, never its enum -- so USMAP wrote a TArray<TEnumAsByte<E>> as a bare byte array.
    {
        blk("CONTAINERENUM - WalkClassEx publishes a container inner's UEnum (Array / Set / Map)");

        static uint8_t ceEntry[13][0x40] = {};
        const char* ceNames[13] = { "", "ArrayProperty", "SetProperty", "MapProperty", "ByteProperty",
                                    "EnumProperty", "IntProperty", "Items", "Tags", "Lookup", "EMyEnum", "EOther",
                                    "Enum" };
        static uintptr_t ceChunk[14] = {};
        for (int i = 1; i <= 12; ++i) {
            memcpy(ceEntry[i] + 0x10, ceNames[i], strlen(ceNames[i]) + 1);
            ceChunk[i] = reinterpret_cast<uintptr_t>(ceEntry[i]);
        }
        static uintptr_t ceChunks[2] = { reinterpret_cast<uintptr_t>(ceChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(ceChunks), 0x10);

        const bool savedFPropCE = DynOff::bUseFProperty;
        DynOff::bUseFProperty = true;
        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };

        static uint8_t ceFC[7][0x20] = {};
        auto fclass = [&](int nameIdx) {
            put32(ceFC[nameIdx], DynOff::FFIELDCLASS_NAME, nameIdx);
            return reinterpret_cast<uintptr_t>(ceFC[nameIdx]);
        };
        static uint8_t ceEnumA[0x40] = {}, ceEnumB[0x40] = {};              // the two UEnum objects
        put32(ceEnumA, Grimoire::OFF_UOBJECT_NAME, 10);                      // "EMyEnum"
        put32(ceEnumB, Grimoire::OFF_UOBJECT_NAME, 11);                      // "EOther"
        // ...of class Enum: an enum slot is read only when it holds a UEnum ([ENUMSLOT-ANY-NAME]).
        static uint8_t ceEnumCls[0x40] = {};
        put32(ceEnumCls, Grimoire::OFF_UOBJECT_NAME, 12);
        putP(ceEnumA, Grimoire::OFF_UOBJECT_CLASS, reinterpret_cast<uintptr_t>(ceEnumCls));
        putP(ceEnumB, Grimoire::OFF_UOBJECT_CLASS, reinterpret_cast<uintptr_t>(ceEnumCls));

        // Inners: a TEnumAsByte (ByteProperty + Enum), an EnumProperty (+ Enum), and a plain IntProperty.
        static uint8_t ceByteInner[0x100] = {}, ceEnumInner[0x100] = {}, ceByteKey[0x100] = {}, ceIntVal[0x100] = {};
        putP(ceByteInner, DynOff::FFIELD_CLASS, fclass(4));
        putP(ceByteInner, DynOff::FBYTEPROP_ENUM, reinterpret_cast<uintptr_t>(ceEnumA));
        putP(ceEnumInner, DynOff::FFIELD_CLASS, fclass(5));
        putP(ceEnumInner, DynOff::FENUMPROP_ENUM, reinterpret_cast<uintptr_t>(ceEnumB));
        putP(ceByteKey, DynOff::FFIELD_CLASS, fclass(4));
        putP(ceByteKey, DynOff::FBYTEPROP_ENUM, reinterpret_cast<uintptr_t>(ceEnumA));
        putP(ceIntVal, DynOff::FFIELD_CLASS, fclass(6));

        // The three containers, chained through FFIELD_NEXT.
        static uint8_t ceArr[0x100] = {}, ceSet[0x100] = {}, ceMap[0x100] = {};
        auto prop = [&](uint8_t* p, int fcIdx, int nameIdx, int32_t off, int32_t size, uint8_t* next) {
            putP(p, DynOff::FFIELD_CLASS, fclass(fcIdx));
            put32(p, DynOff::FFIELD_NAME, nameIdx);
            put32(p, DynOff::FPROPERTY_OFFSET, off);
            put32(p, DynOff::FPROPERTY_ELEMSIZE, size);
            put32(p, DynOff::FPROPERTY_ELEMSIZE - 4, 1);
            putP(p, DynOff::FFIELD_NEXT, reinterpret_cast<uintptr_t>(next));
        };
        prop(ceArr, 1, 7, 0x40, 16, ceSet);
        putP(ceArr, DynOff::FARRAYPROP_INNER, reinterpret_cast<uintptr_t>(ceByteInner));
        prop(ceSet, 2, 8, 0x50, 0x50, ceMap);
        putP(ceSet, DynOff::FARRAYPROP_INNER, reinterpret_cast<uintptr_t>(ceEnumInner));
        prop(ceMap, 3, 9, 0xA0, 0x50, nullptr);
        putP(ceMap, DynOff::FSTRUCTPROP_STRUCT, reinterpret_cast<uintptr_t>(ceByteKey));
        putP(ceMap, DynOff::FSTRUCTPROP_STRUCT + 8, reinterpret_cast<uintptr_t>(ceIntVal));

        static uint8_t ceCls[0x100] = {};
        put32(ceCls, DynOff::USTRUCT_PROPSSIZE, 0x100);
        putP(ceCls, DynOff::USTRUCT_CHILDPROPS, reinterpret_cast<uintptr_t>(ceArr));

        const auto& ceInfo = Ubel::WalkClassEx(reinterpret_cast<uintptr_t>(ceCls));
        const auto& F = ceInfo.Fields;
        int ia = -1, is = -1, im = -1;
        for (int i = 0; i < static_cast<int>(F.size()); ++i) {
            if (F[i].Name == "Items") ia = i; else if (F[i].Name == "Tags") is = i; else if (F[i].Name == "Lookup") im = i;
        }
        check("CONTAINERENUM control: the fake class produced its Array, Set and Map with their inner types",
              ia >= 0 && is >= 0 && im >= 0 && F[ia].innerType == "ByteProperty" && F[is].elemType == "EnumProperty"
                && F[im].keyType == "ByteProperty" && F[im].valueType == "IntProperty",
              std::to_string(F.size()).c_str());
        check("CONTAINERENUM ⭐: an Array's TEnumAsByte inner publishes its enum",
              ia >= 0 && F[ia].innerEnumName == "EMyEnum", ia >= 0 ? F[ia].innerEnumName.c_str() : "(no field)");
        check("CONTAINERENUM ⭐: a Set's EnumProperty element publishes its enum",
              is >= 0 && F[is].elemEnumName == "EOther", is >= 0 ? F[is].elemEnumName.c_str() : "(no field)");
        check("CONTAINERENUM ⭐: a Map's TEnumAsByte key publishes its enum, and its plain value none",
              im >= 0 && F[im].keyEnumName == "EMyEnum" && F[im].valueEnumName.empty(),
              im >= 0 ? F[im].keyEnumName.c_str() : "(no field)");

        DynOff::bUseFProperty = savedFPropCE;
    }

    // -- METACLASS-2026-09-27 -- a ClassProperty's MetaClass reaches FieldInfo, validated ---------------------------
    //
    // ⛔ POOL-FAKING (own pool, after CONTAINERENUM). [SDK-METACLASS] walk_class published a ClassProperty's
    // PropertyClass -- `Class` -- and never its MetaClass, so the SDK header could only write TSubclassOf<class Class>.
    // MetaClass is the next pointer after PropertyClass in every supported layout (4.18..5.8 UnrealType.h; RE-UE4SS
    // templates; Dumper-7 Offsets.cpp). The read is refused unless the PropertyClass slot really holds a class of
    // classes AND the pointer after it is a UClass.
    {
        blk("METACLASS - WalkClassEx publishes a Class/SoftClass property's MetaClass, and refuses a bad read");

        static uint8_t mcEntry[22][0x40] = {};
        const char* mcNames[22] = { "", "ClassProperty", "SoftClassProperty", "ArrayProperty", "ObjectProperty",
                                    "Class", "BlueprintGeneratedClass", "Actor", "Object", "BP_Foo_C", "Pawn_0",
                                    "SubCls", "SoftCls", "BadMeta", "BadAnchor", "Director", "Arr", "Ctrl",
                                    "SetProperty", "Classes", "MapProperty", "ByClass" };
        static uintptr_t mcChunk[23] = {};
        for (int i = 1; i <= 21; ++i) {
            memcpy(mcEntry[i] + 0x10, mcNames[i], strlen(mcNames[i]) + 1);
            mcChunk[i] = reinterpret_cast<uintptr_t>(mcEntry[i]);
        }
        static uintptr_t mcChunks[2] = { reinterpret_cast<uintptr_t>(mcChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(mcChunks), 0x10);

        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto A     = [](uint8_t* b) { return reinterpret_cast<uintptr_t>(b); };

        // The reflection objects: `Class` is its own class; BlueprintGeneratedClass is a Class whose super is Class;
        // Actor / Object are Classes; BP_Foo_C is a BlueprintGeneratedClass; Pawn_0 is an INSTANCE (class Actor).
        static uint8_t mcObj[11][0x100] = {};
        auto obj = [&](int nameIdx) { return A(mcObj[nameIdx]); };
        for (int i = 5; i <= 10; ++i) put32(mcObj[i], Grimoire::OFF_UOBJECT_NAME, i);
        putP(mcObj[5],  Grimoire::OFF_UOBJECT_CLASS, obj(5));
        putP(mcObj[6],  Grimoire::OFF_UOBJECT_CLASS, obj(5));
        putP(mcObj[6],  DynOff::USTRUCT_SUPER, obj(5));
        putP(mcObj[7],  Grimoire::OFF_UOBJECT_CLASS, obj(5));
        putP(mcObj[8],  Grimoire::OFF_UOBJECT_CLASS, obj(5));
        putP(mcObj[9],  Grimoire::OFF_UOBJECT_CLASS, obj(6));
        putP(mcObj[10], Grimoire::OFF_UOBJECT_CLASS, obj(7));

        // ---- FProperty mode ----
        const bool savedFPropMC = DynOff::bUseFProperty;
        DynOff::bUseFProperty = true;
        const int slot = DynOff::FSTRUCTPROP_STRUCT;

        static uint8_t mcFC[5][0x20] = {};
        auto fclass = [&](int nameIdx) { put32(mcFC[nameIdx], DynOff::FFIELDCLASS_NAME, nameIdx); return A(mcFC[nameIdx]); };
        static uint8_t mcSetFC[0x20] = {}, mcMapFC[0x20] = {};
        put32(mcSetFC, DynOff::FFIELDCLASS_NAME, 18);
        put32(mcMapFC, DynOff::FFIELDCLASS_NAME, 20);

        static uint8_t mcP[10][0x100] = {};
        auto prop = [&](int i, uintptr_t fc, int nameIdx, int32_t off, uintptr_t propertyClass, uintptr_t meta,
                        uint8_t* next) {
            putP(mcP[i], DynOff::FFIELD_CLASS, fc);
            put32(mcP[i], DynOff::FFIELD_NAME, nameIdx);
            put32(mcP[i], DynOff::FPROPERTY_OFFSET, off);
            put32(mcP[i], DynOff::FPROPERTY_ELEMSIZE, 8);
            put32(mcP[i], DynOff::FPROPERTY_ELEMSIZE - 4, 1);
            putP(mcP[i], DynOff::FFIELD_NEXT, A(next));
            if (propertyClass) putP(mcP[i], slot, propertyClass);
            if (meta)          putP(mcP[i], slot + 8, meta);
        };
        // Container inners (never chained into the class).
        static uint8_t mcInner[0x100] = {}, mcSetElem[0x100] = {}, mcKey[0x100] = {}, mcVal[0x100] = {};
        auto innerProp = [&](uint8_t* p, uintptr_t fc, uintptr_t propertyClass, uintptr_t meta) {
            putP(p, DynOff::FFIELD_CLASS, fc);
            putP(p, slot, propertyClass);
            putP(p, slot + 8, meta);
        };
        innerProp(mcInner,   fclass(1), obj(5), obj(7));   // TArray<TSubclassOf<Actor>>
        innerProp(mcSetElem, fclass(2), obj(5), obj(9));   // TSet<TSoftClassPtr<BP_Foo_C>>
        innerProp(mcKey,     fclass(1), obj(5), obj(7));   // TMap<TSubclassOf<Actor>, UObject*>
        innerProp(mcVal,     fclass(4), obj(7), obj(7));   //   value: a plain ObjectProperty (no MetaClass)

        prop(0, fclass(1), 11, 0x28, obj(5), obj(7),  mcP[1]);   // SubCls    TSubclassOf<Actor>
        prop(1, fclass(2), 12, 0x30, obj(5), obj(9),  mcP[2]);   // SoftCls   TSoftClassPtr<BP_Foo_C>
        prop(2, fclass(1), 13, 0x38, obj(5), obj(10), mcP[3]);   // BadMeta   +8 is an INSTANCE
        prop(3, fclass(1), 14, 0x40, obj(7), obj(7),  mcP[4]);   // BadAnchor PropertyClass is not a class of classes
        prop(4, fclass(1), 15, 0x48, obj(6), obj(8),  mcP[5]);   // Director  TObjectPtr<UBlueprintGeneratedClass>
        prop(5, fclass(3), 16, 0x50, 0, 0,            mcP[6]);   // Arr       TArray<TSubclassOf<Actor>>
        putP(mcP[5], DynOff::FARRAYPROP_INNER, A(mcInner));
        prop(6, fclass(4), 17, 0x60, obj(7), obj(7),  mcP[7]);   // Ctrl      an ObjectProperty: no MetaClass, ever
        prop(7, A(mcSetFC), 19, 0x68, 0, 0,           mcP[8]);   // Classes   TSet<TSoftClassPtr<BP_Foo_C>>
        putP(mcP[7], DynOff::FARRAYPROP_INNER, A(mcSetElem));
        prop(8, A(mcMapFC), 21, 0xB8, 0, 0,           nullptr);  // ByClass   TMap<TSubclassOf<Actor>, Actor*>
        putP(mcP[8], slot, A(mcKey));
        putP(mcP[8], slot + 8, A(mcVal));

        static uint8_t mcCls[0x100] = {};
        put32(mcCls, DynOff::USTRUCT_PROPSSIZE, 0x108);
        putP(mcCls, DynOff::USTRUCT_CHILDPROPS, A(mcP[0]));

        const auto& mcInfo = Ubel::WalkClassEx(A(mcCls));
        auto field = [&](const char* name) -> const FieldInfo* {
            for (const auto& f : mcInfo.Fields) if (f.Name == name) return &f;
            return nullptr;
        };
        const FieldInfo* fSub = field("SubCls");   const FieldInfo* fSoft = field("SoftCls");
        const FieldInfo* fBadM = field("BadMeta"); const FieldInfo* fBadA = field("BadAnchor");
        const FieldInfo* fDir = field("Director"); const FieldInfo* fArr = field("Arr");
        const FieldInfo* fCtl = field("Ctrl");     const FieldInfo* fSet = field("Classes");
        const FieldInfo* fMap = field("ByClass");
        auto s = [](const FieldInfo* f, std::string FieldInfo::* m) { return f ? (f->*m).c_str() : "(no field)"; };
        check("METACLASS control: the fake class produced all nine fields, typed",
              fSub && fSoft && fBadM && fBadA && fDir && fArr && fCtl && fSet && fMap
                && fSub->TypeName == "ClassProperty" && fArr->innerType == "ClassProperty"
                && fSet->elemType == "SoftClassProperty" && fMap->keyType == "ClassProperty",
              std::to_string(mcInfo.Fields.size()).c_str());
        check("METACLASS ⭐: a ClassProperty publishes its MetaClass",
              fSub && fSub->metaClassName == "Actor", s(fSub, &FieldInfo::metaClassName));
        check("METACLASS control: ...and its PropertyClass is still `Class`",
              fSub && fSub->objClassName == "Class", s(fSub, &FieldInfo::objClassName));
        check("METACLASS ⭐: a SoftClassProperty publishes its MetaClass (a BlueprintGeneratedClass passes the chain)",
              fSoft && fSoft->metaClassName == "BP_Foo_C", s(fSoft, &FieldInfo::metaClassName));
        check("METACLASS ⭐: an INSTANCE after PropertyClass is refused",
              fBadM && fBadM->metaClassName.empty(), s(fBadM, &FieldInfo::metaClassName));
        check("METACLASS ⭐: a PropertyClass that is not a class of classes refuses the whole read",
              fBadA && fBadA->metaClassName.empty(), s(fBadA, &FieldInfo::metaClassName));
        check("METACLASS control: ...and its obj_class is still the plain slot read, not blanked",
              fBadA && fBadA->objClassName == "Actor", s(fBadA, &FieldInfo::objClassName));
        check("METACLASS ⭐: PropertyClass BlueprintGeneratedClass passes the anchor through its super chain",
              fDir && fDir->metaClassName == "Object" && fDir->objClassName == "BlueprintGeneratedClass",
              s(fDir, &FieldInfo::metaClassName));
        check("METACLASS ⭐: an Array's ClassProperty inner publishes its MetaClass",
              fArr && fArr->innerMetaClass == "Actor", s(fArr, &FieldInfo::innerMetaClass));
        check("METACLASS ⭐: a Set's SoftClassProperty element publishes its MetaClass",
              fSet && fSet->elemMetaClass == "BP_Foo_C", s(fSet, &FieldInfo::elemMetaClass));
        check("METACLASS ⭐: a Map's ClassProperty key publishes its MetaClass, its ObjectProperty value none",
              fMap && fMap->keyMetaClass == "Actor" && fMap->valueMetaClass.empty(), s(fMap, &FieldInfo::keyMetaClass));
        check("METACLASS control: an ObjectProperty never gets a MetaClass",
              fCtl && fCtl->metaClassName.empty() && fCtl->objClassName == "Actor", s(fCtl, &FieldInfo::metaClassName));
        DynOff::bUseFProperty = savedFPropMC;

        // ---- UProperty mode, UE 4.18 ----
        // The subclass slot is UPropertySubclassStartFor(0x44, 418) = 0x70; FSTRUCTPROP_STRUCT is hand-set to 0x78 --
        // what Genau left on every UProperty engine before [UPROP-SUBCLASS-SLOT], and what a family move after init can
        // still leave -- so the class-valued read must not borrow it. PropertyClass is planted ONLY at 0x70 and
        // MetaClass at 0x78, so a reader that borrows FSTRUCTPROP_STRUCT reads MetaClass as its anchor.
        const bool     savedFPropU = DynOff::bUseFProperty;
        const bool     savedCpnU   = DynOff::bCasePreservingName;
        const int      savedOffU   = DynOff::UPROPERTY_OFFSET;
        const int      savedSlotU  = DynOff::FSTRUCTPROP_STRUCT;
        const uint32_t savedVerU   = g_cachedUEVersion;
        DynOff::bUseFProperty       = false;
        DynOff::bCasePreservingName = false;
        DynOff::UPROPERTY_OFFSET    = 0x44;
        DynOff::FSTRUCTPROP_STRUCT  = 0x78;
        g_cachedUEVersion           = 418;
        const int uSlot = DynOff::UPropertySubclassStartFor(0x44, 418, false);
        check("METACLASS setup: the 4.18 UProperty subclass slot is 0x70, not FSTRUCTPROP_STRUCT's 0x78",
              uSlot == 0x70 && DynOff::FSTRUCTPROP_STRUCT != uSlot, std::to_string(uSlot).c_str());

        static uint8_t mcUClassProp[0x100] = {};          // the UObject whose NAME is "ClassProperty"
        put32(mcUClassProp, Grimoire::OFF_UOBJECT_NAME, 1);
        static uint8_t mcUProp[0x100] = {};
        putP(mcUProp, Grimoire::OFF_UOBJECT_CLASS, A(mcUClassProp));
        put32(mcUProp, Grimoire::OFF_UOBJECT_NAME, 11);     // "SubCls"
        put32(mcUProp, DynOff::UPROPERTY_ELEMSIZE, 8);
        put32(mcUProp, DynOff::UPROPERTY_ELEMSIZE - 4, 1);        // ArrayDim
        put32(mcUProp, 0x44, 0x28);
        putP(mcUProp, uSlot, obj(5));                        // PropertyClass = Class
        putP(mcUProp, uSlot + 8, obj(7));                    // MetaClass     = Actor
        static uint8_t mcUCls[0x100] = {};
        put32(mcUCls, DynOff::USTRUCT_PROPSSIZE, 0x30);
        putP(mcUCls, DynOff::USTRUCT_CHILDREN, A(mcUProp));
        const auto& muInfo = Ubel::WalkClassEx(A(mcUCls));
        const FieldInfo* fU = nullptr;
        for (const auto& f : muInfo.Fields) if (f.Name == "SubCls") fU = &f;
        check("METACLASS control: the 4.18 UProperty class produced its ClassProperty",
              fU && fU->TypeName == "ClassProperty", std::to_string(muInfo.Fields.size()).c_str());
        check("METACLASS ⭐: UProperty 4.18 reads MetaClass at the VERSION's slot + 8, not FSTRUCTPROP_STRUCT's",
              fU && fU->metaClassName == "Actor", s(fU, &FieldInfo::metaClassName));
        // [SDK-METACLASS] review wf_63e981ac-5e4: obj_class came from FSTRUCTPROP_STRUCT, which on a UProperty engine
        // sits one pointer past PropertyClass -- ON the MetaClass -- so the wire said obj_class == meta_class and the
        // SDK export turned every TSubclassOf<X> into `class X*` (UE423_Flying: 0 of 160 ClassProperty rows right).
        check("METACLASS ⭐: UProperty 4.18 publishes PropertyClass `Class` as obj_class, not the MetaClass beside it",
              fU && fU->objClassName == "Class", s(fU, &FieldInfo::objClassName));

        g_cachedUEVersion           = savedVerU;
        DynOff::FSTRUCTPROP_STRUCT  = savedSlotU;
        DynOff::UPROPERTY_OFFSET    = savedOffU;
        DynOff::bCasePreservingName = savedCpnU;
        DynOff::bUseFProperty       = savedFPropU;
    }

    // -- GENAUABORT-2026-09-12 -- a Genau sweep that bails on a cancel records it AT THE BAIL -----------------
    //
    // [P1-GENAU-ABORT] [A2-GNAMES-PTRSCAN-ABORT] Each sweep polls Tot::Requested() on its first page-aligned slot, so
    // a cancel already pending stops it there -- deterministic, with no thread race. They walk THIS test exe's own
    // sections. Last, because the uncancelled controls run whole sweeps whose success paths may touch Serie.
    {
        blk("GENAUABORT - a Genau sweep that bails on a cancel records the abort at the bail");

        std::vector<uintptr_t> gaCands;
        bool gaHeap = false;
        ResetCancel();
        Tot::g_perCommand.store(true);
        Genau::CollectGObjectsCandidates(gaCands, 0, 4, &gaHeap);
        ResetCancel();
        check("GENAUABORT ⭐: CollectGObjectsCandidates records its abort", gaHeap);

        int gaStride = 0;
        bool gaStatic = false;
        Tot::g_perCommand.store(true);
        Genau::FindGObjectsStaticStruct(&gaStride, &gaStatic);
        ResetCancel();
        check("GENAUABORT ⭐: FindGObjectsStaticStruct records its abort", gaStatic);

        Genau::s_gnamesReport = Genau::ScanReport{};
        Tot::g_perCommand.store(true);
        Genau::FindGNamesByStringRef();
        ResetCancel();
        check("GENAUABORT ⭐: the GNames string-ref tier records its abort in the GNames report",
              Genau::s_gnamesReport.cancelled);

        Genau::s_gnamesReport = Genau::ScanReport{};
        Tot::g_perCommand.store(true);
        Genau::FindGNamesByPointerScan();
        ResetCancel();
        check("GENAUABORT ⭐: the GNames pointer-scan tier records its abort (it bailed with no log and no flag)",
              Genau::s_gnamesReport.cancelled);

        std::vector<uintptr_t> gaCands2;
        bool gaHeap2 = false;
        Genau::CollectGObjectsCandidates(gaCands2, 0, 1, &gaHeap2);
        check("GENAUABORT control: an uncancelled candidate sweep records no abort", !gaHeap2);
        Genau::s_gnamesReport = Genau::ScanReport{};
        Genau::FindGNamesByPointerScan();
        check("GENAUABORT control: an uncancelled pointer scan records no abort", !Genau::s_gnamesReport.cancelled);
        Genau::s_gnamesReport = Genau::ScanReport{};

        // Review 5 of 785b1730: the two sweeps above were checked only with a cancel pending, so a store hoisted above
        // its poll -- set on EVERY call -- passed. On a real game that refuses the init latch on every scan.
        int gaStride3 = 0;
        bool gaStatic3 = false;
        Genau::FindGObjectsStaticStruct(&gaStride3, &gaStatic3);
        check("GENAUABORT control ⭐: an uncancelled static-struct sweep records no abort", !gaStatic3);
        Genau::s_gnamesReport = Genau::ScanReport{};
        Genau::FindGNamesByStringRef();
        check("GENAUABORT control ⭐: an uncancelled string-ref sweep records no abort", !Genau::s_gnamesReport.cancelled);
        Genau::s_gnamesReport = Genau::ScanReport{};
    }

    // -- ENUMNAMESCANCEL-2026-09-12 -- a cancelled UEnum::Names search is not a FAILED one -------------------------
    //
    // [P1-ENUMNAMES] DetectUEnumNames searches through Aura::ForEach, whose cancel was VOID: a cancelled search read as
    // "no known enum here" and latched FAILED for the rest of the process. Re-inits Aura on the main pool first.
    {
        blk("ENUMNAMESCANCEL - ForEach reports its abort; a cancelled DetectUEnumNames does not latch FAILED");
        ResetCancel();
        Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);

        int enSeen = 0;
        const bool enWhole = Aura::ForEach([&](int32_t, uintptr_t) { ++enSeen; return true; });
        check("ENUMNAMESCANCEL control: an uncancelled ForEach walks the whole pool and says so",
              enWhole && enSeen == kCount, std::to_string(enSeen).c_str());
        Tot::g_perCommand.store(true);
        const bool enCut = Aura::ForEach([&](int32_t, uintptr_t) { return true; });
        ResetCancel();
        check("ENUMNAMESCANCEL ⭐: a cancelled ForEach says it was cut short", !enCut);

        DynOff::bUEnumNamesDetected.store(false);
        DynOff::bUEnumNamesFailed.store(false);
        Tot::g_perCommand.store(true);
        const bool enDet = Genau::DetectUEnumNames();
        ResetCancel();
        check("ENUMNAMESCANCEL ⭐: a CANCELLED search does not latch FAILED -- the next call retries",
              !enDet && !DynOff::bUEnumNamesFailed.load() && !DynOff::bUEnumNamesDetected.load());

        // ...and the next call RETRIES. Before the fix the cancelled search latched FAILED -- and the FAILED latch also
        // sets bUEnumNamesDetected ("prevent retry storm"), so this call returned true at its first line, searching
        // nothing. Uncancelled, it searches the fake pool, which holds no known UEnum, and latches FAILED itself.
        const bool enDet2 = Genau::DetectUEnumNames();
        check("ENUMNAMESCANCEL ⭐: ...so the next, uncancelled search really runs -- and, finding nothing, latches FAILED",
              !enDet2 && DynOff::bUEnumNamesFailed.load());

        // Review 5 of 7e5a71fc: a CANCELLED detection sets neither flag, and ResolveEnumValue read on regardless -- with
        // the DEFAULT Names offset / format -- and cached that answer for the process. It must read and cache nothing.
        static uint8_t enFake[0x100] = {};                       // readable, and nothing at the default Names offset
        const uintptr_t enAddr = reinterpret_cast<uintptr_t>(enFake);
        DynOff::bUEnumNamesDetected.store(false);
        DynOff::bUEnumNamesFailed.store(false);
        Tot::g_perCommand.store(true);
        const std::string enV = Ubel::ResolveEnumValue(enAddr, 0);
        ResetCancel();
        {
            std::lock_guard<std::mutex> lk(Ubel::s_enumCacheMutex);
            check("ENUMNAMESCANCEL ⭐: a cancelled detection caches nothing for the enum it was asked about",
                  enV.empty() && Ubel::s_enumCache.count(enAddr) == 0);
        }
        // Control: with detection done, the same lookup reads and caches -- so the observation above can see a cache.
        DynOff::bUEnumNamesDetected.store(true);
        Ubel::ResolveEnumValue(enAddr, 0);
        {
            std::lock_guard<std::mutex> lk(Ubel::s_enumCacheMutex);
            check("ENUMNAMESCANCEL control: a completed detection caches the table it read",
                  Ubel::s_enumCache.count(enAddr) == 1);
            Ubel::s_enumCache.erase(enAddr);
        }
        DynOff::bUEnumNamesDetected.store(false);
        DynOff::bUEnumNamesFailed.store(false);
    }

    // -- DELEGATEARRAYPAD-2026-09-12 -- a TArray<FScriptDelegate> publishes its PER-ELEMENT detector pad -----------
    //
    // ⛔ POOL-FAKING, like STRARRAYWALK: own pool, last. [A4-DELEGATE-ARRAY-PAD] The elements of a TArray<FScriptDelegate>
    // are the standalone unicast delegate, padded on a checked build. The reader always knew (its stride); the wire
    // never said, so CE XML / CSX element leaves read the detector.
    {
        blk("DELEGATEARRAYPAD - the helper and the walk publish a delegate array's per-element pad");
        ResetCancel();

        const bool savedCpnD = DynOff::bCasePreservingName;
        DynOff::bCasePreservingName = false;
        check("DELEGATEARRAYPAD ⭐: DelegateArrayElemPad reads 8 off a padded 24-byte element",
              Ubel::DelegateArrayElemPad(24) == 8, std::to_string(Ubel::DelegateArrayElemPad(24)).c_str());
        check("DELEGATEARRAYPAD control: ...and 0 off an unpadded 16-byte one", Ubel::DelegateArrayElemPad(16) == 0);
        check("DELEGATEARRAYPAD control: ...and 0, never negative, off a size that matches neither",
              Ubel::DelegateArrayElemPad(20) == 0, std::to_string(Ubel::DelegateArrayElemPad(20)).c_str());
        DynOff::bCasePreservingName = true;
        check("DELEGATEARRAYPAD ⭐: with CasePreservingName (12-byte FName) a 28-byte element is padded 8",
              Ubel::DelegateArrayElemPad(28) == 8, std::to_string(Ubel::DelegateArrayElemPad(28)).c_str());
        DynOff::bCasePreservingName = false;

        static uint8_t daEntry[4][0x40] = {};
        const char* daNames[4] = { "", "ArrayProperty", "Handlers", "DelegateProperty" };
        static uintptr_t daChunk[5] = {};
        for (int i = 1; i <= 3; ++i) {
            memcpy(daEntry[i] + 0x10, daNames[i], strlen(daNames[i]) + 1);
            daChunk[i] = reinterpret_cast<uintptr_t>(daEntry[i]);
        }
        static uintptr_t daChunks[2] = { reinterpret_cast<uintptr_t>(daChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(daChunks), 0x10);
        check("DELEGATEARRAYPAD setup: the pool resolves DelegateProperty",
              Serie::GetString(3) == "DelegateProperty", Serie::GetString(3).c_str());

        const bool savedFPropD = DynOff::bUseFProperty;
        DynOff::bUseFProperty = true;

        static uint8_t daArrFC[0x20] = {}, daDelFC[0x20] = {};
        *reinterpret_cast<int32_t*>(daArrFC + DynOff::FFIELDCLASS_NAME) = 1;   // "ArrayProperty"
        *reinterpret_cast<int32_t*>(daDelFC + DynOff::FFIELDCLASS_NAME) = 3;   // "DelegateProperty"

        // Two arrays in one class: a checked build's 24-byte element, then a Shipping build's 16-byte one.
        static uint8_t daInner[2][0x100] = {}, daProp[2][0x100] = {};
        const int32_t daElem[2] = { 24, 16 };
        for (int k = 0; k < 2; ++k) {
            *reinterpret_cast<uintptr_t*>(daInner[k] + DynOff::FFIELD_CLASS) = reinterpret_cast<uintptr_t>(daDelFC);
            *reinterpret_cast<int32_t*>(daInner[k] + DynOff::FPROPERTY_ELEMSIZE) = daElem[k];
            *reinterpret_cast<uintptr_t*>(daProp[k] + DynOff::FFIELD_CLASS) = reinterpret_cast<uintptr_t>(daArrFC);
            *reinterpret_cast<int32_t*>(daProp[k] + DynOff::FFIELD_NAME)        = 2;   // "Handlers"
            *reinterpret_cast<int32_t*>(daProp[k] + DynOff::FPROPERTY_OFFSET)   = 0x40 + k * 0x10;
            *reinterpret_cast<int32_t*>(daProp[k] + DynOff::FPROPERTY_ELEMSIZE) = 16;
            *reinterpret_cast<int32_t*>(daProp[k] + DynOff::FPROPERTY_ELEMSIZE - 4) = 1;
            *reinterpret_cast<uintptr_t*>(daProp[k] + DynOff::FARRAYPROP_INNER) = reinterpret_cast<uintptr_t>(daInner[k]);
        }
        *reinterpret_cast<uintptr_t*>(daProp[0] + DynOff::FFIELD_NEXT) = reinterpret_cast<uintptr_t>(daProp[1]);

        static uint8_t daCls[0x100] = {};
        *reinterpret_cast<int32_t*>(daCls + DynOff::USTRUCT_PROPSSIZE)    = 0x100;
        *reinterpret_cast<uintptr_t*>(daCls + DynOff::USTRUCT_CHILDPROPS) = reinterpret_cast<uintptr_t>(daProp[0]);

        static uint8_t daData[2][2 * 24] = {};   // two zeroed elements per array: unbound delegates
        static uint8_t daInst[0x100] = {};
        for (int k = 0; k < 2; ++k) {
            *reinterpret_cast<uintptr_t*>(daInst + 0x40 + k * 0x10) = reinterpret_cast<uintptr_t>(daData[k]);   // Data
            *reinterpret_cast<int32_t*>(daInst + 0x48 + k * 0x10)   = 2;                                         // Num
            *reinterpret_cast<int32_t*>(daInst + 0x4C + k * 0x10)   = 2;                                         // Max
        }

        const auto dr = Ubel::WalkInstance(reinterpret_cast<uintptr_t>(daInst),
                                           reinterpret_cast<uintptr_t>(daCls), 64, 2, false);
        const Ubel::LiveFieldValue* d24 = dr.fields.size() == 2 ? &dr.fields[0] : nullptr;
        const Ubel::LiveFieldValue* d16 = dr.fields.size() == 2 ? &dr.fields[1] : nullptr;
        check("DELEGATEARRAYPAD setup: two ArrayProperty fields of DelegateProperty, strides 24 and 16",
              d24 && d16 && d24->arrayInnerType == "DelegateProperty" && d24->arrayElemSize == 24
              && d16->arrayElemSize == 16,
              d24 ? std::to_string(d24->arrayElemSize).c_str() : std::to_string(dr.fields.size()).c_str());
        check("DELEGATEARRAYPAD ⭐: the walk publishes the 24-byte array's per-element pad (8)",
              d24 && d24->arrayElemDelegatePad == 8, d24 ? std::to_string(d24->arrayElemDelegatePad).c_str() : "-");
        check("DELEGATEARRAYPAD guard: ...and never folds it into the array FIELD's own delegatePad",
              d24 && d24->delegatePad == 0);
        check("DELEGATEARRAYPAD control: the 16-byte array's elements carry no pad",
              d16 && d16->arrayElemDelegatePad == 0);

        DynOff::bUseFProperty       = savedFPropD;
        DynOff::bCasePreservingName = savedCpnD;
    }

    // -- UPROPDELEGATE-2026-09-12 -- the UProperty-mode delegate-array arms name the readers' refusal ---------------
    //
    // ⛔ POOL-FAKING (own pool, last). [P1-UPROP-DELEGATE] e16d2052 fixed the FProperty arms; the UProperty-mode
    // (UE4 < 4.25) twins kept dropping the refusal. A 20-byte element is neither 16 nor 24, so both readers refuse it.
    {
        blk("UPROPDELEGATE - a UProperty-mode delegate array names the reader's refusal, as the FProperty arm does");
        ResetCancel();

        static uint8_t udEntry[6][0x40] = {};
        const char* udNames[6] = { "", "ArrayProperty", "DelegateProperty", "MulticastDelegateProperty",
                                   "Handlers", "Events" };
        static uintptr_t udChunk[7] = {};
        for (int i = 1; i <= 5; ++i) {
            memcpy(udEntry[i] + 0x10, udNames[i], strlen(udNames[i]) + 1);
            udChunk[i] = reinterpret_cast<uintptr_t>(udEntry[i]);
        }
        static uintptr_t udChunks[2] = { reinterpret_cast<uintptr_t>(udChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(udChunks), 0x10);
        check("UPROPDELEGATE setup: the pool resolves MulticastDelegateProperty",
              Serie::GetString(3) == "MulticastDelegateProperty", Serie::GetString(3).c_str());

        const bool savedFPropUD = DynOff::bUseFProperty;
        const bool savedCpnUD   = DynOff::bCasePreservingName;
        DynOff::bUseFProperty       = false;
        DynOff::bCasePreservingName = false;
        auto udPutP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto udPut32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };

        // The UProperty "classes": UObjects whose FName is the property type's name.
        static uint8_t udTypeCls[4][0x40] = {};
        for (int i = 1; i <= 3; ++i) udPut32(udTypeCls[i], Grimoire::OFF_UOBJECT_NAME, i);

        // Two array UProperties, each with a 20-byte inner UProperty at Offset_Internal + 0x2C (the walker's probe).
        static uint8_t udInner[2][0x100] = {}, udProp[2][0x100] = {};
        for (int k = 0; k < 2; ++k) {
            udPutP(udInner[k], Grimoire::OFF_UOBJECT_CLASS, reinterpret_cast<uintptr_t>(udTypeCls[k == 0 ? 2 : 3]));
            udPut32(udInner[k], DynOff::UPROPERTY_ELEMSIZE, 20);
            udPutP(udProp[k], Grimoire::OFF_UOBJECT_CLASS, reinterpret_cast<uintptr_t>(udTypeCls[1]));   // ArrayProperty
            udPut32(udProp[k], Grimoire::OFF_UOBJECT_NAME, k == 0 ? 4 : 5);                              // Handlers / Events
            udPut32(udProp[k], DynOff::UPROPERTY_OFFSET, 0x40 + k * 0x10);
            udPut32(udProp[k], DynOff::UPROPERTY_ELEMSIZE, 16);
            udPut32(udProp[k], DynOff::UPROPERTY_ELEMSIZE - 4, 1);
            udPutP(udProp[k], DynOff::UPROPERTY_OFFSET + 0x2C, reinterpret_cast<uintptr_t>(udInner[k]));
        }
        udPutP(udProp[0], DynOff::UFIELD_NEXT, reinterpret_cast<uintptr_t>(udProp[1]));

        static uint8_t udCls[0x100] = {};
        udPut32(udCls, DynOff::USTRUCT_PROPSSIZE, 0x100);
        udPutP(udCls, DynOff::USTRUCT_CHILDREN, reinterpret_cast<uintptr_t>(udProp[0]));

        static uint8_t udData[2][2 * 20] = {};
        static uint8_t udInst[0x100] = {};
        for (int k = 0; k < 2; ++k) {
            udPutP(udInst, 0x40 + k * 0x10, reinterpret_cast<uintptr_t>(udData[k]));   // TArray.Data
            udPut32(udInst, 0x48 + k * 0x10, 2);                                          // Num
            udPut32(udInst, 0x4C + k * 0x10, 2);                                          // Max
        }

        const auto ur = Ubel::WalkInstance(reinterpret_cast<uintptr_t>(udInst),
                                           reinterpret_cast<uintptr_t>(udCls), 64, 2, false);
        const Ubel::LiveFieldValue* uHand = ur.fields.size() == 2 ? &ur.fields[0] : nullptr;
        const Ubel::LiveFieldValue* uEvt  = ur.fields.size() == 2 ? &ur.fields[1] : nullptr;
        check("UPROPDELEGATE setup: the UProperty walk found both arrays with their 20-byte inners",
              uHand && uEvt && uHand->arrayInnerType == "DelegateProperty" && uHand->arrayElemSize == 20
              && uEvt->arrayInnerType == "MulticastDelegateProperty",
              std::to_string(ur.fields.size()).c_str());
        check("UPROPDELEGATE ⭐: the unicast arm names the reader's refusal",
              uHand && uHand->typedValue.find("(delegate array") == 0
              && uHand->typedValue.find("element size 20") != std::string::npos,
              uHand ? uHand->typedValue.c_str() : "-");
        check("UPROPDELEGATE ⭐: ...and so does the multicast arm",
              uEvt && uEvt->typedValue.find("(multicast array") == 0
              && uEvt->typedValue.find("element size 20") != std::string::npos,
              uEvt ? uEvt->typedValue.c_str() : "-");

        DynOff::bUseFProperty       = savedFPropUD;
        DynOff::bCasePreservingName = savedCpnUD;
    }

    // -- WALKUNREADABLE-2026-09-12 -- a walk of an unreadable instance SAYS so ---------------------------------------
    //
    // [P1-WALK-UNREADABLE] WalkInstance knew ("not readable (freed?)") and returned a result carrying only addr: no
    // stale, no error, so Live Walker showed a silently blank grid. A reserved, uncommitted page is the freed object.
    {
        blk("WALKUNREADABLE - WalkInstance marks an instance it cannot read");
        uint8_t* wuPage = static_cast<uint8_t*>(VirtualAlloc(nullptr, 0x1000, MEM_RESERVE, PAGE_NOACCESS));
        check("WALKUNREADABLE setup: reserved an uncommitted page", wuPage != nullptr);
        if (wuPage) {
            const auto wr = Ubel::WalkInstance(reinterpret_cast<uintptr_t>(wuPage), 0, 64, 2, false);
            check("WALKUNREADABLE ⭐: an unreadable instance is marked unreadable", wr.unreadable);
            check("WALKUNREADABLE control: ...and not stale, which means something narrower", !wr.isStale);
            VirtualFree(wuPage, 0, MEM_RELEASE);
        }
        static uint8_t wuLive[0x100] = {};
        const auto wl = Ubel::WalkInstance(reinterpret_cast<uintptr_t>(wuLive), 0, 64, 2, false);
        check("WALKUNREADABLE control: a readable instance is not marked", !wl.unreadable);
    }

    // -- SPARSEVALIDATE-2026-09-25 -- the sparse-storage validator accepts a vtable in ANY mapped module ---------------
    //
    // [R7-X3] ValidateSparseDelegates' content check accepted a key only when its vtable lay inside the MAIN module.
    // On a modular build (the UE 4.27 editor, measured 2026-09-25) every UObject vtable lives in a UE4Editor-*.dll,
    // so the real storage was refused ("1 live element(s) but none has a UObject-shaped key") and sparse delegates
    // went unread. kernel32's image stands in for the engine DLL; a VirtualAlloc page stands in for heap garbage.
    {
        blk("SPARSEVALIDATE - the sparse-storage validator accepts a vtable in any mapped module, not heap");
        const uintptr_t k32 = reinterpret_cast<uintptr_t>(GetModuleHandleW(L"kernel32.dll"));
        uint8_t* heapVt = static_cast<uint8_t*>(VirtualAlloc(nullptr, 0x1000, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
        alignas(8) static uint8_t svObj[0x40] = {};
        alignas(8) static uint8_t svSlot[0x60] = {};
        alignas(8) static uint8_t svMap[0x50] = {};
        const uintptr_t objAddr = reinterpret_cast<uintptr_t>(svObj);
        memcpy(svSlot, &objAddr, 8);
        const uintptr_t slotAddr = reinterpret_cast<uintptr_t>(svSlot);
        memcpy(svMap + 0x00, &slotAddr, 8);
        const int32_t one = 1;
        memcpy(svMap + 0x08, &one, 4);
        memcpy(svMap + 0x0C, &one, 4);
        const auto withVtable = [&](uintptr_t vt) {
            memcpy(svObj, &vt, 8);
            return Genau::ValidateSparseDelegates(reinterpret_cast<uintptr_t>(svMap));
        };
        check("SPARSEVALIDATE setup: kernel32 and a private page are both available", k32 != 0 && heapVt != nullptr);
        check("SPARSEVALIDATE control: a vtable in the main module is accepted",
              withVtable(reinterpret_cast<uintptr_t>(&Genau::FindSparseDelegateStorage)));
        check("SPARSEVALIDATE ⭐ R7-X3: a vtable in ANOTHER mapped module (a modular build's engine DLL) is accepted",
              withVtable(k32 + 0x1000));
        check("SPARSEVALIDATE control: a vtable on a private (heap) page is still refused",
              !withVtable(reinterpret_cast<uintptr_t>(heapVt)));
        VirtualFree(heapVt, 0, MEM_RELEASE);
    }

    // -- SPARSEREFS-2026-09-12 -- Find References counts the sparse delegates it could not read ----------------------
    //
    // ⛔ POOL-FAKING (own pool, last). [P1-SPARSEDELEGATE-REFS] A sparse delegate whose InvocationList cannot be located
    // was skipped, and the sweep still reported itself complete -- so the UI blamed the game for our gap. A planted
    // FSparseDelegateStorage map holds two unreadable delegates and one readable one: exactly two must be counted.
    {
        blk("SPARSEREFS - the sweep counts the sparse delegates it could not read");
        ResetCancel();
        Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);

        static uint8_t spEntry[2][0x40] = {};
        memcpy(spEntry[1] + 0x10, "OnHit", 6);
        static uintptr_t spChunk[3] = { 0, reinterpret_cast<uintptr_t>(spEntry[1]), 0 };
        static uintptr_t spChunks[2] = { reinterpret_cast<uintptr_t>(spChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(spChunks), 0x10);
        check("SPARSEREFS setup: the pool resolves the bound function's name", Serie::GetString(1) == "OnHit",
              Serie::GetString(1).c_str());

        const uint32_t  savedVerSp     = g_cachedUEVersion;
        const bool      savedCpnSp     = DynOff::bCasePreservingName;
        const uintptr_t savedStoreSp   = Genau::s_sparseDelegatesCache.load();
        const bool      savedScannedSp = Genau::s_sparseDelegatesScanned.load();
        g_cachedUEVersion           = 505;     // the sparse pass is UE 5.0+
        DynOff::bCasePreservingName = false;   // 8-byte FName: the inner TPair slot is 0x20
        auto spPutP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto spPut32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };

        // The readable delegate: InvocationList { Data, Num 1, Max 1 } at +0, one binding naming "OnHit" whose weak
        // pointer resolves to nothing -- so it is read, and matches no target.
        alignas(8) static uint8_t spBinding[0x10] = {};
        spPut32(spBinding, 0x00, -1);    // FWeakObjectPtr.ObjectIndex
        spPut32(spBinding, 0x08, 1);     // FName "OnHit"
        alignas(8) static uint8_t spGood[0x20] = {};
        spPutP(spGood, 0x00, reinterpret_cast<uintptr_t>(spBinding));
        spPut32(spGood, 0x08, 1);
        spPut32(spGood, 0x0C, 1);
        alignas(8) static uint8_t spBad[0x20] = {};   // zeroed: neither candidate offset holds a coherent list

        // The inner map's TPair<FName, TSharedPtr> slots: two unreadable delegates, then the readable one.
        alignas(8) static uint8_t spInner[3][0x20] = {};
        for (int k = 0; k < 3; ++k) {
            spPut32(spInner[k], 0x00, 1);
            spPutP(spInner[k], 0x08, reinterpret_cast<uintptr_t>(k < 2 ? spBad : spGood));
        }
        // The outer map: one 0x60 slot holding the owner, then the inner map's header at +0x08 (inline bits at +0x10).
        alignas(8) static uint8_t spOwner[0x40] = {};
        // [R7-S2] The storage key probe asks for a UObject (8-aligned, a ClassPrivate pointer at +0x10): give the
        // owner one, as every real key has.
        spPutP(spOwner, Grimoire::OFF_UOBJECT_CLASS, reinterpret_cast<uintptr_t>(spOwner));
        alignas(8) static uint8_t spOuterSlot[0x60] = {};
        spPutP(spOuterSlot, 0x00, reinterpret_cast<uintptr_t>(spOwner));
        spPutP(spOuterSlot, 0x08, reinterpret_cast<uintptr_t>(spInner));
        spPut32(spOuterSlot, 0x08 + 0x08, 3);
        spPut32(spOuterSlot, 0x08 + 0x10, 0x7);
        alignas(8) static uint8_t spOuter[0x50] = {};
        spPutP(spOuter, 0x00, reinterpret_cast<uintptr_t>(spOuterSlot));
        spPut32(spOuter, 0x08, 1);
        spPut32(spOuter, 0x10, 0x1);
        Genau::s_sparseDelegatesCache.store(reinterpret_cast<uintptr_t>(spOuter));
        Genau::s_sparseDelegatesScanned.store(true);
        check("SPARSEREFS setup: the resolver returns the planted storage",
              Genau::FindSparseDelegateStorage() == reinterpret_cast<uintptr_t>(spOuter));

        static uint8_t spTarget[0x40] = {};
        Aura::ContainerScanStats st;
        const auto spRefs = Aura::FindReferencesToUObject(reinterpret_cast<uintptr_t>(spTarget), 32, &st);
        check("SPARSEREFS setup: a complete, empty sweep of the whole pool",
              spRefs.empty() && st.objectsTotal == kCount && !st.deadlineHit,
              std::to_string(st.objectsTotal).c_str());
        check("SPARSEREFS ⭐: the sweep counts the two unreadable sparse delegates, not the readable one",
              st.sparseUnlocated == 2, std::to_string(st.sparseUnlocated).c_str());

        // [R7-A-01] The same storage on a compact-set build: its global TMap is a 16-byte TCompactSet too, which the
        // sparse readers would read past. The sweep must say the pass was skipped, and the Live Walker's reader must
        // refuse instead of reporting "owner not in storage".
        const bool savedCompactSp = DynOff::bCompactSets.load();
        DynOff::bCompactSets.store(true);
        Aura::ContainerScanStats stC;
        const auto spRefsC = Aura::FindReferencesToUObject(reinterpret_cast<uintptr_t>(spTarget), 32, &stC);
        check("SPARSEREFS ⭐ R7-A-01: compact sets -> the sparse pass is reported SKIPPED, and nothing was read",
              spRefsC.empty() && stC.sparseSkipped && stC.sparseUnlocated == 0,
              std::to_string(stC.sparseUnlocated).c_str());
        const Aura::SparseDelegateResult srC =
            Aura::WalkSparseDelegateBindings(reinterpret_cast<uintptr_t>(spOwner), "OnHit", 8);
        check("SPARSEREFS ⭐ R7-A-01: compact sets -> the Live Walker reader refuses (resolved, not supported)",
              srC.resolved && !srC.supported && !srC.ownerFound && srC.bindings.empty());
        DynOff::bCompactSets.store(savedCompactSp);
        Aura::ContainerScanStats stS;
        Aura::FindReferencesToUObject(reinterpret_cast<uintptr_t>(spTarget), 32, &stS);
        check("SPARSEREFS control: a sparse-set build reads the storage and skips nothing",
              !stS.sparseSkipped && stS.sparseUnlocated == 2);

        // [R7-S2] Sparse delegates exist from UE 4.23, and 4.27 keys the storage by a raw pointer (the walker reads it):
        // the pass must run there too, not skip silently behind a ">= 5.0" gate.
        g_cachedUEVersion = 427;
        Aura::ContainerScanStats st4;
        Aura::FindReferencesToUObject(reinterpret_cast<uintptr_t>(spTarget), 32, &st4);
        check("SPARSEREFS ⭐ R7-S2: on UE 4.27 the sparse pass runs (the two unreadable delegates are counted)",
              st4.sparseUnlocated == 2 && !st4.sparseSkipped, std::to_string(st4.sparseUnlocated).c_str());
        // ...and a key that is not a pointer (an FObjectKey-keyed 4.23-4.26 build) is refused and REPORTED, the way
        // WalkSparseDelegateBindings refuses it -- not walked as if it were one.
        uintptr_t svKeySp = 0;
        memcpy(&svKeySp, spOuterSlot, 8);
        const uintptr_t fobjectKey = 0x0000000500000003ull;   // { ObjectIndex 3, SerialNumber 5 }
        memcpy(spOuterSlot, &fobjectKey, 8);
        Aura::ContainerScanStats stK;
        Aura::FindReferencesToUObject(reinterpret_cast<uintptr_t>(spTarget), 32, &stK);
        check("SPARSEREFS ⭐ R7-S2: a non-pointer outer key is refused and reported skipped, nothing read",
              stK.sparseSkipped && stK.sparseUnlocated == 0, std::to_string(stK.sparseUnlocated).c_str());
        memcpy(spOuterSlot, &svKeySp, 8);

        // [R7-S4] An implausible storage header (ArrayNum past SANITY_MAX_CONTAINER_NUM -- a mis-resolved storage)
        // is refused by ReadTMapHeader, which still FILLS the header before saying no: the pass must not walk it.
        int32_t svNumSp = 0;
        memcpy(&svNumSp, spOuter + 0x08, 4);
        const int32_t hugeNum = Grimoire::SANITY_MAX_CONTAINER_NUM + 1;
        memcpy(spOuter + 0x08, &hugeNum, 4);
        Aura::ContainerScanStats stH;
        Aura::FindReferencesToUObject(reinterpret_cast<uintptr_t>(spTarget), 32, &stH);
        check("SPARSEREFS ⭐ R7-S4: an implausible storage header is not walked, and the pass is reported skipped",
              stH.sparseUnlocated == 0 && stH.sparseSkipped, std::to_string(stH.sparseUnlocated).c_str());
        memcpy(spOuter + 0x08, &svNumSp, 4);

        // [R7-X2] The storage was never located (the AOB scan failed, or -- measured on the 4.27 editor -- the
        // validator refused it): the pass cannot run, and "none found" is then not a negative. Before the fix the
        // sweep said nothing, so the UI blamed the game.
        Genau::s_sparseDelegatesCache.store(0);
        Genau::s_sparseDelegatesScanned.store(true);
        Aura::ContainerScanStats stU;
        Aura::FindReferencesToUObject(reinterpret_cast<uintptr_t>(spTarget), 32, &stU);
        check("SPARSEREFS ⭐ R7-X2: an unlocated sparse storage is reported skipped, nothing read",
              stU.sparseSkipped && stU.sparseUnlocated == 0, std::to_string(stU.sparseUnlocated).c_str());
        // ...but not before 4.23, where no sparse delegate exists to miss.
        g_cachedUEVersion = 422;
        Aura::ContainerScanStats stP;
        Aura::FindReferencesToUObject(reinterpret_cast<uintptr_t>(spTarget), 32, &stP);
        check("SPARSEREFS R7-X2 control: pre-4.23 with no storage reports no gap",
              !stP.sparseSkipped && stP.sparseUnlocated == 0);
        Genau::s_sparseDelegatesCache.store(reinterpret_cast<uintptr_t>(spOuter));
        g_cachedUEVersion = 505;

        Genau::s_sparseDelegatesCache.store(savedStoreSp);
        Genau::s_sparseDelegatesScanned.store(savedScannedSp);
        DynOff::bCasePreservingName = savedCpnSp;
        g_cachedUEVersion           = savedVerSp;
    }

    // -- WALKCLASSEXUNMAPPED-2026-09-12 -- an unreadable class is refused, and NOT memoized --------------------------
    //
    // [A2-WALKCLASSEX-UNMAPPED] WalkClass returns {Address, PropertiesSize 0} from its read-fault exit, and WalkClassEx
    // passed `true` as the read verdict, so the value test accepted and memoized that empty walk FOREVER -- and Aura's
    // two refusal gates (`WalkClassEx(cls).Address != cls`) passed with it. A decommitted page is the transient fault;
    // re-committing it is the page coming back. GetCachedStructFields is the twin.
    {
        blk("WALKCLASSEXUNMAPPED - an unreadable class is refused and not memoized, so it walks when it comes back");
        ResetCancel();
        uint8_t* wcPage = static_cast<uint8_t*>(VirtualAlloc(nullptr, 0x1000, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
        check("WALKCLASSEXUNMAPPED setup: committed a page", wcPage != nullptr);
        if (wcPage) {
            const uintptr_t wcA = reinterpret_cast<uintptr_t>(wcPage);           // WalkClassEx's subject
            const uintptr_t wcS = reinterpret_cast<uintptr_t>(wcPage + 0x800);   // GetCachedStructFields'
            VirtualFree(wcPage, 0x1000, MEM_DECOMMIT);                           // the transient fault

            const auto& ex1 = Ubel::WalkClassEx(wcA);
            check("WALKCLASSEXUNMAPPED ⭐: WalkClassEx refuses an unreadable class (Aura's gate sees Address != cls)",
                  ex1.Address != wcA);
            const auto& sf1 = Ubel::GetCachedStructFields(wcS);
            {
                std::lock_guard<std::mutex> lk(Ubel::s_structFieldCacheMutex);
                check("WALKCLASSEXUNMAPPED ⭐: ...and GetCachedStructFields, the twin, memoizes nothing for it",
                      sf1.empty() && Ubel::s_structFieldCache.count(wcS) == 0);
            }

            // The page comes back, holding a plausible class at each subject.
            check("WALKCLASSEXUNMAPPED setup: re-committed the page",
                  VirtualAlloc(wcPage, 0x1000, MEM_COMMIT, PAGE_READWRITE) != nullptr);
            const int32_t wcProps = 0x40;
            memcpy(wcPage + DynOff::USTRUCT_PROPSSIZE, &wcProps, sizeof(wcProps));
            memcpy(wcPage + 0x800 + DynOff::USTRUCT_PROPSSIZE, &wcProps, sizeof(wcProps));
            const auto& ex2 = Ubel::WalkClassEx(wcA);
            check("WALKCLASSEXUNMAPPED ⭐: once the page is back, the class walks -- the fault was not memoized",
                  ex2.Address == wcA && ex2.PropertiesSize == wcProps, std::to_string(ex2.PropertiesSize).c_str());
            Ubel::GetCachedStructFields(wcS);
            {
                std::lock_guard<std::mutex> lk(Ubel::s_structFieldCacheMutex);
                check("WALKCLASSEXUNMAPPED control: ...and the twin memoizes a readable struct",
                      Ubel::s_structFieldCache.count(wcS) == 1);
            }
            // Deliberately NOT released: the memos now hold this address for the process, which ends soon.
        }
    }

    // -- LAZYLATCHGUESS-2026-09-12 -- a lazy array's size is the engine's, and only a real one is latched ----------
    //
    // [A2-LAZY-LATCH-GUESS] InferScalarSize("LazyObjectProperty") is a VERSION GUESS (>= 503 ? 0x18 : 0x1C).
    // ValidateArrayElemSize and ResolveInnerSize let it override the engine's raw ElementSize, and the array reader fed
    // the result into the envelope latch, which logged "payload envelope measured". A 5.0-5.2 title mis-resolved as 504
    // strode 0x18 over 0x1C elements and latched a false +0x08.
    {
        blk("LAZYLATCHGUESS - a lazy array's size comes from the engine, and only a real one is latched");
        ResetCancel();
        const uint32_t savedVerLz   = g_cachedUEVersion;
        const int      savedLatchLz = DynOff::LAZYPTR_GUID;
        g_cachedUEVersion    = 504;   // mis-resolved across 5.2/5.3: the guess says 0x18
        DynOff::LAZYPTR_GUID = -1;    // nothing measured yet

        const int32_t lzKept = Ubel::ValidateArrayElemSize(0x1C, "LazyObjectProperty");
        check("LAZYLATCHGUESS ⭐: a real 0x1C ElementSize is kept, not replaced by the version guess",
              lzKept == 0x1C, std::to_string(lzKept).c_str());
        check("LAZYLATCHGUESS ⭐: ...and the envelope it latches is the measured +0x0C",
              DynOff::LAZYPTR_GUID == 0x0C, std::to_string(DynOff::LAZYPTR_GUID).c_str());

        DynOff::LAZYPTR_GUID = -1;
        alignas(8) static uint8_t lzInner[0x100] = {};
        const int32_t lzRaw = 0x1C;
        memcpy(lzInner + DynOff::FPROPERTY_ELEMSIZE, &lzRaw, sizeof(lzRaw));
        const int32_t lzInnerSize = Ubel::ResolveInnerSize(reinterpret_cast<uintptr_t>(lzInner), "LazyObjectProperty");
        check("LAZYLATCHGUESS ⭐: ResolveInnerSize reads the engine's ElementSize for a lazy inner too",
              lzInnerSize == 0x1C, std::to_string(lzInnerSize).c_str());

        DynOff::LAZYPTR_GUID = -1;
        const int32_t lzGarbage = Ubel::ValidateArrayElemSize(0x77, "LazyObjectProperty");
        check("LAZYLATCHGUESS guard: a garbage ElementSize still falls back to the version default, as before",
              lzGarbage == 0x18, std::to_string(lzGarbage).c_str());
        check("LAZYLATCHGUESS guard: ...and latches nothing -- a fallback is not a measurement",
              DynOff::LAZYPTR_GUID == -1, std::to_string(DynOff::LAZYPTR_GUID).c_str());

        // The reader is handed a size its caller already derived; re-measuring THAT latched a fallback as "measured".
        alignas(8) static uint8_t lzData[2 * 0x18] = {};
        alignas(8) static uint8_t lzInst[0x20] = {};
        const uintptr_t lzDataAddr = reinterpret_cast<uintptr_t>(lzData);
        const int32_t   lzNum      = 2;
        memcpy(lzInst + 0x00, &lzDataAddr, sizeof(lzDataAddr));
        memcpy(lzInst + 0x08, &lzNum, sizeof(lzNum));
        memcpy(lzInst + 0x0C, &lzNum, sizeof(lzNum));
        DynOff::LAZYPTR_GUID = -1;
        const auto lzr = Ubel::ReadLazyObjectArrayElements(reinterpret_cast<uintptr_t>(lzInst), 0, 0x18, 0, 16);
        check("LAZYLATCHGUESS setup: the reader read the array", lzr.ok, lzr.error.c_str());
        check("LAZYLATCHGUESS ⭐: the array reader latches nothing from the size it is handed",
              DynOff::LAZYPTR_GUID == -1, std::to_string(DynOff::LAZYPTR_GUID).c_str());

        DynOff::LAZYPTR_GUID = savedLatchLz;
        g_cachedUEVersion    = savedVerLz;
    }

    // -- CDOSCOPE-2026-09-12 -- every preview class on the chain is credited; a nested row is never previewed --------
    {
        blk("CDOSCOPE - PreviewAncestorsOf credits every preview class above a class; nested rows get no preview");
        // [A4-CDOSCOPE-ANCESTOR] The walk broke at the NEAREST preview class (and an exact hit skipped it), so an
        // ancestor row read "(CDO default)" while Force / Freeze on it acted on live instances.
        // A <- B <- C (C's super is B, B's super is A); D's super is itself.
        const uintptr_t cA = 0xA000, cB = 0xB000, cC = 0xC000, cD = 0xD000;
        auto cdSuperOf = [&](uintptr_t c) -> uintptr_t {
            return c == cC ? cB : c == cB ? cA : c == cD ? cD : 0;
        };
        auto cdAll = [&](uintptr_t c) { return c == cA || c == cB || c == cC; };
        const auto upC = Aura::PreviewAncestorsOf(cC, cdAll, cdSuperOf);
        check("CDOSCOPE ⭐: a live C is credited to EVERY preview ancestor, nearest first -- not to B alone",
              upC.size() == 2 && upC[0] == cB && upC[1] == cA, std::to_string(upC.size()).c_str());
        const auto upB = Aura::PreviewAncestorsOf(cB, cdAll, cdSuperOf);
        check("CDOSCOPE ⭐: the walk starts at the SUPER -- an exact B still credits A",
              upB.size() == 1 && upB[0] == cA, std::to_string(upB.size()).c_str());
        check("CDOSCOPE ⭐: ...and a root preview class credits nothing above itself",
              Aura::PreviewAncestorsOf(cA, cdAll, cdSuperOf).empty());
        auto cdOnlyA = [&](uintptr_t c) { return c == cA; };
        const auto upCa = Aura::PreviewAncestorsOf(cC, cdOnlyA, cdSuperOf);
        check("CDOSCOPE control: a non-preview class in between is passed over, not a stop",
              upCa.size() == 1 && upCa[0] == cA);
        check("CDOSCOPE control: a self-looping chain terminates", Aura::PreviewAncestorsOf(cD, cdAll, cdSuperOf).empty());

        // [A4-CDOSCOPE-NESTED-PREVIEW] A Deep search matching a direct field AND a nested leaf of the same class put that
        // class in the preview map, and the nested row -- keyed by its root field's defining class, its offset
        // informational only -- was previewed at inst + that offset: a UObject header word.
        alignas(8) static uint8_t cdInst[0x40] = {};
        const int32_t cdVal = 1234;
        memcpy(cdInst + 0x20, &cdVal, sizeof(cdVal));
        const uintptr_t cdCls = 0xE000;
        std::vector<Aura::PropertyMatch> cdRows(2);
        cdRows[0].classAddr  = cdCls;
        cdRows[0].propType   = "IntProperty";
        cdRows[0].propOffset = 0x20;
        cdRows[0].propSize   = 4;
        cdRows[1] = cdRows[0];
        cdRows[1].isNested   = true;
        cdRows[1].propOffset = 0x08;
        std::unordered_map<uintptr_t, uintptr_t> cdMap{ { cdCls, reinterpret_cast<uintptr_t>(cdInst) } };
        Ubel::ResolvePropertyPreviews(cdRows, cdMap);
        check("CDOSCOPE control: the direct row is previewed", !cdRows[0].preview.empty(), cdRows[0].preview.c_str());
        check("CDOSCOPE ⭐: the nested row sharing its class is NOT previewed", cdRows[1].preview.empty(),
              cdRows[1].preview.c_str());
    }

    // -- OFFSETS-UNMEASURED-2026-09-12 -- a give-up stores validated=false; the reset forgets; the verdict says why ------
    {
        blk("OFFSETS - a give-up stores validated=false; UE5_Shutdown's reset forgets the verdict; the reason text");
        // [W5-OFFSETS-UNMEASURED] Both early returns relied on the flag's INITIAL false and never stored one, so a second
        // UE5_Init (CE Disable -> Enable) taking one after a validated run kept the old TRUE -- "validated" beside a
        // fallback reason, over default offsets. UE5_Shutdown reset none of the three either.
        // A pool with no Guid / Vector struct, so the run takes the FIRST give-up. Every object points into zeroed bytes
        // with room past the deepest probe offset (+0x70), so no probe reads beyond them. UE 501 skips the version-default
        // block, so the only DynOff it writes are the three saved here (CPN / Outer, FProperty mode).
        const auto svCpn   = DynOff::bCasePreservingName;
        const auto svOuter = DynOff::UOBJECT_OUTER;
        const auto svFProp = DynOff::bUseFProperty;
        alignas(16) static uint8_t ofZero[0x400] = {};
        FakePool ofPool;
        ofPool.Build(2);
        for (int i = 0; i < 2; ++i) {
            const uintptr_t z = reinterpret_cast<uintptr_t>(ofZero) + static_cast<uintptr_t>(i) * 0x100;
            memcpy(ofPool.chunks[0].data() + static_cast<size_t>(i) * FakePool::kItemSize, &z, sizeof(z));
        }
        Aura::InitWithExtendedLayout(ofPool.Addr(), FakePool::kItemSize);

        DynOff::bOffsetsValidated.store(true);   // a PREVIOUS run measured everything
        DynOff::bOffsetsProbeRan.store(true);
        DynOff::g_offsetsFallbackReason = "";
        const bool ofRet = Genau::ValidateAndFixOffsets(501);
        check("OFFSETS setup: a pool with no Guid / Vector takes the no-guid-or-vector give-up",
              !ofRet && std::string(DynOff::g_offsetsFallbackReason) == "no-guid-or-vector-struct",
              DynOff::g_offsetsFallbackReason);
        check("OFFSETS ⭐: the give-up stores validated=false -- it never keeps a previous run's TRUE",
              !DynOff::bOffsetsValidated.load());
        check("OFFSETS control: the give-up still marks detection as run (the &GEngine gates key on it)",
              DynOff::bOffsetsProbeRan.load());

        DynOff::ResetOffsetsVerdict();
        check("OFFSETS ⭐: the shutdown reset forgets all three",
              !DynOff::bOffsetsValidated.load() && !DynOff::bOffsetsProbeRan.load()
              && std::string(DynOff::g_offsetsFallbackReason).empty());

        check("OFFSETS ⭐: before any detection the verdict says so",
              std::string(DynOff::OffsetsVerdictReason(false, false, "")) == "probe-not-run");
        check("OFFSETS ⭐: ...even beside a stale reason",
              std::string(DynOff::OffsetsVerdictReason(false, true, "x")) == "probe-not-run");
        check("OFFSETS control: a measured run has no reason",
              std::string(DynOff::OffsetsVerdictReason(true, true, "")).empty());
        check("OFFSETS control: an unmeasured run gives its reason",
              std::string(DynOff::OffsetsVerdictReason(true, false, "unmeasured:elemsize")) == "unmeasured:elemsize");
        check("OFFSETS ⭐: an unmeasured run with no recorded reason still reads as unmeasured, never as measured",
              std::string(DynOff::OffsetsVerdictReason(true, false, "")) == "unmeasured");

        Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);   // the main fixture, for any later block
        DynOff::bCasePreservingName = svCpn;
        DynOff::UOBJECT_OUTER       = svOuter;
        DynOff::bUseFProperty       = svFProp;
        DynOff::bOffsetsValidated.store(false);
        DynOff::bOffsetsProbeRan.store(false);
        DynOff::g_offsetsFallbackReason = "";
    }

    // -- SEETHRU-PROBE-SUBSTRING-2026-09-23 -- the probe asks a CLASS found by path; internal lookups gate on derivation --
    //
    // ⛔ POOL-FAKING, like OFFSETS: own object pool AND own name pool, last. [SEETHRU-PROBE-SUBSTRING] See-through's
    // producer probe resolved "Actor" through UE5_FindInstanceOfClass -- a class-name SUBSTRING match that falls back to
    // the FIRST matching CDO -- and on DumperTest Shipping 5.4 that was Default__ActorChannel, a UChannel with no
    // SetActorHiddenInGame, so See-through ON refused (-3) on a build that has it. Phase A is that pool: the first object
    // whose class name contains "actor" is ActorChannel's CDO, and nothing live matches. The CheatManager rows are the same
    // trap in the stock engine: UCheatManagerExtension (Engine) and GAS's UAbilitySystemCheatManagerExtension.
    {
        blk("PROBECLASS - the See-through probe finds AActor's function when \"actor\" first matches another CDO");
        ResetCancel();

        enum : int32_t { nClass = 1, nEngine, nPackage, nObject, nChannel, nActorChannel, nActor, nCdoActorChannel,
                         nCdoActor, nFunction, nSetHidden, nBPDoor, nBPDoor0, nActorChannel0, nCheatMgr, nCheatExt,
                         nCdoCheatExt, nCdoCheatMgr, nAbilityExt, nAbilityExt0, nCheatMgr0, nCdoBPDoor, nNames };
        const char* pcNames[nNames] = { "", "Class", "/Script/Engine", "Package", "Object", "Channel", "ActorChannel",
                                        "Actor", "Default__ActorChannel", "Default__Actor", "Function",
                                        "SetActorHiddenInGame", "BP_Door_C", "BP_Door_C_0", "ActorChannel_0",
                                        "CheatManager", "CheatManagerExtension", "Default__CheatManagerExtension",
                                        "Default__CheatManager", "AbilitySystemCheatManagerExtension",
                                        "AbilitySystemCheatManagerExtension_0", "CheatManager_0",
                                        "Default__BP_Door_C" };
        static uint8_t pcEntry[nNames][0x40] = {};
        static uintptr_t pcChunk[nNames + 1] = {};
        for (int i = 1; i < nNames; ++i) {
            memcpy(pcEntry[i] + 0x10, pcNames[i], strlen(pcNames[i]) + 1);
            pcChunk[i] = reinterpret_cast<uintptr_t>(pcEntry[i]);
        }
        static uintptr_t pcChunks[2] = { reinterpret_cast<uintptr_t>(pcChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(pcChunks), 0x10);
        check("PROBECLASS setup: the name pool resolves the longest name",
              Serie::GetString(nAbilityExt0) == "AbilitySystemCheatManagerExtension_0",
              Serie::GetString(nAbilityExt0).c_str());

        const bool     svCpnP   = DynOff::bCasePreservingName;
        const bool     svFPropP = DynOff::bUseFProperty;
        const uint32_t svVerP   = g_cachedUEVersion;
        DynOff::bCasePreservingName = false;
        DynOff::bUseFProperty       = true;
        g_cachedUEVersion           = 504;

        // One zeroed 0x200-byte blob per UObject -- clear of every UStruct / UFunction offset the walks read.
        enum { bMeta, bPkgCls, bPkg, bObject, bChannel, bActorChannel, bActor, bFunctionCls, bSetHidden, bCdoActorChannel,
               bCdoActor, bBPDoor, bDoor0, bChan0, bCheatMgr, bCheatExt, bCdoCheatExt, bCdoCheatMgr, bAbilityExt,
               bAbility0, bCheat0, bCdoBPDoor, kBlobs };
        alignas(16) static uint8_t pcB[kBlobs][0x200] = {};
        auto A     = [&](int b) { return b < 0 ? uintptr_t{0} : reinterpret_cast<uintptr_t>(pcB[b]); };
        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto obj = [&](int b, int cls, int32_t name, int outer) {
            memset(pcB[b], 0, sizeof(pcB[b]));
            putP(pcB[b], Grimoire::OFF_UOBJECT_CLASS, A(cls));
            put32(pcB[b], Grimoire::OFF_UOBJECT_NAME, name);
            putP(pcB[b], DynOff::UOBJECT_OUTER, A(outer));
        };
        // A native UClass: its class IS the meta-class "Class", its outer the /Script/Engine package.
        auto klass = [&](int b, int32_t name, int super) {
            obj(b, bMeta, name, bPkg);
            putP(pcB[b], DynOff::USTRUCT_SUPER, A(super));
        };
        obj(bMeta, bMeta, nClass, -1);
        obj(bPkgCls, bMeta, nPackage, -1);
        obj(bPkg, bPkgCls, nEngine, -1);
        klass(bObject, nObject, -1);
        klass(bChannel, nChannel, bObject);
        klass(bActorChannel, nActorChannel, bChannel);
        klass(bActor, nActor, bObject);
        obj(bFunctionCls, bMeta, nFunction, -1);
        obj(bSetHidden, bFunctionCls, nSetHidden, bActor);
        putP(pcB[bActor], DynOff::USTRUCT_CHILDREN, A(bSetHidden));   // AActor declares SetActorHiddenInGame
        obj(bCdoActorChannel, bActorChannel, nCdoActorChannel, bPkg);
        obj(bCdoActor, bActor, nCdoActor, bPkg);
        klass(bBPDoor, nBPDoor, bActor);                               // no "actor" in its name, yet an AActor
        obj(bDoor0, bBPDoor, nBPDoor0, -1);
        obj(bCdoBPDoor, bBPDoor, nCdoBPDoor, bPkg);                    // a SUBCLASS's CDO: derives from Actor
        obj(bChan0, bActorChannel, nActorChannel0, -1);
        klass(bCheatMgr, nCheatMgr, bObject);
        klass(bCheatExt, nCheatExt, bObject);                          // UCheatManagerExtension : UObject
        obj(bCdoCheatExt, bCheatExt, nCdoCheatExt, bPkg);
        obj(bCdoCheatMgr, bCheatMgr, nCdoCheatMgr, bPkg);
        klass(bAbilityExt, nAbilityExt, bCheatExt);
        obj(bAbility0, bAbilityExt, nAbilityExt0, -1);
        obj(bCheat0, bCheatMgr, nCheatMgr0, -1);

        FakePool pcPool;
        pcPool.Build(11);
        auto slot = [&](int i, int b) {
            const uintptr_t o = A(b);
            memcpy(pcPool.chunks[0].data() + static_cast<size_t>(i) * FakePool::kItemSize, &o, sizeof(o));
        };
        // Phase A -- GObjects order as measured: ActorChannel's CDO first, and no live instance of anything. A
        // subclass's CDO sits before Actor's own, so a fallback that took ANY derived CDO would be caught too.
        slot(0, bCdoActorChannel);
        slot(1, bActorChannel);
        slot(2, bActor);
        slot(3, bCdoBPDoor);
        slot(4, bCdoActor);
        slot(5, bCdoCheatExt);
        slot(6, bCdoCheatMgr);
        Aura::InitWithExtendedLayout(pcPool.Addr(), FakePool::kItemSize);

        auto nm = [](uintptr_t o) { return o ? Ubel::GetName(o) : std::string("(0)"); };
        auto probeHas = [](uintptr_t cls, const char* fn) {   // Schlacht's FindFuncByName: the class, then its supers
            FunctionInfo fi;
            return Ubel::ResolveFunctionInChain(cls, fn,
                [](uintptr_t c) { return Ubel::WalkFunctions(c); },
                [](uintptr_t c, uintptr_t& s) {
                    return Macht::ReadSafe(c + static_cast<uintptr_t>(DynOff::USTRUCT_SUPER), s);
                }, fi);
        };

        const auto subA = Aura::FindInstancesByClass("Actor", false, 100);
        check("PROBECLASS control: the fixture is the measured shape -- \"actor\" first matches Default__ActorChannel",
              !subA.results.empty() && subA.results[0].addr == A(bCdoActorChannel),
              subA.results.empty() ? "(none)" : subA.results[0].name.c_str());

        const uintptr_t actorCls = Aura::FindClassByPath("/Script/Engine.Actor");
        check("PROBECLASS ⭐: /Script/Engine.Actor resolves to the AActor UClass itself", actorCls == A(bActor),
              nm(actorCls).c_str());
        check("PROBECLASS ⭐: ...so the probe finds SetActorHiddenInGame and does not refuse",
              probeHas(actorCls, "SetActorHiddenInGame"));
        check("PROBECLASS control: the class the substring gate chose has no SetActorHiddenInGame -- the -3",
              !probeHas(Ubel::GetClass(A(bCdoActorChannel)), "SetActorHiddenInGame"));
        check("PROBECLASS ⭐: an object at the path that is not a class is refused",
              Aura::FindClassByPath("/Script/Engine.Default__Actor") == 0);
        check("PROBECLASS control: a path nothing lives at resolves to nothing",
              Aura::FindClassByPath("/Script/Engine.Pawn") == 0);

        const uintptr_t actorA = Aura::FindLiveOrDefaultOf("Actor");
        check("PROBECLASS ⭐: with nothing live, the fallback is Actor's OWN CDO -- not ActorChannel's, nor a subclass's",
              actorA == A(bCdoActor), nm(actorA).c_str());
        const uintptr_t cheatA = Aura::FindLiveOrDefaultOf("CheatManager");
        check("PROBECLASS ⭐: ...and CheatManager's, not UCheatManagerExtension's, which the substring gate meets first",
              cheatA == A(bCdoCheatMgr), nm(cheatA).c_str());

        // Phase B -- live instances. Each one the substring gate would take (or miss) sits BEFORE the right one.
        slot(7, bChan0);      // "ActorChannel_0": class name contains "actor", not an AActor
        slot(8, bAbility0);   // GAS's cheat extension: class name contains "CheatManager", not a UCheatManager
        slot(9, bDoor0);      // BP_Door_C_0: an AActor whose class name has no "actor" in it
        slot(10, bCheat0);
        Aura::InitWithExtendedLayout(pcPool.Addr(), FakePool::kItemSize);

        const uintptr_t actorB = Aura::FindLiveOrDefaultOf("Actor");
        check("PROBECLASS ⭐: a live instance DERIVED from Actor wins, whatever its class is called",
              actorB == A(bDoor0), nm(actorB).c_str());
        const uintptr_t cheatB = Aura::FindLiveOrDefaultOf("CheatManager");
        check("PROBECLASS ⭐: the live CheatManager wins over a live cheat EXTENSION at a lower index",
              cheatB == A(bCheat0), nm(cheatB).c_str());
        check("PROBECLASS control: the name is compared case-insensitively, like FindInstancesByClass",
              Aura::FindLiveOrDefaultOf("cheatmanager") == A(bCheat0));

        // A walk cut short must not hand back a CDO it met on the way: a live instance may sit past the cut.
        Tot::g_perCommand.store(true);
        const uintptr_t cut = Aura::FindLiveOrDefaultOf("Actor");
        ResetCancel();
        check("PROBECLASS control: a cancelled walk returns nothing", cut == 0, nm(cut).c_str());

        // The refusal survives: a build whose AActor really lacks the function is still refused.
        putP(pcB[bActor], DynOff::USTRUCT_CHILDREN, 0);
        check("PROBECLASS ⭐: a class that really lacks SetActorHiddenInGame still refuses",
              Aura::FindClassByPath("/Script/Engine.Actor") == A(bActor)
                  && !probeHas(A(bActor), "SetActorHiddenInGame"));

        Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);   // the main fixture, for any later block
        DynOff::bCasePreservingName = svCpnP;
        DynOff::bUseFProperty       = svFPropP;
        g_cachedUEVersion           = svVerP;
    }

    // -- [VND583-02] UField::Next is MEASURED in FProperty mode -------------------------------
    //
    // Upstream's The Pathless config (a 4.25-layout licensee fork) puts UField::Next at 0x30: its
    // UObject carries one more 8-byte member, so every UField / UStruct / UFunction key sits +8
    // later. No title on this machine has that shape, so it is BUILT here: a native class
    // "KismetSystemLibrary" whose three UFunctions are chained at +0x30, with a pointer that is NOT
    // a Function (the fork's extra member) at +0x28. Installs its OWN pool and name pool, like
    // PROBECLASS above, and puts the main fixture back.
    {
        blk("UFIELDNEXT - UField::Next is measured in FProperty mode, on a built Pathless-shaped chain");
        ResetCancel();

        enum : int32_t { uClass = 1, uFunction, uKsl, uFA, uFB, uFC, uNames };
        const char* ufNames[uNames] = { "", "Class", "Function", "KismetSystemLibrary", "FuncA", "FuncB", "FuncC" };
        static uint8_t ufEntry[uNames][0x40] = {};
        static uintptr_t ufChunk[uNames + 1] = {};
        for (int i = 1; i < uNames; ++i) {
            memcpy(ufEntry[i] + 0x10, ufNames[i], strlen(ufNames[i]) + 1);
            ufChunk[i] = reinterpret_cast<uintptr_t>(ufEntry[i]);
        }
        static uintptr_t ufChunks[2] = { reinterpret_cast<uintptr_t>(ufChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(ufChunks), 0x10);
        check("UFIELDNEXT setup: the name pool resolves KismetSystemLibrary",
              Serie::GetString(uKsl) == "KismetSystemLibrary", Serie::GetString(uKsl).c_str());

        const bool     svCpnU   = DynOff::bCasePreservingName;
        const bool     svFPropU = DynOff::bUseFProperty;
        const int      svNextU  = DynOff::UFIELD_NEXT;
        const int      svChildU = DynOff::USTRUCT_CHILDREN;
        const uint32_t svVerU   = g_cachedUEVersion;
        DynOff::bCasePreservingName = false;
        DynOff::bUseFProperty       = true;
        g_cachedUEVersion           = 425;
        DynOff::USTRUCT_CHILDREN    = 0x50;   // the fork's Children: +8, like every key after UObject

        enum { bMeta, bFnCls, bKsl, bFA, bFB, bFC, kUB };
        alignas(16) static uint8_t ufB[kUB][0x200] = {};
        auto A     = [&](int b) { return reinterpret_cast<uintptr_t>(ufB[b]); };
        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto build = [&](int nextOff) {
            for (auto& b : ufB) memset(b, 0, sizeof(b));
            putP(ufB[bMeta], Grimoire::OFF_UOBJECT_CLASS, A(bMeta));   put32(ufB[bMeta], Grimoire::OFF_UOBJECT_NAME, uClass);
            putP(ufB[bFnCls], Grimoire::OFF_UOBJECT_CLASS, A(bMeta));  put32(ufB[bFnCls], Grimoire::OFF_UOBJECT_NAME, uFunction);
            putP(ufB[bKsl], Grimoire::OFF_UOBJECT_CLASS, A(bMeta));    put32(ufB[bKsl], Grimoire::OFF_UOBJECT_NAME, uKsl);
            putP(ufB[bKsl], DynOff::USTRUCT_CHILDREN, A(bFA));
            const int fn[3] = { bFA, bFB, bFC };
            for (int i = 0; i < 3; ++i) {
                putP(ufB[fn[i]], Grimoire::OFF_UOBJECT_CLASS, A(bFnCls));
                put32(ufB[fn[i]], Grimoire::OFF_UOBJECT_NAME, uFA + i);
                if (nextOff != 0x28) putP(ufB[fn[i]], 0x28, A(bKsl));   // the fork's extra member: a pointer, not a Function
                if (i < 2) putP(ufB[fn[i]], nextOff, A(fn[i + 1]));
            }
        };

        FakePool ufPool;
        ufPool.Build(kUB);
        for (int i = 0; i < kUB; ++i) {
            const uintptr_t o = A(i);
            memcpy(ufPool.chunks[0].data() + static_cast<size_t>(i) * FakePool::kItemSize, &o, sizeof(o));
        }
        Aura::InitWithExtendedLayout(ufPool.Addr(), FakePool::kItemSize);

        // The Pathless shape.
        build(0x30);
        DynOff::UFIELD_NEXT = 0x28;   // what FProperty mode used to leave in place
        const size_t atDefault = Ubel::WalkFunctions(A(bKsl)).size();
        check("UFIELDNEXT the defect: at the old default 0x28 the walk sees ONE function, not three",
              atDefault == 1, std::to_string(atDefault).c_str());
        const int probed = Genau::ProbeUFieldNextFProperty();
        check("UFIELDNEXT ⭐: the probe measures 0x30 on the Pathless-shaped chain",
              probed == 0x30, std::to_string(probed).c_str());
        DynOff::UFIELD_NEXT = probed > 0 ? probed : 0x28;
        const size_t atProbed = Ubel::WalkFunctions(A(bKsl)).size();
        check("UFIELDNEXT ⭐: at the measured offset the walk sees all three functions",
              atProbed == 3, std::to_string(atProbed).c_str());

        // The stock shape keeps the default, and a class with no chain measures nothing.
        build(0x28);
        check("UFIELDNEXT control: a stock chain measures the default 0x28", Genau::ProbeUFieldNextFProperty() == 0x28);
        putP(ufB[bKsl], DynOff::USTRUCT_CHILDREN, 0);
        check("UFIELDNEXT control: no function chain gives -1 (the caller keeps the default and says so)",
              Genau::ProbeUFieldNextFProperty() == -1);

        Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);   // the main fixture, for any later block
        DynOff::bCasePreservingName = svCpnU;
        DynOff::bUseFProperty       = svFPropU;
        DynOff::UFIELD_NEXT         = svNextU;
        DynOff::USTRUCT_CHILDREN    = svChildU;
        g_cachedUEVersion           = svVerU;
    }

    // -- [VND583-03] a NameProperty's alignment follows the engine version -----------------
    // ResolveElementAlignment is what every TMap / TSet / TOptional geometry asks. On non-CPN
    // 4.11-4.21 FName is 8-aligned (a union with uint64), so a 4 there shortens the stride.
    {
        blk("FNAMEALIGN - ResolveElementAlignment gives FName its version's alignment");
        const uint32_t svVerA = g_cachedUEVersion;
        const bool     svCpnA = DynOff::bCasePreservingName;
        DynOff::bCasePreservingName = false;
        DynOff::bFNameAlignProbed = true;     // "probed, nothing measured": the version rule answers
        DynOff::FNAME_ALIGN_MEASURED = 0;
        g_cachedUEVersion = 418;
        const int a418 = Ubel::ResolveElementAlignment("NameProperty", 8, 0);
        check("FNAMEALIGN ⭐: non-CPN 4.18 -> 8", a418 == 8, std::to_string(a418).c_str());
        g_cachedUEVersion = 427;
        const int a427 = Ubel::ResolveElementAlignment("NameProperty", 8, 0);
        check("FNAMEALIGN control: 4.27 -> 4", a427 == 4, std::to_string(a427).c_str());
        g_cachedUEVersion = 418; DynOff::bCasePreservingName = true;
        const int a418c = Ubel::ResolveElementAlignment("NameProperty", 12, 0);
        check("FNAMEALIGN control: case-preserving 4.18 -> 4", a418c == 4, std::to_string(a418c).c_str());
        g_cachedUEVersion = svVerA;
        DynOff::bCasePreservingName = svCpnA;
    }

    // -- [VND583-03] ... and a MEASURED alignment beats the version rule -------------------------
    // A built ScriptStruct "CollisionProfileName" (8 bytes, one FName) carries the answer in its
    // MinAlignment. A NameProperty-classed object of the SAME name sits before it in the pool,
    // shaped to answer 8 -- the class check must skip it. Own pool and name pool, like UFIELDNEXT.
    {
        blk("FNAMEMEASURE - alignof(FName) is measured on a single-FName ScriptStruct");
        enum : int32_t { nClass = 1, nScriptStruct, nNameProp, nCpn, nNames };
        const char* fmNames[nNames] = { "", "Class", "ScriptStruct", "NameProperty", "CollisionProfileName" };
        static uint8_t fmEntry[nNames][0x40] = {};
        static uintptr_t fmChunk[nNames + 1] = {};
        for (int i = 1; i < nNames; ++i) {
            memcpy(fmEntry[i] + 0x10, fmNames[i], strlen(fmNames[i]) + 1);
            fmChunk[i] = reinterpret_cast<uintptr_t>(fmEntry[i]);
        }
        static uintptr_t fmChunks[2] = { reinterpret_cast<uintptr_t>(fmChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(fmChunks), 0x10);

        const uint32_t svVerM   = g_cachedUEVersion;
        const bool     svCpnM   = DynOff::bCasePreservingName;
        const bool     svValidM = DynOff::bOffsetsValidated.load();
        DynOff::bCasePreservingName = false;
        DynOff::bOffsetsValidated   = true;

        enum { bMeta, bSSCls, bNPCls, bDecoy, bStruct, kMB };
        alignas(16) static uint8_t fmB[kMB][0x200] = {};
        auto A     = [&](int b) { return reinterpret_cast<uintptr_t>(fmB[b]); };
        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto build = [&](bool withStruct, int32_t structSize, int32_t structAlign) {
            for (auto& b : fmB) memset(b, 0, sizeof(b));
            putP(fmB[bMeta], Grimoire::OFF_UOBJECT_CLASS, A(bMeta));    put32(fmB[bMeta], Grimoire::OFF_UOBJECT_NAME, nClass);
            putP(fmB[bSSCls], Grimoire::OFF_UOBJECT_CLASS, A(bMeta));   put32(fmB[bSSCls], Grimoire::OFF_UOBJECT_NAME, nScriptStruct);
            putP(fmB[bNPCls], Grimoire::OFF_UOBJECT_CLASS, A(bMeta));   put32(fmB[bNPCls], Grimoire::OFF_UOBJECT_NAME, nNameProp);
            // The decoy: FBodyInstance's NameProperty, same name, shaped like an 8-aligned FName.
            putP(fmB[bDecoy], Grimoire::OFF_UOBJECT_CLASS, A(bNPCls));  put32(fmB[bDecoy], Grimoire::OFF_UOBJECT_NAME, nCpn);
            put32(fmB[bDecoy], DynOff::USTRUCT_PROPSSIZE, 8);           put32(fmB[bDecoy], DynOff::USTRUCT_PROPSSIZE + 4, 8);
            if (withStruct) {
                putP(fmB[bStruct], Grimoire::OFF_UOBJECT_CLASS, A(bSSCls)); put32(fmB[bStruct], Grimoire::OFF_UOBJECT_NAME, nCpn);
                put32(fmB[bStruct], DynOff::USTRUCT_PROPSSIZE, structSize);
                put32(fmB[bStruct], DynOff::USTRUCT_PROPSSIZE + 4, structAlign);
            }
            DynOff::bFNameAlignProbed = false;
            DynOff::FNAME_ALIGN_MEASURED = 0;
        };

        FakePool fmPool;
        fmPool.Build(kMB);
        for (int i = 0; i < kMB; ++i) {
            const uintptr_t o = A(i);
            memcpy(fmPool.chunks[0].data() + static_cast<size_t>(i) * FakePool::kItemSize, &o, sizeof(o));
        }
        Aura::InitWithExtendedLayout(fmPool.Addr(), FakePool::kItemSize);

        build(true, 8, 8);
        g_cachedUEVersion = 427;   // the rule would say 4
        const int m8 = Ubel::ResolveElementAlignment("NameProperty", 8, 0);
        check("FNAMEMEASURE ⭐: a measured 8 beats 4.27's rule of 4", m8 == 8, std::to_string(m8).c_str());

        build(true, 8, 4);
        g_cachedUEVersion = 418;   // the rule would say 8, and so would the decoy
        const int m4 = Ubel::ResolveElementAlignment("NameProperty", 8, 0);
        check("FNAMEMEASURE ⭐: a measured 4 beats 4.18's rule of 8, and the NameProperty decoy is skipped",
              m4 == 4, std::to_string(m4).c_str());

        build(true, 12, 8);        // not FName-sized: no measurement
        g_cachedUEVersion = 427;
        const int mSize = Ubel::ResolveElementAlignment("NameProperty", 8, 0);
        check("FNAMEMEASURE control: a struct that is not FName-sized measures nothing -> the rule (4)",
              mSize == 4, std::to_string(mSize).c_str());

        build(false, 0, 0);        // only the decoy
        g_cachedUEVersion = 418;
        const int mNone = Ubel::ResolveElementAlignment("NameProperty", 8, 0);
        check("FNAMEMEASURE control: no ScriptStruct -> 4.18's rule (8)", mNone == 8, std::to_string(mNone).c_str());

        Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);   // the main fixture, for any later block
        DynOff::bFNameAlignProbed   = false;
        DynOff::FNAME_ALIGN_MEASURED = 0;
        DynOff::bOffsetsValidated   = svValidM;
        DynOff::bCasePreservingName = svCpnM;
        g_cachedUEVersion           = svVerM;
    }

    // -- [VND583-04] UE 4.9-4.14 enums carry uint8 values: detected, measured, and read as one byte ----
    // A built "ENetRole" UEnum whose Names is TArray<TPair<FName, uint8>> with 0xCD padding (what an
    // unwritten pair tail looks like). DetectUEnumNames must find it, MEASURE the column's width on
    // ENetRole's 0..n-1 values, and ResolveEnumValue must then name every value. Own pool + name
    // pool, like UFIELDNEXT.
    {
        blk("ENUMU8 - UEnum::Names with uint8 values: the width is measured and the byte read alone");
        ResetCancel();
        enum : int32_t { eClass = 1, eEnum, eNetRole, eR0, eR1, eR2, eR3, eR4, eNames };
        const char* euNames[eNames] = { "", "Class", "Enum", "ENetRole", "ROLE_None", "ROLE_SimulatedProxy",
                                        "ROLE_AutonomousProxy", "ROLE_Authority", "ROLE_MAX" };
        static uint8_t euEntry[eNames][0x40] = {};
        static uintptr_t euChunk[eNames + 1] = {};
        for (int i = 1; i < eNames; ++i) {
            memcpy(euEntry[i] + 0x10, euNames[i], strlen(euNames[i]) + 1);
            euChunk[i] = reinterpret_cast<uintptr_t>(euEntry[i]);
        }
        static uintptr_t euChunks[2] = { reinterpret_cast<uintptr_t>(euChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(euChunks), 0x10);

        const uint32_t svVerE    = g_cachedUEVersion;
        const bool     svCpnE    = DynOff::bCasePreservingName;
        const int      svNamesE  = DynOff::UENUM_NAMES;
        const bool     svNewE    = DynOff::bEnumNamesNewContainer;
        const int      svWidthE  = DynOff::UENUM_VALUE_SIZE;
        const int      svStrideE = DynOff::UENUM_PAIR_STRIDE;
        const bool     svProbedE = DynOff::bFNameAlignProbed.load();
        const int      svAlignE  = DynOff::FNAME_ALIGN_MEASURED.load();
        DynOff::bCasePreservingName  = false;
        DynOff::bFNameAlignProbed    = true;    // nothing measured: alignof(FName) comes from the version rule
        DynOff::FNAME_ALIGN_MEASURED = 0;

        enum { bMeta, bEnumCls, bNetRole, kEB };
        alignas(16) static uint8_t euB[kEB][0x200] = {};
        alignas(16) static uint8_t euData[5 * 16] = {};
        auto A     = [&](int b) { return reinterpret_cast<uintptr_t>(euB[b]); };
        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        // width 1 = uint8 values + 0xCD padding; width 8 = int64 values. stride = pair size.
        auto build = [&](int width, int stride) {
            for (auto& b : euB) memset(b, 0, sizeof(b));
            memset(euData, 0xCD, sizeof(euData));
            putP(euB[bMeta], Grimoire::OFF_UOBJECT_CLASS, A(bMeta));        put32(euB[bMeta], Grimoire::OFF_UOBJECT_NAME, eClass);
            putP(euB[bEnumCls], Grimoire::OFF_UOBJECT_CLASS, A(bMeta));     put32(euB[bEnumCls], Grimoire::OFF_UOBJECT_NAME, eEnum);
            putP(euB[bNetRole], Grimoire::OFF_UOBJECT_CLASS, A(bEnumCls));  put32(euB[bNetRole], Grimoire::OFF_UOBJECT_NAME, eNetRole);
            for (int i = 0; i < 5; ++i) {
                uint8_t* e = euData + i * stride;
                put32(e, 0, eR0 + i);
                put32(e, 4, 0);
                if (width == 1) e[8] = static_cast<uint8_t>(i);
                else { const int64_t v = i; memcpy(e + 8, &v, sizeof(v)); }
            }
            putP(euB[bNetRole], 0x40, reinterpret_cast<uintptr_t>(euData));   // TArray: Data*, Num, Max
            put32(euB[bNetRole], 0x48, 5);
            put32(euB[bNetRole], 0x4C, 5);
            DynOff::bUEnumNamesDetected.store(false);
            DynOff::bUEnumNamesFailed.store(false);
            std::lock_guard<std::mutex> lk(Ubel::s_enumCacheMutex);
            Ubel::s_enumCache.erase(A(bNetRole));
        };

        FakePool euPool;
        euPool.Build(kEB);
        for (int i = 0; i < kEB; ++i) {
            const uintptr_t o = A(i);
            memcpy(euPool.chunks[0].data() + static_cast<size_t>(i) * FakePool::kItemSize, &o, sizeof(o));
        }
        Aura::InitWithExtendedLayout(euPool.Addr(), FakePool::kItemSize);

        // 4.11: uint8 values in a 16-byte pair (FName is 8-aligned there).
        build(1, 16);
        g_cachedUEVersion = 411;
        const bool det411 = Genau::DetectUEnumNames();
        check("ENUMU8 setup: ENetRole's Names is found at +0x40", det411 && DynOff::UENUM_NAMES == 0x40);
        check("ENUMU8 ⭐: 4.11 -> 1-byte values", DynOff::UENUM_VALUE_SIZE == 1,
              std::to_string(DynOff::UENUM_VALUE_SIZE).c_str());
        const std::string r2 = Ubel::ResolveEnumValue(A(bNetRole), 2);
        const std::string r4 = Ubel::ResolveEnumValue(A(bNetRole), 4);
        check("ENUMU8 ⭐: value 2 names ROLE_AutonomousProxy", r2 == "ROLE_AutonomousProxy", r2.c_str());
        check("ENUMU8 ⭐: value 4 names ROLE_MAX", r4 == "ROLE_MAX", r4.c_str());

        // The same memory on a version whose rule says int64: the MEASUREMENT still finds the byte.
        build(1, 16);
        g_cachedUEVersion = 427;
        Genau::DetectUEnumNames();
        const std::string m2 = Ubel::ResolveEnumValue(A(bNetRole), 2);
        check("ENUMU8 ⭐: garbage above sequential low bytes is measured as uint8 even where the rule says int64",
              DynOff::UENUM_VALUE_SIZE == 1 && m2 == "ROLE_AutonomousProxy", m2.c_str());

        // 4.10: FName is 4-aligned, so the uint8 pair strides 12.
        build(1, 12);
        g_cachedUEVersion = 410;
        Genau::DetectUEnumNames();
        const std::string t3 = Ubel::ResolveEnumValue(A(bNetRole), 3);
        check("ENUMU8: a 12-byte pair (4.10) is found, strided and named", DynOff::UENUM_PAIR_STRIDE == 12
              && t3 == "ROLE_Authority", (std::to_string(DynOff::UENUM_PAIR_STRIDE) + " " + t3).c_str());

        // Control: 4.18's int64 column keeps 8 bytes and still names its values.
        build(8, 16);
        g_cachedUEVersion = 418;
        Genau::DetectUEnumNames();
        const std::string c2 = Ubel::ResolveEnumValue(A(bNetRole), 2);
        check("ENUMU8 control: 4.18's int64 values stay 8 bytes and resolve",
              DynOff::UENUM_VALUE_SIZE == 8 && c2 == "ROLE_AutonomousProxy", c2.c_str());

        {
            std::lock_guard<std::mutex> lk(Ubel::s_enumCacheMutex);
            Ubel::s_enumCache.erase(A(bNetRole));
        }
        Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);   // the main fixture, for any later block
        DynOff::bUEnumNamesDetected.store(false);
        DynOff::bUEnumNamesFailed.store(false);
        DynOff::UENUM_NAMES            = svNamesE;
        DynOff::bEnumNamesNewContainer = svNewE;
        DynOff::UENUM_VALUE_SIZE       = svWidthE;
        DynOff::UENUM_PAIR_STRIDE      = svStrideE;
        DynOff::bFNameAlignProbed      = svProbedE;
        DynOff::FNAME_ALIGN_MEASURED   = svAlignE;
        DynOff::bCasePreservingName    = svCpnE;
        g_cachedUEVersion              = svVerE;
    }

    // -- [VND583-07, A9 step 11] FName::Number's offset is measured from NamePrivate on a case-preserving build ----
    // Built: 16 "Thing" objects with a 12-byte (CPN) NamePrivate and OuterPrivate at +0x28, in either member order.
    // UE4 / 5.0: {ComparisonIndex, DisplayIndex, Number}; 5.1+: {ComparisonIndex, Number, DisplayIndex}. Number is
    // 5 on every one, so the right read renders "Thing_4". Own pool and name pool, like FNAMEMEASURE.
    {
        blk("FNAMENUMBER - FName::Number is read where the build keeps it");
        enum : int32_t { nClass = 1, nThing, nNames };
        const char* fnNames[nNames] = { "", "Class", "Thing" };
        static uint8_t fnEntry[nNames][0x40] = {};
        static uintptr_t fnChunk[nNames + 1] = {};
        for (int i = 1; i < nNames; ++i) {
            memcpy(fnEntry[i] + 0x10, fnNames[i], strlen(fnNames[i]) + 1);
            fnChunk[i] = reinterpret_cast<uintptr_t>(fnEntry[i]);
        }
        static uintptr_t fnChunks[2] = { reinterpret_cast<uintptr_t>(fnChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(fnChunks), 0x10);
        const bool svCpnN   = DynOff::bCasePreservingName;
        const int  svOuterN = DynOff::UOBJECT_OUTER;
        const int  svNumN   = DynOff::FNAME_NUMBER;

        constexpr int kObjs = 17;   // [0] the metaclass, [1..16] Things
        auto runCase = [&](bool ue4Order, uint8_t (&objs)[kObjs][0x40]) -> std::string {
            for (auto& o : objs) memset(o, 0, sizeof(o));
            const uintptr_t meta = reinterpret_cast<uintptr_t>(objs[0]);
            memcpy(objs[0] + Grimoire::OFF_UOBJECT_CLASS, &meta, 8);
            const int32_t cls = nClass;
            memcpy(objs[0] + Grimoire::OFF_UOBJECT_NAME, &cls, 4);
            for (int i = 1; i < kObjs; ++i) {
                uint8_t* o = objs[i];
                memcpy(o + Grimoire::OFF_UOBJECT_CLASS, &meta, 8);
                const int32_t comp = nThing, number = 5;
                memcpy(o + 0x18, &comp, 4);
                memcpy(o + (ue4Order ? 0x1C : 0x20), &comp, 4);     // DisplayIndex
                memcpy(o + (ue4Order ? 0x20 : 0x1C), &number, 4);   // Number
                memcpy(o + 0x28, &meta, 8);                         // OuterPrivate (CPN slot)
            }
            FakePool fnPool;
            fnPool.Build(kObjs);
            for (int i = 0; i < kObjs; ++i) {
                const uintptr_t a = reinterpret_cast<uintptr_t>(objs[i]);
                memcpy(fnPool.chunks[0].data() + static_cast<size_t>(i) * FakePool::kItemSize, &a, 8);
            }
            Aura::InitWithExtendedLayout(fnPool.Addr(), FakePool::kItemSize);
            Genau::DetectCasePreservingName();
            const std::string name = Ubel::GetName(reinterpret_cast<uintptr_t>(objs[3]));
            Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);   // before fnPool goes away
            return name;
        };
        static uint8_t fnUe4[kObjs][0x40] = {};
        const std::string n4 = runCase(true, fnUe4);
        check("FNAMENUMBER setup: the UE4-order objects vote case-preserving", DynOff::bCasePreservingName);
        check("FNAMENUMBER ⭐ A9-11: UE4 / 5.0 order -> FName::Number measured at +8", DynOff::FNAME_NUMBER == 8,
              std::to_string(DynOff::FNAME_NUMBER).c_str());
        check("FNAMENUMBER ⭐ A9-11: ...and the name renders with ITS number, Thing_4", n4 == "Thing_4", n4.c_str());
        static uint8_t fnUe51[kObjs][0x40] = {};
        const std::string n51 = runCase(false, fnUe51);
        check("FNAMENUMBER control: 5.1+ order -> Number at +4 and Thing_4 too",
              DynOff::FNAME_NUMBER == 4 && n51 == "Thing_4", n51.c_str());

        DynOff::bCasePreservingName = svCpnN;
        DynOff::UOBJECT_OUTER       = svOuterN;
        DynOff::FNAME_NUMBER        = svNumN;
    }

    // -- [VND583-07, A9 steps 1 + 7] sizeof(FName) is MEASURED; the rule no longer overrides the engine -----------
    // Built: a UClass "Thing" whose ChildProperties chain holds `nName` NameProperty FFields of ElementSize `es`
    // between two IntProperty FFields. The walk reads DynOff's own offsets, so nothing here is hardcoded.
    {
        blk("FNAMESIZE - sizeof(FName) is the engine's NameProperty ElementSize");
        enum : int32_t { nClass = 1, nNameProp, nIntProp, nThing, nNames };
        const char* fsNames[nNames] = { "", "Class", "NameProperty", "IntProperty", "Thing" };
        static uint8_t fsEntry[nNames][0x40] = {};
        static uintptr_t fsChunk[nNames + 1] = {};
        for (int i = 1; i < nNames; ++i) {
            memcpy(fsEntry[i] + 0x10, fsNames[i], strlen(fsNames[i]) + 1);
            fsChunk[i] = reinterpret_cast<uintptr_t>(fsEntry[i]);
        }
        static uintptr_t fsChunks[2] = { reinterpret_cast<uintptr_t>(fsChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(fsChunks), 0x10);
        const bool svCpnS   = DynOff::bCasePreservingName;
        const bool svValidS = DynOff::bOffsetsValidated.load();
        const bool svFpropS = DynOff::bUseFProperty;
        DynOff::bUseFProperty     = true;
        DynOff::bOffsetsValidated = true;

        alignas(16) static uint8_t fsMeta[0x200] = {}, fsThing[0x200] = {};
        alignas(16) static uint8_t fsFcName[0x40] = {}, fsFcInt[0x40] = {};
        alignas(16) static uint8_t fsField[10][0x100] = {};
        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        FakePool fsPool;
        fsPool.Build(2);
        const uintptr_t objs[2] = { reinterpret_cast<uintptr_t>(fsMeta), reinterpret_cast<uintptr_t>(fsThing) };
        for (int i = 0; i < 2; ++i)
            memcpy(fsPool.chunks[0].data() + static_cast<size_t>(i) * FakePool::kItemSize, &objs[i], 8);
        auto build = [&](bool cpn, int nName, int32_t es) {
            memset(fsMeta, 0, sizeof(fsMeta)); memset(fsThing, 0, sizeof(fsThing));
            memset(fsFcName, 0, sizeof(fsFcName)); memset(fsFcInt, 0, sizeof(fsFcInt));
            for (auto& f : fsField) memset(f, 0, sizeof(f));
            putP(fsMeta, Grimoire::OFF_UOBJECT_CLASS, objs[0]);  put32(fsMeta, Grimoire::OFF_UOBJECT_NAME, nClass);
            putP(fsThing, Grimoire::OFF_UOBJECT_CLASS, objs[0]); put32(fsThing, Grimoire::OFF_UOBJECT_NAME, nThing);
            put32(fsFcName, DynOff::FFIELDCLASS_NAME, nNameProp);
            put32(fsFcInt,  DynOff::FFIELDCLASS_NAME, nIntProp);
            const int total = nName + 2;   // an IntProperty at each end
            for (int i = 0; i < total; ++i) {
                const bool isName = i > 0 && i < total - 1;
                putP(fsField[i], DynOff::FFIELD_CLASS, reinterpret_cast<uintptr_t>(isName ? fsFcName : fsFcInt));
                put32(fsField[i], DynOff::FPROPERTY_ELEMSIZE, isName ? es : 4);
                if (i + 1 < total) putP(fsField[i], DynOff::FFIELD_NEXT, reinterpret_cast<uintptr_t>(fsField[i + 1]));
            }
            putP(fsThing, DynOff::USTRUCT_CHILDPROPS, reinterpret_cast<uintptr_t>(fsField[0]));
            DynOff::bCasePreservingName = cpn;
            DynOff::bFNameSizeProbed    = false;
            DynOff::FNAME_SIZE_MEASURED = 0;
        };
        Aura::InitWithExtendedLayout(fsPool.Addr(), FakePool::kItemSize);

        build(false, 6, 4);   // a standard build with UE_FNAME_OUTLINE_NUMBER: FName is 4 bytes
        const int sOutline = Ubel::FNameSize();
        check("FNAMESIZE ⭐ A9-1: six NameProperty fields of 4 -> sizeof(FName) measured 4, not the rule's 8",
              sOutline == 4 && DynOff::SizeofFName() == 4, std::to_string(sOutline).c_str());
        build(true, 6, 12);
        const int sCpn = Ubel::FNameSize();
        check("FNAMESIZE ⭐ A9-1: case-preserving, 12 -> MEASURED 12 (it agrees with the rule, but is measured)",
              sCpn == 12 && DynOff::FNAME_SIZE_MEASURED.load() == 12, std::to_string(DynOff::FNAME_SIZE_MEASURED.load()).c_str());
        build(false, 6, 12);
        check("FNAMESIZE control: 12 on a standard build is not its family -> the rule's 8",
              Ubel::FNameSize() == 8 && DynOff::FNAME_SIZE_MEASURED.load() == 0);
        build(false, 3, 4);
        check("FNAMESIZE control: three fields are too few -> the rule's 8", Ubel::FNameSize() == 8);

        // A9 step 7. Unmeasured (the latch set, nothing found), validated: a plausible engine size is kept.
        build(false, 0, 0);
        DynOff::bFNameSizeProbed = true;
        const int32_t v4 = Ubel::ValidateArrayElemSize(4, "NameProperty");
        check("FNAMESIZE ⭐ A9-7: unmeasured, the engine's plausible 4 is kept (the rule overrode it with 8)",
              v4 == 4, std::to_string(v4).c_str());
        check("FNAMESIZE control: an implausible 0x30 is still overridden with the rule",
              Ubel::ValidateArrayElemSize(0x30, "NameProperty") == 8);
        DynOff::bOffsetsValidated = false;
        check("FNAMESIZE control: unvalidated offsets -> the rule, not the read",
              Ubel::ValidateArrayElemSize(4, "NameProperty") == 8);
        DynOff::bOffsetsValidated = true;
        DynOff::FNAME_SIZE_MEASURED = 8;
        check("FNAMESIZE control: MEASURED 8 beats a single read of 4",
              Ubel::ValidateArrayElemSize(4, "NameProperty") == 8);

        Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);   // the main fixture, for any later block
        DynOff::bFNameSizeProbed    = false;
        DynOff::FNAME_SIZE_MEASURED = 0;
        DynOff::bCasePreservingName = svCpnS;
        DynOff::bOffsetsValidated   = svValidS;
        DynOff::bUseFProperty       = svFpropS;
    }

    // -- [VND583-07] SymbolCallFollow looks ONE CALL deep for the global it follows ---------------------------------
    // The shape measured on UnrealEditor-Core.dll 5.4: the exported function has no RIP reference of its own; its
    // first call's target has `lea rdi,[rip+X]` = the pool. Built in one RWX allocation: funcA at +0, funcB at +0x400
    // (past the body scan's 256-byte fallback, so only the call-follow can reach it), the "pool" at +0x800.
    {
        blk("CALLFOLLOW - the export's callee is scanned when the export itself has no reference");
        uint8_t* code = static_cast<uint8_t*>(VirtualAlloc(nullptr, 0x1000, MEM_COMMIT | MEM_RESERVE,
                                                           PAGE_EXECUTE_READWRITE));
        check("CALLFOLLOW setup: an executable page", code != nullptr);
        if (code) {
            memset(code, 0xCC, 0x1000);
            static uintptr_t s_cfPool = 0;
            s_cfPool = reinterpret_cast<uintptr_t>(code + 0x800);
            // funcA: sub rsp,28h / mov ecx,[rcx+8] / call funcB / add rsp,28h / ret
            const uint8_t fa[] = { 0x48, 0x83, 0xEC, 0x28, 0x8B, 0x49, 0x08, 0xE8, 0, 0, 0, 0,
                                   0x48, 0x83, 0xC4, 0x28, 0xC3 };
            memcpy(code, fa, sizeof(fa));
            const int32_t relB = 0x400 - (7 + 5);
            memcpy(code + 8, &relB, 4);
            // funcB: lea rdi,[rip+X] / mov rax,rdi / ret
            const uint8_t fb[] = { 0x48, 0x8D, 0x3D, 0, 0, 0, 0, 0x48, 0x89, 0xF8, 0xC3 };
            memcpy(code + 0x400, fb, sizeof(fb));
            const int32_t relPool = 0x800 - (0x400 + 7);
            memcpy(code + 0x403, &relPool, 4);
            auto isPool = [](uintptr_t a) { return a == s_cfPool; };

            const uintptr_t viaBody = Genau::ScanFunctionBodyForRipRef(reinterpret_cast<uintptr_t>(code), "CF", isPool);
            check("CALLFOLLOW control: the export's own body holds no reference", viaBody == 0);
            const uintptr_t got = Genau::ScanFunctionAndCalleesForRipRef(reinterpret_cast<uintptr_t>(code), "CF", isPool);
            check("CALLFOLLOW ⭐ VND583-07: the pool is reached through funcA's call", got == s_cfPool);
            // A validator that accepts nothing still gets nothing: the follow adds reach, not answers.
            auto none = [](uintptr_t) { return false; };
            check("CALLFOLLOW control: the validator still decides",
                  Genau::ScanFunctionAndCalleesForRipRef(reinterpret_cast<uintptr_t>(code), "CF", none) == 0);
            VirtualFree(code, 0, MEM_RELEASE);
        }
    }

    // -- [VND583-14] FSoftObjectPath's shape is MEASURED on the reflected SoftObjectPath struct ---------------------
    // A built ScriptStruct "SoftObjectPath" whose first FField is AssetPathName (the 4.x / 5.0 shape), on a title
    // LABELLED 5.5 -- a fork that reports 505 over a 5.0 core. The path must read as ONE FName, not as
    // "Package.Asset" from two. Own pool and name pool, like FNAMEMEASURE.
    {
        blk("SOFTPATH - FSoftObjectPath's shape is read off the SoftObjectPath struct, not the version");
        enum : int32_t { nClass = 1, nScriptStruct, nSoftObjectPath, nAssetPathName, nAssetPath, nNameProp,
                         nPkg, nAsset, nNames };
        const char* spNames[nNames] = { "", "Class", "ScriptStruct", "SoftObjectPath", "AssetPathName", "AssetPath",
                                        "NameProperty", "/Game/Pkg", "Asset" };
        static uint8_t spEntry[nNames][0x40] = {};
        static uintptr_t spChunk[nNames + 1] = {};
        for (int i = 1; i < nNames; ++i) {
            memcpy(spEntry[i] + 0x10, spNames[i], strlen(spNames[i]) + 1);
            spChunk[i] = reinterpret_cast<uintptr_t>(spEntry[i]);
        }
        static uintptr_t spChunks[2] = { reinterpret_cast<uintptr_t>(spChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(spChunks), 0x10);

        const uint32_t svVerS   = g_cachedUEVersion;
        const bool     svFPropS = DynOff::bUseFProperty;
        const bool     svValidS = DynOff::bOffsetsValidated.load();
        DynOff::bUseFProperty     = true;
        DynOff::bOffsetsValidated = true;
        g_cachedUEVersion         = 505;

        enum { bMeta, bSSCls, bStruct, kSB };
        alignas(16) static uint8_t spB[kSB][0x200] = {};
        static uint8_t spFieldClass[0x20] = {};
        static uint8_t spField[0x100] = {};
        auto A = [&](int b) { return reinterpret_cast<uintptr_t>(spB[b]); };
        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto build = [&](int32_t fieldName) {
            for (auto& b : spB) memset(b, 0, sizeof(b));
            memset(spField, 0, sizeof(spField));
            putP(spB[bMeta], Grimoire::OFF_UOBJECT_CLASS, A(bMeta));   put32(spB[bMeta], Grimoire::OFF_UOBJECT_NAME, nClass);
            putP(spB[bSSCls], Grimoire::OFF_UOBJECT_CLASS, A(bMeta));  put32(spB[bSSCls], Grimoire::OFF_UOBJECT_NAME, nScriptStruct);
            putP(spB[bStruct], Grimoire::OFF_UOBJECT_CLASS, A(bSSCls)); put32(spB[bStruct], Grimoire::OFF_UOBJECT_NAME, nSoftObjectPath);
            putP(spB[bStruct], DynOff::USTRUCT_CHILDPROPS, reinterpret_cast<uintptr_t>(spField));
            put32(spFieldClass, DynOff::FFIELDCLASS_NAME, nNameProp);
            putP(spField, DynOff::FFIELD_CLASS, reinterpret_cast<uintptr_t>(spFieldClass));
            put32(spField, DynOff::FFIELD_NAME, fieldName);
            DynOff::bSoftPathProbed = false;
            DynOff::SOFTPATH_TOPLEVEL_MEASURED = -1;
        };
        FakePool spPool;
        spPool.Build(kSB);
        for (int i = 0; i < kSB; ++i) {
            const uintptr_t o = A(i);
            memcpy(spPool.chunks[0].data() + static_cast<size_t>(i) * FakePool::kItemSize, &o, sizeof(o));
        }
        Aura::InitWithExtendedLayout(spPool.Addr(), FakePool::kItemSize);

        // The path's bytes: FName {/Game/Pkg} then FName {Asset}. Read as one FName: "/Game/Pkg".
        // Read as a FTopLevelAssetPath: "/Game/Pkg.Asset".
        static int32_t spPath[8] = { nPkg, 0, nAsset, 0, 0, 0, 0, 0 };
        const uintptr_t pathAddr = reinterpret_cast<uintptr_t>(spPath);

        build(nAssetPathName);
        const std::string p50 = Ubel::ReadSoftObjectPath(pathAddr);
        check("SOFTPATH ⭐ VND583-14: AssetPathName measured on a title labelled 5.5 -> one FName",
              p50 == "/Game/Pkg", p50.c_str());
        check("SOFTPATH: ...and the measurement is latched as 0", DynOff::SOFTPATH_TOPLEVEL_MEASURED.load() == 0);

        build(nAssetPath);
        g_cachedUEVersion = 500;
        const std::string p51 = Ubel::ReadSoftObjectPath(pathAddr);
        check("SOFTPATH ⭐ VND583-14: AssetPath measured on a title labelled 5.0 -> Package.Asset",
              p51 == "/Game/Pkg.Asset", p51.c_str());

        Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);   // the main fixture, for any later block
        DynOff::bSoftPathProbed = false;
        DynOff::SOFTPATH_TOPLEVEL_MEASURED = -1;
        DynOff::bUseFProperty     = svFPropS;
        DynOff::bOffsetsValidated = svValidS;
        g_cachedUEVersion         = svVerS;
    }

    // -- [VND583-13] a compact TSet/TMap build is latched from a 16-byte Set/Map property, and walked header-only --
    // Built like IFACEREAD: its own name pool (the two type names and two field names), one FFieldClass per type,
    // one class blob per case. The sparse 0x50 control runs first, because the latch is process-wide.
    {
        blk("COMPACTSET - a 16-byte Set/Map on 5.7+ latches compact sets; the walk shows the count only");
        const char* csNames[] = { "", "SetProperty", "MapProperty", "Cs", "Cm" };
        static uint8_t csEntry[5][0x40] = {};
        static uintptr_t csChunk[6] = {};
        for (int i = 1; i < 5; ++i) {
            memcpy(csEntry[i] + 0x10, csNames[i], strlen(csNames[i]) + 1);
            csChunk[i] = reinterpret_cast<uintptr_t>(csEntry[i]);
        }
        static uintptr_t csChunks[2] = { reinterpret_cast<uintptr_t>(csChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(csChunks), 0x10);
        const bool svFPropC = DynOff::bUseFProperty;
        const uint32_t svVerC = g_cachedUEVersion;
        DynOff::bUseFProperty = true;
        g_cachedUEVersion = 508;
        DynOff::bCompactSets = false;

        static uint8_t csFieldClass[2][0x20] = {};
        *reinterpret_cast<int32_t*>(csFieldClass[0] + DynOff::FFIELDCLASS_NAME) = 1;   // SetProperty
        *reinterpret_cast<int32_t*>(csFieldClass[1] + DynOff::FFIELDCLASS_NAME) = 2;   // MapProperty
        static uint8_t csProp[4][0x100] = {};
        static uint8_t csCls[4][0x100] = {};
        auto makeClass = [&](int i, bool map, int32_t elemSize) {
            *reinterpret_cast<uintptr_t*>(csProp[i] + DynOff::FFIELD_CLASS) = reinterpret_cast<uintptr_t>(csFieldClass[map ? 1 : 0]);
            *reinterpret_cast<int32_t*>(csProp[i] + DynOff::FFIELD_NAME)           = map ? 4 : 3;
            *reinterpret_cast<int32_t*>(csProp[i] + DynOff::FPROPERTY_OFFSET)      = 0x100;
            *reinterpret_cast<int32_t*>(csProp[i] + DynOff::FPROPERTY_ELEMSIZE)    = elemSize;
            *reinterpret_cast<int32_t*>(csProp[i] + DynOff::FPROPERTY_ELEMSIZE - 4) = 1;
            *reinterpret_cast<int32_t*>(csCls[i] + DynOff::USTRUCT_PROPSSIZE)      = 0x200;
            *reinterpret_cast<uintptr_t*>(csCls[i] + DynOff::USTRUCT_CHILDPROPS)   = reinterpret_cast<uintptr_t>(csProp[i]);
            return reinterpret_cast<uintptr_t>(csCls[i]);
        };
        // The instance: a UObject header, then at +0x100 a compact set { Elements*, Num 3, Max 4 } followed by
        // non-zero bytes -- what a TSparseArray read would run into.
        alignas(16) static uint8_t csInst[0x200] = {};
        static uint8_t csElems[0x40] = {};
        const uintptr_t el = reinterpret_cast<uintptr_t>(csElems);
        memcpy(csInst + 0x100, &el, 8);
        *reinterpret_cast<int32_t*>(csInst + 0x108) = 3;
        *reinterpret_cast<int32_t*>(csInst + 0x10C) = 4;
        memset(csInst + 0x110, 0x7F, 0x40);
        const uintptr_t inst = reinterpret_cast<uintptr_t>(csInst);

        auto fieldOf = [&](const Ubel::InstanceWalkResult& r) {
            return r.fields.empty() ? Ubel::LiveFieldValue{} : r.fields[0];
        };
        const auto sparseF = fieldOf(Ubel::WalkInstance(inst, makeClass(0, false, 0x50), 64, 2, false));
        check("COMPACTSET control: a 0x50 SetProperty does not latch compact sets", !DynOff::bCompactSets.load()
              && sparseF.name == "Cs", sparseF.name.c_str());

        const auto setF = fieldOf(Ubel::WalkInstance(inst, makeClass(1, false, 0x10), 64, 2, false));
        check("COMPACTSET ⭐ VND583-13: a 16-byte SetProperty on 5.8 latches compact sets", DynOff::bCompactSets.load());
        check("COMPACTSET ⭐ VND583-13: ...and the set shows its count from the compact header, not decoded",
              setF.setCount == 3 && setF.typedValue.find("compact TSet") != std::string::npos, setF.typedValue.c_str());
        const auto mapF = fieldOf(Ubel::WalkInstance(inst, makeClass(2, true, 0x10), 64, 2, false));
        check("COMPACTSET ⭐ VND583-13: a compact TMap shows its count, and reads no pairs",
              mapF.mapCount == 3 && mapF.typedValue.find("compact TMap") != std::string::npos
              && mapF.containerElements.empty(), mapF.typedValue.c_str());

        DynOff::bCompactSets = false;
        DynOff::bUseFProperty = svFPropC;
        g_cachedUEVersion = svVerC;
    }

    // -- [VND583-10] the static-struct GObjects resolver scores both array geometries and both item shapes --
    // Heap-built static FUObjectArrays (LooksLikeDataPtr rejects this exe's own .data): the <=5.7 geometry
    // (Objects @+0x10, NumElements @+0x24) and 5.8's (ObjObjects first: Objects @+0x00, NumElements @+0x08);
    // items with the UObject* at +0x00 (classic) or +0x08 (5.7+); strides 20 / 24 / 40.
    {
        blk("STATICGOBJ - the static FUObjectArray scorer reads 5.8's array and 5.7+'s item");
        static uint8_t sgEntry[2][0x40] = {};
        memcpy(sgEntry[1] + 0x10, "CoreObject", sizeof("CoreObject"));
        static uintptr_t sgChunk[3] = { 0, reinterpret_cast<uintptr_t>(sgEntry[1]), 0 };
        static uintptr_t sgChunks[2] = { reinterpret_cast<uintptr_t>(sgChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(sgChunks), 0x10);

        constexpr int kItems = 128;
        std::vector<uint8_t> sgObjects(static_cast<size_t>(kItems) * 0x40, 0);
        for (int i = 0; i < kItems; ++i) {
            const int32_t nameIdx = 1;
            memcpy(sgObjects.data() + static_cast<size_t>(i) * 0x40 + Grimoire::OFF_UOBJECT_NAME, &nameIdx, 4);
        }
        struct Built { std::vector<uint8_t> chunk0; std::vector<uintptr_t> table; std::vector<uint8_t> arr; };
        auto build = [&](Built& b, bool ue58, int stride, int objOff) -> uintptr_t {
            b.chunk0.assign(static_cast<size_t>(kItems) * stride + 0x100, 0);
            for (int i = 0; i < kItems; ++i) {
                const uintptr_t o = reinterpret_cast<uintptr_t>(sgObjects.data() + static_cast<size_t>(i) * 0x40);
                memcpy(b.chunk0.data() + static_cast<size_t>(i) * stride + objOff, &o, sizeof(o));
            }
            b.table.assign(4, 0);
            b.table[0] = reinterpret_cast<uintptr_t>(b.chunk0.data());
            // The array sits 0x40 into its buffer, so a probe BELOW the real base reads this struct's own
            // bytes -- which is how the live 5.8 decoy below was met. Every count field is set, as a real
            // array's are: Max 2,162,688 (33 chunks of 64K), one chunk in use.
            b.arr.assign(0xC0, 0);
            uint8_t* a = b.arr.data() + 0x40;
            const uintptr_t tbl = reinterpret_cast<uintptr_t>(b.table.data());
            const int32_t num = kItems, maxE = 33 * 65536, numC = 1, maxC = 33;
            memcpy(a + (ue58 ? 0x00 : 0x10), &tbl, sizeof(tbl));
            memcpy(a + (ue58 ? 0x08 : 0x24), &num, 4);
            memcpy(a + (ue58 ? 0x0C : 0x20), &maxE, 4);
            memcpy(a + (ue58 ? 0x10 : 0x2C), &numC, 4);
            memcpy(a + (ue58 ? 0x14 : 0x28), &maxC, 4);
            return reinterpret_cast<uintptr_t>(a);
        };
        struct Case { const char* what; bool ue58; int stride; int objOff; bool star; };
        const Case cases[] = {
            { "<=5.7 array, classic item, stride 24",        false, 0x18, 0x00, false },
            { "Obsidian: <=5.7 array, classic 20-byte item", false, 0x14, 0x00, false },
            { "<=5.7 array, 5.7+ item (UObject* @+0x08), 24", false, 0x18, 0x08, true  },
            { "5.8 array (ObjObjects first), 5.7+ item, 24",  true,  0x18, 0x08, true  },
            { "<=5.7 array, 40-byte Test item, UObject* @+0x08", false, 0x28, 0x08, true },
        };
        for (const Case& k : cases) {
            Built b;
            const uintptr_t base = build(b, k.ue58, k.stride, k.objOff);
            int stride = 0, objOff = -1;
            bool ue58 = !k.ue58;
            const int score = Genau::ScoreGObjectsStaticBase(base, &stride, &objOff, &ue58);
            const std::string got = "score " + std::to_string(score) + " stride " + std::to_string(stride)
                                  + " objOff " + std::to_string(objOff) + (ue58 ? " ue58" : " <=5.7");
            check((std::string(k.star ? "STATICGOBJ ⭐ VND583-10: " : "STATICGOBJ control: ") + k.what
                   + " -> read with its own stride, object offset and geometry").c_str(),
                  score >= 32 && stride == k.stride && objOff == k.objOff && ue58 == k.ue58, got.c_str());
        }

        // ⭐ The decoy DumperTest58 (5.8 Shipping) handed the first cut of this fix, live: probed 0x10 BELOW
        // a real 5.8 array, the 5.0-5.7 geometry finds the real Objects pointer at +0x10, and reads the
        // 5.8 array's MaxChunks (33) at +0x24 as NumElements. 33 clean names cleared the early-exit bar
        // of 32, so the true base, 0x10 higher, was never scored, and the pool held 33 objects. A 5.8
        // struct read through the 5.0-5.7 geometry is self-inconsistent -- "MaxElements" (+0x20 = the
        // 5.8 NumChunks, 1) is below "NumElements" (33) -- and must score 0.
        {
            Built b;
            const uintptr_t base58 = build(b, true, 0x18, 0x08);
            int stride = 0, objOff = -1;
            bool ue58 = false;
            const int decoy = Genau::ScoreGObjectsStaticBase(base58 - 0x10, &stride, &objOff, &ue58);
            check("STATICGOBJ ⭐ VND583-10: 0x10 below a 5.8 array, the 5.0-5.7 geometry reads MaxChunks as "
                  "NumElements -- that self-inconsistent struct scores 0",
                  decoy == 0, ("score " + std::to_string(decoy) + " stride " + std::to_string(stride)).c_str());
        }
    }

    // -- [VND583-05] a top-level WeakObjectProperty says null / stale / unreadable ---------------
    // Its three siblings (struct member, array element, search preview) label a pointer that does
    // not resolve; the top-level reader published only the raw index+serial hex, which the Live
    // Walker then showed as the value. It also ignored both reads, so an UNREADABLE pointer
    // printed "0000000000000000" -- the same bytes as a genuinely null one. Built like IFACEREAD:
    // two reserved pages, one committed, with the last case's FWeakObjectPtr across the edge.
    {
        blk("WEAKLABEL - a top-level FWeakObjectPtr is labelled null / null (stale) / unreadable");
        static uint8_t wkTypeEntry[0x40] = {};
        static uint8_t wkNameEntry[0x40] = {};
        memcpy(wkTypeEntry + 0x10, "WeakObjectProperty", sizeof("WeakObjectProperty"));
        memcpy(wkNameEntry + 0x10, "Wk", sizeof("Wk"));
        static uint8_t wkOptEntry[0x40] = {};
        memcpy(wkOptEntry + 0x10, "OptionalProperty", sizeof("OptionalProperty"));
        // [R7-S1] a soft pointer type and an asset path (package, asset) for the soft-optional case
        static uint8_t wkSoftEntry[0x40] = {}, wkPkgEntry[0x40] = {}, wkAssetEntry[0x40] = {};
        memcpy(wkSoftEntry + 0x10, "SoftObjectProperty", sizeof("SoftObjectProperty"));
        memcpy(wkPkgEntry + 0x10, "/Game/T_Foo", sizeof("/Game/T_Foo"));
        memcpy(wkAssetEntry + 0x10, "T_Foo", sizeof("T_Foo"));
        static uintptr_t wkChunk[8] = {};
        wkChunk[1] = reinterpret_cast<uintptr_t>(wkTypeEntry);
        wkChunk[2] = reinterpret_cast<uintptr_t>(wkNameEntry);
        wkChunk[3] = reinterpret_cast<uintptr_t>(wkOptEntry);
        wkChunk[4] = reinterpret_cast<uintptr_t>(wkSoftEntry);
        wkChunk[5] = reinterpret_cast<uintptr_t>(wkPkgEntry);
        wkChunk[6] = reinterpret_cast<uintptr_t>(wkAssetEntry);
        static uintptr_t wkChunks[2] = { reinterpret_cast<uintptr_t>(wkChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(wkChunks), 0x10);
        const bool svFPropW = DynOff::bUseFProperty;
        DynOff::bUseFProperty = true;

        static uint8_t wkFieldClass[0x20] = {};
        *reinterpret_cast<int32_t*>(wkFieldClass + DynOff::FFIELDCLASS_NAME) = 1;
        uint8_t* wpage = static_cast<uint8_t*>(VirtualAlloc(nullptr, 0x2000, MEM_RESERVE, PAGE_READWRITE));
        check("WEAKLABEL setup: reserved two pages", wpage != nullptr);
        if (wpage) {
            VirtualAlloc(wpage, 0x1000, MEM_COMMIT, PAGE_READWRITE);
            const uintptr_t winst = reinterpret_cast<uintptr_t>(wpage);
            // One class blob per case: s_walkClassCache is keyed by the class address.
            static uint8_t wkProp[8][0x80] = {};
            static uint8_t wkCls[8][0x100] = {};
            auto makeClass = [&](int i, int32_t fieldOffset) {
                *reinterpret_cast<uintptr_t*>(wkProp[i] + DynOff::FFIELD_CLASS) = reinterpret_cast<uintptr_t>(wkFieldClass);
                *reinterpret_cast<int32_t*>(wkProp[i] + DynOff::FFIELD_NAME)           = 2;
                *reinterpret_cast<int32_t*>(wkProp[i] + DynOff::FPROPERTY_OFFSET)      = fieldOffset;
                *reinterpret_cast<int32_t*>(wkProp[i] + DynOff::FPROPERTY_ELEMSIZE)    = 8;
                *reinterpret_cast<int32_t*>(wkProp[i] + DynOff::FPROPERTY_ELEMSIZE - 4) = 1;
                *reinterpret_cast<int32_t*>(wkCls[i] + DynOff::USTRUCT_PROPSSIZE)      = 0x2000;
                *reinterpret_cast<uintptr_t*>(wkCls[i] + DynOff::USTRUCT_CHILDPROPS)   = reinterpret_cast<uintptr_t>(wkProp[i]);
                return reinterpret_cast<uintptr_t>(wkCls[i]);
            };
            // Anti-vacuity: every case must produce its one field, or an empty walk passes for free.
            auto weakField = [&](const char* who, const Ubel::InstanceWalkResult& r) -> Ubel::LiveFieldValue {
                check((std::string("WEAKLABEL control: ") + who + " -- the fake class produced exactly one field").c_str(),
                      r.fields.size() == 1, std::to_string(r.fields.size()).c_str());
                for (const auto& f : r.fields)
                    if (f.typeName == "WeakObjectProperty") return f;
                return Ubel::LiveFieldValue{};
            };

            // A null pointer {0, 0}.
            *reinterpret_cast<int32_t*>(wpage + 0x100) = 0;
            *reinterpret_cast<int32_t*>(wpage + 0x104) = 0;
            const auto nullW = weakField("null", Ubel::WalkInstance(winst, makeClass(0, 0x100), 64, 2, false));
            check("WEAKLABEL ⭐: {0, 0} says null", nullW.typedValue == "null", nullW.typedValue.c_str());
            check("WEAKLABEL: ...and keeps its readable hex", nullW.hexValue == "0000000000000000", nullW.hexValue.c_str());

            // A dead reference: a live index whose serial no longer matches.
            *reinterpret_cast<int32_t*>(wpage + 0x200) = 1;
            *reinterpret_cast<int32_t*>(wpage + 0x204) = 0x7777;
            const auto staleW = weakField("stale", Ubel::WalkInstance(winst, makeClass(1, 0x200), 64, 2, false));
            check("WEAKLABEL ⭐: {1, wrong serial} says null (stale)", staleW.typedValue == "null (stale)",
                  staleW.typedValue.c_str());

            // [R7-B-02] A SET TOptional<TWeakObjectPtr> (12 bytes: the pointer, then bIsSet at +8) whose pointer does
            // not resolve takes the same null / null (stale) rule as every other weak reader -- it said "(stale)"
            // for both, including a pointer explicitly set to null.
            static uint8_t wkOptFC[0x20] = {};
            *reinterpret_cast<int32_t*>(wkOptFC + DynOff::FFIELDCLASS_NAME) = 3;
            static uint8_t wkOptInner[0x80] = {};
            *reinterpret_cast<uintptr_t*>(wkOptInner + DynOff::FFIELD_CLASS) = reinterpret_cast<uintptr_t>(wkFieldClass);
            *reinterpret_cast<int32_t*>(wkOptInner + DynOff::FFIELD_NAME)         = 2;
            *reinterpret_cast<int32_t*>(wkOptInner + DynOff::FPROPERTY_ELEMSIZE)  = 8;
            *reinterpret_cast<int32_t*>(wkOptInner + DynOff::FPROPERTY_ELEMSIZE - 4) = 1;
            static uint8_t wkOptProp[6][0x80] = {};
            static uint8_t wkOptCls[6][0x100] = {};
            auto makeOptClass = [&](int i, int32_t fieldOffset) {
                *reinterpret_cast<uintptr_t*>(wkOptProp[i] + DynOff::FFIELD_CLASS) = reinterpret_cast<uintptr_t>(wkOptFC);
                *reinterpret_cast<int32_t*>(wkOptProp[i] + DynOff::FFIELD_NAME)           = 2;
                *reinterpret_cast<int32_t*>(wkOptProp[i] + DynOff::FPROPERTY_OFFSET)      = fieldOffset;
                *reinterpret_cast<int32_t*>(wkOptProp[i] + DynOff::FPROPERTY_ELEMSIZE)    = 12;
                *reinterpret_cast<int32_t*>(wkOptProp[i] + DynOff::FPROPERTY_ELEMSIZE - 4) = 1;
                *reinterpret_cast<uintptr_t*>(wkOptProp[i] + DynOff::FARRAYPROP_INNER) = reinterpret_cast<uintptr_t>(wkOptInner);
                *reinterpret_cast<int32_t*>(wkOptCls[i] + DynOff::USTRUCT_PROPSSIZE)      = 0x2000;
                *reinterpret_cast<uintptr_t*>(wkOptCls[i] + DynOff::USTRUCT_CHILDPROPS)   = reinterpret_cast<uintptr_t>(wkOptProp[i]);
                return reinterpret_cast<uintptr_t>(wkOptCls[i]);
            };
            auto optField = [&](const char* who, const Ubel::InstanceWalkResult& r) -> Ubel::LiveFieldValue {
                check((std::string("WEAKLABEL control: ") + who + " -- the fake class produced exactly one field").c_str(),
                      r.fields.size() == 1, std::to_string(r.fields.size()).c_str());
                for (const auto& f : r.fields)
                    if (f.typeName == "OptionalProperty") return f;
                return Ubel::LiveFieldValue{};
            };
            *reinterpret_cast<int32_t*>(wpage + 0x600) = 0;
            *reinterpret_cast<int32_t*>(wpage + 0x604) = 0;
            wpage[0x608] = 1;                                          // bIsSet
            const auto optNull = optField("optional weak set-null", Ubel::WalkInstance(winst, makeOptClass(0, 0x600), 64, 2, false));
            check("WEAKLABEL ⭐ R7-B-02: a SET TOptional<weak> holding {0, 0} says null, not (stale)",
                  optNull.typedValue == "null", optNull.typedValue.c_str());
            *reinterpret_cast<int32_t*>(wpage + 0x620) = 1;
            *reinterpret_cast<int32_t*>(wpage + 0x624) = 0x7777;
            wpage[0x628] = 1;
            const auto optStale = optField("optional weak stale", Ubel::WalkInstance(winst, makeOptClass(1, 0x620), 64, 2, false));
            check("WEAKLABEL ⭐ R7-B-02: a SET TOptional<weak> with a dead serial says null (stale)",
                  optStale.typedValue == "null (stale)", optStale.typedValue.c_str());
            wpage[0x648] = 0;                                          // bIsSet clear
            const auto optUnset = optField("optional weak unset", Ubel::WalkInstance(winst, makeOptClass(2, 0x640), 64, 2, false));
            check("WEAKLABEL control R7-B-02: an unset TOptional<weak> is still (unset)",
                  optUnset.typedValue == "(unset)", optUnset.typedValue.c_str());

            // [R7-S1] A SET TOptional<TSoftObjectPtr> whose asset is not loaded: its embedded weak pair is {0, 0}
            // (TPersistentObjectPtr fills it only on Get()), and its value is the PATH -- not "null", which says
            // the optional holds nothing. 0x28-byte soft pointer (5.3+), so the optional is 0x30 with bIsSet at +0x28.
            {
                const uint32_t svVerS = g_cachedUEVersion;
                const int svSoftLatch = DynOff::SOFTPTR_PATH;   // [R7-S5] SoftPathOffset latches its measurement
                g_cachedUEVersion = 504;
                static uint8_t wkSoftFC[0x20] = {};
                *reinterpret_cast<int32_t*>(wkSoftFC + DynOff::FFIELDCLASS_NAME) = 4;
                static uint8_t wkSoftInner[0x80] = {};
                *reinterpret_cast<uintptr_t*>(wkSoftInner + DynOff::FFIELD_CLASS) = reinterpret_cast<uintptr_t>(wkSoftFC);
                *reinterpret_cast<int32_t*>(wkSoftInner + DynOff::FFIELD_NAME)         = 2;
                *reinterpret_cast<int32_t*>(wkSoftInner + DynOff::FPROPERTY_ELEMSIZE)  = 0x28;
                *reinterpret_cast<int32_t*>(wkSoftInner + DynOff::FPROPERTY_ELEMSIZE - 4) = 1;
                static uint8_t wkSoftOptProp[0x80] = {};
                static uint8_t wkSoftOptCls[0x100] = {};
                *reinterpret_cast<uintptr_t*>(wkSoftOptProp + DynOff::FFIELD_CLASS) = reinterpret_cast<uintptr_t>(wkOptFC);
                *reinterpret_cast<int32_t*>(wkSoftOptProp + DynOff::FFIELD_NAME)           = 2;
                *reinterpret_cast<int32_t*>(wkSoftOptProp + DynOff::FPROPERTY_OFFSET)      = 0x680;
                *reinterpret_cast<int32_t*>(wkSoftOptProp + DynOff::FPROPERTY_ELEMSIZE)    = 0x30;
                *reinterpret_cast<int32_t*>(wkSoftOptProp + DynOff::FPROPERTY_ELEMSIZE - 4) = 1;
                *reinterpret_cast<uintptr_t*>(wkSoftOptProp + DynOff::FARRAYPROP_INNER) = reinterpret_cast<uintptr_t>(wkSoftInner);
                *reinterpret_cast<int32_t*>(wkSoftOptCls + DynOff::USTRUCT_PROPSSIZE)      = 0x2000;
                *reinterpret_cast<uintptr_t*>(wkSoftOptCls + DynOff::USTRUCT_CHILDPROPS)   = reinterpret_cast<uintptr_t>(wkSoftOptProp);
                memset(wpage + 0x680, 0, 0x30);
                *reinterpret_cast<int32_t*>(wpage + 0x688) = 5;       // FSoftObjectPath: PackageName "/Game/T_Foo"
                *reinterpret_cast<int32_t*>(wpage + 0x690) = 6;       //                  AssetName   "T_Foo"
                wpage[0x680 + 0x28] = 1;                              // bIsSet
                const auto optSoft = optField("optional soft set, not loaded",
                    Ubel::WalkInstance(winst, reinterpret_cast<uintptr_t>(wkSoftOptCls), 64, 2, false));
                check("WEAKLABEL ⭐ R7-S1: a SET TOptional<soft> not loaded shows its asset path, not null",
                      optSoft.typedValue == "/Game/T_Foo.T_Foo", optSoft.typedValue.c_str());
                g_cachedUEVersion = svVerS;
                DynOff::SOFTPTR_PATH = svSoftLatch;
            }

            // Across the page edge: ObjectIndex reads, SerialNumber faults.
            *reinterpret_cast<int32_t*>(wpage + 0xFFC) = 0;
            const auto deadW = weakField("half-unread", Ubel::WalkInstance(winst, makeClass(2, 0x0FFC), 64, 2, false));
            check("WEAKLABEL ⭐: a half-readable FWeakObjectPtr publishes NO hex", deadW.hexValue.empty(),
                  deadW.hexValue.c_str());
            check("WEAKLABEL ⭐: ...and says unreadable at its own offset, not null",
                  deadW.typedValue.find("unreadable at +0xFFC") != std::string::npos, deadW.typedValue.c_str());

            // Control: a pointer that resolves carries its object and no label.
            const uintptr_t o1 = Aura::GetByIndex(1);
            if (o1) {
                // [VND583-08] A real weak pointer carries a NON-zero serial (UE assigns one the first
                // time a weak pointer is made), and serial 0 means null. The fake pool's items all read
                // 0, so item 1 gets one for these cases and puts it back at the end.
                uint8_t* item1s = pool.chunks[0].data() + static_cast<size_t>(1) * FakePool::kItemSize;
                int32_t svSerial1 = 0;
                memcpy(&svSerial1, item1s + 0x10, 4);
                const int32_t serial1 = 0x55;
                memcpy(item1s + 0x10, &serial1, 4);
                check("WEAKLABEL setup: item 1 now carries serial 0x55", Aura::GetSerialNumber(1) == 0x55);
                *reinterpret_cast<int32_t*>(wpage + 0x300) = 1;
                *reinterpret_cast<int32_t*>(wpage + 0x304) = Aura::GetSerialNumber(1);
                const auto liveW = weakField("live", Ubel::WalkInstance(winst, makeClass(3, 0x300), 64, 2, false));
                check("WEAKLABEL control: a resolving pointer publishes the object and no label",
                      liveW.ptrValue == o1 && liveW.typedValue.empty(), liveW.typedValue.c_str());

                // [VND583-06] ...unless UE's Get() would refuse it: still resolved, but LABELLED.
                // UE5 marks Garbage in UObject::ObjectFlags (RF_MirroredGarbage); UE4 marks
                // PendingKill only in the FUObjectItem.
                uint8_t* item1 = pool.chunks[0].data() + static_cast<size_t>(1) * FakePool::kItemSize;
                const uint32_t svVerG = g_cachedUEVersion;
                uint32_t objFlags = 0, itemFlags = 0;
                memcpy(&objFlags, reinterpret_cast<void*>(o1 + Grimoire::OFF_UOBJECT_FLAGS), 4);
                memcpy(&itemFlags, item1 + 8, 4);
                auto setFlags = [&](uint32_t objF, uint32_t itemF) {
                    memcpy(reinterpret_cast<void*>(o1 + Grimoire::OFF_UOBJECT_FLAGS), &objF, 4);
                    memcpy(item1 + 8, &itemF, 4);
                };

                g_cachedUEVersion = 504;
                setFlags(0x40000000u, 0);
                const auto g5 = weakField("garbage-ue5", Ubel::WalkInstance(winst, makeClass(4, 0x300), 64, 2, false));
                check("WEAKLABEL ⭐ VND583-06: UE5 RF_MirroredGarbage -> still resolved, labelled [garbage]",
                      g5.ptrValue == o1 && g5.typedValue.find("[garbage]") != std::string::npos, g5.typedValue.c_str());

                g_cachedUEVersion = 427;
                setFlags(0, 1u << 29);
                const auto g4 = weakField("pendingkill-ue4", Ubel::WalkInstance(winst, makeClass(5, 0x300), 64, 2, false));
                check("WEAKLABEL ⭐ VND583-06: UE4 PendingKill in the item -> labelled [garbage]",
                      g4.ptrValue == o1 && g4.typedValue.find("[garbage]") != std::string::npos, g4.typedValue.c_str());

                g_cachedUEVersion = 504;
                const auto c5 = weakField("ue5-bit29", Ubel::WalkInstance(winst, makeClass(6, 0x300), 64, 2, false));
                check("WEAKLABEL control VND583-06: the same item bit on UE5 is not PendingKill -> no label",
                      c5.ptrValue == o1 && c5.typedValue.empty(), c5.typedValue.c_str());

                // [R7-B-04] The three weak display sites that never showed the tag, on a UE5 Garbage target. The
                // target gets a name ("Wk", pool index 2) so the readers that print a NAME print one.
                int32_t svName1 = 0;
                memcpy(&svName1, reinterpret_cast<void*>(o1 + Grimoire::OFF_UOBJECT_NAME), 4);
                const int32_t wkNameIdx = 2;
                memcpy(reinterpret_cast<void*>(o1 + Grimoire::OFF_UOBJECT_NAME), &wkNameIdx, 4);
                uint32_t svObjF = 0, svItemF = 0;   // the UE4 PendingKill state the array case below relies on
                memcpy(&svObjF, reinterpret_cast<void*>(o1 + Grimoire::OFF_UOBJECT_FLAGS), 4);
                memcpy(&svItemF, item1 + 8, 4);
                setFlags(0x40000000u, 0);
                // (1) A SET TOptional<TWeakObjectPtr>: the tag it added was overwritten by the display builder.
                *reinterpret_cast<int32_t*>(wpage + 0x660) = 1;
                *reinterpret_cast<int32_t*>(wpage + 0x664) = Aura::GetSerialNumber(1);
                wpage[0x668] = 1;
                const auto optG = optField("optional weak garbage", Ubel::WalkInstance(winst, makeOptClass(3, 0x660), 64, 2, false));
                check("WEAKLABEL ⭐ R7-B-04: a SET TOptional<weak> to a Garbage target shows [garbage]",
                      optG.ptrValue == o1 && optG.typedValue.find("[garbage]") != std::string::npos, optG.typedValue.c_str());
                // (2) The delegate-binding label every reader uses (the sparse-binding loop did not add the tag).
                const std::string dbG = Ubel::DescribeDelegateBinding(o1, "Obj", 1, Aura::GetSerialNumber(1), "OnHit");
                check("WEAKLABEL ⭐ R7-B-04: a delegate binding to a Garbage target shows [garbage]",
                      dbG == "Obj::OnHit [garbage]", dbG.c_str());
                // (3) The Property Search soft preview's fallback: no asset path, a weak pointer that resolves.
                std::vector<Aura::PropertyMatch> spRows(1);
                spRows[0].classAddr  = 0xE100;
                spRows[0].propType   = "SoftObjectProperty";
                spRows[0].propOffset = 0x700;
                spRows[0].propSize   = 0x28;
                *reinterpret_cast<int32_t*>(wpage + 0x700) = 1;
                *reinterpret_cast<int32_t*>(wpage + 0x704) = Aura::GetSerialNumber(1);
                std::unordered_map<uintptr_t, uintptr_t> spMap{ { 0xE100, winst } };
                Ubel::ResolvePropertyPreviews(spRows, spMap);
                check("WEAKLABEL ⭐ R7-B-04: the soft preview's resolved fallback shows [garbage]",
                      spRows[0].preview.find("[garbage]") != std::string::npos, spRows[0].preview.c_str());
                setFlags(0, 0);
                const std::string dbLive = Ubel::DescribeDelegateBinding(o1, "Obj", 1, Aura::GetSerialNumber(1), "OnHit");
                check("WEAKLABEL control R7-B-04: a live target's binding has no tag", dbLive == "Obj::OnHit", dbLive.c_str());
                memcpy(reinterpret_cast<void*>(o1 + Grimoire::OFF_UOBJECT_NAME), &svName1, 4);
                setFlags(svObjF, svItemF);

                // The array reader, through the same tag: [{1, serial}, {0, 0}] on UE4 with PendingKill.
                g_cachedUEVersion = 427;
                *reinterpret_cast<uintptr_t*>(wpage + 0x400) = winst + 0x500;   // TArray Data*
                *reinterpret_cast<int32_t*>(wpage + 0x408) = 2;                 // Num
                *reinterpret_cast<int32_t*>(wpage + 0x40C) = 2;                 // Max
                *reinterpret_cast<int32_t*>(wpage + 0x500) = 1;
                *reinterpret_cast<int32_t*>(wpage + 0x504) = Aura::GetSerialNumber(1);
                *reinterpret_cast<int32_t*>(wpage + 0x508) = 0;
                *reinterpret_cast<int32_t*>(wpage + 0x50C) = 0;
                const auto arr = Ubel::ReadWeakObjectArrayElements(winst, 0x400, 8, 0, 8);
                check("WEAKLABEL VND583-06: the array reader read both elements", arr.elements.size() == 2,
                      std::to_string(arr.elements.size()).c_str());
                if (arr.elements.size() == 2) {
                    check("WEAKLABEL ⭐ VND583-06: a PendingKill array element is labelled [garbage]",
                          arr.elements[0].value.find("[garbage]") != std::string::npos, arr.elements[0].value.c_str());
                    check("WEAKLABEL control VND583-06: the null element beside it still says null",
                          arr.elements[1].value == "null", arr.elements[1].value.c_str());
                }

                setFlags(objFlags, itemFlags);
                g_cachedUEVersion = svVerG;

                // [VND583-08] serial 0 is UE's explicit null: {1, 0} must not resolve to object 1 --
                // it did, whenever object 1's own serial had never been assigned (most read 0).
                memcpy(item1s + 0x10, &svSerial1, 4);   // item 1 back to serial 0, as unassigned
                check("WEAKLABEL ⭐ VND583-08: {1, 0} does NOT resolve, even to an object whose serial is 0",
                      Ubel::ResolveWeakObjectPtr(1, 0) == 0);
                *reinterpret_cast<int32_t*>(wpage + 0x380) = 1;
                *reinterpret_cast<int32_t*>(wpage + 0x384) = 0;
                const auto zeroW = weakField("{1,0}", Ubel::WalkInstance(winst, makeClass(7, 0x380), 64, 2, false));
                check("WEAKLABEL ⭐ VND583-08: {1, 0} walks as null, not stale and not object 1",
                      zeroW.ptrValue == 0 && zeroW.typedValue == "null", zeroW.typedValue.c_str());
                // ...and index 0 is a real slot: {0, S} resolves when object 0's serial is S.
                uint8_t* item0s = pool.chunks[0].data();
                int32_t svSerial0 = 0;
                memcpy(&svSerial0, item0s + 0x10, 4);
                const int32_t serial0 = 0x66;
                memcpy(item0s + 0x10, &serial0, 4);
                const uintptr_t o0 = Aura::GetByIndex(0);
                check("WEAKLABEL ⭐ VND583-08: {0, S} resolves to object 0, as UE's does",
                      o0 != 0 && Ubel::ResolveWeakObjectPtr(0, 0x66) == o0);
                memcpy(item0s + 0x10, &svSerial0, 4);
            }
            VirtualFree(wpage, 0, MEM_RELEASE);
        }
        DynOff::bUseFProperty = svFPropW;
    }

    {   blk("SCAN-EARLY — AOBScanAll on a module that was unloaded after the module list was taken");
        // [SCAN-EARLY-TRIGGER-CONTAINED] Measured on build 3555: a trigger_scan ~1 s after launch died with an
        // uncaught non-standard exception in FindGObjects' multi-module fallback. AOBScanAllModules snapshots
        // EnumProcessModules and then reads each module's headers and code with no guard; a DLL the booting engine
        // frees in between is read after it is unmapped. This case frees a real module, then scans its old base.
        const wchar_t* candidates[] = { L"wtsapi32.dll", L"srvcli.dll", L"dsreg.dll", L"wevtapi.dll", L"mscms.dll" };
        // (eleventh review, R11-04) The DLL is QUALIFIED with no scan at all: loaded and freed, it must unmap on its
        // own. Only that is the environment's business; everything after it is the scan's. Before, a scan that leaked
        // its reference made every candidate fail the "unmapped" filter here and read as "no usable DLL".
        // (twelfth review, R12-03) Whether the DLL HAS a code section to cut a pattern from is the environment's too,
        // so it is part of the qualification, as it was before R11-04 split this.
        auto firstCode = [](uintptr_t b) -> const uint8_t* {
            auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(b);
            auto* nt  = reinterpret_cast<IMAGE_NT_HEADERS64*>(b + dos->e_lfanew);
            IMAGE_SECTION_HEADER* sec = IMAGE_FIRST_SECTION(nt);
            for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++sec)
                if ((sec->Characteristics & IMAGE_SCN_MEM_EXECUTE) && sec->Misc.VirtualSize >= 64)
                    return reinterpret_cast<const uint8_t*>(b + sec->VirtualAddress);
            return nullptr;
        };
        const wchar_t* used = nullptr;
        for (const wchar_t* name : candidates) {
            if (GetModuleHandleW(name)) continue;                       // must not be loaded by anyone else
            HMODULE q = LoadLibraryExW(name, nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
            if (!q) continue;
            const uintptr_t qb = reinterpret_cast<uintptr_t>(q);
            const bool hasCode = firstCode(qb) != nullptr;
            FreeLibrary(q);
            if (!hasCode) continue;
            MEMORY_BASIC_INFORMATION qm{};
            if (GetModuleHandleW(name)
                || VirtualQuery(reinterpret_cast<void*>(qb), &qm, sizeof(qm)) != sizeof(qm) || qm.State != MEM_FREE)
                continue;                                               // something keeps it mapped: try the next
            used = name;
            break;
        }
        check("SCAN-EARLY precondition: a System32 DLL with a code section that unmaps when freed (qualified with no scan)",
              used != nullptr);
        uintptr_t base = 0;
        char pattern[16 * 3 + 1] = {};
        HMODULE h = used ? LoadLibraryExW(used, nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32) : nullptr;
        const uint8_t* code = nullptr;
        if (h) {
            base = reinterpret_cast<uintptr_t>(h);
            code = firstCode(base);
            if (!code) { FreeLibrary(h); h = nullptr; }   // (R12-03) never leave the test's reference behind
        }
        if (used)
            check("SCAN-EARLY precondition: it loads again and has a code section to cut a pattern from", h && code);
        if (h && code) {
            // Positive control: 16 bytes from the start of its first code section, as a pattern, are found while
            // it is loaded -- so a later "0 matches" means "gone", not "this scan never finds anything here".
            for (int i = 0; i < 16; ++i) snprintf(pattern + i * 3, 4, i == 15 ? "%02X" : "%02X ", code[i]);
            const size_t liveHits = Macht::AOBScanAll(pattern, base).size();
            // (tenth review, R10-02) The scan released ONLY its own reference: the module is still ours to free.
            const bool stillLoadedAfterScan = GetModuleHandleW(used) == h;
            const bool ownFreeOk = FreeLibrary(h) != FALSE;
            // (R11-04) ...and released it at ALL: after the owner's free nothing else holds it, so it unmaps.
            MEMORY_BASIC_INFORMATION mbi{};
            const bool unmappedAfter = !GetModuleHandleW(used)
                && VirtualQuery(reinterpret_cast<void*>(base), &mbi, sizeof(mbi)) == sizeof(mbi)
                && mbi.State == MEM_FREE;
            check("SCAN-EARLY control: while loaded, a pattern cut from its own code is found in it", liveHits >= 1,
                  std::to_string(liveHits).c_str());
            check("SCAN-EARLY R10-02: after a scan the module is still loaded -- the pin released only its own reference",
                  stillLoadedAfterScan);
            check("SCAN-EARLY R10-02: ...and the owner's own FreeLibrary still succeeds", ownFreeOk);
            check("SCAN-EARLY R11-04: ...and then it unmaps -- the scan released its reference (no leak)", unmappedAfter);
            size_t n = 999;
            const bool faulted = ScanFaultsAt(pattern, base, &n);
            check("SCAN-EARLY ⭐ scanning a module that is gone does NOT fault", !faulted);
            check("SCAN-EARLY ⭐ ...and finds nothing there", !faulted && n == 0, std::to_string(n).c_str());

            // (tenth review, R10-02) The race the fix is for, forced: the owner frees the module DURING the scan (the
            // seam runs between the pin and the reads). The pin must keep the image mapped -- the scan neither faults
            // nor misses -- and, released on return, must be the LAST reference, so the image is gone afterwards.
            HMODULE h2 = LoadLibraryExW(used, nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
            check("SCAN-EARLY R10-02 precondition: the DLL loads again", h2 != nullptr);
            if (h2) {
                const uintptr_t b2 = reinterpret_cast<uintptr_t>(h2);
                Macht::g_afterModulePinForTest = [](uintptr_t mod) { FreeLibrary(reinterpret_cast<HMODULE>(mod)); };
                size_t n2 = 999;
                const bool faulted2 = ScanFaultsAt(pattern, b2, &n2);
                Macht::g_afterModulePinForTest = nullptr;
                MEMORY_BASIC_INFORMATION mbi2{};
                const bool gone = !GetModuleHandleW(used)
                    && VirtualQuery(reinterpret_cast<void*>(b2), &mbi2, sizeof(mbi2)) == sizeof(mbi2)
                    && mbi2.State == MEM_FREE;
                check("SCAN-EARLY R10-02 ⭐ freed by its owner mid-scan, the pinned image does NOT fault", !faulted2);
                check("SCAN-EARLY R10-02 ⭐ ...and is still scanned whole (the pattern is found)", !faulted2 && n2 >= 1,
                      std::to_string(n2).c_str());
                check("SCAN-EARLY R10-02 ⭐ ...and the pin, released on return, was the last reference: unmapped", gone);
            }
        }
    }

    // -- UPROPSLOT-2026-09-27 -- a UProperty engine gets the property subclass family at ITS start -------------------
    //
    // ⛔ POOL-FAKING (own GObjects, own name table; the LAST block, and it restores every DynOff Genau writes).
    // [UPROP-SUBCLASS-SLOT] Genau's UProperty arm derived the bool slot alone, so FSTRUCTPROP_STRUCT and the rest of the
    // family kept the FProperty default 0x78 -- past the end of a 4.23 UStructProperty / UObjectProperty and ON a
    // UClassProperty's MetaClass. Measured on UE423_Flying, SDK export straight after connect: 2,624 of 2,624 struct
    // members raw bytes, 1,038 of 1,038 object pointers UObject*. Only a later Live Walker struct probe corrected it.
    {
        blk("UPROPSLOT - Genau publishes the UProperty subclass family at the version's start, and a walk reads it");

        const auto svCpn = DynOff::bCasePreservingName;   const auto svOuter = DynOff::UOBJECT_OUTER;
        const auto svFProp = DynOff::bUseFProperty;       const auto svNext = DynOff::UFIELD_NEXT;
        const auto svChildren = DynOff::USTRUCT_CHILDREN; const auto svSuper = DynOff::USTRUCT_SUPER;
        const auto svPropsSize = DynOff::USTRUCT_PROPSSIZE; const auto svScript = DynOff::USTRUCT_SCRIPT;
        const auto svUOff = DynOff::UPROPERTY_OFFSET;     const auto svUElem = DynOff::UPROPERTY_ELEMSIZE;
        const auto svUFlags = DynOff::UPROPERTY_FLAGS;    const auto svUBool = DynOff::UBOOLPROP_FIELDSIZE;
        const auto svUStart = DynOff::UPROPERTY_SUBCLASS_START;
        const auto svTagged = DynOff::bTaggedFFieldVariant; const auto svFNum = DynOff::FNAME_NUMBER;
        const DynOff::PropertyFamily svFamily{ DynOff::FSTRUCTPROP_STRUCT, DynOff::FARRAYPROP_INNER,
                                               DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBYTEPROP_ENUM, DynOff::FENUMPROP_ENUM };
        const uint32_t svVer = g_cachedUEVersion;

        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto A     = [](uint8_t* b) { return reinterpret_cast<uintptr_t>(b); };

        static uint8_t upEntry[15][0x40] = {};
        const char* upNames[15] = { "", "ScriptStruct", "Guid", "IntProperty", "A", "B", "C", "D",
                                    "StructProperty", "Where", "Decoy", "ClassProperty", "Class", "Pawn", "Kind" };
        static uintptr_t upChunk[16] = {};
        for (int i = 1; i <= 14; ++i) {
            memcpy(upEntry[i] + 0x10, upNames[i], strlen(upNames[i]) + 1);
            upChunk[i] = A(upEntry[i]);
        }
        static uintptr_t upChunks[2] = { reinterpret_cast<uintptr_t>(upChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(upChunks), 0x10);

        // Guid (a ScriptStruct) whose UStruct::Children at +0x48 heads A -> B -> C -> D, four IntProperty UObjects.
        static uint8_t upScriptStructCls[0x100] = {}, upIntPropCls[0x100] = {}, upGuid[0x100] = {};
        static uint8_t upProp[4][0x100] = {};
        put32(upScriptStructCls, Grimoire::OFF_UOBJECT_NAME, 1);
        put32(upIntPropCls, Grimoire::OFF_UOBJECT_NAME, 3);
        putP(upGuid, Grimoire::OFF_UOBJECT_CLASS, A(upScriptStructCls));
        putP(upGuid, 0x48, A(upProp[0]));
        auto layout = [&](int nameIdxOfGuid, int nextOff, int elemOff, int offOff) {
            put32(upGuid, Grimoire::OFF_UOBJECT_NAME, nameIdxOfGuid);
            for (auto& p : upProp) memset(p, 0, sizeof(p));
            for (int i = 0; i < 4; ++i) {
                putP(upProp[i], Grimoire::OFF_UOBJECT_CLASS, A(upIntPropCls));
                put32(upProp[i], Grimoire::OFF_UOBJECT_NAME, 4 + i);
                if (i < 3) putP(upProp[i], nextOff, A(upProp[i + 1]));
                put32(upProp[i], elemOff - 4, 1);        // ArrayDim
                put32(upProp[i], elemOff, 4);            // ElementSize
                put32(upProp[i], offOff, i * 4);         // Offset_Internal
            }
        };
        FakePool upPool;
        upPool.Build(2);
        static uint8_t upZero[0x100] = {};
        const uintptr_t upObjs[2] = { A(upZero), A(upGuid) };
        for (int i = 0; i < 2; ++i)
            memcpy(upPool.chunks[0].data() + static_cast<size_t>(i) * FakePool::kItemSize, &upObjs[i], sizeof(uintptr_t));
        Aura::InitWithExtendedLayout(upPool.Addr(), FakePool::kItemSize);

        auto runGenau = [&](uint32_t ver) {
            DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x78));   // the FProperty default, as at load
            DynOff::UPROPERTY_OFFSET = 0x44;
            DynOff::UFIELD_NEXT = 0x28;
            Genau::ValidateAndFixOffsets(ver);
        };
        auto fam = [] {
            return std::to_string(DynOff::FSTRUCTPROP_STRUCT) + "/" + std::to_string(DynOff::FARRAYPROP_INNER) + "/"
                 + std::to_string(DynOff::FBOOLPROP_FIELDSIZE) + "/" + std::to_string(DynOff::FBYTEPROP_ENUM) + "/"
                 + std::to_string(DynOff::FENUMPROP_ENUM);
        };
        auto famAt = [](int s) {
            return DynOff::FSTRUCTPROP_STRUCT == s && DynOff::FARRAYPROP_INNER == s && DynOff::FBOOLPROP_FIELDSIZE == s
                && DynOff::FBYTEPROP_ENUM == s && DynOff::FENUMPROP_ENUM == s + 8;
        };

        // 4.23 stock: Next +0x28, ElementSize +0x34, Offset_Internal +0x44 -> subclass start 0x70.
        layout(2, 0x28, 0x34, 0x44);
        runGenau(423);
        check("UPROPSLOT setup: 4.23 is UProperty mode and Offset_Internal was MEASURED at +0x44",
              !DynOff::bUseFProperty && DynOff::UPROPERTY_OFFSET == 0x44 && DynOff::UPROPERTY_ELEMSIZE == 0x34,
              std::to_string(DynOff::UPROPERTY_OFFSET).c_str());
        check("UPROPSLOT control: the bool slot was already derived there (the A6 arm)",
              DynOff::UBOOLPROP_FIELDSIZE == 0x70, std::to_string(DynOff::UBOOLPROP_FIELDSIZE).c_str());
        check("UPROPSLOT ⭐: 4.23 stock -- the whole family at 0x70, UEnumProperty::Enum at 0x78", famAt(0x70),
              fam().c_str());

        // A walk now reads a 4.23-sized UStructProperty's Struct at +0x70 -- and not the NAMED object behind it at +0x78,
        // which the old 0x78 read took for the struct (a real neighbour UObject has a name too).
        static uint8_t upStructPropCls[0x100] = {}, upDecoy[0x100] = {}, upSP[0x100] = {}, upCls[0x100] = {};
        put32(upStructPropCls, Grimoire::OFF_UOBJECT_NAME, 8);
        put32(upDecoy, Grimoire::OFF_UOBJECT_NAME, 10);
        putP(upSP, Grimoire::OFF_UOBJECT_CLASS, A(upStructPropCls));
        put32(upSP, Grimoire::OFF_UOBJECT_NAME, 9);                  // "Where"
        put32(upSP, DynOff::UPROPERTY_ELEMSIZE - 4, 1);
        put32(upSP, DynOff::UPROPERTY_ELEMSIZE, 0x10);
        put32(upSP, DynOff::UPROPERTY_OFFSET, 0x28);
        putP(upSP, 0x70, A(upGuid));                                  // UStructProperty::Struct = Guid
        putP(upSP, 0x78, A(upDecoy));                                 // the next object in memory
        put32(upCls, DynOff::USTRUCT_PROPSSIZE, 0x38);
        putP(upCls, DynOff::USTRUCT_CHILDREN, A(upSP));
        const auto& upInfo = Ubel::WalkClassEx(A(upCls));
        const FieldInfo* upW = nullptr;
        for (const auto& f : upInfo.Fields) if (f.Name == "Where") upW = &f;
        check("UPROPSLOT setup: the 4.23 class walked its StructProperty",
              upW && upW->TypeName == "StructProperty", std::to_string(upInfo.Fields.size()).c_str());
        check("UPROPSLOT ⭐: ...and names its struct `Guid`, not the decoy object one pointer later",
              upW && upW->structType == "Guid", upW ? upW->structType.c_str() : "(no field)");

        // DQ XI S's shifted 4.18 layout: Next +0x38, ElementSize +0x44, Offset_Internal +0x54 -> start 0x80.
        layout(2, 0x38, 0x44, 0x54);
        runGenau(418);
        check("UPROPSLOT setup: the shifted layout's Offset_Internal was measured at +0x54",
              DynOff::UPROPERTY_OFFSET == 0x54, std::to_string(DynOff::UPROPERTY_OFFSET).c_str());
        check("UPROPSLOT ⭐: a DQ XI S-style shifted 4.18 -- the family at 0x80", famAt(0x80), fam().c_str());

        // 4.15 stock: Offset_Internal +0x50, delta 0x28 -> start 0x78, which the FProperty default happens to equal.
        layout(2, 0x28, 0x34, 0x50);
        runGenau(415);
        check("UPROPSLOT control: 4.15 stock -- the family at 0x78 (right before the fix by coincidence)",
              DynOff::UPROPERTY_OFFSET == 0x50 && famAt(0x78), fam().c_str());
        // Review wf_b99fb861-680 (F1): the tail ORDER came from the version alone. A 4.11-4.17 title whose version
        // detection fails is relabelled 422 (the TNameEntryArray rule), so the measured 0x50 got the 4.18+ tail and a
        // misaligned 0x7C where this very layout's start is 0x78 -- worse than the untouched default it replaced. The
        // measured layout says which order it is: Offset_Internal - ElementSize is 0x1C before 4.18, 0x10 from it.
        layout(2, 0x28, 0x34, 0x50);
        runGenau(422);
        check("UPROPSLOT ⭐: a 4.15 layout running under a misdetected 4.22 still gets 0x78 -- the layout decides",
              DynOff::UPROPERTY_OFFSET == 0x50 && famAt(0x78), fam().c_str());
        // ...and so does every reader that takes the UProperty start without the family (review of build 3594, LOW): the
        // class-valued names, WalkFunctions' parameters and Aura's parameter matcher recomputed it from the VERSION,
        // 0x7C here, where the family and the layout say 0x78. A TSubclassOf<APawn> UClassProperty, PropertyClass
        // `Class` at 0x78 and MetaClass `Pawn` at 0x80, walked under the misdetected label:
        {
            static uint8_t upClassCls[0x100] = {}, upPawnCls[0x100] = {}, upClassPropCls[0x100] = {}, upCP[0x100] = {},
                           upCls2[0x100] = {};
            putP(upClassCls, Grimoire::OFF_UOBJECT_CLASS, A(upClassCls));  put32(upClassCls, Grimoire::OFF_UOBJECT_NAME, 12);
            putP(upPawnCls, Grimoire::OFF_UOBJECT_CLASS, A(upClassCls));   put32(upPawnCls, Grimoire::OFF_UOBJECT_NAME, 13);
            put32(upClassPropCls, Grimoire::OFF_UOBJECT_NAME, 11);
            putP(upCP, Grimoire::OFF_UOBJECT_CLASS, A(upClassPropCls));
            put32(upCP, Grimoire::OFF_UOBJECT_NAME, 14);                    // "Kind"
            put32(upCP, DynOff::UPROPERTY_ELEMSIZE - 4, 1);
            put32(upCP, DynOff::UPROPERTY_ELEMSIZE, 8);
            put32(upCP, DynOff::UPROPERTY_OFFSET, 0x28);
            putP(upCP, 0x78, A(upClassCls));                                // UObjectPropertyBase::PropertyClass
            putP(upCP, 0x80, A(upPawnCls));                                 // UClassProperty::MetaClass
            put32(upCls2, DynOff::USTRUCT_PROPSSIZE, 0x30);
            putP(upCls2, DynOff::USTRUCT_CHILDREN, A(upCP));
            const uint32_t svVer2 = g_cachedUEVersion;
            g_cachedUEVersion = 422;
            const auto& upInfo2 = Ubel::WalkClassEx(A(upCls2));
            g_cachedUEVersion = svVer2;
            const FieldInfo* upK = nullptr;
            for (const auto& f : upInfo2.Fields) if (f.Name == "Kind") upK = &f;
            check("UPROPSLOT setup: the misdetected 4.15 class walked its ClassProperty",
                  upK && upK->TypeName == "ClassProperty", std::to_string(upInfo2.Fields.size()).c_str());
            check("UPROPSLOT ⭐: ...and reads its MetaClass at the layout's start, not the version's",
                  upK && upK->metaClassName == "Pawn", upK ? upK->metaClassName.c_str() : "(no field)");
        }

        // No Guid / Vector: Genau gives up on its defaults -- which must be the UProperty family, not FProperty's.
        layout(4, 0x28, 0x34, 0x44);
        runGenau(423);
        check("UPROPSLOT setup: no Guid / Vector takes the give-up",
              std::string(DynOff::g_offsetsFallbackReason) == "no-guid-or-vector-struct",
              DynOff::g_offsetsFallbackReason);
        check("UPROPSLOT ⭐: the give-up ships the 4.23 default family 0x70, not FProperty's 0x78", famAt(0x70),
              fam().c_str());
        // 4.11-4.17 stock put Offset_Internal at 0x50, not the 4.18+ 0x44 UPROPERTY_OFFSET defaults to: a default derived
        // from 0x44 there is 0x6C, where the untouched 0x78 was right.
        runGenau(415);
        check("UPROPSLOT ⭐: a 4.15 give-up keeps 0x78 -- no default derived from the 4.18+ Offset_Internal",
              famAt(0x78), fam().c_str());
        // ...and the readers outside the family take the same 0x78: with no start recorded, UPropertySubclassStart fell
        // back to the version formula over that very 0x44 default -- 0x6C (review of build 3596, LOW).
        check("UPROPSLOT ⭐: ...and so do the readers outside the family (UPropertySubclassStart), not 0x6C",
              DynOff::UPropertySubclassStart(415) == 0x78, std::to_string(DynOff::UPropertySubclassStart(415)).c_str());

        // Review wf_b99fb861-680 (F5), pinned after the review of build 3594 found it untested: a 4.18-4.24 title
        // labelled 4.25+ (Square Enix's 427 bias) starts in FProperty mode, so Step 2.5 set no UProperty family; the
        // ChildProperties scan then fails over to UProperty mode, and if the Offset_Internal probe fails too, the run
        // shipped the FProperty default 0x78 against the UProperty start 0x70. The fixture's UProperties are UObjects,
        // so the FField scan fails and the fallback flips; their Offset_Internal values are all 0, so the probe fails.
        layout(2, 0x28, 0x34, 0x44);
        for (auto& pr : upProp) put32(pr, 0x44, 0);
        runGenau(427);
        check("UPROPSLOT setup: a 4.27 label on a UProperty layout flipped to UProperty mode with Offset_Internal unmeasured",
              !DynOff::bUseFProperty && DynOff::UPROPERTY_OFFSET == 0x44,
              (std::to_string(DynOff::bUseFProperty) + " " + std::to_string(DynOff::UPROPERTY_OFFSET)).c_str());
        check("UPROPSLOT ⭐: ...and still ships the UProperty default family 0x70, not FProperty's 0x78 (the F5 arm)",
              famAt(0x70) && DynOff::UPROPERTY_SUBCLASS_START == 0x70, fam().c_str());

        Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);
        DynOff::ApplyPropertyFamily(svFamily);
        DynOff::bCasePreservingName = svCpn;  DynOff::UOBJECT_OUTER = svOuter;   DynOff::bUseFProperty = svFProp;
        DynOff::UFIELD_NEXT = svNext;         DynOff::USTRUCT_CHILDREN = svChildren; DynOff::USTRUCT_SUPER = svSuper;
        DynOff::USTRUCT_PROPSSIZE = svPropsSize; DynOff::USTRUCT_SCRIPT = svScript;
        DynOff::UPROPERTY_OFFSET = svUOff;    DynOff::UPROPERTY_ELEMSIZE = svUElem; DynOff::UPROPERTY_FLAGS = svUFlags;
        DynOff::UBOOLPROP_FIELDSIZE = svUBool; DynOff::bTaggedFFieldVariant = svTagged; DynOff::FNAME_NUMBER = svFNum;
        DynOff::UPROPERTY_SUBCLASS_START = svUStart;   // Genau set it; a later block must not inherit this fixture's
        DynOff::bOffsetsValidated.store(false); DynOff::bOffsetsProbeRan.store(false);
        DynOff::g_offsetsFallbackReason = "";
        g_cachedUEVersion = svVer;
    }

    // -- UPROPINNER-2026-09-27 -- a UProperty engine's container inners are named -------------------------------------
    //
    // ⛔ OWN name table (after UPROPSLOT). [UPROP-INNER-TYPENAME] WalkClassEx typed a container's inner / key / value /
    // element with the FField reader, which on a UProperty (a UObject) reads ObjectFlags | InternalIndex at +0x8 and
    // never names anything: UE423_Flying's export had 1,016 TArray<uint8_t> and 52 TMap<uint8_t, uint8_t>, and DQ XI S
    // typed 0 of 77 array inners. Objects are sized as 4.23's: subclass start 0x70 (the family, since UPROPSLOT).
    {
        blk("UPROPINNER - on a UProperty engine WalkClassEx names Array / Map / Set inners, and reads their classes");

        const auto svFProp = DynOff::bUseFProperty;       const auto svNext = DynOff::UFIELD_NEXT;
        const auto svChildren = DynOff::USTRUCT_CHILDREN; const auto svPropsSize = DynOff::USTRUCT_PROPSSIZE;
        const auto svUOff = DynOff::UPROPERTY_OFFSET;     const auto svUElem = DynOff::UPROPERTY_ELEMSIZE;
        const auto svCpn = DynOff::bCasePreservingName;
        const DynOff::PropertyFamily svFamily{ DynOff::FSTRUCTPROP_STRUCT, DynOff::FARRAYPROP_INNER,
                                               DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBYTEPROP_ENUM, DynOff::FENUMPROP_ENUM };
        const uint32_t svVer = g_cachedUEVersion;
        DynOff::bUseFProperty = false;  DynOff::bCasePreservingName = false;  g_cachedUEVersion = 423;
        DynOff::UFIELD_NEXT = 0x28;     DynOff::USTRUCT_CHILDREN = 0x48;      DynOff::USTRUCT_PROPSSIZE = 0x50;
        DynOff::UPROPERTY_OFFSET = 0x44; DynOff::UPROPERTY_ELEMSIZE = 0x34;
        DynOff::ApplyPropertyFamily(DynOff::UPropertyFamilyFor(0x44, 423, false));
        const int S = DynOff::FSTRUCTPROP_STRUCT;

        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto A     = [](uint8_t* b) { return reinterpret_cast<uintptr_t>(b); };

        static uint8_t uiEntry[14][0x40] = {};
        const char* uiNames[14] = { "", "ArrayProperty", "MapProperty", "SetProperty", "IntProperty", "NameProperty",
                                    "ObjectProperty", "ClassProperty", "Class", "Actor", "Scores", "ByName", "Kinds",
                                    "Inner" };
        static uintptr_t uiChunk[15] = {};
        for (int i = 1; i <= 13; ++i) {
            memcpy(uiEntry[i] + 0x10, uiNames[i], strlen(uiNames[i]) + 1);
            uiChunk[i] = A(uiEntry[i]);
        }
        static uintptr_t uiChunks[2] = { reinterpret_cast<uintptr_t>(uiChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(uiChunks), 0x10);

        // The property UClasses (named "ArrayProperty" ...), and the reflection objects Class / Actor.
        static uint8_t uiPC[8][0x100] = {};
        auto pcls = [&](int nameIdx) { put32(uiPC[nameIdx], Grimoire::OFF_UOBJECT_NAME, nameIdx); return A(uiPC[nameIdx]); };
        static uint8_t uiClass[0x100] = {}, uiActor[0x100] = {};
        put32(uiClass, Grimoire::OFF_UOBJECT_NAME, 8);  putP(uiClass, Grimoire::OFF_UOBJECT_CLASS, A(uiClass));
        put32(uiActor, Grimoire::OFF_UOBJECT_NAME, 9);  putP(uiActor, Grimoire::OFF_UOBJECT_CLASS, A(uiClass));

        // Inners: never chained into the class, each 4.23-sized, subclass members at S.
        static uint8_t uiInt[0x100] = {}, uiKey[0x100] = {}, uiVal[0x100] = {}, uiElem[0x100] = {};
        auto innerU = [&](uint8_t* p, uintptr_t cls) {
            putP(p, Grimoire::OFF_UOBJECT_CLASS, cls);  put32(p, Grimoire::OFF_UOBJECT_NAME, 13);
        };
        innerU(uiInt, pcls(4));                                                        // int32
        innerU(uiKey, pcls(5));                                                        // FName
        innerU(uiVal, pcls(6));  putP(uiVal, S, A(uiActor));                           // AActor*
        innerU(uiElem, pcls(7)); putP(uiElem, S, A(uiClass)); putP(uiElem, S + 8, A(uiActor));   // TSubclassOf<AActor>

        // The class's three container properties: TArray<int32> Scores, TMap<FName, AActor*> ByName,
        // TSet<TSubclassOf<AActor>> Kinds.
        static uint8_t uiArr[0x100] = {}, uiMap[0x100] = {}, uiSet[0x100] = {}, uiCls[0x100] = {};
        auto prop = [&](uint8_t* p, uintptr_t cls, int nameIdx, int32_t off, int32_t size, uint8_t* next) {
            putP(p, Grimoire::OFF_UOBJECT_CLASS, cls);  put32(p, Grimoire::OFF_UOBJECT_NAME, nameIdx);
            put32(p, DynOff::UPROPERTY_ELEMSIZE - 4, 1);  put32(p, DynOff::UPROPERTY_ELEMSIZE, size);
            put32(p, DynOff::UPROPERTY_OFFSET, off);
            putP(p, DynOff::UFIELD_NEXT, next ? A(next) : 0);
        };
        prop(uiArr, pcls(1), 10, 0x28, 0x10, uiMap);  putP(uiArr, S, A(uiInt));
        prop(uiMap, pcls(2), 11, 0x38, 0x50, uiSet);  putP(uiMap, S, A(uiKey));  putP(uiMap, S + 8, A(uiVal));
        prop(uiSet, pcls(3), 12, 0x88, 0x50, nullptr); putP(uiSet, S, A(uiElem));
        put32(uiCls, DynOff::USTRUCT_PROPSSIZE, 0xD8);
        putP(uiCls, DynOff::USTRUCT_CHILDREN, A(uiArr));

        const auto& uiInfo = Ubel::WalkClassEx(A(uiCls));
        auto field = [&](const char* n) -> const FieldInfo* {
            for (const auto& f : uiInfo.Fields) if (f.Name == n) return &f;
            return nullptr;
        };
        const FieldInfo* fA = field("Scores"); const FieldInfo* fM = field("ByName"); const FieldInfo* fS = field("Kinds");
        auto s = [](const FieldInfo* f, std::string FieldInfo::* m) { return f ? (f->*m).c_str() : "(no field)"; };
        check("UPROPINNER setup: the 4.23 class walked its three containers",
              fA && fM && fS && fA->TypeName == "ArrayProperty" && fM->TypeName == "MapProperty"
                && fS->TypeName == "SetProperty", std::to_string(uiInfo.Fields.size()).c_str());
        check("UPROPINNER ⭐: a TArray's inner is named", fA && fA->innerType == "IntProperty", s(fA, &FieldInfo::innerType));
        check("UPROPINNER ⭐: a TMap's key and value are named",
              fM && fM->keyType == "NameProperty" && fM->valueType == "ObjectProperty", s(fM, &FieldInfo::keyType));
        check("UPROPINNER ⭐: a TSet's element is named", fS && fS->elemType == "ClassProperty", s(fS, &FieldInfo::elemType));
        check("UPROPINNER ⭐: ...and a class-valued element reads its MetaClass",
              fS && fS->elemMetaClass == "Actor", s(fS, &FieldInfo::elemMetaClass));

        DynOff::ApplyPropertyFamily(svFamily);
        DynOff::bUseFProperty = svFProp;  DynOff::bCasePreservingName = svCpn;  g_cachedUEVersion = svVer;
        DynOff::UFIELD_NEXT = svNext;     DynOff::USTRUCT_CHILDREN = svChildren; DynOff::USTRUCT_PROPSSIZE = svPropsSize;
        DynOff::UPROPERTY_OFFSET = svUOff; DynOff::UPROPERTY_ELEMSIZE = svUElem;
    }

    // -- FAMILYEPOCH-2026-09-28 -- a class walked before the family moves is walked again after it ----------------------
    //
    // ⛔ OWN name table (after UPROPINNER). [FAMILY-EPOCH] The walk caches were keyed by class address alone, and two
    // writers move the property family after init (CorrectSubclassOffsets, WalkInstance's struct probe) -- measured on
    // UE423_Flying 0x78 -> 0x70, DQ XI S 0x78 -> 0x80, TQ2 0x74 -> 0x78. Every class cached before the move kept the
    // answer its old slot gave for the rest of the session. A named object at each candidate slot tells which one a
    // walk read; the class has no StructProperty, so CorrectSubclassOffsets cannot move anything by itself here.
    {
        blk("FAMILYEPOCH - a family move makes the walk caches read again, and a same-value write does not");

        const bool svFProp = DynOff::bUseFProperty;
        const DynOff::PropertyFamily svFamily{ DynOff::FSTRUCTPROP_STRUCT, DynOff::FARRAYPROP_INNER,
                                               DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBYTEPROP_ENUM, DynOff::FENUMPROP_ENUM };
        DynOff::bUseFProperty = true;

        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto A     = [](uint8_t* b) { return reinterpret_cast<uintptr_t>(b); };

        static uint8_t feEntry[8][0x40] = {};
        const char* feNames[8] = { "", "ObjectProperty", "BoolProperty", "Actor", "Pawn", "Target", "Flag", "Class" };
        static uintptr_t feChunk[9] = {};
        for (int i = 1; i <= 7; ++i) {
            memcpy(feEntry[i] + 0x10, feNames[i], strlen(feNames[i]) + 1);
            feChunk[i] = A(feEntry[i]);
        }
        static uintptr_t feChunks[2] = { reinterpret_cast<uintptr_t>(feChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(feChunks), 0x10);

        static uint8_t feObjFC[0x20] = {}, feBoolFC[0x20] = {}, feActor[0x100] = {}, fePawn[0x100] = {};
        put32(feObjFC, DynOff::FFIELDCLASS_NAME, 1);
        put32(feBoolFC, DynOff::FFIELDCLASS_NAME, 2);
        put32(feActor, Grimoire::OFF_UOBJECT_NAME, 3);
        put32(fePawn, Grimoire::OFF_UOBJECT_NAME, 4);
        // Both are UClasses -- an object property's PropertyClass must be one to be read ([STRUCTPROBE-ANY-NAME]).
        static uint8_t feClassCls[0x100] = {};
        putP(feClassCls, Grimoire::OFF_UOBJECT_CLASS, A(feClassCls));
        put32(feClassCls, Grimoire::OFF_UOBJECT_NAME, 7);
        putP(feActor, Grimoire::OFF_UOBJECT_CLASS, A(feClassCls));
        putP(fePawn, Grimoire::OFF_UOBJECT_CLASS, A(feClassCls));

        static uint8_t feTarget[0x100] = {}, feFlag[0x100] = {}, feCls[0x100] = {};
        auto fprop = [&](uint8_t* p, uintptr_t fc, int nameIdx, int32_t off, int32_t size, uint8_t* next) {
            putP(p, DynOff::FFIELD_CLASS, fc);
            put32(p, DynOff::FFIELD_NAME, nameIdx);
            put32(p, DynOff::FPROPERTY_OFFSET, off);
            put32(p, DynOff::FPROPERTY_ELEMSIZE, size);
            put32(p, DynOff::FPROPERTY_ELEMSIZE - 4, 1);
            putP(p, DynOff::FFIELD_NEXT, next ? A(next) : 0);
        };
        fprop(feTarget, A(feObjFC), 5, 0x28, 8, feFlag);            // UObject* Target
        putP(feTarget, 0x70, A(feActor));                           //   PropertyClass if the family is at 0x70
        putP(feTarget, 0x78, A(fePawn));                            //   ...or if it is at 0x78
        fprop(feFlag, A(feBoolFC), 6, 0x30, 1, nullptr);            // uint8 Flag : 1
        const uint8_t boolAt70[4] = { 1, 0, 0x04, 0x04 }, boolAt78[4] = { 1, 0, 0x01, 0x01 };
        memcpy(feFlag + 0x70, boolAt70, 4);
        memcpy(feFlag + 0x78, boolAt78, 4);
        put32(feCls, DynOff::USTRUCT_PROPSSIZE, 0x38);
        putP(feCls, DynOff::USTRUCT_CHILDPROPS, A(feTarget));

        auto target = [](const ClassInfo& ci) -> std::string {
            for (const auto& f : ci.Fields) if (f.Name == "Target") return f.objClassName;
            return "(no field)";
        };
        auto flagMask = [](const ClassInfo& ci) -> int {
            for (const auto& f : ci.Fields) if (f.Name == "Flag") return f.boolFieldMask;
            return -1;
        };

        DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x78));
        const ClassInfo& before = Ubel::WalkClassEx(A(feCls));
        check("FAMILYEPOCH setup: at 0x78 the walk reads the object there", target(before) == "Pawn",
              target(before).c_str());
        check("FAMILYEPOCH setup: ...and the plain walk the bool layout there", flagMask(Ubel::WalkClass(A(feCls))) == 1,
              std::to_string(flagMask(Ubel::WalkClass(A(feCls)))).c_str());

        DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x70));   // a late correction, as on UE423_Flying
        const ClassInfo& after = Ubel::WalkClassEx(A(feCls));
        check("FAMILYEPOCH ⭐: after the family moves, WalkClassEx reads the class again at the new slot",
              target(after) == "Actor", target(after).c_str());
        check("FAMILYEPOCH ⭐: ...and so does the plain WalkClass cache (bool layout)",
              flagMask(Ubel::WalkClass(A(feCls))) == 4, std::to_string(flagMask(Ubel::WalkClass(A(feCls)))).c_str());
        check("FAMILYEPOCH control: the reference handed out before the move is still valid and unchanged",
              target(before) == "Pawn", target(before).c_str());

        DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x70));   // the same values again
        check("FAMILYEPOCH control: re-publishing the same family is a cache HIT, not a new walk",
              &Ubel::WalkClassEx(A(feCls)) == &after);

        // Review wf_b99fb861-680 (F4): every builder worked its key out AT PUBLISH, after its reads. A move made by
        // another thread in between (a pipe lane's WalkInstance struct probe, a scan worker's CorrectSubclassOffsets)
        // filed the old slot's answer under the NEW epoch, where every later lookup found it. Each builder's test seam
        // moves the family 0x78 -> 0x70 at exactly that point, once; the next call must read the class again.
        static const char* s_raceCache = nullptr;
        auto moveOnce = [](const char* cache) {
            if (s_raceCache && strcmp(cache, s_raceCache) == 0) {
                s_raceCache = nullptr;
                DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x70));
            }
        };
        auto armRace = [](const char* cache) {
            DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x78));
            s_raceCache = cache;
        };
        static uint8_t feRace[5][0x100] = {};
        for (auto& c : feRace) {
            put32(c, DynOff::USTRUCT_PROPSSIZE, 0x38);
            putP(c, DynOff::USTRUCT_CHILDPROPS, A(feTarget));
        }
        Ubel::g_beforeFamilyCachePublishForTest = moveOnce;
        Aura::g_beforeFamilyCachePublishForTest = moveOnce;

        armRace("WalkClass");
        const int raceMask1 = flagMask(Ubel::WalkClass(A(feRace[0])));
        const bool fired1 = s_raceCache == nullptr;
        const int raceMask2 = flagMask(Ubel::WalkClass(A(feRace[0])));
        check("FAMILYEPOCH race setup: the WalkClass seam fired, after a read at 0x78", fired1 && raceMask1 == 1,
              std::to_string(raceMask1).c_str());
        check("FAMILYEPOCH race ⭐: a plain walk overtaken by a move is NOT served under the new epoch",
              raceMask2 == 4, std::to_string(raceMask2).c_str());

        armRace("WalkClassEx");
        const std::string raceTarget1 = target(Ubel::WalkClassEx(A(feRace[1])));
        const bool fired2 = s_raceCache == nullptr;
        const std::string raceTarget2 = target(Ubel::WalkClassEx(A(feRace[1])));
        check("FAMILYEPOCH race setup: the WalkClassEx seam fired, after a read at 0x78",
              fired2 && raceTarget1 == "Pawn", raceTarget1.c_str());
        check("FAMILYEPOCH race ⭐: an enriched walk overtaken by a move is NOT served under the new epoch",
              raceTarget2 == "Actor", raceTarget2.c_str());

        auto structMask = [](const std::vector<Ubel::CachedStructField>& v) -> int {
            for (const auto& f : v) if (f.name == "Flag") return f.boolFieldMask;
            return -1;
        };
        armRace("StructFields");
        const int raceSMask1 = structMask(Ubel::GetCachedStructFields(A(feRace[2])));
        const bool fired3 = s_raceCache == nullptr;
        const int raceSMask2 = structMask(Ubel::GetCachedStructFields(A(feRace[2])));
        check("FAMILYEPOCH race setup: the struct-field seam fired, after a read at 0x78", fired3 && raceSMask1 == 1,
              std::to_string(raceSMask1).c_str());
        check("FAMILYEPOCH race ⭐: struct fields overtaken by a move are NOT served under the new epoch",
              raceSMask2 == 4, std::to_string(raceSMask2).c_str());

        // The two Aura memos hold no slot value this fixture can tell apart (no container, no reference), so they are
        // pinned by identity: an entry built across the move must not be the one the next call is served.
        armRace("ClassContainers");
        const auto* raceC1 = &Aura::GetClassContainers(A(feRace[3]));
        const bool fired4 = s_raceCache == nullptr;
        const auto* raceC2 = &Aura::GetClassContainers(A(feRace[3]));
        check("FAMILYEPOCH race setup: the container seam fired", fired4);
        check("FAMILYEPOCH race ⭐: a container list built across a move is NOT served under the new epoch",
              raceC1 != raceC2);
        check("FAMILYEPOCH race control: ...and the rebuilt one is a plain cache hit",
              &Aura::GetClassContainers(A(feRace[3])) == raceC2);

        armRace("ClassRefMeta");
        const auto* raceR1 = &Aura::GetClassRefMeta(A(feRace[4]));
        const bool fired5 = s_raceCache == nullptr;
        const auto* raceR2 = &Aura::GetClassRefMeta(A(feRace[4]));
        check("FAMILYEPOCH race setup: the reference-meta seam fired", fired5);
        check("FAMILYEPOCH race ⭐: reference meta built across a move is NOT served under the new epoch",
              raceR1 != raceR2);
        check("FAMILYEPOCH race control: ...and the rebuilt one is a plain cache hit",
              &Aura::GetClassRefMeta(A(feRace[4])) == raceR2);

        Ubel::g_beforeFamilyCachePublishForTest = nullptr;
        Aura::g_beforeFamilyCachePublishForTest = nullptr;
        s_raceCache = nullptr;

        DynOff::ApplyPropertyFamily(svFamily);
        DynOff::bUseFProperty = svFProp;
    }

    // -- UPROPFLAT-2026-09-28 -- WalkInstance's UProperty container arms read an inner's struct at the family slot -------
    //
    // ⛔ OWN name table (after FAMILYEPOCH). [UPROP-CONTAINER-FLAT-2C] The UProperty TArray / TMap / TSet arms probed the
    // inner pointer around a flat UPROPERTY_OFFSET + 0x2C and then read the inner's UScriptStruct at that SAME flat
    // offset, unprobed. It is right on stock 4.18-4.24 only: 4.11-4.17 put the subclass start at Offset_Internal + 0x28
    // and a case-preserving build at + 0x34, so a struct element's type came from the wrong slot.
    {
        blk("UPROPFLAT - a UProperty TArray<struct>'s element struct is read at the family slot on 4.15 and case-preserving 4.23");

        const bool svFProp = DynOff::bUseFProperty;  const bool svCpn = DynOff::bCasePreservingName;
        const auto svNext = DynOff::UFIELD_NEXT;     const auto svChildren = DynOff::USTRUCT_CHILDREN;
        const auto svPropsSize = DynOff::USTRUCT_PROPSSIZE;
        const auto svUOff = DynOff::UPROPERTY_OFFSET; const auto svUElem = DynOff::UPROPERTY_ELEMSIZE;
        const DynOff::PropertyFamily svFamily{ DynOff::FSTRUCTPROP_STRUCT, DynOff::FARRAYPROP_INNER,
                                               DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBYTEPROP_ENUM, DynOff::FENUMPROP_ENUM };

        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto A     = [](uint8_t* b) { return reinterpret_cast<uintptr_t>(b); };

        static uint8_t ufEntry[6][0x40] = {};
        const char* ufNames[6] = { "", "ArrayProperty", "StructProperty", "Vector", "Decoy", "Points" };
        static uintptr_t ufChunk[7] = {};
        for (int i = 1; i <= 5; ++i) {
            memcpy(ufEntry[i] + 0x10, ufNames[i], strlen(ufNames[i]) + 1);
            ufChunk[i] = A(ufEntry[i]);
        }
        static uintptr_t ufChunks[2] = { reinterpret_cast<uintptr_t>(ufChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(ufChunks), 0x10);

        static uint8_t ufArrCls[0x40] = {}, ufStructCls[0x40] = {}, ufVector[0x100] = {}, ufDecoy[0x100] = {};
        put32(ufArrCls, Grimoire::OFF_UOBJECT_NAME, 1);
        put32(ufStructCls, Grimoire::OFF_UOBJECT_NAME, 2);
        put32(ufVector, Grimoire::OFF_UOBJECT_NAME, 3);
        put32(ufDecoy, Grimoire::OFF_UOBJECT_NAME, 4);

        // One layout per call: an ArrayProperty "Points" whose Inner is a StructProperty of Vector, both UProperties
        // with the subclass start at `start`; a named decoy sits at `decoyAt` in the inner when it does not overlap.
        auto walkAt = [&](int ver, bool cpn, int next, int elem, int offInt, int decoyAt, uint8_t* arr, uint8_t* inner,
                          uint8_t* cls, uint8_t* inst) -> std::string {
            DynOff::bUseFProperty = false;  DynOff::bCasePreservingName = cpn;
            DynOff::UFIELD_NEXT = next;     DynOff::UPROPERTY_ELEMSIZE = elem;  DynOff::UPROPERTY_OFFSET = offInt;
            DynOff::USTRUCT_CHILDREN = 0x48; DynOff::USTRUCT_PROPSSIZE = 0x50;
            DynOff::ApplyPropertyFamily(DynOff::UPropertyFamilyFor(offInt, ver, cpn));
            const int S = DynOff::FSTRUCTPROP_STRUCT;
            putP(inner, Grimoire::OFF_UOBJECT_CLASS, A(ufStructCls));
            put32(inner, elem, 12);
            putP(inner, S, A(ufVector));                               // UStructProperty::Struct
            if (decoyAt >= 0) putP(inner, decoyAt, A(ufDecoy));
            putP(arr, Grimoire::OFF_UOBJECT_CLASS, A(ufArrCls));
            put32(arr, Grimoire::OFF_UOBJECT_NAME, 5);
            put32(arr, elem - 4, 1);  put32(arr, elem, 0x10);  put32(arr, offInt, 0x28);
            putP(arr, S, A(inner));                                    // UArrayProperty::Inner
            put32(cls, DynOff::USTRUCT_PROPSSIZE, 0x38);
            putP(cls, DynOff::USTRUCT_CHILDREN, A(arr));
            const auto r = Ubel::WalkInstance(A(inst), A(cls), 64, 2, false);   // an empty TArray: Data 0, Num 0
            for (const auto& f : r.fields) if (f.name == "Points") return f.arrayInnerStructType + "|" + f.arrayInnerType;
            return "(no field)";
        };

        static uint8_t a1[0x100] = {}, i1[0x100] = {}, c1[0x100] = {}, n1[0x100] = {};
        const std::string r415 = walkAt(415, false, 0x28, 0x34, 0x50, -1, a1, i1, c1, n1);   // start 0x78, flat 0x7C
        check("UPROPFLAT setup: the 4.15 walk found the array and its StructProperty inner",
              r415.find('|') != std::string::npos && r415.substr(r415.find('|') + 1) == "StructProperty", r415.c_str());
        check("UPROPFLAT ⭐: 4.15 -- the element struct is read at 0x78, not at the flat 0x7C",
              r415.substr(0, r415.find('|')) == "Vector", r415.c_str());

        static uint8_t a2[0x100] = {}, i2[0x100] = {}, c2[0x100] = {}, n2[0x100] = {};
        const std::string r423 = walkAt(423, true, 0x30, 0x3C, 0x4C, 0x78, a2, i2, c2, n2);   // start 0x80, flat 0x78
        check("UPROPFLAT ⭐: case-preserving 4.23 -- the struct at 0x80, not the named object at the flat 0x78",
              r423.substr(0, r423.find('|')) == "Vector", r423.c_str());

        DynOff::ApplyPropertyFamily(svFamily);
        DynOff::bUseFProperty = svFProp;  DynOff::bCasePreservingName = svCpn;
        DynOff::UFIELD_NEXT = svNext;     DynOff::USTRUCT_CHILDREN = svChildren; DynOff::USTRUCT_PROPSSIZE = svPropsSize;
        DynOff::UPROPERTY_OFFSET = svUOff; DynOff::UPROPERTY_ELEMSIZE = svUElem;
    }

    // -- CONTAINEROBJ-2026-09-28 -- a Map key / value, a Set element and every object-family inner carry their class ------
    //
    // ⛔ OWN name table (after UPROPFLAT). [SDK-CONTAINER-OBJCLASS] walk_class published an object property's class for
    // an Array inner of Object / Class type only, so EVERSPACE 2's SDK header had 20 TMap<FName, class UObject*>, 28
    // TSet<class UObject*> and 50 TSoftObjectPtr<UObject>, and a class-valued map key whose PropertyClass is a UClass
    // subclass -- ALevelVariantSetsActor::DirectorInstances -- came out UClass* (the source oracle's one MISMATCH).
    {
        blk("CONTAINEROBJ - Map key / value, Set element and object-family Array inners publish their PropertyClass");

        const bool svFProp = DynOff::bUseFProperty;
        const DynOff::PropertyFamily svFamily{ DynOff::FSTRUCTPROP_STRUCT, DynOff::FARRAYPROP_INNER,
                                               DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBYTEPROP_ENUM, DynOff::FENUMPROP_ENUM };
        DynOff::bUseFProperty = true;
        DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x78));
        const int slot = DynOff::FSTRUCTPROP_STRUCT;

        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto A     = [](uint8_t* b) { return reinterpret_cast<uintptr_t>(b); };

        static uint8_t coEntry[19][0x40] = {};
        const char* coNames[19] = { "", "MapProperty", "SetProperty", "ArrayProperty", "NameProperty", "ObjectProperty",
                                    "WeakObjectProperty", "SoftObjectProperty", "ClassProperty", "IntProperty", "Class",
                                    "BlueprintGeneratedClass", "Actor", "Object", "ByName", "Watched", "Softs",
                                    "Directors", "Inner" };
        static uintptr_t coChunk[20] = {};
        for (int i = 1; i <= 18; ++i) {
            memcpy(coEntry[i] + 0x10, coNames[i], strlen(coNames[i]) + 1);
            coChunk[i] = A(coEntry[i]);
        }
        static uintptr_t coChunks[2] = { reinterpret_cast<uintptr_t>(coChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(coChunks), 0x10);

        // Reflection objects: Class is its own class; BlueprintGeneratedClass is a Class whose super is Class.
        static uint8_t coObj[14][0x100] = {};
        auto obj = [&](int nameIdx) { return A(coObj[nameIdx]); };
        for (int i = 10; i <= 13; ++i) { put32(coObj[i], Grimoire::OFF_UOBJECT_NAME, i); putP(coObj[i], Grimoire::OFF_UOBJECT_CLASS, obj(10)); }
        putP(coObj[11], DynOff::USTRUCT_SUPER, obj(10));

        static uint8_t coFC[10][0x20] = {};
        auto fclass = [&](int nameIdx) { put32(coFC[nameIdx], DynOff::FFIELDCLASS_NAME, nameIdx); return A(coFC[nameIdx]); };

        // Inners: FFields never chained into the class.
        static uint8_t coKeyName[0x100] = {}, coValObj[0x100] = {}, coElemWeak[0x100] = {}, coInnerSoft[0x100] = {},
                       coKeyDir[0x100] = {}, coValInt[0x100] = {};
        auto inner = [&](uint8_t* p, uintptr_t fc, uintptr_t propertyClass, uintptr_t meta) {
            putP(p, DynOff::FFIELD_CLASS, fc);  put32(p, DynOff::FFIELD_NAME, 18);
            if (propertyClass) putP(p, slot, propertyClass);
            if (meta)          putP(p, slot + 8, meta);
        };
        inner(coKeyName, fclass(4), 0, 0);                 // FName
        inner(coValObj, fclass(5), obj(12), 0);            // AActor*
        inner(coElemWeak, fclass(6), obj(12), 0);          // TWeakObjectPtr<AActor>
        inner(coInnerSoft, fclass(7), obj(12), 0);         // TSoftObjectPtr<AActor>
        inner(coKeyDir, fclass(8), obj(11), obj(13));      // TObjectPtr<UBlueprintGeneratedClass>: PropertyClass BGC, Meta Object
        inner(coValInt, fclass(9), 0, 0);                  // int32

        static uint8_t coP[4][0x100] = {};
        auto prop = [&](int i, uintptr_t fc, int nameIdx, int32_t off, int32_t size, uint8_t* next) {
            putP(coP[i], DynOff::FFIELD_CLASS, fc);          put32(coP[i], DynOff::FFIELD_NAME, nameIdx);
            put32(coP[i], DynOff::FPROPERTY_OFFSET, off);    put32(coP[i], DynOff::FPROPERTY_ELEMSIZE, size);
            put32(coP[i], DynOff::FPROPERTY_ELEMSIZE - 4, 1); putP(coP[i], DynOff::FFIELD_NEXT, next ? A(next) : 0);
        };
        prop(0, fclass(1), 14, 0x28, 0x50, coP[1]);  putP(coP[0], slot, A(coKeyName));  putP(coP[0], slot + 8, A(coValObj));
        prop(1, fclass(2), 15, 0x78, 0x50, coP[2]);  putP(coP[1], slot, A(coElemWeak));
        prop(2, fclass(3), 16, 0xC8, 0x10, coP[3]);  putP(coP[2], slot, A(coInnerSoft));
        prop(3, fclass(1), 17, 0xD8, 0x50, nullptr); putP(coP[3], slot, A(coKeyDir));    putP(coP[3], slot + 8, A(coValInt));
        static uint8_t coCls[0x100] = {};
        put32(coCls, DynOff::USTRUCT_PROPSSIZE, 0x128);
        putP(coCls, DynOff::USTRUCT_CHILDPROPS, A(coP[0]));

        const auto& coInfo = Ubel::WalkClassEx(A(coCls));
        auto field = [&](const char* n) -> const FieldInfo* {
            for (const auto& f : coInfo.Fields) if (f.Name == n) return &f;
            return nullptr;
        };
        const FieldInfo* fBy = field("ByName"); const FieldInfo* fW = field("Watched");
        const FieldInfo* fS = field("Softs");   const FieldInfo* fD = field("Directors");
        auto s = [](const FieldInfo* f, std::string FieldInfo::* m) { return f ? (f->*m).c_str() : "(no field)"; };
        check("CONTAINEROBJ setup: the four containers walked with their inner types",
              fBy && fW && fS && fD && fBy->valueType == "ObjectProperty" && fW->elemType == "WeakObjectProperty"
                && fS->innerType == "SoftObjectProperty" && fD->keyType == "ClassProperty",
              std::to_string(coInfo.Fields.size()).c_str());
        check("CONTAINEROBJ ⭐: a Map's object VALUE publishes its class", fBy && fBy->valueObjClass == "Actor",
              s(fBy, &FieldInfo::valueObjClass));
        check("CONTAINEROBJ control: ...and its FName key none", fBy && fBy->keyObjClass.empty(), s(fBy, &FieldInfo::keyObjClass));
        check("CONTAINEROBJ ⭐: a Set's weak-object ELEMENT publishes its class", fW && fW->elemObjClass == "Actor",
              s(fW, &FieldInfo::elemObjClass));
        check("CONTAINEROBJ ⭐: an Array's soft-object INNER publishes its class (the whole object family, not Object / Class)",
              fS && fS->innerObjClass == "Actor", s(fS, &FieldInfo::innerObjClass));
        check("CONTAINEROBJ ⭐: a class-valued Map KEY publishes its PropertyClass (a UClass subclass)",
              fD && fD->keyObjClass == "BlueprintGeneratedClass", s(fD, &FieldInfo::keyObjClass));
        check("CONTAINEROBJ control: ...beside its MetaClass Object", fD && fD->keyMetaClass == "Object",
              s(fD, &FieldInfo::keyMetaClass));

        // [UE51-CLASSPTRPROP] UE 5.0 / 5.1 build an FClassPtrProperty for every `TObjectPtr<UClass-derived>` UPROPERTY
        // -- an FClassProperty subclass with no data of its own (UE_5.1 UnrealType.h) -- and nothing downstream knew
        // the name: DumperTest51's SDK export wrote 29 of them as raw bytes and its .usmap 30 slots of type Unknown
        // (0xFF), which an unversioned reader cannot size. Same fixture: one more class, one field of that type.
        static uint8_t coPtrFC[0x20] = {};
        static uint8_t coPtrEntry[0x40] = {};
        memcpy(coPtrEntry + 0x10, "ClassPtrProperty", 17);
        static uintptr_t coPtrChunk[21] = {};
        for (int i = 1; i <= 18; ++i) coPtrChunk[i] = coChunk[i];
        coPtrChunk[19] = A(coPtrEntry);
        static uintptr_t coPtrChunks[2] = { reinterpret_cast<uintptr_t>(coPtrChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(coPtrChunks), 0x10);
        put32(coPtrFC, DynOff::FFIELDCLASS_NAME, 19);
        static uint8_t coPtrProp[0x100] = {}, coPtrCls[0x100] = {};
        putP(coPtrProp, DynOff::FFIELD_CLASS, A(coPtrFC));  put32(coPtrProp, DynOff::FFIELD_NAME, 17);   // "Directors"
        put32(coPtrProp, DynOff::FPROPERTY_OFFSET, 0x28);   put32(coPtrProp, DynOff::FPROPERTY_ELEMSIZE, 8);
        put32(coPtrProp, DynOff::FPROPERTY_ELEMSIZE - 4, 1);
        putP(coPtrProp, slot, obj(11));                     // PropertyClass BlueprintGeneratedClass
        putP(coPtrProp, slot + 8, obj(13));                 // MetaClass Object
        put32(coPtrCls, DynOff::USTRUCT_PROPSSIZE, 0x30);
        putP(coPtrCls, DynOff::USTRUCT_CHILDPROPS, A(coPtrProp));
        const auto& cpInfo = Ubel::WalkClassEx(A(coPtrCls));
        const FieldInfo* fP = cpInfo.Fields.empty() ? nullptr : &cpInfo.Fields[0];
        check("CLASSPTRPROP ⭐: a UE 5.0 / 5.1 ClassPtrProperty is reported as the ClassProperty it is",
              fP && fP->TypeName == "ClassProperty", fP ? fP->TypeName.c_str() : "(no field)");
        check("CLASSPTRPROP ⭐: ...and reads its PropertyClass and MetaClass like one",
              fP && fP->objClassName == "BlueprintGeneratedClass" && fP->metaClassName == "Object",
              fP ? fP->objClassName.c_str() : "(no field)");

        DynOff::ApplyPropertyFamily(svFamily);
        DynOff::bUseFProperty = svFProp;
    }

    // -- LISTCLASSES-2026-09-28 -- the class enumerators do not take a metaclass's class-default object for a class -----
    //
    // ⛔ OWN name table and OWN object array: re-initialises Aura, and puts the main pool back at the end.
    // [LISTCLASSES-METACLASS-CDO] A metaclass's CDO -- Default__Class, Default__BlueprintGeneratedClass, ... -- has that
    // metaclass as its own class, so the class-like-meta test alone admitted it: with "Game classes only" unticked,
    // UE423_Flying's class list carried its five metaclass CDOs as classes, and every enumerator counted five classes
    // too many (review wf_b99fb861-680). [DUMPALL-METACLASS-CDO] fixed the same shape in Dump All.
    {
        blk("LISTCLASSES - a metaclass's class-default object is not listed or counted as a class");
        ResetCancel();
        const bool svFProp = DynOff::bUseFProperty;
        DynOff::bUseFProperty = true;
        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto A     = [](const void* b) { return reinterpret_cast<uintptr_t>(b); };

        static uint8_t lcEntry[7][0x40] = {};
        const char* lcNames[7] = { "", "Class", "Actor", "Default__Class", "BlueprintGeneratedClass",
                                   "Default__BlueprintGeneratedClass", "BP_Hero_C" };
        static uintptr_t lcNameChunk[8] = {};
        for (int i = 1; i <= 6; ++i) {
            memcpy(lcEntry[i] + 0x10, lcNames[i], strlen(lcNames[i]) + 1);
            lcNameChunk[i] = A(lcEntry[i]);
        }
        static uintptr_t lcNameChunks[2] = { A(lcNameChunk), 0 };
        Serie::InitUE4(A(lcNameChunks), 0x10);

        // Object i is named lcNames[i + 1], and its class is the metaclass it has in an engine: UClass for the two
        // classes, the BGC metaclass and UClass's own CDO; BlueprintGeneratedClass for the Blueprint class and its CDO.
        static uint8_t lcObj[6][0x100] = {};
        const int lcClassOf[6] = { 0, 0, 0, 0, 3, 3 };
        for (int i = 0; i < 6; ++i) {
            putP(lcObj[i], Grimoire::OFF_UOBJECT_CLASS, A(lcObj[lcClassOf[i]]));
            put32(lcObj[i], Grimoire::OFF_UOBJECT_NAME, i + 1);
        }
        put32(lcObj[1], DynOff::USTRUCT_PROPSSIZE, 0x30);   // Actor
        put32(lcObj[5], DynOff::USTRUCT_PROPSSIZE, 0x38);   // BP_Hero_C

        static uint8_t lcChunk[6 * 24] = {};
        static uintptr_t lcTable[2] = {};
        static uint8_t lcHdr[0x40] = {};
        for (int i = 0; i < 6; ++i) putP(lcChunk, i * 24, A(lcObj[i]));
        lcTable[0] = A(lcChunk);
        putP(lcHdr, 0x10, A(lcTable));
        put32(lcHdr, 0x20, 6);  put32(lcHdr, 0x24, 6);
        put32(lcHdr, 0x28, 1);  put32(lcHdr, 0x2C, 1);
        Aura::InitWithExtendedLayout(A(lcHdr), 24);
        check("LISTCLASSES setup: the array holds the six objects", Aura::GetCount() == 6,
              std::to_string(Aura::GetCount()).c_str());

        const auto lc = Aura::ListClasses(false);
        std::string lcRows;
        bool lcAnyCdo = false;
        for (const auto& e : lc.results) {
            lcRows += e.className + " ";
            if (e.className.rfind("Default__", 0) == 0) lcAnyCdo = true;
        }
        check("LISTCLASSES ⭐: no class-default object is listed as a class", !lcAnyCdo, lcRows.c_str());
        check("LISTCLASSES ⭐: ...nor counted in the total", lc.totalClasses == 4,
              std::to_string(lc.totalClasses).c_str());
        check("LISTCLASSES control: every class is still listed, the metaclasses and a Blueprint class included",
              lc.results.size() == 4 && lcRows.find("BP_Hero_C") != std::string::npos
              && lcRows.find("Actor") != std::string::npos && lcRows.find("BlueprintGeneratedClass") != std::string::npos,
              lcRows.c_str());

        const auto lcSearch = Aura::SearchProperties("Health", {}, false);
        check("LISTCLASSES ⭐: the property search does not scan a class-default object as a class",
              lcSearch.scannedClasses == 4, std::to_string(lcSearch.scannedClasses).c_str());
        const auto lcBatch = Aura::SearchPropertiesBatch({ "Health" }, {}, false);
        check("LISTCLASSES ⭐: ...nor does the batched property search",
              lcBatch.size() == 1 && lcBatch[0].scannedClasses == 4,
              lcBatch.empty() ? "(no result)" : std::to_string(lcBatch[0].scannedClasses).c_str());
        const auto lcFuncs = Aura::EnumerateAllFunctions(false);
        check("LISTCLASSES ⭐: ...nor the function enumerator", lcFuncs.scannedClasses == 4,
              std::to_string(lcFuncs.scannedClasses).c_str());

        Aura::InitWithExtendedLayout(pool.Addr(), FakePool::kItemSize);
        check("LISTCLASSES control: the main pool is back", Aura::GetCount() == kCount);
        DynOff::bUseFProperty = svFProp;
    }

    // -- STRUCTENUM-2026-09-28 -- the struct-field cache keeps a ByteProperty's enum only if it IS a UEnum ------------------
    //
    // ⛔ OWN name table (after LISTCLASSES). [STRUCTCACHE-ENUM-UNCHECKED] GetCachedStructFields stored whatever pointer sat
    // in a ByteProperty's Enum slot, unchecked, for the session; ResolveEnumValue then parsed that object as a UEnum and
    // printed its "enumerators" for a plain byte inside a struct array. The WalkInstance twin and the array reader both
    // require the pointer's class to be Enum / UserDefinedEnum (review wf_63e981ac-5e4, S4).
    {
        blk("STRUCTENUM - a struct's ByteProperty caches its enum only when the pointer is a UEnum");
        const bool svFProp = DynOff::bUseFProperty;
        const DynOff::PropertyFamily svFamily{ DynOff::FSTRUCTPROP_STRUCT, DynOff::FARRAYPROP_INNER,
                                               DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBYTEPROP_ENUM, DynOff::FENUMPROP_ENUM };
        DynOff::bUseFProperty = true;
        DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x78));

        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto A     = [](const void* b) { return reinterpret_cast<uintptr_t>(b); };

        static uint8_t seEntry[9][0x40] = {};
        const char* seNames[9] = { "", "ByteProperty", "Mode", "Kind", "Enum", "EColor", "Actor", "Decoy",
                                   "UserDefinedEnum" };
        static uintptr_t seChunk[10] = {};
        for (int i = 1; i <= 8; ++i) {
            memcpy(seEntry[i] + 0x10, seNames[i], strlen(seNames[i]) + 1);
            seChunk[i] = A(seEntry[i]);
        }
        static uintptr_t seChunks[2] = { A(seChunk), 0 };
        Serie::InitUE4(A(seChunks), 0x10);

        // UObjects: the Enum metaclass and one UEnum of it; an Actor class and a named Actor instance (the decoy).
        static uint8_t seEnumCls[0x100] = {}, seEnum[0x100] = {}, seActorCls[0x100] = {}, seDecoy[0x100] = {};
        putP(seEnumCls, Grimoire::OFF_UOBJECT_CLASS, A(seEnumCls));   put32(seEnumCls, Grimoire::OFF_UOBJECT_NAME, 4);
        putP(seEnum, Grimoire::OFF_UOBJECT_CLASS, A(seEnumCls));      put32(seEnum, Grimoire::OFF_UOBJECT_NAME, 5);
        putP(seActorCls, Grimoire::OFF_UOBJECT_CLASS, A(seActorCls)); put32(seActorCls, Grimoire::OFF_UOBJECT_NAME, 6);
        putP(seDecoy, Grimoire::OFF_UOBJECT_CLASS, A(seActorCls));    put32(seDecoy, Grimoire::OFF_UOBJECT_NAME, 7);

        static uint8_t seByteFC[0x20] = {};
        put32(seByteFC, DynOff::FFIELDCLASS_NAME, 1);
        static uint8_t seMode[0x100] = {}, seKind[0x100] = {}, seStruct[0x100] = {};
        auto fprop = [&](uint8_t* p, int nameIdx, int32_t off, uint8_t* next, uintptr_t enumSlot) {
            putP(p, DynOff::FFIELD_CLASS, A(seByteFC));
            put32(p, DynOff::FFIELD_NAME, nameIdx);
            put32(p, DynOff::FPROPERTY_OFFSET, off);
            put32(p, DynOff::FPROPERTY_ELEMSIZE, 1);
            put32(p, DynOff::FPROPERTY_ELEMSIZE - 4, 1);
            putP(p, DynOff::FFIELD_NEXT, next ? A(next) : 0);
            putP(p, 0x78, enumSlot);                                   // FByteProperty::Enum at the family base
        };
        fprop(seMode, 2, 0x00, seKind, A(seDecoy));   // uint8 Mode -- the slot holds a named non-enum object
        fprop(seKind, 3, 0x01, nullptr, A(seEnum));   // TEnumAsByte<EColor> Kind
        put32(seStruct, DynOff::USTRUCT_PROPSSIZE, 0x02);
        putP(seStruct, DynOff::USTRUCT_CHILDPROPS, A(seMode));

        const auto& seFields = Ubel::GetCachedStructFields(A(seStruct));
        auto enumOf = [&](const char* name) -> uintptr_t {
            for (const auto& f : seFields) if (f.name == name) return f.enumAddr;
            return ~uintptr_t(0);
        };
        check("STRUCTENUM setup: both byte fields were walked", seFields.size() == 2,
              std::to_string(seFields.size()).c_str());
        check("STRUCTENUM ⭐: a ByteProperty whose Enum slot holds a non-enum object caches NO enum",
              enumOf("Mode") == 0, (std::string("Mode enumAddr=") + std::to_string(enumOf("Mode"))).c_str());
        check("STRUCTENUM control: a real UEnum is still cached", enumOf("Kind") == A(seEnum));

        DynOff::ApplyPropertyFamily(svFamily);
        DynOff::bUseFProperty = svFProp;
    }

    // -- STRUCTPROBE-2026-09-28 -- a struct / class slot is accepted only when it holds a struct / a class ----------------
    //
    // ⛔ OWN name table (after STRUCTENUM). [STRUCTPROBE-ANY-NAME] The subclass-slot readers accepted ANY object with a
    // printable name. On a shifted layout the default slot holds another named object -- on DQ XI S a Blueprint-owned
    // property's PostConstructLinkNext, a named UProperty -- and it passed: a struct member was typed as a property's
    // name, and the WalkInstance probe stopped at delta 0 on it instead of finding the struct 8 bytes on (review
    // wf_63e981ac-5e4, S2). A slot is accepted now only when the object's class chain reaches ScriptStruct (a struct)
    // or Class (an object property's PropertyClass).
    {
        blk("STRUCTPROBE - a subclass slot is accepted only when it holds the kind of object it must");
        ResetCancel();
        const bool svFProp = DynOff::bUseFProperty;
        const DynOff::PropertyFamily svFamily{ DynOff::FSTRUCTPROP_STRUCT, DynOff::FARRAYPROP_INNER,
                                               DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBYTEPROP_ENUM, DynOff::FENUMPROP_ENUM };
        DynOff::bUseFProperty = true;
        DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x78));

        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto A     = [](const void* b) { return reinterpret_cast<uintptr_t>(b); };

        static uint8_t spEntry[15][0x40] = {};
        const char* spNames[15] = { "", "StructProperty", "ObjectProperty", "ScriptStruct", "Class", "Vector", "Actor",
                                    "Decoy", "Loc", "Owner", "UserDefinedStruct", "S_Item", "Item", "Pos", "Target" };
        static uintptr_t spChunk[16] = {};
        for (int i = 1; i <= 14; ++i) {
            memcpy(spEntry[i] + 0x10, spNames[i], strlen(spNames[i]) + 1);
            spChunk[i] = A(spEntry[i]);
        }
        static uintptr_t spChunks[2] = { A(spChunk), 0 };
        Serie::InitUE4(A(spChunks), 0x10);

        // UObjects as an engine has them: UClass (its own class), the ScriptStruct and UserDefinedStruct metaclasses
        // (UserDefinedStruct's super is ScriptStruct), a native struct and a Blueprint struct, the Actor class, and a
        // named Actor instance -- the decoy, a named object that is neither a struct nor a class.
        static uint8_t spClassCls[0x100] = {}, spSsCls[0x100] = {}, spUdsCls[0x100] = {}, spVector[0x100] = {},
                       spUds[0x100] = {}, spActorCls[0x100] = {}, spDecoy[0x100] = {};
        auto uobj = [&](uint8_t* o, const uint8_t* cls, int nameIdx) {
            putP(o, Grimoire::OFF_UOBJECT_CLASS, A(cls));
            put32(o, Grimoire::OFF_UOBJECT_NAME, nameIdx);
        };
        uobj(spClassCls, spClassCls, 4);
        uobj(spSsCls, spClassCls, 3);
        uobj(spUdsCls, spClassCls, 10);
        putP(spUdsCls, DynOff::USTRUCT_SUPER, A(spSsCls));
        uobj(spVector, spSsCls, 5);
        uobj(spUds, spUdsCls, 11);
        uobj(spActorCls, spClassCls, 6);
        uobj(spDecoy, spActorCls, 7);

        static uint8_t spStructFC[0x20] = {}, spObjFC[0x20] = {};
        put32(spStructFC, DynOff::FFIELDCLASS_NAME, 1);
        put32(spObjFC, DynOff::FFIELDCLASS_NAME, 2);
        auto fprop = [&](uint8_t* p, const uint8_t* fc, int nameIdx, int32_t off, int32_t size, uint8_t* next,
                         uintptr_t slot) {
            putP(p, DynOff::FFIELD_CLASS, A(fc));
            put32(p, DynOff::FFIELD_NAME, nameIdx);
            put32(p, DynOff::FPROPERTY_OFFSET, off);
            put32(p, DynOff::FPROPERTY_ELEMSIZE, size);
            put32(p, DynOff::FPROPERTY_ELEMSIZE - 4, 1);
            putP(p, DynOff::FFIELD_NEXT, next ? A(next) : 0);
            putP(p, 0x78, slot);
        };
        static uint8_t spPos[0x100] = {}, spItem[0x100] = {}, spLoc[0x100] = {}, spTarget[0x100] = {},
                       spOwner[0x100] = {}, spCls[0x100] = {};
        fprop(spPos,    spStructFC, 13, 0x00, 0x0C, spItem,   A(spVector));   // FVector Pos
        fprop(spItem,   spStructFC, 12, 0x10, 0x08, spLoc,    A(spUds));      // S_Item Item (a Blueprint struct)
        fprop(spLoc,    spStructFC,  8, 0x18, 0x0C, spTarget, A(spDecoy));    // the slot holds a named non-struct
        fprop(spTarget, spObjFC,    14, 0x28, 0x08, spOwner,  A(spActorCls)); // AActor* Target
        fprop(spOwner,  spObjFC,     9, 0x30, 0x08, nullptr,  A(spDecoy));    // the slot holds a named non-class
        put32(spCls, DynOff::USTRUCT_PROPSSIZE, 0x38);
        putP(spCls, DynOff::USTRUCT_CHILDPROPS, A(spPos));

        const auto& spInfo = Ubel::WalkClassEx(A(spCls));
        auto fieldOf = [&](const char* n) -> const FieldInfo* {
            for (const auto& f : spInfo.Fields) if (f.Name == n) return &f;
            return nullptr;
        };
        const FieldInfo* fPos = fieldOf("Pos");
        const FieldInfo* fItem = fieldOf("Item");
        const FieldInfo* fLoc = fieldOf("Loc");
        const FieldInfo* fTarget = fieldOf("Target");
        const FieldInfo* fOwner = fieldOf("Owner");
        check("STRUCTPROBE setup: the five fields were walked", fPos && fItem && fLoc && fTarget && fOwner,
              std::to_string(spInfo.Fields.size()).c_str());
        if (fPos && fItem && fLoc && fTarget && fOwner) {
            check("STRUCTPROBE ⭐: a StructProperty whose slot holds a named NON-struct is not typed by that name",
                  fLoc->structType.empty(), fLoc->structType.c_str());
            check("STRUCTPROBE ⭐: an ObjectProperty whose slot holds a named NON-class is not typed by that name",
                  fOwner->objClassName.empty(), fOwner->objClassName.c_str());
            check("STRUCTPROBE control: a native struct is named", fPos->structType == "Vector", fPos->structType.c_str());
            check("STRUCTPROBE control: a Blueprint struct (UserDefinedStruct : ScriptStruct) is named",
                  fItem->structType == "S_Item", fItem->structType.c_str());
            check("STRUCTPROBE control: an object property's class is named", fTarget->objClassName == "Actor",
                  fTarget->objClassName.c_str());
        }

        // WalkInstance's probe: the decoy at the family slot, the real struct one pointer on. It must not stop at the
        // decoy -- it finds the struct at +8 (and, by design, moves the family there).
        static uint8_t spLoc2[0x100] = {}, spCls2[0x100] = {}, spInst[0x100] = {};
        fprop(spLoc2, spStructFC, 8, 0x00, 0x0C, nullptr, A(spDecoy));
        putP(spLoc2, 0x80, A(spVector));
        put32(spCls2, DynOff::USTRUCT_PROPSSIZE, 0x10);
        putP(spCls2, DynOff::USTRUCT_CHILDPROPS, A(spLoc2));
        putP(spInst, Grimoire::OFF_UOBJECT_CLASS, A(spCls2));
        const auto spWalk = Ubel::WalkInstance(A(spInst), A(spCls2), 64, 2, false);
        std::string spWalkType = "(no field)";
        for (const auto& fv : spWalk.fields) if (fv.name == "Loc") spWalkType = fv.structTypeName;
        check("STRUCTPROBE ⭐: WalkInstance's struct probe passes over a named non-struct to the struct beyond it",
              spWalkType == "Vector", spWalkType.c_str());

        DynOff::ApplyPropertyFamily(svFamily);
        DynOff::bUseFProperty = svFProp;
    }

    // -- ENUMSLOT-2026-09-28 -- an enum slot is accepted only when it holds a UEnum ------------------------------------------
    //
    // ⛔ OWN name table (after STRUCTPROBE). [ENUMSLOT-ANY-NAME] [STRUCTPROBE-ANY-NAME] gave the struct and class slots a
    // kind check; the ENUM slots kept the old one (review of builds 3594-3595): the enum-name readers accepted any
    // printable name -- and those names go into the SDK and USMAP exports -- the EnumProperty pointer readers kept any
    // pointer, and IsUEnumObject matched two exact class names, so a UEnum SUBCLASS was dropped where the struct and class
    // checks walk the chain.
    {
        blk("ENUMSLOT - an enum slot is accepted only when it holds a UEnum (or a UEnum subclass)");
        ResetCancel();
        const bool svFProp = DynOff::bUseFProperty;
        const DynOff::PropertyFamily svFamily{ DynOff::FSTRUCTPROP_STRUCT, DynOff::FARRAYPROP_INNER,
                                               DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBYTEPROP_ENUM, DynOff::FENUMPROP_ENUM };
        DynOff::bUseFProperty = true;
        DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x78));   // Byte's Enum at 0x78, Enum's at 0x80

        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto A     = [](const void* b) { return reinterpret_cast<uintptr_t>(b); };

        static uint8_t esEntry[16][0x40] = {};
        const char* esNames[16] = { "", "EnumProperty", "ByteProperty", "ArrayProperty", "Enum", "EColor", "Actor",
                                    "Decoy", "CustomEnum", "ECustom", "Mode", "Kind", "Tags", "Level", "Grade", "Tier" };
        static uintptr_t esChunk[17] = {};
        for (int i = 1; i <= 15; ++i) {
            memcpy(esEntry[i] + 0x10, esNames[i], strlen(esNames[i]) + 1);
            esChunk[i] = A(esEntry[i]);
        }
        static uintptr_t esChunks[2] = { A(esChunk), 0 };
        Serie::InitUE4(A(esChunks), 0x10);

        // The Enum metaclass, a UEnum SUBCLASS metaclass (super = Enum -- a UserDefinedEnum or a Verse enum has this
        // shape), one enum of each, the Actor class and a named Actor instance: the decoy.
        static uint8_t esEnumCls[0x100] = {}, esCustomCls[0x100] = {}, esColor[0x100] = {}, esCustom[0x100] = {},
                       esActorCls[0x100] = {}, esDecoy[0x100] = {};
        auto uobj = [&](uint8_t* o, const uint8_t* cls, int nameIdx) {
            putP(o, Grimoire::OFF_UOBJECT_CLASS, A(cls));
            put32(o, Grimoire::OFF_UOBJECT_NAME, nameIdx);
        };
        uobj(esEnumCls, esEnumCls, 4);
        uobj(esCustomCls, esEnumCls, 8);
        putP(esCustomCls, DynOff::USTRUCT_SUPER, A(esEnumCls));
        uobj(esColor, esEnumCls, 5);
        uobj(esCustom, esCustomCls, 9);
        uobj(esActorCls, esActorCls, 6);
        uobj(esDecoy, esActorCls, 7);

        static uint8_t esEnumFC[0x20] = {}, esByteFC[0x20] = {}, esArrayFC[0x20] = {};
        put32(esEnumFC, DynOff::FFIELDCLASS_NAME, 1);
        put32(esByteFC, DynOff::FFIELDCLASS_NAME, 2);
        put32(esArrayFC, DynOff::FFIELDCLASS_NAME, 3);
        auto fprop = [&](uint8_t* p, const uint8_t* fc, int nameIdx, int32_t off, int32_t size, uint8_t* next) {
            putP(p, DynOff::FFIELD_CLASS, A(fc));
            put32(p, DynOff::FFIELD_NAME, nameIdx);
            put32(p, DynOff::FPROPERTY_OFFSET, off);
            put32(p, DynOff::FPROPERTY_ELEMSIZE, size);
            put32(p, DynOff::FPROPERTY_ELEMSIZE - 4, 1);
            putP(p, DynOff::FFIELD_NEXT, next ? A(next) : 0);
        };
        static uint8_t esMode[0x100] = {}, esKind[0x100] = {}, esTags[0x100] = {}, esTagsInner[0x100] = {},
                       esLevel[0x100] = {}, esGrade[0x100] = {}, esTier[0x100] = {}, esCls[0x100] = {};
        fprop(esMode,  esEnumFC,  10, 0x00, 1, esKind);    putP(esMode, 0x80, A(esDecoy));     // EnumProperty -> decoy
        fprop(esKind,  esByteFC,  11, 0x01, 1, esTags);    putP(esKind, 0x78, A(esDecoy));     // ByteProperty -> decoy
        fprop(esTags,  esArrayFC, 12, 0x08, 16, esLevel);  putP(esTags, 0x78, A(esTagsInner)); // TArray<enum> Tags
        fprop(esTagsInner, esEnumFC, 12, 0x00, 1, nullptr); putP(esTagsInner, 0x80, A(esDecoy)); //   inner -> decoy
        fprop(esLevel, esEnumFC,  13, 0x18, 1, esGrade);   putP(esLevel, 0x80, A(esColor));    // EnumProperty EColor
        fprop(esGrade, esByteFC,  14, 0x19, 1, esTier);    putP(esGrade, 0x78, A(esCustom));   // TEnumAsByte<ECustom>
        fprop(esTier,  esEnumFC,  15, 0x1A, 1, nullptr);   putP(esTier, 0x80, A(esCustom));    // EnumProperty ECustom
        put32(esCls, DynOff::USTRUCT_PROPSSIZE, 0x20);
        putP(esCls, DynOff::USTRUCT_CHILDPROPS, A(esMode));

        const auto& esInfo = Ubel::WalkClassEx(A(esCls));
        auto fieldOf = [&](const char* n) -> const FieldInfo* {
            for (const auto& f : esInfo.Fields) if (f.Name == n) return &f;
            return nullptr;
        };
        const FieldInfo* fMode = fieldOf("Mode");
        const FieldInfo* fKind = fieldOf("Kind");
        const FieldInfo* fTags = fieldOf("Tags");
        const FieldInfo* fLevel = fieldOf("Level");
        const FieldInfo* fTier = fieldOf("Tier");
        check("ENUMSLOT setup: the fields were walked", fMode && fKind && fTags && fLevel && fTier,
              std::to_string(esInfo.Fields.size()).c_str());
        if (fMode && fKind && fTags && fLevel && fTier) {
            check("ENUMSLOT ⭐: an EnumProperty whose slot holds a named NON-enum is not named by it",
                  fMode->enumName.empty(), fMode->enumName.c_str());
            check("ENUMSLOT ⭐: ...nor a ByteProperty's", fKind->enumName.empty(), fKind->enumName.c_str());
            check("ENUMSLOT ⭐: ...nor an array inner's", fTags->innerEnumName.empty(), fTags->innerEnumName.c_str());
            check("ENUMSLOT control: a real UEnum is named", fLevel->enumName == "EColor", fLevel->enumName.c_str());
            check("ENUMSLOT control: ...and a UEnum subclass's enum too", fTier->enumName == "ECustom",
                  fTier->enumName.c_str());
        }

        const auto& esCached = Ubel::GetCachedStructFields(A(esCls));
        auto cachedEnum = [&](const char* n) -> uintptr_t {
            for (const auto& f : esCached) if (f.name == n) return f.enumAddr;
            return ~uintptr_t(0);
        };
        check("ENUMSLOT ⭐: the struct-field cache keeps no EnumProperty pointer that is not a UEnum",
              cachedEnum("Mode") == 0);
        check("ENUMSLOT ⭐: ...and does keep a ByteProperty's enum of a UEnum SUBCLASS (the exact-name check dropped it)",
              cachedEnum("Grade") == A(esCustom));
        check("ENUMSLOT control: a real EnumProperty enum is cached", cachedEnum("Level") == A(esColor));

        DynOff::ApplyPropertyFamily(svFamily);
        DynOff::bUseFProperty = svFProp;
    }

    // -- OPTSTRUCT-2026-09-28 -- the two struct-slot readers [STRUCTPROBE-ANY-NAME] did not reach -------------------------
    //
    // ⛔ OWN name table (after ENUMSLOT). [OPTSTRUCT-ANY-NAME] ResolveOptionalLayout's probe for a TOptional<FStruct>'s
    // UScriptStruct still took the first NAMED object -- and then read that object's "MinAlignment", so the optional's
    // layout (where bIsSet sits) came out wrong or Unknown -- and the struct-field cache named a nested struct by
    // whatever object its slot held (review of builds 3594-3595, LOW + INFO).
    {
        blk("OPTSTRUCT - the TOptional struct probe and the struct cache take only a struct");
        ResetCancel();
        const bool svFProp = DynOff::bUseFProperty;
        const DynOff::PropertyFamily svFamily{ DynOff::FSTRUCTPROP_STRUCT, DynOff::FARRAYPROP_INNER,
                                               DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBYTEPROP_ENUM, DynOff::FENUMPROP_ENUM };
        DynOff::bUseFProperty = true;
        DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x78));

        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto A     = [](const void* b) { return reinterpret_cast<uintptr_t>(b); };

        static uint8_t osEntry[10][0x40] = {};
        const char* osNames[10] = { "", "OptionalProperty", "StructProperty", "ScriptStruct", "Transform8", "Actor",
                                    "Decoy", "Opt", "Val", "Where" };
        static uintptr_t osChunk[11] = {};
        for (int i = 1; i <= 9; ++i) {
            memcpy(osEntry[i] + 0x10, osNames[i], strlen(osNames[i]) + 1);
            osChunk[i] = A(osEntry[i]);
        }
        static uintptr_t osChunks[2] = { A(osChunk), 0 };
        Serie::InitUE4(A(osChunks), 0x10);

        static uint8_t osSsCls[0x100] = {}, osStruct[0x100] = {}, osActorCls[0x100] = {}, osDecoy[0x100] = {};
        putP(osSsCls, Grimoire::OFF_UOBJECT_CLASS, A(osSsCls));       put32(osSsCls, Grimoire::OFF_UOBJECT_NAME, 3);
        putP(osStruct, Grimoire::OFF_UOBJECT_CLASS, A(osSsCls));      put32(osStruct, Grimoire::OFF_UOBJECT_NAME, 4);
        put32(osStruct, DynOff::USTRUCT_PROPSSIZE, 0x10);
        const int16_t osAlign8 = 8;
        memcpy(osStruct + DynOff::USTRUCT_PROPSSIZE + 4, &osAlign8, sizeof(osAlign8));   // UScriptStruct::MinAlignment
        putP(osActorCls, Grimoire::OFF_UOBJECT_CLASS, A(osActorCls)); put32(osActorCls, Grimoire::OFF_UOBJECT_NAME, 5);
        putP(osDecoy, Grimoire::OFF_UOBJECT_CLASS, A(osActorCls));    put32(osDecoy, Grimoire::OFF_UOBJECT_NAME, 6);

        static uint8_t osOptFC[0x20] = {}, osStructFC[0x20] = {};
        put32(osOptFC, DynOff::FFIELDCLASS_NAME, 1);
        put32(osStructFC, DynOff::FFIELDCLASS_NAME, 2);
        auto fprop = [&](uint8_t* p, const uint8_t* fc, int nameIdx, int32_t off, int32_t size, uint8_t* next) {
            putP(p, DynOff::FFIELD_CLASS, A(fc));
            put32(p, DynOff::FFIELD_NAME, nameIdx);
            put32(p, DynOff::FPROPERTY_OFFSET, off);
            put32(p, DynOff::FPROPERTY_ELEMSIZE, size);
            put32(p, DynOff::FPROPERTY_ELEMSIZE - 4, 1);
            putP(p, DynOff::FFIELD_NEXT, next ? A(next) : 0);
        };
        // TOptional<Transform8>: a 0x10-byte, 8-aligned struct -> the value at 0, bIsSet at 0x10, sizeof 0x18.
        static uint8_t osOpt[0x100] = {}, osVal[0x100] = {};
        fprop(osOpt, osOptFC, 7, 0x00, 0x18, nullptr);
        putP(osOpt, 0x78, A(osVal));                                   // ValueProperty (the Inner slot)
        fprop(osVal, osStructFC, 8, 0x00, 0x10, nullptr);
        putP(osVal, 0x78, A(osDecoy));                                 // the struct slot holds a named NON-struct...
        putP(osVal, 0x80, A(osStruct));                                // ...and the struct is one pointer on
        const auto osOl = Ubel::ResolveOptionalLayout(A(osOpt), 0x18, "");
        check("OPTSTRUCT setup: the optional's value property is the StructProperty", osOl.innerProp == A(osVal)
              && osOl.innerType == "StructProperty", osOl.innerType.c_str());
        check("OPTSTRUCT ⭐: the TOptional probe passes over a named non-struct to the struct's alignment",
              osOl.innerAlign == 8, std::to_string(osOl.innerAlign).c_str());
        check("OPTSTRUCT ⭐: ...so the optional is laid out as the trailing-flag TOptional it is",
              osOl.layout == Ubel::OptionalLayout::TrailingFlag, std::to_string(static_cast<int>(osOl.layout)).c_str());

        // The struct-field cache: a StructProperty whose slot holds the decoy names no nested struct.
        static uint8_t osWhere[0x100] = {}, osCls[0x100] = {};
        fprop(osWhere, osStructFC, 9, 0x00, 0x10, nullptr);
        putP(osWhere, 0x78, A(osDecoy));
        put32(osCls, DynOff::USTRUCT_PROPSSIZE, 0x10);
        putP(osCls, DynOff::USTRUCT_CHILDPROPS, A(osWhere));
        std::string osNested = "(no field)";
        for (const auto& f : Ubel::GetCachedStructFields(A(osCls))) if (f.name == "Where") osNested = f.nestedTypeName;
        check("OPTSTRUCT ⭐: the struct-field cache names no nested struct from a named non-struct",
              osNested.empty(), osNested.c_str());

        DynOff::ApplyPropertyFamily(svFamily);
        DynOff::bUseFProperty = svFProp;
    }

    // -- KINDNAMES-2026-09-28 -- the engine's own metaclasses pass the kind checks without SuperStruct -------------------
    //
    // ⛔ OWN name table (after OPTSTRUCT). [STRUCTPROBE-ANY-NAME] [ENUMSLOT-ANY-NAME] follow-up (review of build 3596,
    // INFO): the kind checks walked the metaclass's SuperStruct chain even for UserDefinedStruct, BlueprintGeneratedClass
    // and UserDefinedEnum, whose names already say what they are. A give-up session never measures USTRUCT_SUPER (its
    // 0x40 default is wrong on 4.11-4.21), so there every Blueprint struct, class and enum was dropped -- where the
    // name-only check before build 3595 kept them. Here the metaclasses have no SuperStruct at all.
    {
        blk("KINDNAMES - a Blueprint struct, class and enum pass by their metaclass's name, with no SuperStruct");
        ResetCancel();
        const bool svFProp = DynOff::bUseFProperty;
        const DynOff::PropertyFamily svFamily{ DynOff::FSTRUCTPROP_STRUCT, DynOff::FARRAYPROP_INNER,
                                               DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBYTEPROP_ENUM, DynOff::FENUMPROP_ENUM };
        DynOff::bUseFProperty = true;
        DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x78));

        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto A     = [](const void* b) { return reinterpret_cast<uintptr_t>(b); };

        static uint8_t knEntry[13][0x40] = {};
        const char* knNames[13] = { "", "StructProperty", "ObjectProperty", "ByteProperty", "UserDefinedStruct",
                                    "S_Item", "BlueprintGeneratedClass", "BP_Hero_C", "UserDefinedEnum", "E_Mood",
                                    "Item", "Hero", "Mood" };
        static uintptr_t knChunk[14] = {};
        for (int i = 1; i <= 12; ++i) {
            memcpy(knEntry[i] + 0x10, knNames[i], strlen(knNames[i]) + 1);
            knChunk[i] = A(knEntry[i]);
        }
        static uintptr_t knChunks[2] = { A(knChunk), 0 };
        Serie::InitUE4(A(knChunks), 0x10);

        // Metaclasses named as the engine names them, with NO SuperStruct; one object of each.
        static uint8_t knUdsMeta[0x100] = {}, knBpgcMeta[0x100] = {}, knUdeMeta[0x100] = {},
                       knUds[0x100] = {}, knBp[0x100] = {}, knUde[0x100] = {};
        put32(knUdsMeta, Grimoire::OFF_UOBJECT_NAME, 4);
        put32(knBpgcMeta, Grimoire::OFF_UOBJECT_NAME, 6);
        put32(knUdeMeta, Grimoire::OFF_UOBJECT_NAME, 8);
        putP(knUds, Grimoire::OFF_UOBJECT_CLASS, A(knUdsMeta));   put32(knUds, Grimoire::OFF_UOBJECT_NAME, 5);
        putP(knBp, Grimoire::OFF_UOBJECT_CLASS, A(knBpgcMeta));   put32(knBp, Grimoire::OFF_UOBJECT_NAME, 7);
        putP(knUde, Grimoire::OFF_UOBJECT_CLASS, A(knUdeMeta));   put32(knUde, Grimoire::OFF_UOBJECT_NAME, 9);

        static uint8_t knStructFC[0x20] = {}, knObjFC[0x20] = {}, knByteFC[0x20] = {};
        put32(knStructFC, DynOff::FFIELDCLASS_NAME, 1);
        put32(knObjFC, DynOff::FFIELDCLASS_NAME, 2);
        put32(knByteFC, DynOff::FFIELDCLASS_NAME, 3);
        auto fprop = [&](uint8_t* p, const uint8_t* fc, int nameIdx, int32_t off, int32_t size, uint8_t* next,
                         uintptr_t slot) {
            putP(p, DynOff::FFIELD_CLASS, A(fc));
            put32(p, DynOff::FFIELD_NAME, nameIdx);
            put32(p, DynOff::FPROPERTY_OFFSET, off);
            put32(p, DynOff::FPROPERTY_ELEMSIZE, size);
            put32(p, DynOff::FPROPERTY_ELEMSIZE - 4, 1);
            putP(p, DynOff::FFIELD_NEXT, next ? A(next) : 0);
            putP(p, 0x78, slot);
        };
        static uint8_t knItem[0x100] = {}, knHero[0x100] = {}, knMood[0x100] = {}, knCls[0x100] = {};
        fprop(knItem, knStructFC, 10, 0x00, 0x10, knHero, A(knUds));
        fprop(knHero, knObjFC,    11, 0x10, 0x08, knMood, A(knBp));
        fprop(knMood, knByteFC,   12, 0x18, 0x01, nullptr, A(knUde));
        put32(knCls, DynOff::USTRUCT_PROPSSIZE, 0x20);
        putP(knCls, DynOff::USTRUCT_CHILDPROPS, A(knItem));

        const auto& knInfo = Ubel::WalkClassEx(A(knCls));
        auto fieldOf = [&](const char* n) -> const FieldInfo* {
            for (const auto& f : knInfo.Fields) if (f.Name == n) return &f;
            return nullptr;
        };
        const FieldInfo* fItem = fieldOf("Item");
        const FieldInfo* fHero = fieldOf("Hero");
        const FieldInfo* fMood = fieldOf("Mood");
        check("KINDNAMES setup: the three fields were walked", fItem && fHero && fMood,
              std::to_string(knInfo.Fields.size()).c_str());
        if (fItem && fHero && fMood) {
            check("KINDNAMES ⭐: a UserDefinedStruct is a struct without its SuperStruct", fItem->structType == "S_Item",
                  fItem->structType.c_str());
            check("KINDNAMES ⭐: a BlueprintGeneratedClass is a class without its SuperStruct",
                  fHero->objClassName == "BP_Hero_C", fHero->objClassName.c_str());
            check("KINDNAMES ⭐: a UserDefinedEnum is an enum without its SuperStruct", fMood->enumName == "E_Mood",
                  fMood->enumName.c_str());
        }

        DynOff::ApplyPropertyFamily(svFamily);
        DynOff::bUseFProperty = svFProp;
    }

    // -- PROBEOVERRUN-2026-09-28 -- the C-ABI subclass getters read no further than a proven slot ---------------------
    //
    // ⛔ OWN name table (after KINDNAMES). [FRIEREN-PROBE-OVERRUN] ProbeSubclassSlot tried eight slots around
    // FSTRUCTPROP_STRUCT (0, -8, +8, -16, +16 and three misaligned) and returned the first object of the wanted kind. On
    // EVERSPACE 2 the PropertyClass getter, asked about a Blueprint UberGraphFrame StructProperty (0x78 bytes), returned
    // the component class at +0x80 -- the NEXT heap block -- and a null PropertyClass would have done the same. A slot a
    // real struct was read out of now gives the final answer; an unproven one is tried one aligned pointer either side
    // and no further (the UE423 0x78 -> 0x70 and DQ XI S 0x78 -> 0x80 moves).
    {
        blk("PROBEOVERRUN - the subclass getters read no further than a proven slot");
        ResetCancel();
        const bool svFProp = DynOff::bUseFProperty;
        const DynOff::PropertyFamily svFamily{ DynOff::FSTRUCTPROP_STRUCT, DynOff::FARRAYPROP_INNER,
                                               DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBYTEPROP_ENUM, DynOff::FENUMPROP_ENUM };
        const bool svCalibrated = Ubel::s_subclassCalibrated.load();
        const uint64_t svConfirmed = Ubel::s_subclassSlotConfirmed.load();
        DynOff::bUseFProperty = true;
        DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x78));

        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto A     = [](const void* b) { return reinterpret_cast<uintptr_t>(b); };

        static uint8_t poEntry[9][0x40] = {};
        const char* poNames[9] = { "", "Class", "ScriptStruct", "Vector", "Actor", "StaticMeshComponent",
                                   "StructProperty", "Pos", "Loc" };
        static uintptr_t poChunk[10] = {};
        for (int i = 1; i <= 8; ++i) {
            memcpy(poEntry[i] + 0x10, poNames[i], strlen(poNames[i]) + 1);
            poChunk[i] = A(poEntry[i]);
        }
        static uintptr_t poChunks[2] = { A(poChunk), 0 };
        Serie::InitUE4(A(poChunks), 0x10);

        // UClass (its own class), the ScriptStruct metaclass, a struct, the property's class and the stranger -- the
        // class that sits in whatever memory follows a property.
        static uint8_t poClassCls[0x100] = {}, poSsCls[0x100] = {}, poVector[0x100] = {}, poActor[0x100] = {},
                       poStranger[0x100] = {};
        auto uobj = [&](uint8_t* o, const uint8_t* cls, int nameIdx) {
            putP(o, Grimoire::OFF_UOBJECT_CLASS, A(cls));
            put32(o, Grimoire::OFF_UOBJECT_NAME, nameIdx);
        };
        uobj(poClassCls, poClassCls, 1);
        uobj(poSsCls, poClassCls, 2);
        uobj(poVector, poSsCls, 3);
        uobj(poActor, poClassCls, 4);
        uobj(poStranger, poClassCls, 5);

        auto S  = [&](const uint8_t* f) { return Ubel::ProbeSubclassSlot(A(f), Ubel::IsScriptStructObject); };
        auto C  = [&](const uint8_t* f) { return Ubel::ProbeSubclassSlot(A(f), Ubel::IsClassObject); };
        auto nm = [&](uintptr_t o) { return o ? Ubel::GetName(o) : std::string("(0)"); };

        // Property bodies: only the slot (0x78) and what surrounds it matter to the probe.
        static uint8_t poStructEnd[0x100] = {}, poNullNext[0x100] = {}, poMisaligned[0x100] = {}, poFar[0x100] = {},
                       poObj[0x100] = {}, poShiftDown[0x100] = {}, poMisP4[0x100] = {}, poMisM4[0x100] = {};
        putP(poStructEnd, 0x78, A(poVector));
        putP(poStructEnd, 0x88, A(poStranger));   // EVERSPACE 2's UberGraphFrame: a class 16 bytes past the struct
        putP(poNullNext, 0x80, A(poStranger));    // a null slot, a class one pointer on
        putP(poMisaligned, 0x84, A(poStranger));  // only a misaligned read reaches it: +12, +4 and -4
        putP(poMisP4, 0x7C, A(poStranger));
        putP(poMisM4, 0x74, A(poStranger));
        const uint8_t* poMis[3] = { poMisaligned, poMisP4, poMisM4 };
        auto noMisaligned = [&]() {
            std::string got;
            for (const uint8_t* m : poMis) if (C(m)) got += nm(C(m)) + " ";
            return got;
        };
        putP(poFar, 0x88, A(poStranger));         // a null slot, a class two pointers on
        putP(poObj, 0x78, A(poActor));
        putP(poShiftDown, 0x70, A(poVector));     // the family one pointer too high

        Ubel::MarkSubclassSlotConfirmed(DynOff::g_propertyFamilyEpoch.load());
        check("PROBEOVERRUN ⭐: proven slot -- a StructProperty has no PropertyClass, not the class past its end",
              C(poStructEnd) == 0, nm(C(poStructEnd)).c_str());
        check("PROBEOVERRUN ⭐: proven slot -- a null PropertyClass stays null, not the class one pointer on",
              C(poNullNext) == 0, nm(C(poNullNext)).c_str());
        check("PROBEOVERRUN ⭐: proven slot -- no misaligned read (+12, +4, -4)", noMisaligned().empty(),
              noMisaligned().c_str());
        check("PROBEOVERRUN control: proven slot -- the struct in it", S(poStructEnd) == A(poVector),
              nm(S(poStructEnd)).c_str());
        check("PROBEOVERRUN control: proven slot -- the class in it", C(poObj) == A(poActor), nm(C(poObj)).c_str());

        Ubel::s_subclassSlotConfirmed.store(0);
        check("PROBEOVERRUN control: unproven slot -- the struct one pointer below (a family one slot too high)",
              S(poShiftDown) == A(poVector), nm(S(poShiftDown)).c_str());
        check("PROBEOVERRUN control: unproven slot -- the class one pointer above (a family one slot too low)",
              C(poNullNext) == A(poStranger), nm(C(poNullNext)).c_str());
        check("PROBEOVERRUN ⭐: unproven slot -- nothing two pointers away", C(poFar) == 0, nm(C(poFar)).c_str());
        check("PROBEOVERRUN ⭐: unproven slot -- no misaligned read (+12, +4, -4)", noMisaligned().empty(),
              noMisaligned().c_str());

        Ubel::MarkSubclassSlotConfirmed(DynOff::g_propertyFamilyEpoch.load());
        DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x80));
        DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(0x78));
        check("PROBEOVERRUN control: a family move withdraws the proof -- the neighbour is tried again",
              C(poNullNext) == A(poStranger), nm(C(poNullNext)).c_str());

        // The two proofs. CorrectSubclassOffsets: a real StructProperty whose struct sits in the slot.
        static uint8_t poStructFC[0x20] = {};
        put32(poStructFC, DynOff::FFIELDCLASS_NAME, 6);
        auto fprop = [&](uint8_t* pr, int nameIdx, uintptr_t slot) {
            putP(pr, DynOff::FFIELD_CLASS, A(poStructFC));
            put32(pr, DynOff::FFIELD_NAME, nameIdx);
            put32(pr, DynOff::FPROPERTY_OFFSET, 0);
            put32(pr, DynOff::FPROPERTY_ELEMSIZE, 0x0C);
            put32(pr, DynOff::FPROPERTY_ELEMSIZE - 4, 1);
            putP(pr, 0x78, slot);
        };
        static uint8_t poPos[0x100] = {}, poCls[0x100] = {};
        fprop(poPos, 7, A(poVector));
        put32(poCls, DynOff::USTRUCT_PROPSSIZE, 0x10);
        putP(poCls, DynOff::USTRUCT_CHILDPROPS, A(poPos));
        Ubel::s_subclassSlotConfirmed.store(0);
        Ubel::s_subclassCalibrated.store(false);
        (void)Ubel::WalkClassEx(A(poCls));
        check("PROBEOVERRUN ⭐: CorrectSubclassOffsets' struct in the slot is the proof", C(poNullNext) == 0,
              nm(C(poNullNext)).c_str());

        // WalkInstance finding the struct IN the slot proves it too -- on a family Genau derived right, the usual case
        // since [UPROP-SUBCLASS-SLOT] and [FPROP-FAMILY-ALIGN], nothing ever needs correcting. The latch stays set, so
        // CorrectSubclassOffsets cannot be the one proving it.
        static uint8_t poAt[0x100] = {}, poClsAt[0x100] = {}, poInstAt[0x100] = {};
        fprop(poAt, 7, A(poVector));
        put32(poClsAt, DynOff::USTRUCT_PROPSSIZE, 0x10);
        putP(poClsAt, DynOff::USTRUCT_CHILDPROPS, A(poAt));
        putP(poInstAt, Grimoire::OFF_UOBJECT_CLASS, A(poClsAt));
        Ubel::s_subclassSlotConfirmed.store(0);
        (void)Ubel::WalkInstance(A(poInstAt), A(poClsAt), 64, 2, false);
        check("PROBEOVERRUN ⭐: WalkInstance's struct in the slot is the proof", C(poNullNext) == 0,
              nm(C(poNullNext)).c_str());

        // UProperty engines: Genau measures the family from the layout, and CorrectSubclassOffsets used to latch there
        // without looking -- no UE4 session could ever be proven. It checks the slot now, and never moves a UProperty
        // family. Called directly: a UProperty class is not walked through the FProperty chain these fixtures build.
        DynOff::bUseFProperty = false;
        FieldInfo poFi{};
        poFi.Address = A(poPos);
        poFi.Name = "Pos";
        poFi.TypeName = "StructProperty";
        Ubel::s_subclassSlotConfirmed.store(0);
        Ubel::s_subclassCalibrated.store(false);
        Ubel::CorrectSubclassOffsets({ poFi });
        check("PROBEOVERRUN ⭐: UProperty -- a struct in the slot proves it", C(poNullNext) == 0, nm(C(poNullNext)).c_str());
        static uint8_t poPosOff[0x100] = {};
        fprop(poPosOff, 7, 0);
        putP(poPosOff, 0x80, A(poVector));
        poFi.Address = A(poPosOff);
        Ubel::s_subclassSlotConfirmed.store(0);
        Ubel::s_subclassCalibrated.store(false);
        Ubel::CorrectSubclassOffsets({ poFi });
        check("PROBEOVERRUN ⭐: UProperty -- a struct one pointer on neither moves the family nor latches",
              DynOff::FSTRUCTPROP_STRUCT == 0x78 && !Ubel::s_subclassCalibrated.load() && C(poNullNext) == A(poStranger),
              (std::to_string(DynOff::FSTRUCTPROP_STRUCT) + " latched=" + std::to_string(Ubel::s_subclassCalibrated.load())
               + " " + nm(C(poNullNext))).c_str());
        Ubel::s_subclassCalibrated.store(true);
        DynOff::bUseFProperty = true;

        // WalkInstance's persisted correction: the struct one pointer on moves the family there, and proves it. The
        // calibration latch stays set, so only WalkInstance's own probe can move it.
        static uint8_t poLoc[0x100] = {}, poCls2[0x100] = {}, poInst[0x100] = {};
        fprop(poLoc, 8, 0);
        putP(poLoc, 0x80, A(poVector));
        put32(poCls2, DynOff::USTRUCT_PROPSSIZE, 0x10);
        putP(poCls2, DynOff::USTRUCT_CHILDPROPS, A(poLoc));
        putP(poInst, Grimoire::OFF_UOBJECT_CLASS, A(poCls2));
        Ubel::s_subclassSlotConfirmed.store(0);
        (void)Ubel::WalkInstance(A(poInst), A(poCls2), 64, 2, false);
        check("PROBEOVERRUN setup: WalkInstance moved the family to the struct", DynOff::FSTRUCTPROP_STRUCT == 0x80,
              std::to_string(DynOff::FSTRUCTPROP_STRUCT).c_str());
        check("PROBEOVERRUN ⭐: WalkInstance's corrected slot is the proof -- a null slot there stays null",
              C(poFar) == 0, nm(C(poFar)).c_str());

        // A proof never goes back: evidence read under an older epoch, filed late, must not withdraw a newer proof.
        const uint32_t poNow = DynOff::g_propertyFamilyEpoch.load();
        Ubel::s_subclassSlotConfirmed.store(0);
        Ubel::MarkSubclassSlotConfirmed(poNow);
        Ubel::MarkSubclassSlotConfirmed(poNow - 1);
        check("PROBEOVERRUN: a proof filed late under an older epoch leaves the newer one standing",
              Ubel::IsSubclassSlotConfirmed(), std::to_string(Ubel::s_subclassSlotConfirmed.load()).c_str());

        DynOff::ApplyPropertyFamily(svFamily);
        DynOff::bUseFProperty = svFProp;
        Ubel::s_subclassCalibrated.store(svCalibrated);
        Ubel::s_subclassSlotConfirmed.store(svConfirmed);
    }

    {
        blk("LIVEFUNCS-HIDE-PERFRAME: Linie::IsPerFrame -- the frame band, sustained over the recording");
        // A 10 s recording from t=1000 ms. Unless a case says otherwise the function fired steadily, so the time it
        // kept firing is its span.
        const uint64_t W = 10000;
        auto fsa = [](uint64_t count, double meanMs, uint64_t gaps, uint64_t firstMs, uint64_t lastMs, uint64_t activeMs) {
            return Linie::FuncStat{ 0x1000, count, 1, meanMs, 0.05, gaps, firstMs, lastMs, activeMs };
        };
        auto fs = [&](uint64_t count, double meanMs, uint64_t gaps, uint64_t firstMs, uint64_t lastMs) {
            return fsa(count, meanMs, gaps, firstMs, lastMs, lastMs - firstMs);
        };
        check("every frame at 60 fps for the whole recording is per-frame",
              Linie::IsPerFrame(fs(600, 16.7, 599, 1000, 10990), W));
        check("twice per frame is per-frame", Linie::IsPerFrame(fs(1200, 8.3, 1199, 1000, 10995), W));
        check("a frame rate that wanders (mean 20 ms) is per-frame", Linie::IsPerFrame(fs(500, 20.0, 499, 1000, 10980), W));
        check("on the band's edge (40 ms) and over half the recording, it is per-frame",
              Linie::IsPerFrame(fs(126, 40.0, 125, 1000, 6000), W));
        check("every frame from 40% on (60% of the recording) is per-frame",
              Linie::IsPerFrame(fs(360, 16.7, 359, 5000, 10990), W));
        check("an action's burst (10 fires in 50 ms) is NOT per-frame: its gaps are short, its span is not",
              !Linie::IsPerFrame(fs(10, 5.5, 9, 6000, 6050), W));
        check("every frame for only the last 30% is NOT per-frame (an effect the action started)",
              !Linie::IsPerFrame(fs(180, 16.7, 179, 8000, 10990), W));
        check("just past the band (40.1 ms) is NOT per-frame", !Linie::IsPerFrame(fs(250, 40.1, 249, 1000, 10990), W));
        check("a 0.5 s timer is NOT per-frame", !Linie::IsPerFrame(fs(20, 500.0, 19, 1000, 10500), W));
        check("two gaps prove nothing: NOT per-frame", !Linie::IsPerFrame(fs(3, 16.7, 2, 1000, 1033), W));
        check("two gaps prove nothing even when they span a 60 ms recording (only the gap count decides here)",
              !Linie::IsPerFrame(fs(3, 16.7, 2, 0, 33), 60));
        check("...while a third gap in that recording makes it per-frame (the control)",
              Linie::IsPerFrame(fs(4, 16.7, 3, 0, 50), 60));
        check("one fire: NOT per-frame", !Linie::IsPerFrame(fs(1, 0.0, 0, 5000, 5000), W));
        check("an empty recording window: nothing is per-frame", !Linie::IsPerFrame(fs(600, 16.7, 599, 1000, 10990), 0));
        check("a span just under half the recording is NOT per-frame",
              !Linie::IsPerFrame(fs(299, 16.7, 298, 1000, 5999), W));
        // Review of the first version: the span from the first fire to the last is not how long a function kept
        // firing. Each of these spans more than half the recording with a frame-band mean, and fired for far less.
        check("the action done twice (1 s of frames at t=1 s and t=7 s) is NOT per-frame",
              !Linie::IsPerFrame(fsa(240, 29.3, 239, 1000, 7992, 1990), W));
        check("four 1 s repetitions spread over the recording are NOT per-frame",
              !Linie::IsPerFrame(fsa(240, 35.5, 239, 1000, 9480, 3960), W));
        check("one broadcast to 80 instances in one frame, twice 6 s apart, is NOT per-frame",
              !Linie::IsPerFrame(fsa(160, 37.7, 159, 2000, 8000, 0), W));
        check("a Tick through a 1 s loading hitch is still per-frame (the control)",
              Linie::IsPerFrame(fsa(540, 18.5, 539, 1000, 10990, 8990), W));

        // The same through the table itself: RecordCall accumulates activeMs and Snapshot measures the window over
        // what was recorded. A: a Tick every 16 ms for 10 s. B: an action at 120 fps for 1 s, done twice 6 s apart.
        // C: gaps of exactly kActiveGapMaxMs (counted) and one more (not).
        Linie::Reset();
        Linie::StartRecording();
        for (uint64_t t = 0; t <= 9984; t += 16) Linie::RecordCall(0xA, 1000 + t);
        for (uint64_t t = 0; t <= 1000; t += 8) Linie::RecordCall(0xB, 2000 + t);   // in time order: RecordCall drops
        for (uint64_t t = 0; t <= 1000; t += 8) Linie::RecordCall(0xB, 8000 + t);   // a fire older than the last
        Linie::RecordCall(0xC, 5000); Linie::RecordCall(0xC, 5100); Linie::RecordCall(0xC, 5201);
        Linie::StopRecording();
        std::vector<Linie::FuncStat> lsnap;
        uint64_t lwin = 0;
        Linie::Snapshot(lsnap, lwin);
        auto stat = [&](uintptr_t f) {
            for (const auto& x : lsnap) if (x.func == f) return x;
            return Linie::FuncStat{};
        };
        const auto a = stat(0xA), b = stat(0xB), c = stat(0xC);
        check("Snapshot's window runs from the earliest fire to the latest", lwin == 9984, std::to_string(lwin).c_str());
        check("a steady Tick kept firing for its whole span", a.activeMs == 9984, std::to_string(a.activeMs).c_str());
        check("an action done twice kept firing for 2 s, not the 7 s between its first and last fire",
              b.activeMs == 2000 && b.lastMs - b.firstMs == 7000, std::to_string(b.activeMs).c_str());
        check("a gap of exactly kActiveGapMaxMs counts, one more does not", c.activeMs == 100, std::to_string(c.activeMs).c_str());
        check("through the table: the Tick is per-frame", Linie::IsPerFrame(a, lwin));
        check("through the table: the action done twice is NOT per-frame", !Linie::IsPerFrame(b, lwin));
        Linie::Reset();
        Linie::Snapshot(lsnap, lwin);
        check("an empty table has no window", lsnap.empty() && lwin == 0, std::to_string(lwin).c_str());

        // T5 (b) reads the previous recording's per-frame functions before StartRecording clears the table.
        Linie::StartRecording();
        for (uint64_t t = 0; t <= 9984; t += 16) Linie::RecordCall(0xA, 1000 + t);
        Linie::RecordCall(0xB, 5000); Linie::RecordCall(0xB, 5008);
        Linie::StopRecording();
        const auto pf = Linie::PerFrameFuncs();
        check("PerFrameFuncs: the Tick, not the action", pf.size() == 1 && pf[0] == 0xA, std::to_string(pf.size()).c_str());
        Linie::Reset();
        check("PerFrameFuncs of an empty table is empty", Linie::PerFrameFuncs().empty());
    }

    {
        blk("LIVEFUNCS-TIMELINE: Linie's call trace -- the ring, the ticked scope, what Stop leaves behind");
        // A clock that counts, so the ticks of every record are known.
        static uint64_t s_fakeTicks = 0;
        Linie::SetTraceClockForTest([]() -> uint64_t { return s_fakeTicks += 10; });
        auto cfg = [](uint64_t records, std::vector<uintptr_t> ticked = {}, std::vector<uintptr_t> exclude = {}) {
            Linie::TraceConfig c;
            c.bytes   = records * sizeof(Linie::TraceRecord);
            c.ticked  = std::move(ticked);
            c.exclude = std::move(exclude);
            return c;
        };
        std::vector<Linie::TraceRecord> recs;
        auto copyAll = [&recs]() { recs.clear(); return Linie::CopyTrace(0, SIZE_MAX, recs); };
        auto sz = [](size_t n) { return std::to_string(n); };

        Linie::AddrSet set;
        set.Build({ 0x1000, 0x2000, 0x3000, 0 });
        check("AddrSet holds what it was built from", set.Contains(0x1000) && set.Contains(0x2000) && set.Contains(0x3000));
        check("...and nothing else", !set.Contains(0x1800) && !set.Contains(0x4000));
        check("...and never 0, the empty slot, even when 0 was passed in", !set.Contains(0) && set.Size() == 3,
              sz(set.Size()).c_str());
        std::vector<uintptr_t> many;
        for (uintptr_t i = 1; i <= 5000; ++i) many.push_back(i * 0x40);
        Linie::AddrSet big;
        big.Build(many);
        bool allIn = true;
        for (uintptr_t x : many) allIn = allIn && big.Contains(x);
        check("a set of 5000 holds every one of them", allIn && big.Size() == 5000, sz(big.Size()).c_str());
        check("...and not an address between two of them", !big.Contains(0x40 * 2500 + 8));
        Linie::AddrSet none;
        none.Build({});
        check("an empty set holds nothing", none.Empty() && !none.Contains(0x1000));
        Linie::AddrSet dup;
        dup.Build({ 0x1000, 0x1000 });
        check("a duplicate counts once", dup.Size() == 1 && dup.Contains(0x1000), sz(dup.Size()).c_str());

        auto b64 = [](const char* s) { return Linie::Base64Encode(reinterpret_cast<const uint8_t*>(s), strlen(s)); };
        check("base64: RFC 4648's test vectors",
              b64("") == "" && b64("f") == "Zg==" && b64("fo") == "Zm8=" && b64("foo") == "Zm9v" &&
              b64("foob") == "Zm9vYg==" && b64("fooba") == "Zm9vYmE=" && b64("foobar") == "Zm9vYmFy",
              b64("foobar").c_str());
        const uint8_t high[] = { 0xFF, 0xFE, 0x00 };
        check("base64: bytes above 0x7F and a zero byte", Linie::Base64Encode(high, 3) == "//4A",
              Linie::Base64Encode(high, 3).c_str());

        Linie::FreeTrace();
        check("a ring of one record is refused", Linie::StartTrace(cfg(1)) == Linie::TraceStartStatus::TooSmall &&
                                                 !Linie::IsTracing());

        // Two nested calls on one thread, nothing ticked: four records, the returns pointing at their entries.
        check("a ring of 8 records starts", Linie::StartTrace(cfg(8)) == Linie::TraceStartStatus::Ok && Linie::IsTracing());
        Linie::TraceToken t1, t2;
        Linie::TraceEnter(0xF1, 0xB1, 1000, 7, t1);
        Linie::TraceEnter(0xF2, 0xB2, 900, 7, t2);
        Linie::TraceReturn(t2, 7);
        Linie::TraceReturn(t1, 7);
        check("both calls traced, neither opened a scope (nothing is ticked)",
              t1.traced && t2.traced && !t1.opened && !t2.opened && t2.entrySeq == 1);
        Linie::StopTrace();
        Linie::TraceInfo info = Linie::GetTraceInfo();
        check("after Stop: 4 records written and all 4 kept",
              info.allocated && !info.tracing && info.quiesced && info.written == 4 && info.firstValid == 0 &&
              info.capacity == 8 && info.qpcFreq > 0, sz(info.written).c_str());
        check("...and they copy out", copyAll() && recs.size() == 4, sz(recs.size()).c_str());
        if (recs.size() == 4) {
            check("entry 1: the function, the object, the thread, sequence 0",
                  recs[0].seqKind == 0 && recs[0].a == 0xF1 && recs[0].b == 0xB1 && recs[0].tid == 7 && recs[0].flags == 0);
            check("entry 2, nested: sequence 1", recs[1].seqKind == 1 && recs[1].a == 0xF2 && recs[1].b == 0xB2);
            check("the inner return points at entry 2",
                  recs[2].seqKind == (2 | Linie::kTraceReturnBit) && recs[2].a == 1 && recs[2].b == 0 && recs[2].tid == 7);
            check("the outer return points at entry 1", recs[3].seqKind == (3 | Linie::kTraceReturnBit) && recs[3].a == 0);
            check("one clock read per record, in order",
                  recs[0].ticks < recs[1].ticks && recs[1].ticks < recs[2].ticks && recs[2].ticks < recs[3].ticks);
        }
        Linie::TraceToken late;
        Linie::TraceEnter(0xF3, 0, 800, 7, late);
        check("after Stop a call is not traced", !late.traced && Linie::GetTraceInfo().written == 4);

        // A call that entered before Stop and returns after it: no record lands in the stopped ring.
        Linie::StartTrace(cfg(8));
        Linie::TraceToken r1;
        Linie::TraceEnter(0xF1, 0, 1000, 1, r1);
        std::vector<Linie::TraceRecord> during;
        check("the ring is not copied while the trace runs", !Linie::CopyTrace(0, 10, during));
        Linie::StopTrace();
        Linie::TraceReturn(r1, 1);
        check("a call that returns after Stop adds no record", r1.traced && Linie::GetTraceInfo().written == 1,
              sz(Linie::GetTraceInfo().written).c_str());

        // The ring keeps the last records: 6 into 4 slots keeps [2, 6).
        Linie::StartTrace(cfg(4));
        for (uintptr_t i = 0; i < 6; ++i) { Linie::TraceToken t; Linie::TraceEnter(0x100 + i, 0, 1000, 1, t); }
        Linie::StopTrace();
        info = Linie::GetTraceInfo();
        check("the ring keeps the LAST 4 of 6: [2, 6)", info.written == 6 && info.firstValid == 2,
              sz(info.firstValid).c_str());
        check("...and copies them in order", copyAll() && recs.size() == 4 && recs[0].seqKind == 2 &&
                                            recs[0].a == 0x102 && recs[3].seqKind == 5 && recs[3].a == 0x105);
        uint64_t nextSeq = 0;
        recs.clear();
        check("a copy from inside the window starts there, and the next page starts at the end",
              Linie::CopyTrace(4, 10, recs, &nextSeq) && recs.size() == 2 && recs[0].seqKind == 4 && nextSeq == 6,
              sz(nextSeq).c_str());
        recs.clear();
        check("a copy that begins before the window is clipped to it, and the next page follows it",
              Linie::CopyTrace(0, 3, recs, &nextSeq) && recs.size() == 1 && recs[0].seqKind == 2 && nextSeq == 3,
              sz(nextSeq).c_str());
        recs.clear();
        check("a page wholly before the window moves the next page to the window",
              Linie::CopyTrace(0, 1, recs, &nextSeq) && recs.empty() && nextSeq == 2, sz(nextSeq).c_str());
        recs.clear();
        check("a copy past the end is empty, not a failure, and does not go back",
              Linie::CopyTrace(6, 10, recs, &nextSeq) && recs.empty() && nextSeq == 6);

        // T5 (a): with a function ticked, only its calls and what they call are traced, on its own thread.
        Linie::StartTrace(cfg(64, { 0xA7 }));
        Linie::TraceToken s0, s1, s2, s3, s4;
        Linie::TraceEnter(0xF1, 0, 1000, 1, s0);   // outside any ticked call
        Linie::TraceEnter(0xA7, 0, 900, 1, s1);    // the ticked function opens the scope
        Linie::TraceEnter(0xF2, 0, 800, 1, s2);    // called inside it
        Linie::TraceReturn(s2, 1);
        Linie::TraceReturn(s1, 1);                 // closes it
        Linie::TraceEnter(0xF3, 0, 900, 1, s3);    // after it, at the same depth
        Linie::TraceEnter(0xF4, 0, 800, 2, s4);    // another thread
        check("scope: a call outside the ticked function is not traced", !s0.traced);
        check("scope: the ticked call opens it", s1.traced && s1.opened);
        check("scope: a call inside it is traced and opens nothing", s2.traced && !s2.opened);
        check("scope: once the ticked call returned, the next call is not traced", !s3.traced);
        Linie::StopTrace();
        check("scope: the ticked call's entry carries the root flag, the inner one does not",
              copyAll() && recs.size() == 4 && recs[0].a == 0xA7 && recs[0].flags == Linie::kTraceScopeRoot &&
              recs[1].flags == 0, sz(recs.size()).c_str());

        // The other thread above ran on this OS thread too (the tid is only a label), so give the cross-thread case
        // a real second thread: a scope opened here is not open there.
        Linie::StartTrace(cfg(64, { 0xA7 }));
        Linie::TraceToken o1;
        Linie::TraceEnter(0xA7, 0, 900, 1, o1);
        bool otherTraced = true;
        std::thread([&otherTraced] {
            Linie::TraceToken o2;
            Linie::TraceEnter(0xF2, 0, 800, 2, o2);
            otherTraced = o2.traced;
        }).join();
        check("scope: another thread is not in this thread's scope", o1.opened && !otherTraced);
        Linie::TraceReturn(o1, 1);
        Linie::StopTrace();

        // An exception unwinds the ticked call: no return closes the scope, the next call from higher up does.
        Linie::StartTrace(cfg(64, { 0xA7 }));
        Linie::TraceToken u1, u2, u3, u4, u5, u6;
        Linie::TraceEnter(0xA7, 0, 900, 1, u1);
        Linie::TraceEnter(0xF2, 0, 800, 1, u2);
        Linie::TraceEnter(0xF5, 0, 950, 1, u3);    // above the root's frame: the root is gone
        Linie::TraceEnter(0xF6, 0, 850, 1, u4);    // deeper again, but the scope is closed
        check("unwound: a call from above the ticked call's frame closes its scope",
              u1.opened && u2.traced && !u3.traced && !u4.traced);
        Linie::TraceEnter(0xA7, 0, 900, 1, u5);
        Linie::TraceEnter(0xA7, 0, 800, 1, u6);    // the ticked function called inside itself
        check("a ticked call inside its own scope is traced, not a new root", u5.opened && u6.traced && !u6.opened);
        Linie::StopTrace();
        // u5's scope is still open on this thread. The next recording does not inherit it.
        Linie::StartTrace(cfg(64, { 0xA7 }));
        Linie::TraceToken g1;
        Linie::TraceEnter(0xF2, 0, 700, 1, g1);
        check("a scope the last recording left open does not carry into the next", !g1.traced);
        Linie::TraceReturn(u5, 1);   // the old root returns now
        check("...and its late return writes nothing into the new recording", Linie::GetTraceInfo().written == 0,
              sz(Linie::GetTraceInfo().written).c_str());
        Linie::StopTrace();

        // T5 (b): an excluded function is left out; what it calls is not.
        Linie::StartTrace(cfg(64, {}, { 0xEE }));
        Linie::TraceToken e1, e2;
        Linie::TraceEnter(0xEE, 0, 900, 1, e1);
        Linie::TraceEnter(0xF2, 0, 800, 1, e2);
        check("exclude: the per-frame function is left out", !e1.traced);
        check("exclude: what it calls is still traced", e2.traced);
        Linie::StopTrace();
        Linie::StartTrace(cfg(64, { 0xEE }, { 0xEE }));
        Linie::TraceToken e3, e4;
        Linie::TraceEnter(0xEE, 0, 900, 1, e3);
        Linie::TraceEnter(0xEE, 0, 800, 1, e4);
        check("exclude: a ticked function still opens its scope", e3.traced && e3.opened);
        check("exclude: inside the scope the excluded function is still left out", !e4.traced);
        Linie::StopTrace();
        info = Linie::GetTraceInfo();
        check("info counts the two sets", info.ticked == 1 && info.excluded == 1);

        // The distinct functions and objects of the KEPT window: the overwritten first record is not among them, and
        // a return record (its `a` is a sequence number, not a function) is not either.
        Linie::StartTrace(cfg(4));
        {
            Linie::TraceToken d0, d1, d2, d3;
            Linie::TraceEnter(0xF9, 0xB9, 1000, 1, d0);   // seq 0, overwritten
            Linie::TraceEnter(0xF2, 0xB2, 1000, 1, d1);   // seq 1
            Linie::TraceEnter(0xF1, 0xB1, 900, 1, d2);    // seq 2
            Linie::TraceReturn(d2, 1);                    // seq 3, a = 2
            Linie::TraceEnter(0xF2, 0, 900, 1, d3);       // seq 4
        }
        Linie::StopTrace();
        std::vector<uintptr_t> dfuncs, dobjs;
        check("distinct: the functions and objects of the kept window, sorted, no 0",
              Linie::TraceDistinct(dfuncs, dobjs) && dfuncs == std::vector<uintptr_t>{ 0xF1, 0xF2 } &&
              dobjs == std::vector<uintptr_t>{ 0xB1, 0xB2 }, sz(dfuncs.size()).c_str());

        // Review DLL-1: what the UI reads and releases belongs to one recording. The copy reports the state it copied
        // under the same lock, the names say which recording they are, and a release names its recording.
        {
            const uint64_t gen = Linie::GetTraceInfo().gen;
            Linie::TraceInfo seen;
            recs.clear();
            check("a copy reports the trace it copied from, under the same lock",
                  Linie::CopyTrace(0, 10, recs, nullptr, &seen) && seen.gen == gen && seen.written == 5 &&
                  seen.firstValid == 1 && seen.allocated && !seen.tracing, sz(seen.written).c_str());
            uint64_t ngen = 0;
            check("the distinct names say which recording they are",
                  Linie::TraceDistinct(dfuncs, dobjs, &ngen) && ngen == gen, sz(ngen).c_str());
            check("a release that names another recording frees nothing",
                  !Linie::FreeTraceIfGen(gen + 1) && Linie::GetTraceInfo().allocated);
            check("a release that names this one frees it",
                  Linie::FreeTraceIfGen(gen) && !Linie::GetTraceInfo().allocated);
            Linie::StartTrace(cfg(8));
            const uint64_t running = Linie::GetTraceInfo().gen;
            Linie::TraceInfo during;
            std::vector<Linie::TraceRecord> none;
            check("a copy refused while recording still reports the trace it saw",
                  !Linie::CopyTrace(0, 10, none, nullptr, &during) && during.tracing && during.gen == running);
            check("a release never frees a recording that is still running",
                  !Linie::FreeTraceIfGen(running) && Linie::IsTracing());
            Linie::StopTrace();
        }

        // Review DLL-5: a recording that wrote nothing has nothing to read, and must not keep its ring in the game.
        Linie::StartTrace(cfg(8));
        check("a running trace is never released as empty", !Linie::ReleaseIfEmpty() && Linie::IsTracing());
        Linie::StopTrace();
        check("a stopped trace that wrote nothing is released", Linie::ReleaseIfEmpty() && !Linie::GetTraceInfo().allocated);
        Linie::StartTrace(cfg(8));
        { Linie::TraceToken t; Linie::TraceEnter(0xF1, 0, 1000, 1, t); }
        Linie::StopTrace();
        check("one that wrote something is kept for the reader", !Linie::ReleaseIfEmpty() && Linie::GetTraceInfo().allocated);

        Linie::FreeTrace();
        info = Linie::GetTraceInfo();
        check("FreeTrace releases the ring", !info.allocated && !Linie::CopyTrace(0, 10, recs));

        check("a trace starts again after a free", Linie::StartTrace(cfg(8)) == Linie::TraceStartStatus::Ok);
        Linie::Reset();
        check("Linie::Reset (the last client left) stops and frees the trace too",
              !Linie::IsTracing() && !Linie::GetTraceInfo().allocated);

        // TR2, made deterministic: the clock is read inside a hook's write, so a clock that blocks holds a writer
        // inside its section. Stop must wait for it; and when it never leaves, the ring is neither read nor freed.
        static HANDLE s_inside  = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        static HANDLE s_release = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        static std::atomic<int> s_blockNext{ 0 };
        Linie::SetTraceClockForTest([]() -> uint64_t {
            if (s_blockNext.exchange(0) == 1) {
                SetEvent(s_inside);
                WaitForSingleObject(s_release, INFINITE);
            }
            return 42;
        });
        Linie::StartTrace(cfg(8));
        s_blockNext = 1;
        std::thread writer([] { Linie::TraceToken t; Linie::TraceEnter(0xF1, 0, 1000, 1, t); });
        WaitForSingleObject(s_inside, 5000);
        std::atomic<bool> stopped{ false };
        std::thread stopper([&stopped] { Linie::StopTrace(); stopped = true; });
        Sleep(100);
        const bool waited = !stopped.load();
        SetEvent(s_release);
        writer.join();
        stopper.join();
        check("Stop waits while a hook is inside its write", waited && stopped.load());
        info = Linie::GetTraceInfo();
        check("...and keeps the write it waited for", info.written == 1 && info.quiesced, sz(info.written).c_str());

        ResetEvent(s_inside);
        ResetEvent(s_release);
        Linie::StartTrace(cfg(8));
        s_blockNext = 1;
        std::thread stuck([] { Linie::TraceToken t; Linie::TraceEnter(0xF1, 0, 1000, 1, t); });
        WaitForSingleObject(s_inside, 5000);
        Linie::StopTrace();   // gives up after its wait
        info = Linie::GetTraceInfo();
        std::vector<Linie::TraceRecord> unread;
        check("a hook that never leaves its write: Stop gives up and the ring is not read",
              !info.quiesced && !Linie::CopyTrace(0, 10, unread));
        // Review DLL-2: everything the hook reads stays as it was while it may still be inside -- the ring, its
        // capacity, the sets -- so a Free leaves it all, and a Start is refused instead of re-arming under it.
        Linie::FreeTrace();
        info = Linie::GetTraceInfo();
        check("...a Free while it may still be inside leaves the ring and its capacity as they were",
              info.allocated && info.capacity == 8, sz(info.capacity).c_str());
        check("...and a Start is refused as busy", Linie::StartTrace(cfg(8)) == Linie::TraceStartStatus::Busy);
        SetEvent(s_release);
        stuck.join();         // the hook finishes its write into the ring it was given: no fault
        // Review DLL-3: once the hook has left, the next wait reaches zero and the ring is readable again.
        Linie::StopTrace();
        info = Linie::GetTraceInfo();
        check("once the hook has left, Stop finds it quiesced and the ring is read again",
              info.quiesced && info.written == 1 && Linie::CopyTrace(0, 10, unread) && unread.size() == 1,
              sz(unread.size()).c_str());
        Linie::FreeTrace();
        check("...and a Free then releases it", !Linie::GetTraceInfo().allocated);

        // Review DLL-2: a fault inside the section (here a C++ throw from the clock; under the DLL's /EHa an SEH
        // fault unwinds the same way) must not leave the in-flight count up, or every later Stop waits it out.
        static std::atomic<int> s_throwNext{ 0 };
        Linie::SetTraceClockForTest([]() -> uint64_t {
            if (s_throwNext.exchange(0) == 1) throw std::runtime_error("clock fault");
            return 42;
        });
        Linie::StartTrace(cfg(8));
        s_throwNext = 1;
        bool threw = false;
        try { Linie::TraceToken t; Linie::TraceEnter(0xF1, 0, 1000, 1, t); } catch (const std::exception&) { threw = true; }
        const ULONGLONG stopAt = GetTickCount64();
        Linie::StopTrace();
        const ULONGLONG stopMs = GetTickCount64() - stopAt;
        check("a fault inside the hook's write does not leave Stop waiting: it quiesces at once",
              threw && Linie::GetTraceInfo().quiesced && stopMs < 1000, std::to_string(stopMs).c_str());
        Linie::FreeTrace();

        // Four threads trace while Stop runs: once Stop returns nothing changes, and every kept slot holds the
        // record its sequence number says.
        Linie::SetTraceClockForTest(nullptr);
        Linie::StartTrace(cfg(1 << 14));
        std::atomic<bool> go{ true };
        std::vector<std::thread> threads;
        for (uint32_t tid = 1; tid <= 4; ++tid) {
            threads.emplace_back([&go, tid] {
                while (go.load(std::memory_order_relaxed)) {
                    Linie::TraceToken outer, inner;
                    Linie::TraceEnter(0x500 + tid, tid, 2000, tid, outer);
                    Linie::TraceEnter(0x600 + tid, tid, 1900, tid, inner);
                    Linie::TraceReturn(inner, tid);
                    Linie::TraceReturn(outer, tid);
                }
            });
        }
        Sleep(30);
        Linie::StopTrace();
        const uint64_t w1 = Linie::GetTraceInfo().written;
        Sleep(20);
        const uint64_t w2 = Linie::GetTraceInfo().written;
        go = false;
        for (auto& th : threads) th.join();
        info = Linie::GetTraceInfo();
        check("threads: nothing is written once Stop returns", w1 == w2 && w1 > 0, sz(w2 - w1).c_str());
        bool slotsOk = copyAll() && !recs.empty();
        for (size_t k = 0; slotsOk && k < recs.size(); ++k) {
            const Linie::TraceRecord& r = recs[k];
            const uint64_t seq = r.seqKind & Linie::kTraceSeqMask;
            const bool isRet = (r.seqKind & Linie::kTraceReturnBit) != 0;
            slotsOk = seq == info.firstValid + k && r.tid >= 1 && r.tid <= 4 &&
                      (isRet ? (r.a < seq && r.b == 0)
                             : ((r.a == 0x500 + r.tid || r.a == 0x600 + r.tid) && r.b == r.tid));
        }
        check("threads: every kept slot holds the record its sequence number says", slotsOk, sz(recs.size()).c_str());
        Linie::FreeTrace();

        // The plan's "what one traced call costs" (docs/live-funcs-timeline-plan.md, Measure before building): printed,
        // not checked -- a timing depends on the machine. One thread, the real clock, a ring that never laps.
        {
            // ...so the benchmarks say which CPU they ran on: a nanosecond figure is that CPU's, and the two PCs this
            // project is measured on differ (the maintainer, 2026-10-07).
            int regs[4] = {};
            char brand[49] = {};
            __cpuid(regs, static_cast<int>(0x80000000u));
            if (static_cast<unsigned>(regs[0]) >= 0x80000004u) {
                for (int leaf = 0; leaf < 3; ++leaf) {
                    __cpuid(regs, static_cast<int>(0x80000002u) + leaf);
                    memcpy(brand + leaf * 16, regs, 16);
                }
            }
            printf("  info  the benchmarks below ran on: %s\n", brand[0] ? brand : "(CPU brand unknown)");
        }
        {
            constexpr int N = 1 << 20;
            LARGE_INTEGER f, t0, t1;
            QueryPerformanceFrequency(&f);
            auto nsPer = [&](LARGE_INTEGER a, LARGE_INTEGER b) {
                return double(b.QuadPart - a.QuadPart) * 1e9 / double(f.QuadPart) / N;
            };
            Linie::StartTrace(cfg(2ull * N + 16));
            QueryPerformanceCounter(&t0);
            for (int i = 0; i < N; ++i) {
                Linie::TraceToken t;
                Linie::TraceEnter(0x1000 + (i & 63), 0x2000, 1000, 1, t);
                Linie::TraceReturn(t, 1);
            }
            QueryPerformanceCounter(&t1);
            const double traced = nsPer(t0, t1);
            Linie::StartTrace(cfg(16, { 0xA7 }));
            QueryPerformanceCounter(&t0);
            for (int i = 0; i < N; ++i) {
                Linie::TraceToken t;
                Linie::TraceEnter(0x1000 + (i & 63), 0x2000, 1000, 1, t);   // not ticked, not in scope: no record
            }
            QueryPerformanceCounter(&t1);
            const double outOfScope = nsPer(t0, t1);
            Linie::FreeTrace();
            printf("  info  trace cost: %.1f ns per traced call (entry + return), %.1f ns per call outside a ticked scope\n",
                   traced, outOfScope);
        }
    }

    {
        blk("TRACE-UNLOADED-NAMES: a function's identity is read when the recording first sees it");
        // D1 (docs/live-funcs-timeline-plan.md, "Decided 2026-10-07"): a function the game unloads before Stop keeps
        // the name it had when it fired. The hook reads it once per distinct function, while it is being dispatched
        // and so certainly alive; a stub reader stands in for Ubel's here.
        static int s_reads = 0;
        static int s_failNext = 0;   // the next N reads fail
        auto stub = [](uintptr_t f, Linie::FuncIdentity& out) -> bool {
            ++s_reads;
            if (s_failNext > 0) { --s_failNext; return false; }
            out.nameIndex     = static_cast<int32_t>(f & 0xFFFF);
            out.classIndex    = 7;
            out.functionFlags = 0x400;
            out.numParms      = 2;
            out.parmsSize     = 16;
            out.isWidget      = (f == 0xB);
            return true;
        };
        auto statOf = [](uintptr_t f) {
            std::vector<Linie::FuncStat> st;
            uint64_t w = 0;
            Linie::Snapshot(st, w);
            for (const auto& x : st) if (x.func == f) return x;
            return Linie::FuncStat{};
        };
        auto num = [](long long n) { return std::to_string(n); };

        Linie::Reset();
        s_reads = 0;
        Linie::StartRecording(stub);
        Linie::RecordCall(0xA, 1000); Linie::RecordCall(0xA, 1001); Linie::RecordCall(0xA, 1002);
        Linie::RecordCall(0xB, 1003);
        Linie::StopRecording();
        const auto ia = statOf(0xA).ident, ib = statOf(0xB).ident;
        check("the reader runs once per distinct function, not once per call", s_reads == 2, num(s_reads).c_str());
        check("...and the table keeps what it read", ia.captured && ia.nameIndex == 0xA && ia.classIndex == 7 &&
              ia.functionFlags == 0x400 && ia.numParms == 2 && ia.parmsSize == 16 && !ia.isWidget);
        check("...for each function", ib.captured && ib.nameIndex == 0xB && ib.isWidget);

        // A read that fails is tried again on the function's next calls, a bounded number of times.
        Linie::StartRecording(stub);
        s_reads = 0; s_failNext = 1;
        Linie::RecordCall(0xC, 2000); Linie::RecordCall(0xC, 2001); Linie::RecordCall(0xC, 2002);
        check("a failed read is tried again on the next call, and not after it succeeds",
              statOf(0xC).ident.captured && s_reads == 2, num(s_reads).c_str());
        s_reads = 0; s_failNext = 1000;
        for (int i = 0; i < 20; ++i) Linie::RecordCall(0xD, 3000 + i);
        check("...a bounded number of times",
              !statOf(0xD).ident.captured && s_reads == Linie::kIdentityTries, num(s_reads).c_str());
        s_failNext = 0;
        check("...and the calls still count", statOf(0xD).count == 20, num(statOf(0xD).count).c_str());

        // A recording without a reader reads nothing; a new recording keeps nothing of the last one's.
        Linie::StartRecording();
        s_reads = 0;
        Linie::RecordCall(0xA, 4000);
        check("a recording without a reader reads nothing", !statOf(0xA).ident.captured && s_reads == 0);
        Linie::StartRecording(stub);
        check("a new recording starts from an empty table", statOf(0xA).func == 0);
        Linie::Reset();

        // The trace's distinct functions carry what the table read; a function only the trace saw has nothing (a call
        // traced between StartTrace and StartRecording, or after StopRecording).
        Linie::TraceConfig tc;
        tc.bytes = 64 * sizeof(Linie::TraceRecord);
        check("setup: a ring of 64 records starts", Linie::StartTrace(tc) == Linie::TraceStartStatus::Ok);
        Linie::StartRecording(stub);
        Linie::TraceToken ta, te;
        Linie::RecordCall(0xA, 5000);
        Linie::TraceEnter(0xA, 0xB1, 1000, 7, ta);
        Linie::TraceReturn(ta, 7);
        Linie::TraceEnter(0xE, 0xB2, 1000, 7, te);   // traced, never counted
        Linie::TraceReturn(te, 7);
        Linie::StopRecording();
        Linie::StopTrace();
        std::vector<uintptr_t> df, dob;
        std::vector<Linie::FuncIdentity> di;
        const bool got = Linie::TraceDistinct(df, dob, nullptr, &di);
        check("TraceDistinct hands one identity per distinct function, in the same order",
              got && df.size() == 2 && di.size() == 2 && df[0] == 0xA && df[1] == 0xE, num(di.size()).c_str());
        check("...the table's for a counted function, none for one only the trace saw",
              di.size() == 2 && di[0].captured && di[0].nameIndex == 0xA && !di[1].captured);
        Linie::StartRecording(stub);   // a new recording empties the table: the stopped trace's names stay frozen
        std::vector<Linie::FuncIdentity> di2;
        Linie::TraceDistinct(df, dob, nullptr, &di2);
        check("...frozen with the trace: a table cleared since does not take them back",
              di2.size() == 2 && di2[0].captured && di2[0].nameIndex == 0xA);
        Linie::Reset();

        // Review DLL-3: a freed function's address taken by another that fires in the same recording. Each call checks
        // the function's key -- its FName and its Outer -- against what was read; another function is read again and
        // the entry marked reused: its count is both functions', and its name the one now there.
        static int32_t  s_occName  = 0x50;     // the function now at 0xF0
        static int32_t  s_occClass = 0x90;     // its class's FName
        static uint64_t s_occOuter = 0x9000;   // its class
        static int      s_keyReads = 0;
        auto occReader = [](uintptr_t, Linie::FuncIdentity& out) -> bool {
            out.nameIndex = s_occName;
            out.classIndex = s_occClass;
            out.outer = s_occOuter;
            return true;
        };
        auto occKey = [](uintptr_t, int32_t& idx, int32_t& n, uint64_t& outer) -> bool {
            ++s_keyReads;
            idx = s_occName;
            n = 0;
            outer = s_occOuter;
            return true;
        };
        Linie::StartRecording(occReader, occKey);
        s_keyReads = 0;
        Linie::RecordCall(0xF0, 6000); Linie::RecordCall(0xF0, 6001);
        check("the same function at its address: not reused",
              !statOf(0xF0).ident.reused && statOf(0xF0).ident.nameIndex == 0x50);
        check("...the key is checked on the calls after the first sight", s_keyReads == 1, num(s_keyReads).c_str());
        s_occOuter = 0x9100;   // its class reloaded: a new UClass object, the same names
        Linie::RecordCall(0xF0, 6002);
        check("...its class reloaded under the same names: not reused, the new class kept",
              !statOf(0xF0).ident.reused && statOf(0xF0).ident.outer == 0x9100);
        s_occName = 0x60; s_occClass = 0xA0; s_occOuter = 0xA000;   // another function took the address
        Linie::RecordCall(0xF0, 6003); Linie::RecordCall(0xF0, 6004);
        const auto ru = statOf(0xF0);
        check("another function at the address: read again and marked reused",
              ru.ident.reused && ru.ident.nameIndex == 0x60 && ru.ident.classIndex == 0xA0 && ru.count == 5,
              num(ru.ident.nameIndex).c_str());
        s_occOuter = 0xA100;   // the new occupant's class reloaded: the same names, so no new reuse...
        Linie::RecordCall(0xF0, 6005);
        check("...and the mark stays when the new one's class reloads (the address did hold two functions)",
              statOf(0xF0).ident.reused && statOf(0xF0).ident.outer == 0xA100);
        s_occName = 0x50; s_occClass = 0x90; s_occOuter = 0x9000;
        Linie::RecordCall(0xF0, 6006);
        check("...and when the first one comes back", statOf(0xF0).ident.reused);
        Linie::Reset();

        // Review UI-1: a tick is an address from an earlier fetch, and the function there may have been unloaded since
        // without the UI learning it (its row cut by the fetch limit). pe_profile_start checks the ticks against the
        // previous recording's table -- still there, since StartRecording has not cleared it yet.
        Linie::StartRecording(stub);
        Linie::RecordCall(0xA, 7000);
        Linie::RecordCall(0xB, 7001);
        Linie::StopRecording();
        std::vector<Linie::FuncIdentity> tids;
        Linie::IdentitiesOf({ 0xA, 0xC, 0xB }, tids);
        check("IdentitiesOf: the table's identity for each address asked, in order; none for one it never saw",
              tids.size() == 3 && tids[0].captured && tids[0].nameIndex == 0xA && !tids[1].captured &&
              tids[2].captured && tids[2].nameIndex == 0xB, num(tids.size()).c_str());
        Linie::Reset();
        Linie::IdentitiesOf({ 0xA }, tids);
        check("...and none from an empty table", tids.size() == 1 && !tids[0].captured);

        // Ubel's reader: loads only, from what Fern installs at Start. A fake UFunction whose Outer is a class two
        // steps below a widget base, and one whose class derives from nothing.
        static uint8_t fFn[0x100] = {}, fFn2[0x100] = {}, fCls[0x100] = {}, fMid[0x100] = {}, fBase[0x100] = {},
                       fOther[0x100] = {};
        auto put   = [](uint8_t* base, int off, uintptr_t v) { memcpy(base + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* base, int off, int32_t v)   { memcpy(base + off, &v, sizeof(v)); };
        auto at    = [](uint8_t* p) { return reinterpret_cast<uintptr_t>(p); };
        constexpr int kFlagsOff = 0xB0;
        put32(fFn, Grimoire::OFF_UOBJECT_NAME, 21);
        put32(fFn, Grimoire::OFF_UOBJECT_NAME + DynOff::FNAME_NUMBER, 3);
        put(fFn, DynOff::UOBJECT_OUTER, at(fCls));
        put32(fFn, kFlagsOff, 0x00080401);
        fFn[kFlagsOff + 4] = 2;                                   // NumParms
        put32(fFn, kFlagsOff + 6, 24);                            // ParmsSize (uint16; the int32 store's high half is 0)
        put32(fCls, Grimoire::OFF_UOBJECT_NAME, 22);
        put(fCls, DynOff::USTRUCT_SUPER, at(fMid));
        put(fMid, DynOff::USTRUCT_SUPER, at(fBase));
        put32(fFn2, Grimoire::OFF_UOBJECT_NAME, 23);
        put(fFn2, DynOff::UOBJECT_OUTER, at(fOther));
        put(fOther, DynOff::USTRUCT_SUPER, at(fOther));          // a chain that points at itself must end

        Ubel::FunctionCaptureSetup cs;
        cs.flagsOffset = kFlagsOff;
        cs.tailOffset  = kFlagsOff;
        cs.widgetBases = { at(fBase) };
        Ubel::SetFunctionCapture(cs);
        Linie::FuncIdentity id{};
        const bool okA = Ubel::CaptureFunctionIdentity(at(fFn), id);
        check("Ubel's reader: the function's FName and its class's", okA && id.nameIndex == 21 && id.nameNumber == 3 &&
              id.classIndex == 22 && id.outer == at(fCls), num(id.nameIndex).c_str());
        int32_t kIdx = 0, kNum = 0;
        uint64_t kOuter = 0;
        check("Ubel's key reader: the FName and the Outer, for the check on every call",
              Ubel::ReadFunctionKey(at(fFn), kIdx, kNum, kOuter) && kIdx == 21 && kNum == 3 && kOuter == at(fCls));
        check("...an address that cannot be read is refused", !Ubel::ReadFunctionKey(0x1000, kIdx, kNum, kOuter));
        check("...its flags and parameters at the offsets set up at Start",
              id.functionFlags == 0x00080401 && id.numParms == 2 && id.parmsSize == 24, num(id.parmsSize).c_str());
        check("...a class two steps below a widget base is a widget's", id.isWidget);
        Linie::FuncIdentity id2{};
        const bool okB = Ubel::CaptureFunctionIdentity(at(fFn2), id2);
        check("...a class whose chain points at itself is not, and the walk ends", okB && id2.nameIndex == 23 &&
              !id2.isWidget);
        Linie::FuncIdentity id3{};
        check("...an address that cannot be read is refused", !Ubel::CaptureFunctionIdentity(0x1000, id3));
        cs.flagsOffset = -1;
        Ubel::SetFunctionCapture(cs);
        Linie::FuncIdentity id4{};
        Ubel::CaptureFunctionIdentity(at(fFn), id4);
        check("...without a flags offset it reads no flags (it never guesses on the game thread)",
              id4.nameIndex == 21 && id4.functionFlags == 0 && id4.numParms == 0 && id4.parmsSize == 0);
        Ubel::SetFunctionCapture(Ubel::FunctionCaptureSetup{});

        // The classifier the pipe applies at read time.
        Linie::FuncIdentity cap{};
        cap.captured = true;
        cap.nameIndex = 21;
        cap.nameNumber = 3;
        cap.classIndex = 22;
        const Ubel::NameWitness same{ 21, 3 }, other{ 40, 0 }, sameCls{ 22, 0 }, otherCls{ 45, 0 }, noCls{};
        using FS = Ubel::FuncState;
        check("classify: still in its slot, its name and its class's unchanged -> live",
              Ubel::ClassifyFunctionState(true, true, same, sameCls, cap) == FS::Live);
        check("...still in its slot under another name -> recycled: another function took the address",
              Ubel::ClassifyFunctionState(true, true, other, sameCls, cap) == FS::Recycled);
        // Review DLL-1: every Blueprint class has its own Construct, ReceiveBeginPlay..., all one FName.
        check("...the same name in ANOTHER class -> recycled too",
              Ubel::ClassifyFunctionState(true, true, same, otherCls, cap) == FS::Recycled);
        check("...a class that cannot be read now, or was not read then, is not a difference",
              Ubel::ClassifyFunctionState(true, true, same, noCls, cap) == FS::Live &&
              Ubel::ClassifyFunctionState(true, true, same, otherCls,
                                          [&] { auto c = cap; c.classIndex = 0; return c; }()) == FS::Live);
        check("...gone from its slot -> unloaded, named from what was read",
              Ubel::ClassifyFunctionState(false, false, Ubel::NameWitness{}, noCls, cap) == FS::Unloaded);
        check("...gone and never read -> unnamed",
              Ubel::ClassifyFunctionState(false, false, Ubel::NameWitness{}, noCls, Linie::FuncIdentity{}) == FS::Unnamed);
        check("...never read but still there -> live, named now as before",
              Ubel::ClassifyFunctionState(true, true, other, otherCls, Linie::FuncIdentity{}) == FS::Live);
        check("...in its slot but its name unreadable -> unloaded when it was read, unnamed when not",
              Ubel::ClassifyFunctionState(true, false, Ubel::NameWitness{}, noCls, cap) == FS::Unloaded &&
              Ubel::ClassifyFunctionState(true, false, Ubel::NameWitness{}, noCls, Linie::FuncIdentity{}) == FS::Unnamed);

        // What the read costs, printed and not checked: the steady state with a reader installed against none, and
        // one first sight through Ubel's reader.
        {
            constexpr int N = 1 << 20;
            LARGE_INTEGER f, t0, t1;
            QueryPerformanceFrequency(&f);
            auto nsPer = [&](LARGE_INTEGER a, LARGE_INTEGER b, int n) {
                return double(b.QuadPart - a.QuadPart) * 1e9 / double(f.QuadPart) / n;
            };
            Linie::StartRecording();
            QueryPerformanceCounter(&t0);
            for (int i = 0; i < N; ++i) Linie::RecordCall(0x1000 + (i & 63), 1000 + i);
            QueryPerformanceCounter(&t1);
            const double bare = nsPer(t0, t1, N);
            Linie::StartRecording(stub);
            QueryPerformanceCounter(&t0);
            for (int i = 0; i < N; ++i) Linie::RecordCall(0x1000 + (i & 63), 1000 + i);
            QueryPerformanceCounter(&t1);
            const double withReader = nsPer(t0, t1, N);
            // Review DLL-3's check on every call, through Ubel's readers on the fake function.
            Linie::StartRecording(&Ubel::CaptureFunctionIdentity, &Ubel::ReadFunctionKey);
            QueryPerformanceCounter(&t0);
            for (int i = 0; i < N; ++i) Linie::RecordCall(at(fFn), 1000 + i);
            QueryPerformanceCounter(&t1);
            const double withKey = nsPer(t0, t1, N);
            Linie::Reset();
            cs.flagsOffset = kFlagsOff;
            Ubel::SetFunctionCapture(cs);
            constexpr int M = 1 << 16;
            QueryPerformanceCounter(&t0);
            for (int i = 0; i < M; ++i) { Linie::FuncIdentity x{}; Ubel::CaptureFunctionIdentity(at(fFn), x); }
            QueryPerformanceCounter(&t1);
            const double firstSight = nsPer(t0, t1, M);
            Ubel::SetFunctionCapture(Ubel::FunctionCaptureSetup{});
            printf("  info  table cost: %.1f ns per call with no reader, %.1f ns with one (after the first sight), "
                   "%.1f ns with Ubel's key check on every call; %.1f ns per first sight through Ubel's reader\n",
                   bare, withReader, withKey, firstSight);
        }
    }

    {
        blk("LIVEFUNCS-STEP2: the arm rules -- a name's key, what one arm copies, when it copies after the call");
        // docs/live-funcs-step2-items.md, N0. Pure rules: the ring a choice gets, what each arming of it copies.
        auto u = [](uint64_t n) { return std::to_string(n); };
        check("a ring's slot: the parameter block rounded to 8", Linie::RingCapFor(41) == 48 && Linie::RingCapFor(40) == 40,
              u(Linie::RingCapFor(41)).c_str());
        check("...capped at kSnapMaxCopy", Linie::RingCapFor(3000) == Linie::kSnapMaxCopy && Linie::kSnapMaxCopy == 2048,
              u(Linie::RingCapFor(3000)).c_str());
        check("...kSnapUnknownCopy when the size could not be read",
              Linie::RingCapFor(0) == Linie::kSnapUnknownCopy && Linie::kSnapUnknownCopy == 256, u(Linie::RingCapFor(0)).c_str());
        check("an arm copies its own parameter size", Linie::ArmCopyBytes(40, 0x400, 64) == 40,
              u(Linie::ArmCopyBytes(40, 0x400, 64)).c_str());
        check("...at most its ring's slot, and says it was cut",
              Linie::ArmCopyBytes(100, 0x400, 64) == 64 && Linie::ArmTruncated(100, 64) && !Linie::ArmTruncated(64, 64),
              u(Linie::ArmCopyBytes(100, 0x400, 64)).c_str());
        check("...the whole slot when nothing could be read", Linie::ArmCopyBytes(0, 0, 64) == 64,
              u(Linie::ArmCopyBytes(0, 0, 64)).c_str());
        check("...nothing for a function read as having no parameters", Linie::ArmCopyBytes(0, 0x400, 64) == 0,
              u(Linie::ArmCopyBytes(0, 0x400, 64)).c_str());
        check("the after copy: out parameters or a return (FUNC_HasOutParms), or flags never read",
              Linie::ArmTakesAfter(Linie::kFuncHasOutParms | 0x400) && Linie::ArmTakesAfter(0) &&
              !Linie::ArmTakesAfter(0x400));
        check("...and a return value alone, which FUNC_HasOutParms does not cover (measured: SnapProbe_RetOnly)",
              Linie::ArmTakesAfter(0x400, 4) && !Linie::ArmTakesAfter(0x400, 0xFFFF));
        const Linie::NameKey k1{ 1, 0, 7, 0 }, k2{ 1, 0, 8, 0 }, k3{ 1, 1, 7, 0 }, k4{ 2, 0, 0, 0 };
        check("a key orders over its four ints, function first",
              k1 < k2 && k1 < k3 && k3 < k4 && k2 < k4 && !(k2 < k1) && !(k1 < k1) && k1 == Linie::NameKey{ 1, 0, 7, 0 } &&
              !(k1 == k2));
        std::vector<Linie::NameKey> ks{ k4, k2, k1, k3 };
        std::sort(ks.begin(), ks.end());
        check("...so a sorted list of keys can be searched", ks[0] == k1 && ks[3] == k4 &&
              std::binary_search(ks.begin(), ks.end(), k2) && !std::binary_search(ks.begin(), ks.end(), Linie::NameKey{ 1, 0, 9, 0 }));
        const Linie::ArmHint none{};
        check("a default hint belongs to no recording and no ring", none.gen == 0 && none.ring == -1 && none.flags == 0);
    }

    {
        blk("LIVEFUNCS-STEP2: arming by name -- the table's first read of an address arms it for a name it matches");
        // docs/live-funcs-step2-items.md, N1 (T10): a tick or a choice is a name, and the table reads every function
        // at its first call anyway. A stub reader stands in for Ubel's: the function's FName is its address's low 16
        // bits, its class's FName 7 -- except 0xC1, whose FName carries a Number, and 0xC2, whose class is 8.
        static int s_armReads = 0;
        auto armStub = [](uintptr_t f, Linie::FuncIdentity& out) -> bool {
            ++s_armReads;
            out.nameIndex     = static_cast<int32_t>(f & 0xFFFF);
            out.nameNumber    = (f == 0xC1) ? 1 : 0;
            out.classIndex    = (f == 0xC2) ? 8 : (f == 0xC3) ? 6 : 7;
            // 0xE1 has out parameters (FUNC_HasOutParms), 0xE2's flags could not be read, 0xE3's block is 100 bytes.
            out.functionFlags = (f == 0xE1) ? 0x00400400u : (f == 0xE2) ? 0u : 0x400u;
            if (f == 0xE4) out.returnValueOffset = 12;   // a return value, and no FUNC_HasOutParms
            out.numParms      = 2;
            out.parmsSize     = (f == 0xE3) ? 100 : 16;
            return true;
        };
        auto u = [](uint64_t n) { return std::to_string(n); };
        auto build = [] {
            auto a = Linie::BuildArmState({
                Linie::ArmSpec{ Linie::NameKey{ 0xB, 0, 7, 0 }, false, 0, 64 },
                Linie::ArmSpec{ Linie::NameKey{ 0xA, 0, 7, 0 }, true, -1, 0 },
                Linie::ArmSpec{ Linie::NameKey{ 0xC1, 0, 7, 0 }, false, 1, 64 },
                Linie::ArmSpec{ Linie::NameKey{ 0xC2, 0, 7, 0 }, false, 2, 64 },
                Linie::ArmSpec{ Linie::NameKey{ 0xC3, 0, 7, 0 }, false, 4, 64 },
                Linie::ArmSpec{ Linie::NameKey{ 0xD, 0, 7, 0 }, true, -1, 0 },
                Linie::ArmSpec{ Linie::NameKey{ 0xD, 0, 7, 0 }, false, 3, 32 },
            }, 8);
            a->gen = 5;
            return a;
        };
        auto st = build();
        bool sorted = true;
        for (size_t i = 1; i < st->specs.size(); ++i) sorted = sorted && st->specs[i - 1].key < st->specs[i].key;
        const Linie::ArmSpec* dSpec = nullptr;
        for (const auto& sp : st->specs) if (sp.key == Linie::NameKey{ 0xD, 0, 7, 0 }) dSpec = &sp;
        check("BuildArmState sorts the names and merges one ticked and chosen into one spec",
              st->specs.size() == 6 && sorted && dSpec && dSpec->tick && dSpec->ring == 3 && dSpec->ringCap == 32 &&
              st->log.capacity() >= 8, u(st->specs.size()).c_str());

        Linie::Reset();
        Linie::StartRecording(armStub, nullptr, st);
        Linie::ArmHint h;
        Linie::RecordCall(0xA, 1000, &h);
        check("a ticked name: its first call is armed to open a scope, with no ring",
              h.gen == 5 && (h.flags & Linie::kArmTick) && h.ring == -1 && st->log.empty(), u(h.gen).c_str());
        Linie::RecordCall(0xB, 1001, &h);
        check("a chosen name: armed with its ring, its first arm, its own copy size",
              h.gen == 5 && h.ring == 0 && h.arm == 0 && h.copy == 16 && !(h.flags & Linie::kArmTick) &&
              st->log.size() == 1 && st->log[0].addr == 0xB && st->log[0].ring == 0 && st->log[0].armMs == 1001,
              u(st->log.size()).c_str());
        check("...the after copy and the cut follow the arm rules (flags 0x400: no out parameters; 16 fits)",
              !(h.flags & Linie::kArmAfter) && !(h.flags & Linie::kArmTruncated));
        Linie::ArmHint h2;
        Linie::RecordCall(0xB, 1002, &h2);
        check("its next call carries the same hint, and arms nothing new",
              h2.gen == 5 && h2.ring == 0 && h2.arm == 0 && h2.copy == 16 && st->log.size() == 1, u(st->log.size()).c_str());
        Linie::RecordCall(0xD, 1003, &h);
        check("a name both ticked and chosen: one arm does both",
              (h.flags & Linie::kArmTick) && h.ring == 3 && h.arm == 1 && st->log.size() == 2);
        Linie::RecordCall(0xC1, 1004, &h);
        check("the same FName index with another Number is another name: not armed", h.gen == 0 && h.ring == -1);
        Linie::RecordCall(0xC2, 1005, &h);
        check("the same function name in another class is another name: not armed", h.gen == 0 && h.ring == -1);
        Linie::RecordCall(0xC3, 1005, &h);   // its class sorts just below the followed one: the search lands on it
        check("...whichever side of the followed class it sorts on", h.gen == 0 && h.ring == -1);
        Linie::RecordCall(0xE, 1006, &h);
        check("a name nobody follows: a default hint", h.gen == 0 && h.ring == -1 && h.flags == 0);
        check("...and the log holds the two arms only", st->log.size() == 2, u(st->log.size()).c_str());

        // The Linie review (tests): the hint's after and cut bits come from the table's read, not only from a test's
        // hand-made hint.
        auto st2 = Linie::BuildArmState({ Linie::ArmSpec{ Linie::NameKey{ 0xE1, 0, 7, 0 }, false, 0, 64 },
                                          Linie::ArmSpec{ Linie::NameKey{ 0xE2, 0, 7, 0 }, false, 1, 64 },
                                          Linie::ArmSpec{ Linie::NameKey{ 0xE3, 0, 7, 0 }, false, 2, 64 },
                                          Linie::ArmSpec{ Linie::NameKey{ 0xE4, 0, 7, 0 }, false, 3, 64 } }, 8);
        st2->gen = 6;
        Linie::StartRecording(armStub, nullptr, st2);
        Linie::RecordCall(0xE1, 3000, &h);
        check("out parameters: the table arms the after copy", (h.flags & Linie::kArmAfter) && !(h.flags & Linie::kArmTruncated));
        Linie::RecordCall(0xE2, 3001, &h);
        check("flags that could not be read: the after copy too", (h.flags & Linie::kArmAfter) != 0);
        Linie::RecordCall(0xE3, 3002, &h);
        check("a block larger than its ring's slot: cut to it, and flagged", (h.flags & Linie::kArmTruncated) &&
              h.copy == 64 && !(h.flags & Linie::kArmAfter), u(h.copy).c_str());
        Linie::RecordCall(0xE4, 3003, &h);
        check("a return value without FUNC_HasOutParms: the table arms the after copy", (h.flags & Linie::kArmAfter) != 0);

        Linie::StartRecording(armStub);
        Linie::RecordCall(0xB, 2000, &h);
        check("a recording that follows no names arms nothing", h.gen == 0 && h.ring == -1);
        Linie::Reset();
    }

    {
        blk("LIVEFUNCS-STEP2: arm upkeep -- a key that changes disarms, a reload is a new arm, an armed class is checked");
        // docs/live-funcs-step2-items.md, N2. 0xF0 holds whatever the statics say; any other address is a function
        // nobody follows. The class reader counts its reads: only armed addresses pay for it.
        static int32_t  s_fn = 0x50, s_cls = 0x90;
        static uint64_t s_outer = 0x9000;
        static int      s_keyFail = 0, s_clsReads = 0, s_clsFail = 0, s_readFail = 0;
        auto reader = [](uintptr_t f, Linie::FuncIdentity& out) -> bool {
            if (f == 0xF0 && s_readFail > 0) { --s_readFail; return false; }
            const bool occ = (f == 0xF0);
            out.nameIndex     = occ ? s_fn : static_cast<int32_t>(f & 0xFFFF);
            out.classIndex    = occ ? s_cls : 7;
            out.outer         = occ ? s_outer : 0x7000;
            out.functionFlags = 0x400;
            out.parmsSize     = 16;
            return true;
        };
        auto keyReader = [](uintptr_t f, int32_t& idx, int32_t& n, uint64_t& outer) -> bool {
            if (f == 0xF0 && s_keyFail > 0) { --s_keyFail; return false; }
            idx = (f == 0xF0) ? s_fn : static_cast<int32_t>(f & 0xFFFF);
            n = 0;
            outer = (f == 0xF0) ? s_outer : 0x7000;
            return true;
        };
        auto clsReader = [](uint64_t obj, int32_t& idx, int32_t& n) -> bool {
            ++s_clsReads;
            if (s_clsFail > 0) { --s_clsFail; return false; }
            idx = (obj == s_outer) ? s_cls : 7;
            n = 0;
            return true;
        };
        auto statOf = [](uintptr_t f) {
            std::vector<Linie::FuncStat> st;
            uint64_t w = 0;
            Linie::Snapshot(st, w);
            for (const auto& x : st) if (x.func == f) return x;
            return Linie::FuncStat{};
        };
        auto u = [](uint64_t n) { return std::to_string(n); };
        std::shared_ptr<Linie::ArmState> st;
        auto restart = [&] {
            s_fn = 0x50; s_cls = 0x90; s_outer = 0x9000; s_keyFail = 0;
            st = Linie::BuildArmState({ Linie::ArmSpec{ Linie::NameKey{ 0x50, 0, 0x90, 0 }, false, 0, 64 } }, 8);
            st->gen = 9;
            st->classNameReader = clsReader;
            Linie::StartRecording(reader, keyReader, st);
        };
        Linie::ArmHint h;

        restart();
        Linie::RecordCall(0xF0, 1000, &h);
        check("setup: the followed function arms its address", h.gen == 9 && h.ring == 0 && h.arm == 0);
        s_clsReads = 0;
        for (int i = 0; i < 10; ++i) Linie::RecordCall(0xF1, 1001 + i, &h);
        check("an address armed for nothing never has its class read", s_clsReads == 0 && h.gen == 0, u(s_clsReads).c_str());
        Linie::RecordCall(0xF0, 1020, &h);
        check("an armed one has, on each later call, and stays armed", s_clsReads == 1 && h.gen == 9 && h.arm == 0,
              u(s_clsReads).c_str());

        s_outer = 0x9100;   // the class reloaded: a new UClass object, the same names
        Linie::RecordCall(0xF0, 1030, &h);
        check("its class reloaded under the same names: a new arm, the same ring",
              h.gen == 9 && h.ring == 0 && h.arm == 1 && st->log.size() == 2, u(st->log.size()).c_str());
        check("...the two arms keep their own class, and the address is not called reused",
              st->log.size() == 2 && st->log[0].ident.outer == 0x9000 && st->log[1].ident.outer == 0x9100 &&
              !statOf(0xF0).ident.reused);

        s_cls = 0xA0;       // another class at the same address: the function's FName and the Outer unchanged
        Linie::RecordCall(0xF0, 1040, &h);
        check("its class's FName changed at the same address: read again, and disarmed",
              h.gen == 0 && h.ring == -1 && statOf(0xF0).ident.classIndex == 0xA0);
        s_cls = 0x90;       // review R3: and the followed class comes back at the same addresses
        Linie::RecordCall(0xF0, 1050, &h);
        check("...and when the followed class comes back there, armed again (review R3)",
              h.gen == 9 && h.ring == 0 && h.arm == 2 && st->log.size() == 3, u(st->log.size()).c_str());

        restart();
        Linie::RecordCall(0xF0, 2000, &h);
        s_fn = 0x60; s_cls = 0xA0; s_outer = 0xA000;   // another function took the address
        Linie::RecordCall(0xF0, 2001, &h);
        check("another function at the address: disarmed", h.gen == 0 && h.ring == -1);
        Linie::RecordCall(0xF0, 2002, &h);
        check("...and it stays so, while the table marks the address reused",
              h.gen == 0 && statOf(0xF0).ident.reused && st->log.size() == 1, u(st->log.size()).c_str());

        restart();
        Linie::RecordCall(0xF0, 3000, &h);
        s_keyFail = 1;
        Linie::RecordCall(0xF0, 3001, &h);
        check("a key that cannot be read: that call gets no hint", h.gen == 0 && h.ring == -1);
        Linie::RecordCall(0xF0, 3002, &h);
        check("...and the next call, read again, is armed as before", h.gen == 9 && h.ring == 0 && h.arm == 0 &&
              st->log.size() == 1);

        // The Linie review (tests): the class read failing, and the full read failing after a key change.
        s_clsFail = 1;
        Linie::RecordCall(0xF0, 3003, &h);
        check("a class that cannot be read: that call gets no hint", h.gen == 0 && h.ring == -1);
        Linie::RecordCall(0xF0, 3004, &h);
        check("...the next, read again, is armed", h.gen == 9 && h.ring == 0);
        s_outer = 0x9200;   // its class reloaded...
        s_readFail = 1;     // ...and the read of what is there now fails
        Linie::RecordCall(0xF0, 3005, &h);
        check("a changed key whose read fails: disarmed, not left with the old arm", h.gen == 0 && h.ring == -1);
        Linie::RecordCall(0xF0, 3006, &h);
        check("...and read on the next call: a new arm", h.gen == 9 && h.ring == 0 && h.arm == 1 && st->log.size() == 2,
              u(st->log.size()).c_str());
        Linie::Reset();
    }

    {
        blk("LIVEFUNCS-STEP2: the arm log's capacity and what became of each followed name");
        // docs/live-funcs-step2-items.md, N3. Every address 0xBx is the same function by name (a class reloaded at
        // each new address); 0xC is nobody's. A log with room for one arm. s_reload moves 0xB1's class: a reload at
        // the same address, which is the same address for the count.
        static uint64_t s_reload = 0;
        auto reader = [](uintptr_t f, Linie::FuncIdentity& out) -> bool {
            out.nameIndex = ((f & 0xF0) == 0xB0) ? 0x77 : static_cast<int32_t>(f & 0xFFFF);
            out.classIndex = 7;
            out.outer = 0x7000 + f + (f == 0xB1 ? s_reload : 0);
            out.functionFlags = 0x400;
            out.parmsSize = 16;
            return true;
        };
        auto keyReader = [](uintptr_t f, int32_t& idx, int32_t& n, uint64_t& outer) -> bool {
            idx = ((f & 0xF0) == 0xB0) ? 0x77 : static_cast<int32_t>(f & 0xFFFF);
            n = 0;
            outer = 0x7000 + f + (f == 0xB1 ? s_reload : 0);
            return true;
        };
        auto u = [](uint64_t n) { return std::to_string(n); };
        auto st = Linie::BuildArmState({ Linie::ArmSpec{ Linie::NameKey{ 0x77, 0, 7, 0 }, true, 0, 64 },
                                         Linie::ArmSpec{ Linie::NameKey{ 0x88, 0, 7, 0 }, false, 1, 64 } }, 1);
        st->gen = 3;
        s_reload = 0;
        Linie::Reset();
        Linie::StartRecording(reader, keyReader, st);
        Linie::ArmHint h;
        Linie::RecordCall(0xB1, 1000, &h);
        check("setup: the first address takes the log's one place", h.ring == 0 && h.arm == 0 && st->log.size() == 1);
        Linie::RecordCall(0xB2, 1001, &h);
        check("a full log: no snapshots for the next address, but its tick still opens",
              h.gen == 3 && h.ring == -1 && (h.flags & Linie::kArmTick) && st->log.size() == 1, u(st->log.size()).c_str());
        Linie::RecordCall(0xB2, 1002, &h);
        s_reload = 0x100;
        Linie::RecordCall(0xB1, 1003, &h);   // read again under a new class: the log is full, the address the same
        Linie::RecordCall(0xC, 1004, &h);
        Linie::StopRecording();
        const auto sum = Linie::ArmsSummary();
        check("ArmsSummary: one per followed name, in the specs' order", sum.size() == 2 &&
              sum[0].key == Linie::NameKey{ 0x77, 0, 7, 0 } && sum[1].key == Linie::NameKey{ 0x88, 0, 7, 0 },
              u(sum.size()).c_str());
        check("...the followed name: two distinct addresses (not four calls, not three readings), one arm, two matches "
              "the log could not take", sum.size() == 2 && sum[0].addresses == 2 && sum[0].arms == 1 &&
              sum[0].armsFull == 2 && sum[0].tick && sum[0].ring == 0, sum.empty() ? "" : u(sum[0].addresses).c_str());
        check("...a name never called: no address", sum.size() == 2 && sum[1].addresses == 0 && sum[1].arms == 0);
        Linie::Reset();
        check("...and none once the recording is gone", Linie::ArmsSummary().empty());

        // Review R4: two followed names taking turns at one address count it once each.
        static int32_t s_turn = 0x77;
        auto turnReader = [](uintptr_t, Linie::FuncIdentity& out) -> bool {
            out.nameIndex = s_turn; out.classIndex = 7; out.outer = 0x7000 + s_turn; out.functionFlags = 0x400;
            out.parmsSize = 16; return true;
        };
        auto turnKey = [](uintptr_t, int32_t& idx, int32_t& n, uint64_t& outer) -> bool {
            idx = s_turn; n = 0; outer = 0x7000 + s_turn; return true;
        };
        auto st2 = Linie::BuildArmState({ Linie::ArmSpec{ Linie::NameKey{ 0x77, 0, 7, 0 }, false, 0, 64 },
                                          Linie::ArmSpec{ Linie::NameKey{ 0x88, 0, 7, 0 }, false, 1, 64 } }, 8);
        st2->gen = 4;
        s_turn = 0x77;
        Linie::StartRecording(turnReader, turnKey, st2);
        Linie::RecordCall(0xB1, 2000, &h);
        s_turn = 0x88;
        Linie::RecordCall(0xB1, 2001, &h);
        s_turn = 0x77;
        Linie::RecordCall(0xB1, 2002, &h);
        const auto turns = Linie::ArmsSummary();
        check("two names taking turns at one address: one address each, three arms (review R4)",
              turns.size() == 2 && turns[0].addresses == 1 && turns[1].addresses == 1 && turns[0].arms == 2 &&
              turns[1].arms == 1, turns.size() == 2 ? (u(turns[0].addresses) + "/" + u(turns[1].addresses)).c_str() : "");
        Linie::Reset();
    }

    {
        blk("LIVEFUNCS-STEP2: arms handed to the background read, sealed at Stop, freed with the trace");
        // docs/live-funcs-step2-items.md, N4. Every 0xBx address is the followed function (a reload per address).
        auto reader = [](uintptr_t f, Linie::FuncIdentity& out) -> bool {
            out.nameIndex = ((f & 0xF0) == 0xB0) ? 0x77 : static_cast<int32_t>(f & 0xFFFF);
            out.classIndex = 7;
            out.outer = 0x7000 + f;
            out.functionFlags = 0x400;
            out.parmsSize = 16;
            return true;
        };
        auto keyReader = [](uintptr_t f, int32_t& idx, int32_t& n, uint64_t& outer) -> bool {
            idx = ((f & 0xF0) == 0xB0) ? 0x77 : static_cast<int32_t>(f & 0xFFFF);
            n = 0;
            outer = 0x7000 + f;
            return true;
        };
        auto u = [](uint64_t n) { return std::to_string(n); };
        auto build = [] { return Linie::BuildArmState({ Linie::ArmSpec{ Linie::NameKey{ 0x77, 0, 7, 0 }, false, 0, 64 } }, 8); };
        using LS = Linie::ArmLayoutState;

        Linie::Reset();
        Linie::FreeTrace();
        auto st = build();
        Linie::TraceConfig tc;
        tc.bytes = 64 * sizeof(Linie::TraceRecord);
        tc.arms = st;
        check("setup: a trace that follows names starts", Linie::StartTrace(tc) == Linie::TraceStartStatus::Ok);
        check("StartTrace stamps the names with its recording", st->gen != 0 && st->gen == Linie::GetTraceInfo().gen,
              u(st->gen).c_str());
        Linie::StartRecording(reader, keyReader, st);
        Linie::ArmHint h;
        Linie::RecordCall(0xB1, 1000, &h);
        Linie::RecordCall(0xB2, 1001, &h);
        const auto p1 = Linie::TakePendingArms(*st, 64);
        check("the arms made so far are handed out, in order",
              p1.size() == 2 && p1[0].index == 0 && p1[0].rec.addr == 0xB1 && p1[1].index == 1 && p1[1].rec.addr == 0xB2,
              u(p1.size()).c_str());
        check("...once each", Linie::TakePendingArms(*st, 64).empty());
        Linie::RecordCall(0xB3, 1002, &h);
        Linie::RecordCall(0xB4, 1003, &h);
        const auto p2 = Linie::TakePendingArms(*st, 1);
        check("...at most as many as asked, the oldest first", p2.size() == 1 && p2[0].index == 2, u(p2.size()).c_str());
        const auto p3 = Linie::TakePendingArms(*st, SIZE_MAX);   // review R2: "all of them", twice
        const auto p4 = Linie::TakePendingArms(*st, SIZE_MAX);
        check("...'all of them' takes the rest, and asked again takes nothing -- no wrap-around (review R2)",
              p3.size() == 1 && p3[0].index == 3 && p4.empty(), (u(p3.size()) + "/" + u(p4.size())).c_str());

        const auto lay = std::make_shared<int>(42);
        check("a layout published for an arm", Linie::PublishArmLayout(*st, 0, LS::Read, lay, {}, 15));
        check("...is not published twice", !Linie::PublishArmLayout(*st, 0, LS::Failed, nullptr, "again"));
        check("...nor for an arm the log does not hold", !Linie::PublishArmLayout(*st, 9, LS::Read, lay));
        check("...nor for a place the log has room for but no arm has taken yet",
              !Linie::PublishArmLayout(*st, 5, LS::Read, lay));

        Linie::StopTrace();
        Linie::RecordCall(0xB5, 1004, &h);
        check("once the trace has stopped, a new address is armed for no snapshots", h.ring == -1 && st->log.size() == 4,
              u(st->log.size()).c_str());

        Linie::SealArms(*st);
        check("sealed: a late publish does not land", !Linie::PublishArmLayout(*st, 1, LS::Read, lay));
        const auto v = Linie::CopyArms(*st);
        check("CopyArms: every arm, with what became of its layout",
              v.size() == 4 && v[0].state == LS::Read && v[0].layout == lay && v[0].readMs == 15 &&
              v[1].state == LS::NotReadBeforeStop && v[3].state == LS::NotReadBeforeStop && !v[1].layout &&
              v[2].rec.addr == 0xB3, u(v.size()).c_str());

        uint64_t armsGen = 0;
        auto held = Linie::TraceArms(&armsGen);
        check("the stopped trace hands its readers the names it follows, with its gen",
              held.get() == st.get() && armsGen == Linie::GetTraceInfo().gen && armsGen != 0);
        held.reset();   // a reader's copy, let go: the trace's own reference is what the next checks follow
        std::weak_ptr<Linie::ArmState> w = st;
        st.reset();
        tc.arms.reset();
        Linie::StopRecording();
        const uint64_t gen = Linie::GetTraceInfo().gen;
        check("freeing the trace lets go of the names -- the trace's and the recording's",
              Linie::FreeTraceIfGen(gen) && w.expired());
        check("...after which there are none to hand out", !Linie::TraceArms());

        // TR2: while a hook may still be inside, a Free changes nothing, the names included.
        static HANDLE s_in2  = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        static HANDLE s_out2 = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        static std::atomic<int> s_block2{ 0 };
        Linie::SetTraceClockForTest([]() -> uint64_t {
            if (s_block2.exchange(0) == 1) { SetEvent(s_in2); WaitForSingleObject(s_out2, INFINITE); }
            return 42;
        });
        auto st2 = build();
        std::weak_ptr<Linie::ArmState> w2 = st2;
        Linie::TraceConfig tc2;
        tc2.bytes = 64 * sizeof(Linie::TraceRecord);
        tc2.arms = std::move(st2);
        Linie::StartTrace(tc2);
        tc2.arms.reset();
        Linie::StartRecording(reader, keyReader, w2.lock());
        s_block2 = 1;
        std::thread stuck([] { Linie::TraceToken t; Linie::TraceEnter(0xF1, 0, 1000, 1, t); });
        WaitForSingleObject(s_in2, 5000);
        Linie::StopTrace();   // gives up after its wait
        Linie::StopRecording();
        const uint64_t gen2 = Linie::GetTraceInfo().gen;
        check("a Free while a hook may still be inside keeps the names", !Linie::FreeTraceIfGen(gen2) && !w2.expired());
        SetEvent(s_out2);
        stuck.join();
        Linie::StopTrace();
        check("...and lets them go once it has left", Linie::FreeTraceIfGen(gen2) && w2.expired());
        Linie::SetTraceClockForTest(nullptr);

        auto st3 = build();
        std::weak_ptr<Linie::ArmState> w3 = st3;
        Linie::StartRecording(reader, keyReader, std::move(st3));
        Linie::Reset();
        check("Reset lets go of the recording's names", w3.expired());
    }

    {
        blk("LIVEFUNCS-STEP2: the trace's scope by name -- a ticked name opens it, a waiting tick records nothing");
        // docs/live-funcs-step2-items.md, T1 (T10). The scope is the Start's: fixed from what was ASKED.
        auto u = [](uint64_t n) { return std::to_string(n); };
        Linie::Reset();
        Linie::FreeTrace();
        Linie::TraceConfig c;
        c.bytes = 64 * sizeof(Linie::TraceRecord);
        c.scoped = true;
        c.tickedNames = 1;
        check("setup: a trace scoped by one ticked name, no ticked address",
              Linie::StartTrace(c) == Linie::TraceStartStatus::Ok);
        const uint64_t gen = Linie::GetTraceInfo().gen;
        Linie::ArmHint tick{};
        tick.gen = gen;
        tick.flags = Linie::kArmTick;
        Linie::TraceToken a, b, d, e, f;
        Linie::TraceEnter(0xF1, 0, 1000, 1, e);            // nothing armed yet: the tick waits
        Linie::TraceEnter(0xF0, 0, 900, 1, a, 0, tick);    // its first call: armed by name
        Linie::TraceEnter(0xF2, 0, 800, 1, b);             // called inside it
        Linie::TraceReturn(b, 1);
        Linie::TraceReturn(a, 1);
        Linie::TraceEnter(0xF3, 0, 1000, 1, d);            // after it
        Linie::ArmHint old = tick;
        old.gen = gen - 1;
        Linie::TraceEnter(0xF0, 0, 900, 1, f, 0, old);     // armed, but by another recording's hint
        check("a call before its ticked name is called: nothing (not every call -- the tick waits)", !e.traced);
        check("the ticked name's call opens the scope", a.traced && a.opened);
        check("...the call inside it is traced", b.traced && !b.opened);
        check("...the one after it is not", !d.traced);
        check("a hint of another recording opens nothing", !f.traced);
        Linie::StopTrace();
        std::vector<Linie::TraceRecord> recs;
        check("the records: the root with its flag, then the nested call, then the two returns",
              Linie::CopyTrace(0, 64, recs) && recs.size() == 4 && recs[0].a == 0xF0 &&
              recs[0].flags == Linie::kTraceScopeRoot && recs[1].a == 0xF2, u(recs.size()).c_str());
        const Linie::TraceInfo info = Linie::GetTraceInfo();
        check("the info says what was asked: scoped, one ticked name, not snapshots-only",
              info.scoped && info.tickedNames == 1 && !info.snapOnly && info.ticked == 0);
        Linie::FreeTrace();

        Linie::TraceConfig legacy;
        legacy.bytes = 64 * sizeof(Linie::TraceRecord);
        legacy.ticked = { 0xA7 };
        Linie::StartTrace(legacy);
        check("ticked addresses scope a trace whatever `scoped` says", Linie::GetTraceInfo().scoped);
        Linie::TraceToken g;
        Linie::TraceEnter(0xF1, 0, 1000, 1, g);
        check("...so a call outside them is not traced", !g.traced);
        Linie::FreeTrace();
        Linie::TraceConfig every;
        every.bytes = 64 * sizeof(Linie::TraceRecord);
        Linie::StartTrace(every);
        Linie::TraceToken k;
        Linie::TraceEnter(0xF1, 0, 1000, 1, k);
        check("nothing asked: every call, as before", !Linie::GetTraceInfo().scoped && k.traced);
        Linie::FreeTrace();
    }

    {
        blk("LIVEFUNCS-STEP2: the snapshot rings -- one per choice in one allocation, the same K, freed with the trace");
        // docs/live-funcs-step2-items.md, S1 (T12). Two rings of 16 and 40 bytes: slots of 24 + 16 and 24 + 40, each
        // ring 64-aligned. K = (bytes - 64 * rings) / (40 + 64).
        auto u = [](uint64_t n) { return std::to_string(n); };
        auto cfg = [](uint64_t snapBytes) {
            Linie::TraceConfig c;
            c.bytes = 64 * sizeof(Linie::TraceRecord);
            c.snapRingCaps = { 16, 40 };
            c.snapBytes = snapBytes;
            return c;
        };
        Linie::Reset();
        Linie::FreeTrace();
        check("bytes for K = 8 exactly: the trace starts", Linie::StartTrace(cfg(128 + 8 * 104)) == Linie::TraceStartStatus::Ok);
        Linie::TraceInfo info = Linie::GetTraceInfo();
        check("...the rings are there, keeping 8 calls each",
              info.snap.allocated && info.snap.rings == 2 && info.snap.slotsPerRing == 8 && info.snap.bytes > 0,
              u(info.snap.slotsPerRing).c_str());
        std::vector<Linie::SnapRingInfo> rings;
        check("SnapRings is refused while the trace runs", !Linie::SnapRings(rings));
        Linie::StopTrace();
        uint64_t rgen = 0;
        check("...and after Stop gives each ring, in order, with nothing written yet",
              Linie::SnapRings(rings, &rgen) && rings.size() == 2 && rings[0].index == 0 && rings[0].cap == 16 &&
              rings[1].cap == 40 && rings[0].written == 0 && rgen == info.gen, u(rings.size()).c_str());

        check("one byte short of K = 8: refused as a snapshot buffer too small",
              Linie::StartTrace(cfg(128 + 8 * 104 - 1)) == Linie::TraceStartStatus::SnapTooSmall);
        check("...and the trace's own ring is freed too", !Linie::GetTraceInfo().allocated && !Linie::IsTracing());
        {
            // The Linie review (tests): "freed" measured, not inferred -- the refused Start's own 256 MB ring is a local
            // that the trace's state never saw. The process's committed private bytes come back.
            auto privateBytes = [] {
                PROCESS_MEMORY_COUNTERS_EX pmc{};
                pmc.cb = sizeof(pmc);
                K32GetProcessMemoryInfo(GetCurrentProcess(), reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&pmc), sizeof(pmc));
                return static_cast<uint64_t>(pmc.PrivateUsage);
            };
            Linie::TraceConfig big = cfg(100);   // a snapshot buffer smaller than the rings' alignment
            big.bytes = 256ull << 20;
            const uint64_t before = privateBytes();
            const bool refused = Linie::StartTrace(big) == Linie::TraceStartStatus::SnapTooSmall;
            const uint64_t after = privateBytes();
            check("...measured: a refused Start leaves no 256 MB ring committed behind it",
                  refused && after < before + (16ull << 20), (u((after - before) >> 20) + " MB").c_str());
        }
        check("a buffer smaller than the rings' alignment: refused, not a wrapped-around K",
              Linie::StartTrace(cfg(100)) == Linie::TraceStartStatus::SnapTooSmall && !Linie::GetTraceInfo().allocated);
        Linie::TraceConfig none = cfg(0);
        none.snapRingCaps.clear();
        Linie::StartTrace(none);
        check("no choices: no snapshot rings", Linie::GetTraceInfo().allocated && !Linie::GetTraceInfo().snap.allocated);

        auto startStop = [&] { Linie::StartTrace(cfg(4096)); Linie::StopTrace(); return Linie::GetTraceInfo().snap.allocated; };
        check("FreeTrace frees the rings", startStop() && (Linie::FreeTrace(), !Linie::GetTraceInfo().snap.allocated));
        check("FreeTraceIfGen frees them", startStop() &&
              Linie::FreeTraceIfGen(Linie::GetTraceInfo().gen) && !Linie::GetTraceInfo().snap.allocated);
        check("ReleaseIfEmpty frees them", startStop() && Linie::ReleaseIfEmpty() && !Linie::GetTraceInfo().snap.allocated);
        check("Reset frees them", startStop() && (Linie::Reset(), !Linie::GetTraceInfo().snap.allocated));

        // TR2: while a hook may still be inside, a Free changes nothing, the rings included.
        static HANDLE s_in3  = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        static HANDLE s_out3 = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        static std::atomic<int> s_block3{ 0 };
        Linie::SetTraceClockForTest([]() -> uint64_t {
            if (s_block3.exchange(0) == 1) { SetEvent(s_in3); WaitForSingleObject(s_out3, INFINITE); }
            return 42;
        });
        Linie::StartTrace(cfg(4096));
        s_block3 = 1;
        std::thread stuck([] { Linie::TraceToken t; Linie::TraceEnter(0xF1, 0, 1000, 1, t); });
        WaitForSingleObject(s_in3, 5000);
        Linie::StopTrace();
        Linie::FreeTrace();
        check("a hook that may still be inside: the rings stay, SnapRings is refused, a Start is busy",
              Linie::GetTraceInfo().snap.allocated && !Linie::SnapRings(rings) &&
              Linie::StartTrace(cfg(4096)) == Linie::TraceStartStatus::Busy);
        SetEvent(s_out3);
        stuck.join();
        Linie::StopTrace();
        Linie::FreeTrace();
        check("...and go once it has left", !Linie::GetTraceInfo().snap.allocated);
        Linie::SetTraceClockForTest(nullptr);
    }

    {
        blk("LIVEFUNCS-STEP2: the entry copy -- a chosen call's parameters into its ring, the entry record flagged");
        // docs/live-funcs-step2-items.md, S2. A copier that is plain memcpy stands in for Macht's.
        auto u = [](uint64_t n) { return std::to_string(n); };
        Linie::Reset();
        Linie::FreeTrace();
        Linie::TraceConfig c;
        c.bytes = 64 * sizeof(Linie::TraceRecord);
        c.snapRingCaps = { 64, 16 };
        c.snapBytes = 64 * 1024;
        c.copier = [](uintptr_t src, void* dst, size_t n) -> bool { memcpy(dst, reinterpret_cast<const void*>(src), n); return true; };
        Linie::StartTrace(c);
        const uint64_t gen = Linie::GetTraceInfo().gen;
        uint8_t buf[128];
        for (int i = 0; i < 128; ++i) buf[i] = static_cast<uint8_t>(i * 3 + 1);
        const uintptr_t params = reinterpret_cast<uintptr_t>(buf);
        Linie::ArmHint h{};
        h.gen = gen; h.ring = 0; h.arm = 3; h.copy = 16;
        Linie::TraceToken t0, t1, t2, t3;
        Linie::TraceEnter(0xF0, 0xB0, 1000, 1, t0);                 // not chosen
        Linie::TraceEnter(0xF1, 0xB1, 1000, 1, t1, params, h);       // chosen: ring 0, arm 3, 16 bytes
        Linie::ArmHint bad = h;
        bad.ring = 5;                                                 // a ring the trace does not have
        Linie::TraceEnter(0xF2, 0xB2, 1000, 1, t2, params, bad);
        Linie::ArmHint big = h;
        big.ring = 1; big.copy = 100;                                 // more than ring 1's 16-byte slot
        Linie::TraceEnter(0xF3, 0xB3, 1000, 1, t3, params, big);
        Linie::StopTrace();
        std::vector<Linie::TraceRecord> recs;
        Linie::CopyTrace(0, 64, recs);
        check("the chosen call's entry record says its parameters were taken; the others do not",
              recs.size() == 4 && recs[0].flags == 0 && (recs[1].flags & Linie::kTraceSnapTaken) &&
              !(recs[2].flags & Linie::kTraceSnapTaken) && (recs[3].flags & Linie::kTraceSnapTaken), u(recs.size()).c_str());
        std::vector<Linie::SnapCopy> s0, s1;
        uint64_t next = 0;
        check("ring 0 holds one slot: the call's entry sequence number, its arm, the 16 bytes of its block",
              Linie::CopySnaps(0, 0, 10, s0, &next) && s0.size() == 1 && s0[0].index == 0 && s0[0].entrySeq == 1 &&
              s0[0].arm == 3 && !s0[0].after && s0[0].len == 16 && s0[0].flags == 0 && s0[0].bytes.size() == 16 &&
              memcmp(s0[0].bytes.data(), buf, 16) == 0 && next == 1, u(s0.size()).c_str());
        check("a ring the trace does not have takes nothing", recs.size() == 4 && t2.traced);
        check("a copy larger than its ring's slot is cut to the slot",
              Linie::CopySnaps(1, 0, 10, s1) && s1.size() == 1 && s1[0].len == 16 && s1[0].entrySeq == 3 &&
              memcmp(s1[0].bytes.data(), buf, 16) == 0, s1.empty() ? "" : u(s1[0].len).c_str());
        Linie::FreeTrace();

        Linie::TraceConfig plain;
        plain.bytes = 64 * sizeof(Linie::TraceRecord);
        Linie::StartTrace(plain);
        Linie::ArmHint h2 = h;
        h2.gen = Linie::GetTraceInfo().gen;
        Linie::TraceToken t4;
        Linie::TraceEnter(0xF1, 0xB1, 1000, 1, t4, params, h2);
        Linie::StopTrace();
        recs.clear();
        std::vector<Linie::SnapCopy> none;
        check("a trace without snapshot rings: the record is step 1's, and there is nothing to copy",
              Linie::CopyTrace(0, 64, recs) && recs.size() == 1 && recs[0].flags == 0 && !Linie::CopySnaps(0, 0, 10, none));
        Linie::FreeTrace();
    }

    {
        blk("LIVEFUNCS-STEP2: the copy after the call -- its own slot, the same entry, only when owed, only this recording");
        // docs/live-funcs-step2-items.md, S3 (TR3: never a write-back).
        auto u = [](uint64_t n) { return std::to_string(n); };
        auto memcpyCopier = [](uintptr_t src, void* dst, size_t n) -> bool {
            memcpy(dst, reinterpret_cast<const void*>(src), n); return true;
        };
        auto start = [&] {
            Linie::TraceConfig c;
            c.bytes = 64 * sizeof(Linie::TraceRecord);
            c.snapRingCaps = { 16 };
            c.snapBytes = 64 * 1024;
            c.copier = memcpyCopier;
            Linie::StartTrace(c);
            return Linie::GetTraceInfo().gen;
        };
        Linie::Reset();
        Linie::FreeTrace();
        uint8_t buf[16];
        memset(buf, 0x11, sizeof buf);
        const uintptr_t params = reinterpret_cast<uintptr_t>(buf);
        uint64_t gen = start();
        Linie::ArmHint h{};
        h.gen = gen; h.ring = 0; h.arm = 2; h.copy = 16; h.flags = Linie::kArmAfter;
        Linie::TraceToken t;
        Linie::TraceEnter(0xF1, 0, 1000, 1, t, params, h);
        memset(buf, 0x22, sizeof buf);                     // the call writes its out parameters and its return
        Linie::TraceReturn(t, 1, params);
        Linie::ArmHint noAfter = h;
        noAfter.flags = 0;
        Linie::TraceToken t2;
        Linie::TraceEnter(0xF1, 0, 1000, 1, t2, params, noAfter);
        Linie::TraceReturn(t2, 1, params);
        Linie::StopTrace();
        std::vector<Linie::SnapCopy> s;
        Linie::CopySnaps(0, 0, 10, s);
        check("three slots: the call's two copies, then the second call's entry copy only",
              s.size() == 3 && !s[0].after && s[1].after && !s[2].after, u(s.size()).c_str());
        if (s.size() == 3) {
            check("the entry copy holds the block as the call began, the after copy as it returned",
                  s[0].bytes.size() == 16 && s[0].bytes[0] == 0x11 && s[1].bytes.size() == 16 && s[1].bytes[0] == 0x22);
            check("...both carry the call's entry sequence number and its arm",
                  s[0].entrySeq == 0 && s[1].entrySeq == 0 && s[1].arm == 2 && s[2].entrySeq == 2,
                  u(s[1].entrySeq).c_str());
        }

        // A call that entered under the last recording returns under a new one: nothing lands in the new rings.
        gen = start();
        h.gen = gen;
        Linie::TraceToken t3;
        Linie::TraceEnter(0xF1, 0, 1000, 1, t3, params, h);
        Linie::StopTrace();
        start();
        Linie::TraceReturn(t3, 1, params);
        Linie::StopTrace();
        std::vector<Linie::SnapCopy> s2;
        Linie::CopySnaps(0, 0, 10, s2);
        check("a return after Stop and a new Start writes no after copy into the new rings", s2.empty(),
              u(s2.size()).c_str());
        Linie::FreeTrace();
    }

    {
        blk("LIVEFUNCS-STEP2: a chosen call the scope would not record is recorded alone; snapshots-only; excluded kept");
        // docs/live-funcs-step2-items.md, S4 (T11). A1 is ticked by name, A7 chosen (ring 0).
        auto u = [](uint64_t n) { return std::to_string(n); };
        uint8_t buf[8] = { 1, 2, 3, 4, 5, 6, 7, 8 };
        const uintptr_t params = reinterpret_cast<uintptr_t>(buf);
        auto start = [](bool ticks, std::vector<uintptr_t> exclude) {
            Linie::TraceConfig c;
            c.bytes = 64 * sizeof(Linie::TraceRecord);
            c.scoped = true;
            c.tickedNames = ticks ? 1 : 0;
            c.snapOnly = !ticks;
            c.exclude = std::move(exclude);
            c.snapRingCaps = { 8 };
            c.snapBytes = 64 * 1024;
            c.copier = [](uintptr_t src, void* dst, size_t n) -> bool { memcpy(dst, reinterpret_cast<const void*>(src), n); return true; };
            Linie::StartTrace(c);
            return Linie::GetTraceInfo().gen;
        };
        auto copyAll = [] { std::vector<Linie::TraceRecord> r; Linie::CopyTrace(0, 64, r); return r; };
        Linie::Reset();
        Linie::FreeTrace();

        uint64_t gen = start(true, {});
        Linie::ArmHint tick{}, chosen{};
        tick.gen = gen; tick.flags = Linie::kArmTick;
        chosen.gen = gen; chosen.ring = 0; chosen.copy = 8;
        Linie::TraceToken a, b, c1, c2, d;
        Linie::TraceEnter(0xA7, 0, 900, 1, a, params, chosen);   // outside any scope
        Linie::TraceEnter(0xF2, 0, 800, 1, b);                   // called by it
        Linie::TraceReturn(b, 1);
        Linie::TraceReturn(a, 1);
        Linie::TraceEnter(0xA1, 0, 900, 1, c1, 0, tick);         // the tick opens its scope
        Linie::TraceEnter(0xA7, 0, 800, 1, c2, params, chosen);  // the chosen one inside it
        Linie::TraceReturn(c2, 1);
        Linie::TraceReturn(c1, 1);
        Linie::TraceEnter(0xF3, 0, 900, 1, d);                   // outside again, not chosen
        Linie::StopTrace();
        auto r = copyAll();
        check("outside every scope the chosen call is recorded alone, its parameters taken",
              a.traced && !a.opened && r.size() >= 1 && r[0].a == 0xA7 &&
              r[0].flags == (Linie::kTraceSnapTaken | Linie::kTraceSnapLone), u(r.empty() ? 0 : r[0].flags).c_str());
        check("...and opens no scope: the call it makes is not recorded", !b.traced);
        check("inside the tick's scope it is an ordinary traced call: not lone",
              c2.traced && r.size() == 6 && r[3].a == 0xA7 && r[3].flags == Linie::kTraceSnapTaken, u(r.size()).c_str());
        check("an unchosen call outside the scope is not recorded", !d.traced);

        gen = start(false, {});
        chosen.gen = gen;
        Linie::TraceToken e, f;
        Linie::TraceEnter(0xF1, 0, 900, 1, e);
        Linie::TraceEnter(0xA7, 0, 900, 1, f, params, chosen);
        Linie::TraceReturn(f, 1);
        Linie::StopTrace();
        r = copyAll();
        check("snapshots-only (chosen, nothing ticked): an unchosen call is not recorded, the chosen one alone",
              !e.traced && f.traced && r.size() == 2 && r[0].a == 0xA7 && (r[0].flags & Linie::kTraceSnapLone) &&
              Linie::GetTraceInfo().snapOnly, u(r.size()).c_str());

        gen = start(true, { 0xA7, 0xA9 });
        tick.gen = gen; chosen.gen = gen;
        Linie::TraceToken g1, g2, g3, g4;
        Linie::TraceEnter(0xA1, 0, 900, 1, g1, 0, tick);
        Linie::TraceEnter(0xA7, 0, 800, 1, g2, params, chosen);   // excluded per-frame, but chosen
        Linie::TraceEnter(0xF2, 0, 700, 1, g3);                   // what it calls
        Linie::TraceReturn(g3, 1);
        Linie::TraceReturn(g2, 1);
        Linie::TraceEnter(0xA9, 0, 800, 1, g4);                   // excluded, not chosen
        Linie::TraceReturn(g1, 1);
        Linie::StopTrace();
        r = copyAll();
        check("an excluded function that is chosen is kept inside the scope, flagged excluded -- not lone",
              g2.traced && r.size() >= 2 && r[1].a == 0xA7 &&
              r[1].flags == (Linie::kTraceSnapTaken | Linie::kTraceSnapExcluded), u(r.size() >= 2 ? r[1].flags : 0).c_str());
        check("...and what it calls is still traced (the scope is open)", g3.traced);
        check("an excluded function that is not chosen is left out, as before", !g4.traced);
        Linie::FreeTrace();
    }

    {
        blk("LIVEFUNCS-STEP2: the budget -- per ring and in all, per second, the first calls of each second kept");
        // docs/live-funcs-step2-items.md, S5 (T9 item 3: the budget is the guarantee). A clock that is set, in
        // QueryPerformanceCounter ticks: the budget's second is ticks / the frequency the trace reports.
        static uint64_t s_now = 0;
        Linie::SetTraceClockForTest([]() -> uint64_t { return s_now; });
        auto u = [](uint64_t n) { return std::to_string(n); };
        uint8_t buf[8] = {};
        const uintptr_t params = reinterpret_cast<uintptr_t>(buf);
        auto start = [](uint32_t perRing, uint32_t total, bool scoped) {
            Linie::TraceConfig c;
            c.bytes = 256 * sizeof(Linie::TraceRecord);
            c.scoped = scoped;
            c.snapOnly = scoped;
            c.snapRingCaps = { 8, 8 };
            c.snapBytes = 64 * 1024;
            c.snapPerRingPerSec = perRing;
            c.snapTotalPerSec = total;
            c.copier = [](uintptr_t src, void* dst, size_t n) -> bool { memcpy(dst, reinterpret_cast<const void*>(src), n); return true; };
            Linie::StartTrace(c);
            return Linie::GetTraceInfo();
        };
        auto hintFor = [](uint64_t gen, int32_t ring) { Linie::ArmHint h{}; h.gen = gen; h.ring = ring; h.copy = 8; return h; };
        Linie::Reset();
        Linie::FreeTrace();

        Linie::TraceInfo info = start(3, 100, false);
        const uint64_t f = info.qpcFreq;
        s_now = 10 * f;                                    // second 10
        for (int i = 0; i < 5; ++i) { Linie::TraceToken t; Linie::TraceEnter(0xA7, 0, 1000, 1, t, params, hintFor(info.gen, 0)); }
        s_now = 11 * f + 5;                                // second 11
        { Linie::TraceToken t; Linie::TraceEnter(0xA7, 0, 1000, 1, t, params, hintFor(info.gen, 0)); }
        Linie::StopTrace();
        std::vector<Linie::TraceRecord> recs;
        Linie::CopyTrace(0, 256, recs);
        int taken = 0, budget = 0;
        for (const auto& r : recs) {
            if (r.flags & Linie::kTraceSnapTaken) ++taken;
            if (r.flags & Linie::kTraceSnapBudget) ++budget;
        }
        std::vector<Linie::SnapCopy> s;
        Linie::CopySnaps(0, 0, 64, s);
        check("a budget of 3 a second: five calls in one second, three taken, two recorded over the budget",
              recs.size() == 6 && taken == 4 && budget == 2, (u(taken) + "/" + u(budget)).c_str());
        check("...the next second admits again", s.size() == 4 && recs.size() == 6 &&
              (recs[5].flags & Linie::kTraceSnapTaken), u(s.size()).c_str());
        info = Linie::GetTraceInfo();
        check("...and the info counts the two over the budget", info.snap.skippedBudget == 2 && info.snap.droppedBudget == 0 &&
              info.snap.perRingPerSec == 3 && info.snap.totalPerSec == 100, u(info.snap.skippedBudget).c_str());

        info = start(100, 4, false);
        s_now = 20 * f;
        for (int i = 0; i < 3; ++i) {
            Linie::TraceToken t0, t1;
            Linie::TraceEnter(0xA7, 0, 1000, 1, t0, params, hintFor(info.gen, 0));
            Linie::TraceEnter(0xA8, 0, 1000, 1, t1, params, hintFor(info.gen, 1));
        }
        Linie::StopTrace();
        std::vector<Linie::SnapCopy> r0, r1;
        Linie::CopySnaps(0, 0, 64, r0);
        Linie::CopySnaps(1, 0, 64, r1);
        check("a total of 4 a second caps the two rings together", r0.size() + r1.size() == 4,
              u(r0.size() + r1.size()).c_str());

        info = start(1, 100, true);                        // snapshots-only: every chosen call is lone
        s_now = 30 * f;
        Linie::TraceToken l0, l1;
        Linie::TraceEnter(0xA7, 0, 1000, 1, l0, params, hintFor(info.gen, 0));
        Linie::TraceEnter(0xA7, 0, 1000, 1, l1, params, hintFor(info.gen, 0));
        Linie::StopTrace();
        info = Linie::GetTraceInfo();
        check("a lone call over the budget writes no record at all, and is counted",
              l0.traced && !l1.traced && info.written == 1 && info.snap.droppedBudget == 1 && info.snap.skippedBudget == 0,
              u(info.written).c_str());

        // The Linie review (tests): a call over the budget owes no after copy -- the ring would fill at the full rate.
        info = start(1, 100, false);
        s_now = 40 * f;
        Linie::ArmHint owing = hintFor(info.gen, 0);
        owing.flags = Linie::kArmAfter;
        for (int i = 0; i < 2; ++i) {
            Linie::TraceToken t;
            Linie::TraceEnter(0xA7, 0, 1000, 1, t, params, owing);
            Linie::TraceReturn(t, 1, params);
        }
        Linie::StopTrace();
        std::vector<Linie::SnapCopy> owed;
        Linie::CopySnaps(0, 0, 64, owed);
        check("an after-copy call over the budget writes no after copy: the first call's two slots only",
              owed.size() == 2 && !owed[0].after && owed[1].after && owed[0].entrySeq == owed[1].entrySeq,
              u(owed.size()).c_str());

        // ...and an excluded choice over the budget writes nothing, as a lone one does (T5 b keeps the ring clear).
        {
            Linie::TraceConfig c;
            c.bytes = 256 * sizeof(Linie::TraceRecord);
            c.scoped = true;
            c.tickedNames = 1;
            c.exclude = { 0xA7 };
            c.snapRingCaps = { 8, 8 };
            c.snapBytes = 64 * 1024;
            c.snapPerRingPerSec = 1;
            c.copier = [](uintptr_t src, void* dst, size_t n) -> bool { memcpy(dst, reinterpret_cast<const void*>(src), n); return true; };
            Linie::StartTrace(c);
            info = Linie::GetTraceInfo();
            s_now = 50 * f;
            Linie::ArmHint tick{};
            tick.gen = info.gen; tick.flags = Linie::kArmTick;
            Linie::TraceToken root, x1, x2;
            Linie::TraceEnter(0xA1, 0, 900, 1, root, 0, tick);
            Linie::TraceEnter(0xA7, 0, 800, 1, x1, params, hintFor(info.gen, 0));
            Linie::TraceReturn(x1, 1, params);
            Linie::TraceEnter(0xA7, 0, 800, 1, x2, params, hintFor(info.gen, 0));
            Linie::StopTrace();
            info = Linie::GetTraceInfo();
            check("an excluded choice over the budget: no record at all, counted dropped",
                  x1.traced && !x2.traced && info.snap.droppedBudget == 1 && info.snap.skippedBudget == 0,
                  u(info.snap.droppedBudget).c_str());
        }
        Linie::FreeTrace();
        Linie::SetTraceClockForTest(nullptr);
    }

    {
        blk("LIVEFUNCS-STEP2: a copy that cannot be made -- no block, a fault, a cut -- and TR2 with the copier inside");
        // docs/live-funcs-step2-items.md, S6. The copier is the step-2 work that runs inside the hook's in-flight
        // section; a copier that blocks or throws stands in for a fault in the game's memory.
        static int      s_mode = 0;   // 0 memcpy, 1 fails, 2 blocks once, 3 throws once
        static HANDLE   s_in   = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        static HANDLE   s_out  = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        auto copier = [](uintptr_t src, void* dst, size_t n) -> bool {
            const int m = s_mode;
            if (m == 1) return false;
            if (m == 2) { s_mode = 0; SetEvent(s_in); WaitForSingleObject(s_out, INFINITE); }
            if (m == 3) { s_mode = 0; throw std::runtime_error("copy fault"); }
            memcpy(dst, reinterpret_cast<const void*>(src), n);
            return true;
        };
        auto u = [](uint64_t n) { return std::to_string(n); };
        uint8_t buf[64] = {};
        const uintptr_t params = reinterpret_cast<uintptr_t>(buf);
        auto start = [&] {
            Linie::TraceConfig c;
            c.bytes = 64 * sizeof(Linie::TraceRecord);
            c.snapRingCaps = { 16 };
            c.snapBytes = 64 * 1024;
            c.copier = copier;
            Linie::StartTrace(c);
            return Linie::GetTraceInfo().gen;
        };
        auto hint = [](uint64_t gen, uint8_t flags = 0) {
            Linie::ArmHint h{}; h.gen = gen; h.ring = 0; h.copy = 16; h.flags = flags; return h;
        };
        Linie::Reset();
        Linie::FreeTrace();

        s_mode = 0;
        uint64_t gen = start();
        Linie::TraceToken a, b, c;
        Linie::TraceEnter(0xF1, 0, 1000, 1, a, 0, hint(gen));                              // no parameter block
        s_mode = 1;
        Linie::TraceEnter(0xF1, 0, 1000, 1, b, params, hint(gen));                         // the copy faults
        s_mode = 0;
        Linie::TraceEnter(0xF1, 0, 1000, 1, c, params, hint(gen, Linie::kArmTruncated));   // larger than its slot
        const ULONGLONG t0 = GetTickCount64();
        Linie::StopTrace();
        const ULONGLONG stopMs = GetTickCount64() - t0;
        std::vector<Linie::SnapCopy> s;
        Linie::CopySnaps(0, 0, 10, s);
        check("no parameter block: the slot says so, and holds nothing",
              s.size() == 3 && s[0].flags == Linie::kSnapNullParams && s[0].len == 0 && s[0].bytes.empty(), u(s.size()).c_str());
        check("a copy that faults: the slot says so, and holds nothing",
              s.size() == 3 && s[1].flags == Linie::kSnapCopyFault && s[1].len == 0);
        check("a block larger than the slot: the slot says it was cut, and holds what fits",
              s.size() == 3 && s[2].flags == Linie::kSnapTruncated && s[2].len == 16);
        check("...and a faulting copy leaves Stop nothing to wait for", Linie::GetTraceInfo().quiesced && stopMs < 1000,
              u(stopMs).c_str());

        // A copier that blocks holds the hook inside its section: Stop waits for it.
        gen = start();
        ResetEvent(s_in); ResetEvent(s_out);
        s_mode = 2;
        std::thread writer([&] { Linie::TraceToken t; Linie::TraceEnter(0xF1, 0, 1000, 1, t, params, hint(gen)); });
        WaitForSingleObject(s_in, 5000);
        std::atomic<bool> stopped{ false };
        std::thread stopper([&stopped] { Linie::StopTrace(); stopped = true; });
        Sleep(100);
        const bool waited = !stopped.load();
        SetEvent(s_out);
        writer.join();
        stopper.join();
        check("Stop waits while a copy is under way", waited && stopped.load() && Linie::GetTraceInfo().quiesced);

        // ...and when it never leaves, the rings are neither read nor freed, and a Start is busy.
        gen = start();
        ResetEvent(s_in); ResetEvent(s_out);
        s_mode = 2;
        std::thread stuck([&] { Linie::TraceToken t; Linie::TraceEnter(0xF1, 0, 1000, 1, t, params, hint(gen)); });
        WaitForSingleObject(s_in, 5000);
        Linie::StopTrace();
        std::vector<Linie::SnapCopy> none;
        std::vector<Linie::SnapRingInfo> rings;
        Linie::FreeTrace();
        check("a copy that never ends: the rings are not read, not freed, and a Start is busy",
              !Linie::CopySnaps(0, 0, 10, none) && !Linie::SnapRings(rings) && Linie::GetTraceInfo().snap.allocated &&
              Linie::StartTrace(Linie::TraceConfig{}) == Linie::TraceStartStatus::Busy);
        SetEvent(s_out);
        stuck.join();
        Linie::StopTrace();
        check("...once it has left, they are read again", Linie::CopySnaps(0, 0, 10, none) && none.size() == 1,
              u(none.size()).c_str());

        // A copier that throws (under the DLL's /EHa an SEH fault unwinds the same way) leaves no count behind.
        gen = start();
        s_mode = 3;
        bool threw = false;
        try { Linie::TraceToken t; Linie::TraceEnter(0xF1, 0, 1000, 1, t, params, hint(gen)); }
        catch (const std::exception&) { threw = true; }
        const ULONGLONG t1 = GetTickCount64();
        Linie::StopTrace();
        const ULONGLONG stop2 = GetTickCount64() - t1;
        check("a copy that throws: Stop quiesces at once", threw && Linie::GetTraceInfo().quiesced && stop2 < 1000,
              u(stop2).c_str());
        Linie::FreeTrace();

        // Review (the Linie review, tr2): a copy that throws mid-call leaves a coherent trace. A ticked, chosen call
        // opens its scope and its copy throws: its record says what it is without "taken", the token still owes the
        // return (which closes the scope), and its slot -- the ring's first, on the first lap -- is not handed out as
        // a copy that worked.
        {
            Linie::TraceConfig c;
            c.bytes = 64 * sizeof(Linie::TraceRecord);
            c.scoped = true;
            c.tickedNames = 1;
            c.snapRingCaps = { 16 };
            c.snapBytes = 64 * 1024;
            c.copier = copier;
            Linie::StartTrace(c);
            Linie::ArmHint th = hint(Linie::GetTraceInfo().gen);
            th.flags = Linie::kArmTick;
            s_mode = 3;
            Linie::TraceToken root;
            bool threw2 = false;
            try { Linie::TraceEnter(0xA1, 0, 900, 1, root, params, th); } catch (const std::exception&) { threw2 = true; }
            check("a throwing copy: the token still owes the return and closes the scope",
                  threw2 && root.traced && root.opened);
            Linie::TraceReturn(root, 1, params);
            Linie::TraceToken after;
            Linie::TraceEnter(0xF2, 0, 800, 1, after);
            check("...so a call after it, deeper on the stack, is not taken for its callee", !after.traced);
            Linie::StopTrace();
            std::vector<Linie::TraceRecord> rr;
            Linie::CopyTrace(0, 64, rr);
            check("...its record says root, without 'taken'", rr.size() == 2 && rr[0].flags == Linie::kTraceScopeRoot,
                  rr.empty() ? "" : u(rr[0].flags).c_str());
            std::vector<Linie::SnapCopy> ss;
            Linie::CopySnaps(0, 0, 10, ss);
            check("...and its unfinished slot is not handed out as a copy", ss.empty(), u(ss.size()).c_str());
            Linie::FreeTrace();
        }
        s_mode = 0;
    }

    {
        blk("LIVEFUNCS-STEP2: the rings' windows -- a busy ring laps only itself; orphans left out; paging");
        // docs/live-funcs-step2-items.md, S7. K = 8 for both rings: 64 * 2 + 8 * (24 + 8) * 2 bytes.
        auto u = [](uint64_t n) { return std::to_string(n); };
        uint8_t buf[8] = {};
        const uintptr_t params = reinterpret_cast<uintptr_t>(buf);
        auto start = [](uint64_t records, uint64_t snapBytes) {
            Linie::TraceConfig c;
            c.bytes = records * sizeof(Linie::TraceRecord);
            c.snapRingCaps = { 8, 8 };
            c.snapBytes = snapBytes;
            c.copier = [](uintptr_t src, void* dst, size_t n) -> bool { memcpy(dst, reinterpret_cast<const void*>(src), n); return true; };
            Linie::StartTrace(c);
            return Linie::GetTraceInfo().gen;
        };
        auto hint = [](uint64_t gen, int32_t ring) { Linie::ArmHint h{}; h.gen = gen; h.ring = ring; h.copy = 8; return h; };
        Linie::Reset();
        Linie::FreeTrace();

        uint64_t gen = start(256, 128 + 8 * 32 * 2);
        check("setup: K = 8", Linie::GetTraceInfo().snap.slotsPerRing == 8, u(Linie::GetTraceInfo().snap.slotsPerRing).c_str());
        for (int i = 0; i < 3; ++i) { Linie::TraceToken t; Linie::TraceEnter(0xA2, 0, 1000, 1, t, params, hint(gen, 1)); }
        for (int i = 0; i < 20; ++i) { Linie::TraceToken t; Linie::TraceEnter(0xA1, 0, 1000, 1, t, params, hint(gen, 0)); }
        Linie::StopTrace();
        std::vector<Linie::SnapRingInfo> rings;
        Linie::SnapRings(rings);
        check("the busy ring kept its last 8 of 20; the rare one all 3 of its own",
              rings.size() == 2 && rings[0].written == 20 && rings[0].firstValid == 12 && rings[1].written == 3 &&
              rings[1].firstValid == 0, rings.size() == 2 ? (u(rings[0].firstValid) + "/" + u(rings[1].firstValid)).c_str() : "");
        std::vector<Linie::SnapCopy> busy, rare;
        uint64_t orphans = 99;
        check("...the rare ring's three, untouched by the busy one",
              Linie::CopySnaps(1, 0, 64, rare, nullptr, &orphans) && rare.size() == 3 && rare[0].entrySeq == 0 &&
              rare[2].entrySeq == 2 && orphans == 0, u(rare.size()).c_str());
        check("...the busy ring's window, in order", Linie::CopySnaps(0, 0, 64, busy) && busy.size() == 8 &&
              busy[0].index == 12 && busy[7].index == 19 && busy[0].entrySeq == 15, u(busy.size()).c_str());
        uint64_t next = 0;
        busy.clear();
        check("a page asked from before the window does not reach into it, and the next page is the window's start",
              Linie::CopySnaps(0, 0, 3, busy, &next) && busy.empty() && next == 12, u(next).c_str());
        check("...a page inside it, and the page after", Linie::CopySnaps(0, 12, 3, busy, &next) && busy.size() == 3 &&
              busy[0].index == 12 && next == 15, u(next).c_str());
        Linie::FreeTrace();

        // The trace's ring laps the calls' entry records while the snapshot ring still keeps their slots.
        gen = start(8, 128 + 32 * 32 * 2);                  // K = 32 > the 8 records the trace keeps
        for (int i = 0; i < 20; ++i) { Linie::TraceToken t; Linie::TraceEnter(0xA1, 0, 1000, 1, t, params, hint(gen, 0)); }
        Linie::StopTrace();
        std::vector<Linie::SnapCopy> kept;
        orphans = 0;
        check("a slot whose call the trace no longer keeps is left out, and counted an orphan",
              Linie::CopySnaps(0, 0, 64, kept, nullptr, &orphans) && kept.size() == 8 && orphans == 12 &&
              kept[0].entrySeq == 12 && Linie::GetTraceInfo().firstValid == 12, (u(kept.size()) + "/" + u(orphans)).c_str());
        Linie::FreeTrace();

        // The Linie review (tests): a write that never finished is left out by its number. Eight whole writes fill
        // K = 8; the ninth takes slot 0 again and its copy throws, so slot 0 holds no whole write of index 8.
        {
            static int s_throwAt = 0;
            Linie::TraceConfig c;
            c.bytes = 256 * sizeof(Linie::TraceRecord);
            c.snapRingCaps = { 8, 8 };
            c.snapBytes = 128 + 8 * 32 * 2;
            c.copier = [](uintptr_t src, void* dst, size_t n) -> bool {
                if (s_throwAt > 0 && --s_throwAt == 0) throw std::runtime_error("copy fault");
                memcpy(dst, reinterpret_cast<const void*>(src), n);
                return true;
            };
            Linie::StartTrace(c);
            const uint64_t g9 = Linie::GetTraceInfo().gen;
            s_throwAt = 9;
            for (int i = 0; i < 9; ++i) {
                Linie::TraceToken t;
                try { Linie::TraceEnter(0xA1, 0, 1000, 1, t, params, hint(g9, 0)); } catch (const std::exception&) {}
            }
            Linie::StopTrace();
            std::vector<Linie::SnapCopy> win;
            check("an unfinished write is not handed out under its number: [1, 9) gives the seven whole ones",
                  Linie::CopySnaps(0, 1, 64, win) && win.size() == 7 && win.front().index == 1 && win.back().index == 7,
                  u(win.size()).c_str());
            Linie::FreeTrace();
        }

        // What step 2 costs on the hook: printed, not checked (a timing is the machine's).
        {
            constexpr int N = 1 << 20;
            LARGE_INTEGER f, t0, t1;
            QueryPerformanceFrequency(&f);
            auto nsPer = [&](LARGE_INTEGER a, LARGE_INTEGER b, int n) {
                return double(b.QuadPart - a.QuadPart) * 1e9 / double(f.QuadPart) / n;
            };
            // The table with 1,000 names followed: steady state, and first sights.
            std::vector<Linie::ArmSpec> specs;
            for (int i = 0; i < 1000; ++i) specs.push_back(Linie::ArmSpec{ Linie::NameKey{ 0x10000 + i, 0, 7, 0 }, false, i % 8, 64 });
            auto st = Linie::BuildArmState(specs, Linie::kArmLogCapacity);
            st->gen = 1;
            auto rd = [](uintptr_t fn, Linie::FuncIdentity& out) -> bool {
                out.nameIndex = static_cast<int32_t>(fn); out.classIndex = 7; out.parmsSize = 16; return true;
            };
            Linie::StartRecording(rd, nullptr, st);
            Linie::ArmHint h;
            QueryPerformanceCounter(&t0);
            for (int i = 0; i < N; ++i) Linie::RecordCall(0x10000 + (i & 1023), 1000 + i, &h);
            QueryPerformanceCounter(&t1);
            const double steady = nsPer(t0, t1, N);
            constexpr int M = 1 << 14;
            Linie::StartRecording(rd, nullptr, st);
            QueryPerformanceCounter(&t0);
            for (int i = 0; i < M; ++i) Linie::RecordCall(0x20000 + i, 1000 + i, &h);   // first sights, no match
            QueryPerformanceCounter(&t1);
            const double firsts = nsPer(t0, t1, M);
            Linie::Reset();
            // The trace: a call whose hint chose nothing, and a 64-byte entry plus after copy.
            Linie::TraceConfig c;
            c.bytes = (2ull * N + 16) * sizeof(Linie::TraceRecord);
            c.snapRingCaps = { 64 };
            c.snapBytes = 64ull << 20;
            c.snapPerRingPerSec = 0xFFFFFF;
            c.snapTotalPerSec = 0xFFFFFF;
            c.copier = [](uintptr_t src, void* dst, size_t n) -> bool { memcpy(dst, reinterpret_cast<const void*>(src), n); return true; };
            Linie::StartTrace(c);
            const uint64_t g = Linie::GetTraceInfo().gen;
            Linie::ArmHint plain{};
            plain.gen = g;
            QueryPerformanceCounter(&t0);
            for (int i = 0; i < N; ++i) {
                Linie::TraceToken t;
                Linie::TraceEnter(0x1000 + (i & 63), 0x2000, 1000, 1, t, params, plain);
                Linie::TraceReturn(t, 1, params);
            }
            QueryPerformanceCounter(&t1);
            const double unchosen = nsPer(t0, t1, N);
            Linie::StartTrace(c);
            uint8_t block[64] = {};
            Linie::ArmHint chosen{};
            chosen.gen = Linie::GetTraceInfo().gen; chosen.ring = 0; chosen.copy = 64; chosen.flags = Linie::kArmAfter;
            constexpr int P = 1 << 16;
            QueryPerformanceCounter(&t0);
            for (int i = 0; i < P; ++i) {
                Linie::TraceToken t;
                Linie::TraceEnter(0x1000, 0x2000, 1000, 1, t, reinterpret_cast<uintptr_t>(block), chosen);
                Linie::TraceReturn(t, 1, reinterpret_cast<uintptr_t>(block));
            }
            QueryPerformanceCounter(&t1);
            const double copied = nsPer(t0, t1, P);
            Linie::FreeTrace();
            printf("  info  step-2 cost: table %.1f ns per call with 1,000 names followed, %.1f ns per first sight; "
                   "trace %.1f ns per unchosen call, %.1f ns per chosen call with a 64-byte entry and after copy\n",
                   steady, firsts, unchosen, copied);
        }
    }

    {
        blk("LIVEFUNCS-STEP3: a stack choice armed by name, its ring after the parameters', its slot at entry");
        // docs/live-funcs-step3-items.md, S3-L1. The N1 block's stub reader: a function's FName is its address's low
        // 16 bits, its class's FName 7. S3StubCap stands in for Macht's capturer.
        auto u = [](uint64_t n) { return std::to_string(n); };
        auto reader = [](uintptr_t f, Linie::FuncIdentity& out) -> bool {
            out.nameIndex     = static_cast<int32_t>(f & 0xFFFF);
            out.classIndex    = 7;
            out.functionFlags = 0x400;
            out.numParms      = 2;
            out.parmsSize     = 16;
            return true;
        };
        // Case 1: a name chosen for its stack alone, with an arm log of no capacity.
        auto st = Linie::BuildArmState({ Linie::ArmSpec{ Linie::NameKey{ 0xC, 0, 7, 0 }, false, -1, 0, 0 } }, 0);
        st->gen = 5;
        Linie::Reset();
        Linie::StartRecording(reader, nullptr, st);
        Linie::ArmHint h;
        Linie::RecordCall(0xC, 1000, &h);
        auto sum = Linie::ArmsSummary();
        check("a name chosen for its stack alone: the hint carries its stack ring, no parameter ring, and no arm",
              h.gen == 5 && h.stackRing == 0 && h.ring == -1 && st->log.empty() && sum.size() == 1 &&
                  sum[0].stackRing == 0 && sum[0].arms == 0,
              (u(h.gen) + " / " + std::to_string(h.stackRing)).c_str());

        // Case 2: one name ticked, chosen for its parameters and for its stack.
        auto st2 = Linie::BuildArmState({ Linie::ArmSpec{ Linie::NameKey{ 0xD, 0, 7, 0 }, true, -1, 0 },
                                          Linie::ArmSpec{ Linie::NameKey{ 0xD, 0, 7, 0 }, false, 0, 64 },
                                          Linie::ArmSpec{ Linie::NameKey{ 0xD, 0, 7, 0 }, false, -1, 0, 0 } }, 8);
        st2->gen = 6;
        Linie::StartRecording(reader, nullptr, st2);
        Linie::RecordCall(0xD, 1001, &h);
        check("one name ticked, chosen for its parameters and for its stack: one spec, one hint with all three",
              st2->specs.size() == 1 && (h.flags & Linie::kArmTick) && h.ring == 0 && h.stackRing == 0 &&
                  st2->log.size() == 1,
              std::to_string(h.stackRing).c_str());

        // Case 3: the arm log is full when the name chosen for both is first called.
        auto st3 = Linie::BuildArmState({ Linie::ArmSpec{ Linie::NameKey{ 0xA, 0, 7, 0 }, false, 0, 16 },
                                          Linie::ArmSpec{ Linie::NameKey{ 0xB, 0, 7, 0 }, false, 1, 16, 0 } }, 1);
        st3->gen = 7;
        Linie::StartRecording(reader, nullptr, st3);
        Linie::RecordCall(0xA, 1002, &h);
        Linie::RecordCall(0xB, 1003, &h);
        sum = Linie::ArmsSummary();
        check("a full arm log: no parameter arm for the name chosen for both, but its stack is armed",
              h.gen == 7 && h.ring == -1 && h.stackRing == 0 && sum.size() == 2 && sum[1].armsFull == 1 &&
                  sum[1].stackRing == 0,
              std::to_string(h.stackRing).c_str());
        Linie::Reset();

        // Case 4: sizes.
        check("the hint is still 24 bytes", sizeof(Linie::ArmHint) == 24);
        check("a stack ring's slot payload: 8 bytes a frame, the depth clamped to 1..62",
              Linie::StackRingCap(0) == 8 && Linie::StackRingCap(16) == 128 && Linie::StackRingCap(100) == 496,
              u(Linie::StackRingCap(16)).c_str());

        // Case 5: one parameter ring (16) and one stack ring (depth 4, 32 bytes) in one allocation:
        // K = (bytes - 64 * 2) / ((24 + 16) + (24 + 32)) = 768 / 96 = 8.
        auto start = [](bool scoped, std::vector<uintptr_t> exclude, Linie::StackCapturer cap) {
            Linie::TraceConfig c;
            c.bytes = 64 * sizeof(Linie::TraceRecord);
            c.scoped = scoped;
            c.exclude = std::move(exclude);
            c.snapRingCaps = { 16 };
            c.snapBytes = 128 + 8 * 96;
            c.copier = [](uintptr_t src, void* dst, size_t n) -> bool { memcpy(dst, reinterpret_cast<const void*>(src), n); return true; };
            c.stackRings = 1;
            c.stackDepth = 4;
            c.stackCapturer = cap;
            Linie::StartTrace(c);
            return Linie::GetTraceInfo();
        };
        auto stackOnly = [](uint64_t gen, int32_t s) { Linie::ArmHint x{}; x.gen = gen; x.stackRing = s; return x; };
        Linie::FreeTrace();
        g_s3StubMode = 0;
        Linie::TraceInfo info = start(false, {}, &S3StubCap);
        check("one parameter ring and one stack ring share the buffer and its K",
              info.snap.allocated && info.snap.rings == 1 && info.snap.slotsPerRing == 8 && info.stack.rings == 1 &&
                  info.stack.depth == 4,
              (u(info.snap.rings) + " / " + u(info.snap.slotsPerRing) + " / " + u(info.stack.rings)).c_str());

        // Case 6: a call chosen for both, in a trace that records every call.
        uint8_t buf[16];
        for (int i = 0; i < 16; ++i) buf[i] = static_cast<uint8_t>(0x40 + i);
        const uintptr_t params = reinterpret_cast<uintptr_t>(buf);
        Linie::ArmHint both{};
        both.gen = info.gen; both.ring = 0; both.copy = 16; both.stackRing = 0;
        g_s3StubMax = 0;
        Linie::TraceToken t6;
        Linie::TraceEnter(0xF0, 0xB0, 900, 1, t6, params, both);
        Linie::StopTrace();
        std::vector<Linie::TraceRecord> recs;
        Linie::CopyTrace(0, 64, recs);
        std::vector<Linie::SnapCopy> ps;
        std::vector<Linie::StackCopy> ss;
        uint64_t next = 0;
        check("chosen for both: the entry record says parameters taken and stack taken",
              recs.size() == 1 && recs[0].flags == (Linie::kTraceSnapTaken | Linie::kTraceStackTaken),
              recs.empty() ? "" : u(recs[0].flags).c_str());
        check("...the parameter ring holds its copy", Linie::CopySnaps(0, 0, 10, ps) && ps.size() == 1 && ps[0].len == 16);
        const bool gotStack = Linie::CopyStacks(0, 0, 10, ss, &next);
        check("...the stack ring holds the frames: the call's entry, three frames, no flags",
              gotStack && ss.size() == 1 && ss[0].index == 0 && ss[0].entrySeq == 0 && ss[0].len == 24 &&
                  ss[0].flags == 0 && ss[0].frames.size() == 3 && ss[0].frames[0] == 900 && ss[0].frames[1] == 901 &&
                  ss[0].frames[2] == 902 && next == 1,
              ss.empty() ? "no slot" : (u(ss[0].len) + " bytes, flags " + u(ss[0].flags)).c_str());
        check("...the capturer was asked for the depth in frames, never the slot's bytes", g_s3StubMax == 4,
              u(g_s3StubMax).c_str());
        std::vector<Linie::SnapRingInfo> pr, sr;
        const bool gotPr = Linie::SnapRings(pr), gotSr = Linie::StackRings(sr);
        check("the parameter readers see the parameter ring alone; the stack ring is the stack readers' ring 0",
              gotPr && pr.size() == 1 && pr[0].cap == 16 && gotSr && sr.size() == 1 && sr[0].index == 0 &&
                  sr[0].cap == 32 && sr[0].written == 1 && !Linie::CopySnaps(1, 0, 10, ps),
              (u(pr.size()) + " / " + u(sr.size())).c_str());

        // Case 7: scoped, nothing ticked, a call chosen for its stack alone.
        info = start(true, {}, &S3StubCap);
        Linie::TraceToken l7, n7;
        Linie::TraceEnter(0xF0, 0, 900, 1, l7, 0, stackOnly(info.gen, 0));
        Linie::TraceEnter(0xF2, 0, 800, 1, n7);
        Linie::TraceReturn(n7, 1);
        Linie::TraceReturn(l7, 1);
        Linie::StopTrace();
        recs.clear();
        Linie::CopyTrace(0, 64, recs);
        check("outside every scope, a call chosen for its stack alone is recorded alone, its stack taken",
              l7.traced && !l7.opened && recs.size() == 2 &&
                  recs[0].flags == (Linie::kTraceSnapLone | Linie::kTraceStackTaken),
              recs.empty() ? "" : u(recs[0].flags).c_str());
        check("...and opens no scope: the call it makes is not recorded", !n7.traced);

        // Case 8: the per-frame exclusion, inside an open scope.
        info = start(true, { 0xF0 }, &S3StubCap);
        Linie::ArmHint tick{};
        tick.gen = info.gen; tick.flags = Linie::kArmTick;
        Linie::TraceToken root8, x8;
        Linie::TraceEnter(0xA1, 0, 900, 1, root8, 0, tick);
        Linie::TraceEnter(0xF0, 0, 800, 1, x8, 0, stackOnly(info.gen, 0));
        Linie::StopTrace();
        recs.clear();
        Linie::CopyTrace(0, 64, recs);
        check("an excluded function chosen for its stack is kept in the scope, excluded and stack taken",
              x8.traced && recs.size() == 2 && recs[1].flags == (Linie::kTraceSnapExcluded | Linie::kTraceStackTaken),
              recs.size() == 2 ? u(recs[1].flags).c_str() : u(recs.size()).c_str());

        // Case 9: a stack ring the trace does not have.
        info = start(false, {}, &S3StubCap);
        Linie::TraceToken t9;
        Linie::TraceEnter(0xF0, 0, 900, 1, t9, 0, stackOnly(info.gen, 5));
        Linie::StopTrace();
        recs.clear();
        ss.clear();
        Linie::CopyTrace(0, 64, recs);
        const bool got9 = Linie::CopyStacks(0, 0, 10, ss);
        check("a stack ring the trace does not have: an ordinary record, and no stack slot",
              recs.size() == 1 && recs[0].flags == 0 && got9 && ss.empty(), u(ss.size()).c_str());
        info = start(true, {}, &S3StubCap);
        Linie::TraceToken t9b;
        Linie::TraceEnter(0xF0, 0, 900, 1, t9b, 0, stackOnly(info.gen, 5));
        Linie::StopTrace();
        check("...and outside a scope it is not recorded at all", !t9b.traced);

        // Case 10: no capturer, and a capturer that read nothing.
        info = start(false, {}, nullptr);
        Linie::TraceToken t10;
        Linie::TraceEnter(0xF0, 0, 900, 1, t10, 0, stackOnly(info.gen, 0));
        Linie::StopTrace();
        ss.clear();
        const bool got10 = Linie::CopyStacks(0, 0, 10, ss);
        check("no capturer installed: a slot with no frames, flagged with Linie's own bit",
              got10 && ss.size() == 1 && ss[0].len == 0 && ss[0].frames.empty() && ss[0].flags == Linie::kSnapNoCapturer,
              ss.empty() ? "no slot" : u(ss[0].flags).c_str());
        g_s3StubMode = 1;
        info = start(false, {}, &S3StubCap);
        Linie::TraceToken t10b;
        Linie::TraceEnter(0xF0, 0, 900, 1, t10b, 0, stackOnly(info.gen, 0));
        Linie::StopTrace();
        ss.clear();
        const bool got10b = Linie::CopyStacks(0, 0, 10, ss);
        check("a capturer that read nothing: no frames, and its own flag kept",
              got10b && ss.size() == 1 && ss[0].len == 0 && ss[0].flags == Macht::kStackBadSp,
              ss.empty() ? "no slot" : u(ss[0].flags).c_str());

        // Case 11: a capturer that throws (TR2 with the walk inside the hook's in-flight section).
        g_s3StubMode = 2;
        info = start(false, {}, &S3StubCap);
        bool threw = false;
        Linie::TraceToken t11;
        try { Linie::TraceEnter(0xF0, 0, 900, 1, t11, 0, stackOnly(info.gen, 0)); }
        catch (const std::exception&) { threw = true; }
        const ULONGLONG s11 = GetTickCount64();
        Linie::StopTrace();
        const ULONGLONG stop11 = GetTickCount64() - s11;
        recs.clear();
        ss.clear();
        Linie::CopyTrace(0, 64, recs);
        const bool got11 = Linie::CopyStacks(0, 0, 10, ss);
        check("a capturer that throws: Stop quiesces at once, the record claims no stack, no slot is handed out",
              threw && t11.traced && Linie::GetTraceInfo().quiesced && stop11 < 1000 && recs.size() == 1 &&
                  (recs[0].flags & Linie::kTraceStackTaken) == 0 && got11 && ss.empty(),
              (u(stop11) + " ms, " + u(ss.size()) + " slots").c_str());
        g_s3StubMode = 0;
        // Case 12 is a guard: with no stack chosen, the step-1 and step-2 blocks above run unchanged.
        Linie::FreeTrace();
    }

    {
        blk("LIVEFUNCS-STEP3: the stack budget apart from the parameters', and what each capture cost");
        // docs/live-funcs-step3-items.md, S3-L2. A clock that moves 7 ticks a read, all inside one second: a capture
        // reads it twice, so each costs exactly 7. Stack budget 2 a ring and 3 in all.
        static uint64_t s_now = 0;
        Linie::SetTraceClockForTest([]() -> uint64_t { return s_now += 7; });
        auto u = [](uint64_t n) { return std::to_string(n); };
        uint8_t buf[8] = {};
        const uintptr_t params = reinterpret_cast<uintptr_t>(buf);
        auto start = [](bool scoped, uint32_t stackRings, uint32_t snapPerRing) {
            Linie::TraceConfig c;
            c.bytes = 256 * sizeof(Linie::TraceRecord);
            c.scoped = scoped;
            c.snapOnly = scoped;
            c.snapRingCaps = { 8 };
            c.snapBytes = 64 * 1024;
            c.snapPerRingPerSec = snapPerRing;
            c.snapTotalPerSec = 1000;
            c.copier = [](uintptr_t src, void* dst, size_t n) -> bool { memcpy(dst, reinterpret_cast<const void*>(src), n); return true; };
            c.stackRings = stackRings;
            c.stackDepth = 4;
            c.stackCapturer = &S3StubCap;
            c.stackPerRingPerSec = 2;
            c.stackTotalPerSec = 3;
            Linie::StartTrace(c);
            return Linie::GetTraceInfo();
        };
        auto stackOnly = [](uint64_t gen, int32_t s) { Linie::ArmHint x{}; x.gen = gen; x.stackRing = s; return x; };
        auto both = [](uint64_t gen) { Linie::ArmHint x{}; x.gen = gen; x.ring = 0; x.copy = 8; x.stackRing = 0; return x; };
        auto copyAll = [] { std::vector<Linie::TraceRecord> r; Linie::CopyTrace(0, 256, r); return r; };
        Linie::Reset();
        Linie::FreeTrace();
        g_s3StubMode = 0;

        // Cases 1, 6 and 7: five calls in second 10, one in second 11, every call recorded.
        Linie::TraceInfo info = start(false, 1, 1000);
        const uint64_t f = info.qpcFreq;
        s_now = 10 * f;
        for (int i = 0; i < 5; ++i) { Linie::TraceToken t; Linie::TraceEnter(0xA7, 0, 900, 1, t, 0, stackOnly(info.gen, 0)); }
        s_now = 11 * f;
        { Linie::TraceToken t; Linie::TraceEnter(0xA7, 0, 900, 1, t, 0, stackOnly(info.gen, 0)); }
        Linie::StopTrace();
        auto recs = copyAll();
        int taken = 0, over = 0, paramOver = 0;
        for (size_t i = 0; i < recs.size() && i < 5; ++i) {
            if (recs[i].flags & Linie::kTraceStackTaken) ++taken;
            if (recs[i].flags & Linie::kTraceStackBudget) ++over;
            if (recs[i].flags & Linie::kTraceSnapBudget) ++paramOver;
        }
        info = Linie::GetTraceInfo();
        check("a stack budget of 2 a second: five calls, two stacks taken, three recorded over the budget",
              recs.size() == 6 && taken == 2 && over == 3 && paramOver == 0, (u(taken) + "/" + u(over)).c_str());
        check("...counted as the stack's skipped, not the parameters'",
              info.stack.skippedBudget == 3 && info.stack.droppedBudget == 0 && info.snap.skippedBudget == 0 &&
                  info.stack.perRingPerSec == 2 && info.stack.totalPerSec == 3,
              (u(info.stack.skippedBudget) + " / " + u(info.snap.skippedBudget)).c_str());
        check("the next second admits again", recs.size() == 6 && (recs[5].flags & Linie::kTraceStackTaken));
        std::vector<Linie::StackCopy> ss;
        std::vector<Linie::SnapRingInfo> sr;
        const bool gotSs = Linie::CopyStacks(0, 0, 64, ss), gotSr = Linie::StackRings(sr);
        bool eachSeven = gotSs && ss.size() == 3;
        for (const auto& s : ss) eachSeven = eachSeven && s.ticks == 7;
        check("each capture's cost is measured: three captures of 7 ticks, the dearest 7",
              info.stack.captures == 3 && info.stack.spentTicks == 21 && info.stack.maxTicks == 7 && eachSeven &&
                  gotSr && sr.size() == 1 && sr[0].spentTicks == 21,
              (u(info.stack.captures) + " / " + u(info.stack.spentTicks) + " / " + u(info.stack.maxTicks)).c_str());

        // Case 2: lone, chosen for its stack alone, over the budget.
        info = start(true, 1, 1000);
        s_now = 20 * f;
        Linie::TraceToken l0, l1, l2;
        Linie::TraceEnter(0xA7, 0, 900, 1, l0, 0, stackOnly(info.gen, 0));
        Linie::TraceEnter(0xA7, 0, 900, 1, l1, 0, stackOnly(info.gen, 0));
        Linie::TraceEnter(0xA7, 0, 900, 1, l2, 0, stackOnly(info.gen, 0));
        Linie::StopTrace();
        info = Linie::GetTraceInfo();
        check("a lone call chosen for its stack alone, over the budget: no record at all, counted dropped",
              l0.traced && l1.traced && !l2.traced && info.written == 2 && info.stack.droppedBudget == 1 &&
                  info.stack.skippedBudget == 0,
              (u(info.written) + " / " + u(info.stack.droppedBudget)).c_str());

        // Case 3: lone, chosen for both; the parameters admitted, the stack over its budget.
        info = start(true, 1, 1000);
        s_now = 30 * f;
        for (int i = 0; i < 2; ++i) { Linie::TraceToken t; Linie::TraceEnter(0xA7, 0, 900, 1, t, 0, stackOnly(info.gen, 0)); }
        Linie::TraceToken b3;
        Linie::TraceEnter(0xA8, 0, 900, 1, b3, params, both(info.gen));
        Linie::StopTrace();
        recs = copyAll();
        info = Linie::GetTraceInfo();
        std::vector<Linie::SnapCopy> ps;
        const bool gotPs = Linie::CopySnaps(0, 0, 64, ps);
        check("lone, chosen for both, the stack over its budget: recorded for its parameters, the stack skipped",
              b3.traced && recs.size() == 3 &&
                  recs[2].flags == (Linie::kTraceSnapLone | Linie::kTraceSnapTaken | Linie::kTraceStackBudget) &&
                  gotPs && ps.size() == 1 && info.stack.skippedBudget == 1 && info.stack.droppedBudget == 0,
              recs.size() == 3 ? u(recs[2].flags).c_str() : u(recs.size()).c_str());

        // Case 4: lone, chosen for both; the parameter budget of 1 spent, the stack admitted.
        info = start(true, 1, 1);
        s_now = 40 * f;
        Linie::TraceToken a4, b4;
        Linie::TraceEnter(0xA8, 0, 900, 1, a4, params, both(info.gen));
        Linie::TraceEnter(0xA8, 0, 900, 1, b4, params, both(info.gen));
        Linie::StopTrace();
        recs = copyAll();
        info = Linie::GetTraceInfo();
        ss.clear();
        const bool got4 = Linie::CopyStacks(0, 0, 64, ss);
        check("lone, chosen for both, the parameters over their budget: recorded for its stack, the parameters skipped",
              b4.traced && recs.size() == 2 &&
                  recs[1].flags == (Linie::kTraceSnapLone | Linie::kTraceSnapBudget | Linie::kTraceStackTaken) &&
                  info.snap.skippedBudget == 1 && info.snap.droppedBudget == 0 && got4 && ss.size() == 2,
              recs.size() == 2 ? u(recs[1].flags).c_str() : u(recs.size()).c_str());

        // Case 5: a second stack ring with room of its own, refused by the total of 3.
        info = start(false, 2, 1000);
        s_now = 50 * f;
        for (int i = 0; i < 2; ++i) { Linie::TraceToken t; Linie::TraceEnter(0xA7, 0, 900, 1, t, 0, stackOnly(info.gen, 0)); }
        for (int i = 0; i < 2; ++i) { Linie::TraceToken t; Linie::TraceEnter(0xA9, 0, 900, 1, t, 0, stackOnly(info.gen, 1)); }
        Linie::StopTrace();
        recs = copyAll();
        ss.clear();
        const bool got5 = Linie::CopyStacks(1, 0, 64, ss);
        check("the stack total of 3 caps the two rings together: the second ring's second call is over it",
              got5 && ss.size() == 1 && recs.size() == 4 && recs[3].flags == Linie::kTraceStackBudget,
              (u(ss.size()) + " / " + (recs.size() == 4 ? u(recs[3].flags) : u(recs.size()))).c_str());
        Linie::FreeTrace();
        Linie::SetTraceClockForTest(nullptr);
    }

    {
        blk("LIVEFUNCS-STEP3: a native stack from the hook's return slot -- bounds, headroom, the anchor, faults");
        // docs/live-funcs-step3-items.md, S3-M1. Case 8 (through TraceEnter) waits for S3-L1.
        static_assert(Macht::kStackRawFrames >= Macht::kStackOwnSlack + Macht::kStackMaxFrames + 1,
                      "the raw buffer holds our frames, the most kept, and one to tell More");
        void* raw[4] = { reinterpret_cast<void*>(5), reinterpret_cast<void*>(6), reinterpret_cast<void*>(7),
                         reinterpret_cast<void*>(8) };
        check("the anchor is found inside its window", Macht::AnchorIndex(raw, 4, 7, 3) == 2);
        check("...and not past it", Macht::AnchorIndex(raw, 4, 8, 3) == 4);
        check("...and an address never walked is absent", Macht::AnchorIndex(raw, 4, 9, 13) == 4);

        uint64_t out[64] = {};
        uint16_t fl = 0xFFFF;
        uint64_t myRet = 0, outerRet = 0;
        uint32_t n = S3Outer(out, 16, fl, myRet, outerRet);
        check("the caller's frame first, then its caller's: our own frames cut at the anchor",
              n >= 2 && out[0] == myRet && out[1] == outerRet && fl == 0,
              (std::to_string(n) + " frames, flags " + std::to_string(fl)).c_str());

        fl = 0xFFFF;
        n = S3Deep(40, out, 16, fl);
        check("a deep stack: the most asked for, and More", n == 16 && (fl & Macht::kStackMore) != 0,
              (std::to_string(n) + " frames, flags " + std::to_string(fl)).c_str());
        fl = 0xFFFF;
        n = S3Outer(out, 62, fl, myRet, outerRet);
        check("a shallow stack under the maximum: no More", n >= 2 && (fl & Macht::kStackMore) == 0,
              std::to_string(fl).c_str());

        uint64_t local = 0x1234;
        fl = 0;
        n = Macht::CaptureCallerStack(1000, out, 16, fl);
        check("a return slot that is no stack address: BadSp, nothing read", n == 0 && fl == Macht::kStackBadSp);
        fl = 0;
        n = Macht::CaptureCallerStack(reinterpret_cast<uintptr_t>(&local) + 1, out, 16, fl);
        check("...a misaligned one", n == 0 && fl == Macht::kStackBadSp);
        uint64_t below[2] = {};
        fl = 0;
        // A megabyte down: always below the capturer's frame, whatever main's own frame holds. Never read.
        n = Macht::CaptureCallerStack(reinterpret_cast<uintptr_t>(&below[0]) - (uintptr_t(1) << 20), out, 16, fl);
        check("...one below the capturer's own frame", n == 0 && fl == Macht::kStackBadSp);

        fl = 0;
        n = Macht::CaptureCallerStackEx(reinterpret_cast<uintptr_t>(&local), out, 16, fl, uintptr_t(1) << 40,
                                        &RtlCaptureStackBackTrace);
        check("too little stack left: LowStack, nothing read", n == 0 && fl == Macht::kStackLowStack);

        fl = 0;
        n = Macht::CaptureCallerStackEx(reinterpret_cast<uintptr_t>(&local), out, 16, fl, Macht::kStackHeadroom,
                                        &S3WalkerNoAnchor);
        check("no anchor among the frames: only the immediate caller, Partial",
              n == 1 && out[0] == 0x1234 && fl == Macht::kStackPartial,
              (std::to_string(n) + " / " + std::to_string(fl)).c_str());

        fl = 0;
        n = Macht::CaptureCallerStackEx(reinterpret_cast<uintptr_t>(&local), out, 16, fl, Macht::kStackHeadroom,
                                        &S3WalkerFaults);
        check("a walk that faults: Fault, nothing kept, and the process goes on", n == 0 && fl == Macht::kStackFault);

        // S3-M2: what a return address is. myRet still holds a return address into S3Outer (its last call above).
        {
            Macht::CodeSite site;
            const bool ok = Macht::DescribeCode(static_cast<uintptr_t>(myRet), site);
            uintptr_t outerStart = reinterpret_cast<uintptr_t>(&S3Outer);
            const auto* op = reinterpret_cast<const uint8_t*>(outerStart);
            if (op[0] == 0xE9) {   // an incremental link's jump thunk: the function is where it jumps
                int32_t rel = 0;
                std::memcpy(&rel, op + 1, sizeof(rel));
                outerStart += 5 + static_cast<intptr_t>(rel);
            }
            check("a return address into S3Outer: this exe, its leaf, own, unwind data, S3Outer's start",
                  ok && site.moduleBase == reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr)) &&
                      _stricmp(site.moduleUtf8.c_str(), "dll_core_test.exe") == 0 && site.own && site.unwind &&
                      site.fnBegin == outerStart,
                  (site.moduleUtf8 + " own " + std::to_string(site.own) + " unwind " + std::to_string(site.unwind) +
                   (site.fnBegin == outerStart ? " start ok" : " start differs")).c_str());
            Macht::CodeSite atStart;
            Macht::DescribeCode(outerStart, atStart);
            check("...and a function's first byte is described by the byte before it (ret-1)",
                  atStart.fnBegin != outerStart);
            const auto rtl = reinterpret_cast<uintptr_t>(
                GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlCaptureStackBackTrace"));
            Macht::CodeSite nt;
            const bool ntOk = rtl != 0 && Macht::DescribeCode(rtl + 1, nt);
            check("an address inside ntdll: its leaf, and not own",
                  ntOk && _stricmp(nt.moduleUtf8.c_str(), "ntdll.dll") == 0 && !nt.own, nt.moduleUtf8.c_str());
            std::vector<uint64_t> heap(4, 0);
            Macht::CodeSite none;
            none.moduleBase = 1;
            const bool h1 = Macht::DescribeCode(reinterpret_cast<uintptr_t>(heap.data()), none);
            check("a heap address is in no module, and the site is reset",
                  !h1 && none.moduleBase == 0 && none.moduleUtf8.empty() && !none.unwind);
            const bool h2 = Macht::DescribeCode(0x1000, none);
            check("...nor is 0x1000", !h2 && none.moduleBase == 0 && none.moduleUtf8.empty() && !none.unwind);
        }

        // S3-M3: a chained fragment's primary function, on a synthetic image: offsets are RVAs into `img`.
        {
            alignas(8) static uint8_t img[0x800] = {};
            auto putRf = [](uint32_t off, uint32_t b, uint32_t e, uint32_t u) {
                RUNTIME_FUNCTION r{};
                r.BeginAddress = b;
                r.EndAddress = e;
                r.UnwindData = u;
                memcpy(img + off, &r, sizeof r);
            };
            auto putUi = [](uint32_t off, bool chained, uint8_t codes) {
                img[off]     = static_cast<uint8_t>(1 | ((chained ? UNW_FLAG_CHAININFO : 0) << 3));   // version 1
                img[off + 1] = 0;
                img[off + 2] = codes;
                img[off + 3] = 0;
            };
            const uintptr_t base = reinterpret_cast<uintptr_t>(img);
            putUi(0x300, false, 0);                    // the primary's own unwind info
            putUi(0x200, true, 3);                     // a fragment: 3 codes, padded to 4, then its parent's entry
            putRf(0x200 + 4 + 8, 0x800, 0x900, 0x300);
            putUi(0x400, true, 2);                     // a fragment of that fragment: 2 codes, no padding
            putRf(0x400 + 4 + 4, 0x1000, 0x1100, 0x200);
            putRf(0x500, 0x700, 0x780, 0x300);         // the entry an indirect one points at
            putUi(0x600, true, 0);                     // a chain that names itself
            putRf(0x600 + 4, 0x5000, 0x5100, 0x600);
            auto hx = [](uint32_t v) { char b[16]; snprintf(b, sizeof b, "0x%X", v); return std::string(b); };
            const uint32_t c1 = Macht::FollowChain(base, 0x1000, 0x200);
            check("a chained fragment names its primary function (its 3 unwind codes padded to 4)", c1 == 0x800,
                  hx(c1).c_str());
            const uint32_t c2 = Macht::FollowChain(base, 0x2000, 0x400);
            check("...through two links", c2 == 0x800, hx(c2).c_str());
            const uint32_t c3 = Macht::FollowChain(base, 0x3000, 0x500 | 1);
            check("an indirect entry (UnwindData bit 0) is followed to the entry it names", c3 == 0x700, hx(c3).c_str());
            check("an entry that chains nowhere is its own primary", Macht::FollowChain(base, 0x4000, 0x300) == 0x4000);
            check("a chain that loops ends after kChainMaxHops links", Macht::FollowChain(base, 0x5000, 0x600) == 0x5000);
        }

        // S3-A1: the native-entry index, pure: code to UFunction, a shared entry counted.
        {
            std::vector<Aura::CodeEntry> idx = { { 0x30, 0xC }, { 0x10, 0xB }, { 0x30, 0xA }, { 0x10, 0xB } };
            Aura::SortCodeEntries(idx);
            const bool sorted = idx.size() == 3 && idx[0].code == 0x10 && idx[0].ufunc == 0xB && idx[1].code == 0x30 &&
                                idx[1].ufunc == 0xA && idx[2].code == 0x30 && idx[2].ufunc == 0xC;
            check("the index sorts by code, then by function, and drops an exact duplicate", sorted,
                  std::to_string(idx.size()).c_str());
            uintptr_t uf = 99;
            const size_t shared = Aura::LookupCodeEntry(idx, 0x30, uf);
            check("an entry two functions share: both counted, the lowest named", shared == 2 && uf == 0xA,
                  (std::to_string(shared) + " / " + std::to_string(uf)).c_str());
            const size_t one = Aura::LookupCodeEntry(idx, 0x10, uf);
            check("an entry of one function names it", one == 1 && uf == 0xB);
            const size_t none = Aura::LookupCodeEntry(idx, 0x20, uf);
            check("an address no function enters at: none, and no function named", none == 0 && uf == 0);
            std::vector<Aura::CodeEntry> empty;
            check("an empty index answers none", Aura::LookupCodeEntry(empty, 0x10, uf) == 0 && uf == 0);
            std::vector<Aura::CodeEntry> collected;
            check("collecting with no object array: an empty index, whole", Aura::CollectCodeEntries(collected) &&
                  collected.empty());

            // The code test asked of the kernel once per region: a thousand addresses in this exe's code, a few queries.
            Aura::CodeRangeCache cache;
            const uintptr_t codeA = reinterpret_cast<uintptr_t>(&S3Outer);
            const uintptr_t codeB = reinterpret_cast<uintptr_t>(&S3Deep);
            bool allCode = true;
            for (int i = 0; i < 1000; ++i) allCode = allCode && cache.IsCode((i & 1) ? codeA : codeB);
            check("a thousand code addresses in one module: all code, and a few kernel queries", allCode &&
                  cache.queries <= 4, std::to_string(cache.queries).c_str());
            std::vector<uint64_t> heapBlock(8, 0);
            const bool heapCode = cache.IsCode(reinterpret_cast<uintptr_t>(heapBlock.data()));
            check("...a heap address is not code, cached or not", !heapCode && !cache.IsCode(0x1000) &&
                  !cache.IsCode(reinterpret_cast<uintptr_t>(heapBlock.data())));
        }

        // [A1-SCRIPT-FUNCS] A script function's Func is the interpreter, and the index reads it like any other: a frame
        // in the interpreter then names one of the functions entering there, with how many share it. Its CE code
        // address stays none, the "Blueprint-only" answer. A fake UFunction: FunctionFlags zero wherever they are read
        // (no FUNC_Native), Func at an offset this block sets and puts back.
        {
            const int savedFunc = DynOff::UFUNCTION_FUNC;
            const bool savedDetected = DynOff::bUFunctionFuncDetected.load();
            DynOff::UFUNCTION_FUNC = 0x80;
            DynOff::bUFunctionFuncDetected.store(true);
            alignas(16) static uint8_t scriptFn[0x200];
            memset(scriptFn, 0, sizeof scriptFn);
            const uintptr_t interp = reinterpret_cast<uintptr_t>(&S3Outer);
            memcpy(scriptFn + 0x80, &interp, sizeof interp);
            const uintptr_t fn = reinterpret_cast<uintptr_t>(scriptFn);
            const uintptr_t slot = Aura::IndexFuncSlot(fn);
            check("[A1-SCRIPT-FUNCS] a script function's Func is the slot the native-entry index reads", slot == interp,
                  (std::to_string(slot) + " vs " + std::to_string(interp)).c_str());
            check("[A1-SCRIPT-FUNCS] ...while its CE code address stays none", Aura::GetFunctionCodeAddr(fn) == 0);
            memset(scriptFn + 0x80, 0, sizeof interp);
            check("[A1-SCRIPT-FUNCS] ...and an unbound one (Func null) gives the index nothing",
                  Aura::IndexFuncSlot(fn) == 0);
            DynOff::UFUNCTION_FUNC = savedFunc;
            DynOff::bUFunctionFuncDetected.store(savedDetected);
        }

        // [A1-INTERP-LABEL] Which shared entry is the interpreter: the one script functions enter. Flags read as zero
        // are "not found" (the CE code address's three-way rule), never "script".
        {
            constexpr uint32_t kNative = 0x00000400, kEvent = 0x00000800, kBlueprintEvent = 0x08000000,
                               kPublic = 0x00020000;
            check("[A1-INTERP-LABEL] a Blueprint event's flags (no FUNC_Native) say script",
                  Aura::IsScriptFunctionFlags(kEvent | kBlueprintEvent | kPublic));
            check("[A1-INTERP-LABEL] ...a native function's do not", !Aura::IsScriptFunctionFlags(kNative | kPublic));
            check("[A1-INTERP-LABEL] ...and flags not found (zero) are no verdict", !Aura::IsScriptFunctionFlags(0));
            check("[A1-INTERP-LABEL] ...nor is a null function", !Aura::IsScriptFunction(0));
        }

        // Case 8 (S3-L1): through TraceEnter, as Stark calls it -- the hook's own return-address slot as `sp`, Macht's
        // capturer installed, a call chosen for its stack alone in a scoped trace.
        {
            static_assert(Linie::kStackMaxDepth + Macht::kStackOwnSlack + 1 <= Macht::kStackRawFrames,
                          "the walk's buffer holds the deepest stack ring, our own frames, and one to tell More");
            Linie::Reset();
            Linie::FreeTrace();
            Linie::TraceConfig c;
            c.bytes = 64 * sizeof(Linie::TraceRecord);
            c.scoped = true;
            c.snapOnly = true;
            c.snapBytes = 64 * 1024;
            c.stackRings = 1;
            c.stackDepth = 4;
            c.stackCapturer = &Macht::CaptureCallerStack;
            Linie::StartTrace(c);
            uint64_t traceRet = 0;
            Linie::TraceToken tk;
            S3TraceDeep(20, Linie::GetTraceInfo().gen, traceRet, tk);
            Linie::StopTrace();
            std::vector<Linie::StackCopy> sc;
            const bool okc = Linie::CopyStacks(0, 0, 4, sc);
            check("through TraceEnter: the first frame is the hooked frame's caller, four kept, and More",
                  okc && sc.size() == 1 && sc[0].len == 32 && sc[0].frames.size() == 4 && sc[0].frames[0] == traceRet &&
                      (sc[0].flags & Macht::kStackMore) != 0,
                  sc.empty() ? "no slot" : (std::to_string(sc[0].len) + " bytes, flags " + std::to_string(sc[0].flags)).c_str());
            Linie::FreeTrace();
        }

        // The cost of one capture, printed (Release), for the budget's defaults (T17).
        {
            constexpr int kRuns = 65536;
            uint64_t sink = 0;
            const auto t0 = std::chrono::steady_clock::now();
            for (int i = 0; i < kRuns; ++i) {
                uint16_t f = 0;
                sink += S3Deep(20, out, 16, f);
            }
            const double ns = std::chrono::duration<double, std::nano>(std::chrono::steady_clock::now() - t0).count();
            std::printf("  bench: one 16-frame capture from 20 deep: %.0f ns (%d runs, %llu frames)\n", ns / kRuns,
                        kRuns, static_cast<unsigned long long>(sink));
        }
        // ...and a hooked call with and without a stack chosen, through TraceEnter (budgets at the 24-bit max).
        {
            constexpr int kRuns = 65536;
            Linie::TraceConfig c;
            c.bytes = (kRuns + 16ull) * sizeof(Linie::TraceRecord);
            c.snapBytes = 64ull << 20;
            c.stackRings = 1;
            c.stackDepth = 16;
            c.stackCapturer = &Macht::CaptureCallerStack;
            c.stackPerRingPerSec = 0xFFFFFF;
            c.stackTotalPerSec = 0xFFFFFF;
            double ns[2] = {};
            for (int withStack = 0; withStack < 2; ++withStack) {
                Linie::StartTrace(c);
                const uint64_t g = withStack ? Linie::GetTraceInfo().gen : 0;   // gen 0: a hint of no recording
                uint64_t r = 0;
                const auto t0 = std::chrono::steady_clock::now();
                for (int i = 0; i < kRuns; ++i) {
                    Linie::TraceToken tk;
                    S3TraceDeep(20, g, r, tk);
                }
                ns[withStack] = std::chrono::duration<double, std::nano>(std::chrono::steady_clock::now() - t0).count() / kRuns;
            }
            const uint64_t kept = Linie::GetTraceInfo().stack.captures;
            Linie::FreeTrace();
            std::printf("  bench: a hooked call from 20 deep: %.0f ns with a 16-frame stack (%llu kept), %.0f ns without\n",
                        ns[1], static_cast<unsigned long long>(kept), ns[0]);
        }
    }

    {
        blk("LIVEFUNCS-STEP2: a parameter's way -- in, const reference, out, in-out, return -- from its flags");
        // docs/live-funcs-step2-items.md, B1. CPF_Parm 0x80, CPF_OutParm 0x100, CPF_ReturnParm 0x400, CPF_ConstParm
        // 0x2, CPF_ReferenceParm 0x08000000. The const-ref value is UHT's own for a `const FString&` parameter.
        using PK = Ubel::ParamKind;
        check("a plain parameter is in", Ubel::ParamKindOf(0x80) == PK::In);
        check("a const reference is in, though it carries the out flag",
              Ubel::ParamKindOf(0x0010000008000182ull) == PK::ConstRef);
        check("an out parameter", Ubel::ParamKindOf(0x180) == PK::Out);
        check("a reference that is not const goes both ways", Ubel::ParamKindOf(0x08000180) == PK::InOut);
        check("the return value, though it carries the out flag too", Ubel::ParamKindOf(0x580) == PK::Return);
    }

    {
        blk("LIVEFUNCS-STEP2: the arms' layouts read in the background -- checked live before and after, reused by key");
        // docs/live-funcs-step2-items.md, B5. Stub checks: no game memory. Every arm's function is 0xBx (or 0xC1 for
        // reuse); its live key, slot state and layout come from the statics below.
        using LS = Linie::ArmLayoutState;
        static int32_t s_liveName = 0x50;   // 0xC1's live FName, moved between passes
        static bool    s_flipB4 = false;    // the capture of 0xB4 finds another function there afterwards
        Ubel::ArmCaptureOps ops;
        ops.classify = [](uintptr_t f, const Linie::FuncIdentity&) -> Ubel::FuncState {
            return f == 0xB2 ? Ubel::FuncState::Unloaded : f == 0xB3 ? Ubel::FuncState::Recycled : Ubel::FuncState::Live;
        };
        ops.readKey = [](uintptr_t f, Linie::NameKey& k, uint64_t& outer) -> bool {
            k = Linie::NameKey{ f == 0xC1 ? s_liveName : static_cast<int32_t>(f), 0, 7, 0 };
            outer = 0x9000;
            if (f == 0xB4 && s_flipB4) k.fnIdx = 0x99;
            return true;
        };
        ops.capture = [](uintptr_t f, Ubel::ParamLayout& out, std::string& why) -> bool {
            out = Ubel::ParamLayout{};
            if (f == 0xB6) { why = "no parameters"; return false; }
            if (f == 0xB4) s_flipB4 = true;
            out.func = f;
            out.numParms = (f == 0xB5) ? 3 : 2;   // 0xB5 claims three parameters and has two
            out.parmsSize = 8;
            for (int i = 0; i < 2; ++i) {
                Ubel::ParamField p;
                p.name = "P" + std::to_string(i); p.offset = 4 * i; p.size = 4;
                out.params.push_back(p);
            }
            out.layoutEnd = 8;
            return true;
        };
        ops.nowMs = []() -> uint64_t { return 1500; };
        ops.tailDecided = true;
        auto arm = [](uintptr_t addr, int32_t name) {
            Linie::ArmRecord r;
            r.addr = addr;
            r.ident.captured = true; r.ident.nameIndex = name; r.ident.classIndex = 7; r.ident.outer = 0x9000;
            r.ident.numParms = 2; r.ident.parmsSize = 8;
            r.ring = 0; r.armMs = 1000;
            return r;
        };
        auto u = [](uint64_t n) { return std::to_string(n); };

        auto st = Linie::BuildArmState({ Linie::ArmSpec{ Linie::NameKey{ 0x50, 0, 7, 0 }, false, 0, 64 } }, 16);
        for (uintptr_t a : { 0xB1, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6 })
            st->log.push_back(arm(a, static_cast<int32_t>(a)));
        st->logCount = st->log.size();
        Ubel::ArmLayoutMemo memo;
        check("a pass takes at most what it is asked", Ubel::RunArmCapturePass(*st, 1, ops, memo) == 1);
        check("...and the next pass the rest", Ubel::RunArmCapturePass(*st, 64, ops, memo) == 5);
        const auto v = Linie::CopyArms(*st);
        check("a live function, unchanged across the read: Read, with its arm-to-read wait",
              v.size() == 6 && v[0].state == LS::Read && v[0].layout && v[0].readMs == 500, v.empty() ? "" : u(v[0].readMs).c_str());
        check("gone from its slot first: unloaded before the read, no layout", v.size() == 6 &&
              v[1].state == LS::UnloadedBeforeRead && !v[1].layout);
        check("another function in its slot: replaced before the read", v.size() == 6 && v[2].state == LS::ReplacedBeforeRead);
        check("another function there by the time the read finished: replaced, the read discarded",
              v.size() == 6 && v[3].state == LS::ReplacedBeforeRead && !v[3].layout);
        check("parameters that do not fill the function's count: doubtful, but published -- it still decodes",
              v.size() == 6 && v[4].state == LS::Doubtful && v[4].layout);
        check("a read that refuses: failed, and why", v.size() == 6 && v[5].state == LS::Failed && v[5].why == "no parameters",
              v.size() == 6 ? v[5].why.c_str() : "");

        // Reuse only under the same five numbers: the address, its Outer, and the four name ints.
        auto st2 = Linie::BuildArmState({ Linie::ArmSpec{ Linie::NameKey{ 0x50, 0, 7, 0 }, false, 0, 64 } }, 16);
        Ubel::ArmLayoutMemo memo2;
        auto add = [&](int32_t name) { st2->log.push_back(arm(0xC1, name)); st2->logCount = st2->log.size(); };
        s_liveName = 0x50; add(0x50); Ubel::RunArmCapturePass(*st2, 64, ops, memo2);
        s_liveName = 0x60; add(0x60); Ubel::RunArmCapturePass(*st2, 64, ops, memo2);   // the same address, another name
        check("another name at the same address and Outer: read again", memo2.captures == 2, u(memo2.captures).c_str());
        s_liveName = 0x50; add(0x50); Ubel::RunArmCapturePass(*st2, 64, ops, memo2);   // the first one back
        const auto v2 = Linie::CopyArms(*st2);
        check("...the first one back, under the same five numbers: its layout reused, not read again",
              memo2.captures == 2 && v2.size() == 3 && v2[2].state == LS::Read && v2[2].layout == v2[0].layout &&
              v2[1].layout != v2[0].layout, u(memo2.captures).c_str());

        // Sealed: Stop has passed; nothing is read for it.
        add(0x50);
        st2->log.back().addr = 0xB7;                 // a live function no earlier arm read: only the seal stops it
        st2->log.back().ident.nameIndex = 0xB7;
        st2->logCount = st2->log.size();
        Linie::SealArms(*st2);
        const size_t before = memo2.captures;
        Ubel::RunArmCapturePass(*st2, 64, ops, memo2);
        check("after SealArms an arm is not read", memo2.captures == before && Linie::CopyArms(*st2)[3].state == LS::NotReadBeforeStop);
    }

    {
        blk("LIVEFUNCS-STEP2: a parameter copy decoded -- numbers, bools, enums, names, structs, arrays, what is missing");
        // docs/live-funcs-step2-items.md, B6. A layout by hand, a copy of plain bytes, a stub for FName text: nothing
        // here reads the game.
        using PK = Ubel::ParamKind;
        using SM = Ubel::SnapMark;
        auto field = [](const char* name, const char* type, int32_t off, int32_t size, PK kind = PK::In) {
            Ubel::ParamField f; f.name = name; f.typeName = type; f.offset = off; f.size = size; f.kind = kind; return f;
        };
        Ubel::ParamLayout L;
        L.params.push_back(field("F", "FloatProperty", 0, 4));
        L.params.push_back(field("D", "DoubleProperty", 8, 8));
        L.params.push_back(field("I8", "Int8Property", 16, 1));
        auto packed = field("B", "BoolProperty", 17, 1); packed.boolMask = 0x04; L.params.push_back(packed);
        L.params.push_back(field("BU", "BoolProperty", 18, 1));   // its layout was not resolved
        auto e = field("E", "EnumProperty", 19, 1); e.enumName = "EKind"; e.enumEntries = { { 0, "EKind::A" }, { 255, "EKind::MAX" } };
        L.params.push_back(e);
        auto e2 = e; e2.name = "E2"; e2.offset = 20; L.params.push_back(e2);
        auto by = field("By", "ByteProperty", 21, 1); L.params.push_back(by);
        L.params.push_back(field("N", "NameProperty", 24, 8));
        auto v = field("V", "StructProperty", 32, 24); v.structType = "Vector";
        v.sub = { field("X", "DoubleProperty", 0, 8), field("Y", "DoubleProperty", 8, 8), field("Z", "DoubleProperty", 16, 8) };
        L.params.push_back(v);
        auto arr = field("A", "IntProperty", 56, 4); arr.arrayDim = 3; L.params.push_back(arr);
        L.params.push_back(field("O", "IntProperty", 68, 4, PK::Out));
        L.params.push_back(field("R", "IntProperty", 72, 4, PK::Return));
        L.params.push_back(field("Past", "IntProperty", 200, 4));
        L.params.push_back(field("Odd", "WhateverProperty", 76, 4));

        uint8_t b[80] = {};
        const float f = 1.5f; memcpy(b + 0, &f, 4);
        const double d = 20.5; memcpy(b + 8, &d, 8);
        b[16] = 0xFF;                 // Int8 -1
        b[17] = 0x04;                 // the packed bit set, its siblings clear
        b[18] = 0x02;                 // unresolved: the whole byte
        b[19] = 255; b[20] = 7; b[21] = 200;
        const int32_t nm[2] = { 21, 3 }; memcpy(b + 24, nm, 8);
        const double xyz[3] = { 1.5, 2, 3 }; memcpy(b + 32, xyz, 24);
        const int32_t a3[3] = { 1, 2, 3 }; memcpy(b + 56, a3, 12);
        const int32_t o = 41, r = 99; memcpy(b + 68, &o, 4); memcpy(b + 72, &r, 4);
        const uint8_t odd[4] = { 0xDE, 0xAD, 0xBE, 0xEF }; memcpy(b + 76, odd, 4);
        Ubel::SnapDecodeCtx ctx;
        ctx.fname = [](int32_t i, int32_t n) -> std::string { return i == 21 ? (n ? "Tag_" + std::to_string(n - 1) : "Tag") : "?"; };

        const auto in = Ubel::DecodeParamSnapshot(L, b, sizeof b, false, ctx);
        const bool shaped = in.size() == L.params.size();
        check("one value per parameter", shaped, std::to_string(in.size()).c_str());
        if (shaped) {
            auto is = [&](size_t i, const char* text, SM mark) { return in[i].text == text && in[i].mark == mark; };
            check("float and double", is(0, "1.5", SM::Exact) && is(1, "20.5", SM::Exact), (in[0].text + "/" + in[1].text).c_str());
            check("an Int8 is signed", is(2, "-1", SM::Exact), in[2].text.c_str());
            check("a packed bool reads its own bit", is(3, "true", SM::Exact), in[3].text.c_str());
            check("an unresolved bool reads the whole byte, and says so", is(4, "true (layout unresolved)", SM::Exact),
                  in[4].text.c_str());
            check("an enum value named from its table", is(5, "EKind::MAX (255)", SM::Exact), in[5].text.c_str());
            check("...and one the table does not have", is(6, "7 (not in EKind)", SM::Exact), in[6].text.c_str());
            check("a plain byte", is(7, "200", SM::Exact), in[7].text.c_str());
            check("an FName with its Number, through the name pool", is(8, "Tag_2", SM::Exact), in[8].text.c_str());
            check("a struct: its members, from their own offsets", is(9, "{X=1.5, Y=2, Z=3}", SM::Exact) &&
                  in[9].sub.size() == 3 && in[9].sub[2].text == "3", in[9].text.c_str());
            check("a static array", is(10, "[1, 2, 3]", SM::Exact), in[10].text.c_str());
            check("an out parameter as the call began", is(11, "41", SM::Exact), in[11].text.c_str());
            check("the return value, at the call: not yet", is(12, "\xE2\x80\x94", SM::Missing), in[12].text.c_str());
            check("a parameter past the copy's end", in[13].mark == SM::Missing, in[13].text.c_str());
            check("a type the decoder does not read: hex", is(14, "DE AD BE EF", SM::Raw), in[14].text.c_str());
        }
        const auto out = Ubel::DecodeParamSnapshot(L, b, sizeof b, true, ctx);
        check("after the call: the out parameter and the return value, nothing else",
              out.size() == L.params.size() && out[11].text == "41" && out[11].mark == SM::Exact &&
              out[12].text == "99" && out[12].mark == SM::Exact && out[0].mark == SM::Missing && out[0].text.empty());
        const auto cut = Ubel::DecodeParamSnapshot(L, b, 12, false, ctx);
        check("a copy cut short: what lies past it is missing, what fits is read",
              cut.size() == L.params.size() && cut[0].text == "1.5" && cut[1].mark == SM::Missing);
        const auto cutArr = Ubel::DecodeParamSnapshot(L, b, 64, false, ctx);   // ends inside A[3] (56..68)
        check("...an array the copy ends inside is missing whole, not shown in part",
              cutArr.size() == L.params.size() && cutArr[10].mark == SM::Missing && cutArr[9].text == "{X=1.5, Y=2, Z=3}",
              cutArr.size() == L.params.size() ? cutArr[10].text.c_str() : "");
    }

    {
        blk("LIVEFUNCS-STEP2: a parameter copy decoded -- objects, weak and soft pointers, strings and containers");
        // docs/live-funcs-step2-items.md, B7. Pointers name what is there NOW (marked so); string and container data
        // was not copied, only their headers. 0x1000 is a live Pawn_0; weak {5, 7} resolves to it.
        using SM = Ubel::SnapMark;
        auto field = [](const char* name, const char* type, int32_t off, int32_t size) {
            Ubel::ParamField f; f.name = name; f.typeName = type; f.offset = off; f.size = size; return f;
        };
        Ubel::ParamLayout L;
        L.params = { field("Who", "ObjectProperty", 0x00, 8), field("Gone", "ObjectProperty", 0x08, 8),
                     field("Null", "ObjectProperty", 0x10, 8), field("Weak", "WeakObjectProperty", 0x18, 8),
                     field("WeakStale", "WeakObjectProperty", 0x20, 8), field("Soft", "SoftObjectProperty", 0x28, 0x30),
                     field("Lazy", "LazyObjectProperty", 0x58, 0x1C), field("S", "StrProperty", 0x78, 16),
                     field("Arr", "ArrayProperty", 0x88, 16), field("Map", "MapProperty", 0x98, 0x50),
                     field("T", "TextProperty", 0xE8, 0x18), field("Dg", "DelegateProperty", 0x100, 16),
                     field("SD", "MulticastSparseDelegateProperty", 0x110, 1),
                     field("MD", "MulticastInlineDelegateProperty", 0x118, 16), field("Iface", "InterfaceProperty", 0x128, 16) };
        auto opt = field("OptT", "OptionalProperty", 0x138, 8);
        opt.optLayout = static_cast<uint8_t>(Ubel::OptionalLayout::TrailingFlag); opt.optInnerType = "IntProperty"; opt.optInnerSize = 4;
        L.params.push_back(opt);
        auto optU = opt; optU.name = "OptU"; optU.offset = 0x140; L.params.push_back(optU);
        auto optN = field("OptN", "OptionalProperty", 0x148, 8);
        optN.optLayout = static_cast<uint8_t>(Ubel::OptionalLayout::Intrusive); optN.optInnerType = "NameProperty"; optN.optInnerSize = 8;
        L.params.push_back(optN);

        alignas(8) uint8_t b[0x150] = {};
        auto put64 = [&](int off, uint64_t v) { memcpy(b + off, &v, 8); };
        auto put32 = [&](int off, int32_t v) { memcpy(b + off, &v, 4); };
        put64(0x00, 0x1000); put64(0x08, 0x2000);                               // live, gone; Null stays 0
        put32(0x18, 5); put32(0x1C, 7); put32(0x20, 6); put32(0x24, 9);         // weak: resolves; stale
        put32(0x28 + 0x10, 31); put32(0x28 + 0x18, 32);                         // soft: package and asset FNames
        put32(0x58 + 0x0C, 1); put32(0x58 + 0x10, 2); put32(0x58 + 0x14, 3); put32(0x58 + 0x18, 4);   // lazy GUID
        put64(0x78, 0xABC0); put32(0x80, 5);                                    // FString header: Num 5
        put64(0x88, 0xDEF0); put32(0x90, 3);                                    // TArray: Num 3
        put64(0x98, 0x5550); put32(0xA0, 4); put32(0x98 + 0x34, 1);             // TMap: 4 slots, 1 free
        put32(0x100, 5); put32(0x104, 7); put32(0x108, 33);                     // delegate: weak to Pawn_0, OnHit
        b[0x110] = 1;
        put64(0x118, 0x7770); put32(0x120, 2);
        put64(0x128, 0x1000);                                                   // interface: its object half
        put32(0x138, 42); b[0x13C] = 1;                                         // TOptional<int32>: set
        put32(0x140, 9);  b[0x144] = 0;                                         //   ...unset
        put32(0x148, -1);                                                       // TOptional<FName>: index ~0u, unset

        Ubel::SnapDecodeCtx ctx;
        ctx.fname = [](int32_t i, int32_t) -> std::string {
            return i == 31 ? "/Game/Maps/Arena" : i == 32 ? "Arena" : i == 33 ? "OnHit" : "None";
        };
        ctx.object = [](uintptr_t p, std::string& name, std::string& cls) -> bool {
            if (p != 0x1000) return false;
            name = "Pawn_0"; cls = "Pawn_C"; return true;
        };
        ctx.weak = [](int32_t idx, int32_t serial) -> uintptr_t { return (idx == 5 && serial == 7) ? 0x1000 : 0; };
        ctx.softPathOffset = 0x10; ctx.softTopLevel = 1; ctx.fnameSize = 8; ctx.lazyGuidOffset = 0x0C;

        const auto v = Ubel::DecodeParamSnapshot(L, b, sizeof b, false, ctx);
        const bool shaped = v.size() == L.params.size();
        check("one value per parameter", shaped, std::to_string(v.size()).c_str());
        if (shaped) {
            auto is = [&](size_t i, const std::string& text, SM mark) { return v[i].text == text && v[i].mark == mark; };
            check("an object: its name and class now", is(0, "Pawn_0 (Pawn_C)", SM::Now), v[0].text.c_str());
            check("an address with no live object now", is(1, "0x2000 (no longer a live object)", SM::Gone), v[1].text.c_str());
            check("a null object", is(2, "null", SM::Exact), v[2].text.c_str());
            check("a weak pointer that resolves", is(3, "Pawn_0 (Pawn_C)", SM::Now), v[3].text.c_str());
            check("...and one that no longer does", is(4, "null (stale)", SM::Gone), v[4].text.c_str());
            check("a soft pointer: its path, from FNames, which never go stale", is(5, "/Game/Maps/Arena.Arena", SM::Exact),
                  v[5].text.c_str());
            check("a lazy pointer: its GUID", is(6, "{00000001-00000002-00000003-00000004}", SM::Exact), v[6].text.c_str());
            check("a string: its header only", is(7, "Num=5 (Data 0xABC0)", SM::Header), v[7].text.c_str());
            check("an array: its header only", is(8, "Num=3 (Data 0xDEF0)", SM::Header), v[8].text.c_str());
            check("a map: its element count, the free slots taken off", is(9, "Num=3 (Data 0x5550)", SM::Header),
                  v[9].text.c_str());
            check("an empty text", is(10, "(empty)", SM::Header), v[10].text.c_str());
            check("a delegate: through the shared binding text", is(11, "Pawn_0::OnHit", SM::Now), v[11].text.c_str());
            check("a sparse delegate's byte", is(12, "sparse (1)", SM::Exact), v[12].text.c_str());
            check("a multicast delegate: its list's header", is(13, "Num=2 (Data 0x7770)", SM::Header), v[13].text.c_str());
            check("an interface: its object half", is(14, "Pawn_0 (Pawn_C)", SM::Now), v[14].text.c_str());
            check("a set TOptional: its value", is(15, "42", SM::Exact), v[15].text.c_str());
            check("...an unset one", is(16, "unset", SM::Exact), v[16].text.c_str());
            check("...an intrusive one, unset by its type's sentinel", is(17, "unset", SM::Exact), v[17].text.c_str());
        }
        // A case-preserving build: sizeof(FName) is 12, so the asset's FName starts 12 bytes after the package's.
        Ubel::ParamLayout cpn;
        cpn.params = { field("Soft", "SoftObjectProperty", 0, 0x38) };
        alignas(8) uint8_t c[0x40] = {};
        const int32_t pk = 31, as = 32;
        memcpy(c + 0x10, &pk, 4);
        memcpy(c + 0x10 + 12, &as, 4);
        Ubel::SnapDecodeCtx ctx12 = ctx;
        ctx12.fnameSize = 12;
        const auto vc = Ubel::DecodeParamSnapshot(cpn, c, sizeof c, false, ctx12);
        check("a soft path on a case-preserving build: the asset's FName after a 12-byte one",
              vc.size() == 1 && vc[0].text == "/Game/Maps/Arena.Arena", vc.empty() ? "" : vc[0].text.c_str());
        // Measured on DumperTest58 (2026-10-07): a soft pointer to an actor is its level's asset path plus a sub-path
        // (":PersistentLevel.<actor>") in an FString whose text is not in the copy. Shown without it, the actor's
        // pointer read as the level's.
        alignas(8) uint8_t s2[0x50] = {};
        memcpy(s2, b + 0x28, 0x30);
        const int32_t subNum = 34;
        memcpy(s2 + 0x20 + 8, &subNum, 4);   // SubPathString {Data, Num, Max} after the two FNames
        Ubel::ParamLayout sp;
        sp.params = { field("Soft", "SoftObjectProperty", 0, 0x30) };
        const auto vs = Ubel::DecodeParamSnapshot(sp, s2, 0x30, false, ctx);
        check("a soft path with a sub-path says so, and that its text was not copied",
              vs.size() == 1 && vs[0].text == "/Game/Maps/Arena.Arena:<sub-path, 33 chars, not copied>" &&
              vs[0].mark == SM::Header, vs.empty() ? "" : vs[0].text.c_str());
    }

    {
        blk("LIVEFUNCS-STEP2: a slot decoded with its own arm's layout -- two loads of one name, arms without one");
        // docs/live-funcs-step2-items.md, F5. The pipe's pe_snap_get decodes through this; nothing here reads the game.
        using LS = Linie::ArmLayoutState;
        auto lay = [](int32_t off) {
            auto L = std::make_shared<Ubel::ParamLayout>();
            Ubel::ParamField f; f.name = "X"; f.typeName = "IntProperty"; f.offset = off; f.size = 4;
            L->params.push_back(f);
            return L;
        };
        std::vector<Linie::ArmView> arms(4);
        arms[0].index = 0; arms[0].state = LS::Read; arms[0].layout = lay(0);
        arms[1].index = 1; arms[1].state = LS::Read; arms[1].layout = lay(4);   // the same name reloaded: X moved
        arms[2].index = 2; arms[2].state = LS::NotReadBeforeStop;
        arms[3].index = 3; arms[3].state = LS::Doubtful; arms[3].layout = lay(4);
        uint8_t b[8] = {};
        const int32_t v0 = 7, v1 = 9; memcpy(b, &v0, 4); memcpy(b + 4, &v1, 4);
        Ubel::SnapDecodeCtx ctx;
        const auto d0 = Ubel::DecodeSlot(arms, 0, b, 8, false, ctx);
        const auto d1 = Ubel::DecodeSlot(arms, 1, b, 8, false, ctx);
        const auto d2 = Ubel::DecodeSlot(arms, 2, b, 8, false, ctx);
        const auto d3 = Ubel::DecodeSlot(arms, 3, b, 8, false, ctx);
        const auto d9 = Ubel::DecodeSlot(arms, 9, b, 8, false, ctx);
        check("arm 0's slot read with arm 0's layout", d0.layout && d0.values.size() == 1 && d0.values[0].text == "7",
              d0.values.empty() ? "" : d0.values[0].text.c_str());
        check("arm 1's slot with arm 1's layout, not arm 0's", d1.layout && d1.values.size() == 1 &&
              d1.values[0].text == "9", d1.values.empty() ? "" : d1.values[0].text.c_str());
        check("an arm sealed before its read stays raw, and says so",
              !d2.layout && d2.values.empty() && d2.state == LS::NotReadBeforeStop);
        check("a doubtful layout still decodes", d3.layout && d3.values.size() == 1 && d3.values[0].text == "9");
        check("an arm the log does not hold: raw", !d9.layout && d9.values.empty());
        const std::vector<Linie::ArmView> rev = { arms[1], arms[0] };
        const auto r1 = Ubel::DecodeSlot(rev, 1, b, 8, false, ctx);
        check("an arm found by its index, not its place in the list", r1.values.size() == 1 && r1.values[0].text == "9");
        const auto a1 = Ubel::DecodeSlot(arms, 1, b, 8, true, ctx);
        check("an after copy leaves an In parameter blank", a1.values.size() == 1 &&
              a1.values[0].mark == Ubel::SnapMark::Missing && a1.values[0].text.empty());
    }

    {
        blk("LIVEFUNCS-STEP2: a function's name key -- read with loads only, checked against the names the UI shows");
        // docs/live-funcs-step2-items.md, B2 (T10). ⛔ POOL-FAKING: its own UE4-style pool, first (the TMAPGEOM header).
        static uint8_t nkEntry[24][0x40] = {};
        static uintptr_t nkChunk[25] = {};
        const char* nkNames[24] = {};
        nkNames[21] = "Fire"; nkNames[22] = "Actor_C"; nkNames[23] = "Other_C";
        for (int i = 1; i < 24; ++i) {
            const char* n = nkNames[i] ? nkNames[i] : "Pad";
            memcpy(nkEntry[i] + 0x10, n, strlen(n) + 1);
            nkChunk[i] = reinterpret_cast<uintptr_t>(nkEntry[i]);
        }
        static uintptr_t nkChunks[2] = { reinterpret_cast<uintptr_t>(nkChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(nkChunks), 0x10);
        check("setup: the pool resolves the names", Serie::GetString(21) == "Fire" && Serie::GetString(22) == "Actor_C" &&
              Serie::GetString(21, 3) == "Fire_2", Serie::GetString(21, 3).c_str());

        static uint8_t fn[0x100] = {}, cls[0x100] = {}, fn3[0x100] = {};
        auto put   = [](uint8_t* base, int off, uintptr_t v) { memcpy(base + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* base, int off, int32_t v)   { memcpy(base + off, &v, sizeof(v)); };
        put32(fn, Grimoire::OFF_UOBJECT_NAME, 21);
        put(fn, DynOff::UOBJECT_OUTER, reinterpret_cast<uintptr_t>(cls));
        put32(cls, Grimoire::OFF_UOBJECT_NAME, 22);
        put32(fn3, Grimoire::OFF_UOBJECT_NAME, 21);
        put32(fn3, Grimoire::OFF_UOBJECT_NAME + DynOff::FNAME_NUMBER, 3);
        put(fn3, DynOff::UOBJECT_OUTER, reinterpret_cast<uintptr_t>(cls));

        Linie::NameKey k{};
        check("ReadNameKey: the function's FName ints and its class's",
              Ubel::ReadNameKey(reinterpret_cast<uintptr_t>(fn), k) && k == Linie::NameKey{ 21, 0, 22, 0 },
              std::to_string(k.fnIdx).c_str());
        check("NameKeyMatches: the names the UI shows", Ubel::NameKeyMatches(k, "Actor_C", "Fire"));
        check("...not under another class", !Ubel::NameKeyMatches(k, "Other_C", "Fire"));
        Linie::NameKey k3{};
        check("a Number renders as the UI shows it, and the bare name is another name",
              Ubel::ReadNameKey(reinterpret_cast<uintptr_t>(fn3), k3) && k3 == Linie::NameKey{ 21, 3, 22, 0 } &&
              Ubel::NameKeyMatches(k3, "Actor_C", "Fire_2") && !Ubel::NameKeyMatches(k3, "Actor_C", "Fire"));
        Linie::NameKey bad{};
        check("an address that cannot be read is refused", !Ubel::ReadNameKey(0x1000, bad));
        int32_t oi = 0, on = 0;
        check("ReadObjectNameKey: an object's FName ints",
              Ubel::ReadObjectNameKey(reinterpret_cast<uint64_t>(cls), oi, on) && oi == 22 && on == 0);
        check("...and an unreadable object is refused", !Ubel::ReadObjectNameKey(0x1000, oi, on));

        // The UObject header of every engine the project supports (UE 4.11 - 4.27 and UE5; the maintainer, 2026-10-07:
        // UE4 is covered here, not live). VTable, flags, index and class are fixed; NamePrivate is at 0x18 and its
        // ComparisonIndex first; what moves is the FName's size: standard, Number at +4 and Outer at 0x20; case-
        // preserving (a DisplayIndex added), Number at +4 or +8 and Outer at 0x28 (Grimoire.h, the UObject offsets).
        struct HeaderLayout { const char* name; bool cpn; int number; int outer; };
        const HeaderLayout layouts[] = {
            { "standard (UE 4.11 - 4.27, UE5)", false, 4, 0x20 },
            { "case-preserving, Number at +4", true, 4, 0x28 },
            { "case-preserving, Number at +8", true, 8, 0x28 },
        };
        const int  savedNumber = DynOff::FNAME_NUMBER, savedOuter = DynOff::UOBJECT_OUTER;
        const bool savedCpn = DynOff::bCasePreservingName;
        for (const HeaderLayout& L : layouts) {
            DynOff::FNAME_NUMBER = L.number;
            DynOff::UOBJECT_OUTER = L.outer;
            DynOff::bCasePreservingName = L.cpn;
            static uint8_t lf[0x100], lc[0x100];
            memset(lf, 0, sizeof lf);
            memset(lc, 0, sizeof lc);
            put32(lf, Grimoire::OFF_UOBJECT_NAME, 21);
            put32(lf, Grimoire::OFF_UOBJECT_NAME + L.number, 3);
            if (L.cpn) put32(lf, Grimoire::OFF_UOBJECT_NAME + (L.number == 4 ? 8 : 4), 21);   // the DisplayIndex
            put(lf, L.outer, reinterpret_cast<uintptr_t>(lc));
            put32(lc, Grimoire::OFF_UOBJECT_NAME, 22);
            Linie::NameKey lk{};
            const std::string what = std::string(L.name);
            check(("ReadNameKey, " + what + ": the Number and the Outer where this header keeps them").c_str(),
                  Ubel::ReadNameKey(reinterpret_cast<uintptr_t>(lf), lk) && lk == Linie::NameKey{ 21, 3, 22, 0 } &&
                  Ubel::NameKeyMatches(lk, "Actor_C", "Fire_2"),
                  (std::to_string(lk.fnNum) + "/" + std::to_string(lk.clsIdx)).c_str());
            int32_t ci = 0, cn = 0;
            check(("ReadObjectNameKey, " + what).c_str(),
                  Ubel::ReadObjectNameKey(reinterpret_cast<uint64_t>(lf), ci, cn) && ci == 21 && cn == 3);
        }
        DynOff::FNAME_NUMBER = savedNumber;
        DynOff::UOBJECT_OUTER = savedOuter;
        DynOff::bCasePreservingName = savedCpn;
    }

    {
        blk("LIVEFUNCS-STEP2: a chosen function's parameter layout -- its own chain, CPF_Parm only, both property models");
        // docs/live-funcs-step2-items.md, B3. ⛔ POOL-FAKING: own pool, first. The layouts the project supports (the
        // maintainer, 2026-10-07: UE4 in the tests): FField on UE5 / 4.25+, standard and case-preserving headers;
        // UProperty on 4.15 (4.11-4.17: the first subclass field at +0x28) and 4.22 (4.18-4.24: +0x2C).
        static uint8_t plEntry[22][0x40] = {};
        static uintptr_t plChunk[23] = {};
        const char* plNames[22] = { "", "Function", "IntProperty", "ObjectProperty", "StructProperty", "BoolProperty",
                                    "Count", "Who", "Hit", "Flag", "ReturnValue", "Temp_Local", "DoIt", "MyActor_C",
                                    "Class", "ScriptStruct", "Actor", "HitResult", "Parent", "Inherited", "NoParms",
                                    "Decoy" };
        for (int i = 1; i < 22; ++i) {
            memcpy(plEntry[i] + 0x10, plNames[i], strlen(plNames[i]) + 1);
            plChunk[i] = reinterpret_cast<uintptr_t>(plEntry[i]);
        }
        static uintptr_t plChunks[2] = { reinterpret_cast<uintptr_t>(plChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(plChunks), 0x10);
        check("setup: the pool resolves Function", Serie::GetString(1) == "Function");

        const bool savedFProp = DynOff::bUseFProperty, savedCpn = DynOff::bCasePreservingName;
        const int savedOuter = DynOff::UOBJECT_OUTER, savedNum = DynOff::FNAME_NUMBER, savedOff = DynOff::UPROPERTY_OFFSET;
        const int savedStart = DynOff::UPROPERTY_SUBCLASS_START;
        const uint32_t savedVer = g_cachedUEVersion;
        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto put64 = [](uint8_t* b, int off, uint64_t v)  { memcpy(b + off, &v, sizeof(v)); };
        static uint8_t named[22][0x100];
        static uint8_t fclassBlob[22][0x20];
        auto obj = [&](int idx) { return reinterpret_cast<uintptr_t>(named[idx]); };
        auto fclass = [&](int idx) { return reinterpret_cast<uintptr_t>(fclassBlob[idx]); };

        struct Layout { const char* name; bool fprop, cpn; unsigned ver; int offsetInternal, slot; };
        const Layout layouts[] = {
            { "FField (UE5, 4.25+), standard header", true, false, 504, 0, 0 },
            { "FField, case-preserving header", true, true, 427, 0, 0 },
            { "UProperty 4.15", false, false, 415, 0x50, 0x78 },
            { "UProperty 4.22", false, false, 422, 0x44, 0x70 },
        };
        constexpr uint64_t kParm = 0x80, kOut = 0x100, kRet = 0x400, kPod = 0x0008001040000200ull;
        struct Entry { int name, type; int32_t size, offset, arrayDim; uint64_t flags; int slotObj; };
        // Count[2] (in), Who (an object -> Actor), Hit (an out struct -> HitResult), Flag (a packed bool), the return,
        // then a Blueprint local past them.
        const Entry chain[6] = { { 6, 2, 4, 0x00, 2, kParm | kPod, 0 },  { 7, 3, 8, 0x08, 1, kParm, 16 },
                                 { 8, 4, 0x88, 0x10, 1, kParm | kOut, 17 }, { 9, 5, 1, 0x98, 1, kParm, 0 },
                                 { 10, 2, 4, 0xA0, 1, kParm | kOut | kRet | kPod, 0 }, { 11, 2, 4, 0xA4, 1, kPod, 0 } };
        static uint8_t fnBlob[0x200], parentBlob[0x200], notFn[0x200], bareFn[0x200];
        static uint8_t props[6][0x100], parentProp[0x100], bareProp[0x100];

        for (const Layout& L : layouts) {
            memset(named, 0, sizeof named);
            memset(fclassBlob, 0, sizeof fclassBlob);
            memset(fnBlob, 0, sizeof fnBlob); memset(parentBlob, 0, sizeof parentBlob);
            memset(notFn, 0, sizeof notFn); memset(bareFn, 0, sizeof bareFn);
            memset(props, 0, sizeof props); memset(parentProp, 0, sizeof parentProp); memset(bareProp, 0, sizeof bareProp);
            DynOff::bUseFProperty = L.fprop;
            DynOff::bCasePreservingName = L.cpn;
            DynOff::UOBJECT_OUTER = L.cpn ? 0x28 : 0x20;
            DynOff::FNAME_NUMBER = 4;
            g_cachedUEVersion = L.ver;
            if (!L.fprop) { DynOff::UPROPERTY_OFFSET = L.offsetInternal; DynOff::UPROPERTY_SUBCLASS_START = 0; }
            for (int i = 1; i < 22; ++i) {
                put32(named[i], Grimoire::OFF_UOBJECT_NAME, i);
                put32(fclassBlob[i], DynOff::FFIELDCLASS_NAME, i);
            }
            putP(named[16], Grimoire::OFF_UOBJECT_CLASS, obj(14));   // Actor : Class
            putP(named[13], Grimoire::OFF_UOBJECT_CLASS, obj(14));   // MyActor_C : Class
            putP(named[21], Grimoire::OFF_UOBJECT_CLASS, obj(14));   // Decoy : Class
            putP(named[17], Grimoire::OFF_UOBJECT_CLASS, obj(15));   // HitResult : ScriptStruct

            const int nextOff  = L.fprop ? DynOff::FFIELD_NEXT : DynOff::UFIELD_NEXT;
            const int elemOff  = L.fprop ? DynOff::FPROPERTY_ELEMSIZE : DynOff::UPROPERTY_ELEMSIZE;
            const int flagsOff = L.fprop ? DynOff::FPROPERTY_FLAGS : DynOff::UPROPERTY_FLAGS;
            const int offOff   = L.fprop ? DynOff::FPROPERTY_OFFSET : DynOff::UPROPERTY_OFFSET;
            const int slotOff  = L.fprop ? DynOff::FSTRUCTPROP_STRUCT : L.slot;
            const int boolOff  = L.fprop ? DynOff::FBOOLPROP_FIELDSIZE : DynOff::UBOOLPROP_FIELDSIZE;
            auto writeProp = [&](uint8_t* pr, const Entry& e, uintptr_t next) {
                if (L.fprop) { putP(pr, DynOff::FFIELD_CLASS, fclass(e.type)); put32(pr, DynOff::FFIELD_NAME, e.name); }
                else         { putP(pr, Grimoire::OFF_UOBJECT_CLASS, obj(e.type)); put32(pr, Grimoire::OFF_UOBJECT_NAME, e.name); }
                put32(pr, elemOff - 4, e.arrayDim);
                put32(pr, elemOff, e.size);
                put64(pr, flagsOff, e.flags);
                put32(pr, offOff, e.offset);
                if (e.slotObj) putP(pr, slotOff, obj(e.slotObj));
                if (!L.fprop && slotOff != DynOff::FSTRUCTPROP_STRUCT && e.slotObj)
                    putP(pr, DynOff::FSTRUCTPROP_STRUCT, obj(21));   // a class where an FField would keep it: a decoy
                if (e.type == 5) { pr[boolOff] = 1; pr[boolOff + 1] = 0; pr[boolOff + 2] = 0x04; pr[boolOff + 3] = 0x04; }
                putP(pr, nextOff, next);
            };
            for (int i = 0; i < 6; ++i)
                writeProp(props[i], chain[i], i < 5 ? reinterpret_cast<uintptr_t>(props[i + 1]) : 0);
            auto writeFn = [&](uint8_t* fb, int name, uintptr_t first) {
                putP(fb, Grimoire::OFF_UOBJECT_CLASS, obj(1));
                put32(fb, Grimoire::OFF_UOBJECT_NAME, name);
                putP(fb, DynOff::UOBJECT_OUTER, obj(13));
                putP(fb, L.fprop ? DynOff::USTRUCT_CHILDPROPS : DynOff::USTRUCT_CHILDREN, first);
            };
            writeFn(fnBlob, 12, reinterpret_cast<uintptr_t>(props[0]));
            // The parent this function overrides, with a parameter of its own: never this function's.
            writeProp(parentProp, Entry{ 19, 2, 4, 0, 1, kParm, 0 }, 0);
            writeFn(parentBlob, 18, reinterpret_cast<uintptr_t>(parentProp));
            putP(fnBlob, DynOff::USTRUCT_SUPER, reinterpret_cast<uintptr_t>(parentBlob));

            const std::string who = L.name;
            Ubel::ParamLayout pl;
            std::string why;
            const bool ok = Ubel::CaptureParamLayout(reinterpret_cast<uintptr_t>(fnBlob), pl, why);
            check(("CaptureParamLayout, " + who + ": the five parameters, not the local, not the parent's").c_str(),
                  ok && pl.params.size() == 5 && pl.params[0].name == "Count" && pl.params[4].name == "ReturnValue",
                  (why + " " + std::to_string(pl.params.size())).c_str());
            if (!ok || pl.params.size() != 5) continue;
            check(("..." + who + ": the function and its class").c_str(), pl.funcName == "DoIt" && pl.className == "MyActor_C",
                  pl.className.c_str());
            using PK = Ubel::ParamKind;
            check(("..." + who + ": each parameter's way").c_str(),
                  pl.params[0].kind == PK::In && pl.params[2].kind == PK::Out && pl.params[4].kind == PK::Return);
            check(("..." + who + ": types, offsets, sizes, the static array").c_str(),
                  pl.params[0].typeName == "IntProperty" && pl.params[0].arrayDim == 2 && pl.params[1].offset == 8 &&
                  pl.params[2].size == 0x88 && pl.layoutEnd == 0xA4, std::to_string(pl.layoutEnd).c_str());
            check(("..." + who + ": the packed bool's bit").c_str(), pl.params[3].boolMask == 0x04);
            check(("..." + who + ": the object's class and the struct's type, from this model's slot").c_str(),
                  pl.params[1].objClass == "Actor" && pl.params[2].structType == "HitResult",
                  (pl.params[1].objClass + "/" + pl.params[2].structType).c_str());
            putP(props[1], slotOff, obj(17));   // a struct where the object's class should be
            putP(props[2], slotOff, obj(16));   // a class where the struct should be
            Ubel::ParamLayout wrong;
            Ubel::CaptureParamLayout(reinterpret_cast<uintptr_t>(fnBlob), wrong, why);
            check(("..." + who + ": a slot holding the wrong kind of object names nothing").c_str(),
                  wrong.params.size() == 5 && wrong.params[1].objClass.empty() && wrong.params[2].structType.empty(),
                  wrong.params.size() == 5 ? (wrong.params[1].objClass + "/" + wrong.params[2].structType).c_str() : "");
            putP(props[1], slotOff, obj(16));
            putP(props[2], slotOff, obj(17));

            putP(notFn, Grimoire::OFF_UOBJECT_CLASS, obj(14));     // a Class, not a Function
            put32(notFn, Grimoire::OFF_UOBJECT_NAME, 21);
            check(("..." + who + ": not a UFunction is refused").c_str(),
                  !Ubel::CaptureParamLayout(reinterpret_cast<uintptr_t>(notFn), pl, why) &&
                  why.find("not a UFunction") != std::string::npos, why.c_str());
            writeProp(bareProp, chain[5], 0);                      // only a local
            writeFn(bareFn, 20, reinterpret_cast<uintptr_t>(bareProp));
            check(("..." + who + ": a function without parameters is refused").c_str(),
                  !Ubel::CaptureParamLayout(reinterpret_cast<uintptr_t>(bareFn), pl, why) &&
                  why.find("no parameters") != std::string::npos, why.c_str());
            put32(props[1], offOff, 0x20000);                      // an offset no parameter block has
            check(("..." + who + ": an implausible layout is refused").c_str(),
                  !Ubel::CaptureParamLayout(reinterpret_cast<uintptr_t>(fnBlob), pl, why) &&
                  why.find("implausible") != std::string::npos, why.c_str());
        }
        DynOff::bUseFProperty = savedFProp;
        DynOff::bCasePreservingName = savedCpn;
        DynOff::UOBJECT_OUTER = savedOuter;
        DynOff::FNAME_NUMBER = savedNum;
        DynOff::UPROPERTY_OFFSET = savedOff;
        DynOff::UPROPERTY_SUBCLASS_START = savedStart;
        g_cachedUEVersion = savedVer;
    }

    {
        blk("LIVEFUNCS-STEP2: the layout by value -- struct members (supers, nesting, a cap), enum tables read fresh");
        // docs/live-funcs-step2-items.md, B4. ⛔ POOL-FAKING: own pool, first. FField and UProperty 4.22 (UE4 in the
        // tests). The second design critic: WalkClassEx's memo and s_enumCache are address-keyed and never erased, so
        // under widget reload churn a freed struct's or enum's address can hold another -- the capture uses neither.
        enum : int { nFunction = 1, nInt, nStructP, nBoolP, nEnumP, nByteP, nFloatP, nHit, nKind, nLevel, nLooped,
                     nDoIt, nMyActor, nClass, nScriptStruct, nEnum, nHitResult, nBaseResult, nInnerStruct, nX, nBase,
                     nInner, nOn, nEKind, nEKindA, nEKindB, nLoop, nNext, nStale, nCount };
        const char* lvNames[nCount] = { "", "Function", "IntProperty", "StructProperty", "BoolProperty",
            "EnumProperty", "ByteProperty", "FloatProperty", "Hit", "Kind", "Level", "Looped", "DoIt", "MyActor_C",
            "Class", "ScriptStruct", "Enum", "HitResult", "BaseResult", "InnerStruct", "X", "Base", "Inner", "bOn",
            "EKind", "EKind::A", "EKind::B", "Loop", "Next", "Stale" };
        static uint8_t lvEntry[nCount][0x40] = {};
        static uintptr_t lvChunk[nCount + 1] = {};
        for (int i = 1; i < nCount; ++i) {
            memcpy(lvEntry[i] + 0x10, lvNames[i], strlen(lvNames[i]) + 1);
            lvChunk[i] = reinterpret_cast<uintptr_t>(lvEntry[i]);
        }
        static uintptr_t lvChunks[2] = { reinterpret_cast<uintptr_t>(lvChunk), 0 };
        Serie::InitUE4(reinterpret_cast<uintptr_t>(lvChunks), 0x10);
        check("setup: the pool resolves EKind", Serie::GetString(nEKind) == "EKind");

        const bool savedFProp = DynOff::bUseFProperty, savedCpn = DynOff::bCasePreservingName;
        const int savedOff = DynOff::UPROPERTY_OFFSET, savedStart = DynOff::UPROPERTY_SUBCLASS_START;
        const uint32_t savedVer = g_cachedUEVersion;
        const int savedNames = DynOff::UENUM_NAMES, savedWidth = DynOff::UENUM_VALUE_SIZE, savedStride = DynOff::UENUM_PAIR_STRIDE;
        const bool savedNew = DynOff::bEnumNamesNewContainer, savedDet = DynOff::bUEnumNamesDetected.load();
        const bool savedFailed = DynOff::bUEnumNamesFailed.load(), savedProbed = DynOff::bFNameAlignProbed.load();
        const int savedAlign = DynOff::FNAME_ALIGN_MEASURED.load();
        DynOff::bCasePreservingName = false;
        DynOff::bFNameAlignProbed = true;
        DynOff::FNAME_ALIGN_MEASURED = 0;
        DynOff::UENUM_NAMES = 0x40;
        DynOff::bEnumNamesNewContainer = false;
        DynOff::UENUM_VALUE_SIZE = 8;
        DynOff::UENUM_PAIR_STRIDE = 16;
        DynOff::bUEnumNamesDetected = true;
        DynOff::bUEnumNamesFailed = false;

        auto putP  = [](uint8_t* b, int off, uintptr_t v) { memcpy(b + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* b, int off, int32_t v)   { memcpy(b + off, &v, sizeof(v)); };
        auto put64 = [](uint8_t* b, int off, uint64_t v)  { memcpy(b + off, &v, sizeof(v)); };
        alignas(16) static uint8_t obj[nCount][0x200];
        static uint8_t fcls[nCount][0x20];
        alignas(16) static uint8_t enumData[2 * 16];
        enum { pHit, pKind, pLevel, pLooped, mBase, mX, mInner, mOn, mNext, kProps };
        static uint8_t prop[kProps][0x100];
        auto O = [&](int i) { return reinterpret_cast<uintptr_t>(obj[i]); };
        auto P = [&](int i) { return reinterpret_cast<uintptr_t>(prop[i]); };

        struct Layout { const char* name; bool fprop; unsigned ver; int offsetInternal, slot; };
        const Layout layouts[] = { { "FField", true, 504, 0, 0 }, { "UProperty 4.22", false, 422, 0x44, 0x70 } };
        for (const Layout& L : layouts) {
            memset(obj, 0, sizeof obj); memset(fcls, 0, sizeof fcls); memset(prop, 0, sizeof prop);
            DynOff::bUseFProperty = L.fprop;
            g_cachedUEVersion = L.ver;
            if (!L.fprop) { DynOff::UPROPERTY_OFFSET = L.offsetInternal; DynOff::UPROPERTY_SUBCLASS_START = 0; }
            for (int i = 1; i < nCount; ++i) { put32(obj[i], Grimoire::OFF_UOBJECT_NAME, i); put32(fcls[i], DynOff::FFIELDCLASS_NAME, i); }
            for (int s : { nHitResult, nBaseResult, nInnerStruct, nLoop }) putP(obj[s], Grimoire::OFF_UOBJECT_CLASS, O(nScriptStruct));
            putP(obj[nMyActor], Grimoire::OFF_UOBJECT_CLASS, O(nClass));
            for (int e : { nEKind, nStale }) putP(obj[e], Grimoire::OFF_UOBJECT_CLASS, O(nEnum));
            // EKind's Names: {A = 0, B = 2}.
            memset(enumData, 0, sizeof enumData);
            put32(enumData, 0, nEKindA);  put64(enumData, 8, 0);
            put32(enumData, 16, nEKindB); put64(enumData, 24, 2);
            putP(obj[nEKind], 0x40, reinterpret_cast<uintptr_t>(enumData));
            put32(obj[nEKind], 0x48, 2);
            put32(obj[nEKind], 0x4C, 2);

            const int nextOff = L.fprop ? DynOff::FFIELD_NEXT : DynOff::UFIELD_NEXT;
            const int elemOff = L.fprop ? DynOff::FPROPERTY_ELEMSIZE : DynOff::UPROPERTY_ELEMSIZE;
            const int flagsOff = L.fprop ? DynOff::FPROPERTY_FLAGS : DynOff::UPROPERTY_FLAGS;
            const int offOff = L.fprop ? DynOff::FPROPERTY_OFFSET : DynOff::UPROPERTY_OFFSET;
            const int structSlot = L.fprop ? DynOff::FSTRUCTPROP_STRUCT : L.slot;
            const int byteSlot = L.fprop ? DynOff::FBYTEPROP_ENUM : L.slot;
            const int enumSlot = L.fprop ? DynOff::FENUMPROP_ENUM : L.slot + 8;
            const int boolOff = L.fprop ? DynOff::FBOOLPROP_FIELDSIZE : DynOff::UBOOLPROP_FIELDSIZE;
            auto write = [&](int pi, int name, int type, int32_t size, int32_t offset, uintptr_t next, uintptr_t slotObj) {
                uint8_t* pr = prop[pi];
                if (L.fprop) { putP(pr, DynOff::FFIELD_CLASS, reinterpret_cast<uintptr_t>(fcls[type])); put32(pr, DynOff::FFIELD_NAME, name); }
                else         { putP(pr, Grimoire::OFF_UOBJECT_CLASS, O(type)); put32(pr, Grimoire::OFF_UOBJECT_NAME, name); }
                put32(pr, elemOff - 4, 1);
                put32(pr, elemOff, size);
                put64(pr, flagsOff, 0x80);
                put32(pr, offOff, offset);
                if (type == nStructP) putP(pr, structSlot, slotObj);
                if (type == nByteP) putP(pr, byteSlot, slotObj);
                if (type == nEnumP) putP(pr, enumSlot, slotObj);
                if (type == nBoolP) { pr[boolOff] = 1; pr[boolOff + 2] = 0x02; pr[boolOff + 3] = 0x02; }
                putP(pr, nextOff, next);
            };
            const int chainOff = L.fprop ? DynOff::USTRUCT_CHILDPROPS : DynOff::USTRUCT_CHILDREN;
            // HitResult : BaseResult { Base } { X, Inner : InnerStruct { bOn } }; Loop { Next : Loop }.
            write(mBase, nBase, nInt, 4, 0, 0, 0);
            write(mX, nX, nFloatP, 4, 4, P(mInner), 0);
            write(mInner, nInner, nStructP, 1, 8, 0, O(nInnerStruct));
            write(mOn, nOn, nBoolP, 1, 0, 0, 0);
            write(mNext, nNext, nStructP, 8, 0, 0, O(nLoop));
            putP(obj[nBaseResult], chainOff, P(mBase));
            putP(obj[nHitResult], chainOff, P(mX));
            putP(obj[nHitResult], DynOff::USTRUCT_SUPER, O(nBaseResult));
            putP(obj[nInnerStruct], chainOff, P(mOn));
            putP(obj[nLoop], chainOff, P(mNext));
            // DoIt(Hit, Kind, Level, Looped).
            write(pHit, nHit, nStructP, 0x10, 0x00, P(pKind), O(nHitResult));
            write(pKind, nKind, nEnumP, 1, 0x10, P(pLevel), O(nEKind));
            write(pLevel, nLevel, nByteP, 1, 0x11, P(pLooped), O(nEKind));
            write(pLooped, nLooped, nStructP, 8, 0x18, 0, O(nLoop));
            if (!L.fprop) {   // what an FField reader would take on a UProperty engine: decoys
                putP(prop[pLevel], DynOff::FBYTEPROP_ENUM, O(nStale));
            }
            putP(obj[nDoIt], Grimoire::OFF_UOBJECT_CLASS, O(nFunction));
            put32(obj[nDoIt], Grimoire::OFF_UOBJECT_NAME, nDoIt);
            putP(obj[nDoIt], DynOff::UOBJECT_OUTER, O(nMyActor));
            putP(obj[nDoIt], chainOff, P(pHit));
            {
                // A stale table cached at EKind's address: another enum lived there once.
                std::lock_guard<std::mutex> lk(Ubel::s_enumCacheMutex);
                Ubel::s_enumCache[O(nEKind)] = { { 0, "Stale::A" } };
            }

            const std::string who = L.name;
            Ubel::ParamLayout pl;
            std::string why;
            const bool ok = Ubel::CaptureParamLayout(O(nDoIt), pl, why) && pl.params.size() == 4;
            check(("layout by value, " + who + ": the four parameters").c_str(), ok, why.c_str());
            if (!ok) continue;
            const auto& hit = pl.params[0];
            check(("..." + who + ": a struct's members, its super's first").c_str(),
                  hit.sub.size() == 3 && hit.sub[0].name == "Base" && hit.sub[1].name == "X" && hit.sub[1].offset == 4 &&
                  hit.sub[2].name == "Inner", std::to_string(hit.sub.size()).c_str());
            check(("..." + who + ": a nested struct's members, with its packed bool's bit").c_str(),
                  hit.sub.size() == 3 && hit.sub[2].sub.size() == 1 && hit.sub[2].sub[0].name == "bOn" &&
                  hit.sub[2].sub[0].boolMask == 0x02);
            const auto& kind = pl.params[1];
            check(("..." + who + ": an EnumProperty's enum and its table, read fresh -- not the stale cache").c_str(),
                  kind.enumName == "EKind" && kind.enumEntries.size() == 2 && kind.enumEntries[0].second == "EKind::A" &&
                  kind.enumEntries[1].first == 2 && kind.enumEntries[1].second == "EKind::B",
                  (kind.enumName + " " + (kind.enumEntries.empty() ? std::string() : kind.enumEntries[0].second)).c_str());
            check(("..." + who + ": a ByteProperty's enum, at its own slot").c_str(),
                  pl.params[2].enumName == "EKind" && pl.params[2].enumEntries.size() == 2, pl.params[2].enumName.c_str());
            int depth = 0;
            for (const Ubel::ParamField* f = &pl.params[3]; !f->sub.empty(); f = &f->sub[0]) ++depth;
            check(("..." + who + ": a struct that holds itself stops at kParamStructDepth").c_str(),
                  depth == Ubel::kParamStructDepth, std::to_string(depth).c_str());

            // By value: wipe every blob the capture read; what it captured does not change.
            memset(obj, 0, sizeof obj); memset(prop, 0, sizeof prop); memset(enumData, 0, sizeof enumData);
            check(("..." + who + ": the captured layout outlives the objects it was read from").c_str(),
                  pl.params[0].sub.size() == 3 && pl.params[0].sub[2].sub[0].name == "bOn" &&
                  pl.params[1].enumEntries.size() == 2 && pl.params[1].enumEntries[1].second == "EKind::B");
        }
        {
            std::lock_guard<std::mutex> lk(Ubel::s_enumCacheMutex);
            Ubel::s_enumCache.erase(reinterpret_cast<uintptr_t>(obj[nEKind]));
        }
        DynOff::bUseFProperty = savedFProp;
        DynOff::bCasePreservingName = savedCpn;
        DynOff::UPROPERTY_OFFSET = savedOff;
        DynOff::UPROPERTY_SUBCLASS_START = savedStart;
        g_cachedUEVersion = savedVer;
        DynOff::UENUM_NAMES = savedNames;
        DynOff::UENUM_VALUE_SIZE = savedWidth;
        DynOff::UENUM_PAIR_STRIDE = savedStride;
        DynOff::bEnumNamesNewContainer = savedNew;
        DynOff::bUEnumNamesDetected = savedDet;
        DynOff::bUEnumNamesFailed = savedFailed;
        DynOff::bFNameAlignProbed = savedProbed;
        DynOff::FNAME_ALIGN_MEASURED = savedAlign;
    }

    printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
    return g_fail == 0 ? 0 : 1;
}
