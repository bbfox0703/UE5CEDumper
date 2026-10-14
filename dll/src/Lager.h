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
    Window w{appMin, appMax};
    if (origin > kMaxMemoryRange && w.minAddr < origin - kMaxMemoryRange) w.minAddr = origin - kMaxMemoryRange;
    if (w.maxAddr > origin + kMaxMemoryRange) w.maxAddr = origin + kMaxMemoryRange;
    w.maxAddr -= kMemoryBlockSize - 1;
    return w;
}

/// GetMemoryBlock's reuse test ("Ignore the blocks too far"). A block that passes is taken whenever it still has a
/// free slot, before any new block is allocated.
inline bool BlockServes(uint64_t block, const Window& w) {
    return !(block < w.minAddr || block >= w.maxAddr);
}

/// VirtualQuery over a synthetic map: the region holding `addr`, or nullptr where the real call returns 0. The map
/// is sorted by base with no overlap -- what a VirtualQuery walk produces.
inline const Region* QueryAt(const std::vector<Region>& map, uint64_t addr) {
    size_t lo = 0, hi = map.size();
    while (lo < hi) {
        const size_t mid = lo + (hi - lo) / 2;
        if (map[mid].base + map[mid].size <= addr) lo = mid + 1;
        else hi = mid;
    }
    if (lo < map.size() && map[lo].base <= addr && addr < map[lo].base + map[lo].size) return &map[lo];
    return nullptr;
}

/// FindPrevFreeRegion: from the allocation-granularity multiple below the one holding `origin`, step DOWN one
/// allocation at a time (to the granule under its AllocationBase) until an address in a free region. 0 = NULL.
/// GetMemoryBlock tries this direction first.
inline uint64_t FindPrevFreeRegion(const std::vector<Region>& map, uint64_t origin, uint64_t minAddr,
                                   uint64_t granularity) {
    uint64_t tryAddr = origin;
    tryAddr -= tryAddr % granularity;
    tryAddr -= granularity;
    while (tryAddr >= minAddr) {
        const Region* r = QueryAt(map, tryAddr);
        if (!r) break;
        if (r->state == kMemFree) return tryAddr;
        if (r->allocationBase < granularity) break;
        tryAddr = r->allocationBase - granularity;
    }
    return 0;
}

/// FindNextFreeRegion: from the allocation-granularity multiple above the one holding `origin`, step UP one region
/// at a time (to the first granule at or past its end) until an address in a free region. 0 = NULL.
inline uint64_t FindNextFreeRegion(const std::vector<Region>& map, uint64_t origin, uint64_t maxAddr,
                                   uint64_t granularity) {
    uint64_t tryAddr = origin;
    tryAddr -= tryAddr % granularity;
    tryAddr += granularity;
    while (tryAddr <= maxAddr) {
        const Region* r = QueryAt(map, tryAddr);
        if (!r) break;
        if (r->state == kMemFree) return tryAddr;
        tryAddr = r->base + r->size;
        tryAddr += granularity - 1;
        tryAddr -= tryAddr % granularity;
    }
    return 0;
}

