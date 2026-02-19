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

// --- TArray<T> reading utilities ---
// UE5 TArray layout: { T* Data +0x00, int32 Count +0x08, int32 Max +0x0C }
struct TArrayView {
    uintptr_t Data  = 0;
    int32_t   Count = 0;
    int32_t   Max   = 0;
};

// Read a TArray header from the given address. Returns true if valid.
inline bool ReadTArray(uintptr_t addr, TArrayView& out) {
    out = {};
    if (!addr) return false;
    if (!ReadSafe(addr + 0x00, out.Data)) return false;
    if (!ReadSafe(addr + 0x08, out.Count)) return false;
    if (!ReadSafe(addr + 0x0C, out.Max)) return false;
    // Sanity checks
    if (out.Count < 0 || out.Count > 0x100000) { out = {}; return false; }
    if (out.Max < out.Count) { out = {}; return false; }
    return true;
}

// Read a pointer element at index i from a TArray of pointers
inline uintptr_t ReadTArrayElement(const TArrayView& arr, int32_t i) {
    uintptr_t elem = 0;
    if (i >= 0 && i < arr.Count && arr.Data)
        ReadSafe(arr.Data + i * sizeof(uintptr_t), elem);
    return elem;
}

} // namespace Mem
