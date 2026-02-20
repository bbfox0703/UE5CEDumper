# UE4SS (RE-UE4SS) Analysis

> Research findings from analyzing UE4SS, Dumper-7, and UEDumper approaches to UE5 runtime reflection.
> Purpose: identify techniques applicable to UE5CEDumper, especially for UE5.5+/5.7 support.

-----

## Summary

| Area | UE4SS / Dumper-7 Approach | Our Approach | Assessment |
|------|--------------------------|--------------|------------|
| CasePreservingName | Compile-time flag (`WITH_CASE_PRESERVING_NAME`) + offset fixup | Runtime `DynOff` probing via known structs (Guid, Vector) | Ours is more flexible |
| Lua AOB Overrides | Not implemented in Dumper-7/UEDumper (C++ only) | Not yet | Future Phase 4+ |
| Property Iteration | `for (FField = ChildProperties; Field; Field = GetNext())` | Same pattern via UStructWalker with DynOff | Equivalent |
| CE Pointer Paths | Not generated | `get_ce_pointer_info` returns CE-compatible XML | We are ahead |
| GNames UE5.5+ | 6 AOB patterns + string ref fallback | 4 patterns + `.data` pointer-scan fallback | Both need improvement |

-----

## 1. CasePreservingName Handling

**Source**: `Dumper-7/Dumper/Engine/Private/OffsetFinder/OffsetFinder.cpp`

### Detection

Dumper-7 detects CasePreservingName by checking FName size:

```
FNameSize = Off::UObject::Outer - Off::UObject::Name
- If 0x10 -> CasePreservingName enabled (adds int32 DisplayIndex)
- If 0x08 -> Standard FName
```

Global flag: `Settings::Internal::bUseCasePreservingName`

### Offset Adjustment

When CasePreservingName is detected, Dumper-7 adds 0x8 to specific offsets:

```cpp
if (Settings::Internal::bUseCasePreservingName) {
    Off::FField::Flags += 0x8;
    Off::FFieldClass::Id += 0x08;
    Off::FFieldClass::CastFlags += 0x08;
    Off::FFieldClass::ClassFlags += 0x08;
    Off::FFieldClass::SuperClass += 0x08;
}
```

### Comparison with Our DynOff

Our approach (`ValidateAndFixOffsets()` in OffsetFinder.cpp) probes known structs (Guid with fields A/B/C/D, Vector with X/Y/Z) to discover correct offsets at runtime — no compile-time flag needed. More robust for unknown UE versions.

-----

## 2. Property Iteration (FField Chain)

**Source**: `Dumper-7/Dumper/Engine/Private/Unreal/UnrealObjects.cpp`

### Standard Loop Pattern

```cpp
for (UEFField Field = GetChildProperties(); Field; Field = Field.GetNext()) {
    if (Field.IsA(EClassCastFlags::Property)) {
        Properties.push_back(Field.Cast<UEProperty>());
    }
}
```

| Component | Offset | Purpose |
|-----------|--------|---------|
| `GetChildProperties()` | `UStruct + Off::UStruct::ChildProperties` | FField* chain head |
| `GetNext()` | `FField + Off::FField::Next` | Next in chain |
| `IsA()` | FFieldClass flags check | Type filtering |

### FFieldVariant Size (UE5.1.1 Change)

- **UE5.0-5.1.0**: FFieldVariant = `{ void*, bool }` (0x10 bytes with padding)
- **UE5.1.1+**: FFieldVariant = `{ void* }` (0x08 bytes)

This affects UStruct::ChildProperties offset. Our DynOff handles this transparently.

-----

## 3. GNames Patterns for UE5.5+

**Source**: `Dumper-7/Dumper/Engine/Private/Unreal/UnrealTypes.cpp`

Dumper-7 uses 6 LEA-based AOB patterns for GNames, plus a string reference fallback (`"ForwardShadingQuality_"`).

All standard patterns failed on TQ2 (UE5.7). Our `.data` pointer-scan fallback (`FindGNamesByPointerScan`) scans for `"None"` FNameEntry pattern — complementary approach.

**Recommendation**: Combine both strategies — try AOB patterns first, then string-ref fallback, then pointer-scan as last resort.

-----

## 4. LiveView / LiveEditor Implementation

**Source**: `UEDumper/Frontend/Windows/LiveEditor.cpp`

### Architecture

```
LiveMemory::MemoryBlock
  gameAddress    -- UObject* address
  buffer         -- cached memory copy
  size           -- struct size
  updateTimeStamp -- for refresh timing
```

### Update Loop

- **Interval**: 500ms (`MEMORY_UPDATE_SPEED`)
- **Method**: Periodic memory read into cached buffer
- **UI rendering**: `drawMembers()` supports read/write fields

### Field Drawing Functions

- `drawReadWriteableField()` — generic field rendering
- `drawStructProperty()` — nested struct support
- `drawMemberArrayProperty()` — TArray with max recursion depth (100)
- `drawMemberObjectProperty()` — pointer traversal in live memory

**Relevance**: Our `watch` pipe command with configurable `interval_ms` provides similar functionality. The 500ms default is a good reference for our UI.

-----

## 5. Key Source Files Reference

| File | Purpose | Relevance |
|------|---------|-----------|
| `Dumper-7/.../OffsetFinder.cpp` | Dynamic offset detection, CasePreservingName fixup | Critical — `FixupHardcodedOffsets()` pattern |
| `Dumper-7/.../Offsets.cpp` | `InitFNameSettings()`, `PostInitFNameSettings()` | Critical — FName size detection |
| `Dumper-7/Settings.h` | Global flags (bUseCasePreservingName, bUseFProperty) | Critical — configuration |
| `Dumper-7/.../UnrealObjects.cpp` | FField chain traversal, GetProperties() | High — property iteration |
| `Dumper-7/.../UnrealTypes.cpp` | FName resolution, GNames AOB patterns | High — name pool access |
| `UEDumper/.../LiveEditor.cpp` | Live memory update loop, field rendering | Medium — UI reference |
| `UEDumper/.../LiveMemory.h` | MemoryBlock caching structure | Medium — live watch pattern |

-----

## 6. Actionable Items for UE5CEDumper

1. **Already implemented**: DynOff runtime probing (superior to compile-time flag approach)
2. **Future**: Lua-based per-game AOB overrides for custom signature definitions
3. **Future**: String reference fallback for GNames (`"ForwardShadingQuality_"` or similar)
4. **UI reference**: 500ms live watch interval, cached MemoryBlock pattern for HexViewPanel
5. **CE pointer generation**: Our `get_ce_pointer_info` is unique — UE4SS/Dumper-7/UEDumper do not provide this
