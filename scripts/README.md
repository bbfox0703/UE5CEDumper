# Scripts

Cheat Engine artefacts that ship with UE5CEDumper.

> **What's actually in use**: only the files in the table below. Everything else has been removed (see git history if you need an older variant).

---

## File Overview

| File | Purpose | Deployment |
|------|---------|------------|
| `UE5CEDumper.CT` | Main CE Cheat Table — DLL injection, init, pipe server | Copied to `dist/` by build |
| `ue5_dissect.lua` | CE Structure Dissect builder — generates CE struct definitions from UE reflection | Copied to `dist/` by build |
| `ue5_invoke_helper.lua` | Runtime helper required by AA Scripts produced via UE5DumpUI's "Copy AA Script (Baked)" / Interesting Funcs AA(B) flow | **Embedded in `UE5DumpUI.exe`** as a manifest resource — see [HelperLuaResource.cs](../ui/UE5DumpUI/Services/HelperLuaResource.cs) — and shipped into the user's open .CT either via Tools → Inject Helper (one click via AOBMaker) or Tools → Export CE Helper Lua File... + manual `Table -> Add File...` |
| `inject-ue.ps1` | Command-line injector — list running UE4/UE5 games and inject `UE5Dumper.dll` (CreateRemoteThread + LoadLibraryW). No Cheat Engine needed. | Ship next to `UE5Dumper.dll` (e.g. copy into `dist/`) |
| `startup-shortcut.ps1` | Create / remove / inspect a **per-user Start Menu Startup shortcut** for `UE5DumpUI.exe`, so the UI launches at sign-in. Resolves the exe from its own folder. | **Not deployed** (build no longer copies it — 2026-08-20). Run it from `scripts/`, or copy it next to `UE5DumpUI.exe` yourself: it resolves the exe from its own directory first, so it must sit beside the exe to find it automatically. |
| `startup_shortcut.py` | Same tool, same verbs and exit codes, in stdlib Python (`IShellLinkW` via `ctypes`). Exists because Bitdefender's behavioural layer quarantined the `.ps1` — see below. | **Not deployed** (build no longer copies it — 2026-08-20). Same note as above. |
| `test_pipe.ps1` | Dev-only pipe protocol test script — mocks the pipe **server** so the UI can be driven with no game | Not deployed |
| `xref_probe.ps1` | Dev-only headless **client** for `find_property_xrefs` / `walk_function_props` — the mirror of `test_pipe.ps1`. Needs a running game with `UE5Dumper.dll` injected and its pipe server up; with no game it times out on `Connect()` after 5 s, which is the expected result, not a fault. | Not deployed |
| `tests/*.lua` | **Executable tests for the Lua helpers** — they run the real scripts against stubbed CE globals. See below. | Not deployed |
| `DEPLOY_README.md` | End-user deployment doc — copied into `dist/` as `README.md` | Copied to `dist/README.md` |

---

## inject-ue.ps1

A standalone command-line injector — an alternative to the Cheat Engine `.CT`
inject and the `version.dll` proxy. One combined CLI (list + inject + auto):

```powershell
.\inject-ue.ps1                 # AUTO: exactly one UE game running -> inject it;
                                #        0 -> abort; 2+ -> list them + abort
.\inject-ue.ps1 -List           # list detected UE processes and exit
.\inject-ue.ps1 -List -All      # list every accessible x64 process
.\inject-ue.ps1 -ProcessId 1234 # inject into a specific PID
.\inject-ue.ps1 -Dll C:\path\UE5Dumper.dll   # override the DLL to inject
```

