#pragma once

// ============================================================
// ObjectArray.h — FChunkedFixedUObjectArray access
// ============================================================

#include <cstdint>
#include <functional>
#include <string>

// UE5 FUObjectItem structure
struct FUObjectItem {
    uintptr_t Object;           // UObject*
    int32_t   Flags;
    int32_t   ClusterRootIndex;
    int32_t   SerialNumber;
    int32_t   _pad;
};

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

} // namespace ObjectArray
