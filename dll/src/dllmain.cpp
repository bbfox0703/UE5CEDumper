// ============================================================
// dllmain.cpp — DLL entry point
// ============================================================

#include <Windows.h>
#include <atomic>
#define LOG_CAT "INIT"
#include "Logger.h"
#include "BuildInfo.h"

// Global DLL module handle — used by CEPlugin.cpp to resolve the DLL's
// own file path when injecting into the game process.
HMODULE g_hDllModule = nullptr;

// Set to true by CEPlugin_InitializePlugin when this DLL is loaded as a
// CE plugin (in CE's own process). Auto-start thread checks this flag to
// decide whether to self-initialize.
std::atomic<bool> g_isCEPlugin{false};

// Forward declaration — defined in ExportAPI.cpp
extern "C" bool UE5_AutoStart();
extern "C" bool UE5_StartPipeServer();
extern "C" void UE5_Shutdown();

#ifdef UE5_PROXY_BUILD
// Cleanup for proxy DLL — defined in ProxyVersion.cpp
extern void ProxyVersion_Cleanup();
#endif

// Handle for the auto-start thread — stored so we can wait for it during DLL unload
static HANDLE g_hAutoStartThread = nullptr;

#ifdef UE5_PROXY_BUILD
// ── Proxy DLL auto-start ───────────────────────────────────────────────────
// In proxy mode, we start the pipe server immediately (no scan).
// The user triggers scanning later from the UI when the game has loaded.
// No CE delay needed — proxy DLL is always in the game process.
static DWORD WINAPI AutoStartThreadProc(LPVOID)
{
    // Guard: if this DLL was accidentally loaded by UE5DumpUI.exe (e.g. both
    // files in the same directory), skip all initialization to avoid the UI
    // connecting to its own in-process pipe server.
    {
        wchar_t procName[MAX_PATH] = {};
        GetModuleFileNameW(nullptr, procName, MAX_PATH);
        std::wstring path(procName);
        auto slash = path.find_last_of(L"\\/");
        std::wstring exe = (slash != std::wstring::npos) ? path.substr(slash + 1) : path;
        // Case-insensitive compare
        for (auto& c : exe) c = towlower(c);
        if (exe == L"ue5dumpui.exe") {
            LOG_WARN("DllMain ProxyStart: loaded by UE5DumpUI.exe — skipping proxy init");
            return 0;
        }
    }

    LOG_INFO("DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)");

    // Brief delay for game to finish early init (avoid pipe creation during process startup)
    Sleep(500);

    bool ok = UE5_StartPipeServer();
    LOG_INFO("DllMain ProxyStart: pipe server %s", ok ? "started" : "FAILED to start");
    return 0;
}
#else
// ── CE / Manual inject auto-start ──────────────────────────────────────────
// Spawned on DLL_PROCESS_ATTACH. Waits 1 second to allow CEPlugin_InitializePlugin
// to run (which sets g_isCEPlugin = true) if CE is loading this as a plugin.
// If g_isCEPlugin is still false after the delay, we're in a game process and
// call UE5_AutoStart() to scan for GObjects/GNames and start the pipe server.
//
// This eliminates the need for CE Lua to call UE5_Init via executeCodeEx,
// which was timing out due to the AOB scan taking longer than CE's remote-
// thread timeout.
static DWORD WINAPI AutoStartThreadProc(LPVOID)
{
    // Log immediately (before any sleep) to confirm thread is running.
    LOG_INFO("DllMain AutoStart: thread started (g_isCEPlugin=%d)", (int)g_isCEPlugin.load());

    // Give CE time to call CEPlugin_InitializePlugin (~200 ms typical).
    Sleep(1000);

    LOG_INFO("DllMain AutoStart: after 1s delay (g_isCEPlugin=%d)", (int)g_isCEPlugin.load());

    if (g_isCEPlugin.load()) {
        LOG_INFO("DllMain AutoStart: CE plugin host — skipping auto-start");
        return 0;
    }

    // Check if another UE5Dumper instance is already running (e.g., proxy DLL)
    // by trying to open the named pipe. If it exists, skip auto-start.
    HANDLE testPipe = CreateFileW(
        L"\\\\.\\pipe\\UE5DumpBfx",
        GENERIC_READ, 0, nullptr,
        OPEN_EXISTING, 0, nullptr);
    if (testPipe != INVALID_HANDLE_VALUE) {
        CloseHandle(testPipe);
        LOG_WARN("DllMain AutoStart: pipe already exists (another UE5Dumper instance running) — skipping auto-start");
        return 0;
    }

    LOG_INFO("DllMain AutoStart: game process — calling UE5_AutoStart");
    UE5_AutoStart();
    LOG_INFO("DllMain AutoStart: UE5_AutoStart returned");
    return 0;
}
#endif

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID /*reserved*/) {
    switch (reason) {
    case DLL_PROCESS_ATTACH: {
        g_hDllModule = hModule;
        DisableThreadLibraryCalls(hModule);
        Logger::Init();
        {
            // Log which process loaded this DLL — distinguishes CE plugin
            // host (ce.exe) from game process injection in the log file.
            wchar_t procPathW[MAX_PATH] = {};
            GetModuleFileNameW(nullptr, procPathW, MAX_PATH);

            // Extract filename from path for log + mirror
            std::wstring fullPath(procPathW);
            auto lastSlash = fullPath.find_last_of(L"\\/");
            std::wstring fileName = (lastSlash != std::wstring::npos)
                ? fullPath.substr(lastSlash + 1) : fullPath;

            // Narrow for log message
            char procNameA[MAX_PATH] = {};
            GetModuleFileNameA(nullptr, procNameA, MAX_PATH);
            LOG_INFO("UE5Dumper DLL loaded | build: %s | process: %s [PID=%lu]",
                     BUILD_VERSION_STRING, procNameA, GetCurrentProcessId());

            // Initialize per-process mirror log subfolder
            Logger::InitProcessMirror(fileName);
        }
        // Spawn auto-start thread. It will self-terminate if g_isCEPlugin
        // is set true by CEPlugin_InitializePlugin within 1 second.
        // Store the handle so DLL_PROCESS_DETACH can wait for it to finish.
        g_hAutoStartThread = CreateThread(nullptr, 0, AutoStartThreadProc, nullptr, 0, nullptr);
        if (g_hAutoStartThread) {
            LOG_INFO("DllMain: auto-start thread created OK");
        } else {
            LOG_ERROR("DllMain: CreateThread failed (error=%lu)", GetLastError());
        }
        break;
    }

    case DLL_PROCESS_DETACH:
        LOG_INFO("UE5Dumper DLL unloading (DLL_PROCESS_DETACH)");
        // Wait for auto-start thread to finish (max 5s) before tearing down
        if (g_hAutoStartThread) {
            WaitForSingleObject(g_hAutoStartThread, 5000);
            CloseHandle(g_hAutoStartThread);
            g_hAutoStartThread = nullptr;
        }
        // Stop pipe server and watch threads before logger shutdown
        UE5_Shutdown();
#ifdef UE5_PROXY_BUILD
        ProxyVersion_Cleanup();
#endif
        Logger::Shutdown();
        break;

    default:
        break;
    }
    return TRUE;
}
