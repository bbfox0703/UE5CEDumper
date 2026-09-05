// ============================================================
// Lineal — 莉涅爾 ("Ruler / Straightedge" — layout / alignment)
// FUObjectItem packing: UE5.7+ packed-pointer split / rejoin reconstruction
//
// UE 5.7 added an optional packed FUObjectItem encoding gated by
// UE_ENABLE_FUOBJECT_ITEM_PACKING (slated to become the future default; NOT
// Epic-default even in ue5-main today). In packed mode the UObject* is no
// longer stored directly inside the item — it is split across two fields and
// must be reconstructed:
//
//     item +0x00  int64  FlagsAndRefCount   (high 32 bits = flags + TOP pointer bits)
//     item +0x08  uint32 ObjectPtrLow       ((ptr >> AlignBits) low 32 bits)
//
//     obj = ((FlagsAndRefCount >> 32) & PtrMask) << (32 + AlignBits)
//         | (uint64(ObjectPtrLow) << AlignBits)
//
// Constants (assumed against EpicGames/UnrealEngine 5.7 source; CALIBRATABLE at
// runtime via Aura::SetPackedConsts / the set_packed_consts pipe command because
// no shipping game uses this layout yet to live-verify them):
//   AlignBits = 3   (UObjectAlignment == 8  -> 3 trailing zero bits)
//   PtrMask   = 0x3FFF (EInternalObjectFlags_MinFlagBitIndex == 14 -> low 14 bits)
//
// *** UNVERIFIED ***: this whole encoding has never been validated against a real
// packed game. The reconstruction MATH below is unit-tested for round-trip
// correctness (Lineal.h has no external dependency so dll_helpers_test can
// include it directly), but the constants and the in-memory item layout are
// best-effort until a packed game exists to calibrate against.
//
// Dependency-free on purpose (only <cstdint>) so it stays unit-testable.
// ============================================================

#pragma once

#include <cstdint>

