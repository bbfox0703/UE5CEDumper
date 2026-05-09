#pragma once

// ============================================================
// Aura — 斷頭台的奧拉 (服從之秤 — Obedience Scale)
// ObjectArray: FUObjectArray slot enumeration and validation
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

namespace Aura {

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
SearchResultSet FindInstancesByClass(const std::string& className, bool exactMatch = false, int maxResults = 500);

// Address-to-Instance reverse lookup result.
//
// Confidence levels (worst to best):
//   exact      — addr IS a UObject pointer (highest confidence)
//   contains   — addr is within UObject + PropertiesSize (high)
//   backward   — backward memory scan found a UObject header (medium —
//                addr is past a NewObject<>'d sub-object not in GObjects)
//   nearest    — closest GObjects entry below addr; addr is BEYOND its
//                PropertiesSize so this is just a "best guess" hint, not a
//                real containment (low — frequently misleading)
struct AddressLookupResult {
    bool        found         = false;
    bool        exactMatch    = false;  // true = addr is a UObject, false = addr is inside a UObject
    std::string matchKind;              // "exact" / "contains" / "backward" / "nearest"
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

// === Container-Aware Address Lookup ===

// One match for an address that falls inside a UObject field's
// heap-allocated container buffer (TArray::Data / TSparseArray::Data).
struct ContainerMatch {
    uintptr_t   ownerObj      = 0;      // UObject that owns the container field
    int32_t     ownerIndex    = -1;     // GObjects index of owner
    std::string ownerName;
    std::string ownerClassName;
    int32_t     fieldOffset   = 0;      // Field offset within owner UObject
    std::string fieldName;
    std::string fieldType;              // "ArrayProperty" / "SetProperty" / "MapProperty"
    std::string innerType;              // Inner type label (Set: elem; Map: "K → V")
    int32_t     elementIndex  = 0;      // (addr - dataAddr) / stride (sparse index for Map/Set)
    int32_t     elementSize   = 0;      // Per-element / per-pair stride
    int32_t     intraOffset   = 0;      // (addr - elementStart) within element
    uintptr_t   dataAddr      = 0;      // TArray::Data / TSparseArray::Data base
    int32_t     count         = 0;      // Logical element count (allocated only for Map/Set)
    // Diagnostic note about match confidence:
    //   ""       — solid hit (within Count, allocated slot)
    //   "slack"  — Array index is in [Count, Max) — uninitialised / freed slack
    //   "freed"  — Map/Set slot is on the free list (stale data, may still match)
    std::string note;
};

// Diagnostic stats from a container scan — surfaced through the pipe so
// the UI can tell the user whether a "not found" was a complete scan
// or got cut off by the deadline.
struct ContainerScanStats {
    int32_t objectsScanned   = 0;   // UObjects iterated
    int32_t objectsTotal     = 0;   // Total in GObjects
    int32_t classesPrimed    = 0;   // Unique classes touched (cache built)
    int64_t durationMs       = 0;
    bool    deadlineHit      = false;
};

// Scan all UObjects' container fields for `addr`. Returns matches where
// addr falls in [Data, Data + bound). Covers ArrayProperty (TArray.Data,
// including slack slots), SetProperty (TSparseArray.Data, including freed
// slots), and MapProperty (TSparseArray.Data of TPair). Has an internal
// time deadline and per-class field cache; cache persists for the DLL
// lifetime so subsequent calls are much faster.
//
// `stats` (optional out param) receives diagnostic counters; if non-null
// the caller can detect a truncated scan via `deadlineHit`.
std::vector<ContainerMatch> FindInContainers(uintptr_t addr, int32_t maxResults = 16,
                                              ContainerScanStats* stats = nullptr);

// === Reverse Reference Search (logical-parent navigation) ===
//
// One match for a UObject that holds a pointer to the target UObject.
// Used to answer "what owns this Item?" — UE's `OuterPrivate` is a
// naming-hierarchy parent (often `/Engine/Transient` for runtime objects)
// rather than the logical gameplay parent. Reverse-scanning all UObjects'
// pointer fields and Object array elements gives the actual owner.
struct ReferenceMatch {
    uintptr_t   ownerObj      = 0;
    int32_t     ownerIndex    = -1;
    std::string ownerName;
    std::string ownerClassName;
    int32_t     fieldOffset   = 0;        // Absolute field offset within owner
    std::string fieldName;                // Dotted path (e.g. "Stats.Equipment");
                                          // Map matches append ".Key" / ".Value"
    std::string fieldType;                // "ObjectProperty" / "ClassProperty" /
                                          // "InterfaceProperty" / "WeakObjectProperty" /
                                          // "SoftObjectProperty" / "SoftClassProperty" /
                                          // "LazyObjectProperty" / "ArrayProperty" /
                                          // "MapProperty" / "SetProperty"
    std::string innerType;                // For Array: inner element type;
                                          // For Set: element type;
                                          // For Map: "<keyType> → <valueType>"
    int32_t     elementIndex  = -1;       // -1 for direct field, >=0 for array/
                                          // map/set element (sparse index for
                                          // Map/Set)
};

// Find UObjects that hold a pointer to `target`. Walks every UObject's:
//   - ObjectProperty / ClassProperty / InterfaceProperty (8B raw pointer)
//   - WeakObjectProperty / SoftObject{Class}Property / LazyObjectProperty
//     (resolves embedded FWeakObjectPtr; only matches when bound to live
//     UObject)
//   - TArray of any of the above
//   - TMap with Object/Class key and/or value (allocated slots only)
//   - TSet with Object/Class element (allocated slots only)
// Walks include fields nested inside StructProperty (depth 3).
//
// Has its own per-class metadata cache (separate from container cache);
// first call primes, subsequent calls are fast.
//
// `stats` mirrors ContainerScanStats — duration and deadline indication.
std::vector<ReferenceMatch> FindReferencesToUObject(uintptr_t target,
                                                     int32_t maxResults = 32,
                                                     ContainerScanStats* stats = nullptr);

// === Property Keyword Search ===

struct PropertyMatch {
    std::string className;
    uintptr_t   classAddr = 0;
    std::string classPath;
    std::string superName;
    std::string propName;
    std::string propType;
    int32_t     propOffset = 0;
    int32_t     propSize   = 0;
    std::string structType;   // StructProperty -> inner struct name
    std::string innerType;    // ArrayProperty -> inner element type

