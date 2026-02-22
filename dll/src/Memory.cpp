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

// Parsed pattern: uint8_t mask avoids bit-packing overhead of std::vector<bool>.
struct ParsedPattern {
    std::vector<uint8_t> bytes;
    std::vector<uint8_t> mask;    // 1 = must match, 0 = wildcard
    uint8_t  firstByte    = 0;
    bool     firstIsFixed = false; // true when bytes[0] is a literal
};

static bool ParsePattern(const char* patStr, ParsedPattern& out) {
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

// Scan a single contiguous memory region for the pattern.
// Returns pointer to first match, or nullptr.
//
// Key optimisations (per Memory-Scanning-Internals.md):
//  1. First-byte skip  — when bytes[0] is a literal, use memchr to jump to
//     the next candidate position instead of testing every byte.
//  2. uint8_t mask     — avoids the bit-manipulation overhead of vector<bool>.
//  3. Early exit       — inner loop breaks at the first mismatch (CE §4).
static const uint8_t* ScanRegion(
    const uint8_t*      scanStart,
    size_t              regionSize,
    const ParsedPattern& pat)
{
    const size_t patLen = pat.bytes.size();
    if (regionSize < patLen) return nullptr;

    const uint8_t* scanEnd = scanStart + regionSize - patLen; // last valid start
    const uint8_t* cur     = scanStart;

    while (cur <= scanEnd) {
        // ── First-byte fast skip ───────────────────────────────────────────
        // memchr scans at native speed (SSE2/AVX on modern CRT) to find the
        // next position where cur[0] == firstByte, eliminating the per-byte
        // overhead of our outer loop for non-matching positions.
        if (pat.firstIsFixed) {
            const size_t remaining = static_cast<size_t>(scanEnd - cur) + patLen;
            cur = static_cast<const uint8_t*>(
                memchr(cur, pat.firstByte, remaining));
            if (!cur) return nullptr;
        }

        // ── Full pattern check (skip index 0 — already verified by memchr) ─
        bool matched = true;
        for (size_t i = (pat.firstIsFixed ? 1u : 0u); i < patLen; ++i) {
            if (pat.mask[i] && cur[i] != pat.bytes[i]) {
                matched = false;
                break; // early exit on first mismatch
            }
        }
        if (matched) return cur;
        ++cur;
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

} // namespace Mem
