# Technical Notes

> Moved from CLAUDE.md. Covers UE version differences, FField vs UProperty, FNamePool internals, and implementation phases.

-----

## UE Version Differences

| Version | Key Differences |
|---------|----------------|
| **UE4.7 and earlier** | Pre-`FUObjectItem`, and a **different, simpler** shape than 4.8–4.10: `FUObjectArray::ObjObjects` is a flat `TArray<UObjectBase*>` at `FUObjectArray+0x10` (Data/Num/Max = `0x10`/`0x18`/`0x1C`), the global is a plain `extern GUObjectArray`, and `UObjectBase` is byte-identical to 4.11. **Still not supported** — the blocker is the stride-8 raw-pointer element, not the layout — see the dedicated section below |
| **UE4.8–4.10** | **Pre-`FUObjectItem` regime — the dumper does NOT support this.** `FUObjectArray::ObjObjects` is a `TStaticIndirectArrayThreadSafeRead<UObjectBase,8388608,16384>` whose element is a **raw `UObjectBase*` (stride 8)**, and whose chunk table is an **INLINE `UObjectBase*[512]` array**, not a pointer; the global is the Meyers singleton `GetUObjectArray()` — see the dedicated section below |
| **UE4.11–4.12** | `FFixedUObjectArray` (flat). **`FUObjectItem` is 16 BYTES** — `Object` / `ClusterAndFlags` / `SerialNumber`: the cluster index and the internal flags share ONE `int32`. `UProperty` chain |
| **UE4.13–4.19** | `FFixedUObjectArray` (flat, single indirection). `FUObjectItem` splits into `Object` / `Flags` / `ClusterIndex` / `SerialNumber` = **24 bytes**. `UProperty` chain. TNameEntryArray |
| **UE4.20–4.24** | `FChunkedFixedUObjectArray` — chunked. `UProperty` still in use |
| UE4.25–4.27 | `FField`/`FProperty` replaces `UProperty` (no longer inherits UObject). `ChildProperties` chain added |
| UE5.0–5.1.0 | FNamePool standard format. FFieldVariant = `{ void*, bool }` (0x10 bytes with padding) |
| UE5.1.1+ | FFieldVariant = `{ void* }` (0x08 bytes) — affects ChildProperties offset |
| UE5.2 | `FChunkedFixedUObjectArray` stride may differ |
| UE5.3+ | Some games enable Object Pointer Encryption |
| UE5.4+ | `FField` chain structure stable, no major changes |
| any | **CasePreservingName** (= `WITH_EDITORONLY_DATA`, so 0 in every packaged build): `sizeof(FName)` grows 0x8 → **0xC** (adds DisplayIndex; three int32, alignof 4, no pad), while the UObject `NamePrivate`→`OuterPrivate` **slot** grows to **0x10** (OuterPrivate is 8-aligned). FField::Flags and the FProperty offsets shift by the +0x8 *slot* delta. ⚠ 0x10 is the slot, **not** the size — see the A9 note on `DynOff::bCasePreservingName`. Must use `DynOff` dynamic detection |
| UE5.8 / UE6.0 | **Cache-locality reorder** of `FUObjectArray` / `FChunkedFixedUObjectArray` (shipped in **5.8**, unchanged in 6.0): `ObjObjects` is `FUObjectArray`'s first member; chunked fields `Objects@0x00, NumElements@0x08, MaxElements@0x0C, NumChunks@0x10, MaxChunks@0x14, PreAllocatedObjects@0x18`. Matched by the **"UE5.8" preset** `{0x00,0x0C,0x08,0x14,0x10}` — grep the string `"UE5.8"` to find it: `Aura.cpp`'s chunked-preset table and `Genau.cpp`'s Tier-1 preset table in `ValidateGObjects`, both written in ArrayLayout order = `{objectsOffset, maxElementsOffset, numElementsOffset, maxChunksOffset, numChunksOffset}`. ⚠ There is a **THIRD** site, and it is not greppable by the tuple: `Genau.cpp`'s Tier-2 relaxed fallback row `"F"` encodes the same layout as `{0x08,0x0C,0x00,0x14,0x10}`, because that table's struct is `{numOff, maxOff, objOff, maxCOff, numCOff, isFlat, name}` — `numOff`/`objOff` swapped. Same five offsets, permuted; a change to one row must be mirrored in all three. **UE6.0 is layout-identical to 5.8** for every dumper-read structure in normal shipping builds — see the parity note below |

