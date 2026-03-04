// ============================================================
// Memory.cpp — AOBScan, module base, RIP-relative resolution
// ============================================================

#include "Memory.h"
#define LOG_CAT "MEM"
#include "Logger.h"

#include <Psapi.h>
#include <vector>
#include <string>
#include <cstring>
#include <immintrin.h>  // AVX2 intrinsics for SIMD pattern scanning

namespace Mem {

// ============================================================
// Basic read / write helpers
// ============================================================

bool ReadBytesSafe(uintptr_t addr, void* buf, size_t size) {
    __try {
        memcpy(buf, reinterpret_cast<const void*>(addr), size);
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        memset(buf, 0, size);
        return false;
    }
}

bool WriteBytes(uintptr_t addr, const void* buf, size_t size) {
    DWORD oldProtect = 0;
    if (!VirtualProtect(reinterpret_cast<void*>(addr), size, PAGE_READWRITE, &oldProtect))
        return false;
    __try {
        memcpy(reinterpret_cast<void*>(addr), buf, size);
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        VirtualProtect(reinterpret_cast<void*>(addr), size, oldProtect, &oldProtect);
        return false;
    }
    VirtualProtect(reinterpret_cast<void*>(addr), size, oldProtect, &oldProtect);
    return true;
}

// ============================================================
// Module helpers
// ============================================================

uintptr_t GetModuleBase(const wchar_t* moduleName) {
    HMODULE hModule = moduleName ? GetModuleHandleW(moduleName)
                                 : GetModuleHandleW(nullptr);
    return reinterpret_cast<uintptr_t>(hModule);
}

size_t GetModuleSize(const wchar_t* moduleName) {
    HMODULE hModule = moduleName ? GetModuleHandleW(moduleName)
                                 : GetModuleHandleW(nullptr);
    if (!hModule) return 0;

    MODULEINFO info{};
    if (GetModuleInformation(GetCurrentProcess(), hModule, &info, sizeof(info)))
        return info.SizeOfImage;
    return 0;
}

// ============================================================
// AOBScan internals
// ============================================================

// ParsedPattern struct is declared in Memory.h (public).

bool ParsePattern(const char* patStr, ParsedPattern& out) {
    out.bytes.clear();
    out.mask.clear();

    const char* p = patStr;
    while (*p) {
        while (*p == ' ' || *p == '\t') ++p;
        if (!p[0] || !p[1]) break; // Need at least 2 chars for a hex byte or ??

        if (p[0] == '?' && p[1] == '?') {
            out.bytes.push_back(0);
            out.mask.push_back(0); // wildcard
            p += 2;
        } else {
            char hex[3] = { p[0], p[1], 0 };
            out.bytes.push_back(static_cast<uint8_t>(strtoul(hex, nullptr, 16)));
            out.mask.push_back(1); // literal
            p += 2;
        }
    }

    if (out.bytes.empty()) return false;
    out.firstByte    = out.bytes[0];
    out.firstIsFixed = (out.mask[0] != 0);

    // Find anchor: first non-wildcard byte for AVX2 SIMD acceleration.
    // Unlike memchr (always byte 0), the anchor can be at any position,
    // enabling fast-skip for wildcard-prefixed patterns like "?? ?? 48 8B".
    out.anchorOffset = -1;
    for (size_t i = 0; i < out.mask.size(); ++i) {
        if (out.mask[i]) {
            out.anchorOffset = static_cast<int>(i);
            out.anchorByte   = out.bytes[i];
            break;
        }
    }
    return true;
}

// Enumerate executable PE sections of the given module base.
// Returns list of (base, size) pairs, covering only .text-like sections.
// This lets AOBScan skip .rdata / .data / .rsrc etc. (often 40-60% of image).
static std::vector<std::pair<uintptr_t, size_t>>
GetExecutableSections(uintptr_t moduleBase)
{
    std::vector<std::pair<uintptr_t, size_t>> result;
    if (!moduleBase) return result;

    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(moduleBase);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return result;

    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS64*>(
        moduleBase + static_cast<LONG>(dos->e_lfanew));
    if (nt->Signature != IMAGE_NT_SIGNATURE) return result;

    IMAGE_SECTION_HEADER* section = IMAGE_FIRST_SECTION(nt);
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++section) {
        constexpr DWORD EXEC_FLAGS = IMAGE_SCN_CNT_CODE | IMAGE_SCN_MEM_EXECUTE;
        if (!(section->Characteristics & EXEC_FLAGS)) continue;
        if (!section->Misc.VirtualSize || !section->VirtualAddress) continue;

        result.emplace_back(
            moduleBase + section->VirtualAddress,
            static_cast<size_t>(section->Misc.VirtualSize));
    }
    return result;
}

