# Cheat Engine Memory Scanning Internals

> **Why this doc is in this repo.** UE5CEDumper does not build a CE plugin today — it is an
> injected C++ DLL plus an Avalonia UI that *generates* CE artifacts (AA scripts with `{$lua}`
> blocks, `.CT` cheat tables, CE XML pointer chains, Structure Dissect files) and talks to the
> **AOBMaker CE plugin** over a named pipe. This document is kept here because this repo has its
> own in-process scanners — `Radar`'s value scan, `Aura`'s `ScanForValue` / `FindInContainers`,
> `Macht`'s `AOBScan` — and CE is the reference implementation they are measured against. §14 in
> particular is the direct precedent: CE's speed comes from **alignment stride plus thread
> fan-out, not from a clever inner loop.** Cross-reference
> [simd-scanning-notes.md](simd-scanning-notes.md) and
> [aob-block-library-eval.md](aob-block-library-eval.md). **This is a mirror, not the master.**
>
> **Master copy:** `<private-ce-repo>/docs/Memory-Scanning-Internals.md` — edit there
> first, then mirror here.

> This document records the core implementation details of the memory scanning engine, based on an
> actual reading of the CE source (**Free Pascal / Lazarus**, compiled with `{$MODE Delphi}`, *not*
> Embarcadero Delphi).
> The original focus was AOB pattern matching; the **2026-05-28 expansion** added the threading
> model and alignment strategy of CE's *numeric / value scan* path (§14) and the details of the
> AOBMaker SIMD scanner (§15). **This copy contains the header block and §1–15 only** — see the
> note where §16 would have been.
>
> ### Version coordinates (every line number below is relative to these)
>
> | What | Value |
> |------|-----|
> | CE **source tree** read for this document | `D:\Github\cheat-engine`, tag **`7.5-195`**, HEAD `4178e037` (level with `upstream/master`) |
> | CE **binary** installed locally | `C:\Program Files\Cheat Engine\`, **7.7.0.10568** (ProductVersion 7.7) |
> | AOBMaker source tree | `D:\Github\AOBMaker`, HEAD `fbc12d3` |
>
> ⚠ What was read is the **7.5 source**; what runs is the **7.7 binary**. Every `memscan.pas:NNNN`-style
> line number in this document holds only at tag `7.5-195`; a different checkout means re-locating
> them (prefer grepping for the **format string / identifier**, not the line number).
>
> Sources:
> - CE: `D:\Github\cheat-engine\Cheat Engine\` — `memscan.pas`, `simpleaobscanner.pas`, `foundlisthelper.pas`, `NewKernelHandler.pas`, `parsers.pas`, `commontypedefs.pas`, `autoassembler.pas` (line counts in §13, **derived values**)
> - AOBMaker: `D:\Github\AOBMaker\src\AOBMaker.Platform.Windows\Services\` — `WindowsMemoryScanner.cs` (455 lines), `WindowsSequenceScanner.cs` (2038 lines); shared grammar `src\AOBMaker.Core\Services\CeAobGrammar.cs`
>
> A synced backup copy is kept at `D:\Github\discrete\docs\Memory-Scanning-Internals.md`.

---

## 1. Architecture Overview

CE's memory scanning is built from three layers of components:

```
TMemScan (high-level controller)
  └── TScanController (scan coordinator)
        └── TScanner[] (worker threads × N)
```

| Component | File | Responsibility |
|------|------|------|
| **TMemScan** | `memscan.pas` | UI layer: manages scan options, scan result files, progress reporting |
| **TScanController** | `memscan.pas` | Thread management: memory region enumeration, work distribution, TScanner lifetime |
| **TScanner** | `memscan.pas` | Worker thread: reads memory buffers, runs pattern matching, writes out results |
| **simpleaobscanner** (3 free functions, **no class**) | `simpleaobscanner.pas` | Lightweight AOB scan wrapper: used by Lua `AOBScan` / `AOBScanUnique` / `AOBScanModuleUnique`. **AutoAssembler's `AOBSCAN` does not go through here** — see §8 |

Class declaration locations (`memscan.pas`): `Tscanfilewriter`(128) / `TScanner`(165) / `TScanController`(518) / `TMemScan`(626).
The fourth class the table above does not list is **`Tscanfilewriter` (memscan.pas:128)**: one is allocated per `TScanner`, writing results asynchronously into the `ADDRESSES` / `MEMORY` files.

---

## 2. Memory Region Enumeration

### VirtualQueryEx scan

```pascal
// TScanController enumerates every scannable region before FirstScan
while VirtualQueryEx(processHandle, address, mbi, sizeof(mbi)) <> 0 do
begin
  if (mbi.State = MEM_COMMIT) and IsValidRegion(mbi) then
    AddRegion(mbi.BaseAddress, mbi.RegionSize);
  address := mbi.BaseAddress + mbi.RegionSize;
end;
```

> The above is **rewritten pseudo-code**, not CE's actual text. The real region enumeration loop is inside `TScanController.firstScan`, memscan.pas:7168-7288, with the filter predicates at 7188-7242. Also note: `IsValidRegion(mbi)` as written above does not exist — CE's real `TScanController.isValidregion(address: ptruint)` (memscan.pas:6887) takes an **address**, and is only called on the working-set path (7129).

> **CE probes the address space once and then trusts the table.** The whole of `memscan.pas`
> contains exactly **one** `VirtualQueryEx` call site — memscan.pas:6915, inside `isValidregion`,
> reached only from the controller's setup in `TScanController.firstScan`. The worker threads
> (`TScanner.firstscan` at 5750, `TScanner.firstNextscan` at 5660) never probe at all: they read
> straight from the `memRegion` table built at 7168-7288. That is the contrast point against any
> scanner that re-runs `VirtualQuery` per chunk — such a design multiplies the syscall count by
> (region bytes / chunk size) for information CE established once. (Note it is **not** achieved via
> a VQ cache: `VirtualQueryEx_StartCache` is a no-op stub on native Windows — see §14.5.)

### Region filtering

| Condition | Notes |
|------|------|
| `MEM_COMMIT` | Only committed pages are scanned |
| Protection | Excludes `PAGE_NOACCESS` and `PAGE_GUARD` |
| Type | Filters `MEM_IMAGE` / `MEM_PRIVATE` / `MEM_MAPPED` according to the user setting |
| Executable | **Not the default.** `TMemScan.parseProtectionflags` (memscan.pas:8679) initialises all three flags to `scanDontCare` (8684-8686), and `Tscanregionpreference=(scanDontCare, scanExclude, scanInclude)` (memscan.pas:46) makes `scanDontCare` the zero value of an unset field. In AutoAssembler only **`AOBSCANEX`** ever sets a protection string (autoassembler.pas:1045 `'*C*W+X'`), and that string is passed into `firstscan` in the **`_scanvalue2`** position (1297/1299), where it has no effect whatsoever for `soExactValue`+`vtByteArray` |
| Writable | Whether writability is required depends on the scan type |

> protection string syntax: `+`=include, `-`=exclude, `*`=don't care, with the letters `W`/`C`/`X` (darwin additionally has `D`); parsing at memscan.pas:8691-8726. `AsyncAOBScan`'s `protectionflags` defaults to `''` (simpleaobscanner.pas:78) → don't care across the board.

### Module range limiting

`AOBScanModuleUnique` / AA's `AOBSCANMODULE` use the module's base address + size to bound the scan range.
(Note: the two functions `getModuleBase(moduleName)` / `getModuleSize(moduleName)` **do not exist** — the only same-named things are the Lua binding `LuaHandler.pas:4263 getModuleSize(L: PLua_state)` and `PointerscanresultReader.pas:106 getModuleBase(modulenr: integer)`, neither of which has that shape.)

```pascal
// simpleaobscanner.pas:111-118 (AsyncAOBScan)
if symhandler.getmodulebyname(modulename, mi) then   // symbolhandler.pas:337
begin
  minaddress := mi.baseaddress;
  maxaddress := mi.baseaddress + mi.basesize;
