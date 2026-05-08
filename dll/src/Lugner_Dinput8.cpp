// ============================================================
// Lugner_Dinput8 — dinput8.dll forwarding proxy
//
// Compiled only for UE5_PROXY_DINPUT8_BUILD. Loads the real
// dinput8.dll from System32 and forwards all 5 exports.
//
// Why dinput8 in addition to version.dll? Some games use anti-
// tampering or installers that interfere with version.dll
// hijacking. dinput8 is the de facto fallback for UE / Unity
// proxy injection (used by SpecialK, RE-UE4SS etc.).
//
// Only 5 exports vs version.dll's 17 → minimal forwarding code.
//
// Mutual exclusion: when both proxy DLLs are present in the game
// folder, only the first to load runs full init. The second
// becomes a passive forwarder — see Heiter.cpp's mutex logic.
// ============================================================

#ifdef UE5_PROXY_DINPUT8_BUILD

#include <Windows.h>
#define LOG_CAT "PROXY"
#include "Sein.h"

// Real dinput8.dll handle — loaded lazily on first call
static HMODULE g_realDinput8 = nullptr;

static HMODULE LoadRealDinput8()
{
    if (g_realDinput8) return g_realDinput8;

    wchar_t systemDir[MAX_PATH] = {};
    GetSystemDirectoryW(systemDir, MAX_PATH);

    wchar_t realPath[MAX_PATH] = {};
    wsprintfW(realPath, L"%s\\dinput8.dll", systemDir);

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

// ── Forwarded exports (5 total) ─────────────────────────────
// Internal names use Proxy_ prefix; ProxyDinput8.def maps them
// to the real export names.

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

// Note: no Cleanup function. Calling FreeLibrary from DLL_PROCESS_DETACH
// is documented as undefined behavior (loader-lock deadlock risk). The
// OS reclaims the loaded dinput8.dll automatically when the host process
// exits.

#endif // UE5_PROXY_DINPUT8_BUILD
