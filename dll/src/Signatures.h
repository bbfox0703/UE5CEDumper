#pragma once

#include <cstdint>

// ============================================================
// Signatures.h — Centralized AOB (Array of Bytes) patterns
//
// All byte-pattern signatures for GObjects, GNames, GWorld,
// and related UE global pointer scanning live in this file.
//
// HOW TO ADD NEW PATTERNS:
//   1. Add a constexpr const char* in the appropriate section
//   2. Name it AOB_{TARGET}_{SOURCE}{N} (e.g., AOB_GOBJECTS_RE3)
//   3. Add a comment with: opcode meaning, UE version, game
//   4. Add an AobSignature entry to the corresponding PATTERNS[] array
//
// Sources:
//   V1-V13  : Original UE5CEDumper patterns
//   PS1-PS7 : patternsleuth (github.com/trumank/patternsleuth)
//   RE1-RE5 : RE-UE4SS CustomGameConfigs (github.com/UE4SS-RE/RE-UE4SS)
//   D7_1    : Dumper-7 (github.com/Encryqed/Dumper-7)
//   CT1-CT5 : UE4 Dumper.CT (vendor/UE4 Dumper.CT)
//   UD1-UD3 : UEDumper (github.com/Spuckwaffel/UEDumper)
//   ES2     : Everspace 2 (UE 5.5)
//   SF      : SatisfFactory (UE 5.3, modular build — patterns in DLLs)
//   TQ      : TQ2 (UE 5.x)
// ============================================================

// ============================================================
// AOB Pattern Metadata Types
// ============================================================

enum class AobTarget : uint8_t {
    GObjects = 0,
    GNames   = 1,
    GWorld   = 2,
};

// How to resolve the AOB match address into a final pointer
enum class AobResolve : uint8_t {
    RipDirect        = 0,  // RIP-relative -> address is direct target
    RipDeref         = 1,  // RIP-relative -> deref once (pointer-to-pointer)
    RipBoth          = 2,  // Try direct first, if validation fails try deref
    SymbolExport     = 3,  // MSVC mangled symbol → address IS the variable
    CallFollow       = 4,  // Follow CALL in AOB match, scan function body for RIP refs
    SymbolCallFollow = 5,  // MSVC mangled symbol → address IS a function → scan body for RIP refs
};

// Unified AOB signature descriptor.
// All fields are POD — constexpr-constructible, stored in .rdata.
struct AobSignature {
    const char* id;           // Unique identifier, e.g. "GOBJ_V1", "GWORLD_ES2_1"
    const char* pattern;      // AOB pattern string ("48 8B 05 ?? ?? ?? ??") or mangled symbol name
    AobTarget   target;       // What global pointer this pattern finds
    AobResolve  resolve;      // How to resolve the match address
    int  instrOffset;         // Byte offset from match start to the RIP instruction (0 = at match start)
    int  opcodeLen;           // Opcode bytes before the 4-byte displacement (typically 3)
    int  totalLen;            // Total instruction length (typically 7 for REX+opcode+modrm+disp32)
    int  adjustment;          // Post-resolution offset adjustment (e.g. -0x10 for struct base)
    int  priority;            // Lower = tried first. 0=symbol exports, 10-20=long, 50=standard, 80=legacy
    int  callOffset;          // For CallFollow: byte offset of E8 opcode within the pattern
    bool gworldAllowNull;     // For GWorld: accept null dereference (write-patterns at startup)
    const char* source;       // Attribution: "V", "PS", "RE", "ES2", "SF", "TQ", etc.
    const char* notes;        // Human-readable: game name, UE version
};

