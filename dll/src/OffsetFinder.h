#pragma once

// ============================================================
// OffsetFinder.h — GObjects / GNames / GWorld pattern scan
// ============================================================

#include <cstdint>
#include <functional>

namespace OffsetFinder {

// Callback for reporting scan progress (phase 0-7, status text).
// Phase: 0=idle, 1=version, 2=GObjects, 3=GNames, 4=GWorld, 5=init, 6=dynoff, 7=complete
using ScanProgressFn = std::function<void(int phase, const char* text)>;

struct EnginePointers {
    uintptr_t GObjects  = 0;   // FUObjectArray*
    uintptr_t GNames    = 0;   // FNamePool* or TNameEntryArray*
    uintptr_t GWorld    = 0;   // UWorld**
    uint32_t  UEVersion = 0;   // e.g. 500, 501, 503, 504, 427, 422
    bool      bUE4NameArray = false;   // true = TNameEntryArray (UE4 <4.23), false = FNamePool
    bool      bVersionDetected = true; // false = PE/memory scan failed, version is inferred or default
    int       ue4StringOffset = 0x10;  // FNameEntry string offset for UE4 mode
    int       fnameEntryHeaderOffset = 0; // Offset to 2-byte header within FNameEntry (0=standard, 4=hash-prefixed UE4.26)

    // Scan method for each pointer: "aob", "data_scan", "string_ref", "pointer_scan", "not_found"
    const char* gobjectsMethod = "not_found";
    const char* gnamesMethod   = "not_found";
    const char* gworldMethod   = "not_found";

    // --- AOB Usage Tracking ---
    // PE hash: TimeDateStamp (8 hex) + SizeOfImage (8 hex) = unique game build ID
    char peHash[17] = {0};

    // Winning pattern IDs (point to AobSignature::id constexpr strings in Signatures.h)
    const char* gobjectsPatternId = nullptr;
    const char* gnamesPatternId   = nullptr;
    const char* gworldPatternId   = nullptr;

    // AOB scan hit addresses (instruction address where the winning pattern matched)
    uintptr_t gobjectsScanAddr = 0;
    uintptr_t gnamesScanAddr   = 0;
    uintptr_t gworldScanAddr   = 0;

    // Per-target scan statistics
    int gobjectsPatternsTried = 0;
    int gobjectsPatternsHit   = 0;
    int gnamesPatternsTried   = 0;
    int gnamesPatternsHit     = 0;
    int gworldPatternsTried   = 0;
    int gworldPatternsHit     = 0;

    // GWorld winning pattern AOB metadata (for CreateSymbolScript)
    const char* gworldAob    = nullptr;  // AOB pattern string (e.g. "48 8B 1D ?? ?? ?? ??")
    int         gworldAobPos = 0;        // instrOffset + opcodeLen: displacement offset within match
    int         gworldAobLen = 0;        // instrOffset + totalLen: instruction end for RIP calculation
};

// Scan and cache all global pointers
// Returns false on failure, error details logged
// progress: optional callback for UI progress reporting (phase 0-7)
bool FindAll(EnginePointers& out, ScanProgressFn progress = nullptr);

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

// Lazy-detect UEnum::Names offset by probing known enums (ENetRole, etc.) in GObjects.
// Called on first EnumProperty encounter, NOT during init.
// Sets DynOff::UENUM_NAMES and DynOff::bUEnumNamesDetected on success.
bool DetectUEnumNames();

// === Extra Scan: user-triggered aggressive fallback techniques ===
// These are computationally expensive (seconds, not milliseconds) and are designed
// to be called from a background thread.  They are READ-ONLY — no global state is
// modified.  The caller is responsible for applying results (ObjectArray::Init, etc.)
// on the pipe thread.

// Scan .data section for FUObjectArray by validating structure heuristics.
// Complements FindGObjectsByDataScan (which follows code references instead).
uintptr_t ExtraScanGObjects();

// Find GWorld by iterating GObjects for UWorld instance, then scanning .data
// for a static pointer to that instance.  Requires GObjects + GNames already initialized.
uintptr_t ExtraScanGWorld();

} // namespace OffsetFinder
