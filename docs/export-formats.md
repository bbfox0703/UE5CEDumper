# Export Formats — CE XML, CSX, SDK Header

This document describes how LiveWalker exports field data to Cheat Engine and C++ formats.

---

## Table of Contents

- [CE XML Export](#ce-xml-export)
- [CSX Export (CE Structure Dissect)](#csx-export)
- [SDK Header Export (.h)](#sdk-header-export)
- [Type Mapping Reference](#type-mapping-reference)

---

## CE XML Export

**Output**: Clipboard (paste into CE address list)
**Service**: `CeXmlExportService.cs`
**Entry points**: `ExportCeXmlAsync`, `ExportCeFieldXmlAsync` (single field)

### Hierarchical Pointer Chain Model

CE XML uses nested `<CheatEntry>` nodes where each child's address is **relative to its parent's resolved address**.

```
GWorld → PersistentLevel → Actor → Component → Field
  │           │                │          │         └── Address=+{offset}  (leaf, no Offsets)
  │           │                │          └── Address=+{offset}, Offsets=[0]  (pointer deref)
  │           │                └── Address=+{offset}, Offsets=[0]
  │           └── Address=+{offset}, Offsets=[0]
  └── Address="module.exe"+RVA  (root, absolute)
```

### Address Rules

| Node Type | Address Tag | Offsets Tag | CE Resolution |
|-----------|-------------|-------------|---------------|
| **Root** | `"module.exe"+RVA` or hex | (none) | Absolute address |
| **Pointer breadcrumb** | `+{fieldOffset}` | `<Offset>0</Offset>` | `*(parent + offset)` then children offset from result |
| **Inline breadcrumb** (struct) | `+{fieldOffset}` | (none) | `parent + offset` |
| **Leaf field** (scalar) | `+{fieldOffset}` | (none) | `parent + offset` |
| **TArray group** | `+{fieldOffset}` | `<Offset>0</Offset>` | Dereferences `TArray.Data*` |
| **TArray element** | `+{N * elemSize}` | (none) | Offset from Data pointer |
| **TMap/TSet group** | `+{fieldOffset}` | `<Offset>0</Offset>` | Dereferences `TSparseArray.Data*` |
| **TMap/TSet element** | `+{sparseIndex * stride}` | (none) | Offset from Data pointer |

> **Critical rule**: Never emit `Address=+0, Offsets=[fieldOffset]` — this causes a double-dereference bug. Always use `Address=+{offset}` with `Offsets=[0]` for pointer deref.

### Breadcrumb → XML Mapping

1. **Root** (breadcrumbs[0]): Absolute address, `GroupHeader=1`
2. **Intermediate** (breadcrumbs[1..n-1]):
   - `Address=+{FieldOffset}`, `GroupHeader=1`
   - If `IsPointerDeref` or `IsContainerView` → add `Offsets=[0]`
3. **Leaf fields** (current Fields list): `EmitFields()` handles per-type

### CleanBreadcrumbs (Cycle Removal)

Before XML generation, breadcrumbs are cleaned to remove navigation cycles:

```
[A, B, C, A, B] → detects A at [0] and [3] → removes [0..3] → [A, B]
```

This prevents deeply nested duplicate pointer chains from Outer→child→Outer loops.

### StructProperty Expansion

**Pre-resolution** via `ResolveStructFieldsAsync()`:
- For each StructProperty with valid `StructClassAddr`: call `WalkInstanceAsync()` via DLL
- Recursively flatten nested structs with dot-prefixed names (max depth 5)
- Result: `resolvedStructs[offset]` → list of flattened sub-fields

**Emission**:
- Struct group: `Address=+{structOffset}`, NO Offsets (inline, not pointer)
- Children: `Address=+{child.Offset}` with proper type mapping

### ArrayProperty Handling

**Scalar arrays** (Float, Int, Bool, Enum, Name):
- Group: `Address=+{fieldOffset}, Offsets=[0]` (deref TArray.Data)
- Per-element: `Address=+{N * elemSize}` (no Offsets)
- Element naming: `[N]`, `[N] EnumName`, or `[N] PtrName (ClassName)`

**Struct arrays** (Phase F with sub-fields):
- Group: `Address=+{fieldOffset}, Offsets=[0]`
- Per-element group: `Address=+{N * elemSize}`, NO Offsets (inline)
- Sub-fields: `Address=+{subFieldOffset}` (relative to element)

### MapProperty / SetProperty Handling

Uses `TSparseArray` addressing:

```
stride = AlignUp(elemSize, 4) + 8    // +8 for HashNextId(4) + HashIndex(4)
elementAddr = sparseIndex * stride
```

- Group: `Address=+{fieldOffset}, Offsets=[0]`
- Per-element: `Address=+{sparseIndex * stride}`
- Map values: offset by `keySize` within each element

### DropDownList (Enum Support)

- **First occurrence** of a UEnum: parent emits `<DropDownList DisplayValueAsItem="1">` with `value:name` pairs
- **Subsequent occurrences**: parent emits `<DropDownListLink>` referencing the first description
- Tracked by `enumAddr` to avoid duplicates

### CollapsePointerNodes Option

When enabled, **every non-root GroupHeader folder** emits the collapse Options —
pointer/array deref nodes, struct groups, AND array/map/set element folders like
`[1]` (the root keeps an absolute address, so it stays expanded):
```xml
<Options moHideChildren="1" moDeactivateChildrenAsWell="1"/>
```

### Collapse chain Option (Copy CE XML / Copy CE Field)

When enabled (`flattenChain` / LiveWalker "Collapse chain" toggle), the navigation
**spine** between `base` and the target field is folded into a **single CE
multi-level-pointer entry** instead of one nested group per breadcrumb. `base`,
the target field, and the field's drill-down are untouched — only the GWorld→…→target
pointer hops collapse. This is purely a transform of the breadcrumb spine; the
leaf-field subtree (struct/pointer/container expansion via `EmitFields`) is never
touched, so the toggle composes with every field type and can't change what a leaf
renders. Implemented by `CeXmlExportService.FoldBreadcrumbSpine`.

**Fold math** (`ProjectBreadcrumb` reduces each breadcrumb to `(offset, derefAfter)`;
`derefAfter = IsPointerDeref || IsContainerView`). CE resolves `Address=+Xbase`,
`Offsets O[0..m-1]` as `p = deref(parent + Xbase); for k=m-1..1: p = deref(p + O[k]); final = p + O[0]`
— so `O[0]` is the **outermost** (no final deref) and `O[m-1]` the first deref after
base. Folding accumulates each run of offsets up to (and incl.) a deref into `D[]`,
with `F` = the trailing inline run after the last deref:

```
Address = +D[0]
Offsets (CE document order) = [F] ++ reverse(D[1..])
```

A pure-inline spine (no deref) folds to `Address=+F` with no `<Offsets>`.

**Worked example** — `base → OwningGameInstance(ptr +180) → m_savedata(ptr +2A8) →
SaveSlotList(array +7D0) → [1](inline +6F8) → OriginalPlayer(inline +18)`:
`D=[180, 2A8, 7D0]`, `F = 6F8 + 18 = 710` →

```xml
<Description>"OwningGameInstance ▸ m_savedata ▸ SaveSlotList ▸ [1] ▸ OriginalPlayer"</Description>
<Address>+180</Address>
<Offsets>
  <Offset>710</Offset>   <!-- F: trailing inline run (array elem [1] + OriginalPlayer) -->
  <Offset>7D0</Offset>   <!-- SaveSlotList deref -->
  <Offset>2A8</Offset>   <!-- m_savedata deref (innermost, first after base) -->
</Offsets>
```

> Two adjacent **pointers** A→B fold to `Offsets=[0, OffsetB]` (the `0` first — it is
> B's post-deref read offset, the outermost `O[0]`). A pointer A then an **inline**
> struct B folds to `Offsets=[OffsetB]`. Offsets are emitted as summed hex.

### Container value drilldown (docs/ce-export-drilldown-spec.md)

Container element **values that are structs/objects** expand recursively, up to
the Drill Depth slider — `Map<Name, Struct>`, `Set<Struct>`, struct arrays, and
nested `struct → Map<…, Struct>`. The unified `ResolveDrilldownAsync` walks
structs (flatten, free) + pointers (cost 1 level) + container element values
(cost 1 level), and the emitters delegate each value back through `EmitFields`
so it reuses the struct/pointer/container emit paths. Depth is measured from the
**current view** (the GWorld→…→view breadcrumb costs nothing). At depth 0 the
export is flat (struct/object values fall back to a placeholder).

### Map key/value rendering

- **Value is label-only** (`Value`, not `Value: ms_stdag`) — the stored int is
  dynamic, so the resolved name is never baked into the description. Key leaves
  are likewise `Key`-only (the element folder `[N] {key}` shows the key for
  orientation).
- **Name/Enum values get a CE `DropDownList`** (rawInt → resolved name, parsed
  from the element's `ValueHex`) on the map group; each value leaf links to it
  via `DropDownListLink`, so CE shows the LIVE name for the current int.
- **Enum key/value widths** follow the real byte size (a 1-byte enum key is
  `Byte`, not `4 Bytes`).
- The DLL's map value offset uses the value property's real alignment
  (`Scharf::RequiredAlignment`), NOT a size guess — required for FName /
  FWeakObjectPtr (8 bytes but 4-aligned). A wrong guess corrupts every element.

### Container View Export

When the current view is a container (Array/Map/Set element list):
- Strip the container breadcrumb from the XML path
- Use the parent's Address + the ContainerField for emission
- Prevents false cycle detection (container breadcrumbs share parent address)
- **Struct-array elements**: a selected element is re-walked in full (Copy CE
  Field), so nested structs/maps inside it expand like drilling into it.

---

## CSX Export

**Output**: File (`.CSX`, CE Structure Dissect XML)
**Service**: `CsxExportService.cs`
**Entry point**: `ExportCsxPre77Async` / `ExportCsx77Async` (the Export CSX dropdown's two
items; both delegate to `ExportCsxCoreAsync(CsxFormat)`)

### CSX Format

```xml
<Structures>
  <Structure Name="ClassName_ObjectName" AutoFill="0" AutoCreate="1"
             DoNotSaveLocal="0" AutoDestroy="0" DefaultHex="0" AutoStructurizemask="0">
    <Element Offset="0" Vartype="4 Bytes" Bytesize="4" OffsetHex="00000000"
             DisplayMethod="unsigned integer" BackColor="80000005"
             Description="FieldName"/>
    <!-- ... more elements ... -->
  </Structure>
</Structures>
```

### Key Differences from CE XML

| Aspect | CE XML | CSX |
|--------|--------|-----|
| Address model | Hierarchical pointer chain | Flat offset list |
| Pointer deref | `Offsets=[0]` | CE native (Vartype="Pointer") |
| Nesting | `<CheatEntries>` children | `<Structure>` inside `<Element>` |
| Drilldown | No | Yes (configurable depth) |
| Output | Clipboard | File |

### Drilldown (drilldownDepth ≥ 1)

When enabled, pointer fields are resolved:

1. **ObjectProperty/ClassProperty**: If `PtrAddress` valid → `WalkInstanceAsync()` → build child `<Structure>` with resolved fields
2. **ArrayProperty/MapProperty/SetProperty**: Convert elements to synthetic fields → build child structure
3. **Cycle detection**: `visited` set prevents infinite loops on circular references
4. **Depth**: Each nesting level decrements depth; stops at 0

CSX shares the **unified `CeXmlExportService.ResolveDrilldownAsync`** resolver
(build 1098) — so container element values that are **structs** (`Map<…, Struct>`,
`Set<Struct>`) flatten inline just like CE XML, not as a raw byte blob.
`ConvertMapElementsToFields` / `ConvertSetStructElementsToFields` stamp each value's
absolute `StructDataAddr`/`StructClassAddr`, and `BuildLiveChildStructure` routes
`StructProperty` fields to `EmitStructPropertyFlattened` (`[idx] key / SubField`).
CSX additionally keeps its own pointer resolution for object-arrays / DataTable rows
/ multicast delegates (shapes the unified resolver doesn't descend; CE XML emits
those as flat leaves). A `⚠ Container element limit (N)` status note flags any
top-level container clipped by `ArrayLimit` so a partial export isn't mistaken for
a complete one.

### StructProperty Flattening

Same as CE XML: pre-resolve via the unified resolver (keyed by `StructDataAddr`),
flatten with `{StructType} / {FieldName}` naming.

### StrProperty Special Case

Emits as `Vartype="Pointer"` with a child structure containing a single string element:
```xml
<Element Offset="0" Vartype="Unicode String" Bytesize="18" Description="Value"/>
```

The child element's Vartype depends on the FString-family variant (all share the same
TArray header, only the char width differs):

| UE Property | Child Vartype | Note |
|-------------|---------------|------|
| `StrProperty` (FString, wchar_t*) | `Unicode String` | wide / UTF-16 |
| `Utf8StrProperty` (FUtf8String, UTF-8) | `String` | CE Structure Dissect has only `String` / `Unicode String` (no CodePage option), so UTF-8 multibyte cannot be decoded here — a CSX format limitation. Use CE XML (CodePage=1) for correct UTF-8 display. |
| `AnsiStrProperty` (FAnsiString, ANSI) | `String` | 1-byte ANSI |

### Format Version: Pre-CE-7.7 vs CE-7.7+ (bit-field bools)

The Export CSX button is a dropdown (`DropDownButton` + `MenuFlyout`) with two items, each
exporting in one of two CE format versions (enum `CsxFormat`, default `PreCe77`):

- **Pre-CE 7.7** (`CsxFormat.PreCe77`): CE before 7.7 has no bit-switch type in Structure
  Dissect, so a **bit-field** `BoolProperty` (`BoolBitIndex >= 0`) is emitted as a whole
  `Vartype="Byte"` with the bit noted in the description — `Description="bFlag (bit N, mask 0xNN)"`.
  Several bit-field bools on one byte become a series of same-address `Byte` elements.
- **CE 7.7+** (`CsxFormat.Ce77Plus`): emits a real bit switch, byte-identical to a native CE 7.7
  export (attribute order `Offset, BitSize, Vartype, BitStart, Bytesize, OffsetHex, Description,
  DisplayMethod`):
  ```xml
  <Element Offset="90" BitSize="1" Vartype="Binary" BitStart="2" Bytesize="1"
           OffsetHex="0000005A" Description="bCanBeDamaged" DisplayMethod="unsigned integer"/>
  ```

Rules (`EmitBinaryBitfieldElement`, gated in `EmitElement`):
- Only **bit-field** bools (`BoolBitIndex >= 0`) become `Binary`. A whole-byte bool
  (`BoolBitIndex == -1`, mask `0xFF`) stays `Byte` in **both** formats.
- Byte address = the EmitElement **`offset` parameter** (already absolute for flattened-struct /
  drilled-child fields — `field.Offset` would be struct-relative) **plus `BoolByteOffset`** (the
  byte within the field holding the bit), matching the DLL read/write path (`Ubel`/`Solitar`/
  `Wirbel` all use `base + Offset + ByteOffset`). `BoolByteOffset` is preserved through struct
  flattening by `CeXmlExportService.ResolveStructRecursiveAsync`'s reconstruction.
- `BitSize`/`BitLength` is always `1` (a UE `FBoolProperty` is single-bit by construction —
  `FieldMask` is a validated power-of-2). The Pre-7.7 `(bit N, mask)` description suffix is
  dropped in 7.7+ (`BitStart` carries it). Mirrors the proven Copy CE XML / Copy CE Field path
  (`CeXmlExportService.MapCeField`: `BoolProperty when BoolBitIndex >= 0 => Binary`).
- **Known limitation** (pre-existing, both formats): a bit-field bool inside a **struct-array**
  element emits as a plain `Byte` — `StructSubFieldValue` carries no bool mask, so there is no
  bit info to emit. Top-level, flattened-struct-member, and drilled-pointer-target bit-field
  bools all emit `Binary` correctly.

The two leading `CsxFormat` paths are the only consumers of `GenerateCsxAsync`'s `format` arg.
The other sample.CSX features the exporter does **not** yet emit (CE `Custom` /
`Customtype="UE FName to String"` FName decoding, `ChildStruct` by-name references,
`PreviewPriority`, per-field `String`, `RLECount`) are separate backlog items, not part of the
bit-switch format.

---

## SDK Header Export

**Output**: File (`.h`, C++ header)
**Service**: `SdkExportService.cs`
**Entry point**: `ExportSdkHeaderAsync`

### Format

```cpp
// Auto-generated by UE5CEDumper
// https://github.com/bbfox0703/UE5CEDumper

// /Game/Path/ClassName
struct ClassName : public SuperName
{
    int32_t     SortNo;             // 0x0010 (0x0004) IntProperty
    float       Price;              // 0x0014 (0x0004) FloatProperty
    bool        bEnabled;           // 0x0018 (0x0001) BoolProperty [Mask: 0x01]
    uint8_t     Pad_001C[0x0004];   // 0x001C (0x0004) PADDING

}; // Size: 0x0020
```

### The own-vs-inherited boundary — and why it needs TWO numbers

`walk_class` returns the **entire** SuperStruct chain, so the emitter must split own from
inherited or it re-declares every base property inside a `struct X : public Super` that
already inherits it (audit #5 W2). The boundary is the super's `PropertiesSize`, on the wire
as `super_props_size`.

⚠ **That number alone is one too HIGH when the super is an EMPTY USTRUCT** (audit A7). UE sets
a native struct's `PropertiesSize` from `CppStructOps->GetSize()`
(`CoreUObject/Private/UObject/Class.cpp:947`), so an empty base reports **1**, not 0 — while
C++ **empty-base optimisation** puts the derived struct's first member at offset **0**. A
`Offset >= super_props_size` split then dropped that member, and the trailing-padding pass
replaced it with `uint8_t Pad_0001[0x0003];`. UE 5.8.2 ships **62** empty USTRUCTs that are
inherited from, with **302** property-bearing children — `FEmptyPayload` (Engine module, no
editor guard), the `FMassFragment` family, `FEditorDataStorageColumn`.

So the DLL also sends `own_props_start`: the lowest `Offset` among the class's **own**
properties, captured in `Ubel::WalkClass` before the super chain is prepended. The emitter
takes the **lower** of the two.

- ⛔ `own_props_start` is **−1** when the class declares no properties of its own, or when an
  older DLL does not send it. A negative means **no information** and must fall back to
  `super_props_size`. Folding −1 or 0 into the comparison re-emits the whole inherited chain,
  which is W2 again and strictly worse than the bug A7 fixes.
- ⛔ It is only ever a **lower** floor, never a higher one.

### Empty structs are emitted EMPTY

A struct with no own properties and `PropertiesSize == 1` gets **no padding member**. Emitting
`uint8_t Pad_0000[0x0001];` would keep `sizeof` at 1 but make the struct **non-empty**, which
defeats empty-base optimisation in the *generated* C++ and puts every derived struct's first
member at 1 where the game has it at 0. `struct X {};` is already `sizeof` 1, so the
`// Size: 0x0001` comment stays honest. Deliberately narrow: an opaque struct with a real size
still gets its padding.

*Measured on MSVC (5 toolsets, `/std:c++17` and `c++20`, cross-checked with clang and with
`/d1reportSingleClassLayout`): a non-empty base **never** has its trailing padding reused —
that is the Itanium rule, and the same translation units compiled for Linux do show reuse — so
EBO is the only shape that intrudes on a derived member's offset.*

### Features

- Sorts fields by offset, inserts padding for gaps
- Queries superclass name via `WalkClassAsync()`
- BoolProperty with bitmask: appends `[Mask: 0xXX]` comment
- Unknown types (and an unresolved `StructProperty`): a `uint8_t Name[0x{size:X}];` blob. The extent
  goes **after** the identifier — C++ has no `uint8_t[0xN] Name;` form, and emitting one made every
  `OptionalProperty` in the header a syntax error. `MapCppDecl` returns the element type and the
  array suffix as separate halves so the two cannot be concatenated in the wrong order; a zero size
  degrades to `[0x1]` because MSVC rejects a zero-length array (C2466).

---

## Type Mapping Reference

### CE XML VariableType

| UE Property | CE VariableType | Signed | ShowAsHex | Special |
|-------------|-----------------|--------|-----------|---------|
| IntProperty | 4 Bytes | ✓ | | |
| Int8Property | Byte | ✓ | | |
| Int16Property | 2 Bytes | ✓ | | |
| Int64Property | 8 Bytes | ✓ | | |
| UInt32Property | 4 Bytes | | | |
| UInt16Property | 2 Bytes | | | |
| UInt64Property | 8 Bytes | | | |
| ByteProperty | Byte | | | |
| FloatProperty | Float | | | |
| DoubleProperty | Double | | | |
| BoolProperty (bit) | Binary | | | BitStart + BitLength=1 |
| BoolProperty (byte) | Byte | | | Fallback |
| NameProperty | 4 Bytes | | | FName index |
| EnumProperty | Byte / 2 / 4 / 8 Bytes | | | width = property byte size; + DropDownList |
| StrProperty | String | | | Unicode=1, CodePage=0, Offsets=[0] |
| Utf8StrProperty | String | | | Unicode=0, **CodePage=1**, Offsets=[0] (UE5.5+ FUtf8String) |
| AnsiStrProperty | String | | | Unicode=0, CodePage=0, Offsets=[0] (UE5.5+ FAnsiString) |
| ObjectProperty | 8 Bytes | | ✓ | Pointer |
| ClassProperty | 8 Bytes | | ✓ | Pointer |
| WeakObjectProperty | 8 Bytes | | ✓ | |
| InterfaceProperty | 8 Bytes | | ✓ | |
| TextProperty | 8 Bytes | | ✓ | Opaque |
| SoftObjectProperty | 8 Bytes | | ✓ | |

### CSX Vartype

| UE Property | Vartype | Bytesize | DisplayMethod |
|-------------|---------|----------|---------------|
| IntProperty | 4 Bytes | 4 | unsigned integer |
| FloatProperty | Float | 4 | unsigned integer |
| DoubleProperty | Double | 8 | unsigned integer |
| ByteProperty | Byte | 1 | unsigned integer |
| BoolProperty | Byte | 1 | unsigned integer |
| ObjectProperty | Pointer | 8 | unsigned integer |
| StrProperty | Pointer | 8 | unsigned integer | (child `Unicode String`) |
| Utf8StrProperty | Pointer | 8 | unsigned integer | (child `String`, byte — CSX has no CodePage) |
| AnsiStrProperty | Pointer | 8 | unsigned integer | (child `String`, byte) |
| ArrayProperty | Pointer | 8 | unsigned integer |
| MapProperty | Pointer | 8 | unsigned integer |
| TextProperty | 8 Bytes | 8 | hexadecimal |
| WeakObjectProperty | 8 Bytes | 8 | hexadecimal |
| (unknown) | Array of byte | size | hexadecimal |

### SDK C++ Types

| UE Property | C++ Type |
|-------------|----------|
| IntProperty | `int32_t` |
| FloatProperty | `float` |
| DoubleProperty | `double` |
| BoolProperty | `bool` |
| NameProperty | `FName` |
| StrProperty | `FString` |
| Utf8StrProperty | `FUtf8String` (UE5.5+) |
| AnsiStrProperty | `FAnsiString` (UE5.5+) |
| TextProperty | `FText` |
| ObjectProperty | `class {ClassName}*` |
| ClassProperty | `TSubclassOf<class {ClassName}>` |
| WeakObjectProperty | `TWeakObjectPtr<class {ClassName}>` |
| StructProperty | `struct {StructType}` or `uint8_t[size]` |
| ArrayProperty | `TArray<{InnerType}>` |
| MapProperty | `TMap<{KeyType}, {ValueType}>` |
| SetProperty | `TSet<{ElemType}>` |
| EnumProperty | `{EnumName}` or `uint8_t` |

---

## TSparseArray Stride Formula

Used by TMap and TSet element addressing:

```
stride = AlignUp(elemSize, 4) + 8
```

Where `+8` accounts for `HashNextId` (4 bytes) + `HashIndex` (4 bytes) appended by UE's `TSparseArray` allocator.

```
AlignUp(size, 4) = (size + 3) & ~3
```
