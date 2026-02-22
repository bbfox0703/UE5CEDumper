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

AOB (Array of Bytes) patterns are the core mechanism for locating engine globals (GObjects, GNames, GWorld). We currently have **51 patterns from 6 sources** in `dll/src/Signatures.h`.

### Why Verification Matters

A bad AOB pattern can cause false positives — matching the wrong memory location, leading to crashes or garbled data. Since maintainers may not own every game, we need sufficient evidence that a contributed pattern is correct.

### What to Provide

Open an issue with the label `aob-pattern` and include **all** of the following:

| Item | Description |
|------|-------------|
| **Pattern bytes** | Hex string with `??` wildcards. Example: `48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 48 8D 04 D1` |
| **Target** | Which global: `GObjects`, `GNames`, or `GWorld` |
| **Resolution type** | `direct` (address = match), `rip-relative` (RIP + offset), or `dereference` (read pointer at resolved address) |
| **RIP offset** | If RIP-relative: the byte offset from the start of the pattern to the 4-byte displacement (e.g., `3` for `48 8B 05 [xx xx xx xx]`) |
| **Game name** | Full name as shown on Steam/store page |
| **UE version** | From PE VERSIONINFO, or our tool's detection, or RE-UE4SS config |
| **Scan log excerpt** | The relevant section from `UE5Dumper-scan-0.log` showing the pattern match and validation result |
| **Object Tree screenshot** | Screenshot of the UI showing valid object names (not garbled) |

### Log File Location

```
%LOCALAPPDATA%\UE5CEDumper\Logs\UE5Dumper-scan-0.log
```

### Scan Log Example

A valid pattern contribution should show log output similar to:

```
[INFO][SCAN] Pattern V_NEW matched at 0x7FF71A234560
[INFO][SCAN] Resolved via RIP: 0x7FF71B7A1820
[INFO][SCAN] ValidateGObjects: NumElements=483670, Layout A (Objects+0x00, Num+0x14)
[INFO][SCAN] GObjects confirmed at 0x7FF71B7A1820
```

### Verification Process

1. **Maintainer review**: We check that the pattern format is valid and the resolution logic is correct.
2. **Log validation**: The scan log must show successful validation (NumElements in reasonable range, valid layout detected).
3. **Visual confirmation**: The Object Tree screenshot must show recognizable UE type names (Package, Class, Object, BlueprintGeneratedClass, etc.), not garbled text.
4. **Third-party confirmation** (preferred): If another user can confirm the pattern works on the same game, the pattern is accepted with higher confidence. This is strongly encouraged but not strictly required if the log + screenshot evidence is clear.
5. **Regression check**: We verify the pattern doesn't false-match on our existing test games before merging.

### Pattern Style Guide

Follow the existing conventions in `Signatures.h`:

```cpp
// V_NEW — Short description of where this pattern comes from
// Target: GObjects | GNames | GWorld
// Resolution: direct | rip(offset) | deref
// Tested on: Game Name (UE X.XX)
{ "\x48\x8B\x05\x00\x00\x00\x00\x48\x8B\x0C\xC8",
  "xxxx???xxxx", 11, PatternSource::Community },
```

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
| DLL scan log | `%LOCALAPPDATA%\UE5CEDumper\Logs\UE5Dumper-scan-0.log` |
| DLL pipe log | `%LOCALAPPDATA%\UE5CEDumper\Logs\UE5Dumper-pipe-0.log` |
| UI log | `%APPDATA%\UE5CEDumper\Logs\UE5DumpUI-0.log` |
| Per-process mirror | `%LOCALAPPDATA%\UE5CEDumper\Logs\<ProcessName>\UE5Dumper-scan-0.log` |

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
