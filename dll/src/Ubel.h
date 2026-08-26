#pragma once

// ============================================================
// Ubel — 尤蓓爾 (外科式暗殺者 — Surgical Assassin)
// UStructWalker: FField chain traversal and property reading
// ============================================================

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>
#include <vector>

struct FieldInfo {
    uintptr_t   Address;        // FField* address
    std::string Name;
    std::string TypeName;
    int32_t     Offset;
    int32_t     Size;           // ElementSize — ONE element (per-element stride)
    int32_t     ArrayDim = 1;   // Static C-array dimension (Type Foo[N]); 1 for scalars.
                                // Full field footprint = Size * ArrayDim. Read from
                                // FProperty::ArrayDim (== ElementSize - 4 on every UE
                                // layout). Used by Ubel::ComputeClassHoles so a static
                                // array isn't mistaken for a hole after its first element.
    uint64_t    PropertyFlags;

    // === Extended type metadata (populated by WalkClassEx) ===
    std::string structType;      // StructProperty -> UScriptStruct name
    std::string objClassName;    // ObjectProperty/ClassProperty -> target UClass name
    std::string innerType;       // ArrayProperty -> inner FProperty type name
    std::string innerStructType; // ArrayProperty of struct -> inner struct name
    std::string innerObjClass;   // ArrayProperty of object -> inner class name
    std::string keyType;         // MapProperty -> key FProperty type name
    std::string keyStructType;   // MapProperty key struct name (if StructProperty)
    std::string valueType;       // MapProperty -> value FProperty type name
    std::string valueStructType; // MapProperty value struct name (if StructProperty)
    std::string elemType;        // SetProperty -> element FProperty type name
    std::string elemStructType;  // SetProperty element struct name (if StructProperty)
    std::string enumName;        // EnumProperty/ByteProperty -> UEnum name
    uint8_t     boolFieldMask = 0; // BoolProperty -> FieldMask byte (0 = not resolved)
};

struct ClassInfo {
    std::string            Name;
    std::string            FullPath;
    uintptr_t              Address;       // UClass* address
    uintptr_t              SuperClass;    // Super UClass* address
    std::string            SuperName;
    int32_t                PropertiesSize;
    // The immediate super's PropertiesSize, i.e. where THIS class's own properties begin.
    // Fields carries the whole SuperStruct chain (they are prepended below), so a consumer
    // that must tell own from inherited -- the SDK header emitter -- cannot do it without
    // this number, and nothing else on the wire implies it (audit #5 W2).
    int32_t                SuperPropertiesSize = 0;
    std::vector<FieldInfo> Fields;
};

struct FunctionParam {
    std::string name;
    std::string typeName;
    int32_t     size = 0;
    int32_t     offset = -1;  // Offset_Internal within param buffer (-1 = unknown)
    bool        isOut = false;
    bool        isReturn = false;
    std::string structType;     // UScriptStruct name for StructProperty params (empty otherwise)
    // Stage 1 (Invoke param picker): target UClass name for pointer-flavoured
    // params (ObjectProperty / ClassProperty / Soft* / Weak* / Lazy* /
    // InterfaceProperty). Mirrors FieldInfo::objClassName. Empty for scalar,
    // struct, container, and bool params.
    std::string objClassName;

    // Phase B: sub-field layout for StructProperty params (dynamic discovery)
    struct StructSubField {
        std::string name;
        std::string typeName;
        int32_t     offset = 0;
        int32_t     size = 0;
    };
    std::vector<StructSubField> structFields;
};

struct FunctionInfo {
    std::string name;
    std::string fullName;
    uintptr_t   address = 0;
    uint32_t    functionFlags = 0;
    uint8_t     numParms = 0;       // UFunction::NumParms (includes return param)
    uint16_t    parmsSize = 0;      // UFunction::ParmsSize (total param buffer bytes)
    uint16_t    returnValueOffset = 0xFFFF; // UFunction::ReturnValueOffset (0xFFFF = void)
    std::vector<FunctionParam> params;
    std::string returnType;  // empty if void
};

namespace Ubel {

// The exact inputs Ubel::GetName's decode consumes: the FName at UObject+0x18 is
// read as { int32 ComparisonIndex; int32 Number } and handed to Serie::GetString,
// so the resolved string is a pure function of these two int32s.
//
// That is what makes them a sound witness for the per-UObject name cache, which is
// keyed by a raw address the engine recycles (audit #5 U6/F3). FNamePool entries are
// append-only for the life of the process — only ~FNameEntryAllocator frees them —
// so if these bytes still read the same, the cached string is still the correct
// answer, whoever owns the address now. It is also strictly cheaper than the FNamePool
// lookup it guards: two int32 reads against a pool walk.
//
// Deliberately NOT the (InternalIndex, SerialNumber) pair the audit prescribed. UE
// zeroes SerialNumber in FreeUObjectIndex and allocates it lazily, essentially only
// on weak-pointer creation, so most objects carry 0 for life and a stale witness of
// (i, 0) matches a recycled (i, 0) — silent in exactly the case it exists to catch.
// It would also have rested on Aura::GetSerialNumber, whose own comment marks the
// packed FUObjectItem layout *** UNVERIFIED ***.
//
// `number` is load-bearing: dropping it is precisely audit #5 U8, which shipped once
// already and rendered Slot_1 / Slot_2 / Slot_3 all as "Slot".
struct NameWitness {
    int32_t comparisonIndex = 0;
    int32_t number = 0;

