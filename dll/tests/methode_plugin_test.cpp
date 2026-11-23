// methode_plugin_test.cpp
// UE5CEDumper — the Cheat Engine plugin (Methode.cpp), driven through a FAKE Cheat Engine.
//
// WHY THIS EXISTS
//   Methode.cpp is what Cheat Engine calls: CEPlugin_GetVersion, CEPlugin_InitializePlugin, CEPlugin_DisablePlugin,
//   and the menu callback that injects this DLL into the attached game. Everything it does goes through the
//   ExportedFunctions table CE hands it, so a test that hands it a fake table sees every message it shows, every
//   inject it asks for and every menu item it registers -- with no Cheat Engine and no game.
//
// WHY IT #includes THE .cpp
//   OnInjectAndConnect, IsAlreadyLoadedInTarget, IsOurModule, IsProxyDllName and the ExportedFunctions layout are file
//   statics of Methode.cpp, not API.
//
// THE PROCESS IT "INJECTS INTO" IS THIS ONE
//   OpenedProcessHandle is GetCurrentProcess(), so the plugin's module walk reads this test's own module list. "Our
//   DLL is loaded" is staged with the REAL built UE5Dumper.dll, mapped with DONT_RESOLVE_DLL_REFERENCES: the loader
//   lists it, and its DllMain never runs, so no auto-start thread, no scan and no pipe. A built file rather than a
//   made-up one, because what the plugin checks is the file's own PE ProductName.

#include <windows.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>   // getenv_s: the AddressSanitizer build's negative control
#include <atomic>
#include <string>
#include <utility>
#include <vector>

// The log lines the plugin writes, captured: the module walk's "not ours" verdict is only visible there.
static std::vector<std::string> g_log;
static void Capture(const char* level, const char* fmt, va_list ap) {
    char line[2048];
    vsnprintf(line, sizeof(line), fmt, ap);
    g_log.emplace_back(std::string(level) + " " + line);
}
namespace Sein {
void Info(const char*, const char* fmt, ...)  { va_list ap; va_start(ap, fmt); Capture("INFO", fmt, ap); va_end(ap); }
void Warn(const char*, const char* fmt, ...)  { va_list ap; va_start(ap, fmt); Capture("WARN", fmt, ap); va_end(ap); }
void Error(const char*, const char* fmt, ...) { va_list ap; va_start(ap, fmt); Capture("ERROR", fmt, ap); va_end(ap); }
}  // namespace Sein

// Defined in the DLL's dllmain; here the module whose path the plugin hands CE is this test exe.
HMODULE g_hDllModule = nullptr;
std::atomic<bool> g_isCEPlugin{false};

#include "../src/Methode.cpp"  // NOLINT -- see "WHY IT #includes THE .cpp" above

// ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────────

static int g_pass = 0, g_fail = 0;

static void check(const char* label, bool cond, const char* got = nullptr) {
    if (cond) { ++g_pass; }
    else {
        ++g_fail;
        printf("  FAIL  %s%s%s\n", label, got ? "   got: " : "", got ? got : "");
    }
}
static void blk(const char* name) { printf("- %s\n", name); }
static bool has(const std::string& s, const char* needle) { return s.find(needle) != std::string::npos; }

// ── the fake Cheat Engine ───────────────────────────────────────────────────────────────────────────────────────

static std::vector<std::string> g_shown;
static void __stdcall FakeShowMessage(char* message) { g_shown.emplace_back(message ? message : "(null)"); }

struct Registration { int pluginid; int type; std::string name; void(__stdcall* callback)(void); bool noShortcut; };
static std::vector<Registration> g_registered;
static int g_registerReturns = 7;
static int __stdcall FakeRegister(int pluginid, int type, void* initStruct) {
    const auto* init = static_cast<const PLUGINTYPE5_INIT*>(initStruct);
    g_registered.push_back({ pluginid, type, (init && init->name) ? init->name : "",
                             init ? init->callbackroutine : nullptr, init && !init->shortcut });
    return g_registerReturns;
}

static std::vector<std::pair<int, int>> g_unregistered;
static BOOL __stdcall FakeUnregister(int pluginid, int functionid) {
    g_unregistered.emplace_back(pluginid, functionid);
    return TRUE;
}

// What InjectDLL does: report failure, report success with nothing mapped, or map the dumper and report success.
enum class InjectOutcome { ReportFalse, ReportTrueNothingMapped, MapTheDumper };
static InjectOutcome g_injectOutcome = InjectOutcome::ReportFalse;
static std::vector<std::pair<std::string, std::string>> g_injects;
static std::wstring g_dumperPath;
static std::string  g_dumperPathA;   // the same path, narrow (the build path is ASCII)
static HMODULE g_mappedDumper = nullptr;

