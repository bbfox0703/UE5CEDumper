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

## Property Type Layouts (Drill-Down Reference)

Single-value handlers and array element readers (Phase B–K in `Ubel.cpp`) are
driven by these on-disk layouts. `fnameSize` = 8 (default) or 16 (when
`bCasePreservingName` is set).

### Pointer-shaped properties (8 bytes each)

| Property | Layout | Notes |
|----------|--------|-------|
| `ObjectProperty` / `ClassProperty` | `UObject*` (8B) | Phase D |
| `WeakObjectProperty` | `{ int32 ObjectIndex, int32 SerialNumber }` (8B) | Phase E — resolve via `ResolveWeakObjectPtr` |

### Smart pointers (TPersistentObjectPtr family)

```
TSoftObjectPtr<T> / TSoftClassPtr<T>      // Phase G
+0x00 FWeakObjectPtr (8B)
+0x08 Tag (4B) + pad (4B)
+0x10 FSoftObjectPath
        UE4 / UE5.0:  FName AssetPathName + FString SubPathString
        UE5.1+:       FName PackageName + FName AssetName + FString SubPathString
total: 0x28 (UE4 default) ... 0x48 (UE5.1+ with CasePreservingName)
```

```
TLazyObjectPtr<T>                          // Phase H
+0x00 FWeakObjectPtr (8B)
+0x08 Tag (4B) + pad (4B)
+0x10 FUniqueObjectGuid (FGuid = 4 x uint32, 16B)
total: 0x20 (fixed)
```

Both expose the embedded `FWeakObjectPtr` so when the asset is currently
loaded the live `UObject*` resolves and is set on `fv.ptrValue` — Live
Walker drill / Address Finder / CSX export all pick this up.

### Interface

```
FScriptInterface (InterfaceProperty)       // Phase I
+0x00 UObject* ObjectPointer  (8B)
+0x08 void*    InterfacePointer (8B)
total: 16 (fixed)
```

### Delegates

```
FScriptDelegate (DelegateProperty)         // Phase J
+0x00 FWeakObjectPtr (8B)  -> bound UObject*
+0x08 FName FunctionName (8B or 16B)
total: 16 or 24 depending on CasePreservingName
```

```
FMulticastScriptDelegate                   // Phase K (single-value AND array)
+0x00 TArray<FScriptDelegate> InvocationList
        Data*  (8B)
        Count  (4B)
        Max    (4B)
total: 16 (fixed)
```

A **single** `MulticastInlineDelegateProperty` field is exposed by
`WalkInstance` as an *implicit* `DelegateProperty` array (`ArrayCount`,
`ArrayInnerType="DelegateProperty"`, `ArrayElemSize`, `ArrayElements`
populated). This makes `IsContainerNavigable=true` so the UI / CE XML /
CSX export reuse the standard array drill path. CE XML's `Offsets=[0]`
correctly dereferences `InvocationList::Data`.

Find Refs v3 piggybacks on the same shape: `DelegateProperty` (single)
goes through `weakLikePointers` because its `FWeakObjectPtr` target sits
at field+0; `MulticastInlineDelegateProperty` /
`MulticastDelegateProperty` go through `weakLikeArrays` because the
field IS already a `TArray<FScriptDelegate>` at field+0, and each
binding has `FWeakObjectPtr` at element+0. Stride is `8 + sizeof(FName)`
(16 with normal FName, 24 with case-preserving). This surfaces "X is
bound to a delegate on Y" relationships that property-only scans miss.

`MulticastSparseDelegateProperty` stores only an `FSparseDelegate { uint8
bIsBound }` flag at the field address. The actual `FScriptDelegate`
bindings live in CoreUObject's global `FSparseDelegateStorage` (a
`TMap<FObjectKey, TMap<FName, TSharedPtr<FMulticastScriptDelegate>>>`),
keyed by the owning UObject and the delegate's FName. `WalkInstance`
surfaces the bound flag as `(sparse, bound — bindings in
FSparseDelegateStorage)` or `(sparse, unbound)`. Drill-down into the
bindings list is **not** wired up — it would require a new AOB
signature to locate the static storage map. Find Refs v2 likewise can't
follow sparse-delegate target pointers without the storage walk.

### OptionalProperty (UE 5.2+)

`FOptionalProperty` wraps `TOptional<T>` and is laid out as
`FProperty + FProperty* ValueProperty` — the same shape as
`FArrayProperty`, so `WalkClassEx` reuses the `FARRAYPROP_INNER` probe
to populate `innerType`. Two storage layouts exist depending on `T`:

