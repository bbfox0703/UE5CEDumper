// ============================================================
// OffsetFinder.cpp — AOB scanning for GObjects/GNames/GWorld
// ============================================================

#include "OffsetFinder.h"
#include "Memory.h"
#define LOG_CAT "SCAN"
#include "Logger.h"
#include "Constants.h"
#include "ObjectArray.h"
#include "FNamePool.h"

#include <string>
#include <cstring>
#include <vector>
#include <Winver.h>   // GetFileVersionInfoW / VerQueryValueW
#include <Psapi.h>    // EnumProcessModules

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

// ─────────────────────────────────────────────────────────────────────────────
// Symbol Export Fallback (RE-UE4SS technique)
// Many retail UE games export MSVC-mangled symbols. GetProcAddress resolves
// them in O(1), far faster than AOB scanning.
// ─────────────────────────────────────────────────────────────────────────────

// Try to resolve a symbol from loaded modules' export tables.
// Since the DLL is injected into the game process, GetModuleHandle(nullptr)
// returns the game executable's HMODULE.
static uintptr_t TrySymbolExport(const char* mangledName) {
    // Try main module first (most common case for monolithic builds)
    HMODULE hGame = GetModuleHandleW(nullptr);
    if (hGame) {
        FARPROC addr = GetProcAddress(hGame, mangledName);
        if (addr) {
            LOG_INFO("TrySymbolExport: Found '%s' in main module at 0x%llX",
                     mangledName, (unsigned long long)(uintptr_t)addr);
            return reinterpret_cast<uintptr_t>(addr);
        }
    }

    // Try other loaded modules (UE modular builds may split into separate DLLs)
    HMODULE modules[1024];
    DWORD cbNeeded = 0;
    if (EnumProcessModules(GetCurrentProcess(), modules, sizeof(modules), &cbNeeded)) {
        DWORD count = cbNeeded / sizeof(HMODULE);
        for (DWORD i = 0; i < count; ++i) {
            if (modules[i] == hGame) continue;
            FARPROC addr = GetProcAddress(modules[i], mangledName);
            if (addr) {
                wchar_t modName[MAX_PATH] = {};
                GetModuleFileNameW(modules[i], modName, MAX_PATH);
                LOG_INFO("TrySymbolExport: Found '%s' in module '%ls' at 0x%llX",
                         mangledName, modName, (unsigned long long)(uintptr_t)addr);
                return reinterpret_cast<uintptr_t>(addr);
            }
        }
    }

    return 0;
}

// Validate a candidate GObjects address (basic: check NumElements range)
static bool ValidateGObjects(uintptr_t addr) {
    if (!addr) return false;

    // Read NumElements (at offset 0x14 in default layout)
    int32_t numElements = 0;
    if (!Mem::ReadSafe(addr + 0x14, numElements)) return false;

    // Sanity: should have a reasonable number of objects
    if (numElements < 0x1000 || numElements > 0x400000) {
        Logger::Warn("SCAN:GObj", "ValidateGObjects: NumElements=%d out of range at 0x%llX",
                 numElements, static_cast<unsigned long long>(addr));
        return false;
    }

    Logger::Info("SCAN:GObj", "ValidateGObjects: Valid at 0x%llX, NumElements=%d",
             static_cast<unsigned long long>(addr), numElements);
    return true;
}

// ─────────────────────────────────────────────────────────────────────────────
// Structural validation for GNames (FNamePool):
//   1. FRWLock at +0x00 should be 0 (unlocked)
//   2. CurrentBlock at +0x08 should be a small int
//   3. Blocks[CurrentBlock+1] should be NULL (no block after last used)
//   4. Blocks[0] starts with "None" FNameEntry
// ─────────────────────────────────────────────────────────────────────────────
static bool ValidateGNamesStructural(uintptr_t addr) {
    if (!addr) return false;

    // FRWLock at +0x00 should be 0 when not locked
    uint64_t rwLock = 0;
    if (!Mem::ReadSafe(addr, rwLock)) return false;
    if (rwLock != 0) {
        Logger::Debug("SCAN:GNam", "ValidateGNamesStructural: FRWLock=0x%llX (non-zero) at 0x%llX",
                  (unsigned long long)rwLock, (unsigned long long)addr);
        return false;
    }

    // CurrentBlock at +0x08
    int32_t currentBlock = 0;
    if (!Mem::ReadSafe(addr + 0x08, currentBlock)) return false;
    if (currentBlock < 0 || currentBlock > 8192) {
        Logger::Debug("SCAN:GNam", "ValidateGNamesStructural: CurrentBlock=%d out of range at 0x%llX",
                  currentBlock, (unsigned long long)addr);
        return false;
    }

    // Blocks[CurrentBlock+1] should be NULL (end sentinel)
    uintptr_t nextBlock = 0;
    if (!Mem::ReadSafe(addr + 0x10 + ((currentBlock + 1) * 8), nextBlock)) return false;
    if (nextBlock != 0) {
        Logger::Debug("SCAN:GNam", "ValidateGNamesStructural: Blocks[%d+1] = 0x%llX (non-null) at 0x%llX",
                  currentBlock, (unsigned long long)nextBlock, (unsigned long long)addr);
        return false;
    }

    // Blocks[0] should point to "None" FNameEntry
    uintptr_t block0 = 0;
    if (!Mem::ReadSafe(addr + 0x10, block0) || block0 == 0) return false;

    // Read first bytes from block0 — should contain "None"
    char buf[10] = {};
    if (!Mem::ReadBytesSafe(block0, buf, 10)) return false;
    if (strstr(buf + 2, "None") == nullptr) {
        // Try reading as string starting at offset +2 (after 2-byte header)
        char name[5] = {};
        if (!Mem::ReadBytesSafe(block0 + 2, name, 4)) return false;
        if (strcmp(name, "None") != 0) {
            Logger::Debug("SCAN:GNam", "ValidateGNamesStructural: Blocks[0] doesn't start with 'None' at 0x%llX",
                      (unsigned long long)addr);
            return false;
        }
    }

    Logger::Info("SCAN:GNam", "ValidateGNamesStructural: Valid FNamePool at 0x%llX (CurrentBlock=%d)",
             (unsigned long long)addr, currentBlock);
    return true;
}

