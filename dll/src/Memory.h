#pragma once

// ============================================================
// Memory.h — Basic memory read/write utilities (DLL is injected)
// ============================================================

#include <Windows.h>
#include <cstdint>
#include <vector>

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

// AOBScanAll: returns ALL match addresses for the given pattern.
// Scans executable sections of a specific module (moduleBase=0 → main module).
std::vector<uintptr_t> AOBScanAll(const char* pattern, uintptr_t moduleBase = 0);

// Loaded module info
struct ModuleInfo {
    HMODULE   hModule = nullptr;
    uintptr_t base    = 0;
    size_t    size    = 0;
    wchar_t   name[MAX_PATH] = {};
};

// Get all loaded modules in the current process
std::vector<ModuleInfo> GetLoadedModules();

// AOBScanAll across ALL loaded modules (main EXE + DLLs).
// Returns all match addresses found in any module.
std::vector<uintptr_t> AOBScanAllModules(const char* pattern);

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

// --- TSparseArray reading utilities (for TSet/TMap) ---
// UE TSparseArray layout within TSet/TMap (0x40 bytes):
//   +0x00  TArray<ElementOrFreeListLink>  (Data*, Count, Max)
//   +0x10  TBitArray AllocationFlags
//          +0x10  InlineData[4] (uint32 x4 = 128 bits inline)
//          +0x20  SecondaryData* (heap ptr for >128 bits)
//          +0x28  NumBits (int32)
//          +0x2C  MaxBits (int32)
//   +0x38  FirstFreeIndex (int32)
//   +0x3C  NumFreeIndices (int32)
struct TSparseArrayView {
    uintptr_t Data         = 0;     // Element data pointer
    int32_t   MaxIndex     = 0;     // Total slots (allocated + free)
    int32_t   MaxCapacity  = 0;     // TArray::Max
    int32_t   NumFreeIndices = 0;   // Free slot count
    // Allocation flags
    uint32_t  inlineBits[4] = {};   // First 128 bits inline
    uintptr_t secondaryData = 0;    // Heap pointer for >128 bits
    int32_t   numBits      = 0;     // Total bit count
};

// Read a TSparseArray header from memory. Returns true if valid.
inline bool ReadTSparseArray(uintptr_t addr, TSparseArrayView& out) {
    out = {};
    if (!addr) return false;
    // TArray header at +0x00
    if (!ReadSafe(addr + 0x00, out.Data)) return false;
    if (!ReadSafe(addr + 0x08, out.MaxIndex)) return false;
    if (!ReadSafe(addr + 0x0C, out.MaxCapacity)) return false;
    // Sanity: MaxIndex should be reasonable
    if (out.MaxIndex < 0 || out.MaxIndex > 0x100000) { out = {}; return false; }
    if (out.MaxCapacity < out.MaxIndex) { out = {}; return false; }
    // Allocation flags inline data (4 x uint32 at +0x10)
    for (int i = 0; i < 4; ++i)
        ReadSafe(addr + 0x10 + i * 4, out.inlineBits[i]);
    // Secondary data pointer at +0x20
    ReadSafe(addr + 0x20, out.secondaryData);
    // NumBits at +0x28
    ReadSafe(addr + 0x28, out.numBits);
    // NumFreeIndices at +0x3C
    ReadSafe(addr + 0x3C, out.NumFreeIndices);
    return true;
}

// Check if a sparse array index is allocated (bit set in AllocationFlags).
inline bool IsSparseIndexAllocated(const TSparseArrayView& sa, int32_t index) {
    if (index < 0 || index >= sa.numBits) return false;
    int wordIdx = index / 32;
    int bitIdx  = index % 32;
    uint32_t word = 0;
    if (wordIdx < 4) {
        word = sa.inlineBits[wordIdx];
    } else if (sa.secondaryData) {
        ReadSafe(sa.secondaryData + wordIdx * 4, word);
    } else {
        return false;
    }
    return (word & (1u << bitIdx)) != 0;
}

// Compute TSetElement stride: { T value; int32 HashNextId; int32 HashIndex; }
// HashNextId is aligned to 4 bytes after the value.
inline int32_t ComputeSetElementStride(int32_t elemSize) {
    int32_t hashStart = (elemSize + 3) & ~3;  // align to 4
    return hashStart + 8;  // + HashNextId(4) + HashIndex(4)
}

} // namespace Mem
