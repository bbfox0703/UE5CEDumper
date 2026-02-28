# Improvement Analysis: UE5CEDumper Gap Analysis Report

> Cross-referencing RE-UE4SS and Dumper-7 against UE5CEDumper (Build 1.0.0.97)
> Generated: 2026-02-25

---

## Executive Summary

UE5CEDumper is production-ready for UE4.22-5.7 with 51 AOB patterns, dynamic offset detection (DynOff), and a full Avalonia UI. This report identifies **12 high-value improvement opportunities** from RE-UE4SS and Dumper-7 that do not conflict with UE5CEDumper's architecture. Items are ranked by impact and feasibility.

---

## Priority Legend

| Priority | Meaning |
|----------|---------|
| **P0 - Critical** | Directly improves game coverage or prevents crashes |
| **P1 - High** | Significant feature gap visible to end users |
| **P2 - Medium** | Quality-of-life or robustness improvement |
| **P3 - Low** | Nice-to-have, future-proofing |

---

## GAP #1: Encrypted GObjects Array Support

| Field | Value |
|-------|-------|
| **Priority** | P0 - Critical |
| **Source** | Dumper-7 (`ObjectArray.h:34-40`, `ObjectArray.cpp:187-191`) |
| **UE5CEDumper Status** | Not implemented |

### What Dumper-7 Does

Dumper-7 supports games that encrypt their GObjects pointer array (e.g., Valorant, Fortnite) through an injectable decryption hook:

```
Static function pointer: DecryptPtr (defaults to identity lambda)
Macro: InitObjectArrayDecryption(DecryptionLambda)
Called at: every chunk pointer dereference in ByIndex()
```

The `DecryptPtr` is invoked in `IsAddressValidGObjects`, `ByIndex` lambdas, and all GObjects initialization paths. If no decryption is set, the identity lambda passes pointers through unchanged — zero overhead for non-encrypted games.

### Recommendation

Add a `DecryptObjectPtr` function pointer to `ObjectArray.cpp` with:
- Default: identity (no overhead)
- Configurable via new pipe command `set_decryption` or DLL export `UE5_SetObjectDecryption()`
- Apply at chunk dereference points in `ObjectArray::GetByIndex()` and `DetectItemSize()`
- CE Lua script can set the decryption lambda per-game

### Impact

Opens support for a class of anti-cheat protected games (Valorant, Fortnite, etc.) that currently produce garbage data.

### Estimated Scope

~100 LOC in ObjectArray.cpp + ~20 LOC in ExportAPI/PipeServer.

---

## GAP #2: Game-Specific ObjectArray Layout Detection

| Field | Value |
|-------|-------|
| **Priority** | P0 - Critical |
| **Source** | Dumper-7 (`ObjectArray.cpp:16-60`) |
| **UE5CEDumper Status** | Partial (4 layouts A/B/C/D, but hardcoded member order) |

### What Dumper-7 Does

Dumper-7 defines explicit `FChunkedFixedUObjectArrayLayout` structs for games that reorder struct members:

| Game | Objects | MaxElements | NumElements | MaxChunks | NumChunks |
|------|---------|-------------|-------------|-----------|-----------|
| **Default** | 0x00 | 0x10 | 0x14 | 0x18 | 0x1C |
| **Back4Blood** | 0x10 | 0x00 | 0x04 | 0x08 | 0x0C |
| **Multiversus** | 0x18 | 0x10 | 0x00 | 0x14 | 0x20 |
| **MindsEye** | 0x18 | 0x00 | 0x14 | 0x10 | 0x04 |

Auto-detection uses a `MatchesAnyLayout` lambda to try all variants.

### Current UE5CEDumper Approach

UE5CEDumper's `DetectLayout()` (v2) checks 4 candidate positions for the `Objects*` pointer but doesn't validate `NumElements`/`MaxElements` member positioning. This means games like Back4Blood could read `NumElements` from the wrong offset.

### Recommendation

Extend `DetectLayout()` to validate the full struct layout (not just Objects pointer position):
1. For each candidate layout, read `NumElements` and `MaxElements` from their respective offsets
2. Validate: `0x1000 <= NumElements <= 0x400000`, `MaxElements >= NumElements`, `MaxElements % 0x10 == 0`
3. Add the 4 Dumper-7 layouts as named presets for fallback