static HMODULE MapDumperWithoutRunningIt() {
    return LoadLibraryExW(g_dumperPath.c_str(), nullptr, DONT_RESOLVE_DLL_REFERENCES);
}
static BOOL __stdcall FakeInject(char* dllname, char* functiontocall) {
    g_injects.emplace_back(dllname ? dllname : "", functiontocall ? functiontocall : "");
    if (g_injectOutcome == InjectOutcome::MapTheDumper) {
        g_mappedDumper = MapDumperWithoutRunningIt();
        return g_mappedDumper ? TRUE : FALSE;
    }
    return g_injectOutcome == InjectOutcome::ReportTrueNothingMapped ? TRUE : FALSE;
}

static ULONG  g_pid = 0;
static HANDLE g_process = nullptr;

static ExportedFunctions MakeFakeCe() {
    ExportedFunctions ef = {};
    ef.sizeofExportedFunctions = static_cast<int>(sizeof(ExportedFunctions));
    ef.ShowMessage         = &FakeShowMessage;
    ef.RegisterFunction    = &FakeRegister;
    ef.UnregisterFunction  = &FakeUnregister;
    ef.OpenedProcessID     = &g_pid;
    ef.OpenedProcessHandle = &g_process;
    ef.InjectDLL           = &FakeInject;
    return ef;
}

// The message the last menu click showed, or "" when it showed none.
static std::string LastShown() { return g_shown.empty() ? std::string() : g_shown.back(); }

// What every failure dialog must say about the logs, checked on the text actually shown.
static void CheckLogDetails(const char* which, const std::string& msg) {
    std::string label = std::string("LOGPATH: the ") + which + " dialog points at init-0.log under %LOCALAPPDATA%";
    check(label.c_str(), has(msg, "init-0.log under %LOCALAPPDATA%\\UE5CEDumper\\Logs\\"), msg.c_str());
    label = std::string("LOGPATH: the ") + which + " dialog names CE's AVX2 folder, which CE starts on most CPUs";
    check(label.c_str(), has(msg, "cheatengine-x86_64-SSE4-AVX2"), msg.c_str());
    label = std::string("LOGPATH: the ") + which + " dialog never points at the flat UE5Dumper-*.log, which does not exist";
    check(label.c_str(), !has(msg, "UE5Dumper-*.log"), msg.c_str());
}

