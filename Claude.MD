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
- **Log output**: All logs go to `%LOCALAPPDATA%\UE5CEDumper\Logs`, split by category into per-process subfolders. DLL: 5 files (init, scan, offsets, pipe, walk). UI: 3 files (init, pipe, view). 2-file rotation, 8MB max per file. Root `Logs/` has no loose files — only subfolders
- **Magic words management**: All magic strings kept in one file per project with proper comments
- **UI Strings**: English only. All UI strings in `Resources/Strings/en.axaml`, referenced via `StaticResource` bindings
- **UE offsets**: All UObject/UStruct offsets must be dynamically verified via OffsetFinder, never hardcoded

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
|      +-- Signatures      <- 95 AOB patterns + 5 exports
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

## Documentation Index

| Document | Contents |
|----------|----------|
| [docs/dev-log.md](docs/dev-log.md) | Implementation status, known challenges & fixes (20 items), next steps |
| [docs/lessons-learned.md](docs/lessons-learned.md) | Hard-won lessons from cross-game debugging |
| [docs/test-games.md](docs/test-games.md) | Recommended test game list with UE versions and GWorld status |
| [docs/technical-notes.md](docs/technical-notes.md) | UE version differences, FField vs UProperty, FNamePool chunk calc, DynOff, implementation phases |
| [docs/architecture.md](docs/architecture.md) | Directory structure, git submodules, build environment (CMakeLists, csproj) |
| [docs/dll-spec.md](docs/dll-spec.md) | C++ DLL interface definitions, offset tables, CE Lua bridge |
| [docs/pipe-protocol.md](docs/pipe-protocol.md) | Named Pipe JSON IPC protocol (Request/Response/Event formats) |
| [docs/ui-spec.md](docs/ui-spec.md) | Avalonia UI tech stack, AOT compatibility, component skeletons |
| [docs/references.md](docs/references.md) | Reference projects (Dumper-7, UEDumper, UE4SS), future extension ideas |
| [docs/ue4ss-analysis.md](docs/ue4ss-analysis.md) | UE4SS/Dumper-7/UEDumper analysis: CasePreservingName, property iteration, GNames patterns |
| [docs/simd-scanning-notes.md](docs/simd-scanning-notes.md) | AOBMaker SIMD scanning research: AVX2 anchor-based technique |
