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

// === Object Decryption (GAP #1) ===
// Set a custom decryption function for encrypted GObjects pointers.
// Pass NULL to clear (revert to identity/no decryption).
// Must be called BEFORE UE5_Init() — decryption is needed during scanning.
// UE5_AutoStart() does NOT support decryption (use manual Lua flow).
__declspec(dllexport) void      UE5_SetObjectDecryption(uintptr_t (*decryptFunc)(uintptr_t));

// === Property Detail Queries (for CE Lua dissect) ===
// Returns the FieldMask byte for a BoolProperty field (0 if not a bool).
// fieldAddr: FProperty* address from UE5_WalkClassGetField.
__declspec(dllexport) int32_t   UE5_GetFieldBoolMask(uintptr_t fieldAddr);

// Returns the UScriptStruct* for a StructProperty (0 if not a struct).
// fieldAddr: FProperty* address from UE5_WalkClassGetField.
__declspec(dllexport) uintptr_t UE5_GetFieldStructClass(uintptr_t fieldAddr);

// Returns the PropertyClass (UClass*) for an ObjectProperty (0 if not an object prop).
// fieldAddr: FProperty* address from UE5_WalkClassGetField.
// Same offset as StructProperty::Struct — separate export for semantic clarity.
__declspec(dllexport) uintptr_t UE5_GetFieldPropertyClass(uintptr_t fieldAddr);

// Returns the PropertiesSize of a UClass/UStruct (total struct byte size).
__declspec(dllexport) int32_t   UE5_GetClassPropsSize(uintptr_t classAddr);

// === UFunction Invocation ===
// Find first non-CDO instance of a class by name. Returns UObject* address or 0.
__declspec(dllexport) uintptr_t UE5_FindInstanceOfClass(const char* className);

// Find a UFunction by name on a UClass. Returns UFunction* address or 0.
__declspec(dllexport) uintptr_t UE5_FindFunctionByName(uintptr_t classAddr, const char* funcName);

// Call UObject::ProcessEvent(ufunc, params). Returns 0 on success, negative on error.
// params must point to a buffer of at least UFunction::ParmsSize bytes.
// Error codes: -1=bad args, -2=vtable read fail, -3=ProcessEvent not found, -4=exception.
__declspec(dllexport) int32_t   UE5_CallProcessEvent(uintptr_t instance, uintptr_t ufunc, uintptr_t params);

// === Pipe Server ===
__declspec(dllexport) bool      UE5_StartPipeServer();
__declspec(dllexport) void      UE5_StopPipeServer();
__declspec(dllexport) bool      UE5_IsPipeConnected();

} // extern "C"
