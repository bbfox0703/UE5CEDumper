#pragma once

// ============================================================
// Falsch — ファルシュ (一級魔法使い試験の試験官・「偽」 — First-Class Mage Exam proctor, "false")
// TrampolinePreflight: before MH_CreateHook copies a hook target's first instructions into a trampoline, check that
// MinHook reads those bytes the way Zydis does, and refuse the hook when it does not ([MH-TRAMPOLINE-PREFLIGHT]).
//
// Why: MinHook's CreateTrampolineFunction trusts its own length decoder (hde64). When hde64 misreads an instruction
// and does not flag it, MinHook returns MH_OK with a trampoline that runs different code from the original: the
// game crashes or misbehaves the first time the hooked function runs, and nothing is logged. Both the pinned hde64
// and the upstream rewrite have such a class, measured over 3.68M real function starts (docs/vendor-pin-reviews.md
// § minhook): the pinned one mis-measures popcnt, 67-addressing and 66+REX.W immediates; the rewrite fixes those but
// leaves trampoline.c reading a VEX / EVEX / XOP opcode 0x80-0x8F as a Jcc, so the instruction is dropped. A
// refusal is safe -- the feature is unavailable and the log says why. Zydis is the reference both measurements used,
// and the DLL links it already.
//
// The check replays CreateTrampolineFunction's x64 walk over a COPY of the target's bytes: which instructions it
// copies, how it classifies each one, and where it stops. For every instruction MinHook copies, Zydis must agree on
// the length, on the encoding MinHook's classification assumes, on the branch target MinHook computes and on where
// the displacement it re-points sits; an instruction MinHook copies unchanged must carry no position-dependent
// operand. What MinHook refuses by itself (an hde64 error, a LOOP out of the patch, a trampoline too long) is not
// modelled: MH_CreateHook reports those, safely.
//
// The replay mirrors vendor/minhook/src/trampoline.c, and check_lager_vendor_sync hashes that function, so a
// submodule bump that changes it fails the gates until this model is re-read.
// ============================================================

#include <cstddef>
#include <cstdint>
#include <string>

namespace Falsch {

/// What CreateTrampolineFunction reads from one hde64_disasm result. A view rather than hde64s itself, so a test can
/// hand the replay a decoder that answers the way a different hde64 revision does.
struct HdeView {
    uint8_t  len = 0;
    bool     error = false;   // F_ERROR: MinHook refuses the target by itself
    uint8_t  opcode = 0;
    uint8_t  opcode2 = 0;
    uint8_t  modrm = 0;
    uint8_t  modrmReg = 0;
    uint8_t  immBytes = 0;    // (flags & 0x3C) >> 2, as trampoline.c computes it: bytes after the displacement
    uint64_t imm = 0;         // hs.imm's bytes; trampoline.c reads imm8 or imm32 out of this union
    uint32_t disp32 = 0;
};

/// Decodes one instruction the way MinHook will. `code` points into the replay's own zero-padded copy, `offset` is
/// the instruction's distance from the target, and `ctx` is the caller's; LinkedHde uses neither.
using HdeDecode = HdeView (*)(const uint8_t* code, int offset, void* ctx);

/// The hde64_disasm this DLL links -- the decoder MinHook's trampoline builder calls.
HdeView LinkedHde(const uint8_t* code, int offset, void* ctx);

enum class Verdict : uint8_t {
    Safe,            // the trampoline MinHook builds runs the same instructions as the original
    MinHookRefuses,  // hde64 flags an instruction MinHook decodes: MH_CreateHook fails by itself, safely
    Unsafe,          // MinHook would build a trampoline that runs different code: do not call MH_CreateHook
};

const char* VerdictName(Verdict v);

struct Report {
    Verdict     verdict = Verdict::Safe;
    int         offset = 0;   // bytes from the target to the instruction the verdict is about
    int         copied = 0;   // instructions MinHook copies into the trampoline, when Safe
    std::string why;          // empty when Safe
};

/// How many bytes of a target the replay reads. CreateTrampolineFunction's last decode starts below 5 + 15 and
/// hde64 reads at most ~33 bytes of an instruction; this is the span Lager derives for the holder (kHolderReadSpan),
/// because it is the same read.
constexpr size_t kSpan = 64;

/// Replays MinHook's trampoline walk over `code` (`len` bytes copied from `address`). `hde` defaults to LinkedHde.
Report CheckTrampoline(const uint8_t* code, size_t len, uint64_t address, HdeDecode hde = nullptr,
                       void* ctx = nullptr);

/// Reads a hook target the way MinHook will, before MinHook does: VirtualQuery must say every byte MinHook can read
/// is committed, readable code (Lager::HolderCandidateReadable), then a SEH copy proves the bytes are there. MinHook
/// reads with no SEH of its own, inside its spin lock, so a fault there ends the game.
bool ReadTarget(uintptr_t target, uint8_t (&out)[kSpan], std::string& why);

/// ReadTarget, then CheckTrampoline with the linked hde64: what every hook that gets ENABLED runs before
/// MH_CreateHook.
Report VetHookTarget(uintptr_t target);

}  // namespace Falsch
