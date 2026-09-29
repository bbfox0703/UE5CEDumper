# AOBMaker CE Plugin Integration

> UE5DumpUI communicates with the [AOBMaker](https://github.com/bbfox0703/AOBMaker) Cheat Engine Plugin via a dedicated named pipe to provide seamless CE navigation, script injection, and symbol registration.

---

## Architecture Overview

```
UE5DumpUI (C# Avalonia)                AOBMaker CE Plugin (C++ DLL)
┌──────────────────────┐                ┌─────────────────────┐
│  AobMakerBridgeService│──── pipe ────▶│  Named Pipe Server   │
│  (IAobMakerBridge)   │◀─── pipe ─────│  (C++ JSON parser)   │
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

The plugin is a C++ DLL. It parses JSON in C++ (`json_parse.h`), and its handlers run CE Lua on CE's main thread
through `synchronize`. AOBMaker's answer to our requests, and the rules a client must follow today, are its
`docs/UE5CEDumper-Requests-Reply.md` (`9431370`); ours are [aobmaker-requests.md](aobmaker-requests.md).

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
| Max message size | 10 MiB (`10 * 1024 * 1024`). An oversize request gets **no reply**, on either pipe (AOBMaker `EXT-5`) |
| JSON encoder | `UnsafeRelaxedJsonEscaping` for requests. Not required: see "JSON Encoding Note" |

### Message Format

Every message (request and response) uses the same `AobMakerMessage` model. For the commands below, a reply's
`type` is the command's name with `Result` appended, except that `GetAttachedProcess` answers
`AttachedProcessResult`. An unknown type gets
`{"type":"Error","success":false,"message":"Unknown type"}` (AOBMaker `API-CEPlugin.md:23-25`,
`plugins/CEPlugin/src/protocol.h:44-58`).

```json
// Request
{ "type": "NavigateHexView", "address": "7FF769E29110" }

// Response
{ "type": "NavigateHexViewResult", "success": true }
```

Common fields:

| Field | Type | Direction | Description |
|-------|------|-----------|-------------|
| `type` | string | both | Message type identifier (`<Command>Result` on a reply) |
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
{ "type": "NavigateHexViewResult", "success": true }
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
{ "type": "NavigateDisassemblerResult", "success": true }
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
  "autoActivate": false,
  "group": "UE5CEDumper (DLL)"
}

// Response
{ "type": "CreateAAScriptResult", "success": true }
```

| Field | Type | Description |
|-------|------|-------------|
| `description` | string | Display name in CE address list |
| `script` | string | Full AA script content (`[ENABLE]`/`[DISABLE]` sections) |
| `autoActivate` | bool | Whether to activate immediately after creation. ⚠ `success` then means "created", not "enabled": the plugin never reads `Active` back (AOBMaker `EXT-4`, open; its reply rule 8). A `CreateSymbolScript` whose AOB scan fails still ends ticked, because its template only prints `[SymbolScanner] WARNING: AOB scan failed`. |
| `group` | string | *(optional)* Target group description. Non-empty → the record goes **under the first record, at any depth, whose description matches and that is not an AA script** (Type 11). That can be a value record or an `IsAddressGroupHeader`, and the plugin's parent self-check passes even on such a wrong match. Only when none matches is a new plain `IsGroupHeader` folder created, at the root (`pipe_server.cpp:1201-1217` at AOBMaker `9431370`; its `K7-13` and reply rule 11). AOBMaker's own `API-CEPlugin.md:142` and `:191` still say "single-level". Our Teleport, trainer and Dissect pushes use `group`, so keep "UE5CEDumper (DLL)" and "UE5CEDumper (no-DLL trainer)" unique across the whole table. Empty/omitted → address-list **root** (back-compatible). **Requires an AOBMaker CE plugin whose `CreateAAScript` handler reads `group`** — older builds ignore the field and land the record at root. |

**Used by:**
- LiveWalker: `GenerateInvokeScriptAsync` sends UFunction invoke scripts directly to CE (falls back to clipboard if AOBMaker unavailable)
- Teleport tab: **Add records to CE** nests the DLL-mailbox Teleport / Movement / Fly scripts under `group` = `"UE5CEDumper (DLL)"`, and **Export standalone trainer** nests the no-DLL trainer entries under `"UE5CEDumper (no-DLL trainer)"` — so the many pushed scripts collapse into two folders (pure-Lua vs DLL-call), not littering the address-list root.
- Instance Finder: **AA** (register a symbol at the instance) pushes like Live Walker's AA and falls back to the clipboard (`[AOBM-INSTFINDER-AA]`, `AobMakerActions.PushAaScriptOrCopyAsync`).
- Tools menu **Add Auto Structure Dissect to Current CE Table**: the unticked record that switches `ue5_dissect.lua`'s auto mode, under `"UE5CEDumper (DLL)"` (`[AOBM-DISSECT-INJECT]`, `DissectScriptGenerator`).

### 4. `InjectTableFile`

Embed an arbitrary text/Lua file straight into the currently open Cheat Engine table. Used by the **Tools -> Inject Helper into Current CE Table** menu so the user no longer has to save the helper to disk and `Table -> Add File...` it manually.

- **Replacing a file.** The plugin stages the new file as `<fileName>.aobmaker-tmpN`, fills it, verifies its size and
  renames it into place. It deletes an existing file of the same name only after that succeeds, so a failure leaves
  the old file untouched. A failed rename or a crash can leave a `.aobmaker-tmpN` orphan in the table; deleting it is
  safe (AOBMaker `aa846c0`, `API-CEPlugin.md:231-241`).
- **Re-injecting does not re-run code.** CE has one Lua state, so globals the first load defined stay alive, and an
  `if not X then … end` guard keeps the old code (`API-CEPlugin.md:248-268`). Our invoke and freeze helpers gate
  their definitions on a version number for exactly this (`ue5_invoke_helper.lua`, `ue5_freeze_helper.lua`
  `THIS_HELPER_VERSION`).

```json
// Request
{
  "type": "InjectTableFile",
  "fileName": "ue5_invoke_helper.lua",
  "content": "if not invokeUFunction then\n  function invokeUFunction(...)\n    ...\n  end\nend\n"
}

// Response
{ "type": "InjectTableFileResult", "success": true }
```

| Field | Type | Description |
|-------|------|-------------|
| `fileName` | string | Filename to register inside the .CT (case-sensitive — AA scripts use the same name in `findTableFile`) |
| `content` | string | Raw UTF-8 file content. The CE Plugin chooses a long-bracket level dynamically so any payload is safe |

The plugin handler runs `synchronize(function() ... end)` so all CE Lua APIs (`findTableFile`, `createTableFile`, `Stream.write`) execute on CE's main thread. Self-verifies via `f.Stream.Size == #content` before returning success. Bridge timeout is **15 s** (vs. the 5 s navigation default) to give the synchronize round-trip headroom for larger payloads.

**Used by:**
- Tools menu **Inject Helper into Current CE Table** — UE5DumpUI ships the embedded `ue5_invoke_helper.lua` straight into the user's open .CT. Falls back to "use Export to disk + Add File..." if AOBMaker plugin isn't loaded.
- Tools menu **Inject Freeze Helper into Current CE Table** — the same for `ue5_freeze_helper.lua`.
- Tools menu **Add Auto Structure Dissect to Current CE Table** — embeds `ue5_dissect.lua`, then adds its enable record with `CreateAAScript`. No clipboard fallback: the record is useless without the file (`[AOBM-DISSECT-INJECT]`).

### 5. `CreateSymbolScript`

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
{ "type": "CreateSymbolScriptResult", "success": true }
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Display name in CE address list |
| `aob` | string | AOB pattern (e.g. `"48 8B 1D ?? ?? ?? ??"`) |
| `pos` | int | Displacement offset within AOB match (instrOffset + opcodeLen) |
| `aoblen` | int | Instruction end relative to AOB match (instrOffset + totalLen) |
| `symbol` | string | CE symbol name to register (e.g. `"gworld_addr"`) |
| `module` | string | Module name for `AOBScanModule` |
| `autoActivate` | bool | Whether to activate immediately. `success` means "created", not "enabled" (see `CreateAAScript`) |

The generated script performs: `AOBScanModule` → read the signed 32-bit displacement at `match + pos` → calculate `final = [match + pos] + match + aoblen` → register as CE symbol (`pipe_server.cpp:1449-1450` at AOBMaker `9431370`; `API-CEPlugin.md:153`). That equals `match + pos + 4 + [displacement]` only when no immediate follows the disp32. Survives game restarts (re-scans on script enable). A failed scan prints `[SymbolScanner] WARNING: AOB scan failed for <name>` and registers nothing, yet the record stays ticked.

**Used by:**
- PointerPanel: SYM buttons register GWorld and &GEngine from the DLL's own AOB triple.
- PointerPanel: SYM on GObjects and GNames, with an AOB from AOBMaker.UI's `GenerateAob` (see "AOBMaker.UI's own
  pipe" below).

### 6. `CreateMemoryRecord`

Add a single typed memory record straight into CE's address list (the plugin calls
`getAddressList().createMemoryRecord()`, sets Description / Address / Type / ShowAsSigned /
ShowAsHex, and self-verifies). One-click alternative to "copy the address, then build the
record by hand in CE" — e.g. so the user can immediately run CE's **Find out what accesses
this address** on a Live Walker field.

