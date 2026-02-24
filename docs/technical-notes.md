# Technical Notes

> Moved from CLAUDE.md. Covers UE version differences, FField vs UProperty, FNamePool internals, and implementation phases.

-----

## UE Version Differences

| Version | Key Differences |
|---------|----------------|
| UE4.11–4.20 | `FFixedUObjectArray` (flat, single indirection). `UProperty` chain. TNameEntryArray on some builds |
| UE4.21–4.24 | `FChunkedFixedUObjectArray` introduced. `UProperty` still in use |
| UE4.25–4.27 | `FField`/`FProperty` replaces `UProperty` (no longer inherits UObject). `ChildProperties` chain added |
| UE5.0–5.1.0 | FNamePool standard format. FFieldVariant = `{ void*, bool }` (0x10 bytes with padding) |
| UE5.1.1+ | FFieldVariant = `{ void* }` (0x08 bytes) — affects ChildProperties offset |
| UE5.2 | `FChunkedFixedUObjectArray` stride may differ |
| UE5.3+ | Some games enable Object Pointer Encryption |
| UE5.4+ | `FField` chain structure stable, no major changes |
| UE5.5+/5.7 | **CasePreservingName**: FName grows from 0x8 to 0x10 bytes (adds DisplayIndex field), shifting FField::Flags +0x8 and all FProperty offsets by +0x8. Must use `DynOff` dynamic detection |

-----

## FField vs UProperty

- **Before UE4.24**: `UProperty` (inherits UObject, found via `UStruct::Children` chain)
- **UE4.25+ / All UE5**: `FField` (**does not** inherit UObject, found via `UStruct::ChildProperties` chain)
- `UStruct::ChildProperties` = `FField*` chain head (FProperty only)
- `UStruct::Children` = `UField*` chain (for functions; `UFunction` inherits UObject)
- `UStructWalker` must handle both chains

### FProperty-to-UProperty Fallback

When version is misdetected (defaults to 504), `DetectUPropertyMode` may select FProperty mode. `ValidateAndFixOffsets` detects the failure (FFieldClass check fails on UProperty) and retries with UProperty scan — checks `UObject::ClassPrivate` for class name containing "Property". This auto-corrects mode even with wrong version detection.

### Key Offset Differences (UE4.18 vs UE5 defaults)

| Field | UE4.18 (FF7R) | UE5 default |
|-------|--------------|-------------|
| UStruct::SuperStruct | +0x30 | +0x40 |
| UStruct::Children | +0x38 | +0x48 |
| UProperty::Offset_Internal | +0x44 | — |
| UField::Next | +0x28 | — |

-----

## FNamePool Internals

### Chunk Calculation (Standard UE5)

```cpp
// FNamePool layout
// Chunks: uintptr_t* array at GNames+0x10
// Each chunk max 0x20000 bytes
// Stride: each FNameEntry aligned to 2 bytes (standard) or 4 bytes (hash-prefixed)

uintptr_t GetNameEntry(int32_t nameIndex) {
    int32_t chunkIndex  = nameIndex >> 16;              // high 16 bits = chunk
    int32_t chunkOffset = (nameIndex & 0xFFFF) * 2;    // low 16 bits * stride
    uintptr_t chunk = Mem::Read<uintptr_t>(GNames + chunkIndex * 8);
    return chunk + chunkOffset;
}
```

### FNameEntry Formats

| Format | Layout | Used by |
|--------|--------|---------|
| Standard UE5 | `[2B header][string]` | Most UE5 games |
| Hash-prefixed (UE4.26 SE fork) | `[4B ComparisonId][2B header][string]` | FF7Re (Square Enix) |
| UE4 TNameEntryArray | double-deref: `array→chunk→FNameEntry*` | OctoPath Traveler, some UE4 |

### FNamePool Structure

```
GNames+0x00: FRWLock (8B) — reads as 0 when unlocked (NORMAL, not a bug)
GNames+0x08: CurrentBlock (4B)
GNames+0x0C: Cursor (4B)
GNames+0x10: Blocks[0] (first chunk pointer)
```