> **UE6.0 vs 5.8 — shipping-build layout parity** (verified `origin/5.8..origin/ue6-main`, 2026-06-30). For normal shipping game builds UE 6.0 reads identically to UE 5.8 across every structure the dumper touches; the core path is already UE6-ready and nothing needs implementing now:
>
> - **FChunkedFixedUObjectArray / FUObjectArray**: SAME (the 5.8 reorder in the row above; 6.0 unchanged).
> - **FUObjectItem**: SAME — `Object*`@+0x08 after the `int64 FlagsAndRefCount` (the reordered-item layout `DetectItemSize` already handles, `Unpacked57` mode). Only 6.0 delta is removal of `FRemoteObjectId RemoteId`, gated on `UE_WITH_REMOTE_OBJECT_HANDLE`.
> - **UObjectBase**: Class/Name/Outer/Index/Flags offsets SAME — the hardcoded `OFF_UOBJECT_*` stay valid. 6.0 inserts `FRemoteObjectId RemoteIdPrivate` between `InternalIndex` and `ClassPrivate` **only** `#if UE_WITH_REMOTE_OBJECT_HANDLE`.
> - **UStruct / UClass**: data members SAME (only virtual signatures gained `AUTORTFM_*` + a `PostLoad(Object)` param).
> - **FProperty / FField**: layout SAME (the 157-line `UnrealType.h` diff is all AutoRTFM annotations, zero data members).
> - **FName / FNamePool**: SAME (AutoRTFM annotation + a `DebugDumpBlock` tail-terminator fix only).
>
> ⚠️ **Watch-item (far future):** `UE_WITH_REMOTE_OBJECT_HANDLE` is an experimental multi-server/UEFN remote-object feature, OFF in normal shipping. If a UE6 game ships it **ON**, `ClassPrivate`/`NamePrivate`/`OuterPrivate` shift by `sizeof(FRemoteObjectId)` (breaking the hardcoded UObject offsets) **and** it forces FUObjectItem packing off. Version-string-map + AOB prep tracked in [todo.md](todo.md).

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

### Flat (FFixedUObjectArray, UE4.1?–4.20)

```
GObjects → FUObjectArray
  +0x00: Objects* (direct item array pointer, no chunk table)
  +0x08: MaxElements (int32)
  +0x0C: NumElements (int32)
```

Detection, in priority order:

1. **If the layout preset already resolved to a flat one, believe it.** `s_isFlat` is set when
   `"Flat"` / `"Flat-Base"` validates, and that is a stronger signal than any heuristic.
2. Otherwise, when `numElements > OBJECTS_PER_CHUNK`, check whether `*(Objects + 8)` is a valid
   heap pointer. If not (e.g. `0x40000000` = `EObjectFlags::Const`), the array is flat.

