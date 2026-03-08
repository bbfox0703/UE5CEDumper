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
│       ├── Signatures.h            ← 128 AOB patterns + 5 symbol exports (14 sources)
│       ├── BuildInfo.h.in          ← Template → BuildInfo.h (version, git hash)
│       ├── version.rc              ← Win32 PE VERSIONINFO resource
│       │
│       ├── Memory.cpp / .h         ← AOBScan, ResolveRIP, ReadSafe (SEH), TArrayView
│       ├── Logger.cpp / .h         ← Category-routed logging (5 files: init/scan/offsets/pipe/walk)
│       ├── OffsetFinder.cpp / .h   ← GObjects/GNames/GWorld scan, DynOff detection
│       ├── ObjectArray.cpp / .h    ← Chunked + flat UObject array, stride detection
│       ├── FNamePool.cpp / .h      ← UE5 FNamePool + UE4 TNameEntryArray + hash-prefixed mode
│       ├── UStructWalker.cpp / .h  ← FField/UProperty chain, WalkInstance, array phases
│       ├── GameThreadDispatch.cpp/.h ← MinHook ProcessEvent hook, game-thread queue
│       ├── Mailbox.cpp / .h        ← Shared memory mailbox for CE Lua invocation
│       ├── HintCache.cpp / .h      ← Scan hint cache for faster repeat scans
│       ├── ProxyVersion.cpp / .def ← version.dll proxy DLL forwarding
│       │
│       ├── ExportAPI.cpp / .h      ← C ABI exports (30 exports for CE Lua bridge)
│       ├── PipeServer.cpp / .h     ← Named pipe IPC server, JSON dispatch (30 commands)
│       └── PipeProtocol.h          ← Shared JSON command/field name constants
│
├── docs/                           ← Documentation
│   ├── architecture.md             ← This file
│   ├── dll-spec.md                 ← C++ header definitions, offset tables, CE Lua bridge
│   ├── pipe-protocol.md            ← Named Pipe JSON IPC protocol (30 commands)
│   ├── ui-spec.md                  ← Avalonia UI tech stack, component skeletons
│   ├── export-formats.md           ← CE XML, CSX, SDK Header, USMAP export rules
│   ├── technical-notes.md          ← UE version diffs, FField vs UProperty, FNamePool internals
│   ├── lessons-learned.md          ← Hard-won debugging lessons (20 games)
│   ├── test-games.md               ← 20 test games with UE version + status
│   ├── ue4ss-analysis.md           ← UE4SS/Dumper-7/UEDumper analysis
│   ├── simd-scanning-notes.md      ← AOBMaker SIMD scanning research
│   ├── CE-Bugs-Minesweeper.md      ← CE-specific bug notes
│   └── private/                    ← Private/scratch notes
│
├── ui/                             ← C# Avalonia UI App
│   ├── UE5DumpUI.sln
│   ├── UE5DumpUI.Tests/            ← xUnit test project (348 tests, 15 files)
│   └── UE5DumpUI/
│       ├── UE5DumpUI.csproj
│       ├── Program.cs              ← Avalonia entry point
│       ├── App.axaml / .cs
│       ├── app.manifest
│       ├── ViewLocator.cs
│       ├── Constants.cs            ← UI magic strings
│       │
│       ├── Models/                 ← IPC response models + UI data models (25 files)
│       │   ├── UObjectNode.cs
│       │   ├── LiveFieldValue.cs   ← Rich field value (typed, hex, arrays, enums)
│       │   ├── InstanceWalkResult.cs
│       │   ├── ClassInfoModel.cs, ClassListResult.cs
│       │   ├── FieldInfoModel.cs, FunctionInfoModel.cs
│       │   ├── ObjectListResult.cs, ObjectDetail.cs
│       │   ├── FindInstancesResult.cs, InstanceResult.cs
│       │   ├── WorldWalkResult.cs, DataTableWalkResult.cs
│       │   ├── AddressLookupResult.cs, PropertySearchResult.cs
│       │   ├── CePointerInfo.cs, EngineState.cs
│       │   ├── DetectedGame.cs     ← Proxy DLL deploy model + status enum
│       │   ├── InvokeFunctionResult.cs
│       │   ├── RescanModels.cs, ScanStatusResult.cs
│       │   ├── AobMakerMessage.cs, AobUsageRecord.cs
│       │   ├── EnumDefinition.cs, SymbolEntry.cs
│       │   └── ...
│       │
│       ├── Services/               ← Business logic + IPC (16 files)
│       │   ├── PipeClient.cs       ← Async named pipe client
│       │   ├── DumpService.cs      ← All pipe request/response helpers
│       │   ├── CeXmlExportService.cs ← CE XML generation (Phase A–F arrays)
│       │   ├── CsxExportService.cs  ← CE Structure Dissect export
│       │   ├── SdkExportService.cs  ← SDK C++ header export
│       │   ├── SymbolExportService.cs ← x64dbg/Ghidra/IDA symbol export
│       │   ├── UsmapExportService.cs ← USMAP export
│       │   ├── LoggingService.cs   ← Serilog setup (3 loggers: init/pipe/view)
│       │   ├── WindowsPlatformService.cs ← Registry, env vars (platform abstraction)
│       │   ├── VdfParser.cs         ← Valve VDF format parser (Steam library detection)
│       │   ├── ProxyDeployService.cs ← Proxy DLL deploy/undeploy/detect
│       │   ├── AobMakerBridgeService.cs ← CE AOBMaker plugin bridge
│       │   ├── AobUsageService.cs   ← AOB pattern usage tracking
│       │   ├── KnownStructLayouts.cs ← Hardcoded UE struct layouts for invoke dialog
│       │   ├── InvokeScriptGenerator.cs ← CE Lua invoke script generation
│       │   └── ParamBufferBuilder.cs ← ProcessEvent param buffer hex builder
│       │
│       ├── ViewModels/             ← ReactiveUI ViewModels (10 files)
│       │   ├── ViewModelBase.cs
│       │   ├── MainWindowViewModel.cs
│       │   ├── ObjectTreeViewModel.cs
│       │   ├── LiveWalkerViewModel.cs
│       │   ├── InstanceFinderViewModel.cs
│       │   ├── ClassStructViewModel.cs
│       │   ├── PointerPanelViewModel.cs
│       │   ├── ProxyDeployViewModel.cs
│       │   ├── PropertySearchViewModel.cs ← Property name search across classes
│       │   └── GameClassFilterViewModel.cs ← Game class filtering for Object Tree
│       │
│       ├── Views/                  ← Avalonia AXAML + code-behind (10 files)
│       │   ├── MainWindow.axaml / .cs
│       │   ├── LiveWalkerPanel.axaml / .cs
│       │   ├── ObjectTreePanel.axaml / .cs
│       │   ├── InstanceFinderPanel.axaml / .cs
│       │   ├── ClassStructPanel.axaml / .cs
│       │   ├── PointerPanel.axaml / .cs
│       │   ├── ProxyDeployPanel.axaml / .cs
│       │   ├── PropertySearchPanel.axaml / .cs
│       │   ├── GameClassFilterPanel.axaml / .cs
│       │   └── InvokeParamDialog.cs  ← Parameter input dialog for UFunction invoke
│       │
│       ├── Core/                   ← Platform abstraction interfaces
│       ├── Converters/             ← Avalonia value converters
│       ├── Assets/
│       └── Resources/
│           └── Strings/
│               └── en.axaml       ← All UI strings (English only)
│
├── scripts/
│   ├── UE5CEDumper.CT              ← Cheat Engine table (injectDLL + init)
│   ├── ue5_dissect.lua             ← CE Structure Dissect builder
│   ├── ue5_invoke.lua              ← CE Lua UFunction invocation helper
│   ├── ue5dump.lua                 ← Legacy standalone loader (superseded by CT)
│   ├── utils.lua                   ← Legacy helpers (superseded by CT)
│   └── test_pipe.ps1               ← PowerShell pipe test client
│
└── vendor/                         ← Git submodules
    ├── Dumper-7/                   ← Reference: AOB patterns, offset detection
    ├── RE-UE4SS/                   ← Reference: CustomGameConfigs, UE4 patterns
    ├── minhook/                    ← MinHook inline hooking library (built)
    ├── nlohmann/                   ← nlohmann/json (header-only)
    └── UnrealEngine/               ← UE source reference headers
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
| Dependencies | `nlohmann/json` (header-only), `MinHook` (inline hooking), `ws2_32`, `Shlwapi`, `Psapi`, `Version` |

