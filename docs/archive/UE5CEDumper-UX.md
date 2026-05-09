# UE5CEDumper — UX Design Specification

> **Document type:** UX Design
> **Project:** UE5CEDumper — Avalonia UI (UE5DumpUI.exe)
> **Date:** 2026-03-10
> **Companion:** [UE5CEDumper-SA.md](UE5CEDumper-SA.md), [UE5CEDumper-SD.md](UE5CEDumper-SD.md)

---

## 1. Design Principles

| Principle | Description |
|-----------|------------|
| **Tool-first** | Users are reverse engineers — prioritize data density and keyboard efficiency over decorative UI |
| **Status always visible** | Connection state, scan progress, and errors are always on screen, never hidden behind modals |
| **Non-blocking** | All operations are async; UI never freezes; progress indicators shown for long ops |
| **Exports accessible** | Export actions (CE XML, CSX, SDK header) are one click from any relevant panel |
| **Error tolerance** | Null GWorld, unresolvable pointers, and partial results degrade gracefully without crashing |

---

## 2. Visual Design

### 2.1 Theme

- **Theme:** Avalonia `FluentTheme` — Dark mode only
- **Background:** `#1E1E1E` (window, panels)
- **Surface:** `#2D2D2D` (toolbars, tab headers)
- **Border:** `#3C3C3C` (panel separators, splitters)
- **Primary text:** `#D4D4D4`
- **Accent / connected:** `#4EC9B0` (teal — status connected, addresses)
- **Warning:** `#FFD700` (yellow — partial results, non-critical errors)
- **Error:** `#F44747` (red — connection lost, critical failures)
- **Mono / hex:** `Consolas` or `Cascadia Code`, 11–12px

### 2.2 Typography

| Use | Font | Size | Color |
|-----|------|------|-------|
| Panel headers | Segoe UI Semibold | 13px | `#D4D4D4` |
| Data labels | Segoe UI | 12px | `#9CDCFE` (light blue) |
| Values / addresses | Consolas | 11px | `#4EC9B0` |
| Error text | Segoe UI | 12px | `#F44747` |
| Dimmed / inherited | Segoe UI | 11px | `#808080` |

---

## 3. Main Window Layout

```
┌──────────────────────────────────────────────────────────────────┐
│  [Connect] [Disconnect]  ●Connected  MyGame-Win64  UE5.4  v1.0  │  ← Toolbar (40px)
├──────────────────┬───────────────────────────────────────────────┤
│                  │  [Tab: Class Structure] [Live Walker] [...]   │
│   Object Tree    │                                               │
│   (350px min)    │        Active Tab Content Panel               │
│                  │                                               │
│   [Search...]    │                                               │
│   [Filter]       │                                               │
│                  │                                               │
│   ▼ World         │                                               │
│     ▶ Actors      │                                               │
│     ▶ Components  │                                               │
│   ▶ Engine        │                                               │
│   ▶ /Game         │                                               │
│                  │                                               │
├──────────────────┴───────────────────────────────────────────────┤
│  Status bar: Last command | Object count | Log path | Version    │  ← (24px)
└──────────────────────────────────────────────────────────────────┘
```

**Splitter:** `GridSplitter` between tree (col 0) and tabs (col 2), draggable, min-width 200px each.

### 3.1 Toolbar Items

| Element | Behavior |
|---------|---------|
| `Connect` button | Opens pipe, disabled while connected |
| `Disconnect` button | Closes pipe gracefully, disabled while disconnected |
| Status dot + text | `●Connected` (teal) / `●Disconnected` (red) / `○Connecting...` (yellow) |
| Game + version badge | `MyGame-Win64-Shipping  UE5.4` — populated after `init` response |
| Version label | `v1.0.123` (DLL build number) — right-aligned |

### 3.2 Status Bar

Left-to-right:
1. Last executed command + result summary (e.g. `walk_class → 47 fields`)
2. Object count (e.g. `58,432 objects`)
3. Log path shortcut (click to open log folder in Explorer)
4. DLL + UI version strings

