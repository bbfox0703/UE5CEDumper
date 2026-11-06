// Falsch — TrampolinePreflight. The contract and the reasons are in Falsch.h.

#include "Falsch.h"
#include "Lager.h"

#include "hde/hde64.h"   // vendor/minhook/src: the decoder MinHook's trampoline builder calls

#include <Windows.h>

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

Report CheckTrampoline(const uint8_t* /*code*/, size_t /*len*/, uint64_t /*address*/, HdeDecode /*hde*/,
                       void* /*ctx*/) {
    return {};   // RED: the replay is not written yet
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
        why = "not readable code: a page MinHook reads is uncommitted, guarded or execute-only";
        return false;
    }
    if (!CopySEH(reinterpret_cast<const void*>(target), out, sizeof(out))) {
        why = "the bytes could not be read (the copy faulted)";
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
