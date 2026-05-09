// ============================================================
// Aura — 斷頭台的奧拉 (服從之秤 — Obedience Scale)
// ObjectArray: FUObjectArray slot enumeration and validation
// ============================================================

#include "Aura.h"
#include "Macht.h"
#define LOG_CAT "OARR"
#include "Sein.h"
#include "Grimoire.h"
#include "Serie.h"

#include "Ubel.h"

#include <algorithm>
#include <cctype>
#include <chrono>
#include <climits>
#include <mutex>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace Aura {

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

// GAP #1: Decryption hook for encrypted GObjects pointers.
// Default nullptr = identity (zero overhead — no indirect call on hot path).
// Set by SetDecryptFunc() from CE Lua export before Init().
static Aura::DecryptFunc s_decryptFunc = nullptr;

void Aura::SetDecryptFunc(DecryptFunc func) {
    s_decryptFunc = func;
    LOG_INFO("ObjectArray: Custom decryption function %s",
             func ? "SET" : "CLEARED (identity)");
}

uintptr_t Aura::DecryptObjectPtr(uintptr_t rawPtr) {
    if (!rawPtr || !s_decryptFunc) return rawPtr;
    return s_decryptFunc(rawPtr);
}

// GAP #2: Named preset layouts for FChunkedFixedUObjectArray (from Dumper-7 reference).
// Games can reorder struct members; these presets cover known variants.
struct LayoutPreset {
    const char* name;
    ArrayLayout layout;
};

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
};
static constexpr int NUM_UE4_EXTENDED_PRESETS = sizeof(s_ue4ExtendedPresets) / sizeof(s_ue4ExtendedPresets[0]);

// Flat (non-chunked) FFixedUObjectArray layout.
// Objects* points directly to FUObjectItem[] (no chunk pointer indirection).
// Used by early UE4 (4.11-4.22) including Octopath Traveller.
// Layout: { Objects*(8), MaxElements(4), NumElements(4) } — total 16 bytes before FCriticalSection.
static const LayoutPreset s_flatPresets[] = {
    { "Flat", { 0x00, 0x08, 0x0C, -1, -1 } },
};
static constexpr int NUM_FLAT_PRESETS = sizeof(s_flatPresets) / sizeof(s_flatPresets[0]);

