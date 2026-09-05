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

    printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
    return g_fail == 0 ? 0 : 1;
}
