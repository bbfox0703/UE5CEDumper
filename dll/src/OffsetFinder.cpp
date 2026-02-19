// ============================================================
// OffsetFinder.cpp — AOB scanning for GObjects/GNames/GWorld
// ============================================================

#include "OffsetFinder.h"
#include "Memory.h"
#include "Logger.h"
#include "Constants.h"
#include "ObjectArray.h"
#include "FNamePool.h"

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

// Validate a candidate GObjects address (basic: check NumElements range)
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
        LOG_DEBUG("ValidateGNamesStructural: FRWLock=0x%llX (non-zero) at 0x%llX",
                  (unsigned long long)rwLock, (unsigned long long)addr);
        return false;
    }

    // CurrentBlock at +0x08
    int32_t currentBlock = 0;
    if (!Mem::ReadSafe(addr + 0x08, currentBlock)) return false;
    if (currentBlock < 0 || currentBlock > 8192) {
        LOG_DEBUG("ValidateGNamesStructural: CurrentBlock=%d out of range at 0x%llX",
                  currentBlock, (unsigned long long)addr);
        return false;
    }

    // Blocks[CurrentBlock+1] should be NULL (end sentinel)
    uintptr_t nextBlock = 0;
    if (!Mem::ReadSafe(addr + 0x10 + ((currentBlock + 1) * 8), nextBlock)) return false;
    if (nextBlock != 0) {
        LOG_DEBUG("ValidateGNamesStructural: Blocks[%d+1] = 0x%llX (non-null) at 0x%llX",
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
            LOG_DEBUG("ValidateGNamesStructural: Blocks[0] doesn't start with 'None' at 0x%llX",
                      (unsigned long long)addr);
            return false;
        }
    }

    LOG_INFO("ValidateGNamesStructural: Valid FNamePool at 0x%llX (CurrentBlock=%d)",
             (unsigned long long)addr, currentBlock);
    return true;
}

// ─────────────────────────────────────────────────────────────────────────────
// FindGObjectsByDataScan — fallback: collect ALL RIP-relative pointer
// references from .text that resolve into the data section, then validate
// each candidate as a GObjects/FUObjectArray.
// ─────────────────────────────────────────────────────────────────────────────
static uintptr_t FindGObjectsByDataScan() {
    LOG_INFO("FindGObjectsByDataScan: Collecting static pointer references...");

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
        LOG_WARN("FindGObjectsByDataScan: Could not identify code/data sections");
        return 0;
    }

    LOG_DEBUG("FindGObjectsByDataScan: code=[0x%llX-0x%llX], data=[0x%llX-0x%llX]",
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

    LOG_INFO("FindGObjectsByDataScan: Found %zu static pointers in data section", bag.size());

    // Try each candidate with GObjects validation
    for (auto& sp : bag) {
        uintptr_t candidate = 0;
        if (!Mem::ReadSafe(sp.targetAddr, candidate) || !candidate) continue;
        if (ValidateGObjects(candidate)) {
            LOG_INFO("FindGObjectsByDataScan: GObjects validated at 0x%llX (via instr@0x%llX)",
                     (unsigned long long)candidate, (unsigned long long)sp.instrAddr);
            return candidate;
        }
    }

    LOG_WARN("FindGObjectsByDataScan: No valid GObjects found among %zu candidates", bag.size());
    return 0;
}