    bool operator==(const NameWitness& other) const {
        return comparisonIndex == other.comparisonIndex && number == other.number;
    }
    bool operator!=(const NameWitness& other) const { return !(*this == other); }
};

// Walk a UClass/UStruct and enumerate all fields (including inherited)
ClassInfo WalkClass(uintptr_t uclassAddr);

// Walk a UClass/UStruct with extended type metadata per field.
// Calls WalkClass(), then enriches each FieldInfo with struct types,
// inner types, enum names, bool masks, etc. by reading FProperty chain.
//
// MEMOIZED, and returns a REFERENCE into that memo — bind it with `const auto&`
// unless you actually need a mutable copy. The entry is immortal for the process
// (node-based map, never erased or cleared), so the reference stays valid. Do not
// cast away the const: mutating the returned object would corrupt every later
// caller's view.
//
// Yields a reference to a shared EMPTY ClassInfo — never a cached one — for a null
// uclassAddr and for any address that fails ShouldPublishClassWalk (unmapped, or an
// implausible PropertiesSize). Callers must treat an empty Fields as "skip this
// address", which is what every existing call site already does; it is NOT a signal
// that the class genuinely declares nothing, because a field-less UCLASS is
// legitimate and IS memoized. (audit #5 U4)
const ClassInfo& WalkClassEx(uintptr_t uclassAddr);

// ============================================================
// Class-walk cache bound (audit #5 U5)
// ============================================================
//
// The finding was re-filed FOUR times saying eviction is illegal until
// WalkClassEx stops returning `const ClassInfo&`. That is right about the
// ENRICHED cache and WRONG about this one: WalkClass returns BY VALUE, and all
// three s_walkClassCache touch points copy under the mutex — so bounding it is
// legal today with zero call-site change. Half the bytes were reclaimable the
// whole time.
//
// What is NOT done here, deliberately: the enriched WalkClassEx memo (its
// `const&` return is the real blocker), and Aura's s_classContainerCache, which
// is the same shape with the same blocker and is tracked as A10.

/// Rough heap cost of one cached ClassInfo, for the get_diagnostics counter.
/// Pure and approximate on purpose — it exists to turn "unbounded" into a
/// per-game number, not to be exact. Counts the two owned strings and the field
/// vector; std::string's SSO makes anything finer a lie about the allocator.
inline size_t EstimateClassInfoBytes(size_t nameLen, size_t pathLen,
                                     size_t superNameLen, size_t fieldCount,
                                     size_t bytesPerField) {
    return sizeof(ClassInfo)
         + nameLen + pathLen + superNameLen
         + fieldCount * bytesPerField;
}

/// Entries kept in the plain class-walk cache before the least-recently-used one
/// is evicted.
///
/// 2048, NOT the 512 first proposed. 512 was sized against "~50x the deepest
/// super chain", which is the wrong denominator: the working set is the classes
/// touched in ONE scan pass, and the DSClient reference log touches 10,046
/// distinct classes. 2048 entries is roughly 55K FieldInfo, and still reclaims
/// most of what the unbounded map held.
///
/// Tune it with the get_diagnostics `class_cache` counters, never with the
/// 0.038 ms average walk time — that average is dominated by cache HITS, so it
/// says nothing about what an eviction costs.
constexpr size_t kMaxWalkClassCacheEntries = 2048;

/// Snapshot of the plain class-walk cache, for get_diagnostics. Turns
/// "unbounded" into a per-game number, which is the only thing that makes the
/// bound above tunable on evidence rather than on one game's log extrapolation.
void GetClassCacheStats(size_t& outEntries, size_t& outFields, size_t& outApproxBytes);

// --- Shared reflection field lookup ---
// (Extracted from the Debug Camera helpers in Frieren.cpp, build 1014;
//  shared by the Debug Camera flow and Wirbel/Teleport.)
//
// Find a field by name within a class (inherited fields included).
// A case-insensitive exact match on `exact` wins; otherwise the first
// field whose name contains `contains` (and, when given, does NOT contain
// `excluding`) and whose TypeName equals `typeFilter` (when given) is
// returned. Returns true and fills `out` on success.
bool FindField(uintptr_t classAddr, const char* exact,
               const char* contains, const char* excluding,
               const char* typeFilter, FieldInfo& out);

// Offset-only convenience wrapper over FindField. Returns -1 when not found.
int32_t FindFieldOffset(uintptr_t classAddr, const char* exact,
                        const char* contains = nullptr,
                        const char* excluding = nullptr,
                        const char* typeFilter = nullptr);

// Resolve the per-element size of an ArrayProperty's Inner FProperty.
// Returns 0 if the size cannot be determined (rare: unknown inner type
// without a usable FPROPERTY_ELEMSIZE or PropertiesSize).
// Used by container-aware Address Finder to compute element index.
int32_t GetArrayInnerElemSize(uintptr_t fieldAddr);

// Per-element stride within an FSetProperty's TSparseArray.Data buffer.
// Returns 0 when the inner element size is undetermined.
int32_t GetSetElementStride(uintptr_t fieldAddr);

// Resolve the inner-element UScriptStruct* of an ArrayProperty / SetProperty
// whose element type is StructProperty (both probe Inner at the same offset).
// Returns 0 when the element is not a struct or the address can't be resolved.
// Used by the recursive deep container scan to descend into struct elements.
uintptr_t GetContainerInnerStructAddr(uintptr_t fieldAddr);

// Per-pair stride within an FMapProperty's TSparseArray.Data buffer
// (TPair<K,V> aligned + TSetElement hash overhead).
// Returns 0 when key or value size is undetermined.
int32_t GetMapPairStride(uintptr_t fieldAddr);

// Full TMap pair layout — used by reverse-reference scan to extract the
// pointer side(s) of a TMap<K, V> when either is an Object/Class property.
struct MapPairLayout {
    int32_t keySize     = 0;
    int32_t valueSize   = 0;
    int32_t valueOffset = 0;  // ComputeMapValueOffset(keySize, valueSize)
    int32_t pairStride  = 0;  // ComputeSetElementStride(valueOffset + valueSize)
    // UScriptStruct* for the key / value when that side is a StructProperty
    // (0 otherwise). Lets the deep container scan descend into a TMap whose
    // value (or key) is a struct holding further nested containers.
    uintptr_t keyStructAddr   = 0;
    uintptr_t valueStructAddr = 0;
};
bool GetMapPairLayout(uintptr_t fieldAddr, MapPairLayout& out);

// Walk all UFunctions of a UClass.
// Iterates the function chain, resolving parameters and return type.
std::vector<FunctionInfo> WalkFunctions(uintptr_t uclassAddr);

// ── Resolving a UFunction BY NAME on a class, INCLUDING inherited ones ──────
// [INVOKEINHERIT-2026-08-20]
//
// WalkFunctions above walks one UClass's OWN UStruct::Children chain and stops there.
// That is right for LISTING — the UI attributes each function to its declaring class, and
// inheriting would repeat AActor's 140 entries under every actor class — but it is wrong
// for RESOLVING, and the by-name resolvers were built straight on top of it. The result:
// `SetActorHiddenInGame` on a StaticMeshActor came back "Function not found" for a function
// the tool's own listing had produced, while the same call on an instance whose class IS
// Actor worked. AActor declares 140 functions, APawn 33, ACharacter 48 — none of them
// reachable by name on any derived instance, which on a real game means K2_TeleportTo,
// SetActorLocation and Jump among ~221 others.
//
// The traversal lives here, in the header, ON PURPOSE: no test target compiles Ubel.cpp
// (nor Frieren.cpp / Mimic.cpp, the two call sites), so a rule left in a .cpp could not be
// pinned at all. Taking the per-class listing and the super read as callables lets a test
// drive a synthetic class chain and keeps this free of any memory access.
//
// ⚠ Both passes run over the WHOLE chain before the other begins. An exact match on a base
// class must beat a case-insensitive match on a derived one, so this cannot be written as
// "try both at each level on the way up".
template <class ListFuncs, class ReadSuper>
inline bool ResolveFunctionInChain(uintptr_t classAddr, const char* funcName,
                                   ListFuncs listFuncs, ReadSuper readSuper,
                                   FunctionInfo& out, int* levelsWalked = nullptr) {
    if (levelsWalked) *levelsWalked = 0;
    if (!classAddr || !funcName || !funcName[0]) return false;

    // Collect the chain first. A malformed/mid-teardown SuperStruct can point at itself or
    // form a cycle, so bound it the way Dunste::FindFuncByName already does.
    constexpr int kMaxSuperDepth = 64;
    std::vector<std::vector<FunctionInfo>> levels;
    uintptr_t cls = classAddr;
    for (int depth = 0; cls && depth < kMaxSuperDepth; ++depth) {
        levels.push_back(listFuncs(cls));
        uintptr_t super = 0;
        if (!readSuper(cls, super) || super == 0 || super == cls) break;
        cls = super;
    }
    if (levelsWalked) *levelsWalked = static_cast<int>(levels.size());

    // Pass 1: exact, most-derived first, so an override beats the base it overrides.
    for (const auto& level : levels)
        for (const auto& f : level)
            if (f.name == funcName) { out = f; return true; }

    // Pass 2: case-insensitive, same order. Kept as a separate sweep — see the note above.
    std::string want(funcName);
    std::transform(want.begin(), want.end(), want.begin(),
                   [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    for (const auto& level : levels) {
        for (const auto& f : level) {
            std::string have = f.name;
            std::transform(have.begin(), have.end(), have.begin(),
                           [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
            if (have == want) { out = f; return true; }
        }
    }
    return false;
}

// Resolve a SINGLE UFunction* to its (name, fullName, functionFlags, numParms,
// parmsSize) — no param-chain walk. Validates the meta-class name == "Function"
// first so a stale/recycled pointer (e.g. one recorded by the Live PE profiler
// before a GC/level-load reused its slot) fails safe. Returns false when funcAddr
// is not (or no longer) a UFunction.
bool ResolveFunctionInfo(uintptr_t funcAddr, FunctionInfo& out);

// Get the UClass* of a UObject
uintptr_t GetClass(uintptr_t uobjectAddr);

// Get the Outer object of a UObject
uintptr_t GetOuter(uintptr_t uobjectAddr);

// Get the FName-based name of a UObject
std::string GetName(uintptr_t uobjectAddr);

// Release the per-UObject GetName cache (bounds long-session growth + clears
// stale names after UObject address reuse). Call at snapshot/re-scan start.
void ClearNameCache();

// Lazy UE5.5+ version marker: true once any class walk has encountered a reflected
// Utf8StrProperty or AnsiStrProperty (those FProperty types were added in UE5.5).
// Set cheaply during WalkClass (a string compare on data it already reads); the
// version badge refines off it (Frieren::UE5_GetVersion) without a dedicated scan.
bool SawUtf8OrAnsiStr();

// Get the full path name (e.g., /Game/BP_Player.BP_Player_C)
std::string GetFullName(uintptr_t uobjectAddr);

// Resolve an FProperty (UE4.25+/UE5) or UProperty (UE4 <4.25) address to its
// (name, type). Validates by requiring the type name to contain "Property"
// (e.g. "FloatProperty", "StructProperty"); returns false for anything that
// doesn't deref to a real property — used by the function->property xref scan
// to confirm a bytecode-embedded pointer really is a property descriptor.
bool ResolvePropertyNameType(uintptr_t fieldAddr, std::string& outName, std::string& outType);

// Get the internal index of a UObject
int32_t GetIndex(uintptr_t uobjectAddr);

// --- String-reading helpers (Phase 2 Value Search) ---
//
// Read an FString (TArray<TCHAR>) at instanceAddr + offset into UTF-8.
// Returns "" on failure or out-of-range length. Caps at kMaxFStringChars
// (8192) wide chars — the cap bounds the allocation against a garbage Count
// from freed memory, not string length, so long descriptions/dialogue lines
// resolve rather than reading as "(empty)" (audit #5 U10).
std::string ReadFStringAt(uintptr_t instanceAddr, int32_t offset);

// Read an FName at instanceAddr + offset and resolve it to a string
// via Serie::GetString. Returns "" on failure or "None" for the
// canonical empty FName. Handles both 8-byte (Index+Number) and
// 16-byte (CasePreservingName) FName layouts.
std::string ReadFNameAt(uintptr_t instanceAddr, int32_t offset);

// Read an FText at instanceAddr + offset and return the embedded
// display string. Cooked builds vary; the implementation probes a
// handful of well-known offsets inside the ITextData payload looking
// for a valid FString header. Returns "" if nothing resolvable.
std::string ReadFTextStringAt(uintptr_t instanceAddr, int32_t offset);

// --- Live Instance Walking ---

// A single field value read from a live instance
struct LiveFieldValue {
    std::string name;
    std::string typeName;
    int32_t     offset   = 0;
    int32_t     size     = 0;

    // Raw hex value (always populated for readable fields)
    std::string hexValue;

    // Human-readable typed value (for Float, Int, Bool, etc.)
    std::string typedValue;

    // For ObjectProperty: pointer to the referenced object
    uintptr_t   ptrValue     = 0;
    std::string ptrName;          // Name of the pointed-to object
    std::string ptrClassName;     // Class name of the pointed-to object
    uintptr_t   ptrClassAddr = 0; // UClass* of the pointed-to object (for CSX drilldown)

    // For BoolProperty: bit field info
    int32_t     boolBitIndex = -1;  // Bit index (0-7) within the byte; -1 = not a bool
    uint8_t     boolFieldMask = 0;  // Raw FieldMask byte from FBoolProperty
    uint8_t     boolByteOffset = 0; // ByteOffset within the property offset

    // For ArrayProperty: TArray header info
    int32_t     arrayCount = -1;  // -1 = not an array
    std::string arrayInnerType;        // Inner element type (e.g., "FloatProperty", "StructProperty")
    std::string arrayInnerStructType;  // For struct arrays: UScriptStruct name (e.g., "FVector")
    int32_t     arrayElemSize = 0;     // Element size in bytes
    uintptr_t   arrayInnerFFieldAddr = 0;  // Inner FProperty* (for read_array_elements command)
    uintptr_t   arrayInnerStructAddr = 0;  // UScriptStruct* for struct arrays (Phase F)
    // Phase G layout metadata for TArray<TSoftObjectPtr/TSoftClassPtr>.
    // Lets the CE XML / CSX exporter emit per-element struct groups with
    // FName leaf(s) at the FSoftObjectPath sub-offset (+0x10) instead of
    // a single 8B WeakPtr-only hex. 0 means "not a soft array".
    //   FSoftObjectPtr layout per UE version:
    //     UE4 / UE5.0: { FWeakObjectPtr(8) + Tag(4) + pad(4) + FName(8|16) + FString(16) }
    //     UE5.1+:      { FWeakObjectPtr(8) + Tag(4) + pad(4) + FName(8|16) + FName(8|16) + FString(16) }
    int32_t     softArrayFNameSize = 0;          // 8 (normal) or 16 (CasePreservingName)
    bool        softArrayIsTopLevelAssetPath = false;  // true for UE >= 5.1 (FTopLevelAssetPath layout)
    uintptr_t   arrayEnumAddr = 0;        // UEnum* for CE DropDownList sharing key
    struct EnumEntry { int64_t value; std::string name; };
    std::vector<EnumEntry> arrayEnumEntries;  // Full UEnum entries for CE DropDownList

    // For ArrayProperty: base address of TArray::Data (for computing element addresses)
    uintptr_t   arrayDataAddr = 0;

    // For ArrayProperty Phase B/D/E/F: inline element values (up to 64)
    struct ArrayElement {
        int32_t     index = 0;
        std::string value;      // Human-readable typed value
        std::string hex;        // Per-element hex string
        std::string enumName;   // Only for enum arrays
        int64_t     rawIntValue = 0;   // Raw integer for CE DropDownList (enum value or FName index)
        // Phase D: pointer array fields
        uintptr_t   ptrAddr = 0;       // UObject* value (0 = null)
        std::string ptrName;           // Object name
        std::string ptrClassName;      // Class name
        // Phase F: struct array sub-fields
        struct StructSubField {
            std::string name;
            std::string typeName;
            int32_t     offset = 0;   // relative to element start
            int32_t     size = 0;
            std::string value;        // formatted scalar value
            // Pointer resolution for ObjectProperty/ClassProperty sub-fields
            uintptr_t   ptrAddr = 0;      // UObject* value (0 = null or non-pointer)
            std::string ptrName;          // Object name
            std::string ptrClassName;     // Class name
            uintptr_t   ptrClassAddr = 0; // UClass* for CSX drilldown
        };
        std::vector<StructSubField> structFields;
    };
    std::vector<ArrayElement> arrayElements;

    // For MapProperty: TMap header info
    int32_t     mapCount = -1;       // -1 = not a map; ≥0 = actual entry count
    std::string mapKeyType;          // Key FProperty type name (e.g. "StrProperty")
    std::string mapValueType;        // Value FProperty type name (e.g. "IntProperty")
    int32_t     mapKeySize = 0;      // Key element size in bytes
    int32_t     mapValueSize = 0;    // Value element size in bytes
    uintptr_t   mapDataAddr = 0;     // TSparseArray::Data base address
    uintptr_t   mapKeyStructAddr = 0;   // UScriptStruct* if key is StructProperty
    std::string mapKeyStructType;        // Struct name for key (e.g. "FVector")
    uintptr_t   mapValueStructAddr = 0; // UScriptStruct* if value is StructProperty
    std::string mapValueStructType;      // Struct name for value
    int32_t     mapValueOffset = 0;      // Aligned byte offset of value within TPair (may differ from mapKeySize)
    // The TSparseArray slot stride this walk ACTUALLY used to read the elements.
    // Published on the wire so the UI never recomputes it: the stride needs
    // alignof(Key)/alignof(Value), which do not cross the wire, and three
    // independent C# re-implementations of the formula silently went stale when
    // Macht::ComputeSetElementStride gained its elemAlign parameter (audit #5 V2).
    int32_t     mapStride = 0;           // 0 = unknown (element data was not read)

    // For SetProperty: TSet header info
    int32_t     setCount = -1;       // -1 = not a set; ≥0 = actual entry count
    std::string setElemType;         // Element FProperty type name
    int32_t     setElemSize = 0;     // Element size in bytes
    int32_t     setStride = 0;       // As mapStride, for TSet. 0 = unknown
    uintptr_t   setDataAddr = 0;     // TSparseArray::Data base address
    uintptr_t   setElemStructAddr = 0;  // UScriptStruct* if element is StructProperty
    std::string setElemStructType;       // Struct name for element

    // For Map/Set: inline element preview (shared structure)
    struct ContainerElement {
        int32_t     index = 0;
        std::string key;           // Map: formatted key; Set: formatted element
        std::string value;         // Map: formatted value; Set: unused
        std::string keyHex;
        std::string valueHex;
        // For pointer keys/values: name, address, and class name
        std::string keyPtrName;
        uintptr_t   keyPtrAddr = 0;
        std::string keyPtrClassName;
        std::string valuePtrName;
        uintptr_t   valuePtrAddr = 0;
        std::string valuePtrClassName;
    };
    std::vector<ContainerElement> containerElements;

    // For StructProperty: inner struct info
    uintptr_t   structDataAddr  = 0;  // Absolute address of struct data (instanceAddr + offset)
    uintptr_t   structClassAddr = 0;  // UScriptStruct* for the struct type
    std::string structTypeName;       // e.g. "FGameplayAttributeData"

    // For EnumProperty / ByteProperty-with-enum
    int64_t     enumValue = 0;      // Raw enum integer value
    std::string enumName;            // Resolved enum name (e.g., "ROLE_Authority")
    uintptr_t   enumAddr  = 0;      // UEnum* address for CE DropDownList (non-array enums)
    std::vector<EnumEntry> enumEntries;  // Full UEnum entries for CE DropDownList (non-array enums)

    // For StrProperty: decoded FString value
    std::string strValue;            // UTF-8 string from FString (wchar→UTF-8)

    // Guess What: true if this field was heuristically guessed (not from UE reflection)
    bool guessed = false;
};

// TWO different questions used to share one constant, and the shared answer was
// wrong about both. (SANEPROPS-2026-08-26)
//
// (1) WORK cap - "how many bytes will we sweep BYTE-WISE for one object?"
//     1 MB, unchanged. A recycled class pointer read 867,763,776 (~827 MB) on
//     Elliot (2026-06-27): one giant gap, ~8e8 per-byte SEH-faulting reads in
//     GuessGapTypes, and the single-threaded pipe worker wedged with the UI on
//     it. This is also a WIRE bound: GuessGapTypes emits one field row per
//     4-byte stride in the common case, so an N-byte gap becomes ~N/4 rows that
//     Fern serialises with no cap of its own.
inline constexpr int32_t kMaxGapFillBytes = 1 * 1024 * 1024;  // 1 MB

// (2) PLAUSIBILITY ceiling - "is this class pointer real, or recycled?"
//     Admission control for caches that are NEVER erased: s_walkClassExCache
//     hands out `const ClassInfo&` and is unbounded BY DESIGN (see its note in
//     Ubel.cpp), and Aura's container / ref caches emplace permanently past
//     their refusal checks. A refused class also fails to RETURN - WalkClassEx
//     hands back an empty ClassInfo - so this must stay a bound, never `>= 0`.
//
//     WARNING: 64 MB is an ADMISSION BOUND, not a claim about UE.
//     UStruct::PropertiesSize is an int32 accumulated by UStruct::Link and the
//     engine imposes no cap. The number sits far above every real class observed
//     and far below the observed garbage; the samples are recorded so the next
//     person can RE-DERIVE it rather than re-guess it:
//       * P3R  XRD777SaveGame  =       3,671,800  (walks cleanly, 2 fields)
//       * P3R  AstreaSaveGame  =       3,671,816  (ditto; both byte-stable 12 min)
//       * Elliot recycled ptr  =     867,763,776  (~827 MB, the wedge above)
//     64 MB is ~17x above the largest real sample and ~13x below the garbage one
//     (their geometric mean is ~56 MB). If a real class is ever seen above this,
//     RAISE it and add the sample here - do not delete it.
//
//     WARNING: the old value was 1 MB and its comment claimed "even the largest
//     generated Blueprint / UClass layouts are at most tens of KB". P3R refutes
//     that: the two SaveGame classes above were declared recycled, so WalkClassEx
//     returned EMPTY for them and they were structurally invisible to Value
//     Search, Group Scan, snapshot capture, CE export, Solitar and Solide - with
//     no log line at all.
inline constexpr int32_t kMaxPlausiblePropertiesSize = 64 * 1024 * 1024;  // 64 MB

// True when `propertiesSize` is a plausible UStruct::PropertiesSize: non-negative
// and within kMaxPlausiblePropertiesSize. A 0 is allowed (a field-less UCLASS is
// legitimate); a negative or wildly-large value means the pointer is recycled.
// Pure - unit-tested in dll_helpers_test so the bound is locked at build time.
inline bool IsPlausiblePropertiesSize(int32_t propertiesSize) {
    return propertiesSize >= 0 && propertiesSize <= kMaxPlausiblePropertiesSize;
}

// True when a completed WalkClass result may be MEMOIZED. The class caches are
// keyed by a raw UClass* the engine recycles and are never erased (see the B10
// note at WalkClass's try_emplace), so a single bad walk is served for the rest
// of the process — WalkInstance already owns the identical predicate but applies
// it AFTER the walk has been published, i.e. the one code path that knows the
// address is garbage has already poisoned the cache. Gate on the identity read
// instead of on the result:
//   * NEVER gate on Fields.empty() — InjectIntrinsicStructFields exists precisely
//     because an empty field list is a legitimate outcome (FDateTime/FTimespan and
//     every UCLASS that declares no own UPROPERTYs).
//   * NEVER gate on Name.empty() — Serie::GetString returns "" for an unresolved
//     obfuscated-fork tag key, so a name gate would disable class caching outright
//     on MindsEye-class forks.
// `propsSizeReadOk` is Macht::ReadSafe's discarded return: on failure it zeroes its
// out-param, and 0 is a *sane* PropertiesSize, so the size test alone cannot see an
// unmapped address. Pure — unit-tested in dll_helpers_test.
inline bool ShouldPublishClassWalk(bool propsSizeReadOk, int32_t propertiesSize) {
    return propsSizeReadOk && IsPlausiblePropertiesSize(propertiesSize);
}

// True when a UEnum member table may be MEMOIZED into the (never-erased) enum cache.
// The two failure modes look alike and must NOT be treated alike:
//   * BuildLayout FAILED (buildLayoutOk == false) -> publish. That is a COMPLETE
//     answer, not a truncated one: Neu rejects count == 0 / num <= 0, so a
//     legitimately member-less UEnum and any address that is not a UEnum both land
//     here, and refusing to cache them would re-probe on every single lookup.
//   * The entry loop BROKE mid-table (readCount < intendedCount) -> do not publish.
//     Caching a half-read table permanently splits one UEnum: values below the break
//     point resolve, values above render as raw integers, everywhere, with no retry.
// Pure — unit-tested in dll_helpers_test. (audit #5, found while fixing U4)
inline bool ShouldPublishEnumTable(bool buildLayoutOk, int32_t intendedCount, size_t readCount) {
    if (!buildLayoutOk) return true;
    return intendedCount >= 0 && readCount == static_cast<size_t>(intendedCount);
}

// Result of walking a live instance
struct InstanceWalkResult {
    uintptr_t   addr      = 0;
    std::string name;
    std::string className;
    uintptr_t   classAddr = 0;
    uintptr_t   outerAddr = 0;    // OuterPrivate — parent UObject
    std::string outerName;         // Name of the outer object
    std::string outerClassName;    // Class name of the outer object
    bool        isDefinition = false;  // True when viewing a class/struct definition (not a live instance)
    bool        isStale = false;   // True when classAddr looks recycled/garbage (implausible PropertiesSize)
    int32_t     propsSize = 0;     // UStruct::PropertiesSize — total struct/class size in bytes
    // The reflected walk RAN and its fields below are real; only the Guess-What
    // raw-byte pass was skipped, because PropertiesSize exceeds kMaxGapFillBytes.
    // NOT staleness — the two used to be the same verdict, which is how a live
    // 3.6 MB USaveGame was reported to the user as freed. (SANEPROPS-2026-08-26)
    bool        gapFillSkipped = false;
    std::vector<LiveFieldValue> fields;
};

// Walk a live instance: read class fields + live values from memory.
// If classAddr == 0, reads ClassPrivate from the instance itself.
// arrayLimit: max array element count for inline reading (default 64, max 16384).
// previewLimit: max sub-fields to show in StructProperty preview (0 = none, default 2, max 6).
InstanceWalkResult WalkInstance(uintptr_t instanceAddr, uintptr_t classAddr = 0, int32_t arrayLimit = 64, int32_t previewLimit = 2, bool fillGaps = false);

// ============================================================
// Unmanaged-region ("hole") computation — shared by the Guess-What gap pass
// (WalkInstance) and the Native-C value scan (see docs/native-c-value-scan-spec.md).
//
// A "hole" is a byte range inside an object's footprint that NO reflected
// property covers — where non-UPROPERTY native C++ members live. These are
// PURE helpers (no game-memory reads) so they are unit-testable without a
// process. Pure algorithm helpers in an existing namespace are exempt from the
// Frieren module-naming rule (same class as GraphPath.h under Aura::).
// ============================================================

// Half-open byte interval [start, end).
struct Interval { int32_t start; int32_t end; };

// Complement of `occupied` within the window [windowStart, windowEnd): the byte
// ranges no occupied interval covers. Each occupied interval is clamped to the
// window first (a field reaching below windowStart or above windowEnd is trimmed,
// not dropped — this is what lets the leading region [windowStart, firstField)
// survive as a hole), then sorted, merged, and the gaps between merged intervals
// (plus the trailing gap to windowEnd) are returned. Empty when fully covered or
// the window is empty. Pure.
inline std::vector<Interval> ComputeHoles(const std::vector<Interval>& occupied,
                                          int32_t windowStart, int32_t windowEnd) {
    std::vector<Interval> holes;
    if (windowStart >= windowEnd) return holes;

    std::vector<Interval> clamped;
    clamped.reserve(occupied.size());
    for (const auto& iv : occupied) {
        int32_t s = iv.start, e = iv.end;
        if (s < windowStart) s = windowStart;
        if (e > windowEnd)   e = windowEnd;
        if (s >= e) continue;                  // entirely outside the window
        clamped.push_back({s, e});
    }
    std::sort(clamped.begin(), clamped.end(),
        [](const Interval& a, const Interval& b) { return a.start < b.start; });

    std::vector<Interval> merged;
    for (const auto& iv : clamped) {
        if (!merged.empty() && iv.start <= merged.back().end)
            merged.back().end = (std::max)(merged.back().end, iv.end);
        else
            merged.push_back(iv);
    }

    int32_t cursor = windowStart;
    for (const auto& iv : merged) {
        if (iv.start > cursor) holes.push_back({cursor, iv.start});
        cursor = (std::max)(cursor, iv.end);
    }
    if (cursor < windowEnd) holes.push_back({cursor, windowEnd});
    return holes;
}

// Class-level convenience: build the occupied set from a walked class's reflected
// fields (footprint = Offset + Size * ArrayDim, so a static C-array Type Foo[N]
// occupies all N elements, not just the first) and return the holes within
// [windowStart, windowEnd). The caller derives the window (typically
// [UObject header end, class PropertiesSize)). Pure (ClassInfo is already walked).
inline std::vector<Interval> ComputeClassHoles(const ClassInfo& ci,
                                               int32_t windowStart, int32_t windowEnd) {
    std::vector<Interval> occupied;
    occupied.reserve(ci.Fields.size());
    for (const auto& f : ci.Fields) {
        int32_t elemSize = (f.Size > 0) ? f.Size : 1;
        int32_t dim      = (f.ArrayDim > 0) ? f.ArrayDim : 1;
        // Guard against a garbage-huge product overflowing int32.
        int64_t span = static_cast<int64_t>(elemSize) * dim;
        if (span > 0x7FFFFFF0) span = 0x7FFFFFF0;
        occupied.push_back({ f.Offset, static_cast<int32_t>(f.Offset + span) });
    }
    return ComputeHoles(occupied, windowStart, windowEnd);
}

// Normalize a "Guess What" type label (GuessGapTypes emits confidence-suffixed
// names like "Float?", and non-numeric "Padding"/"Pointer?") to the CANONICAL
// UE property-type string the rest of the stack requires — Radar::
// TryDataTypeFromPropertyTypeName (DLL) and SnapshotNumeric.TryFromHex (C#) both
// switch on EXACT "FloatProperty"/"IntProperty"/... strings with a default-reject.
// Returns "" for labels that have no gameplay-numeric meaning (Padding / Pointer),
// signalling the caller to DROP the row. The human-readable guessed label is
// carried separately (never in the property-type field). Pure.
// === Byte-blind struct preview (audit U3, build 3169) ===
//
// LAST RESORT ONLY. When a caller cannot resolve the UScriptStruct* it can ask
// for a hint decoded from raw bytes. That is a guess by construction and the
// reflected-layout decoder (Ubel.cpp WalkInstance's `{Name=Value}` preview) is
// always preferable — see the remaining half of U3 in the register.
//
// What this fixes: the old branch skipped 8 bytes unconditionally whenever
// size > 8, on the theory that structs begin with a vtable. That is true of
// FGameplayAttributeData (GAS declares a virtual destructor, so BaseValue /
// CurrentValue really do sit at +8/+0xC — which is why the heuristic was
// written and why it survived) and FALSE of nearly every other USTRUCT. The
// cost was a SILENT DROP of leading members, which is worse than garbage
// because it looks right:
//   * FVector3f, 12 B, 3 floats  -> printed ONE number, the LAST component
//     (live-confirmed on Map_IntToVec3f, todo.md)
//   * FLinearColor, 16 B, 4 floats -> R and G vanish, only B and A print
//
// So the skip is now gated on EVIDENCE rather than on size. `size > 8` is not
// evidence of anything; a pointer-shaped first 8 bytes is.
//
// ⚠ KNOWN REMAINING GAP, deliberately not papered over: a 24-byte struct is
// 3 doubles (UE5 LWC FVector) or 6 floats, and the bytes cannot say which —
// `Radar.h` states this repo rule outright ("the STRUCT NAME does not
// determine the width and neither does the engine version"). This function
// still reads 4-byte floats, so an LWC vector still decodes wrongly. Only the
// reflected layout can settle it. Swapping one guess for another would be the
// same defect with different numbers.
//
// Pure: bytes in, string out. Lives here so dll_helpers_test can pin it — no
// target compiles Ubel.cpp.

// === Reflected struct preview (audit U17, build 3171) ===
//
// The layout half of U3. The byte-blind decoder below cannot tell 3 doubles from
// 6 floats — Radar.h states the rule: the struct name does not determine the
// width and neither does the engine version. The only thing that CAN settle it is
// the reflected field list, and every call site that matters already holds the
// UScriptStruct*. These two are the pure half of that decode, so the width
// handling — which is exactly where the LWC bug lives — is unit-pinnable.

/// Format a number for a preview cell. `%g` flips to scientific past a few digits
/// (18328.64 -> "1.833e+04"), unreadable in a value column; `%f` with trailing
/// zeros trimmed reads correctly over the whole int32/int64-ish range, falling
/// back to `%g` only where a wall of digits would be worse.
inline std::string FormatPreviewNumber(double v) {
    if (v == 0.0) return "0";
    double a = (v < 0.0) ? -v : v;
    char buf[64];
    if (a >= 1e16 || a < 1e-6) {
        snprintf(buf, sizeof(buf), "%.6g", v);   // out of human range -> %g
        return buf;
    }
    snprintf(buf, sizeof(buf), "%.4f", v);
    std::string s(buf);
    size_t dot = s.find('.');
    if (dot != std::string::npos) {
        size_t last = s.find_last_not_of('0');
        if (last == dot) last--;                 // "2245." -> "2245"
        s.erase(last + 1);
    }
    return s;
}

/// Decode ONE reflected scalar at `p`, using the property's OWN declared width.
/// This is the whole point of U17: a DoubleProperty is read as 8 bytes and a
/// FloatProperty as 4, so an LWC FVector yields its three real components instead
/// of six float halves, with no guessing anywhere.
///
/// Returns "" for properties that need process state (Name / Object / Class) or
/// are not previewable — the caller supplies those, which is what keeps this pure.
inline std::string PreviewScalarValue(const std::string& typeName,
                                      const uint8_t* p, int32_t size) {
    if (!p || size <= 0) return "";
    if (typeName == "FloatProperty"  && size == 4) { float  v; memcpy(&v, p, 4); return FormatPreviewNumber(v); }
    if (typeName == "DoubleProperty" && size == 8) { double v; memcpy(&v, p, 8); return FormatPreviewNumber(v); }
    if (typeName == "IntProperty"    && size == 4) { int32_t v; memcpy(&v, p, 4); return std::to_string(v); }
    if (typeName == "UInt32Property" && size == 4) { uint32_t v; memcpy(&v, p, 4); return std::to_string(v); }
    if (typeName == "Int64Property"  && size == 8) { int64_t v; memcpy(&v, p, 8); return std::to_string(v); }
    if (typeName == "UInt64Property" && size == 8) { uint64_t v; memcpy(&v, p, 8); return std::to_string(v); }
    if (typeName == "Int16Property"  && size == 2) { int16_t v; memcpy(&v, p, 2); return std::to_string(v); }
    if (typeName == "UInt16Property" && size == 2) { uint16_t v; memcpy(&v, p, 2); return std::to_string(v); }
    if (typeName == "BoolProperty")                 return p[0] ? "true" : "false";
    if (typeName == "ByteProperty" || typeName == "Int8Property") return std::to_string(p[0]);
    return "";   // needs process state, or not previewable
}

/// True when the first 8 bytes look like a real vtable pointer: non-null,
/// 8-byte aligned, and inside the x64 user-mode canonical range. A float pair
/// (0x400000003F800000), a double (0x40934A0000000000) and an FLinearColor's
/// first two components all fail it; a module address (0x00007FF6...) passes.
inline bool LooksLikeVtablePointer(const uint8_t* bytes, int32_t size) {
    if (!bytes || size < 8) return false;
    uint64_t v = 0;
    memcpy(&v, bytes, 8);
    if (v < 0x10000ULL) return false;                 // null / small integer
    if (v > 0x00007FFFFFFFFFFFULL) return false;      // above user-mode canonical
    return (v & 7ULL) == 0;                           // vtable pointers are aligned
}

/// Decode `size` bytes as consecutive 4-byte floats, skipping a leading vtable
/// pointer ONLY when one is actually present. Returns "" when nothing
/// meaningful decodes, so the caller falls back to hex.
inline std::string InterpretStructBytes(const uint8_t* bytes, int32_t size) {
    if (!bytes || size < 4) return "";
    const int floatStart = LooksLikeVtablePointer(bytes, size) ? 8 : 0;
    const int floatCount = (size - floatStart) / 4;
    if (floatCount <= 0 || floatCount > 16) return "";

    bool anyMeaningful = false;
    for (int i = 0; i < floatCount; ++i) {
        float v;
        memcpy(&v, bytes + floatStart + i * 4, 4);
        if (v != 0.0f && v == v && v > -1e12f && v < 1e12f) { anyMeaningful = true; break; }
    }
    if (!anyMeaningful) return "";

    std::string hint = "f:[";
    for (int i = 0; i < floatCount; ++i) {
        if (i > 0) hint += ", ";
        float v;
        memcpy(&v, bytes + floatStart + i * 4, 4);
        char buf[48];
        snprintf(buf, sizeof(buf), "%.4f", v);   // no scientific notation
        hint += buf;
    }
    hint += "]";
    return hint;
}

inline std::string NormalizeGuessedTypeToProperty(const std::string& guessedType) {
    if (guessedType == "Float"  || guessedType == "Float?")  return "FloatProperty";
    if (guessedType == "Double" || guessedType == "Double?") return "DoubleProperty";
    if (guessedType == "Int32"  || guessedType == "Int32?")  return "IntProperty";
    if (guessedType == "Int16"  || guessedType == "Int16?")  return "Int16Property";
    if (guessedType == "Byte"   || guessedType == "Byte?")   return "ByteProperty";
    if (guessedType == "Int64"  || guessedType == "Int64?")  return "Int64Property";
    return "";  // Padding / Pointer? / unknown -> drop
}

// === Enum raw-value read (audit #5 U9) ===
//
// Read a UEnum member's raw integer value from `size` bytes at `p`. UE stores every
// enum member value as int64 in UEnum::Names, and the standard UHT-generated sentinel
// for `enum class : uint8` is `MAX = 255`. A byte-width enum (size 1) MUST therefore be
// read UNSIGNED: read through int8_t it sign-extends 255 -> -1 (and any enumerator
// >= 128 similarly), which never matches the table, so the member renders as a raw
// negative integer instead of its name. Wider underlying widths (2/4/8) keep their
// natural signedness, matching the array-enum sibling (Ubel.cpp ReadArrayElements) so
// both enum read paths agree. Pure — pinned in dll_helpers_test.
inline int64_t ReadEnumRawValue(const uint8_t* p, int32_t size) {
    if (!p) return 0;
    switch (size) {
        case 1: { uint8_t  v; std::memcpy(&v, p, 1); return v; }   // UNSIGNED (U9)
        case 2: { int16_t  v; std::memcpy(&v, p, 2); return v; }
        case 4: { int32_t  v; std::memcpy(&v, p, 4); return v; }
        case 8: { int64_t  v; std::memcpy(&v, p, 8); return v; }
        default: return 0;
    }
}

// === FString / FUtf8String length cap (audit #5 U10) ===
//
// The largest character count ReadFString / ReadFUtf8String will accept. The cap's
// real job is to bound the allocation against a GARBAGE Count read out of freed memory
// (a corrupt ~2^31 Count would otherwise try to allocate ~4 GB and throw). It is NOT a
// display-length limit: the buffer read is `Count * elemWidth` — the ACTUAL string
// length — so raising the cap costs nothing for the common short string and only lets a
// genuinely long one (a 400-char description that used to render as "(empty)") resolve.
// 8192 matches the FText path's own gate (TryDecodeFStringAt: num > 8192) so the two
// string decoders agree on "implausibly long". Pure — pinned in dll_helpers_test.
inline constexpr int32_t kMaxFStringChars = 8192;

// True when `count` is a plausible in-object FString/FUtf8String character count:
// strictly positive and within kMaxFStringChars. Rejects the 0/negative empty case and
// a garbage-huge Count in one predicate, so both readers share one rule.
inline bool IsPlausibleStringCount(int32_t count) {
    return count > 0 && count <= kMaxFStringChars;
}

// Heuristically guess field types for a raw byte gap [gapStart, gapEnd) of the
// object at baseAddr, appending one LiveFieldValue per guessed slot (each with
// .guessed = true). Reads memory (SEH-safe via Macht). The "Guess What" engine;
// promoted from a file-local static so the Native-C scan can reuse it. Emits
// confidence-suffixed labels (Float / Float? / Int32? / Padding / Pointer? / ...);
// callers feeding Snapshot / Value Search pass each .typeName through
// NormalizeGuessedTypeToProperty.
void GuessGapTypes(uintptr_t baseAddr, int32_t gapStart, int32_t gapEnd,
                   std::vector<LiveFieldValue>& outFields);

// --- DataTable Row Browsing ---

// A single row from a DataTable's RowMap
struct DataTableRow {
    int32_t     sparseIndex  = 0;   // TSparseArray element index
    std::string rowName;             // FName key resolved to string
    uintptr_t   rowDataAddr  = 0;   // Dereferenced uint8* (row data buffer)
    std::vector<LiveFieldValue> fields;  // Row fields using RowStruct layout
};

// Result of walking DataTable rows
struct DataTableWalkResult {
    bool        ok           = false;
    std::string error;
    int32_t     rowCount     = 0;   // Total rows in RowMap
    int32_t     rowMapOffset = 0;   // Detected RowMap offset within DataTable
    uintptr_t   rowStructAddr = 0;  // UScriptStruct* for row layout
    std::string rowStructName;       // Name of the row struct type
    int32_t     fnameSize    = 0;   // FName size (8 or 16) for pointer chain
    int32_t     stride       = 0;   // TMap element stride for pointer chain
    std::vector<DataTableRow> rows;
};

// Walk DataTable rows: probe for RowMap, iterate entries, read row field values.
// offset: starting row index (for pagination), limit: max rows to return.
DataTableWalkResult WalkDataTableRows(uintptr_t dataTableAddr, int32_t offset = 0, int32_t limit = 64);

// Interpret raw bytes as a typed value string based on the field type name
// Decode a struct through its REFLECTED layout — the correct answer, and the one
// every caller holding a UScriptStruct* should use. Emits "{Name=Value, ...}".
// Returns "" when nothing is previewable (or, for InterpretStructAt, when the
// layout cannot be resolved) so the caller can fall back to hex. Audit U17.
std::string InterpretStructByLayout(const uint8_t* buf, int32_t size,
                                    const ClassInfo& si, int previewLimit);
std::string InterpretStructAt(const uint8_t* buf, int32_t size,
                              uintptr_t structClassAddr, int previewLimit);

std::string InterpretValue(const std::string& typeName, const void* data, int32_t size);

// --- Array Element Reading (Phase B) ---

// Result of reading array elements
struct ReadArrayResult {
    bool        ok = false;
    std::string error;
    int32_t     totalCount = 0;
    int32_t     readCount  = 0;
    uintptr_t   enumAddr = 0;  // UEnum* address (for CE DropDownList sharing)
    std::vector<LiveFieldValue::ArrayElement> elements;
};

// Check if an inner type name is a scalar type whose elements can be read inline
bool IsScalarArrayType(const std::string& innerTypeName);

// Read scalar elements from a TArray.
// instanceAddr: base address of the UObject instance
// fieldOffset:  byte offset of the TArray field within the instance
// innerFFieldAddr: Inner FProperty* (needed for enum resolution)
// innerTypeName: e.g. "FloatProperty", "EnumProperty"
// elemSize: bytes per element
// offset/limit: pagination (element index range)
ReadArrayResult ReadArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    uintptr_t innerFFieldAddr, const std::string& innerTypeName,
    int32_t elemSize, int32_t offset = 0, int32_t limit = 64);

// Phase D: check if inner type is a pointer type (ObjectProperty, ClassProperty)
bool IsPointerArrayType(const std::string& innerTypeName);

// Phase D: read pointer elements from a TArray of UObject pointers.
// For each element, reads the pointer, then resolves name + class name.
ReadArrayResult ReadPointerArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset = 0, int32_t limit = 64);

// Phase E: resolve FWeakObjectPtr { int32 ObjectIndex, int32 SerialNumber }
// Returns the UObject* if valid (serial matches), or 0 if stale/invalid.
uintptr_t ResolveWeakObjectPtr(int32_t objectIndex, int32_t serialNumber);

// Phase E: check if inner type is a weak-pointer type
bool IsWeakPointerArrayType(const std::string& innerTypeName);

// Phase E: read weak object pointer elements from a TArray of FWeakObjectPtr.
ReadArrayResult ReadWeakObjectArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset = 0, int32_t limit = 64);

// Get all cached enum entries for a UEnum address (for CE DropDownList).
// Triggers cache population if not yet cached.
std::vector<LiveFieldValue::EnumEntry> GetEnumEntries(uintptr_t enumAddr);

// Phase F: check if inner type is a struct type
bool IsStructArrayType(const std::string& innerTypeName);

// Phase F: read struct array elements, expanding each element's scalar fields.
// innerStructAddr: UScriptStruct* for the inner element type.
ReadArrayResult ReadStructArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    uintptr_t innerStructAddr, int32_t elemSize,
    int32_t offset = 0, int32_t limit = 64);

// Phase G: TSoftObjectPtr / TSoftClassPtr arrays — resolves FSoftObjectPath
// asset name and embedded FWeakObjectPtr (when loaded) per element.
bool IsSoftObjectArrayType(const std::string& innerTypeName);
ReadArrayResult ReadSoftObjectArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset = 0, int32_t limit = 64);