// ─────────────────────────────────────────────────────────────────────────────
// FindGObjectsByDataScan — fallback: collect ALL RIP-relative pointer
// references from .text that resolve into the data section, then validate
// each candidate as a GObjects/FUObjectArray.
// ─────────────────────────────────────────────────────────────────────────────
static uintptr_t FindGObjectsByDataScan() {
    Logger::Info("SCAN:GObj", "FindGObjectsByDataScan: Collecting static pointer references...");

    uintptr_t base = Mem::GetModuleBase(nullptr);
    if (!base) return 0;
    size_t modSize = Mem::GetModuleSize(nullptr);
    if (!modSize) return 0;

    auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;
    auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + static_cast<DWORD>(dos->e_lfanew));
    if (nt->Signature != IMAGE_NT_SIGNATURE) return 0;

    // Find code and data section ranges
    const IMAGE_SECTION_HEADER* section = IMAGE_FIRST_SECTION(nt);
    uintptr_t codeStart = 0, codeEnd = 0;
    uintptr_t dataStart = 0, dataEnd = 0;

    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++section) {
        if (!section->Misc.VirtualSize || !section->VirtualAddress) continue;
        uintptr_t secBase = base + section->VirtualAddress;
        uintptr_t secEnd  = secBase + section->Misc.VirtualSize;

        if (section->Characteristics & IMAGE_SCN_MEM_EXECUTE) {
            if (!codeStart || secBase < codeStart) codeStart = secBase;
            if (secEnd > codeEnd) codeEnd = secEnd;
        } else if (section->Characteristics & IMAGE_SCN_MEM_WRITE) {
            // First writable, non-exec section is the data section
            if (!dataStart) { dataStart = secBase; dataEnd = secEnd; }
        }
    }

    if (!codeStart || !dataStart) {
        Logger::Warn("SCAN:GObj", "FindGObjectsByDataScan: Could not identify code/data sections");
        return 0;
    }

    Logger::Debug("SCAN:GObj", "FindGObjectsByDataScan: code=[0x%llX-0x%llX], data=[0x%llX-0x%llX]",
              (unsigned long long)codeStart, (unsigned long long)codeEnd,
              (unsigned long long)dataStart, (unsigned long long)dataEnd);

    // Scan the code section for MOV reg,[rip+disp32] instructions (48 8B 0D / 48 8B 05 / 4C 8B 0D / etc.)
    // that resolve to addresses within the data section.
    // Opcodes: 48 8B {05,0D,15,1D,25,2D,35,3D} and 4C 8B {05,0D,15,1D,25,2D,35,3D}
    struct StaticPtr {
        uintptr_t instrAddr;    // address of the instruction
        uintptr_t targetAddr;   // resolved data-section address
    };
    std::vector<StaticPtr> bag;

    for (uintptr_t scan = codeStart; scan + 7 < codeEnd; ++scan) {
        uint8_t b0 = 0, b1 = 0, b2 = 0;
        if (!Mem::ReadSafe(scan, b0)) continue;
        if (b0 != 0x48 && b0 != 0x4C) continue;
        if (!Mem::ReadSafe(scan + 1, b1)) continue;
        if (b1 != 0x8B && b1 != 0x8D) continue;  // MOV or LEA
        if (!Mem::ReadSafe(scan + 2, b2)) continue;
        // ModR/M byte: mod=00, r/m=101 (RIP-relative) => lower 3 bits = 5
        if ((b2 & 0x07) != 0x05) continue;

        uintptr_t target = Mem::ResolveRIP(scan, 3, 7);
        if (!target) continue;

        // For MOV instructions (8B), the target is a pointer — dereference it
        uintptr_t value = target;
        if (b1 == 0x8B) {
            if (!Mem::ReadSafe(target, value) || !value) continue;
        }

        // Check if resolved address is in the data section range
        if (target >= dataStart && target < dataEnd) {
            bag.push_back({ scan, target });
        }
    }

    Logger::Info("SCAN:GObj", "FindGObjectsByDataScan: Found %zu static pointers in data section", bag.size());

    // Try each candidate with GObjects validation
    for (auto& sp : bag) {
        uintptr_t candidate = 0;
        if (!Mem::ReadSafe(sp.targetAddr, candidate) || !candidate) continue;
        if (ValidateGObjects(candidate)) {
            Logger::Info("SCAN:GObj", "FindGObjectsByDataScan: GObjects validated at 0x%llX (via instr@0x%llX)",
                     (unsigned long long)candidate, (unsigned long long)sp.instrAddr);
            return candidate;
        }
    }

    Logger::Warn("SCAN:GObj", "FindGObjectsByDataScan: No valid GObjects found among %zu candidates", bag.size());
    return 0;
}

// Try to find GObjects via MSVC symbol export.
// The symbol is the global variable itself — exported address IS the FUObjectArray.
static uintptr_t FindGObjectsByExport() {
    uintptr_t addr = TrySymbolExport(Constants::EXPORT_GOBJECTARRAY);
    if (!addr) return 0;

    // The export may point directly to FUObjectArray or to a pointer to it
    if (ValidateGObjects(addr)) {
        Logger::Info("SCAN:GObj", "FindGObjectsByExport: Validated at 0x%llX (direct)",
                 (unsigned long long)addr);
        return addr;
    }

    uintptr_t derefed = 0;
    if (Mem::ReadSafe(addr, derefed) && derefed && ValidateGObjects(derefed)) {
        Logger::Info("SCAN:GObj", "FindGObjectsByExport: Validated at 0x%llX (deref'd)",
                 (unsigned long long)derefed);
        return derefed;
    }

    Logger::Warn("SCAN:GObj", "FindGObjectsByExport: Symbol at 0x%llX failed validation",
             (unsigned long long)addr);
    return 0;
}

uintptr_t FindGObjects() {
    Logger::Info("SCAN:GObj", "FindGObjects: Scanning for GObjects...");

    // === Priority 0: Symbol export lookup (O(1), fastest) ===
    {
        uintptr_t result = FindGObjectsByExport();
        if (result) return result;
    }

    // Try all known patterns in order of commonness.
    // Each resolves the RIP-relative address, then optionally dereferences.
    // All GObjects instructions are 7-byte RIP-relative loads (opcodeLen=3, totalLen=7).
    struct { const char* pattern; bool deref; } candidates[] = {
        { Constants::AOB_GOBJECTS_V2, false }, // 4C 8B 0D — most common in UE5.3+
        { Constants::AOB_GOBJECTS_V1, false }, // 48 8B 05 — classic UE5.0-5.2
        { Constants::AOB_GOBJECTS_V6, false }, // 48 8B 0D — alt mov rcx variant
        { Constants::AOB_GOBJECTS_V7, false }, // 4C 8B 0D; cdq; movzx (GSpots)
        { Constants::AOB_GOBJECTS_V8, false }, // 4C 8B 0D; bit shift (GSpots)
        { Constants::AOB_GOBJECTS_V9, false }, // 4C 8B 0D; cdqe; lea (GSpots)
        { Constants::AOB_GOBJECTS_V3, false }, // 4C 8B 05
        { Constants::AOB_GOBJECTS_V4, false }, // 48 8B 05 (longer context)
        { Constants::AOB_GOBJECTS_V5, false }, // 4C 8B 15
        { Constants::AOB_GOBJECTS_V13, false }, // Palworld: 48 8B 05 + extended context
        // Retry with extra deref for pointer-to-pointer layouts
        { Constants::AOB_GOBJECTS_V2, true  },
        { Constants::AOB_GOBJECTS_V1, true  },
        { Constants::AOB_GOBJECTS_V6, true  },
        { Constants::AOB_GOBJECTS_V7, true  },
        { Constants::AOB_GOBJECTS_V8, true  },
        { Constants::AOB_GOBJECTS_V9, true  },
        { Constants::AOB_GOBJECTS_V13, true  }, // Palworld deref
    };

    for (auto& c : candidates) {
        uintptr_t result = TryPatternRIP(c.pattern, 3, 7, c.deref);
        if (result && ValidateGObjects(result)) return result;
    }

    // === RE-UE4SS patterns with special offset adjustments ===

    // V10 (Split Fiction): lea rcx; call; call; mov byte[],1
    // Resolved address is +0x10 into FUObjectArray — subtract 0x10
    {
        uintptr_t addr = Mem::AOBScan(Constants::AOB_GOBJECTS_V10);
        if (addr) {
            uintptr_t target = Mem::ResolveRIP(addr, 3, 7);
            if (target) {
                if (ValidateGObjects(target - 0x10)) return target - 0x10;
                if (ValidateGObjects(target))        return target;
            }
        }
    }

    // V11 (Little Nightmares 3): lea reg; mov r9,rcx; mov [rcx],rax; mov eax,-1
    {
        uintptr_t addr = Mem::AOBScan(Constants::AOB_GOBJECTS_V11);
        if (addr) {
            uintptr_t target = Mem::ResolveRIP(addr, 3, 7);
            if (target) {
                if (ValidateGObjects(target))        return target;
                if (ValidateGObjects(target - 0x10)) return target - 0x10;
            }
        }
    }

    // V12 (FF7 Remake): mov reg,[rip+X]; mov r8,[rax+rcx*8]; test; jz
    // This is a MOV, so deref is needed; also try -0x10 adjustment
    {
        uintptr_t addr = Mem::AOBScan(Constants::AOB_GOBJECTS_V12);
        if (addr) {
            uintptr_t target = Mem::ResolveRIP(addr, 3, 7);
            if (target) {
                uintptr_t value = 0;
                if (Mem::ReadSafe(target, value) && value) {
                    if (ValidateGObjects(value - 0x10)) return value - 0x10;
                    if (ValidateGObjects(value))        return value;
                }
            }
        }
    }

    // Fallback: exhaustive data-section pointer scan
    Logger::Warn("SCAN:GObj", "FindGObjects: All patterns failed, trying data-section scan fallback...");
    {
        uintptr_t result = FindGObjectsByDataScan();
        if (result) return result;
    }

    Logger::Error("SCAN:GObj", "FindGObjects: All patterns and fallback scan failed");
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
    static const int kOffsets[] = { 0x10, 0x00, 0x08, 0x18, 0x20, 0x28, 0x40 };

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
            Logger::Info("SCAN:GNam", "ValidateGNames: Valid at 0x%llX (chunks@+0x%02X, FmtA, 'None')",
                     static_cast<unsigned long long>(addr), off);
            return true;
        }
        int lenB = (header >> 1) & 0x7FF;
        memset(name, 0, sizeof(name));
        if (lenB == 4 && Mem::ReadBytesSafe(chunk0 + 2, name, 4) && strcmp(name, "None") == 0) {
            Logger::Info("SCAN:GNam", "ValidateGNames: Valid at 0x%llX (chunks@+0x%02X, FmtB, 'None')",
                     static_cast<unsigned long long>(addr), off);
            return true;
        }

        Logger::Debug("SCAN:GNam", "ValidateGNames: offset +0x%02X chunk0=0x%llX header=0x%04X lenA=%d lenB=%d name='%.4s'",
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
        Logger::Debug("SCAN:GNam", "ValidateGNames: dump@0x%llX:%s",
                  (unsigned long long)addr, hexbuf);
    }
    Logger::Warn("SCAN:GNam", "ValidateGNames: Validation failed at 0x%llX", static_cast<unsigned long long>(addr));
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

