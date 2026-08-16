# CE Bugs — Minesweeper

CE-specific quirks and undocumented behaviours hit while building against Cheat Engine.

> **Version coordinates (re-verified 2026-08-07 — every line number below was re-grepped, not trusted)**
>
> | What | Value |
> |------|-------|
> | CE **source tree** read | `D:\Github\cheat-engine`, tag **`7.5-195`**, HEAD `4178e037` |
> | Last **public** upstream commit | `upstream/master` `ec45d5f4` (2025-04-19) — the tree is level with it |
> | CE **binaries** installed | `C:\Program Files\Cheat Engine\`, **7.7.0.10568** (ProductVersion 7.7) |
> | Plugin SDK version | `CESDK_VERSION 6` (`cepluginsdk.h:16`) — unchanged in 7.7 |
>
> Two things this split means, and they are not the same claim:
> - **The public source is 7.5-era; the shipping binary is 7.7.** CE's GitHub repo lags its
>   releases, so "present in the source" does **not** prove "present in 7.7", and "absent from the
>   source" does not prove it was fixed. Each item below says which one it was checked against.
> - HEAD is **1 commit ahead of upstream** — the local `Assemblerunit.pas` fix in §4. It is not in
>   the public source. **Do not conclude from that it is missing from released CE**: §4 was then
>   measured in 7.7 and the shipping binary already behaves correctly. That inference was made in
>   an earlier revision of this file and the test disproved it, which is the cheapest possible
>   reminder that these two coordinates are separate evidence, not one fact stated twice.
>
> ⚠ Line numbers here are DERIVED from an external tree and `tools/check_derived_counts.py`
> cannot guard them (it only derives from this repo). Re-grep the identifier before trusting one.
> Deeper background on the plugin SDK: [ce-plugin-api-reference.md](ce-plugin-api-reference.md)
> and [ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md).

---

## 1. [Bug Report] CE Plugin SDK — Type 7 specialstringpointer is ignored

**Checked against:** source `7.5-195` — **all five line numbers below still land exactly.**
**Originally observed on:** CE 7.6 public.

Dev environment: making a Native C++ plugin using **Plugin SDK v6, Type 7 (Disassembler Line Renderer)**.

### The specialstringpointer is broken
Even change the `specialstringpointer` in Type 7 callback, CE never render comments in the Comment column.

**Root Cause:**
In `disassemblerviewlinesunit.pas`, the logic order is wrong:
1. Line 451: CE builds `specialstring` (Delphi string) with own comments.
2. **Line 496: CE copies `specialstring` to `specialstrings` (TStringList).** <-- PROBLEM!
3. Line 946–952: CE gives `pspecialstring` pointer to our Type 7 plugin.
4. Line 995–1001: CE draws the Comment column from the **TStringList** (already copied in Step 2).

So, when plugin modifies the string in Step 3, CE already finished the copy in Step 2. It just draws the old data. Other strings like `opcodestring` or `addressString` are OK because they are drawn directly from pointers.

> **Re-verified 2026-08-07 against `7.5-195`** — every step still holds at the stated line:
> `:451 specialstring:=d.DecodeLastParametersToString;` → `:496 specialstrings.text:=specialstring;`
> → `:946 pspecialstring:=@specialstring[1];` → `:952 handledisassemblerplugins(...)` (which forwards
> to the plugin handler via `:1266`/`:1270`) → `:995-1001` draws the Comment column from
> `specialstrings[i]`. The contrast is visible in the same procedure: the other three columns go
> through `DrawTextRectWithColor(..., paddressString / pbytestring / popcodestring)` at `:956`,
> `:960` and `:964` — straight from the pointers the plugin was handed, which is exactly why only
> the Comment column is dead. `specialstrings` is also what sizes the row (`:526-533`), so the
> height is fixed before the plugin runs too.

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

## 2. `{` Character in DrawTextRectWithColor

**Checked against:** source `7.5-195`. **Corrected:** the brace handling is not at 1075.

`DrawTextRectWithColor` treats `{` as a colour-control escape. If plugin output contains a `{` in
opcode/address/bytes, the rendering becomes very messy with random colour blocks. This is not in
the SDK doc.

`:1075` is only where the function is *declared* — the `'{':` case is at **`:1177`**, and the
control letters it accepts are wider than the three originally listed:

| After `{` | Effect | Line |
|-----------|--------|------|
| `N` | reset both foreground and background to normal | `:1200-1204` |
| `H` | hex colour (`colorcode:=1`) | `:1205` |
| `R` | register colour (`colorcode:=2`) | `:1206` |
| `S` | symbol colour (`colorcode:=3`) | `:1207` |
| `B` + `RRGGBB` | **background** RGB — consumes 6 chars | `:1208-1213` |
| `C` + `RRGGBB` | **foreground** RGB — consumes 6 chars | `:1214-1219` |
| `}` | end of the escape | `:1220-1224` |

Two details that make this worse than "one reserved character", both visible in that block:

- **An unknown letter is swallowed, not rejected.** The `else raise exception.create(rsInvalidDisassembly)`
  is **commented out** at `:1226`, so the parser simply keeps consuming until it finds a `}`.
  A stray `{` therefore eats the rest of your string looking for a terminator.
- **`B` and `C` blindly consume 6 characters** (`inc(i,6)` at `:1212`/`:1218`) whether or not they
  are valid hex — `trystrtoint` failing only means the colour is not applied, not that the six
  characters come back.

**Workaround:** append Type 7 output to `opcodestringpointer` and do not emit `{`. If you cannot
guarantee that, escaping is not available — strip or replace the character.

---

## 3. Value-type numbering: the SDK header comment is wrong (and 7.7 fixed it)

**Checked against:** source `7.5-195` **and** the installed 7.7 header — the two differ, which is
the whole point of this entry.
**Originally observed on:** CE 7.6 public.

> **Reframed 2026-08-07 — the original entry named an artifact that does not exist.** It said the
> "SDK Header order" was wrong, which reads as though `cepluginsdk.h` declares a value-type enum.
> **It does not.** `grep -n "enum" cepluginsdk.h` returns exactly two: `PluginType` (`:18`) and
> `AutoAssemblerPhase` (`:19`). The wrong numbering was a **`//` comment** on one struct field,
> and CE has since corrected it.

CE's internal `TVariableType` (`commontypedefs.pas:15`) is the real numbering. The SDK header never
declares it in C at all — a plugin author has to write that enum themselves, which is exactly how
the wrong numbers got copied around.

The artifact that carried the bad numbering is `PLUGINTYPE0_RECORD.valuetype`, and it changed
between the version this repo reads and the version installed here:

```diff
  // cepluginsdk.h:35
- // 7.5 source tree  — WRONG from index 3 onward
- char valuetype; //0=byte, 1=word, 2=dword, 3=float, 4=double, 5=bit, 6=int64, 7=string
+ // 7.7 installed   — corrected, and now agrees with TVariableType for 0-9
+ char valuetype; //0=byte, 1=word, 2=dword, 3=int64, 4=float, 5=double, 6=string, 7=widestring, 8=bytearray, 9=binary
```

That one line is the **only** difference between the two headers; `cepluginsdk.pas` is
byte-identical, so the plugin C ABI itself is unchanged between 7.5 and 7.7.

**The authoritative list** (`commontypedefs.pas:15`) has **17** members, not the 8 the old comment
implied:

```
vtByte=0, vtWord=1, vtDword=2, vtQword=3, vtSingle=4, vtDouble=5, vtString=6,
vtUnicodeString=7, vtByteArray=8, vtBinary=9, vtAll=10, vtAutoAssembler=11,
vtPointer=12, vtCustom=13, vtGrouped=14, vtByteArrays=15, vtCodePageString=16
```

**Why it bites.** `MainUnit.pas:9743` fills `valuetype` straight from the record's `VarType`, and
`:9767-9768` writes whatever you leave there **back into the cheat-table entry** when your Type 0
callback returns TRUE. Writing `3` meaning "float" silently retypes the row as an 8-byte **Qword**;
writing `5` meaning "bit" gives **Double**.

**Workaround:** ignore the header comment in either version and use `TVariableType`. In CE Lua the
same ordering applies — `mr.Type == 11` is an AA-script row, and 7.7's shipped `defines.lua`
confirms `vtQword=3` / `vtAutoAssembler=11` independently.

See [ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md) §10 and
[ce-plugin-api-reference.md](ce-plugin-api-reference.md) §11 for the full treatment.

---

## 4. r/m16, imm8 sign-extension bug for value 0x80–0xFF

> **Status (2026-08-07): FIXED in the shipping 7.7 binary. Still present in the last public source.**
>
> **Measured, not inferred.** `cmp bx, AA` typed into CE **7.7.0.10568**'s Auto Assemble produces:
> ```
> 7FF6F29B0000 - 66 81 FB AA00         - cmp bx,00AA
> ```
> That is the "Expected (Correct)" row of the verification table below — `81` opcode, 16-bit
> immediate, no sign extension. A released 7.7 does **not** have this bug.
>
> **The source says the opposite, and that is the whole reason this file now carries two version
> coordinates.** `upstream/master` (`ec45d5f4`, 2025-04-19) still reads `if vtype=16 then` at
> `Assemblerunit.pas:6845`, and local commit `4178e037` — the single commit our checkout is ahead
> by — is what patches it. CE's public repo lags its releases, so reading the source alone would
> have left this entry asserting a defect that the CE you actually run does not have. This is the
> first item where the two coordinates genuinely disagreed, and the binary won.
>
> Everything below is kept as the analysis of a **real defect in the last public source**. It still
> applies if you build CE from GitHub — which is why the fork carries the patch — and it is a
> worked example of how to find this class of bug. It does **not** describe a released 7.7.

### Problem
When we use instructions like `cmp bx, AA`, the assembler is wrong. It use `r/m16, imm8` (opcode `83`) encoding. The CPU will do sign-extend for `0xAA` and it become `0xFFAA` (-86), but user actually want `0x00AA` (170).

#### Reproduction
```asm
cmp bx, AA    ; user want: compare BX with 170 (0x00AA)
```

#### Actual (Wrong)

```
66 83 FB AA        cmp bx, FFAA    ; sign-extended 0xAA -> 0xFFAA = -86
```

#### Expected (Correct)

```
66 81 FB AA 00     cmp bx, 00AA    ; use 16-bit immediate = 170
```

Now, if we write `cmp bx, 00AA` (add zero in front), it is OK because `StringValueToType` will see length and use `vtype=16`. But we think just `AA` should also work correctly.

---

### Root Cause

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

### Fix

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

### Affected Instructions

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

### Verification

I tested these cases, now they are all correct:

| Input | Before (Wrong) | After (Correct) |
| --- | --- | --- |
| `cmp bx, AA` | `66 83 FB AA` (FFAA) | `66 81 FB AA 00` (00AA) |
| `cmp bx, 80` | `66 83 FB 80` (FF80) | `66 81 FB 80 00` (0080) |
| `cmp bx, FF` | `66 83 FB FF` (FFFF) | `66 81 FB FF 00` (00FF) |
| `cmp bx, 7F` | `66 83 FB 7F` (007F) | `66 83 FB 7F` (No change, safe) |
| `cmp bx, 05` | `66 83 FB 05` (0005) | `66 83 FB 05` (No change, safe) |
| `add bx, C0` | `66 83 C3 C0` (FFC0) | `66 81 C3 C0 00` (00C0) |

---

## 5. Embedded table Lua file stays cached after re-embed (no reload until CE restart)
**Tested CE Version:** 7.6, 7.7

When a table Lua file (e.g. `ue5_invoke_helper.lua`, added via **Table → Add File…**) has already been `load()`-ed in the current session, **swapping it for an updated copy does NOT take effect** — even if you remove the old file and re-add the new one. CE keeps the previously-loaded globals in the main Lua engine for the rest of the session.

> **Mechanism, corrected 2026-08-07 (source `7.5-195`): nothing caches the FILE.** The title says
> "cached" and that is the wrong mental model, which matters because it sends you looking for a
> cache to invalidate. `findTableFile` (`LuaTableFile.pas:115`, registered at `:189`) really does
> hand back the new blob and `load()` really does compile it. What persists is the **Lua state**:
> CE has exactly one, and it never rebuilds it when a table file is added, removed or the table is
> closed — the only thing that makes a new state is the Lua-callable `resetLuaState`
> (`LuaHandler.pas:5108`, whose own comment reads *"this creates a NEW lua state (cut doesn't
> destroy the current one)"*). So the globals from the first `load()` simply stay, and point 1
> below is what stops the fresh source from overwriting them. **The file is not cached; your
> globals are still alive and your own guard is refusing to replace them.**

Two things compound this:
1. **Helpers use a re-declaration guard.** Our `ue5_invoke_helper.lua` wraps its functions in `if not setDebugCamera then … end` so multiple AA Scripts loading the helper don't redefine it. Once the function exists as a global, a later `load()` of the file's source runs the guard, sees the global, and **skips the redefinition** — so the stale function persists.
2. **`findTableFile` returns the embedded blob**, and `load()` compiles fresh source, but (1) means the fresh source's definitions are never installed over the already-present globals.

**Symptom we hit:** after fixing `setDebugCamera` (executeCodeEx → mailbox) and re-exporting + re-embedding the helper, the generated record still ran the OLD function and returned `state=nil`. Deleting and re-adding the file did nothing.

**Workarounds:**
- **`resetLuaState()` from the Lua console** — cheapest fix, and it was missed originally. It is a
  registered CE Lua function (`LuaHandler.pas:16613`; documented in 7.7's `celua.txt:138`) that
  installs a brand-new Lua state, so every stale global disappears and the next `load()` takes.
  **Caveat, and CE says it out loud:** it does not destroy the old state — `celua.txt` calls it a
  memory leak. Fine for an iteration loop, not something to wire into a script.
- **Fully restart Cheat Engine** (closing just the table or the Lua engine window is not always enough — a full CE restart reliably clears the cached globals).
- **Or** make the generated script self-contained so it doesn't depend on the embedded helper at all (what we did for "Copy CE Script": inline the mailbox round-trip, no `findTableFile`). This sidesteps the cache entirely.
- A helper could also force-reload by clearing its own globals before redefining (e.g. drop the `if not …` guard, or set the functions to `nil` first), but that defeats the multi-load guard, so the self-contained route is preferred.

---

## 6. `errorOnLookupFailure` — `celua.txt` says the default is TRUE, the source sets it FALSE

**Checked against:** the 7.5-195 **source** (both branches read); the 7.7 **binary** was NOT run, so
the source's behaviour is proven for 7.5 and *inferred* for 7.7. That gap is the whole reason the fix
described below covers both possibilities instead of picking one.

CE's own Lua documentation states:

> `errorOnLookupFailure(state)`: If set to true (default) address lookups in stringform will raise an error…

— `celua.txt:229` in the installed 7.7, byte-identical at `Cheat Engine/bin/celua.txt:196` in the
7.5 clone. **The source does the opposite.** `TSymhandler.create` ends with

```pascal
  ExceptionOnLuaLookup:=FALSE;          // symbolhandler.pas:6688
```

and that flag is what gates the raise:

```pascal
function TSymhandler.getAddressFromNameL(name: string; ...):ptrUint;  //Lua
begin
  result:=getAddressFromName(name, waitforsymbols, e);
  if e then
  begin
    if ExceptionOnLuaLookup then                          // symbolhandler.pas:5082
      raise symexception.Create(...)
    else
      result:=0;                                          // <- the DEFAULT
  end;
end;
```

An exhaustive search of the clone (`grep -rn -i exceptiononlualookup`) returns four hits — the field
declaration at `symbolhandler.pas:300`, that use at `:5082`, the constructor at `:6688`, and nothing
else. The **only** writer is the Lua-facing setter `errorOnLookupFailure`
(`LuaHandler.pas:9093-9111`, registered at `:16813`). Nothing in CE's Pascal ever sets it true.

**So by default `getAddress("NoSuchSymbol")` returns the integer `0`.** It raises only if a script
somewhere in the session called `errorOnLookupFailure(true)` — and *then* the exception is converted
to a real Lua error by the wrapper's `except` arm (`LuaHandler.pas:4374-4391`,
`lua_pushstring` + `lua_error` on Windows/x64).

### Why this cost us something

Audit #5's finding AA4 asserted, as "CE source-verified", that a bare `getAddress` **raises** on a
missing symbol and that `scripts/ue5_dissect.lua`'s `if fn == nil or fn == 0 then error(…)` was
therefore dead code. Acting on that would have **deleted a live guard** — the only thing that turns
"the DLL was never injected" into a message naming the export, rather than an `executeCodeEx` call
to address 0 reporting `Failure launching thread`. The premise came from reading the wrapper's
`except` block (which does raise) without checking whether the resolver beneath it ever throws.

### What to do about it

**Handle both.** A script cannot know which state CE is in — any table or AA script the user loaded
can have flipped the flag, and it is global and never reset. The correct shape is the one
`ue5_dissect.lua` now uses:

```lua
local fn = getAddress(name)                    -- 0 by default, raises if the flag is on
if fn == nil or fn == 0 then error("… not found: " .. name) end
```

`getAddressSafe` is **not** a drop-in replacement: it returns **nil**, not 0
(`LuaHandler.pas:4329-4332` sets the C-function's *result count* to 0, so Lua receives no value),
which is why the `fn == nil` half of that test is doing real work too.

---

## Appendix: other CE defects, and who owns them

The six entries above are **bugs we hit** — each has a symptom we saw and a workaround we used.
A 2026-08-07 re-audit of the CE 7.5-195 source turned up roughly eighteen more genuine CE-side
defects. **None is listed here as an entry**, for two reasons: we have not hit any of them, and
each already has an owner in a reference doc that explains it at the point of use. Duplicating
them into a second file is how two copies of a fact start disagreeing.

This index exists only so they are *findable* from the file you actually open when CE misbehaves.
One line each; the detail stays with the owner.

**CCODE / AutoAssembler** — owner: [ce-ccode-reference.md](ce-ccode-reference.md) §3, §12, §13.
Note this repo emits `{$lua}` blocks, **not** `{$CCODE}`/`{$LUACODE}`, so none of these bites us
today; they matter only if that changes.

| Defect | Where |
|--------|-------|
| Comma-separated params (`{$CCODE a=RAX,b=RBX}`) are **silently dropped** — the parser splits on space only | `autoassemblercode.pas:194` |
| A **mistyped register name** does not error: it binds to RAX and is written back to RAX on exit | `:197`, `:245`, `:285-286` |
| `RBPF` lands on `0x228` — the stub's saved-RSP pointer → **crash**; `RSPF` lands on `0x230` → **RBP corruption** | `:808` / `:866` vs the stub stores `:313-327` |
| `PREFIX=` written on a `{$CCODE}` line injects a **phantom `PREFIX` variable** bound to RAX | `:769-770`, `:1289-1290` |
| With `PREFIX` set, half the **unprefixed AA labels are never created** — `symbols[i shr 1]` where `i` was meant | `:1455` |
| An out-of-range `XMMn` does not error; it **re-emits the previous parameter's declaration** (the read loop never clears `s`) | `:801-817` |
| **64-bit LUACODE reading `RSP` returns R9** — `+24` is inside `readPointer()` instead of after the deref | `:1003` (cf. correct C side `:806`) |
| **32-bit LUACODE reading `ESP`** uses a `*8` stride where the slot needs `*4` | `:1023` |

**Plugin exports** — owner: [ce-plugin-api-reference.md](ce-plugin-api-reference.md) and
[ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md).

| Defect | Where |
|--------|-------|
| `ce_assembler` **discards** `Assemble()`'s boolean and hardcodes `result:=true` — check `*returnedsize`, not the return value | `pluginexports.pas:774`, `:790` |
| …and has **no `try..except`**, so `EAssemblerException` unwinds out of a `__stdcall` export into your frames | `Assemblerunit.pas:3647` and five more raise sites |
| `ce_disassembler` does a **1-byte `StrCopy` overflow** when `length(s) == maxsize` — pass `sizeof(buf)-1` | `pluginexports.pas:793-809` |
| Type 0 **leaks the record on every click** (`getmem`, never freed), and leaks `offsets` too if you return FALSE (the free sits inside the TRUE branch) | `MainUnit.pas:9724`, `:9735`, `:9771` |
| `timer_onTimer` **silently starts the timer** (`enabled:=true`), and the SDK exports no way to stop one — only `object_destroy` | `pluginexports.pas:291` |
| `previousOpcode` has **no failure return** — on total failure it falls back to `address-1`, so the usual `== 0` guard is dead code | `disassembler.pas:15823` |
| `previousOpcode` also **mutates CE's shared `defaultDisassembler`** (`aggressivealignment`) while `TDisassembler`'s critical section is commented out → data race from a worker thread | `disassembler.pas:15802-15803`, `:16555` |
| Eleven `ExportedFunctions` slots are **permanently `nil`**, driver or no driver | `plugin.pas:1872`–`:1915` |
| `ce_messageDialog`'s **button mapping is unguarded** — an out-of-range value hands a garbage set to `MessageDlg` (the align mapping next door *does* have an `else`) | `pluginexports.pas:2218-2222` |
| CE **loads your DLL twice**: a throwaway probe (LoadLibrary → `GetVersion` → FreeLibrary) runs before the real load, so never init global state in `GetVersion` | `plugin.pas:1497`, `:1522`, `:1525` |

**Scanning** — owner: [ce-memory-scanning-internals.md](ce-memory-scanning-internals.md).

| Defect | Where |
|--------|-------|
| `AsyncAOBScan` **silently discards its alignment arguments** (hardcoded `fsmNotAligned`), which makes the 4th/5th arguments of Lua `AOBScanUnique` / `AOBScanModuleUnique` **dead** | `simpleaobscanner.pas:120` |
| `VirtualQueryEx_StartCache` is a **no-op stub on native Windows** — CE's own comment is `//don't use it in windows`; only the ceserver path implements it | `NewKernelHandler.pas:1515-1518`, installed `:2406` |
| `TVirtualAllocEx` / `TVirtualProtectEx` / `TVirtualQueryEx` publish `dwSize` as **`DWORD`**, so a 64-bit size passed through the plugin table is truncated to its low 32 bits | `NewKernelHandler.pas:589`, `:590`, `:592` |

> **Why the AOB row does not affect us.** Our emitted `AOBScanModuleUE` helper
> (`Services/CeXmlExportService.cs`) drives `createMemScan()` + `ms.firstScan(...)` directly instead
> of going through `simpleaobscanner.pas`, which happens to make it immune. Its 14 arguments were
> re-checked against the Lua binding's parameter order (`LuaMemscan.pas:54-77`) and all land in the
> right slots, including `'+X-C-W'` in `protectionflags` (`:70` → `parseProtectionflags` `:81`).

**CE Lua `executeCodeEx`** — owner: [ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md) §13.
Unlike the groups above, this one **does** run on every path we emit: `scripts/ue5_dissect.lua`'s
`callDLL` and `Services/CeLuaHygiene.cs`'s `AppendCallDllHelper` both go through it.

| Defect | Where |
|--------|-------|
| **SUSPECTED, NOT VERIFIED —** `closehandle(thread)` looks **unreachable**: all four branches `exit()` before it and the `finally` does not close it either, so every call would leak one thread handle in cheatengine.exe. Read only in the 7.5 source; **the 7.7 binary was not probed** — §4 above is the standing reminder that those are different facts. Probe: `(Get-Process cheatengine).HandleCount` before/after N calls | `LuaHandler.pas:11893`, `finally` at `:11901-11917` |
| `WAIT_TIMEOUT` sets `dontfree := true`, so the `finally` skips `VirtualFreeEx` on the stub, the result address, **and every string allocation** — a timeout permanently leaks *target-process* memory, and repeats accumulate. Deliberate (the remote thread may still be running), but documented nowhere; `freeExecuteCodeExStub` is the reclaim path | `LuaHandler.pas:11880` vs `:11907`, `:11412` |

> **Not a CE defect, but the trap next door:** a `nil` timeout means **INFINITE**, not "use a
> default" (`:11504-11505`), and the wait pumps no messages — so `executeCodeEx(1, nil, fn, ...)`
> can freeze CE's GUI permanently with no UI-level recovery. That one is ours to fix at the call
> site, not CE's; see ce-plugin-sdk-notes.md §13.3 and [todo.md](todo.md).
