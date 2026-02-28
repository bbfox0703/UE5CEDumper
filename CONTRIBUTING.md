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

AOB (Array of Bytes) patterns are the core mechanism for locating engine globals (GObjects, GNames, GWorld). We currently have **95+ patterns from 13 sources** in `dll/src/Signatures.h`, including MSVC symbol exports and multiple resolution strategies.

### Why Quality Matters

A bad AOB pattern can cause false positives — matching the wrong memory location, leading to crashes or garbled data. A pattern that is too short or too generic may match multiple locations, making validation unreliable. Since maintainers may not own every game, we need sufficient evidence that a contributed pattern is correct **and** of high enough quality to be reliable across game updates.

### Pattern Quality Guidelines

Before submitting, please evaluate your pattern against these criteria:

#### 1. UE Version Compatibility

Always specify which UE version(s) the pattern was tested on. UE internal structures change between major versions — a pattern working on UE5.3 may not work on UE4.27 or UE5.5. Include:
- The exact UE version (e.g., `UE 5.04`, `UE 4.27`, `UE 5.5`)
- Source of version info: PE VERSIONINFO, our tool's detection, RE-UE4SS config, or SteamDB

#### 2. Prefer Core Engine Functions

The best AOB patterns target **UE core engine functions** that are stable across builds and games. Preferred sources:

| Priority | Function / Location | Why |
|----------|---------------------|-----|
| **Best** | `FUObjectArray` constructor / `UObject::StaticAllocateObject` | Core allocation — always exists |
| **Best** | `FName::ToString`, `FName::FName()` constructor | FNamePool access — fundamental |
| **Best** | `UGameEngine::Tick`, `UWorld::Tick` | GWorld access — standard engine loop |
| **Good** | `FGCObject` related functions | GObjects references in GC subsystem |
| **Good** | MSVC mangled symbol exports (`?GUObjectArray@@3V...`) | Exact match — no ambiguity |
| **Avoid** | Game-specific blueprints or custom code | Breaks on different games |
| **Avoid** | Initialization-only code that may be optimized out | Unreliable across compiler versions |

#### 3. Match Precision

Pattern quality depends on **how uniquely it matches** in the target module:

| Match Count | Assessment |
|-------------|-----------|
| **1 (unique)** | Ideal — no ambiguity |
| **2-5** | Acceptable if validation confirms the correct one |
| **6+** | Too generic — add more context bytes or wildcards to narrow down |
| **0** | Pattern doesn't match — may be version-specific, still submit with version info |

You can check match count in the scan log — look for lines like `"matched at 0x..."` for your pattern ID.

#### 4. Instruction Context

- **Use full instructions**: Include complete x86-64 instructions, not arbitrary byte boundaries
- **RIP-relative patterns**: The `48 8B 05 ?? ?? ?? ??` (mov rax,[rip+disp32]) or `48 8D 0D ?? ?? ?? ??` (lea rcx,[rip+disp32]) prefix is the standard form. Include surrounding instructions for uniqueness
- **Register selection matters**: Patterns using specific GPRs (General Purpose Registers) like `r8`, `r9`, `r10` via REX prefix (`4C` vs `48`) provide extra specificity
- **Minimum length**: Patterns should be at least 10 bytes (ideally 15+) to reduce false positives

#### 5. Symbol Exports (Alternative to AOB)

If the game binary exports MSVC mangled symbols (common in non-monolithic / modular UE builds), these are the most reliable method:

```cpp
// Direct variable export — address IS the global
"?GUObjectArray@@3VFUObjectArray@@A"     // → GObjects
"?GWorld@@3VUWorldProxy@@A"              // → GWorld

// Function export — scan function body for RIP references to the global
"?ToString@FName@@QEBAXAEAVFString@@@Z"  // → GNames (via FNamePool reference)
"??0FName@@QEAA@PEB_WW4EFindName@@@Z"   // → GNames (via FName constructor)
```

Symbol exports have **priority 0** (tried first) because they are exact matches with zero false-positive risk.

### What to Provide

Open an issue with the label `aob-pattern` and include **all** of the following:

| Item | Description |
|------|-------------|
| **Pattern bytes** | Hex string with `??` wildcards. Example: `48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 48 8D 04 D1` |
| **Target** | Which global: `GObjects`, `GNames`, or `GWorld` |
| **Resolution type** | `rip-direct`, `rip-deref`, `rip-both`, `symbol-export`, `symbol-call-follow`, or `call-follow` |
| **RIP offset** | If RIP-relative: byte offset from pattern start to the RIP instruction, and opcode length before the 4-byte displacement |
| **Game name** | Full name as shown on Steam/store page |
| **UE version** | Exact version (e.g., `UE 5.04`) + how determined (PE VERSIONINFO / tool detection / RE-UE4SS / SteamDB) |
| **Source function** | What engine function this pattern is from (e.g., `FUObjectArray::AllocateUObjectIndex`, `FName::ToString`). Use IDA/Ghidra/x64dbg to identify |
| **Match count** | How many times this pattern matches in the game module (from scan log) |
| **Scan log excerpt** | The relevant section from the scan log showing the pattern match and validation result |
| **Object Tree screenshot** | Screenshot of the UI showing valid object names (not garbled) |

### Log File Location

```
%LOCALAPPDATA%\UE5CEDumper\Logs\<ProcessName>\scan-0.log
```

### Scan Log Example

A valid pattern contribution should show log output similar to:

```
[INFO] [SCAN] GOBJ_V_NEW: 1 match(es), best=0x7FF71B7A1820
[INFO] [SCAN] ValidateGObjects: NumElements=483670, Layout A
[INFO] [SCAN] GObjects confirmed at 0x7FF71B7A1820 via GOBJ_V_NEW
```

### Verification Process

1. **Maintainer review**: We check pattern format, resolution logic, instruction boundaries, and match precision.
2. **Log validation**: The scan log must show successful validation (NumElements in reasonable range, valid layout detected, reasonable match count).
3. **Visual confirmation**: The Object Tree screenshot must show recognizable UE type names (Package, Class, Object, BlueprintGeneratedClass, etc.), not garbled text.
4. **Uniqueness check**: Patterns matching 6+ locations in a single module will be rejected unless additional context bytes are added to reduce matches.
5. **Third-party confirmation** (preferred): If another user can confirm the pattern works on the same game, the pattern is accepted with higher confidence.
6. **Regression check**: We verify the pattern doesn't false-match on our existing test games before merging.

### Pattern Style Guide

Follow the existing conventions in `Signatures.h`:

```cpp
// AOB pattern definition
constexpr const char* AOB_GOBJECTS_VNEW = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 48 85 C9 74";

// Registration in GOBJECTS_PATTERNS[] using SIG_RIP macro:
//   SIG_RIP(id, pattern, target, instrOffset, opcodeLen, totalLen, adjustment, priority, source, notes)
SIG_RIP("GOBJ_VNEW", AOB_GOBJECTS_VNEW, AobTarget::GObjects,
        0, 3, 7, 0, 50, "Community", "FUObjectArray access in AllocateUObjectIndex (UE 5.04)"),
```

For symbol exports:
```cpp
SIG_EXPORT("GWLD_EXP", EXPORT_GWORLD, AobTarget::GWorld, 0, "UWorldProxy symbol"),
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
