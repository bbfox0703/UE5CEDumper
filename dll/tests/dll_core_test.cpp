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
#include <stdio.h>
#include <cstdint>
#include <vector>
#include <atomic>

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

// Tot's cancel flag is a per-command atomic in a header. Reset it explicitly between
// cases: it is process-global and a leaked `true` would make every later walk look
// cancelled — the exact shape that would turn this whole file into a false pass.
static void ResetCancel() {
    Tot::g_perCommand.store(false);
    Tot::g_shutdown.store(false);
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
        // And FindByAddress (Aura.cpp:1867) hand-rolls `for (int32_t i = 0; i < count; ++i)` --
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
        // Exactness matters: an exact hit returns at Aura.cpp:1909 and never enters the backward
        // module scan, which the audit deliberately left unpolled.
        ResetCancel();
        auto hit = Aura::FindByAddress(objAddr);
        check("FindByAddress finds an in-pool object when not cancelled", hit.found == true);
        check("...as an EXACT match, so the backward scan is never entered", hit.exactMatch == true);
        check("...at the index we planted it", hit.index == kIdx,
              std::to_string(hit.index).c_str());

        // (b) THE CASE A7 FIXED. The exact-match return is unconditional, so the ONLY thing that
        // can turn this same address into a miss is the poll at Aura.cpp:1891.
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
        std::vector<uint8_t> blob(0x200, 0);
        const uintptr_t X = reinterpret_cast<uintptr_t>(blob.data());

        auto setPropsSize = [&](int32_t v) {
            memcpy(blob.data() + DynOff::USTRUCT_PROPSSIZE, &v, sizeof(v));
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
        // by the raw class address and NOTHING erases it (that unboundedness is exactly
        // why the plausibility ceiling has to stay a bound). Reusing one address makes
        // every case after the first read the FIRST case's memoised answer -- the A10
        // defect the fixture above demonstrates, hit here by accident while writing
        // this test.
        std::vector<uint8_t> objBlob(0x200, 0);
        const uintptr_t O = reinterpret_cast<uintptr_t>(objBlob.data());
        std::vector<uint8_t> clsGarbage(0x200, 0), clsEmpty(0x200, 0), clsBig(0x200, 0);
        auto setPropsSize = [](std::vector<uint8_t>& b, int32_t v) {
            memcpy(b.data() + DynOff::USTRUCT_PROPSSIZE, &v, sizeof(v));
            return reinterpret_cast<uintptr_t>(b.data());
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

    printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
    return g_fail == 0 ? 0 : 1;
}
