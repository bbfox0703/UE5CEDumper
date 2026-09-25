#pragma once

// ============================================================
// Methode — メトーデ (梅特戴 — the all-capable analyst mage)
// CEPlugin: the pure helpers Methode.cpp needs, header-inline so dll_helpers_test can run them (the Scharf pattern:
// no test target compiles Methode.cpp).
// ============================================================

#include <Windows.h>
#include <cwchar>
#include <string>

namespace Methode {

// [PATH-METHODE-NO8DOT3] The narrow path CE's InjectDLL needs for OUR DLL: CE copies it into the game and calls
// LoadLibraryA on it. The EXACT narrowing of longPath in codePage (WC_NO_BEST_FIT_CHARS + used-default: best fit
// names another folder, '?' names none); else the exact narrowing of shortPath, the 8.3 alias; else "" -- the
// caller refuses, naming the cause. The old code checked only the first narrowing and re-narrowed the alias with
// flags 0: on a volume without 8.3 names (D: here) the alias IS the long path, so a '?'-bearing path went to CE.
// codePage CP_ACP is read as GetACP(); a UTF-8 ANSI code page (the Windows beta option) takes UTF-8 as is, and
// refuses both the flag and the used-default out-parameter.
inline std::string NarrowForAnsiLoad(const wchar_t* longPath, const wchar_t* shortPath, UINT codePage)
{
    const UINT cp = (codePage == CP_ACP) ? GetACP() : codePage;
    auto exact = [cp](const wchar_t* w, std::string& out) -> bool {
        if (!w || !*w) return false;
        const bool utf8 = (cp == CP_UTF8);
        const DWORD flags = utf8 ? 0 : WC_NO_BEST_FIT_CHARS;
        BOOL used = FALSE;
        const int n = WideCharToMultiByte(cp, flags, w, -1, nullptr, 0, nullptr, utf8 ? nullptr : &used);
        if (n <= 1 || used) return false;
        std::string s(static_cast<size_t>(n), '\0');
        used = FALSE;
        if (WideCharToMultiByte(cp, flags, w, -1, s.data(), n, nullptr, utf8 ? nullptr : &used) != n || used)
            return false;
        s.resize(static_cast<size_t>(n) - 1);   // the terminating NUL
        out = std::move(s);
        return true;
    };
    std::string out;
    if (exact(longPath, out)) return out;
    if (shortPath && longPath && std::wcscmp(shortPath, longPath) != 0 && exact(shortPath, out)) return out;
    return {};
}

} // namespace Methode