end
else
  exit;   // module not found → give up immediately
// the scan range is [minaddress, maxaddress), passed in via firstscan's _startaddress / _stopaddress
```

The equivalent on the AA side is autoassembler.pas:1174-1189: `AOBSCANMODULE` tries `getmodulebyname` first, then falls back to `getAddressFromName` + `getmodulebyaddress`.

---

## 3. Pattern Representation

### Data structure

```pascal
// commontypedefs.pas:28
type
  TBytes = array of integer;  // 32-bit signed; int is required to represent the wildcard (-1)
                              // and the nibble-wildcard encoding ($8000xxxx)
```

| Value | Meaning |
|----|------|
| `0..255` | Literal byte, must match exactly |
| `-1` (`$FFFFFFFF`) | Full wildcard (`??` / `**`), matches any byte |
| Other negative values (bit31 = 1) | Nibble wildcard: `$80000000 or (mask shl 8) or value`. `$80000000` is the sole bit that identifies "this is a nibble wildcard" (memscan.pas:2561 `if abs_arraytofind[i]<0`) and **cannot be omitted** — without it `(mask shl 8) or value` is a positive value (e.g. `$F070`), and memscan.pas:2572 will compare it as a literal byte, giving 0 hits forever. `7*` → `$8000F070` (mask `$F0`, value `$70`); `*9` → `$80000F09` (mask `$0F`, value `$09`). Comparison: `(byte and ((v shr 8) and $FF)) = (v and $FF)` |

### Pattern parsing

The real parser is `parsers.pas:297`
`procedure ConvertStringToBytes(scanvalue: string; hex: boolean; var bytes: TBytes; canHandleNibbleWildcards: boolean=false);` (interface declaration at 14), with the encoding at 341-364.

```pascal
// parsing "48 8B ?? 08" → TBytes (illustrative; for the real logic see the three points below)
delims := [' ', ',', '-'];                    // parsers.pas:309
helpstr := ExtractWord(i, scanvalue, delims); // 316 — split into "words" first
while j <= length(helpstr) do
begin
  helpstr2 := copy(helpstr, j, 2);            // 334 — then cut each word into 2-hex-char pieces
  try
    bytes[...] := strtoint('$' + helpstr2);   // 337 — success = literal value
  except
    bytes[...] := -1;                          // 339 — failure = full wildcard
    if canHandleNibbleWildcards and (length(helpstr2) = 2) then
      ...                                      // 341-364 — then try the nibble rescue
  end;
  inc(j, 2);                                   // 368
end;
```

Three things that are easy to misunderstand:

- **CE does not compare against the literal token `'??'`**: it is "if `StrToInt('$'+token)` throws, treat it as a wildcard", so `??` / `**` / `xx` all work.
- **It does not tokenise on whitespace**: the outer layer splits "words" on `[' ', ',', '-']`, and the inner layer then cuts each word into **2-hex-character** pieces (334 + 368). So `488B??08` and `48 8B ?? 08` parse to exactly the same thing.
- **The nibble rescue only accepts length 2**: `length(helpstr2)=2` at 341 means a lone trailing single hex character at the end of a word will **never** become a nibble wildcard — it becomes a full `-1`.

---

## 4. Pattern Matching Algorithm

### The core: linear byte-by-byte comparison + early exit at the first mismatching position

> CE has **no** dedicated "compare the first byte first" shortcut (see §11). The so-called early exit is nothing more than the `for` loop below exiting at the first **position** that does not match — it is loop ordering, not a first-byte jump.

```pascal
// memscan.pas:2582-2593 (loop body 2585-2590)
function TScanner.ArrayOfByteExact(newvalue,oldvalue: pointer):boolean;
var i: integer;
begin
  for i:=0 to abs_arraylength-1 do
    if (abs_arraytofind[i]<>-1) and (pbytearray(newvalue)[i]<>abs_arraytofind[i]) then
    begin
      result:=false; //no match
      exit;  // ← exits at the first mismatching position (i can be any index, not just 0)
    end;

  result:=true; //still here, so a match
end;
```

### Nibble wildcard variant

```pascal
// memscan.pas:2554-2580 — the actual contents: no local variables other than i,
// mask/compare done inline on a 32-bit int in one line
function TScanner.ArrayOfByteExact_NibbleWildcardSupport(newvalue,oldvalue: pointer):boolean;
//sperate function as support for this will cause it to be slower
var i: integer;
begin
  for i:=0 to abs_arraylength-1 do
  begin
    if abs_arraytofind[i]=-1 then continue; //full wildcard
    if abs_arraytofind[i]<0 then
    begin
      if ((pbytearray(newvalue)[i] and ((abs_arraytofind[i] shr 8) and $ff))<> (abs_arraytofind[i] and $ff) ) then
      begin result:=false; exit; end;
    end
    else
    if (pbytearray(newvalue)[i]<>abs_arraytofind[i]) then
    begin result:=false; exit; end;
  end;
  result:=true;
end;
```

`nibbleSupport` is only true when the pattern genuinely contains a nibble wildcard (memscan.pas:4884-4894 scans `abs_arraytofind` for elements that are `(<0) and (<>-1)`). The routing choice for a single AOB is in `TScanner.configurescanroutine`:

```pascal
// memscan.pas:5268-5271
if nibbleSupport then
  CheckRoutine:=ArrayOfByteExact_NibbleWildcardSupport
