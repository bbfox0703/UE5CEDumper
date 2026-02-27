// ============================================================
// OffsetFinder.cpp — AOB scanning for GObjects/GNames/GWorld
// ============================================================

#include "OffsetFinder.h"
#include "Memory.h"
#define LOG_CAT "SCAN"
#include "Logger.h"
#include "Constants.h"
#include "Signatures.h"
#include "ObjectArray.h"
#include "FNamePool.h"

#include <string>
#include <cstring>
#include <vector>
#include <algorithm>  // std::sort
#include <Winver.h>   // GetFileVersionInfoW / VerQueryValueW
#include <Psapi.h>    // EnumProcessModules

namespace OffsetFinder {

// UE4 TNameEntryArray detection state (set by ValidateGNamesUE4, read by FindAll)
static bool g_isUE4NameArray = false;
static int  g_ue4NameStringOffset = 0x10;

// FNameEntry hash prefix size: some UE4 builds (e.g. UE4.26 / FF7Re) prepend a 4-byte
// ComparisonId/hash before the standard 2-byte header.  Layout:
//   Standard UE5:       [2B header] [string]        → headerOffset = 0
//   UE4.26 w/ hash:     [4B hash] [2B header] [string]  → headerOffset = 4
// Set by ValidateGNames when it detects the hash prefix.
static int g_fnameEntryHeaderOffset = 0;

// Validation debug log throttle — prevents scan.log rotation from massive
// false-positive output (e.g. 20K+ matches each producing 14+ debug lines).
// Reset per-pattern in ScanForTarget; validators check before logging.
static int  g_validationDbgCount = 0;
static constexpr int kMaxValidationDbgLogs = 10;

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
        DWORD count = static_cast<DWORD>((std::min)(static_cast<size_t>(cbNeeded / sizeof(HMODULE)), _countof(modules)));
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
// Helper: check if a pointer looks like a valid heap/data pointer (not code/null/low).
// Used by ValidateGObjects to reject false positives.
static bool LooksLikeDataPtr(uintptr_t ptr) {
    if (!ptr || ptr < 0x10000) return false;
    if (ptr > 0x00007FFFFFFFFFFF) return false;
    // Reject pointers inside the game module's loaded range (code/rdata/data all contiguous)
    uintptr_t modBase = Mem::GetModuleBase(nullptr);
    uintptr_t modSize = Mem::GetModuleSize(nullptr);
    if (modBase && modSize && ptr >= modBase && ptr < modBase + modSize) return false;
    return true;
}

static bool ValidateGObjects(uintptr_t addr) {
    if (!addr) return false;

    // --- Tier 1: Try all known presets with full validation ---
    // Includes chunk consistency checks and decryption support.
    struct { int objOff; int maxOff; int numOff; int maxCOff; int numCOff; const char* name; } presets[] = {
        { 0x00, 0x10, 0x14, 0x18, 0x1C, "Default" },
        { 0x10, 0x00, 0x04, 0x08, 0x0C, "Back4Blood" },
        { 0x18, 0x10, 0x00, 0x14, 0x20, "Multiversus" },
        { 0x18, 0x00, 0x14, 0x10, 0x04, "MindsEye" },
        { 0x10, 0x18, 0x1C, 0x20, 0x24, "UE4-Extended" },
    };

    for (auto& P : presets) {
        int32_t num = 0, max = 0;
        if (!Mem::ReadSafe(addr + P.numOff, num)) continue;
        if (!Mem::ReadSafe(addr + P.maxOff, max)) continue;
        if (num < 0x1000 || num > 0x400000) continue;
        if (max < num || max > 0x800000) continue;

        // Chunk consistency (if chunk offset fields are valid)
        if (P.maxCOff >= 0 && P.numCOff >= 0) {
            int32_t numC = 0, maxC = 0;
            if (!Mem::ReadSafe(addr + P.numCOff, numC)) continue;
            if (!Mem::ReadSafe(addr + P.maxCOff, maxC)) continue;
            if (numC < 1 || maxC < 1 || numC > maxC) continue;
        }

        uintptr_t objPtr = 0;
        if (!Mem::ReadSafe(addr + P.objOff, objPtr)) continue;
        objPtr = ObjectArray::DecryptObjectPtr(objPtr);

        uintptr_t chunk0 = 0;
        if (!Mem::ReadSafe(objPtr, chunk0)) continue;

        if (chunk0 == 0) {
            if (!LooksLikeDataPtr(objPtr)) continue;
        } else {
            if (!LooksLikeDataPtr(chunk0)) continue;
        }

        Logger::Info("SCAN:GObj", "ValidateGObjects: Valid at 0x%llX (preset %s, Num=%d, Max=%d, Objects=0x%llX)",
                 static_cast<unsigned long long>(addr), P.name, num, max,
                 static_cast<unsigned long long>(objPtr));
        return true;
    }

    // --- Tier 2: Relaxed fallback (prevents regression) ---
    // Only check NumElements range + Objects pointer validity.
    struct { int numOff; int objOff; const char* name; } relaxed[] = {
        { 0x14, 0x00, "A/C" },
        { 0x04, 0x10, "B"   },
        { 0x1C, 0x10, "D"   },
    };

    for (auto& L : relaxed) {
        int32_t numElements = 0;
        if (!Mem::ReadSafe(addr + L.numOff, numElements)) continue;
        if (numElements < 0x1000 || numElements > 0x400000) continue;

        uintptr_t objPtr = 0;
        if (!Mem::ReadSafe(addr + L.objOff, objPtr)) continue;
        objPtr = ObjectArray::DecryptObjectPtr(objPtr);

        uintptr_t chunk0 = 0;
        if (!Mem::ReadSafe(objPtr, chunk0)) continue;

        if (chunk0 == 0) {
            if (!LooksLikeDataPtr(objPtr)) continue;
        } else {
            if (!LooksLikeDataPtr(chunk0)) continue;
        }

        Logger::Info("SCAN:GObj", "ValidateGObjects: Valid at 0x%llX (relaxed %s, Num=%d, Objects=0x%llX, chunk0=0x%llX)",
                 static_cast<unsigned long long>(addr), L.name, numElements,
                 static_cast<unsigned long long>(objPtr), static_cast<unsigned long long>(chunk0));
        return true;
    }

    // Log failure with diagnostic info
    int32_t numA = 0, numB = 0, numD = 0;
    Mem::ReadSafe(addr + 0x14, numA);
    Mem::ReadSafe(addr + 0x04, numB);
    Mem::ReadSafe(addr + 0x1C, numD);
    Logger::Warn("SCAN:GObj", "ValidateGObjects: Failed at 0x%llX (Num@+14=%d, Num@+04=%d, Num@+1C=%d)",
             static_cast<unsigned long long>(addr), numA, numB, numD);
    return false;
}

// Forward declarations for validators used across sections
static bool CorroborateFNameChunk(uintptr_t chunkAddr);
static bool ValidateGNamesUE4(uintptr_t addr, int& outStringOffset);
static bool ValidateGNamesAny(uintptr_t addr);

// ─────────────────────────────────────────────────────────────────────────────
// Structural validation for GNames (FNamePool):
//   1. FRWLock at +0x00 should be 0 (unlocked)
//   2. CurrentBlock at +0x08 should be a small int
//   3. Blocks[CurrentBlock+1] should be NULL (no block after last used)
//   4. Blocks[0] starts with "None" FNameEntry (exact header format match)
//   5. Blocks[0] corroborated with UE type name markers
// ─────────────────────────────────────────────────────────────────────────────
static bool ValidateGNamesStructural(uintptr_t addr) {
    if (!addr) return false;

    // FRWLock at +0x00 should be 0 when not locked
    uint64_t rwLock = 0;
    if (!Mem::ReadSafe(addr, rwLock)) return false;
    if (rwLock != 0) {
        if (g_validationDbgCount++ < kMaxValidationDbgLogs)
            Logger::Debug("SCAN:GNam", "ValidateGNamesStructural: FRWLock=0x%llX (non-zero) at 0x%llX",
                      (unsigned long long)rwLock, (unsigned long long)addr);
        return false;
    }

    // CurrentBlock at +0x08
    int32_t currentBlock = 0;
    if (!Mem::ReadSafe(addr + 0x08, currentBlock)) return false;
    if (currentBlock < 0 || currentBlock > 8192) {
        if (g_validationDbgCount++ < kMaxValidationDbgLogs)
            Logger::Debug("SCAN:GNam", "ValidateGNamesStructural: CurrentBlock=%d out of range at 0x%llX",
                      currentBlock, (unsigned long long)addr);
        return false;
    }

    // Blocks[CurrentBlock+1] should be NULL (end sentinel)
    uintptr_t nextBlock = 0;
    if (!Mem::ReadSafe(addr + 0x10 + ((currentBlock + 1) * 8), nextBlock)) return false;
    if (nextBlock != 0) {
        if (g_validationDbgCount++ < kMaxValidationDbgLogs)
            Logger::Debug("SCAN:GNam", "ValidateGNamesStructural: Blocks[%d+1] = 0x%llX (non-null) at 0x%llX",
                      currentBlock, (unsigned long long)nextBlock, (unsigned long long)addr);
        return false;
    }

    // Blocks[0] should point to "None" FNameEntry
    uintptr_t block0 = 0;
    if (!Mem::ReadSafe(addr + 0x10, block0) || block0 == 0) return false;

    // Validate block0 as a "None" FNameEntry using exact header format matching.
    // Try both standard (header at +0, string at +2) and hash-prefixed (header at +4, string at +6).
    // For each, try Format A (header >> 6 = 4) and Format B ((header >> 1) & 0x7FF = 4).
    auto checkNoneAtOffset = [&](int hdrOff) -> bool {
        uint16_t header = 0;
        if (!Mem::ReadSafe(block0 + hdrOff, header)) return false;

        auto tryFormat = [&](int shift, int mask) -> bool {
            int len = (header >> shift) & mask;
            if (len != 4) return false;
            char name[5] = {};
            if (!Mem::ReadBytesSafe(block0 + hdrOff + 2, name, 4)) return false;
            return strcmp(name, "None") == 0;
        };

        return tryFormat(6, 0x3FF) || tryFormat(1, 0x7FF);
    };

    bool noneFound = checkNoneAtOffset(0);
    if (!noneFound) noneFound = checkNoneAtOffset(4);  // Hash-prefixed UE4.26 layout

    if (noneFound && g_fnameEntryHeaderOffset == 0) {
        // If standard check failed but hash-prefixed succeeded, record it
        if (!checkNoneAtOffset(0)) g_fnameEntryHeaderOffset = 4;
    }

    if (!noneFound) {
        if (g_validationDbgCount++ < kMaxValidationDbgLogs) {
            uint16_t h0 = 0, h4 = 0;
            Mem::ReadSafe(block0, h0);
            Mem::ReadSafe(block0 + 4, h4);
            Logger::Debug("SCAN:GNam", "ValidateGNamesStructural: Blocks[0] headers=0x%04X/0x%04X don't decode to 'None' at 0x%llX",
                      h0, h4, (unsigned long long)addr);
        }
        return false;
    }

    // Corroborate: the first chunk must also contain common UE type names
    // (ByteProperty, Object, Class, etc.) — random heap data won't have these.
    if (!CorroborateFNameChunk(block0)) {
        if (g_validationDbgCount++ < kMaxValidationDbgLogs)
            Logger::Debug("SCAN:GNam", "ValidateGNamesStructural: Blocks[0] corroboration failed at 0x%llX",
                      (unsigned long long)addr);
        return false;
    }

    Logger::Info("SCAN:GNam", "ValidateGNamesStructural: Valid FNamePool at 0x%llX (CurrentBlock=%d, corroborated)",
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

// ============================================================
// ScanForTarget — Unified AOB scanning engine
//
// Iterates all AobSignature entries for a target in priority order.
// For each pattern: AOBScanAll to find ALL matches, resolve RIP,
// apply adjustment, validate, and select the best result.
// Returns the winning address or 0 on failure.
// ============================================================

using ValidatorFn = bool(*)(uintptr_t);

struct PatternScanResult {
    const char* id       = nullptr;
    int         hitCount = 0;
    uintptr_t   selected = 0;
    bool        validated = false;
};

struct ScanReport {
    const char*                    targetName = "";
    std::vector<PatternScanResult> results;
    uintptr_t                      finalAddress = 0;
    const char*                    winningId    = nullptr;
};

// Try to resolve a symbol export from any loaded module.
// Reuses the existing TrySymbolExport helper.
static uintptr_t ResolveSymbolExport(const AobSignature& sig, ValidatorFn validate) {
    uintptr_t addr = TrySymbolExport(sig.pattern);
    if (!addr) return 0;

    // Direct validation
    if (validate(addr)) return addr;

    // Deref and validate
    uintptr_t derefed = 0;
    if (Mem::ReadSafe(addr, derefed) && derefed && validate(derefed))
        return derefed;

    return 0;
}

// Scan the first N bytes of a function body for RIP-relative LEA/MOV
// instructions and validate each resolved target.
// Shared by CallFollow and SymbolCallFollow.
static uintptr_t ScanFunctionBodyForRipRef(
    uintptr_t funcAddr, const char* sigId, ValidatorFn validate, int scanBytes = 256)
{
    for (int off = 0; off + 7 <= scanBytes; ++off) {
        uint8_t b0 = 0, b1 = 0, b2 = 0;
        if (!Mem::ReadSafe(funcAddr + off, b0)) break;
        if (b0 != 0x48 && b0 != 0x4C) continue;
        if (!Mem::ReadSafe(funcAddr + off + 1, b1)) break;
        if (b1 != 0x8B && b1 != 0x8D) continue;
        if (!Mem::ReadSafe(funcAddr + off + 2, b2)) break;
        if ((b2 & 0x07) != 0x05) continue; // RIP-relative addressing

        uintptr_t target = Mem::ResolveRIP(funcAddr + off, 3, 7);
        if (!target) continue;

        uintptr_t candidate = target;
        if (b1 == 0x8B) { // MOV — need deref
            if (!Mem::ReadSafe(target, candidate) || !candidate) continue;
        }

        if (validate(candidate)) {
            LOG_INFO("FuncBodyScan [%s]: Found at 0x%llX (func+0x%X, %s)",
                     sigId, (unsigned long long)candidate, off,
                     b1 == 0x8D ? "LEA" : "MOV");
            return candidate;
        }
    }
    return 0;
}

// Follow a CALL instruction at callOffset within the matched pattern,
// then scan the called function's body for RIP-relative references.
// Used for GNames V7_FNAME_CTOR pattern.
static uintptr_t ResolveCallFollow(uintptr_t matchAddr, const AobSignature& sig, ValidatorFn validate) {
    uintptr_t callInstr = matchAddr + sig.callOffset;
    uint8_t opcode = 0;
    Mem::ReadSafe(callInstr, opcode);
    if (opcode != 0xE8) return 0;

    int32_t rel32 = 0;
    if (!Mem::ReadSafe(callInstr + 1, rel32)) return 0;
    uintptr_t funcAddr = callInstr + 5 + rel32;

    LOG_DEBUG("CallFollow [%s]: Following CALL to function at 0x%llX",
              sig.id, (unsigned long long)funcAddr);

    return ScanFunctionBodyForRipRef(funcAddr, sig.id, validate);
}

// Resolve a MSVC symbol export as a function address, then scan
// the function body for RIP-relative references to the target global.
// Used for GNames: FName::ToString/FName::FName export → FNamePool ref.
static uintptr_t ResolveSymbolCallFollow(const AobSignature& sig, ValidatorFn validate) {
    uintptr_t funcAddr = TrySymbolExport(sig.pattern);
    if (!funcAddr) return 0;

    LOG_DEBUG("SymbolCallFollow [%s]: Scanning function body at 0x%llX",
              sig.id, (unsigned long long)funcAddr);

    return ScanFunctionBodyForRipRef(funcAddr, sig.id, validate);
}

// Try resolving a single match address according to the signature's resolve strategy.
// Returns validated address or 0.
static uintptr_t TryResolveMatch(uintptr_t matchAddr, const AobSignature& sig, ValidatorFn validate) {
    uintptr_t instrAddr = matchAddr + sig.instrOffset;
    uintptr_t target = Mem::ResolveRIP(instrAddr, sig.opcodeLen, sig.totalLen);
    if (!target) return 0;

    // Try with adjustment first (e.g. -0x10), then without
    auto tryValidate = [&](uintptr_t addr) -> uintptr_t {
        if (!addr) return 0;
        // For GWorld write-patterns: check if pointer value is accessible
        if (sig.target == AobTarget::GWorld) {
            uintptr_t world = 0;
            if (!Mem::ReadSafe(addr, world)) return 0;
            if (!sig.gworldAllowNull && world == 0) return 0;
            // Basic pointer sanity for non-null values
            if (world != 0 && (world < 0x10000 || world > 0x00007FFFFFFFFFFF))
                return 0;
        }
        if (validate(addr)) return addr;
        return 0;
    };

    // RipDirect or first pass of RipBoth
    if (sig.resolve == AobResolve::RipDirect || sig.resolve == AobResolve::RipBoth) {
        if (sig.adjustment != 0) {
            uintptr_t adjusted = tryValidate(target + sig.adjustment);
            if (adjusted) return adjusted;
        }
        uintptr_t direct = tryValidate(target);
        if (direct) return direct;
    }

    // RipDeref or second pass of RipBoth
    if (sig.resolve == AobResolve::RipDeref || sig.resolve == AobResolve::RipBoth) {
        uintptr_t value = 0;
        if (Mem::ReadSafe(target, value) && value) {
            if (sig.adjustment != 0) {
                uintptr_t adjusted = tryValidate(value + sig.adjustment);
                if (adjusted) return adjusted;
            }
            uintptr_t derefed = tryValidate(value);
            if (derefed) return derefed;
        }
    }

    return 0;
}

static uintptr_t ScanForTarget(
    const AobSignature* patterns, size_t count,
    ValidatorFn validate, ScanReport& report,
    bool tryMultiModule)
{
    // Sort entries by priority (patterns array is constexpr, so copy pointers)
    std::vector<const AobSignature*> sorted;
    sorted.reserve(count);
    for (size_t i = 0; i < count; ++i) sorted.push_back(&patterns[i]);
    std::sort(sorted.begin(), sorted.end(),
              [](const AobSignature* a, const AobSignature* b) { return a->priority < b->priority; });

    for (const AobSignature* sig : sorted) {
        g_validationDbgCount = 0;  // Reset throttle per pattern

        PatternScanResult pr;
        pr.id = sig->id;

        // ── Symbol Export (variable) ─────────────────────────
        if (sig->resolve == AobResolve::SymbolExport) {
            uintptr_t result = ResolveSymbolExport(*sig, validate);
            pr.hitCount = result ? 1 : 0;
            pr.selected = result;
            pr.validated = (result != 0);
            report.results.push_back(pr);
            if (result) {
                LOG_INFO("[%s] %s: Symbol export -> 0x%llX",
                         report.targetName, sig->id, (unsigned long long)result);
                report.finalAddress = result;
                report.winningId = sig->id;
                return result;
            }
            continue;
        }

        // ── Symbol Call Follow (function → scan body) ────────
        if (sig->resolve == AobResolve::SymbolCallFollow) {
            uintptr_t result = ResolveSymbolCallFollow(*sig, validate);
            pr.hitCount = result ? 1 : 0;
            pr.selected = result;
            pr.validated = (result != 0);
            report.results.push_back(pr);
            if (result) {
                LOG_INFO("[%s] %s: SymbolCallFollow -> 0x%llX",
                         report.targetName, sig->id, (unsigned long long)result);
                report.finalAddress = result;
                report.winningId = sig->id;
                return result;
            }
            continue;
        }

        // ── CallFollow ───────────────────────────────────────
        if (sig->resolve == AobResolve::CallFollow) {
            uintptr_t matchAddr = Mem::AOBScan(sig->pattern);
            pr.hitCount = matchAddr ? 1 : 0;
            if (matchAddr) {
                uintptr_t result = ResolveCallFollow(matchAddr, *sig, validate);
                pr.selected = result;
                pr.validated = (result != 0);
            }
            report.results.push_back(pr);
            if (pr.validated) {
                LOG_INFO("[%s] %s: CallFollow -> 0x%llX",
                         report.targetName, sig->id, (unsigned long long)pr.selected);
                report.finalAddress = pr.selected;
                report.winningId = sig->id;
                return pr.selected;
            }
            continue;
        }

        // ── AOB pattern (RipDirect / RipDeref / RipBoth) ────
        // Phase 1: Scan main module
        std::vector<uintptr_t> matches = Mem::AOBScanAll(sig->pattern);

        // Phase 2: If no matches in main module and multi-module enabled, scan all
        bool usedMultiModule = false;
        if (matches.empty() && tryMultiModule) {
            matches = Mem::AOBScanAllModules(sig->pattern);
            usedMultiModule = !matches.empty();
        }

        pr.hitCount = static_cast<int>(matches.size());

        if (matches.empty()) {
            report.results.push_back(pr);
            continue;
        }

        // Try to validate each match
        uintptr_t bestResult = 0;
        for (uintptr_t matchAddr : matches) {
            uintptr_t resolved = TryResolveMatch(matchAddr, *sig, validate);
            if (resolved) {
                bestResult = resolved;
                break; // Take first validated match
            }
        }

        // Log suppression summary if validation debug output was throttled
        if (g_validationDbgCount > kMaxValidationDbgLogs) {
            LOG_INFO("[%s] %s: Validation debug output throttled (%d entries, showed first %d)",
                     report.targetName, sig->id, g_validationDbgCount, kMaxValidationDbgLogs);
        }

        pr.selected = bestResult;
        pr.validated = (bestResult != 0);
        report.results.push_back(pr);

        if (bestResult) {
            if (pr.hitCount > 1) {
                LOG_INFO("[%s] %s: %d matches%s, validated -> 0x%llX",
                         report.targetName, sig->id, pr.hitCount,
                         usedMultiModule ? " (multi-module)" : "",
                         (unsigned long long)bestResult);
            } else {
                LOG_INFO("[%s] %s: Unique match%s -> 0x%llX",
                         report.targetName, sig->id,
                         usedMultiModule ? " (multi-module)" : "",
                         (unsigned long long)bestResult);
            }
            report.finalAddress = bestResult;
            report.winningId = sig->id;
            return bestResult;
        }

        // Log non-zero hitCount with no validation
        LOG_INFO("[%s] %s: %d match(es)%s, none validated",
                 report.targetName, sig->id, pr.hitCount,
                 usedMultiModule ? " (multi-module)" : "");
    }

    return 0;
}

// Log the scan report summary and per-pattern details for analysis.
// Output goes to scan.log for post-mortem diagnosis.
static void LogScanReport(const ScanReport& report) {
    int totalPatterns = static_cast<int>(report.results.size());
    int patternsWithHits = 0;
    int patternsValidated = 0;
    for (auto& r : report.results) {
        if (r.hitCount > 0) ++patternsWithHits;
        if (r.validated) ++patternsValidated;
    }

    // Summary line
    if (report.finalAddress) {
        Logger::Info("SCAN", "=== %s: %d patterns tried, %d with hits, winner: %s -> 0x%llX ===",
                 report.targetName, totalPatterns, patternsWithHits,
                 report.winningId ? report.winningId : "?",
                 (unsigned long long)report.finalAddress);
    } else {
        Logger::Warn("SCAN", "=== %s: %d patterns tried, %d with hits, NONE validated ===",
                 report.targetName, totalPatterns, patternsWithHits);
    }

    // Per-pattern detail: list all patterns with hits (INFO) and 0-hit (DEBUG)
    for (auto& r : report.results) {
        if (r.validated) {
            Logger::Info("SCAN", "  [%s] %-16s hits=%-4d -> 0x%llX  [WINNER]",
                     report.targetName, r.id, r.hitCount,
                     (unsigned long long)r.selected);
        } else if (r.hitCount > 0) {
            Logger::Info("SCAN", "  [%s] %-16s hits=%-4d  (not validated)",
                     report.targetName, r.id, r.hitCount);
        } else {
            Logger::Debug("SCAN", "  [%s] %-16s hits=0",
                      report.targetName, r.id);
        }
    }
}

// ============================================================
// FindGObjects — unified scan + data-section fallback
// ============================================================

uintptr_t FindGObjects() {
    Logger::Info("SCAN:GObj", "FindGObjects: Scanning for GObjects...");

    ScanReport report;
    report.targetName = "GObjects";

    uintptr_t result = ScanForTarget(
        Sig::GOBJECTS_PATTERNS, std::size(Sig::GOBJECTS_PATTERNS),
        ValidateGObjects, report, /*tryMultiModule=*/true);

    LogScanReport(report);

    if (!result) {
        // Fallback: exhaustive data-section pointer scan
        Logger::Warn("SCAN:GObj", "FindGObjects: All patterns failed, trying data-section scan fallback...");
        result = FindGObjectsByDataScan();
    }

    if (!result) {
        Logger::Error("SCAN:GObj", "FindGObjects: All patterns and fallback scan failed");
    }
    return result;
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

        // Try two header layouts:
        //   (A) Standard: 2-byte header at chunk0+0, string at chunk0+2
        //   (B) Hash-prefixed (UE4.26): 4-byte hash at chunk0+0, 2-byte header at chunk0+4, string at chunk0+6
        // For each layout, try both Format A (len = header >> 6) and Format B (len = (header >> 1) & 0x7FF)

        auto tryHeaderAt = [&](int hdrOff) -> bool {
            uint16_t header = 0;
            if (!Mem::ReadSafe(chunk0 + hdrOff, header)) return false;

            char name[5] = {};
            int lenA = header >> 6;
            if (lenA == 4 && Mem::ReadBytesSafe(chunk0 + hdrOff + 2, name, 4) && strcmp(name, "None") == 0) {
                g_fnameEntryHeaderOffset = hdrOff;
                Logger::Info("SCAN:GNam", "ValidateGNames: Valid at 0x%llX (chunks@+0x%02X, hdrOff=%d, FmtA, 'None')",
                         static_cast<unsigned long long>(addr), off, hdrOff);
                return true;
            }
            memset(name, 0, sizeof(name));
            int lenB = (header >> 1) & 0x7FF;
            if (lenB == 4 && Mem::ReadBytesSafe(chunk0 + hdrOff + 2, name, 4) && strcmp(name, "None") == 0) {
                g_fnameEntryHeaderOffset = hdrOff;
                Logger::Info("SCAN:GNam", "ValidateGNames: Valid at 0x%llX (chunks@+0x%02X, hdrOff=%d, FmtB, 'None')",
                         static_cast<unsigned long long>(addr), off, hdrOff);
                return true;
            }

            if (g_validationDbgCount++ < kMaxValidationDbgLogs)
                Logger::Debug("SCAN:GNam", "ValidateGNames: offset +0x%02X hdrOff=%d chunk0=0x%llX header=0x%04X lenA=%d lenB=%d name='%.4s'",
                          off, hdrOff, static_cast<unsigned long long>(chunk0), header, lenA, lenB, name);
            return false;
        };

        // Standard layout: header at +0
        if (tryHeaderAt(0)) return true;
        // Hash-prefixed layout: 4-byte ComparisonId then 2-byte header at +4
        if (tryHeaderAt(4)) return true;
    }

    // Dump the first 128 bytes so we can diagnose the layout manually
    if (g_validationDbgCount++ < kMaxValidationDbgLogs) {
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

// ─────────────────────────────────────────────────────────────────────────────
// UE4 TNameEntryArray validation
//
// UE4 <4.23 uses TNameEntryArray instead of FNamePool:
//   TNameEntryArray = array of chunk pointers
//   Each chunk = array of FNameEntry* pointers (up to 0x4000 entries per chunk)
//   FNameEntry has a null-terminated string at a fixed offset (+0x10 for UE4.14-4.22, +0x06 for older)
//
// Double-dereference: arrayBase[0] → chunkPtr → entry0Ptr → FNameEntry with "None"
// ─────────────────────────────────────────────────────────────────────────────
static bool ValidateGNamesUE4(uintptr_t addr, int& outStringOffset) {
    if (!addr) return false;

    // Read first chunk pointer: TNameEntryArray[0]
    uintptr_t chunk0Ptr = 0;
    if (!Mem::ReadSafe(addr, chunk0Ptr) || !chunk0Ptr) return false;
    if (chunk0Ptr < 0x10000 || chunk0Ptr > 0x00007FFFFFFFFFFF) return false;

    // chunk0Ptr points to an array of FNameEntry* pointers
    // Read chunk0[0] = first FNameEntry*
    uintptr_t entry0 = 0;
    if (!Mem::ReadSafe(chunk0Ptr, entry0) || !entry0) return false;
    if (entry0 < 0x10000 || entry0 > 0x00007FFFFFFFFFFF) return false;

    // Try reading "None" at common UE4 FNameEntry string offsets
    int offsets[] = { 0x10, 0x06, 0x0C, 0x08 };
    for (int strOff : offsets) {
        char name[5] = {};
        if (!Mem::ReadBytesSafe(entry0 + strOff, name, 4)) continue;
        if (strcmp(name, "None") != 0) continue;

        // Corroborate: entry at index 1 should also be a valid pointer with ASCII string
        uintptr_t entry1 = 0;
        if (!Mem::ReadSafe(chunk0Ptr + 8, entry1) || entry1 < 0x10000) continue;

        char name1[8] = {};
        if (!Mem::ReadBytesSafe(entry1 + strOff, name1, 7)) continue;

        bool valid = true;
        for (int i = 0; i < 7 && name1[i]; ++i) {
            auto c = static_cast<unsigned char>(name1[i]);
            if (c < 0x20 || c >= 0x7F) { valid = false; break; }
        }
        if (!valid) continue;

        // Extra corroboration: entry at index 2 should also be valid
        uintptr_t entry2 = 0;
        if (Mem::ReadSafe(chunk0Ptr + 16, entry2) && entry2 > 0x10000) {
            char name2[8] = {};
            if (Mem::ReadBytesSafe(entry2 + strOff, name2, 7)) {
                bool valid2 = true;
                for (int i = 0; i < 7 && name2[i]; ++i) {
                    auto c = static_cast<unsigned char>(name2[i]);
                    if (c < 0x20 || c >= 0x7F) { valid2 = false; break; }
                }
                if (!valid2) continue;
            }
        }

        outStringOffset = strOff;
        Logger::Info("SCAN:GNam", "ValidateGNamesUE4: Valid TNameEntryArray at 0x%llX "
                 "(strOff=0x%X, entry[0]='None', entry[1]='%.7s')",
                 (unsigned long long)addr, strOff, name1);
        return true;
    }

    return false;
}

// Return true if the memory at `addr` starts with a "None" FNameEntry.
// The FNameEntry header is 2 bytes (all known UE5 versions), followed by the
// name string.  Instead of checking specific header values (which vary across
// UE versions), we just look for ASCII "None" at offset +2.
// Also checks offset +4 in case a future build uses a 4-byte header.
static bool LooksLikeNoneEntry(uintptr_t addr) {
    // Read enough bytes to check all known header layouts:
    //   +2: standard 2-byte header
    //   +4: potential 4-byte header variant
    //   +6: UE4.26 hash-prefixed (4-byte hash + 2-byte header)
    uint8_t buf[10] = {};
    if (!Mem::ReadBytesSafe(addr, buf, 10)) return false;

    // Standard 2-byte header: "None" at offset +2
    if (buf[2] == 'N' && buf[3] == 'o' && buf[4] == 'n' && buf[5] == 'e')
        return true;

    // Potential 4-byte header variant: "None" at offset +4
    if (buf[4] == 'N' && buf[5] == 'o' && buf[6] == 'n' && buf[7] == 'e')
        return true;

    // UE4.26 hash-prefixed: 4-byte ComparisonId + 2-byte header, "None" at offset +6
    if (buf[6] == 'N' && buf[7] == 'o' && buf[8] == 'n' && buf[9] == 'e')
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

// Unified GNames validation: tries FNamePool validators first, then UE4 TNameEntryArray.
// Sets g_isUE4NameArray and g_ue4NameStringOffset if UE4 mode is detected.
static bool ValidateGNamesAny(uintptr_t addr) {
    if (ValidateGNames(addr)) return true;
    if (ValidateGNamesStructural(addr)) return true;
    int strOff = 0;
    if (ValidateGNamesUE4(addr, strOff)) {
        g_isUE4NameArray = true;
        g_ue4NameStringOffset = strOff;
        return true;
    }
    return false;
}

uintptr_t FindGNames() {
    // Reset detection state for each scan attempt
    g_isUE4NameArray = false;
    g_ue4NameStringOffset = 0x10;
    g_fnameEntryHeaderOffset = 0;

    Logger::Info("SCAN:GNam", "FindGNames: Scanning for GNames (FNamePool / TNameEntryArray)...");

    ScanReport report;
    report.targetName = "GNames";

    uintptr_t result = ScanForTarget(
        Sig::GNAMES_PATTERNS, std::size(Sig::GNAMES_PATTERNS),
        ValidateGNamesAny, report, /*tryMultiModule=*/true);

    LogScanReport(report);

    if (!result) {
        // All patterns failed — fall back to data-pointer scan
        Logger::Warn("SCAN:GNam", "FindGNames: All patterns failed, trying pointer scan fallback...");
        result = FindGNamesByPointerScan();
    }

    if (!result) {
        Logger::Error("SCAN:GNam", "FindGNames: All patterns failed");
    }
    return result;
}

// Basic GWorld validator for use with ScanForTarget.
// GWorld validation is simpler than GObjects/GNames — we just check that the
// address is readable and, if the pointer is non-null, it looks like a valid
// heap pointer.  The gworldAllowNull flag in AobSignature controls whether
// null UWorld* values are accepted (handled in TryResolveMatch).
static bool ValidateGWorldBasic(uintptr_t addr) {
    if (!addr) return false;
    uintptr_t world = 0;
    if (!Mem::ReadSafe(addr, world)) return false;
    // A null world is acceptable (write-patterns at startup) — the null
    // filtering is already handled by TryResolveMatch via gworldAllowNull.
    // Here we just accept any readable address.
    if (world == 0) return true;
    return LooksLikeDataPtr(world);
}

uintptr_t FindGWorld() {
    Logger::Info("SCAN:GWld", "FindGWorld: Scanning for GWorld...");

    ScanReport report;
    report.targetName = "GWorld";

    uintptr_t result = ScanForTarget(
        Sig::GWORLD_PATTERNS, std::size(Sig::GWORLD_PATTERNS),
        ValidateGWorldBasic, report, /*tryMultiModule=*/true);

    LogScanReport(report);

    if (!result) {
        Logger::Warn("SCAN:GWld", "FindGWorld: All patterns failed (non-critical)");
    }
    return result;
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

    // Some shippers put UE version in FileVersion instead of ProductVersion
    uint32_t fmajor = HIWORD(fi->dwFileVersionMS);
    uint32_t fminor = LOWORD(fi->dwFileVersionMS);
    if (fmajor == 5 && fminor <= 9) {
        Logger::Info("SCAN:Ver", "DetectVersion: PE FileVersion -> UE %u.%u -> %u", fmajor, fminor, 500u + fminor);
        return 500u + fminor;
    }
    if (fmajor == 4 && fminor <= 27) {
        Logger::Info("SCAN:Ver", "DetectVersion: PE FileVersion -> UE4.%u (treated as 400+minor)", fminor);
        return 400u + fminor;
    }

    Logger::Warn("SCAN:Ver", "DetectVersion: PE VERSIONINFO Product=%u.%u File=%u.%u — unrecognised",
             major, minor, fmajor, fminor);
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

    // Version string patterns to match (ordered by priority — newest first)
    struct { const char* needle; uint32_t value; } patterns[] = {
        { "5.7.", 507 }, { "5.6.", 506 }, { "5.5.", 505 },
        { "5.4.", 504 }, { "5.3.", 503 }, { "5.2.", 502 },
        { "5.1.", 501 }, { "5.0.", 500 },
        { "4.27.", 427 }, { "4.26.", 426 }, { "4.25.", 425 },
        { "4.24.", 424 }, { "4.23.", 423 }, { "4.22.", 422 },
    };

    const uint8_t* scan = reinterpret_cast<const uint8_t*>(base);

    // === Tier 1: Exact UE build strings "++UE5+Release-5.X" / "++UE4+Release-4.XX" ===
    // These are embedded in shipping UE builds and are the most reliable identifier.
    {
        const char* prefixes[] = { "++UE5+Release-", "++UE4+Release-" };
        for (const char* prefix : prefixes) {
            size_t prefixLen = strlen(prefix);
            for (size_t off = 0; off + prefixLen + 4 < size; ++off) {
                if (memcmp(scan + off, prefix, prefixLen) != 0) continue;
                for (auto& p : patterns) {
                    size_t needleLen = strlen(p.needle);
                    if (off + prefixLen + needleLen <= size &&
                        memcmp(scan + off + prefixLen, p.needle, needleLen) == 0) {
                        Logger::Info("SCAN:Ver", "DetectVersion: Tier 1 '%s' -> %u at 0x%zX",
                                 prefix, p.value, off);
                        return p.value;
                    }
                }
            }
        }
    }

    // === Tier 2 + 3: Per-pattern scan with context checks ===
    for (auto& p : patterns) {
        size_t needleLen = strlen(p.needle);
        for (size_t off = 0; off + needleLen + 10 < size; ++off) {
            if (memcmp(scan + off, p.needle, needleLen) != 0) continue;

            // Tier 2: "Release" prefix within the preceding 16 bytes
            if (off >= 8) {
                char ctx[17] = {};
                memcpy(ctx, scan + off - 8, 8);
                if (strstr(ctx, "Release") || strstr(ctx, "release")) {
                    Logger::Info("SCAN:Ver", "DetectVersion: Tier 2 Release prefix -> %u at 0x%zX",
                             p.value, off);
                    return p.value;
                }
            }

            // Tier 3: bare "X.Y.D" — only accept if preceding char is NOT a digit or period.
            // This prevents matching game version strings like "15.6.0" or "v2.5.6.1".
            if (scan[off + needleLen] >= '0' && scan[off + needleLen] <= '9') {
                if (off > 0) {
                    uint8_t prev = scan[off - 1];
                    if ((prev >= '0' && prev <= '9') || prev == '.') {
                        continue;  // Skip — likely a game version string, not UE version
                    }
                }
                Logger::Info("SCAN:Ver", "DetectVersion: Tier 3 bare pattern -> %u at 0x%zX", p.value, off);
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
                // UE5.3+ uses tagged FFieldVariant: LSB=1 means UObject, LSB=0 means FField
                if (ueVersion >= 503) {
                    DynOff::bTaggedFFieldVariant = true;
                    Logger::Info("DYNO", "ValidateAndFixOffsets: UE5.3+ tagged FFieldVariant enabled");
                }
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
        DynOff::bOffsetsValidated.store(true, std::memory_order_release);
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

    if (!childProps && DynOff::bUseFProperty) {
        // FProperty scan failed. This could mean the game is actually UE4 pre-4.25
        // using UProperty (UObject-derived properties). Common when version is misdetected
        // (e.g., FF7R detected as UE5.04 but is actually UE4.18).
        // Retry with UProperty mode: look for UObject-derived properties in the chain.
        Logger::Warn("DYNO", "ValidateAndFixOffsets: FProperty scan failed on '%s', retrying as UProperty...", testName);

        // Expand probe range to include UE4 offsets (UStruct may start at +0x30)
        for (int off = 0x28; off <= 0x80; off += 8) {
            uintptr_t ptr = 0;
            if (!Mem::ReadSafe(testStruct + off, ptr) || !ptr) continue;
            if (ptr < 0x10000 || ptr > 0x00007FFFFFFFFFFF) continue;

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
                DynOff::bUseFProperty = false;
                DynOff::bTaggedFFieldVariant = false;  // UE4 has no tagged FFieldVariant
                DynOff::UFIELD_NEXT = DynOff::bCasePreservingName ? 0x30 : 0x28;
                Logger::Info("DYNO", "ValidateAndFixOffsets: FALLBACK — UProperty mode detected. "
                         "Children at struct+0x%02X → 0x%llX (Class='%s')",
                         off, (unsigned long long)ptr, clsName.c_str());
                break;
            }
        }
    }

    if (!childProps) {
        Logger::Warn("DYNO", "ValidateAndFixOffsets: Cannot find ChildProperties in '%s', keeping defaults", testName);
        DynOff::bOffsetsValidated.store(true, std::memory_order_release);
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

            // UE5.3+: offset 0x10 is FFieldVariant Owner (tagged pointer).
            // If LSB is set, this is a UObject owner reference — skip it as Next candidate.
            if (DynOff::IsFFieldVariantUObject(nextPtr)) continue;
            nextPtr = DynOff::StripFFieldTag(nextPtr);

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
        DynOff::FARRAYPROP_INNER   = propOffsetOff + 0x2C;  // Same subclass extension offset
        DynOff::FBOOLPROP_FIELDSIZE = DynOff::FSTRUCTPROP_STRUCT;
        // FEnumProperty::Enum and FByteProperty::Enum share the same subclass extension offset
        DynOff::FENUMPROP_ENUM     = DynOff::FSTRUCTPROP_STRUCT;
        DynOff::FBYTEPROP_ENUM     = DynOff::FSTRUCTPROP_STRUCT;
    }

    // Infer tagged FFieldVariant from probed offsets:
    // FFieldVariant=0x08 (Next at 0x18) implies UE5.1.1+ layout.
    // UE5.3+ uses the tag-bit encoding. If version is unknown but layout matches,
    // enable tag-bit masking defensively — the StripFFieldTag is a no-op when bit is 0.
    if (DynOff::bUseFProperty && DynOff::FFIELD_NEXT == 0x18 && !DynOff::bTaggedFFieldVariant) {
        DynOff::bTaggedFFieldVariant = true;
        Logger::Info("DYNO", "ValidateAndFixOffsets: Inferred tagged FFieldVariant from FField::Next=0x18");
    }

    DynOff::bOffsetsValidated.store(true, std::memory_order_release);

    // Summary log
    Logger::Info("DYNO", "=== Dynamic Offset Summary ===");
    Logger::Info("DYNO", "  CasePreservingName: %s", DynOff::bCasePreservingName ? "YES" : "no");
    Logger::Info("DYNO", "  TaggedFFieldVariant:%s", DynOff::bTaggedFFieldVariant ? " YES (UE5.3+)" : " no");
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

    // Propagate UE4 TNameEntryArray detection state
    out.bUE4NameArray = g_isUE4NameArray;
    out.ue4StringOffset = g_ue4NameStringOffset;
    out.fnameEntryHeaderOffset = g_fnameEntryHeaderOffset;

    // --- Version inference from detection flags ---
    // If we detected UE4-specific structures but version says UE5, override.
    if (out.bUE4NameArray && out.UEVersion >= 500) {
        LOG_WARN("FindAll: UE4 TNameEntryArray detected but version=%u (>= 500). "
                 "Overriding to 422 (UE4 pre-4.23)", out.UEVersion);
        out.UEVersion = 422;
    } else if (out.fnameEntryHeaderOffset == 4 && out.UEVersion >= 500) {
        LOG_WARN("FindAll: Hash-prefixed FNameEntry (hdrOff=4) suggests UE4.26 fork, "
                 "but version=%u. Overriding to 426", out.UEVersion);
        out.UEVersion = 426;
    }

    out.GWorld = FindGWorld();
    // GWorld is non-critical, just log

    LOG_INFO("FindAll: Complete — GObjects=0x%llX, GNames=0x%llX, GWorld=0x%llX, UE=%u, UE4Names=%s, hdrOff=%d",
             static_cast<unsigned long long>(out.GObjects),
             static_cast<unsigned long long>(out.GNames),
             static_cast<unsigned long long>(out.GWorld),
             out.UEVersion,
             out.bUE4NameArray ? "yes" : "no",
             out.fnameEntryHeaderOffset);

    return true;
}

// ============================================================
// DetectUEnumNames — Lazy-detect UEnum::Names TArray offset
//
// Strategy: Find a well-known UEnum object (ENetRole or EObjectFlags)
// in GObjects, then probe offsets 0x30..0x120 for a TArray header
// whose entries resolve to known enum value names.
// ============================================================

// Read the FName ComparisonIndex at addr and resolve to string (local helper)
static std::string ReadFNameStr(uintptr_t addr) {
    int32_t idx = 0;
    if (!Mem::ReadSafe(addr, idx)) return "";
    return FNamePool::GetString(idx);
}

// Read UObject::ClassPrivate name (compact helper, avoids UStructWalker dep)
static std::string GetObjectClassName(uintptr_t obj) {
    if (!obj) return "";
    uintptr_t cls = 0;
    Mem::ReadSafe(obj + Constants::OFF_UOBJECT_CLASS, cls);
    if (!cls) return "";
    return ReadFNameStr(cls + Constants::OFF_UOBJECT_NAME);
}

// Read UObject::NamePrivate as string
static std::string GetObjectName(uintptr_t obj) {
    if (!obj) return "";
    return ReadFNameStr(obj + Constants::OFF_UOBJECT_NAME);
}

bool DetectUEnumNames() {
    // Already detected?
    if (DynOff::bUEnumNamesDetected.load(std::memory_order_acquire))
        return true;

    Logger::Info("DYNO:Enum", "DetectUEnumNames: Searching for known enums in GObjects...");

    // Candidate enum names to search for, with expected value count ranges
    // and a verification substring that should appear in the first few entry names.
    struct EnumCandidate {
        const char* name;           // UEnum object name
        int         minCount;       // Expected min TArray count
        int         maxCount;       // Expected max TArray count
        const char* verifySubstr;   // Substring to find in entry FNames
    };
    static const EnumCandidate candidates[] = {
        { "ENetRole",       4, 10, "ROLE_" },
        { "EObjectFlags",   5, 50, "RF_" },
        { "EPropertyFlags", 5, 80, "CPF_" },
    };

    // Search GObjects for each candidate
    for (const auto& cand : candidates) {
        uintptr_t enumAddr = 0;

        ObjectArray::ForEach([&](int32_t /*idx*/, uintptr_t obj) -> bool {
            std::string clsName = GetObjectClassName(obj);
            if (clsName != "Enum" && clsName != "UserDefinedEnum")
                return true; // continue

            std::string objName = GetObjectName(obj);
            if (objName == cand.name) {
                enumAddr = obj;
                return false; // stop
            }
            return true; // continue
        });

        if (!enumAddr) {
            Logger::Debug("DYNO:Enum", "  '%s' not found in GObjects", cand.name);
            continue;
        }

        Logger::Info("DYNO:Enum", "  Found '%s' at 0x%llX, probing for Names offset...",
            cand.name, static_cast<unsigned long long>(enumAddr));

        // Probe offsets 0x30..0x120 (step 8) for TArray<TPair<FName,int64>>
        for (int off = 0x30; off <= 0x120; off += 8) {
            uintptr_t data = 0;
            int32_t count = 0;
            if (!Mem::ReadSafe(enumAddr + off, data)) continue;
            if (!Mem::ReadSafe(enumAddr + off + 8, count)) continue;

            // Validate count range
            if (count < cand.minCount || count > cand.maxCount) continue;

            // Validate data pointer looks like heap (non-null, user-mode, not tiny)
            if (data < 0x10000 || data > 0x7FFFFFFFFFFF) continue;

            // Read first few entries and check if FNames resolve to expected substrings
            // Each entry: TPair<FName(8 bytes), int64(8 bytes)> = 16 bytes
            int verified = 0;
            for (int i = 0; i < (std::min)(count, 5); ++i) {
                uintptr_t entryAddr = data + i * 16; // UENUM_ENTRY_SIZE = 0x10
                int32_t nameIdx = 0;
                if (!Mem::ReadSafe(entryAddr, nameIdx)) break;

                std::string entryName = FNamePool::GetString(nameIdx);
                if (entryName.empty()) continue;

                // Check for printable ASCII (basic sanity)
                bool allAscii = true;
                for (char c : entryName) {
                    if (c < 0x20 || c > 0x7E) { allAscii = false; break; }
                }
                if (!allAscii) continue;

                // Check for verification substring
                if (entryName.find(cand.verifySubstr) != std::string::npos) {
                    ++verified;
                }
            }

            if (verified >= 2) {
                DynOff::UENUM_NAMES = off;
                DynOff::bUEnumNamesDetected.store(true, std::memory_order_release);

                Logger::Info("DYNO:Enum", "  UEnum::Names detected at UEnum+0x%02X "
                    "(verified with '%s', count=%d, %d name matches)",
                    off, cand.name, count, verified);
                return true;
            }
        }

        Logger::Debug("DYNO:Enum", "  '%s' found but no valid Names offset detected", cand.name);
    }

    Logger::Warn("DYNO:Enum", "DetectUEnumNames: FAILED — no known enums found or validated");
    return false;
}

} // namespace OffsetFinder
