# UE5CEDumper

A **C++ injected DLL + Cheat Engine Lua Bridge + Avalonia UI** toolchain for runtime inspection of Unreal Engine 5 games. It locates global engine pointers (GObjects / GNames / GWorld) via AOB scanning, traverses the UObject hierarchy, and streams live data over a Named Pipe to an external GUI.

> **Status**: DLL core functional and tested on TQ2 (UE 5.7). Dynamic offset detection (CasePreservingName) built and compiled; awaiting next injection test. Avalonia UI scaffold complete; pipe integration in progress.

---

## Features

| Feature | Status |
|---|---|
| GObjects AOB scan (9 patterns, UE5.0–5.7) | ✅ |
| GNames AOB scan (6 patterns) + `.data` pointer-scan fallback | ⚠️ fallback untested |
| GWorld AOB scan (6 patterns) | ✅ |
| UE version detection (PE VERSIONINFO + memory string scan) | ✅ |
| FChunkedFixedUObjectArray — ForEach / GetByIndex | ✅ |
| FNamePool multi-format reader (format A/B, chunk index) | ✅ |
| Dynamic FField/FProperty/UStruct offset detection (DynOff) | ⚠️ built, untested |
| CasePreservingName detection (UE5.5+, FName = 16 bytes) | ⚠️ built, untested |
| Live Data Walker — walk_class, walk_instance, walk_world | ✅ |
| Instance Finder — find_instances by class name | ✅ |
| CE XML export — get_ce_pointer_info | ✅ |
| Named Pipe IPC server (JSON newline-delimited) | ✅ |
| CE Plugin mode (Type 5 main menu entry) | ✅ |
| Avalonia UI (Dark, i18n: zh-TW / en / ja) | 🔲 scaffold only |
| Git commit hash + build timestamp embedded in DLL | ✅ |

---

## Architecture

```
┌──────────────────────────────────────────────────────┐
│  Game Process (x64, UE5)                            │
│                                                      │
│  CE Lua  (UE5CEDumper.CT / ue5dump.lua)              │
│      │  injectDLL()                                  │
│      ▼                                               │
│  UE5Dumper.dll  [injected]                           │
│      ├── ExportAPI       ← C ABI, called by CE Lua   │
│      ├── PipeServer      ← Named Pipe IPC server     │
│      ├── OffsetFinder    ← AOB scan + DynOff detect  │
│      ├── ObjectArray     ← FChunkedFixedUObjectArray  │
│      ├── FNamePool       ← UE5 FNamePool reader      │
│      ├── UStructWalker   ← FField chain traversal    │
│      ├── CEPlugin        ← CE plugin Type 5 entry    │
│      ├── Logger          ← File rotation, 5MB cap    │
│      └── Memory          ← AOBScan + ResolveRIP      │
└──────────────────────┬───────────────────────────────┘
                       │ \\.\pipe\UE5DumpBfx
                       │ JSON, newline-delimited
┌──────────────────────▼───────────────────────────────┐
│  UE5DumpUI.exe  (Avalonia 11 + CommunityToolkit.Mvvm)│
│      ├── PipeClient      ← async, request/response   │
│      ├── ObjectTreePanel ← UObject hierarchy browser  │
│      ├── ClassStructPanel← Field/property grid        │
│      ├── PointerPanel    ← GObjects / GNames display  │
│      ├── HexViewPanel    ← Live memory hex view       │
│      ├── LiveWalkerPanel ← Real-time field values     │
│      └── InstanceFinder  ← Search by class name       │
└──────────────────────────────────────────────────────┘
```

---

## Repository Layout

```
UE5CEDumper/
├── build.cmd                   ← Build wrapper (debug/release/publish/test)
├── build.ps1                   ← PowerShell build driver
├── CMakeLists.txt              ← Root CMake (delegates to dll/)
│
├── dll/                        ← C++ injected DLL
│   ├── CMakeLists.txt
│   └── src/
│       ├── dllmain.cpp         ← DLL_PROCESS_ATTACH, AutoStartThreadProc
│       ├── Constants.h         ← Centralized magic strings + DynOff namespace
│       ├── ExportAPI.cpp/.h    ← C ABI exports (UE5_Init, UE5_WalkClassBegin…)
│       ├── PipeServer.cpp/.h   ← Named Pipe server + JSON dispatch
│       ├── PipeProtocol.h      ← IPC command/response definitions
│       ├── OffsetFinder.cpp/.h ← GObjects/GNames scan + ValidateAndFixOffsets
│       ├── ObjectArray.cpp/.h  ← FChunkedFixedUObjectArray
│       ├── FNamePool.cpp/.h    ← FNamePool chunk reader
│       ├── UStructWalker.cpp/.h← FField chain + SuperStruct inheritance
│       ├── CEPlugin.cpp        ← CE plugin Type 5 main menu
│       ├── Logger.cpp/.h       ← File log, 5-version rotation, 5 MB cap
│       └── Memory.cpp/.h       ← AOBScan (exec sections), ResolveRIP, ReadSafe
│
├── ui/                         ← C# Avalonia UI
│   ├── UE5DumpUI.sln
│   └── UE5DumpUI/
│       ├── UE5DumpUI.csproj
│       ├── Core/               ← Platform interfaces (IPlatformService…)
│       ├── Models/             ← UObjectNode, FieldInfoModel, EngineState…
│       ├── Services/           ← PipeClient, DumpService, LoggingService…
│       ├── ViewModels/         ← MainWindow, ObjectTree, ClassStruct…
│       └── Views/              ← AXAML panels
│
├── scripts/
│   ├── UE5CEDumper.CT          ← Cheat Engine Cheat Table (main entry point)
│   ├── ue5dump.lua             ← CE Lua helper script
│   └── test_pipe.ps1           ← PowerShell pipe smoke test
│
├── dist/                       ← Pre-built binaries (Release)
│   ├── UE5Dumper.dll
│   └── UE5DumpUI.exe
│
└── vendor/                     ← Git submodules (reference only)
    ├── Dumper-7/
    ├── UEDumper/
    └── GSpots/
```

