# [Bug Report] CE Plugin SDK — Type 7 specialstringpointer is ignored
**Ref. CE Source:** 7.5.2 (GitHub latest)
**Tested CE Version:** 7.6 public

Dev environment: making a Native C++ plugin using **Plugin SDK v6, Type 7 (Disassembler Line Renderer)**.

### 1. The specialstringpointer is broken
Even change the `specialstringpointer` in Type 7 callback, CE never render comments in the Comment column.

**Root Cause:**
In `disassemblerviewlinesunit.pas`, the logic order is wrong:
1. Line 451: CE builds `specialstring` (Delphi string) with own comments.
2. **Line 496: CE copies `specialstring` to `specialstrings` (TStringList).** <-- PROBLEM!
3. Line 946–952: CE gives `pspecialstring` pointer to our Type 7 plugin.
4. Line 995–1001: CE draws the Comment column from the **TStringList** (already copied in Step 2).

So, when plugin modifies the string in Step 3, CE already finished the copy in Step 2. It just draws the old data. Other strings like `opcodestring` or `addressString` are OK because they are drawn directly from pointers.

**Code Reference:**
```delphi
// disassemblerviewlinesunit.pas

// Step 2: TStringList is filled BEFORE plugins run
specialstrings.text := specialstring;  // line 496

// Step 3: Call plugin (Too late! Already copied)
pspecialstring := @specialstring[1];   // line 946
handledisassemblerplugins(..., @pspecialstring, ...); 

// Step 4: CE draws from TStringList, NOT from our modified pointer
for i := 0 to specialstrings.Count-1 do
  fcanvas.TextRect(..., specialstrings[i]); 
```

**Suggested Fix:**
Move the `specialstrings.text` assignment to after the plugin callback:

handledisassemblerplugins(@paddressString, @pbytestring, @popcodestring, @pspecialstring, @textcolor);

// Re-read specialstring from modified pointer
if pspecialstring <> nil then
  specialstrings.text := pspecialstring
else
  specialstrings.text := '';

---

### 2. { Character in DrawTextRectWithColor
In line 1075, `DrawTextRectWithColor` treats `{` as a special color control (like `{H}`, `{R}`, `{CRRGGBB}`). 
If plugin output has `{` in opcode/address/bytes, the rendering will become very messy with random color blocks. This is not in the SDK doc.

**Workaround: (in testing)** Just append Type 7 output to `opcodestringpointer` and don't use `{` characters.

---

### 3. CEValueType Enum Mismatch
**Ref. CE Source:** 7.5.2 (GitHub latest)
**Tested CE Version:** 7.6 public

CE's internal `TVariableType` in `commontypedefs.pas` is different from `cepluginsdk.h`.

**Internal Delphi order:**
`vtByte=0, vtWord=1, vtDword=2, vtQword=3, vtSingle=4, vtDouble=5`

**SDK Header order:**
It says index 3 is Float, 4 is Double, 6 is Int64. This is wrong.
When Type 0 (Address List) callback gives the raw value, plugin will misidentify types (e.g. think it's a Float but actually it's a Qword).

**Workaround:**
Don't follow the SDK header. Use CE internal order: 
`vtQword=3, vtSingle=4, vtDouble=5`.

### 4. r/m16, imm8 sign-extension bug for value 0x80–0xFF

## Problem
When we use instructions like `cmp bx, AA`, the assembler is wrong. It use `r/m16, imm8` (opcode `83`) encoding. The CPU will do sign-extend for `0xAA` and it become `0xFFAA` (-86), but user actually want `0x00AA` (170).

### Reproduction:
```asm
cmp bx, AA    ; user want: compare BX with 170 (0x00AA)
```

### Actual (Wrong):

```
66 83 FB AA        cmp bx, FFAA    ; sign-extended 0xAA -> 0xFFAA = -86
```

### Expected (Correct):

```
66 81 FB AA 00     cmp bx, 00AA    ; use 16-bit immediate = 170
```

Now, if we write `cmp bx, 00AA` (add zero in front), it is OK because `StringValueToType` will see length and use `vtype=16`. But we think just `AA` should also work correctly.

---

## Root Cause

In `Assemblerunit.pas` line 6845, the `r/m16, par_imm8` handler only check `vtype` to decide upgrade to `imm16` or not:

```pascal
if vtype=16 then    // <- only check string length type
```

For the value `AA`:
* `ConvertHexStrToRealStr("AA")` -> `"$AA"`
* `StringValueToType("$AA")` -> length is 3 -> **vtype=8**
* `SignedValueToType(170)` -> 170 > 127 -> **signedvtype=16**

Because `vtype=8` (not 16), the assembler skip the upgrade. Then it send `byte(0xAA)` as `imm8`, so CPU do sign-extend to `0xFFAA`.

I check 32-bit code (`r/m32, par_imm8` at line 6982), it is already correct:

```pascal
if (vtype>8) or (opcodes[j].signed and (signedvtype>8)) then    // <- this is correct
```

So 16-bit path just forgot to check the `signed` flag.

---

## Fix

**Change line 6845**. Just add the `signed` and `signedvtype` check like 32-bit path:

```pascal
// Old code:
if vtype=16 then

// New code:
if (vtype=16) or (opcodes[j].signed and (signedvtype>8)) then
```

This means we will upgrade `imm8` to `imm16` when:

1. User write 16-bit string (like `00AA`).
2. **OR** Opcode has `signed: true` AND the value is bigger than signed-byte range (> 127 or < -128).

---

## Affected Instructions

All `r/m16, imm8` with `signed: true` have this problem (the ALU group):

| Mnemonic | Opcode Line | Encoding |
| --- | --- | --- |
| ADD | 182 | 66 83 /0 |
| ADC | 166 | 66 83 /2 |
| AND | 213 | 66 83 /4 |
| CMP | 351 | 66 83 /7 |
| OR | 1027 | 66 83 /1 |
| SBB | 1576 | 66 83 /3 |
| SUB | 1703 | 66 83 /5 |
| XOR | 2658 | 66 83 /6 |

These all have same bug: if immediate value is 0x80–0xFF, it will become 0xFF80–0xFFFF in 16-bit register.

---

## Verification

I tested these cases, now they are all correct:

| Input | Before (Wrong) | After (Correct) |
| --- | --- | --- |
| `cmp bx, AA` | `66 83 FB AA` (FFAA) | `66 81 FB AA 00` (00AA) |
| `cmp bx, 80` | `66 83 FB 80` (FF80) | `66 81 FB 80 00` (0080) |
| `cmp bx, FF` | `66 83 FB FF` (FFFF) | `66 81 FB FF 00` (00FF) |
| `cmp bx, 7F` | `66 83 FB 7F` (007F) | `66 83 FB 7F` (No change, safe) |
| `cmp bx, 05` | `66 83 FB 05` (0005) | `66 83 FB 05` (No change, safe) |
| `add bx, C0` | `66 83 C3 C0` (FFC0) | `66 81 C3 C0 00` (00C0) |