// ── Scalar fallback ──────────────────────────────────────────────────────
// Used when the pattern has no non-wildcard byte (all ??), or when
// the region is too small for a single AVX2 load.
static const uint8_t* ScanRegionScalar(
    const uint8_t*       scanStart,
    size_t               count,       // number of start positions to check
    const ParsedPattern& pat)
{
    const size_t patLen = pat.bytes.size();
    for (size_t i = 0; i < count; ++i) {
        bool matched = true;
        for (size_t j = 0; j < patLen; ++j) {
            if (pat.mask[j] && scanStart[i + j] != pat.bytes[j]) {
                matched = false;
                break;
            }
        }
        if (matched) return scanStart + i;
    }
    return nullptr;
}

// ── AVX2 SIMD pattern scan ──────────────────────────────────────────────
// Compares 32 bytes at a time against the anchor byte using AVX2 intrinsics.
// For each SIMD match, falls back to scalar verification of the full pattern.
//
// Key improvements over the previous memchr approach:
//  1. Anchor at ANY position  — handles wildcard-prefixed patterns
//     (e.g., "?? ?? 48 8B 05") that memchr(byte[0]) cannot accelerate.
//  2. Batch processing        — finds ALL anchor matches in a 32-byte
//     window at once via movemask, reducing loop overhead.
//  3. Early exit              — inner verification breaks at first mismatch.
static const uint8_t* ScanRegion(
    const uint8_t*      scanStart,
    size_t              regionSize,
    const ParsedPattern& pat)
{
    const size_t patLen = pat.bytes.size();
    if (regionSize < patLen) return nullptr;

    const size_t maxStart = regionSize - patLen; // last valid pattern-start offset

    // All-wildcard pattern — no anchor byte to search for
    if (pat.anchorOffset < 0) {
        return ScanRegionScalar(scanStart, maxStart + 1, pat);
    }

    const int anchorOff = pat.anchorOffset;
    const __m256i needle = _mm256_set1_epi8(static_cast<char>(pat.anchorByte));

    // ── SIMD phase: 32 bytes per iteration at the anchor offset ──────────
    size_t i = 0;
    while (i + anchorOff + 32 <= regionSize) {
        __m256i chunk = _mm256_loadu_si256(
            reinterpret_cast<const __m256i*>(scanStart + i + anchorOff));
        __m256i cmp  = _mm256_cmpeq_epi8(chunk, needle);
        uint32_t bits = static_cast<uint32_t>(_mm256_movemask_epi8(cmp));

        while (bits) {
            unsigned long bitPos;
            _BitScanForward(&bitPos, bits);
            const size_t candidate = i + bitPos;

            if (candidate <= maxStart) {
                const uint8_t* p = scanStart + candidate;
                bool matched = true;
                for (size_t j = 0; j < patLen; ++j) {
                    if (pat.mask[j] && p[j] != pat.bytes[j]) {
                        matched = false;
                        break;
                    }
                }
                if (matched) return p;
            }

            bits &= bits - 1; // clear lowest set bit
        }
        i += 32;
    }

    // ── Scalar tail: positions where a full 32-byte SIMD load won't fit ──
    if (i <= maxStart) {
        const uint8_t* tailHit = ScanRegionScalar(
            scanStart + i, maxStart - i + 1, pat);
        if (tailHit) return tailHit;
    }

    return nullptr;
}

// ============================================================
// Public: AOBScan
// ============================================================

