#pragma once

// ============================================================
// Constants.h — Centralized magic strings and numbers for DLL
// ============================================================

namespace Constants {

// --- Logging ---
constexpr const wchar_t* LOG_FOLDER_NAME  = L"UE5CEDumper";
constexpr const wchar_t* LOG_SUBFOLDER    = L"Logs";
constexpr const wchar_t* LOG_FILE_PREFIX  = L"UE5Dumper";
constexpr const wchar_t* LOG_SCAN_PREFIX  = L"UE5Dumper-scan";
constexpr const wchar_t* LOG_PIPE_PREFIX  = L"UE5Dumper-pipe";
constexpr int            LOG_MAX_FILES    = 5;
constexpr size_t         LOG_MAX_SIZE_MB  = 5;
constexpr size_t         LOG_MAX_SIZE     = LOG_MAX_SIZE_MB * 1024 * 1024;

// --- Named Pipe ---
constexpr const wchar_t* PIPE_NAME        = L"\\\\.\\pipe\\UE5DumpBfx";
constexpr const char*    PIPE_NAME_NARROW  = "\\\\.\\pipe\\UE5DumpBfx";
constexpr unsigned long  PIPE_BUF_SIZE    = 65536;

// --- MSVC Mangled Symbol Exports ---
// Many retail UE games (especially modular builds) export these symbols.
// GetProcAddress on the game module resolves them in O(1).
// Source: RE-UE4SS CustomGameConfigs (Satisfactory, Returnal, Split Fiction)
constexpr const char* EXPORT_GOBJECTARRAY     = "?GUObjectArray@@3VFUObjectArray@@A";
constexpr const char* EXPORT_FNAME_CTOR       = "??0FName@@QEAA@PEB_WW4EFindName@@@Z";
constexpr const char* EXPORT_FNAME_TOSTRING   = "?ToString@FName@@QEBAXAEAVFString@@@Z";
constexpr const char* EXPORT_FNAME_CTOR_CHAR  = "??0FName@@QEAA@PEBDW4EFindName@@@Z";

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
// V6: mov rcx,[rip+X]; mov [rdx],rax           (48 8B 0D) — alt mov rcx variant
constexpr const char* AOB_GOBJECTS_V6 = "48 8B 0D ?? ?? ?? ?? 48 89 02";
// V7: mov r9,[rip+X]; cdq; movzx edx,dx        (4C 8B 0D) — GSpots variant
constexpr const char* AOB_GOBJECTS_V7 = "4C 8B 0D ?? ?? ?? ?? 99 0F B7 D2";
// V8: mov r9,[rip+X]; mov edx,eax; shr edx,10h (4C 8B 0D) — bit shift variant
constexpr const char* AOB_GOBJECTS_V8 = "4C 8B 0D ?? ?? ?? ?? 8B D0 C1 EA 10";
// V9: mov r9,[rip+X]; cdqe; lea rcx,[rax+rax*2]; (4C 8B 0D) — extended index
constexpr const char* AOB_GOBJECTS_V9 = "4C 8B 0D ?? ?? ?? ?? 48 98 48 8D 0C 40 49";
// V10: lea rcx,[rip+X]; call; call; mov byte[],1 — Split Fiction (UE5.5+)
// OnMatchFound needs -0x10 adjustment (points into FUObjectArray, not base)
constexpr const char* AOB_GOBJECTS_V10 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05 ?? ?? ?? ?? 01";
// V11: lea reg,[rip+X]; mov r9,rcx; mov [rcx],rax; mov eax,-1 — Little Nightmares 3
constexpr const char* AOB_GOBJECTS_V11 = "48 8D ?? ?? ?? ?? ?? 4C 8B C9 48 89 01 B8 FF FF FF FF";
// V12: mov reg,[rip+X]; mov r8,[rax+rcx*8]; test r8,r8; jz — FF7 Remake
// OnMatchFound needs -0x10 adjustment
constexpr const char* AOB_GOBJECTS_V12 = "48 8B ?? ?? ?? ?? ?? 4C 8B 04 C8 4D 85 C0 74 07";
// V13: mov rax,[rip+X]; mov rcx,[rax+rcx*8]; lea rax,[rdx+rdx*2]; jmp+3 — Palworld
constexpr const char* AOB_GOBJECTS_V13 = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 4C 8D 04 D1 EB 03";

// --- AOB Patterns (GNames / FNamePool) ---
// V1: lea rsi,[rip+X]; jmp
constexpr const char* AOB_GNAMES_V1 = "48 8D 35 ?? ?? ?? ?? EB";
// V2: lea rcx,[rip+X]; call; mov byte ptr
constexpr const char* AOB_GNAMES_V2 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05";
// V3: lea rax,[rip+X]; jmp
constexpr const char* AOB_GNAMES_V3 = "48 8D 05 ?? ?? ?? ?? EB";
// V4: lea r8,[rip+X]; jmp   (REX.R variant)
constexpr const char* AOB_GNAMES_V4 = "4C 8D 05 ?? ?? ?? ?? EB";
// V5: lea rcx,[rip+X]; call; mov byte ptr[??],1  — extended context variant
constexpr const char* AOB_GNAMES_V5 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05 ?? ?? ?? ?? 01";
// V6: mov rax,[rip+X]; test rax,rax; jnz; mov ecx,0808h — GSpots UE5+ variant
constexpr const char* AOB_GNAMES_V6 = "48 8B 05 ?? ?? ?? ?? 48 85 C0 75 ?? B9 08 08 00";
// V7: FName ctor call-site — mov r8d,1; lea rcx,[rsp+?]; call; mov byte — FF7 Rebirth
// Resolves the CALL target (FName::FName), then scans inside for FNamePool refs
constexpr const char* AOB_GNAMES_V7_FNAME_CTOR = "41 B8 01 00 00 00 48 8D 4C 24 ?? E8 ?? ?? ?? ?? C6 44 24";
// V8: lea rax,[rip+X]; jmp 0x13; lea rcx,[rip+Y]; call; mov byte; movaps — Palworld
// First LEA resolves to FNamePool. Extended context to reduce false positives.
constexpr const char* AOB_GNAMES_V8 = "48 8D 05 ?? ?? ?? ?? EB 13 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05 ?? ?? ?? ?? ?? 0F 10";

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
// V7: mov rbx,[rip+X]; test rbx,rbx; jz 0x33; mov r8b,? — Palworld
constexpr const char* AOB_GWORLD_V7 = "48 8B 1D ?? ?? ?? ?? 48 85 DB 74 33 41 B0";

// --- UObject offsets (stable across all UE5 versions) ---
// UObjectBase
constexpr int OFF_UOBJECT_VTABLE       = 0x00;
constexpr int OFF_UOBJECT_FLAGS        = 0x08;
constexpr int OFF_UOBJECT_INDEX        = 0x0C;
constexpr int OFF_UOBJECT_CLASS        = 0x10;
constexpr int OFF_UOBJECT_NAME         = 0x18;
constexpr int OFF_UOBJECT_OUTER        = 0x20;

// --- UStruct / FField / FProperty offsets (runtime-detected) ---
// These defaults match standard UE5.0–5.4 layout.
// ValidateAndFixOffsets() will update them for UE5.5+ / CasePreservingName.
// Legacy constexpr aliases kept for reference only — all code MUST use DynOff::*.

} // namespace Constants

