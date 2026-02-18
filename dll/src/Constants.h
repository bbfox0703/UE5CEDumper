#pragma once

// ============================================================
// Constants.h — Centralized magic strings and numbers for DLL
// ============================================================

namespace Constants {

// --- Logging ---
constexpr const wchar_t* LOG_FOLDER_NAME  = L"UE5CEDumper";
constexpr const wchar_t* LOG_SUBFOLDER    = L"Logs";
constexpr const wchar_t* LOG_FILE_PREFIX  = L"UE5Dumper";
constexpr int            LOG_MAX_FILES    = 5;
constexpr size_t         LOG_MAX_SIZE_MB  = 5;
constexpr size_t         LOG_MAX_SIZE     = LOG_MAX_SIZE_MB * 1024 * 1024;

// --- Named Pipe ---
constexpr const wchar_t* PIPE_NAME        = L"\\\\.\\pipe\\UE5DumpBfx";
constexpr const char*    PIPE_NAME_NARROW  = "\\\\.\\pipe\\UE5DumpBfx";
constexpr unsigned long  PIPE_BUF_SIZE    = 65536;

// --- AOB Patterns (GObjects) ---
// Primary: mov rax, [rip+rel32]; mov rcx, [rax+rcx*8]
constexpr const char* AOB_GOBJECTS_PRIMARY   = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8";
// Fallback: mov r9, [rip+rel32]
constexpr const char* AOB_GOBJECTS_FALLBACK1 = "4C 8B 0D ?? ?? ?? ?? 4C 89 0D";
// Fallback 2
constexpr const char* AOB_GOBJECTS_FALLBACK2 = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 48 85 C9";

// --- AOB Patterns (GNames / FNamePool) ---
// Primary: lea rsi, [rip+rel32]; jmp
constexpr const char* AOB_GNAMES_PRIMARY   = "48 8D 35 ?? ?? ?? ?? EB";
// Fallback: lea rcx, [rip+rel32]; call
constexpr const char* AOB_GNAMES_FALLBACK1 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05";
// Fallback 2
constexpr const char* AOB_GNAMES_FALLBACK2 = "48 8D 05 ?? ?? ?? ?? EB";

// --- AOB Patterns (GWorld) ---
constexpr const char* AOB_GWORLD_PRIMARY   = "48 8B 05 ?? ?? ?? ?? 48 3B C8 48 0F 44 05";
constexpr const char* AOB_GWORLD_FALLBACK1 = "48 89 05 ?? ?? ?? ?? 48 85 C0 74";

// --- UObject / UStruct typical offsets (dynamically verified) ---
// UObjectBase
constexpr int OFF_UOBJECT_VTABLE       = 0x00;
constexpr int OFF_UOBJECT_FLAGS        = 0x08;
constexpr int OFF_UOBJECT_INDEX        = 0x0C;
constexpr int OFF_UOBJECT_CLASS        = 0x10;
constexpr int OFF_UOBJECT_NAME         = 0x18;
constexpr int OFF_UOBJECT_OUTER        = 0x20;

// UStruct
constexpr int OFF_USTRUCT_SUPER        = 0x40;
constexpr int OFF_USTRUCT_CHILDREN     = 0x48;  // UField* chain (functions)
constexpr int OFF_USTRUCT_CHILDPROPS   = 0x50;  // FField* chain (properties)
constexpr int OFF_USTRUCT_PROPSSIZE    = 0x58;

// FField
constexpr int OFF_FFIELD_CLASS         = 0x08;
constexpr int OFF_FFIELD_NEXT          = 0x20;
constexpr int OFF_FFIELD_NAME          = 0x28;

// FProperty
constexpr int OFF_FPROPERTY_ELEMSIZE   = 0x38;
constexpr int OFF_FPROPERTY_FLAGS      = 0x40;
constexpr int OFF_FPROPERTY_OFFSET     = 0x4C;

// FFieldClass
constexpr int OFF_FFIELDCLASS_NAME     = 0x00;

// --- Object Array ---
constexpr int OBJECTS_PER_CHUNK        = 64 * 1024;

// --- FNamePool ---
constexpr int FNAME_CHUNK_SIZE         = 0x20000;  // 128 KB per chunk
constexpr int FNAME_STRIDE             = 2;         // Alignment stride

} // namespace Constants