- **Intrusive** (UE 5.4+ for pointer types `Object/Class/Interface` and
  the FWeakObjectPtr-shaped `Weak/Soft/Lazy`): `T` occupies the field
  directly; "unset" is encoded as null/zero (or `{ idx=0, serial=0 }`
  for weak-like). `sizeof(TOptional<T>) == sizeof(T)`.
- **Intrusive via `FIntrusiveUnsetOptionalState` specialization** for
  heap-backed types — the unset flag lives *inside* T's normal fields
  rather than as a trailing byte. The DLL hand-codes the sentinel checks
  (which mirror each type's `UEOpEquals(FIntrusiveUnsetOptionalState)`):

  | Inner type     | Sentinel              | Field offset (within `T`) | UE source |
  |----------------|------------------------|---------------------------|-----------|
  | `StrProperty`  | `int32 Max == -1`     | +12 (FString.Max)         | UnrealString.h.inl |
  | `NameProperty` | `uint32 ComparisonIndex == 0xFFFFFFFF` | +0 | NameTypes.h |
  | `TextProperty` | `uintptr_t TextData == nullptr` | +0 | Internationalization/Text.h |

  For these, `sizeof(TOptional<T>) == sizeof(T)` (no trailing flag) and
  reading `bIsSet` past `T` would land on the next UPROPERTY's memory —
  source of subtle false positives until the sentinel paths shipped.
- **Non-intrusive** (older + non-pointer T like Int/Float/Bool/Byte/Enum
  and StructProperty): `{ T value; uint8 bIsSet; }` with the trailing
  flag at `field + sizeof(T)`.

`WalkInstance` dispatches by inner type: pointer-shaped innners use the
null-sentinel test, scalars/structs read the trailing `bIsSet` byte at
`field + ResolveInnerSize(inner)`. The display string is `(unset)` when
not set, otherwise the rendered inner value (resolved UObject*, scalar
text, etc.). Drill-down into struct-typed Optional is not yet wired —
the inner struct fields aren't surfaced.

Find Refs v2 covers `OptionalProperty<Object/Class/Interface>` (treated
as direct pointers) and `OptionalProperty<Weak/Soft/Lazy>` (resolved
through the embedded FWeakObjectPtr). For UE 5.2–5.3 non-intrusive
pointer optionals, an unset slot's value is typically zero so it
trivially fails the comparison; the rare uninitialized-memory false
positive isn't filtered out (would require caching the inner size
alongside the cache entry).

### Validating element stride

Inner FProperty `ELEMSIZE` reads frequently return garbage. Each Phase
reader picks one of three strategies:

- **Force a fixed value** when the layout is invariant: Object/Weak (8),
  Interface (16), Lazy (0x20), Delegate-via-CasePreservingName (16 or 24)
- **Sanity-clamp + fallback** when version-dependent: Soft (0x28..0x48),
  with fallback formula `0x10 + (isTopLevelAssetPath ? 2*fnameSize : fnameSize) + 0x10`
- **Trust the read** when the inner has a real size: Struct (use
  `UScriptStruct::PropertiesSize`), Scalar (4/8/etc.)

`InferScalarSize` only declares known fixed sizes; variable-stride types
(`SoftObjectProperty`, `SoftClassProperty`, `DelegateProperty`) are
deliberately left out so `ValidateArrayElemSize` does not force a wrong
override — the readers self-correct.

-----

## Array Element Reader Phases

| Phase | Inner type(s) | Element size | Notes |
|-------|---------------|--------------|-------|
| B | scalar (Float/Int/Bool/Byte/Name/Enum) | 1..8 | `ReadArrayElements` — pageable via `read_array_elements` pipe cmd |
| D | `ObjectProperty` / `ClassProperty` | 8 (forced) | `ReadPointerArrayElements` — resolves `UObject*` name + class |
| E | `WeakObjectProperty` | 8 (forced) | `ReadWeakObjectArrayElements` — verify SerialNumber |
| F | `StructProperty` | `PropertiesSize` of inner UScriptStruct | `ReadStructArrayElements` — populates `StructSubField[]` |
| G | `SoftObjectProperty` / `SoftClassProperty` | 0x28..0x48 (validated/derived) | `ReadSoftObjectArrayElements` — asset path + resolved live `UObject*` |
| H | `LazyObjectProperty` | 0x20 (forced) | `ReadLazyObjectArrayElements` — FGuid + resolved live `UObject*` |
| I | `InterfaceProperty` | 16 (forced) | `ReadInterfaceArrayElements` — UObject* exposed |
| J | `DelegateProperty` | 16 or 24 | `ReadDelegateArrayElements` — Target::FunctionName + drill-into-target |
| K | `MulticastDelegateProperty` / `MulticastInlineDelegateProperty` | 16 (forced) | `ReadMulticastDelegateArrayElements` — preview only ("(N bindings) [...]"), no per-binding drill |

