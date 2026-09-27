# Native-C Value Scan — Spec & Design

> **Status: P0–P3 SHIPPED and FULLY VERIFIED** — P3's Class Pivot half closed in-game on DumperTest
> on 2026-09-06 (`51668ef0`), its SPC-Query arm on 2026-09-07 (`2b8aad04`, `dae0d319`); the record is
> the Native-C row in `docs/todo.md`. The
> `Native-C (raw)` opt-in is live in Value Search, Group Scan and Snapshot. This header read
> "DESIGN ONLY — not implemented" until 2026-08-05, long after the feature shipped.
> This remains the durable reference for an
> **opt-in** feature that scans the *unmanaged* (non-`UPROPERTY`) bytes inside a
> `UObject` for native C++ gameplay values (HP / MP / stats), in **single Value
> Search**, **Group / multi Value Search**, and **Snapshot** (so downstream **SPC
> Query** + **Class Pivot** inherit them).
>
> House style mirrors [group-value-scan-spec.md](group-value-scan-spec.md): terse,
> `file:line` refs, an architecture diagram, extension-point tables, a caps section.
> All `file:line` citations are against the tree at the time of writing and were
> cross-checked by an adversarial review pass — but **verify against current code
> before implementing** (line numbers drift).
>
> **Implementation status:** **P0 + P1 + P2 SHIPPED** to `dev` (build + 691 dll /
> ~1626 C# tests + AOT 46.5MB all green; **P1 in-game VERIFIED on Octopath**
> — `<raw@0x2C0>` Int32 = 777 found alongside the reflected hit; P2 in-game
> pending). P1 = single Value Search raw-hole scan + Newest-first coupling
> (owner decision, §11). **P2 = Group Scan Native-C** (`ScanForValueGroup(nativeC)`
> + `AppendRawHoleLeaves`: per-object holes folded into the object block,
> **EMIT-ON-MATCH** (a raw leaf is kept only when its bytes satisfy a slot — keeps
> the leaf set tiny so the ≤64/object cap never drops a real match), stride 4, OBJECT
> BLOCK ONLY — never deep; `GroupLeafMeta.isNativeC`/`guessedType` → descriptor →
> per-slot `is_native_c`/`guessed_type`; pipe `native_c`+`newest_first` on
> `begin_group_scan`; UI Native-C checkbox in the group options row (shares
> `NativeCScan`) + `Origin` on `GroupSlotMatch`). **Group also got newest-first**
> (coupled with native): on a huge game (FF7 Rebirth ~433K objects) the single-
> threaded 15s scan truncated before reaching high-index UI widgets, so newest-first
> walks high→low to reach them first. **P2 in-game VERIFIED on FF7 Rebirth**
> (`StatusList_2_C` matched `<raw@0x414>=6568, <raw@0x42C>=9999999`). Newest-first is
> a **visible opt-in checkbox in BOTH single and group modes** (auto-checked with
> native-C, user can uncheck — e.g. for an early-spawned/low-index operated-character
> value vs a late-spawned/high-index UI panel). **P3 = Snapshot/SPC/Pivot** — DONE:
> `CaptureSnapshotChunk(captureNativeC)` + `AppendRawHoleFields` (GuessGapTypes per
> hole → `NormalizeGuessedTypeToProperty` to canonical type → drop Pointer/Padding →
> synthetic `<raw@0xNN>` `SnapshotField`s, numericScope-filtered, ≤256/object); pipe
> `native_c` on `snapshot_chunk`; C# `SnapshotViewModel.IncludeNativeFields` toggle +
> `str.Snapshot.Intro` updated. **SPC Query + Class Pivot consume raw rows with ZERO
> code changes** — they key on `prop_name` (which encodes the offset) + the canonical
> `declared_type` (so `SnapshotNumeric.TryFromHex` decodes it), reusing the existing
> `fields` schema (no migration). **The whole Native-C feature (P0–P3) is now shipped;
> P3 in-game verify was pending then; it closed 2026-09-06 / 09-07 (see the header).**

-----

## 0. Provenance & confidence

This spec was produced by a 7-reader code-survey → architect draft → 3-lens
adversarial critique (feasibility, safety/noise, integration). All three critics
returned **approve-with-changes**: the architecture is sound, and the corrections
below are folded in. The two highest-impact corrections were independently
re-verified against the live tree by the author:

- **Guess type-name mismatch** — `SnapshotNumeric.TryFromHex` / `Render`
  ([SnapshotNumeric.cs:33-89](../ui/UE5DumpUI/Services/SnapshotNumeric.cs)) switch on
  **exact** `"FloatProperty"`/`"IntProperty"`/… with `default: return false` /
  `_ => hex`. **Confirmed.** → §4.3 mandatory normalization.
- **Snapshot has no wall-clock deadline** — `CaptureSnapshotChunk`
  ([Aura.cpp:7065-7082](../dll/src/Aura.cpp)) has only a `Tot::Requested()` check at
  `(i & 0xFFF)`; the `kDeadline` guards live in `ScanForValue` (~1985) and
  `ScanForValueGroup` (~6783), **not** here. **Confirmed.** → §11 must ADD a deadline.

-----

## 1. What it does / problem statement

UE reflection (`UProperty` / `FField`) only describes **reflected** fields — members
declared `UPROPERTY()`. Many games keep gameplay values (HP / MP / stamina / stats)
as plain C++ members that are **not** `UPROPERTY`. Those values physically live inside
a `UObject`'s heap allocation but have **no offset entry in the reflection chain**, so
`Ubel`'s class walker has no path to read them. Today the Value Search tab is
hard-locked with a banner stating exactly this
([ValueSearchPanel.axaml:23](../ui/UE5DumpUI/Views/ValueSearchPanel.axaml), string
`str.VS.Banner` at [en.axaml:52](../ui/UE5DumpUI/Resources/Strings/en.axaml)):