namespace Sig {

// ============================================================
// GObjects / FUObjectArray
// ============================================================

// --- Original patterns (V-series) ---

// V1: mov rax,[rip+X]; mov rcx,[rax+rcx*8]  — classic UE5.0-5.2
constexpr const char* AOB_GOBJECTS_V1 = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8";
// V2: mov r9,[rip+X]; mov [rip+Y],r9  — common UE5.3+
constexpr const char* AOB_GOBJECTS_V2 = "4C 8B 0D ?? ?? ?? ?? 4C 89 0D";
// V3: mov r8,[rip+X]; test r8,r8
constexpr const char* AOB_GOBJECTS_V3 = "4C 8B 05 ?? ?? ?? ?? 4D 85 C0";
// V4: mov rax,[rip+X]; mov rcx,[rax+rcx*8]; test rcx,rcx  (longer context)
constexpr const char* AOB_GOBJECTS_V4 = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 48 85 C9";
// V5: mov r10,[rip+X]; test r10,r10
constexpr const char* AOB_GOBJECTS_V5 = "4C 8B 15 ?? ?? ?? ?? 4D 85 D2";
// V6: mov rcx,[rip+X]; mov [rdx],rax  — alt mov rcx variant
constexpr const char* AOB_GOBJECTS_V6 = "48 8B 0D ?? ?? ?? ?? 48 89 02";
// V7: mov r9,[rip+X]; cdq; movzx edx,dx  — GSpots variant
constexpr const char* AOB_GOBJECTS_V7 = "4C 8B 0D ?? ?? ?? ?? 99 0F B7 D2";
// V8: mov r9,[rip+X]; mov edx,eax; shr edx,10h  — bit shift variant
constexpr const char* AOB_GOBJECTS_V8 = "4C 8B 0D ?? ?? ?? ?? 8B D0 C1 EA 10";
// V9: mov r9,[rip+X]; cdqe; lea rcx,[rax+rax*2]  — extended index
constexpr const char* AOB_GOBJECTS_V9 = "4C 8B 0D ?? ?? ?? ?? 48 98 48 8D 0C 40 49";
// V10: lea rcx,[rip+X]; call; call; mov byte[],1  — Split Fiction (UE5.5+)
//   Needs -0x10 adjustment (points into struct, not base)
constexpr const char* AOB_GOBJECTS_V10 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05 ?? ?? ?? ?? 01";
// V11: lea reg,[rip+X]; mov r9,rcx; mov [rcx],rax; mov eax,-1  — Little Nightmares 3
constexpr const char* AOB_GOBJECTS_V11 = "48 8D ?? ?? ?? ?? ?? 4C 8B C9 48 89 01 B8 FF FF FF FF";
// V12: mov reg,[rip+X]; mov r8,[rax+rcx*8]; test r8,r8; jz  — FF7 Remake
//   Needs -0x10 adjustment
constexpr const char* AOB_GOBJECTS_V12 = "48 8B ?? ?? ?? ?? ?? 4C 8B 04 C8 4D 85 C0 74 07";
// V13: mov rax,[rip+X]; mov rcx,[rax+rcx*8]; lea rax,[rdx+rdx*2]; jmp+3  — Palworld
constexpr const char* AOB_GOBJECTS_V13 = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 4C 8D 04 D1 EB 03";

// --- patternsleuth patterns (instrOffset != 0, use TryPatternRIPOffset) ---

// PS1: cmp/cmp/jne; lea rdx; lea rcx,[rip+X]  — instrOffset=23, opcodeLen=3, totalLen=7
constexpr const char* AOB_GOBJECTS_PS1 = "8B 05 ?? ?? ?? ?? 3B 05 ?? ?? ?? ?? 75 ?? 48 8D 15 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ??";
// PS2: jz; lea rcx,[rip+X]; mov byte; call  — instrOffset=2, opcodeLen=3, totalLen=7
constexpr const char* AOB_GOBJECTS_PS2 = "74 ?? 48 8D 0D ?? ?? ?? ?? C6 05 ?? ?? ?? ?? 01 E8";
// PS3: jne; mov; lea rcx,[rip+X]; call; xor r9d  — instrOffset=5, opcodeLen=3, totalLen=7
constexpr const char* AOB_GOBJECTS_PS3 = "75 ?? 48 ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 45 33 C9 4C 89 74 24";
// PS4: test; mov qword; mov eax,-1; lea r11,[rip+X]  — instrOffset=16, opcodeLen=3, totalLen=7
constexpr const char* AOB_GOBJECTS_PS4 = "45 84 C0 48 C7 41 10 00 00 00 00 B8 FF FF FF FF 4C 8D 1D ?? ?? ?? ??";
// PS5: or esi; and eax; mov [rdi+8]; lea rcx,[rip+X]  — instrOffset=12, opcodeLen=3, totalLen=7
constexpr const char* AOB_GOBJECTS_PS5 = "81 CE 00 00 00 02 83 E0 FB 89 47 08 48 8D 0D ?? ?? ?? ??";
// PS6: mov eax,[rip]; sub eax,[rip]; sub eax,[rip+X]  — arithmetic, instrOffset=14, opcodeLen=2, totalLen=6
constexpr const char* AOB_GOBJECTS_PS6 = "8B 05 ?? ?? ?? ?? 2B 05 ?? ?? ?? ?? 2B 05 ?? ?? ?? ??";
// PS7: call; mov eax,[rip]; mov ecx,[rip]; add ecx,[rip+X]  — arithmetic, instrOffset=17, opcodeLen=2, totalLen=6
constexpr const char* AOB_GOBJECTS_PS7 = "E8 ?? ?? ?? ?? 8B 05 ?? ?? ?? ?? 8B 0D ?? ?? ?? ?? 03 0D ?? ?? ?? ??";

// --- RE-UE4SS CustomGameConfigs ---

// RE1: FF7 Rebirth — special: add [rip+X],ecx; dec eax; cmp edx,eax; jge
//   instrOffset=2, resolution: nextInstr(+6) + DerefToInt32(matchAddr+2)
constexpr const char* AOB_GOBJECTS_RE1 = "03 ?? ?? ?? ?? ?? FF C8 3B D0 0F 8D ?? ?? ?? ?? 44 8B";
// RE2: FF7 Remake — mov reg,[rip+X]; mov r8,[rax+rcx*8]; test r8; jz; ?; ?; ?; setz
//   instrOffset=3, needs -0x10 adjustment (same as V12 but slightly different context)
constexpr const char* AOB_GOBJECTS_RE2 = "48 8B ?? ?? ?? ?? ?? 4C 8B 04 C8 4D 85 C0 74 07 ?? ?? ?? 0F 94";
// RE3: Little Nightmares 3 Demo — lea; mov r9,rcx; mov; mov eax,-1; mov [rcx+8]; cmovne; inc; mov; cmp
//   (extended context variant of V11)
constexpr const char* AOB_GOBJECTS_RE3 = "48 8D ?? ?? ?? ?? ?? 4C 8B C9 48 89 01 B8 FF FF FF FF 89 41 08 0F 45 ?? ?? ?? ?? ?? FF C0 89 41 08 3B";

// --- UE4 Dumper.CT patterns (x64) ---

// CT1: mov r8; lea rax; mov [rsi+10h]; mov qword — UE4 Dumper.CT v5+
//   44 8B * * * 48 8D 05 * * * * * * * * * 48 89 71 10
constexpr const char* AOB_GOBJECTS_CT1 = "44 8B ?? ?? ?? 48 8D 05 ?? ?? ?? ?? ?? ?? ?? ?? ?? 48 89 71 10";
// CT2: push rbx; sub rsp,20h; mov rbx,rcx; test rdx; jz; mov
//   40 53 48 83 EC 20 48 8B D9 48 85 D2 74 * 8B — function prologue
constexpr const char* AOB_GOBJECTS_CT2 = "40 53 48 83 EC 20 48 8B D9 48 85 D2 74 ?? 8B";
// CT3: mov r8,[rip+X]; cmp [r8+?]  — 4C 8B 05 * * * * 45 3B 88
constexpr const char* AOB_GOBJECTS_CT3 = "4C 8B 05 ?? ?? ?? ?? 45 3B 88";

// --- UEDumper patterns ---

// UD1: mov rax,[rip+X]; mov rcx,[rax+rcx*8]; lea rax,[rcx+rdx*8]; test rax,rax
constexpr const char* AOB_GOBJECTS_UD1 = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 48 8D 04 D1 48 85 C0";


// ============================================================
// GNames / FNamePool
// ============================================================

// --- Original patterns (V-series) ---

// V1: lea rsi,[rip+X]; jmp
constexpr const char* AOB_GNAMES_V1 = "48 8D 35 ?? ?? ?? ?? EB";
// V2: lea rcx,[rip+X]; call; mov byte ptr
constexpr const char* AOB_GNAMES_V2 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05";
// V3: lea rax,[rip+X]; jmp
constexpr const char* AOB_GNAMES_V3 = "48 8D 05 ?? ?? ?? ?? EB";
// V4: lea r8,[rip+X]; jmp   (REX.R variant)
constexpr const char* AOB_GNAMES_V4 = "4C 8D 05 ?? ?? ?? ?? EB";
// V5: lea rcx,[rip+X]; call; mov byte ptr[??],1  — extended context
constexpr const char* AOB_GNAMES_V5 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05 ?? ?? ?? ?? 01";
// V6: mov rax,[rip+X]; test rax,rax; jnz; mov ecx,0808h  — GSpots UE5+
constexpr const char* AOB_GNAMES_V6 = "48 8B 05 ?? ?? ?? ?? 48 85 C0 75 ?? B9 08 08 00";
// V7: FName ctor call-site — mov r8d,1; lea rcx; call; mov byte — FF7 Rebirth
//   Resolves CALL target, then scans inside for FNamePool refs
constexpr const char* AOB_GNAMES_V7_FNAME_CTOR = "41 B8 01 00 00 00 48 8D 4C 24 ?? E8 ?? ?? ?? ?? C6 44 24";
// V8: lea rax,[rip+X]; jmp 0x13; lea rcx,[rip+Y]; call; mov byte; movaps  — Palworld
//   First LEA resolves to FNamePool.
constexpr const char* AOB_GNAMES_V8 = "48 8D 05 ?? ?? ?? ?? EB 13 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05 ?? ?? ?? ?? ?? 0F 10";

// --- patternsleuth patterns ---

// PS1: jz+9; lea r8,[rip+X]; jmp; lea rcx; call  — instrOffset=2, opcodeLen=3, totalLen=7
constexpr const char* AOB_GNAMES_PS1 = "74 09 4C 8D 05 ?? ?? ?? ?? EB ?? 48 8D 0D ?? ?? ?? ?? E8";
// PS2: sub rsp,0x20; shr edx,3; lea rbp,[rip+X]  — instrOffset=7, opcodeLen=3, totalLen=7
constexpr const char* AOB_GNAMES_PS2 = "48 83 EC 20 C1 EA 03 48 8D 2D ?? ?? ?? ??";

// --- Dumper-7 pattern ---

// D7_1: lea rcx,[rip+X]; call  — FNamePool ctor singleton (basic form)
//   Dumper-7 iterates all occurrences, verifies called function
//   has InitializeSRWLock + "ByteProperty" reference.
//   For us: same as V2 but shorter context; already covered by V2/V5.
constexpr const char* AOB_GNAMES_D7_1 = "48 8D 0D ?? ?? ?? ?? E8";

// --- UE4 Dumper.CT patterns ---

// CT1: lea rax,[rip+X]; jmp 0x16; lea rcx,[rip+Y]; call  — UE4 Dumper.CT v6+ (UE4.23+)
//   Same as V8 variant but with jmp 0x16 instead of 0x13
constexpr const char* AOB_GNAMES_CT1 = "4C 8D 05 ?? ?? ?? ?? EB 16 48 8D 0D ?? ?? ?? ?? E8";
// CT2: lea rcx,[rip+X]; call; mov r8,rax; mov byte — (UE4 Dumper.CT UE4.23+ main pattern)
constexpr const char* AOB_GNAMES_CT2 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 4C 8B C0 C6";
// CT3: sub rsp,28h; mov rax,[rip+X]; test rax; jnz; mov ecx,0x0808; mov rbx,[rsp+20h]; call
//   — pre-FNamePool (UE4 <4.23), deref pointer
constexpr const char* AOB_GNAMES_CT3 = "48 83 EC 28 48 8B 05 ?? ?? ?? ?? 48 85 C0 75 ?? B9 ?? ?? 00 00 48 89 5C 24 20 E8";
// CT4: ret; ? DB; mov [rip+X],rbx; ?; ?; mov rbx,[rsp+20h]
//   — pre-FNamePool write pattern, instrOffset=5
constexpr const char* AOB_GNAMES_CT4 = "C3 ?? DB 48 89 1D ?? ?? ?? ?? ?? ?? 48 8B 5C 24 20";

// --- UEDumper example patterns ---

// UD1: call; cmp [rbp-18h],0; lea r8,[rip]; lea rdx,[rip]  — FNameToString call-site
constexpr const char* AOB_GNAMES_UD1 = "E8 ?? ?? ?? ?? 83 7D E8 00 4C 8D 05 ?? ?? ?? ?? 48 8D 15 ?? ?? ?? ??";
// UD2: lea rcx,[rip+X]; call; mov r8,rax; mov byte (same as CT2)
constexpr const char* AOB_GNAMES_UD2 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 4C 8B C0 C6 05";


// ============================================================
// GWorld
// ============================================================

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
// V7: mov rbx,[rip+X]; test rbx,rbx; jz 0x33; mov r8b  — Palworld
constexpr const char* AOB_GWORLD_V7 = "48 8B 1D ?? ?? ?? ?? 48 85 DB 74 33 41 B0";


// ============================================================
// MSVC Mangled Symbol Exports
// ============================================================
// Many retail UE games (especially modular builds) export these symbols.
// GetProcAddress resolves them in O(1) before any AOB scan.
// Source: RE-UE4SS (Satisfactory, Returnal use these exclusively)

constexpr const char* EXPORT_GOBJECTARRAY     = "?GUObjectArray@@3VFUObjectArray@@A";
constexpr const char* EXPORT_FNAME_CTOR       = "??0FName@@QEAA@PEB_WW4EFindName@@@Z";
constexpr const char* EXPORT_FNAME_TOSTRING   = "?ToString@FName@@QEBAXAEAVFString@@@Z";
constexpr const char* EXPORT_FNAME_CTOR_CHAR  = "??0FName@@QEAA@PEBDW4EFindName@@@Z";
constexpr const char* EXPORT_GWORLD           = "?GWorld@@3VUWorldProxy@@A";


// ============================================================
// New patterns: Everspace 2 (UE 5.5)
// ============================================================

// --- GWorld (ES2) ---
// ES2_1: mov rax,[GWorld]; lea rdx,[rbp+1F8]; mov rcx,[rax+18]; mov [rbp+48],rcx; lea rcx,[rbp+48]; call
constexpr const char* AOB_GWORLD_ES2_1 = "48 8B 05 ?? ?? ?? ?? 48 8D 95 F8 01 00 00 48 8B 48 18 48 89 4D 48 48 8D 4D 48 E8";
// ES2_2: cmovz r13,[GWorld]; mov r10,[rax+358]; mov rax,[rsi]; mov [rbp-50],rax; mov rax,[rsi+8]
//   CMOVZ: opcodeLen=4 (4C 0F 44 2D), totalLen=8
constexpr const char* AOB_GWORLD_ES2_2 = "4C 0F 44 2D ?? ?? ?? ?? 4C 8B 90 58 03 00 00 48 8B 06 48 89 45 B0 48 8B 46 08";
// ES2_3: mov rax,[GWorld]; mov r8,rbx; mov rcx,[r8]; cmp [rcx+2C0],rax; jne
constexpr const char* AOB_GWORLD_ES2_3 = "48 8B 05 ?? ?? ?? ?? 4C 8B C3 49 8B 08 48 39 81 C0 02 00 00 0F 85 ?? ?? ?? ??";
// ES2_4: cmp [GWorld],rbx; jnz+8; and qword [GWorld],0; mov rcx,[rbx+440]; test rcx,rcx
constexpr const char* AOB_GWORLD_ES2_4 = "48 39 1D ?? ?? ?? ?? 75 08 48 83 25 ?? ?? ?? ?? 00 48 8B 8B 40 04 00 00 48 85 C9";
// ES2_5: mov rdx,[GWorld]; lea rcx,[rsi+28]; mov r9,rax; call r12; add rdi,10; sub r14,1
constexpr const char* AOB_GWORLD_ES2_5 = "48 8B 15 ?? ?? ?? ?? 48 8D 4E 28 4C 8B C8 41 FF D4 48 83 C7 10 49 83 EE 01";
// ES2_6: mov rdx,[GWorld]; lea rcx,[rdi+28]; cmovne r8,[rsp+20]; mov r9,rax; call rbx; mov rcx,[rsp+20]
constexpr const char* AOB_GWORLD_ES2_6 = "48 8B 15 ?? ?? ?? ?? 48 8D 4F 28 4C 0F 45 44 24 20 4C 8B C8 FF D3 48 8B 4C 24";

// --- GNames (ES2) ---
// ES2_1: lea rdx,[NamePoolData]; mov ecx,ebx; movzx eax,bx; mov [rsp+3C],eax; shr ecx,10; mov [rsp+38],ecx; mov rax,[rsp+38]
constexpr const char* AOB_GNAMES_ES2_1 = "48 8D 15 ?? ?? ?? ?? 8B CB 0F B7 C3 89 44 24 3C C1 E9 10 89 4C 24 ?? 48 8B";

// --- GObjects (ES2) ---
// ES2_1: lea rcx,[GUObjectArray]; mov esi,r9d; mov ebp,r8d; mov r15,rdx; call [rip+X]
constexpr const char* AOB_GOBJECTS_ES2_1 = "48 8D 0D ?? ?? ?? ?? 41 8B F1 41 8B E8 4C 8B FA FF 15";


// ============================================================
// New patterns: SatisfFactory (UE 5.3, modular build — in DLLs)
// ============================================================

// --- GWorld (SF, in Game-Engine-Win64-Shipping.DLL) ---
// SF_1: mov rax,[GWorld]; cmp [rcx+2C0],rax  — UGameEngine::Tick
constexpr const char* AOB_GWORLD_SF_1 = "48 8B 05 ?? ?? ?? ?? 48 39 81 C0 02 00 00";
// SF_2: mov rax,[GWorld]; lea r8,[rsp+38]; lea rdx,[rsp+20]; mov [rsp+38],rax  — FAudioDeviceManager::CreateMainAudioDevice
constexpr const char* AOB_GWORLD_SF_2 = "48 8B 05 ?? ?? ?? ?? 4C 8D 44 24 ?? 48 8D 54 24 ?? 48 89 44";
// SF_3: cmp [GWorld],rdi; jne; mov [GWorld],rbx; call  — UWorld::FinishDestroy
constexpr const char* AOB_GWORLD_SF_3 = "48 39 3D ?? ?? ?? ?? 75 ?? 48 89 1D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48";
// SF_4: mov rdi,[GWorld]; mov rbx,[rsp+70]; mov rax,rdi  — UEngine::GetWorldFromContextObject
constexpr const char* AOB_GWORLD_SF_4 = "48 8B 3D ?? ?? ?? ?? 48 8B 5C 24 70 48 8B";
// SF_5: mov rax,[GWorld]; mov ebx,edx; mov rdi,rcx; lea rdx,[r11-38]  — FMallocLeakReporter::WriteReports
constexpr const char* AOB_GWORLD_SF_5 = "48 8B 05 ?? ?? ?? ?? 8B DA 48 8B F9 49 8D";

// --- GNames (SF, in GameSteam-Core-Win64-Shipping.DLL) ---
// SF_1: lea r8,[NamePoolData]; jmp; lea rcx,[NamePoolData]; call FNamePool::FNamePool; mov r8,rax
constexpr const char* AOB_GNAMES_SF_1 = "4C 8D 05 ?? ?? ?? ?? EB ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 4C 8B C0";
// SF_2: lea rax,[NamePoolData]; movups [rsp+38],xmm0; shl rdi,6; add rdi,rax
constexpr const char* AOB_GNAMES_SF_2 = "48 8D 05 ?? ?? ?? ?? 0F 11 44 24 38 48 C1";
// SF_3: lea rcx,[NamePoolData]; mov edi,edx; jne; call FNamePool::FNamePool
constexpr const char* AOB_GNAMES_SF_3 = "48 8D 0D ?? ?? ?? ?? 8B FA 75 ?? E8 ?? ?? ?? ?? 48";

// --- GObjects (SF, via _imp_ import table in EXE) ---
// SF_1: mov rax,[_imp_GUObjectArray]; cmp [rax+0C],sil; je; lea rdx
constexpr const char* AOB_GOBJECTS_SF_1 = "48 8B 05 ?? ?? ?? ?? 40 38 70 0C 74 2E 48 8D 15";


// ============================================================
// New patterns: TQ2
// ============================================================

// --- GWorld (TQ2) ---
// TQ_1: mov rbx,[GWorld]; test rbx,rbx; jz; mov r8b,1; xor edx,edx; mov rcx,rbx; call  — extended V3
constexpr const char* AOB_GWORLD_TQ_1 = "48 8B 1D ?? ?? ?? ?? 48 85 ?? 74 ?? 41 B0 01 33 ?? ?? 8B ?? E8";
// TQ_2: mov rdx,[GWorld]; mov rcx,[GWorld_related]; call; jmp; mov rax,r15; cmp byte [rsi],1
constexpr const char* AOB_GWORLD_TQ_2 = "48 8B 15 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? EB 03 ?? 8B ?? 80 ?? 01";
// TQ_3: ?? prefix; mov rax,[GWorld]; mov rsi,rcx; movaps [r11-38],xmm8; movaps xmm8,xmm1; test rax,rax; je
//   Wildcard-prefixed, RIP at offset 3
constexpr const char* AOB_GWORLD_TQ_3 = "?? 8B 05 ?? ?? ?? ?? ?? 8B ?? ?? 0F 29 43 ?? 44 0F 28 C1 ?? 85 ?? 0F";
// TQ_4: ?? prefix; mov [GWorld],rcx; test rsi,rsi; jz; mov rax,[rsi]; mov rcx,rsi; call [rax+E0]
//   Wildcard-prefixed write pattern, RIP at offset 3
constexpr const char* AOB_GWORLD_TQ_4 = "?? 89 0D ?? ?? ?? ?? ?? 85 ?? 74 ?? 48 8B 06 ?? 8B ?? FF 90 ?? 00 00";


// ============================================================
// Unified Pattern Arrays (sorted by priority)
// ============================================================
// Priority scheme:
//   0     : Symbol exports (O(1) lookup)
//   5     : Export function body scan
//  10-20  : Long unique patterns (20+ fixed bytes)
//  30-40  : Medium patterns (15-20 bytes)
//  50-60  : Short generic patterns, patternsleuth
//  80-100 : UE4/legacy patterns

// Helper macro to reduce boilerplate for common RipBoth patterns
#define SIG_RIP(id, pat, tgt, ioff, opc, tot, adj, pri, src, note) \
    { id, pat, tgt, AobResolve::RipBoth, ioff, opc, tot, adj, pri, 0, false, src, note }
#define SIG_RIP_DIRECT(id, pat, tgt, ioff, opc, tot, adj, pri, src, note) \
    { id, pat, tgt, AobResolve::RipDirect, ioff, opc, tot, adj, pri, 0, false, src, note }
#define SIG_EXPORT(id, sym, tgt, pri, note) \
    { id, sym, tgt, AobResolve::SymbolExport, 0, 0, 0, 0, pri, 0, false, "EXP", note }
#define SIG_SYM_CALL(id, sym, tgt, pri, note) \
    { id, sym, tgt, AobResolve::SymbolCallFollow, 0, 0, 0, 0, pri, 0, false, "EXP", note }
#define SIG_GWORLD_RIP(id, pat, ioff, opc, tot, adj, pri, allowNull, src, note) \
    { id, pat, AobTarget::GWorld, AobResolve::RipBoth, ioff, opc, tot, adj, pri, 0, allowNull, src, note }

// ── GObjects ─────────────────────────────────────────────────────────────
constexpr AobSignature GOBJECTS_PATTERNS[] = {
    // Priority 0: Symbol exports
    SIG_EXPORT("GOBJ_EXP", EXPORT_GOBJECTARRAY, AobTarget::GObjects, 0, "MSVC mangled symbol"),

    // Priority 10-20: Long specific patterns
    { "GOBJ_V10", AOB_GOBJECTS_V10, AobTarget::GObjects, AobResolve::RipBoth,
      0, 3, 7, -0x10, 10, 0, false, "V", "Split Fiction UE5.5+ lea+call+call" },
    { "GOBJ_RE3", AOB_GOBJECTS_RE3, AobTarget::GObjects, AobResolve::RipBoth,
      0, 3, 7, 0, 12, 0, false, "RE", "Little Nightmares 3 Demo extended" },
    { "GOBJ_V11", AOB_GOBJECTS_V11, AobTarget::GObjects, AobResolve::RipBoth,
      0, 3, 7, 0, 14, 0, false, "V", "Little Nightmares 3" },
    SIG_RIP("GOBJ_RE2", AOB_GOBJECTS_RE2, AobTarget::GObjects, 0, 3, 7, -0x10, 15, "RE", "FF7 Remake extended"),
    SIG_RIP("GOBJ_V13", AOB_GOBJECTS_V13, AobTarget::GObjects, 0, 3, 7, 0, 16, "V", "Palworld extended context"),
    SIG_RIP("GOBJ_ES2_1", AOB_GOBJECTS_ES2_1, AobTarget::GObjects, 0, 3, 7, 0, 17, "ES2", "UE5.5 AllocateUObjectIndex"),
    SIG_RIP("GOBJ_V12", AOB_GOBJECTS_V12, AobTarget::GObjects, 0, 3, 7, -0x10, 18, "V", "FF7 Remake"),
    SIG_RIP("GOBJ_SF_1", AOB_GOBJECTS_SF_1, AobTarget::GObjects, 0, 3, 7, 0, 19, "SF", "SatisfFactory via _imp_ (in EXE)"),

    // Priority 20-40: Medium patterns
    { "GOBJ_RE1", AOB_GOBJECTS_RE1, AobTarget::GObjects, AobResolve::RipBoth,
      0, 2, 6, 0, 25, 0, false, "RE", "FF7 Rebirth add+cmp+jge" },
    SIG_RIP("GOBJ_V4",  AOB_GOBJECTS_V4,  AobTarget::GObjects, 0, 3, 7, 0, 30, "V", "classic UE5 longer context"),
    SIG_RIP("GOBJ_V8",  AOB_GOBJECTS_V8,  AobTarget::GObjects, 0, 3, 7, 0, 32, "V", "bit shift variant"),
    SIG_RIP("GOBJ_V9",  AOB_GOBJECTS_V9,  AobTarget::GObjects, 0, 3, 7, 0, 33, "V", "extended index cdqe"),
    SIG_RIP("GOBJ_V7",  AOB_GOBJECTS_V7,  AobTarget::GObjects, 0, 3, 7, 0, 34, "V", "GSpots cdq movzx"),
    SIG_RIP("GOBJ_UD1", AOB_GOBJECTS_UD1, AobTarget::GObjects, 0, 3, 7, 0, 35, "UD", "UEDumper"),

    // Priority 50: Standard short patterns
    SIG_RIP("GOBJ_V2",  AOB_GOBJECTS_V2,  AobTarget::GObjects, 0, 3, 7, 0, 50, "V", "common UE5.3+"),
    SIG_RIP("GOBJ_V1",  AOB_GOBJECTS_V1,  AobTarget::GObjects, 0, 3, 7, 0, 51, "V", "classic UE5.0-5.2"),
    SIG_RIP("GOBJ_V6",  AOB_GOBJECTS_V6,  AobTarget::GObjects, 0, 3, 7, 0, 52, "V", "alt mov rcx"),
    SIG_RIP("GOBJ_V3",  AOB_GOBJECTS_V3,  AobTarget::GObjects, 0, 3, 7, 0, 53, "V", "mov r8"),
    SIG_RIP("GOBJ_V5",  AOB_GOBJECTS_V5,  AobTarget::GObjects, 0, 3, 7, 0, 54, "V", "mov r10"),
    SIG_RIP("GOBJ_CT3", AOB_GOBJECTS_CT3, AobTarget::GObjects, 0, 3, 7, 0, 55, "CT", "mov r8; cmp"),

    // Priority 60: Patternsleuth (instrOffset != 0)
    SIG_RIP("GOBJ_PS1", AOB_GOBJECTS_PS1, AobTarget::GObjects, 23, 3, 7, 0, 60, "PS", "cmp/cmp/jne; lea"),
    SIG_RIP("GOBJ_PS2", AOB_GOBJECTS_PS2, AobTarget::GObjects,  2, 3, 7, 0, 61, "PS", "jz; lea rcx"),
    SIG_RIP("GOBJ_PS3", AOB_GOBJECTS_PS3, AobTarget::GObjects,  5, 3, 7, 0, 62, "PS", "jne; mov; lea rcx"),
    SIG_RIP("GOBJ_PS4", AOB_GOBJECTS_PS4, AobTarget::GObjects, 16, 3, 7, 0, 63, "PS", "test; mov; lea r11"),
    SIG_RIP("GOBJ_PS5", AOB_GOBJECTS_PS5, AobTarget::GObjects, 12, 3, 7, 0, 64, "PS", "or; and; mov; lea rcx"),
    SIG_RIP("GOBJ_PS6", AOB_GOBJECTS_PS6, AobTarget::GObjects, 14, 2, 6, 0, 65, "PS", "arithmetic sub eax"),
    SIG_RIP("GOBJ_PS7", AOB_GOBJECTS_PS7, AobTarget::GObjects, 17, 2, 6, 0, 66, "PS", "arithmetic add ecx"),

    // Priority 80: UE4/legacy
    SIG_RIP("GOBJ_CT1", AOB_GOBJECTS_CT1, AobTarget::GObjects, 5, 3, 7, 0, 80, "CT", "UE4 Dumper.CT v5+"),
};

// ── GNames ───────────────────────────────────────────────────────────────
constexpr AobSignature GNAMES_PATTERNS[] = {
    // Priority 0: Symbol exports → scan function body for FNamePool references
    SIG_SYM_CALL("GNAM_EXP_TOSTR", EXPORT_FNAME_TOSTRING, AobTarget::GNames, 0, "FName::ToString export"),
    SIG_SYM_CALL("GNAM_EXP_CTOR",  EXPORT_FNAME_CTOR,     AobTarget::GNames, 1, "FName ctor (wchar) export"),
    SIG_SYM_CALL("GNAM_EXP_CTOR2", EXPORT_FNAME_CTOR_CHAR,AobTarget::GNames, 2, "FName ctor (char) export"),

    // Priority 5: FName ctor call-site (follows CALL, scans body)
    { "GNAM_V7", AOB_GNAMES_V7_FNAME_CTOR, AobTarget::GNames, AobResolve::CallFollow,
      0, 0, 0, 0, 5, 11, false, "V", "FF7 Rebirth FName ctor call-site" },

    // Priority 10-20: Long specific patterns
    SIG_RIP("GNAM_V8",    AOB_GNAMES_V8,     AobTarget::GNames, 0, 3, 7, 0, 10, "V", "Palworld extended context"),
    SIG_RIP("GNAM_V5",    AOB_GNAMES_V5,     AobTarget::GNames, 0, 3, 7, 0, 12, "V", "lea rcx; call; mov byte[],1 extended"),
    SIG_RIP("GNAM_ES2_1", AOB_GNAMES_ES2_1,  AobTarget::GNames, 0, 3, 7, 0, 15, "ES2", "UE5.5 ResolveEntry"),
    SIG_RIP("GNAM_SF_1",  AOB_GNAMES_SF_1,   AobTarget::GNames, 0, 3, 7, 0, 16, "SF", "SatisfFactory NamePoolData init (in Core DLL)"),
    SIG_RIP("GNAM_CT1",   AOB_GNAMES_CT1,    AobTarget::GNames, 0, 3, 7, 0, 18, "CT", "UE4 Dumper.CT v6+ lea r8; jmp 16"),
    SIG_RIP("GNAM_CT2",   AOB_GNAMES_CT2,    AobTarget::GNames, 0, 3, 7, 0, 19, "CT", "UE4 Dumper.CT UE4.23+ main"),
    SIG_RIP("GNAM_UD2",   AOB_GNAMES_UD2,    AobTarget::GNames, 0, 3, 7, 0, 20, "UD", "UEDumper lea rcx; call; mov r8"),

    // Priority 30-40: Medium patterns
    SIG_RIP("GNAM_SF_2",  AOB_GNAMES_SF_2,   AobTarget::GNames, 0, 3, 7, 0, 30, "SF", "SatisfFactory SHL pattern (in Core DLL)"),
    SIG_RIP("GNAM_SF_3",  AOB_GNAMES_SF_3,   AobTarget::GNames, 0, 3, 7, 0, 31, "SF", "SatisfFactory FNameEntryId (in Core DLL)"),
    SIG_RIP("GNAM_V6",    AOB_GNAMES_V6,     AobTarget::GNames, 0, 3, 7, 0, 35, "V", "GSpots UE5+ mov rax; test; jnz"),
    SIG_RIP("GNAM_V2",    AOB_GNAMES_V2,     AobTarget::GNames, 0, 3, 7, 0, 36, "V", "lea rcx; call; mov byte ptr"),

    // Priority 50: Short patterns
    SIG_RIP("GNAM_V1",    AOB_GNAMES_V1,     AobTarget::GNames, 0, 3, 7, 0, 50, "V", "lea rsi; jmp"),
    SIG_RIP("GNAM_V3",    AOB_GNAMES_V3,     AobTarget::GNames, 0, 3, 7, 0, 51, "V", "lea rax; jmp"),
    SIG_RIP("GNAM_V4",    AOB_GNAMES_V4,     AobTarget::GNames, 0, 3, 7, 0, 52, "V", "lea r8; jmp"),
    SIG_RIP("GNAM_D7_1",  AOB_GNAMES_D7_1,   AobTarget::GNames, 0, 3, 7, 0, 55, "D7", "Dumper-7 basic lea rcx; call"),

    // Priority 60: Patternsleuth
    SIG_RIP("GNAM_PS1",   AOB_GNAMES_PS1,    AobTarget::GNames, 2, 3, 7, 0, 60, "PS", "jz+9; lea r8"),
    SIG_RIP("GNAM_PS2",   AOB_GNAMES_PS2,    AobTarget::GNames, 7, 3, 7, 0, 61, "PS", "sub rsp; shr; lea rbp"),

    // Priority 80: UE4/legacy (pre-FNamePool)
    SIG_RIP("GNAM_CT3",   AOB_GNAMES_CT3,    AobTarget::GNames, 4, 3, 7, 0, 80, "CT", "UE4 <4.23 pre-FNamePool deref"),
    SIG_RIP("GNAM_CT4",   AOB_GNAMES_CT4,    AobTarget::GNames, 3, 3, 7, 0, 81, "CT", "UE4 pre-FNamePool write pattern"),
};

// ── GWorld ───────────────────────────────────────────────────────────────
constexpr AobSignature GWORLD_PATTERNS[] = {
    // Priority 0: Symbol export
    SIG_EXPORT("GWLD_EXP", EXPORT_GWORLD, AobTarget::GWorld, 0, "UWorldProxy symbol"),

    // Priority 10-20: Long specific patterns (from ES2, SF, TQ2)
    SIG_GWORLD_RIP("GWLD_ES2_1", AOB_GWORLD_ES2_1, 0, 3, 7, 0, 10, false, "ES2", "UE5.5 26-byte lea+mov chain"),
    SIG_GWORLD_RIP("GWLD_ES2_2", AOB_GWORLD_ES2_2, 0, 4, 8, 0, 11, false, "ES2", "UE5.5 CMOVZ r13"),
    SIG_GWORLD_RIP("GWLD_ES2_3", AOB_GWORLD_ES2_3, 0, 3, 7, 0, 12, false, "ES2", "UE5.5 cmp [rcx+2C0]"),
    SIG_GWORLD_RIP("GWLD_ES2_4", AOB_GWORLD_ES2_4, 0, 3, 7, 0, 13, false, "ES2", "UE5.5 cmp+and GWorld"),
    SIG_GWORLD_RIP("GWLD_ES2_5", AOB_GWORLD_ES2_5, 0, 3, 7, 0, 14, false, "ES2", "UE5.5 call r12 loop"),
    SIG_GWORLD_RIP("GWLD_ES2_6", AOB_GWORLD_ES2_6, 0, 3, 7, 0, 15, false, "ES2", "UE5.5 cmovne+call rbx"),
    SIG_GWORLD_RIP("GWLD_TQ_1",  AOB_GWORLD_TQ_1,  0, 3, 7, 0, 16, false, "TQ", "TQ2 extended V3"),
    SIG_GWORLD_RIP("GWLD_TQ_2",  AOB_GWORLD_TQ_2,  0, 3, 7, 0, 17, false, "TQ", "TQ2 dual mov"),
    SIG_GWORLD_RIP("GWLD_V7",    AOB_GWORLD_V7,     0, 3, 7, 0, 18, false, "V", "Palworld long context"),
    SIG_GWORLD_RIP("GWLD_V1",    AOB_GWORLD_V1,     0, 3, 7, 0, 19, false, "V", "cmp/cmovz"),

    // Priority 20-30: SatisfFactory DLL patterns
    SIG_GWORLD_RIP("GWLD_SF_1",  AOB_GWORLD_SF_1,   0, 3, 7, 0, 20, false, "SF", "Engine DLL UGameEngine::Tick"),
    SIG_GWORLD_RIP("GWLD_SF_2",  AOB_GWORLD_SF_2,   0, 3, 7, 0, 21, false, "SF", "Engine DLL FAudioDeviceManager"),
    SIG_GWORLD_RIP("GWLD_SF_3",  AOB_GWORLD_SF_3,   0, 3, 7, 0, 22, false, "SF", "Engine DLL UWorld::FinishDestroy"),
    SIG_GWORLD_RIP("GWLD_SF_4",  AOB_GWORLD_SF_4,   0, 3, 7, 0, 23, false, "SF", "Engine DLL GetWorldFromContextObject"),
    SIG_GWORLD_RIP("GWLD_SF_5",  AOB_GWORLD_SF_5,   0, 3, 7, 0, 24, false, "SF", "Engine DLL FMallocLeakReporter"),

    // Priority 30: Wildcard-prefixed TQ2 patterns
    SIG_GWORLD_RIP("GWLD_TQ_3",  AOB_GWORLD_TQ_3,   3, 3, 7, 0, 30, false, "TQ", "TQ2 ??-prefix mov rax"),
    { "GWLD_TQ_4", AOB_GWORLD_TQ_4, AobTarget::GWorld, AobResolve::RipBoth,
      3, 3, 7, 0, 31, 0, true, "TQ", "TQ2 ??-prefix write pattern" },

    // Priority 50: Standard short GWorld patterns
    SIG_GWORLD_RIP("GWLD_V3",    AOB_GWORLD_V3,     0, 3, 7, 0, 50, false, "V", "mov rbx test rbx"),
    SIG_GWORLD_RIP("GWLD_V4",    AOB_GWORLD_V4,     0, 3, 7, 0, 51, false, "V", "mov rdi test rdi"),
    SIG_GWORLD_RIP("GWLD_V5",    AOB_GWORLD_V5,     0, 3, 7, 0, 52, false, "V", "cmp [rip] je"),
    { "GWLD_V2", AOB_GWORLD_V2, AobTarget::GWorld, AobResolve::RipBoth,
      0, 3, 7, 0, 55, 0, true, "V", "write: mov [rip],rax" },
    { "GWLD_V6", AOB_GWORLD_V6, AobTarget::GWorld, AobResolve::RipBoth,
      0, 3, 7, 0, 56, 0, true, "V", "write: mov [rip],rbx; call" },
};

#undef SIG_RIP
#undef SIG_RIP_DIRECT
#undef SIG_EXPORT
#undef SIG_SYM_CALL
#undef SIG_GWORLD_RIP


// ============================================================
// Pattern count summary
// ============================================================
// GObjects: 27 (original) + 2 (ES2, SF) = 29 patterns + 1 symbol export
// GNames:   17 (original) + 4 (ES2, SF) = 21 patterns + 3 symbol exports
// GWorld:    7 (original) + 15 (ES2, SF, TQ) = 22 patterns + 1 symbol export
// Total:    72 AOB patterns + 5 symbol exports = 77 entries

} // namespace Sig
