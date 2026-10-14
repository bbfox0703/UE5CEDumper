#pragma once

// ============================================================
// Lager — ラーガー (魔法特務隊員・「倉庫」 — Special Forces trooper, "depot")
// TrampolineDepot: a pure model of where MinHook puts a hook's trampoline block, and of the main image's PE header
// facts that decide where a block can be held ([MH-PRERESERVE-PROXY]).
//
// Why a model at all: MH_CreateHook answers MH_ERROR_MEMORY_ALLOC and nothing more when it finds no block, so the
// only way to say WHY on a live game is to walk the address space the way MinHook walked it and summarise that.
// Every rule here transcribes vendor/minhook/src/buffer.c (its x64 paths) and is named after the function it
// mirrors, so one can be checked against the other. No Win32 calls: Stark.cpp does the VirtualQuery walk and reads
// the image, and dll_helpers_test drives every rule with synthetic maps, because no test target compiles Stark.cpp.
// ============================================================

#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace Lager {

// buffer.c / buffer.h, x64. A change to any of these in the vendored copy invalidates this model.
constexpr uint64_t kMemoryBlockSize = 0x1000;       // MEMORY_BLOCK_SIZE: one block, one VirtualAlloc
constexpr uint64_t kMaxMemoryRange  = 0x40000000;   // MAX_MEMORY_RANGE: +-1 GB around the hook target
constexpr uint64_t kMemorySlotSize  = 64;           // MEMORY_SLOT_SIZE: one trampoline per slot

// VirtualQuery's MEM_* states and PAGE_* protections by value, so this header needs no <Windows.h>; Stark.cpp
// static_asserts each against the SDK.
constexpr uint32_t kMemCommit  = 0x1000;
constexpr uint32_t kMemReserve = 0x2000;
constexpr uint32_t kMemFree    = 0x10000;
constexpr uint32_t kPageExecute          = 0x10;
constexpr uint32_t kPageExecuteRead      = 0x20;
constexpr uint32_t kPageExecuteReadWrite = 0x40;
constexpr uint32_t kPageExecuteWriteCopy = 0x80;
constexpr uint32_t kPageGuard            = 0x100;

/// One VirtualQuery answer: [base, base + size) shares one state; allocationBase is the base of the allocation (a
/// VirtualAlloc reservation, a mapped image) holding it, which FindPrevFreeRegion steps by.
struct Region {
    uint64_t base = 0;
    uint64_t size = 0;
    uint32_t state = 0;
    uint64_t allocationBase = 0;
};

/// GetMemoryBlock's search bounds for one hook target: a new block may start at any address in [minAddr, maxAddr],
/// and a block already on MinHook's list is reused when minAddr <= block < maxAddr.
struct Window {
    uint64_t minAddr = 0;
    uint64_t maxAddr = 0;
};

/// GetMemoryBlock's window, line for line: the application range, narrowed to origin +-MAX_MEMORY_RANGE, then
/// maxAddr pulled down by MEMORY_BLOCK_SIZE - 1 so a block placed at maxAddr still ends inside the bound.
inline Window SearchWindow(uint64_t origin, uint64_t appMin, uint64_t appMax) {
    (void)origin; (void)appMin; (void)appMax; return Window{};   // RED stub
}

/// GetMemoryBlock's reuse test ("Ignore the blocks too far"). A block that passes is taken whenever it still has a
/// free slot, before any new block is allocated.
inline bool BlockServes(uint64_t block, const Window& w) {
    (void)block; (void)w; return false;   // RED stub
}

/// VirtualQuery over a synthetic map: the region holding `addr`, or nullptr where the real call returns 0. The map
/// is sorted by base with no overlap -- what a VirtualQuery walk produces.
inline const Region* QueryAt(const std::vector<Region>& map, uint64_t addr) {
    (void)map; (void)addr; return nullptr;   // RED stub
}