// Corroborate a FNamePool chunk by checking for common UE type name strings.
// Real FNamePool Blocks[0] always contains fundamental type names ("ByteProperty",
// "IntProperty", "Object", "Class", etc.) within the first 2048 bytes.
// Random heap data containing "None" won't also have these UE-specific strings.
static bool CorroborateFNameChunk(uintptr_t chunkAddr) {
    // Read first 2048 bytes of the chunk
    constexpr int kScanSize = 2048;
    uint8_t buf[kScanSize];
    if (!Mem::ReadBytesSafe(chunkAddr, buf, kScanSize)) return false;

    // Look for at least 2 of these UE type names within the chunk
    const char* markers[] = { "Property", "Object", "Struct", "Class", "Package", "Function" };
    int found = 0;
    for (const char* marker : markers) {
        size_t mlen = strlen(marker);
        for (int i = 0; i + (int)mlen <= kScanSize; ++i) {
            if (memcmp(buf + i, marker, mlen) == 0) {
                ++found;
                break;  // Only count each marker once
            }
        }
        if (found >= 2) return true;  // Early exit
    }
    return false;
}

// Return true if the memory at `addr` starts with a "None" FNameEntry.
// The FNameEntry header is 2 bytes (all known UE5 versions), followed by the
// name string.  Instead of checking specific header values (which vary across
// UE versions), we just look for ASCII "None" at offset +2.
// Also checks offset +4 in case a future build uses a 4-byte header.
static bool LooksLikeNoneEntry(uintptr_t addr) {
    uint8_t buf[8] = {};
    if (!Mem::ReadBytesSafe(addr, buf, 8)) return false;

    // Standard 2-byte header: "None" at offset +2
    if (buf[2] == 'N' && buf[3] == 'o' && buf[4] == 'n' && buf[5] == 'e')
        return true;

    // Potential 4-byte header variant: "None" at offset +4
    if (buf[4] == 'N' && buf[5] == 'o' && buf[6] == 'n' && buf[7] == 'e')
        return true;

    return false;
}

static uintptr_t FindGNamesByPointerScan() {
    Logger::Info("SCAN:GNam", "FindGNamesByPointerScan: Scanning .data for pointer-to-'None' FNameEntry...");

    uintptr_t base = Mem::GetModuleBase(nullptr);
    if (!base) return 0;

    auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;

    auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(
        base + static_cast<DWORD>(dos->e_lfanew));
    if (nt->Signature != IMAGE_NT_SIGNATURE) return 0;

    const IMAGE_SECTION_HEADER* section = IMAGE_FIRST_SECTION(nt);
    size_t modSize = Mem::GetModuleSize(nullptr);

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
        Logger::Debug("SCAN:GNam", "FindGNamesByPointerScan: Scanning section [%s] at 0x%llX (%zu bytes)",
                  secName, (unsigned long long)secBase, secSize);

        // Walk every 8-byte-aligned slot and treat it as a potential pointer.
        int diagCount = 0;  // Limit diagnostic dumps to first few candidates
        for (size_t off = 0; off + 8 <= secSize; off += 8) {
            uintptr_t ptr = 0;
            if (!Mem::ReadSafe(secBase + off, ptr)) continue;

            // Plausible user-space 64-bit address (exclude null, low, kernel)
            if (ptr < 0x10000 || ptr > 0x00007FFFFFFFFFFF) continue;

            // Skip if ptr lives inside the game module itself (not a heap chunk)
            if (ptr >= base && ptr < base + modSize) continue;

            // Check if ptr dereferences to a "None" FNameEntry
            if (!LooksLikeNoneEntry(ptr)) {
                // Near-miss diagnostic: check if "None" appears anywhere in first 16 bytes
                // This catches unknown header formats we didn't account for
                if (diagCount < 10) {
                    uint8_t peek[16] = {};
                    if (Mem::ReadBytesSafe(ptr, peek, 16)) {
                        for (int p = 0; p + 4 <= 16; ++p) {
                            if (peek[p] == 'N' && peek[p+1] == 'o' && peek[p+2] == 'n' && peek[p+3] == 'e') {
                                Logger::Warn("SCAN:GNam", "FindGNamesByPointerScan: NEAR-MISS 'None' at ptr=0x%llX offset=%d "
                                         "header=%02X%02X%02X%02X bytes=%02X %02X %02X %02X %02X %02X %02X %02X "
                                         "%02X %02X %02X %02X %02X %02X %02X %02X (.data+0x%zX)",
                                         (unsigned long long)ptr, p,
                                         peek[0], peek[1], peek[2], peek[3],
                                         peek[0], peek[1], peek[2], peek[3], peek[4], peek[5], peek[6], peek[7],
                                         peek[8], peek[9], peek[10], peek[11], peek[12], peek[13], peek[14], peek[15],
                                         off);
                                ++diagCount;
                                break;
                            }
                        }
                    }
                }
                continue;
            }

            // Found: ptr = chunk0 = FNamePool.Blocks[0]
            // pAddr  = secBase + off  = &FNamePool.Blocks[0] (in .data)
            // FNamePool base = pAddr − (offset of Blocks[0] within FNamePool)
            uintptr_t pAddr = secBase + off;

            Logger::Info("SCAN:GNam", "FindGNamesByPointerScan: chunk0=0x%llX @ 0x%llX — corroborating...",
                     (unsigned long long)ptr, (unsigned long long)pAddr);

            // Corroborate: real FNamePool chunks contain UE type names
            if (!CorroborateFNameChunk(ptr)) {
                Logger::Debug("SCAN:GNam", "FindGNamesByPointerScan: Corroboration failed — skipping");
                continue;
            }

            // Dump what the candidate actually points to for diagnostics
            if (diagCount < 5) {
                char hexbuf[64] = {};
                uint8_t peek[16] = {};
                if (Mem::ReadBytesSafe(ptr, peek, 16)) {
                    snprintf(hexbuf, sizeof(hexbuf),
                             "%02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X",
                             peek[0], peek[1], peek[2], peek[3], peek[4], peek[5], peek[6], peek[7],
                             peek[8], peek[9], peek[10], peek[11], peek[12], peek[13], peek[14], peek[15]);
                }
                Logger::Debug("SCAN:GNam", "FindGNamesByPointerScan: candidate chunk0 bytes: %s", hexbuf);
                ++diagCount;
            }

            // Try common offsets of Blocks[0] within FNamePool:
            //   0x10 = standard UE5 (FRWLock[8] + CurrentBlock[4] + Cursor[4])
            //   0x00, 0x08, 0x18, 0x20, 0x28 = observed variants
            for (int blkOff : { 0x10, 0x00, 0x08, 0x18, 0x20, 0x28 }) {
                if ((size_t)blkOff > pAddr) continue; // underflow guard
                uintptr_t pool = pAddr - static_cast<uintptr_t>(blkOff);
                if (ValidateGNames(pool) || ValidateGNamesStructural(pool)) {
                    Logger::Info("SCAN:GNam", "FindGNamesByPointerScan: Valid pool at 0x%llX (Blocks[0]@+0x%02X)",
                             (unsigned long long)pool, blkOff);
                    return pool;
                }
            }
        }
    }

    Logger::Warn("SCAN:GNam", "FindGNamesByPointerScan: No valid FNamePool found in .data");
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────────
// FindGNamesByExport — find FName::ToString or FName::FName via symbol export,
// then scan inside the function body for RIP-relative references to FNamePool.
//
// No game exports FNamePool directly, but many export FName functions that
// must reference the pool internally. We scan the first 256 bytes of the
// function for LEA/MOV RIP-relative instructions and validate each target.
// ─────────────────────────────────────────────────────────────────────────────
static uintptr_t FindGNamesByExport() {
    const char* symbols[] = {
        Constants::EXPORT_FNAME_TOSTRING,
        Constants::EXPORT_FNAME_CTOR,
        Constants::EXPORT_FNAME_CTOR_CHAR,
    };

    for (const char* sym : symbols) {
        uintptr_t funcAddr = TrySymbolExport(sym);
        if (!funcAddr) continue;

        Logger::Info("SCAN:GNam", "FindGNamesByExport: Scanning function body at 0x%llX for FNamePool refs ('%s')",
                 (unsigned long long)funcAddr, sym);

        // Scan first 256 bytes of the function for RIP-relative LEA/MOV
        // Pattern: REX.W prefix (0x48 or 0x4C) + opcode (0x8B=MOV or 0x8D=LEA) + ModR/M with r/m=101 (RIP)
        for (int off = 0; off + 7 <= 256; ++off) {
            uint8_t b0 = 0, b1 = 0, b2 = 0;
            if (!Mem::ReadSafe(funcAddr + off, b0)) break;
            if (b0 != 0x48 && b0 != 0x4C) continue;
            if (!Mem::ReadSafe(funcAddr + off + 1, b1)) break;
            if (b1 != 0x8B && b1 != 0x8D) continue;
            if (!Mem::ReadSafe(funcAddr + off + 2, b2)) break;
            if ((b2 & 0x07) != 0x05) continue;  // RIP-relative addressing

            uintptr_t target = Mem::ResolveRIP(funcAddr + off, 3, 7);
            if (!target) continue;

            // For MOV (0x8B), target is a pointer — dereference it
            uintptr_t candidate = target;
            if (b1 == 0x8B) {
                if (!Mem::ReadSafe(target, candidate) || !candidate) continue;
            }

            // Validate as FNamePool
            if (ValidateGNames(candidate) || ValidateGNamesStructural(candidate)) {
                Logger::Info("SCAN:GNam", "FindGNamesByExport: Found FNamePool at 0x%llX (via %s func+0x%X, %s)",
                         (unsigned long long)candidate, sym, off,
                         b1 == 0x8D ? "LEA" : "MOV");
                return candidate;
            }
        }
    }

    return 0;
}

