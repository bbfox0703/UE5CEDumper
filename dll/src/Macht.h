#pragma once

// ============================================================
// Macht — 黃金鄉馬哈特 (萬物成金魔法 — Seven Sages, Transmutation)
// Memory: AOB scanning, SEH-protected reads, RIP-relative resolution
// ============================================================

#include <Windows.h>
#include <cstdint>
#include <vector>

namespace Macht {

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

// Check if an address is in committed, readable memory via VirtualQuery.
// Useful as a cheap pre-flight before walking potentially freed objects —
// ReadSafe catches AVs but cannot protect walk loops from garbage-induced
// infinite traversal when memory has been reused for valid-looking data.
bool IsAddrReadable(uintptr_t addr, size_t size = sizeof(uintptr_t));

// Write bytes to memory
bool WriteBytes(uintptr_t addr, const void* buf, size_t size);

// Get base address of a loaded module (nullptr = main module)
uintptr_t GetModuleBase(const wchar_t* moduleName = nullptr);

// Get module size
size_t GetModuleSize(const wchar_t* moduleName = nullptr);

// True when `addr` points into a committed, executable page of a mapped module
// image (MEM_IMAGE + PAGE_EXECUTE*). Used to validate UFunction::Func candidates
// during Path-2 offset detection — a native exec thunk lives in a module's
// .text, so the correct Func offset holds such a pointer for every native func.
bool LooksLikeCodePointer(uintptr_t addr);

// Exact [begin, end) of the x64 function containing `addr`, from the PE exception
// directory. Returns false for a genuine LEAF function (no unwind data) and for a
// binary whose exception directory is absent or stripped — the two are not
// distinguishable, so callers must treat false as "unknown extent", not "no code".
//
// Uses RtlLookupFunctionEntry rather than parsing .pdata by hand, for two measured
// reasons. It is MODULE-AWARE: its ImageBase out-param maps an arbitrary address
// back to its own module, which matters because a symbol-export lookup can land in
// a DLL other than the main image, and every other NT-header walk in this codebase
// is hardcoded to the main module. And it does not depend on SECTION NAMES: DQ7R
// ships no `.pdata` and no `.text` at all (its exception directory sits in
// `.srdata`, its code in `.debug`), and Elliot / Stellar Blade / Hogwarts are each
// named differently again — yet all expose a working IMAGE_DIRECTORY_ENTRY_EXCEPTION.
//
// NOTE `addr` may be MID-function (measured: 6.8%-39.8% of call targets across the
// corpus land where the covering entry's BeginAddress != addr), so clamp a scan with
// `end - addr`, NEVER with `end - begin`.
bool GetFunctionExtent(uintptr_t addr, uintptr_t& begin, uintptr_t& end);

// --- AOB pattern scanning ---

// Parsed pattern: pre-processed bytes/mask for efficient SIMD scanning.
struct ParsedPattern {
    std::vector<uint8_t> bytes;   // pre-masked expected value: (mem[j] & mask[j]) == bytes[j]
    std::vector<uint8_t> mask;    // per-byte AND-mask: 0x00 wildcard, 0x0F/0xF0 nibble, 0xFF literal
    uint8_t  firstByte    = 0;
    bool     firstIsFixed = false; // true when bytes[0] is a full literal (mask 0xFF)
    int      anchorOffset = -1;   // first FULL-literal byte index (SIMD anchor; nibbles can't anchor)
    uint8_t  anchorByte   = 0;    // value at anchorOffset
};

// Parse an AOB pattern string into a ParsedPattern.
// Pattern format: "48 8B 05 ?? ?? ?? ??" where ?? is a full-byte wildcard.
// Nibble wildcards are supported: "4?" fixes the high nibble (matches 40-4F),
// "?5" fixes the low nibble. A token needs at least one fixed nibble to constrain.
// Inline (pure — no Win32) so the leaf unit test can exercise the real parser.
inline bool ParsePattern(const char* patStr, ParsedPattern& out) {
    out.bytes.clear();
    out.mask.clear();

    auto nib = [](char c) -> int {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    };

    const char* p = patStr;
    while (*p) {
        while (*p == ' ' || *p == '\t') ++p;
        if (!p[0] || !p[1]) break; // Need at least 2 chars for a byte/nibble token

        // Each token is 2 chars: ?? (full wildcard), ?X / X? (nibble), or XX (literal).
        const char hiC = p[0], loC = p[1];
        const bool hiW = (hiC == '?'), loW = (loC == '?');
        uint8_t mask = 0, val = 0;
        if (!hiW) {
            int h = nib(hiC);
            if (h < 0) return false;                 // malformed token
            mask |= 0xF0; val |= static_cast<uint8_t>(h << 4);
        }
        if (!loW) {
            int l = nib(loC);
            if (l < 0) return false;
            mask |= 0x0F; val |= static_cast<uint8_t>(l);
        }
        out.mask.push_back(mask);          // 0x00 ?? / 0x0F ?X / 0xF0 X? / 0xFF XX
        out.bytes.push_back(val & mask);   // pre-masked so (mem & mask) == bytes compares cleanly
        p += 2;
    }

    if (out.bytes.empty()) return false;
    out.firstByte    = out.bytes[0];
    out.firstIsFixed = (out.mask[0] == 0xFF);

    // Find anchor: first FULL-literal byte (mask 0xFF) for AVX2 SIMD acceleration.
    // Unlike memchr (always byte 0), the anchor can be at any position, enabling
    // fast-skip for wildcard-prefixed patterns like "?? ?? 48 8B". Nibble-masked
    // bytes can't be broadcast-compared so they never anchor; a pattern with no
    // full literal (anchorOffset stays -1) falls through to the scalar path.
    out.anchorOffset = -1;
    for (size_t i = 0; i < out.mask.size(); ++i) {
        if (out.mask[i] == 0xFF) {
            out.anchorOffset = static_cast<int>(i);
            out.anchorByte   = out.bytes[i];
            break;
        }
    }
    return true;
}

// ── Why this module carries NO Tot::Requested() poll (audit #5 MA1) ──────────
//
// Recorded here because the absence has now been raised three times, and refuted
// once for the WRONG reason. To be exact: it is not that the `__try`/`__except`
// blocks are cancellation — there is no `__try` in any scan core at all. The four
// SEH sites are ReadSafe / ReadBytesSafe / WriteBytes / GetFunctionExtent; the
// scan cores dereference the image with no guard of any kind.
//
// Cancellation lives one level up, at the PATTERN boundary in
// Genau::ScanForTarget (its batch loop and its multi-module Pass 2). That is
// where it belongs because the largest indivisible unit below this line is one
// AOBScanBatch — measured at most 0.64 s on a 213 MB .text — or one
// AOBScanAllModules — measured at most 2.34 s on a 593-module title. Both sit
// inside CE's 5000 ms call ceiling, so a poll here would buy no measurable
// responsiveness while costing a relaxed atomic load per 32-byte AVX2 stride, in
// a file NO test target compiles.
//
// ⚠ If a poll is ever added here anyway, it MUST discard the partial result —
// return 0 / an EMPTY vector, never what was found so far. Two callers read these
// lists as complete: Genau::ResolveNameKeyTable tests `hits.size() != 1` as a
// UNIQUENESS check (a truncated list reads as "unique"), and Pass 2 anchor-sorts
// the full match set. Returning partials there is silently wrong, not merely
// incomplete.
//
// AOB (Array of Bytes) pattern scan in module memory
// Returns first match address, or 0 on failure.
// ⚠ FIRST match only. If the caller needs the match that VALIDATES rather than
// the one that comes first, use AOBScanAll and walk it — a pattern can have
// hundreds of hits with the real site nowhere near the front (GNAM_V1: 166).
// Using this where the batch path uses AOBScanAll is what made a resolvable
// GNames report "NONE validated" (see Genau::ScanForTarget's hint phase).
uintptr_t AOBScan(const char* pattern, uintptr_t start = 0, size_t size = 0);

// AOBScanAll: returns ALL match addresses for the given pattern.
// Scans executable sections of a specific module (moduleBase=0 → main module).
std::vector<uintptr_t> AOBScanAll(const char* pattern, uintptr_t moduleBase = 0);

// --- Batched AOB scanning (multi-anchor AVX2) ---

// Per-pattern result from a batched scan.
struct BatchScanResult {
    int                     patternIndex;  // Caller-provided index for correlation
    std::vector<uintptr_t>  matches;       // All match addresses for this pattern
};

// Batch scan: scan executable sections ONCE for N patterns simultaneously.
// Each pattern's anchor byte gets its own AVX2 comparison per 32-byte window,
// sharing the single memory pass across all patterns in the batch.
// Returns per-pattern match lists. moduleBase=0 → main module.
std::vector<BatchScanResult> AOBScanBatch(
    const char* const* patterns,   // Array of N pattern strings
    const int*         indices,    // Caller-provided indices for correlation
    int                count,      // Number of patterns in this batch
    uintptr_t          moduleBase = 0);

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

// Is this ModR/M byte the x64 RIP-relative form, i.e. `[rip+disp32]`?
//
// BOTH halves must be tested and three of Genau's five hand-rolled decode loops tested only
// one of them for years. mod is bits 7:6 and r/m is bits 2:0; ONLY mod=00 makes r/m=101 mean
// RIP-relative. With r/m=101 the other three mods are ordinary RBP/R13 forms:
//
//   mod=01   48 8B 4D F8   mov rcx,[rbp-8]        (disp8, not disp32)
//   mod=10   48 8B 8D ..   mov rcx,[rbp+disp32]   (base register, not RIP)
//   mod=11   48 8B C5      mov rax,rbp            (register direct — no memory operand)
//
// A caller that accepts those and then reads an int32 at instr+3 is reading a disp8 plus the
// next instruction's bytes (mod=01), or a register operand (mod=11), and calling the result a
// RIP target. `[rsp+X]` is r/m=100 (SIB) so it never passed the r/m test either way — only
// RBP/R13-as-r/m misfired, which is why this went unnoticed.
//
// Named, single-definition and pure so it can be unit-tested (dll_helpers_test) instead of
// living as five copies of a two-line idiom inside scan loops.
constexpr bool IsRipRelativeModRM(uint8_t modrm) {
    return (modrm & 0x07) == 0x05     // r/m = 101
        && (modrm & 0xC0) == 0x00;    // mod = 00
}

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
//   +0x30  FirstFreeIndex (int32)
//   +0x34  NumFreeIndices (int32)
// Total size 0x38. Verified against the Everspace 2 UE 5.4 PDB — see the
// layout reference in Aura.cpp (FSparseDelegateStorage::SparseDelegates).
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
    // NumFreeIndices at +0x34. It sits immediately after FirstFreeIndex (+0x30),
    // which ends the 0x38-byte TSparseArray. Reading it at +0x3C lands past the
    // end — in the enclosing TSet's Hash allocator padding, which is zeroed with
    // the UObject and never written — so it always read 0 and every caller
    // computing `MaxIndex - NumFreeIndices` OVER-reported the element count of
    // any container that had ever had an entry removed.
    ReadSafe(addr + 0x34, out.NumFreeIndices);
    return true;
}