uintptr_t AOBScan(const char* pattern, uintptr_t start, size_t size) {
    ParsedPattern pat;
    if (!ParsePattern(pattern, pat)) {
        LOG_ERROR("AOBScan: Failed to parse pattern [%s]", pattern);
        return 0;
    }

    // ── Explicit range supplied ────────────────────────────────────────────
    if (start != 0 || size != 0) {
        if (start == 0) start = GetModuleBase(nullptr);
        if (size  == 0) size  = GetModuleSize(nullptr);

        LOG_DEBUG("AOBScan: Explicit range — %zu bytes at 0x%llX [%s]",
                  size, static_cast<unsigned long long>(start), pattern);

        const uint8_t* hit = ScanRegion(
            reinterpret_cast<const uint8_t*>(start), size, pat);

        if (hit) {
            uintptr_t res = reinterpret_cast<uintptr_t>(hit);
            LOG_INFO("AOBScan: Found at 0x%llX", static_cast<unsigned long long>(res));
            return res;
        }
        LOG_WARN("AOBScan: Pattern not found [%s]", pattern);
        return 0;
    }

    // ── Default: scan only executable sections of the main module ─────────
    // Skipping .rdata / .data / .rsrc / .pdata reduces scan size significantly
    // (often from ~100 MB down to ~30-50 MB for a typical UE5 game).
    uintptr_t moduleBase = GetModuleBase(nullptr);
    if (!moduleBase) {
        LOG_ERROR("AOBScan: Cannot get module base");
        return 0;
    }

    auto sections = GetExecutableSections(moduleBase);

    if (sections.empty()) {
        // Fallback: no PE section info — scan the whole image
        size_t moduleSize = GetModuleSize(nullptr);
        LOG_WARN("AOBScan: No executable sections found — full-module fallback (%zu bytes)", moduleSize);
        const uint8_t* hit = ScanRegion(
            reinterpret_cast<const uint8_t*>(moduleBase), moduleSize, pat);
        if (hit) {
            uintptr_t res = reinterpret_cast<uintptr_t>(hit);
            LOG_INFO("AOBScan: Found at 0x%llX (full-module fallback)", static_cast<unsigned long long>(res));
            return res;
        }
        LOG_WARN("AOBScan: Pattern not found [%s]", pattern);
        return 0;
    }

    // Log total executable bytes being scanned (useful for perf diagnosis)
    size_t totalExecBytes = 0;
    for (auto& [b, s] : sections) totalExecBytes += s;
    LOG_DEBUG("AOBScan: Scanning %zu exec bytes across %zu section(s) [%s]",
              totalExecBytes, sections.size(), pattern);

    for (auto& [secBase, secSize] : sections) {
        const uint8_t* hit = ScanRegion(
            reinterpret_cast<const uint8_t*>(secBase), secSize, pat);
        if (hit) {
            uintptr_t res = reinterpret_cast<uintptr_t>(hit);
            LOG_INFO("AOBScan: Found at 0x%llX", static_cast<unsigned long long>(res));
            return res;
        }
    }

    LOG_WARN("AOBScan: Pattern not found [%s] (searched %zu exec bytes)",
             pattern, totalExecBytes);
    return 0;
}

// ============================================================
// Public: ResolveRIP
// ============================================================

uintptr_t ResolveRIP(uintptr_t instrAddr, int opcodeLen, int totalLen) {
    // target = (instrAddr + totalLen) + *(int32_t*)(instrAddr + opcodeLen)
    int32_t rel32 = 0;
    if (!ReadSafe<int32_t>(instrAddr + opcodeLen, rel32)) {
        LOG_ERROR("ResolveRIP: Failed to read rel32 at 0x%llX",
                  static_cast<unsigned long long>(instrAddr + opcodeLen));
        return 0;
    }

    uintptr_t target = instrAddr + totalLen + rel32;
    LOG_DEBUG("ResolveRIP: 0x%llX + %d + %d = 0x%llX",
              static_cast<unsigned long long>(instrAddr),
              totalLen, rel32,
              static_cast<unsigned long long>(target));
    return target;
}

// ============================================================
// Internal: ScanRegionAll — collect ALL matches in a region
// ============================================================
// Same SIMD logic as ScanRegion, but accumulates every match
// instead of returning on the first one.

