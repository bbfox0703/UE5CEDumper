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
// VirtualQuery's Type field: what occupies a non-free region. The hypothesis [MH-PRERESERVE-PROXY] rests on is that
// the game's HEAP (MEM_PRIVATE) grows through the window; loaded modules (MEM_IMAGE) and mapped pak/section views
// (MEM_MAPPED) would fit the byte totals just as well, so the summary separates them.
constexpr uint32_t kMemPrivate = 0x20000;
constexpr uint32_t kMemMapped  = 0x40000;
constexpr uint32_t kMemImage   = 0x1000000;
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
    uint32_t type = 0;            // mbi.Type for a non-free region: MEM_PRIVATE / MEM_IMAGE / MEM_MAPPED
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

/// FindPrevFreeRegion's walk over any query source. `query(addr, out)` fills `out` with the region holding `addr`
/// and returns false where VirtualQuery would answer 0 (unmapped). This is buffer.c's walk line for line; both the
/// synthetic-map overload below and the live VirtualQuery walk in Stark drive it, so the two cannot drift.
template <typename QueryFn>
inline uint64_t FindPrevFreeRegionWith(QueryFn query, uint64_t origin, uint64_t minAddr, uint64_t granularity) {
    uint64_t tryAddr = origin;
    tryAddr -= tryAddr % granularity;
    tryAddr -= granularity;
    while (tryAddr >= minAddr) {
        Region r;
        if (!query(tryAddr, r)) break;
        if (r.state == kMemFree) return tryAddr;
        if (r.allocationBase < granularity) break;
        tryAddr = r.allocationBase - granularity;
    }
    return 0;
}

/// FindNextFreeRegion's walk over any query source (see FindPrevFreeRegionWith).
template <typename QueryFn>
inline uint64_t FindNextFreeRegionWith(QueryFn query, uint64_t origin, uint64_t maxAddr, uint64_t granularity) {
    uint64_t tryAddr = origin;
    tryAddr -= tryAddr % granularity;
    tryAddr += granularity;
    while (tryAddr <= maxAddr) {
        Region r;
        if (!query(tryAddr, r)) break;
        if (r.state == kMemFree) return tryAddr;
        tryAddr = r.base + r.size;
        tryAddr += granularity - 1;
        tryAddr -= tryAddr % granularity;
    }
    return 0;
}

/// A query source over a synthetic map: the region holding `addr`, or false where VirtualQuery would answer 0.
inline bool QueryMap(const std::vector<Region>& map, uint64_t addr, Region& out) {
    const Region* r = QueryAt(map, addr);
    if (!r) return false;
    out = *r;
    return true;
}

/// FindPrevFreeRegion: from the allocation-granularity multiple below the one holding `origin`, step DOWN one
/// allocation at a time (to the granule under its AllocationBase) until an address in a free region. 0 = NULL.
/// GetMemoryBlock tries this direction first.
inline uint64_t FindPrevFreeRegion(const std::vector<Region>& map, uint64_t origin, uint64_t minAddr,
                                   uint64_t granularity) {
    return FindPrevFreeRegionWith([&](uint64_t a, Region& out) { return QueryMap(map, a, out); },
                                  origin, minAddr, granularity);
}

/// FindNextFreeRegion: from the allocation-granularity multiple above the one holding `origin`, step UP one region
/// at a time (to the first granule at or past its end) until an address in a free region. 0 = NULL.
inline uint64_t FindNextFreeRegion(const std::vector<Region>& map, uint64_t origin, uint64_t maxAddr,
                                   uint64_t granularity) {
    return FindNextFreeRegionWith([&](uint64_t a, Region& out) { return QueryMap(map, a, out); },
                                  origin, maxAddr, granularity);
}

/// Where GetMemoryBlock would allocate a NEW block for `origin`: below first, then above; 0 = no free block start.
/// This models only the ADDRESS search. A free granule it names can still fail to become a block: VirtualAlloc(
/// PAGE_EXECUTE_READWRITE) can be refused under a dynamic-code policy (ACG) or at the commit limit, and MinHook also
/// answers MH_ERROR_MEMORY_ALLOC when AddHookEntry's HeapAlloc on its private heap fails -- neither is the window. So
/// a non-zero answer here beside a real MH_ERROR_MEMORY_ALLOC means the failure was NOT the address window; the live
/// LastError (FormatWindowSummary's lastError) says which.
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
    uint64_t privateBytes = 0;        // non-free MEM_PRIVATE bytes in span -- the heap, the hypothesised occupant
    uint64_t imageBytes = 0;          // non-free MEM_IMAGE bytes -- loaded modules
    uint64_t mappedBytes = 0;         // non-free MEM_MAPPED bytes -- file/section views (pak)
    uint64_t largestTakenBase = 0;    // the single largest non-free region in span, and what holds it
    uint64_t largestTakenSize = 0;
    uint32_t largestTakenType = 0;
    uint64_t pickBelow = 0;           // FindPrevFreeRegion's answer, 0 = none
    uint64_t pickAbove = 0;           // FindNextFreeRegion's answer, 0 = none
    bool     covered = false;         // the map runs unbroken over [minAddr, spanEnd)
};