uintptr_t FindGObjects() {
    LOG_INFO("FindGObjects: Scanning for GObjects...");

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
        // Retry with extra deref for pointer-to-pointer layouts
        { Constants::AOB_GOBJECTS_V2, true  },
        { Constants::AOB_GOBJECTS_V1, true  },
        { Constants::AOB_GOBJECTS_V6, true  },
        { Constants::AOB_GOBJECTS_V7, true  },
        { Constants::AOB_GOBJECTS_V8, true  },
        { Constants::AOB_GOBJECTS_V9, true  },
    };

    for (auto& c : candidates) {
        uintptr_t result = TryPatternRIP(c.pattern, 3, 7, c.deref);
        if (result && ValidateGObjects(result)) return result;
    }

    // Fallback: exhaustive data-section pointer scan
    LOG_WARN("FindGObjects: All patterns failed, trying data-section scan fallback...");
    {
        uintptr_t result = FindGObjectsByDataScan();
        if (result) return result;
    }

    LOG_ERROR("FindGObjects: All patterns and fallback scan failed");
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
    LOG_INFO("FindGNamesByPointerScan: Scanning .data for pointer-to-'None' FNameEntry...");

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
        LOG_DEBUG("FindGNamesByPointerScan: Scanning section [%s] at 0x%llX (%zu bytes)",
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
                                LOG_WARN("FindGNamesByPointerScan: NEAR-MISS 'None' at ptr=0x%llX offset=%d "
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

            LOG_INFO("FindGNamesByPointerScan: chunk0=0x%llX @ 0x%llX — trying pool offsets...",
                     (unsigned long long)ptr, (unsigned long long)pAddr);

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
                LOG_DEBUG("FindGNamesByPointerScan: candidate chunk0 bytes: %s", hexbuf);
                ++diagCount;
            }

            // Try common offsets of Blocks[0] within FNamePool:
            //   0x10 = standard UE5 (FRWLock[8] + CurrentBlock[4] + Cursor[4])
            //   0x00, 0x08, 0x18, 0x20, 0x28 = observed variants
            for (int blkOff : { 0x10, 0x00, 0x08, 0x18, 0x20, 0x28 }) {
                if ((size_t)blkOff > pAddr) continue; // underflow guard
                uintptr_t pool = pAddr - static_cast<uintptr_t>(blkOff);
                if (ValidateGNames(pool) || ValidateGNamesStructural(pool)) {
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
    };

    for (auto& c : candidates) {
        uintptr_t result = TryPatternRIP(c.pattern, 3, 7, c.deref);
        if (!result) continue;
        // Use both original ValidateGNames (checks "None" entry) and
        // structural validation (FRWLock, CurrentBlock, Blocks sentinel).
        // Accept if either validates — they check complementary properties.
        if (ValidateGNames(result) || ValidateGNamesStructural(result)) return result;
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
            LOG_INFO("FindStructByName: Found '%s' at 0x%llX (index=%d)",
                     structName, (unsigned long long)obj, i);
            return obj;
        }
    }
    return 0;
}

