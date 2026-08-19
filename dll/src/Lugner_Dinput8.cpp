// ============================================================
// Lugner_Dinput8 — dinput8.dll forwarding proxy
//
// Compiled only for UE5_PROXY_DINPUT8_BUILD. Loads the real
// dinput8.dll from System32 and forwards all 6 exports.
//
// Why dinput8 in addition to version.dll? Some games use anti-
// tampering or installers that interfere with version.dll
// hijacking. dinput8 is the de facto fallback for UE / Unity
// proxy injection (used by SpecialK, RE-UE4SS etc.).
//
// Only 6 exports vs version.dll's 17 → minimal forwarding code.
//
// The count is load-bearing, not decorative: a real export we
// fail to forward is BOTH a missing name (a by-name import fails
// process creation with STATUS_ENTRYPOINT_NOT_FOUND, before any
// of our logging exists) AND a stolen ordinal, because link.exe
// hands unpinned exports out from (highest pinned ordinal + 1) in
// name-sorted order — so our UE5_* block slides down into the
// vacated slot and an ordinal import silently calls the wrong
// function. That is why ProxyDinput8.def pins @1..@6 explicitly
// and why tools/check_proxy_exports.py re-derives both halves.
// (Audit PX1 — this file said "5" from day one and was wrong.)
//
// Mutual exclusion: when both proxy DLLs are present in the game
// folder, only the first to load runs full init. The second
// becomes a passive forwarder — see Heiter.cpp's mutex logic.
// ============================================================

#ifdef UE5_PROXY_DINPUT8_BUILD

#include <Windows.h>
#define LOG_CAT "PROXY"
#include "Sein.h"
#include "Lugner.h"

// Real dinput8.dll handle — loaded lazily on first call
static HMODULE g_realDinput8 = nullptr;

static HMODULE LoadRealDinput8()
{
    if (g_realDinput8) return g_realDinput8;

    wchar_t realPath[MAX_PATH] = {};
    if (!Lugner::SystemDllPath(L"dinput8.dll", realPath, MAX_PATH)) {
        LOG_ERROR("Could not resolve the System32 path of dinput8.dll (err=%lu) — refusing "
                  "to load. Forwarded calls will fail, which is the correct outcome: the "
                  "old code fell through to a drive-root-relative path here.",
                  GetLastError());
        return nullptr;
    }

    g_realDinput8 = LoadLibraryW(realPath);
    if (!g_realDinput8) {
        LOG_ERROR("Failed to load real dinput8.dll from %ls (err=%lu)",
                  realPath, GetLastError());
    } else {
        LOG_INFO("Loaded real dinput8.dll: %ls", realPath);
    }
    return g_realDinput8;
}

static FARPROC RealProc(const char* name)
{
    HMODULE real = LoadRealDinput8();
    return real ? GetProcAddress(real, name) : nullptr;
}

// ── Forwarded exports (6 total) ─────────────────────────────
// Internal names use Proxy_ prefix; ProxyDinput8.def maps them
// to the real export names AND pins the real ordinals.

extern "C" HRESULT WINAPI Proxy_DirectInput8Create(
    HINSTANCE hinst, DWORD dwVersion, REFIID riidltf,
    LPVOID* ppvOut, LPVOID punkOuter)
{
    using Fn = HRESULT(WINAPI*)(HINSTANCE, DWORD, REFIID, LPVOID*, LPVOID);
    static auto fn = reinterpret_cast<Fn>(RealProc("DirectInput8Create"));
    return fn ? fn(hinst, dwVersion, riidltf, ppvOut, punkOuter) : E_FAIL;
}

extern "C" HRESULT WINAPI Proxy_DllCanUnloadNow()
{
    using Fn = HRESULT(WINAPI*)();
    static auto fn = reinterpret_cast<Fn>(RealProc("DllCanUnloadNow"));
    return fn ? fn() : S_FALSE;
}

extern "C" HRESULT WINAPI Proxy_DllGetClassObject(
    REFCLSID rclsid, REFIID riid, LPVOID* ppv)
{
    using Fn = HRESULT(WINAPI*)(REFCLSID, REFIID, LPVOID*);
    static auto fn = reinterpret_cast<Fn>(RealProc("DllGetClassObject"));
    return fn ? fn(rclsid, riid, ppv) : E_FAIL;
}

extern "C" HRESULT WINAPI Proxy_DllRegisterServer()
{
    using Fn = HRESULT(WINAPI*)();
    static auto fn = reinterpret_cast<Fn>(RealProc("DllRegisterServer"));
    return fn ? fn() : E_FAIL;
}

extern "C" HRESULT WINAPI Proxy_DllUnregisterServer()
{
    using Fn = HRESULT(WINAPI*)();
    static auto fn = reinterpret_cast<Fn>(RealProc("DllUnregisterServer"));
    return fn ? fn() : E_FAIL;
}

// LPCDIDATAFORMAT WINAPI GetdfDIJoystick(void) — returns a pointer to the
// static c_dfDIJoystick data format. Declared as const void* so this file
// does not have to pull in dinput.h (which would also drag in the DirectX
// SDK headers the rest of the DLL deliberately avoids); the signature is
// no-args / pointer-return either way, so a plain C forwarder is exact and
// the asm jmp-thunk machinery dxgi/winmm need for undocumented internals is
// unnecessary here.
//
// The modern Windows SDK STATICALLY DEFINES this symbol (dinput8.lib carries
// it as a defined COFF symbol, not a short-import record), so a normally-built
// game never imports it. Legacy DirectX-SDK-era consumers and third-party
// dinput8 wrappers do — and for them the export missing is fatal at load time.
extern "C" const void* WINAPI Proxy_GetdfDIJoystick()
{
    using Fn = const void*(WINAPI*)();
    static auto fn = reinterpret_cast<Fn>(RealProc("GetdfDIJoystick"));
    return fn ? fn() : nullptr;
}

// Note: no Cleanup function. Calling FreeLibrary from DLL_PROCESS_DETACH
// is documented as undefined behavior (loader-lock deadlock risk). The
// OS reclaims the loaded dinput8.dll automatically when the host process
// exits.

#endif // UE5_PROXY_DINPUT8_BUILD