```json
// Request
{
  "type": "CreateMemoryRecord",
  "description": "Health",
  "address": "2DA53B24970",
  "valueType": 4,
  "isSigned": false,
  "showAsHex": false
}

// Response
{ "type": "CreateMemoryRecordResult", "success": true }
```

| Field | Type | Description |
|-------|------|-------------|
| `description` | string | Record label in CE's address list (typically the field Name) |
| `address` | string | Hex address without `0x`, or a registersymbol name |
| `valueType` | int | CE `TVariableType`: `0`=Byte, `1`=Word, `2`=Dword, `3`=Qword, `4`=Single, `5`=Double, `6`=String, `7`=UnicodeString, `8`=ByteArray, `9`=Binary |
| `isSigned` | bool | Display integer types as signed |
| `showAsHex` | bool | Display as hex. **Requires an AOBMaker CE plugin compiled on/after 2026-06-07** — older builds silently ignore it (default `false` is back-compatible). Ignored for string types (6/7). |

The wire model omits `valueType` from unrelated messages (it is a nullable `int?` so a Byte
record's `0` still serializes), and omits `isSigned` / `showAsHex` when `false`.

**Used by:**
- LiveWalker: per-row **+CE** buttons (field address + pointer target) — see UI Integration
  Points. Type/signed/hex are derived via `CeXmlExportService.MapFieldToCeRecordType`, reusing
  the exact UE→CE type mapping that drives Copy CE XML / Copy CE Field.
- LiveWalker: **+CE Field (flat)** toolbar button — flat batch form of the per-row +CE (loops
  `CreateMemoryRecord` over the multi-selection, one top-level record per field, early-bails
  if the pipe drops mid-batch). It does NOT reproduce the hierarchical pointer-chain layout —
  Copy CE XML / Copy CE Field stay clipboard-only for that.
- The **+CE** buttons of Value Search, Snapshot, SPC and Instance Finder, typed from the row's type name through
  `CeXmlExportService.MapTypeNameToCeRecordType` (an enum of unknown width goes as one byte). See "Other panels"
  under UI Integration Points.
- **ASM** on a function row (Live Walker, Interesting Functions, Live Funcs, the function-properties dialog and the
  xref dialog): a ByteArray record `"<function> (code)"` at the native entry, then `NavigateDisassembler`.

> **Minimum AOBMaker build:** the typed-record push works against any plugin that handles
> `CreateMemoryRecord`, but the `showAsHex` flag (pointer / 8-byte fields shown as hex)
> needs a plugin **compiled on/after 2026-06-07**. On older plugins the record is still
> created — it just displays in decimal.

### 7. `GetAttachedProcess`

Which process Cheat Engine has open. The plugin reads CE's `OpenedProcessID` and the image name.

```json
// Request
{ "type": "GetAttachedProcess" }

// Response
{ "type": "AttachedProcessResult", "success": true, "processId": 12345, "processName": "Game-Win64-Shipping.exe" }
```

| Field | Type | Description |
|-------|------|-------------|
| `processId` | int | CE's open process; `0` when it has none |
| `processName` | string | Image file name, or empty. Read through the ANSI API (`QueryFullProcessImageNameA`, `pipe_server.cpp:491-519` at AOBMaker `9431370`). A character the system code page cannot represent arrives as `?`. One it can represent (Big5 on code page 950) arrives as raw ANSI bytes: the plugin's `json.h` does not transcode them, so the JSON is not valid UTF-8, and our `Encoding.UTF8.GetString` (`AobMakerBridgeService.ReadMessageAsync`) garbles them: U+FFFD, or wrong characters where a Big5 pair happens to be a valid UTF-8 pair. Compare by id, never by name |

**Used by** (`[AOBM-ATTACH-CHECK]`, `AobMakerStatus.CheckAttachAsync`): the toolbar's **⚠ CE is not on this game**,
checked at connect and on ⟳, and the text of a +CE that CE refused. A warning only — a push into a CE with nothing
open is sometimes exactly right (the DLL bootstrap), so nothing is refused on its account. No reply, or no
`processId`, reads as "cannot tell" and says nothing.

---

## AOBMaker.UI's own pipe (`\\.\pipe\AOBMaker`)

A different server from everything above: **AOBMaker.UI**, the desktop app, not the Cheat Engine plugin. It exists
only while that app is open. UE5DumpUI uses one command on it, through `Services/AobMakerUiClient`.

| Item | Value |
|------|-------|
| Pipe name | `\\.\pipe\AOBMaker` (AOBMaker's default; a renamed pipe reads as "not running") |
| Framing | the same: 4-byte LE length + UTF-8 JSON, one request per connection |
| Caller check | same user, integrity at least the server's; otherwise a message starting `Rejected:` |
| Missing `success` | left out whenever it is false (`PipeMessage.Success` is `JsonIgnore(WhenWritingDefault)`), a `Rejected:` reply included. `Pong` never carries it: detect `Pong` by its `type`. Treat a missing `success` as false (AOBMaker `EXT-11`, reply rule 7) |
| No reply at all | an unknown `type`, malformed JSON, a length mismatch, a JSON `null` body, and an oversize request (`WindowsPipeServer.cs:283`, `:366-368`, `:784-788` at AOBMaker `9431370`; `EXT-11`, `EXT-5`) |
| Idle timeout | the server drops a connection that makes no I/O progress for 5 s, so send the request right after connecting (`API-AOBMaker-UI.md:17-25`) |
| One client at a time | a single pipe instance: a slow `GenerateAob` makes every other client wait (`GenerateAob-Pipe-Command.md:96`) |
| Deadlines | connect 2 s; reply 30 s (the uniqueness scan is handler work and can be slow) |

### `GenerateAob`

```json
// Request
{ "type": "GenerateAob", "address": "0x7FF6100000C", "processId": 12345 }

// Response (RIP-relative seed)
{ "type": "GenerateAobResult", "success": true, "aob": "48 89 5C 24 ?? 48 8B 05 ?? ?? ?? ??",
  "injectionOffset": 5, "matchCount": 1, "pos": 8, "aoblen": 12, "module": "Game-Win64-Shipping.exe",
  "message": "Unique AOB found in Game-Win64-Shipping.exe (+...)" }
```

AOBMaker decodes the instruction **at** `address` (the seed), then runs Mode D (`DynamicAobOptimizer`) to grow
context until the AOB is unique (mask Mode A, `MaxResults` 2; `API-AOBMaker-UI.md:69-73`,
`WindowsPipeServer.cs:554-562`). `pos` and `aoblen` are relative to the AOB's start, which lies `injectionOffset` bytes
before the seed, and are present only for a RIP-relative seed.

**Pass instruction addresses only.** `GenerateAob` does not check that the address is code: given a data address, it
decodes the data bytes as instructions and still returns a "unique AOB" (AOBMaker `EV-4`, reply rule 12). The SYM
buttons below always pass an instruction (`FindRipSeed`).

**Used by** the GObjects / GNames **SYM** buttons (`[AOBM-GNAMES-SYMBOL]`,
`PointerPanelViewModel.RegisterSymbolViaGenerateAobAsync`):

1. Read 64 bytes at the scan hit. It is where the DLL's PATTERN matched, and the RIP instruction can sit further
   in, so find the one whose `[rip+disp32]` lands exactly on the address the DLL resolved
   (`AobMakerActions.FindRipSeed`). None → refuse: the signature adjusts, dereferences or follows a call.
2. `GenerateAob` on that instruction.
3. Replay what CE's script will do — `disp = [aob + pos]`, `aob + aoblen + disp` — against game memory, and push
   `CreateSymbolScript` only when it lands on the DLL's address.

Every failure names its remedy: AOBMaker.UI not running, refused (elevation), no reply (an older build without
`GenerateAob`), no unique AOB (AOBMaker's own words), not RIP-relative, replay mismatch.

### `Ping` (not used yet)

The probe for an AOBMaker.UI status light. `{"type":"Ping"}` → `{"type":"Pong","version":"2.0"}`, with no `success`
field (`API-AOBMaker-UI.md:53-54`; `WindowsPipeServer.cs:323-329`).
- It is the only command with neither a caller check nor a side effect: `SendDisassembly` fills AOBMaker.UI's input
  box (`API-AOBMaker-UI.md:56-58`), and `GetRelocationSelection` advances a copy cursor (`:172-178`).
- Having no caller check, it cannot detect the elevation refusal. Only `GenerateAob` or `ImportCheatTableXml` can.
- A connect timeout while a `GenerateAob` runs means busy, not absent.
- An older AOBMaker.UI answers `Ping` normally, without `features[]`. Every read on this pipe needs a timeout
  (reply §3 rule 7).
- `AobMakerUiMessage` has no `version` property yet, and `AobMakerUiClient` hard-codes the pipe name `AOBMaker`.

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
| &GEngine **SYM** | `CreateSymbolScript` | `GEngineAob` + metadata | as GWorld |
| GObjects / GNames **SYM** | `GenerateAob` (AOBMaker.UI) → `CreateSymbolScript` | the RIP instruction inside `<X>ScanAddr` | `IsAobMakerAvailable && addr != 0 && scan addr != 0` |
| FSparseDelegateStorage / &GEngine scan hit **ASM** | `NavigateDisassembler` | `SparseDelegatesScanAddr` / `GEngineScanAddr` | `IsAobMakerAvailable && addr != 0` |

- **HEX** = data address → CE hex dump
- **ASM** = code address (AOB scan hit) → CE disassembler
- **SYM** = create persistent AOB-scan CE symbol

### LiveWalker Buttons

| Button | AOBMaker Call | Address Source |
|--------|--------------|----------------|
| Field **HEX** | `NavigateHexView` | `field.FieldAddress` (base + offset) |
| Ptr **HEX** | `NavigateHexView` | `field.PtrAddress` (dereferenced pointer target) |
| Object **HEX** | `NavigateHexView` | `CurrentAddress` (current object base) |
| Field **+CE** | `CreateMemoryRecord` | `field.FieldAddress` — typed via `MapFieldToCeRecordType` |
| Ptr **+CE** | `CreateMemoryRecord` | `field.PtrAddress` — 8 Bytes / ShowAsHex (`PointerRecordType`) |

### Other panels `[AOBMAKER-EVAL-2026-09-29]`

These share one availability flag, `Helpers/AobMakerStatus`: probed when their tab is opened (5 s cooldown),
repainted by the toolbar ⟳, and re-learned by every push. The actions themselves are `Helpers/AobMakerActions`.

| Panel | Buttons | Address | Notes |
|-------|---------|---------|-------|
| Value Search | HEX, +CE on candidates and group slots | the candidate / leaf address | a slot only when the decoder gave it a leaf address |
| Snapshot diff, SPC | HEX, +CE on rows and group slots | owner + offset | the Copy button's session gate; an array-element row (`Name[3]`) is refused: it has only its owner's address |
| Instance Finder | HEX, +CE on fields; HEX on instances and container owners; AA pushes | `PayloadAddress` for fields | no +CE on an object base: it would show a vtable pointer |
| Object Tree, Class Pivot, Related Objects | HEX | the object | Class Pivot keeps its session gate |
| Live Walker functions, Interesting Functions, Live Funcs, function-properties dialog | ASM | the native entry | see `CreateMemoryRecord` above |

### Top-Toolbar Status Chip

`MainWindow.axaml` carries an always-visible AOBMaker status chip (colored dot +
Connected/Offline + **⟳** refresh) bound to `MainWindowViewModel.IsAobMakerAvailable`.
It mirrors the per-tab availability (LiveWalker / Pointers each probe on tab activation),
and the **⟳** button re-probes on demand (`RefreshAobMakerCommand`). The System-tab
indicator (PointerPanel) stays as the detailed in-tab status. Beside it, **⚠ CE is not on this game** appears when
`GetAttachedProcess` says Cheat Engine has another process open, or none; its tooltip says which.

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
| `ui/UE5DumpUI/Core/IAobMakerBridge.cs` | Interface — 8 methods (incl. `CreateMemoryRecordAsync`, `InjectTableFileAsync`, `GetAttachedProcessAsync`) |
| `ui/UE5DumpUI/Core/IAobMakerUiClient.cs`, `Services/AobMakerUiClient.cs`, `Models/AobMakerUiMessage.cs` | AOBMaker.UI's own pipe: `GenerateAob` |
| `ui/UE5DumpUI/Helpers/AobMakerStatus.cs`, `Helpers/AobMakerActions.cs` | The shared flag and actions of the panels added 2026-09-29 |
| `ui/UE5DumpUI/Services/DissectScriptGenerator.cs`, `Services/DissectLuaResource.cs` | Auto Structure Dissect record + the embedded `ue5_dissect.lua` |
| `ui/UE5DumpUI/Services/CeXmlExportService.cs` | `MapFieldToCeRecordType` / `PointerRecordType` — UE→CE record-type mapping shared with Copy CE XML/Field |
| `ui/UE5DumpUI/Services/AobMakerBridgeService.cs` | Implementation — pipe client, per-request reconnect |
| `ui/UE5DumpUI/Models/AobMakerMessage.cs` | Wire model + AOT-safe `JsonSerializerContext` |
| `ui/UE5DumpUI/Models/EngineState.cs` | AOB metadata (scan addr, pattern, pos, aoblen) |
| `ui/UE5DumpUI/ViewModels/PointerPanelViewModel.cs` | HEX/ASM/SYM buttons |
| `ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs` | Field/Ptr/Object HEX + invoke script delivery |
| `ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs` | Tab-switch re-detection wiring |
| `ui/UE5DumpUI/App.axaml.cs` | Service creation + DI |

---

## JSON Encoding Note

The CE plugin parses JSON in C++ (`json_parse.h`), not in Lua. It has decoded `\uXXXX` escapes to UTF-8 since AOBMaker `3ac12c2` (2026-03-05), and surrogate pairs since `56b308f` (2026-08-29). A string value that decodes to a NUL is refused whole (`json_parse.h:186-197` at `9431370`). So a single quote sent as `\u0027` arrives as `'`.

`AobMakerJsonContext.Relaxed` still uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` to emit literal characters instead of escape sequences. That is harmless and helps a plugin built before March 2026, but it is not required:

```csharp
public static AobMakerJsonContext Relaxed => _relaxed ??= new(new JsonSerializerOptions
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
});
```

This is only used for **outgoing requests** (which contain script content). Incoming responses use the default context.