static void ScanRegionAll(
    const uint8_t*       scanStart,
    size_t               regionSize,
    const ParsedPattern& pat,
    std::vector<uintptr_t>& results)
{
    const size_t patLen = pat.bytes.size();
    if (regionSize < patLen) return;

    const size_t maxStart = regionSize - patLen;

    // All-wildcard pattern — no anchor byte to search for
    if (pat.anchorOffset < 0) {
        for (size_t i = 0; i <= maxStart; ++i) {
            bool matched = true;
            for (size_t j = 0; j < patLen; ++j) {
                if (pat.mask[j] && scanStart[i + j] != pat.bytes[j]) {
                    matched = false;
                    break;
                }
            }
            if (matched) results.push_back(reinterpret_cast<uintptr_t>(scanStart + i));
        }
        return;
    }

    const int anchorOff = pat.anchorOffset;
    const __m256i needle = _mm256_set1_epi8(static_cast<char>(pat.anchorByte));

    // ── SIMD phase ──
    size_t i = 0;
    while (i + anchorOff + 32 <= regionSize) {
        __m256i chunk = _mm256_loadu_si256(
            reinterpret_cast<const __m256i*>(scanStart + i + anchorOff));
        __m256i cmp  = _mm256_cmpeq_epi8(chunk, needle);
        uint32_t bits = static_cast<uint32_t>(_mm256_movemask_epi8(cmp));

        while (bits) {
            unsigned long bitPos;
            _BitScanForward(&bitPos, bits);
            const size_t candidate = i + bitPos;

            if (candidate <= maxStart) {
                const uint8_t* p = scanStart + candidate;
                bool matched = true;
                for (size_t j = 0; j < patLen; ++j) {
                    if (pat.mask[j] && p[j] != pat.bytes[j]) {
                        matched = false;
                        break;
                    }
                }
                if (matched) results.push_back(reinterpret_cast<uintptr_t>(p));
            }

            bits &= bits - 1;
        }
        i += 32;
    }

    // ── Scalar tail ──
    for (; i <= maxStart; ++i) {
        bool matched = true;
        for (size_t j = 0; j < patLen; ++j) {
            if (pat.mask[j] && scanStart[i + j] != pat.bytes[j]) {
                matched = false;
                break;
            }
        }
        if (matched) results.push_back(reinterpret_cast<uintptr_t>(scanStart + i));
    }
}

// ============================================================
// Public: AOBScanAll
// ============================================================

std::vector<uintptr_t> AOBScanAll(const char* pattern, uintptr_t moduleBase) {
    std::vector<uintptr_t> results;

    ParsedPattern pat;
    if (!ParsePattern(pattern, pat)) {
        LOG_ERROR("AOBScanAll: Failed to parse pattern [%s]", pattern);
        return results;
    }

    if (!moduleBase) moduleBase = GetModuleBase(nullptr);
    if (!moduleBase) {
        LOG_ERROR("AOBScanAll: Cannot get module base");
        return results;
    }

    auto sections = GetExecutableSections(moduleBase);
    if (sections.empty()) {
        // Fallback: scan whole module
        MODULEINFO mi{};
        if (GetModuleInformation(GetCurrentProcess(),
                                 reinterpret_cast<HMODULE>(moduleBase), &mi, sizeof(mi))) {
            ScanRegionAll(reinterpret_cast<const uint8_t*>(moduleBase),
                          mi.SizeOfImage, pat, results);
        }
    } else {
        for (auto& [secBase, secSize] : sections) {
            ScanRegionAll(reinterpret_cast<const uint8_t*>(secBase), secSize, pat, results);
        }
    }

    LOG_DEBUG("AOBScanAll: %zu matches for [%.40s%s]",
              results.size(), pattern, (strlen(pattern) > 40 ? "..." : ""));
    return results;
}

// ============================================================
// Public: GetLoadedModules
// ============================================================

std::vector<ModuleInfo> GetLoadedModules() {
    std::vector<ModuleInfo> out;

    HMODULE modules[1024];
    DWORD cbNeeded = 0;
    if (!EnumProcessModules(GetCurrentProcess(), modules, sizeof(modules), &cbNeeded))
        return out;

    DWORD count = static_cast<DWORD>(
        (std::min)(static_cast<size_t>(cbNeeded / sizeof(HMODULE)), _countof(modules)));

    for (DWORD i = 0; i < count; ++i) {
        ModuleInfo mi{};
        mi.hModule = modules[i];
        mi.base = reinterpret_cast<uintptr_t>(modules[i]);

        MODULEINFO info{};
        if (GetModuleInformation(GetCurrentProcess(), modules[i], &info, sizeof(info)))
            mi.size = info.SizeOfImage;

        GetModuleFileNameW(modules[i], mi.name, MAX_PATH);
        out.push_back(mi);
    }

    return out;
}

