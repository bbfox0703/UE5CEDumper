// ============================================================
// Genau — 葛納烏 (一級魔法使篩選考官 — First-Class Mage Examiner)
// OffsetFinder: AOB pattern scanning for GObjects, GNames, GWorld
// ============================================================

#include "Genau.h"
#include "Utf8Helpers.h"
#include "Flamme.h"
#include "Macht.h"
#include "Stark.h"
#define LOG_CAT "SCAN"
#include "Sein.h"
#include "Grimoire.h"
#include "Himmel.h"
#include "Aura.h"
#include "Serie.h"
#include "Neu.h"     // UEnum::Names layout parse (legacy TArray vs UE5.6+ FNameData)
#include "Tot.h"     // Tot::Requested — Extra Scan must bail so Fern::Stop's join is bounded (B18)
#include "VersionNeedleScan.h"  // gated needle sweep + HasUEAnchorNearby (audit #5 G2)

#include <string>
#include <cstring>
#include <vector>
#include <algorithm>  // std::sort
#include <atomic>     // FindSparseDelegateStorage cache
#include <unordered_map>  // RecoverGWorldViaEngine per-class offset cache
#include <chrono>     // AOBScanBatch timing
#include <Winver.h>   // GetFileVersionInfoW / VerQueryValueW
#include <Psapi.h>    // EnumProcessModules

namespace Genau {

// ============================================================
// ComputePEHash — Unique game build identifier
//
// Reads PE TimeDateStamp + SizeOfImage from the main module's
// in-memory NT headers. Format: "%08X%08X" (16 hex chars).
// This is the same identity key used by Microsoft symbol servers.
// ============================================================
static bool ComputePEHash(char* out, size_t bufSize) {
    if (!out || bufSize < 17) return false;
    out[0] = '\0';

    uintptr_t base = Macht::GetModuleBase(nullptr);
    if (!base) return false;

    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;

    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS64*>(
        base + static_cast<LONG>(dos->e_lfanew));
    if (nt->Signature != IMAGE_NT_SIGNATURE) return false;

    snprintf(out, bufSize, "%08X%08X",
             nt->FileHeader.TimeDateStamp,
             nt->OptionalHeader.SizeOfImage);
    return true;
}

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

// ValidateGNames' 128-byte pool dump gets its own scan-wide budget so the per-offset
// probe lines can never starve it (see ValidateGNames). Not reset per-pattern: a handful
// of dumps across the whole scan is all we ever need to diagnose a pool layout.
static int  g_gnamesDumpCount = 0;
static constexpr int kMaxGNamesDumps = 8;

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
    if (!Grimoire::IsUserspacePointer(ptr)) return false;
    // Reject pointers inside the game module's loaded range (code/rdata/data all contiguous)
    uintptr_t modBase = Macht::GetModuleBase(nullptr);
    uintptr_t modSize = Macht::GetModuleSize(nullptr);
    if (modBase && modSize && ptr >= modBase && ptr < modBase + modSize) return false;
    return true;
}

// Cyclic class pointer validation (GAP #10):
// Reads UObject pointers from chunk[0] and follows obj->Class chain.
// Valid UObjects have a chain that terminates at UClass (Class == itself).
// Returns true if >= 2 objects pass with any common FUObjectItem stride.
static bool ValidateCyclicClassChain(uintptr_t chunk0) {
    if (!chunk0) return false;

    static const int kStrides[] = { 16, 24, 20 };   // UE5 (16), UE4 (24), alt (20) item sizes
    static const int kScanRange = 20;                // Scan up to 20 slots (early slots may be null)
    static const int kMinTried = 2;                  // Need at least 2 non-null objects to judge
    static const int kMinPass = 2;                   // Minimum passing objects
    static const int kMaxHops = 16;                  // Max class chain depth

    for (int stride : kStrides) {
        int passed = 0;
        int tried  = 0;

        for (int i = 0; i < kScanRange; i++) {
            uintptr_t objPtr = 0;
            if (!Macht::ReadSafe(chunk0 + i * stride, objPtr) || !objPtr) continue;
            if (!Grimoire::IsUserspacePointer(objPtr)) continue;
            tried++;

            // Follow Class chain: obj->Class->Class->... looking for self-referential UClass
            uintptr_t current = objPtr;
            bool valid = false;
            for (int hop = 0; hop < kMaxHops; hop++) {
                uintptr_t cls = 0;
                if (!Macht::ReadSafe(current + Grimoire::OFF_UOBJECT_CLASS, cls)) break;
                if (!Grimoire::IsUserspacePointer(cls)) break;

                // UClass terminates: its ClassPrivate points to itself
                uintptr_t clsCls = 0;
                if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_CLASS, clsCls)) break;
                if (clsCls == cls) { valid = true; break; }

                current = cls;
            }
            if (valid) passed++;

            // Early exit: already have enough passing objects
            if (passed >= kMinPass) break;
        }

        if (tried >= kMinTried && passed >= kMinPass) return true;
    }
    return false;
}

// Upper bound on MaxElements for the strict (Tier 1) presets. MaxElements is
// MaxChunks * 64K, so it tracks the title's gc.MaxObjectsInGame, NOT its live object
// count -- a game that preallocates generously is normal, not corrupt. The old cap was
// 0x800000 (8,388,608) and DragonSword Awakening (UE 5.3) rejected the CORRECT preset on
// it: MaxChunks=161 -> MaxElements = 161 * 65536 = 10,551,296. Tier 1 then fell through to
// the relaxed tier, which picked a row that reads NumElements out of the frozen
// ObjLastNonGCIndex -- so only the startup CDOs were ever enumerated (37,099 of 317,810
// objects) and every runtime object was invisible to every scan. Raised to 33.5M: still
// an order of magnitude below the values produced when the slot actually holds half of a
// pointer (MindsEye: 233M), which is what this bound exists to reject.
static constexpr uint32_t kStrictMaxCeiling  = 0x2000000;  // 33,554,432
// Looser bound for the relaxed (Tier 2) fallback, deliberately weaker than Tier 1's.
static constexpr uint32_t kRelaxedMaxCeiling = 0x4000000;  // 67,108,864

static bool ValidateGObjects(uintptr_t addr) {
    if (!addr) return false;

    // --- Tier 1: Try all known presets with full validation ---
    // Includes chunk consistency checks and decryption support.
    // maxCOff/numCOff = -1 means "flat" layout (no chunk indirection).
    // Flat arrays: Objects* points directly to FUObjectItem[], not FUObjectItem*[].
    struct { int objOff; int maxOff; int numOff; int maxCOff; int numCOff; const char* name; } presets[] = {
        { 0x00, 0x10, 0x14, 0x18, 0x1C, "Default" },
        { 0x10, 0x00, 0x04, 0x08, 0x0C, "Back4Blood" },
        { 0x18, 0x10, 0x00, 0x14, 0x20, "Multiversus" },
        { 0x18, 0x00, 0x14, 0x10, 0x04, "MindsEye" },
        // Flat-Base — a FLAT FFixedUObjectArray presented at the FUObjectArray BASE rather than
        // at its ObjObjects sub-struct. Same obj/max/num as "UE4-Extended" but with no chunk
        // fields, so it is that row's flat twin.
        //
        // Why it has to exist: the "Flat" row below is written ObjObjects-RELATIVE
        // ({0x00,0x08,0x0C}), which silently assumes a flat array is only ever handed to us at
        // ObjObjects. That held only because five of ~56 GObjects patterns carry
        // adjustment = -0x10 (GOBJ_V10 / AV1 / AV2 / RE2 / V12) and so offer ObjObjects as a
        // second candidate. On UE 4.11 and 4.13 those five all MISS, every pattern that does hit
        // resolves the BASE, and the ObjObjects-relative row then reads NumElements out of the
        // OpenForDisregardForGC *bool* at +0x0C. That is why both titles failed with all 55
        // patterns "no winner" — a preset gap, not a pattern gap.
        //
        // Ordered BEFORE UE4-Extended deliberately: otherwise whether a flat title is read as
        // flat depends on whether the FCriticalSection dwords at +0x20/+0x24 happen to look like
        // sane chunk counts. It cannot steal a chunked title — applied to one, objPtr is the
        // chunk TABLE, so Item[0].Object is a chunk pointer and ValidateCyclicClassChain reads
        // its +0x10 (which is Item[0].ClusterRootIndex/SerialNumber, small ints) as ClassPrivate;
        // the self-referential-UClass terminator never appears and the row rejects.
        { 0x10, 0x18, 0x1C,   -1,   -1, "Flat-Base" },
        { 0x10, 0x18, 0x1C, 0x20, 0x24, "UE4-Extended" },
        { 0x10, 0x20, 0x24, 0x28, 0x2C, "UE5-Extended" },  // GC prefix + PreAllocatedObjects ptr
        { 0x00, 0x0C, 0x08, 0x14, 0x10, "UE5.8" },          // 5.8 dev: cache-locality reorder, PreAllocatedObjects @+0x18
        { 0x00, 0x08, 0x0C,   -1,   -1, "Flat" },           // FFixedUObjectArray: no chunks (OT / early UE4)
        // MindsEye (Build A Rocket Boy, UE 5.4.4 licensee fork). The AOB resolves the
        // FUObjectArray base, not ObjObjects, so the "MindsEye" row above — which is
        // written relative to FChunkedFixedUObjectArray — has to be shifted by +0x10.
        // This is exactly the Default -> UE5-Extended relationship already in this table.
        // Recovered by disassembling FUObjectArray::AllocateObjectPool (RVA 0x019B17B0):
        //   MaxElements +0x10, NumChunks +0x14, MaxChunks +0x20, NumElements +0x24, Objects +0x28.
        // Appended LAST: any address matching an earlier preset never reaches it.
        { 0x28, 0x10, 0x24, 0x20, 0x14, "MindsEye-Extended" },
    };

    for (auto& P : presets) {
        int32_t num = 0, max = 0;
        if (!Macht::ReadSafe(addr + P.numOff, num)) continue;
        if (!Macht::ReadSafe(addr + P.maxOff, max)) continue;
        if (num < 0x1000 || num > 0x400000) continue;
        if (max < num || static_cast<uint32_t>(max) > kStrictMaxCeiling) continue;

        // Chunk consistency (if chunk offset fields are valid)
        if (P.maxCOff >= 0 && P.numCOff >= 0) {
            int32_t numC = 0, maxC = 0;
            if (!Macht::ReadSafe(addr + P.numCOff, numC)) continue;
            if (!Macht::ReadSafe(addr + P.maxCOff, maxC)) continue;
            if (numC < 1 || maxC < 1 || numC > maxC) continue;
        }

        uintptr_t objPtr = 0;
        if (!Macht::ReadSafe(addr + P.objOff, objPtr)) continue;
        objPtr = Aura::DecryptObjectPtr(objPtr);

        bool isFlat = (P.maxCOff < 0 && P.numCOff < 0);

        if (isFlat) {
            // Flat array: objPtr points directly to FUObjectItem[] data (no chunk indirection).
            // Dereferencing objPtr would give a UObject* (first item), not a chunk pointer.
            if (!LooksLikeDataPtr(objPtr)) continue;
            if (!ValidateCyclicClassChain(objPtr)) continue;
        } else {
            // Chunked array: objPtr is FUObjectItem** (array of chunk pointers)
            uintptr_t chunk0 = 0;
            if (!Macht::ReadSafe(objPtr, chunk0)) continue;

            if (chunk0 == 0) {
                if (!LooksLikeDataPtr(objPtr)) continue;
            } else {
                if (!LooksLikeDataPtr(chunk0)) continue;
            }

            // Cyclic class chain validation (GAP #10): verify actual UObject instances
            uintptr_t validateBase = chunk0 ? chunk0 : objPtr;
            if (!ValidateCyclicClassChain(validateBase)) continue;
        }

        Sein::Info("SCAN:GObj", "ValidateGObjects: Valid at 0x%llX (preset %s, Num=%d, Max=%d, Objects=0x%llX%s)",
                 static_cast<unsigned long long>(addr), P.name, num, max,
                 static_cast<unsigned long long>(objPtr), isFlat ? " [flat]" : "");
        return true;
    }

    // --- Tier 2: Relaxed fallback (prevents regression) ---
    // Only check NumElements range + Objects pointer validity.
    // isFlat: objPtr is FUObjectItem[] directly (no chunk indirection).
    // maxOff/maxCOff/numCOff mirror the corresponding Tier 1 preset above (A/C<-Default,
    // B<-Back4Blood, D<-UE4-Extended, E<-UE5-Extended, F<-UE5.8, Flat<-Flat). maxOff feeds
    // an upper-bound sanity check only — see kRelaxedMaxCeiling below.
    struct { int numOff; int maxOff; int objOff; int maxCOff; int numCOff; bool isFlat; const char* name; } relaxed[] = {
        { 0x14, 0x10, 0x00, 0x18, 0x1C, false, "A/C" },
        { 0x04, 0x00, 0x10, 0x08, 0x0C, false, "B"   },
        { 0x1C, 0x18, 0x10,   -1,   -1, true,  "D-Flat" },  // flat twin of D — see "Flat-Base" above
        { 0x1C, 0x18, 0x10, 0x20, 0x24, false, "D"   },
        { 0x24, 0x20, 0x10, 0x28, 0x2C, false, "E"   },    // UE5-Extended: GC prefix + PreAllocatedObjects
        { 0x08, 0x0C, 0x00, 0x14, 0x10, false, "F"   },    // UE5.8: cache-locality reorder
        { 0x0C, 0x08, 0x00,   -1,   -1, true,  "Flat" },   // FFixedUObjectArray: no chunks (OT / early UE4)
    };

    // TWO relaxed passes, and the order between them is the point. Several rows read their
    // Objects pointer from the SAME offset (B, D and E all use +0x10), so the pointer +
    // cyclic-class-chain checks below cannot tell them apart — whichever row is listed first
    // and has a plausible-looking count slot wins. On a UE5-Extended layout that is B, whose
    // numOff (+0x04) lands on ObjLastNonGCIndex: a real, in-range, but FROZEN count of the
    // startup disregard-for-GC set. The array is then enumerated only up to the CDOs and
    // every runtime object is silently missing (DragonSword Awakening: 37,099 of 317,810).
    //
    // Pass 1 requires the row's CHUNK fields to be self-consistent — the same numC/maxC check
    // Tier 1 applies, which is exactly what makes B reject on such a layout (its numCOff
    // +0x0C is OpenForDisregardForGC, a bool that reads 0). Pass 2 is byte-identical to the
    // historical behaviour, so nothing that resolves today can stop resolving: this only
    // decides WHICH row wins when more than one would have.
    for (int pass = 0; pass < 2; ++pass) {
        const bool requireChunks = (pass == 0);
        for (auto& L : relaxed) {
            int32_t numElements = 0;
            if (!Macht::ReadSafe(addr + L.numOff, numElements)) continue;
            if (numElements < 0x1000 || numElements > 0x400000) continue;

            // Upper-bound sanity check on the MaxElements slot. A heap blob whose "num" slot
            // happens to fall in range is still betrayed by its "max" slot: MindsEye's
            // ICU-like locale blob read max = 0x0DE62600 (233M) because that dword is really
            // the low half of a module pointer. Deliberately WEAKER than Tier 1 (which also
            // rejects `max < num`): we apply a loose ceiling only, and skip the check entirely
            // if the read fails. A title that raises gc.MaxObjectsInGame past our strict cap —
            // or whose max field is not where this row assumes — therefore still reaches the
            // relaxed accept path exactly as it did before.
            int32_t maxElements = 0;
            if (Macht::ReadSafe(addr + L.maxOff, maxElements) &&
                static_cast<uint32_t>(maxElements) > kRelaxedMaxCeiling) continue;

            // Pass 1 only: chunk consistency, mirroring Tier 1. A row whose chunk slots are
            // unreadable or structurally impossible (no chunks, or more live than allocated)
            // is not describing this candidate's layout.
            if (requireChunks && L.maxCOff >= 0 && L.numCOff >= 0) {
                int32_t numC = 0, maxC = 0;
                if (!Macht::ReadSafe(addr + L.numCOff, numC)) continue;
                if (!Macht::ReadSafe(addr + L.maxCOff, maxC)) continue;
                if (numC < 1 || maxC < 1 || numC > maxC) continue;
            }

            uintptr_t objPtr = 0;
            if (!Macht::ReadSafe(addr + L.objOff, objPtr)) continue;
            objPtr = Aura::DecryptObjectPtr(objPtr);

            if (L.isFlat) {
                // Flat array: objPtr is FUObjectItem[] directly
                if (!LooksLikeDataPtr(objPtr)) continue;
                if (!ValidateCyclicClassChain(objPtr)) continue;
            } else {
                uintptr_t chunk0 = 0;
                if (!Macht::ReadSafe(objPtr, chunk0)) continue;

                if (chunk0 == 0) {
                    if (!LooksLikeDataPtr(objPtr)) continue;
                } else {
                    if (!LooksLikeDataPtr(chunk0)) continue;
                }

                // Cyclic class chain validation (GAP #10): verify actual UObject instances
                uintptr_t validateBase = chunk0 ? chunk0 : objPtr;
                if (!ValidateCyclicClassChain(validateBase)) continue;
            }

            Sein::Info("SCAN:GObj", "ValidateGObjects: Valid at 0x%llX (relaxed %s%s, Num=%d, Objects=0x%llX%s)",
                     static_cast<unsigned long long>(addr), L.name,
                     requireChunks ? "" : " no-chunk-check", numElements,
                     static_cast<unsigned long long>(objPtr), L.isFlat ? " [flat]" : "");
            return true;
        }
    }

    // Log failure with diagnostic info (throttled — data scan can produce 20K+ failures)
    if (g_validationDbgCount < kMaxValidationDbgLogs) {
        int32_t numA = 0, numB = 0, numD = 0;
        Macht::ReadSafe(addr + 0x14, numA);
        Macht::ReadSafe(addr + 0x04, numB);
        Macht::ReadSafe(addr + 0x1C, numD);
        Sein::Warn("SCAN:GObj", "ValidateGObjects: Failed at 0x%llX (Num@+14=%d, Num@+04=%d, Num@+1C=%d)",
                 static_cast<unsigned long long>(addr), numA, numB, numD);
    }
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
    if (!Macht::ReadSafe(addr, rwLock)) return false;
    if (rwLock != 0) {
        if (g_validationDbgCount++ < kMaxValidationDbgLogs)
            Sein::Debug("SCAN:GNam", "ValidateGNamesStructural: FRWLock=0x%llX (non-zero) at 0x%llX",
                      (unsigned long long)rwLock, (unsigned long long)addr);
        return false;
    }

    // CurrentBlock at +0x08
    int32_t currentBlock = 0;
    if (!Macht::ReadSafe(addr + 0x08, currentBlock)) return false;
    if (currentBlock < 0 || currentBlock > 8192) {
        if (g_validationDbgCount++ < kMaxValidationDbgLogs)
            Sein::Debug("SCAN:GNam", "ValidateGNamesStructural: CurrentBlock=%d out of range at 0x%llX",
                      currentBlock, (unsigned long long)addr);
        return false;
    }

    // Blocks[CurrentBlock+1] should be NULL (end sentinel)
    uintptr_t nextBlock = 0;
    if (!Macht::ReadSafe(addr + 0x10 + ((currentBlock + 1) * 8), nextBlock)) return false;
    if (nextBlock != 0) {
        if (g_validationDbgCount++ < kMaxValidationDbgLogs)
            Sein::Debug("SCAN:GNam", "ValidateGNamesStructural: Blocks[%d+1] = 0x%llX (non-null) at 0x%llX",
                      currentBlock, (unsigned long long)nextBlock, (unsigned long long)addr);
        return false;
    }

    // Blocks[0] should point to "None" FNameEntry
    uintptr_t block0 = 0;
    if (!Macht::ReadSafe(addr + 0x10, block0) || block0 == 0) return false;

    // Validate block0 as a "None" FNameEntry using exact header format matching.
    // Try both standard (header at +0, string at +2) and hash-prefixed (header at +4, string at +6).
    // For each, try Format A (header >> 6 = 4) and Format B ((header >> 1) & 0x7FF = 4).
    auto checkNoneAtOffset = [&](int hdrOff) -> bool {
        uint16_t header = 0;
        if (!Macht::ReadSafe(block0 + hdrOff, header)) return false;

        auto tryFormat = [&](int shift, int mask) -> bool {
            int len = (header >> shift) & mask;
            if (len != 4) return false;
            char name[5] = {};
            if (!Macht::ReadBytesSafe(block0 + hdrOff + 2, name, 4)) return false;
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
            Macht::ReadSafe(block0, h0);
            Macht::ReadSafe(block0 + 4, h4);
            Sein::Debug("SCAN:GNam", "ValidateGNamesStructural: Blocks[0] headers=0x%04X/0x%04X don't decode to 'None' at 0x%llX",
                      h0, h4, (unsigned long long)addr);
        }
        return false;
    }

    // Corroborate: the first chunk must also contain common UE type names
    // (ByteProperty, Object, Class, etc.) — random heap data won't have these.
    if (!CorroborateFNameChunk(block0)) {
        if (g_validationDbgCount++ < kMaxValidationDbgLogs)
            Sein::Debug("SCAN:GNam", "ValidateGNamesStructural: Blocks[0] corroboration failed at 0x%llX",
                      (unsigned long long)addr);
        return false;
    }

    Sein::Info("SCAN:GNam", "ValidateGNamesStructural: Valid FNamePool at 0x%llX (CurrentBlock=%d, corroborated)",
             (unsigned long long)addr, currentBlock);
    return true;
}

// ─────────────────────────────────────────────────────────────────────────────
// DataScanGObjectsCandidates — core of the data-section GObjects scan.
// Collects RIP-relative static pointer references from .text that resolve into
// the data section, validates each as a GObjects/FUObjectArray, and appends up
// to `maxWanted` UNIQUE validated candidate addresses to `out` (skipping `avoid`).
//
// Shared by:
//   - FindGObjectsByDataScan       (maxWanted=1: original "first validated" fallback)
//   - Genau::CollectGObjectsCandidates (maxWanted>1: post-init decoy recovery)
// ─────────────────────────────────────────────────────────────────────────────
static void DataScanGObjectsCandidates(std::vector<uintptr_t>& out, uintptr_t avoid, size_t maxWanted) {
    Sein::Info("SCAN:GObj", "DataScanGObjectsCandidates: Collecting static pointer references...");

    uintptr_t base = Macht::GetModuleBase(nullptr);
    if (!base) return;
    size_t modSize = Macht::GetModuleSize(nullptr);
    if (!modSize) return;

    auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return;
    auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + static_cast<DWORD>(dos->e_lfanew));
    if (nt->Signature != IMAGE_NT_SIGNATURE) return;

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
        Sein::Warn("SCAN:GObj", "DataScanGObjectsCandidates: Could not identify code/data sections");
        return;
    }

    Sein::Debug("SCAN:GObj", "DataScanGObjectsCandidates: code=[0x%llX-0x%llX], data=[0x%llX-0x%llX]",
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
        // (G2) Whole-.text walk. RECOVERY path (reached only when the normal GObjects
        // resolve came back empty), so an abort means "recovery did not run" and the user
        // sees an unexplained empty object list — which is why the log line is not
        // decoration. void: the caller reads the (partial) bag and reports no winner.
        if ((scan & 0xFFF) == 0 && Tot::Requested()) {
            Sein::Warn("SCAN:GObj", "DataScanGObjectsCandidates: aborted (client gone / shutdown)");
            return;
        }
        uint8_t b0 = 0, b1 = 0, b2 = 0;
        if (!Macht::ReadSafe(scan, b0)) continue;
        if (b0 != 0x48 && b0 != 0x4C) continue;
        if (!Macht::ReadSafe(scan + 1, b1)) continue;
        if (b1 != 0x8B && b1 != 0x8D) continue;  // MOV or LEA
        if (!Macht::ReadSafe(scan + 2, b2)) continue;
        // ModR/M must be the RIP-relative form — mod=00 AND r/m=101. Testing r/m alone
        // also accepts mov rcx,[rbp-8] / mov rax,rbp; see Macht::IsRipRelativeModRM.
        if (!Macht::IsRipRelativeModRM(b2)) continue;

        // Inline RIP resolution (avoids Macht::ResolveRIP's per-call DEBUG logging
        // which produces ~98K lines and causes log rotation overflow)
        int32_t rel32 = 0;
        if (!Macht::ReadSafe<int32_t>(scan + 3, rel32)) continue;
        uintptr_t target = scan + 7 + rel32;
        if (!target) continue;

        // For MOV instructions (8B), the target is a pointer — dereference it
        uintptr_t value = target;
        if (b1 == 0x8B) {
            if (!Macht::ReadSafe(target, value) || !value) continue;
        }

        // Check if resolved address is in the data section range
        if (target >= dataStart && target < dataEnd) {
            bag.push_back({ scan, target });
        }
    }

    Sein::Info("SCAN:GObj", "DataScanGObjectsCandidates: Found %zu static pointers in data section", bag.size());

    // Try each candidate with GObjects validation
    // Throttle validation failure logging: data scan can produce 20K+ candidates,
    // each logging a WARN line on failure — causing log rotation overflow.
    int dataScanFailCount = 0;
    constexpr int kMaxDataScanFailLogs = 20;
    g_validationDbgCount = 0;  // Reset throttle for validators
    for (auto& sp : bag) {
        if (out.size() >= maxWanted) break;
        uintptr_t candidate = 0;
        if (!Macht::ReadSafe(sp.targetAddr, candidate) || !candidate) continue;
        if (avoid && candidate == avoid) continue;          // skip known-bad (decoy)
        bool dup = false;                                    // skip duplicates already collected
        for (uintptr_t c : out) { if (c == candidate) { dup = true; break; } }
        if (dup) continue;
        if (ValidateGObjects(candidate)) {
            Sein::Info("SCAN:GObj", "DataScanGObjectsCandidates: GObjects validated at 0x%llX (via instr@0x%llX)",
                     (unsigned long long)candidate, (unsigned long long)sp.instrAddr);
            out.push_back(candidate);
            continue;
        }
        dataScanFailCount++;
        if (dataScanFailCount == kMaxDataScanFailLogs) {
            Sein::Info("SCAN:GObj", "DataScanGObjectsCandidates: Throttling validation failure logs (showed first %d)",
                     kMaxDataScanFailLogs);
            g_validationDbgCount = kMaxValidationDbgLogs;  // Suppress further validator debug output
        }
    }
    if (dataScanFailCount > kMaxDataScanFailLogs) {
        Sein::Info("SCAN:GObj", "DataScanGObjectsCandidates: (%d validation failures were suppressed)",
                 dataScanFailCount - kMaxDataScanFailLogs);
    }
    if (out.empty()) {
        Sein::Warn("SCAN:GObj", "DataScanGObjectsCandidates: No valid GObjects found among %zu candidates", bag.size());
    }
}

// FindGObjectsByDataScan — fallback: first validated GObjects from the data scan.
static uintptr_t FindGObjectsByDataScan() {
    std::vector<uintptr_t> v;
    DataScanGObjectsCandidates(v, /*avoid=*/0, /*maxWanted=*/1);
    return v.empty() ? 0 : v[0];
}

// ─────────────────────────────────────────────────────────────────────────────
// Static-struct GObjects resolution (Avowed / Obsidian UE5.3 and similar).
//
// Some games place GUObjectArray as a STATIC FUObjectArray in the module's
// zero-init .data/BSS, and NO AOB pattern — not even RE-UE4SS/patternsleuth's —
// matches the code that references it. The struct is still referenced by
// `lea/mov reg,[rip+disp]` (e.g. inside FUObjectArray::AllocateUObjectIndex).
// Our regular data scan DEREFERENCES those .data slots (correct for heap-allocated
// GObjects), so it never tries the slot itself as the struct base and lands on
// decoys. This resolver probes a small window around each referenced .data slot
// as a standard UE5 chunked FUObjectArray and validates by CONTENT — the first
// objects must resolve to clean printable-ASCII names.
// ─────────────────────────────────────────────────────────────────────────────

// A real UObject name is short printable ASCII (e.g. "Class", "/Script/CoreUObject").
// A wrong base/stride decodes FName indices to garbage — non-ASCII (CJK) or control
// bytes — so an ASCII+alnum gate cleanly separates the true array from decoys.
static bool IsCleanAsciiName(const std::string& s) {
    if (s.empty() || s.size() > 128 || s == "None") return false;
    bool hasAlnum = false;
    for (unsigned char c : s) {
        if (c < 0x20 || c > 0x7E) return false;
        if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
            hasAlnum = true;
    }
    return hasAlnum;
}