---

## 4. Object Tree Panel

### 4.1 Layout

```
┌──────────────────────────────────┐
│ [🔍 Search...         ] [Clear]  │
│ [Filter by class: ______] [x]    │
│ [Load All] [Load World]          │
├──────────────────────────────────┤
│ ▼ /Game/Maps/ThirdPersonMap      │  ← grouped by outer path
│   ▼ BP_Player_C_0         [→]   │  ← [→] = open in Live Walker
│     Class: BP_Player_C          │
│   ▶ BP_Enemy_C_0           [→]  │
│ ▶ Engine                         │
│ ▶ /Script/CoreUObject            │
│                                  │
│ [Load more...] (if paginated)    │
└──────────────────────────────────┘
```

### 4.2 Tree Node Display

Each node shows:
- Object name (bold if selected)
- Class name (dimmed, `#808080`, italic)
- `[→]` action icon on hover → opens in Live Walker or ClassStruct

### 4.3 Interactions

| Action | Result |
|--------|--------|
| Click node | Select; populate ClassStruct tab with schema |
| Double-click node | Open Live Walker for that instance |
| Click `[→]` | Open Live Walker |
| Type in Search | Real-time filter (300ms debounce), substring match on name + class |
| Right-click node | Context menu: Copy Address, Copy Name, Find Instances, Open Class Schema |
| `Ctrl+F` | Focus search box |

### 4.4 Pagination

Tree loads 200 objects per page. "Load more..." button at bottom advances offset. "Load All" triggers background progressive load with progress bar at bottom of tree.

---

## 5. Class Structure Panel

### 5.1 Layout

```
┌──────────────────────────────────────────────────────────┐
│ Class: BP_Player_C  (super: Character)        [Export ▼] │
│ Path: /Game/BP_Player.BP_Player_C                        │
│ Addr: 7FF123456000    Props size: 1024 bytes             │
├──────┬──────────────────────┬──────────┬───────┬─────────┤
│ Offs │ Name                 │ Type     │ Size  │ Flags   │
├──────┼──────────────────────┼──────────┼───────┼─────────┤
│ 0x2D0│ Health               │ Float    │  4    │ BlueprintReadWrite │
│ 0x2D4│ MaxHealth            │ Float    │  4    │         │
│ 0x2D8│ bIsDead              │ Bool     │  1    │         │
│ 0x2E0│ WeaponComponent      │ Object   │  8    │ Export  │
│ 0x2E8│ Inventory            │ Array<Object> │ 16 │       │
└──────┴──────────────────────┴──────────┴───────┴─────────┘
│ (inherited from Character — 12 fields)  [Show Inherited ▼]│
└──────────────────────────────────────────────────────────┘
```

### 5.2 Export Dropdown

`[Export ▼]` button opens dropdown:
- Copy CE XML (clipboard)
- Copy CSX (CE Structure Dissect)
- Save SDK Header (.h)
- Export USMAP
- Export Symbols (x64dbg / IDA / Ghidra)

### 5.3 Field Row Click

Clicking a field row:
- Highlights row
- Shows field detail in a right side-panel or tooltip:
  - Full type info (inner type for arrays, struct type, enum values)
  - Property flags decoded (BlueprintReadWrite, SaveGame, etc.)
  - FField address (for cross-reference)

---

## 6. Live Walker Panel

### 6.1 Layout

```
┌──────────────────────────────────────────────────────────┐
│ Instance: BP_Player_C_0   Addr: 7FF6AA000000   [Refresh] │
│ Class: BP_Player_C        Outer: ThirdPersonMap  [→ Tree] │
├──────────────────────────────────────────────────────────┤
│ [🔍 Filter fields...]                    [Auto-refresh ☐]│
├────────┬────────────────────┬───────────────────┬────────┤
│ Offset │ Name               │ Value             │ Type   │
├────────┼────────────────────┼───────────────────┼────────┤
│ 0x2D0  │ Health             │ 100.0             │ Float  │
│ 0x2D4  │ MaxHealth          │ 200.0             │ Float  │
│ 0x2D8  │ bIsDead            │ false             │ Bool   │
│ 0x2E0  │ WeaponComponent →  │ 7FF2... BP_Weapon │ Object │  ← [→] drills in
│ 0x2E8  │ Inventory [5]      │ ▶ expand          │ Array  │  ← collapsible
│ 0x300  │ MovementMode       │ 2 (Walking)       │ Enum   │
└────────┴────────────────────┴───────────────────┴────────┘
│ [Walk Object…] [Invoke Function…]   [Copy CE XML]        │
└──────────────────────────────────────────────────────────┘
```

