# Cheat Engine CCODE — Complete Reference Manual

**Why this doc is in this repo.** UE5CEDumper emits Cheat Engine artifacts — AA scripts with `{$lua}`
blocks, `.CT` cheat tables, CE XML pointer chains, Structure Dissect files — and talks to the AOBMaker
CE plugin over a named pipe; it does not itself build a CE plugin today. Every CE script this repo emits
right now uses `{$lua}` blocks (see `ui/UE5DumpUI/Services/CeLuaHygiene.cs` and the `*ScriptGenerator.cs`
family). `{$CCODE}` is the native-code alternative for the same AA hook slot — compiled by CE's bundled
TCC, roughly free at runtime where a `{$lua}` block costs an interpreter round trip per hit. §12 is the
direct `{$LUACODE}` comparison, and §4's parameter-pointer layout is shared by both, so it already
describes the register marshalling behind the blocks this repo generates. Cross-reference
[docs/export-formats.md](export-formats.md) and the "CE Lua output hygiene" rule in `CLAUDE.md`.
**This file is a mirror, not the master.**

Master copy: `<private-ce-repo>/docs/CE-CCODE-Reference.md` — edit there first, then mirror here.

> This document was written from an analysis of the CE source (`autoassemblercode.pas`, `tcclib.pas`,
> `autoassembler.pas`, `Assemblerunit.pas`). The goal is a reference complete enough that a developer
> does not have to consult the CE source code again to use CCODE correctly.

### Version coordinates

