# Dev Log

Running log of milestone work, current capability matrix, and known gaps.
Newest section first. Tied to the `dev` branch — entries reference build
numbers from `build_number.txt` so a commit can be cross-referenced.

-----

## 2026-05-09 (latest) — CE XML drill-down: cascade struct resolution + OptionalProperty handler

`fix(export): nested StructProperty inside drilled pointer targets +
OptionalProperty CE XML emit`
([`CeXmlExportService.cs`](../ui/UE5DumpUI/Services/CeXmlExportService.cs),
[`CsxExportService.cs`](../ui/UE5DumpUI/Services/CsxExportService.cs),
build 544+)

Two follow-up issues from build 541:

1. **StructProperty inside drilled pointer targets renders as empty
   `<GroupHeader>` placeholder.** Selecting `ScalabilityModifiers`
   (ObjectProperty) and Copy CE Field with Drill Depth=4 correctly
   drilled into `MapScalabilityModifierComponent`, but the inner
   `PrimaryComponentTick (ActorComponentTickFunction)` (StructProperty)
   came out as a group header with no children — same with
   `ComponentTags` / `AssetUserData` (ArrayProperty placeholders).
   The OLD CE XML output (without drill-down) used to expand
   `PrimaryActorTick` fully because it was top-level and got picked up
   by `ResolveStructFieldsAsync`; the new drill-down code path missed
   the cascade entirely.
2. **OptionalProperty fields silently vanished from CE XML.** Falling
   through `EmitFields` they hit no handler and got dropped. ES2's
   `MapScalabilityModifierComponent.VolumetricFogOverrides` (an
   `OptionalProperty<FBox>`) and the test fixtures all disappeared
   from the export.

### Root cause

`resolvedStructs` was keyed by `int field.Offset` and only built for
the root instance's struct fields. When the drill-down resolver
returned a target's children, those children carried their own struct
fields (with their own offsets within the drilled target) — but the
dict had no entry for them, so `EmitFields` fell through to the
navigable-placeholder path. Worse, the offset key collides across
instances (offset 0x30 in object A vs object B).

### Fix

- **`resolvedStructs` re-keyed `int -> string`**: the new key is
  `LiveFieldValue.StructDataAddr` (absolute address of the struct
  data — unique across instances). One dict can now serve struct
  fields anywhere in the drilled tree without collisions.
- **`ResolveStructFieldsAsync`** now writes into a passed dict via a
  new `ResolveStructFieldsIntoAsync` private helper. The public
  signature returns the new string-keyed dict.
- **`ResolvePointerInstancesAsync`** gained an optional
  `resolvedStructs:` parameter — when provided, every drilled
  target's fields are also walked for `StructProperty` and the
  results merged into the same dict. So drilling into A → finds
  StructProperty B inside A → walks B → adds B's sub-fields keyed by
  B's StructDataAddr → emit-time lookup finds them.
- **OptionalProperty handler** added to `EmitFields`:
  - When `StructDataAddr` is stamped (Optional&lt;Struct&gt; when
    set, walker populates the same `{StructDataAddr, StructClassAddr,
    StructTypeName}` triple as bare StructProperty), goes through
    `EmitResolvedStruct` → struct sub-fields rendered inline.
  - Otherwise falls through to a flat 8-byte hex leaf (at minimum CE
    has a watchable address for the optional slot).
  - The cascade resolver also picks up Optional&lt;Struct&gt; fields
    (treats them as StructProperty for resolution purposes).
- **`CsxExportService.EmitStructPropertyFlattened`** adapted to the
  new string-keyed dict (CSX shares `ResolveStructFieldsAsync`).
- **LiveWalkerViewModel** passes `resolvedStructs:` through to
  `ResolvePointerInstancesAsync` for both `ExportCeXmlAsync` and
  `ExportCeFieldXmlAsync`.

### Tests

7 existing tests re-keyed (`Dictionary<int, ...>` → `Dictionary<string, ...>`
with `"0xABC"` matching `StructDataAddr`). 3 new:
- `DrilledPointer_NestedStructProperty_ExpandsViaResolvedStructsCascade`
  — regression coverage for the headline bug
- `OptionalProperty_NoStructInner_EmitsFlatLeaf`
- `OptionalProperty_StructInner_ExpandsToStructGroup`

**Build #544, 496 tests passing** (was 493).

-----

## 2026-05-09 (mid-late) — CE XML pointer drill-down + Property Search scroll restore

