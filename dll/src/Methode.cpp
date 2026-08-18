// ============================================================
// Methode — 梅特戴 (全能分析型 — All-Capable Analyst)
// CEPlugin: CE Plugin Type 5 interface
//
// When loaded by CE from its plugin folder, this adds a
// "UE5CEDumper: Inject & Connect" item to CE's Plugins menu.
//
// Clicking it calls CE's built-in InjectDLL() to load this same
// DLL into the currently attached game process, then calls
// UE5_AutoStart() (which runs UE5_Init + UE5_StartPipeServer).
//
// CE Plugin SDK v6 reference: D:\Github\CE-Handwire-Private\docs\CE-Plugin-API-Reference.md
// Known traps:             D:\Github\CE-Handwire-Private\docs\CE-Plugin-SDK-Notes.md
// ============================================================

#include <Windows.h>
#include <Psapi.h>   // EnumProcessModulesEx, GetModuleFileNameExW
#include <vector>
#include "Utf8Helpers.h"
#include <cstddef>   // offsetof
#include <cstring>
#include <string>
#include <atomic>
#define LOG_CAT "CEP"
#include "Sein.h"

// ── Externals from dllmain.cpp ────────────────────────────────────────────
extern HMODULE g_hDllModule;           // DLL module handle
extern std::atomic<bool> g_isCEPlugin; // Set true here to suppress game-process auto-start

// ── CE SDK v6 constants ───────────────────────────────────────────────────
static constexpr unsigned int CESDK_VERSION = 6;
static constexpr int          ptMainMenu    = 5; // Plugin type 5 = CE main form menu

// ── PluginVersion struct (SDK v6 §1.5) ───────────────────────────────────
// Exactly two fields — do NOT add extras.
// TRAP: second param to GetVersion is int VALUE (sizeof struct ≈ 16 on x64),
//       NOT a pointer.  Dereferencing it → AV at 0x10.
struct CePluginVersion {
    unsigned int version;    // Set to CESDK_VERSION (6)
    char*        pluginname; // Must point to static storage, NOT stack variable
};

// ── Partial ExportedFunctions struct ─────────────────────────────────────
// CE's full struct has 100+ fields. We define only what we need, up to
// InjectDLL (offset 88). The size check in InitializePlugin ensures CE's
// struct is at least this large.
//
// Verified field offsets for x64 MSVC (no pragma pack):
//   [0]  sizeofExportedFunctions  int
//   [4]  _pad                     int   (natural alignment padding)
//   [8]  ShowMessage              ptr
//   [16] RegisterFunction         ptr
//   [24] UnregisterFunction       ptr
//   [32] OpenedProcessID          PULONG
//   [40] OpenedProcessHandle      PHANDLE
//   [48] GetMainWindowHandle      ptr
//   [56] AutoAssemble             ptr
//   [64] Assembler                ptr
//   [72] Disassembler             ptr
//   [80] ChangeRegistersAtAddress ptr
//   [88] InjectDLL                ptr
struct ExportedFunctions {
    int   sizeofExportedFunctions;
    int   _pad;                                      // alignment pad

    void (__stdcall *ShowMessage)       (char* message);
    int  (__stdcall *RegisterFunction)  (int pluginid, int type, void* initStruct);
    BOOL (__stdcall *UnregisterFunction)(int pluginid, int functionid);

    ULONG*  OpenedProcessID;     // Dereference to read current process ID
    HANDLE* OpenedProcessHandle; // Dereference to read current process handle

    void* GetMainWindowHandle;
    void* AutoAssemble;
    void* Assembler;
    void* Disassembler;
    void* ChangeRegistersAtAddress;

    BOOL (__stdcall *InjectDLL)(char* dllname, char* functiontocall);
    // (remaining 90+ fields omitted — not used by this plugin)
};

// Compile-time offset guards — build fails if struct layout is wrong
static_assert(offsetof(ExportedFunctions, ShowMessage)        ==  8, "ShowMessage @ 8");
static_assert(offsetof(ExportedFunctions, RegisterFunction)   == 16, "RegisterFunction @ 16");
static_assert(offsetof(ExportedFunctions, UnregisterFunction) == 24, "UnregisterFunction @ 24");
static_assert(offsetof(ExportedFunctions, OpenedProcessID)    == 32, "OpenedProcessID @ 32");
static_assert(offsetof(ExportedFunctions, OpenedProcessHandle)== 40, "OpenedProcessHandle @ 40");
static_assert(offsetof(ExportedFunctions, InjectDLL)          == 88, "InjectDLL @ 88");

