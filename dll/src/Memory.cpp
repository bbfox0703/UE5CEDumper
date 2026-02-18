// ============================================================
// Memory.cpp — AOBScan, module base, RIP-relative resolution
// ============================================================

#include "Memory.h"
#include "Logger.h"

#include <Psapi.h>
#include <vector>
#include <string>
#include <cstring>

namespace Mem {

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
    if (!VirtualProtect(reinterpret_cast<void*>(addr), size, PAGE_EXECUTE_READWRITE, &oldProtect)) {
        return false;
    }
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

uintptr_t GetModuleBase(const wchar_t* moduleName) {
    HMODULE hModule = nullptr;
    if (moduleName) {
        hModule = GetModuleHandleW(moduleName);
    } else {
        hModule = GetModuleHandleW(nullptr);
    }
    return reinterpret_cast<uintptr_t>(hModule);
}

size_t GetModuleSize(const wchar_t* moduleName) {
    HMODULE hModule = nullptr;
    if (moduleName) {
        hModule = GetModuleHandleW(moduleName);
    } else {
        hModule = GetModuleHandleW(nullptr);
    }
    if (!hModule) return 0;

    MODULEINFO info{};
    if (GetModuleInformation(GetCurrentProcess(), hModule, &info, sizeof(info))) {
        return info.SizeOfImage;
    }
    return 0;
}

// Parse pattern string like "48 8B 05 ?? ?? ?? ??" into bytes and mask
static bool ParsePattern(const char* pattern, std::vector<uint8_t>& bytes, std::vector<bool>& mask) {
    bytes.clear();
    mask.clear();

    const char* p = pattern;
    while (*p) {
        // Skip whitespace
        while (*p == ' ' || *p == '\t') ++p;
        if (!*p) break;

        if (p[0] == '?' && p[1] == '?') {
            bytes.push_back(0);
            mask.push_back(false);  // wildcard
            p += 2;
        } else {
            char hex[3] = { p[0], p[1], 0 };
            bytes.push_back(static_cast<uint8_t>(strtoul(hex, nullptr, 16)));
            mask.push_back(true);   // must match
            p += 2;
        }
    }

    return !bytes.empty();
}

uintptr_t AOBScan(const char* pattern, uintptr_t start, size_t size) {
    std::vector<uint8_t> patBytes;
    std::vector<bool> patMask;

    if (!ParsePattern(pattern, patBytes, patMask)) {
        LOG_ERROR("AOBScan: Failed to parse pattern");
        return 0;
    }

    // Default to main module
    if (start == 0) {
        start = GetModuleBase(nullptr);
        if (start == 0) {
            LOG_ERROR("AOBScan: Failed to get module base");
            return 0;
        }
    }
    if (size == 0) {
        size = GetModuleSize(nullptr);
        if (size == 0) {
            LOG_ERROR("AOBScan: Failed to get module size");
            return 0;
        }
    }

    const size_t patLen = patBytes.size();
    const uint8_t* scanStart = reinterpret_cast<const uint8_t*>(start);
    const uint8_t* scanEnd = scanStart + size - patLen;

    LOG_DEBUG("AOBScan: Scanning %zu bytes at 0x%llX for pattern [%s]",
              size, static_cast<unsigned long long>(start), pattern);

    for (const uint8_t* current = scanStart; current <= scanEnd; ++current) {
        bool found = true;
        for (size_t i = 0; i < patLen; ++i) {
            if (patMask[i] && current[i] != patBytes[i]) {
                found = false;
                break;
            }
        }
        if (found) {
            uintptr_t result = reinterpret_cast<uintptr_t>(current);
            LOG_INFO("AOBScan: Found pattern at 0x%llX", static_cast<unsigned long long>(result));
            return result;
        }
    }

    LOG_WARN("AOBScan: Pattern not found [%s]", pattern);
    return 0;
}

uintptr_t ResolveRIP(uintptr_t instrAddr, int opcodeLen, int totalLen) {
    // RIP-relative: target = (instrAddr + totalLen) + *(int32_t*)(instrAddr + opcodeLen)
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
