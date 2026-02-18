// ============================================================
// dllmain.cpp — DLL entry point
// ============================================================

#include <Windows.h>
#include "Logger.h"

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID /*reserved*/) {
    switch (reason) {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);
        Logger::Init();
        LOG_INFO("UE5Dumper DLL loaded (DLL_PROCESS_ATTACH)");
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
