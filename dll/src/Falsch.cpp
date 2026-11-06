// Falsch — TrampolinePreflight. The contract and the reasons are in Falsch.h.

#include "Falsch.h"
#include "Lager.h"

#include "hde/hde64.h"   // vendor/minhook/src: the decoder MinHook's trampoline builder calls

#include <Windows.h>
#include <Zydis/Zydis.h>

#include <algorithm>
#include <climits>
#include <cstdarg>
#include <cstdio>
#include <cstring>

namespace Falsch {

HdeView LinkedHde(const uint8_t* code, int /*offset*/, void* /*ctx*/) {
    hde64s hs;
    hde64_disasm(code, &hs);
    HdeView v;
    v.len = hs.len;
    v.error = (hs.flags & F_ERROR) != 0;
    v.opcode = hs.opcode;
    v.opcode2 = hs.opcode2;
    v.modrm = hs.modrm;
    v.modrmReg = hs.modrm_reg;
    v.immBytes = static_cast<uint8_t>((hs.flags & 0x3C) >> 2);
    v.imm = hs.imm.imm64;
    v.disp32 = hs.disp.disp32;
    return v;
}

const char* VerdictName(Verdict v) {
    switch (v) {
    case Verdict::Safe:           return "safe";
    case Verdict::MinHookRefuses: return "MinHook refuses";
    case Verdict::Unsafe:         return "unsafe";
    }
    return "?";
}

namespace {

// trampoline.c, x64: the patch is a 5-byte JMP_REL (sizeof(JMP_REL)); the walk copies every instruction that starts
// before it and jumps back to the first that does not.
constexpr int kPatchBytes = 5;

// What trampoline.c writes in place of an instruction it rebuilds (trampoline.h's x64 structs), and the most it lets
// a trampoline hold before the relay: TRAMPOLINE_MAX_SIZE = MEMORY_SLOT_SIZE - sizeof(JMP_ABS).
constexpr int kCallAbsBytes = 16;   // CALL_ABS: FF15 00000002, EB 08, 8-byte address
constexpr int kJmpAbsBytes = 14;    // JMP_ABS:  FF25 00000000, 8-byte address
constexpr int kJccAbsBytes = 16;    // JCC_ABS:  7x 0E, FF25 00000000, 8-byte address
constexpr int kTrampolineMax = static_cast<int>(Lager::kMemorySlotSize) - kJmpAbsBytes;

// How CreateTrampolineFunction treats one instruction, decided from hde64's fields in trampoline.c's own order.
enum class Kind { RipRelative, CallRel, JmpRel, Jcc, Ret, Copy };

Kind Classify(const HdeView& h) {
    if ((h.modrm & 0xC7) == 0x05) return Kind::RipRelative;
    if (h.opcode == 0xE8) return Kind::CallRel;
    if ((h.opcode & 0xFD) == 0xE9) return Kind::JmpRel;
    if ((h.opcode & 0xF0) == 0x70 || (h.opcode & 0xFC) == 0xE0 || (h.opcode2 & 0xF0) == 0x80) return Kind::Jcc;
    if ((h.opcode & 0xFE) == 0xC2) return Kind::Ret;
    return Kind::Copy;
}

const ZydisDecoder& Decoder() {
    static const ZydisDecoder decoder = [] {
        ZydisDecoder d{};
        ZydisDecoderInit(&d, ZYDIS_MACHINE_MODE_LONG_64, ZYDIS_STACK_WIDTH_64);
        return d;
    }();
    return decoder;
}

bool IsIpRegister(ZydisRegister r) {
    return r == ZYDIS_REGISTER_RIP || r == ZYDIS_REGISTER_EIP;
}

// The one-byte-map opcode MinHook's classification takes the instruction to be, in the encoding it assumes.
bool IsLegacyOpcode(const ZydisDecodedInstruction& z, ZydisOpcodeMap map, uint8_t opcode) {
    return z.encoding == ZYDIS_INSTRUCTION_ENCODING_LEGACY && z.opcode_map == map && z.opcode == opcode;
}

Report Fail(Verdict v, int offset, const char* fmt, ...) {
    Report r;
    r.verdict = v;
    r.offset = offset;
    char buf[256];
    va_list ap;
    va_start(ap, fmt);
    std::vsnprintf(buf, sizeof(buf), fmt, ap);
    va_end(ap);
    r.why = buf;
    return r;
}

}  // namespace

Report CheckTrampoline(const uint8_t* code, size_t len, uint64_t address, HdeDecode hde, void* ctx) {
    if (!hde) hde = &LinkedHde;
    // hde64 does not take a length: it reads as far as the instruction it believes it is decoding. Give it the bytes
    // in a zero-padded copy so a misread can never run it past the caller's buffer; Zydis gets the real length.
    uint8_t buf[kSpan + 32] = {};
    const size_t avail = (std::min)(len, kSpan);
    if (code && avail) std::memcpy(buf, code, avail);

    int pos = 0;
    int copied = 0;
    int newPos = 0;         // trampoline.c's newPos: where the next copy goes in the trampoline
    uint64_t jmpDest = 0;   // trampoline.c's jmpDest: the furthest internal jump target, 0 while none
    // Where each copied instruction went, and every jump inside the patch (copied byte for byte), for the check after
    // the walk. The walk copies at most kPatchBytes instructions, so these never fill.
    struct Step { int pos; int newPos; };
    struct Inner { int pos; int newPos; uint64_t dest; };
    Step steps[kPatchBytes];
    Inner inner[kPatchBytes];
    int nSteps = 0, nInner = 0;
    bool walked = false;
    for (int guard = 0; guard < 32; ++guard) {
        const HdeView h = hde(buf + pos, pos, ctx);
        if (h.error)
            return Fail(Verdict::MinHookRefuses, pos, "hde64 cannot decode the instruction at +%d, so MinHook refuses "
                        "the target by itself", pos);
        if (pos >= kPatchBytes) { walked = true; break; }   // the instruction MinHook jumps back to: decoded, not copied

        ZydisDecodedInstruction z;
        ZydisDecodedOperand ops[ZYDIS_MAX_OPERAND_COUNT];
        if (static_cast<size_t>(pos) >= avail ||
            !ZYAN_SUCCESS(ZydisDecoderDecodeFull(&Decoder(), buf + pos, avail - pos, &z, ops)))
            return Fail(Verdict::Unsafe, pos, "MinHook would copy the bytes at +%d, which are no instruction Zydis "
                        "can decode", pos);
        if (z.length != h.len)
            return Fail(Verdict::Unsafe, pos, "hde64 measures the instruction at +%d as %u bytes, Zydis as %u: MinHook "
                        "would copy a cut or overlong instruction", pos, h.len, z.length);

        const uint64_t ip = address + static_cast<uint64_t>(pos);
        const ZydisDecodedOperand* relImm = nullptr;
        const ZydisDecodedOperand* ipMem = nullptr;
        for (uint8_t i = 0; i < z.operand_count; ++i) {
            if (ops[i].type == ZYDIS_OPERAND_TYPE_IMMEDIATE && ops[i].imm.is_relative) relImm = &ops[i];
            if (ops[i].type == ZYDIS_OPERAND_TYPE_MEMORY && IsIpRegister(ops[i].mem.base)) ipMem = &ops[i];
        }
        // The branch target Zydis computes, for comparison with the one trampoline.c computes from hde64's imm.
        uint64_t zTarget = 0;
        const bool haveTarget = relImm && ZYAN_SUCCESS(ZydisCalcAbsoluteAddress(&z, relImm, ip, &zTarget));
        const int32_t imm32 = static_cast<int32_t>(static_cast<uint32_t>(h.imm));
        const int8_t imm8 = static_cast<int8_t>(static_cast<uint8_t>(h.imm));

        bool finished = false;
        int copySize = h.len;   // trampoline.c's copySize: what lands in the trampoline for this instruction
        switch (Classify(h)) {
        case Kind::RipRelative: {
            // trampoline.c re-points the 4 bytes at len - immBytes - 4, using hde64's disp32, so they reach the same
            // address from the trampoline. Right only when that is where the displacement sits, when hde64 read it
            // right, and when the new displacement fits: the trampoline lands within MAX_MEMORY_RANGE of the target
            // and the result is truncated unchecked (the bound below adds a block of margin, so it is conservative).
            if (!ipMem || z.raw.disp.size != 32)
                return Fail(Verdict::Unsafe, pos, "MinHook would re-point a RIP-relative displacement at +%d, but Zydis "
                            "decodes no RIP-relative operand there", pos);
            const int patchAt = h.len - h.immBytes - 4;
            if (z.raw.disp.offset != patchAt || h.disp32 != static_cast<uint32_t>(z.raw.disp.value))
                return Fail(Verdict::Unsafe, pos, "MinHook would re-point bytes +%d..+%d of the instruction at +%d from "
                            "displacement 0x%X; the displacement is 0x%X at +%u", patchAt, patchAt + 3, pos, h.disp32,
                            static_cast<uint32_t>(z.raw.disp.value), z.raw.disp.offset);
            const int64_t reach = static_cast<int64_t>(pos) + h.len + static_cast<int64_t>(z.raw.disp.value);
            const int64_t worst = (reach < 0 ? -reach : reach) + static_cast<int64_t>(Lager::kMaxMemoryRange) +
                                  static_cast<int64_t>(Lager::kMemoryBlockSize);
            if (worst > INT32_MAX)
                return Fail(Verdict::Unsafe, pos, "the RIP-relative reference at +%d is %lld bytes away: from a "
                            "trampoline up to 1 GB off, MinHook's re-pointed displacement can overflow 32 bits", pos,
                            static_cast<long long>(reach));
            // A memory access (not LEA's address arithmetic) into the bytes the hook overwrites -- target - 5 to
            // target + 4, which covers the hot-patch path too -- reads the patch once the hook is enabled.
            if (ipMem->mem.type == ZYDIS_MEMOP_TYPE_MEM) {
                const int64_t hi = reach + (std::max)(1, static_cast<int>(ipMem->size / 8));
                if (hi > -kPatchBytes && reach < kPatchBytes)
                    return Fail(Verdict::Unsafe, pos, "the RIP-relative access at +%d touches +%lld, inside the bytes "
                                "the hook overwrites", pos, static_cast<long long>(reach));
            }
            if (h.opcode == 0xFF && h.modrmReg == 4) finished = true;   // jmp [rip+x]: the function goes elsewhere
            break;
        }
        case Kind::CallRel: {
            const uint64_t dest = ip + h.len + static_cast<int64_t>(imm32);
            if (!IsLegacyOpcode(z, ZYDIS_OPCODE_MAP_DEFAULT, 0xE8) || !haveTarget || zTarget != dest)
                return Fail(Verdict::Unsafe, pos, "MinHook would rebuild the instruction at +%d as CALL 0x%llX; Zydis "
                            "decodes %s", pos, static_cast<unsigned long long>(dest), ZydisMnemonicGetString(z.mnemonic));
            copySize = kCallAbsBytes;
            break;
        }
        case Kind::JmpRel: {
            const uint64_t dest = ip + h.len + (h.opcode == 0xEB ? static_cast<int64_t>(imm8) : static_cast<int64_t>(imm32));
            if (!IsLegacyOpcode(z, ZYDIS_OPCODE_MAP_DEFAULT, h.opcode) || !haveTarget || zTarget != dest)
                return Fail(Verdict::Unsafe, pos, "MinHook would rebuild the instruction at +%d as JMP 0x%llX; Zydis "
                            "decodes %s", pos, static_cast<unsigned long long>(dest), ZydisMnemonicGetString(z.mnemonic));
            if (address <= dest && dest < address + kPatchBytes) {
                jmpDest = (std::max)(jmpDest, dest);   // a jump inside the patch is copied as it is
                if (nInner < kPatchBytes) inner[nInner++] = { pos, newPos, dest };
            } else {
                finished = jmpDest == 0 || ip >= jmpDest;
                copySize = kJmpAbsBytes;
            }
            break;
        }
        case Kind::Jcc: {
            const bool shortForm = (h.opcode & 0xF0) == 0x70 || (h.opcode & 0xFC) == 0xE0;
            const uint64_t dest = ip + h.len + (shortForm ? static_cast<int64_t>(imm8) : static_cast<int64_t>(imm32));
            // trampoline.c takes the condition from hs.opcode, or from hs.opcode2 when hs.opcode is 0F: right only for
            // the legacy 7x / E0-E3 / 0F 8x encodings. Anything else whose opcode2 is 8x is not a Jcc at all.
            const bool encodingOk = shortForm ? IsLegacyOpcode(z, ZYDIS_OPCODE_MAP_DEFAULT, h.opcode)
                                              : h.opcode == 0x0F && IsLegacyOpcode(z, ZYDIS_OPCODE_MAP_0F, h.opcode2);
            if (!encodingOk || !haveTarget || zTarget != dest)
                return Fail(Verdict::Unsafe, pos, "MinHook would rebuild the instruction at +%d as a conditional jump to "
                            "0x%llX; Zydis decodes %s", pos, static_cast<unsigned long long>(dest),
                            ZydisMnemonicGetString(z.mnemonic));
            if (address <= dest && dest < address + kPatchBytes) {
                jmpDest = (std::max)(jmpDest, dest);
                if (nInner < kPatchBytes) inner[nInner++] = { pos, newPos, dest };
            } else if ((h.opcode & 0xFC) == 0xE0) {
                return Fail(Verdict::MinHookRefuses, pos, "a LOOP / JRCXZ at +%d leaves the patch, which MinHook refuses "
                            "by itself", pos);
            } else {
                copySize = kJccAbsBytes;
            }
            break;
        }
        case Kind::Ret:
            if (!IsLegacyOpcode(z, ZYDIS_OPCODE_MAP_DEFAULT, h.opcode))
                return Fail(Verdict::Unsafe, pos, "MinHook would end the copy at +%d on a RET; Zydis decodes %s", pos,
                            ZydisMnemonicGetString(z.mnemonic));
            finished = jmpDest == 0 || ip >= jmpDest;
            break;
        case Kind::Copy:
            // Copied byte for byte: correct only for an instruction whose meaning does not depend on where it sits.
            if (relImm || ipMem)
                return Fail(Verdict::Unsafe, pos, "MinHook would copy %s at +%d unchanged, but it is relative to its "
                            "own address", ZydisMnemonicGetString(z.mnemonic), pos);
            break;
        }
        // trampoline.c's own refusals, which are safe: a length change ahead of an internal jump's target, and a
        // trampoline too long for its slot.
        if (ip < jmpDest && copySize != h.len)
            return Fail(Verdict::MinHookRefuses, pos, "the instruction at +%d changes length ahead of an internal jump's "
                        "target, which MinHook refuses by itself", pos);
        if (newPos + copySize > kTrampolineMax)
            return Fail(Verdict::MinHookRefuses, pos, "the trampoline would outgrow its slot at +%d, which MinHook "
                        "refuses by itself", pos);
        if (nSteps >= kPatchBytes)
            return Fail(Verdict::Unsafe, pos, "more instructions before +%d than the patch can hold", kPatchBytes);
        steps[nSteps++] = { pos, newPos };
        newPos += copySize;
        ++copied;
        pos += h.len;
        if (finished) { walked = true; break; }
    }
    if (!walked)
        return Fail(Verdict::Unsafe, pos, "the walk did not end within its bound at +%d", pos);

    // A jump inside the patch is copied byte for byte, so in the trampoline it lands the same distance away as in the
    // original. That is the same instruction only when the target is one MinHook copied and nothing between the two
    // changed length -- trampoline.c refuses a length change only AFTER the jump, so a jump back over a rebuilt
    // instruction, or into the middle of one, would land on something else.
    for (int k = 0; k < nInner; ++k) {
        bool same = false;
        for (int j = 0; j < nSteps; ++j)
            if (address + static_cast<uint64_t>(steps[j].pos) == inner[k].dest)
                same = steps[j].newPos - steps[j].pos == inner[k].newPos - inner[k].pos;
        if (!same)
            return Fail(Verdict::Unsafe, inner[k].pos, "the jump at +%d into the patch is copied unchanged, but in the "
                        "trampoline it would not land on the instruction at +%lld", inner[k].pos,
                        static_cast<long long>(inner[k].dest - address));
    }

    // Once the hook is enabled, a branch elsewhere in the function that lands on +1..+4 lands inside the hook's jump
    // and crashes the game. Scan what was read past the patch, linearly, until it stops decoding: a partial look --
    // a branch beyond these bytes is not seen -- but it is the shape a short loop over the prologue takes.
    for (size_t at = static_cast<size_t>((std::max)(pos, kPatchBytes)); at < avail;) {
        ZydisDecodedInstruction z;
        ZydisDecodedOperand ops[ZYDIS_MAX_OPERAND_COUNT];
        if (!ZYAN_SUCCESS(ZydisDecoderDecodeFull(&Decoder(), buf + at, avail - at, &z, ops))) break;
        for (uint8_t i = 0; i < z.operand_count; ++i) {
            uint64_t t = 0;
            if (ops[i].type == ZYDIS_OPERAND_TYPE_IMMEDIATE && ops[i].imm.is_relative &&
                ZYAN_SUCCESS(ZydisCalcAbsoluteAddress(&z, &ops[i], address + at, &t)) && address < t &&
                t < address + kPatchBytes)
                return Fail(Verdict::Unsafe, static_cast<int>(at), "the branch at +%d lands on +%lld, inside the bytes "
                            "the hook overwrites", static_cast<int>(at), static_cast<long long>(t - address));
        }
        at += z.length;
    }

    Report r;
    r.copied = copied;
    return r;
}

namespace {

// SEH needs a frame with no C++ objects (C2712), so the copy lives on its own.
bool CopySEH(const void* src, void* dst, size_t len) {
    __try {
        std::memcpy(dst, src, len);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
    return true;
}

}  // namespace

bool ReadTarget(uintptr_t target, uint8_t (&out)[kSpan], std::string& why) {
    static_assert(kSpan == Lager::kHolderReadSpan, "the replay reads exactly the span Lager derives");
    MEMORY_BASIC_INFORMATION at{}, end{}, before{};
    if (!VirtualQuery(reinterpret_cast<LPCVOID>(target), &at, sizeof(at)) ||
        !VirtualQuery(reinterpret_cast<LPCVOID>(target + Lager::kHolderReadSpan - 1), &end, sizeof(end)) ||
        !VirtualQuery(reinterpret_cast<LPCVOID>(target - 5), &before, sizeof(before))) {
        why = "VirtualQuery failed on the target's pages (an address outside user space)";
        return false;
    }
    if (!Lager::HolderCandidateReadable(at.State, at.Protect, end.State, end.Protect, before.State, before.Protect)) {
        why = "not readable code: a page MinHook reads is uncommitted, guarded or not executable";
        return false;
    }
    if (!CopySEH(reinterpret_cast<const void*>(target), out, sizeof(out))) {
        why = "the bytes could not be read (the copy faulted)";
        return false;
    }
    // MinHook reads target - 5 .. target - 1 when IsExecutableAddress calls that page executable (the hot-patch path).
    uint8_t above[5];
    if (before.State == Lager::kMemCommit && Lager::ProtectIsExecutable(before.Protect) &&
        !CopySEH(reinterpret_cast<const void*>(target - 5), above, sizeof(above))) {
        why = "the 5 bytes before the target could not be read (the copy faulted)";
        return false;
    }
    return true;
}

Report VetHookTarget(uintptr_t target) {
    uint8_t bytes[kSpan];
    Report r;
    if (!ReadTarget(target, bytes, r.why)) {
        r.verdict = Verdict::Unsafe;
        return r;
    }
    return CheckTrampoline(bytes, sizeof(bytes), target);
}

}  // namespace Falsch
