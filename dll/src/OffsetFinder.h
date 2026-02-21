#pragma once

// ============================================================
// OffsetFinder.h — GObjects / GNames / GWorld pattern scan
// ============================================================

#include <cstdint>

namespace OffsetFinder {

struct EnginePointers {
    uintptr_t GObjects  = 0;   // FUObjectArray*
    uintptr_t GNames    = 0;   // FNamePool* or TNameEntryArray*
    uintptr_t GWorld    = 0;   // UWorld**
    uint32_t  UEVersion = 0;   // e.g. 500, 501, 503, 504, 427, 422
    bool      bUE4NameArray = false;   // true = TNameEntryArray (UE4 <4.23), false = FNamePool
    int       ue4StringOffset = 0x10;  // FNameEntry string offset for UE4 mode
    int       fnameEntryHeaderOffset = 0; // Offset to 2-byte header within FNameEntry (0=standard, 4=hash-prefixed UE4.26)
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

// Dynamically detect and fix FField/FProperty/UStruct offsets.
// Must be called AFTER GObjects + GNames are initialized (ObjectArray::Init + FNamePool::Init).
// ueVersion: detected UE version (e.g. 505 = UE5.5, 427 = UE4.27). Used to determine
// UProperty vs FProperty mode. Pass 0 if unknown (will fall back to heuristic detection).
// Updates DynOff:: namespace variables.
bool ValidateAndFixOffsets(uint32_t ueVersion);

} // namespace OffsetFinder