> Step 1 exists because step 2 **cannot speak for a small flat array**. It only runs when the
> count needs two chunks, so a flat array with fewer than 65536 objects fell straight through to
> the CHUNKED probe — which treats `Item[0].Object` as a chunk pointer and then probes a
> UObject's own bytes as an item array. Measured on NEKOPALIVE (4.11, Num=27016): `P1 stride 16:
> good=1, named=1, null=51, bad=148`, and ~10% of names resolving downstream. Fantasynth (4.13)
> has the identical layout but Num=80162, needed two chunks, and scored `P0-flat stride 24:
> good=200, named=200, null=0, bad=0`. **The only difference between the two was the object
> count.**

> **Both boundaries are now read off Epic's own source**, not extrapolated — `vendor/UnrealEngine`
> is a full-refspec clone, so `git show <tag>:Engine/Source/Runtime/CoreUObject/Public/UObject/UObjectArray.h`
> answers these directly. The active `typedef …  TUObjectArray;` is the thing to read (there is a
> commented-out decoy typedef right above it in every version):
>
> | tag | `TUObjectArray` |
> |---|---|
> | 4.18.3 / 4.19.0 / 4.19.2 | `FFixedUObjectArray` — **flat** |
> | 4.20.3 / 4.21.2 / 4.22.3 / 4.23.1 | `FChunkedFixedUObjectArray` — **chunked** |
>
> So the flat→chunked change is **4.19 → 4.20**. The table previously said the chunked array
> arrived at "4.21", and that the flat range started at "4.11"; both were guesses.
>
> **A flat array can be presented at either of two anchors**, which is why there are two flat
> presets. Patterns carrying `adjustment = -0x10` hand over `ObjObjects` (preset `"Flat"`,
> `{0x00,0x08,0x0C}`); everything else hands over the `FUObjectArray` **base**, where the same
> three fields sit at `{0x10,0x18,0x1C}` (preset `"Flat-Base"`). Only five of ~56 GObjects
> patterns carry that adjustment, so before `"Flat-Base"` existed, any pre-4.20 title those five
> missed was unfixable by pattern work at *any* priority.

### Pre-`FUObjectItem` (UE 4.8–4.10) — NOT SUPPORTED

Verified against a **UE 4.10.2 PDB** (IS Defense Editor's `UE4Editor-Cmd.pdb`, 30.9 MB of type
records), with every offset independently re-confirmed from instruction encodings in
`UE4Editor-CoreUObject.dll` / `-Core.dll`. `FUObjectItem`, `FChunkedFixedUObjectArray`,
`FFixedUObjectArray` and the whole `FField`/`FProperty` and `FNamePool` families are all **absent
from the type manager** — they do not exist yet at this version.

```
FUObjectArray                                        size 0x1098
  +0x0000: ObjFirstGCIndex / ObjLastNonGCIndex / OpenForDisregardForGC  (int32 ×3)
  +0x0010: ObjObjects = TStaticIndirectArrayThreadSafeRead<UObjectBase,8388608,16384>
             +0x0000 (abs +0x0010): Chunks — INLINE UObjectBase*[512], NOT a pointer
             +0x1000 (abs +0x1010): NumElements (int32)
             +0x1004 (abs +0x1014): NumChunks   (int32)
           No MaxElements / MaxChunks FIELDS — the maxima are template parameters,
           emitted as compile-time literals (8388608 total, 16384 per chunk).
  +0x1018: ObjObjectsCritical ...

IndexToObject:  ChunkIndex = Index >> 14        (sar 0xE)
                Within     = Index & 0x3FFF
                Chunk      = Chunks[ChunkIndex]  <- indexed off the array base DIRECTLY
                Object     = *(UObjectBase**)(Chunk + Within*8)   <- stride 8, ONE deref