### Impact

Fixes silent data corruption on games with reordered FChunkedFixedUObjectArray members.

### Estimated Scope

~80 LOC in ObjectArray.cpp (extend existing DetectLayout).

---

## GAP #3: MapProperty & SetProperty Value Reading

| Field | Value |
|-------|-------|
| **Priority** | P1 - High |
| **Source** | Dumper-7 (`OffsetFinder.cpp:1099-1143`) |
| **UE5CEDumper Status** | Stub only — shows "(container)" placeholder |

### Current State

UE5CEDumper recognizes MapProperty and SetProperty as container types (`UStructWalker.cpp:1013-1015`) but does not read their contents. They appear as `"(container)"` in the UI.

### What Dumper-7 Does

Dumper-7 discovers offsets for:
- `SetProperty::ElementProp` — inner element type pointer
- `MapProperty::KeyProp` / `MapProperty::ValueProp` — key and value type pointers
- Base offsets via `FindSetPropertyBaseOffset()` and `FindMapPropertyBaseOffset()`

### Recommendation

Implement Map/Set reading in `UStructWalker::WalkInstance()`:

**SetProperty** (simpler):
- Read `TSet` header: `ElementsData*`, `HashSize`, `NumElements`
- Iterate elements at stride = ElementSize
- Resolve element types using inner FProperty

**MapProperty** (more complex):
- Read `TMap` header: `Pairs.Data*`, `Pairs.NumElements`
- Each pair entry: `[Key][Value][HashIndex]` at stride = KeySize + ValueSize + sizeof(int32)
- Resolve key/value types using KeyProp/ValueProp inner FProperty

### Impact

Maps and Sets are common in UE projects (e.g., `TMap<FName, FAssetData>`, `TSet<AActor*>`). Currently invisible in the inspector.

### Estimated Scope

~200 LOC in UStructWalker.cpp + pipe protocol extension.

---

## GAP #4: DelegateProperty Support

| Field | Value |
|-------|-------|
| **Priority** | P1 - High |
| **Source** | Dumper-7 (`OffsetFinder.cpp:1058-1080`) |
| **UE5CEDumper Status** | **IMPLEMENTED (Phase B)** — DelegateProperty shows `Target::FuncName`, MulticastInlineDelegateProperty shows `(N bindings) [Target::Func, ...]` in Live Walker |

### What Dumper-7 Does

Discovers `DelegateProperty::SignatureFunction` offset by:
- Finding known delegate members (`K2_GetTimerElapsedTimeDelegate`, `K2_GetTimerRemainingTimeDelegate`)
- Binary pattern matching to locate the signature function pointer field
- Supports single delegates, multicast inline, and multicast sparse

### Recommendation

Add delegate support in two phases:

**Phase A — Schema display** (P1):
- In `WalkClass()`: when encountering DelegateProperty/MulticastInlineDelegateProperty, read the `SignatureFunction` UFunction pointer
- Walk the UFunction's parameters to display the delegate signature (return type + param list)
- Display as: `OnDamageReceived(float Damage, AActor* DamageCauser)` in ClassStruct panel

**Phase B — Instance inspection** (P2):
- Read the delegate's bound object pointer and function name
- For multicast: iterate the invocation list

### Impact

Delegate properties are ubiquitous in Blueprint-heavy games. Currently they show as unknown/untyped fields.

### Estimated Scope

Phase A: ~80 LOC. Phase B: ~120 LOC.

---

## GAP #5: String-Reference Fallback for GNames

| Field | Value |
|-------|-------|
| **Priority** | P1 - High |
| **Source** | Dumper-7 (`UnrealTypes.cpp:69-204`) |
| **UE5CEDumper Status** | **IMPLEMENTED** — `FindGNamesByStringRef()` in OffsetFinder.cpp, Tier 2 fallback between AOB scan and pointer-scan. Searches .rdata for UE string literals, finds XREF, scans ±0x60 bytes for LEA to FNamePool in .data |

### What Dumper-7 Does

When all 6 GNames AOB patterns fail, Dumper-7 falls back to:
1. Search for `"ForwardShadingQuality_"` string in all sections
2. Scan 0x50 bytes around the string reference for LEA+CALL pattern to `FName::AppendString`
3. Detect if AppendString was inlined (different AOB pattern)
4. Secondary fallback: search backwards from `" Bone: "` string for AppendString signatures

