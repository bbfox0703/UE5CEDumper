# Reversing non-standard UE games (when AOB + heuristics fail)

A playbook for the case where a game's `GObjects` / `GNames` / `GWorld` resolves to a
**decoy** or reads as **garbage** — i.e. the standard AOB patterns and the layout
heuristics in `Genau`/`Aura` don't apply because the publisher forked or repacked the
engine. The motivating case was **Avowed (Obsidian, UE5.3)**; the same steps work for any
non-standard UE x64 title. Tools referenced here live in [`tools/`](../tools/README.md).

> **TL;DR of the Avowed case:** GObjects had no AOB match (not even patternsleuth's) because
> it's a *static* `FUObjectArray` and `FUObjectItem` was **packed to 20 bytes (0x14), not 24** —
> reading at 24 misaligned every object into garbage. The fix was found by decompiling
> `AllocateUObjectIndex` in Ghidra. See [`avowed-gobjects-fix.md`](avowed-gobjects-fix.md).

## 0. Recognise the symptom (from the DLL logs)

`%LOCALAPPDATA%\UE5CEDumper\Logs\<game>\` — read `init-*.log`, `scan-*.log`, `offsets-*.log`:

- **`Objects=0`** or an implausibly small count → GObjects pointer wrong, or array layout wrong.
- **A constant, weird "count"** across launches (e.g. `7,667,809` = bytes `0x00750061` = "ua") → a
  non-pointer field is being read as the count; the base or the NumElements offset is wrong.
- **Object tree shows garbage / CJK names mixed with valid ones** → the `FUObjectItem` *stride* is
  wrong (every Nth item misaligned). Standard stride is 24; some forks pack it to 20.
- **`Start from GWorld` empty** but objects work → GWorld pointer is a decoy (`*GWorld` not a UWorld).

## 1. Is it standard at all? — patternsleuth CLI

[patternsleuth](https://github.com/trumank/patternsleuth) is the resolver library RE-UE4SS uses.
Run its CLI on the EXE offline (no game running) to (a) confirm the standard resolvers fail and
(b) get string-anchored candidate functions:

```sh
# does the standard GObjects resolver work?  (Avowed: "expected at least one value" = NO)
cargo run --release -p patternsleuth_cli -- scan --path "<game>.exe" --resolver GUObjectArray

# string-anchored fallback — the GC-pool log string lives in AllocateUObjectIndex
cargo run --release -p patternsleuth_cli -- scan --path "<game>.exe" \
    --resolver FUObjectArrayAllocateUObjectIndex --disassemble-merged
```

The second command yields a few candidate function VAs (it can't always pick one — that's fine,
we disambiguate next). RE-UE4SS's `UEPseudo` repo has the per-version standard layouts to compare
against, and `sync_tools.ps1` checks it out, so read them on disk at
`vendor/RE-UE4SS/deps/first/Unreal/generated_include/FunctionBodies/` — one
`*_MemberVariableLayout_DefaultSetter_FUObjectArray.cpp` per version, 4_07 through 5_08. An empty
`deps/first/` means the sync has not run, not that the submodule is gated: it is a public repo, and
only the `git@github.com:` URL in RE-UE4SS's own `.gitmodules` makes it look otherwise.

## 2. Read the .data cluster — capstone

For each candidate function, disassemble it and look at which **writable .data** addresses it
touches. The ones in the **zero-init BSS tail** (a section whose `VirtualSize > SizeOfRawData`)
are runtime-filled globals — `GUObjectArray` is one.

```sh
py -m pip install capstone pefile
py tools/pe/disasm_function.py "<game>.exe" 0x147A604E0 0x14814D2F0   # candidate VAs
```

A tight cluster of `.data [BSS zero-init]` refs around one base = the `FUObjectArray` struct.

## 3. Read the exact layout — Ghidra headless

The decompiler makes the layout unambiguous. With a Ghidra project already analysed
(Ghidra 12 = PyGhidra, so **use the Java scripts**, not Jython):

```sh
# decompile the candidates; read off field offsets + the item stride
analyzeHeadless <projLoc> <projName> -process -noanalysis -readOnly \
    -scriptPath tools/ghidra -postScript decompile_functions.java 0x147A604E0 0x14814D2F0
```

In `AllocateUObjectIndex` you can read directly (with `this` = `int* GUObjectArray`):

- `param_1[9]++` → `NumElements` at `base + 9*4 = +0x24`
- `param_1[8]` (compared as max) → `MaxElements` at `+0x20`
- `*(longlong*)(param_1+4)` → `ObjObjects.Objects` (chunk table) at `+0x10`
- `(index & 0xffff) * 0x14 + chunkTable[index>>16]` → **item stride `0x14` (20 bytes!)**, 65536/chunk
- the `lea*5; shl<<2` form of the index multiply (`*5`*`4` = *20) confirms the stride in the codegen

## 4. Pin the global address — callers

If the function takes the struct as `this` (a parameter, not a direct global), find the **caller**:
it does `LEA RCX,[GUObjectArray]; call`, so the LEA target is the global.

```sh
analyzeHeadless <projLoc> <projName> -process -noanalysis -readOnly \
    -scriptPath tools/ghidra -postScript find_callers.java 0x14814D2F0
# -> call@... RCX<- LEA RCX,[0x14b5be398]   ... = GUObjectArray (RVA 0xB5BE398)
```

Cross-check: the field accesses in step 3 should line up against this base (e.g. a `CMP byte
[base+0xC]` = `OpenForDisregardForGC`).

## 5. Encode the fix in the DLL

- **AOB** → add a pattern to `Sig::GOBJECTS_PATTERNS` in `dll/src/Himmel.h` (NOT inline in code).
  Use the `SIG_RIP(id, pat, AobTarget::GObjects, instrOffset, opcodeLen, totalLen, adjustment,
  priority, source, notes)` macro. If the RIP target is a member (e.g. chunk table = base+0x10),
  set `adjustment = -0x10`. Prefer a **long, repeated** codegen pattern (object indexing appears
  in many functions) over a single-site one — it survives game patches better. Adding two is fine
  (redundancy); `ValidateGObjects` picks the real base among non-unique matches.
- **Layout / stride** → handled by `Aura::DetectLayout` + `DetectItemSize` once the base is right
  (the chunk allocator is usually standard even when the item is packed). If detection mis-picks,
  `Aura::InitWithExtendedLayout(base, stride)` forces it.
- **No-AOB fallback** → `Genau::FindGObjectsStaticStruct` scans `.text` for `lea/mov [rip]` →
  writable-`.data` slots, windows each as a chunked `FUObjectArray`, and validates by CONTENT
  (first objects must resolve to clean printable-ASCII names) while trying candidate strides.
- **GWorld decoy** → `Genau::ExtraScanGWorld` scans `.data` for a slot pointing to a non-CDO
  `UWorld` and picks the ACTIVE game world by non-null `OwningGameInstance` (skips World-Partition
  `_Generated_` cells). The recovery fires in `Frieren::UE5_Init` when the pointer is invalid.

## Gotchas

- **Ghidra 12 dropped bundled Jython** — headless `.py` post-scripts fail with "Ghidra was not
  started with PyGhidra". Use Java GhidraScripts (the ones in `tools/ghidra/*.java`) or set up
  PyGhidra. (The older `tools/ghidra/*.ghidra.py` scripts run via the pyghidra venv runners.)
- A **partial** Ghidra auto-analysis may not have created code→string references — then
  `find_gobjects.java` finds nothing and you must use the patternsleuth candidates + `find_callers`.
- patternsleuth's `--disassemble-merged` only prints when a resolver returns exactly one value;
  on an ambiguous resolve it errors but still lists the candidate VAs in the message.

## Encrypted games — what `vendor/Dumper-7` and `vendor/RE-UE4SS` can (and can't) do

We periodically re-evaluate whether to adopt the two reference dumpers under `vendor/` to
handle **encrypted** games. **Decision: no — and there is nothing to gain on the
encryption axis.** Both repos (and this project) only provide a *plug-in hook*; neither
decrypts any title out of the box. Read in full 2026-06-28 against `vendor/Dumper-7@c891b17` and
`vendor/RE-UE4SS@2352d15b`; re-checked 2026-09-05 at `Dumper-7@b88241b` and `RE-UE4SS@24b12662`,
where neither delta (37 and 135 commits) adds a decrypt path — no `encrypt`/`decrypt`/`aes` in
either range's changed filenames or commit subjects, and RE-UE4SS's `src`/`include` still hold zero
`decrypt` hits. These are local reference clones, **not** git submodules — see `.gitmodules`.

### First, disambiguate "encrypted"

- **In-memory pointer / struct encryption** — the `FUObjectArray::Objects` pointer is
  XOR/add/rotate-obfuscated, or UE's `TEncryptedObjectProperty` stores some object pointers
  encrypted in memory. This is the only kind a *runtime* dumper cares about.
- **PAK / asset AES encryption** — the on-disk `.pak` AES-256 key. This is an *offline
  unpacking* concern (UnrealPak / FModel / aes-finder), entirely orthogonal to dumping live
  memory. Neither repo's runtime path addresses it (RE-UE4SS's pak loader is for mod-loading,
  not dumping). If a user says "encrypted game" meaning this, point them at offline tools.

### Capability matrix (in-memory encryption)

| | `Dumper-7` | `RE-UE4SS` | **This project** |
|---|---|---|---|
| GObjects pointer-decrypt hook | ✅ `InitObjectArrayDecryption(lambda)`, default identity | ❌ none | ✅ `Aura::SetDecryptFunc` → `UE5_SetObjectDecryption` export, default nullptr |
| Ships any per-game key/routine | ❌ zero (README `^ 0x8375` is a sample) | ❌ zero | ❌ zero (hook only) |
| `TEncryptedObjectProperty` | ⚠️ opt-in `bEnableEncryptedObjectPropertySupport` (default off) | ❌ | ❌ |
| Non-standard / forked engine | manual XOR/offset | per-game `assets/CustomGameConfigs/*.ini` (34 titles) overriding AOB / FName-method / version / vtable | per-game config (roadmap) |

### Findings

- **Dumper-7** decryption = `ObjectArray::DecryptPtr`, defaulting to an identity lambda
  (`Dumper/Engine/Public/Unreal/ObjectArray.h`). The README's `uint64(ObjPtr) ^ 0x8375` key
  is **per-game and must be reverse-engineered by you** — it is not built in. Its `XORString`
  setting is unrelated (it obfuscates Dumper-7's *own generated SDK strings*). The one piece
  worth borrowing *if* we ever hit it: `TEncryptedObjectProperty` support
  (`Dumper/Settings.h`), a real newer-UE feature for in-memory-encrypted object properties.
- **RE-UE4SS** has **no AES / decrypt code at all** (the only `encrypt` hit is a YouTube
  iframe). It copes with hard games via `CustomGameConfigs/*/UE4SS-settings.ini` — signature /
  offset / engine-version / vtable *overrides*, not decryption. It assumes `GUObjectArray` /
  `FName` are directly readable, so it does not target pointer-encrypted / strong-anti-cheat
  titles. Its 34 shipped profiles (FF7 Rebirth/Remake, Atomic Heart, Borderlands 3, Jedi
  Survivor, Lies of P, …) are forked-engine tuning, not encrypted-pointer cases.
- **This project already has parity** with Dumper-7's pointer-decrypt hook: `Aura::SetDecryptFunc`
  / `Aura::DecryptObjectPtr` (`dll/src/Aura.cpp`), wired through the CE-Lua export
  `UE5_SetObjectDecryption` (`dll/src/Frieren.cpp`). Adopting Dumper-7 for decryption would add
  zero coverage.

### Recommendation

- **Do not vendor either repo for decryption.** We already match Dumper-7's hook; the missing
  ingredient for any encrypted game is the **per-game routine**, which you reverse yourself and
  pass to `UE5_SetObjectDecryption` before `UE5_Init` (see `Frieren.h` — `UE5_AutoStart` does
  not support decryption; use the manual Lua flow).
- **Only selectively borrow** Dumper-7's `TEncryptedObjectProperty` handling, and only when a
  concrete UE5 title that uses it shows up.
- RE-UE4SS's value here is its `CustomGameConfigs` *per-game-profile philosophy*, which overlaps
  our existing per-game config — that's a separate (non-encryption) topic.
