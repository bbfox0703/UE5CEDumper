/* Minimal PE64 loader for the differential: reads a file from disk (never
 * executes it), maps its sections at their RVAs into one buffer, and exposes the
 * .pdata RUNTIME_FUNCTION table. Header-only; include from exactly one TU. */
#pragma once
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct PeSec { uint32_t rva, vsize, rawsize, chars; char name[9]; } PeSec;

typedef struct PeImg {
    uint8_t* img;
    size_t   imgSize;
    uint64_t imageBase;
    int      nsec;
    PeSec    sec[128];
    const uint32_t* pdata;   /* triplets: begin, end, unwind (RVAs), inside img */
    size_t   npdata;
} PeImg;

#define PE_SCN_EXEC 0x20000000u

static int pe_load(const char* path, PeImg* pe) {
    memset(pe, 0, sizeof(*pe));
    FILE* f = fopen(path, "rb");
    if (!f) { fprintf(stderr, "pe_load: cannot open %s\n", path); return 0; }
    _fseeki64(f, 0, SEEK_END);
    long long fsz = _ftelli64(f);
    _fseeki64(f, 0, SEEK_SET);
    uint8_t* file = (uint8_t*)malloc((size_t)fsz);
    if (!file || fread(file, 1, (size_t)fsz, f) != (size_t)fsz) { fclose(f); return 0; }
    fclose(f);

    uint32_t e_lfanew = *(uint32_t*)(file + 0x3C);
    uint8_t* nt = file + e_lfanew;
    if (memcmp(nt, "PE\0\0", 4) != 0) { fprintf(stderr, "pe_load: not PE\n"); return 0; }
    uint16_t nsec = *(uint16_t*)(nt + 6);
    uint16_t optSize = *(uint16_t*)(nt + 20);
    uint8_t* opt = nt + 24;
    if (*(uint16_t*)opt != 0x20B) { fprintf(stderr, "pe_load: not PE32+\n"); return 0; }
    pe->imageBase = *(uint64_t*)(opt + 24);
    uint32_t sizeOfImage = *(uint32_t*)(opt + 56);
    uint32_t sizeOfHeaders = *(uint32_t*)(opt + 60);
    uint8_t* dd = opt + 112;                     /* DataDirectory[0] */
    uint32_t excRva = *(uint32_t*)(dd + 3 * 8);
    uint32_t excSize = *(uint32_t*)(dd + 3 * 8 + 4);

    pe->imgSize = sizeOfImage;
    pe->img = (uint8_t*)calloc(1, sizeOfImage);
    memcpy(pe->img, file, sizeOfHeaders < (uint32_t)fsz ? sizeOfHeaders : (size_t)fsz);
    uint8_t* sh = opt + optSize;
    pe->nsec = nsec > 128 ? 128 : nsec;
    for (int i = 0; i < pe->nsec; ++i) {
        uint8_t* s = sh + i * 40;
        PeSec* ps = &pe->sec[i];
        memcpy(ps->name, s, 8); ps->name[8] = 0;
        ps->vsize = *(uint32_t*)(s + 8);
        ps->rva = *(uint32_t*)(s + 12);
        ps->rawsize = *(uint32_t*)(s + 16);
        uint32_t rawptr = *(uint32_t*)(s + 20);
        ps->chars = *(uint32_t*)(s + 36);
        uint32_t n = ps->rawsize < ps->vsize ? ps->rawsize : ps->vsize;
        if (ps->rva + (size_t)n > pe->imgSize) n = (uint32_t)(pe->imgSize - ps->rva);
        if ((size_t)rawptr + n > (size_t)fsz) n = (uint32_t)((size_t)fsz - rawptr);
        memcpy(pe->img + ps->rva, file + rawptr, n);
    }
    free(file);
    if (excRva && excSize && excRva + (size_t)excSize <= pe->imgSize) {
        pe->pdata = (const uint32_t*)(pe->img + excRva);
        pe->npdata = excSize / 12;
    }
    return 1;
}