```

Why every GObjects pattern misses, and why a new AOB alone cannot fix it:

| | Dumper assumes | UE 4.10 |
|---|---|---|
| element | `FUObjectItem`, stride 16/24/20 (`Aura.cpp` `candidates[]`) | raw `UObjectBase*`, **stride 8** — not a candidate |
| chunk table | a `Objects**` pointer field: load it, then index | **inline array** — its address *is* the table |
| elements/chunk | `Grimoire::OBJECTS_PER_CHUNK` = 65536 | **16384** |
| Num / Max | runtime fields near the base | NumElements `+0x1010`, NumChunks `+0x1014`; no Max fields at all |
| the global | `GUObjectArray` data symbol / RIP load | **Meyers singleton `GetUObjectArray()`** — the address exists only in a `lea` inside the magic-static guard, so `GOBJ_EXP` can never hit |
| GC flags | `EInternalObjectFlags` in the item | `EObjectFlags` at `UObjectBase+0x08` |

`ArrayLayout`'s `objectsOffset` means *"read a pointer at this offset"*; 4.10 needs *"take the
**address** of this offset"*. That is an expressiveness gap, not a wrong constant — so even a
pattern that matched by luck would be rejected by `ValidateGObjects`.

**The name side is already correct**: `Serie.cpp`'s `UE4_CHUNK_SIZE = 0x4000` and the
double-deref with the string at `+0x10` match 4.10 exactly, which is why GNames resolves on these
old titles while GObjects does not.

**One editor-build caveat that must not leak.** That PDB is an Editor build, where
`WITH_CASE_PRESERVING_NAME` makes `FName` **12 bytes** instead of 8. `UObjectBase`'s
`ObjectFlags +0x08` / `InternalIndex +0x0C` / `Class +0x10` / `Name +0x18` are identical in a
shipping build, but **`Outer` is +0x28 in the editor and +0x20 shipping** — do not carry +0x28
anywhere. `FUObjectArray`, `TStaticIndirectArrayThreadSafeRead`, `FNameEntry`/`TNameEntryArray`
and `EObjectFlags` are not editor-gated and transfer as-is; the `UField`/`UStruct`/`UProperty`
family does NOT (which is moot — those are resolved dynamically by `DynOff`).

#### UE 4.7 and earlier — a DIFFERENT pre-`FUObjectItem` shape, blocked for a different reason

Everything above is 4.8–4.10. The indirect array and the `GetUObjectArray()` singleton both arrive
**at 4.8**; 4.7 and older are a plainer thing, verified against Epic's source at `origin/4.0` …
`origin/4.7` (all eight branches declare it identically) and corroborated by RE-UE4SS's own version
bands (`UVTD/src/Helpers.cpp` — `tarray_407_and_earlier` vs `indirect_408_to_410`).

```
FUObjectArray  (no base class, no virtual of its own -> NO vtable)
  +0x0000: ObjFirstGCIndex / ObjLastNonGCIndex / OpenForDisregardForGC  (int32 x3)
  +0x000C: (4 bytes padding, TArray is 8-aligned)
  +0x0010: ObjObjects = TArray<UObjectBase*>
             +0x0010  Data (UObjectBase**)
             +0x0018  ArrayNum
             +0x001C  ArrayMax