// ============================================================
// Public: AOBScanAllModules
// ============================================================

std::vector<uintptr_t> AOBScanAllModules(const char* pattern) {
    std::vector<uintptr_t> allResults;

    // Scan main module first (most likely to contain the match)
    uintptr_t mainBase = GetModuleBase(nullptr);
    auto mainHits = AOBScanAll(pattern, mainBase);
    allResults.insert(allResults.end(), mainHits.begin(), mainHits.end());

    // Scan other loaded modules (UE modular builds: Engine, Core DLLs, etc.)
    auto modules = GetLoadedModules();
    for (auto& m : modules) {
        if (m.base == mainBase) continue;  // already scanned
        if (m.size < 0x10000) continue;    // skip tiny modules (< 64KB)

        auto hits = AOBScanAll(pattern, m.base);
        if (!hits.empty()) {
            LOG_DEBUG("AOBScanAllModules: %zu matches in '%ls'",
                      hits.size(), m.name);
            allResults.insert(allResults.end(), hits.begin(), hits.end());
        }
    }

    return allResults;
}

// ============================================================
// Internal: ScanRegionBatch — multi-anchor AVX2 scan
// ============================================================
// Scans a single memory region for N patterns simultaneously.
// Each pattern uses its own anchor byte/offset for SIMD comparison.
// One _mm256_cmpeq_epi8 per pattern per 32-byte stride, sharing
// the single memory pass across all patterns.

struct BatchEntry {
    ParsedPattern parsed;
    int           resultIndex;  // index into the results vector
};

static void ScanRegionBatch(
    const uint8_t*                scanStart,
    size_t                        regionSize,
    const std::vector<BatchEntry>& entries,
    std::vector<std::vector<uintptr_t>>& results)
{
    if (entries.empty()) return;

    // Find max pattern length for maxStart calculation
    size_t maxPatLen = 0;
    for (const auto& e : entries)
        if (e.parsed.bytes.size() > maxPatLen) maxPatLen = e.parsed.bytes.size();
    if (regionSize < maxPatLen) return;

    const size_t maxStart = regionSize - maxPatLen;

    // Separate SIMD-capable patterns from all-wildcard (scalar-only) patterns
    struct SIMDEntry {
        __m256i needle;
        int     anchorOffset;
        size_t  patLen;
        int     entryIdx;     // index into entries[]
    };

    std::vector<SIMDEntry> simdEntries;
    std::vector<int> scalarOnlyIndices;

    for (int k = 0; k < static_cast<int>(entries.size()); ++k) {
        const auto& pat = entries[k].parsed;
        if (pat.anchorOffset < 0) {
            scalarOnlyIndices.push_back(k);
            continue;
        }
        SIMDEntry se;
        se.needle       = _mm256_set1_epi8(static_cast<char>(pat.anchorByte));
        se.anchorOffset = pat.anchorOffset;
        se.patLen       = pat.bytes.size();
        se.entryIdx     = k;
        simdEntries.push_back(se);
    }

    // ── SIMD phase: 32-byte stride, all patterns per stride ────────
    size_t i = 0;
    for (; i + 32 <= regionSize; i += 32) {
        for (const auto& se : simdEntries) {
            // Check bounds: anchor offset must fit within the loaded window
            if (i + se.anchorOffset + 32 > regionSize) continue;

            __m256i chunk = _mm256_loadu_si256(
                reinterpret_cast<const __m256i*>(scanStart + i + se.anchorOffset));
            __m256i cmp  = _mm256_cmpeq_epi8(chunk, se.needle);
            uint32_t bits = static_cast<uint32_t>(_mm256_movemask_epi8(cmp));

            while (bits) {
                unsigned long bitPos;
                _BitScanForward(&bitPos, bits);
                const size_t candidate = i + bitPos;

                if (candidate <= maxStart) {
                    // Scalar verify full pattern
                    const auto& pat = entries[se.entryIdx].parsed;
                    const uint8_t* p = scanStart + candidate;
                    bool matched = true;
                    for (size_t j = 0; j < pat.bytes.size(); ++j) {
                        if (pat.mask[j] && p[j] != pat.bytes[j]) {
                            matched = false;
                            break;
                        }
                    }
                    if (matched) {
                        results[entries[se.entryIdx].resultIndex].push_back(
                            reinterpret_cast<uintptr_t>(p));
                    }
                }

                bits &= bits - 1;
            }
        }
    }

    // ── Scalar tail for SIMD patterns (last < 32 bytes) ──────────
    for (const auto& se : simdEntries) {
        const auto& pat = entries[se.entryIdx].parsed;
        for (size_t pos = i; pos <= maxStart; ++pos) {
            bool matched = true;
            for (size_t j = 0; j < pat.bytes.size(); ++j) {
                if (pat.mask[j] && scanStart[pos + j] != pat.bytes[j]) {
                    matched = false;
                    break;
                }
            }
            if (matched) {
                results[entries[se.entryIdx].resultIndex].push_back(
                    reinterpret_cast<uintptr_t>(scanStart + pos));
            }
        }
    }

    // ── Full scalar scan for all-wildcard patterns ───────────────
    for (int k : scalarOnlyIndices) {
        const auto& pat = entries[k].parsed;
        for (size_t pos = 0; pos <= maxStart; ++pos) {
            bool matched = true;
            for (size_t j = 0; j < pat.bytes.size(); ++j) {
                if (pat.mask[j] && scanStart[pos + j] != pat.bytes[j]) {
                    matched = false;
                    break;
                }
            }
            if (matched) {
                results[entries[k].resultIndex].push_back(
                    reinterpret_cast<uintptr_t>(scanStart + pos));
            }
        }
    }
}

