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

} // namespace Lineal