extern COREUOBJECT_API FUObjectArray GUObjectArray;   // a plain global, not a magic static
```

| | UE 4.8–4.10 | UE 4.7 and earlier |
|---|---|---|
| element | raw `UObjectBase*`, stride 8 | raw `UObjectBase*`, stride 8 (same) |
| container | `TStaticIndirectArrayThreadSafeRead`, INLINE chunk table | flat `TArray<UObjectBase*>` — **no chunks at all** |
| the global | Meyers singleton `GetUObjectArray()` | plain `extern GUObjectArray` |
| `UObjectBase` | 0x28: Flags `+0x08` / Index `+0x0C` / Class `+0x10` / Name `+0x18` / Outer `+0x20` | **identical** (diff of RE-UE4SS's `4_07` vs `4_11` `[UObjectBase]` blocks is empty) |
| what blocks us | `ArrayLayout` cannot express an inline chunk table | **not the layout** — see below |

**The layout is already expressible.** `Aura::ArrayLayout` would encode this as
`{0x10, 0x1C, 0x18, -1, -1}` — literally the `"Flat-Base"` preset with `maxElementsOffset` and
`numElementsOffset` transposed, because `FFixedUObjectArray` orders them Max-then-Num while
`TArray` orders them Num-then-Max. Same expressiveness point already recorded for UE3 below.

**What actually blocks it, measured:** `8` is not in `Lineal::kItemStrideCandidates`
(`{16, 24, 32, 20, 40}`), so stride detection can never select the right stride.

**What is reasoned but NOT measured:** four of those five candidates (16/24/32/40) are multiples of
8, so a probe would land on a *strict subset of the real pointers* — every slot a genuine
`UObject*`, giving `named` ≈ 100% and `bad` == 0. That is the "PASSES → confident" shape the gate
comment in `Lineal.h` warns about, and it is worse than its NULL case: the array would be silently
accepted at half (or a third, or a fifth) of its true length. **This has never been run.** There is
no ≤4.7 reference build in the corpus (`docs/reference-builds.md`'s oldest rows are the two 4.10
ones) and no ≤4.7 GObjects pattern — the version-needle table floors at `"4.18."`, so the only
route below 4.11 at all is the PE-resource `major == 4` branch. Treat the aliasing paragraph as a
prediction to be checked if a ≤4.7 oracle is ever built, not as a result.

The floor stays `MIN_SUPPORTED_UE_VERSION = 411`. Both sub-regimes are refused by the same gate.

**Deciding which regime an unknown old binary is in** — one instruction-level look at the
index→object accessor:

| | old (4.10-like) | new (`FUObjectItem`) |
|---|---|---|
| chunk split | `sar r,0xE` + `and r,0x3FFF` | `sar r,0x10` (65536/chunk) |
| element scale | `lea rax,[chunk+idx*8]` — scale **8** | `*16` / `shl 4` / `imul 24` |
| table fetch | `mov r,[base+chunkIdx*8]` — **no prior load** | `mov rcx,[base]` **first**, then index |

Cheapest single tell: an immediate **`cmp r32, 0x800000`** checked against an object index. In the
old regime `MaxTotalElements` is a compile-time literal in the bounds check; in the new regime the
maximum is a runtime field, so that immediate does not appear.

### Pre-UE4 (Unreal Engine 3) — REFUSED BY DESIGN (build 2508)

UE3 is not "an older version of this"; it is a different object model. There is no
`FUObjectArray`, no `FUObjectItem` and no `FNamePool` at all: the global array is
`UObject::GObjObjects`, a plain `TArray<UObject*>`, and names come from `FName::Names`, a flat
`TArray<FNameEntry*>`. Measured on a shipping UE3 x64 binary via its own exported
`UObject::GetOutermost`: **`Outer` @+0x40, `Class` @+0x50, `UStruct::SuperStruct` @+0x78** — versus
`Grimoire::OFF_UOBJECT_CLASS = 0x10` here.

That last fact is what makes UE3 unreachable rather than merely unimplemented.
`ValidateCyclicClassChain` gates **every** `ValidateGObjects` accept path and terminates on
`*(X + OFF_UOBJECT_CLASS) == X` (UClass's self-reference). At +0x10 a UE3 object has a different
field entirely, so a **correct** UE3 GObjects address is rejected before it can be accepted. The
same constant is baked into `Aura::LooksLikeUObject` (stride probing) and `ProbeStride`'s name
scoring. Promoting those constants to `DynOff` is ~113 use sites plus a bootstrap probe that must
find Class/Name without already knowing them — see the evaluation summary in
[roadmap.md](roadmap.md) before reopening this.

Interesting counterpoint, recorded so the estimate is not re-derived from scratch:
`Aura::ArrayLayout` **can already express** `TArray<UObject*>` as `{0x00, 0x0C, 0x08, -1, -1}` —
UE3's array is an EASIER shape than 4.10's, not the same wall. The blocker is the validators, not
the layout.

#### How a pre-UE4 binary is identified

The numeric floor alone cannot catch it. `MIN_SUPPORTED_UE_VERSION = 411` is only reachable via
`DetectVersionFromPEResource`'s `major == 4` branch (`400 + minor`); the memory needle table floors
at `"4.18."`. A UE3 game's PE version is a **game** version (e.g. `1.0.10897.0`), so detection
returned 0 and `FindAll`'s fallback guessed **504** — above the floor — and the title consumed a
full 150-pattern sweep to reach "no winner", then advised a UE-version override that could never
help. `Grimoire::PRE_UE4_SENTINEL_VERSION = 300` closes that: `CountPreUE4Markers` runs in
`DetectVersionDetailed`'s **terminal branch** and, on ≥2 of 4 markers, returns 300 at tier 1, which
trips the existing too-old gate with no new flag to plumb.

Two design rules that must not be relaxed:

1. **Positive markers only.** The *absence* of a UE4/UE5 tag is exactly the state of SUPPORTED
   stripped-tag titles (The Adventures of Elliot detects as version 0 by the identical route), so
   absence is a necessary conjunct and never a sufficient one. Placing the check in the terminal
   branch makes "no UE4/UE5 evidence" a property of the control flow rather than a separate test.
2. **The sentinel, not a bool.** A flag computed inside detection would be absent on launch 2,
   because a HintCache hit skips detection entirely. The *number* round-trips for free.

| Marker | Gal\*Gun DP (UE3) | 30 reference builds, UE 4.10–5.8 | 35 installed UE games |
|---|---|---|---|
| `UnrealEngine3` (ASCII / UTF-16LE) | 3 / 1 | 0 | 0 |
| `SeqAct_` (ASCII / UTF-16LE) | 10 / 108 | 0 | 0 |
| `PhysXLoader64` (import name) | 1 | 0 | 0 |
| Epic `LegalCopyright` with a year ≤ 2013 | 2012 | 0 | 0 |
| **score** | **4 / 4 → refused** | **0/4 on all 30** | **only this one title scores** |

`SeqAct_` is the strongest: it is UE3 Kismet's native-registration table (neighbouring bytes read
`USeqAct_Latent` / `execAbortFor` / `USeqAct_Delay`) — the object model itself, not a version string
a publisher can strip. UE4 deleted Kismet for Blueprints, which is why it measures 0 everywhere.

The copyright year tracks the **engine snapshot**, not the ship date: Gal\*Gun DP shipped 2015 and
still reads `Copyright 1998-2012 Epic Games, Inc.` UE4.0 went public in March 2014, so an Epic
notice ending ≤2013 cannot be UE4/UE5. **The inference is one-directional** — a 2014+ year proves
nothing (Fantasynth and NEKOPALIVE both read 2016 and are UE 4.13 / 4.11).

Two rejected markers, so they are not retried:

* **`UE3`** — occurs 3–85 times in **every one** of the 30 supported reference binaries. Using it
  would refuse the entire corpus. (Consistent with `HasUEAnchorNearby` already treating short
  `UE4`/`UE5` tokens as generic noise.)
* **A nonstandard section name** (Gal\*Gun has `.nep`) — not a discriminator: 27 of the 30
  supported binaries carry one (`.uedbg`, `.lpp_pre`, `.msvcjmc`, `.detourc`, …).

Near miss worth knowing: **Manor Lords** (UE 5.5, supported) ships `Copyright Epic Games, Inc. All
Rights Reserved.` with **no year**. The copyright marker therefore requires an *explicit* year — a
"no year OR an old year" rule would refuse it.

Re-measure with `py tools/pe/pre_ue4_markers.py --corpus` (the offline twin of the C++ counter; it
needs the reference corpus, so like `blocktest.py` it cannot run in CI). The **mandatory** live
regression target is a supported title that also reaches the terminal branch — measured, that is
**The Adventures of Elliot** (PE 1.2, detects as 0 → 504). Solarpunk and Titan Quest II do *not*
qualify: their PE resources report 5.7, so they exit at tier 1 and never reach the marker check.

### FUObjectItem Sizes

| Size | Used by |
|------|---------|
| 16B | UE5 standard, some UE4 without GC clustering |
| 24B | Most UE4 (Object\* + Flags + ClusterRootIndex + SerialNumber + pad) |
| 20B | Rare variants |

Detection via `DetectItemSize()`: walk stride-aligned positions, validate with FNamePool string resolution. Score = `named * 10 - bad * 3`. When all scores negative, pick stride with fewest bad items (fallback v5).

### FUObjectItem within-item layout modes (`PackedItem::ItemLayoutMode`)

The byte offset of the `UObject*` *inside* an item changed across UE versions. `DetectItemSize()` auto-detects the mode (and `Aura::GetByIndex` / `GetSerialNumber` dispatch on it):

| Mode | Item layout | Object read |
|------|-------------|-------------|
| `Classic` (UE4.x..UE5.6) | `UObject*`@+0x00, Flags, [ClusterRootIndex], Serial | direct @ item+0x00 |
| `Unpacked57` (UE5.7+ reordered) | `int64 FlagsAndRefCount`@+0x00, `UObject*`@+0x08, Serial@+0x10 | direct @ item+0x08 |
| `Packed57` (UE5.7+ `UE_ENABLE_FUOBJECT_ITEM_PACKING`) | `int64 FlagsAndRefCount`@+0x00, `uint32 ObjectPtrLow`@+0x08 | **reconstructed** |

**Packed reconstruction** (`dll/src/PackedItem.h`):
```
obj = ((FlagsAndRefCount >> 32) & PtrMask) << (32 + AlignBits) | (uint64(ObjectPtrLow) << AlignBits)
```
Constants (calibratable, defaults from assumed UE5.7 source): `AlignBits=3` (UObjectAlignment=8), `PtrMask=0x3FFF` (EInternalObjectFlags_MinFlagBitIndex=14).

**Detection precedence (zero-regression):** two DIRECT passes run first — Classic(+0x00) then Unpacked57(+0x08) — and any (even weak) direct match wins. Packed is a **last resort** tried only in the truly-unrecognized branch (both direct passes found nothing), and only activates when ≥2 *reconstructed* pointers resolve real ASCII FNames (`TryDetectPacked`). So every existing game keeps its exact prior path.

> ⚠️ **`Packed57` is UNVERIFIED** — no shipping game uses `UE_ENABLE_FUOBJECT_ITEM_PACKING` yet (not Epic-default even in `ue5-main`), so the constants and serial offset have never been calibrated against a real game. Activation logs a loud `*** UNVERIFIED ... ACTIVATED ***` WARN; the UI shows a packed-layout badge and embeds a best-effort note in CE XML / CSX / +CE Field exports. The native CE GObjects pointer chain (`get_ce_pointer_info`) cannot express the bit reconstruction → it degrades to the absolute object address. Recalibrate the constants live (no rebuild) via the `set_packed_consts` pipe command (`align_bits` / `ptr_mask_bits` / `force` / `serial_off`), which echoes reconstructed `GObjects[0..7]` samples for eyeball calibration. **Teleport is unaffected** — Wirbel resolves the actor via GWorld property chains, never through FUObjectItem.

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

⚠ **The `Tag` is not always there, and this is the single most-repeated mistake in
this family.** `TPersistentObjectPtr` carried `mutable int32 TagAtLastTest` between
the `FWeakObjectPtr` and the payload up to UE 5.2 (present at `5.2.1-release`
`PersistentObjectPtr.h:243`), and **UE 5.3 deleted it** (absent at `5.3.2-release:228`,
and at 5.4 / 5.6 / 5.8). The payload therefore moves up — by 8 for an 8-aligned
payload, by only **4** for a 4-aligned one:

| payload | align | ≤ 5.2 | ≥ 5.3 | sizeof ≤5.2 / ≥5.3 |
|---|---|---|---|---|
| `FSoftObjectPath` | 8 | `+0x10` | `+0x08` | `0x30` (5.1-5.2) or `0x28` (≤5.0) / `0x28` |
| `FUniqueObjectGuid` | 4 | `+0x0C` | `+0x08` | `0x1C` / `0x18` |

So `+0x10` is right for soft **only up to 5.2**, and is right for lazy in **no era at
all**. Both were hardcoded `0x10` until 2026-09-05.

```
TSoftObjectPtr<T> / TSoftClassPtr<T>      // Phase G
+0x00 FWeakObjectPtr (8B)
[+0x08 Tag (4B) + pad (4B)]                 <- UE <= 5.2 ONLY
+0x10 (<=5.2) / +0x08 (>=5.3) FSoftObjectPath
        UE4 / UE5.0:  FName AssetPathName + FString SubPathString
        UE5.1+:       FName PackageName + FName AssetName + FString SubPathString
