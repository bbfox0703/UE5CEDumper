#pragma once

// ============================================================
// ExportAPI.h — C ABI export functions for CE Lua
// ============================================================

#include <cstdint>
#include <Windows.h>

extern "C" {

// === Initialization ===
__declspec(dllexport) bool     UE5_Init();
__declspec(dllexport) void     UE5_Shutdown();
__declspec(dllexport) uint32_t UE5_GetVersion();

// Combined init + pipe server start — called by CEPlugin's InjectDLL
// so that a single entry point activates everything in the game process.
__declspec(dllexport) bool     UE5_AutoStart();

// === Global Pointers ===
__declspec(dllexport) uintptr_t UE5_GetGObjectsAddr();
__declspec(dllexport) uintptr_t UE5_GetGNamesAddr();

// === Object Queries ===
__declspec(dllexport) int32_t   UE5_GetObjectCount();
__declspec(dllexport) uintptr_t UE5_GetObjectByIndex(int32_t index);
__declspec(dllexport) bool      UE5_GetObjectName(uintptr_t obj, char* buf, int32_t bufLen);
__declspec(dllexport) bool      UE5_GetObjectFullName(uintptr_t obj, char* buf, int32_t bufLen);
__declspec(dllexport) uintptr_t UE5_GetObjectClass(uintptr_t obj);
__declspec(dllexport) uintptr_t UE5_GetObjectOuter(uintptr_t obj);

// === Search ===
__declspec(dllexport) uintptr_t UE5_FindObject(const char* fullPath);
__declspec(dllexport) uintptr_t UE5_FindClass(const char* className);

// === WalkClass (batch mode) ===
__declspec(dllexport) int32_t   UE5_WalkClassBegin(uintptr_t uclassAddr);
__declspec(dllexport) bool      UE5_WalkClassGetField(int32_t index,
                                    uintptr_t* outAddr,
                                    char* nameOut, int32_t nameBufLen,
                                    char* typeOut, int32_t typeBufLen,
                                    int32_t* offsetOut,
                                    int32_t* sizeOut);
__declspec(dllexport) void      UE5_WalkClassEnd();

// === FName Resolution ===
__declspec(dllexport) bool      UE5_ResolveFName(uint64_t fname, char* buf, int32_t bufLen);

// === Pipe Server ===
__declspec(dllexport) bool      UE5_StartPipeServer();
__declspec(dllexport) void      UE5_StopPipeServer();
__declspec(dllexport) bool      UE5_IsPipeConnected();

} // extern "C"