namespace Lineal {

// Which in-memory FUObjectItem layout Aura detected for the live game.
//   Classic    — UObject* at item+0x00            (UE4.x .. UE5.6)
//   Unpacked57 — FlagsAndRefCount@+0x00, Object*@+0x08 (UE5.7+ reordered, direct ptr)
//   Packed57   — Object* split across two fields, reconstructed via Reconstruct()
enum class ItemLayoutMode { Classic, Unpacked57, Packed57 };

// Calibratable constants for the packed reconstruction. Defaults match the
// assumed UE5.7 source values; SetPackedConsts can override them at runtime.
struct PackedConsts {
    int      alignBits   = 3;        // UObjectAlignment == 8
    uint64_t ptrMaskBits = 0x3FFFull; // low 14 bits of the high dword
};

// Reconstruct the real UObject* from the two packed fields. Pure: no memory
// access, no side effects. Returns 0 when ptrLow is 0 (empty/null slot).
inline uintptr_t Reconstruct(uint64_t flagsAndRefCount, uint32_t ptrLow,
                             const PackedConsts& c) noexcept {
    if (ptrLow == 0) return 0;  // null slot — keep the caller's "0 == empty" contract
    const uint64_t hi = ((flagsAndRefCount >> 32) & c.ptrMaskBits)
                        << (32 + c.alignBits);
    const uint64_t lo = static_cast<uint64_t>(ptrLow) << c.alignBits;
    return static_cast<uintptr_t>(hi | lo);
}

// Inverse of Reconstruct — for UNIT TESTS ONLY. Splits an aligned pointer back
// into the two packed fields so a round trip is assertable without a live game.
// `flagsExtra` seeds the low 32 bits of FlagsAndRefCount (the real flag/refcount
// bits) to prove they never leak into the reconstructed pointer.
//
// Precondition: `obj` must be aligned to (1 << alignBits) and its top bits must
// fit in ptrMaskBits — true for all real UObject pointers on x64 user space.
inline void Encode(uintptr_t obj, const PackedConsts& c,
                   uint64_t& flagsAndRefCount, uint32_t& ptrLow,
                   uint64_t flagsExtra = 0) noexcept {
    ptrLow = static_cast<uint32_t>(
        (static_cast<uint64_t>(obj) >> c.alignBits) & 0xFFFFFFFFull);
    const uint64_t hiPtrBits =
        (static_cast<uint64_t>(obj) >> (32 + c.alignBits)) & c.ptrMaskBits;
    flagsAndRefCount = (hiPtrBits << 32) | (flagsExtra & 0xFFFFFFFFull);
}

// Byte offset of FUObjectItem::SerialNumber for a given item layout. Pure: no
// memory access, no globals — so it is unit-testable even though no target
// compiles Aura.cpp, which is its only caller.
//
// The offset is decided by the STRIDE, and the reachable stride set is exactly
// {16, 20, 24, 32} — `Aura`'s auto-probe tries {16, 24, 32, 20} and
// `UE5_InitWithExtendedLayout` forces any of {0x14, 0x18, 0x10, 0x20}. It used
// to be computed inline as `s_itemSize >= 24 ? 0x10 : 0x0C`, a two-way split
// that only covers 16 and 24 (audit #5 A1):
//
//   16  Object(8) + Flags(4) + Serial(4)                        -> 0x0C
//   20  Object(8) + Flags(4) + ClusterRootIndex(4) + Serial(4)  -> 0x10
//   24  as 16/20 plus stats padding                             -> 0x10
//   32  classic + stats growth appended after the serial        -> 0x10
//
// **Stride 20 is the packed FUObjectItem Avowed ships**, and it is reachable by
// both routes above. The old expression returned 0x0C for it, which reads
// `ClusterRootIndex` — so `Ubel::ResolveWeakObjectPtr`'s bare
// `if (actualSerial != serialNumber) return 0;` declared EVERY weak reference
// stale, silently: no fallback, no retry, no log. That nulls WeakObjectProperty
// and the whole delegate family outright, and costs Soft/Lazy handlers their
// resolved live object (they still show the asset path).
//
// Offsets are from the Ghidra decompilation of Avowed's AllocateUObjectIndex,
// recorded in docs/avowed-gobjects-fix.md: {Object@+0x00, Flags@+0x08,
// ClusterRoot@+0x0C, Serial@+0x10}.
//
// NOTE this is UE's own FWeakObjectPtr::SerialNumbersMatch check — reading the
// serial at the right ADDRESS. It is NOT the "invent (index, serial) as a
// recycle witness" proposal that working-lessons §4.3 refuses three times over;
// that one is about a passive observer STORING a serial UE allocates lazily.
inline int SerialOffsetForLayout(ItemLayoutMode mode, int itemSize,
                                 int objOffset, int packedSerialOff) noexcept {
    // Packed UE5.7+: position is *** UNVERIFIED ***, calibratable at runtime via
    // set_packed_consts serial_off. A wrong value degrades only weak-ref
    // staleness resolution, never the core object walk.
    if (mode == ItemLayoutMode::Packed57) return packedSerialOff;

    // Unpacked UE5.7+: FlagsAndRefCount(8) + Object(8) + SerialNumber(4) +
    // ClusterRootIndex(4) — the serial sits immediately after the object.
    if (objOffset != 0) return objOffset + 0x08;

    // Classic. Only the 16-byte item has no ClusterRootIndex ahead of the
    // serial, so it is the sole case that stays at 0x0C.
    return (itemSize <= 16) ? 0x0C : 0x10;
}

// ============================================================
// Stride-sweep scoring, in the header so the tests can pin the RULE and not just
// the candidate list. Aura::ProbeStride classifies each probe as good/named/null/bad
// and these two decide the winner.
// ============================================================

// A stride that DIVIDES the real item size still lands on a genuine object every k-th
// probe, so raw hit counts cannot separate it from the truth. Two things do:
//   * it lands OFF-item the rest of the time, which usually reads garbage -> `bad`
//   * even when it does not, it can only score ~1/k of the named count the real stride does
// Hence: named dominates, bad is punished hard.
inline constexpr int StrideScore(int named, int good, int bad) noexcept {
    if (named > 0) return named * 10 - bad * 3;
    if (good  > 0) return good  * 5  - bad * 2;
    return -bad;
}

// ⚠ Ties are REAL and the list order must not be what breaks them. Every candidate is
// probed against the same NUMBER of items (not the same byte range), so a stride and any
// MULTIPLE of it both read only valid objects on a correct pool and score identically --
// true of 16/32 long before 40 existed. In that tie the SMALLER stride is the true item
// size; the larger merely samples every k-th item. The reverse never ties: a DIVISOR of
// the true stride lands off-item on most probes and loses on named, or on bad.
//   bestStride == 0 means "nothing chosen yet".
inline constexpr bool PreferStride(int score, int bestScore, int stride, int bestStride) noexcept {
    return score > bestScore || (score == bestScore && bestStride != 0 && stride < bestStride);
}

// The FUObjectItem sizes the auto-probe sweeps. Here rather than as a local in Aura.cpp
// so a test can pin the SET -- the supporting rules being correct is no use if the true
// size is not a candidate at all, which is the entire content of both A5 and its 2026-08-05
// predecessor.
//   16  UE5 classic
//   24  UE4 classic / UE5.7+ reordered (Object @ +0x08)
//   32  UE 5.4 Development (STATS=1 adds TStatId)
//   20  Avowed's packed 20-byte item
//   40  UE 5.7+ TEST build (ENABLE_STATNAMEDEVENTS_UOBJECT adds TStatId + StatIDStringStorage)
inline constexpr int kItemStrideCandidates[] = { 16, 24, 32, 20, 40 };

// The gate that decides whether a winning stride is trusted OUTRIGHT or only
// tentatively. ⚠ This -- not the score -- is what the two alias shapes separate on:
//   * an alias landing on GARBAGE  : named ~= bad -> FAILS here -> tentative path
//   * an alias landing on a NULL   : named ~= half, bad == 0 -> PASSES -> confident
// which is exactly why the 40-byte Test-build item was silent where the 32-byte one
// was merely wrong. Note both still score POSITIVE (StrideScore(100,100,100) = 700),
// so the score alone never told them apart.
inline constexpr bool StrideQualityOk(int named, int bad) noexcept {
    return named == 0 || named > bad;   // no names at all -> no name-based confidence to check
}

} // namespace Lineal
