#pragma once

// ============================================================
// Denken — 鄧肯, 著重探知魔法的過程 (Frieren naming map #25, "deep analysis / type inference")
// Native-code disassembly: recover [this + offset] field accesses from a
// native UFunction's x64 machine code using the Zydis decoder. This is the
// engine behind Path 2 (cross-referencing native C++ functions to the
// UPROPERTY fields they read/write — bytecode-blind Path 1 can't see these).
//
// Pure decoder core: all process-memory access goes through a caller-supplied
// MemReader callback, so this TU depends only on Zydis + the standard library
// (no Macht / Win32). That keeps it unit-testable on the host and lets the
// leaf dll_helpers_test link it without the rest of the DLL.
// ============================================================

#include <cstdint>
#include <vector>
#include <functional>

namespace Denken {

// [DENKEN-WRITE-SITES] Most write sites kept per offset: a field written in more places than this is reported with the
// ones through `this` first, then the rest in decode order, and writeSitesTruncated set.
constexpr size_t kMaxWriteSites = 8;

// One `[this + offset]` memory access recovered from native machine code.
// Accesses to the same displacement are merged; highConfidence is OR-ed, so
// a single provably-`this` access at an offset marks it high-confidence.
struct NativeFieldAccess {
    uint32_t offset         = 0;  // displacement from the this pointer
    uint8_t  accessSize     = 0;  // 1/2/4/8/16 bytes (0 = unknown)
    int32_t  occurrences    = 0;  // total accesses at this offset
    int32_t  writeCount     = 0;  // of those, writes (reads = occurrences - writeCount)
    bool     highConfidence = false;  // base register provably a `this`-alias
    // [DENKEN-WRITE-SITES] The addresses of the instructions that write this offset, each once: a field's native write
    // site found with no debugger, for an AOB or a code injection there. The first writeSitesViaThis of them write
    // through a register proven to hold `this`, in decode order; the rest write through any other base (another
    // object's field at the same offset -- or not this field at all, such as an exec thunk's store into its FFrame),
    // in decode order. highConfidence is the offset's, not a site's: read writeSitesViaThis for the sites. A store is
    // recorded at its own displacement only, so a wide store over several fields is a site of the first one.
    std::vector<uintptr_t> writeSites;
    uint8_t  writeSitesViaThis   = 0;
    bool     writeSitesTruncated = false;  // more distinct write sites than kMaxWriteSites
};

struct NativeAnalysisResult {
    bool ok             = false;  // decoder ran (start bytes were readable)
    int  instrsDecoded  = 0;
    int  callsFollowed  = 0;      // thunk -> impl handoffs followed
    bool budgetHit      = false;  // hit the instruction budget (result may be partial)
    std::vector<NativeFieldAccess> accesses;
};

// Memory reader: copy up to maxLen bytes from `addr` into `out`.
// Returns the number of bytes actually copied (0 = unreadable).
using MemReader = std::function<size_t(uintptr_t addr, uint8_t* out, size_t maxLen)>;

// Disassemble the native function at `startAddr` and collect `[this+off]`
// field accesses. `this` is assumed to arrive in RCX (Microsoft x64 ABI; the
// UE exec-thunk signature is Func(UObject* Context, FFrame&, void* Result), so
// Context==this lands in RCX). The pass tracks register copies of `this`,
// records `[reg+disp]` accesses (high-confidence when the base is a tracked
// this-alias), and follows up to a few direct CALL/JMP handoffs (the thunk ->
// real C++ impl) when RCX still holds a this-alias at the transfer site.
NativeAnalysisResult Analyze(uintptr_t startAddr, const MemReader& reader);

} // namespace Denken
