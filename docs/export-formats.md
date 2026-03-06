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

When enabled, non-root GroupHeader nodes with `Offsets=[0]` emit:
```xml
<Options moHideChildren="1" moDeactivateChildrenAsWell="1"/>
```

### Container View Export

When the current view is a container (Array/Map/Set element list):
- Strip the container breadcrumb from the XML path
- Use the parent's Address + the ContainerField for emission
- Prevents false cycle detection (container breadcrumbs share parent address)

---

## CSX Export

**Output**: File (`.CSX`, CE Structure Dissect XML)
**Service**: `CsxExportService.cs`
**Entry point**: `ExportCsxAsync`

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

### StructProperty Flattening

Same as CE XML: pre-resolve via `ResolveStructFieldsAsync()`, flatten with `{StructType} / {FieldName}` naming.

### StrProperty Special Case

Emits as `Vartype="Pointer"` with a child structure containing:
```xml
<Element Offset="0" Vartype="Unicode String" Bytesize="18" Description="Value"/>
```

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

### Features

- Sorts fields by offset, inserts padding for gaps
- Queries superclass name via `WalkClassAsync()`
- BoolProperty with bitmask: appends `[Mask: 0xXX]` comment
- Unknown types: `uint8_t[0x{size:X}]` blob

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
| EnumProperty | 4 Bytes | | | + DropDownList |
| StrProperty | String | | | Unicode=1, Offsets=[0] |
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
| StrProperty | Pointer | 8 | unsigned integer |
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
