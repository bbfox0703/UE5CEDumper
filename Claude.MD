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

> Last updated: 2026-02-21 (Pagination fix + hash-prefixed FNameEntry support for UE4.26/FF7Re)
> Latest commit: `1087931` on `dev` branch

### Component Status

| Component | File(s) | Status | Notes |
|-----------|---------|--------|-------|
| Memory module | `Memory.cpp/.h` | Done | AOBScan (exec sections only), ResolveRIP, ReadSafe (SEH) |
| Logger | `Logger.cpp/.h` | Done | Dual-channel (scan+pipe), category tags, SUMMARY level, 5-version rotation, 5MB cap, thread-safe; commit `c9263ed` |
| AOB Signatures | `Signatures.h` | Done | 51 patterns + 4 symbol exports from 6 sources (original, patternsleuth, RE-UE4SS, Dumper-7, UE4 Dumper.CT, UEDumper) |
| OffsetFinder — GObjects | `OffsetFinder.cpp` | Working | 27 AOB patterns + deref variants + MSVC symbol export (`GUObjectArray`); validated in TQ2 (UE5.7) |
| OffsetFinder — GNames | `OffsetFinder.cpp` | Partial | 17 AOB patterns + FName ctor export + `.data` pointer-scan + UE4 TNameEntryArray validator; unified `ValidateGNamesAny` tries FNamePool→Structural→UE4; structural validator hardened with exact header format + corroboration |
| OffsetFinder — GWorld | `OffsetFinder.cpp` | Done | 7 patterns, non-critical |
| OffsetFinder — Version | `OffsetFinder.cpp` | Working | PE VERSIONINFO (ProductVersion + FileVersion) + 3-tier memory string scan (++UE prefix → Release context → bare pattern with digit guard) |
| OffsetFinder — DynOff | `OffsetFinder.cpp`, `Constants.h` | Untested | Dynamic FField/FProperty/UStruct offset detection via known struct probing |
| ObjectArray | `ObjectArray.cpp/.h` | Done | FChunkedFixedUObjectArray, ForEach, GetByIndex |
| FNamePool | `FNamePool.cpp/.h` | Done | Triple-mode: UE5 FNamePool (multi-format header A/B, chunk index) + UE4 TNameEntryArray (double-deref, 0x4000 chunk size) + hash-prefixed FNamePool (4B hash + 2B header, auto-detected) |
| UStructWalker | `UStructWalker.cpp/.h` | Done | FField chain, SuperStruct inheritance, GetFullName; uses DynOff for all offsets |
| ExportAPI | `ExportAPI.cpp/.h` | Done | C ABI exports, UE5_AutoStart, WalkClass Begin/Get/End; calls ValidateAndFixOffsets |
| PipeServer | `PipeServer.cpp/.h` | Done | Named pipe, JSON dispatch, watch/unwatch, get_offsets, walk_world, find_instances, get_ce_pointer_info; get_object_list returns `scanned` count for proper pagination |
| CE Cheat Table | `scripts/UE5CEDumper.CT` | Done | injectDLL-only flow (no executeCodeEx), 15s countdown |
| CEPlugin interface | `CEPlugin.cpp` | Done | CE plugin Type 5 main menu; g_isCEPlugin flag |
| DLL auto-start | `dllmain.cpp` | Done | AutoStartThreadProc, 1s delay, g_isCEPlugin check |
| Avalonia UI | `ui/` | Skeleton done | Object tree + Instance Finder + Class struct panels; pipe integration in progress |

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

### Recommended Test Games

| Game | UE Version | Notes |
|------|-----------|-------|
| EverSpace 2 | UE5.1-5.3 (est.) | Ships with `.pdb` — ideal for validating scanner against known symbols |
| Titan Quest II | UE5.7 (PE reports) | Primary test target for CasePreservingName + dynamic offset detection |
| OctoPath Traveler | UE4 | UE4 TNameEntryArray test target — pipe connection failed pre-fix |
| Final Fantasy VII Rebirth (FF7Re) | UE4 fork | Garbled names + wrong version pre-fix — tests version detection + GNames validation hardening |

### Next Steps

1. **Test FF7Re** — verify hash-prefixed FNameEntry detection works: GNames found, names resolve correctly, pipe server starts, UI connects
2. **Test OctoPath Traveler** — verify pagination fix: Object Tree populates, field names visible in Live Walker
3. **Test no regression** on TQ2 (UE5.7) — verify GNames still found, names correct, DynOff detection works
4. **Test dynamic offset detection** on TQ2 — check `[SUMMARY]` + `[DYNO]` entries in scan log, verify via `get_offsets` pipe command
5. Verify Live Walker field names correct (not "rty") and offsets non-zero on TQ2
6. **Test pointer-scan fallback for GNames** on EverSpace 2 (use `.pdb` to verify address)
7. Once UE4 + DynOff confirmed working → continue Avalonia UI development
8. ~~Consider adding more GNames AOB patterns~~ → Done: 51 patterns from 6 sources in `Signatures.h`

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