### 6.2 Field Display Rules

| Type | Value Column Display |
|------|---------------------|
| Float / Double | Decimal `100.000000` + hex `3F800000` on hover |
| Int / Int64 | Decimal signed + hex on hover |
| Bool | `true` / `false` (color coded: teal / grey) |
| Object pointer | Hex addr + `PtrName (PtrClass)` in grey; `→` icon if non-null |
| Enum | `42 (EnumClass::Value)` |
| String | Quoted `"Hero_01"` |
| Array | `[N elements]` with collapse/expand; scalar elements inline |
| Struct | Preview: `X=1.0, Y=2.0` (first 2 fields); expand for all |
| Null pointer | `0x0 (null)` in dimmed grey |

### 6.3 Drill-Down (→ icon)

Clicking `→` on an ObjectProperty:
1. Resolves pointer → reads `ptr_class` from live data
2. Pushes current instance onto breadcrumb stack (top of panel)
3. Loads new walk_instance for pointed-to object
4. Breadcrumb: `BP_Player_C_0 > WeaponComponent` — click to navigate back

**Known limitation:** If pointed-to object is a `ScriptStruct` type definition (not an instance), Live Walker shows empty fields (struct definitions have no instance data). Workaround: use "Walk Class" action instead.

### 6.4 Auto-refresh

When `Auto-refresh` checkbox is enabled:
- Timer fires every 500ms
- Calls `walk_instance` again
- Updates changed values in place (highlight row briefly in yellow on change)
- Paused automatically when another tab is active

### 6.5 InvokeParamDialog (UFunction Invoke)

Opened from `[Invoke Function…]` → lists all UFunctions from `walk_functions`:

```
┌─────────────────────────────────────────────────┐
│  Invoke Function: SetAttribute                   │
│  Params size: 8 bytes                            │
├──────────────────────────────────────────────────┤
│  ⚡ NewValue  (GameplayAttributeData)            │
│    BaseValue   [_______100.0_______] Float       │
│    CurrentValue[_______100.0_______] Float       │
│                                      ← ⚡ = dynamic layout│
├──────────────────────────────────────────────────┤
│  [Build Script] [Execute] [Cancel]               │
│  Result hex: 3F800000 3F800000                   │
│  ✓ ProcessEvent OK                               │
└─────────────────────────────────────────────────┘
```

- ⚡ prefix = dynamic struct layout from `struct_fields` (DLL-walked)
- No prefix = hardcoded `KnownStructLayouts`
- `[Build Script]` = generates CE Lua invoke script for clipboard
- `[Execute]` = calls `invoke_function` pipe command

---

## 7. Instance Finder Panel

### 7.1 Layout

```
┌────────────────────────────────────────────────┐
│ Class name: [BP_Player_C          ] [Find All]  │
│ Limit: [100 ▼]                                  │
├─────────────────────────────────────────────────┤
│ Found 2 instances of BP_Player_C               │
├──────────────────┬──────────────────────────────┤
│ Address          │ Name              │ Outer     │
├──────────────────┼───────────────────┼───────────┤
│ 7FF6AA000000 [→] │ BP_Player_C_0    │ ThirdPersonMap│
│ 7FF6AB000000 [→] │ BP_Player_C_1    │ ThirdPersonMap│
└──────────────────┴───────────────────┴───────────┘
│ [Copy All Addresses]                            │
└────────────────────────────────────────────────┘
```

