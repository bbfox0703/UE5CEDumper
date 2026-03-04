# UFunction Invoker — Implementation Roadmap

> **Goal**: Browse UFunctions in LiveWalker → see parameters → generate CE invoke scripts automatically.
> Inspired by GH UE Dumper's `UE_InvokeActorEvent` workflow, but integrated into our UI with no Lua debugging needed.

---

## Use Case Examples

```
InstanceFinder: find "ShopKeeper_C" instance
  → LiveWalker: browse to ShopKeeper → expand functions
  → See: openShop(CustomerActor: ObjectProperty, 8B)
  → Click [Generate Script]
  → CE AA Script auto-created with instance resolver + param GUI
  → User activates in CE → fills params → clicks FIRE → shop opens
```

```
LiveWalker: browse playerCharacterBP_C
  → expand addMoney() → see 3 params (Amount int32, SkipCounting bool, Success bool|out)
  → Click [Generate Script]
  → CE creates invoke script with GUI form for all params
```

---

## Current State (what we already have)

| Component | Status | Location |
|-----------|--------|----------|
| UFunction walking (DLL) | Done | `UStructWalker.cpp:648-750` |
| FunctionInfo struct | Done | `UStructWalker.h:46-61` |
| `walk_functions` pipe command | Done | `PipeServer.cpp:518-560` |
| FunctionInfoModel (C#) | Done | `Models/FunctionInfoModel.cs` |
| WalkFunctionsAsync | Done | `DumpService.cs:686-732` |
| SDK function signature gen | Done | `SdkExportService.cs:428-470` |
| Parameter type mapping | Done | SDK export (IntProperty → int32_t, etc.) |

### What's missing

| Item | Impact | Phase |
|------|--------|-------|
| Parameter **offset** within param block | Required for invoke script | I |
| **ParmsSize** (total param buffer size) | Required for invoke script | I |
| FunctionFlags **decoding** | Display (NATIVE, EVENT, STATIC) | I |
| LiveWalker function node display | UX — see functions in tree | I |
| CE Lua invoke script **template engine** | Core feature | I |
| **Generate Script** button in UI | UX trigger | I |
| In-process ProcessEvent call | Direct invocation without CE Lua | II |
| Game thread dispatch | Safe ProcessEvent execution | II |

---

## Phase I: Script Generation (no game thread issues)

Generate CE Lua invoke scripts from UFunction metadata. User activates script in CE to call functions.

### Task 1.1 — DLL: Extract parameter offsets + ParmsSize
**Files**: `UStructWalker.cpp`, `UStructWalker.h`

- [ ] Add `offset` field to `FunctionParam` struct
- [ ] Read each FProperty's `Offset_Internal` (same offset used for class properties)
- [ ] Add `parmsSize` field to `FunctionInfo` struct
- [ ] Read UFunction's `ParmsSize` field (heuristic probe like FunctionFlags)
  - Common offsets for ParmsSize: `0xCC`, `0xDC`, `0x90`, `0xB4`
  - Validate: ParmsSize should be ≥ sum of param sizes, ≤ 0x1000
- [ ] Add `numParms` field to `FunctionInfo` (from UFunction header, cross-validate with param count)

**Estimated**: ~40 LOC

### Task 1.2 — DLL: Pipe protocol update
**Files**: `PipeServer.cpp`

- [ ] Add `offset` to each param in `walk_functions` response
- [ ] Add `parms_size` to each function in response
- [ ] Add `num_parms` to response

Updated response format:
```json
{
  "functions": [{
    "name": "addMoney",
    "addr": "7FF601234000",
    "flags": 1536,
    "parms_size": 6,
    "num_parms": 3,
    "ret": "",
    "params": [
      { "name": "Amount", "type": "IntProperty", "size": 4, "offset": 0, "out": false, "ret": false },
      { "name": "SkipCounting", "type": "BoolProperty", "size": 1, "offset": 4, "out": false, "ret": false },
      { "name": "Success", "type": "BoolProperty", "size": 1, "offset": 5, "out": true, "ret": false }
    ]
  }]
}
```

**Estimated**: ~15 LOC

### Task 1.3 — UI: Update models + service
**Files**: `FunctionInfoModel.cs`, `DumpService.cs`

- [ ] Add `Offset` to `FunctionParamModel`
- [ ] Add `ParmsSize`, `NumParms` to `FunctionInfoModel`
- [ ] Update `WalkFunctionsAsync` JSON deserialization
- [ ] Add `FunctionFlagsHelper` static class to decode flags to readable strings
  - `FUNC_Native`, `FUNC_Event`, `FUNC_BlueprintCallable`, `FUNC_Exec`, etc.

**Estimated**: ~50 LOC

### Task 1.4 — UI: LiveWalker function display
**Files**: `LiveWalkerViewModel.cs`, `LiveWalkerPanel.axaml`

- [ ] After loading class properties, call `WalkFunctionsAsync` for current class
- [ ] Add function nodes to tree under a "Functions" group
- [ ] Each function node shows: name, return type, param summary
- [ ] Expandable: shows individual params with type, size, offset, in/out
- [ ] Display decoded FunctionFlags as tags (Native, Event, BlueprintCallable)

**Estimated**: ~80 LOC

### Task 1.5 — Core: CE Lua Script Template Engine
**Files**: NEW `Services/InvokeScriptGenerator.cs`

- [ ] `GenerateInvokeScript(className, funcName, params, invokeMethod)` → string
- [ ] Template sections:
  1. UE Dumper connection check (reuse existing pattern)
  2. Instance resolver: `UE_GetAllObjectsOfClass` → skip CDOs → fallback to subclasses
  3. UFunction resolver: `UE_GetFunctionsOfObject` → name match → case-insensitive fallback
  4. GUI form builder (if params exist):
     - Label + Edit for each param
     - Type-aware defaults: `0` for int/float, `false` for bool, `0x0` for pointers
     - Size-to-CE-type mapping: 1B→szByte, 2B→szWord, 4B→szDword, 8B→szQword
  5. Fire button: build param table → invoke → show result
  6. Cancel + cleanup
- [ ] Support two invoke methods:
  - `UE_InvokeActorEvent` — for AActor subclasses
  - `UE_InvokeObjectEventSafe` — for general UObject

**Type-to-CE mapping table**:
| UE Property Type | CE Size Constant | Default | Input Parse |
|-----------------|-----------------|---------|-------------|
| BoolProperty | szByte | 0 | `tonumber(text) or 0` |
| ByteProperty | szByte | 0 | `tonumber(text) or 0` |
| Int8Property | szByte | 0 | `tonumber(text) or 0` |
| Int16Property | szWord | 0 | `tonumber(text) or 0` |
| UInt16Property | szWord | 0 | `tonumber(text) or 0` |
| IntProperty | szDword | 0 | `math.floor(tonumber(text) or 0)` |
| UInt32Property | szDword | 0 | `math.floor(tonumber(text) or 0)` |
| Int64Property | szQword | 0 | `tonumber(text) or 0` |
| UInt64Property | szQword | 0 | `tonumber(text) or 0` |
| FloatProperty | szFloat | 0.0 | `tonumber(text) or 0.0` |
| DoubleProperty | szDouble | 0.0 | `tonumber(text) or 0.0` |
| NameProperty | szQword | 0x0 | hex-aware parse |
| ObjectProperty | szQword | 0x0 | hex-aware parse |
| ClassProperty | szQword | 0x0 | hex-aware parse |
| StructProperty | raw bytes | — | hex string to bytes |

**Estimated**: ~200 LOC

### Task 1.6 — UI: Generate Script Button + AOBMaker Integration
**Files**: `LiveWalkerViewModel.cs`, `LiveWalkerPanel.axaml`

- [ ] Add "Generate Invoke Script" context action on UFunction nodes
- [ ] On click:
  1. Call `InvokeScriptGenerator.GenerateInvokeScript()`
  2. If AOBMaker available: `CreateAAScriptAsync(description, script, autoActivate: false)`
  3. Fallback: copy script to clipboard with notification
- [ ] Script description format: `"Invoke: {ClassName}::{FunctionName}"`
- [ ] Add string resources for button labels and tooltips

**Estimated**: ~60 LOC

### Task 1.7 — Strings + Tests
**Files**: `en.axaml`, `UE5DumpUI.Tests/`

- [ ] Add UI strings: function display labels, button text, tooltips, status messages
- [ ] Tests:
  - InvokeScriptGenerator: no-param function, multi-param with types, out-param marking
  - FunctionFlagsHelper: decode known flag combinations
  - FunctionParamModel: offset + size serialization

**Estimated**: ~80 LOC

### Phase I Total: ~525 LOC

---

## Phase II: In-Process ProcessEvent Invocation (Future)

Call UFunction directly from our DLL — no CE Lua dependency. This is the complex phase.

### Task 2.1 — DLL: Find ProcessEvent vtable entry
**Files**: `OffsetFinder.cpp`, `OffsetFinder.h`

- [ ] Resolve `UObject::ProcessEvent` address
  - Option A: AOB scan for ProcessEvent function prologue
  - Option B: vtable index probe — read known UObject's vtable, index ~66-70
  - Option C: String-ref scan for "ProcessEvent" debug name
- [ ] Cache resolved address in EnginePointers
- [ ] Add to pipe `get_pointers` response

**Risk**: vtable index varies across UE versions. Need multi-version heuristic.

### Task 2.2 — DLL: Parameter buffer construction
**Files**: NEW `FunctionInvoker.cpp`, `FunctionInvoker.h`

- [ ] `BuildParamBuffer(UFunction*, vector<ParamValue>)` → allocated buffer
- [ ] Read UFunction's property chain for offset/size layout
- [ ] Support basic types: int8/16/32/64, uint8/16/32/64, float, double, bool
- [ ] Support pointer types: ObjectProperty → write raw address
- [ ] Support FName: write ComparisonIndex + Number pair
- [ ] Support StructProperty: raw byte copy
- [ ] Handle out-params: allocate space, read back after call
- [ ] Handle return value: extract from ReturnValueOffset

### Task 2.3 — DLL: Game thread dispatch
**Files**: `FunctionInvoker.cpp`, `PipeServer.cpp`

- [ ] Thread-safe invocation queue (lock-free or mutex-protected)
- [ ] **Option A — Tick hook**: Hook `UWorld::Tick` or `UGameEngine::Tick`, drain queue each frame
- [ ] **Option B — UE task system**: Use `FFunctionGraphTask::CreateAndDispatchWhenReady` if GEngine accessible
- [ ] **Option C — Remote thread**: `CreateRemoteThread` targeting game's main thread (risky, may crash)
- [ ] Timeout mechanism: if function doesn't return within N ms, report failure
- [ ] Result marshaling: read out-params and return value back to pipe response

**Risk**: This is the hardest part. Wrong thread = crash. Hook stability across UE versions is uncertain.

### Task 2.4 — Pipe protocol: invoke_function command
**Files**: `PipeServer.cpp`, `PipeProtocol.h`

- [ ] New command: `invoke_function`
- [ ] Request:
```json
{
  "cmd": "invoke_function",
  "instance": "7FF601234000",
  "ufunction": "7FF601235000",
  "params": [
    { "type": "int32", "value": 1000 },
    { "type": "bool", "value": false }
  ]
}
```
- [ ] Response (async — may take multiple frames):
```json
{
  "ok": true,
  "out_params": { "Success": true },
  "return_value": null
}
```

### Task 2.5 — UI: Direct Invoke Button
**Files**: `LiveWalkerViewModel.cs`, `LiveWalkerPanel.axaml`

- [ ] "Invoke" button alongside "Generate Script" (only shown when ProcessEvent resolved)
- [ ] Param input dialog in Avalonia (not CE Lua form)
- [ ] Show result: out-params, return value, error message
- [ ] Safety: confirmation dialog before invoking (irreversible game state change)

### Phase II Estimated: ~600-800 LOC (DLL) + ~200 LOC (UI)
### Phase II Risk: High — game thread dispatch may vary per game

---

## Implementation Order

```
Phase I (Script Generation — safe, no crashes)
  1.1  DLL: param offsets + ParmsSize          ← start here
  1.2  DLL: pipe protocol update
  1.3  UI: models + service update
  1.4  UI: LiveWalker function display
  1.5  Core: CE Lua script template engine
  1.6  UI: Generate Script button
  1.7  Strings + tests
  → Build + Test → Commit

Phase II (In-Process Invocation — future, complex)
  2.1  DLL: find ProcessEvent
  2.2  DLL: param buffer construction
  2.3  DLL: game thread dispatch             ← hardest
  2.4  Pipe: invoke_function command
  2.5  UI: direct invoke button
  → Build + Test → Commit
```

---

## Dependencies

- Phase I requires: GH UE Dumper CE plugin (for `UE_InvokeActorEvent` at runtime)
- Phase II removes GH dependency: our DLL calls ProcessEvent directly
- AOBMaker CE bridge (existing) used for CreateAAScript delivery in Phase I

## Notes

- Phase I scripts are **self-contained** — they resolve by class/function name, survive game restarts
- Phase I generated scripts work with any CE UE Dumper that exposes invoke APIs
- Phase II is optional — Phase I alone provides full invoke capability via CE
- FunctionFlags decoding uses UE's `EFunctionFlags` enum (stable across versions)