// Check if a sparse array index is allocated (bit set in AllocationFlags).
inline bool IsSparseIndexAllocated(const TSparseArrayView& sa, int32_t index) {
    if (index < 0 || index >= sa.numBits) return false;
    int wordIdx = index / 32;
    int bitIdx  = index % 32;
    uint32_t word = 0;
    // The heap allocation WINS whenever it exists. A TBitArray that has grown past
    // 128 bits COPIES the inline words to the heap and then keeps updating only the
    // heap copy — the inline buffer is left frozen at its spill-time contents. So
    // checking `wordIdx < 4` first meant that once a TSet/TMap had ever exceeded 128
    // slots, indices 0..127 were judged from STALE bits: a slot freed after the spill
    // still read as allocated, and callers walked a dead element (a phantom Find-Refs
    // hit, a dead value admitted by ScanForValue).
    //
    // Aura::ResolveTMapBitArrayBase has always applied this rule correctly
    // ("Inline if MaxBits <= 128; heap (secondaryPtr) otherwise"); this is the same
    // rule, and it fixes all 13 Aura call sites and 6 Ubel ones at once.
    if (sa.secondaryData) {
        if (!ReadSafe(sa.secondaryData + wordIdx * 4, word)) return false;
    } else if (wordIdx < 4) {
        word = sa.inlineBits[wordIdx];
    } else {
        return false;
    }
    return (word & (1u << bitIdx)) != 0;
}