else
  CheckRoutine:=ArrayOfByteExact;
```

The multi-AOB (`vtByteArrays`) versions are `ArrayOfBytesExact` (memscan.pas:2627) / `ArrayOfBytesExact_NibbleWildcardSupport` (2595), routed inside `FirstScanmem` at 3901-3904.

### Important: **no Boyer-Moore, no KMP**

CE's AOB scan uses the most basic **sliding window advancing one byte at a time** (O(n×m)), accelerated only by "exit at the first mismatching position" — which is a natural consequence of the loop ordering and **not** any kind of first-byte jump or skip table. For a typical game process (a few hundred MB of memory, a pattern of a few dozen bytes) this is already fast enough.

---

## 5. Buffer Management

### Read strategy

```pascal
// CEFuncProc.pas:296 — a global variable, not a const
var buffersize: dword;
// MainUnit2.pas:627-635 — default 512 (KB) * 1024 = 512 KB
// formsettingsunit.pas:595 — user-changeable under Settings → Scan Settings (unit: KB)
```

⚠ `buffersize` is **not** 4 MB, and it is not a compile-time constant. There is no `buffersize = ...` declaration anywhere in the tree; `memscan.pas` only *uses* it (4593, 5046/5084/5124/5164/5202/5236, 5716, 5836-5838, 6755-6756, 7455-7456), it never defines it. It is simultaneously the element-count ceiling of every TScanner result buffer (`maxfound:=buffersize`, memscan.pas:4593) and the `maxregionsize` ceiling (6755-6756 / 7455-7456).

Each TScanner thread:

1. Starts at the beginning of its assigned memory region
2. Calls `ReadProcessMemory` in units of `buffersize` (512 KB by default)
3. **Conditionally** reads `variablesize - 1` extra bytes as a tail (to handle patterns straddling a chunk boundary) — conditions below
4. Scans linearly inside the buffer
5. Advances `buffersize` bytes and repeats until the region ends

#### Tail overlap: FirstScan and FirstNextScan are **two different mechanisms**

`memscan.pas` contains two very similar-looking paths that are easy to confuse:

**(a) `TScanner.firstNextscan` (memscan.pas:5660)** — unconditionally adds a tail whenever there is more data behind:

```pascal
// memscan.pas:5720-5723 (inside firstNextscan)
if size < toread then  // there is more data behind
  ReadProcessMemory(phandle, pointer(currentbase),
    memorybuffer, size + variablesize - 1, actualread)
else  // last chunk
  ReadProcessMemory(phandle, pointer(currentbase),
    memorybuffer, size, actualread);
```

**(b) `TScanner.firstscan` (memscan.pas:5750)** — i.e. the real First Scan, which goes through `canOverlap: boolean` (declared at 5764):

```pascal
// zeroed at the start of every region — memscan.pas:5806
canOverlap := false;

// only two situations turn it on:
// (1) the next memregion is immediately adjacent in address space — memscan.pas:5820-5821
if ((i+1) < OwningScanController.memRegionPos-1) and
   (currentbase + toread = OwningScanController.memregion[i+1].BaseAddress) then
  canOverlap := (scanOption <> soUnknownValue);

// (2) this block was truncated by buffersize — memscan.pas:5836-5839
if (size > buffersize) then
begin
  size := buffersize;
  canOverlap := (scanOption <> soUnknownValue);
end;

// consumption point — memscan.pas:5844-5847
if canOverlap then _size := size + (variablesize-1) else _size := size;
// on a short read there is one more retry "without overlap" — 5853-5863
```

**Consequence**: First Scan does **not** always read `buffersize + (pattern_length - 1)`. A scanner's **first region** (`i = startregion`, 5810-5814) never sets `canOverlap`, so unless that region is large enough to be sliced by `buffersize`, an AOB straddling the end of that region is missed.

### Buffer allocation

Uses `VirtualAlloc` (not the ordinary heap) to obtain page-aligned memory:

```pascal
memorybuffer := VirtualAlloc(nil,
  maxregionsize + variablesize + 16,
  MEM_COMMIT or MEM_RESERVE or MEM_TOP_DOWN,
  PAGE_READWRITE);
```

### Result buffering

- `CurrentAddressBuffer`: pre-allocated address array
- `CurrentFoundBuffer`: pre-allocated matched-value array
- Flushed to disk when full (`ADDRESSES.<name>` + `MEMORY.<name>` files)
- `Tscanfilewriter` writes using **double buffering**: I/O is written out asynchronously while the scan continues
- `TFoundList` lazy-loads results (1024-entry sliding cache, `RebaseAddresslist` foundlisthelper.pas:319, window computed at 328-336); `FindClosestAddress` (foundlisthelper.pas:460) indexes **at most 100 sample points** with a `TAvgLvlTree` (table built at 470-487, `if fcount>100 then step:=fcount div 100`), then linearly scans forward from the nearest sample point (499-500) — this is **not** a full address index

---

## 6. Threading Model

### First Scan

```
TScanController
  ├── TScanner[0]: regions 0 ~ N/4
  ├── TScanner[1]: regions N/4 ~ N/2
  ├── TScanner[2]: regions N/2 ~ 3N/4
  └── TScanner[3]: regions 3N/4 ~ N
```

- Thread count = `threadcount` (default = `GetCPUCount`, i.e. the number of bits in the process affinity mask). **But there are several exceptions that force it down to 1**, of which `OnlyOne` (AutoAssembler's `AOBSCAN`) is the most common — full conditions in §14.1 and §7
- Memory regions are divided evenly among the threads
- Each thread reads, scans and writes out results independently
- The main thread waits by polling `WaitTillDone(25)` + updating the GUI

### Next Scan (rescan)

- Scans from the address list of the previous result
- Reads in batches on **4K page boundaries** (fewer `ReadProcessMemory` calls)
- Still multithreaded, but each thread handles one address range

### Thread synchronisation

```pascal
// TScanController waits for all TScanners to finish
for i := 0 to threadcount - 1 do
begin
  while not (terminated or scanners[i].isdone or scanners[i].Finished) do
  begin
    scanners[i].WaitTillDone(25);  // 25ms polling
    if progressbar <> nil then
      synchronize(updategui);
  end;
  inc(OwningMemScan.found, scanners[i].totalfound);
end;
```

---

## 7. Early Termination

### OnlyOne mode

`OnlyOne` has **three** exit points, each at a different granularity:

```pascal
// (1) TScanner.FirstScanmem inner loop — memscan.pas:4004-4008 (generic) / 3985-3989 (vtCustom)
if OnlyOne then
begin
  AddressFound:=base+ptruint(p)-ptruint(buffer);
  exit;
end;