// ── Type 5 (ptMainMenu) init struct ──────────────────────────────────────
struct PLUGINTYPE5_INIT {
    char* name;
    void (__stdcall *callbackroutine)(void);
    char* shortcut; // nullptr = no shortcut
};

// ── Plugin state ──────────────────────────────────────────────────────────
static ExportedFunctions g_CE;
static int g_PluginId   = -1;
static int g_MenuItemId = -1;

// Static strings — must NOT be stack variables (CE reads them after return)
static char g_PluginName[]   = "UE5CEDumper";
static char g_MenuName[]     = "UE5CEDumper: Inject && Connect";
static char g_AutoStartFn[]  = "UE5_AutoStart";

// ── Proxy DLL file names we ship ─────────────────────────────────────────────
//
// A hijacked copy lives in the GAME folder. This list is only a cheap PRE-FILTER
// for the module walk — the genuine Windows DLL of the same name, and every
// third-party wrapper that uses the same trick, are rejected by PE ProductName
// in IsOurModule, not by their path. Keep it in sync with `ue5_isAlreadyLoaded`
// in scripts/UE5CEDumper.CT and with Models/ProxyType.cs — a flavour missing
// here means the double-inject guard silently does nothing for it.
static const wchar_t* const kProxyDllNames[] = {
    L"version.dll",
    L"dinput8.dll",
    L"dxgi.dll",
    L"winmm.dll",
};

static bool IsProxyDllName(const wchar_t* fileName) {
    for (const wchar_t* name : kProxyDllNames) {
        if (_wcsicmp(fileName, name) == 0) return true;
    }
    return false;
}

// ── Identity: is this module actually OURS? ──────────────────────────────────
//
// The file NAME cannot answer that. Every proxy flavour we ship is named after
// the Windows DLL it hijacks, so "there is a dxgi.dll loaded from the game
// folder" is equally true of ReShade, Ultimate ASI Loader, SpecialK and every
// other wrapper — a configuration this repo's own docs describe as common. The
// PE ProductName can answer it: every one of our binaries sets it to
// "UE5CEDumper" (dll/src/version.rc), and no third-party wrapper does.
//
// This is deliberately the SAME rule the C# side already uses
// (Services/DumperModuleDetector.cs), so the two detectors cannot disagree
// about whether we are loaded.
//
// A false negative here is safe: UE5_StartPipeServer detects an existing pipe
// and returns INIT_SKIPPED, so at worst we map a second copy that then declines
// to serve. A false POSITIVE is not — it tells the user "already loaded, no
// injection needed" when nothing is loaded, and the pipe never appears.
static bool IsOurModule(const wchar_t* modulePath)
{
    DWORD ignored = 0;
    DWORD size = GetFileVersionInfoSizeW(modulePath, &ignored);
    if (size == 0) return false;   // no VERSIONINFO at all → not one of ours

    std::vector<uint8_t> buf(size);
    if (!GetFileVersionInfoW(modulePath, 0, size, buf.data())) return false;

    // Read the ProductName from whichever language block the file actually has,
    // rather than assuming 040904B0 — a fixed codepage guess is why this kind of
    // check usually "works on my machine".
    struct LangCodePage { WORD language; WORD codePage; };
    LangCodePage* langs = nullptr;
    UINT langBytes = 0;
    if (!VerQueryValueW(buf.data(), L"\\VarFileInfo\\Translation",
                        reinterpret_cast<void**>(&langs), &langBytes) || !langs) {
        return false;
    }

    const UINT langCount = langBytes / sizeof(LangCodePage);
    for (UINT i = 0; i < langCount; ++i) {
        wchar_t sub[64];
        swprintf_s(sub, L"\\StringFileInfo\\%04x%04x\\ProductName",
                   langs[i].language, langs[i].codePage);

        wchar_t* value = nullptr;
        UINT valueLen = 0;
        if (VerQueryValueW(buf.data(), sub,
                           reinterpret_cast<void**>(&value), &valueLen)
            && value && valueLen > 0
            && _wcsicmp(value, L"UE5CEDumper") == 0) {
            return true;
        }
    }
    return false;
}