// Sanitize an alignment that came from game memory (UScriptStruct::MinAlignment)
// or from a caller. Returns 0 — "unknown, use the default" — for anything that is
// not a power of two in [1, 32].
inline int32_t SanitizeAlign(int32_t align) noexcept {
    if (align <= 0 || align > 32) return 0;
    if ((align & (align - 1)) != 0) return 0;
    return align;
}

// Compute TSetElement stride: { T value; int32 HashNextId; int32 HashIndex; }
//
// The real slot is TSparseArray<TSetElement<T>>'s, whose size is
//     Align(Align(sizeof(T), alignof(T)) + 8, alignof(T))
// The two int32 hash fields are 4-aligned, so alignof(TSetElement<T>) is
// max(alignof(T), 4) and the element must ALSO be padded at the tail so the
// next element starts aligned.
//
// elemAlign: pass alignof(T). For a TMap that is max(alignof(Key), alignof(Value)).
// Omitting it drops the TPair's TRAILING padding and every element after index 0
// is read at a wrong address — TMap<AActor*,float> has an unpadded pair of 12, so
// the old one-argument form returned 20 where the engine strides 24 (and
// TMap<FString,int32> 28 vs 32, TMap<UObject*,uint8> 20 vs 24).
//
// TSet<T> is unaffected: a bare elemSize is already a multiple of alignof(T),
// so the default of 4 reproduces the previous behaviour exactly and the TSet
// call sites need no change.
inline int32_t ComputeSetElementStride(int32_t elemSize, int32_t elemAlign = 0) {
    int32_t a = SanitizeAlign(elemAlign);
    if (a < 4) a = 4;                                  // hash fields are int32-aligned
    int32_t hashStart = (elemSize + a - 1) & ~(a - 1);  // pad T to its own alignment
    return (hashStart + 8 + a - 1) & ~(a - 1);          // then pad the whole slot
}

// Compute the aligned byte offset of Value within a TMap TPair<Key, Value>.
// UE aligns Value to its natural alignment within the pair struct.
// E.g., TPair<uint8, FStruct80> → value starts at offset 8 (not 1).
//
// valueAlign: pass the value property's REAL alignment (Scharf::RequiredAlignment)
// when known. Guessing alignment from size is WRONG for FName / FWeakObjectPtr —
// both are 8 bytes but 4-byte aligned (2× int32), so TPair<uint8 enum, FName>
// puts the FName at +4, not +8. Reading at +8 (and the inflated stride that
// follows) corrupts every element. valueAlign <= 0 falls back to the size guess.
inline int32_t ComputeMapValueOffset(int32_t keySize, int32_t valueSize, int32_t valueAlign = 0) {
    int32_t valAlign = valueAlign;
    if (valAlign <= 0) {
        valAlign = 1;
        if (valueSize >= 8) valAlign = 8;
        else if (valueSize >= 4) valAlign = 4;
        else if (valueSize >= 2) valAlign = 2;
    }
    return (keySize + valAlign - 1) & ~(valAlign - 1);
}

} // namespace Macht