// (2) the chunk loop of TScanner.firstscan — memscan.pas:5916
//     note this line sits *after* firstscanmem returns (5903), so it stops only once the
//     whole chunk has been scanned, not halfway through
if (OnlyOne and (found>0)) then exit;

// (3) TScanner.execute — the winner terminates the other scanners — memscan.pas:5982-5988
if OnlyOne and (AddressFound<>0) then
  for i:=0 to length(OwningScanController.scanners)-1 do
    OwningScanController.scanners[i].Terminate;
```

AutoAssembler's `AOBSCAN` / `AOBSCANMODULE` **enable `OnlyOne` by default** (autoassembler.pas:1290 `memscan.OnlyOne:=true;`): they stop at the first hit and do **not** check that there is exactly 1 (945-956 only does a range check).

"Exactly one" is a different flag, `isUnique` (memscan.pas:782 `property isUnique: boolean read fIsUnique write fIsUnique; //for AOB scans only`), set by Lua `AOBScanUnique` / `AOBScanModuleUnique` (LuaHandler.pas:4121 passes `true` as the last argument).

**Side effect**: `OnlyOne and (not isUnique)` drops FirstScan to a single thread (memscan.pas:7021-7022), so a plain `AOBSCAN` is in fact a single-threaded scan; AA's parallelism comes from "running several modules' `TMemScan` at the same time" (autoassembler.pas:1276-1283, capped at `GetCPUCount`).

### MaxResults limit

AOBMaker's `WindowsMemoryScanner` implements `MaxResults` early termination:

```csharp
// WindowsMemoryScanner.cs:188 — the counter is Interlocked.Increment(ref matchCount),
// not results.Count (ConcurrentBag.Count is neither cheap nor immediate under parallelism)
if (maxResults > 0 && Interlocked.Increment(ref matchCount) >= maxResults)
{
    Interlocked.Exchange(ref earlyStop, 1);
    loopState.Stop();
    return;
}
```

The `maxResults > 0 &&` prefix is mandatory (`MaxResults` defaults to 0 = unlimited); see §15.3.

**What this means for Mode D**: deciding uniqueness only needs `MaxResults = 2` — stop as soon as the 2nd hit is found.

---

## 8. SimpleAOBScanner (`simpleaobscanner.pas`)

`simpleaobscanner.pas` is a lightweight wrapper for **the Lua `AOBScan*` family and other units**; it is **not** the implementation of AutoAssembler's `AOBSCAN`. AA goes through `autoassembler.pas:899 aobscans()`, builds its own `TMemScan` (1289-1290 `OnlyOne:=true`) and calls `TMemScan.firstscan` directly (1297/1299), merging multiple AOBs for the same module into a single `vtByteArrays` scan.

This unit **has no class** — there is no `TSimpleAOBScanner` anywhere in the CE tree, and no `TQWordArray` either (the latter only exists in gdbserverdebuggerinterface.pas:60). The whole file is 149 lines, and the interface declares only three free functions:

```pascal
// simpleaobscanner.pas:14-16
function findaobInModule(modulename, aobstring: string; protectionflags: string='';
  alignmenttype: TFastScanMethod=fsmNotAligned; alignmentparam: string='';
  isUnique: boolean=false): ptruint;
function findaob(aobstring: string; protectionflags: string='';
  alignmenttype: TFastScanMethod=fsmNotAligned; alignmentparam: string='';
  isUnique: boolean=false): ptruint;
function getaoblist(aobstring: string; list: tstrings; protectionflags: string='';
  alignmenttype: TFastScanMethod=fsmNotAligned; alignmentparam: string=''): boolean;
```

The API functions and their real callers:

| Function | Located at | Callers | `OnlyOne` |
|------|------|--------|-----------|
| `findaobInModule(modulename, aobstring, …)` | simpleaobscanner.pas:136 | Lua `AOBScanModuleUnique` (LuaHandler.pas:4082, called at 4121, registered at 16495) | Yes — set unconditionally by `AsyncAOBScan` (91) |
| `findaob(aobstring, …)` | simpleaobscanner.pas:143 | Lua `AOBScanUnique` (LuaHandler.pas:4137, registered at 16494), gnuassembler.pas:268, dbvmdebuggerinterface.pas:517 | Yes — forwards to `findaobInModule('', …)` (145) |
| `getaoblist(aobstring, list, …)` | simpleaobscanner.pas:23 | Lua `AOBScan` (LuaHandler.pas:4155, called at 4213, registered at 16493) | **No** — never sets `onlyone`, collects every result (34-58) |
| `AsyncAOBScan(modulename, aobstring, …): TMemScan` | simpleaobscanner.pas:78, **implementation-only, not exported** | only `findaobInModule` (139) | **unconditionally true** (91 `ms.onlyone:=true;`) |
| `FinishAOBScan(ms: TMemscan): ptruint` | simpleaobscanner.pas:124, **implementation-only, not exported** | only `findaobInModule` (140) | N/A — calls `ms.GetOnlyOneResult` (130) |
| AA `AOBSCAN` / `AOBSCANMODULE` / `AOBSCANEX` / `AOBSCANREGION` | autoassembler.pas:899 `aobscans()` | AutoAssembler preprocessing | Yes (1290); `vtByteArrays` when there are several AOBs (1299) |

⚠ **`AsyncAOBScan` accepts `alignmenttype` / `alignmentparam` and then never uses them**: simpleaobscanner.pas:78 takes both parameters and `findaobInModule` (136) faithfully passes them down (139), but the actual scan call at simpleaobscanner.pas:120 hardcodes `fsmNotAligned` and does not pass `_fastscanparameter` at all. So the 4th and 5th parameters of Lua `AOBScanUnique` / `AOBScanModuleUnique` (parsed at LuaHandler.pas:4104-4112, passed at 4121) are **dead parameters**; only `getaoblist` (58) actually hands them to `firstscan`.

Characteristics:
- Wraps `TMemScan` and then calls `firstscan(soExactValue, vtByteArray, ...)`. `onlyone := true` is **set only by `AsyncAOBScan`** (91), i.e. only for the `findaob*` family; `getaoblist` (23-76) creates its TMemScan at 34 and never touches `onlyone` — it deliberately collects everything (loop 64-68).
- `findaobInModule` gets the module base+size through `symhandler.getmodulebyname()` and bounds the scan range with it
- **`OnlyOne` forces single-threading**: `if (OnlyOne and (not isUnique)) … then threadcount:=1` (memscan.pas:7021-7022). So `findaob(...)` (isUnique=false, e.g. gnuassembler.pas:268) really is a single TScanner; only the `*Unique` variants (isUnique=true) keep `GetCPUCount` threads, giving each scanner its own `OnlyOne` (7472), with 5982-5988 letting the winner terminate the rest.
- `OnlyOne` / `isUnique` scans skip the "save the First Scan results" step (memscan.pas:7680 `savescannerresults:=false; //DO NOT INTERFERE`, and 7868 skips `ADDRESSES.First` / `MEMORY.First`), but **each scanner still writes its own address/memory file** (`TScanfilewriter`, 5959). `getaoblist` goes further and explicitly **reads the results back from disk** (simpleaobscanner.pas:62 → `TFoundList.Initialize` opens `ADDRESSES.<listname>`, foundlisthelper.pas:724+).
- Suited to small-range scans (a single module)