---

## Requirements

### Build

| Tool | Version |
|---|---|
| Visual Studio / MSVC | 2022 v17+ (MSVC v143) |
| CMake | 3.25+ |
| Ninja | any recent |
| .NET SDK | 10.0 |

> `build.cmd` / `build.ps1` locate MSVC automatically via `vswhere`. No hardcoded paths.

### Runtime

- Windows 10/11 x64
- Cheat Engine 7.5+
- A running Unreal Engine 5 game process (x64)

---

## Building

```cmd
:: Build everything (Release)
build

:: Build DLL only
build dll

:: Build UI only
build ui

:: Debug build
build debug

:: Run tests
build test

:: Native AOT single-file publish
build publish

:: Clean first, then release
build clean
```

Output artifacts:
- `build/UE5Dumper.dll` — injected DLL
- `ui/UE5DumpUI/bin/Release/.../UE5DumpUI.exe` — UI app
- `dist/` — copied after a successful release or publish build

---

## Usage

### 1. Build

```cmd
build release
```

### 2. Deploy DLL

Copy `dist\UE5Dumper.dll` next to `dist\UE5CEDumper.CT` (they must be in the same folder).

### 3. Inject via Cheat Engine

1. Open Cheat Engine and attach it to the UE5 game process.
2. Open `dist\UE5CEDumper.CT`.
3. Check **[Inject DLL + Start Pipe Server]** in the cheat table.
4. The Lua console shows a 15-second countdown while the DLL scans for GObjects/GNames.

### 4. Connect the UI

```cmd
dist\UE5DumpUI.exe
```

Click **Connect** in the toolbar. The UI connects to `\\.\pipe\UE5DumpBfx`.

### 5. Diagnose

- DLL log: `%LOCALAPPDATA%\UE5CEDumper\Logs\UE5Dumper-0.log`
- UI log: `%APPDATA%\UE5CEDumper\Logs\UE5DumpUI-0.log`
- Pipe smoke test: `powershell -f scripts\test_pipe.ps1`

---

## Pipe IPC Protocol

Communication uses newline-delimited JSON over `\\.\pipe\UE5DumpBfx`.

**Selected commands (UI → DLL):**

| Command | Description |
|---|---|
| `init` | Initialize engine, return UE version + global pointer addresses |
| `get_pointers` | Return GObjects / GNames addresses and object count |
| `get_object_list` | Paginated UObject list |
| `walk_class` | Return all FField entries for a UClass |
| `walk_world` | Traverse GWorld → PersistentLevel → Actors |
| `find_instances` | Search for all instances of a named class |
| `get_ce_pointer_info` | Return CE-compatible XML pointer chain |
| `read_mem` / `write_mem` | Raw memory access |
| `watch` / `unwatch` | Subscribe to live memory updates (push events) |
| `get_offsets` | Return runtime-detected DynOff values (diagnostics) |

---

## Dynamic Offset Detection (DynOff)

UE 5.5+ introduced **CasePreservingName**, where `FName` grows from 8 to 16 bytes (adds `DisplayIndex`). This shifts `FField::Flags`, `FField::Name`, and all `FProperty` fields by +8 bytes.

`ValidateAndFixOffsets()` (in `OffsetFinder.cpp`) automatically detects the correct layout by probing known structs (Guid — fields A/B/C/D at offsets 0/4/8/12; Vector — X/Y/Z at offsets 0/4/8) and updates the `DynOff::*` inline variables at runtime.

All `UStructWalker` code uses `DynOff::*` exclusively — no hardcoded FField/FProperty offsets.

---

## UE Version Support

| UE Version | GObjects | GNames | DynOff |
|---|---|---|---|
| 5.0–5.2 | ✅ | ✅ | not needed |
| 5.3–5.4 | ✅ | ✅ | not needed |
| 5.5–5.7 | ✅ | ⚠️ fallback | ⚠️ built, untested |

---

## Known Issues

- **GNames patterns fail on TQ2 (UE 5.7)** — all 4 LEA-based patterns resolve to wrong globals. A `.data` section pointer-scan fallback (`FindGNamesByPointerScan`) is implemented but untested.
- **DynOff / CasePreservingName** — built and compiled successfully, requires injection test on TQ2.
- **Avalonia UI** — scaffold is complete; end-to-end pipe integration pending DynOff validation.

---

## References & Credits

| Project | Use |
|---|---|
| [Encryqed/Dumper-7](https://github.com/Encryqed/Dumper-7) | Dynamic offset detection patterns, FField/FProperty probing strategy |
| [Spuckwaffel/UEDumper](https://github.com/Spuckwaffel/UEDumper) | Live editor UI architecture reference |
| [UE4SS-RE/RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) | UE5 runtime reflection, alternative angle |
| [Do0ks/GSpots](https://github.com/Do0ks/GSpots) | Additional AOB patterns for GObjects/GNames |
| [trumank/patternsleuth](https://github.com/trumank/patternsleuth) | Additional AOB patterns for GObjects/GNames scanning |
| [nlohmann/json](https://github.com/nlohmann/json) | Header-only JSON library used in DLL |
| [cheat-engine/cheat-engine](https://github.com/cheat-engine/cheat-engine) | CE Lua scripting API reference |
| AOBMaker (private) | AOB pattern generation tooling |

---

## License

[MIT](LICENSE) © 2026 bbfox0703
