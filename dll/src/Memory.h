#pragma once

// ============================================================
// Memory.h — Basic memory read/write utilities (DLL is injected)
// ============================================================

#include <Windows.h>
#include <cstdint>

namespace Mem {

// Direct memory read (DLL is in-process)
template<typename T>
inline T Read(uintptr_t addr) {
    return *reinterpret_cast<T*>(addr);
}

// Get typed pointer
template<typename T>
inline T* Ptr(uintptr_t addr) {
    return reinterpret_cast<T*>(addr);
}

// Safe read with SEH protection, returns true on success
template<typename T>
inline bool ReadSafe(uintptr_t addr, T& out) {
    __try {
        out = *reinterpret_cast<T*>(addr);
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        out = T{};
        return false;
    }
}

// Safe memory copy with SEH protection
bool ReadBytesSafe(uintptr_t addr, void* buf, size_t size);

// Write bytes to memory
bool WriteBytes(uintptr_t addr, const void* buf, size_t size);

// Get base address of a loaded module (nullptr = main module)
uintptr_t GetModuleBase(const wchar_t* moduleName = nullptr);

// Get module size
size_t GetModuleSize(const wchar_t* moduleName = nullptr);

// AOB (Array of Bytes) pattern scan in module memory
// Pattern format: "48 8B 05 ?? ?? ?? ??" where ?? is wildcard
// Returns first match address, or 0 on failure
uintptr_t AOBScan(const char* pattern, uintptr_t start = 0, size_t size = 0);

// Resolve RIP-relative address
// instrAddr: address of the instruction start
// opcodeLen: length of the opcode before the rel32 displacement
// totalLen:  total instruction length (for RIP base calculation)
uintptr_t ResolveRIP(uintptr_t instrAddr, int opcodeLen = 3, int totalLen = 7);

} // namespace Mem