All readers cap at 4096 elements per request; `WalkInstance` further
constrains to `arrayLimit` (default 64, configurable in UI). Each Phase
is dispatched twice in the WalkInstance ArrayProperty handler — once in
the FProperty branch (UE4.25+/UE5) and once in the UProperty fallback
branch (UE4.18–4.24).

-----

## Address Finder — Layered Lookup

`Aura::FindByAddress(addr)` produces a single best UObject hit. The full
flow descends through these strategies (high→low confidence) and reports
the kind via `match_kind`:

| match_kind | Strategy | Confidence |
|------------|----------|-----------|
| `exact`    | `addr` IS a UObject pointer (matches GObjects entry) | highest |
| `contains` | `addr` ∈ [obj, obj + obj.PropertiesSize) for some GObjects entry | high |
| `backward` | Backward 64KB memory scan finds a UObject header pattern; `addr` is past its bounds | medium — typically a `NewObject<>`'d sub-object not registered in GObjects |
| `nearest`  | Closest GObjects entry below `addr` within 256KB; `addr` is BEYOND its PropertiesSize | low — frequently misleading, surfaced as a hint only |

`Aura::FindInContainers(addr)` is a parallel container-aware scan: for
every UObject in GObjects, walk its container fields and report any whose
`[Data, Data + bound)` range contains `addr`.

### Nested struct support

The cache builder (`CollectContainersRecursive` in `Aura.cpp`) recurses
through `StructProperty` fields up to depth 3, so nested arrays/maps/sets
inside USTRUCT() fields are detected. Common pattern:

```cpp
USTRUCT() struct FCharStats { TArray<int32> Levels; };
UCLASS()  class  UPlayerInfo : public UObject {
    UPROPERTY() FCharStats Stats;
};
```

A hit on `UPlayerInfo.Stats.Levels[3]` reports field name `"Stats.Levels"`
with absolute offset `Stats.Offset + Levels.Offset`. Cycle protection is
via the depth cap (no `visited` set, allowing the same struct type to be
visited via different paths with different offsets).

### Match confidence notes

Each container match also carries a `note` string:
- `""`     — solid hit (within Count, allocated slot)
- `"slack"` — Array index ∈ [Count, Max); the slot is allocated capacity
              but not currently in use. Memory often retains the last-
              written value, so the match is plausible but lower confidence.
- `"freed"` — Map/Set sparse slot is on the free list; same caveat.

### Reflection limits

Container scan only finds addresses inside reflected memory:
- UObjects registered in GObjects
- Their `UPROPERTY()`-marked container Data buffers (incl. nested)

Game data stored in the following won't be found:
- Custom allocators bypassing `FMemory` (common in Square Enix titles —
  FF7 Rebirth Cloud HP and DQ I&II HD-2D character stats both fall here)
- `TUniquePtr<FCustomData>` / raw `void*` C++ fields not wrapped in a
  `UPROPERTY()` — invisible to UE reflection
- Save-game serialization buffers (`FArchive`, `FBufferArchive`)
- Anti-tamper shadow regions

For these the right tool is CE's "Find what accesses this address" /
pointer-scan workflow, then drill into the exposed pointer chain.

### Performance

| Concern | Mitigation |
|---------|-----------|
| Scan time on huge games (~430K UObjects) | 15s deadline (was 5s); response carries `container_scan` stats so UI can flag truncated scans and prompt retry |
| Repeated scans | Per-class `s_classContainerCache` persists for DLL lifetime; second call typically finishes in ~70ms once cache is warm |
| Corrupt TArray::Max projecting huge buffer span | Defensive 1M cap on Max / MaxCapacity (matches Count's existing cap) |
| Element-count limits | 1M cap on `Count` / `MaxIndex` — well above any realistic game data (6 chars / 30 attrs / 600 items all fit comfortably) |

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
