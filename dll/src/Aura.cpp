// ============================================================
// Aura — 斷頭台的阿烏拉 (服從之秤 — Obedience Scale)
// ObjectArray: FUObjectArray slot enumeration and validation
// ============================================================

#include "Aura.h"
#include "Macht.h"
#define LOG_CAT "OARR"
#include "Sein.h"
#include "Grimoire.h"
#include "Serie.h"

#include "Ubel.h"
#include "Genau.h"
#include "Denken.h"
#include "Tot.h"
#include "Lineal.h"   // UE5.7+ packed FUObjectItem reconstruction (Reconstruct + consts)
#include "Orden.h"    // Multi-value group scan: source-agnostic SDR matcher (MatchGroup)

// Defined in Frieren.cpp — cached UE version for layout branching
extern uint32_t g_cachedUEVersion;

#include <algorithm>
#include <atomic>
#include <cctype>
#include <chrono>
#include <climits>
#include <cstring>
#include <mutex>
#include <set>
#include <thread>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

namespace Aura {

// Deterministic per-object cap on total container elements visited during a deep
// leaf walk (blow-up guard). Shared by the value-scan, group-scan and snapshot
// deep descents so they stay in lockstep (see native-c / snapshot specs).
static constexpr int64_t kDeepWalkMaxTotalElems = 50000;

// FUObjectArray layout offsets (auto-detected)
struct ArrayLayout {
    int32_t objectsOffset;    // FUObjectItem** Objects
    int32_t maxElementsOffset;
    int32_t numElementsOffset;
    int32_t maxChunksOffset;
    int32_t numChunksOffset;
};

static uintptr_t  s_arrayAddr = 0;
static ArrayLayout s_layout = { 0x00, 0x10, 0x14, 0x18, 0x1C }; // Default layout
static int         s_itemSize = 16;  // FUObjectItem stride (auto-detected: 16 or 24)
// Within-item byte offset of the UObject* field inside an FUObjectItem.
//   +0x00 — classic FUObjectItem (UE4.x .. UE5.6): Object* is the first field.
//   +0x08 — UE5.7+ reordered item: int64 FlagsAndRefCount moved to item+0x00,
//           pushing UObject* to +0x08 (verified vs EpicGames/UnrealEngine 5.7.0-release
//           source). Auto-detected in DetectItemSize; mirrors Dumper-7's
//           FUObjectItemInitialOffset. Applied by GetByIndex / ProbeStride / GetSerialNumber.
static int         s_itemObjOffset = 0;
static bool        s_isFlat   = false; // true = non-chunked flat array (some UE4 builds)

// What DetectLayout concluded about flatness, kept SEPARATE from s_isFlat.
//
// s_isFlat is working state: DetectItemSize resets it to false at the top of every
// object-pointer-offset pass so each pass re-derives flatness independently. That reset sits
// one line above the DetectStrideForCurrentObjOffset call, so anything the preset concluded is
// already gone by the time the stride probes run. This variable survives it and is the honest
// answer to "did a flat PRESET win", which is stronger evidence than any probe heuristic.
static bool        s_presetIsFlat = false;

// Preset-bound FUObjectItem hint latched by DetectLayout (see LayoutPreset::itemHint).
// stride 0 = no hint, sweep as usual. Consumed by DetectItemSize as a first, preset-gated
// probe that never enters the shared stride candidate list.
static int         s_hintItemStride = 0;
static int         s_hintItemObjOff = 0;

// Within-item layout mode. Classic / Unpacked57 both read the UObject* directly at
// item+s_itemObjOffset (0x00 / 0x08). Packed57 (UE5.7+ UE_ENABLE_FUOBJECT_ITEM_PACKING)
// must RECONSTRUCT the pointer from two split fields — see Lineal.h. Packed57 is a
// last-resort, auto-detected ONLY when both direct modes fail (see DetectItemSize), and
// is *** UNVERIFIED *** (no shipping game uses it yet). Process-lifetime constant after
// Init() → the GetByIndex Packed57 branch is perfectly predicted on the hot path.
static Lineal::ItemLayoutMode s_layoutMode = Lineal::ItemLayoutMode::Classic;
// Calibratable packed reconstruction constants (defaults from assumed UE5.7 source).
// Overridable at runtime via SetPackedConsts / the set_packed_consts pipe command so a
// real packed game can be calibrated without a rebuild.
static Lineal::PackedConsts   s_packedConsts;
// Best-effort SerialNumber offset under packing (layout unknown — see GetSerialNumber).
static int         s_packedSerialOff = 0x0C;

// GAP #1: Decryption hook for encrypted GObjects pointers.
// Default nullptr = identity (zero overhead — no indirect call on hot path).
// Set by SetDecryptFunc() from CE Lua export before Init().
static Aura::DecryptFunc s_decryptFunc = nullptr;

void Aura::SetDecryptFunc(DecryptFunc func) {
    s_decryptFunc = func;
    LOG_INFO("ObjectArray: Custom decryption function %s",
             func ? "SET" : "CLEARED (identity)");
}

// ============================================================
// Parallel GObjects-walk infrastructure
// ============================================================
//
// The value/reference/container scans below all walk the entire GObjects
// array (object-by-object, reading structured properties). On large games
// (1M+ objects, multi-GB heap) this single-threaded walk is the wall-clock
// floor. Because the walk is READ-ONLY against game memory + init-time
// constants (FNamePool offsets, g_cachedUEVersion, the FUObjectArray layout),
// it parallelizes cleanly: partition the index range into contiguous chunks,
// give each thread its own caches + result buffer, then merge in chunk order
// so the global result stays ascending-index ordered (matching the serial
// semantics exactly). Mirrors the discrete dumper's RunParallelScan design
// (docs reference: Memory-Scanning-Internals.md §16).
namespace {

// Worker thread count for a GObjects walk of `workItems` objects. Leaves
// headroom (-2) for the game's own threads + our pipe/UI thread so we don't
// saturate every core and stall the host. Clamped to [1, 16]; tiny arrays
// stay single-threaded (thread spawn cost would dominate).
int ScanThreadCount(int32_t workItems) {
    if (workItems < 8192) return 1;
    unsigned hc = std::thread::hardware_concurrency();
    int n = (hc >= 3) ? static_cast<int>(hc) - 2 : 1;
    if (n < 1)  n = 1;
    if (n > 16) n = 16;
    return n;
}

// Partition [0, count) into `nthreads` contiguous ascending ranges and run
// body(tid, beginIdx, endIdx) on each. Chunk 0 runs inline on the calling
// thread; the rest run on spawned std::threads which are joined before
// return. Ascending, non-overlapping ranges mean a per-thread result merge
// in tid order reproduces the serial ascending-index ordering.
template <typename BodyFn>
void ParallelIndexRanges(int32_t count, int nthreads, BodyFn&& body) {
    if (count <= 0) return;

    // A worker body must never let an exception escape: an exception leaving a
    // std::thread callable — or an un-joined joinable thread during stack
    // unwinding — calls std::terminate, which would crash the host game. The
    // scans are best-effort, so a throwing chunk just stops early; its partial
    // per-thread results still merge, exactly like a deadline hit.
    auto runChunk = [&body](int tid, int32_t b, int32_t e) {
        try {
            body(tid, b, e);
        } catch (...) {
            LOG_WARN("ParallelIndexRanges: worker tid=%d [%d,%d) threw — dropping chunk",
                     tid, b, e);
        }
    };

    if (nthreads <= 1) { runChunk(0, 0, count); return; }

    // 64-bit chunk math so a corrupted/huge object count can't overflow the
    // int32 multiply when computing a high thread's start offset.
    const int64_t chunk = (static_cast<int64_t>(count) + nthreads - 1) / nthreads;
    std::vector<std::thread> pool;
    pool.reserve(static_cast<size_t>(nthreads) - 1);
    for (int t = 1; t < nthreads; ++t) {
        const int64_t b = static_cast<int64_t>(t) * chunk;
        if (b >= count) break;
        const int32_t bi = static_cast<int32_t>(b);
        const int32_t ei = static_cast<int32_t>(std::min<int64_t>(b + chunk, count));
        pool.emplace_back([&runChunk, t, bi, ei]() { runChunk(t, bi, ei); });
    }
    runChunk(0, 0, static_cast<int32_t>(std::min<int64_t>(chunk, count)));  // chunk 0 inline
    for (auto& th : pool) th.join();
}

// Result of a ParallelGObjectsScan run. `perThread` is exposed so callers can
// fold their own per-thread stat fields (counters, sets) after the run;
// `deadlineHit` is the post-join load of the shared flag; `nthreads` is what
// ScanThreadCount picked (for logging).
template <typename PerThreadT>
struct ParallelScanResult {
    std::vector<PerThreadT> perThread;
    int                     nthreads    = 1;
    bool                    deadlineHit = false;
};

// Run a parallel GObjects walk: spawn ScanThreadCount(count) workers over
// contiguous ascending index ranges, each writing into its own PerThreadT.
// `body(tr, beginIdx, endIdx, deadlineHit)` is the per-thread loop — it owns the
// per-object work, the per-thread local maxResults cap, and the deadline check
// (call deadlineHit.store(true) to signal siblings to stop). This factors out
// the nthreads / perThread-vector / atomic-deadline / ParallelIndexRanges
// boilerplate the three scans shared. The per-thread result-vector merge is left
// to the caller (via ConcatTruncate) because the element type differs per scan.
// After join, returns {perThread (moved), nthreads, deadlineHit.load()}.
template <typename PerThreadT, typename BodyFn>
ParallelScanResult<PerThreadT> ParallelGObjectsScan(int32_t count, BodyFn&& body, int maxThreads = 0) {
    int nthreads = ScanThreadCount(count);
    // maxThreads > 0 caps the auto-picked worker count. A caller passes 1 to
    // force a fully serial walk (the body runs inline on the calling thread, no
    // std::threads spawned) — used by Value Search's "parallel" toggle when the
    // user turns it off, so concurrent cross-thread memory reads can't trip a
    // game's anti-tamper. 0 = no cap (use whatever ScanThreadCount picked).
    if (maxThreads > 0 && nthreads > maxThreads) nthreads = maxThreads;
    std::vector<PerThreadT> perThread(static_cast<size_t>(std::max(1, nthreads)));
    std::atomic<bool> deadlineHit{false};

    // Cooperative cancellation. For the PARALLEL path a short-lived watcher
    // flips the shared deadline flag on cancel (client disconnect via Fern's
    // monitor, or shutdown) so every worker bails at its next stride check —
    // 50ms granularity, exits as soon as the run finishes. For the SERIAL path
    // (nthreads == 1, the Value Search "parallel" toggle OFF) we deliberately
    // spawn NO thread: anti-tamper games turn parallel off precisely to avoid
    // extra thread creation, and an anti-cheat that hooks thread creation would
    // otherwise still see this watcher. The bodies poll Tot::Requested()
    // directly at their stride checks, so serial scans still cancel promptly.
    std::atomic<bool> scanDone{false};
    std::thread cancelWatcher;
    if (nthreads > 1) {
        cancelWatcher = std::thread([&] {
            while (!scanDone.load(std::memory_order_relaxed)) {
                if (Tot::Requested()) {
                    deadlineHit.store(true, std::memory_order_relaxed);
                    return;
                }
                std::this_thread::sleep_for(std::chrono::milliseconds(50));
            }
        });
    }

    ParallelIndexRanges(count, nthreads, [&](int tid, int32_t beginIdx, int32_t endIdx) {
        body(perThread[tid], beginIdx, endIdx, deadlineHit);
    });
    scanDone.store(true, std::memory_order_relaxed);
    if (cancelWatcher.joinable()) cancelWatcher.join();

    return { std::move(perThread), nthreads, deadlineHit.load() };
}

// Concatenate each thread's result vector (selected by pointer-to-member) in
// ascending tid order, stopping at maxResults. Each worker scanned a contiguous
// ascending index range and locally capped at maxResults, so this reproduces the
// serial "first N in ascending index order" set exactly (keeps the lowest-index
// subset when truncating). Elements are moved out of `perThread`.
template <typename PerThreadT, typename ElemT>
std::vector<ElemT> ConcatTruncate(std::vector<PerThreadT>& perThread,
                                  std::vector<ElemT> PerThreadT::* member,
                                  int32_t maxResults) {
    std::vector<ElemT> out;
    for (auto& tr : perThread) {
        for (auto& item : tr.*member) {
            if (static_cast<int32_t>(out.size()) >= maxResults) return out;
            out.push_back(std::move(item));
        }
    }
    return out;
}

} // namespace

uintptr_t Aura::DecryptObjectPtr(uintptr_t rawPtr) {
    if (!rawPtr || !s_decryptFunc) return rawPtr;
    return s_decryptFunc(rawPtr);
}

// GAP #2: Named preset layouts for FChunkedFixedUObjectArray (from Dumper-7 reference).
// Games can reorder struct members; these presets cover known variants.
// Optional per-preset FUObjectItem hint, for licensee forks whose item stride and
// object-pointer offset cannot be recovered by the shared stride sweep in DetectItemSize.
// Deliberately preset-BOUND rather than another entry in the shared candidate list:
// e.g. MindsEye's 32-byte item with the object at +0x10 aliases perfectly with a stock
// 16-byte item (every odd 16-byte slot lands on the real pointer), so adding 32/+0x10 to
// the shared sweep would let it outscore the true stride on genuine stride-16 titles
// (Titan Quest II, Octopath Traveler). stride == 0 means "no hint, sweep as usual".
struct ItemHint {
    int stride;
    int objOff;
};

struct LayoutPreset {
    const char* name;
    ArrayLayout layout;
    ItemHint    itemHint;   // {0, 0} = none
};

// Upper bound on MaxElements. MaxElements is MaxChunks * elements-per-chunk, so it tracks
// the title's gc.MaxObjectsInGame ceiling, NOT its live object count — generous
// preallocation is normal, not corruption. This was 0x800000 (8,388,608) and DragonSword
// Awakening (UE 5.3) rejected the CORRECT preset on it: MaxChunks=161, elements-per-chunk
// 65536 -> MaxElements = 10,551,296. Every strict preset then failed and the relaxed tier
// latched Layout B, whose numElementsOffset 0x04 is ObjLastNonGCIndex — a FROZEN count of
// the startup disregard-for-GC set (37,099) instead of the live NumElements (317,810 and
// climbing). GetCount() then reported 37,099 forever, so every GObjects walk — Value
// Search, Instances, Property Search, snapshots — saw only startup CDOs and not one
// runtime object. Raised to 33.5M: still far below the values seen when this slot actually
// holds half of a pointer (MindsEye: 233M), which is what the bound exists to reject.
static constexpr int32_t kMaxElementsCeiling = 0x2000000;  // 33,554,432

// All known chunked layouts. Order: default first, then game-specific.
static const LayoutPreset s_chunkedPresets[] = {
    { "Default",     { 0x00, 0x10, 0x14, 0x18, 0x1C } },  // UE4.21+ and UE5 standard
    { "Back4Blood",  { 0x10, 0x00, 0x04, 0x08, 0x0C } },  // Objects at end
    { "Multiversus", { 0x18, 0x10, 0x00, 0x14, 0x20 } },  // NumElements first
    { "MindsEye",    { 0x18, 0x00, 0x14, 0x10, 0x04 } },  // MaxElements first
    { "UE5.8",       { 0x00, 0x0C, 0x08, 0x14, 0x10 } },  // 5.8 dev: FUObjectArray fields reordered for cache locality, PreAllocatedObjects moved to end
};
static constexpr int NUM_CHUNKED_PRESETS = sizeof(s_chunkedPresets) / sizeof(s_chunkedPresets[0]);

// Extended: FUObjectArray with GC index fields before ObjObjects.
// UE4-Extended: no PreAllocatedObjects ptr in FChunkedFixedUObjectArray.
// UE5-Extended: has PreAllocatedObjects ptr (+8 bytes shift). TQ2 (UE 5.7) confirmed.
static const LayoutPreset s_ue4ExtendedPresets[] = {
    { "UE4-Extended", { 0x10, 0x18, 0x1C, 0x20, 0x24 } },
    { "UE5-Extended", { 0x10, 0x20, 0x24, 0x28, 0x2C } },
    // MindsEye (UE 5.4.4 licensee fork): the s_chunkedPresets "MindsEye" row shifted
    // +0x10, i.e. relative to the FUObjectArray base rather than ObjObjects. Genau
    // resolves the same address, so without this row DetectLayout would fall through to
    // the relaxed path and read the wrong fields. Kept LAST in the Tier 2 table:
    // DetectLayout is first-match-wins and latches the winner into s_layout, so ordering
    // is load-bearing — UE4-Extended / UE5-Extended keep priority. Cannot steal an
    // existing title: ValidateChunkedLayout requires maxChunks in [6, 0x5FF], and on a
    // UE5-Extended array this row reads MaxElements (~2.1M) as maxChunks.
    // itemHint: FUObjectItem is 32 bytes with UObject* at +0x10 — recovered from the
    // index->object accessors (e.g. RVA 0x0191AA10): `shr rcx,0x10 / movzx edx,bx /
    // shl rdx,5 / add rdx,[r9+rcx*8] / cmp qword [rdx+0x10],0`.
    { "MindsEye-Extended", { 0x28, 0x10, 0x24, 0x20, 0x14 }, { 32, 0x10 } },
};
static constexpr int NUM_UE4_EXTENDED_PRESETS = sizeof(s_ue4ExtendedPresets) / sizeof(s_ue4ExtendedPresets[0]);

// Flat (non-chunked) FFixedUObjectArray layout.
// Objects* points directly to FUObjectItem[] (no chunk pointer indirection).
// Used by early UE4 (4.11-4.22) including Octopath Traveller.
// Layout: { Objects*(8), MaxElements(4), NumElements(4) } — total 16 bytes before FCriticalSection.
//
// TWO ANCHORS, because a flat array can be handed to us at either address:
//   "Flat"      — ObjObjects-relative. What the five adjustment=-0x10 GObjects patterns
//                 (GOBJ_V10 / AV1 / AV2 / RE2 / V12) deliver, e.g. Octopath.
//   "Flat-Base" — the FUObjectArray BASE, i.e. +0x10 further out, past
//                 ObjFirstGCIndex / ObjLastNonGCIndex / MaxObjectsNotConsideredByGC /
//                 OpenForDisregardForGC. Verified by disassembly on UE 4.11.0-preview7
//                 (Nekopara, FUObjectItem = 16 B) and UE 4.13 (Fantasynth, 24 B): both do one
//                 Malloc(Max * stride) with no chunk table, and EVERY pattern that hits them
//                 resolves the base.
//
// Without the second row, a pre-4.21 title whose five ObjObjects-anchored patterns all miss is
// unfixable by pattern work at ANY priority — nothing else can present the ObjObjects anchor.
// That is the real reach argument here; the two old titles are just what exposed it.
//
// This tier runs BEFORE the relaxed tier on purpose: it pre-empts the Layout A/C and B
// fallbacks, which would otherwise latch objectsOffset = 0x00 (reading the two GC index int32s
// as a pointer) or numElementsOffset = 0x04 (the disregard-pool count).
static const LayoutPreset s_flatPresets[] = {
    { "Flat",      { 0x00, 0x08, 0x0C, -1, -1 } },
    { "Flat-Base", { 0x10, 0x18, 0x1C, -1, -1 } },
};
static constexpr int NUM_FLAT_PRESETS = sizeof(s_flatPresets) / sizeof(s_flatPresets[0]);

// Helper: check if a pointer value looks like a valid heap pointer (not code/null/low)
static bool LooksLikeHeapPtr(uintptr_t ptr) {
    // Must be a plausible user-mode heap/data pointer (not null/low/kernel)
    if (!Grimoire::IsUserspacePointer(ptr)) return false;
    // Reject pointers in the game module's code range (likely .text section)
    uintptr_t modBase = Macht::GetModuleBase(nullptr);
    uintptr_t modSize = Macht::GetModuleSize(nullptr);
    if (modBase && modSize && ptr >= modBase && ptr < modBase + modSize) return false;
    return true;
}

// Log all 5 layout field values at an address for diagnosis.
static void LogLayoutFields(uintptr_t addr, const ArrayLayout& layout, const char* presetName) {
    int32_t numElements = 0, maxElements = 0, numChunks = 0, maxChunks = 0;
    uintptr_t objPtr = 0;
    Macht::ReadSafe(addr + layout.numElementsOffset, numElements);
    Macht::ReadSafe(addr + layout.maxElementsOffset, maxElements);
    Macht::ReadSafe(addr + layout.objectsOffset, objPtr);
    if (layout.maxChunksOffset >= 0) Macht::ReadSafe(addr + layout.maxChunksOffset, maxChunks);
    if (layout.numChunksOffset >= 0) Macht::ReadSafe(addr + layout.numChunksOffset, numChunks);

    uintptr_t decObjPtr = DecryptObjectPtr(objPtr);
    LOG_INFO("ObjectArray: Layout '%s': Num=%d, Max=%d, NumChunks=%d, MaxChunks=%d, Objects=0x%llX%s",
             presetName, numElements, maxElements, numChunks, maxChunks,
             (unsigned long long)decObjPtr,
             (objPtr != decObjPtr) ? " (decrypted)" : "");
}

// Full validation of a chunked FUObjectArray layout (Dumper-7 rigor).
// Reads all 5 fields and checks range, alignment, and consistency.
static bool ValidateChunkedLayout(uintptr_t addr, const ArrayLayout& layout) {
    int32_t numElements = 0, maxElements = 0, numChunks = 0, maxChunks = 0;
    uintptr_t objPtr = 0;

    if (!Macht::ReadSafe(addr + layout.numElementsOffset, numElements)) return false;
    if (!Macht::ReadSafe(addr + layout.maxElementsOffset, maxElements)) return false;
    if (!Macht::ReadSafe(addr + layout.objectsOffset, objPtr)) return false;
    objPtr = DecryptObjectPtr(objPtr);

    bool hasChunkFields = (layout.maxChunksOffset >= 0 && layout.numChunksOffset >= 0);
    if (hasChunkFields) {
        if (!Macht::ReadSafe(addr + layout.maxChunksOffset, maxChunks)) return false;
        if (!Macht::ReadSafe(addr + layout.numChunksOffset, numChunks)) return false;
    }

    // --- Range checks ---
    if (numElements < 0x1000 || numElements > 0x400000) return false;
    if (maxElements < numElements || maxElements > kMaxElementsCeiling) return false;

    // Objects pointer must be a valid heap pointer
    if (!LooksLikeHeapPtr(objPtr)) return false;

    // --- Chunk consistency (Dumper-7 rigor, only if chunk fields present) ---
    if (hasChunkFields) {
        if (numChunks < 1 || numChunks > 0x14) return false;
        if (maxChunks < 6 || maxChunks > 0x5FF) return false;
        if (numChunks > maxChunks) return false;

        // MaxElements alignment
        if ((maxElements % 0x10) != 0) return false;

        // Elements per chunk consistency
        int32_t elemPerChunk = maxElements / maxChunks;
        if ((elemPerChunk % 0x10) != 0) return false;
        if (elemPerChunk < 0x8000 || elemPerChunk > 0x80000) return false;

        // Cross-field consistency
        if (((numElements / elemPerChunk) + 1) != numChunks) return false;
        if ((maxElements / elemPerChunk) != maxChunks) return false;
    }

    // --- Pointer dereference validation ---
    uintptr_t chunk0 = 0;
    if (!Macht::ReadSafe(objPtr, chunk0)) return false;

    if (chunk0 == 0) {
        // chunk[0] null — unlikely for valid array, but accept if objPtr is heap
        return true;
    }

    if (!LooksLikeHeapPtr(chunk0)) return false;

    // Validate additional chunk pointers are readable (cap at 5 to avoid excess reads)
    if (hasChunkFields && numChunks > 1) {
        for (int i = 1; i < numChunks && i < 5; ++i) {
            uintptr_t chunkI = 0;
            if (!Macht::ReadSafe(objPtr + i * sizeof(uintptr_t), chunkI)) return false;
            if (chunkI && !LooksLikeHeapPtr(chunkI)) return false;
        }
    }

    return true;
}

static bool DetectLayout(uintptr_t addr) {
    // Re-derived from scratch on every call (Init can run again on re-attach).
    s_presetIsFlat = false;

    // Diagnostic: dump first 48 bytes at the GObjects address
    {
        uint64_t dump[6] = {};
        Macht::ReadBytesSafe(addr, dump, sizeof(dump));
        LOG_DEBUG("ObjectArray: GObjects@0x%llX: +00:%016llX +08:%016llX +10:%016llX +18:%016llX +20:%016llX +28:%016llX",
                  (unsigned long long)addr,
                  dump[0], dump[1], dump[2], dump[3], dump[4], dump[5]);
    }

    // No preset has claimed an item hint yet this run (Init may run again on re-attach).
    s_hintItemStride = 0;
    s_hintItemObjOff = 0;

    // --- Tier 1: Try all chunked presets with FULL validation (Dumper-7 rigor) ---
    for (int i = 0; i < NUM_CHUNKED_PRESETS; ++i) {
        const auto& preset = s_chunkedPresets[i];
        if (ValidateChunkedLayout(addr, preset.layout)) {
            s_layout = preset.layout;
            s_hintItemStride = preset.itemHint.stride;
            s_hintItemObjOff = preset.itemHint.objOff;
            LOG_INFO("ObjectArray: Layout '%s' detected (strict, preset %d/%d)",
                     preset.name, i + 1, NUM_CHUNKED_PRESETS);
            LogLayoutFields(addr, s_layout, preset.name);
            return true;
        }
    }

    // --- Tier 2: Try UE4 extended presets with full validation ---
    for (int i = 0; i < NUM_UE4_EXTENDED_PRESETS; ++i) {
        const auto& preset = s_ue4ExtendedPresets[i];
        if (ValidateChunkedLayout(addr, preset.layout)) {
            s_layout = preset.layout;
            s_hintItemStride = preset.itemHint.stride;
            s_hintItemObjOff = preset.itemHint.objOff;
            LOG_INFO("ObjectArray: Layout '%s' detected (strict)", preset.name);
            LogLayoutFields(addr, s_layout, preset.name);
            return true;
        }
    }

    // --- Tier 3: Flat (non-chunked) presets ---
    // FFixedUObjectArray: Objects* is a direct FUObjectItem[], no chunk pointer table.
    // ValidateChunkedLayout handles this (hasChunkFields=false skips chunk checks).
    for (int i = 0; i < NUM_FLAT_PRESETS; ++i) {
        const auto& preset = s_flatPresets[i];
        if (ValidateChunkedLayout(addr, preset.layout)) {
            s_layout = preset.layout;
            s_isFlat = true;
            s_presetIsFlat = true;
            LOG_INFO("ObjectArray: Layout '%s' detected (flat, non-chunked)", preset.name);
            LogLayoutFields(addr, s_layout, preset.name);
            return true;
        }
    }

    // --- Tier 4: RELAXED fallback (preserves current behavior, prevents regression) ---
    // Some games pass weak checks but fail strict Dumper-7 chunk consistency.
    LOG_INFO("ObjectArray: Strict validation failed for all presets, trying relaxed fallback...");

    // Layout A/C (relaxed): Objects@+0x00, Num@+0x14
    {
        int32_t num = 0;
        Macht::ReadSafe(addr + 0x14, num);
        if (num > 0 && num <= 0x800000) {
            uintptr_t objPtr = 0;
            Macht::ReadSafe(addr + 0x00, objPtr);
            objPtr = DecryptObjectPtr(objPtr);
            if (LooksLikeHeapPtr(objPtr)) {
                s_layout = { 0x00, 0x10, 0x14, 0x18, 0x1C };
                LOG_INFO("ObjectArray: Layout A/C (relaxed) detected (Num=%d, Objects=0x%llX)",
                         num, (unsigned long long)objPtr);
                return true;
            }
        }
    }

    // Layout B (flat/alt): Objects@+0x10, Num@+0x04
    //
    // GUARDED, because B and E read their Objects pointer from the SAME offset (+0x10) and
    // B is tried first: the heap-pointer check below cannot tell them apart, so on a
    // UE5-Extended array B wins and reads NumElements out of +0x04 — which there is
    // ObjLastNonGCIndex, a real but FROZEN count of the startup disregard-for-GC set. The
    // array is then enumerated only up to the CDOs (DragonSword Awakening: 37,099 of
    // 317,810 objects) and no runtime object is ever visible to any scan.
    //
    // The discriminator is B's own chunk counts (Tier 1 "Back4Blood": MaxChunks@+0x08,
    // NumChunks@+0x0C). On a genuine B layout those are live chunk counts; on a
    // UE5-Extended layout +0x0C is OpenForDisregardForGC, a bool that reads 0 once startup
    // is done, so `numChunks >= 1` rejects the row. Deliberately NOT a reorder: putting E
    // first would let E's +0x20/+0x24 reads steal a real B layout instead.
    {
        int32_t num = 0, maxChunks = 0, numChunks = 0;
        Macht::ReadSafe(addr + 0x04, num);
        Macht::ReadSafe(addr + 0x08, maxChunks);
        Macht::ReadSafe(addr + 0x0C, numChunks);
        const bool chunksPlausible = (numChunks >= 1 && maxChunks >= 1 && numChunks <= maxChunks);
        if (num > 0 && num <= 0x800000 && chunksPlausible) {
            uintptr_t objPtr = 0;
            Macht::ReadSafe(addr + 0x10, objPtr);
            objPtr = DecryptObjectPtr(objPtr);
            if (LooksLikeHeapPtr(objPtr)) {
                s_layout = { 0x10, 0x08, 0x04, 0x0C, -1 };
                LOG_INFO("ObjectArray: Layout B (relaxed alt) detected (Num=%d, Objects=0x%llX)",
                         num, (unsigned long long)objPtr);
                return true;
            }
        }
    }

    // Layout D (UE4 extended relaxed): Objects@+0x10, Num@+0x1C, Max@+0x18
    {
        int32_t num = 0, max = 0;
        Macht::ReadSafe(addr + 0x1C, num);
        Macht::ReadSafe(addr + 0x18, max);
        if (num > 0 && num <= max && max <= kMaxElementsCeiling) {
            uintptr_t objPtr = 0;
            Macht::ReadSafe(addr + 0x10, objPtr);
            objPtr = DecryptObjectPtr(objPtr);
            if (LooksLikeHeapPtr(objPtr)) {
                s_layout = { 0x10, 0x18, 0x1C, 0x20, 0x24 };
                LOG_INFO("ObjectArray: Layout D (relaxed UE4 ext) detected (Num=%d, Max=%d, Objects=0x%llX)",
                         num, max, (unsigned long long)objPtr);
                return true;
            }
        }
    }

    // Layout E (UE5 extended relaxed): Objects@+0x10, Num@+0x24, Max@+0x20
    // FUObjectArray with GC prefix + PreAllocatedObjects ptr before array fields.
    {
        int32_t num = 0, max = 0;
        Macht::ReadSafe(addr + 0x24, num);
        Macht::ReadSafe(addr + 0x20, max);
        if (num > 0 && num <= max && max <= kMaxElementsCeiling) {
            uintptr_t objPtr = 0;
            Macht::ReadSafe(addr + 0x10, objPtr);
            objPtr = DecryptObjectPtr(objPtr);
            if (LooksLikeHeapPtr(objPtr)) {
                s_layout = { 0x10, 0x20, 0x24, 0x28, 0x2C };
                LOG_INFO("ObjectArray: Layout E (relaxed UE5 ext) detected (Num=%d, Max=%d, Objects=0x%llX)",
                         num, max, (unsigned long long)objPtr);
                return true;
            }
        }
    }

    LOG_WARN("ObjectArray: Could not detect layout, using default");
    s_layout = s_chunkedPresets[0].layout;
    return true;
}

// Helper: check if a pointer looks like a valid UObject (has valid ClassPrivate chain)
static bool LooksLikeUObject(uintptr_t obj) {
    if (!obj || !Grimoire::IsUserspacePointer(obj)) return false;
    uintptr_t cls = 0;
    if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls)) return false;
    if (!Grimoire::IsUserspacePointer(cls)) return false;
    uintptr_t clsCls = 0;
    if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_CLASS, clsCls)) return false;
    if (!Grimoire::IsUserspacePointer(clsCls)) return false;
    return true;
}

// Test a candidate stride against a chunk, counting valid UObject items.
// Returns the number of items that resolved names (strong) and total valid items (weak).
// NOTE: No early exit — scans all maxItems for fair comparison across strides.
//
// reconstructPacked: when true, the object pointer is RECONSTRUCTED from the UE5.7+
// packed encoding (flags@item+0x00, ptrLow@item+0x08 -> Lineal::Reconstruct) instead
// of read directly at item+s_itemObjOffset. The validation that follows (LooksLikeUObject
// + FName resolution) runs on the RECONSTRUCTED pointer — critical, because on a packed
// layout the raw item+0 field is FlagsAndRefCount, never a pointer, so scoring the raw
// field would mark every packed game all-bad.
static void ProbeStride(uintptr_t chunkBase, int stride, int maxItems,
                        int& outGood, int& outNamed, int& outNull, int& outBad,
                        bool reconstructPacked = false) {
    outGood = outNamed = outNull = outBad = 0;

    for (int idx = 0; idx < maxItems; ++idx) {
        int64_t byteOff = static_cast<int64_t>(idx) * stride;

        uintptr_t obj = 0;
        if (reconstructPacked) {
            uint64_t flags = 0; uint32_t ptrLow = 0;
            if (!Macht::ReadSafe(chunkBase + byteOff, flags) ||
                !Macht::ReadSafe(chunkBase + byteOff + 0x08, ptrLow)) {
                ++outBad;
                if (outBad > 30 && outGood == 0) break;
                continue;
            }
            obj = Lineal::Reconstruct(flags, ptrLow, s_packedConsts);
        } else if (!Macht::ReadSafe(chunkBase + byteOff + s_itemObjOffset, obj)) {
            ++outBad;
            if (outBad > 30 && outGood == 0) break;  // Too many read failures, give up
            continue;
        }

        if (!obj) {
            ++outNull;
            continue;
        }

        if (!LooksLikeUObject(obj)) {
            ++outBad;
            if (outBad > 30 && outGood == 0) break;
            continue;
        }

        ++outGood;

        // If FNamePool is available, use strong validation
        if (Serie::IsInitialized()) {
            uint32_t nameIdx = 0;
            if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx)) {
                std::string name = Serie::GetString(nameIdx);
                if (!name.empty() && name != "None") {
                    bool validAscii = true;
                    for (char c : name) {
                        if (c < 0x20 || c > 0x7E) { validAscii = false; break; }
                    }
                    if (validAscii) ++outNamed;
                }
            }
        }
    }
}

// Compute a quality score for a stride probe result.
// Positive signal: named items (strong) or good items (weak).
// Negative signal: bad items (wrong stride produces many misaligned reads).
// The correct stride should have high named/good and very low bad.
static int ComputeStrideScore(int named, int good, int bad) {
    // If we have named items, the score is primarily based on named count,
    // heavily penalized by bad count. Wrong strides that get "lucky" hits
    // via LCM alignment will have both named AND many bad items.
    if (named > 0) {
        // Score = (named * 10) - (bad * 3)
        // This means a stride with 2 named, 0 bad (score=20) beats
        // a stride with 5 named, 29 bad (score=50-87=-37).
        return named * 10 - bad * 3;
    }
    // No named items — use good count with lesser bad penalty
    if (good > 0) {
        return good * 5 - bad * 2;
    }
    // Nothing found
    return -bad;
}

// Helper: run ProbeStride for all candidate strides on a given base address, updating best.
// How many items each stride candidate is probed against. Named so the "only N validated"
// warning can state the DENOMINATOR -- "27 items validated" means nothing without it.
static constexpr int kStrideProbeBudget = 200;

static void ProbeAllStrides(uintptr_t base, int maxItems, const char* phase,
                            int candidates[], int numCandidates,
                            int& bestStride, int& bestCount, int& bestNamed,
                            int& bestBad, bool& bestHasNames) {
    int bestScore = INT_MIN;

    // Store results for all candidates (for fallback logic)
    struct ProbeResult { int stride, good, named, null_, bad, score; };
    ProbeResult results[5] = {};  // max 5 candidates

    for (int i = 0; i < numCandidates && i < 5; ++i) {
        int stride = candidates[i];
        int good, named, null_, bad;
        ProbeStride(base, stride, maxItems, good, named, null_, bad);

        LOG_INFO("ObjectArray: %s stride %d: good=%d, named=%d, null=%d, bad=%d",
                 phase, stride, good, named, null_, bad);

        int score = ComputeStrideScore(named, good, bad);
        results[i] = { stride, good, named, null_, bad, score };

        if (score > bestScore) {
            bestScore = score;
            bestStride = stride;
            bestCount = good;
            bestNamed = named;
            bestBad = bad;
            bestHasNames = (named > 0);
        }
    }

    // Fallback: when best score is negative (all strides have bad > named),
    // the primary scoring may be unreliable due to LCM alignment false positives.
    // Among strides that have named > 0, prefer fewest bad — BUT only override
    // if the fallback candidate has at least as many named items as the primary winner.
    // Named count is the strongest signal (requires both valid ClassPrivate chain AND
    // FNamePool resolution), so a stride with more named items is more trustworthy
    // even if it has slightly more bad items.
    if (bestScore < 0) {
        int fallbackBad = INT_MAX;
        int fallbackStride = -1;
        int fallbackIdx = -1;
        for (int i = 0; i < numCandidates && i < 5; ++i) {
            if (results[i].named > 0 && results[i].bad < fallbackBad) {
                fallbackBad = results[i].bad;
                fallbackStride = results[i].stride;
                fallbackIdx = i;
            }
        }
        if (fallbackIdx >= 0 && fallbackStride != bestStride) {
            // Only override if fallback has equal or more named items.
            // If primary winner has more named, keep it — named count is the
            // strongest quality signal and outweighs a small bad-count difference.
            if (results[fallbackIdx].named >= bestNamed) {
                LOG_INFO("ObjectArray: %s fallback: all scores negative, selecting stride %d (fewest bad=%d, named=%d) over stride %d (bad=%d, named=%d)",
                         phase, fallbackStride, fallbackBad, results[fallbackIdx].named, bestStride, bestBad, bestNamed);
                bestStride = results[fallbackIdx].stride;
                bestCount = results[fallbackIdx].good;
                bestNamed = results[fallbackIdx].named;
                bestBad = results[fallbackIdx].bad;
                bestHasNames = (results[fallbackIdx].named > 0);
            } else {
                LOG_INFO("ObjectArray: %s fallback: stride %d has fewer bad (%d vs %d) but primary stride %d has more named (%d vs %d), keeping primary",
                         phase, fallbackStride, fallbackBad, bestBad, bestStride, bestNamed, results[fallbackIdx].named);
            }
        }
    }
}

// Run the flat-precheck + chunked/flat stride probes for the CURRENTLY selected
// within-item object-pointer offset (s_itemObjOffset). Fills best* by reference and
// may set s_isFlat. Factored out of DetectItemSize so the detection can run once per
// object-ptr-offset candidate (classic +0x00, then UE5.7+ +0x08). ProbeStride reads
// the object pointer at item+s_itemObjOffset, so this whole pass is offset-aware.
static void DetectStrideForCurrentObjOffset(uintptr_t chunkTable, uintptr_t chunk0,
                                            int candidates[], int numCandidates,
                                            int& bestStride, int& bestCount, int& bestNamed,
                                            int& bestBad, bool& bestHasNames) {
    bestStride = 0; bestCount = 0; bestNamed = 0; bestBad = INT_MAX; bestHasNames = false;
    constexpr int MAX_ITEMS_PHASE1 = kStrideProbeBudget;
    bool detected = false;

    // --- Pre-check: detect flat (non-chunked) FFixedUObjectArray (UE4.11-4.20) ---
    // In a chunked array, each entry in the chunk table is an 8-byte pointer.
    // If we need 2+ chunks but chunk[1] (at chunkTable+8) is NOT a valid heap pointer,
    // then chunkTable is likely the flat item array itself (FUObjectItem*), not a
    // chunk pointer table (FUObjectItem**).
    //
    // UE4.18 (e.g. FF7R) uses FFixedUObjectArray = { FUObjectItem* Objects, int32 Max, int32 Num }
    // where Objects points directly to items. Our Layout B reads Objects at GObjects+0x10,
    // so chunkTable = Objects = flat item array. Reading *(chunkTable) gives Item[0].Object
    // which is a UObject*, not a chunk pointer. chunk[1] = *(chunkTable+8) reads Item[0].Flags
    // (e.g. 0x40000000 = EObjectFlags), which fails LooksLikeHeapPtr.
    {
        uintptr_t chunk1 = 0;
        Macht::ReadSafe(chunkTable + sizeof(uintptr_t), chunk1);
        int32_t numElements = GetCount();
        bool mightBeFlat = false;

        // If the LAYOUT PRESET already said flat, believe it — do not re-derive flatness from
        // the object count. The chunk[1] heuristic below can only speak when the array is big
        // enough to need two chunks, so a flat array with fewer than OBJECTS_PER_CHUNK (65536)
        // objects silently fell through to the CHUNKED probe, which then treats Item[0].Object
        // as a chunk pointer and probes a UObject's own bytes as an item array.
        //
        // Measured on NEKOPALIVE (UE 4.11, Num=27016): "Layout 'Flat-Base' detected
        // (flat, non-chunked)" was logged, then `P1 stride 16: good=1, named=1, null=51,
        // bad=148` — 1 usable item in 200, and ~10% of names resolving downstream. Fantasynth
        // (UE 4.13) has the identical layout but Num=80162, so it needed 2 chunks, the heuristic
        // fired, and it scored `P0-flat stride 24: good=200, named=200, null=0, bad=0`. The only
        // difference between the two was the object count.
        //
        // Pre-existing: this gate has always been count-based. It simply could not bite until a
        // flat array smaller than 65536 objects reached it, which is what the Flat-Base preset
        // made possible. Chunked titles are unaffected — s_isFlat is false for them.
        if (s_presetIsFlat) {
            mightBeFlat = true;
            LOG_INFO("ObjectArray: layout preset is flat (%d objects) — testing flat layout first",
                     numElements);
        } else if (chunk0 && numElements > 0) {
            int chunksNeeded = (numElements + Grimoire::OBJECTS_PER_CHUNK - 1) / Grimoire::OBJECTS_PER_CHUNK;
            if (chunksNeeded >= 2) {
                // Validate chunk[1]: in a real chunk table, chunk[1] must be a valid heap pointer.
                // LooksLikeHeapPtr alone is insufficient — 32-bit values like EObjectFlags
                // (e.g. 0x40000000) pass its checks. Add two extra validations:
                //   1. Magnitude: real heap pointers on x64 with ASLR are > 4GB
                //   2. Dereference: real chunk pointers are readable memory
                bool chunk1Valid = LooksLikeHeapPtr(chunk1);
                if (chunk1Valid && chunk1 < 0x100000000ULL) {
                    // Value fits in 32 bits — suspicious. Verify by dereference.
                    uintptr_t testDeref = 0;
                    if (!Macht::ReadSafe(chunk1, testDeref)) {
                        chunk1Valid = false;
                        LOG_DEBUG("ObjectArray: chunk[1]=0x%llX fits in 32 bits and is unreadable — not a chunk pointer",
                                  (unsigned long long)chunk1);
                    }
                }

                if (!chunk1Valid) {
                    mightBeFlat = true;
                    LOG_INFO("ObjectArray: chunk[1]=0x%llX is not a valid chunk pointer (need %d chunks for %d objects) — testing flat layout first",
                             (unsigned long long)chunk1, chunksNeeded, numElements);
                }
            }
        }

        if (mightBeFlat) {
            // Try flat layout first: probe chunkTable itself as item base (no deref)
            s_isFlat = true;
            ProbeAllStrides(chunkTable, MAX_ITEMS_PHASE1, "P0-flat",
                            candidates, numCandidates,
                            bestStride, bestCount, bestNamed, bestBad, bestHasNames);

            if (bestHasNames && bestNamed >= 2) {
                LOG_INFO("ObjectArray: Flat (non-chunked) array confirmed (P0-flat: %d named, %d bad)",
                         bestNamed, bestBad);
                detected = true;
            } else {
                // Flat didn't work convincingly — reset and try chunked
                LOG_INFO("ObjectArray: Flat probe inconclusive (named=%d), falling back to chunked detection",
                         bestNamed);
                s_isFlat = false;
                bestStride = 0; bestCount = 0; bestNamed = 0; bestBad = INT_MAX; bestHasNames = false;
            }
        }
    }

    // Phase 1: scan first 200 items of chunk[0] (standard chunked layout)
    // Use 200 items (not 100) to give sparse UE4 arrays enough items for correct stride detection.
    if (!detected) {
        ProbeAllStrides(chunk0, MAX_ITEMS_PHASE1, "P1",
                        candidates, numCandidates,
                        bestStride, bestCount, bestNamed, bestBad, bestHasNames);
    }

    // Phase 2: if Phase 1 yielded nothing, try deeper in chunk (items 1000+).
    // Some UE4 games have thousands of null slots at the start.
    if (!detected && bestCount == 0) {
        LOG_INFO("ObjectArray: Phase 1 found no items, trying deep scan from item 1000...");
        ProbeAllStrides(chunk0 + static_cast<int64_t>(1000) * 24, 100, "P2-deep",
                        candidates, numCandidates,
                        bestStride, bestCount, bestNamed, bestBad, bestHasNames);
    }

    // Phase 3: if still nothing, maybe the array is NOT chunked (some UE4 builds).
    // In non-chunked layout, chunkTable IS the item array directly (no extra deref).
    // Try probing chunkTable itself as the item base.
    if (!detected && bestCount == 0) {
        LOG_INFO("ObjectArray: Phase 2 found nothing. Trying flat (non-chunked) array at chunkTable=0x%llX...",
                 (unsigned long long)chunkTable);

        s_isFlat = true;  // Temporarily set for probing

        ProbeAllStrides(chunkTable, MAX_ITEMS_PHASE1, "P3-flat",
                        candidates, numCandidates,
                        bestStride, bestCount, bestNamed, bestBad, bestHasNames);

        if (bestCount == 0) {
            // Try deep scan on flat array too
            ProbeAllStrides(chunkTable + static_cast<int64_t>(1000) * 24, 100, "P3-flat-deep",
                            candidates, numCandidates,
                            bestStride, bestCount, bestNamed, bestBad, bestHasNames);
        }

        if (bestCount == 0) {
            s_isFlat = false;  // Revert — flat didn't work either
        } else {
            LOG_INFO("ObjectArray: Flat (non-chunked) array layout detected");
        }
    }
}

// Result of a packed-layout detection attempt.
struct PackedProbeResult {
    bool isFlat = false;   // matched against the flat (chunkTable) base, not chunk0
    int  stride = 0;       // FUObjectItem stride that matched (24 expected)
    int  good   = 0;       // reconstructed pointers passing LooksLikeUObject
    int  named  = 0;       // of those, how many resolved a valid ASCII FName
    int  probed = 0;       // items examined
};

// Detector for the UE5.7+ PACKED FUObjectItem encoding (UE_ENABLE_FUOBJECT_ITEM_PACKING).
// In packed mode the UObject* is split across two fields and reconstructed — see
// Lineal.h. Probes the candidate bases/strides with RECONSTRUCTION enabled (so the
// validation runs on the rebuilt pointer, not the raw FlagsAndRefCount at item+0) and
// uses the live (calibratable) s_packedConsts so a tuned constant flows through.
//
// *** Anti-false-positive gate ***: this is an UNVERIFIED last-resort path, so the bar is
// at least as strict as the direct passes — require >=2 reconstructed pointers that each
// resolve a real ASCII FName when FNamePool is available; only fall back to a pure
// structural bar (>=8 valid, 0 bad) when names can't be checked. Returns true + fills
// `out` on a confident match; the CALLER decides activation.
static bool TryDetectPacked(uintptr_t chunkTable, uintptr_t chunk0, PackedProbeResult& out) {
    constexpr int kProbeItems = 200;
    struct Base { const char* what; uintptr_t addr; bool flat; };
    const Base bases[]   = { { "chunked", chunk0, false }, { "flat", chunkTable, true } };
    const int  strides[] = { 24, 16 };  // packed item is 24B; 16B as a fallback guess

    int  bestScore = INT_MIN;
    bool found = false;
    const bool haveNames = Serie::IsInitialized();

    for (const auto& base : bases) {
        if (!base.addr) continue;
        for (int stride : strides) {
            int good, named, null_, bad;
            ProbeStride(base.addr, stride, kProbeItems, good, named, null_, bad,
                        /*reconstructPacked=*/true);
            LOG_INFO("ObjectArray: packed-probe %s base / stride %d: good=%d, named=%d, null=%d, bad=%d",
                     base.what, stride, good, named, null_, bad);

            bool strong = haveNames ? (named >= 2) : (good >= 8 && bad == 0);
            if (!strong) continue;

            int score = ComputeStrideScore(named, good, bad);
            if (score > bestScore) {
                bestScore  = score;
                out.isFlat = base.flat;
                out.stride = stride;
                out.good   = good;
                out.named  = named;
                out.probed = kProbeItems;
                found = true;
            }
        }
    }
    return found;
}

// Negative-case companion to TryDetectPacked: log that the layout is genuinely
// unrecognised so the field log stays actionable when even packed reconstruction fails.
static void LogPackedDiagnosticNegative() {
    LOG_INFO("ObjectArray: packed-layout diagnostic negative — items do not match the UE5.7+ packed "
             "FUObjectItem encoding either; layout is genuinely unrecognised (encrypted ptr? wrong "
             "GObjects? new variant?).");
}

// Auto-detect FUObjectItem size AND the within-item object-pointer offset by probing
// consecutive items in chunks.
//   stride:  UE5 (most) 16 bytes, UE4 / some UE5 with clustering 24 bytes.
//   objOff:  +0x00 classic (UE4.x..UE5.6), +0x08 UE5.7+ (FlagsAndRefCount moved to front).
//
// Strategy: For each candidate stride, walk chunk at stride-aligned offsets counting
// valid items. Use FNamePool-based name resolution (strong) if available, falling back
// to ClassPrivate chain (weak) if not. Pick the best stride; tiebreaker prefers fewer bad.
//
// Two object-ptr-offset passes run in order: classic +0x00 FIRST so every previously
// working game keeps its exact prior detection path and result, then UE5.7+ +0x08 ONLY
// when the classic pass is unconvincing. On a reordered (UE5.7+) item, reading +0x00
// yields the int64 FlagsAndRefCount; a stride-ALIGNED scan never resolves a name, but a
// MIS-strided scan (e.g. stride 16 over a 24-byte item) lands on the real +0x08 Object
// field ~1/3 of the time, so the classic pass can look weakly "valid" with named ≈ bad ≈
// null ≈ 1/3 each (seen on Solarpunk, stock UE5.7). Hence the accept gate also requires
// named > bad: a correct layout resolves nearly every non-null slot (bad ≈ 0), so a
// bad-dominated pass is rejected and the +0x08 pass gets its turn.
static void DetectItemSize() {
    uintptr_t chunkTable = 0;
    if (!Macht::ReadSafe(s_arrayAddr + s_layout.objectsOffset, chunkTable) || !chunkTable) {
        LOG_WARN("ObjectArray: Cannot read chunk table for item size detection");
        return;
    }
    chunkTable = DecryptObjectPtr(chunkTable);

    // Diagnostic: dump first 64 bytes at chunkTable address
    {
        uint64_t dump[8] = {};
        Macht::ReadBytesSafe(chunkTable, dump, sizeof(dump));
        LOG_DEBUG("ObjectArray: chunkTable@0x%llX: +00:%016llX +08:%016llX +10:%016llX +18:%016llX +20:%016llX +28:%016llX +30:%016llX +38:%016llX",
                  (unsigned long long)chunkTable,
                  dump[0], dump[1], dump[2], dump[3], dump[4], dump[5], dump[6], dump[7]);
    }

    uintptr_t chunk0 = 0;
    if (!Macht::ReadSafe(chunkTable, chunk0) || !chunk0) {
        LOG_WARN("ObjectArray: Cannot read chunk[0] for item size detection");
        return;
    }

    // Reset the layout mode each detection run (Init may be called again on re-attach).
    // The two direct passes below keep it non-packed; only the last-resort packed branch
    // promotes it to Packed57.
    s_layoutMode = Lineal::ItemLayoutMode::Classic;

    // Preset-bound item hint (licensee forks). Tried FIRST and ONLY when the winning
    // layout preset carried one, so the shared sweep below is byte-for-byte unchanged for
    // every other title. Must precede the sweep: on MindsEye the true 32/+0x10 item
    // aliases with stride 16 (every odd 16-byte slot is a real object pointer), which
    // scores good=100/bad=100 and would otherwise win with half the pool unreachable.
    // Still gated on evidence — if the hint does not clearly beat noise we fall through.
    if (s_hintItemStride > 0) {
        s_itemObjOffset = s_hintItemObjOff;
        s_isFlat = false;
        constexpr int HINT_PROBE_ITEMS = 200;   // same depth as the phase-1 stride probes
        int hGood, hNamed, hNull, hBad;
        ProbeStride(chunk0, s_hintItemStride, HINT_PROBE_ITEMS, hGood, hNamed, hNull, hBad);
        // A correct stride/offset pair reads a real pointer at EVERY item, so demand a
        // strong majority of valid reads. The aliased stride-16 result cannot reach this
        // (it is 50% bad by construction).
        if (hGood >= 8 && hBad * 4 <= hGood) {
            s_itemSize   = s_hintItemStride;
            s_layoutMode = (s_itemObjOffset != 0) ? Lineal::ItemLayoutMode::Unpacked57
                                                  : Lineal::ItemLayoutMode::Classic;
            LOG_INFO("ObjectArray: FUObjectItem size=%d, object-ptr offset=+0x%02X (preset item hint) — %d named, %d total, %d bad",
                     s_hintItemStride, s_itemObjOffset, hNamed, hGood, hBad);
            return;
        }
        LOG_WARN("ObjectArray: preset item hint %d/+0x%02X rejected (good=%d bad=%d) — falling back to stride sweep",
                 s_hintItemStride, s_hintItemObjOff, hGood, hBad);
        s_itemObjOffset = 0;
    }

    // 32 is here because a stock UE 5.4 **Development** package uses it, and its absence
    // was not a near-miss -- it was undetectable. A stride that DIVIDES the real one still
    // lands on a genuine object every k-th probe, so 16 against a real 32 validated half
    // the pool and the sweep settled there "tentatively". Downstream, exactly every other
    // object was garbage: UE5_Init resolved obj[0], obj[2], obj[4] and the Object Tree
    // reported 12,588 of 25,175 named -- 50.0% to the decimal (2026-08-05, DumperTest).
    //
    // Ordering does not decide the winner (ProbeAllStrides scores every candidate and takes
    // the best), so 32 sits after the two common sizes purely for log readability. With the
    // real stride present it wins outright: named ~= all / bad ~= 0, against the alias's
    // named ~= bad ~= half.
    int candidates[] = { 16, 24, 32, 20 };
    constexpr int NUM_CANDIDATES = 4;
    static_assert(NUM_CANDIDATES <= 5, "ProbeAllStrides stores results in a fixed array of 5");

    // Object-ptr-offset candidates, classic first (see header comment).
    const int objOffPasses[] = { 0x00, 0x08 };

    // Strongest result seen across passes, for the tentative fallback below.
    int gStride = 0, gCount = 0, gNamed = 0, gBad = INT_MAX, gObjOff = 0;
    bool gHasNames = false, gFlat = false;

    for (int pass = 0; pass < 2; ++pass) {
        s_itemObjOffset = objOffPasses[pass];
        s_isFlat = false;

        int bestStride, bestCount, bestNamed, bestBad;
        bool bestHasNames;
        DetectStrideForCurrentObjOffset(chunkTable, chunk0, candidates, NUM_CANDIDATES,
                                        bestStride, bestCount, bestNamed, bestBad, bestHasNames);

        int threshold = bestHasNames ? 2 : 3;
        int bestTotal = bestHasNames ? bestNamed : bestCount;
        // A name-resolving pass is only trustworthy if valid items clearly outnumber bad
        // reads. On a UE5.7+ reordered item the mis-strided classic (+0x00) scan lands on
        // the real Object field only ~1/3 of the time (named ≈ bad), so reject a bad-
        // dominated pass and let the +0x08 pass run. A correct layout has bad ≈ 0. The
        // count-only path (no FName check) keeps its prior behaviour (no bad confidence).
        bool qualityOk = !bestHasNames || bestNamed > bestBad;

        // Track the strongest pass (strictly-better, so ties keep the earlier/classic pass).
        if (bestNamed > gNamed || (bestNamed == gNamed && bestCount > gCount)) {
            gStride = bestStride; gCount = bestCount; gNamed = bestNamed; gBad = bestBad;
            gHasNames = bestHasNames; gObjOff = s_itemObjOffset; gFlat = s_isFlat;
        }

        if (bestTotal >= threshold && qualityOk) {
            s_itemSize = bestStride;   // s_itemObjOffset / s_isFlat already reflect this pass
            s_layoutMode = (s_itemObjOffset != 0) ? Lineal::ItemLayoutMode::Unpacked57
                                                  : Lineal::ItemLayoutMode::Classic;
            if (s_itemObjOffset != 0) {
                LOG_INFO("ObjectArray: FUObjectItem size=%d, object-ptr offset=+0x%02X (UE5.7+ reordered item) — %d named, %d total, %d bad%s",
                         bestStride, s_itemObjOffset, bestNamed, bestCount, bestBad, s_isFlat ? " (flat)" : "");
            } else if (bestHasNames) {
                LOG_INFO("ObjectArray: FUObjectItem size detected as %d bytes (%d items with valid names, %d total valid, %d bad)",
                         bestStride, bestNamed, bestCount, bestBad);
            } else {
                LOG_INFO("ObjectArray: FUObjectItem size detected as %d bytes (%d items validated, no FName check)",
                         bestStride, bestCount);
            }
            return;
        }

        if (pass == 0) {
            LOG_INFO("ObjectArray: classic (+0x00) item detection weak (named=%d, count=%d, bad=%d) — retrying with UE5.7+ object-ptr offset +0x08",
                     bestNamed, bestCount, bestBad);
        }
    }

    // Neither DIRECT pass crossed the confidence threshold — fall back to the strongest
    // seen. A weak-but-real direct match still beats the unverified packed path, so the
    // tentative fallback takes precedence over packed detection below.
    s_itemObjOffset = gObjOff;
    s_isFlat = gFlat;
    if (gStride > 0 && (gHasNames ? gNamed : gCount) > 0) {
        s_itemSize = gStride;
        s_layoutMode = (gObjOff != 0) ? Lineal::ItemLayoutMode::Unpacked57
                                      : Lineal::ItemLayoutMode::Classic;
        const int validated = gHasNames ? gNamed : gCount;
        LOG_WARN("ObjectArray: FUObjectItem size tentatively set to %d bytes, object-ptr offset +0x%02X (only %d items validated)",
                 gStride, gObjOff, validated);
        // Say what "tentative" COSTS, because the previous wording read as routine and the
        // scan carried on as though it were an answer. A validated count this far below the
        // probe budget is the signature of an ALIAS: the real item is a multiple of this
        // stride, so every k-th probe hits a real object and the rest are garbage — which
        // surfaces later as a suspiciously round "N% of objects named" and a scan that walks
        // almost nothing. If that is what you are looking at, the missing stride is the bug.
        if (validated * 4 < kStrideProbeBudget) {
            LOG_ERROR("ObjectArray: that is only %d of %d probes — treat every object count and "
                      "name below as UNTRUSTWORTHY. A real stride that is a MULTIPLE of %d "
                      "(e.g. %d) would validate all of them; if the object tree shows a round "
                      "fraction named, that multiple is missing from the candidate list.",
                      validated, kStrideProbeBudget, gStride, gStride * 2);
        }
        return;
    }

    // Both direct modes produced NOTHING — the dump would be empty. As a LAST RESORT try
    // the UE5.7+ PACKED FUObjectItem encoding (reconstruct the ptr from two split fields).
    // This path is *** UNVERIFIED *** (no shipping game uses it yet) and only runs here,
    // where there is nothing to regress — a wrong activation is no worse than the empty
    // dump it replaces, and it is loudly flagged.
    PackedProbeResult packed;
    if (TryDetectPacked(chunkTable, chunk0, packed)) {
        s_layoutMode    = Lineal::ItemLayoutMode::Packed57;
        s_itemSize      = packed.stride;   // 24 expected
        s_itemObjOffset = 0;               // unused for the object read under packing
        s_isFlat        = packed.isFlat;
        LOG_WARN("ObjectArray: *** UNVERIFIED UE5.7+ PACKED FUObjectItem layout ACTIVATED *** "
                 "stride=%d %s, %d reconstructed (%d named) of %d probed. This packed encoding "
                 "has NEVER been validated against a real game — object addresses, serial numbers "
                 "and every downstream export (CE XML / CSX / Teleport) are BEST-EFFORT. "
                 "Constants: alignBits=%d, ptrMask=0x%llX (recalibrate via set_packed_consts if "
                 "names look wrong).",
                 packed.stride, packed.isFlat ? "(flat)" : "(chunked)",
                 packed.good, packed.named, packed.probed,
                 s_packedConsts.alignBits, (unsigned long long)s_packedConsts.ptrMaskBits);
        return;
    }

    // Even packed reconstruction failed — the layout is genuinely unrecognised.
    s_itemObjOffset = 0;
    LOG_WARN("ObjectArray: Could not auto-detect item size, keeping default %d", s_itemSize);
    LogPackedDiagnosticNegative();
}

void Init(uintptr_t gobjectsAddr) {
    s_arrayAddr = gobjectsAddr;
    DetectLayout(gobjectsAddr);
    DetectItemSize();
    LOG_INFO("ObjectArray: Initialized at 0x%llX, Count=%d, ItemSize=%d",
             static_cast<unsigned long long>(gobjectsAddr), GetCount(), s_itemSize);
}

void InitWithExtendedLayout(uintptr_t gobjectsAddr, int forcedItemSize) {
    s_arrayAddr = gobjectsAddr;
    // UE5 chunked-extended: { Objects@+0x10, MaxElements@+0x20, NumElements@+0x24,
    // MaxChunks@+0x28, NumChunks@+0x2C }. Forced (no DetectLayout) — the caller has
    // already confirmed this layout by content (first objects resolve to clean names),
    // so we must not let relaxed auto-detection read NumElements at a wrong offset.
    s_layout = { 0x10, 0x20, 0x24, 0x28, 0x2C };
    s_isFlat = false;
    if (forcedItemSize > 0) {
        // Classic direct item: object pointer at +0x00. Obsidian's UE5.3 packs
        // FUObjectItem to 20 bytes (0x14) — auto-detection can mis-pick 24, so the
        // caller (which already verified this stride by content) forces it.
        s_itemSize = forcedItemSize;
        s_itemObjOffset = 0;
        s_layoutMode = Lineal::ItemLayoutMode::Classic;
        LOG_INFO("ObjectArray: Initialized (forced UE5-Extended, stride=%d) at 0x%llX, Count=%d",
                 forcedItemSize, static_cast<unsigned long long>(gobjectsAddr), GetCount());
    } else {
        DetectItemSize();
        LOG_INFO("ObjectArray: Initialized (forced UE5-Extended) at 0x%llX, Count=%d, ItemSize=%d",
                 static_cast<unsigned long long>(gobjectsAddr), GetCount(), s_itemSize);
    }
}

int32_t GetCount() {
    if (!s_arrayAddr) return 0;
    int32_t count = 0;
    Macht::ReadSafe(s_arrayAddr + s_layout.numElementsOffset, count);
    return count;
}

int32_t GetMax() {
    if (!s_arrayAddr) return 0;
    int32_t max = 0;
    Macht::ReadSafe(s_arrayAddr + s_layout.maxElementsOffset, max);
    return max;
}

int GetItemSize() {
    return s_itemSize;
}

bool IsFlat() {
    return s_isFlat;
}

// Within-item byte offset of the UObject* for the two DIRECT layouts (0x00 classic,
// 0x08 UE5.7+ unpacked). Meaningless under Packed57 (reconstructed, not stored) — the
// CE-chain caller in Fern guards on IsPacked() before using this.
int GetItemObjOffset() {
    return s_itemObjOffset;
}

bool IsPacked() {
    return s_layoutMode == Lineal::ItemLayoutMode::Packed57;
}

// Runtime calibration for the *** UNVERIFIED *** packed reconstruction. Lets the first
// real packed game tune alignBits / ptrMaskBits (and optionally the serial offset) and
// force-activate packed mode without a rebuild. force=true switches s_layoutMode to
// Packed57 unconditionally (used by the set_packed_consts pipe command to dry-run the
// reconstruction against a game even when normal detection chose a direct mode).
void SetPackedConsts(int alignBits, uint64_t ptrMaskBits, bool force, int serialOff) {
    if (alignBits > 0)   s_packedConsts.alignBits   = alignBits;
    if (ptrMaskBits != 0) s_packedConsts.ptrMaskBits = ptrMaskBits;
    if (serialOff >= 0)  s_packedSerialOff = serialOff;
    if (force) {
        s_layoutMode = Lineal::ItemLayoutMode::Packed57;
        s_itemObjOffset = 0;
        LOG_WARN("ObjectArray: *** packed mode FORCE-ENABLED via SetPackedConsts *** "
                 "alignBits=%d, ptrMask=0x%llX, serialOff=0x%X, stride=%d. This is an "
                 "UNVERIFIED layout — reconstructed addresses are best-effort.",
                 s_packedConsts.alignBits, (unsigned long long)s_packedConsts.ptrMaskBits,
                 s_packedSerialOff, s_itemSize);
    } else {
        LOG_INFO("ObjectArray: packed consts updated: alignBits=%d, ptrMask=0x%llX, serialOff=0x%X",
                 s_packedConsts.alignBits, (unsigned long long)s_packedConsts.ptrMaskBits,
                 s_packedSerialOff);
    }
}

uintptr_t GetByIndex(int32_t index) {
    if (!s_arrayAddr || index < 0 || index >= GetCount()) return 0;

    // Read array base pointer
    uintptr_t arrayBase = 0;
    if (!Macht::ReadSafe(s_arrayAddr + s_layout.objectsOffset, arrayBase) || !arrayBase) return 0;
    arrayBase = DecryptObjectPtr(arrayBase);

    uintptr_t itemAddr = 0;

    if (s_isFlat) {
        // Flat (non-chunked): items are at arrayBase + index * itemSize
        itemAddr = arrayBase + static_cast<uintptr_t>(index) * s_itemSize;
    } else {
        // Chunked: arrayBase is a chunk table, each chunk holds OBJECTS_PER_CHUNK items
        int32_t chunkIndex = index / Grimoire::OBJECTS_PER_CHUNK;
        int32_t withinChunk = index % Grimoire::OBJECTS_PER_CHUNK;

        uintptr_t chunk = 0;
        if (!Macht::ReadSafe(arrayBase + chunkIndex * sizeof(uintptr_t), chunk) || !chunk) return 0;

        itemAddr = chunk + static_cast<uintptr_t>(withinChunk) * s_itemSize;
    }

    if (s_layoutMode == Lineal::ItemLayoutMode::Packed57) {
        // *** UNVERIFIED *** UE5.7+ packed item: UObject* is split across two fields.
        uint64_t flags = 0; uint32_t ptrLow = 0;
        if (!Macht::ReadSafe(itemAddr, flags))         return 0;
        if (!Macht::ReadSafe(itemAddr + 0x08, ptrLow)) return 0;
        return Lineal::Reconstruct(flags, ptrLow, s_packedConsts);
    }

    uintptr_t object = 0;
    Macht::ReadSafe(itemAddr + s_itemObjOffset, object);  // +0x00 classic, +0x08 UE5.7+
    return object;
}

FUObjectItem* GetItem(int32_t index) {
    if (!s_arrayAddr || index < 0 || index >= GetCount()) return nullptr;

    uintptr_t arrayBase = 0;
    if (!Macht::ReadSafe(s_arrayAddr + s_layout.objectsOffset, arrayBase) || !arrayBase) return nullptr;
    arrayBase = DecryptObjectPtr(arrayBase);

    uintptr_t itemAddr = 0;

    if (s_isFlat) {
        itemAddr = arrayBase + static_cast<uintptr_t>(index) * s_itemSize;
    } else {
        int32_t chunkIndex = index / Grimoire::OBJECTS_PER_CHUNK;
        int32_t withinChunk = index % Grimoire::OBJECTS_PER_CHUNK;

        uintptr_t chunk = 0;
        if (!Macht::ReadSafe(arrayBase + chunkIndex * sizeof(uintptr_t), chunk) || !chunk) return nullptr;

        itemAddr = chunk + static_cast<uintptr_t>(withinChunk) * s_itemSize;
    }

    return Macht::Ptr<FUObjectItem>(itemAddr);
}

int32_t GetSerialNumber(int32_t index) {
    if (!s_arrayAddr || index < 0 || index >= GetCount()) return 0;

    uintptr_t arrayBase = 0;
    if (!Macht::ReadSafe(s_arrayAddr + s_layout.objectsOffset, arrayBase) || !arrayBase)
        return 0;
    arrayBase = DecryptObjectPtr(arrayBase);

    uintptr_t itemAddr = 0;
    if (s_isFlat) {
        itemAddr = arrayBase + static_cast<uintptr_t>(index) * s_itemSize;
    } else {
        int32_t chunkIndex  = index / Grimoire::OBJECTS_PER_CHUNK;
        int32_t withinChunk = index % Grimoire::OBJECTS_PER_CHUNK;
        uintptr_t chunk = 0;
        if (!Macht::ReadSafe(arrayBase + chunkIndex * sizeof(uintptr_t), chunk) || !chunk)
            return 0;
        itemAddr = chunk + static_cast<uintptr_t>(withinChunk) * s_itemSize;
    }

    // The whole offset rule lives in Lineal so it can be unit-pinned — no target
    // compiles Aura.cpp, and the old inline `s_itemSize >= 24 ? 0x10 : 0x0C`
    // silently returned 0x0C for the reachable 20-byte packed item, which reads
    // ClusterRootIndex and made every weak reference look stale (audit #5 A1).
    const int serialOff = Lineal::SerialOffsetForLayout(
        s_layoutMode, s_itemSize, s_itemObjOffset, s_packedSerialOff);

    int32_t serial = 0;
    Macht::ReadSafe(itemAddr + serialOff, serial);
    return serial;
}

void ForEach(std::function<bool(int32_t idx, uintptr_t obj)> cb) {
    int32_t count = GetCount();
    for (int32_t i = 0; i < count; ++i) {
        if ((i & 0xFFF) == 0 && Tot::Requested()) {
            Sein::Warn("PIPE:scan", "Aura::ForEach: aborted (client gone / shutdown)");
            break;  // stop walking; callers see partial/empty result
        }
        uintptr_t obj = GetByIndex(i);
        if (obj != 0) {
            if (!cb(i, obj)) break;
        }
    }
}

uintptr_t FindByName(const std::string& name) {
    uintptr_t result = 0;
    ForEach([&](int32_t /*idx*/, uintptr_t obj) -> bool {
        // Read FName from UObject
        uint32_t nameIndex = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIndex)) return true;

        std::string objName = Serie::GetString(nameIndex);
        if (objName == name) {
            result = obj;
            return false; // Stop iteration
        }
        return true;
    });
    return result;
}

uintptr_t FindByFullName(const std::string& fullName) {
    // Resolve an object written as a PATH ("/Script/Engine.Actor"), as opposed to
    // FindByName's bare-FName match ("Actor").
    //
    // This was a stub returning 0 from the day it was declared, while `Aura.h`,
    // `docs/dll-spec.md` and `UE5_FindObject`'s own `fullPath` parameter name all
    // advertised it as working. Nothing called it, so nothing failed loudly — the
    // path capability simply did not exist, and `ue5_dissect.lua`'s
    // `createFromPath` (whose interactive dialog is PRE-FILLED with
    // "/Script/Engine.Actor") could never have resolved anything.
    //
    // Two-stage match, and the order is the point: `Ubel::GetFullName` walks the
    // whole Outer chain and allocates, so running it on all ~85K objects would make
    // this far slower than the scan it supports. The FName pre-filter reduces that
    // to the handful of objects that share the leaf name.
    const std::string wantPath = CanonicalizeObjectPath(fullName);
    if (wantPath.empty()) return 0;
    const std::string wantLeaf = PathLeafName(fullName);
    if (wantLeaf.empty()) return 0;

    uintptr_t result = 0;
    ForEach([&](int32_t /*idx*/, uintptr_t obj) -> bool {
        uint32_t nameIndex = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIndex)) return true;

        // Cheap gate first — see above.
        if (Serie::GetString(nameIndex) != wantLeaf) return true;

        if (CanonicalizeObjectPath(Ubel::GetFullName(obj)) == wantPath) {
            result = obj;
            return false;  // Stop iteration
        }
        return true;
    });
    return result;
}

uintptr_t FindByNameOrPath(const std::string& query) {
    // The single entry point every "find me this object" caller should use.
    //
    // A path is tried FIRST when the query carries a separator, because a path is
    // the more specific request: "/Game/Maps/Foo.Foo" and "/Game/Other/Foo.Foo"
    // share the leaf name "Foo", and answering either with whichever FName the
    // GObjects walk reached first is a wrong answer that looks like a right one.
    // Bare names keep their old single-pass cost — LooksLikeObjectPath is false, so
    // FindByName runs directly and nothing about the historical behaviour changes.
    if (LooksLikeObjectPath(query)) {
        if (uintptr_t byPath = FindByFullName(query)) return byPath;
        // Deliberate fallback: an object whose FName legitimately contains a '.'
        // (asset names do) would otherwise become unreachable through this path.
        return FindByName(query);
    }
    return FindByName(query);
}

SearchResultSet SearchByName(const std::string& query, int maxResults, bool instancesOnly) {
    SearchResultSet rset;

    // Whitespace-separated terms are ANDed; each term matches the object name OR the
    // class name (field-level OR) — mirrors the client ObjectTreeFilter so the top
    // Search box behaves like the bottom filter (class-aware + space=AND). The pipe
    // handler already rejects an empty query string; empty terms would match-all.
    const std::vector<std::string> terms = SplitLowerKeywords(query);

    int32_t count = GetCount();
    rset.scanned = count;
    for (int32_t i = 0; i < count && static_cast<int>(rset.results.size()) < maxResults; ++i) {
        if ((i & 0xFFF) == 0 && Tot::Requested()) {
            Sein::Warn("PIPE:search", "SearchByName: aborted (client gone / shutdown)");
            break;  // return partial result
        }
        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;
        rset.nonNull++;

        // Read object FName.
        uint32_t nameIndex = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIndex)) continue;

        std::string objName = Serie::GetString(nameIndex);
        if (objName.empty()) continue;
        rset.named++;

        // Resolve class — its name is needed for the class-name match, the
        // instances-only gate, and the returned row.
        uintptr_t cls = 0;
        std::string clsName;
        if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) && cls) {
            uint32_t clsNameIdx = 0;
            if (Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx))
                clsName = Serie::GetString(clsNameIdx);
        }

        // Instances-only gate: drop the reflection/type layer (UClass / UFunction /
        // UScriptStruct / UEnum / UPackage, UE4 FooProperty) so only live instances remain.
        if (instancesOnly && IsReflectionMetaClass(clsName)) continue;

        // space=AND: every term must hit the object name OR the class name.
        if (!MatchesAllKeywords(terms, objName, clsName)) continue;

        SearchResult sr;
        sr.addr = obj;
        sr.name = objName;
        sr.className = clsName;
        sr.classAddr = cls;

        // Get outer
        Macht::ReadSafe(obj + DynOff::UOBJECT_OUTER, sr.outer);

        rset.results.push_back(std::move(sr));
    }

    // Early-exit at the cap means "at least maxResults matched" — report truncation the
    // same way FindInstancesByClass does on its cheap path so the UI can flag "more
    // exist; narrow the search, or Reload + filter for the whole pool".
    rset.truncated = (static_cast<int>(rset.results.size()) >= maxResults);
    return rset;
}

SearchResultSet FindInstancesByClass(const std::string& className, bool exactMatch, int maxResults, bool newestFirst, const std::string& nameFilter, const std::vector<std::string>& excludeClasses, bool buildHistogram) {
    SearchResultSet rset;

    // Convert queries to lowercase for case-insensitive comparison
    std::string lowerQuery = className;
    for (auto& c : lowerQuery) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    std::string lowerName = nameFilter;
    for (auto& c : lowerName) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

    // An empty class query means "match any class" (object-name-only search).
    const bool matchAnyClass = lowerQuery.empty();
    // An empty name query means "no object-name gate".
    const bool gateByName = !lowerName.empty();

    // Server-side class-noise exclusion — EXACT, case-SENSITIVE (names came from a
    // prior histogram, correctly cased; folding case here would over-exclude a game
    // class that merely shares a substring with an engine class).
    const std::unordered_set<std::string> excludeSet(excludeClasses.begin(), excludeClasses.end());
    const bool hasExclude = !excludeSet.empty();

    // Full-pool class tally (only when the picker needs it). Counted over EVERY row
    // that satisfies the class+name query, BEFORE the exclude-skip and INDEPENDENTLY
    // of the result cap — so an excluded class (or one whose instances all sit past
    // the cap) still shows in the Top-N and stays untickable.
    std::unordered_map<std::string, int> histMap;
    // Matched rows that survived the exclude filter (may exceed results.size() when
    // the cap truncated the returned list — that's what drives `truncated`).
    int matchedNonExcluded = 0;

    int32_t count = GetCount();
    rset.scanned = count;
    // `n` is the visit counter (0..count); `i` is the real GObjects index, walked
    // high->low when newestFirst so the most-recently-allocated matches (the newest
    // runtime spawns) are the ones kept under the maxResults cap. Default low->high
    // keeps the oldest (CDO / class-default / earliest instances).
    //
    // buildHistogram=false (internal callers): keep the cheap early-exit at the cap.
    // buildHistogram=true (pipe): walk ALL of GObjects so the histogram is complete.
    for (int32_t n = 0; n < count; ++n) {
        if (!buildHistogram && static_cast<int>(rset.results.size()) >= maxResults)
            break;   // cheap path: stop collecting once the cap is full
        int32_t i = newestFirst ? (count - 1 - n) : n;
        if ((n & 0xFFF) == 0 && Tot::Requested()) {
            Sein::Warn("PIPE:find", "FindInstancesByClass: aborted (client gone / shutdown)");
            break;  // return partial result
        }
        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;
        rset.nonNull++;

        // Read ClassPrivate
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        // Read class FName
        uint32_t clsNameIdx = 0;
        if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;

        std::string clsName = Serie::GetString(clsNameIdx);
        if (clsName.empty()) continue;
        rset.named++;

        // Class match (skipped entirely for object-name-only search):
        // exact (equality) or partial (substring), case-insensitive.
        if (!matchAnyClass) {
            std::string lowerClsName = clsName;
            for (auto& c : lowerClsName) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

            if (exactMatch) {
                if (lowerClsName != lowerQuery) continue;
            } else {
                if (lowerClsName.find(lowerQuery) == std::string::npos) continue;
            }
        }

        // Read object name (needed for the optional name gate and the result).
        std::string objName;
        uint32_t nameIdx = 0;
        if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx)) {
            objName = Serie::GetString(nameIdx);
        }

        // Object-name gate (case-insensitive substring), when requested.
        if (gateByName) {
            std::string lowerObjName = objName;
            for (auto& c : lowerObjName) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
            if (lowerObjName.find(lowerName) == std::string::npos) continue;
        }

        // This row satisfies the class+name query. Tally it PRE-exclude (full pool)
        // so the picker can still surface — and untick — an excluded/past-cap class.
        if (buildHistogram) ++histMap[clsName];

        // Server-side class-noise skip: excluded classes never consume a cap slot,
        // so a wanted instance that today sits past the cap survives the exclusion.
        if (hasExclude && excludeSet.count(clsName)) continue;
        ++matchedNonExcluded;

        // Collect into the returned list only until the cap is full; past that we
        // keep scanning purely to finish the histogram (buildHistogram path).
        if (static_cast<int>(rset.results.size()) < maxResults) {
            SearchResult sr;
            sr.addr = obj;
            sr.index = i;
            sr.name = objName;
            sr.className = clsName;
            sr.classAddr = cls;   // ClassPrivate read above — key for find_functions_by_class

            // Read outer
            Macht::ReadSafe(obj + DynOff::UOBJECT_OUTER, sr.outer);

            rset.results.push_back(std::move(sr));
        }
    }

    // truncated now means "more non-excluded matches exist than the cap returned".
    // On the cheap internal path matchedNonExcluded stops at the cap (we break), so
    // fall back to the classic "hit the cap" test there.
    rset.truncated = buildHistogram
        ? (matchedNonExcluded > static_cast<int>(rset.results.size()))
        : (static_cast<int>(rset.results.size()) >= maxResults);

    // Materialize + sort the histogram (count desc, then name asc — mirrors the
    // value-scan picker shape).
    if (buildHistogram) {
        rset.classHistogram.reserve(histMap.size());
        for (const auto& kv : histMap) rset.classHistogram.emplace_back(kv.first, kv.second);
        std::sort(rset.classHistogram.begin(), rset.classHistogram.end(),
                  [](const std::pair<std::string, int>& a, const std::pair<std::string, int>& b) {
                      if (a.second != b.second) return a.second > b.second;   // count desc
                      return a.first < b.first;                               // name asc
                  });
        rset.classDistinct = static_cast<int>(rset.classHistogram.size());
    }

    Sein::Info("PIPE:find", "FindInstancesByClass class='%s' name='%s': %d found%s, scanned=%d, nonNull=%d, named=%d, distinct=%d, excluded=%d",
                 className.c_str(), nameFilter.c_str(), (int)rset.results.size(),
                 rset.truncated ? " (capped)" : "", rset.scanned, rset.nonNull, rset.named,
                 rset.classDistinct, (int)excludeSet.size());
    return rset;
}

// True when `cls` itself, or any class in its super chain, is named
// `lowerName` (already lowercased). The case-insensitive sibling of
// ClassDerivesFromAny — that one takes a set of correctly-cased engine base
// names, this one takes a single name that reached us over the pipe, where
// FindInstancesByClass has always folded case. Bounded at 64 levels for the
// same reason: real super-chains are ~5-10 deep, so a longer one means a
// corrupt or recycled UClass pointer, not a deep hierarchy.
static bool ClassChainMatchesLower(uintptr_t cls, const std::string& lowerName) {
    for (int guard = 0; cls && guard < 64; ++guard) {
        std::string n = Ubel::GetName(cls);
        for (auto& c : n) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
        if (n == lowerName) return true;
        uintptr_t super = 0;
        if (!Macht::ReadSafe(cls + static_cast<uintptr_t>(DynOff::USTRUCT_SUPER), super)
            || !super || super == cls)
            break;
        cls = super;
    }
    return false;
}

SearchResultSet FindInstancesDerivedFrom(const std::string& baseClassName, int maxResults,
                                         uintptr_t outerFilter, int32_t* totalOut) {
    SearchResultSet rset;
    if (totalOut) *totalOut = 0;
    if (baseClassName.empty() || maxResults <= 0) return rset;

    std::string lowerQuery = baseClassName;
    for (auto& c : lowerQuery) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

    // Per-UClass verdict cache. The chain walk costs one FName resolve per
    // level, and GObjects holds 10^5-10^6 objects spread over 10^3-10^4
    // distinct classes — caching by UClass* turns a per-OBJECT walk into a
    // per-CLASS one, which is what makes "AActor and everything under it"
    // affordable at all.
    std::unordered_map<uintptr_t, bool> derivedCache;

    int32_t count = GetCount();
    rset.scanned = count;
    int32_t matchTotal = 0;   // matches BEFORE the cap — exact when totalOut was asked for
    for (int32_t i = 0; i < count; ++i) {
        // When the caller asked for an exact total we must keep COUNTING past the cap
        // and stop only APPENDING; otherwise `total` degenerates into the page size and
        // a 500-actor page is indistinguishable from a 500-actor level (audit #5 F6).
        if (!totalOut && static_cast<int>(rset.results.size()) >= maxResults) break;
        if ((i & 0xFFF) == 0 && Tot::Requested()) {
            Sein::Warn("PIPE:find", "FindInstancesDerivedFrom: aborted (client gone / shutdown)");
            rset.aborted = true;   // the total is INCOMPLETE — callers must not publish it
            break;   // return partial result
        }
        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;
        rset.nonNull++;

        // Outer gate FIRST when filtering: one 8-byte read per pool entry, versus a
        // class read + a memo probe + an FName decode. Ordering this cheapest-first is
        // what makes an outer-filtered pass affordable over a 10^5-10^6 object pool.
        uintptr_t objOuter = 0;
        if (outerFilter) {
            if (!Macht::ReadSafe(obj + DynOff::UOBJECT_OUTER, objOuter)) continue;
            if (objOuter != outerFilter) continue;
        }

        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        bool isDerived;
        auto it = derivedCache.find(cls);
        if (it != derivedCache.end()) {
            isDerived = it->second;
        } else {
            isDerived = ClassChainMatchesLower(cls, lowerQuery);
            derivedCache.emplace(cls, isDerived);
        }
        if (!isDerived) continue;

        std::string objName;
        uint32_t nameIdx = 0;
        if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx))
            objName = Serie::GetString(nameIdx);
        if (objName.empty()) continue;
        rset.named++;

        // CDO skip, and it has to happen HERE — before the cap. A base like
        // AActor is the ancestor of thousands of classes, EVERY one of which
        // contributes a Default__ object, and CDOs are constructed at
        // class-load time so they sit at the LOW GObjects indices this walk
        // reaches first. A caller that filtered afterwards would be handed
        // maxResults class-default rows and not one live instance.
        if (objName.rfind("Default__", 0) == 0) continue;

        ++matchTotal;
        if (static_cast<int>(rset.results.size()) >= maxResults) continue;   // count, don't append

        SearchResult sr;
        sr.addr      = obj;
        sr.index     = i;
        sr.name      = objName;
        sr.className = Ubel::GetName(cls);   // the CONCRETE class, not the queried base
        sr.classAddr = cls;
        if (outerFilter) sr.outer = objOuter;                    // already read above
        else Macht::ReadSafe(obj + DynOff::UOBJECT_OUTER, sr.outer);
        rset.results.push_back(std::move(sr));
    }

    if (totalOut) *totalOut = matchTotal;
    // ONE source for "is this a page or the whole set". When an exact total was asked
    // for, derive the flag from it — a second flag computed by a different code path is
    // audit #4's own named root cause, and this handler's caller publishes `truncated`.
    rset.truncated = totalOut ? (matchTotal > maxResults)
                             : (static_cast<int>(rset.results.size()) >= maxResults);

    Sein::Info("PIPE:find", "FindInstancesDerivedFrom base='%s': %d live instance(s)%s over %d distinct class(es), scanned=%d, nonNull=%d",
                 baseClassName.c_str(), (int)rset.results.size(),
                 rset.truncated ? " (capped)" : "",
                 (int)derivedCache.size(), rset.scanned, rset.nonNull);
    return rset;
}

SearchResultSet FindActorsInLevel(uintptr_t levelAddr, int maxResults, int32_t* totalOut) {
    SearchResultSet rset;
    if (totalOut) *totalOut = 0;
    // Zero means "nobody said", never "match everything". Without this a caller that
    // failed to resolve PersistentLevel would silently get every actor in the game
    // presented as the contents of one level.
    if (!levelAddr) {
        Sein::Warn("PIPE:world", "FindActorsInLevel: refused — no level address supplied");
        return rset;
    }
    return FindInstancesDerivedFrom("Actor", maxResults, /*outerFilter=*/levelAddr, totalOut);
}

// Helper: populate an AddressLookupResult from a UObject pointer.
// `kind` distinguishes confidence levels — see AddressLookupResult comment.
// A genuine FName resolves to non-empty printable ASCII. Serie::GetString
// sanitizes non-printable ANSI bytes to '?' and (after the wide-name guard)
// returns "" for mojibake, so any '?', non-ASCII byte, or emptiness marks a
// junk decode. Used to gate backward-scan candidates whose arbitrary +0x18
// bytes can resolve to garbage names — keeps find_by_address from surfacing a
// misidentified "object" that the Live Walker then walks into 亂碼.
static bool IsCleanFName(const std::string& s) {
    if (s.empty() || s.size() > 256) return false;
    for (unsigned char c : s) {
        if (c == '?' || c < 0x20 || c >= 0x7F) return false;
    }
    return true;
}

static void FillLookupResult(AddressLookupResult& out, uintptr_t obj, int32_t index,
                             int32_t offsetFromBase, bool exact,
                             const char* kind = nullptr) {
    out.found = true;
    out.exactMatch = exact;
    out.matchKind = kind ? kind : (exact ? "exact" : "contains");
    out.objectAddr = obj;
    out.index = index;
    out.offsetFromBase = offsetFromBase;

    uint32_t nameIdx = 0;
    if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx)) {
        out.name = Serie::GetString(nameIdx);
    }
    uintptr_t cls = 0;
    if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) && cls) {
        uint32_t clsNameIdx = 0;
        if (Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) {
            out.className = Serie::GetString(clsNameIdx);
        }
    }
    Macht::ReadSafe(obj + DynOff::UOBJECT_OUTER, out.outer);
}

AddressLookupResult FindByAddress(uintptr_t addr) {
    AddressLookupResult result;
    if (!addr || !s_arrayAddr) return result;

    int32_t count = GetCount();
    if (count <= 0) return result;

    LOG_INFO("FindByAddress: Looking up 0x%llX in %d objects",
             static_cast<unsigned long long>(addr), count);

    // --- Single pass: Exact match + track top-N closest objects below addr ---
    // Tracking multiple candidates allows better containment matching
    // even when small UObjects are packed near the query address.
    struct Candidate {
        uintptr_t obj;
        int32_t   idx;
        uintptr_t dist;
    };
    constexpr int MAX_CANDIDATES = 16;
    constexpr uintptr_t MAX_CONTAINMENT_RANGE = 0x40000;  // 256KB

    Candidate candidates[MAX_CANDIDATES] = {};
    int numCandidates = 0;

    for (int32_t i = 0; i < count; ++i) {
        // audit #5 A7: this is a full-GObjects walk; poll cancellation like every sibling
        // scan (FindInstancesByClass / FindInstancesDerivedFrom / the value scans) so a
        // client disconnect or shutdown doesn't block UE5_Shutdown's join for the whole
        // pass. A cancelled lookup returns "not found": the candidate set is incomplete, so
        // emitting a nearest-match from it would be arbitrary.
        if ((i & 0xFFF) == 0 && Tot::Requested()) {
            LOG_INFO("FindByAddress: aborted (client gone / shutdown) at index %d of %d", i, count);
            return result;
        }
        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;

        // Exact match check
        if (obj == addr) {
            FillLookupResult(result, obj, i, 0, true);
            LOG_INFO("FindByAddress: Exact match at index %d (%s : %s)",
                     i, result.name.c_str(), result.className.c_str());
            return result;
        }

        // Track candidates below addr within range
        if (obj < addr) {
            uintptr_t dist = addr - obj;
            if (dist >= MAX_CONTAINMENT_RANGE) continue;

            // Insert into sorted candidates (smallest distance first)
            if (numCandidates < MAX_CANDIDATES) {
                candidates[numCandidates++] = { obj, i, dist };
                // Bubble up
                for (int j = numCandidates - 1; j > 0 && candidates[j].dist < candidates[j-1].dist; --j) {
                    auto tmp = candidates[j];
                    candidates[j] = candidates[j-1];
                    candidates[j-1] = tmp;
                }
            } else if (dist < candidates[MAX_CANDIDATES - 1].dist) {
                candidates[MAX_CANDIDATES - 1] = { obj, i, dist };
                // Bubble up
                for (int j = MAX_CANDIDATES - 1; j > 0 && candidates[j].dist < candidates[j-1].dist; --j) {
                    auto tmp = candidates[j];
                    candidates[j] = candidates[j-1];
                    candidates[j-1] = tmp;
                }
            }
        }
    }

    if (numCandidates == 0) {
        LOG_INFO("FindByAddress: No objects within 256KB below 0x%llX — will try backward scan",
                 static_cast<unsigned long long>(addr));
    } else {
        LOG_INFO("FindByAddress: No exact match. %d candidates within range. Closest at dist=0x%llX",
                 numCandidates, static_cast<unsigned long long>(candidates[0].dist));
    }

    // --- Containment check on candidates ---
    // Try each candidate (closest first), check if addr is within its PropertiesSize.
    // Pick the smallest PropertiesSize that still contains addr (most specific match).
    AddressLookupResult bestMatch;
    int32_t smallestSize = INT32_MAX;

    for (int c = 0; c < numCandidates; ++c) {
        uintptr_t obj = candidates[c].obj;
        uintptr_t dist = candidates[c].dist;

        // Read ClassPrivate to get PropertiesSize
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        int32_t propsSize = 0;
        if (!Macht::ReadSafe(cls + DynOff::USTRUCT_PROPSSIZE, propsSize)) continue;
        if (propsSize <= 0 || propsSize > 0x100000) continue;

        // Log top candidates for diagnosis
        if (c < 5) {
            uint32_t nameIdx = 0;
            std::string name = "(read fail)";
            if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx))
                name = Serie::GetString(nameIdx);
            LOG_INFO("FindByAddress: Candidate #%d: 0x%llX (%s), dist=0x%llX, propsSize=%d, %s",
                     c, static_cast<unsigned long long>(obj), name.c_str(),
                     static_cast<unsigned long long>(dist), propsSize,
                     (dist < static_cast<uintptr_t>(propsSize)) ? "CONTAINS" : "no");
        }

        // Check containment: addr >= obj && addr < obj + propsSize
        if (dist < static_cast<uintptr_t>(propsSize)) {
            if (propsSize < smallestSize) {
                smallestSize = propsSize;
                FillLookupResult(bestMatch, obj, candidates[c].idx,
                                 static_cast<int32_t>(dist), false);
            }
        }
    }

    if (bestMatch.found) {
        LOG_INFO("FindByAddress: Containment match: %s at 0x%llX, offset +0x%X",
                 bestMatch.name.c_str(),
                 static_cast<unsigned long long>(bestMatch.objectAddr),
                 bestMatch.offsetFromBase);
        return bestMatch;
    }

    // --- Backward memory scan: find UObject header before query address ---
    // When the address is inside a subobject that's NOT in GObjects (e.g.,
    // GrimAttributeSetHealth created by NewObject<>), scan backward from the
    // query address looking for a valid UObject header pattern.
    //
    // UObject header layout:
    //   +0x00: VTable* (pointer to module code/data range)
    //   +0x08: ObjectFlags (EObjectFlags, typically small value)
    //   +0x0C: InternalIndex (int32, 0..maxObjects)
    //   +0x10: ClassPrivate* (UClass*, must be non-null and point to valid memory)
    //   +0x18: NamePrivate (FName ComparisonIndex, must resolve in FNamePool)
    //   +0x20/0x28: OuterPrivate* (UObject*, nullable)
    //
    // We scan backward in 8-byte steps (UObjects are at least 8-byte aligned),
    // up to a reasonable range (64KB), checking each candidate address.

    constexpr uintptr_t MAX_BACKWARD_SCAN = 0x10000;  // 64KB backward scan

    uintptr_t moduleBase = Macht::GetModuleBase(nullptr);
    uintptr_t moduleEnd = moduleBase + Macht::GetModuleSize(nullptr);

    uintptr_t scanStart = (addr > MAX_BACKWARD_SCAN) ? (addr - MAX_BACKWARD_SCAN) : 0;
    // Align to 8 bytes
    scanStart = (scanStart + 7) & ~7ULL;

    LOG_INFO("FindByAddress: Backward scan from 0x%llX to 0x%llX (module 0x%llX-0x%llX)...",
             static_cast<unsigned long long>(addr),
             static_cast<unsigned long long>(scanStart),
             static_cast<unsigned long long>(moduleBase),
             static_cast<unsigned long long>(moduleEnd));

    uintptr_t bestScanObj = 0;
    uintptr_t bestScanDist = UINTPTR_MAX;

    // Scan from just below addr backward, in 8-byte steps
    for (uintptr_t probe = (addr & ~7ULL); probe >= scanStart && probe <= addr; probe -= 8) {
        // Quick reject: read VTable pointer
        uintptr_t vtable = 0;
        if (!Macht::ReadSafe(probe + Grimoire::OFF_UOBJECT_VTABLE, vtable) || !vtable) continue;

        // VTable should point into the module's address range
        if (vtable < moduleBase || vtable >= moduleEnd) continue;

        // Read ClassPrivate — must be non-null
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(probe + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        // ClassPrivate's VTable should also be in module range (it's a UClass)
        uintptr_t clsVtable = 0;
        if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_VTABLE, clsVtable)) continue;
        if (clsVtable < moduleBase || clsVtable >= moduleEnd) continue;

        // Read InternalIndex — should be reasonable
        int32_t idx = 0;
        if (!Macht::ReadSafe(probe + Grimoire::OFF_UOBJECT_INDEX, idx)) continue;
        if (idx < 0 || idx > 0x800000) continue;

        // Read FName ComparisonIndex — must resolve to a clean name.
        // NOTE: a plain printable-ASCII check is NOT enough — Serie::GetString
        // sanitizes junk bytes to '?' (0x3F, itself printable), so a garbage
        // name like "Property??IntProperty" would slip through. IsCleanFName
        // rejects any '?' / non-ASCII / empty result.
        uint32_t nameIdx = 0;
        if (!Macht::ReadSafe(probe + Grimoire::OFF_UOBJECT_NAME, nameIdx)) continue;
        if (nameIdx == 0) continue;  // Index 0 = "None", skip
        std::string name = Serie::GetString(nameIdx);
        if (name == "None" || !IsCleanFName(name)) continue;

        // Validate the CLASS too. A backward-scan false positive is arbitrary
        // bytes that merely resemble a UObject header; its ClassPrivate points
        // at junk whose name decodes to '?'-runs (ANSI) or CJK mojibake (wide).
        // Accepting it makes the Live Walker surface 亂碼 (DQ3 HD-2D: a value in
        // raw heap resolved to 'Property??IntProperty' with a 435-char mojibake
        // class). Require a clean class FName so only real subobjects pass.
        // (cls + clsVtable were already validated as in-module above.)
        std::string clsName;
        {
            uint32_t clsNameIdx = 0;
            if (Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx))
                clsName = Serie::GetString(clsNameIdx);
        }
        if (!IsCleanFName(clsName)) continue;

        // Containment gate: the backward scan exists to attribute addr to a
        // non-GObjects SUBobject that CONTAINS it. Require addr to fall within
        // this object's PropertiesSize — otherwise it's merely the nearest
        // header, not the owner. Without this, rejecting a close junk header
        // (above) would let the scan latch onto a real but far object (e.g. addr
        // +0x2630 past a BackgroundBlur) and mislabel it "backward". A genuine
        // miss falls through to the low-confidence "nearest" path instead.
        int32_t propsSize = 0;
        if (!Macht::ReadSafe(cls + DynOff::USTRUCT_PROPSSIZE, propsSize)) continue;
        if (propsSize <= 0 || propsSize > 0x100000) continue;
        if ((addr - probe) >= static_cast<uintptr_t>(propsSize)) continue;

        // This looks like a valid UObject that contains addr!
        uintptr_t dist = addr - probe;

        LOG_INFO("FindByAddress: Backward scan hit at 0x%llX (%s), dist=0x%llX, idx=%d",
                 static_cast<unsigned long long>(probe), name.c_str(),
                 static_cast<unsigned long long>(dist), idx);

        if (dist < bestScanDist) {
            bestScanDist = dist;
            bestScanObj = probe;
        }
        // Found the closest valid UObject — stop scanning
        // (scanning downward, first hit from addr is closest)
        break;
    }

    if (bestScanObj) {
        // Backward scan found a UObject — use it if no GObjects candidates,
        // or if it's closer than the best GObjects candidate.
        // Match kind = "backward" (medium confidence — the UObject was found
        // by memory pattern, not by GObjects, so addr is past its bounds).
        bool useBackward = (numCandidates == 0) ||
                           (bestScanDist < candidates[0].dist);
        if (useBackward) {
            FillLookupResult(result, bestScanObj, -1,
                             static_cast<int32_t>(bestScanDist), false, "backward");
            LOG_INFO("FindByAddress: Backward scan match: %s at 0x%llX, offset +0x%X",
                     result.name.c_str(),
                     static_cast<unsigned long long>(bestScanObj),
                     result.offsetFromBase);
            return result;
        }
    }

    if (numCandidates > 0) {
        // --- Fallback: Return closest GObjects object as "nearest" ---
        // Low confidence: addr is past this UObject's PropertiesSize, so we
        // are NOT actually inside it. Surfaced as a hint only — frequently
        // misleading when the address is heap-allocated container data.
        FillLookupResult(result, candidates[0].obj, candidates[0].idx,
                         static_cast<int32_t>(candidates[0].dist), false, "nearest");
        result.exactMatch = false;
        LOG_INFO("FindByAddress: Nearest GObjects fallback: %s at 0x%llX, offset +0x%X (likely outside bounds)",
                 result.name.c_str(),
                 static_cast<unsigned long long>(candidates[0].obj),
                 result.offsetFromBase);
        return result;
    }

    // Nothing found at all
    LOG_INFO("FindByAddress: No match found for 0x%llX (no candidates, no backward scan hit)",
             static_cast<unsigned long long>(addr));
    return result;
}

// === Container-Aware Address Lookup ===
//
// Persistent per-class cache of container fields (ArrayProperty / MapProperty
// / SetProperty) and their resolved per-element strides. Built lazily on
// first encounter via WalkClassEx. Empty entries (classes with no usable
// container fields) are stored so we don't re-walk them on subsequent queries.
//
// Nested struct support: many UE games store gameplay arrays inside a
// USTRUCT() rather than as direct UPROPERTY() arrays of the UObject —
// e.g. UPlayerInfo { FCharacterStats Stats; } where Stats has a TArray<int>
// Levels member. The cache builder recurses into StructProperty fields
// (depth-capped) and registers nested arrays/maps/sets with their absolute
// offset (parent struct offset + child field offset) and a dotted name
// like "Stats.Levels".

// ContainerKind moved to Aura.h (audit #5 A4) so the coverage predicate
// DeepLeafCoveredByStaticScanIndex can be unit-tested against it.

struct ContainerCacheEntry {
    int32_t       offset;       // Absolute byte offset within owner UObject
    std::string   name;         // Dotted name (e.g. "Stats.Levels")
    std::string   innerType;    // ArrayProperty: inner; Set: elem; Map: "K → V"
    int32_t       stride;       // Bytes per element/pair within Data buffer
    ContainerKind kind;
    // Struct-element descent metadata (recursive deep scan). UScriptStruct* of
    // the element / map value / map key when that side is a StructProperty, so
    // the deep matcher can recurse into a separately-allocated nested container.
    // 0 when the side is a leaf (the element IS the value). valueOffset is the
    // byte offset of the value within a Map pair (0 for Array/Set).
    uintptr_t     elemStruct  = 0;   // Array/Set element struct
    uintptr_t     valueStruct = 0;   // Map value struct
    uintptr_t     keyStruct   = 0;   // Map key struct
    int32_t       valueOffset = 0;   // Map: value offset within the pair
    // Scalar (non-struct) Map side leaf types — the real property type names of a
    // SCALAR map key/value (e.g. TMap<Name,int>: keyLeafType="NameProperty",
    // valueLeafType="IntProperty"). Set only for Maps; "" when that side is a
    // StructProperty (then *Struct above is used) and unused for Array/Set (whose
    // single leaf type is `innerType`). Lets WalkContainerLeaves emit scalar map
    // values/keys as leaves — `innerType` is the "K -> V" label, NOT a leaf type.
    std::string   keyLeafType;       // P3 scalar-keyed maps
    std::string   valueLeafType;     // P3 scalar-valued maps
};

static std::unordered_map<uintptr_t, std::vector<ContainerCacheEntry>> s_classContainerCache;
static std::mutex s_classContainerMutex;

// Recursive collector — walks `structAddr` (a UClass* or UScriptStruct*)
// and emits one ContainerCacheEntry for each ArrayProperty/MapProperty/
// SetProperty found, INCLUDING those nested inside StructProperty fields.
// Depth-capped to avoid pathological cyclic struct definitions.
static void CollectContainersRecursive(
    uintptr_t structAddr,
    int32_t baseOffset,
    const std::string& namePrefix,
    std::vector<ContainerCacheEntry>& out,
    int depth)
{
    // Reasonable cap: most UE games nest at most 1–2 levels (UObject →
    // FStruct → TArray). Depth 3 covers struct-of-struct-of-struct.
    constexpr int kMaxDepth = 3;
    if (depth > kMaxDepth) return;

    const ClassInfo& ci = Ubel::WalkClassEx(structAddr);
    for (const auto& f : ci.Fields) {
        if (!f.Address) continue;

        std::string fullName = namePrefix.empty()
            ? f.Name
            : (namePrefix + "." + f.Name);
        int32_t absOffset = baseOffset + f.Offset;

        if (f.TypeName == "ArrayProperty") {
            int32_t es = Ubel::GetArrayInnerElemSize(f.Address);
            if (es <= 0) continue;
            ContainerCacheEntry e{ absOffset, fullName, f.innerType, es, ContainerKind::Array };
            if (f.innerType == "StructProperty")
                e.elemStruct = Ubel::GetContainerInnerStructAddr(f.Address);
            out.push_back(std::move(e));
        }
        else if (f.TypeName == "SetProperty") {
            int32_t st = Ubel::GetSetElementStride(f.Address);
            if (st <= 0) continue;
            ContainerCacheEntry e{ absOffset, fullName, f.elemType, st, ContainerKind::Set };
            if (f.elemType == "StructProperty")
                e.elemStruct = Ubel::GetContainerInnerStructAddr(f.Address);
            out.push_back(std::move(e));
        }
        else if (f.TypeName == "MapProperty") {
            Ubel::MapPairLayout layout;
            if (!Ubel::GetMapPairLayout(f.Address, layout) || layout.pairStride <= 0) continue;
            std::string innerLabel = f.keyType + " → " + f.valueType;
            ContainerCacheEntry e{ absOffset, fullName, innerLabel, layout.pairStride, ContainerKind::Map };
            e.valueStruct = layout.valueStructAddr;
            e.keyStruct   = layout.keyStructAddr;
            e.valueOffset = layout.valueOffset;
            // Real leaf type per side (used by WalkContainerLeaves when the side is
            // scalar, i.e. *Struct == 0). Harmless when the side is a struct — the
            // emit gate keys on *Struct == 0, and "StructProperty" is non-scalar.
            e.keyLeafType   = f.keyType;
            e.valueLeafType = f.valueType;
            out.push_back(std::move(e));
        }
        else if (f.TypeName == "StructProperty") {
            // Descend into the nested UScriptStruct, accumulating offset
            // and dotted name. Inner UScriptStruct address lives at the
            // FProperty's subclass-extension offset.
            uintptr_t innerStruct = 0;
            if (Macht::ReadSafe(f.Address + DynOff::FSTRUCTPROP_STRUCT, innerStruct)
                && innerStruct) {
                CollectContainersRecursive(innerStruct, absOffset, fullName,
                                           out, depth + 1);
            }
        }
        else if (f.TypeName == "OptionalProperty"
                 && f.innerType == "StructProperty") {
            // TOptional<FStruct> non-intrusive layout: { T value; uint8 bIsSet; }.
            // Value lives at field+0, so offset accumulation is identical to
            // a bare StructProperty. We can't tell at cache-build time which
            // instances are set vs unset, but a container scan that hits an
            // unset slot just sees zeros and naturally fails its address
            // comparison.
            uintptr_t innerProp = 0;
            uintptr_t innerStruct = 0;
            // Probe inner FProperty* (same offset as ArrayProperty::Inner).
            if (Macht::ReadSafe(f.Address + DynOff::FARRAYPROP_INNER, innerProp)
                && innerProp
                && Macht::ReadSafe(innerProp + DynOff::FSTRUCTPROP_STRUCT, innerStruct)
                && innerStruct) {
                CollectContainersRecursive(innerStruct, absOffset, fullName,
                                           out, depth + 1);
            }
        }
    }
}

static const std::vector<ContainerCacheEntry>& GetClassContainers(uintptr_t cls) {
    {
        std::lock_guard<std::mutex> lk(s_classContainerMutex);
        auto it = s_classContainerCache.find(cls);
        if (it != s_classContainerCache.end()) return it->second;
    }

    // Build outside the lock — WalkClassEx is non-trivial and may itself
    // touch caches. Insert under lock at the end.
    std::vector<ContainerCacheEntry> entries;
    // ⚠ DO NOT MEMOIZE A BUILD THAT CAME FROM AN UNREADABLE CLASS.
    //
    // The emplace below is permanent — nothing ever evicts from this map — so a single
    // transient read fault on `cls` pins an EMPTY result for the rest of the process, and
    // every later scan of that class silently finds nothing. This is reachable today: the
    // callers guard only `!cls` (e.g. Aura.cpp's `if (!ReadSafe(obj + OFF_UOBJECT_CLASS,
    // cls) || !cls) continue;`) and pass class pointers read straight out of live objects.
    //
    // The test is the walk's own verdict, not a new heuristic: WalkClassEx returns
    // `s_emptyClassInfo` — default-constructed, so Address == 0 — both when the address is
    // null and when U4's ShouldPublishClassWalk REFUSES the class (Ubel.cpp:1119), while a
    // good walk sets `info.Address = uclassAddr`. So `Address != cls` means "this class did
    // not walk", and caching anything derived from it would be caching a failure.
    //
    // Costs one WalkClassEx on cache MISSES only, and the builder below calls it anyway —
    // so on a healthy class this is a cache hit, not a second walk.
    //
    // ⚠ This is NOT the A10 fix. A10 is the recycled-UClass* staleness, which needs the
    // return-type refactor across five caches; see its row. This is the separate, present-
    // tense defect found while scoping it.
    static const std::vector<ContainerCacheEntry> s_emptyContainers;
    if (Ubel::WalkClassEx(cls).Address != cls) return s_emptyContainers;

    CollectContainersRecursive(cls, /*baseOffset*/ 0, /*namePrefix*/ "",
                               entries, /*depth*/ 0);

    std::lock_guard<std::mutex> lk(s_classContainerMutex);
    auto [ins, _] = s_classContainerCache.emplace(cls, std::move(entries));
    return ins->second;
}

// === Shared recursive container-leaf walker (build 1204) ===
//
// Visits every scalar leaf reachable from a struct THROUGH containers (struct-
// array elements, map keys/values, set elements, AND leaf-container elements
// like TArray<int>) + direct sub-structs, to arbitrary (bounded) depth. The
// same descent that FindInContainersDeep uses to LOCATE an address, but here it
// ENUMERATES leaves via a visitor — so Value Search (value-match) and Snapshot
// capture (record) share one engine. Depth-0 (the object's own direct fields)
// is intentionally NOT emitted — those are captured by each caller's normal
// direct-field pass; only container-reachable leaves (depth >= 1) are visited.
struct ContainerLeaf {
    const std::string& arrayPath;   // dotted path to the deepest container, outer
                                    // indices substituted, e.g.
                                    // "SaveSlotList[1].MsTuneData.MsTunes[0].WeaponTuneList[0].Tunes"
    int32_t            elemIndex;   // leaf's element index within that container
    uintptr_t          elemStructAddr;  // UScriptStruct* of the element (0 = leaf-container element)
    uintptr_t          elemBaseAddr;    // element's base address (for inner-key rendering)
    const std::string& leafName;    // leaf field name within the element ("" = the element IS the value)
    uintptr_t          leafAddr;    // absolute address of the value
    const std::string& leafType;    // property type name ("IntProperty" / "NameProperty" / ...)
    int32_t            leafSize;    // byte width (0 for variable/string — caller resolves)
    uint8_t            boolMask;    // BoolProperty bit mask (0xFF otherwise)
    // Placed immediately BEFORE `depth` deliberately (audit #5 A4): these leaves are
    // aggregate-initialised positionally, so a site that forgets `kind` binds an
    // `int` to a scoped enum and fails to COMPILE. Appending it instead would
    // value-initialise to the zero enumerator and pass silently, which is how the
    // fix would reproduce the very defect it removes.
    ContainerKind      kind;        // container shape this leaf was reached through
    // audit #5 A12 — same placement rule, same reason, one step further: ONE sub-object
    // rather than four loose scalars. A site that omits it still binds `depth`'s `int` to
    // a class type with no converting constructor (C2440), and a partially-braced `{...}`
    // cannot swallow `depth`. Loose scalars would NOT be safe: `int -> int32_t` is an
    // identity conversion, so supplying three of four and stopping compiles clean, `depth`
    // silently becomes 0, and `deepVisitor`'s `if (lf.depth < 1) return;` then drops EVERY
    // deep group leaf — the feature reporting zero and reading as "this game has none".
    // (⚠ `int -> uintptr_t` narrowing is only a WARNING here — this repo builds /W4 with
    // no /WX — so do not rely on it. Measured, not assumed.)
    Radar::LeafAnchor  anchor;      // container identity at scan time (see Radar.h)
    int                depth;       // recursion depth at which this leaf was emitted (>= 1)
};
using ContainerLeafVisitor = std::function<void(const ContainerLeaf&)>;

struct WalkLeafLimits {
    int maxDepth = 4;     // container nesting levels
    int maxElems = 256;   // per-container element cap
    // DETERMINISTIC per-walk element-visit budget (0 = unlimited). The per-
    // container maxElems bounds flat width, but nested wide containers still
    // blow up combinatorially (256^maxDepth). This caps the TOTAL elements one
    // top-level walk visits, so a single pathological object (e.g. a deep, wide
    // container graph) can't stall a snapshot chunk. Deterministic (walk order
    // is stable) so two captures of the same state truncate identically — SPC
    // diff stays consistent, unlike a wall-clock deadline. (build 1211)
    int64_t maxTotalElems = 0;
    std::function<bool()> aborted;   // cancel + wall-clock backstop poll (may be null)
};

// True for a scalar leaf field (not a container / struct / optional — those are
// recursed). Such fields are emitted as leaves; the visitor filters by type.
static bool IsScalarLeafType(const std::string& t) {
    return t != "ArrayProperty" && t != "SetProperty" && t != "MapProperty"
        && t != "StructProperty" && t != "OptionalProperty" && !t.empty();
}

// Emit `structAddr`'s direct scalar leaves (incl. those nested in direct
// sub-structs), at element path `arrayPath`[`elemIndex`]. Containers inside the
// struct are NOT emitted here — they're walked via GetClassContainers.
// `anchor` has NO default (audit #5 A12): the three callers reach this helper from
// genuinely different places -- inside a container element, recursing through a direct
// sub-struct, and from snapshot capture with no container at all -- so each must SAY
// which it is. It is passed whole rather than as a `slotBase` to recompute from,
// because this helper already threads two interchangeable `uintptr_t` bases (`base`,
// `elemBaseAddr`) and a third would be a coin-flip at every call site.
static void EmitStructDirectLeaves(uintptr_t structAddr, uintptr_t base,
                                   const std::string& arrayPath, int32_t elemIndex,
                                   uintptr_t elemStructAddr, uintptr_t elemBaseAddr,
                                   const std::string& namePrefix, ContainerKind kind,
                                   const Radar::LeafAnchor& anchor,
                                   int depth, int structDepth,
                                   const ContainerLeafVisitor& visit) {
    constexpr int kMaxStructDepth = 4;
    if (structDepth > kMaxStructDepth || !structAddr) return;
    const ClassInfo& ci = Ubel::WalkClassEx(structAddr);
    for (const auto& f : ci.Fields) {
        std::string leafName = namePrefix.empty() ? f.Name : (namePrefix + "." + f.Name);
        if (IsScalarLeafType(f.TypeName)) {
            ContainerLeaf lf{ arrayPath, elemIndex, elemStructAddr, elemBaseAddr,
                              leafName, base + f.Offset, f.TypeName, f.Size, f.boolFieldMask,
                              kind, anchor, depth };
            visit(lf);
        } else if (f.TypeName == "StructProperty" && f.Address) {
            uintptr_t nested = 0;
            if (Macht::ReadSafe(f.Address + DynOff::FSTRUCTPROP_STRUCT, nested) && nested)
                EmitStructDirectLeaves(nested, base + f.Offset, arrayPath, elemIndex,
                                       elemStructAddr, elemBaseAddr, leafName, kind, anchor,
                                       depth, structDepth + 1, visit);
        }
    }
}

// Recursive container walk. `pathPrefix` is the dotted+indexed path accumulated
// so far ("" at the object). Emits container-element leaves (depth >= 1).
static void WalkContainerLeaves(uintptr_t structBase, uintptr_t structAddr,
                                const std::string& pathPrefix, int depth,
                                const WalkLeafLimits& lim, const ContainerLeafVisitor& visit,
                                int64_t* visited = nullptr) {
    if (depth > lim.maxDepth) return;
    if (lim.aborted && lim.aborted()) return;
    // Total-element budget exceeded — bail this (and, via the per-element check
    // below, every in-flight) frame fast. `visited` is shared across the whole
    // top-level walk by passing the same pointer down the recursion.
    if (visited && lim.maxTotalElems > 0 && *visited >= lim.maxTotalElems) return;

    const auto& containers = GetClassContainers(structAddr);
    for (const auto& cfe : containers) {
        if (cfe.stride <= 0) continue;
        uintptr_t fieldAddr = structBase + cfe.offset;

        uintptr_t bufData = 0;
        int32_t   capacity = 0;
        Macht::TSparseArrayView sa{};
        const bool isSparse = (cfe.kind != ContainerKind::Array);
        // audit #5 A12 — the container's identity AT SCAN TIME, so refine can re-read the
        // header instead of trusting each leaf's absolute address. Built HERE because this
        // is the only scope holding the header address and the raw counts.
        //
        // ⚠ `capacity` below is NOT the number to stamp for a sparse container: it is
        // `MaxCapacity` (the backing TArray's Max, used as the iteration bound), while
        // refine re-reads `MaxIndex`. Stamping capacity would make numAtScan exceed nowNum
        // for every TSet/TMap with a spare slot, so the shrink rule would DROP every sparse
        // group candidate on the first Next Scan. The two named factories exist so this
        // cannot be got wrong by reaching for the local that happens to be in hand.
        Radar::LeafAnchor leafAnchor;
        if (cfe.kind == ContainerKind::Array) {
            Macht::TArrayView arr;
            if (!Macht::ReadTArray(fieldAddr, arr)) continue;
            if (arr.Max <= 0 || !arr.Data || arr.Max > 0x100000) continue;
            // Use Count (logical) for capture — slack slots hold stale data.
            bufData = arr.Data; capacity = arr.Count;
            leafAnchor = Radar::MakeArrayLeafAnchor(fieldAddr, arr.Data, arr.Count,
                                                    /*leafDepth=*/depth + 1);
        } else {
            if (!Macht::ReadTSparseArray(fieldAddr, sa)) continue;
            if (sa.MaxCapacity <= 0 || !sa.Data || sa.MaxCapacity > 0x100000) continue;
            bufData = sa.Data; capacity = sa.MaxCapacity;
            leafAnchor = Radar::MakeSparseLeafAnchor(fieldAddr, sa.Data, sa.MaxIndex,
                                                     /*leafDepth=*/depth + 1);
        }
        if (capacity <= 0) continue;

        // Path to THIS container (cfe.name is already a direct-struct-dotted name).
        std::string containerPath = pathPrefix.empty() ? cfe.name : (pathPrefix + "." + cfe.name);

        // Sides to handle: Array/Set element; Map value (+ key). For a struct
        // side recurse + emit its direct leaves; for a leaf side the element IS
        // the value.
        struct Side { uintptr_t structAddr; int32_t regionOff; const char* tag; };
        Side sides[2];
        int nSides = 0;
        if (cfe.kind == ContainerKind::Map) {
            sides[nSides++] = { cfe.valueStruct, cfe.valueOffset, ".Value" };
            if (cfe.keyStruct) sides[nSides++] = { cfe.keyStruct, 0, ".Key" };
        } else {
            sides[nSides++] = { cfe.elemStruct, 0, "" };
        }

        const int32_t probe = capacity < lim.maxElems ? capacity : lim.maxElems;
        for (int32_t e = 0; e < probe; ++e) {
            if ((e & 0x3F) == 0 && lim.aborted && lim.aborted()) return;
            if (isSparse && !Macht::IsSparseIndexAllocated(sa, e)) continue;
            // Count each PROCESSED (allocated) element against the total budget;
            // unallocated sparse slots are skipped cheaply and don't count.
            if (visited) {
                ++*visited;
                if (lim.maxTotalElems > 0 && *visited > lim.maxTotalElems) return;
            }
            uintptr_t slotBase = bufData + static_cast<int64_t>(e) * cfe.stride;

            for (int s = 0; s < nSides; ++s) {
                if (sides[s].structAddr) {
                    // Struct element: emit its direct leaves + recurse its containers.
                    std::string sidePath = containerPath;
                    if (cfe.kind == ContainerKind::Map && nSides > 1) sidePath += sides[s].tag;
                    uintptr_t elemBase = slotBase + sides[s].regionOff;
                    EmitStructDirectLeaves(sides[s].structAddr, elemBase, sidePath, e,
                                           sides[s].structAddr, elemBase, "", cfe.kind,
                                           leafAnchor, depth + 1, 0, visit);
                    WalkContainerLeaves(elemBase, sides[s].structAddr,
                                        sidePath + "[" + std::to_string(e) + "]", depth + 1, lim, visit, visited);
                } else if (s == 0 && cfe.kind != ContainerKind::Map
                           && IsScalarLeafType(cfe.innerType)) {
                    // Leaf-container element (TArray<int> / TSet<int>): the element
                    // IS the value at slotBase, and cfe.innerType is its real type.
                    // (Map scalar sides are handled below — for a Map, cfe.innerType
                    // is the "K -> V" label, not a leaf type, and the two sides live
                    // at distinct offsets, so they need the per-side handling.)
                    const std::string empty;
                    ContainerLeaf lf{ containerPath, e, 0, slotBase, empty,
                                      slotBase, cfe.innerType, 0 /*caller resolves size*/, 0xFF,
                                      cfe.kind, leafAnchor, depth + 1 };
                    visit(lf);
                }
            }

            // Scalar Map sides (P3 — scalar-valued / scalar-keyed maps, e.g.
            // TMap<Name,int>). The struct-side loop above handled struct key/value
            // sides; here we emit the LEAF a scalar key/value forms. The value
            // lives at slotBase+valueOffset, the key at slotBase (pair+0). Paths
            // get ".Value" / ".Key" so the two never collide — matching the
            // top-level static map scan (sf.name = base + ".Value"/".Key") and the
            // struct-both-sides convention. Each side is emitted only when it is
            // scalar (*Struct == 0); a struct side is already covered above, so a
            // map never double-emits a side. leafName "" = the element IS the value
            // (consumers treat the whole side as one scalar container / block).
            if (cfe.kind == ContainerKind::Map) {
                if (cfe.valueStruct == 0 && IsScalarLeafType(cfe.valueLeafType)) {
                    const std::string empty;
                    std::string vpath = containerPath; vpath += ".Value";
                    ContainerLeaf lf{ vpath, e, 0, slotBase, empty,
                                      slotBase + cfe.valueOffset, cfe.valueLeafType,
                                      0 /*caller resolves size*/, 0xFF, cfe.kind,
                                      leafAnchor, depth + 1 };
                    visit(lf);
                }
                if (cfe.keyStruct == 0 && IsScalarLeafType(cfe.keyLeafType)) {
                    const std::string empty;
                    std::string kpath = containerPath; kpath += ".Key";
                    ContainerLeaf lf{ kpath, e, 0, slotBase, empty,
                                      slotBase /*key at pair+0*/, cfe.keyLeafType,
                                      0 /*caller resolves size*/, 0xFF, cfe.kind,
                                      leafAnchor, depth + 1 };
                    visit(lf);
                }
            }
        }
    }
}

// Helper: emit one ContainerMatch given a resolved hit. Reads owner name +
// class name lazily so we only pay that cost when a match is actually found.
static ContainerMatch BuildMatch(uintptr_t obj, int32_t ownerIndex, uintptr_t cls,
                                  const ContainerCacheEntry& cfe,
                                  uintptr_t dataAddr, int32_t count,
                                  int32_t elementIndex, int32_t intraOffset,
                                  const char* note = "") {
    ContainerMatch m;
    m.ownerObj     = obj;
    m.ownerIndex   = ownerIndex;

    uint32_t nameIdx = 0;
    if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx))
        m.ownerName = Serie::GetString(nameIdx);

    uint32_t clsNameIdx = 0;
    if (Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx))
        m.ownerClassName = Serie::GetString(clsNameIdx);

    m.fieldOffset  = cfe.offset;
    m.fieldName    = cfe.name;
    m.fieldType    = (cfe.kind == ContainerKind::Array) ? "ArrayProperty"
                   : (cfe.kind == ContainerKind::Set)   ? "SetProperty"
                                                        : "MapProperty";
    m.innerType    = cfe.innerType;
    m.elementSize  = cfe.stride;
    m.elementIndex = elementIndex;
    m.intraOffset  = intraOffset;
    m.dataAddr     = dataAddr;
    m.count        = count;
    m.note         = note ? note : "";
    return m;
}

std::vector<ContainerMatch> FindInContainers(uintptr_t addr, int32_t maxResults,
                                              ContainerScanStats* stats) {
    std::vector<ContainerMatch> matches;
    if (stats) *stats = {};
    if (!addr || !s_arrayAddr) return matches;
    if (maxResults <= 0) maxResults = 16;

    int32_t count = GetCount();
    if (count <= 0) return matches;
    if (stats) stats->objectsTotal = count;

    LOG_INFO("FindInContainers: scanning %d objects for addr 0x%llX",
             count, static_cast<unsigned long long>(addr));

    // Per-call deadline so a slow / huge-class game doesn't hang the UI.
    // 15s is comfortable on first scan even for 400K-object games (FF7
    // Rebirth) — first scan primes the per-class cache, subsequent scans
    // finish in ~1s. 5s was too tight for first-scan on big games.
    constexpr int kDeadlineMs = 15000;
    auto t0 = std::chrono::steady_clock::now();

    // Parallel GObjects walk. GetClassContainers + Ubel caches are mutex-guarded,
    // so per-thread state is just the match buffer + diagnostic counters; the
    // ascending-tid merge below reproduces the serial ascending-index ordering.
    struct ThreadResult {
        std::vector<ContainerMatch> matches;
        int32_t                     scanned       = 0;
        int32_t                     classesWalked = 0;
    };

    auto scan = ParallelGObjectsScan<ThreadResult>(count,
        [&](ThreadResult& tr, int32_t beginIdx, int32_t endIdx,
            std::atomic<bool>& deadlineHit) {

        // maxResults is a per-thread local cap; the ascending-tid merge truncates
        // to maxResults, reproducing the serial "first N in ascending index order".
        for (int32_t i = beginIdx; i < endIdx && static_cast<int>(tr.matches.size()) < maxResults; ++i) {
        // Chunk-relative stride (see ScanForValue) so the deadline + sibling
        // deadlineHit check fires from this chunk's first iteration.
        if (((i - beginIdx) & 0x3FF) == 0) {
            if (deadlineHit.load(std::memory_order_relaxed)) return;
            // Serial path has no cancel-watcher thread — poll Tot here so the
            // scan still bails promptly; setting deadlineHit also stops siblings
            // on the parallel path.
            if (Tot::Requested()) { deadlineHit.store(true, std::memory_order_relaxed); return; }
            auto dt = std::chrono::duration_cast<std::chrono::milliseconds>(
                          std::chrono::steady_clock::now() - t0).count();
            if (dt > kDeadlineMs) {
                deadlineHit.store(true, std::memory_order_relaxed);
                return;
            }
        }
        tr.scanned++;

        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;

        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        const auto& containers = GetClassContainers(cls);
        if (containers.empty()) continue;
        ++tr.classesWalked;

        for (const auto& cfe : containers) {
            uintptr_t fieldAddr = obj + cfe.offset;

            if (cfe.kind == ContainerKind::Array) {
                Macht::TArrayView arr;
                if (!Macht::ReadTArray(fieldAddr, arr)) continue;
                if (arr.Max <= 0 || !arr.Data) continue;
                // ReadTArray sanity-caps Count at 1M but not Max. A corrupted
                // Max would project a huge buffer span and dilute results.
                // Apply the same cap defensively.
                if (arr.Max > 0x100000) continue;

                // Use Max (allocated capacity) rather than Count so we also
                // catch addresses landing in the array's slack region — when
                // a value comes from a previously-shrunk array element the
                // memory often still holds the last-written game value.
                uintptr_t bufEnd = arr.Data + static_cast<int64_t>(arr.Max) * cfe.stride;
                if (addr < arr.Data || addr >= bufEnd) continue;

                int32_t intraTotal = static_cast<int32_t>(addr - arr.Data);
                int32_t elemIdx    = intraTotal / cfe.stride;
                const char* note   = (elemIdx >= arr.Count) ? "slack" : "";

                auto m = BuildMatch(obj, i, cls, cfe, arr.Data, arr.Count,
                                    elemIdx, intraTotal % cfe.stride, note);
                LOG_INFO("FindInContainers: hit %s.%s[%d]+0x%X (Array%s, owner=0x%llX, %s)",
                         m.ownerName.c_str(), m.fieldName.c_str(),
                         m.elementIndex, m.intraOffset,
                         note[0] ? "/slack" : "",
                         static_cast<unsigned long long>(obj),
                         m.ownerClassName.c_str());
                tr.matches.push_back(std::move(m));
            }
            else { // Set or Map — both use TSparseArray
                Macht::TSparseArrayView sa;
                if (!Macht::ReadTSparseArray(fieldAddr, sa)) continue;
                if (sa.MaxCapacity <= 0 || !sa.Data) continue;
                // Defensive cap — same rationale as Array.Max above.
                if (sa.MaxCapacity > 0x100000) continue;

                // TSparseArray frees slots without overwriting them, so an
                // address landing on a free-list slot may still hold the
                // last-written value. Don't filter — surface them with a
                // "freed" note so the user can judge.
                uintptr_t bufEnd = sa.Data + static_cast<int64_t>(sa.MaxCapacity) * cfe.stride;
                if (addr < sa.Data || addr >= bufEnd) continue;

                int32_t intraTotal = static_cast<int32_t>(addr - sa.Data);
                int32_t sparseIdx  = intraTotal / cfe.stride;
                bool allocated = Macht::IsSparseIndexAllocated(sa, sparseIdx);
                const char* note = allocated ? "" : "freed";

                int32_t logicalCount = sa.MaxIndex - sa.NumFreeIndices;
                auto m = BuildMatch(obj, i, cls, cfe, sa.Data, logicalCount,
                                    sparseIdx, intraTotal % cfe.stride, note);
                LOG_INFO("FindInContainers: hit %s.%s[%d]+0x%X (%s%s, owner=0x%llX, %s)",
                         m.ownerName.c_str(), m.fieldName.c_str(),
                         m.elementIndex, m.intraOffset,
                         m.fieldType.c_str(),
                         note[0] ? "/freed" : "",
                         static_cast<unsigned long long>(obj),
                         m.ownerClassName.c_str());
                tr.matches.push_back(std::move(m));
            }

            if (static_cast<int>(tr.matches.size()) >= maxResults) break;
        }
    }
    });  // ParallelGObjectsScan

    // Fold per-thread stats; match vectors concat in ascending tid order.
    int32_t scanned = 0, classesWalked = 0;
    for (auto& tr : scan.perThread) {
        scanned       += tr.scanned;
        classesWalked += tr.classesWalked;
    }
    matches = ConcatTruncate(scan.perThread, &ThreadResult::matches, maxResults);

    auto dt = std::chrono::duration_cast<std::chrono::milliseconds>(
                  std::chrono::steady_clock::now() - t0).count();
    if (stats) {
        stats->objectsScanned = scanned;
        stats->classesPrimed  = classesWalked;
        stats->durationMs     = static_cast<int64_t>(dt);
        stats->deadlineHit    = scan.deadlineHit;
    }
    LOG_INFO("FindInContainers: found %d matches in %lld ms (scanned %d/%d, %d non-empty classes, %d thread(s)%s)",
             static_cast<int>(matches.size()), static_cast<long long>(dt),
             scanned, count, classesWalked, scan.nthreads, scan.deadlineHit ? ", DEADLINE HIT" : "");
    return matches;
}

// === Deep (recursive) container descent ===
//
// The shallow FindInContainers above bounds-checks `addr` against each
// container buffer at fixed offsets within the object (incl. those nested in
// DIRECT structs — GetClassContainers flattens that). It finds inline values:
// a TArray<int>'s elements, or a field of a struct stored INLINE in a
// TArray<FStruct> buffer (e.g. SaveSlotList[1].GP at SaveSlotList+0x4D8).
//
// It CANNOT find a value in a SEPARATELY-allocated nested container — e.g. a
// TArray<int> whose header is inline in a struct element but whose data buffer
// lives elsewhere on the heap (SaveSlotList[1].MsTuneData.MsTunes[0].
// WeaponTuneList[0].Tunes[N]). The deep matcher recurses into struct elements,
// reading each level's nested container headers and checking `addr` against
// THEIR buffers, building the full chain.

// Try to locate `addr` within the containers of the struct at `structAddr`
// (instance data based at `structBase`), descending into struct elements up to
// maxDepth. On success appends one-or-more hops (this level down) to `chain`
// and returns true. `maxElemProbe` caps elements visited per container.
static bool MatchAddrInStructContainers(
    uintptr_t addr, uintptr_t structBase, uintptr_t structAddr,
    int depth, int maxDepth, int maxElemProbe,
    const std::chrono::steady_clock::time_point& t0, int kDeadlineMs,
    std::atomic<bool>& deadlineHit, std::vector<ContainerHop>& chain)
{
    if (depth > maxDepth) return false;
    if (deadlineHit.load(std::memory_order_relaxed)) return false;
    if (Tot::Requested()) { deadlineHit.store(true, std::memory_order_relaxed); return false; }
    if (std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - t0).count() > kDeadlineMs) {
        deadlineHit.store(true, std::memory_order_relaxed);
        return false;
    }

    const auto& containers = GetClassContainers(structAddr);
    for (const auto& cfe : containers) {
        if (cfe.stride <= 0) continue;
        uintptr_t fieldAddr = structBase + cfe.offset;

        uintptr_t bufData = 0;
        int32_t   capacity = 0;       // Max (array) / MaxCapacity (sparse)
        int32_t   logicalCount = 0;   // Count (array) / allocated count (sparse)
        Macht::TSparseArrayView sa{};
        const bool isSparse = (cfe.kind != ContainerKind::Array);

        if (cfe.kind == ContainerKind::Array) {
            Macht::TArrayView arr;
            if (!Macht::ReadTArray(fieldAddr, arr)) continue;
            if (arr.Max <= 0 || !arr.Data || arr.Max > 0x100000) continue;
            bufData = arr.Data; capacity = arr.Max; logicalCount = arr.Count;
        } else {
            if (!Macht::ReadTSparseArray(fieldAddr, sa)) continue;
            if (sa.MaxCapacity <= 0 || !sa.Data || sa.MaxCapacity > 0x100000) continue;
            bufData = sa.Data; capacity = sa.MaxCapacity;
            logicalCount = sa.MaxIndex - sa.NumFreeIndices;
        }
        uintptr_t bufEnd = bufData + static_cast<int64_t>(capacity) * cfe.stride;

        // Case 1 — addr is INSIDE this buffer: a leaf value, or a field of a
        // struct element stored inline here. This is the terminal hit.
        if (addr >= bufData && addr < bufEnd) {
            int32_t intraTotal  = static_cast<int32_t>(addr - bufData);
            int32_t elemIdx     = intraTotal / cfe.stride;
            int32_t intraInElem = intraTotal % cfe.stride;
            const char* note = "";
            if (cfe.kind == ContainerKind::Array)
                note = (elemIdx >= logicalCount) ? "slack" : "";
            else
                note = Macht::IsSparseIndexAllocated(sa, elemIdx) ? "" : "freed";

            ContainerHop hop;
            hop.fieldOffset  = cfe.offset;
            hop.fieldName    = cfe.name;
            hop.fieldType    = (cfe.kind == ContainerKind::Array) ? "ArrayProperty"
                             : (cfe.kind == ContainerKind::Set)   ? "SetProperty" : "MapProperty";
            hop.innerType    = cfe.innerType;
            hop.elementIndex = elemIdx;
            hop.elementSize  = cfe.stride;
            hop.intraOffset  = intraInElem;
            hop.dataAddr     = bufData;
            hop.mapValueSide = (cfe.kind == ContainerKind::Map) && (intraInElem >= cfe.valueOffset);
            hop.note         = note;
            chain.push_back(std::move(hop));
            return true;
        }

        // Case 2 — addr NOT in this buffer, but elements are structs whose
        // OWN (separately-allocated) nested containers may hold addr. Descend.
        if (depth >= maxDepth) continue;
        const bool hasStructElem = (cfe.kind == ContainerKind::Map)
            ? (cfe.valueStruct != 0 || cfe.keyStruct != 0)
            : (cfe.elemStruct != 0);
        if (!hasStructElem) continue;

        const int32_t probeCount = capacity < maxElemProbe ? capacity : maxElemProbe;
        for (int32_t e = 0; e < probeCount; ++e) {
            if ((e & 0x3F) == 0 && deadlineHit.load(std::memory_order_relaxed)) return false;
            if (isSparse && !Macht::IsSparseIndexAllocated(sa, e)) continue;
            uintptr_t slotBase = bufData + static_cast<int64_t>(e) * cfe.stride;

            // Sides to descend, value-first for maps (the common case).
            struct Side { uintptr_t s; int32_t regionOff; bool isValue; };
            Side sides[2];
            int nSides = 0;
            if (cfe.kind == ContainerKind::Map) {
                if (cfe.valueStruct) sides[nSides++] = { cfe.valueStruct, cfe.valueOffset, true };
                if (cfe.keyStruct)   sides[nSides++] = { cfe.keyStruct, 0, false };
            } else {
                sides[nSides++] = { cfe.elemStruct, 0, false };
            }

            for (int s = 0; s < nSides; ++s) {
                ContainerHop hop;
                hop.fieldOffset  = cfe.offset;
                hop.fieldName    = cfe.name;
                hop.fieldType    = (cfe.kind == ContainerKind::Array) ? "ArrayProperty"
                                 : (cfe.kind == ContainerKind::Set)   ? "SetProperty" : "MapProperty";
                hop.innerType    = cfe.innerType;
                hop.elementIndex = e;
                hop.elementSize  = cfe.stride;
                hop.intraOffset  = 0;   // intermediate hop — leaf intra lives on the deepest hop
                hop.dataAddr     = bufData;
                hop.mapValueSide = sides[s].isValue;
                hop.note         = (isSparse && !Macht::IsSparseIndexAllocated(sa, e)) ? "freed" : "";
                chain.push_back(std::move(hop));
                if (MatchAddrInStructContainers(addr, slotBase + sides[s].regionOff,
                                                sides[s].s, depth + 1, maxDepth, maxElemProbe,
                                                t0, kDeadlineMs, deadlineHit, chain))
                    return true;
                chain.pop_back();   // backtrack — this element didn't lead to addr
            }
        }
    }
    return false;
}

std::vector<ContainerMatch> FindInContainersDeep(uintptr_t addr, int32_t maxResults,
                                                 int32_t maxDepth, int32_t maxElemProbe,
                                                 ContainerScanStats* stats) {
    std::vector<ContainerMatch> matches;
    if (stats) *stats = {};
    if (!addr || !s_arrayAddr) return matches;
    if (maxResults <= 0) maxResults = 8;
    if (maxDepth < 1) maxDepth = 1;
    if (maxElemProbe < 1) maxElemProbe = 256;   // per-container element cap during descent

    int32_t count = GetCount();
    if (count <= 0) return matches;
    if (stats) stats->objectsTotal = count;

    LOG_INFO("FindInContainersDeep: scanning %d objects for addr 0x%llX (maxDepth=%d, maxElemProbe=%d)",
             count, static_cast<unsigned long long>(addr), maxDepth, maxElemProbe);

    constexpr int kDeadlineMs = 15000;
    auto t0 = std::chrono::steady_clock::now();

    struct ThreadResult {
        std::vector<ContainerMatch> matches;
        int32_t                     scanned       = 0;
        int32_t                     classesWalked = 0;
    };

    auto scan = ParallelGObjectsScan<ThreadResult>(count,
        [&](ThreadResult& tr, int32_t beginIdx, int32_t endIdx,
            std::atomic<bool>& deadlineHit) {
        for (int32_t i = beginIdx; i < endIdx && static_cast<int>(tr.matches.size()) < maxResults; ++i) {
            if (((i - beginIdx) & 0x3FF) == 0) {
                if (deadlineHit.load(std::memory_order_relaxed)) return;
                if (Tot::Requested()) { deadlineHit.store(true, std::memory_order_relaxed); return; }
                if (std::chrono::duration_cast<std::chrono::milliseconds>(
                        std::chrono::steady_clock::now() - t0).count() > kDeadlineMs) {
                    deadlineHit.store(true, std::memory_order_relaxed);
                    return;
                }
            }
            tr.scanned++;

            uintptr_t obj = GetByIndex(i);
            if (!obj) continue;
            uintptr_t cls = 0;
            if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

            const auto& containers = GetClassContainers(cls);
            if (containers.empty()) continue;
            ++tr.classesWalked;

            std::vector<ContainerHop> chain;
            if (!MatchAddrInStructContainers(addr, obj, cls, 0, maxDepth, maxElemProbe,
                                             t0, kDeadlineMs, deadlineHit, chain)
                || chain.empty())
                continue;

            ContainerMatch m;
            m.ownerObj   = obj;
            m.ownerIndex = i;
            uint32_t nameIdx = 0;
            if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx))
                m.ownerName = Serie::GetString(nameIdx);
            uint32_t clsNameIdx = 0;
            if (Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx))
                m.ownerClassName = Serie::GetString(clsNameIdx);

            const auto& h0 = chain.front();
            m.fieldOffset  = h0.fieldOffset;
            m.fieldName    = h0.fieldName;
            m.fieldType    = h0.fieldType;
            m.innerType    = h0.innerType;
            m.elementIndex = h0.elementIndex;
            m.elementSize  = h0.elementSize;
            m.intraOffset  = h0.intraOffset;
            m.dataAddr     = h0.dataAddr;
            m.note         = h0.note;
            for (size_t k = 1; k < chain.size(); ++k)
                m.nestedChain.push_back(chain[k]);

            LOG_INFO("FindInContainersDeep: hit %s.%s (owner=0x%llX, %s, %zu hop(s) deep)",
                     m.ownerName.c_str(), m.fieldName.c_str(),
                     static_cast<unsigned long long>(obj), m.ownerClassName.c_str(),
                     m.nestedChain.size() + 1);

            tr.matches.push_back(std::move(m));
            // One match is the answer for a by-address lookup — stop siblings.
            deadlineHit.store(true, std::memory_order_relaxed);
            return;
        }
    });

    int32_t scanned = 0, classesWalked = 0;
    for (auto& tr : scan.perThread) {
        scanned       += tr.scanned;
        classesWalked += tr.classesWalked;
    }
    matches = ConcatTruncate(scan.perThread, &ThreadResult::matches, maxResults);

    auto dt = std::chrono::duration_cast<std::chrono::milliseconds>(
                  std::chrono::steady_clock::now() - t0).count();
    if (stats) {
        stats->objectsScanned = scanned;
        stats->classesPrimed  = classesWalked;
        stats->durationMs     = static_cast<int64_t>(dt);
        // Early-out sets the shared flag too; a real deadline only when nothing matched.
        stats->deadlineHit    = scan.deadlineHit && matches.empty();
    }
    LOG_INFO("FindInContainersDeep: found %d match(es) in %lld ms (scanned %d/%d, %d thread(s)%s)",
             static_cast<int>(matches.size()), static_cast<long long>(dt),
             scanned, count, scan.nthreads,
             (scan.deadlineHit && matches.empty()) ? ", DEADLINE HIT" : "");
    return matches;
}

// === Reverse Reference Search ===
//
// Per-class cache of pointer-shaped fields and Object array fields.
// Built lazily, mirrors the container cache pattern.
//
// Coverage (v2 + v3):
//   - Direct ObjectProperty / ClassProperty / InterfaceProperty
//     (8-byte UObject* read directly at field+0)
//   - Direct WeakObjectProperty / SoftObjectProperty / SoftClassProperty /
//     LazyObjectProperty (FWeakObjectPtr at field+0 → resolved via
//     ResolveWeakObjectPtr; only matches when the ref is currently bound
//     to a live UObject)
//   - DelegateProperty (single FScriptDelegate — FWeakObjectPtr target at
//     field+0; same resolution path)
//   - MulticastInlineDelegateProperty / MulticastDelegateProperty
//     (FMulticastScriptDelegate is just TArray<FScriptDelegate> at
//     field+0; each element's FWeakObjectPtr is the binding target).
//     MulticastSparseDelegateProperty deliberately NOT covered — bindings
//     live in FSparseDelegateStorage rather than at the field.
//   - OptionalProperty<T> for pointer-shaped T — bucketed alongside its
//     bare T because the intrusive layout is identical at field+0.
//   - TArray<UObject*> / TArray<UClass*> (8-byte stride)
//   - TArray<FScriptInterface> (16-byte stride, ptr at elem+0)
//   - TArray<FWeakObjectPtr> / TArray<FSoftObjectPtr> /
//     TArray<FLazyObjectPtr> / TArray<FScriptDelegate> (variable stride,
//     FWeakObjectPtr at elem+0)
//   - TMap<UObject*,V> / TMap<K,UObject*> (TSparseArray walk, allocated
//     slots only — frees aren't real references)
//   - TSet<UObject*> (TSparseArray walk, allocated slots only)
//
// Each entry's `offset` is absolute within the owner UObject (parent
// struct offsets pre-summed for nested fields, depth-capped at 3).

struct DirectPointerEntry {
    int32_t     offset;
    std::string name;
    std::string typeName;     // "ObjectProperty" / "ClassProperty" / "InterfaceProperty"
};

// FWeakObjectPtr-shaped single field: { int32 ObjectIndex, int32 Serial }
// at field+0. Soft/Lazy place this same struct at the head of a richer
// envelope (FSoftObjectPath / FGuid follows) but the resolution path is
// identical — the embedded weak ptr is what reveals the live UObject.
struct WeakLikePointerEntry {
    int32_t     offset;
    std::string name;
    std::string typeName;     // "WeakObjectProperty" / "SoftObjectProperty"
                              // / "SoftClassProperty" / "LazyObjectProperty"
};

struct ObjectArrayEntry {
    int32_t     offset;
    std::string name;
    std::string innerType;    // "ObjectProperty" / "ClassProperty"
};

// TArray<FScriptInterface>: 16-byte elements, UObject* at elem+0.
struct InterfaceArrayEntry {
    int32_t     offset;
    std::string name;
};

// TArray<FWeakObjectPtr>/<FSoftObjectPtr>/<FLazyObjectPtr>: variable
// per-element stride, FWeakObjectPtr at elem+0 in every case.
struct WeakLikeArrayEntry {
    int32_t     offset;
    std::string name;
    std::string innerType;    // Same vocabulary as WeakLikePointerEntry::typeName
    int32_t     elemStride;   // From Ubel::GetArrayInnerElemSize
};

// TMap with at least one Object/Class side. Both flags can be true for a
// TMap<UObject*, UObject*> — scan emits one match per matching side.
struct ObjectMapEntry {
    int32_t     offset;
    std::string name;
    int32_t     pairStride;
    int32_t     valueOffset;  // Within each pair
    bool        keyIsObject;
    bool        valueIsObject;
    std::string keyTypeName;    // "ObjectProperty" / "ClassProperty" (for matched side)
    std::string valueTypeName;
    std::string innerLabel;     // "<keyType> → <valueType>" for UI
};

// TSet with Object/Class element type.
struct ObjectSetEntry {
    int32_t     offset;
    std::string name;
    int32_t     elemStride;
    std::string elemTypeName;   // "ObjectProperty" / "ClassProperty"
};

struct ClassReferenceMeta {
    std::vector<DirectPointerEntry>     directPointers;
    std::vector<WeakLikePointerEntry>   weakLikePointers;
    std::vector<ObjectArrayEntry>       objectArrays;
    std::vector<InterfaceArrayEntry>    interfaceArrays;
    std::vector<WeakLikeArrayEntry>     weakLikeArrays;
    std::vector<ObjectMapEntry>         objectMaps;
    std::vector<ObjectSetEntry>         objectSets;

    bool empty() const {
        return directPointers.empty() && weakLikePointers.empty()
            && objectArrays.empty() && interfaceArrays.empty()
            && weakLikeArrays.empty() && objectMaps.empty()
            && objectSets.empty();
    }
};

static bool IsWeakLikeProp(const std::string& tn) {
    return tn == "WeakObjectProperty" || tn == "SoftObjectProperty"
        || tn == "SoftClassProperty"  || tn == "LazyObjectProperty";
}
static bool IsDirectObjectProp(const std::string& tn) {
    return tn == "ObjectProperty" || tn == "ClassProperty";
}

static std::unordered_map<uintptr_t, ClassReferenceMeta> s_classRefCache;
static std::mutex s_classRefMutex;

// Recursive walker — descends through StructProperty (depth-capped) and
// emits one cache entry for each pointer-shaped field, pointer-array
// field, ObjectMap, or ObjectSet found. Mirrors CollectContainersRecursive.
static void CollectRefMetaRecursive(uintptr_t structAddr,
                                     int32_t baseOffset,
                                     const std::string& namePrefix,
                                     ClassReferenceMeta& out,
                                     int depth)
{
    constexpr int kMaxDepth = 3;
    if (depth > kMaxDepth) return;

    const ClassInfo& ci = Ubel::WalkClassEx(structAddr);
    for (const auto& f : ci.Fields) {
        if (!f.Address) continue;

        std::string fullName = namePrefix.empty()
            ? f.Name
            : (namePrefix + "." + f.Name);
        int32_t absOffset = baseOffset + f.Offset;

        // --- Single pointer fields ---
        if (IsDirectObjectProp(f.TypeName) || f.TypeName == "InterfaceProperty") {
            // All three layouts hold a UObject* at field+0 (FScriptInterface
            // also has ifacePtr at +8, but we ignore that — only objPtr is
            // the resolvable reference).
            out.directPointers.push_back({ absOffset, fullName, f.TypeName });
        }
        else if (IsWeakLikeProp(f.TypeName)) {
            out.weakLikePointers.push_back({ absOffset, fullName, f.TypeName });
        }
        // --- TOptional<T> wrapping a pointer-shaped T ---
        // For pointer-shaped T, FOptionalProperty stores T directly at
        // field+0; "unset" is encoded as null/zero. So the comparison logic
        // is identical to the bare pointer/weak-like field — only the
        // type-name label changes (so the user can see it was reached via
        // an Optional). innerType comes from WalkClassEx.
        else if (f.TypeName == "OptionalProperty"
              && (IsDirectObjectProp(f.innerType)
                  || f.innerType == "InterfaceProperty")) {
            out.directPointers.push_back({ absOffset, fullName, f.TypeName });
        }
        else if (f.TypeName == "OptionalProperty"
              && IsWeakLikeProp(f.innerType)) {
            out.weakLikePointers.push_back({ absOffset, fullName, f.TypeName });
        }
        // --- DelegateProperty (single FScriptDelegate) ---
        // Layout: { FWeakObjectPtr Target(8B), FName FunctionName(8/16B) }.
        // The FWeakObjectPtr at field+0 is the binding's target — same
        // resolution path as WeakObjectProperty, so reuse weakLikePointers.
        // typeName is preserved so the user sees this was reached via a
        // delegate (a "register on click" bind, not a property reference).
        else if (f.TypeName == "DelegateProperty") {
            out.weakLikePointers.push_back({ absOffset, fullName, f.TypeName });
        }
        // --- MulticastInline / MulticastDelegate (single field) ---
        // FMulticastScriptDelegate := TArray<FScriptDelegate> at field+0.
        // Each binding has FWeakObjectPtr at elem+0 — same scan logic as
        // weakLikeArrays, just with a delegate-specific stride. (Sparse
        // multicast deliberately excluded: bindings live in
        // FSparseDelegateStorage, not at the field.)
        else if (f.TypeName == "MulticastInlineDelegateProperty"
              || f.TypeName == "MulticastDelegateProperty") {
            int32_t fnameSize = DynOff::bCasePreservingName ? 0x10 : 0x08;
            int32_t stride    = 8 + fnameSize;
            out.weakLikeArrays.push_back({ absOffset, fullName,
                                            f.TypeName, stride });
        }
        // --- Array of pointer-shaped types ---
        else if (f.TypeName == "ArrayProperty") {
            if (IsDirectObjectProp(f.innerType)) {
                out.objectArrays.push_back({ absOffset, fullName, f.innerType });
            }
            else if (f.innerType == "InterfaceProperty") {
                out.interfaceArrays.push_back({ absOffset, fullName });
            }
            else if (IsWeakLikeProp(f.innerType)) {
                int32_t es = Ubel::GetArrayInnerElemSize(f.Address);
                if (es > 0) {
                    out.weakLikeArrays.push_back({ absOffset, fullName,
                                                    f.innerType, es });
                }
            }
            else if (f.innerType == "DelegateProperty") {
                // TArray<FScriptDelegate> — element layout matches the
                // multicast bindings list. Stride is FName-size dependent.
                int32_t fnameSize = DynOff::bCasePreservingName ? 0x10 : 0x08;
                int32_t stride    = 8 + fnameSize;
                out.weakLikeArrays.push_back({ absOffset, fullName,
                                                f.innerType, stride });
            }
        }
        // --- Map with pointer-shaped key and/or value ---
        else if (f.TypeName == "MapProperty") {
            bool keyIsObj = IsDirectObjectProp(f.keyType);
            bool valIsObj = IsDirectObjectProp(f.valueType);
            if (keyIsObj || valIsObj) {
                Ubel::MapPairLayout layout;
                if (Ubel::GetMapPairLayout(f.Address, layout)
                    && layout.pairStride > 0) {
                    ObjectMapEntry e;
                    e.offset        = absOffset;
                    e.name          = fullName;
                    e.pairStride    = layout.pairStride;
                    e.valueOffset   = layout.valueOffset;
                    e.keyIsObject   = keyIsObj;
                    e.valueIsObject = valIsObj;
                    e.keyTypeName   = f.keyType;
                    e.valueTypeName = f.valueType;
                    e.innerLabel    = f.keyType + " → " + f.valueType;
                    out.objectMaps.push_back(std::move(e));
                }
            }
        }
        // --- Set with pointer-shaped element ---
        else if (f.TypeName == "SetProperty") {
            if (IsDirectObjectProp(f.elemType)) {
                int32_t st = Ubel::GetSetElementStride(f.Address);
                if (st > 0) {
                    out.objectSets.push_back({ absOffset, fullName,
                                                st, f.elemType });
                }
            }
        }
        // --- Recurse into nested structs ---
        else if (f.TypeName == "StructProperty") {
            uintptr_t innerStruct = 0;
            if (Macht::ReadSafe(f.Address + DynOff::FSTRUCTPROP_STRUCT, innerStruct)
                && innerStruct) {
                CollectRefMetaRecursive(innerStruct, absOffset, fullName,
                                         out, depth + 1);
            }
        }
        else if (f.TypeName == "OptionalProperty"
                 && f.innerType == "StructProperty") {
            // TOptional<FStruct>: { T value; uint8 bIsSet; } — value at field+0,
            // so absOffset is unchanged for sub-fields. The bIsSet trailing
            // byte doesn't matter for reverse scan: an unset slot is zero
            // and naturally fails pointer comparisons.
            uintptr_t innerProp = 0;
            uintptr_t innerStruct = 0;
            if (Macht::ReadSafe(f.Address + DynOff::FARRAYPROP_INNER, innerProp)
                && innerProp
                && Macht::ReadSafe(innerProp + DynOff::FSTRUCTPROP_STRUCT, innerStruct)
                && innerStruct) {
                CollectRefMetaRecursive(innerStruct, absOffset, fullName,
                                         out, depth + 1);
            }
        }
    }
}

static const ClassReferenceMeta& GetClassRefMeta(uintptr_t cls) {
    {
        std::lock_guard<std::mutex> lk(s_classRefMutex);
        auto it = s_classRefCache.find(cls);
        if (it != s_classRefCache.end()) return it->second;
    }

    // ⚠ DO NOT MEMOIZE A BUILD THAT CAME FROM AN UNREADABLE CLASS.
    //
    // The emplace below is permanent — nothing ever evicts from this map — so a single
    // transient read fault on `cls` pins an EMPTY result for the rest of the process, and
    // every later scan of that class silently finds nothing. This is reachable today: the
    // callers guard only `!cls` (e.g. Aura.cpp's `if (!ReadSafe(obj + OFF_UOBJECT_CLASS,
    // cls) || !cls) continue;`) and pass class pointers read straight out of live objects.
    //
    // The test is the walk's own verdict, not a new heuristic: WalkClassEx returns
    // `s_emptyClassInfo` — default-constructed, so Address == 0 — both when the address is
    // null and when U4's ShouldPublishClassWalk REFUSES the class (Ubel.cpp:1119), while a
    // good walk sets `info.Address = uclassAddr`. So `Address != cls` means "this class did
    // not walk", and caching anything derived from it would be caching a failure.
    //
    // Costs one WalkClassEx on cache MISSES only, and the builder below calls it anyway —
    // so on a healthy class this is a cache hit, not a second walk.
    //
    // ⚠ This is NOT the A10 fix. A10 is the recycled-UClass* staleness, which needs the
    // return-type refactor across five caches; see its row. This is the separate, present-
    // tense defect found while scoping it.
    static const ClassReferenceMeta s_emptyRefMeta;
    if (Ubel::WalkClassEx(cls).Address != cls) return s_emptyRefMeta;

    ClassReferenceMeta meta;
    CollectRefMetaRecursive(cls, 0, "", meta, 0);

    std::lock_guard<std::mutex> lk(s_classRefMutex);
    auto [ins, _] = s_classRefCache.emplace(cls, std::move(meta));
    return ins->second;
}

static void FillRefMatchOwner(ReferenceMatch& m, uintptr_t obj, int32_t idx, uintptr_t cls) {
    m.ownerObj   = obj;
    m.ownerIndex = idx;
    uint32_t nameIdx = 0;
    if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx))
        m.ownerName = Serie::GetString(nameIdx);
    uint32_t clsNameIdx = 0;
    if (Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx))
        m.ownerClassName = Serie::GetString(clsNameIdx);
}

// Resolve FWeakObjectPtr at `addr` (8 bytes: int32 idx + int32 serial) to
// a live UObject* — same logic Ubel uses, inlined here so the scan loop
// stays self-contained.
static uintptr_t ResolveWeakAt(uintptr_t addr) {
    int32_t objIdx = 0, serial = 0;
    if (!Macht::ReadSafe(addr,     objIdx))  return 0;
    if (!Macht::ReadSafe(addr + 4, serial))  return 0;
    return Ubel::ResolveWeakObjectPtr(objIdx, serial);
}

// ============================================================
// TMap header reader — shared by FindReferencesToUObject (sparse pass)
// and WalkSparseDelegateBindings.
//
// Layout reference (UE 5.0+, verified against Everspace 2 UE 5.4 PDB,
// FSparseDelegateStorage::SparseDelegates):
//
//   TMap (0x50 bytes):
//     +0x00  Elements.Data.AllocatorInstance.Data    (TPair<...>* heap base)
//     +0x08  Elements.Data.ArrayNum                  (int32, total slots incl. freed)
//     +0x0C  Elements.Data.ArrayMax                  (int32)
//     +0x10  Elements.AllocationFlags inline data    (16B = 128 bits inline)
//     +0x20  Elements.AllocationFlags secondary ptr  (heap if NumBits > 128)
//     +0x28  Elements.AllocationFlags.NumBits        (int32)
//     +0x2C  Elements.AllocationFlags.MaxBits        (int32)
//     +0x30  Elements.FirstFreeIndex                 (int32)
//     +0x34  Elements.NumFreeIndices                 (int32)
//     +0x40  Hash secondary ptr
//     +0x48  HashSize                                (int32)
//
//   FSparseDelegateStorage outer TSetElement stride: 0x60
//     +0x00  Key   (UObjectBase*, 8B)
//     +0x08  Value (inner TMap, 0x50B)
//     +0x58  HashNextId / HashIndex (8B)
//
//   FSparseDelegateStorage inner TSetElement stride:
//     bCasePreservingName=false (FName=8): TPair=24, +HashId 8 = 0x20
//     bCasePreservingName=true  (FName=16): TPair=32, +HashId 8 = 0x28
//
//   TSharedPtr<TMulticastScriptDelegate, ThreadSafe> (16B):
//     +0x00  Object* (FMulticastScriptDelegate*)
//     +0x08  SharedReferenceCount*
//
//   FMulticastScriptDelegate (16B):
//     +0x00  TArray<FScriptDelegate> InvocationList { Data, Num, Max }
//
//   FScriptDelegate (16B or 24B for case-preserving FName):
//     +0x00  FWeakObjectPtr Object { int32 Idx, int32 Serial }
//     +0x08  FName FunctionName
// ============================================================

// ResolveTMapBitArrayBase — figure out where the AllocationFlags bits live.
// Inline if MaxBits <= 128; heap (secondaryPtr) otherwise.
static uintptr_t ResolveTMapBitArrayBase(uintptr_t mapAddr) {
    uintptr_t secondaryPtr = 0;
    Macht::ReadSafe(mapAddr + 0x20, secondaryPtr);
    if (secondaryPtr) return secondaryPtr;
    return mapAddr + 0x10;  // inline buffer
}

static bool TMapBitSet(uintptr_t bitArrayBase, int32_t idx) {
    if (idx < 0) return false;
    uint32_t word = 0;
    if (!Macht::ReadSafe(bitArrayBase + (idx >> 5) * 4u, word)) return false;
    return (word >> (idx & 31)) & 1u;
}

// Read a TMap header. Returns false on read failure.
struct TMapHeader {
    uintptr_t arrayData      = 0;
    int32_t   arrayNum       = 0;   // total slots (includes freed)
    int32_t   numFreeIndices = 0;
    uintptr_t bitArrayBase   = 0;
};

static bool ReadTMapHeader(uintptr_t mapAddr, TMapHeader& out) {
    if (!Macht::ReadSafe(mapAddr + 0x00, out.arrayData))      return false;
    if (!Macht::ReadSafe(mapAddr + 0x08, out.arrayNum))       return false;
    if (!Macht::ReadSafe(mapAddr + 0x34, out.numFreeIndices)) return false;
    out.bitArrayBase = ResolveTMapBitArrayBase(mapAddr);
    // Sanity: ArrayNum bounded; some games hit 6-7 figures of total entries
    // when many UObjects use sparse delegates, but never beyond 1M.
    if (out.arrayNum < 0 || out.arrayNum > 0x100000) return false;
    return true;
}

std::vector<ReferenceMatch> FindReferencesToUObject(uintptr_t target,
                                                     int32_t maxResults,
                                                     ContainerScanStats* stats)
{
    std::vector<ReferenceMatch> matches;
    if (stats) *stats = {};
    if (!target || !s_arrayAddr) return matches;
    if (maxResults <= 0) maxResults = 32;

    int32_t count = GetCount();
    if (count <= 0) return matches;
    if (stats) stats->objectsTotal = count;

    LOG_INFO("FindReferencesToUObject: scanning %d objects for ref to 0x%llX",
             count, static_cast<unsigned long long>(target));

    // Reference search is more expensive than container scan (each UObject
    // checked has up to N pointer fields + array elements). Bump deadline
    // to 30s so first-pass cache prime can complete on huge games.
    constexpr int kDeadlineMs = 30000;
    auto t0 = std::chrono::steady_clock::now();

    // Parallel GObjects walk. GetClassRefMeta + Ubel caches are mutex-guarded,
    // so per-thread state is just the match buffer + diagnostic counters; the
    // ascending-tid merge reproduces serial ascending-index ordering. (The
    // sparse-delegate pass below the loop is a single global-TMap walk and stays
    // serial — it runs once, after the merge.)
    struct ThreadResult {
        std::vector<ReferenceMatch> matches;
        int32_t                     scanned       = 0;
        int32_t                     classesPrimed = 0;
    };

    auto scan = ParallelGObjectsScan<ThreadResult>(count,
        [&](ThreadResult& tr, int32_t beginIdx, int32_t endIdx,
            std::atomic<bool>& deadlineHit) {

        // maxResults is a per-thread local cap; the ascending-tid merge truncates
        // to maxResults (== serial "first N in ascending index order").
        auto pushMatch = [&](ReferenceMatch&& m) -> bool {
            tr.matches.push_back(std::move(m));
            return static_cast<int>(tr.matches.size()) >= maxResults;
        };

        for (int32_t i = beginIdx; i < endIdx && static_cast<int>(tr.matches.size()) < maxResults; ++i) {
        // Chunk-relative stride (see ScanForValue) so the deadline + sibling
        // deadlineHit check fires from this chunk's first iteration.
        if (((i - beginIdx) & 0x3FF) == 0) {
            if (deadlineHit.load(std::memory_order_relaxed)) return;
            // Serial path has no cancel-watcher thread — poll Tot here so the
            // scan still bails promptly; setting deadlineHit also stops siblings
            // on the parallel path.
            if (Tot::Requested()) { deadlineHit.store(true, std::memory_order_relaxed); return; }
            auto dt = std::chrono::duration_cast<std::chrono::milliseconds>(
                          std::chrono::steady_clock::now() - t0).count();
            if (dt > kDeadlineMs) {
                deadlineHit.store(true, std::memory_order_relaxed);
                return;
            }
        }
        tr.scanned++;

        uintptr_t obj = GetByIndex(i);
        if (!obj || obj == target) continue;  // Don't report self-reference

        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        const auto& meta = GetClassRefMeta(cls);
        if (meta.empty()) continue;
        ++tr.classesPrimed;

        bool hitMaxThisObj = false;

        // --- Direct ObjectProperty / ClassProperty / InterfaceProperty ---
        for (const auto& pfe : meta.directPointers) {
            uintptr_t ptr = 0;
            if (!Macht::ReadSafe(obj + pfe.offset, ptr)) continue;
            if (ptr != target) continue;

            ReferenceMatch m;
            FillRefMatchOwner(m, obj, i, cls);
            m.fieldOffset  = pfe.offset;
            m.fieldName    = pfe.name;
            m.fieldType    = pfe.typeName;
            m.elementIndex = -1;

            LOG_INFO("FindReferencesToUObject: hit %s.%s (%s, owner=0x%llX, %s)",
                     m.ownerName.c_str(), m.fieldName.c_str(),
                     pfe.typeName.c_str(),
                     static_cast<unsigned long long>(obj),
                     m.ownerClassName.c_str());
            if (pushMatch(std::move(m))) { hitMaxThisObj = true; break; }
        }
        if (hitMaxThisObj) break;

        // --- Weak/Soft/Lazy single fields (FWeakObjectPtr at field+0) ---
        for (const auto& wpe : meta.weakLikePointers) {
            uintptr_t resolved = ResolveWeakAt(obj + wpe.offset);
            if (resolved != target) continue;

            ReferenceMatch m;
            FillRefMatchOwner(m, obj, i, cls);
            m.fieldOffset  = wpe.offset;
            m.fieldName    = wpe.name;
            m.fieldType    = wpe.typeName;
            m.elementIndex = -1;

            LOG_INFO("FindReferencesToUObject: hit %s.%s (%s, owner=0x%llX, %s)",
                     m.ownerName.c_str(), m.fieldName.c_str(),
                     wpe.typeName.c_str(),
                     static_cast<unsigned long long>(obj),
                     m.ownerClassName.c_str());
            if (pushMatch(std::move(m))) { hitMaxThisObj = true; break; }
        }
        if (hitMaxThisObj) break;

        // --- TArray<UObject*> / TArray<UClass*> (8-byte stride) ---
        for (const auto& oae : meta.objectArrays) {
            Macht::TArrayView arr;
            if (!Macht::ReadTArray(obj + oae.offset, arr)) continue;
            if (arr.Count <= 0 || !arr.Data) continue;

            // Bulk-read the TArray's data buffer once and scan in-memory.
            constexpr int32_t kElemBytes = 8;
            std::vector<uintptr_t> buf(arr.Count, 0);
            if (!Macht::ReadBytesSafe(arr.Data, buf.data(),
                                       arr.Count * kElemBytes))
                continue;

            for (int32_t e = 0; e < arr.Count; ++e) {
                if (buf[e] != target) continue;

                ReferenceMatch m;
                FillRefMatchOwner(m, obj, i, cls);
                m.fieldOffset  = oae.offset;
                m.fieldName    = oae.name;
                m.fieldType    = "ArrayProperty";
                m.innerType    = oae.innerType;
                m.elementIndex = e;

                LOG_INFO("FindReferencesToUObject: hit %s.%s[%d] (Array<%s>, owner=0x%llX, %s)",
                         m.ownerName.c_str(), m.fieldName.c_str(), e,
                         oae.innerType.c_str(),
                         static_cast<unsigned long long>(obj),
                         m.ownerClassName.c_str());
                if (pushMatch(std::move(m))) { hitMaxThisObj = true; break; }
            }
            if (hitMaxThisObj) break;
        }
        if (hitMaxThisObj) break;

        // --- TArray<FScriptInterface> (16-byte stride, ptr at elem+0) ---
        for (const auto& iae : meta.interfaceArrays) {
            Macht::TArrayView arr;
            if (!Macht::ReadTArray(obj + iae.offset, arr)) continue;
            if (arr.Count <= 0 || !arr.Data) continue;

            constexpr int32_t kElemBytes = 16;
            for (int32_t e = 0; e < arr.Count; ++e) {
                uintptr_t ptr = 0;
                if (!Macht::ReadSafe(arr.Data + static_cast<int64_t>(e) * kElemBytes, ptr))
                    continue;
                if (ptr != target) continue;

                ReferenceMatch m;
                FillRefMatchOwner(m, obj, i, cls);
                m.fieldOffset  = iae.offset;
                m.fieldName    = iae.name;
                m.fieldType    = "ArrayProperty";
                m.innerType    = "InterfaceProperty";
                m.elementIndex = e;

                LOG_INFO("FindReferencesToUObject: hit %s.%s[%d] (Array<InterfaceProperty>, owner=0x%llX, %s)",
                         m.ownerName.c_str(), m.fieldName.c_str(), e,
                         static_cast<unsigned long long>(obj),
                         m.ownerClassName.c_str());
                if (pushMatch(std::move(m))) { hitMaxThisObj = true; break; }
            }
            if (hitMaxThisObj) break;
        }
        if (hitMaxThisObj) break;

        // --- TArray<FWeak/Soft/Lazy ObjectPtr> (FWeakObjectPtr at elem+0) ---
        for (const auto& wae : meta.weakLikeArrays) {
            Macht::TArrayView arr;
            if (!Macht::ReadTArray(obj + wae.offset, arr)) continue;
            if (arr.Count <= 0 || !arr.Data || wae.elemStride <= 0) continue;

            for (int32_t e = 0; e < arr.Count; ++e) {
                uintptr_t resolved = ResolveWeakAt(
                    arr.Data + static_cast<int64_t>(e) * wae.elemStride);
                if (resolved != target) continue;

                ReferenceMatch m;
                FillRefMatchOwner(m, obj, i, cls);
                m.fieldOffset  = wae.offset;
                m.fieldName    = wae.name;
                m.fieldType    = "ArrayProperty";
                m.innerType    = wae.innerType;
                m.elementIndex = e;

                LOG_INFO("FindReferencesToUObject: hit %s.%s[%d] (Array<%s>, owner=0x%llX, %s)",
                         m.ownerName.c_str(), m.fieldName.c_str(), e,
                         wae.innerType.c_str(),
                         static_cast<unsigned long long>(obj),
                         m.ownerClassName.c_str());
                if (pushMatch(std::move(m))) { hitMaxThisObj = true; break; }
            }
            if (hitMaxThisObj) break;
        }
        if (hitMaxThisObj) break;

        // --- TMap<UObject*, V> / TMap<K, UObject*> (allocated slots only) ---
        for (const auto& ome : meta.objectMaps) {
            Macht::TSparseArrayView sa;
            if (!Macht::ReadTSparseArray(obj + ome.offset, sa)) continue;
            if (sa.MaxIndex <= 0 || !sa.Data || ome.pairStride <= 0) continue;

            for (int32_t e = 0; e < sa.MaxIndex; ++e) {
                if (!Macht::IsSparseIndexAllocated(sa, e)) continue;
                uintptr_t pair = sa.Data + static_cast<int64_t>(e) * ome.pairStride;

                if (ome.keyIsObject) {
                    uintptr_t kp = 0;
                    if (Macht::ReadSafe(pair, kp) && kp == target) {
                        ReferenceMatch m;
                        FillRefMatchOwner(m, obj, i, cls);
                        m.fieldOffset  = ome.offset;
                        m.fieldName    = ome.name + ".Key";
                        m.fieldType    = "MapProperty";
                        m.innerType    = ome.innerLabel;
                        m.elementIndex = e;
                        LOG_INFO("FindReferencesToUObject: hit %s.%s.Key[%d] (Map<%s>, owner=0x%llX, %s)",
                                 m.ownerName.c_str(), ome.name.c_str(), e,
                                 ome.innerLabel.c_str(),
                                 static_cast<unsigned long long>(obj),
                                 m.ownerClassName.c_str());
                        if (pushMatch(std::move(m))) { hitMaxThisObj = true; break; }
                    }
                }
                if (ome.valueIsObject) {
                    uintptr_t vp = 0;
                    if (Macht::ReadSafe(pair + ome.valueOffset, vp) && vp == target) {
                        ReferenceMatch m;
                        FillRefMatchOwner(m, obj, i, cls);
                        m.fieldOffset  = ome.offset;
                        m.fieldName    = ome.name + ".Value";
                        m.fieldType    = "MapProperty";
                        m.innerType    = ome.innerLabel;
                        m.elementIndex = e;
                        LOG_INFO("FindReferencesToUObject: hit %s.%s.Value[%d] (Map<%s>, owner=0x%llX, %s)",
                                 m.ownerName.c_str(), ome.name.c_str(), e,
                                 ome.innerLabel.c_str(),
                                 static_cast<unsigned long long>(obj),
                                 m.ownerClassName.c_str());
                        if (pushMatch(std::move(m))) { hitMaxThisObj = true; break; }
                    }
                }
            }
            if (hitMaxThisObj) break;
        }
        if (hitMaxThisObj) break;

        // --- TSet<UObject*> (allocated slots only) ---
        for (const auto& ose : meta.objectSets) {
            Macht::TSparseArrayView sa;
            if (!Macht::ReadTSparseArray(obj + ose.offset, sa)) continue;
            if (sa.MaxIndex <= 0 || !sa.Data || ose.elemStride <= 0) continue;

            for (int32_t e = 0; e < sa.MaxIndex; ++e) {
                if (!Macht::IsSparseIndexAllocated(sa, e)) continue;
                uintptr_t elem = sa.Data + static_cast<int64_t>(e) * ose.elemStride;
                uintptr_t ptr = 0;
                if (!Macht::ReadSafe(elem, ptr)) continue;
                if (ptr != target) continue;

                ReferenceMatch m;
                FillRefMatchOwner(m, obj, i, cls);
                m.fieldOffset  = ose.offset;
                m.fieldName    = ose.name;
                m.fieldType    = "SetProperty";
                m.innerType    = ose.elemTypeName;
                m.elementIndex = e;

                LOG_INFO("FindReferencesToUObject: hit %s.%s[%d] (Set<%s>, owner=0x%llX, %s)",
                         m.ownerName.c_str(), m.fieldName.c_str(), e,
                         ose.elemTypeName.c_str(),
                         static_cast<unsigned long long>(obj),
                         m.ownerClassName.c_str());
                if (pushMatch(std::move(m))) { hitMaxThisObj = true; break; }
            }
            if (hitMaxThisObj) break;
        }
        if (hitMaxThisObj) break;
    }
    });  // ParallelGObjectsScan

    // Fold per-thread stats; match vectors concat in ascending tid order.
    int32_t scanned = 0, classesPrimed = 0;
    for (auto& tr : scan.perThread) {
        scanned       += tr.scanned;
        classesPrimed += tr.classesPrimed;
    }
    matches = ConcatTruncate(scan.perThread, &ThreadResult::matches, maxResults);

    // Carry the parallel phase's deadline state into the serial sparse pass as a
    // plain bool (the atomic lived inside ParallelGObjectsScan). The sparse pass
    // may set it if IT runs long; the epilogue reports the final value.
    bool deadlineHit = scan.deadlineHit;

    // Serial pushMatch for the single-pass sparse-delegate walk below (appends
    // to the already-merged `matches`).
    auto pushMatch = [&](ReferenceMatch&& m) -> bool {
        matches.push_back(std::move(m));
        return static_cast<int>(matches.size()) >= maxResults;
    };

    // ── MulticastSparseDelegateProperty pass (UE 5.0+) ────────────────
    // Field-level scan above can't see sparse delegates because their
    // bindings live in a CoreUObject-global TMap, not in the owning
    // UObject's memory. Walk that TMap once and check every binding's
    // FWeakObjectPtr against `target`. Skipped silently when AOB scan
    // failed or UE version is unsupported.
    if (static_cast<int>(matches.size()) < maxResults && !deadlineHit &&
        ::g_cachedUEVersion >= 500)
    {
        uintptr_t storage = Genau::FindSparseDelegateStorage();
        if (storage) {
            TMapHeader outerHdr{};
            if (ReadTMapHeader(storage, outerHdr) && outerHdr.arrayData &&
                outerHdr.arrayNum > 0)
            {
                constexpr int32_t kOuterStride = 0x60;
                constexpr int32_t kOuterValueOffset = 0x08;
                int fnameSize = DynOff::bCasePreservingName ? 0x10 : 0x08;
                int32_t innerStride = (fnameSize == 0x10 ? 0x28 : 0x20);
                int32_t scriptDelegateSize = 8 + fnameSize;

                int32_t outerVisited = 0;
                bool sparseAbort = false;
                for (int32_t oi = 0; oi < outerHdr.arrayNum && !sparseAbort; ++oi) {
                    if (!TMapBitSet(outerHdr.bitArrayBase, oi)) continue;
                    if ((++outerVisited & 0xFF) == 0) {
                        auto dt = std::chrono::duration_cast<std::chrono::milliseconds>(
                                      std::chrono::steady_clock::now() - t0).count();
                        if (dt > kDeadlineMs) { deadlineHit = true; break; }
                    }

                    uintptr_t outerSlot = outerHdr.arrayData +
                        static_cast<uintptr_t>(oi) * kOuterStride;
                    uintptr_t ownerObj = 0;
                    if (!Macht::ReadSafe(outerSlot, ownerObj) || !ownerObj) continue;
                    if (ownerObj == target) continue;  // self-reference suppressed

                    uintptr_t innerMapAddr = outerSlot + kOuterValueOffset;
                    TMapHeader innerHdr{};
                    if (!ReadTMapHeader(innerMapAddr, innerHdr)) continue;
                    if (!innerHdr.arrayData || innerHdr.arrayNum == 0) continue;

                    for (int32_t ii = 0; ii < innerHdr.arrayNum; ++ii) {
                        if (!TMapBitSet(innerHdr.bitArrayBase, ii)) continue;
                        uintptr_t innerSlot = innerHdr.arrayData +
                            static_cast<uintptr_t>(ii) * innerStride;

                        int32_t funcComp = 0;
                        if (!Macht::ReadSafe(innerSlot, funcComp)) continue;
                        std::string fieldFName = Serie::GetString(funcComp);

                        // TPair: FName at +0, TSharedPtr at +fnameSize
                        uintptr_t mcdAddr = 0;
                        if (!Macht::ReadSafe(innerSlot + fnameSize, mcdAddr) || !mcdAddr)
                            continue;

                        uintptr_t invData = 0;
                        int32_t   invNum  = 0;
                        Macht::ReadSafe(mcdAddr + 0x00, invData);
                        Macht::ReadSafe(mcdAddr + 0x08, invNum);
                        if (invNum < 0 || invNum > 4096 || !invData) continue;

                        for (int32_t bi = 0; bi < invNum; ++bi) {
                            uintptr_t bindAddr = invData +
                                static_cast<uintptr_t>(bi) * scriptDelegateSize;
                            uintptr_t resolved = ResolveWeakAt(bindAddr);
                            if (resolved != target) continue;

                            // Match. Resolve owner metadata.
                            int32_t ownerIdx = -1;
                            Macht::ReadSafe(ownerObj + Grimoire::OFF_UOBJECT_INDEX,
                                            ownerIdx);
                            uintptr_t ownerCls = 0;
                            Macht::ReadSafe(ownerObj + Grimoire::OFF_UOBJECT_CLASS,
                                            ownerCls);

                            ReferenceMatch m;
                            FillRefMatchOwner(m, ownerObj, ownerIdx, ownerCls);
                            m.fieldOffset  = 0;  // unknown — bindings live outside owner
                            m.fieldName    = fieldFName;
                            m.fieldType    = "MulticastSparseDelegateProperty";
                            m.elementIndex = bi;

                            LOG_INFO("FindReferencesToUObject: hit %s.%s[%d] "
                                     "(MulticastSparseDelegateProperty, owner=0x%llX, %s)",
                                     m.ownerName.c_str(), m.fieldName.c_str(), bi,
                                     static_cast<unsigned long long>(ownerObj),
                                     m.ownerClassName.c_str());
                            if (pushMatch(std::move(m))) { sparseAbort = true; break; }
                        }
                        if (sparseAbort) break;
                    }
                }
            }
        }
    }

    auto dt = std::chrono::duration_cast<std::chrono::milliseconds>(
                  std::chrono::steady_clock::now() - t0).count();
    if (stats) {
        stats->objectsScanned = scanned;
        stats->classesPrimed  = classesPrimed;
        stats->durationMs     = static_cast<int64_t>(dt);
        stats->deadlineHit    = deadlineHit;
    }
    LOG_INFO("FindReferencesToUObject: found %d matches in %lld ms (scanned %d/%d, %d classes with refs, %d thread(s)%s)",
             static_cast<int>(matches.size()), static_cast<long long>(dt),
             scanned, count, classesPrimed, scan.nthreads, deadlineHit ? ", DEADLINE HIT" : "");
    return matches;
}

// ============================================================
// Forward Object-Graph Path Search ("Locate in GWorld")
//
// The inverse of FindReferencesToUObject. EnumerateOutgoingObjectPtrs reuses
// the per-class reference-metadata cache (GetClassRefMeta) — the same metadata
// the reverse search walks — but ENQUEUES each child object instead of
// comparing it against a target. The BFS itself lives in GraphPath.h (pure,
// unit-tested with a mock graph); this is the live adjacency adapter + the
// GWorld-agnostic entry point.
// ============================================================

// Enumerate every outgoing object-pointer edge of `obj`. For each child,
// invokes emit(child, fieldOffset, fieldName, fieldType, innerType, elementIndex,
//              elemStride, elemValueOffset)
// and returns immediately once emit returns true (target found / cap hit).
// elemStride/elemValueOffset describe a container element's geometry (stride in
// the Data buffer + the followed pointer's within-element offset) so a Map/Set
// element hop can be split into container+element CE derefs; 0 for direct edges.
// Mirrors FindReferencesToUObject's per-object read patterns.
template <typename EmitFn>
static void EnumerateOutgoingObjectPtrs(uintptr_t obj, EmitFn&& emit, bool deep = false) {
    uintptr_t cls = 0;
    if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) return;
    const auto& meta = GetClassRefMeta(cls);
    // NOTE: don't early-return on meta.empty() when deep — a class with no direct
    // pointer fields can still own a TArray<FStruct> whose elements hold pointers
    // (the deep pass below walks GetClassContainers, a separate cache).
    if (meta.empty() && !deep) return;

    static const std::string kEmpty;
    static const std::string kArrayProp  = "ArrayProperty";
    static const std::string kMapProp    = "MapProperty";
    static const std::string kSetProp    = "SetProperty";
    static const std::string kIfaceProp  = "InterfaceProperty";
    static const std::string kStructProp = "StructProperty";

    // --- Direct ObjectProperty / ClassProperty / InterfaceProperty ---
    for (const auto& pfe : meta.directPointers) {
        uintptr_t ptr = 0;
        if (!Macht::ReadSafe(obj + pfe.offset, ptr) || !ptr) continue;
        if (emit(ptr, pfe.offset, pfe.name, pfe.typeName, kEmpty, -1, 0, 0)) return;
    }
    // --- Weak/Soft/Lazy single fields (FWeakObjectPtr at field+0) ---
    for (const auto& wpe : meta.weakLikePointers) {
        uintptr_t r = ResolveWeakAt(obj + wpe.offset);
        if (!r) continue;
        if (emit(r, wpe.offset, wpe.name, wpe.typeName, kEmpty, -1, 0, 0)) return;
    }
    // --- TArray<UObject*> / TArray<UClass*> (8-byte stride) ---
    for (const auto& oae : meta.objectArrays) {
        Macht::TArrayView arr;
        if (!Macht::ReadTArray(obj + oae.offset, arr) || arr.Count <= 0 || !arr.Data) continue;
        std::vector<uintptr_t> buf(static_cast<size_t>(arr.Count), 0);
        if (!Macht::ReadBytesSafe(arr.Data, buf.data(), static_cast<size_t>(arr.Count) * 8)) continue;
        for (int32_t e = 0; e < arr.Count; ++e) {
            if (!buf[e]) continue;
            // Object/class arrays use the implicit 8-byte pointer stride — the UI
            // hardcodes it, so stride/valueOffset are not carried (0,0).
            if (emit(buf[e], oae.offset, oae.name, kArrayProp, oae.innerType, e, 0, 0)) return;
        }
    }
    // --- TArray<FScriptInterface> (16-byte stride, ptr at elem+0) ---
    for (const auto& iae : meta.interfaceArrays) {
        Macht::TArrayView arr;
        if (!Macht::ReadTArray(obj + iae.offset, arr) || arr.Count <= 0 || !arr.Data) continue;
        for (int32_t e = 0; e < arr.Count; ++e) {
            uintptr_t ptr = 0;
            if (!Macht::ReadSafe(arr.Data + static_cast<int64_t>(e) * 16, ptr) || !ptr) continue;
            // FScriptInterface is a 16-byte slot with the object pointer at elem+0,
            // so the hop IS splittable: stride 16, value offset 0.
            if (emit(ptr, iae.offset, iae.name, kArrayProp, kIfaceProp, e, 16, 0)) return;
        }
    }
    // --- TArray<FWeak/Soft/Lazy ObjectPtr> (FWeakObjectPtr at elem+0) ---
    for (const auto& wae : meta.weakLikeArrays) {
        Macht::TArrayView arr;
        if (!Macht::ReadTArray(obj + wae.offset, arr) || arr.Count <= 0 || !arr.Data || wae.elemStride <= 0) continue;
        for (int32_t e = 0; e < arr.Count; ++e) {
            uintptr_t r = ResolveWeakAt(arr.Data + static_cast<int64_t>(e) * wae.elemStride);
            if (!r) continue;
            if (emit(r, wae.offset, wae.name, kArrayProp, wae.innerType, e, 0, 0)) return;
        }
    }
    // --- TMap<UObject*, V> / TMap<K, UObject*> (allocated slots only) ---
    for (const auto& ome : meta.objectMaps) {
        Macht::TSparseArrayView sa;
        if (!Macht::ReadTSparseArray(obj + ome.offset, sa) || sa.MaxIndex <= 0 || !sa.Data || ome.pairStride <= 0) continue;
        for (int32_t e = 0; e < sa.MaxIndex; ++e) {
            if (!Macht::IsSparseIndexAllocated(sa, e)) continue;
            uintptr_t pair = sa.Data + static_cast<int64_t>(e) * ome.pairStride;
            if (ome.keyIsObject) {
                uintptr_t kp = 0;
                if (Macht::ReadSafe(pair, kp) && kp)
                    if (emit(kp, ome.offset, ome.name + ".Key", kMapProp, ome.innerLabel, e, ome.pairStride, 0)) return;
            }
            if (ome.valueIsObject) {
                uintptr_t vp = 0;
                if (Macht::ReadSafe(pair + ome.valueOffset, vp) && vp)
                    if (emit(vp, ome.offset, ome.name + ".Value", kMapProp, ome.innerLabel, e, ome.pairStride, ome.valueOffset)) return;
            }
        }
    }
    // --- TSet<UObject*> (allocated slots only) ---
    for (const auto& ose : meta.objectSets) {
        Macht::TSparseArrayView sa;
        if (!Macht::ReadTSparseArray(obj + ose.offset, sa) || sa.MaxIndex <= 0 || !sa.Data || ose.elemStride <= 0) continue;
        for (int32_t e = 0; e < sa.MaxIndex; ++e) {
            if (!Macht::IsSparseIndexAllocated(sa, e)) continue;
            uintptr_t ptr = 0;
            if (!Macht::ReadSafe(sa.Data + static_cast<int64_t>(e) * ose.elemStride, ptr) || !ptr) continue;
            if (emit(ptr, ose.offset, ose.name, kSetProp, ose.elemTypeName, e, ose.elemStride, 0)) return;
        }
    }

    if (!deep) return;

    // --- Deep (opt-in): object pointers inside ONE struct-element container
    //     level — TArray<FStruct> / TSet<FStruct> / TMap<*,FStruct> whose element
    //     (or map value/key) struct holds a UObject* (direct or weak/soft/lazy,
    //     incl. nested in an inline sub-struct — GetClassRefMeta flattens that to
    //     a fixed element-relative offset). The graph analogue of Value Search
    //     "Deep". Each pointer is one CE-splittable hop: deref the container Data
    //     (at field+0), then deref the element pointer at
    //     index*stride + within-element-offset (carried as elemStride /
    //     elemValueOffset, exactly like a Map value). Object containers nested
    //     INSIDE the element struct (two container levels) are deliberately NOT
    //     followed — they aren't a single splittable hop. Bounded by a per-
    //     container element cap; the BFS deadline / visited cap bound the rest. ---
    constexpr int32_t kDeepElemCap = 256;
    for (const auto& cfe : GetClassContainers(cls)) {
        if (cfe.stride <= 0) continue;

        // The struct sides we can descend into for pointers, with the byte offset
        // of that side within the element/pair (0 for array/set element & map key;
        // valueOffset for the map value). `suffix` keeps the Map ".Key"/".Value"
        // naming convention the top-level emits use (and that PathStepToBreadcrumbs'
        // StripContainerKeyValueSuffix / kvHint split relies on) — the inner
        // pointer field name is NOT appended, so the container crumb names the real
        // field and back-nav re-hydration matches a live parent walk.
        struct Side { uintptr_t structAddr; int32_t within; const char* suffix; };
        Side sides[2]; int nSides = 0;
        if (cfe.kind == ContainerKind::Map) {
            if (cfe.valueStruct) sides[nSides++] = { cfe.valueStruct, cfe.valueOffset, ".Value" };
            if (cfe.keyStruct)   sides[nSides++] = { cfe.keyStruct,   0,               ".Key"   };
        } else if (cfe.elemStruct) {
            sides[nSides++] = { cfe.elemStruct, 0, "" };
        }
        if (nSides == 0) continue;

        uintptr_t bufData = 0; int32_t maxIdx = 0;
        const bool isSparse = (cfe.kind != ContainerKind::Array);
        Macht::TSparseArrayView sa{};
        if (cfe.kind == ContainerKind::Array) {
            Macht::TArrayView arr;
            if (!Macht::ReadTArray(obj + cfe.offset, arr) || arr.Count <= 0 || !arr.Data) continue;
            bufData = arr.Data; maxIdx = arr.Count;
        } else {
            if (!Macht::ReadTSparseArray(obj + cfe.offset, sa) || sa.MaxIndex <= 0 || !sa.Data) continue;
            bufData = sa.Data; maxIdx = sa.MaxIndex;
        }
        const int32_t probe = maxIdx < kDeepElemCap ? maxIdx : kDeepElemCap;
        const std::string& fieldType = (cfe.kind == ContainerKind::Array) ? kArrayProp
                                     : (cfe.kind == ContainerKind::Set)   ? kSetProp : kMapProp;

        for (int32_t e = 0; e < probe; ++e) {
            if (isSparse && !Macht::IsSparseIndexAllocated(sa, e)) continue;
            uintptr_t slotBase = bufData + static_cast<int64_t>(e) * cfe.stride;
            for (int s = 0; s < nSides; ++s) {
                uintptr_t structBase = slotBase + sides[s].within;
                const auto& emeta = GetClassRefMeta(sides[s].structAddr);
                // Direct Object/Class/Interface pointers in the element struct.
                for (const auto& pfe : emeta.directPointers) {
                    uintptr_t ptr = 0;
                    if (!Macht::ReadSafe(structBase + pfe.offset, ptr) || !ptr) continue;
                    if (emit(ptr, cfe.offset, cfe.name + sides[s].suffix,
                             fieldType, kStructProp, e, cfe.stride, sides[s].within + pfe.offset))
                        return;
                }
                // Weak/soft/lazy single pointers in the element struct.
                for (const auto& wpe : emeta.weakLikePointers) {
                    uintptr_t r = ResolveWeakAt(structBase + wpe.offset);
                    if (!r) continue;
                    if (emit(r, cfe.offset, cfe.name + sides[s].suffix,
                             fieldType, kStructProp, e, cfe.stride, sides[s].within + wpe.offset))
                        return;
                }
            }
        }
    }
}

// Public façade over the file-static EnumerateOutgoingObjectPtrs template — see
// Aura.h. Lets Edel (current-target detection) score outgoing edges without
// duplicating the container / weak-ptr traversal.
void CollectOutgoingObjectPtrs(uintptr_t obj, std::vector<OutgoingPtr>& out,
                               int32_t maxEdges) {
    if (!obj) return;
    if (maxEdges <= 0) maxEdges = 1024;
    EnumerateOutgoingObjectPtrs(obj,
        [&](uintptr_t child, int32_t ptrOff, const std::string& ptrName,
            const std::string& ptrType, const std::string& innerType,
            int32_t elemIdx, int32_t elemStride, int32_t elemValueOffset) -> bool {
            OutgoingPtr e;
            e.target          = child;
            e.fieldOffset     = ptrOff;
            e.fieldName       = ptrName;
            e.fieldType       = ptrType;
            e.innerType       = innerType;
            e.elementIndex    = elemIdx;
            e.elemStride      = elemStride;
            e.elemValueOffset = elemValueOffset;
            out.push_back(std::move(e));
            return out.size() >= static_cast<size_t>(maxEdges);  // stop at the cap
        });
}

// Locate-in-GWorld recovery for a streaming / World-Partition actor whose ULevel
// is NOT forward-reachable from the root UWorld (so the plain BFS returns
// not_reachable). Key insight: an AActor's Outer IS its ULevel, and
// ULevel::OwningWorld points back at the world — so we reach the owning level by
// that BACK-reference (no forward pointer needed), confirm the actor is in
// ULevel::Actors, and emit:
//   world →(world-level back-ref)→ ULevel → Actors[k] → actor [→ … → target]
// The world→level hop is synthetic (fieldType "WorldLevel") because, by
// construction, if the level were forward-reachable the actor would have been too
// — so the plain BFS would already have found it. (That parenthetical used to read
// "level → Actors → actor is a reflected chain". It is not: ULevel::Actors carries
// no UPROPERTY, which is exactly what broke this recovery — audit #5 F8. Both hops
// below are synthetic back-references.) Returns found=true / status "ok_via_level" on success, or a
// default {found=false} GraphPathResult to signal "no recovery available".
// maxDepth is intentionally not a parameter: this recovery uses its own fixed bounds
// (the Outer climb is capped at 8 hops; the actor→target tail BFS at a deliberate 6),
// independent of the caller's BFS depth.
template <typename AbortFn>
static GraphPathResult RecoverViaWorldLevel(uintptr_t rootWorld, uintptr_t target,
                                            AbortFn&& abortFn, bool deep = false) {
    GraphPathResult res;  // found=false by default

    // The root must be a UWorld for the OwningWorld back-reference to mean anything.
    uintptr_t rootCls = Ubel::GetClass(rootWorld);
    if (!rootCls || Ubel::GetName(rootCls) != "World") return res;

    // Climb the Outer chain from `target` to the first object whose Outer is a
    // ULevel — that object is the owning ACTOR (target itself when target is an
    // actor; the owning actor when target is a component / AttributeSet). Bounded.
    uintptr_t actor = target, level = 0;
    for (int hop = 0; hop < 8 && actor; ++hop) {
        uintptr_t outer = Ubel::GetOuter(actor);
        if (!outer) break;
        uintptr_t outerCls = Ubel::GetClass(outer);
        if (outerCls && Ubel::GetName(outerCls) == "Level") { level = outer; break; }
        actor = outer;
    }
    if (!level || !actor) return res;

    // Confirm the level belongs to THIS world (guards multi-world / PIE). If the
    // OwningWorld field can't be resolved, proceed best-effort (don't reject).
    uintptr_t levelCls = Ubel::GetClass(level);
    if (!levelCls) return res;
    int32_t owOff = Ubel::FindFieldOffset(levelCls, "OwningWorld", "OwningWorld",
                                          nullptr, "ObjectProperty");
    if (owOff >= 0) {
        uintptr_t ow = 0;
        if (Macht::ReadSafe(level + owOff, ow) && ow && ow != rootWorld) return res;
    }

    // NO ULevel::Actors lookup, and that is the fix (audit #5 F8).
    //
    // `ULevel::Actors` is declared `TArray<TObjectPtr<AActor>> Actors;` with NO
    // UPROPERTY, so FindFieldOffset cannot see it. It returned < 0 and this whole
    // recovery bailed — which is why `ok_via_level` never fired: 18 sessions logged
    // actor_count 0 and not one non-zero. Worse, the fuzzy name fallback could bind
    // "Actors" to DestroyedReplicatedStaticActors, which IS reflected, and then scan
    // the wrong array.
    //
    // The membership it was proving is already guaranteed by construction: the Outer
    // climb above exits ONLY with `level = GetOuter(actor)`, i.e. this actor's own
    // level. Re-deriving that from a reflected array added a hard failure mode and
    // no information.
    //
    // The element index goes with it. There is no reflected array to index INTO, so
    // -1 is the honest answer, and it is the shape the UI already renders for the
    // synthetic hop directly above.

    // --- Build the chain. ---
    std::vector<GraphPathStep> steps;
    // (1) world → level: synthetic back-reference hop (no static pointer).
    {
        GraphPathStep s;
        s.fromObj = rootWorld; s.toObj = level;
        s.fieldOffset = -1; s.fieldName = "Levels"; s.fieldType = "WorldLevel";
        s.elementIndex = -1;
        steps.push_back(std::move(s));
    }
    // (2) level → actor: the Outer back-reference, synthetic for the same reason
    //     the world→level hop above is. ULevel::Actors carries no UPROPERTY, so
    //     there is no reflected offset to publish and no index to publish either
    //     — -1/-1 is what the UI already renders for a synthetic hop.
    {
        GraphPathStep s;
        s.fromObj = level; s.toObj = actor;
        s.fieldOffset = -1; s.fieldName = "Actors"; s.fieldType = "LevelActor";
        s.elementIndex = -1;
        steps.push_back(std::move(s));
    }
    // (3) actor → … → target: the short forward chain to the owned sub-object
    //     (empty when target IS the actor). The actor is now "reachable", and an
    //     owned component / AttributeSet is a few forward hops away (Related proves
    //     the chain exists). Bounded; if it isn't forward-linked, we still return
    //     the chain to the actor (landing on the owner is useful).
    if (actor != target) {
        auto neighborFn = [&](uintptr_t node, auto&& emit) {
            EnumerateOutgoingObjectPtrs(node, std::forward<decltype(emit)>(emit), deep);
        };
        GraphPathResult tail = BfsShortestObjectPath(actor, target, /*maxDepth*/ 6,
                                                     /*maxVisited*/ 500000, neighborFn, abortFn);
        if (tail.found)
            for (auto& s : tail.steps) steps.push_back(std::move(s));
    }

    res.steps        = std::move(steps);
    res.found        = true;
    res.status       = "ok_via_level";
    res.depthReached = static_cast<int32_t>(res.steps.size());
    return res;
}

GraphPathResult FindObjectGraphPath(uintptr_t rootObj, uintptr_t targetObj,
                                    int32_t maxDepth, int32_t deadlineMs, bool deep) {
    GraphPathResult res;
    if (!rootObj || !targetObj || !s_arrayAddr) { res.status = "invalid"; return res; }
    if (maxDepth <= 0)  maxDepth = 5;
    if (maxDepth > 32)  maxDepth = 32;     // hard cap — the reachable set grows fast
    if (deadlineMs <= 0) deadlineMs = 20000;

    constexpr int32_t kMaxVisited = 3000000;  // runaway guard (~48MB of map entries)
    auto t0 = std::chrono::steady_clock::now();

    LOG_INFO("FindObjectGraphPath: root=0x%llX target=0x%llX maxDepth=%d deep=%d",
             static_cast<unsigned long long>(rootObj),
             static_cast<unsigned long long>(targetObj), maxDepth, deep ? 1 : 0);

    auto abortFn = [&]() -> bool {
        if (Tot::Requested()) return true;
        auto dt = std::chrono::duration_cast<std::chrono::milliseconds>(
                      std::chrono::steady_clock::now() - t0).count();
        return dt > deadlineMs;
    };
    auto neighborFn = [&](uintptr_t node, auto&& emit) {
        EnumerateOutgoingObjectPtrs(node, std::forward<decltype(emit)>(emit), deep);
    };

    res = BfsShortestObjectPath(rootObj, targetObj, maxDepth, kMaxVisited,
                                neighborFn, abortFn);

    auto dt = std::chrono::duration_cast<std::chrono::milliseconds>(
                  std::chrono::steady_clock::now() - t0).count();
    res.durationMs = static_cast<int64_t>(dt);

    // Translate the core's generic "aborted" into the concrete reason.
    if (res.aborted)
        res.status = Tot::Requested() ? "cancelled" : "deadline";

    // Recovery: a streaming / World-Partition actor whose owning ULevel isn't
    // forward-reachable from the world. Reach the level by its OwningWorld
    // back-reference (see RecoverViaWorldLevel). Only on a clean not_reachable —
    // never override a deadline/cancel/cap (those mean "search incomplete", not
    // "definitively unreferenced"), and never re-run the heavy work on success.
    if (!res.found && res.status == "not_reachable") {
        GraphPathResult rec = RecoverViaWorldLevel(rootObj, targetObj, abortFn, deep);
        if (rec.found) {
            int32_t prevVisited = res.visited;   // keep the BFS diagnostic count
            res = std::move(rec);
            res.visited = prevVisited;
            LOG_INFO("FindObjectGraphPath: recovered via world-level back-reference (%d step(s))",
                     res.depthReached);
        }
    }

    // Resolve readable names for the path nodes only (cheap — a handful).
    for (auto& st : res.steps) {
        st.toName      = Ubel::GetName(st.toObj);
        uintptr_t cls  = Ubel::GetClass(st.toObj);
        st.toClassName = cls ? Ubel::GetName(cls) : "";
    }

    LOG_INFO("FindObjectGraphPath: %s — %d hop(s), visited %d, %lld ms%s",
             res.status.c_str(), res.depthReached, res.visited,
             static_cast<long long>(dt), res.found ? "" : " (no path)");
    return res;
}

// === Property Keyword Search ===

// Identify "class-like" metas. UClass instances have meta-class name "Class",
// but UE has several UClass subclasses whose own meta is a different string:
//   * Class                           — regular C++ UClass
//   * BlueprintGeneratedClass         — every BP-derived class (most games)
//   * AnimBlueprintGeneratedClass     — Anim BP-derived classes
//   * WidgetBlueprintGeneratedClass   — UMG widget BP-derived classes
//   * DynamicClass                    — Shipping cooked dynamic classes
// Before this whitelist, SearchProperties / ListClasses / EnumerateAllFunctions
// matched only "Class" and silently dropped every game-specific BPGC — which
// is where 90%+ of game-specific Health / Damage / Gold properties live. The
// user's TowerOfMask repro: `SearchProperties 'Health': 0 matches` despite
// `Health @ AnimMan_Player_C` clearly existing in the Class Struct view.
static bool IsClassLikeMeta(const std::string& metaClassName) {
    return metaClassName == "Class"
        || metaClassName == "BlueprintGeneratedClass"
        || metaClassName == "AnimBlueprintGeneratedClass"
        || metaClassName == "WidgetBlueprintGeneratedClass"
        || metaClassName == "DynamicClass";
}

// IsEnginePackage moved to Aura.h (header-inline, pure + unit-tested).

static std::string ToLower(const std::string& s) {
    std::string out = s;
    for (auto& c : out) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return out;
}

// FindDefiningClass: walk SuperStruct chain upward and return the
// highest-up class that still declares the property at `fieldOffset`.
//
// Algorithm:
//   For class C with SuperStruct S:
//     - If S exists and S.PropertiesSize > fieldOffset, then S has the
//       property too -- keep walking up (cur = S).
//     - Otherwise S doesn't have it (or no S), so C is the defining
//       class.
//
// 32-step depth cap matches Ubel's WalkClass inherited-walk so a
// pathological cycle in the SuperStruct chain can't hang us.
uintptr_t FindDefiningClass(uintptr_t classAddr, int32_t fieldOffset) {
    if (!classAddr) return 0;
    uintptr_t cur = classAddr;
    for (int depth = 0; depth < 32; ++depth) {
        uintptr_t super = 0;
        if (!Macht::ReadSafe(cur + DynOff::USTRUCT_SUPER, super) || !super) {
            // No super left -- cur is at the root (UObject) and must
            // be where the property lives.
            return cur;
        }
        int32_t superPropsSize = 0;
        if (!Macht::ReadSafe(super + DynOff::USTRUCT_PROPSSIZE, superPropsSize)
            || superPropsSize <= 0) {
            // Can't read super's PropertiesSize -- conservatively
            // attribute to current class.
            return cur;
        }
        // Super has the property too iff its size covers this offset.
        // Note: PropertiesSize is the END of the struct, so a property
        // at offset O is inside super iff O < superPropsSize.
        if (fieldOffset < superPropsSize) {
            cur = super;  // super has it; keep going up
            continue;
        }
        return cur;  // super doesn't have it; cur is the defining class
    }
    return cur;
}

// === Deep schema-leaf enumeration (build 1222) ===
//
// Property Search "Deep" mode descends into nested struct + struct-typed
// container-element schemas so a field like `GP` living at
// `BP_LifeSaveData_C.SaveSlotList[].MsTuneData.GP` becomes findable by name.
//
// Unlike GetClassContainers (which records the CONTAINERS for a class), this
// walks every SCALAR LEAF reachable THROUGH StructProperty fields and
// struct-typed container elements (TArray/TSet<FStruct>, TMap<K,FStruct>),
// emitting one synthetic dotted path per leaf.
//
// Schema-only: NO instance reads — the result depends solely on the type graph,
// so it is independent of any live object. Depth-capped + path-cycle guarded
// against self-referential struct definitions, and hard-capped on total leaves
// per class to bound pathological schemas.
struct SchemaLeaf {
    std::string path;            // dotted+indexed path ("SaveSlotList[].MsTuneData.GP")
    std::string leafName;        // last segment ("GP") — what the keyword matches
    std::string leafType;        // "IntProperty" / "NameProperty" / ...
    uintptr_t   leafFieldAddr = 0; // FProperty* of the leaf (find_property_xrefs key)
    int32_t     leafOffset = 0;  // offset within innermost struct (display only); from class
                                 // base when !throughContainer
    int32_t     leafSize = 0;
    uint8_t     boolMask = 0;
    int32_t     rootFieldOffset = 0; // offset of the depth-0 property that began the descent
                                     // (for defining-class dedup, mirrors the shallow path)
    bool        throughContainer = false; // path crosses >= 1 container hop
};

// Per-class hard cap on emitted leaves — protects against deep/wide or cyclic
// schemas. Most game classes produce well under this; a cap of a few thousand
// covers even the heaviest save-data blobs without unbounded growth.
static constexpr size_t kMaxSchemaLeavesPerClass = 4000;

static void CollectSchemaLeaves(
    uintptr_t structAddr,
    const std::string& namePrefix,
    int32_t rootFieldOffset,        // -1 at top level → pinned to the first hop's field
    int32_t accOffset,              // accumulated offset from class base (valid iff !throughContainer)
    bool throughContainer,
    int depth,
    std::unordered_set<uintptr_t>& pathStructs,  // cycle guard along the CURRENT path
    std::vector<SchemaLeaf>& out)
{
    constexpr int kMaxSchemaDepth = 4;
    if (depth > kMaxSchemaDepth || !structAddr) return;
    if (out.size() >= kMaxSchemaLeavesPerClass) return;
    // Cycle guard: a self-referential struct (FFoo holds a TArray<FFoo>) would
    // otherwise recurse forever. Only guard along the active path so two
    // sibling fields of the same struct type are both visited.
    if (!pathStructs.insert(structAddr).second) return;

    const ClassInfo& ci = Ubel::WalkClassEx(structAddr);
    for (const auto& f : ci.Fields) {
        if (out.size() >= kMaxSchemaLeavesPerClass) break;
        if (!f.Address) continue;
        std::string fullName = namePrefix.empty() ? f.Name : (namePrefix + "." + f.Name);
        int32_t rootOff = (rootFieldOffset < 0) ? f.Offset : rootFieldOffset;

        if (IsScalarLeafType(f.TypeName)) {
            // Only emit at depth >= 1 (nested). Depth-0 leaves are the class's
            // own direct fields, already covered by the shallow search loop.
            if (depth >= 1) {
                SchemaLeaf lf;
                lf.path             = fullName;
                lf.leafName         = f.Name;
                lf.leafType         = f.TypeName;
                lf.leafFieldAddr    = f.Address;
                lf.leafOffset       = accOffset + f.Offset;
                lf.leafSize         = f.Size;
                lf.boolMask         = f.boolFieldMask;
                lf.rootFieldOffset  = rootOff;
                lf.throughContainer = throughContainer;
                out.push_back(std::move(lf));
            }
        }
        else if (f.TypeName == "StructProperty") {
            uintptr_t inner = 0;
            if (Macht::ReadSafe(f.Address + DynOff::FSTRUCTPROP_STRUCT, inner) && inner)
                CollectSchemaLeaves(inner, fullName, rootOff,
                                    accOffset + f.Offset, throughContainer,
                                    depth + 1, pathStructs, out);
        }
        else if (f.TypeName == "ArrayProperty" && f.innerType == "StructProperty") {
            uintptr_t inner = Ubel::GetContainerInnerStructAddr(f.Address);
            if (inner)
                CollectSchemaLeaves(inner, fullName + "[]", rootOff,
                                    /*accOffset reset*/ 0, /*throughContainer*/ true,
                                    depth + 1, pathStructs, out);
        }
        else if (f.TypeName == "SetProperty" && f.elemType == "StructProperty") {
            uintptr_t inner = Ubel::GetContainerInnerStructAddr(f.Address);
            if (inner)
                CollectSchemaLeaves(inner, fullName + "[]", rootOff,
                                    0, true, depth + 1, pathStructs, out);
        }
        else if (f.TypeName == "MapProperty") {
            Ubel::MapPairLayout layout;
            if (Ubel::GetMapPairLayout(f.Address, layout)) {
                // Tag the side only when BOTH sides are structs (mirrors
                // WalkContainerLeaves); a struct-on-one-side map reads cleaner
                // as "Map[].Field".
                const bool bothStruct = layout.valueStructAddr && layout.keyStructAddr;
                if (layout.valueStructAddr)
                    CollectSchemaLeaves(layout.valueStructAddr,
                                        fullName + "[]" + (bothStruct ? ".Value" : ""),
                                        rootOff, 0, true, depth + 1, pathStructs, out);
                if (layout.keyStructAddr)
                    CollectSchemaLeaves(layout.keyStructAddr,
                                        fullName + "[]" + (bothStruct ? ".Key" : ""),
                                        rootOff, 0, true, depth + 1, pathStructs, out);
            }
        }
        else if (f.TypeName == "OptionalProperty" && f.innerType == "StructProperty") {
            // TOptional<FStruct>: value lives at field+0, so offset accumulation
            // matches a bare StructProperty (same as CollectContainersRecursive).
            uintptr_t innerProp = 0, inner = 0;
            if (Macht::ReadSafe(f.Address + DynOff::FARRAYPROP_INNER, innerProp) && innerProp
                && Macht::ReadSafe(innerProp + DynOff::FSTRUCTPROP_STRUCT, inner) && inner)
                CollectSchemaLeaves(inner, fullName, rootOff,
                                    accOffset + f.Offset, throughContainer,
                                    depth + 1, pathStructs, out);
        }
    }
    pathStructs.erase(structAddr);
}

// Cache for FindDefiningClass results -- per (classAddr, fieldOffset).
// Reset implicitly per SearchProperties call (keyed by a thread-local
// epoch would be over-engineering; the cost is one map per call).

PropertySearchResult SearchProperties(
    const std::string& query,
    const std::vector<std::string>& typeFilter,
    bool gameOnly,
    int maxResults,
    bool deep)
{
    PropertySearchResult result;

    std::string lowerQuery = ToLower(query);

    // Build lowercase type filter set for fast lookup
    std::unordered_set<std::string> typeSet;
    for (const auto& t : typeFilter) typeSet.insert(ToLower(t));

    // Track already-visited UClass addresses to avoid duplicates
    std::unordered_set<uintptr_t> visitedClasses;

    // Per-call cache of FindDefiningClass results -- a single property
    // walked across many subclasses would otherwise re-walk the
    // SuperStruct chain redundantly. Keyed by (classAddr, fieldOffset)
    // because different fields on the same class have different
    // defining classes. Wrapped in a struct because std::pair isn't
    // hashable out of the box.
    struct FieldKey {
        uintptr_t classAddr;
        int32_t   offset;
        bool operator==(const FieldKey& o) const {
            return classAddr == o.classAddr && offset == o.offset;
        }
    };
    struct FieldKeyHash {
        size_t operator()(const FieldKey& k) const {
            return std::hash<uintptr_t>{}(k.classAddr)
                 ^ (std::hash<int32_t>{}(k.offset) << 1);
        }
    };
    std::unordered_map<FieldKey, uintptr_t, FieldKeyHash> definingCache;

    // Dedup map: groups inheriting classes by (definingClass, propName, offset).
    // Value is the index into `result.results` for that group's
    // representative match. inheritedByCount accumulates as we visit
    // more classes that inherit the same field.
    struct DedupKey {
        uintptr_t   definingClassAddr;
        std::string propName;
        int32_t     offset;
        bool operator==(const DedupKey& o) const {
            return definingClassAddr == o.definingClassAddr
                && offset == o.offset
                && propName == o.propName;
        }
    };
    struct DedupKeyHash {
        size_t operator()(const DedupKey& k) const {
            return std::hash<uintptr_t>{}(k.definingClassAddr)
                 ^ (std::hash<std::string>{}(k.propName) << 1)
                 ^ (std::hash<int32_t>{}(k.offset) << 2);
        }
    };
    std::unordered_map<DedupKey, size_t, DedupKeyHash> dedupIndex;

    int32_t count = GetCount();
    // NOT result.scannedObjects — that is filled after the loop with what was
    // actually walked. Assigning the pool size here made every capped search claim
    // a full sweep. (audit #5 D5/F4)
    int32_t walked = 0;

    for (int32_t i = 0; i < count && static_cast<int>(result.results.size()) < maxResults; ++i) {
        walked = i + 1;   // objects ENTERED, so a full sweep reports count, not count-1
        if ((i & 0xFFF) == 0 && Tot::Requested()) {
            Sein::Warn("PIPE:search", "SearchProperties: aborted (client gone / shutdown)");
            result.aborted = true;
            break;  // return partial result
        }
        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;

        // Identify class-like objects (UClass + BlueprintGeneratedClass +
        // variants). See IsClassLikeMeta for why "== Class" alone is too
        // strict — it drops every BPGC and breaks property search on
        // game-specific BP fields.
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        uint32_t clsNameIdx = 0;
        if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;

        std::string metaClassName = Serie::GetString(clsNameIdx);
        if (!IsClassLikeMeta(metaClassName)) continue;

        // This object is a class. Skip if already visited.
        if (!visitedClasses.insert(obj).second) continue;

        // Get class path for game_only filter
        std::string classPath = Ubel::GetFullName(obj);
        if (gameOnly && IsEnginePackage(classPath)) continue;

        result.scannedClasses++;

        // Walk class properties (including inherited)
        const ClassInfo& ci = Ubel::WalkClassEx(obj);
        if (ci.Fields.empty()) continue;

        // Search properties
        for (const auto& field : ci.Fields) {
            if (static_cast<int>(result.results.size()) >= maxResults) break;

            // Case-insensitive substring match on property name
            std::string lowerPropName = ToLower(field.Name);
            if (lowerPropName.find(lowerQuery) == std::string::npos) continue;

            // Optional type filter
            if (!typeSet.empty()) {
                std::string lowerType = ToLower(field.TypeName);
                if (typeSet.find(lowerType) == typeSet.end()) continue;
            }

            // Resolve defining class (cached per field key).
            FieldKey fk{ obj, field.Offset };
            uintptr_t definingAddr = 0;
            auto cacheIt = definingCache.find(fk);
            if (cacheIt != definingCache.end()) {
                definingAddr = cacheIt->second;
            } else {
                definingAddr = FindDefiningClass(obj, field.Offset);
                definingCache[fk] = definingAddr;
            }
            if (!definingAddr) definingAddr = obj;  // safety net

            // Dedup: have we seen this defining-class+name+offset combo?
            DedupKey dk{ definingAddr, field.Name, field.Offset };
            auto dedupIt = dedupIndex.find(dk);
            if (dedupIt != dedupIndex.end()) {
                // This class inherits a field we've already emitted.
                // Bump the inheritedByCount on the existing match.
                auto& existing = result.results[dedupIt->second];
                existing.inheritedByCount++;
                // Update preview-source if THIS subclass is more derived
                // (bigger PropertiesSize) than the previous best -- bias
                // toward leaf classes that actually have live instances.
                if (ci.PropertiesSize > existing.previewPropertiesSize) {
                    existing.previewClassAddr      = obj;
                    existing.previewPropertiesSize = ci.PropertiesSize;
                }
                continue;
            }

            // First time seeing this (definingClass, propName, offset)
            // triple -- emit a representative row keyed by the defining
            // class, NOT the iterated class. That way the user sees
            // "bCanBeDamaged @ AActor (inherited by 4822)" instead of
            // "bCanBeDamaged @ BP_RandomChild_C" depending on iteration
            // order.
            std::string definingName;
            std::string definingPath;
            if (definingAddr == obj) {
                // Defining class is the one we're iterating -- already have its name/path.
                definingName = ci.Name;
                definingPath = classPath;
            } else {
                // Defining class is somewhere up the chain -- read its name + path.
                definingName = Ubel::GetName(definingAddr);
                definingPath = Ubel::GetFullName(definingAddr);
            }

            PropertyMatch match;
            // The headline className/classAddr/classPath/superName all
            // reflect the DEFINING class -- the user wants to see the
            // canonical home of the field, not whichever subclass we
            // happened to iterate first.
            match.className   = definingName;
            match.classAddr   = definingAddr;
            match.classPath   = definingPath;
            // SuperName is the defining class's super (if we can read it
            // cheaply). For non-iterated defining-classes we'd need to
            // read it via DynOff::USTRUCT_SUPER -> name; skip for now
            // since it's not load-bearing for the dedup story.
            match.superName   = (definingAddr == obj) ? ci.SuperName : "";
            match.propName    = field.Name;
            match.propType    = field.TypeName;
            match.propOffset  = field.Offset;
            match.propSize    = field.Size;
            match.structType  = field.structType;
            match.innerType   = field.innerType;
            // Inheritance fields
            match.definingClassName = definingName;
            match.definingClassAddr = definingAddr;
            match.definingClassPath = definingPath;
            match.inheritedByCount  = 0;  // bumps as we encounter inheritors below
            // Preview metadata (read from any class -- the defining
            // class CDO would be most "canonical" but the iterated
            // class's preview is still valid since the field is
            // identical).
            match.fieldAddr      = field.Address;
            match.propertyFlags  = field.PropertyFlags;
            match.boolFieldMask  = field.boolFieldMask;
            match.keyType        = field.keyType;
            match.valueType      = field.valueType;
            // Seed preview source with the iterated class -- guaranteed
            // to have the field (we're walking its property chain). Will
            // be replaced by a more-derived subclass on later count bumps.
            match.previewClassAddr      = obj;
            match.previewPropertiesSize = ci.PropertiesSize;

            dedupIndex[dk] = result.results.size();
            result.results.push_back(std::move(match));
        }

        // === Deep descent: nested struct + container-element leaves ===
        //
        // Opt-in. Emits synthetic dotted-path matches for scalar leaves reached
        // THROUGH StructProperty members + struct-typed container elements. The
        // result is findable-by-name only: Find Instances (owning class) and
        // Find Funcs (leaf FProperty*) work; Copy Offset / Freeze are gated off
        // in the UI for these rows (no single class-absolute address once the
        // path crosses a container). Schema-only — no instance reads here.
        if (deep && static_cast<int>(result.results.size()) < maxResults) {
            std::vector<SchemaLeaf> leaves;
            std::unordered_set<uintptr_t> pathStructs;
            CollectSchemaLeaves(obj, /*namePrefix*/ "", /*rootFieldOffset*/ -1,
                                /*accOffset*/ 0, /*throughContainer*/ false,
                                /*depth*/ 0, pathStructs, leaves);

            for (const auto& lf : leaves) {
                if (static_cast<int>(result.results.size()) >= maxResults) break;

                // Match keyword against the LEAF name (last path segment) —
                // same "property named X" semantics as the shallow search.
                if (lowerQuery.empty()
                    || ToLower(lf.leafName).find(lowerQuery) == std::string::npos)
                    continue;
                if (!typeSet.empty()
                    && typeSet.find(ToLower(lf.leafType)) == typeSet.end())
                    continue;

                // Defining-class dedup keyed on the ROOT field's defining class
                // + dotted path. Mirrors the shallow dedup so an inherited
                // nested struct field emits one row (bumping inheritedByCount
                // across the subclasses that share it). Reuses the shared
                // definingCache — a root field IS a direct field of `obj`, so
                // the (obj, offset) key resolves identically on both paths.
                FieldKey rfk{ obj, lf.rootFieldOffset };
                uintptr_t definingAddr = 0;
                auto rcacheIt = definingCache.find(rfk);
                if (rcacheIt != definingCache.end()) {
                    definingAddr = rcacheIt->second;
                } else {
                    definingAddr = FindDefiningClass(obj, lf.rootFieldOffset);
                    definingCache[rfk] = definingAddr;
                }
                if (!definingAddr) definingAddr = obj;

                // The dotted path makes sibling leaves under the same root
                // distinct; rootFieldOffset keys the inheritance collapse.
                DedupKey dk{ definingAddr, lf.path, lf.rootFieldOffset };
                auto dedupIt = dedupIndex.find(dk);
                if (dedupIt != dedupIndex.end()) {
                    result.results[dedupIt->second].inheritedByCount++;
                    continue;
                }

                std::string definingName, definingPath;
                if (definingAddr == obj) {
                    definingName = ci.Name;
                    definingPath = classPath;
                } else {
                    definingName = Ubel::GetName(definingAddr);
                    definingPath = Ubel::GetFullName(definingAddr);
                }

                PropertyMatch match;
                match.className   = definingName;
                match.classAddr   = definingAddr;
                match.classPath   = definingPath;
                match.superName   = "";
                match.propName    = lf.path;        // synthetic dotted path
                match.propType    = lf.leafType;
                match.propOffset  = lf.leafOffset;  // informational only (see isNested)
                match.propSize    = lf.leafSize;
                match.definingClassName = definingName;
                match.definingClassAddr = definingAddr;
                match.definingClassPath = definingPath;
                match.inheritedByCount  = 0;
                match.fieldAddr      = lf.leafFieldAddr;  // leaf FProperty* (xref key)
                match.boolFieldMask  = lf.boolMask;
                match.isNested       = true;
                // No preview source — previewClassAddr stays 0 so Phase 2 skips
                // these rows (the swap makes classAddr 0 → no instance found).
                match.previewClassAddr      = 0;
                match.previewPropertiesSize = 0;

                dedupIndex[dk] = result.results.size();
                result.results.push_back(std::move(match));
            }
        }
    }

    // --- Phase 2: Resolve value previews from representative instances ---
    //
    // After dedup, match.classAddr is the DEFINING class (often abstract
    // -- AActor / APawn / etc -- with no direct instances). We use the
    // separate previewClassAddr (the most-derived subclass observed
    // during the search loop) to find a live instance whose data we can
    // sample. Since the property is at the same offset on every subclass,
    // the preview value is identical regardless of which subclass we
    // sampled.
    if (!result.results.empty()) {
        // 2a. Collect unique preview-source class set (subclasses
        // chosen for instance lookup, NOT the defining classes).
        // ⚠ Keyed on the DEFINING class (`classAddr`), not on `previewClassAddr`.
        // [CDOSCOPE-2026-08-20]
        //
        // `previewClassAddr` is whichever subclass had the biggest `PropertiesSize`, chosen
        // with the comment "bias toward leaf classes that actually have live instances" —
        // a PROXY for having live instances rather than a test of it, and the proxy is wrong
        // often enough to matter. Measured on DumperTest: the `NiagaraComponent ·
        // WarmupTickCount` row picked class 0x…797000, which has only a CDO, while
        // `NiagaraComponent` itself (0x…797800) had two live `NiagaraComponent0` instances.
        // The row therefore read `0 (CDO default)` — "nothing is live" — while Freeze on that
        // same row reported `on 2 instance(s)`.
        //
        // The defining class is what Force (Solide) and Freeze target, so keying on it is
        // what makes the preview and the actions answer the same question. Sampling any
        // instance in that hierarchy is sound for the same reason the original comment gives:
        // the property sits at the same offset on every subclass.
        //
        // Rows with no preview source (`previewClassAddr == 0`: the deep-descent nested
        // leaves) must still be skipped — that zero is load-bearing.
        std::unordered_set<uintptr_t> needPreviewClasses;
        for (const auto& m : result.results)
            if (m.previewClassAddr) needPreviewClasses.insert(m.classAddr);

        // 2b. Scan GObjects to find one instance per preview-source class.
        //
        // A LIVE instance, not the class default object. The CDO is an instance of its own
        // class and is constructed at class-load time, so it sits near the front of GObjects
        // and won this first-match-wins race essentially every time — the Preview column then
        // showed the Blueprint default forever (Health = 100 while the player is at 37).
        // `obj != cls` only excluded the UClass itself, never its CDO. (audit #5 A5)
        //
        // Same `Default__` test Solide.cpp:170/:282, Wirbel.cpp:328 and Edel.cpp:94 already
        // apply — a name compare rather than RF_ClassDefaultObject because the flags word is
        // one more unverified offset, and CLAUDE.md's rule is that UObject offsets are probed,
        // not assumed. The name is only resolved for objects whose class we actually want,
        // which is a handful out of the pool.
        std::unordered_map<uintptr_t, uintptr_t> instanceMap;
        // Classes for which the CDO is all that exists. Used only after the sweep, so a live
        // instance appearing later always wins — the whole point of the finding.
        std::unordered_map<uintptr_t, uintptr_t> cdoOnlyMap;

        // Live instances of a SUBCLASS of a preview class. [CDOSCOPE-2026-08-20]
        //
        // Force and Freeze on a Property Search row are scoped to the class AND EVERY
        // SUBCLASS, so a preview that only looked for an exact-class instance could mark a
        // row `(CDO default)` — reading as "nothing is live" — while the action on that same
        // row then reported `on 2 instance(s)`. Measured on DumperTest with
        // NiagaraComponent::WarmupTickCount. Collected here and applied only where no exact
        // instance was found, so an exact sample always wins.
        std::unordered_map<uintptr_t, uintptr_t> derivedMap;

        // Per-UClass verdict cache: cls -> the preview class it derives from, or 0 for none.
        // Same reasoning as FindInstancesDerivedFrom's derivedCache — GObjects holds 10^5-10^6
        // objects over 10^3-10^4 distinct classes, so caching by UClass* turns a per-OBJECT
        // chain walk into a per-CLASS one, which is what makes this affordable at all.
        std::unordered_map<uintptr_t, uintptr_t> derivesFromCache;
        auto previewBaseOf = [&](uintptr_t cls) -> uintptr_t {
            auto it = derivesFromCache.find(cls);
            if (it != derivesFromCache.end()) return it->second;
            uintptr_t found = 0;
            uintptr_t cur = cls;
            // Bounded the same way Ubel::ResolveFunctionInChain and Dunste::FindFuncByName
            // are: a malformed or mid-teardown SuperStruct can self-loop.
            for (int depth = 0; cur && depth < 64; ++depth) {
                if (needPreviewClasses.count(cur)) { found = cur; break; }
                uintptr_t super = 0;
                if (!Macht::ReadSafe(cur + static_cast<uintptr_t>(DynOff::USTRUCT_SUPER), super)
                    || super == 0 || super == cur)
                    break;
                cur = super;
            }
            derivesFromCache[cls] = found;
            return found;
        };

        int32_t cnt = GetCount();
        // ⚠ The early exit counts EXACT hits only. A class whose only live instance is a
        // subclass must not stop the sweep early — the derived sample it needs may still be
        // ahead of us, and settling for the CDO there is the defect being fixed.
        for (int32_t i = 0; i < cnt && instanceMap.size() < needPreviewClasses.size(); ++i) {
            // Skipping CDOs means a class with no live instance no longer satisfies the
            // early-exit condition, so this loop can now run the full pool. It is the same
            // sweep the search loop above already does, but it must stay abortable.
            if ((i & 0xFFF) == 0 && Tot::Requested()) break;   // partial previews, not a hang

            uintptr_t obj = GetByIndex(i);
            if (!obj) continue;

            uintptr_t cls = 0;
            if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

            // Skip if this IS a UClass (we want instances, not the class itself)
            if (obj == cls) continue;

            const bool exact = needPreviewClasses.count(cls) != 0;
            // Only pay for the chain walk when the class is not one we already want, and
            // only while some preview class still lacks a derived sample.
            const uintptr_t base = exact ? cls
                                 : (derivedMap.size() < needPreviewClasses.size()
                                        ? previewBaseOf(cls) : 0);
            if (!base) continue;
            if (exact ? (instanceMap.count(base) != 0) : (derivedMap.count(base) != 0)) continue;

            uint32_t nameIdx = 0;
            if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx) &&
                Serie::GetString(nameIdx).find("Default__") != std::string::npos) {
                // Only the row's OWN class default is offered as the last-resort sample. A
                // subclass CDO is another class's default, which would be a worse answer than
                // the row's own and is not what the actions would touch either.
                if (exact) cdoOnlyMap.emplace(base, obj);   // first CDO wins; live still preferred
                continue;
            }

            if (exact) instanceMap[base] = obj;
            else       derivedMap.emplace(base, obj);
        }

        // Fall back to the CDO where nothing live exists — a default value is still the only
        // truth available, and dropping the preview entirely would lose information. But it
        // must not be presented as a live reading: that silent substitution IS the defect
        // this fix removes, so the row says which one it got (marked in 2c below).
        // Exact > derived > class default, decided by Aura::ChoosePreviewSource so the
        // ordering is stated in a header a test can compile. [CDOSCOPE-2026-08-20]
        std::unordered_map<uintptr_t, PreviewSource> previewSourceOf;
        for (uintptr_t pc : needPreviewClasses) {
            const PreviewSource src = ChoosePreviewSource(
                instanceMap.count(pc) != 0, derivedMap.count(pc) != 0, cdoOnlyMap.count(pc) != 0);
            switch (src) {
                case PreviewSource::Derived:      instanceMap[pc] = derivedMap[pc];  break;
                case PreviewSource::ClassDefault: instanceMap[pc] = cdoOnlyMap[pc];  break;
                default: break;   // Exact is already in instanceMap; None has nothing to add
            }
            if (src != PreviewSource::None) previewSourceOf[pc] = src;
        }

        // 2c. Read property values and fill previews. `instanceMap` is now keyed by the
        // DEFINING class, which is what `ResolvePropertyPreviews` already looks matches up
        // by — so the old swap of classAddr <-> previewClassAddr around the call is gone.
        // It existed only to make the lookup use the size-picked subclass.
        if (!instanceMap.empty()) {
            // Resolve EnumProperty: read UEnum* from FField for matches that need it
            for (auto& m : result.results) {
                if (m.propType == "EnumProperty" && m.enumAddr == 0 && m.fieldAddr) {
                    Macht::ReadSafe(m.fieldAddr + DynOff::FENUMPROP_ENUM, m.enumAddr);
                }
            }
            Ubel::ResolvePropertyPreviews(result.results, instanceMap);
            // Mark the rows whose only available sample was the class default object, so a
            // Blueprint default cannot read as a live value. `preview` is a free-text column
            // (bound as a TextBlock, and one of the fields the keyword box ORs over), so the
            // marker needs no wire or UI change and stays greppable by the user.
            for (auto& m : result.results) {
                if (m.preview.empty()) continue;
                auto it = previewSourceOf.find(m.classAddr);
                if (it == previewSourceOf.end()) continue;
                m.preview += PreviewSourceSuffix(it->second);
            }
        }
    }

    result.scannedObjects = walked;
    result.truncated = static_cast<int>(result.results.size()) >= maxResults;

    // The stop reason is in the line, not just the counts. "3 matches from 8
    // classes (scanned 24445 objects)" was self-contradictory and read as a
    // completed sweep; "scanned 8 objects, STOPPED at the 3-row cap" cannot.
    Sein::Info("PIPE:search",
                 "SearchProperties '%s': %d matches from %d classes (scanned %d objects)%s",
                 query.c_str(), static_cast<int>(result.results.size()),
                 result.scannedClasses, result.scannedObjects,
                 result.aborted   ? ", ABORTED (client gone / shutdown)"
               : result.truncated ? ", STOPPED at the result cap — more matches exist"
                                  : ", full sweep");
    return result;
}

// === Property Keyword Search — Batched ===
//
// Walks GObjects + WalkClassEx ONCE and checks every property against
// every query in `queries` in the same iteration. The big-O is now
// O(classes × fields × queries) but the queries-loop is a cheap
// std::string::find on lowercased names — the cost is dwarfed by the
// classes × fields walk that dominates the single-query version. For
// a 36-query / 4400-class game this drops wall time from ~42s
// (sequential pipe calls each re-walking GObjects) to ~1.5s.
//
// Per-query state (dedup index, results vector, fill count) is
// independent; per-field state (defining class, WalkClassEx output)
// is shared across queries. PropertyMatch.inheritedByCount counts
// inheritance hits PER QUERY since dedup keys are local to each
// query's result set.

std::vector<PropertySearchResult> SearchPropertiesBatch(
    const std::vector<std::string>& queries,
    const std::vector<std::string>& typeFilter,
    bool gameOnly,
    int maxResultsPerQuery,
    bool /*withPreviews*/)
{
    // Per-query state — independent dedup + results, lowercased query
    // pre-computed once.
    struct DedupKey {
        uintptr_t   definingClassAddr;
        std::string propName;
        int32_t     offset;
        bool operator==(const DedupKey& o) const {
            return definingClassAddr == o.definingClassAddr
                && offset == o.offset
                && propName == o.propName;
        }
    };
    struct DedupKeyHash {
        size_t operator()(const DedupKey& k) const {
            return std::hash<uintptr_t>{}(k.definingClassAddr)
                 ^ (std::hash<std::string>{}(k.propName) << 1)
                 ^ (std::hash<int32_t>{}(k.offset) << 2);
        }
    };
    struct QueryState {
        std::string lowerQuery;
        PropertySearchResult result;
        std::unordered_map<DedupKey, size_t, DedupKeyHash> dedup;
    };
    std::vector<QueryState> qs;
    qs.reserve(queries.size());
    for (const auto& q : queries) {
        qs.push_back(QueryState{ ToLower(q), {}, {} });
    }

    // Shared state across queries.
    std::unordered_set<std::string> typeSet;
    for (const auto& t : typeFilter) typeSet.insert(ToLower(t));

    struct FieldKey {
        uintptr_t classAddr;
        int32_t   offset;
        bool operator==(const FieldKey& o) const {
            return classAddr == o.classAddr && offset == o.offset;
        }
    };
    struct FieldKeyHash {
        size_t operator()(const FieldKey& k) const {
            return std::hash<uintptr_t>{}(k.classAddr)
                 ^ (std::hash<int32_t>{}(k.offset) << 1);
        }
    };

    std::unordered_set<uintptr_t> visitedClasses;
    std::unordered_map<FieldKey, uintptr_t, FieldKeyHash> definingCache;

    int32_t count = GetCount();
    int32_t scannedClasses = 0;
    // Same fix as the single-query path: what was WALKED, not the pool size.
    // (audit #5 D5/F4)
    int32_t walked = 0;
    bool    batchAborted = false;

    for (int32_t i = 0; i < count; ++i) {
        walked = i + 1;   // objects ENTERED, so a full sweep reports count, not count-1
        if ((i & 0xFFF) == 0 && Tot::Requested()) {
            Sein::Warn("PIPE:search", "SearchPropertiesBatch: aborted (client gone / shutdown)");
            batchAborted = true;
            break;  // return partial result
        }
        // Early-exit: if every query is already at limit, stop walking.
        bool allFull = true;
        for (const auto& s : qs) {
            if (static_cast<int>(s.result.results.size()) < maxResultsPerQuery) {
                allFull = false;
                break;
            }
        }
        if (allFull) break;

        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;

        // Class-like meta filter (matches single-query SearchProperties).
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;
        uint32_t clsNameIdx = 0;
        if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;
        std::string metaClassName = Serie::GetString(clsNameIdx);
        if (!IsClassLikeMeta(metaClassName)) continue;

        if (!visitedClasses.insert(obj).second) continue;

        std::string classPath = Ubel::GetFullName(obj);
        if (gameOnly && IsEnginePackage(classPath)) continue;

        scannedClasses++;

        const ClassInfo& ci = Ubel::WalkClassEx(obj);
        if (ci.Fields.empty()) continue;

        // Per-field: check every query in one pass over the field list.
        for (const auto& field : ci.Fields) {
            // Lowercase property name once per field (not per-query).
            std::string lowerPropName = ToLower(field.Name);

            // Type filter — apply once per field, before the keyword loop,
            // because it's keyword-independent.
            if (!typeSet.empty()) {
                std::string lowerType = ToLower(field.TypeName);
                if (typeSet.find(lowerType) == typeSet.end()) continue;
            }

            // Defining-class lookup: cached across queries since the
            // (class, offset) pair determines the definition site
            // independently of which keyword caused the match.
            uintptr_t definingAddr = 0;
            bool definingResolved = false;

            for (auto& s : qs) {
                if (static_cast<int>(s.result.results.size()) >= maxResultsPerQuery) continue;
                if (lowerPropName.find(s.lowerQuery) == std::string::npos) continue;

                // Resolve defining class lazily — only the first matching
                // query in this field triggers the lookup.
                if (!definingResolved) {
                    FieldKey fk{ obj, field.Offset };
                    auto cacheIt = definingCache.find(fk);
                    if (cacheIt != definingCache.end()) {
                        definingAddr = cacheIt->second;
                    } else {
                        definingAddr = FindDefiningClass(obj, field.Offset);
                        definingCache[fk] = definingAddr;
                    }
                    if (!definingAddr) definingAddr = obj;
                    definingResolved = true;
                }

                // Per-query dedup: if this query already emitted a match
                // for the same (defining-class, name, offset), bump the
                // inheritor count instead of duplicating.
                DedupKey dk{ definingAddr, field.Name, field.Offset };
                auto dedupIt = s.dedup.find(dk);
                if (dedupIt != s.dedup.end()) {
                    auto& existing = s.result.results[dedupIt->second];
                    existing.inheritedByCount++;
                    if (ci.PropertiesSize > existing.previewPropertiesSize) {
                        existing.previewClassAddr      = obj;
                        existing.previewPropertiesSize = ci.PropertiesSize;
                    }
                    continue;
                }

                // First-time match for this query — emit a new row.
                std::string definingName;
                std::string definingPath;
                if (definingAddr == obj) {
                    definingName = ci.Name;
                    definingPath = classPath;
                } else {
                    definingName = Ubel::GetName(definingAddr);
                    definingPath = Ubel::GetFullName(definingAddr);
                }

                PropertyMatch match;
                match.className   = definingName;
                match.classAddr   = definingAddr;
                match.classPath   = definingPath;
                match.superName   = (definingAddr == obj) ? ci.SuperName : "";
                match.propName    = field.Name;
                match.propType    = field.TypeName;
                match.propOffset  = field.Offset;
                match.propSize    = field.Size;
                match.structType  = field.structType;
                match.innerType   = field.innerType;
                match.definingClassName = definingName;
                match.definingClassAddr = definingAddr;
                match.definingClassPath = definingPath;
                match.inheritedByCount  = 0;
                match.fieldAddr      = field.Address;
                match.propertyFlags  = field.PropertyFlags;
                match.boolFieldMask  = field.boolFieldMask;
                match.keyType        = field.keyType;
                match.valueType      = field.valueType;
                match.previewClassAddr      = obj;
                match.previewPropertiesSize = ci.PropertiesSize;

                s.dedup[dk] = s.result.results.size();
                s.result.results.push_back(std::move(match));
            }
        }
    }

    // Phase 2 (preview resolution) is INTENTIONALLY SKIPPED in the
    // batch path. Interesting Properties — the primary consumer —
    // doesn't display previews; values are read on-demand when the user
    // opens a row in Live Walker. Skipping the second GObjects pass +
    // ResolvePropertyPreviews call buys us another big chunk of the
    // batch-vs-sequential speedup.
    //
    // If a future caller needs previews, add a branch on withPreviews
    // that mirrors the single-query Phase 2 — collect unique preview
    // classes across all queries, find one instance per class, resolve
    // previews per match.

    std::vector<PropertySearchResult> out;
    out.reserve(qs.size());
    for (auto& s : qs) {
        s.result.scannedObjects = walked;
        s.result.scannedClasses = scannedClasses;
        s.result.aborted   = batchAborted;
        // Per-query, not per-batch: the loop stops when EVERY query is full, so one
        // query can be capped while another is not.
        s.result.truncated = static_cast<int>(s.result.results.size()) >= maxResultsPerQuery;
        out.push_back(std::move(s.result));
    }

    int totalMatches = 0;
    for (const auto& r : out) totalMatches += static_cast<int>(r.results.size());
    Sein::Info("PIPE:search", "SearchPropertiesBatch: %d queries -> %d total matches from %d classes (scanned %d objects)",
                 static_cast<int>(queries.size()), totalMatches,
                 scannedClasses, count);
    return out;
}

// === Batched class schema walk ===
//
// Pure pipe-amortisation helper: invokes Ubel::WalkClassEx once per
// input address and returns the results in the same order. Built as a
// trivial loop on top of the single-class function so each batch
// element is byte-identical to a single-call walk_class response —
// the safety guarantee that lets SdkExportService / DumpAllService
// switch from N round-trips to N/200 without risking dropped fields.
//
// Caller chunks the request (~200 addrs per call) to keep response
// payloads bounded and progress feedback live.
std::vector<ClassInfo> WalkClassesBatch(const std::vector<uintptr_t>& addrs)
{
    auto t0 = std::chrono::high_resolution_clock::now();
    std::vector<ClassInfo> out;
    out.reserve(addrs.size());

    int emptyCount = 0;
    size_t batchIdx = 0;
    for (uintptr_t addr : addrs) {
        // Cooperative cancel: a Full SDK dump can pass a large addr[] chunk and
        // each WalkClassEx is heavy. Bail if the client disconnected / shutting
        // down (NOT a time deadline — a long legitimate dump must run to end).
        if ((batchIdx++ & 0xFFF) == 0 && Tot::Requested()) {
            Sein::Warn("PIPE:walk", "WalkClassesBatch: aborted (client gone / shutdown)");
            break;  // return partial result
        }
        // ClassInfo lives at global scope (see Ubel.h), but the
        // WalkClassEx function lives inside namespace Ubel.
        const ClassInfo& ci = Ubel::WalkClassEx(addr);
        if (ci.Fields.empty()) ++emptyCount;
        out.push_back(std::move(ci));
    }

    auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
                       std::chrono::high_resolution_clock::now() - t0).count();
    Sein::Info("PIPE:walk", "WalkClassesBatch: %d addrs -> %d results (%d empty) in %lld ms",
               static_cast<int>(addrs.size()), static_cast<int>(out.size()),
               emptyCount, static_cast<long long>(elapsed));
    return out;
}

// --- Heuristic Scorer: auto-rank classes by RE interest ---

static int GetFieldTypeWeight(const std::string& typeName) {
    // High-value: game stats, collections
    if (typeName == "FloatProperty" || typeName == "DoubleProperty") return 3;
    if (typeName == "ArrayProperty") return 3;

    // Medium-value: integers, structs, object refs, maps/sets
    if (typeName == "IntProperty"   || typeName == "Int8Property"  ||
        typeName == "Int16Property" || typeName == "Int32Property" ||
        typeName == "Int64Property" || typeName == "UInt16Property"||
        typeName == "UInt32Property"|| typeName == "UInt64Property") return 2;
    if (typeName == "StructProperty") return 2;
    if (typeName == "ObjectProperty"     || typeName == "ClassProperty"      ||
        typeName == "WeakObjectProperty" || typeName == "LazyObjectProperty" ||
        typeName == "SoftObjectProperty" || typeName == "SoftClassProperty"  ||
        typeName == "InterfaceProperty") return 2;
    if (typeName == "MapProperty" || typeName == "SetProperty") return 2;

    // Low-value: enums, bools, strings, bytes
    if (typeName == "EnumProperty") return 1;
    if (typeName == "BoolProperty") return 1;
    if (typeName == "StrProperty"  || typeName == "TextProperty" ||
        typeName == "NameProperty" || typeName == "ByteProperty") return 1;

    return 1; // Unknown types get minimum weight
}

static int GetSuperClassBonus(const std::string& superName) {
    if (superName.empty()) return 0;

    // Convert to lowercase for case-insensitive matching
    std::string lower = superName;
    for (auto& c : lower) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

    // Checked in priority order (most specific first)
    if (lower.find("character") != std::string::npos || lower.find("pawn") != std::string::npos) return 20;
    if (lower.find("playercontroller") != std::string::npos ||
        lower.find("aicontroller")     != std::string::npos ||
        lower.find("controller")       != std::string::npos) return 15;
    if (lower.find("playerstate") != std::string::npos ||
        lower.find("gamestate")   != std::string::npos ||
        lower.find("gamemode")    != std::string::npos) return 15;
    if (lower.find("gameinstance") != std::string::npos) return 10;
    if (lower.find("actor") != std::string::npos) return 10;
    if (lower.find("actorcomponent") != std::string::npos ||
        lower.find("scenecomponent") != std::string::npos) return 8;
    if (lower.find("widget") != std::string::npos || lower.find("userwidget") != std::string::npos) return 5;
    if (lower.find("animinstance") != std::string::npos) return 5;
    if (lower.find("dataasset") != std::string::npos) return 5;

    return 0;
}

static int ComputeHeuristicScore(const ClassInfo& ci) {
    int score = 0;

    // Sum per-field type weights
    for (const auto& f : ci.Fields) {
        score += GetFieldTypeWeight(f.TypeName);
    }

    // Super class bonus
    score += GetSuperClassBonus(ci.SuperName);

    // Size bonus
    if (ci.PropertiesSize > 0x400)       score += 5;
    else if (ci.PropertiesSize > 0x100)  score += 3;
    else if (ci.PropertiesSize > 0)      score += 1;

    // Penalty for empty/abstract classes
    if (ci.Fields.empty()) score -= 5;

    return (score < 0) ? 0 : score;
}

// --- ListClasses ---

ClassListResult ListClasses(bool gameOnly, int maxResults) {
    ClassListResult result;

    std::unordered_set<uintptr_t> visitedClasses;

    int32_t count = GetCount();
    result.scannedObjects = count;

    // The walk is NOT bounded by the row cap: it runs to the end of GObjects so
    // `totalClasses` is the HONEST pool total, not a copy of the cap ([CLASSTOTAL]).
    // Only ROW materialization (the costly WalkClassEx + score + push) stops at the
    // cap; past it we still count each qualifying class — a handful of cheap reads
    // per object, the same per-object work EnumerateAllFunctions already does over the
    // whole pool. `truncated` keeps its exact meaning: the row list is a page, not the
    // pool.
    for (int32_t i = 0; i < count; ++i) {
        if ((i & 0xFFF) == 0 && Tot::Requested()) {
            Sein::Warn("PIPE:list", "ListClasses: aborted (client gone / shutdown)");
            break;  // return partial result
        }
        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;

        // Identify class-like objects (UClass + BPGC variants); see
        // IsClassLikeMeta for the rationale on accepting more than just
        // "Class".
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        uint32_t clsNameIdx = 0;
        if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;

        std::string metaClassName = Serie::GetString(clsNameIdx);
        if (!IsClassLikeMeta(metaClassName)) continue;

        // Skip if already visited
        if (!visitedClasses.insert(obj).second) continue;

        // Get class path for game_only filter
        std::string classPath = Ubel::GetFullName(obj);
        if (gameOnly && IsEnginePackage(classPath)) continue;

        // Count EVERY qualifying class — this is the honest total, independent of the
        // row cap. The expensive per-class walk below is skipped once results fills.
        result.totalClasses++;

        // Row cap reached: keep counting classes (above) but stop building rows.
        // WalkClassEx / ComputeHeuristicScore / entry alloc are the costly parts, so
        // nothing heavy runs past the cap.
        if (static_cast<int>(result.results.size()) >= maxResults) continue;

        // Walk class to get property count and size
        const ClassInfo& ci = Ubel::WalkClassEx(obj);

        ClassListEntry entry;
        entry.className      = ci.Name;
        entry.classAddr      = obj;
        entry.classPath      = classPath;
        entry.superName      = ci.SuperName;
        entry.propertyCount  = static_cast<int32_t>(ci.Fields.size());
        entry.propertiesSize = ci.PropertiesSize;
        entry.heuristicScore = ComputeHeuristicScore(ci);
        result.results.push_back(std::move(entry));
    }

    // A full page means the walk collected the cap and there were more classes to
    // collect — same contract (and the same one-index-of-slack ambiguity) as
    // SearchByName / SearchProperties. `totalClasses` now carries the real count, so a
    // truncated result reports "shown of total" honestly.
    result.truncated = static_cast<int>(result.results.size()) >= maxResults;

    // Sort by heuristic score descending, then alphabetically for ties
    std::sort(result.results.begin(), result.results.end(),
        [](const ClassListEntry& a, const ClassListEntry& b) {
            if (a.heuristicScore != b.heuristicScore)
                return a.heuristicScore > b.heuristicScore;
            return a.className < b.className;
        });

    Sein::Info("PIPE:list", "ListClasses: %d rows of %d classes total (gameOnly=%d, scanned %d objects)%s",
                 static_cast<int>(result.results.size()), result.totalClasses, gameOnly ? 1 : 0,
                 result.scannedObjects,
                 result.truncated ? ", STOPPED at the result cap — more classes exist" : "");
    return result;
}

// === Class-noise auto-detect (class-filter Phase 3) ===

bool ClassDerivesFromAny(uintptr_t classObj,
                         const std::unordered_set<std::string>& baseNames) {
    uintptr_t cls = classObj;
    for (int guard = 0; cls && guard < 64; ++guard) {   // bounded; super-chains are ~5-10 deep
        if (baseNames.count(Ubel::GetName(cls))) return true;
        uintptr_t super = 0;
        if (!Macht::ReadSafe(cls + static_cast<uintptr_t>(DynOff::USTRUCT_SUPER), super)
            || !super || super == cls)
            break;
        cls = super;
    }
    return false;
}

std::vector<NoiseClassVerdict> ClassifyNoiseClasses(const std::vector<std::string>& classNames) {
    // Pure-engine LEAF bases (shared with the snapshot source-level skip so both
    // surfaces agree on what counts as noise). See SnapshotEngineNoiseBases().
    const std::unordered_set<std::string>& kNoiseBases = SnapshotEngineNoiseBases();

    // Verdict slot per distinct requested name, preserving input order.
    std::vector<NoiseClassVerdict> out;
    std::unordered_map<std::string, size_t> indexByName;
    out.reserve(classNames.size());
    for (const auto& n : classNames) {
        if (n.empty() || indexByName.count(n)) continue;
        indexByName.emplace(n, out.size());
        NoiseClassVerdict v; v.className = n;
        out.push_back(std::move(v));
    }
    if (out.empty()) return out;

    // The histogram keys on the SHORT class name, so two distinct UClasses can
    // share one requested name across packages (a game class + an engine
    // namesake). Resolve CONSERVATIVELY: a name is noise only if EVERY UClass
    // with that name is noise — a single non-noise namesake spares it. This
    // upholds "never false-exclude a gameplay class" even on a short-name
    // collision (we'd rather miss hiding an engine namesake than hide a game
    // class). So we scan the WHOLE GObjects array (no first-wins early-out).
    std::vector<char> resolved(out.size(), 0);
    std::vector<char> allNoise(out.size(), 1);   // AND-accumulator across namesakes

    int32_t count = GetCount();
    for (int32_t i = 0; i < count; ++i) {
        if ((i & 0xFFF) == 0 && Tot::Requested()) break;
        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;

        // Is obj a UClass? (metaclass-name gate, same as ListClasses.)
        uintptr_t metaCls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, metaCls) || !metaCls) continue;
        uint32_t metaNameIdx = 0;
        if (!Macht::ReadSafe(metaCls + Grimoire::OFF_UOBJECT_NAME, metaNameIdx)) continue;
        if (!IsClassLikeMeta(Serie::GetString(metaNameIdx))) continue;

        auto it = indexByName.find(Ubel::GetName(obj));   // the class's own name
        if (it == indexByName.end()) continue;
        const size_t idx = it->second;

        bool thisNoise = false;
        std::string thisReason;
        if (IsEnginePackage(Ubel::GetFullName(obj))) {
            thisNoise = true;  thisReason = "engine package";
        } else if (ClassDerivesFromAny(obj, kNoiseBases)) {
            thisNoise = true;  thisReason = "engine base class";
        }

        resolved[idx] = 1;
        if (thisNoise) {
            if (out[idx].reason.empty()) out[idx].reason = thisReason;  // remember a rule label
        } else {
            allNoise[idx] = 0;   // a non-noise namesake spares the name
        }
    }

    int flagged = 0;
    for (size_t idx = 0; idx < out.size(); ++idx) {
        out[idx].isNoise = resolved[idx] && allNoise[idx];
        if (!out[idx].isNoise) out[idx].reason.clear();
        else ++flagged;
    }

    int resolvedCount = 0;
    for (char r : resolved) if (r) ++resolvedCount;
    Sein::Info("PIPE:noise", "ClassifyNoiseClasses: %d/%d names resolved, %d flagged noise",
               resolvedCount, static_cast<int>(out.size()), flagged);
    return out;
}

// --- EnumerateAllFunctions ---
//
// Mirrors the SearchProperties / ListClasses GObjects-walk pattern: scan
// every object, identify UClasses by metaclass-name, dedupe via a visited
// set, and flatten the per-class function list into a single result vector.
//
// Per-class cost is dominated by Ubel::WalkFunctions which walks the
// UField::Children chain (4096-iteration safety cap, 256-iteration param
// cap per function). On 1M-object games this typically takes 2-10s
// because the UFunction count per class is small (usually <50) and the
// per-class walk caches nothing — we pay the full O(F) per class.

AllFunctionsResult EnumerateAllFunctions(bool gameOnly, int maxEntries) {
    AllFunctionsResult result;

    std::unordered_set<uintptr_t> visitedClasses;

    int32_t count = GetCount();
    result.scannedObjects = count;

    for (int32_t i = 0; i < count; ++i) {
        if ((i & 0xFFF) == 0 && Tot::Requested()) {
            Sein::Warn("PIPE:list", "EnumerateAllFunctions: aborted (client gone / shutdown)");
            result.aborted = true;
            break;  // return partial result
        }
        if (static_cast<int>(result.entries.size()) >= maxEntries) { result.truncated = true; break; }

        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;

        // Identify class-like object (UClass + BPGC variants) via
        // IsClassLikeMeta — same helper SearchProperties + ListClasses use.
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        uint32_t clsNameIdx = 0;
        if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;

        std::string metaClassName = Serie::GetString(clsNameIdx);
        if (!IsClassLikeMeta(metaClassName)) continue;

        // Skip duplicates (same UClass can be referenced from multiple GObjects slots
        // when CDOs or hot-reload artefacts keep stale handles around).
        if (!visitedClasses.insert(obj).second) continue;

        std::string classPath = Ubel::GetFullName(obj);
        if (gameOnly && IsEnginePackage(classPath)) continue;

        result.scannedClasses++;

        // Walk class metadata + functions. WalkClassEx is needed for SuperName
        // (used by the UI's class-keyword scoring). It also walks fields, which
        // is wasted work here, but the alternative (a Functions-only walker)
        // would mean a parallel reader path -- not worth the maintenance burden
        // for the typical perf budget.
        const ClassInfo& ci = Ubel::WalkClassEx(obj);
        std::vector<FunctionInfo> funcs = Ubel::WalkFunctions(obj);

        for (const auto& f : funcs) {
            if (static_cast<int>(result.entries.size()) >= maxEntries) { result.truncated = true; break; }

            AllFunctionEntry entry;
            entry.className     = ci.Name;
            entry.classAddr     = obj;
            entry.superName     = ci.SuperName;
            entry.classPath     = classPath;
            entry.funcName      = f.name;
            entry.funcAddr      = f.address;
            entry.functionFlags = f.functionFlags;
            entry.numParms      = f.numParms;
            entry.parmsSize     = f.parmsSize;
            result.entries.push_back(std::move(entry));
            result.totalFunctions++;
        }
    }

    Sein::Info("PIPE:list",
        "EnumerateAllFunctions: %d entries from %d classes "
        "(gameOnly=%d, scanned %d objects, total funcs %d%s%s)",
        static_cast<int>(result.entries.size()), result.scannedClasses,
        gameOnly ? 1 : 0, result.scannedObjects, result.totalFunctions,
        result.truncated ? ", TRUNCATED at cap" : "",
        result.aborted ? ", ABORTED" : "");
    return result;
}

// --- v2a: Blueprint ubergraph entry-offset table ---
//
// Every BP event compiles into the single ExecuteUbergraph_<BP> function; each
// event's stub UFunction calls it with an int "entry offset" into the bytecode:
//   EX_(Local)FinalFunction <ExecuteUbergraph* 8B> EX_IntConst <entryOffset 4B> ...
// We anchor on the ubergraph function's ADDRESS (zero false positives), verify
// the preceding opcode is EX_LocalFinalFunction(0x46)/EX_FinalFunction(0x1C) and
// the following byte is EX_IntConst(0x1D), then read the entry offset. Returns
// (entryOffset, eventName) sorted ascending so the caller can attribute a
// reference at byte P to the event whose entry offset is the largest <= P.
static std::vector<std::pair<int32_t, std::string>>
BuildUbergraphEntryTable(uintptr_t classAddr, uintptr_t ubergraphAddr) {
    std::vector<std::pair<int32_t, std::string>> table;
    if (!classAddr || !ubergraphAddr) return table;

    uint8_t anchor[8];
    memcpy(anchor, &ubergraphAddr, sizeof(anchor));

    std::vector<FunctionInfo> funcs = Ubel::WalkFunctions(classAddr);
    std::vector<uint8_t> buf;
    for (const auto& fi : funcs) {
        if (fi.address == ubergraphAddr) continue;  // skip the ubergraph itself

        uintptr_t scriptData = 0; int32_t scriptNum = 0;
        Macht::ReadSafe(fi.address + DynOff::USTRUCT_SCRIPT,        scriptData);
        Macht::ReadSafe(fi.address + DynOff::USTRUCT_SCRIPT + 0x08, scriptNum);
        if (!scriptData || scriptNum < 13 || scriptNum > (1 << 22)) continue;  // need op+ptr+int

        buf.resize(static_cast<size_t>(scriptNum));
        if (!Macht::ReadBytesSafe(scriptData, buf.data(), static_cast<size_t>(scriptNum)))
            continue;

        // p starts at 1 so buf[p-1] (the call opcode) is valid; need ptr(8) +
        // EX_IntConst(1) + int32(4) to follow.
        for (int32_t p = 1; p + 8 + 1 + 4 <= scriptNum; ++p) {
            if (memcmp(buf.data() + p, anchor, 8) != 0) continue;
            uint8_t op = buf[p - 1];
            if (op != 0x46 && op != 0x1C) continue;   // EX_LocalFinalFunction / EX_FinalFunction
            if (buf[p + 8] != 0x1D) continue;         // EX_IntConst
            int32_t entryOffset = 0;
            memcpy(&entryOffset, buf.data() + p + 9, 4);
            table.emplace_back(entryOffset, fi.name);
            break;  // one ubergraph entry per stub
        }
    }
    std::sort(table.begin(), table.end(),
              [](const std::pair<int32_t, std::string>& a,
                 const std::pair<int32_t, std::string>& b) { return a.first < b.first; });
    return table;
}

// v2 read/write: is the FProperty reference at byte offset p (its pointer
// position; the variable opcode sits at p-1) the DESTINATION of an assignment?
// Detects the common direct forms (opcode values from UE EExprToken):
//   EX_LetBool(0x14)/MulticastDelegate(0x43)/Delegate(0x44)/Obj(0x5F)/WeakObjPtr(0x60)
//                                  [LetOp][varOp][ptr]
//   EX_Let(0x0F)                   [0x0F][propptr 8B][varOp][ptr]
//   EX_LetValueOnPersistentFrame(0x64)  [0x64][ptr]  (property read directly = dest)
// Best-effort: a write whose LHS is wrapped (EX_Context / EX_StructMemberContext /
// EX_ArrayGetByRef — i.e. Other.Field = x, self.Struct.Member = x, Arr[i] = x) is
// NOT detected and falls through as a read. The address-shape check on EX_Let's
// property slot keeps false positives near zero.
static bool IsWriteContext(const uint8_t* buf, int32_t p) {
    if (p < 1) return false;
    uint8_t op = buf[p - 1];
    if (op == 0x64) return true;  // EX_LetValueOnPersistentFrame (property = destination)
    // Destination must be a variable-access opcode:
    //   EX_LocalVariable/InstanceVariable/DefaultVariable/LocalOutVariable/ClassSparseDataVariable
    if (op != 0x00 && op != 0x01 && op != 0x02 && op != 0x48 && op != 0x6C)
        return false;
    if (p >= 2) {
        uint8_t b = buf[p - 2];  // LetX forms with no property slot
        if (b == 0x14 || b == 0x43 || b == 0x44 || b == 0x5F || b == 0x60) return true;
    }
    if (p >= 10 && buf[p - 10] == 0x0F) {  // EX_Let: opcode + 8B property + varOp + ptr
        uintptr_t prop = 0;
        memcpy(&prop, buf + p - 9, sizeof(prop));
        if (LooksLikeHeapPtr(prop)) return true;
    }
    return false;
}

// --- FindPropertyXrefs: Kismet bytecode static cross-reference (Path 1) ---
//
// Walk GObjects; for every UFunction, read UStruct::Script and byte-scan the
// bytecode for the 8-byte little-endian `propAddr`. The variable-access opcodes
// embed the live FProperty* directly, so any function that references the field
// contains its pointer in the script buffer. Parallelised over GObjects index
// ranges via ParallelGObjectsScan (Ubel name/outer caches are mutex-guarded).
PropertyXrefResult FindPropertyXrefs(uintptr_t propAddr, bool gameOnly,
                                     int32_t maxResults) {
    PropertyXrefResult out;
    if (!propAddr || !s_arrayAddr) return out;
    if (maxResults <= 0) maxResults = 200;

    int32_t count = GetCount();
    if (count <= 0) return out;
    out.stats.objectsTotal = count;

    LOG_INFO("FindPropertyXrefs: scanning %d objects for xrefs to FProperty 0x%llX (gameOnly=%d)",
             count, static_cast<unsigned long long>(propAddr), gameOnly ? 1 : 0);

    // Target pointer as 8 little-endian bytes for the memcmp window.
    uint8_t needle[8];
    memcpy(needle, &propAddr, sizeof(needle));

    constexpr int kDeadlineMs = 30000;
    auto t0 = std::chrono::steady_clock::now();

    struct ThreadResult {
        std::vector<PropertyXref> xrefs;
        int32_t funcsScanned    = 0;
        int32_t funcsWithScript = 0;
    };

    auto scan = ParallelGObjectsScan<ThreadResult>(count,
        [&](ThreadResult& tr, int32_t beginIdx, int32_t endIdx,
            std::atomic<bool>& deadlineHit) {

        std::vector<uint8_t> buf;    // reused across functions (keeps capacity)
        std::vector<int32_t> offs;   // match byte offsets within this Script

        for (int32_t i = beginIdx;
             i < endIdx && static_cast<int>(tr.xrefs.size()) < maxResults; ++i) {
            // Chunk-relative stride so the deadline / sibling check fires from
            // this chunk's first iteration (mirrors FindReferencesToUObject).
            if (((i - beginIdx) & 0x3FF) == 0) {
                if (deadlineHit.load(std::memory_order_relaxed)) return;
                // Serial path has no cancel-watcher thread — poll Tot here too.
                if (Tot::Requested()) { deadlineHit.store(true, std::memory_order_relaxed); return; }
                auto dt = std::chrono::duration_cast<std::chrono::milliseconds>(
                              std::chrono::steady_clock::now() - t0).count();
                if (dt > kDeadlineMs) {
                    deadlineHit.store(true, std::memory_order_relaxed);
                    return;
                }
            }

            uintptr_t obj = GetByIndex(i);
            if (!obj) continue;

            // UFunction? Its UClass name is "Function".
            uintptr_t cls = 0;
            if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;
            uint32_t clsNameIdx = 0;
            if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;
            if (Serie::GetString(clsNameIdx) != "Function") continue;
            tr.funcsScanned++;

            // Read UStruct::Script { Data*, Num, Max }.
            uintptr_t scriptData = 0; int32_t scriptNum = 0;
            Macht::ReadSafe(obj + DynOff::USTRUCT_SCRIPT,        scriptData);
            Macht::ReadSafe(obj + DynOff::USTRUCT_SCRIPT + 0x08, scriptNum);
            if (!scriptData || scriptNum <= 0 || scriptNum > (1 << 22)) continue;  // sanity guard
            tr.funcsWithScript++;

            // Bulk-read bytecode, byte-scan for the (UNALIGNED) pointer value.
            buf.resize(static_cast<size_t>(scriptNum));
            if (!Macht::ReadBytesSafe(scriptData, buf.data(), static_cast<size_t>(scriptNum)))
                continue;

            offs.clear();
            int32_t writeCount = 0;
            for (int32_t p = 0; p + 8 <= scriptNum; ++p) {
                if (memcmp(buf.data() + p, needle, 8) == 0) {
                    offs.push_back(p);
                    if (IsWriteContext(buf.data(), p)) writeCount++;
                }
            }
            if (offs.empty()) continue;
            uint8_t precByte = (offs[0] > 0) ? buf[offs[0] - 1] : 0xFF;  // classify first hit

            // Owning class = UFunction's Outer. Apply gameOnly on its path.
            uintptr_t owner = Ubel::GetOuter(obj);
            if (gameOnly && owner && IsEnginePackage(Ubel::GetFullName(owner))) continue;

            std::string funcName = Ubel::GetName(obj);

            PropertyXref x;
            x.funcAddr       = obj;
            x.funcName       = funcName;
            x.funcFullName   = Ubel::GetFullName(obj);
            x.ownerClassAddr = owner;
            x.ownerClassName = owner ? Ubel::GetName(owner) : "";
            x.occurrences    = static_cast<int32_t>(offs.size());
            x.writeCount     = writeCount;
            x.kind = (precByte == 0x01) ? "instance"
                   : (precByte == 0x00) ? "local" : "ref";
            // v2a: retain match offsets only for ubergraph hits (attributed to
            // events in a serial post-pass). funcName.rfind(prefix,0)==0 = starts_with.
            if (funcName.rfind("ExecuteUbergraph", 0) == 0)
                x.ubergraphOffsets = offs;  // copied (offs is reused next iter)
            tr.xrefs.push_back(std::move(x));
        }
    });

    out.xrefs = ConcatTruncate(scan.perThread, &ThreadResult::xrefs, maxResults);
    for (auto& tr : scan.perThread) {
        out.stats.functionsScanned    += tr.funcsScanned;
        out.stats.functionsWithScript += tr.funcsWithScript;
    }

    // v2a: attribute ubergraph hits to BP events (serial — the entry-table build
    // walks sibling UFunctions). Cache per ubergraph address; several refs can
    // share one. Attribution = the event whose entry offset is the largest <= P.
    {
        std::unordered_map<uintptr_t, std::vector<std::pair<int32_t, std::string>>> entryCache;
        for (auto& x : out.xrefs) {
            if (x.ubergraphOffsets.empty()) continue;
            auto it = entryCache.find(x.funcAddr);
            if (it == entryCache.end())
                it = entryCache.emplace(
                         x.funcAddr,
                         BuildUbergraphEntryTable(x.ownerClassAddr, x.funcAddr)).first;
            const auto& table = it->second;
            if (!table.empty()) {
                std::set<std::string> events;
                for (int32_t off : x.ubergraphOffsets) {
                    const std::string* best = nullptr;
                    for (const auto& e : table) {
                        if (e.first <= off) best = &e.second; else break;  // sorted ascending
                    }
                    if (best) events.insert(*best);
                }
                std::string joined;
                for (const auto& e : events) {
                    if (!joined.empty()) joined += ", ";
                    joined += e;
                }
                x.eventName = joined;
            }
            x.ubergraphOffsets.clear();  // transient
        }
    }
    out.stats.deadlineHit = scan.deadlineHit;
    out.stats.durationMs  = std::chrono::duration_cast<std::chrono::milliseconds>(
                                std::chrono::steady_clock::now() - t0).count();

    LOG_INFO("FindPropertyXrefs: %zu xrefs (scanned %d functions, %d with script, %lldms%s)",
             out.xrefs.size(), out.stats.functionsScanned, out.stats.functionsWithScript,
             static_cast<long long>(out.stats.durationMs),
             out.stats.deadlineHit ? ", DEADLINE" : "");
    return out;
}

// --- FindFunctionsByClassParam: reflection xref (class as parameter) ---------
//
// The target UClass*/UScriptStruct* a Struct/Object-family FProperty (or UE4
// UProperty) param points at, via its subclass-extension slot; 0 otherwise.
// Mirrors the param-type enrichment in Ubel::WalkFunctions.
static uintptr_t ParamTargetType(uintptr_t fieldAddr) {
    std::string pName, pType;
    if (!Ubel::ResolvePropertyNameType(fieldAddr, pName, pType)) return 0;
    const bool classBearing =
        pType == "StructProperty"     || pType == "ObjectProperty"     ||
        pType == "ClassProperty"      || pType == "WeakObjectProperty" ||
        pType == "SoftObjectProperty" || pType == "SoftClassProperty"  ||
        pType == "InterfaceProperty"  || pType == "LazyObjectProperty";
    if (!classBearing) return 0;
    // FStructProperty::Struct and FObjectPropertyBase::PropertyClass share the
    // FProperty subclass-extension slot; UE4 (<4.25) UProperty uses Offset+0x2C.
    const int slot = DynOff::bUseFProperty ? DynOff::FSTRUCTPROP_STRUCT
                                           : (DynOff::UPROPERTY_OFFSET + 0x2C);
    uintptr_t target = 0;
    Macht::ReadSafe(fieldAddr + slot, target);
    return target;
}

// Count `func`'s params whose declared type pointer == targetClass; set
// hasReturnMatch if any matched param is the return value. Walks the function's
// own FProperty (USTRUCT_CHILDPROPS) or UE4 UProperty (USTRUCT_CHILDREN) chain
// exactly as Ubel::WalkFunctions does.
static int32_t CountClassParams(uintptr_t func, uintptr_t targetClass,
                                bool& hasReturnMatch) {
    constexpr uint64_t CPF_ReturnParm = 0x0400;
    hasReturnMatch = false;
    const bool fprop = DynOff::bUseFProperty;
    uintptr_t chain = 0;
    const int chainOff = fprop ? DynOff::USTRUCT_CHILDPROPS : DynOff::USTRUCT_CHILDREN;
    if (!Macht::ReadSafe(func + chainOff, chain) || !chain) return 0;
    uintptr_t cur = fprop ? DynOff::StripFFieldTag(chain) : chain;
    int32_t matches = 0, limit = 256;
    std::unordered_set<uintptr_t> seen;
    while (cur != 0 && limit-- > 0) {
        if (!seen.insert(cur).second) break;
        if (fprop && DynOff::IsFFieldVariantUObject(cur)) break;

        if (ParamTargetType(cur) == targetClass) {   // targetClass != 0 (caller-checked)
            matches++;
            uint64_t flags = 0;
            const int flagsOff = fprop ? DynOff::FPROPERTY_FLAGS : DynOff::UPROPERTY_FLAGS;
            Macht::ReadSafe(cur + flagsOff, flags);
            if (flags & CPF_ReturnParm) hasReturnMatch = true;
        }
        uintptr_t next = 0;
        const int nextOff = fprop ? DynOff::FFIELD_NEXT : DynOff::UFIELD_NEXT;
        if (!Macht::ReadSafe(cur + nextOff, next)) break;
        cur = fprop ? DynOff::StripFFieldTag(next) : next;
    }
    return matches;
}

// See Aura.h: which UFunctions take `classAddr` as a direct param/return. Same
// parallel-GObjects scaffolding as FindPropertyXrefs, but the per-function inner
// step is a reflection param-chain walk instead of a bytecode byte-scan — so it
// catches native functions too.
PropertyXrefResult FindFunctionsByClassParam(uintptr_t classAddr, bool gameOnly,
                                             int32_t maxResults) {
    PropertyXrefResult out;
    if (!classAddr || !s_arrayAddr) return out;
    if (maxResults <= 0) maxResults = 200;

    // ParamTargetType reads the FProperty subclass-extension slot
    // (DynOff::FSTRUCTPROP_STRUCT / UProperty +0x2C), which is only an ESTIMATE
    // until Ubel::CorrectSubclassOffsets fine-tunes it — and that runs lazily,
    // inside WalkClassEx/WalkInstance only (never at init). Force one calibrating
    // walk of the target class up front so a COLD scan (before any Class Struct /
    // Live Walker walk) reads the correct slot on shifted-layout games (UE5.7+).
    // Idempotent + per-class cached → a no-op once warm. (Calibration keys off a
    // StructProperty in the class hierarchy; near-universal for gameplay classes.)
    Ubel::WalkClassEx(classAddr);

    int32_t count = GetCount();
    if (count <= 0) return out;
    out.stats.objectsTotal = count;

    LOG_INFO("FindFunctionsByClassParam: scanning %d objects for UFunctions taking class 0x%llX as param (gameOnly=%d)",
             count, static_cast<unsigned long long>(classAddr), gameOnly ? 1 : 0);

    constexpr int kDeadlineMs = 30000;
    auto t0 = std::chrono::steady_clock::now();

    struct ThreadResult {
        std::vector<PropertyXref> xrefs;
        int32_t funcsScanned = 0;
        int32_t funcsMatched = 0;
    };

    auto scan = ParallelGObjectsScan<ThreadResult>(count,
        [&](ThreadResult& tr, int32_t beginIdx, int32_t endIdx,
            std::atomic<bool>& deadlineHit) {
        for (int32_t i = beginIdx;
             i < endIdx && static_cast<int>(tr.xrefs.size()) < maxResults; ++i) {
            if (((i - beginIdx) & 0x3FF) == 0) {
                if (deadlineHit.load(std::memory_order_relaxed)) return;
                if (Tot::Requested()) { deadlineHit.store(true, std::memory_order_relaxed); return; }
                auto dt = std::chrono::duration_cast<std::chrono::milliseconds>(
                              std::chrono::steady_clock::now() - t0).count();
                if (dt > kDeadlineMs) { deadlineHit.store(true, std::memory_order_relaxed); return; }
            }

            uintptr_t obj = GetByIndex(i);
            if (!obj) continue;

            // UFunction? Its UClass name is "Function".
            uintptr_t cls = 0;
            if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;
            uint32_t clsNameIdx = 0;
            if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;
            if (Serie::GetString(clsNameIdx) != "Function") continue;
            tr.funcsScanned++;

            bool hasReturn = false;
            int32_t matches = CountClassParams(obj, classAddr, hasReturn);
            if (matches == 0) continue;

            // Owning class = UFunction's Outer. Apply gameOnly on its path.
            uintptr_t owner = Ubel::GetOuter(obj);
            if (gameOnly && owner && IsEnginePackage(Ubel::GetFullName(owner))) continue;
            tr.funcsMatched++;

            PropertyXref x;
            x.funcAddr       = obj;
            x.funcName       = Ubel::GetName(obj);
            x.funcFullName   = Ubel::GetFullName(obj);
            x.ownerClassAddr = owner;
            x.ownerClassName = owner ? Ubel::GetName(owner) : "";
            x.occurrences    = matches;
            x.writeCount     = 0;
            x.kind           = hasReturn ? "return" : "param";
            tr.xrefs.push_back(std::move(x));
        }
    });

    out.xrefs = ConcatTruncate(scan.perThread, &ThreadResult::xrefs, maxResults);
    for (auto& tr : scan.perThread) {
        out.stats.functionsScanned    += tr.funcsScanned;
        out.stats.functionsWithScript += tr.funcsMatched;  // reused slot: functions matched
    }
    out.stats.deadlineHit = scan.deadlineHit;
    out.stats.durationMs  = std::chrono::duration_cast<std::chrono::milliseconds>(
                                std::chrono::steady_clock::now() - t0).count();

    LOG_INFO("FindFunctionsByClassParam: %zu matches (scanned %d functions, %lldms%s)",
             out.xrefs.size(), out.stats.functionsScanned,
             static_cast<long long>(out.stats.durationMs),
             out.stats.deadlineHit ? ", DEADLINE" : "");
    return out;
}

// Map a value-access opcode to a property-scope label (see FunctionPropRef::scope).
static const char* ScopeForOpcode(uint8_t op) {
    switch (op) {
        case 0x01: return "instance";  // EX_InstanceVariable (class member — the RE target)
        case 0x00: return "local";     // EX_LocalVariable (BP temporaries / locals)
        case 0x48: return "local";     // EX_LocalOutVariable (out param)
        case 0x02: return "default";   // EX_DefaultVariable
        case 0x6C: return "sparse";    // EX_ClassSparseDataVariable
        case 0x42: return "struct";    // EX_StructMemberContext (inner struct member)
        case 0x64: return "frame";     // EX_LetValueOnPersistentFrame
        default:   return "";
    }
}

// --- Path 2: detect UFunction::Func offset (lazy, cached) ---------------
//
// Every UFunction stores a FNativeFuncPtr (UFunction::Func) near its tail:
// native functions point it at their execXxx thunk in .text, script functions
// at UObject::ProcessInternal — either way it's an in-module code pointer. No
// other pointer member in the 0x80..0x158 window holds a MEM_IMAGE code pointer
// (FirstPropertyToInit / Outer / Class are heap objects), so the first offset
// in range where every sampled UFunction holds a LooksLikeCodePointer IS Func.
// Detected once on first Path-2 use; 0 = not found → native analysis disabled.
static void EnsureUFunctionFuncOffset() {
    if (DynOff::bUFunctionFuncDetected.load(std::memory_order_acquire)) return;
    static std::mutex s_detectMutex;
    std::lock_guard<std::mutex> lk(s_detectMutex);
    if (DynOff::bUFunctionFuncDetected.load(std::memory_order_relaxed)) return;

    constexpr int kWant = 32;
    std::vector<uintptr_t> samples;
    int32_t count = GetCount();
    for (int32_t i = 0; i < count && static_cast<int>(samples.size()) < kWant; ++i) {
        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;
        uint32_t clsNameIdx = 0;
        if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;
        if (Serie::GetString(clsNameIdx) != "Function") continue;
        samples.push_back(obj);
    }

    int best = 0;
    if (samples.size() >= 8) {
        const int need = static_cast<int>(samples.size()) - 1;  // tolerate 1 oddball
        for (int off = 0x80; off <= 0x158 && best == 0; off += 8) {
            int hits = 0;
            for (uintptr_t f : samples) {
                uintptr_t p = 0;
                if (Macht::ReadSafe(f + off, p) && Macht::LooksLikeCodePointer(p)) hits++;
            }
            if (hits >= need) best = off;
        }
    }
    DynOff::UFUNCTION_FUNC = best;
    DynOff::bUFunctionFuncDetected.store(true, std::memory_order_release);
    LOG_INFO("DetectUFunctionFuncOffset: %zu UFunction samples -> UFUNCTION_FUNC=+0x%X%s",
             samples.size(), best,
             best ? "" : " (NOT FOUND — Path 2 native analysis disabled)");
}

// Resolve a UFunction's native code entry point (UFunction->Func). For native
// (FUNC_Native) functions this is the execXxx thunk in .text — the address to
// disassemble; for Blueprint functions it points at the interpreter
// (ProcessInternal). Returns 0 if the Func offset isn't detected yet or the
// slot doesn't hold a code pointer. Used by the UI "Disassemble in CE" button
// (push to CE via AOBMaker) on the xref dialog.
// Read UFunction::FunctionFlags via the version-aware offset (mirrors the
// probe in Ubel::WalkFunctions): primary by UE version, then a fallback sweep.
static uint32_t ReadFunctionFlags(uintptr_t funcAddr) {
    int primary = (g_cachedUEVersion >= 550) ? 0xC0
                : (g_cachedUEVersion >= 425) ? 0xB0
                : (g_cachedUEVersion >= 421) ? 0x98 : 0x88;
    uint32_t flags = 0;
    if (Macht::ReadSafe<uint32_t>(funcAddr + primary, flags) && flags != 0) return flags;
    for (int tryOff : { 0xB0, 0xC0, 0x88, 0x98, 0xA8, 0xB8 }) {
        if (tryOff == primary) continue;
        if (Macht::ReadSafe<uint32_t>(funcAddr + tryOff, flags) && flags != 0) return flags;
    }
    return 0;
}

uintptr_t GetFunctionCodeAddr(uintptr_t funcAddr) {
    if (!funcAddr) return 0;
    EnsureUFunctionFuncOffset();
    if (DynOff::UFUNCTION_FUNC == 0) return 0;

    // Only NATIVE (FUNC_Native) functions have a per-function .text entry worth
    // disassembling — their Func points at the execXxx thunk. Blueprint/script
    // functions point Func at the SHARED interpreter (UObject::ProcessInternal),
    // which is not this function's code, so return 0 for them — the UI then shows
    // the honest "Blueprint-only" message instead of pushing the interpreter
    // dispatcher and reporting a misleading success.
    constexpr uint32_t FUNC_Native = 0x00000400;
    if ((ReadFunctionFlags(funcAddr) & FUNC_Native) == 0) return 0;

    uintptr_t exec = 0;
    if (!Macht::ReadSafe(funcAddr + DynOff::UFUNCTION_FUNC, exec) ||
        !Macht::LooksLikeCodePointer(exec))
        return 0;
    return exec;
}

// --- Path 2: disassemble a native UFunction and map [this+off] to props ---
//
// Returns the method tag ("disasm" when the decoder ran, "none" when the exec
// pointer couldn't be resolved/read). Fills out.refs with the field accesses
// that map to a property on the function's owning class. Heuristic: see Denken.
static std::string AnalyzeNativeFunctionProps(uintptr_t funcAddr,
                                              FunctionPropRefResult& out) {
    EnsureUFunctionFuncOffset();
    if (DynOff::UFUNCTION_FUNC == 0) return "none";

    // ⛔ NATIVE-ONLY GATE — the same one GetFunctionCodeAddr applies a few lines above,
    // for the reason spelled out there: a script/Blueprint UFunction points `Func` at the
    // SHARED interpreter (UObject::ProcessInternal), not at its own code. That is a
    // perfectly valid code pointer, so the LooksLikeCodePointer check below CANNOT tell
    // the two apart.
    //
    // Without this, a Blueprint function whose Script is unreadable reaches here — the
    // selector in WalkFunctionPropertyRefs routes on `Script` alone and never consults
    // FunctionFlags — and we disassemble the INTERPRETER, then map ITS [this+off]
    // accesses against the function's owner class. Any interpreter displacement that
    // collided with a real property offset would have been reported as a genuine
    // reference by that function. GetFunctionCodeAddr's comment names the failure exactly:
    // "pushing the interpreter dispatcher and reporting a misleading success".
    //
    // And the UI made it worse than silence: `method="disasm"` prints
    // "[native disasm — heuristic, N unmapped]", i.e. it CLAIMS to have disassembled the
    // function. Observed on DQ7R: `GetRemainingExpToNextLevel` (BC,BP,Const) reported
    // "0 properties … 2 unmapped" on the native path.
    //
    // ⚠ `!= 0` is load-bearing, and the gate is deliberately THREE-WAY. ReadFunctionFlags
    // returns 0 when every probe offset failed, i.e. "unknown" — NOT "not native". A plain
    // `(flags & FUNC_Native) == 0` would refuse every function on any build where the
    // FunctionFlags offset does not resolve, deleting Path 2 wholesale to fix a case we
    // have no evidence of there. So: refuse only when the flags were READ and say script.
    // The confirmed failure has readable flags by construction — the UI rendered
    // "BC,BP,Const" for it, and that string is derived from these very bits.
    constexpr uint32_t FUNC_Native = 0x00000400;
    const uint32_t funcFlags = ReadFunctionFlags(funcAddr);
    if (funcFlags != 0 && (funcFlags & FUNC_Native) == 0)
        return "blueprint_no_script";

    uintptr_t exec = 0;
    if (!Macht::ReadSafe(funcAddr + DynOff::UFUNCTION_FUNC, exec) ||
        !Macht::LooksLikeCodePointer(exec))
        return "none";

    // In-process SEH-safe reader: take the full window when readable, else read
    // byte-by-byte up to the first unreadable address (function tail / guard page).
    Denken::MemReader reader = [](uintptr_t addr, uint8_t* buf, size_t maxLen) -> size_t {
        if (Macht::ReadBytesSafe(addr, buf, maxLen)) return maxLen;
        size_t n = 0;
        for (; n < maxLen; ++n)
            if (!Macht::ReadBytesSafe(addr + n, buf + n, 1)) break;
        return n;
    };

    Denken::NativeAnalysisResult r = Denken::Analyze(exec, reader);
    if (!r.ok) return "none";

    // Carry the decoder's "I stopped early" flag OUT rather than only into the log
    // (audit #5 AF7). Set before the mapping loop so an early return can't drop it.
    out.budgetHit = r.budgetHit;

    // Map each [this+off] access to a property on the owning class (UFunction's
    // Outer). WalkClass already includes the full inherited super chain with
    // absolute Offset_Internal, so an offset matches at most one property.
    uintptr_t owner = Ubel::GetOuter(funcAddr);
    if (owner) {
        ClassInfo ci = Ubel::WalkClass(owner);
        std::unordered_map<int32_t, const FieldInfo*> byOff;
        byOff.reserve(ci.Fields.size());
        for (const auto& f : ci.Fields) byOff.emplace(f.Offset, &f);

        for (const auto& acc : r.accesses) {
            auto it = byOff.find(static_cast<int32_t>(acc.offset));
            if (it == byOff.end()) { out.unmappedAccesses++; continue; }
            const FieldInfo& f = *it->second;
            FunctionPropRef ref;
            ref.propAddr    = f.Address;
            ref.name        = f.Name;
            ref.type        = f.TypeName;
            ref.offset      = static_cast<int32_t>(acc.offset);
            ref.occurrences = acc.occurrences;
            ref.writeCount  = acc.writeCount;
            ref.scope       = "instance";   // mapped to a class member
            ref.confidence  = acc.highConfidence ? "high" : "low";
            out.refs.push_back(std::move(ref));
        }

        // High-confidence first, then writers, then frequency, then name.
        std::sort(out.refs.begin(), out.refs.end(),
                  [](const FunctionPropRef& a, const FunctionPropRef& b) {
            bool ah = a.confidence == "high", bh = b.confidence == "high";
            if (ah != bh) return ah;
            if ((a.writeCount > 0) != (b.writeCount > 0)) return a.writeCount > 0;
            if (a.occurrences != b.occurrences) return a.occurrences > b.occurrences;
            return a.name < b.name;
        });
    }

    LOG_INFO("AnalyzeNativeFunctionProps: 0x%llX exec=0x%llX -> %zu mapped props "
             "(%d unmapped, %d instrs, %d calls%s)",
             static_cast<unsigned long long>(funcAddr),
             static_cast<unsigned long long>(exec), out.refs.size(),
             out.unmappedAccesses, r.instrsDecoded, r.callsFollowed,
             r.budgetHit ? ", BUDGET" : "");
    return "disasm";
}

// --- WalkFunctionPropertyRefs: reverse edge (function -> properties) ---
FunctionPropRefResult WalkFunctionPropertyRefs(uintptr_t funcAddr) {
    FunctionPropRefResult out;
    if (!funcAddr) return out;

    uintptr_t scriptData = 0; int32_t scriptNum = 0;
    Macht::ReadSafe(funcAddr + DynOff::USTRUCT_SCRIPT,        scriptData);
    Macht::ReadSafe(funcAddr + DynOff::USTRUCT_SCRIPT + 0x08, scriptNum);
    if (!scriptData || scriptNum <= 0 || scriptNum > (1 << 22)) {
        // Empty/native bytecode → Path 2 x64 disassembly fallback (heuristic).
        out.method = AnalyzeNativeFunctionProps(funcAddr, out);
        return out;
    }
    out.scriptBytes = scriptNum;
    out.method = "bytecode";

    std::vector<uint8_t> buf(static_cast<size_t>(scriptNum));
    if (!Macht::ReadBytesSafe(scriptData, buf.data(), static_cast<size_t>(scriptNum))) return out;

    // Opcode-anchored property-reference scan. Anchors are the value-access
    // opcodes whose immediate operand is an FProperty* (8B):
    //   EX_LocalVariable 0x00 / InstanceVariable 0x01 / DefaultVariable 0x02 /
    //   LocalOutVariable 0x48 / ClassSparseDataVariable 0x6C /
    //   StructMemberContext 0x42 / LetValueOnPersistentFrame 0x64
    // Each candidate is confirmed via Ubel::ResolvePropertyNameType (rejects
    // non-property pointers). Deduped by propAddr; read/write via IsWriteContext.
    std::unordered_map<uintptr_t, size_t> idx;
    for (int32_t p = 0; p + 1 + 8 <= scriptNum; ++p) {
        uint8_t op = buf[p];
        if (op != 0x00 && op != 0x01 && op != 0x02 && op != 0x48
            && op != 0x6C && op != 0x42 && op != 0x64) continue;

        uintptr_t cand = 0;
        memcpy(&cand, &buf[p + 1], sizeof(cand));
        if (!LooksLikeHeapPtr(cand)) continue;

        std::string name, type;
        if (!Ubel::ResolvePropertyNameType(cand, name, type)) continue;

        // IsWriteContext is keyed on the pointer offset (p+1); 0x64 is itself a write.
        bool isWrite = (op == 0x64) || IsWriteContext(buf.data(), p + 1);

        auto it = idx.find(cand);
        if (it == idx.end()) {
            FunctionPropRef r;
            r.propAddr    = cand;
            r.name        = std::move(name);
            r.type        = std::move(type);
            r.occurrences = 1;
            r.writeCount  = isWrite ? 1 : 0;
            r.scope       = ScopeForOpcode(op);
            idx.emplace(cand, out.refs.size());
            out.refs.push_back(std::move(r));
        } else {
            auto& r = out.refs[it->second];
            r.occurrences++;
            if (isWrite) r.writeCount++;
            // Prefer the "instance" label if any access proves it's a class member.
            if (r.scope != "instance" && op == 0x01) r.scope = "instance";
        }
    }

    // Class members (instance) first — local BP temporaries are noise — then
    // writers, then by frequency, then name. Most actionable on top.
    std::sort(out.refs.begin(), out.refs.end(),
              [](const FunctionPropRef& a, const FunctionPropRef& b) {
        bool ai = a.scope == "instance", bi = b.scope == "instance";
        if (ai != bi) return ai;
        if ((a.writeCount > 0) != (b.writeCount > 0)) return a.writeCount > 0;
        if (a.occurrences != b.occurrences) return a.occurrences > b.occurrences;
        return a.name < b.name;
    });

    LOG_INFO("WalkFunctionPropertyRefs: 0x%llX -> %zu props (%d script bytes)",
             static_cast<unsigned long long>(funcAddr), out.refs.size(), scriptNum);
    return out;
}

SparseDelegateResult WalkSparseDelegateBindings(uintptr_t ownerObj,
                                                 const std::string& fieldName,
                                                 int32_t maxBindings)
{
    SparseDelegateResult result{};
    if (!ownerObj || fieldName.empty()) return result;

    // Layout gate. This USED to be a version check (`UEVersion < 500 -> unsupported`) on the
    // premise that "UE 4.23-4.27 keys the outer TMap by FObjectKey, not a raw pointer".
    // That premise is wrong for 4.27: the DropIn 4.27.2 PDB gives the global's type as
    //   TMap<UObjectBase const*, TMap<FName, TSharedPtr<TMulticastScriptDelegate<...>,0>>>
    // — a raw pointer key, same as UE 5.x (and vendor/UnrealEngine 5.8 declares it
    // identically). FObjectKey is also 8 bytes there, not the 16 the old note claimed.
    // Every other constant below was checked against that PDB and matches exactly.
    //
    // We still have NO symbol evidence for 4.23-4.26, so instead of widening the version
    // range on a guess, probe the actual key shape: the first occupied outer slot must hold
    // something that looks like a userspace pointer. An FObjectKey-keyed build stores two
    // small int32s there, which fails the test and lands us back on the bIsBound fallback —
    // i.e. unknown builds fail safe rather than misreading memory.
    uintptr_t storage = Genau::FindSparseDelegateStorage();
    if (!storage) return result;  // resolved=false

    TMapHeader outerHdr{};
    if (!ReadTMapHeader(storage, outerHdr)) return result;
    if (!outerHdr.arrayData || outerHdr.arrayNum == 0) {
        result.resolved = true;  // empty storage is a valid state
        return result;
    }

    constexpr int32_t kOuterStride = 0x60;
    constexpr int32_t kOuterValueOffset = 0x08;  // TPair value (inner TMap) starts after key

    {
        bool sawSlot = false, keyLooksLikePointer = false;
        for (int32_t i = 0; i < outerHdr.arrayNum && i < 64; ++i) {
            if (!TMapBitSet(outerHdr.bitArrayBase, i)) continue;
            uintptr_t k = 0;
            if (!Macht::ReadSafe(outerHdr.arrayData + static_cast<uintptr_t>(i) * kOuterStride, k))
                continue;
            sawSlot = true;
            if (Grimoire::IsUserspacePointer(k)) { keyLooksLikePointer = true; break; }
        }
        if (sawSlot && !keyLooksLikePointer) {
            LOG_WARN("WalkSparseDelegateBindings: outer key does not look like a raw pointer "
                     "(UE=%u) — refusing to walk (possible FObjectKey-keyed build)",
                     ::g_cachedUEVersion);
            result.supported = false;
            return result;
        }
    }

    // Phase 1: linear scan outer slots for matching owner key.
    uintptr_t innerMapAddr = 0;
    for (int32_t i = 0; i < outerHdr.arrayNum; ++i) {
        if (!TMapBitSet(outerHdr.bitArrayBase, i)) continue;  // freed slot
        uintptr_t slot = outerHdr.arrayData + static_cast<uintptr_t>(i) * kOuterStride;
        uintptr_t key = 0;
        if (!Macht::ReadSafe(slot, key)) continue;
        if (key == ownerObj) {
            innerMapAddr = slot + kOuterValueOffset;
            break;
        }
    }

    result.resolved = true;
    if (!innerMapAddr) return result;  // owner not in storage
    result.ownerFound = true;

    // Phase 2: linear scan inner TMap for matching FName key.
    TMapHeader innerHdr{};
    if (!ReadTMapHeader(innerMapAddr, innerHdr)) return result;
    if (!innerHdr.arrayData || innerHdr.arrayNum == 0) return result;

    int32_t fnameSize = DynOff::bCasePreservingName ? 0x10 : 0x08;
    int32_t innerStride = (fnameSize == 0x10 ? 0x28 : 0x20);
    int32_t sharedPtrOffset = fnameSize;  // TPair: FName at +0, TSharedPtr at +fnameSize

    uintptr_t sharedPtrAddr = 0;
    for (int32_t i = 0; i < innerHdr.arrayNum; ++i) {
        if (!TMapBitSet(innerHdr.bitArrayBase, i)) continue;
        uintptr_t slot = innerHdr.arrayData + static_cast<uintptr_t>(i) * innerStride;
        int32_t comp = 0;
        if (!Macht::ReadSafe(slot, comp)) continue;
        std::string keyStr = Serie::GetString(comp);
        if (keyStr == fieldName) {
            sharedPtrAddr = slot + sharedPtrOffset;
            break;
        }
    }
    if (!sharedPtrAddr) return result;  // FName not in inner map
    result.nameFound = true;

    // Phase 3: deref TSharedPtr, walk InvocationList: TArray<FScriptDelegate>.
    uintptr_t mcdAddr = 0;
    if (!Macht::ReadSafe(sharedPtrAddr, mcdAddr) || !mcdAddr) return result;

    // FMulticastScriptDelegate { TArray<FScriptDelegate> InvocationList; }
    uintptr_t invData = 0;
    int32_t   invNum  = 0;
    Macht::ReadSafe(mcdAddr + 0x00, invData);
    Macht::ReadSafe(mcdAddr + 0x08, invNum);
    if (invNum < 0 || invNum > 4096) invNum = 0;

    int32_t scriptDelegateSize = 8 + fnameSize;  // FWeakObjectPtr + FName
    int32_t readMax = std::min(invNum, maxBindings);
    result.bindings.reserve(readMax);
    for (int32_t i = 0; invData && i < readMax; ++i) {
        uintptr_t elemAddr = invData + static_cast<uintptr_t>(i) * scriptDelegateSize;
        SparseDelegateBinding b{};
        Macht::ReadSafe(elemAddr,     b.objectIndex);
        Macht::ReadSafe(elemAddr + 4, b.serialNumber);

        b.targetObj = Ubel::ResolveWeakObjectPtr(b.objectIndex, b.serialNumber);
        if (b.targetObj) {
            b.targetName = Ubel::GetName(b.targetObj);
            uintptr_t cls = Ubel::GetClass(b.targetObj);
            if (cls) b.targetClassName = Ubel::GetName(cls);
        }

        // FName at +8 (FWeakObjectPtr is always 8 bytes regardless of FName size)
        int32_t funcComp = 0;
        Macht::ReadSafe(elemAddr + 8, funcComp);
        b.functionName = Serie::GetString(funcComp);

        result.bindings.push_back(std::move(b));
    }

    return result;
}

// === Value Search (CE-style First Scan / Next Scan workflow) ===

// True when a container's inner/element/key/value property type matches the
// scan's accepted leaf types. For vector scans (acceptedStructNames non-empty)
// a StructProperty inner must additionally match an accepted struct name —
// mirrors the leaf + TArray-inner StructProperty filters. (V1a, build 927.)
static bool ContainerInnerAccepted(
    const std::string& innerType,
    const std::string& innerStructType,
    const std::vector<std::string>& acceptedTypes,
    const std::vector<std::string>& acceptedStructNames) {
    bool accepted = false;
    for (const auto& t : acceptedTypes) {
        if (innerType == t) { accepted = true; break; }
    }
    if (accepted && !acceptedStructNames.empty() && innerType == "StructProperty") {
        bool nameMatch = false;
        for (const auto& name : acceptedStructNames) {
            if (innerStructType == name) { nameMatch = true; break; }
        }
        accepted = nameMatch;
    }
    return accepted;
}

// Forward decl: the snapshot source-level noise verdict (defined below, near
// CaptureSnapshotChunk) is reused by the Value Search / Group Scan pre-filter so
// all three surfaces apply the SAME engine/system skip + gameplay guardrail.
static bool IsSnapshotNoiseClass(uintptr_t cls, const std::string& classPath);

ValueScanResult ScanForValue(
    Radar::DataType dt,
    Radar::ScanType st,
    const uint8_t*      targetBytes,
    const uint8_t*      target2Bytes,
    bool                gameOnly,
    int32_t             maxResults,
    Radar::RoundMode    roundMode,
    const std::string&  targetString,
    bool                caseSensitive,
    const Radar::NumericTargetSet* multiTargets,
    const Radar::NumericTargetSet* multiTargets2,
    bool                parallel,
    bool                batchRead,
    bool                deep,
    bool                nativeC,
    int32_t             nativeAlign,
    bool                newestFirst,
    int32_t             deadlineMs,
    bool                preFilterNoise)
{
    ValueScanResult result;
    auto t0 = std::chrono::steady_clock::now();
    const auto kDeadline = std::chrono::milliseconds(deadlineMs > 0 ? deadlineMs : 15000);

    const bool isString = Radar::IsStringDataType(dt);
    const bool isVector = Radar::IsVectorDataType(dt);
    const bool isMulti  = Radar::IsMultiNumericDataType(dt);
    const size_t dtSize = Radar::SizeOf(dt);

    // Native-C raw-hole scan is numeric-only: a raw byte range can't sensibly be
    // an FString/FName/FText/FVector/bool-bitfield, so the native pass is a no-op
    // for those dt families (the reflected scan is unaffected). Eligible = a
    // concrete fixed-width numeric (1..8B, excluding Bool) or a multi-numeric meta.
    const bool nativeEligible = nativeC &&
        (isMulti || (!isString && !isVector && dt != Radar::DataType::Bool
                     && dtSize >= 1 && dtSize <= 8));
    // Stride for sliding within a hole. Clamp to {1,2,4,8}; default/garbage -> 4.
    int32_t nativeStride = (nativeAlign == 1 || nativeAlign == 2
                            || nativeAlign == 4 || nativeAlign == 8) ? nativeAlign : 4;

    // Validate inputs per type family.
    if (isString) {
        // String scans take the user's needle on targetString; the byte
        // buffers are unused. Caller must pass a non-empty target for
        // targeted predicates (substring matchers + Exact); Changed /
        // Unchanged use the candidate's prevStr.
        if (!Radar::IsPrevValueScanType(st) && targetString.empty()) return result;
    } else if (isMulti) {
        // Multi-numeric meta scan: the pre-parsed per-width target set
        // replaces targetBytes. First scan requires it (prev-value scan
        // types never reach here — rejected below).
        if (!multiTargets || multiTargets->entries.empty()) return result;
        if (st == Radar::ScanType::Between
            && (!multiTargets2 || multiTargets2->entries.empty())) return result;
    } else if (isVector) {
        // Vector targets arrive in the CANONICAL form — 3 doubles in
        // targetBytes — because the per-FIELD source width (12 or 24) is not
        // known until the index is built and differs between fields of one
        // scan. SizeOf() is 0 for vectors for exactly that reason, so the
        // dtSize check below would reject every vector scan.
        if (!targetBytes) return result;
        if (st == Radar::ScanType::Between && !target2Bytes) return result;
    } else {
        if (dtSize == 0 || !targetBytes) return result;
        if (st == Radar::ScanType::Between && !target2Bytes) return result;
    }
    // Prev-value scan types have no meaning on a first scan -- caller (pipe
    // handler) is responsible for rejecting these, but be defensive.
    if (Radar::IsPrevValueScanType(st)) return result;

    // Decode the vector target(s) ONCE. Every per-candidate compare then runs
    // against doubles, so a 12-byte field and a 24-byte field in the same scan
    // are both compared against the same target without re-encoding it.
    double targetVec[3]  = { 0.0, 0.0, 0.0 };
    double targetVec2[3] = { 0.0, 0.0, 0.0 };
    if (isVector) {
        Radar::DecodeVectorBytes(targetBytes, Radar::VECTOR_CANON_BYTES, targetVec);
        if (target2Bytes)
            Radar::DecodeVectorBytes(target2Bytes, Radar::VECTOR_CANON_BYTES, targetVec2);
    }
    const double* targetVec2Ptr = target2Bytes ? targetVec2 : nullptr;

    const auto& acceptedTypes = Radar::PropertyTypeNames(dt);
    // Vector types match by StructProperty + inner struct name (e.g.
    // "Vector", "Vector3f"). Empty for non-vector dt so the inner-name
    // check is skipped.
    const auto& acceptedStructNames = Radar::VectorStructNames(dt);

    // Per-class field index. classAddr -> filtered subset of FieldInfo
    // that match the requested DataType. Built lazily on first
    // encounter; reused across all instances of that class.
    //
    // Phase 2C extends this with an "isArray" flag — when set, the
    // ScanField represents an ArrayProperty whose Inner matches the
    // requested DataType. The per-instance loop reads the TArray
    // header (Data ptr, Num, Max) and emits ONE candidate per matching
    // element. elemStride captures the per-element size in bytes;
    // size still refers to the field-level size (16B TArray header).
    // Which container a ScanField walks (None = direct scalar field).
    // Array/Set emit one value per element; MapKey/MapValue emit the key
    // or value half of each TPair (build 927, V1a).
    // StructArrayInner: a LEAF field that lives inside the element struct of a
    // TArray<FStruct> (e.g. SaveSlotList[1].GP). The TArray header is at `offset`
    // within the object; each element struct is `elemStride` bytes; the leaf sits
    // at `structInnerOffset` within the element. Scanned per element at
    // arrayData + idx*elemStride + structInnerOffset. (build 1201)
    enum class ScanContainer : uint8_t { None, Array, Set, MapKey, MapValue, StructArrayInner };
    struct ScanField {
        int32_t       offset;
        int32_t       size;
        std::string   name;
        std::string   typeName;
        uint8_t       boolFieldMask;
        ScanContainer container    = ScanContainer::None;
        int32_t       elemStride   = 0;   // Array elem / Set elem / Map pair stride / struct-array elem
        int32_t       valueOffset  = 0;   // MapValue: byte offset of value within the TPair
        int32_t       structInnerOffset = 0;  // StructArrayInner: leaf offset within the element struct
        // V1c: TOptional<T> unset gate. >= 0 → this leaf is a TOptional whose
        // wrapped value is at offset+0 with a trailing bIsSet byte at
        // offset+optionalFlagOffset; the per-instance read skips slots whose
        // flag is 0 (unset). -1 = ordinary leaf / container (no gate).
        int32_t       optionalFlagOffset = -1;
        std::string   elemTypeName;       // Inner/elem/key/value type name (e.g. "IntProperty")
        // Vector scans only: the REFLECTED width of the value this ScanField
        // reads — 12 (3xfloat) or 24 (3xdouble LWC). Sourced from the property
        // that actually holds the triple, which is NOT the same field for every
        // container shape: a leaf uses its own ElementSize, a TArray/TSet its
        // inner element size (`size` there is the 16-byte container header, and
        // `elemStride` for a TSet is the padded sparse-array slot), and a TMap
        // the key or value half of the pair. 0 for non-vector scans.
        int32_t       vectorWidth = 0;
        // V3-A: lazily-resolved index into the worker thread's
        // FieldDescriptor pool (the field's interned class/defining-class/
        // name/type/offset/mask). -1 until the first candidate of this
        // field is emitted, then reused by every element + every instance.
        int32_t       descriptorIdx = -1;
    };
    struct ScanClassInfo {
        std::string             className;
        std::string             classPath;
        bool                    gameClass = false;   // !IsEnginePackage(classPath)
        bool                    noiseClass = false;  // pre-filter: engine/system noise
                                                     // (only computed when preFilterNoise)
        // The static field index for this class hit kMaxScanFieldsPerClass, so
        // "already covered by the static paths" is no longer true for it and the
        // deep pass must not skip anything on that basis (audit #5 A4).
        bool                    fieldCapHit = false;
        std::vector<ScanField>  fields;
        // Per-object batch-read plan (build 974). The body span covering this
        // class's DIRECT fixed-width leaf fields (container == None) — the reads
        // that can be served from ONE object-body read instead of one SEH read
        // per field. String fields and container DATA live in separate heap
        // allocations, so they're excluded and always read directly.
        // batchSpan == 0 → nothing worth batching (computed in buildClassIndex).
        int32_t                 batchMin       = 0;   // min body-leaf offset from obj
        int32_t                 batchSpan      = 0;   // (max offset+16) - batchMin; 0 = none
        int32_t                 bodyFieldCount = 0;   // # of container==None leaves
        // build 1206: this class has a struct-element container whose element
        // struct itself holds containers — so a value can live > 1 container
        // level deep (e.g. SaveSlotList[1]…Tunes[N]). When set, the per-instance
        // scan runs the recursive WalkContainerLeaves pass after the static
        // fields (the static StructArrayInner path covers only depth 1).
        bool                    needsDeepWalk  = false;
        // Native-C (P1): the unmanaged byte ranges of this class (complement of
        // reflected coverage within [winStart, winEnd)). Computed once per class
        // (layout is build-stable) and reused by every instance. Empty unless the
        // native scan is enabled. winTruncated = PropertiesSize exceeded the 64KB
        // sanity cap so members past winEnd weren't scanned.
        std::vector<Ubel::Interval> nativeHoles;
        int32_t                 nativeWinStart = 0;
        int32_t                 nativeWinEnd   = 0;
        bool                    nativeTruncated = false;
    };
    // DefKey caches FindDefiningClass results per (classAddr, fieldOffset)
    // so a hot scan over many instances of the same class doesn't re-walk
    // the SuperStruct chain on every candidate emission. (Defined here at
    // function scope so the per-thread worker below can hold one locally.)
    struct DefKey {
        uintptr_t classAddr;
        int32_t   offset;
        bool operator==(const DefKey& o) const {
            return classAddr == o.classAddr && offset == o.offset;
        }
    };
    struct DefKeyHash {
        size_t operator()(const DefKey& k) const {
            return std::hash<uintptr_t>{}(k.classAddr)
                 ^ (std::hash<int32_t>{}(k.offset) << 1);
        }
    };

    const int32_t count = GetCount();

    // Per-object batch-read gate (build 974). Reading the object's leaf-field
    // span once and serving fields from the buffer only pays off when there are
    // enough fields to amortize the read AND they're packed densely enough that
    // the unused gaps don't out-cost the SEH reads they replace:
    //   - kMinBatchFields: need at least this many container==None leaves.
    //   - kMaxBatchSpan:   absolute cap so a huge object never triggers a giant
    //                      read (also bounds the per-thread scratch buffer).
    //   - kBatchBytesPerField: density cap — span must be <= fields * this, so a
    //                      few fields spread far apart fall back to per-field.
    // Tunable; validated by live First-Scan timing. Buffer cost is
    // (worker threads) x (<= kMaxBatchSpan), reused per object — a few MB max.
    constexpr int32_t kMinBatchFields     = 4;
    constexpr int32_t kMaxBatchSpan       = 64 * 1024;
    constexpr int32_t kBatchBytesPerField = 512;

    LOG_INFO("Radar: First Scan dt=%s st=%d (target %zuB, gameOnly=%d, max=%d, parallel=%d, batch=%d) over %d objects",
             Radar::NameOf(dt), static_cast<int>(st), dtSize,
             gameOnly ? 1 : 0, maxResults, parallel ? 1 : 0, batchRead ? 1 : 0, count);

    // Per-thread output of the parallel GObjects walk. Each thread owns its
    // own caches + candidate buffer (lock-free hot path); results merge in
    // ascending tid order so the global candidate list stays ascending by
    // object index — identical ordering to the old serial walk.
    struct ThreadResult {
        std::vector<Radar::Candidate>        candidates;   // indices are THREAD-LOCAL
        std::vector<Radar::FieldDescriptor>  descriptors;  // thread-local pool
        std::vector<Radar::InstanceRecord>   instances;    // thread-local pool
        int32_t                                  scannedObjects = 0;
        std::unordered_set<uintptr_t>            classesWithFields;
    };

    auto scan = ParallelGObjectsScan<ThreadResult>(count,
        [&](ThreadResult& tr, int32_t beginIdx, int32_t endIdx,
            std::atomic<bool>& deadlineHit) {

        // Thread-local per-class field index. classAddr -> filtered subset of
        // FieldInfo matching the requested DataType. Built lazily on first
        // encounter; reused across all instances of that class within this
        // thread's chunk. (Threads may redundantly build the same class index;
        // that cost is negligible vs. the GObjects walk and avoids any locking.)
        std::unordered_map<uintptr_t, ScanClassInfo> classCache;

        // Reused per-object batch-read scratch (build 974). One buffer per
        // worker thread, resized/overwritten per object — so the batch read
        // costs (threads x <= kMaxBatchSpan), independent of object count.
        std::vector<uint8_t> objBodyBuf;

        // Native-C (P1): reused per-object window buffer for the unmanaged-hole
        // scan ([winStart, winEnd) read once, probed in-buffer). Thread-local.
        std::vector<uint8_t> nativeBuf;

    // Recursive struct expansion: walks a UStruct's FProperty chain and
    // emits ScanField entries for every leaf property matching the target
    // DataType, including fields nested inside StructProperty members.
    //
    // Critical for GAS / Gameplay Ability System games: the most common
    // pattern is `UAttributeSet -> FGameplayAttributeData MaximumHealth ->
    // float BaseValue / float CurrentValue`. Without recursion the scan
    // sees only the outer StructProperty (which isn't a leaf type) and
    // returns 0 candidates -- the original 2026-05-26 TQ2 repro that
    // motivated this fix. Same applies to FVector / FRotator / FTransform
    // members of any UObject.
    //
    // Struct-array descent (build 1201): collect LEAF fields inside the element
    // struct of a TArray<FStruct> so a value held inside an element (e.g.
    // SaveSlotList[1].GP) is found by VALUE. Emits one StructArrayInner ScanField
    // per leaf, carrying the outer array field offset + the leaf's element-
    // relative offset (through direct sub-structs). One struct-array level only:
    // containers nested inside the element (TArray/TSet/TMap) are SEPARATE
    // allocations and intentionally NOT followed — the by-address deep scan
    // (FindInContainersDeep) covers those. Direct-struct nesting inside the
    // element IS followed (depth-capped).
    auto collectStructArrayInner = [&](auto& self,
                                       uintptr_t innerStructAddr,
                                       int32_t   arrayFieldOffset,
                                       int32_t   elemStride,
                                       int32_t   elemRelOffset,
                                       const std::string& namePrefix,
                                       std::vector<ScanField>& out,
                                       int depth) -> void {
        constexpr int kMaxStructDepth = 4;   // direct-struct nesting inside the element
        if (depth > kMaxStructDepth || !innerStructAddr) return;
        const ClassInfo& ci = Ubel::WalkClassEx(innerStructAddr);
        for (const auto& f : ci.Fields) {
            bool accepted = false;
            for (const auto& t : acceptedTypes) {
                if (f.TypeName == t) { accepted = true; break; }
            }
            if (accepted) {
                ScanField sf;
                sf.offset           = arrayFieldOffset;        // TArray header within the object
                sf.size             = f.Size;
                sf.name             = namePrefix + "." + f.Name;
                sf.typeName         = f.TypeName;
                sf.boolFieldMask    = f.boolFieldMask;
                sf.container        = ScanContainer::StructArrayInner;
                sf.elemStride       = elemStride;
                sf.structInnerOffset = elemRelOffset + f.Offset;
                sf.elemTypeName     = f.TypeName;              // descriptor type + multiResolve
                out.push_back(std::move(sf));
                continue;
            }
            // Direct sub-struct: recurse, accumulating the element-relative offset.
            if (f.TypeName == "StructProperty" && f.Address) {
                uintptr_t nested = 0;
                if (Macht::ReadSafe(f.Address + DynOff::FSTRUCTPROP_STRUCT, nested) && nested) {
                    self(self, nested, arrayFieldOffset, elemStride,
                         elemRelOffset + f.Offset, namePrefix + "." + f.Name, out, depth + 1);
                }
            }
        }
    };

    // Cycle / pathological-depth guards:
    //   - kMaxDepth = 4. Real UE structs rarely nest beyond 2-3 levels;
    //     beyond 4 we're either in a recursive type loop or pathological
    //     data. The cap bounds worst-case CPU per class.
    //   - the guard set is scoped to the ACTIVE PATH (Aura::StructPathGuard),
    //     so a self-referential struct still terminates while two sibling
    //     fields of the SAME struct type are both expanded. It used to be a
    //     whole-walk set with no erase, which silently meant "only the first
    //     FVector in a class is ever indexed" — Location found, Velocity /
    //     Scale3D / Extent not, subtree and all (audit A3). The two walkers
    //     that got this right are CollectSchemaLeaves and CollectGroupLeaves.
    //   - kMaxScanFieldsPerClass, because path-scoping deliberately removes the
    //     accidental bound the whole-walk set was providing. Without it the only
    //     limit left is depth<=4 on a tree whose fan-out is (fields per struct)^4.
    //     Mirrors kMaxSchemaLeavesPerClass, which its sibling pairs with the same
    //     path-scoped guard for exactly this reason.
    constexpr size_t kMaxScanFieldsPerClass = 4000;
    bool fieldCapHit = false;
    auto expandFields = [&](auto& self,
                            uintptr_t structAddr,
                            int32_t   baseOffset,
                            const std::string& namePrefix,
                            std::vector<ScanField>& out,
                            std::unordered_set<uintptr_t>& visited,
                            int depth) -> void {
        constexpr int kMaxDepth = 4;
        if (depth > kMaxDepth) return;
        if (out.size() >= kMaxScanFieldsPerClass) { fieldCapHit = true; return; }
        // Cycle guard along the CURRENT path only — RAII so every early return
        // below unwinds it. A plain insert-without-erase is the A3 defect.
        Aura::StructPathGuard pathGuard(visited, structAddr);
        if (!pathGuard.Entered()) return;  // already on this path: real cycle

        // Use WalkClassEx at every depth so BoolProperty FieldMask is
        // populated for nested bitfield bools (WalkClass alone covers it
        // on the UE4 UProperty path only; the UE5 FProperty path needs
        // the WalkClassEx pass). The extra metadata reads we don't use
        // are cheap relative to the GObjects walk itself.
        const ClassInfo& ci = Ubel::WalkClassEx(structAddr);
        for (const auto& f : ci.Fields) {
            if (out.size() >= kMaxScanFieldsPerClass) { fieldCapHit = true; break; }
            // Leaf-type match: emit a ScanField at the cumulative offset.
            bool accepted = false;
            for (const auto& t : acceptedTypes) {
                if (f.TypeName == t) { accepted = true; break; }
            }

            // Vector data types additionally require the inner struct
            // name to match (e.g. "Vector" / "Vector3f"). This both
            // filters out unrelated StructProperty fields (which are
            // numerous) and avoids the cost of reading a triple from
            // every non-vector struct on every scan.
            //
            // The name is only half the gate: it says "X/Y/Z triple", not how
            // wide. The property's own reflected ElementSize settles that (12 =
            // 3xfloat, 24 = 3xdouble LWC) and a size that is neither is refused
            // here rather than read at a guessed width. (audit #5 AB3)
            int32_t vecWidth = 0;
            if (accepted && !acceptedStructNames.empty() && f.TypeName == "StructProperty") {
                bool nameMatch = false;
                for (const auto& name : acceptedStructNames) {
                    if (f.structType == name) { nameMatch = true; break; }
                }
                accepted = nameMatch && Radar::IsSupportedVectorWidth(f.Size);
                if (accepted) vecWidth = f.Size;
            }

            if (accepted) {
                ScanField sf;
                sf.offset        = baseOffset + f.Offset;
                sf.size          = f.Size;
                sf.name          = namePrefix.empty() ? f.Name : (namePrefix + "." + f.Name);
                sf.typeName      = f.TypeName;
                sf.boolFieldMask = f.boolFieldMask;
                sf.vectorWidth   = vecWidth;
                out.push_back(std::move(sf));
                continue;
            }

            // Phase 2C: TArray<T> container scan. When the inner
            // FProperty's type matches the requested DataType, emit a
            // ScanField with container=Array + the per-element stride.
            // The per-instance loop branches on sf.container to walk the
            // TArray buffer.
            //
            // Vector inner types additionally require innerStructType
            // to match the accepted struct names (mirrors the leaf
            // StructProperty filter).
            if (f.TypeName == "ArrayProperty" && !f.innerType.empty()) {
                bool innerAccepted = false;
                for (const auto& t : acceptedTypes) {
                    // Inner matches a leaf-property type the scan
                    // wants, but vectors also need StructProperty inner
                    // (and the struct name check below).
                    if (f.innerType == t) {
                        innerAccepted = true;
                        break;
                    }
                }
                if (innerAccepted && !acceptedStructNames.empty()
                    && f.innerType == "StructProperty") {
                    bool nameMatch = false;
                    for (const auto& name : acceptedStructNames) {
                        if (f.innerStructType == name) { nameMatch = true; break; }
                    }
                    innerAccepted = nameMatch;
                }
                if (innerAccepted) {
                    int32_t stride = Ubel::GetArrayInnerElemSize(f.Address);
                    // For TArray<FVector> the inner element size IS the triple's
                    // width — elements are packed, so stride == the vector.
                    // (`f.Size` here is the 16-byte TArray header, not the value.)
                    int32_t arrVecWidth = 0;
                    if (!acceptedStructNames.empty()) {
                        if (!Radar::IsSupportedVectorWidth(stride)) stride = 0;  // refuse
                        else arrVecWidth = stride;
                    }
                    // Skip arrays whose inner-element size couldn't be
                    // resolved (rare; defensive). Without stride we
                    // can't iterate safely.
                    if (stride > 0) {
                        ScanField sf;
                        sf.offset        = baseOffset + f.Offset;
                        sf.size          = f.Size;
                        sf.name          = namePrefix.empty()
                            ? f.Name : (namePrefix + "." + f.Name);
                        sf.typeName      = "ArrayProperty";
                        sf.boolFieldMask = 0xFF;
                        sf.container     = ScanContainer::Array;
                        sf.elemStride    = stride;
                        sf.elemTypeName  = f.innerType;
                        sf.vectorWidth   = arrVecWidth;
                        out.push_back(std::move(sf));
                        continue;
                    }
                }

                // TArray<FStruct>: descend into the element struct's leaf fields
                // so a value inside an element (SaveSlotList[1].GP) is found by
                // value. Non-vector scans only (vectors want whole-struct matches,
                // handled by the inner-name check above). (build 1201)
                if (acceptedStructNames.empty() && f.innerType == "StructProperty") {
                    uintptr_t innerStruct = Ubel::GetContainerInnerStructAddr(f.Address);
                    int32_t   stride      = Ubel::GetArrayInnerElemSize(f.Address);
                    if (innerStruct && stride > 0) {
                        std::string arrName = namePrefix.empty()
                            ? f.Name : (namePrefix + "." + f.Name);
                        // "[]" placeholder marks where the element index goes at
                        // display time → "SaveSlotList[3].GP" (FieldDisplayName).
                        collectStructArrayInner(collectStructArrayInner, innerStruct,
                                                baseOffset + f.Offset, stride,
                                                /*elemRelOffset*/ 0, arrName + "[]", out, /*depth*/ 0);
                        continue;
                    }
                }
            }

            // V1c: TOptional<T> scan. FOptionalProperty stores the wrapped
            // value inline at field+0 (same Inner probe as TArray), with a
            // trailing bIsSet byte for non-intrusive optionals. When the inner
            // type matches the requested DataType, emit a LEAF ScanField at
            // field+0 (read identically to a direct field by the per-instance
            // loop) plus the bIsSet gate offset so unset slots are skipped.
            // innerType / innerStructType come from WalkClassEx.
            if (f.TypeName == "OptionalProperty" && !f.innerType.empty()) {
                bool innerAccepted = false;
                for (const auto& t : acceptedTypes) {
                    if (f.innerType == t) { innerAccepted = true; break; }
                }
                // Vector inner additionally requires the struct name to match
                // (mirrors the leaf + TArray-inner StructProperty filter).
                if (innerAccepted && !acceptedStructNames.empty()
                    && f.innerType == "StructProperty") {
                    bool nameMatch = false;
                    for (const auto& name : acceptedStructNames) {
                        if (f.innerStructType == name) { nameMatch = true; break; }
                    }
                    innerAccepted = nameMatch;
                }
                if (innerAccepted) {
                    // FOptionalProperty shares TArray's Inner-at-FARRAYPROP_INNER
                    // shape, so GetArrayInnerElemSize yields sizeof(T) here.
                    int32_t innerSize = Ubel::GetArrayInnerElemSize(f.Address);
                    // TOptional<FVector>: the width is the WRAPPED type's, not
                    // f.Size — which is the optional's whole footprint and
                    // includes the trailing bIsSet byte plus padding.
                    int32_t optVecWidth = 0;
                    if (!acceptedStructNames.empty()) {
                        if (!Radar::IsSupportedVectorWidth(innerSize)) continue;  // refuse
                        optVecWidth = innerSize;
                    }
                    ScanField sf;
                    sf.offset        = baseOffset + f.Offset;   // value at field+0
                    sf.size          = f.Size;
                    sf.name          = namePrefix.empty()
                        ? f.Name : (namePrefix + "." + f.Name);
                    sf.typeName      = f.innerType;             // read as the inner leaf type
                    sf.boolFieldMask = 0xFF;                    // optionals never bitfield-pack
                    sf.optionalFlagOffset =
                        Radar::OptionalFlagOffset(f.Size, innerSize);
                    sf.vectorWidth   = optVecWidth;
                    out.push_back(std::move(sf));
                    continue;
                }
            }

            // V1a: TSet<T> container scan. The element type must match the
            // requested DataType (+ struct name for vectors). Per-instance
            // loop walks the FSetProperty's TSparseArray (allocated slots
            // only); each element is read at slot+0.
            if (f.TypeName == "SetProperty" && !f.elemType.empty()
                && ContainerInnerAccepted(f.elemType, f.elemStructType,
                                          acceptedTypes, acceptedStructNames)) {
                int32_t stride = Ubel::GetSetElementStride(f.Address);
                // TSet<FVector>: the SLOT stride is padded out by the sparse
                // array's hash bookkeeping, so it is NOT the triple's width —
                // the element itself sits at slot+0 with its own element size.
                // FSetProperty's ElementProp lives at the same offset as
                // FArrayProperty's Inner (GetSetElementStride probes exactly
                // there), so the array helper reads the right property.
                int32_t setVecWidth = 0;
                if (!acceptedStructNames.empty()) {
                    int32_t elemSize = Ubel::GetArrayInnerElemSize(f.Address);
                    if (!Radar::IsSupportedVectorWidth(elemSize)) stride = 0;  // refuse
                    else setVecWidth = elemSize;
                }
                if (stride > 0) {
                    ScanField sf;
                    sf.offset        = baseOffset + f.Offset;
                    sf.size          = f.Size;
                    sf.name          = namePrefix.empty()
                        ? f.Name : (namePrefix + "." + f.Name);
                    sf.typeName      = "SetProperty";
                    sf.boolFieldMask = 0xFF;
                    sf.container     = ScanContainer::Set;
                    sf.elemStride    = stride;
                    sf.elemTypeName  = f.elemType;
                    sf.vectorWidth   = setVecWidth;
                    out.push_back(std::move(sf));
                    // fall through is unnecessary; a SetProperty is never
                    // also a leaf/array/struct, so continue.
                    continue;
                }
            }

            // V1a: TMap<K,V> container scan. Key and value are scanned
            // independently: a TMap<int,int> with dt=Int32 emits BOTH a
            // MapKey and a MapValue ScanField. Per-instance loop walks the
            // FMapProperty's TSparseArray of TPair; key at pair+0, value at
            // pair+valueOffset.
            if (f.TypeName == "MapProperty"
                && (!f.keyType.empty() || !f.valueType.empty())) {
                const bool keyOk = !f.keyType.empty()
                    && ContainerInnerAccepted(f.keyType, f.keyStructType,
                                              acceptedTypes, acceptedStructNames);
                const bool valOk = !f.valueType.empty()
                    && ContainerInnerAccepted(f.valueType, f.valueStructType,
                                              acceptedTypes, acceptedStructNames);
                if (keyOk || valOk) {
                    Ubel::MapPairLayout layout;
                    if (Ubel::GetMapPairLayout(f.Address, layout) && layout.pairStride > 0) {
                        const std::string base = namePrefix.empty()
                            ? f.Name : (namePrefix + "." + f.Name);
                        // A TMap half's vector width is that half's own size
                        // from the pair layout — the pair stride spans both
                        // halves plus the sparse-array bookkeeping.
                        const bool vecScan = !acceptedStructNames.empty();
                        if (keyOk && (!vecScan || Radar::IsSupportedVectorWidth(layout.keySize))) {
                            ScanField sf;
                            sf.offset        = baseOffset + f.Offset;
                            sf.size          = f.Size;
                            sf.name          = base + ".Key";
                            sf.typeName      = "MapProperty";
                            sf.boolFieldMask = 0xFF;
                            sf.container     = ScanContainer::MapKey;
                            sf.elemStride    = layout.pairStride;
                            sf.valueOffset   = 0;
                            sf.elemTypeName  = f.keyType;
                            sf.vectorWidth   = vecScan ? layout.keySize : 0;
                            out.push_back(std::move(sf));
                        }
                        if (valOk && (!vecScan || Radar::IsSupportedVectorWidth(layout.valueSize))) {
                            ScanField sf;
                            sf.offset        = baseOffset + f.Offset;
                            sf.size          = f.Size;
                            sf.name          = base + ".Value";
                            sf.typeName      = "MapProperty";
                            sf.boolFieldMask = 0xFF;
                            sf.container     = ScanContainer::MapValue;
                            sf.elemStride    = layout.pairStride;
                            sf.valueOffset   = layout.valueOffset;
                            sf.elemTypeName  = f.valueType;
                            sf.vectorWidth   = vecScan ? layout.valueSize : 0;
                            out.push_back(std::move(sf));
                        }
                        continue;
                    }
                }
            }

            // StructProperty: resolve the inner UScriptStruct via
            // FStructProperty::Struct (FField + FSTRUCTPROP_STRUCT) and
            // recurse with the cumulative offset + dotted name prefix.
            // TOptional<T> whose value directly matches the DataType
            // (TOptional<int>/<float>/<FVector>/<FString>) is scanned via the
            // dedicated OptionalProperty branch above (V1c); drilling INTO an
            // optional's wrapped struct for nested leaves is a further step
            // and is intentionally not recursed here.
            //
            // For vector data types, skip recursion -- we only want
            // leaves whose own type IS the vector struct, not nested
            // structs that happen to contain a vector. This matches the
            // CE-style cheat workflow (find an FVector at a known
            // location, not all FVectors-anywhere-inside).
            if (acceptedStructNames.empty()
                && f.TypeName == "StructProperty" && f.Address) {
                uintptr_t nested = 0;
                if (Macht::ReadSafe(f.Address + DynOff::FSTRUCTPROP_STRUCT, nested) && nested) {
                    std::string childPrefix = namePrefix.empty()
                        ? f.Name : (namePrefix + "." + f.Name);
                    self(self, nested, baseOffset + f.Offset, childPrefix, out, visited, depth + 1);
                }
            }
        }
    };

    auto buildClassIndex = [&](uintptr_t classAddr) -> ScanClassInfo* {
        auto it = classCache.find(classAddr);
        if (it != classCache.end()) return &it->second;

        ScanClassInfo sci;
        // Two passes: first WalkClassEx for the class metadata (Name +
        // FullPath are populated by Ubel::WalkClass already, but
        // WalkClassEx also populates structType / inner / enum metadata
        // we want available). Then expandFields walks the property chain
        // recursively for ScanField emission.
        const ClassInfo& ci = Ubel::WalkClassEx(classAddr);
        sci.className = ci.Name;
        sci.classPath = ci.FullPath;
        sci.gameClass = !IsEnginePackage(ci.FullPath);
        // Pre-filter "Auto detect Engine/System noise": compute the source-level
        // skip verdict once per class (reusing the snapshot helper so all surfaces
        // agree + its gameplay guardrail force-keeps Pawn/Actor/component/...).
        // Only when the toggle is on, so the OFF path pays no super-chain walk.
        sci.noiseClass = preFilterNoise && IsSnapshotNoiseClass(classAddr, ci.FullPath);

        std::unordered_set<uintptr_t> visited;
        fieldCapHit = false;
        expandFields(expandFields, classAddr, /*baseOffset=*/0,
                     /*namePrefix=*/"", sci.fields, visited, /*depth=*/0);
        // Never truncate silently. A missing leaf is indistinguishable from a
        // value that is not there, which is precisely how A3 stayed invisible
        // for ~2400 builds — the scan simply reported no match.
        sci.fieldCapHit = fieldCapHit;
        if (fieldCapHit) {
            LOG_WARN("ScanForValue: class '%s' hit the %zu scan-field cap — "
                     "deeper struct leaves were NOT indexed for this scan",
                     sci.className.c_str(), kMaxScanFieldsPerClass);
        }

        // Precompute the per-object batch-read span over DIRECT fixed-width
        // leaf fields (container == None). Each such field reads at most 16B
        // (readBuf) from obj+offset; a TOptional also reads its flag byte at
        // offset+optionalFlagOffset. Strings/containers chase pointers
        // elsewhere, so they're excluded. The caller's gate decides whether the
        // span is worth batching; here we just record it.
        {
            constexpr int32_t kLeaf = 16;   // readBuf max per leaf
            int32_t lo = 0, hi = 0, n = 0;
            bool first = true;
            for (const auto& f : sci.fields) {
                if (f.container != ScanContainer::None) continue;
                if (f.offset < 0) continue;   // defensive: corrupt offset
                int32_t end = f.offset + kLeaf;
                if (f.optionalFlagOffset >= 0)
                    end = std::max(end, f.offset + f.optionalFlagOffset + 1);
                if (first) { lo = f.offset; hi = end; first = false; }
                else { if (f.offset < lo) lo = f.offset; if (end > hi) hi = end; }
                ++n;
            }
            if (n > 0 && hi > lo) {
                sci.batchMin       = lo;
                sci.batchSpan      = hi - lo;
                sci.bodyFieldCount = n;
            }
        }

        // build 1206: does a value live > 1 container level deep? True when a
        // top-level container's element struct itself has containers (one level
        // of look-ahead via the cached GetClassContainers). When false the deep
        // walk is skipped entirely — no per-instance cost for the common case.
        for (const auto& cfe : GetClassContainers(classAddr)) {
            uintptr_t es = cfe.elemStruct ? cfe.elemStruct
                         : cfe.valueStruct ? cfe.valueStruct : cfe.keyStruct;
            if (es && !GetClassContainers(es).empty()) { sci.needsDeepWalk = true; break; }
        }
        // Opt-in "Deep": force the recursive container pass on every class (not just
        // the auto-detected struct-element-container nesting), so a value buried in
        // a container the heuristic doesn't flag is still reached.
        if (deep && !GetClassContainers(classAddr).empty()) sci.needsDeepWalk = true;

        // Native-C (P1): precompute this class's unmanaged holes once. Window =
        // [UObject header end, PropertiesSize) — PropertiesSize is the exact end
        // of the reflected region, so every native member lives below it. Sanity-
        // clamp huge/garbage PropertiesSize (packed / non-standard engines):
        // > 64KB -> clamp + flag truncated; <= header -> conservative 1KB window.
        if (nativeEligible) {
            const int32_t headerEnd = DynOff::UOBJECT_OUTER + 8;   // 0x28 / 0x30 (CPN)
            constexpr int32_t kSanity   = 0x10000;   // 64KB corruption guard
            constexpr int32_t kFallback = 0x400;     // 1KB when PropertiesSize unusable
            int32_t propsSize = ci.PropertiesSize;
            int32_t winEnd;
            if (propsSize > headerEnd && propsSize <= kSanity) {
                winEnd = propsSize;
            } else if (propsSize > kSanity) {
                winEnd = kSanity;
                sci.nativeTruncated = true;
            } else {
                winEnd = headerEnd + kFallback;
            }
            sci.nativeWinStart = headerEnd;
            sci.nativeWinEnd   = winEnd;
            sci.nativeHoles    = Ubel::ComputeClassHoles(ci, headerEnd, winEnd);
        }

        auto inserted = classCache.emplace(classAddr, std::move(sci));
        return &inserted.first->second;
    };

        // Thread-local FindDefiningClass result cache (see DefKey above).
        std::unordered_map<DefKey, std::string, DefKeyHash> definingNameCache;

        // Thread-local deep-leaf descriptor pool index, keyed by
        // (className \x01 full-display-path). Deep leaves (build 1206) have a
        // per-leaf path rather than a per-ScanField identity, so they can't use
        // the sf.descriptorIdx cache — they intern here instead (shared across
        // instances of the same class within this thread).
        std::unordered_map<std::string, uint32_t> deepDescriptors;

        // Thread-local raw-hole descriptor pool index (Native-C, P1), keyed by
        // className \x02 offset \x02 canonical-type. A raw leaf has no ScanField
        // identity, so it interns here (shared across instances of the same class
        // within this thread). Built on first emit per (class, offset, width).
        std::unordered_map<std::string, uint32_t> rawDescriptors;

        // Thread-local descriptor pool for Native-C raw holes found INSIDE struct-
        // array elements (Deep + Native-C, P3), keyed by container-path \x03 element-
        // index \x03 offset \x03 canonical-type. Separate from rawDescriptors so an
        // object-body hole and a struct-element hole at the same numeric offset don't
        // collide.
        std::unordered_map<std::string, uint32_t> deepRawDescriptors;

        // Multi-numeric meta resolver. For NumericNoByte scans, resolve a
        // field/element's own concrete DataType from its property type
        // name and point `tgt`/`tgt2` at the matching pre-parsed target.
        // Returns false (skip the field) when the type isn't a numeric
        // member or the value can't fit that width. Only reached on the
        // targeted first-scan path (prev-value scan types never get here).
        auto multiResolve = [&](const std::string& propTypeName,
                                Radar::DataType& memberDt,
                                const Radar::NumericTargetSet::Entry*& tgt,
                                const uint8_t*&      tgt2) -> bool {
            if (!Radar::TryDataTypeFromPropertyTypeName(propTypeName, memberDt)) return false;
            // FindEntry, not Find: a width the target cannot ENCODE may still be one
            // every field of satisfies (Smaller 500 over Int8). Find() hides that
            // verdict by design, so using it here is what dropped whole width
            // classes from every ordered scan. (audit #5 AB4)
            const Radar::NumericTargetSet::Entry* e =
                multiTargets ? multiTargets->FindEntry(memberDt) : nullptr;
            if (!e) return false;
            tgt  = e;
            tgt2 = nullptr;
            if (st == Radar::ScanType::Between) {
                const uint8_t* e2 = multiTargets2 ? multiTargets2->Find(memberDt) : nullptr;
                if (!e2) return false;
                tgt2 = e2;
            }
            return true;
        };

        for (int32_t i = beginIdx; i < endIdx; ++i) {
        // Periodic deadline + max-results check (every 4K objects keeps
        // the chrono cost negligible while still bounding worst-case
        // wall time to ~16ms past the deadline). maxResults is a per-thread
        // local cap: each thread keeps at most maxResults of its own
        // (ascending) candidates, and the ascending-order merge truncates to
        // maxResults — yielding exactly the lowest-index matches the serial
        // walk would have stopped at.
        // Chunk-relative stride so the check fires on this chunk's first
        // iteration (i == beginIdx) and every 4096 after, regardless of where
        // beginIdx lands — otherwise a non-4096-aligned chunk start would delay
        // the deadline + cross-thread deadlineHit check by up to 4095 objects.
        if (((i - beginIdx) & 0xFFF) == 0) {
            if (deadlineHit.load(std::memory_order_relaxed)) return;
            // Serial path (parallel toggle OFF) has no cancel-watcher thread —
            // poll Tot here so the scan still bails promptly; setting
            // deadlineHit also stops siblings on the parallel path.
            if (Tot::Requested()) { deadlineHit.store(true, std::memory_order_relaxed); return; }
            if (std::chrono::steady_clock::now() - t0 > kDeadline) {
                deadlineHit.store(true, std::memory_order_relaxed);
                return;
            }
            if (static_cast<int32_t>(tr.candidates.size()) >= maxResults) return;
        }

        // Newest-first: map the ascending loop index to a DESCENDING GObjects
        // index so the ascending-tid merge keeps the HIGHEST (most-recently-
        // allocated) indices when truncating to maxResults — the just-spawned
        // pawn survives instead of low-index CDOs/templates. realIdx is the true
        // GObjects index used everywhere (GetByIndex + the InstanceRecord); the
        // loop var i only drives chunk partitioning + the periodic deadline check.
        const int32_t realIdx = newestFirst ? (count - 1 - i) : i;
        uintptr_t obj = GetByIndex(realIdx);
        if (!obj) continue;
        tr.scannedObjects++;

        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        // Skip class-meta objects -- we want instances + CDOs, not the
        // UClass entries themselves. (A UClass's own class is "Class" /
        // "BlueprintGeneratedClass" / etc.; an instance's class is the
        // game class.)
        uint32_t metaIdx = 0;
        if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, metaIdx)) continue;
        std::string metaName = Serie::GetString(metaIdx);
        if (IsClassLikeMeta(metaName)) continue;

        ScanClassInfo* sci = buildClassIndex(cls);
        if (!sci || sci->fields.empty()) continue;
        if (gameOnly && !sci->gameClass) continue;
        // Pre-filter (opt-in): skip pure engine/system classes at the source so
        // their instances never enter the candidate set. noiseClass is false unless
        // preFilterNoise was on (guarded in buildClassIndex), and the guardrail
        // inside IsSnapshotNoiseClass keeps player Pawn / components / AttributeSets.
        if (sci->noiseClass) continue;

        // Per-object batch body read (build 974, default on via `batchRead`).
        // Read the object's fixed-width-leaf span ONCE into the reused thread-
        // local buffer, then serve those field reads from it instead of one SEH
        // read per field. Gated so it only fires when it pays off (enough
        // fields, span bounded absolutely AND by density), and never for string
        // scans (they chase a separate char buffer). If the single read FAILS
        // (e.g. the span straddles an unmapped page — ReadBytesSafe zeroes the
        // whole buffer on fault), bodyBuf stays null and every field falls back
        // to its own direct read (the pre-974 behavior).
        const uint8_t* bodyBuf  = nullptr;
        int32_t        bodyBase = 0, bodyLen = 0;
        if (batchRead && !isString
            && sci->bodyFieldCount >= kMinBatchFields
            && sci->batchSpan > 0
            && sci->batchSpan <= kMaxBatchSpan
            && sci->batchSpan <= sci->bodyFieldCount * kBatchBytesPerField) {
            objBodyBuf.resize(static_cast<size_t>(sci->batchSpan));
            if (Macht::ReadBytesSafe(obj + sci->batchMin, objBodyBuf.data(),
                                     static_cast<size_t>(sci->batchSpan))) {
                bodyBuf  = objBodyBuf.data();
                bodyBase = sci->batchMin;
                bodyLen  = sci->batchSpan;
            }
        }
        // Read `size` bytes of the object body at `off`: from the batch buffer
        // when it fully covers [off, off+size), else a direct SEH read. Returns
        // false only on a faulting direct read. Used for the fixed-width direct
        // leaf reads + the TOptional flag byte; strings / container element data
        // (separate heap) keep their own direct reads.
        auto readBody = [&](int32_t off, void* dst, size_t size) -> bool {
            if (bodyBuf && off >= bodyBase
                && static_cast<int64_t>(off) + static_cast<int64_t>(size)
                     <= static_cast<int64_t>(bodyBase) + bodyLen) {
                std::memcpy(dst, bodyBuf + (off - bodyBase), size);
                return true;
            }
            return Macht::ReadBytesSafe(obj + off, dst, size);
        };

        // Defer instance interning until we know this object has a match;
        // FName lookup is cheap but billions of unused calls add up. On
        // the first emit for this object we resolve its name once and push
        // a single InstanceRecord; every candidate of this object then
        // shares that index. -1 = not yet interned for this object.
        int32_t curInstanceIdx = -1;

        // ensureDescriptor: resolve (and cache on the ScanField) the
        // thread-local FieldDescriptor index for `sf`. The descriptor
        // interns everything that's a function of (class, field) —
        // class / defining-class / base name / type / offset / mask — so
        // it's built once per field (on first emit) and reused by every
        // array element and every instance. Defining-class resolution
        // reuses the per-(class,offset) definingNameCache.
        auto ensureDescriptor = [&](ScanField& sf) -> uint32_t {
            if (sf.descriptorIdx >= 0) return static_cast<uint32_t>(sf.descriptorIdx);
            Radar::FieldDescriptor d;
            d.className     = sci->className;
            d.fieldName     = sf.name;  // BASE name; element "[i]" added at display time
            d.fieldType     = (sf.container != ScanContainer::None)
                                ? sf.elemTypeName : sf.typeName;
            d.fieldOffset   = sf.offset;
            d.boolFieldMask = sf.boolFieldMask;
            // audit #5 A11 — tell refine HOW this field's address was formed, so a
            // container element can be re-anchored instead of re-read blind. The
            // switch is exhaustive on purpose: a ScanContainer added later must
            // state its anchor here or the compiler will not have covered it.
            switch (sf.container) {
                case ScanContainer::None:
                    d.anchor = Radar::ValueAnchor::Direct;
                    break;
                case ScanContainer::Array:
                    d.anchor = Radar::ValueAnchor::ArrayElement;
                    d.elemStride = sf.elemStride;
                    break;
                case ScanContainer::StructArrayInner:
                    d.anchor = Radar::ValueAnchor::ArrayElement;
                    d.elemStride = sf.elemStride;
                    d.elemIntra  = sf.structInnerOffset;
                    break;
                case ScanContainer::Set:
                case ScanContainer::MapKey:
                    d.anchor = Radar::ValueAnchor::SparseElement;
                    d.elemStride = sf.elemStride;
                    break;
                case ScanContainer::MapValue:
                    d.anchor = Radar::ValueAnchor::SparseElement;
                    d.elemStride = sf.elemStride;
                    d.elemIntra  = sf.valueOffset;
                    break;
            }
            // Vector scans only: the source width the refine path can no longer
            // re-derive (fieldType is the bare "StructProperty").
            d.vectorWidth   = sf.vectorWidth;

            DefKey dk{ cls, sf.offset };
            auto dit = definingNameCache.find(dk);
            if (dit != definingNameCache.end()) {
                d.definingClassName = dit->second;
            } else {
                uintptr_t defAddr = FindDefiningClass(cls, sf.offset);
                std::string defName = (defAddr && defAddr != cls)
                    ? Ubel::GetName(defAddr) : sci->className;
                definingNameCache.emplace(dk, defName);
                d.definingClassName = std::move(defName);
            }
            sf.descriptorIdx = static_cast<int32_t>(tr.descriptors.size());
            tr.descriptors.push_back(std::move(d));
            return static_cast<uint32_t>(sf.descriptorIdx);
        };

        // Helper: emit one lean candidate. Per-(class,field) metadata is
        // referenced via descriptorIdx; per-object metadata via the
        // interned instance index; only the value's address + snapshot +
        // element index live on the candidate itself. Used by both the
        // direct-field and per-array-element paths.
        // `containerNum` has NO default, deliberately (audit #5 A11): every call site
        // must state the container's element count at scan time, or -1 for "not a
        // container element". A site that forgets it fails to COMPILE rather than
        // silently minting a candidate refine cannot re-anchor.
        auto emitCandidate = [&](uintptr_t valueAddr,
                                 uint32_t  descriptorIdx,
                                 int32_t   elementIndex,   // -1 = direct field
                                 int32_t   containerNum,   // -1 = not a container element
                                 const uint8_t* rawBytes,
                                 size_t   rawByteCount,
                                 const std::string* strValue) {
            if (curInstanceIdx < 0) {
                curInstanceIdx = static_cast<int32_t>(tr.instances.size());
                tr.instances.push_back(Radar::InstanceRecord{
                    obj, realIdx, Ubel::GetName(obj) });
            }
            Radar::Candidate cand;
            cand.addr          = valueAddr;
            cand.descriptorIdx = descriptorIdx;
            cand.instanceIdx   = static_cast<uint32_t>(curInstanceIdx);
            cand.elementIndex  = elementIndex;
            cand.containerNum  = containerNum;
            if (strValue) {
                cand.prevStr = *strValue;
            } else if (rawBytes && rawByteCount > 0) {
                std::memcpy(cand.prevValue, rawBytes, rawByteCount);
            }
            tr.candidates.push_back(std::move(cand));
        };

        // Shared per-element scan for the container paths (Array / Set /
        // Map key|value): read the value at elemAddr, test the predicate,
        // and emit a candidate (elementIndex = the container slot index) on
        // a match. Mirrors the direct-field read/compare branches; the
        // descriptor is interned lazily on the first match of this field.
        // `containerNum` = the container's element count at scan time (TArray::Num /
        // TSparseArray::MaxIndex). Required, not defaulted — see emitCandidate.
        auto scanElement = [&](ScanField& sf, uintptr_t elemAddr, int32_t elemIndex,
                               int32_t containerNum) {
            // Sized for the widest value read through it: a 24-byte LWC vector.
            uint8_t     readBuf[Radar::VECTOR_CANON_BYTES] = {};
            std::string readStr;
            if (isString) {
                if (dt == Radar::DataType::FString) {
                    readStr = Ubel::ReadFStringAt(elemAddr, 0);
                } else if (dt == Radar::DataType::FName) {
                    readStr = Ubel::ReadFNameAt(elemAddr, 0);
                } else {
                    readStr = Ubel::ReadFTextStringAt(elemAddr, 0);
                }
                if (!Radar::CompareStringPredicate(st, readStr, targetString, caseSensitive)) return;
                emitCandidate(elemAddr, ensureDescriptor(sf), elemIndex, containerNum, nullptr, 0, &readStr);
            } else if (isVector) {
                // Read the field's OWN width, decode to the canonical triple,
                // then store the canonical form so display + refine never have
                // to re-ask how wide the source was.
                double cur[3];
                if (!Macht::ReadBytesSafe(elemAddr, readBuf,
                                          static_cast<size_t>(sf.vectorWidth))) return;
                if (!Radar::DecodeVectorBytes(readBuf, sf.vectorWidth, cur)) return;
                if (!Radar::CompareVectorPredicate(st, cur, targetVec, targetVec2Ptr, roundMode)) return;
                uint8_t canon[Radar::VECTOR_CANON_BYTES];
                Radar::StoreVectorCanonical(cur, canon);
                emitCandidate(elemAddr, ensureDescriptor(sf), elemIndex, containerNum,
                              canon, sizeof(canon), nullptr);
            } else if (isMulti) {
                // Resolve the element's own width (key/value/elem type) + target.
                Radar::DataType elemDt = dt;
                const Radar::NumericTargetSet::Entry* mtgt = nullptr;
                const uint8_t* mtgt2 = nullptr;
                if (!multiResolve(sf.elemTypeName, elemDt, mtgt, mtgt2)) return;
                size_t sz = Radar::SizeOf(elemDt);
                if (!Macht::ReadBytesSafe(elemAddr, readBuf, sz)) return;
                if (!Radar::ComparePredicate(elemDt, st, readBuf, mtgt, mtgt2, roundMode)) return;
                emitCandidate(elemAddr, ensureDescriptor(sf), elemIndex, containerNum, readBuf, sz, nullptr);
            } else {
                // Container elements never share a bitfield byte (TArray /
                // TSet<bool> + TMap<bool,...> store bool unpacked), so the
                // boolFieldMask = 0xFF path applies.
                if (!Macht::ReadBytesSafe(elemAddr, readBuf, dtSize)) return;
                if (!Radar::ComparePredicate(dt, st, readBuf, targetBytes, target2Bytes, roundMode)) return;
                emitCandidate(elemAddr, ensureDescriptor(sf), elemIndex, containerNum, readBuf, dtSize, nullptr);
            }
        };

        // Deep-walk descriptor + emit (build 1206). For a leaf found by the
        // recursive WalkContainerLeaves at container depth >= 2 (the static
        // StructArrayInner path covers depth 1; the TArray<leaf> branch covers
        // top-level leaf-containers), build a per-path descriptor and emit a
        // candidate if the value matches. Vectors are skipped (the walker emits
        // scalar leaves, not whole vector structs).
        auto ensureDeepDescriptor = [&](const std::string& displayName,
                                        const std::string& fieldType,
                                        uint8_t boolMask) -> uint32_t {
            std::string key = sci->className; key += '\x01'; key += displayName;
            auto it = deepDescriptors.find(key);
            if (it != deepDescriptors.end()) return it->second;
            Radar::FieldDescriptor d;
            d.className         = sci->className;
            d.definingClassName = sci->className;
            d.fieldName         = displayName;   // fully-substituted path, no "[]" placeholder
            d.fieldType         = fieldType;
            d.fieldOffset       = 0;             // deep leaf: object-relative offset not meaningful
            d.boolFieldMask     = boolMask;      // NOT 0xFF: a packed bool shares its byte
                                                 // with up to 7 siblings, so a hardcoded
                                                 // whole-byte mask compares all of them.
                                                 // Newly reachable now that struct-sided
                                                 // Set/Map leaves emit (audit #5 A4).
            uint32_t idx = static_cast<uint32_t>(tr.descriptors.size());
            tr.descriptors.push_back(std::move(d));
            deepDescriptors.emplace(std::move(key), idx);
            return idx;
        };

        auto deepEmit = [&](const ContainerLeaf& lf) {
            if (static_cast<int32_t>(tr.candidates.size()) >= maxResults) return;
            // Was `lf.depth < 2`, i.e. "depth 1 is covered by the static paths". That
            // is true only for ARRAYS -- collectStructArrayInner is reached solely
            // from the ArrayProperty branch -- so a struct-sided TSet<FStruct> or
            // TMap<K, FStruct> element was covered by neither the static index nor
            // this pass, and an everyday TMap<FName, FItemData> inventory count was
            // unfindable with Deep ON as well as OFF (audit #5 A4). The two sibling
            // consumers already had the right shape: the snapshot path tests
            // `leafName.empty() && depth < 2`, and the group scan uses `depth < 1`.
            // `!sci->fieldCapHit` guards the predicate's own premise: it answers
            // "does the STATIC INDEX reach this leaf", which is only meaningful if
            // that index was built completely. A class truncated at the cap falls
            // through to emitting rather than trusting coverage it may not have.
            if (!sci->fieldCapHit
                && DeepLeafCoveredByStaticScanIndex(lf.depth, lf.kind, lf.leafName.empty())) return;
            if (isVector) return;       // walker yields scalar leaves, not vector structs

            bool typeOk = false;
            for (const auto& t : acceptedTypes) if (lf.leafType == t) { typeOk = true; break; }

            std::string disp = lf.arrayPath;
            disp += '['; disp += std::to_string(lf.elemIndex); disp += ']';
            if (!lf.leafName.empty()) { disp += '.'; disp += lf.leafName; }

            uint8_t readBuf[16] = {};
            if (isString) {
                if (!typeOk) return;
                std::string readStr;
                if (dt == Radar::DataType::FString)      readStr = Ubel::ReadFStringAt(lf.leafAddr, 0);
                else if (dt == Radar::DataType::FName)    readStr = Ubel::ReadFNameAt(lf.leafAddr, 0);
                else                                          readStr = Ubel::ReadFTextStringAt(lf.leafAddr, 0);
                if (!Radar::CompareStringPredicate(st, readStr, targetString, caseSensitive)) return;
                emitCandidate(lf.leafAddr, ensureDeepDescriptor(disp, lf.leafType, lf.boolMask), -1, /*containerNum=*/-1, nullptr, 0, &readStr);
            } else if (isMulti) {
                Radar::DataType mdt = dt;
                const Radar::NumericTargetSet::Entry* mtgt = nullptr; const uint8_t* mtgt2 = nullptr;
                if (!multiResolve(lf.leafType, mdt, mtgt, mtgt2)) return;
                size_t sz = Radar::SizeOf(mdt);
                if (!Macht::ReadBytesSafe(lf.leafAddr, readBuf, sz)) return;
                if (!Radar::ComparePredicate(mdt, st, readBuf, mtgt, mtgt2, roundMode)) return;
                emitCandidate(lf.leafAddr, ensureDeepDescriptor(disp, lf.leafType, lf.boolMask), -1, /*containerNum=*/-1, readBuf, sz, nullptr);
            } else {
                if (!typeOk) return;
                if (!Macht::ReadBytesSafe(lf.leafAddr, readBuf, dtSize)) return;
                if (!Radar::ComparePredicate(dt, st, readBuf, targetBytes, target2Bytes, roundMode)) return;
                emitCandidate(lf.leafAddr, ensureDeepDescriptor(disp, lf.leafType, lf.boolMask), -1, /*containerNum=*/-1, readBuf, dtSize, nullptr);
            }
        };

        for (auto& sf : sci->fields) {
            if (static_cast<int32_t>(tr.candidates.size()) >= maxResults) break;

            // Phase 2C: TArray<T> path. Read the TArray header (Data,
            // Num, Max), defensively validate, then iterate elements
            // and emit one candidate per match.
            if (sf.container == ScanContainer::Array) {
                uintptr_t arrayDataPtr = 0;
                int32_t   arrayNum     = 0;
                int32_t   arrayMax     = 0;
                if (!Macht::ReadSafe(obj + sf.offset, arrayDataPtr)) continue;
                if (!Macht::ReadSafe(obj + sf.offset + 8, arrayNum)) continue;
                if (!Macht::ReadSafe(obj + sf.offset + 12, arrayMax)) continue;

                // Safety circuit-breakers (memory project_value_search_caveats):
                //   - Negative or absurdly-large Num is a corrupted /
                //     freed-memory marker; skip with a single LOG_WARN
                //     so we surface pathological data without spamming.
                //   - Max must be >= Num for a valid TArray; mismatch
                //     signals corruption (or OptionalProperty being
                //     misread as TArray).
                //   - Empty array (Num == 0) is fine, just skip the
                //     iteration; Data ptr may be null in that case.
                constexpr int32_t kMaxElementsPerArray = 10'000'000;
                if (arrayNum < 0 || arrayNum > kMaxElementsPerArray) {
                    LOG_WARN("Radar: skipping TArray with Num=%d on field '%s' at 0x%llX (instance 0x%llX)",
                             arrayNum, sf.name.c_str(),
                             (unsigned long long)(obj + sf.offset),
                             (unsigned long long)obj);
                    continue;
                }
                if (arrayNum == 0) continue;
                if (arrayMax < arrayNum) continue;
                if (!arrayDataPtr) continue;

                // The element display name "Field[idx]" is reconstructed at
                // serialization time from the shared descriptor + elementIndex.
                for (int32_t idx = 0; idx < arrayNum; ++idx) {
                    if (static_cast<int32_t>(tr.candidates.size()) >= maxResults) break;
                    // A pathological 10M-element array would otherwise pin this
                    // worker far past the deadline and ignore a cancel; poll the
                    // shared deadline/cancel flag every 4K elements (Tot sets
                    // deadlineHit via the ParallelGObjectsScan watcher).
                    if ((idx & 0xFFF) == 0) {
                        if (deadlineHit.load(std::memory_order_relaxed)) return;
                        if (Tot::Requested()) { deadlineHit.store(true, std::memory_order_relaxed); return; }
                        if (std::chrono::steady_clock::now() - t0 > kDeadline) {
                            deadlineHit.store(true, std::memory_order_relaxed);
                            return;
                        }
                    }
                    scanElement(sf, arrayDataPtr + static_cast<uintptr_t>(idx) * sf.elemStride, idx,
                                /*containerNum=*/arrayNum);
                }
                continue;
            }

            // Struct-array-inner path (build 1201): a leaf field INSIDE a
            // TArray<FStruct> element (e.g. SaveSlotList[1].GP). Same TArray
            // header read as Array, but the value sits at
            // arrayData + idx*elemStride + structInnerOffset (an element-relative
            // leaf offset, possibly through direct sub-structs).
            if (sf.container == ScanContainer::StructArrayInner) {
                uintptr_t arrayDataPtr = 0;
                int32_t   arrayNum     = 0;
                int32_t   arrayMax     = 0;
                if (!Macht::ReadSafe(obj + sf.offset, arrayDataPtr)) continue;
                if (!Macht::ReadSafe(obj + sf.offset + 8, arrayNum)) continue;
                if (!Macht::ReadSafe(obj + sf.offset + 12, arrayMax)) continue;

                constexpr int32_t kMaxElementsPerArray = 10'000'000;
                if (arrayNum < 0 || arrayNum > kMaxElementsPerArray) {
                    LOG_WARN("Radar: skipping struct TArray with Num=%d on field '%s' at 0x%llX (instance 0x%llX)",
                             arrayNum, sf.name.c_str(),
                             (unsigned long long)(obj + sf.offset),
                             (unsigned long long)obj);
                    continue;
                }
                if (arrayNum == 0) continue;
                if (arrayMax < arrayNum) continue;
                if (!arrayDataPtr) continue;

                for (int32_t idx = 0; idx < arrayNum; ++idx) {
                    if (static_cast<int32_t>(tr.candidates.size()) >= maxResults) break;
                    if ((idx & 0xFFF) == 0) {
                        if (deadlineHit.load(std::memory_order_relaxed)) return;
                        if (Tot::Requested()) { deadlineHit.store(true, std::memory_order_relaxed); return; }
                        if (std::chrono::steady_clock::now() - t0 > kDeadline) {
                            deadlineHit.store(true, std::memory_order_relaxed);
                            return;
                        }
                    }
                    scanElement(sf,
                        arrayDataPtr + static_cast<uintptr_t>(idx) * sf.elemStride
                                     + static_cast<uintptr_t>(sf.structInnerOffset),
                        idx, /*containerNum=*/arrayNum);
                }
                continue;
            }

            // V1a: TSet<T> / TMap<K,V>(key|value) path. Walk the FSet/
            // FMapProperty's TSparseArray (allocated slots only); the value
            // lives at slot_base + slotOff (0 for Set / Map key, valueOffset
            // for Map value). The sparse Data buffer holds freed slots too,
            // so IsSparseIndexAllocated skips them. Element addresses are raw
            // (like TArray) — refine re-anchors them via Radar::ValueAnchor::
            // SparseElement, which re-reads the header and re-tests THIS slot's
            // allocation bit rather than trusting the stored address (audit #5
            // A11; the old "a realloc just drops the candidate" was wrong — a
            // freed sparse slot is REUSED in place and reads as a live value).
            if (sf.container == ScanContainer::Set
                || sf.container == ScanContainer::MapKey
                || sf.container == ScanContainer::MapValue) {
                if (sf.elemStride <= 0) continue;
                Macht::TSparseArrayView sa;
                if (!Macht::ReadTSparseArray(obj + sf.offset, sa)) continue;
                if (sa.MaxIndex <= 0 || !sa.Data) continue;
                const int32_t slotOff = (sf.container == ScanContainer::MapValue)
                                          ? sf.valueOffset : 0;
                for (int32_t e = 0; e < sa.MaxIndex; ++e) {
                    if (static_cast<int32_t>(tr.candidates.size()) >= maxResults) break;
                    // Poll the shared deadline/cancel flag every 4K slots so a huge
                    // TSet/TMap can't hold this worker past the deadline or a cancel.
                    if ((e & 0xFFF) == 0) {
                        if (deadlineHit.load(std::memory_order_relaxed)) return;
                        if (Tot::Requested()) { deadlineHit.store(true, std::memory_order_relaxed); return; }
                        if (std::chrono::steady_clock::now() - t0 > kDeadline) {
                            deadlineHit.store(true, std::memory_order_relaxed);
                            return;
                        }
                    }
                    if (!Macht::IsSparseIndexAllocated(sa, e)) continue;
                    scanElement(sf,
                        sa.Data + static_cast<int64_t>(e) * sf.elemStride + slotOff, e,
                        /*containerNum=*/sa.MaxIndex);
                }
                continue;
            }

            // V1c: TOptional<T> unset gate. The wrapped value lives at
            // field+0 (read like a direct leaf below), but for a non-intrusive
            // optional it's only meaningful when the trailing bIsSet byte is
            // set — skip unset slots so a scan for 0 / stale bytes doesn't
            // false-hit. optionalFlagOffset is -1 for ordinary leaf fields.
            if (sf.optionalFlagOffset >= 0) {
                uint8_t isSet = 0;
                if (!readBody(sf.offset + sf.optionalFlagOffset, &isSet, 1)
                    || isSet == 0)
                    continue;
            }

            uintptr_t valueAddr = obj + sf.offset;
            // Sized for the widest value read through it: a 24-byte LWC vector.
            uint8_t  readBuf[Radar::VECTOR_CANON_BYTES] = {};
            std::string readStr;

            if (isString) {
                // FString / FName / FText -- resolve to UTF-8 via Ubel
                // helpers. Empty resolution returns "" and we still test
                // it (target may be "" for Exact-empty searches).
                if (dt == Radar::DataType::FString) {
                    readStr = Ubel::ReadFStringAt(obj, sf.offset);
                } else if (dt == Radar::DataType::FName) {
                    readStr = Ubel::ReadFNameAt(obj, sf.offset);
                } else {
                    readStr = Ubel::ReadFTextStringAt(obj, sf.offset);
                }
                if (!Radar::CompareStringPredicate(st, readStr, targetString, caseSensitive)) continue;
                emitCandidate(valueAddr, ensureDescriptor(sf), -1, /*containerNum=*/-1, nullptr, 0, &readStr);
                continue;
            }
            if (isVector) {
                // Vector / Rotator: read the field's OWN reflected width from
                // the struct start — 12 (3xfloat) or 24 (3xdouble LWC) — and
                // decode to the canonical triple the target is already in.
                double cur[3];
                if (!readBody(sf.offset, readBuf, static_cast<size_t>(sf.vectorWidth))) continue;
                if (!Radar::DecodeVectorBytes(readBuf, sf.vectorWidth, cur)) continue;
                if (!Radar::CompareVectorPredicate(st, cur, targetVec, targetVec2Ptr, roundMode)) continue;
                uint8_t canon[Radar::VECTOR_CANON_BYTES];
                Radar::StoreVectorCanonical(cur, canon);
                emitCandidate(valueAddr, ensureDescriptor(sf), -1, /*containerNum=*/-1, canon, sizeof(canon), nullptr);
                continue;
            }

            if (isMulti) {
                // Resolve this field's own width + matching target; skip
                // if the value can't fit it. Compare with the per-field
                // DataType so an int field compares as int, a float field
                // as float — no byte-reinterpret.
                Radar::DataType memberDt;
                const Radar::NumericTargetSet::Entry* mtgt = nullptr;
                const uint8_t* mtgt2 = nullptr;
                if (!multiResolve(sf.typeName, memberDt, mtgt, mtgt2)) continue;
                size_t msz = Radar::SizeOf(memberDt);
                if (!readBody(sf.offset, readBuf, msz)) continue;
                if (!Radar::ComparePredicate(memberDt, st, readBuf, mtgt, mtgt2, roundMode)) continue;
                emitCandidate(valueAddr, ensureDescriptor(sf), -1, /*containerNum=*/-1, readBuf, msz, nullptr);
                continue;
            }

            if (!readBody(sf.offset, readBuf, dtSize)) continue;

            // BoolProperty bitfield normalisation. The bytes we
            // store as prevValue must reflect the LOGICAL bool
            // (0/1), not the raw shared byte, so Changed /
            // Unchanged refines compare on a stable value even
            // when sibling bits flip.
            if (dt == Radar::DataType::Bool
                && sf.boolFieldMask != 0 && sf.boolFieldMask != 0xFF) {
                readBuf[0] = ((readBuf[0] & sf.boolFieldMask) != 0) ? 1 : 0;
            }

            if (!Radar::ComparePredicate(dt, st, readBuf, targetBytes, target2Bytes, roundMode)) continue;
            emitCandidate(valueAddr, ensureDescriptor(sf), -1, /*containerNum=*/-1, readBuf, dtSize, nullptr);
        }

        // Deeply-nested container values (build 1206). The static fields above
        // reach depth-1 struct-array leaves (StructArrayInner) + top-level leaf-
        // containers (the TArray<leaf> branch); this recursive pass reaches a
        // value > 1 container level deep (e.g. SaveSlotList[1].MsTuneData.
        // MsTunes[0].WeaponTuneList[0].Tunes[N]). Gated per class (needsDeepWalk)
        // so the common case — no struct-element container nesting — pays nothing.
        if (sci->needsDeepWalk && static_cast<int32_t>(tr.candidates.size()) < maxResults) {
            WalkLeafLimits dlim;
            dlim.maxDepth = 4;
            dlim.maxElems = 256;
            // Same deterministic per-object element cap as snapshot (build 1211):
            // a single deeply-nested wide object can't monopolise the global
            // scan budget. The 15s wall-clock deadline below remains the global
            // backstop across all objects.
            dlim.maxTotalElems = kDeepWalkMaxTotalElems;
            int64_t deepVisited = 0;
            dlim.aborted  = [&] {
                if (deadlineHit.load(std::memory_order_relaxed)) return true;
                if (Tot::Requested()) { deadlineHit.store(true, std::memory_order_relaxed); return true; }
                if (std::chrono::steady_clock::now() - t0 > kDeadline) {
                    deadlineHit.store(true, std::memory_order_relaxed); return true;
                }
                return false;
            };
            WalkContainerLeaves(obj, cls, /*pathPrefix*/ "", /*depth*/ 0, dlim, deepEmit, &deepVisited);
        }

        // === Native-C (P1): scan this object's UNMANAGED holes ===
        // The reflected loop above covered every UPROPERTY; this pass tests the
        // requested numeric value at the user's width across the byte ranges no
        // property covers (where native, non-UPROPERTY C++ members live). Holes +
        // window are precomputed per class (sci->nativeHoles). Read the window
        // ONCE (SEH-safe) and probe in-buffer; on a faulting read (rare unmapped
        // tail) skip native for this object — its reflected matches are unaffected.
        if (nativeEligible && !sci->nativeHoles.empty()
            && static_cast<int32_t>(tr.candidates.size()) < maxResults) {
            const int32_t winStart = sci->nativeWinStart;
            const int32_t winEnd   = sci->nativeWinEnd;
            nativeBuf.resize(static_cast<size_t>(winEnd - winStart));
            if (Macht::ReadBytesSafe(obj + winStart, nativeBuf.data(), nativeBuf.size())) {
                // Intern a synthetic raw descriptor for (this class, offset, width).
                auto ensureRawDescriptor = [&](int32_t offset, Radar::DataType width) -> uint32_t {
                    const char* canon = Radar::PropertyTypeNameOf(width);
                    std::string key = sci->className;
                    key += '\x02'; key += std::to_string(offset);
                    key += '\x02'; key += canon;
                    auto rit = rawDescriptors.find(key);
                    if (rit != rawDescriptors.end()) return rit->second;
                    Radar::FieldDescriptor d;
                    d.className     = sci->className;
                    d.definingClassName = "";   // unmanaged — no declaring class
                    char nb[32];
                    snprintf(nb, sizeof(nb), "<raw@0x%X>", offset);
                    d.fieldName     = nb;
                    d.fieldType     = canon;   // canonical -> refine round-trips
                    d.fieldOffset   = offset;
                    d.boolFieldMask = 0xFF;
                    d.isNativeC     = true;
                    d.guessedType   = Radar::NameOf(width);
                    uint32_t idx = static_cast<uint32_t>(tr.descriptors.size());
                    tr.descriptors.push_back(std::move(d));
                    rawDescriptors.emplace(std::move(key), idx);
                    return idx;
                };

                // Bounds keep a single wide object from monopolising the scan:
                // emitted native candidates and offset probes are both capped per
                // object (the global maxResults + 15s deadline still apply).
                constexpr int32_t kMaxRawPerObj = 256;
                constexpr int32_t kMaxRawProbes = 32768;
                int32_t rawEmitted = 0, probes = 0;

                for (const auto& hole : sci->nativeHoles) {
                    if (rawEmitted >= kMaxRawPerObj || probes >= kMaxRawProbes) break;
                    if (static_cast<int32_t>(tr.candidates.size()) >= maxResults) break;
                    int32_t off = (hole.start + (nativeStride - 1)) & ~(nativeStride - 1);
                    for (; off < hole.end; off += nativeStride) {
                        if (rawEmitted >= kMaxRawPerObj || probes >= kMaxRawProbes) break;
                        if (static_cast<int32_t>(tr.candidates.size()) >= maxResults) break;
                        ++probes;
                        const uint8_t* p = nativeBuf.data() + (off - winStart);
                        if (isMulti) {
                            // Test each member width that fits and whose target the
                            // user's value can represent (one candidate per match).
                            for (const auto& e : multiTargets->entries) {
                                size_t msz = Radar::SizeOf(e.dt);
                                if (msz == 0 || off + static_cast<int32_t>(msz) > hole.end) continue;
                                const uint8_t* mtgt2 = nullptr;
                                if (st == Radar::ScanType::Between) {
                                    mtgt2 = multiTargets2 ? multiTargets2->Find(e.dt) : nullptr;
                                    if (!mtgt2) continue;
                                }
                                if (!Radar::ComparePredicate(e.dt, st, p, &e, mtgt2, roundMode)) continue;
                                emitCandidate(obj + off, ensureRawDescriptor(off, e.dt), -1, /*containerNum=*/-1, p, msz, nullptr);
                                if (++rawEmitted >= kMaxRawPerObj) break;
                            }
                        } else {
                            if (off + static_cast<int32_t>(dtSize) > hole.end) break;  // width can't fit the hole's tail
                            if (!Radar::ComparePredicate(dt, st, p, targetBytes, target2Bytes, roundMode)) continue;
                            emitCandidate(obj + off, ensureRawDescriptor(off, dt), -1, /*containerNum=*/-1, p, dtSize, nullptr);
                            ++rawEmitted;
                        }
                    }
                }
            }
        }

        // === Native-C deep (P3): UNMANAGED holes INSIDE struct-array elements ===
        // The object-body native pass above only covers [header, PropertiesSize) of
        // the object itself — a struct-array element lives in the TArray's OWN heap
        // buffer, outside that window, so a native (non-UPROPERTY) value inside a
        // 0-reflected-field element struct (e.g. CustomAbilityEffectDuration's elapsed
        // seconds) was unreachable by both the reflected deep pass (no fields to emit)
        // and the object-body native pass. When Deep + Native-C are BOTH on, walk each
        // struct-element container of this class and probe each element's holes (the
        // bytes its element struct does not reflect — the whole element for a 0-field
        // struct) for the target value. Self-contained: reuses GetClassContainers +
        // Ubel::ComputeClassHoles; the shared WalkContainerLeaves (also used by group
        // scan) is untouched. Bounded per container + per object; honors the deadline.
        if (nativeEligible && deep
            && static_cast<int32_t>(tr.candidates.size()) < maxResults) {
            // Native-C analog of ensureDeepDescriptor: like the deep REFLECTED path,
            // the element index is baked into the descriptor display name (and key) and
            // candidates pass elementIndex=-1 (the path string is the full identity) —
            // this matches the established deep-leaf convention. Bounded by the per-
            // object emit cap below, so the descriptor pool stays small. The only
            // difference vs ensureDeepDescriptor is isNativeC=true + guessedType.
            auto ensureDeepRawDescriptor =
                [&](const std::string& path, int32_t elemIdx, int32_t off,
                    Radar::DataType width) -> uint32_t {
                const char* canon = Radar::PropertyTypeNameOf(width);
                std::string key = path; key += '\x03';
                key += std::to_string(elemIdx); key += '\x03';
                key += std::to_string(off);     key += '\x03'; key += canon;
                auto it = deepRawDescriptors.find(key);
                if (it != deepRawDescriptors.end()) return it->second;
                Radar::FieldDescriptor d;
                d.className         = sci->className;
                d.definingClassName = "";          // unmanaged — no declaring class
                char nb[96];
                snprintf(nb, sizeof(nb), "%s[%d].<raw@0x%X>", path.c_str(), elemIdx, off);
                d.fieldName     = nb;
                d.fieldType     = canon;            // canonical -> refine round-trips
                d.fieldOffset   = 0;                // deep leaf: object-relative offset n/a
                d.boolFieldMask = 0xFF;
                d.isNativeC     = true;
                d.guessedType   = Radar::NameOf(width);
                uint32_t idx = static_cast<uint32_t>(tr.descriptors.size());
                tr.descriptors.push_back(std::move(d));
                deepRawDescriptors.emplace(std::move(key), idx);
                return idx;
            };

            constexpr int32_t kMaxDeepRawElems = 4096;  // elements probed per container
            constexpr int32_t kMaxDeepRawEmit  = 256;   // candidates emitted per object
            int32_t deepRawEmitted = 0;
            std::vector<uint8_t> elemBuf;

            const auto& containers = GetClassContainers(cls);
            for (const auto& cfe : containers) {
                if (deepRawEmitted >= kMaxDeepRawEmit) break;
                if (static_cast<int32_t>(tr.candidates.size()) >= maxResults) break;
                if (cfe.stride <= 0) continue;
                // Only struct-element sides (array/set element, or map value struct).
                uintptr_t elemStruct = (cfe.kind == ContainerKind::Map) ? cfe.valueStruct : cfe.elemStruct;
                int32_t   regionOff  = (cfe.kind == ContainerKind::Map) ? cfe.valueOffset : 0;
                if (!elemStruct) continue;

                // Holes = the element struct's bytes NOT covered by a reflected field
                // (the native region). For a 0-UPROPERTY struct that's the whole
                // element; for a partially-reflected struct only its gaps.
                const ClassInfo& eci = Ubel::WalkClassEx(elemStruct);   // memoized (B10) — by ref
                const int32_t maxRegion = cfe.stride - regionOff;   // bytes available to this side
                int32_t structSize = eci.PropertiesSize;
                if (structSize <= 0)        structSize = maxRegion;
                if (structSize > maxRegion) structSize = maxRegion;  // never read past the element slot
                if (structSize <= 0 || structSize > 0x10000) continue;
                auto holes = Ubel::ComputeClassHoles(eci, 0, structSize);
                if (holes.empty()) continue;

                // Read the container header (mirror WalkContainerLeaves' guards).
                uintptr_t fieldAddr = obj + cfe.offset;
                uintptr_t bufData = 0; int32_t capacity = 0;
                Macht::TSparseArrayView sa{};
                const bool isSparse = (cfe.kind != ContainerKind::Array);
                if (cfe.kind == ContainerKind::Array) {
                    Macht::TArrayView arr;
                    if (!Macht::ReadTArray(fieldAddr, arr)) continue;
                    if (arr.Count <= 0 || !arr.Data || arr.Max <= 0 || arr.Max > 0x100000) continue;
                    bufData = arr.Data; capacity = arr.Count;
                } else {
                    if (!Macht::ReadTSparseArray(fieldAddr, sa)) continue;
                    if (sa.MaxCapacity <= 0 || !sa.Data || sa.MaxCapacity > 0x100000) continue;
                    bufData = sa.Data; capacity = sa.MaxCapacity;
                }
                if (capacity <= 0) continue;

                elemBuf.resize(static_cast<size_t>(structSize));
                const int32_t probe = capacity < kMaxDeepRawElems ? capacity : kMaxDeepRawElems;
                for (int32_t e = 0; e < probe; ++e) {
                    if (deepRawEmitted >= kMaxDeepRawEmit) break;
                    if (static_cast<int32_t>(tr.candidates.size()) >= maxResults) break;
                    if ((e & 0xFFF) == 0) {
                        if (deadlineHit.load(std::memory_order_relaxed)) break;
                        if (Tot::Requested()) { deadlineHit.store(true, std::memory_order_relaxed); break; }
                        if (std::chrono::steady_clock::now() - t0 > kDeadline) {
                            deadlineHit.store(true, std::memory_order_relaxed); break;
                        }
                    }
                    if (isSparse && !Macht::IsSparseIndexAllocated(sa, e)) continue;
                    uintptr_t elemBase = bufData + static_cast<int64_t>(e) * cfe.stride + regionOff;
                    if (!Macht::ReadBytesSafe(elemBase, elemBuf.data(), elemBuf.size())) continue;

                    for (const auto& hole : holes) {
                        if (deepRawEmitted >= kMaxDeepRawEmit) break;
                        int32_t off = (hole.start + (nativeStride - 1)) & ~(nativeStride - 1);
                        for (; off < hole.end; off += nativeStride) {
                            if (deepRawEmitted >= kMaxDeepRawEmit) break;
                            if (static_cast<int32_t>(tr.candidates.size()) >= maxResults) break;
                            const uint8_t* p = elemBuf.data() + off;
                            if (isMulti) {
                                for (const auto& me : multiTargets->entries) {
                                    size_t msz = Radar::SizeOf(me.dt);
                                    if (msz == 0 || off + static_cast<int32_t>(msz) > hole.end) continue;
                                    const uint8_t* mtgt2 = nullptr;
                                    if (st == Radar::ScanType::Between) {
                                        mtgt2 = multiTargets2 ? multiTargets2->Find(me.dt) : nullptr;
                                        if (!mtgt2) continue;
                                    }
                                    if (!Radar::ComparePredicate(me.dt, st, p, &me, mtgt2, roundMode)) continue;
                                    emitCandidate(elemBase + off,
                                                  ensureDeepRawDescriptor(cfe.name, e, off, me.dt),
                                                  -1, /*containerNum=*/-1, p, msz, nullptr);
                                    if (++deepRawEmitted >= kMaxDeepRawEmit) break;
                                }
                            } else {
                                if (off + static_cast<int32_t>(dtSize) > hole.end) break;
                                if (!Radar::ComparePredicate(dt, st, p, targetBytes, target2Bytes, roundMode)) continue;
                                emitCandidate(elemBase + off,
                                              ensureDeepRawDescriptor(cfe.name, e, off, dt),
                                              -1, /*containerNum=*/-1, p, dtSize, nullptr);
                                ++deepRawEmitted;
                            }
                        }
                    }
                }
            }
        }
    }

        // Tally classes that contributed at least one matching field so the
        // merge can report a deduplicated global class count.
        for (const auto& kv : classCache) {
            if (!kv.second.fields.empty()) tr.classesWithFields.insert(kv.first);
        }
    }, /*maxThreads=*/ parallel ? 0 : 1);  // ParallelGObjectsScan (1 = serial when toggle off)

    // Fold per-thread stats.
    std::unordered_set<uintptr_t> classesWithFields;
    for (auto& tr : scan.perThread) {
        result.stats.scannedObjects += tr.scannedObjects;
        classesWithFields.insert(tr.classesWithFields.begin(),
                                 tr.classesWithFields.end());
    }

    // Merge per-thread candidate + descriptor + instance pools in ascending
    // tid order — preserves the serial "scan ascending, stop at maxResults"
    // invariant (same as ConcatTruncate, which we can't use here because the
    // candidate indices need remapping). Each thread's descriptorIdx /
    // instanceIdx are LOCAL to that thread's pools; offset them by the
    // running pool base as the pools concatenate. Cross-thread dedup is
    // intentionally skipped: descriptors are a few hundred per thread (minor
    // duplication) and a given object is scanned by exactly one thread (so
    // instances never duplicate across threads). A handful of descriptors
    // referenced only by truncated candidates may carry over unused — cheap.
    for (auto& tr : scan.perThread) {
        if (static_cast<int32_t>(result.candidates.size()) >= maxResults) break;
        const uint32_t descBase = static_cast<uint32_t>(result.descriptors.size());
        const uint32_t instBase = static_cast<uint32_t>(result.instances.size());
        for (auto& d : tr.descriptors) result.descriptors.push_back(std::move(d));
        for (auto& ins : tr.instances) result.instances.push_back(std::move(ins));
        for (auto& c : tr.candidates) {
            if (static_cast<int32_t>(result.candidates.size()) >= maxResults) break;
            c.descriptorIdx += descBase;
            c.instanceIdx   += instBase;
            result.candidates.push_back(std::move(c));
        }
    }
    result.stats.scannedClasses = static_cast<int32_t>(classesWithFields.size());
    result.stats.deadlineHit    = scan.deadlineHit;
    // Reflect a maxResults cap hit too (mirrors the group scan): the candidate
    // set — and the class histogram built from it — is then a lower bound, so the
    // UI's "counts are partial / truncated" warning must show. The walk self-caps
    // per thread, so reaching maxResults means more matches existed.
    if (static_cast<int32_t>(result.candidates.size()) >= maxResults)
        result.stats.deadlineHit = true;

    auto dtms = std::chrono::duration_cast<std::chrono::milliseconds>(
                    std::chrono::steady_clock::now() - t0).count();
    result.stats.durationMs = static_cast<int64_t>(dtms);

    LOG_INFO("Radar: First Scan complete -- %d candidates in %lld ms (%d objects, %d classes with matching fields, %d thread(s)%s)",
             static_cast<int>(result.candidates.size()),
             static_cast<long long>(dtms),
             result.stats.scannedObjects,
             static_cast<int>(classesWithFields.size()), scan.nthreads,
             result.stats.deadlineHit ? ", DEADLINE HIT" : "");
    return result;
}

ValueScanStats RefineCandidates(
    Radar::DataType                            dt,
    Radar::ScanType                            st,
    const uint8_t*                                 targetBytes,
    const uint8_t*                                 target2Bytes,
    std::vector<Radar::Candidate>&             candidates,
    const std::vector<Radar::FieldDescriptor>& descriptors,
    const std::vector<Radar::InstanceRecord>&  instances,
    Radar::RoundMode                               roundMode,
    const std::string&                             targetString,
    bool                                           caseSensitive,
    const Radar::NumericTargetSet*             multiTargets,
    const Radar::NumericTargetSet*             multiTargets2)
{
    ValueScanStats stats;
    auto t0 = std::chrono::steady_clock::now();

    const bool isString = Radar::IsStringDataType(dt);
    const bool isVector = Radar::IsVectorDataType(dt);
    const bool isMulti  = Radar::IsMultiNumericDataType(dt);
    const size_t dtSize = Radar::SizeOf(dt);
    // Vectors report SizeOf 0 (variable per field — see Radar::SizeOf); their
    // width comes from each candidate's descriptor, so they must not be caught
    // by the fixed-width guard.
    if (!isString && !isMulti && !isVector && dtSize == 0) return stats;

    const bool usePrev = Radar::IsPrevValueScanType(st);
    if (isMulti) {
        // Targeted multi-numeric refine needs the pre-parsed target set;
        // prev-value predicates compare against each candidate's snapshot.
        if (!usePrev && (!multiTargets || multiTargets->entries.empty())) return stats;
        if (!usePrev && st == Radar::ScanType::Between
            && (!multiTargets2 || multiTargets2->entries.empty())) return stats;
    } else if (!isString) {
        if (!usePrev && !targetBytes) return stats;
        if (st == Radar::ScanType::Between && !target2Bytes) return stats;
    }

    // Canonical (3-double) vector target, decoded once — same contract as the
    // first scan. On a prev-value refine the comparison target is the
    // candidate's own stored snapshot, which is already canonical.
    double targetVec[3]  = { 0.0, 0.0, 0.0 };
    double targetVec2[3] = { 0.0, 0.0, 0.0 };
    if (isVector) {
        if (targetBytes)
            Radar::DecodeVectorBytes(targetBytes, Radar::VECTOR_CANON_BYTES, targetVec);
        if (target2Bytes)
            Radar::DecodeVectorBytes(target2Bytes, Radar::VECTOR_CANON_BYTES, targetVec2);
    }
    const double* targetVec2Ptr = target2Bytes ? targetVec2 : nullptr;

    const int32_t initialSize = static_cast<int32_t>(candidates.size());

    std::vector<Radar::Candidate> kept;
    kept.reserve(candidates.size());

    // === audit #5 A11 — re-anchor container-element candidates ================
    // Read each container header ONCE per pass: N candidates in one TArray share
    // one header, and a refine over 100K candidates must not pay N reads for it.
    struct HeaderSnapshot {
        bool                    ok     = false;
        uintptr_t               data   = 0;
        int32_t                 num    = 0;
        Macht::TSparseArrayView sa;             // populated for sparse only
    };
    std::unordered_map<uintptr_t, HeaderSnapshot> headerCache;
    auto headerFor = [&](uintptr_t headerAddr, bool sparse) -> const HeaderSnapshot& {
        auto it = headerCache.find(headerAddr);
        if (it != headerCache.end()) return it->second;
        HeaderSnapshot hs;
        if (sparse) {
            if (Macht::ReadTSparseArray(headerAddr, hs.sa)) {
                hs.ok = true; hs.data = hs.sa.Data; hs.num = hs.sa.MaxIndex;
            }
        } else {
            Macht::TArrayView av;
            if (Macht::ReadTArray(headerAddr, av)) {
                hs.ok = true; hs.data = av.Data; hs.num = av.Count;
            }
        }
        return headerCache.emplace(headerAddr, hs).first->second;
    };
    int32_t reanchorDropped = 0, reanchorRepointed = 0;

    for (auto& c : candidates) {
        // Per-(class,field) metadata (fieldType / boolFieldMask) lives in
        // the shared descriptor pool the candidate indexes into (V3-A).
        const Radar::FieldDescriptor& desc = descriptors[c.descriptorIdx];

        // Re-anchor BEFORE any read of c.addr — every branch below reads it, and a
        // container element's stored address can be stale in a way that still reads
        // cleanly (see Radar::RefineContainerAnchor). `Unknown` (deep / group /
        // native-raw) and `Direct` fall straight through, unchanged.
        if (desc.anchor == Radar::ValueAnchor::ArrayElement
            || desc.anchor == Radar::ValueAnchor::SparseElement) {
            const bool sparse = (desc.anchor == Radar::ValueAnchor::SparseElement);
            const uintptr_t headerAddr =
                instances[c.instanceIdx].instanceAddr + desc.fieldOffset;
            const HeaderSnapshot& hs = headerFor(headerAddr, sparse);
            if (!hs.ok) { ++reanchorDropped; continue; }   // container gone / unreadable

            // The scan-time buffer base is EXACT from what we already store —
            // c.addr was built as base + idx*stride + intra — so it costs no bytes
            // on the candidate.
            const uintptr_t dataAtScan = c.addr
                - static_cast<uintptr_t>(static_cast<int64_t>(c.elementIndex) * desc.elemStride)
                - static_cast<uintptr_t>(desc.elemIntra);
            const bool slotAlloc =
                !sparse || Macht::IsSparseIndexAllocated(hs.sa, c.elementIndex);

            switch (Radar::RefineContainerAnchor(desc.anchor, c.elementIndex,
                                                 c.containerNum, dataAtScan,
                                                 hs.data, hs.num, slotAlloc)) {
                case Radar::RefineAnchorVerdict::Drop:
                    ++reanchorDropped;
                    continue;
                case Radar::RefineAnchorVerdict::Repoint:
                    c.addr = Radar::ContainerElemAddr(hs.data, c.elementIndex,
                                                      desc.elemStride, desc.elemIntra);
                    c.containerNum = hs.num;
                    ++reanchorRepointed;
                    break;
                case Radar::RefineAnchorVerdict::KeepAddress:
                    c.containerNum = hs.num;   // keep the stamp current for the NEXT refine
                    break;
            }
        }
        if (isMulti) {
            // Re-resolve this candidate's own width from its stored
            // fieldType (concrete property type, e.g. "FloatProperty").
            // Targeted predicates compare against the matching target
            // entry; prev-value predicates against the snapshot.
            Radar::DataType memberDt;
            if (!Radar::TryDataTypeFromPropertyTypeName(desc.fieldType, memberDt)) continue;
            size_t msz = Radar::SizeOf(memberDt);
            uint8_t readBuf[16] = {};
            if (!Macht::ReadBytesSafe(c.addr, readBuf, msz)) continue;

            // Two shapes, and they cannot share a variable: a prev-value refine
            // compares against raw stored bytes, everything else against a target
            // ENTRY whose verdict may be "every value of this width matches"
            // (audit #5 AB4 — Find() cannot express that, FindEntry can).
            const uint8_t* cmpTarget = nullptr;
            const Radar::NumericTargetSet::Entry* cmpEntry = nullptr;
            const uint8_t* cmp2      = nullptr;
            if (usePrev) {
                cmpTarget = c.prevValue;
            } else {
                cmpEntry = multiTargets ? multiTargets->FindEntry(memberDt) : nullptr;
                if (!cmpEntry) continue;  // no value of this width can satisfy it
                if (st == Radar::ScanType::Between) {
                    cmp2 = multiTargets2 ? multiTargets2->Find(memberDt) : nullptr;
                    if (!cmp2) continue;
                }
            }
            const bool keep = cmpEntry
                ? Radar::ComparePredicate(memberDt, st, readBuf, cmpEntry, cmp2, roundMode)
                : Radar::ComparePredicate(memberDt, st, readBuf, cmpTarget, cmp2, roundMode);
            if (!keep) continue;
            std::memcpy(c.prevValue, readBuf, msz);
            kept.push_back(std::move(c));
            continue;
        }

        if (isString) {
            // Re-resolve the string from the candidate's recorded
            // address. c.addr already points at the value (FString
            // header / FName slot / FText payload) — for direct fields
            // this is instanceAddr + fieldOffset; for array elements
            // it's arrayDataPtr + index * stride. Reading from c.addr
            // with offset=0 works uniformly for both.
            //
            // Array reallocation caveat: if the underlying TArray
            // resized between scans, c.addr is stale and the read may
            // fail or return garbage. Macht's SEH-wrapped reads turn
            // bad accesses into safe failures (continue), so we drop
            // the candidate quietly; the user can First-Scan again
            // to refresh.
            std::string cur;
            if (desc.fieldType == "StrProperty") {
                cur = Ubel::ReadFStringAt(c.addr, 0);
            } else if (desc.fieldType == "NameProperty") {
                cur = Ubel::ReadFNameAt(c.addr, 0);
            } else if (desc.fieldType == "TextProperty") {
                cur = Ubel::ReadFTextStringAt(c.addr, 0);
            } else {
                // Shouldn't happen for a string-typed session, but be
                // defensive: a candidate with the wrong fieldType
                // can't be re-read, so drop it.
                continue;
            }

            const std::string& cmpTarget = usePrev ? c.prevStr : targetString;
            if (!Radar::CompareStringPredicate(st, cur, cmpTarget, caseSensitive)) continue;

            c.prevStr = std::move(cur);
            kept.push_back(std::move(c));
            continue;
        }

        if (isVector) {
            // The source width is a per-FIELD fact the descriptor carries; it
            // cannot be re-derived from desc.fieldType, which is the bare
            // "StructProperty" for every vector. A session captured before the
            // width was recorded (or a descriptor that failed the width gate)
            // has 0 here, and the candidate is dropped rather than re-read at a
            // guessed width.
            uint8_t readBuf[Radar::VECTOR_CANON_BYTES] = {};
            double  cur[3];
            if (!Radar::IsSupportedVectorWidth(desc.vectorWidth)) continue;
            if (!Macht::ReadBytesSafe(c.addr, readBuf,
                                      static_cast<size_t>(desc.vectorWidth))) continue;
            if (!Radar::DecodeVectorBytes(readBuf, desc.vectorWidth, cur)) continue;
            // A prev-value predicate compares against the candidate's stored
            // snapshot, which is ALREADY canonical — no width involved.
            double prev[3];
            const double* cmpTarget = targetVec;
            if (usePrev) {
                Radar::DecodeVectorBytes(c.prevValue, Radar::VECTOR_CANON_BYTES, prev);
                cmpTarget = prev;
            }
            if (!Radar::CompareVectorPredicate(st, cur, cmpTarget, targetVec2Ptr, roundMode)) continue;
            Radar::StoreVectorCanonical(cur, c.prevValue);
            kept.push_back(std::move(c));
            continue;
        }

        uint8_t readBuf[16] = {};
        if (!Macht::ReadBytesSafe(c.addr, readBuf, dtSize)) continue;

        if (dt == Radar::DataType::Bool
            && desc.boolFieldMask != 0 && desc.boolFieldMask != 0xFF) {
            readBuf[0] = ((readBuf[0] & desc.boolFieldMask) != 0) ? 1 : 0;
        }

        const uint8_t* cmpTarget = usePrev ? c.prevValue : targetBytes;
        if (!Radar::ComparePredicate(dt, st, readBuf, cmpTarget, target2Bytes, roundMode)) continue;

        std::memcpy(c.prevValue, readBuf, dtSize);
        kept.push_back(std::move(c));
    }

    stats.scannedObjects = initialSize;
    candidates           = std::move(kept);

    auto dtms = std::chrono::duration_cast<std::chrono::milliseconds>(
                    std::chrono::steady_clock::now() - t0).count();
    stats.durationMs = static_cast<int64_t>(dtms);

    LOG_INFO("Radar: Refine st=%d (usePrev=%d): %d -> %d candidates in %lld ms",
             static_cast<int>(st), usePrev ? 1 : 0,
             initialSize, static_cast<int>(candidates.size()),
             static_cast<long long>(dtms));
    // Only when it actually fired — a line that is always there stops being read.
    // `repointed` is the half that is a GAIN: those candidates were lost outright
    // before A11, because a grown container's realloc left every element address
    // stale. (audit #5 A11)
    if (reanchorDropped || reanchorRepointed) {
        LOG_INFO("Radar: Refine re-anchor: %d container element(s) repointed after a "
                 "realloc, %d dropped (slot freed / container shrank / header gone)",
                 reanchorRepointed, reanchorDropped);
    }
    return stats;
}

// ------------------------------------------------------------------
// Snapshot capture (Phase A1a)
// ------------------------------------------------------------------

namespace {
// Uppercase, no-prefix hex — matches Renge::BytesToHex without pulling the
// json-heavy Renge.h into this TU.
std::string SnapshotBytesToHex(const uint8_t* d, size_t n) {
    static const char* kHex = "0123456789ABCDEF";
    std::string s;
    s.reserve(n * 2);
    for (size_t i = 0; i < n; ++i) {
        s.push_back(kHex[(d[i] >> 4) & 0xF]);
        s.push_back(kHex[d[i] & 0xF]);
    }
    return s;
}

// Render a struct-array element's inner-key value to a string: FName -> its
// string; integer -> decimal; otherwise "" (caller falls back to elem index).
std::string RenderInnerKey(const FieldInfo& kf, uintptr_t elemAddr) {
    if (kf.TypeName == "NameProperty")
        return Ubel::ReadFNameAt(elemAddr, kf.Offset);

    Radar::DataType dt;
    if (Radar::TryDataTypeFromPropertyTypeName(kf.TypeName, dt)) {
        size_t sz = Radar::SizeOf(dt);
        uint8_t buf[8] = {};
        if (sz >= 1 && sz <= 8 && Macht::ReadBytesSafe(elemAddr + kf.Offset, buf, sz)) {
            switch (dt) {
                case Radar::DataType::Int8:   return std::to_string(static_cast<int>(static_cast<int8_t>(buf[0])));
                case Radar::DataType::UInt8:  return std::to_string(static_cast<unsigned>(buf[0]));
                case Radar::DataType::Int16:  { int16_t v;  std::memcpy(&v, buf, 2); return std::to_string(v); }
                case Radar::DataType::UInt16: { uint16_t v; std::memcpy(&v, buf, 2); return std::to_string(v); }
                case Radar::DataType::Int32:  { int32_t v;  std::memcpy(&v, buf, 4); return std::to_string(v); }
                case Radar::DataType::UInt32: { uint32_t v; std::memcpy(&v, buf, 4); return std::to_string(v); }
                case Radar::DataType::Int64:  { int64_t v;  std::memcpy(&v, buf, 8); return std::to_string(v); }
                case Radar::DataType::UInt64: { uint64_t v; std::memcpy(&v, buf, 8); return std::to_string(v); }
                default: break;
            }
        }
    }
    return "";
}

// Capture struct-array elements of `obj` (Phase A1b). For each
// TArray<StructProperty> field, resolve the inner UScriptStruct, pick an
// inner-key field (reorder-immune join key) + its numeric inner fields, and
// emit up to arrayCap elements.
// Capture numeric leaves reachable through containers at ANY (bounded) depth
// (build 1204), via the shared WalkContainerLeaves. The full nested path is
// baked into SnapshotArray.field (e.g.
// "SaveSlotList[1].MsTuneData.MsTunes[0].WeaponTuneList[0].Tunes") so SPC/Diff,
// which key on array_field + elem_index, get deep support with no schema change.
// Leaf-container elements (TArray<int> etc.) are captured too (leaf name "").
void CaptureStructArrays(uintptr_t obj, uintptr_t cls,
                         Radar::DataType numericScope, int32_t arrayCap,
                         std::vector<Aura::SnapshotArray>& out,
                         Aura::NumericFamily family = Aura::NumericFamily::Any,
                         bool captureTopLevelScalarArrays = false) {
    if (!obj || !cls) return;
    if (arrayCap <= 0) arrayCap = 256;

    const auto& members = Radar::MultiNumericMembers(numericScope);
    if (members.empty()) return;   // not a meta scope -> capture nothing

    // Regroup the flat leaf stream back into SnapshotArray{field, elements[]}.
    std::unordered_map<std::string, size_t> arrPos;    // arrayPath -> out[] index
    std::unordered_map<std::string, size_t> elemPos;   // arrayPath\x01idx -> element pos within that array

    WalkLeafLimits lim;
    lim.maxDepth = 4;
    lim.maxElems = arrayCap;
    // Per-object budget (build 1211): the 256-elem × depth-4 caps still allow a
    // pathological object (deeply-nested WIDE container graph) to visit billions
    // of elements — on SEED a cluster of such objects stalled one chunk ~24s and
    // the user cancelled (perceived hang). Bound each object DETERMINISTICALLY by
    // total elements visited (reproducible → SPC diff stays consistent), with a
    // short wall-clock backstop for pathological per-element cost (huge structs /
    // slow reads). 50k is far above any real object yet caps the blow-up to tens
    // of ms; the chunk loop's own cancel poll handles client-gone between objects.
    int64_t visited = 0;
    lim.maxTotalElems = kDeepWalkMaxTotalElems;
    const auto t0 = std::chrono::steady_clock::now();
    constexpr auto kPerObjBackstop = std::chrono::milliseconds(750);
    lim.aborted  = [t0] {
        return Tot::Requested()
            || (std::chrono::steady_clock::now() - t0) > kPerObjBackstop;
    };

    WalkContainerLeaves(obj, cls, /*pathPrefix*/ "", /*depth*/ 0, lim,
        [&](const ContainerLeaf& lf) {
            // Skip TOP-LEVEL leaf-containers (a TArray<int> directly on the object,
            // depth==1) by default: the main capture loop never tracked those, and
            // capturing every object's scalar arrays would balloon the DB.
            // EXCEPTION (build 1827): for GAMEPLAY classes (captureTopLevelScalarArrays
            // — Actor/Pawn/component/PlayerState/... — the value carriers) keep these.
            // A Pawn's numeric stat-bank TArray<float> (e.g. SupportActionGauge[]) is
            // exactly a hack target users hunt, and was previously invisible to Snapshot
            // Diff / SPC / Pivot (Value Search already finds it via the live Array scan).
            // Struct-array element fields (e.g. SaveSlotList[1].GP, depth 1) ARE always
            // captured, and nested leaf-containers (Tunes[N], depth >= 2) too. (build 1204)
            if (lf.leafName.empty() && lf.depth < 2 && !captureTopLevelScalarArrays) return;
            // Snapshot tracks only numeric leaves within the configured scope.
            Radar::DataType ldt;
            if (!Radar::TryDataTypeFromPropertyTypeName(lf.leafType, ldt)) return;
            bool inScope = false;
            for (Radar::DataType m : members) if (m == ldt) { inScope = true; break; }
            if (!inScope) return;
            if (!Aura::NumericDataTypeInFamily(ldt, family)) return;   // type-family narrowing
            size_t sz = Radar::SizeOf(ldt);
            if (sz == 0 || sz > 8) return;
            uint8_t buf[8] = {};
            if (!Macht::ReadBytesSafe(lf.leafAddr, buf, sz)) return;

            // Find/create the SnapshotArray for this path.
            size_t aPos;
            auto ai = arrPos.find(lf.arrayPath);
            if (ai == arrPos.end()) {
                aPos = out.size();
                Aura::SnapshotArray sa; sa.field = lf.arrayPath;
                out.push_back(std::move(sa));
                arrPos.emplace(lf.arrayPath, aPos);
            } else aPos = ai->second;

            // Find/create the element (resolve inner-key once, on first leaf).
            std::string ekey = lf.arrayPath; ekey += '\x01'; ekey += std::to_string(lf.elemIndex);
            size_t ePos;
            auto ei = elemPos.find(ekey);
            if (ei == elemPos.end()) {
                ePos = out[aPos].elements.size();
                Aura::SnapshotArrayElement el; el.index = lf.elemIndex;
                if (lf.elemStructAddr) {
                    const ClassInfo& eci = Ubel::WalkClassEx(lf.elemStructAddr);  // memoized (B10) — by ref
                    std::vector<std::string> types, names;
                    types.reserve(eci.Fields.size()); names.reserve(eci.Fields.size());
                    for (const auto& ff : eci.Fields) { types.push_back(ff.TypeName); names.push_back(ff.Name); }
                    int kIdx = Radar::SelectArrayInnerKey(types, names);
                    if (kIdx >= 0 && kIdx < static_cast<int>(eci.Fields.size())) {
                        el.keyName  = eci.Fields[kIdx].Name;
                        el.keyValue = RenderInnerKey(eci.Fields[kIdx], lf.elemBaseAddr);
                    }
                }
                out[aPos].elements.push_back(std::move(el));
                elemPos.emplace(ekey, ePos);
            } else ePos = ei->second;

            Aura::SnapshotField f2;
            f2.name = lf.leafName;   // "" for a leaf-container element (the element IS the value)
            f2.offset = 0;           // element-relative; not meaningful for a deep path
            f2.type = lf.leafType;
            f2.hex  = SnapshotBytesToHex(buf, sz);
            out[aPos].elements[ePos].fields.push_back(std::move(f2));
        }, &visited);
}

// Capture the inner numeric leaves of the object's PLAIN (non-container)
// StructProperty members — the case neither the scalar picks loop (numeric leaves
// only) nor CaptureStructArrays (containers only) covers. This is where GAS
// FGameplayAttributeData.BaseValue/CurrentValue (the #1 hack target) live, plus
// FVector/FRotator components (Location/Rotation). Reuses EmitStructDirectLeaves (the
// same struct descent the container path uses for struct ELEMENTS) and emits each as a
// scalar field named "Health.BaseValue" at the OBJECT-relative offset, so the
// group/SPC/diff/pivot consumers treat it exactly like a top-level scalar (found even
// without the "Deep" query toggle, which only folds in container rows). Bounded by
// EmitStructDirectLeaves' kMaxStructDepth(4) + a per-object leaf cap. The numericScope +
// family filter is applied per leaf, matching the top-level pass. (build 1648)
void CaptureDirectStructFields(uintptr_t obj, uintptr_t cls,
                               Radar::DataType numericScope, Aura::NumericFamily family,
                               std::vector<Aura::SnapshotField>& out) {
    if (!obj || !cls) return;
    const auto& members = Radar::MultiNumericMembers(numericScope);
    if (members.empty()) return;   // not a meta scope -> capture nothing

    const ClassInfo& ci = Ubel::WalkClassEx(cls);   // memoized (B10) — BY REF, no copy
    constexpr int kMaxStructLeafFields = 512;        // per object, defensive
    int added = 0;
    for (const auto& f : ci.Fields) {
        if (added >= kMaxStructLeafFields) break;
        if (f.TypeName != "StructProperty" || !f.Address) continue;
        uintptr_t nested = 0;
        if (!Macht::ReadSafe(f.Address + DynOff::FSTRUCTPROP_STRUCT, nested) || !nested) continue;

        EmitStructDirectLeaves(nested, obj + f.Offset, /*arrayPath*/ "", /*elemIndex*/ 0,
                               /*elemStructAddr*/ 0, /*elemBaseAddr*/ 0, /*namePrefix*/ f.Name,
                               // Not a container at all. These leaves are depth 0, so the
                               // coverage predicate short-circuits before it reads this --
                               // but Direct is the honest label, not an arbitrary sentinel.
                               ContainerKind::Direct,
                               // No container here either, so there is nothing to re-anchor
                               // against. `Direct` rather than a default, so `Unknown` keeps
                               // meaning "a hop dropped the stamp". (audit #5 A12)
                               Radar::MakeDirectLeafAnchor(),
                               /*depth*/ 0, /*structDepth*/ 0,
            [&](const ContainerLeaf& lf) {
                if (added >= kMaxStructLeafFields) return;
                Radar::DataType ldt;
                if (!Radar::TryDataTypeFromPropertyTypeName(lf.leafType, ldt)) return;
                bool inScope = false;
                for (Radar::DataType m : members) if (m == ldt) { inScope = true; break; }
                if (!inScope) return;
                if (!Aura::NumericDataTypeInFamily(ldt, family)) return;   // type-family narrowing
                size_t sz = Radar::SizeOf(ldt);
                if (sz == 0 || sz > 8) return;
                uint8_t buf[8] = {};
                if (!Macht::ReadBytesSafe(lf.leafAddr, buf, sz)) return;
                Aura::SnapshotField sf;
                sf.name   = lf.leafName;                              // "Health.BaseValue"
                sf.offset = static_cast<int32_t>(lf.leafAddr - obj);  // object-relative
                sf.type   = lf.leafType;
                sf.hex    = SnapshotBytesToHex(buf, sz);
                out.push_back(std::move(sf));
                ++added;
            });
    }
}
} // namespace

// ============================================================
// Multiple values group scan (build 1276) — see Aura.h + Orden.h.
// ============================================================
namespace {

// One numeric leaf's display/refine metadata, index-aligned with the
// Orden::Leaf vector produced per block (leaves[k] <-> metas[k]).
struct GroupLeafMeta {
    std::string fieldName;       // direct: "Stats.Str"; deep: "SaveSlotList[1]...Tunes[2]"
    std::string fieldType;       // UE property type ("IntProperty" / "FloatProperty" / ...)
    std::string definingClass;   // struct/class the leaf is declared in
    int32_t     offset       = 0;   // bytes from the OWNING object base (direct); 0 for deep container leaves
    int32_t     elementIndex = -1;  // -1 direct field; >=0 container element index
    uintptr_t   leafAddr     = 0;   // ABSOLUTE value address (direct: owner+offset; deep: element addr)
    uintptr_t   ownerAddr    = 0;   // object directly holding the leaf (actor, or owned sub-object for P4)
    std::string ownerClass;         // class name of ownerAddr's object (P4 inc 2; drives the Pivot handoff)
    uint8_t     boolMask     = 0xFF;
    bool        isNativeC    = false;  // P2: raw-hole leaf (unmanaged, non-UPROPERTY)
    std::string guessedType;           // P2: interpreted width label (e.g. "Int32"); "" for reflected
    // audit #5 A12 — relayed verbatim to GroupSlotMatch so refine can re-anchor a
    // container element. Defaults to Unknown/-1, which the rule refuses to act on.
    Radar::LeafAnchor anchor;
};

// One deep block = a numeric container's elements, or one struct-array/map
// element's inner numeric fields. Orden runs over each block independently so a
// group is matched WITHIN one array/element (the "array as a block" rule), not
// scattered across the whole object's deep tree.
struct GroupBlock {
    std::vector<Orden::Leaf>   leaves;
    std::vector<GroupLeafMeta> metas;
};

// Is `typeName` a numeric scalar in scope? `wantByte` includes Int8/UInt8.
// Bool + non-numeric return false (TryDataTypeFromPropertyTypeName rejects Bool).
inline bool GroupNumericLeafType(const std::string& typeName, bool wantByte,
                                 Radar::DataType& dt) {
    if (!Radar::TryDataTypeFromPropertyTypeName(typeName, dt)) return false;
    if ((dt == Radar::DataType::Int8 || dt == Radar::DataType::UInt8) && !wantByte) return false;
    const size_t sz = Radar::SizeOf(dt);
    return sz >= 1 && sz <= 8;
}

// Collect an object's numeric scalar leaves: direct fields + depth-capped
// direct-StructProperty descent (mirrors ScanForValue's reach for P1). Numeric
// containers (TArray/TSet/TMap) are P3 and intentionally not followed here.
// `obj` is the instance base; `structAddr` is the UStruct being walked;
// `baseOffset` accumulates obj->structAddr. leaves[k] <-> metas[k].
void CollectGroupLeaves(uintptr_t obj, const std::string& ownerClassName,
                        uintptr_t structAddr, int32_t baseOffset,
                        const std::string& namePrefix, bool wantByte, int depth,
                        std::vector<uintptr_t>& visited,
                        std::vector<Orden::Leaf>& leaves,
                        std::vector<GroupLeafMeta>& metas, size_t leafCap) {
    constexpr int kMaxGroupDepth = 4;
    if (depth > kMaxGroupDepth || !structAddr) return;
    if (leaves.size() >= leafCap) return;
    for (uintptr_t v : visited) if (v == structAddr) return;  // cycle guard
    visited.push_back(structAddr);

    const ClassInfo& ci = Ubel::WalkClassEx(structAddr);  // memoized per struct (B10)
    for (const auto& f : ci.Fields) {
        if (leaves.size() >= leafCap) break;
        Radar::DataType dt;
        if (GroupNumericLeafType(f.TypeName, wantByte, dt)) {
            const size_t sz = Radar::SizeOf(dt);
            uint8_t buf[8] = {};
            if (!Macht::ReadBytesSafe(obj + baseOffset + f.Offset, buf, sz)) continue;
            Orden::Leaf lf;
            lf.position     = baseOffset + f.Offset;
            lf.width        = dt;
            lf.elementIndex = -1;
            std::memcpy(lf.bytes, buf, sz);
            leaves.push_back(lf);
            GroupLeafMeta m;
            m.fieldName     = namePrefix.empty() ? f.Name : (namePrefix + "." + f.Name);
            m.fieldType     = f.TypeName;
            m.definingClass = ci.Name;
            m.offset        = baseOffset + f.Offset;
            m.elementIndex  = -1;
            m.leafAddr      = obj + baseOffset + f.Offset;   // direct: absolute = obj + offset
            m.ownerAddr     = obj;                            // the object directly holding this leaf
            m.ownerClass    = ownerClassName;                 // class of obj (constant across this walk)
            m.boolMask      = f.boolFieldMask;
            m.anchor        = Radar::MakeDirectLeafAnchor();   // A12: not a container element
            metas.push_back(std::move(m));
        } else if (f.TypeName == "StructProperty" && f.Address) {
            uintptr_t nested = 0;
            if (Macht::ReadSafe(f.Address + DynOff::FSTRUCTPROP_STRUCT, nested) && nested) {
                CollectGroupLeaves(obj, ownerClassName, nested, baseOffset + f.Offset,
                                   namePrefix.empty() ? f.Name : (namePrefix + "." + f.Name),
                                   wantByte, depth + 1, visited, leaves, metas, leafCap);
            }
        }
    }
    visited.pop_back();
}

// True when `child`'s Outer chain reaches `actor` within `maxHops` steps. A
// UActorComponent's Outer is the actor (1 hop); a GAS UAttributeSet's Outer is
// the UAbilitySystemComponent whose Outer is the actor (2 hops). Bounds the
// cross-object reach (P4) to objects the actor genuinely OWNS — a pointer to a
// shared / global object (another actor, the world, GameInstance) fails the test
// and is never followed.
bool IsOwnedBy(uintptr_t child, uintptr_t actor, int maxHops) {
    uintptr_t o = child;
    for (int h = 0; h < maxHops; ++h) {
        o = Ubel::GetOuter(o);
        if (!o) return false;
        if (o == actor) return true;
    }
    return false;
}

// Cross-object block assembly (P4, approach C). Append the numeric leaves of the
// sub-objects `actor` OWNS to the actor's block (`leaves`/`metas`), so a group
// whose values are distributed across {actor, its components, its GAS
// AttributeSets} is matched as ONE block. A bounded 2-level BFS over OWNED objects:
//   depth 1 — the actor's direct owned sub-objects (custom HealthComponent /
//             StatsComponent / InventoryComponent / the ASC itself);
//   depth 2 — each of those sub-objects' owned objects (the GAS ASC's
//             SpawnedAttributes -> the UAttributeSet objects), so a value held on
//             an AttributeSet is reached actor -> ASC -> AttributeSet.
// Sub-objects are discovered with EnumerateOutgoingObjectPtrs (the same
// outgoing-pointer adapter Locate-in-GWorld uses — direct ObjectProperty fields
// AND object-pointer CONTAINERS like OwnedComponents TSet / SpawnedAttributes
// TArray, which neither P1 nor Deep walks) and kept only when the actor OWNS them
// (IsOwnedBy: Outer chains back to the actor within 2 hops). Selectivity is the
// value AND across slots, NOT a class-name filter (a Mesh's RelativeLocation simply
// won't equal the searched values). Each sub-object leaf carries its own ownerAddr
// (the sub-object) so the handoffs land on the right object. Bounded by leafCap +
// a sub-object count cap.
void AppendOwnedSubObjectLeaves(uintptr_t actor, bool wantByte,
                                std::vector<Orden::Leaf>& leaves,
                                std::vector<GroupLeafMeta>& metas, size_t leafCap) {
    constexpr int    kMaxOwnedSubs = 64;   // most actors own < 20 components
    constexpr int    kMaxOwnDepth  = 2;    // actor -> component -> AttributeSet
    int subCount = 0;
    std::unordered_set<uintptr_t> seen;
    seen.insert(actor);                    // never re-collect the actor itself
    std::vector<uintptr_t> subVisited;

    // Explicit owned-object work-list (object + accumulated display path + depth).
    struct Frontier { uintptr_t obj; std::string prefix; int depth; };
    std::vector<Frontier> frontier;
    frontier.push_back({actor, std::string(), 0});

    while (!frontier.empty()) {
        Frontier cur = frontier.back();
        frontier.pop_back();
        if (cur.depth >= kMaxOwnDepth) continue;   // don't expand past the depth bound
        EnumerateOutgoingObjectPtrs(cur.obj,
            [&](uintptr_t child, int32_t /*ptrOff*/, const std::string& ptrName,
                const std::string& /*ptrType*/, const std::string& /*innerType*/,
                int32_t elemIdx, int32_t /*elemStride*/, int32_t /*elemValueOffset*/) -> bool {
                if (leaves.size() >= leafCap || subCount >= kMaxOwnedSubs) return true;  // stop
                if (!child || seen.count(child)) return false;
                if (!IsOwnedBy(child, actor, /*maxHops*/ kMaxOwnDepth)) return false;
                uintptr_t childCls = Ubel::GetClass(child);
                if (!childCls) return false;
                seen.insert(child);
                ++subCount;
                // Accumulate the path so the offset table reads e.g.
                // "HealthComp.CurrentHealth" or "AbilitySystem.SpawnedAttributes[0].Health.CurrentValue".
                std::string prefix = cur.prefix;
                if (!prefix.empty()) prefix += ".";
                prefix += ptrName;
                if (elemIdx >= 0) { prefix += "["; prefix += std::to_string(elemIdx); prefix += "]"; }
                subVisited.clear();
                // Each cross-object leaf is OWNED by this sub-object, so its Pivot
                // handoff class is the sub-object's class, not the actor's (P4 inc 2).
                std::string childClassName = Ubel::GetName(childCls);
                CollectGroupLeaves(child, childClassName, childCls, 0, prefix, wantByte, 0,
                                   subVisited, leaves, metas, leafCap);
                frontier.push_back({child, prefix, cur.depth + 1});   // expand one more level
                return false;  // keep enumerating this parent's other owned children
            });
    }
}

// Feasibility re-check after a refine prunes per-slot lists: a System of
// Distinct Representatives must still exist, each leaf keyed by its ABSOLUTE
// address so two slots can't both claim the same value (works for direct and
// deep container leaves alike).
bool GroupCandidateFeasible(const Radar::GroupCandidate& gc) {
    std::vector<uintptr_t> leafKeys;
    auto keyIdx = [&](uintptr_t a) -> int {
        for (size_t k = 0; k < leafKeys.size(); ++k)
            if (leafKeys[k] == a) return static_cast<int>(k);
        leafKeys.push_back(a);
        return static_cast<int>(leafKeys.size() - 1);
    };
    std::vector<Orden::SlotMatches> m(gc.slotMatches.size());
    for (size_t s = 0; s < gc.slotMatches.size(); ++s)
        for (const auto& sm : gc.slotMatches[s])
            m[s].leafIdx.push_back(keyIdx(sm.leafAddr));
    return Orden::HasDistinctAssignment(m, static_cast<int>(leafKeys.size()));
}

}  // namespace

// === Related-object graph (forward, owned) — see Aura.h for the contract ===
// Reuses EnumerateOutgoingObjectPtrs (the outgoing-pointer adapter) + IsOwnedBy
// (the Outer-chain ownership gate) — the SAME pieces the P4 cross-object group
// scan uses, here collecting OBJECTS instead of numeric leaves. Bounded + fast.
std::vector<RelatedObject> GetRelatedObjects(uintptr_t target, int32_t maxResults) {
    std::vector<RelatedObject> out;
    if (!target) return out;
    if (maxResults <= 0) maxResults = 128;

    // Bound the owned walk: a wall-clock deadline + cooperative cancel + a hard
    // emit-iteration cap, so a target exposing a huge reflected object-pointer
    // container (e.g. an AllActors-style TArray<AActor*> with up to ~1M
    // elements) can't stall the synchronous pipe worker. Mirrors
    // FindObjectGraphPath's abort pattern (rejected elements never advance the
    // add-caps, so without this the loop is unbounded).
    auto t0 = std::chrono::steady_clock::now();
    constexpr int64_t kDeadlineMs = 8000;
    constexpr int64_t kMaxVisited = 200000;
    int64_t visited = 0;
    auto aborted = [&]() -> bool {
        if (Tot::Requested()) return true;
        return std::chrono::duration_cast<std::chrono::milliseconds>(
                   std::chrono::steady_clock::now() - t0).count() > kDeadlineMs;
    };

    // Dedup across the WHOLE result. Seeding `seen` from add() means a
    // hierarchy/counterpart object (Class / Outer / Controller / Pawn) can't be
    // re-emitted as an owned sub-object — Controller/Pawn are reflected
    // ObjectProperty fields, so the owned BFS would otherwise re-walk them on a
    // game whose pawn owns a controller-like sub-object.
    std::unordered_set<uintptr_t> seen;
    auto add = [&](uintptr_t obj, const char* relation, const std::string& fieldName,
                   int32_t fieldOffset, int32_t depth, uintptr_t parent) {
        if (!obj || out.size() >= static_cast<size_t>(maxResults)) return;
        RelatedObject r;
        r.addr        = obj;
        int32_t idx   = -1;
        r.index       = Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_INDEX, idx) ? idx : -1;
        r.name        = Ubel::GetName(obj);
        uintptr_t c   = Ubel::GetClass(obj);
        r.className   = c ? Ubel::GetName(c) : std::string();
        r.relation    = relation;
        r.fieldName   = fieldName;
        r.fieldOffset = fieldOffset;
        r.depth       = depth;
        r.parentAddr  = parent;
        seen.insert(obj);
        out.push_back(std::move(r));
    };

    // --- Hierarchy: Self / Class / Outer ---
    add(target, "Self", std::string(), -1, 0, 0);
    uintptr_t cls = Ubel::GetClass(target);
    if (cls) add(cls, "Class", std::string(), -1, 0, target);
    if (uintptr_t outer = Ubel::GetOuter(target)) add(outer, "Outer", std::string(), -1, 0, target);

    // --- Counterpart: Pawn <-> Controller (reflected field by name) ---
    if (cls) {
        int32_t ctrlOff = Ubel::FindFieldOffset(cls, "Controller");
        if (ctrlOff >= 0) {
            uintptr_t ctrl = 0;
            if (Macht::ReadSafe(target + ctrlOff, ctrl) && ctrl)
                add(ctrl, "Controller", "Controller", ctrlOff, 0, target);
        }
        const char* pawnField = "AcknowledgedPawn";
        int32_t pawnOff = Ubel::FindFieldOffset(cls, "AcknowledgedPawn");
        if (pawnOff < 0) { pawnField = "Pawn"; pawnOff = Ubel::FindFieldOffset(cls, "Pawn"); }
        if (pawnOff >= 0) {
            uintptr_t pawn = 0;
            if (Macht::ReadSafe(target + pawnOff, pawn) && pawn)
                add(pawn, "Pawn", pawnField, pawnOff, 0, target);
        }
    }

    // --- Owned sub-objects (up to depth 3): components/ASC, then the ASC's
    //     AttributeSets — incl. pawn -> stats component -> ASC -> AttributeSet ---
    // Label is a class-name convenience; discovery is the structural owned walk.
    auto classify = [](const std::string& clsName) -> const char* {
        std::string lo = clsName;
        for (auto& ch : lo) ch = static_cast<char>(std::tolower(static_cast<unsigned char>(ch)));
        if (lo.find("abilitysystemcomponent") != std::string::npos) return "AbilitySystem (ASC)";
        // Guard "attributeset" against a class that is ALSO a component (e.g. a
        // hypothetical "AttributeSetComponent") so it isn't mislabeled.
        if (lo.find("attributeset") != std::string::npos
            && lo.find("component") == std::string::npos) return "AttributeSet";
        if (lo.find("component")              != std::string::npos) return "Owned Component";
        return "Owned Object";
    };

    constexpr int kMaxOwnedSubs = 128;
    // Depth 3 so a GAS AttributeSet nested behind a stats/ability component is
    // reached when entering from the PAWN: pawn -> stats component -> ASC ->
    // AttributeSet (some games — e.g. TQ2 — don't hang the ASC directly off the
    // actor). Still bounded by kMaxOwnedSubs + the seen-set; IsOwnedBy uses the
    // same hop budget so a depth-3 leaf whose Outer chains back is still kept.
    constexpr int kMaxOwnDepth  = 3;     // pawn -> stats component -> ASC -> AttributeSet
    // `seen` is declared above and already holds `target` (add(target,"Self",...)
    // inserted it) plus every hierarchy/counterpart object, so none of those can
    // reappear as an owned row.
    int subCount = 0;
    struct Frontier { uintptr_t obj; int depth; };
    std::vector<Frontier> frontier;
    frontier.push_back({target, 0});
    while (!frontier.empty()) {
        Frontier cur = frontier.back();
        frontier.pop_back();
        if (cur.depth >= kMaxOwnDepth) continue;
        if (out.size() >= static_cast<size_t>(maxResults) || subCount >= kMaxOwnedSubs) break;
        if (aborted()) break;
        EnumerateOutgoingObjectPtrs(cur.obj,
            [&](uintptr_t child, int32_t ptrOff, const std::string& ptrName,
                const std::string& /*ptrType*/, const std::string& /*innerType*/,
                int32_t elemIdx, int32_t /*elemStride*/, int32_t /*elemValueOffset*/) -> bool {
                // Bound REJECTED iterations too (a huge non-owned container would
                // otherwise spin without ever advancing the add-caps).
                if (++visited > kMaxVisited || aborted()) return true;  // stop enumerating
                if (out.size() >= static_cast<size_t>(maxResults) || subCount >= kMaxOwnedSubs)
                    return true;  // stop enumerating
                if (!child || seen.count(child)) return false;
                if (!IsOwnedBy(child, target, kMaxOwnDepth)) return false;
                uintptr_t childCls = Ubel::GetClass(child);
                if (!childCls) return false;
                seen.insert(child);
                ++subCount;
                std::string fname = ptrName;
                if (elemIdx >= 0) { fname += '['; fname += std::to_string(elemIdx); fname += ']'; }
                // A container ELEMENT pointer lives in the heap Data buffer, not
                // at cur.obj+ptrOff (which is the container header field), so
                // report -1 rather than a misleading "@ 0xNN" handoff hint; the
                // [idx] is already encoded in fname.
                int32_t foff = (elemIdx >= 0) ? -1 : ptrOff;
                add(child, classify(Ubel::GetName(childCls)), fname, foff, cur.depth + 1, cur.obj);
                frontier.push_back({child, cur.depth + 1});
                return false;  // keep enumerating this parent's other owned children
            });
    }
    return out;
}

// Native-C (P2, opt-in). Append an object's UNMANAGED-hole leaves to its group
// block, so a group whose values include native (non-UPROPERTY) C++ members is
// matched. Computes the class's holes (complement of reflected coverage within
// [UObject header, PropertiesSize), via Ubel::ComputeClassHoles), reads the window
// ONCE (SEH-safe), and at each aligned offset tests each candidate `width`.
//
// EMIT-ON-MATCH: unlike the reflected CollectGroupLeaves (which emits every field
// because reflected fields are few), a raw scan would emit thousands of useless
// leaves per object (every offset × every width). So a raw leaf is emitted ONLY
// when its bytes satisfy at least ONE slot's predicate — the same leaves Orden
// would keep anyway. This keeps the leaf set tiny (matches are rare), the SDR cheap,
// and — critically — means the per-object cap never truncates a REAL match (the
// emit-all + small-cap version could drop the 2nd value past the cap). `slots`
// carry the per-slot targets/predicate; `widths` is their width union (read once
// per offset). Each emitted leaf is synthetic (isNativeC, "<raw@0xNN>", canonical
// fieldType so refine round-trips). OBJECT-BLOCK ONLY (never deep). Bounded by a
// probe cap + the per-object emit cap + the shared leafCap. Stride fixed at 4.
void AppendRawHoleLeaves(uintptr_t obj, uintptr_t cls, const std::string& className,
                         const std::vector<Radar::SlotSpec>& slots,
                         const std::vector<Radar::DataType>& widths,
                         std::vector<uint8_t>& winBuf,
                         std::vector<Orden::Leaf>& leaves,
                         std::vector<GroupLeafMeta>& metas, size_t leafCap) {
    if (widths.empty() || leaves.size() >= leafCap) return;

    const ClassInfo& ci = Ubel::WalkClassEx(cls);   // memoized (B10) — BY REF, no copy
    const int32_t headerEnd = DynOff::UOBJECT_OUTER + 8;
    constexpr int32_t kSanity   = 0x10000;
    constexpr int32_t kFallback = 0x400;
    int32_t propsSize = ci.PropertiesSize;
    int32_t winEnd = (propsSize > headerEnd && propsSize <= kSanity) ? propsSize
                   : (propsSize > kSanity) ? kSanity
                   : (headerEnd + kFallback);
    if (winEnd <= headerEnd) return;

    auto holes = Ubel::ComputeClassHoles(ci, headerEnd, winEnd);
    if (holes.empty()) return;

    winBuf.resize(static_cast<size_t>(winEnd - headerEnd));
    if (!Macht::ReadBytesSafe(obj + headerEnd, winBuf.data(), winBuf.size())) return;

    constexpr int32_t kMaxRawGroupLeaves = 64;     // emitted (matching) raw leaves / object
    constexpr int32_t kMaxRawGroupProbes = 65536;  // offset×width probes / object (cost bound)
    constexpr int32_t kStride            = 4;
    int32_t rawAdded = 0, probes = 0;
    for (const auto& hole : holes) {
        if (rawAdded >= kMaxRawGroupLeaves || probes >= kMaxRawGroupProbes
            || leaves.size() >= leafCap) break;
        int32_t off = (hole.start + (kStride - 1)) & ~(kStride - 1);
        for (; off < hole.end; off += kStride) {
            if (rawAdded >= kMaxRawGroupLeaves || probes >= kMaxRawGroupProbes
                || leaves.size() >= leafCap) break;
            const uint8_t* p = winBuf.data() + (off - headerEnd);
            for (Radar::DataType w : widths) {
                size_t sz = Radar::SizeOf(w);
                if (sz == 0 || off + static_cast<int32_t>(sz) > hole.end) continue;
                ++probes;
                // Emit only if these bytes satisfy SOME slot's predicate at this width.
                bool matchesAny = false;
                for (const auto& sp : slots) {
                    const uint8_t* tgt = sp.targets.Find(w);
                    if (!tgt) continue;
                    const uint8_t* tgt2 = nullptr;
                    if (sp.st == Radar::ScanType::Between) {
                        tgt2 = sp.targets2.Find(w);
                        if (!tgt2) continue;
                    }
                    if (Radar::ComparePredicate(w, sp.st, p, tgt, tgt2, sp.roundMode)) {
                        matchesAny = true; break;
                    }
                }
                if (!matchesAny) continue;

                Orden::Leaf lf;
                lf.position     = off;
                lf.width        = w;
                lf.elementIndex = -1;
                std::memcpy(lf.bytes, p, sz);
                leaves.push_back(lf);
                GroupLeafMeta m;
                char nb[32];
                snprintf(nb, sizeof(nb), "<raw@0x%X>", off);
                m.fieldName     = nb;
                m.fieldType     = Radar::PropertyTypeNameOf(w);
                m.definingClass = "";
                m.offset        = off;
                m.elementIndex  = -1;
                m.leafAddr      = obj + off;
                m.ownerAddr     = obj;
                m.ownerClass    = className;
                m.boolMask      = 0xFF;
                m.isNativeC     = true;
                m.guessedType   = Radar::NameOf(w);
                m.anchor        = Radar::MakeDirectLeafAnchor();   // A12: a raw hole in the object body
                metas.push_back(std::move(m));
                if (++rawAdded >= kMaxRawGroupLeaves) break;
            }
        }
    }
}

GroupScanResult ScanForValueGroup(const std::vector<Radar::SlotSpec>& slots,
                                  bool gameOnly, int32_t maxResults, bool deep,
                                  bool crossObject, bool nativeC, bool newestFirst,
                                  int32_t deadlineMs, bool preFilterNoise,
                                  int perSlotCap) {
    GroupScanResult result;
    auto t0 = std::chrono::steady_clock::now();
    const auto kDeadline = std::chrono::milliseconds(deadlineMs > 0 ? deadlineMs : 15000);

    const size_t nSlots = slots.size();
    // Set if any object had more satisfying leaves than the per-slot cap. Reported once
    // at the end: a truncated set is still a correct FIRST scan, but a later
    // Changed/Decreased refine can only re-read what was kept -- which is how a cap of 8
    // hid every derived-class field behind AActor's.
    bool capHit = false;
    if (nSlots < 2) return result;                       // a group needs >= 2 values
    for (const auto& sp : slots)
        if (sp.targets.entries.empty()) return result;   // a slot that fits no width => no hits

    // Lean leaf enumeration: only read 1-byte fields when a slot wants them.
    bool wantByte = false;
    for (const auto& sp : slots)
        if (sp.dt == Radar::DataType::NumericAll ||
            sp.dt == Radar::DataType::Int8 || sp.dt == Radar::DataType::UInt8) { wantByte = true; break; }

    // Native-C (P2): the union of widths any slot can represent (distinct dt across
    // all slots' target entries). The raw-hole pass reads only these widths at each
    // hole offset, so an int-only group never probes float holes, etc.
    std::vector<Radar::DataType> nativeWidths;
    if (nativeC) {
        for (const auto& sp : slots)
            for (const auto& e : sp.targets.entries) {
                bool seen = false;
                for (Radar::DataType w : nativeWidths) if (w == e.dt) { seen = true; break; }
                if (!seen) nativeWidths.push_back(e.dt);
            }
    }
    std::vector<uint8_t> nativeWinBuf;   // reused per-object window buffer

    std::vector<Orden::SlotTarget> ordenSlots;
    ordenSlots.reserve(nSlots);
    for (const auto& sp : slots) {
        Orden::SlotTarget t;
        t.targets   = &sp.targets;
        t.st        = sp.st;
        t.roundMode = sp.roundMode;
        t.targets2  = &sp.targets2;   // Between upper bound (unused for other types)
        ordenSlots.push_back(t);
    }

    const int32_t total = GetCount();
    constexpr size_t kLeafCap        = 4096;   // object-block leaf cap
    constexpr size_t kMaxDeepBlocks  = 8192;   // distinct container blocks per object
    constexpr size_t kMaxBlockLeaves = 1024;   // leaves per deep block

    std::unordered_map<uintptr_t, char>       eligible;        // cls -> 1 keep / 0 skip (game_only)
    std::unordered_map<uintptr_t, uint32_t>   instanceIntern;  // obj -> instanceIdx (blocks share)
    std::unordered_map<std::string, uint32_t> descIntern;      // "class|name|off" -> descriptorIdx

    auto internInstance = [&](uintptr_t obj, int32_t objIndex, const std::string& name) -> uint32_t {
        auto it = instanceIntern.find(obj);
        if (it != instanceIntern.end()) return it->second;
        uint32_t idx = static_cast<uint32_t>(result.instances.size());
        Radar::InstanceRecord inst;
        inst.instanceAddr  = obj;
        inst.instanceIndex = objIndex;
        inst.instanceName  = name;
        result.instances.push_back(std::move(inst));
        instanceIntern.emplace(obj, idx);
        return idx;
    };

    auto internDesc = [&](const std::string& className, const GroupLeafMeta& meta) -> uint32_t {
        // Direct leaves intern by (class, field, offset) across instances; deep
        // leaves carry the fully-indexed path in fieldName so they're distinct.
        std::string key = className; key += '|'; key += meta.fieldName;
        key += '|'; key += std::to_string(meta.offset);
        auto it = descIntern.find(key);
        if (it != descIntern.end()) return it->second;
        Radar::FieldDescriptor d;
        d.className         = className;
        d.definingClassName = meta.definingClass;
        d.fieldName         = meta.fieldName;
        d.fieldType         = meta.fieldType;
        d.fieldOffset       = meta.offset;
        d.boolFieldMask     = meta.boolMask;
        d.isNativeC         = meta.isNativeC;     // P2: native-C badge flows via the descriptor
        d.guessedType       = meta.guessedType;
        uint32_t idx = static_cast<uint32_t>(result.descriptors.size());
        result.descriptors.push_back(std::move(d));
        descIntern.emplace(std::move(key), idx);
        return idx;
    };

    // Emit one group candidate from a matched block (object-block or deep-block).
    auto emitGroupCandidate = [&](uintptr_t obj, int32_t objIndex, const std::string& name,
                                  const std::string& className,
                                  const std::vector<Orden::Leaf>& blkLeaves,
                                  const std::vector<GroupLeafMeta>& blkMetas,
                                  const std::vector<Orden::SlotMatches>& mout) {
        uint32_t instanceIdx = internInstance(obj, objIndex, name);
        Radar::GroupCandidate gc;
        gc.instanceIdx = instanceIdx;
        gc.slotMatches.resize(nSlots);
        for (size_t s = 0; s < nSlots; ++s) {
            for (int leafIdx : mout[s].leafIdx) {
                const GroupLeafMeta& meta = blkMetas[static_cast<size_t>(leafIdx)];
                Radar::GroupSlotMatch sm;
                sm.descriptorIdx = internDesc(className, meta);
                sm.elementIndex  = meta.elementIndex;
                sm.offset        = meta.offset;
                sm.leafAddr      = meta.leafAddr;
                sm.ownerAddr     = meta.ownerAddr ? meta.ownerAddr : obj;  // P4: leaf's owning object
                sm.ownerClass    = meta.ownerClass.empty() ? className : meta.ownerClass;  // P4 inc 2
                sm.anchor        = meta.anchor;   // A12: relay the container identity
                std::memcpy(sm.prevValue, blkLeaves[static_cast<size_t>(leafIdx)].bytes, 8);
                gc.slotMatches[s].push_back(sm);
            }
        }
        result.candidates.push_back(std::move(gc));
    };

    // Per-object scratch (reused).
    std::vector<Orden::Leaf>        leaves;
    std::vector<GroupLeafMeta>      metas;
    std::vector<uintptr_t>          visited;
    std::vector<Orden::SlotMatches> matchOut;
    int32_t scanned = 0;

    // Deep-block scratch + walker (built once; the refs it captures are updated
    // per object). The walker reuses the SAME recursive descent as snapshot capture.
    std::string curClassName;
    uintptr_t   curObjAddr = 0;
    std::unordered_map<std::string, GroupBlock> deepBlocks;
    WalkLeafLimits dlim;
    dlim.maxDepth      = 4;
    dlim.maxElems      = 256;
    dlim.maxTotalElems = kDeepWalkMaxTotalElems;
    dlim.aborted = [&] {
        return Tot::Requested() || (std::chrono::steady_clock::now() - t0 > kDeadline);
    };
    ContainerLeafVisitor deepVisitor = [&](const ContainerLeaf& lf) {
        if (lf.depth < 1) return;                       // depth 0 = object direct (object-block)
        Radar::DataType dt;
        if (!GroupNumericLeafType(lf.leafType, wantByte, dt)) return;   // numeric scope only
        const size_t sz = Radar::SizeOf(dt);
        uint8_t buf[8] = {};
        if (!Macht::ReadBytesSafe(lf.leafAddr, buf, sz)) return;

        std::string idx = "["; idx += std::to_string(lf.elemIndex); idx += "]";
        std::string blockKey, display;
        if (lf.leafName.empty()) {            // scalar-container element: the whole array is one block
            blockKey = lf.arrayPath;
            display  = lf.arrayPath + idx;
        } else {                              // struct-array / map element: this element is one block
            blockKey = lf.arrayPath + idx;
            display  = lf.arrayPath + idx + "." + lf.leafName;
        }
        auto bit = deepBlocks.find(blockKey);
        if (bit == deepBlocks.end()) {
            if (deepBlocks.size() >= kMaxDeepBlocks) return;
            bit = deepBlocks.emplace(blockKey, GroupBlock{}).first;
        }
        GroupBlock& blk = bit->second;
        if (blk.leaves.size() >= kMaxBlockLeaves) return;
        Orden::Leaf ol;
        ol.position     = lf.elemIndex;
        ol.width        = dt;
        ol.elementIndex = lf.elemIndex;
        std::memcpy(ol.bytes, buf, sz);
        blk.leaves.push_back(ol);
        GroupLeafMeta m;
        m.fieldName     = display;
        m.fieldType     = lf.leafType;
        m.definingClass = curClassName;
        m.offset        = 0;
        m.elementIndex  = lf.elemIndex;
        m.leafAddr      = lf.leafAddr;
        m.ownerAddr     = curObjAddr;     // deep leaves are owned by the scanned object
        m.ownerClass    = curClassName;   // ...so the Pivot handoff uses its class (P4 inc 2)
        m.boolMask      = lf.boolMask;
        m.anchor        = lf.anchor;      // A12: the container this element lives in
        blk.metas.push_back(std::move(m));
    };

    for (int32_t i = 0; i < total; ++i) {
        if ((i & 0xFFF) == 0) {
            if (Tot::Requested()) { result.stats.deadlineHit = true; break; }
            if (std::chrono::steady_clock::now() - t0 > kDeadline) { result.stats.deadlineHit = true; break; }
        }
        // Newest-first (coupled with native-C in the UI): walk high-index first so
        // that when the 15s deadline truncates a huge game (FF7 Rebirth ~433K
        // objects), the survivors are the most-recently-allocated objects — the
        // just-spawned UI widgets / actors that hold native values — rather than
        // low-index CDOs/templates. `idx` is the true GObjects index used everywhere.
        const int32_t objIdx = newestFirst ? (total - 1 - i) : i;
        uintptr_t obj = GetByIndex(objIdx);
        if (!obj) continue;
        std::string name = Ubel::GetName(obj);
        if (name.empty()) continue;
        uintptr_t cls = Ubel::GetClass(obj);
        if (!cls) continue;

        auto eit = eligible.find(cls);
        if (eit == eligible.end()) {
            // game_only + the opt-in "Auto detect Engine/System noise" pre-filter
            // share one GetFullName(cls) read. The pre-filter reuses the snapshot
            // noise verdict (engine /Script packages + engine leaf bases), whose
            // gameplay guardrail force-keeps Actor/Pawn/component/... so a player
            // Pawn / its components / AttributeSets are never source-skipped.
            std::string clsPath = (gameOnly || preFilterNoise) ? Ubel::GetFullName(cls)
                                                               : std::string();
            bool keep = !(gameOnly && IsEnginePackage(clsPath))
                     && !(preFilterNoise && IsSnapshotNoiseClass(cls, clsPath));
            eit = eligible.emplace(cls, static_cast<char>(keep ? 1 : 0)).first;
        }
        if (!eit->second) continue;
        ++scanned;

        std::string className = Ubel::GetName(cls);
        curObjAddr = obj;

        // --- Object block: the object's direct + struct-nested numeric leaves. ---
        leaves.clear(); metas.clear(); visited.clear();
        CollectGroupLeaves(obj, className, cls, 0, "", wantByte, 0, visited, leaves, metas, kLeafCap);
        // Cross-object (opt-in): also fold in the numeric leaves of the sub-objects
        // this actor OWNS (components + GAS AttributeSets), so a group whose values
        // span {actor, components, attribute sets} matches as one block.
        if (crossObject)
            AppendOwnedSubObjectLeaves(obj, wantByte, leaves, metas, kLeafCap);
        // Native-C (opt-in): also fold in this object's unmanaged-hole leaves so a
        // group including a native (non-UPROPERTY) value matches. Object block only
        // (never deep — see AppendRawHoleLeaves). Bounded per object.
        if (nativeC)
            AppendRawHoleLeaves(obj, cls, className, slots, nativeWidths, nativeWinBuf,
                                leaves, metas, kLeafCap);
        if (leaves.size() >= nSlots && Orden::MatchGroup(leaves, ordenSlots, matchOut,
                                                        perSlotCap, &capHit))
            emitGroupCandidate(obj, objIdx, name, className, leaves, metas, matchOut);

        // --- Deep blocks (opt-in): each numeric container / struct-array element
        // is its own block, reached via the recursive container walk. Finds groups
        // hidden inside deeply-nested containers (e.g. ...WeaponTuneList[0].Tunes[N]). ---
        if (deep && static_cast<int32_t>(result.candidates.size()) < maxResults) {
            curClassName = className;
            deepBlocks.clear();
            // audit #5 A9: pass the per-walk element counter so dlim.maxTotalElems actually
            // bounds this object's deep walk. Without it (the 7th arg defaulted to nullptr)
            // the budget set at dlim.maxTotalElems above was inert, and a single deep/wide
            // object (the recorded SEED ~24 s chunk) ran to the global 15 s deadline,
            // consuming the whole scan budget. Mirrors ScanForValue's deepEmit call.
            int64_t deepVisited = 0;
            WalkContainerLeaves(obj, cls, /*pathPrefix*/ "", /*depth*/ 0, dlim, deepVisitor, &deepVisited);
            for (auto& kv : deepBlocks) {
                GroupBlock& blk = kv.second;
                if (blk.leaves.size() < nSlots) continue;
                if (Orden::MatchGroup(blk.leaves, ordenSlots, matchOut,
                                      perSlotCap, &capHit))
                    emitGroupCandidate(obj, objIdx, name, className, blk.leaves, blk.metas, matchOut);
                if (static_cast<int32_t>(result.candidates.size()) >= maxResults) break;
            }
        }

        if (static_cast<int32_t>(result.candidates.size()) >= maxResults) {
            result.stats.deadlineHit = true;  // truncated (cap hit)
            break;
        }
    }

    // Report it, don't just log it. The log line below is invisible to the user who is
    // looking at the results grid, and this is the single fact that explains a slot
    // whose "All fields" list is short — four "the scan missed my field" reports.
    // (audit #5 AE13)
    result.perSlotCapHit = capHit;
    if (capHit) {
        LOG_WARN("ScanForValueGroup: at least one object had more than %d leaves matching a slot "
                 "- the extras were dropped, and a later Changed/Decreased refine can only "
                 "re-read what was kept. Narrow the first scan's range if an expected field is "
                 "missing.", perSlotCap);
    }
    result.stats.scannedObjects = scanned;
    result.stats.scannedClasses = static_cast<int32_t>(eligible.size());
    result.stats.durationMs = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();
    return result;
}

ValueScanStats RefineGroupCandidates(
    const std::vector<Radar::SlotSpec>&        slots,
    std::vector<Radar::GroupCandidate>&        candidates,
    const std::vector<Radar::FieldDescriptor>& descriptors,
    const std::vector<Radar::InstanceRecord>&  instances) {
    ValueScanStats stats;
    auto t0 = std::chrono::steady_clock::now();
    const size_t nSlots = slots.size();

    // Group result sets are small (the AND across slots is highly selective) and
    // refine only re-reads each candidate's located leaves, so process the whole
    // set into a fresh survivors vector. Each slot match re-reads its ABSOLUTE
    // leafAddr (direct: obj+offset; deep: container element — stale on container
    // realloc, where the SEH-safe read faults and the match is dropped).
    // Diagnostic budget: enough to see a pattern, few enough to stay off the hot path.
    constexpr int kDiagCandidates = 5;
    int dbgCandidates = 0;

    // audit #5 A12 -- re-anchor container-element leaves before re-reading them, using
    // the same pure rule the single-value refine uses. Read each container header ONCE
    // per pass: a block is one container element, so many leaves share one header.
    struct HeaderSnapshot {
        bool                    ok   = false;
        uintptr_t               data = 0;
        int32_t                 num  = 0;
        Macht::TSparseArrayView sa;             // populated for sparse only
    };
    std::unordered_map<uintptr_t, HeaderSnapshot> headerCache;
    auto headerFor = [&](uintptr_t headerAddr, bool sparse) -> const HeaderSnapshot& {
        auto it = headerCache.find(headerAddr);
        if (it != headerCache.end()) return it->second;
        HeaderSnapshot hs;
        if (sparse) {
            if (Macht::ReadTSparseArray(headerAddr, hs.sa)) {
                hs.ok = true; hs.data = hs.sa.Data; hs.num = hs.sa.MaxIndex;
            }
        } else {
            Macht::TArrayView av;
            if (Macht::ReadTArray(headerAddr, av)) {
                hs.ok = true; hs.data = av.Data; hs.num = av.Count;
            }
        }
        return headerCache.emplace(headerAddr, hs).first->second;
    };
    int32_t reanchorDropped = 0, reanchorRepointed = 0;
    bool warnedUnstamped = false;

    std::vector<Radar::GroupCandidate> survivors;
    survivors.reserve(candidates.size());
    for (auto& gc : candidates) {
        if (gc.slotMatches.size() != nSlots) continue;
        if (gc.instanceIdx >= instances.size()) continue;   // defensive
        bool alive = true;
        // Per-slot drop tally for the first few candidates. A refine that prunes to zero
        // says only "0 surviving" today, which is the same output for six different causes:
        // the leaf could not be read, its width has no target, the predicate rejected it, or
        // the survivors could not form a distinct assignment. Three separate hypotheses were
        // written and abandoned against that silence on 2026-08-05 -- so it now counts.
        const bool diag = (dbgCandidates < kDiagCandidates);
        int dRead = 0, dWidth = 0, dNoTarget = 0, dPredicate = 0, dKept = 0, dEntered = 0;
        int dReanchor = 0;   // A12: dropped because its container moved/shrank under it
        for (size_t s = 0; s < nSlots && alive; ++s) {
            // P2: per-slot predicate. Prev-value types (Changed/Increased/...)
            // compare each leaf's re-read bytes against its own stored prevValue;
            // targeted types (Exact/Bigger/Smaller) against the slot's new target
            // for that leaf's width. prevValue is updated to the latest bytes on
            // every survival so the next refine compares against "what we saw".
            const Radar::ScanType st = slots[s].st;
            const bool usePrev = Radar::IsPrevValueScanType(st);
            std::vector<Radar::GroupSlotMatch> keep;
            keep.reserve(gc.slotMatches[s].size());
            for (auto& sm : gc.slotMatches[s]) {
                ++dEntered;
                if (sm.descriptorIdx >= descriptors.size()) { ++dWidth; continue; }
                Radar::DataType width;
                if (!Radar::TryDataTypeFromPropertyTypeName(descriptors[sm.descriptorIdx].fieldType, width))
                    { ++dWidth; continue; }
                const size_t sz = Radar::SizeOf(width);
                if (sz == 0 || sz > 8) { ++dWidth; continue; }

                // A12: re-anchor BEFORE reading leafAddr. `Unknown` / `Direct` /
                // `UnverifiableNested` fall straight through unchanged.
                const Radar::LeafAnchor& an = sm.anchor;
                if (an.kind == Radar::ValueAnchor::ArrayElement
                    || an.kind == Radar::ValueAnchor::SparseElement) {
                    const bool sparse = (an.kind == Radar::ValueAnchor::SparseElement);
                    const HeaderSnapshot& hs = headerFor(an.header, sparse);
                    if (!hs.ok) { ++dReanchor; ++reanchorDropped; continue; }   // container gone
                    const bool slotAlloc =
                        !sparse || Macht::IsSparseIndexAllocated(hs.sa, sm.elementIndex);
                    switch (Radar::RefineContainerAnchor(an.kind, sm.elementIndex, an.num,
                                                         an.data, hs.data, hs.num, slotAlloc)) {
                        case Radar::RefineAnchorVerdict::Drop:
                            ++dReanchor;          // per-candidate diagnostic bucket
                            ++reanchorDropped;    // whole-pass summary
                            continue;
                        case Radar::RefineAnchorVerdict::Repoint:
                            // Exact by construction: every leaf in the buffer shifts by the
                            // same delta, whatever its stride or intra-element offset was.
                            sm.leafAddr    = Radar::RepointByBufferMove(sm.leafAddr, an.data, hs.data);
                            sm.anchor.data = hs.data;
                            sm.anchor.num  = hs.num;
                            ++reanchorRepointed;
                            break;
                        case Radar::RefineAnchorVerdict::KeepAddress:
                            sm.anchor.num = hs.num;   // keep the stamp current for the NEXT refine
                            break;
                    }
                } else if (sm.elementIndex >= 0 && an.kind == Radar::ValueAnchor::Unknown
                           && !warnedUnstamped) {
                    // A container element with no stamp at all. The deep visitor is the only
                    // writer of elementIndex >= 0 into a GroupLeafMeta, and it always relays
                    // an anchor -- so this can only mean one of the by-name hops
                    // (ContainerLeaf -> GroupLeafMeta -> GroupSlotMatch) dropped it. Warn
                    // once; do NOT change the verdict, which would turn a wiring bug into a
                    // scan regression.
                    warnedUnstamped = true;
                    Sein::Warn("SCAN:grp",
                        "RefineGroup: container element '%s' carries no ValueAnchor -- a leaf "
                        "metadata hop dropped it; this leaf is refined at its stale absolute "
                        "address (audit #5 A12)",
                        descriptors[sm.descriptorIdx].fieldName.c_str());
                }

                uint8_t buf[8] = {};
                if (!Macht::ReadBytesSafe(sm.leafAddr, buf, sz)) { ++dRead; continue; }
                const uint8_t* cmp = usePrev ? sm.prevValue : slots[s].targets.Find(width);
                if (!cmp) { ++dNoTarget; continue; }          // value can't fit this width
                const uint8_t* cmp2 = nullptr;
                if (st == Radar::ScanType::Between) {
                    cmp2 = slots[s].targets2.Find(width);
                    if (!cmp2) { ++dNoTarget; continue; }    // upper bound can't fit this width
                }
                if (!Radar::ComparePredicate(width, st, buf, cmp, cmp2, slots[s].roundMode))
                    { ++dPredicate; continue; }
                std::memcpy(sm.prevValue, buf, sz);
                ++dKept;
                keep.push_back(sm);
            }
            gc.slotMatches[s] = std::move(keep);
            if (gc.slotMatches[s].empty()) alive = false;
        }
        const bool feasible = alive && GroupCandidateFeasible(gc);
        if (diag) {
            ++dbgCandidates;
            const char* verdict = !alive     ? "DROPPED (a slot has no surviving leaf)"
                                : !feasible  ? "DROPPED (survivors cannot form a DISTINCT assignment "
                                               "-- every slot matched the same leaf)"
                                             : "kept";
            Sein::Debug("SCAN:grp",
                "RefineGroup cand[%u]: %s | leaves entered=%d kept=%d | dropped: unreadable=%d "
                "bad-width=%d no-target-for-width=%d predicate-said-no=%d container-moved=%d",
                (unsigned)gc.instanceIdx, verdict, dEntered, dKept, dRead, dWidth, dNoTarget,
                dPredicate, dReanchor);
        }
        if (feasible)
            survivors.push_back(std::move(gc));
    }
    candidates = std::move(survivors);

    stats.scannedObjects = static_cast<int32_t>(candidates.size());
    stats.durationMs = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();
    // Only when it actually fired. `repointed` is the half that is a GAIN: before A12 a
    // grown container left every element address stale and those leaves were lost.
    if (reanchorDropped || reanchorRepointed) {
        Sein::Info("SCAN:grp",
                   "RefineGroup re-anchor: %d container element(s) repointed after a realloc, "
                   "%d dropped (slot freed / container shrank / header gone)",
                   reanchorRepointed, reanchorDropped);
    }
    return stats;
}

// Native-C (P3): append an object's UNMANAGED-hole guesses as synthetic snapshot
// fields, so a captured snapshot ALSO carries native (non-UPROPERTY) values for the
// SPC diff / Class Pivot consumers. Reuses the "Guess What" engine (Ubel::GuessGapTypes)
// over each hole, then NORMALIZES each guessed type to the canonical UE property-type
// string (Ubel::NormalizeGuessedTypeToProperty) — MANDATORY, because the C# consumer
// SnapshotNumeric.TryFromHex switches on exact "FloatProperty"/"IntProperty"/... and
// would store a NULL numeric_value (breaking SPC compare + Pivot decode) for a suffixed
// "Float?"/"Int32?". Pointer/Padding guesses normalize to "" and are DROPPED (not
// gameplay numerics; they'd pollute the Pivot value picker). Field names are
// "<raw@0xNN>" — offset-only, so the same hole joins across snapshots regardless of the
// guessed value, and so SPC/Pivot (which key on prop_name + offset) treat it uniformly;
// matches the P1/P2 discriminator. Honors the snapshot numericScope (e.g. Byte guesses
// drop under NumericNoByte). Bounded per object (kMaxRawSnapFields).
void AppendRawHoleFields(uintptr_t obj, uintptr_t cls, Radar::DataType numericScope,
                         std::vector<SnapshotField>& out,
                         Aura::NumericFamily family = Aura::NumericFamily::Any) {
    const std::vector<Radar::DataType>& members = Radar::MultiNumericMembers(numericScope);
    if (members.empty()) return;   // numericScope isn't a meta type -> capture nothing

    const ClassInfo& ci = Ubel::WalkClassEx(cls);   // memoized (B10) — BY REF, no copy
    const int32_t headerEnd = DynOff::UOBJECT_OUTER + 8;
    constexpr int32_t kSanity   = 0x10000;
    constexpr int32_t kFallback = 0x400;
    int32_t propsSize = ci.PropertiesSize;
    int32_t winEnd = (propsSize > headerEnd && propsSize <= kSanity) ? propsSize
                   : (propsSize > kSanity) ? kSanity
                   : (headerEnd + kFallback);
    if (winEnd <= headerEnd) return;

    auto holes = Ubel::ComputeClassHoles(ci, headerEnd, winEnd);
    if (holes.empty()) return;

    auto inScope = [&](Radar::DataType dt) {
        for (Radar::DataType m : members) if (m == dt) return true;
        return false;
    };

    constexpr int kMaxRawSnapFields = 256;   // per object
    int added = 0;
    std::vector<Ubel::LiveFieldValue> guesses;
    for (const auto& hole : holes) {
        if (added >= kMaxRawSnapFields) break;
        guesses.clear();
        Ubel::GuessGapTypes(obj, hole.start, hole.end, guesses);
        for (const auto& g : guesses) {
            if (added >= kMaxRawSnapFields) break;
            std::string canon = Ubel::NormalizeGuessedTypeToProperty(g.typeName);
            if (canon.empty()) continue;   // padding / pointer -> drop
            Radar::DataType dt;
            if (!Radar::TryDataTypeFromPropertyTypeName(canon, dt)) continue;
            if (!inScope(dt)) continue;    // honor numericScope (e.g. Byte under NoByte)
            if (!Aura::NumericDataTypeInFamily(dt, family)) continue;   // type-family narrowing
            SnapshotField sf;
            char nb[32];
            snprintf(nb, sizeof(nb), "<raw@0x%X>", g.offset);
            sf.name   = nb;
            sf.offset = g.offset;
            sf.type   = canon;            // canonical -> SnapshotNumeric decodes it
            sf.hex    = g.hexValue;       // little-endian hex, width already matches canon
            out.push_back(std::move(sf));
            ++added;
        }
    }
}

// True if `cls` is pure engine/system noise that should be skipped at snapshot
// CAPTURE time (source-level) so it never enters the store — the in-loop mirror
// of ClassifyNoiseClasses (same /Script-package + engine-leaf-base rules), but
// single-pass with no histogram pre-scan. The gameplay guardrail wins first
// (Actor/component/Pawn-derived classes are force-kept; see DecideSnapshotNoise +
// SnapshotGameplayKeepBases). `classPath` is the caller's already-computed
// Ubel::GetFullName(cls) — reused on a cache miss, ignored on a hit. Per-thread
// memoized on the class pointer to amortize the bounded super-chain walks across
// a chunk's many same-class instances. Thread-safe: pure string + read-only
// memory reads (the thread_local cache is per-worker, no shared state).
static bool IsSnapshotNoiseClass(uintptr_t cls, const std::string& classPath) {
    thread_local std::unordered_map<uintptr_t, char> verdictCache;
    auto it = verdictCache.find(cls);
    if (it != verdictCache.end()) return it->second != 0;

    bool noise = DecideSnapshotNoise(
        ClassDerivesFromAny(cls, SnapshotGameplayKeepBases()),   // guardrail wins
        IsEnginePackage(classPath),
        ClassDerivesFromAny(cls, SnapshotEngineNoiseBases()));
    verdictCache.emplace(cls, noise ? char{1} : char{0});
    return noise;
}

// True if `cls` is a GAMEPLAY class (Actor/component/Pawn/Character/Controller/
// PlayerState/GameInstance-derived; see SnapshotGameplayKeepBases) — the usual
// carriers of the values users hunt. Used to opt these classes into TOP-LEVEL
// scalar-array capture (e.g. a Pawn's numeric stat-bank TArray<float>), which is
// otherwise skipped for every object to keep the DB small (see the guard in
// CaptureStructArrays). Thread-local memoized on the class pointer, mirroring
// IsSnapshotNoiseClass's super-chain-walk amortization. Thread-safe: pure
// read-only memory reads + a per-worker cache. (build 1827)
static bool IsSnapshotGameplayClass(uintptr_t cls) {
    thread_local std::unordered_map<uintptr_t, char> verdictCache;
    auto it = verdictCache.find(cls);
    if (it != verdictCache.end()) return it->second != 0;
    bool keep = ClassDerivesFromAny(cls, SnapshotGameplayKeepBases());
    verdictCache.emplace(cls, keep ? char{1} : char{0});
    return keep;
}

SnapshotChunkResult CaptureSnapshotChunk(int32_t offset, int32_t limit,
                                         bool gameOnly,
                                         Radar::DataType numericScope,
                                         int32_t arrayCap,
                                         bool captureNativeC,
                                         bool skipNoiseClasses,
                                         NumericFamily family) {
    SnapshotChunkResult result;
    const int32_t total = GetCount();
    result.total = total;

    if (offset < 0) offset = 0;
    if (limit < 0)  limit = 0;
    const int32_t end = (std::min)(offset + limit, total);
    result.scanned = (end > offset) ? (end - offset) : 0;

    if (result.scanned <= 0) return result;

    // Parallel capture: the chunk's objects are walked across worker threads — the
    // single-threaded walk was the dominant cost on huge games (FF7 Rebirth ~433K
    // objects: snapshot ran 10+ min and didn't finish). Every per-object dependency
    // is parallel-safe: Ubel's class/name/struct caches are mutex-guarded and return
    // value copies (the same primitives ScanForValue already parallelizes), the
    // Native-C GuessGapTypes pass uses a thread_local buffer, and Macht reads are
    // SEH-isolated. Each worker fills its OWN object vector; we concat them after
    // (order is irrelevant — every row carries its GObjects index, the SQLite key).
    //
    // The chunk processes its WHOLE [offset, end) range — there is deliberately NO
    // per-chunk wall-clock deadline: the C# pager advances by `scanned` (= the full
    // range), so progress must stay CONTIGUOUS (an early parallel return would leave
    // index holes that the pager would skip = data loss). The per-object caps
    // (deep-walk maxTotalElems / Native-C kMaxRawSnapFields) plus the chunk size
    // bound the work, and Tot (client-gone / shutdown) still cancels cooperatively
    // via the parallel watcher + the body's stride check. SnapshotChunkSize is sized
    // >= ScanThreadCount's 8192 threshold so each full chunk runs multi-threaded.
    struct ThreadResult { std::vector<SnapshotObject> objects; };
    const int32_t chunkLen = end - offset;

    // Phase-0 telemetry: time the parallel walk + merge so the C# side can report
    // walk-vs-serialize-vs-write per chunk (decides where future optimisation goes).
    const auto walkT0 = std::chrono::steady_clock::now();

    auto scan = ParallelGObjectsScan<ThreadResult>(chunkLen,
        [&](ThreadResult& tr, int32_t beginIdx, int32_t endIdx,
            std::atomic<bool>& deadlineHit) {
        std::vector<std::string> typeNames;   // per-thread reused scratch
        for (int32_t k = beginIdx; k < endIdx; ++k) {
            // Range-relative stride so EACH worker polls cancel on its first
            // iteration (beginIdx) + every 4096 after, regardless of where its
            // sub-range starts (matches the ScanForValue / FindRefs idiom; a
            // chunk-global `k & 0xFFF` would delay cancel on off-aligned workers).
            if (((k - beginIdx) & 0xFFF) == 0) {
                if (deadlineHit.load(std::memory_order_relaxed)) return;
                if (Tot::Requested()) { deadlineHit.store(true, std::memory_order_relaxed); return; }
            }
            const int32_t i = offset + k;   // real GObjects index
            uintptr_t obj = GetByIndex(i);
            if (!obj) continue;

            std::string name = Ubel::GetName(obj);
            if (name.empty()) continue;  // skip unnamed slots (matches get_object_list)

            uintptr_t cls = Ubel::GetClass(obj);
            if (!cls) continue;

            // game_only filter keys on the class path (engine packages skipped).
            std::string classPath = Ubel::GetFullName(cls);
            if (gameOnly && IsEnginePackage(classPath)) continue;

            // Auto-detect Engine/System noise (opt-in, default ON in the UI): skip
            // pure engine/system classes BEFORE the costly per-field walk so they
            // never enter the snapshot — cutting capture time + DB size at the
            // source. The gameplay guardrail inside IsSnapshotNoiseClass force-keeps
            // Actor/component/Pawn-derived classes, so a player Pawn's X/Y/Z is
            // never dropped. Verdict is thread-local memoized per class.
            if (skipNoiseClasses && IsSnapshotNoiseClass(cls, classPath)) continue;

            const ClassInfo& ci = Ubel::WalkClassEx(cls);  // memoized per class (B10) — no copy
            if (ci.Fields.empty()) continue;

            typeNames.clear();
            typeNames.reserve(ci.Fields.size());
            for (const auto& f : ci.Fields) typeNames.push_back(f.TypeName);

            auto picks = Radar::SelectSnapshotNumericFields(typeNames, numericScope);

            SnapshotObject so;
            so.index     = i;  // GObjects index == logical slot index
            so.addr      = obj;
            so.name      = std::move(name);
            so.className = ci.Name;
            so.path      = Ubel::GetFullName(obj);
            uintptr_t outer = Ubel::GetOuter(obj);
            so.outerClassName = outer ? Ubel::GetName(Ubel::GetClass(outer)) : "";

            // Top-level numeric scalar fields.
            for (const auto& p : picks) {
                if (!NumericDataTypeInFamily(p.dt, family)) continue;   // type-family narrowing
                const auto& fi = ci.Fields[p.fieldIndex];
                size_t sz = Radar::SizeOf(p.dt);
                if (sz == 0 || sz > 8) continue;  // defensive; meta members are 1..8B
                uint8_t buf[8] = {};
                if (!Macht::ReadBytesSafe(obj + fi.Offset, buf, sz)) continue;

                SnapshotField sf;
                sf.name   = fi.Name;
                sf.offset = fi.Offset;
                sf.type   = fi.TypeName;
                sf.hex    = SnapshotBytesToHex(buf, sz);
                so.fields.push_back(std::move(sf));
            }

            // Inner numeric leaves of PLAIN (non-container) struct members — GAS
            // FGameplayAttributeData (HP/MP BaseValue/CurrentValue), FVector/FRotator
            // (Location/Rotation), etc. Neither the scalar picks above (numeric leaves
            // only) nor CaptureStructArrays (containers only) reach these. (build 1648)
            CaptureDirectStructFields(obj, cls, numericScope, family, so.fields);

            // Container-element leaves at any (bounded) depth — struct-array / map /
            // set elements + nested leaf-containers (build 1204). Gameplay classes
            // (Actor/Pawn/component/... — the value carriers) additionally capture
            // their TOP-LEVEL numeric scalar arrays (e.g. a Pawn's stat-bank
            // TArray<float>); non-gameplay classes skip those to keep the DB small.
            // (build 1827)
            CaptureStructArrays(obj, cls, numericScope, arrayCap, so.arrays, family,
                                /*captureTopLevelScalarArrays*/ IsSnapshotGameplayClass(cls));

            // Native-C (P3, opt-in): append this object's unmanaged-hole guesses as
            // synthetic "<raw@0xNN>" fields so the snapshot also carries native
            // (non-UPROPERTY) values for SPC diff / Class Pivot.
            if (captureNativeC)
                AppendRawHoleFields(obj, cls, numericScope, so.fields, family);

            // Keep objects with any captured scalar field OR array element.
            if (so.fields.empty() && so.arrays.empty()) continue;
            tr.objects.push_back(std::move(so));
        }
    });

    // Merge per-thread objects (ascending tid -> ascending index; order is
    // irrelevant to the SQLite store, which keys each row by its GObjects index).
    size_t totalObjs = 0;
    for (auto& tr : scan.perThread) totalObjs += tr.objects.size();
    result.objects.reserve(totalObjs);
    for (auto& tr : scan.perThread)
        for (auto& so : tr.objects) result.objects.push_back(std::move(so));

    result.walkMs = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - walkT0).count();

    // Tot cancellation (client gone / shutdown) — the partial chunk is discarded by
    // the disconnected/closing C# side, so result.scanned is left at the full range.
    if (scan.deadlineHit)
        Sein::Warn("PIPE:snapshot", "CaptureSnapshotChunk: cancelled (client gone / shutdown)");

    return result;
}

} // namespace Aura