// ── Helper: detect if UE5CEDumper is already present in the target process ───
//
// Catches the two cases that would otherwise cause a redundant inject:
//   1. UE5Dumper.dll already injected (e.g. the user clicked the menu twice)
//   2. One of our proxy DLLs (see kProxyDllNames) hijacked from the game folder
//
// Both are confirmed by PE ProductName, never by file name alone — see
// IsOurModule. This used to accept ANY module named version/dinput8/dxgi/winmm
// that was not under System32, which made ReShade's dxgi.dll look exactly like
// our proxy: the menu then reported "already loaded, no injection needed", let
// the user go away happy, and no pipe ever appeared (audit #4 B29).
//
// Wide throughout: the previous ANSI enumeration rendered every character the
// code page could not represent as '?', so a perfectly good path like
// D:\Games\EVERSPACE™ 2\... was displayed and logged as EVERSPACE? 2 — an
// unpasteable path in the one message that is supposed to help. The sibling in
// Heiter.cpp was fixed the same way.
//
// CE has already attached to the target process by the time the menu callback
// runs, so we use the OpenedProcessHandle to enumerate its modules.
static bool IsAlreadyLoadedInTarget(HANDLE hProcess, std::string& outName,
                                    std::string& outPath)
{
    if (!hProcess) return false;

    // Size the buffer to the process's ACTUAL module count (audit #5 AB12). A fixed
    // 1024-slot array silently truncated a process with more modules, and the cost is
    // no longer just a redundant map: this same walk is the POST-INJECT verification,
    // and freshly-loaded modules append to the END of the list — so a successful
    // inject landing past slot 1024 was reported to the user as "not mapped". A size
    // probe (nullptr, 0) returns the needed byte count; allocate, then enumerate.
    DWORD cbNeeded = 0;
    if (!EnumProcessModulesEx(hProcess, nullptr, 0, &cbNeeded, LIST_MODULES_ALL)
        || cbNeeded == 0) {
        LOG_WARN("CEPlugin: EnumProcessModulesEx size probe failed (err=%lu)", GetLastError());
        return false;
    }
    std::vector<HMODULE> modules(cbNeeded / sizeof(HMODULE));
    DWORD cbNeeded2 = 0;
    if (!EnumProcessModulesEx(hProcess, modules.data(),
                              static_cast<DWORD>(modules.size() * sizeof(HMODULE)),
                              &cbNeeded2, LIST_MODULES_ALL)) {
        LOG_WARN("CEPlugin: EnumProcessModulesEx failed (err=%lu)", GetLastError());
        return false;
    }
    // A module can load between the two calls, so re-clamp: never read past what the
    // second call actually filled, nor past the buffer we sized.
    DWORD filled = cbNeeded2 / sizeof(HMODULE);
    DWORD cap    = static_cast<DWORD>(modules.size());
    DWORD count  = filled < cap ? filled : cap;

    for (DWORD i = 0; i < count; ++i) {
        wchar_t modPath[MAX_PATH] = {};
        if (!GetModuleFileNameExW(hProcess, modules[i], modPath, MAX_PATH))
            continue;

        const wchar_t* slash = wcsrchr(modPath, L'\\');
        const wchar_t* fileName = slash ? slash + 1 : modPath;

        // Name is only a cheap PRE-FILTER, to avoid reading version info for
        // every module in the process. It never decides anything on its own.
        const bool nameLooksLikeOurs =
            (_wcsicmp(fileName, L"UE5Dumper.dll") == 0) || IsProxyDllName(fileName);
        if (!nameLooksLikeOurs) continue;

        if (!IsOurModule(modPath)) {
            // A same-named module that is NOT ours: the genuine Windows copy, or
            // a third-party wrapper (ReShade, Ultimate ASI Loader, SpecialK…).
            // Worth logging — it is exactly the case that used to be misread.
            LOG_INFO("CEPlugin: '%ls' is loaded but is not ours (path=%ls) — not a UE5CEDumper proxy",
                     fileName, modPath);
            continue;
        }

        outName = Utf8Helpers::EncodeUtf16(fileName, wcslen(fileName));
        outPath = Utf8Helpers::EncodeUtf16(modPath, wcslen(modPath));
        return true;
    }

    return false;
}