uintptr_t FindGNames() {
    Logger::Info("SCAN:GNam", "FindGNames: Scanning for GNames (FNamePool)...");

    // === Priority 0: Symbol export + function body scan (O(1) lookup) ===
    {
        uintptr_t result = FindGNamesByExport();
        if (result) return result;
    }

    // Try each pattern both with and without a pointer dereference:
    //   deref=false: the LEA resolves directly to the FNamePool object
    //   deref=true:  the LEA resolves to a FNamePool* pointer we must deref
    // (Which variant applies depends on how the compiler emitted the reference.)
    struct { const char* pattern; bool deref; } candidates[] = {
        { Constants::AOB_GNAMES_V5, false },  // lea rcx; call; mov byte ptr[],1 (extended context)
        { Constants::AOB_GNAMES_V5, true  },  // lea rcx; call; mov byte ptr[],1; deref
        { Constants::AOB_GNAMES_V1, false },  // lea rsi; direct
        { Constants::AOB_GNAMES_V1, true  },  // lea rsi; deref pointer
        { Constants::AOB_GNAMES_V3, false },  // lea rax; direct
        { Constants::AOB_GNAMES_V3, true  },  // lea rax; deref pointer
        { Constants::AOB_GNAMES_V4, false },  // lea r8;  direct
        { Constants::AOB_GNAMES_V4, true  },  // lea r8;  deref pointer
        { Constants::AOB_GNAMES_V2, false },  // lea rcx; call; direct
        { Constants::AOB_GNAMES_V2, true  },  // lea rcx; call; deref pointer
        { Constants::AOB_GNAMES_V6, false },  // mov rax,[rip+X]; test; jnz (GSpots UE5+)
        { Constants::AOB_GNAMES_V6, true  },  // mov rax,[rip+X]; test; jnz; deref
        { Constants::AOB_GNAMES_V8, false },  // Palworld: lea rax,[rip+X]; jmp 0x13 (extended context)
        { Constants::AOB_GNAMES_V8, true  },  // Palworld deref
    };

    for (auto& c : candidates) {
        uintptr_t result = TryPatternRIP(c.pattern, 3, 7, c.deref);
        if (!result) continue;
        // Use both original ValidateGNames (checks "None" entry) and
        // structural validation (FRWLock, CurrentBlock, Blocks sentinel).
        // Accept if either validates — they check complementary properties.
        if (ValidateGNames(result) || ValidateGNamesStructural(result)) return result;
    }

    // === FName ctor call-site pattern (V7, FF7 Rebirth) ===
    // This pattern finds a call-site that invokes FName::FName(). We follow the
    // CALL target, then scan the function body for RIP-relative refs to FNamePool.
    {
        uintptr_t callSite = Mem::AOBScan(Constants::AOB_GNAMES_V7_FNAME_CTOR);
        if (callSite) {
            // The CALL instruction is at offset +11 in the pattern: E8 xx xx xx xx
            // Pattern: 41 B8 01 00 00 00 48 8D 4C 24 ?? E8 ?? ?? ?? ?? C6 44 24
            //          0  1  2  3  4  5  6  7  8  9  10 11 12 13 14 15
            uintptr_t callInstr = callSite + 11;
            uint8_t callOpcode = 0;
            Mem::ReadSafe(callInstr, callOpcode);

            if (callOpcode == 0xE8) {
                int32_t rel32 = 0;
                if (Mem::ReadSafe(callInstr + 1, rel32)) {
                    uintptr_t funcAddr = callInstr + 5 + rel32;
                    Logger::Info("SCAN:GNam", "FindGNames: V7 FName ctor call-site → function at 0x%llX",
                             (unsigned long long)funcAddr);

                    // Scan function body for RIP-relative refs to FNamePool
                    for (int off = 0; off + 7 <= 256; ++off) {
                        uint8_t b0 = 0, b1 = 0, b2 = 0;
                        if (!Mem::ReadSafe(funcAddr + off, b0)) break;
                        if (b0 != 0x48 && b0 != 0x4C) continue;
                        if (!Mem::ReadSafe(funcAddr + off + 1, b1)) break;
                        if (b1 != 0x8B && b1 != 0x8D) continue;
                        if (!Mem::ReadSafe(funcAddr + off + 2, b2)) break;
                        if ((b2 & 0x07) != 0x05) continue;

                        uintptr_t target = Mem::ResolveRIP(funcAddr + off, 3, 7);
                        if (!target) continue;

                        uintptr_t candidate = target;
                        if (b1 == 0x8B) {
                            if (!Mem::ReadSafe(target, candidate) || !candidate) continue;
                        }

                        if (ValidateGNames(candidate) || ValidateGNamesStructural(candidate)) {
                            Logger::Info("SCAN:GNam", "FindGNames: V7 found FNamePool at 0x%llX (func+0x%X)",
                                     (unsigned long long)candidate, off);
                            return candidate;
                        }
                    }
                }
            }
        }
    }

    // All AOB patterns failed — fall back to data-pointer scan.
    Logger::Warn("SCAN:GNam", "FindGNames: All patterns failed, trying pointer scan fallback...");
    {
        uintptr_t result = FindGNamesByPointerScan();
        if (result) return result;
    }

    Logger::Error("SCAN:GNam", "FindGNames: All patterns failed");
    return 0;
}

