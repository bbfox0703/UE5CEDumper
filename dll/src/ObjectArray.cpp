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

static bool DetectLayout(uintptr_t addr) {
    // Default layout: Objects at 0x00, then PreAllocatedObjects, then MaxElements, NumElements
    int32_t numAtDefault = 0;
    int32_t maxAtDefault = 0;
    Mem::ReadSafe(addr + 0x14, numAtDefault);
    Mem::ReadSafe(addr + 0x10, maxAtDefault);

    if (numAtDefault > 0 && numAtDefault <= maxAtDefault && maxAtDefault <= 0x800000) {
        s_layout = { 0x00, 0x10, 0x14, 0x18, 0x1C };
        LOG_INFO("ObjectArray: Default layout detected (Num=%d, Max=%d)", numAtDefault, maxAtDefault);
        return true;
    }

    // Alternate layout (some games): Objects at 0x10, NumElements at 0x04
    int32_t numAtAlt = 0;
    Mem::ReadSafe(addr + 0x04, numAtAlt);

    if (numAtAlt > 0 && numAtAlt <= 0x800000) {
        s_layout = { 0x10, 0x08, 0x04, 0x0C, -1 };
        LOG_INFO("ObjectArray: Alternate layout detected (Num=%d)", numAtAlt);
        return true;
    }

    LOG_WARN("ObjectArray: Could not detect layout, using default");
    return true;
}

void Init(uintptr_t gobjectsAddr) {
    s_arrayAddr = gobjectsAddr;
    DetectLayout(gobjectsAddr);
    LOG_INFO("ObjectArray: Initialized at 0x%llX, Count=%d",
             static_cast<unsigned long long>(gobjectsAddr), GetCount());
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

uintptr_t GetByIndex(int32_t index) {
    if (!s_arrayAddr || index < 0 || index >= GetCount()) return 0;

    // Read chunk table pointer
    uintptr_t chunkTable = 0;
    if (!Mem::ReadSafe(s_arrayAddr + s_layout.objectsOffset, chunkTable) || !chunkTable) return 0;

    int32_t chunkIndex = index / Constants::OBJECTS_PER_CHUNK;
    int32_t withinChunk = index % Constants::OBJECTS_PER_CHUNK;

    // Read chunk pointer from table
    uintptr_t chunk = 0;
    if (!Mem::ReadSafe(chunkTable + chunkIndex * sizeof(uintptr_t), chunk) || !chunk) return 0;

    // Read FUObjectItem at the index within chunk
    uintptr_t itemAddr = chunk + withinChunk * sizeof(FUObjectItem);
    uintptr_t object = 0;
    Mem::ReadSafe(itemAddr, object);

    return object;
}

FUObjectItem* GetItem(int32_t index) {
    if (!s_arrayAddr || index < 0 || index >= GetCount()) return nullptr;

    uintptr_t chunkTable = 0;
    if (!Mem::ReadSafe(s_arrayAddr + s_layout.objectsOffset, chunkTable) || !chunkTable) return nullptr;

    int32_t chunkIndex = index / Constants::OBJECTS_PER_CHUNK;
    int32_t withinChunk = index % Constants::OBJECTS_PER_CHUNK;

    uintptr_t chunk = 0;
    if (!Mem::ReadSafe(chunkTable + chunkIndex * sizeof(uintptr_t), chunk) || !chunk) return nullptr;

    return Mem::Ptr<FUObjectItem>(chunk + withinChunk * sizeof(FUObjectItem));
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
