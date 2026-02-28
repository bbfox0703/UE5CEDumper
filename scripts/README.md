# Scripts

Cheat Engine Lua scripts and table files for UE5CEDumper.

---

## File Overview

| File | Purpose | Deployment |
|------|---------|------------|
| `UE5CEDumper.CT` | Main CE Cheat Table — DLL injection, init, pipe server | Copied to `dist/` by build |
| `ue5_dissect.lua` | CE Structure Dissect builder — generates CE struct definitions from UE reflection | Copied to `dist/` by build |
| `ue5dump.lua` | Legacy standalone loader (superseded by CT) | Not deployed |
| `utils.lua` | Legacy helper utilities (superseded by CT) | Not deployed |
| `test_pipe.ps1` | Dev-only pipe protocol test script | Not deployed |

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
- **Gap filling** — fills unused byte ranges with `vtPointer` placeholders
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

## Legacy Scripts

### ue5dump.lua

Standalone script that loads `UE5Dumper.dll`, calls `UE5_Init`, and starts the pipe server. This functionality is now fully embedded in `UE5CEDumper.CT` — the standalone script is kept for reference only.

### utils.lua

Helper library used by `ue5dump.lua` (`callDLL`, `log`, `addrToHex`). Not needed when using the CT or `ue5_dissect.lua` (which has its own built-in helpers).
