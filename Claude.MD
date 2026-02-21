# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

-----

## Build & Dev Commands

### C++ DLL (dll/)

The DLL build uses CMake + Ninja + MSVC. Locate Visual Studio toolchain via `vswhere`, not hardcoded paths.

```bash
# Find VS install path (use in scripts)
vswhere -latest -property installationPath

# Configure (from repo root)
cmake -S . -B build -G Ninja -DCMAKE_BUILD_TYPE=Release

# Build
cmake --build build --config Release

# Output: build/UE5Dumper.dll
```

### C# Avalonia UI (ui/)

```bash
# Restore packages
dotnet restore ui/UE5DumpUI.csproj

# Build
dotnet build ui/UE5DumpUI.csproj -c Release

# Run
dotnet run --project ui/UE5DumpUI.csproj

# Run tests
dotnet test ui/UE5DumpUI.Tests/UE5DumpUI.Tests.csproj

# Publish single-file exe
dotnet publish ui/UE5DumpUI.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### Git Submodules

```bash
git submodule update --init --recursive
```

-----

## Rules (MUST follow)

- **Language**: Code comments and UI strings in English
- **Single Instance**: UI app uses Mutex to ensure only one instance runs
- **Async everywhere**: All I/O, pipe operations, and alert actions must be async
- **Platform Abstraction**: Any system/OS-dependent call (P/Invoke, Registry, OS commands) MUST go through an interface in `Core` project. `Core` must NEVER contain direct platform-specific code
- **Log output**: All logs go to `%APPDATA%\UE5CEDumper\Logs`, 5-version rotation, 5MB max per file. Each app/DLL start triggers a rotate
- **Magic words management**: All magic strings kept in one file per project with proper comments
- **UI Strings**: English only. All UI strings in `Resources/Strings/en.axaml`, referenced via `StaticResource` bindings
- **UE offsets**: All UObject/UStruct offsets must be dynamically verified via OffsetFinder, never hardcoded

-----

## Implementation Status & Challenges Log

> Last updated: 2026-02-21 (FF7R flat array fix + UI search indicator + 10-game test results)
> Latest commit: `362c0ae` on `dev` branch (uncommitted: DetectStride + UProperty fallback)

### Component Status

| Component | File(s) | Status | Notes |
|-----------|---------|--------|-------|
| Memory module | `Memory.cpp/.h` | Done | AOBScan (exec sections only), ResolveRIP, ReadSafe (SEH) |
| Logger | `Logger.cpp/.h` | Done | Dual-channel (scan+pipe), category tags, SUMMARY level, 5-version rotation, 5MB cap, thread-safe; per-process mirror logging (subfolder per process name, 2-version rotation, max 20 subfolders) |
| AOB Signatures | `Signatures.h` | Done | 51 patterns + 4 symbol exports from 6 sources (original, patternsleuth, RE-UE4SS, Dumper-7, UE4 Dumper.CT, UEDumper) |
| OffsetFinder — GObjects | `OffsetFinder.cpp` | Working | 27 AOB patterns + deref variants + MSVC symbol export (`GUObjectArray`); validated in TQ2 (UE5.7) |
| OffsetFinder — GNames | `OffsetFinder.cpp` | Partial | 17 AOB patterns + FName ctor export + `.data` pointer-scan + UE4 TNameEntryArray validator; unified `ValidateGNamesAny` tries FNamePool→Structural→UE4; structural validator hardened with exact header format + corroboration |
| OffsetFinder — GWorld | `OffsetFinder.cpp` | Done | 7 patterns, non-critical |
| OffsetFinder — Version | `OffsetFinder.cpp` | Working | PE VERSIONINFO (ProductVersion + FileVersion) + 3-tier memory string scan (++UE prefix → Release context → bare pattern with digit guard) |
| OffsetFinder — DynOff | `OffsetFinder.cpp`, `Constants.h` | Untested | Dynamic FField/FProperty/UStruct offset detection via known struct probing |
| ObjectArray | `ObjectArray.cpp/.h` | Done | FChunkedFixedUObjectArray + FFixedUObjectArray (flat) auto-detection, ForEach, GetByIndex; auto-detect FUObjectItem stride {16,24,20} via FNamePool validation with scoring + fallback; flat array detected via chunk[1] pointer validation |
| FNamePool | `FNamePool.cpp/.h` | Done | Triple-mode: UE5 FNamePool (multi-format header A/B, chunk index) + UE4 TNameEntryArray (double-deref, 0x4000 chunk size) + hash-prefixed FNamePool (4B hash + 2B header, auto-detected) |
| UStructWalker | `UStructWalker.cpp/.h` | Done | FField chain, SuperStruct inheritance, GetFullName; uses DynOff for all offsets |
| ExportAPI | `ExportAPI.cpp/.h` | Done | C ABI exports, UE5_AutoStart, WalkClass Begin/Get/End; calls ValidateAndFixOffsets |
| PipeServer | `PipeServer.cpp/.h` | Done | Named pipe, JSON dispatch, watch/unwatch, get_offsets, walk_world, find_instances, get_ce_pointer_info; get_object_list returns `scanned` count for proper pagination |
| CE Cheat Table | `scripts/UE5CEDumper.CT` | Done | injectDLL-only flow (no executeCodeEx), 15s countdown |
| CEPlugin interface | `CEPlugin.cpp` | Done | CE plugin Type 5 main menu; g_isCEPlugin flag |
| DLL auto-start | `dllmain.cpp` | Done | AutoStartThreadProc, 1s delay, g_isCEPlugin check; per-process mirror log init |
| Avalonia UI | `ui/` | Skeleton done | Object tree + Instance Finder + Class struct panels; pipe integration working; dynamic window title (process name on connect) |

### What Is Working (Confirmed in Testing)

- **DLL injection** via `injectDLL()` in CE Lua — confirmed in TQ2
- **Auto-start thread** — spawned on `DLL_PROCESS_ATTACH`, waits 1s, detects CE plugin vs game process correctly
- **GObjects scan** — found at `0x7FF71B7A1820` in TQ2 (UE5.7), 483,670 objects, NumElements validation passed
- **UE version detection** — PE VERSIONINFO returns UE5.7 for TQ2 correctly
- **Build system** — CMake + Ninja + MSVC via `vswhere`, reproducible Release build
- **CE plugin mode** — `g_isCEPlugin = true` suppresses auto-start when loaded into CE.exe itself
- **Logging** — Dual-channel: `UE5Dumper-scan-0.log` (init/scan, ~20-50KB) + `UE5Dumper-pipe-0.log` (runtime); category tags on all 166 calls; SUMMARY level for 3-line triage; auto-switches Scan→Pipe after `UE5_Init()`
- **Pipe Server** — Named pipe `\\.\pipe\UE5DumpBfx` accepts connections, JSON dispatch working
- **Live Data Walker** — `walk_class`, `walk_instance`, `walk_world` commands functional (type names correct on TQ2)
- **Instance Finder** — `find_instances` command returns matching objects by class name
- **CE XML Export** — `get_ce_pointer_info` returns Cheat Engine-compatible XML pointer records
- **FF7Re Object Tree** — working after FNAME_STRIDE=4 fix + stride detection v1 (24 bytes detected); names correct, Instance Finder functional
- **TQ2 DetectItemSize v2** — stride 16 correctly detected (5 named, 0 from wrong strides); FNamePool validation prevents false positives
- **FF7Re Instance Finder** — `find_instances` returns correct results with valid names
- **FF7Re DetectItemSize v3** — tiebreaker correctly selects stride 24 (0 bad) over stride 16 (5 bad) when named counts are equal (both 5)
- **Tower of Mask (UE4.27)** — Full pipeline working: stride 24 detected, DynOff offsets correct, Object Tree populated (62661 objects), Live Walker walks classes with fields. GWorld null but UWorld found via GObjects scan fallback
- **DQ I&II HD-2D Remake (UE5.05)** — Full pipeline working: stride 24, 128678 objects, standard FNamePool, Object Tree populated, Instance Finder works. GWorld null. Guid/Vector struct-based offset detection works.
- **OctoPath Traveler (UE4, build 1.0.0.19)** — GObjects found (Layout B, 35087 objects), GNames found via UE4 TNameEntryArray, FName[0]='None', FName[1]='ByteProperty'. Object Tree populates. GWorld found but "Start from GWorld" fails in UI.
- **FF7R ValidateGObjects v2 (build 1.0.0.19)** — False positive correctly rejected. V12 pattern finds real GObjects at `0x7FF7868AD470` (Layout B, 165792 objects, chunk[0] valid heap pointer). But names truncated — see challenge #16.
- **DQ I&II CE pointer lookup (build 1.0.0.19)** — Address-to-object lookup success (used different CE instance to load table and get value)
- **FF7R UProperty fallback (build 1.0.0.20)** — FProperty-to-UProperty fallback triggered correctly: Children at struct+0x38, SuperStruct=+0x30, UField::Next=+0x28, UProperty::Offset_Internal=+0x44 (matching RE-UE4SS exactly). But FUObjectItem stride still wrong (16 instead of 24) — see challenge #17
- **EverSpace 2 (UE5.5, build 1.0.0.20)** — Full pipeline working: stride 24 (tiebreaker), GNames via pointer-scan fallback (all AOB patterns failed), 1,158,676 objects, DynOff validated. UE 505 detected from PE.
- **Hogwarts Legacy (UE4.27, build 1.0.0.20)** — Full pipeline working: stride 24 (tiebreaker: 0 bad), 379,793 objects, FProperty mode with FField::Name=+0x28, FField::Next=+0x20. PE reports UE 4.27 → version 427.
- **IDOLM@STER STARLIT SEASON (UE4.24, build 1.0.0.25)** — Working. Object Tree populated. "Start from GWorld" fails in UI.
- **Romancing SaGa 2: Revenge of the Seven (UE4.27, build 1.0.0.25)** — Working. Object Tree populated. "Start from GWorld" fails in UI.
- **Star Wars Jedi: Fallen Order (UE4.21, build 1.0.0.25)** — Working. 318,022 objects. Full pipeline functional.

### Known Challenges & Failures

#### 1. GNames AOB Patterns Fail on TQ2 (UE5.7)

All 4 LEA-based patterns resolve to wrong globals — none contain FNamePool data:

| Pattern | Resolved Address | Actual Content | Reason |
|---------|-----------------|----------------|--------|
| `48 8D 35` (lea rsi) | `0x7FF71AA1B5B0` | `F76FEExx` values | Obfuscated/encrypted data block |
| `48 8D 05` (lea rax) | `0x7FF71AA16A84` | `"free bit..."` ASCII | C string constant in .rdata |
| `4C 8D 05` (lea r8) | `0x7FF71AA17D70` | Self-referential ptr | SRWLOCK / sync primitive |
| `48 8D 0D` (lea rcx) | `0x7FF718E1C5C8` | `"Working color space..."` | Wide string struct |

**Root cause**: TQ2 (UE5.7) either uses non-standard GNames referencing instructions, or the patterns appear in an unexpected order and the scanner finds the wrong occurrence.

**Fix implemented** (`FindGNamesByPointerScan`): Scan game module `.data` section for any 8-byte-aligned pointer that dereferences to a `"None"` FNameEntry (`00 01 4E 6F 6E 65` or `08 00 4E 6F 6E 65`). The pointer is `FNamePool.Blocks[0]`; try offsets `{0x10, 0x00, 0x08, 0x18, 0x20}` from it to find pool base. **Untested** — requires next test run.

#### 2. CE executeCodeEx Returns nil for All DLL Calls

**Symptom**: Every `executeCodeEx()` call in Lua returned `nil`, even for functions that exist.

**Root cause**: When DLL is loaded as a CE plugin, `getAddress("UE5_Init")` returns the address in CE's own process space. `executeCodeEx` then tries `CreateRemoteThread` in the game process with that CE-process address -> immediate crash -> nil.

**Fix**: Removed all `executeCodeEx` calls from CT Lua. DLL now auto-starts via `AutoStartThreadProc` on `DLL_PROCESS_ATTACH`. CE Lua only calls `injectDLL()` and then waits.

#### 3. CT DLL_PATH Resolution

**Symptom**: `DLL_PATH` resolved to CE install folder instead of the CT's own folder.

**Root cause**: `File -> Open` in CE updates `OpenDialog1.FileName`, not `SaveDialog1.FileName`. The old CT code only checked `SaveDialog1`.

**Fix**: Try `OpenDialog1.FileName` first, fall back to `SaveDialog1.FileName`, then `getCheatEngineDir()`.

#### 4. ValidateGNames False Failure (FRWLock Misread)

**Symptom**: `ValidateGNames` silently returned false even when passed a real FNamePool address, because it only tried offset `+0x00` to find `Blocks[0]`.

**Root cause**: Standard `FNameEntryAllocator` layout: `[+0x00] FRWLock (8B)`, `[+0x08] CurrentBlock`, `[+0x0C] Cursor`, `[+0x10] Blocks[0]`. Reading the FRWLock value as a chunk pointer gave a non-zero but invalid address, causing `ReadSafe` to fail with SEH.

**Fix**: Try offsets `{0x10, 0x00, 0x08, 0x20, 0x40}` in order; added hex dump of first 128 bytes on failure for diagnosis.

#### 5. FField/FProperty Offset Mismatch on TQ2 (UE5.7) — CasePreservingName

**Symptom**: Live Data Walker on TQ2 showed all field names as "rty" (last 3 chars of "Property"), all field offsets as 0x0, and ArrayProperty showing 262216 elements (garbage). Type names were correct (StructProperty, ArrayProperty, etc.).

**Root cause**: TQ2 (UE5.7) uses **CasePreservingName** — FName is 0x10 bytes instead of 0x8. This shifts FField::Flags from 0x30 to 0x38, and ALL FProperty fields after FField::Flags shift by +0x8. The hardcoded offsets in Constants.h (FField::Name=0x28, FProperty::Offset_Internal=0x4C) were reading wrong memory locations.

**Diagnosis clues**: FField::ClassPrivate at +0x08 was correct (type names worked), but FField::Name at +0x28 was reading into the middle of the FName data (the "rty" from "Property" type name leaking through). Offset_Internal at +0x4C read zeros because it was pointing at padding bytes.

**Fix implemented** (commit `8c2f983`):
1. Added `DynOff` namespace in `Constants.h` with mutable `inline int` offsets (replacing `constexpr` for FField/FProperty/UStruct)
2. Implemented `ValidateAndFixOffsets()` in `OffsetFinder.cpp` (~370 lines) — probes known structs (Guid with fields A/B/C/D at offsets 0/4/8/12, or Vector with X/Y/Z at 0/4/8) to dynamically discover correct FField::Name, FField::Next, FProperty::Offset_Internal, and UStruct::ChildProperties offsets
3. Updated `UStructWalker.cpp` to use `DynOff::*` instead of `Constants::OFF_*`
4. Added `get_offsets` pipe command for runtime offset diagnostics
5. Called from `UE5_Init()` after FNamePool + ObjectArray initialization

**Status**: Built successfully (0 errors, 0 warnings), **untested on TQ2** — requires next injection test.

#### 6. FF7 Rebirth (FF7Re): Garbled Names + Wrong UE Version + GWorld Empty

**Symptom**: Object tree showed garbled names (????), UE version reported as 5.06 (should be UE4), GWorld scan returned pointer value 0x348 (garbage).

**Root causes** (three cascading failures):
1. **Version detection false positive**: Bare "5.6." string found in FF7Re game data → string scan matched → reported UE 5.06 (actually UE4 fork)
2. **GNames structural validator false positive**: `strstr(buf+2, "None")` accepted garbage address 0x7FF61A7CA780 because FRWLock happened to be 0 and `strstr` matched "None" in random adjacent bytes
3. **GWorld AOB pattern false match**: Pattern resolved to wrong global → UWorld*=0x348 (garbage)

**Fixes applied**:
1. **Version detection**: Added 3-tier detection — Tier 1: `++UE5+Release-` / `++UE4+Release-` exact prefix; Tier 2: `Release` in preceding 16 bytes; Tier 3: bare pattern with guard against preceding digit/period (rejects "15.6.0")
2. **ValidateGNamesStructural hardened**: Replaced `strstr` with exact FNameEntry header format matching (Format A/B length check + strcmp for "None") + `CorroborateFNameChunk` gate (requires UE type names like "Property", "Object" in chunk)
3. PE FileVersion fallback added alongside ProductVersion check

#### 7. OctoPath Traveler: Pipe Connection Failed (UE4 TNameEntryArray)

**Symptom**: UI pipe connection failed — DLL init aborted, pipe server never started.

**Root cause**: OctoPath Traveler is a UE4 game using `TNameEntryArray` (flat chunked pointer array), not `FNamePool`. All 17 GNames AOB patterns + pointer-scan fallback failed because they only validate against FNamePool structure → GNames=0 → `FindAll` returned false → pipe server never started.

**Fix applied**: Added `ValidateGNamesUE4()` validator for UE4 TNameEntryArray layout (double-deref: array→chunk→entry pointer, tries string offsets 0x10/0x06/0x0C/0x08 for "None" with corroboration). Added dual-mode `FNamePool` with `InitUE4()` for UE4 path. `ValidateGNamesAny()` wrapper tries all three validators in sequence: FNamePool→Structural→UE4. `EnginePointers` extended with `bUE4NameArray` + `ue4StringOffset` flags. `ExportAPI::UE5_Init()` branches on the flag.

**Status**: Fix confirmed working — GNames found via UE4 path, pipe server starts, UI connects. But Object Tree empty (see #8).

#### 8. OctoPath Traveler: Object Tree Empty (Pagination Bug)

**Symptom**: UI connects, GObjects=32758, but Object Tree shows "Objects: 0 (of 0)". Live Walker UWorld fields also empty.

**Root cause**: Pagination bug in `get_object_list` handler. The DLL iterates indices `offset..offset+limit` and silently skips objects with empty names (null objects, unnamed objects). The UI advances pagination by `result.Objects.Count` (number of actually returned items), not by the number of indices scanned. If most objects in a range are unnamed/null, the UI stalls (returns 0 objects → offset never advances → infinite loop, or very slow progress).

**Fix applied**:
1. DLL: `get_object_list` now returns `"scanned"` field = number of indices actually iterated (`end - offset`)
2. UI: `ObjectTreeViewModel.LoadAsync()` advances `offset += result.Scanned` instead of `offset += result.Objects.Count`
3. UI: Break condition changed from `offset >= 2000` to `_allNodes.Count >= 2000` (limit by loaded objects, not scanned indices)

**Status**: Built successfully, **untested** — requires next injection test.

#### 9. FF7 Rebirth: GNames Not Found (Hash-Prefixed FNameEntry)

**Symptom**: All GNames AOB patterns resolve to `0x7FF7D95AA780` which has correct FNamePool structure (FRWLock=0, Blocks[] at +0x10 with valid heap pointers), but `ValidateGNames` fails because entry 0 has header `0x2DC2` at offset +0 which doesn't decode to length 4.

**Root cause**: FF7Re (UE4.26, Square Enix fork) uses a **hash-prefixed FNameEntry** format:
- Standard UE5: `[2B header][string]` → string at entry+2
- FF7Re UE4.26: `[4B ComparisonId/hash][2B header][string]` → string at entry+6
- The near-miss log showed "None" at offset +6 from chunk pointer, with bytes `C2 2D 35 A6 2A 01 4E 6F 6E 65`
- Header at offset +4: `0x012A`, and `0x012A >> 6 = 4` (length 4, Format A) ✓

**Fix applied**:
1. `ValidateGNames`: now tries header at both offset +0 (standard) and +4 (hash-prefixed), sets `g_fnameEntryHeaderOffset`
2. `ValidateGNamesStructural`: same dual-offset check
3. `LooksLikeNoneEntry`: now checks offsets +2, +4, and +6 for "None" string
4. `EnginePointers`: added `fnameEntryHeaderOffset` field
5. `FNamePool::Init()`: accepts `headerOffset` parameter, stores as `s_headerOffset`
6. `FNamePool::GetString()`: reads header at `entry + s_headerOffset`, string at `entry + s_headerOffset + 2`
7. All detection functions (`DetectHeaderFormat`, `DetectBlockOffsetBits`, `ValidateChunkForNone`) use `s_headerOffset`

**Status**: Built successfully (0 errors, 0 warnings), **untested** — requires next injection test.

#### 10. OctoPath + FF7Re: Object Names Empty / Garbled (FUObjectItem Size Mismatch)

**Symptom**: OctoPath: Object Tree shows "Objects: 0 (of 32758)" — all 167 paginated requests return 0 named objects. CPN detection: "tested 0 objects". FF7Re: Names show as "????Editor", "????e????????????????e?..." — garbled with valid suffixes.

**Root cause**: `ObjectArray.h` hardcoded `FUObjectItem = 16 bytes` (`static_assert`), but UE4 (and some UE5) uses **24 bytes** (`Object*(8) + Flags(4) + ClusterRootIndex(4) + SerialNumber(4) + _pad(4)`). `GetByIndex(n)` reads at `chunk + n * 16` instead of `chunk + n * 24`. Index 0 works (offset 0), but index 1+ reads wrong memory. OctoPath: ALL names fail → 0 objects. FF7Re: every 3rd index accidentally aligns (LCM(16,24)=48, 48/16=3) → partial garbled names.

**Secondary cause**: Version detection defaults to 504/505 when no UE version strings found. For OctoPath (UE4 pre-4.25), this makes `DetectUPropertyMode` select FProperty mode instead of UProperty mode.

**Fix applied**:
1. `ObjectArray.cpp`: Added `DetectItemSize()` — probes consecutive items in chunk 0 with strides {16, 24, 20}, validates 3+ items by checking UObject ClassPrivate chain (triple deref). Dynamic `s_itemSize` replaces all `sizeof(FUObjectItem)`.
2. `ObjectArray.h`: Removed `static_assert(sizeof(FUObjectItem) == 16)`, added `GetItemSize()`.
3. `OffsetFinder.cpp` `FindAll()`: Version override — if `bUE4NameArray && version >= 500` → force 422; if `hdrOff=4 && version >= 500` → force 426.
4. `ExportAPI.cpp`: Added post-init name resolution sanity check (10 objects, logs count of successful resolutions).
5. `PipeServer.cpp`: CE pointer info uses `ObjectArray::GetItemSize()` instead of hardcoded 16.

**Status (first attempt)**: Item size detection worked for FF7Re (24 bytes) but failed for OctoPath (item[0] null → fell back to 16). Version override worked for both. TQ2 regression OK. But names still garbled in FF7Re, Object Tree still empty in OctoPath. See #11.

#### 11. OctoPath: DetectItemSize Fails on Null item[0] + FF7Re: FNAME_STRIDE Wrong for Hash-Prefixed Entries

**Symptom**: After fix #10, OctoPath still shows 0 objects (scan log: `item[0].Object is null or invalid`, `ItemSize=16`). FF7Re item size correctly detected as 24 but names still garbled (`obj[0] name='?' idx=1985`, `obj[3] name='operty' idx=270`).

**Root causes** (two separate issues):

1. **OctoPath DetectItemSize**: The `DetectItemSize()` function reads `item[0].Object` and gives up if null. But OctoPath's GObjects array has null in slot 0 (common in UE4). The function falls back to `s_itemSize=16` (wrong — should be 24). TQ2 also had null item[0] but happened to work because UE5 default of 16 was correct.

2. **FF7Re FNAME_STRIDE**: Hash-prefixed FNameEntry has `struct { uint32_t ComparisonId; uint16_t Header; char String[]; }`. The `alignof` is 4 (due to uint32_t), so `FNameEntryAllocator::Stride = 4`. Our code used `FNAME_STRIDE=2` (correct for standard entries), making `GetEntry(nameIndex)` compute wrong byte offsets: `(nameIndex & 0xFFFF) * 2` instead of `* 4`. This means every entry address is off by a factor of 2, landing in the middle of other entries. Explains: 'operty' = tail of 'Property', '?' = reading hash bytes as string.

**Diagnosis clues**:
- OctoPath: `Name sanity: 0/0 objects resolved` — zero objects could be read at all (stride 16 instead of 24 made every pointer garbage)
- FF7Re: `obj[2] name='PropertyAccess/EPropertyAccessCopyBatch'` — some names resolve partially because large indices happen to land near valid entries, but most are off
- RE-UE4SS `MemberVariableLayout.ini` for FF7Re confirms all UObject/FField/FProperty offsets match our DynOff detection → the UObject layout is correct, only FNamePool stride was wrong

**Fix applied (v1)**:
1. `ObjectArray.cpp` `DetectItemSize()`: Complete rewrite. Scans first 384 bytes of chunk[0] in 8-byte steps to find first valid UObject (via `LooksLikeUObject()` helper). Then tries strides {24, 16, 20} both forward and backward from the anchor to validate. No longer requires item[0] to be non-null.
2. `FNamePool.cpp`: Added `s_stride` variable (2 for standard, 4 for hash-prefixed). Set in `Init()` based on `headerOffset >= 4`. `GetEntry()` and `DetectBlockOffsetBits()` now use `s_stride` instead of `Constants::FNAME_STRIDE`.

**Status (v1)**: FF7Re FNAME_STRIDE fix ✅ (names correct, Object Tree works, Instance Finder works). TQ2 REGRESSED ❌ (DetectItemSize falsely detects 24 instead of 16). OctoPath still fails ❌. See #12.

#### 12. TQ2 Regression + OctoPath Still Empty (DetectItemSize Anchor-Scan Flaws)

**Symptom**: After fix #11:
- TQ2: `First valid UObject at chunk+0x8`, `FUObjectItem size detected as 24 bytes` → wrong (should be 16). `Name sanity: 0/10 objects resolved`.
- OctoPath: `No valid UObject found in first 384 bytes of chunk[0]` → falls back to default 16 (should be 24). Object Tree still empty.

**Root causes** (two separate issues):

1. **TQ2 false detection**: The anchor-scan approach found a "valid UObject" at chunk offset 0x8 — which is NOT an item boundary for any stride. Offset 0x8 lands in the middle of item[0]'s Flags/SerialNumber fields (for 16-byte items, items are at 0, 16, 32...). The 64-bit value at that offset happened to pass `LooksLikeUObject()` (triple-deref chain by coincidence). Then stride 24 was tried first (24>16>20 order) and found enough false positives at {32, 56, 80...} to validate 10 items. The code didn't enforce `anchorOffset % stride == 0` and tried 24 before 16.

2. **OctoPath insufficient scan range**: 384 bytes = ~16 items at stride 24. OctoPath has many null/empty items at the start of chunk[0] (possibly thousands). The scan range was too small to find any valid items.

**Fix applied (v2)**: Complete rewrite of `DetectItemSize()` with fundamentally different approach:
1. **No anchor scan** — instead, for each candidate stride, walk chunk from offset 0 at stride-aligned positions only (offset = idx * stride). This inherently ensures stride alignment.
2. **FNamePool-based validation** — since FNamePool is initialized before ObjectArray, use `FNamePool::GetString()` to verify each UObject's FName resolves to a valid ASCII string. This is far stronger than the `LooksLikeUObject()` triple-deref check which produces false positives.
3. **Try all strides, pick best** — no early exit. Compare named counts (strong signal) across all strides.
4. **Extracted `ProbeStride()` helper** — reusable function that counts good/named/null/bad items for a given stride.
5. **Deep scan phase** — if first 100 items per stride yield nothing (e.g., OctoPath's many null early slots), retry starting from item index 1000 for 50 more items.
6. **Order: {16, 24, 20}** — try 16 first (UE5 most common).

**Status (v2)**: TQ2 ✅ (stride 16 correct: `Stride 16: good=5, named=5, null=4, bad=6` vs others all 0). FF7Re ❌ (stride 16 falsely wins: all strides show `named=5` due to LCM alignment, no tiebreaker). OctoPath ❌ (all strides show `good=0, named=0, bad=11` — not even a chunked array). See #13.

#### 13. FF7Re Stride Tiebreaker + OctoPath Flat Array (DetectItemSize v3)

**Symptom**: After DetectItemSize v2:
- FF7Re: `Stride 16: good=5, named=5, null=3, bad=5`, `Stride 24: good=5, named=5, null=0, bad=0`, `Stride 20: good=5, named=5, null=6, bad=14`. Stride 16 wins (tried first) but should be 24 (0 bad items). `ItemSize=16` (wrong).
- OctoPath: ALL strides show `good=0, named=0, null=0, bad=11` in Phase 1 AND Phase 2 deep scan. All bytes read as "bad".

**Root causes** (two separate issues):

1. **FF7Re tiebreaker**: With `ProbeStride` early exit `if (outNamed >= 5) break`, all strides stop at exactly 5 named items. For stride 16, every 3rd 16-byte slot (LCM(16,24)=48, 48/16=3) happens to align with a real 24-byte item boundary. So stride 16 finds 5 named items but with 5 bad items (misaligned reads). Stride 24 finds 5 named with 0 bad items. Since named counts are equal and stride 16 is tried first, it wins — no tiebreaker existed.

2. **OctoPath non-chunked array**: The `chunk[0]` deref (reading `*(chunkTable + 0)`) yields a pointer that does not contain UObject items. This suggests OctoPath's GObjects array is **flat** (non-chunked): `*(GObjects + objectsOffset)` directly points to the item array, without the extra chunk table indirection. The chunkTable hex dump shows `+00` is a valid heap pointer but `*(chunkTable + 0)` at that location contains item data directly, not more chunk pointers.

**Fix applied (v3)**:
1. **Tiebreaker**: In `ProbeAllStrides()`, when named counts are equal (both > 0), prefer the stride with fewer bad items (`bad < bestBad`). Same logic for good-count-only comparisons.
2. **Flat array Phase 3**: If Phases 1+2 find nothing, set `s_isFlat=true` and probe `chunkTable` itself (not `*chunkTable`) as the item base. Deep scan (item 1000+) also tried on flat layout. If still nothing, revert `s_isFlat=false`.
3. **GetByIndex/GetItem flat path**: When `s_isFlat`, compute `itemAddr = arrayBase + index * itemSize` directly (no chunk table deref).
4. **Diagnostic hex dump**: First 64 bytes of chunkTable logged for diagnosis.

**Status (v3)**: TQ2 ✅ (stride 16, no regression). FF7Re ✅ (tiebreaker: stride 24 wins with 0 bad vs stride 16's 5 bad). OctoPath ❌ (all phases fail — not a stride issue, see #14). Tower of Mask ✅ (stride 24, full pipeline working).

#### 14. OctoPath: DetectLayout Selects Wrong Layout (Objects Pointer is Code Address)

**Symptom**: After DetectItemSize v3, OctoPath still shows 0 objects. All 4 phases of DetectItemSize (P1, P2-deep, P3-flat, P3-flat-deep) return `good=0, named=0, bad=11` for all strides. The `chunkTable` hex dump shows machine code bytes: `83485708245C8948...`.

**Root cause**: `DetectLayout` selects **alternate layout** (`Objects at +0x10, Num at +0x04`), but `*(GObjects + 0x10) = 0x7FF62D12B410` is in the game module's `.text` section — it's executable code, not a pointer to the Objects array.

**Why alternate layout was selected**:
1. Default layout check: `*(addr+0x10)` (MaxElements) is `0x7FF62D12B410` truncated to int32 = `0x2D12B410` = 756M, exceeding the 0x800000 (8M) cap → fails.
2. Alternate layout check: `*(addr+0x04)` = 32758, which passes the `numAtAlt > 0 && numAtAlt <= 0x800000` check. But this value is actually `ObjLastNonGCIndex` (an FUObjectArray member), NOT NumElements.
3. With `objectsOffset = 0x10`, the code reads a code address as the Objects pointer. All subsequent stride probing reads garbage.

**Actual layout**: The GObjects address `0x7FF62EB02190` was validated by `ValidateGObjects` checking `*(addr+0x14)` = 32758 (the real NumElements). The default layout (`Objects at +0x00, Num at +0x14`) is correct, but the MaxElements check rejects it because `*(addr+0x10)` is a pointer value (PreAllocatedObjects*), not a count.

**Fix applied (DetectLayout v2)**:
1. Added `LooksLikeHeapPtr()` helper: validates that a pointer is non-null, in user-mode range, and NOT inside the game module's code section.
2. Rewrote `DetectLayout` with 4 layout candidates (A/B/C/D), each validating that the Objects pointer is a heap address:
   - **A** (UE5 default): Objects +0x00, Max +0x10, Num +0x14 — full Max/Num validation + heap pointer check
   - **B** (UE4 alternate): Objects +0x10, Num +0x04 — now requires Objects pointer to be a heap address
   - **C** (UE4 relaxed): Objects +0x00, Num +0x14 — accepts invalid Max at +0x10 (pointer contamination), validates Objects pointer is heap
   - **D** (UE4 extended): Objects +0x10, Max +0x18, Num +0x1C — for FUObjectArray with ObjFirstGCIndex/ObjLastNonGCIndex before ObjObjects
3. Added hex dump of first 48 bytes at GObjects address for diagnosis.

**Expected result**: OctoPath should now match Layout C: Objects at +0x00 (heap pointer), Num at +0x14 (32758). DetectItemSize should then find valid items.

**Status**: OctoPath was tested with old build (pre-DetectLayout v2). The fix was NOT in the compiled DLL. Re-test required with build 1.0.0.18.

#### 15. FF7R (Remake Intergrade): ValidateGObjects False Positive + V12 Deref Bug

**Symptom**: FF7R connected (UE504, 32759 objects), but Object Tree shows "Objects: 0 (of 32759)". All DetectItemSize phases return `good=0, named=0, bad=11`. FNamePool initialized correctly, FName[0]='None', FName[1]='ne??ByteProperty' (garbled concatenation). Name sanity: 0/10 objects resolved.

**Root causes** (two cascading failures):

1. **ValidateGObjects false positive**: The deref variant of pattern V6 (`48 8B 0D`) resolves to `0x7FF786CD99D0`, reads `*(0x7FF786CD99D0)` → `0x1B32FE82B80` (heap pointer). Then `ValidateGObjects(0x1B32FE82B80)` checks `*(0x1B32FE82B80 + 0x14)` = `0x00007FF7` (upper 32 bits of a code-section pointer at +0x10) = 32759 — which COINCIDENTALLY passes the NumElements range check. The address `0x1B32FE82B80` is NOT an FUObjectArray — it's some heap allocation where pointer bytes happen to look like a valid count.

2. **V12/RE2 pattern deref-only resolution**: The FF7R-specific V12 pattern (`48 8B ?? ?? ?? ?? ?? 4C 8B 04 C8`) and RE2 pattern should use `target - 0x10` (RE-UE4SS style: the RIP target points to the ObjObjects field, base = target-0x10). But our code only tried `*(target) - 0x10` (deref'd), which computes a nonsensical heap-pointer-minus-16. Since the false positive from issue #1 was accepted first, V12/RE2 was never even reached.

**Diagnosis clues**:
- `GObjects@0x1B32FE82B80: +00:00007FF7860A5D70` — all objects pointers are in game module range (code section), not heap
- `chunkTable` hex dump shows game module code pointers, not heap chunk pointers
- RE-UE4SS config for FF7R: `EngineVersionOverride: 4.18`, `UStruct::SuperStruct = +0x30`, GUObjectArray signature uses `-0x10` offset (ObjObjects at +0x10)
- DQ I&II HD-2D Remake (UE5.05) works correctly with same stride detection logic — the FF7R issue is purely in GObjects address discovery

**Fix applied (ValidateGObjects v2 + V12 non-deref)**:
1. Rewrote `ValidateGObjects` with multi-layout validation: tries 3 layout candidates (A/C: Num+0x14/Obj+0x00, B: Num+0x04/Obj+0x10, D: Num+0x1C/Obj+0x10). For each, validates:
   - NumElements in [0x1000, 0x400000]
   - Objects pointer can be dereferenced to get chunk[0]
   - chunk[0] must be a valid heap pointer (not in game module) via `LooksLikeDataPtr()` — rejects code section addresses
   - If chunk[0] is null, Objects pointer itself must be a heap pointer
2. Added `LooksLikeDataPtr()` helper: rejects null, low, kernel, and game-module-range pointers
3. Fixed V12 and RE2 handlers to try `ValidateGObjects(target - 0x10)` BEFORE deref variant, matching RE-UE4SS's resolution logic

**Expected result**: False positive `0x1B32FE82B80` rejected (chunk[0] is in game module). Scan continues to V12 pattern, which resolves with `target - 0x10` to find the real FUObjectArray in the game module's data section.

**Status**: ValidateGObjects v2 ✅ (false positive correctly rejected). V12 non-deref ✅ (finds real GObjects at `0x7FF7868AD470`, Layout B, 165792 objects). But names still garbled — see #16.

#### 16. FF7R (Remake Intergrade): Truncated Names + UProperty Mode Misdetection

**Symptom**: Object Tree loads 165792 objects but names are visibly truncated/garbled ("vel", "Package" appearing repeatedly). FName[0]='None' correct but FName[1]='ne'. Sanity check: 4/10 objects resolved names. Live Walker shows no fields (DynOff `Cannot find ChildProperties in 'Guid'`).

**Root causes** (three cascading failures):

1. **FNamePool stride not auto-detected**: FF7R has standard FNamePool (hdrOff=0, Format A) with stride=2 confirmed by "None" at offset 2. FName[1]='ne' is expected — index 1 falls inside "None" entry and is an invalid/unallocated index. The real issue is FUObjectItem stride (see below).

2. **FUObjectItem stride=16 selected (should be 24)**: DetectItemSize P1 shows stride 16: `good=5, named=5, null=56, bad=29` vs stride 24: `good=2, named=2, null=68, bad=30`. Stride 16 wins on named count. But stride 16 with actual stride 24 means every 3rd item (LCM(16,24)=48) coincidentally aligns. The tiebreaker (bad count) only applies when named counts are equal. Here 5 > 2, so no tiebreaker. With wrong stride, 2/3 of objects have wrong UObject pointer → wrong FName index → truncated/garbage names.

3. **Version misdetection → FProperty mode → wrong UStruct offsets**: Version defaults to 504 (no version string found). `DetectUPropertyMode(504)` → FProperty mode (>= 425). But FF7R is UE4.18 (RE-UE4SS config confirms `MajorVersion=4, MinorVersion=18`). UE4.18 uses UProperty (UObject-derived), not FProperty. UStruct offsets are completely different:
   - Our defaults: SuperStruct=0x40, Children=0x48, ChildProperties=0x50
   - FF7R (RE-UE4SS): SuperStruct=0x30, Children=0x38, NO ChildProperties
   - Also: FProperty::Offset_Internal=0x44, UField::Next=0x28

   `ValidateAndFixOffsets` probes Guid struct for FFieldClass at ptr+0x08 (FProperty mode) — fails because FF7R properties are UObject-derived. Falls back to defaults. All class walking produces garbage.

**Fix applied**:
1. `FNamePool.cpp`: Added `DetectStride()` — scans chunk[0] for "None" (0x656E6F4E) to auto-detect FNameEntry alignment stride. Called after `DetectChunksOffset()` but before `DetectBlockOffsetBits()`. Won't change the result for FF7R (stride stays 2, which is correct for standard FNamePool), but provides insurance for future games.
2. `OffsetFinder.cpp`: Added **FProperty-to-UProperty fallback** in `ValidateAndFixOffsets()`. When FProperty mode can't find ChildProperties, retries with UProperty mode by scanning for UObject-derived properties (Class at ptr+0x10 → class name contains "Property"). If found, switches `bUseFProperty=false` and sets `UFIELD_NEXT=0x28`. All subsequent offset detection (Name, Next, Offset_Internal, SuperStruct derivation) then uses UProperty logic correctly.

**Expected result**: FF7R fallback finds UProperty chain at struct+0x38 → derives SuperStruct=0x30, PropertiesSize=0x40. UField::Next=0x28, UProperty::Offset_Internal=0x44 (matching RE-UE4SS exactly). Class walking should produce correct fields. FUObjectItem stride still 16 (may need separate fix if tiebreaker doesn't help).

**Status (build 1.0.0.20)**: UProperty fallback ✅ (correct offsets detected: Super=+0x30, Children=+0x38, Next=+0x28, Offset=+0x44). FUObjectItem stride still 16 ❌ (5 named vs 2 named — scoring doesn't penalize bad items). See #17.

#### 17. FF7R (Remake Intergrade): Stride 16 Wins Over 24 Despite High Bad Count

**Symptom**: After fix #16, FF7R scan log (build 1.0.0.20) shows stride detection still selects 16 bytes: `P1 stride 16: good=5, named=5, null=56, bad=29` vs `P1 stride 24: good=2, named=2, null=68, bad=30`. Object Tree still shows truncated names ("vel", "Package"). Name sanity: 4/10 objects resolved.

**Root cause**: The stride comparison used `named` count as the primary metric. Stride 16 gets 5 named items (LCM alignment every 3rd) while stride 24 only gets 2 (sparse beginning of GObjects array). The tiebreaker (bad count) only applies when named counts are equal. Additionally, the `ProbeStride` function had `if (outNamed >= 5) break;` early exit — this capped all strides at 5 named items max, preventing the correct stride from accumulating more evidence with longer scans. Without the early exit and with more items scanned, the correct stride (24) would show 0 bad items while the wrong stride (16) would show 60-90+ bad items.

**Fix applied (stride scoring v4)**:
1. `ObjectArray.cpp` `ProbeStride()`: Removed `if (outNamed >= 5) break;` early exit. All strides now scan the same number of items for fair comparison.
2. `ObjectArray.cpp`: Added `ComputeStrideScore()` function: `score = named * 10 - bad * 3` (named), `score = good * 5 - bad * 2` (good-only), or `-bad` (nothing). This heavily penalizes strides with many bad items.
3. `ObjectArray.cpp` `ProbeAllStrides()`: Comparison now uses `ComputeStrideScore()` instead of named-count-first logic. The stride with the highest score wins.
4. Phase 1 items increased from 100 to 200 for better signal.
5. Phase 2/3 simplified to use `ProbeAllStrides()` instead of duplicating comparison logic.

**Expected result**: FF7R stride 24 should win with score ≈ 50-150 (many named, near-zero bad) vs stride 16's score ≈ -120 (some named, but 60-90 bad items heavily penalize it). Other games remain correct because correct strides always have low bad counts.

**Regression check** (predicted scores from scan data):
- TQ2 (stride 16): 16 wins (positive score, others have 0 named)
- OctoPath (stride 16): 16 wins (5 named, others have ≤2, and 16 has same bad pattern)
- Hogwarts Legacy (stride 24): 24 wins (0 bad >> 6 bad for stride 16)
- FF7Re (stride 24): 24 wins (0 bad >> 5 bad for stride 16)
- EverSpace 2 (stride 24): 24 wins (0 bad >> 5 bad for stride 16)
- DQ (stride 24): 24 wins (0 bad >> 4 bad for stride 16)

**Status (build 1.0.0.23)**: v4 scoring insufficient. FF7R stride 16 still wins: score -83 vs -99 (both negative). v5 fallback added: when all scores negative, pick stride with fewest bad. Build 1.0.0.25 correctly selects stride 24 via fallback. But names STILL garbled — root cause is flat array, not stride. See #18.

#### 18. FF7R (Remake Intergrade): Flat FFixedUObjectArray Misidentified as Chunked

**Symptom**: After stride v5 correctly selects 24, FF7R scan log (build 1.0.0.25) shows stride 24 detected but name sanity only 2/10. Object Tree still shows "vel", "Package", "None_2". Both strides have ~53-61 bad items (unusual — correct stride on other games has ~0 bad).

**Root cause**: FF7R uses UE4.18 which has `FFixedUObjectArray` (flat, 16-byte struct: `{FUObjectItem* Objects, int32 Max, int32 Num}`), NOT `FChunkedFixedUObjectArray` (chunked, 32-byte struct with chunk table). RE-UE4SS MemberVariableLayout_4_18_Template.ini confirms: `TUObjectArray` is 16 bytes with `Objects=0x0, MaxElements=0x8, NumElements=0xC`.

Our Layout B correctly reads `Objects = 0x20C7AAB0000` at GObjects+0x10. But this pointer points directly to the flat FUObjectItem array, not a chunk table. The code then reads `*(0x20C7AAB0000) = 0x20C7C880000` thinking it's "chunk[0]" — but `0x20C7C880000` is actually `Item[0].Object` (the first UObject* in the flat array). ProbeStride then reads UObject internal memory as FUObjectItems → mostly garbage → high bad counts for ALL strides. GetByIndex uses chunked path with OBJECTS_PER_CHUNK=65536, computing wrong addresses for all objects.

**Evidence from hex dump**:
```
arrayBase@0x20C7AAB0000 (what code thinks is chunk table):
+00: 0000020C7C880000  ← Item[0].Object (UObject*)
+08: 0000000040000000  ← Item[0].Flags (EObjectFlags::Const = 0x40000000)
+10: 0000000000000000  ← Item[0].ClusterIdx + SerialNum
+18: 0000020C60F50A00  ← Item[1].Object (UObject*)
+20: 0000000042000000  ← Item[1].Flags
```

**Detection signal**: For 165792 objects with OBJECTS_PER_CHUNK=65536, we need 3 chunks. But `chunk[1] = *(arrayBase + 8) = 0x40000000` is NOT a valid heap pointer → the "chunk table" is actually a flat item array.

**Fix applied**: Added flat array pre-detection in `DetectItemSize()`. Before Phase 1 (chunked probe), check if `chunk[1]` looks like a heap pointer when 2+ chunks are needed. If chunk[1] is NOT a valid pointer (fails `LooksLikeHeapPtr`), try flat layout first (probe `chunkTable` directly as item base, set `s_isFlat=true`). If flat probe finds ≥2 named items, confirm flat mode and skip chunked phases. Otherwise fall back to chunked detection.

**Expected result**: FF7R flat array detected via chunk[1] validation → stride 24 with correct item addresses → 8-10/10 name sanity → correct Object Tree names.

**Regression safety**: For chunked games (TQ2, ES2, Hogwarts, etc.), chunk[1] is either a valid heap pointer or null (single chunk). For small games with ≤65536 objects, chunksNeeded=1, so the check is skipped entirely. Only triggers when numElements > OBJECTS_PER_CHUNK AND chunk[1] is not a pointer.

**Status**: Built successfully (build 1.0.0.26), **untested** — requires injection test.

### Lessons Learned

- `executeCodeEx` internally uses `CreateRemoteThread` — games with thread injection protection will block it silently (returns nil, not an error code)
- CE `File -> Open` vs `File -> Save` update **different** dialog objects (`OpenDialog1` vs `SaveDialog1`)
- MSVC C2360: variable declarations inside `case` require wrapping `{}` braces
- AOB patterns for GNames must be more context-specific for newer UE builds (UE5.5+); consider additional patterns or the pointer-scan fallback as primary for UE5.5+
- Always add memory hex dumps in validators — invaluable for diagnosis when running blind inside a game process
- **CasePreservingName (UE5.5+/5.7)**: FName grows from 0x8 to 0x10 bytes (adds DisplayIndex), shifting FField::Flags and all subsequent FProperty offsets by +0x8. Never hardcode FField/FProperty offsets — always detect dynamically using known structs (Guid, Vector)
- **Dumper-7 reference patterns**: `FindFFieldNameOffset()` brute-force probing, `FixupHardcodedOffsets()`, `InitFNameSettings()` for CasePreservingName detection — these are the gold standard for dynamic offset discovery
- **"rty" diagnostic pattern**: When field names show truncated type names (e.g., "rty" from "Property"), it means Name offset is wrong and reading into adjacent FFieldClass data. Type names being correct while field names are wrong = FField::ClassPrivate offset is OK but FField::Name offset is shifted
- **Log structure matters for AI analysis**: A single 2MB log with mixed init/runtime traffic wastes context tokens. Splitting by phase (scan vs pipe) + adding category tags + SUMMARY level reduces analysis from reading 2MB to `grep "[SUMMARY]"` → 3 lines. Use `LOG_CAT` file-level `#define` for minimal call-site changes; functions needing subcategories call `Logger::Info("SUBCAT", ...)` directly
- **Build system**: VS 2026 (v18) at `C:\Program Files\Microsoft Visual Studio\18\Community`. Use a temp `build.bat` that calls `vcvarsall.bat amd64` then cmake, because bash→cmd quoting for paths with spaces is fragile. Delete after build.
- **AOB patterns are cheap insurance**: Each new pattern potentially covers a specific game/UE version that others miss. 51 patterns + 4 symbol exports from 6 sources (original, patternsleuth, RE-UE4SS, Dumper-7, UE4 Dumper.CT, UEDumper) provide ~90-95% coverage. Keep patterns in a dedicated `Signatures.h` file for easy contribution and maintenance.
- **`strstr` in validators is dangerous**: It ignores null terminators within the buffer, allowing matches across garbage bytes. Always use `strcmp` with exact length checks in security-critical validation paths.
- **Version detection needs context**: Bare version patterns like "5.6." can appear in game data (release notes, changelogs, embedded strings). Three-tier approach: (1) exact `++UE5+Release-` prefix, (2) "Release" context, (3) bare pattern with guard against preceding digit/period.
- **UE4 TNameEntryArray vs UE5 FNamePool**: Completely different data structures. UE4: `Chunks[chunkIdx]` → `FNameEntry*[0x4000]` (double deref, 16384 entries per chunk, string at fixed offset +0x10/+0x06). UE5: packed inline entries with 2-byte header in continuous chunks (single deref, header encodes length). Must detect and branch on mode at init time.
- **Unified validation helper**: `ValidateGNamesAny()` wraps FNamePool→Structural→UE4 validators. Single call site makes code DRYer and ensures new validators are automatically tried for all patterns.
- **Game naming convention**: FF7Re = Final Fantasy VII Rebirth, FF7R = Final Fantasy VII Remake Intergrade. Both are UE4 forks despite different version numbers.
- **Hash-prefixed FNameEntry (UE4.26)**: Some UE4 builds (notably Square Enix's UE4.26 fork used in FF7Re) prepend a 4-byte ComparisonId/hash to each FNameEntry before the standard 2-byte header. Layout: `[4B hash][2B header][string]` vs standard `[2B header][string]`. Validators must try header at both offset 0 and offset 4. The diagnostic clue: near-miss log showing "None" at offset 6 from the chunk pointer, with bytes before it being non-ASCII garbage.
- **Pagination: advance by scanned, not returned**: When the DLL skips unnamed/null objects in paginated results, the UI must advance by the number of indices scanned (`scanned` field), not by the number of objects returned (`objects.count`). Otherwise pagination stalls or loops infinitely when many objects have empty names.
- **RE-UE4SS CustomGameConfigs**: RE-UE4SS maintains per-game configs (`UE4SS-settings.ini` + `MemberVariableLayout.ini`) with engine version overrides and exact struct offsets. These are invaluable reference data for verifying our auto-detection. Located at `D:\Github\RE-UE4SS\assets\CustomGameConfigs\<GameName>\`.
- **FUObjectItem item[0] can be null**: GObjects arrays often have null in slot 0 (especially UE4). `DetectItemSize` must scan forward to find the first non-null item before validating strides. Don't require item[0] to be valid — scan chunk bytes in alignment steps.
- **Hash-prefixed FNameEntry stride = 4, not 2**: When FNameEntry has a 4-byte ComparisonId prefix, `alignof(FNameEntry)` becomes 4 (due to uint32_t member), and the FNameEntryAllocator stride is 4. The FName index offset-to-byte conversion must use `offset * 4` instead of `offset * 2`. Diagnostic clue: names like 'operty' (tail of 'Property') or '?' with some correct names intermixed → stride is wrong, indices land at wrong byte offsets within chunks.
- **ValidateGObjects must validate the chunk chain, not just NumElements**: Checking `*(addr+0x14)` in range is too weak — the upper 32 bits of a code-section pointer (e.g., `0x00007FF7` = 32759) can coincidentally fall in the valid NumElements range. Must also verify: (1) Objects pointer can be deref'd, (2) chunk[0] is a heap pointer (not in game module). This prevents false positives from deref'd heap addresses where pointer bytes masquerade as valid counts.
- **V12/RE2 pattern resolution: target-0x10 vs *(target)-0x10**: RE-UE4SS's GUObjectArray signatures resolve to the address of the ObjObjects field inside FUObjectArray, then subtract 0x10 to get the struct base. This is `target - 0x10` (no deref). Our code was doing `*(target) - 0x10` (deref first, then subtract), which gives a nonsensical heap-pointer-minus-16. Always try the non-deref `target - 0x10` first.
- **Build scripts should always force clean builds**: CMake's incremental build can miss source changes, causing the DLL to ship with stale code. The build script now deletes `build/` before cmake configure and `bin/obj/` before dotnet build, ensuring every build is fresh. Build number auto-increment in cmake configure also requires a fresh configure to update.
- **FF7R (Remake Intergrade) is UE4.18**: RE-UE4SS config confirms `EngineVersionOverride: MajorVersion=4, MinorVersion=18`. The game uses FProperty (not UProperty), and the FUObjectArray has ObjObjects at +0x10 (needs -0x10 adjustment from GUObjectArray pattern).
- **DQ I&II HD-2D Remake is UE5.05**: Detected via Tier 3 bare pattern "5.05". Standard FNamePool, stride 24, 128678 objects. Works with no special handling. GWorld=0 (null, common for some games).
- **Anchor-scan is fragile for stride detection**: Scanning chunk bytes in fine steps (e.g., 8-byte) to find a "first valid pointer" is unreliable because non-UObject fields (Flags, SerialNumber, padding) can look like valid pointers by coincidence. `LooksLikeUObject()` triple-deref check is too weak — random pointer-like values at 8-byte-aligned offsets can pass it. The correct approach: walk at stride-aligned positions only (offset = idx * stride, starting from 0) and validate with FNamePool string resolution (strong signal) rather than pointer chain checks (weak signal).
- **FNamePool should be initialized before ObjectArray**: This ordering enables using FName resolution as the primary validation signal for FUObjectItem stride detection. Name resolution is nearly impossible to get false positives on — it requires the correct stride AND a valid UObject AND correct FNamePool parameters.
- **LCM alignment creates false positive strides**: For items of true stride S, probing with stride S' finds valid items at positions that are multiples of `LCM(S, S')`. For S=24 and S'=16, LCM=48, so every 3rd 16-byte position (0, 48, 96...) lands on a real 24-byte boundary. The `named` count can be equal across strides — always add a tiebreaker (fewer bad items = correct stride, since wrong strides read misaligned data as bad).
- **Early exit bias**: `if (outNamed >= 5) break` makes all strides stop at same named count when LCM alignment provides enough matches. The early exit is needed for performance but the tiebreaker (bad count) is essential to disambiguate.
- **Some UE4 builds use flat (non-chunked) GObjects arrays**: Instead of `FUObjectArray → Objects** (chunk table) → chunk[] → FUObjectItem`, the array is `Objects* → FUObjectItem[]` directly. Detection: if chunked probing finds nothing, try the Objects pointer itself as the item base. Must update `GetByIndex` to skip the chunk table deref in flat mode.
- **Per-process mirror logging aids cross-session comparison**: Writing logs to both the main `Logs/` folder and a `Logs/<ProcessName>/` subfolder allows comparing current vs previous runs without renaming files. 2-version rotation per process keeps disk usage bounded.
- **DetectLayout must validate Objects pointer**: The alternate layout check `*(addr+0x04) > 0` is too loose — values like `ObjLastNonGCIndex` can match. Always validate that `*(addr + objectsOffset)` is a heap-like pointer (non-null, user-mode, NOT in the game module's code section). This catches cases where a layout candidate puts `objectsOffset` on a field that contains code addresses instead of heap pointers.
- **UE4 FUObjectArray has extra members before ObjObjects**: Fields like `ObjFirstGCIndex`, `ObjLastNonGCIndex`, `MaxObjectsNotConsideredByGC`, `OpenForDisregardForGC` can appear at offsets 0x00-0x0C, pushing `ObjObjects.Objects**` to +0x10. The `Max` at +0x10 in default layout may actually be a PreAllocatedObjects pointer. Multiple layout candidates with pointer validation are needed.
- **GWorld null is common in UE4**: Many UE4 games have GWorld=0 or garbage. The DLL's `walk_world` command has a fallback: searches GObjects for a UWorld instance. This finds `Default__World` (CDO) or actual world instances.
- **FProperty-to-UProperty fallback**: When version is misdetected (e.g., 504 instead of 418), `DetectUPropertyMode` selects FProperty mode. But `ValidateAndFixOffsets` fails to find ChildProperties (FFieldClass check fails on UProperty). Solution: when FProperty scan fails, retry with UProperty scan (check UObject::ClassPrivate for "Property" class name). This auto-corrects the mode even with wrong version detection.
- **RE-UE4SS CustomGameConfigs are the ground truth for UE4 offset validation**: `MemberVariableLayout.ini` provides exact struct member offsets per game. FF7R (UE4.18): SuperStruct=0x30, Children=0x38, UProperty::Offset_Internal=0x44. These are a 0x10 offset shift from UE5 defaults due to smaller UObject base size.
- **UE4.18 uses FNamePool, not TNameEntryArray**: Despite being a very old UE version, FF7R (Square Enix UE4.18 fork) uses FNamePool with standard 2-byte header (not UE4 TNameEntryArray). The validator correctly identified this. Don't assume UE4 = TNameEntryArray.
- **FName index 1 can be invalid**: In FNamePool, FName indices are allocation offsets (byte_offset / stride), not sequential entry indices. Index 1 (byte offset 2 with stride=2) falls INSIDE entry[0]'s "None" string (header=2 bytes + "None"=4 bytes = 6 bytes). This is normal — index 1 is simply an unallocated/invalid index. Don't use FName[1] as a diagnostic indicator; use actual object name indices instead.
- **Stride detection early exit creates unfair comparisons**: `if (outNamed >= 5) break;` in ProbeStride causes all strides to stop at the same named count when LCM alignment provides enough matches. This makes the comparison meaningless — all strides show 5 named. Removing the early exit and scanning all items equally reveals the true signal: correct strides have near-zero bad items, wrong strides have many bad items from misaligned reads.
- **Bad items are the strongest stride signal**: When LCM alignment causes wrong strides to find named items (e.g., LCM(16,24)=48 means stride 16 hits real stride-24 items every 3rd slot), the named count becomes unreliable. But the bad count is highly discriminative: the correct stride reads valid data (objects or null) at every position, while wrong strides read misaligned garbage at most positions. Scoring formula `named * 10 - bad * 3` correctly penalizes this.
- **Hogwarts Legacy uses UE4.27**: PE VERSIONINFO correctly detects it as UE4.27 (version=427). Despite being a large AAA game, it uses FProperty mode (UE4.25+) with FField::Name at +0x28, FField::Next at +0x20. The 'b' field in Guid (lowercase) is a minor anomaly but doesn't affect functionality.
- **EverSpace 2 and Hogwarts Legacy require GNames pointer-scan fallback**: All 17 standard GNames AOB patterns fail on these UE5.5/4.27 games. The pointer-scan fallback (`FindGNamesByPointerScan`) successfully finds FNamePool by scanning `.data` section for pointers to "None" FNameEntry. This pattern may become the primary discovery method for newer UE builds.
- **OctoPath Traveler uses stride 16 (not 24)**: Despite being UE4, OctoPath's FUObjectItem is 16 bytes. This was confirmed by correct Object Tree operation with stride 16. Not all UE4 games use 24-byte items. The stride depends on whether the engine was compiled with GC clustering support.
- **UE4.11-4.20 uses FFixedUObjectArray (flat), not FChunkedFixedUObjectArray**: RE-UE4SS MemberVariableLayout_4_18_Template.ini confirms `TUObjectArray` is 16 bytes: `{FUObjectItem* Objects, int32 MaxElements, int32 NumElements}`. Objects points directly to the item array (single indirection). UE4.21+ switches to `FChunkedFixedUObjectArray` (32 bytes) with a chunk pointer table (double indirection). Detection: when numElements > OBJECTS_PER_CHUNK (need 2+ chunks), check if `*(Objects + 8)` is a valid heap pointer. If not (e.g., it's `0x40000000` = EObjectFlags), the array is flat.
- **EObjectFlags 0x40000000 = Const**: In UE4's FUObjectItem, the Flags field at item+0x08 can contain values like `0x40000000` (RF_Const) which look like small integers, not pointers. When a flat item array is misidentified as a chunk table, reading item[0].Flags as "chunk[1]" gives these flag values, which correctly fail `LooksLikeHeapPtr()`.
- **Flat array detection must happen BEFORE chunked probing**: If flat detection only runs as fallback (Phase 3, when chunked finds nothing), it won't trigger for FF7R because chunked Phase 1 accidentally finds some valid items via LCM alignment when reading UObject internal memory. The fix: check chunk[1] validity FIRST, and if suspicious, try flat layout before chunked.

### Recommended Test Games

| Game | UE Version | Notes |
|------|-----------|-------|
| EverSpace 2 | UE5.5 (PE: 505) | GNames via pointer-scan fallback. Stride 24, 1.16M objects — fully working (build 1.0.0.20) |
| Titan Quest II | UE5.7 (PE reports) | Primary test target for CasePreservingName + dynamic offset detection. Stride 16 |
| OctoPath Traveler | UE4 (pre-4.25) | UE4 TNameEntryArray + Layout B + stride 16. Object Tree working (build 1.0.0.20). "Start from GWorld" fails in UI |
| Final Fantasy VII Rebirth (FF7Re) | UE4.26 fork | Hash-prefixed FNameEntry (hdrOff=4, stride=4) + stride 24 — fully working. CE address-to-object lookup fails |
| Final Fantasy VII Remake Intergrade (FF7R) | UE4.18 fork | UProperty fallback ✅. Flat FFixedUObjectArray fix pending test (build 1.0.0.26). 165792 objects |
| DQ I&II HD-2D Remake | UE5.05 | Stride 24, 128678 objects — full pipeline working. GWorld null. CE pointer lookup works |
| Tower of Mask | UE4.27 | Standard UE4 indie game — full pipeline confirmed working. Stride 24 |
| Hogwarts Legacy | UE4.27 (PE: 427) | GNames via pointer-scan fallback. Stride 24, 379K objects — fully working (build 1.0.0.20) |
| IDOLM@STER STARLIT SEASON | UE4.24 | Working (build 1.0.0.25). "Start from GWorld" fails in UI |
| Romancing SaGa 2 | UE4.27 | Working (build 1.0.0.25). "Start from GWorld" fails in UI |
| Star Wars Jedi: Fallen Order | UE4.21 | Working (build 1.0.0.25). 318022 objects |

### Next Steps

1. **Test FF7R** with flat array fix (build 1.0.0.26):
   - Expect: flat array detected via chunk[1] validation
   - Expect: stride 24 selected with good named count, low bad
   - Expect: 8-10/10 name sanity, correct Object Tree names
   - Expect: Live Walker with correct fields (UProperty mode already working)
2. **Regression test** all 10+ games: TQ2, OctoPath, FF7Re, DQ HD-2D, FF7R, EverSpace 2, Hogwarts Legacy, Tower of Mask, Star Wars Jedi, IDOLM@STER
3. **Investigate FF7Re CE address-to-object lookup failure** — works on DQ but not FF7Re
4. **Fix "Start from GWorld" UI** — GWorld found for OctoPath, FF7Re, IDOLM@STER, Romancing SaGa 2 but UI button doesn't work
5. ~~Test pointer-scan fallback for GNames on EverSpace 2~~ → Done: works on both ES2 and Hogwarts Legacy
6. Once FF7R confirmed working → continue Avalonia UI development
7. ~~Consider adding more GNames AOB patterns~~ → Done: 51 patterns from 6 sources in `Signatures.h`

-----

## Project Goal

Develop a **C++ DLL + Cheat Engine Lua Bridge** Unreal Engine Dumper.
Target: Support UE4 (4.22+) and UE5 (5.0 ~ 5.7+), find global pointers (GObjects / GNames / GWorld), build complete object/struct hierarchy, integrate with CE. Despite the "UE5" name, UE4 games are a priority target — many popular games use UE4, and RE-UE4SS demonstrates broad UE4 support is achievable.

-----

## Architecture Overview

```
+------------------------------------------------------+
|  Game Process                                        |
|                                                      |
|  CE Lua (ue5dump.lua)                                |
|      |  loadLibrary / callFunction                   |
|      v                                               |
|  UE5Dumper.dll  [injected]                           |
|      +-- ExportAPI       <- C ABI for CE Lua         |
|      +-- PipeServer      <- Named Pipe IPC server    |
|      +-- Signatures      <- 51 AOB patterns + 4 exports|
|      +-- OffsetFinder    <- AOB scan GObjects/GNames |
|      +-- ObjectArray     <- FChunkedFixedUObjectArray|
|      +-- FNamePool       <- UE5 FNamePool reader     |
|      +-- UStructWalker   <- FField chain traversal   |
|      +-- Memory          <- Base read/write wrapper  |
+----------------------+-------------------------------+
                       | Named Pipe: \\.\pipe\UE5DumpBfx
                       | JSON over pipe (newline-delimited)
+----------------------v-------------------------------+
|  UE5DumpUI  (Avalonia + ReactiveUI, standalone exe)  |
|      +-- PipeClient          <- async pipe mgmt     |
|      +-- ObjectTreePanel     <- UObject hierarchy    |
|      +-- ClassStructPanel    <- Property Grid        |
|      +-- PointerPanel        <- GObjects/GNames addr |
|      +-- HexViewPanel        <- Live Memory Hex View |
+------------------------------------------------------+
```

-----

## Detailed Specifications

Reference documentation has been moved to `docs/` to keep this file focused on session-critical content:

- [docs/architecture.md](docs/architecture.md) — Directory structure, git submodules, build environment (CMakeLists, csproj)
- [docs/dll-spec.md](docs/dll-spec.md) — C++ DLL interface definitions (Memory.h, OffsetFinder.h, ObjectArray.h, FNamePool.h, UStructWalker.h, ExportAPI.h, PipeServer.h), offset tables, CE Lua bridge
- [docs/pipe-protocol.md](docs/pipe-protocol.md) — Named Pipe JSON IPC protocol (Request/Response/Event formats)
- [docs/ui-spec.md](docs/ui-spec.md) — Avalonia UI tech stack, AOT compatibility, component skeletons (PipeClient, DumpService, ViewModels, AXAML layouts)
- [docs/references.md](docs/references.md) — Reference projects (Dumper-7, UEDumper, UE4SS), future extension ideas
- [docs/ue4ss-analysis.md](docs/ue4ss-analysis.md) — UE4SS (RE-UE4SS) analysis: CasePreservingName handling, Lua scripting, property iteration
- [docs/simd-scanning-notes.md](docs/simd-scanning-notes.md) — AOBMaker SIMD scanning research: AVX2 anchor-based technique, comparison with our scanner

-----

## Key Technical Notes

### UE Version Differences

| Version | Key Differences |
|---------|----------------|
| UE5.0 ~ 5.1 | FNamePool standard format; FFieldVariant 0x10 (UE5.0-5.1.0), 0x08 (UE5.1.1+) |
| UE5.2 | `FChunkedFixedUObjectArray` stride may differ |
| UE5.3+ | Some games enable Object Pointer Encryption |
| UE5.4+ | `FField` chain structure stable, no major changes |
| UE5.5+/5.7 | **CasePreservingName**: FName 0x10 bytes (adds DisplayIndex), shifts FField::Flags +0x8, all FProperty offsets shift +0x8. Must use `DynOff` dynamic detection |

### FField vs UProperty

- Before UE4.24: `UProperty` (inherits UObject)
- UE4.25+ / All UE5: `FField` (**does not** inherit UObject)
- `UStruct::ChildProperties` = `FField*` chain head
- `UStruct::Children` = `UField*` chain (for functions, UFunction inherits UObject)
- WalkClass must handle both chains

### FNamePool Chunk Calculation

```cpp
// FNamePool layout
// Chunks: uintptr_t* array
// Each chunk max 0x20000 bytes
// Stride: each FNameEntry aligned to 2 bytes

uintptr_t GetNameEntry(int32_t nameIndex) {
    int32_t chunkIndex  = nameIndex >> 16;       // high 16 bits = chunk
    int32_t chunkOffset = (nameIndex & 0xFFFF) * 2; // low 16 bits * stride
    uintptr_t chunk = Mem::Read<uintptr_t>(GNames + chunkIndex * 8);
    return chunk + chunkOffset;
}
```

### Export Function Naming Rules

- All exports prefixed with `UE5_`
- Avoid callbacks across DLL boundary (use Begin/Get/End batch mode instead)
- Buffers allocated by caller (Lua side), DLL only writes into them

-----

## Implementation Phases

### Phase 1 — DLL Core

1. `Memory.cpp` — AOBScan + GetModuleBase
2. `OffsetFinder.cpp` — GObjects / GNames pattern scan
3. `ObjectArray.cpp` — FChunkedFixedUObjectArray + ForEach
4. `FNamePool.cpp` — GetString
5. `ExportAPI.cpp` — C ABI wrapper, CE Lua verification

### Phase 2 — Pipe IPC

1. `PipeServer.cpp` — Named Pipe server + JSON dispatch
2. CE Lua update: reduced to init + StartPipeServer only
3. PowerShell pipe testing (`[System.IO.Pipes.NamedPipeClientStream]`)

### Phase 3 — UI App

1. Avalonia project skeleton + ReactiveUI + Dark theme
2. `PipeClient.cs` — connection + send/receive + ReadLoop
3. `DumpService.cs` — business logic wrapper
4. `PointerPanel` — simplest, verify pipe connection first
5. `ObjectTreePanel` — paginated loading, virtualized TreeView
6. `ClassStructPanel` — walk_class -> DataGrid display
7. `HexViewPanel` — read_mem + live watch

### Phase 4 — Polish

1. UStructWalker full implementation (FField chain + SuperStruct inheritance chain)
2. Object Tree search / filter
3. Single-file publish setup and testing
