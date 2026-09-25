#pragma once

// ============================================================
// Methode — メトーデ (梅特戴 — the all-capable analyst mage)
// CEPlugin: the pure helpers Methode.cpp needs, header-inline so dll_helpers_test can run them (the Scharf pattern:
// no test target compiles Methode.cpp).
// ============================================================

#include <Windows.h>
#include <cwchar>
#include <string>
#include "Utf8Helpers.h"

namespace Methode {

// [PATH-METHODE-NO8DOT3] The narrow path CE's InjectDLL needs for OUR DLL: CE copies it into the game and calls
// LoadLibraryA on it. An ASCII longPath as it is. Otherwise the ASCII alias (shortPath: the 8.3 DIRECTORY plus the
// DLL's own long name -- an alias that renames the file is ignored), else the EXACT narrowing of longPath in codePage
// (WC_NO_BEST_FIT_CHARS + used-default: best fit names another folder, '?' names none), else "" -- the caller
// refuses, naming the cause. The old code checked only the first narrowing and re-narrowed the alias with
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
    auto ascii = [](const wchar_t* w) {
        for (const wchar_t* p = w; p && *p; ++p) if (*p >= 0x80) return false;
        return true;
    };
    auto leaf = [](const wchar_t* w) {
        const wchar_t* l = w;
        for (const wchar_t* p = w; p && *p; ++p) if (*p == L'\\' || *p == L'/') l = p + 1;
        return l;
    };
    std::string out;
    // (second review, T-ALIAS-LEAF-PLUGIN -- HIGH, measured) An ASCII path is never aliased: it already reads the
    // same in every code page, and a real 8.3 alias of the FILE renames it (UE5DUM~1.DLL) -- the game then maps our
    // DLL under that name, and every name check after the inject misses it.
    if (longPath && ascii(longPath)) return exact(longPath, out) ? out : std::string{};
    // An alias is usable only if it keeps the DLL's own name: the caller shortens the DIRECTORY, not the file.
    const bool haveShort = shortPath && longPath && std::wcscmp(shortPath, longPath) != 0
                           && _wcsicmp(leaf(shortPath), leaf(longPath)) == 0;
    // (skeptic CEINJ-4) CE makes these bytes in ITS code page, the game decodes them in its own (Locale Emulator
    // gives it another): an ASCII alias reads the same in every code page, so it wins when there is one.
    if (haveShort && ascii(shortPath) && exact(shortPath, out)) return out;
    if (exact(longPath, out)) return out;
    if (haveShort && exact(shortPath, out)) return out;
    return {};
}

// [PATH-MODULE-NAME-UTF8] Cheat Engine's OWN name for a module file, as UTF-8 -- exactly what ANSI Module32First
// puts in szModule, which CE's symbol handler names the module with (WinCPToUTF8, symbolhandler.pas ~6040): the
// leaf narrowed with best fit (flags 0: Café -> Cafe, ™ -> ?), then cut after its LAST byte 0x5C, because
// Module32First keeps what follows the ANSI path's last '\\' byte and a DBCS trail byte can be 0x5C (Big5 功 = A5 5C,
// Shift-JIS ソ = 83 5C: CE knows 功夫-….exe as 夫-….exe -- measured, skeptic MODVIEW-5C-TRAIL). Widened back and UTF-8.
// codePage CP_ACP is read as GetACP() -- the GAME's; the UI computes its own view for everything IT hands CE
// (ISystemCodePage.AnsiModuleName), because a game started under another locale has another ACP. Used for
// get_ce_pointer_info's ce_base. Any API failure falls back to the plain UTF-8 name.
inline std::string CeModuleNameUtf8(const wchar_t* leaf, size_t len, UINT codePage)
{
    if (!leaf || len == 0) return {};
    const UINT cp = (codePage == CP_ACP) ? GetACP() : codePage;
    const int wlen = static_cast<int>(len);
    const int n = WideCharToMultiByte(cp, 0, leaf, wlen, nullptr, 0, nullptr, nullptr);
    if (n <= 0) return Utf8Helpers::EncodeUtf16(leaf, len);
    std::string ansi(static_cast<size_t>(n), '\0');
    if (WideCharToMultiByte(cp, 0, leaf, wlen, ansi.data(), n, nullptr, nullptr) != n)
        return Utf8Helpers::EncodeUtf16(leaf, len);
    const size_t cut = ansi.find_last_of('\\');   // a BYTE search: a DBCS trail byte counts, as it does for Windows
    if (cut != std::string::npos) ansi.erase(0, cut + 1);
    if (ansi.empty()) return {};
    const int m = MultiByteToWideChar(cp, 0, ansi.data(), static_cast<int>(ansi.size()), nullptr, 0);
    if (m <= 0) return Utf8Helpers::EncodeUtf16(leaf, len);
    std::wstring back(static_cast<size_t>(m), L'\0');
    if (MultiByteToWideChar(cp, 0, ansi.data(), static_cast<int>(ansi.size()), back.data(), m) != m)
        return Utf8Helpers::EncodeUtf16(leaf, len);
    return Utf8Helpers::EncodeUtf16(back.c_str(), back.size());
}

} // namespace Methode