`feat(export): CE XML/CE Field N-level ObjectProperty drill-down (depth slider)`
([`CeXmlExportService.cs`](../ui/UE5DumpUI/Services/CeXmlExportService.cs),
[`LiveWalkerViewModel.cs`](../ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs),
[`PropertySearchPanel.axaml.cs`](../ui/UE5DumpUI/Views/PropertySearchPanel.axaml.cs),
build 541+)

Two related issues from the build-534 session:

1. **Copy CE Field on `ScalabilityModifiers` (ObjectProperty) only emits a
   single 8-byte hex leaf.** The build-534 fix made the leaf shape valid
   (`<VariableType>8 Bytes</VariableType>` instead of the broken
   `<GroupHeader>1</GroupHeader>` placeholder), but users with
   already-shipped CE tables expected the export to **follow the
   pointer** and include the target's children — exactly the same way
   the CSX export's `drilldownDepth` slider already worked. Their
   workflow is "copy field → paste into CE → instantly inspect the
   referenced UObject's UPROPERTYs without manually re-poking offsets".

2. **Property Search loses scroll position + visible selection** when
   switching tabs (Property Search → Instance Finder → back). The VM
   keeps `SelectedResult` populated, but Avalonia's TabControl swaps
   the tab content out and back in, and the freshly-attached DataGrid
   doesn't auto-scroll to its `SelectedItem` — the highlighted row is
   offscreen so it visually looks like the selection was cleared.

### CE XML pointer drill-down

Mirrors the CSX implementation almost verbatim:

- New `CeXmlExportService.ResolvePointerInstancesAsync(dump, fields,
  depth, arrayLimit)` ports
  `CsxExportService.ResolvePointerInstancesAsync` — recursive walk
  through `ObjectProperty` / `ClassProperty` /
  `WeakObjectProperty` / `Soft*` / `LazyObjectProperty` /
  `InterfaceProperty` targets, depth-capped, with a shared `visited`
  HashSet for cycle protection. Returns `Dictionary<PtrAddress,
  Fields>` so emit-time lookup is O(1).
- `EmitFields` learned a new branch: when `resolvedInstances` has the
  field's PtrAddress and the type is in the `IsObjectPropertyType` set,
  call `EmitDrilledPointer` instead of the flat leaf path.
- `EmitDrilledPointer` writes the leaf as
  `<GroupHeader>1</GroupHeader> <Address>+{fieldOffset}</Address>
  <Offsets><Offset>0</Offset></Offsets>` followed by a recursive
  `EmitFields` over the target's children at their natural offsets
  within the dereferenced UObject. Description is decorated with the
  resolved `PtrClassName` so `BP_X (UCharacter)` is distinguishable
  from `BP_X (UPawn)` without expanding.
- `GenerateHierarchicalXml` / `GenerateInstanceXml` /
  `GenerateAobWrappedXml` all gained an optional
  `resolvedInstances:` parameter that flows through to `EmitFields`.
- `LiveWalkerViewModel.ExportCeXmlAsync` and `ExportCeFieldXmlAsync`
  pre-resolve via `ResolvePointerInstancesAsync(depth: CsxDrilldownDepth)`
  using the same toolbar slider.

### Slider repurposed

The 0-4 slider previously labelled `CSX Depth` (`str.Toolbar.CsxDepth`)
now drives drill-down for **both** CSX and CE XML / CE Field exports;
renamed to `Drill Depth:` and the tooltip clarifies the broader scope.
Backing property `CsxDrilldownDepth` is unchanged (preserves user
preferences).

### Property Search scroll restore

`PropertySearchPanel.axaml.cs` hooks `Loaded` → looks up
`ResultsGrid` (newly named `x:Name`) → schedules a
`Dispatcher.UIThread.Post(... grid.ScrollIntoView(vm.SelectedResult))`
at `Background` priority so the call runs after the DataGrid has
materialized its row containers (otherwise `ScrollIntoView` no-ops on
unrealized rows). Defensive try/catch covers recycled-grid /
missing-row cases.

### Tests

3 new in `CeXmlExportServiceTests` covering:
- ObjectProperty with resolved instance → GroupHeader + Offsets=[0] +
  children at natural offsets
- ObjectProperty with mismatched resolved-instance dict → falls back to
  flat leaf (no leaked children)
- Drilled children of common scalar types (FloatProperty / IntProperty)
  emit as proper leaves

