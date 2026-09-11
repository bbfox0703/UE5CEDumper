// ============================================================
// Denken — 鄧肯, 著重探知魔法的過程 native-code disassembly for Path 2 (see Denken.h)
// ============================================================

#include "Denken.h"

#include <Zydis/Zydis.h>

#include <array>
#include <unordered_map>

namespace Denken {
namespace {

// --- Tuning constants ---------------------------------------------------
// Total decoded-instruction budget across the thunk + any followed impls.
constexpr int    kInstrBudget   = 8192;
// Per-block guard (a single basic-block run before a terminator).
constexpr int    kBlockGuard    = 4096;
// Bytes pre-read per block. 4KB covers the vast majority of exec thunks and
// simple getter/setter impls; larger bodies get truncated (budget/chunk) and
// the result is flagged partial via budgetHit.
constexpr size_t kReadChunk     = 0x1000;
// Plausible field-displacement ceiling (128KB). UObjects can be large but a
// disp past this is almost certainly not a member field.
constexpr int64_t kDispLimit    = 0x20000;
// Max thunk->impl handoffs to follow (bounds fan-out on multi-call thunks).
constexpr int    kMaxFollow     = 6;

// Canonicalize a register to its largest enclosing GPR (eax/ax/al -> rax) so
// sub-register writes/reads compare cleanly. Returns the input unchanged for
// non-GPR / NONE.
inline ZydisRegister Canon(ZydisRegister r) {
    if (r == ZYDIS_REGISTER_NONE) return r;
    ZydisRegister c = ZydisRegisterGetLargestEnclosing(ZYDIS_MACHINE_MODE_LONG_64, r);
    return c != ZYDIS_REGISTER_NONE ? c : r;
}

// Volatile (caller-saved) GPRs under the Microsoft x64 ABI. A CALL clobbers
// these, so any this-alias living in one is dropped afterwards.
constexpr std::array<ZydisRegister, 7> kVolatile = {
    ZYDIS_REGISTER_RAX, ZYDIS_REGISTER_RCX, ZYDIS_REGISTER_RDX,
    ZYDIS_REGISTER_R8,  ZYDIS_REGISTER_R9,  ZYDIS_REGISTER_R10,
    ZYDIS_REGISTER_R11,
};

// Tracks which canonical GPRs currently alias the `this` pointer.
struct ThisRegs {
    std::vector<ZydisRegister> regs;
    bool Has(ZydisRegister r) const {
        r = Canon(r);
        for (auto x : regs) if (x == r) return true;
        return false;
    }
    void Add(ZydisRegister r) {
        r = Canon(r);
        if (r == ZYDIS_REGISTER_NONE) return;
        if (!Has(r)) regs.push_back(r);
    }
    void Remove(ZydisRegister r) {
        r = Canon(r);
        for (size_t i = 0; i < regs.size(); ++i)
            if (regs[i] == r) { regs.erase(regs.begin() + i); return; }
    }
    bool Empty() const { return regs.empty(); }
};

// Accumulates per-offset access stats; flushed to NativeFieldAccess at the end.
struct Accum {
    int32_t occurrences = 0;
    int32_t writeCount  = 0;
    uint8_t accessSize  = 0;
    bool    highConf    = false;
};

struct Ctx {
    const MemReader&                       reader;
    std::unordered_map<uint32_t, Accum>    byOffset;
    int                                    instrs       = 0;
    int                                    callsFollowed = 0;
    bool                                   budgetHit    = false;
};

// Record one [base+disp] access. highConf when base is a tracked this-alias.
void RecordAccess(Ctx& ctx, int64_t disp, uint8_t sizeBytes, bool isWrite, bool highConf) {
    if (disp < 0 || disp >= kDispLimit) return;
    auto& a = ctx.byOffset[static_cast<uint32_t>(disp)];
    a.occurrences++;
    if (isWrite) a.writeCount++;
    if (sizeBytes && !a.accessSize) a.accessSize = sizeBytes;
    a.highConf = a.highConf || highConf;
}

// Decode the basic-block run starting at `startAddr` with `this` in `tr`.
// `depth` bounds thunk->impl recursion (0 = top thunk, 1 = followed impl).
void TraceBlock(Ctx& ctx, uintptr_t startAddr, ThisRegs tr, int depth);

// On a direct relative CALL/JMP whose target resolves into readable code,
// follow it as a thunk->impl handoff (this preserved in RCX). Returns true
// when a follow was performed.
bool TryFollow(Ctx& ctx, const ZydisDecodedInstruction& instr,
               const ZydisDecodedOperand* ops, uintptr_t ip,
               const ThisRegs& tr, int depth) {
    if (depth != 0) return false;                       // depth-1 only
    if (ctx.callsFollowed >= kMaxFollow) return false;
    if (!tr.Has(ZYDIS_REGISTER_RCX)) return false;       // not a this handoff
    if (instr.operand_count_visible < 1) return false;
    if (ops[0].type != ZYDIS_OPERAND_TYPE_IMMEDIATE) return false;  // indirect
    ZyanU64 target = 0;
    if (!ZYAN_SUCCESS(ZydisCalcAbsoluteAddress(&instr, &ops[0], ip, &target)))
        return false;
    if (target == 0 || target == ip) return false;
    ctx.callsFollowed++;
    ThisRegs callee;
    callee.Add(ZYDIS_REGISTER_RCX);                      // impl gets this in RCX
    TraceBlock(ctx, static_cast<uintptr_t>(target), callee, depth + 1);
    return true;
}

void TraceBlock(Ctx& ctx, uintptr_t startAddr, ThisRegs tr, int depth) {
    std::vector<uint8_t> buf(kReadChunk);
    uintptr_t blockAddr = startAddr;

    // Outer loop allows a tail-call JMP to "continue" the block at a new
    // address without growing the C++ stack (we just re-read from there).
    for (int hops = 0; hops < kMaxFollow + 2; ++hops) {
        size_t got = ctx.reader(blockAddr, buf.data(), buf.size());
        if (got == 0) return;

        ZydisDecoder decoder;
        if (!ZYAN_SUCCESS(ZydisDecoderInit(
                &decoder, ZYDIS_MACHINE_MODE_LONG_64, ZYDIS_STACK_WIDTH_64)))
            return;

        size_t    consumed = 0;
        uintptr_t ip       = blockAddr;
        bool      jumped   = false;

        for (int step = 0; step < kBlockGuard; ++step) {
            if (ctx.instrs >= kInstrBudget) { ctx.budgetHit = true; return; }
            if (consumed >= got) return;   // ran off the read window

            ZydisDecodedInstruction instr;
            ZydisDecodedOperand     ops[ZYDIS_MAX_OPERAND_COUNT];
            if (!ZYAN_SUCCESS(ZydisDecoderDecodeFull(
                    &decoder, buf.data() + consumed, got - consumed, &instr, ops)))
                return;   // undecodable byte (likely padding / past the body)
            ctx.instrs++;

            // --- record [reg+disp] memory accesses ----------------------
            for (uint8_t i = 0; i < instr.operand_count_visible; ++i) {
                const auto& op = ops[i];
                if (op.type != ZYDIS_OPERAND_TYPE_MEMORY) continue;
                if (op.mem.index != ZYDIS_REGISTER_NONE) continue;   // skip SIB-indexed
                if (op.mem.disp.size == 0) continue;                 // no displacement (Zydis v5: has_displacement removed; size is width in bits, 0 == absent)
                const ZydisRegister base = Canon(op.mem.base);
                if (base == ZYDIS_REGISTER_NONE) continue;           // absolute / RIP-less
                if (base == ZYDIS_REGISTER_RIP) continue;            // global, not a field
                const bool isThis = tr.Has(base);
                // Low-confidence path excludes stack-relative bases (locals).
                if (!isThis &&
                    (base == ZYDIS_REGISTER_RSP || base == ZYDIS_REGISTER_RBP))
                    continue;
                const bool isWrite =
                    (op.actions & ZYDIS_OPERAND_ACTION_MASK_WRITE) != 0;
                const uint8_t sz = static_cast<uint8_t>(op.size / 8);
                RecordAccess(ctx, op.mem.disp.value, sz, isWrite, isThis);
            }

            const ZydisInstructionCategory cat = instr.meta.category;

            // --- terminators / transfers --------------------------------
            if (cat == ZYDIS_CATEGORY_RET) return;

            if (cat == ZYDIS_CATEGORY_UNCOND_BR) {
                // Tail-call: if it hands `this` (RCX) to readable code, keep
                // tracing at the target in this same block; else stop.
                if (depth == 0 && tr.Has(ZYDIS_REGISTER_RCX) &&
                    instr.operand_count_visible >= 1 &&
                    ops[0].type == ZYDIS_OPERAND_TYPE_IMMEDIATE) {
                    ZyanU64 tgt = 0;
                    if (ZYAN_SUCCESS(ZydisCalcAbsoluteAddress(&instr, &ops[0], ip, &tgt))
                        && tgt && tgt != ip) {
                        blockAddr = static_cast<uintptr_t>(tgt);
                        jumped = true;
                    }
                }
                break;   // leave inner loop (either jumped, or stop)
            }

            if (cat == ZYDIS_CATEGORY_CALL) {
                TryFollow(ctx, instr, ops, ip, tr, depth);
                // The call clobbers volatile registers regardless.
                for (auto r : kVolatile) tr.Remove(r);
            }
            else if (cat == ZYDIS_CATEGORY_COND_BR) {
                // Fall through (don't follow the taken branch). The insn
                // writes no tracked reg, so no state change.
            }
            else if (instr.mnemonic == ZYDIS_MNEMONIC_MOV &&
                     instr.operand_count_visible >= 2 &&
                     ops[0].type == ZYDIS_OPERAND_TYPE_REGISTER &&
                     ops[1].type == ZYDIS_OPERAND_TYPE_REGISTER) {
                // reg<-reg MOV: propagate the this-tag (read src before clobber).
                const ZydisRegister dst = Canon(ops[0].reg.value);
                const ZydisRegister src = Canon(ops[1].reg.value);
                const bool srcIsThis = tr.Has(src);
                tr.Remove(dst);
                if (srcIsThis) tr.Add(dst);
            }
            else {
                // Standard clobber: drop the tag from any written register.
                for (uint8_t i = 0; i < instr.operand_count_visible; ++i) {
                    const auto& op = ops[i];
                    if (op.type != ZYDIS_OPERAND_TYPE_REGISTER) continue;
                    if ((op.actions & ZYDIS_OPERAND_ACTION_MASK_WRITE) == 0) continue;
                    tr.Remove(op.reg.value);
                }
            }

            // [W5-DENKEN-DEADGUARD] A "bail to save budget in a followed impl" guard stood here --
            // `if (tr.Empty() && ctx.callsFollowed == 0) { if (depth > 0) return; }` -- and it was DEAD
            // in every reachable state: TryFollow counts the follow BEFORE it recurses, so callsFollowed
            // is >= 1 whenever depth > 0. Removed, not "repaired": dropping the callsFollowed term would
            // stop a followed impl the moment its this-alias dies and lose its later low-confidence
            // accesses (dll_helpers_test's Test_Denken_FollowedImplOutlivesItsAlias). kInstrBudget and
            // kBlockGuard bound the cost instead.

            consumed += instr.length;
            ip       += instr.length;
        }

        if (!jumped) break;   // block ended without a tail-call to continue
    }
}

} // namespace

NativeAnalysisResult Analyze(uintptr_t startAddr, const MemReader& reader) {
    NativeAnalysisResult result;
    if (!startAddr || !reader) return result;

    Ctx ctx{ reader };
    ThisRegs tr;
    tr.Add(ZYDIS_REGISTER_RCX);   // MS x64: Context(this) arrives in RCX

    // Pre-flight: if the very first bytes aren't readable, report not-ok.
    {
        uint8_t probe[1] = {};
        if (reader(startAddr, probe, sizeof(probe)) == 0)
            return result;
    }

    TraceBlock(ctx, startAddr, tr, 0);

    result.ok            = true;
    result.instrsDecoded = ctx.instrs;
    result.callsFollowed = ctx.callsFollowed;
    result.budgetHit     = ctx.budgetHit;
    result.accesses.reserve(ctx.byOffset.size());
    for (const auto& [off, a] : ctx.byOffset) {
        NativeFieldAccess fa;
        fa.offset         = off;
        fa.accessSize     = a.accessSize;
        fa.occurrences    = a.occurrences;
        fa.writeCount     = a.writeCount;
        fa.highConfidence = a.highConf;
        result.accesses.push_back(fa);
    }
    return result;
}

} // namespace Denken