// ── Type 5 callback: runs on CE's main thread when the user clicks the menu item
static void __stdcall OnInjectAndConnect()
{
    LOG_INFO("CEPlugin: OnInjectAndConnect triggered");

    // 1. Check that CE has a process attached
    if (!g_CE.OpenedProcessID || *g_CE.OpenedProcessID == 0) {
        LOG_WARN("CEPlugin: No process attached (OpenedProcessID=0)");
        g_CE.ShowMessage(const_cast<char*>(
            "UE5CEDumper: Please attach CE to a UE5 game process first."));
        return;
    }

    ULONG pid = *g_CE.OpenedProcessID;

    // 1.5. Check if UE5CEDumper is already loaded in the target process.
    // Common scenario: user has version.dll proxy in the game folder and
    // then clicks the inject menu — we should bail out before mapping a
    // duplicate copy. Heiter.cpp's pipe-existence guard is a fallback;
    // detecting here avoids loading the DLL into VA at all.
    HANDLE hTarget = g_CE.OpenedProcessHandle ? *g_CE.OpenedProcessHandle : nullptr;
    {
        std::string presentName, presentPath;
        if (IsAlreadyLoadedInTarget(hTarget, presentName, presentPath)) {
            LOG_INFO("CEPlugin: Already loaded as '%s' (path=%s) — skipping inject",
                     presentName.c_str(), presentPath.c_str());
            char msg[1024];
            snprintf(msg, sizeof(msg),
                "UE5CEDumper is already loaded in this process as '%s'.\n\n"
                "Path: %s\n\n"
                "No injection needed — just launch UE5DumpUI.exe and click Connect.\n"
                "Pipe: \\\\.\\pipe\\UE5DumpBfx",
                presentName.c_str(), presentPath.c_str());
            g_CE.ShowMessage(msg);
            return;
        }
    }

    // 2. Resolve this DLL's own path (running in CE's process). WIDE, not ...A:
    //    GetModuleFileNameA narrows through the ANSI code page, mapping any character
    //    it cannot represent to '?'. Unlike this file's two DISPLAY sites (already
    //    wide-fixed), the path here is FUNCTIONAL — handed to CE's InjectDLL, which
    //    LoadLibrary's it in the target — so a mangled path (CE installed under a
    //    non-ANSI folder) fails the load on a path that does not exist. (audit #5 AB13)
    wchar_t dllPathW[MAX_PATH] = {};
    if (!GetModuleFileNameW(g_hDllModule, dllPathW, MAX_PATH)) {
        LOG_ERROR("CEPlugin: GetModuleFileNameW failed (error=%lu)", GetLastError());
        g_CE.ShowMessage(const_cast<char*>(
            "UE5CEDumper: Failed to resolve DLL path."));
        return;
    }
    // InjectDLL takes a narrow (char*) path. Narrow the long path with the system
    // ANSI code page first; if any character is unrepresentable (lpUsedDefaultChar),
    // fall back to the 8.3 SHORT path, which is pure ASCII and survives the narrowing
    // (best-effort — only when the volume keeps 8.3 names). ASCII paths narrow to the
    // exact old bytes, so only the non-ASCII case this fixes changes behaviour.
    char dllPath[MAX_PATH] = {};
    BOOL usedDefault = FALSE;
    int narrowed = WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, dllPathW, -1,
                                       dllPath, sizeof(dllPath), nullptr, &usedDefault);
    if (narrowed == 0 || usedDefault) {
        wchar_t shortW[MAX_PATH] = {};
        if (GetShortPathNameW(dllPathW, shortW, MAX_PATH) > 0)
            narrowed = WideCharToMultiByte(CP_ACP, 0, shortW, -1,
                                           dllPath, sizeof(dllPath), nullptr, nullptr);
    }
    if (narrowed == 0 || dllPath[0] == '\0') {
        LOG_ERROR("CEPlugin: could not narrow DLL path to a loadable ANSI form (err=%lu)",
                  GetLastError());
        g_CE.ShowMessage(const_cast<char*>(
            "UE5CEDumper: Failed to resolve a loadable DLL path."));
        return;
    }

    // Log the EXACT path (UTF-8 of the wide form): the narrow dllPath may be an 8.3
    // alias or otherwise lossy, so the human-readable log uses the wide path.
    const std::string dllPathU8 = Utf8Helpers::EncodeUtf16(dllPathW, wcslen(dllPathW));
    LOG_INFO("CEPlugin: Injecting into PID=%lu | DLL=%s | fn=%s",
             pid, dllPathU8.c_str(), g_AutoStartFn);

    // 3. Inject this DLL into the game process and call UE5_AutoStart.
    //    CE's InjectDLL handles: CreateRemoteThread → LoadLibrary(path)
    //    → GetProcAddress("UE5_AutoStart") → call it in the game process.
    //
    //    UE5_AutoStart is ASYNCHRONOUS since build 2932 (audit #5 AB2): it spawns
    //    the AOB scan + pipe start and returns at once. That is not a style
    //    choice — CE's stub does not wait for us:
    //      CEFuncProc.pas:1346-1360  a hard 10 s ceiling (counter := 10000 div 10)
    //      CEFuncProc.pas:1332-1343  or none at all on the APC path — CreateRemoteAPC
    //                                then a flat sleep(1000)
    //      CEFuncProc.pas:1379-1387  `finally ... virtualfreeex(...)` UNCONDITIONALLY
    //    so a multi-second scan used to have the page it is running on freed out from
    //    under it, and the `ret` crashed the GAME. (CE's own timeout text even says
    //    "Injection routine not freed", which the `finally` above contradicts —
    //    read from the published 7.5 source, 2026-08-15.)
    BOOL ok = g_CE.InjectDLL(dllPath, g_AutoStartFn);
    LOG_INFO("CEPlugin: InjectDLL returned %s", ok ? "TRUE" : "FALSE");

    // 4. Decide what to report by LOOKING, not by trusting that BOOL.
    //
    //    ce_InjectDLL's result is not the outcome of the injection — it is only
    //    "did an exception escape", and CE's own handler swallows the two it
    //    declares (pluginexports.pas:622-640, CEFuncProc.pas:1050-1051,
    //    1391-1396, read from the 7.5 source):
    //      * "Failed injecting the DLL"    → EInjectError → CAUGHT inside, falls
    //        back to forceLoadModule, and ce_InjectDLL returns **TRUE**.
    //      * "took longer than 10 seconds" → a plain Exception → escapes → FALSE.
    //      * "Failed executing the function of the dll" → EInjectDLLFunctionFailure,
    //        which is a SIBLING of EInjectError rather than a subclass, so it also
    //        escapes → FALSE.
    //    So the BOOL can be true on a real failure and false while the DLL is
    //    loaded and working. We already have a direct observation — the same
    //    module-list walk used above — so use it and stop guessing.
    //
    //    Short retry window: on the APC path CE only sleeps 1 s and does not wait
    //    on the loader at all, so the module can still be appearing.
    std::string loadedName, loadedPath;
    bool present = false;
    for (int attempt = 0; attempt < 5 && !present; ++attempt) {
        if (attempt) Sleep(100);
        present = IsAlreadyLoadedInTarget(hTarget, loadedName, loadedPath);
    }
    LOG_INFO("CEPlugin: post-inject module check: %s (ok=%d)",
             present ? loadedPath.c_str() : "NOT PRESENT", ok ? 1 : 0);

    if (!present) {
        char msg[1024];
        snprintf(msg, sizeof(msg),
            "UE5CEDumper: Injection failed — the DLL is not mapped in the target.\n\n"
            "%s\n\n"
            "Usual causes: the target is 32-bit, anti-cheat blocked the load, or CE\n"
            "needs to run as administrator.\n"
            "Details: Logs\\UE5Dumper-*.log",
            ok ? "(Cheat Engine reported success, but the module is absent — CE\n"
                 "swallows \"Failed injecting the DLL\" internally and still returns\n"
                 "true, so its result cannot be trusted on its own.)"
               : "(Cheat Engine also reported failure.)");
        g_CE.ShowMessage(msg);
        return;
    }

    // Mapped. The scan runs on its own thread now, so this is genuinely "started",
    // not "finished" — the UI's Connect polls the pipe until it is up.
    g_CE.ShowMessage(const_cast<char*>(
        "UE5CEDumper: DLL injected — GObjects/GNames scan started in the background.\n"
        "It takes a few seconds; connect UE5DumpUI.exe to \\\\.\\pipe\\UE5DumpBfx\n"
        "(check the log file if the scan fails on first run)"));
}