/// A one-word name for a VirtualQuery Type, for the log line.
inline const char* MemTypeName(uint32_t type) {
    if (type == kMemPrivate) return "private/heap";
    if (type == kMemImage)   return "image/module";
    if (type == kMemMapped)  return "mapped/view";
    return "other";
}

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
        } else if (r.state == kMemReserve || r.state == kMemCommit) {
            if (r.state == kMemReserve) s.reservedBytes += bytes;
            else                        s.committedBytes += bytes;
            if (r.type == kMemPrivate)      s.privateBytes += bytes;
            else if (r.type == kMemImage)   s.imageBytes += bytes;
            else if (r.type == kMemMapped)  s.mappedBytes += bytes;
            if (bytes > s.largestTakenSize) {
                s.largestTakenSize = bytes;
                s.largestTakenBase = r.allocationBase ? r.allocationBase : lo;
                s.largestTakenType = r.type;
            }
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

/// Why the region walk that built the map stopped. When the map does not cover the whole window, the reason the
/// summary prints must be the TRUE one: a budget or region-cap stop is not "a VirtualQuery failed", and reporting it
/// as one sent a reader looking for an unmapped page that was never there.
enum class WalkStop { Complete, QueryFailed, Budget, RegionCap };

inline const char* WalkStopNote(WalkStop stop) {
    switch (stop) {
        case WalkStop::Budget:     return " (map INCOMPLETE: the walk's time budget was spent before the window was "
                                          "fully mapped -- the byte totals cover only the part walked)";
        case WalkStop::RegionCap:  return " (map INCOMPLETE: the region cap was hit before the window was fully "
                                          "mapped -- the byte totals cover only the part walked)";
        case WalkStop::QueryFailed:return " (map INCOMPLETE: a VirtualQuery failed inside the window)";
        default:                   return " (map INCOMPLETE)";
    }
}

/// The log line for a failed block search: the window, what is free in it and where a new block would go, what
/// took the rest, the held block, and the main image beside it. heldBlock 0 = no block held. `stop` is why the map
/// walk ended, printed only when the map did not cover the span (so a budget stop is not blamed on a phantom
/// VirtualQuery failure). `lastError` is the GetLastError() captured right after MH_CreateHook returned
/// MH_ERROR_MEMORY_ALLOC: when the address search DID find room (a pick is non-zero, or the held block serves the
/// target), the failure was not the window, and the line says so with that error -- VirtualAlloc was refused (commit
/// limit / ACG) or MinHook's hook table could not grow.
inline std::string FormatWindowSummary(const WindowSummary& s, uint64_t origin, const ImageLayout& img,
                                       uint64_t heldBlock, WalkStop stop = WalkStop::Complete, uint32_t lastError = 0) {
    auto hex = [](uint64_t v) {
        char b[24];
        if (v) std::snprintf(b, sizeof b, "0x%llX", (unsigned long long)v);
        else   std::snprintf(b, sizeof b, "none");
        return std::string(b);
    };
    const std::string below = hex(s.pickBelow), above = hex(s.pickAbove), held = hex(heldBlock);
    const bool roomFound = s.pickBelow != 0 || s.pickAbove != 0 ||
                           (heldBlock != 0 && BlockServes(heldBlock, s.window));
    char taken[224];
    std::snprintf(taken, sizeof taken,
        "; taken by private 0x%llX, image 0x%llX, mapped 0x%llX, largest occupant 0x%llX x 0x%llX (%s)",
        (unsigned long long)s.privateBytes, (unsigned long long)s.imageBytes, (unsigned long long)s.mappedBytes,
        (unsigned long long)s.largestTakenBase, (unsigned long long)s.largestTakenSize,
        MemTypeName(s.largestTakenType));
    char refused[224] = "";
    if (roomFound)
        std::snprintf(refused, sizeof refused,
            " -- NB a block start WAS free, so the window is not the cause: VirtualAlloc refused the page "
            "(commit limit / ACG) or MinHook's hook table could not grow (LastError %u)", (unsigned)lastError);
    char buf[1536];
    std::snprintf(buf, sizeof buf,
        "MinHook block window for 0x%llX: block starts [0x%llX, 0x%llX]%s -- %u free region(s), 0x%llX bytes free, "
        "largest 0x%llX at 0x%llX, %u can start a block; reserved 0x%llX, committed 0x%llX%s; "
        "FindPrevFreeRegion -> %s, FindNextFreeRegion -> %s; held block %s%s; main image 0x%llX + 0x%X, "
        "DYNAMICBASE %s%s",
        (unsigned long long)origin, (unsigned long long)s.window.minAddr, (unsigned long long)s.window.maxAddr,
        s.covered ? "" : WalkStopNote(stop),
        s.freeRegions, (unsigned long long)s.freeBytes, (unsigned long long)s.largestFreeSize,
        (unsigned long long)s.largestFreeBase, s.blockableRegions, (unsigned long long)s.reservedBytes,
        (unsigned long long)s.committedBytes, taken, below.c_str(), above.c_str(), held.c_str(),
        heldBlock == 0 ? "" : (BlockServes(heldBlock, s.window) ? " (serves this target)"
                                                                : " (out of this target's reach)"),
        (unsigned long long)img.base, img.sizeOfImage,
        img.valid ? (img.dynamicBase ? "yes" : "no") : "unknown (headers unreadable)", refused);
    return buf;
}

// ---- The held block ([MH-PRERESERVE-PROXY]) ----------------------------------------------------------------------
//
// GetMemoryBlock reuses any block on its list that BlockServes the new target and still has a free slot, before it
// allocates, and FreeBuffer releases a block only with its last used slot. So one hook created early on a target
// inside the main image -- and never enabled, never removed -- keeps a block next to the image for the process
// lifetime, and every later hook in the image that the block serves lands in it whatever the heap has done since.

/// The MinHook block a trampoline slot belongs to, the way FreeBuffer finds it: the slot address rounded down to
/// MEMORY_BLOCK_SIZE (buffer.c: `(slot / MEMORY_BLOCK_SIZE) * MEMORY_BLOCK_SIZE`). One place, so the held block and a
/// freshly-installed hook's block are computed by the same rule MinHook frees by.
inline uint64_t BlockOfSlot(uint64_t slot) {
    return slot & ~(kMemoryBlockSize - 1);
}

/// The hook targets a block at `block` serves: BlockServes(block, SearchWindow(origin, ...)) solved for origin.
/// origin <= block + MAX_MEMORY_RANGE keeps minAddr at or below the block; origin >= block + MEMORY_BLOCK_SIZE -
/// MAX_MEMORY_RANGE keeps maxAddr above it. `empty` when no origin is served (a block outside the application range).
struct Span {
    uint64_t lo = 0;
    uint64_t hi = 0;     // inclusive
    bool empty = true;
};
inline Span OriginsServedBy(uint64_t block, uint64_t appMin, uint64_t appMax) {
    Span s;
    if (block < appMin || appMax < kMemoryBlockSize - 1 || block >= appMax - (kMemoryBlockSize - 1)) return s;
    s.lo = (block + kMemoryBlockSize > kMaxMemoryRange) ? block + kMemoryBlockSize - kMaxMemoryRange : 0;
    s.hi = block + kMaxMemoryRange;
    s.empty = false;
    return s;
}

/// A section's mapped extent: the loader maps VirtualSize (some linkers leave it 0, then SizeOfRawData stands in),
/// clipped to SizeOfImage.
inline uint64_t SectionSpanEnd(const ImageLayout& img, const Section& s) {
    const uint64_t size = s.virtualSize ? s.virtualSize : s.rawSize;
    const uint64_t end  = (uint64_t)s.rva + size;
    return end < img.sizeOfImage ? end : img.sizeOfImage;
}

/// [lo, hi) spanning every executable section, as absolute addresses; lo == hi when none is marked executable.
struct ExecRange {
    uint64_t lo = 0;
    uint64_t hi = 0;
};
inline ExecRange ExecutableRange(const ImageLayout& img) {
    ExecRange r;
    bool any = false;
    for (const Section& s : img.sections) {
        if (!(s.characteristics & kScnMemExecute)) continue;
        const uint64_t end = SectionSpanEnd(img, s);
        if (end <= s.rva) continue;
        const uint64_t lo = img.base + s.rva, hi = img.base + end;
        if (!any || lo < r.lo) r.lo = lo;
        if (!any || hi > r.hi) r.hi = hi;
        any = true;
    }
    return r;
}

/// True when the block whose served span is `served` serves every address in [range.lo, range.hi).
inline bool SpanCovers(const Span& served, const ExecRange& range) {
    if (range.hi <= range.lo || served.empty) return false;
    return served.lo <= range.lo && range.hi - 1 <= served.hi;
}

/// Where the holder hook goes, best first. MH_CreateHook only READS its target (CreateTrampolineFunction copies the
/// first instructions into the slot) and only MH_EnableHook writes it, so the target's code is never changed -- but
/// the address is taken in MinHook's table, so it has to be one no real hook will want. The entry point is the CRT
/// startup routine, never a UFunction or UObject::ProcessEvent; a section start is the fallback for an entry point
/// MinHook cannot trampoline (a protector stub that opens on a short jump). Stark steps the holder aside if a real
/// hook ever asks for its address anyway.
enum class HolderKind { EntryPoint, SectionStart };

struct HolderCandidate {
    uint64_t   address = 0;
    HolderKind kind = HolderKind::EntryPoint;
};

inline const char* HolderKindName(HolderKind k) {
    return k == HolderKind::EntryPoint ? "the image's entry point" : "the start of an executable section";
}

inline std::vector<HolderCandidate> ChooseHolderTargets(const ImageLayout& img, size_t maxCandidates = 4) {
    std::vector<HolderCandidate> out;
    if (!img.valid) return out;
    auto add = [&](uint64_t addr, HolderKind kind) {
        if (out.size() >= maxCandidates) return;
        for (const HolderCandidate& c : out) if (c.address == addr) return;
        out.push_back({addr, kind});
    };
    if (img.entryRva != 0 && img.entryRva < img.sizeOfImage) add(img.base + img.entryRva, HolderKind::EntryPoint);
    for (const Section& s : img.sections) {
        if (!(s.characteristics & kScnMemExecute)) continue;
        if (SectionSpanEnd(img, s) <= s.rva) continue;
        add(img.base + s.rva, HolderKind::SectionStart);
    }
    return out;
}

/// MinHook reads a holder target with no SEH, inside its own spin lock: a fault there kills the game, and a caught
/// one would leave the lock taken for every later MinHook call. So a candidate is used only where every byte
/// MinHook reads is readable. Its IsExecutableAddress accepts any PAGE_EXECUTE_* page, execute-only PAGE_EXECUTE
/// included, and a guard page faults on its first read.
inline bool ProtectIsExecutable(uint32_t protect) {
    return (protect & (kPageExecute | kPageExecuteRead | kPageExecuteReadWrite | kPageExecuteWriteCopy)) != 0;
}
inline bool ProtectIsReadableCode(uint32_t protect) {
    return (protect & kPageGuard) == 0 &&
           (protect & (kPageExecuteRead | kPageExecuteReadWrite | kPageExecuteWriteCopy)) != 0;
}

// How far past a holder target MinHook reads, with no SEH of its own. CreateTrampolineFunction decodes the prologue
// instruction by instruction until the bytes copied reach sizeof(JMP_REL) == 5; the LAST instruction it disassembles
// therefore starts at an offset of at most 4 + 15 (the longest x86-64 instruction can precede it) = 19, and hde64
// then reads up to ~33 bytes of THAT instruction -- up to 16 legacy prefixes, a REX byte, a 1-2 byte opcode, ModRM,
// SIB, a 4-byte displacement and an 8-byte immediate -- before it caps the reported length at 15. 19 + 33 = 52;
// rounded up to a tidy 64 for margin. HolderCandidateSafe queries the page of target + kHolderReadSpan - 1 and
// SEH-probes this many bytes, so a candidate whose decode reach crosses into an unreadable page is refused, not
// faulted on inside MinHook's spin lock.
constexpr uint64_t kHolderReadSpan = 64;

/// at / end: the pages holding the candidate and the last byte MinHook can decode (target + kHolderReadSpan - 1;
/// CreateTrampolineFunction decodes forward from the target); before: the page holding target - 5, which MinHook
/// reads only when its IsExecutableAddress calls it executable (the hot-patch path).
inline bool HolderCandidateReadable(uint32_t stateAt, uint32_t protectAt, uint32_t stateEnd, uint32_t protectEnd,
                                    uint32_t stateBefore, uint32_t protectBefore) {
    if (stateAt != kMemCommit || !ProtectIsReadableCode(protectAt)) return false;
    if (stateEnd != kMemCommit || !ProtectIsReadableCode(protectEnd)) return false;
    if (stateBefore == kMemCommit && ProtectIsExecutable(protectBefore) && !ProtectIsReadableCode(protectBefore))
        return false;
    return true;
}

} // namespace Lager