**Versioning:** Version `1.0.0.x` where `x` is auto-incremented per build and stored in `build_number.txt`. Git commit hash and dirty-state are embedded via `BuildInfo.h` (generated from `BuildInfo.h.in` at CMake configure time).

### UI App (C# Avalonia)

| Property | Value |
|----------|-------|
| .NET | 10 |
| Avalonia | 11.3.12+ |
| UI pattern | ReactiveUI + CommunityToolkit.Mvvm (source generators) |
| Publish | Single-file self-contained (`PublishSingleFile=true`, ~60–80 MB) |
| Runtime | `win-x64` |

-----

## Component Interaction

```
Game Process
  └── UE5Dumper.dll (injected via CE Lua injectDLL() or version.dll proxy)
        ├── AutoStartThreadProc — 1s delay, detects CE plugin vs game
        ├── OffsetFinder       — 133 AOB patterns, GObjects / GNames / GWorld
        ├── FNamePool          — string resolution (3 modes)
        ├── ObjectArray        — UObject enumeration (chunked + flat)
        ├── UStructWalker      — FField chain traversal + live reads
        ├── GameThreadDispatch — MinHook ProcessEvent hook, game-thread queue
        ├── HintCache          — Scan hint caching for repeat scans
        └── PipeServer         — 30 JSON commands on \\.\pipe\UE5DumpBfx
                                        ↕ Named pipe (JSON newline-delimited)
CE Lua (UE5CEDumper.CT)               UE5DumpUI.exe (Avalonia)
  ├── injectDLL()                      ├── PipeClient       — async connect/send/recv
  └── ue5_dissect.lua (optional)       ├── DumpService      — request helpers
                                       ├── CeXmlExportService / CsxExportService
                                       ├── SdkExportService / SymbolExportService
                                       ├── UsmapExportService
                                       └── ViewModels → Views (MVVM)
```

### Startup Sequence

1. User opens game → opens CT in CE → CE Lua calls `injectDLL(DLL_PATH)` (or proxy `version.dll` auto-loads)
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

4-file rotation per category, 8 MB max per file. UI mirrors to `ui-init`, `ui-pipe`, `ui-view` prefixed files.