// Score `base` as a standard UE5 chunked FUObjectArray (ObjObjects.Objects @ +0x10,
// NumElements @ +0x24): returns the count of clean-named objects among the first
// slots, or 0 if `base` is not a valid, name-resolving object array. The FUObjectItem
// stride is NOT fixed — Obsidian's UE5.3 packs it to 20 bytes (0x14) instead of the
// standard 24 (0x18) — so we try several and report the one that decodes cleanly via
// outStride (the winning stride, fed to Aura::InitWithExtendedLayout).
static int ScoreGObjectsStaticBase(uintptr_t base, int* outStride) {
    uintptr_t chunkTable = 0;
    if (!Macht::ReadSafe(base + 0x10, chunkTable)) return 0;     // ObjObjects.Objects
    chunkTable = Aura::DecryptObjectPtr(chunkTable);
    if (!LooksLikeDataPtr(chunkTable)) return 0;
    int32_t num = 0;
    if (!Macht::ReadSafe(base + 0x24, num)) return 0;           // NumElements
    if (num < 16 || num > 0x800000) return 0;
    uintptr_t chunk0 = 0;
    if (!Macht::ReadSafe(chunkTable, chunk0) || !LooksLikeDataPtr(chunk0)) return 0;

    // The first chunk is a contiguous FUObjectItem[]; the first entries are the
    // permanent core objects (Class / Package / etc.), all named. Object ptr @ +0x00.
    static const int kStrides[] = { 0x14, 0x18, 0x10, 0x20 };   // 20 (Obsidian-packed), 24 (std), 16, 32
    const int kProbe = 64;
    int bestClean = 0, bestStride = 0;
    for (int stride : kStrides) {
        int scanned = 0, clean = 0;
        for (int i = 0; i < kProbe && i < num; ++i) {
            uintptr_t obj = 0;
            if (!Macht::ReadSafe(chunk0 + static_cast<uintptr_t>(i) * stride, obj)) continue;
            if (!LooksLikeDataPtr(obj)) continue;
            ++scanned;
            uint32_t nameIdx = 0;
            if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx)) continue;
            if (IsCleanAsciiName(Serie::GetString(nameIdx))) ++clean;
        }
        // Require a clear majority of clean names plus an absolute floor.
        if (scanned >= 8 && clean >= 6 && clean * 2 >= scanned && clean > bestClean) {
            bestClean = clean;
            bestStride = stride;
        }
    }
    if (outStride) *outStride = bestStride;
    return bestClean;
}

uintptr_t FindGObjectsStaticStruct(int* outItemStride) {
    if (outItemStride) *outItemStride = 0;
    Sein::Info("SCAN:GObj", "FindGObjectsStaticStruct: scanning for a static FUObjectArray...");

    uintptr_t modBase = Macht::GetModuleBase(nullptr);
    if (!modBase) return 0;
    auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(modBase);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;
    auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(modBase + static_cast<DWORD>(dos->e_lfanew));
    if (nt->Signature != IMAGE_NT_SIGNATURE) return 0;

    const IMAGE_SECTION_HEADER* section = IMAGE_FIRST_SECTION(nt);
    uintptr_t codeStart = 0, codeEnd = 0, dataStart = 0, dataEnd = 0;
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++section) {
        if (!section->Misc.VirtualSize || !section->VirtualAddress) continue;
        uintptr_t s0 = modBase + section->VirtualAddress;
        uintptr_t s1 = s0 + section->Misc.VirtualSize;
        if (section->Characteristics & IMAGE_SCN_MEM_EXECUTE) {
            if (!codeStart || s0 < codeStart) codeStart = s0;
            if (s1 > codeEnd) codeEnd = s1;
        } else if (section->Characteristics & IMAGE_SCN_MEM_WRITE) {
            if (!dataStart || s0 < dataStart) dataStart = s0;
            if (s1 > dataEnd) dataEnd = s1;   // cover the zero-init (BSS) tail too
        }
    }
    if (!codeStart || !dataStart) return 0;

    // Collect every unique writable-.data slot referenced by lea/mov reg,[rip+disp].
    std::vector<uintptr_t> targets;
    targets.reserve(1u << 16);
    for (uintptr_t scan = codeStart; scan + 7 < codeEnd; ++scan) {
        // (G2) Whole-.text walk, and the sibling of DataScanGObjectsCandidates' above.
        // Also a RECOVERY path; returning 0 makes Frieren fall through to the heap
        // fallback, and Flamme::UpdateGObjectsMethod is inside the success branch only,
        // so an aborted run memoizes nothing.
        if ((scan & 0xFFF) == 0 && Tot::Requested()) {
            Sein::Warn("SCAN:GObj", "FindGObjectsStaticStruct: aborted (client gone / shutdown)");
            return 0;
        }
        uint8_t b0 = 0, b1 = 0, b2 = 0;
        if (!Macht::ReadSafe(scan, b0)) continue;
        if (b0 != 0x48 && b0 != 0x4C) continue;
        if (!Macht::ReadSafe(scan + 1, b1)) continue;
        if (b1 != 0x8B && b1 != 0x8D) continue;        // MOV / LEA
        if (!Macht::ReadSafe(scan + 2, b2)) continue;
        if (!Macht::IsRipRelativeModRM(b2)) continue;   // mod=00 AND r/m=101
        int32_t rel = 0;
        if (!Macht::ReadSafe<int32_t>(scan + 3, rel)) continue;
        uintptr_t tgt = scan + 7 + rel;
        if (tgt >= dataStart && tgt < dataEnd) targets.push_back(tgt);
    }
    std::sort(targets.begin(), targets.end());
    targets.erase(std::unique(targets.begin(), targets.end()), targets.end());
    Sein::Info("SCAN:GObj", "FindGObjectsStaticStruct: probing %zu unique .data refs", targets.size());

    // Probe a small window around each referenced slot — a code ref may point at any
    // member of the struct, so step back up to ObjAvailableList (+0x58) to find the base.
    uintptr_t bestBase = 0;
    int bestScore = 0, bestStride = 0;
    for (uintptr_t tgt : targets) {
        for (int off = -0x58; off <= 0x10; off += 8) {
            int stride = 0;
            int score = ScoreGObjectsStaticBase(tgt + off, &stride);
            if (score > bestScore) {
                bestScore = score;
                bestBase = tgt + off;
                bestStride = stride;
                if (bestScore >= 32) {   // unambiguous: a dense run of clean core objects
                    if (outItemStride) *outItemStride = bestStride;
                    Sein::Info("SCAN:GObj", "FindGObjectsStaticStruct: static GObjects at 0x%llX (clean=%d, stride=%d)",
                               static_cast<unsigned long long>(bestBase), bestScore, bestStride);
                    return bestBase;
                }
            }
        }
    }
    if (bestBase) {
        if (outItemStride) *outItemStride = bestStride;
        Sein::Info("SCAN:GObj", "FindGObjectsStaticStruct: static GObjects at 0x%llX (clean=%d, stride=%d)",
                   static_cast<unsigned long long>(bestBase), bestScore, bestStride);
    } else {
        Sein::Warn("SCAN:GObj", "FindGObjectsStaticStruct: no static FUObjectArray found");
    }
    return bestBase;
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
    uintptr_t                      scanAddr     = 0;  // AOB match address (instruction that references the pointer)
    const char*                    winningId    = nullptr;
    const AobSignature*            winningSig   = nullptr;  // Winning pattern (for AOB metadata)
    bool                           hintUsed     = false;    // true if winner came from hint cache
};

// The `*_method` label for a successful scan. Every caller used to hardcode "aob", which
// was wrong for the ~6 symbol-export and CallFollow entries: they reported method="aob"
// next to a pattern_id of "GWLD_EXP" and an empty AOB triple, three fields in the same
// payload contradicting each other.
//
// winningSig is null only if a winner was recorded without its signature; "aob" is the
// right answer there because it is what every such path used to report, and a guess that
// changes behaviour is worse than a guess that preserves it.
static const char* ScanMethodFor(const ScanReport& report) {
    return report.winningSig ? ScanMethodName(report.winningSig->resolve) : "aob";
}

// Try to resolve a symbol export from any loaded module.
// Reuses the existing TrySymbolExport helper.
static uintptr_t ResolveSymbolExport(const AobSignature& sig, ValidatorFn validate) {
    uintptr_t addr = TrySymbolExport(sig.pattern);
    if (!addr) return 0;

    // Direct validation
    if (validate(addr)) return addr;

    // Deref and validate
    uintptr_t derefed = 0;
    if (Macht::ReadSafe(addr, derefed) && derefed && validate(derefed))
        return derefed;

    return 0;
}

// Scan the first N bytes of a function body for RIP-relative LEA/MOV
// instructions and validate each resolved target.
// Shared by CallFollow and SymbolCallFollow.
static uintptr_t ScanFunctionBodyForRipRef(
    uintptr_t funcAddr, const char* sigId, ValidatorFn validate, int scanBytes = 256)
{
    // BOUND THE SCAN TO THE REAL CALLEE. Without this the loop runs a flat 256 bytes
    // and its only stop condition is an unreadable page, which never fires inside a
    // module's .text — so on a short callee it reads straight into the next function
    // and can "succeed" on a reference that has nothing to do with the one we
    // followed. That is not hypothetical: on UE 4.23 GNAM_V7 followed a 124-byte
    // FName ctor and resolved GNames from a ref at +0x94, i.e. 132 bytes past the
    // end, inside a DIFFERENT FName constructor's lazy-init prologue. It got the
    // right answer for the wrong reason, and 17 of 19 callees in the corpus overrun.
    //
    // Clamp with (end - funcAddr), NOT the entry length: funcAddr is frequently
    // mid-entry (6.8%-39.8% of call targets across the corpus), so the entry's own
    // length would over-run by exactly the part already behind us.
    //
    // A false return means LEAF or no exception directory — indistinguishable, and
    // both keep the historical 256, so that path is bit-identical to today's
    // behaviour and cannot regress anything. It is also the only path that can still
    // overrun; measured leaf share is 12-17% of call targets.
    int limit = scanBytes;
    uintptr_t fnBegin = 0, fnEnd = 0;
    const bool bounded = Macht::GetFunctionExtent(funcAddr, fnBegin, fnEnd)
                      && fnEnd > funcAddr
                      && static_cast<uintptr_t>(limit) > (fnEnd - funcAddr);
    if (bounded) limit = static_cast<int>(fnEnd - funcAddr);

    for (int off = 0; off + 7 <= limit; ++off) {
        uint8_t b0 = 0, b1 = 0, b2 = 0;
        if (!Macht::ReadSafe(funcAddr + off, b0)) break;
        if (b0 != 0x48 && b0 != 0x4C) continue;
        if (!Macht::ReadSafe(funcAddr + off + 1, b1)) break;
        if (b1 != 0x8B && b1 != 0x8D) continue;
        if (!Macht::ReadSafe(funcAddr + off + 2, b2)) break;
        if (!Macht::IsRipRelativeModRM(b2)) continue; // mod=00 AND r/m=101

        uintptr_t target = Macht::ResolveRIP(funcAddr + off, 3, 7);
        if (!target) continue;

        uintptr_t candidate = target;
        if (b1 == 0x8B) { // MOV — need deref
            if (!Macht::ReadSafe(target, candidate) || !candidate) continue;
        }

        if (validate(candidate)) {
            // Log WHICH limit applied. If a future engine shifts the site, an
            // "unbounded" hit near the 256 mark is the tell that we are reading past
            // the callee again — the failure this bound exists to make visible.
            LOG_INFO("FuncBodyScan [%s]: Found at 0x%llX (func+0x%X of %d, %s, %s)",
                     sigId, (unsigned long long)candidate, off, limit,
                     b1 == 0x8D ? "LEA" : "MOV",
                     bounded ? "bounded by .pdata" : "UNBOUNDED (leaf/no-xdata)");
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
    Macht::ReadSafe(callInstr, opcode);
    if (opcode != 0xE8) return 0;

    int32_t rel32 = 0;
    if (!Macht::ReadSafe(callInstr + 1, rel32)) return 0;
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
    uintptr_t target = Macht::ResolveRIP(instrAddr, sig.opcodeLen, sig.totalLen);
    if (!target) return 0;

    // Try with adjustment first (e.g. -0x10), then without
    auto tryValidate = [&](uintptr_t addr) -> uintptr_t {
        if (!addr) return 0;
        // For GWorld write-patterns: check if pointer value is accessible
        if (sig.target == AobTarget::GWorld) {
            uintptr_t world = 0;
            if (!Macht::ReadSafe(addr, world)) return 0;
            if (!sig.gworldAllowNull && world == 0) return 0;
            // Basic pointer sanity for non-null values
            if (world != 0 && (!Grimoire::IsUserspacePointer(world)))
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
        if (Macht::ReadSafe(target, value) && value) {
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

// Batch size for multi-anchor AOBScanBatch. 8 patterns per batch means
// at most 8 AVX2 cmpeq operations per 32-byte window — negligible overhead
// compared to the massive memory bandwidth savings from scanning .text once
// per batch instead of once per pattern.
static constexpr int kBatchSize = 8;

// ── Module anchor for the multi-module fallback (D1) ─────────────────────────
//
// GObjects resolves FIRST, so by the time GNames/GWorld/GEngine scan we know which
// module the engine's globals actually live in. That fact is worth more than any list.
//
// THE BUG THIS EXISTS FOR (measured 2026-08-05, stock UE 5.4 Development package):
// every in-executable GNames pattern missed — the tables are Shipping-tuned — so the
// multi-module fallback ran and matched a data pattern inside
// `Engine\Binaries\Win64\EOSSDK-Win64-Shipping.dll`, whose pointer happened to reach a
// plausible name pool. ValidateGNames accepted it, and GObjects (in the exe, 0x7FF675…)
// and GNames (0x7FFCEF…) ended up in different modules on a MONOLITHIC build, which
// cannot be right. Everything downstream failed from that one address: no Guid/Vector
// struct -> offsets fell back to defaults wrong for this build -> GWorld would not
// deref -> Start-from-GWorld and Value Search both failed. One misresolution, four
// symptoms, and the scan reported success throughout.
//
// The rule is structural, NOT a denylist of SDK names. A denylist is a list, and this
// repo has been bitten three times by fixes verified against their own list rather than
// against the world (B34's CE filenames, B14's thread procs, B47's session). "The engine
// globals are all in one module unless the build is modular" needs no maintenance and
// cannot go stale.
//
// Multi-module support itself stays: a genuinely modular build puts GNames in
// CoreUObject.dll, which is exactly why the pattern that won here is named GNAM_SAT425
// (Satisfactory 4.25). When the anchor is a DLL we only REORDER; we refuse nothing.
static uintptr_t s_moduleAnchor = 0;

static HMODULE ModuleOfAddress(uintptr_t addr) {
    HMODULE h = nullptr;
    if (!addr) return nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                       GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       reinterpret_cast<LPCWSTR>(addr), &h);
    return h;
}

static std::string ModuleNameOf(HMODULE h) {
    wchar_t path[MAX_PATH] = {};
    if (!h || !GetModuleFileNameW(h, path, MAX_PATH)) return "(unknown)";
    std::wstring w(path);
    auto slash = w.find_last_of(L"\\/");
    if (slash != std::wstring::npos) w = w.substr(slash + 1);
    return Utf8Helpers::EncodeUtf16(w.c_str(), w.size());
}

/// Should a multi-module candidate at `resolved` be refused?
/// Only when the anchor says the build is monolithic and the candidate is elsewhere.
///
/// Does NOT log: a pattern with N matches all resolving to the same foreign pointer would
/// print N identical lines, and the first live run did exactly that -- 8 to 11 copies per
/// pattern, five patterns deep. The caller accumulates and prints one line per pattern.
static bool RefuseForeignModule(uintptr_t resolved, std::string* outModule) {
    if (!s_moduleAnchor || !resolved) return false;
    const HMODULE mainExe   = GetModuleHandleW(nullptr);
    const HMODULE anchorMod = ModuleOfAddress(s_moduleAnchor);
    if (!anchorMod || anchorMod != mainExe) return false;   // modular build — allow

    const HMODULE candMod = ModuleOfAddress(resolved);
    if (candMod == mainExe) return false;

    if (outModule && outModule->empty()) *outModule = ModuleNameOf(candMod);
    return true;
}

void SetModuleAnchor(uintptr_t gobjects) { s_moduleAnchor = gobjects; }

static uintptr_t ScanForTarget(
    const AobSignature* patterns, size_t count,
    ValidatorFn validate, ScanReport& report,
    bool tryMultiModule,
    const char* hintPatternId = nullptr)
{
    // Sort entries by priority (patterns array is constexpr, so copy pointers)
    std::vector<const AobSignature*> sorted;
    sorted.reserve(count);
    for (size_t i = 0; i < count; ++i) sorted.push_back(&patterns[i]);
    std::sort(sorted.begin(), sorted.end(),
              [](const AobSignature* a, const AobSignature* b) { return a->priority < b->priority; });

    // ── Hint phase: try cached winning pattern first ─────────────────
    // If a previous scan recorded a winning AOB pattern for this PE hash,
    // try it first as a single-pattern scan before the normal phases.
    // On hit: immediate return (massive speedup for repeat scans).
    // On miss: remove from list (avoid re-scanning) and fall through.
    if (hintPatternId && hintPatternId[0]) {
        auto it = std::find_if(sorted.begin(), sorted.end(),
            [&](const AobSignature* s) { return strcmp(s->id, hintPatternId) == 0; });

        if (it != sorted.end()) {
            const AobSignature* hintSig = *it;

            // Only use hint for AOB-type patterns (not exports/callfollow)
            if (hintSig->resolve != AobResolve::SymbolExport &&
                hintSig->resolve != AobResolve::SymbolCallFollow &&
                hintSig->resolve != AobResolve::CallFollow) {

                LOG_INFO("[%s] Hint: trying cached pattern '%s' first...",
                         report.targetName, hintPatternId);

                g_validationDbgCount = 0;

                // ⚠ AOBScanALL, not AOBScan. The hint fast path MUST NOT be weaker than the
                // full scan that produced the hint, and it was: Macht::AOBScan returns the
                // FIRST match only (ScanRegion returns on first hit, and AOBScan returns on
                // the first section that hits), while the batch path this shortcuts hands
                // Pass 1 EVERY match and walks them until one validates. So a pattern whose
                // first match failed validation was logged as a MISS and then ERASED from the
                // pattern set below — hiding it from the very path that would have found the
                // later, valid match.
                //
                // Live-reproduced on the maintainer's own logs, same binary (PE 6A7EA6031...),
                // five minutes apart: a cold run reported
                //   "GNames: 31 patterns tried, 10 with hits, winner: GNAM_V1 -> 0x7FF63CD568C0"
                // and the next run, using the hint that run had just saved, reported
                //   "Hint MISS: 'GNAM_V1'" ... "GNames: 33 patterns tried, 12 with hits, NONE validated".
                // GNAM_V1 had 166 matches on that image and the winner was not the first.
                // GObjects showed the same shape and went 0.66 s -> 6.14 s. Worse, the false
                // "not found" was then PERSISTED over a good hint, so the failure oscillates:
                // fail -> hint destroyed -> cold scan succeeds -> hint saved -> fail again.
                auto hintT0 = std::chrono::high_resolution_clock::now();
                std::vector<uintptr_t> hintHits = Macht::AOBScanAll(hintSig->pattern);
                auto hintT1 = std::chrono::high_resolution_clock::now();
                auto hintUs = std::chrono::duration_cast<std::chrono::microseconds>(hintT1 - hintT0).count();

                PatternScanResult pr;
                pr.id = hintSig->id;
                // The REPORTED count is the real one. It used to be `matchAddr ? 1 : 0`, so a
                // 166-match pattern logged "hits=1" — the report and the reality computed by
                // different code paths (audit #4 root cause #1), which is what hid this.
                pr.hitCount = static_cast<int>(hintHits.size());   // hitCount is int (see PatternScanResult)

                // Same cap and the same "first validated match wins" rule as Pass 1, so the
                // two paths cannot disagree about what this pattern resolves to.
                constexpr size_t kMaxValidateHint = 4096;
                uintptr_t matchAddr = 0;
                uintptr_t resolved   = 0;
                size_t    hintTried  = 0;
                for (uintptr_t cand : hintHits) {
                    if (++hintTried > kMaxValidateHint) {
                        LOG_WARN("[%s] %s: %zu matches — hint validation capped at %zu",
                                 report.targetName, hintSig->id, hintHits.size(), kMaxValidateHint);
                        break;
                    }
                    uintptr_t r2 = TryResolveMatch(cand, *hintSig, validate);
                    if (r2) { matchAddr = cand; resolved = r2; break; }
                }

                if (!hintHits.empty()) {
                    pr.selected = resolved;
                    pr.validated = (resolved != 0);
                    report.results.push_back(pr);

                    if (resolved) {
                        LOG_INFO("[%s] Hint HIT: '%s' -> 0x%llX (scan %lld us, skipping remaining patterns)",
                                 report.targetName, hintPatternId,
                                 static_cast<unsigned long long>(resolved),
                                 static_cast<long long>(hintUs));
                        report.finalAddress = resolved;
                        report.scanAddr = matchAddr;
                        report.winningId = hintSig->id;
                        report.winningSig = hintSig;
                        report.hintUsed = true;
                        return resolved;
                    }
                } else {
                    report.results.push_back(pr);
                }

                LOG_INFO("[%s] Hint MISS: '%s' (%zu matches, none validated; scan %lld us) — "
                         "falling back to full scan",
                         report.targetName, hintPatternId, hintHits.size(),
                         static_cast<long long>(hintUs));

                // Erase ONLY when the pattern genuinely produced no matches at all. A pattern
                // that matched but failed validation must stay in the batch set: the hint path
                // and Pass 1 now apply the identical rule, so re-scanning it costs one more
                // batch entry and buys back the case where the image changed such that a
                // different match validates. Erasing on a validation failure is what turned a
                // resolvable target into "NONE validated" (see the block above).
                if (hintHits.empty()) {
                    sorted.erase(it);
                }
            } else {
                LOG_INFO("[%s] Hint: pattern '%s' is non-AOB type (%d), skipping hint",
                         report.targetName, hintPatternId, static_cast<int>(hintSig->resolve));
            }
        } else {
            LOG_INFO("[%s] Hint: cached pattern '%s' not found in current pattern set",
                     report.targetName, hintPatternId);
        }
    }

    // ── Phase 1: Handle non-AOB patterns sequentially (unchanged) ────
    // Symbol exports, CallFollow, etc. are rare (priority 0-5) and don't
    // benefit from batching. Process them first with immediate early exit.
    std::vector<const AobSignature*> aobPatterns;
    aobPatterns.reserve(sorted.size());

    for (const AobSignature* sig : sorted) {
        g_validationDbgCount = 0;

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
                report.winningSig = sig;
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
                report.winningSig = sig;
                return result;
            }
            continue;
        }

        // ── CallFollow ───────────────────────────────────────
        if (sig->resolve == AobResolve::CallFollow) {
            uintptr_t matchAddr = Macht::AOBScan(sig->pattern);
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
                report.scanAddr = matchAddr;
                report.winningId = sig->id;
                report.winningSig = sig;
                return pr.selected;
            }
            continue;
        }

        // Collect AOB patterns for batched scanning
        aobPatterns.push_back(sig);
    }

    // ── Phase 2: Batched AOB scanning ────────────────────────────────
    // Process AOB patterns in batches of kBatchSize. Each batch scans
    // .text ONCE for all patterns in the batch (multi-anchor AVX2).
    const int totalAob = static_cast<int>(aobPatterns.size());
    const int totalBatches = (totalAob + kBatchSize - 1) / kBatchSize;

    auto batchT0 = std::chrono::high_resolution_clock::now();

    for (int batchIdx = 0; batchIdx < totalBatches; ++batchIdx) {
        const int batchStart = batchIdx * kBatchSize;
        const int batchEnd   = (std::min)(batchStart + kBatchSize, totalAob);
        const int batchCount = batchEnd - batchStart;

        // Build pattern string array + index mapping for AOBScanBatch
        std::vector<const char*> patStrings(batchCount);
        std::vector<int>         patIndices(batchCount);
        for (int j = 0; j < batchCount; ++j) {
            patStrings[j] = aobPatterns[batchStart + j]->pattern;
            patIndices[j] = j;
        }

        auto t0 = std::chrono::high_resolution_clock::now();
        auto batchResults = Macht::AOBScanBatch(
            patStrings.data(), patIndices.data(), batchCount);
        auto t1 = std::chrono::high_resolution_clock::now();
        auto batchUs = std::chrono::duration_cast<std::chrono::microseconds>(t1 - t0).count();

        LOG_INFO("[%s] Batch %d/%d (%d patterns): scanned in %lld us",
                 report.targetName, batchIdx + 1, totalBatches, batchCount,
                 static_cast<long long>(batchUs));

        // ── Two-pass validation within each batch ──────────────────────
        // Pass 1: Validate patterns that got batch hits (no extra I/O).
        //         If a winner is found here, multi-module fallback for
        //         zero-hit patterns is skipped entirely.
        // Pass 2: Multi-module fallback for zero-hit patterns (expensive,
        //         only runs when pass 1 finds no winner).

        // Pass 1: batch-hit patterns in priority order
        for (int j = 0; j < batchCount; ++j) {
            if (batchResults[j].matches.empty()) continue;

            const AobSignature* sig = aobPatterns[batchStart + j];
            g_validationDbgCount = 0;

            PatternScanResult pr;
            pr.id = sig->id;
            pr.hitCount = static_cast<int>(batchResults[j].matches.size());

            // Try to validate each match.
            //
            // Bounded (build 2404). Validation is not free — every candidate costs several
            // SEH-guarded reads — and an over-generic pattern can produce five-figure match
            // counts: the removed GNAM_D7_1 ("48 8D 0D ?? ?? ?? ?? E8", i.e. `lea rcx,[rip];
            // call`, three literal bytes) took 27,001 matches on a UE4.20 title and 104,897
            // on a UE4.27 one, ALL of which were validated before the scan could reach the
            // patterns that actually work (pri 800/840 there). The cap keeps that failure
            // mode bounded for any future pattern: if the correct site is not within the
            // first kMaxValidatePerPattern matches, the pattern was not selective enough to
            // be trusted anyway. Logged loudly so it is visible rather than silent.
            constexpr size_t kMaxValidatePerPattern = 4096;
            uintptr_t bestResult = 0;
            uintptr_t bestMatchAddr = 0;
            size_t tried = 0;
            for (uintptr_t matchAddr : batchResults[j].matches) {
                if (++tried > kMaxValidatePerPattern) {
                    LOG_WARN("[%s] %s: %zu matches — validation capped at %zu. This pattern is "
                             "too generic; consider tightening or removing it.",
                             report.targetName, sig->id,
                             batchResults[j].matches.size(), kMaxValidatePerPattern);
                    break;
                }
                uintptr_t resolved = TryResolveMatch(matchAddr, *sig, validate);
                if (resolved) {
                    bestResult = resolved;
                    bestMatchAddr = matchAddr;
                    break; // Take first validated match
                }
            }

            if (g_validationDbgCount > kMaxValidationDbgLogs) {
                LOG_INFO("[%s] %s: Validation debug output throttled (%d entries, showed first %d)",
                         report.targetName, sig->id, g_validationDbgCount, kMaxValidationDbgLogs);
            }

            pr.selected = bestResult;
            pr.validated = (bestResult != 0);
            report.results.push_back(pr);

            if (bestResult) {
                if (pr.hitCount > 1) {
                    LOG_INFO("[%s] %s: %d matches, validated -> 0x%llX",
                             report.targetName, sig->id, pr.hitCount,
                             (unsigned long long)bestResult);
                } else {
                    LOG_INFO("[%s] %s: Unique match -> 0x%llX",
                             report.targetName, sig->id,
                             (unsigned long long)bestResult);
                }

                auto totalUs = std::chrono::duration_cast<std::chrono::microseconds>(
                    std::chrono::high_resolution_clock::now() - batchT0).count();
                LOG_INFO("[%s] AOB scan total: %lld us (%d batches, winner in batch %d)",
                         report.targetName, static_cast<long long>(totalUs),
                         batchIdx + 1, batchIdx + 1);

                report.finalAddress = bestResult;
                report.scanAddr = bestMatchAddr;
                report.winningId = sig->id;
                report.winningSig = sig;
                return bestResult;
            }

            LOG_INFO("[%s] %s: %d match(es), none validated",
                     report.targetName, sig->id, pr.hitCount);
        }

        // Pass 2: multi-module fallback for zero-hit patterns
        if (tryMultiModule) {
            for (int j = 0; j < batchCount; ++j) {
                if (!batchResults[j].matches.empty()) continue; // processed in pass 1

                const AobSignature* sig = aobPatterns[batchStart + j];
                g_validationDbgCount = 0;

                auto multiMatches = Macht::AOBScanAllModules(sig->pattern);

                PatternScanResult pr;
                pr.id = sig->id;
                pr.hitCount = static_cast<int>(multiMatches.size());

                if (multiMatches.empty()) {
                    report.results.push_back(pr);
                    continue;
                }

                // Try candidates in the ANCHOR's module first. On a monolithic build a
                // match anywhere else is then refused outright rather than merely
                // deprioritised — see the s_moduleAnchor block above for the EOSSDK
                // misresolution this prevents.
                if (s_moduleAnchor) {
                    const HMODULE anchorMod = ModuleOfAddress(s_moduleAnchor);
                    std::stable_sort(multiMatches.begin(), multiMatches.end(),
                        [&](uintptr_t a, uintptr_t b) {
                            const bool aIn = ModuleOfAddress(a) == anchorMod;
                            const bool bIn = ModuleOfAddress(b) == anchorMod;
                            return aIn && !bIn;   // same-module first, order otherwise kept
                        });
                }

                uintptr_t bestResult = 0;
                uintptr_t bestMatchAddr = 0;
                int         refusedCount = 0;
                uintptr_t   refusedAddr  = 0;
                std::string refusedModule;
                for (uintptr_t matchAddr : multiMatches) {
                    uintptr_t resolved = TryResolveMatch(matchAddr, *sig, validate);
                    // Gate on the RESOLVED address, not the match site: the match is only
                    // where the instruction sits, while `resolved` is the pointer we are
                    // about to hand the whole dumper.
                    if (resolved && RefuseForeignModule(resolved, &refusedModule)) {
                        ++refusedCount;
                        refusedAddr = resolved;
                        continue;
                    }
                    if (resolved) {
                        bestResult = resolved;
                        bestMatchAddr = matchAddr;
                        break;
                    }
                }
                if (refusedCount) {
                    Sein::Warn("SCAN", "[%s] %s: REFUSED %d match(es) resolving to 0x%llX in '%s' "
                               "— GObjects resolved inside the main executable, so this build is "
                               "monolithic and the engine globals cannot live in another module",
                               report.targetName, sig->id, refusedCount,
                               (unsigned long long)refusedAddr, refusedModule.c_str());
                }

                if (g_validationDbgCount > kMaxValidationDbgLogs) {
                    LOG_INFO("[%s] %s: Validation debug output throttled (%d entries, showed first %d)",
                             report.targetName, sig->id, g_validationDbgCount, kMaxValidationDbgLogs);
                }

                pr.selected = bestResult;
                pr.validated = (bestResult != 0);
                report.results.push_back(pr);

                if (bestResult) {
                    if (pr.hitCount > 1) {
                        LOG_INFO("[%s] %s: %d matches (multi-module), validated -> 0x%llX",
                                 report.targetName, sig->id, pr.hitCount,
                                 (unsigned long long)bestResult);
                    } else {
                        LOG_INFO("[%s] %s: Unique match (multi-module) -> 0x%llX",
                                 report.targetName, sig->id,
                                 (unsigned long long)bestResult);
                    }

                    auto totalUs = std::chrono::duration_cast<std::chrono::microseconds>(
                        std::chrono::high_resolution_clock::now() - batchT0).count();
                    LOG_INFO("[%s] AOB scan total: %lld us (%d batches, winner in batch %d, multi-module)",
                             report.targetName, static_cast<long long>(totalUs),
                             batchIdx + 1, batchIdx + 1);

                    report.finalAddress = bestResult;
                    report.scanAddr = bestMatchAddr;
                    report.winningId = sig->id;
                    report.winningSig = sig;
                    return bestResult;
                }

                LOG_INFO("[%s] %s: %d match(es) (multi-module), none validated",
                         report.targetName, sig->id, pr.hitCount);
            }
        } else {
            // Record zero-hit patterns for report (no multi-module)
            for (int j = 0; j < batchCount; ++j) {
                if (!batchResults[j].matches.empty()) continue;
                PatternScanResult pr;
                pr.id = aobPatterns[batchStart + j]->id;
                report.results.push_back(pr);
            }
        }
    }

    auto totalUs = std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::high_resolution_clock::now() - batchT0).count();
    LOG_INFO("[%s] AOB scan exhausted: %lld us (%d patterns in %d batches, no winner)",
             report.targetName, static_cast<long long>(totalUs), totalAob, totalBatches);

    return 0;
}

// Log the scan report summary and per-pattern details for analysis.
// Output goes to scan.log for post-mortem diagnosis.
// Extract summary stats from a ScanReport for EnginePointers propagation.
static void ExtractScanStats(const ScanReport& report, int& triedCount, int& hitCount) {
    triedCount = static_cast<int>(report.results.size());
    hitCount = 0;
    for (const auto& r : report.results) {
        if (r.hitCount > 0) ++hitCount;
    }
}

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
        Sein::Info("SCAN", "=== %s: %d patterns tried, %d with hits, winner: %s -> 0x%llX%s ===",
                 report.targetName, totalPatterns, patternsWithHits,
                 report.winningId ? report.winningId : "?",
                 (unsigned long long)report.finalAddress,
                 report.hintUsed ? " (hint)" : "");
    } else {
        Sein::Warn("SCAN", "=== %s: %d patterns tried, %d with hits, NONE validated ===",
                 report.targetName, totalPatterns, patternsWithHits);
    }

    // Per-pattern detail: list all patterns with hits (INFO) and 0-hit (DEBUG)
    for (auto& r : report.results) {
        if (r.validated) {
            Sein::Info("SCAN", "  [%s] %-16s hits=%-4d -> 0x%llX  [WINNER]",
                     report.targetName, r.id, r.hitCount,
                     (unsigned long long)r.selected);
        } else if (r.hitCount > 0) {
            Sein::Info("SCAN", "  [%s] %-16s hits=%-4d  (not validated)",
                     report.targetName, r.id, r.hitCount);
        } else {
            Sein::Debug("SCAN", "  [%s] %-16s hits=0",
                      report.targetName, r.id);
        }
    }
}