- `[→]` opens Live Walker for that instance
- Class name field supports partial match (DLL does substring scan on class name)
- `Copy All Addresses` copies all found addresses as newline-delimited hex

---

## 8. Pointer Panel (Global Pointers)

### 8.1 Layout

```
┌─────────────────────────────────────────────────────────┐
│ Global Pointers                         [Rescan] [Apply] │
├──────────────────┬──────────────────────────────────────┤
│ GObjects         │ 7FF600A12340  (method: aob)          │
│ GNames           │ 7FF600B56780  (method: string_ref)   │
│ GWorld           │ 0x0           (method: not_found) ⚠  │
│ Object Count     │ 58,432                                │
│ Module           │ MyGame-Win64-Shipping.exe             │
│ Module Base      │ 7FF600000000                          │
│ UE Version       │ 5.4 (504)                             │
├──────────────────┴──────────────────────────────────────┤
│ Scan Stats                                               │
│  GObjects: 40 patterns tried, 3 hits                    │
│  GNames:   27 patterns tried, 0 hits (string fallback)  │
│  GWorld:   37 patterns tried, 0 hits                    │
├─────────────────────────────────────────────────────────┤
│ Dynamic Offsets                                          │
│  FField::Name:     +0x28   FField::Next:   +0x20       │
│  FProperty::Offset:+0x4C   UStruct::Props: +0x58       │
│  CasePreservingName: false                               │
└─────────────────────────────────────────────────────────┘
```

- `[Rescan]` triggers background rescan; progress bar replaces button during scan
- `[Apply]` activates rescanned results (only enabled after successful rescan)
- GWorld = 0 shown with ⚠ warning (non-critical — GWorld fallback via GObjects scan)
- All addresses are clickable: copies to clipboard

---

## 9. Property Search Panel

### 9.1 Layout

```
┌──────────────────────────────────────────────────┐
│ Property name: [Health              ] [Search]   │
│ Limit: [100 ▼]                                   │
├──────────────────────────────────────────────────┤
│ Found 47 classes with property "Health"          │
├────────────────────┬──────────────┬──────────────┤
│ Class              │ Offset       │ Type         │
├────────────────────┼──────────────┼──────────────┤
│ BP_Player_C   [→]  │ 0x2D0        │ Float        │
│ BP_Enemy_C    [→]  │ 0x1C8        │ Float        │
│ BP_Vehicle_C  [→]  │ 0x3A0        │ Float        │
└────────────────────┴──────────────┴──────────────┘
```

- `[→]` opens Class Structure panel for that class
- Search is substring match across all classes in object array

---

## 10. Proxy Deploy Panel

### 10.1 Layout

```
┌──────────────────────────────────────────────────────────────────┐
│ Proxy DLL Deployment                  [Detect Steam Games]       │
├──────────────────────┬───────────┬────────────┬──────────────────┤
│ Game                 │ Status    │ Version    │ Path             │
├──────────────────────┼───────────┼────────────┼──────────────────┤
│ ☑  FF7 Rebirth       │ ✓ Deployed│ v1.0.123   │ C:\Steam\...    │
│ ☑  Satisfactory      │ Not deployed│ —         │ C:\Steam\...   │
│ ☐  Hogwarts Legacy   │ ⚠ Other proxy│ Unknown  │ C:\Steam\...   │
│ ☑  Manor Lords       │ ✓ Deployed│ v1.0.120   │ C:\Steam\...   │
└──────────────────────┴───────────┴────────────┴──────────────────┘
│ [Deploy Selected]  [Undeploy Selected]  [Update All]             │
│ ☐ Force Overwrite (skip OtherProxy check)                        │
└──────────────────────────────────────────────────────────────────┘
```

### 10.2 Status Colors

| Status | Color | Meaning |
|--------|-------|---------|
| `✓ Deployed` | Teal | Our version.dll is deployed |
| `Not deployed` | Grey | No version.dll present |
| `⚠ Other proxy` | Yellow | Another program's version.dll — actions blocked |
| `✗ Error` | Red | File access error, permission denied |
| `↑ Update available` | Blue | Deployed but older build number |

