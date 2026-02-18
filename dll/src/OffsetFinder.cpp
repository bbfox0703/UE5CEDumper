// ============================================================
// OffsetFinder.cpp — AOB scanning for GObjects/GNames/GWorld
// ============================================================

#include "OffsetFinder.h"
#include "Memory.h"
#include "Logger.h"
#include "Constants.h"

#include <string>
#include <cstring>

namespace OffsetFinder {

// Try a single AOB pattern and resolve RIP-relative address
static uintptr_t TryPatternRIP(const char* pattern, int opcodeLen = 3, int totalLen = 7, bool derefResult = false) {
    uintptr_t addr = Mem::AOBScan(pattern);
    if (!addr) return 0;

    uintptr_t target = Mem::ResolveRIP(addr, opcodeLen, totalLen);
    if (!target) return 0;

    if (derefResult) {
        uintptr_t value = 0;
        if (!Mem::ReadSafe(target, value) || value == 0) {
            LOG_WARN("TryPatternRIP: Deref at 0x%llX yielded null",
                     static_cast<unsigned long long>(target));
            return 0;
        }
        return value;
    }

    return target;
}

// Validate a candidate GObjects address
static bool ValidateGObjects(uintptr_t addr) {
    if (!addr) return false;

    // Read NumElements (at offset 0x14 in default layout)
    int32_t numElements = 0;
    if (!Mem::ReadSafe(addr + 0x14, numElements)) return false;

    // Sanity: should have a reasonable number of objects
    if (numElements < 0x1000 || numElements > 0x400000) {
        LOG_WARN("ValidateGObjects: NumElements=%d out of range at 0x%llX",
                 numElements, static_cast<unsigned long long>(addr));
        return false;
    }

    LOG_INFO("ValidateGObjects: Valid at 0x%llX, NumElements=%d",
             static_cast<unsigned long long>(addr), numElements);
    return true;
}

uintptr_t FindGObjects() {
    LOG_INFO("FindGObjects: Scanning for GObjects...");

    // Try primary pattern (mov rax, [rip+rel32]; mov rcx, [rax+rcx*8])
    // This pattern points to a pointer that holds the FUObjectArray address
    uintptr_t result = TryPatternRIP(Constants::AOB_GOBJECTS_PRIMARY, 3, 7, false);
    if (result && ValidateGObjects(result)) return result;

    // Fallback: deref one more level
    result = TryPatternRIP(Constants::AOB_GOBJECTS_PRIMARY, 3, 7, true);
    if (result && ValidateGObjects(result)) return result;

    // Fallback pattern 1
    result = TryPatternRIP(Constants::AOB_GOBJECTS_FALLBACK1, 3, 7, false);
    if (result && ValidateGObjects(result)) return result;

    // Fallback pattern 2
    result = TryPatternRIP(Constants::AOB_GOBJECTS_FALLBACK2, 3, 7, false);
    if (result && ValidateGObjects(result)) return result;

    LOG_ERROR("FindGObjects: All patterns failed");
    return 0;
}

// Validate GNames by checking that known FName indices resolve to valid strings
static bool ValidateGNames(uintptr_t addr) {
    if (!addr) return false;

    // Check that chunk[0] pointer is valid
    uintptr_t chunk0 = 0;
    if (!Mem::ReadSafe(addr, chunk0) || chunk0 == 0) {
        LOG_WARN("ValidateGNames: chunk[0] is null at 0x%llX",
                 static_cast<unsigned long long>(addr));
        return false;
    }

    // FName index 0 should be "None"
    // Entry at chunk0 + 0 * 2 = chunk0
    // FNameEntry: uint16 header, then chars
    uint16_t header = 0;
    if (!Mem::ReadSafe(chunk0, header)) return false;

    int len = header >> 6;
    if (len == 4) {
        char name[5] = {};
        if (Mem::ReadBytesSafe(chunk0 + 2, name, 4) && strcmp(name, "None") == 0) {
            LOG_INFO("ValidateGNames: Valid at 0x%llX (verified 'None')",
                     static_cast<unsigned long long>(addr));
            return true;
        }
    }

    // Try alternate header format: len in bits 1-15, wide flag at bit 0
    len = (header >> 1) & 0x7FF;
    if (len == 4) {
        char name[5] = {};
        if (Mem::ReadBytesSafe(chunk0 + 2, name, 4) && strcmp(name, "None") == 0) {
            LOG_INFO("ValidateGNames: Valid at 0x%llX (alt header, verified 'None')",
                     static_cast<unsigned long long>(addr));
            return true;
        }
    }

    LOG_WARN("ValidateGNames: Validation failed at 0x%llX", static_cast<unsigned long long>(addr));
    return false;
}

uintptr_t FindGNames() {
    LOG_INFO("FindGNames: Scanning for GNames (FNamePool)...");

    // Primary: lea rsi, [rip+rel32]; jmp
    uintptr_t result = TryPatternRIP(Constants::AOB_GNAMES_PRIMARY, 3, 7, false);
    if (result && ValidateGNames(result)) return result;

    // Fallback 1: lea rcx, [rip+rel32]; call
    result = TryPatternRIP(Constants::AOB_GNAMES_FALLBACK1, 3, 7, false);
    if (result && ValidateGNames(result)) return result;

    // Fallback 2: lea rax, [rip+rel32]; jmp
    result = TryPatternRIP(Constants::AOB_GNAMES_FALLBACK2, 3, 7, false);
    if (result && ValidateGNames(result)) return result;

    LOG_ERROR("FindGNames: All patterns failed");
    return 0;
}

uintptr_t FindGWorld() {
    LOG_INFO("FindGWorld: Scanning for GWorld...");

    uintptr_t result = TryPatternRIP(Constants::AOB_GWORLD_PRIMARY, 3, 7, false);
    if (result) {
        uintptr_t world = 0;
        if (Mem::ReadSafe(result, world) && world != 0) {
            LOG_INFO("FindGWorld: Found at 0x%llX (value=0x%llX)",
                     static_cast<unsigned long long>(result),
                     static_cast<unsigned long long>(world));
            return result;
        }
    }

    result = TryPatternRIP(Constants::AOB_GWORLD_FALLBACK1, 3, 7, false);
    if (result) {
        uintptr_t world = 0;
        if (Mem::ReadSafe(result, world) && world != 0) {
            LOG_INFO("FindGWorld: Found at 0x%llX via fallback",
                     static_cast<unsigned long long>(result));
            return result;
        }
    }

    LOG_WARN("FindGWorld: All patterns failed (non-critical)");
    return 0;
}

uint32_t DetectVersion() {
    LOG_INFO("DetectVersion: Attempting to detect UE version...");

    // Scan for version string pattern in memory: "5.X.Y"
    // UE stores this in FEngineVersion
    uintptr_t base = Mem::GetModuleBase(nullptr);
    size_t size = Mem::GetModuleSize(nullptr);
    if (!base || !size) return 0;

    // Search for "++" prefix that UE version strings use: "++UE5+Release-5.X"
    const char* versionPatterns[] = {
        "5.4.", "5.3.", "5.2.", "5.1.", "5.0."
    };
    const uint32_t versionValues[] = {
        504, 503, 502, 501, 500
    };

    // Scan .rdata section for version strings
    for (int i = 0; i < 5; ++i) {
        const char* verStr = versionPatterns[i];
        size_t verLen = strlen(verStr);
        const uint8_t* scan = reinterpret_cast<const uint8_t*>(base);

        for (size_t off = 0; off < size - verLen - 10; ++off) {
            if (memcmp(scan + off, verStr, verLen) == 0) {
                // Check if preceded by "Release-" or similar
                if (off >= 8) {
                    char prefix[16] = {};
                    memcpy(prefix, scan + off - 8, 8);
                    if (strstr(prefix, "Release") || strstr(prefix, "release")) {
                        LOG_INFO("DetectVersion: Found version %s at offset 0x%zX -> %u",
                                 verStr, off, versionValues[i]);
                        return versionValues[i];
                    }
                }
                // Also check if it looks like a clean version string
                char context[32] = {};
                Mem::ReadBytesSafe(base + off, context, 31);
                if (context[3] >= '0' && context[3] <= '9') {
                    LOG_INFO("DetectVersion: Detected version %u from string at 0x%zX",
                             versionValues[i], off);
                    return versionValues[i];
                }
            }
        }
    }

    LOG_WARN("DetectVersion: Could not detect UE version, defaulting to 504");
    return 504; // Default to latest
}

bool FindAll(EnginePointers& out) {
    LOG_INFO("FindAll: Starting global pointer scan...");

    out.UEVersion = DetectVersion();
    LOG_INFO("FindAll: UE Version = %u", out.UEVersion);

    out.GObjects = FindGObjects();
    if (!out.GObjects) {
        LOG_ERROR("FindAll: Failed to find GObjects — aborting");
        return false;
    }

    out.GNames = FindGNames();
    if (!out.GNames) {
        LOG_ERROR("FindAll: Failed to find GNames — aborting");
        return false;
    }

    out.GWorld = FindGWorld();
    // GWorld is non-critical, just log

    LOG_INFO("FindAll: Complete — GObjects=0x%llX, GNames=0x%llX, GWorld=0x%llX, UE=%u",
             static_cast<unsigned long long>(out.GObjects),
             static_cast<unsigned long long>(out.GNames),
             static_cast<unsigned long long>(out.GWorld),
             out.UEVersion);

    return true;
}

} // namespace OffsetFinder
