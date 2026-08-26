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

// ── The CRT-ready latch ────────────────────────────────────────────────────────────
//
// ⚠ AN EXPORT OF OURS CAN BE CALLED BEFORE `_DllMainCRTStartup` HAS RUN, and when it is,
// our CRT does not exist yet: `__acrt_heap` is still NULL, so the first `malloc` becomes
// `HeapAlloc(NULL, …)` and faults in `ntdll!RtlAllocateHeap+0x54`. That is not a
// hypothetical — it is what makes OCTOPATH TRAVELER refuse to start with the dxgi proxy
// deployed, confirmed at the instruction level in
// docs/audit-2026-08-26-dxgi-appcompat-crash.md.
//
// The caller is Windows' own AppCompat shim engine. When a game carries a compat layer
// (Octopath: `HIGHDPIAWARE`), `apphelp.dll` + `AcGenral.dll` load at module [4]/[5] — ahead
// of msvcrt — and `AcGenral!NS_DXGICompat` does
// `GetModuleHandleW(L"dxgi.dll")` → `GetProcAddress("SetAppCompatStringPointer")` → call,
// driven by `apphelp!SE_DllLoaded`, which the loader raises when a module is **MAPPED** —
// before its init routine runs. Our export for that name was a lazy thunk whose resolver
// LOGS, and logging allocates.
//
// ⭐ The lesson is NOT "do less in DllMain". ReShade does MORE under the loader lock (file
// I/O, 44+ hook installs) and boots fine; it simply does not export
// `SetAppCompatStringPointer`, so the shim's `GetProcAddress` returns NULL and it is never
// entered. We DO export it, so we need this latch instead.
//
// ⚠ MUST stay a plain `volatile LONG` with a CONSTANT initialiser. It is read on a path
// where the CRT does not exist, so it has to live in zero-initialised .data with **no
// dynamic initialiser** — anything with a runtime constructor (including a `std::atomic`
// that is not constant-initialised) would be read before it was written. An aligned LONG
// load is atomic on x64, and this is a one-way 0→1 latch, so the read needs no interlock;
// the write uses one anyway because it costs nothing.
inline volatile LONG g_crtReady = 0;

// Number of forwarded calls that arrived before the latch was set. Bumped on a path that
// must not allocate, so it is an InterlockedIncrement and nothing else. Reported ONCE from
// DllMain, when there is finally a logger to report it to — without this, the pre-CRT
// refusal below is completely invisible and the next person debugging "why did the shim not
// apply" has nothing to go on.
inline volatile LONG g_preCrtCalls = 0;

// Called from DLL_PROCESS_ATTACH, before anything else. By then `_DllMainCRTStartup` has
// already run `__acrt_initialize`, so the heap is real.
inline void MarkCrtReady()       { InterlockedExchange(&g_crtReady, 1); }
inline bool CrtReady()           { return g_crtReady != 0; }
inline void NotePreCrtCall()     { InterlockedIncrement(&g_preCrtCalls); }
inline LONG PreCrtCallCount()    { return g_preCrtCalls; }

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