namespace DynOff {

// UStruct
inline int USTRUCT_SUPER      = 0x40;
inline int USTRUCT_CHILDREN   = 0x48;  // UField* chain (functions)
inline int USTRUCT_CHILDPROPS = 0x50;  // FField* chain (properties)
inline int USTRUCT_PROPSSIZE  = 0x58;

// FField
inline int FFIELD_CLASS       = 0x08;  // FFieldClass*
inline int FFIELD_NEXT        = 0x20;  // FField* next in chain
inline int FFIELD_NAME        = 0x28;  // FName

// FProperty (inherits from FField)
inline int FPROPERTY_ELEMSIZE = 0x38;
inline int FPROPERTY_FLAGS    = 0x40;  // uint64 PropertyFlags
inline int FPROPERTY_OFFSET   = 0x4C;  // int32 Offset_Internal

// FFieldClass
inline int FFIELDCLASS_NAME   = 0x00;  // FName at start of FFieldClass

// FStructProperty (subclass of FProperty)
// UScriptStruct* — first field after FProperty base layout.
// Default 0x78 for UE5.0-5.4; derived from FPROPERTY_OFFSET in ValidateAndFixOffsets.
inline int FSTRUCTPROP_STRUCT = 0x78;

// FBoolProperty layout (subclass of FProperty):
//   uint8 FieldSize, ByteOffset, ByteMask, FieldMask
// These 4 bytes are consecutive, located after the standard FProperty fields.
// Default 0x78 for UE5.0-5.4; adjusted based on detected offsets.
inline int FBOOLPROP_FIELDSIZE = 0x78;

// Detection state
inline bool bCasePreservingName  = false;
inline bool bOffsetsValidated    = false;

} // namespace DynOff

namespace Constants {

// --- Object Array ---
constexpr int OBJECTS_PER_CHUNK        = 64 * 1024;

// --- FNamePool ---
constexpr int FNAME_CHUNK_SIZE         = 0x20000;  // 128 KB per chunk
constexpr int FNAME_STRIDE             = 2;         // Alignment stride

} // namespace Constants