---

## 9. Fast Scan Alignment

```pascal
// TScanner properties
fastscanalignsize: integer;   // alignment size (1, 2, 4, 8, 16)
fastscanmethod: TFastScanMethod;
```

| Method | Notes |
|------|------|
| `fsmNotAligned` | Byte-by-byte scan (align=1) |
| `fsmAligned` | Scan every `alignsize` bytes (AOB default align=1) |
| `fsmLastDigits` | Alignment decided by the last digits of the value (for numeric scans) |

AOB scans normally use `align=1` (unaligned), because machine code is not guaranteed to be aligned.

---

## 10. Performance Characteristics

### CE scan speed reference ⚠ NOT verified by measurement

> The table below is **not** a fact derived from the CE source, and it carries no measurement conditions — **do not treat it as a benchmark.** The only quantitative numbers in the CE tree are the cycle comments at memscan.pas:4450-4452 (a 4-byte read ≈4000 cycles, a 4096-byte read ≈6500, a 2048-byte read ≈5900); there is no benchmark harness anywhere in the tree.
>
> To turn the table below into a real measurement, every row would need at minimum: CPU model and core count (`GetCPUCount` takes the **number of bits in the process affinity mask**, CEFuncProc.pas:2998), the `buffersize` setting (default 512 KB, not 4 MB), the fast-scan alignment value (GUI default `4`), whether `Active memory only` is ticked, the target process's total commit and region count, and the `OnlyOne`/`isUnique` state (`OnlyOne` forces single-threading, memscan.pas:7021).

| Scenario | Time once observed (**source unknown, needs re-measuring**) |
|------|----------|
| 50,000 matches (aobscan over all modules) | < 1 second |
| First scan of a 2GB process space | 2-5 seconds |
| `aobscanmodule` on a single 100MB module | < 0.5 seconds |

### Bottleneck analysis

| Bottleneck | CE approach | AOBMaker approach |
|------|---------|---------------|
| `ReadProcessMemory` syscall | Batched reads in `buffersize` chunks (default **512 KB**, adjustable in Settings; CEFuncProc.pas:296 / MainUnit2.pas:627-635) | 64MB chunk (`MaxRegionSize`) |
| Pattern matching | Linear scan + exit at the first mismatching position (no first-byte shortcut) | SIMD anchor-byte location + full verification with `MatchPatternNibble` |
| Multithreading | One TScanner per CPU core (= number of bits in the process affinity mask, CEFuncProc.pas:2998); but `OnlyOne` (AA AOBSCAN), Lua formula and Lua custom type all force it down to 1 (memscan.pas:7021-7030), and small-range scans have an additional 4 KB/thread floor (7351-7356) | `Parallel.ForEach` (Max=CPU cores) |
| Result count | Collect everything + spill to disk | `MaxResults` early termination (**0 = unlimited**, see §15.3) |
| Region filtering | Module range limiting | `ExecutableOnly` + region type filtering |

### The 0-match problem

If the pattern does not exist in memory (e.g. a wrong address), the scan has to walk every region before it can confirm 0 hits. This is the slowest case:

- CE: also has to scan everything, but multithreading + the 512 KB buffer keep the latency manageable
- AOBMaker: `MaxResults=2` cannot terminate early → a full walk of every region

---

## 11. Differences Between AOBMaker and CE

| Aspect | CE | AOBMaker |
|------|-----|---------|
| Language | Free Pascal / Lazarus (`{$MODE Delphi}`, **not** Embarcadero Delphi) | C# (.NET) |
| Pattern source | User-entered hex string | Iced.NET instruction analysis + register masking |
| Wildcard | `??` (full) + nibble (`?X`) | `??` (full) + nibble (`4?` / `?1`) — all routed through the single chokepoint `AOBMaker.Core/Services/CeAobGrammar.cs` (four masks `0x00`/`0x0F`/`0xF0`/`0xFF`); a malformed token always throws instead of being silently treated as a wildcard |
| Scan range | Module-limited or whole process | `ExecutableOnly` + MEM_IMAGE/PRIVATE/MAPPED type filtering (`IsValidRegion`, WindowsMemoryScanner.cs:315-334; defaults image=true / private=true / mapped=false, MemoryScanOptions.cs:9/12/15); `WindowsModuleScopedScanner` additionally does module-limited scanning |
| Result staging | Disk files (supports millions of entries) | In-memory `ConcurrentBag<ulong>` |
| SIMD acceleration | None (zero hits for asm/movdqa/pcmpeq/xmm/ymm inside memscan.pas; the only SSE lines, 5943-5948, are commented out) | `Vector256` / `Vector128` anchor-byte scanning + scalar fallback |
| First-byte optimisation | No dedicated first-byte comparison; only the `ArrayOfByteExact` byte-by-byte loop `exit`ing at the first mismatch (memscan.pas:2585-2590) | `FindAnchorOffset` takes the first **fully literal** byte as the anchor, `Vector256.Equals` finds 32 candidates at once, then `MatchPatternNibble` verifies in full |
| Buffer size | `buffersize` global variable, default **512 KB**, user-adjustable (CEFuncProc.pas:296 / MainUnit2.pas:627-635) | 64MB for AOB scanning (`WindowsMemoryScanner`); 16MB for sequence scanning, 64KB under `AntiCheatSafeMode` (`WindowsSequenceScanner`) |
| Mode D optimisation | N/A | Precomputed token array + byte-by-byte expansion |

> ⚠ "CE has nibbles, AOBMaker only has `??`" and "neither side has SIMD" were errors in earlier versions of this document, and they were **already wrong when they were written** (not something that went stale later): AOBMaker's live scanner has parsed nibble wildcards since day one. §15 and §16.4 were always right; it was this table that had not kept up.

---

## 12. Possible Improvements

Based on what the CE analysis suggests, AOBMaker's `WindowsMemoryScanner` could consider the following optimisations:

> ⚠ **This section has been overtaken by §15 and is kept as a historical record.** §12.1 (first-byte skip) and §12.2 (SIMD) are **both already implemented** in today's `WindowsMemoryScanner.cs`: `FindAnchorOffset` (:256-265) + the `Vector256`/`Vector128` anchor scan (:172-229) *are* those two items — details in §15.2. §12.3 is also marked as applied. The pseudo-code in all three subsections **differs from the current implementation** (in particular §12.1 assumes the anchor is always at offset 0); treat §15.2 as authoritative.

### 12.1 First-Byte Skip

If the pattern's first byte is not a wildcard, you can use `IndexOf` or a similar fast search to find candidate positions first:

```csharp
// quickly skip non-matching first bytes
byte firstByte = pattern[0];
while (i <= maxIndex)
{
    if (buffer[i] != firstByte) { i++; continue; }
    // then compare the full pattern...
}
```

### 12.2 SIMD Acceleration

.NET's `System.Runtime.Intrinsics` can compare 16/32 bytes at a time with SSE2/AVX2:

```csharp
// use Vector128/Vector256 to quickly locate the first byte
var needle = Vector256.Create(firstByte);
// advance 32 bytes at a time looking for candidate positions
```

### 12.3 N=2 Early Termination

Mode D only needs to know whether the count is 0, 1 or ≥2 to decide uniqueness:

```csharp
ScanOptions.MaxResults = 2;  // stop once 2 hits are found
```

This optimisation is already applied in the current implementation.

---

## 13. CE Source Reference Files

> **The line counts are DERIVED values — do not hand-edit them.** Re-run this whenever the checkout changes:
>
> ```bash
> cd "D:/Github/cheat-engine/Cheat Engine" && wc -l memscan.pas simpleaobscanner.pas \
>   foundlisthelper.pas NewKernelHandler.pas autoassembler.pas LuaMemscan.pas \
>   parsers.pas commontypedefs.pas
> ```
>
> The table below was measured at CE tag `7.5-195` / HEAD `4178e037`. **Historical lesson**: the source block at the top of this document used to carry its own copy of the line counts (`memscan.pas` ~8964, correct), which contradicted this table (~8000, wrong) inside the same document — because the two places had each been hand-edited separately. The line counts are now held in exactly one place, this table, and the top of the document only points here.
>
> ⚠ This repo's CI gate `tools/check_derived_counts.py` does **not** cover any of the numbers in this document, because it only derives from the UE5CEDumper tree while every number here comes from the external CE / AOBMaker trees — so they must be re-derived by hand with the commands given.

