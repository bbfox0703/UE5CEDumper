/* Compiled once per Zydis revision into zproj_<tag>.dll. Decodes with the SAME
 * call Denken.cpp makes (ZydisDecoderInit LONG_64/STACK_WIDTH_64 with default
 * modes, ZydisDecoderDecodeFull) and projects the result into ZProj. */
#include <Zydis/Zydis.h>
#include <string.h>
#include "zproj.h"

static ZydisDecoder g_dec;
static int g_init = 0;

static void cpy(char* dst, size_t n, const char* s) {
    if (!s) s = "?";
    strncpy(dst, s, n - 1);
    dst[n - 1] = 0;
}

static ZydisRegister canon(ZydisRegister r) {
    if (r == ZYDIS_REGISTER_NONE) return r;
    ZydisRegister c = ZydisRegisterGetLargestEnclosing(ZYDIS_MACHINE_MODE_LONG_64, r);
    return c != ZYDIS_REGISTER_NONE ? c : r;
}

__declspec(dllexport) const char* zproj_version(void) {
#ifdef ZP_TAG
    return ZP_TAG;
#else
    return "?";
#endif
}

__declspec(dllexport) int zproj_decode(const uint8_t* buf, size_t len, uint64_t ip, ZProj* out) {
    memset(out, 0, sizeof(*out));
    if (!g_init) {
        if (!ZYAN_SUCCESS(ZydisDecoderInit(&g_dec, ZYDIS_MACHINE_MODE_LONG_64, ZYDIS_STACK_WIDTH_64)))
            return -1;
        g_init = 1;
    }
    ZydisDecodedInstruction instr;
    ZydisDecodedOperand ops[ZYDIS_MAX_OPERAND_COUNT];
    ZyanStatus st = ZydisDecoderDecodeFull(&g_dec, buf, len, &instr, ops);
    out->status = (uint32_t)st;
    out->ok = ZYAN_SUCCESS(st) ? 1 : 0;
    if (!out->ok) return 0;

    out->length = instr.length;
    switch (instr.meta.category) {
    case ZYDIS_CATEGORY_RET:       out->catClass = ZP_CAT_RET; break;
    case ZYDIS_CATEGORY_UNCOND_BR: out->catClass = ZP_CAT_UNCOND_BR; break;
    case ZYDIS_CATEGORY_CALL:      out->catClass = ZP_CAT_CALL; break;
    case ZYDIS_CATEGORY_COND_BR:   out->catClass = ZP_CAT_COND_BR; break;
    default:                       out->catClass = ZP_CAT_OTHER; break;
    }
    out->isMov = instr.mnemonic == ZYDIS_MNEMONIC_MOV;
    out->opCountVisible = instr.operand_count_visible;
    cpy(out->mnemonic, sizeof(out->mnemonic), ZydisMnemonicGetString(instr.mnemonic));
    cpy(out->category, sizeof(out->category), ZydisCategoryGetString(instr.meta.category));
    cpy(out->isaSet, sizeof(out->isaSet), ZydisISASetGetString(instr.meta.isa_set));

    for (uint8_t i = 0; i < instr.operand_count_visible && i < ZP_MAXOPS; ++i) {
        const ZydisDecodedOperand* op = &ops[i];
        ZPOp* o = &out->ops[i];
        o->size = op->size;
        o->actions = (uint8_t)op->actions;
        o->write = (op->actions & ZYDIS_OPERAND_ACTION_MASK_WRITE) != 0;
        switch (op->type) {
        case ZYDIS_OPERAND_TYPE_REGISTER:
            o->type = ZP_OP_REG;
            cpy(o->reg, sizeof(o->reg), ZydisRegisterGetString(op->reg.value));
            cpy(o->regCanon, sizeof(o->regCanon), ZydisRegisterGetString(canon(op->reg.value)));
            break;
        case ZYDIS_OPERAND_TYPE_MEMORY:
            o->type = ZP_OP_MEM;
            cpy(o->memBase, sizeof(o->memBase), ZydisRegisterGetString(op->mem.base));
            cpy(o->memBaseCanon, sizeof(o->memBaseCanon), ZydisRegisterGetString(canon(op->mem.base)));
            cpy(o->memIndex, sizeof(o->memIndex), ZydisRegisterGetString(op->mem.index));
            o->memScale = op->mem.scale;
            o->dispSize = op->mem.disp.size;
            o->dispValue = op->mem.disp.value;
            break;
        case ZYDIS_OPERAND_TYPE_POINTER:
            o->type = ZP_OP_PTR;
            o->immValue = ((uint64_t)op->ptr.segment << 32) | op->ptr.offset;
            break;
        case ZYDIS_OPERAND_TYPE_IMMEDIATE: {
            o->type = ZP_OP_IMM;
            o->immIsRel = op->imm.is_relative;
            o->immValue = op->imm.value.u;
            ZyanU64 t = 0;
            if (ZYAN_SUCCESS(ZydisCalcAbsoluteAddress(&instr, op, ip, &t))) { o->absOk = 1; o->absTarget = t; }
            break;
        }
        default:
            o->type = ZP_OP_UNUSED;
            break;
        }
    }
    return 1;
}
