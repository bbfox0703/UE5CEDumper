#pragma once

// ============================================================
// Ubel — 尤蓓爾 (外科式暗殺者 — Surgical Assassin)
// UStructWalker: FField chain traversal and property reading
// ============================================================

#include <cstdint>
#include <string>
#include <unordered_map>
#include <vector>

struct FieldInfo {
    uintptr_t   Address;        // FField* address
    std::string Name;
    std::string TypeName;
    int32_t     Offset;
    int32_t     Size;
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
ClassInfo WalkClassEx(uintptr_t uclassAddr);

// Resolve the per-element size of an ArrayProperty's Inner FProperty.
// Returns 0 if the size cannot be determined (rare: unknown inner type
// without a usable FPROPERTY_ELEMSIZE or PropertiesSize).
// Used by container-aware Address Finder to compute element index.
int32_t GetArrayInnerElemSize(uintptr_t fieldAddr);

// Per-element stride within an FSetProperty's TSparseArray.Data buffer.
// Returns 0 when the inner element size is undetermined.
int32_t GetSetElementStride(uintptr_t fieldAddr);

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
};
bool GetMapPairLayout(uintptr_t fieldAddr, MapPairLayout& out);

// Walk all UFunctions of a UClass.
// Iterates the function chain, resolving parameters and return type.
std::vector<FunctionInfo> WalkFunctions(uintptr_t uclassAddr);

// Get the UClass* of a UObject
uintptr_t GetClass(uintptr_t uobjectAddr);

// Get the Outer object of a UObject
uintptr_t GetOuter(uintptr_t uobjectAddr);

// Get the FName-based name of a UObject
std::string GetName(uintptr_t uobjectAddr);

// Get the full path name (e.g., /Game/BP_Player.BP_Player_C)
std::string GetFullName(uintptr_t uobjectAddr);

// Get the internal index of a UObject
int32_t GetIndex(uintptr_t uobjectAddr);

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

    // For SetProperty: TSet header info
    int32_t     setCount = -1;       // -1 = not a set; ≥0 = actual entry count
    std::string setElemType;         // Element FProperty type name
    int32_t     setElemSize = 0;     // Element size in bytes
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
    int32_t     propsSize = 0;     // UStruct::PropertiesSize — total struct/class size in bytes
    std::vector<LiveFieldValue> fields;
};

// Walk a live instance: read class fields + live values from memory.
// If classAddr == 0, reads ClassPrivate from the instance itself.
// arrayLimit: max array element count for inline reading (default 64, max 16384).
// previewLimit: max sub-fields to show in StructProperty preview (0 = none, default 2, max 6).
InstanceWalkResult WalkInstance(uintptr_t instanceAddr, uintptr_t classAddr = 0, int32_t arrayLimit = 64, int32_t previewLimit = 2, bool fillGaps = false);

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
