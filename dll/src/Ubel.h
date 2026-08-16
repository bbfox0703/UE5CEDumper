#pragma once

// ============================================================
// Ubel — 尤蓓爾 (外科式暗殺者 — Surgical Assassin)
// UStructWalker: FField chain traversal and property reading
// ============================================================

#include <algorithm>
#include <cstdint>
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
// Returns "" on failure or out-of-range length. Caps at 256 wide chars
// per the existing ReadFString implementation — the cap protects
// against garbage Count values from freed memory; cheat-relevant
// strings (item names, save keys) fit well under it.
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

// A real UStruct::PropertiesSize is bounded — even the largest generated
// Blueprint / UClass layouts are at most tens of KB. A value beyond this means
// the class pointer is recycled/garbage: the instance was freed and its memory
// slot reused (e.g. a level transition / GC that ran while the user was away in
// another panel, then returned to Live Walker on the stale address). Used to
//   (a) reject such stale objects in WalkInstance, and
//   (b) cap the Guess-What gap-fill so a bogus multi-hundred-MB PropertiesSize
//       can't become one giant gap that wedges the single-threaded pipe worker
//       (827 MB observed → ~8e8 per-byte SEH-faulting reads = effective hang).
inline constexpr int32_t kMaxSanePropertiesSize = 1 * 1024 * 1024;  // 1 MB

// True when `propertiesSize` is a plausible UStruct::PropertiesSize: non-negative
// and within kMaxSanePropertiesSize. A 0 is allowed (unusual but not garbage); a
// negative or wildly-large value means the class pointer is recycled/garbage.
// Pure — unit-tested in dll_helpers_test so the bound is locked at build time.
inline bool IsSanePropertiesSize(int32_t propertiesSize) {
    return propertiesSize >= 0 && propertiesSize <= kMaxSanePropertiesSize;
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
    return propsSizeReadOk && IsSanePropertiesSize(propertiesSize);
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
inline std::string NormalizeGuessedTypeToProperty(const std::string& guessedType) {
    if (guessedType == "Float"  || guessedType == "Float?")  return "FloatProperty";
    if (guessedType == "Double" || guessedType == "Double?") return "DoubleProperty";
    if (guessedType == "Int32"  || guessedType == "Int32?")  return "IntProperty";
    if (guessedType == "Int16"  || guessedType == "Int16?")  return "Int16Property";
    if (guessedType == "Byte"   || guessedType == "Byte?")   return "ByteProperty";
    if (guessedType == "Int64"  || guessedType == "Int64?")  return "Int64Property";
    return "";  // Padding / Pointer? / unknown -> drop
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