This finds GNames/FNamePool indirectly through code that *uses* FName resolution, rather than scanning for the pool itself.

### Recommendation

Add a new fallback tier in `OffsetFinder::FindGNames()` between the current AOB scan and the pointer-scan fallback:

1. Search .rdata for `"ForwardShadingQuality_"` UTF-16/UTF-8 string
2. Find XREF in .text (LEA pointing to string address)
3. Scan nearby code for `LEA reg, [rip+disp]` pattern that loads FNamePool address
4. Validate via existing `ValidateGNamesStructural()`

### Impact

Adds an independent discovery path that works even when GNames is relocated or compiler optimizations break existing AOB patterns. Particularly valuable for custom UE builds.

### Estimated Scope

~100 LOC in OffsetFinder.cpp.

---

## GAP #6: FFieldVariant Tag-Bit Detection (UE 5.3+)

| Field | Value |
|-------|-------|
| **Priority** | P1 - High |
| **Source** | RE-UE4SS (`FField.hpp:37-100`) |
| **UE5CEDumper Status** | **IMPLEMENTED** — `StripFFieldTag()` / `IsFFieldVariantUObject()` in Constants.h, applied in UStructWalker + OffsetFinder |

### What RE-UE4SS Does

Starting from UE 5.3, FFieldVariant changed from a 2-member struct (`{Pointer, bIsUObject}`) to a tagged pointer where the LSB indicates type:

```
Pre-5.3:  FFieldVariant = { void* Field; bool bIsUObject; }  // 0x10 bytes
5.3+:     FFieldVariant = { uintptr_t TaggedPtr; }           // 0x08 bytes
          bit 0 = 1 → UObject*, bit 0 = 0 → FField*
          UObjectMask = 0x1
```

RE-UE4SS handles this with `Version::IsAtLeast(5, 3)` checks and masks the LSB before dereferencing.

### Implementation (Build 101)

Added FFieldVariant tag-bit infrastructure across 3 files:

1. **Constants.h**: `bTaggedFFieldVariant` flag + `StripFFieldTag()` / `IsFFieldVariantUObject()` helpers
2. **OffsetFinder.cpp**: Sets flag for UE5.3+ (version-based) and infers from probed `FField::Next=0x18` (unknown version). Applies tag-bit stripping in FField::Next probe loop to reject tagged Owner pointers.
3. **UStructWalker.cpp**: `WalkFFieldChain()` strips tag bit on entry and on each Next pointer read. Breaks chain if tagged pointer indicates UObject (not FField).

---

## GAP #7: SDK/Header Generation Export

| Field | Value |
|-------|-------|
| **Priority** | P2 - Medium |
| **Source** | Dumper-7 (`CppGenerator.h:19-155`) |
| **UE5CEDumper Status** | Not implemented (CE XML export only) |

### What Dumper-7 Does

Generates complete C++ SDK headers organized by UE package:
- Full struct/class definitions with inheritance
- Member offsets as hex comments (e.g., `// 0x00A0(0x0008)`)
- Property flags and sizes
- Function signatures with parameters and return types
- Enum definitions with underlying types
- BitField packing syntax
- Auto-generated padding to maintain struct layout
- Dependency resolution and forward declarations

### Recommendation

Add an `export_sdk` pipe command that generates a simplified SDK dump:

**Phase A — Offset header** (P2):
- Walk all loaded classes, emit `struct ClassName : SuperName { ... }` with offset comments
- One file per package, or a single monolithic header
- Output to `%LOCALAPPDATA%\UE5CEDumper\SDK\` folder

**Phase B — Full C++ SDK** (P3):
- Add proper C++ type mapping (IntProperty → int32, etc.)
- Function parameter structs
- Enum definitions
- Forward declarations and include guards

### Impact

SDK generation is the #1 requested feature in UE dumper tools. Many users want headers for use in external C++ projects, not just CE address tables.

### Estimated Scope

Phase A: ~300 LOC (new SdkGenerator.cpp). Phase B: ~600 LOC.

---

## GAP #8: USMAP / IDA Mapping Export

| Field | Value |
|-------|-------|
| **Priority** | P2 - Medium |
| **Source** | Dumper-7 (`MappingGenerator.h:63-139`, `IDAMappingGenerator`) |
| **UE5CEDumper Status** | Not implemented |

### What Dumper-7 Does

Generates mapping files for external tools:

**USMAP Format** (binary property mapping):
- Magic: 0x30C4, versioned format (5 versions)
- Compressed name tables + property descriptors
- Used by modding tools and pak extractors

**IDA Mapping**:
- Virtual table name generation
- Function name mangling for IDA symbol import
- Class function extraction and labeling

### Recommendation

**Phase A — USMAP export** (P2):
- Implement USMAP v3 (LongFName) writer
- Walk all classes/structs, serialize property descriptors
- Add `export_usmap` pipe command, output to file

**Phase B — IDA mapping** (P3):
- Generate `.idc` or `.py` script with class/struct offset annotations
- Output function signatures at known virtual table entries

### Impact

USMAP is the de-facto standard for UE modding tool interop. IDA mapping accelerates reverse engineering workflows.

### Estimated Scope

Phase A: ~250 LOC. Phase B: ~200 LOC.

---

## GAP #9: PredefinedMembers Override System

| Field | Value |
|-------|-------|
| **Priority** | P2 - Medium |
| **Source** | Dumper-7 (`PredefinedMembers.h:1-99`, `CppGenerator.cpp:1731-1800`) |
| **UE5CEDumper Status** | Not implemented |

### What Dumper-7 Does

Allows manual struct member injection for cases where auto-detection fails:
- `PredefinedMember` struct: type, name, offset, size, array dimension, alignment, bitfield info
- Per-class override tables (UObject core members, ULevel.Actors, UDataTable.RowMap, etc.)
- Merged with auto-generated members during iteration, sorted by offset
- Supports adding members that UE reflection doesn't expose (VTable pointer, internal flags)

### Recommendation

Add a JSON-based override system:
1. Load `%LOCALAPPDATA%\UE5CEDumper\overrides.json` on init
2. Format: `{ "ClassName": [{ "name": "Actors", "offset": "0x1A8", "type": "TArray<AActor*>", "size": 16 }] }`
3. In `WalkClass()`: merge overrides with auto-detected fields, sorted by offset
4. UI: allow adding overrides via right-click menu on ClassStruct panel

### Impact

Enables users to fix broken class layouts without code changes. Particularly valuable for heavily modified UE builds (custom engines, Chinese MMOs) where reflection data is stripped or corrupted.

### Estimated Scope

~150 LOC in UStructWalker + ~50 LOC JSON parsing + ~80 LOC UI.

---

## GAP #10: Cyclic Class Pointer Validation

| Field | Value |
|-------|-------|
| **Priority** | P2 - Medium |
| **Source** | Dumper-7 (OffsetFinder — cyclic validation pattern) |
| **UE5CEDumper Status** | Uses range-based validation only |

### What Dumper-7 Does

Validates UObject::Class pointers by following the class chain:
1. Read `obj->Class` pointer
2. Follow `Class->Class->Class...` up to 16 hops
3. Valid if chain terminates at a self-referential pointer (UClass's Class == itself)
4. Rejects false positives that pass range checks but aren't real UObjects

### Current UE5CEDumper Approach

`ValidateGObjects()` checks:
- NumElements range [0x1000, 0x400000]
- chunk[0] is a heap pointer (not null, not game module address)
- `LooksLikeDataPtr()` rejects code-section pointers

This is sufficient for most games but can admit false positives where a data pointer happens to satisfy range checks.

### Recommendation

Add cyclic class validation as a secondary check in `ValidateGObjects()`:
1. After range/pointer checks pass, read first 3-5 valid objects from chunk[0]
2. For each: follow `obj->Class` chain (max 16 hops)
3. Require at least 2/3 objects to have self-terminating class chains
4. Reject candidate if cyclic validation fails

### Impact

Reduces false positive GObjects detection. Most beneficial for games with complex memory layouts where data pointers coincidentally pass existing checks.

### Estimated Scope

~60 LOC in OffsetFinder.cpp.

---

## GAP #11: SoftObjectProperty / LazyObjectProperty / InterfaceProperty Support

| Field | Value |
|-------|-------|
| **Priority** | P2 - Medium |
| **Source** | Dumper-7 (PropertyWrapper — type mapping), RE-UE4SS (Unreal property types) |
| **UE5CEDumper Status** | Not implemented |

### Missing Property Types

| Property Type | UE Usage | Current Handling |
|---------------|----------|-----------------|
| SoftObjectProperty | `TSoftObjectPtr<>` — asset references (lazy load) | Partial (listed in ObjectProperty handler, may not fully resolve paths) |
| SoftClassProperty | `TSoftClassPtr<>` — class asset references | Shows raw bytes |
| LazyObjectProperty | `TLazyObjectPtr<>` — GC-safe lazy references | Partial (similar to SoftObject) |
| InterfaceProperty | `TScriptInterface<>` — interface pointers | Shows raw bytes |
| TextProperty | `FText` — localized text | **Partial** (reads as FString, misses localization key/namespace) |
| FieldPathProperty | `TFieldPath<>` — UE5 field references | Shows raw bytes |

### What Dumper-7 Does

Maps all property types to C++ equivalents with proper offset/size handling. RE-UE4SS provides runtime accessors for reading these types' inner values.

### Recommendation

Implement value reading for the most common missing types:

**TextProperty** (high frequency, currently partial):
- Already reads as FString, but misses localization key/namespace
- Full support: dereference `FTextData*`, read `Key`, `Namespace`, and `SourceString`
- Display as: `"Hello World" [Key=NSLOCTEXT("Game", "Greeting")]`

**SoftObjectProperty** (high frequency):
- `FSoftObjectPath` = `{ FName AssetPathName; FString SubPathString; }`
- Read: resolve FName + read FString

**InterfaceProperty** (medium frequency):
- `FScriptInterface` = `{ UObject* ObjectPointer; void* InterfacePointer; }`
- Read: dereference ObjectPointer, resolve name

### Impact

These types appear frequently in content-heavy games (RPGs, open world). Currently shown as opaque hex bytes.

### Estimated Scope

~150 LOC total across UStructWalker for the 3 priority types.

---

## GAP #12: AVX2/SIMD Pattern Scanning (Vector Operations)

| Field | Value |
|-------|-------|
| **Priority** | P3 - Low |
| **Source** | Internal research (`docs/simd-scanning-notes.md`) |
| **UE5CEDumper Status** | **IMPLEMENTED** — `ScanRegion()` now uses explicit AVX2 intrinsics with anchor-based scanning |

### Implementation (Build 99)

Replaced the `memchr`-based `ScanRegion()` with an AVX2 anchor-based approach:

1. **Anchor selection**: `ParsePattern()` now finds the first non-wildcard byte at *any* position (not just byte 0). Handles wildcard-prefixed patterns like `"?? ?? 48 8B 05"`.
2. **SIMD scan**: `_mm256_cmpeq_epi8` compares 32 bytes at a time against the anchor byte. `_mm256_movemask_epi8` extracts a 32-bit match bitmask. `_BitScanForward` iterates set bits.
3. **Scalar verification**: For each SIMD hit, the full pattern is verified byte-by-byte with early exit on first mismatch.
4. **Scalar tail**: Positions where a 32-byte SIMD load won't fit fall back to `ScanRegionScalar()`.

No CPUID check needed — the DLL build already requires `/arch:AVX2`.

---

## Summary Matrix

| # | Gap | Priority | Source | Effort | Impact |
|---|-----|----------|--------|--------|--------|
| 1 | Encrypted GObjects Array | **P0** | Dumper-7 | Small (~100 LOC) | Opens anti-cheat game support |
| 2 | Game-Specific ObjectArray Layouts | **P0** | Dumper-7 | Small (~80 LOC) | Fixes silent data corruption |
| 3 | MapProperty / SetProperty | **P1** | Dumper-7 | Medium (~200 LOC) | Common container types visible |
| 4 | DelegateProperty Support | ~~P1~~ | Dumper-7 | ~~Medium~~ | **DONE** — DelegateProperty shows Target::FuncName, MulticastInlineDelegateProperty shows (N bindings) |
| 5 | String-Ref GNames Fallback | ~~P1~~ | Dumper-7 | ~~Small~~ | **DONE** — `FindGNamesByStringRef()` Tier 2 fallback in OffsetFinder.cpp |
| 6 | FFieldVariant Tag-Bit (UE 5.3+) | ~~P1~~ | RE-UE4SS | ~~Small~~ | **DONE** — `StripFFieldTag()` in Constants.h, applied in walker + offset finder |
| 7 | SDK/Header Generation | **P2** | Dumper-7 | Large (~300-900 LOC) | #1 requested dumper feature |
| 8 | USMAP / IDA Mapping Export | **P2** | Dumper-7 | Medium (~450 LOC) | Modding tool interop |
| 9 | PredefinedMembers Override | **P2** | Dumper-7 | Medium (~280 LOC) | User-fixable broken layouts |
| 10 | Cyclic Class Pointer Validation | **P2** | Dumper-7 | Small (~60 LOC) | Fewer false positive GObjects |
| 11 | Soft/Lazy/Interface/Text Properties | **P2** | Both | Medium (~150 LOC) | More property types readable (TextProperty already partial) |
| 12 | AVX2/SIMD Vector Scanning | ~~P3~~ | Internal | ~~Medium~~ | **DONE** — explicit AVX2 intrinsics in `ScanRegion()` |

---

## Recommended Implementation Order

### Sprint 1 — Robustness (P0)
1. **GAP #1**: Encrypted GObjects (small, high impact)
2. **GAP #2**: ObjectArray layout detection (small, prevents corruption)
3. **GAP #6**: FFieldVariant tag-bit masking (tiny, prevents crashes)

### Sprint 2 — Property Coverage (P1) ✅ COMPLETE
4. **GAP #3**: MapProperty / SetProperty ✅
5. **GAP #4**: DelegateProperty ✅
6. **GAP #11**: TextProperty + SoftObjectProperty + InterfaceProperty ✅
7. **GAP #5**: String-reference GNames fallback ✅

### Sprint 3 — Export & Interop (P2)
8. **GAP #7**: SDK header generation (Phase A: offset headers)
9. **GAP #8**: USMAP export
10. **GAP #10**: Cyclic class pointer validation
11. **GAP #9**: PredefinedMembers override system

### Sprint 4 — Polish (P2-P3)
12. **GAP #7**: SDK generation Phase B (full C++ types)
13. **GAP #8**: IDA mapping Phase B
14. **GAP #12**: AVX2 SIMD scanning (if scan times warrant it)

---

## Excluded from Analysis

Per instructions, the following were excluded:
- **UEDumper**: UI and initial pointer handling only — no novel features beyond what UE5CEDumper already implements
- **RE-UE4SS Lua scripting**: Fundamentally different architecture (Lua runtime vs C++ DLL); not portable without rearchitecting
- **RE-UE4SS Live Coding / Hot-Reload**: Requires DLL hooking infrastructure that conflicts with CE injection model
- **Dumper-7 Package dependency resolution**: Only relevant for SDK generation (covered in GAP #7)
- **UVTD (Virtual Table Dumper)**: Requires PDB access which is unavailable for shipped game binaries; only useful for local UE source builds

---

## References

| Source | Location |
|--------|----------|
| RE-UE4SS FField.hpp | `vendor/RE-UE4SS/deps/first/Unreal/include/Unreal/FField.hpp` |
| Dumper-7 ObjectArray | `vendor/Dumper-7/Dumper/Engine/Public/Unreal/ObjectArray.h` |
| Dumper-7 OffsetFinder | `vendor/Dumper-7/Dumper/Engine/Private/Unreal/OffsetFinder.cpp` |
| Dumper-7 CppGenerator | `vendor/Dumper-7/Dumper/Generator/Public/Generators/CppGenerator.h` |
| Dumper-7 MappingGenerator | `vendor/Dumper-7/Dumper/Generator/Public/Generators/MappingGenerator.h` |
| Dumper-7 PredefinedMembers | `vendor/Dumper-7/Dumper/Generator/Public/PredefinedMembers.h` |
| UE5CEDumper ObjectArray | `dll/src/ObjectArray.cpp` |
| UE5CEDumper OffsetFinder | `dll/src/OffsetFinder.cpp` |
| UE5CEDumper UStructWalker | `dll/src/UStructWalker.cpp` |
| UE5CEDumper SIMD Notes | `docs/simd-scanning-notes.md` |
| UE5CEDumper UE4SS Analysis | `docs/ue4ss-analysis.md` |