> "Native C++ fields (non-UPROPERTY) cannot be found here — even if they live inside a
> UObject's memory. Use Cheat Engine's raw memory scan for those."

This caveat is a **locked-in UX rule** (see memory `project-value-search-caveats`).
This feature turns that caveat into an **opt-in capability** (default OFF) that, in
addition to reflected fields, scans the raw bytes in the **holes** between reflected
fields and interprets them with the existing "Guess What" heuristic
([Ubel.cpp:2842 `GuessGapTypes`](../dll/src/Ubel.cpp)).

**Key advantage over CE:** because we know each reflected field's
`[offset, offset+size)`, we confine the raw scan to the **unmanaged holes** and never
re-scan bytes that already produce structured (typed, zero-false-hit) candidates. CE
has no type info and scans everything.

-----

## 2. Scope decision — holes-only, full-`PropertiesSize` window

### 2.1 Holes-only (decision)

**Scan HOLES ONLY. Do NOT re-interpret reflected bytes.** Reflected fields already
produce structured candidates in `Radar`/`Orden`; re-scanning them as raw guesses
creates duplicate/conflicting candidates and wasted I/O (≈80% of a typical class's
footprint is already covered). The user's complaint targets the gaps, not re-parsing
known fields. (A future "guessed re-interpretation of reflected bytes" mode could reuse
`Ubel::WalkInstance`'s `fillGaps` seam, but is out of scope.)

### 2.2 Scan window

```
window = [ headerEnd , bound )
```

**Start (`headerEnd`)** — always skip the full `UObject` header; those bytes are UE
metadata, never user data. Use the same formula the existing gap pass uses
([Ubel.cpp:5117](../dll/src/Ubel.cpp)):

```cpp
int32_t headerEnd = DynOff::UOBJECT_OUTER + 8;   // 0x28 normal, 0x30 when CasePreservingName
```

`DynOff::UOBJECT_OUTER` is set dynamically to `0x20` / `0x28` at
[Genau.cpp:2366-2371](../dll/src/Genau.cpp) (CPN → 0x28). Native-C scan targets
**UObjects only**, so always use `headerEnd` (never offset 0).

**Bound = the class `PropertiesSize`.** This is the *exact* end of the reflected region,
so every native member of *this* class lives below it, and it is bounded by the game's
own class size (not an arbitrary window). **Prefer the cached value** from
`Ubel::WalkClassEx(cls).PropertiesSize` (already resolved per class) over a fresh SEH
read of `cls + DynOff::USTRUCT_PROPSSIZE` (0x58, resolved at
[Genau.cpp:2582/3183](../dll/src/Genau.cpp)) — this also keeps the per-class hole cache
correct (§3.1).

```cpp
const ClassInfo& ci = Ubel::WalkClassEx(cls);          // cached
int32_t propsSize = ci.PropertiesSize;
bool propsOk = (propsSize > headerEnd && propsSize <= kPropsSizeSanity);  // sanity gate
int32_t bound = propsOk ? propsSize : kFallbackWindow;  // see §2.3
```

### 2.3 Caps & fallbacks (corrected — do **not** cap at 4 KB)

> ⚠ **Critique correction:** the draft capped the window at `0x1000` (4 KB). Large UE
> actor/character classes — *exactly* the gameplay classes this feature targets —
> routinely exceed 4 KB of `PropertiesSize`, so a 4 KB cap would silently make native
> members beyond +4 KB unreachable. **Use the full `PropertiesSize`** (it is already a
> per-class bound), with only a generous corruption sanity cap.

- `kPropsSizeSanity = 0x10000` (64 KB) — purely a garbage/packed-layout guard
  (mirrors the `propsSize <= 0x100000` gate around [Aura.cpp:~1466](../dll/src/Aura.cpp)).
- `kFallbackWindow = 0x400` (1 KB) — conservative window when `PropertiesSize` is
  missing/corrupt (non-standard / packed engines, e.g. the Avowed-class layouts in
  [reversing-nonstandard-ue-games.md](reversing-nonstandard-ue-games.md)).
- If `propsSize > kPropsSizeSanity`, clamp to `kPropsSizeSanity` **and surface a
  "window truncated" indicator** so the user knows native members past the cap weren't
  scanned (don't fail silently).

**"Next object" boundary is NOT feasible** — the heap tracks no object boundaries; the
gap to the next `GObjects` entry may be padding, alignment, or an unrelated allocation.
`PropertiesSize` is the only safe bound.

-----

## 3. Hole computation

Per class, compute the complement of reflected coverage within `[headerEnd, bound)`.

### 3.1 Algorithm + shared helper

> ⚠ **Critique correction (refactor scope understated):** the existing inline gap pass
> ([Ubel.cpp:5130-5161](../dll/src/Ubel.cpp)) merges **per-instance `LiveFieldValue`**
> intervals, *not* class-level `WalkClassEx(cls).Fields`. The two field-size models are
> not byte-identical, so a "lift-and-shift that produces byte-identical results" is
> **unsatisfiable**. Instead, extract a helper that takes the **interval list as input**
> (decoupled from the field-size source):

```cpp
// Ubel.h (new, public)
struct Interval { int32_t start; int32_t end; };           // [start, end)
std::vector<Interval> ComputeHoles(const std::vector<Interval>& occupied,
                                   int32_t windowStart, int32_t windowEnd);
```

`ComputeHoles` clamps each occupied interval to `[windowStart, windowEnd)`, sorts,
merges overlapping/adjacent, and returns the complement. Both callers feed their own
intervals:

- `WalkInstance` keeps feeding `LiveFieldValue`-derived intervals → its gap output is
  **unchanged** (regression goal: "WalkInstance gap output unchanged", not
  "byte-identical to a class-level walk").
- The Native-C paths feed `ClassInfo.Fields`-derived intervals (per §3.2).

### 3.2 Field intervals — handle static C-arrays (`Type Foo[N]`)

> ⚠ **Critique correction (correctness bug):** `FieldInfo.Size` is `ElementSize`
> (ONE element) — `ArrayDim` is never read into `FieldInfo`
> ([Ubel.cpp:441 / 506](../dll/src/Ubel.cpp); `ArrayDim` only appears in a `Genau.cpp`
> comment). For a fixed C-array `UPROPERTY` the field occupies `ElementSize * ArrayDim`,
> so the naive interval `[Offset, Offset+ElementSize)` leaves the rest as a **phantom
> hole** that re-scans reflected array bytes — directly violating §2.1.

**Fix:** compute each field interval as
`[Offset, Offset + ElementSize * max(1, ArrayDim))`. This requires reading `ArrayDim`
(its offset is engine-version dependent — see [Genau.cpp:3156-3163](../dll/src/Genau.cpp),
it precedes `ElementSize`) into `FieldInfo` (a small, broadly-useful `Ubel` addition).
If `ArrayDim` cannot be resolved on a given layout, fall back to single-element size
**and document the phantom-hole risk as a known limit** (the same latent bug already
exists in the current Guess-What gap pass — fixing it there is a side benefit).

`StructProperty` interiors are already correct: `FieldInfo.Size = ElementSize` = full
struct size, so the whole struct interval is occupied (not a hole). Inherited fields are
already correct: `WalkClassEx` merges the full SuperStruct chain
([Ubel.cpp:611-650](../dll/src/Ubel.cpp)), so inherited reflected offsets are excluded
from holes.

### 3.3 Per-class hole cache

The merged reflected-coverage interval set depends **only on the class layout** (offsets
are build-stable) and the window (derived from the class via cached `PropertiesSize`).
Cache the holes keyed by `cls` (UClass*). Per-object cost then reduces to: read the body
once + walk cached holes. **The cache key must not vary per object** — because `bound` is
derived from the class (cached `PropertiesSize`), the per-object `PropertiesSize` re-read
in the draft is redundant and is dropped (this also removes the "two objects of the same
class capped differently" cache-poisoning hazard the critique raised).

### 3.4 Stride within holes

- **Single / Group:** test the user's chosen `DataType` width at each **aligned** offset
  inside each hole. Default alignment **4 bytes**; expose `1 / 2 / 4 / 8`. UE layout is
  overwhelmingly 4-byte aligned; stride-4 cuts the candidate-offset count ~75% vs
  stride-1. Misaligned native values (hand-packed structs / bitfields) are rare — stride-1
  is the escape hatch.
- **Snapshot:** `GuessGapTypes` picks the widths (it walks the gap itself).

-----

## 4. Guess integration

Two distinct consumption modes; pick per call-site.

### 4.1 Snapshot mode — pre-guess each hole (reuse `GuessGapTypes`)

`GuessGapTypes(uintptr_t baseAddr, int32_t gapStart, int32_t gapEnd, std::vector<LiveFieldValue>& out)`
([Ubel.cpp:2842](../dll/src/Ubel.cpp)) already does
`padding → pointer → float → double (aliasing guard from 75ea723) → int32 → int16 → byte`,
with confidence-suffixed type names (`Float` vs `Float?`). It reads memory itself via
`Macht`. It is currently `static`.

**Required extraction (Ubel):** declare it on the public `Ubel::` surface
([Ubel.h](../dll/src/Ubel.h)) so `Aura` can call it. Snapshot consumes it through a
**mandatory** translation (§4.3), not verbatim.

### 4.2 Single / Group mode — test the user's width (don't gate on the guess)

The user supplies a concrete `DataType`. Pre-guessing per hole would discard hits whose
byte pattern doesn't *look* like the user's type but *is* the value they want. So:

- For each hole, slide by alignment (§3.4); at each offset read `SizeOf(dt)` bytes and run
  the **same** `Radar::ComparePredicate(dt, st, readBuf, target, …)` used for reflected
  candidates. Emit a candidate on match.
- Use Guess **only to label** a surviving hit's guessed type for display — never to gate
  the match.

This keeps the predicate path identical to reflected scanning; refine, view, and wire
formatting need zero new logic (§5.3).

### 4.3 ⚠ MANDATORY type normalization (Guess → canonical property-type string)

> **This is the fix for the most-cited defect (raised by 2 of 3 critics, re-verified).**
> `GuessGapTypes` emits suffixed / non-property names (`"Float?"`, `"Int32?"`,
> `"Int16?"`, `"Byte?"`, `"Double?"`, `"Pointer?"`, `"Padding"`). The downstream
> consumers require **exact** canonical strings:
> - C# `SnapshotNumeric.TryFromHex` / `Render`
>   ([SnapshotNumeric.cs:33-89](../ui/UE5DumpUI/Services/SnapshotNumeric.cs)) →
>   non-match ⇒ `numeric_value` NULL and Pivot renders raw **hex**.
> - DLL `Radar::TryDataTypeFromPropertyTypeName`
>   ([Radar.cpp:318-331](../dll/src/Radar.cpp)) → non-match ⇒ group refine `continue`s
>   and **silently drops the leaf at the first Next Scan**
>   ([Aura.cpp:7034-7035](../dll/src/Aura.cpp)); single refine
>   ([Aura.cpp:6232](../dll/src/Aura.cpp)) the same.

Every native field/leaf MUST store the **canonical** property-type string in its
`fieldType`. The human-facing guessed label (e.g. `"Int32?"`) lives in a **separate**
field (`GuessedType` / `Origin`, §5, §10), never in `fieldType`.

| Guess output | Canonical `fieldType` | Notes |
|---|---|---|
| `Float` / `Float?` | `FloatProperty` | strip `?` |
| `Double` / `Double?` | `DoubleProperty` | |
| `Int32` / `Int32?` | `IntProperty` | |
| `Int16` / `Int16?` | `Int16Property` | |
| `Byte` / `Byte?` | `ByteProperty` | |
| `Int64` / `Int64?` | `Int64Property` | if `GuessGapTypes` emits it |
| `Pointer?` | **dropped** | not a gameplay numeric (or → `UInt64Property` only if pointer-as-value is ever wanted) |
| `Padding` | **dropped** | zero/uninit region |

Add a unit test asserting **every** type `GuessGapTypes` can emit round-trips through
both `SnapshotNumeric.TryFromHex` and `Radar::TryDataTypeFromPropertyTypeName` after
normalization.

-----

## 5. DLL data model — synthetic raw descriptors / leaves

Both pools already accept arbitrary metadata; **no `FProperty*` is required** anywhere
(re-verified: `FieldDescriptor` is pure strings+offset+mask
[Radar.h:311-320](../dll/src/Radar.h); `Orden::Leaf` is `{position,width,bytes,…}`
[Orden.h:58-64](../dll/src/Orden.h); both refine paths re-read the absolute address and
re-resolve width from the `fieldType` **string** — never deref a property pointer).

### 5.1 Single — synthetic `FieldDescriptor`

Intern one per `(class, offset, width)`:

| Field | Native-C value |
|---|---|
| `className` | object's class name (`Ubel::GetName`) |
| `definingClassName` | `""` (unmanaged — unknown defining class) |
| `fieldName` | synthetic, encodes the offset: `"<raw@0x" + hex(offset) + ">"` |
| `fieldType` | **canonical** width string from §4.3 (`IntProperty` / `FloatProperty` / …) |
| `fieldOffset` | the raw byte offset |
| `boolFieldMask` | `0xFF` |
| `isNativeC` (NEW) | `true` — drives UI badging; wired as `is_native_c` |
| `guessedType` (NEW) | human label `"Int32?"` for display only |

`Candidate` ([Radar.h:344](../dll/src/Radar.h)) needs no change: `addr = obj + offset`,
`descriptorIdx` → the synthetic descriptor, `elementIndex = -1`.

### 5.2 Group — synthetic `Orden::Leaf` + `GroupLeafMeta`

```cpp
Orden::Leaf l;
l.position     = rawOffset;          // direct-field convention (matches CollectGroupLeaves)
l.width        = userOrGuessedDt;
std::memcpy(l.bytes, readBuf, SizeOf(l.width));
l.elementIndex = -1;
```

Parallel `GroupLeafMeta` ([Aura.cpp:6486](../dll/src/Aura.cpp)):
`fieldName = "<raw@0x..>"`, `fieldType =` **canonical** string (§4.3 — **not** a
`"RawInt32?"` variant; that would break refine, see §4.3), `definingClass = ""`,
`offset = rawOffset`, `leafAddr = obj + rawOffset`, `ownerAddr = obj`,
`ownerClass = className` (so the Pivot handoff targets the candidate's class),
`boolMask = 0xFF`, plus the `isNativeC` / `guessedType` display fields.

### 5.3 leafAddr math + refine contract (no change)

Raw leaves use the direct-field formula `leafAddr = obj + rawOffset`. Refine re-reads the
absolute address and re-resolves width from the stored canonical `fieldType`:

- Single: `RefineCandidates` reads `c.addr`, resolves via
  `TryDataTypeFromPropertyTypeName(desc.fieldType,…)` ([Aura.cpp:6232](../dll/src/Aura.cpp)). **No change.**
- Group: `RefineGroupCandidates` re-reads `sm.leafAddr` SEH-safe
  ([Aura.cpp:7032-7035](../dll/src/Aura.cpp)). **No change** (given canonical `fieldType`).

Contract holds because UObjects don't move once allocated; a freed object → SEH-zeroed /
faulted read → predicate drop. Same staleness behavior as reflected direct fields.

-----

## 6. Single Value Search integration

**Skip the native pass for non-numeric sessions.** The single-scan path also supports
String / FName / FText / Vector / Bool ([Aura.cpp:6050-6098](../dll/src/Aura.cpp)). A raw
hole can't sensibly be an `FString`/`FName`/bool-bitfield, so `ScanForValue` runs the
native pass **only** when the session `DataType` is numeric (incl. `NumericNoByte` /
`NumericAll`); otherwise it is a no-op.

**Minimal touch set (DLL):**

| File | Change |
|---|---|
| `Aura.cpp` `ScanForValue` | Add `bool nativeC`, `int32_t nativeAlign` params. After the reflected per-object loop (~6099), if `nativeC` && numeric `dt`: read body `[headerEnd, bound)` **once** SEH-safe (§11 read-once-clamp), walk cached holes, test user width per aligned offset, `emitCandidate(obj+off, internRawDesc(...), -1, …)`. |
| `Aura.cpp` raw-descriptor intern | Per-thread map keyed `(className, offset, width)` → descriptor index (mirror existing descriptor interning). |
| `Radar.h` `FieldDescriptor` | Add `isNativeC`, `guessedType`. |
| `Fern.cpp` `begin_value_scan` | Parse `native_c` (+ `native_align`) → pass to `ScanForValue`. |

`RefineCandidates`, V3-A pooling, V3-C windowed view, and wire formatting need **no
changes** — synthetic descriptors flow through identically.

-----

## 7. Group Scan integration

**Mirror the Deep / Cross-object opt-in template exactly**
([Aura.cpp `ScanForValueGroup` ~6962-6968](../dll/src/Aura.cpp)):

```cpp
CollectGroupLeaves(obj, className, cls, 0, "", wantByte, 0, visited, leaves, metas, kLeafCap);
if (crossObject) AppendOwnedSubObjectLeaves(obj, wantByte, leaves, metas, kLeafCap);
if (nativeC)     AppendRawHoleLeaves(obj, cls, slots, leaves, metas, kMaxRawHolesPerObject);  // NEW
if (leaves.size() >= nSlots && Orden::MatchGroup(leaves, ordenSlots, matchOut))
    emitGroupCandidate(...);
```

`Fern.cpp` `begin_group_scan` parses `native_c` (parallel to `deep` / `cross_object`),
passes it through. **`Orden.h` is unchanged** — it is the explicit reuse seam
(group-value-scan-spec §3.1). `AppendRawHoleLeaves` computes holes, slides by alignment,
reads each candidate offset, appends `Orden::Leaf` + `GroupLeafMeta` per §5.2.

### 7.1 Selectivity — the AND does **not** save small common values

> ⚠ **Critique correction (selectivity hazard):** the draft claimed "the AND remains the
> gate, verified empirically." That holds for **reflected** leaves (each is a real typed
> field). It does **not** hold for raw holes when the target is a **small common value**
> (`0`, `1`, `-1`, `100` — precisely the HP-class values this feature exists to find):
> with ~hundreds of aligned hole offsets per object, the probability an object has *some*
> hole equal to a small value approaches 1, so the SDR (`Orden::MatchGroup`, Kuhn,
> `perSlotCap=8`) can assemble N distinct offsets that are all **noise**. The AND requires
> distinct *offsets*, not distinct *meaning*.

**As shipped (P2):**

1. **EMIT-ON-MATCH (primary mechanism)** — `Aura::AppendRawHoleLeaves` emits a raw leaf
   ONLY when its bytes satisfy at least one slot's predicate (the same leaves Orden would
   keep). Unlike the reflected `CollectGroupLeaves` (every field, because fields are few),
   a raw "emit-all" pass would add thousands of useless leaves per object — which both
   slows the scan AND lets the per-object cap truncate a real 2nd-value match past the cap
   (the FF7 Rebirth bug: emit-all + a 64-cap dropped `9999999`'s leaf on a wide widget).
   Emit-on-match keeps the set tiny (matches are rare), so the cap is never the binding
   constraint and the SDR stays cheap.
2. **Per-object caps** — `kMaxRawGroupLeaves = 64` emitted matches + `kMaxRawGroupProbes =
   65536` offset×width probes, far below the block-level `kMaxBlockLeaves = 1024`.
3. **Object block only** — raw holes are appended to the actor's own block (alongside the
   reflected + cross-object leaves), **never** to the deep-container blocks. Stride 4; only
   the union of slot widths is probed.
4. **Newest-first (coupled with native)** — group scan is single-threaded and was low→high,
   so on a huge game (FF7 Rebirth ~433K objects) the 15 s deadline truncated before reaching
   high-index UI widgets (the just-spawned objects that hold native values). `newestFirst`
   walks high→low (`objIdx = total-1-i`) so the newest objects are reached first; the UI
   couples it on with native-C (same as single-value).
5. **Banner + workflow guidance** — the Native-C banner + the group tooltip steer the user
   to **distinctive values** (the value-AND makes them highly selective) and to converge
   with Next Scan.

**Deferred (owner can revisit):** the original "hard-deny small common targets (`|value| <
256`) on the first scan" was simplified to the cap + guidance above. Rationale: group
refine (`RefineGroupCandidates`) only *narrows* existing candidates — it cannot *add* raw
leaves — so a first-scan deny would make native group unusable for small-value groups
rather than "allow after refine". The cap + `maxResults` + the 15 s deadline already bound
the **cost** deterministically (no crash/hang); the residual **noise** for all-small groups
is the user's informed choice (banner-warned), exactly as in single-value P1. If users
drown on small-value groups, add a soft warning (decode `SlotSpec.value`; flag when every
slot `|value| < 256`) rather than a hard block.

-----

## 8. Snapshot + SPC + Class Pivot integration

**No SQLite migration.** The `fields` table
([SnapshotStore.cs:134-165](../ui/UE5DumpUI/Services/SnapshotStore.cs)) already has
`prop_name / prop_offset / declared_type / hex / numeric_value`. Native fields are just
rows with synthetic names and **canonical** `declared_type` (§4.3).

**Capture (DLL):** add `bool captureNativeC` to `CaptureSnapshotChunk`
([Aura.cpp:7065](../dll/src/Aura.cpp)). After reflected fields, call a new
`AppendRawHoleFields(obj, cls, so.fields)` that runs §4.1 (pre-guess each hole) then
**normalizes** each guess to a canonical `declared_type` (§4.3), **dropping
`Pointer?`/`Padding`** so the Pivot value picker isn't polluted with non-numeric rows.
`Fern.cpp` `snapshot_chunk` parses `native_c` → pass through. Honors the snapshot
`numericScope` filter where it makes sense (note: `GuessGapTypes` emits all widths;
post-filter by `numericScope` after normalization for consistency with
[`SelectSnapshotNumericFields` Aura.cpp:7106](../dll/src/Aura.cpp)).

**Synthetic name `"<raw@0xNN>"`** cannot collide with UE property names (no `<`/`@` in
them), is deterministic per `(class, offset)`, and is the **discriminator** for SPC/Pivot
UI badging (snapshot rows lose any in-memory flag through SQLite — the name prefix is what
survives).

**SPC join — zero code changes, but the rationale is offset-via-name (corrected).** The
join keys are **not** all `prop_name + offset`: only **Strict** includes `prop_offset`;
**Loose** keys on `outer_chain`; **InSession** keys on `gobjects_index`
([SnapshotStore.cs:705-717](../ui/UE5DumpUI/Services/SnapshotStore.cs)). What makes raw
fields join across captures in **all three** modes is that the **offset is encoded in
`prop_name`** (`<raw@0x..>`), so any key mode distinguishes raw fields by offset identity.
`LoadIntersectedCandidatesAsync` is name-agnostic.

**Class Pivot — zero code changes.** `ListPivotFieldsAsync`
([SnapshotStore.cs:960](../ui/UE5DumpUI/Services/SnapshotStore.cs)) groups by
`(prop_name, declared_type)`; normalized raw fields appear in the key/value pickers
automatically. `PivotEngine.Build` is name-agnostic.

**Cross-session stability caveat (must document — corrected).** Raw offsets are
**build-stable, not patch-stable**. Because a raw field's *identity is its offset* (baked
into `prop_name`), it does **not** tolerate layout drift in **any** mode — including
Loose (reflected Loose tolerates drift precisely by *not* keying on offset; raw fields
can't). After a game patch shifts a struct, raw `<raw@0x..>` names diverge and simply
stop re-joining (conservative — empty intersections, never corruption).

-----

## 9. Pipe protocol changes (back-compat — absent key = OFF)

[pipe-protocol.md](pipe-protocol.md) to be updated. New fields (follow the existing
"attach only when true" convention — [DumpService.cs:1552/1764](../ui/UE5DumpUI/Services/DumpService.cs)):

**`begin_value_scan`** / **`begin_group_scan`:**
```jsonc
"native_c": true,        // bool, default false (omit when off)
"native_align": 4        // stride 1|2|4|8, default 4
```

**`snapshot_chunk`:** `"native_c": true`. Raw guesses are written as ordinary `fields[]`
rows with `<raw@0x..>` names + canonical `declared_type`. Live-scan responses (value/
group) MAY tag each candidate/leaf with `is_native_c` + `guessed_type` for UI badging
(snapshot rows use the `<raw@` name prefix as the discriminator instead).

Existing clients send none of these → defaults OFF. No breaking change.

-----

## 10. C# UI changes

**Single + Group toggle** — add `[ObservableProperty] private bool _nativeCScan;` to
[ValueSearchViewModel.cs:74](../ui/UE5DumpUI/ViewModels/ValueSearchViewModel.cs) (beside
`_deepScan`). Bind a `<CheckBox>` in **both** mode rows of
[ValueSearchPanel.axaml](../ui/UE5DumpUI/Views/ValueSearchPanel.axaml) (single opts row
~130-156; group opts row ~382-406, beside Deep / Cross-object). Optionally a stride combo
(`native_align`). Pass `NativeCScan` into `BeginValueScanAsync` (~609) and
`BeginGroupScanAsync` (~958).

**DumpService** — attach the flag conditionally, **AOT-safe cast**:
`if (nativeC) req["native_c"] = (JsonNode?)true;` (the bool indexer is the documented
IL2026/IL3050 trap — see `MEMORY.md` group-scan gotcha). Add `native_c` params to
`BeginValueScanAsync` / `BeginGroupScanAsync`.

**Snapshot toggle** — `[ObservableProperty] private bool _includeNativeFields;` in
[SnapshotViewModel.cs](../ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs); pass to the
`snapshot_chunk` request.

**Banner change** — replace `Text="{StaticResource str.VS.Banner}"`
([ValueSearchPanel.axaml:23](../ui/UE5DumpUI/Views/ValueSearchPanel.axaml)) with a bound
`BannerText`:
- OFF → the existing exact disclosure text (preserves the locked-in UX rule).
- ON → "Native-C opt-in active: also scanning raw/unmanaged bytes (PropertiesSize window)
  via the Guess-What heuristic. **Noisy on first scan — narrow by class or refine with
  Next Scan.** Slower per object."

**Result badge** (recommended in the first phase, low cost) — an `Origin` column /
emoji prefix on the Class.Field cell: `"Reflected"` vs `"Native-C (Int32?)"`, sourced from
the new `IsNativeField` + `GuessedType` on `ValueCandidate` / `GroupSlotMatch`
([ValueScanModels.cs](../ui/UE5DumpUI/Models/ValueScanModels.cs)) — plain auto-properties
(AOT-safe), parsed in the hand-rolled JSON readers (no `JsonSerializer` reflection).

**Touch-points the draft missed (corrected — all required):**
- **Two banner tests, not one.** Update *both* the `en.axaml` literal-substring guard
  (`ValueSearchTests.cs:~2254`) **and** `Banner_IsReferencedByValueSearchPanel`
  ([ValueSearchTests.cs:~2287](../ui/UE5DumpUI.Tests/ValueSearchTests.cs)), which asserts
  the panel source literally contains `str.VS.Banner` (broken by the `{Binding}` swap).
  Either keep `BannerText` (OFF) routing through the `str.VS.Banner` resource (so the key
  still appears in AXAML) or update that test to assert the disclosure reaches the panel
  via `BannerText`.
- **Snapshot intro string.** [en.axaml:306 `str.Snapshot.Intro`](../ui/UE5DumpUI/Resources/Strings/en.axaml)
  currently says "Native (non-UPROPERTY) fields are not captured." — must change when the
  `_includeNativeFields` toggle exists, or the panel contradicts its own feature.

**AOT:** `[ObservableProperty]` (source-gen), `{StaticResource}`/compiled bindings, enum
`ToString()` are AOT-safe. The only trap is the `JsonNode` bool cast above.
`TreatWarningsAsErrors=true` + full `-Mode Publish` verify is mandatory (`-Target UI`
skips DevShell → link.exe 9009).

-----

## 11. Safety, noise & selectivity

**A raw first-scan is intentionally noisy** — like CE's first scan — and only useful after
narrowing (class filter) or Next-Scan convergence. The dominant risk areas, with the
corrected mitigations:

| Concern | Mitigation |
|---|---|
| **Default state** | OFF everywhere; absent pipe key = off. |
| **Index-ordered truncation defeats the goal** (⚠ critique) | A stride-4 scan of `[0x28, PropertiesSize)` is hundreds–thousands of offsets/object; on a 1M-object game the result cap is exhausted within the first few-dozen *low-index* objects, and `ConcatTruncate` keeps **low-index** entries (CDOs / templates / engine objects — the **least** useful), truncating the high-index freshly-spawned pawn the user wants. **A lower cap makes this worse.** → **Primary workflow = class-scoped** (the user knows the pawn class from InstanceFinder / Related Objects); a class filter sidesteps both the 1M-object cost and the truncation. For an un-scoped native scan, **walk GObjects high-index-first** (reuse the `FindInstancesByClass` "Newest first" ordering insight, memory `project-related-objects-panel`) so truncation keeps runtime instances. Surface a **saturation warning** when `matches == max_results && native_c`. |
| **False-hit explosion (first scan)** | stride-4 default; confidence-suffixed display labels; lower native result cap (≈5,000); banner warns. *Combined with* class-scoping/high-index-first above. |
| **Snapshot has NO deadline** (⚠ critique, verified) | `CaptureSnapshotChunk` ([Aura.cpp:7065-7082](../dll/src/Aura.cpp)) has only a `Tot::Requested()` check — **no `kDeadline`**. **ADD** a wall-clock deadline (mirror `ScanForValue`'s `kDeadlineMs=15000 + t0`) to its outer loop **before** wiring `captureNativeC`, and have `AppendRawHoleFields` honor it + `kMaxRawHolesPerObject`. |
| **SEH fault-walk on partial tails** (⚠ critique) | `Macht::ReadBytesSafe` returns `false` for the *whole* buffer on fault (no partial zero-fill); letting `GuessGapTypes` `pos++`-retry into an unmapped tail = hundreds of sequential faults/object. **Read the `[headerEnd, bound)` body ONCE up front, detect the readable length, clamp `bound` to it**, scan in-buffer. Don't fault-walk. |
| **Per-object op ceiling** | State a per-object budget (offset count × width) + early-out: first unreadable read in a body → abandon the rest of that body. |
| **Single-thread option** | Honor the existing `Parallel` toggle (off = serial reads, anti-tamper-friendly). |
| **Soft circuit-breakers** | `kMaxRawHolesPerObject` (≈128 single / ≈64 group); skip all-zero hole regions beyond a threshold (corruption guard). |
| **Group leaf budget** | Raw leaves share `kMaxBlockLeaves`; overflow → stop + budget hint. Never folded into Deep. Plus the §7.1 small-common-value denial. |
| **Window cap** | Use full `PropertiesSize` (§2.3); only the 64 KB corruption cap truncates, with a surfaced indicator. |

-----

## 12. Phased rollout

| Phase | Ships | Effort | Risk |
|---|---|---|---|
| **P0 — shared prerequisites** | `Ubel::ComputeHoles` (interval-list input, §3.1) + public `Ubel::GuessGapTypes` + `ArrayDim` in `FieldInfo` (§3.2) + the Guess→canonical normalization table + its round-trip test (§4.3) + the `CaptureSnapshotChunk` deadline (§11). | Medium | Low (pure refactor + guards; regression-test Guess-What) |
| **P1 — Single Value Search** | `native_c`/`native_align` on `begin_value_scan`; `AppendRawHole` pass in `ScanForValue` (numeric-only, read-once-clamp, high-index-first / class-scoped); synthetic descriptors; UI checkbox + dynamic banner + Origin badge. | Medium | Medium (noise/truncation UX is the open question — §11) |
| **P2 — Group Scan** | `AppendRawHoleLeaves` + `native_c` on `begin_group_scan`; small-common-value denial + per-object cap; NOT in Deep. Ships after P1 feedback. | Low–Medium | Medium (selectivity — mitigated by §7.1) |
| **P3 — Snapshot / SPC / Pivot** | `captureNativeC` on `CaptureSnapshotChunk` + `snapshot_chunk` flag; normalized raw rows in existing schema (no migration); SPC/Pivot already consume them. Update `str.Snapshot.Intro`. | Medium | Low (downstream is generic once normalization + deadline land) |

-----

## 13. Module naming (Frieren convention)

**No new C++ module/namespace is warranted.** The feature reuses existing modules:

- `Ubel` (UStructWalker) — `ComputeHoles` + public `GuessGapTypes` + `ArrayDim` (its domain).
- `Aura` (ObjectArray) — `AppendRawHoleLeaves` / `AppendRawHoleFields` + the
  `ScanForValue` / `ScanForValueGroup` / `CaptureSnapshotChunk` param additions (already
  hosts the Deep / Cross-object equivalents).
- `Radar` (ValueScan) — synthetic `FieldDescriptor` + `isNativeC` / `guessedType`.
- `Orden` (GroupMatch) — unchanged seam.

Per [CLAUDE.md](../CLAUDE.md) Rules, a Frieren roster name is required only for a **new
file with its own namespace** — none is introduced here. **DECISION FLAG for the
owner/implementer:** if the raw-hole helpers grow enough to justify their own `.cpp/.h` +
namespace, that new module MUST take an unused Frieren roster name
([naming-convention.md](naming-convention.md)) with the required header comment + 🟢 status
flip — never `RawScan` / `NativeScan`.

-----

## 14. Risks & open questions

1. **Noise + index-truncation UX (biggest).** Recommended resolution (§11): make
   **class-scoped** the primary native-C workflow; for un-scoped scans, **high-index-first
   ordering** + a saturation warning; banner steers to refine. **Owner call:** is a
   first-scan-over-all-objects native option offered at all, or is it gated to
   class-scoped / Next-Scan-only? Lean toward gating.
2. **`ArrayDim` resolution per UE version** (§3.2) — offset is version-dependent; if it
   can't be resolved on a layout, fall back to single-element size and document phantom-hole
   risk. (Fixing it also fixes the latent bug in the existing Guess-What gap pass.)
3. **`ComputeHoles` extraction must not regress Guess-What** — goal is "WalkInstance gap
   output unchanged," verified by the existing Guess-What tests + a new class-level test.
4. **`PropertiesSize` reliability on non-standard engines** (packed / garbage layouts,
   Avowed-class) — `kFallbackWindow` + sanity cap handle it but may under/over-scan.
   Document as a known limit.
5. **Cross-session raw-offset stability** — build-stable only; patch shifts break SPC
   re-join in *every* mode incl. Loose (§8). Documented.
6. **Anti-tamper** — extra reads over unmanaged regions are ordinary reads; single-thread
   option covers the cautious case. Low risk; watch on aggressive-AC titles.
7. **`Pointer?` handling** — dropped by default (not a gameplay numeric). Revisit only if a
   "find a pointer value" use-case appears (would map to `UInt64Property`).

-----

## 15. Test plan

**Unit (DLL test project):**
- `ComputeHoles` (interval-list form): correct complement within `[windowStart, windowEnd)`;
  leading-gap preserved (regression for commit `75ea723`); fully-covered class → empty
  holes; merge of adjacent/overlapping intervals.
- `ArrayDim`: a class with a `Type Foo[N]` `UPROPERTY` produces a single occupied interval
  of `ElementSize*N` (no phantom hole).
- **Type normalization round-trip:** every type `GuessGapTypes` can emit → normalized →
  resolves in **both** `SnapshotNumeric.TryFromHex` and
  `Radar::TryDataTypeFromPropertyTypeName`. (Guards the §4.3 defect.)
- `Orden::MatchGroup` with mixed reflected + raw leaves → SDR still finds distinct-address
  assignments; raw-only leaves don't break matching; per-object raw cap honored.
- Synthetic `FieldDescriptor` refine: re-reads `obj+offset`, re-resolves width from the
  canonical `fieldType`, compares identically to an equivalent reflected hit.
- `CaptureSnapshotChunk` deadline fires (new guard).

**C# unit:**
- `BannerText` toggles OFF/ON; disclosure substring always present; **both** banner tests
  pass after the `{Binding}` swap.
- `req["native_c"]` / `native_align` attached only when set; full `-Mode Publish` clean
  (no IL2026/IL3050).
- `ValueCandidate.IsNativeField` / `GuessedType` parse from JSON; `Origin` column renders.
- `SnapshotNumeric.TryFromHex` succeeds on every normalized raw `declared_type`.

**In-game verification:**
- A "Native-C" game where HP/MP are non-`UPROPERTY`: native single-scan (class-scoped) hits
  must coincide with CE's raw "Find Value" on the same object range.
- TQ2 / SEED (existing group-scan verification games): enabling native-C must **not**
  regress reflected results (identical candidates with flag OFF); adds raw hits with flag ON.
- Snapshot round-trip: capture `native_c` on two snapshots → SPC query on a `<raw@0x..>`
  field joins by offset; Class Pivot on a normalized raw key groups + renders a decoded
  number (not hex).
- Refine convergence: native-C first scan (noisy) → Next Scan Unchanged/Changed → candidate
  count collapses, target survives.
- Large character class (`PropertiesSize > 4 KB`): confirm native members past +4 KB are
  reached (validates the §2.3 cap removal).

-----

## 16. Extension-point summary (for future reuse)

| Seam | Reused by Native-C | Still open for |
|---|---|---|
| `Orden::MatchGroup` (source-agnostic `Leaf`) | raw-hole leaves (§5.2) | Snapshot/SPC group matching (group-value-scan-spec §3.1) |
| `Ubel::GuessGapTypes` (now public) | snapshot pre-guess + single/group labeling | LiveWalker (existing caller) |
| `Ubel::ComputeHoles` (interval-list) | hole computation (§3) | any future "reflected-byte re-interpretation" mode |
| Deep / Cross-object opt-in plumbing | the `native_c` flag pattern (§7) | further per-object leaf producers |