> **Note**: `[GNames]` (reading GNames as a pointer) gives FRWLock = 0. This is not a null pointer — GNames is an inline struct in `.data`, not a pointer-to-pointer.

-----

## GObjects Array Layouts

### Chunked (FChunkedFixedUObjectArray, UE4.21+/UE5)

```
GObjects → FUObjectArray
  +0x00: Objects** (chunk table pointer)     [Layout A/C]
  +0x10: MaxElements (int32)
  +0x14: NumElements (int32)
```

Or for UE4 with extra members:
```
  +0x10: Objects** (chunk table pointer)     [Layout B]
  +0x04: NumElements (or ObjLastNonGCIndex)
```

### Flat (FFixedUObjectArray, UE4.11–4.20)

```
GObjects → FUObjectArray
  +0x00: Objects* (direct item array pointer, no chunk table)
  +0x08: MaxElements (int32)
  +0x0C: NumElements (int32)
```

Detection: when `numElements > OBJECTS_PER_CHUNK`, check if `*(Objects + 8)` is a valid heap pointer. If not (e.g., `0x40000000` = EObjectFlags::Const), the array is flat.

### FUObjectItem Sizes

| Size | Used by |
|------|---------|
| 16B | UE5 standard, some UE4 without GC clustering |
| 24B | Most UE4 (Object\* + Flags + ClusterRootIndex + SerialNumber + pad) |
| 20B | Rare variants |

Detection via `DetectItemSize()`: walk stride-aligned positions, validate with FNamePool string resolution. Score = `named * 10 - bad * 3`. When all scores negative, pick stride with fewest bad items (fallback v5).

-----

## DynOff — Dynamic Offset Detection

`ValidateAndFixOffsets()` in `OffsetFinder.cpp` probes **known-layout structs** to discover correct FField/FProperty/UStruct offsets at runtime:

1. Find a `Guid` UStruct (fields A/B/C/D at byte offsets 0/4/8/12 within the struct)
2. Or find a `Vector` UStruct (fields X/Y/Z at byte offsets 0/4/8)
3. Walk the `ChildProperties` chain, match fields by name and expected offset
4. From matching, derive: `FField::Name`, `FField::Next`, `FProperty::Offset_Internal`, `UStruct::ChildProperties`
5. Detect CasePreservingName: if derived `FField::Flags` offset = 0x38, add +0x8 to all FField/FProperty offsets

All DLL code uses `DynOff::*` namespace (mutable `inline int` values), never hardcoded `constexpr` offsets.

-----

## Export Function Naming Rules

- All C ABI exports prefixed with `UE5_`
- Avoid callbacks across DLL boundary — use Begin/Get/End batch mode instead
- Buffers allocated by caller (CE Lua side); DLL only writes into them

-----

## Implementation Phases

### Phase 1 — DLL Core

1. `Memory.cpp` — AOBScan + GetModuleBase
2. `OffsetFinder.cpp` — GObjects / GNames pattern scan
3. `ObjectArray.cpp` — FChunkedFixedUObjectArray + ForEach
4. `FNamePool.cpp` — GetString
5. `ExportAPI.cpp` — C ABI wrapper, CE Lua verification

### Phase 2 — Pipe IPC

1. `PipeServer.cpp` — Named Pipe server + JSON dispatch
2. CE Lua update: reduced to init + StartPipeServer only
3. PowerShell pipe testing (`[System.IO.Pipes.NamedPipeClientStream]`)

### Phase 3 — UI App

1. Avalonia project skeleton + ReactiveUI + Dark theme
2. `PipeClient.cs` — connection + send/receive + ReadLoop
3. `DumpService.cs` — business logic wrapper
4. `PointerPanel` — simplest, verify pipe connection first
5. `ObjectTreePanel` — paginated loading, virtualized TreeView
6. `ClassStructPanel` — walk_class → DataGrid display
7. `HexViewPanel` — read_mem + live watch

### Phase 4 — Polish

1. UStructWalker full implementation (FField chain + SuperStruct inheritance chain)
2. Object Tree search / filter
3. Single-file publish setup and testing
