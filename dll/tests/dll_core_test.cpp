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


    {   blk("D3/D5 -- an UNREADABLE array element must not be published as a VALUE");
        // Blind-spot sweep, 2026-09-08. Two array readers fabricated a plausible answer
        // when the element read faulted: the multicast-delegate one published the
        // affirmative "(0 bindings)" and the TLazyObjectPtr one an all-zero FGuid, both
        // counted in readCount. Macht::ReadTArray (Macht.h:287-297) validates only Count
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
    // UNREADVAL, BOOLNATIVE, UFUNCWALK and OPTLAYOUT below it and NOTHING ELSE after any of them. It calls Serie::InitUE4,
    // and Serie's pool state (s_poolAddr / s_isUE4Mode / s_initialized) lives in
    // file-statics that no header exposes -- so it CANNOT be restored. Anything appended
    // after this block would run against a fake UE4 name pool and could pass or fail for
    // that reason. IFACEREAD, UNREADVAL, BOOLNATIVE, UFUNCWALK and OPTLAYOUT are the legal exceptions: each installs its OWN
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

        static uint8_t wfEntry[9][0x40] = {};
        const char* wfNames[9] = { "", "Function", "ObjectProperty", "Target", "Actor",
                                   "StructProperty", "Hit", "HitResult", "DoIt" };
        static uintptr_t wfChunk[10] = {};
        for (int i = 1; i <= 8; ++i) {
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
        DynOff::bUseFProperty       = false;
        DynOff::bCasePreservingName = false;

        // Named objects: a zeroed UObject whose FName is the given pool index. 0x100 bytes, so a
        // WalkClass of the fake struct reads zeros, not a neighbour.
        static uint8_t wfNamed[9][0x100] = {};
        auto named = [&](int idx) {
            *reinterpret_cast<int32_t*>(wfNamed[idx] + Grimoire::OFF_UOBJECT_NAME) = idx;
            return reinterpret_cast<uintptr_t>(wfNamed[idx]);
        };
        auto put   = [](uint8_t* base, int off, uintptr_t v) { memcpy(base + off, &v, sizeof(v)); };
        auto put32 = [](uint8_t* base, int off, int32_t v)   { memcpy(base + off, &v, sizeof(v)); };

        // ONE set of blobs per case: a class, its UFunction, two UProperty params.
        static uint8_t wfCls[2][0x100] = {}, wfFn[2][0x100] = {};
        static uint8_t wfObjP[2][0x100] = {}, wfStrP[2][0x100] = {};
        auto walkAt = [&](int c, unsigned ver, int offsetInternal, int subclassStart) {
            g_cachedUEVersion        = ver;
            DynOff::UPROPERTY_OFFSET = offsetInternal;
            put(wfCls[c], DynOff::USTRUCT_CHILDREN, reinterpret_cast<uintptr_t>(wfFn[c]));
            put(wfFn[c], Grimoire::OFF_UOBJECT_CLASS, named(1));                 // "Function"
            put32(wfFn[c], Grimoire::OFF_UOBJECT_NAME, 8);                       // "DoIt"
            put32(wfFn[c], DynOff::FunctionFlagsOffsetFor(ver, false), 0x00080401);
            put(wfFn[c], DynOff::USTRUCT_CHILDREN, reinterpret_cast<uintptr_t>(wfObjP[c]));
            // param 1: ObjectProperty "Target" -> PropertyClass "Actor" at the REAL subclass start
            put(wfObjP[c], Grimoire::OFF_UOBJECT_CLASS, named(2));
            put32(wfObjP[c], Grimoire::OFF_UOBJECT_NAME, 3);
            put32(wfObjP[c], DynOff::UPROPERTY_ELEMSIZE, 8);
            put32(wfObjP[c], offsetInternal, 0);
            put(wfObjP[c], subclassStart, named(4));
            put(wfObjP[c], DynOff::UFIELD_NEXT, reinterpret_cast<uintptr_t>(wfStrP[c]));
            // param 2: StructProperty "Hit" -> Struct "HitResult" at the REAL subclass start
            put(wfStrP[c], Grimoire::OFF_UOBJECT_CLASS, named(5));
            put32(wfStrP[c], Grimoire::OFF_UOBJECT_NAME, 6);
            put32(wfStrP[c], DynOff::UPROPERTY_ELEMSIZE, 0x88);
            put32(wfStrP[c], offsetInternal, 8);
            put(wfStrP[c], subclassStart, named(7));
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

        g_cachedUEVersion           = savedVerW;
        DynOff::UPROPERTY_OFFSET    = savedOffW;
        DynOff::bCasePreservingName = savedCpnW;
        DynOff::bUseFProperty       = savedFPropW;
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

        static uint8_t olEntry[12][0x40] = {};
        const char* olNames[12] = { "", "OptionalProperty", "ObjectProperty", "ArrayProperty",
                                    "StrProperty", "Opt", "Inner", "NameProperty", "TextProperty",
                                    "StructProperty", "MyStruct", "LazyObjectProperty" };
        static uintptr_t olChunk[13] = {};
        for (int i = 1; i <= 11; ++i) {
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
        auto structInner = [&](int k, int s, int16_t minAlign) {
            *reinterpret_cast<int32_t*>(olStruct[s] + Grimoire::OFF_UOBJECT_NAME) = 10;      // "MyStruct"
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

        g_cachedUEVersion           = savedVerO;
        DynOff::bCasePreservingName = savedCpnO;
        DynOff::bUseFProperty       = savedFPropO;
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

        static uint8_t ceEntry[12][0x40] = {};
        const char* ceNames[12] = { "", "ArrayProperty", "SetProperty", "MapProperty", "ByteProperty",
                                    "EnumProperty", "IntProperty", "Items", "Tags", "Lookup", "EMyEnum", "EOther" };
        static uintptr_t ceChunk[13] = {};
        for (int i = 1; i <= 11; ++i) {
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

    printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
    return g_fail == 0 ? 0 : 1;
}
