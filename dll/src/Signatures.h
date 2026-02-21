#pragma once

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
//   4. Register it in OffsetFinder.cpp's pattern list
//
// Sources:
//   V1-V13  : Original UE5CEDumper patterns
//   PS1-PS7 : patternsleuth (github.com/trumank/patternsleuth)
//   RE1-RE5 : RE-UE4SS CustomGameConfigs (github.com/UE4SS-RE/RE-UE4SS)
//   D7_1    : Dumper-7 (github.com/Encryqed/Dumper-7)
//   CT1-CT5 : UE4 Dumper.CT (vendor/UE4 Dumper.CT)
//   UD1-UD3 : UEDumper (github.com/Spuckwaffel/UEDumper)
// ============================================================

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


// ============================================================
// Pattern count summary
// ============================================================
// GObjects: 13 (V) + 7 (PS) + 3 (RE) + 3 (CT) + 1 (UD) = 27 patterns
// GNames:   8 (V) + 2 (PS) + 1 (D7) + 4 (CT) + 2 (UD) = 17 patterns
// GWorld:   7 (V) = 7 patterns
// Total:    51 patterns + 4 symbol exports

} // namespace Sig
