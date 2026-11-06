/* zdiff <pin.dll> <master.dll> <pe file> <code|all> <out.tsv>
 *
 * Decodes the same bytes with two Zydis revisions and reports every decode
 * whose projection differs. "pin" is the BASE revision and "master" the
 * CANDIDATE, whatever revisions zydis_bump_diff.py was given.
 *   code : linear sweep of every .pdata RUNTIME_FUNCTION [Begin, End) -- real
 *          instruction boundaries of compiler-emitted code. Advances by the PIN's
 *          length (today's behaviour), +1 on a pin failure.
 *   all  : every byte offset of every executable section -- covers padding,
 *          data-in-code and misaligned starts (bytes Denken can walk into past a
 *          noreturn call).
 * Two diff tiers: RAW (any projected field, ISA-set string excluded and counted
 * on its own) and DENKEN (only the fields Denken.cpp branches on or records).
 */
#include <windows.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include "zproj.h"
#include "pe_image.h"

static zproj_decode_fn g_pin, g_mas;

static size_t raw_key(const ZProj* p, char* b, size_t n) {
    size_t k = 0;
    if (!p->ok) return (size_t)snprintf(b, n, "FAIL(0x%08X)", p->status);
    k += snprintf(b + k, n - k, "%u %s [%s] v%u", p->length, p->mnemonic, p->category, p->opCountVisible);
    for (int i = 0; i < p->opCountVisible && i < ZP_MAXOPS && k < n; ++i) {
        const ZPOp* o = &p->ops[i];
        switch (o->type) {
        case ZP_OP_REG: k += snprintf(b + k, n - k, " | R %s s%u a%u", o->reg, o->size, o->actions); break;
        case ZP_OP_MEM: k += snprintf(b + k, n - k, " | M [%s+%s*%u%+lld/%u] s%u a%u", o->memBase, o->memIndex,
                                       o->memScale, (long long)o->dispValue, o->dispSize, o->size, o->actions); break;
        case ZP_OP_IMM: k += snprintf(b + k, n - k, " | I %s0x%llx s%u t%u:0x%llx", o->immIsRel ? "rel" : "",
                                       (unsigned long long)o->immValue, o->size, o->absOk,
                                       (unsigned long long)o->absTarget); break;
        case ZP_OP_PTR: k += snprintf(b + k, n - k, " | P 0x%llx", (unsigned long long)o->immValue); break;
        default:        k += snprintf(b + k, n - k, " | U"); break;
        }
    }
    return k;
}

/* Exactly the inputs Denken.cpp's TraceBlock consumes, over-approximated where
 * the use depends on tracked state (all [base+disp] rows kept, every written
 * register kept). */
static size_t denken_key(const ZProj* p, char* b, size_t n) {
    size_t k = 0;
    if (!p->ok) return (size_t)snprintf(b, n, "F");
    k += snprintf(b + k, n - k, "L%u C%u", p->length, p->catClass);
    const int v = p->opCountVisible;
    if ((p->catClass == ZP_CAT_CALL || p->catClass == ZP_CAT_UNCOND_BR) && v >= 1 && p->ops[0].type == ZP_OP_IMM)
        k += snprintf(b + k, n - k, " T%u:%llx", p->ops[0].absOk, (unsigned long long)p->ops[0].absTarget);
    if (p->isMov && v >= 2 && p->ops[0].type == ZP_OP_REG && p->ops[1].type == ZP_OP_REG)
        k += snprintf(b + k, n - k, " MOV %s<-%s", p->ops[0].regCanon, p->ops[1].regCanon);
    for (int i = 0; i < v && i < ZP_MAXOPS && k < n; ++i) {
        const ZPOp* o = &p->ops[i];
        if (o->type == ZP_OP_MEM && strcmp(o->memIndex, "none") == 0 && o->dispSize != 0 &&
            strcmp(o->memBaseCanon, "none") != 0)
            k += snprintf(b + k, n - k, " m%s%+lld/%u/%u", o->memBaseCanon, (long long)o->dispValue,
                          (unsigned)(uint8_t)(o->size / 8), o->write);
        else if (o->type == ZP_OP_REG && o->write)
            k += snprintf(b + k, n - k, " w%s", o->regCanon);
    }
    return k;
}

typedef struct Stats {
    unsigned long long decodes, rawDiff, dkDiff, isaOnly, ok2fail, fail2ok, lenDiff, mnemDiff, catDiff, opsDiff;
} Stats;

static FILE* g_out;
static unsigned long long g_lines, g_cap = 3000000ULL;

