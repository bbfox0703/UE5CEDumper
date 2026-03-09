# AOBMaker CE Plugin Integration

> UE5DumpUI communicates with the [AOBMaker](https://github.com/bbfox0703/AOBMaker) Cheat Engine Plugin via a dedicated named pipe to provide seamless CE navigation, script injection, and symbol registration.

---

## Architecture Overview

```
UE5DumpUI (C# Avalonia)                AOBMaker CE Plugin (Lua)
┌──────────────────────┐                ┌─────────────────────┐
│  AobMakerBridgeService│──── pipe ────▶│  Named Pipe Server   │
│  (IAobMakerBridge)   │◀─── pipe ─────│  (Lua JSON handler)  │
│                      │                │                      │
│  PointerPanelVM      │                │  Memory Viewer nav   │
│   - HEX buttons      │                │  Disassembler nav    │
│   - ASM buttons      │                │  AA Script creation  │
│   - SYM registration │                │  Symbol registration │
│                      │                │                      │
│  LiveWalkerVM        │                │                      │
│   - Field HEX nav    │                │                      │
│   - Ptr HEX nav      │                │                      │
│   - Object HEX nav   │                │                      │
│   - Invoke scripts   │                │                      │
└──────────────────────┘                └─────────────────────┘
       \\.\pipe\AOBMakerCEBridge
```

---

## Wire Protocol

| Item | Value |
|------|-------|
| Pipe name | `\\.\pipe\AOBMakerCEBridge` |
| Direction | Duplex (InOut) — request/response |
| Connection model | **Per-request reconnect** — CE Plugin disconnects after each response |
| Framing | 4-byte LE `uint32` length prefix + UTF-8 JSON payload |
| Connect timeout | 2000 ms |
| Response timeout | 5000 ms |
| Max message size | 10 MB |
| JSON encoder | `UnsafeRelaxedJsonEscaping` for requests (CE Lua parser cannot handle `\uXXXX` escapes) |

### Message Format

Every message (request and response) uses the same `AobMakerMessage` model:

```json
{
  "type": "NavigateHexView",
  "address": "7FF769E29110",
  "success": true,
  "message": "OK"
}
```

Common fields:

| Field | Type | Direction | Description |
|-------|------|-----------|-------------|
| `type` | string | request | Message type identifier |
| `address` | string? | request | Hex address without `0x` prefix |
| `success` | bool | response | Whether the operation succeeded |
| `message` | string? | response | Error detail on failure |

---

## Message Types

### 1. `NavigateHexView`

Navigate CE Memory Viewer hex dump (bottom pane) to a specific address.

```json
// Request
{ "type": "NavigateHexView", "address": "2DA53B24970" }

// Response
{ "type": "NavigateHexView", "success": true }
```

**Used by:**
- PointerPanel: HEX buttons for GObjects / GNames / GWorld data addresses
- LiveWalker: Field address HEX, pointer target HEX, object address HEX

### 2. `NavigateDisassembler`

Navigate CE Memory Viewer disassembler (top pane) to a specific code address.

```json
// Request
{ "type": "NavigateDisassembler", "address": "7FF7F3456789" }

// Response
{ "type": "NavigateDisassembler", "success": true }
```

**Used by:**
- PointerPanel: ASM buttons for GObjects / GNames / GWorld AOB scan hit addresses (the instruction that references the global pointer)

### 3. `CreateAAScript`

Create an Auto Assembler script entry in CE's address list.

```json
// Request
{
  "type": "CreateAAScript",
  "description": "Invoke: BP_SantiagoGameInstance_C::GetSkillManager",
  "script": "[ENABLE]\n...\n[DISABLE]\n...",
  "autoActivate": false
}

// Response
{ "type": "CreateAAScript", "success": true }
```

| Field | Type | Description |
|-------|------|-------------|
| `description` | string | Display name in CE address list |
| `script` | string | Full AA script content (`[ENABLE]`/`[DISABLE]` sections) |
| `autoActivate` | bool | Whether to activate immediately after creation |

**Used by:**
- LiveWalker: `GenerateInvokeScriptAsync` sends UFunction invoke scripts directly to CE (falls back to clipboard if AOBMaker unavailable)

### 4. `CreateSymbolScript`

Create an AOB-scan-based symbol registration AA script. The CE Plugin's `BuildSymbolScanScript()` generates the full AA script from these parameters.

```json
// Request
{
  "type": "CreateSymbolScript",
  "name": "GWorld → gworld_addr",
  "aob": "48 8B 1D ?? ?? ?? ??",
  "pos": 3,
  "aoblen": 7,
  "symbol": "gworld_addr",
  "module": "DQIandIIHD2DRemake-Win64-Shipping.exe",
  "autoActivate": true
}

// Response
{ "type": "CreateSymbolScript", "success": true }
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Display name in CE address list |
| `aob` | string | AOB pattern (e.g. `"48 8B 1D ?? ?? ?? ??"`) |
| `pos` | int | Displacement offset within AOB match (instrOffset + opcodeLen) |
| `aoblen` | int | Instruction end relative to AOB match (instrOffset + totalLen) |
| `symbol` | string | CE symbol name to register (e.g. `"gworld_addr"`) |
| `module` | string | Module name for `AOBScanModule` |
| `autoActivate` | bool | Whether to activate immediately |

The generated script performs: `AOBScanModule` → read RIP-relative displacement at `pos` → calculate `match + pos + 4 + [displacement]` → register as CE symbol. Survives game restarts (re-scans on script enable).

**Used by:**
- PointerPanel: SYM button registers GWorld pointer as persistent CE symbol

---

## Detection & Lifecycle

### Startup Detection

```
App.axaml.cs
  └─ new AobMakerBridgeService(logging)
       └─ Injected into MainWindowViewModel
            ├─ PointerPanelViewModel(aobMaker: ...)
            └─ LiveWalkerViewModel(aobMaker: ...)
```

On first connection (after pipe connects and engine state loads), both ViewModels call `CheckAobMakerAsync()`:

```csharp
// AobMakerBridgeService.CheckAvailabilityAsync():
// 1. Attempt pipe connect to \\.\pipe\AOBMakerCEBridge (2s timeout)
// 2. If connects → IsAvailable = true, close pipe
// 3. If fails → IsAvailable = false
```

### Tab-Switch Re-detection

Every time the user switches tabs in the main window:

```csharp
// MainWindowViewModel.OnSelectedTabIndexChanged()
case 0: _ = LiveWalker.CheckAobMakerAsync();   // Live Walker tab
case 5: _ = Pointers.CheckAobMakerAsync();      // Pointers tab
```

This detects:
- **CE opened after UI started** → buttons enable
- **CE closed while UI running** → buttons disable

### Navigation Cooldown

LiveWalkerViewModel uses a **5-second cooldown** to avoid spamming pipe connects during rapid field navigation (each connect attempt takes up to 2s when CE is not running):

```csharp
private DateTime _lastAobMakerCheck = DateTime.MinValue;
private static readonly TimeSpan AobMakerCheckCooldown = TimeSpan.FromSeconds(5);

private void TryCheckAobMaker()
{
    if (_aobMaker == null) return;
    if (DateTime.UtcNow - _lastAobMakerCheck < AobMakerCheckCooldown) return;
    _ = CheckAobMakerAsync();  // fire-and-forget
}
```

Called on every drilldown (`ClickFieldAsync`), breadcrumb navigation, and back navigation.

### Connection Recovery

Each `NavigateHexViewAsync` / `NavigateDisassemblerAsync` / `CreateAAScriptAsync` / `CreateSymbolScriptAsync` call:

1. **Reconnects** fresh (CE Plugin disconnects after each request)
2. On success → `IsAvailable = true`
3. On pipe connect failure → `IsAvailable = false` (buttons disable)
4. On response failure → logs warning, returns false (but keeps `IsAvailable`)
5. On timeout → logs warning, returns false

---

## UI Integration Points

### PointerPanel Buttons

| Button | AOBMaker Call | Address Source | Condition |
|--------|--------------|----------------|-----------|
| GObjects **HEX** | `NavigateHexView` | `GObjectsAddress` | `IsAobMakerAvailable && addr != 0` |
| GNames **HEX** | `NavigateHexView` | `GNamesAddress` | `IsAobMakerAvailable && addr != 0` |
| GWorld **HEX** | `NavigateHexView` | `GWorldAddress` | `IsAobMakerAvailable && addr != 0` |
| GObjects **ASM** | `NavigateDisassembler` | `GObjectsScanAddr` | `IsAobMakerAvailable && addr != 0` |
| GNames **ASM** | `NavigateDisassembler` | `GNamesScanAddr` | `IsAobMakerAvailable && addr != 0` |
| GWorld **ASM** | `NavigateDisassembler` | `GWorldScanAddr` | `IsAobMakerAvailable && addr != 0` |
| GWorld **SYM** | `CreateSymbolScript` | `GWorldAob` + metadata | `IsAobMakerAvailable && addr != 0 && AOB != ""` |

- **HEX** = data address → CE hex dump
- **ASM** = code address (AOB scan hit) → CE disassembler
- **SYM** = create persistent AOB-scan CE symbol

### LiveWalker Buttons

| Button | AOBMaker Call | Address Source |
|--------|--------------|----------------|
| Field **HEX** | `NavigateHexView` | `field.FieldAddress` (base + offset) |
| Ptr **HEX** | `NavigateHexView` | `field.PtrAddress` (dereferenced pointer target) |
| Object **HEX** | `NavigateHexView` | `CurrentAddress` (current object base) |

### Invoke Script Delivery

When generating UFunction invoke scripts via `GenerateInvokeScriptAsync`:

```
1. Generate AA script via InvokeScriptGenerator.Generate()
2. If AOBMaker available:
   └─ CreateAAScriptAsync(description, script, autoActivate: false)
   └─ On success → status: "Invoke script created in CE"
3. Fallback (AOBMaker unavailable or failed):
   └─ Copy script to clipboard
   └─ Status: "Invoke script copied to clipboard"
```

---

## Data Flow: AOB Metadata

The AOB scan metadata flows from DLL → pipe → UI → AOBMaker:

```
DLL (OffsetFinder.cpp)
  │  Scans for GObjects/GNames/GWorld using AOB patterns
  │  Records: winning pattern ID, scan hit address, AOB string, pos, aoblen
  ▼
PipeServer.cpp (get_pointers / scan_status)
  │  Serializes to JSON response:
  │  {
  │    "gobjects_scan_addr": "0x7FF7F3493983",
  │    "gworld_aob": "48 8B 1D ?? ?? ?? ??",
  │    "gworld_aob_pos": 3,
  │    "gworld_aob_len": 7,
  │    "module_name": "Game-Win64-Shipping.exe",
  │    ...
  │  }
  ▼
DumpService.cs (GetPointersAsync / TriggerScanAsync)
  │  Parses into EngineState model
  ▼
PointerPanelViewModel / LiveWalkerViewModel
  │  Binds to UI buttons, calls IAobMakerBridge methods
  ▼
AobMakerBridgeService
  │  Sends length-prefixed JSON to \\.\pipe\AOBMakerCEBridge
  ▼
AOBMaker CE Plugin
  └─ Executes CE navigation / creates scripts
```

---

## Key Source Files

| File | Role |
|------|------|
| `ui/UE5DumpUI/Core/IAobMakerBridge.cs` | Interface — 5 methods |
| `ui/UE5DumpUI/Services/AobMakerBridgeService.cs` | Implementation — pipe client, per-request reconnect |
| `ui/UE5DumpUI/Models/AobMakerMessage.cs` | Wire model + AOT-safe `JsonSerializerContext` |
| `ui/UE5DumpUI/Models/EngineState.cs` | AOB metadata (scan addr, pattern, pos, aoblen) |
| `ui/UE5DumpUI/ViewModels/PointerPanelViewModel.cs` | HEX/ASM/SYM buttons |
| `ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs` | Field/Ptr/Object HEX + invoke script delivery |
| `ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs` | Tab-switch re-detection wiring |
| `ui/UE5DumpUI/App.axaml.cs` | Service creation + DI |

---

## JSON Encoding Note

The CE Plugin's Lua-side JSON parser does not handle `\uXXXX` Unicode escape sequences. For example, a single quote `'` serialized as `\u0027` would break AA script parsing.

Solution: `AobMakerJsonContext.Relaxed` uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` to emit literal characters instead of escape sequences:

```csharp
public static AobMakerJsonContext Relaxed => _relaxed ??= new(new JsonSerializerOptions
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
});
```

This is only used for **outgoing requests** (which contain script content). Incoming responses use the default context.