total: 0x28 (UE4, and UE5.3+ non-CPN) ... 0x48 (UE5.1-5.2 with CasePreservingName)
```

```
TLazyObjectPtr<T>                          // Phase H
+0x00 FWeakObjectPtr (8B)
[+0x08 Tag (4B)]                            <- UE <= 5.2 ONLY; NO pad, FGuid is 4-aligned
+0x0C (<=5.2) / +0x08 (>=5.3) FUniqueObjectGuid (FGuid = 4 x uint32, 16B)
total: 0x1C (<=5.2) / 0x18 (>=5.3)
```

**We measure this rather than gate on the version**, because a misdetected version is
exactly the case where a hardcoded offset does the most damage:

```
envelope = ElementSize - sizeof(payload)
```

⚠ `ElementSize` **alone is ambiguous** and must never be matched against a table of
whole sizes: `0x28` is both a ≤5.0 tagged soft pointer (`0x10` + a `0x18` FName/FString
path) and a ≥5.3 untagged one (`0x08` + a `0x20` `FTopLevelAssetPath` path). Subtracting
the payload size — which the `FTopLevelAssetPath` discriminator already gives us — is
what makes the answer unique. The arithmetic is `DynOff::PersistentPtrEnvelopeFor`
in [`dll/src/Grimoire.h`](../dll/src/Grimoire.h), pinned by `Test_PersistentPtrEnvelope`;
the latching, the `DYNO:PersistPtr` log line and the fallback live in
`PersistentObjectPtrEnvelope` in [`dll/src/Ubel.cpp`](../dll/src/Ubel.cpp).
The measured offset also goes on the wire as `soft_path_offset`, because the CE XML
exporter used to bake `+10` into every emitted table.

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
bindings live in CoreUObject's global `FSparseDelegateStorage`. **UE 5.0+
layout** (validated against ES2 5.4 PDB + TQ2 5.7):

```
TMap<UObjectBase const*,
     TMap<FName, TSharedPtr<TMulticastScriptDelegate, ThreadSafe>>>
