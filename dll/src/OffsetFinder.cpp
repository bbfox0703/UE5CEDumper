// ============================================================
// OffsetFinder.cpp — AOB scanning for GObjects/GNames/GWorld
// ============================================================

#include "OffsetFinder.h"
#include "Memory.h"
#include "Logger.h"
#include "Constants.h"

#include <string>
#include <cstring>
#include <vector>
#include <Winver.h>   // GetFileVersionInfoW / VerQueryValueW

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

    // Try all known patterns in order of commonness.
    // Each resolves the RIP-relative address, then optionally dereferences.
    // All GObjects instructions are 7-byte RIP-relative loads (opcodeLen=3, totalLen=7).
    struct { const char* pattern; bool deref; } candidates[] = {
        { Constants::AOB_GOBJECTS_V2, false }, // 4C 8B 0D — most common in UE5.3+
        { Constants::AOB_GOBJECTS_V1, false }, // 48 8B 05 — classic UE5.0-5.2
        { Constants::AOB_GOBJECTS_V3, false }, // 4C 8B 05
        { Constants::AOB_GOBJECTS_V4, false }, // 48 8B 05 (longer context)
        { Constants::AOB_GOBJECTS_V5, false }, // 4C 8B 15
        // Retry first two with an extra deref for pointer-to-pointer layouts
        { Constants::AOB_GOBJECTS_V2, true  },
        { Constants::AOB_GOBJECTS_V1, true  },
    };

    for (auto& c : candidates) {
        uintptr_t result = TryPatternRIP(c.pattern, 3, 7, c.deref);
        if (result && ValidateGObjects(result)) return result;
    }

    LOG_ERROR("FindGObjects: All patterns failed");
    return 0;
}

// Validate GNames by checking that FName[0] == "None".
//
// The AOB pattern resolves to the FNamePool object address. The Blocks[]
// chunk pointer array lives INSIDE FNamePool at a variable offset:
//
//   Standard UE5 layout (FNameEntryAllocator):
//     [+0x00] FRWLock (SRWLOCK, 8 bytes)   ← reading this as chunk0 gives bad pointer
//     [+0x08] CurrentBlock  (uint32)
//     [+0x0C] CurrentByteCursor (uint32)
//     [+0x10] Blocks[0]  ← first actual chunk pointer
//
// We try multiple offsets so the validator works across engine variants.
static bool ValidateGNames(uintptr_t addr) {
    if (!addr) return false;

    // Offsets to try for the start of the Blocks[] array within FNamePool.
    // 0x10 is the standard UE5 offset; 0x00 covers builds where the AOB
    // resolves directly to the chunk array rather than the pool object.
    static const int kOffsets[] = { 0x10, 0x00, 0x08, 0x20, 0x40 };

    for (int off : kOffsets) {
        uintptr_t chunk0 = 0;
        if (!Mem::ReadSafe(addr + off, chunk0) || chunk0 == 0) continue;

        // chunk0 must be a readable address
        uint16_t header = 0;
        if (!Mem::ReadSafe(chunk0, header)) continue;

        // FName[0] should be "None" (length 4).
        // Try both header formats:
        //   Format A (older): len = header >> 6
        //   Format B (newer): len = (header >> 1) & 0x7FF
        char name[5] = {};
        int lenA = header >> 6;
        if (lenA == 4 && Mem::ReadBytesSafe(chunk0 + 2, name, 4) && strcmp(name, "None") == 0) {
            LOG_INFO("ValidateGNames: Valid at 0x%llX (chunks@+0x%02X, FmtA, 'None')",
                     static_cast<unsigned long long>(addr), off);
            return true;
        }
        int lenB = (header >> 1) & 0x7FF;
        memset(name, 0, sizeof(name));
        if (lenB == 4 && Mem::ReadBytesSafe(chunk0 + 2, name, 4) && strcmp(name, "None") == 0) {
            LOG_INFO("ValidateGNames: Valid at 0x%llX (chunks@+0x%02X, FmtB, 'None')",
                     static_cast<unsigned long long>(addr), off);
            return true;
        }

        LOG_DEBUG("ValidateGNames: offset +0x%02X chunk0=0x%llX header=0x%04X lenA=%d lenB=%d name='%.4s'",
                  off, static_cast<unsigned long long>(chunk0), header, lenA, lenB, name);
    }

    LOG_WARN("ValidateGNames: Validation failed at 0x%llX", static_cast<unsigned long long>(addr));
    return false;
}

uintptr_t FindGNames() {
    LOG_INFO("FindGNames: Scanning for GNames (FNamePool)...");

    // LEA instructions are RIP-relative (opcodeLen=3, totalLen=7) and yield
    // the address directly (no dereference needed for the pool itself).
    const char* candidates[] = {
        Constants::AOB_GNAMES_V1,  // lea rsi,[rip+X]; jmp
        Constants::AOB_GNAMES_V2,  // lea rcx,[rip+X]; call
        Constants::AOB_GNAMES_V3,  // lea rax,[rip+X]; jmp
        Constants::AOB_GNAMES_V4,  // lea r8,[rip+X];  jmp (REX.R)
    };

    for (auto* pat : candidates) {
        uintptr_t result = TryPatternRIP(pat, 3, 7, false);
        if (result && ValidateGNames(result)) return result;
    }

    LOG_ERROR("FindGNames: All patterns failed");
    return 0;
}

