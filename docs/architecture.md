# Architecture & Build Environment

> For build commands, see [CLAUDE.md](../CLAUDE.md). For DLL/UI interface details, see [dll-spec.md](dll-spec.md) and [ui-spec.md](ui-spec.md). For the rationale behind the Frieren-themed C++ file/namespace names, see [naming-convention.md](naming-convention.md).

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
│   └── src/                        ← 15 .cpp + 14 .h (Frieren-themed; see naming-convention.md)
│       ├── Heiter.cpp              ← dllmain — DLL_PROCESS_ATTACH, AutoStartThreadProc
│       ├── Methode.cpp             ← CEPlugin — CE plugin Type 5 main menu
│       ├── Grimoire.h              ← Constants — magic strings, pipe name, UObject offsets, DynOff namespace
│       ├── Himmel.h                ← Signatures — 128 AOB patterns + 5 symbol exports (14 sources)
│       ├── BuildInfo.h.in          ← Template → BuildInfo.h (version, git hash)
│       ├── version.rc              ← Win32 PE VERSIONINFO resource
│       │
│       ├── Macht.cpp / .h          ← Memory — AOBScan, ResolveRIP, ReadSafe (SEH), TArrayView
│       ├── Sein.cpp / .h           ← Logger — category-routed logging (5 files: init/scan/offsets/pipe/walk)
│       ├── Genau.cpp / .h          ← OffsetFinder — GObjects/GNames/GWorld scan, DynOff detection
│       ├── Aura.cpp / .h           ← ObjectArray — chunked + flat UObject array, stride detection,
│       │                              FindByAddress / FindInContainers / FindReferencesToUObject (Find Refs v3)
│       ├── Serie.cpp / .h          ← FNamePool — UE5 FNamePool + UE4 TNameEntryArray + hash-prefixed mode
│       ├── Ubel.cpp / .h           ← UStructWalker — FField/UProperty chain, WalkInstance, array Phases B-K,
│       │                              OptionalProperty (intrusive + non-intrusive)
│       ├── Stark.cpp / .h          ← GameThreadDispatch — MinHook ProcessEvent hook, game-thread queue
│       ├── Mimic.cpp / .h          ← Mailbox — shared memory mailbox for CE Lua invocation
│       ├── Flamme.cpp / .h         ← HintCache — scan hint cache for faster repeat scans
│       ├── Lugner.cpp              ← ProxyVersion — version.dll proxy DLL forwarding
│       ├── Lugner_Dinput8.cpp      ← dinput8.dll proxy variant
│       ├── ProxyVersion.def        ← version.dll export forwarding
│       ├── ProxyDinput8.def        ← dinput8.dll export forwarding
│       │
│       ├── Frieren.cpp / .h        ← ExportAPI — 30 C ABI exports for CE Lua bridge
│       ├── Fern.cpp / .h           ← PipeServer — Named pipe IPC server, JSON dispatch (31 commands)
│       └── Renge.h                 ← PipeProtocol — shared JSON command/field name constants
│
├── docs/                           ← Documentation
│   ├── architecture.md             ← This file
│   ├── dev-log.md                  ← Running milestone log + capability matrix + gaps (read first)
│   ├── dll-spec.md                 ← C++ header definitions, offset tables, CE Lua bridge
│   ├── pipe-protocol.md            ← Named Pipe JSON IPC protocol (31 commands)
│   ├── ui-spec.md                  ← Avalonia UI tech stack, component skeletons
│   ├── export-formats.md           ← CE XML, CSX, SDK Header, USMAP export rules
│   ├── technical-notes.md          ← UE version diffs, FField vs UProperty, FNamePool internals,
│   │                                  Property Type Layouts (Phases B-K), Address Finder layered lookup
│   ├── lessons-learned.md          ← Hard-won debugging lessons (20+ games)
│   ├── test-games.md               ← 20 test games with UE version + status
│   ├── naming-convention.md        ← Frieren-themed file / namespace mapping
│   ├── aobmaker-integration.md     ← AOBMaker CE Plugin pipe bridge spec
│   ├── simd-scanning-notes.md      ← AOBMaker SIMD scanning research (reference)
│   ├── CE-Bugs-Minesweeper.md      ← CE-specific bug notes (evergreen)
│   ├── archive/                    ← Outdated design docs preserved for history
│   └── private/                    ← Private/scratch notes (gitignored if listed in .gitignore)
│
├── ui/                             ← C# Avalonia UI App
│   ├── UE5DumpUI.sln
│   ├── UE5DumpUI.Tests/            ← xUnit test project (16 .cs test files, 496 tests)
│   └── UE5DumpUI/
│       ├── UE5DumpUI.csproj        ← .NET 10 windows, Avalonia 12.0.2, Native AOT
│       ├── Program.cs              ← Avalonia entry point
│       ├── App.axaml / .cs         ← Service creation + DI (incl. AobMakerBridgeService)
│       ├── app.manifest
│       ├── ViewLocator.cs
│       ├── Constants.cs            ← UI magic strings
│       │
│       ├── Models/                 ← IPC response models + UI data models (27 files)
│       │   ├── UObjectNode.cs
│       │   ├── LiveFieldValue.cs   ← Rich field value (typed, hex, arrays, enums, struct sub-fields)
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
│       │   ├── DumpService.cs      ← All pipe request/response helpers (incl. find_refs_to_uobject)
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
│       │   ├── LiveWalkerViewModel.cs   ← Find Refs reverse Open auto-scroll
│       │   ├── InstanceFinderViewModel.cs
│       │   ├── ClassStructViewModel.cs  ← class-like routing + null-fire dedupe
│       │   ├── PointerPanelViewModel.cs
│       │   ├── ProxyDeployViewModel.cs
│       │   ├── PropertySearchViewModel.cs ← Type filter + autocomplete + client-side result filter
│       │   └── GameClassFilterViewModel.cs ← Package column + auto-run Find Instances
│       │
│       ├── Views/                  ← Avalonia AXAML + code-behind (9 axaml + 10 cs)
│       │   ├── MainWindow.axaml / .cs
│       │   ├── LiveWalkerPanel.axaml / .cs
│       │   ├── ObjectTreePanel.axaml / .cs
│       │   ├── InstanceFinderPanel.axaml / .cs
│       │   ├── ClassStructPanel.axaml / .cs
│       │   ├── PointerPanel.axaml / .cs
│       │   ├── ProxyDeployPanel.axaml / .cs
│       │   ├── PropertySearchPanel.axaml / .cs
│       │   ├── GameClassFilterPanel.axaml / .cs
│       │   └── InvokeParamDialog.cs  ← Parameter input dialog for UFunction invoke (no .axaml — code-only)
│       │
│       ├── Core/                   ← Platform abstraction interfaces (incl. IAobMakerBridge)
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

