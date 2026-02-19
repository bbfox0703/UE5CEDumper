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

    // Dump the first 128 bytes so we can diagnose the layout manually
    {
        char hexbuf[256];
        int pos = 0;
        for (int i = 0; i < 128 && pos < 200; i += 8) {
            uintptr_t v = 0;
            if (Mem::ReadSafe(addr + i, v))
                pos += snprintf(hexbuf + pos, sizeof(hexbuf) - pos,
                                " +%02X:%016llX", i, (unsigned long long)v);
            else
                pos += snprintf(hexbuf + pos, sizeof(hexbuf) - pos, " +%02X:[??]", i);
        }
        LOG_DEBUG("ValidateGNames: dump@0x%llX:%s",
                  (unsigned long long)addr, hexbuf);
    }
    LOG_WARN("ValidateGNames: Validation failed at 0x%llX", static_cast<unsigned long long>(addr));
    return false;
}

// ─────────────────────────────────────────────────────────────────────────────
// FindGNamesByPointerScan — fallback when all AOB patterns fail
//
// Strategy:
//   The FNamePool object lives in the game's .data / .bss section and contains
//   an internal Blocks[] array (at a variable offset, typically +0x10).
//   Blocks[0] is a pointer to a heap-allocated chunk whose very first bytes
//   are the "None" FNameEntry (the #0 name in FNamePool).
//
//   By scanning the game module's writable, non-exec sections for any 8-byte-
//   aligned pointer that dereferences to a "None" FNameEntry, we can locate
//   Blocks[0] and work backwards to the FNamePool base address.
// ─────────────────────────────────────────────────────────────────────────────

// Return true if the memory at `addr` starts with a "None" FNameEntry.
// Supports both header formats used across UE5 versions:
//   Format A (older): len = header >> 6        → len==4 ⇒ header=0x0100
//   Format B (newer): len = (header>>1)&0x7FF  → len==4 ⇒ header=0x0008/0x0009
static bool LooksLikeNoneEntry(uintptr_t addr) {
    uint8_t buf[6] = {};
    if (!Mem::ReadBytesSafe(addr, buf, 6)) return false;
    if (buf[2] != 'N' || buf[3] != 'o' || buf[4] != 'n' || buf[5] != 'e') return false;

    // Format A: header = 0x0100
    if (buf[0] == 0x00 && buf[1] == 0x01) return true;
    // Format B: header = 0x0008 or 0x0009 (IsWide flag in bit 0)
    if ((buf[0] == 0x08 || buf[0] == 0x09) && buf[1] == 0x00) return true;

    return false;
}

static uintptr_t FindGNamesByPointerScan() {
    LOG_INFO("FindGNamesByPointerScan: Scanning .data for pointer-to-'None' FNameEntry...");

    uintptr_t base = Mem::GetModuleBase(nullptr);
    if (!base) return 0;

    auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;

    auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(
        base + static_cast<DWORD>(dos->e_lfanew));
    if (nt->Signature != IMAGE_NT_SIGNATURE) return 0;

    const IMAGE_SECTION_HEADER* section = IMAGE_FIRST_SECTION(nt);

    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++section) {
        // Target: writable, non-executable sections (.data / .bss).
        // FNamePool is a static global — its Blocks[] array lives here.
        constexpr DWORD kWrite = IMAGE_SCN_MEM_WRITE;
        constexpr DWORD kExec  = IMAGE_SCN_MEM_EXECUTE;
        if (!(section->Characteristics & kWrite)) continue;
        if (  section->Characteristics & kExec ) continue;
        if (!section->Misc.VirtualSize || !section->VirtualAddress) continue;

        uintptr_t secBase = base + section->VirtualAddress;
        size_t    secSize = section->Misc.VirtualSize;

        char secName[9] = {};
        memcpy(secName, section->Name, 8);
        LOG_DEBUG("FindGNamesByPointerScan: Scanning section [%s] at 0x%llX (%zu bytes)",
                  secName, (unsigned long long)secBase, secSize);

        // Walk every 8-byte-aligned slot and treat it as a potential pointer.
        for (size_t off = 0; off + 8 <= secSize; off += 8) {
            uintptr_t ptr = 0;
            if (!Mem::ReadSafe(secBase + off, ptr)) continue;

            // Plausible user-space 64-bit address (exclude null, low, kernel)
            if (ptr < 0x10000 || ptr > 0x00007FFFFFFFFFFF) continue;

            // Skip if ptr lives inside the game module itself (not a heap chunk)
            size_t modSize = Mem::GetModuleSize(nullptr);
            if (ptr >= base && ptr < base + modSize) continue;

            // Check if ptr dereferences to a "None" FNameEntry
            if (!LooksLikeNoneEntry(ptr)) continue;

            // Found: ptr = chunk0 = FNamePool.Blocks[0]
            // pAddr  = secBase + off  = &FNamePool.Blocks[0] (in .data)
            // FNamePool base = pAddr − (offset of Blocks[0] within FNamePool)
            uintptr_t pAddr = secBase + off;

            LOG_INFO("FindGNamesByPointerScan: chunk0=0x%llX @ 0x%llX — trying pool offsets...",
                     (unsigned long long)ptr, (unsigned long long)pAddr);

            // Try common offsets of Blocks[0] within FNamePool:
            //   0x10 = standard UE5 (FRWLock[8] + CurrentBlock[4] + Cursor[4])
            //   0x00, 0x08, 0x18, 0x20 = observed variants
            for (int blkOff : { 0x10, 0x00, 0x08, 0x18, 0x20 }) {
                if ((size_t)blkOff > pAddr) continue; // underflow guard
                uintptr_t pool = pAddr - static_cast<uintptr_t>(blkOff);
                if (ValidateGNames(pool)) {
                    LOG_INFO("FindGNamesByPointerScan: Valid pool at 0x%llX (Blocks[0]@+0x%02X)",
                             (unsigned long long)pool, blkOff);
                    return pool;
                }
            }
        }
    }

    LOG_WARN("FindGNamesByPointerScan: No valid FNamePool found in .data");
    return 0;
}

uintptr_t FindGNames() {
    LOG_INFO("FindGNames: Scanning for GNames (FNamePool)...");

    // Try each pattern both with and without a pointer dereference:
    //   deref=false: the LEA resolves directly to the FNamePool object
    //   deref=true:  the LEA resolves to a FNamePool* pointer we must deref
    // (Which variant applies depends on how the compiler emitted the reference.)
    struct { const char* pattern; bool deref; } candidates[] = {
        { Constants::AOB_GNAMES_V1, false },  // lea rsi; direct
        { Constants::AOB_GNAMES_V1, true  },  // lea rsi; deref pointer
        { Constants::AOB_GNAMES_V3, false },  // lea rax; direct
        { Constants::AOB_GNAMES_V3, true  },  // lea rax; deref pointer
        { Constants::AOB_GNAMES_V4, false },  // lea r8;  direct
        { Constants::AOB_GNAMES_V4, true  },  // lea r8;  deref pointer
        { Constants::AOB_GNAMES_V2, false },  // lea rcx; call; direct
        { Constants::AOB_GNAMES_V2, true  },  // lea rcx; call; deref pointer
    };

    for (auto& c : candidates) {
        uintptr_t result = TryPatternRIP(c.pattern, 3, 7, c.deref);
        if (result && ValidateGNames(result)) return result;
    }

    // All AOB patterns failed — fall back to data-pointer scan.
    LOG_WARN("FindGNames: All patterns failed, trying pointer scan fallback...");
    {
        uintptr_t result = FindGNamesByPointerScan();
        if (result) return result;
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
