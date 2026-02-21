#pragma once

// ============================================================
// ObjectArray.h — FChunkedFixedUObjectArray access
// ============================================================

#include <cstdint>
#include <functional>
#include <string>

// UE5 FUObjectItem structure (in FChunkedFixedUObjectArray)
// Standard UE5 layout: 16 bytes per item
//   +0x00  Object*       (8 bytes, UObject*)
//   +0x08  Flags         (4 bytes, EInternalObjectFlags)
//   +0x0C  SerialNumber  (4 bytes)
struct FUObjectItem {
    uintptr_t Object;           // UObject*
    int32_t   Flags;
    int32_t   SerialNumber;
};
static_assert(sizeof(FUObjectItem) == 16, "FUObjectItem must be 16 bytes for correct chunk stride");

namespace ObjectArray {

// Initialize with the FUObjectArray address found by OffsetFinder
void Init(uintptr_t gobjectsAddr);

// Get total number of allocated objects
int32_t GetCount();

// Get max number of objects
int32_t GetMax();

// Get UObject* by index (returns 0 if invalid/null)
uintptr_t GetByIndex(int32_t index);

// Get FUObjectItem by index (returns nullptr if invalid)
FUObjectItem* GetItem(int32_t index);

// Iterate all valid objects
// Callback: return false to stop iteration
void ForEach(std::function<bool(int32_t idx, uintptr_t obj)> cb);

// Find first object matching name (linear scan)
uintptr_t FindByName(const std::string& name);

// Find first object matching full path (linear scan)
uintptr_t FindByFullName(const std::string& fullName);

// Search objects by partial name (case-insensitive), returns up to maxResults
struct SearchResult {
    uintptr_t addr;
    int32_t   index;       // InternalIndex in GObjects
    std::string name;
    std::string className;
    uintptr_t outer;
};
std::vector<SearchResult> SearchByName(const std::string& query, int maxResults = 200);

// Find all instances whose class name matches (case-insensitive partial match)
// Returns addr, index, name, className, outer for each instance
std::vector<SearchResult> FindInstancesByClass(const std::string& className, int maxResults = 500);

// Address-to-Instance reverse lookup result
struct AddressLookupResult {
    bool        found         = false;
    bool        exactMatch    = false;  // true = addr is a UObject, false = addr is inside a UObject
    uintptr_t   objectAddr    = 0;      // The owning UObject address
    int32_t     index         = -1;     // InternalIndex in GObjects
    std::string name;
    std::string className;
    uintptr_t   outer         = 0;
    int32_t     offsetFromBase = 0;     // addr - objectAddr (0 for exact match)
};

// Given an arbitrary address, find which UObject it belongs to.
// First tries exact match (is this address a UObject?), then containment
// (is this address inside a UObject's property data?).
AddressLookupResult FindByAddress(uintptr_t addr);

} // namespace ObjectArray