static int compare_at(const PeImg* pe, uint32_t rva, const char* mode, Stats* s, ZProj* a, ZProj* b) {
    const uint8_t* p = pe->img + rva;
    size_t len = pe->imgSize - rva;
    uint64_t ip = pe->imageBase + rva;
    g_pin(p, len, ip, a);
    g_mas(p, len, ip, b);
    s->decodes++;
    int isaDiff = strcmp(a->isaSet, b->isaSet) != 0;
    char ia[48], ib[48];
    memcpy(ia, a->isaSet, 48); memcpy(ib, b->isaSet, 48);
    memset(a->isaSet, 0, 48); memset(b->isaSet, 0, 48);
    int same = memcmp(a, b, sizeof(ZProj)) == 0;
    memcpy(a->isaSet, ia, 48); memcpy(b->isaSet, ib, 48);
    if (same) { if (isaDiff) s->isaOnly++; return a->ok ? a->length : 0; }

    char ra[1024], rb[1024], da[512], db[512];
    raw_key(a, ra, sizeof ra); raw_key(b, rb, sizeof rb);
    denken_key(a, da, sizeof da); denken_key(b, db, sizeof db);
    int rawd = strcmp(ra, rb) != 0;
    int dkd = strcmp(da, db) != 0;
    if (!rawd && !dkd) { if (isaDiff) s->isaOnly++; return a->ok ? a->length : 0; }
    s->rawDiff++;
    if (dkd) s->dkDiff++;
    const char* kind;
    if (a->ok && !b->ok) { kind = "OK2FAIL"; s->ok2fail++; }
    else if (!a->ok && b->ok) { kind = "FAIL2OK"; s->fail2ok++; }
    else if (a->length != b->length) { kind = "LEN"; s->lenDiff++; }
    else if (strcmp(a->mnemonic, b->mnemonic) != 0) { kind = "MNEM"; s->mnemDiff++; }
    else if (strcmp(a->category, b->category) != 0) { kind = "CAT"; s->catDiff++; }
    else { kind = "OPS"; s->opsDiff++; }
    if (g_lines < g_cap) {
        char hex[64] = {0};
        size_t nb = len < 15 ? len : 15;
        for (size_t i = 0; i < nb; ++i) sprintf(hex + i * 2, "%02X", p[i]);
        fprintf(g_out, "%s\t%08X\t%s\t%d\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n", mode, rva, hex, dkd, kind, ra, rb, da, db, ia, ib);
        g_lines++;
    }
    return a->ok ? a->length : 0;
}

static void print_stats(const char* label, const Stats* s) {
    printf("%-5s decodes=%llu rawDiff=%llu denkenDiff=%llu isaOnly=%llu | ok2fail=%llu fail2ok=%llu len=%llu mnem=%llu cat=%llu ops=%llu\n",
           label, s->decodes, s->rawDiff, s->dkDiff, s->isaOnly, s->ok2fail, s->fail2ok, s->lenDiff, s->mnemDiff,
           s->catDiff, s->opsDiff);
}

int main(int argc, char** argv) {
    if (argc < 6) { fprintf(stderr, "usage: zdiff pin.dll master.dll pe code|all out.tsv\n"); return 2; }
    HMODULE hp = LoadLibraryA(argv[1]), hm = LoadLibraryA(argv[2]);
    if (!hp || !hm) { fprintf(stderr, "LoadLibrary failed\n"); return 2; }
    g_pin = (zproj_decode_fn)GetProcAddress(hp, "zproj_decode");
    g_mas = (zproj_decode_fn)GetProcAddress(hm, "zproj_decode");
    zproj_version_fn vp = (zproj_version_fn)GetProcAddress(hp, "zproj_version");
    zproj_version_fn vm = (zproj_version_fn)GetProcAddress(hm, "zproj_version");
    if (!g_pin || !g_mas || !vp || !vm) { fprintf(stderr, "GetProcAddress failed\n"); return 2; }
    printf("pin=%s master=%s\n", vp(), vm());
    if (strcmp(vp(), vm()) == 0) { fprintf(stderr, "both DLLs report the same tag\n"); return 2; }

    PeImg pe;
    if (!pe_load(argv[3], &pe)) return 2;
    g_out = fopen(argv[5], "w");
    if (!g_out) return 2;
    const char* mode = argv[4];
    ZProj a, b;
    Stats s; memset(&s, 0, sizeof s);
    unsigned long long execBytes = 0;
    for (int i = 0; i < pe.nsec; ++i)
        if (pe.sec[i].chars & PE_SCN_EXEC) execBytes += pe.sec[i].rawsize < pe.sec[i].vsize ? pe.sec[i].rawsize : pe.sec[i].vsize;
    printf("file=%s imageBase=0x%llx sizeOfImage=0x%zx execBytes=%llu pdata=%zu\n", argv[3],
           (unsigned long long)pe.imageBase, pe.imgSize, execBytes, pe.npdata);

    if (strcmp(mode, "code") == 0) {
        unsigned long long fails = 0;
        for (size_t f = 0; f < pe.npdata; ++f) {
            uint32_t beg = pe.pdata[f * 3], end = pe.pdata[f * 3 + 1];
            if (!beg || end <= beg || end > pe.imgSize) continue;
            uint32_t rva = beg;
            while (rva < end) {
                int adv = compare_at(&pe, rva, "code", &s, &a, &b);
                if (adv <= 0) { fails++; adv = 1; }
                rva += (uint32_t)adv;
            }
        }
        printf("code: pin decode failures inside .pdata ranges (resynced +1) = %llu\n", fails);
    } else {
        for (int i = 0; i < pe.nsec; ++i) {
            if (!(pe.sec[i].chars & PE_SCN_EXEC)) continue;
            uint32_t n = pe.sec[i].rawsize < pe.sec[i].vsize ? pe.sec[i].rawsize : pe.sec[i].vsize;
            for (uint32_t off = 0; off < n; ++off) compare_at(&pe, pe.sec[i].rva + off, "all", &s, &a, &b);
        }
    }
    print_stats(mode, &s);
    printf("diff lines written=%llu (cap %llu)\n", g_lines, g_cap);
    fclose(g_out);
    return 0;
}