/// FindPrevFreeRegion: from the allocation-granularity multiple below the one holding `origin`, step DOWN one
/// allocation at a time (to the granule under its AllocationBase) until an address in a free region. 0 = NULL.
/// GetMemoryBlock tries this direction first.
inline uint64_t FindPrevFreeRegion(const std::vector<Region>& map, uint64_t origin, uint64_t minAddr,
                                   uint64_t granularity) {
    (void)map; (void)origin; (void)minAddr; (void)granularity; return 0;   // RED stub
}

/// FindNextFreeRegion: from the allocation-granularity multiple above the one holding `origin`, step UP one region
/// at a time (to the first granule at or past its end) until an address in a free region. 0 = NULL.
inline uint64_t FindNextFreeRegion(const std::vector<Region>& map, uint64_t origin, uint64_t maxAddr,
                                   uint64_t granularity) {
    (void)map; (void)origin; (void)maxAddr; (void)granularity; return 0;   // RED stub
}

/// Where GetMemoryBlock would allocate a NEW block for `origin`: below first, then above; 0 = MH_ERROR_MEMORY_ALLOC.
/// A free granule-aligned address always takes the one-page VirtualAlloc (a free region ends on a page boundary
/// past it), so only a race could make the real call disagree.
inline uint64_t NewBlockAddress(const std::vector<Region>& map, uint64_t origin, uint64_t appMin, uint64_t appMax,
                                uint64_t granularity) {
    (void)map; (void)origin; (void)appMin; (void)appMax; (void)granularity; return 0;   // RED stub
}

/// What the window around one hook target held when it was summarised: whether MinHook could have placed a block
/// there, and if not, what took the space. Byte counts cover [minAddr, spanEnd), the bytes a block could occupy.
struct WindowSummary {
    Window   window;
    uint64_t spanEnd = 0;             // exclusive: the last block start (maxAddr) plus the block's own bytes
    uint32_t freeRegions = 0;
    uint64_t freeBytes = 0;
    uint64_t largestFreeBase = 0;
    uint64_t largestFreeSize = 0;
    uint32_t blockableRegions = 0;    // free regions holding a granule-aligned address a block could start at
    uint64_t reservedBytes = 0;
    uint64_t committedBytes = 0;
    uint64_t pickBelow = 0;           // FindPrevFreeRegion's answer, 0 = none
    uint64_t pickAbove = 0;           // FindNextFreeRegion's answer, 0 = none
    bool     covered = false;         // the map runs unbroken over [minAddr, spanEnd)
};

inline WindowSummary SummarizeWindow(const std::vector<Region>& map, uint64_t origin, uint64_t appMin,
                                     uint64_t appMax, uint64_t granularity) {
    (void)map; (void)origin; (void)appMin; (void)appMax; (void)granularity; return WindowSummary{};   // RED stub
}

/// The main image as its PE headers describe it: reported beside the window summary, and what the block holder
/// picks its target from.
struct Section {
    uint32_t rva = 0;
    uint32_t virtualSize = 0;
    uint32_t rawSize = 0;
    uint32_t characteristics = 0;
};
constexpr uint32_t kScnMemExecute      = 0x20000000;   // IMAGE_SCN_MEM_EXECUTE
constexpr uint32_t kDllCharDynamicBase = 0x0040;       // IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE

struct ImageLayout {
    bool     valid = false;
    uint64_t base = 0;
    uint32_t sizeOfImage = 0;
    uint32_t entryRva = 0;
    bool     dynamicBase = false;
    std::vector<Section> sections;
};

/// Parse a PE32+ header from `len` bytes copied from the start of an image mapped at `base`. A section is kept only
/// when its whole table entry is in the copy, so a short copy loses sections, never the header fields.
inline ImageLayout ParseImageLayout(const uint8_t* bytes, size_t len, uint64_t base) {
    (void)bytes; (void)len; ImageLayout img; img.base = base; return img;   // RED stub
}

/// The log line for a failed block search: the window, what is free in it and where a new block would go, what
/// took the rest, the held block, and the main image beside it. heldBlock 0 = no block held.
inline std::string FormatWindowSummary(const WindowSummary& s, uint64_t origin, const ImageLayout& img,
                                       uint64_t heldBlock) {
    (void)s; (void)origin; (void)img; (void)heldBlock; return std::string();   // RED stub
}

} // namespace Lager
