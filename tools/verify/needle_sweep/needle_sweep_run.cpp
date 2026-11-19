// The version-string tiers of one VersionNeedleScan.h, run over whole files: needle_sweep.py compiles this against
// two copies of the header and compares what each reads. One path per stdin line; one output line per path:
//   <path> \t <tier1 value>/<tier> \t <branch value>/<tier> \t <tier23 value>/<tier>
// A value is the kVersionNeedles row's code (0: no hit). The DLL scans the mapped image; the strings these tiers read
// sit in .rdata, whose bytes the file holds as they are.
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <string>
#include <vector>

#include "VersionNeedleScan.h"

static unsigned ValueOf(const Genau::NeedleScanResult& r) {
    return r.found() ? Genau::kVersionNeedles[r.index].value : 0u;
}

int main() {
    std::string path;
    while (std::getline(std::cin, path)) {
        if (path.empty()) continue;
        // The path arrives as UTF-8; a narrow open would read it in the ANSI code page and miss a non-ASCII name.
        std::ifstream in(std::filesystem::path(reinterpret_cast<const char8_t*>(path.c_str())), std::ios::binary);
        if (!in) {
            std::printf("%s\tERROR\n", path.c_str());
            continue;
        }
        std::vector<uint8_t> bytes((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
        const auto t1 = Genau::ScanVersionTier1(bytes.data(), bytes.size());
        const auto br = Genau::ScanVersionBranchName(bytes.data(), bytes.size());
        const auto t23 = Genau::ScanVersionTier23(bytes.data(), bytes.size());
        std::printf("%s\t%u/%d\t%u/%d\t%u/%d\n", path.c_str(), ValueOf(t1), t1.tier, ValueOf(br), br.tier,
                    ValueOf(t23), t23.tier);
        std::fflush(stdout);
    }
    return 0;
}