**Build #541, 493 tests passing** (was 490).

-----

## 2026-05-09 (mid-latest) — CE Field/XML ObjectProperty leaf shape fix

`fix(export): emit ObjectProperty/ClassProperty/WeakObjectProperty as 8-Byte leaf`
([`CeXmlExportService.cs::MapCeField`](../ui/UE5DumpUI/Services/CeXmlExportService.cs),
build 534+)

Reported via Copy CE Field on `LocationInfo.ScalabilityModifiers`
(ObjectProperty): the resulting CE XML entry contained
`<GroupHeader>1</GroupHeader>` and **no `<VariableType>`** —
CE rendered it as an empty folder rather than a readable pointer:

```xml
<CheatEntry>
  <Description>"ScalabilityModifiers"</Description>
  <ShowAsHex>1</ShowAsHex>
  <GroupHeader>1</GroupHeader>     ← wrong: leaf is a folder
  <Address>+2C8</Address>
                                   ← wrong: missing <VariableType>
</CheatEntry>
```

**Root cause:** `MapCeField` had `TextProperty` / `Soft*Property` /
`LazyObjectProperty` / `InterfaceProperty` mapped to `("8 Bytes",
ShowAsHex: true)`, but **`ObjectProperty` / `ClassProperty` /
`WeakObjectProperty` were missing**. With `ceField == null`, `EmitFields`
fell through to `IsNavigable` → `EmitNavigableField` →
`EmitGroupPlaceholder`, which emits the GroupHeader-without-VariableType
shape. That code path was originally meant for *struct* navigation (no
scalar value, just a folder) — using it for raw object pointers
produced the buggy output.

**Fix:** add `ObjectProperty` / `ClassProperty` / `WeakObjectProperty`
to `MapCeField` returning `("8 Bytes", ShowAsHex: true)`. They now
emit through `EmitLeaf` and produce the same shape the soft / weak /
interface pointer types already produced:

```xml
<CheatEntry>
  <Description>"ScalabilityModifiers"</Description>
  <ShowAsHex>1</ShowAsHex>
  <ShowAsSigned>0</ShowAsSigned>
  <VariableType>8 Bytes</VariableType>
  <Address>+2C8</Address>
</CheatEntry>
```

Also covers null-pointer ObjectProperty fields (where `IsNavigable`
returns false because `PtrAddress` is empty) — they used to silently
drop from the export entirely.

**Tests:** 4 regression tests in `CeXmlExportServiceTests` covering
ObjectProperty / ClassProperty / WeakObjectProperty leaf shape and
null-ObjectProperty still-emits-leaf cases. **490 tests passing**
(was 486).

-----

## 2026-05-09 (mid-late) — Soft array CE XML: per-element FName leaf emission

`feat(export): TArray<TSoftObjectPtr> per-element CE XML group with FName leaf`
([`Ubel.cpp`](../dll/src/Ubel.cpp) Phase G, [`Fern.cpp`](../dll/src/Fern.cpp) array
JSON, [`CeXmlExportService.cs`](../ui/UE5DumpUI/Services/CeXmlExportService.cs),
build 532+)

Soft arrays (`TArray<TSoftObjectPtr>` / `TArray<TSoftClassPtr>`) used to
collapse to a single `8 Bytes` hex leaf per element in CE XML — only
`FWeakObjectPtr.ObjectIndex + SerialNumber` was addressable, and the
asset path FName at `+0x10` was invisible. Practical result: users had
to manually re-poke offsets in CE every time they wanted to read the
asset reference.

**Element layout the new emission writes** (DLL-provided `fnameSize` +
`isTopLevelAssetPath` flag let the exporter pick the right offsets per
UE version / CasePreservingName):

```
+0x00  WeakPtr   (8 Bytes hex)        — FWeakObjectPtr {ObjectIndex, Serial}
+0x10  AssetPath (4 Bytes, FName index) — UE4 / UE5.0
       PackageName (4 Bytes)            — UE5.1+ FTopLevelAssetPath
+0x10 + fnameSize  AssetName (4 Bytes)  — UE5.1+ only
```

Wire-up:
- **`Ubel.h`** gains two LiveFieldValue fields: `softArrayFNameSize`
  (8 normal / 16 case-preserving) and `softArrayIsTopLevelAssetPath`
  (true for UE >= 5.1). Stamped by both Phase G call sites (FProperty
  and UProperty fallback) so the metadata is present even when the
  array is empty.
