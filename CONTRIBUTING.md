# Contributing to UE5CEDumper

Thank you for your interest in contributing! This document explains how to submit AOB patterns, report detection failures, and contribute code changes.

---

## Table of Contents

- [AOB Pattern Contributions](#aob-pattern-contributions)
- [Bug Reports (Detection / Extraction Failures)](#bug-reports)
- [Code Contributions](#code-contributions)
- [Development Setup](#development-setup)

---

## AOB Pattern Contributions

AOB (Array of Bytes) patterns are the core mechanism for locating engine globals (GObjects, GNames, GWorld). We currently have **133 patterns from 14 sources** in `dll/src/Signatures.h`, covering UE4.18 through UE5.7+ across 20+ tested games.

### Most Helpful: Report Detection Failures

If your game isn't detected, the **most helpful thing you can do** is open an issue with the `detection-failure` label and attach the full scan log (see [Bug Reports](#bug-reports) below). The scan log contains all the diagnostic data maintainers need to analyze the failure and create new patterns — you don't need to reverse-engineer the pattern yourself.

### For Reverse Engineers: Direct Pattern Contributions

If you have RE experience (IDA/Ghidra/x64dbg) and want to contribute patterns directly, open an issue with the `aob-pattern` label and include:

| Item | Description |
|------|-------------|
| **Pattern bytes** | Hex string with `??` wildcards (e.g., `48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 48 85 C9 74`) |
| **Target** | Which global: `GObjects`, `GNames`, or `GWorld` |
| **Resolution type** | `rip-direct`, `rip-deref`, `rip-both`, `symbol-export`, `symbol-call-follow`, or `call-follow` |
| **Game name + UE version** | Full name + exact version (e.g., `UE 5.04`) |
| **Source function** | Engine function the pattern is from (e.g., `FUObjectArray::AllocateUObjectIndex`) |
| **Scan log + Object Tree screenshot** | Proof that the pattern resolves correctly |

Follow the existing conventions in `Signatures.h` for pattern format and registration macros.

---

## Bug Reports

If UE5CEDumper fails to detect a game or produces incorrect results, please open an issue with the label `detection-failure` and provide the information below.

### Required Information

| Item | Description |
|------|-------------|
| **Game name** | Full name + Steam/store page link |
| **UE version** | If known (from RE-UE4SS, SteamDB, or other source). "Unknown" is fine |
| **Scan log** | Full `UE5Dumper-scan-0.log` (attach as file) |
| **UI log** | Full `UE5DumpUI-0.log` if the UI was involved (attach as file) |
| **Screenshot** | Screenshot of the UI showing the failure state |
| **What works / what doesn't** | Example: "Object Tree loads but names are garbled" or "Pipe connection fails" |
| **CE version** | Cheat Engine version used |
| **RE-UE4SS status** | Does RE-UE4SS work on this game? If yes, which version and any custom config? |

### Log File Locations

| Log | Path |
|-----|------|
| DLL scan log | `%LOCALAPPDATA%\UE5CEDumper\Logs\<ProcessName>\scan-0.log` |
| DLL offsets log | `%LOCALAPPDATA%\UE5CEDumper\Logs\<ProcessName>\offsets-0.log` |
| DLL pipe log | `%LOCALAPPDATA%\UE5CEDumper\Logs\<ProcessName>\pipe-0.log` |
| DLL walk log | `%LOCALAPPDATA%\UE5CEDumper\Logs\<ProcessName>\walk-0.log` |
| UI log | `%LOCALAPPDATA%\UE5CEDumper\Logs\ui-0.log` |

### Failure Categories

To help us triage faster, indicate which category matches your issue:

| Category | Symptoms |
|----------|----------|
| **No connection** | UI cannot connect to pipe, or DLL scan aborts before pipe starts |
| **GObjects not found** | Scan log shows "GObjects not found" or all patterns fail |
| **GNames not found** | Scan log shows "GNames not found" or all validators fail |
| **Garbled names** | Object Tree loads but names contain `????`, truncated text, or random characters |
| **Empty Object Tree** | UI connects, shows object count > 0, but tree is empty |
| **GWorld failure** | "Start from GWorld" button shows nothing or error message |
| **Crash on inject** | Game crashes when DLL is injected |
| **Wrong UE version** | Detected version doesn't match actual engine version |

### What Helps Us Most

The **scan log** is the single most valuable piece of information. It contains hex dumps, validation results, and diagnostic data that allow us to diagnose issues without having the game ourselves. Please always attach the full scan log, not just excerpts.

---

## Code Contributions

### Pull Request Process

1. Fork the repository and create a feature branch from `dev`.
2. Follow the existing code style and conventions.
3. Ensure both DLL and UI build successfully (`build release`).
4. Run UI tests (`build test`).
5. If adding AOB patterns, include the evidence described above in the PR description.
6. Submit a PR targeting the `dev` branch.

### Code Style

- **C++ (DLL)**: C++23, MSVC. Use `LOG_INFO`/`LOG_DEBUG`/`LOG_WARN` macros for logging. Use `Mem::ReadSafe` for all memory reads (SEH-protected).
- **C# (UI)**: .NET 10, Avalonia 11, CommunityToolkit.Mvvm. All I/O must be async. UI strings in `Resources/Strings/en.axaml`.
- **Comments and UI strings**: English only.
- **Platform abstraction**: Any OS-dependent call must go through an interface in `Core/`. The `Core` project must never contain direct platform-specific code.

### Commit Messages

- Use concise, descriptive commit messages.
- Reference issue numbers where applicable (e.g., `Fix stride detection for UE4.18 (#42)`).

---

## Development Setup

### Prerequisites

- Visual Studio 2022+ (v17+) with C++ Desktop workload
- .NET SDK 10.0
- CMake 3.25+
- Ninja (any recent version)
- Cheat Engine 7.5+ (for testing)

### Building

```cmd
:: Full build (DLL + UI)
build release

:: DLL only
build dll

:: UI only
build ui

:: Run tests
build test
```

### Testing

Testing requires a running UE4/UE5 game. See `CLAUDE.md` for the list of recommended test games and known working configurations.

---

## Questions?

Feel free to open a [Discussion](../../discussions) for questions that aren't bug reports or feature requests.