uintptr_t FindGWorld() {
    LOG_INFO("FindGWorld: Scanning for GWorld...");

    // All GWorld patterns are 7-byte RIP-relative instructions (read or write).
    // We return the address of the global variable (&GWorld), not the UWorld* value,
    // so the UI can do live-watch.  The current UWorld* must be non-null to validate
    // read-patterns; write-patterns are accepted even if currently null (at startup).
    struct { const char* pattern; bool requireNonNull; } candidates[] = {
        { Constants::AOB_GWORLD_V1, true  }, // mov rax,[rip+X]; cmp/cmov
        { Constants::AOB_GWORLD_V2, false }, // mov [rip+X],rax (write — may be null at scan time)
        { Constants::AOB_GWORLD_V3, true  }, // mov rbx,[rip+X]
        { Constants::AOB_GWORLD_V4, true  }, // mov rdi,[rip+X]
        { Constants::AOB_GWORLD_V5, true  }, // cmp [rip+X],rax
        { Constants::AOB_GWORLD_V6, false }, // mov [rip+X],rbx (write)
    };

    for (auto& c : candidates) {
        uintptr_t result = TryPatternRIP(c.pattern, 3, 7, false);
        if (!result) continue;

        uintptr_t world = 0;
        Mem::ReadSafe(result, world);

        if (!c.requireNonNull || world != 0) {
            LOG_INFO("FindGWorld: Found at 0x%llX (value=0x%llX)",
                     static_cast<unsigned long long>(result),
                     static_cast<unsigned long long>(world));
            return result;
        }
    }

    LOG_WARN("FindGWorld: All patterns failed (non-critical)");
    return 0;
}

// Fast O(1) version detection via PE VERSIONINFO resource.
// UE games embed the engine version in their VS_FIXEDFILEINFO.dwProductVersion:
//   HIWORD(dwProductVersionMS) = major (5 for UE5)
//   LOWORD(dwProductVersionMS) = minor (0-4 for UE 5.0-5.4)
static uint32_t DetectVersionFromPEResource() {
    wchar_t exePath[MAX_PATH] = {};
    if (!GetModuleFileNameW(nullptr, exePath, MAX_PATH)) return 0;

    DWORD handle = 0;
    DWORD infoSize = GetFileVersionInfoSizeW(exePath, &handle);
    if (!infoSize) return 0;

    std::vector<uint8_t> buf(infoSize);
    if (!GetFileVersionInfoW(exePath, handle, infoSize, buf.data())) return 0;

    VS_FIXEDFILEINFO* fi = nullptr;
    UINT len = 0;
    if (!VerQueryValueW(buf.data(), L"\\",
                        reinterpret_cast<LPVOID*>(&fi), &len)) return 0;
    if (!fi || len < sizeof(VS_FIXEDFILEINFO)) return 0;

    uint32_t major = HIWORD(fi->dwProductVersionMS);
    uint32_t minor = LOWORD(fi->dwProductVersionMS);

    if (major == 5 && minor <= 9) {
        LOG_INFO("DetectVersion: PE VERSIONINFO -> UE %u.%u -> %u",
                 major, minor, 500u + minor);
        return 500u + minor;
    }

    // Some shippers put 4.x in the info (UE4 fork claiming UE5 classes)
    if (major == 4 && minor <= 27) {
        LOG_INFO("DetectVersion: PE VERSIONINFO -> UE4.%u (treated as 400+minor)", minor);
        return 400u + minor;
    }

    LOG_WARN("DetectVersion: PE VERSIONINFO major=%u minor=%u — unrecognised", major, minor);
    return 0;
}

uint32_t DetectVersion() {
    LOG_INFO("DetectVersion: Attempting to detect UE version...");

    // Fast path: read the PE VERSIONINFO resource (O(1), no memory scan)
    uint32_t ver = DetectVersionFromPEResource();
    if (ver) return ver;

    LOG_WARN("DetectVersion: PE resource failed, falling back to memory string scan");

    // Slow path: scan for UE version strings embedded in the binary
    uintptr_t base = Mem::GetModuleBase(nullptr);
    size_t    size = Mem::GetModuleSize(nullptr);
    if (!base || !size) {
        LOG_WARN("DetectVersion: Cannot get module base — defaulting to 504");
        return 504;
    }

    // Patterns like "++UE5+Release-5.X" or bare "5.X." in .rdata
    struct { const char* needle; uint32_t value; } patterns[] = {
        { "5.4.", 504 }, { "5.3.", 503 }, { "5.2.", 502 },
        { "5.1.", 501 }, { "5.0.", 500 },
    };

    const uint8_t* scan = reinterpret_cast<const uint8_t*>(base);
    for (auto& p : patterns) {
        size_t needleLen = strlen(p.needle);
        for (size_t off = 0; off + needleLen + 10 < size; ++off) {
            if (memcmp(scan + off, p.needle, needleLen) != 0) continue;

            // Require "Release" prefix within the preceding 16 bytes
            if (off >= 8) {
                char ctx[17] = {};
                memcpy(ctx, scan + off - 8, 8);
                if (strstr(ctx, "Release") || strstr(ctx, "release")) {
                    LOG_INFO("DetectVersion: String scan -> %u (Release prefix at 0x%zX)",
                             p.value, off);
                    return p.value;
                }
            }
            // Also accept if the char after "5.X." is a digit (e.g. "5.4.0")
            if (scan[off + needleLen] >= '0' && scan[off + needleLen] <= '9') {
                LOG_INFO("DetectVersion: String scan -> %u (at 0x%zX)", p.value, off);
                return p.value;
            }
        }
    }

    LOG_WARN("DetectVersion: Could not detect UE version, defaulting to 504");
    return 504;
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