bool ValidateAndFixOffsets() {
    LOG_INFO("ValidateAndFixOffsets: Starting dynamic offset detection...");

    // Step 1: Detect CasePreservingName by checking FName size in UObject
    // UObject layout: Name at 0x18, Outer at 0x20 (standard).
    // If FName is 8 bytes: 0x20 - 0x18 = 8 → standard
    // If FName is 0x10 bytes: the gap would be larger, meaning Outer shifts.
    // But actually, UObject offsets are compile-time fixed. The CasePreservingName
    // flag affects FName *contents* (adds DisplayIndex), not UObject field positions.
    // The effect is on FField layout — FField::Flags shifts because FName after Name is bigger.
    //
    // We detect by probing empirically on known structs.

    // Step 2: Find "Guid" struct
    uintptr_t guidStruct = FindStructByName("Guid");
    uintptr_t vectorStruct = FindStructByName("Vector");

    if (!guidStruct && !vectorStruct) {
        LOG_WARN("ValidateAndFixOffsets: Cannot find Guid or Vector struct, keeping defaults");
        return false;
    }

    uintptr_t testStruct = guidStruct ? guidStruct : vectorStruct;
    const char* testName = guidStruct ? "Guid" : "Vector";

    // Expected field names for each struct
    // Guid:   A, B, C, D  (offsets: 0, 4, 8, 12)
    // Vector: X, Y, Z     (offsets: 0, 4, 8) — but may be float (ElementSize=4)
    const char* expectedFirst  = guidStruct ? "A" : "X";
    const char* expectedSecond = guidStruct ? "B" : "Y";
    int expectedElemSize     = 4;

    LOG_INFO("ValidateAndFixOffsets: Using struct '%s' at 0x%llX", testName, (unsigned long long)testStruct);

    // Step 3: Find ChildProperties pointer
    // Probe offsets 0x38..0x80 in 8-byte steps for a valid FField* pointer
    uintptr_t childProps = 0;
    int childPropsOff = -1;

    for (int off = 0x38; off <= 0x80; off += 8) {
        uintptr_t ptr = 0;
        if (!Mem::ReadSafe(testStruct + off, ptr) || !ptr) continue;

        // Basic pointer validity: must be in user space, non-module
        if (ptr < 0x10000 || ptr > 0x00007FFFFFFFFFFF) continue;

        // Check if this pointer has an FFieldClass* at the known offset (0x08)
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
            LOG_INFO("ValidateAndFixOffsets: ChildProperties found at struct+0x%02X → 0x%llX (FFieldClass='%s')",
                     off, (unsigned long long)ptr, fcName.c_str());
            break;
        }
    }

    if (!childProps) {
        LOG_WARN("ValidateAndFixOffsets: Cannot find ChildProperties in '%s', keeping defaults", testName);
        return false;
    }

    // Update ChildProperties offset
    DynOff::USTRUCT_CHILDPROPS = childPropsOff;

    // Also find SuperStruct: probe 0x30..childPropsOff-8 for a pointer to another UStruct
    // (Guid's super is probably UStruct itself or none)
    // We'll keep the default 0x40 unless we find evidence otherwise.

    // Step 4: Find FField::Name offset
    // Probe 4-byte aligned offsets from 0x18 to 0x48 on the first FField
    int nameOff = -1;
    for (int off = 0x18; off <= 0x48; off += 4) {
        uint32_t nameIdx = 0;
        if (!Mem::ReadSafe(childProps + off, nameIdx)) continue;
        if (nameIdx == 0 || nameIdx > 0x00FFFFFF) continue; // Sanity: reasonable FName index

        std::string name = FNamePool::GetString(nameIdx);
        if (name == expectedFirst || name == expectedSecond) {
            nameOff = off;
            LOG_INFO("ValidateAndFixOffsets: FField::Name at FField+0x%02X (resolved='%s')",
                     off, name.c_str());
            break;
        }
    }

    if (nameOff < 0) {
        LOG_WARN("ValidateAndFixOffsets: Cannot find FField::Name, keeping default 0x%02X",
                 DynOff::FFIELD_NAME);
    } else {
        DynOff::FFIELD_NAME = nameOff;
    }

    // Detect CasePreservingName: if Name offset is still 0x28 but there's a DisplayIndex
    // at 0x2C that equals ComparisonIndex, then CasePreservingName is true.
    if (nameOff == 0x28) {
        uint32_t compIdx = 0, dispIdx = 0;
        Mem::ReadSafe(childProps + 0x28, compIdx);
        Mem::ReadSafe(childProps + 0x2C, dispIdx);
        if (compIdx == dispIdx && compIdx != 0) {
            DynOff::bCasePreservingName = true;
            LOG_INFO("ValidateAndFixOffsets: CasePreservingName detected (CompIdx==DispIdx=0x%X)", compIdx);
        }
    } else if (nameOff > 0x28) {
        // Name shifted → likely due to larger FFieldVariant or other layout change
        LOG_INFO("ValidateAndFixOffsets: FField::Name at 0x%02X (shifted from default 0x28)", nameOff);
    }

    // Step 5: Find FField::Next offset
    // Probe 8-byte aligned offsets 0x10..0x38 for a pointer to the next FField
    // The next field should also have an FFieldClass at +0x08 with a "Property" name
    int nextOff = -1;
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
                    LOG_INFO("ValidateAndFixOffsets: FField::Next at FField+0x%02X (next='%s')",
                             off, nextName.c_str());
                    break;
                }
            }
        } else {
            // Can't verify name, just accept it has valid FFieldClass
            nextOff = off;
            LOG_INFO("ValidateAndFixOffsets: FField::Next at FField+0x%02X (unverified name)", off);
            break;
        }
    }

    if (nextOff < 0) {
        LOG_WARN("ValidateAndFixOffsets: Cannot find FField::Next, keeping default 0x%02X",
                 DynOff::FFIELD_NEXT);
    } else {
        DynOff::FFIELD_NEXT = nextOff;
    }

    // Step 6: Find FProperty::Offset_Internal
    // Walk the chain to find a field we know the struct offset of.
    // For Guid: A=0, B=4, C=8, D=12.  For Vector: X=0, Y=4, Z=8.
    // We use the second field (B or Y) which should have Offset_Internal=4.
    uintptr_t secondField = 0;
    if (nextOff >= 0) {
        Mem::ReadSafe(childProps + nextOff, secondField);
    }

    // Also try to collect the first and second field names
    struct { uintptr_t addr; std::string name; int expectedOffset; } fields[4] = {};
    int fieldCount = 0;

    // Walk the chain
    uintptr_t curField = childProps;
    for (int i = 0; i < 4 && curField && fieldCount < 4; ++i) {
        fields[fieldCount].addr = curField;
        if (nameOff >= 0) {
            uint32_t ni = 0;
            Mem::ReadSafe(curField + nameOff, ni);
            fields[fieldCount].name = FNamePool::GetString(ni);
        }

        // Map known names to expected offsets
        const auto& fn = fields[fieldCount].name;
        if (fn == "A" || fn == "X") fields[fieldCount].expectedOffset = 0;
        else if (fn == "B" || fn == "Y") fields[fieldCount].expectedOffset = 4;
        else if (fn == "C" || fn == "Z") fields[fieldCount].expectedOffset = 8;
        else if (fn == "D") fields[fieldCount].expectedOffset = 12;
        else fields[fieldCount].expectedOffset = -1;

        ++fieldCount;

        // Advance to next
        if (nextOff >= 0) {
            uintptr_t next = 0;
            Mem::ReadSafe(curField + nextOff, next);
            curField = next;
        } else {
            break;
        }
    }

    LOG_INFO("ValidateAndFixOffsets: Collected %d fields from '%s' chain", fieldCount, testName);
    for (int i = 0; i < fieldCount; ++i) {
        LOG_DEBUG("  Field[%d]: '%s' at 0x%llX, expectedOff=%d",
                  i, fields[i].name.c_str(), (unsigned long long)fields[i].addr, fields[i].expectedOffset);
    }

    // Probe for Offset_Internal: scan 4-byte aligned offsets from 0x38 to 0x68
    // For a field with expectedOffset >= 0, find where int32 matches
    int propOffsetOff = -1;
    int propElemSizeOff = -1;

    for (int probe = 0x38; probe <= 0x68; probe += 4) {
        int matches = 0;
        int sizeMatches = 0;

        for (int i = 0; i < fieldCount; ++i) {
            if (fields[i].expectedOffset < 0) continue;

            int32_t val = -1;
            if (Mem::ReadSafe(fields[i].addr + probe, val) && val == fields[i].expectedOffset) {
                ++matches;
            }

            // Also check if this could be ElementSize (all should be 4)
            int32_t sz = -1;
            if (Mem::ReadSafe(fields[i].addr + probe, sz) && sz == expectedElemSize) {
                ++sizeMatches;
            }
        }

        if (matches >= 2 && propOffsetOff < 0) {
            propOffsetOff = probe;
            LOG_INFO("ValidateAndFixOffsets: FProperty::Offset_Internal at FField+0x%02X (%d matches)",
                     probe, matches);
        }

        // ElementSize: all fields should have size == 4, but only count if different from Offset probe
        if (sizeMatches >= 2 && propElemSizeOff < 0 && probe != propOffsetOff) {
            propElemSizeOff = probe;
            LOG_INFO("ValidateAndFixOffsets: FProperty::ElementSize at FField+0x%02X (%d matches)",
                     probe, sizeMatches);
        }
    }

    if (propOffsetOff < 0) {
        LOG_WARN("ValidateAndFixOffsets: Cannot find FProperty::Offset_Internal, keeping default 0x%02X",
                 DynOff::FPROPERTY_OFFSET);
    } else {
        DynOff::FPROPERTY_OFFSET = propOffsetOff;
    }

    if (propElemSizeOff < 0) {
        // ElementSize is harder to detect uniquely because it equals the same value as offset
        // for field "B" (both are 4). Use a heuristic: it's usually 0x14 bytes before Offset_Internal
        // in the standard layout (0x38 vs 0x4C), so try propOffsetOff - 0x14
        if (propOffsetOff > 0) {
            int guess = propOffsetOff - 0x14;
            if (guess >= 0x30) {
                int32_t val = 0;
                if (Mem::ReadSafe(childProps + guess, val) && val == expectedElemSize) {
                    propElemSizeOff = guess;
                    LOG_INFO("ValidateAndFixOffsets: FProperty::ElementSize (heuristic) at FField+0x%02X", guess);
                }
            }
        }
        if (propElemSizeOff < 0) {
            LOG_WARN("ValidateAndFixOffsets: Cannot find FProperty::ElementSize, keeping default 0x%02X",
                     DynOff::FPROPERTY_ELEMSIZE);
        }
    }

    if (propElemSizeOff >= 0) {
        DynOff::FPROPERTY_ELEMSIZE = propElemSizeOff;
    }

    // Step 7: Derive other offsets
    // FProperty::PropertyFlags is typically right after ElementSize + ArrayDim (int32)
    // Standard: ElementSize=0x38, ArrayDim=0x3C, PropertyFlags=0x40
    // The gap ElementSize→PropertyFlags is usually 8 bytes (ArrayDim fills the gap)
    if (propElemSizeOff >= 0) {
        DynOff::FPROPERTY_FLAGS = propElemSizeOff + 8;
        LOG_INFO("ValidateAndFixOffsets: FProperty::PropertyFlags (derived) at FField+0x%02X",
                 DynOff::FPROPERTY_FLAGS);
    }

    // UStruct::PropertiesSize: typically ChildProperties + 8
    DynOff::USTRUCT_PROPSSIZE = childPropsOff + 8;
    LOG_INFO("ValidateAndFixOffsets: UStruct::PropertiesSize (derived) at UStruct+0x%02X",
             DynOff::USTRUCT_PROPSSIZE);

    // UStruct::Children (UField* chain for functions): typically ChildProperties - 8
    DynOff::USTRUCT_CHILDREN = childPropsOff - 8;
    LOG_INFO("ValidateAndFixOffsets: UStruct::Children (derived) at UStruct+0x%02X",
             DynOff::USTRUCT_CHILDREN);

    // UStruct::SuperStruct: typically 0x10 before ChildProperties
    // But probe to be safe — it should be a pointer to another UStruct (or null for root structs)
    // For "Guid" struct, SuperStruct is probably null. Let's check the default 0x40.
    // If ChildProperties moved, SuperStruct likely moved proportionally.
    int superOff = childPropsOff - 0x10; // Default gap
    uintptr_t superVal = 0;
    Mem::ReadSafe(testStruct + superOff, superVal);
    // Guid's super might be null, which is fine
    DynOff::USTRUCT_SUPER = superOff;
    LOG_INFO("ValidateAndFixOffsets: UStruct::SuperStruct (derived) at UStruct+0x%02X (value=0x%llX)",
             superOff, (unsigned long long)superVal);

    DynOff::bOffsetsValidated = true;

    // Summary log
    LOG_INFO("=== Dynamic Offset Summary ===");
    LOG_INFO("  CasePreservingName: %s", DynOff::bCasePreservingName ? "YES" : "no");
    LOG_INFO("  UStruct::Super      = +0x%02X", DynOff::USTRUCT_SUPER);
    LOG_INFO("  UStruct::Children   = +0x%02X", DynOff::USTRUCT_CHILDREN);
    LOG_INFO("  UStruct::ChildProps = +0x%02X", DynOff::USTRUCT_CHILDPROPS);
    LOG_INFO("  UStruct::PropsSize  = +0x%02X", DynOff::USTRUCT_PROPSSIZE);
    LOG_INFO("  FField::Class       = +0x%02X", DynOff::FFIELD_CLASS);
    LOG_INFO("  FField::Next        = +0x%02X", DynOff::FFIELD_NEXT);
    LOG_INFO("  FField::Name        = +0x%02X", DynOff::FFIELD_NAME);
    LOG_INFO("  FProperty::ElemSize = +0x%02X", DynOff::FPROPERTY_ELEMSIZE);
    LOG_INFO("  FProperty::Flags    = +0x%02X", DynOff::FPROPERTY_FLAGS);
    LOG_INFO("  FProperty::Offset   = +0x%02X", DynOff::FPROPERTY_OFFSET);
    LOG_INFO("==============================");

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
