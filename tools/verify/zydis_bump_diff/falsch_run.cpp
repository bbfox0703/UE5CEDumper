// falsch_run <pe file> <out.txt>
//
// Links the REAL dll/src/Falsch.cpp (MinHook trampoline pre-flight, [MH-TRAMPOLINE-PREFLIGHT]) against one Zydis
// revision and the vendored hde64, and runs Falsch::CheckTrampoline on the first 64 bytes of every .pdata function
// start of a PE image mapped from disk (never executed). One line per start: verdict, offset and instructions copied --
// not the reason text, which names Zydis mnemonics and so legitimately changes when a mnemonic is renamed. Falsch is
// Zydis's second user in the DLL: a Zydis bump that changed its verdicts would change which hooks get created.
#include "Falsch.h"
extern "C" {
#include "pe_image.h"
}
#include <algorithm>
#include <cstdio>
#include <cstring>
#include <vector>

int main(int argc, char** argv) {
    if (argc < 3) { std::fprintf(stderr, "usage: falsch_run pe out.txt\n"); return 2; }
    PeImg pe;
    if (!pe_load(argv[1], &pe)) return 2;
    FILE* out = std::fopen(argv[2], "w");
    if (!out) return 2;
    std::vector<uint32_t> starts;
    for (size_t f = 0; f < pe.npdata; ++f) {
        uint32_t b = pe.pdata[f * 3];
        if (b && b < pe.imgSize) starts.push_back(b);
    }
    std::sort(starts.begin(), starts.end());
    starts.erase(std::unique(starts.begin(), starts.end()), starts.end());
    unsigned long long safe = 0, refuses = 0, unsafe = 0;
    for (uint32_t rva : starts) {
        const size_t n = (std::min)(Falsch::kSpan, pe.imgSize - rva);
        const Falsch::Report r = Falsch::CheckTrampoline(pe.img + rva, n, pe.imageBase + rva);
        std::fprintf(out, "%08X %d %d %d\n", rva, static_cast<int>(r.verdict), r.offset, r.copied);
        (r.verdict == Falsch::Verdict::Safe ? safe : r.verdict == Falsch::Verdict::Unsafe ? unsafe : refuses)++;
    }
    std::fclose(out);
    std::printf("starts=%zu safe=%llu minhook_refuses=%llu unsafe=%llu\n", starts.size(), safe, refuses, unsafe);
    return 0;
}