| File | Lines | Contents |
|------|------|------|
| `memscan.pas` | 8964 | `Tscanfilewriter`(128) / `TScanner`(165) / `TScanController`(518) / `TMemScan`(626) — the complete scan engine |
| `simpleaobscanner.pas` | 149 | The three free functions `findaobInModule` / `findaob` / `getaoblist` (for Lua `AOBScan*`, **not** AA's AOBSCAN) |
| `foundlisthelper.pas` | 935 | `TFoundList` — result lazy-load (1024-entry window 328-336) and address lookup (`FindClosestAddress` 460-500). **No sort method**; the results are already in ascending address order (each scanner owns one contiguous address range, and the controller concatenates them by thread index) |
| `NewKernelHandler.pas` | 2593 | `VirtualQueryEx`(1498) / `ReadProcessMemory` wrappers; `VirtualQueryEx_StartCache` is a stub on Windows (1515) |
| `autoassembler.pas` | 4685 | `aobscans()`(899) — `AOBSCAN` / `AOBSCANMODULE` / `AOBSCANEX` / `AOBSCANREGION` preprocessing, builds its own `TMemScan` |
| `LuaMemscan.pas` | 333 | `memscan_*` Lua bindings + the `TMemScan` metatable (`memscan_addMetaData` 296, `luaclass_register` 329). **`createMemScan` is not here** — it is at `LuaHandler.pas:5121` (registered at 16626) |
| `parsers.pas` | 584 | `ConvertStringToBytes`(297, interface 14) — AOB pattern parsing and nibble-wildcard encoding (341-364) |
| `commontypedefs.pas` | 142 | `TScanOption`(12), `TVariableType`(15), `TFastScanMethod`(20), `TBytes`(28). `Tscanregionpreference` is instead in memscan.pas:46 |

---

## 14. CE Numeric / Value Scan Path (2026-05-28 expansion)

§1–13 focus on AOB pattern matching. Numeric scanning (`soExactValue` + `vtDWord`/`vtSingle`…) goes through the same `TScanner` pipeline, but has a few details that matter most for "fast". The line numbers below refer to `memscan.pas`.

### 14.1 Threading model — this is the main reason CE is fast

> ⚠ The line numbers in earlier versions of this section (6637 / 6678 / 6689–6753) all landed in **`TScanController.FirstNextScan`** (memscan.pas:6622), which is the next-scan-over-regions path used *after* an unknown-initial-value first scan — **not** FirstScan. `memscan.pas` contains three almost identical split/join implementations: `NextNextScan`(6439) / `FirstNextScan`(6622) / `firstScan`(6978), and it is very easy to read the wrong one. The real FirstScan is at 6978.

```pascal
// TScanController.firstScan — memscan.pas:6978
if (OnlyOne and (not isUnique)) or (luaformula and (newluastate=false)) then
  threadcount:=1                       // 7021-7022 ← AutoAssembler's AOBSCAN takes this branch
else
  threadcount:=GetCPUCount;            // 7024 — = number of bits in the process affinity mask (CEFuncProc.pas:2998)

if totalProcessMemorySize<threadcount*4096 then   // 7351-7356 drop thread count for small-range scans
  threadcount:=1+(totalProcessMemorySize div 4096);

Blocksize:=totalProcessMemorySize div threadcount;       // 7361
if (Blocksize mod 4096) > 0 then
  Blocksize:=blocksize-(blocksize mod 4096);             // 7362-7363 align to 4K pages
```

- FirstScan uses `Blocksize` to **divide the memory regions evenly among N `TScanner`s** (**7376–7443**); each thread reads/scans/writes results independently, and the last one scans to the end.
- Exceptions that drop it to a single thread: `OnlyOne and (not isUnique)` (7021, **AutoAssembler's AOBSCAN takes this branch**), Lua formula without a new state (7021), and custom Lua type (7029-7030).
- The `scannersCS` critical section protects the creation of the scanner array (**7369–7502**).
- `GetCPUCount` is itself `getbitcount(ProcessAffinityMask)` (CEFuncProc.pas:2998-3015, cached in `_CPUCOUNT`) — it is the **number of logical processors inside the affinity mask**, not the physical core count.
- The main thread polls `WaitTillDone(25)` every 25 ms, updates the GUI and then joins (6580 / 6812 / 7520).
- The other structurally identical path, `TScanController.FirstNextScan` (6622): threadcount 6637, Lua exceptions 6639/6643, Blocksize 6677-6678, splitting 6689–6753, `scannersCS` 6682–6795.
- NextScan (rescan) `TScanController.NextNextScan` (6439): threadcount 6455-6459, but the split is by **result count** instead — `blocksize := totaladdresses div threadcount` (6488).

### 14.2 Fast-scan alignment — numeric scans skip 3/4 of the positions

```pascal
// TScanController.fillVariableAndFastScanAlignSize — memscan.pas:6117, case at 6128-6163
case variableType of
  vtByte:   fastscanalignsize := 1;   // 6131  (variablesize 1)
  vtWord:   fastscanalignsize := 2;   // 6137  (variablesize 2)
  vtDWord:  fastscanalignsize := 4;   // 6143  (variablesize 4) ← default for a 4-byte int
  vtQWord:  fastscanalignsize := 4;   // 6149  (variablesize 8) ← note: 4, not 8
  vtSingle: fastscanalignsize := 4;   // 6155  (variablesize 4)
  vtDouble: fastscanalignsize := 4;   // 6161  (variablesize 8) ← note: 4, not 8
end;
// inner loop: stepsize := fastscanalignsize; inc(p, stepsize);  // 3887 / 4011
```

(In the source `vtQWord` comes **before** `vtSingle`; earlier versions of this document had the two line numbers 6149/6155 swapped.)

`fsmAligned` (**the GUI default**; the parameter default of `TMemScan.firstscan` is actually `fsmNotAligned`, memscan.pas:750) strides by `alignsize` → a DWord scan only compares 1/4 of the addresses. Only `fsmNotAligned` uses stride=1.

**But the type table above is only a fallback, and it is normally overwritten.** Immediately after the case block:

```pascal
// memscan.pas:6286-6290
if fastscan then //override the alignment if given
begin
  if (fastscanmethod=fsmLastDigits) or (fastscanalignment<>0) then
    fastscanalignsize:=fastscanalignment;
end;
```

And `fastscanalignment` is parsed **as hexadecimal** out of `TMemScan.firstscan`'s `_fastscanparameter`, falling back to **1** for an empty string:

```pascal
// memscan.pas:8611-8616
if fastscanparameter<>'' then
  self.fastscanalignment:=strtoint('$'+fastscanparameter)
else
  self.fastscanalignment:=1;
self.fastscandigitcount:=length(fastscanparameter);
```

The GUI always passes `edtAlignment.Text` (MainUnit.pas:10485), whose `.lfm` default is `'4'` (MainUnit.lfm:500), and which is rewritten automatically when the type is switched (MainUnit.pas:7107-7126).
→ **The actual stride is the user's alignment field**; a programmatic call passing `''` makes every type (including DWord) stride 1.

### 14.3 The numeric comparison inner loop — scalar, no SIMD

```pascal
function TScanner.DWordExact(newvalue, oldvalue: pointer): boolean;
begin
  result := pdword(newvalue)^ = dword(value);   // line 3108 — a single load + CMP
end;
```

CE's numeric comparison is the plainest possible **pointer dereference + `=`**, with no vectorisation at all. Its speed comes from "alignment stride + multithreading", not from the inner instruction.

### 14.4 Buffer / read strategy

- The result buffer is sized per type as `FoundBufferSize := buffersize * varsize` (memscan.pas:5046/5084/5124/5164/5202/5236; `vtAll` uses `maxfound*variablesize`, 5332; for `buffersize` see §5). **The 16 MB ceiling only applies to `vtCustom`** (branch opens at 5369, clamp at 5372-5375); **built-in types have no ceiling.** `CurrentFoundBuffer` + `SecondaryFoundBuffer` are a double buffer allocated with `getmem` (heap, not VirtualAlloc) (5483–5484), swapped in `genericFlush`(3678–3691, swap at 3682-3687) / `allFlush`(3714–3727), while `Tscanfilewriter` (class declared at 128, thread body `execute` 3731, `writeresults` 3786) writes to disk asynchronously (`ADDRESSES.TMP` / `MEMORY.TMP`).
- The old-value buffer is allocated with `VirtualAlloc(MEM_TOP_DOWN)` (5560/5581).
- **NextScan batches on 4K pages**: addresses on the same page are gathered into a single `ReadProcessMemory` (`TScanner.nextnextscanmem` **4446–4562**, page mask `and qword($FFFFFFFFFFFFF000)` at 4508/4515, 4 KB stack buffer 4463, batched read 4532) → far fewer syscalls. The structurally identical siblings are `nextnextscanmemAll` (4205–4363) and `nextnextscanmembinary` (4364–4445). The reasoning is in CE's own comment at 4448–4454: a 4 KB read ≈6500 cycles, a 4-byte read ≈4000 cycles.

### 14.5 Other accelerations

- `VirtualQueryEx_StartCache / EndCache` (call sites memscan.pas:7095 / 7290) cache region metadata. **But on a native Windows target this is a no-op stub** — NewKernelHandler.pas:1515-1518 `result:=false;  //don't use it in windows`, and the unit's `initialization` always installs the stub (2406-2407). The only real implementation is for the network/ceserver target (networkInterfaceApi.pas:386-387 → networkInterface.pas:1334), where what it saves is a **network round trip**, not a local syscall. The three bits of `vqecacheflag` are at 7085-7092 (`VQE_NOSHARED` / `VQE_PAGEDONLY` / `VQE_DIRTYONLY`).
- `workingsetonly` + `QueryWorkingSet` (7099–7165): scan only physically resident pages, avoiding page faults. **Off by default**, enabled by the main window's `Active memory only` checkbox (MainUnit.pas:10481; MainUnit.lfm:573-587 has no `Checked = True`), and forced to false for networked targets. When enabled, memregions are accumulated page by page in units of 4096 (7131–7152) rather than being VirtualQuery regions.
- `OnlyOne` exits as soon as it finds one (3985–3989, 4004–4008; chunk level additionally at 5916, cross-scanner termination at 5982–5988, see §7).
- Region filtering (7188–7242): `MEM_COMMIT` only, excludes `PAGE_GUARD`/`PAGE_NOACCESS`; `MEM_PRIVATE`/`MEM_IMAGE`/`MEM_MAPPED` plus writable/executable are all settable from the UI.

---

## 15. AOBMaker SIMD Scanner (C#/.NET, 2026-05-28 expansion)

`WindowsMemoryScanner.cs` is a cross-process (RPM) scanner, but its **SIMD + parallelisation** design is worth borrowing from.

> The line numbers in this section refer to **AOBMaker HEAD `fbc12d3`**; `WindowsMemoryScanner.cs` is **455 lines** (a derived value: `wc -l`). Earlier versions of this document were consistently off by 1 — commit 6dfa2dd inserted `using AOBMaker.Core.Services;` at line 9 of that file, shifting everything below it by +1. If the numbers shift wholesale again, look for that kind of single-line insertion first.

### 15.1 Parallelisation + chunking

```csharp
private const int MaxRegionSize = 64 * 1024 * 1024;  // :51 — 64 MB chunk
// large regions are first split into 64MB chunks (297–303), then:
Parallel.ForEach(regions, new ParallelOptions {
    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)   // :127
}, ...);
```

- The unit of work is a `MemoryRegion` (per-region parallelism). `ArrayPool<byte>.Shared.Rent(region.Size)` (:154) borrows the buffer to avoid GC, and `finally` returns it (:252).
- `AntiCheatSafeMode` is **not in this file** — it is an option of `WindowsSequenceScanner.cs` (declared at `SequenceScanRequest.cs:70`): `maxThreads = 1` (:137, fed to the `MaxDegreeOfParallelism` at :191), and the chunk shrinks from the normal **16 MB** (`MaxRegionSize`, :45) to **64 KB** (`SafeModeRegionSize`, :46; picked by the ternary at :1154). The presumed intent is to keep the size of any single `ReadProcessMemory` inconspicuous (the source gives no reason); in any case, writing it as "safe mode = 16 MB" swaps those two constants. `WindowsMemoryScanner` itself has **no** safe mode — it always chunks at 64 MB.

### 15.2 SIMD comparison: anchor byte + Vector256/128

```csharp
// FindAnchorOffset(): finds the pattern's first **fully literal** byte (nibbleMask == 0xFF) to use as the anchor (256–265)
var needle = Vector256.Create(anchorByte);                                  // :174
int simdMaxI = Math.Min(maxIndex, bytesRead - anchorOffset - Vector256<byte>.Count); // :175
var chunk  = Vector256.LoadUnsafe(ref buffer[0], (nuint)(i + anchorOffset));// :179 — the (nuint) cast is mandatory, it will not compile without it
uint bits  = Vector256.Equals(chunk, needle).ExtractMostSignificantBits();  // :180
while (bits != 0) {
    int cand = i + BitOperations.TrailingZeroCount(bits);       // :184 — note you must add i back
    if (cand <= maxIndex &&                                     // :185 — tail bound; without it you read out of bounds
        MatchPatternNibble(buffer, cand, pattern, nibbleMask))
        results.Add(region.BaseAddress + (ulong)cand);          // :187
    bits &= bits - 1;                                           // :195 clear the lowest set bit
}
```

⚠ The `i +` at `:184` and the `cand <= maxIndex` at `:185` are **the two things most easily lost in a port** (earlier versions of this document lost both). §16 explicitly proposes porting this inner loop into discrete (§16.4 "inner-loop SIMD → P2"), so it is a **source for re-implementation**, not decoration:

- Without `i +`, the candidate becomes an "offset within the vector" of 0–31, and a faithful port would only ever report hits inside the first 32 bytes of each region.
- Without `cand <= maxIndex`, `MatchPatternNibble` can read past `bytesRead`. The SIMD window `simdMaxI` (:175) is computed from the **anchor position**, so a set bit at the end of the window can still produce a candidate whose full pattern would run past the end.

Other points:

- 32 bytes are compared at once to find anchor candidates, and each candidate is then fully verified by `MatchPatternNibble` (**268–277**): `(buffer[off+j] & m) == (pattern[j] & m)`, a per-byte AND-mask. **The mask constants are not in this file**, they are in the shared grammar `AOBMaker.Core/Services/CeAobGrammar.cs:30-39` (`0x00`/`0x0F`/`0xF0`/`0xFF`) — the project has a structural test forbidding production code outside `CeAobGrammar` from referencing the partial-mask constants. There is a `Vector128` fallback (201–229) and a scalar fallback (232–244).
- **The anchor only accepts a complete byte**: the test in `FindAnchorOffset` (256–265) is `nibbleMask[k] == 0xFF`, not "not a wildcard". A nibble token (`4?` → 0xF0, `?1` → 0x0F) is not a wildcard, but still **cannot** be an anchor (it cannot be broadcast with `Vector256.Create`). So the anchor for `4? 8B 05 ??` is the `8B` at offset 1, not offset 0; and when a pattern consists solely of nibbles/wildcards, `FindAnchorOffset` returns -1, `anchorOffset >= 0` (:168) fails, and it drops straight into the scalar loop (232–244) with **no SIMD at all**.
- `[MethodImpl(AggressiveInlining)]` on the matcher (:267) and on the anchor finder (:256).

### 15.3 Early termination + result collection

```csharp
// atomic count; once the target is reached, every thread stops
if (maxResults > 0 && Interlocked.Increment(ref matchCount) >= maxResults) {
    Interlocked.Exchange(ref earlyStop, 1);  loopState.Stop();  // :188–193
}
// other threads check before entering a region — :131–135
if (maxResults > 0 && Volatile.Read(ref earlyStop) != 0) { loopState.Stop(); return; }
ConcurrentBag<ulong> results;                                   // :116 — lock-free
```

⚠ **The `maxResults > 0 &&` prefix cannot be dropped.** Every real call site has it (:131, :188, :217, :237). `MemoryScanOptions.MaxResults` is `public int MaxResults { get; set; }` (MemoryScanOptions.cs:21) **with no initial value, so the default is 0 = unlimited**. Without the prefix, `1 >= 0` is always true → a caller asking for "unlimited" instead **stops at the very first hit**, and does so as silently wrong data.

The `WindowsSequenceScanner` (**note the class name**: there is no type called `SequenceScanner`) variant keeps its thread-locals as `ConcurrentBag<List<ulong>[][]>` (per-thread × per-plan × per-value, :173 / :196-201), merged and sorted by `MergeHitLists` (:391-413) once the scan finishes. Saturation protection is a **per-value hit cap** (`DefaultPerValueHitCap = 10_000_000`, :53; divided `/ planCount` when auto-detecting the type, :168), and reaching it only stops appending, not the whole scan (the comment at :206 reads "saturation only stops appending (inside the kernel), not the whole scan"). There is one trap already hit during merging: `ConcurrentBag`'s enumeration order is non-deterministic and empty thread-locals often come before the ones with hits, so an empty list must `continue` rather than `break` (:402-404).

---

> **§16 is deliberately not mirrored.** Section 16 of the master copy documents a different project's in-process scanner — its current architecture, bottleneck analysis, acceleration roadmap and build numbers — none of which is about CE or about UE5CEDumper. Read it in the master copy: `<private-ce-repo>/docs/Memory-Scanning-Internals.md`.