// ============================================================
// Scan method tracking — set by each Find function, read by FindAll()
// ============================================================
static const char* s_gobjectsMethod        = "not_found";
static const char* s_gnamesMethod           = "not_found";
static const char* s_gworldMethod           = "not_found";
static const char* s_sparseDelegatesMethod  = "not_found";

// File-scope ScanReports — promoted from local so FindAll() can read winningId + stats
static ScanReport s_gobjectsReport;
static ScanReport s_gnamesReport;
static ScanReport s_gworldReport;
static ScanReport s_sparseReport;

// ============================================================
// FindGObjects — unified scan + data-section fallback
// ============================================================

uintptr_t FindGObjects(const char* hintPatternId) {
    s_gobjectsMethod = "not_found";
    Sein::Info("SCAN:GObj", "FindGObjects: Scanning for GObjects...");

    s_gobjectsReport = ScanReport{};
    ScanReport& report = s_gobjectsReport;
    report.targetName = "GObjects";

    uintptr_t result = ScanForTarget(
        Sig::GOBJECTS_PATTERNS, std::size(Sig::GOBJECTS_PATTERNS),
        ValidateGObjects, report, /*tryMultiModule=*/true, hintPatternId);

    LogScanReport(report);

    if (result) {
        s_gobjectsMethod = ScanMethodFor(report);
    } else {
        // Fallback: exhaustive data-section pointer scan
        Sein::Warn("SCAN:GObj", "FindGObjects: All patterns failed, trying data-section scan fallback...");
        result = FindGObjectsByDataScan();
        if (result) s_gobjectsMethod = "data_scan";
    }

    // Anchor every LATER target's multi-module fallback to whichever module GObjects
    // came from. This is the only place that knows it, and it must be set however
    // GObjects was found -- the data-scan fallback anchors just as well as the AOB.
    if (result) {
        SetModuleAnchor(result);
        Sein::Info("SCAN:GObj", "Module anchor set to '%s' — later targets must resolve there "
                   "unless this build is modular",
                   ModuleNameOf(ModuleOfAddress(result)).c_str());
    }

    if (!result) {
        Sein::Error("SCAN:GObj", "FindGObjects: All patterns and fallback scan failed");
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
// ─────────────────────────────────────────────────────────────────────────────
// Obfuscated FNameEntry payloads (licensee forks) — EXPERIMENTAL-GATED
//
// Some forks keep the stock 2-byte header but insert a field after it and XOR the
// characters. MindsEye (Build A Rocket Boy, UE 5.4.4):
//     [+0x00] u16 header  (stock: bIsWide:1 | LowercaseProbeHash:5 | Len:10)
//     [+0x02] u16 tag     (non-stock — selects this block's XOR key)
//     [+0x04] chars[len]  (XOR-obfuscated)
//
// The key is recovered LOCALLY from the ciphertext — no game code is called. The
// game's own routine takes a lock before its key lookup, so a fault inside it would
// unwind out of game frames with that lock still held and permanently wedge every
// later FName::ToString; recovering the key ourselves has no such failure mode.
//
// Acceptance is the SAME standard as the stock path — entry 0 must decode to exactly
// "None" — plus a chain-walk corroboration, so this cannot accept a pool the stock
// path would have rejected for being the wrong address.
static int       g_nameChunksOffset = 0;   // pool -> Blocks[] offset proved by the accept
static int       g_namePayloadGap   = 0;   // 0 = stock; >0 = obfuscated fork
static uintptr_t g_nameKeyTableCtx  = 0;   // fork's tag -> key hash map (obfuscated forks only)

// Locate the fork's tag->key table WITHOUT calling any game code.
//
// The fork's own name-decrypt routine has a distinctive, semantically anchored
// prologue (read header / point at entry+4 / len = header >> 6 / take the u16 tag at
// entry+2). From its match we follow two purely static links:
//     match+0x2F  E8 rel32          -> the table-context getter
//     getter      48 8D 05 rip+d    -> the static context object
// The context is an ordinary open-hash map we can read directly, which is exact —
// no heuristics, and no control ever transfers into game code (the game's own lookup
// takes an SRW lock before probing, so a fault inside it would unwind out of game
// frames with that lock held and wedge every later FName::ToString).
static uintptr_t ResolveNameKeyTable() {
    auto hits = Macht::AOBScanAll(Sig::AOB_NAMEDECRYPT_ME1);
    if (hits.size() != 1) {
        Sein::Warn("SCAN:GNam", "ObfuscatedNames: decrypt-routine pattern matched %zu time(s), need exactly 1",
                   hits.size());
        return 0;
    }
    uintptr_t fn = hits[0];

    // The second call in the match is the key-table ctx getter (rel32 at +1).
    constexpr int kCall = Sig::AOB_NAMEDECRYPT_ME1_CTX_CALL_OFF;
    int32_t rel = 0;
    if (!Macht::ReadSafe(fn + kCall + 1, rel)) return 0;
    uintptr_t getter = fn + kCall + 5 + static_cast<intptr_t>(rel);

    // First `lea rax, [rip+disp32]` inside the getter is the context object.
    unsigned char code[0x60] = {};
    if (!Macht::ReadBytesSafe(getter, code, sizeof(code))) return 0;
    for (size_t i = 0; i + 7 <= sizeof(code); ++i) {
        if (code[i] == 0x48 && code[i + 1] == 0x8D && code[i + 2] == 0x05) {
            int32_t d = 0;
            memcpy(&d, code + i + 3, 4);
            uintptr_t ctx = getter + i + 7 + static_cast<intptr_t>(d);
            Sein::Info("SCAN:GNam", "ObfuscatedNames: decrypt fn=0x%llX getter=0x%llX keyTable=0x%llX",
                       static_cast<unsigned long long>(fn),
                       static_cast<unsigned long long>(getter),
                       static_cast<unsigned long long>(ctx));
            return ctx;
        }
    }
    Sein::Warn("SCAN:GNam", "ObfuscatedNames: no rip-relative LEA in the ctx getter at 0x%llX",
               static_cast<unsigned long long>(getter));
    return 0;
}

static bool ObfChainOk(uintptr_t blockBase, uint8_t key, int gap, int minEntries) {
    int  entries = 0;
    int  pos     = 0;
    char buf[256];
    while (entries < 16) {
        uint16_t header = 0;
        if (!Macht::ReadSafe(blockBase + pos, header)) break;
        if (header & 1) break;                      // wide entry — stop, do not judge
        int len = header >> 6;
        if (len == 0 || len > 255) return false;
        int strStart = pos + 2 + gap;
        if (!Macht::ReadBytesSafe(blockBase + strStart, buf, len)) break;
        for (int i = 0; i < len; ++i) {
            uint8_t c = static_cast<uint8_t>(buf[i]) ^ key;
            bool ident = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                         (c >= '0' && c <= '9') || c == '_';
            if (!ident) return false;
        }
        ++entries;
        pos = strStart + len;
        if (pos & 1) ++pos;                         // 2-byte entry alignment
    }
    return entries >= minEntries;
}

static bool TryObfuscatedPool(uintptr_t addr, int off, uintptr_t chunk0) {
    if (!Flamme::IsExperimentalEnabled()) return false;

    constexpr int GAP = 2;   // the only forked geometry seen so far
    uint16_t header = 0;
    if (!Macht::ReadSafe(chunk0, header)) return false;
    if (header & 1) return false;                   // entry 0 ("None") is never wide
    if ((header >> 6) != 4) return false;           // len must be 4

    uint8_t enc[4] = {};
    if (!Macht::ReadBytesSafe(chunk0 + 2 + GAP, enc, 4)) return false;

    static const uint8_t kNone[4] = { 'N', 'o', 'n', 'e' };
    // A single-byte XOR that maps these 4 bytes onto "None" exists only if the three
    // inter-byte deltas already match, so this costs one compare, not 256.
    if ((enc[0] ^ enc[1]) != (kNone[0] ^ kNone[1])) return false;
    if ((enc[1] ^ enc[2]) != (kNone[1] ^ kNone[2])) return false;
    if ((enc[2] ^ enc[3]) != (kNone[2] ^ kNone[3])) return false;
    uint8_t key = static_cast<uint8_t>(enc[0] ^ kNone[0]);

    // Corroborate: the rest of block 0 must chain-decode as identifiers under the
    // same key. Random memory that happens to satisfy "None" dies here.
    if (!ObfChainOk(chunk0, key, GAP, 6)) return false;

    // The key table is what makes the OTHER blocks decodable — each carries its own
    // key, and a block whose tag is absent from the table is stored in plain text
    // (the fork's lookup returns NULL and it XORs with 0). Resolve it only now, once
    // the pool has already proved itself, so an ordinary title never runs this scan.
    // Refuse the pool without it: decoding block 0 alone is not name resolution.
    static uintptr_t s_ctxCache = 0;
    static bool      s_ctxTried = false;
    if (!s_ctxTried) { s_ctxTried = true; s_ctxCache = ResolveNameKeyTable(); }
    if (!s_ctxCache) {
        Sein::Warn("SCAN:GNam", "ValidateGNames: obfuscated pool at 0x%llX rejected — key table not resolved",
                   static_cast<unsigned long long>(addr));
        return false;
    }

    g_fnameEntryHeaderOffset = 0;
    g_nameChunksOffset       = off;
    g_namePayloadGap         = GAP;
    g_nameKeyTableCtx        = s_ctxCache;
    Sein::Info("SCAN:GNam",
             "ValidateGNames: Valid at 0x%llX (chunks@+0x%02X, OBFUSCATED payload gap=%d, block0 key=0x%02X, 'None')",
             static_cast<unsigned long long>(addr), off, GAP, key);
    return true;
}

// We try multiple offsets so the validator works across engine variants.
static bool ValidateGNames(uintptr_t addr) {
    if (!addr) return false;

    // Offsets to try for the start of the Blocks[] array within FNamePool.
    // 0x10 is the standard UE5 offset; 0x00 covers builds where the AOB
    // resolves directly to the chunk array rather than the pool object.
    static const int kOffsets[] = { 0x10, 0x00, 0x08, 0x18, 0x20, 0x28, 0x40 };

    for (int off : kOffsets) {
        uintptr_t chunk0 = 0;
        if (!Macht::ReadSafe(addr + off, chunk0) || chunk0 == 0) continue;

        // Dump the head of the block itself. The per-offset probes below only see the
        // first entry; several consecutive entries are what actually distinguishes a
        // stock pool from a forked entry stride, and — where the payload is obfuscated —
        // whether the transform is a constant, positional, or streaming one.
        if (g_gnamesDumpCount < kMaxGNamesDumps) {
            ++g_gnamesDumpCount;
            unsigned char blk[96] = {};
            if (Macht::ReadBytesSafe(chunk0, blk, sizeof(blk))) {
                char hex[sizeof(blk) * 3 + 1] = {};
                char prn[sizeof(blk) + 1] = {};
                for (size_t b = 0; b < sizeof(blk); ++b) {
                    snprintf(hex + b * 3, 4, "%02X ", blk[b]);
                    prn[b] = (blk[b] >= 0x20 && blk[b] <= 0x7E) ? static_cast<char>(blk[b]) : '.';
                }
                Sein::Debug("SCAN:GNam", "ValidateGNames: block@0x%llX (base=0x%llX blocks+0x%02X): %s | %s",
                          static_cast<unsigned long long>(chunk0),
                          static_cast<unsigned long long>(addr), off, hex, prn);
            }
        }

        // Try two header layouts:
        //   (A) Standard: 2-byte header at chunk0+0, string at chunk0+2
        //   (B) Hash-prefixed (UE4.26): 4-byte hash at chunk0+0, 2-byte header at chunk0+4, string at chunk0+6
        // For each layout, try both Format A (len = header >> 6) and Format B (len = (header >> 1) & 0x7FF)

        auto tryHeaderAt = [&](int hdrOff) -> bool {
            uint16_t header = 0;
            if (!Macht::ReadSafe(chunk0 + hdrOff, header)) return false;

            char name[5] = {};
            int lenA = header >> 6;
            if (lenA == 4 && Macht::ReadBytesSafe(chunk0 + hdrOff + 2, name, 4) && strcmp(name, "None") == 0) {
                g_fnameEntryHeaderOffset = hdrOff;
                Sein::Info("SCAN:GNam", "ValidateGNames: Valid at 0x%llX (chunks@+0x%02X, hdrOff=%d, FmtA, 'None')",
                         static_cast<unsigned long long>(addr), off, hdrOff);
                return true;
            }
            memset(name, 0, sizeof(name));
            int lenB = (header >> 1) & 0x7FF;
            if (lenB == 4 && Macht::ReadBytesSafe(chunk0 + hdrOff + 2, name, 4) && strcmp(name, "None") == 0) {
                g_fnameEntryHeaderOffset = hdrOff;
                Sein::Info("SCAN:GNam", "ValidateGNames: Valid at 0x%llX (chunks@+0x%02X, hdrOff=%d, FmtB, 'None')",
                         static_cast<unsigned long long>(addr), off, hdrOff);
                return true;
            }

            // Log the RAW bytes at the header position, not `name`: `name` is only ever
            // filled when a length check already matched, and is memset back to 0 above,
            // so an empty name here is a logging artifact — not evidence about the game.
            // The candidate POOL BASE is logged too; without it a chunk0 value cannot be
            // attributed to the candidate that produced it.
            if (g_validationDbgCount++ < kMaxValidationDbgLogs) {
                unsigned char raw[8] = {};
                char hex[32] = {}, prn[9] = {};
                bool okRaw = Macht::ReadBytesSafe(chunk0 + hdrOff, raw, sizeof(raw));
                for (int b = 0; b < 8; ++b) {
                    snprintf(hex + b * 3, 4, "%02X ", raw[b]);
                    prn[b] = (raw[b] >= 0x20 && raw[b] <= 0x7E) ? static_cast<char>(raw[b]) : '.';
                }
                Sein::Debug("SCAN:GNam", "ValidateGNames: base=0x%llX blocks+0x%02X hdrOff=%d chunk0=0x%llX "
                                         "header=0x%04X lenA=%d lenB=%d raw=[%s] '%s'%s",
                          static_cast<unsigned long long>(addr), off, hdrOff,
                          static_cast<unsigned long long>(chunk0), header, lenA, lenB,
                          hex, prn, okRaw ? "" : " (READ FAILED)");
            }
            return false;
        };

        // Standard layout: header at +0
        if (tryHeaderAt(0)) return true;
        // Hash-prefixed layout: 4-byte ComparisonId then 2-byte header at +4
        if (tryHeaderAt(4)) return true;
        // Obfuscated licensee fork — LAST, and only after both stock layouts have
        // been rejected for this candidate, so every stock title reaches its accept
        // through exactly the same code as before.
        if (TryObfuscatedPool(addr, off, chunk0)) return true;
    }

    // Dump the first 128 bytes so we can diagnose the layout manually.
    // Own budget: a single candidate burns up to 14 per-offset lines above (7 block
    // offsets x 2 header offsets), which used to exhaust the shared throttle before the
    // most diagnostic line in the whole GNames path could ever print.
    if (g_gnamesDumpCount++ < kMaxGNamesDumps) {
        char hexbuf[256];
        int pos = 0;
        for (int i = 0; i < 128 && pos < 200; i += 8) {
            uintptr_t v = 0;
            if (Macht::ReadSafe(addr + i, v))
                pos += snprintf(hexbuf + pos, sizeof(hexbuf) - pos,
                                " +%02X:%016llX", i, (unsigned long long)v);
            else
                pos += snprintf(hexbuf + pos, sizeof(hexbuf) - pos, " +%02X:[??]", i);
        }
        Sein::Debug("SCAN:GNam", "ValidateGNames: dump@0x%llX:%s",
                  (unsigned long long)addr, hexbuf);
    }
    if (g_validationDbgCount <= kMaxValidationDbgLogs)
        Sein::Warn("SCAN:GNam", "ValidateGNames: Validation failed at 0x%llX", static_cast<unsigned long long>(addr));
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
    if (!Macht::ReadBytesSafe(chunkAddr, buf, kScanSize)) return false;

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
    if (!Macht::ReadSafe(addr, chunk0Ptr) || !chunk0Ptr) return false;
    if (!Grimoire::IsUserspacePointer(chunk0Ptr)) return false;

    // chunk0Ptr points to an array of FNameEntry* pointers
    // Read chunk0[0] = first FNameEntry*
    uintptr_t entry0 = 0;
    if (!Macht::ReadSafe(chunk0Ptr, entry0) || !entry0) return false;
    if (!Grimoire::IsUserspacePointer(entry0)) return false;

    // Try reading "None" at common UE4 FNameEntry string offsets
    int offsets[] = { 0x10, 0x06, 0x0C, 0x08 };
    for (int strOff : offsets) {
        char name[5] = {};
        if (!Macht::ReadBytesSafe(entry0 + strOff, name, 4)) continue;
        if (strcmp(name, "None") != 0) continue;

        // Corroborate: entry at index 1 should also be a valid pointer with ASCII string
        uintptr_t entry1 = 0;
        if (!Macht::ReadSafe(chunk0Ptr + 8, entry1) || entry1 < 0x10000) continue;

        char name1[8] = {};
        if (!Macht::ReadBytesSafe(entry1 + strOff, name1, 7)) continue;

        bool valid = true;
        for (int i = 0; i < 7 && name1[i]; ++i) {
            auto c = static_cast<unsigned char>(name1[i]);
            if (c < 0x20 || c >= 0x7F) { valid = false; break; }
        }
        if (!valid) continue;

        // Extra corroboration: entry at index 2 should also be valid
        uintptr_t entry2 = 0;
        if (Macht::ReadSafe(chunk0Ptr + 16, entry2) && entry2 > 0x10000) {
            char name2[8] = {};
            if (Macht::ReadBytesSafe(entry2 + strOff, name2, 7)) {
                bool valid2 = true;
                for (int i = 0; i < 7 && name2[i]; ++i) {
                    auto c = static_cast<unsigned char>(name2[i]);
                    if (c < 0x20 || c >= 0x7F) { valid2 = false; break; }
                }
                if (!valid2) continue;
            }
        }

        outStringOffset = strOff;
        Sein::Info("SCAN:GNam", "ValidateGNamesUE4: Valid TNameEntryArray at 0x%llX "
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
    if (!Macht::ReadBytesSafe(addr, buf, 10)) return false;

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
    Sein::Info("SCAN:GNam", "FindGNamesByPointerScan: Scanning .data for pointer-to-'None' FNameEntry...");

    uintptr_t base = Macht::GetModuleBase(nullptr);
    if (!base) return 0;

    auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;

    auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(
        base + static_cast<DWORD>(dos->e_lfanew));
    if (nt->Signature != IMAGE_NT_SIGNATURE) return 0;

    const IMAGE_SECTION_HEADER* section = IMAGE_FIRST_SECTION(nt);
    size_t modSize = Macht::GetModuleSize(nullptr);

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
        Sein::Debug("SCAN:GNam", "FindGNamesByPointerScan: Scanning section [%s] at 0x%llX (%zu bytes)",
                  secName, (unsigned long long)secBase, secSize);

        // Walk every 8-byte-aligned slot and treat it as a potential pointer.
        int diagCount = 0;  // Limit diagnostic dumps to first few candidates
        for (size_t off = 0; off + 8 <= secSize; off += 8) {
            uintptr_t ptr = 0;
            // Cooperative cancel. UE5_Shutdown runs on the CE Lua caller's thread and
            // joins the accept thread, so an unbounded sweep here freezes CE's UI for its
            // whole duration. Aura's idiom: poll cheaply every 4096 slots. (B18)
            if ((off & 0xFFF) == 0 && Tot::Requested()) return 0;
            if (!Macht::ReadSafe(secBase + off, ptr)) continue;

            // Plausible user-space 64-bit address (exclude null, low, kernel)
            if (!Grimoire::IsUserspacePointer(ptr)) continue;

            // Skip if ptr lives inside the game module itself (not a heap chunk)
            if (ptr >= base && ptr < base + modSize) continue;

            // Check if ptr dereferences to a "None" FNameEntry
            if (!LooksLikeNoneEntry(ptr)) {
                // Near-miss diagnostic: check if "None" appears anywhere in first 16 bytes
                // This catches unknown header formats we didn't account for
                if (diagCount < 10) {
                    uint8_t peek[16] = {};
                    if (Macht::ReadBytesSafe(ptr, peek, 16)) {
                        for (int p = 0; p + 4 <= 16; ++p) {
                            if (peek[p] == 'N' && peek[p+1] == 'o' && peek[p+2] == 'n' && peek[p+3] == 'e') {
                                Sein::Warn("SCAN:GNam", "FindGNamesByPointerScan: NEAR-MISS 'None' at ptr=0x%llX offset=%d "
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

            Sein::Info("SCAN:GNam", "FindGNamesByPointerScan: chunk0=0x%llX @ 0x%llX — corroborating...",
                     (unsigned long long)ptr, (unsigned long long)pAddr);

            // Corroborate: real FNamePool chunks contain UE type names
            if (!CorroborateFNameChunk(ptr)) {
                Sein::Debug("SCAN:GNam", "FindGNamesByPointerScan: Corroboration failed — skipping");
                continue;
            }

            // Dump what the candidate actually points to for diagnostics
            if (diagCount < 5) {
                char hexbuf[64] = {};
                uint8_t peek[16] = {};
                if (Macht::ReadBytesSafe(ptr, peek, 16)) {
                    snprintf(hexbuf, sizeof(hexbuf),
                             "%02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X",
                             peek[0], peek[1], peek[2], peek[3], peek[4], peek[5], peek[6], peek[7],
                             peek[8], peek[9], peek[10], peek[11], peek[12], peek[13], peek[14], peek[15]);
                }
                Sein::Debug("SCAN:GNam", "FindGNamesByPointerScan: candidate chunk0 bytes: %s", hexbuf);
                ++diagCount;
            }

            // Try common offsets of Blocks[0] within FNamePool:
            //   0x10 = standard UE5 (FRWLock[8] + CurrentBlock[4] + Cursor[4])
            //   0x00, 0x08, 0x18, 0x20, 0x28 = observed variants
            for (int blkOff : { 0x10, 0x00, 0x08, 0x18, 0x20, 0x28 }) {
                if ((size_t)blkOff > pAddr) continue; // underflow guard
                uintptr_t pool = pAddr - static_cast<uintptr_t>(blkOff);
                if (ValidateGNames(pool) || ValidateGNamesStructural(pool)) {
                    Sein::Info("SCAN:GNam", "FindGNamesByPointerScan: Valid pool at 0x%llX (Blocks[0]@+0x%02X)",
                             (unsigned long long)pool, blkOff);
                    return pool;
                }
            }
        }
    }

    Sein::Warn("SCAN:GNam", "FindGNamesByPointerScan: No valid FNamePool found in .data");
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

// ─────────────────────────────────────────────────────────────────────────────
// FindGNamesByStringRef — fallback: search .rdata for a known UE string
// literal, find XREF in .text (LEA pointing to it), then scan nearby code
// for LEA reg,[rip+disp] instructions that load FNamePool address.
//
// Strategy: The FName constructor is called with string literals like
// "ForwardShadingQuality_" or "bInitializedSerializeFromMismatchedTag".
// The call site typically has:
//   LEA rcx, [rip+FNamePool]    ; load FNamePool address
//   LEA rdx, [rip+StringLit]    ; load the string literal
//   CALL FName::Init / FName::FName
//
// We find the string literal XREF, then scan ±0x60 bytes around it for
// another LEA that resolves to a valid FNamePool in the .data section.
//
// Source: Dumper-7 UnrealTypes.cpp:69-204 (adapted approach)
// ─────────────────────────────────────────────────────────────────────────────
static uintptr_t FindGNamesByStringRef() {
    Sein::Info("SCAN:GNam", "FindGNamesByStringRef: Searching for string-ref to FNamePool...");

    uintptr_t base = Macht::GetModuleBase(nullptr);
    if (!base) return 0;
    size_t modSize = Macht::GetModuleSize(nullptr);
    if (!modSize) return 0;

    auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;
    auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(
        base + static_cast<DWORD>(dos->e_lfanew));
    if (nt->Signature != IMAGE_NT_SIGNATURE) return 0;

    // Locate .text (code), .rdata (read-only), and .data (writable) sections
    const IMAGE_SECTION_HEADER* section = IMAGE_FIRST_SECTION(nt);
    uintptr_t codeStart = 0, codeEnd = 0;
    uintptr_t rdataStart = 0, rdataEnd = 0;
    uintptr_t dataStart = 0, dataEnd = 0;

    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++section) {
        if (!section->Misc.VirtualSize || !section->VirtualAddress) continue;
        uintptr_t secBase = base + section->VirtualAddress;
        uintptr_t secEnd  = secBase + section->Misc.VirtualSize;

        if (section->Characteristics & IMAGE_SCN_MEM_EXECUTE) {
            if (!codeStart || secBase < codeStart) codeStart = secBase;
            if (secEnd > codeEnd) codeEnd = secEnd;
        } else if (!(section->Characteristics & IMAGE_SCN_MEM_WRITE) &&
                   !(section->Characteristics & IMAGE_SCN_MEM_EXECUTE)) {
            // Read-only, non-executable = .rdata
            if (!rdataStart || secBase < rdataStart) rdataStart = secBase;
            if (secEnd > rdataEnd) rdataEnd = secEnd;
        } else if ((section->Characteristics & IMAGE_SCN_MEM_WRITE) &&
                   !(section->Characteristics & IMAGE_SCN_MEM_EXECUTE)) {
            // Writable, non-exec = .data (where FNamePool lives)
            if (!dataStart) { dataStart = secBase; dataEnd = secEnd; }
        }
    }

    if (!codeStart || !rdataStart || !dataStart) {
        Sein::Debug("SCAN:GNam", "FindGNamesByStringRef: Could not identify required sections");
        return 0;
    }

    Sein::Debug("SCAN:GNam", "FindGNamesByStringRef: code=[0x%llX-0x%llX], rdata=[0x%llX-0x%llX], data=[0x%llX-0x%llX]",
              (unsigned long long)codeStart, (unsigned long long)codeEnd,
              (unsigned long long)rdataStart, (unsigned long long)rdataEnd,
              (unsigned long long)dataStart, (unsigned long long)dataEnd);

    // Known UE string literals used near FName construction sites.
    // We search for each in .rdata, then find XREFs from .text.
    const char* markerStrings[] = {
        "ForwardShadingQuality_",
        "bInitializedSerializeFromMismatchedTag",
        "NameProperty",
    };

    for (const char* marker : markerStrings) {
        size_t markerLen = strlen(marker);

        // Search .rdata for the ASCII string.
        // (G2) The heaviest byte-walk in this file: it calls the OUT-OF-LINE SEH-guarded
        // ReadBytesSafe once per offset, not the inline ReadSafe the sibling walks use,
        // and its break fires only when a marker is FOUND — an absent marker walks the
        // whole section. Bailing here is benign: FindGNames falls through to the next
        // strategy and nothing memoizes a not-found result.
        for (uintptr_t scan = rdataStart; scan + markerLen < rdataEnd; ++scan) {
            if ((scan & 0xFFF) == 0 && Tot::Requested()) {
                Sein::Warn("SCAN:GNam", "FindGNamesByStringRef: aborted (client gone / shutdown)");
                return 0;
            }
            char buf[64] = {};
            size_t readLen = (markerLen < 63) ? markerLen + 1 : 63;
            if (!Macht::ReadBytesSafe(scan, buf, readLen)) continue;
            if (memcmp(buf, marker, markerLen) != 0) continue;

            // Verify null-termination (exact match, not substring)
            if (readLen > markerLen && buf[markerLen] != '\0') { scan += markerLen; continue; }

            Sein::Debug("SCAN:GNam", "FindGNamesByStringRef: Found '%s' at 0x%llX",
                      marker, (unsigned long long)scan);

            // Scan .text for LEA reg,[rip+disp32] that resolve to 'scan'.
            // LEA opcodes: {48,4C} 8D {05,0D,15,1D,25,2D,35,3D} disp32
            int xrefCount = 0;
            for (uintptr_t cs = codeStart; cs + 7 < codeEnd && xrefCount < 8; ++cs) {
                uint8_t b0 = 0, b1 = 0, b2 = 0;
                if (!Macht::ReadSafe(cs, b0)) continue;
                if (b0 != 0x48 && b0 != 0x4C) continue;
                if (!Macht::ReadSafe(cs + 1, b1)) continue;
                if (b1 != 0x8D) continue;
                if (!Macht::ReadSafe(cs + 2, b2)) continue;
                if (!Macht::IsRipRelativeModRM(b2)) continue;  // RIP-relative

                int32_t disp = 0;
                if (!Macht::ReadSafe(cs + 3, disp)) continue;
                uintptr_t resolved = cs + 7 + static_cast<int64_t>(disp);
                if (resolved != scan) continue;

                // Found XREF at 'cs'. Scan ±0x60 bytes around it for
                // another LEA that resolves to a .data address (FNamePool candidate).
                ++xrefCount;
                Sein::Debug("SCAN:GNam", "FindGNamesByStringRef: XREF #%d at 0x%llX",
                          xrefCount, (unsigned long long)cs);

                uintptr_t searchStart = (cs > codeStart + 0x60) ? cs - 0x60 : codeStart;
                uintptr_t searchEnd   = (cs + 0x60 < codeEnd)   ? cs + 0x60 : codeEnd;

                for (uintptr_t ns = searchStart; ns + 7 < searchEnd; ++ns) {
                    if (ns == cs) continue;  // Skip the string XREF itself

                    uint8_t n0 = 0, n1 = 0, n2 = 0;
                    if (!Macht::ReadSafe(ns, n0)) continue;
                    if (n0 != 0x48 && n0 != 0x4C) continue;
                    if (!Macht::ReadSafe(ns + 1, n1)) continue;
                    if (n1 != 0x8D) continue;
                    if (!Macht::ReadSafe(ns + 2, n2)) continue;
                    if (!Macht::IsRipRelativeModRM(n2)) continue;

                    int32_t ndisp = 0;
                    if (!Macht::ReadSafe(ns + 3, ndisp)) continue;
                    uintptr_t candidate = ns + 7 + static_cast<int64_t>(ndisp);

                    // Must resolve to the data section
                    if (candidate < dataStart || candidate >= dataEnd) continue;

                    // Validate as FNamePool
                    if (ValidateGNamesAny(candidate)) {
                        Sein::Info("SCAN:GNam", "FindGNamesByStringRef: Valid FNamePool at 0x%llX "
                                 "(via '%s' XREF at 0x%llX, LEA at 0x%llX)",
                                 (unsigned long long)candidate, marker,
                                 (unsigned long long)cs, (unsigned long long)ns);
                        return candidate;
                    }
                }
            }

            // Only check first occurrence of each marker string
            break;
        }
    }

    Sein::Debug("SCAN:GNam", "FindGNamesByStringRef: No FNamePool found via string references");
    return 0;
}

uintptr_t FindGNames(const char* hintPatternId) {
    s_gnamesMethod = "not_found";
    // Reset detection state for each scan attempt
    g_isUE4NameArray = false;
    g_ue4NameStringOffset = 0x10;
    g_fnameEntryHeaderOffset = 0;
    g_nameChunksOffset = 0;
    g_namePayloadGap = 0;
    g_nameKeyTableCtx = 0;

    Sein::Info("SCAN:GNam", "FindGNames: Scanning for GNames (FNamePool / TNameEntryArray)...");

    s_gnamesReport = ScanReport{};
    ScanReport& report = s_gnamesReport;
    report.targetName = "GNames";

    uintptr_t result = ScanForTarget(
        Sig::GNAMES_PATTERNS, std::size(Sig::GNAMES_PATTERNS),
        ValidateGNamesAny, report, /*tryMultiModule=*/true, hintPatternId);

    LogScanReport(report);

    if (result) {
        s_gnamesMethod = ScanMethodFor(report);
    } else {
        // Tier 2: string-reference fallback — find FNamePool via code that uses FName
        Sein::Warn("SCAN:GNam", "FindGNames: All patterns failed, trying string-ref fallback...");
        result = FindGNamesByStringRef();
        if (result) {
            s_gnamesMethod = "string_ref";
        } else {
            // Tier 3: data-pointer scan — brute-force .data for "None" chunk pointers
            Sein::Warn("SCAN:GNam", "FindGNames: String-ref failed, trying pointer scan fallback...");
            result = FindGNamesByPointerScan();
            if (result) s_gnamesMethod = "pointer_scan";
        }
    }

    if (!result) {
        Sein::Error("SCAN:GNam", "FindGNames: All patterns and fallbacks failed");
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
    if (!Macht::ReadSafe(addr, world)) return false;
    // A null world is acceptable (write-patterns at startup) — the null
    // filtering is already handled by TryResolveMatch via gworldAllowNull.
    if (world == 0) return true;
    if (!LooksLikeDataPtr(world)) return false;     // must be an off-module heap pointer

    // Decoy guard (offset-independent). GWorld's generic short patterns can land on a
    // .data global that merely holds a data-shaped pointer (the Solarpunk UE5.7 / Avowed
    // GWorld decoy — 0xA8B0 below the real GWorld on Solarpunk). A real UWorld is a C++
    // object: [world] is a vtable pointer and [[world]] is its first virtual method — a
    // code pointer in a module image. The decoy's double-deref is not executable code, so
    // it fails here and the scan continues to the real GWorld. This checks only the vtable
    // at +0, so it is safe to run before DynOff validates any UObject field offset. A
    // rejection that leaves GWorld unfound falls through to UE5_Init instance-scan recovery.
    uintptr_t vtbl = 0;
    if (!Macht::ReadSafe(world, vtbl) || !Grimoire::IsUserspacePointer(vtbl)) return false;
    uintptr_t firstVirtual = 0;
    if (!Macht::ReadSafe(vtbl, firstVirtual) || !Macht::LooksLikeCodePointer(firstVirtual))
        return false;
    return true;
}

uintptr_t FindGWorld(const char* hintPatternId) {
    s_gworldMethod = "not_found";
    Sein::Info("SCAN:GWld", "FindGWorld: Scanning for GWorld...");

    s_gworldReport = ScanReport{};
    ScanReport& report = s_gworldReport;
    report.targetName = "GWorld";

    uintptr_t result = ScanForTarget(
        Sig::GWORLD_PATTERNS, std::size(Sig::GWORLD_PATTERNS),
        ValidateGWorldBasic, report, /*tryMultiModule=*/true, hintPatternId);

    LogScanReport(report);

    if (result) {
        s_gworldMethod = ScanMethodFor(report);
    } else {
        Sein::Warn("SCAN:GWld", "FindGWorld: All patterns failed (non-critical)");
    }
    return result;
}

// ============================================================
// FindSparseDelegateStorage — lazy resolver for FSparseDelegateStorage::SparseDelegates
//
// Called on-demand by Aura::WalkSparseDelegateBindings when the user drills
// into a MulticastSparseDelegateProperty. NOT part of FindAll boot — most
// sessions never touch a sparse delegate so the AOB scan would be wasted.
//
// Caches the result in a process-local atomic; thread-safe via double-checked
// locking. Returns 0 if AOB scan fails (caller falls back to bIsBound flag).
//
// Validator: TMap header sanity. The static is a default-constructed TMap
// whose initial state has FirstFreeIndex == -1 and AllocationFlags.MaxBits
// == 0x80 (TInlineAllocator<4> initial). After live use either may change,
// so the validator only checks pointer-shape sanity (readable + low bytes
// don't look like code/heap garbage).
// ============================================================
static bool ValidateSparseDelegates(uintptr_t addr) {
    if (!addr) return false;
    // The TMap object lives in writable .data / .bss. Read the first 0x40
    // bytes; reject if the read fails. All-zero is acceptable (storage may
    // be empty if no sparse delegate is bound yet at scan time).
    uintptr_t firstWord = 0;
    if (!Macht::ReadSafe(addr, firstWord)) return false;
    int32_t arrayMax = 0;
    if (!Macht::ReadSafe(addr + 0x0C, arrayMax)) return false;
    int32_t allocMaxBits = 0;
    if (!Macht::ReadSafe(addr + 0x2C, allocMaxBits)) return false;
    // Sanity: ArrayMax shouldn't be negative or absurdly large. MaxBits
    // is a power of 2; default 0x80, grows by doubling.
    if (arrayMax < 0 || arrayMax > 0x100000) return false;
    if (allocMaxBits < 0 || allocMaxBits > 0x100000) return false;

    // ── Content check (added build 2404) ────────────────────────────────
    // Everything above is shape-only: it accepts ANY .data TMap-looking blob, which
    // is why offline sweeps kept finding sparse patterns whose decoys resolve to
    // unrelated 0x60-stride TSets. When the map is NON-EMPTY we can do far better,
    // because we know exactly what a live key is: SparseDelegates is
    //   TMap<UObjectBase const*, TMap<FName, TSharedPtr<FMulticastScriptDelegate>>>
    // so the first 8 bytes of an occupied element are a real UObject pointer, and
    // *that* pointer's first qword is a vtable inside a loaded module. A TMap keyed
    // by FName/int/FString cannot satisfy both.
    //
    // Deliberately NOT applied when the map is empty: FindAll can legitimately run
    // before anything binds a sparse delegate, and rejecting there would lose the
    // address entirely. Empty stays accepted on shape alone, exactly as before.
    uintptr_t elemData = 0;
    int32_t   elemNum  = 0;
    if (!Macht::ReadSafe(addr + 0x00, elemData)) return false;
    if (!Macht::ReadSafe(addr + 0x08, elemNum))  return false;
    if (elemNum <= 0 || !elemData) return true;          // empty / not yet allocated
    if (elemNum > 0x100000) return false;

    constexpr int32_t kOuterStride = 0x60;               // TSetElement<TTuple<ptr, TMap>>
    constexpr int32_t kProbeSlots  = 32;                 // enough to skip freed slots
    int32_t probe = (elemNum < kProbeSlots) ? elemNum : kProbeSlots;
    for (int32_t i = 0; i < probe; ++i) {
        uintptr_t key = 0;
        if (!Macht::ReadSafe(elemData + static_cast<uintptr_t>(i) * kOuterStride, key))
            continue;
        if (!Grimoire::IsUserspacePointer(key)) continue;   // freed slot / not a pointer
        uintptr_t vt = 0;
        if (!Macht::ReadSafe(key, vt)) continue;
        if (!Grimoire::IsUserspacePointer(vt)) continue;
        // A UObject's vtable lives in the module image; a heap/garbage value does not.
        uintptr_t modBase = Macht::GetModuleBase(nullptr);
        uintptr_t modSize = Macht::GetModuleSize(nullptr);
        if (modBase && modSize && vt >= modBase && vt < modBase + modSize) {
            Sein::Info("SCAN:Sparse",
                       "ValidateSparseDelegates: 0x%llX num=%d — slot %d key=0x%llX has a "
                       "module vtable, accepted", (unsigned long long)addr, elemNum, i,
                       (unsigned long long)key);
            return true;
        }
    }
    Sein::Debug("SCAN:Sparse",
                "ValidateSparseDelegates: 0x%llX rejected — %d live element(s) but none has a "
                "UObject-shaped key", (unsigned long long)addr, elemNum);
    return false;
}

static std::atomic<uintptr_t> s_sparseDelegatesCache{0};
static std::atomic<bool>      s_sparseDelegatesScanned{false};

uintptr_t FindSparseDelegateStorage() {
    // Fast path: already resolved (or already failed).
    if (s_sparseDelegatesScanned.load(std::memory_order_acquire))
        return s_sparseDelegatesCache.load(std::memory_order_relaxed);

    Sein::Info("SCAN:Sparse", "FindSparseDelegateStorage: Scanning for FSparseDelegateStorage::SparseDelegates...");

    s_sparseReport = ScanReport{};
    s_sparseReport.targetName = "SparseDelegates";
    s_sparseDelegatesMethod = "not_found";

    uintptr_t result = ScanForTarget(
        Sig::SPARSE_PATTERNS, std::size(Sig::SPARSE_PATTERNS),
        ValidateSparseDelegates, s_sparseReport, /*tryMultiModule=*/true,
        /*hintPatternId=*/nullptr);

    LogScanReport(s_sparseReport);

    if (result) {
        s_sparseDelegatesMethod = ScanMethodFor(s_sparseReport);
        Sein::Info("SCAN:Sparse", "FindSparseDelegateStorage: Found at 0x%llX",
                   static_cast<unsigned long long>(result));
    } else {
        Sein::Warn("SCAN:Sparse", "FindSparseDelegateStorage: All patterns failed (non-critical, sparse drill-down disabled)");
    }

    s_sparseDelegatesCache.store(result, std::memory_order_relaxed);
    s_sparseDelegatesScanned.store(true, std::memory_order_release);
    return result;
}

// ─────────────────────────────────────────────────────────────────────────────
// Publisher thumbprint table — read from PE VERSIONINFO StringFileInfo
//
// SquareEnix is the only publisher we recognise here (FF7 Remake/Rebirth and
// DQ I&II HD-2D are all UE4 forks that lie about their version: they ship
// stripped UE++Release strings AND embed unrelated 5.x.y SDK strings that
// fool Tier 3 of the memory scan into reporting UE 5.0+).
//
// Adding more publishers casually is risky — wrong bias overrides correct
// detection. Add only when there is a documented misdetection on multiple
// titles from the same publisher.
// ─────────────────────────────────────────────────────────────────────────────
struct PublisherEntry {
    const char* needle;        // case-insensitive substring (LegalCopyright or CompanyName)
    const char* thumbprint;    // canonical key exposed to UI / HintCache
    uint32_t    biasFallback;  // UE version to use when memory scan fails
};

// NOTE: changing a biasFallback VALUE here affects the version a cached game resolves to →
// bump Genau::kVersionDetectLogicRev so already-cached games re-detect under the new bias.
// (Adding/removing a publisher does NOT need a bump for the low-confidence BADGE — FindAll's
// reuse branch re-applies publisher low-confidence LIVE from the per-launch DetectPublisherFromPE.)
static const PublisherEntry kPublishers[] = {
    // FF7 Remake (4.18), FF7 Rebirth (4.26 fork), DQ I&II HD-2D (4.27).
    // 427 is the most-common case — Rebirth/Remake structural detection
    // (bUE4NameArray / fnameEntryHeaderOffset) overrides downward to 422/426
    // automatically post-FNamePool init.
    { "SQUARE ENIX", "SQUARE_ENIX", 427 },
};

// Read a StringFileInfo string (LegalCopyright / CompanyName / etc.) from a
// VERSIONINFO buffer. Walks every (lang, codepage) pair declared in
// VarFileInfo\Translation and returns the first non-empty value.
static std::string ReadVersionInfoString(uint8_t* buf, const wchar_t* keyName) {
    struct LangCodepage { WORD wLanguage; WORD wCodePage; };
    LangCodepage* langs = nullptr;
    UINT langsLen = 0;
    if (!VerQueryValueW(buf, L"\\VarFileInfo\\Translation",
                        reinterpret_cast<LPVOID*>(&langs), &langsLen) || !langs)
        return {};

    int langCount = static_cast<int>(langsLen / sizeof(LangCodepage));
    for (int i = 0; i < langCount; ++i) {
        wchar_t subBlock[256];
        swprintf_s(subBlock, L"\\StringFileInfo\\%04x%04x\\%ls",
                   langs[i].wLanguage, langs[i].wCodePage, keyName);
        wchar_t* str = nullptr;
        UINT strLen = 0;
        if (VerQueryValueW(buf, subBlock,
                           reinterpret_cast<LPVOID*>(&str), &strLen) && str && strLen > 0) {
            int sz = WideCharToMultiByte(CP_UTF8, 0, str, -1,
                                         nullptr, 0, nullptr, nullptr);
            if (sz <= 1) continue;
            std::string out(static_cast<size_t>(sz - 1), '\0');
            WideCharToMultiByte(CP_UTF8, 0, str, -1,
                                out.data(), sz, nullptr, nullptr);
            return out;
        }
    }
    return {};
}

static std::string AsciiUpper(std::string s) {
    for (char& c : s) {
        unsigned char u = static_cast<unsigned char>(c);
        if (u >= 'a' && u <= 'z') c = static_cast<char>(u - ('a' - 'A'));
    }
    return s;
}

// Match a publisher from PE VERSIONINFO LegalCopyright / CompanyName.
// Returns the matched PublisherEntry pointer (with string-literal-lifetime
// thumbprint), or nullptr if no entry matched.
static const PublisherEntry* DetectPublisherFromPE() {
    wchar_t exePath[MAX_PATH] = {};
    if (!GetModuleFileNameW(nullptr, exePath, MAX_PATH)) return nullptr;

    DWORD handle = 0;
    DWORD infoSize = GetFileVersionInfoSizeW(exePath, &handle);
    if (!infoSize) return nullptr;

    std::vector<uint8_t> buf(infoSize);
    if (!GetFileVersionInfoW(exePath, handle, infoSize, buf.data())) return nullptr;

    std::string copyright = ReadVersionInfoString(buf.data(), L"LegalCopyright");
    std::string company   = ReadVersionInfoString(buf.data(), L"CompanyName");

    std::string upC = AsciiUpper(copyright);
    std::string upN = AsciiUpper(company);

    for (const auto& p : kPublishers) {
        if ((!upC.empty() && upC.find(p.needle) != std::string::npos) ||
            (!upN.empty() && upN.find(p.needle) != std::string::npos)) {
            Sein::Info("SCAN:Ver",
                       "DetectPublisher: matched '%s' (Copyright='%s', Company='%s')",
                       p.thumbprint,
                       copyright.empty() ? "-" : copyright.c_str(),
                       company.empty()   ? "-" : company.c_str());
            return &p;
        }
    }

    if (!copyright.empty() || !company.empty()) {
        Sein::Info("SCAN:Ver",
                   "DetectPublisher: no thumbprint match (Copyright='%s', Company='%s')",
                   copyright.empty() ? "-" : copyright.c_str(),
                   company.empty()   ? "-" : company.c_str());
    }
    return nullptr;
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
        Sein::Info("SCAN:Ver", "DetectVersion: PE VERSIONINFO -> UE %u.%u -> %u",
                 major, minor, 500u + minor);
        return 500u + minor;
    }

    // Some shippers put 4.x in the info (UE4 fork claiming UE5 classes)
    if (major == 4 && minor <= 27) {
        Sein::Info("SCAN:Ver", "DetectVersion: PE VERSIONINFO -> UE4.%u (treated as 400+minor)", minor);
        return 400u + minor;
    }

    // Some shippers put UE version in FileVersion instead of ProductVersion
    uint32_t fmajor = HIWORD(fi->dwFileVersionMS);
    uint32_t fminor = LOWORD(fi->dwFileVersionMS);
    if (fmajor == 5 && fminor <= 9) {
        Sein::Info("SCAN:Ver", "DetectVersion: PE FileVersion -> UE %u.%u -> %u", fmajor, fminor, 500u + fminor);
        return 500u + fminor;
    }
    if (fmajor == 4 && fminor <= 27) {
        Sein::Info("SCAN:Ver", "DetectVersion: PE FileVersion -> UE4.%u (treated as 400+minor)", fminor);
        return 400u + fminor;
    }

    // Last resort inside the resource: the StringFileInfo *strings*. Some games leave
    // VS_FIXEDFILEINFO generic (1.0.0.0) but paste the real engine tag into
    // ProductVersion / FileVersion. DropIn 4.27.2's ProductVersion string is literally
    // "++UE4+Release-4.27-CL-18319896" — the exact Tier-1 needle the memory scan hunts
    // for, available here as an O(1) resource lookup. ReadVersionInfoString already walks
    // every (lang, codepage) pair, which matters: .NET's FileVersionInfo returns empty
    // for this file because the strings are not under the default translation.
    for (const wchar_t* key : { L"ProductVersion", L"FileVersion" }) {
        std::string s = ReadVersionInfoString(buf.data(), key);
        if (s.empty()) continue;
        for (const char* prefix : { "++UE5+Release-", "++UE4+Release-" }) {
            size_t p = s.find(prefix);
            if (p == std::string::npos) continue;
            p += strlen(prefix);
            // expect "<major>.<minor>" right after the tag
            unsigned maj = 0, min = 0;
            if (sscanf_s(s.c_str() + p, "%u.%u", &maj, &min) == 2) {
                if (maj == 5 && min <= 9) {
                    Sein::Info("SCAN:Ver", "DetectVersion: VERSIONINFO string '%ls' = '%s' -> %u",
                               key, s.c_str(), 500u + min);
                    return 500u + min;
                }
                if (maj == 4 && min <= 27) {
                    Sein::Info("SCAN:Ver", "DetectVersion: VERSIONINFO string '%ls' = '%s' -> %u",
                               key, s.c_str(), 400u + min);
                    return 400u + min;
                }
            }
        }
    }

    Sein::Warn("SCAN:Ver", "DetectVersion: PE VERSIONINFO Product=%u.%u File=%u.%u — unrecognised",
             major, minor, fmajor, fminor);
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────────
// Pre-UE4 (Unreal Engine 3) positive identification
//
// The too-old gate can only be reached by a version NUMBER below 4.11, and the only path
// that produces one is DetectVersionFromPEResource's major==4 branch — so a UE3 title, whose
// PE resource carries a game version like 1.0.x, slipped straight through it and consumed a
// full 150-pattern AOB sweep to arrive at "no winner" plus a misleading "set an override"
// nudge. These markers give the DLL a way to say "this is not Unreal 4/5 at all".
//
// DESIGN RULE — POSITIVE markers only. The ABSENCE of a UE4/UE5 branch tag must NEVER be
// sufficient on its own: that is exactly the state of the SUPPORTED stripped-tag titles
// (The Adventures of Elliot detects as version 0 by the identical route), so gating on
// absence would refuse working games. Absence is a necessary conjunct, never a sufficient one.
//
// FALSE-POSITIVE MEASUREMENTS behind this marker set:
//   * 30 reference builds spanning UE 4.10-5.8 (Shipping / Development / DebugGame):
//     "UnrealEngine3" 0 hits, "SeqAct_" 0 hits, "PhysXLoader64" 0 hits. Zero false positives.
//   * 35 locally installed UE games: exactly ONE (the UE3 title) matches "Epic Games" in
//     LegalCopyright AND carries a year. Manor Lords (UE 5.5) matches "Epic Games" but ships
//     NO year, which is why HasPreUE4EpicCopyright requires an explicit one.
//   * REJECTED as a marker: the bare token "UE3". It occurs 3-85 times in every one of the 30
//     supported reference binaries — using it would refuse the entire corpus. (Consistent with
//     HasUEAnchorNearby already treating short "UE4"/"UE5" tokens as generic noise.)
//   * REJECTED as a marker: the unusual ".nep" section name. Nonstandard section names are not
//     a discriminator — 27 of the 30 supported binaries carry one (.uedbg, .lpp_pre, .msvcjmc,
//     .detourc, ...). Kept as a log breadcrumb only.
//
// "SeqAct_" is the strongest of the four: it is UE3's Kismet native-registration table
// (neighbouring bytes read USeqAct_Latent / execAbortFor / USeqAct_Delay), i.e. the object
// model itself rather than a version string a publisher can strip or fake. UE4 deleted Kismet
// outright in favour of Blueprints, which is why it measures 0 across every supported build.
// ─────────────────────────────────────────────────────────────────────────────

// UE4.0 went public in March 2014, so 2013 is the last year an Epic copyright notice can
// carry and still be pre-UE4.
constexpr int      kLastPreUE4CopyrightYear = 2013;
constexpr int      kPreUE4MarkerCount       = 4;
// 2-of-4. The threshold is conservative on purpose: a UE3 title that strips its markers falls
// through to today's behaviour (full scan, no winner), which is the right direction to fail in.
constexpr int      kPreUE4MarkerThreshold   = 2;

// Internal: does the PE VERSIONINFO carry an Epic Games copyright whose NEWEST year predates
// UE4? Epic bumps the engine copyright year every release, and the year tracks the ENGINE
// SNAPSHOT rather than the game's ship date — the UE3 title measured here shipped in 2015 and
// still reads "Copyright 1998-2012 Epic Games, Inc.", because 2012 is the snapshot it licensed.
// That makes it a better engine-age proxy than a release date.
//
// The inference is ONE-DIRECTIONAL: year <= 2013 implies not-UE4/UE5, but year >= 2014 implies
// nothing (late UE3 games shipped well past 2014). Only the first direction is used here.
static bool HasPreUE4EpicCopyright() {
    wchar_t exePath[MAX_PATH] = {};
    if (!GetModuleFileNameW(nullptr, exePath, MAX_PATH)) return false;

    DWORD handle = 0;
    DWORD infoSize = GetFileVersionInfoSizeW(exePath, &handle);
    if (!infoSize) return false;

    std::vector<uint8_t> buf(infoSize);
    if (!GetFileVersionInfoW(exePath, handle, infoSize, buf.data())) return false;

    std::string copyright = ReadVersionInfoString(buf.data(), L"LegalCopyright");
    if (copyright.empty()) return false;
    if (AsciiUpper(copyright).find("EPIC GAMES") == std::string::npos) return false;

    // Newest 19xx/20xx year in the string. An Epic notice with NO year proves nothing — that
    // is Manor Lords (UE 5.5, supported) — so a missing year must NOT count as "old".
    int newest = 0;
    for (size_t i = 0; i + 4 <= copyright.size(); ++i) {
        const char* p = copyright.c_str() + i;
        if (!((p[0] == '1' && p[1] == '9') || (p[0] == '2' && p[1] == '0'))) continue;
        if (p[2] < '0' || p[2] > '9' || p[3] < '0' || p[3] > '9') continue;
        int year = (p[0] - '0') * 1000 + (p[1] - '0') * 100 + (p[2] - '0') * 10 + (p[3] - '0');
        if (year > newest) newest = year;
        i += 3;
    }
    if (newest == 0) {
        Sein::Info("SCAN:Ver", "PreUE4: Epic copyright with no year — not counted ('%s')",
                   copyright.c_str());
        return false;
    }

    bool isOld = (newest <= kLastPreUE4CopyrightYear);
    Sein::Info("SCAN:Ver", "PreUE4: Epic copyright newest year %d (%s) — '%s'",
               newest, isOld ? "PRE-UE4" : "UE4-era or later", copyright.c_str());
    return isOld;
}

// Internal: count how many independent pre-UE4 markers this module carries (0..4).
//
// The three string markers are found in ONE pass over the image, not one pass each. That
// matters because this runs on the version==0 path, which is shared with stripped-tag but
// fully SUPPORTED titles — Elliot is a 482 MB image, and six full naive sweeps over it would
// be a visible regression on a game that must keep working.
static int CountPreUE4Markers() {
    static const char* kMarkerNames[kPreUE4MarkerCount] = {
        "UnrealEngine3", "SeqAct_ (UE3 Kismet)", "PhysXLoader64 (PhysX 2.8)",
        "Epic copyright <= 2013",
    };
    bool found[kPreUE4MarkerCount] = {};

    found[3] = HasPreUE4EpicCopyright();

    uintptr_t base = Macht::GetModuleBase(nullptr);
    size_t    size = Macht::GetModuleSize(nullptr);
    if (base && size) {
        // Narrow AND UTF-16LE for the two text markers — the same two-encoding rule Tier 1
        // uses, because some builds keep a literal only as a wide string.
        struct Pat { std::vector<uint8_t> bytes; int marker; };
        std::vector<Pat> pats;
        auto add = [&pats](const char* s, bool wide, int marker) {
            std::vector<uint8_t> b;
            for (const char* p = s; *p; ++p) {
                b.push_back(static_cast<uint8_t>(*p));
                if (wide) b.push_back(0);
            }
            pats.push_back({ std::move(b), marker });
        };
        add("UnrealEngine3", false, 0);
        add("UnrealEngine3", true,  0);
        add("SeqAct_",       false, 1);
        add("SeqAct_",       true,  1);
        add("PhysXLoader64", false, 2);   // an IMPORT name; never appears UTF-16

        const uint8_t* scan = reinterpret_cast<const uint8_t*>(base);
        for (size_t off = 0; off < size; ++off) {
            // Cheap first-byte reject: every pattern starts 'U', 'S' or 'P'.
            uint8_t c = scan[off];
            if (c != 'U' && c != 'S' && c != 'P') continue;
            for (const auto& p : pats) {
                if (found[p.marker] || p.bytes[0] != c) continue;
                if (off + p.bytes.size() > size) continue;
                if (memcmp(scan + off, p.bytes.data(), p.bytes.size()) == 0) {
                    found[p.marker] = true;
                    Sein::Info("SCAN:Ver", "PreUE4: marker '%s' hit at 0x%zX",
                               kMarkerNames[p.marker], off);
                }
            }
            if (found[0] && found[1] && found[2]) break;   // all three string markers in
        }
    }

    int score = 0;
    for (bool f : found) if (f) ++score;
    return score;
}

// Internal: scan a window of memory for any of a set of UE-related anchor
// substrings. Used by Tier 3 to require contextual evidence ("Engine",
// "Unreal", "UE4", "UE5", "++UE") near a bare version pattern, so that
// stray SDK strings like "PhysX 5.5.0" / "DirectX 12.5" can no longer
// fool the bare-pattern path.
// HasUEAnchorNearby moved to VersionNeedleScan.h (audit #5 G2) so it can be unit-tested
// and so the gated Tier 2/3 pass can share it. Behaviour is identical apart from a
// first-byte reject inside the window, which matters because the fused pass no longer
// returns the instant it sees a Tier 2 hit and so reaches more anchor calls.

// Detection result. tier:
//   1 = Tier 1 exact "++UE?+Release-X.Y" → highest confidence
//   2 = Tier 2 "Release" within 16 bytes → high confidence
//   3 = Tier 3 bare "X.Y.D" + UE anchor in 256-byte window → low confidence
//   0 = no detection
struct VersionScanResult {
    uint32_t version = 0;
    int      tier    = 0;
    // true = `version` is Grimoire::PRE_UE4_SENTINEL_VERSION, set by the marker check in the
    // terminal branch. File-local struct, so adding a field has no ABI impact.
    bool     preUE4  = false;
};

static VersionScanResult DetectVersionDetailed() {
    VersionScanResult r;
    Sein::Info("SCAN:Ver", "DetectVersion: Attempting to detect UE version...");

    // Fast path: PE VERSIONINFO resource (treated as Tier 1 — high confidence)
    uint32_t ver = DetectVersionFromPEResource();
    if (ver) {
        // ...with ONE exception: a result BELOW the support floor arms a total scan refusal, and
        // that is the most destructive verdict this detector can reach. A single uncorroborated
        // VS_FIXEDFILEINFO field is not enough evidence for it, and every other version signal in
        // this file demands context. So a sub-4.11 PE reading does NOT short-circuit as tier 1 —
        // fall through to the memory scan and let it agree or not. If it cannot corroborate, the
        // terminal branch keeps the PE value but marks it tier 3, which sets bLowConfidence, which
        // the refusal gate requires to be false. The cost of being wrong that way is a wasted
        // sweep (~0.35 s since the G2 gate, was ~29 s); the cost of being wrong the other way is refusing to scan a game that
        // works. (Audit #4 B25. Note the memory needle table floors at "4.18.", so a GENUINE
        // 4.0-4.10 title will not be corroborated and will pay that sweep — accepted.)
        if (ver >= Grimoire::MIN_SUPPORTED_UE_VERSION) { r.version = ver; r.tier = 1; return r; }
        Sein::Warn("SCAN:Ver", "DetectVersion: PE VERSIONINFO says UE %u, below the %u floor — "
                   "NOT accepting that on its own (it would refuse the whole scan). "
                   "Corroborating against the memory string scan.",
                   ver, Grimoire::MIN_SUPPORTED_UE_VERSION);
        r.version = ver;
        r.tier    = 3;   // downgraded unless the memory scan below agrees
    }

    Sein::Warn("SCAN:Ver", "DetectVersion: PE resource failed, falling back to memory string scan");

    uintptr_t base = Macht::GetModuleBase(nullptr);
    size_t    size = Macht::GetModuleSize(nullptr);
    if (!base || !size) {
        Sein::Warn("SCAN:Ver", "DetectVersion: Cannot get module base");
        return r;
    }

    const uint8_t* scan = reinterpret_cast<const uint8_t*>(base);

    // === Tier 1 + Tier 2/3 — see VersionNeedleScan.h ==========================
    // Both sweeps used to be written inline here as naive whole-image memcmp passes:
    // 4 for Tier 1, 19 for Tier 2/3. MEASURED on Elliot-Win64-Shipping.exe (460.0 MiB,
    // MSVC /O2 x64) that was 4.348 s + 24.465 s and 9,165,424,620 memcmp calls, all of
    // it uninterruptible — which is what audit #5 G2 filed. Gated on the first needle
    // byte the same cost is 0.2245 s + 0.0957 s and 39,351 compares, with identical
    // results on 7 real binaries plus a unit oracle. (audit #5 G2)
    //
    // The needle table, the tier rules and the anchor helper now live in the header so
    // dll_helpers_test can compile them against a naive reference — nothing in
    // dll/CMakeLists.txt compiles Genau.cpp, so this logic had no build-time check at all.
    //
    // Log lines below are deliberately byte-identical to the old inline versions:
    // scan-0.log is the only instrument that can verify this function on a real game.
    //
    // ⚠ VERSION DETECTION IS DELIBERATELY NOT CANCELLABLE. Do not "finish G2" by pasting
    // the (B18) `if ((off & 0xFFF) == 0 && Tot::Requested()) return 0;` idiom in here.
    // Three reasons, in order of severity:
    //   1. The verdict is PERSISTED. Flamme writes ueVersion / versionDetected /
    //      lowConfidence into UE5CEDumper.{Machine}.json keyed by PE hash, and FindAll
    //      skips detection entirely on every later launch ("skipped DetectVersion"). A
    //      cancelled sweep returning "nothing found" would therefore be memoized as the
    //      fallback guess FOREVER for that game — the exact never-invalidated-cache shape
    //      this audit just spent three commits removing from Ubel (U4/U16/U6).
    //   2. CountPreUE4Markers below feeds the PRE-UE4 REFUSAL. A truncated marker count is
    //      not a smaller answer, it is a WRONG one, and in the direction that can refuse to
    //      scan a supported game.
    //   3. After the gate the whole stretch is ~0.35 s on a 482 MB image, down from ~29 s.
    //      There is nothing left worth interrupting.
    // If it ever must become cancellable, the shape is: a `cancelled` flag on
    // VersionScanResult, plumbed through EnginePointers into Flamme::SaveResults so the
    // four version fields are SKIPPED rather than written, plus a distinguishable (not
    // merely truncated) return from CountPreUE4Markers — all in the SAME commit.

    // === Tier 1: Exact UE build strings "++UE5+Release-5.X" / "++UE4+Release-4.XX" ===
    // Run TWICE: once narrow, once UTF-16LE. Some builds keep the engine tag only as a wide
    // literal — DropIn 4.27.2 has four UTF-16 copies in .rdata/.rsrc and ZERO ASCII ones, so
    // the narrow-only scan was blind to it (and Tier 2/3 miss too: the ASCII "4.27" strings
    // in that image are build paths like "4.27\Engine\Source\...", with no trailing dot).
    {
        static const char* kPrefixNames[] = { "++UE5+Release-", "++UE4+Release-" };
        NeedleScanResult t1 = ScanVersionTier1(scan, size);
        if (t1.found()) {
            std::string bare = kVersionNeedles[t1.index].needle;
            if (!bare.empty() && bare.back() == '.') bare.pop_back();
            Sein::Info("SCAN:Ver", "DetectVersion: Tier 1 (%s) '%s%s' -> %u at 0x%zX",
                     t1.wide ? "utf16" : "ascii", kPrefixNames[t1.prefixIndex], bare.c_str(),
                     kVersionNeedles[t1.index].value, t1.offset);
            r.version = kVersionNeedles[t1.index].value; r.tier = 1; return r;
        }
    }

    // === Tier 2 + 3: needle scan with context checks ===
    // Tier 3 hardening: defer first hit and prefer Tier 2 hits across all
    // patterns. If no Tier 2 hit exists, the deferred Tier 3 hit is returned (low
    // confidence). This stops a stray "5.5.0" SDK string near the start of the
    // module from out-racing a real "Release-4.27" string later in the module.
    VersionScanResult bestTier3{};
    {
        NeedleScanResult t23 = ScanVersionTier23(scan, size);
        if (t23.tier == 2) {
            Sein::Info("SCAN:Ver", "DetectVersion: Tier 2 Release prefix -> %u at 0x%zX",
                     kVersionNeedles[t23.index].value, t23.offset);
            r.version = kVersionNeedles[t23.index].value; r.tier = 2; return r;
        }
        if (t23.tier == 3) {
            Sein::Info("SCAN:Ver", "DetectVersion: Tier 3 candidate (deferred) -> %u at 0x%zX",
                     kVersionNeedles[t23.index].value, t23.offset);
            bestTier3.version = kVersionNeedles[t23.index].value;
            bestTier3.tier    = 3;
        }
    }

    if (bestTier3.tier == 3) {
        Sein::Warn("SCAN:Ver", "DetectVersion: Tier 3 (low confidence) -> %u",
                   bestTier3.version);
        return bestTier3;
    }

    // Nothing in the UE4/UE5 vocabulary matched. Before giving up — and letting FindAll fall
    // back to guessing 5.4 — ask the OPPOSITE question: is this positively a pre-UE4 engine?
    //
    // Placing the check HERE, rather than at the top of this function, IS the false-positive
    // defence. Reaching this line already proves the PE resource, Tier 1 (ASCII + UTF-16LE),
    // Tier 2 and Tier 3 all found nothing, so "no UE4/UE5 evidence" is a STRUCTURAL property of
    // the control flow rather than a separate test that could be written wrong. Any binary
    // carrying a table-recognised engine tag returned long before this point and can never be
    // gated here.
    //
    // RESIDUAL EXPOSURE, stated so it is not forgotten: the needle table floors at "4.18.", so
    // a genuine UE 4.11-4.17 title whose PE resource is also unusable reaches this same line
    // (Fantasynth carries "++UE4+Release-4.13" and Tier 1 still misses it — its PE resource is
    // what saves it). That is precisely why the bar is 2 markers and why every marker is
    // UE3-exclusive rather than merely "old-looking".
    int markers = CountPreUE4Markers();
    if (markers >= kPreUE4MarkerThreshold) {
        Sein::Warn("SCAN:Ver",
                   "DetectVersion: PRE-UE4 engine POSITIVELY identified (%d/%d markers, %d needed)"
                   " -> sentinel %u. The AOB scan will be skipped, not attempted.",
                   markers, kPreUE4MarkerCount, kPreUE4MarkerThreshold,
                   Grimoire::PRE_UE4_SENTINEL_VERSION);
        r.version = Grimoire::PRE_UE4_SENTINEL_VERSION;
        // Tier 1: this is a positive identification, not a fragile guess. It must NOT read as
        // low confidence — FindAll's too-old gate requires !bLowConfidence.
        r.tier    = 1;
        r.preUE4  = true;
        return r;
    }

    Sein::Warn("SCAN:Ver", "DetectVersion: Could not detect UE version from PE or memory "
               "(pre-UE4 markers %d/%d, below the %d needed)",
               markers, kPreUE4MarkerCount, kPreUE4MarkerThreshold);
    return r;
}

uint32_t DetectVersion() {
    return DetectVersionDetailed().version;
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
    int32_t count = Aura::GetCount();
    for (int32_t i = 0; i < count; ++i) {
        uintptr_t obj = Aura::GetByIndex(i);
        if (!obj) continue;

        // Check class name == "ScriptStruct"
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        uint32_t clsNameIdx = 0;
        if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;
        std::string clsName = Serie::GetString(clsNameIdx);
        if (clsName != "ScriptStruct") continue;

        // Check object name matches
        uint32_t nameIdx = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx)) continue;
        std::string name = Serie::GetString(nameIdx);
        if (name == structName) {
            Sein::Info("DYNO", "FindStructByName: Found '%s' at 0x%llX (index=%d)",
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
    Sein::Info("DYNO", "DetectCasePreservingName: Probing UObject layout...");

    // Collect a few UObjects to test consensus
    int voteStandard = 0, voteCPN = 0;
    int tested = 0;

    int32_t count = Aura::GetCount();
    for (int32_t i = 1; i < count && tested < 20; ++i) {
        uintptr_t obj = Aura::GetByIndex(i);
        if (!obj) continue;

        // Read Class at +0x10 to confirm this is a valid UObject
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;
        if (!Grimoire::IsUserspacePointer(cls)) continue;

        // Read candidate Outer at standard offset 0x20
        uintptr_t outerAt20 = 0;
        Macht::ReadSafe(obj + 0x20, outerAt20);

        // Read candidate Outer at CPN offset 0x28
        uintptr_t outerAt28 = 0;
        Macht::ReadSafe(obj + 0x28, outerAt28);

        // A valid Outer is either null (Package-level objects) or a plausible user-space pointer.
        // Also: Outer must be a UObject, so its Class at +0x10 should be a valid pointer too.
        auto isValidOuter = [](uintptr_t val) -> bool {
            if (val == 0) return true; // null = root package
            if (!Grimoire::IsUserspacePointer(val)) return false;
            uintptr_t outerCls = 0;
            if (!Macht::ReadSafe(val + Grimoire::OFF_UOBJECT_CLASS, outerCls)) return false;
            return outerCls > Grimoire::PTR_USERSPACE_MIN && outerCls < Grimoire::PTR_USERSPACE_MAX;
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
            Macht::ReadSafe(obj + 0x18, compIdx);
            Macht::ReadSafe(obj + 0x1C, dispIdx);
            if (compIdx == dispIdx && compIdx > 0 && compIdx < 0x00FFFFFF) {
                ++voteCPN;
            } else {
                ++voteStandard;
            }
        }
        ++tested;
    }

    Sein::Info("DYNO", "DetectCasePreservingName: votes standard=%d, CPN=%d (tested %d objects)",
             voteStandard, voteCPN, tested);

    if (voteCPN > voteStandard) {
        DynOff::bCasePreservingName = true;
        DynOff::UOBJECT_OUTER = 0x28;
        Sein::Info("DYNO", "DetectCasePreservingName: CPN ACTIVE — UObject::Outer = +0x28");
    } else {
        DynOff::bCasePreservingName = false;
        DynOff::UOBJECT_OUTER = 0x20;
        Sein::Info("DYNO", "DetectCasePreservingName: Standard FName — UObject::Outer = +0x20");
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
    Sein::Info("DYNO", "DetectUPropertyMode: Checking for UProperty vs FProperty (UE version=%u)...", ueVersion);

    // Primary: version-based detection (most reliable)
    if (ueVersion >= 425) {
        // UE4.25 introduced FProperty/FField; all UE5 versions use it
        DynOff::bUseFProperty = true;
        Sein::Info("DYNO", "DetectUPropertyMode: FProperty mode (UE version %u >= 425)", ueVersion);
        return;
    }

    if (ueVersion > 0 && ueVersion < 425) {
        // Confirmed UE4 <4.25 — uses UProperty
        DynOff::bUseFProperty = false;
        DynOff::UFIELD_NEXT = DynOff::bCasePreservingName ? 0x30 : 0x28;
        Sein::Info("DYNO", "DetectUPropertyMode: UProperty mode (UE version %u < 425), UField::Next = +0x%02X",
                 ueVersion, DynOff::UFIELD_NEXT);
        return;
    }

    // Fallback: UE version unknown (0) — heuristic detection via GObjects.
    // Search for actual property *instances* whose class name ends with "Property".
    // In UE4 <4.25: objects like "Owner" (class=ObjectProperty) exist in GObjects.
    // In UE5: only the UClass definition "ObjectProperty" exists (class=Class), not instances.
    Sein::Info("DYNO", "DetectUPropertyMode: Version unknown — using heuristic GObjects scan");

    bool foundPropertyInstance = false;
    int32_t count = Aura::GetCount();

    for (int32_t i = 0; i < count && i < 50000; ++i) {
        uintptr_t obj = Aura::GetByIndex(i);
        if (!obj) continue;

        // Read this object's class
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        // Get the class name
        uint32_t clsNameIdx = 0;
        if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;
        std::string clsName = Serie::GetString(clsNameIdx);

        // Skip "Class" — we don't want the UClass definition, we want instances
        if (clsName == "Class" || clsName == "ScriptStruct" || clsName == "Package" ||
            clsName == "Function" || clsName == "Enum") continue;

        // Check if class name ends with "Property" (e.g., "ObjectProperty", "IntProperty")
        if (clsName.size() > 8 && clsName.substr(clsName.size() - 8) == "Property") {
            // This is a UProperty instance — confirms UE4 <4.25 mode
            uint32_t objNameIdx = 0;
            Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, objNameIdx);
            std::string objName = Serie::GetString(objNameIdx);
            foundPropertyInstance = true;
            Sein::Info("DYNO", "DetectUPropertyMode: Found UProperty instance '%s' (class=%s) at 0x%llX",
                     objName.c_str(), clsName.c_str(), (unsigned long long)obj);
            break;
        }
    }

    if (foundPropertyInstance) {
        DynOff::bUseFProperty = false;
        DynOff::UFIELD_NEXT = DynOff::bCasePreservingName ? 0x30 : 0x28;
        Sein::Info("DYNO", "DetectUPropertyMode: UProperty mode (heuristic), UField::Next = +0x%02X",
                 DynOff::UFIELD_NEXT);
    } else {
        DynOff::bUseFProperty = true;
        Sein::Info("DYNO", "DetectUPropertyMode: FProperty mode (no UProperty instances found in GObjects)");
    }
}

bool ValidateAndFixOffsets(uint32_t ueVersion) {
    Sein::Info("DYNO", "ValidateAndFixOffsets: Starting dynamic offset detection...");

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
                Sein::Info("DYNO", "ValidateAndFixOffsets: Set UE5.1.1+ defaults (FFieldVariant=0x08)");
                // UE5.3+ uses tagged FFieldVariant: LSB=1 means UObject, LSB=0 means FField
                if (ueVersion >= 503) {
                    DynOff::bTaggedFFieldVariant = true;
                    Sein::Info("DYNO", "ValidateAndFixOffsets: UE5.3+ tagged FFieldVariant enabled");
                }
            }
            // UE5.1 is ambiguous (5.1.0 = larger, 5.1.1+ = smaller), leave as-is for probing
        }
    }

    // Step 3: Find "Guid" or "Vector" struct for probing
    uintptr_t guidStruct = FindStructByName("Guid");
    uintptr_t vectorStruct = FindStructByName("Vector");

    if (!guidStruct && !vectorStruct) {
        Sein::Warn("DYNO", "ValidateAndFixOffsets: Cannot find Guid or Vector struct — trying heuristic fallback");

        // ═══════════════════════════════════════════════════════════════════
        // Heuristic fallback (no Guid/Vector struct to probe against)
        //
        // Two-phase approach:
        //   Phase A: Probe USTRUCT_CHILDPROPS — the default (0x50) may be wrong
        //            for newer UE versions (e.g. UE 5.7 may have shifted UStruct layout).
        //            Scans multiple candidate offsets on GObjects classes to find which
        //            one leads to valid FField chains with resolvable "Property" type names.
        //   Phase B: Probe FPROPERTY_OFFSET — find Offset_Internal within FProperty
        //            by checking for strictly increasing sequences bounded by PropertiesSize.
        // ═══════════════════════════════════════════════════════════════════
        if (DynOff::bUseFProperty) {

            // ── Phase A: Detect USTRUCT_CHILDPROPS ──────────────────────────
            // For each candidate offset (0x48 to 0x68, step 8), check if reading
            // a pointer from GObjects UClass/UStruct objects leads to valid FField
            // chains whose FFieldClass resolves to a name containing "Property".
            Sein::Info("DYNO", "Phase A: Probing USTRUCT_CHILDPROPS...");

            int bestChildPropsOff = -1;
            int bestChildPropsScore = 0;

            for (int cpOff = 0x40; cpOff <= 0x70; cpOff += 8) {
                int score = 0;
                int tested = 0;
                int objLimit = (std::min)(Aura::GetCount(), 80000);
                for (int32_t i = 0; i < objLimit && tested < 30; ++i) {
                    uintptr_t obj = Aura::GetByIndex(i);
                    if (!obj) continue;

                    uintptr_t cp = 0;
                    if (!Macht::ReadSafe(obj + cpOff, cp) || !cp) continue;
                    cp = DynOff::StripFFieldTag(cp);
                    if (!Grimoire::IsUserspacePointer(cp)) continue;

                    // Check FFieldClass* at first FField
                    uintptr_t fc = 0;
                    if (!Macht::ReadSafe(cp + DynOff::FFIELD_CLASS, fc) || !fc) continue;
                    if (!Grimoire::IsUserspacePointer(fc)) continue;

                    // Resolve FFieldClass name — must contain "Property"
                    uint32_t fcNameIdx = 0;
                    if (!Macht::ReadSafe(fc + DynOff::FFIELDCLASS_NAME, fcNameIdx)) continue;
                    std::string fcName = Serie::GetString(fcNameIdx);
                    if (fcName.find("Property") == std::string::npos) continue;

                    // Validate chain has 2+ entries with valid Next pointers
                    uintptr_t next = 0;
                    if (!Macht::ReadSafe(cp + DynOff::FFIELD_NEXT, next)) continue;
                    next = DynOff::StripFFieldTag(next);
                    if (next && next > Grimoire::PTR_USERSPACE_MIN && next < Grimoire::PTR_USERSPACE_MAX) {
                        // Second entry also has valid FFieldClass with "Property"?
                        uintptr_t fc2 = 0;
                        if (Macht::ReadSafe(next + DynOff::FFIELD_CLASS, fc2) && fc2 > 0x10000) {
                            uint32_t fc2Idx = 0;
                            if (Macht::ReadSafe(fc2 + DynOff::FFIELDCLASS_NAME, fc2Idx)) {
                                std::string fc2Name = Serie::GetString(fc2Idx);
                                if (fc2Name.find("Property") != std::string::npos) {
                                    score += 2; // Strong match: 2-entry chain with Property types
                                }
                            }
                        }
                    }
                    ++score;
                    ++tested;
                }

                Sein::Info("DYNO", "  CHILDPROPS probe +0x%02X: score=%d (tested %d objects)", cpOff, score, tested);

                if (score > bestChildPropsScore) {
                    bestChildPropsScore = score;
                    bestChildPropsOff = cpOff;
                }
            }

            if (bestChildPropsOff >= 0 && bestChildPropsOff != DynOff::USTRUCT_CHILDPROPS) {
                int oldCP = DynOff::USTRUCT_CHILDPROPS;
                DynOff::USTRUCT_CHILDPROPS = bestChildPropsOff;
                // Derive related UStruct offsets (relative positions are stable across UE versions)
                DynOff::USTRUCT_CHILDREN  = bestChildPropsOff - 0x08;
                DynOff::USTRUCT_SUPER     = bestChildPropsOff - 0x10;
                DynOff::USTRUCT_PROPSSIZE = bestChildPropsOff + 0x08;
                Sein::Info("DYNO", "Phase A: USTRUCT_CHILDPROPS changed 0x%02X -> 0x%02X "
                    "(score=%d, Super=0x%02X, PropsSize=0x%02X)",
                    oldCP, bestChildPropsOff, bestChildPropsScore,
                    DynOff::USTRUCT_SUPER, DynOff::USTRUCT_PROPSSIZE);
            } else if (bestChildPropsOff >= 0) {
                Sein::Info("DYNO", "Phase A: USTRUCT_CHILDPROPS confirmed at 0x%02X (score=%d)",
                    DynOff::USTRUCT_CHILDPROPS, bestChildPropsScore);
            } else {
                Sein::Warn("DYNO", "Phase A: No valid CHILDPROPS offset found, keeping default 0x%02X",
                    DynOff::USTRUCT_CHILDPROPS);
            }

            // ── Phase B: Detect FPROPERTY_OFFSET ────────────────────────────
            // Now that USTRUCT_CHILDPROPS is (hopefully) correct, collect FField
            // chain addresses from multiple classes and probe for Offset_Internal.
            Sein::Info("DYNO", "Phase B: Probing FPROPERTY_OFFSET...");

            // Helper: infer natural alignment from FFieldClass type name.
            // Pointers/containers = 8, int/float = 4, short = 2, bool/byte = 1.
            // Returns 0 for unknown types (no alignment penalty applied).
            auto GetNaturalAlignment = [](uintptr_t ffieldAddr) -> int {
                uintptr_t fc = 0;
                if (!Macht::ReadSafe(ffieldAddr + DynOff::FFIELD_CLASS, fc) || !fc) return 0;
                if (!Grimoire::IsUserspacePointer(fc)) return 0;
                uint32_t nameIdx = 0;
                if (!Macht::ReadSafe(fc + DynOff::FFIELDCLASS_NAME, nameIdx)) return 0;
                std::string tn = Serie::GetString(nameIdx);
                if (tn.empty()) return 0;

                // 8-byte: all pointer/container/64-bit types
                if (tn.find("ObjectProperty") != std::string::npos ||
                    tn.find("ClassProperty")  != std::string::npos ||
                    tn == "ArrayProperty" || tn == "MapProperty" || tn == "SetProperty" ||
                    tn == "NameProperty"  || tn == "StrProperty"  || tn == "TextProperty" ||
                    tn == "Int64Property" || tn == "UInt64Property" || tn == "DoubleProperty" ||
                    tn == "DelegateProperty" || tn == "MulticastDelegateProperty" ||
                    tn == "MulticastInlineDelegateProperty" || tn == "MulticastSparseDelegateProperty" ||
                    tn == "WeakObjectProperty"  || tn == "LazyObjectProperty" ||
                    tn == "SoftObjectProperty"  || tn == "SoftClassProperty" ||
                    tn == "InterfaceProperty")
                    return 8;
                // 4-byte: int/uint32/float/enum
                if (tn == "IntProperty" || tn == "UInt32Property" || tn == "FloatProperty" ||
                    tn == "EnumProperty")
                    return 4;
                // 2-byte: short
                if (tn == "UInt16Property" || tn == "Int16Property")
                    return 2;
                // 1-byte: bool/byte
                if (tn == "BoolProperty" || tn == "ByteProperty" || tn == "Int8Property")
                    return 1;
                return 0;
            };

            struct FallbackCandidate {
                uintptr_t classAddr;
                uintptr_t fAddrs[8];
                int       nFields;
                int32_t   propsSize;
            };
            FallbackCandidate candidates[5] = {};
            int nCandidates = 0;

            int limit = (std::min)(Aura::GetCount(), 100000);
            for (int32_t i = 0; i < limit && nCandidates < 5; ++i) {
                uintptr_t obj = Aura::GetByIndex(i);
                if (!obj) continue;

                uintptr_t cp = 0;
                if (!Macht::ReadSafe(obj + DynOff::USTRUCT_CHILDPROPS, cp) || !cp) continue;
                cp = DynOff::StripFFieldTag(cp);
                if (!Grimoire::IsUserspacePointer(cp)) continue;

                uintptr_t fc = 0;
                if (!Macht::ReadSafe(cp + DynOff::FFIELD_CLASS, fc) || !fc) continue;
                if (!Grimoire::IsUserspacePointer(fc)) continue;

                auto& c = candidates[nCandidates];
                c.classAddr = obj;
                c.nFields = 0;

                uintptr_t cur = cp;
                while (cur && c.nFields < 8) {
                    c.fAddrs[c.nFields++] = cur;
                    uintptr_t next = 0;
                    if (!Macht::ReadSafe(cur + DynOff::FFIELD_NEXT, next)) break;
                    cur = DynOff::StripFFieldTag(next);
                }
                if (c.nFields < 4) continue;

                int32_t ps = 0;
                Macht::ReadSafe(obj + DynOff::USTRUCT_PROPSSIZE, ps);
                if (ps <= 0 || ps > 0x100000) continue;
                c.propsSize = ps;

                uint32_t nameIdx = 0;
                std::string className;
                if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx))
                    className = Serie::GetString(nameIdx);
                Sein::Info("DYNO", "  Phase B candidate[%d]: '%s' at 0x%llX, fields=%d, propsSize=%d",
                    nCandidates, className.c_str(), (unsigned long long)obj, c.nFields, ps);

                ++nCandidates;
            }

            if (nCandidates > 0) {
                int bestProbe = -1;
                int bestScore = 0;

                for (int probe = 0x30; probe <= 0x78; probe += 4) {
                    int totalScore = 0;
                    int classesValid = 0;

                    for (int ci = 0; ci < nCandidates; ++ci) {
                        auto& c = candidates[ci];
                        int score = 0;
                        int32_t prevOff = -1;
                        int32_t firstOff = -1;
                        bool valid = true;
                        bool hasStrictIncrease = false;

                        for (int i = 0; i < c.nFields && valid; ++i) {
                            int32_t off = -1;
                            if (!Macht::ReadSafe(c.fAddrs[i] + probe, off)) { valid = false; break; }
                            if (off < 0 || off >= c.propsSize)            { valid = false; break; }
                            if (prevOff >= 0 && off < prevOff)            { valid = false; break; }
                            if (i == 0) firstOff = off;
                            if (off > prevOff) hasStrictIncrease = true;
                            if (off >= prevOff) ++score;
                            prevOff = off;

                            // Alignment bonus/penalty: reward correctly aligned offsets
                            int align = GetNaturalAlignment(c.fAddrs[i]);
                            if (align > 0 && (off % align) == 0)         score += 2;
                            else if (align >= 4 && (off % align) != 0)   score -= 1;
                        }

                        // Validation: require strict increase AND first offset >= 0x20
                        // (real FProperty::Offset_Internal is always >= UObject header size)
                        if (valid && hasStrictIncrease && firstOff >= 0x20) {
                            totalScore += score;
                            ++classesValid;
                        }
                    }

                    if (classesValid > 0) {
                        Sein::Debug("DYNO", "  probe +0x%02X: validClasses=%d, totalScore=%d",
                            probe, classesValid, totalScore);
                    }

                    if (classesValid > 0 && totalScore > bestScore) {
                        bestScore = totalScore;
                        bestProbe = probe;
                    }
                }

                if (bestProbe >= 0 && bestProbe != DynOff::FPROPERTY_OFFSET) {
                    Sein::Info("DYNO", "Phase B: FPROPERTY_OFFSET changed 0x%02X -> 0x%02X (score=%d)",
                        DynOff::FPROPERTY_OFFSET, bestProbe, bestScore);
                    DynOff::FPROPERTY_OFFSET = bestProbe;
                    DynOff::FPROPERTY_ELEMSIZE = bestProbe - 0x10;
                    DynOff::FPROPERTY_FLAGS    = bestProbe - 0x0C;
                    DynOff::FSTRUCTPROP_STRUCT  = bestProbe + 0x2C;
                    DynOff::FARRAYPROP_INNER   = bestProbe + 0x2C;
                    DynOff::FBOOLPROP_FIELDSIZE = bestProbe + 0x2C;
                    DynOff::FBYTEPROP_ENUM     = bestProbe + 0x2C;
                    DynOff::FENUMPROP_ENUM     = DynOff::FBYTEPROP_ENUM + 8;  // Enum follows UnderlyingProp
                } else if (bestProbe >= 0) {
                    Sein::Info("DYNO", "Phase B: Confirmed default FPROPERTY_OFFSET=0x%02X", DynOff::FPROPERTY_OFFSET);
                } else {
                    Sein::Warn("DYNO", "Phase B: No valid FPROPERTY_OFFSET found — dumping raw probe data");
                    if (nCandidates > 0) {
                        auto& c = candidates[0];
                        for (int probe = 0x30; probe <= 0x78; probe += 4) {
                            char buf[256];
                            int pos = snprintf(buf, sizeof(buf), "  raw +0x%02X:", probe);
                            for (int i = 0; i < c.nFields && i < 6; ++i) {
                                int32_t val = 0;
                                Macht::ReadSafe(c.fAddrs[i] + probe, val);
                                pos += snprintf(buf + pos, sizeof(buf) - pos, " %d", val);
                            }
                            Sein::Debug("DYNO", "%s", buf);
                        }
                    }
                }
            } else {
                Sein::Warn("DYNO", "Phase B: No class with 4+ fields found for FPROPERTY_OFFSET probing");
            }
        }

        // Log final offset state
        Sein::Info("DYNO", "Heuristic final: USTRUCT_SUPER=0x%02X CHILDPROPS=0x%02X PROPSSIZE=0x%02X "
            "FFIELD_NEXT=0x%02X FFIELD_NAME=0x%02X FPROPERTY_OFFSET=0x%02X",
            DynOff::USTRUCT_SUPER, DynOff::USTRUCT_CHILDPROPS, DynOff::USTRUCT_PROPSSIZE,
            DynOff::FFIELD_NEXT, DynOff::FFIELD_NAME, DynOff::FPROPERTY_OFFSET);

        // The probe RAN but gave up — DynOff holds unmeasured defaults. Say so, so the
        // summary cannot claim validated=yes (it used to, three lines after this warning).
        DynOff::g_offsetsFallbackReason = "no-guid-or-vector-struct";
        DynOff::bOffsetsProbeRan.store(true, std::memory_order_release);
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

    Sein::Info("DYNO", "ValidateAndFixOffsets: Using struct '%s' at 0x%llX", testName, (unsigned long long)testStruct);

    // Step 4: Find ChildProperties (or Children for UE4 UProperty mode)
    uintptr_t childProps = 0;
    int childPropsOff = -1;

    // For UE4 UProperty mode, the chain is in UStruct::Children and items are UObject-derived.
    // For FProperty mode, the chain is in UStruct::ChildProperties and items are FField-based.

    // Probe offsets 0x38..0x80 in 8-byte steps for a valid chain head pointer
    for (int off = 0x38; off <= 0x80; off += 8) {
        uintptr_t ptr = 0;
        if (!Macht::ReadSafe(testStruct + off, ptr) || !ptr) continue;

        // Basic pointer validity: must be in user space
        if (!Grimoire::IsUserspacePointer(ptr)) continue;

        if (DynOff::bUseFProperty) {
            // FProperty mode: check if this pointer has an FFieldClass* at +0x08
            uintptr_t fieldClass = 0;
            if (!Macht::ReadSafe(ptr + DynOff::FFIELD_CLASS, fieldClass) || !fieldClass) continue;
            if (!Grimoire::IsUserspacePointer(fieldClass)) continue;

            // The FFieldClass should have an FName that resolves to a *Property type name.
            //
            // This read is ALSO where FFIELDCLASS_NAME gets decided, and it has to be:
            // UE 5.8 made ~FFieldClass() virtual, putting a vfptr at +0x00 and moving Name
            // to +0x08 (see the DynOff::FFIELDCLASS_NAME comment). Since this very loop is
            // what discovers ChildProperties, a later phase cannot fix it — a wrong name
            // offset here means NO candidate matches at ANY probed struct offset, the whole
            // validation gives up on defaults, and the walker then types every property as
            // Scalar. That is exactly what UE 5.8 did before this joint probe existed.
            const int fcNameOff = DynOff::PickFFieldClassNameOffset(
                [&](int o) -> std::string {
                    uint32_t idx = 0;
                    if (!Macht::ReadSafe(fieldClass + o, idx)) return std::string();
                    return Serie::GetString(idx);
                });
            if (fcNameOff >= 0) {
                uint32_t fcNameIdx = 0;
                Macht::ReadSafe(fieldClass + fcNameOff, fcNameIdx);
                const std::string fcName = Serie::GetString(fcNameIdx);

                DynOff::FFIELDCLASS_NAME = fcNameOff;   // latch for every downstream reader
                childProps = ptr;
                childPropsOff = off;
                Sein::Info("DYNO", "ValidateAndFixOffsets: ChildProperties found at struct+0x%02X → 0x%llX (FFieldClass='%s', FFieldClass::Name=+0x%02X)",
                         off, (unsigned long long)ptr, fcName.c_str(), fcNameOff);
                break;
            }
        } else {
            // UProperty mode: items are UObjects. Check if Class at +0x10 resolves to a *Property class.
            uintptr_t cls = 0;
            if (!Macht::ReadSafe(ptr + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;
            if (!Grimoire::IsUserspacePointer(cls)) continue;

            uint32_t clsNameIdx = 0;
            if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;
            std::string clsName = Serie::GetString(clsNameIdx);
            if (clsName.find("Property") != std::string::npos) {
                childProps = ptr;
                childPropsOff = off;
                Sein::Info("DYNO", "ValidateAndFixOffsets: Children (UProperty) found at struct+0x%02X → 0x%llX (Class='%s')",
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
        Sein::Warn("DYNO", "ValidateAndFixOffsets: FProperty scan failed on '%s', retrying as UProperty...", testName);

        // Expand probe range to include UE4 offsets (UStruct may start at +0x30)
        for (int off = 0x28; off <= 0x80; off += 8) {
            uintptr_t ptr = 0;
            if (!Macht::ReadSafe(testStruct + off, ptr) || !ptr) continue;
            if (!Grimoire::IsUserspacePointer(ptr)) continue;

            // UProperty mode: items are UObjects. Check if Class at +0x10 resolves to a *Property class.
            uintptr_t cls = 0;
            if (!Macht::ReadSafe(ptr + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;
            if (!Grimoire::IsUserspacePointer(cls)) continue;

            uint32_t clsNameIdx = 0;
            if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;
            std::string clsName = Serie::GetString(clsNameIdx);
            if (clsName.find("Property") != std::string::npos) {
                childProps = ptr;
                childPropsOff = off;
                DynOff::bUseFProperty = false;
                DynOff::bTaggedFFieldVariant = false;  // UE4 has no tagged FFieldVariant
                DynOff::UFIELD_NEXT = DynOff::bCasePreservingName ? 0x30 : 0x28;
                Sein::Info("DYNO", "ValidateAndFixOffsets: FALLBACK — UProperty mode detected. "
                         "Children at struct+0x%02X → 0x%llX (Class='%s')",
                         off, (unsigned long long)ptr, clsName.c_str());
                break;
            }
        }
    }

    if (!childProps) {
        Sein::Warn("DYNO", "ValidateAndFixOffsets: Cannot find ChildProperties in '%s', keeping defaults", testName);
        DynOff::g_offsetsFallbackReason = "childprops-probe-failed";
        DynOff::bOffsetsProbeRan.store(true, std::memory_order_release);
        return false;
    }

    // Which probes below GAVE UP and kept an unmeasured default (audit #5 G1).
    //
    // Every "keeping default" branch from here on falls THROUGH to the success tail, which
    // used to store bOffsetsValidated = true unconditionally. Per Grimoire.h's contract that
    // flag means "the values were actually MEASURED", so a run that could not find
    // FField::Next reported validated=yes over a version guess — and Ubel.cpp:4206 documents
    // what a blind FPROPERTY_ELEMSIZE then costs downstream (audit #5 U1).
    //
    // A bitmask rather than a bool because the reason string has to name WHICH probe fell
    // back: a maintainer reading `validated=NO (DEFAULTS) reason=` needs to know where to look.
    enum : uint32_t {
        UNMEASURED_FFIELD_NAME    = 1u << 0,
        UNMEASURED_FFIELD_NEXT    = 1u << 1,
        UNMEASURED_PROP_OFFSET    = 1u << 2,
        UNMEASURED_PROP_ELEMSIZE  = 1u << 3,
    };
    uint32_t unmeasured = 0;

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
            if (!Macht::ReadSafe(childProps + off, nameIdx)) continue;
            if (nameIdx == 0 || nameIdx > 0x00FFFFFF) continue;

            std::string name = Serie::GetString(nameIdx);
            if (name == expectedFirst || name == expectedSecond) {
                nameOff = off;
                Sein::Info("DYNO", "ValidateAndFixOffsets: FField::Name at FField+0x%02X (resolved='%s')",
                         off, name.c_str());
                break;
            }
        }

        if (nameOff < 0) {
            Sein::Warn("DYNO", "ValidateAndFixOffsets: Cannot find FField::Name, keeping default 0x%02X",
                     DynOff::FFIELD_NAME);
            unmeasured |= UNMEASURED_FFIELD_NAME;
        } else {
            DynOff::FFIELD_NAME = nameOff;
        }
    } else {
        // UProperty (UObject-derived): Name is at UObject::Name = 0x18 (always stable)
        nameOff = Grimoire::OFF_UOBJECT_NAME;
        Sein::Info("DYNO", "ValidateAndFixOffsets: UProperty::Name at UObject+0x%02X (standard)", nameOff);
    }

    // Step 6: Find Next offset on the chain
    int nextOff = -1;

    if (DynOff::bUseFProperty) {
        // FField::Next: Probe 8-byte aligned offsets 0x10..0x38
        for (int off = 0x10; off <= 0x38; off += 8) {
            if (off == DynOff::FFIELD_CLASS) continue; // Skip the Class pointer

            uintptr_t nextPtr = 0;
            if (!Macht::ReadSafe(childProps + off, nextPtr) || !nextPtr) continue;

            // UE5.3+: offset 0x10 is FFieldVariant Owner (tagged pointer).
            // If LSB is set, this is a UObject owner reference — skip it as Next candidate.
            if (DynOff::IsFFieldVariantUObject(nextPtr)) continue;
            nextPtr = DynOff::StripFFieldTag(nextPtr);

            if (!Grimoire::IsUserspacePointer(nextPtr)) continue;

            // Verify it looks like an FField: check FFieldClass at +0x08
            uintptr_t nextFieldClass = 0;
            if (!Macht::ReadSafe(nextPtr + DynOff::FFIELD_CLASS, nextFieldClass) || !nextFieldClass) continue;

            uint32_t fcNameIdx2 = 0;
            // Latched by the ChildProperties probe above (Step 4 runs before this) — a bare
            // +0 read here would break on UE 5.8, where FFieldClass::Name is at +0x08.
            if (!Macht::ReadSafe(nextFieldClass + DynOff::FFIELDCLASS_NAME, fcNameIdx2)) continue;
            std::string fcName2 = Serie::GetString(fcNameIdx2);
            if (fcName2.find("Property") == std::string::npos) continue;

            // Double-check: read FName at the detected Name offset on the next field
            if (nameOff >= 0) {
                uint32_t nextNameIdx = 0;
                if (Macht::ReadSafe(nextPtr + nameOff, nextNameIdx) && nextNameIdx > 0) {
                    std::string nextName = Serie::GetString(nextNameIdx);
                    if (!nextName.empty() && nextName.length() <= 64) {
                        nextOff = off;
                        Sein::Info("DYNO", "ValidateAndFixOffsets: FField::Next at FField+0x%02X (next='%s')",
                                 off, nextName.c_str());
                        break;
                    }
                }
            } else {
                nextOff = off;
                Sein::Info("DYNO", "ValidateAndFixOffsets: FField::Next at FField+0x%02X (unverified name)", off);
                break;
            }
        }

        if (nextOff < 0) {
            Sein::Warn("DYNO", "ValidateAndFixOffsets: Cannot find FField::Next, keeping default 0x%02X",
                     DynOff::FFIELD_NEXT);
            unmeasured |= UNMEASURED_FFIELD_NEXT;
        } else {
            DynOff::FFIELD_NEXT = nextOff;
        }

        // Step 6.5: Infer FFieldVariant size from detected Next offset.
        // If UE5.1.1+ defaults were set (Next=0x18) but probing found Next=0x20,
        // the game uses FFieldVariant=0x10 (UE5.0-5.1.0 layout) despite its version number.
        // Common in Square Enix forks (DQ HD-2D series reports UE505 but uses UE5.0 FField layout).
        // Fix: set Name = Next + 8, disable tagged FFieldVariant.
        // FProperty offsets will be re-probed correctly in Step 8 with fixed field names.
        if (nextOff == 0x20 && nameOff < 0) {
            DynOff::FFIELD_NAME = 0x28;  // FName follows Next in FField layout
            nameOff = 0x28;
            // Derived from a MEASURED Next, not a version guess — the same class of
            // derivation as Step 9's, which nobody counts as unmeasured. Clear the bit the
            // Step-5 give-up set, or the run reports a fallback it has already recovered from.
            unmeasured &= ~UNMEASURED_FFIELD_NAME;
            Sein::Info("DYNO", "ValidateAndFixOffsets: Inferred FField::Name=0x28 from Next=0x20 (FFieldVariant=0x10)");

            if (DynOff::bTaggedFFieldVariant) {
                DynOff::bTaggedFFieldVariant = false;
                Sein::Info("DYNO", "ValidateAndFixOffsets: Disabled tagged FFieldVariant (FFieldVariant=0x10 layout)");
            }
        }
    } else {
        // UProperty (UObject-derived): Probe for UField::Next
        // Standard UE4: Next at +0x28 (or +0x30 for CPN), but some games have extra
        // UObject/UField members that push Next further (e.g., DQ XI S has Next at +0x38).
        // Probe candidate offsets and validate by checking the next item is a *Property UObject.
        int defaultNext = DynOff::UFIELD_NEXT;  // 0x28 or 0x30

        for (int off = 0x20; off <= 0x48; off += 8) {
            uintptr_t nextPtr = 0;
            if (!Macht::ReadSafe(childProps + off, nextPtr) || !nextPtr) continue;
            if (!Grimoire::IsUserspacePointer(nextPtr)) continue;

            // Verify: target must be a UObject whose Class name contains "Property"
            uintptr_t nextCls = 0;
            if (!Macht::ReadSafe(nextPtr + Grimoire::OFF_UOBJECT_CLASS, nextCls) || !nextCls) continue;
            if (!Grimoire::IsUserspacePointer(nextCls)) continue;

            uint32_t nextClsNameIdx = 0;
            if (!Macht::ReadSafe(nextCls + Grimoire::OFF_UOBJECT_NAME, nextClsNameIdx)) continue;
            std::string nextClsName = Serie::GetString(nextClsNameIdx);
            if (nextClsName.find("Property") == std::string::npos) continue;

            // Double-check: read the FName on the next field — must be a valid short name
            uint32_t nextNameIdx = 0;
            if (Macht::ReadSafe(nextPtr + nameOff, nextNameIdx) && nextNameIdx > 0) {
                std::string nextName = Serie::GetString(nextNameIdx);
                if (!nextName.empty() && nextName.length() <= 64) {
                    nextOff = off;
                    Sein::Info("DYNO", "ValidateAndFixOffsets: UField::Next at UObject+0x%02X "
                             "(probed, next='%s', class='%s')", off, nextName.c_str(), nextClsName.c_str());
                    break;
                }
            }
        }

        if (nextOff < 0) {
            // Fallback: use the default (may fail downstream but at least log it)
            nextOff = defaultNext;
            Sein::Warn("DYNO", "ValidateAndFixOffsets: UField::Next probe failed, falling back to +0x%02X", nextOff);
            // Same give-up as the FProperty arm above, in the UE4 UProperty arm — found by
            // grepping for siblings while fixing G1, which named only the FProperty site.
            unmeasured |= UNMEASURED_FFIELD_NEXT;
        }

        // Update the global offset if probing found a non-default value
        if (nextOff != DynOff::UFIELD_NEXT) {
            DynOff::UFIELD_NEXT = nextOff;
            Sein::Info("DYNO", "ValidateAndFixOffsets: Updated UFIELD_NEXT to +0x%02X (was +0x%02X)",
                     nextOff, defaultNext);
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
            Macht::ReadSafe(curField + nameOff, ni);
            fields[fieldCount].name = Serie::GetString(ni);
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
            Macht::ReadSafe(curField + nextOff, next);
            curField = next;
        } else {
            break;
        }
    }

    Sein::Info("DYNO", "ValidateAndFixOffsets: Collected %d fields from '%s' chain", fieldCount, testName);
    for (int i = 0; i < fieldCount; ++i) {
        Sein::Debug("DYNO", "  Field[%d]: '%s' at 0x%llX, expectedOff=%d",
                  i, fields[i].name.c_str(), (unsigned long long)fields[i].addr, fields[i].expectedOffset);
    }

    // Step 8: Probe for Offset_Internal: scan 4-byte aligned offsets
    // Range depends on mode: FProperty starts after FField header (~0x30-0x68),
    // UProperty starts after UField header (~0x28-0x70).
    // UProperty range extended to 0x70 for games with expanded UObject/UField
    // (e.g., DQ XI S has +0x10 extra bytes, shifting all UProperty fields).
    int probeStart = DynOff::bUseFProperty ? 0x30 : 0x28;
    int probeEnd   = DynOff::bUseFProperty ? 0x68 : 0x70;
    int propOffsetOff = -1;
    int propElemSizeOff = -1;

    for (int probe = probeStart; probe <= probeEnd; probe += 4) {
        int matches = 0;
        int sizeMatches = 0;

        for (int i = 0; i < fieldCount; ++i) {
            if (fields[i].expectedOffset < 0) continue;

            int32_t val = -1;
            if (Macht::ReadSafe(fields[i].addr + probe, val) && val == fields[i].expectedOffset) {
                ++matches;
            }

            int32_t sz = -1;
            if (Macht::ReadSafe(fields[i].addr + probe, sz) && sz == expectedElemSize) {
                ++sizeMatches;
            }
        }

        if (matches >= 2 && propOffsetOff < 0) {
            propOffsetOff = probe;
            Sein::Info("DYNO", "ValidateAndFixOffsets: Offset_Internal at +0x%02X (%d matches)", probe, matches);
        }

        if (sizeMatches >= 2 && propElemSizeOff < 0 && probe != propOffsetOff) {
            propElemSizeOff = probe;
            Sein::Info("DYNO", "ValidateAndFixOffsets: ElementSize at +0x%02X (%d matches)", probe, sizeMatches);
        }
    }

    if (propOffsetOff >= 0) {
        if (DynOff::bUseFProperty) {
            DynOff::FPROPERTY_OFFSET = propOffsetOff;
        } else {
            DynOff::UPROPERTY_OFFSET = propOffsetOff;
        }
    } else {
        Sein::Warn("DYNO", "ValidateAndFixOffsets: Cannot find Offset_Internal, keeping defaults");
        unmeasured |= UNMEASURED_PROP_OFFSET;
    }

    if (propElemSizeOff < 0 && propOffsetOff > 0) {
        // Heuristic: ElementSize sits 0x10 bytes before Offset_Internal.
        // Holds in BOTH known layouts — UE4.25-4.27 / UE5.0-5.1.0 (0x3C vs 0x4C) and
        // UE5.1.1+ (0x34 vs 0x44). The previous 0x14 landed on ArrayDim in both.
        int guess = propOffsetOff - 0x10;
        if (guess >= probeStart) {
            int32_t val = 0;
            if (Macht::ReadSafe(childProps + guess, val) && val == expectedElemSize) {
                propElemSizeOff = guess;
                Sein::Info("DYNO", "ValidateAndFixOffsets: ElementSize (heuristic) at +0x%02X", guess);
            }
        }
    }

    if (propElemSizeOff >= 0) {
        if (DynOff::bUseFProperty) {
            DynOff::FPROPERTY_ELEMSIZE = propElemSizeOff;
        } else {
            DynOff::UPROPERTY_ELEMSIZE = propElemSizeOff;
        }
    } else {
        // Checked AFTER the heuristic above, because that heuristic READS BACK the guessed
        // slot and requires it to equal expectedElemSize — a recovery that qualifies as a
        // measurement. Only a still-negative offset means the version guess survives.
        // This is the one that matters most: an ELEMSIZE landing on PropertyFlags is what
        // Ubel.cpp:4206 documents as a ~1 GiB per-element allocation (audit #5 U1).
        Sein::Warn("DYNO", "ValidateAndFixOffsets: Cannot find ElementSize, keeping default 0x%02X",
                 DynOff::bUseFProperty ? DynOff::FPROPERTY_ELEMSIZE : DynOff::UPROPERTY_ELEMSIZE);
        unmeasured |= UNMEASURED_PROP_ELEMSIZE;
    }

    // Step 9: Derive remaining offsets
    //
    // PropertyFlags follows ElementSize directly (no padding). UE source
    // layout (CoreUObject/Public/UObject/UnrealType.h on UE 4.25+):
    //
    //   class FProperty : public FField {
    //       int32 ArrayDim;        // +0x30
    //       int32 ElementSize;     // +0x34   <- propElemSizeOff
    //       EPropertyFlags Flags;  // +0x38   <- propElemSizeOff + 4
    //       uint16 RepIndex;       // +0x40
    //       int32 Offset_Internal; // +0x44
    //   };
    //
    // ArrayDim is BEFORE ElementSize, not between ElementSize and Flags --
    // the previous "+8" formula assumed the wrong order and read the high
    // 32 bits of the 64-bit Flags instead of the low. CPF_ReturnParm
    // (0x400), CPF_OutParm (0x100), and other parm flags all live in the
    // low 32 bits, so IsReturn / IsOut / IsParm detection silently
    // returned false for every UFunction param across every UE version.
    // This in turn made baked AA Scripts emit ReturnValue as an INPUT
    // param and the verify-mode print as "(void return)".
    if (DynOff::bUseFProperty) {
        if (propElemSizeOff >= 0) {
            DynOff::FPROPERTY_FLAGS = propElemSizeOff + 4;
        }
    } else {
        if (propElemSizeOff >= 0) {
            DynOff::UPROPERTY_FLAGS = propElemSizeOff + 4;
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

    Sein::Info("DYNO", "ValidateAndFixOffsets: UStruct::SuperStruct at +0x%02X", DynOff::USTRUCT_SUPER);

    // FStructProperty::Struct = Offset_Internal + 0x2C
    if (DynOff::bUseFProperty && propOffsetOff >= 0) {
        DynOff::FSTRUCTPROP_STRUCT  = propOffsetOff + 0x2C;
        DynOff::FARRAYPROP_INNER   = propOffsetOff + 0x2C;  // Same subclass extension offset
        DynOff::FBOOLPROP_FIELDSIZE = DynOff::FSTRUCTPROP_STRUCT;
        // FByteProperty::Enum is the first subclass field (== sizeof(FProperty), same place
        // as FStructProperty::Struct). FEnumProperty has FNumericProperty* UnderlyingProp
        // BEFORE its UEnum* Enum, so its Enum sits 8 bytes later (UE5.7.4 EnumProperty.h).
        DynOff::FBYTEPROP_ENUM     = DynOff::FSTRUCTPROP_STRUCT;
        DynOff::FENUMPROP_ENUM     = DynOff::FBYTEPROP_ENUM + 8;
    }

    // Infer tagged FFieldVariant from probed offsets:
    // FFieldVariant=0x08 (Next at 0x18) implies UE5.1.1+ layout.
    // UE5.3+ uses the tag-bit encoding. If version is unknown but layout matches,
    // enable tag-bit masking defensively — the StripFFieldTag is a no-op when bit is 0.
    if (DynOff::bUseFProperty && DynOff::FFIELD_NEXT == 0x18 && !DynOff::bTaggedFFieldVariant) {
        DynOff::bTaggedFFieldVariant = true;
        Sein::Info("DYNO", "ValidateAndFixOffsets: Inferred tagged FFieldVariant from FField::Next=0x18");
    }

    // A give-up recorded above means at least one DynOff value is a version GUESS, not a
    // measurement — so bOffsetsValidated must be false even though we reached the success
    // tail (audit #5 G1: it used to be stored true here unconditionally, three lines after
    // "keeping default"). Grimoire.h:243 is the contract; bOffsetsProbeRan stays true either
    // way, which is what FindGEngineSlot / ResolveGEngineDeferred actually gate on.
    //
    // The table is indexed by the bitmask so the reason can name EVERY probe that fell back,
    // not just the worst one. All 16 entries are string literals — g_offsetsFallbackReason is
    // a bare const char* read from other threads and must never point at a heap string.
    static const char* const kUnmeasuredReason[16] = {
        /* 0000 */ "",
        /* 0001 */ "unmeasured:ffield-name",
        /* 0010 */ "unmeasured:ffield-next",
        /* 0011 */ "unmeasured:ffield-name+ffield-next",
        /* 0100 */ "unmeasured:offset-internal",
        /* 0101 */ "unmeasured:ffield-name+offset-internal",
        /* 0110 */ "unmeasured:ffield-next+offset-internal",
        /* 0111 */ "unmeasured:ffield-name+ffield-next+offset-internal",
        /* 1000 */ "unmeasured:elemsize",
        /* 1001 */ "unmeasured:ffield-name+elemsize",
        /* 1010 */ "unmeasured:ffield-next+elemsize",
        /* 1011 */ "unmeasured:ffield-name+ffield-next+elemsize",
        /* 1100 */ "unmeasured:offset-internal+elemsize",
        /* 1101 */ "unmeasured:ffield-name+offset-internal+elemsize",
        /* 1110 */ "unmeasured:ffield-next+offset-internal+elemsize",
        /* 1111 */ "unmeasured:ffield-name+ffield-next+offset-internal+elemsize",
    };
    const bool allMeasured = (unmeasured == 0);
    DynOff::g_offsetsFallbackReason = kUnmeasuredReason[unmeasured & 0x0F];
    DynOff::bOffsetsProbeRan.store(true, std::memory_order_release);
    DynOff::bOffsetsValidated.store(allMeasured, std::memory_order_release);
    if (!allMeasured) {
        Sein::Warn("DYNO", "ValidateAndFixOffsets: PARTIAL — %s (validated=NO, offsets below "
                           "mix probed values with version defaults)",
                 DynOff::g_offsetsFallbackReason);
    }

    // Summary log
    Sein::Info("DYNO", "=== Dynamic Offset Summary ===");
    // Derived: UStruct::Script (Kismet bytecode TArray) is always PROPSSIZE + 8
    // (MinAlignment int32 sits between them). Inherits PROPSSIZE's calibration,
    // so shifted-layout games (Atomic Heart / Silent Hill F / Outer Worlds) are
    // handled with no extra probe. Used by Aura::FindPropertyXrefs.
    DynOff::USTRUCT_SCRIPT = DynOff::USTRUCT_PROPSSIZE + 0x08;

    Sein::Info("DYNO", "  CasePreservingName: %s", DynOff::bCasePreservingName ? "YES" : "no");
    Sein::Info("DYNO", "  TaggedFFieldVariant:%s", DynOff::bTaggedFFieldVariant ? " YES (UE5.3+)" : " no");
    Sein::Info("DYNO", "  UseFProperty:       %s", DynOff::bUseFProperty ? "yes (UE4.25+/UE5)" : "NO (UE4 UProperty)");
    Sein::Info("DYNO", "  UObject::Outer      = +0x%02X", DynOff::UOBJECT_OUTER);
    Sein::Info("DYNO", "  UStruct::Super      = +0x%02X", DynOff::USTRUCT_SUPER);
    Sein::Info("DYNO", "  UStruct::Children   = +0x%02X", DynOff::USTRUCT_CHILDREN);
    Sein::Info("DYNO", "  UStruct::ChildProps = +0x%02X", DynOff::USTRUCT_CHILDPROPS);
    Sein::Info("DYNO", "  UStruct::PropsSize  = +0x%02X", DynOff::USTRUCT_PROPSSIZE);
    Sein::Info("DYNO", "  UStruct::Script     = +0x%02X", DynOff::USTRUCT_SCRIPT);
    if (DynOff::bUseFProperty) {
        Sein::Info("DYNO", "  FField::Class       = +0x%02X", DynOff::FFIELD_CLASS);
        Sein::Info("DYNO", "  FField::Next        = +0x%02X", DynOff::FFIELD_NEXT);
        Sein::Info("DYNO", "  FField::Name        = +0x%02X", DynOff::FFIELD_NAME);
        Sein::Info("DYNO", "  FProperty::ElemSize = +0x%02X", DynOff::FPROPERTY_ELEMSIZE);
        Sein::Info("DYNO", "  FProperty::Flags    = +0x%02X", DynOff::FPROPERTY_FLAGS);
        Sein::Info("DYNO", "  FProperty::Offset   = +0x%02X", DynOff::FPROPERTY_OFFSET);
        Sein::Info("DYNO", "  FStructProp::Struct = +0x%02X", DynOff::FSTRUCTPROP_STRUCT);
    } else {
        Sein::Info("DYNO", "  UField::Next        = +0x%02X", DynOff::UFIELD_NEXT);
        Sein::Info("DYNO", "  UProperty::ElemSize = +0x%02X", DynOff::UPROPERTY_ELEMSIZE);
        Sein::Info("DYNO", "  UProperty::Flags    = +0x%02X", DynOff::UPROPERTY_FLAGS);
        Sein::Info("DYNO", "  UProperty::Offset   = +0x%02X", DynOff::UPROPERTY_OFFSET);
    }
    Sein::Info("DYNO", "==============================");

    return true;
}

// ============================================================
// CollectGObjectsCandidates — gather multiple validated GObjects candidates
// for the post-init decoy-recovery path (see Frieren::UE5_Init).  Reuses the
// proven data-section RIP-relative scan; the caller re-validates each via
// Aura::Init + name resolution to reject count-only decoys that pass the
// structural validator but contain no usable objects.
// ============================================================
size_t CollectGObjectsCandidates(std::vector<uintptr_t>& out, uintptr_t avoid, size_t maxCandidates) {
    size_t before = out.size();
    DataScanGObjectsCandidates(out, avoid, before + maxCandidates);
    return out.size() - before;
}

// ============================================================
// ExtraScanGObjects — Dumper-7-style .data heuristic
//
// Scan the .data section directly at 4-byte granularity and validate
// each location as a potential FUObjectArray base.  This complements
// FindGObjectsByDataScan (which follows code RIP-relative refs) by
// probing addresses that may not be referenced by any code instruction.
//
// Two modes per candidate address:
//   1. Inline:  the FUObjectArray struct lives at &addr itself
//   2. Pointer: addr holds a pointer to the FUObjectArray struct on the heap
//
// Thread-safe: reads game memory only, no global state mutation.
// ============================================================
uintptr_t ExtraScanGObjects() {
    Sein::Info("SCAN:GObj", "ExtraScanGObjects: Starting .data heuristic scan...");

    uintptr_t base = Macht::GetModuleBase(nullptr);
    if (!base) return 0;

    auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;
    auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + static_cast<DWORD>(dos->e_lfanew));
    if (nt->Signature != IMAGE_NT_SIGNATURE) return 0;

    // Collect ALL writable, non-exec sections (.data, .bss, etc.)
    struct SectionRange { uintptr_t start; uintptr_t end; };
    std::vector<SectionRange> dataSections;

    const IMAGE_SECTION_HEADER* section = IMAGE_FIRST_SECTION(nt);
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++section) {
        if (!section->Misc.VirtualSize || !section->VirtualAddress) continue;
        bool writable = (section->Characteristics & IMAGE_SCN_MEM_WRITE) != 0;
        bool exec     = (section->Characteristics & IMAGE_SCN_MEM_EXECUTE) != 0;
        if (writable && !exec) {
            dataSections.push_back({ base + section->VirtualAddress,
                                     base + section->VirtualAddress + section->Misc.VirtualSize });
        }
    }

    if (dataSections.empty()) {
        Sein::Warn("SCAN:GObj", "ExtraScanGObjects: No writable sections found");
        return 0;
    }

    int candidatesTested = 0;
    g_validationDbgCount = 0;  // Reset throttle for validators

    for (auto& sec : dataSections) {
        Sein::Debug("SCAN:GObj", "ExtraScanGObjects: Scanning [0x%llX-0x%llX] (%zu bytes)",
                  (unsigned long long)sec.start, (unsigned long long)sec.end,
                  (size_t)(sec.end - sec.start));

        for (uintptr_t addr = sec.start; addr + 0x20 < sec.end; addr += 4) {
            if ((addr & 0xFFF) == 0 && Tot::Requested()) {
                Sein::Warn("SCAN:GObj", "ExtraScanGObjects: cancelled (%d candidates tested)",
                           candidatesTested);
                return 0;   // (B18)
            }
            // Mode 1: Inline — the FUObjectArray struct starts at addr
            if (ValidateGObjects(addr)) {
                Sein::Info("SCAN:GObj", "ExtraScanGObjects: Found inline FUObjectArray at 0x%llX (%d candidates tested)",
                         (unsigned long long)addr, candidatesTested);
                return addr;
            }

            // Mode 2: Pointer — addr holds a pointer to FUObjectArray
            uintptr_t ptrVal = 0;
            if (Macht::ReadSafe(addr, ptrVal) && ptrVal && LooksLikeDataPtr(ptrVal)) {
                if (ValidateGObjects(ptrVal)) {
                    Sein::Info("SCAN:GObj", "ExtraScanGObjects: Found pointed FUObjectArray at 0x%llX (ptr@0x%llX, %d candidates tested)",
                             (unsigned long long)ptrVal, (unsigned long long)addr, candidatesTested);
                    return ptrVal;
                }
            }

            candidatesTested++;
            // Suppress excessive debug logging after first 50 failures
            if (candidatesTested == 50)
                g_validationDbgCount = kMaxValidationDbgLogs;
        }
    }

    Sein::Warn("SCAN:GObj", "ExtraScanGObjects: No valid FUObjectArray found (%d candidates tested)", candidatesTested);
    return 0;
}

// ============================================================
// ExtraScanGWorld — Dumper-7-style instance scan
//
// 1. Iterate GObjects to find a UWorld instance (by class name)
// 2. Scan .data sections for a static pointer holding that instance address
//
// Requires ObjectArray + FNamePool to be initialized.
// Thread-safe: reads game memory and existing ObjectArray/FNamePool statics
// (immutable after init), no global state mutation.
// ============================================================
// Find the byte offset of a reflected property by NAME within a UStruct (walks the
// FField property chain + super-struct chain). Uses the runtime-calibrated DynOff
// offsets, so it is version-independent (the NAMES — e.g. "OwningGameInstance" — are
// stable UE members; only their offsets vary). Returns -1 if not found.
// Walks the reflected property chain of a UStruct (and its supers) for a named property.
//
// TWO CHAINS, because UE4.25 moved properties off UObject:
//   FField era (4.25+)  — UStruct::ChildProperties -> FField::Next, name is FField::NamePrivate,
//                         offset is FProperty::Offset_Internal.
//   UProperty era (<4.25) — UStruct::Children -> UField::Next. A UProperty IS a UObject, so its
//                         name is UObjectBase::NamePrivate and the offset is
//                         UProperty::Offset_Internal.
//
// Grimoire.h says it outright: USTRUCT_CHILDPROPS is "absent in UE4 <4.25". This function used to
// walk only that one, so on every pre-4.25 title it read a non-existent member and returned -1 —
// which quietly disabled EVERY caller: ValidateGEngineSlot (so no GEngine AOB could ever
// validate), FindLiveGameEngine, and RecoverGWorldViaEngine. Seen on NEKOPALIVE 4.11, where
// GENG_X1/X3/X4 all hit the correct address and all three were rejected.
static int FindPropertyOffsetByName(uintptr_t structPtr, const char* propName) {
    const bool useFField = DynOff::bUseFProperty;
    const int  chainHead = useFField ? DynOff::USTRUCT_CHILDPROPS : DynOff::USTRUCT_CHILDREN;
    const int  nextOff   = useFField ? DynOff::FFIELD_NEXT        : DynOff::UFIELD_NEXT;
    const int  nameOff   = useFField ? DynOff::FFIELD_NAME        : Grimoire::OFF_UOBJECT_NAME;
    const int  valueOff  = useFField ? DynOff::FPROPERTY_OFFSET   : DynOff::UPROPERTY_OFFSET;

    int superGuard = 0;
    for (uintptr_t s = structPtr; s && superGuard++ < 64; ) {
        uintptr_t field = 0;
        if (Macht::ReadSafe(s + chainHead, field)) {
            int fieldGuard = 0;
            while (field && fieldGuard++ < 8192) {
                uint32_t nameIdx = 0;
                if (Macht::ReadSafe(field + nameOff, nameIdx) &&
                    Serie::GetString(nameIdx) == propName) {
                    int32_t off = 0;
                    return Macht::ReadSafe(field + valueOff, off) ? off : -1;
                }
                if (!Macht::ReadSafe(field + nextOff, field)) break;
            }
        }
        uintptr_t super = 0;
        if (!Macht::ReadSafe(s + DynOff::USTRUCT_SUPER, super) || super == s) break;
        s = super;
    }
    return -1;
}

uintptr_t ExtraScanGWorld() {
    Sein::Info("SCAN:GWld", "ExtraScanGWorld: Starting instance scan...");

    // Step 1: Find a UWorld instance in GObjects
    int32_t count = Aura::GetCount();
    if (count <= 0) {
        Sein::Warn("SCAN:GWld", "ExtraScanGWorld: ObjectArray not initialized (count=%d)", count);
        return 0;
    }

    // Step 1: collect ALL non-CDO UWorld instances (addr + GObjects index). Games like
    // Avowed have many UWorlds (UI "stage" worlds + the active map); the FIRST one is
    // usually a stage that no global points to. So instead of "pick a world then look for
    // a pointer to it", we scan .data for any slot that POINTS to one of these — that slot
    // IS a UWorld* global (the real GWorld; the decoy points to non-world garbage and is
    // skipped) — and prefer the highest-index world (most recently created = the active map).
    std::vector<std::pair<uintptr_t, int32_t>> worlds;   // (instance addr, index), sorted by addr
    for (int32_t i = 0; i < count; ++i) {
        if ((i & 0xFFF) == 0 && Tot::Requested()) return 0;   // (B18)
        uintptr_t obj = Aura::GetByIndex(i);
        if (!obj) continue;
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;
        uint32_t clsNameIdx = 0;
        if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, clsNameIdx)) continue;
        if (Serie::GetString(clsNameIdx) != "World") continue;
        uint32_t nameIdx = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx)) continue;
        std::string name = Serie::GetString(nameIdx);
        if (name.empty() || name.find("Default__") != std::string::npos) continue;
        worlds.emplace_back(obj, i);
    }
    if (worlds.empty()) {
        Sein::Warn("SCAN:GWld", "ExtraScanGWorld: No UWorld instance found in GObjects (scanned %d objects)", count);
        return 0;
    }
    std::sort(worlds.begin(), worlds.end());   // by addr, for binary search
    Sein::Info("SCAN:GWld", "ExtraScanGWorld: %zu non-CDO UWorld instance(s) — scanning .data for a static pointer",
             worlds.size());

    // Step 2: scan writable .data for an 8-byte slot whose value is one of those worlds.
    uintptr_t base = Macht::GetModuleBase(nullptr);
    if (!base) return 0;
    auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;
    auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + static_cast<DWORD>(dos->e_lfanew));
    if (nt->Signature != IMAGE_NT_SIGNATURE) return 0;

    struct Match { int32_t idx; uintptr_t slot; uintptr_t world; };
    std::vector<Match> matches;
    const IMAGE_SECTION_HEADER* section = IMAGE_FIRST_SECTION(nt);
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++section) {
        if (!section->Misc.VirtualSize || !section->VirtualAddress) continue;
        bool writable = (section->Characteristics & IMAGE_SCN_MEM_WRITE) != 0;
        bool exec     = (section->Characteristics & IMAGE_SCN_MEM_EXECUTE) != 0;
        if (!writable || exec) continue;

        uintptr_t secBase = base + section->VirtualAddress;
        size_t    secSize = section->Misc.VirtualSize;
        for (size_t off = 0; off + sizeof(uintptr_t) <= secSize; off += sizeof(uintptr_t)) {
            uintptr_t val = 0;
            if ((off & 0xFFF) == 0 && Tot::Requested()) return 0;   // (B18)
            if (!Macht::ReadSafe(secBase + off, val) || val < 0x10000) continue;
            auto it = std::lower_bound(worlds.begin(), worlds.end(), std::make_pair(val, 0));
            if (it != worlds.end() && it->first == val) {
                matches.push_back({ it->second, secBase + off, val });
            }
        }
    }

    if (matches.empty()) {
        Sein::Warn("SCAN:GWld", "ExtraScanGWorld: No static pointer to any UWorld instance found in .data sections");
        return 0;
    }

    // Pick the .data-referenced world that is the ACTIVE game world. World-Partition games
    // (Avowed) spawn many transient "_Generated_" sub-worlds — higher GObjects index, but
    // OwningGameInstance == 0. Only the real game world has a non-null OwningGameInstance.
    // So: by index DESCENDING, take the first match whose OwningGameInstance is non-null;
    // if that property can't be located or none qualifies, fall back to the highest index.
    std::sort(matches.begin(), matches.end(),
              [](const Match& a, const Match& b) { return a.idx > b.idx; });

    int ogiOff = -1;
    {
        uintptr_t cls = 0;
        if (Macht::ReadSafe(matches[0].world + Grimoire::OFF_UOBJECT_CLASS, cls) && cls)
            ogiOff = FindPropertyOffsetByName(cls, "OwningGameInstance");
    }

    const Match* chosen = nullptr;
    const char* how = "highest-index";
    if (ogiOff >= 0) {
        for (const Match& m : matches) {
            uintptr_t ogi = 0;
            if (Macht::ReadSafe(m.world + ogiOff, ogi) && ogi >= 0x10000) {
                chosen = &m;
                how = "active (OwningGameInstance set)";
                break;
            }
        }
    }
    if (!chosen) chosen = &matches[0];   // fallback: highest index

    Sein::Info("SCAN:GWld", "ExtraScanGWorld: GWorld at 0x%llX -> UWorld 0x%llX (index=%d, %zu candidate(s), %s)",
             (unsigned long long)chosen->slot, (unsigned long long)chosen->world,
             chosen->idx, matches.size(), how);
    return chosen->slot;
}

// ============================================================
// RecoverGWorldViaEngine — gap-fill when ExtraScanGWorld finds no static slot.
//
// Some titles keep no static .data pointer to the live UWorld in the main module
// (the world pointer lives only behind the engine's runtime objects, or in a
// separately-loaded engine DLL). ExtraScanGWorld then returns 0. Instead of a
// static module slot we return the address of the engine's LIVE "current world"
// pointer field: GEngine -> GameViewport -> &World. UGameViewportClient::World is
// updated by the engine on level transitions, so a single deref of this slot
// always yields the current UWorld — exactly how every GWorld consumer reads it
// (DerefWorld() / ReadSafe(GWorld, uworld)).
//
// GEngine is identified by a reflected member, not a class name: the (sole) live
// non-CDO object whose class exposes a "GameViewport" object property with a
// non-null value. Class names vary (UGameEngine or a game subclass), but the
// member name is a stable UE member; FindPropertyOffsetByName makes the offset
// version-independent.
// ============================================================
// Find the live GEngine object: the (sole) non-CDO whose class exposes a
// "GameViewport" object property with a non-null value. Class names vary
// (UGameEngine or a game subclass), but the member is a stable UE member, so
// FindPropertyOffsetByName makes the offset version-independent. Walks GObjects
// once, resolving the GameViewport offset PER DISTINCT CLASS (cached) so
// non-engine classes are walked at most once (cached -1 = skip).
// Optionally outputs the GameViewport pointer + its offset. Returns 0 if none.
// Shared by RecoverGWorldViaEngine (GWorld recovery) and FindGameEngine
// (Live Walker "Start from GameEngine").
// &GEngine as resolved by FindGEngineSlot during FindAll. 0 = not resolved.
// Declared here (rather than next to FindGEngineSlot) because BOTH engine consumers
// below it — FindLiveGameEngine and, through it, RecoverGWorldViaEngine — take the
// fast path off it.
static uintptr_t s_gengineSlot = 0;

static uintptr_t FindLiveGameEngine(uintptr_t* outViewport, int* outGvOff) {
    if (outViewport) *outViewport = 0;
    if (outGvOff) *outGvOff = -1;

    // Fast path: &GEngine came from an AOB, so the live engine is one deref away and we
    // can skip walking the whole object pool. The slot is restart-stable but its VALUE is
    // not, so re-read and re-check the reflected member every call rather than caching it.
    if (s_gengineSlot) {
        uintptr_t engine = 0;
        if (Macht::ReadSafe(s_gengineSlot, engine) && Grimoire::IsUserspacePointer(engine)) {
            uintptr_t cls = 0;
            if (Macht::ReadSafe(engine + Grimoire::OFF_UOBJECT_CLASS, cls) && cls) {
                int off = FindPropertyOffsetByName(cls, "GameViewport");
                if (off >= 0) {
                    uintptr_t vp = 0;
                    Macht::ReadSafe(engine + static_cast<uintptr_t>(off), vp);
                    if (vp) {
                        if (outViewport) *outViewport = vp;
                        if (outGvOff) *outGvOff = off;
                        return engine;
                    }
                }
            }
        }
        // Slot present but the engine is not live yet (early boot / between maps) —
        // fall through to the pool walk rather than reporting "no engine".
    }

    int32_t count = Aura::GetCount();
    if (count <= 0) return 0;

    std::unordered_map<uintptr_t, int> gvOffByClass;
    for (int32_t i = 0; i < count; ++i) {
        uintptr_t obj = Aura::GetByIndex(i);
        if (!obj) continue;
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) continue;

        int off;
        auto it = gvOffByClass.find(cls);
        if (it != gvOffByClass.end()) {
            off = it->second;
        } else {
            off = FindPropertyOffsetByName(cls, "GameViewport");
            gvOffByClass.emplace(cls, off);
        }
        if (off < 0) continue;                       // not a UEngine-derived class

        // Skip CDOs (Default__GameEngine has the property too but a null viewport).
        uint32_t nameIdx = 0;
        if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx)) {
            std::string nm = Serie::GetString(nameIdx);
            if (nm.find("Default__") != std::string::npos) continue;
        }

        uintptr_t vp = 0;
        if (Macht::ReadSafe(obj + off, vp) && vp >= 0x10000) {
            if (outViewport) *outViewport = vp;
            if (outGvOff) *outGvOff = off;
            return obj;                               // first live engine wins
        }
    }
    return 0;
}

uintptr_t RecoverGWorldViaEngine() {
    Sein::Info("SCAN:GWld", "RecoverGWorldViaEngine: no static slot — trying GEngine->GameViewport->World...");

    int32_t count = Aura::GetCount();
    if (count <= 0) {
        Sein::Warn("SCAN:GWld", "RecoverGWorldViaEngine: ObjectArray not initialized (count=%d)", count);
        return 0;
    }

    // Step 1: find the live GEngine + its GameViewport (shared helper).
    uintptr_t viewport = 0;
    int gvOff = -1;
    uintptr_t engine = FindLiveGameEngine(&viewport, &gvOff);
    if (!engine || !viewport) {
        Sein::Warn("SCAN:GWld", "RecoverGWorldViaEngine: no live GEngine with a non-null GameViewport found");
        return 0;
    }
    Sein::Info("SCAN:GWld", "RecoverGWorldViaEngine: GEngine=0x%llX GameViewport=0x%llX (GameViewport off=0x%X)",
               (unsigned long long)engine, (unsigned long long)viewport, gvOff);

    // Helper: does *slot deref to a UObject whose class FName is "World"?
    auto derefsToWorld = [](uintptr_t slot) -> bool {
        uintptr_t w = 0;
        if (!Macht::ReadSafe(slot, w) || w < 0x10000) return false;
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(w + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) return false;
        uint32_t cn = 0;
        return Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, cn) && Serie::GetString(cn) == "World";
    };

    uintptr_t vpCls = 0;
    if (!Macht::ReadSafe(viewport + Grimoire::OFF_UOBJECT_CLASS, vpCls) || !vpCls) {
        Sein::Warn("SCAN:GWld", "RecoverGWorldViaEngine: GameViewport has no readable class");
        return 0;
    }

    // Step 2: prefer the reflected "World" property on the viewport.
    int worldOff = FindPropertyOffsetByName(vpCls, "World");
    if (worldOff >= 0 && derefsToWorld(viewport + (uintptr_t)worldOff)) {
        uintptr_t slot = viewport + (uintptr_t)worldOff;
        Sein::Info("SCAN:GWld", "RecoverGWorldViaEngine: World reflected at off=0x%X -> slot=0x%llX",
                   worldOff, (unsigned long long)slot);
        return slot;
    }

    // Step 2b: UGameViewportClient::World is a private member in many builds (not reflected).
    // Probe the viewport object's pointer-aligned fields for one that points at a class-"World"
    // object. Bound the scan to the class PropertiesSize (fallback cap if unreadable).
    int32_t vpSize = 0;
    Macht::ReadSafe(vpCls + DynOff::USTRUCT_PROPSSIZE, vpSize);
    if (vpSize <= 0 || vpSize > 0x4000) vpSize = 0x800;
    for (int32_t off = 0; off + (int32_t)sizeof(uintptr_t) <= vpSize; off += (int32_t)sizeof(uintptr_t)) {
        if (derefsToWorld(viewport + (uintptr_t)off)) {
            uintptr_t slot = viewport + (uintptr_t)off;
            Sein::Info("SCAN:GWld", "RecoverGWorldViaEngine: World probed at off=0x%X -> slot=0x%llX",
                       off, (unsigned long long)slot);
            return slot;
        }
    }

    Sein::Warn("SCAN:GWld", "RecoverGWorldViaEngine: GameViewport has no UWorld* field (size=0x%X)", vpSize);
    return 0;
}

// ============================================================
// FindGEngineSlot — resolve &GEngine (the static slot) by AOB.
//
// Ordering contract: this MUST run after GObjects + GNames + offset validation,
// because ValidateGEngineSlot derefs the candidate and asks the reflected class for a
// "GameViewport" property. That is deliberate — it is the same version-independent test
// FindLiveGameEngine uses, and it is what stops a decoy .data global from being accepted
// (unlike the SparseDelegates validator, which can only range-check two ints).
//
// Why the SLOT and not just the object: FindLiveGameEngine walks the entire GObjects pool
// resolving a property offset per class. With the slot that becomes one deref. More
// importantly a slot is restart-stable, so a CE symbol registered against it auto-follows
// engine recreation — the same property that makes the GWorld AOB worth having.
// ============================================================
static ScanReport  s_gengineReport;
static const char* s_gengineMethod = "not_found";

static bool ValidateGEngineSlot(uintptr_t slotAddr) {
    if (!slotAddr) return false;

    uintptr_t engine = 0;
    if (!Macht::ReadSafe(slotAddr, engine)) return false;
    if (!Grimoire::IsUserspacePointer(engine)) return false;

    // A live UEngine is a heap UObject: its vtable must point into a loaded module.
    uintptr_t vt = 0;
    if (!Macht::ReadSafe(engine, vt) || !Grimoire::IsUserspacePointer(vt)) return false;

    uintptr_t cls = 0;
    if (!Macht::ReadSafe(engine + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) return false;
    if (!Grimoire::IsUserspacePointer(cls)) return false;

    // The discriminator: only UEngine and its subclasses expose a "GameViewport"
    // object property. Class NAMES vary (UGameEngine or a game subclass), the member
    // does not.
    int gvOff = FindPropertyOffsetByName(cls, "GameViewport");
    if (gvOff < 0) return false;

    // A non-null viewport additionally proves this is the ACTIVE engine rather than a
    // CDO. Do not require it: FindAll can legitimately run before the viewport exists.
    uintptr_t viewport = 0;
    Macht::ReadSafe(engine + static_cast<uintptr_t>(gvOff), viewport);

    Sein::Info("SCAN:Eng", "ValidateGEngineSlot: slot=0x%llX -> engine=0x%llX gvOff=0x%X viewport=0x%llX",
               static_cast<unsigned long long>(slotAddr),
               static_cast<unsigned long long>(engine), gvOff,
               static_cast<unsigned long long>(viewport));
    return true;
}

// Copy the GEngine scan metadata out of s_gengineReport. Shared by FindAll and
// ResolveGEngineDeferred so the deferred re-run publishes exactly what the eager path would —
// otherwise a deferred win would resolve the address but leave the CE-export AOB triple empty.
static void PublishGEngineMetadata(EnginePointers& out) {
    out.gengineMethod    = s_gengineMethod;
    out.genginePatternId = s_gengineReport.winningId;
    out.gengineScanAddr  = s_gengineReport.scanAddr;
    out.gengineAob    = nullptr;
    out.gengineAobPos = 0;
    out.gengineAobLen = 0;
    // Same replayability gate as the GWorld triple above — a symbol/call-follow winner
    // is a perfectly good way for US to find &GEngine, and a useless thing to hand CE.
    if (auto* es = s_gengineReport.winningSig; es && IsCeReplayableAob(es->resolve)) {
        out.gengineAob    = es->pattern;
        out.gengineAobPos = es->instrOffset + es->opcodeLen;
        out.gengineAobLen = es->instrOffset + es->totalLen;
    }
}

uintptr_t FindGEngineSlot() {
    s_gengineReport = ScanReport{};
    s_gengineReport.targetName = "GEngine";
    s_gengineMethod = "not_found";

    // ValidateGEngineSlot asks the reflected class for a "GameViewport" property. That needs
    // BOTH the dynamic FField/UStruct offsets (DynOff) and a live FNamePool (Serie::GetString),
    // and neither exists yet while FindAll is running — ValidateAndFixOffsets and the FNamePool
    // init both happen AFTER it. So every candidate is rejected and the ~0.2-0.7 s AVX2 pass is
    // pure waste.
    //
    // This is not hypothetical. It made "&GEngine — AOB not found" the answer on EVERY game
    // while the patterns themselves were fine: on Everspace 2 (UE 5.5) 14 of 15 candidates
    // resolved to 0x7FF68ACC37B0, which is exactly the PDB's &GEngine (image VA 0x149DA37B0),
    // and all 14 were thrown away here. Bail out cheaply instead, and let
    // ResolveGEngineDeferred re-run the scan once the offsets land.
    // Gate on PROBE-RAN, not on VALIDATED: this exists only to order us after offset
    // detection. Using the strict flag would regress &GEngine on every build where
    // detection gives up on defaults — e.g. UE 5.8 before the FFieldClass::Name fix,
    // where the give-up path was the only reason the slot resolved at all.
    if (!DynOff::bOffsetsProbeRan.load(std::memory_order_acquire)) {
        s_gengineMethod = "deferred";
        Sein::Info("SCAN:Eng", "FindGEngineSlot: deferred — dynamic offsets not validated yet "
                               "(re-runs after ValidateAndFixOffsets)");
        return 0;
    }

    uintptr_t result = ScanForTarget(
        Sig::GENGINE_PATTERNS, std::size(Sig::GENGINE_PATTERNS),
        ValidateGEngineSlot, s_gengineReport, /*tryMultiModule=*/true,
        /*hintPatternId=*/nullptr);

    LogScanReport(s_gengineReport);

    if (result) {
        s_gengineMethod = ScanMethodFor(s_gengineReport);
        Sein::Info("SCAN:Eng", "FindGEngineSlot: &GEngine = 0x%llX",
                   static_cast<unsigned long long>(result));
    } else {
        Sein::Warn("SCAN:Eng", "FindGEngineSlot: no pattern validated "
                               "(non-critical — FindGameEngine falls back to the GObjects walk)");
    }
    return result;
}

uintptr_t ResolveGEngineDeferred(EnginePointers& out) {
    if (out.GEngine) return out.GEngine;   // already resolved eagerly — nothing to redo

    // Offset detection can itself fail (ValidateAndFixOffsets returned false). Say so rather
    // than emitting FindGEngineSlot's "re-runs after ValidateAndFixOffsets" a second time,
    // which would point at a step that has already been and gone.
    if (!DynOff::bOffsetsProbeRan.load(std::memory_order_acquire)) {
        s_gengineMethod = "not_found";
        Sein::Warn("SCAN:Eng", "ResolveGEngineDeferred: offsets never validated — cannot "
                               "reflect 'GameViewport', so &GEngine stays unresolved");
        PublishGEngineMetadata(out);
        return 0;
    }

    uintptr_t slot = FindGEngineSlot();
    s_gengineSlot = slot;
    out.GEngine   = slot;
    PublishGEngineMetadata(out);
    return slot;
}

// Resolve the live GEngine object for the Live Walker "Start from GameEngine"
// root + validate its standard pointer members. Detection is by reflected
// member (GameViewport), NOT by class name, so it works across UE versions and
// game-specific UGameEngine subclasses. A meaningful (non-null) GameInstance is
// the extra "this is the active engine" signal the UI surfaces.
GameEngineInfo FindGameEngine() {
    GameEngineInfo info;

    uintptr_t viewport = 0;
    // FindLiveGameEngine already takes the &GEngine fast path when the AOB resolved it.
    uintptr_t engine = FindLiveGameEngine(&viewport, nullptr);
    if (!engine) {
        Sein::Warn("SCAN:Eng", "FindGameEngine: no live GEngine with a non-null GameViewport found");
        return info;
    }
    info.engineAddr     = engine;
    info.gameViewportOk = (viewport != 0);

    uintptr_t cls = 0;
    if (Macht::ReadSafe(engine + Grimoire::OFF_UOBJECT_CLASS, cls) && cls) {
        info.classAddr = cls;
        uint32_t cn = 0;
        if (Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, cn))
            info.className = Serie::GetString(cn);

        // Validate a meaningful GameInstance pointer (reflected member, non-null).
        int giOff = FindPropertyOffsetByName(cls, "GameInstance");
        if (giOff >= 0) {
            uintptr_t gi = 0;
            if (Macht::ReadSafe(engine + static_cast<uintptr_t>(giOff), gi) && gi >= 0x10000)
                info.gameInstanceOk = true;
        }
    }

    Sein::Info("SCAN:Eng", "FindGameEngine: GEngine=0x%llX class='%s' viewportOk=%d instanceOk=%d",
               (unsigned long long)engine, info.className.c_str(),
               info.gameViewportOk ? 1 : 0, info.gameInstanceOk ? 1 : 0);
    return info;
}

bool FindAll(EnginePointers& out, ScanProgressFn progress) {
    LOG_INFO("FindAll: Starting global pointer scan...");

    // Compute PE hash early — needed for hint cache lookup
    ComputePEHash(out.peHash, sizeof(out.peHash));
    LOG_INFO("FindAll: PE hash = %s", out.peHash);

    // Load hint cache: previously-winning pattern IDs for this game version
    auto hints = Flamme::LoadHints(out.peHash);

    // UE version detection — priority order:
    //   1. User-set persistent override (from HintCache, set via UI) — always wins.
    //   2. Cached detection from a previous scan whose logic rev matches the current DLL.
    //   3. Fresh detection (PE VERSIONINFO + Tier 1/2 string scan + Tier 3 anchored fallback).
    //   4. Hard fallback (504 by default, or publisher-biased if we can identify the shipper).
    //
    // DetectVersion() costs ~0.35 s on a 482 MB image since the needle gate (audit #5 G2);
    // it was ~29 s before, so this cache used to be load-bearing and is now merely nice.
    // (Measured on Elliot-Win64-Shipping.exe, 460.0 MiB, MSVC /O2 x64 — the conditions are
    // part of the number.) The version is NOT used during
    // AOB scanning (completely version-agnostic), only post-scan for DynOff offset selection.
    // Structural findings (UE4 TNameEntryArray, hash-prefixed headers) override the version
    // later anyway, so a cached value is safe.
    //
    // Cache-reuse policy (build 1521+): trust the cached version for ANY same-peHash game —
    // including publisher-stripped ones (SquareEnix) and inferred/low-confidence results —
    // as long as it was stamped by the current detection-logic rev (kVersionDetectLogicRev).
    // DetectVersionDetailed scans the module's STATIC image, so the same binary always yields
    // the same answer; re-running it every launch only burns time. The cached versionDetected /
    // lowConfidence flags are restored verbatim, so the UI badge (and the "set an override"
    // nudge for stripped/forked engines) stays exactly as honest as a fresh detection. A logic
    // change → bump kVersionDetectLogicRev → every cache re-detects once and re-stamps.
    if (progress) progress(1, "Detecting UE version...");

    // Probe publisher (cheap, just PE info) regardless of cached state — useful for UI display
    // and as a tiebreaker when detection runs.
    const PublisherEntry* publisher = DetectPublisherFromPE();
    out.publisherThumbprint = publisher ? publisher->thumbprint : nullptr;

    // Apply per-game GameThreadDispatch invoke-timeout override (if user set one).
    // Done early so any UFunction call placed before scan completion uses the right
    // timeout. Default (Stark::kDefaultInvokeTimeoutMs) was already in effect; this
    // bumps it if the JSON cache has a value.
    if (hints.hasInvokeTimeoutOverride && hints.invokeTimeoutMs > 0) {
        Stark::SetInvokeTimeoutMs(hints.invokeTimeoutMs);
        LOG_INFO("FindAll: Applied invoke timeout override: %dms", hints.invokeTimeoutMs);
    }

    if (hints.hasUserOverride && hints.userOverrideVersion != 0) {
        out.UEVersion = hints.userOverrideVersion;
        out.bVersionDetected = true;     // user is the source of truth — surface as confident
        out.bUserOverride    = true;
        out.bLowConfidence   = false;
        LOG_INFO("FindAll: UE Version = %u (USER OVERRIDE — persistent for this game)",
                 out.UEVersion);
    } else if (hints.hasVersionHint && hints.ueVersion != 0
               && hints.versionDetectRev == kVersionDetectLogicRev) {
        // Reuse the cached version (skip the slow memory string scan) whenever it was stamped
        // by the current detection-logic rev — regardless of publisher or confidence. The same
        // binary produces the same detection deterministically, so a re-scan only wastes time.
        // Restore the cached confidence so the UI badge / override nudge survive (versionDetected
        // verbatim; lowConfidence cached OR live-publisher, see below).
        out.UEVersion        = hints.ueVersion;
        out.bVersionDetected = hints.versionDetected;
        out.bUserOverride    = false;
        // Restore the cached low-confidence flag, but ALSO force it live whenever a publisher
        // thumbprint matched — DetectPublisherFromPE ran above (cheap, PE-only) and a publisher
        // match means unreliable version strings, so the badge must always nudge toward an override.
        // Applying it live (not just from cache) keeps the badge honest even if the publisher table
        // gains a new shipper after this game was already cached (the old publisher gate was live too).
        //
        // The >= MIN_SUPPORTED guard mirrors the identical rule in the fresh-detection branch
        // below, and it MUST be here too: this is the branch that runs on every launch after the
        // first, so guarding only the fresh path would gate a pre-UE4 game correctly once and
        // then silently un-gate it from launch 2 onward. Publisher bias exists to flag an
        // UNRELIABLE version STRING; it has nothing to say about a version we refused outright.
        out.bLowConfidence   = hints.lowConfidence
                            || (publisher != nullptr
                                && out.UEVersion >= Grimoire::MIN_SUPPORTED_UE_VERSION);
        LOG_INFO("FindAll: UE Version = %u (cached, rev=%u, detected=%s, lowConf=%s) — skipped DetectVersion",
                 out.UEVersion, hints.versionDetectRev,
                 out.bVersionDetected ? "yes" : "no",
                 out.bLowConfidence   ? "yes" : "no");
    } else {
        if (hints.hasVersionHint && hints.ueVersion != 0
            && hints.versionDetectRev != kVersionDetectLogicRev) {
            LOG_INFO("FindAll: Cached version %u was stamped by logic rev %u (current %u) — "
                     "re-detecting once and re-stamping.",
                     hints.ueVersion, hints.versionDetectRev, kVersionDetectLogicRev);
        }

        VersionScanResult dr = DetectVersionDetailed();
        out.UEVersion        = dr.version;
        out.bVersionDetected = (dr.version != 0);
        out.bUserOverride    = false;
        // Tier 3 hits are not zero — they are real string matches — but the bare-pattern
        // path is far more fragile than Tier 1/2, so flag them as low-confidence.
        out.bLowConfidence   = (dr.tier == 3);

        if (out.UEVersion == 0) {
            // Detection failed completely. Bias the fallback by publisher when possible.
            if (publisher) {
                out.UEVersion = publisher->biasFallback;
                out.bLowConfidence = true;
                LOG_WARN("FindAll: UE detection failed — using publisher (%s) bias fallback %u",
                         publisher->thumbprint, out.UEVersion);
            } else {
                out.UEVersion = 504;
                out.bLowConfidence = true;
                LOG_WARN("FindAll: UE version detection failed — using default %u",
                         out.UEVersion);
            }
        }

        // Final pass: a publisher thumbprint match unconditionally flags low confidence.
        // We have direct evidence the publisher ships unreliable version strings (SquareEnix),
        // so the badge must always nudge the user toward setting an override — even when
        // detection produced a clean Tier 1 / Tier 2 hit, since those strings can come from
        // bundled SDKs (PhysX, etc.) rather than the actual engine.
        //
        // Scoped to versions at or above the support floor. Below it the version is not a
        // fragile guess to be second-guessed, it is a refusal — and because the too-old gate
        // requires !bLowConfidence, an unconditional rule here would let a thumbprinted
        // publisher's pre-UE4 title defeat the gate. No behavioural change for any supported
        // game: the needle table floors at 4.18 and the PE-resource path at 400+minor, so the
        // only ways to land below 411 are the 4.0-4.10 resource branch and the UE3 sentinel.
        if (publisher && out.UEVersion >= Grimoire::MIN_SUPPORTED_UE_VERSION) {
            out.bLowConfidence = true;
        }

        LOG_INFO("FindAll: UE Version = %u (tier=%d, detected=%s, lowConfidence=%s, publisher=%s)",
                 out.UEVersion, dr.tier,
                 out.bVersionDetected ? "yes" : "no",
                 out.bLowConfidence   ? "yes" : "no",
                 out.publisherThumbprint ? out.publisherThumbprint : "-");
    }

    // ── Too-old gate ────────────────────────────────────────────────────────
    // Two distinct refusals share this one exit, distinguished by the version number alone:
    //
    //  (a) UE 4.0-4.10 — the right engine family, a version too old. Pre-4.11 predates
    //      FUObjectItem entirely: ObjObjects is a TStaticIndirectArrayThreadSafeRead of raw
    //      UObjectBase* (stride 8) with an INLINE chunk table, which ArrayLayout cannot express
    //      at all. Verified against Epic's source: 4.10.2 has no FUObjectItem; 4.11.0 introduces
    //      it (16 bytes, ClusterAndFlags packed). Reachable ONLY via DetectVersionFromPEResource's
    //      major==4 branch — the memory needle table floors at "4.18." and can never go below it,
    //      which is worth knowing before assuming this path is exercised often. (It is not: no
    //      title in the local 35-game corpus reports a 4.0-4.10 PE version. The 4.10 evidence is
    //      the two reference builds in the AOB corpus, see Himmel.h's provenance block.)
    //
    //  (b) UEVersion == PRE_UE4_SENTINEL_VERSION — positively identified as pre-UE4 (UE3) by
    //      CountPreUE4Markers. A different object model, not an older version of this one: no
    //      FUObjectArray, no FNamePool, and UObject::Class/Name nowhere near the offsets the
    //      validators hardcode. Before this case existed such a game fell through the gate
    //      entirely (its PE version is a GAME version like 1.0.x, so detection returned 0 and
    //      the fallback guessed 5.4 — above the floor), and paid a full 150-pattern sweep to
    //      reach "no winner" plus a "set an override" nudge that could never help.
    //
    // Deliberately gated on a CONFIDENT detection only. A low-confidence or publisher-biased
    // version is a guess, and a user override is the user's call — misreading a working game as
    // "too old" and refusing to scan it is a far worse failure than wasting four seconds.
    //
    // That gate is only as good as what counts as confident, and case (a) used to slip through:
    // DetectVersionFromPEResource's `major == 4` branch returned tier 1 off a single
    // uncorroborated VS_FIXEDFILEINFO field, so bLowConfidence was false and this refusal armed
    // on one PE value. It no longer short-circuits below the floor — see the note in
    // DetectVersionDetailed — so reaching here with case (a) now means the memory scan agreed, or
    // at least did not contradict it with a tier 1/2 hit. Case (b) is unchanged: the pre-UE4
    // sentinel is a POSITIVE 2-of-4 marker identification and is deliberately tier 1. (B25)
    if (out.UEVersion < Grimoire::MIN_SUPPORTED_UE_VERSION
        && out.bVersionDetected && !out.bLowConfidence && !out.bUserOverride) {
        out.bVersionTooOld = true;
        if (out.UEVersion == Grimoire::PRE_UE4_SENTINEL_VERSION) {
            LOG_WARN("FindAll: PRE-UE4 engine (Unreal Engine 3) — SKIPPING the scan. There is no "
                     "FUObjectArray, no FUObjectItem and no FNamePool in this binary, and its "
                     "UObject layout puts Class/Name nowhere near the offsets the validators "
                     "require, so no pattern, preset, override or Extra Scan can resolve "
                     "GObjects. This is a refusal by design, not a scan failure.");
        } else {
            LOG_WARN("FindAll: UE %u is older than the minimum supported %u — SKIPPING the scan. "
                     "Pre-4.11 has no FUObjectItem (raw UObjectBase* in an inline chunk table), so "
                     "no pattern or preset can resolve GObjects. Set a UE version override if this "
                     "detection is wrong.",
                     out.UEVersion, Grimoire::MIN_SUPPORTED_UE_VERSION);
        }
        return true;   // not a failure — a clean, explained no-op
    }

    if (progress) progress(2, "Scanning GObjects...");
    out.GObjects = FindGObjects(hints.gobjectsPatternId.empty() ? nullptr
                                : hints.gobjectsPatternId.c_str());
    if (!out.GObjects) {
        LOG_WARN("FindAll: Failed to find GObjects (will continue — Extra Scan may recover)");
    }

    if (progress) progress(3, "Scanning GNames...");
    out.GNames = FindGNames(hints.gnamesPatternId.empty() ? nullptr
                            : hints.gnamesPatternId.c_str());
    if (!out.GNames) {
        LOG_WARN("FindAll: Failed to find GNames (will continue — Extra Scan may recover)");
    }

    // Propagate UE4 TNameEntryArray detection state
    out.bUE4NameArray = g_isUE4NameArray;
    out.ue4StringOffset = g_ue4NameStringOffset;
    out.fnameEntryHeaderOffset = g_fnameEntryHeaderOffset;
    out.nameChunksOffset       = g_nameChunksOffset;
    out.namePayloadGap         = g_namePayloadGap;
    out.nameKeyTableCtx        = g_nameKeyTableCtx;

    // Propagate scan method info (how each pointer was found)
    out.gobjectsMethod = s_gobjectsMethod;
    out.gnamesMethod   = s_gnamesMethod;

    // --- Version inference from detection flags ---
    // Only apply if GNames was actually found (these flags are set during GNames scan)
    if (out.GNames) {
        if (out.bUE4NameArray && out.UEVersion >= 500) {
            LOG_WARN("FindAll: UE4 TNameEntryArray detected but version=%u (>= 500). "
                     "Overriding to 422 (UE4 pre-4.23)", out.UEVersion);
            out.UEVersion = 422;
        } else if (out.fnameEntryHeaderOffset == 4 && out.UEVersion >= 500) {
            LOG_WARN("FindAll: Hash-prefixed FNameEntry (hdrOff=4) suggests UE4.26 fork, "
                     "but version=%u. Overriding to 426", out.UEVersion);
            out.UEVersion = 426;
        }
    }

    // &GEngine — resolved BEFORE GWorld on purpose. It needs GObjects/GNames/offsets
    // (its validator asks the reflected class for "GameViewport"), and having it lets
    // GWorld's recovery path deref the engine instead of walking the whole object pool.
    if (progress) progress(4, "Scanning GEngine...");
    out.GEngine   = FindGEngineSlot();
    s_gengineSlot = out.GEngine;
    out.gengineMethod = s_gengineMethod;

    if (progress) progress(4, "Scanning GWorld...");
    out.GWorld = FindGWorld(hints.gworldPatternId.empty() ? nullptr
                            : hints.gworldPatternId.c_str());
    out.gworldMethod = s_gworldMethod;
    // GWorld is non-critical, just log

    // FSparseDelegateStorage::SparseDelegates — UE 4.23+ (sparse delegates were
    // introduced in 4.23). Was gated at >= 500 on the mistaken belief that 4.23-4.27 keyed
    // the outer map by FObjectKey; the DropIn 4.27.2 PDB shows a raw UObjectBase* key, and
    // SPARSE_ES2_1 resolves correctly on that build. Aura's walker now probes the live key
    // shape, so a genuinely FObjectKey-keyed build declines to walk instead of misreading.
    // Eagerly scanned here (instead of lazy-on-first-drill-down) so the Pointer panel can
    // display it. Cache is the same one used by Aura::WalkSparseDelegateBindings,
    // so first drill-down is O(1).
    if (out.UEVersion >= 423) {
        if (progress) progress(4, "Scanning FSparseDelegateStorage...");
        out.SparseDelegates = FindSparseDelegateStorage();
        out.sparseDelegatesMethod = s_sparseDelegatesMethod;
    }

    // --- AOB Usage Tracking: propagate scan stats ---
    out.gobjectsPatternId        = s_gobjectsReport.winningId;
    out.gnamesPatternId          = s_gnamesReport.winningId;
    out.gworldPatternId          = s_gworldReport.winningId;
    out.sparseDelegatesPatternId = s_sparseReport.winningId;
    out.gobjectsScanAddr         = s_gobjectsReport.scanAddr;
    out.gnamesScanAddr           = s_gnamesReport.scanAddr;
    out.gworldScanAddr           = s_gworldReport.scanAddr;
    out.sparseDelegatesScanAddr  = s_sparseReport.scanAddr;

    ExtractScanStats(s_gobjectsReport, out.gobjectsPatternsTried, out.gobjectsPatternsHit);
    ExtractScanStats(s_gnamesReport,   out.gnamesPatternsTried,   out.gnamesPatternsHit);
    ExtractScanStats(s_gworldReport,   out.gworldPatternsTried,   out.gworldPatternsHit);

    // GWorld winning pattern AOB metadata (for CE symbol registration via CreateSymbolScript).
    // Published ONLY when the winner is a form CE can actually replay — see
    // IsCeReplayableAob. A SymbolExport winner (e.g. Satisfactory, which resolves
    // GWorld through ?GWorld@@3VUWorldProxy@@A) stores a mangled NAME here, and shipping it
    // as if it were a byte pattern makes the UI's "AOB available" checkbox light up for an
    // export in which every address then resolves to `??`.
    if (auto* ws = s_gworldReport.winningSig; ws && IsCeReplayableAob(ws->resolve)) {
        out.gworldAob    = ws->pattern;
        out.gworldAobPos = ws->instrOffset + ws->opcodeLen;
        out.gworldAobLen = ws->instrOffset + ws->totalLen;
    }
    // Same triple for GEngine, so a GameEngine-rooted CE export can be AOB-wrapped
    // exactly like a GWorld-rooted one instead of baking in a stale UEngine* snapshot.
    // Shared with ResolveGEngineDeferred — GEngine is normally resolved THERE, not here,
    // because its validator needs offsets that do not exist yet at this point.
    PublishGEngineMetadata(out);

    LOG_INFO("FindAll: Complete — GObjects=0x%llX (%s), GNames=0x%llX (%s), GWorld=0x%llX (%s), Sparse=0x%llX (%s), GEngine=0x%llX (%s), UE=%u, UE4Names=%s, hdrOff=%d",
             static_cast<unsigned long long>(out.GObjects), out.gobjectsMethod,
             static_cast<unsigned long long>(out.GNames), out.gnamesMethod,
             static_cast<unsigned long long>(out.GWorld), out.gworldMethod,
             static_cast<unsigned long long>(out.SparseDelegates), out.sparseDelegatesMethod,
             static_cast<unsigned long long>(out.GEngine), out.gengineMethod,
             out.UEVersion,
             out.bUE4NameArray ? "yes" : "no",
             out.fnameEntryHeaderOffset);

    // Save scan results to hint cache for future acceleration
    {
        wchar_t exePathW[MAX_PATH] = {};
        GetModuleFileNameW(nullptr, exePathW, MAX_PATH);
        // Extract just the filename
        const wchar_t* slash = wcsrchr(exePathW, L'\\');
        const wchar_t* nameW = slash ? slash + 1 : exePathW;
        // Convert to UTF-8
        int sz = WideCharToMultiByte(CP_UTF8, 0, nameW, -1, nullptr, 0, nullptr, nullptr);
        std::string processName(sz > 0 ? sz - 1 : 0, '\0');
        if (sz > 0)
            WideCharToMultiByte(CP_UTF8, 0, nameW, -1, processName.data(), sz, nullptr, nullptr);
        Flamme::SaveResults(out.peHash, out, processName.c_str());
    }

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
    if (!Macht::ReadSafe(addr, idx)) return "";
    return Serie::GetString(idx);
}

// Read UObject::ClassPrivate name (compact helper, avoids UStructWalker dep)
static std::string GetObjectClassName(uintptr_t obj) {
    if (!obj) return "";
    uintptr_t cls = 0;
    Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_CLASS, cls);
    if (!cls) return "";
    return ReadFNameStr(cls + Grimoire::OFF_UOBJECT_NAME);
}

// Read UObject::NamePrivate as string
static std::string GetObjectName(uintptr_t obj) {
    if (!obj) return "";
    return ReadFNameStr(obj + Grimoire::OFF_UOBJECT_NAME);
}

bool DetectUEnumNames() {
    // Already detected?
    if (DynOff::bUEnumNamesDetected.load(std::memory_order_acquire))
        return true;

    Sein::Info("DYNO:Enum", "DetectUEnumNames: Searching for known enums in GObjects...");

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

        Aura::ForEach([&](int32_t /*idx*/, uintptr_t obj) -> bool {
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
            Sein::Debug("DYNO:Enum", "  '%s' not found in GObjects", cand.name);
            continue;
        }

        Sein::Info("DYNO:Enum", "  Found '%s' at 0x%llX, probing for Names offset...",
            cand.name, static_cast<unsigned long long>(enumAddr));

        // Probe offsets 0x30..0x120 (step 8). At each, try BOTH the legacy
        // TArray<TPair<FName,int64>> layout AND the UE5.6+ FNameData
        // struct-of-arrays (Neu disambiguates which one parses) — version-number
        // gating alone is unreliable on forked engines, so we validate by reading
        // the actual member FNames (same as before, format-agnostic now).
        auto readMem = [](uintptr_t a, void* o, size_t n) -> bool {
            return Macht::ReadBytesSafe(a, o, n);
        };
        const int fnameStride = DynOff::bCasePreservingName ? 0x10 : 0x08;

        for (int off = 0x30; off <= 0x120; off += 8) {
            Neu::EnumNamesLayout layout;
            if (!Neu::DetectLayout(readMem, enumAddr + off, fnameStride, 16384, layout))
                continue;

            // Validate count range
            if (layout.count < cand.minCount || layout.count > cand.maxCount) continue;

            // Read first few members and check FNames resolve to expected substrings.
            int verified = 0;
            const int32_t toCheck = (std::min)(layout.count, 5);
            for (int32_t i = 0; i < toCheck; ++i) {
                int32_t nameIdx = 0;
                int64_t val = 0;
                if (!Neu::ReadEntry(readMem, layout, i, nameIdx, val)) break;

                std::string entryName = Serie::GetString(nameIdx);
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
                DynOff::bEnumNamesNewContainer =
                    (layout.format == Neu::EnumNamesFormat::FNameData57);
                DynOff::bUEnumNamesDetected.store(true, std::memory_order_release);

                Sein::Info("DYNO:Enum", "  UEnum::Names detected at UEnum+0x%02X "
                    "(%s, verified with '%s', count=%d, %d name matches)",
                    off, DynOff::bEnumNamesNewContainer ? "UE5.6+ FNameData"
                                                        : "legacy TArray",
                    cand.name, layout.count, verified);
                return true;
            }
        }

        Sein::Debug("DYNO:Enum", "  '%s' found but no valid Names offset detected", cand.name);
    }

    // Mark as failed to prevent retry storm.
    // Without this, every EnumProperty/ByteProperty field triggers a full GObjects scan
    // (~0.85s each), causing 25-45 second delays for classes with many enum fields.
    DynOff::bUEnumNamesFailed.store(true, std::memory_order_release);
    DynOff::bUEnumNamesDetected.store(true, std::memory_order_release);

    Sein::Warn("DYNO:Enum", "DetectUEnumNames: FAILED — no known enums found or validated "
        "(will not retry; enum names will show as raw values)");
    return false;
}

} // namespace Genau
