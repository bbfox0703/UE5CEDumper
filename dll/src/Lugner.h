#pragma once

// ============================================================
// Lugner — 呂格納 (騙子 — the Liar)
// Shared helper for the four forwarding proxy DLLs (version /
// dinput8 / dxgi / winmm). Header-only: each proxy is its own DLL
// compiled from its own TU, so there is nowhere to link a .cpp.
//
// SystemDllPath: build "<System32>\<dllName>" or refuse.
// ============================================================

#include <Windows.h>
#include <wchar.h>

namespace Lugner {

// Compose the absolute System32 path of `dllName` into `out`.
// Returns false — leaving `out` an EMPTY STRING — if the directory could not be
// obtained or the result does not fit. Callers must treat false as "do not load".
//
// ⚠ Why this is not four inline copies of `GetSystemDirectoryW` + `wsprintfW`:
//
//   * GetSystemDirectoryW's RETURN WAS DISCARDED in all four proxies. On failure it
//     writes nothing, so the zero-initialised buffer stayed empty and the format
//     produced `L"\dinput8.dll"` — a DRIVE-ROOT-RELATIVE path. LoadLibraryW then
//     resolves it against the current drive, i.e. the proxy loads
//     `<current drive>:\dinput8.dll` into the game process if anything is sitting
//     there. That is a DLL-hijack shape reached by an ordinary API failure, no
//     attacker access to System32 required. Returning 0 is not hypothetical: it is
//     also what happens when the buffer is too small, in which case the return is the
//     REQUIRED SIZE and, again, nothing is written.
//   * wsprintfW takes no destination size. It is capped at 1024 chars internally and
//     will happily run past a MAX_PATH buffer.
//
// Mimic.cpp's timer-resolution loader already had this right (checked return +
// wcsncat_s); the proxies are the copies that did not. Truncation is refused rather
// than accepted — a silently shortened path is another unintended file to load, which
// is the failure we are removing, not a milder version of it. (audit #5 AD18)
inline bool SystemDllPath(const wchar_t* dllName, wchar_t* out, size_t outCount)
{
    if (!out || outCount == 0) return false;
    out[0] = L'\0';
    if (!dllName || !dllName[0]) return false;

    // 0 = failed; >= outCount = buffer too small (the return is the required size and
    // NOTHING was written). Both leave `out` empty, which is exactly the state that
    // used to become a root-relative path.
    const UINT n = GetSystemDirectoryW(out, static_cast<UINT>(outCount));
    if (n == 0 || n >= outCount) { out[0] = L'\0'; return false; }

    // The API omits the trailing backslash except at a drive root ("C:\").
    if (out[n - 1] != L'\\' && wcscat_s(out, outCount, L"\\") != 0) { out[0] = L'\0'; return false; }
    if (wcscat_s(out, outCount, dllName) != 0) { out[0] = L'\0'; return false; }
    return true;
}

} // namespace Lugner
