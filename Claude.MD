# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> For detailed specs, implementation history, and debugging notes, see the **[docs/](docs/)** directory.

-----

## Build & Dev Commands

### Unified Build Script (PREFERRED — always use this)

The `build.ps1` script handles VS DevShell setup, CMake, dotnet, and test execution automatically. **Always use this for building** — bare `cmake` / `dotnet build` commands will fail without the VS DevShell environment.

```bash
# Build everything (DLL + UI + Tests) — Release
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Github\UE5CEDumper\build.ps1"

# Build DLL only
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Github\UE5CEDumper\build.ps1" -Target DLL

# Build UI only
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Github\UE5CEDumper\build.ps1" -Target UI

# Run tests only
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Github\UE5CEDumper\build.ps1" -Target Test

# Debug build
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Github\UE5CEDumper\build.ps1" -Mode Debug

# Clean + publish (distribution)
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Github\UE5CEDumper\build.ps1" -Mode Publish -Clean
```

> **Why not bare cmake/dotnet?** The C++ DLL requires MSVC x64 environment (include paths, linker). `build.ps1` loads this via `Enter-VsDevShell` automatically. Running `cmake --build` without it causes `fatal error C1083: No such file or directory` for standard headers.

### Manual Commands (reference only — prefer build.ps1)

```bash
# C++ DLL (requires VS DevShell loaded first)
cmake -S . -B build -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release

# C# UI
dotnet build "ui/UE5DumpUI/UE5DumpUI.csproj" -c Release
dotnet test "ui/UE5DumpUI.Tests/UE5DumpUI.Tests.csproj"
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
- **Log output**: All logs go to `%LOCALAPPDATA%\UE5CEDumper\Logs`, split by category into per-process subfolders. DLL: 5 files (init, scan, offsets, pipe, walk). UI: 3 files (init, pipe, view). 4-file rotation, 8MB max per file. Root `Logs/` has no loose files — only subfolders
- **Magic words management**: All magic strings kept in one file per project with proper comments
- **UI Strings**: English only. All UI strings in `Resources/Strings/en.axaml`, referenced via `StaticResource` bindings
- **UE offsets**: All UObject/UStruct offsets must be dynamically verified via OffsetFinder, never hardcoded
- **AOT compatible**: All C# code must be Native AOT / trimming compatible. No reflection-based APIs — use source generators instead (e.g. `[JsonSerializable]` context for `System.Text.Json`, `[ObservableProperty]` for MVVM). The UI is published as a self-contained trimmed binary

-----

## Project Goal

Develop a **C++ DLL + Cheat Engine Lua Bridge** Unreal Engine Dumper.
Target: Support UE4 (4.18+) and UE5 (5.0 ~ 5.7+), find global pointers (GObjects / GNames / GWorld), build complete object/struct hierarchy, integrate with CE. Despite the "UE5" name, UE4 games are a priority target — many popular games use UE4, and RE-UE4SS demonstrates broad UE4 support is achievable.

-----

## Architecture Overview

```
+------------------------------------------------------+
|  Game Process                                        |
|                                                      |
|  CE Lua                                              |
|    ue5dump.lua        <- DLL inject + pipe start     |
|    ue5_dissect.lua    <- CE Structure Dissect builder |
|      |  loadLibrary / callFunction (28 C ABI exports)|
|      v                                               |
|  UE5Dumper.dll  [injected]                           |
|      +-- ExportAPI       <- C ABI for CE Lua         |
|      +-- PipeServer      <- Named Pipe IPC (15 cmds) |
|      +-- Signatures      <- 99 AOB + 5 exports       |
|      +-- OffsetFinder    <- GObjects/GNames/GWorld   |
|      +-- ObjectArray     <- Chunked + Flat arrays    |
|      +-- FNamePool       <- UE5 Pool / UE4 TNameEntry|
|      +-- UStructWalker   <- FField + UProperty chains|
|      +-- Memory          <- AOBScan + SEH read/write |
|      +-- Logger          <- 5-category per-process   |
+----------------------+-------------------------------+
                       | Named Pipe: \\.\pipe\UE5DumpBfx
                       | JSON over pipe (newline-delimited)
+----------------------v-------------------------------+
|  UE5DumpUI  (Avalonia + ReactiveUI, standalone exe)  |
|      +-- PipeClient          <- async pipe mgmt     |
|      +-- ObjectTreePanel     <- UObject hierarchy    |
|      +-- ClassStructPanel    <- Property Grid        |
|      +-- PointerPanel        <- GObjects/GNames addr |
|      +-- LiveWalkerPanel     <- Instance drill-down  |
|      +-- InstanceFinderPanel <- Class instance search|
|      +-- ProxyDeployPanel   <- Proxy DLL management |
|      +-- CeXmlExport        <- CE XML pointer chains |
|      +-- CsxExport          <- CE Structure Dissect  |
+------------------------------------------------------+
```

-----

## Documentation Index

| Document | Contents |
|----------|----------|
| [docs/dev-log.md](docs/dev-log.md) | Implementation status, 60+ features, known challenges & fixes, next steps |
| [docs/lessons-learned.md](docs/lessons-learned.md) | Hard-won lessons from cross-game debugging (20 games) |
| [docs/test-games.md](docs/test-games.md) | 20 test games with UE versions, GWorld status, stride info |
| [docs/technical-notes.md](docs/technical-notes.md) | UE version differences, FField vs UProperty, FNamePool, DynOff detection flow |
| [docs/architecture.md](docs/architecture.md) | Directory structure, git submodules, build environment |
| [docs/dll-spec.md](docs/dll-spec.md) | C++ DLL interface, offset tables, CE Lua bridge exports |
| [docs/pipe-protocol.md](docs/pipe-protocol.md) | Named Pipe JSON IPC protocol (15 commands, Request/Response/Event) |
| [docs/ui-spec.md](docs/ui-spec.md) | Avalonia UI tech stack, AOT compatibility, component specs |
| [docs/references.md](docs/references.md) | Reference projects (Dumper-7, UEDumper, UE4SS), future ideas |
| [docs/ue4ss-analysis.md](docs/ue4ss-analysis.md) | UE4SS/Dumper-7/UEDumper analysis: CasePreservingName, GNames patterns |
| [docs/simd-scanning-notes.md](docs/simd-scanning-notes.md) | AOBMaker SIMD scanning research: AVX2 anchor technique |
| [docs/CE-Bugs-Minesweeper.md](docs/CE-Bugs-Minesweeper.md) | CE-specific quirks and undocumented API behaviors |
| [docs/export-formats.md](docs/export-formats.md) | CE XML, CSX, SDK Header export rules, pointer chain model, type mappings |