uintptr_t FindGWorld() {
    Logger::Info("SCAN:GWld", "FindGWorld: Scanning for GWorld...");

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
        { Constants::AOB_GWORLD_V7, true  }, // Palworld: mov rbx,[rip+X]; test; jz 0x33; mov r8b
    };

    for (auto& c : candidates) {
        uintptr_t result = TryPatternRIP(c.pattern, 3, 7, false);
        if (!result) continue;

        uintptr_t world = 0;
        Mem::ReadSafe(result, world);

        if (!c.requireNonNull || world != 0) {
            Logger::Info("SCAN:GWld", "FindGWorld: Found at 0x%llX (value=0x%llX)",
                     static_cast<unsigned long long>(result),
                     static_cast<unsigned long long>(world));
            return result;
        }
    }

    Logger::Warn("SCAN:GWld", "FindGWorld: All patterns failed (non-critical)");
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
        Logger::Info("SCAN:Ver", "DetectVersion: PE VERSIONINFO -> UE %u.%u -> %u",
                 major, minor, 500u + minor);
        return 500u + minor;
    }

    // Some shippers put 4.x in the info (UE4 fork claiming UE5 classes)
    if (major == 4 && minor <= 27) {
        Logger::Info("SCAN:Ver", "DetectVersion: PE VERSIONINFO -> UE4.%u (treated as 400+minor)", minor);
        return 400u + minor;
    }

    Logger::Warn("SCAN:Ver", "DetectVersion: PE VERSIONINFO major=%u minor=%u — unrecognised", major, minor);
    return 0;
}

uint32_t DetectVersion() {
    Logger::Info("SCAN:Ver", "DetectVersion: Attempting to detect UE version...");

    // Fast path: read the PE VERSIONINFO resource (O(1), no memory scan)
    uint32_t ver = DetectVersionFromPEResource();
    if (ver) return ver;

    Logger::Warn("SCAN:Ver", "DetectVersion: PE resource failed, falling back to memory string scan");

    // Slow path: scan for UE version strings embedded in the binary
    uintptr_t base = Mem::GetModuleBase(nullptr);
    size_t    size = Mem::GetModuleSize(nullptr);
    if (!base || !size) {
        Logger::Warn("SCAN:Ver", "DetectVersion: Cannot get module base — defaulting to 504");
        return 504;
    }

    // Patterns like "++UE5+Release-5.X" or bare "5.X." in .rdata
    // Include UE4 versions too for broader support
    struct { const char* needle; uint32_t value; } patterns[] = {
        { "5.7.", 507 }, { "5.6.", 506 }, { "5.5.", 505 },
        { "5.4.", 504 }, { "5.3.", 503 }, { "5.2.", 502 },
        { "5.1.", 501 }, { "5.0.", 500 },
        { "4.27.", 427 }, { "4.26.", 426 }, { "4.25.", 425 },
        { "4.24.", 424 }, { "4.23.", 423 }, { "4.22.", 422 },
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
                    Logger::Info("SCAN:Ver", "DetectVersion: String scan -> %u (Release prefix at 0x%zX)",
                             p.value, off);
                    return p.value;
                }
            }
            // Also accept if the char after "5.X." is a digit (e.g. "5.4.0")
            if (scan[off + needleLen] >= '0' && scan[off + needleLen] <= '9') {
                Logger::Info("SCAN:Ver", "DetectVersion: String scan -> %u (at 0x%zX)", p.value, off);
                return p.value;
            }
        }
    }

    Logger::Warn("SCAN:Ver", "DetectVersion: Could not detect UE version, defaulting to 504");
    return 504;
}

// ─────────────────────────────────────────────────────────────────────────────
// ValidateAndFixOffsets — Runtime FField/FProperty offset detection
//
// Strategy:
//   1. Find well-known UScriptStruct "Guid" (has 4 int32 fields: A,B,C,D
//      at offsets 0,4,8,12 respectively, all ElementSize=4)
//   2. Walk from UStruct base to find ChildProperties pointer
//   3. From first FField, probe for Name offset (where FName resolves to "A"/"D")
//   4. Probe for Next pointer (leads to another FField with known name)
//   5. Probe for FProperty::Offset_Internal (should be 0 for field "A", 4 for "B")
//   6. Probe for FProperty::ElementSize (should be 4 for all Guid fields)
// ─────────────────────────────────────────────────────────────────────────────