- **UE detection** is by the executable path (same heuristics as the UI drive
  scan): a `*-Shipping.exe` name, or an exe under `\Binaries\Win64\` with an
  `Engine` folder / `Content\Paks` up the tree.
- **x64 only** — `UE5Dumper.dll` is 64-bit; 32-bit targets are reported and skipped.
- **DLL path** defaults to `UE5Dumper.dll` next to the script, else `..\dist\`.
- Re-injecting is a safe no-op (`LoadLibraryW` returns the existing handle); the
  script skips it unless `-Force`.
- After a successful inject the DLL starts its pipe server automatically — launch
  `UE5DumpUI.exe` and **Connect**.
- **Auto-elevation:** if the game runs as Administrator, injection hits Access
  Denied and the script **auto-relaunches itself elevated** (one UAC prompt) — no
  manual `Run as administrator`.
- **Notes:** real-time AV / anti-cheat may flag `CreateRemoteThread` injection —
  single-player / offline use only.

---

## startup-shortcut.ps1 / startup_shortcut.py

Put `UE5DumpUI.exe` in the **current user's** Start Menu Startup folder, so it
launches at sign-in. Two implementations of one tool — same verbs, same exit
codes, same refusals. Both ship into `dist/`, beside the exe.

```powershell
.\startup-shortcut.ps1              # STATUS (default) — read-only, changes nothing
.\startup-shortcut.ps1 install
.\startup-shortcut.ps1 install -Minimized
.\startup-shortcut.ps1 remove
```
```powershell
py startup_shortcut.py              # identical, --minimized / --force / --name
```

- **Target resolution:** `<script dir>\UE5DumpUI.exe` first — the shipped case,
  so an unzipped release needs no arguments — then `..\dist\`, `dist\`, `cwd`.
  Same order `inject-ue.ps1` uses for `UE5Dumper.dll`.
- **No pipe, no network.** Install is: resolve the path → confirm the file
  exists → write the `.lnk` → read it back. The UI and the DLL do not need to be
  running. (`xref_probe.ps1` is the script in this folder that *does* connect —
  see its row above.)
- **Current user only, by design.** The folder comes from the shell
  (`GetFolderPath('Startup')` / `SHGetKnownFolderPath`), never from a literal
  `%APPDATA%\...` path — that one is localised on non-English Windows and
  relocatable by policy. The all-users folder is deliberately unsupported: it
  needs elevation, and a debugging tool that starts for every account on the
  machine is not a default anyone asked for.
- **It refuses to clobber other programs' shortcuts.** `install` will not
  overwrite a shortcut of that name pointing elsewhere; `remove` will not delete
  one whose target is not `UE5DumpUI.exe`. Both say what they found; both take
  `-Force` / `--force`.
- **Every write is read back.** `Save()` reports success by not failing, which is
  not the same as having written what you asked for — and a wrong Startup
  shortcut gives no feedback at all until the next sign-in.
- **Exit codes:** `0` ok, `1` error, `2` not installed, `3` installed but the
  target is missing or foreign. `2` and `3` are distinct so a wrapper can tell
  "never set up" from "set up and since broken" — the second is what a moved or
  re-extracted `dist\` looks like, and it is the failure users actually hit.
- **`--startup-dir` (Python)** points the whole tool at another folder. It exists
  so the write path can be tested in a temp directory: installing into the *real*
  Startup folder is a genuine persistence change to the machine and should not be
  something a test run does casually.

> **Why two implementations.** Bitdefender's Advanced Threat Defense quarantined
> the `.ps1` (along with `build.ps1` and two `tools/*.py`) the first time it ran.
> The detection was **behavioural, not a signature**: an unsigned parent process
> spawning `pwsh` spawning `powershell`, which then wrote a `.lnk` into the
> Startup folder. That is a textbook persistence shape and an AV is right to look
> at it. Neither script tries to look like anything else — the Python twin simply
> gives a machine where the PowerShell host is the problem another interpreter to
> do the same job. If yours trips too, the honest fix is a folder exclusion for
> the repo / release directory, not a workaround.

---

## UE5CEDumper.CT

The main Cheat Table is **self-contained** — all Lua code (DLL loading, initialization, pipe server start) is embedded inline. No external `.lua` files are required.

### Usage

1. Open a UE game in Cheat Engine
2. Load `UE5CEDumper.CT` (File > Open)
3. Enable the **init** entry — this injects `UE5Dumper.dll` and starts the pipe server
4. Launch `UE5DumpUI.exe` and click **Connect**

---

## ue5_dissect.lua

A standalone CE Lua module that creates **Structure Dissect** entries from UE class reflection data. It calls the injected `UE5Dumper.dll` exports to walk class hierarchies and map UE property types to CE structure elements.

### Prerequisites

- Game process must be open in Cheat Engine
- `UE5Dumper.dll` must be injected and initialized (via `UE5CEDumper.CT` or manual `loadLibrary`)

### Features

- **25+ UE property type mappings** — Int, Float, Bool, Enum, Name, Str, Object, Array, Map, Set, Struct, Delegate, etc.
- **StructProperty flattening** — recursively resolves inner struct fields (up to 6 levels)
- **BoolProperty bitmask** — sets CE `ChildStructStart` for bitfield display
- **Array/Map/Set helpers** — emits pointer + `_count` + `_capacity` elements
- **UObject header** — auto-adds VTable, ObjectFlags, Class, FNameIndex, Outer
- **Auto callback** — registers `registerStructureDissectOverride` so CE auto-fills when you open Structure Dissect on any UObject address
- **Caching** — structures are cached by class name to avoid redundant DLL calls

### Required DLL Exports

The script depends on these `UE5Dumper.dll` exports:

| Export | Purpose |
|--------|---------|
| `UE5_WalkClassBegin` | Start walking class fields |
| `UE5_WalkClassGetField` | Get field details (name, type, offset, size, address) |
| `UE5_WalkClassEnd` | End class walk |
| `UE5_GetFieldBoolMask` | Get BoolProperty field mask byte |
| `UE5_GetFieldStructClass` | Get StructProperty inner UScriptStruct* |
| `UE5_GetClassPropsSize` | Get UStruct::PropertiesSize |
| `UE5_GetObjectName` | Resolve object name |
| `UE5_GetObjectClass` | Get UObject class pointer |
| `UE5_FindObject` | Find object by full path |
| `UE5_FindClass` | Find UClass by name |

### Quick Start

```lua
-- In CE Lua Engine (after DLL is injected):

-- Load the module
local dissect = dofile("ue5_dissect.lua")

-- Option 1: Interactive — shows dialog to enter class address or UE path
dissect.createInteractive()

-- Option 2: By UE path
dissect.createFromPath("/Script/Engine.Actor")

-- Option 3: By class address (hex)
dissect.createFromClass(0x7FF6A1234567)

-- Option 4: Auto mode — CE auto-fills Structure Dissect for any UObject
dissect.enableAutoCallback()
-- Now open "Dissect data/structure" on any UObject address in CE
```

### API Reference

| Function | Description |
|----------|-------------|
| `dissect.createFromClass(classAddr, [structName])` | Create CE structure from a UClass address |
| `dissect.createFromPath(fullPath)` | Create CE structure from a full UE object path |
| `dissect.createInteractive()` | Show input dialog, create structure from user input |
| `dissect.enableAutoCallback()` | Register CE dissect override — auto-fills on any UObject |
| `dissect.disableAutoCallback()` | Unregister the auto-fill callbacks |
| `dissect.clearAll()` | Destroy all created structures and clear cache |

### Type Mapping

UE property types are mapped to CE structure element types:

| UE Property | CE Vartype | Size |
|-------------|-----------|------|
| IntProperty, UInt32Property | vtDword | 4 |
| Int16Property, UInt16Property | vtWord | 2 |
| ByteProperty, Int8Property, BoolProperty | vtByte | 1 |
| Int64Property, UInt64Property | vtQword | 8 |
| FloatProperty | vtSingle | 4 |
| DoubleProperty | vtDouble | 8 |
| NameProperty | vtQword | 8 |
| ObjectProperty, StrProperty, ArrayProperty, MapProperty, SetProperty | vtPointer | 8 |
| StructProperty | (flattened inline) | — |
| Unknown types | vtDword | field size |

---

## ue5_invoke_helper.lua

The runtime mailbox-protocol shim for **AA Script (Baked)** invocations generated from UE5DumpUI's LiveWalker `AA(Baked)` button and Interesting Funcs `AA(B)` button. It exposes two public functions to AA Script code:

| Function | Description |
|----------|-------------|
| `invokeUFunction(className, funcName, parmsSize, params)` | Marshal a `CMD_INVOKE_BY_NAME` mailbox request to the DLL: find non-CDO instance → find UFunction → write baked params → ProcessEvent → return ok/err |
| `readUFunctionReturn(offset, valueType)` | Decode a single scalar from the params buffer. `int32` (the default) and `int16` are **signed** — a UFunction returning `-1` reads as `-1`, not `4294967295`; `uint32`/`dword` and `uint16`/`word` are the unsigned spellings, plus `float` / `double` / `bool` / `byte` / `uint64`/`qword`. Used by Verify Return Value mode |

### How it lands in your .CT

The user's .CT must contain this file as an **embedded table file** (CE → File List view) for any baked AA Script to work. Two paths to get there, both starting from inside UE5DumpUI:

1. **Tools → Inject Helper into Current CE Table** — one click. Routes through the AOBMaker CE Plugin's `InjectTableFile` pipe command (`createStringStream` + `Stream.copyFrom`). Requires AOBMaker plugin to be loaded in CE; falls back gracefully if not.
2. **Tools → Export CE Helper Lua File...** — saves a copy to disk, then you add it via CE's `Table -> Add File...` menu. Manual fallback for users without AOBMaker.

The file is the same in both cases — read from the manifest resource embedded in `UE5DumpUI.exe`. You don't need a copy in `dist/`; the EXE carries it.

### Generated AA Script flow

```lua
local tf = findTableFile('ue5_invoke_helper.lua')
-- ... ss.copyFrom + load + fn() ...
local ok, err = invokeUFunction('Class', 'Func', parmsSize, PARAMS)
```

If `findTableFile` returns nil the script `showMessage`s a setup hint and bails — there is **no filesystem fallback** by design (avoids ambiguity over which copy is in use).

### Re-injection

You only need to re-run **Inject Helper** when:

- The helper itself changes (we'll note that in [docs/dev-log.md](../docs/dev-log.md))
- You start from a fresh `.CT` that has no helper embedded yet
- You manually deleted the helper from your .CT

Day-to-day, once-per-table is enough.

---

## tests/ — executable tests for the Lua helpers

The C# suite can only assert on these scripts' **source text**, so a change can satisfy every string
assertion and still behave wrongly. These rigs stub the handful of Cheat Engine globals each script
touches over plain Lua tables, run the **real** functions, and check what actually happened.

| Rig | Covers |
|-----|--------|
| `tests/freeze_helper_test.lua` | `ue5_freeze_helper.lua` — packed-bitfield bool writes, the recycled-slot identity guard, the failing-rescan stop (audit #5 AA1–AA3) |
| `tests/dissect_test.lua` | `ue5_dissect.lua` — the `callDLL` raise contract, the CE-callback barriers, and that a failed walk registers nothing (audit #5 AA4–AA7) |
| `tests/invoke_helper_test.lua` | `ue5_invoke_helper.lua` — the mailbox round-trip: a refused allocation, the param-type tokens the C# generator emits, the params-buffer wipe, timeout diagnosis and reentrancy, signed return decoding (audit #5 AA14–AA20) |

```bash
lua scripts/tests/dissect_test.lua
lua scripts/tests/freeze_helper_test.lua
lua scripts/tests/invoke_helper_test.lua
```

Exit 0 = all pass, 1 = a failure (with the case named). `luac -p <file>` syntax-checks any script.

**Deliberately not wired into `build.ps1` or CI.** A standalone `lua` is not a declared dependency of
this repo, and a test step that silently *skips* when its tool is missing is exactly the defect audit
#5's AD1/AD2 fixed in the C++ test phase. These fail loudly when run rather than passing quietly when
not — so run them whenever you touch the script they cover.

Four traps, all measured rather than guessed, if you write a fourth rig:

- **How the chunk hands you its API differs per file.** `ue5_dissect.lua` **returns** its public
  table; `ue5_freeze_helper.lua` and `ue5_invoke_helper.lua` define **globals** and return nothing.
  Capture the return value for the first, not for the other two.
- **CE's `vt*` constants must exist BEFORE the chunk runs** (dissect only) — `TYPE_MAP` is a
  file-scope literal, so it captures them at load time. Stub them afterwards and mapped types
  silently get `Vartype = nil` while `EnumProperty`, the unknown-type fallback and the UObject
  header rows still resolve, which is harder to diagnose than a uniform failure.
- **Re-declaration guards make a reload a no-op** (`invoke`, `freeze`). `if not invokeUFunction then`
  means a second `loadfile()+call` rebinds nothing, so a rig cannot reload between cases for a clean
  slate — reset the helper's globals by hand instead (`_ue5_invoke_busy`, `_ue5_invoke_str_bufs`).
  Miss one and state leaks silently from case to case.
- **⚠ Stub FIDELITY decides what the rig can see.** CE does *not* raise on a nil address:
  `lua_toaddress` falls through to `lua_tointeger`, and `lua_tointeger(nil)` is `0`, so
  `writeBytes(nil, t)` writes to address **0** and returns. The first version of the invoke rig
  raised instead — which turned AA14's *silent success* (an FString with `Data = 0` and
  `ArrayNum = n+1` sent to a live UFunction, reported as `ok = true`) into a clean failure, and made
  three of that case's assertions pass for the wrong reason. **A stub that is stricter than CE hides
  exactly the defects worth finding.**

---

## test_pipe.ps1

Dev-only PowerShell script that exercises the named pipe protocol against a running game with `UE5Dumper.dll` injected. Used to smoke-test pipe command wiring without going through the UI. Not part of any release artefact.