| What | Value |
|------|-----|
| CE source tree the analysis is based on | `D:\Github\cheat-engine`, tag **`7.5-195`**, HEAD `4178e037` (level with `upstream/master`) |
| Installed CE binaries cross-checked against | `C:\Program Files\Cheat Engine\`, **7.7.0.10568** (ProductVersion 7.7) |

So: **behaviour descriptions and line numbers come from the 7.5 source; the shipped-file lists (TCC DLLs,
`include\`) come from the 7.7 install**. Where the two differ, it is marked individually in the text.

> ⚠ **Every "line count / file count / number of items" in this document is DERIVED from the source tree
> and the install directory — regenerate them, do not hand-edit.**
> How to regenerate:
> - File line counts — `wc -l "<CE source>/Cheat Engine/tcclib.pas"`
> - Number of shipped TCC DLLs — `ls "C:\Program Files\Cheat Engine\" | grep -iE "^tcc.*\.dll$"`
>   (using just `^tcc` also counts the `tcclib\` **directory**, giving one too many)
> - Header file list — `ls "C:\Program Files\Cheat Engine\include\"`
> - Number of TCC runtime helpers — `grep -c "tcclibimportlist.Add" autoassemblercode.pas`
>
> This repo's CI gate `tools/check_derived_counts.py` does **not** cover these numbers, because it only
> derives from the UE5CEDumper tree and every number here comes from the external CE / AOBMaker trees —
> so they must be re-derived by hand with the commands above.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Syntax](#2-syntax)
3. [Parameter Mapping — Registers and Variables](#3-parameter-mapping--registers-and-variables)
4. [Memory Layout — the Parameter Pointer Structure](#4-memory-layout--the-parameter-pointer-structure)
5. [The SafeCall Stub](#5-the-safecall-stub)
6. [Compilation Pipeline — the Two-Pass Flow](#6-compilation-pipeline--the-two-pass-flow)
7. [Available C Functions and Headers](#7-available-c-functions-and-headers)
8. [Calling External Functions / DLLs with extern](#8-calling-external-functions--dlls-with-extern)
9. [Symbol Interop — the Bridge Between CCODE and AA Script](#9-symbol-interop--the-bridge-between-ccode-and-aa-script)
10. [XMM Register Access in Detail](#10-xmm-register-access-in-detail)
11. [Block Options](#11-block-options)
12. [LUACODE Comparison](#12-luacode-comparison)
13. [Limits and Caveats](#13-limits-and-caveats)
14. [Practical Examples](#14-practical-examples)
15. [Internal Naming Conventions](#15-internal-naming-conventions)
16. [Debug Support](#16-debug-support)
---

## 1. Overview

CCODE is an AutoAssembler extension introduced in CE 7.3+ that lets you embed C code inside an AA script.
CE uses the bundled **TCC (Tiny C Compiler)** to compile the C into machine code, which is injected into
the target process's memory and executed there.

### Core flow

```
{$CCODE} block in the user's AA script
  → CE generates a C function wrapper (parameter marshalling)
  → CE generates a SafeCall Stub (register save/restore)
  → TCC compiles the C code to machine code
  → the machine code is written into the target process's memory
  → the AA script's call instruction calls the SafeCall Stub
  → the stub saves register state → calls the compiled C function → restores register state
```

### The two C block types

| Block      | Syntax                      | Purpose                                     |
|-----------|---------------------------|-------------------------------------------|
| `{$C}`    | `{$C}...{$ASM}`            | Raw C code, for declaring headers / helper functions / typedefs |
| `{$CCODE}` | `{$CCODE params}...{$ASM}` | C function with parameter mapping; generates a wrapper + safecall stub automatically |

---

## 2. Syntax

### {$CCODE} — a C function with parameters

```
{$CCODE variableName1=REGISTER1 variableName2=REGISTER2 ...}
  // C code
  // variableName1 and variableName2 are already C local variables
  // modifying them writes back to the corresponding register automatically
{$ASM}
```

**Example:**
```
alloc(newmem, 256)

newmem:
{$CCODE health=RBX playerbase=RCX}
  int isPlayer = *(int *)((unsigned long long)playerbase + 0xB8);
  if (isPlayer)
    health = 100000;
{$ASM}
  jmp returnhere
```

### {$C} — a raw C block (generates no function)

```
{$C}
  // headers, typedefs, helper functions
  // this code is added to TCC's compilation unit but is not executed directly
{$ASM}
```

**Example:**
```
{$C}
typedef struct {
  float x, y, z;
} Vec3;

float vec3_length(Vec3 *v) {
  return sqrt(v->x * v->x + v->y * v->y + v->z * v->z);
}
{$ASM}

{$CCODE pos=RCX}
  Vec3 *position = (Vec3 *)pos;
  float len = vec3_length(position);
  // ...
{$ASM}
```

---

## 3. Parameter Mapping — Registers and Variables

### Syntax

`{$CCODE myVar=RAX anotherVar=XMM0}`

Each parameter has the form `CVariableName=RegisterName`, and they can **only be separated by spaces**.

> ⚠ **Do not use commas, and do not leave whitespace on either side of `=`.** Parameters are split with
> `s.Split(' ')` (autoassemblercode.pas:194), and each resulting piece is then split on `'='` (:199), with
> "anything that does not come out as exactly 2 pieces gets `continue`d" (:201-202). So
> `{$CCODE a=RAX,b=RBX}` splits into the single piece `a=RAX,b=RBX`, which splits on `=` into 3 pieces →
> **both parameters are silently dropped together** (the C side then reports undeclared identifiers, and
> the error message never mentions parameter parsing at all).
> `{$CCODE a = RAX}` is just as broken, but breaks differently: `a` and `RAX` are each dropped, while the
> `=` in the middle passes the check (it splits into two empty strings — exactly 2 pieces), producing a
> parameter with an **empty variable name** and the default contextitem 0 → a `unsigned long long =*(...)`
> syntax error on the C side.
> Commas are separators only in the parse of **block options** (`NODEBUG` / `PREFIX=` / `KERNEL`)
> (autoassemblercode.pas:696 `params:=s.Split([' ',',']);`).

### Available registers (64-bit process)

| Register name | contextitem | C type | Notes |
|-----------|-------------|--------|------|
| `RAX` | 0 | `unsigned long long` | General-purpose register (special: indirect access) |
| `RBX` | 1 | `unsigned long long` | General-purpose register |
| `RCX` | 2 | `unsigned long long` | General-purpose register |
| `RDX` | 3 | `unsigned long long` | General-purpose register |
| `RSI` | 4 | `unsigned long long` | General-purpose register |
| `RDI` | 5 | `unsigned long long` | General-purpose register |
| `RSP` | 6 | `unsigned long long` | Read-only — **never written back** |
| `RBP` | 7 | `unsigned long long` | General-purpose register |
| `R8`~`R15` | 8~15 | `unsigned long long` | Extended registers |
| `RAXF` / `EAXF` | 16 | `float` | ⚠ **Do not use (CE's CCODE implementation is broken)** |
| `RBXF` `RCXF` `RDXF` `RSIF` `RDIF` | 17~21 | `float` | Map to RBX / RCX / RDX / RSI / RDI (correct) |
| `RBPF` / `EBPF` | 22 | `float` | ⚠ **Actually lands on 0x228 = the original-RSP pointer slot, not RBP** |
| `RSPF` / `ESPF` | 23 | `float` | ⚠ **Actually lands on 0x230 = the RBP slot, and it does get written back** |
| `R8F`~`R15F` | 24~31 | `float` | Map to R8~R15 (correct) |
| `XMM0`~`XMM15` | 32~47 | `xmmreg` (struct) | 128-bit SSE registers (whole-register access) |
| `XMM0.0`~`XMM15.3` | 48~111 | `float` | Individual 32-bit float elements of an XMM |
| `XMM0.0D`~`XMM15.1D` | 112~143 | `double` | Individual 64-bit double elements of an XMM |

> ⚠ **`RAXF` / `EAXF` are unusable in {$CCODE}.**
> The integer `RAX` (contextitem 0) does two dereferences, but `RAXF` (contextitem 16) does **not**: what
> CE generates is
> `float v = *(float *)((unsigned long long)parameters + 0x228);` (autoassemblercode.pas:807),
> and `parameters+0x228` holds the "original RSP pointer", not RAX. What you read is the low 32 bits of
> that pointer (garbage); worse, the write-back `*(float *)(parameters+0x228) = v;` (:865) destroys the low
> 32 bits of that pointer directly, and the end of the stub restores RSP from that slot with
> `mov rsp,[rsp+248]` (:354) → **corrupted stack pointer, guaranteed crash**.
> To use RAX as a float, declare `x=RAX` and bit-cast it yourself in C.
> (Note: `{$LUACODE}`'s contextitem 16 is the correct `readFloat(readPointer(parameters+0x228))` — only
> CCODE is broken.)

> ⚠ **`RBPF` and `RSPF` are unusable.** The numbering order of the float aliases (…`RDIF`=21, `RBPF`=22,
> `RSPF`=23…, autoassemblercode.pas:229-244) is the **reverse** of the integer order (…`RSP`=6, `RBP`=7…,
> :218-219), yet both share the one formula `0x200+(contextitem-1-16)*8` (read :808 / write-back :866):
> `RBPF` → 0x200+(22-17)*8 = **0x228** (the original-RSP pointer slot; writing back destroys RSP → crash);
> `RSPF` → 0x200+(23-17)*8 = **0x230** (the RBP slot; and contextitem 23 falls inside the `17..31`
> write-back branch, so unlike the integer `RSP`(6) it is not excluded — a float really is written into RBP
> and restored at the end of the stub → RBP corrupted).
> The same misalignment exists in `{$LUACODE}` — the Lua side uses `0x200+(contextitem-17)*8`
> (autoassemblercode.pas:1005 read / :1046 write), numerically identical to the C side.

> ⚠ **XMM numbers are not range-checked — going out of range does not error, it duplicates the previous
> parameter's declaration.**
> The parser accepts any `XMMn` (`o.contextItem:=32+xmmnr`, autoassemblercode.pas:257;
> `112+xmmnr*2+subnr` at :276, `48+xmmnr*4+subnr` at :278) with no upper bound.
> But **the read loop does not clear `s` before the `case`** (:801-803; compare the write-back loop at :859
> which does have `s:='';`), so a contextitem that falls outside every branch leaves `s` holding the previous
> iteration's string, and `cscript.add('  '+s)` (:817) emits it a second time → a **duplicate definition
> compile error**; and if it is the first parameter, `s` still holds the raw parameter string carved out at
> :769 by `s:=copy(s,9,length(s)-9)`, so that whole string is emitted as a line of C code.
> 32-bit is the easiest place to hit this: the generator only handles 32..39 / 48..79 / 112..127 (:830, :835,
> :836; :837 is the `case`'s closing `end;`, not a branch),
> while the parser will happily produce 40 for `XMM8`.

### Available registers (32-bit process)

| Register name | contextitem | C type |
|-----------|-------------|--------|
| `EAX` | 0 | `unsigned long` |
| `EBX` | 1 | `unsigned long` |
| `ECX` | 2 | `unsigned long` |
| `EDX` | 3 | `unsigned long` |
| `ESI` | 4 | `unsigned long` |
| `EDI` | 5 | `unsigned long` |
| `ESP` | 6 | `unsigned long` (read-only) |
| `EBP` | 7 | `unsigned long` |
| `EAXF` | 16 | `float` ⚠ **Do not use (same defect as 64-bit `RAXF`)** |
| `EBXF` `ECXF` `EDXF` `ESIF` `EDIF` | 17~21 | `float` — map to EBX/ECX/EDX/ESI/EDI |
| `EBPF` | 22 | `float` ⚠ actually lands on 0x214 = the original-ESP pointer slot |
| `ESPF` | 23 | `float` ⚠ actually lands on 0x218 = the EBP slot, and it does get written back |
| `XMM0`~`XMM7` | 32~39 | `xmmreg` (struct) |
| `XMM0.0`~`XMM7.3` | 48~79 | `float` |
| `XMM0.0D`~`XMM7.1D` | 112~127 | `double` |

32-bit and 64-bit are exactly the same bug, only with the multiplier changed to `0x200+(contextitem-1-16)*4`
(read autoassemblercode.pas:829 / write-back :891): `EBPF`→0x214, `ESPF`→0x218.
`EAXF`(16) likewise reads and writes 0x214 **directly** instead of dereferencing (:828 / :890).

### Write-back rules

- **Every register** (except RSP/ESP) is written back automatically when the CCODE block ends
- **RSP/ESP**: never written back (safety; the `6:` branch at autoassemblercode.pas:864 / :889 is an empty string)
- Write-back order: the order the parameters were declared in

### ⚠ A misspelled register name does not error — it silently becomes RAX

`o` is zeroed with `FillByte(o,sizeof(o),0)` before each parameter is parsed (autoassemblercode.pas:197),
and contextitem **0 is RAX/EAX** (:212).
A name that does not start with `XMM` and is not in the lookup table (`x=RXA`, `x=AL`, `x=EX`…) falls into the
`else` branch of the `case` starting at :245, and that branch only acts when
`(length(regname)>=4) and regname.StartsWith('XMM')` (:248); everything else **raises no exception and emits no
warning**, and the parameter is added to the list anyway (:285-286) with contextitem still 0.
The result: the variable is bound to **RAX**, and RAX **is written back** when the CCODE block ends.
(Only a name that starts with `XMM` but is malformed will `raise exception`; see autoassemblercode.pas:262 / :269.)

---

## 4. Memory Layout — the Parameter Pointer Structure

A CCODE function receives a `void *parameters` pointer, pointing at the register state the SafeCall Stub saved
on the stack.

### 64-bit layout

The SafeCall Stub allocates `0x2a0` (672) bytes, and `parameters` points at `[rsp+0x20]`:

```
Offset    Contents                  Size
────────────────────────────────────────────
0x000     FX/SSE state (fxsave)     512 bytes
  ├ 0x0A0   XMM0                    16 bytes
  ├ 0x0B0   XMM1                    16 bytes
  ├ ...
  └ 0x190   XMM15                   16 bytes
0x200     RBX                       8 bytes
0x208     RCX                       8 bytes
0x210     RDX                       8 bytes
0x218     RSI                       8 bytes
0x220     RDI                       8 bytes
0x228     original RSP pointer (= the common source for RSP/RAX/FLAGS)  8 bytes
            ├ [value]+0  = original RAX
            ├ [value]+8  = original RFLAGS
            ├ [value]+16 = return address of the call to the stub
            └ [value]+24 = original RSP (RSP at the hook site)
0x230     RBP                       8 bytes
0x238     R8                        8 bytes
0x240     R9                        8 bytes
0x248     R10                       8 bytes
0x250     R11                       8 bytes
0x258     R12                       8 bytes
0x260     R13                       8 bytes
0x268     R14                       8 bytes
0x270     R15                       8 bytes
```

> **The tables in §4 and §5 use different bases — do not mix them.** §4's offsets are relative to
> `parameters`; the offsets in §5's stub pseudo-code are relative to `rsp`. The two differ by a fixed
> **0x20** (`lea rcx,[rsp+20]`, autoassemblercode.pas:333). For example RCX is `parameters+0x208` in §4 and
> `[rsp+0x228]` in §5; and §4's `parameters+0x228` (the original RSP pointer) is `[rsp+0x248]` in §5. Both
> tables are correct.

The 0x228 slot is simultaneously the indirect source for RAX, the source for RSP, and the basis on which the
end of the stub restores the stack with `mov rsp,[rsp+248]` (:354) — so any write to it is fatal (see the
`RAXF` / `RBPF` warnings in §3).

**RAX special access**: RAX is not stored directly in the structure above. `parameters+0x228` holds a
**pointer** to the RAX value on the original stack. Accessing RAX therefore takes two dereferences:
```c
// read RAX
unsigned long long rax = *(unsigned long long *)*(unsigned long long *)((unsigned long long)parameters + 0x228);
// write RAX back
*(unsigned long long *)*(unsigned long long *)((unsigned long long)parameters + 0x228) = rax;
```

**RSP special handling**: on read, an offset of 24 (0x18) is added to recover the true original RSP:
```c
unsigned long long rsp = *(unsigned long long *)((unsigned long long)parameters + 0x228) + 24;
```

### 64-bit offset formulas

CE's `case` branches are exactly the following (autoassemblercode.pas:804-815); contextitem **0 and 16 are
hardcoded to 0x228** (no formula), and contextitem **6 adds +24 after the formula result**:

```
contextitem 0   (RAX)        : *(ull*)*(ull*)(parameters+0x228)      ← two dereferences, no formula
contextitem 1..5, 7..15      : 0x200 + (contextitem - 1) * 8
contextitem 6   (RSP)        : *(ull*)(parameters + 0x228) + 24      ← +24 applied after the formula result 0x228; never written back
contextitem 16  (RAXF)       : *(float*)(parameters+0x228)           ← ⚠ broken, see §3
contextitem 17..31 (float)   : 0x200 + (contextitem - 1 - 16) * 8    ← ⚠ 22/23 collide with the RSP/RBP slots
contextitem 32..47 (XMM)     : 0x0A0 + (contextitem - 32) * 16
contextitem 48..111 (float)  : 0x0A0 + (contextitem - 48) * 4        ← the parser computes 48 + xmmnr*4 + subnr
contextitem 112..143 (double): 0x0A0 + (contextitem - 112) * 8       ← the parser computes 112 + xmmnr*2 + subnr
```

### 32-bit layout

The stub allocates `0x220` (544) bytes:

```
Offset    Contents                  Size
────────────────────────────────────────────
0x000     FX/SSE state (fxsave)     512 bytes
  └ 0x0A0   XMM0~XMM7              16 bytes each
0x200     EBX                       4 bytes
0x204     ECX                       4 bytes
0x208     EDX                       4 bytes
0x20C     ESI                       4 bytes
0x210     EDI                       4 bytes
0x214     original ESP pointer (= the common source for ESP/EAX/EFLAGS)  4 bytes
            ├ [value]+0  = original EAX
            ├ [value]+4  = original EFLAGS
            ├ [value]+8  = return address of the call to the stub
            └ [value]+12 = original ESP
0x218     EBP                       4 bytes
```

> **On 32-bit, `parameters` IS the base of the stub's allocated block** (offset 0 = fxsave); there is **no**
> +0x20 offset as on 64-bit: 64-bit does `lea rcx,[rsp+20]` (autoassemblercode.pas:333), 32-bit does
> `mov eax,esp` / `push eax` (:382-383), and `fxsave [esp]` (:369) sits at offset 0. So on 32-bit the §4
> offsets and the §5 stub offsets are **the same set of numbers**.

### 32-bit offset formulas

```
contextitem 0  (EAX)         : *(ul*)*(ul*)(parameters+0x214)        ← two dereferences, no formula
contextitem 1..5, 7          : 0x200 + (contextitem - 1) * 4
contextitem 6  (ESP)         : *(ul*)(parameters+0x214) + 12         ← never written back
contextitem 16 (EAXF)        : *(float*)(parameters+0x214)           ← ⚠ broken, see §3
contextitem 17..23 (float)   : 0x200 + (contextitem - 1 - 16) * 4    ← ⚠ 22/23 collide with the ESP/EBP slots
contextitem 32..39 (XMM)     : 0x0A0 + (contextitem - 32) * 16
contextitem 48..79 (float)   : 0x0A0 + (contextitem - 48) * 4
contextitem 112..127 (double): 0x0A0 + (contextitem - 112) * 8
```

---

## 5. The SafeCall Stub

CE generates a SafeCall Stub (512 bytes) automatically for every CCODE block. It is responsible for:
1. Saving all CPU registers and FLAGS
2. Aligning the stack to a 16-byte boundary (required by the x86-64 ABI)
3. Saving FPU/SSE state (fxsave)
4. Calling the compiled C function
5. Restoring all state after the C function returns
6. Restoring the original RSP

### 64-bit SafeCall Stub pseudo-code

```asm
ceinternal_autofree_safecallstub_for_[functionname]:
  pushfq                                    ; save RFLAGS
  push rax                                  ; save RAX
  mov rax, rsp                              ; remember RSP after pushfq+push rax (this value points at the pushed RAX)
  and rsp, 0xFFFFFFFFFFFFFFF0               ; 16-byte alignment
  sub rsp, 0x2A0                            ; allocate space

  ; save state
  fxsave qword [rsp+0x20]                   ; SSE/FPU state
  mov [rsp+0x220], rbx
  mov [rsp+0x228], rcx
  mov [rsp+0x230], rdx
  mov [rsp+0x238], rsi
  mov [rsp+0x240], rdi
  mov [rsp+0x248], rax                      ; store RSP as it was after pushfq+push rax
                                            ; this value "points at" the pushed RAX (not at the state before the push)
  mov [rsp+0x250], rbp
  mov [rsp+0x258], r8
  mov [rsp+0x260], r9
  mov [rsp+0x268], r10
  mov [rsp+0x270], r11
  mov [rsp+0x278], r12
  mov [rsp+0x280], r13
  mov [rsp+0x288], r14
  mov [rsp+0x290], r15

  ; call the C function
  ; [rsp+0x248]+0 = original RAX
  ; [rsp+0x248]+8 = original RFLAGS
  lea rcx, [rsp+0x20]                       ; RCX = the parameters pointer
  call [functionname_address]               ; indirect call

  ; restore registers (CCODE may have modified them)
  mov r15, [rsp+0x290]
  mov r14, [rsp+0x288]
  mov r13, [rsp+0x280]
  mov r12, [rsp+0x278]
  mov r11, [rsp+0x270]
  mov r10, [rsp+0x268]
  mov r9,  [rsp+0x260]
  mov r8,  [rsp+0x258]
  mov rbp, [rsp+0x250]
  mov rdi, [rsp+0x240]
  mov rsi, [rsp+0x238]
  mov rdx, [rsp+0x230]
  mov rcx, [rsp+0x228]
  mov rbx, [rsp+0x220]

  fxrstor qword [rsp+0x20]                  ; restore SSE/FPU

  mov rsp, [rsp+0x248]                      ; restore original RSP
  pop rax                                   ; restore RAX
  popfq                                     ; restore RFLAGS
  ret
```

(The above matches autoassemblercode.pas:301-357 line for line.)

### 32-bit SafeCall Stub pseudo-code

```asm
ceinternal_autofree_safecallstub_for_[functionname]:
  pushfd                       ; save EFLAGS
  push eax
  mov eax, esp                 ; remember ESP after pushfd+push eax
  and esp, 0xFFFFFFF0          ; 16-byte alignment
  sub esp, 0x220               ; allocate space (544 bytes)

  fxsave [esp]                 ; ← note: on 32-bit this is [esp], with no +0x20
  mov [esp+0x200], ebx
  mov [esp+0x204], ecx
  mov [esp+0x208], edx
  mov [esp+0x20C], esi
  mov [esp+0x210], edi
  mov [esp+0x214], eax         ; original ESP pointer ([value]+0=EAX, +4=EFLAGS, +12=original ESP)
  mov [esp+0x218], ebp

  mov eax, esp                 ; parameters = the base of the block
  push eax                     ; cdecl: the argument goes on the stack, not in a register
  call [functionname_address]
  add esp, 4                   ; caller cleans the stack

  mov ebp, [esp+0x218]
  mov edi, [esp+0x210]
  mov esi, [esp+0x20C]
  mov edx, [esp+0x208]
  mov ecx, [esp+0x204]
  mov ebx, [esp+0x200]
  fxrstor [esp]
  mov esp, [esp+0x214]         ; restore original ESP
  pop eax
  popfd
  ret
```

Three points where this differs from 64-bit and is worth calling out:

1. **The argument is passed on the stack, cdecl-style** (`push eax` / `add esp,4`,
   autoassemblercode.pas:382-385), not via `lea rcx,[rsp+20]`.
2. **`parameters` is the base of the block** (`fxsave [esp]` sits at offset 0, :369) — there is no 0x20 of
   scratch space.
3. In the restore section the CE source actually writes `[rsp+210]`, `[rsp+20c]`, `[rsp+208]`, `[rsp+204]`,
   `[rsp+200]`, `fxrstor [rsp]`, `mov esp,[rsp+214]` (**`rsp` rather than `esp` inside the 32-bit branch**,
   autoassemblercode.pas:389-397). Only the save section before it (:370-376) uses `esp`. That is the CE
   source as-is; verify it yourself for 32-bit targets.

### Key properties

- **Stack management is fully automatic**: the user does not have to manage stack alignment
- **All registers saved/restored**: including RFLAGS and FPU/SSE state
- **The stack is already aligned inside the C function**: other C functions can be called safely
- **Stub size is fixed at 512 bytes**: `alloc(ceinternal_autofree_safecallstub_for_..., 512)`

---

## 6. Compilation Pipeline — the Two-Pass Flow

### Pass 1: preprocessing (`AutoAssemblerCCodePass1`)

1. Scan for `{$CCODE ...}` and `{$C}` blocks
2. Parse the parameter mapping (`variableName=REGISTER`)
3. Generate the C function wrapper:
   - Function name: `ceinternal_autofree_cfunction_at_line[line number]`
   - Parameter read code (extract register values from the parameters pointer)
   - The user's C code
   - Parameter write-back code (write modified values back to registers)
4. Generate the SafeCall Stub
5. Allocate the function-address pointer: `alloc(functionname_address, 8)`
6. Replace the CCODE block with: `call ceinternal_autofree_safecallstub_for_...`
7. **Test compile**: compile with TCC to determine the memory size needed
8. Insert: `alloc(ceinternal_autofree_ccode, bytesize)`

> **All `{$C}` and `{$CCODE}` blocks in one AA script are concatenated into a SINGLE C compilation unit.**
> CE maintains exactly one `cdata.cscript` string list (autoassemblercode.pas:719-720 and :772-773 both do
> `if dataForPass2.cdata.cscript=nil then dataForPass2.cdata.cscript:=tstringlist.create;`), each block is
> appended to it in order of appearance (:794 `cscript.add('void '+functionname+'(void *parameters)');`), and
> the whole thing is handed to TCC and compiled once (both the test compile at :1347 and the real compile).
> Consequences:
> - Typedefs / helpers in an earlier `{$C}` are directly visible to a later `{$CCODE}`; no need to redeclare them.
> - Two blocks defining a function or global variable with the same name **is a duplicate definition and fails to compile**.
> - `NODEBUG`, `KERNEL` and `PREFIX=` all live in the same `cdata` record → **they are settings for the whole
>   script, not for one block** (see §11).
> - The `xmmreg` typedef is only generated when some parameter uses a whole XMM register, and it is inserted
>   at the very top of the whole C unit (:1302-1321, lines 0~15).

### Pass 2: final compilation (`AutoAssemblerCCodePass2`)

1. The allocs are done, so the memory addresses are known
2. Call `TCC.compileScript()` to compile the C code to the target address
3. If the compiled result exceeds the allocated size, fall back to a bare `VirtualAllocEx` of
   "4 × the actual compiled size" and compile again
4. Run TCC relocate (address relocation)
5. Write the compiled machine code into the target process's memory
6. Extract symbol addresses and fill in the linklist (linking `functionname_address` → the real address)
7. **Only when the system does not support writable-and-executable memory**
   (`SystemSupportsWritableExecutableMemory=false`) are permissions touched: before writing, drop to
   `PAGE_READWRITE` (autoassemblercode.pas:543-544), and after writing apply `PAGE_EXECUTE_READ` (code
   sections) / `PAGE_READWRITE` (data sections) section by section as reported by TCC (:655-660, with the
   permission values from tcclib.pas:1418).
   Ordinary Windows targets do not take this step — memory permissions come from AA's own alloc.
8. Register debug information (STAB format)

### Memory allocation strategy

```
initial size = max(32, align(estimated_size * 2, 16))   ← AA's alloc(ceinternal_autofree_ccode, N)
not enough → a bare VirtualAllocEx of "4 × the actual compiled size", then compile again
still not enough → error '(Unexplained and unmitigated code growth)'
the second compile itself fails → error '3rd time failure of c-code'
```

> ⚠ **The reallocation goes through a bare `VirtualAllocEx`, not AA's alloc.** The CE source comment says so
> outright: `//this will be a slight memoryleak but whatever` (autoassemblercode.pas:486).
> This block is not registered in AA's alloc list, so **it is not freed when the script is disabled** — every
> enable leaks another one.
>
> On systems without W+X support (`SystemSupportsWritableExecutableMemory=false`) the reallocation is `1×`
> rather than `4×`, with `PAGE_READWRITE` permissions (:494-497). Under the `KERNEL` option this path uses
> `KernelAlloc(4*bytes.size)` instead (:489-490).

---

## 7. Available C Functions and Headers

### Standard C headers

CE ships a full set of mingw-style headers and mounts 6 include paths automatically at compile time
(tcclib.pas:1465-1474), so **`#include <string.h>` and `#include <windows.h>` can be used directly** (better
than hand-writing `extern`, since the header takes care of the calling convention).

Contents of `C:\Program Files\Cheat Engine\include\` in the 7.7 install (**derived — regenerate with
`ls "C:\Program Files\Cheat Engine\include\"`, do not hand-edit**):

```
_mingw.h assert.h celib.h celog.h cepipelib.c cesocket.h conio.h ctype.h dir.h direct.h
dirent.h dos.h errno.h excpt.h fcntl.h fenv.h float.h inttypes.h io.h jni.h limits.h
linux-x86_64-ExceptionHandler.c locale.h lua/ luaclient.c macspeedhack.c malloc.h math.h
mem.h memory.h process.h sec_api/ setjmp.h share.h signal.h stdalign.h stdarg.h stdatomic.h
stdbool.h stddef.h stdint.h stdio.h stdlib.h stdnoreturn.h string.h sys/ tcc/ tccdefs.h
tchar.h tgmath.h time.h vadefs.h values.h varargs.h wchar.h wctype.h winapi/ windowslite.h
```

Relative to `Cheat Engine\bin\include\` in the 7.5 source tree, 7.7 is a **pure superset**, adding:
`stdalign.h`, `stdatomic.h`, `stdnoreturn.h`, `tgmath.h`, `lua/`, `tcc/`, `luaclient.c`,
`macspeedhack.c`, `linux-x86_64-ExceptionHandler.c`.
(The first four are **C11 headers**. That is the correct reading of §13's "incomplete C11 coverage" — the
headers are there, complete C11 language/library coverage is not; do not write it up as "TCC does not
support C11 at all".)

`winapi\` additionally contains `windows.h`, `winbase.h`, `winuser.h`, `winnt.h` and others.

> ⚠ **But CE always compiles with `-nostdlib` — no library is linked at all.**
> **Both branches at tcclib.pas:1484-1487 carry `-nostdlib`**, so it is unconditional.
> The headers only supply *declarations*; every libc call must be resolved at link time by CE's symbol
> handler finding an exported symbol of the same name **inside the target process** (for example `sprintf`
> or `malloc` from `msvcrt.dll`). If the target process has not loaded the corresponding DLL, the symbol
> cannot be resolved. This is not "the C89/C99 standard library is available" but "the standard library's
> **declarations** are available; you have to source the implementation yourself".
> This is the same fact as §13's "no standard library linkage".
> A handful of 64-bit integer/float conversion helpers are the exception — CE compiles the TCC runtime in
> automatically (see below).

Common declarations (**the declarations come from the headers; the implementation must be findable in the
target process**):

```c
// memory operations
void *malloc(size_t size);
void *realloc(void *ptr, size_t size);
void *calloc(size_t nmemb, size_t size);
void free(void *ptr);
void *memcpy(void *dest, const void *src, size_t n);
void *memmove(void *dest, const void *src, size_t n);
void *memset(void *s, int c, size_t n);
int memcmp(const void *s1, const void *s2, size_t n);

// string operations
size_t strlen(const char *s);
char *strcpy(char *dest, const char *src);
char *strncpy(char *dest, const char *src, size_t n);
int strcmp(const char *s1, const char *s2);
char *strcat(char *dest, const char *src);
char *strchr(const char *s, int c);
char *strdup(const char *s);

// formatting
int sprintf(char *str, const char *format, ...);
int snprintf(char *str, size_t size, const char *format, ...);

// math (requires appropriate library support)
double sqrt(double x);
double pow(double x, double y);
double sin(double x);
double cos(double x);
```

### TCC runtime helpers (injected automatically)

When the compiled code references any of the following **16** symbols (the list is derived — regenerate with
`grep -c "tcclibimportlist.Add" autoassemblercode.pas`; the registrations are at autoassemblercode.pas:1188-1203),
CE checks whether the target process has `__floatundidf`; if not, it calls the Lua global function
`compileTCCLib()`, which compiles `<CE directory>\tcclib\lib\libtcc1.c` into the target process and registers
it under the symbol-list name **`TCC Library`**
(trigger path autoassemblercode.pas:1405-1418; Lua side `LuaHandler.pas:15388` / `:15410`).
A failed compile raises `'This code requires the TCC Library, but it failed to compile'`.

**32-bit only** (CE's own grouping comments are at autoassemblercode.pas:155-175):
```c
__divdi3      // int64 division
__moddi3      // int64 modulo
__udivdi3     // unsigned int64 division
__umoddi3     // unsigned int64 modulo
__ashrdi3     // int64 arithmetic shift right
__lshrdi3     // int64 logical shift right
__ashldi3     // int64 shift left
__floatundisf // unsigned int64 → float
```

**Possible on both 32-bit and 64-bit:**
```c
__floatundidf // unsigned int64 → double
__floatundixf // unsigned int64 → long double
__fixunssfdi  // float → unsigned int64
__fixsfdi     // float → int64
__fixunsdfdi  // double → unsigned int64
__fixdfdi     // double → int64
__fixunsxfdi  // long double → unsigned int64
__fixxfdi     // long double → int64
```

Separately, `__mzerosf` / `__mzerodf` (negative-zero constants) do **not** go through the TCC Library: when CE
detects them it simply inserts `float __mzerosf=-0.0f; //Autogenerated` /
`double __mzerodf=-0.0f; //Autogenerated` at the very top of the C source
(autoassemblercode.pas:1396-1401).

(The `tcclib1-ce` name in older documentation came from an out-of-date comment in the CE source; the file
actually compiled is `tcclib/lib/libtcc1.c`.)

### CE-specific helper headers

CE provides a few non-standard headers under `include\`:

**`celib.h`** — low-level critical section (an active-wait spinlock with no API dependencies). Here are the
contents from the 7.7 install (omitting the `#ifndef celib_h` / `#define celib_h` / `#endif` include guard):

```c
//first call lua command injectCEHelperLib()
typedef struct _cecs
{
  volatile int locked;
  volatile int threadid;
  volatile int lockcount;
} cecs, *Pcecs;

void csenter(cecs *cs);
void csleave(cecs *cs);
```

> ⚠ **All three fields are `volatile int`** — that is the whole point of a spinlock struct; do not copy it as
> plain `int`.
>
> ⚠ **`csenter`/`csleave` do not exist automatically.** The very first line of the header says
> `//first call lua command injectCEHelperLib()`: you must first call `injectCEHelperLib()` from CE's Lua
> console (or an autorun script) so CE assembles/compiles the spinlock and those two functions into the target
> process; then `#include <celib.h>` in your C. Use them without injecting first and the symbols will not be
> found at link time.
> (The implementation is in `autorun\celib.lua`: `function injectCEHelperLib()` is on line 8, and line 9's
> `if getAddressSafe('csenter')==nil then` does the de-duplication; it is itself written with a `{$c}` block,
> so it can be read as a CCODE example.)

**`celog.h`** — `debug_log(const char *format, ...)`, which becomes `OutputDebugStringA` on Windows (with
equivalents on Android/Apple).
**`windowslite.h`** — a slimmed-down set of Win32 types and declarations (`CRITICAL_SECTION`, `CreateFileA`,
`CreateNamedPipeA`…), for when you do not want to pull in the whole `windows.h`.
**`cesocket.h`** — Unix domain socket declarations (non-Windows use).
**`cepipelib.c`** — source for a named-pipe helper (a `.c`, so you `#include` it yourself).
**`tccdefs.h`** — TCC's built-in definitions file, present in **both 7.5 and 7.7**. **`luaclient.c`** — added
only in 7.7 (see the superset list above). Neither is covered by this document.

---

## 8. Calling External Functions / DLLs with extern

### Declaring with extern

CCODE can call external functions via the `extern` keyword. CE's symbol resolver tries to find the function
address in the target process's symbol table.

```c
// the calling convention must be stated explicitly
extern __cdecl int sprintf(char *, char *, ...);
extern __stdcall int MessageBoxA(void *, char *, char *, int);
```

### Symbol resolution flow

When TCC hits an undefined symbol it calls the callback CE installed. **The real compile has only two steps**
(tcclib.pas:1349-1367):

1. **The secondary reference list supplied by AA** (exact string comparison, `secondaryLookup.IndexOf(name)`).
   This list comes from the import names collected during the test-compile stage plus the linklist; addresses
   are filled in by AA beforehand
   (autoassembler.pas:3968 `dataForAACodePass2.cdata.references[i].address:=getAddressFromScript(...)` fills
   the address, autoassemblercode.pas:470-471 builds the list).
2. **`symhandler.GetAddressFromName(name, true, error)`** — when the target is CE itself, `selfsymhandler` is
   used instead (`symbolLookupFunctionSelf`, tcclib.pas:1370-1386).
   This step alone already covers: AA allocs/labels, registersymbol, user-defined symbols,
   **and exported functions of modules already loaded in the target process (this is where Windows API calls
   are resolved)**. When nothing is found it returns **0**
   (the 3-parameter overload at symbolhandler.pas:5111-5115, `if haserror then result:=0;`).

There is no third step. The 3/4/5 in older documentation were a misreading:

- "Windows API" is not a separate step; it is inside the symhandler in step 2.
- **`_symbolname@N` is post-compile reverse processing**, not a lookup fallback: on 32-bit CE takes the
  `_name@N` symbols TCC produced and **additionally registers an undecorated `name`**
  (autoassemblercode.pas:592-612 / :1429-1448) for convenient reference from the AA side.
- **The test compile uses a completely different callback** (`symbolLookupFunctionTestCompile`,
  tcclib.pas:1341-1347), which returns the fake address `0x00400000` for **any** name while recording the name
  in the import list — this is precisely how CE learns which external symbols need resolving.
  So **the test compile can never fail because a symbol was not found**; symbol problems only surface in Pass 2.
  The two paths each call `set_symbol_lookup_func` (test compile tcclib.pas:1582, real compile :1642, each
  overriding the default installed by `setupCompileEnvironment` at :1510/:1513) — this is not a fallback chain.

> ⚠ If AA fails to resolve a reference before Pass 2, it only does
> `OutputDebugString('Failure getting reference for '+...)` and **continues** (autoassembler.pas:3969-3973);
> the entry enters the secondary list with address 0, and the secondary list is consulted **before** the
> symhandler.

### Accessing AA-defined symbols

```
alloc(myBuffer, 256)

{$C}
extern unsigned long long myBuffer;
{$ASM}

{$CCODE result=RAX}
  unsigned long long *buf = (unsigned long long *)&myBuffer;
  // use buf...
{$ASM}
```

**Important**: the CE wiki mentions that `extern` can be used to access AA variables and other symbols.

### Practical patterns for calling DLL functions

```c
// Method 1: if the target process has already loaded the DLL, extern it directly
extern __stdcall void *GetModuleHandleA(char *);
extern __stdcall void *GetProcAddress(void *, char *);

// Method 2: manual LoadLibrary + GetProcAddress
typedef int (__stdcall *MSGBOX)(void*, char*, char*, int);
void *user32 = GetModuleHandleA("user32.dll");
MSGBOX msgbox = (MSGBOX)GetProcAddress(user32, "MessageBoxA");
msgbox(0, "Hello", "CCODE", 0);
```

---

## 9. Symbol Interop — the Bridge Between CCODE and AA Script

### CCODE → AA

Every public function / global variable defined in CCODE automatically becomes an AA label.
**After Pass 1 these symbols immediately become undefined AA labels (`afterccode=true`)**
(autoassembler.pas:1887 calls `AutoAssemblerCodePass1`, and :1891-1904 creates the labels right after, with
`labels[j].afterccode:=true;` at :1900), while **the addresses are not filled in until Pass 2 completes**
(:4002 `if labels[j].afterccode then` … :4009-4010 `labels[j].address:=...; labels[j].defined:=true;`).
They can be referenced by later AA instructions.

```
{$C}
int my_global_counter = 0;

int get_counter() {
  return my_global_counter;
}
{$ASM}

// later AA instructions can use these symbols
mov rax, my_global_counter
call get_counter
```

**The PREFIX option** (its actual behaviour is not what intuition suggests — read carefully)

```
{$C PREFIX=mod1}
int value = 42;
{$ASM}

// both names get registered in CE's global symbol table:
mov rax,[mod1.value]
mov rax,[value]        // ← the original name is still there too (but referencing it inside the script is unreliable — see below)
```

> ⚠ **PREFIX does not rename anything, it only adds an alias — so it does not prevent name collisions.**
> CE calls `AddSymbol` twice for every C symbol (autoassemblercode.pas:645-651): once for `prefix.name` and
> once for the original `name` (the latter with `skipaddresstostringlookup=true` as the 5th parameter, see
> SymbolListHandler.pas:151 — that only excludes it from address→name reverse lookup; name→address still works).
>
> ⚠ **But referencing the original name inside the same script is unreliable.** AA labels inside a script do
> not come from the symbol table; they are built from `cdata.symbols` (autoassembler.pas:1900 marks
> `afterccode:=true`), and Pass 1 fills that array with a CE off-by-shift bug:
> autoassemblercode.pas:1455 reads `dataforpass2.cdata.symbols[i*2].name:=symbols[i shr 1];` (it should be
> `symbols[i]`).
> The result is that the un-prefixed half becomes symbols[0], symbols[0], symbols[1], symbols[1], … —
> **the first half is duplicated and the second half is lost entirely**.
> The prefixed half (:1457) is correct.
> **When PREFIX is in use, always reference `prefix.name` inside the script.**
>
> ⚠ **`PREFIX=` is a whole-script setting, not a per-block setting.**
> `symbolPrefix` is a single string field on `cdata` (autoassemblercode.pas:708-709
> `if copy(us,1,7)='PREFIX=' then DataForPass2.cdata.symbolPrefix:=copy(params[i],8);`), and every
> `{$C ...}` / `{$CCODE ...}` line overwrites it — **the last PREFIX to appear wins**.
> Writing `{$C PREFIX=mod1}` and `{$C PREFIX=mod2}` in one script does not give you two namespaces.
> Given that all C blocks are one compilation unit anyway (see §6), the real way to avoid collisions is to
> give the C identifiers different names.

### AA → CCODE

Use the `extern` keyword to declare allocs / labels defined in the AA script:

```
alloc(sharedData, 64)

{$C}
extern unsigned char sharedData[];
{$ASM}

{$CCODE}
  sharedData[0] = 0xFF;
  sharedData[1] = 0x00;
{$ASM}
```

---

## 10. XMM Register Access in Detail

### The xmmreg struct definition

The exact text CE inserts, line by line (autoassemblercode.pas:1305-1320, inserted as lines 0~15 of cscript):

```c
typedef struct {
  union{
    struct{
        float f0;
        float f1;
        float f2;
        float f3;
    };
    struct{
        double d0;
        double d1;
    };
    float fa[4];
    double da[2];
  };
} xmmreg, *pxmmreg;
```

> This typedef is inserted **only when at least one parameter binds a WHOLE XMM register (the `var=XMM0`
> form)** (`usesXMMType:=true` is set only in the contextitem 32..47 / 32..39 branches:
> autoassemblercode.pas:811, :832, :869, :894), and it goes at the very top of the entire C compilation unit
> (lines 0~15). Using only element accessors like `XMM0.0` / `XMM0.0D` does **not** produce `xmmreg` — those
> two are plain `float` / `double`.
> Because it is inserted at the top, defining your own `xmmreg` in a `{$C}` block will always collide.

### Access forms

| Syntax | C type | Meaning | Example |
|------|--------|------|------|
| `var=XMM0` | `xmmreg` | The whole 128-bit register | `var.f0`, `var.d1`, `var.fa[2]` |
| `var=XMM0.0` | `float` | Float 0 (bits 0-31) | use `var` directly |
| `var=XMM0.1` | `float` | Float 1 (bits 32-63) | use `var` directly |
| `var=XMM0.2` | `float` | Float 2 (bits 64-95) | use `var` directly |
| `var=XMM0.3` | `float` | Float 3 (bits 96-127) | use `var` directly |
| `var=XMM0.0D` | `double` | Double 0 (bits 0-63) | use `var` directly |
| `var=XMM0.1D` | `double` | Double 1 (bits 64-127) | use `var` directly |

### Example

```
{$CCODE position=XMM0 speed=XMM1.0}
  // position is an xmmreg struct
  position.f0 *= 2.0f;   // X coordinate * 2
  position.f1 += 10.0f;  // Y coordinate + 10

  // speed is a float
  if (speed > 100.0f) speed = 100.0f;
{$ASM}
```

---

## 11. Block Options

Special options can be added on the `{$CCODE ...}` or `{$C ...}` line:

| Option | Meaning |
|------|------|
| `KERNEL` / `KALLOC` / `KERNELMODE` | **Only** the function-address pointer (`..._address`) switches to `kalloc()`; requires the DBK driver |
| `NODEBUG` | Do not generate STAB debug information (no `-g`, so the compiled output is smaller) |
| `PREFIX=name` | Register an **additional** `name.symbol` alias for every C symbol (the original name is kept as well, see §9) |

**These three are all of the block options there are** (autoassemblercode.pas:701-709), and **all of them are
shared across the whole script** (see §6).

> ⚠ **`KERNEL`'s scope is far smaller than the name suggests.** All it does is make the 8/4-byte
> `ceinternal_autofree_cfunction_at_lineN_address` pointer use `kalloc()`
> (the `ifthen(DataForPass2.cdata.kernelAlloc,'k','')` at autoassemblercode.pas:912 / :914).
> The compiled C code itself (`ceinternal_autofree_ccode`) and the 512-byte safecall stub are **both still
> ordinary `alloc()`** (:1481 / :298 have no `'k'` prefix).
> Only when Pass 2 runs out of space and takes the reallocation path does the code buffer switch to
> `KernelAlloc()` (:489-490).
> The three keywords (`KALLOC` / `KERNELMODE` / `KERNEL`) are exactly equivalent (:701-702).

**Example:**
```
{$C PREFIX=health}
int health_backup = 0;      // → registers both health.health_backup and health_backup
{$ASM}

{$CCODE NODEBUG health_val=RBX}
  health_val = 9999;
{$ASM}
```

> ⚠ **Do not put `PREFIX=` on the `{$CCODE}` line (including the older documentation's
> `{$CCODE NODEBUG PREFIX=health health_val=RBX}` example) — it injects an extra bogus variable bound to RAX.**
> The same string on a `{$CCODE ...}` line is consumed by **two** parsers:
> `ParseCBlockSpecificParameters` (block options, autoassemblercode.pas:770) and
> `parseLuaCodeParameters` (parameter mapping, :1290).
> To the latter, `NODEBUG` has no `=` so it is dropped (:201-202), but `PREFIX=health` splits into exactly
> 2 pieces → varname=`PREFIX`, regname=`HEALTH`, which is not in the lookup table and does not start with
> `XMM`, so contextitem stays at the **0 = RAX** that `FillByte` cleared it to (:197 / :212), and it is added
> to the parameter list all the same (:285-286).
> The generated C then contains an extra
> `unsigned long long PREFIX=*(unsigned long long *)*(unsigned long long *)((unsigned long long)parameters+0x228);`
> plus a corresponding **write-back to RAX** (:862).
> Any block option written as `KEY=value` does this; right now `PREFIX=` is the only one of that form.
> **The fix: put `PREFIX=` in its own separate `{$C PREFIX=name}` block** (it is a whole-script setting anyway).

---

## 12. LUACODE Comparison

CE also provides a `{$LUACODE}` block, which uses the same SafeCall Stub and the same parameter **syntax** as
CCODE (both share `parseLuaCodeParameters`, autoassemblercode.pas:1249 and :1290), but executes Lua instead of
C — and the **parameter mapping differs in two places** (see below):

```
{$LUACODE health=RBX}
  if health < 100 then
    health = 9999
  end
{$ASM}
```

### LUACODE's prerequisites and limits (CCODE has none of these)

- **Windows only**: the entire `{$LUACODE}` branch in the CE source is wrapped in `{$ifdef windows}`
  (autoassemblercode.pas:1241 … :1278); the macOS/Linux builds of CE do not process the block at all.
- **Cannot be used on CE itself**: with `targetself`, or when the target is CE itself, it raises
  `'{$LUACODE} blocks can not be used inside CE'` (:1244-1245). (`{$CCODE}` has no such restriction.)
- **It rewrites your script automatically**: CE inserts `loadlibrary(luaclient-x86_64.dll)` at the top of the
  script (`luaclient-i386.dll` on 32-bit, :1259-1262), plus a `CELUA_ServerName:` label and a pipe-name string
  (:1268-1269), and additionally allocates `alloc(ceinternal_autofree_luacallstub_at<line number>,64)` (:1100).
  At run time it calls back into the Lua VM inside the CE process via `CELUA_ExecuteFunctionByReference`
  (:1116), across a pipe — which is why it is slow.
- **⚠ An early `return` inside your Lua block skips ALL register write-back.**
  The shape CE generates is
  `return createRef(function(parameters) <reads> <your code> <write-back> return end )`
  (:991 for the opening, :1079 for the close), and the write-back code comes **after** your code (:1054 / :1074).
  Returning early means nothing was changed.

### Parameter-mapping differences between CCODE and LUACODE

1. **contextitem 16 (`RAXF`/`EAXF`)**: LUACODE does the correct indirect access
   `readFloat(readPointer(parameters+0x228))` (:1004) / `writeFloat(readPointer(parameters+0x228),v)` (:1045);
   CCODE does a direct `*(float*)(parameters+0x228)` (:807 / :865) — **only CCODE is broken** (see the warning
   in §3).
2. **Whole XMM (contextitem 32~47)**: CCODE gives you an `xmmreg` struct (:812);
   LUACODE gives you a **table of 16 bytes** returned by `readBytes(addr,16,true)` (:1006), with write-back via
   `writeBytes(addr,tbl)` (:1047).

For everything else (0, 1~15, 17~31, 48~111, 112~143) the offset formulas are identical on **64-bit**;
32-bit has one further ESP offset that is wrong only on the Lua side (see the warning at the end of this section).
The `RBPF`/`RSPF` slot mix-up (§3) exists on **both sides**, because the Lua side uses the same
`0x200+(contextitem-17)*8`.

### CCODE vs LUACODE

| Aspect | CCODE | LUACODE |
|------|-------|---------|
| Compiler | TCC (c99'ish; for coverage see §13) | CE's built-in Lua |
| Execution speed | native machine code, very fast | Lua interpreter, slower |
| Parameter **syntax** | identical (shared `parseLuaCodeParameters`) | identical |
| Parameter **mapping** | contextitem 16 broken; whole XMM gives `xmmreg` | contextitem 16 correct; whole XMM gives a 16-byte table |
| SafeCall Stub | identical | identical |
| Available CE functions | limited (needs extern) | complete (readInteger etc.) |
| Debug support | STAB debugging | none |
| Best suited to | high-performance computation, heavy memory work | anything that needs the CE API |

### LUACODE parameter access

LUACODE uses CE Lua's memory read/write functions:

```lua
-- read
local rbx = readPointer(parameters + 0x200)           -- RBX
local rax = readPointer(readPointer(parameters + 0x228)) -- RAX (indirect)
local xmm0_f0 = readFloat(parameters + 0x0A0)          -- XMM0.0

-- write back
writePointer(parameters + 0x200, rbx)                  -- RBX
writePointer(readPointer(parameters + 0x228), rax)     -- RAX
writeFloat(parameters + 0x0A0, xmm0_f0)                -- XMM0.0
```

> ⚠ **Reading `ESP` from 32-bit LUACODE returns garbage.** What CE generates is
> `readPointer(parameters+0x228+12)`, but on 32-bit the original-ESP pointer slot is at **0x214** (the
> multiplier should be `*4`; autoassemblercode.pas:1023 mistakenly uses `*8` on that line — the adjacent
> `1..5,7` branch at :1022 is a correct `*4`, and the C side's 32-bit counterpart at :827 is `*4` too).
> To read ESP on a 32-bit target, do `readPointer(parameters+0x214)+12` yourself.
>
> ⚠ **Reading `RSP` from 64-bit LUACODE returns garbage too.** The multiplier (`*8`) is right, but :1003 puts
> the `+24` **inside** the `readPointer()` parentheses — producing `readPointer(parameters+0x228+24)`, which
> reads `parameters+0x240`. Working the slot formula `0x200+(contextitem-1)*8 = 0x240` backwards gives
> contextitem = 9, i.e. **the R9 slot**, not the original RSP pointer. The C side at :806 has the correct
> shape (`*(unsigned long long*)(parameters+0x228)` — **dereference first, then `+24`**).
> To read RSP from 64-bit LUACODE, likewise do `readPointer(parameters+0x228)+24` yourself.

---

## 13. Limits and Caveats

### Stack management

- ✅ **No manual stack management needed**: the SafeCall Stub handles alignment and save/restore
- ⚠️ **RSP cannot be modified freely**: RSP is never written back (even when declared as a parameter)
- ✅ **Other functions can be called safely**: the stack is already 16-byte aligned

### Memory

- The compiled C code lives in the `ceinternal_autofree_ccode` allocation
- Initial size = `max(32, align(estimatedSize * 2, 16))` (autoassemblercode.pas:1389)
- If that is not enough, a bare `VirtualAllocEx` of 4× the size is used — **that block is not in AA's alloc list
  and is not freed on disable** (see §6)
- The `KERNEL` option only affects the function-address pointer and the reallocation path above (see §11)

### Compilation limits

- **No standard library linkage** — CE always compiles with `-nostdlib` (tcclib.pas:1484-1487, on both branches).
  Headers (`string.h`, `windows.h`…) provide declarations only; the implementation must already be loaded in
  the target process (see §7).
- Windows APIs require the corresponding DLL to be loaded in the target process
- C++ syntax is not supported
- **C11 coverage is incomplete** — 7.7 ships C11 headers (`stdalign.h`, `stdatomic.h`, `stdnoreturn.h`,
  `tgmath.h`), but that only means the headers are present, not that there is full C11/C17 language and library
  support.
  The introducing commit calls it a "c(c99'ish)-compiler"; **the exact C11/C17 coverage cannot be proven from
  CE's Pascal side** (TCC itself is a vendored, modified copy), so test it yourself when it matters.

### Functions and symbols

- A CCODE C function's signature is always `void func(void *parameters)`
- There is no direct return value (results go back through register write-back)
- Symbols defined in CCODE become undefined AA labels **after Pass 1** (`afterccode=true`), with addresses
  filled in during Pass 2 (see §9)
- Only C symbols beginning with **`ceinternal_autofree_cfunction`** (that is, the wrapper function each
  `{$CCODE}` generates automatically) are skipped when symbols are returned to CE's symbol table
  (autoassemblercode.pas:642-643 — a narrower prefix than the older documentation claimed).
  The other `ceinternal_autofree_*` names (`_safecallstub_for_*`, `_ccode`, `_luacallstub_at*`) are **AA
  allocs/labels rather than C symbols** in the first place, so they never pass through that filter; AA handles
  them separately — such allocs are moved to the end of the alloc list (autoassembler.pas:3308-3320) and freed
  automatically on disable (:4211-4212).

### Performance considerations

- The SafeCall Stub includes `fxsave`/`fxrstor` (roughly 200-400 cycles)
- Suitable for use in an injection hook (executed once per frame / per call)
- Not suitable inside a tight loop

### Common traps

1. **Forgetting the `{$ASM}` terminator**: a CCODE block must end with `{$ASM}`
2. **Omitting the calling convention on extern**: `__stdcall` must be specified when calling Windows APIs
3. **Pointer size**: `unsigned long` on 32-bit, `unsigned long long` on 64-bit
4. **RAX's indirect access**: RAX needs two dereferences, unlike every other register
5. **XMM needs the xmmreg typedef**: CE generates it automatically for whole-XMM use, but watch for collisions
   with your own structs

The following four are **defects in CE itself**, not usage mistakes — take them literally and they break, so all
you can do is avoid them (details in §3 / §11):

6. **Comma-separated parameters**: `{$CCODE a=RAX,b=RBX}` **silently drops every parameter**; only spaces work
7. **A misspelled register name**: no error; it silently binds to **RAX** and writes back to RAX at the end
8. **`RAXF` / `EAXF` / `RBPF` / `EBPF`**: the float aliases compute the wrong slot and overwrite the pointer the
   stub uses to restore RSP/ESP → **crash**; **`RSPF` / `ESPF`** land on the RBP/EBP slot and are written back
   anyway → **RBP/EBP corrupted** (for where each actually lands, see §3)
9. **`PREFIX=` on the `{$CCODE}` line**: injects an extra bogus variable named `PREFIX` bound to RAX — put it in
   a separate `{$C PREFIX=name}` block instead

---

## 14. Practical Examples

### Example 1: health modification (basic usage)

```
alloc(newmem, 256)

newmem:
{$CCODE health=RBX playerFlag=RCX}
  int isPlayer = *(int *)((unsigned long long)playerFlag + 0xB8);
  if (isPlayer)
    health = 100000;
{$ASM}
  jmp returnhere

targetAddress:
  jmp newmem
returnhere:
```

### Example 2: using a helper function ({$C} + {$CCODE})

```
{$C}
typedef struct {
  float x, y, z;
} Vec3;

float clamp(float val, float min, float max) {
  if (val < min) return min;
  if (val > max) return max;
  return val;
}
{$ASM}

alloc(hook, 256)

hook:
{$CCODE pos=RCX}
  Vec3 *v = (Vec3 *)(pos + 0x80);
  v->y = clamp(v->y, 0.0f, 1000.0f);
{$ASM}
  jmp returnhere
```

### Example 3: using sprintf to log debug information

```
alloc(debugbuf, 512)

{$C}
extern __cdecl int sprintf(char *, char *, ...);
extern unsigned char debugbuf[];
{$ASM}

{$CCODE value=RAX addr=RCX}
  sprintf((char *)debugbuf, "RAX=0x%llX, RCX=0x%llX", value, addr);
{$ASM}
```

> ⚠ Whether this example links depends on **whether the target process has loaded a module that exports
> `sprintf`** (usually `msvcrt.dll`). CE always compiles with `-nostdlib` and will not link any libc for you
> (see §7).

### Example 4: XMM floating-point arithmetic

```
{$CCODE damage=XMM0.0 multiplier=XMM1.0}
  if (damage > 0.0f) {
    damage *= multiplier;
    if (damage > 99999.0f)
      damage = 99999.0f;
  }
{$ASM}
```

### Example 5: conditional register modification

```
{$CCODE rax=RAX rbx=RBX rcx=RCX}
  // check whether RCX is a valid pointer
  if (rcx > 0x10000 && rcx < 0x7FFFFFFFFFFF) {
    unsigned long long *ptr = (unsigned long long *)rcx;
    if (ptr[0] == 0xDEADBEEF) {
      rax = ptr[1];  // read a value out of the structure
      rbx = 1;       // mark as found
    }
  }
{$ASM}
```

---

## 15. Internal Naming Conventions

The symbols CE generates automatically for CCODE follow these naming rules:

| Name format | Meaning |
|---------|------|
| `ceinternal_autofree_cfunction_at_line[N]` | The C function itself |
| `ceinternal_autofree_cfunction_at_line[N]_address` | The function-address pointer (an 8/4-byte alloc) |
| `ceinternal_autofree_safecallstub_for_ceinternal_autofree_cfunction_at_line[N]_address` | The SafeCall Stub |
| `ceinternal_autofree_ccode` | The memory block holding the compiled C code |
| `ceinternal_autofree_luacallstub_at[N]` | LUACODE's stub |

`[N]` is the line number in the AA script (`ptruint(script.objects[scriptstart])`,
autoassemblercode.pas:793).

The only things filtered out and not exported to the user symbol list are **C symbols** beginning with
**`ceinternal_autofree_cfunction`** (autoassemblercode.pas:642-643). The remaining `ceinternal_autofree_*`
names in the table above are **AA allocs/labels** and take a different path: AA moves them to the end of the
alloc list (autoassembler.pas:3308-3320) and frees them automatically on disable (:4211-4212). See §13.

---

## 16. Debug Support

### STAB debug information

CE extracts STAB (Symbol TABle) format debug information from TCC's compilation output, containing:

- Line-number mapping (C source line → machine code address)
- Local variable information
- Function boundaries (lexical blocks)
- Stack frame information

### How to use it

1. Make sure the `NODEBUG` option is not used
2. Set a breakpoint inside the CCODE function in CE's Memory Viewer
3. When the breakpoint hits, CE displays the C source
4. You can single-step the C code and inspect variable values

### Debugging caveats

- `NODEBUG` makes CE omit `-g` (tcclib.pas:1491-1492 `if nodebug=false then params:='-g '+params;`), and the
  compiled output is smaller.
  **The exact ratio is not defined anywhere in the CE source; do not quote a fixed percentage.**
- Worth knowing: **the size-probing stage never passes `-g`; only the real compile does**
  (`nodebug` is passed in as `sourcecodeinfo=nil`, tcclib.pas:1579 / :1630;
  autoassemblercode.pas:1347 is the only call site of `testcompileScript` and it passes `nil`).
  In other words, the size measured in Pass 1 and the size actually produced in Pass 2 are generated under
  **different compile options** — which is one of the reasons the ×2 headroom in `max(32, align(size*2,16))`
  and the 4× retry path exist (the CE source does not quantify the difference).
- Optimised code may not map back to line numbers exactly

---

## Appendix A: TCC Compiler Information

### Supported platforms

**Naming rule**: `tcc<CE's own bitness>-<target bitness>[-<OS/architecture>].dll`
(compare tcclib.pas:1233, where 32-bit CE loads `tcc32-32.dll` "//generates 32-bit code", and :1240-1243, where
64-bit CE loads `tcc64-32.dll` / `tcc64-64.dll` / `tcc64-*-linux.dll` depending on the target).

The 7.7 Windows install actually ships **12** of them (**derived — regenerate with
`ls "C:\Program Files\Cheat Engine\" | grep -i "^tcc"`, do not hand-edit**):

| DLL file | CE architecture | Target architecture |
|---------|---------|---------|
| `tcc32-32.dll` | 32-bit | 32-bit Windows |
| `tcc32-64.dll` | 32-bit | 64-bit Windows |
| `tcc64-32.dll` | 64-bit | 32-bit Windows |
| `tcc64-64.dll` | 64-bit | 64-bit Windows |
| `tcc32-32-linux.dll` | 32-bit | 32-bit Linux |
| `tcc32-64-linux.dll` | 32-bit | 64-bit Linux |
| `tcc64-32-linux.dll` | 64-bit | 32-bit Linux |
| `tcc64-64-linux.dll` | 64-bit | 64-bit Linux |
| `tcc32-arm-linux.dll` | 32-bit | ARM Linux |
| `tcc32-arm64-linux.dll` | 32-bit | ARM64 Linux |
| `tcc64-arm-linux.dll` | 64-bit | ARM Linux |
| `tcc64-arm64-linux.dll` | 64-bit | ARM64 Linux |

`libtcc_x86_64.dylib` / `libtcc_arm64.dylib` are the **macOS** filenames (tcclib.pas:1256 / :1270), and are
(correctly) absent from a Windows install.

> Note: the loading logic in the 7.5 source (tcclib.pas:1233, :1240-1243) names only
> `tcc32-32.dll` / `tcc64-32.dll` / `tcc64-64.dll` / `tcc64-32-linux.dll` / `tcc64-64-linux.dll`;
> the install directory contains more files than the source names. Treat the **install directory** as
> authoritative and regenerate the list with the command above.

### Compile options

For how they are actually assembled, see tcclib.pas:1484-1502:

```
-nostdlib                            // always present (no library is linked)
-g                                   // added only when NODEBUG is not specified (generates STAB)
-Wl,-section-alignment=1000          // added only when the system does not support writable-and-executable memory
                                     // (on non-Windows this is the hexadecimal value of getPageSize)
-D ANDROID                           // when the target is Android
-D __APPLE__                         // when CE was built on darwin
```

Include paths (a fixed 6, mounted in this order, tcclib.pas:1465-1474):

```
include/                 include\winapi/                 include\sys/
<CE exe directory>\include/   <CE exe directory>\include\winapi/   <CE exe directory>\include\sys/
```

Plus any user-added paths in `additonalIncludePaths` (:1477-1479).
(The two `include\winapi` entries are only mounted on Windows builds, so non-Windows has 4.)

### Output format

`TCC_OUTPUT_MEMORY` (= 1) — compile straight to memory, producing no file (tcclib.pas:1505).

---

## Appendix B: Source-Code Reference

> **The line counts are derived** (measured at CE tag `7.5-195` / HEAD `4178e037`):
> regenerate with `wc -l "<CE source>/Cheat Engine/<filename>"`, do not hand-edit.

| File | Description | Key line numbers |
|------|------|---------|
| `autoassemblercode.pas` | Main CCODE/LUACODE implementation (**1494 lines** total) | Pass1: 745-937, Pass2: 406-688, Stub: 290-402 |
| `tcclib.pas` | TCC compiler wrapper | **1863 lines** total; highlights: `setupCompileEnvironment` **1460-1514** (compile options + include paths + symbol callback), `symbolLookupFunction` **1349-1367**, `symbolLookupFunctionTestCompile` **1341-1347**, `testcompileScript` **1561-1602**, `compileScript` **1604-1680**, `parseStabData` **1516-1559**, DLL loading **1219-1290** |
| `autoassembler.pas` | The main AA engine (integrates CCODE; **4685 lines** total) | registerSymbol: 1971-1996, aobscanmodule: 1141-1212 |
| `Assemblerunit.pas` | The assembler — **rewrites a relative jmp/call as a far jump when it exceeds ±2GB** (`jmp/call [2]` / `jmp +8` / `DQ address`; **8737 lines** total) | **5409-5422** |
| `symbolhandler.pas` | Symbol resolution (including math expressions / pointer arithmetic; **6904 lines** total) | The main body `TSymhandler.getAddressFromName(name, waitforsymbols, out haserror, context, shallow)` starts at **5122**; CCODE goes through the 3-parameter overload at **5111-5115** (**returns 0 when nothing is found**) |

(The `Assemblerunit.pas` row was labelled "RIP-relative computation" in older documentation; 5409-5422 is
actually the far-jump rewrite. If you are looking for the displacement computation of RIP-relative memory
operands, it is not in that range and has to be located separately.)

---

*Last updated: 2026-08-07*
*Produced from an analysis of the Cheat Engine source code (tag 7.5-195 / HEAD 4178e037) and the CE 7.7.0.10568 install*