**Versioning:** Version `1.0.0.x` where `x` is auto-incremented per build and stored in `build_number.txt` (currently 547). Git commit hash and dirty-state are embedded via `BuildInfo.h` (generated from `BuildInfo.h.in` at CMake configure time).

### UI App (C# Avalonia)

| Property | Value |
|----------|-------|
| .NET | 10.0 (windows) |
| Avalonia | 12.0.2 (Themes.Fluent + Controls.DataGrid 12.0.0) |
| UI pattern | ReactiveUI + CommunityToolkit.Mvvm 8.* (source generators) |
| Logging | Serilog 4.3.1 (file + console sinks) |
| Publish | Single-file self-contained / Native AOT trimmed (`PublishSingleFile=true`) |
| Runtime | `win-x64` |

-----

## Component Interaction

```
Game Process
  └── UE5Dumper.dll (injected via CE Lua injectDLL() or version.dll proxy)
        ├── AutoStartThreadProc — 1s delay, detects CE plugin vs game
        ├── Genau (OffsetFinder)    — 128 AOB patterns + 5 symbol exports, GObjects / GNames / GWorld
        ├── Serie (FNamePool)       — string resolution (3 modes)
        ├── Aura (ObjectArray)      — UObject enumeration (chunked + flat) + Find Refs / FindInContainers / FindByAddress
        ├── Ubel (UStructWalker)    — FField chain traversal + live reads (Phases B-K, OptionalProperty, MulticastDelegate)
        ├── Stark (GameThreadDispatch) — MinHook ProcessEvent hook, game-thread queue
        ├── Flamme (HintCache)      — scan hint caching for repeat scans
        └── Fern (PipeServer)       — 31 JSON commands on \\.\pipe\UE5DumpBfx
                                        ↕ Named pipe (JSON newline-delimited)
CE Lua (UE5CEDumper.CT)               UE5DumpUI.exe (Avalonia)
  ├── injectDLL()                      ├── PipeClient       — async connect/send/recv
  └── ue5_dissect.lua (optional)       ├── DumpService      — request helpers
                                       ├── AobMakerBridgeService — \\.\pipe\AOBMakerCEBridge (optional)
                                       ├── CeXmlExportService / CsxExportService
                                       ├── SdkExportService / SymbolExportService
                                       ├── UsmapExportService
                                       └── ViewModels → Views (MVVM)
```

### Startup Sequence

1. User opens game → opens CT in CE → CE Lua calls `injectDLL(DLL_PATH)` (or proxy `version.dll` auto-loads)
2. `DLL_PROCESS_ATTACH` (Heiter.cpp) → spawns `AutoStartThreadProc` (1 second delay)
3. `AutoStartThreadProc` → checks `g_isCEPlugin` (suppresses if loaded into CE.exe)
4. `UE5_Init()`: `Genau::FindGObjects()` → `Genau::FindGNames()` → `Genau::DetectVersion()` → `Serie::Init()` → `Aura::Init()` → `Genau::ValidateAndFixOffsets()`
5. `Fern::PipeServer::Start()` → listens on `\\.\pipe\UE5DumpBfx`
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