// Phase H: TLazyObjectPtr arrays — resolves FGuid + embedded FWeakObjectPtr.
bool IsLazyObjectArrayType(const std::string& innerTypeName);
ReadArrayResult ReadLazyObjectArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset = 0, int32_t limit = 64);

// Phase I: TScriptInterface arrays — exposes underlying UObject* + iface ptr.
bool IsInterfaceArrayType(const std::string& innerTypeName);
ReadArrayResult ReadInterfaceArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset = 0, int32_t limit = 64);

// Phase J: TArray<FScriptDelegate> — resolves bound UObject* + FName.
// Stride derives from CasePreservingName: 16 (8B FName) or 24 (16B FName).
bool IsDelegateArrayType(const std::string& innerTypeName);
ReadArrayResult ReadDelegateArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset = 0, int32_t limit = 64);

// Phase K: TArray<FMulticastScriptDelegate> — preview only (each element is
// a TArray<FScriptDelegate>; no per-binding drill-down at this depth).
bool IsMulticastDelegateArrayType(const std::string& innerTypeName);
ReadArrayResult ReadMulticastDelegateArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset = 0, int32_t limit = 64);

} // namespace Ubel

// --- Property Search Preview Resolution ---
// (Outside UStructWalker namespace because PropertyMatch is in ObjectArray namespace)

namespace Aura { struct PropertyMatch; }

namespace Ubel {

// Resolve inline value previews for property search results.
// For each PropertyMatch, reads the property value from a representative
// instance of its class and stores the preview string in match.preview.
// instanceMap: classAddr -> instanceAddr (one representative instance per class)
void ResolvePropertyPreviews(
    std::vector<Aura::PropertyMatch>& matches,
    const std::unordered_map<uintptr_t, uintptr_t>& instanceMap);

} // namespace Ubel
