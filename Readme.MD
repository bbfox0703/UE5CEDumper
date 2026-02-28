# UE5CEDumper

**The Live Bridge between Unreal Engine Runtime and Cheat Engine.**

UE5CEDumper is a interactive inspector toolchain. It provides a **live window** into the game's memory, allowing you to browse objects, find instances, and export CE-ready structures in real-time.

> It's built for the *active* table maker. It bridges the gap between seeing an offset and actually using it in Cheat Engine.

> *UE5CEDumper is not meant to be a another dumper that extracts large amounts of data for analysis. Instead, it focuses on quickly finding UE structures and integrating with CE for live development. Think of it as a general-purpose UE tool rather than a specialized dumper.*

---
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D4)](https://www.microsoft.com/windows)
[![UE Version](https://img.shields.io/badge/UE-4.18--5.7-orange)](https://www.unrealengine.com/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![C++](https://img.shields.io/badge/C++-23-00599C)](https://isocpp.org/)
[![Avalonia](https://img.shields.io/badge/Avalonia-UI-8B5CF6)](https://avaloniaui.net/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Built with Claude Code](https://img.shields.io/badge/Built%20with-Claude%20Code-CC785C?logo=claude)](https://claude.ai/code)

## Tested Version Matrix

| UE Version | GObjects | GNames | DynOff | Verified Games |
|---|:---:|:---:|:---:|---|
| **4.18** | ✅ | ✅ | ✅ | *FF7 Remake Intergrade* |
| **4.21 – 4.24** | ✅ | ✅ | ✅ | *Star Wars Jedi, DQ XI S, IDOLM@STER STARLIT SEASON, Octopath Traveler, DQ I&II / III HD-2D Remaster* |
| **4.25 – 4.27** | ✅ | ✅ | ✅ | *FF7 Rebirth, Tower of Mask, Hogwarts Legacy, Romancing SaGa 2 RotS, Ghostwire: Tokyo* |
| **5.0 – 5.2** | ✅ | ✅ | ✅ | (Confirmed support via generic patterns) |
| **5.3 – 5.4** | ✅ | ✅ | ✅ | *Satisfactory (v1.1.3.1)* |
| **5.5 – 5.7** | ✅ | ✅* | ✅** | *Titan Quest II, EverSpace 2, Lushfoil Photography Sim, Manor Lords, Cat Island Petrichor Demo, Way of the Hunter 2 Demo, COMBAT PILOT: CARRIER QUALIFICATION Demo* |

*\*GNames uses .data pointer-scan fallback for 5.5+*
*\**DynOff supports **CasePreservingName (FName = 16 bytes)** layout.*

---

## Features for Table Makers

| Feature | Description |
|---|---|
| **AOB Scanning** | 100 entries (95 patterns + 5 exports) from 8 sources. Supports `FChunkedFixed` and `FFixed` object arrays. Encrypted GObjects + preset layout detection. |
| **DynOff (Dynamic Offsets)** | Runtime probing of `FField`, `FProperty`, and `UStruct`. No more manual offset updating after game patches. |
| **Live Walker** | Inspect instance memory data with real-time value updates. Navigate into pointers (→), inline structs ({}), and containers ([]). Breadcrumb trail for full navigation history. |
| **Container Drill-Down** | Drill into **Array**, **Map**, and **Set** containers. ObjectProperty elements show resolved names and support click-through navigation. |
| **Instance Finder** | Locate all active objects of a class instantly. One-click to open in Live Walker. |
| **One-Click CE Export** | Generate CE XML address records — scalars, bitfields, structs, arrays, **Map/Set**, and **UEnum DropDownLists**. Paste directly into your address list. |
| **Modern UI** | External **Avalonia UI** connected via high-speed Named Pipe. Keep your game performance smooth. |

---

## Architecture & Workflow

1.  **Inject DLL**: Run Cheat Engine, attach game process, load a save. Make sure game data is loaded first. Open `UE5CEDumper.CT`.
2.  **Enable Script**: Enable `init <== enable after process attached`, then `Inject DLL + Start Pipe Server`. The DLL locates global engine pointers and detects the UE version/layout automatically.
3.  **Connect UI**: Wait a few seconds for the scan to finish. Launch **UE5DumpUI.exe** and click **Connect**. Live data streams to the UI via Named Pipes (JSON-RPC).
4.  **Navigate & Analyze**: Browse the `UObject` hierarchy, find a class, drill into containers, or paste an address from CE to reverse-lookup and export.

| **Game Process (Injected)** |
| :---: |
| DLL + CE Lua Bridge |
| ⬇️ |
| **Named Pipe IPC (JSON-RPC Protocol)** |
| ⬇️ |
| **External GUI (Avalonia UI App)** |
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
- Cheat Engine 7.6+
- A running Unreal Engine 4 or 5 game process (x64)

---

## Important Notes

* **Custom Data Structures**: In games like *FF7 Rebirth*, some critical data (e.g., HP) is stored in custom structures outside standard `UObjects`. The Live Walker can help investigate these regions, but direct discovery is not possible.
* **GWorld Connectivity**: `GWorld` traversal works in ~60% of tested games. For others, use the **Object Tree** or **Instance Finder** as the primary entry point.
* **Container Limits**: Array/Map/Set element reading respects a configurable limit to avoid excessive memory reads. Adjust the **Array Limit** slider in the Live Walker when working with large containers.

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

**License**: [MIT](LICENSE) © 2026 bbfox0703