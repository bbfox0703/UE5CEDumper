/* Version-neutral projection of one Zydis decode.
 *
 * Every field is filled by zproj_lib.c compiled against ONE Zydis revision and
 * mapped to a stable code or string here, so two DLLs built from different
 * revisions (whose enum VALUES differ -- BSR0 / USER_MSR / PFRCPIT1 shift them)
 * can be compared field by field. The fields are exactly what Denken.cpp reads,
 * plus mnemonic/category/ISA strings to explain a difference.
 */
#pragma once
#include <stdint.h>
#include <stddef.h>

#define ZP_MAXOPS 10

enum { ZP_OP_UNUSED = 0, ZP_OP_REG = 1, ZP_OP_MEM = 2, ZP_OP_PTR = 3, ZP_OP_IMM = 4 };
enum { ZP_CAT_OTHER = 0, ZP_CAT_RET = 1, ZP_CAT_UNCOND_BR = 2, ZP_CAT_CALL = 3, ZP_CAT_COND_BR = 4 };

typedef struct ZPOp {
    int32_t  type;          /* ZP_OP_* */
    uint16_t size;          /* operand size in bits (Denken: op.size / 8) */
    uint8_t  write;         /* actions & ZYDIS_OPERAND_ACTION_MASK_WRITE != 0 */
    uint8_t  actions;       /* raw action bits (stable bit values across revisions) */
    char     reg[24];
    char     regCanon[24];  /* ZydisRegisterGetLargestEnclosing(LONG_64, reg) or reg */
    char     memBase[24];
    char     memBaseCanon[24];
    char     memIndex[24];
    uint8_t  memScale;
    uint8_t  dispSize;      /* bits; 0 == no displacement */
    int64_t  dispValue;
    uint8_t  immIsRel;
    uint64_t immValue;
    uint8_t  absOk;         /* ZydisCalcAbsoluteAddress succeeded */
    uint64_t absTarget;
} ZPOp;

typedef struct ZProj {
    int32_t  ok;            /* ZYAN_SUCCESS(ZydisDecoderDecodeFull) */
    uint32_t status;
    uint8_t  length;
    uint8_t  catClass;      /* ZP_CAT_* -- what Denken branches on */
    uint8_t  isMov;         /* mnemonic == ZYDIS_MNEMONIC_MOV */
    uint8_t  opCountVisible;
    char     mnemonic[32];
    char     category[32];
    char     isaSet[48];
    ZPOp     ops[ZP_MAXOPS];
} ZProj;

typedef int (*zproj_decode_fn)(const uint8_t* buf, size_t len, uint64_t ip, ZProj* out);
typedef const char* (*zproj_version_fn)(void);
