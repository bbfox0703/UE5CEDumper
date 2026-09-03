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
// Pure index-geometry helpers (unit-pinned in dll_helpers_test). These were written
// when no target compiled Serie.cpp; since build 3355 (`373f3083`) dll_core_test does
// (`#include "../src/Serie.cpp"`), so a rule may now be pinned in EITHER place — put
// pure arithmetic here, anything needing a live pool in dll_core_test.
// ============================================================

// FNamePool entry geometry for a candidate FNameBlockOffsetBits width: the entry for
// `nameIndex` sits in chunk `nameIndex >> bits`, at byte offset
// `(nameIndex & ((1<<bits)-1)) * stride` within that chunk.
struct BlockProbe { int32_t chunkIndex; int32_t chunkOffset; };
inline BlockProbe ComputeBlockProbe(int32_t nameIndex, int bits, int stride) {
    const int32_t mask = (bits >= 31) ? 0x7FFFFFFF : ((1 << bits) - 1);
    return { nameIndex >> bits, (nameIndex & mask) * stride };
}

// The lowest NON-ZERO FName index that is a real entry BOUNDARY, derived from the
// geometry we measured rather than guessed.
//
// ⚠⚠ An FName index is a STRIDE-QUANTISED BYTE OFFSET inside its block, not an entry
// ordinal — `FNameEntryHandle(CurrentBlock, ByteOffset / Stride)`, and `Resolve` is a
// bare `Blocks[Block] + Stride * Offset` with NO validity check (vendored
// Core/Private/UObject/UnrealNames.cpp:592 and :679). So an index that lands mid-entry
// silently returns a pointer into the MIDDLE of a real entry and decodes as garbage.
// Entry 0 is always "None" (`REGISTER_NAME(0,None)`, UnrealNames.inl:12) and occupies
// `prefix + 4` bytes, which is ALWAYS more than one stride unit — so **index 1 is never
// an entry boundary on any layout we support**, and probing it can only ever fail.
// That is why `FName[1]` came back empty in every log this repo has ever produced.
//
// Verified against block[0], byte-identical on five games / two engine generations:
//   1E 01 | 4E 6F 6E 65 | 10 03 | 42 79 74 65 50 72 6F 70 65 72 74 79 | C0 02 | 49 6E 74…
//   hdr=4 |  N  o  n  e | hdr=12|  B  y  t  e  P  r  o  p  e  r  t  y | hdr=11|  I  n  t
//   idx 0 ─────────────── idx 3 ─────────────────────────────────────── idx 10 ─────────
//
// ⚠ FNamePool ONLY. In pre-FNamePool `TNameEntryArray` mode (Serie::InitUE4) an index IS
// a pointer-array ordinal, so index 1 there is a genuine second entry — `InitUE4` samples
// it correctly and must NOT be "fixed" to match this.
//
// `noneOffset` is where the CHARACTERS of entry 0 start (what DetectStride measures):
// 2 stock, 6 hash-prefixed, 2+payloadGap on an obfuscated fork. Returns -1 when the
// geometry was never established, so the caller reports "unknown" instead of probing.
// ⚠ Do NOT replace this with a literal 3: it is 3 for BOTH stock layouts, which is
// exactly what makes a hardcoded 3 look safe — but a MindsEye-style `payloadGap = 2`
// fork gives 4, and a literal there would be the index-1 bug again with a new number.
inline int32_t FirstEntryAfterNone(int noneOffset, int stride) {
    if (stride <= 0 || noneOffset < 0) return -1;
    const int bytes   = noneOffset + 4;  // entry 0 = prefix + "None"
    const int aligned = ((bytes + stride - 1) / stride) * stride;
    return aligned / stride;
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
