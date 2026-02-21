// ============================================================
// dllmain.cpp — DLL entry point
// ============================================================

#include <Windows.h>
#define LOG_CAT "INIT"
#include "Logger.h"
#include "BuildInfo.h"

// Global DLL module handle — used by CEPlugin.cpp to resolve the DLL's
// own file path when injecting into the game process.
HMODULE g_hDllModule = nullptr;

// Set to true by CEPlugin_InitializePlugin when this DLL is loaded as a
// CE plugin (in CE's own process). Auto-start thread checks this flag to
// decide whether to self-initialize.
bool g_isCEPlugin = false;

// Forward declaration — defined in ExportAPI.cpp
extern "C" bool UE5_AutoStart();

// ── Auto-start thread ──────────────────────────────────────────────────────
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
    LOG_INFO("DllMain AutoStart: thread started (g_isCEPlugin=%d)", (int)g_isCEPlugin);

    // Give CE time to call CEPlugin_InitializePlugin (~200 ms typical).
    Sleep(1000);

    LOG_INFO("DllMain AutoStart: after 1s delay (g_isCEPlugin=%d)", (int)g_isCEPlugin);

    if (g_isCEPlugin) {
        LOG_INFO("DllMain AutoStart: CE plugin host — skipping auto-start");
        return 0;
    }

    LOG_INFO("DllMain AutoStart: game process — calling UE5_AutoStart");
    UE5_AutoStart();
    LOG_INFO("DllMain AutoStart: UE5_AutoStart returned");
    return 0;
}

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
        HANDLE hThread = CreateThread(nullptr, 0, AutoStartThreadProc, nullptr, 0, nullptr);
        if (hThread) {
            LOG_INFO("DllMain: auto-start thread created OK");
            CloseHandle(hThread);
        } else {
            LOG_ERROR("DllMain: CreateThread failed (error=%lu)", GetLastError());
        }
        break;
    }

    case DLL_PROCESS_DETACH:
        LOG_INFO("UE5Dumper DLL unloading (DLL_PROCESS_DETACH)");
        Logger::Shutdown();
        break;

    default:
        break;
    }
    return TRUE;
}
