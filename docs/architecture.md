# Architecture & Build Environment

> For build commands, see [CLAUDE.md](../CLAUDE.md). For DLL/UI interface details, see [dll-spec.md](dll-spec.md) and [ui-spec.md](ui-spec.md).

-----

## Directory Structure

```
UE5CEDumper/
├── CLAUDE.md                       ← Dev rules + build commands + docs index
├── CMakeLists.txt                  ← Root CMake (delegates to dll/)
├── build_number.txt                ← Auto-incremented build counter
├── build.cmd / build.ps1           ← Full clean-build scripts (DLL + UI)
├── .gitmodules
│
├── dll/                            ← C++ DLL (injected into game process)
│   ├── CMakeLists.txt              ← DLL build config (versioning, git hash, deps)
│   └── src/
│       ├── dllmain.cpp             ← DLL_PROCESS_ATTACH, AutoStartThreadProc
│       ├── CEPlugin.cpp            ← CE plugin Type 5 main menu, g_isCEPlugin flag
│       ├── Constants.h             ← Magic strings, pipe name, UObject offsets, DynOff namespace
│       ├── Signatures.h            ← 51 AOB patterns + 4 symbol exports (6 sources)
│       ├── BuildInfo.h.in          ← Template → BuildInfo.h (version, git hash)
│       ├── version.rc              ← Win32 PE VERSIONINFO resource
│       │
│       ├── Memory.cpp / .h         ← AOBScan, ResolveRIP, ReadSafe (SEH), TArrayView
│       ├── Logger.cpp / .h         ← Category-routed logging (5 files: init/scan/offsets/pipe/walk)
│       ├── OffsetFinder.cpp / .h   ← GObjects/GNames/GWorld scan, DynOff detection
│       ├── ObjectArray.cpp / .h    ← Chunked + flat UObject array, stride detection
│       ├── FNamePool.cpp / .h      ← UE5 FNamePool + UE4 TNameEntryArray + hash-prefixed mode
│       ├── UStructWalker.cpp / .h  ← FField/UProperty chain, WalkInstance, array phases
│       │
│       ├── ExportAPI.cpp / .h      ← C ABI exports (UE5_Init, UE5_AutoStart, WalkClass, ...)
│       ├── PipeServer.cpp / .h     ← Named pipe IPC server, JSON dispatch
│       └── PipeProtocol.h          ← Shared JSON command/field name constants
│
├── docs/                           ← Documentation
│   ├── architecture.md             ← This file
│   ├── dev-log.md                  ← Implementation status, 20 known challenges, next steps
│   ├── dll-spec.md                 ← C++ header definitions, offset tables, CE Lua bridge
│   ├── pipe-protocol.md            ← Named Pipe JSON IPC protocol
│   ├── ui-spec.md                  ← Avalonia UI tech stack, component skeletons
│   ├── technical-notes.md          ← UE version diffs, FField vs UProperty, FNamePool internals
│   ├── lessons-learned.md          ← Hard-won debugging lessons
│   ├── test-games.md               ← Recommended test games with UE version + status
│   ├── references.md               ← Reference projects, future ideas
│   ├── ue4ss-analysis.md           ← UE4SS/Dumper-7/UEDumper analysis
│   ├── simd-scanning-notes.md      ← AOBMaker SIMD scanning research
│   ├── CE-Bugs-Minesweeper.md      ← CE-specific bug notes
│   └── private/                    ← Private/scratch notes
│
├── ui/                             ← C# Avalonia UI App
│   ├── UE5DumpUI.sln
│   ├── UE5DumpUI.Tests/            ← xUnit test project (42 tests)
│   └── UE5DumpUI/
│       ├── UE5DumpUI.csproj
│       ├── Program.cs              ← Avalonia entry point
│       ├── App.axaml / .cs
│       ├── app.manifest
│       ├── ViewLocator.cs
│       ├── Constants.cs            ← UI magic strings
│       │
│       ├── Models/                 ← IPC response models + UI data models
│       │   ├── UObjectNode.cs
│       │   ├── LiveFieldValue.cs   ← Rich field value (typed, hex, arrays, enums)
│       │   ├── InstanceWalkResult.cs
│       │   ├── ClassInfoModel.cs
│       │   ├── FieldInfoModel.cs
│       │   ├── ObjectListResult.cs
│       │   ├── FindInstancesResult.cs
│       │   ├── WorldWalkResult.cs
│       │   ├── AddressLookupResult.cs
│       │   ├── CePointerInfo.cs
│       │   ├── EngineState.cs
│       │   ├── HexViewRow.cs
│       │   ├── InstanceResult.cs
│       │   └── ObjectDetail.cs
│       │
│       ├── Services/               ← Business logic + IPC
│       │   ├── PipeClient.cs       ← Async named pipe client
│       │   ├── DumpService.cs      ← All pipe request/response helpers
│       │   ├── CeXmlExportService.cs ← CE XML generation (Phase A–C arrays)
│       │   ├── LoggingService.cs   ← Serilog setup (3 loggers: init/pipe/view)
│       │   └── WindowsPlatformService.cs ← Registry, env vars (platform abstraction)
│       │
│       ├── ViewModels/             ← ReactiveUI ViewModels
│       │   ├── ViewModelBase.cs
│       │   ├── MainWindowViewModel.cs
│       │   ├── ObjectTreeViewModel.cs
│       │   ├── LiveWalkerViewModel.cs
│       │   ├── InstanceFinderViewModel.cs
│       │   ├── ClassStructViewModel.cs
│       │   ├── HexViewViewModel.cs
│       │   └── PointerPanelViewModel.cs
│       │
│       ├── Views/                  ← Avalonia AXAML + code-behind
│       │   ├── MainWindow.axaml / .cs
│       │   ├── LiveWalkerPanel.axaml / .cs
│       │   ├── ObjectTreePanel.axaml / .cs
│       │   ├── InstanceFinderPanel.axaml / .cs
│       │   ├── ClassStructPanel.axaml / .cs
│       │   ├── HexViewPanel.axaml / .cs
│       │   └── PointerPanel.axaml / .cs
│       │
│       ├── Core/                   ← Platform abstraction interfaces
│       ├── Converters/             ← Avalonia value converters
│       ├── Assets/
│       └── Resources/
│           └── Strings/
│               └── en.axaml       ← All UI strings (English only)
│
├── scripts/
│   ├── UE5CEDumper.CT              ← Cheat Engine table (injectDLL + countdown)
│   ├── ue5dump.lua                 ← CE Lua injection script
│   ├── utils.lua                   ← Lua helpers
│   └── test_pipe.ps1               ← PowerShell pipe test client
│
└── vendor/                         ← Git submodules (reference only, not built)
    ├── Dumper-7/
    ├── GSpots/
    └── UEDumper/
```

