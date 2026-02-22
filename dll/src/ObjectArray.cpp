// ============================================================
// ObjectArray.cpp — FChunkedFixedUObjectArray implementation
// ============================================================

#include "ObjectArray.h"
#include "Memory.h"
#define LOG_CAT "OARR"
#include "Logger.h"
#include "Constants.h"
#include "FNamePool.h"

#include <cctype>
#include <climits>
#include <vector>

namespace ObjectArray {

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
static bool        s_isFlat   = false; // true = non-chunked flat array (some UE4 builds)

// Helper: check if a pointer value looks like a valid heap pointer (not code/null/low)
static bool LooksLikeHeapPtr(uintptr_t ptr) {
    if (!ptr || ptr < 0x10000) return false;
    // Must be in user-mode address range (below kernel boundary)
    if (ptr > 0x00007FFFFFFFFFFF) return false;
    // Reject pointers in the game module's code range (likely .text section)
    uintptr_t modBase = Mem::GetModuleBase(nullptr);
    uintptr_t modSize = Mem::GetModuleSize(nullptr);
    if (modBase && modSize && ptr >= modBase && ptr < modBase + modSize) return false;
    return true;
}

static bool DetectLayout(uintptr_t addr) {
    // Diagnostic: dump first 48 bytes at the GObjects address
    {
        uint64_t dump[6] = {};
        Mem::ReadBytesSafe(addr, dump, sizeof(dump));
        LOG_DEBUG("ObjectArray: GObjects@0x%llX: +00:%016llX +08:%016llX +10:%016llX +18:%016llX +20:%016llX +28:%016llX",
                  (unsigned long long)addr,
                  dump[0], dump[1], dump[2], dump[3], dump[4], dump[5]);
    }

    // Layout A (UE5 default): Objects* at +0x00, PreAllocated* at +0x08, Max at +0x10, Num at +0x14
    {
        int32_t num = 0, max = 0;
        Mem::ReadSafe(addr + 0x14, num);
        Mem::ReadSafe(addr + 0x10, max);
        if (num > 0 && num <= max && max <= 0x800000) {
            uintptr_t objPtr = 0;
            Mem::ReadSafe(addr + 0x00, objPtr);
            if (LooksLikeHeapPtr(objPtr)) {
                s_layout = { 0x00, 0x10, 0x14, 0x18, 0x1C };
                LOG_INFO("ObjectArray: Layout A (default) detected (Num=%d, Max=%d, Objects=0x%llX)",
                         num, max, (unsigned long long)objPtr);
                return true;
            }
        }
    }

    // Layout B: Objects* at +0x10, Num at +0x04 (some UE4 alternate layout)
    // Validate that Objects pointer is a heap address, not code
    {
        int32_t num = 0;
        Mem::ReadSafe(addr + 0x04, num);
        if (num > 0 && num <= 0x800000) {
            uintptr_t objPtr = 0;
            Mem::ReadSafe(addr + 0x10, objPtr);
            if (LooksLikeHeapPtr(objPtr)) {
                s_layout = { 0x10, 0x08, 0x04, 0x0C, -1 };
                LOG_INFO("ObjectArray: Layout B (alt) detected (Num=%d, Objects=0x%llX)",
                         num, (unsigned long long)objPtr);
                return true;
            }
        }
    }

    // Layout C (UE4 relaxed): Objects* at +0x00, Num at +0x14, but Max at +0x10 is garbage
    // This happens in older UE4 where +0x08 is PreAllocatedObjects (pointer, not count),
    // making +0x10 contain a pointer value that looks like a huge "Max".
    // We validate Objects at +0x00 is a heap pointer and Num is reasonable.
    {
        int32_t num = 0;
        Mem::ReadSafe(addr + 0x14, num);
        if (num > 0 && num <= 0x800000) {
            uintptr_t objPtr = 0;
            Mem::ReadSafe(addr + 0x00, objPtr);
            if (LooksLikeHeapPtr(objPtr)) {
                s_layout = { 0x00, 0x10, 0x14, 0x18, 0x1C };
                LOG_INFO("ObjectArray: Layout C (relaxed default) detected (Num=%d, Objects=0x%llX, Max@+0x10 skipped)",
                         num, (unsigned long long)objPtr);
                return true;
            }
        }
    }

    // Layout D: UE4 FUObjectArray with members before ObjObjects:
    // +0x00: ObjFirstGCIndex, +0x04: ObjLastNonGCIndex, +0x08: MaxObjectsNotConsideredByGC,
    // +0x0C: OpenForDisregardForGC, +0x10: ObjObjects.Objects**, +0x18: Max, +0x1C: Num
    {
        int32_t num = 0, max = 0;
        Mem::ReadSafe(addr + 0x1C, num);
        Mem::ReadSafe(addr + 0x18, max);
        if (num > 0 && num <= max && max <= 0x800000) {
            uintptr_t objPtr = 0;
            Mem::ReadSafe(addr + 0x10, objPtr);
            if (LooksLikeHeapPtr(objPtr)) {
                s_layout = { 0x10, 0x18, 0x1C, 0x20, 0x24 };
                LOG_INFO("ObjectArray: Layout D (UE4 extended) detected (Num=%d, Max=%d, Objects=0x%llX)",
                         num, max, (unsigned long long)objPtr);
                return true;
            }
        }
    }

    LOG_WARN("ObjectArray: Could not detect layout, using default");
    return true;
}

// Helper: check if a pointer looks like a valid UObject (has valid ClassPrivate chain)
static bool LooksLikeUObject(uintptr_t obj) {
    if (!obj || obj < 0x10000 || obj > 0x00007FFFFFFFFFFF) return false;
    uintptr_t cls = 0;
    if (!Mem::ReadSafe(obj + 0x10, cls)) return false;
    if (cls < 0x10000 || cls > 0x00007FFFFFFFFFFF) return false;
    uintptr_t clsCls = 0;
    if (!Mem::ReadSafe(cls + 0x10, clsCls)) return false;
    if (clsCls < 0x10000 || clsCls > 0x00007FFFFFFFFFFF) return false;
    return true;
}

// Test a candidate stride against a chunk, counting valid UObject items.
// Returns the number of items that resolved names (strong) and total valid items (weak).
// NOTE: No early exit — scans all maxItems for fair comparison across strides.
static void ProbeStride(uintptr_t chunkBase, int stride, int maxItems,
                        int& outGood, int& outNamed, int& outNull, int& outBad) {
    outGood = outNamed = outNull = outBad = 0;

    for (int idx = 0; idx < maxItems; ++idx) {
        int64_t byteOff = static_cast<int64_t>(idx) * stride;

        uintptr_t obj = 0;
        if (!Mem::ReadSafe(chunkBase + byteOff, obj)) {
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
        if (FNamePool::IsInitialized()) {
            uint32_t nameIdx = 0;
            if (Mem::ReadSafe(obj + Constants::OFF_UOBJECT_NAME, nameIdx)) {
                std::string name = FNamePool::GetString(nameIdx);
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
    // the primary scoring is unreliable due to LCM alignment false positives.
    // In this case, among strides that have named > 0, prefer the one with
    // fewest bad items — the correct stride reads aligned data and produces
    // fewer garbage reads even when the chunk table is non-standard.
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
            LOG_INFO("ObjectArray: %s fallback: all scores negative, selecting stride %d (fewest bad=%d) over stride %d (bad=%d)",
                     phase, fallbackStride, fallbackBad, bestStride, bestBad);
            bestStride = results[fallbackIdx].stride;
            bestCount = results[fallbackIdx].good;
            bestNamed = results[fallbackIdx].named;
            bestBad = results[fallbackIdx].bad;
            bestHasNames = (results[fallbackIdx].named > 0);
        }
    }
}

// Auto-detect FUObjectItem size by probing consecutive items in chunks.
// UE5 (most): 16 bytes, UE4 / some UE5 with clustering: 24 bytes.
//
// Strategy: For each candidate stride, walk chunk at stride-aligned offsets
// counting valid items. Use FNamePool-based name resolution (strong) if available,
// falling back to ClassPrivate chain (weak) if not. Try all strides and pick best.
// Uses tiebreaker: when named counts are equal, prefer stride with fewer bad items.
static void DetectItemSize() {
    uintptr_t chunkTable = 0;
    if (!Mem::ReadSafe(s_arrayAddr + s_layout.objectsOffset, chunkTable) || !chunkTable) {
        LOG_WARN("ObjectArray: Cannot read chunk table for item size detection");
        return;
    }

    // Diagnostic: dump first 64 bytes at chunkTable address
    {
        uint64_t dump[8] = {};
        Mem::ReadBytesSafe(chunkTable, dump, sizeof(dump));
        LOG_DEBUG("ObjectArray: chunkTable@0x%llX: +00:%016llX +08:%016llX +10:%016llX +18:%016llX +20:%016llX +28:%016llX +30:%016llX +38:%016llX",
                  (unsigned long long)chunkTable,
                  dump[0], dump[1], dump[2], dump[3], dump[4], dump[5], dump[6], dump[7]);
    }

    uintptr_t chunk0 = 0;
    if (!Mem::ReadSafe(chunkTable, chunk0) || !chunk0) {
        LOG_WARN("ObjectArray: Cannot read chunk[0] for item size detection");
        return;
    }

    int candidates[] = { 16, 24, 20 };
    constexpr int NUM_CANDIDATES = 3;
    int bestStride = 0;
    int bestCount = 0;
    int bestNamed = 0;
    int bestBad = INT_MAX;
    bool bestHasNames = false;

    constexpr int MAX_ITEMS_PHASE1 = 200;

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
        Mem::ReadSafe(chunkTable + sizeof(uintptr_t), chunk1);
        int32_t numElements = GetCount();
        bool mightBeFlat = false;

        if (chunk0 && numElements > 0) {
            int chunksNeeded = (numElements + Constants::OBJECTS_PER_CHUNK - 1) / Constants::OBJECTS_PER_CHUNK;
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
                    if (!Mem::ReadSafe(chunk1, testDeref)) {
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
                            candidates, NUM_CANDIDATES,
                            bestStride, bestCount, bestNamed, bestBad, bestHasNames);

            if (bestHasNames && bestNamed >= 2) {
                LOG_INFO("ObjectArray: Flat (non-chunked) array confirmed (P0-flat: %d named, %d bad)",
                         bestNamed, bestBad);
                goto accept_size;
            }
            // Flat didn't work convincingly — reset and try chunked
            LOG_INFO("ObjectArray: Flat probe inconclusive (named=%d), falling back to chunked detection",
                     bestNamed);
            s_isFlat = false;
            bestStride = 0; bestCount = 0; bestNamed = 0; bestBad = INT_MAX; bestHasNames = false;
        }
    }

    // Phase 1: scan first 200 items of chunk[0] (standard chunked layout)
    // Use 200 items (not 100) to give sparse UE4 arrays enough items for correct stride detection.
    ProbeAllStrides(chunk0, MAX_ITEMS_PHASE1, "P1",
                    candidates, NUM_CANDIDATES,
                    bestStride, bestCount, bestNamed, bestBad, bestHasNames);

    // Phase 2: if Phase 1 yielded nothing, try deeper in chunk (items 1000+).
    // Some UE4 games have thousands of null slots at the start.
    if (bestCount == 0) {
        LOG_INFO("ObjectArray: Phase 1 found no items, trying deep scan from item 1000...");
        ProbeAllStrides(chunk0 + static_cast<int64_t>(1000) * 24, 100, "P2-deep",
                        candidates, NUM_CANDIDATES,
                        bestStride, bestCount, bestNamed, bestBad, bestHasNames);
    }

    // Phase 3: if still nothing, maybe the array is NOT chunked (some UE4 builds).
    // In non-chunked layout, chunkTable IS the item array directly (no extra deref).
    // Try probing chunkTable itself as the item base.
    if (bestCount == 0) {
        LOG_INFO("ObjectArray: Phase 2 found nothing. Trying flat (non-chunked) array at chunkTable=0x%llX...",
                 (unsigned long long)chunkTable);

        s_isFlat = true;  // Temporarily set for probing

        ProbeAllStrides(chunkTable, MAX_ITEMS_PHASE1, "P3-flat",
                        candidates, NUM_CANDIDATES,
                        bestStride, bestCount, bestNamed, bestBad, bestHasNames);

        if (bestCount == 0) {
            // Try deep scan on flat array too
            ProbeAllStrides(chunkTable + static_cast<int64_t>(1000) * 24, 100, "P3-flat-deep",
                            candidates, NUM_CANDIDATES,
                            bestStride, bestCount, bestNamed, bestBad, bestHasNames);
        }

        if (bestCount == 0) {
            s_isFlat = false;  // Revert — flat didn't work either
        } else {
            LOG_INFO("ObjectArray: Flat (non-chunked) array layout detected");
        }
    }

accept_size:
    // Determine minimum threshold for acceptance
    int threshold = bestHasNames ? 2 : 3;
    int bestTotal = bestHasNames ? bestNamed : bestCount;

    if (bestTotal >= threshold) {
        s_itemSize = bestStride;
        if (bestHasNames) {
            LOG_INFO("ObjectArray: FUObjectItem size detected as %d bytes (%d items with valid names, %d total valid, %d bad)",
                     bestStride, bestNamed, bestCount, bestBad);
        } else {
            LOG_INFO("ObjectArray: FUObjectItem size detected as %d bytes (%d items validated, no FName check)",
                     bestStride, bestCount);
        }
    } else if (bestStride > 0 && bestTotal > 0) {
        s_itemSize = bestStride;
        LOG_WARN("ObjectArray: FUObjectItem size tentatively set to %d bytes (only %d items validated)",
                 bestStride, bestTotal);
    } else {
        LOG_WARN("ObjectArray: Could not auto-detect item size, keeping default %d", s_itemSize);
    }
}

void Init(uintptr_t gobjectsAddr) {
    s_arrayAddr = gobjectsAddr;
    DetectLayout(gobjectsAddr);
    DetectItemSize();
    LOG_INFO("ObjectArray: Initialized at 0x%llX, Count=%d, ItemSize=%d",
             static_cast<unsigned long long>(gobjectsAddr), GetCount(), s_itemSize);
}

int32_t GetCount() {
    if (!s_arrayAddr) return 0;
    int32_t count = 0;
    Mem::ReadSafe(s_arrayAddr + s_layout.numElementsOffset, count);
    return count;
}

int32_t GetMax() {
    if (!s_arrayAddr) return 0;
    int32_t max = 0;
    Mem::ReadSafe(s_arrayAddr + s_layout.maxElementsOffset, max);
    return max;
}

int GetItemSize() {
    return s_itemSize;
}

uintptr_t GetByIndex(int32_t index) {
    if (!s_arrayAddr || index < 0 || index >= GetCount()) return 0;

    // Read array base pointer
    uintptr_t arrayBase = 0;
    if (!Mem::ReadSafe(s_arrayAddr + s_layout.objectsOffset, arrayBase) || !arrayBase) return 0;

    uintptr_t itemAddr = 0;

    if (s_isFlat) {
        // Flat (non-chunked): items are at arrayBase + index * itemSize
        itemAddr = arrayBase + static_cast<uintptr_t>(index) * s_itemSize;
    } else {
        // Chunked: arrayBase is a chunk table, each chunk holds OBJECTS_PER_CHUNK items
        int32_t chunkIndex = index / Constants::OBJECTS_PER_CHUNK;
        int32_t withinChunk = index % Constants::OBJECTS_PER_CHUNK;

        uintptr_t chunk = 0;
        if (!Mem::ReadSafe(arrayBase + chunkIndex * sizeof(uintptr_t), chunk) || !chunk) return 0;

        itemAddr = chunk + static_cast<uintptr_t>(withinChunk) * s_itemSize;
    }

    uintptr_t object = 0;
    Mem::ReadSafe(itemAddr, object);
    return object;
}

FUObjectItem* GetItem(int32_t index) {
    if (!s_arrayAddr || index < 0 || index >= GetCount()) return nullptr;

    uintptr_t arrayBase = 0;
    if (!Mem::ReadSafe(s_arrayAddr + s_layout.objectsOffset, arrayBase) || !arrayBase) return nullptr;

    uintptr_t itemAddr = 0;

    if (s_isFlat) {
        itemAddr = arrayBase + static_cast<uintptr_t>(index) * s_itemSize;
    } else {
        int32_t chunkIndex = index / Constants::OBJECTS_PER_CHUNK;
        int32_t withinChunk = index % Constants::OBJECTS_PER_CHUNK;

        uintptr_t chunk = 0;
        if (!Mem::ReadSafe(arrayBase + chunkIndex * sizeof(uintptr_t), chunk) || !chunk) return nullptr;

        itemAddr = chunk + static_cast<uintptr_t>(withinChunk) * s_itemSize;
    }

    return Mem::Ptr<FUObjectItem>(itemAddr);
}

void ForEach(std::function<bool(int32_t idx, uintptr_t obj)> cb) {
    int32_t count = GetCount();
    for (int32_t i = 0; i < count; ++i) {
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
        if (!Mem::ReadSafe(obj + Constants::OFF_UOBJECT_NAME, nameIndex)) return true;

        std::string objName = FNamePool::GetString(nameIndex);
        if (objName == name) {
            result = obj;
            return false; // Stop iteration
        }
        return true;
    });
    return result;
}

uintptr_t FindByFullName(const std::string& fullName) {
    // Forward declared — uses UStructWalker::GetFullName
    // This is implemented after UStructWalker is available
    (void)fullName;
    return 0;
}

std::vector<SearchResult> SearchByName(const std::string& query, int maxResults) {
    std::vector<SearchResult> results;

    // Convert query to lowercase for case-insensitive comparison
    std::string lowerQuery = query;
    for (auto& c : lowerQuery) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

    int32_t count = GetCount();
    for (int32_t i = 0; i < count && static_cast<int>(results.size()) < maxResults; ++i) {
        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;

        // Read FName from UObject
        uint32_t nameIndex = 0;
        if (!Mem::ReadSafe(obj + Constants::OFF_UOBJECT_NAME, nameIndex)) continue;

        std::string objName = FNamePool::GetString(nameIndex);
        if (objName.empty()) continue;

        // Case-insensitive partial match
        std::string lowerName = objName;
        for (auto& c : lowerName) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

        if (lowerName.find(lowerQuery) == std::string::npos) continue;

        SearchResult sr;
        sr.addr = obj;
        sr.name = objName;

        // Get class name
        uintptr_t cls = 0;
        if (Mem::ReadSafe(obj + Constants::OFF_UOBJECT_CLASS, cls) && cls) {
            uint32_t clsNameIdx = 0;
            if (Mem::ReadSafe(cls + Constants::OFF_UOBJECT_NAME, clsNameIdx)) {
                sr.className = FNamePool::GetString(clsNameIdx);
            }
        }

        // Get outer
        Mem::ReadSafe(obj + DynOff::UOBJECT_OUTER, sr.outer);

        results.push_back(std::move(sr));
    }

    return results;
}

std::vector<SearchResult> FindInstancesByClass(const std::string& className, int maxResults) {
    std::vector<SearchResult> results;

    // Convert query to lowercase for case-insensitive comparison
    std::string lowerQuery = className;
    for (auto& c : lowerQuery) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

    int32_t count = GetCount();
    for (int32_t i = 0; i < count && static_cast<int>(results.size()) < maxResults; ++i) {
        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;

        // Read ClassPrivate
        uintptr_t cls = 0;
        if (!Mem::ReadSafe(obj + Constants::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        // Read class FName
        uint32_t clsNameIdx = 0;
        if (!Mem::ReadSafe(cls + Constants::OFF_UOBJECT_NAME, clsNameIdx)) continue;

        std::string clsName = FNamePool::GetString(clsNameIdx);
        if (clsName.empty()) continue;

        // Case-insensitive partial match on class name
        std::string lowerClsName = clsName;
        for (auto& c : lowerClsName) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

        if (lowerClsName.find(lowerQuery) == std::string::npos) continue;

        SearchResult sr;
        sr.addr = obj;
        sr.index = i;

        // Read object name
        uint32_t nameIdx = 0;
        if (Mem::ReadSafe(obj + Constants::OFF_UOBJECT_NAME, nameIdx)) {
            sr.name = FNamePool::GetString(nameIdx);
        }
        sr.className = clsName;

        // Read outer
        Mem::ReadSafe(obj + DynOff::UOBJECT_OUTER, sr.outer);

        results.push_back(std::move(sr));
    }

    return results;
}

// Helper: populate an AddressLookupResult from a UObject pointer
static void FillLookupResult(AddressLookupResult& out, uintptr_t obj, int32_t index,
                             int32_t offsetFromBase, bool exact) {
    out.found = true;
    out.exactMatch = exact;
    out.objectAddr = obj;
    out.index = index;
    out.offsetFromBase = offsetFromBase;

    uint32_t nameIdx = 0;
    if (Mem::ReadSafe(obj + Constants::OFF_UOBJECT_NAME, nameIdx)) {
        out.name = FNamePool::GetString(nameIdx);
    }
    uintptr_t cls = 0;
    if (Mem::ReadSafe(obj + Constants::OFF_UOBJECT_CLASS, cls) && cls) {
        uint32_t clsNameIdx = 0;
        if (Mem::ReadSafe(cls + Constants::OFF_UOBJECT_NAME, clsNameIdx)) {
            out.className = FNamePool::GetString(clsNameIdx);
        }
    }
    Mem::ReadSafe(obj + DynOff::UOBJECT_OUTER, out.outer);
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
        LOG_INFO("FindByAddress: No objects within 256KB below 0x%llX",
                 static_cast<unsigned long long>(addr));
        return result;
    }

    LOG_INFO("FindByAddress: No exact match. %d candidates within range. Closest at dist=0x%llX",
             numCandidates, static_cast<unsigned long long>(candidates[0].dist));

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
        if (!Mem::ReadSafe(obj + Constants::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        int32_t propsSize = 0;
        if (!Mem::ReadSafe(cls + DynOff::USTRUCT_PROPSSIZE, propsSize)) continue;
        if (propsSize <= 0 || propsSize > 0x100000) continue;

        // Log top candidates for diagnosis
        if (c < 5) {
            uint32_t nameIdx = 0;
            std::string name = "(read fail)";
            if (Mem::ReadSafe(obj + Constants::OFF_UOBJECT_NAME, nameIdx))
                name = FNamePool::GetString(nameIdx);
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
    // up to a reasonable range (e.g., 16KB), checking each candidate address.

    constexpr uintptr_t MAX_BACKWARD_SCAN = 0x4000;  // 16KB backward scan

    uintptr_t moduleBase = Mem::GetModuleBase(nullptr);
    uintptr_t moduleEnd = moduleBase + Mem::GetModuleSize(nullptr);

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
        if (!Mem::ReadSafe(probe + Constants::OFF_UOBJECT_VTABLE, vtable) || !vtable) continue;

        // VTable should point into the module's address range
        if (vtable < moduleBase || vtable >= moduleEnd) continue;

        // Read ClassPrivate — must be non-null
        uintptr_t cls = 0;
        if (!Mem::ReadSafe(probe + Constants::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        // ClassPrivate's VTable should also be in module range (it's a UClass)
        uintptr_t clsVtable = 0;
        if (!Mem::ReadSafe(cls + Constants::OFF_UOBJECT_VTABLE, clsVtable)) continue;
        if (clsVtable < moduleBase || clsVtable >= moduleEnd) continue;

        // Read InternalIndex — should be reasonable
        int32_t idx = 0;
        if (!Mem::ReadSafe(probe + Constants::OFF_UOBJECT_INDEX, idx)) continue;
        if (idx < 0 || idx > 0x800000) continue;

        // Read FName ComparisonIndex — must resolve to a non-empty string
        uint32_t nameIdx = 0;
        if (!Mem::ReadSafe(probe + Constants::OFF_UOBJECT_NAME, nameIdx)) continue;
        if (nameIdx == 0) continue;  // Index 0 = "None", skip
        std::string name = FNamePool::GetString(nameIdx);
        if (name.empty() || name == "None") continue;

        // Additional validation: name should contain only printable ASCII
        bool validName = true;
        for (char c : name) {
            if (c < 0x20 || c > 0x7E) { validName = false; break; }
        }
        if (!validName) continue;

        // This looks like a valid UObject!
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

    if (bestScanObj && bestScanDist < candidates[0].dist) {
        // Backward scan found a closer UObject than GObjects scan
        FillLookupResult(result, bestScanObj, -1,
                         static_cast<int32_t>(bestScanDist), false);
        LOG_INFO("FindByAddress: Backward scan match: %s at 0x%llX, offset +0x%X",
                 result.name.c_str(),
                 static_cast<unsigned long long>(bestScanObj),
                 result.offsetFromBase);
        return result;
    }

    // --- Fallback: Return closest GObjects object as "nearest" ---
    FillLookupResult(result, candidates[0].obj, candidates[0].idx,
                     static_cast<int32_t>(candidates[0].dist), false);
    result.exactMatch = false;
    LOG_INFO("FindByAddress: Nearest GObjects fallback: %s at 0x%llX, offset +0x%X",
             result.name.c_str(),
             static_cast<unsigned long long>(candidates[0].obj),
             result.offsetFromBase);
    return result;
}

} // namespace ObjectArray