### 10.3 Behavior Rules

- "Other proxy" games are unchecked and disabled by default
- `Force Overwrite` checkbox bypasses the OtherProxy block (warns user first)
- Deploy/Undeploy are batch operations over selected rows only
- Panel works without pipe connection (operates directly on filesystem)

---

## 11. Game Class Filter Panel

```
┌───────────────────────────────────────────────┐
│ Show only game classes in Object Tree         │
│ Filter prefix: [/Game/  ] [Apply] [Clear]     │
├───────────────────────────────────────────────┤
│ Example patterns:                             │
│   /Game/       ← game content                │
│   /Script/     ← scripted classes            │
│   BP_          ← blueprints                  │
│   /Game/Maps/  ← per-level                   │
└───────────────────────────────────────────────┘
```

Filters Object Tree to show only nodes whose full path starts with the given prefix. Updates tree immediately on Apply.

---

## 12. User Workflows

### 12.1 Standard Reverse Engineering Session

```
1. Open game
2. Open UE5CEDumper.CT in CE → DLL injected automatically
3. Launch UE5DumpUI.exe → click Connect
4. Object Tree loads with progress bar
5. Search for target class (e.g. "Player")
6. Double-click instance → Live Walker opens
7. Identify field (e.g. Health at 0x2D0)
8. Click "Copy CE XML" → paste into CE address list
9. Optionally: Export CSX for CE Structure Dissect
```

### 12.2 Finding a Unknown Offset

```
1. Property Search tab → type field name (e.g. "Stamina")
2. See all classes containing "Stamina" + their offsets
3. Click [→] on relevant class → view full schema in Class Structure
4. Click field → see full type details
```

### 12.3 Proxy DLL Deployment (persistent injection)

```
1. Open Proxy Deploy tab
2. Click "Detect Steam Games" → auto-finds UE games
3. Select games → click "Deploy Selected"
4. Launch games → DLL loads automatically without CE
5. Connect UI normally
```

### 12.4 UFunction Invocation

```
1. Live Walker: navigate to UObject instance
2. Click "Invoke Function…"
3. Select function from list
4. Fill in parameter values (struct fields expanded)
5. Click "Execute" → ProcessEvent called on game thread
6. View return values in result hex display
   OR
7. Click "Build Script" → copy CE Lua script for manual invocation
```

---

## 13. Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+F` | Focus Object Tree search |
| `F5` | Refresh current panel |
| `Ctrl+C` | Copy selected address |
| `Ctrl+E` | Export (CE XML) |
| `Enter` (on tree node) | Open Live Walker |
| `Backspace` | Navigate back in drill-down breadcrumb |
| `Escape` | Close dialog / clear filter |

---

## 14. Empty States & Error States

| State | Display |
|-------|---------|
| Not connected | Tree shows "Connect to game first" placeholder |
| GWorld not found | World Walk tab shows "GWorld not found — fallback via GObjects" warning |
| walk_instance empty | "No fields found — class may use UProperty mode" + DynOff debug info |
| Drill-down on ScriptStruct definition | "This is a type definition — use Walk Class instead" hint |
| Rescan in progress | Button replaced by progress bar + animated spinner |
| Export with 0 fields | "Nothing to export — walk class first" |
| ProxyDeploy no games found | "No UE games detected. Check Steam path in settings." |

---

## 15. String Externalization

All user-visible strings are in `Resources/Strings/en.axaml` and referenced via `StaticResource`:

```xml
<!-- en.axaml -->
<x:String x:Key="LiveWalker.DrillDownHint">Click → to drill into pointer</x:String>
<x:String x:Key="ProxyDeploy.OtherProxyWarning">Another proxy DLL detected — enable Force Overwrite to replace</x:String>

<!-- Usage in AXAML -->
<TextBlock Text="{StaticResource LiveWalker.DrillDownHint}" />
```

No hardcoded UI strings in `.cs` or `.axaml` view files. Code-behind strings (logs, exceptions) are English only and do not use the resource file.
