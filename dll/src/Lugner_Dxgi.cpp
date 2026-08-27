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

// 1 once DxgiProxy_EnsureResolved has actually ATTEMPTED resolution (whether or not the
// real dxgi.dll loaded). Read by the asm thunks, which need to tell two very different
// reasons a slot can be null apart:
//
//   mResolveAttempted == 0 → the resolver REFUSED because our CRT was not up yet
//                            (Lugner::CrtReady() was false). The thunk answers 0 without
//                            forwarding; the next call after DllMain resolves normally.
//   mResolveAttempted == 1 → the resolver ran and this name is genuinely absent from the
//                            host's System32 dxgi.dll. The thunk keeps the pre-existing
//                            `jmp rax` with rax == 0, i.e. a loud crash — a DELIBERATE
//                            choice recorded in Lugner_Winmm.asm (a stub returning 0 is
//                            worse there, where 0 == TIMERR_NOERROR would silently no-op
//                            timeBeginPeriod). Do not collapse these two cases.
//
// Plain uintptr_t, constant-initialised, so it lives in zero-filled .data and is readable
// before the CRT exists — same requirement as Lugner::g_crtReady.
extern "C" uintptr_t mResolveAttempted = 0;

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
    // ⛔ PRE-CRT GATE — the first thing, before the lock, before anything that could
    // allocate. See Lugner::g_crtReady for the full story; the short version is that
    // Windows' AppCompat shim engine calls dxgi.dll!SetAppCompatStringPointer from
    // apphelp!SE_DllLoaded, i.e. after our module is MAPPED but before
    // _DllMainCRTStartup has run, and everything below this line — LoadLibraryW under a
    // still-held loader lock, and LOG_* which allocates a std::string for the timestamp —
    // is unsafe at that point. Returning leaves mProcs[] null and mResolveAttempted 0,
    // which the asm thunk reads as "answer 0, do not forward"; the game's own first dxgi
    // call, long after DllMain, then resolves normally.
    //
    // ⚠ Deliberately does NOT latch anything. An early-out that set s_done here would
    // make one shim call permanently disable the proxy.
    if (!Lugner::CrtReady()) { Lugner::NotePreCrtCall(); return; }

    // ⛔ SAME-THREAD RE-ENTRY GATE. The LoadLibraryW below re-enters this function on this
    // very thread when the game carries an AppCompat layer — see Lugner::ResolveReentry for
    // the measured stack. Bail out and let the nested thunk answer 0 without forwarding.
    //
    // ⚠ There is NO LOCK here any more, and that is the actual fix for the hang, not this
    // guard. An SRWLOCK across LoadLibraryW self-deadlocked on exactly that re-entry. B43's
    // argument for why winmm needs none applies verbatim: the stores are aligned and
    // pointer-sized, racers write identical values, and the thunk's own null test is what
    // stops anyone seeing a half-filled table.
    Lugner::ResolveReentry guard;
    if (guard.reentered) return;

    wchar_t realPath[MAX_PATH] = {};
    // false => refuse. The old code discarded GetSystemDirectoryW's return and
    // formatted an empty buffer into a drive-root-relative `\dxgi.dll`. (AD18)
    HMODULE real = Lugner::SystemDllPath(L"dxgi.dll", realPath, MAX_PATH)
                 ? LoadLibraryW(realPath) : nullptr;
    // Captured AT the failure, before GetProcAddress/logging clobber it. (AB10/AB11)
    const DWORD loadErr = real ? 0 : GetLastError();
    int resolved = 0;
    if (real) {
        for (int i = 0; i < kDxgiExportCount; ++i) {
            mProcs[i] = reinterpret_cast<uintptr_t>(GetProcAddress(real, kDxgiExports[i]));
            if (mProcs[i]) ++resolved;
        }
    }
    // Published only after mProcs[] is fully written, so no thunk observes "resolution
    // finished" over a half-filled table.
    mResolveAttempted = 1;

    // Claimed once: this is file I/O, and a duplicate line would misreport a race as two
    // resolves. After the work, never around it — same shape as the winmm twin.
    static volatile LONG s_logged = 0;
    if (InterlockedCompareExchange(&s_logged, 1, 0) != 0) return;
    if (real) {
        LOG_INFO("dxgi proxy: lazily forwarded %d/%d exports to real System32 dxgi.dll", resolved, kDxgiExportCount);
    } else {
        LOG_ERROR("dxgi proxy: FAILED to load real System32 dxgi.dll (err=%lu) — forwarded calls will crash",
                  loadErr);
    }
}

#endif // UE5_PROXY_DXGI_BUILD
