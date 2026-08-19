// ============================================================
// Lugner_Dxgi — dxgi.dll forwarding proxy (data + resolver half)
//
// Compiled only for UE5_PROXY_DXGI_BUILD. Resolves the real
// dxgi.dll exports from System32 into mProcs[], which the asm
// jmp-thunks in Lugner_Dxgi.asm jump through. The .def
// (ProxyDxgi.def) maps each Windows export name to its thunk.
//
// Why dxgi in addition to version.dll / dinput8.dll? Some games
// import neither version.dll nor dinput8.dll, so those proxies
// are never loaded by the OS at all (their forwarders are dead
// weight). dxgi.dll, by contrast, is statically imported by every
// D3D11/D3D12 Unreal Engine game on Windows — making it the
// reliable hijack target for that population. (Observed concrete
// case: a SQUARE ENIX UE4.27 demo that imports dxgi + winmm but
// neither version nor dinput8.)
//
// Unlike Lugner.cpp / Lugner_Dinput8.cpp — which use plain C
// forwarders because every version/dinput8 export has a known,
// documented signature — dxgi exports several undocumented
// internals (DXGID3D10*, Compat*, PIX*) whose prototypes we don't
// know. So forwarding goes through signature-agnostic asm jmp
// trampolines (Lugner_Dxgi.asm) instead. This mirrors RE-UE4SS's
// proxy generator (vendor/RE-UE4SS/UE4SS/proxy_generator).
//
// LAZY resolution: the real dxgi exports are resolved on the FIRST
// forwarded dxgi call (from the asm thunks, via DxgiProxy_EnsureResolved
// below) — NOT eagerly in DllMain. An earlier version resolved eagerly in
// DllMain; that ran LoadLibrary(real dxgi.dll) under the loader lock while
// the EXE's static imports were still being initialised, and crashed games
// that import dxgi.dll directly and before d3d11.dll (e.g. Octopath
// Traveler): the real dxgi was not yet mapped, so the eager load forced a
// full recursive load of dxgi + its dependency tree under the lock, dying
// before our logger even came up. The first real dxgi call
// (CreateDXGIFactory1 during RHI init) happens on a game thread AFTER
// DllMain returns and the loader lock is released — the safe place to load,
// exactly like the version.dll / dinput8.dll proxies.
// ============================================================

#ifdef UE5_PROXY_DXGI_BUILD

#include <Windows.h>
#include <cstdint>
#define LOG_CAT "PROXY"
#include "Sein.h"
#include "Lugner.h"

// Real dxgi export addresses, indexed to match the f<N> thunks in
// Lugner_Dxgi.asm and the "name = f<N>" map in ProxyDxgi.def.
// The .asm references this exact symbol via `extern mProcs:QWORD`,
// so it must have C linkage (no name mangling) and the matching name.
static constexpr int kDxgiExportCount = 20;  // f0..f19 — MUST match ProxyDxgi.def + the .asm thunk order
extern "C" uintptr_t mProcs[kDxgiExportCount] = { 0 };

// Export names in f0..f19 order. MUST stay in sync with ProxyDxgi.def
// and the asm thunk order. Resolution is by NAME (version-robust: a name
// absent on some Windows build simply yields a null slot, which only
// matters if that rarely-used internal is ever called).
static const char* const kDxgiExports[kDxgiExportCount] = {
    "ApplyCompatResolutionQuirking",    // f0  @1
    "CompatString",                     // f1  @2
    "CompatValue",                      // f2  @3
    "DXGIDumpJournal",                  // f3  @4
    "PIXBeginCapture",                  // f4  @5
    "PIXEndCapture",                    // f5  @6
    "PIXGetCaptureState",               // f6  @7
    "SetAppCompatStringPointer",        // f7  @8
    "UpdateHMDEmulationStatus",         // f8  @9
    "CreateDXGIFactory",                // f9  @10
    "CreateDXGIFactory1",               // f10 @11
    "CreateDXGIFactory2",               // f11 @12
    "DXGID3D10CreateDevice",            // f12 @13
    "DXGID3D10CreateLayeredDevice",     // f13 @14
    "DXGID3D10GetLayeredDeviceSize",    // f14 @15
    "DXGID3D10RegisterLayers",          // f15 @16
    "DXGIDeclareAdapterRemovalSupport", // f16 @17
    "DXGIDisableVBlankVirtualization",  // f17 @18
    "DXGIGetDebugInterface1",           // f18 @19
    "DXGIReportAdapterConfiguration",   // f19 @20
};

// Populate mProcs from the real System32 dxgi.dll, once. Called from the
// asm forwarding thunks (ResolveAll in Lugner_Dxgi.asm) on the FIRST
// forwarded dxgi call.
//
// SAFE to LoadLibrary here (unlike the old DllMain path): the first dxgi
// call (CreateDXGIFactory1 during the game's RHI init) runs on a game
// thread after the EXE entry point — i.e. after every static-import
// DllMain has completed and the loader lock has been released.
//
// The SRWLOCK serialises concurrent first-callers: a second thread blocks
// until the first finishes, so it never observes a half-populated mProcs[]
// (would otherwise jump through a null slot). In practice the first dxgi
// call is single-threaded RHI init, so the lock is essentially uncontended.
//
// Logging here is safe and immediate: Sein::Init + InitProcessMirror ran in
// DllMain before any game code executed, so the logger is fully up. (In the
// rare passive-forwarder case where Sein was never initialised, Sein routes
// to its early buffer — harmless.)
extern "C" void DxgiProxy_EnsureResolved()
{
    static SRWLOCK s_lock = SRWLOCK_INIT;
    static bool    s_done = false;

    AcquireSRWLockExclusive(&s_lock);
    if (!s_done) {
        s_done = true;

        wchar_t realPath[MAX_PATH] = {};
        // false => refuse. The old code discarded GetSystemDirectoryW's return and
        // formatted an empty buffer into a drive-root-relative `\dxgi.dll`. (AD18)
        HMODULE real = Lugner::SystemDllPath(L"dxgi.dll", realPath, MAX_PATH)
                     ? LoadLibraryW(realPath) : nullptr;
        if (real) {
            int resolved = 0;
            for (int i = 0; i < kDxgiExportCount; ++i) {
                mProcs[i] = reinterpret_cast<uintptr_t>(GetProcAddress(real, kDxgiExports[i]));
                if (mProcs[i]) ++resolved;
            }
            LOG_INFO("dxgi proxy: lazily forwarded %d/%d exports to real System32 dxgi.dll", resolved, kDxgiExportCount);
        } else {
            LOG_ERROR("dxgi proxy: FAILED to load real System32 dxgi.dll (err=%lu) — forwarded calls will crash",
                      GetLastError());
        }
    }
    ReleaseSRWLockExclusive(&s_lock);
}

#endif // UE5_PROXY_DXGI_BUILD