-----

## Build Environment

### DLL (C++)

| Property | Value |
|----------|-------|
| IDE | Visual Studio 2026 (v18, MSVC 19.50) |
| C++ Standard | C++23 (`/std:c++latest`) |
| Target | x64 Release DLL, static CRT (`/MT` release, `/MTd` debug) |
| Compiler flags | `/utf-8 /W4 /permissive- /EHa` |
| Build system | CMake 3.25+ with Ninja generator |
| Toolchain discovery | `vswhere -latest` — never hardcoded paths |
| Dependencies | `nlohmann/json` (header-only, vendor/), `ws2_32`, `Shlwapi`, `Psapi`, `Version` |

**Versioning:** Version `1.0.0.x` where `x` is auto-incremented per build and stored in `build_number.txt`. Git commit hash and dirty-state are embedded via `BuildInfo.h` (generated from `BuildInfo.h.in` at CMake configure time).

**Root CMakeLists.txt:**
```cmake
cmake_minimum_required(VERSION 3.25)
project(UE5CEDumper LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_subdirectory(dll)
```

**dll/CMakeLists.txt source list:**
```cmake
add_library(UE5Dumper SHARED
    dll/src/dllmain.cpp
    dll/src/CEPlugin.cpp
    dll/src/Memory.cpp
    dll/src/Logger.cpp
    dll/src/OffsetFinder.cpp
    dll/src/ObjectArray.cpp
    dll/src/FNamePool.cpp
    dll/src/UStructWalker.cpp
    dll/src/PipeServer.cpp
    dll/src/ExportAPI.cpp
    dll/src/version.rc
)
target_include_directories(UE5Dumper PRIVATE dll/src vendor/nlohmann)
target_compile_definitions(UE5Dumper PRIVATE UNICODE _UNICODE)
target_link_libraries(UE5Dumper PRIVATE ws2_32 Shlwapi Psapi Version)
```

### UI App (C# Avalonia)

| Property | Value |
|----------|-------|
| .NET | 10 |
| Avalonia | 11.3.12+ |
| UI pattern | ReactiveUI + ReactiveUI.Fody (auto property notification) |
| Publish | Single-file self-contained (`PublishSingleFile=true`, ~60–80 MB) |
| Runtime | `win-x64` |

-----

## Component Interaction

```
Game Process
  └── UE5Dumper.dll (injected via CE Lua injectDLL())
        ├── AutoStartThreadProc — 1s delay, detects CE plugin vs game
        ├── OffsetFinder       — AOB scan GObjects / GNames / GWorld
        ├── FNamePool          — string resolution
        ├── ObjectArray        — UObject enumeration
        ├── UStructWalker      — FField chain traversal + live reads
        └── PipeServer         — JSON-line IPC on \\.\pipe\UE5DumpBfx
                                        ↕ TCP-like named pipe
CE Lua (UE5CEDumper.CT)               UE5DumpUI.exe (Avalonia)
  └── injectDLL() only                  ├── PipeClient   — async connect/send/recv
                                        ├── DumpService  — request helpers
                                        ├── CeXmlExportService
                                        └── ViewModels → Views (MVVM)
```

### Startup Sequence

1. User opens game → opens CT in CE → CE Lua calls `injectDLL(DLL_PATH)`
2. `DLL_PROCESS_ATTACH` → spawns `AutoStartThreadProc` (1 second delay)
3. `AutoStartThreadProc` → checks `g_isCEPlugin` (suppresses if loaded into CE.exe)
4. `UE5_Init()`: `FindGObjects()` → `FindGNames()` → `DetectVersion()` → `FNamePool::Init()` → `ObjectArray::Init()` → `ValidateAndFixOffsets()`
5. `PipeServer::Start()` → listens on `\\.\pipe\UE5DumpBfx`
6. User launches `UE5DumpUI.exe` → `PipeClient` connects → UI populated

### Logging

All logs written to `%LOCALAPPDATA%\UE5CEDumper\Logs\<ProcessName>\`:

| File prefix | Category | Content |
|-------------|----------|---------|
| `init-*.log` | Init | DLL attach, version detection |
| `scan-*.log` | Scan | GObjects/GNames AOB scan results |
| `offsets-*.log` | Offsets | DynOff detection, ValidateAndFixOffsets |
| `pipe-*.log` | Pipe | JSON command dispatch, responses |
| `walk-*.log` | Walk | UStructWalker field reads |

2-file rotation per category, 5 MB cap. UI mirrors to `ui-init`, `ui-pipe`, `ui-view` prefixed files.