```

**CORRECTED (build 2399).** This doc used to say "UE 4.23-4.27 used
`FObjectKey { FWeakObjectPtr, int32 }` (16B) as outer key". That was wrong on
both counts, and it cost us the whole UE4 sparse-delegate feature:

* The DropIn **UE 4.27.2** PDB gives the global's type as
  `TMap<UObjectBase const*, TMap<FName, TSharedPtr<TMulticastScriptDelegate<FWeakObjectPtr>,0>>>`
  — a **raw pointer** key, identical to UE 5.x. `vendor/UnrealEngine` 5.8
  declares it the same way (`SparseDelegate.h:111`).
* `FObjectKey` is **8 bytes** there — `{ int32 ObjectIndex; int32 ObjectSerialNumber; }`.
  `FWeakObjectPtr` *is* those two ints, so "FWeakObjectPtr + int32 = 16" double-counted.

Every walker constant was re-checked against that PDB and matches exactly
(outer stride `0x60`, value at `+0x08`, `FName` 8B → inner stride `0x20`,
`FScriptDelegate` 16B). The version gate is gone; `Aura::WalkSparseDelegateBindings`
now probes the live outer key and declines to walk if it does not look like a
userspace pointer, so 4.23-4.26 (still unverified — we have no symbols for them)
fail safe instead of misreading memory.

`Aura::WalkSparseDelegateBindings(owner, fname, max)` (build 561+) is
the read-side path. Three phases:

1. AOB-resolve the static via `Genau::FindSparseDelegateStorage`
   (signature `SPARSE_ES2_1` in `Himmel.h`, anchored on
   `NotifyUObjectDeleted` middle with twin-reference to the same static
   8 bytes apart for false-positive resistance).
2. Linear-scan outer TSparseArray for `key == owner`. Stride 0x60 =
   `TSetElement<TPair<UObjectBase*, TMap[0x50]>>`. Allocation bits
   come from inline buffer (≤128 bits) or heap-secondary at TMap+0x20.
3. Linear-scan inner TSparseArray for FName key match. Stride 0x20
   (FName=8) or 0x28 (`bCasePreservingName`: the FName is 0xC but PADS to a 0x10 slot
   inside the TPair, because the TSharedPtr value is 8-aligned). Deref the
   matched `TSharedPtr` (16 B: `Object*` + `RefCount*`) to reach
   `FMulticastScriptDelegate`, then walk
   `InvocationList: TArray<FScriptDelegate>` and resolve each
   `FWeakObjectPtr` via `Ubel::ResolveWeakObjectPtr`.

`WalkInstance`'s sparse handler exposes the result as an implicit
DelegateProperty array (same shape as MulticastInline), so drill-down,
CE XML / CSX export, and Find Refs target navigation reuse existing
wiring. `Aura::FindReferencesToUObject` adds a global pass over
`FSparseDelegateStorage` (after the per-UObject loop) that surfaces
multicast-sparse bindings whose target matches the search address —
closing the v3 gap that previously left this category invisible.

Fallback strings when the walker can't deliver:
`(sparse, bound — UE < 5.0 unsupported)`,
`(sparse, bound — FSparseDelegateStorage AOB not found)`,
`(sparse, bound — owner not in storage)`,
`(sparse, bound — function name not in storage)`. The bare
`(sparse, unbound)` continues to mean `bIsBound == 0`.

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
