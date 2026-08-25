#pragma once

// ============================================================
// Serie — 賽莉耶 (千年大魔法使 — Living-History Great Mage)
// FNamePool: FName string resolution (UE5 pool + UE4 TNameEntry)
// ============================================================

#include <cstdint>
#include <string>

namespace Serie {

// Initialize with the FNamePool address found by OffsetFinder (UE4.23+/UE5)
// headerOffset: bytes before the 2-byte header within each FNameEntry.
//   0 = standard (UE5 / most UE4): [2B header][string]
//   4 = hash-prefixed (UE4.26 / FF7Re): [4B hash][2B header][string]
void Init(uintptr_t gnamesAddr, int headerOffset = 0);

// Initialize for a licensee fork that OBFUSCATES the FNameEntry character payload.
// Experimental-gated; only ever reached when Genau proved the format (decoded entry 0
// to exactly "None") and located the fork's key table. No game code is called — the
// table is read directly out of memory.
//   chunksOffset: pool -> Blocks[] offset that Genau proved (MindsEye: 0x10)
//   payloadGap:   extra bytes between the 2-byte header and the characters
//                 (0 = stock layout, 2 = MindsEye's non-stock u16 tag field)
//   keyTableCtx:  the fork's own tag -> XOR key hash map, located statically by Genau
void InitObfuscated(uintptr_t gnamesAddr, int chunksOffset, int payloadGap,
                    uintptr_t keyTableCtx);

// Initialize for UE4 TNameEntryArray mode (UE4 <4.23)
// nameArrayAddr: pointer to the TNameEntryArray chunk pointer array
// stringOffset:  offset within FNameEntry to the null-terminated string (typically 0x10)
void InitUE4(uintptr_t nameArrayAddr, int stringOffset = 0x10);

// Resolve an FName to its string representation
// nameIndex: FName::ComparisonIndex (the main index)
// number:    FName::Number (for _N suffix when > 0)
std::string GetString(int32_t nameIndex, int32_t number = 0);

// Get the raw FNameEntry address for a given index
uintptr_t GetEntry(int32_t nameIndex);

// Check if the pool has been initialized
bool IsInitialized();

// Check if running in UE4 TNameEntryArray mode
bool IsUE4Mode();

// Diagnostic: probe a range of ComparisonIndex values and log failure reasons.
// Logs pool parameters, max valid chunk, and categorizes failures.
// Call when name resolution ratio is unexpectedly low.
void LogDiagnostics();

// ============================================================
// Pure index-geometry helpers (unit-pinned in dll_helpers_test — no target
// compiles Serie.cpp, so the rules that matter live here).
// ============================================================

// FNamePool entry geometry for a candidate FNameBlockOffsetBits width: the entry for
// `nameIndex` sits in chunk `nameIndex >> bits`, at byte offset
// `(nameIndex & ((1<<bits)-1)) * stride` within that chunk.
struct BlockProbe { int32_t chunkIndex; int32_t chunkOffset; };
inline BlockProbe ComputeBlockProbe(int32_t nameIndex, int bits, int stride) {
    const int32_t mask = (bits >= 31) ? 0x7FFFFFFF : ((1 << bits) - 1);
    return { nameIndex >> bits, (nameIndex & mask) * stride };
}

// True when two candidate block-offset-bit widths address the SAME byte for
// `nameIndex` — i.e. a probe at that index CANNOT distinguish the two widths (audit
// #5 G4). At the historical probe index (testIdx = 1) every width < the index's bit
// position collapses to (chunkIndex=0, chunkOffset=1*stride), which is why
// DetectBlockOffsetBits could never pick 14 over the default 16: the 14-bit arm was
// structurally unreachable. A discriminating index must satisfy this == false.
inline bool BlockBitsAreIndistinguishable(int32_t nameIndex, int bitsA, int bitsB, int stride) {
    BlockProbe a = ComputeBlockProbe(nameIndex, bitsA, stride);
    BlockProbe b = ComputeBlockProbe(nameIndex, bitsB, stride);
    return a.chunkIndex == b.chunkIndex && a.chunkOffset == b.chunkOffset;
}

// UE4 TNameEntryArray index bounds (audit #5 G5). A negative ComparisonIndex — e.g. a
// poison 0xFFFFFFFF read into an int32 as -1 — yields chunkIndex 0 and elemIndex -1
// under C++ truncating division, which passes a chunkIndex-only guard and then
// dereferences chunk + (-1)*8 as an FNameEntry* (a fabricated name returned as real).
// The UE5 path is safe because an arithmetic `>>` keeps a negative index negative and
// its `chunkIndex < 0` guard fires; the UE4 `/`,`%` path needs this explicit check.
// `nameIndex >= 0` also guarantees `nameIndex % chunkSize >= 0`, so bounding the index
// bounds both derived indices. chunkSize / maxChunks are the caller's UE4 constants.
inline bool UE4NameIndexInBounds(int32_t nameIndex, int32_t chunkSize, int32_t maxChunks) {
    if (nameIndex < 0 || chunkSize <= 0) return false;
    int32_t chunkIndex = nameIndex / chunkSize;
    return chunkIndex >= 0 && chunkIndex <= maxChunks;
}

} // namespace Serie
