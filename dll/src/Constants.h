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

// --- AOB Patterns (GObjects / FUObjectArray) ---
// V1: mov rax,[rip+X]; mov rcx,[rax+rcx*8]   (48 8B 05) — classic UE5.0–5.2
constexpr const char* AOB_GOBJECTS_V1 = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8";
// V2: mov r9,[rip+X]; mov [rip+Y],r9          (4C 8B 0D) — common UE5.3+
constexpr const char* AOB_GOBJECTS_V2 = "4C 8B 0D ?? ?? ?? ?? 4C 89 0D";
// V3: mov r8,[rip+X]; test r8,r8              (4C 8B 05)
constexpr const char* AOB_GOBJECTS_V3 = "4C 8B 05 ?? ?? ?? ?? 4D 85 C0";
// V4: mov rax,[rip+X]; mov rcx,[rax+rcx*8]; test rcx,rcx  (longer context)
constexpr const char* AOB_GOBJECTS_V4 = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 48 85 C9";
// V5: mov r10,[rip+X]; test r10,r10           (4C 8B 15)
constexpr const char* AOB_GOBJECTS_V5 = "4C 8B 15 ?? ?? ?? ?? 4D 85 D2";

// --- AOB Patterns (GNames / FNamePool) ---
// V1: lea rsi,[rip+X]; jmp
constexpr const char* AOB_GNAMES_V1 = "48 8D 35 ?? ?? ?? ?? EB";
// V2: lea rcx,[rip+X]; call; mov byte ptr
constexpr const char* AOB_GNAMES_V2 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05";
// V3: lea rax,[rip+X]; jmp
constexpr const char* AOB_GNAMES_V3 = "48 8D 05 ?? ?? ?? ?? EB";
// V4: lea r8,[rip+X]; jmp   (REX.R variant)
constexpr const char* AOB_GNAMES_V4 = "4C 8D 05 ?? ?? ?? ?? EB";

// --- AOB Patterns (GWorld) ---
// V1: mov rax,[rip+X]; cmp rcx,rax; cmovz rax,[rip+Y]
constexpr const char* AOB_GWORLD_V1 = "48 8B 05 ?? ?? ?? ?? 48 3B C8 48 0F 44 05";
// V2: mov [rip+X],rax; test rax,rax; jz
constexpr const char* AOB_GWORLD_V2 = "48 89 05 ?? ?? ?? ?? 48 85 C0 74";
// V3: mov rbx,[rip+X]; test rbx,rbx
constexpr const char* AOB_GWORLD_V3 = "48 8B 1D ?? ?? ?? ?? 48 85 DB";
// V4: mov rdi,[rip+X]; test rdi,rdi
constexpr const char* AOB_GWORLD_V4 = "48 8B 3D ?? ?? ?? ?? 48 85 FF";
// V5: cmp [rip+X],rax; je
constexpr const char* AOB_GWORLD_V5 = "48 39 05 ?? ?? ?? ?? 74";
// V6: mov [rip+X],rbx; call  (GWorld write after UWorld creation)
constexpr const char* AOB_GWORLD_V6 = "48 89 1D ?? ?? ?? ?? E8";

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