    // Preview support — populated in Phase 2 of SearchProperties
    std::string preview;           // Inline value preview from a representative instance
    uintptr_t   fieldAddr   = 0;   // FField/FProperty address (for enum resolve)
    uint8_t     boolFieldMask  = 0; // BoolProperty: FieldMask byte
    uint8_t     boolByteOffset = 0; // BoolProperty: ByteOffset within property
    uintptr_t   enumAddr    = 0;   // EnumProperty: UEnum* for name resolution
    std::string keyType;           // MapProperty: key type name
    std::string valueType;         // MapProperty: value type name
};

struct PropertySearchResult {
    int scannedClasses = 0;
    int scannedObjects = 0;
    std::vector<PropertyMatch> results;
};

// Search for properties matching a keyword across all UClass objects.
// query: case-insensitive substring match on property name.
// typeFilter: optional list of property types (e.g. "FloatProperty"); empty = all types.
// gameOnly: skip engine packages (/Script/Engine, /Script/CoreUObject, etc.)
PropertySearchResult SearchProperties(
    const std::string& query,
    const std::vector<std::string>& typeFilter,
    bool gameOnly,
    int maxResults = 200);

// === Game Class List ===

struct ClassListEntry {
    std::string className;
    uintptr_t   classAddr;
    std::string classPath;
    std::string superName;
    int32_t     propertyCount;
    int32_t     propertiesSize;
    int32_t     heuristicScore;   // Auto-ranked suspicion score (higher = more interesting for RE)
};

struct ClassListResult {
    int scannedObjects = 0;
    int totalClasses = 0;
    std::vector<ClassListEntry> results;
};

// List all UClass objects, optionally filtering out engine packages.
ClassListResult ListClasses(bool gameOnly, int maxResults = 5000);

} // namespace Aura
