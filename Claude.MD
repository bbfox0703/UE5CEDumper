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

> Last updated: 2026-02-20 (Dual-channel logger with category tags + SUMMARY)

### Component Status

| Component | File(s) | Status | Notes |
|-----------|---------|--------|-------|
| Memory module | `Memory.cpp/.h` | Done | AOBScan (exec sections only), ResolveRIP, ReadSafe (SEH) |
| Logger | `Logger.cpp/.h` | Done | Dual-channel (scan+pipe), category tags, SUMMARY level, 5-version rotation, 5MB cap, thread-safe; commit `c9263ed` |
| OffsetFinder — GObjects | `OffsetFinder.cpp` | Working | 5 AOB patterns + deref variants + MSVC symbol export (`GUObjectArray`); validated in TQ2 (UE5.7) |
| OffsetFinder — GNames | `OffsetFinder.cpp` | Partial | 7 AOB patterns + FName ctor export + `.data` pointer-scan with corroboration; AOBs fail on TQ2, fallbacks untested |
| OffsetFinder — GWorld | `OffsetFinder.cpp` | Done | 6 patterns, non-critical |
| OffsetFinder — Version | `OffsetFinder.cpp` | Working | PE VERSIONINFO fast path + memory string scan fallback |
| OffsetFinder — DynOff | `OffsetFinder.cpp`, `Constants.h` | Untested | Dynamic FField/FProperty/UStruct offset detection via known struct probing; commit `8c2f983` |
| ObjectArray | `ObjectArray.cpp/.h` | Done | FChunkedFixedUObjectArray, ForEach, GetByIndex |
| FNamePool | `FNamePool.cpp/.h` | Done | Multi-format header (A/B), chunk index, GetString |
| UStructWalker | `UStructWalker.cpp/.h` | Done | FField chain, SuperStruct inheritance, GetFullName; uses DynOff for all offsets |
| ExportAPI | `ExportAPI.cpp/.h` | Done | C ABI exports, UE5_AutoStart, WalkClass Begin/Get/End; calls ValidateAndFixOffsets |
| PipeServer | `PipeServer.cpp/.h` | Done | Named pipe, JSON dispatch, watch/unwatch, get_offsets, walk_world, find_instances, get_ce_pointer_info |
| CE Cheat Table | `scripts/UE5CEDumper.CT` | Done | injectDLL-only flow (no executeCodeEx), 15s countdown |
| CEPlugin interface | `CEPlugin.cpp` | Done | CE plugin Type 5 main menu; g_isCEPlugin flag |
| DLL auto-start | `dllmain.cpp` | Done | AutoStartThreadProc, 1s delay, g_isCEPlugin check |
| Avalonia UI | `ui/` | Not started | Pending DynOff validation on TQ2 and end-to-end pipe validation |

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

### Recommended Test Games

| Game | UE Version | Notes |
|------|-----------|-------|
| EverSpace 2 | UE5.1-5.3 (est.) | Ships with `.pdb` — ideal for validating scanner against known symbols |
| Titan Quest II | UE5.7 (PE reports) | Primary test target for CasePreservingName + dynamic offset detection |

### Next Steps

1. **Test dynamic offset detection** on TQ2 — inject DLL, check `UE5Dumper-scan-0.log` for `[SUMMARY]` lines and `[DYNO]` category entries, verify via `get_offsets` pipe command
2. Verify Live Walker field names are correct (not "rty") and offsets are non-zero
3. Verify Instance Finder can locate `bp_tq2_character_stats_component_C` via top-down GWorld navigation
4. **Test pointer-scan fallback for GNames** on EverSpace 2 (use `.pdb` to verify address)
5. Once both GNames + DynOff confirmed working → begin Avalonia UI (`Phase 3` in plan)
6. Consider adding more GNames AOB patterns specific to UE5.5+ codegen patterns

-----

## Project Goal

Develop a **C++ DLL + Cheat Engine Lua Bridge** Unreal Engine 5 Dumper.
Target: Support all UE5 versions (5.0 ~ 5.4+), find global pointers (GObjects / GNames / GWorld), build complete object/struct hierarchy, integrate with CE.

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