// Helper: check if a pointer value looks like a valid heap pointer (not code/null/low)
static bool LooksLikeHeapPtr(uintptr_t ptr) {
    if (!ptr || ptr < 0x10000) return false;
    // Must be in user-mode address range (below kernel boundary)
    if (ptr > 0x00007FFFFFFFFFFF) return false;
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
    if (maxElements < numElements || maxElements > 0x800000) return false;

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
    // Diagnostic: dump first 48 bytes at the GObjects address
    {
        uint64_t dump[6] = {};
        Macht::ReadBytesSafe(addr, dump, sizeof(dump));
        LOG_DEBUG("ObjectArray: GObjects@0x%llX: +00:%016llX +08:%016llX +10:%016llX +18:%016llX +20:%016llX +28:%016llX",
                  (unsigned long long)addr,
                  dump[0], dump[1], dump[2], dump[3], dump[4], dump[5]);
    }

    // --- Tier 1: Try all chunked presets with FULL validation (Dumper-7 rigor) ---
    for (int i = 0; i < NUM_CHUNKED_PRESETS; ++i) {
        const auto& preset = s_chunkedPresets[i];
        if (ValidateChunkedLayout(addr, preset.layout)) {
            s_layout = preset.layout;
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
    {
        int32_t num = 0;
        Macht::ReadSafe(addr + 0x04, num);
        if (num > 0 && num <= 0x800000) {
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
        if (num > 0 && num <= max && max <= 0x800000) {
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
        if (num > 0 && num <= max && max <= 0x800000) {
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
    if (!obj || obj < 0x10000 || obj > 0x00007FFFFFFFFFFF) return false;
    uintptr_t cls = 0;
    if (!Macht::ReadSafe(obj + 0x10, cls)) return false;
    if (cls < 0x10000 || cls > 0x00007FFFFFFFFFFF) return false;
    uintptr_t clsCls = 0;
    if (!Macht::ReadSafe(cls + 0x10, clsCls)) return false;
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
        if (!Macht::ReadSafe(chunkBase + byteOff, obj)) {
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

// Auto-detect FUObjectItem size by probing consecutive items in chunks.
// UE5 (most): 16 bytes, UE4 / some UE5 with clustering: 24 bytes.
//
// Strategy: For each candidate stride, walk chunk at stride-aligned offsets
// counting valid items. Use FNamePool-based name resolution (strong) if available,
// falling back to ClassPrivate chain (weak) if not. Try all strides and pick best.
// Uses tiebreaker: when named counts are equal, prefer stride with fewer bad items.
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
        Macht::ReadSafe(chunkTable + sizeof(uintptr_t), chunk1);
        int32_t numElements = GetCount();
        bool mightBeFlat = false;

        if (chunk0 && numElements > 0) {
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

    uintptr_t object = 0;
    Macht::ReadSafe(itemAddr, object);
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

    // SerialNumber offset depends on item stride:
    //   16B: Object(8) + Flags(4) + Serial(4)                        → +0x0C
    //   24B: Object(8) + Flags(4) + ClusterRootIndex(4) + Serial(4)  → +0x10
    int serialOff = (s_itemSize >= 24) ? 0x10 : 0x0C;
    int32_t serial = 0;
    Macht::ReadSafe(itemAddr + serialOff, serial);
    return serial;
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
    // Forward declared — uses Ubel::GetFullName
    // This is implemented after UStructWalker is available
    (void)fullName;
    return 0;
}

SearchResultSet SearchByName(const std::string& query, int maxResults) {
    SearchResultSet rset;

    // Convert query to lowercase for case-insensitive comparison
    std::string lowerQuery = query;
    for (auto& c : lowerQuery) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

    int32_t count = GetCount();
    rset.scanned = count;
    for (int32_t i = 0; i < count && static_cast<int>(rset.results.size()) < maxResults; ++i) {
        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;
        rset.nonNull++;

        // Read FName from UObject
        uint32_t nameIndex = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIndex)) continue;

        std::string objName = Serie::GetString(nameIndex);
        if (objName.empty()) continue;
        rset.named++;

        // Case-insensitive partial match
        std::string lowerName = objName;
        for (auto& c : lowerName) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

        if (lowerName.find(lowerQuery) == std::string::npos) continue;

        SearchResult sr;
        sr.addr = obj;
        sr.name = objName;

        // Get class name
        uintptr_t cls = 0;
        if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) && cls) {
            uint32_t clsNameIdx = 0;
            if (Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) {
                sr.className = Serie::GetString(clsNameIdx);
            }
        }

        // Get outer
        Macht::ReadSafe(obj + DynOff::UOBJECT_OUTER, sr.outer);

        rset.results.push_back(std::move(sr));
    }

    return rset;
}

SearchResultSet FindInstancesByClass(const std::string& className, bool exactMatch, int maxResults) {
    SearchResultSet rset;

    // Convert query to lowercase for case-insensitive comparison
    std::string lowerQuery = className;
    for (auto& c : lowerQuery) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

    int32_t count = GetCount();
    rset.scanned = count;
    for (int32_t i = 0; i < count && static_cast<int>(rset.results.size()) < maxResults; ++i) {
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

        // Case-insensitive match: exact (equality) or partial (substring)
        std::string lowerClsName = clsName;
        for (auto& c : lowerClsName) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

        if (exactMatch) {
            if (lowerClsName != lowerQuery) continue;
        } else {
            if (lowerClsName.find(lowerQuery) == std::string::npos) continue;
        }

        SearchResult sr;
        sr.addr = obj;
        sr.index = i;

        // Read object name
        uint32_t nameIdx = 0;
        if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx)) {
            sr.name = Serie::GetString(nameIdx);
        }
        sr.className = clsName;

        // Read outer
        Macht::ReadSafe(obj + DynOff::UOBJECT_OUTER, sr.outer);

        rset.results.push_back(std::move(sr));
    }

    Sein::Info("PIPE:find", "FindInstancesByClass '%s': %d found, scanned=%d, nonNull=%d, named=%d",
                 className.c_str(), (int)rset.results.size(), rset.scanned, rset.nonNull, rset.named);
    return rset;
}

// Helper: populate an AddressLookupResult from a UObject pointer.
// `kind` distinguishes confidence levels — see AddressLookupResult comment.
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

        // Read FName ComparisonIndex — must resolve to a non-empty string
        uint32_t nameIdx = 0;
        if (!Macht::ReadSafe(probe + Grimoire::OFF_UOBJECT_NAME, nameIdx)) continue;
        if (nameIdx == 0) continue;  // Index 0 = "None", skip
        std::string name = Serie::GetString(nameIdx);
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

enum class ContainerKind {
    Array,   // TArray.Data buffer, stride = inner element size
    Set,     // TSparseArray.Data buffer, stride = ComputeSetElementStride
    Map,     // TSparseArray.Data buffer, stride = ComputeSetElementStride(pair)
};

struct ContainerCacheEntry {
    int32_t       offset;       // Absolute byte offset within owner UObject
    std::string   name;         // Dotted name (e.g. "Stats.Levels")
    std::string   innerType;    // ArrayProperty: inner; Set: elem; Map: "K → V"
    int32_t       stride;       // Bytes per element/pair within Data buffer
    ContainerKind kind;
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

    auto ci = Ubel::WalkClassEx(structAddr);
    for (const auto& f : ci.Fields) {
        if (!f.Address) continue;

        std::string fullName = namePrefix.empty()
            ? f.Name
            : (namePrefix + "." + f.Name);
        int32_t absOffset = baseOffset + f.Offset;

        if (f.TypeName == "ArrayProperty") {
            int32_t es = Ubel::GetArrayInnerElemSize(f.Address);
            if (es <= 0) continue;
            out.push_back({ absOffset, fullName, f.innerType, es, ContainerKind::Array });
        }
        else if (f.TypeName == "SetProperty") {
            int32_t st = Ubel::GetSetElementStride(f.Address);
            if (st <= 0) continue;
            out.push_back({ absOffset, fullName, f.elemType, st, ContainerKind::Set });
        }
        else if (f.TypeName == "MapProperty") {
            int32_t st = Ubel::GetMapPairStride(f.Address);
            if (st <= 0) continue;
            std::string innerLabel = f.keyType + " → " + f.valueType;
            out.push_back({ absOffset, fullName, innerLabel, st, ContainerKind::Map });
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
    CollectContainersRecursive(cls, /*baseOffset*/ 0, /*namePrefix*/ "",
                               entries, /*depth*/ 0);

    std::lock_guard<std::mutex> lk(s_classContainerMutex);
    auto [ins, _] = s_classContainerCache.emplace(cls, std::move(entries));
    return ins->second;
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
    int32_t classesWalked = 0;
    int32_t scanned = 0;
    bool deadlineHit = false;

    for (int32_t i = 0; i < count && static_cast<int>(matches.size()) < maxResults; ++i) {
        if ((i & 0x3FF) == 0) {
            auto dt = std::chrono::duration_cast<std::chrono::milliseconds>(
                          std::chrono::steady_clock::now() - t0).count();
            if (dt > kDeadlineMs) {
                LOG_INFO("FindInContainers: deadline reached after %d objects (%lld ms)",
                         i, static_cast<long long>(dt));
                deadlineHit = true;
                break;
            }
        }
        scanned = i + 1;

        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;

        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        const auto& containers = GetClassContainers(cls);
        if (containers.empty()) continue;
        ++classesWalked;

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
                matches.push_back(std::move(m));
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
                matches.push_back(std::move(m));
            }

            if (static_cast<int>(matches.size()) >= maxResults) break;
        }
    }

    auto dt = std::chrono::duration_cast<std::chrono::milliseconds>(
                  std::chrono::steady_clock::now() - t0).count();
    if (stats) {
        stats->objectsScanned = scanned;
        stats->classesPrimed  = classesWalked;
        stats->durationMs     = static_cast<int64_t>(dt);
        stats->deadlineHit    = deadlineHit;
    }
    LOG_INFO("FindInContainers: found %d matches in %lld ms (scanned %d/%d, %d non-empty classes%s)",
             static_cast<int>(matches.size()), static_cast<long long>(dt),
             scanned, count, classesWalked, deadlineHit ? ", DEADLINE HIT" : "");
    return matches;
}

// === Reverse Reference Search ===
//
// Per-class cache of pointer-shaped fields and Object array fields.
// Built lazily, mirrors the container cache pattern.
//
// v2 (this revision) covers:
//   - Direct ObjectProperty / ClassProperty / InterfaceProperty
//     (8-byte UObject* read directly at field+0)
//   - Direct WeakObjectProperty / SoftObjectProperty / SoftClassProperty /
//     LazyObjectProperty (FWeakObjectPtr at field+0 → resolved via
//     ResolveWeakObjectPtr; only matches when the soft/lazy ref is
//     currently bound to a live UObject)
//   - TArray<UObject*> / TArray<UClass*> (8-byte stride)
//   - TArray<FScriptInterface> (16-byte stride, ptr at elem+0)
//   - TArray<FWeakObjectPtr> / TArray<FSoftObjectPtr> /
//     TArray<FLazyObjectPtr> (variable stride, FWeakObjectPtr at elem+0)
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

    auto ci = Ubel::WalkClassEx(structAddr);
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
    }
}

