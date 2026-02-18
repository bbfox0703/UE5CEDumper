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

} // namespace UStructWalker