- **`Ubel.cpp` Phase G reader** also writes
  `elem.rawIntValue = AssetPathName.ComparisonIndex` so the CE XML
  exporter can build a shared `<DropDownList>` mapping the FName index
  to the resolved asset path string (CE shows the path text in the
  Value column rather than a bare uint32).
- **`Fern.cpp`** serializes the two new fields as `soft_fname_size` and
  `soft_top_level_asset_path` on each ArrayProperty JSON object.
- **`DumpService.cs` + `LiveFieldValue.cs`** parse them into
  `SoftArrayFNameSize` / `SoftArrayIsTopLevelAssetPath`. The container
  navigation clone in `LiveWalkerViewModel` and the flatten clone in
  `CeXmlExportService.ResolveStructFieldsAsync` both forward the new
  fields so they survive the in-UI copies.
- **`CeXmlExportService.EmitSoftObjectArrayProperty`** is the new
  per-element emission. The outer array group keeps
  `Address=+{fieldOffset}, Offsets=[0]` (deref `TArray.Data`), each
  element becomes a sub-group at `+{N * elemSize}` containing the
  WeakPtr leaf, the AssetPath/PackageName FName leaf (with shared
  DropDownList), and on UE5.1+ also the AssetName leaf at
  `+0x10 + fnameSize`. Element description includes the resolved path
  (`[0] /Game/Items/IT_Potion.IT_Potion`).

Backwards-compat: when `SoftArrayFNameSize == 0` (legacy DLL or
deserialized payload without the new fields), the emission falls
through to the original 8-byte-hex path so older CE XML exports stay
readable.

**Tests:** 4 new + 1 backwards-compat case in
`CeXmlExportServiceTests.cs` covering UE4/UE5.0, UE5.1+ TopLevelAsset,
UE5.5+ CasePreservingName, SoftClassProperty, and the legacy fallback.
**486 tests passing** (was 483).

-----

## 2026-05-09 (mid) — OptionalProperty\<String/Name/Text\> intrusive specialization fix

`fix(walker): OptionalProperty intrusive isSet detection + value surfacing`
([`Ubel.cpp`](../dll/src/Ubel.cpp), build 530+)

Surface-tested OptionalProperty\<Struct\> on ES2 and noticed
`OptionalText` showing `(set)` despite the hex dump being all zeros,
while `OptionalString` neighbour with `Max=-1` correctly showed
`(unset)`. Investigation:

