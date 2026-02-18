#pragma once

// ============================================================
// OffsetFinder.h — GObjects / GNames / GWorld pattern scan
// ============================================================

#include <cstdint>

namespace OffsetFinder {

struct EnginePointers {
    uintptr_t GObjects  = 0;   // FUObjectArray*
    uintptr_t GNames    = 0;   // FNamePool*
    uintptr_t GWorld    = 0;   // UWorld**
    uint32_t  UEVersion = 0;   // e.g. 500, 501, 503, 504
};

// Scan and cache all global pointers
// Returns false on failure, error details logged
bool FindAll(EnginePointers& out);

// Find GObjects (FUObjectArray) address
uintptr_t FindGObjects();

// Find GNames (FNamePool) address
uintptr_t FindGNames();

// Find GWorld pointer address
uintptr_t FindGWorld();

// Detect UE version from memory or PE resources
uint32_t DetectVersion();

} // namespace OffsetFinder