int main() {
    // UNBUFFERED: a crash mid-run still shows what was already established.
    setvbuf(stdout, nullptr, _IONBF, 0);
    printf("methode_plugin_test — the Cheat Engine plugin, driven through a fake Cheat Engine\n");
#ifdef __SANITIZE_ADDRESS__
    // [CPP-HARDENING-2026-11-14] The AddressSanitizer build only: the banner and negative control
    // build_dll.py --asan --run needs (a deliberate heap overflow that must be reported).
    printf("asan: on\n");
    {
        size_t negLen = 0;
        if (getenv_s(&negLen, nullptr, 0, "UE5_ASAN_NEGATIVE_CONTROL") == 0 && negLen > 0) {
            char* volatile p = new char[16];
            p[16] = 1;   // heap-buffer-overflow: ASan ends the process here
            delete[] p;
        }
    }
#endif

    // The built UE5Dumper.dll, from the path CMake hands the build ("/" separators; the loader wants "\").
    {
        const char* built = METHODE_TEST_DUMPER_DLL;
        for (const char* c = built; *c; ++c) {
            const char ch = (*c == '/') ? '\\' : *c;
            g_dumperPathA.push_back(ch);
            g_dumperPath.push_back(static_cast<wchar_t>(static_cast<unsigned char>(ch)));
        }
        check("setup: the built UE5Dumper.dll exists", GetFileAttributesW(g_dumperPath.c_str()) != INVALID_FILE_ATTRIBUTES,
              built);
    }
    g_hDllModule = GetModuleHandleW(nullptr);

    {   blk("GETVERSION - CE reads the plugin's name, and the DLL learns it is the plugin");
        check("GETVERSION: a null struct is refused", CEPlugin_GetVersion(nullptr, 16) == FALSE);
        check("GETVERSION control: ...and claims nothing", !g_isCEPlugin.load());
        CePluginVersion pv = {};
        check("GETVERSION: TRUE", CEPlugin_GetVersion(&pv, static_cast<int>(sizeof(pv))) == TRUE);
        check("GETVERSION: SDK version 6", pv.version == 6);
        check("GETVERSION: named UE5CEDumper", pv.pluginname && std::string(pv.pluginname) == "UE5CEDumper");
        check("GETVERSION ⭐: the DLL is now marked as CE's plugin, so its auto-start stays out of CE's process",
              g_isCEPlugin.load());
    }

    void(__stdcall* menuClick)(void) = nullptr;
    {   blk("INIT - the plugin registers one main-menu item, and only on a table it can read");
        g_isCEPlugin.store(false);
        check("INIT: a null table is refused", CEPlugin_InitializePlugin(nullptr, 3) == FALSE);

        ExportedFunctions shortTable = MakeFakeCe();   // not `small`: rpcndr.h defines that as `char`
        shortTable.sizeofExportedFunctions = static_cast<int>(sizeof(ExportedFunctions)) - 8;
        check("INIT ⭐: a table shorter than the fields it reads is refused",
              CEPlugin_InitializePlugin(&shortTable, 3) == FALSE);
        check("INIT ⭐: ...and nothing is registered from it", g_registered.empty());

        ExportedFunctions ef = MakeFakeCe();
        g_registerReturns = -1;
        check("INIT: a registration CE refuses is a failed init", CEPlugin_InitializePlugin(&ef, 3) == FALSE);
        g_registered.clear();

        g_registerReturns = 7;
        check("INIT: TRUE", CEPlugin_InitializePlugin(&ef, 3) == TRUE);
        check("INIT: the DLL is marked as CE's plugin", g_isCEPlugin.load());
        check("INIT: exactly one item registered", g_registered.size() == 1);
        if (!g_registered.empty()) {
            const Registration& r = g_registered.front();
            check("INIT: for this plugin id", r.pluginid == 3);
            check("INIT: as a main-menu item (type 5)", r.type == 5);
            check("INIT: named 'UE5CEDumper: Inject && Connect' (&& shows one '&' in CE's menu)",
                  r.name == "UE5CEDumper: Inject && Connect", r.name.c_str());
            check("INIT: with no shortcut", r.noShortcut);
            check("INIT: with a callback", r.callback != nullptr);
            menuClick = r.callback;
        }
    }
    if (!menuClick) { printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail + 1); return 1; }

    {   blk("NOPROCESS - a click with no process attached asks for one and injects nothing");
        g_pid = 0;
        g_process = nullptr;
        menuClick();
        check("NOPROCESS: asks the user to attach CE first", has(LastShown(), "Please attach CE"), LastShown().c_str());
        check("NOPROCESS: no inject", g_injects.empty());
    }

    g_pid = GetCurrentProcessId();
    g_process = GetCurrentProcess();

    {   blk("INJECT-FALSE - CE reports failure and the module is absent");
        g_injectOutcome = InjectOutcome::ReportFalse;
        g_log.clear();
        menuClick();
        check("INJECT-FALSE: one inject asked for", g_injects.size() == 1);
        if (!g_injects.empty()) {
            char exe[MAX_PATH] = {};
            GetModuleFileNameA(nullptr, exe, MAX_PATH);
            check("INJECT-FALSE: of the plugin DLL's own path (here the test exe's, which is ASCII)",
                  g_injects.back().first == exe, g_injects.back().first.c_str());
            check("INJECT-FALSE ⭐: calling UE5_AutoStart", g_injects.back().second == "UE5_AutoStart",
                  g_injects.back().second.c_str());
        }
        const std::string msg = LastShown();
        check("INJECT-FALSE: says the injection failed", has(msg, "Injection failed"), msg.c_str());
        check("INJECT-FALSE: ...and that CE reported failure too", has(msg, "(Cheat Engine also reported failure.)"),
              msg.c_str());
        CheckLogDetails("inject-failed", msg);

        // System32's version.dll is in this process (the plugin links version.lib): same NAME as our proxy, not ours.
        // The loader spells it VERSION.dll; compare without case.
        bool notOurs = false;
        for (std::string l : g_log) {
            for (char& c : l) c = static_cast<char>(tolower(static_cast<unsigned char>(c)));
            notOurs = notOurs || has(l, "'version.dll' is loaded but is not ours");
        }
        check("IDENTITY ⭐: Windows' own version.dll is walked past by its ProductName, not mistaken for our proxy",
              notOurs);
    }

    // [A2-METHODE-MANUALMAP] CE's TRUE after a failed inject means its forced load (a manual map the module walk cannot
    // see) SUCCEEDED; the APC path also returns TRUE with nothing mapped. So the text is ambiguous and names the
    // setting. [METHODE-MANUALMAP-EXCEPTION-CLAIM] It claims only what a run cannot falsify: "may", never "will crash".
    // [METHODE-STALE-LOGPATH] Every failure dialog points at the per-process logs that exist (CheckLogDetails).
    {   blk("INJECT-TRUE-ABSENT - CE reports success but the module list does not show it: ambiguous");
        g_injectOutcome = InjectOutcome::ReportTrueNothingMapped;
        menuClick();
        const std::string msg = LastShown();
        check("AMBIGUOUS ⭐: says it is one of two things", has(msg, "That means one of two things"), msg.c_str());
        check("AMBIGUOUS ⭐: names CE's \"Always force load modules\" setting",
              has(msg, "\"Always force load modules\""), msg.c_str());
        check("AMBIGUOUS: says a manually mapped UE5Dumper.dll is not supported",
              has(msg, "UE5Dumper.dll is not supported"), msg.c_str());
        check("AMBIGUOUS: says what to do", has(msg, "untick that setting, restart the game, and inject again"),
              msg.c_str());
        check("AMBIGUOUS ⭐: does not call it a failure -- CE's TRUE after a forced load means the load succeeded",
              !has(msg, "Injection failed"), msg.c_str());
        check("AMBIGUOUS: claims no more than a run can show -- no 'cannot handle exceptions'",
              !has(msg, "cannot handle exceptions"), msg.c_str());
        check("AMBIGUOUS: ...and no 'is likely to crash'", !has(msg, "is likely to crash"), msg.c_str());
        check("AMBIGUOUS: does not blame CE's result as untrustworthy -- it is one of two named outcomes",
              !has(msg, "cannot be trusted on its own"), msg.c_str());
        CheckLogDetails("ambiguous", msg);
    }

    char pipe[64];
    snprintf(pipe, sizeof(pipe), "\\\\.\\pipe\\UE5DumpBfx_%lu", static_cast<unsigned long>(g_pid));
    {   blk("INJECT-MAPPED - CE maps the DLL: the plugin sees it in the module list and says where to connect");
        g_injectOutcome = InjectOutcome::MapTheDumper;
        menuClick();
        check("setup: the built dumper was mapped (DllMain not run)", g_mappedDumper != nullptr);
        const std::string msg = LastShown();
        check("MAPPED ⭐: reports the DLL injected", has(msg, "DLL injected"), msg.c_str());
        check("MAPPED: names this game's own pipe", has(msg, pipe), msg.c_str());
    }

    {   blk("ALREADY - a second click finds the dumper loaded and does not inject again");
        const size_t before = g_injects.size();
        g_injectOutcome = InjectOutcome::ReportFalse;
        menuClick();
        const std::string msg = LastShown();
        check("ALREADY ⭐: no second inject -- a later copy would take the game over from this one", g_injects.size() == before);
        check("ALREADY: says it is already loaded, as UE5Dumper.dll",
              has(msg, "already loaded in this process as 'UE5Dumper.dll'"), msg.c_str());
        // The module list's spelling of the path may differ in case from CMake's, so compare case-insensitively.
        const std::string pathLine = "Path: " + g_dumperPathA + "\n";
        const size_t at = msg.find("Path: ");
        check("ALREADY: gives the loaded file's path (an ASCII build path here)",
              at != std::string::npos && _stricmp(msg.substr(at, pathLine.size()).c_str(), pathLine.c_str()) == 0,
              msg.c_str());
        check("ALREADY: names this game's own pipe", has(msg, pipe), msg.c_str());
    }

    {   blk("UNLOADED - once the dumper is gone, a click injects again");
        if (g_mappedDumper) FreeLibrary(g_mappedDumper);
        g_mappedDumper = nullptr;
        const size_t before = g_injects.size();
        menuClick();
        check("UNLOADED control: an inject is asked for again", g_injects.size() == before + 1);
        check("UNLOADED control: ...and fails as the fake CE says", has(LastShown(), "Injection failed"), LastShown().c_str());
    }

    {   blk("IDENTITY - the proxy names are a pre-filter; the PE ProductName decides");
        for (const wchar_t* n : { L"version.dll", L"dinput8.dll", L"dxgi.dll", L"winmm.dll", L"DXGI.DLL" })
            check("IDENTITY: a proxy name we ship (any case) passes the pre-filter", IsProxyDllName(n));
        check("IDENTITY control: a name we do not ship does not", !IsProxyDllName(L"d3d11.dll"));
        check("IDENTITY ⭐: the built UE5Dumper.dll is ours by its ProductName", IsOurModule(g_dumperPath.c_str()));
        wchar_t sys[MAX_PATH] = {};
        GetSystemDirectoryW(sys, MAX_PATH);
        const std::wstring winVersion = std::wstring(sys) + L"\\version.dll";
        check("IDENTITY ⭐: Windows' own version.dll is not", !IsOurModule(winVersion.c_str()));
        check("IDENTITY control: a file that does not exist is not", !IsOurModule(L"C:\\no\\such\\UE5Dumper.dll"));
    }

    {   blk("DISABLE - the menu item is unregistered once");
        CEPlugin_DisablePlugin();
        check("DISABLE: unregistered once, with the plugin id and the item id CE gave",
              g_unregistered.size() == 1 && g_unregistered[0] == std::make_pair(3, 7));
        CEPlugin_DisablePlugin();
        check("DISABLE: a second disable unregisters nothing more", g_unregistered.size() == 1);
    }

    printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
    return g_fail == 0 ? 0 : 1;
}
