#pragma once

// ============================================================
// UStructWalker.h — UStruct / FField chain traversal
// ============================================================

#include <cstdint>
#include <string>
#include <vector>

struct FieldInfo {
    uintptr_t   Address;        // FField* address
    std::string Name;
    std::string TypeName;
    int32_t     Offset;
    int32_t     Size;
    uint64_t    PropertyFlags;
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

namespace UStructWalker {

// Walk a UClass/UStruct and enumerate all fields (including inherited)
ClassInfo WalkClass(uintptr_t uclassAddr);

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
    uintptr_t   ptrValue  = 0;
    std::string ptrName;       // Name of the pointed-to object
    std::string ptrClassName;  // Class name of the pointed-to object

    // For BoolProperty: bit field info
    int32_t     boolBitIndex = -1;  // Bit index (0-7) within the byte; -1 = not a bool
    uint8_t     boolFieldMask = 0;  // Raw FieldMask byte from FBoolProperty
    uint8_t     boolByteOffset = 0; // ByteOffset within the property offset

    // For ArrayProperty: TArray header info
    int32_t     arrayCount = -1;  // -1 = not an array

    // For StructProperty: inner struct info
    uintptr_t   structDataAddr  = 0;  // Absolute address of struct data (instanceAddr + offset)
    uintptr_t   structClassAddr = 0;  // UScriptStruct* for the struct type
    std::string structTypeName;       // e.g. "FGameplayAttributeData"

    // For EnumProperty / ByteProperty-with-enum
    int64_t     enumValue = 0;      // Raw enum integer value
    std::string enumName;            // Resolved enum name (e.g., "ROLE_Authority")

    // For StrProperty: decoded FString value
    std::string strValue;            // UTF-8 string from FString (wchar→UTF-8)
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
    std::vector<LiveFieldValue> fields;
};

// Walk a live instance: read class fields + live values from memory.
// If classAddr == 0, reads ClassPrivate from the instance itself.
InstanceWalkResult WalkInstance(uintptr_t instanceAddr, uintptr_t classAddr = 0);

// Interpret raw bytes as a typed value string based on the field type name
std::string InterpretValue(const std::string& typeName, const void* data, int32_t size);

} // namespace UStructWalker