// ── Required CE Plugin exports ────────────────────────────────────────────
extern "C" {

// ── CEPlugin_GetVersion ───────────────────────────────────────────────────
// CE calls this to verify the plugin and retrieve its name.
// Returns TRUE. Second param is an int VALUE (sizeof struct), NOT a pointer.
__declspec(dllexport)
BOOL __stdcall CEPlugin_GetVersion(CePluginVersion* pv, int /*sizeofpluginversion*/)
{
    if (!pv) return FALSE;
    // Claim CE-plugin identity HERE, not only in InitializePlugin. CE calls
    // InitializePlugin only when the plugin is ENABLED, so a registered-but-unticked
    // plugin used to get DllMain + GetVersion and nothing else — and DllMain's 1 s
    // wait cannot be beaten by a human ticking a checkbox. The auto-start thread then
    // ran a full AOB scan and opened \\.\pipe\UE5DumpBfx INSIDE cheatengine-x86_64.exe.
    // GetVersion is called for enabled and disabled plugins alike, and it is called
    // early, which is exactly the signal the race needed. (B34)
    g_isCEPlugin.store(true);
    pv->version    = CESDK_VERSION;
    pv->pluginname = g_PluginName;
    return TRUE;
}

// ── CEPlugin_InitializePlugin ─────────────────────────────────────────────
// CE calls this when the plugin is enabled. We copy the ExportedFunctions
// struct (partial copy — only our truncated fields) and register a Type 5
// Main Menu item.
__declspec(dllexport)
BOOL __stdcall CEPlugin_InitializePlugin(ExportedFunctions* ef, int pluginid)
{
    if (!ef) return FALSE;

    // Mark this instance as the CE plugin host so the DllMain auto-start
    // thread (which waits 1 second before checking) will skip auto-init.
    g_isCEPlugin.store(true);

    // Sanity check: CE's declared struct size must be >= our partial struct.
    // Must be checked BEFORE copying to avoid reading past the source buffer.
    if (ef->sizeofExportedFunctions < static_cast<int>(sizeof(ExportedFunctions)))
        return FALSE;

    // Copy only our truncated struct portion from CE's full struct.
    // CE's struct is always larger than ours — safe because we verified offsets.
    g_CE       = *ef;
    g_PluginId = pluginid;

    // Register Type 5 (ptMainMenu) — adds item to CE's main Plugins menu.
    PLUGINTYPE5_INIT init = {};
    init.name            = g_MenuName;    // "UE5CEDumper: Inject && Connect"
    init.callbackroutine = OnInjectAndConnect;
    init.shortcut        = nullptr;

    g_MenuItemId = g_CE.RegisterFunction(pluginid, ptMainMenu, &init);
    LOG_INFO("CEPlugin: InitializePlugin pluginid=%d menuItemId=%d ef_size=%d",
             pluginid, g_MenuItemId, g_CE.sizeofExportedFunctions);
    return (g_MenuItemId != -1) ? TRUE : FALSE;
}

// ── CEPlugin_DisablePlugin ────────────────────────────────────────────────
// CE calls this when the plugin is disabled or CE is closing.
// Unregister the menu item to free CE's internal state.
__declspec(dllexport)
BOOL __stdcall CEPlugin_DisablePlugin(void)
{
    if (g_MenuItemId != -1 && g_PluginId != -1) {
        g_CE.UnregisterFunction(g_PluginId, g_MenuItemId);
        g_MenuItemId = -1;
    }
    return TRUE;
}

} // extern "C"
