// ============================================================
// dllmain.cpp — DLL entry point
// ============================================================

#include <Windows.h>
#include "Logger.h"

// Global DLL module handle — used by CEPlugin.cpp to resolve the DLL's
// own file path when injecting into the game process.
HMODULE g_hDllModule = nullptr;

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID /*reserved*/) {
    switch (reason) {
    case DLL_PROCESS_ATTACH:
        g_hDllModule = hModule;
        DisableThreadLibraryCalls(hModule);
        Logger::Init();
        {
            // Log which process loaded this DLL — distinguishes CE plugin
            // host (ce.exe) from game process injection in the log file.
            char procName[MAX_PATH] = {};
            GetModuleFileNameA(nullptr, procName, MAX_PATH);
            LOG_INFO("UE5Dumper DLL loaded | process: %s [PID=%lu]",
                     procName, GetCurrentProcessId());
        }
        break;

    case DLL_PROCESS_DETACH:
        LOG_INFO("UE5Dumper DLL unloading (DLL_PROCESS_DETACH)");
        Logger::Shutdown();
        break;

    default:
        break;
    }
    return TRUE;
}