/// Where GetMemoryBlock would allocate a NEW block for `origin`: below first, then above; 0 = MH_ERROR_MEMORY_ALLOC.
/// A free granule-aligned address always takes the one-page VirtualAlloc (a free region ends on a page boundary
/// past it), so only a race could make the real call disagree.
inline uint64_t NewBlockAddress(const std::vector<Region>& map, uint64_t origin, uint64_t appMin, uint64_t appMax,
                                uint64_t granularity) {
    const Window w = SearchWindow(origin, appMin, appMax);
    if (const uint64_t below = FindPrevFreeRegion(map, origin, w.minAddr, granularity)) return below;
    return FindNextFreeRegion(map, origin, w.maxAddr, granularity);
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
    WindowSummary s;
    s.window  = SearchWindow(origin, appMin, appMax);
    s.spanEnd = s.window.maxAddr + kMemoryBlockSize;
    uint64_t reached = s.window.minAddr;   // how far an unbroken run of regions covers the span
    for (const Region& r : map) {
        const uint64_t rEnd = r.base + r.size;
        if (r.base <= reached && rEnd > reached) reached = rEnd;
        const uint64_t lo = r.base > s.window.minAddr ? r.base : s.window.minAddr;
        const uint64_t hi = rEnd < s.spanEnd ? rEnd : s.spanEnd;
        if (lo >= hi) continue;
        const uint64_t bytes = hi - lo;
        if (r.state == kMemFree) {
            ++s.freeRegions;
            s.freeBytes += bytes;
            if (bytes > s.largestFreeSize) { s.largestFreeSize = bytes; s.largestFreeBase = lo; }
            const uint64_t first = lo + (granularity - lo % granularity) % granularity;
            if (first < hi && first <= s.window.maxAddr) ++s.blockableRegions;
        } else if (r.state == kMemReserve) {
            s.reservedBytes += bytes;
        } else if (r.state == kMemCommit) {
            s.committedBytes += bytes;
        }
    }
    s.covered   = reached >= s.spanEnd;
    s.pickBelow = FindPrevFreeRegion(map, origin, s.window.minAddr, granularity);
    s.pickAbove = FindNextFreeRegion(map, origin, s.window.maxAddr, granularity);
    return s;
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
    ImageLayout img;
    img.base = base;
    auto u16 = [&](size_t off) -> uint32_t {
        return (bytes && off + 2 <= len) ? (uint32_t)bytes[off] | ((uint32_t)bytes[off + 1] << 8) : 0u;
    };
    auto u32 = [&](size_t off) -> uint32_t {
        return (bytes && off + 4 <= len) ? (uint32_t)bytes[off] | ((uint32_t)bytes[off + 1] << 8) |
                                           ((uint32_t)bytes[off + 2] << 16) | ((uint32_t)bytes[off + 3] << 24)
                                         : 0u;
    };
    if (len < 0x40 || u16(0) != 0x5A4D) return img;                           // "MZ"
    const size_t pe = u32(0x3C);
    if (pe > len || len - pe < 24 || u32(pe) != 0x00004550) return img;      // "PE\0\0"
    const uint32_t numSections = u16(pe + 6);
    const uint32_t optSize     = u16(pe + 20);
    const size_t   opt         = pe + 24;
    if (optSize < 72 || opt + 72 > len || u16(opt) != 0x20B) return img;    // PE32+ only: the DLL is x64
    img.entryRva    = u32(opt + 16);
    img.sizeOfImage = u32(opt + 56);
    img.dynamicBase = (u16(opt + 70) & kDllCharDynamicBase) != 0;
    img.valid       = img.sizeOfImage != 0;
    const size_t table = opt + optSize;
    for (uint32_t i = 0; i < numSections; ++i) {
        const size_t e = table + (size_t)i * 40;
        if (e + 40 > len) break;
        Section s;
        s.virtualSize     = u32(e + 8);
        s.rva             = u32(e + 12);
        s.rawSize         = u32(e + 16);
        s.characteristics = u32(e + 36);
        img.sections.push_back(s);
    }
    return img;
}

/// The log line for a failed block search: the window, what is free in it and where a new block would go, what
/// took the rest, the held block, and the main image beside it. heldBlock 0 = no block held.
inline std::string FormatWindowSummary(const WindowSummary& s, uint64_t origin, const ImageLayout& img,
                                       uint64_t heldBlock) {
    auto hex = [](uint64_t v) {
        char b[24];
        if (v) std::snprintf(b, sizeof b, "0x%llX", (unsigned long long)v);
        else   std::snprintf(b, sizeof b, "none");
        return std::string(b);
    };
    const std::string below = hex(s.pickBelow), above = hex(s.pickAbove), held = hex(heldBlock);
    char buf[1024];
    std::snprintf(buf, sizeof buf,
        "MinHook block window for 0x%llX: block starts [0x%llX, 0x%llX]%s -- %u free region(s), 0x%llX bytes free, "
        "largest 0x%llX at 0x%llX, %u can start a block; reserved 0x%llX, committed 0x%llX; "
        "FindPrevFreeRegion -> %s, FindNextFreeRegion -> %s; held block %s%s; main image 0x%llX + 0x%X, "
        "DYNAMICBASE %s",
        (unsigned long long)origin, (unsigned long long)s.window.minAddr, (unsigned long long)s.window.maxAddr,
        s.covered ? "" : " (map INCOMPLETE: a VirtualQuery failed inside the window)",
        s.freeRegions, (unsigned long long)s.freeBytes, (unsigned long long)s.largestFreeSize,
        (unsigned long long)s.largestFreeBase, s.blockableRegions, (unsigned long long)s.reservedBytes,
        (unsigned long long)s.committedBytes, below.c_str(), above.c_str(), held.c_str(),
        heldBlock == 0 ? "" : (BlockServes(heldBlock, s.window) ? " (serves this target)"
                                                                : " (out of this target's reach)"),
        (unsigned long long)img.base, img.sizeOfImage,
        img.valid ? (img.dynamicBase ? "yes" : "no") : "unknown (headers unreadable)");
    return buf;
}

} // namespace Lager
