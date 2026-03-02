# UE5CEDumper — Quick Start

UE5CEDumper is a live inspector for Unreal Engine games (UE4.18 – UE5.7).
It injects a DLL into the game process, then communicates with a standalone UI via Named Pipe.

---

## Package Contents

| File | Description |
|------|-------------|
| `UE5DumpUI.exe` | Standalone UI application (connect, browse, export) |
| `UE5Dumper.dll` | Injected DLL — used with Cheat Engine (Option A) |
| `version.dll` | Proxy DLL — auto-loads without CE (Option B) |
| `UE5CEDumper.CT` | CE Cheat Table — loads and injects the DLL |
| `ue5_dissect.lua` | CE Structure Dissect builder (optional, for advanced CE users) |

---

## Injection Methods

There are **two ways** to get UE5Dumper running inside the game process.
Choose one — do **not** use both at the same time.

### Option A: Cheat Engine Injection

Best for: CE power users who want full CE integration (Structure Dissect, memory editing, Lua scripting).

**Required files**: `UE5Dumper.dll`, `UE5CEDumper.CT`, `UE5DumpUI.exe`

1. Open **Cheat Engine** and attach to the game process.
2. Load a save / reach gameplay so UE objects are populated.
3. Open `UE5CEDumper.CT` (File > Open).
4. Enable `init <== enable after process attached`, then `Inject DLL + Start Pipe Server`.
5. Wait a few seconds for the AOB scan to finish.
6. Launch `UE5DumpUI.exe` and click **Connect**.

> **CE-specific extras**: With CE injection, you can also use `ue5_dissect.lua` to generate
> CE Structure Dissect entries from UE class reflection. See the *CE Scripts* section below.

### Option B: Proxy DLL (No Cheat Engine)

Best for: users who don't need CE, or when CE causes compatibility issues with the game.

**Required files**: `version.dll`, `UE5DumpUI.exe`

1. Copy `version.dll` into the **game's root folder** (next to the game `.exe`).
2. Launch the game normally. The proxy DLL loads automatically via Windows DLL search order.
3. Load a save / reach gameplay so UE objects are populated.
4. Launch `UE5DumpUI.exe` and click **Connect**.
5. Click the **Start Scan** button in the toolbar. The DLL performs the AOB scan and returns engine data.
6. Browse objects, find instances, export — same workflow as Option A.

> **Why deferred scan?** The proxy DLL loads very early (before the game's main loop).
> At that point, GObjects/GNames may not be initialized yet. Waiting until you reach
> gameplay ensures the scan finds valid data.

> **Removing**: To stop using the proxy DLL, simply delete `version.dll` from the game folder.

### Comparison

| | Option A (CE Inject) | Option B (Proxy DLL) |
|---|---|---|
| Requires Cheat Engine | Yes | No |
| Auto-loads on game start | No (manual inject) | Yes |
| CE Structure Dissect | Yes (`ue5_dissect.lua`) | No |
| CE memory editing | Yes | No (UI read-only browse) |
| Scan timing | Immediate on inject | Deferred (click Start Scan) |
| Anti-cheat risk | CE may be detected | Lower profile (common DLL name) |

### Dual-Injection Safety

If you accidentally have `version.dll` in the game folder **and** try to inject `UE5Dumper.dll` via CE:

- The second instance detects that the pipe `\\.\pipe\UE5DumpBfx` already exists.
- It **skips** auto-start to prevent conflicts.
- No crash, no duplicate data — the first instance wins.

Best practice: use one method at a time. Remove `version.dll` from the game folder when using CE injection.

---

## CE Scripts (Option A Only)

### ue5_dissect.lua

A CE Lua module that creates **Structure Dissect** entries from UE class reflection.
Only works when `UE5Dumper.dll` is injected via CE.

```lua
-- In CE Lua Engine (after DLL is injected):
local dissect = dofile("ue5_dissect.lua")

-- Interactive: shows dialog to enter class address or UE path
dissect.createInteractive()

-- By UE path
dissect.createFromPath("/Script/Engine.Actor")

-- Auto mode: CE auto-fills Structure Dissect for any UObject address
dissect.enableAutoCallback()
```

Features: 25+ UE property type mappings, struct flattening (6 levels), BoolProperty bitmask, Array/Map/Set helpers, gap filling, UObject header, auto-callback, caching.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| UI says "Connection failed" | Pipe server not running | Make sure DLL is injected (Option A) or version.dll is in game folder (Option B) |
| UI connects but shows 0 objects | Scan not run or game data not loaded | Load a save first, then inject (A) or click Start Scan (B) |
| Start Scan button not visible | Already scanned, or CE inject mode | Normal — button only appears in proxy DLL mode before first scan |
| Game crashes on launch with version.dll | Rare DLL conflict | File a bug report — alternative proxy DLL targets may be needed |
| "UE version: Unknown" | AOB patterns didn't match | File a bug report with game name + UE version |

---

## More Information

- Project page: https://github.com/user/UE5CEDumper
- Pre-compiled releases: https://opencheattables.com/viewtopic.php?f=17&t=1797
- Bug reports: Include the game name, UE version, and logs from `%LOCALAPPDATA%\UE5CEDumper\Logs\`
