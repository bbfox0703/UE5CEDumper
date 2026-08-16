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
| `test_pipe.ps1` | Dev-only pipe protocol test script | Not deployed |
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
| `readUFunctionReturn(offset, valueType)` | Decode a single scalar from the params buffer (`int32` / `float` / `double` / `bool` / `byte` / `qword` / `int16`). Used by Verify Return Value mode |

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

## test_pipe.ps1

Dev-only PowerShell script that exercises the named pipe protocol against a running game with `UE5Dumper.dll` injected. Used to smoke-test pipe command wiring without going through the UI. Not part of any release artefact.