The `bIsSet` trailing-byte read at `field + sizeof(T)` is wrong for
heap-backed types — UE specializes `TOptional<T>` via
`FIntrusiveUnsetOptionalState` (see
[Misc/Optional.h `FOptional::IsSet`](../vendor/UnrealEngine/Engine/Source/Runtime/Core/Public/Misc/Optional.h#L26-L37))
so the "set" flag lives *inside* T's normal fields rather than as a
trailing byte. Reading past T lands on the next UPROPERTY's memory and
produces both false positives and false negatives depending on neighbour
layout. The 4 ES2 `OptionalPropertyTestObject` test fields all aliased
each other:

| Field        | innerSize used | bIsSet read addr   | Lands on...                                |
|--------------|---------------:|--------------------|--------------------------------------------|
| OptionalString @0x28 | 16 | 0x38 (next field start) | OptionalText TextData byte 0 = 00 ✓ unset |
| OptionalText @0x38   | 16 | 0x48 (next field start) | OptionalName ComparisonIndex = 0xFF ❌ false-positive set |
| OptionalName @0x48   | 8  | 0x50 (next field start) | OptionalInt int byte 0 = 00 ✓ unset |
| OptionalInt @0x50    | 4  | 0x54 (4 bytes in)       | trailing pad = 00 ✓ unset |

Fix: dispatch by inner type *before* the trailing-bIsSet fallback, with
sentinel checks lifted directly from each type's
`UEOpEquals(FIntrusiveUnsetOptionalState)`:

| Inner type      | Sentinel check                                | Source ref |
|-----------------|-----------------------------------------------|------------|
| `StrProperty`   | `int32` at field+12 (`FString::Max`) == `-1`  | [UnrealString.h.inl:212](../vendor/UnrealEngine/Engine/Source/Runtime/Core/Public/Containers/UnrealString.h.inl#L212) |
| `NameProperty`  | `uint32` at field+0 (`FName::ComparisonIndex`) == `0xFFFFFFFF` | [NameTypes.h:76](../vendor/UnrealEngine/Engine/Source/Runtime/Core/Public/UObject/NameTypes.h#L76) |
| `TextProperty`  | `uintptr_t` at field+0 (`FText::TextData`) == `nullptr`       | [Internationalization/Text.h:837](../vendor/UnrealEngine/Engine/Source/Runtime/Core/Public/Internationalization/Text.h#L837) |

When set, the resolved contents are surfaced via the existing
`ReadFString` / `ReadFName` helpers and rendered as `"FooBar"` / `Bar`
instead of the placeholder `(set)`. `fv.strValue` is already wired
through `Fern.cpp::str_value` JSON, so the UI picks up the new field
without changes.

Other primitive scalar inners (Int/Float/Bool/Byte/Enum) don't have
intrusive specializations and stay on the trailing-bIsSet path —
that path is correct because those types leave at least one trailing
byte for the flag (e.g. `TOptional<int32>` is 8B).

`fi.Size` for these intrusive Optionals is reported as `sizeof(T)` (no
trailing flag), so the 64B hex cap stays correct without tweaking.

**Build / tests:** clean build #530, 483 tests passing.

-----

## 2026-05-09 (later) — OptionalProperty\<Struct\> drill-down + Find Refs descent

`feat(walker): OptionalProperty<Struct> inner sub-field surfacing`
([`Ubel.cpp`](../dll/src/Ubel.cpp), [`Aura.cpp`](../dll/src/Aura.cpp), build 528+)

`OptionalProperty<StructProperty>` was the highest-impact gap remaining
after the 2026-05-09 morning session — ES2 alone has 5 real game-class
cases (`WorldPartitionRuntimeCellData.CellBounds`,
`FontFace.PlatformRasterization`,
`MapScalabilityModifierComponent.VolumetricFog...`) plus the
`OptionalPropertyTestObject` test fixtures.

**WalkInstance change** ([`Ubel.cpp`](../dll/src/Ubel.cpp)):
The OptionalProperty handler already determined `isSet` via the trailing
`bIsSet` byte for non-pointer inners. A new branch runs after `isSet` is
known and before the display-string switch:

- Probe `FStructProperty::Struct` (UScriptStruct\*) on the inner
  FProperty, mirroring the single-value StructProperty handler's probe
  list (`{0, ±4, ±8, ±0x10}`) so a mis-detected `FSTRUCTPROP_STRUCT`
  self-corrects.
- When set, populate the standard
  `{structClassAddr, structDataAddr, structTypeName}` triple. The UI's
  existing `LiveWalkerViewModel.NavigateToFieldAsync` already routes
  these to `WalkInstanceAsync(structDataAddr, structClassAddr)` — no UI
  change needed for drill-down.
- Generate the inline preview from the cached `WalkClass(struct)` via
  one bulk-read of the struct bytes, formatting up to `previewLimit`
  scalar sub-fields (`{X=10.5, Y=200, ...}`). Same pattern as the bare
  `StructProperty` handler at line ~3861.
- Hex display cap raised from 64B → 256B for struct inners so
  `sizeof(TOptional<FBox>)` etc. fit comfortably.

Layout reminder: `TOptional<T>` for struct `T` is **always
non-intrusive** — `{ T value; uint8 bIsSet; }` — so the value lives at
field+0 (same as the bare struct case). The intrusive layout only
applies to pointer-shaped `T` (Object/Class/Interface, Weak/Soft/Lazy)
where null/zero is the unset sentinel.

Unset slots cleanly take the existing `(unset)` path because
`structDataAddr`/`structClassAddr` are only populated when `isSet`.

**Find Refs / Address Finder descent** ([`Aura.cpp`](../dll/src/Aura.cpp)):
Both `CollectContainersRecursive` (Address Finder) and
`CollectRefMetaRecursive` (Find Refs v3) gained a parallel
`OptionalProperty + StructProperty` branch alongside the existing
`StructProperty` descent. The recursion walks through the inner
UScriptStruct at the same `absOffset` (TOptional value sits at field+0),
so a UObject pointer buried inside an `Optional<Struct>` now surfaces
in the reverse scan with the dotted name `Field.SubField`. Depth cap of
3 still applies.

**Build / tests:** clean build, 483 tests passing.

-----

## 2026-05-09 — Find Refs v2/v3, OptionalProperty, Property Search UX, Class Structure fixes

Session focused on closing reverse-reference coverage gaps and fixing UI
papercuts in the Property Search / Game Class / Class Structure tabs.

### Reverse Reference Search (Find Refs)

`Aura::FindReferencesToUObject` walks every UObject's pointer-shaped
fields (and the containers that hold them) to answer "who logically owns
this object?" — UE's `OuterPrivate` is a naming hierarchy, not a gameplay
hierarchy, so for runtime-spawned objects the reverse scan is the only
way to surface real ownership.

**Find Refs v2** (build 511+, [`394a285`](../dll/src/Aura.cpp)):
extended from "ObjectProperty/ClassProperty + TArray<UObject*>" to:
- Direct `Object/Class/Interface` (8B raw pointer at field+0)
- Direct `Weak/Soft{Class}/Lazy` (resolves embedded `FWeakObjectPtr`)
- `TArray` of any of the above
- `TMap<UObject*, V>` / `TMap<K, UObject*>` (allocated slots only)
- `TSet<UObject*>` (allocated slots only)

**Find Refs v3** (build 519+, [`7efe862`](../dll/src/Aura.cpp)):
added delegate / multicast target scan:
- `DelegateProperty` (single `FScriptDelegate` — `FWeakObjectPtr` target
  at field+0)
- `MulticastInlineDelegateProperty` / `MulticastDelegateProperty`
  (FMulticastScriptDelegate is `TArray<FScriptDelegate>` at field+0;
  walks each binding's `FWeakObjectPtr`)
- `TArray<FScriptDelegate>` via `ArrayProperty<DelegateProperty>`

Stride for `FScriptDelegate` derives from `DynOff::bCasePreservingName`
(16 or 24) at runtime, so case-preserving builds compute the right
per-element step.

`MulticastSparseDelegateProperty` is **not** covered — bindings live in
CoreUObject's global `FSparseDelegateStorage`
(`TMap<FObjectKey, TMap<FName, TSharedPtr<FMulticastScriptDelegate>>>`),
not at the field. The AOB to locate that storage is universal (it's UE
engine code, same as GObjects/GNames/GWorld). The blocker is the
read-side TMap walk, not finding the address.

### OptionalProperty (UE 5.2+)

`feat(walker): OptionalProperty drill-down + Find Refs coverage`
([`8f52f63`](../dll/src/Ubel.cpp))

`TOptional<T>` ships in two layouts depending on `T`:
- **Intrusive** (UE 5.4+ for pointer types `Object/Class/Interface` and
  the `FWeakObjectPtr`-shaped `Weak/Soft/Lazy`): `T` directly at field+0,
  null/zero is the unset sentinel. `sizeof(TOptional<T>) == sizeof(T)`.
- **Non-intrusive** (older + non-pointer T): `{ T value; uint8 bIsSet; }`
  with the trailing flag at `field + sizeof(T)`.

`WalkClassEx` probes `FOptionalProperty::ValueProperty` using the same
`FARRAYPROP_INNER` offset (FOptional + FArray have the same shape:
`FProperty + FProperty*`), populating `innerType` /
`innerStructType` / `innerObjClass`.

`WalkInstance` dispatches by inner type:
- Object/Class/Interface: read pointer at field+0; null = unset
- Weak/Soft/Lazy: `{ idx=0, serial=0 }` = unset
- Scalar: trailing `bIsSet` at field+`ResolveInnerSize(inner)`
- Display: `(unset)` or rendered inner value

Find Refs reuses `directPointers` / `weakLikePointers` because the
intrusive layout puts T at field+0 — the comparison is identical to the
bare T. `fieldType` is reported as `OptionalProperty` so the user sees
the Optional wrapper in the result.

**Verified on Everspace 2** (UE 5.4): `OptionalPropertyTestObject`'s 4
test fields (Str/Text/Name/Int) plus 5 game-class StructProperty inners
(`WorldPartitionRuntimeCellData.CellBounds`, `FontFace.Platform...`,
`MapScalabilityModifierComponent.VolumetricFog...`).

### MulticastSparseDelegateProperty bound-flag surfacing

`feat(walker): MulticastSparseDelegateProperty bound-flag surfacing`
([`600045f`](../dll/src/Ubel.cpp))

Sparse multicast delegates were falling through the generic scalar
handler and rendering as garbage hex. Added a field-level handler that:
- Reads `bIsBound` byte at field+0
- Displays `(sparse, bound — bindings in FSparseDelegateStorage)` or
  `(sparse, unbound)`
- Hex over reported size (defensively capped at 16B)
- Leaves `arrayCount=0` so `IsContainerNavigable` stays false

Binding enumeration (drill-down into individual `FScriptDelegate`s) is
queued for v4 — needs the storage AOB + nested TMap + TSharedPtr walk.

### Property Search panel UX

Three usability fixes pushed in successive commits because they all hit
the same "find every OptionalProperty in this game" workflow:

**Type filter exposed + type-only queries allowed**
([`b461c40`](../dll/src/Fern.cpp))

The DLL backend (`Aura::SearchProperties`) and `DumpService` already
supported a `types` filter, but the UI never surfaced it. Added a Type
filter input. Also relaxed the empty-query check in
`Fern.cpp::CMD_SEARCH_PROPERTIES` — name OR types must be set, not
strictly name. SearchProperties already tolerates an empty substring
(empty `find` returns 0 always).

**Type filter autocomplete + client-side result filter +
ObjectTree suggestions refresh**
([`67eaa62`](../ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs))

- **AutoCompleteBox** for the Type filter, backed by a curated 32-entry
  `PropertyTypeSuggestions` list. Typing "opt" surfaces
  `OptionalProperty`; "del" surfaces all four delegate variants; "weak"
  surfaces `WeakObjectProperty`. Comma-separated multi-type input still
  parsed in the VM.
- **Client-side result filter**: a new `ResultFilter` TextBox under the
  search bar. `ApplyResultFilter` walks a private `_allResults` cache
  and rebuilds `Results` with case-insensitive substring match across
  Class / Property / Type / Super / Preview. 150 ms debounce on the
  partial-changed hook so per-keystroke rebuild doesn't churn the
  ObservableCollection. VM now `IDisposable`.
- **ObjectTree.SearchSuggestions refresh**: dropped A/U-prefixed
  duplicates (`ACharacter`/`Character`, `APawn`/`Pawn`,
  `UAttributeSet`/`AttributeSet` — UE introspection drops prefixes so
  the A/U variants never matched), added universally-useful entries
  (`GameInstance`, `World`, `Level`, `SaveGame`, `GameplayAbility`,
  `GameplayEffect`, `AnimInstance`, ...). 30 categorized entries:
  GAS / Components / Player & Character / Game Framework /
  World+Level / UMG.

### Class Structure / Game Class fixes

**Find Refs reverse Open auto-scroll**
([`a5634b9`](../ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs))

After Open from a Find Refs row the holding field was selected but the
DataGrid stayed at the top. Avalonia's DataGrid only auto-scrolls on
user-driven selection, not on programmatic `SelectedItem` assignment —
raise `ScrollToFieldRequested` so the View calls `ScrollIntoView`,
matching the path edit-commit and inline drill navigation already use.

**Class Structure flash-then-blank** (build 524,
[`449f4e4`](../ui/UE5DumpUI/ViewModels/ClassStructViewModel.cs))

ClassStructPanel briefly showed the clicked object's class data and
then went blank. Avalonia's ListBox raises `SelectionChanged` with null
whenever its `ItemsSource` mutates (filter typing, fresh load,
suggestion auto-selection), and the MainWindow handler dutifully
forwarded that null to `ClassStruct.OnObjectSelected`, which then set
`HasClass=false` and cleared `Fields`. Fix: treat null as
"selection cleared, but keep showing what we last loaded" — the user
already picked a class, the IDE-style transient selection wobble
shouldn't undo that. Plus dedupe consecutive selections of the same
node via a private `_lastLoadedNodeAddress`.

**Class Structure: route class-like nodes to themselves**
(build 525, [`89f637b`](../ui/UE5DumpUI/ViewModels/ClassStructViewModel.cs))

Even after fixing the null-fire blank, clicking `LocalPlayer` (or any
other UClass) in the ObjectTree still showed `//Script/CoreUObject/Class`
with 0 fields — `GetObjectAsync` on a UClass returns its metaclass
(UClass-of-Class), and walking that metaclass yields an empty FProperty
chain because UClass's data lives in native C++ members rather than
UPROPERTY-tagged ones. Fix: detect class-like nodes by ClassName —
anything ending in `Class` (Class, BlueprintGeneratedClass,
WidgetBlueprintGeneratedClass, AnimBlueprintGeneratedClass, ...) plus
`ScriptStruct` / `UserDefinedStruct` / `Enum` / `UserDefinedEnum` /
`Function` / `DelegateFunction` — and walk `node.Address` directly.
Only proper instances go through `GetObjectAsync`.

**Game Class: auto-run Find Instances pre-fill** (`449f4e4`)

`PropertySearch` and `GameClassFilter` both raised
`NavigateToInstanceFinder`, which switched to the Instance Finder tab
and pre-filled `SearchClassName` — but stopped short of running the
query. Trigger `SearchCommand` immediately after pre-fill (with
`CanExecute` guard) so clicking "Find Instances" produces results
without an extra Search click.

**Game Class: add Package column** (`89f637b`)

The Super filter aligned with a Super column, but the Package filter
had no matching column — it was prefix-matching against ClassPath,
which displayed only the full `/Script/Engine.Actor` form. Added a
"Package" column showing the extracted prefix (`/Script/Engine`,
`/Game`, `/Script/ES2`, ...) next to Super and Path. Moved
`ExtractPackagePrefix` from the VM onto `GameClassEntry` so the column
binding and the filter logic share one implementation.

-----

## Capability matrix (current — build 544)

| Layer | Drill-down | Find Refs |
|-------|-----------|-----------|
| Object / Class / Interface | ✅ | ✅ |
| Weak / Soft{Class} / Lazy (single + array) | ✅ | ✅ |
| TArray of any pointer-shaped inner | ✅ | ✅ |
| TMap / TSet (Object/Class) | ✅ | ✅ (allocated slots only) |
| Delegate (single FScriptDelegate) | ✅ | ✅ (v3) |
| MulticastInline / MulticastDelegate | ✅ | ✅ (v3) |
| TArray<FScriptDelegate> | ✅ | ✅ (v3) |
| MulticastSparseDelegate | ⚠️ bound flag only | ❌ (needs FSparseDelegateStorage AOB + walk) |
| OptionalProperty\<pointer / weak\> | ✅ | ✅ |
| OptionalProperty\<scalar Int/Float/Bool/Byte/Enum\> | ✅ trailing-bIsSet | — |
| OptionalProperty\<String / Name / Text\> | ✅ intrusive sentinel + value (build 530) | — |
| OptionalProperty\<Struct\> | ✅ (build 528) | ✅ depth-3 descent through inner struct (build 528) |
| FieldPathProperty | ❌ | ❌ |
| TMap / TSet with weak-like inner sides | — | ❌ (v4 candidate) |

## Remaining gaps (next-session pickup candidates)

1. **MulticastSparseDelegateProperty bindings list** — bound flag is
   shown; full enumeration needs:
   - Universal AOB for `FSparseDelegateStorage::SparseDelegates` static
     map (effort: similar to existing 128 AOB patterns)
   - Nested `TMap<FObjectKey, TMap<FName, TSharedPtr<...>>>` walk
     (effort: NEW — TMap walking infra is mostly there but `FObjectKey`
     hashing + `TSharedPtr` control-block reading aren't)

2. **Find Refs v4** —
   - `TMap` / `TSet` with weak-like inner sides (currently Object/Class
     only)
   - MulticastSparseDelegate target scan (needs the storage AOB above)

3. **Find Refs auto-drill into array/map/set element [N]** — currently
   auto-scrolls to the container field, but the user has to click drill
   manually to reach the specific element.

4. **`FieldPathProperty`** — rare, low priority.

5. **GWorld**: Star Wars Jedi untested, Satisfactory fails.

6. **UE version misdetection** on some UE4 games (DQ I&II,
   Ghostwire: Tokyo show UE505 incorrectly).

## Tested games (last verified 2026-05-09)

- **Everspace 2** ✅ (UE 5.4): item template ID via container scan; Find
  Refs v3 returns 9 correct references in 224ms (cache hot, scan
  complete: 1180536/1180536); auto-scroll-to-field after Open works;
  Class Structure for `LocalPlayer` shows correct fields after the
  class-like routing fix; PropertySearch type filter `OptionalProperty`
  finds 9 matches across 5 real classes + 4 test-object fields.
- **DQ I&II HD-2D / FF7 Rebirth** (UE4): Char Lv / Cloud HP / Party Lv
  in non-reflected memory (custom allocator, high-memory region
  0x255*/0x296*/0x7FEF*). Container scan complete, 0 matches — out of
  reflection scope. Use CE pointer scan for these.