static const ClassReferenceMeta& GetClassRefMeta(uintptr_t cls) {
    {
        std::lock_guard<std::mutex> lk(s_classRefMutex);
        auto it = s_classRefCache.find(cls);
        if (it != s_classRefCache.end()) return it->second;
    }

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
    int32_t classesPrimed = 0;
    int32_t scanned = 0;
    bool deadlineHit = false;

    auto pushMatch = [&](ReferenceMatch&& m) -> bool {
        matches.push_back(std::move(m));
        return static_cast<int>(matches.size()) >= maxResults;
    };

    for (int32_t i = 0; i < count && static_cast<int>(matches.size()) < maxResults; ++i) {
        if ((i & 0x3FF) == 0) {
            auto dt = std::chrono::duration_cast<std::chrono::milliseconds>(
                          std::chrono::steady_clock::now() - t0).count();
            if (dt > kDeadlineMs) {
                LOG_INFO("FindReferencesToUObject: deadline reached after %d objects (%lld ms)",
                         i, static_cast<long long>(dt));
                deadlineHit = true;
                break;
            }
        }
        scanned = i + 1;

        uintptr_t obj = GetByIndex(i);
        if (!obj || obj == target) continue;  // Don't report self-reference

        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        const auto& meta = GetClassRefMeta(cls);
        if (meta.empty()) continue;
        ++classesPrimed;

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

    auto dt = std::chrono::duration_cast<std::chrono::milliseconds>(
                  std::chrono::steady_clock::now() - t0).count();
    if (stats) {
        stats->objectsScanned = scanned;
        stats->classesPrimed  = classesPrimed;
        stats->durationMs     = static_cast<int64_t>(dt);
        stats->deadlineHit    = deadlineHit;
    }
    LOG_INFO("FindReferencesToUObject: found %d matches in %lld ms (scanned %d/%d, %d classes with refs%s)",
             static_cast<int>(matches.size()), static_cast<long long>(dt),
             scanned, count, classesPrimed, deadlineHit ? ", DEADLINE HIT" : "");
    return matches;
}

// === Property Keyword Search ===

// Engine packages to skip when gameOnly is true
static bool IsEnginePackage(const std::string& path) {
    static const char* kEnginePrefixes[] = {
        "/Script/Engine",
        "/Script/CoreUObject",
        "/Script/CoreOnline",
        "/Script/UMG",
        "/Script/Slate",
        "/Script/SlateCore",
        "/Script/InputCore",
        "/Script/PhysicsCore",
        "/Script/NavigationSystem",
        "/Script/AIModule",
        "/Script/Niagara",
        "/Script/MovieScene",
        "/Script/LevelSequence",
        "/Script/Landscape",
        "/Script/Foliage",
        "/Script/AnimGraphRuntime",
        "/Script/AudioMixer",
        "/Script/ChaosCloth",
        "/Script/ChaosSolverEngine",
        "/Script/ClothingSystemRuntimeNv",
        "/Script/GeometryCollectionEngine",
        "/Script/FieldSystemEngine",
        "/Script/GameplayTags",
        "/Script/GameplayTasks",
        "/Script/GameplayAbilities",
        "/Script/PacketHandler",
        "/Script/PropertyAccess",
        "/Script/DeveloperSettings",
        "/Script/AssetRegistry",
        "/Script/MediaAssets",
        "/Script/HeadMountedDisplay",
    };

    for (const auto* prefix : kEnginePrefixes) {
        size_t prefixLen = std::strlen(prefix);
        // Match exact prefix followed by end-of-string, '/', or '.'
        if (path.compare(0, prefixLen, prefix) == 0) {
            if (path.size() == prefixLen || path[prefixLen] == '/' || path[prefixLen] == '.') {
                return true;
            }
        }
    }
    return false;
}

static std::string ToLower(const std::string& s) {
    std::string out = s;
    for (auto& c : out) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return out;
}

PropertySearchResult SearchProperties(
    const std::string& query,
    const std::vector<std::string>& typeFilter,
    bool gameOnly,
    int maxResults)
{
    PropertySearchResult result;

    std::string lowerQuery = ToLower(query);

    // Build lowercase type filter set for fast lookup
    std::unordered_set<std::string> typeSet;
    for (const auto& t : typeFilter) typeSet.insert(ToLower(t));

    // Track already-visited UClass addresses to avoid duplicates
    std::unordered_set<uintptr_t> visitedClasses;

    int32_t count = GetCount();
    result.scannedObjects = count;

    for (int32_t i = 0; i < count && static_cast<int>(result.results.size()) < maxResults; ++i) {
        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;

        // Check if this object IS a UClass (its class name == "Class")
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        uint32_t clsNameIdx = 0;
        if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;

        std::string metaClassName = Serie::GetString(clsNameIdx);
        if (metaClassName != "Class") continue;

        // This object is a UClass. Skip if already visited.
        if (!visitedClasses.insert(obj).second) continue;

        // Get class path for game_only filter
        std::string classPath = Ubel::GetFullName(obj);
        if (gameOnly && IsEnginePackage(classPath)) continue;

        result.scannedClasses++;

        // Walk class properties (including inherited)
        ClassInfo ci = Ubel::WalkClassEx(obj);
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

            PropertyMatch match;
            match.className  = ci.Name;
            match.classAddr  = ci.Address;
            match.classPath  = classPath;
            match.superName  = ci.SuperName;
            match.propName   = field.Name;
            match.propType   = field.TypeName;
            match.propOffset = field.Offset;
            match.propSize   = field.Size;
            match.structType = field.structType;
            match.innerType  = field.innerType;
            // Preview metadata
            match.fieldAddr      = field.Address;
            match.boolFieldMask  = field.boolFieldMask;
            match.keyType        = field.keyType;
            match.valueType      = field.valueType;
            result.results.push_back(std::move(match));
        }
    }

    // --- Phase 2: Resolve value previews from representative instances ---
    if (!result.results.empty()) {
        // 2a. Collect unique classAddr set
        std::unordered_set<uintptr_t> needClasses;
        for (const auto& m : result.results)
            needClasses.insert(m.classAddr);

        // 2b. Scan GObjects to find one instance per class
        std::unordered_map<uintptr_t, uintptr_t> instanceMap;
        int32_t cnt = GetCount();
        for (int32_t i = 0; i < cnt && instanceMap.size() < needClasses.size(); ++i) {
            uintptr_t obj = GetByIndex(i);
            if (!obj) continue;

            uintptr_t cls = 0;
            if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

            // Skip if this IS a UClass (we want instances, not the class itself)
            if (needClasses.count(cls) && !instanceMap.count(cls) && obj != cls) {
                instanceMap[cls] = obj;
            }
        }

        // 2c. Read property values and fill previews
        if (!instanceMap.empty()) {
            // Resolve EnumProperty: read UEnum* from FField for matches that need it
            for (auto& m : result.results) {
                if (m.propType == "EnumProperty" && m.enumAddr == 0 && m.fieldAddr) {
                    Macht::ReadSafe(m.fieldAddr + DynOff::FENUMPROP_ENUM, m.enumAddr);
                }
            }
            Ubel::ResolvePropertyPreviews(result.results, instanceMap);
        }
    }

    Sein::Info("PIPE:search", "SearchProperties '%s': %d matches from %d classes (scanned %d objects)",
                 query.c_str(), static_cast<int>(result.results.size()),
                 result.scannedClasses, result.scannedObjects);
    return result;
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

    for (int32_t i = 0; i < count && static_cast<int>(result.results.size()) < maxResults; ++i) {
        uintptr_t obj = GetByIndex(i);
        if (!obj) continue;

        // Check if this object IS a UClass (its class name == "Class")
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        uint32_t clsNameIdx = 0;
        if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;

        std::string metaClassName = Serie::GetString(clsNameIdx);
        if (metaClassName != "Class") continue;

        // Skip if already visited
        if (!visitedClasses.insert(obj).second) continue;

        // Get class path for game_only filter
        std::string classPath = Ubel::GetFullName(obj);
        if (gameOnly && IsEnginePackage(classPath)) continue;

        result.totalClasses++;

        // Walk class to get property count and size
        ClassInfo ci = Ubel::WalkClassEx(obj);

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

    // Sort by heuristic score descending, then alphabetically for ties
    std::sort(result.results.begin(), result.results.end(),
        [](const ClassListEntry& a, const ClassListEntry& b) {
            if (a.heuristicScore != b.heuristicScore)
                return a.heuristicScore > b.heuristicScore;
            return a.className < b.className;
        });

    Sein::Info("PIPE:list", "ListClasses: %d classes (gameOnly=%d, scanned %d objects)",
                 static_cast<int>(result.results.size()), gameOnly ? 1 : 0, result.scannedObjects);
    return result;
}

} // namespace Aura
