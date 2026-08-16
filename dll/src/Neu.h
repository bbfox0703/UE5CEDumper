// ============================================================
// Neu — 諾伊 ("New" / the scout who uncovers the disguised)
// EnumNames: parse UEnum::Names, telling apart the two on-disk layouts that
// occupy the SAME UEnum offset, and extract the (FName comparison-index, value)
// pairs of an enum's members.
//
//   Legacy (UE4.x .. UE5.5):  Names is a TArray<TPair<FName,int64>>
//       +0x00  FName/int64 pairs Data*        (name + value INTERLEAVED)
//       +0x08  int32 ArrayNum
//       +0x0C  int32 ArrayMax
//     entry i @ Data + i*(sizeof(FName)+8):  FName @ +0, int64 value @ +sizeof(FName)
//
//   FNameData57 (UE5.6+):     Names is a struct-of-arrays (UEnum::FNameData)
//       +0x00  UPTRINT TaggedNames    (tagged FName*  — mask &~1)   names array
//       +0x08  UPTRINT TaggedValues   (tagged int64*  — mask &~1)   values array
//       +0x10  int32   NumValues
//     name i  @ (TaggedNames &~1) + i*sizeof(FName)   (FName @ +0)
//     value i @ (TaggedValues&~1) + i*8
//
// The UE5.6+ container is "disguised" at the old offset: a single-format reader
// mis-reads TaggedValues (a pointer) as ArrayNum (a count) and fails — this is
// what broke stock UE5.7 enum name resolution (Solarpunk, ~46% names). The whole
// job here is to disambiguate the two and read the right one.
//
// Disambiguation: a real ArrayNum is a small int (< maxCount) and is NEVER a
// readable heap pointer; TaggedValues IS a readable heap pointer. So if the word
// at +0x08 masks to a readable pointer AND +0x10 holds a plausible count, it is
// the new container; otherwise low32(+0x08) is the legacy ArrayNum. Both
// hypotheses additionally require their data pointer(s) to be readable, which
// rejects the rare case where a legacy ArrayNum|ArrayMax word happens to fall in
// the pointer numeric range (the masked value then points at unmapped memory).
//
// Dependency-free (only <cstdint>) and memory access goes through an injected
// read functor, so format detection + extraction are unit-testable with
// synthetic buffers and WITHOUT a live process or FNamePool. FName -> string
// resolution stays in Serie/Ubel (needs the live pool); Neu only yields the raw
// FName comparison-index + int64 value.
// ============================================================

#pragma once

#include <cstdint>