// ============================================================
// Public: AOBScanBatch — batch scan N patterns in one pass
// ============================================================

std::vector<BatchScanResult> AOBScanBatch(
    const char* const* patterns,
    const int*         indices,
    int                count,
    uintptr_t          moduleBase)
{
    std::vector<BatchScanResult> results(count);
    for (int k = 0; k < count; ++k) {
        results[k].patternIndex = indices[k];
    }

    if (count <= 0) return results;

    // Parse all patterns
    std::vector<BatchEntry> entries;
    entries.reserve(count);
    int validCount = 0;
    for (int k = 0; k < count; ++k) {
        BatchEntry be;
        be.resultIndex = k;
        if (!ParsePattern(patterns[k], be.parsed)) {
            LOG_ERROR("AOBScanBatch: Failed to parse pattern[%d] [%s]", k, patterns[k]);
            continue;
        }
        entries.push_back(std::move(be));
        ++validCount;
    }

    if (entries.empty()) return results;

    // Get module and executable sections
    if (!moduleBase) moduleBase = GetModuleBase(nullptr);
    if (!moduleBase) {
        LOG_ERROR("AOBScanBatch: Cannot get module base");
        return results;
    }

    auto sections = GetExecutableSections(moduleBase);

    // Intermediate per-pattern match lists (indexed by result position)
    std::vector<std::vector<uintptr_t>> matchLists(count);

    if (sections.empty()) {
        // Fallback: scan whole module
        MODULEINFO mi{};
        if (GetModuleInformation(GetCurrentProcess(),
                                 reinterpret_cast<HMODULE>(moduleBase), &mi, sizeof(mi))) {
            ScanRegionBatch(reinterpret_cast<const uint8_t*>(moduleBase),
                            mi.SizeOfImage, entries, matchLists);
        }
    } else {
        // Log total exec bytes for diagnostics
        size_t totalExecBytes = 0;
        for (auto& [b, s] : sections) totalExecBytes += s;
        LOG_DEBUG("AOBScanBatch: %d patterns, %zu exec bytes across %zu section(s)",
                  validCount, totalExecBytes, sections.size());

        for (auto& [secBase, secSize] : sections) {
            ScanRegionBatch(reinterpret_cast<const uint8_t*>(secBase),
                            secSize, entries, matchLists);
        }
    }

    // Transfer matches into results
    for (int k = 0; k < count; ++k) {
        results[k].matches = std::move(matchLists[k]);
    }

    LOG_DEBUG("AOBScanBatch: completed (%d patterns)", validCount);
    return results;
}

} // namespace Mem
