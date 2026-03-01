#pragma once

// ============================================================
// ObjectArray.h — FChunkedFixedUObjectArray access
// ============================================================

#include <cstdint>
#include <functional>
#include <string>

// FUObjectItem structure (in FChunkedFixedUObjectArray)
// Size varies by UE version — auto-detected at Init() time:
//   UE5 (most):  16 bytes { Object*(8), Flags(4), SerialNumber(4) }
//   UE4 / some UE5: 24 bytes { Object*(8), Flags(4), ClusterRootIndex(4), SerialNumber(4), _pad(4) }
// Only the Object* field at +0x00 is used; the rest is stride padding.
struct FUObjectItem {
    uintptr_t Object;           // UObject* (always at +0x00)
    int32_t   Flags;
    int32_t   SerialNumber;
};

namespace ObjectArray {

// === Encrypted GObjects Support (GAP #1) ===
// Some anti-cheat games encrypt the Objects pointer in FUObjectArray.
// Set a custom decryption function BEFORE calling Init().
// Default: nullptr (identity, zero overhead for non-encrypted games).
using DecryptFunc = uintptr_t(*)(uintptr_t rawPtr);
void SetDecryptFunc(DecryptFunc func);
uintptr_t DecryptObjectPtr(uintptr_t rawPtr);

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

// Read the SerialNumber of the FUObjectItem at the given index.
// Handles both 16-byte (serial@+0x0C) and 24-byte (serial@+0x10) items.
int32_t GetSerialNumber(int32_t index);

// Iterate all valid objects
// Callback: return false to stop iteration
void ForEach(std::function<bool(int32_t idx, uintptr_t obj)> cb);

// Find first object matching name (linear scan)
uintptr_t FindByName(const std::string& name);

// Find first object matching full path (linear scan)
uintptr_t FindByFullName(const std::string& fullName);

// Get the detected FUObjectItem stride in bytes (16 or 24)
int GetItemSize();

// Whether the GObjects array is a flat (non-chunked) FFixedUObjectArray.
// Flat arrays were used in UE4.11-4.20; chunked arrays in UE4.21+ and all UE5.
bool IsFlat();

// Search objects by partial name (case-insensitive), returns up to maxResults
struct SearchResult {
    uintptr_t addr;
    int32_t   index;       // InternalIndex in GObjects
    std::string name;
    std::string className;
    uintptr_t outer;
};

// Search results with diagnostic counters for debugging
struct SearchResultSet {
    std::vector<SearchResult> results;
    int32_t scanned = 0;    // Total indices iterated (= GetCount() at call time)
    int32_t nonNull = 0;    // Objects that were non-null
    int32_t named   = 0;    // Objects whose class name resolved successfully
};

SearchResultSet SearchByName(const std::string& query, int maxResults = 200);

// Find all instances whose class name matches (case-insensitive partial match)
// Returns addr, index, name, className, outer for each instance
SearchResultSet FindInstancesByClass(const std::string& className, int maxResults = 500);

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
