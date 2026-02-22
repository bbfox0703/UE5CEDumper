# UE5CEDumper

A **C++ injected DLL + Cheat Engine Lua Bridge + Avalonia UI** toolchain for runtime inspection of Unreal Engine 4 & 5 games. It locates global engine pointers (GObjects / GNames / GWorld) via AOB scanning, traverses the UObject hierarchy, and streams live data over a Named Pipe to an external GUI.

This is not another replacement of existing UE dumpers. It's like a inspector. Targeted to make a more easy to create Cheat Engine tables. For professional dumpers, refer: 
[Encryqed/Dumper-7](https://github.com/Encryqed/Dumper-7), [Spuckwaffel/UEDumper](https://github.com/Spuckwaffel/UEDumper) and [UE4SS-RE/RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS)

> **Status**: DLL core functional. Tested and working on **17 games** across UE 4.18 ~ 5.7 (TQ2, EverSpace 2, Hogwarts Legacy, FF7 Rebirth, FF7 Remake, Octopath Traveler, DQ I&II HD-2D, DQ III HD-2D, DQ XI S, Tower of Mask, Star Wars Jedi, IDOLM@STER, Romancing SaGa 2 RotS, Ghostwire: Tokyo, Lushfoil Photography Sim, Manor Lords, Satisfactory). Avalonia UI functional with Object Tree, Live Walker, Instance Finder, and Class Structure panels.

---

[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D4)](https://www.microsoft.com/windows)
[![UE Version](https://img.shields.io/badge/UE-4.18--5.7-orange)](https://www.unrealengine.com/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![C++](https://img.shields.io/badge/C++-23-00599C)](https://isocpp.org/)
[![Avalonia](https://img.shields.io/badge/Avalonia-UI-8B5CF6)](https://avaloniaui.net/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Built with Claude Code](https://img.shields.io/badge/Built%20with-Claude%20Code-CC785C?logo=claude)](https://claude.ai/code)

## Features

| Feature | Status |
|---|---|
| GObjects AOB scan (51 patterns from 6 sources, UE4.18–5.7) | ✅ |
| GNames AOB scan (17 patterns) + `.data` pointer-scan fallback | ✅ |
| GWorld AOB scan (7 patterns) | ✅ |
| UE version detection (PE VERSIONINFO + memory string scan) | ✅ |
| FChunkedFixedUObjectArray + FFixedUObjectArray (flat) auto-detect | ✅ |
| FUObjectItem stride auto-detect (16/24/20 bytes) | ✅ |
| FNamePool multi-format: standard, hash-prefixed, UE4 TNameEntryArray | ✅ |
| Dynamic FField/FProperty/UStruct offset detection (DynOff): UE 5.5+ CasePreservingName support | ✅ |
| CasePreservingName detection (UE5.5+, FName = 16 bytes) | ✅ |
| FProperty-to-UProperty fallback (UE4 <4.25) | ✅ |
| Live Data Walker — Inspect instance memory data | ✅ |
| Instance Finder — find instance by address or class name. Can open in Live Walker | ✅ |
| CE XML export — One-click CE XML address records export (with bitfields) and paste into CE address field | ✅ |
| Named Pipe IPC server (JSON newline-delimited) | ✅ |
| CE Plugin mode (Type 5 main menu entry, not used now) | ✅ |
| Avalonia UI (Object Tree, Live Walker, Instance Finder, Class Structure) | ✅ |
| Git commit hash + build timestamp embedded in DLL | ✅ |

---

## Architecture

```
┌──────────────────────────────────────────────────────┐
│  Game Process (x64, UE4/UE5)                         │
│                                                      │
│  CE Lua  (UE5CEDumper.CT / ue5dump.lua)              │
│      │  injectDLL()                                  │
│      ▼                                               │
│  UE5Dumper.dll  [injected]                           │
│      ├── ExportAPI       ← C ABI, called by CE Lua   │
│      ├── PipeServer      ← Named Pipe IPC server     │
│      ├── OffsetFinder    ← AOB scan + DynOff detect  │
│      ├── ObjectArray     ← FChunkedFixed + FFixed auto │
│      ├── FNamePool       ← UE4/UE5 FNamePool reader   │
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
│       ├── Signatures.h        ← 51 AOB patterns + 4 symbol exports (all sources)
│       ├── Constants.h         ← Centralized magic strings + DynOff namespace
│       ├── ExportAPI.cpp/.h    ← C ABI exports (UE5_Init, UE5_WalkClassBegin…)
│       ├── PipeServer.cpp/.h   ← Named Pipe server + JSON dispatch
│       ├── PipeProtocol.h      ← IPC command/response definitions
│       ├── OffsetFinder.cpp/.h ← GObjects/GNames scan + ValidateAndFixOffsets
│       ├── ObjectArray.cpp/.h  ← FChunkedFixed + FFixed (flat) auto-detect
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
- A running Unreal Engine 4 or 5 game process (x64)

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

1. Open Cheat Engine and attach it to the UE game process.
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

| UE Version | GObjects | GNames | DynOff | Tested Games |
|---|---|---|---|---|
| 4.18 | ✅ | ✅ | ✅ (UProperty fallback) | FF7 Remake Intergrade |
| 4.21–4.24 | ✅ | ✅ | ✅ | Star Wars Jedi, DQ XI S, IDOLM@STER, Octopath Traveler, DQ I&II HD-2D Remake, DQ III HD-2D Remake |
| 4.25–4.27 | ✅ | ✅ | ✅ | FF7 Rebirth, Tower of Mask, Hogwarts Legacy, Romancing SaGa 2, Ghostwire: Tokyo (TBD) |
| 5.0–5.2 | ✅ | ✅ | ✅ | — |
| 5.3–5.4 | ✅ | ✅ | ✅ | Satisfactory |
| 5.5–5.7 | ✅ | ✅ (pointer-scan) | ✅ (CPN) | Titan Quest II, EverSpace 2, Lushfoil Photography Sim, Manor Lords |

---

## Known Issues

- **"Start from GWorld" fails on some games** — GWorld works on 9/17 tested games (TQ2, EverSpace 2, Hogwarts Legacy, IDOLM@STER, Romancing SaGa 2 RotS, Tower of Mask, Ghostwire: Tokyo, Lushfoil Photography Sim, Manor Lords). Fails on some UE4 games (FF7R, FF7Re, DQ series, OctoPath) and Satisfactory. Under investigation.
- **UE version misdetection** — Some games report incorrect UE version (e.g., FF7R shows 504 instead of 418, Ghostwire: Tokyo shows 505). Cosmetic issue — all pipelines work correctly via automatic fallback detection.
- **FF7 Rebirth CE address lookup** — `get_ce_pointer_info` address-to-object lookup fails on FF7 Rebirth but works on other games. Because FF7Re data, like HP is stored outside UObject.

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on:
- **Submitting AOB patterns** — what evidence to provide and how verification works
- **Reporting detection failures** — what logs and info to include
- **Code contributions** — PR process and code style

---

## References & Credits

| Project | Use |
|---|---|
| [Encryqed/Dumper-7](https://github.com/Encryqed/Dumper-7) | Dynamic offset detection patterns, FField/FProperty probing strategy |
| [Spuckwaffel/UEDumper](https://github.com/Spuckwaffel/UEDumper) | Live editor UI architecture reference |
| [UE4SS-RE/RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) | UE5 runtime reflection, alternative angle |
| [Do0ks/GSpots](https://github.com/Do0ks/GSpots) | Additional AOB patterns for GObjects/GNames |
| [trumank/patternsleuth](https://github.com/trumank/patternsleuth) | Additional AOB patterns for GObjects/GNames scanning |
| UE4 Dumper.CT | Cake-san's cheat table — additional AOB patterns (CT-series in Signatures.h) |
| [nlohmann/json](https://github.com/nlohmann/json) | Header-only JSON library used in DLL |
| [cheat-engine/cheat-engine](https://github.com/cheat-engine/cheat-engine) | CE Lua scripting API reference |
| AOBMaker (private) | AOB pattern generation tooling |

---

## Built with Claude Code

This project is developed with the assistance of [Claude Code](https://claude.ai/code) by Anthropic. The C++ DLL, C# Avalonia UI, build scripts, and documentation are collaboratively authored by the developer and Claude Code.

---

## License

[MIT](LICENSE) © 2026 bbfox0703
