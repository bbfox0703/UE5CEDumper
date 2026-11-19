// denken_run <pe file> <out.txt>
//
// Links the REAL dll/src/Denken.cpp against one Zydis revision and runs
// Denken::Analyze from every .pdata function start of a PE image mapped from
// disk (never executed). One line per start: the full canonical result, so two
// builds (pin / master) can be diffed byte for byte. This is Denken's output
// end to end -- not a projection of what it might read. Each access prints
// offset:size:occurrences:writes:highConfidence:v<writeSitesViaThis>:t<writeSitesTruncated>
// then its write sites as RVAs in Denken's order, [rva,...] ([DENKEN-WRITE-SITES]).
// The printer takes both result types apart with structured bindings, which
// must name every member: a field added to Denken.h stops this file compiling
// instead of going unprinted, which would let a bump that moves only that
// field hash identically.
#include "Denken.h"
extern "C" {
#include "pe_image.h"
}
#include <algorithm>
#include <cstdio>
#include <cstring>
#include <vector>

// Positive control: each case is followed by `mov [rcx+0x10], eax ; ret`, so a
// run proves the harness CAN see a pin/master difference through Denken (an
// all-identical result is otherwise indistinguishable from two builds of the
// same Zydis). vex-map7 rdmsr and movrs differ from a base before fcc63c6, the
// 66-prefixed PTWRITE (refused since 7254d2c) from a base before 7254d2c; a case
// stops differing once the base is past its commit. ibhf and wbnoinvd decode at
// a95bb71 too, to the same length, and Denken's output for them is the same at
// every revision (a run from a95bb71 shows it): they show that a decoder change
// Denken does not read stays invisible.
// Plain mov is the baseline. zydis_bump_diff.py flags the run only when NO case
// differs: then add bytes from one of the new delta's decode changes.
static int RunControl() {
    struct Case { const char* name; std::vector<uint8_t> head; };
    const std::vector<Case> cases = {
        { "vex-map7 rdmsr (3bca602+fcc63c6)", { 0xC4, 0xE7, 0x7B, 0xF6, 0xC3, 0x78, 0x56, 0x34, 0x12 } },
        { "movrs al,[rdi] (b4ebc9a)",          { 0x0F, 0x38, 0x8A, 0x07 } },
        { "ibhf (8a961be, mode on)",           { 0xF3, 0x48, 0x0F, 0x1E, 0xF8 } },
        { "wbnoinvd (efadec2, mode on)",       { 0xF3, 0x0F, 0x09 } },
        { "66 ptwrite [rax] (7254d2c)",        { 0x66, 0xF3, 0x0F, 0xAE, 0x20 } },
        { "plain mov (baseline)",              { 0x48, 0x89, 0xCB } },
    };
    for (const auto& c : cases) {
        std::vector<uint8_t> code = c.head;
        for (uint8_t b : { 0x89, 0x41, 0x10, 0xC3 }) code.push_back(b);
        const uintptr_t base = 0x140000000ULL;
        Denken::MemReader rd = [&](uintptr_t a, uint8_t* d, size_t m) -> size_t {
            if (a < base || a >= base + code.size()) return 0;
            size_t off = static_cast<size_t>(a - base), n = std::min(m, code.size() - off);
            std::memcpy(d, code.data() + off, n);
            return n;
        };
        auto r = Denken::Analyze(base, rd);
        std::printf("%-36s ok%d instrs=%d accesses=%zu", c.name, r.ok, r.instrsDecoded, r.accesses.size());
        for (const auto& a : r.accesses)
            std::printf(" @%X w%d hc%d v%u s%zu", a.offset, a.writeCount, a.highConfidence ? 1 : 0,
                        static_cast<unsigned>(a.writeSitesViaThis), a.writeSites.size());
        std::printf("\n");
    }
    return 0;
}

int main(int argc, char** argv) {
    if (argc >= 2 && std::strcmp(argv[1], "--control") == 0) return RunControl();
    if (argc < 3) { std::fprintf(stderr, "usage: denken_run pe out.txt | --control\n"); return 2; }
    PeImg pe;
    if (!pe_load(argv[1], &pe)) return 2;
    FILE* out = std::fopen(argv[2], "w");
    if (!out) return 2;
    const uint64_t base = pe.imageBase;
    const size_t size = pe.imgSize;
    const uint8_t* img = pe.img;
    Denken::MemReader reader = [=](uintptr_t addr, uint8_t* dst, size_t maxLen) -> size_t {
        if (addr < base || addr >= base + size) return 0;
        size_t off = static_cast<size_t>(addr - base);
        size_t n = std::min(maxLen, size - off);
        std::memcpy(dst, img + off, n);
        return n;
    };
    std::vector<uint32_t> starts;
    for (size_t f = 0; f < pe.npdata; ++f) {
        uint32_t b = pe.pdata[f * 3];
        if (b && b < size) starts.push_back(b);
    }
    std::sort(starts.begin(), starts.end());
    starts.erase(std::unique(starts.begin(), starts.end()), starts.end());
    unsigned long long totalInstr = 0, totalAcc = 0, budgetHits = 0;
    for (uint32_t rva : starts) {
        auto r = Denken::Analyze(static_cast<uintptr_t>(base + rva), reader);
        auto& [ok, instrs, calls, budget, accesses] = r;
        std::sort(accesses.begin(), accesses.end(), [](const auto& x, const auto& y) { return x.offset < y.offset; });
        std::fprintf(out, "%08X ok%d i%d c%d b%d n%zu", rva, ok ? 1 : 0, instrs, calls, budget ? 1 : 0,
                     accesses.size());
        for (const auto& a : accesses) {
            const auto& [off, bytes, occurrences, writes, hc, sites, viaThis, truncated] = a;
            std::fprintf(out, " %X:%u:%d:%d:%d:v%u:t%d[", off, static_cast<unsigned>(bytes), occurrences, writes,
                         hc ? 1 : 0, static_cast<unsigned>(viaThis), truncated ? 1 : 0);
            for (size_t k = 0; k < sites.size(); ++k)
                std::fprintf(out, "%s%llX", k ? "," : "",
                             static_cast<unsigned long long>(sites[k] - static_cast<uintptr_t>(base)));
            std::fputc(']', out);
        }
        std::fputc('\n', out);
        totalInstr += instrs;
        totalAcc += accesses.size();
        budgetHits += budget ? 1 : 0;
    }
    std::fclose(out);
    std::printf("starts=%zu instrs=%llu accesses=%llu budgetHits=%llu\n", starts.size(), totalInstr, totalAcc,
                budgetHits);
    return 0;
}