namespace Neu {

enum class EnumNamesFormat : uint8_t {
    Legacy,        // TArray<TPair<FName,int64>>  (UE4.x .. UE5.5)
    FNameData57,   // UEnum::FNameData struct-of-arrays  (UE5.6+)
};

// Parsed header of the UEnum::Names region (the words at enumAddr + namesOffset).
struct EnumNamesLayout {
    EnumNamesFormat format      = EnumNamesFormat::Legacy;
    uintptr_t       namesPtr    = 0;   // FName[] base, tag bit already masked off
    uintptr_t       valuesPtr   = 0;   // int64[] base (FNameData57 only; 0 for Legacy)
    int32_t         count       = 0;   // number of enum members
    int             fnameStride = 8;   // bytes between FName entries (8 normal / 0x10 CasePreserving)
};

// Tag bit on the FNameData pointers: 1 = dynamically allocated FName array (the
// live/runtime state). 0 = still the compiled-in static UTF8 string table, which
// is a transient construction-time state we never expect to observe in a running
// shipping game (and cannot resolve as FNames). Mask is ~1 either way.
inline constexpr uint64_t kTagBit = 0x1ull;

// x64 user-space heap-pointer plausibility (non-null, below the canonical split).
inline bool LooksLikePtr(uint64_t p) noexcept {
    return p >= 0x10000ull && p <= 0x7FFFFFFFFFFFull;
}

// Parse the UEnum::Names region at `regionAddr` assuming a KNOWN `format` (the
// per-game format established once by DetectLayout). This is what the live enum
// reader uses — no guessing, so it can't mis-pick a format on an arbitrary enum.
//   read(addr, out, n) -> bool : copy n bytes from game memory; false on fault.
//   fnameStride                : sizeof(FName) for this game (8, or 0x10 CasePreserving).
//   maxCount                   : upper bound on a plausible enum member count (e.g. 16384).
// Returns true + fills `out` when the region parses sanely under `format`.
template <typename ReadFn>
inline bool BuildLayout(ReadFn&& read, uintptr_t regionAddr, EnumNamesFormat format,
                        int fnameStride, int32_t maxCount, EnumNamesLayout& out) noexcept {
    uint64_t w0 = 0, w1 = 0;
    if (!read(regionAddr + 0x00, &w0, sizeof(w0))) return false;
    if (!read(regionAddr + 0x08, &w1, sizeof(w1))) return false;

    if (format == EnumNamesFormat::FNameData57) {
        // +0x00 tagged FName*, +0x08 tagged int64*, +0x10 int32 NumValues.
        const uintptr_t nPtr = static_cast<uintptr_t>(w0 & ~kTagBit);
        const uintptr_t vPtr = static_cast<uintptr_t>(w1 & ~kTagBit);
        if (!LooksLikePtr(nPtr) || !LooksLikePtr(vPtr)) return false;
        uint32_t numNew = 0;
        int32_t  probeName = 0;
        int64_t  probeVal  = 0;
        if (!read(regionAddr + 0x10, &numNew, sizeof(numNew))) return false;
        // Range-check BEFORE the narrowing cast, in a type that can hold every input.
        // `static_cast<int32_t>(numNew) > maxCount` let the whole upper half of the
        // uint32 range through: 0x80000000 casts to -2147483648, which is not greater
        // than maxCount, so the guard passed and `out.count` below became NEGATIVE. The
        // Legacy branch never had this because its `num <= 0` test catches the wrapped
        // value; this branch only tested `== 0`. (audit #5 AF1)
        if (numNew == 0 || static_cast<int64_t>(numNew) > static_cast<int64_t>(maxCount))
            return false;
        // Both arrays must be readable (rejects a Legacy Num|Max word that merely
        // looks pointer-shaped — its masked value points at unmapped memory).
        if (!read(nPtr, &probeName, sizeof(probeName))) return false;
        if (!read(vPtr, &probeVal,  sizeof(probeVal)))  return false;
        out.format      = EnumNamesFormat::FNameData57;
        out.namesPtr    = nPtr;
        out.valuesPtr   = vPtr;
        out.count       = static_cast<int32_t>(numNew);
        out.fnameStride = fnameStride;
        return true;
    }

    // Legacy TArray<TPair<FName,int64>>: +0x00 = Data*, low32(+0x08) = Num.
    const int32_t num = static_cast<int32_t>(w1 & 0xFFFFFFFFull);
    if (!LooksLikePtr(w0) || num <= 0 || num > maxCount) return false;
    int32_t probeName = 0;
    if (!read(static_cast<uintptr_t>(w0), &probeName, sizeof(probeName))) return false;
    out.format      = EnumNamesFormat::Legacy;
    out.namesPtr    = static_cast<uintptr_t>(w0);
    out.valuesPtr   = 0;
    out.count       = num;
    out.fnameStride = fnameStride;
    return true;
}

// Decide the layout of the UEnum::Names region at `regionAddr` by trying both
// formats. New-container hypothesis is tried first because its signature (a
// readable pointer where Legacy keeps a small count) is unambiguous once
// dereferenced. Used at DETECTION time on known enums; the caller still confirms
// by resolving member FName strings. Returns the winning format in `out.format`.
template <typename ReadFn>
inline bool DetectLayout(ReadFn&& read, uintptr_t regionAddr, int fnameStride,
                         int32_t maxCount, EnumNamesLayout& out) noexcept {
    if (BuildLayout(read, regionAddr, EnumNamesFormat::FNameData57, fnameStride, maxCount, out))
        return true;
    if (BuildLayout(read, regionAddr, EnumNamesFormat::Legacy, fnameStride, maxCount, out))
        return true;
    return false;
}

// Read the i-th (FName comparison-index, enum value) pair for a parsed layout.
// Returns false on a bad index or a memory fault.
template <typename ReadFn>
inline bool ReadEntry(ReadFn&& read, const EnumNamesLayout& L, int32_t i,
                      int32_t& nameIndex, int64_t& value) noexcept {
    if (i < 0 || i >= L.count) return false;
    if (L.format == EnumNamesFormat::FNameData57) {
        const uintptr_t nameAddr = L.namesPtr  + static_cast<uintptr_t>(i) * static_cast<uintptr_t>(L.fnameStride);
        const uintptr_t valAddr  = L.valuesPtr + static_cast<uintptr_t>(i) * 8u;
        if (!read(nameAddr, &nameIndex, sizeof(nameIndex))) return false;
        if (!read(valAddr,  &value,     sizeof(value)))     return false;
        return true;
    }
    // Legacy: interleaved TPair<FName,int64>. Value sits right after the FName,
    // so the per-entry stride is sizeof(FName)+8 and the value is at +sizeof(FName).
    const uintptr_t entryStride = static_cast<uintptr_t>(L.fnameStride) + 8u;
    const uintptr_t entryAddr   = L.namesPtr + static_cast<uintptr_t>(i) * entryStride;
    if (!read(entryAddr, &nameIndex, sizeof(nameIndex))) return false;
    if (!read(entryAddr + static_cast<uintptr_t>(L.fnameStride), &value, sizeof(value))) return false;
    return true;
}

} // namespace Neu