// Helper: find a UScriptStruct by name via GObjects scan
static uintptr_t FindStructByName(const char* structName) {
    int32_t count = ObjectArray::GetCount();
    for (int32_t i = 0; i < count; ++i) {
        uintptr_t obj = ObjectArray::GetByIndex(i);
        if (!obj) continue;

        // Check class name == "ScriptStruct"
        uintptr_t cls = 0;
        if (!Mem::ReadSafe(obj + Constants::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        uint32_t clsNameIdx = 0;
        if (!Mem::ReadSafe(cls + Constants::OFF_UOBJECT_NAME, clsNameIdx)) continue;
        std::string clsName = FNamePool::GetString(clsNameIdx);
        if (clsName != "ScriptStruct") continue;

        // Check object name matches
        uint32_t nameIdx = 0;
        if (!Mem::ReadSafe(obj + Constants::OFF_UOBJECT_NAME, nameIdx)) continue;
        std::string name = FNamePool::GetString(nameIdx);
        if (name == structName) {
            Logger::Info("DYNO", "FindStructByName: Found '%s' at 0x%llX (index=%d)",
                     structName, (unsigned long long)obj, i);
            return obj;
        }
    }
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────────
// DetectCasePreservingName — Measure FName size from UObject layout.
//
// Strategy (from Dumper-7 InitFNameSettings):
//   Pick any UObject* from GObjects. Read the pointer at +0x20 (candidate Outer).
//   If it's a valid pointer, FName is 8 bytes (standard), Outer=0x20.
//   If not, try +0x28. If THAT is a valid pointer (or null for Package),
//   FName is 0x10 bytes (CasePreservingName), Outer=0x28.
//
// Also checks: if the two int32s at UObject::Name (+0x18 and +0x1C) are equal,
// it's likely ComparisonIndex == DisplayIndex, confirming CPN.
// ─────────────────────────────────────────────────────────────────────────────
static void DetectCasePreservingName() {
    Logger::Info("DYNO", "DetectCasePreservingName: Probing UObject layout...");

    // Collect a few UObjects to test consensus
    int voteStandard = 0, voteCPN = 0;
    int tested = 0;

    int32_t count = ObjectArray::GetCount();
    for (int32_t i = 1; i < count && tested < 20; ++i) {
        uintptr_t obj = ObjectArray::GetByIndex(i);
        if (!obj) continue;

        // Read Class at +0x10 to confirm this is a valid UObject
        uintptr_t cls = 0;
        if (!Mem::ReadSafe(obj + Constants::OFF_UOBJECT_CLASS, cls) || !cls) continue;
        if (cls < 0x10000 || cls > 0x00007FFFFFFFFFFF) continue;

        // Read candidate Outer at standard offset 0x20
        uintptr_t outerAt20 = 0;
        Mem::ReadSafe(obj + 0x20, outerAt20);

        // Read candidate Outer at CPN offset 0x28
        uintptr_t outerAt28 = 0;
        Mem::ReadSafe(obj + 0x28, outerAt28);

        // A valid Outer is either null (Package-level objects) or a plausible user-space pointer.
        // Also: Outer must be a UObject, so its Class at +0x10 should be a valid pointer too.
        auto isValidOuter = [](uintptr_t val) -> bool {
            if (val == 0) return true; // null = root package
            if (val < 0x10000 || val > 0x00007FFFFFFFFFFF) return false;
            uintptr_t outerCls = 0;
            if (!Mem::ReadSafe(val + Constants::OFF_UOBJECT_CLASS, outerCls)) return false;
            return outerCls > 0x10000 && outerCls < 0x00007FFFFFFFFFFF;
        };

        bool at20valid = isValidOuter(outerAt20);
        bool at28valid = isValidOuter(outerAt28);

        // If +0x20 is valid and +0x28 is NOT a valid UObject pointer → standard
        // If +0x20 is NOT valid and +0x28 IS valid → CPN
        // If both valid, check ComparisonIndex vs DisplayIndex
        if (at20valid && !at28valid) {
            ++voteStandard;
        } else if (!at20valid && at28valid) {
            ++voteCPN;
        } else if (at20valid && at28valid) {
            // Ambiguous — check if CompIdx == DispIdx (CPN signature)
            uint32_t compIdx = 0, dispIdx = 0;
            Mem::ReadSafe(obj + 0x18, compIdx);
            Mem::ReadSafe(obj + 0x1C, dispIdx);
            if (compIdx == dispIdx && compIdx > 0 && compIdx < 0x00FFFFFF) {
                ++voteCPN;
            } else {
                ++voteStandard;
            }
        }
        ++tested;
    }

    Logger::Info("DYNO", "DetectCasePreservingName: votes standard=%d, CPN=%d (tested %d objects)",
             voteStandard, voteCPN, tested);

    if (voteCPN > voteStandard) {
        DynOff::bCasePreservingName = true;
        DynOff::UOBJECT_OUTER = 0x28;
        Logger::Info("DYNO", "DetectCasePreservingName: CPN ACTIVE — UObject::Outer = +0x28");
    } else {
        DynOff::bCasePreservingName = false;
        DynOff::UOBJECT_OUTER = 0x20;
        Logger::Info("DYNO", "DetectCasePreservingName: Standard FName — UObject::Outer = +0x20");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DetectUPropertyMode — Determine if this is a UE4 <4.25 game using UProperty
// (UObject-derived properties in Children chain) vs FProperty (FField-based).
//
// Primary: Use the detected UE version number (>= 425 means FProperty).
// Fallback (version unknown): Search for actual UProperty *instances* in GObjects.
//   In UE4 <4.25, property instances (e.g., "Owner" with class "ObjectProperty")
//   are UObject-derived and registered in GObjects.
//   In UE4.25+/UE5, property instances are FField-based and NOT in GObjects
//   (even though the UClass "ObjectProperty" still exists for reflection).
// ─────────────────────────────────────────────────────────────────────────────
static void DetectUPropertyMode(uint32_t ueVersion) {
    Logger::Info("DYNO", "DetectUPropertyMode: Checking for UProperty vs FProperty (UE version=%u)...", ueVersion);

    // Primary: version-based detection (most reliable)
    if (ueVersion >= 425) {
        // UE4.25 introduced FProperty/FField; all UE5 versions use it
        DynOff::bUseFProperty = true;
        Logger::Info("DYNO", "DetectUPropertyMode: FProperty mode (UE version %u >= 425)", ueVersion);
        return;
    }

    if (ueVersion > 0 && ueVersion < 425) {
        // Confirmed UE4 <4.25 — uses UProperty
        DynOff::bUseFProperty = false;
        DynOff::UFIELD_NEXT = DynOff::bCasePreservingName ? 0x30 : 0x28;
        Logger::Info("DYNO", "DetectUPropertyMode: UProperty mode (UE version %u < 425), UField::Next = +0x%02X",
                 ueVersion, DynOff::UFIELD_NEXT);
        return;
    }

    // Fallback: UE version unknown (0) — heuristic detection via GObjects.
    // Search for actual property *instances* whose class name ends with "Property".
    // In UE4 <4.25: objects like "Owner" (class=ObjectProperty) exist in GObjects.
    // In UE5: only the UClass definition "ObjectProperty" exists (class=Class), not instances.
    Logger::Info("DYNO", "DetectUPropertyMode: Version unknown — using heuristic GObjects scan");

    bool foundPropertyInstance = false;
    int32_t count = ObjectArray::GetCount();

    for (int32_t i = 0; i < count && i < 50000; ++i) {
        uintptr_t obj = ObjectArray::GetByIndex(i);
        if (!obj) continue;

        // Read this object's class
        uintptr_t cls = 0;
        if (!Mem::ReadSafe(obj + Constants::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        // Get the class name
        uint32_t clsNameIdx = 0;
        if (!Mem::ReadSafe(cls + Constants::OFF_UOBJECT_NAME, clsNameIdx)) continue;
        std::string clsName = FNamePool::GetString(clsNameIdx);

        // Skip "Class" — we don't want the UClass definition, we want instances
        if (clsName == "Class" || clsName == "ScriptStruct" || clsName == "Package" ||
            clsName == "Function" || clsName == "Enum") continue;

        // Check if class name ends with "Property" (e.g., "ObjectProperty", "IntProperty")
        if (clsName.size() > 8 && clsName.substr(clsName.size() - 8) == "Property") {
            // This is a UProperty instance — confirms UE4 <4.25 mode
            uint32_t objNameIdx = 0;
            Mem::ReadSafe(obj + Constants::OFF_UOBJECT_NAME, objNameIdx);
            std::string objName = FNamePool::GetString(objNameIdx);
            foundPropertyInstance = true;
            Logger::Info("DYNO", "DetectUPropertyMode: Found UProperty instance '%s' (class=%s) at 0x%llX",
                     objName.c_str(), clsName.c_str(), (unsigned long long)obj);
            break;
        }
    }

    if (foundPropertyInstance) {
        DynOff::bUseFProperty = false;
        DynOff::UFIELD_NEXT = DynOff::bCasePreservingName ? 0x30 : 0x28;
        Logger::Info("DYNO", "DetectUPropertyMode: UProperty mode (heuristic), UField::Next = +0x%02X",
                 DynOff::UFIELD_NEXT);
    } else {
        DynOff::bUseFProperty = true;
        Logger::Info("DYNO", "DetectUPropertyMode: FProperty mode (no UProperty instances found in GObjects)");
    }
}

bool ValidateAndFixOffsets(uint32_t ueVersion) {
    Logger::Info("DYNO", "ValidateAndFixOffsets: Starting dynamic offset detection...");

    // Step 1: Detect CasePreservingName by probing UObject layout
    DetectCasePreservingName();

    // Step 2: Detect UE4 UProperty vs FProperty mode
    DetectUPropertyMode(ueVersion);

    // Step 2.5: Set version-based defaults BEFORE probing (so if probing fails, we have sane values)
    // These serve as the fallback if Guid/Vector structs can't be found.
    if (DynOff::bUseFProperty) {
        if (ueVersion >= 501 || ueVersion == 0) {
            // UE5.1.1+ uses FFieldVariant=0x08 (smaller): Next=0x18, Name=0x20, Offset=0x44
            // Also apply for unknown version since most modern UE5 games are 5.1+
            // Note: UE5.0 and UE5.1.0 use FFieldVariant=0x10 (larger): Next=0x20, Name=0x28, Offset=0x4C
            // We default to the more common 5.1.1+ layout; probing will correct if wrong.
            if (ueVersion >= 502 || (ueVersion == 0)) {
                // UE5.2+ almost certainly uses the smaller FFieldVariant
                DynOff::FFIELD_NEXT        = 0x18;
                DynOff::FFIELD_NAME        = 0x20;
                DynOff::FPROPERTY_ELEMSIZE = 0x34;
                DynOff::FPROPERTY_FLAGS    = 0x38;
                DynOff::FPROPERTY_OFFSET   = 0x44;
                DynOff::FSTRUCTPROP_STRUCT  = 0x70;
                DynOff::FBOOLPROP_FIELDSIZE = 0x70;
                Logger::Info("DYNO", "ValidateAndFixOffsets: Set UE5.1.1+ defaults (FFieldVariant=0x08)");
            }
            // UE5.1 is ambiguous (5.1.0 = larger, 5.1.1+ = smaller), leave as-is for probing
        }
    }

    // Step 3: Find "Guid" or "Vector" struct for probing
    uintptr_t guidStruct = FindStructByName("Guid");
    uintptr_t vectorStruct = FindStructByName("Vector");

    if (!guidStruct && !vectorStruct) {
        Logger::Warn("DYNO", "ValidateAndFixOffsets: Cannot find Guid or Vector struct, using version-based defaults");
        // Still mark as validated since CPN and UProperty detection succeeded
        DynOff::bOffsetsValidated = true;
        return false;
    }

    uintptr_t testStruct = guidStruct ? guidStruct : vectorStruct;
    const char* testName = guidStruct ? "Guid" : "Vector";

    // Expected field names for each struct
    // Guid:   A, B, C, D  (offsets: 0, 4, 8, 12)
    // Vector: X, Y, Z     (offsets: 0, 4, 8) — but may be float/double
    const char* expectedFirst  = guidStruct ? "A" : "X";
    const char* expectedSecond = guidStruct ? "B" : "Y";
    int expectedElemSize     = 4;

    Logger::Info("DYNO", "ValidateAndFixOffsets: Using struct '%s' at 0x%llX", testName, (unsigned long long)testStruct);

    // Step 4: Find ChildProperties (or Children for UE4 UProperty mode)
    uintptr_t childProps = 0;
    int childPropsOff = -1;

    // For UE4 UProperty mode, the chain is in UStruct::Children and items are UObject-derived.
    // For FProperty mode, the chain is in UStruct::ChildProperties and items are FField-based.

    // Probe offsets 0x38..0x80 in 8-byte steps for a valid chain head pointer
    for (int off = 0x38; off <= 0x80; off += 8) {
        uintptr_t ptr = 0;
        if (!Mem::ReadSafe(testStruct + off, ptr) || !ptr) continue;

        // Basic pointer validity: must be in user space
        if (ptr < 0x10000 || ptr > 0x00007FFFFFFFFFFF) continue;

        if (DynOff::bUseFProperty) {
            // FProperty mode: check if this pointer has an FFieldClass* at +0x08
            uintptr_t fieldClass = 0;
            if (!Mem::ReadSafe(ptr + 0x08, fieldClass) || !fieldClass) continue;
            if (fieldClass < 0x10000 || fieldClass > 0x00007FFFFFFFFFFF) continue;

            // The FFieldClass should have an FName that resolves to a *Property type name
            uint32_t fcNameIdx = 0;
            if (!Mem::ReadSafe(fieldClass, fcNameIdx)) continue;
            std::string fcName = FNamePool::GetString(fcNameIdx);
            if (fcName.find("Property") != std::string::npos) {
                childProps = ptr;
                childPropsOff = off;
                Logger::Info("DYNO", "ValidateAndFixOffsets: ChildProperties found at struct+0x%02X → 0x%llX (FFieldClass='%s')",
                         off, (unsigned long long)ptr, fcName.c_str());
                break;
            }
        } else {
            // UProperty mode: items are UObjects. Check if Class at +0x10 resolves to a *Property class.
            uintptr_t cls = 0;
            if (!Mem::ReadSafe(ptr + Constants::OFF_UOBJECT_CLASS, cls) || !cls) continue;
            if (cls < 0x10000 || cls > 0x00007FFFFFFFFFFF) continue;

            uint32_t clsNameIdx = 0;
            if (!Mem::ReadSafe(cls + Constants::OFF_UOBJECT_NAME, clsNameIdx)) continue;
            std::string clsName = FNamePool::GetString(clsNameIdx);
            if (clsName.find("Property") != std::string::npos) {
                childProps = ptr;
                childPropsOff = off;
                Logger::Info("DYNO", "ValidateAndFixOffsets: Children (UProperty) found at struct+0x%02X → 0x%llX (Class='%s')",
                         off, (unsigned long long)ptr, clsName.c_str());
                break;
            }
        }
    }

    if (!childProps) {
        Logger::Warn("DYNO", "ValidateAndFixOffsets: Cannot find ChildProperties in '%s', keeping defaults", testName);
        DynOff::bOffsetsValidated = true;
        return false;
    }

    // Update ChildProperties offset
    if (DynOff::bUseFProperty) {
        DynOff::USTRUCT_CHILDPROPS = childPropsOff;
    } else {
        // In UE4 UProperty mode, the chain is in Children
        DynOff::USTRUCT_CHILDREN = childPropsOff;
    }

    // Step 5: Find Name offset on the first chain item
    int nameOff = -1;

    if (DynOff::bUseFProperty) {
        // FProperty: Probe 4-byte aligned offsets from 0x18 to 0x48 on the first FField
        for (int off = 0x18; off <= 0x48; off += 4) {
            uint32_t nameIdx = 0;
            if (!Mem::ReadSafe(childProps + off, nameIdx)) continue;
            if (nameIdx == 0 || nameIdx > 0x00FFFFFF) continue;

            std::string name = FNamePool::GetString(nameIdx);
            if (name == expectedFirst || name == expectedSecond) {
                nameOff = off;
                Logger::Info("DYNO", "ValidateAndFixOffsets: FField::Name at FField+0x%02X (resolved='%s')",
                         off, name.c_str());
                break;
            }
        }

        if (nameOff < 0) {
            Logger::Warn("DYNO", "ValidateAndFixOffsets: Cannot find FField::Name, keeping default 0x%02X",
                     DynOff::FFIELD_NAME);
        } else {
            DynOff::FFIELD_NAME = nameOff;
        }
    } else {
        // UProperty (UObject-derived): Name is at UObject::Name = 0x18 (always stable)
        nameOff = Constants::OFF_UOBJECT_NAME;
        Logger::Info("DYNO", "ValidateAndFixOffsets: UProperty::Name at UObject+0x%02X (standard)", nameOff);
    }

    // Step 6: Find Next offset on the chain
    int nextOff = -1;

    if (DynOff::bUseFProperty) {
        // FField::Next: Probe 8-byte aligned offsets 0x10..0x38
        for (int off = 0x10; off <= 0x38; off += 8) {
            if (off == DynOff::FFIELD_CLASS) continue; // Skip the Class pointer

            uintptr_t nextPtr = 0;
            if (!Mem::ReadSafe(childProps + off, nextPtr) || !nextPtr) continue;
            if (nextPtr < 0x10000 || nextPtr > 0x00007FFFFFFFFFFF) continue;

            // Verify it looks like an FField: check FFieldClass at +0x08
            uintptr_t nextFieldClass = 0;
            if (!Mem::ReadSafe(nextPtr + DynOff::FFIELD_CLASS, nextFieldClass) || !nextFieldClass) continue;

            uint32_t fcNameIdx2 = 0;
            if (!Mem::ReadSafe(nextFieldClass, fcNameIdx2)) continue;
            std::string fcName2 = FNamePool::GetString(fcNameIdx2);
            if (fcName2.find("Property") == std::string::npos) continue;

            // Double-check: read FName at the detected Name offset on the next field
            if (nameOff >= 0) {
                uint32_t nextNameIdx = 0;
                if (Mem::ReadSafe(nextPtr + nameOff, nextNameIdx) && nextNameIdx > 0) {
                    std::string nextName = FNamePool::GetString(nextNameIdx);
                    if (!nextName.empty() && nextName.length() <= 64) {
                        nextOff = off;
                        Logger::Info("DYNO", "ValidateAndFixOffsets: FField::Next at FField+0x%02X (next='%s')",
                                 off, nextName.c_str());
                        break;
                    }
                }
            } else {
                nextOff = off;
                Logger::Info("DYNO", "ValidateAndFixOffsets: FField::Next at FField+0x%02X (unverified name)", off);
                break;
            }
        }

        if (nextOff < 0) {
            Logger::Warn("DYNO", "ValidateAndFixOffsets: Cannot find FField::Next, keeping default 0x%02X",
                     DynOff::FFIELD_NEXT);
        } else {
            DynOff::FFIELD_NEXT = nextOff;
        }
    } else {
        // UProperty: Next is UField::Next (0x28 standard, 0x30 for CPN)
        nextOff = DynOff::UFIELD_NEXT;

        // Verify: the pointer at childProps + nextOff should be another UObject (or null for last)
        uintptr_t nextPtr = 0;
        Mem::ReadSafe(childProps + nextOff, nextPtr);
        if (nextPtr) {
            uintptr_t nextCls = 0;
            if (Mem::ReadSafe(nextPtr + Constants::OFF_UOBJECT_CLASS, nextCls) && nextCls > 0x10000) {
                Logger::Info("DYNO", "ValidateAndFixOffsets: UField::Next at UObject+0x%02X verified", nextOff);
            }
        }
    }

    // Step 7: Collect fields from the chain for offset probing
    struct { uintptr_t addr; std::string name; int expectedOffset; } fields[4] = {};
    int fieldCount = 0;

    uintptr_t curField = childProps;
    for (int i = 0; i < 4 && curField && fieldCount < 4; ++i) {
        fields[fieldCount].addr = curField;
        if (nameOff >= 0) {
            uint32_t ni = 0;
            Mem::ReadSafe(curField + nameOff, ni);
            fields[fieldCount].name = FNamePool::GetString(ni);
        }

        const auto& fn = fields[fieldCount].name;
        if (fn == "A" || fn == "X") fields[fieldCount].expectedOffset = 0;
        else if (fn == "B" || fn == "Y") fields[fieldCount].expectedOffset = 4;
        else if (fn == "C" || fn == "Z") fields[fieldCount].expectedOffset = 8;
        else if (fn == "D") fields[fieldCount].expectedOffset = 12;
        else fields[fieldCount].expectedOffset = -1;

        ++fieldCount;

        if (nextOff >= 0) {
            uintptr_t next = 0;
            Mem::ReadSafe(curField + nextOff, next);
            curField = next;
        } else {
            break;
        }
    }

    Logger::Info("DYNO", "ValidateAndFixOffsets: Collected %d fields from '%s' chain", fieldCount, testName);
    for (int i = 0; i < fieldCount; ++i) {
        Logger::Debug("DYNO", "  Field[%d]: '%s' at 0x%llX, expectedOff=%d",
                  i, fields[i].name.c_str(), (unsigned long long)fields[i].addr, fields[i].expectedOffset);
    }

    // Step 8: Probe for Offset_Internal: scan 4-byte aligned offsets
    // Range depends on mode: FProperty starts after FField header (~0x30-0x68),
    // UProperty starts after UField header (~0x30-0x60)
    int probeStart = DynOff::bUseFProperty ? 0x30 : 0x28;
    int probeEnd   = DynOff::bUseFProperty ? 0x68 : 0x60;
    int propOffsetOff = -1;
    int propElemSizeOff = -1;

    for (int probe = probeStart; probe <= probeEnd; probe += 4) {
        int matches = 0;
        int sizeMatches = 0;

        for (int i = 0; i < fieldCount; ++i) {
            if (fields[i].expectedOffset < 0) continue;

            int32_t val = -1;
            if (Mem::ReadSafe(fields[i].addr + probe, val) && val == fields[i].expectedOffset) {
                ++matches;
            }

            int32_t sz = -1;
            if (Mem::ReadSafe(fields[i].addr + probe, sz) && sz == expectedElemSize) {
                ++sizeMatches;
            }
        }

        if (matches >= 2 && propOffsetOff < 0) {
            propOffsetOff = probe;
            Logger::Info("DYNO", "ValidateAndFixOffsets: Offset_Internal at +0x%02X (%d matches)", probe, matches);
        }

        if (sizeMatches >= 2 && propElemSizeOff < 0 && probe != propOffsetOff) {
            propElemSizeOff = probe;
            Logger::Info("DYNO", "ValidateAndFixOffsets: ElementSize at +0x%02X (%d matches)", probe, sizeMatches);
        }
    }

    if (propOffsetOff >= 0) {
        if (DynOff::bUseFProperty) {
            DynOff::FPROPERTY_OFFSET = propOffsetOff;
        } else {
            DynOff::UPROPERTY_OFFSET = propOffsetOff;
        }
    } else {
        Logger::Warn("DYNO", "ValidateAndFixOffsets: Cannot find Offset_Internal, keeping defaults");
    }

    if (propElemSizeOff < 0 && propOffsetOff > 0) {
        // Heuristic: ElementSize is usually 0x14 bytes before Offset_Internal
        int guess = propOffsetOff - 0x14;
        if (guess >= probeStart) {
            int32_t val = 0;
            if (Mem::ReadSafe(childProps + guess, val) && val == expectedElemSize) {
                propElemSizeOff = guess;
                Logger::Info("DYNO", "ValidateAndFixOffsets: ElementSize (heuristic) at +0x%02X", guess);
            }
        }
    }

    if (propElemSizeOff >= 0) {
        if (DynOff::bUseFProperty) {
            DynOff::FPROPERTY_ELEMSIZE = propElemSizeOff;
        } else {
            DynOff::UPROPERTY_ELEMSIZE = propElemSizeOff;
        }
    }

    // Step 9: Derive remaining offsets
    // PropertyFlags: ElementSize + 8 (ArrayDim int32 fills the gap)
    if (DynOff::bUseFProperty) {
        if (propElemSizeOff >= 0) {
            DynOff::FPROPERTY_FLAGS = propElemSizeOff + 8;
        }
    } else {
        if (propElemSizeOff >= 0) {
            DynOff::UPROPERTY_FLAGS = propElemSizeOff + 8;
        }
    }

    // UStruct offsets derived from ChildProperties position
    if (DynOff::bUseFProperty) {
        DynOff::USTRUCT_PROPSSIZE = childPropsOff + 8;
        DynOff::USTRUCT_CHILDREN  = childPropsOff - 8;
        DynOff::USTRUCT_SUPER     = childPropsOff - 0x10;
    } else {
        // UE4 UProperty mode: Children is the chain itself
        DynOff::USTRUCT_SUPER     = childPropsOff - 8;
        DynOff::USTRUCT_PROPSSIZE = childPropsOff + 8;
    }

    Logger::Info("DYNO", "ValidateAndFixOffsets: UStruct::SuperStruct at +0x%02X", DynOff::USTRUCT_SUPER);

    // FStructProperty::Struct = Offset_Internal + 0x2C
    if (DynOff::bUseFProperty && propOffsetOff >= 0) {
        DynOff::FSTRUCTPROP_STRUCT  = propOffsetOff + 0x2C;
        DynOff::FBOOLPROP_FIELDSIZE = DynOff::FSTRUCTPROP_STRUCT;
    }

    DynOff::bOffsetsValidated = true;

    // Summary log
    Logger::Info("DYNO", "=== Dynamic Offset Summary ===");
    Logger::Info("DYNO", "  CasePreservingName: %s", DynOff::bCasePreservingName ? "YES" : "no");
    Logger::Info("DYNO", "  UseFProperty:       %s", DynOff::bUseFProperty ? "yes (UE4.25+/UE5)" : "NO (UE4 UProperty)");
    Logger::Info("DYNO", "  UObject::Outer      = +0x%02X", DynOff::UOBJECT_OUTER);
    Logger::Info("DYNO", "  UStruct::Super      = +0x%02X", DynOff::USTRUCT_SUPER);
    Logger::Info("DYNO", "  UStruct::Children   = +0x%02X", DynOff::USTRUCT_CHILDREN);
    Logger::Info("DYNO", "  UStruct::ChildProps = +0x%02X", DynOff::USTRUCT_CHILDPROPS);
    Logger::Info("DYNO", "  UStruct::PropsSize  = +0x%02X", DynOff::USTRUCT_PROPSSIZE);
    if (DynOff::bUseFProperty) {
        Logger::Info("DYNO", "  FField::Class       = +0x%02X", DynOff::FFIELD_CLASS);
        Logger::Info("DYNO", "  FField::Next        = +0x%02X", DynOff::FFIELD_NEXT);
        Logger::Info("DYNO", "  FField::Name        = +0x%02X", DynOff::FFIELD_NAME);
        Logger::Info("DYNO", "  FProperty::ElemSize = +0x%02X", DynOff::FPROPERTY_ELEMSIZE);
        Logger::Info("DYNO", "  FProperty::Flags    = +0x%02X", DynOff::FPROPERTY_FLAGS);
        Logger::Info("DYNO", "  FProperty::Offset   = +0x%02X", DynOff::FPROPERTY_OFFSET);
        Logger::Info("DYNO", "  FStructProp::Struct = +0x%02X", DynOff::FSTRUCTPROP_STRUCT);
    } else {
        Logger::Info("DYNO", "  UField::Next        = +0x%02X", DynOff::UFIELD_NEXT);
        Logger::Info("DYNO", "  UProperty::ElemSize = +0x%02X", DynOff::UPROPERTY_ELEMSIZE);
        Logger::Info("DYNO", "  UProperty::Flags    = +0x%02X", DynOff::UPROPERTY_FLAGS);
        Logger::Info("DYNO", "  UProperty::Offset   = +0x%02X", DynOff::UPROPERTY_OFFSET);
    }
    Logger::Info("DYNO", "==============================");

    return true;
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
